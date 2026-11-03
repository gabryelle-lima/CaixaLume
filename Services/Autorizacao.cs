using CaixaLume.Data;
using CaixaLume.Models;

namespace CaixaLume.Services;

public static class Autorizacao
{
    public static int ExigirUsuario(bool administrador = false)
    {
        using var connection = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        return ExigirUsuario(connection, administrador);
    }

    internal static int ExigirUsuario(DatabaseConnection db, bool administrador = false)
    {
        int id = SessaoUsuario.UsuarioId ?? throw new InvalidOperationException("Entre no sistema para continuar.");

        var user = db.Query("SELECT Perfil FROM Usuarios WHERE Id=@Id AND Ativo=1;", new Dictionary<string, object?> { ["@Id"] = id }).FirstOrDefault()
            ?? throw new InvalidOperationException("O usuário não está ativo. Entre novamente.");
        if (administrador && !string.Equals(Convert.ToString(user["Perfil"])?.Trim(), "ADMINISTRADOR", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Somente administradores podem executar esta operação.");
        return id;
    }

    public static void ExigirAdministrador() => ExigirUsuario(administrador: true);

    public static bool PodeExcluirProduto()
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        return PodeExcluirProduto(db);
    }

    private static bool PodeExcluirProduto(DatabaseConnection db)
    {
        if (SessaoUsuario.UsuarioId is not int id) return false;
        var user = db.Query("SELECT Perfil FROM Usuarios WHERE Id=@Id AND Ativo=1;", new Dictionary<string, object?> { ["@Id"] = id }).FirstOrDefault();
        if (user is null || !string.Equals(Convert.ToString(user["Perfil"])?.Trim(), "ADMINISTRADOR", StringComparison.OrdinalIgnoreCase)) return false;
        string? individual = Valor(db, $"Permissao:PRODUTO_EXCLUIR:Usuario:{id}");
        return individual is not null ? individual == "1" : Valor(db, "Permissao:PRODUTO_EXCLUIR:Perfil:ADMINISTRADOR") == "1";
    }

    public static void ExigirExclusaoProduto()
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        ExigirExclusaoProduto(db);
    }

    internal static void ExigirExclusaoProduto(DatabaseConnection db)
    {
        ExigirUsuario(db, administrador: true);
        if (!PodeExcluirProduto(db)) throw new InvalidOperationException("Esta conta não tem permissão para excluir produtos.");
    }

    public static void ConfigurarPermissaoUsuario(int usuarioId, bool? permitirExcluirProduto)
        => Configurar($"Usuario:{usuarioId}", permitirExcluirProduto, usuarioId);

    public static void ConfigurarPermissaoPerfil(string perfil, bool permitirExcluirProduto)
    {
        perfil = (perfil ?? "").Trim().ToUpperInvariant();
        if (perfil is not ("ADMINISTRADOR" or "OPERADOR")) throw new InvalidOperationException("Perfil inválido.");
        Configurar($"Perfil:{perfil}", permitirExcluirProduto, null);
    }

    private static void Configurar(string destino, bool? permitir, int? usuarioId)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        db.Execute("BEGIN IMMEDIATE;");
        try
        {
            int ator = ExigirUsuario(db, administrador: true);
            if (usuarioId.HasValue && !db.Query("SELECT 1 FROM Usuarios WHERE Id=@Id;", new Dictionary<string, object?> { ["@Id"] = usuarioId }).Any())
                throw new InvalidOperationException("Usuário não encontrado.");
            var args = new Dictionary<string, object?> { ["@Chave"] = $"Permissao:PRODUTO_EXCLUIR:{destino}", ["@Valor"] = permitir == true ? "1" : "0" };
            if (permitir is null) db.Execute("DELETE FROM Configuracoes WHERE Chave=@Chave;", new Dictionary<string, object?> { ["@Chave"] = args["@Chave"] });
            else db.Execute("INSERT INTO Configuracoes (Chave,Valor) VALUES (@Chave,@Valor) ON CONFLICT(Chave) DO UPDATE SET Valor=excluded.Valor;", args);
            db.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Id,@Data,'PERMISSAO','USUARIO',@Descricao);",
                new Dictionary<string, object?> { ["@Id"] = ator, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Descricao"] = $"PRODUTO_EXCLUIR; {destino}; {permitir?.ToString() ?? "HERDAR"}" });
            db.Execute("COMMIT;");
        }
        catch { db.Execute("ROLLBACK;"); throw; }
    }

    private static string? Valor(DatabaseConnection db, string chave)
    {
        var row = db.Query("SELECT Valor FROM Configuracoes WHERE Chave=@Chave;", new Dictionary<string, object?> { ["@Chave"] = chave }).SingleOrDefault();
        return row is null ? null : Convert.ToString(row["Valor"]);
    }
}
