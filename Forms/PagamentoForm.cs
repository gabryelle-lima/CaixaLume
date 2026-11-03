using System.Globalization;
using CaixaLume.Services;

namespace CaixaLume.Forms;

internal sealed class PagamentoForm : Form
{
    private readonly decimal _total;
    private readonly Label _change = new();
    private readonly Label _validation = new() { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(248, 113, 113), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private Control? _cashEntryBlock;
    private readonly TextBox _received = new() { Dock = DockStyle.Fill, Font = new Font("Consolas", 12, FontStyle.Bold), TextAlign = HorizontalAlignment.Right };
    private readonly Label _changeCaption = new() { Text = "Troco", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _cancel = new() { Text = "CANCELAR", Dock = DockStyle.Fill, DialogResult = DialogResult.Cancel };
    private readonly Button _confirm = new() { Text = "CONFIRMAR VENDA", Dock = DockStyle.Fill, Enabled = false };
    private readonly Dictionary<string, Button> _methods = new(StringComparer.OrdinalIgnoreCase);
    public string FormaPagamento { get; private set; } = "";
    public decimal ValorRecebido => FormaPagamento == "DINHEIRO" && EntradaNumerica.TryParseDinheiro(_received.Text, out decimal value) ? value : _total;
    public decimal Troco => FormaPagamento == "DINHEIRO" ? Math.Max(0, ValorRecebido - _total) : 0;

    public PagamentoForm(decimal total)
    {
        _total = total;
        Text = "Pagamento";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(480, 510);
        MinimumSize = new Size(440, 480);
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        Font = new Font("Segoe UI", 9F);
        UiWindowChrome.Apply(this);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(22), BackColor = UiTheme.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var totalBlock = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        totalBlock.Controls.Add(new Label { Text = "PAGAMENTO", AutoSize = true, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8, FontStyle.Bold) });
        totalBlock.Controls.Add(new Label { Text = total.ToString("C2"), AutoSize = true, ForeColor = UiTheme.Text, Font = new Font("Consolas", 21, FontStyle.Bold) });
        root.Controls.Add(totalBlock, 0, 0);
        root.Controls.Add(new Label { Text = "FORMA DE PAGAMENTO", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8, FontStyle.Bold) }, 0, 1);
        var methodGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        methodGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); methodGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        methodGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); methodGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        string[] methods = ["DINHEIRO", "PIX", "DÉBITO", "CRÉDITO"];
        for (int i = 0; i < methods.Length; i++)
        {
            string method = methods[i];
            var button = new Button { Text = $"{method} · F{i+6}", Dock = DockStyle.Fill, Margin = new Padding(3), FlatStyle = FlatStyle.Flat, BackColor = UiTheme.Surface, ForeColor = UiTheme.Text };
            button.FlatAppearance.BorderColor = UiTheme.Outline;
            button.Click += (_, _) => SelectMethod(method);
            _methods.Add(method, button);
            methodGrid.Controls.Add(button, i % 2, i / 2);
        }
        root.Controls.Add(methodGrid, 0, 2);
        var cashBlock = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        cashBlock.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); cashBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cashBlock.Controls.Add(new Label { Text = "VALOR RECEBIDO", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8, FontStyle.Bold) }, 0, 0);
        cashBlock.Controls.Add(_received, 0, 1);
        _cashEntryBlock=cashBlock;
        root.Controls.Add(cashBlock, 0, 3);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.Controls.Add(_changeCaption, 0, 0);
        _change.Text = 0m.ToString("C2"); _change.Dock = DockStyle.Fill; _change.TextAlign = ContentAlignment.MiddleRight; _change.ForeColor = UiTheme.Accent; _change.Font = new Font("Consolas", 10, FontStyle.Bold);
        footer.Controls.Add(_change, 1, 0);
        _confirm.Dock = DockStyle.Fill;
        _confirm.FlatStyle = FlatStyle.Flat; _confirm.FlatAppearance.BorderSize = 0; _confirm.BackColor = UiTheme.Accent; _confirm.ForeColor = Color.FromArgb(4, 37, 27); _confirm.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        _cancel.Margin = new Padding(0, 4, 5, 0);
        _confirm.Margin = new Padding(5, 4, 0, 0);
        buttons.Controls.Add(_cancel, 0, 0); buttons.Controls.Add(_confirm, 1, 0);
        footer.Controls.Add(_validation, 0, 1); footer.SetColumnSpan(_validation, 2);
        footer.Controls.Add(buttons, 0, 2); footer.SetColumnSpan(buttons, 2);
        root.Controls.Add(footer, 0, 4);
        Controls.Add(root);

        _received.TextChanged += (_, _) => UpdateChange();
        _received.Enter += (_, _) => SelectReceived();
        _received.MouseDown += (_, _) => SelectReceived();
        _received.KeyPress += (_, e) =>
        {
            if (char.IsControl(e.KeyChar)) return;
            if (char.IsDigit(e.KeyChar))
            {
                int decimalPosition = _received.Text.IndexOfAny([',', '.']);
                if (decimalPosition >= 0 && _received.SelectionLength == 0 && _received.SelectionStart > decimalPosition && _received.Text.Length - decimalPosition > 2) e.Handled = true;
                return;
            }
            if (e.KeyChar is '.' or ',')
            {
                e.KeyChar = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0];
                if (_received.Text.Contains(',') || _received.Text.Contains('.')) e.Handled = true;
                return;
            }
            e.Handled = true;
        };
        _confirm.Click += (_, _) => { if (!IsPaymentValid()) return; DialogResult = DialogResult.OK; Close(); };
        Shown += (_, _) => _methods["DINHEIRO"].Focus();
        CancelButton = _cancel;
        AcceptButton = _confirm;
        KeyPreview=true;AutoScaleMode=AutoScaleMode.Dpi;UiTheme.FitToWorkingArea(this);
        KeyDown+=(_,e)=>{string? method=e.KeyCode switch{Keys.F6=>"DINHEIRO",Keys.F7=>"PIX",Keys.F8=>"DÉBITO",Keys.F9=>"CRÉDITO",_=>null};if(method is not null){e.SuppressKeyPress=true;SelectMethod(method);if(method!="DINHEIRO")_confirm.Focus();}};
        SelectMethod("");
        UpdateChange();
    }

    private void SelectMethod(string method)
    {
        FormaPagamento = method;
        foreach (var entry in _methods)
        {
            bool selected = entry.Key == method;
            entry.Value.BackColor = selected ? Color.FromArgb(18, 55, 47) : UiTheme.Surface;
            entry.Value.ForeColor = selected ? UiTheme.Accent : UiTheme.Text;
            entry.Value.FlatAppearance.BorderColor = selected ? UiTheme.Accent : UiTheme.Outline;
        }
        bool cash = method == "DINHEIRO";
        _received.Enabled = cash;
        if(_cashEntryBlock is not null)_cashEntryBlock.Visible=cash;
        if (rootFromControl(_cashEntryBlock) is TableLayoutPanel root && root.RowStyles.Count > 3) root.RowStyles[3].Height = cash ? 104 : 0;
        _changeCaption.Text = cash ? "Troco" : method.Length == 0 ? "Selecione a forma de pagamento" : "TOTAL";
        if (cash && string.IsNullOrWhiteSpace(_received.Text)) _received.Text = _total.ToString("0.00", CultureInfo.CurrentCulture);
        _confirm.Text = method.Length == 0 ? "SELECIONE O PAGAMENTO" : $"REGISTRAR EM {method}";
        UpdateChange();
        if (cash) { _received.Focus(); _received.SelectAll(); }
    }

    private void UpdateChange()
    {
        bool parsed=EntradaNumerica.TryParseDinheiro(_received.Text, out var amount);
        decimal received=parsed?amount:0;
        decimal change = FormaPagamento == "DINHEIRO" && received >= _total ? PagamentoService.CalcularTroco(_total, received) : 0;
        _change.Text = FormaPagamento == "DINHEIRO" ? change.ToString("C2") : _total.ToString("C2");
        bool valid = FormaPagamento.Length > 0 && (FormaPagamento != "DINHEIRO" || parsed && received >= _total);
        _confirm.Enabled = valid;
        _validation.Text = FormaPagamento == "DINHEIRO" && !valid && !string.IsNullOrWhiteSpace(_received.Text) ? (!parsed?"Informe um valor válido, como 14,27 ou 14.27.":"O valor recebido precisa cobrir o total da venda.") : "";
    }

    private bool IsPaymentValid()
    {
        if (FormaPagamento.Length == 0) { _validation.Text = "Selecione a forma de pagamento desta venda."; return false; }
        if (FormaPagamento != "DINHEIRO") return true;
        if (!EntradaNumerica.TryParseDinheiro(_received.Text,out var amount)){_validation.Text="Informe um valor válido, como 14,27 ou 14.27.";_received.Focus();return false;}
        if(amount>=_total)return true;
        _validation.Text = "O valor recebido precisa cobrir o total da venda.";
        return false;
    }

    private void SelectReceived()
    {
        if (_received.IsDisposed || !_received.IsHandleCreated) return;
        _received.BeginInvoke(new Action(() => { if (!_received.IsDisposed) _received.SelectAll(); }));
    }

    private static Control? rootFromControl(Control? control) => control?.Parent;

}
