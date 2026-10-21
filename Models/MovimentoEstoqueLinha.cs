namespace MercadinhoPdv.Models;

public sealed class MovimentoEstoqueLinha
{
    public DateTime Data { get; set; }
    public string Produto { get; set; } = string.Empty;
    public string? CodigoBarras { get; set; }
    public string UnidadeMedida { get; set; } = "UN";
    public string Tipo { get; set; } = string.Empty;
    public decimal Quantidade { get; set; }
    public string? Motivo { get; set; }
    public string? Usuario { get; set; }
    public string? Documento { get; set; }
}
