@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 8 SDK was not found. Install Visual Studio 2022 with .NET desktop development or the .NET 8 SDK.
  exit /b 1
)

dotnet publish "src\SchedulerMonitor\SchedulerMonitor.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "dist\SchedulerMonitor-win-x64"
if errorlevel 1 exit /b 1

echo.
echo Portable build created in:
echo %CD%\dist\SchedulerMonitor-win-x64
endlocal
