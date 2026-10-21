using System.Text.Json;
using System.Globalization;
using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MercadinhoPdv.Models;
using MercadinhoPdv.Services;

namespace MercadinhoPdv.Forms;

/// <summary>Shell web local apresentado depois da autenticação.</summary>
internal sealed class AppWebViewForm : Form
{
    private const string VirtualHost = "appassets.example";
    private static readonly JsonSerializerOptions WebJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly WebView2 _browser = new() { Dock = DockStyle.Fill };
    private readonly Task<CoreWebView2Environment> _environment;
    private readonly PdvWebBridge _pdvBridge = new();
    private readonly ManagementWebBridge _managementBridge = new();
    private readonly ProdutoService _productService = new();
    private readonly Dictionary<string, string> _pickedProductImages = new();
    private bool _initialized;
    private bool _fallbackRequested;
    private readonly System.Windows.Forms.Timer _startupTimeout = new() { Interval = 20000 };

    public AppWebViewForm() : this(CoreWebView2Environment.CreateAsync()) { }

    public AppWebViewForm(Task<CoreWebView2Environment> environment)
    {
        _environment = environment;
        Text = "Mercado · Gestão";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(900, 600);
        BackColor = Color.FromArgb(11, 17, 23);
        UiWindowChrome.Apply(this);
        Controls.Add(_browser);
        Shown += InitializeBrowser;
        _startupTimeout.Tick += (_, _) => Fallback("A interface demorou para iniciar.");
        FormClosed += (_, _) => _startupTimeout.Dispose();
    }

    private async void InitializeBrowser(object? sender, EventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            var startupWatch = Stopwatch.StartNew();
            _startupTimeout.Start();
            string webRoot = Path.Combine(AppContext.BaseDirectory, "Web");
            foreach (string asset in new[] { "home.html", "index.html", "products.html", "reports.html", "modules.html", "home.js", "app.js", "products.js", "reports.js", "modules.js", "bridge.js", "ui.js", "pdv-domain.js", "home.css", "products.css", "reports.css", "modules.css", "styles.css" })
                if (!File.Exists(Path.Combine(webRoot, asset))) throw new FileNotFoundException($"Arquivo da interface local ausente: {asset}.");
            await _browser.EnsureCoreWebView2Async(await _environment);
            if (_fallbackRequested || IsDisposed) return;
            CoreWebView2 core = _browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = true;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.SetVirtualHostNameToFolderMapping(VirtualHost, webRoot, CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals(VirtualHost, StringComparison.OrdinalIgnoreCase)) args.Cancel = true;
            };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.NavigationCompleted += (_, args) =>
            {
                _startupTimeout.Stop();
                if (!args.IsSuccess) Fallback("A página local da interface não pôde ser carregada.");
            };
            core.ProcessFailed += (_, args) =>
            {
                Diagnostics.AppLogger.Error(new InvalidOperationException($"Processo WebView2 encerrou ({args.ProcessFailedKind}, código {args.ExitCode})."), "Renderer do shell WebView2");
                Fallback("O processo da interface foi encerrado.");
            };
            core.WebMessageReceived += HandleWebMessage;
            core.Navigate($"https://{VirtualHost}/home.html");
            startupWatch.Stop();
            if (startupWatch.ElapsedMilliseconds > 750) Diagnostics.AppLogger.Slow("Inicialização da interface", startupWatch.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            Diagnostics.AppLogger.Error(exception, "Inicializar shell WebView2");
            Fallback(exception.Message);
        }
    }

    private async void HandleWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            if (!Uri.TryCreate(e.Source, UriKind.Absolute, out Uri? source) || source.Scheme != Uri.UriSchemeHttps || !source.Host.Equals(VirtualHost, StringComparison.OrdinalIgnoreCase)) return;
            string json = e.WebMessageAsJson;
            var operationWatch = Stopwatch.StartNew();
            string id = "";
            string operationName = "ação não identificada";
            bool restartAfterRestore = false;
            object response;
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                id = root.TryGetProperty("id", out JsonElement idValue) ? idValue.GetString() ?? "" : "";
                string action = root.TryGetProperty("action", out JsonElement actionValue) ? actionValue.GetString() ?? "" : "";
                operationName = action;
                if (action == "diagnostics.performance")
                {
                    ValidateEnvelope(root, id);
                    JsonElement metric = root.GetProperty("payload");
                    string page = metric.GetProperty("page").GetString() ?? "";
                    string phase = metric.GetProperty("phase").GetString() ?? "";
                    int elapsed = metric.GetProperty("milliseconds").GetInt32();
                    if (new[] { "home", "index", "products", "reports", "modules" }.Contains(page) &&
                        new[] { "navigation", "data", "ready" }.Contains(phase) && elapsed is >= 0 and <= 120000)
                        Diagnostics.AppLogger.Performance(page, phase, elapsed);
                    response = new { version = 1, id, ok = true, data = new { recorded = true } };
                }
                else if (action == "dashboard.getData")
                {
                    ValidateEnvelope(root, id);
                    response = new { version = 1, id, ok = true, data = await Task.Run(GetDashboardData) };
                }
                else if (action == "products.getData")
                {
                    ValidateEnvelope(root, id);
                    response = new { version = 1, id, ok = true, data = await Task.Run(GetProductsData) };
                }
                else if (action == "products.save")
                {
                    ValidateEnvelope(root, id);
                    var draft = root.GetProperty("payload").Deserialize<ProductDraft>(WebJsonOptions)
                        ?? throw new InvalidOperationException("Dados do produto inválidos.");
                    int savedId = await SaveProductAsync(draft);
                    response = new { version = 1, id, ok = true, data = new { productId = savedId } };
                }
                else if (action == "products.lookup")
                {
                    ValidateEnvelope(root, id);
                    Autorizacao.ExigirAdministrador();
                    string barcode = root.GetProperty("payload").GetProperty("barcode").GetString() ?? "";
                    var found = await new OpenFoodFactsService().BuscarPorCodigoAsync(barcode);
                    response = new { version = 1, id, ok = true, data = new { product = found } };
                }
                else if (action == "products.pickImage")
                {
                    ValidateEnvelope(root, id);
                    Autorizacao.ExigirAdministrador();
                    using var picker = new OpenFileDialog { Title = "Escolher imagem do produto", Filter = "Imagens (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true };
                    if (picker.ShowDialog(this) == DialogResult.OK)
                    {
                        string path = new ImagemProdutoService().ImportarArquivo(picker.FileName);
                        string token = Guid.NewGuid().ToString("N");
                        _pickedProductImages[token] = path;
                        response = new { version = 1, id, ok = true, data = new { token, fileName = Path.GetFileName(picker.FileName), imageData = ReadImageData(path) } };
                    }
                    else response = new { version = 1, id, ok = true, data = new { token = (string?)null, fileName = (string?)null } };
                }
                else if (action == "products.getImage")
                {
                    ValidateEnvelope(root, id);
                    int productId = root.GetProperty("payload").GetProperty("productId").GetInt32();
                    var product = _productService.BuscarPorId(productId) ?? throw new InvalidOperationException("Produto não encontrado.");
                    response = new { version = 1, id, ok = true, data = new { imageData = ReadImageData(product.LocalImagePath) } };
                }
                else if (action == "products.previewImage")
                {
                    ValidateEnvelope(root, id);
                    Autorizacao.ExigirAdministrador();
                    string? url = root.GetProperty("payload").GetProperty("url").GetString();
                    string? path = await new ImagemProdutoService().CachearUrlAsync(url, null);
                    response = new { version = 1, id, ok = true, data = new { imageData = ReadImageData(path) } };
                }
                else if (action == "products.setActive")
                {
                    ValidateEnvelope(root, id);
                    var payload = root.GetProperty("payload");
                    int productId = payload.GetProperty("productId").GetInt32();
                    bool active = payload.GetProperty("active").GetBoolean();
                    if (active) _productService.Reativar(productId); else _productService.Desativar(productId);
                    response = new { version = 1, id, ok = true, data = new { updated = true } };
                }
                else if (action == "products.delete")
                {
                    ValidateEnvelope(root, id);
                    _productService.Excluir(root.GetProperty("payload").GetProperty("productId").GetInt32());
                    response = new { version = 1, id, ok = true, data = new { deleted = true } };
                }
                else if (action == "reports.getData")
                {
                    ValidateEnvelope(root, id);
                    var (start, end) = ReadReportPeriod(root);
                    response = new { version = 1, id, ok = true, data = new
                    {
                        storeName = new ConfiguracaoService().ObterNomeLoja(),
                        userName = SessaoUsuario.Nome,
                        userRole = SessaoUsuario.Perfil,
                        report = await Task.Run(() => new RelatoriosService().Gerar(start, end))
                    } };
                }
                else if (action is "management.getData" or "management.details" or "management.execute")
                {
                    ValidateEnvelope(root, id);
                    JsonElement payload = root.GetProperty("payload").Clone();
                    object result = await Task.Run(() => action switch
                    {
                        "management.getData" => _managementBridge.GetData(payload),
                        "management.details" => _managementBridge.GetDetails(payload),
                        _ => _managementBridge.Execute(payload)
                    });
                    response = new { version = 1, id, ok = true, data = result };
                }
                else if (action == "management.backup")
                {
                    ValidateEnvelope(root, id);
                    Autorizacao.ExigirAdministrador();
                    using var dialog = new SaveFileDialog { Title = "Salvar cópia de segurança", Filter = "Banco de dados (*.db)|*.db", FileName = $"mercado-backup-{DateTime.Now:yyyyMMdd-HHmm}.db", AddExtension = true, DefaultExt = "db" };
                    bool saved = false;
                    if (dialog.ShowDialog(this) == DialogResult.OK) { new BackupService().Criar(dialog.FileName); saved = true; }
                    response = new { version = 1, id, ok = true, data = new { saved } };
                }
                else if (action == "management.restore")
                {
                    ValidateEnvelope(root, id);
                    Autorizacao.ExigirAdministrador();
                    using var dialog = new OpenFileDialog { Title = "Restaurar cópia de segurança", Filter = "Banco de dados (*.db;*.bak)|*.db;*.bak|Todos os arquivos (*.*)|*.*", CheckFileExists = true };
                    bool restored = false;
                    if (dialog.ShowDialog(this) == DialogResult.OK && UiDialog.Confirm(this, "Restaurar backup", "O banco atual será substituído pelo arquivo selecionado. Uma cópia de segurança do banco atual será criada automaticamente. Deseja continuar?"))
                    {
                        await Task.Run(() => new BackupService().Restaurar(dialog.FileName));
                        restored = true;
                        restartAfterRestore = true;
                    }
                    response = new { version = 1, id, ok = true, data = new { restored } };
                }
                else if (action == "reports.exportExcel")
                {
                    ValidateEnvelope(root, id);
                    var (start, end) = ReadReportPeriod(root);
                    Autorizacao.ExigirAdministrador();
                    using var dialog = new SaveFileDialog { Title = "Salvar relatório no Excel", Filter = "Pasta de trabalho do Excel (*.xlsx)|*.xlsx", FileName = $"relatorio-{start:yyyyMMdd}-{end:yyyyMMdd}.xlsx", AddExtension = true, DefaultExt = "xlsx" };
                    bool saved = false;
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        var report = new RelatoriosService().Gerar(start, end);
                        RelatorioExcelService.Salvar(report, new ConfiguracaoService().ObterNomeLoja(), dialog.FileName);
                        saved = true;
                    }
                    response = new { version = 1, id, ok = true, data = new { saved } };
                }
                else if (action == "reports.exportPdf")
                {
                    ValidateEnvelope(root, id);
                    Autorizacao.ExigirAdministrador();
                    ReadReportPeriod(root);
                    using var dialog = new SaveFileDialog { Title = "Salvar relatório em PDF", Filter = "Documento PDF (*.pdf)|*.pdf", FileName = $"relatorio-{DateTime.Now:yyyyMMdd-HHmm}.pdf", AddExtension = true, DefaultExt = "pdf" };
                    bool saved = false;
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        string temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".pdf";
                        try
                        {
                            if (!await _browser.CoreWebView2.PrintToPdfAsync(temporary)) throw new InvalidOperationException("Não foi possível gerar o PDF do relatório.");
                            File.Move(temporary, dialog.FileName, true);
                            saved = true;
                        }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                    response = new { version = 1, id, ok = true, data = new { saved } };
                }
                else if (action == "reports.print")
                {
                    ValidateEnvelope(root, id);
                    Autorizacao.ExigirAdministrador();
                    ReadReportPeriod(root);
                    _browser.CoreWebView2.ShowPrintUI();
                    response = new { version = 1, id, ok = true, data = new { saved = false } };
                }
                else if (action == "session.logout")
                {
                    ValidateEnvelope(root, id);
                    try { new AuditoriaService().Registrar("LOGOUT", "SESSAO", $"Usuário {SessaoUsuario.Nome} encerrou a sessão"); }
                    catch (Exception exception) { Diagnostics.AppLogger.Error(exception, "Registrar logout"); }
                    response = new { version = 1, id, ok = true, data = new { loggedOut = true } };
                    BeginInvoke(() => { SessaoUsuario.Encerrar(); DialogResult = DialogResult.Cancel; Close(); });
                }
                else response = await Task.Run(() => _pdvBridge.Handle(json));
            }
            catch (Exception exception)
            {
                Diagnostics.AppLogger.Error(exception, "Ação da interface WebView2");
                response = new { version = 1, id, ok = false, data = (object?)null, error = exception.Message };
            }
            _browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response, WebJsonOptions));
            operationWatch.Stop();
            if (operationWatch.ElapsedMilliseconds > 750) Diagnostics.AppLogger.Slow(operationName, operationWatch.ElapsedMilliseconds);
            if (restartAfterRestore) BeginInvoke(() => { SessaoUsuario.Encerrar(); DialogResult = DialogResult.Cancel; Close(); });
        }
        catch (Exception exception) { Diagnostics.AppLogger.Error(exception, "Responder mensagem do shell WebView2"); }
    }

    private static void ValidateEnvelope(JsonElement root, string id)
    {
        if (root.GetProperty("version").GetInt32() != 1 || string.IsNullOrWhiteSpace(id) || id.Length > 80)
            throw new InvalidOperationException("Versão ou identificador da mensagem inválido.");
    }

    private static (DateTime Start, DateTime End) ReadReportPeriod(JsonElement root)
    {
        JsonElement payload = root.GetProperty("payload");
        if (!DateTime.TryParseExact(payload.GetProperty("start").GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime start) ||
            !DateTime.TryParseExact(payload.GetProperty("end").GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime end) || end < start || end >= DateTime.MaxValue.Date)
            throw new InvalidOperationException("Informe um período válido.");
        return (start, end);
    }

    private async Task<int> SaveProductAsync(ProductDraft draft)
    {
        Autorizacao.ExigirAdministrador();
        if (draft.Id < 0) throw new InvalidOperationException("Produto inválido.");
        Produto product = draft.Id == 0 ? new Produto() : _productService.BuscarPorId(draft.Id) ?? throw new InvalidOperationException("Produto não encontrado.");
        string? oldUrl = product.ImagemUrl;
        product.Nome = draft.Nome ?? string.Empty;
        product.CodigoBarras = draft.CodigoBarras;
        product.Marca = draft.Marca;
        product.ImagemUrl = draft.RemoveImage ? null : draft.ImagemUrl;
        product.CategoriaId = draft.CategoriaId;
        product.FornecedorId = draft.FornecedorId;
        product.PrecoCusto = draft.PrecoCusto;
        product.PrecoVenda = draft.PrecoVenda;
        product.UnidadeMedida = draft.UnidadeMedida ?? "UN";
        product.EstoqueAtual = draft.EstoqueAtual;
        product.EstoqueMinimo = draft.EstoqueMinimo;
        product.DataValidade = draft.DataValidade?.Date;
        if (draft.RemoveImage) product.LocalImagePath = null;
        else if (!string.IsNullOrWhiteSpace(draft.ImageToken))
        {
            if (!_pickedProductImages.TryGetValue(draft.ImageToken, out string? path)) throw new InvalidOperationException("Selecione a imagem novamente.");
            product.LocalImagePath = path;
        }
        else if (!string.Equals(oldUrl, product.ImagemUrl, StringComparison.Ordinal)) product.LocalImagePath = null;
        if (product.LocalImagePath is null && product.ImagemUrl is not null)
            product.LocalImagePath = await new ImagemProdutoService().CachearUrlAsync(product.ImagemUrl, product.CodigoBarras);
        _productService.Salvar(product);
        if (!string.IsNullOrWhiteSpace(draft.ImageToken)) _pickedProductImages.Remove(draft.ImageToken);
        return product.Id;
    }

    private static string? ReadImageData(string? path)
    {
        if (!ImagemProdutoService.TryOpen(path, out Image? image) || image is null) return null;
        using (image)
        using (var stream = new MemoryStream())
        {
            image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
        }
    }

    private sealed record ProductDraft(int Id, string? Nome, string? CodigoBarras, string? Marca, string? ImagemUrl,
        int? CategoriaId, int? FornecedorId, decimal PrecoCusto, decimal PrecoVenda, string? UnidadeMedida,
        decimal EstoqueAtual, decimal EstoqueMinimo, DateTime? DataValidade, string? ImageToken, bool RemoveImage);

    private object GetProductsData()
    {
        var products = _productService.Listar(null);
        var categories = _productService.ListarCategorias();
        var suppliers = _productService.ListarFornecedores();
        var categoryIds = categories.Select(option => option.Id).ToHashSet();
        var supplierIds = suppliers.Select(option => option.Id).ToHashSet();
        foreach (var product in products)
        {
            if (product.CategoriaId is int categoryId && categoryIds.Add(categoryId))
                categories.Add(new OpcaoCadastro { Id = categoryId, Nome = product.CategoriaNome ?? $"Categoria #{categoryId}" });
            if (product.FornecedorId is int supplierId && supplierIds.Add(supplierId))
                suppliers.Add(new OpcaoCadastro { Id = supplierId, Nome = product.FornecedorNome ?? $"Fornecedor #{supplierId}" });
        }
        return new
        {
            storeName = new ConfiguracaoService().ObterNomeLoja(),
            userName = SessaoUsuario.Nome,
            userRole = SessaoUsuario.Perfil,
            canDelete = Autorizacao.PodeExcluirProduto(),
            categories,
            suppliers,
            products = products.Select(product => new
            {
                product.Id, product.Nome, product.CodigoBarras, product.CategoriaNome, product.UnidadeMedida,
                product.PrecoVenda, product.PrecoCusto, product.EstoqueAtual, product.EstoqueMinimo, product.DataValidade, product.Ativo,
                product.Marca, product.CategoriaId, product.FornecedorId, product.ImagemUrl,
                hasLocalImage = !string.IsNullOrWhiteSpace(product.LocalImagePath)
            }).ToList()
        };
    }

    private static object GetDashboardData()
    {
        var summary = new DashboardService().ObterResumo();
        return new
        {
            storeName = summary.NomeLoja,
            userName = SessaoUsuario.Nome,
            userRole = SessaoUsuario.Perfil,
            revenue = summary.FaturamentoHoje,
            salesToday = summary.VendasHoje,
            averageTicket = summary.TicketMedio,
            lowStockCount = summary.ItensEstoqueBaixo,
            cashOpen = summary.CaixaAberto,
            recentSales = summary.VendasRecentes.Select(sale => new { id = sale.Id, date = sale.Data, operatorName = sale.Operador, payment = sale.Pagamento, total = sale.Total }),
            lowStock = summary.ProdutosEstoqueBaixo.Select(product => new { product = product.Produto, unit = product.Unidade, stock = product.Estoque, minimum = product.Minimo }),
            expired = summary.ProdutosVencidos.Select(product => new { product = product.Produto, expiry = product.Validade, days = product.Dias }),
            expiring = summary.ProdutosValidadeProxima.Select(product => new { product = product.Produto, expiry = product.Validade, days = product.Dias })
        };
    }

    private void Fallback(string reason)
    {
        if (_fallbackRequested || IsDisposed || !IsHandleCreated) return;
        _fallbackRequested = true;
        _startupTimeout.Stop();
        BeginInvoke(() =>
        {
            if (IsDisposed || !Visible) return;
            Diagnostics.AppLogger.Error(new InvalidOperationException(reason), "Interface integrada indisponível");
            UiDialog.Warning(this, "Interface indisponível", $"A interface integrada foi interrompida. Feche e abra o Mercado novamente.\n\n{reason}\n\nDetalhes: {Diagnostics.AppLogger.LogPath}");
            DialogResult = DialogResult.Cancel;
            Close();
        });
    }
}
