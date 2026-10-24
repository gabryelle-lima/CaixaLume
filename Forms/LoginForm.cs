using MercadinhoPdv.Services;

namespace MercadinhoPdv.Forms;

internal sealed class LoginForm : Form
{
    private readonly UsuarioService _users = new();
    private readonly Panel _viewport = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly TextBox _name = new() { Dock = DockStyle.Fill, CharacterCasing = CharacterCasing.Upper };
    private readonly TextBox _login = new() { Dock = DockStyle.Fill };
    private readonly TextBox _password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _confirmPassword = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly Label _heading = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 22, FontStyle.Bold) };
    private readonly Label _context = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = UiTheme.Accent };
    private readonly Label _message = new() { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(248, 113, 113) };
    private readonly Button _submit = new() { Dock = DockStyle.Fill, Height = 44 };
    private readonly CheckBox _showPassword = new() { Text = "Mostrar senhas", AutoSize = true, Anchor = AnchorStyles.Left };
    private bool _setup;
    private readonly string _storeName;

    public LoginForm()
    {
        using (var db = new Data.DatabaseConnection(Data.DatabaseInitializer.DatabasePath))
            _storeName = Convert.ToString(db.Query("SELECT Valor FROM Configuracoes WHERE Chave='NomeLoja';").FirstOrDefault()?.GetValueOrDefault("Valor")) is { Length: > 0 } name ? name.Trim() : "Mercado";
        Text = $"CaixaLume · {_storeName}";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 610);
        MinimumSize = new Size(440, 560);
        AutoScaleMode = AutoScaleMode.Dpi;

        _setup = _users.PrecisaAdministradorInicial();
        Controls.Add(_viewport);
        _showPassword.CheckedChanged += (_, _) =>
        {
            _password.UseSystemPasswordChar = !_showPassword.Checked;
            _confirmPassword.UseSystemPasswordChar = !_showPassword.Checked;
        };
        _submit.Click += (_, _) => Submit();
        Shown += (_, _) => (_setup ? _name : _login).Focus();
        BuildLayout();
        UiTheme.Apply(this);
        _submit.BackColor = UiTheme.Accent;
        _submit.ForeColor = Color.FromArgb(4, 37, 27);
        _submit.FlatAppearance.BorderSize = 0;
        _submit.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
    }

    private void BuildLayout()
    {
        _viewport.Controls.Clear();
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(28, 24, 28, 24),
            MinimumSize = new Size(400, 0),
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 0,
            Padding = new Padding(22),
            BackColor = UiTheme.Surface,
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _heading.Text = "CaixaLume";
        _context.Text = _setup ? $"PRIMEIRO ACESSO · LOJA {_storeName.ToUpperInvariant()}" : $"ACESSO À LOJA {_storeName.ToUpperInvariant()}";
        _submit.Text = _setup ? "CADASTRAR ADMINISTRADOR" : "ENTRAR";
        _message.Text = _setup ? "Crie a conta que terá acesso de administrador." : "Entre com seu login e senha.";
        _message.ForeColor = UiTheme.Muted;

        AddRow(card, _heading, 64);
        AddRow(card, _context, 28);
        if (_setup) AddField(card, "Nome", _name);
        AddField(card, "Login", _login);
        AddField(card, "Senha", _password);
        if (_setup) AddField(card, "Confirmar senha", _confirmPassword);
        AddRow(card, _showPassword, 36);
        AddRow(card, _submit, 52);
        AddRow(card, _message, 76);
        outer.Controls.Add(card, 0, 0);
        _viewport.Controls.Add(outer);
        AcceptButton = _submit;
    }

    private static void AddField(TableLayoutPanel card, string caption, Control field)
    {
        var block = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 13),
        };
        block.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        block.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        block.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        block.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Muted }, 0, 0);
        field.Margin = Padding.Empty;
        block.Controls.Add(field, 0, 1);
        AddRow(card, block, 82);
    }

    private static void AddRow(TableLayoutPanel card, Control control, int minimumHeight)
    {
        int row = card.RowCount++;
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.MinimumSize = new Size(control.MinimumSize.Width, minimumHeight);
        control.Margin = new Padding(0, 0, 0, 8);
        card.Controls.Add(control, 0, row);
    }

    private void Submit()
    {
        _message.ForeColor = Color.FromArgb(248, 113, 113);
        try
        {
            if (_setup)
            {
                _users.CriarAdministradorInicial(_name.Text, _login.Text, _password.Text, _confirmPassword.Text);
                _setup = false;
                _name.Clear();
                _password.Clear();
                _confirmPassword.Clear();
                BuildLayout();
                _message.Text = "Administrador criado. Entre com seu login e senha.";
                _message.ForeColor = UiTheme.Accent;
                _login.Focus();
                return;
            }

            var authenticated = _users.Autenticar(_login.Text, _password.Text);
            if (authenticated is null)
            {
                _message.Text = "Login ou senha incorretos.";
                _password.Clear();
                _password.Focus();
                return;
            }
            MercadinhoPdv.Models.SessaoUsuario.Iniciar(authenticated.Value.Id, authenticated.Value.Nome, authenticated.Value.Perfil);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            Diagnostics.AppLogger.Error(exception, _setup ? "Configuração do administrador" : "Autenticação");
            _message.Text = exception.Message;
            _message.ForeColor = Color.FromArgb(248, 113, 113);
        }
    }
}
