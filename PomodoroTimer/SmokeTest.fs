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
        check (Settings.load path = custom) "Rejected settings changed the saved preferences."
        File.WriteAllText(path, "{\"WorkDurationMinutes\":25}")
        check (Settings.load path = { custom with TimerScalePercent = 100 }) "Legacy settings did not default to 100 percent."
        File.WriteAllText(path, "{\"WorkDurationMinutes\":25,\"TimerScalePercent\":0}")
        check ((Settings.load path).TimerScalePercent = 100) "Invalid scale did not fall back to 100 percent."
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
    interact openFromMenu (fun dialog ->
        check (not workTimer.IsRunning) "Work time was not paused while editing settings."
        let input = dialog.FindName("DurationInput") :?> TextBox
        check (input.Text = "40") "Settings did not show the current duration."
        input.Text <- "0"
        click (dialog.FindName("SaveSettingsButton") :?> Button)
        check dialog.IsVisible "Invalid settings closed the dialog."
        check ((dialog.FindName("ValidationError") :?> TextBlock).Visibility = Visibility.Visible) "Validation feedback was not shown."
        input.Text <- "25"
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
        let elapsedBeforeSave = workTimer.Elapsed
        click (dialog.FindName("SaveSettingsButton") :?> Button)
        check (workTimer.Elapsed = elapsedBeforeSave) "Save reset elapsed work time."
        check (not workTimer.IsRunning) "Save resumed work before the dialog closed.")
    check (getDuration() = 120 && (Settings.load settingsPath).WorkDurationMinutes = 120) "Save did not apply and persist the new duration."
    check workTimer.IsRunning "Save did not resume the timer."
    let firstPart = window.FindName("firstTimelinePart") :?> Control
    let countParts() =
        LogicalTreeHelper.GetChildren(firstPart.Parent)
        |> Seq.cast<obj>
        |> Seq.filter (fun item -> item :? Control)
        |> Seq.length
    check (countParts() >= 12) "Timeline did not expand for a longer duration."

    interact openRemoteSettings (fun dialog ->
        let input = dialog.FindName("DurationInput") :?> TextBox
        check (input.Text = "120") "Taskbar settings command did not open the saved duration."
        captureWindow dialog "settings-smoke-test.png"
        use lockedFile = File.Open(settingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
        input.Text <- "30"
        (dialog.FindName("TimerScaleSlider") :?> Slider).Value <- 50.
        click (dialog.FindName("SaveSettingsButton") :?> Button)
        check dialog.IsVisible "Failed save closed the dialog."
        check (getDuration() = 120) "Failed save changed the active duration."
        check (window.Width = 180.) "Failed save changed timer size."
        click (dialog.FindName("CancelSettingsButton") :?> Button))
    check ((Settings.load settingsPath).WorkDurationMinutes = 120) "Failed save damaged persisted settings."

    interact openFromMenu (fun dialog ->
        (dialog.FindName("DurationInput") :?> TextBox).Text <- "40"
        let elapsedBeforeSave = workTimer.Elapsed
        click (dialog.FindName("SaveSettingsButton") :?> Button)
        check (workTimer.Elapsed = elapsedBeforeSave) "Shortening the duration reset elapsed work time.")
    check (getDuration() = 40 && (Settings.load settingsPath).WorkDurationMinutes = 40) "Shorter duration was not applied."
    check (countParts() = 8) "Timeline did not shrink after saving a shorter duration."

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

let run (window: Window) (workTimer: Stopwatch) (getCountdowns: unit -> Window array) startNotification stopNotification settingsPath getDuration openRemoteSettings createCountdown =
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

    captureWindow window "smoke-test.png"

    quit.RaiseEvent(RoutedEventArgs(MenuItem.ClickEvent))