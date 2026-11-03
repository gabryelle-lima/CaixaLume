using CaixaLume.Data;
using CaixaLume.Models;

namespace CaixaLume.Services;

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
    public string ObterCnpjLoja() => _repository.ObterCnpjLoja();
    public void SalvarCnpjLoja(string value)
    {
        Autorizacao.ExigirAdministrador();
        string cnpj = (value ?? "").Trim().Replace(".", "").Replace("/", "").Replace("-", "").Replace(" ", "");
        if (cnpj.Length != 14 || !cnpj.All(char.IsAsciiDigit) || !CnpjValido(cnpj))
            throw new InvalidOperationException("Informe um CNPJ numérico válido com 14 dígitos. CNPJ alfanumérico ainda não é aceito nesta importação.");
        _repository.SalvarCnpjLoja(cnpj);
    }
    private static bool CnpjValido(string cnpj)
    {
        if (cnpj.Distinct().Count() == 1) return false;
        static int Digit(string digits, int[] weights)
        {
            int sum = 0;
            for (int i = 0; i < weights.Length; i++) sum += (digits[i] - '0') * weights[i];
            int rest = sum % 11;
            return rest < 2 ? 0 : 11 - rest;
        }
        int first = Digit(cnpj, [5,4,3,2,9,8,7,6,5,4,3,2]);
        int second = Digit(cnpj, [6,5,4,3,2,9,8,7,6,5,4,3,2]);
        return cnpj[12] - '0' == first && cnpj[13] - '0' == second;
    }
    public void SalvarNomeLoja(string name)
    {
        Autorizacao.ExigirAdministrador();
        name = name.Trim();
        if (name.Length == 0) throw new InvalidOperationException("Informe o nome do estabelecimento.");
        _repository.SalvarNomeLoja(name);
    }
}
