namespace CaixaLume.Models;

public sealed class ResumoCaixa
{
    public long Id { get; init; }
    public DateTime DataAbertura { get; init; }
    public decimal SaldoInicial { get; init; }
    public int QuantidadeVendas { get; init; }
    public decimal TotalVendas { get; init; }
    public decimal VendasDinheiro { get; init; }
    public decimal Suprimentos { get; init; }
    public decimal Sangrias { get; init; }
    public decimal DespesasDinheiro { get; init; }
    public decimal DespesasTotais { get; init; }
    public decimal ValorEsperadoDinheiro => SaldoInicial + VendasDinheiro + Suprimentos - Sangrias - DespesasDinheiro;
}

public sealed class ResumoFechamento
{
    public decimal ValorEsperado { get; init; }
    public decimal ValorContado { get; init; }
    public decimal Diferenca => ValorContado - ValorEsperado;
}
