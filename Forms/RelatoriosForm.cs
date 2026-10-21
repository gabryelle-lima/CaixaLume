using MercadinhoPdv.Models;
using MercadinhoPdv.Services;

namespace MercadinhoPdv.Forms;

internal sealed class RelatoriosForm : Form
{
    private readonly RelatoriosService _service = new();
    private readonly ComboBox _period = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly DateTimePicker _start = new() { Format = DateTimePickerFormat.Short, Width = 135 };
    private readonly DateTimePicker _end = new() { Format = DateTimePickerFormat.Short, Width = 135 };
    private readonly Label _summary = new() { AutoSize = true, Font = new Font("Segoe UI", 13, FontStyle.Bold) };
    private readonly Label _expensesSummary = new() { AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
    private readonly DataGridView _sales = CreateGrid();
    private readonly DataGridView _payments = CreateGrid();
    private readonly DataGridView _products = CreateGrid();
    private readonly DataGridView _expenses = CreateGrid();
    private readonly DataGridView _leastProducts=CreateGrid();
    private readonly DataGridView _stock=CreateGrid();
    private readonly DataGridView _expiry=CreateGrid();
    private readonly DataGridView _movements=CreateGrid();
    private readonly DataGridView _cash=CreateGrid();
    private readonly FlowLayoutPanel _customDates = new() { AutoSize = true, WrapContents = false };

    public RelatoriosForm()
    {
        Text = "Relatórios";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(860, 540);
        Size = new Size(1160, 760);
        BuildLayout();
        UiTheme.Apply(this);
        ApplyPeriod();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true };
        _period.Items.AddRange(["Hoje", "Ontem", "Últimos 7 dias", "Últimos 30 dias", "Este mês", "Mês anterior", "Período personalizado"]);
        _period.SelectedIndex = 0;
        _customDates.Controls.AddRange([new Label { Text = "De", AutoSize = true, Margin = new Padding(4, 8, 2, 0) }, _start, new Label { Text = "Até", AutoSize = true, Margin = new Padding(4, 8, 2, 0) }, _end]);
        _customDates.Visible = false;
        var apply = new Button { Text = "Aplicar filtro", AutoSize = true, Height = 32 };
        filters.Controls.AddRange([new Label { Text = "Período", AutoSize = true, Margin = new Padding(0, 8, 4, 0) }, _period, _customDates, apply]);
        var summaries = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        summaries.SizeChanged += (_, _) => { _summary.MaximumSize = _expensesSummary.MaximumSize = new Size(Math.Max(200, summaries.ClientSize.Width - 28), 0); };
        summaries.Controls.Add(_summary);
        summaries.Controls.Add(_expensesSummary);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var salesPage = new TabPage("Vendas");
        var productsPage = new TabPage("Produtos mais vendidos");
        var paymentsPage = new TabPage("Formas de pagamento");
        var expensesPage = new TabPage("Despesas");
        var leastPage=new TabPage("Menos vendidos");var stockPage=new TabPage("Estoque baixo");var expiryPage=new TabPage("Validade");var movementsPage=new TabPage("Movimentações");var cashPage=new TabPage("Caixas");
        ConfigureColumns(_sales, (nameof(VendaRelatorioLinha.Id), "Venda", null), (nameof(VendaRelatorioLinha.Data), "Data", "dd/MM/yyyy HH:mm"), (nameof(VendaRelatorioLinha.FormaPagamento), "Pagamento", null), (nameof(VendaRelatorioLinha.Subtotal), "Subtotal", "C2"), (nameof(VendaRelatorioLinha.Desconto), "Desconto", "C2"), (nameof(VendaRelatorioLinha.Acrescimo), "Acréscimo", "C2"), (nameof(VendaRelatorioLinha.Total), "Total líquido", "C2"));
        ConfigureColumns(_products, (nameof(ProdutoMaisVendidoLinha.Produto), "Produto", null), (nameof(ProdutoMaisVendidoLinha.Quantidade), "Quantidade", "0.###"), (nameof(ProdutoMaisVendidoLinha.Unidade), "Unidade", null), (nameof(ProdutoMaisVendidoLinha.Total), "Total bruto", "C2"));
        ConfigureColumns(_payments, (nameof(PagamentoRelatorioLinha.FormaPagamento), "Forma de pagamento", null), (nameof(PagamentoRelatorioLinha.Quantidade), "Vendas", "N0"), (nameof(PagamentoRelatorioLinha.Total), "Total", "C2"));
        ConfigureColumns(_expenses, (nameof(DespesaRelatorioLinha.Data), "Data", "dd/MM/yyyy HH:mm"), (nameof(DespesaRelatorioLinha.Descricao), "Despesa", null), (nameof(DespesaRelatorioLinha.FormaPagamento), "Pagamento", null), (nameof(DespesaRelatorioLinha.Valor), "Valor", "C2"));
        ConfigureColumns(_leastProducts,(nameof(ProdutoMaisVendidoLinha.Produto),"Produto",null),(nameof(ProdutoMaisVendidoLinha.Quantidade),"Quantidade","0.###"),(nameof(ProdutoMaisVendidoLinha.Unidade),"Unidade",null),(nameof(ProdutoMaisVendidoLinha.Total),"Total bruto","C2"));
        ConfigureColumns(_stock,(nameof(ProdutoEstoqueBaixo.Produto),"Produto",null),(nameof(ProdutoEstoqueBaixo.Estoque),"Estoque","0.###"),(nameof(ProdutoEstoqueBaixo.Minimo),"Mínimo","0.###"),(nameof(ProdutoEstoqueBaixo.Unidade),"Unidade",null));
        ConfigureColumns(_expiry,(nameof(ProdutoValidadeProxima.Produto),"Produto",null),(nameof(ProdutoValidadeProxima.Validade),"Validade","dd/MM/yyyy"),(nameof(ProdutoValidadeProxima.Dias),"Dias","N0"));
        ConfigureColumns(_movements,(nameof(MovimentoEstoqueLinha.Data),"Data","dd/MM/yyyy HH:mm"),(nameof(MovimentoEstoqueLinha.Produto),"Produto",null),(nameof(MovimentoEstoqueLinha.CodigoBarras),"Código",null),(nameof(MovimentoEstoqueLinha.Tipo),"Tipo",null),(nameof(MovimentoEstoqueLinha.Quantidade),"Quantidade","0.###"),(nameof(MovimentoEstoqueLinha.UnidadeMedida),"Unidade",null),(nameof(MovimentoEstoqueLinha.Motivo),"Motivo",null),(nameof(MovimentoEstoqueLinha.Usuario),"Usuário",null));
        ConfigureColumns(_cash,(nameof(HistoricoCaixaLinha.Abertura),"Abertura","dd/MM/yyyy HH:mm"),(nameof(HistoricoCaixaLinha.Fechamento),"Fechamento","dd/MM/yyyy HH:mm"),(nameof(HistoricoCaixaLinha.SaldoInicial),"Saldo inicial","C2"),(nameof(HistoricoCaixaLinha.Vendas),"Vendas","C2"),(nameof(HistoricoCaixaLinha.Despesas),"Despesas","C2"),(nameof(HistoricoCaixaLinha.ValorEsperado),"Esperado","C2"),(nameof(HistoricoCaixaLinha.ValorContado),"Contado","C2"),(nameof(HistoricoCaixaLinha.Diferenca),"Diferença","C2"),(nameof(HistoricoCaixaLinha.Status),"Status",null));
        salesPage.Controls.Add(_sales);
        productsPage.Controls.Add(_products);
        paymentsPage.Controls.Add(_payments);
        expensesPage.Controls.Add(_expenses);
        leastPage.Controls.Add(_leastProducts);stockPage.Controls.Add(_stock);expiryPage.Controls.Add(_expiry);movementsPage.Controls.Add(_movements);cashPage.Controls.Add(_cash);
        tabs.TabPages.AddRange([salesPage, productsPage, leastPage, paymentsPage, expensesPage,stockPage,expiryPage,movementsPage,cashPage]);
        root.Controls.Add(filters, 0, 0);
        root.Controls.Add(summaries, 0, 1);
        root.Controls.Add(tabs, 0, 2);
        Controls.Add(root);
        _period.SelectedIndexChanged += (_, _) => { _customDates.Visible = _period.SelectedIndex == 6; if (! _customDates.Visible) ApplyPeriod(); };
        apply.Click += (_, _) => ApplyPeriod();
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AutoGenerateColumns = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        UiTheme.StyleGrid(grid);
        return grid;
    }

    private static void ConfigureColumns(DataGridView grid, params (string Property, string Title, string? Format)[] fields)
    {
        foreach (var field in fields) grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = field.Property, HeaderText = field.Title, DefaultCellStyle = new DataGridViewCellStyle { Format = field.Format ?? "" } });
    }

    private void ApplyPeriod()
    {
        DateTime start;
        DateTime end;
        DateTime today = DateTime.Today;
        switch (_period.SelectedIndex)
        {
            case 1: start = end = today.AddDays(-1); break;
            case 2: start = today.AddDays(-6); end = today; break;
            case 3: start = today.AddDays(-29); end = today; break;
            case 4: start = new DateTime(today.Year, today.Month, 1); end = today; break;
            case 5:
                start = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
                end = new DateTime(today.Year, today.Month, 1).AddDays(-1);
                break;
            case 6: start = _start.Value.Date; end = _end.Value.Date; break;
            default: start = end = today; break;
        }
        if (end < start) { UiDialog.Warning(this, "Período inválido", "A data final deve ser igual ou posterior à data inicial."); return; }
        try
        {
            ResumoRelatorio report = _service.Gerar(start, end);
            _sales.DataSource = report.Vendas;
            _products.DataSource = report.ProdutosMaisVendidos;
            _payments.DataSource = report.Pagamentos;
            _expenses.DataSource = report.Despesas;
            _leastProducts.DataSource=report.ProdutosMenosVendidos;_stock.DataSource=report.EstoqueBaixo;_expiry.DataSource=report.Validades;_movements.DataSource=report.MovimentacoesEstoque;_cash.DataSource=report.Caixas;
            _summary.Text = $"{start:dd/MM/yyyy} a {end:dd/MM/yyyy}  ·  {report.QuantidadeVendas} venda(s)  ·  Faturamento {report.TotalVendido:C2}  ·  Ticket médio {report.TicketMedio:C2}";
            _expensesSummary.Text = $"Despesas: {report.TotalDespesas:C2}  ·  Custo estimado: {report.CustoEstimado:C2}  ·  Lucro bruto estimado: {report.LucroBrutoEstimado:C2}  ·  Margem: {report.MargemMediaPercentual:N1}%";
            _expensesSummary.Text += $"\nSubtotal: {report.SubtotalVendas:C2} · Descontos: {report.TotalDescontos:C2} · Acréscimos: {report.TotalAcrescimos:C2}. Totais por produto são valores brutos dos itens.";
        }
        catch (Exception exception) { UiDialog.Warning(this, "Não foi possível gerar o relatório", exception.Message); }
    }
}
