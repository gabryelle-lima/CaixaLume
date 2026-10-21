using MercadinhoPdv.Data;
using MercadinhoPdv.Forms;
using MercadinhoPdv.Models;
using MercadinhoPdv.Services;

internal static class UiFixture
{
    internal static int Run(string[] args)
    {
        string path = Path.GetFullPath(args[1]);
        if (!path.EndsWith(".ui-test.db", StringComparison.OrdinalIgnoreCase) || File.Exists(path))
            throw new InvalidOperationException("Informe um arquivo novo com sufixo .ui-test.db para a inspeção visual isolada.");
        Environment.SetEnvironmentVariable("MERCADINHOPDV_DATABASE", path);
        DatabaseInitializer.Initialize();
        var users = new UsuarioService();
        users.CriarAdministradorInicial("Administrador de teste", "teste-ui", "senha-teste-ui-123");
        var admin = users.Autenticar("teste-ui", "senha-teste-ui-123")!.Value;
        SessaoUsuario.Iniciar(admin.Id, admin.Nome, admin.Perfil);
        new ConfiguracaoService().SalvarNomeLoja("AMBIENTE DE TESTES");
        var products = new ProdutoService();
        products.Salvar(new Produto { Nome = "LEITE DE TESTE", CodigoBarras = "7891234567890", PrecoVenda = 4.50m, PrecoCusto = 3, EstoqueAtual = 20, EstoqueMinimo = 5, DataValidade = DateTime.Today.AddDays(7) });
        products.Salvar(new Produto { Nome = "CARNE DE TESTE", CodigoBarras = "7899876543210", UnidadeMedida = "KG", PrecoVenda = 19.90m, PrecoCusto = 10, EstoqueAtual = 5 });
        products.Salvar(new Produto { Nome = "VALIDADE VENCIDA TESTE", PrecoVenda = 2, EstoqueAtual = 1, DataValidade = DateTime.Today.AddDays(-2) });
        new CaixaService().Abrir(50);
        int width = args.Length > 2 ? int.Parse(args[2]) : 1280;
        int height = args.Length > 3 ? int.Parse(args[3]) : 720;
        Exception? failure = null;
        var ui = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                InicializacaoInterface.Executar(
                    () => { using var form = new AppWebViewForm { Text = "Mercado · TESTE ISOLADO", WindowState = FormWindowState.Normal, Size = new Size(width, height) }; return form.ShowDialog(); },
                    () => { using var form = new MainForm { Text = "Mercado · TESTE ISOLADO WinForms", Size = new Size(width, height) }; return form.ShowDialog(); });
            }
            catch (Exception exception) { failure = exception; }
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start(); ui.Join();
        if (failure is not null) throw failure;
        return 0;
    }
}
