using System.Drawing.Drawing2D;

namespace SyncPlayer;

sealed class IconButton : Button
{
    static readonly Lazy<Bitmap> LanguageIcon = new(() => {
        using var stream = typeof(IconButton).Assembly.GetManifestResourceStream("SyncPlayer.LanguageIcon.png") ?? throw new InvalidOperationException("Language icon missing");
        using var original = Image.FromStream(stream);
        return new Bitmap(original);
    });
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
            var icon = LanguageIcon.Value;
            using var attributes = new System.Drawing.Imaging.ImageAttributes();
            attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix([
                [0, 0, 0, 0, 0], [0, 0, 0, 0, 0], [0, 0, 0, 0, 0],
                [0, 0, 0, 1, 0], [color.R / 255f, color.G / 255f, color.B / 255f, 0, 1]
            ]));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(icon, new Rectangle(-12, -12, 24, 24), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, attributes);
        } else {
            // A typographic question mark provides a continuous curve and balanced dot.
            g.ResetTransform();
            using var font = new Font("Segoe UI", Height * .65f, FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, "?", font, ClientRectangle, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
        g.ResetTransform();
    }
}


