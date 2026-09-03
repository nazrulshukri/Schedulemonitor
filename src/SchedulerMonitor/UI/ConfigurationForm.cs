using SchedulerMonitor.Infrastructure;
using SchedulerMonitor.Models;
using SchedulerMonitor.Services;

namespace SchedulerMonitor.UI;

public sealed class ConfigurationForm : Form
{
    private const string DefaultTaskFolder = @"\Microsoft\BE1MES";

    private readonly AppPaths _paths;
    private readonly ConfigStore _store;
    private readonly FileLogger _logger;
    private readonly AppConfig _config;

    private readonly DataGridView _serverGrid = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly ComboBox _jobServer = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly TextBox _taskFolder = new() { Width = 220, Text = DefaultTaskFolder };
    private readonly CheckBox _folderOnly = new() { Text = "Limit to folder", AutoSize = true, Checked = true };
    private readonly TextBox _jobSearch = new() { Width = 230, PlaceholderText = "Search tasks" };
    private readonly CheckBox _selectedOnly = new() { Text = "Show selected only", AutoSize = true };
    private readonly DataGridView _jobGrid = new() { Dock = DockStyle.Fill };
    private readonly Label _jobMessage = new() { AutoSize = true, ForeColor = Color.DimGray };
    private List<DiscoveredTask> _scannedTasks = [];
    private HashSet<string> _workingSelection = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _workingLimits = new(StringComparer.OrdinalIgnoreCase);
    private bool _loadingMonitoring;
    private readonly NumericUpDown _defaultLongRunning = new() { Minimum = 0, Maximum = 10080, Value = 5, Width = 90 };
    private readonly CheckBox _useRepeatInterval = new()
    {
        Text = "Use the repeat interval from Task Scheduler as the limit when a task has one",
        AutoSize = true
    };
    private readonly CheckBox _useEventLog = new()
    {
        Text = "Flag LONG RUNNING from Task Scheduler events (322 / 324: a start was skipped, instance already running)",
        AutoSize = true
    };
    private readonly TextBox _eventIds = new() { Width = 140 };
    private readonly NumericUpDown _eventLookback = new() { Minimum = 5, Maximum = 20160, Value = 720, Width = 90 };

    private readonly CheckBox _emailEnabled = new() { Text = "Send email after automatic monitoring", AutoSize = true };
    private readonly TextBox _smtp = new();
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 25 };
    private readonly CheckBox _tls = new() { Text = "Use TLS / SSL", AutoSize = true };
    private readonly TextBox _username = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly TextBox _sender = new();
    private readonly TextBox _recipients = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };

    private readonly CheckBox _scheduleEnabled = new() { Text = "Daily monitoring enabled", AutoSize = true };
    private readonly DateTimePicker _runTime = new() { Format = DateTimePickerFormat.Time, ShowUpDown = true, Width = 120 };
    private readonly Label _scheduleStatus = new() { AutoSize = true, ForeColor = Color.DimGray };

    public ConfigurationForm(AppPaths paths, ConfigStore store, FileLogger logger)
    {
        _paths = paths; _store = store; _logger = logger; _config = store.Load();
        Text = "Scheduler Monitor Configuration";
        MinimumSize = new Size(920, 620); Size = new Size(1050, 700);
        StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 9F); BackColor = UiTheme.Page;

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(16, 6) };
        tabs.TabPages.Add(BuildServersTab());
        tabs.TabPages.Add(BuildJobsTab());
        tabs.TabPages.Add(BuildEmailTab());
        tabs.TabPages.Add(BuildScheduleTab());
        Controls.Add(tabs);

        LoadServers(); LoadEmail(); LoadSchedule(); LoadMonitoring();
        Shown += async (_, _) => await RefreshScheduleStatusAsync();
    }

    private TabPage BuildServersTab()
    {
        var page = NewPage("Servers");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        UiTheme.StyleGrid(_serverGrid);
        _serverGrid.Columns.Add("Name", "Display Name");
        _serverGrid.Columns.Add("Host", "Hostname / IP");
        _serverGrid.Columns.Add("Enabled", "Enabled");
        _serverGrid.CellDoubleClick += (_, eventArgs) => { if (eventArgs.RowIndex >= 0) EditServer(); };

        var actions = ActionBar();
        var add = UiTheme.Button("Add"); var edit = UiTheme.Button("Edit");
        var remove = UiTheme.Button("Remove"); var test = UiTheme.Button("Test Connection", true);
        add.Click += (_, _) => AddServer(); edit.Click += (_, _) => EditServer(); remove.Click += (_, _) => RemoveServer();
        test.Click += async (_, _) => await TestServerAsync();
        actions.Controls.Add(add); actions.Controls.Add(edit); actions.Controls.Add(remove); actions.Controls.Add(test);

        actions.Dock = DockStyle.Fill;
        layout.Controls.Add(_serverGrid, 0, 0); layout.Controls.Add(actions, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildJobsTab()
    {
        var page = NewPage("Tasks");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 90, Padding = new Padding(10), WrapContents = true };
        top.Controls.Add(new Label { Text = "Server", AutoSize = true, Margin = new Padding(3, 9, 5, 3) });
        top.Controls.Add(_jobServer);
        top.Controls.Add(new Label { Text = "Folder", AutoSize = true, Margin = new Padding(10, 9, 5, 3) });
        top.Controls.Add(_taskFolder);
        top.Controls.Add(_folderOnly);
        var scan = UiTheme.Button("Scan Tasks", true); scan.Click += async (_, _) => await ScanTasksAsync();
        top.Controls.Add(scan); top.Controls.Add(_jobSearch); top.Controls.Add(_selectedOnly);
        var selectAll = UiTheme.Button("Select All"); var clear = UiTheme.Button("Clear");
        selectAll.Click += (_, _) => SetAllVisible(true); clear.Click += (_, _) => SetAllVisible(false);
        top.Controls.Add(selectAll); top.Controls.Add(clear); top.Controls.Add(_jobMessage);
        _jobServer.SelectedIndexChanged += (_, _) => ResetTaskView();
        _jobSearch.TextChanged += (_, _) => RebuildTaskGrid();
        _selectedOnly.CheckedChanged += (_, _) => RebuildTaskGrid();
        _folderOnly.CheckedChanged += (_, _) => RebuildTaskGrid();
        _taskFolder.TextChanged += (_, _) => RebuildTaskGrid();

        UiTheme.StyleGrid(_jobGrid); _jobGrid.ReadOnly = false;
        _jobGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Monitor", HeaderText = "Monitor", FillWeight = 45 });
        _jobGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Task", HeaderText = "Task Name", ReadOnly = true, FillWeight = 130 });
        _jobGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Path", HeaderText = "Task Path", ReadOnly = true, FillWeight = 210 });
        _jobGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "Current State", ReadOnly = true, FillWeight = 75 });
        _jobGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Limit", HeaderText = "Max Run (min)", FillWeight = 70,
            ToolTipText = "Runtime budget for this task. 0 or empty uses the schedule interval, otherwise the default limit."
        });
        _jobGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_jobGrid.IsCurrentCellDirty) _jobGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _jobGrid.CellValueChanged += JobGridCellValueChanged;

        var bottom = ActionBar();
        var save = UiTheme.Button("Save Selection", true); save.Click += (_, _) => SaveTaskSelection();
        bottom.Controls.Add(save);
        bottom.Controls.Add(new Label { Text = "Only selected tasks appear in monitoring and email. Max Run flags a task as LONG RUNNING once it exceeds that many minutes.", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(10, 9, 3, 3) });

        top.Dock = DockStyle.Fill; bottom.Dock = DockStyle.Fill;
        layout.Controls.Add(top, 0, 0); layout.Controls.Add(_jobGrid, 0, 1); layout.Controls.Add(bottom, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildEmailTab()
    {
        var page = NewPage("Email");
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, Height = 440, Padding = new Padding(22), ColumnCount = 2, RowCount = 9 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(panel, 0, "", _emailEnabled);
        AddField(panel, 1, "SMTP server", _smtp);
        AddField(panel, 2, "Port", _port);
        AddField(panel, 3, "", _tls);
        AddField(panel, 4, "Username (optional)", _username);
        AddField(panel, 5, "Password (optional)", _password);
        AddField(panel, 6, "Sender email", _sender);
        AddField(panel, 7, "Recipients\r\n(one per line)", _recipients, 86);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var save = UiTheme.Button("Save Email", true); var test = UiTheme.Button("Send Test Email");
        save.Click += (_, _) => SaveEmail(true); test.Click += async (_, _) => await TestEmailAsync();
        actions.Controls.Add(save); actions.Controls.Add(test); panel.Controls.Add(actions, 1, 8);
        page.Controls.Add(panel); return page;
    }

    private TabPage BuildScheduleTab()
    {
        var page = NewPage("Schedule");
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, Height = 560, Padding = new Padding(25), ColumnCount = 2, RowCount = 10 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(panel, 0, "", _scheduleEnabled);
        AddField(panel, 1, "Daily run time", _runTime);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var register = UiTheme.Button("Register Daily Task", true); var remove = UiTheme.Button("Remove Schedule");
        register.Click += async (_, _) => await RegisterScheduleAsync(); remove.Click += async (_, _) => await RemoveScheduleAsync();
        actions.Controls.Add(register); actions.Controls.Add(remove); panel.Controls.Add(actions, 1, 2);
        panel.Controls.Add(new Label { Text = "Current", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        panel.Controls.Add(_scheduleStatus, 1, 3);
        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(650, 0),
            ForeColor = Color.DimGray,
            Text = "The tool registers SchedulerMonitor-Daily in Windows Task Scheduler. It runs this portable EXE with --run --silent. Register it while signed in with the Windows account that has permission to query the remote servers."
        };
        panel.Controls.Add(note, 1, 4);
        AddField(panel, 5, "Default max run (min)", _defaultLongRunning);
        AddField(panel, 6, "", _useRepeatInterval);
        AddField(panel, 7, "", _useEventLog);
        AddField(panel, 8, "Event IDs", _eventIds);
        AddField(panel, 9, "Event lookback (min)", _eventLookback);
        _defaultLongRunning.ValueChanged += (_, _) => SaveMonitoring();
        _useRepeatInterval.CheckedChanged += (_, _) => SaveMonitoring();
        _useEventLog.CheckedChanged += (_, _) => SaveMonitoring();
        _eventIds.Leave += (_, _) => SaveMonitoring();
        _eventLookback.ValueChanged += (_, _) => SaveMonitoring();
        page.Controls.Add(panel); return page;
    }

    private void LoadServers()
    {
        _serverGrid.Rows.Clear(); _jobServer.Items.Clear();
        foreach (var server in _config.Servers)
        {
            var row = _serverGrid.Rows[_serverGrid.Rows.Add(server.Name, server.Host, server.Enabled ? "Yes" : "No")];
            row.Tag = server; _jobServer.Items.Add(server);
        }
        if (_jobServer.Items.Count > 0) _jobServer.SelectedIndex = 0;
    }

    private void AddServer()
    {
        using var dialog = new ServerEditDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        _config.Servers.Add(dialog.Result); SaveConfig(); LoadServers();
    }

    private void EditServer()
    {
        if (SelectedServerRow() is not { } server) return;
        using var dialog = new ServerEditDialog(server);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        var index = _config.Servers.FindIndex(item => item.Id == server.Id);
        _config.Servers[index] = dialog.Result; SaveConfig(); LoadServers();
    }

    private void RemoveServer()
    {
        if (SelectedServerRow() is not { } server) return;
        if (MessageBox.Show($"Remove {server} and its selected tasks?", Text, MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _config.Servers.RemoveAll(item => item.Id == server.Id);
        _config.MonitoredTasks.RemoveAll(task => task.ServerId == server.Id);
        SaveConfig(); LoadServers();
    }

    private async Task TestServerAsync()
    {
        if (SelectedServerRow() is not { } server) return;
        try
        {
            UseWaitCursor = true;
            var tasks = await new RemoteTaskQuery(_logger).ScanAsync(server.Host, _config.Monitoring.TimeoutSeconds);
            MessageBox.Show($"Connection successful. {tasks.Count} scheduled task(s) found.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }

    private async Task ScanTasksAsync()
    {
        if (_jobServer.SelectedItem is not ServerConfig server) return;
        try
        {
            UseWaitCursor = true; _jobMessage.Text = $"Scanning {server}...";
            var query = new RemoteTaskQuery(_logger);
            var timeout = _config.Monitoring.TimeoutSeconds;
            var scanned = (await Task.Run(() => query.ScanAsync(server.Host, timeout))).ToList();
            var saved = _config.MonitoredTasks.Where(task => task.ServerId == server.Id && task.Enabled).ToList();
            _workingSelection = new HashSet<string>(saved.Select(task => task.TaskPath), StringComparer.OrdinalIgnoreCase);
            _workingLimits = saved.Where(task => task.LongRunningMinutes > 0)
                .ToDictionary(task => task.TaskPath, task => task.LongRunningMinutes, StringComparer.OrdinalIgnoreCase);

            foreach (var missing in saved.Where(savedTask => scanned.All(task => !task.TaskPath.Equals(savedTask.TaskPath, StringComparison.OrdinalIgnoreCase))))
            {
                scanned.Add(new DiscoveredTask { TaskPath = missing.TaskPath, DisplayName = missing.DisplayName, WindowsState = "Not Found", Enabled = true });
            }
            _scannedTasks = scanned.Where(InFolder)
                .OrderBy(task => task.TaskPath, StringComparer.OrdinalIgnoreCase).ToList();
            RebuildTaskGrid();
            _jobMessage.Text = _folderOnly.Checked
                ? $"{_scannedTasks.Count} found in {CurrentFolder()} • {_workingSelection.Count} selected"
                : $"{_scannedTasks.Count} found • {_workingSelection.Count} selected";
        }
        catch (Exception ex)
        {
            _jobMessage.Text = "Scan failed"; MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    private void ResetTaskView()
    {
        _scannedTasks = []; _workingSelection.Clear(); _workingLimits.Clear(); _jobGrid.Rows.Clear();
        if (_jobServer.SelectedItem is ServerConfig server)
        {
            var saved = _config.MonitoredTasks.Where(task => task.ServerId == server.Id && task.Enabled).ToList();
            _workingSelection = new HashSet<string>(saved.Select(task => task.TaskPath), StringComparer.OrdinalIgnoreCase);
            _workingLimits = saved.Where(task => task.LongRunningMinutes > 0)
                .ToDictionary(task => task.TaskPath, task => task.LongRunningMinutes, StringComparer.OrdinalIgnoreCase);
            _jobMessage.Text = $"{_workingSelection.Count} saved • Click Scan Tasks";
        }
    }

    private string CurrentFolder()
    {
        var folder = _taskFolder.Text.Trim().Replace('/', '\\');
        if (folder.Length == 0) return string.Empty;
        if (!folder.StartsWith('\\')) folder = "\\" + folder;
        return folder.TrimEnd('\\');
    }

    private bool InFolder(DiscoveredTask task)
    {
        if (!_folderOnly.Checked) return true;
        var folder = CurrentFolder();
        if (folder.Length == 0) return true;
        var path = task.TaskPath.Replace('/', '\\');
        if (!path.StartsWith('\\')) path = "\\" + path;
        return path.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildTaskGrid()
    {
        if (_jobGrid.IsCurrentCellDirty) _jobGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        var search = _jobSearch.Text.Trim();
        _jobGrid.Rows.Clear();
        foreach (var task in _scannedTasks.Where(task =>
                     InFolder(task)
                     && (search.Length == 0 || task.TaskPath.Contains(search, StringComparison.OrdinalIgnoreCase))
                     && (!_selectedOnly.Checked || _workingSelection.Contains(task.TaskPath))))
        {
            var row = _jobGrid.Rows[_jobGrid.Rows.Add(_workingSelection.Contains(task.TaskPath), task.DisplayName,
                task.TaskPath, task.WindowsState, LimitText(task))];
            row.Tag = task;
            if (task.WindowsState.Equals("Not Found", StringComparison.OrdinalIgnoreCase)) row.DefaultCellStyle.ForeColor = Color.FromArgb(180, 35, 24);
        }
    }

    private void JobGridCellValueChanged(object? sender, DataGridViewCellEventArgs eventArgs)
    {
        if (eventArgs.RowIndex < 0 || _jobGrid.Rows[eventArgs.RowIndex].Tag is not DiscoveredTask task) return;
        var row = _jobGrid.Rows[eventArgs.RowIndex];

        if (eventArgs.ColumnIndex == 4)
        {
            var text = Convert.ToString(row.Cells[4].Value)?.Trim() ?? "";
            if (text.Length == 0 || (int.TryParse(text, out var minutes) && minutes == 0))
            {
                _workingLimits.Remove(task.TaskPath);
                row.Cells[4].Value = AutomaticLimitText(task);
            }
            else if (int.TryParse(text, out minutes) && minutes > 0)
            {
                _workingLimits[task.TaskPath] = minutes;
                row.Cells[4].Value = minutes.ToString();
            }
            else
            {
                row.Cells[4].Value = LimitText(task);
            }
            return;
        }

        if (eventArgs.ColumnIndex != 0) return;
        var selected = Convert.ToBoolean(row.Cells[0].Value);
        if (selected) _workingSelection.Add(task.TaskPath); else _workingSelection.Remove(task.TaskPath);
        _jobMessage.Text = $"{_scannedTasks.Count} found • {_workingSelection.Count} selected";
    }

    /// <summary>Cell text for the per-task runtime budget: an explicit value, or the automatic one.</summary>
    private string LimitText(DiscoveredTask task) =>
        _workingLimits.TryGetValue(task.TaskPath, out var minutes) && minutes > 0
            ? minutes.ToString()
            : AutomaticLimitText(task);

    private string AutomaticLimitText(DiscoveredTask task)
    {
        var resolved = MonitoringService.ResolveThreshold(task, _config.Monitoring, null);
        return resolved is null ? "-" : $"auto ({(int)Math.Max(1, resolved.Value.TotalMinutes)})";
    }

    private void SetAllVisible(bool selected)
    {
        foreach (DataGridViewRow row in _jobGrid.Rows)
        {
            if (row.Tag is not DiscoveredTask task) continue;
            row.Cells[0].Value = selected;
            if (selected) _workingSelection.Add(task.TaskPath); else _workingSelection.Remove(task.TaskPath);
        }
        _jobMessage.Text = $"{_scannedTasks.Count} found • {_workingSelection.Count} selected";
    }

    private void SaveTaskSelection()
    {
        if (_jobServer.SelectedItem is not ServerConfig server) return;
        if (_scannedTasks.Count == 0)
        {
            MessageBox.Show("Scan the server before saving a selection.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return;
        }
        _config.MonitoredTasks.RemoveAll(task => task.ServerId == server.Id);
        foreach (var path in _workingSelection)
        {
            var found = _scannedTasks.FirstOrDefault(task => task.TaskPath.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (found is null) continue;
            _config.MonitoredTasks.Add(new MonitoredTaskConfig
            {
                ServerId = server.Id, TaskPath = path, DisplayName = found.DisplayName, Enabled = true,
                LongRunningMinutes = _workingLimits.TryGetValue(path, out var minutes) ? minutes : 0
            });
        }
        SaveConfig(); MessageBox.Show($"{_workingSelection.Count} task(s) selected for {server}.", Text,
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void LoadEmail()
    {
        var email = _config.Email;
        _emailEnabled.Checked = email.Enabled; _smtp.Text = email.SmtpServer;
        _port.Value = Math.Clamp(email.Port, 1, 65535); _tls.Checked = email.EnableTls;
        _username.Text = email.Username; _password.Text = DpapiProtector.Unprotect(email.EncryptedPassword);
        _sender.Text = email.Sender; _recipients.Lines = [.. email.Recipients];
    }

    private void SaveEmail(bool showMessage)
    {
        _config.Email.Enabled = _emailEnabled.Checked; _config.Email.SmtpServer = _smtp.Text.Trim();
        _config.Email.Port = (int)_port.Value; _config.Email.EnableTls = _tls.Checked;
        _config.Email.Username = _username.Text.Trim(); _config.Email.EncryptedPassword = DpapiProtector.Protect(_password.Text);
        _config.Email.Sender = _sender.Text.Trim();
        _config.Email.Recipients = _recipients.Lines.Select(line => line.Trim()).Where(line => line.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        SaveConfig();
        if (showMessage) MessageBox.Show("Email configuration saved.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task TestEmailAsync()
    {
        try
        {
            UseWaitCursor = true; SaveEmail(false);
            await new EmailService(_logger).SendTestAsync(_config.Email);
            MessageBox.Show("Test email sent successfully.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }

    private void LoadMonitoring()
    {
        _loadingMonitoring = true;
        _defaultLongRunning.Value = Math.Clamp(_config.Monitoring.LongRunningMinutes, 0, 10080);
        _useRepeatInterval.Checked = _config.Monitoring.UseRepeatIntervalAsLimit;
        _useEventLog.Checked = _config.Monitoring.UseEventLog;
        _eventIds.Text = string.Join(", ", _config.Monitoring.LongRunningEventIds);
        _eventLookback.Value = Math.Clamp(_config.Monitoring.EventLookbackMinutes, 5, 20160);
        _loadingMonitoring = false;
    }

    private void SaveMonitoring()
    {
        if (_loadingMonitoring) return;
        _config.Monitoring.LongRunningMinutes = (int)_defaultLongRunning.Value;
        _config.Monitoring.UseRepeatIntervalAsLimit = _useRepeatInterval.Checked;
        _config.Monitoring.UseEventLog = _useEventLog.Checked;
        _config.Monitoring.EventLookbackMinutes = (int)_eventLookback.Value;

        var ids = _eventIds.Text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value.Trim(), out var id) ? id : 0)
            .Where(id => id > 0).Distinct().ToList();
        if (ids.Count > 0) _config.Monitoring.LongRunningEventIds = ids;
        _eventIds.Text = string.Join(", ", _config.Monitoring.LongRunningEventIds);

        SaveConfig();
        RebuildTaskGrid();
    }

    private void LoadSchedule()
    {
        _scheduleEnabled.Checked = _config.Schedule.Enabled;
        if (TimeSpan.TryParse(_config.Schedule.RunTime, out var value)) _runTime.Value = DateTime.Today.Add(value);
    }

    private async Task RegisterScheduleAsync()
    {
        try
        {
            UseWaitCursor = true;
            _config.Schedule.Enabled = _scheduleEnabled.Checked = true;
            _config.Schedule.RunTime = _runTime.Value.ToString("HH:mm"); SaveConfig();
            await new ScheduleRegistrar().RegisterAsync(Application.ExecutablePath, _runTime.Value.TimeOfDay);
            await RefreshScheduleStatusAsync();
            MessageBox.Show("Daily Windows scheduled task registered.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }

    private async Task RemoveScheduleAsync()
    {
        try
        {
            UseWaitCursor = true; await new ScheduleRegistrar().RemoveAsync();
            _config.Schedule.Enabled = _scheduleEnabled.Checked = false; SaveConfig(); await RefreshScheduleStatusAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }

    private async Task RefreshScheduleStatusAsync()
    {
        var exists = await new ScheduleRegistrar().ExistsAsync();
        _scheduleStatus.Text = exists ? $"Registered daily at {_config.Schedule.RunTime}" : "Not registered";
        _scheduleStatus.ForeColor = exists ? Color.FromArgb(32, 114, 69) : Color.DimGray;
    }

    private ServerConfig? SelectedServerRow() => _serverGrid.CurrentRow?.Tag as ServerConfig;
    private void SaveConfig() => _store.Save(_config);

    private static TabPage NewPage(string name) => new(name) { BackColor = UiTheme.Page, Padding = new Padding(10) };
    private static FlowLayoutPanel ActionBar() => new() { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(5, 10, 5, 5) };
    private static void AddField(TableLayoutPanel panel, int row, string label, Control control, int height = 42)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        if (label.Length > 0) panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        control.Dock = control is TextBox { Multiline: true } ? DockStyle.Fill : DockStyle.Top;
        panel.Controls.Add(control, 1, row);
    }
}
