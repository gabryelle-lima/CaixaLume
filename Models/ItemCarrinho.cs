namespace CaixaLume.Models;

using System.Globalization;

public sealed class ItemCarrinho
{
    public int ProdutoId { get; set; }
    public string Produto { get; set; } = string.Empty;
    public string? CodigoBarras { get; set; }
    public string UnidadeMedida { get; set; } = "UN";
    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal EstoqueDisponivel { get; set; }
    public decimal Subtotal => Services.ValoresMonetarios.Subtotal(Quantidade, PrecoUnitario);
    public string QuantidadeFormatada => $"{Quantidade.ToString(UnidadeMedida == "UN" ? "0" : "0.000", CultureInfo.CurrentCulture)} {UnidadeMedida}";
}
