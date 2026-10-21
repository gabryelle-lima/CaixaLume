using MercadinhoPdv.Models;
using MercadinhoPdv.Services;

namespace MercadinhoPdv.Forms;

internal sealed class PesoProdutoForm : Form
{
    private readonly TextBox _weight = new() { Dock = DockStyle.Fill, PlaceholderText = "Ex.: 0,315" };
    private readonly Label _error = new() { Dock = DockStyle.Fill, ForeColor = UiTheme.Error, AutoEllipsis = true };
    public decimal Peso { get; private set; }

    public PesoProdutoForm(Produto product, decimal available)
    {
        Text = "Informar peso";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(430, 270);
        Size = new Size(480, 300);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label { Text = product.Nome, Dock = DockStyle.Fill, ForeColor = UiTheme.Text, Font = new Font("Segoe UI", 12, FontStyle.Bold), AutoEllipsis = true }, 0, 0);
        root.Controls.Add(new Label { Text = $"{product.PrecoVenda:C2}/kg · disponível: {available:0.###} kg", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted }, 0, 1);
        root.Controls.Add(_weight, 0, 2);
        root.Controls.Add(_error, 0, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var add = new Button { Text = "Adicionar peso", Width = 150, Height = 38 };
        var cancel = new Button { Text = "Cancelar", Width = 100, Height = 38, DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange([add, cancel]); root.Controls.Add(actions, 0, 4);
        Controls.Add(root); UiTheme.Apply(this);
        Shown += (_, _) => _weight.Focus();
        add.Click += (_, _) =>
        {
            try { Peso = QuantidadeService.Interpretar(_weight.Text, "KG", available); DialogResult = DialogResult.OK; Close(); }
            catch (InvalidOperationException exception) { _error.Text = exception.Message; _weight.Focus(); _weight.SelectAll(); }
        };
        AcceptButton = add; CancelButton = cancel;
    }
}
