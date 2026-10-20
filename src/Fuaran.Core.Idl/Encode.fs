namespace Fuaran.Core.Idl

open Fuaran.Core

/// The schema-driven encoder: an authored `IdlValue`, validated against the IDL,
/// becomes a canonical `JVal`; `Canonical.write Rendering.CrossHost` then gives the wire bytes.
module Encode =

    /// [[Canonical.typed]] under the DECLARED discriminator key (Phase 108) — the
    /// default key reproduces `Canonical.typed` byte-for-byte.
    let private typedWith (key: string) (tag: string) (fields: (string * JVal) list) : JVal =
        JObj((key, JStr tag) :: fields)

    let private provided (name: string) (fields: (string * IdlValue) list) =
        fields |> List.tryFind (fun (n, _) -> n = name) |> Option.map snd

    /// Phase 292 — a verbatim JSON value (a `json` slot, or a hosted slot carried as one)
    /// holding a NON-FINITE float has no canonical rendering of its own: `Canonical.write Rendering.CrossHost`
    /// spells it as the quoted token, the bytes of the STRING `"NaN"`, and nothing at a
    /// verbatim slot can tell a reader it was a number. A `float` slot is different — its
    /// type says how to read the token back (WIRE_FORMAT §7) — so the refusal is made here,
    /// at the slots where the token aliases, and never at a typed float.
    let private verbatim (j: JVal) : Result<JVal, string> =
        match Json.firstNonFinite j with
        | Some(path, tok) ->
            Error(
                "non-finite float has no canonical rendering of its own inside a verbatim json value: "
                + tok
                + " at "
                + path
            )
        | None -> Ok j

    /// The named rendering the vocabulary's declared key order selects (Phase 520):
    /// `Rendering.CrossHost` (Ordinal-sorted) by default, `Rendering.Declared` under
    /// `KeyOrder.Declared` — where the encoder's own construction order (discriminator, id,
    /// declared fields) is normative.
    let private rendering (idl: Idl) : Rendering =
        match idl.Wire.KeyOrder with
        | KeyOrder.Sorted -> Rendering.CrossHost
        | KeyOrder.Declared -> Rendering.Declared

    /// Whether two encoded values are one canonical value under the vocabulary's declared
    /// key order — the omit-at-default test's notion of equality (Phase 292).
    let private sameCanonical (idl: Idl) (a: JVal) (b: JVal) : bool =
        Canonical.write (rendering idl) a = Canonical.write (rendering idl) b

    let rec private encodeValue (idl: Idl) (t: IdlType) (v: IdlValue) : Result<JVal, string> =
        // Phase 513 — the type's wire form is decided once, by [[WireForm.ofType]]; this arm list only
        // writes it.
        match WireForm.ofType idl t, v with
        | WireForm.Str, VStr s -> Ok(JStr s)
        | WireForm.Int, VInt i -> Ok(JInt i)
        | WireForm.Bool, VBool b -> Ok(JBool b)
        | WireForm.Float, VFloat f -> Ok(JFloat f)
        | WireForm.Float, VInt i -> Ok(JFloat(float i))
        // `VEnum` carries the WIRE string, exactly as `VUnion` carries the wire `$type` tag — so an
        // enum that declares a case↔wire mapping is checked against its wire strings here, and only
        // the F# emitter maps back.
        | WireForm.Enum e, VEnum case when List.contains case e.WireCases -> Ok(JStr case)
        | WireForm.Enum e, VEnum case -> Error(sprintf "enum '%s' has no case '%s'" e.Name case)
        | WireForm.Undeclared(Undeclared.Enum name), VEnum _ -> Error(sprintf "unknown enum '%s'" name)
        | WireForm.Union u, VUnion(tag, fields) ->
            match u.Binding, u.Decl.Cases |> List.tryFind (fun c -> c.Tag = tag) with
            | None, _ ->
                Error(
                    sprintf
                        "union '%s' given %d type args, expects %d"
                        u.Name
                        (List.length u.Args)
                        (List.length u.Decl.Params)
                )
            | Some _, None -> Error(sprintf "union '%s' has no case '%s'" u.Name tag)
            | Some binding, Some c ->
                let caseFields = WireForm.caseFields binding c

                match u.Transparent with
                | Some ttag when ttag = tag ->
                    // Transparent case (the declared one): emit the single field's value bare.
                    match caseFields with
                    | [ single ] ->
                        match provided single.Name fields with
                        | Some v -> encodeValue idl single.Type v
                        | (None | Some VAbsent) ->
                            Error(sprintf "transparent union '%s' case '%s' missing field '%s'" u.Name tag single.Name)
                    | _ -> Error(sprintf "transparent union case '%s' must have exactly one field" tag)
                | _ ->
                    encodeFields idl caseFields fields
                    |> Result.map (typedWith idl.Wire.Discriminator tag)
        | WireForm.Undeclared(Undeclared.Union(name, _)), VUnion _ -> Error(sprintf "unknown union '%s'" name)
        | WireForm.Unbound v, _ -> Error(sprintf "unsubstituted type variable '%s'" v)
        | WireForm.Sentinel(value, wire), v when v = value -> Ok(JStr wire)
        // Phase 676 — verbatim passthrough. Emitting the `JVal` unchanged is what keeps the bytes
        // canonical: `Canonical.write Rendering.CrossHost` already sorts keys Ordinal, escapes per rule 6 and lays floats
        // out per rule 5, so a passthrough inherits all three instead of re-implementing them. A
        // hosted slot's content is the host codec's business, carried the same way (see
        // [[HostedCodec]]); a declared wire form (Phase 252) is checked on DECODE, the direction a
        // document arrives from.
        | (WireForm.Verbatim | WireForm.Hosted _), VJson j -> verbatim j
        | WireForm.Record r, VRecord fields -> encodeFields idl r.Fields fields |> Result.map JObj
        | WireForm.Undeclared(Undeclared.Record name), VRecord _ -> Error(sprintf "unknown record '%s'" name)
        | WireForm.Map vt, VMap entries ->
            let rec go acc =
                function
                | [] -> Ok(JObj(List.rev acc))
                | (k, v) :: rest ->
                    match encodeValue idl vt v with
                    | Ok j -> go ((k, j) :: acc) rest
                    | Error m -> Error m

            // Phase 111 — a map has no DECLARED order, so its entries are
            // Ordinal-sorted at encode: a no-op under `Sorted` rendering, and
            // what keeps `Declared`-order canonical form deterministic. A key
            // the entries carry twice is refused by [[render]] (Phase 495).
            go
                []
                (entries
                 |> List.sortWith (fun (a, _) (b, _) -> System.String.CompareOrdinal(a, b)))
        | WireForm.Node, VNode(id, kindTag, fields) -> encodeNode idl id kindTag fields
        // Phase 698 — the enveloped form, at ANY depth: a nested child carries its
        // envelope through exactly this arm, so the sweep is not root-only.
        | WireForm.Node, VNodeEnv(id, envelope, kindTag, fields) -> encodeNodeEnv idl id envelope kindTag fields
        // A bare kind and an op are both `$type`-tagged objects with named fields —
        // structurally what a union case is — so `VUnion` carries them, and the wire
        // difference is only which vocabulary the tag resolves against.
        | WireForm.Kind, VUnion(tag, fields) ->
            match IdlLookup.tryKind idl tag with
            | None -> Error(sprintf "unknown kind '%s'" tag)
            | Some k ->
                encodeFields idl k.Fields fields
                |> Result.map (typedWith idl.Wire.Discriminator tag)
        | WireForm.Op, VUnion(tag, fields) ->
            match idl.Ops |> List.tryFind (fun o -> o.Tag = tag) with
            | None -> Error(sprintf "unknown op '%s'" tag)
            | Some o ->
                encodeFields idl o.Fields fields
                |> Result.map (typedWith idl.Wire.Discriminator tag)
        | WireForm.List inner, VList xs ->
            let rec go acc =
                function
                | [] -> Ok(JArr(List.rev acc))
                | x :: rest ->
                    match encodeValue idl inner x with
                    | Ok j -> go (j :: acc) rest
                    | Error m -> Error m

            go [] xs
        | _, VAbsent -> Error "absent value reached the encoder (should be omitted at the field level)"
        | _ -> Error(sprintf "authored value does not match IDL type %A" t)

    and private encodeFields
        (idl: Idl)
        (fields: IdlField list)
        (authored: (string * IdlValue) list)
        : Result<(string * JVal) list, string> =
        let known = fields |> List.map _.Name |> Set.ofList

        let extra =
            authored |> List.filter (fun (n, v) -> v <> VAbsent && not (known.Contains n))

        if not (List.isEmpty extra) then
            Error(sprintf "authored fields not in IDL: %s" (extra |> List.map fst |> String.concat ", "))
        else
            let rec go acc =
                function
                | [] -> Ok(List.rev acc)
                | (f: IdlField) :: rest ->
                    match provided f.Name authored, f.Opt with
                    | _, HostOnly -> go acc rest
                    | (None | Some VAbsent), (Optional | OmitDefault _) -> go acc rest
                    | (None | Some VAbsent), Required -> Error(sprintf "required field '%s' is absent" f.Name)
                    // omit-at-default: a present value equal to the field's identity default
                    // emits nothing. Equal IN THE SLOT'S VALUE SPACE (Phase 292), not as
                    // authored terms: `VInt 2` and `VFloat 2.0` at a float slot are one value
                    // with one encoding, and comparing the terms gave that value two — present
                    // under one spelling, omitted under the other.
                    | Some v, OmitDefault d ->
                        match encodeValue idl f.Type v with
                        | Error m -> Error m
                        | Ok j ->
                            let atDefault =
                                v = d
                                || (match encodeValue idl f.Type d with
                                    | Ok jd -> sameCanonical idl j jd
                                    | Error _ -> false)

                            if atDefault then
                                go acc rest
                            else
                                go ((f.Name, j) :: acc) rest
                    | Some v, _ ->
                        match encodeValue idl f.Type v with
                        | Ok j -> go ((f.Name, j) :: acc) rest
                        | Error m -> Error m

            go [] fields

    /// A node with no envelope as its `JVal`, laid out by the declared [[NodeEnvelopeShape]] —
    /// the tree, not the bytes: [[encode]] adds the declared key order and the refusal of an
    /// ill-formed string. An unknown kind tag, a field the kind does not declare, or an absent
    /// `Required` field is an `Error` naming it.
    and encodeNode (idl: Idl) (id: string) (kindTag: string) (fields: (string * IdlValue) list) : Result<JVal, string> =
        match IdlLookup.tryKind idl kindTag with
        | None -> Error(sprintf "unknown kind '%s'" kindTag)
        | Some k ->
            encodeFields idl k.Fields fields
            |> Result.map (fun fs ->
                // Phase 109 — the declared node envelope shape. Nested is the
                // default and byte-identical to the pre-declarable emission; flat
                // puts the tag, the id and the kind fields in ONE object (key
                // order is irrelevant — `Canonical.write Rendering.CrossHost` sorts Ordinal).
                match idl.Wire.NodeEnvelope with
                | NodeEnvelopeShape.NestedKind ->
                    JObj [ "id", JStr id; "kind", typedWith idl.Wire.Discriminator kindTag fs ]
                | NodeEnvelopeShape.FlatKind -> JObj((idl.Wire.Discriminator, JStr kindTag) :: ("id", JStr id) :: fs))

    /// The enveloped partner of [[encodeNode]] (Phase 698) — `id` + `kind` + the
    /// declared node envelope. The envelope rides the SAME [[encodeFields]] the kind
    /// fields ride, so `Optional`-absent, `OmitDefault`-at-default and `HostOnly`
    /// behave identically on a node field and on a kind field; that shared path is
    /// the whole reason the generated hosts and the interpreter can be expected to
    /// agree. Key order is irrelevant — `Canonical.write Rendering.CrossHost` sorts Ordinal.
    and internal encodeNodeEnv
        (idl: Idl)
        (id: string)
        (envelope: (string * IdlValue) list)
        (kindTag: string)
        (fields: (string * IdlValue) list)
        : Result<JVal, string> =
        match IdlLookup.tryKind idl kindTag with
        | None -> Error(sprintf "unknown kind '%s'" kindTag)
        | Some k ->
            match encodeFields idl k.Fields fields with
            | Error m -> Error m
            | Ok kindFs ->
                match encodeFields idl idl.NodeFields envelope with
                | Error m -> Error(sprintf "node envelope: %s" m)
                | Ok envFs ->
                    match idl.Wire.NodeEnvelope with
                    | NodeEnvelopeShape.NestedKind ->
                        Ok(
                            JObj(
                                ("id", JStr id)
                                :: ("kind", typedWith idl.Wire.Discriminator kindTag kindFs)
                                :: envFs
                            )
                        )
                    // Flat: envelope and kind fields share the node object — the
                    // collision [[Declare.wireShapeErrors]] refuses at declaration.
                    // Discriminator first, then id, kind fields, envelope: the
                    // Phase 111 declared order (irrelevant under Sorted rendering).
                    | NodeEnvelopeShape.FlatKind ->
                        Ok(JObj((idl.Wire.Discriminator, JStr kindTag) :: ("id", JStr id) :: (kindFs @ envFs)))

    /// A value of `t` as its canonical `JVal` — what the sampler draws a hosted slot's
    /// declared wire form through (Phase 252), so the drawn JSON is exactly what the
    /// interpreter would write for that type.
    let internal valueJson (idl: Idl) (t: IdlType) (v: IdlValue) : Result<JVal, string> = encodeValue idl t v

    /// The declared canonical renderer (Phase 111) — [[rendering]], GUARDED (Phase 292; THE guard
    /// since Phase 520, `Json.firstRenderFault`, in its one sentence): an ill-formed string (a
    /// lone surrogate has no UTF-8 encoding, so its bytes would be some other string's, and the
    /// parser refuses them on read) and a repeated member name (Phase 495: a `map` value is an
    /// entry list, so it can carry one key twice, and a verbatim `json` value can too; written as
    /// it stands, either is text `Decode.decode` refuses, the decode-rejects corpus's
    /// `repeated-member` row) are refused, named and at their path, so `decode (encode v)` is
    /// defined on every value `encode` accepts. Non-finite floats are NOT refused here: at a
    /// `float` slot the quoted token is WIRE_FORMAT §7's spelling, which the slot's type reads
    /// back, and inside a verbatim value [[verbatim]] has already refused it with its path.
    let private render (idl: Idl) (j: JVal) : Result<string, string> =
        match Json.firstRenderFault true j with
        | Some f -> Error(RenderFault.describe f)
        | None -> Ok(Canonical.write (rendering idl) j)

    /// A value of `t` through the GUARDED renderer (Phase 520) — what `Declare.errors` checks a
    /// declared default against, so a default this encoder would refuse to write (a map carrying
    /// one key twice, an ill-formed string) is refused at its declaration, not at the first
    /// `encode` that meets it.
    let internal renderValue (idl: Idl) (t: IdlType) (v: IdlValue) : Result<string, string> =
        encodeValue idl t v |> Result.bind (render idl)


    /// Encode an authored node to canonical wire JSON — byte-identical to the UI host.
    let encode (idl: Idl) (v: IdlValue) : Result<string, string> =
        match v with
        | VNode(id, kindTag, fields) -> encodeNode idl id kindTag fields |> Result.bind (render idl)
        | VNodeEnv(id, envelope, kindTag, fields) ->
            encodeNodeEnv idl id envelope kindTag fields |> Result.bind (render idl)
        | _ -> Error "top-level authored value must be a node"

    /// Encode an authored TREE OP to canonical wire JSON (Phase 703) — the wire's
    /// second root. Separate from [[encode]] rather than folded into it: the two
    /// roots are distinguishable on the wire (a node carries `id` + `kind`, an op a
    /// top-level `$type`), but which one a caller MEANT is not the codec's guess to
    /// make. The schema states the same thing as `oneOf`.
    let encodeOp (idl: Idl) (v: IdlValue) : Result<string, string> =
        encodeValue idl TOp v |> Result.bind (render idl)
