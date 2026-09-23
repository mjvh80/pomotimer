module PausePreview

type Options = { WorkMinutes: int; BreakSeconds: int }

let usage = "Use --preview-pause [--work-minutes 0..240] [--break-seconds 0..86400]."

let parse (arguments: string array) =
    if not (Array.contains "--preview-pause" arguments) then
        if arguments |> Array.exists (fun argument -> argument = "--work-minutes" || argument = "--break-seconds") then
            Error usage
        else Ok None
    else
        let rec read remaining seen options =
            match remaining with
            | [] -> Ok (Some options)
            | "--smoke-test" :: rest -> read rest seen options
            | "--preview-pause" :: rest when not (Set.contains "--preview-pause" seen) ->
                read rest (Set.add "--preview-pause" seen) options
            | name :: value :: rest when (name = "--work-minutes" || name = "--break-seconds") && not (Set.contains name seen) ->
                match System.Int32.TryParse(value) with
                | true, number when number >= 0 && number <= (if name = "--work-minutes" then 240 else 86400) ->
                    let updated =
                        if name = "--work-minutes" then { options with WorkMinutes = number }
                        else { options with BreakSeconds = number }
                    read rest (Set.add name seen) updated
                | _ -> Error usage
            | _ -> Error usage
        read (Array.toList arguments) Set.empty { WorkMinutes = 20; BreakSeconds = 0 }