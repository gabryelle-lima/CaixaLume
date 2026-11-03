namespace CaixaLume.Models;

public static class SessaoUsuario
{
    public static int? UsuarioId { get; private set; }
    public static string Nome { get; private set; } = "";
    public static string Perfil { get; private set; } = "OPERADOR";
    public static bool Administrador => Perfil == "ADMINISTRADOR";

    public static void Iniciar(int id, string nome, string perfil)
    {
        UsuarioId = id;
        Nome = nome;
        Perfil = perfil.Trim().ToUpperInvariant() == "ADMINISTRADOR" ? "ADMINISTRADOR" : "OPERADOR";
    }

    public static void Encerrar() { UsuarioId = null; Nome = ""; Perfil = "OPERADOR"; }
}
