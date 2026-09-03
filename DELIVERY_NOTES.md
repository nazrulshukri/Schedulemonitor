# Delivery notes

## Included

- Complete Visual Studio 2022 solution and C# source.
- Portable self-contained `win-x64` single-file publish configuration.
- One-click Windows publish scripts (`.bat` and `.ps1`).
- Simplified main monitoring screen.
- Four configuration areas: Servers, Tasks, Email, and Schedule.
- Remote scheduled-task scan and administrator selection workflow.
- JSON configuration, DPAPI SMTP password protection, HTML report, SMTP sending, local logs, and daily schedule registration.
- Read-only monitoring of remote tasks.

## Validation completed in the delivery environment

- All JSON and XML files parsed successfully.
- All 19 C# source files passed lexical bracket/structure checks.
- Required feature markers and command boundaries were checked.
- The archive contents and checksum were verified after packaging.

## Build limitation

The delivery environment is Linux and does not contain the .NET SDK or Windows Desktop targeting pack. Therefore, no Windows executable is claimed as compiled or runtime-tested here. Run `publish-win-x64.bat` on a Windows PC with Visual Studio 2022/.NET 8 SDK. The publish command builds first and will stop if the compiler finds an issue.

After publishing, validate in this order:

1. Add `localhost` or the local computer name.
2. Scan and select one harmless scheduled task.
3. Run a manual check.
4. Test one remote server using the intended Windows execution account.
5. Send a test email.
6. Register the daily task and confirm its next run time in Windows Task Scheduler.
