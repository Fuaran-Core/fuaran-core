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
              // `Chart.source` is a `Binding<obj>.Static` — opaque on the wire (Chart/DataGrid
              // sources stayed opaque through the 0.2.x typed-Static advance).
              match Encode.encode uiIdl (List.find (fun (n, _) -> n = "chart-1") visCases |> snd) with
              | Ok w -> Expect.stringContains w "\"<opaque>\"" "the opaque sentinel renders for the obj value"
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
// Phase 321 tasks 2 + 3 — the codegen trust boundary. `Custom` is inert-by-
// default (allowlist + content-hash gate); URLs / markdown are sanitised at
// codegen time; the whole boundary is a pure `harden` transform over the
// authored value, verified over the real meta / display corpus shapes.
// ---------------------------------------------------------------------------

let private metaOf (name: string) =
    List.find (fun (n, _) -> n = name) metaCases |> snd

let private allow (m: string) (c: string) (h: string) : Trust.AllowEntry =
    { ModuleId = m
      ComponentId = c
      Hash = h }

[<Tests>]
let trustTests =
    testList
        "Phase 321 — codegen trust boundary"
        [ testCase "Sanitize: URL default-deny (dangerous/unknown schemes → about:blank; safe pass)" (fun _ ->
              Expect.equal (Sanitize.sanitizeUrlOrBlank "javascript:alert(1)") "about:blank" "javascript: blocked"
              Expect.equal (Sanitize.sanitizeUrlOrBlank "JAVA\tSCRIPT:alert(1)") "about:blank" "obfuscated js blocked"
              Expect.equal (Sanitize.sanitizeUrlOrBlank "vbscript:x") "about:blank" "vbscript blocked"
              Expect.equal (Sanitize.sanitizeUrlOrBlank "data:text/html,x") "about:blank" "data: blocked (unknown)"
              Expect.equal (Sanitize.sanitizeUrlOrBlank "//evil.com/x") "about:blank" "protocol-relative blocked"
              Expect.equal (Sanitize.sanitizeUrlOrBlank "/about") "/about" "relative allowed"
              Expect.equal (Sanitize.sanitizeUrlOrBlank "https://example.com") "https://example.com" "https allowed"
              Expect.equal (Sanitize.sanitizeUrlOrBlank "mailto:a@b.com") "mailto:a@b.com" "mailto allowed")

          testCase "Sanitize: attribute-key allowlist (data-*/aria-* only; on*/other rejected)" (fun _ ->
              Expect.isTrue (Sanitize.isAllowedAttributeKey "data-test") "data- allowed"
              Expect.isTrue (Sanitize.isAllowedAttributeKey "aria-label") "aria- allowed"
              Expect.isFalse (Sanitize.isAllowedAttributeKey "onclick") "on* rejected"
              Expect.isFalse (Sanitize.isAllowedAttributeKey "class") "arbitrary rejected")

          testCase "Sanitize: markdown scrub removes dangerous elements + schemes, keeps benign text" (fun _ ->
              let scrubbed =
                  Sanitize.scrubMarkdown "hello <script>alert(1)</script> world javascript:x"

              Expect.isFalse (scrubbed.Contains "<script") "script tag removed"
              Expect.isFalse (scrubbed.Contains "javascript:") "javascript scheme removed"
              Expect.stringContains scrubbed "hello" "benign text preserved"
              Expect.equal (Sanitize.scrubMarkdown "Updated hourly.") "Updated hourly." "benign markdown untouched")

          testCase "Custom gate: an unhashed Custom becomes an inert placeholder (never a live call)" (fun _ ->
              // custom-1 carries no contentHash → inert-by-default regardless of allowlist.
              match
                  Trust.harden (Trust.uiPolicy [ allow "analytics" "trend-card" "whatever" ]) (metaOf "custom-1")
              with
              | VNode(id, "Markdown", fields) ->
                  Expect.equal id "custom-1" "placeholder preserves the node id"

                  match fields with
                  | [ (_, VUnion("Literal", [ (_, VStr label) ])) ] ->
                      Expect.stringContains label "inert placeholder" "labelled as inert"
                      Expect.stringContains label "trend-card" "names the gated component"
                  | _ -> failtest "unexpected placeholder shape"
              | VNode(_, "Custom", _) -> failtest "unhashed Custom must NOT stay live"
              | _ -> failtest "unexpected node")

          testCase "Custom gate: allowlisted + hash-verified Custom passes through live" (fun _ ->
              // custom-bounded-1: module deal-flow / component QualityRing / hash abc123def456 / StrictReplay.
              let policy = Trust.uiPolicy [ allow "deal-flow" "QualityRing" "abc123def456" ]

              match Trust.harden policy (metaOf "custom-bounded-1") with
              | VNode(_, "Custom", _) -> () // allowed
              | _ -> failtest "allowlisted + hash-matched Custom should stay live"

              // Empty allowlist → the same Custom is gated out.
              match Trust.harden (Trust.uiPolicy []) (metaOf "custom-bounded-1") with
              | VNode(_, "Markdown", _) -> () // inert
              | _ -> failtest "non-allowlisted Custom must be gated to inert")

          testCase
              "Custom gate: hash mismatch is inert under StrictReplay, advisory-live under AdvisoryWarning"
              (fun _ ->
                  // StrictReplay (custom-bounded-1) + wrong hash → inert.
                  match
                      Trust.harden
                          (Trust.uiPolicy [ allow "deal-flow" "QualityRing" "WRONG" ])
                          (metaOf "custom-bounded-1")
                  with
                  | VNode(_, "Markdown", _) -> ()
                  | _ -> failtest "StrictReplay hash mismatch must be gated to inert"

                  // AdvisoryWarning (custom-bounded-advisory) + wrong hash → live (advisory).
                  match
                      Trust.harden
                          (Trust.uiPolicy [ allow "deal-flow" "TrendCard" "WRONG" ])
                          (metaOf "custom-bounded-advisory")
                  with
                  | VNode(_, "Custom", _) -> ()
                  | _ -> failtest "AdvisoryWarning hash mismatch should stay live (advisory)")

          testCase "Sanitise at codegen time: a hostile Link href + Markdown are cleaned by harden" (fun _ ->
              let hostileLink =
                  VNode(
                      "l",
                      "Link",
                      [ "href", VUnion("Static", [ "value", VStr "javascript:alert(document.cookie)" ])
                        "label", lit "Click"
                        "download", VBool false ]
                  )

              match Trust.harden (Trust.uiPolicy []) hostileLink |> Encode.encode uiIdl with
              | Ok wire ->
                  Expect.isFalse (wire.Contains "javascript:") "the javascript: URL was sanitised out of the wire"
                  Expect.stringContains wire "about:blank" "replaced with the deny sentinel"
              | Error m -> failtestf "hostile link encode failed: %s" m

              let hostileMd =
                  VNode("m", "Markdown", [ "text", VUnion("Literal", [ "text", VStr "hi <script>evil()</script>" ]) ])

              match Trust.harden (Trust.uiPolicy []) hostileMd |> Encode.encode uiIdl with
              | Ok wire -> Expect.isFalse (wire.Contains "<script") "the script tag was scrubbed"
              | Error m -> failtestf "hostile markdown encode failed: %s" m)

          testCase "End to end: harden → scaffold emits inert, provenance-stamped source (no live Custom)" (fun _ ->
              match
                  Trust.scaffoldFSharp (Trust.uiPolicy []) uiIdl "wirehash-abc" "agent:claude@4.8" (metaOf "custom-1")
              with
              | Ok src ->
                  Expect.stringContains src "INERT" "carries the provenance trust-split invariant"
                  Expect.stringContains src "wirehash-abc" "carries the source wire hash"
                  Expect.stringContains src "NodeKind.Markdown" "emits the inert placeholder, not a live component"
                  Expect.isFalse (src.Contains "NodeKind.Custom") "no live Custom construction in the generated source"
              | Error m -> failtestf "scaffold failed: %s" m) ]
