using System.Globalization;

namespace MercadinhoPdv.Forms;

internal sealed class CadastroDialog : Form
{
    private readonly Dictionary<string, TextBox> _inputs = new();
    public Dictionary<string, string> Values => _inputs.ToDictionary(item => item.Key, item => item.Value.Text.Trim());
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<IReadOnlyDictionary<string, string>, string?>? ValidateAndSave { get; set; }

    public void SetValues(IReadOnlyDictionary<string, string> values)
    {
        foreach (var value in values)
            if (_inputs.TryGetValue(value.Key, out TextBox? input)) input.Text = value.Value;
    }

    public CadastroDialog(string title, params string[] fields)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Width = 430;
        Height = 175 + fields.Length * 48;
        MinimumSize = new Size(420, 240);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = fields.Length + 2 };
        layout.AutoScroll = true; layout.AutoScrollMinSize = new Size(350, fields.Length * 42 + 90);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        int row = 0;
        foreach (string field in fields)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.Controls.Add(new Label { Text = field, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            var input = new TextBox { Dock = DockStyle.Fill };
            if (field.Contains("email", StringComparison.OrdinalIgnoreCase)) input.Tag = "preserve-case";
            if (!field.Contains("email", StringComparison.OrdinalIgnoreCase)
                && !field.Contains("telefone", StringComparison.OrdinalIgnoreCase)
                && !field.Contains("documento", StringComparison.OrdinalIgnoreCase)
                && !field.Contains("código", StringComparison.OrdinalIgnoreCase)
                && !field.Contains("valor", StringComparison.OrdinalIgnoreCase)
                && !field.Contains("saldo", StringComparison.OrdinalIgnoreCase)) input.CharacterCasing = CharacterCasing.Upper;
            if (field.Contains("valor", StringComparison.OrdinalIgnoreCase) || field.Contains("saldo", StringComparison.OrdinalIgnoreCase))
            {
                input.TextAlign = HorizontalAlignment.Right;
                input.Enter += (_, _) => input.BeginInvoke(new Action(input.SelectAll));
                input.KeyPress += (_, e) => { if (e.KeyChar is '.' or ',') e.KeyChar = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0]; };
            }
            _inputs[field] = input;
            layout.Controls.Add(input, 1, row++);
        }
        var error = new Label { Dock = DockStyle.Fill, ForeColor = UiTheme.Error, AutoEllipsis = true };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(error, 0, row);
        layout.SetColumnSpan(error, 2);
        row++;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "Salvar", Width = 90 };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Width = 90 };
        buttons.Controls.AddRange([save, cancel]);
        layout.Controls.Add(buttons, 0, row);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
        UiTheme.Apply(this);
        save.Click += (_, _) =>
        {
            try
            {
                string? message = ValidateAndSave?.Invoke(Values);
                if (!string.IsNullOrWhiteSpace(message)) { error.Text = message; _inputs.Values.FirstOrDefault()?.Focus(); return; }
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception exception) { error.Text = exception.Message; _inputs.Values.FirstOrDefault()?.Focus(); }
        };
        AcceptButton = save;
        CancelButton = cancel;
    }
}
