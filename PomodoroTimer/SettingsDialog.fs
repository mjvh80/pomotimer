module SettingsDialog

open System.Windows
open System.Windows.Controls
open System.Windows.Controls.Primitives
open System.Windows.Input

let configure (dialog: Window) (preferences: Settings.Preferences) savePreferences =
    let duration = preferences.WorkDurationMinutes
    let input = dialog.FindName("DurationInput") :?> TextBox
    let snoozeInput = dialog.FindName("SnoozeInput") :?> TextBox
    snoozeInput.Text <- string preferences.SnoozeMinutes
    let scale = dialog.FindName("TimerScaleSlider") :?> Slider
    scale.Minimum <- float Settings.minimumScale
    scale.Maximum <- float Settings.maximumScale
    scale.Value <- float preferences.TimerScalePercent
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
    let stepSnooze amount =
        let current = Settings.tryParseSnooze snoozeInput.Text |> Option.defaultValue preferences.SnoozeMinutes
        snoozeInput.Text <- string (max Settings.minimumSnooze (min Settings.maximumSnooze (current + amount)))
    (dialog.FindName("DecreaseSnoozeButton") :?> RepeatButton).Click.Add(fun _ -> stepSnooze -1)
    (dialog.FindName("IncreaseSnoozeButton") :?> RepeatButton).Click.Add(fun _ -> stepSnooze 1)
    snoozeInput.PreviewKeyDown.Add(fun eventArgs ->
        match eventArgs.Key with
        | Key.Up -> stepSnooze 1; eventArgs.Handled <- true
        | Key.Down -> stepSnooze -1; eventArgs.Handled <- true
        | _ -> ())
    snoozeInput.TextChanged.Add(fun _ -> errorText.Visibility <- Visibility.Collapsed)
    (dialog.FindName("CancelSettingsButton") :?> Button).Click.Add(fun _ -> dialog.DialogResult <- false)
    saveButton.Click.Add(fun _ ->
        let result =
            match Settings.tryParseDuration input.Text, Settings.tryParseSnooze snoozeInput.Text with
            | Some value, Some snooze -> savePreferences { preferences with WorkDurationMinutes = value; TimerScalePercent = int scale.Value; SnoozeMinutes = snooze }
            | None, _ -> Error $"Enter a whole number between {Settings.minimumDuration} and {Settings.maximumDuration}."
            | _, None -> Error $"Enter a snooze interval between {Settings.minimumSnooze} and {Settings.maximumSnooze} minutes."
        match result with
        | Ok () -> dialog.DialogResult <- true
        | Error message ->
            errorText.Text <- message
            errorText.Visibility <- Visibility.Visible
            input.Focus() |> ignore)
    dialog.ContentRendered.Add(fun _ ->
        input.Focus() |> ignore
        input.SelectAll())