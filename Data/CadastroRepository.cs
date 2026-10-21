using System.Globalization;
using MercadinhoPdv.Models;
using MercadinhoPdv.Services;

namespace MercadinhoPdv.Data;

internal sealed class CadastroRepository
{
    public List<Categoria> ListarCategorias(string? pesquisa, bool incluirInativas)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        return db.Query("SELECT Id, Nome, Ativa FROM Categorias WHERE (@Incluir = 1 OR Ativa = 1) AND (@Busca = '' OR Nome LIKE @Termo) ORDER BY Nome;", new Dictionary<string, object?> { ["@Incluir"] = incluirInativas, ["@Busca"] = pesquisa?.Trim() ?? "", ["@Termo"] = $"%{pesquisa?.Trim()}%" })
            .Select(row => new Categoria { Id = Convert.ToInt32(row["Id"], CultureInfo.InvariantCulture), Nome = Convert.ToString(row["Nome"]) ?? "", Ativa = Convert.ToInt32(row["Ativa"], CultureInfo.InvariantCulture) == 1 }).ToList();
    }

    public void Salvar(Categoria category)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        if (category.Id == 0) db.Execute("INSERT INTO Categorias (Nome, Ativa) VALUES (@Nome, 1);", new Dictionary<string, object?> { ["@Nome"] = category.Nome });
        else db.Execute("UPDATE Categorias SET Nome = @Nome WHERE Id = @Id;", new Dictionary<string, object?> { ["@Nome"] = category.Nome, ["@Id"] = category.Id });
        new AuditoriaService().Registrar(category.Id == 0 ? "CRIACAO" : "EDICAO", "CATEGORIA", category.Nome);
    }

    public void DesativarCategoria(int id)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        db.Execute("UPDATE Categorias SET Ativa = 0 WHERE Id = @Id;", new Dictionary<string, object?> { ["@Id"] = id });
        new AuditoriaService().Registrar("DESATIVACAO", "CATEGORIA", $"Categoria #{id}");
    }

    public List<Fornecedor> ListarFornecedores(string? pesquisa, bool incluirInativos)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        return db.Query("SELECT Id, Nome, Telefone, Email, Documento, Endereco, Observacao, Ativo FROM Fornecedores WHERE (@Incluir = 1 OR Ativo = 1) AND (@Busca = '' OR Nome LIKE @Termo OR Telefone LIKE @Termo OR Documento LIKE @Termo) ORDER BY Nome;", new Dictionary<string, object?> { ["@Incluir"] = incluirInativos, ["@Busca"] = pesquisa?.Trim() ?? "", ["@Termo"] = $"%{pesquisa?.Trim()}%" })
            .Select(row => new Fornecedor { Id = Convert.ToInt32(row["Id"], CultureInfo.InvariantCulture), Nome = Convert.ToString(row["Nome"]) ?? "", Telefone = Convert.ToString(row["Telefone"]), Email = Convert.ToString(row["Email"]), Documento = Convert.ToString(row["Documento"]), Endereco = Convert.ToString(row["Endereco"]), Observacao = Convert.ToString(row["Observacao"]), Ativo = Convert.ToInt32(row["Ativo"], CultureInfo.InvariantCulture) == 1 }).ToList();
    }

    public void Salvar(Fornecedor supplier)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        if (supplier.Id == 0) db.Execute("INSERT INTO Fornecedores (Nome, Telefone, Email, Documento, Endereco, Observacao, Ativo) VALUES (@Nome, @Telefone, @Email, @Documento, @Endereco, @Observacao, 1);", Parameters(supplier));
        else
        {
            var parameters = Parameters(supplier);
            parameters["@Id"] = supplier.Id;
            db.Execute("UPDATE Fornecedores SET Nome = @Nome, Telefone = @Telefone, Email = @Email, Documento = @Documento, Endereco=@Endereco, Observacao=@Observacao WHERE Id = @Id;", parameters);
        }
        new AuditoriaService().Registrar(supplier.Id == 0 ? "CRIACAO" : "EDICAO", "FORNECEDOR", supplier.Nome);
    }

    public void DesativarFornecedor(int id)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        db.Execute("UPDATE Fornecedores SET Ativo = 0 WHERE Id = @Id;", new Dictionary<string, object?> { ["@Id"] = id });
        new AuditoriaService().Registrar("DESATIVACAO", "FORNECEDOR", $"Fornecedor #{id}");
    }

    public string ObterNomeLoja()
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var row = db.Query("SELECT Valor FROM Configuracoes WHERE Chave = 'NomeLoja';").FirstOrDefault();
        return Convert.ToString(row?.GetValueOrDefault("Valor")) ?? "MERCADINHO PDV";
    }

    public void SalvarNomeLoja(string value)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        db.Execute("INSERT INTO Configuracoes (Chave, Valor) VALUES ('NomeLoja', @Valor) ON CONFLICT(Chave) DO UPDATE SET Valor = excluded.Valor;", new Dictionary<string, object?> { ["@Valor"] = value });
        new AuditoriaService().Registrar("EDICAO", "ESTABELECIMENTO", value);
    }

    private static Dictionary<string, object?> Parameters(Fornecedor supplier) => new() { ["@Nome"] = supplier.Nome, ["@Telefone"] = supplier.Telefone, ["@Email"] = supplier.Email, ["@Documento"] = supplier.Documento, ["@Endereco"] = supplier.Endereco, ["@Observacao"] = supplier.Observacao };
}
