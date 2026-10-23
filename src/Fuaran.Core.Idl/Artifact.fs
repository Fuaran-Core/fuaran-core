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

    let private canonPresence (c: PresenceCondition) : PresenceCondition =
        match c with
        | WhenEquals(s, v) -> WhenEquals(s, canonValue v)
        | other -> other

    let private canonFields (fs: IdlField list) : IdlField list =
        fs
        |> List.map (fun f ->
            { f with
                Opt = canonOpt f.Opt
                Presence = f.Presence |> Option.map canonPresence })

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
                        |> List.sortWith (fun (a, _) (b, _) -> ordinal (e.WireOf a) (e.WireOf b))
                    // Phase 539 — addressed exactly as `CaseAnnotations` is; each alias list keeps
                    // its order, which is its priority.
                    Aliases =
                        e.Aliases
                        |> List.filter (fun (_, a) -> not (List.isEmpty a))
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

    /// Who a vocabulary IS (Phase 252) — the artifact's `name` and `description` members.
    ///
    /// **Beside the [[Idl]], not on it.** Identity is a property of the published document rather
    /// than of the structure the generators read: no leg emits a different type, codec or schema
    /// for it, and carrying it on the record would widen a record every vocabulary builds by
    /// literal. So [[codecWith]] and [[renderWith]] take it, and [[identityOf]] reads it back, which
    /// is what lets an authored `idl.json` re-render to itself.
    ///
    /// Both members are optional (Phase 527, D40 amended): a vocabulary that declares no
    /// description gets none, where every artifact used to carry one domain's prose by default.
    type Identity =
        {
            /// The vocabulary's name, written as the artifact's `name` member when present.
            Name: string option
            /// The artifact's `description` member, verbatim, written when present.
            Description: string option
        }

    /// The identity that declares nothing: no `name` member and no `description` member. What
    /// [[codec]], [[json]] and [[render]] write.
    let unnamed: Identity = { Name = None; Description = None }

    // -----------------------------------------------------------------------
    // Phase 527 — the artifact is a CODEC.
    //
    // `idl.json` is written, read and described by ONE declaration built from `Wire`'s `Codec`
    // combinators, so its writer, its reader and its schema cannot disagree about a member's name,
    // whether it is required, or the kind of value it holds. Before this phase the writer was a
    // projection with six conditional-emission clauses, each with a hand-written reader mirror;
    // a conditional member is now ONE declaration (`Codec.defaultField` / `Codec.optField`) that
    // states both halves, and the omissions are one table ([[omitted]]).
    //
    // The read is STRICT, as every codec's is: a member the artifact does not declare is
    // `UndeclaredMember` rather than silently dropped — the same reason the encoding version is
    // checked first. And it COLLECTS: every defect the document carries is answered, each at its
    // path, where the hand readers stopped at the first.
    //
    // `Proposal` reads its types, defaults, optionalities and annotations through these codecs and
    // refuses what a proposal may not mint as a POLICY over the read (`Proposal`'s `admit`), so
    // there is one reader of the encoding and the refusals are kept.
    //
    // The law that makes it worth having: `parse (render idl) = canonicalise idl`, pinned over
    // every vocabulary the suite declares. Anything the codec drops fails it.
    // -----------------------------------------------------------------------

    /// A member written only away from its default: where it sits, its key, the value its absence
    /// means, and the test that omits it. One row of [[omitted]].
    type private Omission<'A> =
        { Owner: string
          Key: string
          Default: 'A
          Omits: 'A -> bool }

    /// The omit rules (Phase 527) — every member the artifact writes conditionally, each declared
    /// once and used once by the codec below. Each is a byte-stability clause: the member arrived
    /// after artifacts that lacked it were published, so it is omitted at the value their absence
    /// meant, and every earlier artifact stays byte-identical.
    module private Omit =
        /// Phase 703 — an op-free vocabulary writes no `ops`.
        let ops: Omission<IdlKind list> =
            { Owner = "the artifact"
              Key = "ops"
              Default = []
              Omits = List.isEmpty }

        /// Phases 108/109 — a `$type`-nested vocabulary writes no `wire`.
        let wire: Omission<WireShape> =
            { Owner = "the artifact"
              Key = "wire"
              Default = WireShape.Default
              Omits = (=) WireShape.Default }

        /// Phases 113/119/542 — a declaration that says nothing writes no `annotations`.
        let annotations: Omission<Annotations> =
            { Owner = "a kind, op, field, union, union case, enum or record"
              Key = "annotations"
              Default = Annotations.Empty
              Omits = fun a -> a.IsEmpty }

        /// Phase 113 — an annotation set writes `inProcessOnly` only when it is set.
        let inProcessOnly: Omission<bool> =
            { Owner = "an annotation set"
              Key = "inProcessOnly"
              Default = false
              Omits = not }

        /// Phase 119 — an enum none of whose cases says anything writes no `caseAnnotations`.
        let caseAnnotations: Omission<(string * Annotations) list> =
            { Owner = "an enum"
              Key = "caseAnnotations"
              Default = []
              Omits = List.isEmpty }

        /// Phase 539 — a member with no retired spelling writes no `aliases`.
        let fieldAliases: Omission<string list> =
            { Owner = "a field"
              Key = "aliases"
              Default = []
              Omits = List.isEmpty }

        /// Phase 539 — an enum none of whose cases declares an alias writes no `aliases`.
        let enumAliases: Omission<(string * string list) list> =
            { Owner = "an enum"
              Key = "aliases"
              Default = []
              Omits = List.isEmpty }

        /// Phase 550 — a union whose wire tags are its case names writes no `tagRule`.
        let tagRule: Omission<UnionTagRule> =
            { Owner = "a union"
              Key = "tagRule"
              Default = UnionTagRule.CaseName
              Omits = (=) UnionTagRule.CaseName }

        /// Phase 474 — a union told apart by its discriminator writes no `byPresence`.
        let discrimination: Omission<UnionDiscrimination> =
            { Owner = "a union"
              Key = "byPresence"
              Default = UnionDiscrimination.Tagged
              Omits = (=) UnionDiscrimination.Tagged }

        /// Phase 474 — a presence-keyed case that writes only its own members writes no `synthesised`.
        let synthesised: Omission<(string * IdlValue) list> =
            { Owner = "a presence-keyed case"
              Key = "synthesised"
              Default = []
              Omits = List.isEmpty }

        /// Phase 474 — a record that is its own object writes no `inline`.
        let recordInline: Omission<bool> =
            { Owner = "a record"
              Key = "inline"
              Default = false
              Omits = not }

        /// Phase 550 — a host surface (a hosted slot's, a case codec's) whose decode answers a
        /// sentence writes no `refusal`.
        let hostedRefusal: Omission<HostedRefusal> =
            { Owner = "a hosted slot's host surface"
              Key = "refusal"
              Default = HostedRefusal.Sentence
              Omits = (=) HostedRefusal.Sentence }

        /// Phase 707 — an enum whose wire strings are its case names writes no `hostCases`.
        let hostCases: Omission<string list option> =
            { Owner = "an enum"
              Key = "hostCases"
              Default = None
              Omits = Option.isNone }

    /// Every member the artifact omits at its default, as `(owner, member)` (Phase 527): the omit
    /// rule as one table. Members that are optional in the value itself — a vocabulary's `name`
    /// and `description`, a hosted slot's `format`, an annotation set's `since` / `doc` /
    /// `deprecated` — are written exactly when present and are not defaults to omit.
    let omitted: (string * string) list =
        [ Omit.ops.Owner, Omit.ops.Key
          Omit.wire.Owner, Omit.wire.Key
          Omit.annotations.Owner, Omit.annotations.Key
          Omit.inProcessOnly.Owner, Omit.inProcessOnly.Key
          Omit.caseAnnotations.Owner, Omit.caseAnnotations.Key
          Omit.hostCases.Owner, Omit.hostCases.Key
          Omit.fieldAliases.Owner, Omit.fieldAliases.Key
          Omit.enumAliases.Owner, Omit.enumAliases.Key
          Omit.tagRule.Owner, Omit.tagRule.Key
          Omit.hostedRefusal.Owner, Omit.hostedRefusal.Key
          Omit.discrimination.Owner, Omit.discrimination.Key
          Omit.synthesised.Owner, Omit.synthesised.Key
          Omit.recordInline.Owner, Omit.recordInline.Key ]

    /// The member `o` declares, written away from its default and read as it when absent.
    let private omitting
        (o: Omission<'A>)
        (get: 'R -> 'A)
        (c: Codec<'A>)
        (fs: CodecFields<'R, 'A -> 'C>)
        : CodecFields<'R, 'C> =
        Codec.defaultField o.Key o.Default o.Omits get c fs

    /// A member always written and read leniently — absent reads as `fallback`, so an artifact
    /// written before the member existed still reads (`args`, `params`, a sentinel's `wire`).
    let private lenient
        (name: string)
        (fallback: 'A)
        (get: 'R -> 'A)
        (c: Codec<'A>)
        (fs: CodecFields<'R, 'A -> 'C>)
        : CodecFields<'R, 'C> =
        Codec.defaultField name fallback (fun _ -> false) get c fs

    /// A declaration known to be consistent: every codec below is built once, at module
    /// initialisation, from literals the suite exercises, so a fault here is a defect in this file.
    let private declared (what: string) (built: Result<Codec<'T>, CodecDeclarationFault list>) : Codec<'T> =
        match built with
        | Ok c -> c
        | Error faults ->
            invalidOp (
                "Artifact: the "
                + what
                + " codec is inconsistent: "
                + (faults |> List.map CodecDeclarationFault.describe |> String.concat "; ")
            )

    /// A union case carrying no member beside its tag.
    let private bare (tag: string) (value: 'T) : CodecCase<'T> =
        Codec.case tag (fun v -> if v = value then Some() else None) (fun () -> value) (Codec.record ())

    /// A union case carrying one member.
    let private single (tag: string) (name: string) (project: 'T -> 'A option) (inject: 'A -> 'T) (c: Codec<'A>) =
        Codec.case tag project inject (Codec.record id |> Codec.field name id c)

    /// A FIXED sentinel spelling (`wire` on a closure, an opaque slot or a host-signature function):
    /// a RESTATEMENT of what the engine already knows, not a carried value, so it is written as the
    /// sentinel and read back only as the sentinel — a hand-edited artifact cannot redefine what
    /// `<closure>` means.
    let private sentinel (spelling: string) : Codec<unit> =
        Codec.refine
            ("the sentinel '" + spelling + "'")
            (fun s -> if s = spelling then Ok() else Error("'" + s + "'"))
            (fun () -> spelling)
            Codec.string

    let private stringRecord3
        (a: string)
        (b: string)
        (c: string)
        (make: string -> string -> string -> 'T)
        (get: 'T -> string * string * string)
        =
        Codec.record make
        |> Codec.field a (fun v -> let x, _, _ = get v in x) Codec.string
        |> Codec.field b (fun v -> let _, y, _ = get v in y) Codec.string
        |> Codec.field c (fun v -> let _, _, z = get v in z) Codec.string
        |> Codec.build
        |> declared ("hostSurface (" + a + ", " + b + ", " + c + ")")

    /// A host-signature function's `hostSurface`: the three verbatim host strings of a [[ClosureSig]].
    let private closureSurface: Codec<ClosureSig> =
        stringRecord3
            "fsharp"
            "typescript"
            "placeholder"
            (fun f t p ->
                { FSharp = f
                  TypeScript = t
                  Placeholder = p })
            (fun s -> s.FSharp, s.TypeScript, s.Placeholder)


    /// Phase 391 — a hosted slot's `format` is the closed [[HostedFormat]]; an unknown name is
    /// `UnknownTag` at it.
    let private hostedFormat: Codec<HostedFormat> =
        Codec.enum (HostedFormat.all |> List.map (fun f -> HostedFormat.name f, f))
        |> declared "format"

    /// Phase 550 — what a hosted slot's decode answers on a refusal.
    let private hostedRefusal: Codec<HostedRefusal> =
        Codec.enum [ "sentence", HostedRefusal.Sentence; "typed", HostedRefusal.Typed ]
        |> declared "hosted refusal"

    /// A hosted slot's `hostSurface`: its F# type, its two codec expressions and (Phase 550) what its
    /// decode answers on a refusal, written only when it is the typed `DecodeError`. The refusal is a
    /// fact about the host codec, never the wire, so it sits inside the host surface the stability
    /// classifier reads as host-surface.
    let private hostedSurface: Codec<HostedCodec> =
        (Codec.record (fun f e d refusal ->
            { FSharp = f
              Encode = e
              Decode = d
              Wire = None
              Format = None
              Refusal = refusal })
        : CodecFields<HostedCodec, _>)
        |> Codec.field "fsharp" _.FSharp Codec.string
        |> Codec.field "encode" _.Encode Codec.string
        |> Codec.field "decode" _.Decode Codec.string
        |> omitting Omit.hostedRefusal _.Refusal hostedRefusal
        |> Codec.build
        |> declared "hostSurface (fsharp, encode, decode)"

    /// Phase 550 — a union's tag-naming rule.
    let private tagRule: Codec<UnionTagRule> =
        Codec.enum [ "caseName", UnionTagRule.CaseName; "lowerFirst", UnionTagRule.LowerFirst ]
        |> declared "tag rule"

    /// Phase 252 — a hosted slot's DECLARED wire form, as a type object, or the string `"json"`
    /// when it declares none (carried verbatim, byte-for-byte what every earlier artifact wrote).
    let private hostedWire (idlType: Codec<IdlType>) : Codec<IdlType option> =
        { Write =
            fun w ->
                match w with
                | None -> Ok(JStr "json")
                | Some t -> idlType.Write t
          ReadAll =
            fun j ->
                match j with
                | JStr "json" -> Ok None
                | JObj _ -> idlType.ReadAll j |> Result.map Some
                | other -> Error [ DecodeError.make DecodeCode.WrongKind "\"json\" or a type" (JVal.kindName other) ]
          Schema = JObj [ "oneOf", JArr [ JObj [ "const", JStr "json" ]; idlType.Schema ] ] }

    /// The structural type of a field. `$type` carries the [[IdlType]] case; the `wire` member,
    /// where present, states the FIXED wire form a third party will see for that case, so a
    /// sentinel is never mistaken for authored content — or, on a hosted slot, its DECLARED wire
    /// form (Phase 252).
    ///
    /// `hostSurface` (on [[TFn]] / [[THosted]]) carries the host-language strings from
    /// [[ClosureSig]] / [[HostedCodec]]. They are part of the contract a host must satisfy, and
    /// flagged under their own key because they are **host-surface spec, not wire spec**: nothing
    /// in them is observable on the wire, and a non-F# consumer reading this artifact to build a
    /// codec must ignore them entirely.
    ///
    /// The object spells the DECLARED type, not its lowered wire form (Phase 513): a closure and a
    /// host-signature function stay two cases, a union stays named and applied rather than
    /// substituted, and a type variable stays a variable, because the artifact is read back into
    /// the declaration. What it states about the wire — the sentinel spellings — it reads from
    /// [[WireForm]], the one lowering the codec and the sampler switch on.
    let internal idlType: Codec<IdlType> =
        Codec.recursive "IdlType" (fun self ->
            Codec.union
                "$type"
                [ bare "str" TStr
                  bare "int" TInt
                  bare "bool" TBool
                  bare "float" TFloat
                  bare "node" TNode
                  bare "kind" TKind
                  bare "op" TOp
                  bare "json" TJson
                  Codec.case
                      "closure"
                      (fun t -> if t = TClosure then Some() else None)
                      (fun () -> TClosure)
                      (Codec.record id |> lenient "wire" () id (sentinel WireForm.closure))
                  Codec.case
                      "opaque"
                      (fun t -> if t = TOpaque then Some() else None)
                      (fun () -> TOpaque)
                      (Codec.record id |> lenient "wire" () id (sentinel WireForm.opaque))
                  single
                      "enum"
                      "name"
                      (function
                      | TEnum n -> Some n
                      | _ -> None)
                      TEnum
                      Codec.string
                  single
                      "record"
                      "name"
                      (function
                      | TRecord n -> Some n
                      | _ -> None)
                      TRecord
                      Codec.string
                  single
                      "var"
                      "name"
                      (function
                      | TVar n -> Some n
                      | _ -> None)
                      TVar
                      Codec.string
                  single
                      "list"
                      "of"
                      (function
                      | TList t -> Some t
                      | _ -> None)
                      TList
                      self
                  single
                      "map"
                      "values"
                      (function
                      | TMap t -> Some t
                      | _ -> None)
                      TMap
                      self
                  Codec.case
                      "union"
                      (function
                      | TUnion(n, args) -> Some(n, args)
                      | _ -> None)
                      TUnion
                      (Codec.record (fun n args -> n, args)
                       |> Codec.field "name" fst Codec.string
                       // Positional — never sorted.
                       |> lenient "args" [] snd (Codec.list self))
                  Codec.case
                      "fn"
                      (function
                      | TFn sg -> Some sg
                      | _ -> None)
                      TFn
                      (Codec.record (fun () sg -> sg)
                       |> lenient "wire" () ignore (sentinel WireForm.closure)
                       |> Codec.field "hostSurface" id closureSurface)
                  // Phase 252 — `wire` states the slot's DECLARED wire form and `format` the string
                  // format on top of it; both sit OUTSIDE hostSurface: they are wire spec a
                  // third-party codec reads.
                  Codec.case
                      "hosted"
                      (function
                      | THosted h -> Some h
                      | _ -> None)
                      THosted
                      (Codec.record (fun (surface: HostedCodec) w fmt -> { surface with Wire = w; Format = fmt })
                       |> Codec.field "hostSurface" id hostedSurface
                       |> lenient "wire" None _.Wire (hostedWire self)
                       |> Codec.optField "format" _.Format hostedFormat) ]
            |> declared "type")

    /// An authored float — a field default's, or a nested part of one. Phase 303: a NON-FINITE
    /// float is written as WIRE_FORMAT §7's quoted token (`Rendering.CrossHost` spells a
    /// non-finite `JFloat` as the string `"NaN"`), so the token reads back at this one value tag,
    /// or `render` would write an `idl.json` carrying a `VFloat nan` default its own `parse`
    /// refuses. A whole-valued float renders with no `.`, so either number constructor reads.
    let private authoredFloat: Codec<float> =
        { Codec.float with
            ReadAll =
                fun j ->
                    match j with
                    | JStr(FloatToken.NonFinite f) -> Ok f
                    | _ -> Codec.float.ReadAll j
            Schema =
                JObj
                    [ "oneOf",
                      JArr
                          [ JObj [ "type", JStr "number" ]
                            JObj [ "enum", JArr [ JStr "NaN"; JStr "Infinity"; JStr "-Infinity" ] ] ] ] }

    /// An authored value — a field default, or a nested part of one. Named sub-values (union /
    /// record / node / map fields) are written in the order they arrive: they are wire KEYS, which
    /// carry no authored order to preserve, and [[canonicalise]] — which every write runs first —
    /// has already Ordinal-sorted them, so the model ordering and the artifact ordering are one
    /// definition. List ITEMS keep their order: it is the value.
    let internal idlValue: Codec<IdlValue> =
        Codec.recursive "IdlValue" (fun self ->
            let named: Codec<(string * IdlValue) list> =
                Codec.record (fun n v -> n, v)
                |> Codec.field "name" fst Codec.string
                |> Codec.field "value" snd self
                |> Codec.build
                |> declared "named value"
                |> Codec.list

            let one tag name project inject c = single tag name project inject c

            Codec.union
                "$type"
                [ bare "absent" VAbsent
                  bare "closure" VClosure
                  bare "opaque" VOpaque
                  one
                      "str"
                      "value"
                      (function
                      | VStr s -> Some s
                      | _ -> None)
                      VStr
                      Codec.string
                  one
                      "int"
                      "value"
                      (function
                      | VInt i -> Some i
                      | _ -> None)
                      VInt
                      Codec.int
                  one
                      "bool"
                      "value"
                      (function
                      | VBool b -> Some b
                      | _ -> None)
                      VBool
                      Codec.bool
                  one
                      "float"
                      "value"
                      (function
                      | VFloat f -> Some f
                      | _ -> None)
                      VFloat
                      authoredFloat
                  one
                      "enum"
                      "case"
                      (function
                      | VEnum c -> Some c
                      | _ -> None)
                      VEnum
                      Codec.string
                  // Verbatim — a `TJson` value is real data, and `Rendering.CrossHost` already lays
                  // it out canonically, so passing the `JVal` through inherits every rule.
                  one
                      "json"
                      "value"
                      (function
                      | VJson j -> Some j
                      | _ -> None)
                      VJson
                      Codec.json
                  one
                      "list"
                      "items"
                      (function
                      | VList xs -> Some xs
                      | _ -> None)
                      VList
                      (Codec.list self)
                  Codec.case
                      "union"
                      (function
                      | VUnion(tag, fields) -> Some(tag, fields)
                      | _ -> None)
                      VUnion
                      (Codec.record (fun t fs -> t, fs)
                       |> Codec.field "tag" fst Codec.string
                       |> Codec.field "fields" snd named)
                  one
                      "record"
                      "fields"
                      (function
                      | VRecord fs -> Some fs
                      | _ -> None)
                      VRecord
                      named
                  one
                      "map"
                      "entries"
                      (function
                      | VMap es -> Some es
                      | _ -> None)
                      VMap
                      named
                  // Phase 698 — the enveloped form records its envelope under its own key rather
                  // than merged into `fields`: the two namespaces can collide (an envelope `style`
                  // and `Drawing.style` both exist). It is told from the bare form by the PRESENCE
                  // of `envelope`; an empty envelope is still one (`"envelope": []`).
                  Codec.case
                      "node"
                      (function
                      | VNode(id, kind, fields) -> Some(id, kind, fields, None)
                      | VNodeEnv(id, envelope, kind, fields) -> Some(id, kind, fields, Some envelope)
                      | _ -> None)
                      (fun (id, kind, fields, envelope) ->
                          match envelope with
                          | None -> VNode(id, kind, fields)
                          | Some e -> VNodeEnv(id, e, kind, fields))
                      (Codec.record (fun id kind fields envelope -> id, kind, fields, envelope)
                       |> Codec.field "id" (fun (id, _, _, _) -> id) Codec.string
                       |> Codec.field "kind" (fun (_, kind, _, _) -> kind) Codec.string
                       |> Codec.field "fields" (fun (_, _, fields, _) -> fields) named
                       |> Codec.optField "envelope" (fun (_, _, _, envelope) -> envelope) named) ]
            |> declared "value")

    /// Whether a field is on the wire, and under what condition. `omitDefault` carries the identity
    /// default VALUE — the single thing a JSON Schema projection of the same vocabulary cannot
    /// express.
    let internal optionality: Codec<Optionality> =
        Codec.union
            "$type"
            [ bare "required" Required
              bare "optional" Optional
              bare "hostOnly" HostOnly
              single
                  "omitDefault"
                  "default"
                  (function
                  | OmitDefault d -> Some d
                  | _ -> None)
                  OmitDefault
                  idlValue ]
        |> declared "optionality"

    /// One annotation set (Phase 113) — the value under an owner's `annotations` member and
    /// (Phase 119) under each entry of an enum's `caseAnnotations`. Each slot is written exactly
    /// when it says something, so `since` alone renders as `{"since": "…"}`.
    ///
    /// **Not a hostSurface key.** A `hostSurface` block is a host-LANGUAGE declaration a non-F#
    /// consumer must ignore (§13); an annotation is a statement about the vocabulary itself that
    /// every consumer wants — a third-party codec needs to know a case is being retired quite as
    /// much as the reference host does.
    let internal annotations: Codec<Annotations> =
        let deprecation =
            Codec.record (fun r m -> { Replacement = r; Message = m })
            |> Codec.optField "replacement" _.Replacement Codec.string
            |> Codec.optField "message" _.Message Codec.string
            |> Codec.build
            |> declared "deprecation"

        Codec.record (fun d ipo since doc ->
            { Deprecated = d
              InProcessOnly = ipo
              Since = since
              Doc = doc })
        |> Codec.optField "deprecated" _.Deprecated deprecation
        |> omitting Omit.inProcessOnly _.InProcessOnly Codec.bool
        |> Codec.optField "since" _.Since Codec.string
        // Phase 255 — the authored doc, verbatim.
        |> Codec.optField "doc" _.Doc Codec.string
        |> Codec.build
        |> declared "annotations"

    /// A declaration's own `annotations` member, omitted when it says nothing.
    let private annotated (get: 'R -> Annotations) (fs: CodecFields<'R, Annotations -> 'C>) : CodecFields<'R, 'C> =
        omitting Omit.annotations get annotations fs

    /// A field's presence condition (Phase 539, DECISIONS.md D190): `$type`-tagged by its test, the
    /// sibling it reads, and for an equality test the value, written as every authored value is.
    let internal presenceCondition: Codec<PresenceCondition> =
        Codec.union
            "$type"
            [ single
                  "whenPresent"
                  "sibling"
                  (function
                  | WhenPresent s -> Some s
                  | _ -> None)
                  WhenPresent
                  Codec.string
              single
                  "whenAbsent"
                  "sibling"
                  (function
                  | WhenAbsent s -> Some s
                  | _ -> None)
                  WhenAbsent
                  Codec.string
              Codec.case
                  "whenEquals"
                  (function
                  | WhenEquals(s, v) -> Some(s, v)
                  | _ -> None)
                  WhenEquals
                  (Codec.record (fun s v -> s, v)
                   |> Codec.field "sibling" fst Codec.string
                   |> Codec.field "value" snd idlValue) ]
        |> declared "presence"

    /// One field object. Field lists keep their AUTHORED order (the module's ordering contract).
    /// Phase 539 — `aliases` (in priority order) and `presence` follow the optionality, each
    /// omitted when it says nothing, so a vocabulary that declares neither renders as it did.
    let internal field: Codec<IdlField> =
        (Codec.record (fun name ty opt aliases presence ann ->
            { Name = name
              Type = ty
              Opt = opt
              Aliases = aliases
              Presence = presence
              Annotations = ann })
        : CodecFields<IdlField, _>)
        |> Codec.field "name" _.Name Codec.string
        |> Codec.field "type" _.Type idlType
        |> Codec.field "optionality" _.Opt optionality
        |> omitting Omit.fieldAliases _.Aliases (Codec.list Codec.string)
        |> Codec.optField "presence" _.Presence presenceCondition
        |> annotated _.Annotations
        |> Codec.build
        |> declared "field"

    let private fields: Codec<IdlField list> = Codec.list field

    /// A node kind or an op case.
    let private kind: Codec<IdlKind> =
        (Codec.record (fun tag category fs ann ->
            { Tag = tag
              Category = category
              Fields = fs
              Annotations = ann })
        : CodecFields<IdlKind, _>)
        |> Codec.field "tag" _.Tag Codec.string
        |> Codec.field "category" _.Category Codec.string
        |> Codec.field "fields" _.Fields fields
        // Phase 119 — the kind's OWN annotations, on a field's terms.
        |> annotated _.Annotations
        |> Codec.build
        |> declared "kind"

    /// Phase 550 — a case-level host codec: the host surface and the refusal. It declares no wire
    /// form of its own (the case's fields do), so a case codec is read with `Wire` and `Format` unset.
    let private caseHosted: Codec<HostedCodec> =
        Codec.record id
        |> Codec.field "hostSurface" id hostedSurface
        |> Codec.build
        |> declared "case host codec"

    let private unionCase: Codec<IdlUnionCase> =
        (Codec.record (fun tag fs ann hosted ->
            ({ Tag = tag
               Fields = fs
               Annotations = ann
               Hosted = hosted }
            : IdlUnionCase))
        : CodecFields<IdlUnionCase, _>)
        |> Codec.field "tag" _.Tag Codec.string
        |> Codec.field "fields" _.Fields fields
        |> annotated _.Annotations
        // Phase 550 — a case-level host codec: its host surface and its refusal, written only when
        // declared.
        |> Codec.optField "hosted" _.Hosted caseHosted
        |> Codec.build
        |> declared "union case"

    /// Phase 474 — one case's presence rule in a presence-keyed union: the case, its key member and
    /// the members its encoder synthesises (keyed by member name, each at its authored value).
    let private keyedCase: Codec<KeyedCase> =
        (Codec.record (fun case key synthesised ->
            ({ Case = case
               Key = key
               Synthesised = synthesised }
            : KeyedCase))
        : CodecFields<KeyedCase, _>)
        |> Codec.field "case" _.Case Codec.string
        |> Codec.field "key" _.Key Codec.string
        |> omitting Omit.synthesised _.Synthesised (Codec.entries idlValue)
        |> Codec.build
        |> declared "presence-keyed case"

    /// Phase 474 — a union's discrimination as the artifact spells it: the presence rules of a
    /// presence-keyed union, one per case in case order (a tagged union writes nothing).
    let private byPresence: Codec<UnionDiscrimination> =
        Codec.map
            UnionDiscrimination.ByPresence
            (function
            | UnionDiscrimination.ByPresence keyed -> keyed
            | UnionDiscrimination.Tagged -> [])
            (Codec.list keyedCase)

    /// A union declaration, beside the `transparentCase` DERIVED for it. A transparent case
    /// encodes as a BARE value rather than a `$type`-tagged object, so a consumer that missed it
    /// would decode the union wrongly; it is derived from the vocabulary's declared
    /// [[HardenPolicy.TransparentUnions]] (Phase 116) and written per union because that is where a
    /// third-party decoder needs it. It is READ and not kept: the declaration it derives from is
    /// read back, so an artifact cannot claim a per-union transparency the declared policy does
    /// not state.
    let private union: Codec<IdlUnion * string option> =
        (Codec.record (fun name ps cases transparent ann rule discrimination ->
            ({ Name = name
               Params = ps
               Cases = cases
               Annotations = ann
               TagRule = rule
               Discrimination = discrimination }
            : IdlUnion),
            transparent)
        : CodecFields<IdlUnion * string option, _>)
        |> Codec.field "name" (fun (u, _) -> u.Name) Codec.string
        // Positional — never sorted.
        |> lenient "params" [] (fun (u, _) -> u.Params) (Codec.list Codec.string)
        |> Codec.field "cases" (fun (u, _) -> u.Cases) (Codec.list unionCase)
        |> Codec.optField "transparentCase" snd Codec.string
        // Phase 542 — the union's OWN annotations.
        |> annotated (fun (u, _) -> u.Annotations)
        // Phase 550 — the tag-naming rule, omitted at `caseName`.
        |> omitting Omit.tagRule (fun (u, _) -> u.TagRule) tagRule
        // Phase 474 — the presence rules of a presence-keyed union, omitted for a tagged one.
        |> omitting Omit.discrimination (fun (u, _) -> u.Discrimination) byPresence
        |> Codec.build
        |> declared "union"

    /// An enum as the artifact spells it, before its case↔wire mapping is resolved.
    type private EnumEntry =
        { Name: string
          WireCases: string list
          HostCases: string list option
          CaseAnnotations: (string * Annotations) list
          Aliases: (string * string list) list
          Annotations: Annotations }

    /// `cases` is the wire contract (what a decoder must accept). `hostCases` appears ONLY for a
    /// Phase 707 wire-mapped enum and is a hostSurface key in the §13 sense. `caseAnnotations`
    /// (Phase 119) is keyed by the WIRE string rather than the host case name: `cases` is the one
    /// key every revision carries, so a third-party reader resolves an entry without consulting
    /// the conditional `hostCases`. Resolving it back is where a defect can hide, so a `hostCases`
    /// of the wrong length and an entry naming a case the enum does not declare are each refused at
    /// their own path.
    let private enumDecl: Codec<IdlEnum> =
        let entry =
            (Codec.record (fun name cases hostCases caseAnnotations aliases ann ->
                ({ Name = name
                   WireCases = cases
                   HostCases = hostCases
                   CaseAnnotations = caseAnnotations
                   Aliases = aliases
                   Annotations = ann }
                : EnumEntry))
            : CodecFields<EnumEntry, _>)
            |> Codec.field "name" _.Name Codec.string
            |> Codec.field "cases" _.WireCases (Codec.list Codec.string)
            |> omitting Omit.hostCases _.HostCases (Codec.map Some Option.get (Codec.list Codec.string))
            |> omitting Omit.caseAnnotations _.CaseAnnotations (Codec.entries annotations)
            // Phase 539 — the lenient spellings of each case, keyed by the case's WIRE string as
            // `caseAnnotations` is and for the same reason.
            |> omitting Omit.enumAliases _.Aliases (Codec.entries (Codec.list Codec.string))
            // Phase 542 — the enum's OWN annotations.
            |> annotated _.Annotations
            |> Codec.build
            |> declared "enum"

        let resolve (raw: EnumEntry) =
            let mapped =
                match raw.HostCases with
                | None -> Ok(Declare.enumOf raw.Name raw.WireCases)
                | Some hosts when List.length hosts = List.length raw.WireCases ->
                    Ok(Declare.enumWith raw.Name (List.zip hosts raw.WireCases))
                | Some hosts ->
                    Error
                        [ [ PathSegment.Key "hostCases" ],
                          RootDecodeCode.OutOfRange,
                          string (List.length raw.WireCases) + " host cases",
                          string (List.length hosts) + " host cases" ]

            mapped
            |> Result.bind (fun e ->
                // An entry keyed by a wire string resolves to its case, or is refused at its path.
                let byCase (key: string) (entries: (string * 'A) list) =
                    entries
                    |> List.map (fun (wire, a) ->
                        match e.CaseOf wire with
                        | Some case -> Ok(case, a)
                        | None ->
                            Error(
                                [ PathSegment.Key key; PathSegment.Key wire ],
                                RootDecodeCode.UnknownTag,
                                "a case the enum declares",
                                "case '" + wire + "'"
                            ))

                let caseAnnotations = byCase "caseAnnotations" raw.CaseAnnotations
                let aliases = byCase "aliases" raw.Aliases

                let faults xs =
                    xs
                    |> List.choose (function
                        | Error f -> Some f
                        | Ok _ -> None)

                match faults caseAnnotations @ faults aliases with
                | [] ->
                    Ok
                        { e with
                            CaseAnnotations = caseAnnotations |> List.choose Result.toOption
                            Aliases = aliases |> List.choose Result.toOption
                            Annotations = raw.Annotations }
                | faults -> Error faults)

        let spell (e: IdlEnum) : EnumEntry =
            { Name = e.Name
              WireCases = e.WireCases
              HostCases = if List.isEmpty e.Wires then None else Some e.Cases
              CaseAnnotations =
                e.CaseAnnotations
                |> List.filter (fun (_, a) -> not a.IsEmpty)
                |> List.map (fun (c, a) -> e.WireOf c, a)
              Aliases =
                e.Aliases
                |> List.filter (fun (_, a) -> not (List.isEmpty a))
                |> List.map (fun (c, a) -> e.WireOf c, a)
              Annotations = e.Annotations }

        Codec.narrow resolve spell entry

    let private record: Codec<IdlRecord> =
        (Codec.record (fun name fs ann isInline ->
            ({ Name = name
               Fields = fs
               Annotations = ann
               Inline = isInline }
            : IdlRecord))
        : CodecFields<IdlRecord, _>)
        |> Codec.field "name" _.Name Codec.string
        |> Codec.field "fields" _.Fields fields
        // Phase 542 — the record's OWN annotations.
        |> annotated _.Annotations
        // Phase 474 — an inline record's members are keys of the object holding it.
        |> omitting Omit.recordInline _.Inline Codec.bool
        |> Codec.build
        |> declared "record"

    let private fieldDefault: Codec<IdlDefault> =
        (Codec.record (fun kind field value ->
            ({ Kind = kind
               Field = field
               Value = value }
            : IdlDefault))
        : CodecFields<IdlDefault, _>)
        |> Codec.field "kind" _.Kind Codec.string
        |> Codec.field "field" _.Field Codec.string
        |> Codec.field "value" _.Value idlValue
        |> Codec.build
        |> declared "default"

    /// The declared wire shape (Phases 108/109).
    let private wireShape: Codec<WireShape> =
        Codec.record (fun disc envelope order ->
            { Discriminator = disc
              NodeEnvelope = envelope
              KeyOrder = order })
        |> Codec.field "discriminator" _.Discriminator Codec.string
        |> Codec.field
            "nodeEnvelope"
            _.NodeEnvelope
            (Codec.enum
                [ "nestedKind", NodeEnvelopeShape.NestedKind
                  "flatKind", NodeEnvelopeShape.FlatKind ]
             |> declared "nodeEnvelope")
        |> Codec.field
            "keyOrder"
            _.KeyOrder
            (Codec.enum [ "sorted", KeyOrder.Sorted; "declared", KeyOrder.Declared ]
             |> declared "keyOrder")
        |> Codec.build
        |> declared "wire"

    /// The declared hardening vocabulary (Phase 116). `transparentUnions` is the one member a WIRE
    /// consumer must read — the per-union `transparentCase` is derived from it; the rest is
    /// codegen-boundary spec, what the trust boundary gates and mints in its place.
    let private harden: Codec<HardenPolicy> =
        let transparent =
            Codec.record (fun u c -> u, c)
            |> Codec.field "union" fst Codec.string
            |> Codec.field "case" snd Codec.string
            |> Codec.build
            |> declared "transparent union"

        Codec.record (fun gated placeholder placeholderField textCase textField valueCase valueField unions ->
            { GatedKind = gated
              PlaceholderKind = placeholder
              PlaceholderField = placeholderField
              TextLiteralCase = textCase
              TextLiteralField = textField
              ValueLiteralCase = valueCase
              ValueLiteralField = valueField
              TransparentUnions = unions })
        |> Codec.field "gatedKind" _.GatedKind Codec.string
        |> Codec.field "placeholderKind" _.PlaceholderKind Codec.string
        |> Codec.field "placeholderField" _.PlaceholderField Codec.string
        |> Codec.field "textLiteralCase" _.TextLiteralCase Codec.string
        |> Codec.field "textLiteralField" _.TextLiteralField Codec.string
        |> Codec.field "valueLiteralCase" _.ValueLiteralCase Codec.string
        |> Codec.field "valueLiteralField" _.ValueLiteralField Codec.string
        |> Codec.field "transparentUnions" _.TransparentUnions (Codec.list transparent)
        |> Codec.build
        |> declared "harden"

    /// The encoding version: written as [[version]], and refused by name when it is not this
    /// engine's.
    let private encodingVersion: Codec<unit> =
        Codec.refine
            ("encoding version " + string version)
            (fun v ->
                if v = version then
                    Ok()
                else
                    Error("encoding version " + string v))
            (fun () -> version)
            Codec.int

    /// The whole document: the vocabulary's [[Identity]] and the vocabulary. The write runs
    /// [[canonicalise]] FIRST, which owns every ordering decision; nothing in the codec sorts.
    let private document: Codec<Identity * Idl> =
        let c =
            Codec.record (fun () name description kinds unions enums records defaults nodeFields ops wire harden ->
                ({ Name = name
                   Description = description }
                : Identity),
                { Kinds = kinds
                  Unions = unions |> List.map fst
                  Enums = enums
                  Records = records
                  Defaults = defaults
                  NodeFields = nodeFields
                  Ops = ops
                  Wire = wire
                  Harden = harden })
            |> Codec.field "version" ignore encodingVersion
            // The vocabulary's identity (Phase 252), each member written only when declared.
            |> Codec.optField "name" (fun (i: Identity, _) -> i.Name) Codec.string
            |> Codec.optField "description" (fun (i: Identity, _) -> i.Description) Codec.string
            |> Codec.field "kinds" (fun (_, idl: Idl) -> idl.Kinds) (Codec.list kind)
            |> Codec.field
                "unions"
                (fun (_, idl: Idl) -> idl.Unions |> List.map (fun u -> u, TransparentUnion.tag idl.Harden u))
                (Codec.list union)
            |> Codec.field "enums" (fun (_, idl: Idl) -> idl.Enums) (Codec.list enumDecl)
            |> Codec.field "records" (fun (_, idl: Idl) -> idl.Records) (Codec.list record)
            |> Codec.field "defaults" (fun (_, idl: Idl) -> idl.Defaults) (Codec.list fieldDefault)
            |> Codec.field "nodeFields" (fun (_, idl: Idl) -> idl.NodeFields) fields
            // The op vocabulary (Phase 703) — the wire's second root.
            |> omitting Omit.ops (fun (_, idl: Idl) -> idl.Ops) (Codec.list kind)
            |> omitting Omit.wire (fun (_, idl: Idl) -> idl.Wire) wireShape
            // The declared hardening vocabulary, written ALWAYS since Phase 179 and read as
            // `Undeclared` when absent since Phase 180 — the D40 two-step: the block's absence
            // used to MEAN one domain's tokens, so the writer had to declare every policy outright
            // before the reader could stop inferring one. `IdlArtifactTests`' reader-flip family
            // is the guard; read D40 before changing it.
            |> lenient "harden" HardenPolicy.Undeclared (fun (_, idl: Idl) -> idl.Harden) harden
            |> Codec.build
            |> declared "idl.json"

        // The encoding version is checked FIRST and refused alone when it is not this engine's: an
        // artifact written by a newer encoder may spell members this reader does not know, and
        // reporting each of those as undeclared would bury the one refusal that explains them.
        let readAll (root: JVal) =
            match root with
            | JObj _ ->
                match Decoder.tryMember "version" root with
                | Some v ->
                    match encodingVersion.ReadAll v with
                    | Error es -> Error(es |> List.map (DecodeError.under (PathSegment.Key "version")))
                    | Ok() -> c.ReadAll root
                | None -> c.ReadAll root
            | _ -> c.ReadAll root

        { c with
            Write = fun (identity, idl) -> c.Write(identity, canonicalise idl)
            ReadAll = readAll }

    /// Phase 292 — a read vocabulary is VALIDATED before anyone receives it: `idl.json` is untrusted
    /// input (DECISIONS D95). Every fault is named, each a `SchemaFault` at its owner's path
    /// (`Declare.errors`, Phase 520).
    let private validated (idl: Idl) : Result<Idl, DecodeError list> =
        match Declare.errors idl with
        | [] -> Ok idl
        | faults ->
            faults
            |> List.map (fun f ->
                DecodeError.make DecodeCode.SchemaFault "a well-formed vocabulary" f.Detail
                |> DecodeError.within f.At)
            |> Error

    /// The artifact's codec under a declared [[Identity]] (Phase 527): `Write` the vocabulary as
    /// `idl.json`'s value with `identity`'s members, `ReadAll` the vocabulary from it — every
    /// structural defect at its path, then every [[Declare.errors]] fault at its owner's — and
    /// `Schema` the JSON Schema of the document. The identity a document carries is read by
    /// [[identityOfJson]].
    let codecWith (identity: Identity) : Codec<Idl> =
        { Write = fun idl -> document.Write(identity, idl)
          ReadAll = fun root -> document.ReadAll root |> Result.bind (snd >> validated)
          Schema = document.Schema }

    /// The artifact's codec (Phase 527) — [[codecWith]] [[unnamed]]: `idl.json` written, read and
    /// described by one declaration.
    let codec: Codec<Idl> = codecWith unnamed

    /// The JSON Schema (2020-12) of `idl.json` (Phase 527) — the codec's schema as a standalone
    /// document, published beside the artifact. Descriptive: [[codec]]'s reader, not the schema, is
    /// the authority on what reads.
    let schema: JVal = Codec.schemaDocument document

    /// Every declaration the codec writes is exhaustive over its type — each union and enum names
    /// every case — so a write cannot be refused; were one ever not, it is a defect in this file
    /// and is said so rather than written as a partial document.
    let private written (r: Result<JVal, CodecDeclarationFault>) : JVal =
        match r with
        | Ok j -> j
        | Error f ->
            invalidOp (
                "Artifact: the codec refused to write a vocabulary: "
                + CodecDeclarationFault.describe f
            )

    /// The whole IDL as a `JVal`, under a declared [[Identity]] (Phase 252).
    let jsonWith (identity: Identity) (idl: Idl) : JVal =
        written ((codecWith identity).Write idl)

    /// The whole IDL as a `JVal`, declaring no identity.
    let json (idl: Idl) : JVal = jsonWith unnamed idl

    /// The same indented, canonically-ordered layout [[render]] uses, over an arbitrary `JVal`.
    /// Exposed so a SIBLING document of the vocabulary — the declared-support record beside it, the
    /// schema — lays out identically without a second stringifier appearing in any consumer.
    let renderJson (v: JVal) : string = indent 0 v + "\n"

    /// The `idl.json` bytes — indented, canonically ordered, newline-terminated — declaring no
    /// identity (Phase 527: no `description` either; D40 amended).
    let render (idl: Idl) : string = renderJson (json idl)

    /// [[render]] under a declared [[Identity]] (Phase 252). With the identity [[identityOf]]
    /// reads off an artifact, an authored `idl.json` re-renders to itself.
    let renderWith (identity: Identity) (idl: Idl) : string = renderJson (jsonWith identity idl)

    /// Field lists as `idl.json` writes them — internal since Phase 403: the support document
    /// renders a projected record's declared fields with it, so a field reads the same in both
    /// documents of one triple.
    let internal fieldsJson (fs: IdlField list) : JVal = written ((Codec.list field).Write fs)

    /// One field object as `idl.json` reads it — internal since Phase 403, the support document's
    /// reader of a projected record's declared fields.
    let internal readField: Decoder<IdlField> = Codec.decoder field

    /// Read a vocabulary from the artifact's parsed root — [[codec]]'s reader: every structural
    /// defect at its path (the encoding version first, alone), then every `SchemaFault`
    /// [[Declare.errors]] names at its owner's path (Phase 292).
    let ofJsonDetailed (root: JVal) : Result<Idl, DecodeError list> = codec.ReadAll root

    /// Every refusal, one per line in `DecodeError.render`'s form (code, path, sentence).
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

    /// Read a vocabulary from `idl.json` bytes — the inverse of [[render]], up to the ordering
    /// [[canonicalise]] states.
    let parse (text: string) : Result<Idl, string> =
        parseDetailed text |> Result.mapError describeAll

    /// The [[Identity]] an artifact declares (Phase 252) — read by the same codec as the
    /// vocabulary, so it answers for a document that reads. The other half of [[parse]]:
    /// `renderWith (identityOfJson root) (ofJson root)` reproduces the artifact.
    let identityOfJson (root: JVal) : Result<Identity, DecodeError list> = document.ReadAll root |> Result.map fst

    /// [[identityOfJson]] over `idl.json` bytes; a parse failure is refused at the root.
    let identityOf (text: string) : Result<Identity, DecodeError list> =
        Decoder.parse ReadOptions.strict text
        |> Result.mapError List.singleton
        |> Result.bind identityOfJson
