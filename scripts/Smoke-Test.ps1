param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../PomodoroTimer/PomodoroTimer.fsproj'
dotnet build $project --configuration $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$outputDirectory = Join-Path $PSScriptRoot "../PomodoroTimer/bin/$Configuration/net10.0-windows"
$executable = Join-Path $outputDirectory 'PomodoroTimer.exe'
$stdout = Join-Path $outputDirectory 'smoke-test.stdout.log'
$stderr = Join-Path $outputDirectory 'smoke-test.stderr.log'
$process = Start-Process $executable -ArgumentList '--smoke-test' -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
try {
    if (-not $process.WaitForExit(20000)) {
        throw 'Smoke test did not finish within 20 seconds.'
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0) {
        Get-Content $stderr
        throw "Smoke test failed with exit code $($process.ExitCode)."
    }
    Write-Output 'PASS: startup, resources, rendering, timeline, countdown, configurable Snooze, display changes, keyboard lock/unlock, pause indicator, Restart/Quit, and settings validation, Save/Cancel, persistence, and taskbar command handler.'
    Write-Output "Screenshot: $(Join-Path $outputDirectory 'smoke-test.png')"
    Write-Output "Settings screenshot: $(Join-Path $outputDirectory 'settings-smoke-test.png')"
}
finally {
    if (-not $process.HasExited) { $process.Kill() }
    $process.Dispose()
}

foreach ($breakSeconds in @(0, 299, 300)) {
    $previewError = Join-Path $outputDirectory "preview-$breakSeconds.stderr.log"
    $previewOutput = Join-Path $outputDirectory "preview-$breakSeconds.stdout.log"
    $process = Start-Process $executable -ArgumentList "--smoke-test --preview-pause --work-minutes 38 --break-seconds $breakSeconds" -PassThru -RedirectStandardOutput $previewOutput -RedirectStandardError $previewError
    try {
        if (-not $process.WaitForExit(20000)) { throw 'Pause preview test did not finish within 20 seconds.' }
        $process.Refresh()
        if ($process.ExitCode -ne 0) {
            Get-Content $previewError
            throw "Pause preview test failed with exit code $($process.ExitCode)."
        }
    }
    finally {
        if (-not $process.HasExited) { $process.Kill() }
        $process.Dispose()
    }
}
Write-Output 'PASS: pause preview startup, work position, pulsing, automatic transition to solid, and Quit.'