namespace CaixaLume.Services;

public static class ValoresMonetarios
{
    public static decimal Arredondar(decimal valor) => decimal.Round(valor, 2, MidpointRounding.AwayFromZero);
    public static decimal Subtotal(decimal quantidade, decimal preco) => Arredondar(quantidade * Arredondar(preco));
    public static long Centavos(decimal valor)
    {
        if (valor < 0 || valor > 1_000_000_000m || Arredondar(valor) != valor)
            throw new InvalidOperationException("Valor monetário inválido: informe até duas casas decimais e no máximo R$ 1 bilhão.");
        return checked((long)(valor * 100m));
    }
}
