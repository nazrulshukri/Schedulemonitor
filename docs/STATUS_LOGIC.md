# Status logic

The V1 classifier intentionally stays small and operational.

Evaluation order:

1. A failed server query produces `UNREACHABLE` for every selected task on that server.
2. A selected task absent from a successful scan produces `FAILED` with `Task not found`.
3. A disabled task produces `DISABLED`.
4. A Windows state containing `Running` produces `LONG RUNNING` when the execution has already
   passed its runtime budget, otherwise `RUNNING`.
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
which is exactly the mismatch between a 5-minute schedule and an executable that needs longer. Any
such event inside the lookback window (12 hours by default) marks the task `LONG RUNNING`, and the
event ID and its time are kept in the task details and the report.

The event log read is best effort. A server that denies it (Remote Event Log Management blocked, no
permission) is logged as a warning and the task still falls back to the elapsed-time rule below.

### Source 2: elapsed time

`now - Last Run Time` while the Windows state is `Running`. The runtime budget it is compared
against is resolved in this order:

1. The per-task **Max Run (min)** value set on the Tasks tab of Configuration.
2. The repetition interval declared in Task Scheduler (`Repeat: Every`), when
   **Use the repeat interval** is enabled on the Schedule tab. A task scheduled every 5 minutes is
   expected to finish inside 5 minutes, so an execution still alive after that overlaps its own
   next start.
3. The global **Default max run (min)** value, 5 minutes out of the box.

Evaluation puts the event log first: if Windows itself reported the overlap, the task is
`LONG RUNNING` even when the current execution has already finished.

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
