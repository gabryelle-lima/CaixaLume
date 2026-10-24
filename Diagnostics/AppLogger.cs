using System.Collections.Concurrent;
using System.Text;

namespace MercadinhoPdv.Diagnostics;

internal static class AppLogger
{
    private static readonly object FileGate = new();
    private static readonly ConcurrentQueue<string> PendingMetrics = new();
    private static int _drainingMetrics;

    public static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MercadinhoPdv", "logs", "application.log");

    public static void Error(Exception exception, string operation)
    {
        try
        {
            lock (FileGate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {operation}{Environment.NewLine}{exception}{Environment.NewLine}{new string('-', 72)}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { /* O registro de diagnóstico não deve impedir a operação principal. */ }
    }

    public static void Slow(string operation, long milliseconds)
    {
        QueueMetric($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Operação lenta: {operation} ({milliseconds} ms){Environment.NewLine}");
    }

    public static void Performance(string page, string phase, int milliseconds)
    {
        QueueMetric($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Tempo: {page} · {phase} · {milliseconds} ms{Environment.NewLine}");
    }

    private static void QueueMetric(string entry)
    {
        PendingMetrics.Enqueue(entry);
        StartDrain();
    }

    private static void StartDrain()
    {
        if (Interlocked.CompareExchange(ref _drainingMetrics, 1, 0) == 0)
            ThreadPool.QueueUserWorkItem(static _ => DrainMetrics());
    }

    private static void DrainMetrics()
    {
        try
        {
            var batch = new StringBuilder();
            while (PendingMetrics.TryDequeue(out string? entry)) batch.Append(entry);
            if (batch.Length == 0) return;
            lock (FileGate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, batch.ToString(), Encoding.UTF8);
            }
        }
        catch { /* Métricas não devem bloquear a operação principal. */ }
        finally
        {
            Interlocked.Exchange(ref _drainingMetrics, 0);
            if (!PendingMetrics.IsEmpty) StartDrain();
        }
    }
}
