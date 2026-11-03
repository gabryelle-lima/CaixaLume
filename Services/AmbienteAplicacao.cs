using CaixaLume.Data;

namespace CaixaLume.Services;

public static class AmbienteAplicacao
{
    public static bool FerramentasTesteDisponiveis
    {
        get
        {
#if DEBUG
            string? configured = CaixaLume.Data.CaminhosAplicacao.BancoConfigurado;
            string production = CaminhosAplicacao.BancoProducao;
            return CaixaLume.Data.CaminhosAplicacao.AmbienteConfigurado == "DESENVOLVIMENTO"
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
