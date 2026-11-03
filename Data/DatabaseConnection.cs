using System.Globalization;
using System.Runtime.InteropServices;

namespace CaixaLume.Data;

/// <summary>Pequena ponte para o SQLite nativo já disponível no Windows.</summary>
internal sealed class DatabaseConnection : IDisposable
{
    private const int SqliteOk = 0;
    private const int SqliteRow = 100;
    private const int SqliteDone = 101;
    private const int OpenReadWrite = 0x00000002;
    private const int OpenCreate = 0x00000004;
    private const int OpenFullMutex = 0x00010000;
    private static readonly IntPtr SqliteTransient = new(-1);
    private IntPtr _database;

    public DatabaseConnection(string databasePath)
    {
        int result = sqlite3_open_v2(databasePath, out _database, OpenReadWrite | OpenCreate | OpenFullMutex, IntPtr.Zero);
        if (result != SqliteOk)
        {
            Dispose();
            throw new InvalidOperationException($"Não foi possível abrir o banco SQLite. Código: {result}.");
        }
        try
        {
            // A interface pode consultar e gravar em conexões diferentes ao mesmo tempo.
            // Aguarde a transação em curso antes de declarar o banco ocupado.
            if (sqlite3_busy_timeout(_database, 5000) != SqliteOk) ThrowDatabaseError();
            Execute("PRAGMA foreign_keys = ON;");
            // Confirmações duráveis e recuperação do journal após encerramento do processo.
            Execute("PRAGMA synchronous = FULL;");
            if (Convert.ToInt64(Query("PRAGMA foreign_keys;").Single().Values.Single()) != 1)
                throw new InvalidOperationException("A integridade referencial do SQLite não pôde ser habilitada.");
        }
        catch { Dispose(); throw; }
    }

    public void Execute(string sql, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        IntPtr statement = Prepare(sql);
        try
        {
            BindParameters(statement, parameters);
            if (sqlite3_step(statement) != SqliteDone) ThrowDatabaseError();
        }
        finally { sqlite3_finalize(statement); }
    }

    public long LastInsertRowId => sqlite3_last_insert_rowid(_database);
    public int Changes => sqlite3_changes(_database);

    /// <summary>Cria um snapshot consistente do banco usando a API nativa de backup do SQLite.</summary>
    public void BackupTo(string destinationPath, bool overwrite = false)
    {
        destinationPath = Path.GetFullPath(destinationPath);
        if (!Directory.Exists(Path.GetDirectoryName(destinationPath)!)) Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        bool fileExisted = File.Exists(destinationPath);
        if (fileExisted && !overwrite) throw new IOException("O arquivo de destino do backup já existe.");
        IntPtr destination = IntPtr.Zero;
        IntPtr backup = IntPtr.Zero;
        bool completed = false;
        try
        {
            int openResult = sqlite3_open_v2(destinationPath, out destination, OpenReadWrite | OpenCreate | OpenFullMutex, IntPtr.Zero);
            if (openResult != SqliteOk) throw new InvalidOperationException($"Não foi possível criar o snapshot SQLite. Código: {openResult}.");
            backup = sqlite3_backup_init(destination, "main", _database, "main");
            if (backup == IntPtr.Zero) throw new InvalidOperationException(Marshal.PtrToStringUTF8(sqlite3_errmsg(destination)) ?? "Não foi possível iniciar o backup SQLite.");
            int stepResult = sqlite3_backup_step(backup, -1);
            int finishResult = sqlite3_backup_finish(backup);
            backup = IntPtr.Zero;
            if (stepResult != SqliteDone || finishResult != SqliteOk)
                throw new InvalidOperationException(Marshal.PtrToStringUTF8(sqlite3_errmsg(destination)) ?? $"O snapshot SQLite falhou (step={stepResult}, finish={finishResult}).");
            completed = true;
        }
        finally
        {
            if (backup != IntPtr.Zero) sqlite3_backup_finish(backup);
            if (destination != IntPtr.Zero) sqlite3_close_v2(destination);
            if (!completed && !fileExisted && File.Exists(destinationPath)) File.Delete(destinationPath);
        }
    }

    // Usado somente por scripts internos de criação; operações com dados usam Execute parametrizado.
    public void ExecuteScript(string sql)
    {
        int result = sqlite3_exec(_database, sql, IntPtr.Zero, IntPtr.Zero, out IntPtr errorMessage);
        if (result == SqliteOk) return;
        string message = errorMessage == IntPtr.Zero ? "Erro no banco SQLite." : Marshal.PtrToStringUTF8(errorMessage) ?? "Erro no banco SQLite.";
        if (errorMessage != IntPtr.Zero) sqlite3_free(errorMessage);
        throw new InvalidOperationException(message);
    }

    public List<Dictionary<string, object?>> Query(string sql, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        IntPtr statement = Prepare(sql);
        try
        {
            BindParameters(statement, parameters);
            var rows = new List<Dictionary<string, object?>>();
            int result;
            while ((result = sqlite3_step(statement)) == SqliteRow)
            {
                var row = new Dictionary<string, object?>();
                for (int column = 0; column < sqlite3_column_count(statement); column++)
                {
                    string name = Marshal.PtrToStringUTF8(sqlite3_column_name(statement, column))!;
                    row[name] = ReadColumn(statement, column);
                }
                rows.Add(row);
            }
            if (result != SqliteDone) ThrowDatabaseError();
            return rows;
        }
        finally { sqlite3_finalize(statement); }
    }

    private IntPtr Prepare(string sql)
    {
        if (sqlite3_prepare_v2(_database, sql, -1, out IntPtr statement, IntPtr.Zero) != SqliteOk) ThrowDatabaseError();
        return statement;
    }

    private void BindParameters(IntPtr statement, IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null) return;
        foreach (var parameter in parameters)
        {
            int index = sqlite3_bind_parameter_index(statement, parameter.Key);
            if (index == 0) throw new InvalidOperationException($"Parâmetro não encontrado: {parameter.Key}.");
            object? value = parameter.Value;
            int result = value switch
            {
                null => sqlite3_bind_null(statement, index),
                int number => sqlite3_bind_int64(statement, index, number),
                long number => sqlite3_bind_int64(statement, index, number),
                bool flag => sqlite3_bind_int64(statement, index, flag ? 1 : 0),
                decimal number => sqlite3_bind_text(statement, index, number.ToString(CultureInfo.InvariantCulture), -1, SqliteTransient),
                double number => sqlite3_bind_double(statement, index, number),
                DateTime date => sqlite3_bind_text(statement, index, date.ToString("yyyy-MM-dd"), -1, SqliteTransient),
                _ => sqlite3_bind_text(statement, index, value.ToString()!, -1, SqliteTransient)
            };
            if (result != SqliteOk) ThrowDatabaseError();
        }
    }

    private object? ReadColumn(IntPtr statement, int column) => sqlite3_column_type(statement, column) switch
    {
        1 => sqlite3_column_int64(statement, column),
        2 => sqlite3_column_double(statement, column),
        3 => Marshal.PtrToStringUTF8(sqlite3_column_text(statement, column)),
        _ => null
    };

    private void ThrowDatabaseError() => throw new InvalidOperationException(Marshal.PtrToStringUTF8(sqlite3_errmsg(_database)) ?? "Erro no banco SQLite.");
    public void Dispose() { if (_database != IntPtr.Zero) { sqlite3_close_v2(_database); _database = IntPtr.Zero; } }

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr database, int flags, IntPtr zVfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr database, int milliseconds);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_exec(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr callbackArgument, out IntPtr errorMessage);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void sqlite3_free(IntPtr pointer);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_errmsg(IntPtr database);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_parameter_index(IntPtr statement, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_null(IntPtr statement, int index);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_double(IntPtr statement, int index, double value);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_text(IntPtr statement, int index, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int bytes, IntPtr destructor);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_count(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_name(IntPtr statement, int index);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_type(IntPtr statement, int index);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern long sqlite3_column_int64(IntPtr statement, int index);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern double sqlite3_column_double(IntPtr statement, int index);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int index);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close_v2(IntPtr database);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_backup_init(IntPtr destination, [MarshalAs(UnmanagedType.LPUTF8Str)] string destinationName, IntPtr source, [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceName);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_backup_step(IntPtr backup, int pages);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_backup_finish(IntPtr backup);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern long sqlite3_last_insert_rowid(IntPtr database);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_changes(IntPtr database);
}
