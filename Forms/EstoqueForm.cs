using MercadinhoPdv.Models;
using MercadinhoPdv.Services;
using MercadinhoPdv.Diagnostics;

namespace MercadinhoPdv.Forms;

internal sealed class EstoqueForm : Form
{
    private readonly EstoqueService _service = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _search = new() { Width = 280, PlaceholderText = "Pesquisar produto ou código" };
    private readonly ComboBox _category = new() { Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _unit = new() { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _status = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _operation = new() { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _quantity = new() { Minimum = 0, Maximum = 999999, DecimalPlaces = 3, Increment = 0.001m, Value = 1, Width = 140, ThousandsSeparator = true };
    private readonly Label _quantityLabel = new() { AutoSize = true, Text = "Quantidade a adicionar", Margin = new Padding(8, 10, 3, 0) };
    private readonly TextBox _reason = new() { Width = 220, CharacterCasing = CharacterCasing.Upper, PlaceholderText = "Motivo da movimentação" };
    private readonly Label _movementPreview = new() { Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };

    public EstoqueForm()
    {
        Text = "Estoque e movimentações";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(860, 540);
        Size = new Size(1200, 760);
        BuildLayout();
        UiTheme.Apply(this);
        LoadCategories();
        LoadProducts();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        filters.Controls.AddRange([_search, _category, _unit, _status]);
        _category.Margin = new Padding(8, 3, 0, 0);
        _unit.Margin = new Padding(8, 3, 0, 0);
        _status.Margin = new Padding(8, 3, 0, 0);
        _unit.Items.AddRange(["Todas unidades", "UN", "KG"]);
        _unit.SelectedIndex = 0;
        _status.Items.AddRange(["Todos os estoques", "Estoque baixo", "Estoque normal"]);
        _status.SelectedIndex = 0;
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        UiTheme.StyleGrid(_grid);
        _grid.Columns.AddRange(
            GridColumn(nameof(Produto.CodigoBarras), "Código", 12), GridColumn(nameof(Produto.Nome), "Produto", 23),
            GridColumn(nameof(Produto.CategoriaNome), "Categoria", 15), GridColumn(nameof(Produto.UnidadeMedida), "Unid.", 7),
            GridColumn(nameof(Produto.EstoqueAtual), "Estoque atual", 12, "0.###"), GridColumn(nameof(Produto.EstoqueMinimo), "Estoque mínimo", 12, "0.###"),
            GridColumn(nameof(Produto.DataValidade), "Validade", 11, "dd/MM/yyyy"), GridColumn("StatusEstoque", "Status", 12));
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].DataPropertyName == "StatusEstoque" && Equals(e.Value, "BAIXO")) e.CellStyle.ForeColor = Color.DarkOrange;
        };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true, Padding = new Padding(0, 4, 0, 0) };
        _operation.Items.AddRange(["Entrada", "Saída", "Ajustar saldo"]);
        _operation.SelectedIndex = 0;
        var apply = new Button { Text = "Registrar", Width = 100, Height = 34 };
        var history = new Button { Text = "Histórico", Width = 112, Height = 34 };
        var fullEntry = new Button { Text = "Entrada de mercadoria", Width = 170, Height = 34 };
        actions.Controls.AddRange([_operation, _quantityLabel, _quantity, _reason, apply, fullEntry, history]);
        if (!SessaoUsuario.Administrador)
        {
            _operation.Enabled = false; _quantity.Enabled = false; _reason.Enabled = false; apply.Enabled = false; fullEntry.Enabled = false;
        }
        root.Controls.Add(filters, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(actions, 0, 2);
        root.Controls.Add(_movementPreview, 0, 3);
        Controls.Add(root);
        _search.TextChanged += (_, _) => LoadProducts();
        _search.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await HandleUnknownBarcodeAsync(_search.Text.Trim());
        };
        _category.SelectedIndexChanged += (_, _) => LoadProducts();
        _unit.SelectedIndexChanged += (_, _) => LoadProducts();
        _status.SelectedIndexChanged += (_, _) => LoadProducts();
        _grid.SelectionChanged += (_, _) => UpdateUnitPrecision();
        _operation.SelectedIndexChanged += (_, _) => UpdateOperationLabel();
        _quantity.ValueChanged += (_, _) => UpdateMovementPreview();
        apply.Click += (_, _) => ApplyMovement();
        history.Click += (_, _) => ShowHistory();
        fullEntry.Click += (_, _) => { using var form = new EntradaMercadoriaForm(_search.Text.Trim()); if (form.ShowDialog(this) == DialogResult.OK) LoadProducts(); };
    }

    private static DataGridViewTextBoxColumn GridColumn(string property, string title, int weight, string? format = null) => new() { DataPropertyName = property, HeaderText = title, FillWeight = weight, DefaultCellStyle = new DataGridViewCellStyle { Format = format ?? "" } };

    private void LoadCategories()
    {
        var values = new ProdutoService().ListarCategorias();
        values.Insert(0, new OpcaoCadastro { Id = null, Nome = "Todas as categorias" });
        _category.DataSource = values;
        _category.DisplayMember = nameof(OpcaoCadastro.Nome);
        _category.ValueMember = nameof(OpcaoCadastro.Id);
    }

    private void LoadProducts()
    {
        int? categoryId = (_category.SelectedItem as OpcaoCadastro)?.Id;
        string? unit = _unit.SelectedItem?.ToString() is "UN" or "KG" ? _unit.SelectedItem.ToString() : null;
        int status = _status.SelectedIndex;
        _grid.DataSource = _service.Listar(_search.Text).Where(product => product.Ativo
            && (!categoryId.HasValue || product.CategoriaId == categoryId)
            && (unit is null || product.UnidadeMedida == unit)
            && (status == 0 || (status == 1 ? product.EstoqueAtual <= product.EstoqueMinimo : product.EstoqueAtual > product.EstoqueMinimo)))
            .Select(product => new { Product = product, product.CodigoBarras, product.Nome, product.CategoriaNome, product.UnidadeMedida, product.EstoqueAtual, product.EstoqueMinimo, product.DataValidade, StatusEstoque = product.EstoqueAtual <= product.EstoqueMinimo ? "BAIXO" : "NORMAL" }).ToList();
        UpdateUnitPrecision();
    }

    private Produto? SelectedProduct() => _grid.CurrentRow?.DataBoundItem?.GetType().GetProperty("Product")?.GetValue(_grid.CurrentRow.DataBoundItem) as Produto;

    private void UpdateUnitPrecision()
    {
        bool unitCount = SelectedProduct()?.UnidadeMedida == "UN";
        if (unitCount && _quantity.Value % 1 != 0) _quantity.Value = Math.Max(1, decimal.Truncate(_quantity.Value));
        _quantity.DecimalPlaces = unitCount ? 0 : 3;
        _quantity.Increment = unitCount ? 1 : 0.001m;
        UpdateOperationLabel();
    }

    private void UpdateOperationLabel()
    {
        _quantityLabel.Text = _operation.SelectedItem?.ToString() switch
        {
            "Saída" => "Quantidade a retirar",
            "Ajustar saldo" => "Novo saldo contado",
            _ => "Quantidade a adicionar"
        };
        bool unitCount = SelectedProduct()?.UnidadeMedida == "UN";
        decimal minimum = _operation.SelectedItem?.ToString() == "Ajustar saldo" ? 0 : (unitCount ? 1 : 0.001m);
        _quantity.Minimum = minimum;
        if (_quantity.Value < minimum) _quantity.Value = minimum;
        UpdateMovementPreview();
    }

    private void UpdateMovementPreview()
    {
        Produto? product = SelectedProduct();
        if (product is null) { _movementPreview.Text = "Selecione um produto para ver o saldo após a movimentação."; return; }
        string operation = _operation.SelectedItem?.ToString() ?? "Entrada";
        decimal next = operation switch
        {
            "Entrada" => product.EstoqueAtual + _quantity.Value,
            "Saída" => product.EstoqueAtual - _quantity.Value,
            _ => _quantity.Value
        };
        decimal difference = next - product.EstoqueAtual;
        _movementPreview.Text = $"{product.Nome} · saldo atual: {product.EstoqueAtual:0.###} {product.UnidadeMedida} → novo saldo: {next:0.###} {product.UnidadeMedida} (diferença: {difference:+0.###;-0.###;0} {product.UnidadeMedida})";
        _movementPreview.ForeColor = next < 0 ? UiTheme.Error : UiTheme.Muted;
    }

    private void ApplyMovement()
    {
        Produto? product = SelectedProduct();
        if (product is null) { UiDialog.Info(this, "Selecione um produto", "Escolha um produto na tabela antes de registrar a movimentação."); return; }
        try
        {
            string operation = _operation.SelectedItem?.ToString() ?? "Entrada";
            if (operation == "Ajustar saldo" && string.IsNullOrWhiteSpace(_reason.Text))
            {
                UiDialog.Warning(this, "Motivo obrigatório", "Informe por que o saldo será ajustado.");
                _reason.Focus();
                return;
            }
            if (operation == "Ajustar saldo" && !UiDialog.Confirm(this, "Confirmar ajuste de saldo", $"{product.Nome}\nSaldo atual: {product.EstoqueAtual:0.###} {product.UnidadeMedida}\nNovo saldo: {_quantity.Value:0.###} {product.UnidadeMedida}\nMotivo: {_reason.Text.Trim()}")) return;
            if (operation == "Ajustar saldo") _service.AjustarSaldo(product.Id, _quantity.Value, _reason.Text);
            else _service.RegistrarMovimentacao(product.Id, operation, _quantity.Value, _reason.Text);
            LoadProducts();
            UiDialog.Success(this, "Movimentação registrada", "O saldo e o histórico de estoque foram atualizados.");
        }
        catch (Exception exception) { UiDialog.Warning(this, "Movimentação não registrada", exception.Message); }
    }

    private void ShowHistory()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        var search = new TextBox { Width = 220, PlaceholderText = "Produto ou código" };
        if (SelectedProduct() is { } selected) search.Text = selected.Nome;
        var start = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 125, Value = DateTime.Today.AddDays(-30) };
        var end = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 125, Value = DateTime.Today };
        var type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        type.Items.AddRange(["Todos os tipos", "ENTRADA", "SAÍDA", "AJUSTE", "VENDA"]); type.SelectedIndex = 0;
        filters.Controls.AddRange([new Label { Text = "Produto", AutoSize = true, Margin = new Padding(2, 9, 2, 0) }, search, new Label { Text = "De", AutoSize = true, Margin = new Padding(2, 9, 2, 0) }, start, new Label { Text = "Até", AutoSize = true, Margin = new Padding(2, 9, 2, 0) }, end, type]);
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = false };
        UiTheme.StyleGrid(grid);
        void RefreshHistory()
        {
            grid.DataSource = _service.ListarMovimentacoes(inicio: start.Value.Date, fim: end.Value.Date, tipo: type.SelectedIndex == 0 ? null : type.SelectedItem?.ToString(), pesquisaProduto: search.Text);
            if (grid.Columns[nameof(MovimentoEstoqueLinha.Data)] is { } date) date.DefaultCellStyle.Format = "dd/MM/yyyy HH:mm";
        }
        search.TextChanged += (_, _) => RefreshHistory(); start.ValueChanged += (_, _) => RefreshHistory(); end.ValueChanged += (_, _) => RefreshHistory(); type.SelectedIndexChanged += (_, _) => RefreshHistory();
        root.Controls.Add(filters, 0, 0); root.Controls.Add(grid, 0, 1);
        using var form = new Form { Text = "Histórico de estoque", StartPosition = FormStartPosition.CenterParent, MinimumSize = new Size(860, 500), Size = new Size(1020, 620), BackColor = UiTheme.Background };
        form.Controls.Add(root);
        UiWindowChrome.Apply(form);
        RefreshHistory();
        form.ShowDialog(this);
    }

    private async Task HandleUnknownBarcodeAsync(string barcode)
    {
        if (barcode.Length is < 8 or > 14 || !barcode.All(char.IsDigit)) return;
        var products = new ProdutoService();
        if (products.BuscarPorCodigoBarras(barcode) is not null) return;
        using var prompt = new Form { Text = "Produto não cadastrado", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, ClientSize = new Size(440, 246), MaximizeBox = false, MinimizeBox = false, BackColor = UiTheme.Background, ForeColor = UiTheme.Text };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        layout.Controls.Add(new Label { Text = $"PRODUTO NÃO CADASTRADO\nCódigo: {barcode}\n\nVocê pode cadastrar manualmente ou consultar o catálogo público.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = true };
        var lookup = new Button { Text = "BUSCAR INFORMAÇÕES", Width = 170, Height = 36, DialogResult = DialogResult.Retry };
        var manual = new Button { Text = "CADASTRAR PRODUTO", Width = 170, Height = 36, DialogResult = DialogResult.Yes, Enabled = SessaoUsuario.Administrador };
        var cancel = new Button { Text = "VOLTAR", Width = 90, Height = 36, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([lookup, manual, cancel]); layout.Controls.Add(buttons, 0, 1); prompt.Controls.Add(layout); prompt.AcceptButton = lookup; prompt.CancelButton = cancel; UiWindowChrome.Apply(prompt);
        var result = prompt.ShowDialog(this);
        if (result == DialogResult.Yes) { OpenProduct(); return; }
        if (result != DialogResult.Retry) return;
        try
        {
            UseWaitCursor = true;
            var found = await new OpenFoodFactsService().BuscarPorCodigoAsync(barcode);
            if (found is null) { UiDialog.Info(this, "Produto não encontrado", "A consulta pública não retornou dados. O estoque continua disponível para operação manual."); return; }
            string review = $"Nome: {found.Nome}\nMarca: {found.Marca ?? "não informada"}\nCategoria: {found.Categoria ?? "não informada"}\nImagem: {(found.ImagemUrl is null ? "indisponível" : "disponível")}\n\nAbrir cadastro para revisar antes de salvar?";
            if (UiDialog.Confirm(this, "Revise os dados públicos", review) && SessaoUsuario.Administrador)
                OpenProduct(new Produto { CodigoBarras = found.CodigoBarras, Nome = found.Nome, Marca = found.Marca, ImagemUrl = found.ImagemUrl });
            else if (!SessaoUsuario.Administrador) UiDialog.Info(this, "Consulta somente leitura", "Os dados foram consultados. O cadastro de produtos exige perfil administrador.");
        }
        catch (Exception ex) { AppLogger.Error(ex, "Consultar código de barras no estoque"); UiDialog.Warning(this, "Busca indisponível", "Não foi possível consultar agora. Você pode tentar novamente mais tarde."); }
        finally { UseWaitCursor = false; _search.Focus(); }

        void OpenProduct(Produto? draft = null)
        {
            if (!SessaoUsuario.Administrador) { UiDialog.Info(this, "Acesso restrito", "O cadastro de produtos exige perfil administrador."); return; }
            using var form = new ProdutoForm(products, draft, barcode);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                LoadProducts();
                foreach (DataGridViewRow row in _grid.Rows)
                    if (string.Equals(Convert.ToString(row.Cells[0].Value), barcode, StringComparison.OrdinalIgnoreCase)) { row.Selected = true; _grid.CurrentCell = row.Cells[0]; break; }
            }
        }
    }
}
