module Settings

open System
open System.IO
open System.Text.Json

[<CLIMutable>]
type Preferences = { WorkDurationMinutes: int; TimerScalePercent: int; SnoozeMinutes: int }

let defaultDuration = 40
let minimumDuration = 1
let maximumDuration = 240
let defaultScale = 100
let minimumScale = 50
let maximumScale = 200
let defaultSnooze = 5
let minimumSnooze = 1
let maximumSnooze = 60
let defaults = { WorkDurationMinutes = defaultDuration; TimerScalePercent = defaultScale; SnoozeMinutes = defaultSnooze }

let filePath =
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PomodoroTimer", "settings.json")

let isValidDuration duration = duration >= minimumDuration && duration <= maximumDuration
let isValidScale scale = scale >= minimumScale && scale <= maximumScale
let isValidSnooze minutes = minutes >= minimumSnooze && minutes <= maximumSnooze

let tryParseSnooze text =
    match Int32.TryParse(text: string) with
    | true, minutes when isValidSnooze minutes -> Some minutes
    | _ -> None

let tryParseDuration text =
    match Int32.TryParse(text: string) with
    | true, duration when isValidDuration duration -> Some duration
    | _ -> None

let load path =
    try
        let preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path))
        if not (obj.ReferenceEquals(preferences, null)) then
            { WorkDurationMinutes = if isValidDuration preferences.WorkDurationMinutes then preferences.WorkDurationMinutes else defaultDuration
              TimerScalePercent = if isValidScale preferences.TimerScalePercent then preferences.TimerScalePercent else defaultScale
              SnoozeMinutes = if isValidSnooze preferences.SnoozeMinutes then preferences.SnoozeMinutes else defaultSnooze }
        else
            defaults
    with
    | :? IOException
    | :? UnauthorizedAccessException
    | :? JsonException -> defaults

let save path preferences =
    if not (isValidDuration preferences.WorkDurationMinutes) then
        Error $"Enter a whole number between {minimumDuration} and {maximumDuration}."
    elif not (isValidScale preferences.TimerScalePercent) then
        Error $"Choose a timer size between {minimumScale}%% and {maximumScale}%%."
    elif not (isValidSnooze preferences.SnoozeMinutes) then
        Error $"Enter a snooze interval between {minimumSnooze} and {maximumSnooze} minutes."
    else
        let temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp"
        try
            try
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))) |> ignore
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences))
                File.Move(temporaryPath, path, true)
                Ok ()
            finally
                if File.Exists(temporaryPath) then File.Delete(temporaryPath)
        with
        | :? IOException as error -> Error $"Could not save settings: {error.Message}"
        | :? UnauthorizedAccessException as error -> Error $"Could not save settings: {error.Message}"