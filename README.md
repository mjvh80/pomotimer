# pomotimer

A simple "pomodoro" timer with some logic that suits my needs.

* Work intervals default to 40 minutes and are configurable, followed by a break of at least 5 minutes.
* Unlocking or logging on resets the work timer after a sufficiently long break; shorter breaks keep the elapsed work time.
* A countdown appears on each display shortly before expiry, then the workstation locks.
* Holding right Ctrl when the timer expires prevents the lock.
* The timer stays on top, including when all windows are minimized.
* Right-click for Settings, Restart, or Quit. The unfinished Snooze command is disabled.

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

## Settings

Right-click the timer and choose **Settings**, or choose **Settings** from its
taskbar jump list. Work duration accepts whole minutes from 1 to 240 and defaults
to 40. Use the numeric field or its minus/plus buttons to change it.

**Timer size** scales the tomato window and its timeline from 50% to 200% in
5% steps, with 100% as the default. Countdown windows, menus, and the settings
dialog remain at their normal size. The size is applied when you save and restored
on startup. Changing only the size does not change the work deadline.

The timer pauses while the dialog is open. **Save** saves your preferences and updates
the current deadline without resetting elapsed work time. If the new duration has
already elapsed, a fresh ten-second warning is given before locking. Otherwise,
the timer continues toward the updated deadline. **Cancel** resumes without changing
your preferences. Use **Restart** explicitly when you want a fresh work interval.
The five-minute minimum break is unchanged.

Settings are saved per Windows user at `%LOCALAPPDATA%\PomodoroTimer\settings.json`
and loaded on startup. Missing, invalid, or unreadable settings fall back to
40 minutes and 100% size. Older duration-only files keep their saved duration and
use 100% size. Save failures are shown in the dialog without changing the timer.

## Verification

Run the bounded UI smoke test from an interactive Windows desktop:

```powershell
./scripts/Smoke-Test.ps1
```

It builds Release and exercises the timer, countdowns, and settings dialog. Checks
cover resource loading, timeline resizing, countdown animation, Restart/Quit,
settings validation, Save/Cancel, elapsed-time preservation, overdue-save deadlines,
persistence, failed-save recovery, legacy settings migration, and tomato rendering
at 50%, 75%, 100%, and 200%. Simulated display disconnects, reconnects,
rearrangements, and repeated change notifications verify countdown cleanup and
animation continuity without changing the actual monitor configuration. The
settings command handler used by the taskbar jump list is also exercised.
Tests use temporary settings files and leave your saved preferences untouched.
Workstation locking and jump-list changes are disabled in this mode.
Screenshots and logs are written alongside the executable. The script terminates
the test if it exceeds 20 seconds. Actual session lock/unlock, physical docking, mixed-DPI display
placement, and the Windows taskbar jump-list appearance still require manual verification.

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
SDK, and WpfScreenHelper 2.1.1 provides display information. Resolved versions are
recorded in [packages.lock.json](PomodoroTimer/packages.lock.json).

The legacy [App.config](PomodoroTimer/App.config), [packages.config](PomodoroTimer/packages.config),
and [Application.xaml](PomodoroTimer/Application.xaml) remain on disk for reference,
but are excluded from the modern project. Runtime configuration is generated
by the SDK.

After intentionally changing SDK or package versions, run `dotnet restore` and
review the lock-file changes. Use `dotnet restore --locked-mode` for reproducible restores.
