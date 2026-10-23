namespace Fuaran.Core.Idl

open Fuaran.Core

/// The symmetric decode leg — the IDL also drives JSON → `IdlValue`, so the codec
/// round-trips (`encode (decode wire) = wire`). Parsing is the shared portable
/// `Fuaran.Core.Json.parse`; the IDL drives the walk. Decoders are key-order and
/// extra-key tolerant by contract (only declared fields are read), so this is the
/// floor the Phase 319 unknown-kind tolerance builds on.
module Decode =

    // Phase 310 — the walk reports a `DecodeError`: a code, the path through the document the
    // vocabulary drove it down, and what the position expected. Since Phase 520 every refusal is
    // `Decoder`'s own or built by `DecodeError.make`, so its sentence is the projection of its code
    // (`DecodeError.sentence`) and nothing here re-spells one: a discriminator is read by
    // `Decoder.tagDispatch`, a member by `Decoder.field`. The string forms at the foot (`value`,
    // `decode`, `decodeOp`) answer that sentence; the `…Detailed` forms answer the whole refusal.

    let private err (code: DecodeCode) (expected: string) (detail: string) : Result<'T, DecodeError> =
        Error(DecodeError.make code expected detail)

    let private under (step: PathSegment) (r: Result<'T, DecodeError>) : Result<'T, DecodeError> =
        r |> Result.mapError (DecodeError.under step)

    /// The arity refusal both union arms share (Phase 292 — the bare arm used to zip the
    /// two lists unchecked and throw where the object arm refused).
    let private arity (u: UnionForm) =
        err
            DecodeCode.SchemaFault
            (sprintf "%d type args for union '%s'" (List.length u.Decl.Params) u.Name)
            (sprintf "%d type args for union '%s'" (List.length u.Args) u.Name)

    /// Phase 474 — the member a required presence-keyed union is refused `MissingField` at when no
    /// case's key is present: its first case's key.
    let private keyedFirst (keyed: KeyedCase list) : string =
        match keyed with
        | k :: _ -> k.Key
        | [] -> ""

    /// Phase 474 — what a presence-keyed union's both-present refusal says its position admits.
    let private keyedExpected (keyed: KeyedCase list) : string =
        "at most one of "
        + (keyed |> List.map (fun k -> "'" + k.Key + "'") |> String.concat ", ")

    let rec private decodeValue (idl: Idl) (t: IdlType) (j: JVal) : Result<IdlValue, DecodeError> =
        // Phase 513 — the type's wire form is decided once, by [[WireForm.ofType]]; this arm list only
        // reads it.
        match WireForm.ofType idl t, j with
        | WireForm.Str, JStr s -> Ok(VStr s)
        | WireForm.Int, JInt i -> Ok(VInt i)
        | WireForm.Bool, JBool b -> Ok(VBool b)
        | WireForm.Float, JFloat f -> Ok(VFloat f)
        | WireForm.Float, JInt i -> Ok(VFloat(float i))
        // Phase 303 — WIRE_FORMAT §7: at a FLOAT slot the quoted tokens `"NaN"`, `"Infinity"` and
        // `"-Infinity"` are the spelling of a non-finite value, and the encoder writes exactly them
        // (`Canon` renders a non-finite `JFloat` as the quoted token). The generated F# `dFloat`, the
        // generated TypeScript decoder and the emitted JSON schema already read them back; the
        // reference interpreter was the one host refusing its own output. Only these three strings,
        // and only at a float slot — §7 stops there, so an `int` slot and a verbatim `json` slot are
        // untouched (the encoder refuses a non-finite float inside a verbatim value, Phase 292).
        | WireForm.Float, JStr(FloatToken.NonFinite f) -> Ok(VFloat f)
        | WireForm.Enum e, JStr s when List.contains s e.WireCases -> Ok(VEnum s)
        // Phase 539 — a declared alias decodes to its case's canonical spelling.
        | WireForm.Enum e, JStr s when (e.CaseAccepting s).IsSome -> Ok(VEnum(e.WireOf(Option.get (e.CaseAccepting s))))
        | WireForm.Enum e, JStr s ->
            err
                DecodeCode.UnknownTag
                ("one of "
                 + (e.WireCases |> List.map (fun c -> "'" + c + "'") |> String.concat ", "))
                (sprintf "case '%s' of enum '%s'" s e.Name)
        | WireForm.Undeclared(Undeclared.Enum name), JStr _ ->
            err DecodeCode.SchemaFault ("a declared enum '" + name + "'") (sprintf "enum '%s'" name)
        | WireForm.Undeclared(Undeclared.Union(name, _)), _ ->
            err DecodeCode.SchemaFault ("a declared union '" + name + "'") (sprintf "union '%s'" name)
        | WireForm.Union { Decl = { Discrimination = UnionDiscrimination.ByPresence keyed } as u }, JObj fs ->
            // Phase 474 — read on its own, a presence-keyed union's members are this object's.
            decodeKeyed idl u keyed fs
            |> Result.bind (function
                | Some v -> Ok v
                | None -> Error(Decoder.missing (keyedFirst keyed)))
        | WireForm.Union u, JObj _ ->
            match u.Binding with
            | None -> arity u
            | Some binding ->
                tagged
                    idl
                    (u.Decl.Cases
                     |> List.map (fun c -> u.Decl.WireTagOf c.Tag, WireForm.caseFields binding c))
                    j
        // A transparent union decoded from a BARE (non-object) wire value — the
        // Fuaran-UI 0.2.0 bare-string `TextSource.Literal` (`"x"` → `Literal{text="x"}`).
        | WireForm.Union u, j ->
            match u.Transparent with
            | None -> Error(Decoder.wrongKind "object" j)
            | Some ttag ->
                match u.Binding, u.Decl.Cases |> List.tryFind (fun c -> c.Tag = ttag) with
                | None, _ -> arity u
                | Some _, None ->
                    err
                        DecodeCode.SchemaFault
                        ("a declared transparent case '" + ttag + "'")
                        (sprintf "union '%s' with no case '%s'" u.Name ttag)
                | Some binding, Some c ->
                    match WireForm.caseFields binding c with
                    | [ single ] ->
                        decodeValue idl single.Type j
                        |> Result.map (fun v -> VUnion(u.Decl.WireTagOf ttag, [ single.Name, v ]))
                    | fields ->
                        err
                            DecodeCode.SchemaFault
                            "a transparent case of exactly one field"
                            (sprintf "transparent case '%s' of %d fields" ttag (List.length fields))
        | WireForm.Unbound v, _ ->
            err DecodeCode.SchemaFault "a substituted type" (sprintf "unsubstituted type variable '%s'" v)
        | WireForm.Sentinel(value, wire), JStr s when s = wire -> Ok value
        // Phase 676 — accept any JSON at this position, verbatim and unvalidated.
        // A shape check here would be wrong by definition: the field's whole
        // contract is that its content is not the schema's business. A hosted slot that declares
        // no wire form decodes the same way — only the generated F# runs the real host codec (see
        // [[HostedCodec]]).
        | WireForm.Verbatim, j
        | WireForm.Hosted(None, _), j -> Ok(VJson j)
        // Since Phase 252 a hosted slot that declares its wire form is checked against it first,
        // so the interpreter refuses what the host codec and the TypeScript host refuse.
        | WireForm.Hosted(Some w, format), j ->
            decodeValue idl w j
            |> Result.bind (fun _ ->
                match format, j with
                | None, _ -> Ok(VJson j)
                | Some fmt, JStr s when HostedFormat.admits fmt s -> Ok(VJson j)
                | Some fmt, _ ->
                    err
                        DecodeCode.OutOfRange
                        ("a '" + HostedFormat.name fmt + "' string")
                        (Canonical.write Rendering.CrossHost j))
        | WireForm.Record r, JObj fs -> decodeFields idl r.Fields fs |> Result.map VRecord
        | WireForm.Undeclared(Undeclared.Record name), JObj _ ->
            err DecodeCode.SchemaFault ("a declared record '" + name + "'") (sprintf "record '%s'" name)
        | WireForm.Map vt, JObj fs ->
            let rec go acc =
                function
                | [] -> Ok(VMap(List.rev acc))
                | (k, jv) :: rest ->
                    match decodeValue idl vt jv |> under (PathSegment.Key k) with
                    | Ok v -> go ((k, v) :: acc) rest
                    | Error e -> Error e

            go [] fs
        | WireForm.Node, JObj _ -> decodeNode idl j
        | WireForm.Kind, JObj _ -> tagged idl (idl.Kinds |> List.map (fun k -> k.Tag, k.Fields)) j
        | WireForm.Op, JObj _ -> tagged idl (idl.Ops |> List.map (fun o -> o.Tag, o.Fields)) j
        | WireForm.List inner, JArr xs ->
            let rec go i acc =
                function
                | [] -> Ok(VList(List.rev acc))
                | x :: rest ->
                    match decodeValue idl inner x |> under (PathSegment.Index i) with
                    | Ok v -> go (i + 1) (v :: acc) rest
                    | Error e -> Error e

            go 0 [] xs
        | form, _ ->
            // A sentinel position given a string that is not its sentinel holds the right KIND and
            // the wrong value; every other fall-through is a kind the type does not take.
            match form, j with
            | WireForm.Sentinel _, JStr s -> err DecodeCode.OutOfRange (WireForm.jsonKind form) ("'" + s + "'")
            | _ -> Error(Decoder.wrongKind (WireForm.jsonKind form) j)

    /// A tagged object read by its discriminator (Phase 520: `Decoder.tagDispatch` under the DECLARED
    /// key, Phase 108): each case reads the SAME object's declared fields, and its value is the tag
    /// with them. An absent discriminator is `MissingField` there, a non-string one `WrongKind`, and
    /// an unknown tag `UnknownTag` at it, naming the known cases.
    and private tagged (idl: Idl) (cases: (string * IdlField list) list) : Decoder<IdlValue> =
        Decoder.tagDispatch
            idl.Wire.Discriminator
            (cases
             |> List.map (fun (tag, fields) ->
                 tag,
                 fun j ->
                     match j with
                     | JObj fs -> decodeFields idl fields fs |> Result.map (fun flds -> VUnion(tag, flds))
                     | other -> Error(Decoder.wrongKind "object" other)))

    /// Phase 474 — a presence-keyed union read from an object's members ([[WireForm.pickKeyed]]):
    /// `None` when no case's key is present; the both-present refusal `OutOfRange` at the second
    /// key left; otherwise the picked case's members, then each member it synthesises, which must be
    /// present (`MissingField`) and canonically equal to its declared value (`OutOfRange` at it).
    and private decodeKeyed
        (idl: Idl)
        (u: IdlUnion)
        (keyed: KeyedCase list)
        (fs: (string * JVal) list)
        : Result<IdlValue option, DecodeError> =
        match WireForm.pickKeyed u keyed fs with
        | KeyedPick.NoCase -> Ok None
        | KeyedPick.BothPresent(first, second) ->
            err DecodeCode.OutOfRange (keyedExpected keyed) (sprintf "'%s' beside '%s'" second first)
            |> under (PathSegment.Key second)
        | KeyedPick.Picked(c, k) ->
            decodeFields idl c.Fields fs
            |> Result.bind (fun members ->
                let rec check =
                    function
                    | [] -> Ok(Some(VUnion(c.Tag, members)))
                    | (name: string, v: IdlValue) :: rest ->
                        let declared =
                            u.Cases
                            |> List.tryPick (fun o -> o.Fields |> List.tryFind (fun f -> f.Name = name))
                            |> Option.map (fun f -> Encode.valueJson idl f.Type v)

                        match fs |> List.tryFind (fun (n, _) -> n = name), declared with
                        | None, _ -> Error(Decoder.missing name)
                        | Some(_, j), Some(Ok expected) when
                            Canonical.write Rendering.CrossHost j = Canonical.write Rendering.CrossHost expected
                            ->
                            check rest
                        | Some(_, j), Some(Ok expected) ->
                            err
                                DecodeCode.OutOfRange
                                (Canonical.write Rendering.CrossHost expected)
                                (Canonical.write Rendering.CrossHost j)
                            |> under (PathSegment.Key name)
                        | Some _, _ ->
                            err
                                DecodeCode.SchemaFault
                                "a synthesised member a case declares"
                                (sprintf "member '%s'" name)

                check k.Synthesised)

    and private decodeFields
        (idl: Idl)
        (fields: IdlField list)
        (jfields: (string * JVal) list)
        : Result<(string * IdlValue) list, DecodeError> =
        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            // Phase 474 — an inline record's members and a presence-keyed union's are this object's
            // own, read from it where the field is declared; a refusal inside one is at its member.
            | (f: IdlField) :: rest when f.Opt <> HostOnly && WireForm.isInline idl f.Type ->
                let read =
                    match WireForm.inlineRecord idl f.Type, WireForm.keyedUnion idl f.Type with
                    | Some r, _ -> decodeFields idl r.Fields jfields |> Result.map (VRecord >> Some)
                    | _, Some(u, keyed) -> decodeKeyed idl u keyed jfields
                    | None, None -> Ok None

                match read, f.Opt with
                | Error e, _ -> Error e
                | Ok(Some v), _ -> go ((f.Name, v) :: acc) rest
                | Ok None, Required ->
                    match WireForm.keyedUnion idl f.Type with
                    | Some(_, keyed) -> Error(Decoder.missing (keyedFirst keyed))
                    | None -> Error(Decoder.missing f.Name)
                | Ok None, _ -> go acc rest
            | (f: IdlField) :: rest ->
                // Phase 539 — the member is read under its spellings and dropped when its presence
                // condition fails ([[WireForm.readMember]]), so it reaches the optionality rules below
                // as the canonical form has it; a refusal inside it is at its CANONICAL spelling, the
                // generated decoders' path too.
                match WireForm.readMember idl fields f jfields, f.Opt with
                | _, HostOnly -> go acc rest
                | None, Optional -> go acc rest
                // omit-at-default: an absent field restores its identity default
                | None, OmitDefault d -> go ((f.Name, d) :: acc) rest
                | None, Required -> Error(Decoder.missing f.Name)
                | Some j, _ ->
                    match decodeValue idl f.Type j |> under (PathSegment.Key f.Name) with
                    | Ok v -> go ((f.Name, v) :: acc) rest
                    | Error e -> Error e

        go [] fields

    /// A node: its `id`, then its kind, then the declared node envelope. Every refusal is `Decoder`'s
    /// (Phase 520): an absent `id` or kind member is `MissingField` at it and one of the wrong kind
    /// `WrongKind` there, in that order; an unknown kind is `UnknownTag` at the discriminator.
    and private decodeNode (idl: Idl) (j: JVal) : Result<IdlValue, DecodeError> =
        // Phase 698 — the envelope decodes through the same `decodeFields` the kind fields do, so it
        // is the encoder's inverse by construction. An empty result (nothing on the wire, and no
        // `OmitDefault` to restore) yields the bare `VNode` this returned before the envelope
        // existed, which is why every pre-existing decode round-trip is byte-unchanged.
        let withEnvelope (id: string) (fs: (string * JVal) list) (kind: IdlValue) =
            match kind with
            | VUnion(kindTag, fields) ->
                decodeFields idl idl.NodeFields fs
                |> Result.map (function
                    | [] -> VNode(id, kindTag, fields)
                    | envelope -> VNodeEnv(id, envelope, kindTag, fields))
            | other -> Ok other

        let kinds = idl.Kinds |> List.map (fun k -> k.Tag, k.Fields)

        match j, idl.Wire.NodeEnvelope with
        | JObj fs, NodeEnvelopeShape.NestedKind ->
            Decoder.field "id" Decoder.str j
            |> Result.bind (fun id ->
                Decoder.field "kind" Decoder.obj j
                |> Result.bind (fun _ -> Decoder.field "kind" (tagged idl kinds) j)
                |> Result.bind (withEnvelope id fs))
        // Phase 109 — the FLAT envelope: the tag, the id, the kind's fields (and
        // any declared node envelope) share this one object. `decodeFields` reads
        // only declared names, so the discriminator and the id are tolerated as
        // the extra keys they are.
        | JObj fs, NodeEnvelopeShape.FlatKind ->
            Decoder.field "id" Decoder.str j
            |> Result.bind (fun id -> tagged idl kinds j |> Result.bind (withEnvelope id fs))
        | _, _ -> Error(Decoder.wrongKind "object" j)

    let private parsed (json: string) : Result<JVal, DecodeError> = Decoder.parse ReadOptions.strict json

    // -----------------------------------------------------------------------
    // Phase 550 (DECISIONS.md D198) — the STRICT-MEMBERS reading. A members check over the document
    // BEFORE the ordinary walk, as the seams' strict readers run theirs: every object the vocabulary
    // declares may carry only the members [[WireForm.roster]] derives for it. It walks the value's
    // declared shape and checks each object's members first, then its fields in declared order — so
    // the first undeclared member it meets is the refusal, `UndeclaredMember` at that member. A shape
    // fault is left to the walk that follows: the check only ever ADDS the undeclared-member refusal,
    // so a document both readings accept decodes to one value. A verbatim, hosted or unbound slot is
    // its reader's to police, and is not entered.
    // -----------------------------------------------------------------------

    let rec private strictValue (idl: Idl) (t: IdlType) (j: JVal) : Result<unit, DecodeError> =
        let disc = idl.Wire.Discriminator

        match WireForm.ofType idl t, j with
        | WireForm.Record r, JObj fs -> strictObject idl [] r.Fields j fs
        | WireForm.Union { Decl = { Discrimination = UnionDiscrimination.ByPresence keyed } as u }, JObj fs ->
            Decoder.members (WireForm.keyedRoster u) j
            |> Result.bind (fun () ->
                match WireForm.pickKeyed u keyed fs with
                | KeyedPick.Picked(c, _) -> strictFields idl c.Fields fs
                | _ -> Ok())
        | WireForm.Union u, JObj fs ->
            match u.Binding, Decoder.tryMember disc j with
            | Some binding, Some(JStr tag) ->
                match u.Decl.CaseByWireTag tag with
                | Some c -> strictObject idl [ disc ] (WireForm.caseFields binding c) j fs
                | None -> Ok()
            | _ -> Ok()
        | WireForm.Union u, bare ->
            match
                u.Binding,
                u.Transparent
                |> Option.bind (fun t -> u.Decl.Cases |> List.tryFind (fun c -> c.Tag = t))
            with
            | Some binding, Some c ->
                match WireForm.caseFields binding c with
                | [ single ] -> strictValue idl single.Type bare
                | _ -> Ok()
            | _ -> Ok()
        | WireForm.Kind, JObj fs -> strictTagged idl (idl.Kinds |> List.map (fun k -> k.Tag, k.Fields)) [] j fs
        | WireForm.Op, JObj fs -> strictTagged idl (idl.Ops |> List.map (fun o -> o.Tag, o.Fields)) [] j fs
        | WireForm.Node, JObj fs ->
            let kinds = idl.Kinds |> List.map (fun k -> k.Tag, k.Fields)

            match idl.Wire.NodeEnvelope with
            | NodeEnvelopeShape.NestedKind ->
                strictObject idl [ "id"; "kind" ] idl.NodeFields j fs
                |> Result.bind (fun () ->
                    match Decoder.tryMember "kind" j with
                    | Some(JObj kfs as kj) -> strictTagged idl kinds [] kj kfs |> under (PathSegment.Key "kind")
                    | _ -> Ok())
            // The flat object holds the tag, the id, the kind's fields and the envelope's at once.
            | NodeEnvelopeShape.FlatKind -> strictTagged idl kinds ("id" :: WireForm.roster idl [] idl.NodeFields) j fs
        | WireForm.List inner, JArr xs ->
            xs
            |> List.indexed
            |> List.fold
                (fun acc (i, x) ->
                    acc
                    |> Result.bind (fun () -> strictValue idl inner x |> under (PathSegment.Index i)))
                (Ok())
        | WireForm.Map vt, JObj fs ->
            fs
            |> List.fold
                (fun acc (k, v) -> acc |> Result.bind (fun () -> strictValue idl vt v |> under (PathSegment.Key k)))
                (Ok())
        | _ -> Ok()

    /// A tagged object's members, against the case its discriminator names; an unknown tag is the
    /// walk's to refuse.
    and private strictTagged
        (idl: Idl)
        (cases: (string * IdlField list) list)
        (extra: string list)
        (j: JVal)
        (fs: (string * JVal) list)
        : Result<unit, DecodeError> =
        match Decoder.tryMember idl.Wire.Discriminator j with
        | Some(JStr tag) ->
            match cases |> List.tryFind (fun (t, _) -> t = tag) with
            | Some(_, fields) -> strictObject idl (idl.Wire.Discriminator :: extra) fields j fs
            | None -> Ok()
        | _ -> Ok()

    /// One object: its members against the roster, then each meaningful field's value, in declared
    /// order.
    and private strictObject
        (idl: Idl)
        (extra: string list)
        (fields: IdlField list)
        (j: JVal)
        (fs: (string * JVal) list)
        : Result<unit, DecodeError> =
        Decoder.members (WireForm.roster idl extra fields) j
        |> Result.bind (fun () -> strictFields idl fields fs)

    /// Each meaningful field's value, in declared order — an inline declaration's members read from
    /// the same object (Phase 474), a presence-keyed union's from the case its reading picks.
    and private strictFields (idl: Idl) (fields: IdlField list) (fs: (string * JVal) list) : Result<unit, DecodeError> =
        fields
        |> List.filter (fun f -> f.Opt <> HostOnly)
        |> List.fold
            (fun acc f ->
                acc
                |> Result.bind (fun () ->
                    match WireForm.inlineRecord idl f.Type, WireForm.keyedUnion idl f.Type with
                    | Some r, _ -> strictFields idl r.Fields fs
                    | _, Some(u, keyed) ->
                        match WireForm.pickKeyed u keyed fs with
                        | KeyedPick.Picked(c, _) -> strictFields idl c.Fields fs
                        | _ -> Ok()
                    | None, None ->
                        // At the CANONICAL spelling, as every refusal inside a member is (Phase 539).
                        match WireForm.readMember idl fields f fs with
                        | Some v -> strictValue idl f.Type v |> under (PathSegment.Key f.Name)
                        | None -> Ok()))
            (Ok())

    /// [[valueDetailed]] under the STRICT-MEMBERS reading (Phase 550): an object the vocabulary
    /// declares that carries a member its declaration does not name is refused `UndeclaredMember` at
    /// that member, before anything else is read; a document without one decodes exactly as
    /// [[valueDetailed]] decodes it.
    let valueStrictDetailed (idl: Idl) (t: IdlType) (j: JVal) : Result<IdlValue, DecodeError> =
        strictValue idl t j |> Result.bind (fun () -> decodeValue idl t j)

    /// [[decodeDetailed]] under the STRICT-MEMBERS reading (Phase 550) — see [[valueStrictDetailed]].
    let decodeStrictDetailed (idl: Idl) (json: string) : Result<IdlValue, DecodeError> =
        parsed json |> Result.bind (valueStrictDetailed idl TNode)


    /// Decode one parsed JSON value at a declared type, answering a typed refusal (Phase 310): its
    /// code, its path from `j`, what the position expected, and [[value]]'s sentence.
    let valueDetailed (idl: Idl) (t: IdlType) (j: JVal) : Result<IdlValue, DecodeError> = decodeValue idl t j

    /// [[decode]] answering a typed refusal (Phase 310) — the path runs through the vocabulary
    /// from the node's root.
    let decodeDetailed (idl: Idl) (json: string) : Result<IdlValue, DecodeError> =
        parsed json |> Result.bind (decodeNode idl)

    /// [[decodeOp]] answering a typed refusal (Phase 310).
    let decodeOpDetailed (idl: Idl) (json: string) : Result<IdlValue, DecodeError> =
        parsed json |> Result.bind (decodeValue idl TOp)

    /// Decode one parsed JSON value at a declared type (Phase 252) — the per-slot face of
    /// [[decode]], for a caller holding a value rather than a node: a hosted slot's JSON
    /// read through its declared wire form, for instance. The sentence of [[valueDetailed]]'s
    /// refusal.
    let value (idl: Idl) (t: IdlType) (j: JVal) : Result<IdlValue, string> =
        valueDetailed idl t j |> Result.mapError DecodeError.describe

    /// Decode canonical wire JSON to an authored `IdlValue`, driven by the IDL. The sentence of
    /// [[decodeDetailed]]'s refusal.
    let decode (idl: Idl) (json: string) : Result<IdlValue, string> =
        decodeDetailed idl json |> Result.mapError DecodeError.describe

    /// Decode canonical wire JSON as a TREE OP (Phase 703) — the symmetric partner
    /// of [[Encode.encodeOp]], and the wire's second root. The sentence of
    /// [[decodeOpDetailed]]'s refusal.
    let decodeOp (idl: Idl) (json: string) : Result<IdlValue, string> =
        decodeOpDetailed idl json |> Result.mapError DecodeError.describe
