using Microsoft.Web.WebView2.Core;
using System.Security.Cryptography;
using System.Text;

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
            string databasePath = Path.GetFullPath(Data.DatabaseInitializer.DatabasePath).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
            string databaseKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(databasePath)));
            using var instance = new Mutex(true, @"Local\CaixaLume-" + databaseKey, out bool firstInstance);
            if (!firstInstance)
            {
                MessageBox.Show("O CaixaLume já está aberto com este banco de dados. Volte à janela existente antes de continuar.",
                    "CaixaLume já aberto", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Data.DatabaseInitializer.Initialize();
            // O perfil do navegador precisa ficar em uma pasta gravável também na instalação.
            // Inicializa o runtime enquanto o usuário está na tela de acesso.
            bool testEnvironment = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MERCADINHOPDV_DATABASE"));
            string browserData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MercadinhoPdv", testEnvironment ? "webview2-teste-" + databaseKey[..12] : "webview2");
            Directory.CreateDirectory(browserData);
            Task<CoreWebView2Environment> browserEnvironment = CoreWebView2Environment.CreateAsync(userDataFolder: browserData);
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
