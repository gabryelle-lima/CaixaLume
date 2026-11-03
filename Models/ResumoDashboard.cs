namespace CaixaLume.Models;

public sealed class ResumoDashboard
{
    public string NomeLoja { get; init; } = "CAIXALUME";
    public decimal FaturamentoHoje { get; init; }
    public int VendasHoje { get; init; }
    public decimal TicketMedio { get; init; }
    public int ItensEstoqueBaixo { get; init; }
    public bool CaixaAberto { get; init; }
    public List<VendaRecente> VendasRecentes { get; init; } = [];
    public List<ProdutoEstoqueBaixo> ProdutosEstoqueBaixo { get; init; } = [];
    public List<ProdutoValidadeProxima> ProdutosValidadeProxima { get; init; } = [];
    public List<ProdutoValidadeProxima> ProdutosVencidos { get; init; } = [];
}

public sealed class ProdutoValidadeProxima
{
    public string Produto { get; init; } = "";
    public DateTime Validade { get; init; }
    public int Dias { get; init; }
    public string Status => Dias < 0 ? "VENCIDO" : Dias == 0 ? "VENCE HOJE" : Dias <= 30 ? "PRÓXIMO" : "DENTRO DA VALIDADE";
}

public sealed class VendaRecente
{
    public long Id { get; init; }
    public DateTime Data { get; init; }
    public string Operador { get; init; } = "—";
    public string Pagamento { get; init; } = "";
    public decimal Total { get; init; }
}

public sealed class ProdutoEstoqueBaixo
{
    public string Produto { get; init; } = "";
    public string Unidade { get; init; } = "UN";
    public decimal Estoque { get; init; }
    public decimal Minimo { get; init; }
}
