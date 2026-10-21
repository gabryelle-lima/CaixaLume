using MercadinhoPdv.Data;

namespace MercadinhoPdv.Services;

public static class AmbienteAplicacao
{
    public static bool FerramentasTesteDisponiveis
    {
        get
        {
#if DEBUG
            string? configured = Environment.GetEnvironmentVariable("MERCADINHOPDV_DATABASE");
            string production = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MercadinhoPdv", "dados", "mercadinho.db");
            return Environment.GetEnvironmentVariable("MERCADINHOPDV_AMBIENTE") == "DESENVOLVIMENTO"
                && !string.IsNullOrWhiteSpace(configured)
                && string.Equals(Path.GetFullPath(configured), DatabaseInitializer.DatabasePath, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(DatabaseInitializer.DatabasePath, production, StringComparison.OrdinalIgnoreCase)
                && File.Exists(DatabaseInitializer.DatabasePath + ".descartavel");
#else
            return false;
#endif
        }
    }

    public static void ExigirBancoDescartavel()
    {
        if (!FerramentasTesteDisponiveis)
            throw new InvalidOperationException("A limpeza está bloqueada. Use somente uma compilação de desenvolvimento com banco descartável explicitamente identificado.");
        Autorizacao.ExigirAdministrador();
    }
}
