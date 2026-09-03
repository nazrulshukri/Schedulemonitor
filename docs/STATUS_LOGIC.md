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
time itself: `now - Last Run Time` while the Windows state is `Running`. The runtime budget it is
compared against is resolved in this order:

1. The per-task **Max Run (min)** value set on the Tasks tab of Configuration.
2. The repetition interval declared in Task Scheduler (`Repeat: Every`), when
   **Use the repeat interval** is enabled on the Schedule tab. A task scheduled every 5 minutes is
   expected to finish inside 5 minutes, so an execution still alive after that overlaps its own
   next start.
3. The global **Default max run (min)** value, 5 minutes out of the box.

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
