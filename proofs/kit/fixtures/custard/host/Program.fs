/// The custard-leg fixture's .NET oracle host (Phase 641). The kit's host step runs it with a
/// filter argument, which it ignores: every expectation runs, and the exit code is the verdict.
module LegHost

let mutable private failures = 0

let private expect (what: string) (holds: bool) =
    if not holds then
        failures <- failures + 1
        eprintfn "the extracted oracle disagrees on .NET: %s" what

[<EntryPoint>]
let main _ =
    let pair: LegCustard.pair = { first = 1I; second = 2I }
    let swapped = LegCustard.swap pair
    expect "LegPulse.add_two 40 = 42" (LegPulse.add_two 40I = 42I)
    expect "LegCustard.sum [1; 2; 3] = 6" (LegCustard.sum [ 1I; 2I; 3I ] = 6I)
    expect "LegCustard.swap swaps" (swapped.first = 2I && swapped.second = 1I)

    if failures = 0 then
        printfn "dotnet oracle: green"

    failures
