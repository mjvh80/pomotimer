module Settings

open System
open System.IO
open System.Text.Json

[<CLIMutable>]
type Preferences = { WorkDurationMinutes: int }

let defaultDuration = 40
let minimumDuration = 1
let maximumDuration = 240

let filePath =
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PomodoroTimer", "settings.json")

let isValidDuration duration = duration >= minimumDuration && duration <= maximumDuration

let tryParseDuration text =
    match Int32.TryParse(text: string) with
    | true, duration when isValidDuration duration -> Some duration
    | _ -> None

let load path =
    try
        let preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path))
        if not (obj.ReferenceEquals(preferences, null)) && isValidDuration preferences.WorkDurationMinutes then
            preferences.WorkDurationMinutes
        else
            defaultDuration
    with
    | :? IOException
    | :? UnauthorizedAccessException
    | :? JsonException -> defaultDuration

let save path duration =
    if not (isValidDuration duration) then
        Error $"Enter a whole number between {minimumDuration} and {maximumDuration}."
    else
        let temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp"
        try
            try
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))) |> ignore
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize({ WorkDurationMinutes = duration }))
                File.Move(temporaryPath, path, true)
                Ok ()
            finally
                if File.Exists(temporaryPath) then File.Delete(temporaryPath)
        with
        | :? IOException as error -> Error $"Could not save settings: {error.Message}"
        | :? UnauthorizedAccessException as error -> Error $"Could not save settings: {error.Message}"