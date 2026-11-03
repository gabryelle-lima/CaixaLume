namespace CaixaLume.Models;

public sealed record AjusteVenda
{
    public string TipoDesconto { get; init; } = "VALOR";
    public decimal Desconto { get; init; }
    public decimal Acrescimo { get; init; }
}

public sealed record TotaisVenda(decimal Subtotal, decimal Desconto, decimal Acrescimo, decimal Total);

public sealed record PoliticaAjustesVenda
{
    public bool Habilitada { get; init; }
    public bool PermitirOperador { get; init; }
    public decimal MaxDescontoPercentual { get; init; }
    public decimal MaxAcrescimoValor { get; init; }
}
