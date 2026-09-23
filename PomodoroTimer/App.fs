module App

// Ignore unverifiable IL warning.
#nowarn "9"

open System
open System.Net
open System.Threading
open System.Windows
open System.Windows.Input
open System.Windows.Interop
open System.Windows.Controls
open System.Windows.Media
open System.Windows.Media.Animation
open System.Windows.Shell
open Microsoft.Win32
open System.Windows.Markup
open System.Runtime.InteropServices
open System.Diagnostics

// Interop
[<DllImport("user32.dll", SetLastError = true)>]
extern bool LockWorkStation();

[<DllImport("user32.dll", SetLastError = true)>]
extern nativeint SendMessage(nativeint hWnd, int Msg, nativeint wParam, nativeint lParam)

[<DllImport("user32.dll", SetLastError = true)>]
extern bool PostMessage(nativeint windowHandle, int message, nativeint wParam, nativeint lParam)

[<DllImport("user32.dll", SetLastError = true)>]
extern bool RegisterHotKey(nativeint windowHandle, int identifier, uint32 modifiers, uint32 virtualKey)

[<DllImport("user32.dll", SetLastError = true)>]
extern bool UnregisterHotKey(nativeint windowHandle, int identifier)

[<StructLayout(LayoutKind.Sequential)>]
type FLASHWINFO =
   struct
      val cbSize: UInt32
      val hwnd: nativeint
      val dwFlags: UInt32
      val uCount: UInt32
      val dwTimeout: UInt32

      new(hwnd, dwFlags, uCount, dwTimeout) = { cbSize = Convert.ToUInt32(Marshal.SizeOf(typeof<FLASHWINFO>)); hwnd = hwnd; dwFlags = dwFlags; uCount = uCount; dwTimeout = dwTimeout }
   end

[<DllImport("user32.dll", SetLastError = true)>]
extern [<return: MarshalAs(UnmanagedType.Bool)>] bool FlashWindowEx(FLASHWINFO& pInfo) 

// End Interop

let loadWindow resourceName =
   let uri = Uri($"pack://application:,,,/PomodoroTimer;component/{resourceName}")
   use stream = Application.GetResourceStream(uri).Stream
   XamlReader.Load(stream, ParserContext(BaseUri = uri)) :?> Window

type WindowsMsg = 
   | Restart = 0x0401
   | Quit = 0x0402
   | Snooze = 0x0403
   | Settings = 0x0404

type Window with
   member this.Handle = WindowInteropHelper(this).Handle

// Info about break and work. WorkTimer times work done, BreakTimer times the current break.
type BreakInfo = { WorkTimer: Stopwatch; BreakTimer: Stopwatch } with
   static member FromDispatcherTimer(timer: System.Windows.Threading.DispatcherTimer) = timer.Tag :?> BreakInfo

// Config
let isSmokeTest = Environment.GetCommandLineArgs() |> Array.contains "--smoke-test"
let previewResult = Environment.GetCommandLineArgs() |> Array.skip 1 |> PausePreview.parse
let pausePreview = previewResult |> Result.toOption |> Option.flatten
let isPreview = pausePreview.IsSome
let settingsPath =
   if isSmokeTest then
      System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PomodoroTimer-smoke-" + Guid.NewGuid().ToString("N"), "settings.json")
   else
      Settings.filePath
let mutable preferences = Settings.load settingsPath
let mutable workSlotInMinutes = preferences.WorkDurationMinutes


// Construct application etc.
let application = Application(ShutdownMode = ShutdownMode.OnMainWindowClose)
let window = loadWindow "MainWindow.xaml"
application.MainWindow <- window
let baseWindowWidth, baseWindowHeight = window.Width, window.Height
let applyTimerScale percent =
   let factor = float percent / 100.
   (window.Content :?> FrameworkElement).LayoutTransform <- ScaleTransform(factor, factor)
   window.Width <- baseWindowWidth * factor
   window.Height <- baseWindowHeight * factor

applyTimerScale preferences.TimerScalePercent
let placementPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(settingsPath), "window-position.json")
let getPlacementMonitors() : WindowPlacement.Monitor array =
   WpfScreenHelper.Screen.AllScreens
   |> Seq.map (fun screen -> ({ DeviceName = screen.DeviceName; WorkingArea = screen.WorkingArea; IsPrimary = screen.Primary }: WindowPlacement.Monitor))
   |> Seq.toArray
let mutable placementReady = false
let saveWindowPlacement() =
   if placementReady && not isSmokeTest && not isPreview && window.WindowState = WindowState.Normal then
      WindowPlacement.capture (getPlacementMonitors()) (Rect(window.Left, window.Top, window.Width, window.Height))
      |> Option.iter (fun placement ->
         match WindowPlacement.save placementPath placement with
         | Ok () -> ()
         | Error message -> Trace.TraceWarning($"Could not save window position: {message}"))
window.SourceInitialized.Add(fun _ ->
   if not isSmokeTest && not isPreview then
      WindowPlacement.load placementPath
      |> Option.bind (WindowPlacement.restore (getPlacementMonitors()) (Size(window.Width, window.Height)))
      |> Option.iter (fun position ->
         window.WindowStartupLocation <- WindowStartupLocation.Manual
         window.Left <- position.X
         window.Top <- position.Y))
let scroller = window.FindName("TimelineScroller") :?> System.Windows.Controls.ScrollViewer

let icon = loadWindow "Icon.xaml"
icon.ShowInTaskbar <- false
icon.ShowActivated <- false
icon.Left <- -10000. // offscreen
icon.Show() // required for rendering to image source to work

// Forward taskbar commands to the relevant instance before quitting.
application.Startup.Add(fun (args: StartupEventArgs) ->
   match args.Args with
   | [| command; target |] when command = "/restart" || command = "/settings" ->
      match Int64.TryParse(target) with
      | true, handle when handle <> 0L ->
         let message = if command = "/settings" then WindowsMsg.Settings else WindowsMsg.Restart
         let posted = PostMessage(nativeint handle, int message, IntPtr.Zero, IntPtr.Zero)
         application.Shutdown(if posted then 0 else 1)
      | _ -> application.Shutdown(1)
   | _ -> ())


// Allow window to be moved.
let mutable dragCoords = new Windows.Point()

let getTimelineParts (window: Window) = 
   let firstPart = window.FindName("firstTimelinePart") :?> Control
   LogicalTreeHelper.GetChildren(firstPart.Parent) 
      |> Seq.cast<FrameworkElement>
      |> Seq.skipWhile (fun element -> not(Object.ReferenceEquals(element, firstPart)))
      |> Seq.cast<Control>

// WPF timer to update our timer UI on the message loop.
let dispatcherTimer = new System.Windows.Threading.DispatcherTimer()
dispatcherTimer.Tag <- box({ WorkTimer = Stopwatch.StartNew(); BreakTimer = new Stopwatch() })

// Protect against locking out the user in case of bugs, don't lock if control is down.
let doActualWorkStationLock() =
   if not isSmokeTest && not isPreview && not(Keyboard.IsKeyDown(Key.RightCtrl)) then
      LockWorkStation() |> ignore // todo handle somehow?

// Sets the taskbar icon by rendering the Icon.xaml file to it.
let updateWindowIcon(minutes: int) = 
   let rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(icon.Width |> int, icon.Height |> int, 96., 96., PixelFormats.Pbgra32);
   let minutesLabel = icon.FindName("minutes") :?> Label
   minutesLabel.Content <- minutes.ToString()
   minutesLabel.FontSize <- if minutes >= 100 then 80. else 125.
   icon.UpdateLayout()
   rtb.Render(icon)
   window.Icon <- rtb
   ()

let countdownWindows =
   new CountdownWindows.Manager(
     (fun () -> loadWindow "TimesUpTimer.xaml"),
     (fun () -> WpfScreenHelper.Screen.AllScreens |> Seq.map (fun screen -> screen.Bounds) |> Seq.toArray),
     (fun () -> Stopwatch.GetElapsedTime(0L)))

let showTimesUpTimers() = countdownWindows.Show()

let hideTimesUpTimers() = countdownWindows.Hide()

let mutable showingBreakApproachingNotification = false
let snoozeMenuItem = window.FindName("SnoozeMenuItem") :?> MenuItem

let startBreakApproachingNotification() =
    if not showingBreakApproachingNotification then
         showingBreakApproachingNotification <- true
         snoozeMenuItem.IsEnabled <- true
         let mutable info = new FLASHWINFO(window.Handle, (* flash task tray *) 2u, 50u, 200u)
         FlashWindowEx(&info) |> ignore
         showTimesUpTimers()

let stopBreakApproachingNotification() =
    if showingBreakApproachingNotification then
         showingBreakApproachingNotification <- false
         snoozeMenuItem.IsEnabled <- false
         let mutable info = new FLASHWINFO(window.Handle, (* stop flashing *) 0u, 0u, 0u)
         FlashWindowEx(&info) |> ignore
         hideTimesUpTimers()

let mutable workDeadline = TimeSpan.FromMinutes(float workSlotInMinutes)
let locks = LockState.Tracker()
let mutable lockRevision = 0
let mutable settingsWindow: Window option = None
let keyboardLockHotkey = 1
let keyboardUnlockHotkey = 2
let mutable registeredKeyboardHotkeys = false

let pauseIndicator = window.FindName("PauseIndicator") :?> FrameworkElement
let pauseIndicatorTimer = System.Windows.Threading.DispatcherTimer(Interval = TimeSpan.FromSeconds(1.))

let updatePauseIndicator elapsed =
   if not locks.IsLocked then
      pauseIndicator.BeginAnimation(UIElement.OpacityProperty, null)
      pauseIndicator.Visibility <- Visibility.Collapsed
      pauseIndicatorTimer.Stop()
   else
      pauseIndicator.Visibility <- Visibility.Visible
      let willReset = WorkInterval.shouldResetAfterBreak elapsed
      let description = if willReset then "Paused - work timer will reset on return" else "Paused - short break"
      pauseIndicator.ToolTip <- description
      System.Windows.Automation.AutomationProperties.SetName(pauseIndicator, description)
      if willReset then
         pauseIndicator.BeginAnimation(UIElement.OpacityProperty, null)
      elif not pauseIndicator.HasAnimatedProperties then
         let pulse = DoubleAnimation(1., 0.25, Duration(TimeSpan.FromSeconds(1.)))
         pulse.AutoReverse <- true
         pulse.RepeatBehavior <- RepeatBehavior.Forever
         pauseIndicator.BeginAnimation(UIElement.OpacityProperty, pulse)
      pauseIndicatorTimer.Start()

pauseIndicatorTimer.Tick.Add(fun _ ->
   let offset = pausePreview |> Option.map (fun preview -> TimeSpan.FromSeconds(float preview.BreakSeconds)) |> Option.defaultValue TimeSpan.Zero
   updatePauseIndicator (offset + BreakInfo.FromDispatcherTimer(dispatcherTimer).BreakTimer.Elapsed))

let canSnooze() =
   let elapsed = BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer.Elapsed
   not locks.IsLocked && settingsWindow.IsNone &&
      (showingBreakApproachingNotification || elapsed >= workDeadline)

let updateSnoozeMenu() =
   snoozeMenuItem.Header <- $"Snooze ({preferences.SnoozeMinutes} min)"
   snoozeMenuItem.IsEnabled <- canSnooze()

let snooze() =
   if canSnooze() then
      let breakInfo = BreakInfo.FromDispatcherTimer(dispatcherTimer)
      workDeadline <- WorkInterval.snoozeDeadline preferences.SnoozeMinutes breakInfo.WorkTimer.Elapsed
      stopBreakApproachingNotification()
      breakInfo.BreakTimer.Reset()
      breakInfo.WorkTimer.Start()
      dispatcherTimer.Start()
      updateSnoozeMenu()

// Take a break, stops the dispatch timer and locks the current computer.
let takeBreak() = 
   dispatcherTimer.Stop()
   // let the work timer continue, in case the break is too short
   BreakInfo.FromDispatcherTimer(dispatcherTimer).BreakTimer.Restart()
   doActualWorkStationLock()

// Start work, resets work timer if the break was long enough.
// Todo: if the break is too short after 25 minutes, the workstation will lock in a "loop"
let startWork(ignoreBreak) =
   dispatcherTimer.Stop()

   let breakInfo = BreakInfo.FromDispatcherTimer(dispatcherTimer)
   if not(ignoreBreak) && not (WorkInterval.shouldResetAfterBreak breakInfo.BreakTimer.Elapsed) then
      // break is too short, so we don't touch the timer
      ()
   else
      stopBreakApproachingNotification()

      updateWindowIcon(0)
      workDeadline <- TimeSpan.FromMinutes(float workSlotInMinutes)
      BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer.Restart()

      (scroller :?> Controls.ExtendedScrollViewer).OnRestart()
      

   if not locks.IsLocked && settingsWindow.IsNone then dispatcherTimer.Start()

let setLockState source locked =
   match locks.Set(source, locked) with
   | LockState.Unchanged -> ()
   | LockState.BreakStarted ->
      lockRevision <- lockRevision + 1
      dispatcherTimer.Stop()
      stopBreakApproachingNotification()
      BreakInfo.FromDispatcherTimer(dispatcherTimer).BreakTimer.Restart()
   | LockState.BreakEnded ->
      lockRevision <- lockRevision + 1
      startWork false
      if settingsWindow.IsSome then BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer.Stop()
   updatePauseIndicator (BreakInfo.FromDispatcherTimer(dispatcherTimer).BreakTimer.Elapsed)


// Initialize timer values in 5 minute intervals.
let mutable minutes = 0;
let updateTimeline() =
   let parts = getTimelineParts window |> Seq.toArray
   let panel = parts[0].Parent :?> StackPanel
   let rulerLimit = max workSlotInMinutes (pausePreview |> Option.map (fun preview -> preview.WorkMinutes) |> Option.defaultValue 0)
   let requiredCount = max 8 (rulerLimit / 10 + 1)
   if requiredCount > parts.Length then
      for chunkIndex in parts.Length .. requiredCount - 1 do
         panel.Children.Add(Control(Template = parts[0].Template)) |> ignore
   elif requiredCount < parts.Length then
      for part in parts |> Array.skip requiredCount do
         panel.Children.Remove(part)
   minutes <- 0
   for part in getTimelineParts window do
      part.ApplyTemplate() |> ignore
      let marker = part.Template.FindName("WorkLimitMarker", part) :?> FrameworkElement
      marker.Visibility <- if workSlotInMinutes >= minutes && workSlotInMinutes < minutes + 10 then Visibility.Visible else Visibility.Collapsed
      Canvas.SetLeft(marker, float ((workSlotInMinutes - minutes) * 5) - 1.)
      marker.ToolTip <- $"Work limit: {workSlotInMinutes} minutes"
      (part.Template.FindName("firstMinute", part) :?> Label).Content <- string minutes
      minutes <- minutes + 5
      (part.Template.FindName("secondMinute", part) :?> Label).Content <- string minutes
      minutes <- minutes + 5

updateTimeline()

let showSettings() =
   match settingsWindow with
   | Some dialog -> dialog.Activate() |> ignore
   | None ->
      let dialog = loadWindow "SettingsWindow.xaml"
      dialog.Owner <- window
      dialog.Topmost <- window.Topmost
      let workTimer = BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer
      let wasTimingWork = workTimer.IsRunning
      let wasUpdating = dispatcherTimer.IsEnabled
      let revisionBeforeDialog = lockRevision
      let mutable saved = false
      SettingsDialog.configure dialog preferences (fun updated ->
         match Settings.save settingsPath updated with
         | Error message -> Error message
         | Ok () ->
            if updated.WorkDurationMinutes <> workSlotInMinutes then
               workSlotInMinutes <- updated.WorkDurationMinutes
               updateTimeline()
               workDeadline <- WorkInterval.deadlineAfterDurationChange workSlotInMinutes workTimer.Elapsed
            preferences <- updated
            applyTimerScale preferences.TimerScalePercent
            saved <- true
            Ok ())
      settingsWindow <- Some dialog
      dispatcherTimer.Stop()
      workTimer.Stop()
      stopBreakApproachingNotification()
      try
         dialog.ShowDialog() |> ignore
      finally
         settingsWindow <- None
         let shouldUpdate = not locks.IsLocked && (wasUpdating || lockRevision <> revisionBeforeDialog)
         if saved && shouldUpdate && workTimer.Elapsed >= workDeadline - TimeSpan.FromSeconds(10.) then
            startBreakApproachingNotification()
         if wasTimingWork then workTimer.Start()
         if shouldUpdate then dispatcherTimer.Start()


// Update every 5s or so (10 is noticeable).
let updateIntervalInSeconds = 5
dispatcherTimer.Interval <- new TimeSpan(0, 0, updateIntervalInSeconds);

let updateWorkDisplay (elapsed: TimeSpan) =
   scroller.ScrollToHorizontalOffset((scroller.ScrollableWidth + 75. ) * (elapsed.TotalMinutes / (float minutes)))
   updateWindowIcon(Math.Round(elapsed.TotalMinutes, 0) |> int)

dispatcherTimer.Tick.Add(fun e ->
   updateWorkDisplay (BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer.Elapsed)
   let hiresTimer = BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer

   // Let's give a 10s headsup by flashing the taskbar.
   let flashAfter = workDeadline - TimeSpan.FromSeconds(10.)
   if hiresTimer.Elapsed >= flashAfter then
      startBreakApproachingNotification()

   if hiresTimer.Elapsed >= workDeadline then
      takeBreak()
   )

if not isPreview then startWork(true)

// Always keep on top, even when everything is minimized (e.g. show desktop).
window.StateChanged.Add(fun _ ->
    if (window.WindowState = WindowState.Minimized) then
         window.WindowState <- WindowState.Normal
)

// Set up window movement with mouse.
window.PreviewMouseDoubleClick.Add(fun eventArgs ->
   if eventArgs.ChangedButton = MouseButton.Left && not isPreview then
      window.ReleaseMouseCapture()
      if locks.IsLocked then setLockState LockState.Keyboard false
      eventArgs.Handled <- true)
window.PreviewMouseDown.Add(fun eventArgs ->
   if eventArgs.ChangedButton = MouseButton.Left then
      if eventArgs.ClickCount = 1 then
         dragCoords <- eventArgs.GetPosition(window)
         window.CaptureMouse() |> ignore
      else window.ReleaseMouseCapture())
window.PreviewMouseUp.Add(fun eventArgs ->
   if eventArgs.ChangedButton = MouseButton.Left then
      window.ReleaseMouseCapture()
      saveWindowPlacement())
window.PreviewMouseMove.Add(fun eventArgs ->
   if Mouse.LeftButton = MouseButtonState.Released then
      window.ReleaseMouseCapture()
   elif window.IsMouseCaptured then
      let position = eventArgs.GetPosition(window)
      window.Left <- window.Left + position.X - dragCoords.X
      window.Top <- window.Top + position.Y - dragCoords.Y)

snoozeMenuItem.Click.Add(fun _ -> snooze())
window.ContextMenu.Opened.Add(fun _ -> updateSnoozeMenu())
updateSnoozeMenu()
(window.FindName("RestartMenuItem") :?> MenuItem).Click.Add(fun _ -> startWork true)
(window.FindName("SettingsMenuItem") :?> MenuItem).Click.Add(fun _ -> showSettings())
(window.FindName("QuitMenuItem") :?> MenuItem).Click.Add(fun _ -> application.Shutdown())

// Hook system events to respond to lock event.
let sessionSwitchHandler = SessionSwitchEventHandler(fun _ args ->
   if not isSmokeTest && not isPreview then
      window.Dispatcher.InvokeAsync(Action(fun () ->
         match args.Reason with
         | SessionSwitchReason.SessionLock ->
            setLockState LockState.Session true
         | SessionSwitchReason.SessionLogon
         | SessionSwitchReason.SessionUnlock -> setLockState LockState.Session false
         | _ -> ())) |> ignore)

SystemEvents.SessionSwitch.AddHandler(sessionSwitchHandler)
let displaySettingsChangedHandler = EventHandler(fun _ _ ->
   window.Dispatcher.InvokeAsync(Action(fun () -> countdownWindows.Refresh())) |> ignore)
SystemEvents.DisplaySettingsChanged.AddHandler(displaySettingsChangedHandler)
application.Exit.Add(fun _ ->
   saveWindowPlacement()
   dispatcherTimer.Stop()
   pauseIndicatorTimer.Stop()
   pauseIndicator.BeginAnimation(UIElement.OpacityProperty, null)
   SystemEvents.SessionSwitch.RemoveHandler(sessionSwitchHandler)
   SystemEvents.DisplaySettingsChanged.RemoveHandler(displaySettingsChangedHandler)
   if registeredKeyboardHotkeys then
      UnregisterHotKey(window.Handle, keyboardLockHotkey) |> ignore
      UnregisterHotKey(window.Handle, keyboardUnlockHotkey) |> ignore
   (countdownWindows :> IDisposable).Dispose()
   if isSmokeTest then
      let directory = System.IO.Path.GetDirectoryName(settingsPath)
      if System.IO.Directory.Exists(directory) then System.IO.Directory.Delete(directory, true))

// Once loaded, show taskbar icon and hook windows messages.
window.Loaded.Add(fun _ -> 
   placementReady <- true
   // Avoid the icon showing up as a separate window in e.g. alt+tab
   icon.Owner <- window
   updateWindowIcon(0)

   // Hook the win proc for processing remote commands.
   let hwndSrc = PresentationSource.FromVisual(window) :?> HwndSource
   hwndSrc.AddHook(new System.Windows.Interop.HwndSourceHook(fun handle -> fun msg -> fun wParam -> fun lParam -> fun handled -> 
      handled <- true

      match LanguagePrimitives.EnumOfValue<int, WindowsMsg>(msg) with
      | WindowsMsg.Quit -> application.Shutdown()
      | _ when isPreview -> handled <- false
      | _ when msg = 0x0312 && wParam = nativeint keyboardLockHotkey -> setLockState LockState.Keyboard true
      | _ when msg = 0x0312 && wParam = nativeint keyboardUnlockHotkey -> setLockState LockState.Keyboard false
      | WindowsMsg.Restart -> startWork true
      | WindowsMsg.Snooze -> snooze()
      | WindowsMsg.Settings -> showSettings()
      | _ -> handled <- false
   
      IntPtr.Zero))

   // Add the restart command to the jumplist.
   // Initialize the jumplist. todo: add nice icon
   if not isSmokeTest && not isPreview then
      let lockRegistered = RegisterHotKey(window.Handle, keyboardLockHotkey, 0x4000u, 0x87u)
      let unlockRegistered = RegisterHotKey(window.Handle, keyboardUnlockHotkey, 0x4000u, 0x85u)
      registeredKeyboardHotkeys <- lockRegistered && unlockRegistered
      if not registeredKeyboardHotkeys then
         if lockRegistered then UnregisterHotKey(window.Handle, keyboardLockHotkey) |> ignore
         if unlockRegistered then UnregisterHotKey(window.Handle, keyboardUnlockHotkey) |> ignore
         MessageBox.Show(window, "Could not register F24/F22 for keyboard lock detection. Another application or timer instance may be using these keys. Windows lock detection remains available.", "Keyboard lock detection", MessageBoxButton.OK, MessageBoxImage.Warning) |> ignore
      let restartTask = new JumpTask()
      restartTask.ApplicationPath <- Environment.ProcessPath
      restartTask.Arguments <- "/restart " + window.Handle.ToString()
      restartTask.Title <- "Restart"
      restartTask.Description <- "Restarts the current timer"
      restartTask.IconResourcePath <- restartTask.ApplicationPath
      restartTask.IconResourceIndex <- 1

      let jumpList = new JumpList()
      jumpList.JumpItems.Add(restartTask)
      let settingsTask = new JumpTask()
      settingsTask.ApplicationPath <- Environment.ProcessPath
      settingsTask.Arguments <- "/settings " + window.Handle.ToString()
      settingsTask.Title <- "Settings"
      settingsTask.Description <- "Change the work duration"
      settingsTask.IconResourcePath <- settingsTask.ApplicationPath
      settingsTask.IconResourceIndex <- 0
      jumpList.JumpItems.Add(settingsTask)
      JumpList.SetJumpList(application, jumpList)
      jumpList.Apply()
   )


[<STAThread>]
[<EntryPoint>]
let main _ =
   if isPreview then
      dispatcherTimer.Stop()
      window.Title <- "Pomodoro Timer - Pause Preview"
      for name in ["RestartMenuItem"; "SettingsMenuItem"] do
         (window.FindName(name) :?> MenuItem).IsEnabled <- false
      window.ContentRendered.Add(fun _ ->
         let preview = pausePreview.Value
         updateTimeline()
         window.UpdateLayout()
         BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer.Stop()
         setLockState LockState.Keyboard true
         updateWorkDisplay (TimeSpan.FromMinutes(float preview.WorkMinutes))
         updatePauseIndicator (TimeSpan.FromSeconds(float preview.BreakSeconds))
         if isSmokeTest then
            try
               SmokeTest.checkPausePreview window preview (fun () -> dispatcherTimer.IsEnabled)
            with error ->
               Console.Error.WriteLine(error)
               application.Shutdown(1))
   elif isSmokeTest then
      dispatcherTimer.Stop()
      window.ContentRendered.Add(fun _ ->
         try
            SmokeTest.checkPreviewArguments()
            SmokeTest.checkWindowPlacement()
            SmokeTest.checkKeyboardLocks window
               (BreakInfo.FromDispatcherTimer(dispatcherTimer).BreakTimer)
               (fun () -> dispatcherTimer.IsEnabled)
               (fun locked -> SendMessage(window.Handle, 0x0312, nativeint (if locked then keyboardLockHotkey else keyboardUnlockHotkey), IntPtr.Zero) |> ignore)
            SmokeTest.checkPauseIndicator window updatePauseIndicator
               (fun locked -> SendMessage(window.Handle, 0x0312, nativeint (if locked then keyboardLockHotkey else keyboardUnlockHotkey), IntPtr.Zero) |> ignore)
               (fun locked -> setLockState LockState.Session locked)
               (fun () -> pauseIndicatorTimer.IsEnabled)
               (fun () -> dispatcherTimer.IsEnabled)
            SmokeTest.checkDoubleClickResume window (BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer)
               (fun locked -> setLockState LockState.Keyboard locked)
               (fun locked -> setLockState LockState.Session locked)
               (fun () -> dispatcherTimer.IsEnabled)
            SmokeTest.run window (BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer)
               (fun () -> countdownWindows.Windows) startBreakApproachingNotification stopBreakApproachingNotification
               settingsPath (fun () -> workSlotInMinutes)
               (fun () -> SendMessage(window.Handle, int WindowsMsg.Settings, IntPtr.Zero, IntPtr.Zero) |> ignore)
               (fun () -> loadWindow "TimesUpTimer.xaml")
               (fun () ->
                  SmokeTest.checkSnooze window (BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer)
                     preferences.SnoozeMinutes (fun () -> workDeadline)
                     (fun deadline ->
                        workDeadline <- deadline
                        dispatcherTimer.Stop()
                        updateSnoozeMenu())
                     (fun () -> dispatcherTimer.IsEnabled)
                     (fun locked -> setLockState LockState.Keyboard locked)
                     startBreakApproachingNotification (fun () -> countdownWindows.Windows))
         with error ->
            Console.Error.WriteLine(error)
            application.Shutdown(1))
   match previewResult with
   | Error message ->
      if isSmokeTest then Console.Error.WriteLine(message)
      else MessageBox.Show(message, "Pause preview", MessageBoxButton.OK, MessageBoxImage.Information) |> ignore
      2
   | Ok _ -> application.Run(window)