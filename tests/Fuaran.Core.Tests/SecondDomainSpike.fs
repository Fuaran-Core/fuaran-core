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
// document-shaped tree language from another Fuaran domain, whose conformance
// corpus is read out-of-band and never vendored here — and certifies it against
// that domain's real fixtures, so what the engine cannot express surfaces as a
// failing certification rather than as an opinion.
//
// The deliverable is the MEASUREMENT, not a shipped vocabulary. Nothing below is
// consumed by any other suite, no engine behaviour is changed by it, and the
// declared slice is not a contract anyone may depend on.
//
// WHAT THE SPIKE FOUND — the readiness report, recorded beside the code that
// produced it (each item is asserted by a test in this file, so it cannot rot
// silently into prose):
//
//  1. BLOCKER — the discriminator KEY is hard-coded to `$type`. `Canon.typed`
//     writes it and `Decode`'s `dollarType` reads it, so a vocabulary that tags
//     its unions with any other key (this one uses a bare-string `kind`) cannot
//     be decoded or encoded by the interpreter at all. Not a type-model gap: the
//     `Idl` value has no slot in which to say what the key is. See
//     `direct decode of the foreign envelope fails`.
//
//  2. BLOCKER — the node ENVELOPE's SHAPE is hard-coded. `encodeNode` emits
//     `{ id, kind: { $type, ...fields } }`: the kind body is NESTED under a
//     `kind` member and `id` is its sibling. The second vocabulary's node is
//     FLAT — tag, id and kind fields share one object. `Idl.NodeFields` declares
//     WHICH fields the envelope carries (Phase 690/698) but not WHERE they sit,
//     and the nesting is what differs. Same disposition as (1), and plausibly the
//     same change: a declared envelope SHAPE, not merely a declared field list.
//
//  3. GAP, tolerable for a new adopter / blocking for a retrofit — canonical key
//     ORDER is Ordinal-sorted and not declarable. `Canon.render` sorts; this
//     vocabulary's own canonical encoder emits DECLARATION order. So even with
//     (1) and (2) closed, generated hosts would not be byte-compatible with an
//     existing corpus. A vocabulary that adopts the IDL before it has a shipped
//     wire pays nothing here; one that adopts after pays a corpus migration.
//     This is the §4.1 adopt-before-calcification lesson, priced.
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
//     Worth recording that the stored corpus does NOT exercise this: every
//     fixture populates its optionals, so a corpus-only probe would have missed
//     it entirely. See `an explicit null is not representable`.
//
//  5. NEGATIVE RESULT — transparent unions are NOT demanded by this vocabulary.
//     Every union position is tag-discriminated; no case is encoded bare. The
//     `TransparentUnion` hard-coding (a known wart) costs this domain nothing.
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
// while (1) and (2) — which were not on the list at all — are hard blockers.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// The declared slice
// ---------------------------------------------------------------------------

let private f (name: string) (t: IdlType) (opt: Optionality) : IdlField = { Name = name; Type = t; Opt = opt }

let private req name t = f name t Required
let private opt name t = f name t Optional

/// The inline-run union: a recursive, tag-discriminated value union carried
/// inside run-bearing leaves rather than as tree nodes with identity.
let private runType = TUnion("Run", [])
let private runList = TList runType

let private kind tag category fields : IdlKind =
    { Tag = tag
      Category = category
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
                  Fields = [ req "value" TStr ] }
                { Tag = "Emphasis"
                  Fields = [ req "runs" runList ] }
                { Tag = "Strong"
                  Fields = [ req "runs" runList ] }
                { Tag = "InlineRef"
                  Fields = [ req "target" TStr ] }
                { Tag = "InlineVariable"
                  Fields = [ req "field" TStr ] }
                { Tag = "Link"
                  Fields = [ req "text" TStr; req "url" TStr ] }
                { Tag = "Code"
                  Fields = [ req "value" TStr ] } ] } ]
      Enums =
        [ Declare.enumOf "Locale" [ "EnGB"; "EnUS" ]
          Declare.enumOf "Numbering" [ "NoNumbering"; "DecimalNumbering"; "LegalNumbering" ]
          Declare.enumOf "HeadingDepth" [ "H1"; "H2"; "H3"; "H4"; "H5"; "H6" ]
          Declare.enumOf "ListStyle" [ "Bulleted"; "Numbered"; "Lettered"; "Roman" ] ]
      Records = []
      Defaults = []
      // The second vocabulary's node carries nothing beside its identity and its
      // kind — an empty envelope, which is the `Idl` default and is exactly what
      // finding (2) is about: what it carries is declarable, where it sits is not.
      NodeFields = []
      Ops = [] }

let private nodeTags = docIdl.Kinds |> List.map (fun k -> k.Tag) |> Set.ofList

let private runTags =
    docIdl.Unions
    |> List.collect (fun u -> u.Cases |> List.map (fun c -> c.Tag))
    |> Set.ofList

let private declaredTags = Set.union nodeTags runTags

// ---------------------------------------------------------------------------
// The shape adapter — findings (1) and (2), isolated to fifty lines
//
// This is NOT a workaround a real adopter could ship: it re-shapes every wire
// boundary in both directions, which is precisely the cost the engine exists to
// remove. It is here so the rest of the model can be certified against real
// fixtures instead of stopping at the first structural mismatch — the two
// blockers are quarantined into this module and nothing else in the file
// compensates for anything.
// ---------------------------------------------------------------------------

module private Shape =

    let private tagOf (fields: (string * JVal) list) =
        fields
        |> List.tryPick (function
            | "kind", JStr t -> Some t
            | _ -> None)

    let private strOf (name: string) (fields: (string * JVal) list) =
        fields
        |> List.tryPick (function
            | k, JStr s when k = name -> Some s
            | _ -> None)

    let private without (name: string) (fields: (string * JVal) list) =
        fields |> List.filter (fun (k, _) -> k <> name)

    /// Foreign shape → the interpreter's shape: rename the discriminator, and
    /// lift a node's kind body under a `kind` member beside its `id`.
    let rec toIdl (j: JVal) : JVal =
        match j with
        | JArr xs -> JArr(List.map toIdl xs)
        | JObj fields ->
            match tagOf fields with
            | Some tag ->
                let body = fields |> without "kind" |> List.map (fun (k, v) -> k, toIdl v)

                if nodeTags.Contains tag then
                    match strOf "id" body with
                    | Some id -> JObj [ "id", JStr id; "kind", Canon.typed tag (without "id" body) ]
                    // A kind WITHOUT an identity — the bare-kind wire position.
                    | None -> Canon.typed tag body
                else
                    Canon.typed tag body
            | None -> JObj(fields |> List.map (fun (k, v) -> k, toIdl v))
        | other -> other

    /// The inverse — the foreign document must be recoverable from the
    /// interpreter's round-trip, or the adapter is hiding a loss rather than
    /// isolating a shape.
    let rec fromIdl (j: JVal) : JVal =
        match j with
        | JArr xs -> JArr(List.map fromIdl xs)
        | JObj fields ->
            let asNode =
                match strOf "id" fields with
                | Some id ->
                    fields
                    |> List.tryPick (function
                        | "kind", JObj kf -> Some(id, kf)
                        | _ -> None)
                | None -> None

            match asNode with
            | Some(id, kf) ->
                match strOf "$type" kf with
                | Some tag ->
                    JObj(
                        ("kind", JStr tag)
                        :: ("id", JStr id)
                        :: (kf |> without "$type" |> List.map (fun (k, v) -> k, fromIdl v))
                    )
                | None -> JObj(fields |> List.map (fun (k, v) -> k, fromIdl v))
            | None ->
                match strOf "$type" fields with
                | Some tag ->
                    JObj(
                        ("kind", JStr tag)
                        :: (fields |> without "$type" |> List.map (fun (k, v) -> k, fromIdl v))
                    )
                | None -> JObj(fields |> List.map (fun (k, v) -> k, fromIdl v))
        | other -> other

// ---------------------------------------------------------------------------
// Corpus resolution — out-of-band, never vendored
//
// The foreign fixtures are not copied into this repo. `FUARAN_SPIKE_CORPUS`
// names the corpus directory; failing that, a bounded search looks for a corpus
// that IDENTIFIES ITSELF through its own manifest, so no foreign path or repo
// name is baked in here. Absent ⇒ the certification legs report themselves
// skipped; they never report green without their oracle.
// ---------------------------------------------------------------------------

let private uninteresting (name: string) =
    name.StartsWith "." || name = "bin" || name = "obj" || name = "node_modules"

let private isCorpus (dir: string) =
    try
        let manifest = Path.Combine(dir, "manifest.json")

        File.Exists manifest
        && File.ReadAllText(manifest).Contains "\"modelRoundTrips\""
        && Directory.Exists(Path.Combine(dir, "model-roundtrips"))
    with _ ->
        false

let private childrenOf (dir: string) =
    try
        Directory.GetDirectories dir
        |> Array.filter (Path.GetFileName >> uninteresting >> not)
        |> List.ofArray
    with _ ->
        []

let private tryFindCorpus () : string option =
    match Environment.GetEnvironmentVariable "FUARAN_SPIKE_CORPUS" with
    | e when not (String.IsNullOrWhiteSpace e) && isCorpus e -> Some e
    | _ ->
        let rec climb (dir: string) (budget: int) =
            if budget < 0 || isNull dir then
                None
            else
                let near = childrenOf dir |> List.collect (fun c -> c :: childrenOf c)

                match near |> List.tryFind isCorpus with
                | Some hit -> Some hit
                | None ->
                    match Directory.GetParent dir with
                    | null -> None
                    | parent -> climb parent.FullName (budget - 1)

        climb AppContext.BaseDirectory 12

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

/// The certification of one fixture against one IDL: the foreign root is shaped
/// into the interpreter's wire, decoded, re-encoded, and the bytes compared.
/// Byte-identity here means every field the fixture carries is declared with the
/// right name, type and optionality — a dropped, added, retyped or mis-optional
/// field all move the bytes.
let private certify (idl: Idl) (root: JVal) : Result<unit, string> =
    let expected = Canon.render (Shape.toIdl root)

    match Decode.decode idl expected with
    | Error e -> Error("decode: " + e)
    | Ok value ->
        match Encode.encode idl value with
        | Error e -> Error("encode: " + e)
        | Ok actual when actual <> expected ->
            Error(sprintf "bytes differ:\n  expected %s\n  actual   %s" expected actual)
        | Ok actual ->
            // And the foreign document must come back out — otherwise the adapter
            // is absorbing a loss rather than isolating a shape.
            match Json.parse actual with
            | Error e -> Error("re-parse: " + e)
            | Ok reparsed ->
                let recovered = Canon.render (Shape.fromIdl reparsed)
                let original = Canon.render root

                if recovered <> original then
                    Error(sprintf "foreign document not recovered:\n  original  %s\n  recovered %s" original recovered)
                else
                    Ok()

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

          // ---- finding (1) + (2): the two blockers, shown rather than asserted in prose ----

          test
              "direct decode of the foreign envelope fails — the discriminator key and the envelope shape are hard-coded" {
              let authored =
                  VNode("p1", "Paragraph", [ "runs", VList [ VUnion("Text", [ "value", VStr "x" ]) ] ])

              let idlBytes =
                  match Encode.encode docIdl authored with
                  | Ok s -> s
                  | Error e -> failtestf "control: the slice must encode its own authored node (%s)" e

              // Control: the interpreter's own shape round-trips.
              Expect.isTrue
                  (Result.isOk (Decode.decode docIdl idlBytes))
                  "control: the interpreter decodes its own shape"

              // The SAME document in the foreign shape — one adapter step away — does not.
              let foreign =
                  match Json.parse idlBytes with
                  | Ok j -> Canon.render (Shape.fromIdl j)
                  | Error e -> failtestf "control: %s" e

              Expect.notEqual foreign idlBytes "the two shapes really are different bytes"

              match Decode.decode docIdl foreign with
              | Ok _ ->
                  failtest
                      "the interpreter decoded the foreign envelope — findings (1)/(2) are stale and this file must be re-measured"
              | Error e ->
                  Expect.stringContains
                      e
                      "node"
                      (sprintf "the failure names the node envelope rather than a field-level defect (got: %s)" e)
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
              let withNull = """{"id":"d","kind":{"$type":"Paragraph","runs":[]},"title":null}"""
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
                      (TransparentUnion.tag u)
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
              match tryFindCorpus () with
              | None -> skiptest "the second vocabulary's corpus is not resolvable — set FUARAN_SPIKE_CORPUS"
              | Some corpus ->
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
              match tryFindCorpus () with
              | None -> skiptest "the second vocabulary's corpus is not resolvable — set FUARAN_SPIKE_CORPUS"
              | Some corpus ->
                  let fixtures = inSliceFixtures corpus

                  let failures =
                      fixtures
                      |> List.filter (fun (_, root) -> Result.isError (certify paragraphMissingRuns root))

                  Expect.isNonEmpty
                      failures
                      "removing a declared field must break the certification — otherwise it is not certifying"
          } ]
