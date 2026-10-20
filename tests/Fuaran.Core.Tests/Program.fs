module Fuaran.Core.Tests.Program

open System
open System.IO
open Expecto

/// Phase 400 - the skip report. Expecto already counts the legs a missing prerequisite ignores; this
/// names them, one family (the test list's first name segment, which Expecto joins with a dot) at a time, so a green run says how much
/// of the suite it did not read. The list is printed after the summary; when `FUARAN_CORE_SKIP_REPORT`
/// names a file it is also written there as one line, which is how `verify.ps1` repeats it on the gate's
/// final line without parsing the runner's output.
module private SkipReport =
    let private skipped = System.Collections.Concurrent.ConcurrentBag<string>()

    let private family (name: string) : string =
        match name.Split([| '.' |], StringSplitOptions.RemoveEmptyEntries) with
        | [||] -> name
        | parts -> parts.[0].Trim()

    /// The default printer, additionally recording each ignored test's name.
    let printer: Impl.TestPrinters =
        { Impl.TestPrinters.defaultPrinter with
            ignored =
                fun name reason ->
                    skipped.Add name
                    Impl.TestPrinters.defaultPrinter.ignored name reason }

    /// "N skipped across M families: A (n), B (m)", or "0 skipped".
    let summary () : string =
        let names = skipped.ToArray()

        if names.Length = 0 then
            "0 skipped"
        else
            let families =
                names
                |> Array.countBy family
                |> Array.sortBy fst
                |> Array.map (fun (f, n) -> sprintf "%s (%d)" f n)

            sprintf "%d skipped across %d families: %s" names.Length families.Length (String.Join(", ", families))

    /// Print the summary, and write it to the file the launcher named, if any.
    let report () : unit =
        let line = summary ()
        printfn "Skipped: %s" line

        match Environment.GetEnvironmentVariable "FUARAN_CORE_SKIP_REPORT" with
        | null
        | "" -> ()
        | path -> File.WriteAllText(path, line)

/// The test runner, and only that (Phase 532): it has no verb. The registry's generated artefacts are
/// written by the tool (`dotnet run --project tools/Fuaran.Core.Tools -- render <family>`), which also
/// answers the proof leg's cone (`proof-cone`); the suite holds what the tool writes, and rewrites a held
/// projection only when an approval switch admits it (Approval.fs).
[<EntryPoint>]
let main argv =
    let code = runTestsInAssemblyWithCLIArgs [ Printer SkipReport.printer ] argv
    SkipReport.report ()
    code
