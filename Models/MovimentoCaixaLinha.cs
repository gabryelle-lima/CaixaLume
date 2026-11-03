namespace CaixaLume.Models;

public sealed class MovimentoCaixaLinha
{
    public DateTime Data { get; init; }
    public string Tipo { get; init; } = string.Empty;
    public decimal Valor { get; init; }
    public string? Observacao { get; init; }
    public string Usuario { get; init; } = "—";
}
