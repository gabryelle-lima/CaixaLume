using MercadinhoPdv.Data;
using MercadinhoPdv.Models;

namespace MercadinhoPdv.Services;

public sealed class VendaService
{
    private readonly VendaRepository _repository = new();

    public long Registrar(List<ItemCarrinho> itens, string formaPagamento, decimal? valorRecebido = null, AjusteVenda? ajuste = null, string? operacaoId = null, decimal? totalEsperado = null)
    {
        if (itens.Count == 0) throw new InvalidOperationException("Adicione ao menos um produto à venda.");
        formaPagamento = formaPagamento.Trim().ToUpperInvariant();
        if (formaPagamento is not ("DINHEIRO" or "PIX" or "DÉBITO" or "CRÉDITO")) throw new InvalidOperationException("Selecione uma forma de pagamento válida.");
        if (itens.Any(item => item.Quantidade <= 0 || item.PrecoUnitario < 0)) throw new InvalidOperationException("Confira as quantidades e os preços dos itens.");
        if (valorRecebido is < 0 || (valorRecebido.HasValue && decimal.Round(valorRecebido.Value, 2) != valorRecebido.Value))
            throw new InvalidOperationException("Informe um valor recebido válido, com até duas casas decimais.");
        return _repository.Registrar(itens, formaPagamento, valorRecebido, ajuste, operacaoId, totalEsperado);
    }

    public List<VendaHistoricoLinha> ListarHistorico(DateTime inicio, DateTime fim, string? status = null) => _repository.ListarHistorico(inicio, fim, status);
    public List<VendaItemDetalhe> Detalhes(long vendaId) => _repository.Detalhes(vendaId);
    public TotaisVenda ObterTotais(long vendaId) => _repository.ObterTotais(vendaId);
    public void Cancelar(long vendaId, string motivo) => _repository.Cancelar(vendaId, motivo);
}
