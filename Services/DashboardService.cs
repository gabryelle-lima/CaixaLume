using System.Globalization;
using CaixaLume.Data;
using CaixaLume.Models;

namespace CaixaLume.Services;

public sealed class DashboardService
{
    public ResumoDashboard ObterResumo()
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        string today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var totals = db.Query("SELECT COUNT(*) AS Quantidade, COALESCE(SUM(ValorTotal), 0) AS Faturamento FROM Vendas WHERE Status = 'Concluida' AND DataVenda >= @Inicio AND DataVenda < @Fim;", new Dictionary<string, object?> { ["@Inicio"] = today, ["@Fim"] = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }).Single();
        decimal revenue = Convert.ToDecimal(totals["Faturamento"], CultureInfo.InvariantCulture);
        int count = Convert.ToInt32(totals["Quantidade"], CultureInfo.InvariantCulture);
        var low = db.Query("SELECT Nome AS Produto, UnidadeMedida AS Unidade, EstoqueAtual AS Estoque, EstoqueMinimo AS Minimo FROM Produtos WHERE ExcluidoEm IS NULL AND Ativo = 1 AND EstoqueAtual <= EstoqueMinimo ORDER BY EstoqueAtual, Nome LIMIT 12;");
        var expiring = db.Query("SELECT Nome AS Produto,DataValidade FROM Produtos WHERE ExcluidoEm IS NULL AND Ativo=1 AND DataValidade IS NOT NULL AND DataValidade>=@Hoje AND DataValidade<=@Limite ORDER BY DataValidade LIMIT 12;", new Dictionary<string, object?> { ["@Hoje"] = today, ["@Limite"] = DateTime.Today.AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
        var recent = db.Query("SELECT v.Id, v.DataVenda AS Data, COALESCE(u.Nome,'—') AS Operador, v.FormaPagamento AS Pagamento, v.ValorTotal AS Total FROM Vendas v LEFT JOIN Usuarios u ON u.Id=v.UsuarioId WHERE v.Status = 'Concluida' ORDER BY v.Id DESC LIMIT 10;");
        var expired = db.Query("SELECT Nome AS Produto,DataValidade FROM Produtos WHERE ExcluidoEm IS NULL AND Ativo=1 AND DataValidade IS NOT NULL AND DataValidade<@Hoje ORDER BY DataValidade LIMIT 50;", new Dictionary<string, object?> { ["@Hoje"] = today });
        var store = db.Query("SELECT Valor FROM Configuracoes WHERE Chave = 'NomeLoja';").FirstOrDefault();
        string storeName = Convert.ToString(store?.GetValueOrDefault("Valor")) ?? "";
        bool cashOpen = db.Query("SELECT Id FROM Caixa WHERE Status = 'Aberto' LIMIT 1;").Count > 0;
        return new ResumoDashboard
        {
            NomeLoja = string.IsNullOrWhiteSpace(storeName) ? "Mercado" : storeName.Trim(),
            FaturamentoHoje = revenue,
            VendasHoje = count,
            TicketMedio = count > 0 ? revenue / count : 0,
            ItensEstoqueBaixo = Convert.ToInt32(db.Query("SELECT COUNT(*) AS Quantidade FROM Produtos WHERE ExcluidoEm IS NULL AND Ativo = 1 AND EstoqueAtual <= EstoqueMinimo;").Single()["Quantidade"], CultureInfo.InvariantCulture),
            CaixaAberto = cashOpen,
            ProdutosVencidos = expired.Select(row => { var date = DateTime.Parse(Convert.ToString(row["DataValidade"])!, CultureInfo.InvariantCulture); return new ProdutoValidadeProxima { Produto = Convert.ToString(row["Produto"]) ?? "", Validade = date, Dias = (date - DateTime.Today).Days }; }).ToList(),
            VendasRecentes = recent.Select(row => new VendaRecente { Id = Convert.ToInt64(row["Id"], CultureInfo.InvariantCulture), Data = DateTime.Parse(Convert.ToString(row["Data"]) ?? "", CultureInfo.InvariantCulture), Operador = Convert.ToString(row["Operador"]) ?? "—", Pagamento = Convert.ToString(row["Pagamento"]) ?? "", Total = Convert.ToDecimal(row["Total"], CultureInfo.InvariantCulture) }).ToList(),
            ProdutosEstoqueBaixo = low.Select(row => new ProdutoEstoqueBaixo { Produto = Convert.ToString(row["Produto"]) ?? "", Unidade = Convert.ToString(row["Unidade"]) ?? "UN", Estoque = Convert.ToDecimal(row["Estoque"], CultureInfo.InvariantCulture), Minimo = Convert.ToDecimal(row["Minimo"], CultureInfo.InvariantCulture) }).ToList(),
            ProdutosValidadeProxima=expiring.Select(row=>{var expiry=DateTime.Parse(Convert.ToString(row["DataValidade"])!,CultureInfo.InvariantCulture);return new ProdutoValidadeProxima{Produto=Convert.ToString(row["Produto"])??"",Validade=expiry,Dias=(expiry-DateTime.Today).Days};}).ToList()
        };
    }
}
