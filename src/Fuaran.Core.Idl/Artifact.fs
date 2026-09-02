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
/// Object keys need no such rule — [[Canon.render]] Ordinal-sorts them recursively.
module Artifact =

    /// The artifact ENCODING version — bumped when this module's output shape
    /// changes (a new key, a renamed discriminator), never when the vocabulary it
    /// describes changes. A consumer pins the encoding, not the contents.
    [<Literal>]
    let version = 1

    let private ordinal (a: string) (b: string) = System.String.CompareOrdinal(a, b)

    /// Render a `JVal` with two-space indentation.
    ///
    /// Only WHITESPACE is added here: every scalar and every key string is rendered
    /// by [[Canon.render]] itself, so canonical escaping (rule 6), the pinned float
    /// layout (rule 5) and Ordinal key order are all INHERITED rather than
    /// re-implemented — the drift risk `TJson`'s verbatim passthrough names, in the
    /// one place a second stringifier would otherwise appear.
    ///
    /// Indented rather than compact because this artifact exists to be read and
    /// DIFFED, and a single-line file diffs as "everything changed". `schema.json`
    /// beside it in the corpus takes the same posture.
    let rec private indent (depth: int) (v: JVal) : string =
        let pad n = String.replicate n "  "

        match v with
        | JObj [] -> "{}"
        | JObj fields ->
            let body =
                fields
                |> List.sortWith (fun (a, _) (b, _) -> ordinal a b)
                |> List.map (fun (k, fv) -> pad (depth + 1) + Canon.render (JStr k) + ": " + indent (depth + 1) fv)
                |> String.concat ",\n"

            "{\n" + body + "\n" + pad depth + "}"
        | JArr [] -> "[]"
        | JArr xs ->
            let body =
                xs
                |> List.map (fun x -> pad (depth + 1) + indent (depth + 1) x)
                |> String.concat ",\n"

            "[\n" + body + "\n" + pad depth + "]"
        | scalar -> Canon.render scalar

    /// The structural type of a field. `$type` carries the [[IdlType]] case; the
    /// `wire` key, where present, states the FIXED wire form a third party will see
    /// for that case, so a sentinel is never mistaken for authored content.
    ///
    /// `hostSurface` (on [[TFn]] / [[THosted]]) carries the host-language strings
    /// from [[ClosureSig]] / [[HostedCodec]]. They are included because they are
    /// genuinely part of the contract a host must satisfy — and flagged under their
    /// own key because they are **host-surface spec, not wire spec**: nothing in
    /// them is observable on the wire, and a non-F# consumer reading this artifact
    /// to build a codec must ignore them entirely.
    let rec private typeJson (t: IdlType) : JVal =
        match t with
        | TStr -> Canon.typed "str" []
        | TInt -> Canon.typed "int" []
        | TBool -> Canon.typed "bool" []
        | TFloat -> Canon.typed "float" []
        | TEnum name -> Canon.typed "enum" [ "name", JStr name ]
        | TUnion(name, args) -> Canon.typed "union" [ "name", JStr name; "args", JArr(args |> List.map typeJson) ]
        | TVar name -> Canon.typed "var" [ "name", JStr name ]
        | TNode -> Canon.typed "node" []
        | TKind -> Canon.typed "kind" []
        | TOp -> Canon.typed "op" []
        | TList inner -> Canon.typed "list" [ "of", typeJson inner ]
        | TMap valueType -> Canon.typed "map" [ "values", typeJson valueType ]
        | TRecord name -> Canon.typed "record" [ "name", JStr name ]
        | TClosure -> Canon.typed "closure" [ "wire", JStr "<closure>" ]
        | TOpaque -> Canon.typed "opaque" [ "wire", JStr "<opaque>" ]
        | TJson -> Canon.typed "json" []
        | TFn sg ->
            Canon.typed
                "fn"
                [ "wire", JStr "<closure>"
                  "hostSurface",
                  JObj
                      [ "fsharp", JStr sg.FSharp
                        "typescript", JStr sg.TypeScript
                        "placeholder", JStr sg.Placeholder ] ]
        | THosted h ->
            Canon.typed
                "hosted"
                [ "wire", JStr "json"
                  "hostSurface", JObj [ "fsharp", JStr h.FSharp; "encode", JStr h.Encode; "decode", JStr h.Decode ] ]

    /// An authored value — a field default, or a nested part of one.
    let rec private valueJson (v: IdlValue) : JVal =
        match v with
        | VStr s -> Canon.typed "str" [ "value", JStr s ]
        | VInt i -> Canon.typed "int" [ "value", JInt i ]
        | VBool b -> Canon.typed "bool" [ "value", JBool b ]
        | VFloat f -> Canon.typed "float" [ "value", JFloat f ]
        | VEnum case -> Canon.typed "enum" [ "case", JStr case ]
        | VUnion(tag, fields) -> Canon.typed "union" [ "tag", JStr tag; "fields", namedValues fields ]
        | VList xs -> Canon.typed "list" [ "items", JArr(xs |> List.map valueJson) ]
        | VNode(id, kindTag, fields) ->
            Canon.typed "node" [ "id", JStr id; "kind", JStr kindTag; "fields", namedValues fields ]
        // Phase 698 — the enveloped form records its envelope under its own key
        // rather than merged into `fields`: the two namespaces can collide (an
        // envelope `style` and `Drawing.style` both exist), so merging them would
        // make the artefact ambiguous and the Phase 700 diff classifier wrong.
        | VNodeEnv(id, envelope, kindTag, fields) ->
            Canon.typed
                "node"
                [ "envelope", namedValues envelope
                  "fields", namedValues fields
                  "id", JStr id
                  "kind", JStr kindTag ]
        | VAbsent -> Canon.typed "absent" []
        | VClosure -> Canon.typed "closure" []
        | VOpaque -> Canon.typed "opaque" []
        // Verbatim — a `TJson` value is real data, and `Canon.render` already lays it
        // out canonically, so passing the `JVal` through inherits every rule.
        | VJson j -> Canon.typed "json" [ "value", j ]
        | VRecord fields -> Canon.typed "record" [ "fields", namedValues fields ]
        | VMap entries -> Canon.typed "map" [ "entries", namedValues entries ]

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
        | Required -> Canon.typed "required" []
        | Optional -> Canon.typed "optional" []
        | HostOnly -> Canon.typed "hostOnly" []
        | OmitDefault d -> Canon.typed "omitDefault" [ "default", valueJson d ]

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
    let private annotationsJson (a: Annotations) : (string * JVal) list =
        if a.IsEmpty then
            []
        else
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

            [ "annotations",
              JObj(
                  deprecated
                  @ (if a.InProcessOnly then
                         [ "inProcessOnly", JBool true ]
                     else
                         [])
                  @ (match a.Since with
                     | Some v -> [ "since", JStr v ]
                     | None -> [])
              ) ]

    /// Field lists keep their AUTHORED order (see the module's ordering contract).
    let private fieldsJson (fs: IdlField list) : JVal =
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
        JObj
            [ "tag", JStr k.Tag
              "category", JStr k.Category
              "fields", fieldsJson k.Fields ]

    let private unionJson (u: IdlUnion) : JVal =
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
        // object, so a consumer that missed it would decode the union wrongly. The
        // engine hard-codes the set by name ([[TransparentUnion]]) rather than
        // declaring it in the vocabulary; surfacing it here keeps the artifact
        // faithful instead of inheriting that gap.
        match TransparentUnion.tag u with
        | Some tag -> JObj(baseFields @ [ "transparentCase", JStr tag ])
        | None -> JObj baseFields

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
          Enums = idl.Enums |> sortedBy _.Name
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
          Wire = idl.Wire }

    /// The whole IDL as a `JVal`.
    ///
    /// [[canonicalise]] runs FIRST and owns every ordering decision; nothing below
    /// sorts. That is what keeps the ordering contract one definition now that
    /// [[parse]] has to reproduce it exactly.
    let json (idl: Idl) : JVal =
        let idl = canonicalise idl

        JObj(
            [ "version", JInt version
              "description",
              JStr(
                  "Canonical data rendering of the Fuaran UI wire vocabulary — kinds, unions, enums, "
                  + "records, field defaults and the node envelope. This is the STRUCTURAL source: it "
                  + "states what the vocabulary is, including optionality classes and omit-at-default "
                  + "values that a JSON Schema cannot express. schema.json beside it is the VALIDATION "
                  + "surface, derived from the same contract. Keys marked hostSurface are host-language "
                  + "declarations, not wire spec, and carry nothing observable on the wire. See "
                  + "WIRE_FORMAT.md section 13."
              )
              "kinds", JArr(idl.Kinds |> List.map kindJson)
              "unions", JArr(idl.Unions |> List.map unionJson)
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
                      ))
              )
              "records",
              JArr(
                  idl.Records
                  |> List.map (fun r -> JObj [ "name", JStr r.Name; "fields", fieldsJson r.Fields ])
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
        )

    /// The `idl.json` bytes — indented, canonically ordered, newline-terminated
    /// (matching `schema.json`'s convention in the same corpus).
    let render (idl: Idl) : string = indent 0 (json idl) + "\n"

    /// The same indented, canonically-ordered layout [[render]] uses, over an arbitrary
    /// `JVal`. Exposed so a SIBLING document of the vocabulary — the declared-support
    /// record beside it — lays out identically without a second stringifier appearing in
    /// the estate, which is the drift the module header names for `TJson`'s passthrough.
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

    let private atKey (name: string) (v: JVal) : JVal option =
        match v with
        | JObj fields -> fields |> List.tryPick (fun (n, x) -> if n = name then Some x else None)
        | _ -> None

    let private strAt (name: string) (v: JVal) : Result<string, string> =
        match atKey name v with
        | Some(JStr s) -> Ok s
        | Some _ -> Error("'" + name + "' is not a string")
        | None -> Error("missing '" + name + "'")

    let private arrAt (name: string) (v: JVal) : Result<JVal list, string> =
        match atKey name v with
        | Some(JArr xs) -> Ok xs
        | Some _ -> Error("'" + name + "' is not an array")
        | None -> Error("missing '" + name + "'")

    /// An array key that is OMITTED when empty (`params`, `args`, `fields`) reads as
    /// empty rather than as an error — the projection's omit-when-empty rule, read back.
    let private arrOrEmpty (name: string) (v: JVal) : Result<JVal list, string> =
        match atKey name v with
        | None -> Ok []
        | Some(JArr xs) -> Ok xs
        | Some _ -> Error("'" + name + "' is not an array")

    let private discriminator (v: JVal) : Result<string, string> = strAt "$type" v

    let private sequence (results: Result<'a, string> list) : Result<'a list, string> =
        (Ok [], results)
        ||> List.fold (fun acc r ->
            match acc, r with
            | Error e, _ -> Error e
            | _, Error e -> Error e
            | Ok xs, Ok x -> Ok(x :: xs))
        |> Result.map List.rev

    let private traverse (f: JVal -> Result<'a, string>) (xs: JVal list) : Result<'a list, string> =
        xs |> List.map f |> sequence

    /// The host-surface block of a `fn` / `hosted` type — the three verbatim host
    /// strings each carries. Named so the two arms report the same way.
    let private hostSurface (keys: string list) (v: JVal) : Result<string list, string> =
        match atKey "hostSurface" v with
        | None -> Error("'" + (defaultArg (List.tryHead keys) "?") + "' type has no 'hostSurface'")
        | Some block -> keys |> List.map (fun k -> strAt k block) |> sequence

    let rec private readType (v: JVal) : Result<IdlType, string> =
        match discriminator v with
        | Error e -> Error("type: " + e)
        | Ok t ->
            match t with
            | "str" -> Ok TStr
            | "int" -> Ok TInt
            | "bool" -> Ok TBool
            | "float" -> Ok TFloat
            | "node" -> Ok TNode
            | "kind" -> Ok TKind
            | "op" -> Ok TOp
            | "json" -> Ok TJson
            // `wire` is a RESTATEMENT of a fixed sentinel the engine already knows, not
            // a carried value — reading it back would let a hand-edited artifact
            // redefine what `<closure>` means.
            | "closure" -> Ok TClosure
            | "opaque" -> Ok TOpaque
            | "enum" -> strAt "name" v |> Result.map TEnum
            | "record" -> strAt "name" v |> Result.map TRecord
            | "var" -> strAt "name" v |> Result.map TVar
            | "list" ->
                match atKey "of" v with
                | Some inner -> readType inner |> Result.map TList
                | None -> Error "list type has no 'of'"
            | "map" ->
                match atKey "values" v with
                | Some inner -> readType inner |> Result.map TMap
                | None -> Error "map type has no 'values'"
            | "union" ->
                strAt "name" v
                |> Result.bind (fun n ->
                    arrOrEmpty "args" v
                    |> Result.bind (traverse readType)
                    |> Result.map (fun args -> TUnion(n, args)))
            | "fn" ->
                hostSurface [ "fsharp"; "typescript"; "placeholder" ] v
                |> Result.bind (function
                    | [ fs; ts; ph ] ->
                        Ok(
                            TFn
                                { FSharp = fs
                                  TypeScript = ts
                                  Placeholder = ph }
                        )
                    | _ -> Error "fn type has an incomplete 'hostSurface'")
            | "hosted" ->
                hostSurface [ "fsharp"; "encode"; "decode" ] v
                |> Result.bind (function
                    | [ fs; enc; dec ] ->
                        Ok(
                            THosted
                                { FSharp = fs
                                  Encode = enc
                                  Decode = dec }
                        )
                    | _ -> Error "hosted type has an incomplete 'hostSurface'")
            | other -> Error("unknown type '" + other + "'")

    let rec private readValue (v: JVal) : Result<IdlValue, string> =
        match discriminator v with
        | Error e -> Error("value: " + e)
        | Ok t ->
            match t with
            | "absent" -> Ok VAbsent
            | "closure" -> Ok VClosure
            | "opaque" -> Ok VOpaque
            | "str" ->
                match atKey "value" v with
                | Some(JStr s) -> Ok(VStr s)
                | _ -> Error "str value has no string 'value'"
            | "int" ->
                match atKey "value" v with
                | Some(JInt i) -> Ok(VInt i)
                | _ -> Error "int value has no integer 'value'"
            | "bool" ->
                match atKey "value" v with
                | Some(JBool b) -> Ok(VBool b)
                | _ -> Error "bool value has no boolean 'value'"
            // A whole-valued float renders with no `.` and no exponent, so the parser
            // hands it back as `JInt`. Reading only `JFloat` here would refuse every
            // `VFloat 1.0` the projection itself wrote.
            | "float" ->
                match atKey "value" v with
                | Some(JFloat f) -> Ok(VFloat f)
                | Some(JInt i) -> Ok(VFloat(float i))
                | _ -> Error "float value has no numeric 'value'"
            | "enum" -> strAt "case" v |> Result.map VEnum
            | "json" ->
                match atKey "value" v with
                | Some j -> Ok(VJson j)
                | None -> Error "json value has no 'value'"
            | "list" -> arrAt "items" v |> Result.bind (traverse readValue) |> Result.map VList
            | "union" ->
                strAt "tag" v
                |> Result.bind (fun tag -> readNamed "fields" v |> Result.map (fun fields -> VUnion(tag, fields)))
            | "record" -> readNamed "fields" v |> Result.map VRecord
            | "map" -> readNamed "entries" v |> Result.map VMap
            // The enveloped form is told from the bare one by the PRESENCE of the
            // `envelope` key, which is the same thing the projection branches on. An
            // empty envelope is still an envelope: `VNodeEnv(id, [], …)` renders
            // `"envelope": []` and must read back as itself.
            | "node" ->
                strAt "id" v
                |> Result.bind (fun id ->
                    strAt "kind" v
                    |> Result.bind (fun kindTag ->
                        readNamed "fields" v
                        |> Result.bind (fun fields ->
                            match atKey "envelope" v with
                            | None -> Ok(VNode(id, kindTag, fields))
                            | Some _ ->
                                readNamed "envelope" v
                                |> Result.map (fun env -> VNodeEnv(id, env, kindTag, fields)))))
            | other -> Error("unknown value kind '" + other + "'")

    and private readNamed (key: string) (owner: JVal) : Result<(string * IdlValue) list, string> =
        arrAt key owner
        |> Result.bind (
            traverse (fun entry ->
                strAt "name" entry
                |> Result.bind (fun name ->
                    match atKey "value" entry with
                    | Some value -> readValue value |> Result.map (fun v -> name, v)
                    | None -> Error("named value '" + name + "' has no 'value'")))
        )

    let private readOptionality (v: JVal) : Result<Optionality, string> =
        match discriminator v with
        | Error e -> Error("optionality: " + e)
        | Ok "required" -> Ok Required
        | Ok "optional" -> Ok Optional
        | Ok "hostOnly" -> Ok HostOnly
        | Ok "omitDefault" ->
            match atKey "default" v with
            | Some d -> readValue d |> Result.map OmitDefault
            | None -> Error "omitDefault has no 'default'"
        | Ok other -> Error("unknown optionality '" + other + "'")

    /// The annotation set, or [[Annotations.Empty]] when the key is absent — the
    /// projection omits an empty set entirely, so absence is the default and not a gap.
    let private readAnnotations (owner: JVal) : Result<Annotations, string> =
        match atKey "annotations" owner with
        | None -> Ok Annotations.Empty
        | Some block ->
            let optStr name =
                match atKey name block with
                | None -> Ok None
                | Some(JStr s) -> Ok(Some s)
                | Some _ -> Error("'" + name + "' is not a string")

            let deprecated =
                match atKey "deprecated" block with
                | None -> Ok None
                | Some d ->
                    let slot name =
                        match atKey name d with
                        | None -> Ok None
                        | Some(JStr s) -> Ok(Some s)
                        | Some _ -> Error("deprecated '" + name + "' is not a string")

                    slot "replacement"
                    |> Result.bind (fun r ->
                        slot "message" |> Result.map (fun m -> Some { Replacement = r; Message = m }))

            let inProcessOnly =
                match atKey "inProcessOnly" block with
                | None -> Ok false
                | Some(JBool b) -> Ok b
                | Some _ -> Error "'inProcessOnly' is not a boolean"

            deprecated
            |> Result.bind (fun d ->
                inProcessOnly
                |> Result.bind (fun ipo ->
                    optStr "since"
                    |> Result.map (fun since ->
                        { Deprecated = d
                          InProcessOnly = ipo
                          Since = since })))

    let private readField (v: JVal) : Result<IdlField, string> =
        strAt "name" v
        |> Result.bind (fun name ->
            match atKey "type" v, atKey "optionality" v with
            | None, _ -> Error("field '" + name + "' has no 'type'")
            | _, None -> Error("field '" + name + "' has no 'optionality'")
            | Some t, Some o ->
                readType t
                |> Result.bind (fun ty ->
                    readOptionality o
                    |> Result.bind (fun opt ->
                        readAnnotations v
                        |> Result.map (fun ann ->
                            { Name = name
                              Type = ty
                              Opt = opt
                              Annotations = ann }))))

    let private readFields (owner: JVal) : Result<IdlField list, string> =
        arrAt "fields" owner |> Result.bind (traverse readField)

    let private readKind (v: JVal) : Result<IdlKind, string> =
        strAt "tag" v
        |> Result.bind (fun tag ->
            strAt "category" v
            |> Result.bind (fun category ->
                readFields v
                |> Result.map (fun fields ->
                    { Tag = tag
                      Category = category
                      Fields = fields })))

    let private readUnion (v: JVal) : Result<IdlUnion, string> =
        strAt "name" v
        |> Result.bind (fun name ->
            arrOrEmpty "params" v
            |> Result.bind (
                traverse (function
                    | JStr s -> Ok s
                    | _ -> Error("union '" + name + "' has a non-string type parameter"))
            )
            |> Result.bind (fun ps ->
                arrAt "cases" v
                |> Result.bind (
                    traverse (fun c ->
                        strAt "tag" c
                        |> Result.bind (fun tag ->
                            readFields c
                            |> Result.bind (fun fields ->
                                readAnnotations c
                                |> Result.map (fun ann ->
                                    { Tag = tag
                                      Fields = fields
                                      Annotations = ann }))))
                )
                |> Result.map (fun cases ->
                    // `transparentCase` is DERIVED — the engine hard-codes the set by
                    // name and the projection surfaces it for a third-party reader.
                    // Reading it back would let an artifact claim a transparency the
                    // engine does not implement, so it is deliberately ignored here.
                    { Name = name
                      Params = ps
                      Cases = cases })))

    let private readStrings (name: string) (v: JVal) : Result<string list, string> =
        arrAt name v
        |> Result.bind (
            traverse (function
                | JStr s -> Ok s
                | _ -> Error("'" + name + "' has a non-string entry"))
        )

    /// `cases` is always the WIRE contract; `hostCases` appears only for a wire-mapped
    /// enum. So an entry with no `hostCases` is the identity mapping (`Wires = []`),
    /// which is what keeps a pre-mapping vocabulary's read exactly what it was.
    let private readEnum (v: JVal) : Result<IdlEnum, string> =
        strAt "name" v
        |> Result.bind (fun name ->
            readStrings "cases" v
            |> Result.bind (fun wireCases ->
                match atKey "hostCases" v with
                | None ->
                    Ok
                        { Name = name
                          Cases = wireCases
                          Wires = [] }
                | Some _ ->
                    readStrings "hostCases" v
                    |> Result.bind (fun hostCases ->
                        if List.length hostCases = List.length wireCases then
                            Ok
                                { Name = name
                                  Cases = hostCases
                                  Wires = wireCases }
                        else
                            Error("enum '" + name + "': 'hostCases' and 'cases' differ in length"))))

    let private readRecord (v: JVal) : Result<IdlRecord, string> =
        strAt "name" v
        |> Result.bind (fun name -> readFields v |> Result.map (fun fields -> { Name = name; Fields = fields }))

    let private readDefault (v: JVal) : Result<IdlDefault, string> =
        strAt "kind" v
        |> Result.bind (fun kind ->
            strAt "field" v
            |> Result.bind (fun field ->
                match atKey "value" v with
                | None -> Error("default " + kind + "." + field + " has no 'value'")
                | Some value ->
                    readValue value
                    |> Result.map (fun value ->
                        { Kind = kind
                          Field = field
                          Value = value })))

    /// The declared wire shape. Absent means [[WireShape.Default]] — the projection
    /// omits the block when it is the default, so every `$type`-nested vocabulary's
    /// artifact reads back unchanged.
    let private readWire (root: JVal) : Result<WireShape, string> =
        match atKey "wire" root with
        | None -> Ok WireShape.Default
        | Some block ->
            strAt "discriminator" block
            |> Result.bind (fun disc ->
                strAt "nodeEnvelope" block
                |> Result.bind (fun env ->
                    strAt "keyOrder" block
                    |> Result.bind (fun order ->
                        let envelope =
                            match env with
                            | "nestedKind" -> Ok NodeEnvelopeShape.NestedKind
                            | "flatKind" -> Ok NodeEnvelopeShape.FlatKind
                            | other -> Error("unknown nodeEnvelope '" + other + "'")

                        let keyOrder =
                            match order with
                            | "sorted" -> Ok KeyOrder.Sorted
                            | "declared" -> Ok KeyOrder.Declared
                            | other -> Error("unknown keyOrder '" + other + "'")

                        envelope
                        |> Result.bind (fun e ->
                            keyOrder
                            |> Result.map (fun k ->
                                { Discriminator = disc
                                  NodeEnvelope = e
                                  KeyOrder = k })))))

    /// Read a vocabulary from the artifact's parsed root.
    ///
    /// The encoding version is checked FIRST and refused by name when it is not this
    /// engine's: an artifact written by a newer encoder may spell a member this reader
    /// would silently drop, and a vocabulary that loses a field quietly is worse than
    /// one that will not load at all.
    let ofJson (root: JVal) : Result<Idl, string> =
        match atKey "version" root with
        | None -> Error "idl.json has no 'version'"
        | Some(JInt v) when v <> version ->
            Error(
                "idl.json declares encoding version "
                + string v
                + "; this engine reads version "
                + string version
            )
        | Some(JInt _) ->
            let listAt name read =
                arrAt name root |> Result.bind (traverse read)

            listAt "kinds" readKind
            |> Result.bind (fun kinds ->
                listAt "unions" readUnion
                |> Result.bind (fun unions ->
                    listAt "enums" readEnum
                    |> Result.bind (fun enums ->
                        listAt "records" readRecord
                        |> Result.bind (fun records ->
                            listAt "defaults" readDefault
                            |> Result.bind (fun defaults ->
                                arrAt "nodeFields" root
                                |> Result.bind (traverse readField)
                                |> Result.bind (fun nodeFields ->
                                    // `ops` is omitted for an op-free vocabulary.
                                    (match atKey "ops" root with
                                     | None -> Ok []
                                     | Some _ -> listAt "ops" readKind)
                                    |> Result.bind (fun ops ->
                                        readWire root
                                        |> Result.map (fun wire ->
                                            { Kinds = kinds
                                              Unions = unions
                                              Enums = enums
                                              Records = records
                                              Defaults = defaults
                                              NodeFields = nodeFields
                                              Ops = ops
                                              Wire = wire }))))))))
        | Some _ -> Error "idl.json 'version' is not an integer"

    /// Read a vocabulary from `idl.json` bytes — the inverse of [[render]], up to the
    /// ordering [[canonicalise]] states.
    let parse (text: string) : Result<Idl, string> = Json.parse text |> Result.bind ofJson
