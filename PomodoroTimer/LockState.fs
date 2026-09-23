module LockState

type Source = Keyboard | Session
type Transition = Unchanged | BreakStarted | BreakEnded

type Tracker() =
    let mutable sources = Set.empty<Source>
    member _.IsLocked = not sources.IsEmpty
    member _.Set(source, locked) =
        let previous = sources
        sources <- if locked then sources.Add(source) else sources.Remove(source)
        if previous.IsEmpty && not sources.IsEmpty then BreakStarted
        elif not previous.IsEmpty && sources.IsEmpty then BreakEnded
        else Unchanged