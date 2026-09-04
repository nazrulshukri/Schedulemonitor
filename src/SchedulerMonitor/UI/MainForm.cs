using System.Diagnostics;
using SchedulerMonitor.Infrastructure;
using SchedulerMonitor.Models;
using SchedulerMonitor.Services;

namespace SchedulerMonitor.UI;

public sealed class MainForm : Form
{
    private readonly AppPaths _paths;
    private readonly ConfigStore _store;
    private readonly FileLogger _logger;
    private readonly BufferedGrid _grid = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly Label _lastCheck = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Label _activity = new() { AutoSize = true, ForeColor = UiTheme.Petrol };
    private readonly Dictionary<string, Label> _summary = new();
    private readonly List<Button> _actionButtons = [];
    private readonly Button _runButton;
    private readonly ProgressBar _progress = new()
    {
        Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30,
        Width = 150, Height = 6, Visible = false, Margin = new Padding(6, 15, 6, 3)
    };
    private MonitorRunResult? _lastRun;

    /// <summary>DataGridView repaints row by row; buffering it removes the flicker on refresh.</summary>
    private sealed class BufferedGrid : DataGridView
    {
        public BufferedGrid() => DoubleBuffered = true;
    }

    public MainForm(AppPaths paths, ConfigStore store, FileLogger logger)
    {
        _paths = paths; _store = store; _logger = logger;
        Text = "Task Scheduler Monitor";
        MinimumSize = new Size(980, 620); Size = new Size(1180, 720);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 9F);
        BackColor = UiTheme.Page;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildSummary(), 0, 1);
        root.Controls.Add(BuildGridPanel(), 0, 2);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(17, 12, 17, 8), FlowDirection = FlowDirection.LeftToRight };
        _runButton = UiTheme.Button("Run Check", true);
        var send = UiTheme.Button("Send Report");
        var config = UiTheme.Button("Configuration");
        var logs = UiTheme.Button("Open Log Folder");
        _runButton.Click += async (_, _) => await RunCheckAsync();
        send.Click += async (_, _) => await SendReportAsync();
        config.Click += (_, _) => OpenConfiguration();
        logs.Click += (_, _) => OpenFolder(_paths.LogDirectory);
        actions.Controls.Add(_runButton); actions.Controls.Add(send); actions.Controls.Add(config); actions.Controls.Add(logs);
        actions.Controls.Add(_progress);
        actions.Controls.Add(_activity);
        _actionButtons.AddRange([_runButton, send, config, logs]);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);

        ConfigureGrid();
        Shown += (_, _) => ShowInitialState();
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Petrol, Padding = new Padding(22, 12, 22, 8) };
        var title = new Label { Text = "TASK SCHEDULER MONITOR", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 17F) };
        _lastCheck.ForeColor = Color.FromArgb(220, 240, 242); _lastCheck.Location = new Point(24, 48);
        title.Location = new Point(20, 10); panel.Controls.Add(title); panel.Controls.Add(_lastCheck);
        return panel;
    }

    private Control BuildSummary()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(17, 14, 10, 8), WrapContents = false, AutoScroll = true };
        foreach (var key in new[] { "Total", "Success", "Running", "Long Running", "Abnormal", "Pending", "Problems" })
        {
            var card = new Panel { Width = 132, Height = 72, BackColor = Color.White, Margin = new Padding(4) };
            card.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle, UiTheme.Border, ButtonBorderStyle.Solid);
            var value = new Label { Text = "0", AutoSize = true, Location = new Point(12, 8), Font = new Font("Segoe UI Semibold", 20F), ForeColor = UiTheme.Petrol };
            var label = new Label { Text = key.ToUpperInvariant(), AutoSize = true, Location = new Point(13, 45), ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8F) };
            card.Controls.Add(value); card.Controls.Add(label); panel.Controls.Add(card); _summary[key] = value;
        }
        return panel;
    }

    private Control BuildGridPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(21, 5, 21, 4) };
        panel.Controls.Add(_grid); return panel;
    }

    private void ConfigureGrid()
    {
        UiTheme.StyleGrid(_grid);
        _grid.Columns.Add("Server", "Server");
        _grid.Columns.Add("Task", "Task");
        _grid.Columns.Add("Status", "Monitor Status");
        _grid.Columns.Add("Windows", "Windows State");
        _grid.Columns.Add("Duration", "Running For");
        _grid.Columns.Add("Event", "Events");
        _grid.Columns.Add("LastRun", "Last Run");
        _grid.Columns.Add("Result", "Last Result");
        _grid.Columns.Add("NextRun", "Next Run");
        _grid.Columns[1].FillWeight = 160;
        _grid.Columns[5].FillWeight = 150;
        _grid.CellDoubleClick += (_, eventArgs) => ShowTaskDetails(eventArgs.RowIndex);
    }

    private void ShowInitialState()
    {
        var config = _store.Load();
        var selected = config.MonitoredTasks.Count(task => task.Enabled);
        _lastCheck.Text = selected == 0 ? "No tasks selected. Open Configuration to begin." : $"{selected} task(s) selected • Click Run Check";
    }

    private async Task RunCheckAsync()
    {
        try
        {
            SetBusy(true, "Starting check...");
            var config = _store.Load();
            if (!config.MonitoredTasks.Any(task => task.Enabled))
            {
                MessageBox.Show("No tasks are selected. Scan and select tasks in Configuration first.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information); return;
            }
            var progress = new Progress<string>(message => _activity.Text = message);
            var monitor = new MonitoringService(new RemoteTaskQuery(_logger), _logger);

            // schtasks, CSV parsing and report writing all stay off the UI thread so the
            // window keeps repainting while the check runs.
            var run = await Task.Run(() => monitor.RunAsync(config, progress));
            var report = await Task.Run(() => new ReportBuilder(_paths).BuildAndSave(run));

            _lastRun = run;
            DisplayRun(run);

            var alerts = new AlertService(new EmailService(_logger), new AlertStateStore(_paths, _logger), _logger);
            var sent = await alerts.SendAsync(config, run);
            _activity.Text = sent > 0
                ? $"Completed • {Path.GetFileName(report.FilePath)} • {sent} alert(s) sent"
                : $"Completed • {Path.GetFileName(report.FilePath)}";
        }
        catch (Exception ex)
        {
            _logger.Error("Manual monitoring failed", ex);
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    private void DisplayRun(MonitorRunResult run)
    {
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var task in run.Tasks.OrderBy(task => StatusOrder(task.Status)).ThenBy(task => task.ServerName).ThenBy(task => task.DisplayName))
        {
            var rowIndex = _grid.Rows.Add(task.ServerName, task.DisplayName, task.StatusText, task.WindowsState,
                task.RunningFor is { } elapsed ? MonitoringService.Describe(elapsed) : "-",
                EmptyDash(task.EventSummary),
                task.LastRunTime?.ToString("dd-MMM-yyyy HH:mm") ?? "-", EmptyDash(task.LastResult),
                task.NextRunTime?.ToString("dd-MMM-yyyy HH:mm") ?? "-");
            var row = _grid.Rows[rowIndex]; row.Tag = task;
            row.Cells[2].Style.ForeColor = StatusColor(task.Status);
            row.Cells[2].Style.Font = new Font(_grid.Font, FontStyle.Bold);
        }
        _grid.ResumeLayout();
        _summary["Total"].Text = run.Tasks.Count.ToString();
        _summary["Success"].Text = run.Tasks.Count(task => task.Status == MonitorStatus.Success).ToString();
        _summary["Running"].Text = run.Tasks.Count(task => task.Status == MonitorStatus.Running).ToString();
        _summary["Long Running"].Text = run.Tasks.Count(task => task.Status == MonitorStatus.LongRunning).ToString();
        _summary["Abnormal"].Text = run.Tasks.Count(task => task.Status == MonitorStatus.Abnormal).ToString();
        _summary["Pending"].Text = run.Tasks.Count(task => task.Status == MonitorStatus.Pending).ToString();
        _summary["Problems"].Text = run.Tasks.Count(task => task.Status is MonitorStatus.Failed or MonitorStatus.Overdue or MonitorStatus.Disabled or MonitorStatus.Unreachable or MonitorStatus.LongRunning or MonitorStatus.Abnormal).ToString();
        _lastCheck.Text = $"Last check: {run.CompletedAt:dd-MMM-yyyy HH:mm:ss} • Servers: {run.ServerCount}";
    }

    private async Task SendReportAsync()
    {
        if (_lastRun is null)
        {
            await RunCheckAsync();
            if (_lastRun is null) return;
        }
        try
        {
            SetBusy(true, "Sending report...");
            var config = _store.Load();
            var run = _lastRun!;
            var report = await Task.Run(() => new ReportBuilder(_paths).BuildAndSave(run));
            await new EmailService(_logger).SendAsync(config.Email, report.Subject, report.Html);
            MessageBox.Show("Report sent successfully.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger.Error("Unable to send report", ex);
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    private void OpenConfiguration()
    {
        using var form = new ConfigurationForm(_paths, _store, _logger);
        form.ShowDialog(this); ShowInitialState();
    }

    private void ShowTaskDetails(int rowIndex)
    {
        if (rowIndex < 0 || _grid.Rows[rowIndex].Tag is not TaskMonitorResult task) return;
        var details = $"Server: {task.ServerName}\r\nHost: {task.Host}\r\n\r\nTask: {task.TaskPath}\r\nMonitor Status: {task.StatusText}\r\nWindows State: {task.WindowsState}\r\n\r\nLast Run: {task.LastRunTime?.ToString("F") ?? "-"}\r\nLast Result: {EmptyDash(task.LastResult)}\r\nNext Run: {task.NextRunTime?.ToString("F") ?? "-"}\r\nRunning For: {(task.RunningFor is { } elapsed ? MonitoringService.Describe(elapsed) : "-")}\r\nExpected Within: {(task.LongRunningThreshold is { } limit ? MonitoringService.Describe(limit) : "-")}\r\nWindows Event: {EmptyDash(task.EventSummary)}\r\nLast Checked: {task.CheckedAt:F}\r\n\r\nDetail: {task.Detail}";
        MessageBox.Show(details, "Task Details", MessageBoxButtons.OK, task.Status == MonitorStatus.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void SetBusy(bool busy, string? text = null)
    {
        foreach (var button in _actionButtons) button.Enabled = !busy;
        _progress.Visible = busy;
        Cursor = busy ? Cursors.AppStarting : Cursors.Default;
        if (text is not null) _activity.Text = text;
        _activity.Refresh();
    }

    private static void OpenFolder(string path) => Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    private static string EmptyDash(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;
    private static int StatusOrder(MonitorStatus status) => status switch { MonitorStatus.Failed => 0, MonitorStatus.Unreachable => 1, MonitorStatus.Abnormal => 2, MonitorStatus.Overdue => 3, MonitorStatus.Disabled => 4, MonitorStatus.LongRunning => 5, MonitorStatus.Running => 6, MonitorStatus.Pending => 7, _ => 8 };

    private void InitializeComponent()
    {

    }

    private static Color StatusColor(MonitorStatus status) => status switch { MonitorStatus.Success => Color.FromArgb(32, 114, 69), MonitorStatus.Running or MonitorStatus.Pending => Color.FromArgb(18, 97, 160), MonitorStatus.LongRunning => Color.FromArgb(198, 74, 0), MonitorStatus.Abnormal => Color.FromArgb(146, 43, 122), MonitorStatus.Overdue or MonitorStatus.Disabled => Color.FromArgb(192, 100, 0), _ => Color.FromArgb(180, 35, 24) };
}
