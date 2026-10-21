namespace MercadinhoPdv.Models;

public sealed class HistoricoCaixaLinha
{
    public long Id { get; init; }
    public DateTime Abertura { get; init; }
    public DateTime? Fechamento { get; init; }
    public decimal SaldoInicial { get; init; }
    public decimal Vendas { get; init; }
    public decimal Suprimentos { get; init; }
    public decimal Sangrias { get; init; }
    public decimal Despesas { get; init; }
    public decimal ValorEsperado { get; init; }
    public decimal? ValorContado { get; init; }
    public decimal? Diferenca => ValorContado.HasValue ? ValorContado.Value - ValorEsperado : null;
    public string Status { get; init; } = "";
    public string UsuarioAbertura { get; init; } = "—";
    public string UsuarioFechamento { get; init; } = "—";
}
