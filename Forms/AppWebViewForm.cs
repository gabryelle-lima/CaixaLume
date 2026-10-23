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
    private readonly SemaphoreSlim _operationTurnstile = new(1, 1);
    private readonly SemaphoreSlim _operationReadersGate = new(1, 1);
    private readonly SemaphoreSlim _operationResource = new(1, 1);
    private int _activeOperations;
    private readonly Dictionary<string, string> _pickedProductImages = new();
    private readonly HashSet<ulong> _blockedNavigationIds = new();
    private readonly Label _loading = new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(11, 17, 23),
        ForeColor = Color.FromArgb(175, 191, 207),
        Font = new Font("Segoe UI", 15F, FontStyle.Regular),
        TextAlign = ContentAlignment.MiddleCenter,
        Text = "Preparando o Mercado..."
    };
    private bool _initialized;
    private bool _startupReady;
    private bool _fallbackRequested;
    private bool _sessionEnding;
    private int _startupNavigationRetries;
    private readonly Stopwatch _startupWatch = new();
    private readonly System.Windows.Forms.Timer _startupTimeout = new() { Interval = 20000 };

    public AppWebViewForm() : this(CoreWebView2Environment.CreateAsync()) { }

    public AppWebViewForm(Task<CoreWebView2Environment> environment)
    {
        _environment = environment;
        Text = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MERCADINHOPDV_DATABASE"))
            ? "Mercado · Gestão"
            : "AMBIENTE DE TESTE  |  Mercado · Gestão";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(900, 600);
        BackColor = Color.FromArgb(11, 17, 23);
        UiWindowChrome.Apply(this);
        Controls.Add(_browser);
        Controls.Add(_loading);
        Shown += InitializeBrowser;
        _startupTimeout.Tick += (_, _) =>
        {
            if (!_startupReady) Fallback("A interface não respondeu dentro de 20 segundos.");
        };
        FormClosed += (_, _) => _startupTimeout.Dispose();
    }

    private async void InitializeBrowser(object? sender, EventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            _startupWatch.Start();
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
                if (!IsLocalPage(args.Uri))
                {
                    args.Cancel = true;
                    if (_blockedNavigationIds.Count > 128) _blockedNavigationIds.Clear();
                    _blockedNavigationIds.Add(args.NavigationId);
                }
            };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.NavigationCompleted += (_, args) =>
            {
                if (_fallbackRequested || IsDisposed) return;
                if (_blockedNavigationIds.Remove(args.NavigationId)) return;
                if (args.IsSuccess)
                {
                    if (_startupReady) return;
                    _startupReady = true;
                    _startupTimeout.Stop();
                    _startupWatch.Stop();
                    _loading.Visible = false;
                    if (_startupWatch.ElapsedMilliseconds > 750)
                        Diagnostics.AppLogger.Slow("Inicialização da interface", _startupWatch.ElapsedMilliseconds);
                    return;
                }
                Diagnostics.AppLogger.Error(
                    new InvalidOperationException($"Navegação local falhou: {args.WebErrorStatus}."),
                    "Carregar interface WebView2");
                if (!_startupReady && _startupNavigationRetries++ == 0)
                {
                    core.Navigate($"https://{VirtualHost}/home.html");
                    return;
                }
                Fallback($"A página local da interface não pôde ser carregada ({args.WebErrorStatus}).");
            };
            core.ProcessFailed += (_, args) =>
            {
                Diagnostics.AppLogger.Error(new InvalidOperationException($"Processo WebView2 encerrou ({args.ProcessFailedKind}, código {args.ExitCode})."), "Renderer do shell WebView2");
                // Falhas de GPU e processos auxiliares são recuperadas pelo próprio WebView2.
                // Encerrar o caixa por elas pode interromper uma venda sem necessidade.
                if (args.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited)
                    Fallback("O processo principal da interface foi encerrado.");
            };
            core.WebMessageReceived += HandleWebMessage;
            core.Navigate($"https://{VirtualHost}/home.html");
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
            if (!IsLocalPage(e.Source)) return;
            string json = e.WebMessageAsJson;
            var operationWatch = Stopwatch.StartNew();
            string id = "";
            string operationName = "ação não identificada";
            bool restartAfterRestore = false;
            object response;
            bool regularOperationHeld = false;
            bool restoreHeld = false;
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                id = root.TryGetProperty("id", out JsonElement idValue) ? idValue.GetString() ?? "" : "";
                string action = root.TryGetProperty("action", out JsonElement actionValue) ? actionValue.GetString() ?? "" : "";
                operationName = action;
                if (action == "management.restore")
                {
                    await EnterRestoreAsync();
                    restoreHeld = true;
                }
                else if (action != "diagnostics.performance")
                {
                    await EnterRegularOperationAsync();
                    regularOperationHeld = true;
                }
                if (_sessionEnding && action != "diagnostics.performance")
                    throw new InvalidOperationException("A sessão está sendo encerrada. Entre novamente para continuar.");
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
                        string selectedFile = picker.FileName;
                        string path = await Task.Run(() => new ImagemProdutoService().ImportarArquivo(selectedFile));
                        string token = Guid.NewGuid().ToString("N");
                        _pickedProductImages[token] = path;
                        string? imageData = await Task.Run(() => ReadImageData(path));
                        response = new { version = 1, id, ok = true, data = new { token, fileName = Path.GetFileName(selectedFile), imageData } };
                    }
                    else response = new { version = 1, id, ok = true, data = new { token = (string?)null, fileName = (string?)null } };
                }
                else if (action == "products.getImage")
                {
                    ValidateEnvelope(root, id);
                    int productId = root.GetProperty("payload").GetProperty("productId").GetInt32();
                    string? imageData = await Task.Run(() =>
                    {
                        var product = _productService.BuscarPorId(productId) ?? throw new InvalidOperationException("Produto não encontrado.");
                        return ReadImageData(product.LocalImagePath);
                    });
                    response = new { version = 1, id, ok = true, data = new { imageData } };
                }
                else if (action == "products.previewImage")
                {
                    ValidateEnvelope(root, id);
                    Autorizacao.ExigirAdministrador();
                    string? url = root.GetProperty("payload").GetProperty("url").GetString();
                    string? path = await new ImagemProdutoService().CachearUrlAsync(url, null);
                    string? imageData = await Task.Run(() => ReadImageData(path));
                    response = new { version = 1, id, ok = true, data = new { imageData } };
                }
                else if (action == "products.setActive")
                {
                    ValidateEnvelope(root, id);
                    var payload = root.GetProperty("payload");
                    int productId = payload.GetProperty("productId").GetInt32();
                    bool active = payload.GetProperty("active").GetBoolean();
                    await Task.Run(() =>
                    {
                        if (active) _productService.Reativar(productId); else _productService.Desativar(productId);
                    });
                    response = new { version = 1, id, ok = true, data = new { updated = true } };
                }
                else if (action == "products.delete")
                {
                    ValidateEnvelope(root, id);
                    int productId = root.GetProperty("payload").GetProperty("productId").GetInt32();
                    await Task.Run(() => _productService.Excluir(productId));
                    response = new { version = 1, id, ok = true, data = new { deleted = true } };
                }
                else if (action == "reports.getData")
                {
                    ValidateEnvelope(root, id);
                    var (start, end) = ReadReportPeriod(root);
                    string? userName = SessaoUsuario.Nome;
                    string? userRole = SessaoUsuario.Perfil;
                    object reportData = await Task.Run(() => new
                    {
                        storeName = new ConfiguracaoService().ObterNomeLoja(),
                        userName,
                        userRole,
                        report = new RelatoriosService().Gerar(start, end)
                    });
                    response = new { version = 1, id, ok = true, data = reportData };
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
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        string destination = dialog.FileName;
                        await Task.Run(() => new BackupService().Criar(destination));
                        saved = true;
                    }
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
                        string sourceFile = dialog.FileName;
                        _sessionEnding = true;
                        restartAfterRestore = true;
                        await Task.Run(() => new BackupService().Restaurar(sourceFile));
                        restored = true;
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
                        string destination = dialog.FileName;
                        await Task.Run(() =>
                        {
                            var report = new RelatoriosService().Gerar(start, end);
                            RelatorioExcelService.Salvar(report, new ConfiguracaoService().ObterNomeLoja(), destination);
                        });
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
                            string destination = dialog.FileName;
                            await Task.Run(() => File.Move(temporary, destination, true));
                            saved = true;
                        }
                        finally { await Task.Run(() => { if (File.Exists(temporary)) File.Delete(temporary); }); }
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
                    string? userName = SessaoUsuario.Nome;
                    try { await Task.Run(() => new AuditoriaService().Registrar("LOGOUT", "SESSAO", $"Usuário {userName} encerrou a sessão")); }
                    catch (Exception exception) { Diagnostics.AppLogger.Error(exception, "Registrar logout"); }
                    response = new { version = 1, id, ok = true, data = new { loggedOut = true } };
                    _sessionEnding = true;
                    BeginInvoke(() => { SessaoUsuario.Encerrar(); DialogResult = DialogResult.Cancel; Close(); });
                }
                else response = await Task.Run(() => _pdvBridge.Handle(json));
            }
            catch (Exception exception)
            {
                Diagnostics.AppLogger.Error(exception, "Ação da interface WebView2");
                if (restartAfterRestore && !IsDisposed)
                    UiDialog.Warning(this, "Restauração interrompida", "Não foi possível confirmar a restauração. O Mercado será fechado para proteger os dados. Confira o registro do erro e a cópia de segurança antes de abrir novamente.");
                response = new { version = 1, id, ok = false, data = (object?)null, error = exception.Message };
            }
            finally
            {
                if (restoreHeld) ExitRestore();
                else if (regularOperationHeld) await ExitRegularOperationAsync();
            }
            try
            {
                if (!IsDisposed && _browser.CoreWebView2 is not null)
                    _browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response, WebJsonOptions));
                operationWatch.Stop();
                if (operationWatch.ElapsedMilliseconds > 750) Diagnostics.AppLogger.Slow(operationName, operationWatch.ElapsedMilliseconds);
            }
            finally
            {
                if (restartAfterRestore && !IsDisposed && IsHandleCreated)
                    BeginInvoke(() => { if (IsDisposed) return; SessaoUsuario.Encerrar(); DialogResult = DialogResult.Cancel; Close(); });
            }
        }
        catch (Exception exception) { Diagnostics.AppLogger.Error(exception, "Responder mensagem do shell WebView2"); }
    }

    private static void ValidateEnvelope(JsonElement root, string id)
    {
        if (root.GetProperty("version").GetInt32() != 1 || string.IsNullOrWhiteSpace(id) || id.Length > 80)
            throw new InvalidOperationException("Versão ou identificador da mensagem inválido.");
    }

    private static bool IsLocalPage(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && uri.UserInfo.Length == 0 &&
        uri.Host.Equals(VirtualHost, StringComparison.OrdinalIgnoreCase);

    private async Task EnterRegularOperationAsync()
    {
        // A restauração segura a catraca enquanto aguarda todas as ações ativas.
        await _operationTurnstile.WaitAsync();
        _operationTurnstile.Release();
        await _operationReadersGate.WaitAsync();
        try
        {
            if (++_activeOperations == 1) await _operationResource.WaitAsync();
        }
        finally { _operationReadersGate.Release(); }
    }

    private async Task ExitRegularOperationAsync()
    {
        await _operationReadersGate.WaitAsync();
        try
        {
            if (--_activeOperations == 0) _operationResource.Release();
        }
        finally { _operationReadersGate.Release(); }
    }

    private async Task EnterRestoreAsync()
    {
        await _operationTurnstile.WaitAsync();
        try { await _operationResource.WaitAsync(); }
        catch { _operationTurnstile.Release(); throw; }
    }

    private void ExitRestore()
    {
        _operationResource.Release();
        _operationTurnstile.Release();
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
        Produto product;
        if (draft.Id == 0) product = new Produto();
        else product = await Task.Run(() => _productService.BuscarPorId(draft.Id))
            ?? throw new InvalidOperationException("Produto não encontrado.");
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
        await Task.Run(() => _productService.Salvar(product));
        if (!string.IsNullOrWhiteSpace(draft.ImageToken)) _pickedProductImages.Remove(draft.ImageToken);
        return product.Id;
    }

    private static string? ReadImageData(string? path)
    {
        if (!ImagemProdutoService.TryOpen(path, out Image? image) || image is null) return null;
        using (image)
        using (var stream = new MemoryStream())
        {
            const int previewMaxDimension = 900;
            double scale = Math.Min(1d, (double)previewMaxDimension / Math.Max(image.Width, image.Height));
            if (scale < 1d)
            {
                using var preview = new Bitmap(Math.Max(1, (int)Math.Round(image.Width * scale)), Math.Max(1, (int)Math.Round(image.Height * scale)));
                using (var graphics = Graphics.FromImage(preview))
                {
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(image, new Rectangle(0, 0, preview.Width, preview.Height));
                }
                preview.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            }
            else image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
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
        _sessionEnding = true;
        _startupTimeout.Stop();
        BeginInvoke(() =>
        {
            if (IsDisposed || !Visible) return;
            Diagnostics.AppLogger.Error(new InvalidOperationException(reason), "Interface integrada indisponível");
            string guidance = _startupReady
                ? "Ao reabrir, confira o histórico de vendas antes de repetir um pagamento."
                : "Confira se os arquivos da instalação estão completos e se o Microsoft Edge WebView2 Runtime está instalado. Depois, abra o Mercado novamente.";
            UiDialog.Warning(this, "Interface indisponível", $"{reason}\n\n{guidance}\n\nRegistro do erro: {Diagnostics.AppLogger.LogPath}");
            DialogResult = DialogResult.Cancel;
            Close();
        });
    }
}
