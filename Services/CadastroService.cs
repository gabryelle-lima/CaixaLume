using MercadinhoPdv.Data;
using MercadinhoPdv.Models;

namespace MercadinhoPdv.Services;

public sealed class CategoriaService
{
    private readonly CadastroRepository _repository = new();
    public List<Categoria> Listar(string? pesquisa = null, bool incluirInativas = false) => _repository.ListarCategorias(pesquisa, incluirInativas);
    public void Salvar(Categoria category)
    {
        Autorizacao.ExigirAdministrador();
        category.Nome = category.Nome.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(category.Nome)) throw new InvalidOperationException("Informe o nome da categoria.");
        _repository.Salvar(category);
    }
    public void Desativar(int id) { Autorizacao.ExigirAdministrador(); _repository.DesativarCategoria(id); }
}

public sealed class FornecedorService
{
    private readonly CadastroRepository _repository = new();
    public List<Fornecedor> Listar(string? pesquisa = null, bool incluirInativos = false) => _repository.ListarFornecedores(pesquisa, incluirInativos);
    public void Salvar(Fornecedor supplier)
    {
        Autorizacao.ExigirAdministrador();
        supplier.Nome = supplier.Nome.Trim().ToUpperInvariant();
        supplier.Telefone = BlankToNull(supplier.Telefone);
        supplier.Documento = BlankToNull(supplier.Documento);
        supplier.Email = BlankToNull(supplier.Email);
        supplier.Endereco = UpperOrNull(supplier.Endereco);
        supplier.Observacao = UpperOrNull(supplier.Observacao);
        if (string.IsNullOrWhiteSpace(supplier.Nome)) throw new InvalidOperationException("Informe o nome do fornecedor.");
        _repository.Salvar(supplier);
    }
    public void Desativar(int id) { Autorizacao.ExigirAdministrador(); _repository.DesativarFornecedor(id); }
    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? UpperOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}

public sealed class ConfiguracaoService
{
    private readonly CadastroRepository _repository = new();
    public string ObterNomeLoja() => _repository.ObterNomeLoja();
    public void SalvarNomeLoja(string name)
    {
        Autorizacao.ExigirAdministrador();
        name = name.Trim();
        if (name.Length == 0) throw new InvalidOperationException("Informe o nome do estabelecimento.");
        _repository.SalvarNomeLoja(name);
    }
}
