using MercadinhoPdv.Data;

namespace MercadinhoPdv.Services;

public sealed class BackupService
{
    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    public void Criar(string destination)
    {
        Autorizacao.ExigirAdministrador();
        if (!File.Exists(DatabaseInitializer.DatabasePath)) throw new FileNotFoundException("O banco de dados ainda não existe.");
        destination = Path.GetFullPath(destination);
        if (string.Equals(destination, DatabaseInitializer.DatabasePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Escolha um arquivo diferente do banco em uso.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var db = new DatabaseConnection(DatabaseInitializer.DatabasePath)) db.BackupTo(temporary);
            Validar(temporary);
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Restaurar(string source)
    {
        Autorizacao.ExigirAdministrador();
        source = Path.GetFullPath(source); Validar(source);
        if (string.Equals(source, DatabaseInitializer.DatabasePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Selecione um arquivo de backup diferente do banco em uso.");
        string safetyCopy = DatabaseInitializer.DatabasePath + ".antes-restauracao-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
        using (var current = new DatabaseConnection(DatabaseInitializer.DatabasePath)) current.BackupTo(safetyCopy);
        try
        {
            using (var backup = new DatabaseConnection(source)) backup.BackupTo(DatabaseInitializer.DatabasePath, overwrite: true);
            Validar(DatabaseInitializer.DatabasePath);
        }
        catch
        {
            using var safety = new DatabaseConnection(safetyCopy);
            safety.BackupTo(DatabaseInitializer.DatabasePath, overwrite: true);
            throw;
        }
    }

    private static void Validar(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < SqliteHeader.Length) throw new InvalidOperationException("Arquivo de backup inválido.");
        using var stream = File.OpenRead(path); byte[] header = new byte[SqliteHeader.Length]; stream.ReadExactly(header);
        if (!header.SequenceEqual(SqliteHeader)) throw new InvalidOperationException("O arquivo selecionado não é um banco SQLite válido.");
        using var database = new DatabaseConnection(path);
        var integrity = database.Query("PRAGMA integrity_check;");
        if (integrity.Count != 1 || !string.Equals(Convert.ToString(integrity[0].GetValueOrDefault("integrity_check")), "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O banco selecionado falhou na verificação de integridade.");
        if (database.Query("PRAGMA foreign_key_check;").Count != 0) throw new InvalidOperationException("O banco selecionado contém referências inconsistentes e não pode ser restaurado.");
    }
}
