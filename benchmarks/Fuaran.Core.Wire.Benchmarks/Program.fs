/// The wire benchmark's entry point (Phase 364). One program for both hosts:
///
///   dotnet <Release dll> [runs]            the .NET table
///   node <Fable output>/Program.js [runs]  the node table
///   ... --fingerprints                     every case's output fingerprint, for the cross-host check
///
/// `runs` is the measured samples per case (default 10) after two warm-up calls. ../run.ps1 runs both
/// hosts, compares their fingerprints before anything is timed, and prints the node / .NET ratios.
module Fuaran.Core.WireBench.Program

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    | "--fingerprints" :: _ ->
        Harness.fingerprints () |> List.iter (printfn "%s")
        0
    | rest ->
        let runs =
            match rest with
            | a :: _ -> int a
            | [] -> 10

        if runs < 1 then
            failwith "runs must be at least 1"

        Harness.measure runs |> Harness.report |> List.iter (printfn "%s")
        0
