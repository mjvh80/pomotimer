module SmokeTest

open System
open System.Diagnostics
open System.IO
open System.Windows
open System.Windows.Controls
open System.Windows.Controls.Primitives
open System.Windows.Media
open System.Windows.Media.Animation
open System.Windows.Media.Imaging
open System.Windows.Threading

let private check condition message =
    if not condition then failwith message

let checkKeyboardLocks (window: Window) (breakTimer: Stopwatch) isUpdating sendKeyboardSignal =
    check (not (WorkInterval.shouldResetAfterBreak (TimeSpan.FromSeconds(299.)))) "Short break reset the work interval."
    check (WorkInterval.shouldResetAfterBreak (TimeSpan.FromMinutes(5.))) "Five-minute break did not reset the work interval."
    check (WorkInterval.shouldResetAfterBreak (TimeSpan.FromMinutes(10.))) "Long break did not reset the work interval."
    let tracker = LockState.Tracker()
    check (tracker.Set(LockState.Keyboard, false) = LockState.Unchanged) "Unmatched unlock changed lock state."
    check (tracker.Set(LockState.Keyboard, true) = LockState.BreakStarted) "Keyboard lock did not begin a break."
    check (tracker.Set(LockState.Keyboard, true) = LockState.Unchanged) "Repeated lock restarted a break."
    check (tracker.Set(LockState.Session, true) = LockState.Unchanged) "Overlapping lock restarted a break."
    check (tracker.Set(LockState.Keyboard, false) = LockState.Unchanged && tracker.IsLocked) "Keyboard unlock ignored the Windows lock."
    check (tracker.Set(LockState.Session, false) = LockState.BreakEnded && not tracker.IsLocked) "Final unlock did not end the break."
    tracker.Set(LockState.Keyboard, true) |> ignore
    tracker.Set(LockState.Session, true) |> ignore
    check (tracker.Set(LockState.Session, false) = LockState.BreakEnded && not tracker.IsLocked) "Windows unlock did not clear a missed keyboard unlock."
    check (tracker.Set(LockState.Keyboard, false) = LockState.Unchanged) "Late keyboard unlock ended the break twice."
    check (tracker.Set(LockState.Session, false) = LockState.Unchanged) "Repeated Windows unlock ended the break twice."
    tracker.Set(LockState.Keyboard, true) |> ignore
    check (tracker.Set(LockState.Session, false) = LockState.BreakEnded && not tracker.IsLocked) "Windows unlock retained a keyboard lock when the session lock event was missed."
    sendKeyboardSignal true
    check (not (isUpdating()) && breakTimer.IsRunning) "F24 did not stop updates and time the break."
    breakTimer.Stop()
    let beforeRepeat = breakTimer.Elapsed
    sendKeyboardSignal true
    check (not breakTimer.IsRunning && breakTimer.Elapsed = beforeRepeat) "Repeated F24 reset the break timer."
    (window.FindName("RestartMenuItem") :?> MenuItem).RaiseEvent(RoutedEventArgs(MenuItem.ClickEvent))
    check (not (isUpdating())) "Restart resumed updates while keyboard was locked."
    sendKeyboardSignal false
    check (isUpdating()) "F22 did not resume updates."
    sendKeyboardSignal false
    check (isUpdating()) "Repeated F22 interrupted work."

let private checkWorkDeadline() =
    let elapsed = TimeSpan.FromMinutes(30.)
    check (WorkInterval.deadlineAfterDurationChange 40 elapsed = TimeSpan.FromMinutes(40.))
        "Extending the duration did not preserve the elapsed-time deadline."
    check (WorkInterval.deadlineAfterDurationChange 31 elapsed = TimeSpan.FromMinutes(31.))
        "An unexpired duration received an unnecessary grace period."
    let graceDeadline = elapsed + TimeSpan.FromSeconds(10.)
    check (WorkInterval.deadlineAfterDurationChange 25 elapsed = graceDeadline)
        "An overdue duration did not receive a fresh ten-second warning."
    check (WorkInterval.deadlineAfterDurationChange 30 elapsed = graceDeadline)
        "Saving at the deadline did not receive a fresh ten-second warning."
    check (WorkInterval.deadlineAfterDurationChange 40 graceDeadline = TimeSpan.FromMinutes(40.))
        "Extending the duration retained the old grace deadline."

let private checkSettingsStorage() =
    let directory = Path.Combine(Path.GetTempPath(), "PomodoroTimer-settings-test-" + Guid.NewGuid().ToString("N"))
    let path = Path.Combine(directory, "settings.json")
    try
        check (Settings.load path = Settings.defaults) "Missing settings did not use defaults."
        for invalid in [""; "0"; "241"; "1.5"; "text"] do
            check (Settings.tryParseDuration invalid = None) "Invalid duration was accepted."
        check (Settings.tryParseDuration "1" = Some 1) "Minimum duration was rejected."
        check (Settings.tryParseDuration "240" = Some 240) "Maximum duration was rejected."
        let custom = { Settings.defaults with WorkDurationMinutes = 25; TimerScalePercent = 75 }
        check (Settings.save path custom = Ok ()) "Settings could not be saved."
        check (Settings.load path = custom) "Saved settings did not round-trip."
        check (Settings.save path { custom with WorkDurationMinutes = 0 } |> Result.isError) "Invalid settings were saved."
        check (Settings.save path { custom with TimerScalePercent = 49 } |> Result.isError) "Invalid scale was saved."
        check (Settings.save path { custom with TimerScalePercent = 201 } |> Result.isError) "Oversized scale was saved."
        for invalid in [""; "0"; "61"; "1.5"; "text"] do
            check (Settings.tryParseSnooze invalid = None) "Invalid snooze interval was accepted."
        check (Settings.tryParseSnooze "1" = Some 1 && Settings.tryParseSnooze "60" = Some 60) "Snooze limits were rejected."
        check (Settings.save path { custom with SnoozeMinutes = 0 } |> Result.isError) "Invalid snooze interval was saved."
        check (Settings.save path { custom with SnoozeMinutes = 61 } |> Result.isError) "Excessive snooze interval was saved."
        check (Settings.load path = custom) "Rejected settings changed the saved preferences."
        File.WriteAllText(path, "{\"WorkDurationMinutes\":25}")
        check (Settings.load path = { custom with TimerScalePercent = 100 }) "Legacy settings did not default to 100 percent."
        File.WriteAllText(path, "{\"WorkDurationMinutes\":25,\"TimerScalePercent\":0}")
        check ((Settings.load path).TimerScalePercent = 100) "Invalid scale did not fall back to 100 percent."
        check ((Settings.load path).SnoozeMinutes = 5) "Legacy settings did not default to five-minute snooze."
        check (Settings.save path { custom with SnoozeMinutes = 7 } = Ok ()) "Custom snooze could not be saved."
        check ((Settings.load path).SnoozeMinutes = 7) "Custom snooze did not round-trip."
        File.WriteAllText(path, "not json")
        check (Settings.load path = Settings.defaults) "Corrupt settings did not fall back to defaults."
    finally
        if Directory.Exists(directory) then Directory.Delete(directory, true)

let private captureWindow (window: Window) fileName =
    window.UpdateLayout()
    let bitmap = RenderTargetBitmap(int window.ActualWidth, int window.ActualHeight, 96., 96., PixelFormats.Pbgra32)
    bitmap.Render(window)
    let stride = bitmap.PixelWidth * 4
    let pixels = Array.zeroCreate<byte> (stride * bitmap.PixelHeight)
    bitmap.CopyPixels(pixels, stride, 0)
    check (pixels |> Array.exists ((<>) 0uy)) "Window rendered blank."
    let encoder = PngBitmapEncoder()
    encoder.Frames.Add(BitmapFrame.Create(bitmap))
    use output = File.Create(Path.Combine(AppContext.BaseDirectory, fileName))
    encoder.Save(output)

let checkPauseIndicator (window: Window) refresh sendKeyboardSignal setSessionLocked isUpdating isWorkUpdating =
    let indicator = window.FindName("PauseIndicator") :?> FrameworkElement
    check (indicator.Visibility = Visibility.Collapsed && not (isUpdating())) "Pause indicator was active during work."
    sendKeyboardSignal true
    check (indicator.IsVisible && indicator.HasAnimatedProperties && isUpdating()) "F24 did not start the pause pulse."
    sendKeyboardSignal true
    check indicator.HasAnimatedProperties "Repeated F24 stopped the pause pulse."
    refresh (TimeSpan.FromSeconds(299.))
    check indicator.HasAnimatedProperties "Pause stopped pulsing before a qualifying break."
    refresh (TimeSpan.FromMinutes(5.))
    check (indicator.IsVisible && not indicator.HasAnimatedProperties && indicator.Opacity = 1.) "Qualifying break did not show a solid pause symbol."
    captureWindow window "pause-ready-smoke-test.png"
    let content = window.Content :?> FrameworkElement
    let originalTransform, originalWidth, originalHeight = content.LayoutTransform, window.Width, window.Height
    try
        for percent in [50; 200] do
            let factor = float percent / 100.
            content.LayoutTransform <- ScaleTransform(factor, factor)
            window.Width <- 180. * factor
            window.Height <- 180. * factor
            captureWindow window $"pause-scale-{percent}.png"
    finally
        content.LayoutTransform <- originalTransform
        window.Width <- originalWidth
        window.Height <- originalHeight
    refresh (TimeSpan.FromMinutes(10.))
    check (not indicator.HasAnimatedProperties) "Long break restarted the pulse."
    sendKeyboardSignal false
    check (indicator.Visibility = Visibility.Collapsed && not indicator.HasAnimatedProperties && not (isUpdating())) "F22 did not clear the pause indicator."
    sendKeyboardSignal true
    check indicator.HasAnimatedProperties "A new short break did not pulse."
    setSessionLocked true
    sendKeyboardSignal false
    check indicator.IsVisible "Keyboard unlock hid an ongoing Windows break."
    setSessionLocked false
    check (indicator.Visibility = Visibility.Collapsed && not (isUpdating())) "Final unlock left the pause indicator active."
    setSessionLocked true
    check (indicator.IsVisible && indicator.HasAnimatedProperties) "Windows lock did not show the pause indicator."
    sendKeyboardSignal true
    setSessionLocked false
    check (indicator.Visibility = Visibility.Collapsed && not (isUpdating()) && isWorkUpdating()) "Windows unlock did not clear the stale keyboard pause and resume work."
    refresh (TimeSpan.FromMinutes(10.))
    check (indicator.Visibility = Visibility.Collapsed && not (isUpdating())) "A later indicator update restored the stale pause."
    sendKeyboardSignal false
    check (indicator.Visibility = Visibility.Collapsed && not (isUpdating())) "Keyboard unlock left the pause indicator active."
    check (isWorkUpdating()) "Late F22 interrupted resumed work."
    sendKeyboardSignal true
    setSessionLocked true
    refresh (TimeSpan.FromMinutes(5.))
    setSessionLocked false
    check (indicator.Visibility = Visibility.Collapsed && not indicator.HasAnimatedProperties && isWorkUpdating()) "Windows unlock left the solid pause visible after a missed F22."

let checkDoubleClickResume (window: Window) (workTimer: Stopwatch) setKeyboardLocked setSessionLocked isUpdating =
    let indicator = window.FindName("PauseIndicator") :?> FrameworkElement
    let doubleClick button =
        let args = Input.MouseButtonEventArgs(Input.Mouse.PrimaryDevice, Environment.TickCount, button)
        args.RoutedEvent <- Control.PreviewMouseDoubleClickEvent
        window.RaiseEvent(args)
    setKeyboardLocked true
    doubleClick Input.MouseButton.Right
    check (indicator.IsVisible && not (isUpdating())) "Right double-click resumed work."
    let elapsed = workTimer.Elapsed
    doubleClick Input.MouseButton.Left
    check (indicator.Visibility = Visibility.Collapsed && isUpdating()) "Left double-click did not resume work and hide pause."
    check (workTimer.Elapsed >= elapsed) "Double-click reset a short break."
    let resumedElapsed = workTimer.Elapsed
    doubleClick Input.MouseButton.Left
    check (isUpdating() && workTimer.Elapsed >= resumedElapsed) "Double-click reset an already-running timer."
    setSessionLocked true
    setKeyboardLocked true
    doubleClick Input.MouseButton.Left
    check (indicator.IsVisible && not (isUpdating())) "Double-click bypassed a Windows lock."
    setSessionLocked false
    check (indicator.Visibility = Visibility.Collapsed && isUpdating()) "Windows unlock did not resume after double-click."

let private checkWorkLimitMarker (window: Window) expectedMinutes =
    let firstPart = window.FindName("firstTimelinePart") :?> Control
    let panel = firstPart.Parent :?> StackPanel
    let markers =
        panel.Children |> Seq.cast<UIElement> |> Seq.choose (function :? Control as part -> Some part | _ -> None)
        |> Seq.mapi (fun index part ->
            part.ApplyTemplate() |> ignore
            index, part.Template.FindName("WorkLimitMarker", part) :?> FrameworkElement)
        |> Seq.filter (fun (_, marker) -> marker.Visibility = Visibility.Visible)
        |> Seq.toArray
    check (markers.Length = 1) "Timeline must show exactly one work limit marker."
    let index, marker = markers[0]
    check (float (index * 50) + Canvas.GetLeft(marker) + 1. = float (expectedMinutes * 5)) "Work limit marker was at the wrong minute."

let checkPreviewArguments() =
    check (PausePreview.parse [||] = Ok None) "Normal startup enabled preview."
    check (PausePreview.parse [|"/settings"; "123"|] = Ok None) "Preview parsing intercepted taskbar commands."
    check (PausePreview.parse [|"--preview-pause"|] = Ok (Some { PausePreview.WorkMinutes = 20; BreakSeconds = 0 })) "Preview defaults were incorrect."
    check (PausePreview.parse [|"--preview-pause"; "--break-seconds"; "295"; "--work-minutes"; "25"|] = Ok (Some { PausePreview.WorkMinutes = 25; BreakSeconds = 295 })) "Preview options were not parsed."
    for arguments in [
        [|"--work-minutes"; "20"|]
        [|"--preview-pause"; "--break-seconds"|]
        [|"--preview-pause"; "--break-seconds"; "-1"|]
        [|"--preview-pause"; "--break-seconds"; "86401"|]
        [|"--preview-pause"; "--work-minutes"; "241"|]
        [|"--preview-pause"; "--work-minutes"; "text"|]
        [|"--preview-pause"; "--work-minutes"; "1.5"|]
        [|"--preview-pause"; "--unknown"|]
        [|"--preview-pause"; "--preview-pause"|]
        [|"--preview-pause"; "--work-minutes"; "1"; "--work-minutes"; "2"|]
    ] do
        check (PausePreview.parse arguments |> Result.isError) "Invalid preview arguments were accepted."

let checkPausePreview (window: Window) (options: PausePreview.Options) isUpdating =
    checkWorkLimitMarker window Settings.defaults.WorkDurationMinutes
    let indicator = window.FindName("PauseIndicator") :?> FrameworkElement
    check (indicator.IsVisible && not (isUpdating())) "Preview did not start paused."
    check (indicator.HasAnimatedProperties = (options.BreakSeconds < 300)) "Preview showed the wrong initial pause state."
    for name in ["RestartMenuItem"; "SettingsMenuItem"; "SnoozeMenuItem"] do
        check (not (window.FindName(name) :?> MenuItem).IsEnabled) "Preview enabled a timer command."
    let timer = DispatcherTimer(DispatcherPriority.ApplicationIdle, window.Dispatcher)
    timer.Interval <- TimeSpan.FromSeconds(2.)
    timer.Tick.Add(fun _ ->
        timer.Stop()
        try
            check (indicator.HasAnimatedProperties = (options.BreakSeconds + 2 < 300)) "Preview did not advance the pause state."
            check (not (isUpdating())) "Preview resumed normal updates."
            let scroller = window.FindName("TimelineScroller") :?> ScrollViewer
            let firstPart = window.FindName("firstTimelinePart") :?> Control
            let panel = firstPart.Parent :?> StackPanel
            let rulerMinutes = (panel.Children.Count - 1) * 10
            let expectedOffset = min scroller.ScrollableWidth ((scroller.ScrollableWidth + 75.) * float options.WorkMinutes / float rulerMinutes)
            check (abs (scroller.HorizontalOffset - expectedOffset) < 1.) "Preview lost the requested work position."
            captureWindow window $"pause-preview-{options.BreakSeconds}.png"
            (window.FindName("QuitMenuItem") :?> MenuItem).RaiseEvent(RoutedEventArgs(MenuItem.ClickEvent))
        with error ->
            Console.Error.WriteLine(error)
            Application.Current.Shutdown(1))
    timer.Start()

let private checkSettingsDialog (window: Window) (workTimer: Stopwatch) settingsPath getDuration openRemoteSettings =
    let settingsMenu = window.FindName("SettingsMenuItem") :?> MenuItem
    let click (control: FrameworkElement) = control.RaiseEvent(RoutedEventArgs(ButtonBase.ClickEvent))
    let interact openDialog action =
        let mutable failure: exn option = None
        let operation = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, Action(fun () ->
            let dialog = window.OwnedWindows |> Seq.cast<Window> |> Seq.find (fun owned -> owned.Name = "SettingsWindow")
            try action dialog
            with error ->
                failure <- Some error
                dialog.Close()))
        openDialog()
        if operation.Status = DispatcherOperationStatus.Pending then
            operation.Abort() |> ignore
            failwith "Settings dialog did not open."
        failure |> Option.iter raise
    let openFromMenu() = settingsMenu.RaiseEvent(RoutedEventArgs(MenuItem.ClickEvent))

    check (getDuration() = 40) "Smoke test did not use isolated default settings."
    checkWorkLimitMarker window 40
    interact openFromMenu (fun dialog ->
        check (not workTimer.IsRunning) "Work time was not paused while editing settings."
        let input = dialog.FindName("DurationInput") :?> TextBox
        check (input.Text = "40") "Settings did not show the current duration."
        check ((dialog.FindName("SnoozeInput") :?> TextBox).Text = "5") "Default snooze was not five minutes."
        input.Text <- "0"
        click (dialog.FindName("SaveSettingsButton") :?> Button)
        check dialog.IsVisible "Invalid settings closed the dialog."
        check ((dialog.FindName("ValidationError") :?> TextBlock).Visibility = Visibility.Visible) "Validation feedback was not shown."
        input.Text <- "25"
        (dialog.FindName("SnoozeInput") :?> TextBox).Text <- "9"
        (dialog.FindName("TimerScaleSlider") :?> Slider).Value <- 50.
        click (dialog.FindName("CancelSettingsButton") :?> Button))
    check (getDuration() = 40 && not (File.Exists(settingsPath))) "Cancel changed settings."
    check (window.Width = 180.) "Cancel changed timer size."
    check workTimer.IsRunning "Cancel did not resume the work timer."

    interact openFromMenu (fun dialog ->
        let input = dialog.FindName("DurationInput") :?> TextBox
        input.Text <- "120"
        click (dialog.FindName("DecreaseDurationButton") :?> RepeatButton)
        check (input.Text = "119") "Decrease duration did not work."
        click (dialog.FindName("IncreaseDurationButton") :?> RepeatButton)
        check (input.Text = "120") "Increase duration did not work."
        let snoozeInput = dialog.FindName("SnoozeInput") :?> TextBox
        check (snoozeInput.Text = "5") "Cancel changed the snooze preference."
        for invalid in ["0"; "61"; "1.5"] do
            snoozeInput.Text <- invalid
            click (dialog.FindName("SaveSettingsButton") :?> Button)
            check dialog.IsVisible "Invalid snooze closed the dialog."
        snoozeInput.Text <- "7"
        click (dialog.FindName("DecreaseSnoozeButton") :?> RepeatButton)
        check (snoozeInput.Text = "6") "Snooze decrement did not work."
        click (dialog.FindName("IncreaseSnoozeButton") :?> RepeatButton)
        check (snoozeInput.Text = "7") "Snooze increment did not work."
        let elapsedBeforeSave = workTimer.Elapsed
        click (dialog.FindName("SaveSettingsButton") :?> Button)
        check (workTimer.Elapsed = elapsedBeforeSave) "Save reset elapsed work time."
        check (not workTimer.IsRunning) "Save resumed work before the dialog closed.")
    check (getDuration() = 120 && (Settings.load settingsPath).WorkDurationMinutes = 120) "Save did not apply and persist the new duration."
    check workTimer.IsRunning "Save did not resume the timer."
    check ((Settings.load settingsPath).SnoozeMinutes = 7) "Dialog did not persist the custom snooze interval."
    let firstPart = window.FindName("firstTimelinePart") :?> Control
    let countParts() =
        LogicalTreeHelper.GetChildren(firstPart.Parent)
        |> Seq.cast<obj>
        |> Seq.filter (fun item -> item :? Control)
        |> Seq.length
    check (countParts() >= 12) "Timeline did not expand for a longer duration."
    checkWorkLimitMarker window 120

    interact openRemoteSettings (fun dialog ->
        let input = dialog.FindName("DurationInput") :?> TextBox
        check (input.Text = "120") "Taskbar settings command did not open the saved duration."
        check ((dialog.FindName("SnoozeInput") :?> TextBox).Text = "7") "Dialog did not reload snooze."
        captureWindow dialog "settings-smoke-test.png"
        use lockedFile = File.Open(settingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
        input.Text <- "30"
        (dialog.FindName("SnoozeInput") :?> TextBox).Text <- "10"
        (dialog.FindName("TimerScaleSlider") :?> Slider).Value <- 50.
        click (dialog.FindName("SaveSettingsButton") :?> Button)
        check dialog.IsVisible "Failed save closed the dialog."
        check (getDuration() = 120) "Failed save changed the active duration."
        check (window.Width = 180.) "Failed save changed timer size."
        click (dialog.FindName("CancelSettingsButton") :?> Button))
    check ((Settings.load settingsPath).WorkDurationMinutes = 120) "Failed save damaged persisted settings."
    check ((Settings.load settingsPath).SnoozeMinutes = 7) "Failed save changed the persisted snooze interval."

    interact openFromMenu (fun dialog ->
        (dialog.FindName("DurationInput") :?> TextBox).Text <- "40"
        let elapsedBeforeSave = workTimer.Elapsed
        click (dialog.FindName("SaveSettingsButton") :?> Button)
        check (workTimer.Elapsed = elapsedBeforeSave) "Shortening the duration reset elapsed work time.")
    check (getDuration() = 40 && (Settings.load settingsPath).WorkDurationMinutes = 40) "Shorter duration was not applied."
    check (countParts() = 8) "Timeline did not shrink after saving a shorter duration."
    checkWorkLimitMarker window 40

    for percent in [50; 75; 200; 100] do
        interact openFromMenu (fun dialog ->
            let elapsedBeforeSave = workTimer.Elapsed
            (dialog.FindName("TimerScaleSlider") :?> Slider).Value <- float percent
            click (dialog.FindName("SaveSettingsButton") :?> Button)
            check (workTimer.Elapsed = elapsedBeforeSave) "Scaling reset elapsed work time.")
        check ((Settings.load settingsPath).TimerScalePercent = percent) "Timer scale was not persisted."
        check (getDuration() = 40) "Scale-only save changed work duration."
        let factor = float percent / 100.
        check (window.Width = 180. * factor && window.Height = 180. * factor) "Window dimensions did not scale."
        let transform = (window.Content :?> FrameworkElement).LayoutTransform :?> ScaleTransform
        check (transform.ScaleX = factor && transform.ScaleY = factor) "Timer contents did not scale uniformly."
        captureWindow window $"timer-scale-{percent}.png"
    interact openFromMenu (fun dialog ->
        check ((dialog.FindName("TimerScaleSlider") :?> Slider).Value = 100.) "Dialog did not reload saved scale."
        click (dialog.FindName("CancelSettingsButton") :?> Button))

let private checkDisplayChanges createCountdown =
    let firstScreen = Rect(0., 0., 640., 480.)
    let secondScreen = Rect(640., 0., 640., 480.)
    let mutable bounds = [| firstScreen; secondScreen |]
    let mutable elapsed = TimeSpan.Zero
    let mutable createdCount = 0
    let mutable closedCount = 0
    let createWindow() =
        let countdown: Window = createCountdown()
        createdCount <- createdCount + 1
        countdown.Closed.Add(fun _ -> closedCount <- closedCount + 1)
        countdown
    use manager = new CountdownWindows.Manager(createWindow, (fun () -> bounds), (fun () -> elapsed))
    manager.Show()
    check (manager.Windows.Length = 2) "Two displays did not receive two countdowns."
    let originalWindows = manager.Windows
    elapsed <- TimeSpan.FromSeconds(4.)
    bounds <- [| Rect(0., 0., 800., 600.) |]
    manager.Refresh()
    check (originalWindows |> Array.forall (fun countdown -> not countdown.IsVisible)) "Removed-display countdowns remained visible."
    check (closedCount = 2) "Old countdown windows were hidden rather than closed."
    check (manager.Windows.Length = 1) "Removing a display left duplicate countdowns."
    let remaining = manager.Windows[0]
    let expectedLeft = (800. - remaining.Width) / 2.
    let expectedTop = (600. - remaining.Height) / 2.
    check (abs (remaining.Left - expectedLeft) <= 1. && abs (remaining.Top - expectedTop) <= 1.)
        $"Countdown position ({remaining.Left}, {remaining.Top}) did not match ({expectedLeft}, {expectedTop}) within pixel rounding."
    check ((remaining.FindName("TimesUpTimerText") :?> TextBlock).Text = "5")
        "Display change restarted the countdown animation."
    manager.Refresh()
    check (createdCount - closedCount = 1) "Repeated display notifications accumulated windows."
    bounds <- [| firstScreen; secondScreen; secondScreen |]
    manager.Refresh()
    check (manager.Windows.Length = 2 && createdCount - closedCount = 2) "Reconnect or duplicate bounds produced the wrong window count."
    manager.Hide()
    bounds <- [| secondScreen |]
    manager.Refresh()
    check (manager.Windows.Length = 0 && createdCount = closedCount) "An idle display change opened countdowns."
    manager.Show()
    check (manager.Windows.Length = 1) "A new warning reused stale display information."
    check ((manager.Windows[0].FindName("TimesUpTimerText") :?> TextBlock).Text = "9") "New warning did not restart its animation."
    bounds <- [||]
    manager.Refresh()
    check (createdCount = closedCount) "An empty display list retained countdowns."
    bounds <- [| firstScreen |]
    manager.Refresh()
    (manager :> IDisposable).Dispose()
    manager.Refresh()
    check (manager.Windows.Length = 0 && createdCount = closedCount) "Shutdown or a queued refresh leaked countdown windows."

let checkSnooze (window: Window) (workTimer: Stopwatch) snoozeMinutes getDeadline prepareDeadline isUpdating setKeyboardLocked startNotification (getCountdowns: unit -> Window array) =
    let menu = window.FindName("SnoozeMenuItem") :?> MenuItem
    let click() = menu.RaiseEvent(RoutedEventArgs(MenuItem.ClickEvent))
    let originalDeadline = getDeadline()
    click()
    check (getDeadline() = originalDeadline) "Snooze changed an interval before its warning."
    workTimer.Stop()
    prepareDeadline (workTimer.Elapsed + TimeSpan.FromSeconds(5.))
    startNotification()
    let warningWindows = getCountdowns()
    check menu.IsEnabled "Snooze was unavailable during the warning."
    let beforeSnooze = workTimer.Elapsed
    click()
    let expectedDeadline = beforeSnooze + TimeSpan.FromMinutes(float snoozeMinutes)
    check (getDeadline() = expectedDeadline) "Snooze did not use the configured interval from now."
    check (workTimer.Elapsed >= beforeSnooze && workTimer.IsRunning) "Snooze reset elapsed time."
    check (isUpdating()) "Snooze did not resume updates."
    check (warningWindows |> Array.forall (fun countdown -> not countdown.IsVisible)) "Snooze left warning windows visible."
    check (not menu.IsEnabled) "Snooze remained enabled outside its warning period."
    click()
    check (getDeadline() = expectedDeadline) "Repeated click extended Snooze outside its warning period."

    workTimer.Stop()
    prepareDeadline (workTimer.Elapsed - TimeSpan.FromSeconds(1.))
    check menu.IsEnabled "Snooze was unavailable after expiry."
    let expiredElapsed = workTimer.Elapsed
    click()
    check (getDeadline() = expiredElapsed + TimeSpan.FromMinutes(float snoozeMinutes)) "Expired timer was not snoozed from now."
    check (isUpdating()) "Expired timer did not resume after Snooze."

    setKeyboardLocked true
    prepareDeadline (workTimer.Elapsed - TimeSpan.FromSeconds(1.))
    let lockedDeadline = getDeadline()
    check (not menu.IsEnabled) "Snooze was enabled while locked."
    click()
    check (getDeadline() = lockedDeadline && not (isUpdating())) "Snooze bypassed the keyboard lock."
    setKeyboardLocked false
    (window.FindName("RestartMenuItem") :?> MenuItem).RaiseEvent(RoutedEventArgs(MenuItem.ClickEvent))

let run (window: Window) (workTimer: Stopwatch) (getCountdowns: unit -> Window array) startNotification stopNotification settingsPath getDuration openRemoteSettings createCountdown verifySnooze =
    checkWorkDeadline()
    checkSettingsStorage()
    checkDisplayChanges createCountdown
    check (window.IsVisible && window.ActualWidth > 0.) "Main window did not open."
    check (window.Icon <> null) "Taskbar icon was not rendered."

    let restart = window.FindName("RestartMenuItem") :?> MenuItem
    let quit = window.FindName("QuitMenuItem") :?> MenuItem
    for menuItem in [restart; quit] do
        let image = menuItem.Icon :?> Image
        check (image.Source <> null && image.Source.Width > 0.) "Menu icon did not load."

    let firstPart = window.FindName("firstTimelinePart") :?> Control
    let firstMinute = firstPart.Template.FindName("firstMinute", firstPart) :?> Label
    check (string firstMinute.Content = "0") "Timeline labels were not initialized."
    checkSettingsDialog window workTimer settingsPath getDuration openRemoteSettings

    startNotification()
    let countdowns = getCountdowns()
    check (countdowns.Length > 0) "No display was detected."
    for countdown in countdowns do
        check countdown.IsVisible "Countdown window did not open."
        let text = countdown.FindName("TimesUpTimerText") :?> TextBlock
        let animation = text.FindResource("Animation") :?> Storyboard
        animation.SeekAlignedToLastTick(countdown, TimeSpan.FromSeconds(1.), TimeSeekOrigin.BeginTime)
        check (text.Text = "8") "Countdown animation did not advance."
    stopNotification()
    check (countdowns |> Array.forall (fun countdown -> not countdown.IsVisible)) "Countdown did not hide."

    startNotification()
    let restartedCountdowns = getCountdowns()
    workTimer.Stop()
    let elapsedBeforeRestart = workTimer.Elapsed
    restart.RaiseEvent(RoutedEventArgs(MenuItem.ClickEvent))
    check workTimer.IsRunning "Restart did not start the work timer."
    check (workTimer.Elapsed < elapsedBeforeRestart) "Restart did not reset elapsed work time."
    check (restartedCountdowns |> Array.forall (fun countdown -> not countdown.IsVisible)) "Restart did not clear the notification."

    verifySnooze()
    captureWindow window "smoke-test.png"

    quit.RaiseEvent(RoutedEventArgs(MenuItem.ClickEvent))