using System.Globalization;
using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MercadinhoPdv.Forms;

internal static class UiTheme
{
    public static readonly Color Background = Color.FromArgb(13, 17, 23);
    public static readonly Color Surface = Color.FromArgb(22, 27, 34);
    public static readonly Color SurfaceRaised = Color.FromArgb(29, 36, 44);
    public static readonly Color Outline = Color.FromArgb(45, 54, 64);
    public static readonly Color Text = Color.FromArgb(231, 237, 243);
    public static readonly Color Muted = Color.FromArgb(139, 153, 168);
    public static readonly Color Accent = Color.FromArgb(16, 185, 129);
    public static readonly Color Success = Color.FromArgb(52, 211, 153);
    public static readonly Color Warning = Color.FromArgb(245, 158, 11);
    public static readonly Color Error = Color.FromArgb(248, 113, 113);
    public static readonly int Space1 = 4;
    public static readonly int Space2 = 8;
    public static readonly int Space3 = 12;
    public static readonly int Space4 = 16;
    public static readonly int Space5 = 24;

    public static void Apply(Form form)
    {
        UiWindowChrome.Apply(form);
        form.BackColor = Background;
        form.ForeColor = Text;
        form.Font = new Font("Segoe UI", 9F);
        StyleControls(form.Controls);
        FitToWorkingArea(form);
    }

    internal static void FitToWorkingArea(Form form)
    {
        form.Shown += (_, _) =>
        {
            var area = Screen.FromControl(form).WorkingArea;
            form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, area.Width), Math.Min(form.MinimumSize.Height, area.Height));
            if (form.WindowState == FormWindowState.Normal)
            {
                form.Size = new Size(Math.Min(form.Width, area.Width), Math.Min(form.Height, area.Height));
                form.Location = new Point(Math.Clamp(form.Left, area.Left, Math.Max(area.Left, area.Right - form.Width)), Math.Clamp(form.Top, area.Top, Math.Max(area.Top, area.Bottom - form.Height)));
            }
        };
    }

    private static void StyleControls(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            switch (control)
            {
                case DataGridView grid:
                    StyleGrid(grid);
                    break;
                case Button button:
                    bool primary = IsPrimaryAction(button.Text);
                    bool destructive = IsDestructiveAction(button.Text);
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = primary ? Accent : Outline;
                    button.FlatAppearance.BorderSize = primary ? 0 : 1;
                    button.BackColor = primary ? Accent : destructive ? Color.FromArgb(125, 45, 55) : SurfaceRaised;
                    button.ForeColor = primary ? Color.FromArgb(4, 37, 27) : Color.White;
                    button.Font = new Font("Segoe UI", 9F, primary ? FontStyle.Bold : FontStyle.Regular);
                    button.MinimumSize = new Size(Math.Max(button.MinimumSize.Width, 86), Math.Max(button.MinimumSize.Height, 34));
                    break;
                case TextBox textBox:
                    textBox.BackColor = SurfaceRaised;
                    textBox.ForeColor = Text;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    if (!textBox.UseSystemPasswordChar && textBox.PasswordChar == '\0' && !Equals(textBox.Tag, "preserve-case") &&
                        !textBox.Name.Contains("email", StringComparison.OrdinalIgnoreCase) &&
                        !textBox.Name.Contains("url", StringComparison.OrdinalIgnoreCase))
                        textBox.CharacterCasing = CharacterCasing.Upper;
                    break;
                case ComboBox combo:
                    combo.BackColor = SurfaceRaised;
                    combo.ForeColor = Text;
                    combo.FlatStyle = FlatStyle.Flat;
                    combo.DrawMode = DrawMode.OwnerDrawFixed;
                    combo.ItemHeight = 26;
                    combo.DrawItem += (_, e) =>
                    {
                        if (e.Index < 0 || e.Index >= combo.Items.Count) return;
                        bool selected = (e.State & DrawItemState.Selected) != 0;
                        using var background = new SolidBrush(selected ? Color.FromArgb(22, 83, 67) : SurfaceRaised);
                        e.Graphics.FillRectangle(background, e.Bounds);
                        TextRenderer.DrawText(e.Graphics, combo.GetItemText(combo.Items[e.Index]), combo.Font,
                            new Rectangle(e.Bounds.Left + 9, e.Bounds.Top, Math.Max(1, e.Bounds.Width - 12), e.Bounds.Height),
                            combo.Enabled ? Text : Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    };
                    break;
                case NumericUpDown numeric:
                    numeric.BackColor = SurfaceRaised;
                    numeric.ForeColor = Text;
                    numeric.Enter += (_, _) => numeric.BeginInvoke(new Action(() => numeric.Select(0, numeric.Text.Length)));
                    numeric.KeyPress += (_, e) =>
                    {
                        if (e.KeyChar is '.' or ',') e.KeyChar = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0];
                    };
                    break;
                case DateTimePicker date:
                    date.CalendarMonthBackground = SurfaceRaised;
                    date.CalendarForeColor = Text;
                    date.CalendarTitleBackColor = Surface;
                    date.CalendarTitleForeColor = Text;
                    date.CalendarTrailingForeColor = Muted;
                    date.BackColor = SurfaceRaised;
                    date.ForeColor = Text;
                    break;
                case CheckBox check:
                    check.ForeColor = Text;
                    break;
                case RadioButton radio:
                    radio.ForeColor = Text;
                    break;
                case TabControl tabs:
                    tabs.BackColor = Background;
                    tabs.ForeColor = Text;
                    break;
                case TabPage page:
                    page.BackColor = Background;
                    page.ForeColor = Text;
                    break;
                case GroupBox group:
                    group.BackColor = Surface;
                    group.ForeColor = Accent;
                    break;
                case TableLayoutPanel table:
                    if (table.BackColor == SystemColors.Control) table.BackColor = Background;
                    break;
                case FlowLayoutPanel flow:
                    if (flow.BackColor == SystemColors.Control) flow.BackColor = Background;
                    break;
                case Panel panel:
                    panel.BackColor = panel.BackColor == Color.Transparent ? Background : Surface;
                    break;
                case Label label:
                    if (label.ForeColor == SystemColors.ControlText) label.ForeColor = Text;
                    break;
            }
            if (control.HasChildren) StyleControls(control.Controls);
        }
    }

    private static bool IsPrimaryAction(string text) => new[] { "salvar", "aplicar", "abrir", "fechar", "entrada", "ajustar", "nova", "editar", "cadastrar", "registrar", "atualizar", "confirmar", "adicionar", "gravar", "cobrar" }.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase));
    private static bool IsDestructiveAction(string text) => new[] { "desativar", "remover", "excluir", "sangria" }.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase));

    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.GridColor = Outline;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceRaised;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(9, 0, 6, 0);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceRaised;
        grid.ColumnHeadersHeight = 38;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.Padding = new Padding(9, 0, 6, 0);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(22, 83, 67);
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(25, 31, 39);
        grid.RowTemplate.Height = 40;
        grid.RowHeadersDefaultCellStyle.BackColor = Surface;
        grid.RowHeadersDefaultCellStyle.ForeColor = Text;
        grid.GridColor = Outline;
        grid.BackgroundColor = Surface;
        grid.ForeColor = Text;
        grid.BorderStyle = BorderStyle.None;
    }
}

internal static class UiWindowChrome
{
    private const int DarkTitleBar = 20;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;
    private static readonly Lazy<Icon?> ApplicationIcon = new(() => Icon.ExtractAssociatedIcon(Application.ExecutablePath));

    public static void Apply(Form form)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (ApplicationIcon.Value is { } icon) form.Icon = (Icon)icon.Clone();
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or Win32Exception) { }

        form.HandleCreated += (_, _) => StyleTitleBar(form.Handle);
        if (form.IsHandleCreated) StyleTitleBar(form.Handle);
    }

    private static void StyleTitleBar(nint handle)
    {
        try
        {
            int enabled = 1;
            DwmSetWindowAttribute(handle, DarkTitleBar, ref enabled, sizeof(int));
            SetColor(handle, CaptionColor, UiTheme.Surface);
            SetColor(handle, BorderColor, UiTheme.Outline);
            SetColor(handle, TextColor, UiTheme.Text);
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private static void SetColor(nint handle, int attribute, Color color)
    {
        int colorRef = color.R | color.G << 8 | color.B << 16;
        DwmSetWindowAttribute(handle, attribute, ref colorRef, sizeof(int));
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}

internal sealed class UiCard : Panel
{
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 12;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = UiTheme.Outline;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowBorder { get; set; } = true;

    public UiCard()
    {
        DoubleBuffered = true;
        BackColor = UiTheme.Surface;
        Padding = new Padding(UiTheme.Space4);
        Resize += (_, _) => UpdateClip();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = CreatePath(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), CornerRadius);
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillPath(brush, path);
        if (ShowBorder)
        {
            using var pen = new Pen(BorderColor, 1);
            e.Graphics.DrawPath(pen, path);
        }
    }

    private void UpdateClip()
    {
        if (Width < 2 || Height < 2) return;
        using GraphicsPath path = CreatePath(new Rectangle(0, 0, Width, Height), CornerRadius);
        Region? previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    private static GraphicsPath CreatePath(Rectangle bounds, int radius)
    {
        int diameter = Math.Max(2, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
