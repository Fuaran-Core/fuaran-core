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
    /// Ordinal-sorted by name: these are wire KEYS, which `Canon.render` sorts anyway,
    /// so authored order carries no information to preserve here.
    and private namedValues (fields: (string * IdlValue) list) : JVal =
        fields
        |> List.sortWith (fun (a, _) (b, _) -> ordinal a b)
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

    /// Field lists keep their AUTHORED order (see the module's ordering contract).
    let private fieldsJson (fs: IdlField list) : JVal =
        fs
        |> List.map (fun f ->
            JObj
                [ "name", JStr f.Name
                  "type", typeJson f.Type
                  "optionality", optionalityJson f.Opt ])
        |> JArr

    let private kindJson (k: IdlKind) : JVal =
        JObj
            [ "tag", JStr k.Tag
              "category", JStr k.Category
              "fields", fieldsJson k.Fields ]

    let private unionJson (u: IdlUnion) : JVal =
        let cases =
            u.Cases
            |> List.map (fun c -> JObj [ "tag", JStr c.Tag; "fields", fieldsJson c.Fields ])
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

    /// The whole IDL as a `JVal`.
    let json (idl: Idl) : JVal =
        let sortedBy (key: 'a -> string) (xs: 'a list) =
            xs |> List.sortWith (fun a b -> ordinal (key a) (key b))

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
              "kinds", JArr(idl.Kinds |> sortedBy _.Tag |> List.map kindJson)
              "unions", JArr(idl.Unions |> sortedBy _.Name |> List.map unionJson)
              "enums",
              JArr(
                  idl.Enums
                  |> sortedBy _.Name
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
                  |> sortedBy _.Name
                  |> List.map (fun r -> JObj [ "name", JStr r.Name; "fields", fieldsJson r.Fields ])
              )
              "defaults",
              JArr(
                  idl.Defaults
                  |> List.sortWith (fun a b ->
                      match ordinal a.Kind b.Kind with
                      | 0 -> ordinal a.Field b.Field
                      | c -> c)
                  |> List.map (fun d -> JObj [ "kind", JStr d.Kind; "field", JStr d.Field; "value", valueJson d.Value ])
              )
              "nodeFields", fieldsJson idl.NodeFields ]
            // The op vocabulary (Phase 703) — the wire's second root. Emitted only
            // when the domain declares ops, so an op-free vocabulary's artefact is
            // byte-for-byte what it was, the same posture `hostCases` takes.
            @ (if List.isEmpty idl.Ops then
                   []
               else
                   [ "ops", JArr(idl.Ops |> sortedBy _.Tag |> List.map kindJson) ])
        )

    /// The `idl.json` bytes — indented, canonically ordered, newline-terminated
    /// (matching `schema.json`'s convention in the same corpus).
    let render (idl: Idl) : string = indent 0 (json idl) + "\n"
