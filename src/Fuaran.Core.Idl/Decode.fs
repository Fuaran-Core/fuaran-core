namespace Fuaran.Core.Idl

open Fuaran.Core

/// The symmetric decode leg — the IDL also drives JSON → `IdlValue`, so the codec
/// round-trips (`encode (decode wire) = wire`). Parsing is the shared portable
/// `Fuaran.Core.Json.parse`; the IDL drives the walk. Decoders are key-order and
/// extra-key tolerant by contract (only declared fields are read), so this is the
/// floor the Phase 319 unknown-kind tolerance builds on.
module Decode =

    // Phase 310 — the walk reports a `DecodeError`: a code, the path through the document the
    // vocabulary drove it down, and the sentence this module has always returned. The string forms
    // at the foot (`value`, `decode`, `decodeOp`) answer that sentence, byte for byte; the
    // `…Detailed` forms answer the whole refusal.

    let private err (code: DecodeCode) (expected: string) (message: string) : Result<'T, DecodeError> =
        Error(DecodeError.make code expected message)

    let private under (step: PathSegment) (r: Result<'T, DecodeError>) : Result<'T, DecodeError> =
        r |> Result.mapError (DecodeError.under step)

    let private field (name: string) (fields: (string * JVal) list) : JVal option = Decoder.tryMember name (JObj fields)

    /// The tag under the DECLARED discriminator key (Phase 108) — `"$type"` on a
    /// default-shape vocabulary, so the error text is byte-identical there. An absent tag is
    /// `MissingField` and a non-string one `WrongKind`, both at the discriminator.
    let private tagUnder (key: string) (fields: (string * JVal) list) : Result<string, DecodeError> =
        match field key fields with
        | Some(JStr t) -> Ok t
        | Some _ ->
            Error(
                DecodeError.under
                    (PathSegment.Key key)
                    (DecodeError.make DecodeCode.WrongKind "string" ("missing or non-string " + key))
            )
        | None ->
            Error(
                { Decoder.missing key with
                    Message = "missing or non-string " + key }
            )

    let private dollarType (idl: Idl) (fields: (string * JVal) list) = tagUnder idl.Wire.Discriminator fields

    /// An unknown case under the discriminator: `UnknownTag`, at the discriminator, naming the known cases.
    let private unknownTag (idl: Idl) (known: string list) (message: string) : Result<'T, DecodeError> =
        Error(
            DecodeError.under
                (PathSegment.Key idl.Wire.Discriminator)
                (DecodeError.make
                    DecodeCode.UnknownTag
                    ("one of " + (known |> List.map (fun k -> "'" + k + "'") |> String.concat ", "))
                    message)
        )

    /// The arity refusal both union arms share (Phase 292 — the bare arm used to zip the
    /// two lists unchecked and throw where the object arm refused).
    let private arity (u: UnionForm) =
        err
            DecodeCode.SchemaFault
            (sprintf "%d type args for union '%s'" (List.length u.Decl.Params) u.Name)
            (sprintf "union '%s' given %d type args, expects %d" u.Name (List.length u.Args) (List.length u.Decl.Params))

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
        | WireForm.Enum e, JStr s ->
            err
                DecodeCode.UnknownTag
                ("one of "
                 + (e.WireCases |> List.map (fun c -> "'" + c + "'") |> String.concat ", "))
                (sprintf "enum '%s' has no case '%s'" e.Name s)
        | WireForm.Undeclared(Undeclared.Enum name), JStr _ ->
            err DecodeCode.SchemaFault ("a declared enum '" + name + "'") (sprintf "unknown enum '%s'" name)
        | WireForm.Undeclared(Undeclared.Union(name, _)), _ ->
            err DecodeCode.SchemaFault ("a declared union '" + name + "'") (sprintf "unknown union '%s'" name)
        | WireForm.Union u, JObj fs ->
            match u.Binding with
            | None -> arity u
            | Some binding ->
                dollarType idl fs
                |> Result.bind (fun tag ->
                    match u.Decl.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                    | None ->
                        unknownTag
                            idl
                            (u.Decl.Cases |> List.map _.Tag)
                            (sprintf "union '%s' has no case '%s'" u.Name tag)
                    | Some c ->
                        decodeFields idl (WireForm.caseFields binding c) fs
                        |> Result.map (fun fields -> VUnion(tag, fields)))
        // A transparent union decoded from a BARE (non-object) wire value — the
        // Fuaran-UI 0.2.0 bare-string `TextSource.Literal` (`"x"` → `Literal{text="x"}`).
        | WireForm.Union u, j ->
            match u.Transparent with
            | None -> err DecodeCode.WrongKind "object" (sprintf "union '%s' expects an object" u.Name)
            | Some ttag ->
                match u.Binding, u.Decl.Cases |> List.tryFind (fun c -> c.Tag = ttag) with
                | None, _ -> arity u
                | Some _, None ->
                    err
                        DecodeCode.SchemaFault
                        ("a declared transparent case '" + ttag + "'")
                        (sprintf "union '%s' has no transparent case '%s'" u.Name ttag)
                | Some binding, Some c ->
                    match WireForm.caseFields binding c with
                    | [ single ] ->
                        decodeValue idl single.Type j
                        |> Result.map (fun v -> VUnion(ttag, [ single.Name, v ]))
                    | _ ->
                        err
                            DecodeCode.SchemaFault
                            "a transparent case of exactly one field"
                            (sprintf "transparent union case '%s' must have exactly one field" ttag)
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
                        (sprintf "hosted value is not a '%s' string" (HostedFormat.name fmt)))
        | WireForm.Record r, JObj fs -> decodeFields idl r.Fields fs |> Result.map VRecord
        | WireForm.Undeclared(Undeclared.Record name), JObj _ ->
            err DecodeCode.SchemaFault ("a declared record '" + name + "'") (sprintf "unknown record '%s'" name)
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
        | WireForm.Kind, JObj fs ->
            dollarType idl fs
            |> Result.bind (fun tag ->
                match IdlLookup.tryKind idl tag with
                | None -> unknownTag idl (idl.Kinds |> List.map _.Tag) (sprintf "unknown kind '%s'" tag)
                | Some k -> decodeFields idl k.Fields fs |> Result.map (fun flds -> VUnion(tag, flds)))
        | WireForm.Op, JObj fs ->
            dollarType idl fs
            |> Result.bind (fun tag ->
                match idl.Ops |> List.tryFind (fun o -> o.Tag = tag) with
                | None -> unknownTag idl (idl.Ops |> List.map _.Tag) (sprintf "unknown op '%s'" tag)
                | Some o -> decodeFields idl o.Fields fs |> Result.map (fun flds -> VUnion(tag, flds)))
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
            let code =
                match form, j with
                | WireForm.Sentinel _, JStr _ -> DecodeCode.OutOfRange
                | _ -> DecodeCode.WrongKind

            err code (WireForm.jsonKind form) (sprintf "wire value does not match IDL type %A" t)

    and private decodeFields
        (idl: Idl)
        (fields: IdlField list)
        (jfields: (string * JVal) list)
        : Result<(string * IdlValue) list, DecodeError> =
        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | (f: IdlField) :: rest ->
                match field f.Name jfields, f.Opt with
                | _, HostOnly -> go acc rest
                | None, Optional -> go acc rest
                // omit-at-default: an absent field restores its identity default
                | None, OmitDefault d -> go ((f.Name, d) :: acc) rest
                | None, Required ->
                    Error(
                        { Decoder.missing f.Name with
                            Message = sprintf "required field '%s' is absent" f.Name }
                    )
                | Some j, _ ->
                    match decodeValue idl f.Type j |> under (PathSegment.Key f.Name) with
                    | Ok v -> go ((f.Name, v) :: acc) rest
                    | Error e -> Error e

        go [] fields

    /// The node envelope's own refusal: the member at fault is named by the path, the sentence is
    /// the one this module has always returned for the whole envelope.
    and private envelopeFault
        (message: string)
        (fs: (string * JVal) list)
        (members: (string * (JVal -> bool) * string) list)
        : DecodeError =
        members
        |> List.tryPick (fun (name, ok, kind) ->
            match field name fs with
            | None ->
                Some(
                    { Decoder.missing name with
                        Message = message }
                )
            | Some v when not (ok v) ->
                Some(DecodeError.under (PathSegment.Key name) (DecodeError.make DecodeCode.WrongKind kind message))
            | Some _ -> None)
        |> Option.defaultValue (DecodeError.make DecodeCode.WrongKind "a node object" message)

    and private decodeNode (idl: Idl) (j: JVal) : Result<IdlValue, DecodeError> =
        let isStr =
            function
            | JStr _ -> true
            | _ -> false

        let isObj =
            function
            | JObj _ -> true
            | _ -> false

        match j, idl.Wire.NodeEnvelope with
        | JObj fs, NodeEnvelopeShape.NestedKind ->
            match field "id" fs, field "kind" fs with
            | Some(JStr id), Some(JObj kindFs) ->
                dollarType idl kindFs
                |> under (PathSegment.Key "kind")
                |> Result.bind (fun kindTag ->
                    match IdlLookup.tryKind idl kindTag with
                    | None ->
                        unknownTag idl (idl.Kinds |> List.map _.Tag) (sprintf "unknown kind '%s'" kindTag)
                        |> under (PathSegment.Key "kind")
                    | Some k ->
                        decodeFields idl k.Fields kindFs
                        |> under (PathSegment.Key "kind")
                        |> Result.bind (fun fields ->
                            // Phase 698 — the envelope decodes through the same
                            // `decodeFields` the kind fields do, so it is the encoder's
                            // inverse by construction. An empty result (nothing on the
                            // wire, and no `OmitDefault` to restore) yields the bare
                            // `VNode` this returned before the envelope existed, which is
                            // why every pre-existing decode round-trip is byte-unchanged.
                            decodeFields idl idl.NodeFields fs
                            |> Result.map (function
                                | [] -> VNode(id, kindTag, fields)
                                | envelope -> VNodeEnv(id, envelope, kindTag, fields))))
            | _ ->
                Error(
                    envelopeFault
                        "node must have a string 'id' and an object 'kind'"
                        fs
                        [ "id", isStr, "string"; "kind", isObj, "object" ]
                )
        // Phase 109 — the FLAT envelope: the tag, the id, the kind's fields (and
        // any declared node envelope) share this one object. `decodeFields` reads
        // only declared names, so the discriminator and the id are tolerated as
        // the extra keys they are.
        | JObj fs, NodeEnvelopeShape.FlatKind ->
            match field "id" fs, dollarType idl fs with
            | Some(JStr id), Ok kindTag ->
                match IdlLookup.tryKind idl kindTag with
                | None -> unknownTag idl (idl.Kinds |> List.map _.Tag) (sprintf "unknown kind '%s'" kindTag)
                | Some k ->
                    decodeFields idl k.Fields fs
                    |> Result.bind (fun fields ->
                        decodeFields idl idl.NodeFields fs
                        |> Result.map (function
                            | [] -> VNode(id, kindTag, fields)
                            | envelope -> VNodeEnv(id, envelope, kindTag, fields)))
            | _ ->
                Error(
                    envelopeFault
                        (sprintf "node must have a string 'id' and a string '%s' discriminator" idl.Wire.Discriminator)
                        fs
                        [ "id", isStr, "string"; idl.Wire.Discriminator, isStr, "string" ]
                )
        | _, _ -> Error(DecodeError.make DecodeCode.WrongKind "object" "node must be an object")

    /// The parser's refusal under this module's own prefix (`parse failed: …`).
    let private parsed (json: string) : Result<JVal, DecodeError> =
        Decoder.parse ReadOptions.strict json
        |> Result.mapError (DecodeError.reword (fun m -> "parse failed: " + m))

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
