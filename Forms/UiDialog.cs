namespace CaixaLume.Forms;

internal static class UiDialog
{
    public static void Info(IWin32Window owner, string title, string message) => Show(owner, title, message, DialogKind.Info);
    public static void Success(IWin32Window owner, string title, string message) => Show(owner, title, message, DialogKind.Success);
    public static void Warning(IWin32Window owner, string title, string message) => Show(owner, title, message, DialogKind.Warning);
    public static void Error(IWin32Window owner, string title, string message) => Show(owner, title, message, DialogKind.Error);

    public static bool Confirm(IWin32Window owner, string title, string message, bool destructive = false)
    {
        using var dialog = Create(title, message, destructive ? DialogKind.Warning : DialogKind.Info);
        var cancel = MakeButton("CANCELAR", false);
        var accept = MakeButton(destructive ? "CONFIRMAR" : "CONTINUAR", true);
        var actions = Actions(cancel, accept);
        ((TableLayoutPanel)dialog.Controls[0]).Controls.Add(actions, 0, 2);
        ((TableLayoutPanel)dialog.Controls[0]).SetColumnSpan(actions, 2);
        cancel.Click += (_, _) => { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); };
        accept.Click += (_, _) => { dialog.DialogResult = DialogResult.Yes; dialog.Close(); };
        dialog.CancelButton = cancel;
        dialog.AcceptButton = destructive ? cancel : accept;
        return dialog.ShowDialog(owner) == DialogResult.Yes;
    }

    private static void Show(IWin32Window owner, string title, string message, DialogKind kind)
    {
        using var dialog = Create(title, message, kind);
        var ok = MakeButton("ENTENDI", true);
        ((TableLayoutPanel)dialog.Controls[0]).Controls.Add(ok, 1, 2);
        ok.Click += (_, _) => { dialog.DialogResult = DialogResult.OK; dialog.Close(); };
        dialog.AcceptButton = ok;
        dialog.ShowDialog(owner);
    }

    private static Form Create(string title, string message, DialogKind kind)
    {
        using var measurementFont = new Font("Segoe UI", 9F);
        int messageHeight = TextRenderer.MeasureText(message, measurementFont, new Size(380, int.MaxValue), TextFormatFlags.WordBreak).Height;
        int availableHeight = Screen.PrimaryScreen?.WorkingArea.Height - 90 ?? 650;
        int dialogHeight = Math.Min(Math.Max(250, messageHeight + 150), Math.Max(250, availableHeight));
        var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ShowInTaskbar = false,
            MaximizeBox = false,
            MinimizeBox = false,
            AutoScaleMode = AutoScaleMode.Dpi,
            MinimumSize = new Size(390, 230),
            ClientSize = new Size(470, dialogHeight),
            BackColor = UiTheme.Background,
            ForeColor = UiTheme.Text,
            Font = new Font("Segoe UI", 9F),
        };
        Color accent = kind switch
        {
            DialogKind.Success => UiTheme.Accent,
            DialogKind.Warning => Color.FromArgb(245, 158, 11),
            DialogKind.Error => Color.FromArgb(248, 113, 113),
            _ => UiTheme.Accent,
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 3, BackColor = UiTheme.Surface };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var mark = new Panel { Dock = DockStyle.Fill, BackColor = accent, Margin = new Padding(0, 2, 18, 2) };
        layout.Controls.Add(mark, 0, 0);
        var heading = new Label { Text = title.ToUpperInvariant(), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = UiTheme.Text, AutoEllipsis = true };
        layout.Controls.Add(heading, 1, 0);
        var body = new Label { Text = message, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Muted, AutoSize = false, AutoEllipsis = false, MaximumSize = new Size(380, 0) };
        var scroll=new Panel { Dock=DockStyle.Fill,AutoScroll=true };
        body.Dock=DockStyle.None;body.AutoSize=true;body.MaximumSize=new Size(360,0);scroll.Controls.Add(body);
        scroll.SizeChanged+=(_,_)=>body.MaximumSize=new Size(Math.Max(100,scroll.ClientSize.Width-24),0);
        layout.Controls.Add(scroll, 1, 1);
        form.Controls.Add(layout);
        UiWindowChrome.Apply(form);
        UiTheme.FitToWorkingArea(form);
        return form;
    }

    private static FlowLayoutPanel Actions(Control cancel, Control accept)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        panel.Controls.AddRange([accept, cancel]);
        return panel;
    }

    private static Button MakeButton(string text, bool primary) => new()
    {
        Text = text,
        Width = 122,
        Height = 38,
        FlatStyle = FlatStyle.Flat,
        BackColor = primary ? UiTheme.Accent : UiTheme.SurfaceRaised,
        ForeColor = Color.White,
        FlatAppearance = { BorderSize = primary ? 0 : 1, BorderColor = UiTheme.Outline },
        Margin = new Padding(6, 3, 0, 3),
    };

    private enum DialogKind { Info, Success, Warning, Error }
}
