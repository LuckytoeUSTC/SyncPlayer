using System.ComponentModel;
using System.Globalization;

namespace SyncPlayer;

class TimeInput : UserControl
{
    readonly TextBox editor = new() { BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Center };
    readonly StepArrow decrease = new(false), increase = new(true);
    decimal value;
    public decimal Minimum = 0, Maximum = 864000, Increment = .1m;
    internal event EventHandler? TextEdited, CommitRequested;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string RawText { get => editor.Text; set => editor.Text = value; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Value {
        get {
            if (string.IsNullOrWhiteSpace(editor.Text)) return value = 0;
            if (!decimal.TryParse(editor.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) && !decimal.TryParse(editor.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) throw new FormatException(Localization.T("请检查输入的秒数"));
            value = Math.Clamp(parsed, Minimum, Maximum); return value;
        }
        set { this.value = Math.Clamp(value, Minimum, Maximum); editor.Text = this.value.ToString("0.0##", CultureInfo.CurrentCulture); }
    }
    public TimeInput()
    {
        Size = new Size(180, 38); BackColor = Color.White; Margin = new Padding(0, 0, 8, 0);
        Controls.AddRange([decrease, editor, increase]); Value = 0;
        decrease.Click += (_, _) => Step(-1); increase.Click += (_, _) => Step(1);
        editor.TextChanged += (_, _) => TextEdited?.Invoke(this, EventArgs.Empty);
        editor.KeyDown += (_, e) => {
            if (e.KeyCode is Keys.Up or Keys.Down) { Step(e.KeyCode == Keys.Up ? 1 : -1); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Enter) { CommitRequested?.Invoke(this, EventArgs.Empty); e.SuppressKeyPress = true; }
        };
        AccessibleName = Localization.T("跳转"); editor.AccessibleName = AccessibleName;
    }
    internal void FocusEditor(bool selectAll) { editor.Focus(); if (selectAll) editor.SelectAll(); }
    internal void Submit() => ProcessDialogKey(Keys.Enter);
    protected override bool ProcessDialogKey(Keys keyData) {
        if ((keyData & Keys.KeyCode) == Keys.Enter) { CommitRequested?.Invoke(this, EventArgs.Empty); return true; }
        return base.ProcessDialogKey(keyData);
    }
    internal void Step(int direction) { try { Value = Value + direction * Increment; } catch (FormatException) { Value = value; } CommitRequested?.Invoke(this, EventArgs.Empty); }
    internal static Rectangle FieldBounds(Rectangle cell, Font font) {
        int height = Math.Min(cell.Height - 4, font.Height + 10);
        return new Rectangle(cell.Left + 4, cell.Top + (cell.Height - height) / 2, cell.Width - 8, height);
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int width = StepArrow.WidthAt(DeviceDpi);
        decrease.SetBounds(1, 1, width, Height - 2); increase.SetBounds(Width - width - 1, 1, width, Height - 2);
        editor.SetBounds(width + 5, Math.Max(0, (Height - editor.PreferredHeight) / 2), Math.Max(10, Width - 2 * width - 10), editor.PreferredHeight);
    }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using var border = new Pen(Color.FromArgb(210, 215, 222)); e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1); }
}

sealed class StepArrow(bool forward) : Button
{
    public StepArrow() : this(true) { }
    public static int WidthAt(int dpi) => (int)Math.Round(24 * dpi / 96.0);
    public static void Draw(Graphics graphics, Rectangle bounds, bool forward, int dpi, bool enabled = true)
    {
        int x = bounds.Left + bounds.Width / 2, y = bounds.Top + bounds.Height / 2, radius = (int)Math.Round(4 * dpi / 96.0);
        using var brush = new SolidBrush(enabled ? Color.FromArgb(65, 72, 82) : Color.FromArgb(160, 166, 174));
        Point[] points = forward ? [new(x - radius / 2, y - radius), new(x - radius / 2, y + radius), new(x + radius / 2, y)] : [new(x + radius / 2, y - radius), new(x + radius / 2, y + radius), new(x - radius / 2, y)];
        graphics.FillPolygon(brush, points);
    }
    protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Color.FromArgb(246, 247, 249)); Draw(e.Graphics, ClientRectangle, forward, DeviceDpi, Enabled); }
    protected override void OnCreateControl() { base.OnCreateControl(); TabStop = false; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; }
}
