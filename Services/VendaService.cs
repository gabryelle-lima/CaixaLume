using MercadinhoPdv.Data;
using MercadinhoPdv.Models;

namespace MercadinhoPdv.Services;

public sealed class VendaService
{
    private readonly VendaRepository _repository = new();
    private readonly ProdutoRepository _products = new();

    public long RegistrarPorProdutoIds(IReadOnlyList<(int ProdutoId, decimal Quantidade)> solicitados, string formaPagamento,
        decimal? valorRecebido = null, AjusteVenda? ajuste = null, string? operacaoId = null, decimal? totalEsperado = null)
    {
        if (solicitados.Count is < 1 or > 250) throw new InvalidOperationException("A venda precisa conter de 1 a 250 itens.");
        if (solicitados.Any(item => item.ProdutoId <= 0 || item.Quantidade <= 0 || item.Quantidade > 100_000))
            throw new InvalidOperationException("Produto ou quantidade inválidos.");

        var products = _products.BuscarParaVenda(solicitados.Select(item => item.ProdutoId).ToArray());
        var items = new List<ItemCarrinho>(solicitados.Count);
        foreach (var requested in solicitados)
        {
            if (!products.TryGetValue(requested.ProdutoId, out Produto? product))
                throw new InvalidOperationException("Um dos produtos não está mais disponível.");
            items.Add(new ItemCarrinho
            {
                ProdutoId = product.Id,
                Produto = product.Nome,
                CodigoBarras = product.CodigoBarras,
                UnidadeMedida = product.UnidadeMedida,
                Quantidade = requested.Quantidade,
                PrecoUnitario = product.PrecoVenda,
                EstoqueDisponivel = product.EstoqueAtual
            });
        }
        return Registrar(items, formaPagamento, valorRecebido, ajuste, operacaoId, totalEsperado);
    }

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
    public VendaOperacao? ObterPorOperacao(string? operacaoId)
    {
        if (!Guid.TryParse(operacaoId, out Guid parsed) || parsed == Guid.Empty)
            throw new InvalidOperationException("Identificador da venda inválido.");
        int usuarioId = Autorizacao.ExigirUsuario();
        return _repository.ObterPorOperacao(parsed.ToString("D"), usuarioId);
    }
    public void Cancelar(long vendaId, string motivo) => _repository.Cancelar(vendaId, motivo);
}

public sealed record VendaOperacao(long Id, string Status, decimal Total, string FormaPagamento, decimal ValorRecebido, decimal Troco);
