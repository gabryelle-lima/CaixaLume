using MercadinhoPdv.Data;
using MercadinhoPdv.Models;

namespace MercadinhoPdv.Services;

public sealed class EstoqueService
{
    private readonly ProdutoService _products = new();

    public List<Produto> Listar(string? pesquisa = null) => _products.Listar(pesquisa);

    public void Ajustar(int produtoId, decimal quantidade, bool entrada, string motivo)
        => RegistrarMovimentacao(produtoId, entrada ? "Entrada" : "Saída", quantidade, motivo);

    public void RegistrarMovimentacao(int produtoId, string tipo, decimal quantidade, string motivo)
    {
        Autorizacao.ExigirAdministrador();
        if (quantidade <= 0) throw new InvalidOperationException("Informe uma quantidade maior que zero.");
        if (string.IsNullOrWhiteSpace(motivo)) throw new InvalidOperationException("Informe o motivo da movimentação.");
        if (tipo is not ("Entrada" or "Saída")) throw new InvalidOperationException("Tipo de movimentação inválido.");
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        
        db.Execute("BEGIN IMMEDIATE;");
        try
        {
            var product = db.Query("SELECT UnidadeMedida FROM Produtos WHERE Id = @Id AND Ativo = 1;", new Dictionary<string, object?> { ["@Id"] = produtoId }).FirstOrDefault();
            if (product is null) throw new InvalidOperationException("Produto não encontrado ou inativo.");
            string unit = Convert.ToString(product["UnidadeMedida"]) ?? "UN";
            if (unit == "UN" && quantidade % 1 != 0) throw new InvalidOperationException("Produtos vendidos por unidade precisam de uma quantidade inteira.");
            if (unit == "KG" && quantidade * 1000 % 1 != 0) throw new InvalidOperationException("Produtos vendidos por KG aceitam até três casas decimais.");
            bool entrada = tipo == "Entrada";
            string sinal = entrada ? "+" : "-";
            db.Execute($"UPDATE Produtos SET EstoqueAtual = EstoqueAtual {sinal} @Quantidade WHERE Id = @Id AND Ativo = 1 AND (@Entrada = 1 OR EstoqueAtual >= @Quantidade);",
                new Dictionary<string, object?> { ["@Quantidade"] = quantidade, ["@Id"] = produtoId, ["@Entrada"] = entrada });
            if (db.Changes != 1) throw new InvalidOperationException("Produto não encontrado ou estoque insuficiente para essa saída.");
            db.Execute("INSERT INTO MovimentacoesEstoque (ProdutoId, Tipo, Quantidade, UnidadeMedida, DataMovimentacao, Motivo, UsuarioId) VALUES (@Produto, @Tipo, @Quantidade, @Unidade, @Data, @Motivo, @Usuario);",
                new Dictionary<string, object?> { ["@Produto"] = produtoId, ["@Tipo"] = entrada ? "Entrada" : "Saida", ["@Quantidade"] = quantidade, ["@Unidade"] = unit, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Motivo"] = motivo.Trim().ToUpperInvariant(), ["@Usuario"] = SessaoUsuario.UsuarioId });
            db.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,@Acao,'ESTOQUE',@Descricao);", new Dictionary<string, object?> { ["@Usuario"] = SessaoUsuario.UsuarioId, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Acao"] = entrada ? "ENTRADA" : "SAIDA", ["@Descricao"] = $"Produto #{produtoId}; {quantidade:0.###} {unit}; {motivo.Trim().ToUpperInvariant()}" });
            db.Execute("COMMIT;");
        }
        catch { db.Execute("ROLLBACK;"); throw; }
    }

    public void AjustarSaldo(int produtoId, decimal novoSaldo, string motivo)
    {
        int usuario = Autorizacao.ExigirUsuario(administrador: true);
        if (novoSaldo < 0) throw new InvalidOperationException("O saldo não pode ser negativo.");
        if (string.IsNullOrWhiteSpace(motivo)) throw new InvalidOperationException("Informe o motivo do ajuste.");
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        
        db.Execute("BEGIN IMMEDIATE;");
        try
        {
            var product = db.Query("SELECT UnidadeMedida, EstoqueAtual FROM Produtos WHERE Id = @Id AND Ativo = 1;", new Dictionary<string, object?> { ["@Id"] = produtoId }).FirstOrDefault();
            if (product is null) throw new InvalidOperationException("Produto não encontrado ou inativo.");
            string unit = Convert.ToString(product["UnidadeMedida"]) ?? "UN";
            decimal previous = Convert.ToDecimal(product["EstoqueAtual"], System.Globalization.CultureInfo.InvariantCulture);
            if (unit == "UN" && novoSaldo % 1 != 0) throw new InvalidOperationException("Produtos vendidos por unidade precisam de um saldo inteiro.");
            if (unit == "KG" && novoSaldo * 1000 % 1 != 0) throw new InvalidOperationException("Produtos vendidos por KG aceitam até três casas decimais no saldo.");
            db.Execute("UPDATE Produtos SET EstoqueAtual = @Saldo WHERE Id = @Id;", new Dictionary<string, object?> { ["@Saldo"] = novoSaldo, ["@Id"] = produtoId });
            decimal difference = novoSaldo - previous;
            db.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,'AJUSTE','ESTOQUE',@Descricao);",
                new Dictionary<string, object?> { ["@Usuario"] = usuario, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Descricao"] = FormattableString.Invariant($"Produto #{produtoId}; anterior {previous} {unit}; posterior {novoSaldo} {unit}; diferença {difference}; motivo {motivo.Trim().ToUpperInvariant()}") });
            if (difference != 0) db.Execute("INSERT INTO MovimentacoesEstoque (ProdutoId, Tipo, Quantidade, UnidadeMedida, DataMovimentacao, Motivo, UsuarioId) VALUES (@Produto, 'Ajuste', @Quantidade, @Unidade, @Data, @Motivo, @Usuario);",
                new Dictionary<string, object?> { ["@Produto"] = produtoId, ["@Quantidade"] = Math.Abs(difference), ["@Unidade"] = unit, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Motivo"] = motivo.Trim().ToUpperInvariant(), ["@Usuario"] = SessaoUsuario.UsuarioId });
            db.Execute("COMMIT;");
        }
        catch { db.Execute("ROLLBACK;"); throw; }
    }

    public void RegistrarEntradaMercadoria(int produtoId, decimal quantidade, decimal precoCusto, decimal? precoVenda, DateTime? validade, int? fornecedorId, string? documento, string observacao)
    {
        Autorizacao.ExigirAdministrador();
        if (quantidade <= 0 || precoCusto < 0 || (precoVenda.HasValue && precoVenda.Value < 0)) throw new InvalidOperationException("Confira quantidade e preços.");
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);  db.Execute("BEGIN IMMEDIATE;");
        try
        {
            var product = db.Query("SELECT UnidadeMedida FROM Produtos WHERE Id=@Id AND Ativo=1;", new Dictionary<string, object?> { ["@Id"] = produtoId }).FirstOrDefault() ?? throw new InvalidOperationException("Produto não encontrado ou inativo.");
            string unit = Convert.ToString(product["UnidadeMedida"]) ?? "UN";
            if (unit == "UN" && quantidade % 1 != 0) throw new InvalidOperationException("A quantidade de produto UN deve ser inteira.");
            if (unit == "KG" && quantidade * 1000 % 1 != 0) throw new InvalidOperationException("A quantidade aceita no máximo três casas decimais.");
            db.Execute("UPDATE Produtos SET EstoqueAtual=EstoqueAtual+@Qtd,PrecoCusto=@Custo,PrecoVenda=COALESCE(@Venda,PrecoVenda),FornecedorId=COALESCE(@Fornecedor,FornecedorId),DataValidade=COALESCE(@Validade,DataValidade) WHERE Id=@Id;", new Dictionary<string, object?> { ["@Qtd"] = quantidade, ["@Custo"] = precoCusto, ["@Venda"] = precoVenda, ["@Fornecedor"] = fornecedorId, ["@Validade"] = validade?.ToString("yyyy-MM-dd"), ["@Id"] = produtoId });
            string reason = string.IsNullOrWhiteSpace(observacao) ? "ENTRADA DE MERCADORIA" : $"ENTRADA DE MERCADORIA · {observacao.Trim().ToUpperInvariant()}";
            db.Execute("INSERT INTO MovimentacoesEstoque (ProdutoId,Tipo,Quantidade,UnidadeMedida,DataMovimentacao,Motivo,UsuarioId,FornecedorId,Documento) VALUES (@Produto,'Entrada',@Qtd,@Unidade,@Data,@Motivo,@Usuario,@Fornecedor,@Documento);", new Dictionary<string, object?> { ["@Produto"] = produtoId, ["@Qtd"] = quantidade, ["@Unidade"] = unit, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Motivo"] = reason, ["@Usuario"] = SessaoUsuario.UsuarioId, ["@Fornecedor"] = fornecedorId, ["@Documento"] = string.IsNullOrWhiteSpace(documento) ? null : documento.Trim() });
            db.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,'ENTRADA','ESTOQUE',@Descricao);", new Dictionary<string, object?> { ["@Usuario"] = SessaoUsuario.UsuarioId, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Descricao"] = $"Entrada de {quantidade:0.###} {unit} do produto #{produtoId}; documento {documento?.Trim()}" });
            db.Execute("COMMIT;");
        }
        catch { db.Execute("ROLLBACK;"); throw; }
    }

    public List<MovimentoEstoqueLinha> ListarMovimentacoes(int? produtoId = null, DateTime? inicio = null, DateTime? fim = null, string? tipo = null, string? pesquisaProduto = null)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        string? start = inicio?.Date.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        string? end = fim?.Date.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        string? normalizedType = tipo?.Trim().ToUpperInvariant() switch { "ENTRADA" => "Entrada", "SAÍDA" or "SAIDA" => "Saida", "AJUSTE" => "Ajuste", "VENDA" => "VENDA", "ESTORNO" => "ESTORNO", _ => null };
        return db.Query("SELECT m.DataMovimentacao AS Data, p.Nome AS Produto,p.CodigoBarras,m.UnidadeMedida, CASE m.Tipo WHEN 'Entrada' THEN 'ENTRADA' WHEN 'Saida' THEN 'SAÍDA' ELSE UPPER(m.Tipo) END AS Tipo, m.Quantidade, m.Motivo, u.Nome AS Usuario,m.Documento FROM MovimentacoesEstoque m JOIN Produtos p ON p.Id = m.ProdutoId LEFT JOIN Usuarios u ON u.Id = m.UsuarioId WHERE (@Produto IS NULL OR m.ProdutoId = @Produto) AND (@Inicio IS NULL OR m.DataMovimentacao >= @Inicio) AND (@Fim IS NULL OR m.DataMovimentacao < @Fim) AND (@Tipo IS NULL OR m.Tipo = @Tipo) AND (@Busca = '' OR p.Nome LIKE @Termo OR p.CodigoBarras LIKE @Termo) ORDER BY m.Id DESC LIMIT 1000;", new Dictionary<string, object?> { ["@Produto"] = produtoId, ["@Inicio"] = start, ["@Fim"] = end, ["@Tipo"] = normalizedType, ["@Busca"] = pesquisaProduto?.Trim() ?? "", ["@Termo"] = $"%{pesquisaProduto?.Trim()}%" })
            .Select(row => new MovimentoEstoqueLinha { Data = DateTime.Parse(Convert.ToString(row["Data"]) ?? "", System.Globalization.CultureInfo.InvariantCulture), Produto = Convert.ToString(row["Produto"]) ?? "", CodigoBarras=Convert.ToString(row["CodigoBarras"]), UnidadeMedida = Convert.ToString(row["UnidadeMedida"]) ?? "UN", Tipo = Convert.ToString(row["Tipo"]) ?? "", Quantidade = Convert.ToDecimal(row["Quantidade"], System.Globalization.CultureInfo.InvariantCulture), Motivo = Convert.ToString(row["Motivo"]), Usuario = Convert.ToString(row["Usuario"]), Documento=Convert.ToString(row["Documento"]) }).ToList();
    }
}
