using System.Globalization;
using CaixaLume.Data;
using CaixaLume.Models;

namespace CaixaLume.Services;

public sealed class RelatoriosService
{
    public ResumoRelatorio Gerar(DateTime inicio, DateTime fim)
    {
        Autorizacao.ExigirAdministrador();
        inicio = inicio.Date;
        fim = fim.Date;
        if (fim < inicio) throw new InvalidOperationException("A data final deve ser igual ou posterior à data inicial.");
        string from = inicio.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string until = fim.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var salesRows = db.Query("SELECT Id, DataVenda AS Data, FormaPagamento, ValorTotal AS Total, Subtotal, ValorDesconto AS Desconto, ValorAcrescimo AS Acrescimo FROM Vendas WHERE Status = 'Concluida' AND DataVenda >= @Inicio AND DataVenda < @Fim ORDER BY DataVenda DESC;", Params(from, until));
        var sales = salesRows.Select(row => new VendaRelatorioLinha { Id = ToLong(row["Id"]), Data = ToDate(row["Data"]), FormaPagamento = ToText(row["FormaPagamento"]), Total = ToDecimal(row["Total"]), Subtotal=ToDecimal(row["Subtotal"]), Desconto=ToDecimal(row["Desconto"]), Acrescimo=ToDecimal(row["Acrescimo"]) }).ToList();
        var payments = db.Query("SELECT FormaPagamento, COUNT(*) AS Quantidade, SUM(Centavos)/100.0 AS Total FROM (SELECT p.Forma AS FormaPagamento,p.ValorCentavos AS Centavos FROM PagamentosVenda p JOIN Vendas v ON v.Id=p.VendaId WHERE v.Status='Concluida' AND v.DataVenda>=@Inicio AND v.DataVenda<@Fim UNION ALL SELECT v.FormaPagamento,CAST(ROUND(v.ValorTotal*100) AS INTEGER) FROM Vendas v WHERE v.Status='Concluida' AND v.DataVenda>=@Inicio AND v.DataVenda<@Fim AND NOT EXISTS (SELECT 1 FROM PagamentosVenda p WHERE p.VendaId=v.Id)) GROUP BY FormaPagamento ORDER BY Total DESC;", Params(from, until))
            .Select(row => new PagamentoRelatorioLinha { FormaPagamento = ToText(row["FormaPagamento"]), Quantidade = ToInt(row["Quantidade"]), Total = ToDecimal(row["Total"]) }).ToList();
        var products = db.Query("SELECT p.Nome AS Produto, i.UnidadeMedida AS Unidade, SUM(i.Quantidade) AS Quantidade, SUM(i.Subtotal) AS Total FROM ItensVenda i JOIN Vendas v ON v.Id = i.VendaId JOIN Produtos p ON p.Id = i.ProdutoId WHERE v.Status = 'Concluida' AND v.DataVenda >= @Inicio AND v.DataVenda < @Fim GROUP BY p.Id, p.Nome, i.UnidadeMedida ORDER BY Quantidade DESC LIMIT 10;", Params(from, until))
            .Select(row => new ProdutoMaisVendidoLinha { Produto = ToText(row["Produto"]), Unidade = ToText(row["Unidade"]), Quantidade = ToDecimal(row["Quantidade"]), Total = ToDecimal(row["Total"]) }).ToList();
        var leastProducts = db.Query("SELECT p.Nome AS Produto,p.UnidadeMedida AS Unidade,COALESCE(SUM(CASE WHEN v.Id IS NOT NULL THEN i.Quantidade ELSE 0 END),0) AS Quantidade,COALESCE(SUM(CASE WHEN v.Id IS NOT NULL THEN i.Subtotal ELSE 0 END),0) AS Total FROM Produtos p LEFT JOIN ItensVenda i ON i.ProdutoId=p.Id LEFT JOIN Vendas v ON v.Id=i.VendaId AND v.Status='Concluida' AND v.DataVenda>=@Inicio AND v.DataVenda<@Fim WHERE p.Ativo=1 GROUP BY p.Id,p.Nome,p.UnidadeMedida ORDER BY Quantidade ASC,p.Nome LIMIT 10;",Params(from,until))
            .Select(row=>new ProdutoMaisVendidoLinha{Produto=ToText(row["Produto"]),Unidade=ToText(row["Unidade"]),Quantidade=ToDecimal(row["Quantidade"]),Total=ToDecimal(row["Total"])}).ToList();
        var lowStock=db.Query("SELECT Nome AS Produto,UnidadeMedida AS Unidade,EstoqueAtual AS Estoque,EstoqueMinimo AS Minimo FROM Produtos WHERE Ativo=1 AND EstoqueAtual<=EstoqueMinimo ORDER BY EstoqueAtual,Nome;")
            .Select(row=>new ProdutoEstoqueBaixo{Produto=ToText(row["Produto"]),Unidade=ToText(row["Unidade"]),Estoque=ToDecimal(row["Estoque"]),Minimo=ToDecimal(row["Minimo"])}).ToList();
        var expiryRows=db.Query("SELECT Nome AS Produto,DataValidade FROM Produtos WHERE Ativo=1 AND DataValidade IS NOT NULL AND DataValidade>=@Inicio AND DataValidade<@Fim ORDER BY DataValidade;",Params(from,until));
        var expiry=expiryRows.Select(row=>{var date=ToDate(row["DataValidade"]);return new ProdutoValidadeProxima{Produto=ToText(row["Produto"]),Validade=date,Dias=(date-DateTime.Today).Days};}).ToList();
        var movements=new EstoqueService().ListarMovimentacoes(inicio:inicio,fim:fim);
        var cashHistory=new CaixaService().ListarHistorico(2000).Where(x=>x.Abertura.Date>=inicio&&x.Abertura.Date<=fim).ToList();
        var expenses = db.Query("SELECT DataDespesa AS Data, Descricao, COALESCE(FormaPagamento, '') AS FormaPagamento, Valor FROM Despesas WHERE Status <> 'Cancelada' AND DataDespesa >= @Inicio AND DataDespesa < @Fim ORDER BY DataDespesa DESC;", Params(from, until))
            .Select(row => new DespesaRelatorioLinha { Data = ToDate(row["Data"]), Descricao = ToText(row["Descricao"]), FormaPagamento = ToText(row["FormaPagamento"]), Valor = ToDecimal(row["Valor"]) }).ToList();
        decimal total = sales.Sum(sale => sale.Total);
        decimal estimatedCost = ToDecimal(db.Query("SELECT COALESCE(SUM(i.Quantidade*i.PrecoCustoUnitario),0) AS Custo FROM ItensVenda i JOIN Vendas v ON v.Id=i.VendaId WHERE v.Status='Concluida' AND v.DataVenda>=@Inicio AND v.DataVenda<@Fim;", Params(from,until)).Single()["Custo"]);
        decimal estimatedGrossProfit = total - estimatedCost;
        return new ResumoRelatorio { Inicio = inicio, Fim = fim, QuantidadeVendas = sales.Count, TotalVendido = total, SubtotalVendas=sales.Sum(s=>s.Subtotal), TotalDescontos=sales.Sum(s=>s.Desconto), TotalAcrescimos=sales.Sum(s=>s.Acrescimo), TicketMedio = sales.Count > 0 ? total / sales.Count : 0, TotalDespesas = expenses.Sum(expense => expense.Valor), CustoEstimado=estimatedCost, LucroBrutoEstimado=estimatedGrossProfit, MargemMediaPercentual=total>0?estimatedGrossProfit/total*100:0, Vendas = sales, Pagamentos = payments, ProdutosMaisVendidos = products, ProdutosMenosVendidos=leastProducts,EstoqueBaixo=lowStock,Validades=expiry,MovimentacoesEstoque=movements,Caixas=cashHistory,Despesas = expenses };
    }

    private static Dictionary<string, object?> Params(string from, string until) => new() { ["@Inicio"] = from, ["@Fim"] = until };
    private static string ToText(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    private static DateTime ToDate(object? value) => DateTime.Parse(ToText(value), CultureInfo.InvariantCulture);
    private static int ToInt(object? value) => Convert.ToInt32(value, CultureInfo.InvariantCulture);
    private static long ToLong(object? value) => Convert.ToInt64(value, CultureInfo.InvariantCulture);
    private static decimal ToDecimal(object? value) => Convert.ToDecimal(value, CultureInfo.InvariantCulture);
}
