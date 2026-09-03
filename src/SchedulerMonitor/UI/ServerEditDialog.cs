using SchedulerMonitor.Models;

namespace SchedulerMonitor.UI;

internal sealed class ServerEditDialog : Form
{
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly TextBox _host = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _enabled = new() { Text = "Enabled", Checked = true, AutoSize = true };

    public ServerEditDialog(ServerConfig? existing = null)
    {
        Text = existing is null ? "Add Server" : "Edit Server";
        Width = 480; Height = 235; StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        Font = new Font("Segoe UI", 9F); BackColor = UiTheme.Page;

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 4 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 130));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.Controls.Add(new Label { Text = "Display name", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        table.Controls.Add(_name, 1, 0);
        table.Controls.Add(new Label { Text = "Hostname / IP", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        table.Controls.Add(_host, 1, 1);
        table.Controls.Add(_enabled, 1, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = UiTheme.Button("Save", true);
        var cancel = UiTheme.Button("Cancel");
        save.Click += (_, _) => SaveAndClose(existing);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        table.Controls.Add(buttons, 0, 3); table.SetColumnSpan(buttons, 2);
        Controls.Add(table);

        if (existing is not null)
        {
            _name.Text = existing.Name; _host.Text = existing.Host; _enabled.Checked = existing.Enabled;
        }
        AcceptButton = save; CancelButton = cancel;
    }

    public ServerConfig? Result { get; private set; }

    private void SaveAndClose(ServerConfig? existing)
    {
        if (string.IsNullOrWhiteSpace(_host.Text))
        {
            MessageBox.Show("Hostname or IP is required.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Result = new ServerConfig
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(_name.Text) ? _host.Text.Trim() : _name.Text.Trim(),
            Host = _host.Text.Trim(), Enabled = _enabled.Checked
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}
