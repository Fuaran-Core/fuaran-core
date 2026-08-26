module Fuaran.Core.Tests.IdlSchemaTests

open System.IO
open System.Text.Json
open Expecto
open Json.Schema
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Tests.UiIdl

// ---------------------------------------------------------------------------
// Phase 697 — certify the IDL's JSON-schema leg against the wire corpus.
//
// `Gen.jsonSchema` had only ever been smoke-tested on the 8-kind `miniIdl`. A
// generated schema that has never met the corpus is a FOURTH MIRROR of the wire
// format, not a projection of it — it can drift from the format silently, and
// the smoke test cannot tell you, because a schema that is wrong in the same way
// twice still round-trips its own toy input.
//
// So this suite runs the real thing: the schema generated from the full `uiIdl`,
// evaluated by an off-the-shelf Draft 2020-12 validator (JsonSchema.Net, the same
// implementation the UI tier uses for the hand-written `schema.json`) against
// every fixture in the corpus. Passing means the leg is a genuine drop-in for an
// external validator, which is the only claim worth making about a schema.
//
// The three defects the phase went in naming, and what happened to each:
//
//   * **Dangling `$ref`s.** `idl.Records` were absent from the `$defs` assembly
//     while every `TRecord` slot emitted `$ref: #/$defs/<name>`. A strict
//     validator treats an unresolvable `$ref` as an ERROR, not a permissive skip,
//     so the leg could not have certified at all. Records now join the assembly,
//     via `recordSchema` — no `$type` const, which is exactly what distinguishes
//     a record from a union case on the wire. Pinned below by walking the emitted
//     document and resolving every `$ref` it contains.
//
//   * **No transparent-union reflection.** `TextSource.Literal` is on the wire
//     BARE — `"x"`, not `{"$type":"Literal","text":"x"}`. Without reflecting that,
//     the schema rejected the canonical form of every literal string in the
//     corpus, which is most fixtures. `unionDef` now emits the bare form beside
//     the tagged branches.
//
//   * **`additionalProperties: false` everywhere.** Resolved by ALIGNING with the
//     format rather than by exempting the leg: the decoder tolerates unknown keys
//     (`WIRE_FORMAT.md` §2.1 rule 2, field-lookup-by-name) and the published
//     `schema.json` says so in its own header. A schema stricter than the format
//     rejects payloads the format accepts — and worse, it breaks the forward
//     compatibility that tolerance exists for, since an older host validating a
//     newer producer's output would fail on a key it has not learned yet.
//
// The suite reads the emitted corpus, never the F# fixture VALUES, so what it
// asserts is exactly what a third party running the schema would see.
// ---------------------------------------------------------------------------

let private schemaText = Gen.jsonSchema uiIdl

let private schema = JsonSchema.FromText schemaText

/// Evaluate a wire payload. `None` ⇒ not parseable JSON (a rejection in its own
/// right); `Some isValid` ⇒ parsed and schema-evaluated.
let private validate (wire: string) : bool option =
    // `JsonDocument.Parse` defaults to a 64-level depth cap, which is BELOW the 256
    // WIRE_FORMAT §21 pins — so the §21 at-the-limit node fixture (72 JSON levels)
    // reported "not parseable JSON" and failed the certification on the reader's own
    // cap rather than on anything the schema said. Parse at the format's own bound.
    let parseOptions = JsonDocumentOptions(MaxDepth = 256)

    let parsed =
        try
            Some(JsonDocument.Parse(wire, parseOptions))
        with _ ->
            None

    match parsed with
    | None -> None
    | Some doc ->
        use doc = doc
        Some(schema.Evaluate(doc.RootElement, EvaluationOptions()).IsValid)

let private corpusDir (family: string) =
    IdlArtifactTests.tryFindCorpusRoot ()
    |> Option.map (fun root -> Path.Combine(root, family))
    |> Option.filter Directory.Exists

let private fixtures (family: string) =
    match corpusDir family with
    | None -> []
    | Some dir ->
        Directory.GetFiles(dir, "*.json")
        |> Array.filter (fun p -> not (Path.GetFileName(p).EndsWith ".expected.json"))
        |> Array.sortWith (fun a b -> System.String.CompareOrdinal(a, b))
        |> List.ofArray

/// Every `$ref` target named anywhere in the emitted document.
let rec private refsIn (j: JVal) : string list =
    match j with
    | JObj fields ->
        [ for name, v in fields do
              match name, v with
              | "$ref", JStr target -> target
              | _ -> yield! refsIn v ]
    | JArr items -> items |> List.collect refsIn
    | _ -> []

let private emittedDoc =
    match Json.parse schemaText with
    | Ok j -> j
    | Error e -> failwithf "the generated schema is not parseable JSON: %s" e

let private definedNames =
    match emittedDoc with
    | JObj fields ->
        match fields |> List.tryFind (fun (n, _) -> n = "$defs") |> Option.map snd with
        | Some(JObj defs) -> defs |> List.map fst |> Set.ofList
        | _ -> failwith "the generated schema has no `$defs` object"
    | _ -> failwith "the generated schema is not a JSON object"

[<Tests>]
let tests =
    testList
        "Phase 697 — IDL schema leg, corpus-certified"
        [

          // ── structural integrity of the emitted document ─────────────────

          testCase "every `$ref` resolves — no dangling reference" (fun _ ->
              let dangling =
                  refsIn emittedDoc
                  |> List.distinct
                  |> List.filter (fun r -> r.StartsWith "#/$defs/")
                  |> List.map (fun r -> r.Substring "#/$defs/".Length)
                  |> List.filter (fun n -> not (definedNames.Contains n))

              Expect.isEmpty
                  dangling
                  (sprintf
                      "unresolvable $ref(s): %s — a strict validator ERRORS on these, so the leg cannot certify"
                      (String.concat ", " dangling)))

          testCase "every declared record has a definition" (fun _ ->
              for r in uiIdl.Records do
                  Expect.isTrue
                      (definedNames.Contains r.Name)
                      (sprintf "record '%s' is referenced by TRecord slots but absent from $defs" r.Name))

          testCase "a record definition carries no `$type` const" (fun _ ->
              // The distinction a record schema exists to make: a union case is
              // `$type`-tagged, a record is not. Emitting the const would demand a
              // key no encoder writes for these.
              let recordNames = uiIdl.Records |> List.map _.Name |> Set.ofList

              match emittedDoc with
              | JObj fields ->
                  match fields |> List.tryFind (fun (n, _) -> n = "$defs") |> Option.map snd with
                  | Some(JObj defs) ->
                      for name, def in defs do
                          if recordNames.Contains name then
                              match def with
                              | JObj df ->
                                  match df |> List.tryFind (fun (n, _) -> n = "properties") |> Option.map snd with
                                  | Some(JObj props) ->
                                      Expect.isFalse
                                          (props |> List.exists (fun (p, _) -> p = "$type"))
                                          (sprintf "record '%s' declares a $type property" name)
                                  | _ -> failtestf "record '%s' has no properties object" name
                              | _ -> failtestf "record '%s' is not an object schema" name
                  | _ -> failtest "no $defs"
              | _ -> failtest "not an object")

          testCase "the strictness posture is aligned with the decoder, not stricter" (fun _ ->
              // A recorded DECISION, pinned so it cannot regress silently. The
              // decoder tolerates unknown keys and the published schema.json matches
              // that; `additionalProperties: false` anywhere in the emitted document
              // would make this leg reject payloads the format accepts. `TMap` still
              // uses `additionalProperties` as a VALUE schema — a different meaning,
              // and never `false`.
              let rec falseAdditional (j: JVal) : bool =
                  match j with
                  | JObj fields ->
                      fields
                      |> List.exists (fun (n, v) ->
                          (n = "additionalProperties" && v = JBool false) || falseAdditional v)
                  | JArr items -> items |> List.exists falseAdditional
                  | _ -> false

              Expect.isFalse
                  (falseAdditional emittedDoc)
                  "the generated schema closes an object — stricter than WIRE_FORMAT.md §2.1 rule 2")

          // ── the certification itself ─────────────────────────────────────

          testCase "every node accept fixture validates against the generated schema" (fun _ ->
              match fixtures "nodes" with
              | [] -> skiptest "wire-format-fixtures not checked out alongside — certification skipped"
              | files ->
                  let failures =
                      [ for path in files do
                            let wire = File.ReadAllText path

                            match validate wire with
                            | Some true -> ()
                            | Some false -> Path.GetFileName path, "schema REJECTED an accept fixture"
                            | None -> Path.GetFileName path, "fixture is not parseable JSON" ]

                  Expect.isEmpty
                      failures
                      (sprintf
                          "%d of %d node fixtures failed the generated schema:\n  %s"
                          failures.Length
                          files.Length
                          (failures |> List.map (fun (f, m) -> f + " — " + m) |> String.concat "\n  ")))

          testCase "the certification is not vacuous — the corpus was actually read" (fun _ ->
              // The guard above SKIPS without the corpus, and a silent skip that
              // reads zero fixtures looks identical to a pass. Assert the corpus is
              // present and non-trivial whenever it is checked out at all.
              match IdlArtifactTests.tryFindCorpusRoot () with
              | None -> skiptest "wire-format-fixtures not checked out alongside"
              | Some _ ->
                  Expect.isGreaterThan
                      (List.length (fixtures "nodes"))
                      50
                      "the node family should carry the whole corpus, not a handful")

          testCase "a structurally-invalid payload is still rejected" (fun _ ->
              // The other half of a non-vacuous certification: a schema that
              // validated EVERYTHING would pass the accept sweep too. Two payloads
              // the generated schema must reject on structure alone.
              let unknownKind = """{"id":"n1","kind":{"$type":"NoSuchKind"}}"""
              let missingId = """{"kind":{"$type":"Heading","level":2,"text":"x"}}"""

              Expect.equal (validate unknownKind) (Some false) "an unknown kind tag must not validate"
              Expect.equal (validate missingId) (Some false) "a node without `id` must not validate")

          testCase "reject fixtures: how many the schema catches, and which it cannot" (fun _ ->
              // NOT an assertion that every reject fixture fails the schema — many
              // encode rules Draft 2020-12 provably cannot state (cross-field
              // ordering, node-id uniqueness across a tree, §16 lenient policy), and
              // the decoder is their only enforcer. The tier's hand-written suite
              // pins its own exemption set BY NAME; this leg is not yet a
              // subsumption candidate, so what is useful here is the enumerated
              // worksheet, reported rather than asserted.
              match fixtures "reject" with
              | [] -> skiptest "wire-format-fixtures not checked out alongside"
              | files ->
                  let caught, uncaught =
                      files
                      |> List.partition (fun p ->
                          match validate (File.ReadAllText p) with
                          | Some true -> false
                          | _ -> true)

                  printfn
                      "  reject family: %d/%d caught structurally by the generated schema"
                      caught.Length
                      files.Length

                  printfn "  NOT caught (decoder-only rules — see the Phase 697 worksheet):"

                  for p in uncaught do
                      printfn "    %s" (Path.GetFileName p)

                  Expect.isNonEmpty caught "the schema should catch SOME reject fixture structurally") ]
