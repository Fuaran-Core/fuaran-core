/// The custard-leg fixture's Fable runner (Phase 641). Under Node an expectation that does not hold
/// is an uncaught exception, so the process exits non-zero and the leg is red.
module LegFableMain

let private expect (what: string) (holds: bool) =
    if not holds then
        failwithf "the extracted oracle disagrees under Fable: %s" what

expect "LegPulse.add_two 40 = 42" (LegPulse.add_two 40I = 42I)
expect "LegCustard.sum [1; 2; 3] = 6" (LegCustard.sum [ 1I; 2I; 3I ] = 6I)
printfn "fable oracle: green"
