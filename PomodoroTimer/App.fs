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
open System.Windows.Shell
open FSharpx
open Microsoft.Win32
open FsXaml
open System.Runtime.InteropServices
open System.Diagnostics

// Interop
[<DllImport("user32.dll", SetLastError = true)>]
extern bool LockWorkStation();

[<DllImport("user32.dll", SetLastError = true)>]
extern nativeint SendMessage(nativeint hWnd, int Msg, nativeint wParam, nativeint lParam)

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

type Application = XAML<"Application.xaml">
type _MainWindow = XAML<"MainWindow.xaml">
type Icon = XAML<"Icon.xaml">

type WindowsMsg = 
   | Reset = 0x0401 // rename to restart todo
   | Quit = 0x0402

type MainWindow() =
   inherit _MainWindow()
   member this.Handle with get() = (new WindowInteropHelper(this)).Handle
   override this.ContextRestartClick(sender, args) = SendMessage(this.Handle, WindowsMsg.Reset |> int, IntPtr.Zero, IntPtr.Zero) |> ignore
   override this.ContextQuitClick(sender, args) = SendMessage(this.Handle, WindowsMsg.Quit |> int, IntPtr.Zero, IntPtr.Zero) |> ignore

// Info about break and work. WorkTimer times work done, BreakTimer times the current break.
type BreakInfo = { WorkTimer: Stopwatch; BreakTimer: Stopwatch } with
   static member FromDispatcherTimer(timer: System.Windows.Threading.DispatcherTimer) = timer.Tag :?> BreakInfo

// Config
let workSlotInMinutes = 25


// Construct application etc.
let window = MainWindow()
let scroller = window.Root.FindName("TimelineScroller") :?> System.Windows.Controls.ScrollViewer

let icon = new Icon()
icon.ShowInTaskbar <- false
icon.Left <- -10000. // offscreen
icon.Show() // required for rendering to image source to work

let application = new Application()

// Add hook that checks for the /restart command line, and sends a Windows message to the relevant instance if found, before quitting.
application.Startup.Add(fun (args: StartupEventArgs) ->
   (
      if args.Args.Length >= 2 then
         if (args.Args.[0] = "/restart") then
            let targetWindow = args.Args.[1] |> Int32.Parse |> nativeint
            let result = SendMessage(targetWindow, LanguagePrimitives.EnumToValue(WindowsMsg.Reset), IntPtr.Zero, IntPtr.Zero)
            application.Shutdown()
   )
)


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
   ()


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
      let mutable info = new FLASHWINFO(window.Handle, (* stop flashing *) 0u, 0u, 0u)
      FlashWindowEx(&info) |> ignore

      updateWindowIcon(0)
      BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer.Restart()

      (scroller :?> Controls.ExtendedScrollViewer).OnRestart()
      

   dispatcherTimer.Start()


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
   updateWindowIcon(Math.Round(workTimer.Elapsed.TotalMinutes, 0) |> int)

   let hiresTimer = BreakInfo.FromDispatcherTimer(dispatcherTimer).WorkTimer

   // Let's give a 10s headsup by flashing the taskbar.
   let flashAfterSeconds = workSlotInMinutes * 60 - 10 |> float
   if hiresTimer.Elapsed.TotalSeconds > flashAfterSeconds then
      let mutable info = new FLASHWINFO(window.Handle, (* flash task tray *) 2u, 50u, 200u)
      FlashWindowEx(&info) |> ignore

   if hiresTimer.Elapsed.TotalMinutes > float(workSlotInMinutes) then
      takeBreak()
   )

startWork(true)

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
      startWork false

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
      | WindowsMsg.Reset -> startWork true
      | WindowsMsg.Quit -> application.Shutdown()
      | _ -> handled <- false
   
      IntPtr.Zero))

   // Add the restart command to the jumplist.
   // Initialize the jumplist. todo: add nice icon
   let restartTask = new JumpTask()
   restartTask.ApplicationPath <- System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName
   restartTask.Arguments <- "/restart " + window.Handle.ToString()
   restartTask.Title <- "Restart"
   restartTask.Description <- "Restarts the current timer"
   restartTask.IconResourcePath <- restartTask.ApplicationPath
   restartTask.IconResourceIndex <- 1

   // note: it's possible to add to an existing jumplist defined in xaml, however, items appear to disappear
   // after use (re-rendered?)
   let jumpList = new JumpList() // JumpList.GetJumpList(application)
   jumpList.JumpItems.Add(restartTask)
   JumpList.SetJumpList(application, jumpList)
   jumpList.Apply()

   )


[<STAThread>]
[<EntryPoint>]
application.Run(window) |> ignore