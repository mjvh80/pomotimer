module CountdownWindows

open System
open System.Windows
open System.Windows.Media.Animation

type Manager(createWindow: unit -> Window, getBounds: unit -> Rect array, getTime: unit -> TimeSpan) =
    let mutable windows: Window array = [||]
    let mutable startedAt: TimeSpan option = None
    let mutable disposed = false

    let closeWindows() =
        let previous = windows
        windows <- [||]
        for window in previous do window.Close()

    member _.Windows = Array.copy windows

    member _.Refresh() =
        if not disposed then
            match startedAt with
            | None -> closeWindows()
            | Some start ->
                let bounds = getBounds() |> Array.distinct
                closeWindows()
                for screen in bounds do
                    let window = createWindow()
                    windows <- Array.append windows [| window |]
                    window.ShowInTaskbar <- false
                    window.Left <- screen.Left + (screen.Width - window.Width) / 2.
                    window.Top <- screen.Top + (screen.Height - window.Height) / 2.
                    window.Show()
                    let text = window.FindName("TimesUpTimerText") :?> FrameworkElement
                    let animation = text.FindResource("Animation") :?> Storyboard
                    animation.Begin(window, true)
                    animation.SeekAlignedToLastTick(window, getTime() - start, TimeSeekOrigin.BeginTime)

    member this.Show() =
        if not disposed then
            startedAt <- Some (getTime())
            this.Refresh()

    member _.Hide() =
        startedAt <- None
        closeWindows()

    interface IDisposable with
        member _.Dispose() =
            disposed <- true
            startedAt <- None
            closeWindows()