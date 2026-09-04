# Status logic

The V1 classifier intentionally stays small and operational.

Evaluation order:

0. The Events column is filled from the same event log for every task, independently of the status.

1. An abnormal event logged for the current execution produces `ABNORMAL`.

1. A failed server query produces `UNREACHABLE` for every selected task on that server.
2. A selected task absent from a successful scan produces `FAILED` with `Task not found`.
3. A disabled task produces `DISABLED`.
4. A Windows state containing `Running` produces `LONG RUNNING` when Windows logged an overlap
   event for this execution (or, when the optional timing rule is on, when the execution passed its
   budget), otherwise `RUNNING`.
5. A Windows state containing `Queued` produces `PENDING`.
6. A next-run time older than five minutes produces `OVERDUE`.
7. A task without a last-run time produces `PENDING` with `Task has not run yet`.
8. Last result `0` or `0x0` produces `SUCCESS`.
9. Any other last result produces `FAILED`, retaining the original code.

## Long running

Task Scheduler reports only that a task is running, never for how long, so V1 measures the elapsed
time itself, and it also asks Windows directly.

### Source 1: the Task Scheduler event log (strongest)

`Microsoft-Windows-TaskScheduler/Operational` is read with `wevtutil.exe` for the configured event
IDs, by default:

| Event | Meaning |
|---|---|
| 322 | Launch request ignored, instance already running |
| 324 | Launch request queued, instance already running |

Windows logs these when a scheduled start is skipped because the previous execution is still going,
which is exactly the mismatch between a 5-minute schedule and an executable that needs longer. Such
an event marks the task `LONG RUNNING` while the task is still running and the event belongs to the
current execution, that is, it was logged after the current last-run time.

The event log read is best effort. A server that denies it (Remote Event Log Management blocked, no
permission) is logged as a warning and the task still falls back to the elapsed-time rule below.

### Source 2: elapsed time (optional, off by default)

Being slower than expected is not by itself an overlap, so timing alone does not raise
`LONG RUNNING`. A long run with no event stays `RUNNING`, and `SUCCESS` once it ends.

Enabling **Also flag LONG RUNNING from elapsed time** in Configuration → Schedule adds the timing
rule: `now - Last Run Time` while the Windows state is `Running`, compared with a budget resolved in
this order:

1. The per-task **Max Run (min)** value set on the Tasks tab of Configuration.
2. The repetition interval declared in Task Scheduler (`Repeat: Every`), when
   **Use the repeat interval** is enabled on the Schedule tab. A task scheduled every 5 minutes is
   expected to finish inside 5 minutes, so an execution still alive after that overlaps its own
   next start.
3. The global **Default max run (min)** value, 5 minutes out of the box.

`LONG RUNNING` is a live state, not a memory. When the execution ends and Task Scheduler returns to
`Ready` with result `0`, the task reports `SUCCESS` again. What happened stays visible in the
**Events** column, which shows the last Task Scheduler event for every task — `322` for an overrun,
`102` for a normal completion — with the time it was logged.

## Abnormal

The same event-log mechanism carries a second status. Any event in the configured abnormal list
(`101` task start failed, `103` action start failed, `203` action failed to start, `102` task
completed, by default) that Windows logged for the current execution produces `ABNORMAL`, which is
evaluated after the running states and before the last-result rules. `102` is Windows' normal
completion event, so keeping it in the list marks every finished task abnormal; remove it in
Configuration → Schedule to report only failures, and empty the list to turn the status off.

`ABNORMAL` counts as a problem and raises its own email alert with its own subject and body.

A `LONG RUNNING` result also raises an immediate email alert, using the templates in
Configuration → Alerts and the repeat window kept in `alertstate.json`.

`LONG RUNNING` counts as a problem: it appears under Attention Required in the email report, it is
included in the Problems card, and headless runs exit with code 2 when it appears.

The order matters. For example, a currently running task is `RUNNING` even if its last completed execution failed.

## Data source

V1 calls:

```text
schtasks.exe /Query /S <hostname> /FO CSV /V
```

It parses the standard English CSV fields for task name, status, enabled state, last run, last result, and next run. The query runs under the current Windows identity.

## Known boundary

Task Scheduler does not know the business progress of the process it launches. V1 does not invent a percentage. Application-level progress can be added later only for jobs that expose a reliable log, API, file, or database status.
