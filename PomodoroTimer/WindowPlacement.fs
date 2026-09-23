module WindowPlacement

open System
open System.IO
open System.Text.Json
open System.Windows

type Monitor = { DeviceName: string; WorkingArea: Rect; IsPrimary: bool }

[<CLIMutable>]
type Placement = { MonitorName: string; OffsetX: float; OffsetY: float; Left: float; Top: float }

let private isValid (placement: Placement) =
    not (obj.ReferenceEquals(placement, null)) &&
    not (String.IsNullOrWhiteSpace(placement.MonitorName)) &&
    [placement.OffsetX; placement.OffsetY; placement.Left; placement.Top] |> List.forall Double.IsFinite

let private overlap (bounds: Rect) (monitor: Monitor) =
    let intersection = Rect.Intersect(bounds, monitor.WorkingArea)
    if intersection.IsEmpty then 0. else intersection.Width * intersection.Height

let private chooseMonitor bounds (monitors: Monitor array) =
    if monitors.Length = 0 then None
    else
        let nearest = monitors |> Array.maxBy (overlap bounds)
        if overlap bounds nearest > 0. then Some nearest
        else monitors |> Array.tryFind (fun monitor -> monitor.IsPrimary) |> Option.orElse (Some monitors[0])

let private clamp (area: Rect) (size: Size) (position: Point) =
    Point(Math.Clamp(position.X, area.Left, max area.Left (area.Right - size.Width)),
          Math.Clamp(position.Y, area.Top, max area.Top (area.Bottom - size.Height)))

let capture (monitors: Monitor array) (bounds: Rect) =
    chooseMonitor bounds monitors
    |> Option.map (fun monitor ->
        { MonitorName = monitor.DeviceName
          OffsetX = bounds.Left - monitor.WorkingArea.Left
          OffsetY = bounds.Top - monitor.WorkingArea.Top
          Left = bounds.Left; Top = bounds.Top })

let restore (monitors: Monitor array) (size: Size) placement =
    if not (isValid placement) then None
    else
        match monitors |> Array.tryFind (fun monitor -> monitor.DeviceName = placement.MonitorName) with
        | Some monitor ->
            Some (clamp monitor.WorkingArea size (Point(monitor.WorkingArea.Left + placement.OffsetX, monitor.WorkingArea.Top + placement.OffsetY)))
        | None ->
            chooseMonitor (Rect(Point(placement.Left, placement.Top), size)) monitors
            |> Option.map (fun monitor -> clamp monitor.WorkingArea size (Point(placement.Left, placement.Top)))

let load path =
    try
        let placement = JsonSerializer.Deserialize<Placement>(File.ReadAllText(path))
        if isValid placement then Some placement else None
    with
    | :? IOException
    | :? UnauthorizedAccessException
    | :? JsonException -> None

let save path placement =
    if not (isValid placement) then Error "Invalid window position."
    else
        let temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp"
        try
            try
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))) |> ignore
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(placement))
                File.Move(temporaryPath, path, true)
                Ok ()
            finally
                if File.Exists(temporaryPath) then File.Delete(temporaryPath)
        with
        | :? IOException as error -> Error error.Message
        | :? UnauthorizedAccessException as error -> Error error.Message