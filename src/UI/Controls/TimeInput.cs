using System.ComponentModel;
using System.Globalization;

namespace SyncPlayer;

sealed class TimeInput : UserControl
{
    readonly TextBox editor = new() { BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Center };
    readonly Button up = new(), down = new();
    decimal value;
    public decimal Maximum = 864000;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Value {
        get {
            if (!decimal.TryParse(editor.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) && !decimal.TryParse(editor.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed)) throw new FormatException(Localization.T("请检查输入的秒数"));
            value = Math.Clamp(parsed, 0, Maximum); return value;
        }
        set { this.value = Math.Clamp(value, 0, Maximum); editor.Text = this.value.ToString("0.000", CultureInfo.CurrentCulture); }
    }
    public TimeInput()
    {
        Size = new Size(140, 38); BackColor = Color.White; Margin = new Padding(0, 0, 8, 0);
        Controls.AddRange([editor, up, down]); Value = 0;
        foreach (var button in new[] { up, down }) {
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.BackColor = Color.FromArgb(246, 247, 249); button.TabStop = false;
            button.Paint += (_, e) => {
                int x = button.Width / 2, y = button.Height / 2;
                using var brush = new SolidBrush(Color.FromArgb(65, 72, 82));
                Point[] points = button == up ? [new(x - 5, y + 3), new(x + 5, y + 3), new(x, y - 3)] : [new(x - 5, y - 3), new(x + 5, y - 3), new(x, y + 3)];
                e.Graphics.FillPolygon(brush, points);
            };
        }
        up.Click += (_, _) => Step(1); down.Click += (_, _) => Step(-1);
        editor.KeyDown += (_, e) => { if (e.KeyCode is Keys.Up or Keys.Down) { Step(e.KeyCode == Keys.Up ? 1 : -1); e.SuppressKeyPress = true; } };
        editor.Leave += (_, _) => { try { Value = Value; } catch (FormatException) { } };
        AccessibleName = Localization.T("跳转"); editor.AccessibleName = AccessibleName;
    }
    void Step(int increment) { try { Value = Value + increment; } catch (FormatException) { Value = value; } }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int spinner = 24;
        editor.SetBounds(5, Math.Max(0, (Height - editor.PreferredHeight) / 2), Math.Max(10, Width - spinner - 10), editor.PreferredHeight);
        up.SetBounds(Width - spinner - 1, 1, spinner, (Height - 2) / 2);
        down.SetBounds(Width - spinner - 1, Height / 2, spinner, Height - Height / 2 - 1);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); using var border = new Pen(Color.FromArgb(210, 215, 222));
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
}
