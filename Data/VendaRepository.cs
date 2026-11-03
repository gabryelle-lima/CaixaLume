using CaixaLume.Models;
using CaixaLume.Services;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace CaixaLume.Data;

internal sealed class VendaRepository
{
    public long Registrar(List<ItemCarrinho> itens, string formaPagamento, decimal? valorRecebidoInformado, AjusteVenda? ajuste, string? operacaoId, decimal? totalEsperado, IReadOnlyList<PagamentoVenda>? pagamentos = null, bool consultarCatalogo = false)
    {
        operacaoId ??= Guid.NewGuid().ToString("D");
        if (pagamentos is not null && pagamentos.Any(p => p is null || string.IsNullOrWhiteSpace(p.Forma)))
            throw new InvalidOperationException("Dados de pagamento inválidos.");
        if (operacaoId is not null)
        {
            if (!Guid.TryParse(operacaoId, out var operation) || operation == Guid.Empty) throw new InvalidOperationException("Identificador da venda inválido.");
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
        if (pagamentos is not null)
            assinatura = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(assinatura + JsonSerializer.Serialize(pagamentos.OrderBy(p => p.Forma.Trim().ToUpperInvariant(), StringComparer.Ordinal).Select(p => new { Forma=p.Forma.Trim().ToUpperInvariant(), Valor=p.Valor.ToString("G29",System.Globalization.CultureInfo.InvariantCulture) })))));
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
            string[] paymentTypes = ["DINHEIRO", "PIX", "DÉBITO", "CRÉDITO", "MISTO"];
            if (!paymentTypes.Contains(formaPagamento, StringComparer.Ordinal)) throw new InvalidOperationException("Forma de pagamento inválida.");
            // Consolida repetições antes de arredondar, impedindo que fracionar uma linha altere o preço.
            if (itens.GroupBy(i => i.ProdutoId).Any(g => g.Select(i => (i.PrecoUnitario,i.UnidadeMedida)).Distinct().Count() > 1))
                throw new InvalidOperationException("Itens repetidos com preço ou unidade divergentes.");
            itens = itens.GroupBy(i => i.ProdutoId).Select(g => new ItemCarrinho
            {
                ProdutoId = g.Key, Produto = g.First().Produto, PrecoUnitario = g.First().PrecoUnitario,
                UnidadeMedida = g.First().UnidadeMedida, Quantidade = g.Sum(i => i.Quantidade)
            }).ToList();
            if (itens.Any(i => i.Quantidade > 100_000)) throw new InvalidOperationException("Quantidade inválida.");
            stage = "validar produtos e estoque";
            var currentCosts = new Dictionary<int, decimal>();
            var quantidades = itens.GroupBy(i => i.ProdutoId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantidade));
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
                if (consultarCatalogo)
                {
                    item.PrecoUnitario = currentPrice;
                    item.UnidadeMedida = unit;
                    item.Produto = Convert.ToString(product["Nome"]) ?? "";
                }
                if (unit != item.UnidadeMedida || currentPrice != item.PrecoUnitario)
                    throw new InvalidOperationException($"Preço ou unidade de {item.Produto} foi alterado durante a venda. Remova o item e adicione novamente para atualizar o carrinho.");
                decimal stock = Convert.ToDecimal(product["EstoqueAtual"], System.Globalization.CultureInfo.InvariantCulture);
                if (stock < quantidades[item.ProdutoId]) throw new InvalidOperationException($"Estoque insuficiente para {item.Produto}. Disponível: {stock:0.###} {unit}.");
            }
            TotaisVenda totais = AjustesVendaService.Calcular(itens.Sum(item => item.Subtotal), ajuste, AjustesVendaService.LerPolitica(db), Convert.ToString(user["Perfil"]) == "ADMINISTRADOR");
            decimal total = totais.Total;
            if (totalEsperado.HasValue && totalEsperado.Value != total)
                throw new InvalidOperationException("O preço ou a política da venda mudou. Atualize o carrinho e confira o total antes de confirmar.");
            var partes = PagamentoService.Distribuir(total, formaPagamento, valorRecebidoInformado, pagamentos);
            decimal valorRecebido = partes.Sum(p => p.RecebidoCentavos) / 100m;
            stage = "inserir venda";
            decimal change = PagamentoService.CalcularTroco(total, valorRecebido);
            db.Execute("INSERT INTO Vendas (DataVenda,ValorTotal,FormaPagamento,Status,CaixaId,UsuarioId,ValorRecebido,Troco,Subtotal,ValorDesconto,ValorAcrescimo,TipoDesconto,DescontoInformado,OperacaoId,AssinaturaOperacao) VALUES (@Data,@Total,@Pagamento,'Concluida',@Caixa,@Usuario,@Recebido,@Troco,@Subtotal,@Desconto,@Acrescimo,@Tipo,@Informado,@Operacao,@Assinatura);",
                new Dictionary<string, object?> { ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Total"] = total, ["@Pagamento"] = formaPagamento, ["@Caixa"] = caixaId, ["@Usuario"] = usuarioId, ["@Recebido"] = valorRecebido, ["@Troco"] = change, ["@Subtotal"] = totais.Subtotal, ["@Desconto"] = totais.Desconto, ["@Acrescimo"] = totais.Acrescimo, ["@Tipo"] = (ajuste.TipoDesconto ?? "VALOR").Trim().ToUpperInvariant(), ["@Informado"] = ajuste.Desconto, ["@Operacao"] = operacaoId, ["@Assinatura"] = assinatura });
            long vendaId = db.LastInsertRowId;
            if (vendaId <= 0) throw new InvalidOperationException("O banco não retornou o identificador da venda.");

            foreach (ItemCarrinho item in itens)
            {
                stage = $"baixar estoque do produto {item.Produto} (#{item.ProdutoId})";
                db.Execute("UPDATE Produtos SET EstoqueAtual = ROUND(EstoqueAtual - @Quantidade,3) WHERE Id = @Id AND Ativo = 1 AND EstoqueAtual >= @Quantidade;",
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
            foreach (var parte in partes)
            {
                db.Execute("INSERT INTO PagamentosVenda (VendaId,Forma,ValorCentavos,RecebidoCentavos) VALUES (@Venda,@Forma,@Valor,@Recebido);",
                    new Dictionary<string, object?> { ["@Venda"] = vendaId, ["@Forma"] = parte.Forma, ["@Valor"] = parte.ValorCentavos, ["@Recebido"] = parte.RecebidoCentavos });
                string movementType = "VENDA_" + (parte.Forma switch { "DÉBITO" => "DEBITO", "CRÉDITO" => "CREDITO", _ => parte.Forma });
                db.Execute("INSERT INTO MovimentacoesCaixa (CaixaId, Tipo, Valor, DataMovimentacao, Observacao, UsuarioId, VendaId) VALUES (@Caixa,@Tipo,@Valor,@Data,@Obs,@Usuario,@Venda);",
                    new Dictionary<string, object?> { ["@Caixa"] = caixaId, ["@Tipo"] = movementType, ["@Valor"] = parte.ValorCentavos / 100m, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Obs"] = $"VENDA #{vendaId}; REGISTRO MANUAL; SEM CONFIRMAÇÃO BANCÁRIA", ["@Usuario"] = usuarioId, ["@Venda"] = vendaId });
                if (db.Changes != 1) throw new InvalidOperationException("Não foi possível registrar a movimentação financeira da venda.");
            }
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
        // Compatibilidade para relatórios/chamadores antigos: nunca oculta registros por um teto arbitrário.
        return ConsultarHistorico(inicio, fim, status, null, null, null).Itens;
    }

    public PaginaVendas ListarHistoricoPaginado(DateTime inicio, DateTime fim, string? status, int pagina, int tamanho, long? limiteId)
        => ConsultarHistorico(inicio, fim, status, pagina, tamanho, limiteId);

    private static PaginaVendas ConsultarHistorico(DateTime inicio, DateTime fim, string? status, int? pagina, int? tamanho, long? limiteId)
    {
        Autorizacao.ExigirUsuario();
        if (fim.Date < inicio.Date || pagina is < 1 || tamanho is < 1 or > 500 || limiteId is < 0)
            throw new InvalidOperationException("Período ou paginação inválidos.");
        if (status is not (null or "" or "Concluida" or "Cancelada")) throw new InvalidOperationException("Status inválido.");
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        db.Execute("BEGIN;");
        try
        {
            long limite = limiteId ?? Convert.ToInt64(db.Query("SELECT COALESCE(MAX(Id),0) FROM Vendas;").Single().Values.Single());
            string where = "v.DataVenda>=@Inicio AND v.DataVenda<@Fim AND v.Id<=@Limite";
            var args = new Dictionary<string,object?> { ["@Inicio"]=inicio.Date.ToString("yyyy-MM-dd"),["@Fim"]=fim.Date.AddDays(1).ToString("yyyy-MM-dd"),["@Limite"]=limite };
            if (!string.IsNullOrEmpty(status)) { where += " AND v.Status=@Status";args["@Status"]=status; }
            long total = Convert.ToInt64(db.Query("SELECT COUNT(*) FROM Vendas v WHERE " + where + ";", args).Single().Values.Single());
            string sql = "SELECT v.Id,v.DataVenda AS Data,COALESCE(u.Nome,'—') AS Operador,COALESCE(v.FormaPagamento,'—') AS Pagamento,v.ValorTotal AS Total,v.Subtotal,v.ValorDesconto AS Desconto,v.ValorAcrescimo AS Acrescimo,v.Status FROM Vendas v LEFT JOIN Usuarios u ON u.Id=v.UsuarioId WHERE " + where + " ORDER BY v.Id DESC";
            if (pagina.HasValue) { sql += " LIMIT @Tamanho OFFSET @Offset";args["@Tamanho"]=tamanho;args["@Offset"]=((long)pagina.Value-1)*tamanho!.Value; }
            var itens = db.Query(sql+";",args).Select(r => new VendaHistoricoLinha { Id=Convert.ToInt64(r["Id"]), Data=DateTime.Parse(Convert.ToString(r["Data"])!, System.Globalization.CultureInfo.InvariantCulture), Operador=Convert.ToString(r["Operador"])!, Pagamento=Convert.ToString(r["Pagamento"])!, Total=Convert.ToDecimal(r["Total"], System.Globalization.CultureInfo.InvariantCulture), Subtotal=Convert.ToDecimal(r["Subtotal"], System.Globalization.CultureInfo.InvariantCulture), Desconto=Convert.ToDecimal(r["Desconto"], System.Globalization.CultureInfo.InvariantCulture), Acrescimo=Convert.ToDecimal(r["Acrescimo"], System.Globalization.CultureInfo.InvariantCulture), Status=Convert.ToString(r["Status"])! }).ToList();
            db.Execute("COMMIT;");
            return new(itens,pagina??1,tamanho??itens.Count,total,limite);
        }
        catch { db.Execute("ROLLBACK;");throw; }
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
            Autorizacao.ExigirUsuario(db, administrador: true);
            var sale = db.Query("SELECT CaixaId, Status, ValorTotal, FormaPagamento FROM Vendas WHERE Id=@Id;", new Dictionary<string, object?> { ["@Id"] = vendaId }).FirstOrDefault() ?? throw new InvalidOperationException("Venda não encontrada.");
            if (Convert.ToString(sale["Status"]) != "Concluida") throw new InvalidOperationException("Somente vendas concluídas podem ser canceladas.");
            var cash = db.Query("SELECT Status FROM Caixa WHERE Id=@Id;", new Dictionary<string, object?> { ["@Id"] = sale["CaixaId"] }).FirstOrDefault();
            if (cash is null || Convert.ToString(cash["Status"]) != "Aberto") throw new InvalidOperationException("Para manter a conferência do caixa, cancele a venda antes do fechamento do caixa correspondente.");
            var movimentos = db.Query("SELECT Tipo,Valor FROM MovimentacoesCaixa WHERE VendaId=@Venda AND Tipo IN ('VENDA_DINHEIRO','VENDA_PIX','VENDA_DEBITO','VENDA_CREDITO');", new Dictionary<string, object?> { ["@Venda"] = vendaId });
            if (movimentos.Count == 0)
            {
                string sufixo = Convert.ToString(sale["FormaPagamento"]) switch { "DINHEIRO" => "DINHEIRO", "PIX" => "PIX", "DÉBITO" => "DEBITO", "CRÉDITO" => "CREDITO", _ => throw new InvalidOperationException("Composição financeira da venda indisponível.") };
                movimentos.Add(new Dictionary<string, object?> { ["Tipo"] = "VENDA_" + sufixo, ["Valor"] = sale["ValorTotal"] });
            }
            if (ValoresMonetarios.Arredondar(movimentos.Sum(m => Convert.ToDecimal(m["Valor"], System.Globalization.CultureInfo.InvariantCulture))) != Convert.ToDecimal(sale["ValorTotal"], System.Globalization.CultureInfo.InvariantCulture))
                throw new InvalidOperationException("As movimentações financeiras divergem do total da venda. Revise o histórico antes de cancelar.");
            decimal estornoDinheiro = movimentos.Where(m => Convert.ToString(m["Tipo"]) == "VENDA_DINHEIRO").Sum(m => Convert.ToDecimal(m["Valor"], System.Globalization.CultureInfo.InvariantCulture));
            if (CaixaService.CriarResumo(db, Convert.ToInt64(sale["CaixaId"])).ValorEsperadoDinheiro < estornoDinheiro)
                throw new InvalidOperationException("O dinheiro disponível é insuficiente para devolver esta venda. Registre um suprimento antes de cancelar.");
            var items = db.Query("SELECT ProdutoId, Quantidade, UnidadeMedida FROM ItensVenda WHERE VendaId=@Venda;", new Dictionary<string, object?> { ["@Venda"] = vendaId });
            foreach (var item in items)
            {
                int productId=Convert.ToInt32(item["ProdutoId"]); decimal quantity=Convert.ToDecimal(item["Quantidade"], System.Globalization.CultureInfo.InvariantCulture); string unit=Convert.ToString(item["UnidadeMedida"]) ?? "UN";
                db.Execute("UPDATE Produtos SET EstoqueAtual=ROUND(EstoqueAtual+@Quantidade,3) WHERE Id=@Produto;", new Dictionary<string, object?> { ["@Quantidade"] = quantity, ["@Produto"] = productId });
                db.Execute("INSERT INTO MovimentacoesEstoque (ProdutoId, Tipo, Quantidade, UnidadeMedida, DataMovimentacao, Motivo, VendaId, UsuarioId) VALUES (@Produto,'ESTORNO',@Quantidade,@Unidade,@Data,@Motivo,@Venda,@Usuario);", new Dictionary<string, object?> { ["@Produto"] = productId, ["@Quantidade"] = quantity, ["@Unidade"] = unit, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Motivo"] = $"CANCELAMENTO DA VENDA #{vendaId}", ["@Venda"] = vendaId, ["@Usuario"] = SessaoUsuario.UsuarioId });
            }
            db.Execute("UPDATE Vendas SET Status='Cancelada', CanceladaEm=@Data, CanceladaPorId=@Usuario, MotivoCancelamento=@Motivo WHERE Id=@Id AND Status='Concluida';", new Dictionary<string, object?> { ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Usuario"] = SessaoUsuario.UsuarioId, ["@Motivo"] = motivo.Trim().ToUpperInvariant(), ["@Id"] = vendaId });
            foreach (var movimento in movimentos)
                db.Execute("INSERT INTO MovimentacoesCaixa (CaixaId,Tipo,Valor,DataMovimentacao,Observacao,UsuarioId,VendaId) VALUES (@Caixa,@Tipo,@Valor,@Data,@Obs,@Usuario,@Venda);", new Dictionary<string, object?> { ["@Caixa"] = sale["CaixaId"], ["@Tipo"] = "ESTORNO_" + Convert.ToString(movimento["Tipo"]), ["@Valor"] = movimento["Valor"], ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Obs"] = $"ESTORNO MANUAL VENDA #{vendaId}; DEVOLUÇÃO EXTERNA NÃO CONFIRMADA", ["@Usuario"] = SessaoUsuario.UsuarioId, ["@Venda"] = vendaId });
            db.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,'CANCELAMENTO','VENDA',@Descricao);", new Dictionary<string, object?> { ["@Usuario"] = SessaoUsuario.UsuarioId, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Descricao"] = $"Venda #{vendaId} cancelada. Motivo: {motivo.Trim().ToUpperInvariant()}" });
            db.Execute("COMMIT;");
        }
        catch { db.Execute("ROLLBACK;"); throw; }
    }
}
