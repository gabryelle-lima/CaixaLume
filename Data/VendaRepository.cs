using MercadinhoPdv.Models;
using MercadinhoPdv.Services;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace MercadinhoPdv.Data;

internal sealed class VendaRepository
{
    public long Registrar(List<ItemCarrinho> itens, string formaPagamento, decimal? valorRecebidoInformado, AjusteVenda? ajuste, string? operacaoId, decimal? totalEsperado)
    {
        if (operacaoId is not null)
        {
            if (!Guid.TryParse(operacaoId, out var operation)) throw new InvalidOperationException("Identificador da venda inválido.");
            operacaoId = operation.ToString("D");
        }
        ajuste ??= new();
        string assinatura = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Itens = itens.OrderBy(x => x.ProdutoId).ThenBy(x => x.Quantidade).Select(x => new { x.ProdutoId, Quantidade=x.Quantidade.ToString("G29",System.Globalization.CultureInfo.InvariantCulture) }),
            formaPagamento, Recebido=valorRecebidoInformado?.ToString("G29",System.Globalization.CultureInfo.InvariantCulture),
            Tipo=ajuste.TipoDesconto?.Trim().ToUpperInvariant(), Desconto=ajuste.Desconto.ToString("G29",System.Globalization.CultureInfo.InvariantCulture), Acrescimo=ajuste.Acrescimo.ToString("G29",System.Globalization.CultureInfo.InvariantCulture),
            Esperado=totalEsperado?.ToString("G29",System.Globalization.CultureInfo.InvariantCulture)
        }))));
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        
        db.Execute("BEGIN IMMEDIATE;");
        string stage = "validar sessão";
        try
        {
            int usuarioId = Models.SessaoUsuario.UsuarioId ?? throw new InvalidOperationException("Nenhum usuário está autenticado.");
            var user = db.Query("SELECT Id, Perfil, Ativo FROM Usuarios WHERE Id=@Id;", new Dictionary<string, object?> { ["@Id"] = usuarioId }).FirstOrDefault()
                ?? throw new InvalidOperationException("Usuário da sessão não encontrado.");
            if (Convert.ToInt64(user["Ativo"]) != 1) throw new InvalidOperationException("Usuário não está ativo.");
            if (operacaoId is not null)
            {
                var previous = db.Query("SELECT Id,UsuarioId,Status,AssinaturaOperacao FROM Vendas WHERE OperacaoId=@Operacao;", new Dictionary<string, object?> { ["@Operacao"] = operacaoId }).FirstOrDefault();
                if (previous is not null)
                {
                    if (Convert.ToInt32(previous["UsuarioId"]) != usuarioId || Convert.ToString(previous["AssinaturaOperacao"]) != assinatura)
                        throw new InvalidOperationException("Esta operação já foi registrada com outros dados. Consulte o histórico antes de iniciar outra venda.");
                    if (Convert.ToString(previous["Status"]) != "Concluida") throw new InvalidOperationException("Esta operação já foi cancelada. Consulte o histórico.");
                    db.Execute("COMMIT;");
                    return Convert.ToInt64(previous["Id"]);
                }
            }
            stage = "validar caixa aberto";
            var openCash = db.Query("SELECT Id, Status FROM Caixa WHERE Status='Aberto' ORDER BY Id DESC LIMIT 1;").FirstOrDefault();
            if (openCash is null) throw new InvalidOperationException("Abra o caixa antes de realizar uma venda.");
            long caixaId = Convert.ToInt64(openCash["Id"], System.Globalization.CultureInfo.InvariantCulture);
            string[] paymentTypes = ["DINHEIRO", "PIX", "DÉBITO", "CRÉDITO"];
            if (!paymentTypes.Contains(formaPagamento, StringComparer.Ordinal)) throw new InvalidOperationException("Forma de pagamento inválida.");
            TotaisVenda totais = AjustesVendaService.Calcular(itens.Sum(item => item.Subtotal), ajuste, AjustesVendaService.LerPolitica(db), Convert.ToString(user["Perfil"]) == "ADMINISTRADOR");
            decimal total = totais.Total;
            if (totalEsperado.HasValue && totalEsperado.Value != total)
                throw new InvalidOperationException("O preço ou a política da venda mudou. Atualize o carrinho e confira o total antes de confirmar.");
            decimal valorRecebido = formaPagamento == "DINHEIRO" ? valorRecebidoInformado ?? total : total;
            if (formaPagamento == "DINHEIRO" && valorRecebido < total) throw new InvalidOperationException("O valor recebido precisa cobrir o total da venda.");
            stage = "validar produtos e estoque";
            var currentCosts = new Dictionary<int, decimal>();
            foreach (ItemCarrinho item in itens)
            {
                if (item.Quantidade <= 0) throw new InvalidOperationException($"Quantidade inválida para {item.Produto}.");
                var product = db.Query("SELECT Id, Nome, PrecoVenda, PrecoCusto, UnidadeMedida, EstoqueAtual, Ativo FROM Produtos WHERE Id = @Id;", new Dictionary<string, object?> { ["@Id"] = item.ProdutoId }).FirstOrDefault();
                if (product is null || Convert.ToInt64(product["Ativo"]) != 1) throw new InvalidOperationException($"O produto {item.Produto} está indisponível.");
                string unit = Convert.ToString(product["UnidadeMedida"]) ?? "UN";
                if (unit == "UN" && item.Quantidade % 1 != 0) throw new InvalidOperationException($"{item.Produto} é vendido por unidade inteira.");
                if (unit == "KG" && item.Quantidade * 1000 % 1 != 0) throw new InvalidOperationException($"{item.Produto} aceita no máximo três casas decimais por KG.");
                decimal currentPrice = Convert.ToDecimal(product["PrecoVenda"], System.Globalization.CultureInfo.InvariantCulture);
                currentCosts[item.ProdutoId] = Convert.ToDecimal(product["PrecoCusto"], System.Globalization.CultureInfo.InvariantCulture);
                if (unit != item.UnidadeMedida || currentPrice != item.PrecoUnitario)
                    throw new InvalidOperationException($"Preço ou unidade de {item.Produto} foi alterado durante a venda. Remova o item e adicione novamente para atualizar o carrinho.");
                decimal stock = Convert.ToDecimal(product["EstoqueAtual"], System.Globalization.CultureInfo.InvariantCulture);
                if (stock < item.Quantidade) throw new InvalidOperationException($"Estoque insuficiente para {item.Produto}. Disponível: {stock:0.###} {unit}.");
            }
            stage = "inserir venda";
            decimal change = formaPagamento == "DINHEIRO" ? valorRecebido - total : 0;
            db.Execute("INSERT INTO Vendas (DataVenda,ValorTotal,FormaPagamento,Status,CaixaId,UsuarioId,ValorRecebido,Troco,Subtotal,ValorDesconto,ValorAcrescimo,TipoDesconto,DescontoInformado,OperacaoId,AssinaturaOperacao) VALUES (@Data,@Total,@Pagamento,'Concluida',@Caixa,@Usuario,@Recebido,@Troco,@Subtotal,@Desconto,@Acrescimo,@Tipo,@Informado,@Operacao,@Assinatura);",
                new Dictionary<string, object?> { ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Total"] = total, ["@Pagamento"] = formaPagamento, ["@Caixa"] = caixaId, ["@Usuario"] = usuarioId, ["@Recebido"] = valorRecebido, ["@Troco"] = change, ["@Subtotal"] = totais.Subtotal, ["@Desconto"] = totais.Desconto, ["@Acrescimo"] = totais.Acrescimo, ["@Tipo"] = (ajuste.TipoDesconto ?? "VALOR").Trim().ToUpperInvariant(), ["@Informado"] = ajuste.Desconto, ["@Operacao"] = operacaoId, ["@Assinatura"] = assinatura });
            long vendaId = db.LastInsertRowId;
            if (vendaId <= 0) throw new InvalidOperationException("O banco não retornou o identificador da venda.");

            foreach (ItemCarrinho item in itens)
            {
                stage = $"baixar estoque do produto {item.Produto} (#{item.ProdutoId})";
                db.Execute("UPDATE Produtos SET EstoqueAtual = EstoqueAtual - @Quantidade WHERE Id = @Id AND Ativo = 1 AND EstoqueAtual >= @Quantidade;",
                    new Dictionary<string, object?> { ["@Quantidade"] = item.Quantidade, ["@Id"] = item.ProdutoId });
                if (db.Changes != 1) throw new InvalidOperationException($"Estoque insuficiente para {item.Produto}.");

                stage = $"inserir item da venda #{vendaId}";
                db.Execute("INSERT INTO ItensVenda (VendaId, ProdutoId, Quantidade, UnidadeMedida, ValorUnitario, PrecoCustoUnitario, Subtotal) VALUES (@Venda, @Produto, @Quantidade, @Unidade, @Preco, @Custo, @Subtotal);",
                    new Dictionary<string, object?> { ["@Venda"] = vendaId, ["@Produto"] = item.ProdutoId, ["@Quantidade"] = item.Quantidade, ["@Unidade"] = item.UnidadeMedida, ["@Preco"] = decimal.Round(item.PrecoUnitario, 2, MidpointRounding.AwayFromZero), ["@Custo"] = currentCosts[item.ProdutoId], ["@Subtotal"] = item.Subtotal });
                if (db.Changes != 1) throw new InvalidOperationException($"Não foi possível registrar o item {item.Produto}.");
                stage = $"registrar movimentação de estoque do produto {item.Produto}";
                db.Execute("INSERT INTO MovimentacoesEstoque (ProdutoId, Tipo, Quantidade, UnidadeMedida, DataMovimentacao, Motivo, VendaId, UsuarioId) VALUES (@Produto, 'VENDA', @Quantidade, @Unidade, @Data, 'Venda', @Venda, @Usuario);",
                    new Dictionary<string, object?> { ["@Produto"] = item.ProdutoId, ["@Quantidade"] = item.Quantidade, ["@Unidade"] = item.UnidadeMedida, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Venda"] = vendaId, ["@Usuario"] = usuarioId });
                if (db.Changes != 1) throw new InvalidOperationException($"Não foi possível registrar a baixa de estoque de {item.Produto}.");
            }

            stage = "registrar movimentação financeira";
            string movementType = formaPagamento switch { "DINHEIRO" => "VENDA_DINHEIRO", "PIX" => "VENDA_PIX", "DÉBITO" => "VENDA_DEBITO", _ => "VENDA_CREDITO" };
            db.Execute("INSERT INTO MovimentacoesCaixa (CaixaId, Tipo, Valor, DataMovimentacao, Observacao, UsuarioId, VendaId) VALUES (@Caixa,@Tipo,@Valor,@Data,@Obs,@Usuario,@Venda);",
                new Dictionary<string, object?> { ["@Caixa"] = caixaId, ["@Tipo"] = movementType, ["@Valor"] = total, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Obs"] = $"VENDA #{vendaId}", ["@Usuario"] = usuarioId, ["@Venda"] = vendaId });
            if (db.Changes != 1) throw new InvalidOperationException("Não foi possível registrar a movimentação financeira da venda.");
            stage = "registrar auditoria";
            db.Execute("INSERT INTO Auditoria (UsuarioId, DataHora, Acao, Entidade, Descricao) VALUES (@Usuario, @Data, 'VENDA', 'VENDA', @Descricao);",
                new Dictionary<string, object?> { ["@Usuario"] = usuarioId, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Descricao"] = $"Venda #{vendaId}; total {total}; forma {formaPagamento}; caixa #{caixaId}" });
            if (db.Changes != 1) throw new InvalidOperationException("Não foi possível registrar a auditoria da venda.");

            stage = "confirmar transação";
            db.Execute("COMMIT;");
            return vendaId;
        }
        catch (Exception exception)
        {
            exception.Data["VendaEtapa"] = stage;
            try { db.Execute("ROLLBACK;"); }
            catch (Exception rollbackException) { exception.Data["VendaRollback"] = rollbackException.ToString(); }
            throw;
        }
    }

    public List<VendaHistoricoLinha> ListarHistorico(DateTime inicio, DateTime fim, string? status)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        return db.Query("SELECT v.Id,v.DataVenda AS Data,COALESCE(u.Nome,'—') AS Operador,COALESCE(v.FormaPagamento,'—') AS Pagamento,v.ValorTotal AS Total,v.Subtotal,v.ValorDesconto AS Desconto,v.ValorAcrescimo AS Acrescimo,v.Status FROM Vendas v LEFT JOIN Usuarios u ON u.Id=v.UsuarioId WHERE v.DataVenda>=@Inicio AND v.DataVenda<@Fim AND (@Status='' OR v.Status=@Status) ORDER BY v.Id DESC LIMIT 2000;",
            new Dictionary<string, object?> { ["@Inicio"] = inicio.Date.ToString("yyyy-MM-dd"), ["@Fim"] = fim.Date.AddDays(1).ToString("yyyy-MM-dd"), ["@Status"] = status ?? "" })
            .Select(r => new VendaHistoricoLinha { Id=Convert.ToInt64(r["Id"]), Data=DateTime.Parse(Convert.ToString(r["Data"])!, System.Globalization.CultureInfo.InvariantCulture), Operador=Convert.ToString(r["Operador"])!, Pagamento=Convert.ToString(r["Pagamento"])!, Total=Convert.ToDecimal(r["Total"], System.Globalization.CultureInfo.InvariantCulture), Subtotal=Convert.ToDecimal(r["Subtotal"], System.Globalization.CultureInfo.InvariantCulture), Desconto=Convert.ToDecimal(r["Desconto"], System.Globalization.CultureInfo.InvariantCulture), Acrescimo=Convert.ToDecimal(r["Acrescimo"], System.Globalization.CultureInfo.InvariantCulture), Status=Convert.ToString(r["Status"])! }).ToList();
    }

    public VendaOperacao? ObterPorOperacao(string operacaoId, int usuarioId)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var row = db.Query("SELECT Id,Status,ValorTotal,FormaPagamento,ValorRecebido,Troco FROM Vendas WHERE OperacaoId=@Operacao AND UsuarioId=@Usuario LIMIT 1;",
            new Dictionary<string, object?> { ["@Operacao"] = operacaoId, ["@Usuario"] = usuarioId }).FirstOrDefault();
        if (row is null) return null;
        decimal Value(string name) => Convert.ToDecimal(row[name], System.Globalization.CultureInfo.InvariantCulture);
        return new VendaOperacao(Convert.ToInt64(row["Id"], System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToString(row["Status"]) ?? "", Value("ValorTotal"), Convert.ToString(row["FormaPagamento"]) ?? "",
            Value("ValorRecebido"), Value("Troco"));
    }

    public TotaisVenda ObterTotais(long vendaId)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var row = db.Query("SELECT Subtotal,ValorDesconto,ValorAcrescimo,ValorTotal FROM Vendas WHERE Id=@Id;", new Dictionary<string, object?> { ["@Id"] = vendaId }).SingleOrDefault() ?? throw new InvalidOperationException("Venda não encontrada.");
        decimal Value(string name) => Convert.ToDecimal(row[name], System.Globalization.CultureInfo.InvariantCulture);
        return new(Value("Subtotal"), Value("ValorDesconto"), Value("ValorAcrescimo"), Value("ValorTotal"));
    }

    public List<VendaItemDetalhe> Detalhes(long vendaId)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        return db.Query("SELECT p.Nome AS Produto, i.Quantidade, i.UnidadeMedida AS Unidade, i.ValorUnitario AS PrecoUnitario, i.Subtotal FROM ItensVenda i JOIN Produtos p ON p.Id=i.ProdutoId WHERE i.VendaId=@Venda ORDER BY i.Id;", new Dictionary<string, object?> { ["@Venda"] = vendaId })
            .Select(r => new VendaItemDetalhe { Produto=Convert.ToString(r["Produto"])!, Quantidade=Convert.ToDecimal(r["Quantidade"], System.Globalization.CultureInfo.InvariantCulture), Unidade=Convert.ToString(r["Unidade"])!, PrecoUnitario=Convert.ToDecimal(r["PrecoUnitario"], System.Globalization.CultureInfo.InvariantCulture), Subtotal=Convert.ToDecimal(r["Subtotal"], System.Globalization.CultureInfo.InvariantCulture) }).ToList();
    }

    public void Cancelar(long vendaId, string motivo)
    {
        Services.Autorizacao.ExigirAdministrador();
        if (string.IsNullOrWhiteSpace(motivo)) throw new InvalidOperationException("Informe o motivo do cancelamento.");
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);  db.Execute("BEGIN IMMEDIATE;");
        try
        {
            var sale = db.Query("SELECT CaixaId, Status, ValorTotal, FormaPagamento FROM Vendas WHERE Id=@Id;", new Dictionary<string, object?> { ["@Id"] = vendaId }).FirstOrDefault() ?? throw new InvalidOperationException("Venda não encontrada.");
            if (Convert.ToString(sale["Status"]) != "Concluida") throw new InvalidOperationException("Somente vendas concluídas podem ser canceladas.");
            var cash = db.Query("SELECT Status FROM Caixa WHERE Id=@Id;", new Dictionary<string, object?> { ["@Id"] = sale["CaixaId"] }).FirstOrDefault();
            if (cash is null || Convert.ToString(cash["Status"]) != "Aberto") throw new InvalidOperationException("Para manter a conferência do caixa, cancele a venda antes do fechamento do caixa correspondente.");
            var items = db.Query("SELECT ProdutoId, Quantidade, UnidadeMedida FROM ItensVenda WHERE VendaId=@Venda;", new Dictionary<string, object?> { ["@Venda"] = vendaId });
            foreach (var item in items)
            {
                int productId=Convert.ToInt32(item["ProdutoId"]); decimal quantity=Convert.ToDecimal(item["Quantidade"], System.Globalization.CultureInfo.InvariantCulture); string unit=Convert.ToString(item["UnidadeMedida"]) ?? "UN";
                db.Execute("UPDATE Produtos SET EstoqueAtual=EstoqueAtual+@Quantidade WHERE Id=@Produto;", new Dictionary<string, object?> { ["@Quantidade"] = quantity, ["@Produto"] = productId });
                db.Execute("INSERT INTO MovimentacoesEstoque (ProdutoId, Tipo, Quantidade, UnidadeMedida, DataMovimentacao, Motivo, VendaId, UsuarioId) VALUES (@Produto,'ESTORNO',@Quantidade,@Unidade,@Data,@Motivo,@Venda,@Usuario);", new Dictionary<string, object?> { ["@Produto"] = productId, ["@Quantidade"] = quantity, ["@Unidade"] = unit, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Motivo"] = $"CANCELAMENTO DA VENDA #{vendaId}", ["@Venda"] = vendaId, ["@Usuario"] = SessaoUsuario.UsuarioId });
            }
            db.Execute("UPDATE Vendas SET Status='Cancelada', CanceladaEm=@Data, CanceladaPorId=@Usuario, MotivoCancelamento=@Motivo WHERE Id=@Id AND Status='Concluida';", new Dictionary<string, object?> { ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Usuario"] = SessaoUsuario.UsuarioId, ["@Motivo"] = motivo.Trim().ToUpperInvariant(), ["@Id"] = vendaId });
            string paymentSuffix = Convert.ToString(sale["FormaPagamento"]) switch { "DINHEIRO" => "DINHEIRO", "PIX" => "PIX", "DÉBITO" => "DEBITO", _ => "CREDITO" };
            db.Execute("INSERT INTO MovimentacoesCaixa (CaixaId,Tipo,Valor,DataMovimentacao,Observacao,UsuarioId,VendaId) VALUES (@Caixa,@Tipo,@Valor,@Data,@Obs,@Usuario,@Venda);", new Dictionary<string, object?> { ["@Caixa"] = sale["CaixaId"], ["@Tipo"] = $"ESTORNO_VENDA_{paymentSuffix}", ["@Valor"] = sale["ValorTotal"], ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Obs"] = $"ESTORNO VENDA #{vendaId}", ["@Usuario"] = SessaoUsuario.UsuarioId, ["@Venda"] = vendaId });
            db.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,'CANCELAMENTO','VENDA',@Descricao);", new Dictionary<string, object?> { ["@Usuario"] = SessaoUsuario.UsuarioId, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Descricao"] = $"Venda #{vendaId} cancelada. Motivo: {motivo.Trim().ToUpperInvariant()}" });
            db.Execute("COMMIT;");
        }
        catch { db.Execute("ROLLBACK;"); throw; }
    }
}
