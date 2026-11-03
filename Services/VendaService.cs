using CaixaLume.Data;
using CaixaLume.Models;

namespace CaixaLume.Services;

public sealed class VendaService
{
    private readonly VendaRepository _repository = new();

    public long RegistrarPorProdutoIds(IReadOnlyList<(int ProdutoId, decimal Quantidade)> solicitados, string formaPagamento,
        decimal? valorRecebido = null, AjusteVenda? ajuste = null, string? operacaoId = null, decimal? totalEsperado = null, IReadOnlyList<PagamentoVenda>? pagamentos = null)
    {
        if (solicitados.Count is < 1 or > 250) throw new InvalidOperationException("A venda precisa conter de 1 a 250 itens.");
        if (solicitados.Any(item => item.ProdutoId <= 0 || item.Quantidade <= 0 || item.Quantidade > 100_000))
            throw new InvalidOperationException("Produto ou quantidade inválidos.");

        string forma = formaPagamento.Trim().ToUpperInvariant();
        ValidarPagamento(forma, valorRecebido);
        // O repositório reconhece o reenvio antes de consultar catálogo, preço e estoque.
        var itens = solicitados.Select(x => new ItemCarrinho { ProdutoId = x.ProdutoId, Quantidade = x.Quantidade }).ToList();
        return _repository.Registrar(itens, forma, valorRecebido, ajuste, operacaoId, totalEsperado, pagamentos, consultarCatalogo: true);
    }

    public long Registrar(List<ItemCarrinho> itens, string formaPagamento, decimal? valorRecebido = null, AjusteVenda? ajuste = null, string? operacaoId = null, decimal? totalEsperado = null, IReadOnlyList<PagamentoVenda>? pagamentos = null)
    {
        if (itens.Count is < 1 or > 250) throw new InvalidOperationException("Adicione ao menos um produto à venda.");
        formaPagamento = formaPagamento.Trim().ToUpperInvariant();
        ValidarPagamento(formaPagamento, valorRecebido);
        if (itens.Any(item => item.ProdutoId <= 0 || item.Quantidade <= 0 || item.Quantidade > 100_000 || item.PrecoUnitario < 0)) throw new InvalidOperationException("Confira as quantidades e os preços dos itens.");
        return _repository.Registrar(itens, formaPagamento, valorRecebido, ajuste, operacaoId, totalEsperado, pagamentos);
    }

    private static void ValidarPagamento(string forma, decimal? recebido)
    {
        if (forma is not ("DINHEIRO" or "PIX" or "DÉBITO" or "CRÉDITO" or "MISTO")) throw new InvalidOperationException("Selecione uma forma de pagamento válida.");
        if (recebido.HasValue) ValoresMonetarios.Centavos(recebido.Value);
    }

    public List<VendaHistoricoLinha> ListarHistorico(DateTime inicio, DateTime fim, string? status = null) => _repository.ListarHistorico(inicio, fim, status);
    public PaginaVendas ListarHistoricoPaginado(DateTime inicio, DateTime fim, string? status = null, int pagina = 1, int tamanho = 100, long? limiteId = null)
        => _repository.ListarHistoricoPaginado(inicio,fim,status,pagina,tamanho,limiteId);
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

public sealed record PaginaVendas(List<VendaHistoricoLinha> Itens,int Pagina,int Tamanho,long Total,long LimiteId) { public bool TemProxima => (long)Pagina*Tamanho<Total; }
