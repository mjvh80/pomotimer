module SettingsDialog

open System.Windows
open System.Windows.Controls
open System.Windows.Controls.Primitives
open System.Windows.Input

let configure (dialog: Window) duration saveDuration =
    let input = dialog.FindName("DurationInput") :?> TextBox
    let errorText = dialog.FindName("ValidationError") :?> TextBlock
    let saveButton = dialog.FindName("SaveSettingsButton") :?> Button
    input.Text <- string duration

    let step amount =
        let current = Settings.tryParseDuration input.Text |> Option.defaultValue duration
        input.Text <- string (max Settings.minimumDuration (min Settings.maximumDuration (current + amount)))

    (dialog.FindName("DecreaseDurationButton") :?> RepeatButton).Click.Add(fun _ -> step -1)
    (dialog.FindName("IncreaseDurationButton") :?> RepeatButton).Click.Add(fun _ -> step 1)
    input.PreviewKeyDown.Add(fun eventArgs ->
        match eventArgs.Key with
        | Key.Up -> step 1; eventArgs.Handled <- true
        | Key.Down -> step -1; eventArgs.Handled <- true
        | _ -> ())
    input.TextChanged.Add(fun _ -> errorText.Visibility <- Visibility.Collapsed)
    (dialog.FindName("CancelSettingsButton") :?> Button).Click.Add(fun _ -> dialog.DialogResult <- false)
    saveButton.Click.Add(fun _ ->
        let result =
            match Settings.tryParseDuration input.Text with
            | Some value -> saveDuration value
            | None -> Error $"Enter a whole number between {Settings.minimumDuration} and {Settings.maximumDuration}."
        match result with
        | Ok () -> dialog.DialogResult <- true
        | Error message ->
            errorText.Text <- message
            errorText.Visibility <- Visibility.Visible
            input.Focus() |> ignore)
    dialog.ContentRendered.Add(fun _ ->
        input.Focus() |> ignore
        input.SelectAll())