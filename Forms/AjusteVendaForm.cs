using CaixaLume.Models;
using CaixaLume.Services;

namespace CaixaLume.Forms;

internal sealed class AjusteVendaForm : Form
{
    internal AjusteVenda Ajuste { get; private set; }
    internal AjusteVendaForm(IReadOnlyCollection<ItemCarrinho> itens, AjusteVenda atual)
    {
        Ajuste = atual;
        Text = "Desconto e acréscimo"; StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(440, 390); Size = new Size(520, 450);
        var service = new AjustesVendaService();
        var type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        type.Items.AddRange(["VALOR", "PERCENTUAL"]); type.SelectedItem = atual.TipoDesconto;
        var discount = new NumericUpDown { Maximum = 1_000_000_000, DecimalPlaces = 2, Value = atual.Desconto, Dock = DockStyle.Fill };
        var surcharge = new NumericUpDown { Maximum = 1_000_000_000, DecimalPlaces = 2, Value = atual.Acrescimo, Dock = DockStyle.Fill };
        var preview = new Label { AutoSize = true }; var error = new Label { ForeColor = UiTheme.Error, Dock = DockStyle.Fill };
        var apply = new Button { Text = "Aplicar à venda", AutoSize = true };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        Control[] fields = [type, discount, surcharge, preview]; string[] labels = ["Tipo de desconto", "Desconto (R$ ou %)", "Acréscimo (R$)", "Total ajustado"];
        for (int i = 0; i < fields.Length; i++) { layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); layout.Controls.Add(new Label { Text = labels[i], AutoSize = true }, 0, i); layout.Controls.Add(fields[i], 1, i); }
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.Controls.Add(error, 0, 4); layout.SetColumnSpan(error, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false }; buttons.Controls.AddRange([apply, cancel]);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); layout.Controls.Add(buttons, 0, 5); layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout); UiTheme.Apply(this); AcceptButton = apply; CancelButton = cancel;
        void Calculate()
        {
            try { Ajuste = new AjusteVenda { TipoDesconto = type.Text, Desconto = discount.Value, Acrescimo = surcharge.Value }; preview.Text = service.Calcular(itens, Ajuste).Total.ToString("C2"); error.Text = ""; apply.Enabled = true; }
            catch (InvalidOperationException exception) { error.Text = exception.Message; apply.Enabled = false; }
        }
        type.SelectedIndexChanged += (_, _) => Calculate(); discount.ValueChanged += (_, _) => Calculate(); surcharge.ValueChanged += (_, _) => Calculate();
        apply.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); }; Calculate();
    }
}
