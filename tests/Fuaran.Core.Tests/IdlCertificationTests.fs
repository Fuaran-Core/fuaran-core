module Fuaran.Core.Tests.IdlCertificationTests

open System
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Tests.ReferenceIdl

// ---------------------------------------------------------------------------
// Phase 114 — the engine's certification, WITHOUT a domain vocabulary.
//
// D14 said the UI vocabulary in this repo's tests is an engine-certification
// fixture, not a home, and that it may leave "once the engine's certification no
// longer rests on it". This file is what that sentence has to be measured against:
// the interpreter's byte-identity, the artifact round-trip, the regeneration triple
// and the codegen trust boundary, all certified over vocabularies no domain owns —
// the two VENDORED foreign ones (`docIdl`, `scoreIdl`, each with a corpus written
// outside this repo) and the Core-owned `refIdl` beside them.
//
// The division of labour, stated here because it is the claim and not a detail:
//
//   * `docIdl`   — a foreign corpus, a declared non-default wire shape (bare-string
//                  discriminator, flat node envelope, declaration key order), and a
//                  generated F# module that COMPILES in this project (`DocGenerated.fs`).
//   * `scoreIdl` — a second foreign corpus, records and omit-at-default at scale.
//   * `refIdl`   — the remainder of the type model neither foreign vocabulary uses:
//                  `TFn`, `THosted`, `TClosure`, `TOpaque`, `TJson`, `TMap`, `TVar`,
//                  `HostOnly`, declared annotations, a wire-mapped enum, and the op
//                  vocabulary (`TKind` / `TOp`).
//
// The coverage claim is ENFORCED below rather than asserted in this comment: a test
// walks the union of the three and fails if any `IdlType` or `Optionality` case is
// unreached. That is what makes the D14 completion criterion checkable instead of a
// judgement someone has to re-make.
// ---------------------------------------------------------------------------

/// The vocabularies the engine is certified over — no domain owns any of them.
let private neutralVocabularies =
    [ "reference", refIdl
      "second-domain", SecondDomainSpike.docIdl
      "score-domain", ScoreDomainSpike.scoreIdl
      "spike", Fuaran.Core.Idl.Spike.Fixtures.miniIdl ]

/// The round-trip sweep runs over the certification set PLUS the value-coverage
/// vocabulary, which exists only to exercise the artifact's `IdlValue` projection and
/// is deliberately not part of the engine-coverage claim above.
let private roundTripVocabularies =
    neutralVocabularies @ [ "value-coverage", valueCoverageIdl ]

// ---------------------------------------------------------------------------
// Coverage — the D14 completion criterion, mechanised.
// ---------------------------------------------------------------------------

let rec private typeCases (t: IdlType) : string list =
    match t with
    | TStr -> [ "TStr" ]
    | TInt -> [ "TInt" ]
    | TBool -> [ "TBool" ]
    | TFloat -> [ "TFloat" ]
    | TEnum _ -> [ "TEnum" ]
    | TUnion(_, args) -> "TUnion" :: (args |> List.collect typeCases)
    | TVar _ -> [ "TVar" ]
    | TNode -> [ "TNode" ]
    | TKind -> [ "TKind" ]
    | TOp -> [ "TOp" ]
    | TList inner -> "TList" :: typeCases inner
    | TMap v -> "TMap" :: typeCases v
    | TRecord _ -> [ "TRecord" ]
    | TClosure -> [ "TClosure" ]
    | TFn _ -> [ "TFn" ]
    | TOpaque -> [ "TOpaque" ]
    | TJson -> [ "TJson" ]
    | THosted _ -> [ "THosted" ]

let private optCase (o: Optionality) =
    match o with
    | Required -> "Required"
    | Optional -> "Optional"
    | OmitDefault _ -> "OmitDefault"
    | HostOnly -> "HostOnly"

let private allFields (idl: Idl) : IdlField list =
    (idl.Kinds |> List.collect _.Fields)
    @ (idl.Ops |> List.collect _.Fields)
    @ (idl.Unions |> List.collect (fun u -> u.Cases |> List.collect _.Fields))
    @ (idl.Records |> List.collect _.Fields)
    @ idl.NodeFields

let private everyTypeCase =
    [ "TStr"
      "TInt"
      "TBool"
      "TFloat"
      "TEnum"
      "TUnion"
      "TVar"
      "TNode"
      "TKind"
      "TOp"
      "TList"
      "TMap"
      "TRecord"
      "TClosure"
      "TFn"
      "TOpaque"
      "TJson"
      "THosted" ]

[<Tests>]
let coverage =
    testList
        "Phase 114 — the engine is certified without a domain vocabulary"
        [ testCase "the neutral vocabularies between them reach every IdlType case" (fun _ ->
              let reached =
                  neutralVocabularies
                  |> List.collect (fun (_, idl) -> allFields idl |> List.collect (fun f -> typeCases f.Type))
                  |> Set.ofList

              // `TClosure` is `TFn` without a declared signature — the pre-689 spelling,
              // still admitted by the model and still encoded, so it is named here rather
              // than treated as superseded.
              let missing = everyTypeCase |> List.filter (fun c -> not (reached.Contains c))

              Expect.isEmpty
                  missing
                  (sprintf
                      "these IdlType cases are reached by NO neutral vocabulary, so the engine's certification would rest on a domain's: %A"
                      missing))

          testCase "the neutral vocabularies between them reach every Optionality case" (fun _ ->
              let reached =
                  neutralVocabularies
                  |> List.collect (fun (_, idl) -> allFields idl |> List.map (fun f -> optCase f.Opt))
                  |> Set.ofList

              let missing =
                  [ "Required"; "Optional"; "OmitDefault"; "HostOnly" ]
                  |> List.filter (fun c -> not (reached.Contains c))

              Expect.isEmpty
                  missing
                  (sprintf "these Optionality cases are reached by no neutral vocabulary: %A" missing))

          testCase "the reference vocabulary declares what it says it declares" (fun _ ->
              Expect.isEmpty (Declare.enumWireErrors refIdl) "the wire-mapped enum is well-formed"
              Expect.isEmpty (Declare.wireShapeErrors refIdl) "the declared wire shape is well-formed"

              Expect.equal
                  (Artifact.canonicalise refIdl)
                  refIdl
                  "the reference vocabulary is authored in canonical order (so the round-trip law reads as an equality)") ]

// ---------------------------------------------------------------------------
// Byte identity — the interpreter over the reference vocabulary.
//
// The vendored samples carry the FOREIGN-corpus half of this proof; what is added
// here is the remainder of the type model, with the expected bytes hand-authored to
// the canonical rules rather than captured from the encoder.
// ---------------------------------------------------------------------------

[<Tests>]
let byteIdentity =
    testList
        "Phase 114 — reference-vocabulary byte identity"
        [ testCase "every node fixture encodes to its canonical bytes" (fun _ ->
              for name, value, expected in nodeCases do
                  match Encode.encode refIdl value with
                  | Ok actual -> Expect.equal actual expected (sprintf "byte mismatch encoding '%s'" name)
                  | Error m -> failtestf "encode failed for '%s': %s" name m)

          testCase "every node fixture decodes back to the authored value" (fun _ ->
              for name, value, wire in nodeCases do
                  match Decode.decode refIdl wire with
                  | Ok actual -> Expect.equal actual value (sprintf "decode did not reconstruct '%s'" name)
                  | Error m -> failtestf "decode failed for '%s': %s" name m)

          testCase "the op root round-trips in both directions" (fun _ ->
              for name, value, expected in opCases do
                  match Encode.encodeOp refIdl value with
                  | Ok actual -> Expect.equal actual expected (sprintf "byte mismatch encoding op '%s'" name)
                  | Error m -> failtestf "encodeOp failed for '%s': %s" name m

                  match Decode.decodeOp refIdl expected with
                  | Ok actual -> Expect.equal actual value (sprintf "decodeOp did not reconstruct '%s'" name)
                  | Error m -> failtestf "decodeOp failed for '%s': %s" name m)

          testCase "negative control: a mutated field diverges from its wire" (fun _ ->
              let mutated =
                  VNode("note-1", "Note", [ "body", VUnion("Inline", [ "text", VStr "Updated hourly" ]) ])

              let _, _, expected = nodeCases |> List.find (fun (n, _, _) -> n = "note-1")

              match Encode.encode refIdl mutated with
              | Ok actual -> Expect.notEqual actual expected "negative control unexpectedly matched"
              | Error m -> failtestf "negative control failed to encode: %s" m) ]

// ---------------------------------------------------------------------------
// Phase 124 — the value-carrying union default, and the refusal beside it.
//
// Two claims, and the second is the load-bearing one.
//
//   (1) A payload-carrying `OmitDefault` generates encoder and decoder arms that
//       AGREE with the declaration, in F# and in TypeScript. `refIdl`'s
//       `Measure.value` carries `Slot.Fixed(0.0)`, and the two `measure-*` fixtures
//       stand on either side of it: `measure-1` authors the slot away from its
//       default (so the key is emitted), `measure-2` authors it exactly at the
//       default (so it is not, and decode restores it). The TypeScript half is
//       EXECUTED under node, which is the only round trip here that runs rather
//       than reads.
//
//   (2) A declaration the generator cannot render REFUSES, on every path. Before
//       this phase the literal emitters answered `None` and four of the six paths
//       that consult them fell back to always-emit or `dReq` — so an artefact
//       could contradict its own IDL with a green build. Each go-red case below
//       names one of those paths, and each of them PASSED (silently emitted a
//       module) against the pre-124 generator.
//
// The honesty boundary on the F# half: this suite cannot invoke the F# compiler,
// so what it pins is the emitted TEXT — that the omit test is a `match` against the
// payload-carrying literal and that the decoder restores the same literal. That the
// literal is legal in both a pattern and an expression position is a compile-time
// property, and the vocabulary whose generated module this repository actually
// COMPILES is the vendored `docIdl` one, which declares no such default.
// ---------------------------------------------------------------------------

/// A vocabulary carrying one deliberately UNRENDERABLE default, placed at the named slot.
/// `TMap` has no default literal in any backend — its JS form compares by reference and its
/// F# form is not a pattern — so it is the shape each case below plants.
let private unrenderable = VMap [ "k", VStr "v" ]

let private oneKind (fields: IdlField list) : IdlKind =
    { Tag = "Probe"
      Category = "content"
      Annotations = Annotations.Empty
      Fields = fields }

let private field (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Annotations = Annotations.Empty }

/// The smallest vocabulary that generates at all: one kind, one required string field.
let private probeIdl: Idl =
    { Kinds = [ oneKind [ field "text" TStr Required ] ]
      Unions = []
      Enums = []
      Records = []
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      Harden = HardenPolicy.Default }

let private badMapField = field "bag" (TMap TStr) (OmitDefault unrenderable)

/// Each formerly-silent path, as a vocabulary that plants the unrenderable default there.
let private silentPaths: (string * Idl) list =
    [ "record field",
      { probeIdl with
          Records =
              [ { Name = "Bag"
                  Fields = [ badMapField ] } ]
          Kinds = [ oneKind [ field "bag" (TRecord "Bag") Required ] ] }
      "union-case field",
      { probeIdl with
          Unions =
              [ { Name = "Holder"
                  Params = []
                  Cases =
                    [ { Tag = "Some"
                        Fields = [ badMapField ]
                        Annotations = Annotations.Empty } ] } ]
          Kinds = [ oneKind [ field "held" (TUnion("Holder", [])) Required ] ] }
      "kind-spec field",
      { probeIdl with
          Kinds = [ oneKind [ badMapField ] ] }
      "node envelope field",
      { probeIdl with
          NodeFields = [ badMapField ] } ]

let private probeTags (idl: Idl) = idl.Kinds |> List.map _.Tag

let private isUnsupportedDefault (where: string) (r: Result<string, CodegenError>) =
    match r with
    | Error(CodegenError.UnsupportedDefault _) -> ()
    | Error other -> failtestf "%s: refused, but not as UnsupportedDefault: %A" where other
    | Ok _ -> failtestf "%s: an unrenderable default GENERATED a module — the silent divergence is back" where

[<Tests>]
let valueCarryingDefaults =
    testList
        "Phase 124 — value-carrying union defaults"
        [ testCase "the generated F# encoder tests the payload, not just the tag" (fun _ ->
              match Gen.fsharpModule "Phase124.Generated" refIdl (refIdl.Kinds |> List.map _.Tag) with
              | Error e -> failtestf "codegen rejected the reference vocabulary: %A" e
              | Ok src ->
                  // The omit test is a MATCH (Phase 691) against the full literal — a tag-only
                  // test would omit the key for `Fixed(7.0)` too, which is a wire-visible lie.
                  Expect.stringContains
                      src
                      "(match s.Value with | Slot.Fixed(0.0) -> None"
                      "the encoder's omit test carries the payload"

                  // The decoder restores the SAME literal, so an absent key rebuilds a value the
                  // encoder will omit again — which is what makes the round trip closed.
                  Expect.stringContains
                      src
                      "dDef \"value\" __fs (decSlot dFloat) (Slot.Fixed(0.0))"
                      "the decoder restores the payload-carrying default"

                  // And the smart constructor fills it, rather than taking it as a parameter.
                  Expect.stringContains src "Value = Slot.Fixed(0.0)" "the smart constructor fills the default")

          testCase "the generated TypeScript predicate and literal carry the payload" (fun _ ->
              match Gen.typescriptModule refIdl (refIdl.Kinds |> List.map _.Tag) with
              | Error e -> failtestf "TypeScript codegen rejected the reference vocabulary: %A" e
              | Ok src ->
                  Expect.stringContains
                      src
                      "s.value.$type === \"Fixed\" && s.value.value === 0"
                      "the TS omit predicate conjoins the tag test with a test per declared field"

                  Expect.stringContains
                      src
                      "{ $type: \"Fixed\", value: 0 }"
                      "the TS decoder restores the payload-carrying default")

          // The executed half. `measure-2` omits the slot on the wire and must come back with it;
          // `measure-1` carries a non-default payload and must keep it. Read against the F#
          // interpreter's own bytes, so a TS-only regression cannot hide behind a shared oracle.
          testCase "node round-trips both sides of the payload-carrying default" (fun _ ->
              match Gen.typescriptModule refIdl (refIdl.Kinds |> List.map _.Tag) with
              | Error e -> failtestf "TypeScript codegen rejected the reference vocabulary: %A" e
              | Ok tsModule ->
                  let cases = nodeCases |> List.filter (fun (n, _, _) -> n.StartsWith "measure-")

                  Expect.isGreaterThanOrEqual
                      (List.length cases)
                      2
                      "both sides of the default must be present or the round trip is one-sided"

                  let jsStr (s: string) = Text.Json.JsonSerializer.Serialize s

                  let wireJs =
                      cases
                      |> List.map (fun (name, _, wire) -> sprintf "  [%s, %s]," (jsStr name) (jsStr wire))
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
                      IO.Path.Combine(
                          IO.Path.GetTempPath(),
                          sprintf "fuaran-phase124-ts-%s.mjs" (Guid.NewGuid().ToString("N"))
                      )

                  IO.File.WriteAllText(tmp, harness)

                  try
                      let psi = ChildProcess.redirected "node" ("\"" + tmp + "\"")

                      let proc =
                          try
                              Some(Diagnostics.Process.Start psi)
                          with _ ->
                              None

                      match proc with
                      | None -> skiptest "node not on PATH — the executed TS round trip is skipped"
                      | Some p ->
                          let stdout = p.StandardOutput.ReadToEnd()
                          let stderr = p.StandardError.ReadToEnd()
                          p.WaitForExit()

                          if p.ExitCode <> 0 then
                              failtestf "node failed running the generated TS module: %s" stderr

                          let got =
                              stdout.Replace("\r\n", "\n").Split('\n')
                              |> Array.filter (fun l -> l <> "")
                              |> Array.map (fun l ->
                                  let parts = l.Split('\u0001')
                                  parts.[0], parts.[1])
                              |> Map.ofArray

                          for name, _, wire in cases do
                              match Map.tryFind name got with
                              | Some actual ->
                                  Expect.equal actual wire (sprintf "TS round-trip bytes differ for '%s'" name)
                              | None -> failtestf "the TS module produced no output for '%s'" name
                  finally
                      try
                          IO.File.Delete tmp
                      with _ ->
                          ())

          // ---- the go-red half: each formerly-silent path refuses BY NAME ----

          // The positive control for the four go-red cases below. Without it, each of them
          // would pass just as happily if the probe vocabularies were unGENERATABLE for some
          // unrelated reason — a refusal that arrives for the wrong cause is a probe measuring
          // something other than the question it was asked.
          testCase "the probe vocabularies generate once the unrenderable default is removed" (fun _ ->
              let renderable (f: IdlField) =
                  if f.Name = "bag" then { f with Opt = Optional } else f

              let fix (idl: Idl) =
                  { idl with
                      Kinds =
                          idl.Kinds
                          |> List.map (fun k ->
                              { k with
                                  Fields = k.Fields |> List.map renderable })
                      Records =
                          idl.Records
                          |> List.map (fun r ->
                              { r with
                                  Fields = r.Fields |> List.map renderable })
                      Unions =
                          idl.Unions
                          |> List.map (fun u ->
                              { u with
                                  Cases =
                                      u.Cases
                                      |> List.map (fun c ->
                                          { c with
                                              Fields = c.Fields |> List.map renderable }) })
                      NodeFields = idl.NodeFields |> List.map renderable }

              for where, idl in silentPaths do
                  let ok = fix idl

                  match Gen.fsharpModule "Phase124.Control" ok (probeTags ok) with
                  | Ok _ -> ()
                  | Error e -> failtestf "control (F#, %s): the probe vocabulary does not generate at all: %A" where e

                  match Gen.typescriptModule ok (probeTags ok) with
                  | Ok _ -> ()
                  | Error e ->
                      failtestf "control (TypeScript, %s): the probe vocabulary does not generate at all: %A" where e)

          testCase "an unrenderable default refuses the F# module on every path" (fun _ ->
              for where, idl in silentPaths do
                  Gen.fsharpModule "Phase124.Bad" idl (probeTags idl)
                  |> isUnsupportedDefault ("F#, " + where))

          testCase "an unrenderable default refuses the TypeScript module on every path" (fun _ ->
              // The TS backend had NO error case at all before this phase — no `Result`, no
              // refusal, nothing — so every one of these emitted a module whose omit tests
              // contradicted the vocabulary it was generated from. The node-envelope path is
              // absent here only because the TS envelope is emitted from the same
              // `tsSpecPieceOf` the kind-spec path already covers.
              for where, idl in silentPaths do
                  Gen.typescriptModule idl (probeTags idl)
                  |> isUnsupportedDefault ("TypeScript, " + where))

          testCase "a PROJECTED kind's default is checked even though its constructor is not emitted" (fun _ ->
              // Phase 945 skips the generated smart constructor for a projected kind, and that
              // skip took the default check with it: the only leg that ran `defaultExpr` over a
              // kind's fields was the constructor. The encoder still emitted the field.
              let idl =
                  { probeIdl with
                      Kinds = [ oneKind [ badMapField ] ] }

              let projection: Gen.KindProjection =
                  { SpecDecl = "ProbeSpec = { Bag: Map<string, string> }"
                    Encoder = "and private encProbeSpec (s: ProbeSpec) : JVal = JObj []"
                    Decoder = "and private decProbeSpec (j: JVal) : Result<ProbeSpec, string> = Ok { Bag = Map.empty }"
                    Mk = None }

              let support =
                  { Gen.GenSupport.Empty with
                      KindProjections = Map.ofList [ "Probe", projection ] }

              Gen.fsharpModuleWith support "Phase124.Projected" idl (probeTags idl)
              |> isUnsupportedDefault "F#, projected kind")

          testCase "the scaffold leg answers with the SAME typed case, rendered" (fun _ ->
              // `Gen.fsharpValue` publishes a plain-string channel, so it cannot carry the case
              // itself — but it can carry the case's own words rather than a second sentence of
              // its own invention, which is what "folded into the same case" has to mean here.
              let idl =
                  { probeIdl with
                      Kinds = [ oneKind [ badMapField ] ] }

              match Gen.fsharpValue idl TNode (VNode("p", "Probe", [])) with
              | Ok _ -> failtest "the scaffold emitted source for a default it cannot render"
              | Error m ->
                  Expect.stringContains
                      m
                      (CodegenError.describe (CodegenError.UnsupportedDefault(TMap TStr, unrenderable)))
                      "the scaffold's refusal is the module emitters' own case, rendered")

          testCase "a nullary spelling of a case that TAKES a payload is refused, not mis-emitted" (fun _ ->
              // `VUnion(tag, [])` matched ANY union before Phase 124, so a default authored
              // without its payload emitted the bare tag — which for a case with fields is a
              // FUNCTION, not a value. Refusing is the honest answer; the declaration is wrong.
              let idl =
                  { probeIdl with
                      Unions =
                          [ { Name = "Holder"
                              Params = []
                              Cases =
                                [ { Tag = "Of"
                                    Fields = [ field "n" TInt Required ]
                                    Annotations = Annotations.Empty } ] } ]
                      Kinds = [ oneKind [ field "held" (TUnion("Holder", [])) (OmitDefault(VUnion("Of", []))) ] ] }

              Gen.fsharpModule "Phase124.Nullary" idl (probeTags idl)
              |> isUnsupportedDefault "F#, a payload-carrying case spelled nullary") ]

// ---------------------------------------------------------------------------
// The artifact round-trip law (task 1).
//
// Two statements, and both are needed. The BYTE law is what a third party depends
// on: whatever `parse` returns must render back to the document it came from. The
// STRUCTURAL law is the stronger one and is what proves nothing is dropped — an
// omitted family would still satisfy the byte law forever, because it would
// re-render identically.
// ---------------------------------------------------------------------------

[<Tests>]
let artifactRoundTrip =
    testList
        "Phase 114 — idl.json round-trip law"
        [ testCase "render (parse (render idl)) = render idl, for every neutral vocabulary" (fun _ ->
              for name, idl in roundTripVocabularies do
                  let text = Artifact.render idl

                  match Artifact.parse text with
                  | Error m -> failtestf "'%s': parse rejected its own rendering: %s" name m
                  | Ok reparsed -> Expect.equal (Artifact.render reparsed) text (sprintf "'%s': bytes not stable" name))

          testCase "parse (render idl) = canonicalise idl, for every neutral vocabulary" (fun _ ->
              for name, idl in roundTripVocabularies do
                  match Artifact.parse (Artifact.render idl) with
                  | Error m -> failtestf "'%s': parse rejected its own rendering: %s" name m
                  | Ok reparsed ->
                      Expect.equal
                          reparsed
                          (Artifact.canonicalise idl)
                          (sprintf "'%s': the artifact does not carry the whole vocabulary" name))

          testCase "the reparsed vocabulary encodes and decodes identically" (fun _ ->
              // The law above is structural; this is the consequence a domain actually
              // cares about — a vocabulary loaded from bytes produces the same wire as the
              // one that was compiled.
              match Artifact.parse (Artifact.render refIdl) with
              | Error m -> failtestf "parse rejected its own rendering: %s" m
              | Ok reparsed ->
                  for name, value, expected in nodeCases do
                      match Encode.encode reparsed value with
                      | Ok actual -> Expect.equal actual expected (sprintf "loaded vocabulary re-encodes '%s'" name)
                      | Error m -> failtestf "loaded vocabulary failed to encode '%s': %s" name m

                  for name, value, wire in opCases do
                      match Encode.encodeOp reparsed value with
                      | Ok actual -> Expect.equal actual wire (sprintf "loaded vocabulary re-encodes op '%s'" name)
                      | Error m -> failtestf "loaded vocabulary failed to encode op '%s': %s" name m)

          // The value half of the law. `refIdl`'s own defaults are scalars (the F# emitter
          // admits nothing else), so without this the artifact's `IdlValue` projection
          // would be certified on four of its fifteen cases.
          testCase "every IdlValue case survives the round trip" (fun _ ->
              Expect.equal
                  (Artifact.canonicalise valueCoverageIdl)
                  valueCoverageIdl
                  "the coverage vocabulary is authored in canonical order"

              let cases = valueCoverageIdl.Defaults |> List.map (fun d -> d.Field) |> Set.ofList

              let expected =
                  Set.ofList
                      [ "absent"
                        "bool"
                        "closure"
                        "enum"
                        "float"
                        "int"
                        "json"
                        "list"
                        "map"
                        "node"
                        "nodeEnv"
                        "opaque"
                        "record"
                        "str"
                        "union" ]

              Expect.equal cases expected "every IdlValue case is represented"

              match Artifact.parse (Artifact.render valueCoverageIdl) with
              | Error m -> failtestf "parse rejected the value-coverage vocabulary: %s" m
              | Ok reparsed ->
                  Expect.equal reparsed valueCoverageIdl "a value case is lost or altered by the round trip")

          // The ordering contract as a statement about VALUES: two vocabularies the
          // artifact cannot tell apart canonicalise to the same thing and parse back
          // identically. Without this, `canonicalise` could be the identity and the law
          // above would still hold.
          testCase "a reshuffled authoring canonicalises onto the same vocabulary" (fun _ ->
              Expect.notEqual unsortedCoverageIdl valueCoverageIdl "the reshuffled authoring is genuinely different"

              Expect.equal
                  (Artifact.canonicalise unsortedCoverageIdl)
                  valueCoverageIdl
                  "canonicalise does not normalise a reshuffled authoring"

              Expect.equal
                  (Artifact.render unsortedCoverageIdl)
                  (Artifact.render valueCoverageIdl)
                  "a reshuffle produces a diff in the artifact"

              match Artifact.parse (Artifact.render unsortedCoverageIdl) with
              | Error m -> failtestf "parse rejected the reshuffled rendering: %s" m
              | Ok reparsed -> Expect.equal reparsed valueCoverageIdl "the reshuffled artifact reads back canonically")

          // Phase 116 — the acceptance stated as a test rather than as a claim about
          // bytes nobody re-renders. A vocabulary carrying the tokens the engine used to
          // hard-code emits NO harden block, so its artifact cannot have moved; one that
          // declares its own emits it. Both branches are exercised, because
          // `refIdl` declares its own and every other vocabulary here does not.
          testCase "the harden block is omitted at the default and present when declared" (fun _ ->
              for name, idl in roundTripVocabularies do
                  let text = Artifact.render idl

                  if idl.Harden = HardenPolicy.Default then
                      Expect.isFalse
                          (text.Contains "\"harden\"")
                          (sprintf "'%s' declares the default tokens, so its artifact must be byte-unchanged" name)
                  else
                      Expect.stringContains
                          text
                          "\"harden\""
                          (sprintf "'%s' declares its own tokens, so the artifact must carry them" name))

          testCase "canonicalise is idempotent" (fun _ ->
              for name, idl in roundTripVocabularies do
                  let once = Artifact.canonicalise idl

                  Expect.equal (Artifact.canonicalise once) once (sprintf "'%s': canonicalise is not a fixpoint" name))

          // `transparentCase` is DERIVED — from the vocabulary's declared
          // `Harden.TransparentUnions` since Phase 116 — so the projection publishes it
          // per union for a third-party decoder and the reader deliberately ignores it,
          // reconstructing it from the declaration instead. Pinned, because "ignored on
          // purpose" and "forgotten" look identical in a passing round-trip otherwise.
          //
          // The union is named in this vocabulary's own words, which is the point: the
          // transparent set used to be a hard-coded name and is a declaration now.
          testCase "a derived transparentCase key is published and read back as derived" (fun _ ->
              let transparent =
                  { refIdl with
                      Unions =
                          [ { Name = "Text"
                              Params = []
                              Cases =
                                [ { Tag = "Inline"
                                    Fields =
                                      [ { Name = "text"
                                          Type = TStr
                                          Opt = Required
                                          Annotations = Annotations.Empty } ]
                                    Annotations = Annotations.Empty } ] } ]
                      Harden =
                          { refIdl.Harden with
                              TransparentUnions = [ "Text", "Inline" ] }
                      Kinds = []
                      Ops = []
                      Records = []
                      Defaults = []
                      NodeFields = [] }

              let text = Artifact.render transparent
              Expect.stringContains text "\"transparentCase\": \"Inline\"" "the derived key is published"

              Expect.stringContains
                  text
                  "\"transparentUnions\""
                  "the DECLARATION the key derives from is published beside it"

              match Artifact.parse text with
              | Error m -> failtestf "parse rejected a transparent union: %s" m
              | Ok reparsed ->
                  Expect.equal
                      reparsed
                      (Artifact.canonicalise transparent)
                      "the derived key round-trips without becoming a declared one")

          testCase "an artifact from another encoding version is refused by name" (fun _ ->
              let text = (Artifact.render refIdl).Replace("\"version\": 1", "\"version\": 2")

              match Artifact.parse text with
              | Ok _ -> failtest "a future encoding version was accepted — a dropped family would be silent"
              | Error m ->
                  Expect.stringContains m "encoding version 2" "the refusal names the version it read"
                  Expect.stringContains m "reads version 1" "and the version it can read")

          testCase "a truncated artifact is refused rather than silently thinned" (fun _ ->
              match Artifact.parse "{\"version\": 1}" with
              | Ok _ -> failtest "an artifact with no families was accepted"
              | Error m -> Expect.stringContains m "kinds" "the refusal names the first family it could not find") ]

// ---------------------------------------------------------------------------
// The declaration triple (task 2) — vocabulary + declared support + host prelude,
// all three carried as files a domain holds.
// ---------------------------------------------------------------------------

[<Tests>]
let declarationTriple =
    testList
        "Phase 114 — the regeneration triple"
        [ testCase "render (parse (render support)) = render support" (fun _ ->
              let text = SupportArtifact.render support

              match SupportArtifact.parse text with
              | Error m -> failtestf "parse rejected its own rendering: %s" m
              | Ok reparsed -> Expect.equal (SupportArtifact.render reparsed) text "support bytes are not stable")

          testCase "parse (render support) = support — every channel survives" (fun _ ->
              match SupportArtifact.parse (SupportArtifact.render support) with
              | Error m -> failtestf "parse rejected its own rendering: %s" m
              | Ok reparsed ->
                  Expect.equal reparsed support "the support document does not carry the whole declared-support record")

          testCase "an empty support document round-trips and adds no keys" (fun _ ->
              // The KEY, quoted — the document's own prose says the words "splices" and
              // "host prelude", so a bare substring test would pass for the wrong reason.
              let text = SupportArtifact.render SupportDocument.Empty
              Expect.isFalse (text.Contains "\"docs\":") "a support-free document declares no docs"
              Expect.isFalse (text.Contains "\"splices\":") "nor splices"
              Expect.isFalse (text.Contains "\"hostPrelude\":") "nor a prelude"

              match SupportArtifact.parse text with
              | Error m -> failtestf "parse rejected the empty document: %s" m
              | Ok reparsed -> Expect.equal reparsed SupportDocument.Empty "the empty document round-trips")

          testCase "the host prelude is NAMED, not inlined" (fun _ ->
              // The declaration is a module name and a relative path — the prelude's SOURCE
              // stays the one file the domain compiles. Inlining it would mint a second copy
              // of a compiled artefact with nothing keeping the two equal.
              match support.HostPrelude with
              | None -> failtest "the reference support document declares no prelude"
              | Some p ->
                  Expect.equal p.Module "Fuaran.Core.Tests.ReferencePrelude" "the prelude names its module"
                  Expect.equal p.Path "ReferencePrelude.fs" "and a path relative to the document")

          // The acceptance criterion, on the neutral vocabulary: a domain holding the
          // three files regenerates its structural layer against the packaged engine,
          // with no sibling checkout involved. A demonstration at a whole domain's
          // scale is that domain's own gate, in that domain's repository — which is
          // the point of the claim, not an omission from it (DECISIONS.md D14).
          testCase "a vocabulary + support document loaded FROM BYTES regenerates the same module" (fun _ ->
              let emit (idl: Idl) (s: Gen.GenSupport) =
                  Gen.fsharpModuleWith s "Fuaran.Core.Tests.ReferenceGenerated" idl (idl.Kinds |> List.map _.Tag)

              let fromMemory = emit refIdl support.Support

              let fromBytes =
                  match
                      Artifact.parse (Artifact.render refIdl), SupportArtifact.parse (SupportArtifact.render support)
                  with
                  | Ok idl, Ok doc -> emit idl doc.Support
                  | Error m, _ -> failtestf "the vocabulary did not load: %s" m
                  | _, Error m -> failtestf "the support document did not load: %s" m

              match fromMemory, fromBytes with
              | Ok a, Ok b -> Expect.equal b a "the triple loaded from bytes emits a different module"
              | Error e, _ -> failtestf "codegen rejected the in-memory triple: %A" e
              | _, Error e -> failtestf "codegen rejected the loaded triple: %A" e) ]

// ---------------------------------------------------------------------------
// The sanitisation floor and the codegen trust boundary (task 3).
//
// MOVED here from `IdlUiTests` rather than copied. `Sanitize.*` was already
// domain-neutral — pure string functions that never touch a vocabulary — and the
// `Trust` cases only needed a vocabulary to have kinds to harden, which `refIdl`
// supplies. Leaving them beside the UI vocabulary would have meant the engine's
// security floor left the repo with a domain's contract.
//
// Every Phase 96 case below FAILED against a pre-96 implementation; a regression
// here is a real divergence, not a style drift.
// ---------------------------------------------------------------------------

let private allow (m: string) (c: string) (h: string) : Trust.AllowEntry =
    { ModuleId = m
      ComponentId = c
      Hash = h }

[<Tests>]
let sanitisationFloor =
    testList
        "Phase 114 — the sanitisation floor (domain-neutral)"
        [ testCase "URL default-deny (dangerous/unknown schemes → about:blank; safe pass)" (fun _ ->
              Expect.equal (Sanitize.sanitizeUrlOrBlank "javascript:alert(1)") "about:blank" "javascript: blocked"
              Expect.equal (Sanitize.sanitizeUrlOrBlank "JAVA\tSCRIPT:alert(1)") "about:blank" "obfuscated js blocked"
              Expect.equal (Sanitize.sanitizeUrlOrBlank "vbscript:x") "about:blank" "vbscript blocked"
              Expect.equal (Sanitize.sanitizeUrlOrBlank "data:text/html,x") "about:blank" "data: blocked (unknown)"
              Expect.equal (Sanitize.sanitizeUrlOrBlank "//evil.com/x") "about:blank" "protocol-relative blocked"
              Expect.equal (Sanitize.sanitizeUrlOrBlank "/about") "/about" "relative allowed"
              Expect.equal (Sanitize.sanitizeUrlOrBlank "https://example.com") "https://example.com" "https allowed"
              Expect.equal (Sanitize.sanitizeUrlOrBlank "mailto:a@b.com") "mailto:a@b.com" "mailto allowed")

          testCase "attribute-key allowlist (data-*/aria-* only; on*/other rejected)" (fun _ ->
              Expect.isTrue (Sanitize.isAllowedAttributeKey "data-test") "data- allowed"
              Expect.isTrue (Sanitize.isAllowedAttributeKey "aria-label") "aria- allowed"
              Expect.isFalse (Sanitize.isAllowedAttributeKey "onclick") "on* rejected"
              Expect.isFalse (Sanitize.isAllowedAttributeKey "class") "arbitrary rejected")

          testCase "markdown scrub removes dangerous elements + schemes, keeps benign text" (fun _ ->
              let scrubbed =
                  Sanitize.scrubMarkdown "hello <script>alert(1)</script> world javascript:x"

              Expect.isFalse (scrubbed.Contains "<script") "script tag removed"
              Expect.isFalse (scrubbed.Contains "javascript:") "javascript scheme removed"
              Expect.stringContains scrubbed "hello" "benign text preserved"
              Expect.equal (Sanitize.scrubMarkdown "Updated hourly.") "Updated hourly." "benign markdown untouched")

          testCase "the scheme scrub cannot resurrect a spliced match (Phase 96)" (fun _ ->
              // Deleting the match splices the halves together and re-forms the pattern;
              // a single non-rescanning pass then emits it. The old code turned this input
              // into a live `javascript:alert(1)` — the sanitiser constructing the payload
              // it exists to remove.
              let scrubbed = Sanitize.scrubMarkdown "javascjavascript:ript:alert(1)"
              Expect.isFalse (scrubbed.Contains "javascript:") "no scheme reconstructed from the halves"

              let inHref =
                  Sanitize.scrubMarkdown "<a href=\"javascjavascript:ript:alert(1)\">x</a>"

              Expect.isFalse (inHref.Contains "javascript:") "nor in an href position"

              Expect.isFalse ((Sanitize.scrubMarkdown "vbscvbscript:ript:x").Contains "vbscript:") "same for vbscript:")

          testCase "inline on*= handlers are stripped from tag interiors (Phase 96)" (fun _ ->
              Expect.equal
                  (Sanitize.scrubMarkdown "<a href=\"x\" onclick=\"alert(1)\">x</a>")
                  "<a href=\"x\">x</a>"
                  "quoted handler removed"

              Expect.isFalse
                  ((Sanitize.scrubMarkdown "<img src=\"x\" onerror=alert(1)>").Contains "onerror")
                  "unquoted handler removed"

              Expect.isFalse
                  ((Sanitize.scrubMarkdown "<div onload>x</div>").Contains "onload")
                  "boolean handler removed")

          testCase "the tag-interior anchor leaves prose intact (Phase 96)" (fun _ ->
              // Without the anchor the scan matches whitespace-`on`-letter in ordinary
              // English and the boolean-attribute branch deletes the word from body text.
              let prose = "Only one once, online and onto the next."
              Expect.equal (Sanitize.scrubMarkdown prose) prose "words beginning `on` survive outside a tag")

          testCase "attribute values reject controls and angle brackets (Phase 96)" (fun _ ->
              Expect.isTrue (Sanitize.isSafeAttributeValue "plain value") "benign value passes"
              Expect.isTrue (Sanitize.isSafeAttributeValue "tab\there") "tab tolerated"
              Expect.isFalse (Sanitize.isSafeAttributeValue "a<b") "angle bracket rejected"
              Expect.isFalse (Sanitize.isSafeAttributeValue ("nul" + string (char 1) + "here")) "C0 control rejected"
              Expect.isFalse (Sanitize.isAllowedAttributeKey "style") "style rejected"

              let filtered =
                  Sanitize.sanitizeAttributes (
                      Map.ofList [ "data-ok", "fine"; "data-bad", "a<b"; "onclick", "alert(1)" ]
                  )

              Expect.equal (Map.toList filtered) [ "data-ok", "fine" ] "only the safe data- entry survives") ]

[<Tests>]
let trustBoundary =
    testList
        "Phase 114 — the codegen trust boundary (domain-neutral)"
        [
          // Phase 116 — the claim this whole list rests on, stated as an assertion
          // rather than as the header comment it used to be. A vocabulary that
          // "supplies its own policy" and one that "happens to agree with the engine's
          // default" are indistinguishable in every test below; only this one can tell
          // them apart, and without it the floor could drift back to being hard-coded
          // while the suite stayed green.
          testCase "the reference vocabulary shares NO hardening token with the default" (fun _ ->
              let d = HardenPolicy.Default
              let r = refIdl.Harden

              let distinct =
                  [ "gatedKind", d.GatedKind, r.GatedKind
                    "placeholderKind", d.PlaceholderKind, r.PlaceholderKind
                    "placeholderField", d.PlaceholderField, r.PlaceholderField
                    "textLiteralCase", d.TextLiteralCase, r.TextLiteralCase
                    "valueLiteralCase", d.ValueLiteralCase, r.ValueLiteralCase ]

              for name, engineToken, ours in distinct do
                  Expect.notEqual ours engineToken (sprintf "'%s' still spells the default's token" name)

              // The two field members are separate for a reason — a vocabulary may
              // spell the placeholder KIND's field and the literal CASE's field
              // differently, and this one does. Merging them would make such a
              // vocabulary unmodellable.
              Expect.notEqual
                  r.PlaceholderField
                  r.TextLiteralField
                  "the placeholder field and the literal-case field are separately declared")

          testCase "an unhashed gated node becomes an inert placeholder (never a live call)" (fun _ ->
              match Trust.harden refIdl (trustPolicy [ allow "analytics" "trend-card" "whatever" ]) embed1 with
              | VNode(id, "Note", fields) ->
                  Expect.equal id "embed-1" "the placeholder preserves the node id"

                  match fields with
                  | [ (_, VUnion("Inline", [ (_, VStr label) ])) ] ->
                      Expect.stringContains label "inert placeholder" "labelled as inert"
                      Expect.stringContains label "trend-card" "names the gated component"
                  | _ -> failtest "unexpected placeholder shape"
              | VNode(_, "Embed", _) -> failtest "an unhashed gated node must NOT stay live"
              | _ -> failtest "unexpected node")

          testCase "allowlisted + hash-verified gated node passes through live" (fun _ ->
              match
                  Trust.harden refIdl (trustPolicy [ allow "deal-flow" "QualityRing" "abc123def456" ]) embedBounded1
              with
              | VNode(_, "Embed", _) -> ()
              | _ -> failtest "an allowlisted + hash-matched gated node should stay live"

              match Trust.harden refIdl (trustPolicy []) embedBounded1 with
              | VNode(_, "Note", _) -> ()
              | _ -> failtest "a non-allowlisted gated node must be gated to inert")

          testCase "hash mismatch is inert under StrictReplay, advisory-live under AdvisoryWarning" (fun _ ->
              match Trust.harden refIdl (trustPolicy [ allow "deal-flow" "QualityRing" "WRONG" ]) embedBounded1 with
              | VNode(_, "Note", _) -> ()
              | _ -> failtest "a StrictReplay hash mismatch must be gated to inert"

              match Trust.harden refIdl (trustPolicy [ allow "deal-flow" "TrendCard" "WRONG" ]) embedAdvisory1 with
              | VNode(_, "Embed", _) -> ()
              | _ -> failtest "an AdvisoryWarning hash mismatch should stay live (advisory)")

          testCase "a hostile URL and a hostile markdown body are cleaned before emission" (fun _ ->
              let hostileLink =
                  VNode(
                      "l",
                      "Link",
                      [ "href", VUnion("Fixed", [ "value", VStr "javascript:alert(document.cookie)" ])
                        "label", VUnion("Inline", [ "text", VStr "Click" ])
                        "onClick", VClosure ]
                  )

              match Trust.harden refIdl (trustPolicy []) hostileLink |> Encode.encode refIdl with
              | Ok wire ->
                  Expect.isFalse (wire.Contains "javascript:") "the javascript: URL was sanitised out of the wire"
                  Expect.stringContains wire "about:blank" "replaced with the deny sentinel"
              | Error m -> failtestf "hostile link encode failed: %s" m

              let hostileMd =
                  VNode("m", "Note", [ "body", VUnion("Inline", [ "text", VStr "hi <script>evil()</script>" ]) ])

              match Trust.harden refIdl (trustPolicy []) hostileMd |> Encode.encode refIdl with
              | Ok wire -> Expect.isFalse (wire.Contains "<script") "the script tag was scrubbed"
              | Error m -> failtestf "hostile markdown encode failed: %s" m)

          testCase "harden → scaffold emits inert, provenance-stamped source (no live gated node)" (fun _ ->
              match Trust.scaffoldFSharp (trustPolicy []) refIdl "wirehash-abc" "agent:reference" embed1 with
              | Ok src ->
                  Expect.stringContains src "INERT" "carries the provenance trust-split invariant"
                  Expect.stringContains src "wirehash-abc" "carries the source wire hash"
                  Expect.stringContains src "NodeKind.Note" "emits the inert placeholder, not a live component"
                  Expect.isFalse (src.Contains "NodeKind.Embed") "no live gated construction in the generated source"
              | Error m -> failtestf "scaffold failed: %s" m) ]
