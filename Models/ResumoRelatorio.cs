namespace CaixaLume.Models;

public sealed class ResumoRelatorio
{
    public DateTime Inicio { get; init; }
    public DateTime Fim { get; init; }
    public int QuantidadeVendas { get; init; }
    public decimal TotalVendido { get; init; }
    public decimal SubtotalVendas { get; init; }
    public decimal TotalDescontos { get; init; }
    public decimal TotalAcrescimos { get; init; }
    public decimal TicketMedio { get; init; }
    public decimal TotalDespesas { get; init; }
    public decimal CustoEstimado { get; init; }
    public decimal LucroBrutoEstimado { get; init; }
    public decimal MargemMediaPercentual { get; init; }
    public List<VendaRelatorioLinha> Vendas { get; init; } = [];
    public List<PagamentoRelatorioLinha> Pagamentos { get; init; } = [];
    public List<ProdutoMaisVendidoLinha> ProdutosMaisVendidos { get; init; } = [];
    public List<ProdutoMaisVendidoLinha> ProdutosMenosVendidos { get; init; } = [];
    public List<ProdutoEstoqueBaixo> EstoqueBaixo { get; init; } = [];
    public List<MovimentoEstoqueLinha> MovimentacoesEstoque { get; init; } = [];
    public List<HistoricoCaixaLinha> Caixas { get; init; } = [];
    public List<ProdutoValidadeProxima> Validades { get; init; } = [];
    public List<DespesaRelatorioLinha> Despesas { get; init; } = [];
}

public sealed class VendaRelatorioLinha
{
    public long Id { get; init; }
    public DateTime Data { get; init; }
    public string FormaPagamento { get; init; } = "";
    public decimal Total { get; init; }
    public decimal Subtotal { get; init; }
    public decimal Desconto { get; init; }
    public decimal Acrescimo { get; init; }
}

public sealed class PagamentoRelatorioLinha
{
    public string FormaPagamento { get; init; } = "";
    public int Quantidade { get; init; }
    public decimal Total { get; init; }
}

public sealed class ProdutoMaisVendidoLinha
{
    public string Produto { get; init; } = "";
    public string Unidade { get; init; } = "UN";
    public decimal Quantidade { get; init; }
    public decimal Total { get; init; }
}

public sealed class DespesaRelatorioLinha
{
    public DateTime Data { get; init; }
    public string Descricao { get; init; } = "";
    public string FormaPagamento { get; init; } = "";
    public decimal Valor { get; init; }
}
