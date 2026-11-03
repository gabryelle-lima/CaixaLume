namespace CaixaLume.Data;

/// <summary>New installations use CaixaLume; existing databases remain in place.</summary>
public static class CaminhosAplicacao
{
    public static string? BancoConfigurado => ReadCompatible("CAIXALUME_DATABASE", "MERCADINHOPDV_DATABASE");
    public static string? AmbienteConfigurado => ReadCompatible("CAIXALUME_AMBIENTE", "MERCADINHOPDV_AMBIENTE");
    public static string BancoProducao => ResolverBanco(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    public static string Imagens
    {
        get
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string legacy = Path.Combine(local, "MercadinhoPdv", "imagens");
            return Directory.Exists(legacy) ? legacy : Path.Combine(local, "CaixaLume", "imagens");
        }
    }
    internal static string ResolverBanco(string local)
    {
        // Never select an empty database after a branding update.
        string legacy = Path.Combine(local, "MercadinhoPdv", "dados", "mercadinho.db");
        return File.Exists(legacy) ? legacy : Path.Combine(local, "CaixaLume", "dados", "caixalume.db");
    }
    private static string? ReadCompatible(string current, string legacy)
    {
        string? value = Environment.GetEnvironmentVariable(current);
        return string.IsNullOrWhiteSpace(value) ? Environment.GetEnvironmentVariable(legacy) : value;
    }
}
