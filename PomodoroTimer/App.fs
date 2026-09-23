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
let settingsPath =
   if isSmokeTest then
      System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PomodoroTimer-smoke-" + Guid.NewGuid().ToString("N"), "settings.json")
   else
      Settings.filePath
let mutable workSlotInMinutes = Settings.load settingsPath


// Construct application etc.
let application = Application(ShutdownMode = ShutdownMode.OnMainWindowClose)
let window = loadWindow "MainWindow.xaml"
application.MainWindow <- window
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
   if not isSmokeTest && not(Keyboard.IsKeyDown(Key.RightCtrl)) then
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

// Create "time's up timers" (counters that display when time's up).
let timesUpTimerWindows =
    WpfScreenHelper.Screen.AllScreens
    |> Seq.map (fun screen ->
         let countdown = loadWindow "TimesUpTimer.xaml"
         countdown.ShowActivated <- true
         countdown.ShowInTaskbar <- false
         countdown.Left <- screen.Bounds.Left + (screen.Bounds.Width - countdown.Width) / 2.0
         countdown.Top <- screen.Bounds.Top + (screen.Bounds.Height - countdown.Height) / 2.0
         countdown)
    |> Seq.toArray

let showTimesUpTimers() =
    for countdown in timesUpTimerWindows do
         countdown.Show()
         let text = countdown.FindName("TimesUpTimerText") :?> FrameworkElement
         (text.FindResource("Animation") :?> Storyboard).Begin(countdown, true)
         countdown.Activate() |> ignore

let hideTimesUpTimers() =
    for countdown in timesUpTimerWindows do
         let text = countdown.FindName("TimesUpTimerText") :?> FrameworkElement
         (text.FindResource("Animation") :?> Storyboard).Remove(countdown)
         countdown.Hide()

let mutable showingBreakApproachingNotification = false

let startBreakApproachingNotification() =
    if not showingBreakApproachingNotification then
        showingBreakApproachingNotification <- true
        let mutable info = new FLASHWINFO(window.Handle, (* flash task tray *) 2u, 50u, 200u)
        FlashWindowEx(&info) |> ignore

        // Note: the following shows an animation starting at 9, not fully accurate but will suffice
        showTimesUpTimers()

let stopBreakApproachingNotification() = 
  if showingBreakApproachingNotification then
    showingBreakApproachingNotification <- false
    let mutable info = new FLASHWINFO(window.Handle, (* stop flashing *) 0u, 0u, 0u)
    FlashWindowEx(&info) |> ignore
    hideTimesUpTimers()

let snooze() = ()

let mutable workDeadline = TimeSpan.FromMinutes(float workSlotInMinutes)

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
   let breakTimeInMinutes = if breakInfo.BreakTimer = null then 0.0 else breakInfo.BreakTimer.Elapsed.TotalMinutes
   if not(ignoreBreak) && (breakTimeInMinutes < 5.) then
      // break is too short, so we don't touch the timer
      ()
   else
      stopBreakApproachingNotification()

      updateWindowIcon(0)
      workDeadline <- TimeSpan.FromMinutes(float workSlotInMinutes)
      BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer.Restart()

      (scroller :?> Controls.ExtendedScrollViewer).OnRestart()
      

   dispatcherTimer.Start()


// Initialize timer values in 5 minute intervals.
let mutable minutes = 0;
let updateTimeline() =
   let parts = getTimelineParts window |> Seq.toArray
   let panel = parts[0].Parent :?> StackPanel
   let requiredCount = max 8 ((workSlotInMinutes + 9) / 10)
   if requiredCount > parts.Length then
      for chunkIndex in parts.Length .. requiredCount - 1 do
         panel.Children.Add(Control(Template = parts[0].Template)) |> ignore
   elif requiredCount < parts.Length then
      for part in parts |> Array.skip requiredCount do
         panel.Children.Remove(part)
   minutes <- 0
   for part in getTimelineParts window do
      part.ApplyTemplate() |> ignore
      (part.Template.FindName("firstMinute", part) :?> Label).Content <- string minutes
      minutes <- minutes + 5
      (part.Template.FindName("secondMinute", part) :?> Label).Content <- string minutes
      minutes <- minutes + 5

updateTimeline()

let mutable settingsWindow: Window option = None

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
      let mutable saved = false
      SettingsDialog.configure dialog workSlotInMinutes (fun duration ->
         match Settings.save settingsPath duration with
         | Error message -> Error message
         | Ok () ->
            workSlotInMinutes <- duration
            updateTimeline()
            workDeadline <- WorkInterval.deadlineAfterDurationChange duration workTimer.Elapsed
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
         if saved && wasUpdating && workTimer.Elapsed >= workDeadline - TimeSpan.FromSeconds(10.) then
            startBreakApproachingNotification()
         if wasTimingWork then workTimer.Start()
         if wasUpdating then dispatcherTimer.Start()


// Update every 5s or so (10 is noticeable).
let updateIntervalInSeconds = 5
dispatcherTimer.Interval <- new TimeSpan(0, 0, updateIntervalInSeconds);

dispatcherTimer.Tick.Add(fun e ->

   let workTimer = BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer 
   scroller.ScrollToHorizontalOffset((scroller.ScrollableWidth + 75. ) * (workTimer.Elapsed.TotalMinutes / (float minutes)))
   updateWindowIcon(Math.Round(workTimer.Elapsed.TotalMinutes, 0) |> int)

   let hiresTimer = BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer

   // Let's give a 10s headsup by flashing the taskbar.
   let flashAfter = workDeadline - TimeSpan.FromSeconds(10.)
   if hiresTimer.Elapsed >= flashAfter then
      startBreakApproachingNotification()

   if hiresTimer.Elapsed >= workDeadline then
      takeBreak()
   )

startWork(true)

// Always keep on top, even when everything is minimized (e.g. show desktop).
window.StateChanged.Add(fun _ ->
    if (window.WindowState = WindowState.Minimized) then
         window.WindowState <- WindowState.Normal
)

// Set up window movement with mouse.
window.PreviewMouseDown.Add(fun eventArgs ->
   if eventArgs.ChangedButton = MouseButton.Left then
      dragCoords <- eventArgs.GetPosition(window)
      window.CaptureMouse() |> ignore)
window.PreviewMouseUp.Add(fun eventArgs ->
   if eventArgs.ChangedButton = MouseButton.Left then window.ReleaseMouseCapture())
window.PreviewMouseMove.Add(fun eventArgs ->
   if Mouse.LeftButton = MouseButtonState.Released then
      window.ReleaseMouseCapture()
   elif window.IsMouseCaptured then
      let position = eventArgs.GetPosition(window)
      window.Left <- window.Left + position.X - dragCoords.X
      window.Top <- window.Top + position.Y - dragCoords.Y)

(window.FindName("RestartMenuItem") :?> MenuItem).Click.Add(fun _ -> startWork true)
(window.FindName("SettingsMenuItem") :?> MenuItem).Click.Add(fun _ -> showSettings())
(window.FindName("QuitMenuItem") :?> MenuItem).Click.Add(fun _ -> application.Shutdown())

// Hook system events to respond to lock event.
let sessionSwitchHandler = SessionSwitchEventHandler(fun _ args ->
   if not isSmokeTest then
      window.Dispatcher.InvokeAsync(Action(fun () ->
         match args.Reason with
         | SessionSwitchReason.SessionLock ->
            dispatcherTimer.Stop()
            BreakInfo.FromDispatcherTimer(dispatcherTimer).BreakTimer.Restart()
         | SessionSwitchReason.SessionLogon
         | SessionSwitchReason.SessionUnlock -> startWork false
         | _ -> ())) |> ignore)

SystemEvents.SessionSwitch.AddHandler(sessionSwitchHandler)
application.Exit.Add(fun _ ->
   dispatcherTimer.Stop()
   SystemEvents.SessionSwitch.RemoveHandler(sessionSwitchHandler)
   if isSmokeTest then
      let directory = System.IO.Path.GetDirectoryName(settingsPath)
      if System.IO.Directory.Exists(directory) then System.IO.Directory.Delete(directory, true))

// Once loaded, show taskbar icon and hook windows messages.
window.Loaded.Add(fun _ -> 
   // Avoid the icon showing up as a separate window in e.g. alt+tab
   icon.Owner <- window
   updateWindowIcon(0)

   // Hook the win proc for processing remote commands.
   let hwndSrc = PresentationSource.FromVisual(window) :?> HwndSource
   hwndSrc.AddHook(new System.Windows.Interop.HwndSourceHook(fun handle -> fun msg -> fun wParam -> fun lParam -> fun handled -> 
      handled <- true

      match LanguagePrimitives.EnumOfValue<int, WindowsMsg>(msg) with
      | WindowsMsg.Restart -> startWork true
      | WindowsMsg.Settings -> showSettings()
      | WindowsMsg.Quit -> application.Shutdown()
      | _ -> handled <- false
   
      IntPtr.Zero))

   // Add the restart command to the jumplist.
   // Initialize the jumplist. todo: add nice icon
   if not isSmokeTest then
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
   if isSmokeTest then
      dispatcherTimer.Stop()
      window.ContentRendered.Add(fun _ ->
         try
            SmokeTest.run window (BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer)
               timesUpTimerWindows startBreakApproachingNotification stopBreakApproachingNotification
               settingsPath (fun () -> workSlotInMinutes)
               (fun () -> SendMessage(window.Handle, int WindowsMsg.Settings, IntPtr.Zero, IntPtr.Zero) |> ignore)
         with error ->
            Console.Error.WriteLine(error)
            application.Shutdown(1))
   application.Run(window)