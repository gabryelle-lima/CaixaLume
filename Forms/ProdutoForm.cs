using CaixaLume.Diagnostics;
using CaixaLume.Models;
using CaixaLume.Services;
using System.Net.Http;

namespace CaixaLume.Forms;

internal sealed class ProdutoForm : Form
{
    private readonly ProdutoService _service;
    private readonly Produto _product;
    private readonly TextBox _name = new() { CharacterCasing = CharacterCasing.Upper };
    private readonly TextBox _barcode = new();
    private readonly TextBox _brand = new();
    private readonly TextBox _imageUrl = new() { Tag = "preserve-case" };
    private readonly PictureBox _imagePreview = new() { Width = 140, Height = 110, SizeMode = PictureBoxSizeMode.Zoom, BackColor = UiTheme.SurfaceRaised };
    private readonly ImagemProdutoService _images = new();
    private readonly Button _lookupCatalog = new() { Text = "Buscar catálogo", AutoSize = true };
    private readonly Button _chooseImage = new() { Text = "Escolher imagem…", AutoSize = true };
    private readonly Button _removeImage = new() { Text = "Remover imagem", AutoSize = true };
    private readonly ComboBox _category = new();
    private readonly ComboBox _supplier = new();
    private readonly ComboBox _unit = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _cost = NumberInput(2, 999999.99m);
    private readonly NumericUpDown _price = NumberInput(2, 999999.99m);
    private readonly NumericUpDown _stock = NumberInput(3, 999999.999m);
    private readonly NumericUpDown _minimumStock = NumberInput(3, 999999.999m);
    private readonly Label _margin = new() { AutoSize=true, Anchor=AnchorStyles.Left };
    private readonly CheckBox _hasExpiration = new() { Text = "Produto possui validade", AutoSize = true };
    private readonly DateTimePicker _expiration = new() { Format = DateTimePickerFormat.Short };
    private bool _loadingFields;
    private string _previousUnit = "UN";

    public ProdutoForm(ProdutoService service, Produto? product, string? initialBarcode = null)
    {
        _service = service;
        _product = product ?? new Produto { CodigoBarras = initialBarcode };
        Text = _product.Id == 0 ? "Novo produto" : "Editar produto";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(620, 520);
        Size = new Size(700, 600);
        BuildLayout();
        UiTheme.Apply(this);
        LoadOptions();
        FillFields();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(18) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var content = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.Controls.Add(BuildGroup("DADOS PARA VENDA", ("Código de barras", _barcode), ("Consultar catálogo", _lookupCatalog), ("Nome *", _name), ("Preço de venda *", _price), ("Unidade de medida", _unit), ("Estoque atual", _stock)));
        content.Controls.Add(BuildGroup("DETALHES E CONTROLE", ("Categoria", _category), ("Marca", _brand), ("Fornecedor", _supplier), ("Preço de custo", _cost), ("Margem bruta estimada", _margin), ("Estoque mínimo", _minimumStock), ("", _hasExpiration), ("Data de validade", _expiration)));
        content.Controls.Add(BuildGroup("IMAGEM DO PRODUTO", ("Imagem (URL)", _imageUrl), ("Arquivo local", _chooseImage), ("Remoção", _removeImage), ("Prévia da imagem", _imagePreview)));
        scroll.Controls.Add(content);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
        var save = new Button { Text = "Salvar produto", Width = 140, Height = 36, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Cancelar", Width = 110, Height = 36, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([save, cancel]);
        root.Controls.Add(scroll, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        _unit.Items.AddRange(["UN", "KG"]);
        _unit.SelectedIndexChanged += (_, _) => UnitChanged();
        _cost.ValueChanged += (_,_)=>UpdateMargin(); _price.ValueChanged += (_,_)=>UpdateMargin();
        _imageUrl.TextChanged += (_, _) => { if (!string.Equals(_imageUrl.Text, _product.ImagemUrl ?? string.Empty, StringComparison.Ordinal)) _product.LocalImagePath = null; };
        _hasExpiration.CheckedChanged += (_, _) => _expiration.Enabled = _hasExpiration.Checked;
        save.Click += async (_, _) => await SaveAsync(save);
        _lookupCatalog.Click += async (_, _) => await LookupCatalogAsync();
        _chooseImage.Click += (_, _) => ChooseLocalImage();
        _removeImage.Click += (_, _) => { _imageUrl.Clear(); _product.LocalImagePath = null; var previous = _imagePreview.Image; _imagePreview.Image = null; previous?.Dispose(); };
        AcceptButton = save;
        CancelButton = cancel;
    }

    private static GroupBox BuildGroup(string title, params (string Label, Control Input)[] fields)
    {
        var group = new GroupBox { Text = title, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 12) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8, 10, 8, 4) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        foreach (var field in fields)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Math.Max(42, field.Input.Height + 8)));
            layout.Controls.Add(new Label { Text = field.Label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 0) }, 0, row);
            field.Input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            field.Input.Margin = new Padding(0, 4, 12, 4);
            layout.Controls.Add(field.Input, 1, row);
        }
        group.Controls.Add(layout);
        return group;
    }

    private static NumericUpDown NumberInput(int decimals, decimal maximum) => new() { DecimalPlaces = decimals, Minimum = 0, Maximum = maximum, Increment = decimals == 0 ? 1 : 0.001m, ThousandsSeparator = true, Dock = DockStyle.Fill };

    private void ConfigureQuantityPrecision()
    {
        bool unitCount = _unit.SelectedItem?.ToString() == "UN";
        _stock.DecimalPlaces = unitCount ? 0 : 3;
        _minimumStock.DecimalPlaces = unitCount ? 0 : 3;
        _stock.Increment = unitCount ? 1 : 0.001m;
        _minimumStock.Increment = unitCount ? 1 : 0.001m;
    }

    private void UnitChanged()
    {
        if (_loadingFields) { ConfigureQuantityPrecision(); return; }
        string selected = _unit.SelectedItem?.ToString() ?? "UN";
        if (selected == "UN" && (_stock.Value % 1 != 0 || _minimumStock.Value % 1 != 0))
        {
            UiDialog.Warning(this, "Unidade incompatível", "Antes de mudar para UN, ajuste o estoque atual e o estoque mínimo para números inteiros. Nenhuma quantidade foi alterada.");
            _unit.SelectedItem = _previousUnit;
            return;
        }
        _previousUnit = selected;
        ConfigureQuantityPrecision();
    }

    private void LoadOptions()
    {
        ConfigureOptions(_category, _service.ListarCategorias(_product.CategoriaId));
        ConfigureOptions(_supplier, _service.ListarFornecedores(_product.FornecedorId));
    }

    private static void ConfigureOptions(ComboBox combo, List<OpcaoCadastro> options)
    {
        options.Insert(0, new OpcaoCadastro { Id = null, Nome = "Não informado" });
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.DataSource = options;
        combo.DisplayMember = nameof(OpcaoCadastro.Nome);
        combo.ValueMember = nameof(OpcaoCadastro.Id);
        combo.Dock = DockStyle.Fill;
    }

    private void FillFields()
    {
        _loadingFields = true;
        _unit.SelectedItem = _product.UnidadeMedida is "KG" ? "KG" : "UN";
        _name.Text = _product.Nome;
        _barcode.Text = _product.CodigoBarras ?? string.Empty;
        _brand.Text = _product.Marca ?? string.Empty;
        _imageUrl.Text = _product.ImagemUrl ?? string.Empty;
        if (ImagemProdutoService.TryOpen(_product.LocalImagePath, out var local)) SetPreview(local!);
        else if (!string.IsNullOrWhiteSpace(_product.ImagemUrl)) _ = LoadImagePreviewAsync(_product.ImagemUrl);
        SelectOption(_category, _product.CategoriaId);
        SelectOption(_supplier, _product.FornecedorId);
        _cost.Value = Math.Clamp(_product.PrecoCusto, _cost.Minimum, _cost.Maximum);
        _price.Value = Math.Clamp(_product.PrecoVenda, _price.Minimum, _price.Maximum);
        UpdateMargin();
        _stock.Value = Math.Clamp(_product.EstoqueAtual, _stock.Minimum, _stock.Maximum);
        _minimumStock.Value = Math.Clamp(_product.EstoqueMinimo, _minimumStock.Minimum, _minimumStock.Maximum);
        _hasExpiration.Checked = _product.DataValidade.HasValue;
        _expiration.Value = _product.DataValidade ?? DateTime.Today;
        _expiration.Enabled = _hasExpiration.Checked;
        _previousUnit = _unit.SelectedItem?.ToString() ?? "UN";
        _loadingFields = false;
        ConfigureQuantityPrecision();
    }

    private void UpdateMargin()
    {
        decimal margin = _price.Value > 0 ? (_price.Value - _cost.Value) / _price.Value * 100m : 0;
        _margin.Text = $"{(_price.Value-_cost.Value):C2} · {margin:N1}% do preço de venda";
        _margin.ForeColor = margin >= 0 ? UiTheme.Accent : Color.IndianRed;
    }

    private static void SelectOption(ComboBox combo, int? id)
    {
        if (id.HasValue && combo.Items.Cast<OpcaoCadastro>().Any(option => option.Id == id.Value)) combo.SelectedValue = id.Value;
        else combo.SelectedIndex = 0;
    }

    private async Task LoadImagePreviewAsync(string url)
    {
        try
        {
            string? path = await _images.CachearUrlAsync(url, _barcode.Text);
            if (path is not null && ImagemProdutoService.TryOpen(path, out var image))
            {
                if (!_imagePreview.IsDisposed && _imageUrl.Text == url) SetPreview(image!);
                else image?.Dispose();
            }
        }
        catch (Exception exception) { AppLogger.Error(exception, "Carregar prévia de imagem do produto"); }
    }

    private async Task LookupCatalogAsync()
    {
        string barcode = _barcode.Text.Trim();
        if (barcode.Length == 0) { UiDialog.Info(this, "Código necessário", "Informe o código de barras antes de consultar o catálogo público."); return; }
        _lookupCatalog.Enabled = false;
        string originalButtonText = _lookupCatalog.Text;
        _lookupCatalog.Text = "Consultando catálogo…";
        try
        {
            ProdutoCatalogoPublico? found = await new OpenFoodFactsService().BuscarPorCodigoAsync(barcode);
            if (IsDisposed || Disposing || _barcode.Text.Trim() != barcode) return;
            if (found is null) { UiDialog.Info(this, "Produto não encontrado", "O catálogo não retornou informações para este código. Você pode continuar o cadastro manualmente."); return; }
            string details = $"Nome: {found.Nome}\nMarca: {found.Marca ?? "não informada"}\nCategoria: {found.Categoria ?? "não informada"}\nImagem: {(found.ImagemUrl is null ? "não disponível" : "disponível")}\n\nDeseja preencher os campos com esses dados para revisar?";
            if (!UiDialog.Confirm(this, "Revise os dados externos", details)) return;
            // Preenche somente campos vazios; dados digitados pelo usuário têm prioridade.
            if (string.IsNullOrWhiteSpace(_name.Text)) _name.Text = found.Nome.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(_brand.Text)) _brand.Text = found.Marca?.Trim().ToUpperInvariant() ?? "";
            _barcode.Text = found.CodigoBarras;
            bool preencherImagem = string.IsNullOrWhiteSpace(_imageUrl.Text) && string.IsNullOrWhiteSpace(_product.LocalImagePath);
            if (preencherImagem) _imageUrl.Text = found.ImagemUrl ?? "";
            if (!string.IsNullOrWhiteSpace(found.Categoria) && (_category.SelectedItem as OpcaoCadastro)?.Id is null)
            {
                string candidate = found.Categoria.Split(',').Last().Trim();
                var category = _category.Items.Cast<OpcaoCadastro>().FirstOrDefault(item => item.Nome.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                if (category is not null) _category.SelectedItem = category;
            }
            if (preencherImagem && found.ImagemUrl is not null) await LoadImagePreviewAsync(found.ImagemUrl);
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Consultar catálogo Open Food Facts no cadastro");
            if (!IsDisposed) UiDialog.Warning(this, "Catálogo indisponível", $"Não foi possível consultar agora: {exception.Message}\n\nO cadastro manual continua disponível.");
        }
        finally { if (!IsDisposed) { _lookupCatalog.Text = originalButtonText; _lookupCatalog.Enabled = true; } }
    }

    private void ChooseLocalImage()
    {
        using var dialog = new OpenFileDialog { Title = "Escolher imagem do produto", Filter = "Imagens (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _product.LocalImagePath = _images.ImportarArquivo(dialog.FileName);
            if (ImagemProdutoService.TryOpen(_product.LocalImagePath, out var image)) SetPreview(image!);
        }
        catch (Exception ex) { UiDialog.Warning(this, "Imagem não carregada", ex.Message); }
    }

    private void SetPreview(Image image)
    {
        var previous = _imagePreview.Image;
        _imagePreview.Image = image;
        previous?.Dispose();
    }

    private async Task SaveAsync(Button save)
    {
        _product.Nome = _name.Text.Trim().ToUpperInvariant();
        _product.CodigoBarras = _barcode.Text;
        _product.Marca = string.IsNullOrWhiteSpace(_brand.Text) ? null : _brand.Text.Trim().ToUpperInvariant();
        _product.ImagemUrl = string.IsNullOrWhiteSpace(_imageUrl.Text) ? null : _imageUrl.Text.Trim();
        _product.CategoriaId = (_category.SelectedItem as OpcaoCadastro)?.Id;
        _product.FornecedorId = (_supplier.SelectedItem as OpcaoCadastro)?.Id;
        _product.PrecoCusto = _cost.Value;
        _product.PrecoVenda = _price.Value;
        _product.UnidadeMedida = _unit.SelectedItem?.ToString() ?? "UN";
        _product.EstoqueAtual = _stock.Value;
        _product.EstoqueMinimo = _minimumStock.Value;
        _product.DataValidade = _hasExpiration.Checked ? _expiration.Value.Date : null;
        try
        {
            save.Enabled = false;
            if (_product.LocalImagePath is null && _product.ImagemUrl is not null)
                _product.LocalImagePath = await _images.CachearUrlAsync(_product.ImagemUrl, _product.CodigoBarras);
            _service.Salvar(_product);
            UiDialog.Success(this, "Produto salvo", "O cadastro do produto foi atualizado.");
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (ProdutoValidationException exception)
        {
            UiDialog.Warning(this, "Verifique o cadastro", exception.Message);
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Salvar produto");
            UiDialog.Error(this, "Não foi possível salvar", $"Os dados continuam preenchidos.\n\n{exception.Message}\nDetalhes registrados em: {AppLogger.LogPath}");
        }
        finally { if (!IsDisposed) save.Enabled = true; }
    }
}
