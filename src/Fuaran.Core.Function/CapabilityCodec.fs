namespace Fuaran.Core

/// The canonical wire codec for a `Capability` declaration + a typed invocation record. Round-trips
/// the full `Signature` (so an enumerated capability re-serialises identically), the determinism
/// tag, and the placement. Fable-clean (`Json` / `Decode`).
module CapabilityCodec =

    // ---- value-space ----

    // The value space is `SpaceCodec`'s and the effect `EffectCodec`'s (Phase 295): one writer and
    // one reader each, shared with every other codec that carries one.

    // Phase 310 — every member is read through the typed decode layer (`Decoder`), so a refusal
    // carries a code and the path to the value at fault (`decodeJsonDetailedWith` and its
    // siblings); the string forms answer the sentence this codec has always answered. One reading
    // changed: an optional `slotKind` that is present and not a string is refused, where it was
    // read as absent.

    // Phase 509: the combinators these readers share are Wire's — `Decoder.pair` (two members of
    // one object), `Decoder.oneOf` (a string from a closed set), `Decoder.strings` and the
    // `"$type"` dispatch `Decoder.tagDispatch` (each in `Decoder`'s own sentence since Phase 520);
    // each was written here, body for body.

    // ---- signature entry + signature ----

    let private entryJson (e: SigEntry) : JVal =
        [ "addr", JStr e.Addr
          "name", JStr e.Name
          "kind", JStr(HoleKind.tag e.Kind)
          "required", JBool e.Required ]
        @ Function.kindMembers SpaceCodec.toJson e.Kind
        |> JObj

    let private entryOf (el: JVal) : Result<SigEntry, DecodeError> =
        let str name = Decoder.field name Decoder.str el

        str "addr"
        |> Result.bind (fun addr ->
            str "name"
            |> Result.bind (fun name ->
                // Phase 295: the tag is one of `HoleKind.tags`; any other is refused.
                Decoder.field "kind" (Decoder.oneOf (HoleKind.tags |> List.map (fun t -> t, t))) el
                |> Result.bind (fun kind ->
                    Decoder.field "required" Decoder.bool el
                    |> Result.bind (fun required ->
                        Decoder.optField "space" SpaceCodec.decoder el
                        |> Result.bind (fun sp ->
                            Decoder.optField "actionEffect" EffectCodec.decoder el
                            |> Result.bind (fun ac ->
                                Decoder.optField "slotKind" Decoder.str el
                                |> Result.bind (fun slot ->
                                    // Phase 409: the entry is read AS its hole kind, so a tag without
                                    // the member its kind needs is refused `MissingField` at that
                                    // member. A slot travels without its derived space (Phase 229);
                                    // a member the kind does not read is not part of the entry.
                                    let holeKind =
                                        match kind, sp, ac with
                                        | "value", Some s, _ -> Ok(ValueHole s)
                                        | "repeat", Some s, _ -> Ok(RepeatHole s)
                                        | "slot", _, _ -> Ok(SlotHole slot)
                                        | "action", _, Some e -> Ok(ActionHole e)
                                        | "action", _, None -> Error(Decoder.missing "actionEffect")
                                        | _ -> Error(Decoder.missing "space")

                                    holeKind
                                    |> Result.map (fun k ->
                                        { Addr = addr
                                          Name = name
                                          Kind = k
                                          Required = required }))))))))

    let internal signatureJson (sg: Signature) : JVal =
        JObj
            [ "name", JStr sg.Name
              "effect", EffectCodec.toJson sg.Effect
              "holes", JArr(sg.Holes |> List.map entryJson) ]

    /// A signature's members, read with no admission check (Phase 385): the capability reader runs
    /// the registries' whole gate (`Capability.admissionOf`) over the signature it reads, and
    /// the bare signature reader runs well-formedness over this.
    let private signatureFieldsOf (el: JVal) : Result<Signature, DecodeError> =
        Decoder.field "name" Decoder.str el
        |> Result.bind (fun name ->
            Decoder.field "effect" EffectCodec.decoder el
            |> Result.bind (fun eff ->
                Decoder.field "holes" (Decoder.list entryOf) el
                |> Result.map (fun holes ->
                    { Name = name
                      Holes = holes
                      Effect = eff })))

    let private signatureOfDetailed (el: JVal) : Result<Signature, DecodeError> =
        signatureFieldsOf el
        |> Result.bind (fun sg ->
            // Phase 307: the reader runs the well-formedness check the registries run, so a
            // signature that decodes is one a registry could admit on well-formedness.
            match Signature.validate sg with
            | Ok() -> Ok sg
            | Error fault ->
                Error(
                    DecodeError.under
                        (PathSegment.Key "holes")
                        (DecodeError.make
                            DecodeCode.OutOfRange
                            "a well-formed declaration"
                            ("ill-formed signature: " + DeclarationFault.describe fault))
                ))

    /// Read a signature object (`name`, `effect`, `holes`) leniently, refusing a hole-kind tag
    /// outside `HoleKind.tags`, an entry without the member its kind needs (`space` for a value or
    /// repeat hole, `actionEffect` for an action hole; Phase 409) and a signature `Signature.validate`
    /// refuses (Phase 307). A slot's space is its constraint's tree, and is never read.
    let signatureOf (el: JVal) : Result<Signature, string> =
        Decoder.describing signatureOfDetailed el

    // ---- placement ----
    // Phase 551: `Placement` (and its `IslandKind`) is read and written by the codec DERIVED from its
    // IDL declaration (`CapabilitySeamCodec`, DECISIONS.md D200); the strict reading's roster is the
    // declaration's.

    // ---- capability declaration ----

    /// Encode a `Capability` declaration to a `JVal` (`"$type":"capability"`).
    let encodeJson (c: Capability) : JVal =
        Canonical.typed
            "capability"
            [ "id", JStr c.Id
              "signature", signatureJson c.Signature
              "determinism", JStr(Effect.determinismTag c.Determinism)
              "placement", CapabilitySeamCodec.encodePlacement c.Placement ]

    /// `encodeJson` rendered canonically: one capability always renders to the same string. Total;
    /// over a declaration the admission gate accepts it is exactly `tryEncode`'s `Ok`, and over one
    /// with a non-finite bound it writes bytes no reader takes back — use `tryEncode` for a
    /// declaration nothing has validated.
    let encode (c: Capability) : string =
        Canonical.write Rendering.CrossHost (encodeJson c)

    /// The GUARDED encode (Phase 307): `Canonical.tryWrite Rendering.CrossHost` of `encodeJson`, so a declaration with a
    /// non-finite bound or an ill-formed string is refused, naming it, rather than written as bytes
    /// the decoder would refuse. `Ok` is exactly `encode`'s string.
    let tryEncode (c: Capability) : Result<string, string> =
        Canonical.tryWrite Rendering.CrossHost (encodeJson c)

    /// The declaration's reader, with the reader of its `placement` (the lenient or the strict one).
    let private decodeJsonDetailedUnder (placement: Decoder<Placement>) (el: JVal) : Result<Capability, DecodeError> =
        // Phase 307: a capability document says so — its `"$type"` is `capability`.
        Decoder.field "$type" (Decoder.oneOf [ "capability", () ]) el
        |> Result.bind (fun () -> Decoder.field "id" Decoder.str el)
        |> Result.bind (fun id ->
            Decoder.field "signature" signatureFieldsOf el
            |> Result.bind (fun sg ->
                // Phase 385: THE admission gate the registries run (D111) — totality, then
                // well-formedness — over the signature just read, where Phase 307's reader ran the
                // well-formedness half alone. A declaration that decodes is one `register` admits,
                // and a refused one is refused at `signature.holes` with the registry's own error.
                match Capability.admissionOf id sg with
                | Some e ->
                    Error(
                        DecodeError.under
                            (PathSegment.Key "signature")
                            (DecodeError.under
                                (PathSegment.Key "holes")
                                (DecodeError.make
                                    DecodeCode.OutOfRange
                                    "a declaration the registries admit"
                                    (InvokeError.describe e)))
                    )
                | None -> Ok sg)
            |> Result.bind (fun sg ->
                // Cross-check the wire `determinism` tag against the signature's effect determinism
                // (Phase 44). The tag is written on encode but was previously ignored on decode, so a
                // payload whose tag disagrees with its signature — tampered, or from a divergent host —
                // decoded silently to the signature-derived value, mis-keying the Phase 27 replay seam.
                let expectedTag = Effect.determinismTag sg.Effect.Determinism

                Decoder.field
                    "determinism"
                    (Decoder.str
                     |> Decoder.andThen (fun wireTag ->
                         if wireTag <> expectedTag then
                             Error(
                                 RootDecodeCode.OutOfRange,
                                 "'" + expectedTag + "', the signature's determinism",
                                 "capability determinism disagrees with signature effect: wire '"
                                 + wireTag
                                 + "' vs signature '"
                                 + expectedTag
                                 + "'"
                             )
                         else
                             Ok()))
                    el
                |> Result.bind (fun () ->
                    Decoder.field "placement" placement el
                    |> Result.map (fun placement ->
                        { Id = id
                          Signature = sg
                          Placement = placement }))))

    let private decodeJsonDetailed: Decoder<Capability> =
        decodeJsonDetailedUnder CapabilitySeamCodec.decodePlacement

    /// Read a capability declaration leniently. The wire `determinism` must equal the label the
    /// decoded signature's effect derives; a disagreeing tag is refused, never silently corrected.
    /// A declaration the registries' admission gate refuses (`NonTotalCapability`,
    /// `IllFormedCapability`) is refused with that error's sentence (Phase 385).
    let decodeJson (el: JVal) : Result<Capability, string> =
        Decoder.describing decodeJsonDetailed el

    /// Parse, then `decodeJson`; a malformed document is refused with the parser's sentence.
    let decode (s: string) : Result<Capability, string> = Json.parse s |> Result.bind decodeJson

    // ---- typed invocation record ----

    /// Encode a typed invocation `(capabilityId, args)` to a `JVal` (`"$type":"invocation"`).
    let internal encodeInvocationJson (capabilityId: string) (args: (string * string) list) : JVal =
        Canonical.typed
            "invocation"
            [ "capabilityId", JStr capabilityId
              "args", JArr(args |> List.map (fun (a, v) -> JObj [ "addr", JStr a; "value", JStr v ])) ]

    /// A typed invocation as a canonical `"$type":"invocation"` document. The arguments keep the
    /// order given, each an `{addr, value}` object; nothing is validated against a signature here.
    let encodeInvocation (capabilityId: string) (args: (string * string) list) : string =
        Canonical.write Rendering.CrossHost (encodeInvocationJson capabilityId args)

    let private invocationOf: Decoder<string * (string * string) list> =
        let arg =
            Decoder.pair (Decoder.field "addr" Decoder.str) (Decoder.field "value" Decoder.str) (fun addr v -> addr, v)

        // Phase 307: one address, one argument — a repeated address is refused here as it is by
        // `validateArgs` (`DuplicateArg`), at the second occurrence. The refusal is raised through
        // `at` (Phase 453), which walks `args[i]` in the document rather than trusting the index.
        let distinctArgs (cid: string, args: (string * string) list) : Decoder<string * (string * string) list> =
            let rec go (seen: Set<string>) (i: int) =
                function
                | [] -> Decoder.succeed (cid, args)
                | (addr: string, _) :: rest ->
                    if seen.Contains addr then
                        Decoder.at
                            [ PathSegment.Key "args"; PathSegment.Index i ]
                            (Decoder.fail
                                RootDecodeCode.OutOfRange
                                "each address once"
                                ("argument '" + addr + "' is given more than once"))
                    else
                        go (seen.Add addr) (i + 1) rest

            go Set.empty 0 args

        Decoder.pair
            (Decoder.field "capabilityId" Decoder.str)
            (Decoder.field "args" (Decoder.list arg))
            (fun cid args -> cid, args)
        |> Decoder.bind distinctArgs

    /// Read an invocation back as `(capabilityId, args)` in wire order, leniently; argument values are
    /// read as strings and checked against nothing until `validateArgs`, but an address given twice
    /// is refused (Phase 307).
    let decodeInvocation (s: string) : Result<string * (string * string) list, string> =
        Json.parse s |> Result.bind (Decoder.describing invocationOf)

    // ---- Deferred<'T> async-result envelope (Phase 32) ----
    // Phase 551: the envelope's codec is DERIVED (`CapabilitySeamCodec.encodeDeferred` /
    // `decodeDeferred` / `decodeDeferredStrict`, typed over the payload's codec); these are its text
    // forms over a payload reader that answers a sentence.

    /// A `Deferred<'T>` as canonical text (`"$type"`-tagged `pending` / `ready` / `failed`), using
    /// `encodeT` for a `Ready` payload. The async-invocation result wire shape every host shares.
    let encodeDeferred (encodeT: 'T -> JVal) (d: Deferred<'T>) : string =
        Canonical.write Rendering.CrossHost (CapabilitySeamCodec.encodeDeferred encodeT d)

    /// A sentence-answering payload reader as a typed one: its refusal is `OutOfRange` at the payload,
    /// carrying the sentence, since the reader names no code.
    let private payloadOf (decodeT: JVal -> Result<'T, string>) : Decoder<'T> =
        fun v ->
            decodeT v
            |> Result.mapError (fun m -> DecodeError.make DecodeCode.OutOfRange "a payload its reader accepts" m)

    /// Parse, then read the envelope, using `decodeT` for a `ready` payload; a refusal from `decodeT` is
    /// carried as its own sentence.
    let decodeDeferred (decodeT: JVal -> Result<'T, string>) (s: string) : Result<Deferred<'T>, string> =
        Json.parse s
        |> Result.bind (Decoder.describing (CapabilitySeamCodec.decodeDeferred (payloadOf decodeT)))

    // ---- typed refusal (Phase 251) ----
    // The refusal crosses the wire like every other seam type, under the same `$type` envelope (one
    // case, one tag; the case name with its first letter lowered, D163). A value space travels in its
    // codec form, so the document decodes back to the same `InvokeError`. The sentence a model reads is
    // `InvokeError.describe`; the structured form beside it is `CapabilitySeamCodec`'s (Phase 551),
    // derived from the union's IDL declaration with `DeclarationFault`'s.

    /// An `InvokeError` as canonical text; `decodeInvokeError` reads it back to the same value.
    let encodeInvokeError (e: InvokeError) : string =
        Canonical.write Rendering.CrossHost (CapabilitySeamCodec.encodeInvokeError e)

    /// Parse, then read an `InvokeError`; a refusal is the decode refusal's sentence (an unknown `$type`
    /// names the tags the reader knows).
    let decodeInvokeError (s: string) : Result<InvokeError, string> =
        Json.parse s
        |> Result.bind (Decoder.describing CapabilitySeamCodec.decodeInvokeError)

    // ---- strict read policy (Phase 251) ----
    // `Strict` is a members check over the document BEFORE the ordinary decoder runs: every object
    // this codec reads may carry only the members this codec reads from it. The decoders above are
    // untouched, so `Lenient` is byte-for-byte the old behaviour. A shape fault (a string where an
    // object belongs) is left to the decoder to name; the check only ever adds the unknown-member
    // refusal. A `ready` envelope's payload is the caller's `decodeT`'s to read, so its members are
    // the caller's to police.

    // Phase 310 — the members check IS the decode layer's strict policy (`Decoder.members`, its
    // generalisation); a refusal keeps this codec's sentence, naming the object it is in, and its
    // path names the member.

    open SeamCodec

    let private strictSpace (el: JVal) =
        let extra =
            match tagOf el with
            | Some "intRange"
            | Some "floatRange"
            | Some "stringLen" -> [ "min"; "max" ]
            | Some "enum" -> [ "values" ]
            | Some "slotTree" -> [ "slotKind" ]
            | _ -> []

        Decoder.members ("$type" :: extra) el

    let private strictEffect (el: JVal) = Decoder.members EffectCodec.members el

    let private strictEntry (el: JVal) =
        Decoder.members [ "addr"; "name"; "kind"; "required"; "space"; "slotKind"; "actionEffect" ] el
        |> Result.bind (fun () -> within "space" strictSpace el)
        |> Result.bind (fun () -> within "actionEffect" strictEffect el)

    let private strictSignature (el: JVal) =
        Decoder.members [ "name"; "effect"; "holes" ] el
        |> Result.bind (fun () -> within "effect" strictEffect el)
        |> Result.bind (fun () -> within "holes" (each strictEntry) el)

    let private strictCapability (el: JVal) =
        Decoder.members [ "$type"; "id"; "signature"; "determinism"; "placement" ] el
        |> Result.bind (fun () -> within "signature" strictSignature el)

    let private strictInvocation (el: JVal) =
        Decoder.members [ "$type"; "capabilityId"; "args" ] el
        |> Result.bind (fun () -> within "args" (each (Decoder.members [ "addr"; "value" ])) el)

    let private under (policy: ReadPolicy) (check: Decoder<unit>) (d: Decoder<'T>) : Decoder<'T> =
        match policy with
        | ReadPolicy.Lenient -> d
        | ReadPolicy.Strict -> check |> Decoder.bind (fun () -> d)

    /// `decodeJsonWith` answering a typed refusal (Phase 310): its code, the path to the value at
    /// fault (an undeclared member under `Strict` is `UndeclaredMember` at that member), what the
    /// position expected, and `decodeJsonWith`'s sentence.
    /// Under `Strict` the declaration's own members are checked first, and its `placement` is read by
    /// the derived strict reader (Phase 551), whose roster is the placement's IDL declaration.
    let decodeJsonDetailedWith (policy: ReadPolicy) (el: JVal) : Result<Capability, DecodeError> =
        match policy with
        | ReadPolicy.Lenient -> decodeJsonDetailed el
        | ReadPolicy.Strict ->
            strictCapability el
            |> Result.bind (fun () -> decodeJsonDetailedUnder CapabilitySeamCodec.decodePlacementStrict el)

    /// `decodeWith` answering a typed refusal (Phase 310); a parse failure is refused at the root.
    let decodeDetailedWith (policy: ReadPolicy) (s: string) : Result<Capability, DecodeError> =
        Decoder.parse ReadOptions.strict s
        |> Result.bind (decodeJsonDetailedWith policy)

    /// `decodeJson` under a read policy: `Strict` refuses an unknown member anywhere in the
    /// declaration (its signature, holes, value spaces, effects and placement included).
    let decodeJsonWith (policy: ReadPolicy) (el: JVal) : Result<Capability, string> =
        decodeJsonDetailedWith policy el |> Result.mapError DecodeError.describe

    /// `decode` under a read policy.
    let decodeWith (policy: ReadPolicy) (s: string) : Result<Capability, string> =
        Json.parse s |> Result.bind (decodeJsonWith policy)

    /// `decodeInvocationWith` answering a typed refusal (Phase 310); a parse failure is refused at
    /// the root.
    let decodeInvocationDetailedWith
        (policy: ReadPolicy)
        (s: string)
        : Result<string * (string * string) list, DecodeError> =
        Decoder.parse ReadOptions.strict s
        |> Result.bind (under policy strictInvocation invocationOf)

    /// `decodeInvocation` under a read policy: `Strict` refuses an unknown member of the
    /// invocation or of any of its arguments — an `"actor"` beside the `capabilityId`, say, which
    /// `Lenient` reads past.
    let decodeInvocationWith (policy: ReadPolicy) (s: string) : Result<string * (string * string) list, string> =
        Json.parse s
        |> Result.bind (fun el ->
            under policy strictInvocation invocationOf el
            |> Result.mapError DecodeError.describe)

    /// `decodeDeferred` under a read policy: `Strict` refuses an unknown member of the envelope
    /// (`CapabilitySeamCodec.decodeDeferredStrict`); the `ready` payload is `decodeT`'s to read.
    let decodeDeferredWith
        (policy: ReadPolicy)
        (decodeT: JVal -> Result<'T, string>)
        (s: string)
        : Result<Deferred<'T>, string> =
        let read =
            match policy with
            | ReadPolicy.Lenient -> CapabilitySeamCodec.decodeDeferred (payloadOf decodeT)
            | ReadPolicy.Strict -> CapabilitySeamCodec.decodeDeferredStrict (payloadOf decodeT)

        Json.parse s |> Result.bind (Decoder.describing read)
