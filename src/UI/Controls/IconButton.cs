using System.Drawing.Drawing2D;

namespace SyncPlayer;

sealed class IconButton : Button
{
    public string Symbol = "play";
    public bool Emphasized;
    public IconButton()
    {
        Size = new Size(48, 48); FlatStyle = FlatStyle.Flat; BackColor = Color.White;
        FlatAppearance.BorderColor = Color.FromArgb(210, 215, 222);
        Margin = new Padding(0, 3, 8, 3); AccessibleRole = AccessibleRole.PushButton;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(Width / 2f, Height / 2f);
        g.ScaleTransform(Width / 36f, Height / 36f);
        var color = Enabled ? Emphasized ? Color.White : Color.FromArgb(65, 72, 82) : Color.Gray;
        using var pen = new Pen(color, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(color);
        if (Symbol == "play") g.FillPolygon(brush, [new(-5, -8), new(8, 0), new(-5, 8)]);
        else if (Symbol == "pause") { g.FillRectangle(brush, -7, -8, 5, 16); g.FillRectangle(brush, 2, -8, 5, 16); }
        else if (Symbol == "refresh") {
            g.DrawArc(pen, -8, -8, 16, 16, 45, 280);
            g.FillPolygon(brush, [new(8, -8), new(8, -1), new(1, -3)]);
        } else if (Symbol == "settings") {
            var points = new List<PointF>();
            for (int tooth = 0; tooth < 8; tooth++) for (int corner = 0; corner < 4; corner++) {
                double angle = (tooth * 45 + corner * 11.25 - 90) * Math.PI / 180;
                float radius = corner is 1 or 2 ? 12 : 8.5f;
                points.Add(new((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius));
            }
            g.FillPolygon(brush, points.ToArray());
            using var hole = new SolidBrush(BackColor); g.FillEllipse(hole, -4, -4, 8, 8);
        } else {
            // A typographic question mark provides a continuous curve and balanced dot.
            g.ResetTransform();
            using var font = new Font("Segoe UI", Height * .65f, FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, "?", font, ClientRectangle, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
        g.ResetTransform();
    }
}
