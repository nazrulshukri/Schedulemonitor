# Scheduler Monitor

A small, portable Windows utility for monitoring selected Task Scheduler jobs across servers and workstations.

The administrator scans a server, selects only the important scheduled tasks, and the tool reports their useful execution status instead of merely repeating Windows' `Ready` state.

## Version 1 scope

- Portable `.NET 8` WinForms application; no installer, database, IIS, or Windows service.
- Add, edit, remove, enable, and test servers.
- Scan the full Task Scheduler library on a selected server.
- Search the scan result and select only tasks that should appear in monitoring and email.
- Preserve previous selections during rescans; new tasks are unselected by default.
- Report a selected task as `FAILED – Task not found` if it was deleted or renamed.
- Display `SUCCESS`, `RUNNING`, `LONG RUNNING`, `PENDING`, `FAILED`, `OVERDUE`, `DISABLED`, and `UNREACHABLE`.
- Retain Windows state, last run, last result code, and next run for troubleshooting.
- Generate a compact HTML report and optionally send it through SMTP.
- Encrypt the SMTP password with Windows DPAPI for the current Windows user.
- Register or remove a daily `SchedulerMonitor-Daily` Windows scheduled task.
- Write local text logs and HTML reports.

## Important status behavior

Windows `Ready` is only a current scheduler state. Scheduler Monitor combines current state with the last execution information:

| Windows information | Monitor result |
|---|---|
| Running, still inside its runtime budget | RUNNING |
| Running, past its runtime budget | LONG RUNNING |
| Queued | PENDING |
| Disabled | DISABLED |
| Ready + last result `0` or `0x0` | SUCCESS |
| Ready + non-zero or unavailable result | FAILED |
| Configured task no longer found | FAILED – Task not found |
| Server query fails | UNREACHABLE |

`OVERDUE` is used only when a returned next-run time is already more than five minutes in the past. Task Scheduler normally advances its next-run time, so this is intentionally conservative in V1.

Task Scheduler does not normally expose percentage progress. A running task is shown as `RUNNING` with its available start/last-run time; application-specific progress would require the job's own log, API, file, or database.

### Long running

Task Scheduler reports that a task is running but never for how long, so the monitor measures
`now - Last Run Time` itself and compares it with a runtime budget:

**Source 1 — the Task Scheduler event log.** `Microsoft-Windows-TaskScheduler/Operational` is read
with `wevtutil.exe` for event **322** (*launch request ignored, instance already running*) and
**324** (*launch request queued, instance already running*). Windows logs these when a scheduled
start is skipped because the previous run is still going — the exact mismatch between a 5-minute
schedule and a 3-minute-plus executable. Any such event inside the lookback window flags the task,
and the event ID and time are shown in the task details and the email report. The event IDs, the
lookback window and the whole check are configurable in **Configuration → Schedule**; a server that
denies the event log is logged and falls back to the timing rule below.

**Source 2 — elapsed time**, compared with a budget:

1. The per-task **Max Run (min)** value in **Configuration → Tasks**.
2. Otherwise the task's own repetition interval (`Repeat: Every`), while
   **Use the repeat interval** is enabled in **Configuration → Schedule**. A task that repeats every
   5 minutes is expected to finish inside 5 minutes, so an execution still alive after that window
   overlaps its own next start.
3. Otherwise the global **Default max run (min)**, 5 minutes by default.

`LONG RUNNING` is treated as a problem: it is highlighted in the grid, counted in the Problems card,
listed under Attention Required in the email report, and returns exit code 2 in silent runs. The
**Running For** column shows the measured elapsed time.

## Requirements

- Windows 10/11 or Windows Server 2016 or later.
- Visual Studio 2022 with the .NET desktop development workload, or .NET 8 SDK, to build.
- The Windows account running the tool must have permission to query Task Scheduler on each remote machine.
- Remote Task Scheduler/RPC access and applicable Windows Firewall rules must be enabled.
- For the event-based long running check, the **Remote Event Log Management** firewall rules must be
  enabled on the monitored servers and the account must be able to read the Task Scheduler
  operational log. Without it, monitoring still works using elapsed time only.
- English Windows Task Scheduler command output. V1 parses the standard English `schtasks.exe /Query /FO CSV /V` headers.

No server username or password is stored. Remote queries use the Windows identity that runs the EXE or its registered scheduled task.

## Build a portable EXE

On a Windows development PC:

1. Open `SchedulerMonitor.sln` in Visual Studio 2022 and build, or run `publish-win-x64.bat`.
2. Find the portable output under `dist\SchedulerMonitor-win-x64`.
3. Copy that whole folder to the admin server.
4. Run `SchedulerMonitor.exe`.

The publish is self-contained and single-file, so the destination server does not need a separate .NET runtime installation.

## First-time setup

1. Open **Configuration → Servers** and add a hostname or IP.
2. Use **Test Connection**.
3. Open **Tasks**, select the server, and choose **Scan Tasks**.
4. Check only the jobs that should be monitored, then choose **Save Selection**.
5. Configure and test SMTP if an email report is required.
6. In **Schedule**, select the time and choose **Register Daily Task**.
7. Return to the main screen and choose **Run Check**.

## Command modes

```text
SchedulerMonitor.exe
```

Opens the UI.

```text
SchedulerMonitor.exe --run --silent
```

Loads `config.json`, checks selected tasks, creates the HTML report, sends email when enabled, writes the log, and exits.

Exit codes for unattended mode:

- `0`: run completed and no task was failed/unreachable.
- `1`: the monitor itself could not complete.
- `2`: run completed but at least one task was failed or unreachable.

## Portable folder at runtime

```text
SchedulerMonitor\
├── SchedulerMonitor.exe
├── config.json
├── Logs\
└── Reports\
```

The application creates the JSON, log folder, and report folder on first use. Keep the EXE in a writable IT-tools folder such as `C:\Tools\SchedulerMonitor`.

## Security and operational notes

- SMTP passwords are encrypted with Windows DPAPI and can only be decrypted under the Windows user profile that saved them. Configure email while signed in as the same account that will run the daily task.
- Do not move the EXE after registering the daily task. If it is moved, remove and register the schedule again.
- The registered task uses the Windows account performing registration. Verify its **Run whether user is logged on or not** setting and credentials in Task Scheduler if unattended execution while logged off is required by local policy.
- The tool is read-only toward remote scheduled jobs. It does not start, stop, create, delete, or modify monitored jobs.
- Logs may include server names, task names, result codes, and connection error messages. Store the folder according to company access rules.

## Project structure

```text
src/SchedulerMonitor
├── Infrastructure   JSON, DPAPI, logging, CSV parsing
├── Models           Configuration and monitoring results
├── Services         Task query, status logic, email, reports, scheduling
└── UI               Main screen and four-tab configuration
```
