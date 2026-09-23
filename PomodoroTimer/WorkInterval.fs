module WorkInterval

open System

let shouldResetAfterBreak (elapsed: TimeSpan) = elapsed >= TimeSpan.FromMinutes(5.)

let snoozeDeadline minutes (elapsed: TimeSpan) = elapsed + TimeSpan.FromMinutes(float minutes)

let deadlineAfterDurationChange durationMinutes (elapsed: TimeSpan) =
    let duration = TimeSpan.FromMinutes(float durationMinutes)
    if elapsed >= duration then elapsed + TimeSpan.FromSeconds(10.) else duration