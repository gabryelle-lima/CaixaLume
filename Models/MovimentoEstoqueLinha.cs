namespace CaixaLume.Models;

public sealed class MovimentoEstoqueLinha
{
    public long Id { get; set; }
    public int ProdutoId { get; set; }
    public long? VendaId { get; set; }
    public int? UsuarioId { get; set; }
    public decimal? SaldoAnterior { get; set; }
    public decimal? SaldoPosterior { get; set; }
    public decimal? Variacao => SaldoAnterior.HasValue && SaldoPosterior.HasValue ? SaldoPosterior-SaldoAnterior : Tipo switch { "ENTRADA" or "ESTORNO" => Quantidade, "SAÍDA" or "VENDA" => -Quantidade, _ => null };
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
