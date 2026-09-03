module Fuaran.Core.Tests.SecondDomainSpike

open System
open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Second-vocabulary readiness spike — throwaway code, keep-the-findings.
//
// `Fuaran.Core.Idl` is domain-generic by construction, but it has only ever been
// exercised against ONE vocabulary: the tree language whose needs chose the 16
// `IdlType` cases in the first place. One data point cannot tell a general model
// from a well-fitted one. This file declares a slice of a SECOND vocabulary — a
// document-shaped tree language, deliberately not this repository's own — and
// certifies it against a corpus of sample structured documents written in that
// vocabulary's wire shape, so what the engine cannot express surfaces as a
// failing certification rather than as an opinion. The corpus is vendored beside
// this file (`fixtures/second-domain/`), so the certification runs in any clone.
//
// The deliverable is the MEASUREMENT, not a shipped vocabulary. Nothing below is
// consumed by any other suite, no engine behaviour is changed by it, and the
// declared slice is not a contract anyone may depend on.
//
// WHAT THE SPIKE FOUND — the readiness report, recorded beside the code that
// produced it (each item is asserted by a test in this file, so it cannot rot
// silently into prose):
//
//  1. BLOCKER, now CLOSED (Phase 108) — the discriminator KEY was hard-coded to
//     `$type`, so a vocabulary that tags its unions with any other key (this one
//     uses a bare-string `kind`) could not be decoded or encoded by the
//     interpreter at all. The key is now DECLARED (`Idl.Wire.Discriminator`);
//     this vocabulary declares `"kind"` and the corpus decodes directly. The
//     go-red partner below pins that the slot is load-bearing: the same corpus
//     under a default-shape declaration is refused.
//
//  2. BLOCKER, now CLOSED (Phase 109) — the node ENVELOPE's SHAPE was
//     hard-coded to `{ id, kind: { $type, ...fields } }` where this vocabulary's
//     node is FLAT — tag, id and kind fields share one object. The nesting is
//     now DECLARED (`Idl.Wire.NodeEnvelope`); this vocabulary declares
//     `FlatKind`, and the shape adapter that quarantined both blockers is
//     DELETED — the certification below runs the corpus in its NATIVE shape
//     through the interpreter, the generated F# module (`DocGenerated.fs`) and
//     the generated TypeScript module (the leg the original spike skipped as
//     blocked on exactly this).
//
//  3. GAP, now CLOSED (Phase 111) — canonical key ORDER was Ordinal-sorted and
//     not declarable, where this vocabulary's own canonical encoder emits
//     DECLARATION order; a retrofitting adopter paid a corpus migration even
//     with (1) and (2) closed. The order is now the third declared axis
//     (`Idl.Wire.KeyOrder`); this vocabulary declares `Declared`, and every leg
//     below certifies against the corpus's OWN byte order — the interpreter,
//     the generated F# module and the generated TS module are byte-compatible
//     with the pre-existing corpus, which is the retrofit cost gone. Re-encode
//     NORMALISES any input key order to the declared one, so canonical form
//     stays unique; the go-red partner pins that a `Sorted` declaration
//     produces different bytes.
//
//  4. GAP, half-closed — no explicit-null optionality. `Optionality` offers
//     `Required | Optional | OmitDefault | HostOnly`; none of them says "always
//     present, `null` when absent", which is what this vocabulary's encoder does
//     for an absent optional. The READ half already exists in Core
//     (`Json.parseTolerantOfNull`, which erases a null member to absence) but the
//     IDL's own `Decode.decode` entry point calls strict `Json.parse`, so the
//     interpreter cannot reach it. The WRITE half is deliberately absent — the
//     estate's canonical form is null-free by decision — so the honest
//     disposition is `tolerate`: an adopting vocabulary omits rather than nulls.
//     Worth recording that the corpus does NOT exercise this: every fixture
//     populates its optionals, so a corpus-only probe would have missed it
//     entirely. See `an explicit null is not representable`.
//
//  5. NEGATIVE RESULT — transparent unions are NOT demanded by this vocabulary.
//     Every union position is tag-discriminated; no case is encoded bare. The
//     `TransparentUnion` rule costs this domain nothing — and since Phase 116 it is
//     DECLARED rather than hard-coded, so the negative result is now a property of
//     this vocabulary's own policy rather than of a name the engine happened to know.
//     It keeps `HardenPolicy.Default`, which names a union this vocabulary does not
//     have, so the answer is `None` for every union either way.
//
//  6. NEGATIVE RESULT — enum wire-strings are NOT demanded by this vocabulary.
//     Every closed set's wire string is already a legal F# case identifier, so
//     the case/wire split is unexercised here.
//
//  7. NEGATIVE RESULT — no tuple position surfaced. The multi-value shapes in
//     this slice are union cases with named fields, which `IdlUnionCase` already
//     models; the absent tuple type cost nothing.
//
//  8. BY DESIGN, not a gap — the vocabulary's DOCUMENT envelope (a version
//     stamp, the root node, and a semantic sidecar carrying declared fields,
//     bound values and provenance) has no IDL position, because the IDL is a
//     NODE-vocabulary tool. The slice certifies the root subtree; the envelope
//     stays hand-written above the generated layer, exactly where the
//     structure/policy boundary puts it.
//
// (5)–(7) are the useful shape of a negative result: they say the two gap
// closures already queued behind this evidence draw NO demand from this pilot,
// while (1) and (2) — which were not on the list at all — were hard blockers.
// Phases 108/109 closed both from exactly this evidence (re-confirmed by the
// third-vocabulary spike, `ScoreDomainSpike.fs`, before the closure).
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// The declared slice
// ---------------------------------------------------------------------------

let private f (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Annotations = Annotations.Empty }

let private req name t = f name t Required
let private opt name t = f name t Optional

/// The inline-run union: a recursive, tag-discriminated value union carried
/// inside run-bearing leaves rather than as tree nodes with identity.
let private runType = TUnion("Run", [])
let private runList = TList runType

let private kind tag category fields : IdlKind =
    { Tag = tag
      Category = category
      Annotations = Annotations.Empty
      Fields = fields }

/// A slice of a second domain's node vocabulary — eleven kinds chosen to span
/// the structural variety rather than to be complete: a root carrying an
/// optional string and two closed sets, containers over child nodes, a
/// run-bearing leaf, a boolean-bearing row, an optional list, and a
/// string-bearing leaf.
let docIdl: Idl =
    { Kinds =
        [ kind
              "Document"
              "structure"
              [ opt "title" TStr
                req "locale" (TEnum "Locale")
                req "numbering" (TEnum "Numbering")
                req "children" (TList TNode) ]
          kind
              "Section"
              "structure"
              [ req "heading" runList
                req "depth" (TEnum "HeadingDepth")
                req "children" (TList TNode) ]
          kind "Paragraph" "leaf" [ req "runs" runList ]
          kind "ListBlock" "structure" [ req "style" (TEnum "ListStyle"); req "children" (TList TNode) ]
          kind "ListItem" "structure" [ req "children" (TList TNode) ]
          kind "Table" "structure" [ opt "caption" runList; req "children" (TList TNode) ]
          kind "Row" "structure" [ req "isHeader" TBool; req "children" (TList TNode) ]
          kind "Cell" "leaf" [ req "runs" runList ]
          kind "Figure" "structure" [ req "source" TStr; req "children" (TList TNode) ]
          kind "Caption" "leaf" [ req "runs" runList ]
          kind "Footnote" "structure" [ req "children" (TList TNode) ] ]
      Unions =
        [ { Name = "Run"
            Params = []
            Cases =
              [ { Tag = "Text"
                  Fields = [ req "value" TStr ]
                  Annotations = Annotations.Empty }
                { Tag = "Emphasis"
                  Fields = [ req "runs" runList ]
                  Annotations = Annotations.Empty }
                { Tag = "Strong"
                  Fields = [ req "runs" runList ]
                  Annotations = Annotations.Empty }
                { Tag = "InlineRef"
                  Fields = [ req "target" TStr ]
                  Annotations = Annotations.Empty }
                { Tag = "InlineVariable"
                  Fields = [ req "field" TStr ]
                  Annotations = Annotations.Empty }
                { Tag = "Link"
                  Fields = [ req "text" TStr; req "url" TStr ]
                  Annotations = Annotations.Empty }
                { Tag = "Code"
                  Fields = [ req "value" TStr ]
                  Annotations = Annotations.Empty } ] } ]
      Enums =
        [ Declare.enumOf "Locale" [ "EnGB"; "EnUS" ]
          Declare.enumOf "Numbering" [ "NoNumbering"; "DecimalNumbering"; "LegalNumbering" ]
          Declare.enumOf "HeadingDepth" [ "H1"; "H2"; "H3"; "H4"; "H5"; "H6" ]
          Declare.enumOf "ListStyle" [ "Bulleted"; "Numbered"; "Lettered"; "Roman" ] ]
      Records = []
      Defaults = []
      // The second vocabulary's node carries nothing beside its identity and its
      // kind — an empty envelope, which is the `Idl` default.
      NodeFields = []
      Ops = []
      // Findings (1)/(2)/(3), closed: the wire shape is DECLARED (Phases
      // 108/109/111) — a bare-string `kind` discriminator, the flat node
      // envelope, and declaration-order canonical keys.
      Wire =
        { Discriminator = "kind"
          NodeEnvelope = NodeEnvelopeShape.FlatKind
          KeyOrder = KeyOrder.Declared }
      Harden = HardenPolicy.Default }

let private nodeTags = docIdl.Kinds |> List.map (fun k -> k.Tag) |> Set.ofList

let private runTags =
    docIdl.Unions
    |> List.collect (fun u -> u.Cases |> List.map (fun c -> c.Tag))
    |> Set.ofList

let private declaredTags = Set.union nodeTags runTags

// ---------------------------------------------------------------------------
// The shape adapter that used to sit here is DELETED (Phases 108/109). It
// quarantined findings (1) and (2) — renaming the discriminator and re-nesting
// every node boundary in both directions — so the rest of the model could be
// certified at all. Both hard-codings are now declared slots on the `Idl`
// (`Wire`), so the corpus decodes and encodes in its native shape and there is
// nothing left to adapt: its retirement onto the real slots was Phase 108's own
// closing task.
// ---------------------------------------------------------------------------
// Corpus resolution — vendored by default, overridable
//
// The certification legs used to resolve their fixtures out-of-band and report
// themselves SKIPPED when nothing was found, which made them inert in a fresh
// clone — the measurement was durable, the check was not. A bounded search for a
// corpus that identifies itself through its own manifest is also not sound: a
// workspace holding more than one such corpus resolves whichever the directory
// walk reaches first, and a certification pointed at the WRONG vocabulary fails
// with an empty fixture set, which reads as a defect in the declaration.
//
// So the vendored corpus beside this file is the DEFAULT and is always present:
// the legs run, or they fail — they never pass by resolving nothing, and they no
// longer skip. `FUARAN_SPIKE_CORPUS` remains, for pointing the same declaration
// at a richer corpus in the same layout; it is validated by shape and refused by
// name rather than falling back silently, because a typo that degrades to the
// vendored set would report a certification the operator did not ask for.
// ---------------------------------------------------------------------------

/// A directory is a corpus when it identifies itself as one: a manifest naming
/// the round-trip family, beside the directory holding it.
let private isCorpus (dir: string) =
    try
        let manifest = Path.Combine(dir, "manifest.json")

        File.Exists manifest
        && File.ReadAllText(manifest).Contains "\"modelRoundTrips\""
        && Directory.Exists(Path.Combine(dir, "model-roundtrips"))
    with _ ->
        false

/// The vendored corpus, located the way this project's other fixture stores are:
/// climb from the CWD / test binary to `tests/Fuaran.Core.Tests` by probing for a
/// stable marker file, rather than baking in a build-output-relative path.
let private vendoredCorpus () : string option =
    let rec climb (dir: string) (budget: int) : string option =
        if budget < 0 || isNull dir then
            None
        else
            let cand = Path.Combine(dir, "tests", "Fuaran.Core.Tests")

            if File.Exists(Path.Combine(cand, "Fuaran.Core.Tests.fsproj")) then
                Some cand
            else
                match Directory.GetParent dir with
                | null -> None
                | parent -> climb parent.FullName (budget - 1)

    [ Directory.GetCurrentDirectory(); AppContext.BaseDirectory ]
    |> List.tryPick (fun start -> climb start 12)
    |> Option.map (fun projectDir -> Path.Combine(projectDir, "fixtures", "second-domain"))

/// The corpus this run certifies against, with the reason on the failure path —
/// there is no third outcome in which the legs quietly do nothing.
let private resolveCorpus () : Result<string, string> =
    match Environment.GetEnvironmentVariable "FUARAN_SPIKE_CORPUS" with
    | ovr when not (String.IsNullOrWhiteSpace ovr) ->
        if isCorpus ovr then
            Ok ovr
        else
            Error(
                sprintf
                    "FUARAN_SPIKE_CORPUS is set to '%s', which is not a corpus: it must hold a manifest.json naming \"modelRoundTrips\" and a model-roundtrips/ directory. Unset it to use the vendored corpus."
                    ovr
            )
    | _ ->
        match vendoredCorpus () with
        | Some dir when isCorpus dir -> Ok dir
        | Some dir -> Error(sprintf "the vendored corpus at '%s' is missing or malformed" dir)
        | None ->
            Error
                "could not locate tests/Fuaran.Core.Tests (Fuaran.Core.Tests.fsproj marker) from the CWD or the test binary"

/// Every `kind` tag reachable in a parsed fixture — the fixture is IN SLICE only
/// when the declaration covers all of them, so the selection is derived from the
/// declaration and cannot silently include a kind this spike never declared.
let rec private tagsIn (j: JVal) : Set<string> =
    match j with
    | JArr xs -> xs |> List.map tagsIn |> Set.unionMany
    | JObj fields ->
        let here =
            fields
            |> List.choose (function
                | "kind", JStr t -> Some t
                | _ -> None)
            |> Set.ofList

        fields |> List.map (snd >> tagsIn) |> Set.unionMany |> Set.union here
    | _ -> Set.empty

/// The in-slice fixtures, as (name, root-node JSON value) pairs.
let private inSliceFixtures (corpus: string) =
    Directory.GetFiles(Path.Combine(corpus, "model-roundtrips"), "*.json")
    |> Array.toList
    |> List.choose (fun path ->
        match Json.parse (File.ReadAllText path) with
        | Error _ -> None
        | Ok(JObj fields) ->
            fields
            |> List.tryPick (function
                | "root", root -> Some root
                | _ -> None)
            |> Option.bind (fun root ->
                if Set.isSubset (tagsIn root) declaredTags then
                    Some(Path.GetFileNameWithoutExtension path, root)
                else
                    None)
        | Ok _ -> None)

/// The certification of one fixture against one IDL: the foreign root — in its
/// NATIVE shape, no adapter — is canonically rendered, decoded, re-encoded, and
/// the bytes compared. Byte-identity here means every field the fixture carries
/// is declared with the right name, type and optionality — a dropped, added,
/// retyped or mis-optional field all move the bytes.
///
/// The bytes it is measured against are the CORPUS's own — in the corpus's OWN
/// key order (finding (3), closed by Phase 111: this vocabulary's canonical
/// form is declaration order, and `renderOrdered` of the parsed fixture IS the
/// fixture's authored byte order, compacted).
let private certify (idl: Idl) (root: JVal) : Result<unit, string> =
    let expected = Canon.renderOrdered root

    match Decode.decode idl expected with
    | Error e -> Error("decode: " + e)
    | Ok value ->
        match Encode.encode idl value with
        | Error e -> Error("encode: " + e)
        | Ok actual when actual <> expected ->
            Error(sprintf "bytes differ:\n  expected %s\n  actual   %s" expected actual)
        | Ok _ -> Ok()

/// The declaration with `Paragraph`'s run list removed — the go-red control. A
/// certification that cannot fail is not a certification.
let private paragraphMissingRuns =
    { docIdl with
        Kinds =
            docIdl.Kinds
            |> List.map (fun k -> if k.Tag = "Paragraph" then { k with Fields = [] } else k) }

[<Tests>]
let tests =
    testList
        "Second-vocabulary readiness spike"
        [

          test "the declared slice is well-formed" {
              Expect.isEmpty (Declare.enumWireErrors docIdl) "every enum's case/wire mapping is well-formed"
              Expect.isEmpty (Declare.wireShapeErrors docIdl) "the declared wire shape is well-formed"

              Expect.equal
                  (List.length (List.distinct (docIdl.Kinds |> List.map (fun k -> k.Tag))))
                  (List.length docIdl.Kinds)
                  "kind tags are distinct"

              let enumNames = docIdl.Enums |> List.map (fun e -> e.Name) |> Set.ofList
              let unionNames = docIdl.Unions |> List.map (fun u -> u.Name) |> Set.ofList

              let rec referenced t =
                  match t with
                  | TEnum n -> [ Choice1Of2 n ]
                  | TUnion(n, args) -> Choice2Of2 n :: List.collect referenced args
                  | TList inner
                  | TMap inner -> referenced inner
                  | _ -> []

              let allTypes =
                  [ for k in docIdl.Kinds do
                        for fld in k.Fields -> fld.Type
                    for u in docIdl.Unions do
                        for c in u.Cases do
                            for fld in c.Fields -> fld.Type ]

              for r in List.collect referenced allTypes do
                  match r with
                  | Choice1Of2 n -> Expect.isTrue (enumNames.Contains n) (sprintf "enum '%s' is declared" n)
                  | Choice2Of2 n -> Expect.isTrue (unionNames.Contains n) (sprintf "union '%s' is declared" n)
          }

          // ---- findings (1) + (2), CLOSED: the declared shape decodes the native
          // ---- wire directly, and the go-red partners pin that the slots are
          // ---- load-bearing rather than decorative ----

          test "the foreign envelope decodes DIRECTLY under the declared shape — findings (1)/(2) are closed" {
              let authored =
                  VNode("p1", "Paragraph", [ "runs", VList [ VUnion("Text", [ "value", VStr "x" ]) ] ])

              let bytes =
                  match Encode.encode docIdl authored with
                  | Ok s -> s
                  | Error e -> failtestf "the slice must encode its own authored node (%s)" e

              // The emitted wire IS the vocabulary's native shape: a bare-string
              // `kind` discriminator sharing one flat object with the id.
              Expect.isTrue (bytes.Contains "\"kind\":\"Paragraph\"") "the declared discriminator tags the node"
              Expect.isFalse (bytes.Contains "$type") "nothing on this wire is $type-tagged"

              match Decode.decode docIdl bytes with
              | Ok roundTripped ->
                  match Encode.encode docIdl roundTripped with
                  | Ok again -> Expect.equal again bytes "the native shape round-trips byte-identically"
                  | Error e -> failtestf "re-encode: %s" e
              | Error e -> failtestf "the declared shape must decode its own wire (%s)" e
          }

          test "the go-red partner: the same wire under a DEFAULT-shape declaration is refused" {
              // Phase 108's acceptance shape: the fixture that round-trips under
              // the declared key is REFUSED under the default one — proof the
              // declaration, not some widened tolerance, is doing the work.
              let defaultShaped = { docIdl with Wire = WireShape.Default }

              let native =
                  match
                      Encode.encode
                          docIdl
                          (VNode("p1", "Paragraph", [ "runs", VList [ VUnion("Text", [ "value", VStr "x" ]) ] ]))
                  with
                  | Ok s -> s
                  | Error e -> failtestf "control: %s" e

              match Decode.decode defaultShaped native with
              | Ok _ ->
                  failtest
                      "the default-shape declaration decoded the flat kind-tagged wire — nothing is declared any more"
              | Error e ->
                  Expect.stringContains
                      e
                      "node"
                      (sprintf "the failure names the node envelope rather than a field-level defect (got: %s)" e)

              // And the inverse: the declared shape refuses the interpreter's
              // OLD nested `$type` shape — the two wires are disjoint, not lenient.
              let nested = """{"id":"p1","kind":{"$type":"Paragraph","runs":["x"]}}"""

              Expect.isTrue
                  (Result.isError (Decode.decode docIdl nested))
                  "the declared flat shape refuses the nested $type wire"
          }

          test "the key-order go-red partner: a Sorted declaration produces different bytes — finding (3)" {
              match resolveCorpus () with
              | Error e -> failtestf "corpus: %s" e
              | Ok corpus ->
                  let sorted =
                      { docIdl with
                          Wire =
                              { docIdl.Wire with
                                  KeyOrder = KeyOrder.Sorted } }

                  let divergent =
                      inSliceFixtures corpus
                      |> List.filter (fun (_, root) ->
                          let ordered = Canon.renderOrdered root

                          match Decode.decode docIdl ordered with
                          | Error _ -> false
                          | Ok v ->
                              match Encode.encode sorted v with
                              | Ok bytes -> bytes <> ordered
                              | Error _ -> false)

                  Expect.isNonEmpty
                      divergent
                      "at least one fixture's declared order differs from Ordinal — the key-order axis is load-bearing"
          }

          // ---- finding (4): the optionality model has no explicit-null case ----

          test "an explicit null is not representable — the optionality model has no null-when-absent case" {
              Expect.isTrue
                  (docIdl.Kinds
                   |> List.exists (fun k -> k.Fields |> List.exists (fun fld -> fld.Opt = Optional)))
                  "the slice declares at least one Optional field, which is what this is about"

              // The interpreter's strict parser has no null at all: `JVal` does not
              // model it, so a vocabulary that spells an absent optional `null`
              // cannot even be PARSED, let alone decoded.
              let withNull = """{"kind":"Document","id":"d","title":null,"children":[]}"""
              Expect.isTrue (Result.isError (Json.parse withNull)) "strict parse rejects a null member"

              // Core's null-tolerant read (the erase-to-absence policy) handles the
              // read half — but the IDL's own decode entry point does not offer it.
              Expect.isTrue
                  (Result.isOk (Json.parseTolerantOfNull withNull))
                  "the tolerant read exists in Core, unreached by Decode.decode"
          }

          // ---- findings (5) and (6): the negative results, pinned so they cannot rot ----

          test "no transparent union is demanded by this vocabulary" {
              for u in docIdl.Unions do
                  Expect.isNone
                      (TransparentUnion.tag docIdl.Harden u)
                      (sprintf "union '%s' has no transparent case — finding (5)" u.Name)
          }

          test "no enum needs a case/wire split in this vocabulary" {
              for e in docIdl.Enums do
                  Expect.isEmpty
                      e.Wires
                      (sprintf "enum '%s' spells its wire strings as its case names — finding (6)" e.Name)

                  for c in e.Cases do
                      Expect.isTrue
                          (c.Length > 0 && Char.IsUpper c[0] && c |> Seq.forall Char.IsLetterOrDigit)
                          (sprintf "enum '%s' case '%s' is a legal F# identifier" e.Name c)
          }

          // ---- the certification against the foreign corpus ----

          test "the declared slice round-trips the foreign corpus byte-identically" {
              match resolveCorpus () with
              | Error e -> failtestf "corpus: %s" e
              | Ok corpus ->
                  let fixtures = inSliceFixtures corpus

                  Expect.isGreaterThanOrEqual
                      (List.length fixtures)
                      6
                      "the slice covers enough of the corpus for the certification to mean something"

                  for (name, root) in fixtures do
                      match certify docIdl root with
                      | Ok() -> ()
                      | Error e -> failtestf "%s: %s" name e
          }

          test "the certification can go red — a field dropped from the declaration is caught" {
              match resolveCorpus () with
              | Error e -> failtestf "corpus: %s" e
              | Ok corpus ->
                  let fixtures = inSliceFixtures corpus

                  let failures =
                      fixtures
                      |> List.filter (fun (_, root) -> Result.isError (certify paragraphMissingRuns root))

                  Expect.isNonEmpty
                      failures
                      "removing a declared field must break the certification — otherwise it is not certifying"
          }

          // ---- the generated F# leg, in the vocabulary's NATIVE shape — the leg
          // ---- the original spike skipped as blocked on findings (1)/(2) ----

          test "drift guard: the generator still reproduces the committed DocGenerated.fs" {
              let generated =
                  match
                      Gen.fsharpModuleWith
                          Gen.GenSupport.Empty
                          "Fuaran.Core.Tests.DocGenerated"
                          docIdl
                          (docIdl.Kinds |> List.map (fun k -> k.Tag))
                  with
                  | Ok s -> s
                  | Error e -> failtestf "codegen rejected the second vocabulary: %A" e

              // Located by the same climb the corpus resolver uses.
              let path =
                  match vendoredCorpus () with
                  | Some corpus -> Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName corpus), "DocGenerated.fs")
                  | None -> failtest "could not locate tests/Fuaran.Core.Tests from the CWD or the test binary"

              if not (File.Exists path) then
                  failtestf "DocGenerated.fs not found at %s — regenerate with --regen-snapshots" path

              if Environment.GetEnvironmentVariable "FUARAN_REGEN" = "1" then
                  File.WriteAllText(path, generated)

              let strip (s: string) =
                  s |> Seq.filter (Char.IsWhiteSpace >> not) |> Seq.toArray |> String

              Expect.equal
                  (strip generated)
                  (strip (File.ReadAllText path))
                  "the generator no longer reproduces DocGenerated.fs — regenerate it (--regen-snapshots)"
          }

          test "the generated F# module round-trips the corpus in the native shape" {
              match resolveCorpus () with
              | Error e -> failtestf "corpus: %s" e
              | Ok corpus ->
                  let fixtures = inSliceFixtures corpus

                  Expect.isGreaterThanOrEqual
                      (List.length fixtures)
                      6
                      "the slice covers enough of the corpus for the certification to mean something"

                  for (name, root) in fixtures do
                      let expected = Canon.renderOrdered root

                      match DocGenerated.decodeNode expected with
                      | Error e -> failtestf "%s: generated decode: %s" name e
                      | Ok node ->
                          let actual = DocGenerated.encodeNode node

                          Expect.equal actual expected (sprintf "%s: generated round-trip bytes differ" name)
          }

          // ---- the generated TypeScript leg, in the vocabulary's NATIVE shape —
          // ---- one of the two legs the original spike skipped as blocked ----

          test "the generated TypeScript module round-trips the corpus in the native shape" {
              match resolveCorpus () with
              | Error e -> failtestf "corpus: %s" e
              | Ok corpus ->
                  let fixtures = inSliceFixtures corpus
                  let tags = docIdl.Kinds |> List.map (fun k -> k.Tag)
                  let tsModule = Gen.typescriptModule docIdl tags

                  let jsStr (s: string) = Text.Json.JsonSerializer.Serialize s

                  let wireJs =
                      fixtures
                      |> List.map (fun (name, root) ->
                          sprintf "  [%s, %s]," (jsStr name) (jsStr (Canon.renderOrdered root)))
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
                      Path.Combine(Path.GetTempPath(), sprintf "fuaran-doc-ts-%s.mjs" (Guid.NewGuid().ToString("N")))

                  File.WriteAllText(tmp, harness)

                  try
                      let psi = ChildProcess.redirected "node" ("\"" + tmp + "\"")

                      let proc =
                          try
                              Some(Diagnostics.Process.Start psi)
                          with _ ->
                              None

                      match proc with
                      | None -> skiptest "node not on PATH — TS leg skipped"
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

                          for (name, root) in fixtures do
                              let expectedWire = Canon.renderOrdered root

                              match Map.tryFind name got with
                              | Some actual ->
                                  Expect.equal actual expectedWire (sprintf "TS wire mismatch for '%s'" name)
                              | None -> failtestf "TS module produced no output for '%s'" name
                  finally
                      try
                          File.Delete tmp
                      with _ ->
                          ()
          } ]
