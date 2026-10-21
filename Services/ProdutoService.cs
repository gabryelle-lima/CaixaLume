using MercadinhoPdv.Data;
using MercadinhoPdv.Models;

namespace MercadinhoPdv.Services;

public sealed class ProdutoService
{
    private readonly ProdutoRepository _repository = new();

    public List<Produto> Listar(string? pesquisa) => _repository.Listar(pesquisa);
    public Produto? BuscarPorId(int id) => _repository.BuscarPorId(id);
    public Produto? BuscarPorCodigoBarras(string codigo) => _repository.BuscarPorCodigoBarras(codigo);
    public List<OpcaoCadastro> ListarCategorias(int? selectedId = null) => _repository.ListarCategorias(selectedId);
    public List<OpcaoCadastro> ListarFornecedores(int? selectedId = null) => _repository.ListarFornecedores(selectedId);

    public void Salvar(Produto produto)
    {
        Autorizacao.ExigirAdministrador();
        produto.Nome = produto.Nome.Trim().ToUpperInvariant();
        produto.Marca = string.IsNullOrWhiteSpace(produto.Marca) ? null : produto.Marca.Trim().ToUpperInvariant();
        produto.ImagemUrl = string.IsNullOrWhiteSpace(produto.ImagemUrl) ? null : produto.ImagemUrl.Trim();
        produto.CodigoBarras = string.IsNullOrWhiteSpace(produto.CodigoBarras) ? null : produto.CodigoBarras.Trim();
        produto.UnidadeMedida = string.IsNullOrWhiteSpace(produto.UnidadeMedida) ? "UN" : produto.UnidadeMedida.Trim().ToUpperInvariant();
        Validar(produto);
        if (produto.CodigoBarras is not null && _repository.CodigoBarrasJaExiste(produto.CodigoBarras, produto.Id))
            throw new ProdutoValidationException("Já existe um produto com este código de barras.");
        if (produto.Id == 0) _repository.Inserir(produto); else _repository.Atualizar(produto);
    }

    public void Desativar(int id) { Autorizacao.ExigirAdministrador(); _repository.Desativar(id); }
    public void Reativar(int id) { Autorizacao.ExigirAdministrador(); _repository.Reativar(id); }
    public void Excluir(int id) { Autorizacao.ExigirExclusaoProduto(); _repository.Excluir(id); }

    private static void Validar(Produto produto)
    {
        if (string.IsNullOrWhiteSpace(produto.Nome)) throw new ProdutoValidationException("Informe o nome do produto.");
        if (produto.PrecoVenda < 0) throw new ProdutoValidationException("O preço de venda não pode ser negativo.");
        if (produto.PrecoCusto < 0) throw new ProdutoValidationException("O preço de custo não pode ser negativo.");
        if (produto.EstoqueAtual < 0) throw new ProdutoValidationException("O estoque atual não pode ser negativo.");
        if (produto.EstoqueMinimo < 0) throw new ProdutoValidationException("O estoque mínimo não pode ser negativo.");
        if (produto.UnidadeMedida is not ("UN" or "KG")) throw new ProdutoValidationException("Selecione uma unidade de medida válida.");
        if (produto.UnidadeMedida == "UN" && (produto.EstoqueAtual % 1 != 0 || produto.EstoqueMinimo % 1 != 0))
            throw new ProdutoValidationException("Produtos vendidos por unidade devem ter estoque inteiro.");
        if (produto.UnidadeMedida == "KG" && (produto.EstoqueAtual * 1000 % 1 != 0 || produto.EstoqueMinimo * 1000 % 1 != 0))
            throw new ProdutoValidationException("Produtos vendidos por KG aceitam até três casas decimais no estoque.");
    }
}

public sealed class ProdutoValidationException(string message) : Exception(message);
