/// The oracle project's `Compile` list (Phase 532; held since Phase 504): GENERATED from the roster
/// (`proofs/modules.json`, read by `ProofLadder.parseRoster`, the one reading — D74) by the tool's
/// `render oracle-project`. Every extracted or shared module, in check order, which is an order in which
/// each module follows every module it opens. The hand-written runtime floor (`Prims.fs`,
/// `FStar_Pervasives_Native.fs`) sits above the generated region and is not the roster's.
module Fuaran.Core.Tests.OracleProject

open System
open Fuaran.Core.Tests.ProofLadder

/// The project file, repository-relative.
[<Literal>]
let Path = "proofs/oracle/Fuaran.Core.Proofs.Oracle.fsproj"

/// The roster file it is generated from, repository-relative.
[<Literal>]
let RosterPath = "proofs/modules.json"

/// The line that opens the generated region. (An XML comment may not hold `--`, so it names the verb,
/// not the whole command.)
let beginMarker =
    sprintf
        "    <!-- roster:begin - GENERATED from ../modules.json by the tool's `render oracle-project` (%s): every extracted and shared module, in check order. Do not hand-edit. -->"
        Tool.Project

/// The line that closes it.
let endMarker = "    <!-- roster:end -->"

/// The generated items, one per extracted or shared module, in roster order.
let items (roster: RosterEntry list) : string list =
    roster
    |> List.filter isExtracted
    |> List.map (fun e -> sprintf "    <Compile Include=\"%s.fs\" />" e.Module)

/// The project text with its generated region rendered from `modulesJson`. LF throughout.
let render (modulesJson: string) (projectText: string) : Result<string, string> =
    match parseRoster modulesJson with
    | Error errors -> Error(sprintf "%s is not a readable roster: %s" RosterPath (String.concat "; " errors))
    | Ok roster ->
        let lines = projectText.Replace("\r\n", "\n").Split '\n' |> List.ofArray
        let b = lines |> List.tryFindIndex (fun l -> l = beginMarker)
        let e = lines |> List.tryFindIndex (fun l -> l = endMarker)

        match b, e with
        | Some b, Some e when b < e -> Ok(String.concat "\n" (lines[..b] @ items roster @ lines[e..]))
        | _ ->
            Error(
                sprintf
                    "%s has no generated region: the line `%s`, then the line `%s`, are what `render oracle-project` rewrites between"
                    Path
                    beginMarker
                    endMarker
            )
