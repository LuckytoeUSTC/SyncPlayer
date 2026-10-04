using System.Drawing.Drawing2D;

namespace SyncPlayer;

sealed class ToggleSwitch : CheckBox
{
    public ToggleSwitch() { AutoSize = false; Size = new Size(180, 36); Margin = new Padding(0, 0, 0, 8); Cursor = Cursors.Hand; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(Checked ? Color.FromArgb(75, 84, 98) : Color.FromArgb(195, 200, 208));
        int height = Height - 6, top = (Height - height) / 2, width = height * 2;
        e.Graphics.FillRectangle(fill, 0, top, width, height);
        using var knob = new SolidBrush(Color.White); e.Graphics.FillRectangle(knob, Checked ? width - height + 3 : 3, top + 3, height - 6, height - 6);
        var textBounds = new Rectangle(width + 12, 0, Width - width - 12, Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, textBounds);
    }
}
