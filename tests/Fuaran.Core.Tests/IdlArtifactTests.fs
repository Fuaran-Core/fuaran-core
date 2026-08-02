module Fuaran.Core.Tests.IdlArtifactTests

open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Tests.UiIdl

// ---------------------------------------------------------------------------
// Phase 696 — the `idl.json` stale-artifact guard.
//
// Mirrors the stale-SCHEMA guard the UI tier runs over `schema.json`: the
// committed artifact must be byte-identical to a fresh emission, so a vocabulary
// edit that skips regeneration fails a test rather than silently serving a spec
// the format no longer matches.
//
// Why the guard lives HERE and not beside the stale-schema guard in the UI tier's
// `SchemaConformanceTests.fs`, which is where the phase's goal points: a guard
// must be able to compute the fresh emission, and only this project can. The
// encoder is in `Fuaran.Core.Idl` (`IsPackable=false` — it ships in no package)
// and the vocabulary is `UiIdl.uiIdl` in this test project, so neither is
// reachable from any repo that consumes Core as packages. The UI tier owns the
// `manifest.json` pointer to the artifact; this project owns the artifact itself.
//
// The corpus is a separate repo cloned alongside, so the guard SKIPS when it is
// absent — the same posture `IdlUiTests` / `IdlSpikeTests` already take for their
// live-corpus drift guards. A standalone `Fuaran-Core` clone stays green.
// ---------------------------------------------------------------------------

/// The regeneration command, named in every failure message so a red guard is
/// self-servicing (the `SchemaConformanceTests` pattern).
[<Literal>]
let regenCommand =
    "dotnet run --project tests/Fuaran.Core.Tests -- --emit-idl ..\\Fuaran-UI\\wire-format-fixtures"

[<Literal>]
let artifactFileName = "idl.json"

/// Locate the corpus ROOT (the directory holding `manifest.json`) by climbing from
/// the CWD / test binary. Both layouts the sibling guards accept are tried: the
/// workspace shape (`Fuaran-UI/wire-format-fixtures`) and a flat checkout.
let tryFindCorpusRoot () : string option =
    let candidates (root: string) =
        [ Path.Combine(root, "Fuaran-UI", "wire-format-fixtures")
          Path.Combine(root, "wire-format-fixtures") ]

    let rec climb (dir: string) (budget: int) =
        if budget < 0 || isNull dir then
            None
        else
            match
                candidates dir
                |> List.tryFind (fun d -> File.Exists(Path.Combine(d, "manifest.json")))
            with
            | Some d -> Some d
            | None ->
                match Directory.GetParent dir with
                | null -> None
                | parent -> climb parent.FullName (budget - 1)

    [ Directory.GetCurrentDirectory(); System.AppContext.BaseDirectory ]
    |> List.tryPick (fun start -> climb start 12)

/// Write `idl.json` into the corpus clone at `outputDir`. The `--emit-idl` leg.
let emit (outputDir: string) : unit =
    let path = Path.Combine(outputDir, artifactFileName)
    let text = Artifact.render uiIdl
    File.WriteAllText(path, text)

    printfn
        "Emitted %s (%d kinds, %d ops, %d unions, %d enums, %d records, %d defaults) to %s"
        artifactFileName
        uiIdl.Kinds.Length
        uiIdl.Ops.Length
        uiIdl.Unions.Length
        uiIdl.Enums.Length
        uiIdl.Records.Length
        uiIdl.Defaults.Length
        outputDir

[<Tests>]
let staleIdlArtifactGuard =
    testList
        "Fuaran.Core.Idl.Artifact — stale-artifact guard"
        [ testCase "committed idl.json is byte-identical to a fresh emission" (fun () ->
              match tryFindCorpusRoot () with
              | None -> skiptest "wire-format-fixtures not checked out alongside — drift guard skipped"
              | Some root ->
                  let path = Path.Combine(root, artifactFileName)

                  if not (File.Exists path) then
                      failtestf
                          "%s is missing from the corpus at %s — generate it with `%s`"
                          artifactFileName
                          root
                          regenCommand

                  Expect.equal
                      (File.ReadAllText path)
                      (Artifact.render uiIdl)
                      (sprintf
                          "wire-format-fixtures/%s is stale relative to the IDL vocabulary (UiIdl.uiIdl) — regenerate with `%s`"
                          artifactFileName
                          regenCommand))

          // The artifact's whole promise is that a non-F# consumer can read the
          // vocabulary from it ALONE. These pin the promise rather than the bytes:
          // a rendering that parsed but carried an empty family would satisfy the
          // byte guard above forever, because it would regenerate identically.
          testCase "the emission is parseable canonical JSON carrying every family" (fun () ->
              let text = Artifact.render uiIdl

              match Fuaran.Core.Json.parse text with
              | Error m -> failtestf "idl.json is not parseable JSON: %s" m
              | Ok(JObj fields) ->
                  let get name =
                      fields |> List.tryFind (fun (n, _) -> n = name) |> Option.map snd

                  let count name =
                      match get name with
                      | Some(JArr xs) -> xs.Length
                      | Some _ -> failtestf "'%s' is not an array" name
                      | None -> failtestf "'%s' is missing from idl.json" name

                  Expect.equal (count "kinds") uiIdl.Kinds.Length "every kind is present"
                  Expect.equal (count "unions") uiIdl.Unions.Length "every union is present"
                  Expect.equal (count "enums") uiIdl.Enums.Length "every enum is present"
                  Expect.equal (count "records") uiIdl.Records.Length "every record is present"
                  Expect.equal (count "defaults") uiIdl.Defaults.Length "every default is present"
                  Expect.equal (count "nodeFields") uiIdl.NodeFields.Length "the node envelope is present"

                  match get "version" with
                  | Some(JInt v) -> Expect.equal v Artifact.version "the encoding version is stamped"
                  | _ -> failtest "'version' is missing or not an int"
              | Ok _ -> failtest "idl.json is not a JSON object")

          // Enum vocabularies are one of the four things acceptance names a third
          // party must be able to enumerate, and the only one whose payload is a
          // bare string list — so a mis-rendered enum would be invisible above.
          // Phase 707: `cases` is the WIRE contract — the strings a non-F# consumer
          // must accept — which is `WireCases`, not the host case names. For an enum
          // that declares a mapping the two differ, and the host identifiers appear
          // separately under `hostCases` (pinned by the sibling case below).
          testCase "enum vocabularies carry their wire case lists verbatim" (fun () ->
              let expected =
                  uiIdl.Enums
                  |> List.sortWith (fun a b -> System.String.CompareOrdinal(a.Name, b.Name))
                  |> List.map (fun e -> e.Name, e.WireCases)

              match Artifact.json uiIdl with
              | JObj fields ->
                  match fields |> List.tryFind (fun (n, _) -> n = "enums") |> Option.map snd with
                  | Some(JArr entries) ->
                      let actual =
                          entries
                          |> List.map (fun entry ->
                              match entry with
                              | JObj ef ->
                                  let str name =
                                      match ef |> List.tryFind (fun (n, _) -> n = name) |> Option.map snd with
                                      | Some(JStr s) -> s
                                      | _ -> failtestf "enum entry has no string '%s'" name

                                  let cases =
                                      match ef |> List.tryFind (fun (n, _) -> n = "cases") |> Option.map snd with
                                      | Some(JArr cs) ->
                                          cs
                                          |> List.map (function
                                              | JStr s -> s
                                              | _ -> failtest "enum case is not a string")
                                      | _ -> failtest "enum entry has no 'cases' array"

                                  str "name", cases
                              | _ -> failtest "enum entry is not an object")

                      Expect.equal actual expected "every enum name maps to its authored case list, in order"
                  | _ -> failtest "'enums' is missing or not an array"
              | _ -> failtest "the artifact is not a JSON object")

          // The other half of the Phase 707 promise: an enum whose host case names
          // differ from its wire strings publishes BOTH, and one whose don't
          // publishes only the wire list — so the artefact of an unmapped
          // vocabulary is byte-for-byte what it always was.
          testCase "a wire-mapped enum publishes its host case names, an unmapped one does not" (fun () ->
              let entries =
                  match Artifact.json uiIdl with
                  | JObj fields ->
                      match fields |> List.tryFind (fun (n, _) -> n = "enums") |> Option.map snd with
                      | Some(JArr es) -> es
                      | _ -> failtest "'enums' is missing or not an array"
                  | _ -> failtest "the artifact is not a JSON object"

              let hostCasesOf (name: string) =
                  entries
                  |> List.tryPick (function
                      | JObj ef ->
                          match ef |> List.tryFind (fun (n, _) -> n = "name") |> Option.map snd with
                          | Some(JStr n) when n = name ->
                              Some(
                                  ef
                                  |> List.tryFind (fun (k, _) -> k = "hostCases")
                                  |> Option.map (fun (_, v) ->
                                      match v with
                                      | JArr cs ->
                                          cs
                                          |> List.map (function
                                              | JStr s -> s
                                              | _ -> failtest "hostCases entry is not a string")
                                      | _ -> failtest "'hostCases' is not an array")
                              )
                          | _ -> None
                      | _ -> None)

              // `LiveRegionKind` is the vocabulary's one wire-mapped enum: closed set,
              // lower-case wire strings, F# identifiers that cannot spell them.
              Expect.equal
                  (hostCasesOf "LiveRegionKind")
                  (Some(Some [ "Polite"; "Assertive"; "Off" ]))
                  "the mapped enum publishes its host identifiers"

              for e in uiIdl.Enums |> List.filter (fun e -> List.isEmpty e.Wires) do
                  Expect.equal
                      (hostCasesOf e.Name)
                      (Some None)
                      (sprintf "'%s' is unmapped and adds no hostCases" e.Name)) ]
