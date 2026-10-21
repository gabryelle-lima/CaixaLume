using System.Globalization;
using MercadinhoPdv.Models;
using MercadinhoPdv.Services;

namespace MercadinhoPdv.Forms;

internal sealed class CaixaForm : Form
{
    private readonly CaixaService _service = new();
    private readonly Label _status = new() { AutoSize = true, Font = new Font("Segoe UI", 17, FontStyle.Bold) };
    private readonly Label _opening = new() { AutoSize = true };
    private readonly Label _openingBalance = new() { AutoSize = true };
    private readonly Label _sales = new() { AutoSize = true, Font = new Font("Segoe UI", 13, FontStyle.Bold) };
    private readonly Label _cashExpected = new() { AutoSize = true, Font = new Font("Segoe UI", 13, FontStyle.Bold) };
    private readonly Label _supplyTotal = new() { AutoSize = true };
    private readonly Label _withdrawalTotal = new() { AutoSize = true };
    private readonly Label _expenseTotal = new() { AutoSize = true };
    private readonly DataGridView _grid = new();
    private readonly Button _open = new() { Text = "Abrir caixa", Width = 140, Height = 42 };
    private readonly Button _supply = new() { Text = "Suprimento", Width = 130, Height = 42 };
    private readonly Button _withdrawal = new() { Text = "Sangria", Width = 110, Height = 42 };
    private readonly Button _expense = new() { Text = "Registrar despesa", Width = 175, Height = 42 };
    private readonly Button _close = new() { Text = "Fechar caixa", Width = 145, Height = 42 };
    private readonly Button _history = new() { Text = "Histórico de caixas", Width = 165, Height = 42 };
    private ResumoCaixa? _current;

    public CaixaForm()
    {
        Text = "Caixa";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(850, 600);
        Size = new Size(980, 680);
        BuildLayout();
        UiTheme.Apply(this);
        RefreshStatus();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        RefreshStatus();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        header.Controls.Add(new Label { Text = "STATUS DO CAIXA", Font = new Font("Segoe UI", 11), AutoSize = true });
        header.Controls.Add(_status);
        var summary = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true, Padding = new Padding(4) };
        summary.Controls.Add(SummaryMetric("ABERTO EM", _opening, 300));
        summary.Controls.Add(SummaryMetric("SALDO INICIAL", _openingBalance));
        summary.Controls.Add(SummaryMetric("VENDAS", _sales));
        summary.Controls.Add(SummaryMetric("DINHEIRO ESPERADO", _cashExpected));
        summary.Controls.Add(SummaryMetric("SUPRIMENTOS", _supplyTotal));
        summary.Controls.Add(SummaryMetric("SANGRIAS", _withdrawalTotal));
        summary.Controls.Add(SummaryMetric("DESPESAS EM DINHEIRO", _expenseTotal));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 8, 0, 0) };
        actions.Controls.AddRange([_open, _supply, _withdrawal, _expense, _close, _history]);
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MovimentoCaixaLinha.Data), HeaderText = "Data", DefaultCellStyle = new DataGridViewCellStyle { Format = "dd/MM/yyyy HH:mm" } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MovimentoCaixaLinha.Tipo), HeaderText = "Movimentação" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MovimentoCaixaLinha.Valor), HeaderText = "Valor", DefaultCellStyle = new DataGridViewCellStyle { Format = "C2" } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MovimentoCaixaLinha.Observacao), HeaderText = "Descrição" });
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(summary, 0, 1);
        root.Controls.Add(actions, 0, 2);
        root.Controls.Add(_grid, 0, 3);
        Controls.Add(root);
        _open.Click += (_, _) => OpenCash();
        _supply.Click += (_, _) => RegisterCashMovement("SUPRIMENTO");
        _withdrawal.Click += (_, _) => RegisterCashMovement("SANGRIA");
        _expense.Click += (_, _) => RegisterExpense();
        _close.Click += (_, _) => CloseCash();
        _history.Click += (_, _) => ShowCashHistory();
    }

    private void RefreshStatus()
    {
        _current = _service.ObterAberto();
        bool isOpen = _current is not null;
        _status.Text = isOpen ? "🟢 CAIXA ABERTO" : "🔴 CAIXA FECHADO";
        _status.ForeColor = isOpen ? UiTheme.Success : UiTheme.Error;
        _opening.Text = isOpen ? $"{_current!.DataAbertura:dd/MM/yyyy HH:mm}" : "Sem caixa aberto";
        _openingBalance.Text = isOpen ? $"{_current!.SaldoInicial:C2}" : "";
        _sales.Text = isOpen ? $"{_current!.QuantidadeVendas} venda(s) · {_current.TotalVendas:C2}" : "";
        _cashExpected.Text = isOpen ? _current!.ValorEsperadoDinheiro.ToString("C2") : "";
        _supplyTotal.Text = isOpen ? _current!.Suprimentos.ToString("C2") : "";
        _withdrawalTotal.Text = isOpen ? _current!.Sangrias.ToString("C2") : "";
        _expenseTotal.Text = isOpen ? _current!.DespesasDinheiro.ToString("C2") : "";
        _open.Visible = !isOpen;
        _supply.Visible = _withdrawal.Visible = _expense.Visible = _close.Visible = isOpen;
        _grid.DataSource = isOpen ? _service.ListarMovimentacoes(_current!.Id) : new List<MovimentoCaixaLinha>();
    }

    private static Control SummaryMetric(string title, Control value, int width = 210)
    {
        var panel = new TableLayoutPanel { Width = width, Height = 52, ColumnCount = 1, RowCount = 2, Margin = new Padding(4), Padding = new Padding(8, 2, 8, 2), BackColor = UiTheme.Surface };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 42)); panel.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        panel.Controls.Add(new Label { Text = title, AutoSize = true, Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 7.5F, FontStyle.Bold) }, 0, 0);
        value.Dock = DockStyle.Fill;
        value.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        panel.Controls.Add(value, 0, 1);
        return panel;
    }

    private void ShowCashHistory()
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
        UiTheme.StyleGrid(grid);
        var period = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        period.Items.AddRange(["Hoje", "Últimos 7 dias", "Últimos 30 dias", "Personalizado", "Todo o histórico"]);
        period.SelectedIndex = 2;
        var from = new DateTimePicker { Width = 130, Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddDays(-29) };
        var to = new DateTimePicker { Width = 130, Format = DateTimePickerFormat.Short, Value = DateTime.Today };
        var dates = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoScroll = true };
        dates.Controls.AddRange([new Label { Text = "De", AutoSize = true, Padding = new Padding(0, 7, 0, 0) }, from, new Label { Text = "Até", AutoSize = true, Padding = new Padding(6, 7, 0, 0) }, to]);
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 4, 0, 8) };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.Controls.Add(period, 0, 0); toolbar.Controls.Add(dates, 1, 0);
        using var dialog = new Form { Text = "Histórico de caixas", StartPosition = FormStartPosition.CenterParent, MinimumSize = new Size(820, 480), Size = new Size(1120, 650), BackColor = UiTheme.Background };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(toolbar, 0, 0); layout.Controls.Add(grid, 0, 1); dialog.Controls.Add(layout);
        UiWindowChrome.Apply(dialog);
        void RefreshHistory()
        {
            DateTime? start = period.SelectedIndex switch { 0 => DateTime.Today, 1 => DateTime.Today.AddDays(-6), 2 => DateTime.Today.AddDays(-29), 3 => from.Value.Date, _ => null };
            DateTime? end = period.SelectedIndex == 3 ? to.Value.Date : period.SelectedIndex == 4 ? null : DateTime.Today;
            dates.Visible = period.SelectedIndex == 3;
            if (start.HasValue && end.HasValue && start.Value.Date > end.Value.Date) { UiDialog.Warning(dialog, "Período inválido", "A data inicial deve ser anterior ou igual à data final."); return; }
            grid.DataSource = _service.ListarHistorico(start, end);
        }
        period.SelectedIndexChanged += (_, _) => RefreshHistory();
        from.ValueChanged += (_, _) => { if (period.SelectedIndex == 3) RefreshHistory(); };
        to.ValueChanged += (_, _) => { if (period.SelectedIndex == 3) RefreshHistory(); };
        RefreshHistory();
        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (column.DataPropertyName is nameof(HistoricoCaixaLinha.Abertura) or nameof(HistoricoCaixaLinha.Fechamento)) column.DefaultCellStyle.Format = "dd/MM/yyyy HH:mm";
            else if (column.DataPropertyName is nameof(HistoricoCaixaLinha.SaldoInicial) or nameof(HistoricoCaixaLinha.Vendas) or nameof(HistoricoCaixaLinha.Suprimentos) or nameof(HistoricoCaixaLinha.Sangrias) or nameof(HistoricoCaixaLinha.Despesas) or nameof(HistoricoCaixaLinha.ValorEsperado) or nameof(HistoricoCaixaLinha.ValorContado) or nameof(HistoricoCaixaLinha.Diferenca)) column.DefaultCellStyle.Format = "C2";
        }
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || grid.Rows[e.RowIndex].DataBoundItem is not HistoricoCaixaLinha row) return;
            ShowCashDetails(row);
        };
        dialog.ShowDialog(this);
    }

    private void ShowCashDetails(HistoricoCaixaLinha cash)
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = false };
        UiTheme.StyleGrid(grid);
        grid.DataSource = _service.ListarMovimentacoes(cash.Id);
        foreach (DataGridViewColumn column in grid.Columns)
            if (column.DataPropertyName == nameof(MovimentoCaixaLinha.Data)) column.DefaultCellStyle.Format = "dd/MM/yyyy HH:mm";
            else if (column.DataPropertyName == nameof(MovimentoCaixaLinha.Valor)) column.DefaultCellStyle.Format = "C2";
        using var details = new Form { Text = $"Caixa #{cash.Id} · {cash.Status}", StartPosition = FormStartPosition.CenterParent, MinimumSize = new Size(760, 440), Size = new Size(920, 560), BackColor = UiTheme.Background };
        details.Controls.Add(grid);
        UiWindowChrome.Apply(details);
        details.ShowDialog(this);
    }

    private void OpenCash()
    {
        using var dialog = new CadastroDialog("Abertura de caixa", "Saldo inicial");
        dialog.ValidateAndSave = values =>
        {
            if (!TryParseAmount(values["Saldo inicial"], out decimal amount)) return "Informe um saldo válido em reais. Exemplo: 50,00.";
            _service.Abrir(amount); return null;
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) RefreshStatus();
    }

    private void RegisterCashMovement(string type)
    {
        using var dialog = new CadastroDialog(type == "SUPRIMENTO" ? "Registrar suprimento" : "Registrar sangria", "Valor", "Observação");
        dialog.ValidateAndSave = values =>
        {
            if (!TryParseAmount(values["Valor"], out decimal amount)) return "Informe um valor válido em reais. Exemplo: 14,27.";
            _service.RegistrarMovimentacao(type, amount, values["Observação"]); return null;
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) RefreshStatus();
    }

    private void RegisterExpense()
    {
        using var dialog = new DespesaDialog();
        dialog.ValidateAndSave = () => { _service.RegistrarDespesa(dialog.Descricao, dialog.Valor, dialog.FormaPagamento); return null; };
        if (dialog.ShowDialog(this) == DialogResult.OK) RefreshStatus();
    }

    private void CloseCash()
    {
        if (_current is null) return;
        using var dialog = new CadastroDialog("Conferência e fechamento", "Valor contado");
        dialog.SetValues(new Dictionary<string, string> { ["Valor contado"] = _current.ValorEsperadoDinheiro.ToString("N2", CultureInfo.CurrentCulture) });
        ResumoFechamento? close = null;
        dialog.ValidateAndSave = values =>
        {
            if (!TryParseAmount(values["Valor contado"], out decimal amount)) return "Informe o valor contado em reais. Exemplo: 50,00.";
            if (!UiDialog.Confirm(dialog, "Conferência do caixa", $"Saldo inicial  { _current.SaldoInicial:C2}\nVendas em dinheiro  +{_current.VendasDinheiro:C2}\nSuprimentos  +{_current.Suprimentos:C2}\nSangrias  −{_current.Sangrias:C2}\nDespesas em dinheiro  −{_current.DespesasDinheiro:C2}\n\nValor esperado  {_current.ValorEsperadoDinheiro:C2}\nValor contado  {amount:C2}\nDiferença  {amount - _current.ValorEsperadoDinheiro:C2}\n\nConfirma o fechamento?", true)) return "Revise o valor contado antes de confirmar.";
            close = _service.Fechar(amount); return null;
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || close is null) return;
        string status = close.Diferenca switch { > 0 => $"SOBRA: {close.Diferenca:C2}", < 0 => $"FALTA: {Math.Abs(close.Diferenca):C2}", _ => "CAIXA CONFERIDO" };
        UiDialog.Success(this, "Caixa fechado", status);
        RefreshStatus();
    }

    private static bool TryParseAmount(string input, out decimal amount) => PagamentoService.TryParseValor(input, out amount) && amount >= 0;

    private void ShowError(Exception exception, string title) => UiDialog.Warning(this, title, exception.Message);
}
