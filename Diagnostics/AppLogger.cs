using System.Text;

namespace MercadinhoPdv.Diagnostics;

internal static class AppLogger
{
    public static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MercadinhoPdv", "logs", "application.log");

    public static void Error(Exception exception, string operation)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {operation}{Environment.NewLine}{exception}{Environment.NewLine}{new string('-', 72)}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { /* O registro de diagnóstico não deve impedir a operação principal. */ }
    }

    public static void Slow(string operation, long milliseconds)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Operação lenta: {operation} ({milliseconds} ms){Environment.NewLine}", Encoding.UTF8);
        }
        catch { /* O diagnóstico não deve impedir a operação principal. */ }
    }

    public static void Performance(string page, string phase, int milliseconds)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Tempo: {page} · {phase} · {milliseconds} ms{Environment.NewLine}", Encoding.UTF8);
        }
        catch { /* O diagnóstico não deve impedir a operação principal. */ }
    }
}
