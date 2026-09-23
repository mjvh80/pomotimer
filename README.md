# pomotimer

A simple "pomodoro" timer with some logic that suits my needs.

* Work intervals default to 40 minutes and are configurable, followed by a break of at least 5 minutes.
* Unlocking or logging on resets the work timer after a sufficiently long break; shorter breaks keep the elapsed work time.
* A countdown appears on each display shortly before expiry, then the workstation locks.
* Holding right Ctrl when the timer expires prevents the lock.
* The timer stays on top, including when all windows are minimized.
* Right-click for Snooze, Settings, Restart, or Quit.

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

## Keyboard Lock

Unmodified **F24** starts a break and hides countdown warnings without locking
Windows. **F22** ends the keyboard break. These are global hotkeys, so the tomato
does not need focus. A break of at least five minutes resets the work interval;
shorter breaks retain the existing elapsed-work accounting, just like Windows
lock/unlock. Repeated lock signals do not restart the break clock. F22 does not
resume work while Windows remains locked. Unlocking or logging into Windows
resumes work and clears any stale keyboard-lock state, since F22 may have been
sent while the Windows lock screen prevented the app from receiving it.

A pause symbol on the tomato pulses slowly during a short break. After five
minutes it stays solid, indicating that returning will reset the work timer.
It disappears when all lock sources are cleared. No notification or sound is used.
Left-double-click the tomato to resume a keyboard break manually, using the same
five-minute reset rule as F22. This does not reset an already-running timer or
bypass a Windows lock. Single-click dragging is unchanged; preview stays paused.

The app reports a warning if another application or timer instance has already
reserved either hotkey. Hotkeys are released when the app exits. The keyboard must
send an unlock signal when Windows stays unlocked; its physical lock state cannot
otherwise be queried.

## Settings

Right-click the timer and choose **Settings**, or choose **Settings** from its
taskbar jump list. Work duration accepts whole minutes from 1 to 240 and defaults
to 40. Use the numeric field or its minus/plus buttons to change it.

**Snooze (minutes)** defaults to 5 and accepts whole minutes from 1 to 60. During
the warning or after expiry, right-click the tomato and choose **Snooze** to clear
the countdown and set a new deadline that many minutes from now. Elapsed work time
is preserved, and the warning appears again before the new deadline. Snooze is
unavailable before the warning or while the keyboard or Windows is locked. It
cannot unlock Windows; unlock first if the workstation has already locked.
Changing the snooze setting affects the next Snooze command, not an active deadline.

The stripes are an elapsed-minute ruler, not a duration-sized progress bar. The
original ruler covers 80 minutes, so stripes beyond 25 or 40 minutes are expected.
Longer intervals extend it; expiry still uses the configured duration independently.
A gold tick marks the configured work limit and scrolls with the ruler; the white
triangle remains the current-position indicator. The gold tick updates when the
work duration changes, but stays at the configured limit when you snooze.
A pink tick marks the active snooze endpoint. Further snoozes move the pink tick;
Restart, a qualifying break, or a work-duration change clears it. The ruler extends
as needed to include the snooze endpoint.

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
40 minutes, 100% size, and a five-minute snooze. Older files retain their saved
preferences and use defaults for missing fields. Save failures are shown in the
dialog without changing the timer.

## Window Position

The tomato remembers its position after dragging and on normal exit. Placement is
saved separately in `%LOCALAPPDATA%\PomodoroTimer\window-position.json`, using the
Windows display device name and an offset within that display's usable area.
Relaunching restores that position, accounting for the saved timer size.

If the monitor has moved, the position follows it. If it is unavailable, the timer
uses a display overlapping its previous position, or the primary display, and
clamps the position inside the usable area. Windows may rename displays after
docking or driver changes, so the device name is not a permanent hardware ID.
Missing or corrupt placement files leave the default startup position unchanged.
Preview and smoke-test launches do not read or overwrite your saved position.

## Pause Preview

Preview the pause symbol without keyboard firmware or waiting five minutes:

```powershell
dotnet run --project PomodoroTimer --configuration Release -- --preview-pause --work-minutes 20 --break-seconds 295
```

This shows 20 minutes on the timeline with a break already 4:55 old. The pause
symbol pulses, then becomes solid about five seconds later. Use `--break-seconds 0`
for a new break or `--break-seconds 300` to start solid. Work minutes accept 0..240
(default 20); break seconds accept 0..86400 (default 0).

Preview is display-only: work time stays fixed while break time advances. It reads
your saved appearance settings but cannot change them, lock Windows, register
global hotkeys, or modify the taskbar jump list. Right-click **Quit** to close it;
other commands are disabled. An already-running normal timer is unaffected and
retains its normal locking behavior.

## Verification

Run the bounded UI smoke test from an interactive Windows desktop:

```powershell
./scripts/Smoke-Test.ps1
```

It builds Release and exercises the timer, countdowns, and settings dialog. Checks
cover resource loading, timeline resizing, countdown animation, Restart/Quit,
snoozing warnings and expired timers with a custom interval, snooze lock guards,
settings validation, Save/Cancel, elapsed-time preservation, overdue-save deadlines,
persistence, failed-save recovery, legacy settings migration, and tomato rendering
at 50%, 75%, 100%, and 200%. Simulated display disconnects, reconnects,
rearrangements, and repeated change notifications verify countdown cleanup and
animation continuity without changing the actual monitor configuration. The
settings command handler used by the taskbar jump list is also exercised.
Keyboard tests send messages only to the test window and cover duplicate signals,
overlapping lock sources, the five-minute break boundary, and pulsing/solid pause
indicator states (including rendering at 50% and 200%). They neither inject
keys into other applications nor reserve the global hotkeys.
Separate preview launches check the requested work position, initial pause states,
and the automatic transition from pulsing to solid across the five-minute boundary.
Tests use temporary settings files and leave your saved preferences untouched.
Placement tests cover saved-position round trips, monitor rearrangement/removal,
on-screen clamping, invalid files, and failed-save recovery using simulated monitors.
Workstation locking and jump-list changes are disabled in this mode.
Screenshots and logs are written alongside the executable. The script terminates
the test if it exceeds 20 seconds. Actual session lock/unlock, physical docking, mixed-DPI display
placement, hardware F24/F22 delivery, and the Windows taskbar jump-list appearance
still require manual verification.

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
