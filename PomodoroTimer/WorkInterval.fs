module WorkInterval

open System

let deadlineAfterDurationChange durationMinutes (elapsed: TimeSpan) =
    let duration = TimeSpan.FromMinutes(float durationMinutes)
    if elapsed >= duration then elapsed + TimeSpan.FromSeconds(10.) else duration