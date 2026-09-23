# pomotimer

A simple "pomodoro" timer with some logic that suits my needs.

* Work intervals are currently 25 minutes, followed by a break of at least 5 minutes.
* Unlocking or logging on resets the work timer after a sufficiently long break; shorter breaks keep the elapsed work time.
* The taskbar icon flashes shortly before expiry, then the workstation locks.
* Holding right Ctrl when the timer expires prevents the lock.
* The timer stays on top, including when all windows are minimized.
* Right-click for Restart or Quit.

## Requirements

Windows with the .NET 10 SDK (10.0.400 or a newer patch in that SDK feature band).
The SDK supplies the F# compiler and FSharp.Core; no separate F# installation is needed.
For VS Code, the Ionide-fsharp extension provides F# language support.

This remains a Windows-only WPF application, now using an SDK-style project,
F# 10, and .NET 10 LTS instead of .NET Framework and legacy XAML type providers.

## Build and Run

From the repository root:

```powershell
dotnet restore PomodoroTimer.sln --locked-mode
dotnet build PomodoroTimer.sln --configuration Release --no-restore
dotnet run --project PomodoroTimer/PomodoroTimer.fsproj --configuration Release --no-build
```

The built application is at `PomodoroTimer/bin/Release/net10.0-windows/PomodoroTimer.exe`.
Normal operation locks the workstation when the work interval expires.
The interval is currently configured by `workSlotInMinutes` in [App.fs](PomodoroTimer/App.fs).

## Verification

Run the bounded UI smoke test from an interactive Windows desktop:

```powershell
./scripts/Smoke-Test.ps1
```

It builds Release, briefly opens the timer, verifies resource loading,
timeline labels, Restart/Quit, and nonblank rendering, then exits.
Workstation locking and jump-list changes are disabled in this mode.
A screenshot and logs are written alongside the executable. The script terminates
the test if it exceeds 20 seconds. Actual session lock/unlock still requires manual
verification.

## Publish

To produce a Windows x64 folder that does not require an installed .NET runtime:

```powershell
dotnet publish PomodoroTimer/PomodoroTimer.fsproj --configuration Release --runtime win-x64 --self-contained true --output artifacts/win-x64
```

Run `artifacts/win-x64/PomodoroTimer.exe`. Distribute the entire output folder.

## Dependencies

NuGet restores dependencies automatically. The old `packages/` directory is no
longer needed. FSharpx, FSharp.ViewModule, and FsXaml are no longer referenced;
WPF's built-in XAML loader loads the embedded views. FSharp.Core is supplied by the
SDK. Resolved versions are recorded in [packages.lock.json](PomodoroTimer/packages.lock.json).

The legacy [App.config](PomodoroTimer/App.config), [packages.config](PomodoroTimer/packages.config),
and [Application.xaml](PomodoroTimer/Application.xaml) remain on disk for reference,
but are excluded from the modern project. Runtime configuration is generated
by the SDK.

After intentionally changing SDK or package versions, run `dotnet restore` and
review the lock-file changes. Use `dotnet restore --locked-mode` for reproducible restores.
