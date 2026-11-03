namespace CaixaLume.Models;

// Valor é o montante informado pelo operador; somente dinheiro admite troco.
// O fluxo local não confirma pagamentos bancários.
public sealed record PagamentoVenda(string Forma, decimal Valor);
public sealed record PagamentoRegistrado(string Forma, long ValorCentavos, long RecebidoCentavos,
    string Origem = "MANUAL", string Status = "REGISTRADO_MANUALMENTE");
