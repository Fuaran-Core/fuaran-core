/// The engine's own reference VOCABULARY — a domain-neutral `Idl` that stands in for
/// a real domain's contract, the way `Reference.RNode` stands in for a real domain's
/// tree (DECISIONS.md D7).
///
/// **Why it exists (Phase 114, completing D14).** D14 recorded that the UI vocabulary
/// in this repo's tests is not a home but an engine-certification FIXTURE, and named
/// the completion criterion: it moves to the domain's own repo once the engine's
/// certification no longer rests on it. Two vendored foreign vocabularies already
/// carry most of that load — `SecondDomainSpike.docIdl` and `ScoreDomainSpike.scoreIdl`
/// certify the interpreter, the generated F# module and the generated TypeScript module
/// against corpora written OUTSIDE this repo, which is the strongest byte-identity
/// evidence available. What they do not reach is the part of the type model neither
/// foreign vocabulary happens to use: both declare `Ops = []`, no annotations, and none
/// of `TFn` / `THosted` / `TClosure` / `TOpaque` / `TJson` / `TMap` / `TVar` / `HostOnly`.
///
/// This vocabulary covers exactly that remainder, deliberately small. It is NOT an
/// attempt to grow a Core-owned fixture to the scale of the UI one — D14 offered that
/// as one of two routes and it is the worse one, because a 40-kind vocabulary in this
/// repo would be a domain in all but name. What certifies the engine is the union of
/// the three, and `IdlCertificationTests` states which leg each one carries.
///
/// **Neutrality, and one honest exception.** Nothing here names a domain: a group, a
/// link, a note and a measure are shapes any artefact language has. The exception is
/// that the codegen trust boundary hard-codes vocabulary tokens — `Trust.harden`
/// branches on the kind tag `Custom`, mints its inert placeholder as a `Markdown` node
/// carrying a `Literal` text, and `Trust.sanitiseUrlValue` matches a `Static` union
/// case with a `value` field. So a vocabulary that exercises the trust boundary must
/// spell those tokens, and this one does. They are the ENGINE's tokens, not a domain's;
/// that they are hard-coded rather than named by the policy is a real leak, recorded in
/// this phase's outcome rather than silently worked around here.
///
/// **Authored in canonical order.** Kinds, ops, unions, enums and records are declared
/// Ordinal-sorted by identity and every composite default has its named sub-values
/// sorted, so `Artifact.canonicalise refIdl = refIdl` holds exactly — which is what lets
/// `IdlCertificationTests` state the artifact round-trip law as an equality on THIS
/// vocabulary rather than only on bytes.
module Fuaran.Core.Tests.ReferenceIdl

open Fuaran.Core
open Fuaran.Core.Idl

let private f (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Annotations = Annotations.Empty }

let private req name t = f name t Required
let private opt name t = f name t Optional
let private omit name t d = f name t (OmitDefault d)

let private annotated (ann: Annotations) (field: IdlField) = { field with Annotations = ann }

let private kind tag category fields : IdlKind =
    { Tag = tag
      Category = category
      Fields = fields }

let private case tag fields : IdlUnionCase =
    { Tag = tag
      Fields = fields
      Annotations = Annotations.Empty }

/// A function-typed slot carrying its declared host signature — the `TFn` leg.
let private handler (fsharp: string) (ts: string) : IdlType =
    TFn
        { FSharp = fsharp
          TypeScript = ts
          Placeholder = "ignore" }

/// A wire-visible slot whose value is a HOST type with its own codec — the `THosted`
/// leg. The expressions name a prelude this repo does not compile (nothing here emits
/// and then builds the reference module), which is exactly the shape a real domain's
/// declaration takes.
let private series: IdlType =
    THosted
        { FSharp = "float list"
          Encode = "encSeries"
          Decode = "decSeries" }

// ---------------------------------------------------------------------------
// The vocabulary.
// ---------------------------------------------------------------------------

let refIdl: Idl =
    { Kinds =
        [ kind
              "Custom"
              "meta"
              [ req "componentId" TStr
                opt "contentHash" (TRecord "ContentHash")
                req "moduleId" TStr
                // HostOnly: declared on the host, absent from every encoding. Its type
                // must be a `TFn` — that is what carries the decoder's placeholder.
                f "onMount" (handler "unit -> unit" "() => void") HostOnly
                opt "props" (TMap TJson) ]
          kind
              "Group"
              "layout"
              [ req "children" (TList TNode)
                omit "layout" (TEnum "LayoutKind") (VEnum "Stack")
                req "onSelect" (handler "int -> unit" "(i: number) => void") ]
          kind
              "Link"
              "content"
              [ req "href" (TUnion("Slot", [ TStr ]))
                req "label" (TUnion("Text", []))
                // `TClosure` is `TFn` without a declared host signature — the pre-689
                // spelling, still admitted and still encoded as the same sentinel. Kept
                // reachable so the model's older half is certified too.
                req "onClick" TClosure ]
          kind "Markdown" "content" [ req "text" (TUnion("Text", [])) ]
          kind
              "Measure"
              "data"
              [ req "label" (TUnion("Text", []))
                omit "level" (TEnum "Level") (VEnum "low")
                opt "origin" (TRecord "Point")
                req "raw" TOpaque
                req "series" series
                req "value" (TUnion("Slot", [ TFloat ])) ] ]
      Unions =
        [ { Name = "Slot"
            Params = [ "T" ]
            Cases =
              [ case "Static" [ req "value" (TVar "T") ]
                { Tag = "Ref"
                  Fields = [ req "name" TStr ]
                  // The retirement half of the annotation set (Phase 113) — nothing on
                  // the wire changes, every other surface renders it.
                  Annotations =
                    { Deprecated =
                        Some
                            { Replacement = Some "Static"
                              Message = Some "A by-name slot cannot be resolved without a host registry." }
                      InProcessOnly = false
                      Since = Some "0.1.0" } } ] }
          { Name = "Text"
            Params = []
            Cases =
              [ case "Literal" [ req "text" TStr ]
                case
                    "Lookup"
                    [ annotated
                          { Annotations.Empty with
                              InProcessOnly = true }
                          (opt "args" (TMap TStr))
                      req "key" TStr ] ] } ]
      Enums =
        [ Declare.enumOf "LayoutKind" [ "Stack"; "Row"; "Grid" ]
          // A wire-mapped enum (Phase 707): host identifiers an F# DU can spell, wire
          // strings it cannot.
          Declare.enumWith "Level" [ "Low", "low"; "Medium", "medium"; "High", "high" ]
          Declare.enumOf "Strictness" [ "StrictReplay"; "AdvisoryWarning" ] ]
      Records =
        [ { Name = "ContentHash"
            Fields = [ req "hash" TStr; req "strictness" (TEnum "Strictness") ] }
          { Name = "Point"
            Fields = [ req "x" TFloat; req "y" TFloat ] } ]
      // Declared defaults for the generated smart constructors. Scalars and enum cases
      // only, because that is what the F# emitter can write a default EXPRESSION for
      // (`CodegenError.UnsupportedDefault` refuses the rest) — so a composite default
      // here would make this vocabulary un-generatable and take the triple proof with
      // it. The artifact's composite-value coverage lives in `valueCoverageIdl` below,
      // which is never handed to a generator.
      Defaults =
        [ { Kind = "Group"
            Field = "layout"
            Value = VEnum "Stack" } ]
      // The node envelope. Every slot is `Optional` rather than `OmitDefault`: an
      // omit-at-default envelope field is RESTORED on decode, so every node would come
      // back enveloped and the authored bare `VNode` fixtures could not round-trip. The
      // omit-at-default leg is carried by `Group.layout` and `Measure.level` instead.
      NodeFields = [ opt "hidden" TBool; opt "label" TStr ]
      // The op vocabulary — the wire's second root, and the only place `TKind` and
      // `TOp` appear. Neither vendored sample declares one.
      Ops =
        [ kind "Batch" "op" [ req "ops" (TList TOp) ]
          kind "Insert" "op" [ req "index" TInt; req "newKind" TKind; req "parentId" TStr ] ]
      Wire = WireShape.Default }

/// A vocabulary whose DEFAULTS enumerate every `IdlValue` case — the artifact
/// round-trip law's value half.
///
/// Separate from `refIdl` for a stated reason: the F# emitter can write a default
/// expression only for a scalar, an enum case, a nullary union case or an empty list,
/// so a vocabulary carrying a composite default cannot be generated from. Rather than
/// choose between covering the value model and covering the generator, this carries
/// the values and `refIdl` carries the generator. Nothing hands this one to `Gen`.
///
/// Authored in canonical order — defaults sorted by (kind, field), named sub-values
/// sorted, JSON object keys sorted and whole-valued numbers written as integers — so
/// `Artifact.canonicalise valueCoverageIdl = valueCoverageIdl` holds exactly.
let valueCoverageIdl: Idl =
    { refIdl with
        Defaults =
            [ "absent", VAbsent
              "bool", VBool true
              "closure", VClosure
              "enum", VEnum "high"
              "float", VFloat 1.5
              "int", VInt 7
              "json", VJson(JObj [ "a", JInt 2; "b", JArr [ JStr "x" ] ])
              "list", VList [ VStr "a"; VStr "b" ]
              "map", VMap [ "k1", VStr "v1"; "k2", VStr "v2" ]
              "node", VNode("n1", "Markdown", [ "text", VStr "t" ])
              "nodeEnv", VNodeEnv("n2", [ "hidden", VBool false ], "Markdown", [ "text", VStr "t" ])
              "opaque", VOpaque
              "record", VRecord [ "hash", VStr "h"; "strictness", VEnum "StrictReplay" ]
              "str", VStr "s"
              "union", VUnion("Static", [ "value", VFloat 0.0 ]) ]
            |> List.map (fun (field, value) ->
                { Kind = "Coverage"
                  Field = field
                  Value = value }) }

/// The same vocabulary with one default authored OUT of canonical order — its named
/// sub-values reversed and a whole-valued float where the artifact carries an integer.
/// `canonicalise` must map it onto `valueCoverageIdl`, which is the "a reshuffle of the
/// authored file produces no diff" contract stated as a value equality.
let unsortedCoverageIdl: Idl =
    { valueCoverageIdl with
        Defaults =
            valueCoverageIdl.Defaults
            |> List.map (fun d ->
                match d.Field with
                | "record" ->
                    { d with
                        Value = VRecord [ "strictness", VEnum "StrictReplay"; "hash", VStr "h" ] }
                | "json" ->
                    { d with
                        Value = VJson(JObj [ "b", JArr [ JStr "x" ]; "a", JFloat 2.0 ]) }
                | _ -> d)
            |> List.rev }

// ---------------------------------------------------------------------------
// The declared-support record + the host-prelude declaration — members 2 and 3 of
// the regeneration triple, small but exercising every channel `Gen.GenSupport` has.
// ---------------------------------------------------------------------------

let private markdownProjection: Gen.KindProjection =
    { SpecDecl =
        """MarkdownSpec =
    { Text: Text }"""
      Encoder = """and private encMarkdownSpec (s: MarkdownSpec) : JVal = JObj [ "text", encText s.Text ]"""
      Decoder =
        """and private decMarkdownSpec (j: JVal) : Result<MarkdownSpec, string> =
        jprop "text" j |> Result.bind decText |> Result.map (fun t -> { Text = t })"""
      Mk = Some """let mkMarkdown (text: Text) : MarkdownSpec = { Text = text }""" }

let support: SupportDocument =
    { Support =
        { Docs =
            Map.ofList
                [ "type:Point", [ "/// A point in the reference vocabulary's own coordinate space." ]
                  "case:Slot.Static", [ "/// A value supplied inline rather than resolved by name." ] ]
          TypeSplice = Some """and ReferenceMarker = { Note: string }"""
          EncodeSplice =
            Some """and private encReferenceMarker (m: ReferenceMarker) : JVal = JObj [ "note", JStr m.Note ]"""
          DecodeSplice =
            Some
                """and private decReferenceMarker (j: JVal) : Result<ReferenceMarker, string> =
        jprop "note" j |> Result.bind jstr |> Result.map (fun n -> { Note = n })"""
          AccessorSplice = Some "let referenceMarkerName = \"reference\""
          CaseRefines = Map.ofList [ "Text.Lookup", "Ok(Text.Lookup(args, key))" ]
          KindProjections = Map.ofList [ "Markdown", markdownProjection ] }
      HostPrelude =
        Some
            { Module = "Fuaran.Core.Tests.ReferencePrelude"
              Path = "ReferencePrelude.fs" } }

// ---------------------------------------------------------------------------
// Fixtures — authored values beside the canonical wire bytes they must produce.
//
// The bytes are HAND-AUTHORED to the canonical rules (Ordinal-sorted keys, no
// whitespace, `$type` first), not captured from the encoder: a captured expectation
// only ever confirms the encoder back to itself, which is the trap `IdlUiTests`
// records having fallen into once over `Binding.Query`'s accessor.
// ---------------------------------------------------------------------------

let private literal (s: string) = VUnion("Literal", [ "text", VStr s ])

let markdown1 =
    VNode("markdown-1", "Markdown", [ "text", literal "Updated hourly." ])

let private markdown1Wire =
    """{"id":"markdown-1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"Updated hourly."}}}"""

/// Every remaining type case in one node: a hosted slot, an opaque sentinel, a record,
/// a generic union instantiated at `float`, and a wire-mapped enum away from its default.
let measure1 =
    VNode(
        "measure-1",
        "Measure",
        [ "label", literal "Revenue"
          "level", VEnum "high"
          "origin", VRecord [ "x", VFloat 1.5; "y", VFloat -2.0 ]
          "raw", VOpaque
          "series", VJson(JArr [ JInt 1; JInt 2; JInt 3 ])
          "value", VUnion("Static", [ "value", VFloat 1234.5 ]) ]
    )

let private measure1Wire =
    """{"id":"measure-1","kind":{"$type":"Measure","label":{"$type":"Literal","text":"Revenue"},"level":"high","origin":{"x":1.5,"y":-2},"raw":"<opaque>","series":[1,2,3],"value":{"$type":"Static","value":1234.5}}}"""

/// The ENVELOPED form, plus an on-the-wire closure sentinel and an omit-at-default
/// field sitting exactly on its default (so it emits nothing).
let group1 =
    VNodeEnv(
        "group-1",
        [ "hidden", VBool true; "label", VStr "Panel" ],
        "Group",
        [ "children", VList [ markdown1 ]
          "layout", VEnum "Stack"
          "onSelect", VClosure ]
    )

let private group1Wire =
    """{"hidden":true,"id":"group-1","kind":{"$type":"Group","children":[{"id":"markdown-1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"Updated hourly."}}}],"onSelect":"<closure>"},"label":"Panel"}"""

let link1 =
    VNode(
        "link-1",
        "Link",
        [ "href", VUnion("Static", [ "value", VStr "https://example.com" ])
          "label", literal "Docs"
          "onClick", VClosure ]
    )

let private link1Wire =
    """{"id":"link-1","kind":{"$type":"Link","href":{"$type":"Static","value":"https://example.com"},"label":{"$type":"Literal","text":"Docs"},"onClick":"<closure>"}}"""

/// An UNHASHED `Custom` — inert-by-default at the trust boundary regardless of the
/// allowlist. Also the `TMap` leg.
let custom1 =
    VNode(
        "custom-1",
        "Custom",
        [ "componentId", VStr "trend-card"
          "moduleId", VStr "analytics"
          "props", VMap [ "ratio", VJson(JFloat 0.5); "title", VJson(JStr "Trend") ] ]
    )

let private custom1Wire =
    """{"id":"custom-1","kind":{"$type":"Custom","componentId":"trend-card","moduleId":"analytics","props":{"ratio":0.5,"title":"Trend"}}}"""

/// A hashed `Custom` under `StrictReplay` — live only when allowlisted AND the hash
/// matches.
let customBounded1 =
    VNode(
        "custom-bounded-1",
        "Custom",
        [ "componentId", VStr "QualityRing"
          "contentHash", VRecord [ "hash", VStr "abc123def456"; "strictness", VEnum "StrictReplay" ]
          "moduleId", VStr "deal-flow" ]
    )

let private customBounded1Wire =
    """{"id":"custom-bounded-1","kind":{"$type":"Custom","componentId":"QualityRing","contentHash":{"hash":"abc123def456","strictness":"StrictReplay"},"moduleId":"deal-flow"}}"""

/// The same component under `AdvisoryWarning` — a hash mismatch stays live, advisory.
let customAdvisory1 =
    VNode(
        "custom-advisory-1",
        "Custom",
        [ "componentId", VStr "TrendCard"
          "contentHash", VRecord [ "hash", VStr "abc123def456"; "strictness", VEnum "AdvisoryWarning" ]
          "moduleId", VStr "deal-flow" ]
    )

/// Node fixtures: authored value beside its canonical wire bytes.
let nodeCases: (string * IdlValue * string) list =
    [ "markdown-1", markdown1, markdown1Wire
      "measure-1", measure1, measure1Wire
      "group-1", group1, group1Wire
      "link-1", link1, link1Wire
      "custom-1", custom1, custom1Wire
      "custom-bounded-1", customBounded1, customBounded1Wire ]

// ---- the op root ----------------------------------------------------------

let insert1 =
    VUnion(
        "Insert",
        [ "index", VInt 0
          "newKind", VUnion("Markdown", [ "text", literal "New" ])
          "parentId", VStr "group-1" ]
    )

let private insert1Wire =
    """{"$type":"Insert","index":0,"newKind":{"$type":"Markdown","text":{"$type":"Literal","text":"New"}},"parentId":"group-1"}"""

let batch1 = VUnion("Batch", [ "ops", VList [ insert1 ] ])

let private batch1Wire =
    """{"$type":"Batch","ops":[{"$type":"Insert","index":0,"newKind":{"$type":"Markdown","text":{"$type":"Literal","text":"New"}},"parentId":"group-1"}]}"""

/// Op fixtures — the wire's second root, which neither vendored sample declares.
let opCases: (string * IdlValue * string) list =
    [ "insert-1", insert1, insert1Wire; "batch-1", batch1, batch1Wire ]

/// The trust policy for this vocabulary: which fields carry a URL and which carry
/// markdown. Supplied by the caller, as `Trust.HardenPolicy` intends.
let hardenPolicy (allowlist: Trust.AllowEntry list) : Trust.HardenPolicy =
    { Allowlist = allowlist
      UrlFields = Set.ofList [ "Link", "href" ]
      MarkdownFields = Set.ofList [ "Markdown", "text" ] }
