﻿module App


open System
open System.Net
open System.Threading
open System.Windows
open System.Windows.Input
open System.Windows.Interop
open System.Windows.Controls
open System.Windows.Media
open System.Windows.Shell
open FSharpx
open Microsoft.Win32
open FsXaml
open System.Runtime.InteropServices
open System.Diagnostics

type Application = XAML<"Application.xaml">
type MainWindow = XAML<"MainWindow.xaml">
type Icon = XAML<"Icon.xaml">

// Info about break and work. WorkTimer times work done, BreakTimer times the current break.
type BreakInfo = { WorkTimer: Stopwatch; BreakTimer: Stopwatch } with
   static member FromDispatcherTimer(timer: System.Windows.Threading.DispatcherTimer) = timer.Tag :?> BreakInfo

type WindowsMsg = 
   | Reset = 0x0401 // rename to restart todo

[<DllImport("user32.dll", SetLastError = true)>]
extern bool LockWorkStation();

[<DllImport("user32.dll", SetLastError = true)>]
extern nativeint SendMessage(nativeint hWnd, int Msg, nativeint wParam, nativeint lParam);

let window = MainWindow()
let scroller = window.Root.FindName("TimelineScroller") :?> System.Windows.Controls.ScrollViewer

let icon = new Icon()
icon.ShowInTaskbar <- false
icon.Left <- -10000. // offscreen
icon.Show() // required for rendering to image source to work

// Allow window to be moved.
let mutable dragCoords = new Windows.Point()

let getTimelineParts (window: MainWindow) = 
   let firstPart = window.Root.FindName("firstTimelinePart") :?> Control
   LogicalTreeHelper.GetChildren(firstPart.Parent) 
      |> Seq.cast<FrameworkElement>
      |> Seq.skipWhile (fun element -> not(Object.ReferenceEquals(element, firstPart)))
      |> Seq.cast<Control>

// WPF timer to update our timer UI on the message loop.
let dispatcherTimer = new System.Windows.Threading.DispatcherTimer()
dispatcherTimer.Tag <- box({ WorkTimer = Stopwatch.StartNew(); BreakTimer = new Stopwatch() })

// Protect against locking out the user in case of bugs, don't lock if control is down.
let doActualWorkStationLock() =
   if not(Keyboard.IsKeyDown(Key.RightCtrl)) then 
      LockWorkStation() |> ignore // todo handle somehow?

// Sets the taskbar icon by rendering the Icon.xaml file to it.
let updateWindowIcon(minutes: int) = 
   let rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(icon.Width |> int, icon.Height |> int, 96., 96., PixelFormats.Pbgra32);
   (icon.FindName("minutes") :?> Label).Content <- minutes.ToString() // fuck databinding
   rtb.Render(icon)
   window.Icon <- rtb

let resetTimer() =
   BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer.Restart()
   scroller.ScrollToHorizontalOffset(0.0)

// Take a break, stops the dispatch timer and locks the current computer.
let takeBreak() = 
   dispatcherTimer.Stop()
   // let the work timer continue, in case the break is too short
   BreakInfo.FromDispatcherTimer(dispatcherTimer).BreakTimer.Restart()
   doActualWorkStationLock()

// Start work, resets work timer if the break was long enough.
// Todo: if the break is too short after 25 minutes, the workstation will lock in a "loop"
let startWork() =
   let breakInfo = BreakInfo.FromDispatcherTimer(dispatcherTimer)
   let breakTimeInMinutes = if breakInfo.BreakTimer = null then 0.0 else breakInfo.BreakTimer.Elapsed.TotalMinutes
   if (breakTimeInMinutes < 5.) then
      // break is too short, so we don't touch the timer
      ()
   else
      breakInfo.WorkTimer.Restart()
      scroller.ScrollToHorizontalOffset(0.0)
      updateWindowIcon(0)

   dispatcherTimer.Start()

// Function to create the main application window, and hook it up.
let createMainWindow  (application) = 

   // Initialize timer values in 5 minute intervals.
   let mutable minutes = 0;
   let timelineParts = (getTimelineParts window) |> Seq.toArray
   for i = 0 to (timelineParts.Length - 1) do
      timelineParts.[i].ApplyTemplate() |> ignore
      
      let firstMinute = (timelineParts.[i].Template.FindName("firstMinute", timelineParts.[i])) :?> Label
      firstMinute.Content <- minutes.ToString()
      minutes <- minutes + 5

      let secondMinute = (timelineParts.[i].Template.FindName("secondMinute", timelineParts.[i])) :?> Label
      secondMinute.Content <- minutes.ToString()
      minutes <- minutes + 5


   // Update every 5s or so (10 is noticeable).
   let updateIntervalInSeconds = 5
   dispatcherTimer.Interval <- new TimeSpan(0, 0, updateIntervalInSeconds);

   dispatcherTimer.Tick.Add(fun e ->

      let workTimer = BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer 
      scroller.ScrollToHorizontalOffset((scroller.ScrollableWidth + 75. ) * (workTimer.Elapsed.TotalMinutes / (float minutes)))
      updateWindowIcon(workTimer.Elapsed.TotalMinutes |> int)

      let hiresTimer = BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer

      // todo: make this configurable
      if (hiresTimer.Elapsed.TotalMinutes > 25.0) then
         takeBreak()
      )
   
   startWork()

   // Always keep on top, even when everything is minimized (e.g. show desktop).
   window.Root.StateChanged.Add(fun _ ->
      if (window.Root.WindowState = WindowState.Minimized) then
         window.Root.WindowState <- WindowState.Normal
   )

   // Set up window movement with mouse.
   window.Root.PreviewMouseDown.Add(fun e -> dragCoords <- e.GetPosition(window.Root); window.Root.CaptureMouse() |> ignore)
   window.Root.PreviewMouseUp.Add(fun _ ->  window.Root.ReleaseMouseCapture())
   window.Root.PreviewMouseMove.Add(fun e -> if Input.Mouse.LeftButton = Input.MouseButtonState.Released then
                                                    window.Root.ReleaseMouseCapture()
                                                  else if window.Root.IsMouseCaptured then
                                                    let p = e.GetPosition(window.Root)
                                                    let dx, dy = p.X - dragCoords.X, p.Y - dragCoords.Y
                                                    window.Root.Left <- window.Root.Left + dx
                                                    window.Root.Top <- window.Root.Top + dy)

   // Hook system events to respond to lock event.
   SystemEvents.SessionSwitch.Add(fun (args: SessionSwitchEventArgs) -> 
      match args.Reason with
      | SessionSwitchReason.SessionLock ->
         takeBreak()
      
      | SessionSwitchReason.SessionLogon
      | SessionSwitchReason.SessionUnlock ->
         // For now, this'll get more complex prolly
         // todo: enforce a minumum pauze interval?
         startWork()

      | _ -> ()
   )

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
         | WindowsMsg.Reset -> resetTimer()
         | _ -> handled <- false
      
         IntPtr.Zero))

      // Add the restart command to the jumplist.
      // Initialize the jumplist. todo: add nice icon
      let restartTask = new JumpTask()
      restartTask.ApplicationPath <- System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName
      restartTask.Arguments <- "/restart " + (new WindowInteropHelper(window)).Handle.ToString()
      restartTask.Title <- "Restart"
      restartTask.Description <- "Restarts the current timer"

      // note: it's possible to add to an existing jumplist defined in xaml, however, items appear to disappear
      // after use (re-rendered?)
      let jumpList = new JumpList() // JumpList.GetJumpList(application)
      jumpList.JumpItems.Add(restartTask)
      JumpList.SetJumpList(application, jumpList)
      jumpList.Apply()
      )

   // Return.
   window.Root


type PomoApplication() =
   inherit Application()
   


let application = new Application()
let mainWindow = createMainWindow(application)

// Add hook that checks for the /task command line, and sends a Windows message to the relevant instance if found, before quitting.
application.Startup.Add(fun (args: StartupEventArgs) ->
   (
      if args.Args.Length >= 2 then
         if (args.Args.[0] = "/restart") then
            let targetWindow = args.Args.[1] |> Int32.Parse |> nativeint
            let result = SendMessage(targetWindow, LanguagePrimitives.EnumToValue(WindowsMsg.Reset), IntPtr.Zero, IntPtr.Zero)
            application.Shutdown()
   )
)

[<STAThread>]
[<EntryPoint>]
application.Run(mainWindow) |> ignore