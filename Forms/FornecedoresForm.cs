using CaixaLume.Diagnostics;
using CaixaLume.Models;
using CaixaLume.Services;

namespace CaixaLume.Forms;

internal sealed class FornecedoresForm : Form
{
    private readonly FornecedorService _supplierService = new();
    private readonly CategoriaService _categoryService = new();
    private readonly DataGridView _suppliers = Grid();
    private readonly DataGridView _categories = Grid();
    private readonly TextBox _categorySearch = new() { PlaceholderText = "Pesquisar categoria", Width = 210, Margin = new Padding(8, 4, 0, 0) };

    public FornecedoresForm()
    {
        Text = "Fornecedores e categorias";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 500);
        Size = new Size(900, 600);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var suppliers = new TabPage("Fornecedores");
        var categories = new TabPage("Categorias");
        suppliers.Controls.Add(BuildPage(_suppliers, "Novo fornecedor", "Desativar", AddSupplier, DeactivateSupplier));
        categories.Controls.Add(BuildPage(_categories, "Nova categoria", "Desativar", AddCategory, DeactivateCategory, _categorySearch));
        tabs.TabPages.AddRange([suppliers, categories]);
        Controls.Add(tabs);
        UiTheme.Apply(this);
        _suppliers.CellDoubleClick += (_, _) => EditSupplier();
        _categories.CellDoubleClick += (_, _) => EditCategory();
        _categorySearch.TextChanged += (_, _) => LoadCategories();
        LoadData();
    }

    private static DataGridView Grid()
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AutoGenerateColumns = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
        UiTheme.StyleGrid(grid);
        return grid;
    }

    private static Control BuildPage(DataGridView grid, string addText, string removeText, Action add, Action remove, Control? search = null)
    {
        var page = new Panel { Dock = DockStyle.Fill };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(8), WrapContents = false, AutoScroll = true };
        var addButton = new Button { Text = addText, AutoSize = true, Height = 34 };
        var removeButton = new Button { Text = removeText, AutoSize = true, Height = 34 };
        addButton.Click += (_, _) => add();
        removeButton.Click += (_, _) => remove();
        bar.Controls.AddRange([addButton, removeButton]);
        if (search is not null) bar.Controls.Add(search);
        page.Controls.AddRange([grid, bar]);
        return page;
    }

    private void LoadData()
    {
        _suppliers.DataSource = _supplierService.Listar(null,true).Select(supplier => new Fornecedor { Id = supplier.Id, Nome = supplier.Nome, Telefone = supplier.Telefone, Email = supplier.Email, Documento = supplier.Documento, Endereco=supplier.Endereco, Observacao=supplier.Observacao, Ativo=supplier.Ativo }).ToList();
        LoadCategories();
    }

    private void LoadCategories() => _categories.DataSource = _categoryService.Listar(_categorySearch.Text);

    private void AddSupplier()
    {
        using var dialog = new CadastroDialog("Novo fornecedor", "Nome", "Telefone", "Email", "Documento", "Endereço", "Observação");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var value = dialog.Values;
        try
        {
            _supplierService.Salvar(new Fornecedor { Nome = value["Nome"], Telefone = value["Telefone"], Email = value["Email"], Documento = value["Documento"], Endereco=value["Endereço"], Observacao=value["Observação"] });
            LoadData();
            UiDialog.Success(this, "Fornecedor cadastrado", "O fornecedor foi salvo.");
        }
        catch (Exception exception) { ShowError(exception, "Cadastrar fornecedor"); }
    }

    private void EditSupplier()
    {
        if (_suppliers.CurrentRow?.DataBoundItem is not Fornecedor supplier) return;
        using var dialog = new CadastroDialog("Editar fornecedor", "Nome", "Telefone", "Email", "Documento", "Endereço", "Observação");
        dialog.SetValues(new Dictionary<string, string> { ["Nome"] = supplier.Nome, ["Telefone"] = supplier.Telefone ?? "", ["Email"] = supplier.Email ?? "", ["Documento"] = supplier.Documento ?? "", ["Endereço"]=supplier.Endereco??"", ["Observação"]=supplier.Observacao??"" });
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var value = dialog.Values;
        supplier.Nome = value["Nome"];
        supplier.Telefone = value["Telefone"];
        supplier.Email = value["Email"];
        supplier.Documento = value["Documento"];
        supplier.Endereco = value["Endereço"];
        supplier.Observacao = value["Observação"];
        try { _supplierService.Salvar(supplier); LoadData(); UiDialog.Success(this, "Fornecedor atualizado", "As alterações foram salvas."); }
        catch (Exception exception) { ShowError(exception, "Atualizar fornecedor"); }
    }

    private void DeactivateSupplier()
    {
        if (_suppliers.CurrentRow?.DataBoundItem is not Fornecedor supplier || !Confirm(supplier.Nome)) return;
        try { _supplierService.Desativar(supplier.Id); LoadData(); }
        catch (Exception exception) { ShowError(exception, "Desativar fornecedor"); }
    }

    private void AddCategory()
    {
        using var dialog = new CadastroDialog("Nova categoria", "Nome");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { _categoryService.Salvar(new Categoria { Nome = dialog.Values["Nome"] }); LoadCategories(); UiDialog.Success(this, "Categoria cadastrada", "A categoria foi salva."); }
        catch (Exception exception) { ShowError(exception, "Cadastrar categoria"); }
    }

    private void EditCategory()
    {
        if (_categories.CurrentRow?.DataBoundItem is not Categoria category) return;
        using var dialog = new CadastroDialog("Editar categoria", "Nome");
        dialog.SetValues(new Dictionary<string, string> { ["Nome"] = category.Nome });
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        category.Nome = dialog.Values["Nome"];
        try { _categoryService.Salvar(category); LoadCategories(); UiDialog.Success(this, "Categoria atualizada", "As alterações foram salvas."); }
        catch (Exception exception) { ShowError(exception, "Atualizar categoria"); }
    }

    private void DeactivateCategory()
    {
        if (_categories.CurrentRow?.DataBoundItem is not Categoria category || !Confirm(category.Nome)) return;
        try { _categoryService.Desativar(category.Id); LoadCategories(); }
        catch (Exception exception) { ShowError(exception, "Desativar categoria"); }
    }

    private bool Confirm(string name) => UiDialog.Confirm(this, "Desativar cadastro", $"Desativar {name}? Os registros históricos serão preservados.", true);

    private void ShowError(Exception exception, string operation)
    {
        AppLogger.Error(exception, operation);
        UiDialog.Warning(this, operation, $"{exception.Message}\n\nDetalhes registrados em: {AppLogger.LogPath}");
    }
}
