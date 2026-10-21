using MercadinhoPdv.Data;
using MercadinhoPdv.Models;

namespace MercadinhoPdv.Services;

public static class Autorizacao
{
    public static int ExigirUsuario(bool administrador = false)
    {
        int id = SessaoUsuario.UsuarioId ?? throw new InvalidOperationException("Entre no sistema para continuar.");
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var user = db.Query("SELECT Perfil FROM Usuarios WHERE Id=@Id AND Ativo=1;", new Dictionary<string, object?> { ["@Id"] = id }).FirstOrDefault()
            ?? throw new InvalidOperationException("O usuário não está ativo. Entre novamente.");
        if (administrador && !string.Equals(Convert.ToString(user["Perfil"]), "ADMINISTRADOR", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Somente administradores podem executar esta operação.");
        return id;
    }

    public static void ExigirAdministrador() => ExigirUsuario(administrador: true);

    public static bool PodeExcluirProduto()
    {
        if (SessaoUsuario.UsuarioId is not int id) return false;
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var user = db.Query("SELECT Nome,Perfil FROM Usuarios WHERE Id=@Id AND Ativo=1;", new Dictionary<string, object?> { ["@Id"] = id }).FirstOrDefault();
        if (user is null || !string.Equals(Convert.ToString(user["Perfil"]), "ADMINISTRADOR", StringComparison.OrdinalIgnoreCase)) return false;
        int? ownerId = ObterExclusorProduto(db);
        return ownerId == id;
    }

    public static void ExigirExclusaoProduto()
    {
        int id = ExigirUsuario(administrador: true);
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        int? ownerId = ObterExclusorProduto(db);
        if (ownerId != id) throw new InvalidOperationException("A exclusão de produtos está disponível apenas para a conta GABRYELLE.");
        db.Execute("INSERT OR IGNORE INTO Configuracoes (Chave,Valor) VALUES ('ProdutoExcluirUsuarioId',@Id);", new Dictionary<string, object?> { ["@Id"] = id });
        if (ObterExclusorProduto(db) != id) throw new InvalidOperationException("Esta conta não tem permissão para excluir produtos.");
    }

    private static int? ObterExclusorProduto(DatabaseConnection db)
    {
        var configured = db.Query("SELECT Valor FROM Configuracoes WHERE Chave='ProdutoExcluirUsuarioId' LIMIT 1;").FirstOrDefault();
        if (configured is not null) return int.TryParse(Convert.ToString(configured["Valor"]), out int saved) ? saved : null;
        var matches = db.Query("SELECT Id FROM Usuarios WHERE UPPER(TRIM(Nome))='GABRYELLE' AND UPPER(Perfil)='ADMINISTRADOR' AND Ativo=1;");
        return matches.Count == 1 ? Convert.ToInt32(matches[0]["Id"]) : null;
    }
}
