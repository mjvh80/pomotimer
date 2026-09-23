module SmokeTest

open System
open System.Diagnostics
open System.IO
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Windows.Media.Animation
open System.Windows.Media.Imaging

let private check condition message =
    if not condition then failwith message

let run (window: Window) (workTimer: Stopwatch) (countdowns: Window array) startNotification stopNotification =
    check (window.IsVisible && window.ActualWidth > 0.) "Main window did not open."
    check (window.Icon <> null) "Taskbar icon was not rendered."
    check (countdowns.Length > 0) "No display was detected."

    let restart = window.FindName("RestartMenuItem") :?> MenuItem
    let quit = window.FindName("QuitMenuItem") :?> MenuItem
    for menuItem in [restart; quit] do
        let image = menuItem.Icon :?> Image
        check (image.Source <> null && image.Source.Width > 0.) "Menu icon did not load."

    let firstPart = window.FindName("firstTimelinePart") :?> Control
    let firstMinute = firstPart.Template.FindName("firstMinute", firstPart) :?> Label
    check (string firstMinute.Content = "0") "Timeline labels were not initialized."

    startNotification()
    for countdown in countdowns do
        check countdown.IsVisible "Countdown window did not open."
        let text = countdown.FindName("TimesUpTimerText") :?> TextBlock
        let animation = text.FindResource("Animation") :?> Storyboard
        animation.SeekAlignedToLastTick(countdown, TimeSpan.FromSeconds(1.), TimeSeekOrigin.BeginTime)
        check (text.Text = "8") "Countdown animation did not advance."
    stopNotification()
    check (countdowns |> Array.forall (fun countdown -> not countdown.IsVisible)) "Countdown did not hide."

    startNotification()
    workTimer.Stop()
    let elapsedBeforeRestart = workTimer.Elapsed
    restart.RaiseEvent(RoutedEventArgs(MenuItem.ClickEvent))
    check workTimer.IsRunning "Restart did not start the work timer."
    check (workTimer.Elapsed < elapsedBeforeRestart) "Restart did not reset elapsed work time."
    check (countdowns |> Array.forall (fun countdown -> not countdown.IsVisible)) "Restart did not clear the notification."

    window.UpdateLayout()
    let bitmap = RenderTargetBitmap(int window.ActualWidth, int window.ActualHeight, 96., 96., PixelFormats.Pbgra32)
    bitmap.Render(window)
    let stride = bitmap.PixelWidth * 4
    let pixels = Array.zeroCreate<byte> (stride * bitmap.PixelHeight)
    bitmap.CopyPixels(pixels, stride, 0)
    check (pixels |> Array.exists ((<>) 0uy)) "Window rendered blank."
    let encoder = PngBitmapEncoder()
    encoder.Frames.Add(BitmapFrame.Create(bitmap))
    use output = File.Create(Path.Combine(AppContext.BaseDirectory, "smoke-test.png"))
    encoder.Save(output)

    quit.RaiseEvent(RoutedEventArgs(MenuItem.ClickEvent))