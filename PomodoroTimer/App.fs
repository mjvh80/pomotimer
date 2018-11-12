module App


open System
open System.Net
open System.Threading
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open FSharpx
open Microsoft.Win32
open FsXaml
open System.Runtime.InteropServices
open System.Diagnostics

type MainWindow = XAML<"MainWindow.xaml">

type MainWindow2() =
   inherit MainWindow()
   override x.TimelineLoaded(timeline: obj, _) = 
      match timeline with
      | :? Control as timelineCtrl ->
//         let first = timelineCtrl.Template.FindName("firstMinute", timelineCtrl)
//         let x = first.GetType()
         ()
      | _ -> ()
      ()

[<DllImport("user32.dll", SetLastError = true)>]
extern bool LockWorkStation();

//type MainWindow with
//   member x.foo_click (a, b) = ()

//type MainWindow() as this = 
//   inherit MainWindowBase()
//   override base.ont

let window = MainWindow2()


// Allow window to be moved.
let mutable dragCoords = new Windows.Point()

let getTimelineParts (window: MainWindow) = 
   let firstPart = window.Root.FindName("firstTimelinePart") :?> Control
   LogicalTreeHelper.GetChildren(firstPart.Parent) 
      |> Seq.cast<FrameworkElement>
      |> Seq.skipWhile (fun element -> not(Object.ReferenceEquals(element, firstPart)))
      |> Seq.cast<Control>


let dispatcherTimer = new System.Windows.Threading.DispatcherTimer()
dispatcherTimer.Tag <- System.Diagnostics.Stopwatch.StartNew()

let startTimer() = dispatcherTimer.Start(); (dispatcherTimer.Tag :?> Stopwatch).Restart()
let stopTimer() = dispatcherTimer.Stop()

// Function to create the main application window, and hook it up.
let createMainWindow  () = 
  

   let scroller = window.Root.FindName("TimelineScroller") :?> System.Windows.Controls.ScrollViewer

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


   // Demonstrate scroller
   
   // Update every 5s or so (10 is noticeable).
   let updateIntervalInSeconds = 10
   dispatcherTimer.Interval <- new TimeSpan(0, 0, updateIntervalInSeconds);

   dispatcherTimer.Tick.Add(fun e ->
      // Increment with one minute
      // Todo: let's get the 75 (width of spacer) somehow (translatepoint?)
      let delta = ((scroller.ScrollableWidth + 75. ) / (float minutes)) / ( 60. / (float updateIntervalInSeconds))
      scroller.ScrollToHorizontalOffset(scroller.ContentHorizontalOffset + delta)
      
      let hiresTimer = dispatcherTimer.Tag :?> Stopwatch

      // todo: make this configurable
      if (hiresTimer.Elapsed.TotalMinutes > 25.) then LockWorkStation() |> ignore
      )
   
   startTimer()

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
      | SessionSwitchReason.SessionLock
      | SessionSwitchReason.SessionLogoff ->()
      
      | SessionSwitchReason.SessionLogon
      | SessionSwitchReason.SessionUnlock ->
         // For now, this'll get more complex prolly
         // todo: enforce a minumum pauze interval?
         stopTimer()
         scroller.ScrollToHorizontalOffset(0.0)
         startTimer()

      | _ -> ()
   )

   window.Root

[<STAThread>]
[<EntryPoint>]
(new Application()).Run(createMainWindow()) |> ignore