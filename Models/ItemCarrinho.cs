namespace MercadinhoPdv.Models;

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
    public decimal Subtotal => decimal.Round(Quantidade * decimal.Round(PrecoUnitario, 2, MidpointRounding.AwayFromZero), 2, MidpointRounding.AwayFromZero);
    public string QuantidadeFormatada => $"{Quantidade.ToString(UnidadeMedida == "UN" ? "0" : "0.000", CultureInfo.CurrentCulture)} {UnidadeMedida}";
}
