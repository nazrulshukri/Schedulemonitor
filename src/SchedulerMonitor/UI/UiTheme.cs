namespace SchedulerMonitor.UI;

internal static class UiTheme
{
    public static readonly Color Petrol = Color.FromArgb(3, 111, 123);
    public static readonly Color PetrolDark = Color.FromArgb(1, 80, 89);
    public static readonly Color Orange = Color.FromArgb(245, 130, 32);
    public static readonly Color Page = Color.FromArgb(246, 248, 249);
    public static readonly Color Border = Color.FromArgb(218, 226, 228);
    public static readonly Color Text = Color.FromArgb(43, 55, 59);

    public static Button Button(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, Height = 34, Padding = new Padding(12, 2, 12, 2),
            FlatStyle = FlatStyle.Flat, BackColor = primary ? Petrol : Color.White,
            ForeColor = primary ? Color.White : Text, Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = primary ? Petrol : Border;
        return button;
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.GridColor = Border;
        grid.RowHeadersVisible = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 242, 243);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(235, 242, 243);
        grid.EnableHeadersVisualStyles = false;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(218, 239, 241);
        grid.DefaultCellStyle.SelectionForeColor = Text;
        grid.DefaultCellStyle.Padding = new Padding(3);
        grid.RowTemplate.Height = 31;
    }
}
