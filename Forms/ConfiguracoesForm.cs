using CaixaLume.Data;
using CaixaLume.Diagnostics;

namespace CaixaLume.Forms;

internal sealed class ConfiguracoesForm : Form
{
    private readonly TextBox _storeName = new() { Dock = DockStyle.Fill, CharacterCasing = CharacterCasing.Upper };
    private readonly Services.BackupService _backup = new();

    public ConfiguracoesForm()
    {
        Services.Autorizacao.ExigirAdministrador();
        Text = "Configurações";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(580, 400);
        Size = new Size(720, 500);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 2, RowCount = 6, AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 44, 80, 46, 150, 56 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.AutoScrollMinSize = new Size(480, 440);
        layout.Controls.Add(new Label { Text = "Nome da loja", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_storeName, 1, 0);
        layout.Controls.Add(new Label { Text = "Banco de dados", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(new Label { Text = DatabaseInitializer.DatabasePath, AutoSize = true, MaximumSize = new Size(340, 0) }, 1, 1);
        var save = new Button { Text = "Salvar configurações", AutoSize = true };
        layout.Controls.Add(save, 1, 2);
        var backup = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
        var create = new Button { Text = "Fazer backup", AutoSize = true, Height = 36 };
        var restore = new Button { Text = "Restaurar backup", AutoSize = true, Height = 36 };
        var audit = new Button { Text = "Auditoria", AutoSize = true, Height = 36 };
        var users = new Button { Text = "Usuários", AutoSize = true, Height = 36 };
        var policy = new Button { Text = "Regras de desconto", AutoSize = true, Height = 36 };
        policy.Click += (_, _) => { using var form = new PoliticaAjustesForm(); form.ShowDialog(this); };
        backup.Controls.AddRange([create, restore, users, audit, policy]); layout.Controls.Add(new Label { Text = "Segurança", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3); layout.Controls.Add(backup, 1, 3);
        var testTools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
        var clearCashHistory = new Button { Text = "Limpar histórico do caixa (testes)", AutoSize = true, Height = 36 };
        testTools.Controls.Add(clearCashHistory);
        if (Services.AmbienteAplicacao.FerramentasTesteDisponiveis)
        {
            layout.Controls.Add(new Label { Text = "Desenvolvimento", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
            layout.Controls.Add(testTools, 1, 4);
        }
        else testTools.Dispose();
        Controls.Add(layout);
        UiTheme.Apply(this);
        save.Click += (_, _) => Save();
        create.Click += (_, _) => CreateBackup(); restore.Click += (_, _) => RestoreBackup(); audit.Click += (_, _) => ShowAudit(); users.Click += (_,_)=>{using var form=new UsuariosForm();form.ShowDialog(this);};
        clearCashHistory.Click += (_, _) => ClearCashHistory();
        LoadValue();
    }

    private void CreateBackup()
    {
        using var dialog = new SaveFileDialog { Filter = "Banco SQLite (*.db)|*.db", FileName = $"caixalume-{DateTime.Now:yyyyMMdd-HHmm}.db" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { _backup.Criar(dialog.FileName); UiDialog.Success(this, "Backup concluído", "O arquivo de backup foi criado e validado."); }
        catch (Exception exception) { AppLogger.Error(exception, "Criar backup"); UiDialog.Warning(this, "Backup", exception.Message); }
    }

    private void RestoreBackup()
    {
        using var dialog = new OpenFileDialog { Filter = "Banco SQLite (*.db;*.bak)|*.db;*.bak|Todos os arquivos (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!UiDialog.Confirm(this, "Restaurar backup", "O banco atual terá uma cópia de segurança e será substituído pelo backup selecionado. Continuar?", true)) return;
        try { _backup.Restaurar(dialog.FileName); UiDialog.Success(this, "Backup restaurado", "Feche e abra o aplicativo para carregar os dados restaurados."); }
        catch (Exception exception) { AppLogger.Error(exception, "Restaurar backup"); UiDialog.Warning(this, "Restauração", exception.Message); }
    }

    private void ShowAudit()
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true, AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        UiTheme.StyleGrid(grid);
        grid.DataSource = db.Query("SELECT A.DataHora AS Data, COALESCE(U.Nome,'SISTEMA') AS Usuario, A.Acao, A.Entidade, A.Descricao FROM Auditoria A LEFT JOIN Usuarios U ON U.Id=A.UsuarioId ORDER BY A.Id DESC LIMIT 1000;");
        using var window = new Form { Text = "Auditoria de ações", StartPosition = FormStartPosition.CenterParent, Size = new Size(1000, 600), MinimumSize = new Size(760, 420) };
        window.Controls.Add(grid); UiTheme.Apply(window); window.ShowDialog(this);
    }

    private void ClearCashHistory()
    {
        if (!UiDialog.Confirm(this, "Limpar histórico de caixas", "Esta ação apagará caixas abertos e fechados e suas movimentações. Vendas e despesas serão preservadas, mas ficarão sem vínculo com um caixa. Deseja continuar?", true)) return;
        try
        {
            int count = new Services.CaixaService().LimparHistoricoParaTestes();
            UiDialog.Success(this, "Histórico limpo", $"{count} registro(s) de caixa removido(s). Agora você pode iniciar um novo teste.");
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Limpar histórico do caixa para testes");
            UiDialog.Warning(this, "Não foi possível limpar", exception.Message);
        }
    }

    private void LoadValue()
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var value = db.Query("SELECT Valor FROM Configuracoes WHERE Chave = 'NomeLoja';").FirstOrDefault();
        _storeName.Text = value is null ? "Mercado" : Convert.ToString(value["Valor"]) ?? "Mercado";
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_storeName.Text)) { UiDialog.Warning(this, "Nome obrigatório", "Informe o nome do mercado."); return; }
        try
        {
            string name = _storeName.Text.Trim();
            new Services.ConfiguracaoService().SalvarNomeLoja(name);
            _storeName.Text = name;
            UiDialog.Success(this, "Configurações salvas", "O nome do mercado foi atualizado.");
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Salvar configurações do estabelecimento");
            UiDialog.Warning(this, "Configurações", $"Não foi possível salvar. {exception.Message}\n\nDetalhes: {AppLogger.LogPath}");
        }
    }
}
