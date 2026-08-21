module Fuaran.Core.Tests.IdlSpikeTests

open System
open System.IO
open System.Diagnostics
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Spike.Fixtures

module G = Fuaran.Core.Idl.Spike.Generated

// ---------------------------------------------------------------------------
// Phase 316 — IDL inversion spike gate. Proves the IDL drives the codec in BOTH
// directions, byte-for-byte:
//   * authored `IdlValue` --encode--> canonical wire  (authoring leg)
//   * canonical wire --decode--> IdlValue --encode--> canonical wire  (round-trip)
// The expected bytes are a vendored snapshot, so the gate is self-contained and
// never vacuous; a drift guard checks the snapshot still matches the live
// `Fuaran-UI/wire-format-fixtures` corpus when it is checked out alongside.
//
// Two proof layers: the schema-DRIVEN interpreter (Encode/Decode walk the IDL at
// runtime, round-tripping the corpus, incl. generics — Phase 316 + 317 inc. 1),
// and *real code emission* (Phase 317 inc. 2–3): `Gen.fsharpModule` emits a
// compiling F# module (`Generated.fs`, in the build) for all 8 kinds, handling
// every feature class — optionals, generics (`Binding<'T>` by codec-passing),
// lists, and node nesting. Its generated encoder round-trips heading/badge/button/
// stack byte-identical; a drift guard checks the generator still reproduces it.
// ---------------------------------------------------------------------------

// Vendored wire snapshots load from the golden store (regenerate with
// `--regen-snapshots`); the spike's own `expected` literal is retired.
let private expected = Snapshots.loadPaired "spike" cases
let private expectedMap = Map.ofList expected

let private wire (name: string) =
    match Map.tryFind name expectedMap with
    | Some s -> s
    | None -> failwithf "no vendored wire snapshot for '%s'" name

/// The kinds the generated F# encoder (`Generated.fs`) covers — the 6-kind slice
/// (Fuaran-UI 0.2.0: Card / Stack unified into `Box`, Divider retired), plus
/// `Tabs`, the Phase 689 `'Msg`-threading spike kind. This list and the one in
/// `Program.fs`'s `--regen-snapshots` must stay in step: the generative
/// conformance test samples from the whole IDL but compares against a TS module
/// built from THIS list, so a kind missing here shows up as `"kind":undefined` on
/// the TS side rather than as a missing-kind error.
let private generatedKinds =
    [ "Heading"; "Badge"; "Button"; "Metric"; "Box"; "Markdown"; "Tabs" ]

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

let private tryFindGenerated () : string option =
    let rel = Path.Combine("src", "Fuaran.Core.Idl.Spike", "Generated.fs")

    let rec climb (dir: string) (budget: int) =
        if budget < 0 || isNull dir then
            None
        else
            let candidate = Path.Combine(dir, rel)

            if File.Exists candidate then
                Some candidate
            else
                match Directory.GetParent dir with
                | null -> None
                | parent -> climb parent.FullName (budget - 1)

    [ Directory.GetCurrentDirectory(); System.AppContext.BaseDirectory ]
    |> List.tryPick (fun start -> climb start 12)

/// A wire string crafted to break out of a source literal + inject code (Phase 321).
let private hostile = "x\"; PWNED(); \\ </script>\n\t<&>"

let private hostileNode =
    VNode(
        "h",
        "Heading",
        [ "level", VInt 1
          "text", VUnion("Literal", [ "text", VStr hostile ])
          "variant", VEnum "Standard" ]
    )

/// The trusted (interpreter) wire for the hostile node — the scaffold backends
/// must reproduce exactly (payload stays inert escaped data).
let private trustedWire =
    lazy
        (match Encode.encode miniIdl hostileNode with
         | Ok w -> w
         | Error e -> failwithf "interpreter failed to encode the hostile node: %s" e)

[<Tests>]
let tests =
    testList
        "Phase 316 — IDL inversion spike"
        [ testCase "authoring leg: every authored value encodes byte-identical to the canonical wire" (fun _ ->
              for name, value in cases do
                  match Encode.encode miniIdl value with
                  | Ok actual -> Expect.equal actual (wire name) (sprintf "byte mismatch encoding '%s'" name)
                  | Error m -> failtestf "encode failed for '%s': %s" name m)

          testCase "round-trip: decode(wire) re-encodes byte-identical (IDL drives both directions)" (fun _ ->
              for name, _ in cases do
                  let json = wire name

                  match Decode.decode miniIdl json with
                  | Error m -> failtestf "decode failed for '%s': %s" name m
                  | Ok v ->
                      match Encode.encode miniIdl v with
                      | Ok reencoded -> Expect.equal reencoded json (sprintf "round-trip byte mismatch for '%s'" name)
                      | Error m -> failtestf "re-encode failed for '%s': %s" name m)

          testCase "negative control diverges from its wire" (fun _ ->
              let name, mutated = negativeControl

              match Encode.encode miniIdl mutated with
              | Ok actual -> Expect.notEqual actual (wire name) "negative control unexpectedly matched"
              | Error m -> failtestf "negative control failed to encode: %s" m)

          testCase "drift guard: vendored snapshot matches the live corpus (when checked out)" (fun _ ->
              // Phase 86 realignment: the spike IDL now tracks the live 0.2.x corpus (Box
              // unification, bare-string TextSource, Metric source→value, Divider retired),
              // so every fixture is byte-exact-guarded again with NO exemptions.
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
                              (sprintf "vendored snapshot for '%s' has drifted from the live corpus" name)
                      else
                          failtestf "live corpus fixture missing for '%s' (%s)" name path)

          testCase
              "generics: one Binding<'T> def serves both float (Metric.source) and bool (Button.disabled)"
              (fun _ ->
                  // Both already round-trip above via metric-1 / btn-1; assert the single union
                  // definition really is parameterised (one case set, a type parameter).
                  let binding = miniIdl.Unions |> List.find (fun u -> u.Name = "Binding")
                  Expect.equal binding.Params [ "T" ] "Binding should have exactly one type parameter"

                  Expect.isTrue
                      (binding.Cases
                       |> List.exists (fun c -> c.Fields |> List.exists (fun f -> f.Type = TVar "T")))
                      "Binding's cases should reference the type parameter 'T")

          testCase "generics: a Binding<bool> field given a float value is rejected" (fun _ ->
              match Encode.encode miniIdl wrongElementType with
              | Ok _ -> failtest "expected the encoder to reject a type-mismatched generic instantiation"
              | Error _ -> ())

          testCase "type-generation leg emits F# source (incl. a generic Binding<'T>)" (fun _ ->
              let src = Gen.fsharpTypes miniIdl
              Expect.isGreaterThan src.Length 0 "type-gen produced empty source"
              Expect.isTrue (src.Contains "type Binding<'T>") "type-gen should emit a generic Binding<'T>"

              for k in miniIdl.Kinds do
                  Expect.isTrue (src.Contains(k.Tag + "Spec")) (sprintf "missing generated spec for kind '%s'" k.Tag)

              for u in miniIdl.Unions do
                  Expect.isTrue (src.Contains("type " + u.Name)) (sprintf "missing generated union '%s'" u.Name))

          testCase "encoder rejects an authored field absent from the IDL" (fun _ ->
              let bogus =
                  VNode("x", "Badge", [ "label", lit "Beta"; "variant", VEnum "Info"; "bogus", VInt 1 ])

              match Encode.encode miniIdl bogus with
              | Ok _ -> failtest "expected the encoder to reject an unknown authored field"
              | Error _ -> ())

          // ---- Phase 317 increment 2: real code emission (compiled generator output) ----

          testCase "real code emission: the GENERATED encoder round-trips byte-identical" (fun _ ->
              // Construct values of the GENERATED types and encode via the GENERATED encoder
              // (not the interpreter) — proving the emitted F# compiles and is byte-correct.
              let heading: G.Node<unit> =
                  { Id = "heading-1"
                    Kind =
                      G.NodeKind.Heading
                          { Level = 2
                            Text = G.TextSource.Literal "Channel performance"
                            Variant = G.HeadingVariant.Standard } }

              Expect.equal (G.encodeNode heading) (wire "heading-1") "generated encoder byte mismatch for heading-1"

              let badge: G.Node<unit> =
                  { Id = "badge-1"
                    Kind =
                      G.NodeKind.Badge
                          { Label = G.TextSource.Literal "Beta"
                            Variant = G.BadgeVariant.Info } }

              Expect.equal (G.encodeNode badge) (wire "badge-1") "generated encoder byte mismatch for badge-1"

              // Button — optionals + a generic Binding<bool> + an action.
              let button: G.Node<unit> =
                  { Id = "btn-1"
                    Kind =
                      G.NodeKind.Button
                          { Disabled = Some(G.Binding.State(false, "loading"))
                            Icon = Some "refresh"
                            Label = G.TextSource.Literal "Refresh"
                            OnClick = G.Action.Chain []
                            Variant = G.ButtonVariant.Primary } }

              Expect.equal (G.encodeNode button) (wire "btn-1") "generated encoder byte mismatch for btn-1"

              // Box (role Group / Flex) — a list of nested nodes (Metric: generic Binding<float>
              // + optionals; Markdown).
              let metric: G.MetricSpec =
                  { Emphasis = G.Emphasis.Normal
                    Format = G.Format.Currency "GBP"
                    Icon = Some "trending-up"
                    Label = G.TextSource.Literal "Revenue"
                    Value = G.Binding.Static 1234.5
                    Subtext = Some(G.TextSource.Literal "vs last month")
                    Tone = G.ToneVariant.Brand
                    Trend = Some(G.Binding.Static 0.07)
                    TrendFormat = Some(G.Format.Percent 1)
                    Weight = G.StyleWeight.Standard }

              let stack: G.Node<unit> =
                  { Id = "stack-1"
                    Kind =
                      G.NodeKind.Box
                          { Children =
                              [ { Id = "metric-1"
                                  Kind = G.NodeKind.Metric metric }
                                { Id = "markdown-1"
                                  Kind = G.NodeKind.Markdown { Text = G.TextSource.Literal "Updated hourly." } } ]
                            Heading = None
                            Layout = G.LayoutMode.Flex(G.Orientation.Vertical, false)
                            Role = G.BoxRole.Group } }

              Expect.equal (G.encodeNode stack) (wire "stack-1") "generated encoder byte mismatch for stack-1")

          testCase "real code emission: the generator still reproduces the committed Generated.fs" (fun _ ->
              let generated =
                  match Gen.fsharpModule "Fuaran.Core.Idl.Spike.Generated" miniIdl generatedKinds with
                  | Ok s -> s
                  | Error e -> failtestf "codegen rejected the spike vocabulary: %A" e

              match tryFindGenerated () with
              | None -> skiptest "Generated.fs not found on disk — drift guard skipped"
              | Some path ->
                  // Regeneration escape hatch, same as IdlUiGenTests: FUARAN_REGEN=1
                  // rewrites the committed file instead of asserting, so a deliberate
                  // generator change is a one-command update, not a hand-edit of
                  // generated code.
                  if System.Environment.GetEnvironmentVariable "FUARAN_REGEN" = "1" then
                      File.WriteAllText(path, generated)

                  // Whitespace-insensitive: a real generator change is caught.
                  let strip (s: string) =
                      s
                      |> Seq.filter (System.Char.IsWhiteSpace >> not)
                      |> Seq.toArray
                      |> System.String

                  Expect.equal
                      (strip generated)
                      (strip (File.ReadAllText path))
                      "the generator no longer reproduces Generated.fs — regenerate it")

          // ---- Phase 317 increment 5: the Core witness-record leg ----

          testCase "witness leg: the generated NodeWitness drives Fuaran.Core.Tree over generated nodes" (fun _ ->
              // The generated `nodeWitness` lets a generic Core operation (here Tree.ids,
              // the preorder id walk) traverse generated nodes with no UI dependency —
              // proving the generated structural layer plugs into the Core spine.
              let metric: G.MetricSpec =
                  { Emphasis = G.Emphasis.Normal
                    Format = G.Format.Currency "GBP"
                    Icon = Some "trending-up"
                    Label = G.TextSource.Literal "Revenue"
                    Value = G.Binding.Static 1234.5
                    Subtext = Some(G.TextSource.Literal "vs last month")
                    Tone = G.ToneVariant.Brand
                    Trend = Some(G.Binding.Static 0.07)
                    TrendFormat = Some(G.Format.Percent 1)
                    Weight = G.StyleWeight.Standard }

              let card: G.Node<unit> =
                  { Id = "card-1"
                    Kind =
                      G.NodeKind.Box
                          { Children =
                              [ { Id = "metric-1"
                                  Kind = G.NodeKind.Metric metric } ]
                            Heading = Some(G.TextSource.Literal "Insights")
                            Layout = G.LayoutMode.Flex(G.Orientation.Vertical, false)
                            Role = G.BoxRole.Card } }

              let w = G.nodeWitness

              Expect.equal (w.KindTag card) "Box" "witness KindTag reads the node's kind"
              Expect.equal (Fuaran.Core.Tree.ids w card) [ "card-1"; "metric-1" ] "preorder ids via witness Children"

              // A leaf kind (no node-bearing fields) reports no children.
              let leaf: G.Node<unit> =
                  { Id = "markdown-1"
                    Kind = G.NodeKind.Markdown { Text = G.TextSource.Literal "x" } }

              Expect.isEmpty (w.Children leaf) "a leaf kind has no structural children"

              // ReplaceChildren round-trips through the structural-edit seam.
              let cleared = w.ReplaceChildren card []
              Expect.isEmpty (w.Children cleared) "ReplaceChildren [] clears the child list"
              Expect.equal (Fuaran.Core.Tree.ids w cleared) [ "card-1" ] "cleared card has no descendants")

          testCase
              "validator scaffold: the generated runValidator runs a domain RuleFamily over generated nodes"
              (fun _ ->
                  // Rule CONTENT stays domain-side (Core.Validator's whole design); the
                  // generated scaffold + witness do the traversal. A trivial domain rule:
                  // flag any node whose Id is empty.
                  let noEmptyId =
                      Validator.perNode "GEN001" (fun w n ->
                          if w.Id n = "" then
                              [ { Code = "GEN001"
                                  Severity = Severity.Error
                                  Message = "empty id"
                                  Node = Some(w.Id n) } ]
                          else
                              [])

                  let reg = Validator.empty |> Validator.register noEmptyId

                  let tree: G.Node<unit> =
                      { Id = "root"
                        Kind =
                          G.NodeKind.Box
                              { Children =
                                  [ { Id = ""
                                      Kind = G.NodeKind.Markdown { Text = G.TextSource.Literal "x" } } ]
                                Heading = None
                                Layout = G.LayoutMode.Flex(G.Orientation.Vertical, false)
                                Role = G.BoxRole.Group } }

                  let defects = G.runValidator reg tree
                  Expect.equal (List.length defects) 1 "one empty-id defect found via the generated scaffold"
                  Expect.equal defects.[0].Code "GEN001" "the registered domain rule fired"

                  // A clean tree yields no defects.
                  let clean: G.Node<unit> =
                      { Id = "root"
                        Kind = G.NodeKind.Markdown { Text = G.TextSource.Literal "ok" } }

                  Expect.isEmpty (G.runValidator reg clean) "no defects over a clean generated tree")

          // ---- Phase 317 increment 7: the IDL-declared-defaults leg ----

          testCase "defaults leg: generated smart constructors fill IDL-declared defaults" (fun _ ->
              // mkHeading takes only required-without-default fields; `variant` is
              // filled from the IDL default (HeadingVariant.Standard).
              let h = G.mkHeading "h1" 2 (G.TextSource.Literal "Title")

              match h.Kind with
              | G.NodeKind.Heading s ->
                  Expect.equal s.Variant G.HeadingVariant.Standard "variant defaulted to Standard"
              | _ -> failtest "expected a Heading"

              // mkButton: variant defaulted to Primary; the two optionals default to None.
              let b = G.mkButton "b1" (G.TextSource.Literal "Go") (G.Action.Chain [])

              match b.Kind with
              | G.NodeKind.Button s ->
                  Expect.equal s.Variant G.ButtonVariant.Primary "variant defaulted to Primary"
                  Expect.isNone s.Disabled "optional disabled defaults to None"
                  Expect.isNone s.Icon "optional icon defaults to None"
              | _ -> failtest "expected a Button"

              // mkBox: heading defaults to None; children/layout/role are parameters.
              let st =
                  G.mkBox "s1" [ h ] (G.LayoutMode.Flex(G.Orientation.Vertical, false)) G.BoxRole.Group

              match st.Kind with
              | G.NodeKind.Box s ->
                  Expect.isNone s.Heading "optional heading defaults to None"
                  Expect.equal (List.length s.Children) 1 "children passed through"
              | _ -> failtest "expected a Box"

              // The default-applied node still encodes byte-identical to the hand-authored wire.
              Expect.equal
                  (G.encodeNode (G.mkHeading "heading-1" 2 (G.TextSource.Literal "Channel performance")))
                  (wire "heading-1")
                  "smart-constructed heading encodes byte-identical to the corpus")

          // ---- Phase 317 increment 4: the schema.json leg (third §11 mirror) ----

          testCase "schema.json leg: the IDL emits a well-formed JSON Schema for every kind/union/enum" (fun _ ->
              let schema = Gen.jsonSchema miniIdl

              match Fuaran.Core.Json.parse schema with
              | Error m -> failtestf "emitted schema is not valid JSON: %s" m
              | Ok _ -> ()

              Expect.isTrue (schema.Contains "json-schema.org/draft/2020-12") "schema should declare Draft 2020-12"
              Expect.isTrue (schema.Contains "\"$defs\"") "schema should have a $defs section"

              for k in miniIdl.Kinds do
                  Expect.isTrue
                      (schema.Contains("\"" + k.Tag + "\""))
                      (sprintf "schema missing a def for kind '%s'" k.Tag)

              for u in miniIdl.Unions do
                  Expect.isTrue
                      (schema.Contains("\"" + u.Name + "\""))
                      (sprintf "schema missing a def for union '%s'" u.Name))

          // ---- Phase 317 increment 8: the SECOND BACKEND (TypeScript) ----

          testCase "second backend: the generated TypeScript encoder is byte-identical to F# over the corpus" (fun _ ->
              // Generate an independent host's encoder from the SAME IDL, run it under
              // node over the 8 fixtures, and assert each wire string equals the vendored
              // corpus (== the F# generated encoder's output). This is the cross-host
              // byte-identity that underwrites cross-host attestation (Phase 320).
              let tsModule = Gen.typescriptModule miniIdl generatedKinds

              let fixturesJs =
                  cases
                  |> List.map (fun (name, v) -> sprintf "  [\"%s\", %s]," name (Gen.typescriptValue v))
                  |> String.concat "\n"

              let harness =
                  tsModule
                  + "\n\nconst __fixtures = [\n"
                  + fixturesJs
                  + "\n];\n"
                  + "for (const [name, node] of __fixtures) { console.log(name + '\\u0001' + encodeNode(node)); }\n"

              let tmp =
                  Path.Combine(Path.GetTempPath(), sprintf "fuaran-ts-backend-%s.mjs" (Guid.NewGuid().ToString("N")))

              File.WriteAllText(tmp, harness)

              try
                  let psi = Diagnostics.ProcessStartInfo("node", "\"" + tmp + "\"")
                  psi.RedirectStandardOutput <- true
                  psi.RedirectStandardError <- true
                  psi.UseShellExecute <- false

                  let proc =
                      try
                          Some(Diagnostics.Process.Start psi)
                      with _ ->
                          None

                  match proc with
                  | None -> skiptest "node not on PATH — TS-backend byte-identity check skipped"
                  | Some p ->
                      let stdout = p.StandardOutput.ReadToEnd()
                      let stderr = p.StandardError.ReadToEnd()
                      p.WaitForExit()

                      if p.ExitCode <> 0 then
                          failtestf "node failed running the generated TS encoder: %s" stderr

                      let got =
                          stdout.Replace("\r\n", "\n").Split('\n')
                          |> Array.filter (fun l -> l <> "")
                          |> Array.map (fun l ->
                              let parts = l.Split('')
                              parts.[0], parts.[1])
                          |> Map.ofArray

                      for name, expectedWire in expected do
                          match Map.tryFind name got with
                          | Some actual ->
                              Expect.equal actual expectedWire (sprintf "TS-backend wire mismatch for '%s'" name)
                          | None -> failtestf "TS backend produced no output for '%s'" name
              finally
                  try
                      File.Delete tmp
                  with _ ->
                      ())

          // ---- Phase 672 task 4: the TS backend's DECODER ----

          testCase "second backend: the generated TypeScript decoder round-trips the corpus" (fun _ ->
              // The same gate the F# decoder passes, run on the independent host:
              // `decodeNode >> encodeNode` must be byte-identical. Doing it on both
              // backends is what makes the saving multiply across hosts rather than
              // accruing only to F#, and it re-proves cross-host byte-identity from
              // the decode side (Phase 320's attestation rests on both directions).
              let tsModule = Gen.typescriptModule miniIdl generatedKinds

              let jsStr (s: string) = Text.Json.JsonSerializer.Serialize s

              let wireJs =
                  expected
                  |> List.map (fun (name, w) -> sprintf "  [%s, %s]," (jsStr name) (jsStr w))
                  |> String.concat "\n"

              let harness =
                  tsModule
                  + "\n\nconst __wire = [\n"
                  + wireJs
                  + "\n];\n"
                  + "for (const [name, s] of __wire) {\n"
                  + "  const r = decodeNode(s);\n"
                  + "  console.log(name + '\\u0001' + (r.ok ? encodeNode(r.value) : 'DECODE-ERROR: ' + r.error));\n"
                  + "}\n"

              let tmp =
                  Path.Combine(Path.GetTempPath(), sprintf "fuaran-ts-decode-%s.mjs" (Guid.NewGuid().ToString("N")))

              File.WriteAllText(tmp, harness)

              try
                  let psi = Diagnostics.ProcessStartInfo("node", "\"" + tmp + "\"")
                  psi.RedirectStandardOutput <- true
                  psi.RedirectStandardError <- true
                  psi.UseShellExecute <- false

                  let proc =
                      try
                          Some(Diagnostics.Process.Start psi)
                      with _ ->
                          None

                  match proc with
                  | None -> skiptest "node not on PATH — TS-backend decode round-trip skipped"
                  | Some p ->
                      let stdout = p.StandardOutput.ReadToEnd()
                      let stderr = p.StandardError.ReadToEnd()
                      p.WaitForExit()

                      if p.ExitCode <> 0 then
                          failtestf "node failed running the generated TS decoder: %s" stderr

                      let got =
                          stdout.Replace("\r\n", "\n").Split('\n')
                          |> Array.filter (fun l -> l <> "")
                          |> Array.map (fun l ->
                              let parts = l.Split('')
                              parts.[0], parts.[1])
                          |> Map.ofArray

                      for name, expectedWire in expected do
                          match Map.tryFind name got with
                          | Some actual ->
                              Expect.equal
                                  actual
                                  expectedWire
                                  (sprintf "TS decodeNode >> encodeNode is not the identity for '%s'" name)
                          | None -> failtestf "TS backend produced no output for '%s'" name
              finally
                  try
                      File.Delete tmp
                  with _ ->
                      ())

          // ---- Phase 317: GENERATIVE cross-host conformance ----

          testCase "generative conformance: F# and TypeScript agree on 500 generated vectors" (fun _ ->
              // The fixed corpus proves the hosts agree on the shapes someone
              // thought to write down. This closes the other half: deterministic
              // vectors generated FROM the IDL, encoded by the F# interpreter and
              // by the independent TypeScript backend under node, asserted
              // byte-identical.
              //
              // The pools are adversarial where hosts actually diverge — string
              // escaping and float formatting — so this reaches shapes the corpus
              // does not contain: control characters, surrogate pairs, whole-valued
              // floats, empty collections, and both sides of every presence rule.
              let vectors = Sample.sampleNodes miniIdl generatedKinds 20260726 500

              Expect.equal (List.length vectors) 500 "the sampler produced the requested vectors"

              let fsharpWire =
                  vectors
                  |> List.mapi (fun i v ->
                      match Encode.encode miniIdl v with
                      | Ok w -> w
                      | Error m -> failtestf "F# encode failed on vector %d: %s" i m)

              let tsModule = Gen.typescriptModule miniIdl generatedKinds

              let vectorsJs =
                  vectors
                  |> List.mapi (fun i v -> sprintf "  [%d, %s]," i (Gen.typescriptValue v))
                  |> String.concat "\n"

              let harness =
                  tsModule
                  + "\n\nconst __vectors = [\n"
                  + vectorsJs
                  + "\n];\n"
                  // Fault-isolate per vector: one bad vector must not lose the other
                  // 199, and the index has to survive into the report.
                  + "for (const [i, node] of __vectors) {
"
                  + "  try { console.log(i + '\u0001' + encodeNode(node)); }
"
                  + "  catch (e) { console.log(i + '\u0001' + 'TS-THREW: ' + (e && e.message ? e.message : e)); }
"
                  + "}
"

              let tmp =
                  Path.Combine(Path.GetTempPath(), sprintf "fuaran-generative-%s.mjs" (Guid.NewGuid().ToString("N")))

              File.WriteAllText(tmp, harness)

              try
                  let psi = Diagnostics.ProcessStartInfo("node", "\"" + tmp + "\"")
                  psi.RedirectStandardOutput <- true
                  psi.RedirectStandardError <- true
                  psi.UseShellExecute <- false

                  let proc =
                      try
                          Some(Diagnostics.Process.Start psi)
                      with _ ->
                          None

                  match proc with
                  | None -> skiptest "node not on PATH — generative cross-host conformance skipped"
                  | Some p ->
                      let stdout = p.StandardOutput.ReadToEnd()
                      let stderr = p.StandardError.ReadToEnd()
                      p.WaitForExit()

                      if p.ExitCode <> 0 then
                          failtestf "node failed running the generative harness: %s" stderr

                      let got =
                          stdout.Replace("\r\n", "\n").Split('\n')
                          |> Array.filter (fun l -> l <> "")
                          |> Array.map (fun l ->
                              let parts = l.Split('')
                              int parts.[0], parts.[1])
                          |> Map.ofArray

                      // Report the FIRST divergence with its index, so a failing
                      // vector reproduces from (seed, index) alone.
                      let divergence =
                          fsharpWire
                          |> List.mapi (fun i w -> i, w)
                          |> List.tryPick (fun (i, w) ->
                              match Map.tryFind i got with
                              | None -> Some(i, w, "(no TS output)")
                              | Some actual when actual <> w -> Some(i, w, actual)
                              | Some _ -> None)

                      match divergence with
                      | None -> ()
                      | Some(i, fs, ts) ->
                          failtestf "cross-host divergence at vector %d (seed 20260726)\n  F#: %s\n  TS: %s" i fs ts
              finally
                  try
                      File.Delete tmp
                  with _ ->
                      ())

          // ---- Phase 317 syntax-tree-emission leg + Phase 321 trust boundary ----

          testCase
              "scaffold injection-safety (F#): a hostile wire string cannot break out of the generated literal"
              (fun _ ->
                  // Emit the hostile node as F# value-source, drop it into the generated
                  // module, run it under fsi. If the payload broke out it would either fail
                  // to compile (fsi exit != 0) or execute `PWNED()` (output diverges) — the
                  // escaped literal makes it inert data, so the wire == the trusted encoder.
                  let valueSrc =
                      match Gen.fsharpValue miniIdl TNode hostileNode with
                      | Ok s -> s
                      | Error e -> failtestf "fsharpValue rejected the node: %s" e

                  let decls =
                      match Gen.fsharpModule "Scaffold" miniIdl generatedKinds with
                      | Ok m -> m.Replace("module Scaffold\n", "")
                      | Error e -> failtestf "codegen rejected the spike vocabulary: %A" e

                  let dllRef (name: string) =
                      "#r @\"" + Path.Combine(AppContext.BaseDirectory, name) + "\"\n"

                  // The generated module references Wire (Canon/JVal) + Tree (NodeWitness)
                  // + Validator (the runValidator scaffold).
                  let fsx =
                      dllRef "Fuaran.Core.Wire.dll"
                      + dllRef "Fuaran.Core.Tree.dll"
                      + dllRef "Fuaran.Core.Validator.dll"
                      + decls
                      // Phase 689 — the spike IDL now carries `'Msg`-producing handlers, so the
                      // generated `Node` is generic. The scaffold instantiates it; the injection
                      // question this test asks is about the emitted STRING literals, not `'Msg`.
                      + "\n\nlet __scaffolded: Node<unit> = "
                      + valueSrc
                      + "\nprintfn \"%s\" (encodeNode __scaffolded)\n"

                  let path =
                      Path.Combine(Path.GetTempPath(), sprintf "fuaran-scaffold-%s.fsx" (Guid.NewGuid().ToString("N")))

                  File.WriteAllText(path, fsx)

                  try
                      let psi = ProcessStartInfo("dotnet", "fsi \"" + path + "\"")
                      psi.RedirectStandardOutput <- true
                      psi.RedirectStandardError <- true
                      psi.UseShellExecute <- false

                      match
                          (try
                              Some(Process.Start psi)
                           with _ ->
                               None)
                      with
                      | None -> skiptest "dotnet not on PATH — F# scaffold injection-safety check skipped"
                      | Some p ->
                          let stdout = p.StandardOutput.ReadToEnd()
                          let stderr = p.StandardError.ReadToEnd()
                          p.WaitForExit()

                          // Proof of no-breakout: the source compiles/parses (exit 0) AND the
                          // wire equals the trusted encoder — a breakout would invalidate the
                          // source (non-zero exit) or alter the output. The payload's "PWNED()"
                          // text appears ONLY as escaped data inside the wire string.
                          if p.ExitCode <> 0 then
                              failtestf "fsi failed to compile the scaffolded source (possible breakout): %s" stderr

                          Expect.equal
                              (stdout.Replace("\r\n", "\n").Trim())
                              trustedWire.Value
                              "scaffolded F# wire equals the trusted encoder output (payload stayed inert data)"
                  finally
                      try
                          File.Delete path
                      with _ ->
                          ())

          testCase
              "scaffold injection-safety (TS): a hostile wire string cannot break out of the generated literal"
              (fun _ ->
                  let valueSrc = Gen.typescriptValue hostileNode
                  let tsModule = Gen.typescriptModule miniIdl generatedKinds

                  let harness = tsModule + "\n\nconsole.log(encodeNode(" + valueSrc + "));\n"

                  let path =
                      Path.Combine(Path.GetTempPath(), sprintf "fuaran-scaffold-%s.mjs" (Guid.NewGuid().ToString("N")))

                  File.WriteAllText(path, harness)

                  try
                      let psi = ProcessStartInfo("node", "\"" + path + "\"")
                      psi.RedirectStandardOutput <- true
                      psi.RedirectStandardError <- true
                      psi.UseShellExecute <- false

                      match
                          (try
                              Some(Process.Start psi)
                           with _ ->
                               None)
                      with
                      | None -> skiptest "node not on PATH — TS scaffold injection-safety check skipped"
                      | Some p ->
                          let stdout = p.StandardOutput.ReadToEnd()
                          let stderr = p.StandardError.ReadToEnd()
                          p.WaitForExit()

                          // Proof of no-breakout: the source compiles/parses (exit 0) AND the
                          // wire equals the trusted encoder — a breakout would invalidate the
                          // source (non-zero exit) or alter the output. The payload's "PWNED()"
                          // text appears ONLY as escaped data inside the wire string.
                          if p.ExitCode <> 0 then
                              failtestf "node failed running the scaffolded TS (possible breakout): %s" stderr

                          Expect.equal
                              (stdout.Replace("\r\n", "\n").Trim())
                              trustedWire.Value
                              "scaffolded TS wire equals the trusted encoder output (payload stayed inert data)"
                  finally
                      try
                          File.Delete path
                      with _ ->
                          ())

          testCase "scaffold rejects an unsupported value rather than mis-emitting" (fun _ ->
              // The syntax-tree-emission contract: error, never silently mangle. A value
              // whose shape doesn't match the IDL type is rejected.
              match Gen.fsharpValue miniIdl TStr (VInt 7) with
              | Error _ -> ()
              | Ok s -> failtestf "expected a rejection, got '%s'" s)

          testCase "provenance stamp records the source wire hash + actor + the holes trust-split invariant" (fun _ ->
              let header = Gen.provenanceHeader "//" "abc123" "agent:claude@4.8"
              Expect.stringContains header "abc123" "carries the source wire hash"
              Expect.stringContains header "agent:claude@4.8" "carries the typed actor"
              Expect.stringContains header "INERT" "states generated code is inert structure"
              Expect.stringContains header "human-bound" "states behaviour is human-bound (holes)") ]
