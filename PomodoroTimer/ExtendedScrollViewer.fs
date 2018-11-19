namespace Controls

open System
open System.Windows


type CustomEvent<'t>(add, remove) =
      interface IEvent<'t> with
         member this.Subscribe _ = null :> IDisposable
         member this.AddHandler handler = add(handler)
         member this.RemoveHandler handler = remove(handler)

// Extends a WPF ScrollViewer with a "Restart" routed event used for triggering an animation.
type public ExtendedScrollViewer() as this =
   inherit System.Windows.Controls.ScrollViewer()

   static let _restartEvent = EventManager.RegisterRoutedEvent("Restart", RoutingStrategy.Direct, typeof<RoutedEventHandler>, typeof<ExtendedScrollViewer>)

   static let _horPosProp = 
      DependencyProperty.Register("HorizontalScrollPosition", typeof<Double>, typeof<ExtendedScrollViewer>, 
         new PropertyMetadata(
            new PropertyChangedCallback(
               fun depObj -> 
                  fun args -> (let scroller = depObj :?> System.Windows.Controls.ScrollViewer
                               scroller.ScrollToHorizontalOffset(args.NewValue :?> Double)))))
         

   let add h = this.AddHandler(ExtendedScrollViewer.RestartEvent, h)
   let remove h = this.RemoveHandler(ExtendedScrollViewer.RestartEvent, h)

   static member public RestartEvent: RoutedEvent = _restartEvent

   member public this.OnRestart() =
      this.RaiseEvent(new RoutedEventArgs(_restartEvent))

   // This isn't needed really, anyway.
   [<CLIEvent>]
   member public this.Restart = (new CustomEvent<RoutedEventHandler>(add, remove) :> IEvent<RoutedEventHandler>)

   // Effectively the same as HorizontalOffset to allow it to be animatable.
   member public this.HorizontalScrollPosition 
      with get() = this.HorizontalOffset
      and set (value: Double) = this.SetValue(_horPosProp, value)
