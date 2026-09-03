$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $projectRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 8 SDK was not found. Install Visual Studio 2022 with .NET desktop development or the .NET 8 SDK.'
}

dotnet publish 'src\SchedulerMonitor\SchedulerMonitor.csproj' `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o 'dist\SchedulerMonitor-win-x64'

Write-Host "Portable build created in: $projectRoot\dist\SchedulerMonitor-win-x64" -ForegroundColor Green
