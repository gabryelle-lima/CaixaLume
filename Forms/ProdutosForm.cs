using CaixaLume.Models;
using CaixaLume.Services;

namespace CaixaLume.Forms;

internal sealed class ProdutosForm : Form
{
    private readonly ProdutoService _service = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _search = new();
    private readonly Button _edit = new() { Text = "Editar", Enabled = false };
    private readonly Button _deactivate = new() { Text = "Desativar", Enabled = false };
    private readonly Button _delete = new() { Text = "Excluir produto", Enabled = false };
    private readonly ComboBox _category = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly CheckBox _showInactive = new() { Text = "Incluir inativos", Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, AutoSize = false, Size = new Size(140, 34), TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(14, 3, 0, 0) };

    public ProdutosForm()
    {
        Text = "Produtos";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(860, 540);
        Size = new Size(1200, 700);
        BuildLayout();
        UiTheme.Apply(this);
        _delete.Visible = Autorizacao.PodeExcluirProduto();
        UpdateInactiveToggle();
        LoadCategories();
        LoadProducts();
    }

    private void BuildLayout()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(16, 13, 16, 8), WrapContents = false, AutoScroll = true };
        var add = new Button { Text = "Novo produto", AutoSize = true, Height = 34 };
        _search.PlaceholderText = "Pesquisar por nome ou código de barras";
        _search.Width = 310;
        _search.Margin = new Padding(18, 3, 0, 0);
        _category.Margin = new Padding(14, 3, 0, 0);
        top.Controls.AddRange([add, _search, _category, _showInactive]);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _grid.AllowUserToResizeColumns = true;
        UiTheme.StyleGrid(_grid);
        _grid.Columns.AddRange(
            Column("CodigoBarras", "Código", 14), Column("Nome", "Produto", 23), Column("CategoriaNome", "Categoria", 14),
            Column("UnidadeMedida", "Unid.", 7), Column("PrecoVenda", "Preço de venda", 12, "C2"),
            Column("EstoqueAtual", "Estoque", 9, "0.###"), Column("DataValidade", "Validade", 10, "dd/MM/yyyy"), Column("Status", "Status", 11));
        int[] minimumWidths = [135, 200, 140, 65, 125, 85, 105, 205];
        for (int index = 0; index < _grid.Columns.Count; index++) _grid.Columns[index].MinimumWidth = minimumWidths[index];
        _grid.ShowCellToolTips = true;

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 66, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 10, 16, 8), WrapContents = false };
        _edit.AutoSize = false;
        _edit.Size = new Size(112, 38);
        _deactivate.AutoSize = false;
        _deactivate.Size = new Size(130, 38);
        _delete.AutoSize = false;
        _delete.Size = new Size(150, 38);
        bottom.Controls.AddRange([_delete, _deactivate, _edit]);
        if (!SessaoUsuario.Administrador) { add.Enabled = false; _edit.Enabled = false; _deactivate.Enabled = false; }
        Controls.AddRange([_grid, bottom, top]);

        add.Click += (_, _) => OpenEditor(null);
        _edit.Click += (_, _) => OpenEditor(SelectedProduct());
        _deactivate.Click += (_, _) => ChangeActiveSelected();
        _delete.Click += (_, _) => DeleteSelected();
        _search.TextChanged += (_, _) => LoadProducts();
        _category.SelectedIndexChanged += (_, _) => LoadProducts();
        _showInactive.CheckedChanged += (_, _) => { UpdateInactiveToggle(); LoadProducts(); };
        _grid.SelectionChanged += (_, _) => UpdateActions();
        _grid.CellDoubleClick += (_, _) => OpenEditor(SelectedProduct());
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].DataPropertyName != "Status") return;
            string status = Convert.ToString(e.Value) ?? "";
            if (status.StartsWith("Vencido", StringComparison.Ordinal)) e.CellStyle.ForeColor = UiTheme.Error;
            else if (status.StartsWith("Validade próxima", StringComparison.Ordinal) || status.Contains("Estoque baixo", StringComparison.Ordinal)) e.CellStyle.ForeColor = UiTheme.Warning;
            else if (Equals(e.Value, "Inativo") || Equals(e.Value, "Validade não informada")) e.CellStyle.ForeColor = UiTheme.Muted;
            else e.CellStyle.ForeColor = UiTheme.Success;
        };
    }

    private static DataGridViewTextBoxColumn Column(string property, string title, int fillWeight, string? format = null) => new()
    {
        DataPropertyName = property, HeaderText = title, FillWeight = fillWeight, DefaultCellStyle = new DataGridViewCellStyle { Format = format ?? string.Empty }
    };

    private void LoadProducts()
    {
        int? categoryId = (_category.SelectedItem as OpcaoCadastro)?.Id;
        var rows = _service.Listar(_search.Text).Where(product => (_showInactive.Checked || product.Ativo) && (!categoryId.HasValue || product.CategoriaId == categoryId)).Select(product => new
        {
            Produto = product, product.CodigoBarras, product.Nome, CategoriaNome = product.CategoriaNome ?? "—",
            product.PrecoVenda, product.EstoqueAtual, product.UnidadeMedida, product.DataValidade,
            Status = !product.Ativo ? "Inativo" : (product.DataValidade?.Date < DateTime.Today ? "Vencido" : product.DataValidade?.Date <= DateTime.Today.AddDays(30) ? "Validade próxima" : product.DataValidade is null ? "Validade não informada" : "Dentro da validade") + (product.EstoqueAtual <= product.EstoqueMinimo ? " · Estoque baixo" : "")
        }).ToList();
        _grid.DataSource = rows;
        _grid.ClearSelection();
        UpdateActions();
    }

    private void LoadCategories()
    {
        var categories = _service.ListarCategorias();
        categories.Insert(0, new OpcaoCadastro { Id = null, Nome = "Todas as categorias" });
        _category.DataSource = categories;
        _category.DisplayMember = nameof(OpcaoCadastro.Nome);
        _category.ValueMember = nameof(OpcaoCadastro.Id);
    }

    private Produto? SelectedProduct()
    {
        if (_grid.CurrentRow is not { Selected: true } row) return null;
        return row.DataBoundItem?.GetType().GetProperty("Produto")?.GetValue(row.DataBoundItem) as Produto;
    }

    private void UpdateActions()
    {
        Produto? product = SelectedProduct();
        _edit.Enabled = SessaoUsuario.Administrador && product is not null;
        _deactivate.Enabled = SessaoUsuario.Administrador && product is not null;
        _deactivate.Text = product?.Ativo == false ? "Reativar" : "Desativar";
        _delete.Enabled = _delete.Visible && product is not null;
    }

    private void UpdateInactiveToggle()
    {
        _showInactive.Text = _showInactive.Checked ? "Inativos visíveis ✓" : "Incluir inativos";
        _showInactive.BackColor = _showInactive.Checked ? Color.FromArgb(22, 83, 67) : UiTheme.SurfaceRaised;
        _showInactive.ForeColor = _showInactive.Checked ? UiTheme.Success : UiTheme.Text;
        _showInactive.FlatAppearance.BorderColor = _showInactive.Checked ? UiTheme.Accent : UiTheme.Outline;
    }

    private void DeleteSelected()
    {
        Produto? product = SelectedProduct();
        if (product is null || !Autorizacao.PodeExcluirProduto()) return;
        if (!UiDialog.Confirm(this, "Excluir produto", $"Excluir {product.Nome} do catálogo?\n\nEle deixará de aparecer nas listas e seu código de barras ficará livre para outro cadastro. Vendas e movimentações antigas continuarão no histórico.", true)) return;
        try
        {
            _service.Excluir(product.Id);
            LoadProducts();
            UiDialog.Success(this, "Produto excluído", "O produto foi retirado do catálogo. O histórico foi preservado.");
        }
        catch (Exception exception) { UiDialog.Error(this, "Não foi possível excluir", exception.Message); }
    }

    private void OpenEditor(Produto? product)
    {
        if (!SessaoUsuario.Administrador) return;
        if (product is not null) product = _service.BuscarPorId(product.Id);
        using var form = new ProdutoForm(_service, product);
        if (form.ShowDialog(this) == DialogResult.OK) LoadProducts();
    }

    private void ChangeActiveSelected()
    {
        Produto? product = SelectedProduct();
        if (product is null) return;
        bool reativar = !product.Ativo;
        if (!UiDialog.Confirm(this, reativar ? "Reativar produto" : "Desativar produto", $"{(reativar ? "Reativar" : "Desativar")} {product.Nome}? O histórico de vendas e estoque será preservado.", !reativar)) return;
        try
        {
            if (reativar) _service.Reativar(product.Id); else _service.Desativar(product.Id);
            LoadProducts();
        }
        catch (Exception exception)
        {
            UiDialog.Error(this, "Não foi possível desativar", exception.Message);
        }
    }
}
