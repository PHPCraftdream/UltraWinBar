# Publishes UltraWinBar (Release, ReadyToRun like the released builds, so it starts fast) and replaces the installed
# copy with it, then starts it again. Run through reinstall.cmd from cmd, or directly with PowerShell.
# Nothing is touched if the publish fails.
#
#   reinstall.cmd                 publish, stop the running copy, back up, copy, start
#   reinstall.cmd -NoBuild        reuse the last publish
#   reinstall.cmd -NoReadyToRun   plain IL build (no crossgen packages needed; slower to start)
#   reinstall.cmd -NoStart        leave the new copy stopped
#   reinstall.cmd -DryRun         only print what would happen
#   reinstall.cmd -InstallDir D:\apps\UltraWinBar
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\UltraWinBar'),
    [string]$Framework = 'net6.0-windows',
    [switch]$NoBuild,
    [switch]$NoReadyToRun,
    [switch]$NoStart,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$project = Join-Path $repoRoot 'UltraWinBar\UltraWinBar.csproj'
$buildDir = Join-Path $repoRoot "UltraWinBar\Support\BuildTools\artifacts\publish\$Framework"
$installedExe = Join-Path $InstallDir 'UltraWinBar.exe'
$backupRoot = Join-Path $env:LOCALAPPDATA 'UltraWinBar\DeploymentBackups'

function Step($text) { Write-Host "==> $text" }
function Run($text, [scriptblock]$action) { Step $text; if (-not $DryRun) { & $action } }

if (-not $NoBuild) {
    Step "Publishing $Framework (Release$(if (-not $NoReadyToRun) { ', ReadyToRun' }))"
    if (-not $DryRun) {
        if (Test-Path $buildDir) { Remove-Item -Path $buildDir -Recurse -Force }
        $publishArguments = @('publish', $project, '-c', 'Release', '-f', $Framework, '-r', 'win-x64', '--self-contained', 'false',
            '-o', $buildDir, '--nologo', '-v', 'quiet')
        if (-not $NoReadyToRun) { $publishArguments += '-p:PublishReadyToRun=true' }
        & dotnet @publishArguments
        if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit code $LASTEXITCODE); the installed copy was not touched." }
    }
}
if (-not (Test-Path (Join-Path $buildDir 'UltraWinBar.exe'))) { throw "No build output in $buildDir" }

# Only the processes started from the install directory: the panel itself and its work-area watchdog child.
$running = @(Get-CimInstance Win32_Process -Filter "Name='UltraWinBar.exe'" |
    Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $installedExe) })
$main = @($running | Where-Object { $_.CommandLine -notmatch '--restore-work-area' })
$watchdogs = @($running | Where-Object { $_.CommandLine -match '--restore-work-area' })

foreach ($process in $main) {
    Run "Stopping UltraWinBar (PID $($process.ProcessId))" { Stop-Process -Id $process.ProcessId -Force }
}
# The watchdog restores the work area when the panel dies, then exits on its own.
foreach ($process in $watchdogs) {
    Run "Waiting for the work-area watchdog (PID $($process.ProcessId))" {
        Wait-Process -Id $process.ProcessId -Timeout 20 -ErrorAction SilentlyContinue
        if (Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue) {
            throw "The work-area watchdog (PID $($process.ProcessId)) is still running; stop it and retry."
        }
    }
}

if (Test-Path $installedExe) {
    $backup = Join-Path $backupRoot ("{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), [guid]::NewGuid().ToString('N'))
    Run "Backing up $InstallDir to $backup" {
        New-Item -ItemType Directory -Path $backup -Force | Out-Null
        Copy-Item -Path (Join-Path $InstallDir '*') -Destination $backup -Recurse -Force
    }
}

Run "Copying $buildDir to $InstallDir" {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    Copy-Item -Path (Join-Path $buildDir '*') -Destination $InstallDir -Recurse -Force
}

if (-not $DryRun) {
    $installed = (Get-Item (Join-Path $InstallDir 'UltraWinBar.dll')).VersionInfo
    Step "Installed version $($installed.ProductVersion)"
}

if (-not $NoStart) {
    Run "Starting $installedExe" { Start-Process -FilePath $installedExe | Out-Null }
}
Step $(if ($DryRun) { 'Dry run finished; nothing was changed.' } else { 'Done.' })
