namespace Fuaran.Core.Idl

open Fuaran.Core

// ---------------------------------------------------------------------------
// Phase 696 — the IDL as a canonical DATA artifact (`idl.json`).
//
// Every other leg in this file projects the IDL into some HOST's shape — F#
// source, a TypeScript codec, a JSON Schema. This one projects the IDL into
// *itself*: a faithful, language-neutral rendering of the `Idl` record, so the
// vocabulary can be read, diffed and anchored to without an F# toolchain.
//
// Why it is worth its own leg, given `Gen.jsonSchema` exists: a JSON Schema is a
// VALIDATION surface, and validation loses exactly the information a vocabulary
// consumer needs. Optionality collapses (Draft 2020-12 has `required`, but no way
// to say "omitted when equal to this value" — so every `OmitDefault` becomes
// indistinguishable from `Optional`, and the default VALUE is gone); unions
// flatten into `oneOf`; the host-surface declarations (`TFn` / `THosted`) have
// nowhere to live at all. The schema answers "is this payload legal?"; this
// artifact answers "what is the vocabulary?" — and only the second can be
// diffed into a stability classification.
// ---------------------------------------------------------------------------

/// The IDL rendered as canonical JSON — the `idl.json` spec artifact.
///
/// **Ordering contract**, which is what makes the artifact diffable rather than
/// merely readable:
///
/// - **Across** entries, the top-level collections are Ordinal-sorted by identity
///   (kinds by tag, unions / enums / records by name, defaults by kind-then-field).
///   The authored order of a vocabulary file is incidental grouping, so a reshuffle
///   there must produce no diff, and an addition must land as one clean insert.
/// - **Within** an entry, the authored order is preserved verbatim — field lists,
///   union cases, union type parameters, enum cases. That order IS significant:
///   `Gen` emits union-case fields as POSITIONAL bindings and type parameters
///   positionally, so a reorder is a real host-surface change a reviewer should see.
///
/// Object keys need no such rule — `Canonical.write Rendering.CrossHost` Ordinal-sorts them recursively.
module Artifact =

    /// The artifact ENCODING version — bumped when this module's output shape
    /// changes (a new key, a renamed discriminator), never when the vocabulary it
    /// describes changes. A consumer pins the encoding, not the contents.
    [<Literal>]
    let version = 1

    let private ordinal (a: string) (b: string) = System.String.CompareOrdinal(a, b)

    /// The rendering the artifact is the indented form of (Phase 520): its scalars and member keys
    /// are written by it, and its members ordered as it orders them.
    let private rendering: Rendering = Rendering.CrossHost

    /// Render a `JVal` with two-space indentation.
    ///
    /// Only WHITESPACE is added here: every scalar and every key string is written by
    /// [[rendering]] (`Rendering.CrossHost`) itself, and members are ordered by its member order,
    /// so canonical escaping (rule 6), the pinned float layout (rule 5) and Ordinal key order are
    /// all INHERITED rather than re-implemented — the drift risk `TJson`'s verbatim passthrough
    /// names, in the one place a second stringifier would otherwise appear.
    ///
    /// Indented rather than compact because this artifact exists to be read and
    /// DIFFED, and a single-line file diffs as "everything changed". `schema.json`
    /// beside it in the corpus takes the same posture.
    let rec private indent (depth: int) (v: JVal) : string =
        let pad n = String.replicate n "  "

        match v with
        | JObj [] -> "{}"
        | JObj fields ->
            let ordered =
                match rendering.Order with
                | MemberOrder.Sorted -> fields |> List.sortWith (fun (a, _) (b, _) -> ordinal a b)
                | MemberOrder.Authored -> fields

            let body =
                ordered
                |> List.map (fun (k, fv) ->
                    pad (depth + 1)
                    + Canonical.write rendering (JStr k)
                    + ": "
                    + indent (depth + 1) fv)
                |> String.concat ",\n"

            "{\n" + body + "\n" + pad depth + "}"
        | JArr [] -> "[]"
        | JArr xs ->
            let body =
                xs
                |> List.map (fun x -> pad (depth + 1) + indent (depth + 1) x)
                |> String.concat ",\n"

            "[\n" + body + "\n" + pad depth + "]"
        | scalar -> Canonical.write rendering scalar

    /// The structural type of a field. `$type` carries the [[IdlType]] case; the
    /// `wire` key, where present, states the FIXED wire form a third party will see
    /// for that case, so a sentinel is never mistaken for authored content — or, on a
    /// hosted slot, its DECLARED wire form as a type object (Phase 252), `"json"` when it
    /// declares none.
    ///
    /// `hostSurface` (on [[TFn]] / [[THosted]]) carries the host-language strings
    /// from [[ClosureSig]] / [[HostedCodec]]. They are included because they are
    /// genuinely part of the contract a host must satisfy — and flagged under their
    /// own key because they are **host-surface spec, not wire spec**: nothing in
    /// them is observable on the wire, and a non-F# consumer reading this artifact
    /// to build a codec must ignore them entirely.
    ///
    /// The object spells the DECLARED type, not its lowered wire form (Phase 513): a closure and a
    /// host-signature function stay two cases, a union stays named and applied rather than
    /// substituted, and a type variable stays a variable, because the artifact is read back into the
    /// declaration ([[readType]]). What it states about the wire — the sentinel spellings — it reads
    /// from [[WireForm]], the one lowering the codec and the sampler switch on.
    let rec private typeJson (t: IdlType) : JVal =
        match t with
        | TStr -> Canonical.typed "str" []
        | TInt -> Canonical.typed "int" []
        | TBool -> Canonical.typed "bool" []
        | TFloat -> Canonical.typed "float" []
        | TEnum name -> Canonical.typed "enum" [ "name", JStr name ]
        | TUnion(name, args) -> Canonical.typed "union" [ "name", JStr name; "args", JArr(args |> List.map typeJson) ]
        | TVar name -> Canonical.typed "var" [ "name", JStr name ]
        | TNode -> Canonical.typed "node" []
        | TKind -> Canonical.typed "kind" []
        | TOp -> Canonical.typed "op" []
        | TList inner -> Canonical.typed "list" [ "of", typeJson inner ]
        | TMap valueType -> Canonical.typed "map" [ "values", typeJson valueType ]
        | TRecord name -> Canonical.typed "record" [ "name", JStr name ]
        | TClosure -> Canonical.typed "closure" [ "wire", JStr WireForm.closure ]
        | TOpaque -> Canonical.typed "opaque" [ "wire", JStr WireForm.opaque ]
        | TJson -> Canonical.typed "json" []
        | TFn sg ->
            Canonical.typed
                "fn"
                [ "wire", JStr WireForm.closure
                  "hostSurface",
                  JObj
                      [ "fsharp", JStr sg.FSharp
                        "typescript", JStr sg.TypeScript
                        "placeholder", JStr sg.Placeholder ] ]
        // Phase 252 — wire states the slot's DECLARED wire form as a type object, and
        // `format` the string format on top of it; a slot that declares none says "json"
        // (carried verbatim), byte-for-byte what every earlier artifact wrote. Both sit
        // OUTSIDE hostSurface: they are wire spec a third-party codec reads.
        | THosted h ->
            Canonical.typed
                "hosted"
                ([ "wire",
                   (match h.Wire with
                    | Some w -> typeJson w
                    | None -> JStr "json")
                   "hostSurface", JObj [ "fsharp", JStr h.FSharp; "encode", JStr h.Encode; "decode", JStr h.Decode ] ]
                 @ (match h.Format with
                    | Some f -> [ "format", JStr(HostedFormat.name f) ]
                    | None -> []))

    /// An authored value — a field default, or a nested part of one.
    let rec private valueJson (v: IdlValue) : JVal =
        match v with
        | VStr s -> Canonical.typed "str" [ "value", JStr s ]
        | VInt i -> Canonical.typed "int" [ "value", JInt i ]
        | VBool b -> Canonical.typed "bool" [ "value", JBool b ]
        | VFloat f -> Canonical.typed "float" [ "value", JFloat f ]
        | VEnum case -> Canonical.typed "enum" [ "case", JStr case ]
        | VUnion(tag, fields) -> Canonical.typed "union" [ "tag", JStr tag; "fields", namedValues fields ]
        | VList xs -> Canonical.typed "list" [ "items", JArr(xs |> List.map valueJson) ]
        | VNode(id, kindTag, fields) ->
            Canonical.typed "node" [ "id", JStr id; "kind", JStr kindTag; "fields", namedValues fields ]
        // Phase 698 — the enveloped form records its envelope under its own key
        // rather than merged into `fields`: the two namespaces can collide (an
        // envelope `style` and `Drawing.style` both exist), so merging them would
        // make the artefact ambiguous and the Phase 700 diff classifier wrong.
        | VNodeEnv(id, envelope, kindTag, fields) ->
            Canonical.typed
                "node"
                [ "envelope", namedValues envelope
                  "fields", namedValues fields
                  "id", JStr id
                  "kind", JStr kindTag ]
        | VAbsent -> Canonical.typed "absent" []
        | VClosure -> Canonical.typed "closure" []
        | VOpaque -> Canonical.typed "opaque" []
        // Verbatim — a `TJson` value is real data, and `Canonical.write Rendering.CrossHost` already lays it
        // out canonically, so passing the `JVal` through inherits every rule.
        | VJson j -> Canonical.typed "json" [ "value", j ]
        | VRecord fields -> Canonical.typed "record" [ "fields", namedValues fields ]
        | VMap entries -> Canonical.typed "map" [ "entries", namedValues entries ]

    /// Named sub-values of a composite default (union / record / node / map fields).
    /// Emitted in the order they arrive: these are wire KEYS, which carry no authored
    /// order to preserve, and [[canonicalise]] — which [[json]] runs first — has already
    /// Ordinal-sorted them. The sort lives THERE and only there, so the model ordering
    /// and the artifact ordering cannot drift apart into two definitions.
    and private namedValues (fields: (string * IdlValue) list) : JVal =
        fields
        |> List.map (fun (name, v) -> JObj [ "name", JStr name; "value", valueJson v ])
        |> JArr

    /// Whether a field is on the wire, and under what condition. `omitDefault`
    /// carries the identity default VALUE — the single thing a JSON Schema
    /// projection of the same vocabulary cannot express.
    let private optionalityJson (o: Optionality) : JVal =
        match o with
        | Required -> Canonical.typed "required" []
        | Optional -> Canonical.typed "optional" []
        | HostOnly -> Canonical.typed "hostOnly" []
        | OmitDefault d -> Canonical.typed "omitDefault" [ "default", valueJson d ]

    /// The declared annotation set (Phase 113), or `[]` when it says nothing.
    ///
    /// Returned as the key-value PAIRS to splice rather than as a `JVal`, so the
    /// empty set contributes no key at all — an unannotated vocabulary's artifact is
    /// byte-for-byte what it was, the posture `ops` / `hostCases` / `wire` all take.
    /// Each slot is likewise omitted when absent, so `since` alone renders as
    /// `{"since": "…"}` and nothing else.
    ///
    /// **Not a hostSurface key.** A `hostSurface` block is a host-LANGUAGE
    /// declaration a non-F# consumer must ignore (§13); an annotation is a statement
    /// about the vocabulary itself that every consumer wants — a third-party codec
    /// reading this artifact needs to know a case is being retired quite as much as
    /// the reference host does.
    /// The annotation set's own object — the value under an `annotations` key, and
    /// (Phase 119) the value under each entry of an enum's `caseAnnotations` map. Split
    /// out from [[annotationsJson]] so the two placements render one shape rather than
    /// two that have to be kept equal.
    let private annotationBlock (a: Annotations) : JVal =
        let deprecated =
            match a.Deprecated with
            | None -> []
            | Some d ->
                [ "deprecated",
                  JObj(
                      (match d.Replacement with
                       | Some r -> [ "replacement", JStr r ]
                       | None -> [])
                      @ (match d.Message with
                         | Some m -> [ "message", JStr m ]
                         | None -> [])
                  ) ]

        JObj(
            deprecated
            @ (if a.InProcessOnly then
                   [ "inProcessOnly", JBool true ]
               else
                   [])
            @ (match a.Since with
               | Some v -> [ "since", JStr v ]
               | None -> [])
            // Phase 255 — the authored doc, verbatim. Omitted when absent like every
            // other slot, so only an artifact that declares one gains the key (and a
            // new content hash).
            @ (match a.Doc with
               | Some v -> [ "doc", JStr v ]
               | None -> [])
        )

    let private annotationsJson (a: Annotations) : (string * JVal) list =
        if a.IsEmpty then
            []
        else
            [ "annotations", annotationBlock a ]

    /// Field lists keep their AUTHORED order (see the module's ordering contract). Internal since
    /// Phase 403: the support document renders a projected record's declared fields with it, so a
    /// field reads the same in both documents of one triple.
    let internal fieldsJson (fs: IdlField list) : JVal =
        fs
        |> List.map (fun f ->
            JObj(
                [ "name", JStr f.Name
                  "type", typeJson f.Type
                  "optionality", optionalityJson f.Opt ]
                @ annotationsJson f.Annotations
            ))
        |> JArr

    let private kindJson (k: IdlKind) : JVal =
        JObj(
            [ "tag", JStr k.Tag
              "category", JStr k.Category
              "fields", fieldsJson k.Fields ]
            // Phase 119 — the kind's OWN annotations, spliced by the same helper and on
            // the same terms as a field's: omitted entirely when the kind says nothing,
            // so every pre-119 `idl.json` is byte-identical.
            @ annotationsJson k.Annotations
        )

    let private unionJson (policy: HardenPolicy) (u: IdlUnion) : JVal =
        let cases =
            u.Cases
            |> List.map (fun c ->
                JObj(
                    [ "tag", JStr c.Tag; "fields", fieldsJson c.Fields ]
                    @ annotationsJson c.Annotations
                ))
            |> JArr

        let baseFields =
            [ "name", JStr u.Name
              // Positional — never sorted.
              "params", JArr(u.Params |> List.map JStr)
              "cases", cases ]

        // A transparent case encodes as a BARE value rather than a `$type`-tagged
        // object, so a consumer that missed it would decode the union wrongly. DERIVED
        // from the vocabulary's declared [[HardenPolicy.TransparentUnions]] (Phase 116;
        // it was derived from a hard-coded union name before that), and surfaced per
        // union because that is where a third-party decoder needs it.
        let transparent =
            match TransparentUnion.tag policy u with
            | Some tag -> [ "transparentCase", JStr tag ]
            | None -> []

        // Phase 542 — the union's OWN annotations, last and on a kind's terms: omitted
        // entirely when the union says nothing, so every pre-542 `idl.json` is byte-identical.
        JObj(baseFields @ transparent @ annotationsJson u.Annotations)

    // -----------------------------------------------------------------------
    // Phase 114 - the ordering contract as a function over the MODEL.
    //
    // [[json]] used to apply the module's ordering contract inline, which made the
    // rule true of the BYTES and unstated about the value. That was tolerable while
    // the projection was one-way; with [[parse]] beside it the two have to agree
    // exactly, because `parse (render idl)` can only ever return the canonically
    // ordered form and a round-trip law has to say so. Stating the order once, as a
    // function, is what lets the law read `parse (render idl) = canonicalise idl`
    // rather than a hedge.
    // -----------------------------------------------------------------------

    /// Ordinal-sort a `JVal`'s object keys recursively, and normalise a whole-valued
    /// float to the integer the parser will produce for it.
    ///
    /// The second half looks like a fudge and is not: JSON has ONE number type, and
    /// the canonical renderer lays an integral double out with no `.` and no exponent
    /// whenever it fits [[JInt]]'s Int32 range - so `JFloat 1.0` and `JInt 1` are the
    /// same byte for byte and no parser could tell them apart. Only a [[TJson]] payload
    /// is affected (a [[VFloat]] is tagged `float` in the artifact and keeps its case);
    /// leaving it out would make the round-trip law false for a reason that has nothing
    /// to do with the vocabulary.
    let rec private canonJson (v: JVal) : JVal =
        match v with
        | JObj fields ->
            fields
            |> List.map (fun (k, fv) -> k, canonJson fv)
            |> List.sortWith (fun (a, _) (b, _) -> ordinal a b)
            |> JObj
        | JArr xs -> JArr(xs |> List.map canonJson)
        | JFloat f when f = floor f && f >= -2147483648.0 && f <= 2147483647.0 -> JInt(int f)
        | scalar -> scalar

    /// Canonicalise an authored value: named sub-value lists Ordinal-sorted by name
    /// (they are wire keys), list ITEMS left alone (their order IS the value).
    let rec private canonValue (v: IdlValue) : IdlValue =
        match v with
        | VUnion(tag, fields) -> VUnion(tag, canonNamed fields)
        | VList xs -> VList(xs |> List.map canonValue)
        | VNode(id, kindTag, fields) -> VNode(id, kindTag, canonNamed fields)
        | VNodeEnv(id, envelope, kindTag, fields) -> VNodeEnv(id, canonNamed envelope, kindTag, canonNamed fields)
        | VRecord fields -> VRecord(canonNamed fields)
        | VMap entries -> VMap(canonNamed entries)
        | VJson j -> VJson(canonJson j)
        | scalar -> scalar

    and private canonNamed (fields: (string * IdlValue) list) : (string * IdlValue) list =
        fields
        |> List.map (fun (n, v) -> n, canonValue v)
        |> List.sortWith (fun (a, _) (b, _) -> ordinal a b)

    let private canonOpt (o: Optionality) : Optionality =
        match o with
        | OmitDefault d -> OmitDefault(canonValue d)
        | other -> other

    let private canonFields (fs: IdlField list) : IdlField list =
        fs |> List.map (fun f -> { f with Opt = canonOpt f.Opt })

    let private canonKind (k: IdlKind) : IdlKind =
        { k with Fields = canonFields k.Fields }

    /// The vocabulary in the exact shape [[render]] projects it, and therefore the exact
    /// shape [[parse]] returns: top-level collections Ordinal-sorted by identity, authored
    /// order preserved WITHIN an entry (field lists, union cases, union type parameters,
    /// enum cases, the node envelope), and every authored value's named sub-values sorted.
    ///
    /// Idempotent, and equal on any two vocabularies the artifact cannot tell apart -
    /// which is what "a reshuffle of the authored file produces no diff" means as a
    /// statement about VALUES rather than about bytes.
    let canonicalise (idl: Idl) : Idl =
        let sortedBy (key: 'a -> string) (xs: 'a list) =
            xs |> List.sortWith (fun a b -> ordinal (key a) (key b))

        { Kinds = idl.Kinds |> List.map canonKind |> sortedBy _.Tag
          Unions =
            idl.Unions
            |> List.map (fun u ->
                { u with
                    Cases = u.Cases |> List.map (fun c -> { c with Fields = canonFields c.Fields }) })
            |> sortedBy _.Name
          Enums =
            idl.Enums
            // Phase 119 — `CaseAnnotations` is an ADDRESSED collection (like
            // `Harden.TransparentUnions`), so the authored order carries nothing and the
            // projection cannot reproduce it: it is sorted by the key the artifact writes
            // — the WIRE string — and an entry that says nothing is dropped, because the
            // projection omits it and `parse` could never bring it back.
            |> List.map (fun e ->
                { e with
                    CaseAnnotations =
                        e.CaseAnnotations
                        |> List.filter (fun (_, a) -> not a.IsEmpty)
                        |> List.sortWith (fun (a, _) (b, _) -> ordinal (e.WireOf a) (e.WireOf b)) })
            |> sortedBy _.Name
          Records =
            idl.Records
            |> List.map (fun r -> { r with Fields = canonFields r.Fields })
            |> sortedBy _.Name
          Defaults =
            idl.Defaults
            |> List.map (fun d -> { d with Value = canonValue d.Value })
            |> List.sortWith (fun a b ->
                match ordinal a.Kind b.Kind with
                | 0 -> ordinal a.Field b.Field
                | c -> c)
          NodeFields = canonFields idl.NodeFields
          Ops = idl.Ops |> List.map canonKind |> sortedBy _.Tag
          Wire = idl.Wire
          Harden =
            { idl.Harden with
                // Addressed by union name, so the authored order carries nothing —
                // sorted here for the same reason every other named collection is.
                TransparentUnions = idl.Harden.TransparentUnions |> List.sortWith (fun (a, _) (b, _) -> ordinal a b) } }

    /// Who a vocabulary IS (Phase 252) — the artifact's `name` and `description`.
    ///
    /// **Beside the [[Idl]], not on it.** Identity is a property of the published
    /// document rather than of the structure the generators read: no leg emits a
    /// different type, codec or schema for it. And carrying it on the record would
    /// widen a record every vocabulary builds by literal. So [[renderWith]] takes it,
    /// and [[identityOf]] reads it back, which is what lets an authored `idl.json`
    /// re-render to itself.
    type Identity =
        {
            /// The vocabulary's name, written as the artifact's `name` member when
            /// present. `None` writes no member, which is every artifact [[render]]
            /// has ever written.
            Name: string option
            /// The artifact's `description` member, verbatim.
            Description: string
        }

    /// The `description` [[render]] writes — the UI vocabulary's, as it always has.
    /// Kept as [[render]]'s default because the artifact's bytes are pinned: a
    /// vocabulary that wants its own description says so through [[renderWith]].
    let defaultDescription =
        "Canonical data rendering of the Fuaran UI wire vocabulary — kinds, unions, enums, "
        + "records, field defaults and the node envelope. This is the STRUCTURAL source: it "
        + "states what the vocabulary is, including optionality classes and omit-at-default "
        + "values that a JSON Schema cannot express. schema.json beside it is the VALIDATION "
        + "surface, derived from the same contract. Keys marked hostSurface are host-language "
        + "declarations, not wire spec, and carry nothing observable on the wire. See "
        + "WIRE_FORMAT.md section 13."

    /// The identity [[render]] writes: no name, [[defaultDescription]].
    let defaultIdentity: Identity =
        { Name = None
          Description = defaultDescription }

    /// The whole IDL as a `JVal`, under a declared [[Identity]] (Phase 252).
    ///
    /// [[canonicalise]] runs FIRST and owns every ordering decision; nothing below
    /// sorts. That is what keeps the ordering contract one definition now that
    /// [[parse]] has to reproduce it exactly.
    let jsonWith (identity: Identity) (idl: Idl) : JVal =
        let idl = canonicalise idl

        JObj(
            [ "version", JInt version
              "description", JStr identity.Description
              "kinds", JArr(idl.Kinds |> List.map kindJson)
              "unions", JArr(idl.Unions |> List.map (unionJson idl.Harden))
              "enums",
              JArr(
                  idl.Enums
                  |> List.map (fun e ->
                      // `cases` is the wire contract (what a decoder must accept).
                      // `hostCases` appears ONLY for a Phase 707 wire-mapped enum and
                      // is a hostSurface key in the §13 sense — a host-language
                      // declaration carrying nothing observable on the wire. Omitting
                      // it for the identity mapping is what keeps every pre-707
                      // artefact byte-identical.
                      JObj(
                          [ "name", JStr e.Name; "cases", JArr(e.WireCases |> List.map JStr) ]
                          @ (if List.isEmpty e.Wires then
                                 []
                             else
                                 [ "hostCases", JArr(e.Cases |> List.map JStr) ])
                          // Phase 119 — per-case annotations, keyed by the WIRE string
                          // rather than the host case name. `cases` is the one key every
                          // revision carries, so a third-party reader resolves an entry
                          // without consulting the conditional `hostCases`; and an
                          // annotation is a statement about the vocabulary that every
                          // consumer wants, not a hostSurface declaration a non-F#
                          // reader must ignore (the argument at [[annotationsJson]]).
                          // Emitted only when some case says something, so every pre-119
                          // artefact is byte-identical.
                          @ (if e.HasCaseAnnotations then
                                 [ "caseAnnotations",
                                   JObj(
                                       e.CaseAnnotations
                                       |> List.filter (fun (_, a) -> not a.IsEmpty)
                                       |> List.map (fun (c, a) -> e.WireOf c, annotationBlock a)
                                   ) ]
                             else
                                 [])
                          // Phase 542 — the enum's OWN annotations, omitted when empty.
                          @ annotationsJson e.Annotations
                      ))
              )
              "records",
              JArr(
                  idl.Records
                  |> List.map (fun r ->
                      // Phase 542 — the record's OWN annotations, omitted when empty.
                      JObj(
                          [ "name", JStr r.Name; "fields", fieldsJson r.Fields ]
                          @ annotationsJson r.Annotations
                      ))
              )
              "defaults",
              JArr(
                  idl.Defaults
                  |> List.map (fun d -> JObj [ "kind", JStr d.Kind; "field", JStr d.Field; "value", valueJson d.Value ])
              )
              "nodeFields", fieldsJson idl.NodeFields ]
            // The op vocabulary (Phase 703) — the wire's second root. Emitted only
            // when the domain declares ops, so an op-free vocabulary's artefact is
            // byte-for-byte what it was, the same posture `hostCases` takes.
            @ (if List.isEmpty idl.Ops then
                   []
               else
                   [ "ops", JArr(idl.Ops |> List.map kindJson) ])
            // The vocabulary's name (Phase 252). Written only when one is declared, so
            // every artifact rendered without one is byte-for-byte what it was — the
            // `ops` posture again.
            @ (match identity.Name with
               | Some name -> [ "name", JStr name ]
               | None -> [])
            // The declared wire shape (Phases 108/109). Emitted only when it
            // differs from the default, so every `$type`-nested vocabulary's
            // artefact is byte-for-byte what it was — the `ops` posture again.
            @ (if idl.Wire = WireShape.Default then
                   []
               else
                   [ "wire",
                     JObj
                         [ "discriminator", JStr idl.Wire.Discriminator
                           "nodeEnvelope",
                           JStr(
                               match idl.Wire.NodeEnvelope with
                               | NodeEnvelopeShape.NestedKind -> "nestedKind"
                               | NodeEnvelopeShape.FlatKind -> "flatKind"
                           )
                           "keyOrder",
                           JStr(
                               match idl.Wire.KeyOrder with
                               | KeyOrder.Sorted -> "sorted"
                               | KeyOrder.Declared -> "declared"
                           ) ] ])
            // The declared hardening vocabulary (Phase 116), emitted ALWAYS since
            // Phase 179 — the policy the engine used to call `Default` included,
            // which used to omit it.
            //
            // This was step one of the two-step wire migration D40 laid out, and the
            // reason it needed two steps is that the default was a WIRE fact rather
            // than only a source one: the block's ABSENCE meant one domain's tokens,
            // by a promise [[readHarden]] made to every artifact written before they
            // were declarable, so emptying the default in place would have changed
            // what already-published bytes MEAN, silently and with a green build.
            // Emitting unconditionally makes a freshly rendered artifact declare its
            // policy outright, so no reader has to infer it — additive on the wire (an
            // extra member a reader tolerates by `WIRE_FORMAT.md` §2.1 rule 2,
            // field-lookup-by-name) and additive on the API.
            //
            // Step two is Phase 180 and it is TAKEN: `HardenPolicy.Default` is gone
            // and [[readHarden]] resolves an absent block as `Undeclared`. This writer
            // is unchanged by it — it already emitted the block for every policy,
            // which is exactly what made the reader flip safe.
            //
            // `transparentUnions` is the one member here a WIRE consumer must read:
            // the per-union `transparentCase` above is derived from it, and a decoder
            // that missed it would read a bare value as a tagged object. The rest is
            // codegen-boundary spec — what the trust boundary gates and what it mints
            // in its place — and a decoder ignores it.
            @ [ "harden",
                JObj
                    [ "gatedKind", JStr idl.Harden.GatedKind
                      "placeholderKind", JStr idl.Harden.PlaceholderKind
                      "placeholderField", JStr idl.Harden.PlaceholderField
                      "textLiteralCase", JStr idl.Harden.TextLiteralCase
                      "textLiteralField", JStr idl.Harden.TextLiteralField
                      "valueLiteralCase", JStr idl.Harden.ValueLiteralCase
                      "valueLiteralField", JStr idl.Harden.ValueLiteralField
                      "transparentUnions",
                      JArr(
                          idl.Harden.TransparentUnions
                          |> List.map (fun (union, case) -> JObj [ "union", JStr union; "case", JStr case ])
                      ) ] ]
        )

    /// The whole IDL as a `JVal`, under [[defaultIdentity]].
    let json (idl: Idl) : JVal = jsonWith defaultIdentity idl

    /// The `idl.json` bytes — indented, canonically ordered, newline-terminated
    /// (matching `schema.json`'s convention in the same corpus).
    let render (idl: Idl) : string = indent 0 (json idl) + "\n"

    /// [[render]] under a declared [[Identity]] (Phase 252) — the vocabulary's own
    /// name and description rather than [[defaultDescription]]. With the identity
    /// [[identityOf]] reads off an artifact, an authored `idl.json` re-renders to
    /// itself.
    let renderWith (identity: Identity) (idl: Idl) : string = indent 0 (jsonWith identity idl) + "\n"

    /// The same indented, canonically-ordered layout [[render]] uses, over an arbitrary
    /// `JVal`. Exposed so a SIBLING document of the vocabulary — the declared-support
    /// record beside it — lays out identically without a second stringifier appearing in
    /// any consumer, which is the drift the module header names for `TJson`'s passthrough.
    let renderJson (v: JVal) : string = indent 0 v + "\n"

    // -----------------------------------------------------------------------
    // Phase 114 — the artifact READ back.
    //
    // The projection above made the vocabulary readable without an F# toolchain;
    // this makes it LOADABLE. That is the difference between a domain being able to
    // inspect its contract and a domain being able to own it: until now the only way
    // to obtain an `Idl` value was to declare it in F# and compile it, so a domain
    // whose vocabulary lived in its own repo still could not regenerate its structural
    // layer against the packaged engine — the vocabulary had to be a compile input,
    // and the one that existed was in this repo's tests. With `parse` the vocabulary
    // is DATA the domain holds, exactly as D14 says it should be.
    //
    // **This is a total inverse, and `Proposal.parse` is deliberately not.** That
    // reader refuses `closure` / `fn` / `opaque` / `hosted` / `var` / `hostOnly` by
    // name because a data-only change proposal has no business minting a host-surface
    // declaration. The refusals are policy about who may author what; they are not a
    // statement that the encoding cannot be read. So the two readers stay separate
    // rather than one delegating to the other — merging them would either lose the
    // refusals or make this one partial.
    //
    // The law that makes it worth having: `parse (render idl) = canonicalise idl`,
    // pinned over every vocabulary the suite declares. Anything the projection drops
    // fails it.
    // -----------------------------------------------------------------------

    // Phase 310 — every member is read through the typed decode layer, so a refusal carries a
    // code and the path to the value at fault ([[ofJsonDetailed]], [[parseDetailed]]). Since
    // Phase 520 the readers are `Decoder`'s combinators and nothing else: the private layer that
    // re-spelled each refusal in this reader's own sentence is gone, so a refusal's sentence is the
    // projection of its code (`DecodeError.sentence`), and its code and path are unchanged.

    let private under (step: PathSegment) (r: Result<'T, DecodeError>) : Result<'T, DecodeError> =
        r |> Result.mapError (DecodeError.under step)

    /// The required string member `name`.
    let private str (name: string) (v: JVal) : Result<string, DecodeError> = Decoder.field name Decoder.str v

    /// An array member that is OMITTED when empty (`params`, `args`, `fields`) reads as
    /// empty rather than as a refusal — the projection's omit-when-empty rule, read back.
    let private listOrEmpty (name: string) (f: Decoder<'a>) (v: JVal) : Result<'a list, DecodeError> =
        Decoder.fieldOr name [] (Decoder.list f) v

    /// The required array member `name`, each item read with `f`.
    let private listOf (name: string) (f: Decoder<'a>) (v: JVal) : Result<'a list, DecodeError> =
        Decoder.field name (Decoder.list f) v

    /// The host-surface block of a `fn` / `hosted` type — the three verbatim host
    /// strings each carries. Named so the two arms report the same way.
    let private hostSurface (keys: string list) (v: JVal) : Result<string list, DecodeError> =
        Decoder.field "hostSurface" (Decoder.sequence (keys |> List.map str)) v

    let rec private readType: Decoder<IdlType> =
        fun v ->
            let named (f: string -> IdlType) : Decoder<IdlType> = str "name" >> Result.map f

            Decoder.tagDispatch
                "$type"
                [ "str", Decoder.succeed TStr
                  "int", Decoder.succeed TInt
                  "bool", Decoder.succeed TBool
                  "float", Decoder.succeed TFloat
                  "node", Decoder.succeed TNode
                  "kind", Decoder.succeed TKind
                  "op", Decoder.succeed TOp
                  "json", Decoder.succeed TJson
                  // `wire` is a RESTATEMENT of a fixed sentinel the engine already knows, not
                  // a carried value — reading it back would let a hand-edited artifact
                  // redefine what `<closure>` means.
                  "closure", Decoder.succeed TClosure
                  "opaque", Decoder.succeed TOpaque
                  "enum", named TEnum
                  "record", named TRecord
                  "var", named TVar
                  "list", Decoder.field "of" readType |> Decoder.map TList
                  "map", Decoder.field "values" readType |> Decoder.map TMap
                  "union",
                  fun u ->
                      str "name" u
                      |> Result.bind (fun n ->
                          listOrEmpty "args" readType u |> Result.map (fun args -> TUnion(n, args)))
                  "fn",
                  fun f ->
                      hostSurface [ "fsharp"; "typescript"; "placeholder" ] f
                      |> Result.map (fun hs ->
                          // `hostSurface` reads exactly the three keys it is handed.
                          let at (i: int) = List.item i hs

                          TFn
                              { FSharp = at 0
                                TypeScript = at 1
                                Placeholder = at 2 })
                  "hosted", readHosted ]
                v

    and private readHosted: Decoder<IdlType> =
        fun v ->
            hostSurface [ "fsharp"; "encode"; "decode" ] v
            |> Result.bind (fun hs ->
                let wire =
                    match Decoder.tryMember "wire" v with
                    | None
                    | Some(JStr "json") -> Ok None
                    | Some(JObj _ as w) -> readType w |> Result.map Some |> under (PathSegment.Key "wire")
                    | Some other ->
                        Error(DecodeError.make DecodeCode.WrongKind "\"json\" or a type" (JVal.kindName other))
                        |> under (PathSegment.Key "wire")

                let format =
                    match Decoder.tryMember "format" v with
                    | None -> Ok None
                    | Some(JStr f) ->
                        // Phase 391: a format is the closed `HostedFormat`; an unknown name
                        // is refused here, where `Declare.hostedWireErrors` reported it before.
                        match HostedFormat.tryParse f with
                        | Some fmt -> Ok(Some fmt)
                        | None ->
                            Error(
                                DecodeError.make
                                    DecodeCode.UnknownTag
                                    ("one of " + String.concat ", " HostedFormat.known)
                                    ("format '" + f + "'")
                            )
                            |> under (PathSegment.Key "format")
                    | Some other -> Error(Decoder.wrongKind "string" other) |> under (PathSegment.Key "format")

                match wire, format with
                | Error e, _
                | _, Error e -> Error e
                | Ok w, Ok f ->
                    let at (i: int) = List.item i hs

                    Ok(
                        THosted
                            { FSharp = at 0
                              Encode = at 1
                              Decode = at 2
                              Wire = w
                              Format = f }
                    ))

    let rec private readValue: Decoder<IdlValue> =
        fun v ->
            let value (d: Decoder<'T>) (f: 'T -> IdlValue) : Decoder<IdlValue> =
                Decoder.field "value" d |> Decoder.map f

            // A whole-valued float renders with no `.` and no exponent, so the parser
            // hands it back as `JInt`. `Decoder.float` reads both, or this would refuse
            // every `VFloat 1.0` the projection itself wrote.
            //
            // Phase 303 — and a NON-FINITE float renders as WIRE_FORMAT §7's quoted token
            // (`Rendering.CrossHost` spells a non-finite `JFloat` as the string `"NaN"`), so the
            // reader takes the three tokens back at this one value tag, or `render` would write an
            // `idl.json` carrying a `VFloat nan` default that its own `parse` refuses.
            let sentinelOrNumber: Decoder<float> =
                function
                | JStr(FloatToken.NonFinite f) -> Ok f
                | j -> Decoder.float j

            Decoder.tagDispatch
                "$type"
                [ "absent", Decoder.succeed VAbsent
                  "closure", Decoder.succeed VClosure
                  "opaque", Decoder.succeed VOpaque
                  "str", value Decoder.str VStr
                  "int", value Decoder.int VInt
                  "bool", value Decoder.bool VBool
                  "float", value sentinelOrNumber VFloat
                  "enum", str "case" >> Result.map VEnum
                  "json", value Decoder.json VJson
                  "list", listOf "items" readValue >> Result.map VList
                  "union",
                  fun u ->
                      str "tag" u
                      |> Result.bind (fun tag -> readNamed "fields" u |> Result.map (fun fields -> VUnion(tag, fields)))
                  "record", readNamed "fields" >> Result.map VRecord
                  "map", readNamed "entries" >> Result.map VMap
                  // The enveloped form is told from the bare one by the PRESENCE of the
                  // `envelope` key, which is the same thing the projection branches on. An
                  // empty envelope is still an envelope: `VNodeEnv(id, [], …)` renders
                  // `"envelope": []` and must read back as itself.
                  "node",
                  fun n ->
                      str "id" n
                      |> Result.bind (fun id ->
                          str "kind" n
                          |> Result.bind (fun kindTag ->
                              readNamed "fields" n
                              |> Result.bind (fun fields ->
                                  match Decoder.tryMember "envelope" n with
                                  | None -> Ok(VNode(id, kindTag, fields))
                                  | Some _ ->
                                      readNamed "envelope" n
                                      |> Result.map (fun env -> VNodeEnv(id, env, kindTag, fields))))) ]
                v

    and private readNamed (key: string) (owner: JVal) : Result<(string * IdlValue) list, DecodeError> =
        listOf
            key
            (fun entry ->
                str "name" entry
                |> Result.bind (fun name -> Decoder.field "value" readValue entry |> Result.map (fun v -> name, v)))
            owner

    let private readOptionality: Decoder<Optionality> =
        Decoder.tagDispatch
            "$type"
            [ "required", Decoder.succeed Required
              "optional", Decoder.succeed Optional
              "hostOnly", Decoder.succeed HostOnly
              "omitDefault", Decoder.field "default" readValue |> Decoder.map OmitDefault ]

    /// One annotation-set OBJECT — the value under an `annotations` key, and (Phase 119)
    /// the value under each entry of an enum's `caseAnnotations` map. The inverse of
    /// [[annotationBlock]], and split out for the same reason it was.
    let private readAnnotationBlock (block: JVal) : Result<Annotations, DecodeError> =
        let optStr (name: string) (v: JVal) = Decoder.optField name Decoder.str v

        let deprecated =
            match Decoder.tryMember "deprecated" block with
            | None -> Ok None
            | Some d ->
                optStr "replacement" d
                |> Result.bind (fun r ->
                    optStr "message" d
                    |> Result.map (fun m -> Some { Replacement = r; Message = m }))
                |> under (PathSegment.Key "deprecated")

        let inProcessOnly = Decoder.fieldOr "inProcessOnly" false Decoder.bool block

        deprecated
        |> Result.bind (fun d ->
            inProcessOnly
            |> Result.bind (fun ipo ->
                optStr "since" block
                |> Result.bind (fun since ->
                    optStr "doc" block
                    |> Result.map (fun doc ->
                        { Deprecated = d
                          InProcessOnly = ipo
                          Since = since
                          Doc = doc }))))

    /// The annotation set under an owner's `annotations` key, or [[Annotations.Empty]]
    /// when the key is absent — the projection omits an empty set entirely, so absence
    /// is the default and not a gap.
    let private readAnnotations (owner: JVal) : Result<Annotations, DecodeError> =
        Decoder.fieldOr "annotations" Annotations.Empty readAnnotationBlock owner

    /// One field object — internal since Phase 403, the support document's reader of a projected
    /// record's declared fields. Its `type` and `optionality` are both looked for BEFORE either is
    /// read, so a field missing one reports the missing one whatever the other holds.
    let internal readField (v: JVal) : Result<IdlField, DecodeError> =
        str "name" v
        |> Result.bind (fun name ->
            Decoder.sequence [ Decoder.field "type" Decoder.json; Decoder.field "optionality" Decoder.json ] v
            |> Result.bind (fun _ -> Decoder.field "type" readType v)
            |> Result.bind (fun ty ->
                Decoder.field "optionality" readOptionality v
                |> Result.bind (fun opt ->
                    readAnnotations v
                    |> Result.map (fun ann ->
                        { Name = name
                          Type = ty
                          Opt = opt
                          Annotations = ann }))))

    let private readFields (owner: JVal) : Result<IdlField list, DecodeError> = listOf "fields" readField owner

    let private readKind (v: JVal) : Result<IdlKind, DecodeError> =
        str "tag" v
        |> Result.bind (fun tag ->
            str "category" v
            |> Result.bind (fun category ->
                readFields v
                |> Result.bind (fun fields ->
                    readAnnotations v
                    |> Result.map (fun ann ->
                        { Tag = tag
                          Category = category
                          Fields = fields
                          Annotations = ann }))))

    let private readUnion (v: JVal) : Result<IdlUnion, DecodeError> =
        str "name" v
        |> Result.bind (fun name ->
            listOrEmpty "params" Decoder.str v
            |> Result.bind (fun ps ->
                listOf
                    "cases"
                    (fun c ->
                        str "tag" c
                        |> Result.bind (fun tag ->
                            readFields c
                            |> Result.bind (fun fields ->
                                readAnnotations c
                                |> Result.map (fun ann ->
                                    { Tag = tag
                                      Fields = fields
                                      Annotations = ann }))))
                    v
                |> Result.map (fun cases ->
                    // `transparentCase` is DERIVED from the vocabulary's declared
                    // [[HardenPolicy.TransparentUnions]], which `readHarden` reads
                    // back, so the round-trip reproduces it without this reader
                    // touching it. Reading it back HERE would let an artifact claim a
                    // per-union transparency the declared policy does not state, and
                    // the two could then disagree — so it stays ignored.
                    { Name = name
                      Params = ps
                      Cases = cases
                      Annotations = Annotations.Empty })))
        |> Result.bind (fun u -> readAnnotations v |> Result.map (fun ann -> { u with Annotations = ann }))

    /// `cases` is always the WIRE contract; `hostCases` appears only for a wire-mapped
    /// enum. So an entry with no `hostCases` is the identity mapping (`Wires = []`),
    /// which is what keeps a pre-mapping vocabulary's read exactly what it was.
    /// Per-case annotations (Phase 119), keyed on the WIRE string the projection writes
    /// and resolved back to the HOST case name the model keys on. A wire string the enum
    /// does not declare is an ERROR rather than a silently dropped entry: the round-trip
    /// law is what this reader exists to satisfy, and a hand-edited artifact naming a
    /// case that is not there is the one thing the sparse shape can get wrong.
    let private readCaseAnnotations (e: IdlEnum) (v: JVal) : Result<(string * Annotations) list, DecodeError> =
        match Decoder.tryMember "caseAnnotations" v with
        | None -> Ok []
        | Some(JObj entries) ->
            entries
            |> List.map (fun (wire, block) ->
                match e.CaseOf wire with
                | None ->
                    Error(DecodeError.make DecodeCode.UnknownTag "a case the enum declares" ("case '" + wire + "'"))
                    |> under (PathSegment.Key wire)
                | Some case ->
                    readAnnotationBlock block
                    |> Result.map (fun a -> case, a)
                    |> under (PathSegment.Key wire))
            |> List.fold
                (fun acc r ->
                    match acc, r with
                    | Error e, _ -> Error e
                    | _, Error e -> Error e
                    | Ok xs, Ok x -> Ok(x :: xs))
                (Ok [])
            |> Result.map List.rev
            |> under (PathSegment.Key "caseAnnotations")
        | Some other ->
            Error(Decoder.wrongKind "object" other)
            |> under (PathSegment.Key "caseAnnotations")

    let private readEnum (v: JVal) : Result<IdlEnum, DecodeError> =
        str "name" v
        |> Result.bind (fun name ->
            listOf "cases" Decoder.str v
            |> Result.bind (fun wireCases ->
                match Decoder.tryMember "hostCases" v with
                | None -> Ok(Declare.enumOf name wireCases)
                | Some _ ->
                    listOf "hostCases" Decoder.str v
                    |> Result.bind (fun hostCases ->
                        if List.length hostCases = List.length wireCases then
                            Ok(Declare.enumWith name (List.zip hostCases wireCases))
                        else
                            Error(
                                DecodeError.make
                                    DecodeCode.OutOfRange
                                    (string (List.length wireCases) + " host cases")
                                    (string (List.length hostCases) + " host cases")
                            )
                            |> under (PathSegment.Key "hostCases")))
            // The case↔wire mapping has to be in hand before an entry keyed on a wire
            // string can be resolved, so the annotations are read into the enum rather
            // than alongside it.
            |> Result.bind (fun e ->
                readCaseAnnotations e v
                |> Result.bind (fun anns ->
                    readAnnotations v
                    |> Result.map (fun ann ->
                        { e with
                            CaseAnnotations = anns
                            Annotations = ann }))))

    let private readRecord (v: JVal) : Result<IdlRecord, DecodeError> =
        str "name" v
        |> Result.bind (fun name ->
            readFields v
            |> Result.bind (fun fields ->
                readAnnotations v
                |> Result.map (fun ann ->
                    { Name = name
                      Fields = fields
                      Annotations = ann })))

    let private readDefault (v: JVal) : Result<IdlDefault, DecodeError> =
        str "kind" v
        |> Result.bind (fun kind ->
            str "field" v
            |> Result.bind (fun field ->
                Decoder.field "value" readValue v
                |> Result.map (fun value ->
                    { Kind = kind
                      Field = field
                      Value = value })))

    /// The declared wire shape. Absent means [[WireShape.Default]] — the projection
    /// omits the block when it is the default, so every `$type`-nested vocabulary's
    /// artifact reads back unchanged. All three members are read as strings before either
    /// enumerated one is checked, so a block missing one reports the missing one.
    let private readWire (root: JVal) : Result<WireShape, DecodeError> =
        let shape (block: JVal) =
            str "discriminator" block
            |> Result.bind (fun disc ->
                Decoder.sequence
                    [ Decoder.field "nodeEnvelope" Decoder.str
                      Decoder.field "keyOrder" Decoder.str ]
                    block
                |> Result.bind (fun _ ->
                    Decoder.field
                        "nodeEnvelope"
                        (Decoder.oneOf
                            [ "nestedKind", NodeEnvelopeShape.NestedKind
                              "flatKind", NodeEnvelopeShape.FlatKind ])
                        block)
                |> Result.bind (fun e ->
                    Decoder.field
                        "keyOrder"
                        (Decoder.oneOf [ "sorted", KeyOrder.Sorted; "declared", KeyOrder.Declared ])
                        block
                    |> Result.map (fun k ->
                        { Discriminator = disc
                          NodeEnvelope = e
                          KeyOrder = k })))

        Decoder.fieldOr "wire" WireShape.Default shape root

    /// The declared hardening vocabulary. Absent means [[HardenPolicy.Undeclared]] —
    /// an artifact that names no hardening tokens has not named them, and a hardening
    /// run over the vocabulary it decodes to is `Trust.harden`'s typed refusal.
    ///
    /// **This answer was INVERTED by Phase 180, and the inversion is the whole point
    /// of the phase.** Until then an absent block resolved through
    /// `HardenPolicy.Default` — the five names the engine hard-coded before Phase 116
    /// made them declarable — so the block's ABSENCE was a positive claim in one
    /// domain's spelling. D40 refused to flip that in place, because both published
    /// `idl.json` artifacts carried no block and the flip would have changed what
    /// already-published bytes MEAN, silently and with a green build. The flip is safe
    /// now and only now: Phase 179 made [[json]] emit the block for every policy,
    /// `fuaran#1755` re-rendered `fuaran-dotnet/src/Fuaran.UI.Idl/idl.json` and the
    /// shared cross-host corpus, and the workspace copy registry reports both host snapshots
    /// of that corpus in step. So no artifact a consumer publishes relies on this
    /// answer, and one that did — rendered before Phase 179, carrying no block —
    /// decodes to a vocabulary that refuses to harden rather than one that hardens as
    /// a domain it never named.
    ///
    /// `IdlArtifactTests`' reader-flip family is the guard; read D40 before changing it
    /// back.
    let private readHarden (root: JVal) : Result<HardenPolicy, DecodeError> =
        let policy (block: JVal) =
            let s name = str name block

            let transparent =
                listOf
                    "transparentUnions"
                    (fun e ->
                        str "union" e
                        |> Result.bind (fun u -> str "case" e |> Result.map (fun c -> u, c)))
                    block

            s "gatedKind"
            |> Result.bind (fun gated ->
                s "placeholderKind"
                |> Result.bind (fun placeholder ->
                    s "placeholderField"
                    |> Result.bind (fun placeholderField ->
                        s "textLiteralCase"
                        |> Result.bind (fun textCase ->
                            s "textLiteralField"
                            |> Result.bind (fun textField ->
                                s "valueLiteralCase"
                                |> Result.bind (fun valueCase ->
                                    s "valueLiteralField"
                                    |> Result.bind (fun valueField ->
                                        transparent
                                        |> Result.map (fun unions ->
                                            { GatedKind = gated
                                              PlaceholderKind = placeholder
                                              PlaceholderField = placeholderField
                                              TextLiteralCase = textCase
                                              TextLiteralField = textField
                                              ValueLiteralCase = valueCase
                                              ValueLiteralField = valueField
                                              TransparentUnions = unions }))))))))

        Decoder.fieldOr "harden" HardenPolicy.Undeclared policy root

    /// Phase 292 — a read vocabulary is VALIDATED before anyone receives it: `idl.json` is
    /// untrusted input (DECISIONS D95), and a hand-edited one could otherwise carry a quote in
    /// its discriminator, a line break in an annotation or a dangling type name straight to
    /// an emitter. Every fault is named, not just the first, so one edit fixes them all — and
    /// since Phase 520 each is a `SchemaFault` AT ITS OWNER'S PATH (`Declare.errors`), where it
    /// was one root-level refusal whose sentence numbered them.
    let private validated (idl: Idl) : Result<Idl, DecodeError list> =
        match Declare.errors idl with
        | [] -> Ok idl
        | faults ->
            faults
            |> List.map (fun f ->
                DecodeError.make DecodeCode.SchemaFault "a well-formed vocabulary" f.Detail
                |> DecodeError.within f.At)
            |> Error

    /// Read a vocabulary from the artifact's parsed root, answering typed refusals (Phase 310;
    /// a LIST since Phase 520): a document that does not read is refused with the one refusal at
    /// its path — its code, the path to the value at fault, what the position expected — and a
    /// vocabulary that reads and does not declare well with every `SchemaFault` [[Declare.errors]]
    /// names, each at its owner's path (Phase 292).
    ///
    /// The encoding version is checked FIRST and refused by name when it is not this
    /// engine's: an artifact written by a newer encoder may spell a member this reader
    /// would silently drop, and a vocabulary that loses a field quietly is worse than
    /// one that will not load at all.
    let ofJsonDetailed (root: JVal) : Result<Idl, DecodeError list> =
        let read: Result<Idl, DecodeError> =
            match Decoder.tryMember "version" root with
            // A root that is not an object has no `version` either; its refusal is the root's
            // kind, at the root, where the path resolves.
            | None when
                (match root with
                 | JObj _ -> false
                 | _ -> true)
                ->
                Error(Decoder.wrongKind "object" root)
            | None -> Error(Decoder.missing "version")
            | Some(JInt v) when v <> version ->
                Error(
                    DecodeError.make
                        DecodeCode.OutOfRange
                        ("encoding version " + string version)
                        ("encoding version " + string v)
                )
                |> under (PathSegment.Key "version")
            | Some(JInt _) ->
                listOf "kinds" readKind root
                |> Result.bind (fun kinds ->
                    listOf "unions" readUnion root
                    |> Result.bind (fun unions ->
                        listOf "enums" readEnum root
                        |> Result.bind (fun enums ->
                            listOf "records" readRecord root
                            |> Result.bind (fun records ->
                                listOf "defaults" readDefault root
                                |> Result.bind (fun defaults ->
                                    listOf "nodeFields" readField root
                                    |> Result.bind (fun nodeFields ->
                                        // `ops` is omitted for an op-free vocabulary.
                                        listOrEmpty "ops" readKind root
                                        |> Result.bind (fun ops ->
                                            readWire root
                                            |> Result.bind (fun wire ->
                                                readHarden root
                                                |> Result.map (fun harden ->
                                                    { Kinds = kinds
                                                      Unions = unions
                                                      Enums = enums
                                                      Records = records
                                                      Defaults = defaults
                                                      NodeFields = nodeFields
                                                      Ops = ops
                                                      Wire = wire
                                                      Harden = harden })))))))))
            | Some other -> Error(Decoder.wrongKind "int" other) |> under (PathSegment.Key "version")

        read |> Result.mapError List.singleton |> Result.bind validated

    /// Every refusal of [[ofJsonDetailed]], one per line in `DecodeError.render`'s form (code, path,
    /// sentence) — the string forms' account.
    let private describeAll (errors: DecodeError list) : string =
        errors |> List.map DecodeError.render |> String.concat "\n"

    /// Read a vocabulary from the artifact's parsed root — [[ofJsonDetailed]]'s refusals as text,
    /// one per line, each with its code and path.
    let ofJson (root: JVal) : Result<Idl, string> =
        ofJsonDetailed root |> Result.mapError describeAll

    /// [[ofJsonDetailed]] over `idl.json` bytes; a parse failure is refused at the root.
    let parseDetailed (text: string) : Result<Idl, DecodeError list> =
        Decoder.parse ReadOptions.strict text
        |> Result.mapError List.singleton
        |> Result.bind ofJsonDetailed

    /// Read a vocabulary from `idl.json` bytes — the inverse of [[render]], up to the
    /// ordering [[canonicalise]] states.
    let parse (text: string) : Result<Idl, string> =
        parseDetailed text |> Result.mapError describeAll

    /// The [[Identity]] an artifact declares (Phase 252): its `description`, and its
    /// `name` when it carries one. The other half of [[parse]] — what the `Idl` record
    /// does not hold — so `renderWith (identityOf root) (ofJson root)` reproduces the
    /// artifact instead of re-stamping it with [[defaultDescription]].
    let identityOfJson (root: JVal) : Result<Identity, string> =
        str "description" root
        |> Result.bind (fun description ->
            Decoder.optField "name" Decoder.str root
            |> Result.map (fun name ->
                { Name = name
                  Description = description }))
        |> Result.mapError DecodeError.describe

    /// [[identityOfJson]] over `idl.json` bytes.
    let identityOf (text: string) : Result<Identity, string> =
        Json.parse text |> Result.bind identityOfJson
