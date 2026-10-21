using Microsoft.Web.WebView2.Core;

namespace MercadinhoPdv;

internal static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) =>
        {
            Diagnostics.AppLogger.Error(args.Exception, "Erro não tratado na interface");
            MessageBox.Show($"Ocorreu um erro inesperado. Os detalhes foram registrados em:\n{Diagnostics.AppLogger.LogPath}\n\n{args.Exception.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        try
        {
            Data.DatabaseInitializer.Initialize();
            // Inicializa o runtime enquanto o usuário está na tela de acesso.
            Task<CoreWebView2Environment> browserEnvironment = CoreWebView2Environment.CreateAsync();
            while (true)
            {
                using (var login = new Forms.LoginForm())
                {
                    if (login.ShowDialog() != DialogResult.OK) break;
                }
                using (var app = new Forms.AppWebViewForm(browserEnvironment)) app.ShowDialog();
                if (Models.SessaoUsuario.UsuarioId is not null) break;
            }
        }
        catch (Exception exception)
        {
            Diagnostics.AppLogger.Error(exception, "Inicialização do Mercadinho PDV");
            MessageBox.Show($"Não foi possível iniciar o sistema.\n{exception.Message}\n\nDetalhes registrados em:\n{Diagnostics.AppLogger.LogPath}", "Erro ao iniciar", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }    
}
