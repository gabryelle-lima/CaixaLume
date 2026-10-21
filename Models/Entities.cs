namespace MercadinhoPdv.Models;

// Modelos representam os dados do domínio. Operações sobre eles ficarão em Services.
public class Produto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? CodigoBarras { get; set; }
    public string? Marca { get; set; }
    public string? ImagemUrl { get; set; }
    public string? LocalImagePath { get; set; }
    public int? CategoriaId { get; set; }
    public string? CategoriaNome { get; set; }
    public int? FornecedorId { get; set; }
    public string? FornecedorNome { get; set; }
    public decimal PrecoCusto { get; set; }
    public decimal PrecoVenda { get; set; }
    public string UnidadeMedida { get; set; } = "UN";
    public decimal EstoqueAtual { get; set; }
    public decimal EstoqueMinimo { get; set; }
    public DateTime? DataValidade { get; set; }
    public bool Ativo { get; set; } = true;
}
public class Categoria { public int Id { get; set; } public string Nome { get; set; } = string.Empty; public bool Ativa { get; set; } = true; }
public class Venda { public int Id { get; set; } public DateTime DataVenda { get; set; } public decimal ValorTotal { get; set; } }
public class VendaHistoricoLinha { public decimal Subtotal { get; set; } public decimal Desconto { get; set; } public decimal Acrescimo { get; set; } public long Id { get; set; } public DateTime Data { get; set; } public string Operador { get; set; } = ""; public string Pagamento { get; set; } = ""; public decimal Total { get; set; } public string Status { get; set; } = ""; }
public class VendaItemDetalhe { public string Produto { get; set; } = ""; public decimal Quantidade { get; set; } public string Unidade { get; set; } = "UN"; public decimal PrecoUnitario { get; set; } public decimal Subtotal { get; set; } }
public class ItemVenda { public int Id { get; set; } public int VendaId { get; set; } public int ProdutoId { get; set; } public decimal Quantidade { get; set; } public decimal Subtotal { get; set; } }
public class Caixa { public int Id { get; set; } public DateTime DataAbertura { get; set; } public decimal SaldoInicial { get; set; } public string Status { get; set; } = string.Empty; }
public class Despesa { public int Id { get; set; } public string Descricao { get; set; } = string.Empty; public decimal Valor { get; set; } public DateTime DataDespesa { get; set; } }
public class Fornecedor { public int Id { get; set; } public string Nome { get; set; } = string.Empty; public string? Telefone { get; set; } public string? Email { get; set; } public string? Documento { get; set; } public string? Endereco { get; set; } public string? Observacao { get; set; } public bool Ativo { get; set; } = true; }
public class MovimentacaoEstoque { public int Id { get; set; } public int ProdutoId { get; set; } public string Tipo { get; set; } = string.Empty; public decimal Quantidade { get; set; } public DateTime DataMovimentacao { get; set; } }
public class Usuario { public int Id { get; set; } public string Nome { get; set; } = string.Empty; public string Login { get; set; } = string.Empty; }
