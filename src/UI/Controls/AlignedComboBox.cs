namespace SyncPlayer;

sealed class AlignedComboBox : ComboBox
{
    public AlignedComboBox() { DrawMode = DrawMode.OwnerDrawFixed; DropDownStyle = ComboBoxStyle.DropDownList; }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        e.DrawBackground();
        string text = e.Index >= 0 ? Items[e.Index]?.ToString() ?? "" : Text;
        var bounds = Rectangle.Inflate(e.Bounds, -4, 0);
        TextRenderer.DrawText(e.Graphics, text, e.Font ?? Font, bounds, e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
}

