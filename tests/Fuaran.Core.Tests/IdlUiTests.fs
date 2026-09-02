module Fuaran.Core.Tests.IdlUiTests

open System.IO
open Expecto
open Fuaran.Core.Idl
open Fuaran.Core.Tests.UiIdl

// ---------------------------------------------------------------------------
// Phase 317 — real-tier migration gate (all five families, ~40 kinds).
//
// The corpus-backed byte-diff the phase's acceptance #3 names: author each live
// `wire-format-fixtures/nodes/<name>.json` fixture as an `IdlValue` over the
// real-tier `uiIdl`, and assert the schema-driven `Encode.encode` reproduces it
// byte-for-byte. This proves the IDL *model* now covers the full Display + Layout
// + Input + Visualisation + Meta vocabulary of the real `Fuaran.UI` tier — the
// structural layer `CanonicalJson.encodeNode` hand-writes — over the same corpus
// that gates the hand-written host. The proof loop the migration scaled
// kind-family by kind-family.
//
// The expected bytes are a self-contained vendored snapshot (the gate never goes
// vacuous), drift-guarded against the live corpus when it is checked out.
// ---------------------------------------------------------------------------

let private tryFindCorpus () : string option =
    let candidates (root: string) =
        [ Path.Combine(root, "Fuaran-UI", "wire-format-fixtures", "nodes")
          Path.Combine(root, "wire-format-fixtures", "nodes") ]

    let rec climb (dir: string) (budget: int) =
        if budget < 0 || isNull dir then
            None
        else
            match candidates dir |> List.tryFind Directory.Exists with
            | Some d -> Some d
            | None ->
                match Directory.GetParent dir with
                | null -> None
                | parent -> climb parent.FullName (budget - 1)

    [ Directory.GetCurrentDirectory(); System.AppContext.BaseDirectory ]
    |> List.tryPick (fun start -> climb start 12)

// ---------------------------------------------------------------------------
// Phase 86 (2026-07-18) — the Core IDL mirror is now realigned with the live
// 0.2.6 corpus: Box container unification (Dashboard/Card/Stack/GridLayout → one
// `Box` kind with `role` + `layout`), typed `Static` payloads (Sparkline/Select/
// Map sources, Choice options carry real JSON; Chart/DataGrid stay opaque),
// Spacer/Divider retirement, and the 0.2.x strands (bare-string canonical
// `TextSource.Literal`, Metric/LabelValueRow `source`→`value`, omit-when-default
// flags, Phase-596 form-field auto-bind). EVERY fixture is byte-exact-guarded
// against the live corpus again — the drift-guard exemption sets are DELETED.
// ---------------------------------------------------------------------------

/// The per-family gate: authoring byte-identity + round-trip + coverage + the
/// live-corpus drift guard. Parameterised so a new family plugs straight in.
let private familyTests
    (family: string)
    (cases: (string * IdlValue) list)
    (expected: (string * string) list)
    (kinds: IdlKind list)
    =
    let expectedMap = Map.ofList expected

    let wire (name: string) =
        match Map.tryFind name expectedMap with
        | Some s -> s
        | None -> failwithf "no vendored wire snapshot for '%s'" name

    testList
        (sprintf "Phase 317 — real-tier migration (%s family)" family)
        [ testCase "authoring leg: every fixture encodes byte-identical to the canonical wire" (fun _ ->
              for name, value in cases do
                  match Encode.encode uiIdl value with
                  | Ok actual -> Expect.equal actual (wire name) (sprintf "byte mismatch encoding '%s'" name)
                  | Error m -> failtestf "encode failed for '%s': %s" name m)

          testCase "round-trip: decode(wire) re-encodes byte-identical (IDL drives both directions)" (fun _ ->
              for name, _ in cases do
                  let json = wire name

                  match Decode.decode uiIdl json with
                  | Error m -> failtestf "decode failed for '%s': %s" name m
                  | Ok v ->
                      match Encode.encode uiIdl v with
                      | Ok reencoded -> Expect.equal reencoded json (sprintf "round-trip byte mismatch for '%s'" name)
                      | Error m -> failtestf "re-encode failed for '%s': %s" name m)

          testCase "coverage: every kind in the family is exercised by ≥1 fixture" (fun _ ->
              let kindTags = kinds |> List.map (fun k -> k.Tag) |> Set.ofList

              let exercised =
                  cases
                  |> List.choose (fun (_, v) ->
                      match v with
                      | VNode(_, kindTag, _) -> Some kindTag
                      | _ -> None)
                  |> Set.ofList

              let missing = Set.difference kindTags exercised
              Expect.isEmpty missing (sprintf "%s kinds with no fixture: %A" family (Set.toList missing)))

          testCase "drift guard: vendored snapshot matches the live corpus (when checked out)" (fun _ ->
              // Phase 86: every fixture is byte-exact against the live 0.2.6 corpus — no exemptions.
              match tryFindCorpus () with
              | None -> skiptest "Fuaran-UI/wire-format-fixtures not checked out alongside — drift guard skipped"
              | Some dir ->
                  for name, snapshot in expected do
                      let path = Path.Combine(dir, name + ".json")

                      if File.Exists path then
                          let live = File.ReadAllText(path).TrimEnd('\n', '\r', ' ', '\t')

                          Expect.equal
                              snapshot
                              live
                              (sprintf "vendored %s snapshot for '%s' has drifted from the live corpus" family name)
                      else
                          failtestf "live corpus fixture missing for '%s' (%s)" name path) ]

[<Tests>]
let tests =
    testList
        "Phase 317 — real-tier migration"
        [ familyTests "Display" displayCases displayExpected displayKinds
          familyTests "Layout" layoutCases layoutExpected layoutKinds
          familyTests "Input" inputCases inputExpected inputKinds
          familyTests "Visualisation" visCases visExpected visKinds
          familyTests "Meta" metaCases metaExpected metaKinds

          testCase "sentinel modelling: the closure + opaque field classes round-trip" (fun _ ->
              // `Chart.source` carries TYPED rows since fuaran#665 (the rows slot left the
              // residual-`"<opaque>"` boundary): a Static rows payload renders as a JSON
              // array of row objects, no sentinel anywhere in the emission.
              match Encode.encode uiIdl (List.find (fun (n, _) -> n = "chart-1") visCases |> snd) with
              | Ok w ->
                  Expect.stringContains
                      w
                      "\"value\":[{\"cost\":420,\"month\":\"Jan\",\"revenue\":980}"
                      "typed rows render as an array of row objects"

                  Expect.isFalse
                      (w.Contains "\"<opaque>\"")
                      "the rows slot must no longer emit the opaque sentinel (fuaran#665)"
              | Error m -> failtestf "chart-1 encode failed: %s" m

              // A `Binding.Query` renders `dependsOn` (omitted when absent) + `name`
              // and NO accessor. Phase 671 step 2's direct byte-diff caught the IDL
              // declaring `accessor` here: the wire dropped it at 0.2.0, and this
              // assertion had been confirming the model's own mistake back to
              // itself rather than checking the wire. The genuine on-the-wire
              // closure sentinel is asserted just below, on `Tabs.onSelect`.
              let queried =
                  VNode(
                      "q",
                      "Image",
                      [ "alt", lit "x"
                        "src", VUnion("Query", [ "name", VStr "avatarUrl" ])
                        "variant", VEnum "Default" ]
                  )

              match Encode.encode uiIdl queried with
              | Ok w ->
                  Expect.stringContains w "\"$type\":\"Query\"" "the Query case renders"
                  Expect.stringContains w "\"name\":\"avatarUrl\"" "Query carries its name"

                  Expect.isFalse
                      (w.Contains "accessor")
                      "Query must NOT emit an accessor — the wire dropped it at 0.2.0"
              | Error m -> failtestf "query-binding encode failed: %s" m

              // `Tabs.onSelect` is an on-the-wire closure sentinel (not omitted).
              match Encode.encode uiIdl (List.find (fun (n, _) -> n = "tabs-1") layoutCases |> snd) with
              | Ok w ->
                  Expect.stringContains w "\"onSelect\":\"<closure>\"" "Tabs.onSelect renders the closure sentinel"
              | Error m -> failtestf "tabs-1 encode failed: %s" m)

          testCase
              "nesting: a composite tree round-trips across families (Box[Dashboard] > Box[Card] > Metric …)"
              (fun _ ->
                  let root = List.find (fun (n, _) -> n = "composite-root") layoutCases |> snd

                  match Decode.decode uiIdl (List.find (fun (n, _) -> n = "composite-root") layoutExpected |> snd) with
                  | Error m -> failtestf "composite-root decode failed: %s" m
                  | Ok v -> Expect.equal v root "decode(composite wire) reconstructs the authored nested tree")

          testCase "negative control: a mutated field diverges from its wire" (fun _ ->
              let mutated =
                  VNode("badge-1", "Badge", [ "label", lit "Beta — TYPO"; "variant", VEnum "Info" ])

              match Encode.encode uiIdl mutated with
              | Ok actual ->
                  Expect.notEqual
                      actual
                      (Map.ofList displayExpected |> Map.find "badge-1")
                      "negative control unexpectedly matched"
              | Error m -> failtestf "negative control failed to encode: %s" m) ]

// ---------------------------------------------------------------------------
// The Phase 321 codegen trust boundary MOVED OUT of this file (Phase 114).
//
// It is ENGINE behaviour - the `Custom` allowlist + content-hash gate, the URL and
// markdown sanitisation `harden` applies, the inert-by-construction scaffold - and it
// needed a vocabulary only because it needs kinds to harden. Certified here, it would
// leave this repo with the UI vocabulary and take the engine's security floor with it,
// so it is certified over the domain-neutral reference vocabulary instead, in
// `IdlCertificationTests`. MOVED, not copied: what remains in this file is what is
// genuinely about the UI vocabulary and its corpus.
// ---------------------------------------------------------------------------
