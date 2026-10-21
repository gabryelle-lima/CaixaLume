namespace MercadinhoPdv.Forms;

internal sealed class DespesaDialog : Form
{
    private readonly TextBox _description = new() { CharacterCasing = CharacterCasing.Upper, Dock = DockStyle.Fill };
    private readonly NumericUpDown _amount = new() { Minimum = 0.01m, Maximum = 99999999, DecimalPlaces = 2, Increment = 1, ThousandsSeparator = true, Dock = DockStyle.Fill };
    private readonly ComboBox _payment = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    public string Descricao => _description.Text.Trim();
    public decimal Valor => _amount.Value;
    public string FormaPagamento => _payment.SelectedItem?.ToString() ?? "";
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<string?>? ValidateAndSave { get; set; }

    public DespesaDialog()
    {
        Text = "Registrar despesa";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        Size = new Size(500, 300);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        AddRow(layout, "Descrição", _description, 0);
        AddRow(layout, "Valor", _amount, 1);
        _payment.Items.AddRange(["DINHEIRO", "PIX", "DÉBITO", "CRÉDITO"]);
        _payment.SelectedIndex = 0;
        AddRow(layout, "Forma de pagamento", _payment, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "Registrar despesa", Width = 150, Height = 38 };
        var cancel = new Button { Text = "Cancelar", Width = 100, Height = 38, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([save, cancel]);
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
        UiTheme.Apply(this);
        save.Click += (_, _) =>
        {
            try
            {
                string? message = ValidateAndSave?.Invoke();
                if (!string.IsNullOrWhiteSpace(message)) { UiDialog.Warning(this, "Revise a despesa", message); _description.Focus(); return; }
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception exception) { UiDialog.Warning(this, "Despesa não registrada", exception.Message); _description.Focus(); }
        };
        AcceptButton = save;
        CancelButton = cancel;
    }

    private static void AddRow(TableLayoutPanel layout, string label, Control control, int row)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        layout.Controls.Add(control, 1, row);
    }
}
