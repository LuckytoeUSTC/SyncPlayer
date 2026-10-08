namespace SyncPlayer;

sealed class OffsetCell : DataGridViewTextBoxCell
{
    public override Type EditType => typeof(OffsetEditor);
    public override Type ValueType => typeof(string);
    public override object DefaultNewRowValue => "0.0";
    public override void InitializeEditingControl(int rowIndex, object? initialFormattedValue, DataGridViewCellStyle style)
    {
        base.InitializeEditingControl(rowIndex, initialFormattedValue, style);
        ((OffsetEditor)DataGridView!.EditingControl!).RawText = initialFormattedValue?.ToString() ?? "0.0";
    }
    public override void PositionEditingControl(bool setLocation, bool setSize, Rectangle bounds, Rectangle clip, DataGridViewCellStyle style, bool vertical, bool horizontal, bool firstColumn, bool firstRow)
    {
        // The text-cell implementation adds text padding a second time and clips
        // this composite editor, particularly in rows with wrapped filenames.
        var grid = DataGridView!;
        var field = TimeInput.FieldBounds(bounds, style.Font ?? grid.Font);
        grid.EditingPanel.Bounds = field;
        grid.EditingControl!.Bounds = new Rectangle(Point.Empty, field.Size);
    }
    protected override void Paint(Graphics graphics, Rectangle clip, Rectangle bounds, int rowIndex, DataGridViewElementStates state, object? value, object? formatted, string? error, DataGridViewCellStyle style, DataGridViewAdvancedBorderStyle border, DataGridViewPaintParts parts)
    {
        base.Paint(graphics, clip, bounds, rowIndex, state, value, formatted, error, style, border, parts & ~DataGridViewPaintParts.ContentForeground & ~DataGridViewPaintParts.ErrorIcon);
        if (OwningRow?.ReadOnly == true) return;
        var field = TimeInput.FieldBounds(bounds, style.Font ?? DataGridView!.Font);
        int arrow = StepArrow.WidthAt(DataGridView!.DeviceDpi);
        using var fill = new SolidBrush(Color.White); graphics.FillRectangle(fill, field);
        using var outline = new Pen(Color.FromArgb(210, 215, 222)); graphics.DrawRectangle(outline, field.X, field.Y, field.Width - 1, field.Height - 1);
        var left = new Rectangle(field.Left + 1, field.Top + 1, arrow, field.Height - 2);
        var right = new Rectangle(field.Right - arrow - 1, field.Top + 1, arrow, field.Height - 2);
        using var button = new SolidBrush(Color.FromArgb(246, 247, 249)); graphics.FillRectangle(button, left); graphics.FillRectangle(button, right);
        StepArrow.Draw(graphics, left, false, DataGridView.DeviceDpi, !ReadOnly); StepArrow.Draw(graphics, right, true, DataGridView.DeviceDpi, !ReadOnly);
        var text = new Rectangle(left.Right + 4, field.Top, right.Left - left.Right - 8, field.Height);
        TextRenderer.DrawText(graphics, formatted?.ToString(), style.Font ?? DataGridView.Font, text, ReadOnly ? Color.Gray : Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
    protected override void OnMouseDown(DataGridViewCellMouseEventArgs e)
    {
        if (!ReadOnly && e.Button == MouseButtons.Left && DataGridView != null) {
            var field = TimeInput.FieldBounds(new Rectangle(Point.Empty, Size), InheritedStyle.Font ?? DataGridView.Font);
            int arrow = StepArrow.WidthAt(DataGridView.DeviceDpi);
            if (field.Contains(e.X, e.Y) && (e.X < field.Left + arrow + 1 || e.X >= field.Right - arrow - 1)) {
                decimal.TryParse(Value?.ToString(), out var number);
                number = Math.Clamp(number + (e.X < field.Left + arrow + 1 ? -.1m : .1m), -86400, 86400);
                Value = number.ToString("0.0##"); return;
            }
        }
        base.OnMouseDown(e);
    }
}

sealed class OffsetEditor : TimeInput, IDataGridViewEditingControl
{
    DataGridView? grid;
    bool changed;
    int rowIndex;
    public OffsetEditor() {
        Minimum = -86400; Maximum = 86400;
        TextEdited += (_, _) => { changed = true; grid?.NotifyCurrentCellDirty(true); };
        CommitRequested += (_, _) => Commit();
        Leave += (_, _) => { if (IsHandleCreated) BeginInvoke((Action)(() => { if (!ContainsFocus && grid?.EditingControl == this) Commit(false); })); };
    }
    void Commit(bool focusGrid = true) { if (grid?.EndEdit() == true) { grid.CurrentCell = null; if (focusGrid) grid.Focus(); } }
    DataGridView? IDataGridViewEditingControl.EditingControlDataGridView { get => grid; set => grid = value; }
    [System.Diagnostics.CodeAnalysis.AllowNull]
    object IDataGridViewEditingControl.EditingControlFormattedValue { get => RawText; set => RawText = value?.ToString() ?? ""; }
    int IDataGridViewEditingControl.EditingControlRowIndex { get => rowIndex; set => rowIndex = value; }
    bool IDataGridViewEditingControl.EditingControlValueChanged { get => changed; set => changed = value; }
    Cursor IDataGridViewEditingControl.EditingPanelCursor => Cursors.IBeam;
    bool IDataGridViewEditingControl.RepositionEditingControlOnValueChange => false;
    object IDataGridViewEditingControl.GetEditingControlFormattedValue(DataGridViewDataErrorContexts context) => RawText;
    void IDataGridViewEditingControl.ApplyCellStyleToEditingControl(DataGridViewCellStyle style) { Font = style.Font ?? grid!.Font; BackColor = Color.White; }
    bool IDataGridViewEditingControl.EditingControlWantsInputKey(Keys key, bool gridWantsKey) => (key & Keys.KeyCode) is Keys.Enter or Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End || !gridWantsKey;
    void IDataGridViewEditingControl.PrepareEditingControlForEdit(bool selectAll) => FocusEditor(selectAll);
}

