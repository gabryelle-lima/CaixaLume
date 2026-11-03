using CaixaLume.Models;
using CaixaLume.Services;

namespace CaixaLume.Forms;

internal sealed class MainForm : Form
{
    private static readonly Color BackgroundColor = UiTheme.Background;
    private static readonly Color CardColor = UiTheme.Surface;
    private readonly DashboardService _dashboard = new();
    private readonly Label _title = new() { AutoSize = true, Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = UiTheme.Text };
    private readonly Label _subtitle = new() { AutoSize = true, ForeColor = UiTheme.Muted, Text = "Veja o resumo do seu negócio." };
    private readonly Label _brand = new() { Text = "MERCADO\nPDV", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 15, FontStyle.Bold), ForeColor = UiTheme.Accent, AutoEllipsis = true };
    private readonly Label _cashStatus = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = false, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
    private readonly ToolTip _userTooltip = new() { AutoPopDelay = 10000, InitialDelay = 350, ReshowDelay = 100 };
    private readonly FlowLayoutPanel _navigation = new() { Dock = DockStyle.Fill, FlowDirection=FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8, 6, 8, 0), BackColor=UiTheme.Surface };
    private readonly List<Button> _navigationButtons = [];
    private Button? _selectedNavigation;
    private string _displayStoreName = "Mercado";
    private readonly TableLayoutPanel _metrics = new() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
    private readonly DataGridView _recent = CreateGrid();
    private readonly DataGridView _lowStock = CreateGrid();
    private readonly DataGridView _expiring = CreateGrid();
    private readonly Panel _recentEmpty = EmptyPanel("Nenhuma venda hoje.", "Comece pelo PDV.", true);
    private readonly Panel _lowStockEmpty = EmptyPanel("Tudo em dia.", "Nenhum produto precisa de reposição.");
    private readonly Panel _expiringEmpty = EmptyPanel("Nenhum produto próximo da validade.", "Acompanhe os próximos 30 dias.");

    public MainForm(bool modoAlternativo = false)
    {
        Text = "CaixaLume";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);
        Size = new Size(1280, 820);
        BackColor = BackgroundColor;
        Font = new Font("Segoe UI", 10);
        BuildLayout();
        if (modoAlternativo) _subtitle.Text = "Modo alternativo ativo · os dados e as operações permanecem disponíveis.";
        UiTheme.Apply(this);
        StyleNavigation();
        Resize += (_, _) => UpdateBrandTypography();
        RefreshDashboard();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 3, BackColor = BackgroundColor };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        var greeting = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 2, 0, 0) };
        greeting.Controls.Add(_title);
        greeting.Controls.Add(_subtitle);
        header.Controls.Add(greeting, 0, 0);
        header.Controls.Add(_cashStatus, 1, 0);

        AddNavigation("Início", "Resumo do negócio");
        AddNavigation("PDV", "Abrir frente de caixa");
        AddNavigation("Produtos", "Cadastro de produtos");
        AddNavigation("Estoque", "Movimentações e saldos");
        AddNavigation("Caixa", "Abertura e fechamento");
        AddNavigation("Vendas", "Histórico e cancelamentos");
        AddNavigation("Despesas", "Consulta de despesas");
        AddNavigation("Relatórios", "Vendas e despesas");
        AddNavigation("Fornecedores", "Fornecedores e categorias");
        AddNavigation("Configurações", "Dados do estabelecimento");
        AddNavigation("Usuários", "Usuários e permissões");

        for (int i = 0; i < 4; i++) _metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _metrics.Controls.Add(CreateMetric("FATURAMENTO HOJE", Color.FromArgb(20, 120, 90)), 0, 0);
        _metrics.Controls.Add(CreateMetric("VENDAS HOJE", Color.FromArgb(36, 104, 170)), 1, 0);
        _metrics.Controls.Add(CreateMetric("TICKET MÉDIO", Color.FromArgb(130, 80, 170)), 2, 0);
        _metrics.Controls.Add(CreateMetric("ESTOQUE BAIXO", Color.FromArgb(190, 112, 30)), 3, 0);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 12, 0, 0) };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        content.Controls.Add(CreateSection("VENDAS RECENTES", _recent, _recentEmpty), 0, 0);
        content.Controls.Add(CreateSection("ALERTAS DE ESTOQUE", _lowStock, _lowStockEmpty), 1, 0);
        content.Controls.Add(CreateSection("VALIDADE · VENCIDOS E PRÓXIMOS 30 DIAS", _expiring, _expiringEmpty), 0, 1);
        content.SetColumnSpan(content.GetControlFromPosition(0, 1)!, 2);
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_metrics, 0, 1);
        root.Controls.Add(content, 0, 2);
        var workspace=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,BackColor=BackgroundColor};workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,205));workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var sidebar=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,BackColor=UiTheme.Surface};sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,82));sidebar.RowStyles.Add(new RowStyle(SizeType.Percent,100));sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,132));
        sidebar.Controls.Add(_brand,0,0);sidebar.Controls.Add(_navigation,0,1);
        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12,6,12,10),ColumnCount=1,RowCount=3};footer.RowStyles.Add(new RowStyle(SizeType.Absolute,36));footer.RowStyles.Add(new RowStyle(SizeType.Absolute,23));footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        string fullName = Models.SessaoUsuario.Nome.Trim();
        var userName = new Label { Text = "●  " + fullName, Dock = DockStyle.Fill, ForeColor = UiTheme.Text, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        _userTooltip.SetToolTip(userName, fullName);
        footer.Controls.Add(userName,0,0);
        footer.Controls.Add(new Label{Text="    "+Models.SessaoUsuario.Perfil,Dock=DockStyle.Fill,ForeColor=UiTheme.Muted,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true},0,1);
        var logout=new Button{Text="SAIR",Dock=DockStyle.Fill,Margin=new Padding(0,4,0,0)};logout.Click+=(_,_)=>{try{new AuditoriaService().Registrar("LOGOUT","SESSAO",$"Usuário {Models.SessaoUsuario.Nome} encerrou a sessão");}catch(Exception ex){Diagnostics.AppLogger.Error(ex,"Registrar logout");}Models.SessaoUsuario.Encerrar();Close();};footer.Controls.Add(logout,0,2);sidebar.Controls.Add(footer,0,2);
        workspace.Controls.Add(sidebar,0,0);workspace.Controls.Add(root,1,0);Controls.Add(workspace);
        _selectedNavigation = _navigationButtons.FirstOrDefault(button => button.AccessibleName == "Início");
    }

    private void AddNavigation(string name, string description, bool enabled = true)
    {
        var button = new Button { Text = name, Width=174, Height = 40, Margin = new Padding(0, 2, 0, 4), AccessibleDescription = description, TextAlign=ContentAlignment.MiddleLeft, Padding=new Padding(12,0,0,0) };
        button.Enabled = enabled && (Models.SessaoUsuario.Administrador || name is not ("Despesas" or "Relatórios" or "Fornecedores" or "Configurações" or "Usuários"));
        if (!button.Enabled) button.AccessibleDescription = "Disponível para administradores";
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.AccessibleName = name;
        button.Click += (_, _) => { _selectedNavigation = button; StyleNavigation(); OpenModule(name); };
        _navigationButtons.Add(button);
        _navigation.Controls.Add(button);
    }

    private static Panel CreateMetric(string title, Color accent)
    {
        var panel = new UiCard { Dock = DockStyle.Fill, BackColor = CardColor, Margin = new Padding(0, 0, 12, 0), Padding = new Padding(14), ShowBorder = false };
        var caption = new Label { Text = title, ForeColor = Color.FromArgb(103, 115, 128), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), Dock = DockStyle.Top };
        var value = new Label { Name = "MetricValue", Text = "—", ForeColor = accent, AutoSize = true, Font = new Font("Segoe UI", 20, FontStyle.Bold), Dock = DockStyle.Bottom };
        panel.Controls.Add(value);
        panel.Controls.Add(caption);
        return panel;
    }

    private Control CreateSection(string title, DataGridView grid, Panel emptyState)
    {
        var section = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(0, 0, 4, 8) };
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        section.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 9, FontStyle.Bold) }, 0, 0);
        var card = new UiCard { Dock = DockStyle.Fill, BackColor = CardColor, Padding = new Padding(12), ShowBorder = false };
        card.Controls.Add(grid);
        card.Controls.Add(emptyState);
        emptyState.BringToFront();
        section.Controls.Add(card, 0, 1);
        return section;
    }

    private static Panel EmptyPanel(string title, string description, bool showButton = false)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = CardColor, Padding = new Padding(14) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = showButton ? 3 : 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        if (showButton) layout.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomCenter, ForeColor = UiTheme.Text, Font = new Font("Segoe UI", 11, FontStyle.Bold) }, 0, 0);
        layout.Controls.Add(new Label { Text = description, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopCenter, ForeColor = UiTheme.Muted }, 0, 1);
        if (showButton)
        {
            var button = new Button { Text = "ABRIR PDV", Anchor = AnchorStyles.Top, Width = 130, Height = 36 };
            button.Click += (_, _) => (panel.FindForm() as MainForm)?.OpenModule("PDV");
            layout.Controls.Add(button, 0, 2);
        }
        panel.Controls.Add(layout);
        return panel;
    }

    private static DataGridView CreateGrid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AutoGenerateColumns = true,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = CardColor,
        BorderStyle = BorderStyle.None
    };

    private void RefreshDashboard()
    {
        try
        {
            ResumoDashboard summary = _dashboard.ObterResumo();
            string firstName = Models.SessaoUsuario.Nome.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            _title.Text = $"Bom dia, {firstName}";
            _displayStoreName = summary.NomeLoja.Trim();
            _brand.Text = $"{_displayStoreName.ToUpperInvariant()}\nPDV";
            _userTooltip.SetToolTip(_brand, summary.NomeLoja);
            UpdateBrandTypography();
            Text = $"{summary.NomeLoja} · PDV";
            _cashStatus.Text = summary.CaixaAberto ? "●  CAIXA ABERTO" : "●  CAIXA FECHADO";
            _cashStatus.ForeColor = summary.CaixaAberto ? UiTheme.Accent : Color.IndianRed;
            SetMetric(0, summary.FaturamentoHoje.ToString("C2"));
            SetMetric(1, summary.VendasHoje.ToString());
            SetMetric(2, summary.TicketMedio.ToString("C2"));
            SetMetric(3, summary.ItensEstoqueBaixo.ToString());
        _recent.DataSource = summary.VendasRecentes;
        _lowStock.DataSource = summary.ProdutosEstoqueBaixo;
        var validade = summary.ProdutosVencidos.Concat(summary.ProdutosValidadeProxima).ToList();
        _expiring.DataSource=validade;
        _recent.Visible = summary.VendasRecentes.Count > 0;
        _recentEmpty.Visible = summary.VendasRecentes.Count == 0;
        _lowStock.Visible = summary.ProdutosEstoqueBaixo.Count > 0;
        _lowStockEmpty.Visible = summary.ProdutosEstoqueBaixo.Count == 0;
        _expiring.Visible = validade.Count > 0;
        _expiringEmpty.Visible = validade.Count == 0;
        if(_expiring.Columns[nameof(ProdutoValidadeProxima.Validade)] is DataGridViewColumn expiry)expiry.DefaultCellStyle.Format="dd/MM/yyyy";
        if (_recent.Columns[nameof(VendaRecente.Data)] is DataGridViewColumn dateColumn) dateColumn.DefaultCellStyle.Format = "dd/MM/yyyy HH:mm";
        if (_recent.Columns[nameof(VendaRecente.Total)] is DataGridViewColumn totalColumn) totalColumn.DefaultCellStyle.Format = "C2";
        }
        catch (Exception exception)
        {
            Diagnostics.AppLogger.Error(exception, "Atualizar dashboard");
            UiDialog.Warning(this, "Dashboard indisponível", exception.Message);
        }
    }

    private void UpdateBrandTypography()
    {
        if (_brand.IsDisposed) return;
        float size = 15;
        int availableWidth = Math.Max(80, _brand.ClientSize.Width - 18);
        while (size > 8)
        {
            using var candidate = new Font("Segoe UI", size, FontStyle.Bold);
            if (TextRenderer.MeasureText(_displayStoreName.ToUpperInvariant(), candidate).Width <= availableWidth) break;
            size -= 0.5f;
        }
        _brand.Font = new Font("Segoe UI", size, FontStyle.Bold);
    }

    private void StyleNavigation()
    {
        foreach (Button button in _navigationButtons)
        {
            bool selected = ReferenceEquals(button, _selectedNavigation);
            button.BackColor = selected ? Color.FromArgb(18, 55, 47) : UiTheme.Surface;
            button.ForeColor = selected ? UiTheme.Accent : UiTheme.Text;
            button.Font = new Font("Segoe UI", 9, selected ? FontStyle.Bold : FontStyle.Regular);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(31, 42, 49);
        }
    }

    private void SetMetric(int index, string value)
    {
        var panel = _metrics.Controls[index];
        panel.Controls["MetricValue"]!.Text = value;
    }

    private void OpenModule(string name)
    {
        if (name == "Sair") { Models.SessaoUsuario.Encerrar(); Close(); return; }
        if (name == "Início") { RefreshDashboard(); return; }
        bool adminOnly = name is "Despesas" or "Relatórios" or "Fornecedores" or "Configurações" or "Usuários";
        if (adminOnly && !Models.SessaoUsuario.Administrador)
        {
            UiDialog.Info(this, "Acesso restrito", "Esta área está disponível apenas para administradores.");
            return;
        }
        Form? module = name switch
        {
            "PDV" => new PdvForm(),
            "Produtos" => new ProdutosForm(),
            "Estoque" => new EstoqueForm(),
            "Caixa" => new CaixaForm(),
            "Vendas" => new VendasForm(),
            "Despesas" => new DespesasForm(),
            "Relatórios" => new RelatoriosForm(),
            "Fornecedores" => new FornecedoresForm(),
            "Configurações" => new ConfiguracoesForm(),
            "Usuários" => new UsuariosForm(),
            _ => null
        };
        if (module is null) return;
        using (module) module.ShowDialog(this);
        RefreshDashboard();
    }
}
