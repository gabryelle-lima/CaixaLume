using MercadinhoPdv.Diagnostics;
using MercadinhoPdv.Models;
using MercadinhoPdv.Data;
using MercadinhoPdv.Services;

namespace MercadinhoPdv.Forms;

internal sealed class PdvForm : Form
{
    private static readonly Color Canvas = UiTheme.Background;
    private static readonly Color Panel = UiTheme.Surface;
    private static readonly Color Card = UiTheme.SurfaceRaised;
    private static readonly Color Border = UiTheme.Outline;
    private static readonly Color Muted = UiTheme.Muted;
    private static readonly Color Green = UiTheme.Accent;
    private readonly ProdutoService _products = new();
    private readonly VendaService _sales = new();
    private readonly List<ItemCarrinho> _cart = [];
    private readonly TextBox _search = new();
    private readonly FlowLayoutPanel _catalog = new();
    private readonly FlowLayoutPanel _filters = new();
    private readonly FlowLayoutPanel _cartItems = new();
    private readonly Label _subtotal = new();
    private readonly Label _total = new();
    private readonly Label _cartCount = new();
    private readonly Label _cashStatus = new() { AutoSize=true, ForeColor=Green, Font=new Font("Segoe UI",8,FontStyle.Bold) };
    private readonly Button _finish = new();
    private readonly ImagemProdutoService _imageCache = new();
    private readonly HashSet<string> _imageLoads = new(StringComparer.OrdinalIgnoreCase);
    private List<Produto> _allProducts = [];
    private string _category = "TODOS";
    private bool _cashOpen;
    private AjusteVenda _adjustment = new();
    private bool _saleBusy;
    private bool _searchBusy;
    private readonly Label _adjustmentSummary = new() { Dock = DockStyle.Fill, ForeColor = Muted, AutoSize = true };

    public PdvForm()
    {
        Text = "Mercado · Ponto de venda";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(860, 580);
        Size = new Size(1320, 820);
        BackColor = Canvas;
        ForeColor = Color.FromArgb(231, 237, 243);
        Font = new Font("Segoe UI", 9F);
        UiWindowChrome.Apply(this);
        KeyPreview = true;
        BuildLayout();
        UiTheme.FitToWorkingArea(this);
        LoadProducts();
        UpdateCashStatus();
        Shown += (_, _) => _search.Focus();
        FormClosing += (_, e) =>
        {
            if (_cart.Count > 0 && !_saleBusy && !UiDialog.Confirm(this, "Venda em andamento", "Há produtos no carrinho. Descartar esta venda e fechar o ponto de venda?", true)) e.Cancel = true;
        };
    }

    protected override void OnActivated(EventArgs e){base.OnActivated(e);UpdateCashStatus();}

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 1, BackColor = Canvas };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Canvas, Padding = new Padding(0, 0, 16, 0) };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        var titleGroup = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 2, 0, 0) };
        titleGroup.Controls.Add(new Label { Text = "PONTO DE VENDA", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 15, FontStyle.Bold) });
        titleGroup.Controls.Add(new Label { Text = "Nova venda · F2 para buscar · F4 para cobrar", AutoSize = true, ForeColor = Muted });
        titleGroup.Controls.Add(_cashStatus);
        header.Controls.Add(titleGroup, 0, 0);
        _search.Dock = DockStyle.Fill;
        _search.Font = new Font("Segoe UI", 11);
        _search.BackColor = Panel;
        _search.ForeColor = Color.White;
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.PlaceholderText = "⌕  Buscar produto ou código de barras...";
        _search.Margin = new Padding(8, 10, 0, 14);
        header.Controls.Add(_search, 1, 0);
        left.Controls.Add(header, 0, 0);

        _filters.Dock = DockStyle.Fill;
        _filters.WrapContents = false;
        _filters.AutoScroll = true;
        _filters.Padding = new Padding(0, 4, 0, 4);
        left.Controls.Add(_filters, 0, 1);

        _catalog.Dock = DockStyle.Fill;
        _catalog.AutoScroll = true;
        _catalog.WrapContents = true;
        _catalog.Padding = new Padding(2, 6, 2, 8);
        _catalog.BackColor = Canvas;
        left.Controls.Add(_catalog, 0, 2);
        root.Controls.Add(left, 0, 0);
        root.Controls.Add(BuildCartPanel(), 1, 0);
        Controls.Add(root);

        _search.TextChanged += (_, _) => RenderCatalog();
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None) { e.SuppressKeyPress = true; _ = AddFromSearchAsync(); }
            if (e.KeyCode == Keys.F4 && e.Modifiers == Keys.None) { e.SuppressKeyPress = true; FinishSale(); }
        };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.F4 && e.Modifiers == Keys.None) { e.SuppressKeyPress = true; FinishSale(); } };
    }

    private void UpdateCashStatus(){_cashOpen=new CaixaService().ObterAberto() is not null;_cashStatus.Text=_cashOpen?"● CAIXA ABERTO":"● CAIXA FECHADO · ABRA PARA VENDER";_cashStatus.ForeColor=_cashOpen?Green:Color.IndianRed;_finish.Enabled=_cashOpen&&_cart.Count>0;}

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F2) { _search.Focus(); _search.SelectAll(); return true; }
        if (keyData == Keys.F4) { FinishSale(); return true; }
        if (keyData == Keys.Enter && _search.Focused) { _ = AddFromSearchAsync(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private Control BuildCartPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Panel, Padding = new Padding(14) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 172));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var title = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        title.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        title.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        title.Controls.Add(new Label { Text = "CARRINHO", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = Color.White }, 0, 0);
        _cartCount.Text = "0";
        _cartCount.Dock = DockStyle.Fill;
        _cartCount.TextAlign = ContentAlignment.MiddleCenter;
        _cartCount.ForeColor = Green;
        _cartCount.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        title.Controls.Add(_cartCount, 1, 0);
        panel.Controls.Add(title, 0, 0);
        _cartItems.Dock = DockStyle.Fill;
        _cartItems.FlowDirection = FlowDirection.TopDown;
        _cartItems.WrapContents = false;
        _cartItems.AutoScroll = true;
        _cartItems.Padding = new Padding(0, 6, 0, 4);
        _cartItems.BackColor = Panel;
        panel.Controls.Add(_cartItems, 0, 1);
        var totals = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(0, 8, 0, 2) };
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        totals.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        totals.RowStyles.Add(new RowStyle(SizeType.Percent, 67));
        totals.Controls.Add(new Label { Text = "Subtotal", Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _subtotal.Text = "R$ 0,00";
        _subtotal.Dock = DockStyle.Fill;
        _subtotal.TextAlign = ContentAlignment.MiddleRight;
        _subtotal.ForeColor = Muted;
        totals.Controls.Add(_subtotal, 1, 0);
        totals.Controls.Add(new Label { Text = "TOTAL", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = Color.White }, 0, 1);
        _total.Text = "R$ 0,00";
        _total.Dock = DockStyle.Fill;
        _total.TextAlign = ContentAlignment.MiddleRight;
        _total.Font = new Font("Consolas", 15, FontStyle.Bold);
        _total.ForeColor = Green;
        totals.Controls.Add(_total, 1, 1);
        totals.RowStyles.Clear();
        totals.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); totals.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        totals.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); totals.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        totals.Controls.Add(_adjustmentSummary, 0, 2); totals.SetColumnSpan(_adjustmentSummary, 2);
        var adjustmentButton = new Button { Text = "Desconto / acréscimo", Dock = DockStyle.Fill };
        adjustmentButton.Click += (_, _) => { using var dialog = new AjusteVendaForm(_cart, _adjustment); if (dialog.ShowDialog(this) == DialogResult.OK) { _adjustment = dialog.Ajuste; RefreshCart(); } };
        totals.Controls.Add(adjustmentButton, 0, 3); totals.SetColumnSpan(adjustmentButton, 2);
        panel.Controls.Add(totals, 0, 2);
        _finish.Text = "▣  COBRAR  R$ 0,00   ›";
        _finish.Dock = DockStyle.Fill;
        _finish.FlatStyle = FlatStyle.Flat;
        _finish.FlatAppearance.BorderSize = 0;
        _finish.BackColor = Green;
        _finish.ForeColor = Color.White;
        _finish.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        _finish.Enabled = false;
        _finish.Click += (_, _) => FinishSale();
        panel.Controls.Add(_finish, 0, 3);
        return panel;
    }

    private void LoadProducts()
    {
        _allProducts = _products.Listar(null).Where(p => p.Ativo).OrderBy(p => p.Nome).ToList();
        var categories = _products.ListarCategorias().Select(category => category.Nome).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(x => x).ToList();
        _filters.Controls.Clear();
        AddFilter("TODOS");
        foreach (var item in categories) AddFilter(item.ToUpperInvariant());
        RenderCatalog();
    }

    private void AddFilter(string category)
    {
        var button = new Button { Text = category, AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 8, 0), Padding = new Padding(12, 0, 12, 0), BackColor = _category == category ? Green : Panel, ForeColor = _category == category ? Color.White : Muted };
        button.FlatAppearance.BorderColor = Border;
        button.Click += (_, _) => { _category = category; foreach (Button b in _filters.Controls) { bool active = b.Text == category; b.BackColor = active ? Green : Panel; b.ForeColor = active ? Color.White : Muted; } RenderCatalog(); };
        _filters.Controls.Add(button);
    }

    private void RenderCatalog()
    {
        if (_catalog.IsDisposed) return;
        _catalog.SuspendLayout();
        _catalog.Controls.Clear();
        string query = _search.Text.Trim();
        IEnumerable<Produto> products = _allProducts;
        if (_category != "TODOS") products = products.Where(p => string.Equals(p.CategoriaNome ?? "OUTROS", _category, StringComparison.CurrentCultureIgnoreCase));
        if (query.Length > 0) products = products.Where(p => p.Nome.Contains(query, StringComparison.CurrentCultureIgnoreCase) || (p.CodigoBarras?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        foreach (Produto product in products) _catalog.Controls.Add(CreateProductCard(product));
        if (_catalog.Controls.Count == 0) _catalog.Controls.Add(new Label { Text = "Nenhum produto encontrado.", AutoSize = true, ForeColor = Muted, Padding = new Padding(16) });
        _catalog.ResumeLayout();
    }

    private Control CreateProductCard(Produto product)
    {
        var card = new Panel { Width = 188, Height = 112, Margin = new Padding(4), Padding = new Padding(8), BackColor = Card, Cursor = Cursors.Hand };
        var image = new PictureBox { Width = 52, Height = 52, Location = new Point(8, 8), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Panel };
        var name = new Label { Text = product.Nome, Location = new Point(68, 8), Size = new Size(104, 36), ForeColor = Color.White, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), AutoEllipsis = true };
        var price = new Label { Text = $"{product.PrecoVenda:C2} / {product.UnidadeMedida}", Location = new Point(68, 47), Size = new Size(106, 22), ForeColor = Green, Font = new Font("Consolas", 8.5F, FontStyle.Bold) };
        var stock = new Label { Text = $"{product.EstoqueAtual:0.###} {product.UnidadeMedida} disp.", Location = new Point(8, 76), Size = new Size(164, 20), ForeColor = product.EstoqueAtual <= product.EstoqueMinimo ? Color.FromArgb(255, 207, 112) : Muted, Font = new Font("Segoe UI", 8F), AutoEllipsis = true };
        card.Controls.AddRange([image, name, price, stock]);
        void Add(object? sender, EventArgs e) => AddProduct(product, product.UnidadeMedida == "KG" ? 0m : 1m);
        card.Click += Add; image.Click += Add; name.Click += Add; price.Click += Add; stock.Click += Add;
        image.Disposed += (_, _) => image.Image?.Dispose();
        if (!ImagemProdutoService.TryOpen(product.LocalImagePath, out var localImage)) _ = LoadCatalogImageAsync(product, image);
        else image.Image = localImage;
        return card;
    }

    private async Task LoadCatalogImageAsync(Produto product, PictureBox target)
    {
        if (string.IsNullOrWhiteSpace(product.ImagemUrl) || !_imageLoads.Add(product.ImagemUrl)) return;
        try
        {
            string? path = await _imageCache.CachearUrlAsync(product.ImagemUrl, product.CodigoBarras);
            product.LocalImagePath = path;
            if (path is not null && !target.IsDisposed && ImagemProdutoService.TryOpen(path, out var image)) target.Image = image;
        }
        catch (Exception ex) { AppLogger.Error(ex, "Carregar imagem do catálogo do PDV"); }
        finally { if (product.ImagemUrl is not null) _imageLoads.Remove(product.ImagemUrl); }
    }

    private async Task AddFromSearchAsync()
    {
        if (_searchBusy || _saleBusy) return;
        _searchBusy = true;
        try { await AddFromSearchCoreAsync(); }
        catch (Exception exception) { AppLogger.Error(exception, "Adicionar produto pela busca"); UiDialog.Warning(this, "Não foi possível adicionar", exception.Message); }
        finally { _searchBusy = false; }
    }

    private async Task AddFromSearchCoreAsync()
    {
        string query = _search.Text.Trim();
        if (query.Length == 0) return;
        Produto? exact = _products.BuscarPorCodigoBarras(query);
        Produto? product = exact ?? _allProducts.FirstOrDefault(p => string.Equals(p.Nome, query, StringComparison.CurrentCultureIgnoreCase));
        if (product is null)
        {
            var matches = _allProducts.Where(p => p.Nome.Contains(query, StringComparison.CurrentCultureIgnoreCase)).Take(2).ToList();
            if (matches.Count == 1) product = matches[0];
            else if (matches.Count > 1) { _search.SelectAll(); return; }
        }
        if (product is null)
        {
            if (query.Length >= 8 && query.All(char.IsDigit)) await HandleUnknownBarcodeAsync(query);
            else UiDialog.Info(this, "Produto não encontrado", "Confira o código de barras ou a descrição e tente novamente.");
            return;
        }
        AddProduct(product, product.UnidadeMedida == "KG" ? 0m : 1m);
    }

    private async Task HandleUnknownBarcodeAsync(string barcode)
    {
        using var choice = new Form { Text = "Produto não cadastrado", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, ClientSize = new Size(430, 220), MaximizeBox = false, MinimizeBox = false, BackColor = Panel, ForeColor = Color.White, Font = Font };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.Controls.Add(new Label { Text = "PRODUTO NÃO CADASTRADO", Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        layout.Controls.Add(new Label { Text = $"Não encontramos o código {barcode} no estoque.\n\nVocê pode cadastrar manualmente ou buscar informações públicas do produto.", Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var manual = new Button { Text = "CADASTRAR PRODUTO", Dock = DockStyle.Fill, DialogResult = DialogResult.Yes, BackColor = Green, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Enabled = SessaoUsuario.Administrador };
        var lookup = new Button { Text = "BUSCAR INFORMAÇÕES", Dock = DockStyle.Fill, DialogResult = DialogResult.Retry, BackColor = Card, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        buttons.Controls.Add(manual, 0, 0); buttons.Controls.Add(lookup, 1, 0); layout.Controls.Add(buttons, 0, 2); choice.Controls.Add(layout); UiWindowChrome.Apply(choice);
        DialogResult result = choice.ShowDialog(this);
        if (result == DialogResult.Yes) { OpenNewProduct(barcode); return; }
        if (result != DialogResult.Retry) return;

        try
        {
            UseWaitCursor = true;
            ProdutoCatalogoPublico? found = await new OpenFoodFactsService().BuscarPorCodigoAsync(barcode);
            if (found is null)
            {
                if (UiDialog.Confirm(this, "Produto não encontrado", "O catálogo público não retornou esse código. Deseja cadastrá-lo manualmente?"))
                    OpenNewProduct(barcode);
                return;
            }
            var imported = new Produto { CodigoBarras = found.CodigoBarras, Nome = found.Nome, Marca = found.Marca, ImagemUrl = found.ImagemUrl };
            if (!string.IsNullOrWhiteSpace(found.Categoria))
            {
                string candidate = found.Categoria.Split(',').Last().Trim();
                var match = _products.ListarCategorias().FirstOrDefault(c => string.Equals(c.Nome, candidate, StringComparison.CurrentCultureIgnoreCase));
                imported.CategoriaId = match?.Id;
            }
            string imageNote = string.IsNullOrWhiteSpace(found.ImagemUrl) ? "Imagem: indisponível" : $"Imagem: {found.ImagemUrl}";
            if (UiDialog.Confirm(this, "Revise os dados do produto", $"Nome: {found.Nome}\nMarca: {found.Marca ?? "não informada"}\nCategoria: {found.Categoria ?? "não informada"}\n{imageNote}\n\nAs informações públicas podem estar incompletas. Abrir o cadastro para revisar?"))
                OpenNewProduct(barcode, imported);
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Buscar produto no Open Food Facts");
            if (UiDialog.Confirm(this, "Busca indisponível", "Não foi possível consultar o catálogo público. Deseja cadastrar o produto manualmente?"))
                OpenNewProduct(barcode);
        }
        finally { UseWaitCursor = false; _search.Focus(); }
    }

    private void OpenNewProduct(string barcode, Produto? imported = null)
    {
        if (!SessaoUsuario.Administrador) { UiDialog.Info(this, "Acesso restrito", "O cadastro de produtos exige perfil de administrador."); return; }
        using var form = new ProdutoForm(_products, imported, barcode);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            LoadProducts();
            Produto? saved = _products.BuscarPorCodigoBarras(barcode);
            if (saved is not null && saved.EstoqueAtual > 0) AddProduct(saved, saved.UnidadeMedida == "KG" ? 0m : 1m);
        }
        _search.Focus();
    }

    private void AddProduct(Produto product, decimal amount)
    {
        if (!product.Ativo) return;
        ItemCarrinho? existing = _cart.FirstOrDefault(item => item.ProdutoId == product.Id);
        if (product.UnidadeMedida == "KG" && amount == 0)
        {
            decimal available = product.EstoqueAtual - (existing?.Quantidade ?? 0);
            if (available <= 0) { UiDialog.Warning(this, "Sem estoque", "Não há peso disponível para adicionar."); return; }
            using var weight = new PesoProdutoForm(product, available);
            if (weight.ShowDialog(this) != DialogResult.OK) return;
            amount = weight.Peso;
        }
        if (amount <= 0) { UiDialog.Warning(this, "Sem estoque", "Não há quantidade disponível para adicionar."); return; }
        decimal next = (existing?.Quantidade ?? 0) + amount;
        if (product.UnidadeMedida == "UN" && next % 1 != 0) return;
        if (next > product.EstoqueAtual) { UiDialog.Warning(this, "Estoque insuficiente", $"Disponível: {product.EstoqueAtual:0.###} {product.UnidadeMedida}."); return; }
        if (existing is null) _cart.Add(new ItemCarrinho { ProdutoId = product.Id, Produto = product.Nome, CodigoBarras = product.CodigoBarras, UnidadeMedida = product.UnidadeMedida, Quantidade = amount, PrecoUnitario = product.PrecoVenda, EstoqueDisponivel = product.EstoqueAtual });
        else existing.Quantidade = next;
        _search.Clear();
        RefreshCart();
        _search.Focus();
    }

    private void RefreshCart()
    {
        _cartItems.SuspendLayout();
        _cartItems.Controls.Clear();
        foreach (var item in _cart) _cartItems.Controls.Add(CreateCartRow(item));
        if (_cart.Count == 0) _cartItems.Controls.Add(new Label { Text = "\n\nSeu carrinho está vazio\n\nClique em um produto para adicionar", Width = 270, Height = 170, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted });
        _cartItems.ResumeLayout();
        TotaisVenda values;
        try { values = new AjustesVendaService().Calcular(_cart, _adjustment); }
        catch (InvalidOperationException exception) { _adjustment = new(); values = new AjustesVendaService().Calcular(_cart); UiDialog.Warning(this, "Ajuste removido", exception.Message); }
        decimal total = values.Total;
        _cartCount.Text = _cart.Count.ToString();
        _subtotal.Text = values.Subtotal.ToString("C2");
        _adjustmentSummary.Text = $"Desconto {values.Desconto:C2} · Acréscimo {values.Acrescimo:C2}";
        _total.Text = total.ToString("C2");
        _finish.Text = $"▣  COBRAR  {total:C2}   ›";
        _finish.Enabled = _cashOpen && _cart.Count > 0;
    }

    private Control CreateCartRow(ItemCarrinho item)
    {
        var row = new TableLayoutPanel { Width = 278, Height = 78, ColumnCount = 4, RowCount = 2, Margin = new Padding(0, 2, 0, 5), Padding = new Padding(2, 4, 2, 4), BackColor = Panel };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var name = new Label { Text = item.Produto, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), AutoEllipsis = true };
        row.Controls.Add(name, 0, 0);
        row.SetColumnSpan(name, 4);
        var price = new Label { Text = $"{item.PrecoUnitario:C2}/{item.UnidadeMedida}", Dock = DockStyle.Fill, ForeColor = Green, Font = new Font("Consolas", 8.5F) };
        row.Controls.Add(price, 0, 1);
        var minus = SmallButton("−");
        minus.Click += (_, _) => ChangeQuantity(item, -1);
        row.Controls.Add(minus, 1, 1);
        var qty = new TextBox { Text = item.Quantidade.ToString(item.UnidadeMedida == "UN" ? "0" : "0.000"), Dock = DockStyle.Fill, TextAlign = HorizontalAlignment.Center, ForeColor = Color.White, BackColor = Card, AccessibleName = "Quantidade em " + item.UnidadeMedida };
        qty.Validating += (_, e) =>
        {
            try
            {
                decimal next = QuantidadeService.Interpretar(qty.Text, item.UnidadeMedida, item.EstoqueDisponivel);
                if (next != item.Quantidade) { item.Quantidade = next; BeginInvoke(RefreshCart); }
            }
            catch (InvalidOperationException exception) { e.Cancel = true; UiDialog.Warning(this, "Quantidade inválida", exception.Message); qty.SelectAll(); }
        };
        qty.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _search.Focus(); } };
        row.Controls.Add(qty, 2, 1);
        var plus = SmallButton("+");
        plus.Click += (_, _) => ChangeQuantity(item, 1);
        row.Controls.Add(plus, 3, 1);
        return row;
    }

    private static Button SmallButton(string text) => new() { Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderColor = Border }, BackColor = Card, ForeColor = Color.White, Margin = new Padding(1) };

    private void ChangeQuantity(ItemCarrinho item, int direction)
    {
        decimal increment = item.UnidadeMedida == "UN" ? 1 : 0.250m;
        decimal next = item.Quantidade + increment * direction;
        if (next <= 0) _cart.Remove(item);
        else if (next > item.EstoqueDisponivel) { UiDialog.Warning(this, "Estoque insuficiente", $"Disponível: {item.EstoqueDisponivel:0.###} {item.UnidadeMedida}."); return; }
        else item.Quantidade = next;
        RefreshCart();
        _search.Focus();
    }

    private void FinishSale()
    {
        if (_saleBusy || _cart.Count == 0 || !ValidateChildren()) return;
        UpdateCashStatus();
        if (!_cashOpen) { UiDialog.Info(this, "Caixa fechado", "Abra o caixa antes de registrar uma venda."); return; }
        decimal total;
        try { total = new AjustesVendaService().Calcular(_cart, _adjustment).Total; }
        catch (InvalidOperationException exception) { UiDialog.Warning(this, "Reveja o ajuste", exception.Message); return; }
        using var payment = new PagamentoForm(total);
        if (payment.ShowDialog(this) != DialogResult.OK) return;
        long saleId;
        try
        {
            _saleBusy = true;
            saleId = _sales.Registrar(_cart, payment.FormaPagamento, payment.ValorRecebido, _adjustment, Guid.NewGuid().ToString(), total);
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Finalizar venda");
            string context = BuildSaleDiagnosticContext(payment, total);
            AppLogger.Error(new InvalidOperationException(context, exception), "Diagnóstico detalhado da venda");
            string? stage = Convert.ToString(exception.Data["VendaEtapa"]);
            UiDialog.Error(this, "Não foi possível concluir a venda", $"O carrinho foi mantido e nenhuma venda foi confirmada.\n\nEtapa: {stage ?? "validar/registrar venda"}\nMotivo: {exception.Message}\n\nDiagnóstico completo registrado em: {AppLogger.LogPath}");
            return;
        }
        finally { _saleBusy = false; }

        try
        {
            var completedItems = _cart.ToList();
            var completedTotals = _sales.ObterTotais(saleId);
            _adjustment = new();
            _cart.Clear();
            RefreshCart();
            var receipt = new ComprovanteService();
            string receiptText = receipt.CriarTexto(saleId, DateTime.Now, completedItems, total, payment.FormaPagamento, payment.ValorRecebido, payment.Troco, completedTotals);
            try { LoadProducts(); }
            catch (Exception refreshException) { AppLogger.Error(refreshException, "Atualizar produtos após venda confirmada"); }
            using var success = new Form { Text = "Venda concluída", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, ClientSize = new Size(480, 380), MinimumSize = new Size(440, 350), MaximizeBox = false, MinimizeBox = false, BackColor = Panel, ForeColor = Color.White, Font = Font, ShowInTaskbar = false };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), RowCount = 6, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.Controls.Add(new Label { Text = "✓", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Green, Font = new Font("Segoe UI", 30, FontStyle.Bold) }, 0, 0);
            layout.Controls.Add(new Label { Text = "VENDA CONCLUÍDA", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 14, FontStyle.Bold) }, 0, 1);
            layout.Controls.Add(new Label { Text = $"Venda #{saleId:D5}  ·  {total:C2}", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Consolas", 15, FontStyle.Bold) }, 0, 2);
            string paymentDetails = payment.FormaPagamento == "DINHEIRO"
                ? $"DINHEIRO\nRecebido  {payment.ValorRecebido:C2}     Troco  {payment.Troco:C2}"
                : payment.FormaPagamento;
            layout.Controls.Add(new Label { Text = paymentDetails, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted }, 0, 3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoScroll = true };
            var print = new Button { Text = "IMPRIMIR", Width = 100, Height = 36 };
            var pdf = new Button { Text = "SALVAR PDF", Width = 105, Height = 36 };
            var view = new Button { Text = "VER VENDA", Width = 100, Height = 36 };
            actions.Controls.AddRange([print, pdf, view]);
            print.Click += (_, _) => { try { receipt.Imprimir(receiptText); } catch (Exception ex) { UiDialog.Warning(success, "Comprovante", ex.Message); } };
            pdf.Click += (_, _) => { using var dialog = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = $"venda-{saleId:D6}.pdf" }; if (dialog.ShowDialog(success) != DialogResult.OK) return; try { receipt.SalvarPdf(receiptText, dialog.FileName); UiDialog.Success(success, "Comprovante salvo", "O comprovante da venda foi salvo em PDF."); } catch (Exception ex) { UiDialog.Warning(success, "PDF", ex.Message); } };
            view.Click += (_, _) => ShowSaleDetails(success, saleId);
            layout.Controls.Add(actions, 0, 4);
            var again = new Button { Text = "NOVA VENDA", Dock = DockStyle.Fill, DialogResult = DialogResult.OK, BackColor = Green, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            layout.Controls.Add(again, 0, 5);
            success.Controls.Add(layout);
            UiWindowChrome.Apply(success);
            success.AcceptButton = again;
            success.ShowDialog(this);
            _search.Focus();
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, $"Apresentar confirmação da venda #{saleId}");
            UiDialog.Success(this, "Venda concluída", $"A venda #{saleId:D5} foi registrada com sucesso.\n\n{total:C2} · {payment.FormaPagamento}");
            _search.Focus();
        }
    }

    private string BuildSaleDiagnosticContext(PagamentoForm payment, decimal total)
    {
        string cash;
        try
        {
            var open = new CaixaService().ObterAberto();
            cash = open is null ? "nenhum" : $"Id={open.Id}; status=Aberto";
        }
        catch (Exception ex) { cash = $"falha ao consultar: {ex}"; }
        string items = string.Join(Environment.NewLine, _cart.Select(item =>
        {
            string stock;
            try
            {
                using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
                var row = db.Query("SELECT EstoqueAtual FROM Produtos WHERE Id=@Id;", new Dictionary<string, object?> { ["@Id"] = item.ProdutoId }).FirstOrDefault();
                stock = row is null ? "produto não encontrado" : Convert.ToString(row["EstoqueAtual"], System.Globalization.CultureInfo.InvariantCulture) ?? "indisponível";
            }
            catch (Exception ex) { stock = $"falha ao consultar: {ex.Message}"; }
            return $"ProdutoId={item.ProdutoId}; produto={item.Produto}; quantidade={item.Quantidade}; unidade={item.UnidadeMedida}; preço={item.PrecoUnitario}; subtotal={item.Subtotal}; estoque carrinho={item.EstoqueDisponivel}; estoque banco={stock}";
        }));
        return $"DatabasePath={DatabaseInitializer.DatabasePath}{Environment.NewLine}Usuario={SessaoUsuario.Nome}; UsuarioId={SessaoUsuario.UsuarioId?.ToString() ?? "null"}; Perfil={SessaoUsuario.Perfil}{Environment.NewLine}Caixa={cash}{Environment.NewLine}Total={total}; Pagamento={payment.FormaPagamento}; Recebido={payment.ValorRecebido}{Environment.NewLine}Itens:{Environment.NewLine}{items}";
    }

    private void ShowSaleDetails(IWin32Window owner, long saleId)
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        UiTheme.StyleGrid(grid);
        grid.DataSource = _sales.Detalhes(saleId);
        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (column.DataPropertyName is nameof(VendaItemDetalhe.PrecoUnitario) or nameof(VendaItemDetalhe.Subtotal)) column.DefaultCellStyle.Format = "C2";
            if (column.DataPropertyName == nameof(VendaItemDetalhe.Quantidade)) column.DefaultCellStyle.Format = "0.###";
        }
        using var details = new Form { Text = $"Venda #{saleId:D5}", StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(720, 380), MinimumSize = new Size(520, 320), BackColor = UiTheme.Background };
        details.Controls.Add(grid);
        UiWindowChrome.Apply(details);
        details.ShowDialog(owner);
    }
}
