using CaixaLume.Models;
using CaixaLume.Services;

namespace CaixaLume.Forms;

internal sealed class PoliticaAjustesForm : Form
{
    internal PoliticaAjustesForm()
    {
        Autorizacao.ExigirAdministrador();
        Text = "Regras de desconto e acréscimo";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(620, 440); MinimumSize = new Size(540, 400);
        var service = new AjustesVendaService();
        var policy = service.ObterPolitica();
        var enabled = new CheckBox { Text = "Habilitar ajustes nas vendas", Checked = policy.Habilitada, AutoSize = true };
        var operators = new CheckBox { Text = "Permitir que operadores apliquem os limites definidos", Checked = policy.PermitirOperador, AutoSize = true };
        var discount = new NumericUpDown { Maximum = 100, DecimalPlaces = 2, Value = policy.MaxDescontoPercentual, Dock = DockStyle.Fill };
        var surcharge = new NumericUpDown { Maximum = 1_000_000_000, DecimalPlaces = 2, Value = Math.Min(policy.MaxAcrescimoValor, 1_000_000_000), Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        void Wide(Control control, int height) { int row = layout.RowCount++; layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); layout.Controls.Add(control, 0, row); layout.SetColumnSpan(control, 2); }
        Wide(new Label { Text = "Defina a política da loja antes de habilitar. Limite zero impede a concessão correspondente. Vendas anteriores serão preservadas.", Dock = DockStyle.Fill }, 64);
        Wide(enabled, 42); Wide(operators, 52);
        int r = layout.RowCount++; layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.Controls.Add(new Label { Text = "Desconto máximo (%)", AutoSize = true }, 0, r); layout.Controls.Add(discount, 1, r);
        r = layout.RowCount++; layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.Controls.Add(new Label { Text = "Acréscimo máximo (R$)", AutoSize = true }, 0, r); layout.Controls.Add(surcharge, 1, r);
        var save = new Button { Text = "Salvar política", AutoSize = true };
        Wide(save, 48); Controls.Add(layout); UiTheme.Apply(this);
        save.Click += (_, _) =>
        {
            try { service.SalvarPolitica(new PoliticaAjustesVenda { Habilitada = enabled.Checked, PermitirOperador = operators.Checked, MaxDescontoPercentual = discount.Value, MaxAcrescimoValor = surcharge.Value }); DialogResult = DialogResult.OK; Close(); }
            catch (Exception exception) { UiDialog.Warning(this, "Política não salva", exception.Message); }
        };
        AcceptButton = save;
    }
}
