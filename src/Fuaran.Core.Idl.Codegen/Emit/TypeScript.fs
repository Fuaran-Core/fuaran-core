namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.Reach
open Fuaran.Core.Idl.Emit.Annotations
open Fuaran.Core.Idl.Emit.FSharpDefaults

/// The TypeScript backend — the structural encoder/decoder module, the declarations, and the
/// scaffold value emitter.
module internal TypeScript =

    // -----------------------------------------------------------------------
    // Phase 317 increment 8 — the SECOND BACKEND (TypeScript). The same IDL now
    // generates an *independent* host's structural encoder (no FSharp.Core /
    // .NET dependency — plain JS string-building). A node run of it over the
    // corpus is byte-identical to the F# generated encoder, establishing the
    // cross-host byte-identity that is the precondition for cross-host
    // attestation (Phase 320). Encode-only, mirroring the F# encoder leg.
    // -----------------------------------------------------------------------

    let invariantFloat (f: float) : string =
        f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)

    /// Property access under the declared discriminator: `v.$type` / `v.kind`,
    /// or `v["odd key"]` for a key JS cannot spell bare.
    let tsDiscProp (disc: string) (obj: string) =
        if SourceLit.tsIsIdentifier disc then
            obj + "." + disc
        else
            obj + "[" + SourceLit.tsString disc + "]"

    /// The declared discriminator in object-literal key position.
    let tsDiscKey (disc: string) =
        if SourceLit.tsIsIdentifier disc then
            disc
        else
            SourceLit.tsString disc

    /// Emit a TypeScript value literal for an authored `IdlValue` under the
    /// vocabulary's DECLARED wire shape (Phases 108/109) — unions/nodes become
    /// discriminator-tagged objects (matching the generated encoder's dispatch),
    /// fields keyed by name; a flat vocabulary's node is ONE object. Builds the
    /// conformance fixtures the node harness runs.
    let rec internal typescriptValueWith (shape: WireShape) (v: IdlValue) : string =
        let go = typescriptValueWith shape
        let disc = tsDiscKey shape.Discriminator

        match v with
        | VStr s -> SourceLit.tsString s
        | VInt i -> string i
        | VBool b -> if b then "true" else "false"
        | VFloat f -> invariantFloat f
        | VEnum s -> SourceLit.tsString s
        | VUnion(tag, fields) ->
            "{ "
            + disc
            + ": "
            + SourceLit.tsString tag
            + (fields
               |> List.map (fun (n, fv) -> ", " + SourceLit.tsKey n + ": " + go fv)
               |> String.concat "")
            + " }"
        | VList xs -> "[" + (xs |> List.map go |> String.concat ", ") + "]"
        | VRecord fields ->
            "{ "
            + (fields
               |> List.map (fun (n, fv) -> SourceLit.tsKey n + ": " + go fv)
               |> String.concat ", ")
            + " }"
        | VMap entries ->
            "{ "
            + (entries
               |> List.map (fun (k, fv) -> SourceLit.tsString k + ": " + go fv)
               |> String.concat ", ")
            + " }"
        | VNode(id, kindTag, fields) ->
            match shape.NodeEnvelope with
            | NodeEnvelopeShape.NestedKind ->
                "{ id: "
                + SourceLit.tsString id
                + ", kind: { "
                + disc
                + ": "
                + SourceLit.tsString kindTag
                + (fields
                   |> List.map (fun (n, fv) -> ", " + SourceLit.tsKey n + ": " + go fv)
                   |> String.concat "")
                + " } }"
            | NodeEnvelopeShape.FlatKind ->
                "{ "
                + disc
                + ": "
                + SourceLit.tsString kindTag
                + ", id: "
                + SourceLit.tsString id
                + (fields
                   |> List.map (fun (n, fv) -> ", " + SourceLit.tsKey n + ": " + go fv)
                   |> String.concat "")
                + " }"
        // Phase 698 — the envelope sits BESIDE `kind` on the emitted object, which is
        // where the generated `encodeNode` reads it from (`n.style`, `n.state`, …).
        // Nothing else changes: the generated TS node encoder has read the envelope
        // since Phase 690; only the VALUE emitter could not express one.
        | VNodeEnv(id, envelope, kindTag, fields) ->
            match shape.NodeEnvelope with
            | NodeEnvelopeShape.NestedKind ->
                "{ id: "
                + SourceLit.tsString id
                + (envelope
                   |> List.map (fun (n, fv) -> ", " + SourceLit.tsKey n + ": " + go fv)
                   |> String.concat "")
                + ", kind: { "
                + disc
                + ": "
                + SourceLit.tsString kindTag
                + (fields
                   |> List.map (fun (n, fv) -> ", " + SourceLit.tsKey n + ": " + go fv)
                   |> String.concat "")
                + " } }"
            | NodeEnvelopeShape.FlatKind ->
                "{ "
                + disc
                + ": "
                + SourceLit.tsString kindTag
                + ", id: "
                + SourceLit.tsString id
                + (envelope
                   |> List.map (fun (n, fv) -> ", " + SourceLit.tsKey n + ": " + go fv)
                   |> String.concat "")
                + (fields
                   |> List.map (fun (n, fv) -> ", " + SourceLit.tsKey n + ": " + go fv)
                   |> String.concat "")
                + " }"
        | VAbsent -> "undefined"
        // Closure / opaque values carry no data the TS encoder reads — its codec
        // emits the sentinel regardless, so any placeholder operand serialises right.
        //
        // But it must not be `undefined`, which is how [[VAbsent]] says "this field
        // is NOT on the wire": `tsSpecPieceOf` omits an optional field on exactly that
        // test, so an OPTIONAL closure/opaque slot silently vanished from the TS
        // encoding while F# emitted its sentinel. Latent since the TS backend landed
        // — no fixture and no sampled vector had an optional sentinel field until the
        // Phase 689 spike added `Tabs.onSelect`, at which point the generative
        // conformance test failed at vector 6. A stand-in that is PRESENT keeps the
        // presence test honest without giving the codec anything to read.
        | VClosure
        | VOpaque -> "(() => undefined)"
        // Phase 676 — a JSON value emits as its canonical literal.
        | VJson j -> Canon.render j

    /// The point-free TS encoder reference for a type (used where a codec must be
    /// passed — a generic union's type-parameter codec).
    let rec tsEncFn (t: IdlType) : Result<string, CodegenError> =
        match t with
        | TStr -> Ok "encStr"
        | TInt -> Ok "encInt"
        | TBool -> Ok "encBool"
        | TFloat -> Ok "encFloat"
        | TEnum _ -> Ok "encStr" // an enum value IS its wire string
        | TVar v -> Ok("enc" + v)
        | TNode -> Ok "encodeNode"
        // Phase 195 — the op vocabulary is REFUSED AS DATA rather than thrown at. See
        // [[opVocabularySlot]] for why the arm exists and what it says.
        | TKind
        | TOp -> Error(opVocabularySlot "the TypeScript encoder backend" t)
        | TUnion(n, []) -> Ok("enc" + n)
        | TUnion(n, args) ->
            args
            |> List.map tsEncFn
            |> concatR ", "
            |> Result.map (fun a -> "((x) => enc" + n + "(" + a + ", x))")
        | TList inner ->
            tsEncFn inner
            |> Result.map (fun e -> "((xs) => '[' + xs.map(" + e + ").join(',') + ']')")
        // A closure/opaque codec ignores its argument and emits the fixed sentinel.
        | TClosure
        | TFn _ -> Ok "(() => '\"<closure>\"')"
        | TOpaque -> Ok "(() => '\"<opaque>\"')"
        // Phase 252 — a hosted slot that declares its wire form is written THROUGH it, so
        // the bytes are that type's canonical ones.
        | THosted h when h.Wire.IsSome ->
            match hostedWireRefusal "the TypeScript encoder backend" h with
            | Some e -> Error e
            | None -> tsEncFn h.Wire.Value
        // Phase 676 — `encJson` renders the parsed value canonically (see the prelude).
        // A hosted slot is verbatim JSON to the TS backend, like the interpreter.
        | TJson
        | THosted _ -> Ok "encJson"
        | TRecord n -> Ok("enc" + n)
        | TMap vt ->
            tsEncFn vt
            |> Result.map (fun e ->
                "((m) => '{' + Object.keys(m).sort().map((k) => encStr(k) + ':' + ("
                + e
                + ")(m[k])).join(',') + '}')")

    /// The applied TS encode expression for a value of `t` bound to `var`.
    let tsEncApplied (var: string) (t: IdlType) : Result<string, CodegenError> =
        match t with
        | TList inner ->
            tsEncFn inner
            |> Result.map (fun e -> "'[' + " + var + ".map(" + e + ").join(',') + ']'")
        | TUnion(n, (_ :: _ as args)) ->
            args
            |> List.map tsEncFn
            |> concatR ", "
            |> Result.map (fun a -> "enc" + n + "(" + a + ", " + var + ")")
        | TNode -> Ok("encodeNode(" + var + ")")
        | _ -> tsEncFn t |> Result.map (fun e -> e + "(" + var + ")")

    /// TS boolean predicate: is `src` at the omit-when-default field's identity default?
    /// Enums render as wire strings (`s.tone === "Default"`); nullary unions as
    /// discriminator-tagged objects (`s.format.$type === "None"`); since Phase 124 a
    /// VALUE-CARRYING union case conjoins a test per declared field
    /// (`s.value.$type === "Fixed" && s.value.value === 0`), nested to any depth.
    ///
    /// **This predicate decides what the TS backend admits as a default at all**, because it is
    /// the harder half: JS has no structural equality, so every admissible shape has to reduce to
    /// a chain of `===` (plus the one `.length === 0` an empty array needs, arrays comparing by
    /// reference). [[tsDefaultLit]] takes its admissibility from here rather than deciding
    /// separately — an encoder that cannot TEST for a default must not have a decoder that
    /// RESTORES it, or the two halves of the round trip disagree silently, which is the class
    /// Phase 124 exists to close.
    let rec tsIsDefault
        (idl: Idl)
        (disc: string)
        (src: string)
        (t: IdlType)
        (d: IdlValue)
        : Result<string, CodegenError> =
        let refuse () =
            Error(CodegenError.UnsupportedDefault(t, d))

        match t, d with
        // A `VEnum` carries the WIRE string; one the enum does not admit is refused, as the F#
        // backend refuses it (Phase 292) and as the encoder refuses the value.
        | TEnum n, VEnum c ->
            if idl.Enums |> List.exists (fun e -> e.Name = n && List.contains c e.WireCases) then
                Ok(src + " === " + SourceLit.tsString c)
            else
                refuse ()
        // Refused as the F# backend refuses it, so the two agree (D33).
        | TStr, VStr s when not (SourceLit.isWellFormed s) -> refuse ()
        | TStr, VStr s -> Ok(src + " === " + SourceLit.tsString s)
        | TInt, VInt i -> Ok(src + " === " + string i)
        | TFloat, VFloat f when System.Double.IsFinite f -> Ok(src + " === " + invariantFloat f)
        | TFloat, VInt i -> Ok(src + " === " + invariantFloat (float i))
        | TBool, VBool b -> Ok(src + " === " + (if b then "true" else "false"))
        // Phase 1080 — an omitted-when-empty repeated slot. `.length === 0` rather
        // than an equality test, because JS arrays compare by reference.
        | TList _, VList [] -> Ok(src + ".length === 0")
        | TRecord n, VRecord authored ->
            match idl.Records |> List.tryFind (fun r -> r.Name = n) with
            | None -> refuse ()
            | Some r ->
                r.Fields
                |> List.map (fun rf -> tsIsDefaultField idl disc src Map.empty rf authored)
                |> concatR " && "
        | TUnion(n, args), VUnion(tag, authored) ->
            match idl.Unions |> List.tryFind (fun u -> u.Name = n) with
            | None -> refuse ()
            | Some u when List.length u.Params <> List.length args -> refuse ()
            // A DECLARED transparent case is on the wire BARE, so neither the tagged predicate
            // below nor the tagged literal [[tsDefaultLit]] renders would be about the value the
            // encoder actually sees. Refused rather than guessed at. (Unreachable before Phase
            // 124: a transparent case holds exactly one field, so it was never nullary.) Since
            // the 2026-09-13 ruling the test is [[isDeclaredTransparentCase]], which `fsDefaultLit`
            // calls too — the rule is about the WIRE, so it cannot belong to one backend.
            | Some u when isDeclaredTransparentCase idl u tag -> refuse ()
            | Some u ->
                match u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                | None -> refuse ()
                | Some c ->
                    let subst = TypeParams.bind u args |> Option.defaultValue Map.empty

                    c.Fields
                    |> List.map (fun cf -> tsIsDefaultField idl disc src subst cf authored)
                    |> sequenceR
                    |> Result.map (fun conjuncts ->
                        (tsDiscProp disc src + " === " + SourceLit.tsString tag) :: conjuncts
                        |> String.concat " && ")
        | _ -> refuse ()

    /// One declared member of a union case or a record, as a conjunct of the predicate above.
    /// An absent optional is a real conjunct (`s.value.name === undefined`), not a skip: the
    /// encoder omits that key, so a value carrying it is NOT at the default and must not be.
    and tsIsDefaultField
        (idl: Idl)
        (disc: string)
        (src: string)
        (subst: Map<string, IdlType>)
        (f: IdlField)
        (authored: (string * IdlValue) list)
        : Result<string, CodegenError> =
        let ft = TypeParams.substitute subst f.Type
        let member' = src + "." + f.Name

        match authored |> List.tryFind (fun (n, _) -> n = f.Name) with
        | Some(_, av) when av <> VAbsent ->
            match f.Opt with
            // A host-only slot never reaches the wire, so the JS object the encoder sees has no
            // member to test — a default that depends on one is not testable at all.
            | HostOnly -> Error(CodegenError.UnsupportedDefault(ft, av))
            | _ -> tsIsDefault idl disc member' ft av
        | _ ->
            match f.Opt with
            | Optional -> Ok(member' + " === undefined")
            | OmitDefault dd -> tsIsDefault idl disc member' ft dd
            | HostOnly
            | Required -> Error(CodegenError.UnsupportedDefault(ft, VAbsent))

    /// One field of a spec encoder — a `[key, enc]` pair, or `null` (filtered) for
    /// an absent optional.
    /// `recv` is the bound JS variable — `s` for a spec/record encoder, `n` for the
    /// node envelope (Phase 690), mirroring [[specPieceOf]] on the F# side.
    let tsSpecPieceOf (idl: Idl) (disc: string) (recv: string) (f: IdlField) : Result<string, CodegenError> =
        let src = recv + "." + f.Name

        tsEncApplied src f.Type
        |> Result.bind (fun enc ->
            let pair = "[" + SourceLit.tsString f.Name + ", " + enc + "]"

            match f.Opt with
            | Required -> Ok pair
            | Optional -> Ok("(" + src + " === undefined ? null : " + pair + ")")
            | HostOnly -> Ok "null"
            | OmitDefault d ->
                tsIsDefault idl disc src f.Type d
                |> Result.map (fun pred -> "(" + pred + " ? null : " + pair + ")"))

    /// A union CASE field, honouring the same presence rules as a spec field.
    /// Phase 317 generative conformance caught this ignoring `f.Opt` entirely:
    /// TS emitted every optional union-case field unconditionally while F# omitted
    /// it, so `LayoutMode.Grid` without `templateColumns` diverged (and threw in
    /// the escaper). No fixed fixture carried that shape.
    let tsCasePair (idl: Idl) (disc: string) (f: IdlField) : Result<string, CodegenError> =
        let src = "v." + f.Name

        tsEncApplied src f.Type
        |> Result.bind (fun enc ->
            let pair = "[" + SourceLit.tsString f.Name + ", " + enc + "]"

            match f.Opt with
            | Required -> Ok pair
            | Optional -> Ok("(" + src + " === undefined ? null : " + pair + ")")
            | HostOnly -> Ok "null"
            | OmitDefault d ->
                tsIsDefault idl disc src f.Type d
                |> Result.map (fun pred -> "(" + pred + " ? null : " + pair + ")"))

    // -----------------------------------------------------------------------
    // Phase 113 — declared annotations in the TypeScript backend.
    //
    // This backend emits plain JS: there is no per-field declaration to hang a
    // JSDoc block on, because a field is an inline entry in a one-line object
    // literal or pairs array. So an annotation renders as `//` line comments at the
    // closest line the emission actually has — the `case "<Tag>":` arm for a union
    // case, and the owning `function` for a field, NAMING the field.
    //
    // Line comments rather than a `/** … */` JSDoc block, deliberately: a
    // `@deprecated` JSDoc attached to `encTextSource` would tell every editor that
    // the ENCODER is deprecated, which is false. A comment that names its subject
    // says the true thing in a place tooling will not misread.
    // -----------------------------------------------------------------------

    /// The annotation lines for one member, at the given indent, each naming
    /// `subject`. Empty for an empty set — an unannotated vocabulary's emitted JS is
    /// byte-for-byte what it was.
    let tsAnnotationLines (indent: string) (subject: string) (a: Annotations) : string list =
        [ match a.Deprecated with
          | Some d ->
              "@deprecated `"
              + subject
              + "`"
              + (match said d.Replacement with
                 | Some r -> " — use `" + r + "` instead."
                 | None -> " — marked for retirement.")
              + (match said d.Message with
                 | Some m -> " " + m
                 | None -> "")
          | None -> ()

          if a.InProcessOnly then
              "`"
              + subject
              + "` is in-process only: no wire projection, so a value here is lost across a wire boundary."

          match said a.Since with
          | Some v -> "`" + subject + "` — since " + v + "."
          | None -> () ]
        // Phase 292 — each note is split at every line break ([[SourceLit.tsCommentLines]]):
        // a message, replacement or version carrying one used to end the `//` comment and
        // put the rest of the text into the generated module as live code.
        |> List.collect SourceLit.tsCommentLines
        |> List.map (fun l -> indent + "// " + l)

    /// The comment block a generated function carries for its annotated FIELDS,
    /// each named `<owner>.<field>`. Empty string when nothing is annotated.
    let tsFieldAnnotationHeader (owner: string) (fields: IdlField list) : string =
        let lines =
            fields
            |> List.collect (fun f -> tsAnnotationLines "" (owner + "." + f.Name) f.Annotations)

        match lines with
        | [] -> ""
        | ls -> (ls |> String.concat "\n") + "\n"

    /// Phase 119 — the header a kind's own annotations earn, on the two generated
    /// functions that ARE the kind on this side: its spec encoder and its spec decoder.
    /// The comment NAMES the kind, for the same reason a field's names the field — a
    /// `@deprecated` JSDoc on `encFooSpec` would tell tooling the ENCODER is deprecated,
    /// which is false. Combined with the field header, so an annotated kind whose fields
    /// are also annotated says both, kind first.
    let tsKindAnnotationHeader (k: IdlKind) : string =
        let lines = tsAnnotationLines "" k.Tag k.Annotations

        (match lines with
         | [] -> ""
         | ls -> (ls |> String.concat "\n") + "\n")
        + tsFieldAnnotationHeader k.Tag k.Fields

    let tsUnionEncoder (idl: Idl) (disc: string) (tokens: HardenPolicy) (u: IdlUnion) =
        let argList =
            match u.Params with
            | [] -> "v"
            | ps -> (ps |> List.map (fun p -> "enc" + p) |> String.concat ", ") + ", v"

        let arm (c: IdlUnionCase) =
            let ann =
                match tsAnnotationLines "    " c.Tag c.Annotations with
                | [] -> ""
                | ls -> (ls |> String.concat "\n") + "\n"

            (match TransparentUnion.tag tokens u with
             | Some ttag when ttag = c.Tag ->
                 // Transparent case: return the single field's value bare (no `typed(...)`).
                 match c.Fields with
                 | [ f ] ->
                     tsEncApplied ("v." + f.Name) f.Type
                     |> Result.map (fun enc -> "    case " + SourceLit.tsString c.Tag + ": return " + enc + ";")
                 | _ -> Error(transparentArity u.Name c.Tag)
             | _ ->
                 c.Fields
                 |> List.map (tsCasePair idl disc)
                 |> concatR ", "
                 |> Result.map (fun pairs ->
                     "    case "
                     + SourceLit.tsString c.Tag
                     + ": return typed("
                     + SourceLit.tsString c.Tag
                     + ", ["
                     + pairs
                     + "]);"))
            |> Result.map (fun body -> ann + body)

        u.Cases
        |> List.map arm
        |> concatR "\n"
        |> Result.map (fun arms ->
            (u.Cases
             |> List.map (fun c -> tsFieldAnnotationHeader (u.Name + "." + c.Tag) c.Fields)
             |> String.concat "")
            + "function enc"
            + u.Name
            + "("
            + argList
            + ") {\n  switch ("
            + tsDiscProp disc "v"
            + ") {\n"
            + arms
            + "\n  }\n}")

    /// A non-discriminated *record* encoder — a plain object, no discriminator,
    /// mirroring `recordEncoder` on the F# side. Added by Phase 690: the TS backend
    /// decoded records but could not ENCODE them, so `tsEncFn`'s `TRecord n -> "enc" + n`
    /// named a function that was never emitted. Harmless while the only IDL the TS
    /// backend ran on had no records, and a `ReferenceError` waiting for the first
    /// one — the node envelope is three of them.
    let tsRecordEncoder (idl: Idl) (disc: string) (r: IdlRecord) =
        r.Fields
        |> List.map (tsSpecPieceOf idl disc "s")
        |> concatR ", "
        |> Result.map (fun pieces ->
            tsFieldAnnotationHeader r.Name r.Fields
            + "function enc"
            + r.Name
            + "(s) {\n  return plain(["
            + pieces
            + "]);\n}")

    /// A kind's spec encoder. Nested (the default): the discriminator-tagged
    /// object, byte-identical to the pre-declarable emission. Flat (Phase 109):
    /// the PAIRS alone — `encodeNode` merges them with the id and the envelope
    /// into the one flat object.
    let tsSpecEncoder (idl: Idl) (disc: string) (flat: bool) (k: IdlKind) =
        let ann = tsKindAnnotationHeader k

        k.Fields
        |> List.map (tsSpecPieceOf idl disc "s")
        |> concatR ", "
        |> Result.map (fun pieces ->
            if flat then
                ann + "function enc" + k.Tag + "SpecPairs(s) {\n  return [" + pieces + "];\n}"
            else
                ann
                + "function enc"
                + k.Tag
                + "Spec(s) {\n  return typed("
                + SourceLit.tsString k.Tag
                + ", ["
                + pieces
                + "]);\n}")

    // ---- Phase 672 task 4: the TS decoder backend ----
    // The JS host's in-memory shape IS the wire shape (plain objects), so decoding
    // is validation plus rebuilding the positions the encoder writes implicitly:
    // omit-when-default fields (refilled so the encoder omits them again),
    // transparent union cases (re-wrapped so the encoder can re-flatten them), and
    // closure/opaque sentinels (whose PRESENCE is the only information they carry).

    /// The point-free TS decoder reference for a type.
    let rec tsDecFn (t: IdlType) : Result<string, CodegenError> =
        match t with
        | TStr -> Ok "dStr"
        | TInt -> Ok "dInt"
        | TBool -> Ok "dBool"
        | TFloat -> Ok "dFloat"
        | TEnum n -> Ok("dec" + n)
        | TVar v -> Ok("dec" + v)
        | TNode -> Ok "decNode"
        // Phase 195 — the op vocabulary is REFUSED AS DATA rather than thrown at. See
        // [[opVocabularySlot]] for why the arm exists and what it says.
        | TKind
        | TOp -> Error(opVocabularySlot "the TypeScript decoder backend" t)
        | TUnion(n, []) -> Ok("dec" + n)
        | TUnion(n, args) ->
            args
            |> List.map tsDecFn
            |> concatR ", "
            |> Result.map (fun a -> "((x) => dec" + n + "(" + a + ", x))")
        | TList inner -> tsDecFn inner |> Result.map (fun d -> "dList(" + d + ")")
        // The value carries nothing; only its presence matters (see tsDecField).
        // A `TFn` slot is the same on the wire — the TS tier has no `'Msg` to
        // rebuild into, so it stays `null` there regardless of the declared signature.
        | TClosure
        | TFn _
        | TOpaque -> Ok "(() => null)"
        // Phase 252 — a hosted slot that declares its wire form is CHECKED against it (its
        // type, then its format), which is what makes this host refuse what the F# host's
        // codec refuses.
        | THosted h when h.Wire.IsSome ->
            match hostedWireRefusal "the TypeScript decoder backend" h with
            | Some e -> Error e
            | None ->
                tsDecFn h.Wire.Value
                |> Result.map (fun d ->
                    match h.Format with
                    | Some f -> "((x) => dFormat(" + SourceLit.tsString f + ", " + d + "(x)))"
                    | None -> d)
        // Phase 676 — keep the parsed JSON as-is. Hosted slots identically.
        | TJson
        | THosted _ -> Ok "((x) => x)"
        | TRecord n -> Ok("dec" + n)
        | TMap vt -> tsDecFn vt |> Result.map (fun d -> "dMap(" + d + ")")

    /// The TS literal for an omit-when-default value, in its WIRE representation —
    /// refilled on decode so the encoder's omit test fires again and the bytes match.
    ///
    /// **Admissibility is [[tsIsDefault]]'s, and the rendering is
    /// [[typescriptValueWith]]'s** — neither is re-decided here. The first is the correctness
    /// argument (a default the encoder cannot test for must not be one the decoder restores);
    /// the second keeps every pre-Phase-124 emission byte-identical, since the value emitter
    /// already rendered a discriminator-tagged object exactly the way this function used to
    /// build one by hand, and it already handles the payload-carrying case.
    let tsDefaultLit (idl: Idl) (disc: string) (t: IdlType) (d: IdlValue) : Result<string, CodegenError> =
        tsIsDefault idl disc "__probe" t d
        // Phase 1080 — an absent repeated slot decodes to the EMPTY ARRAY, never to
        // `undefined` or `null`: the missing-list-field decode class, pinned here
        // rather than left to each host's own reading of an absent key.
        |> Result.map (fun _ -> typescriptValueWith idl.Wire d)

    let tsDecField (idl: Idl) (disc: string) (f: IdlField) : Result<string, CodegenError> =
        let key = SourceLit.tsString f.Name

        match f.Type with
        | TClosure
        | TFn _
        | TOpaque ->
            match f.Opt with
            | Optional -> Ok("dPresent(" + key + ", fs)")
            | _ -> Ok "null"
        | _ ->
            match f.Opt with
            | Required -> tsDecFn f.Type |> Result.map (fun d -> "dReq(" + key + ", fs, " + d + ")")
            | Optional -> tsDecFn f.Type |> Result.map (fun d -> "dOpt(" + key + ", fs, " + d + ")")
            | HostOnly -> Ok "undefined"
            // Phase 124 — `dReq` here was the TS mirror of the F# decoder's own fallback: the
            // encoder emitted the key unconditionally, so the decoder demanded it, and the pair
            // agreed with each other while contradicting the IDL that declared the slot omitted
            // at its default. There is no fallback now; an unrenderable default refuses the module.
            | OmitDefault d ->
                tsDefaultLit idl disc f.Type d
                |> Result.bind (fun lit ->
                    tsDecFn f.Type
                    |> Result.map (fun dfn -> "dDef(" + key + ", fs, " + dfn + ", " + lit + ")"))

    let tsFieldObject
        (idl: Idl)
        (disc: string)
        (extra: (string * string) list)
        (fields: IdlField list)
        : Result<string, CodegenError> =
        fields
        |> List.map (fun f ->
            tsDecField idl disc f
            |> Result.map (fun e -> SourceLit.tsString f.Name + ": " + e))
        |> sequenceR
        |> Result.map (fun decoded ->
            let pairs = (extra |> List.map (fun (k, v) -> k + ": " + v)) @ decoded
            "{ " + String.concat ", " pairs + " }")

    let tsEnumDecoder (e: IdlEnum) =
        // TS holds an enum AS its wire string — there is no second representation
        // on this side, so the decoder's closed set is the wire strings.
        let cases = e.WireCases |> List.map SourceLit.tsString |> String.concat ", "

        // Phase 119 — a case's annotations render above the one line this backend emits
        // for the whole enum, each naming `<Enum>."<wire>"`: the emission is a single
        // `dEnum` call with no per-case declaration to sit on, and a case here IS its
        // wire string. Absent for an unannotated enum, so the emitted JS is unchanged.
        let ann =
            e.Cases
            |> List.collect (fun c ->
                tsAnnotationLines "" (e.Name + "." + SourceLit.tsString (e.WireOf c)) (e.AnnotationsOf c))

        (match ann with
         | [] -> ""
         | ls -> (ls |> String.concat "\n") + "\n")
        + "const dec"
        + e.Name
        + " = dEnum("
        + SourceLit.tsString e.Name
        + ", ["
        + cases
        + "]);"

    let tsUnionDecoder (idl: Idl) (disc: string) (tokens: HardenPolicy) (u: IdlUnion) =
        let argList =
            match u.Params with
            | [] -> "j"
            | ps -> (ps |> List.map (fun p -> "dec" + p) |> String.concat ", ") + ", j"

        let arm (c: IdlUnionCase) =
            let ann =
                match tsAnnotationLines "    " c.Tag c.Annotations with
                | [] -> ""
                | ls -> (ls |> String.concat "\n") + "\n"

            tsFieldObject idl disc [ tsDiscKey disc, SourceLit.tsString c.Tag ] c.Fields
            |> Result.map (fun obj -> ann + "    case " + SourceLit.tsString c.Tag + ": return " + obj + ";")

        let taggedR =
            u.Cases
            |> List.map arm
            |> concatR "\n"
            |> Result.map (fun arms ->
                "  if (isTagged(j)) {\n    const fs = j;\n    switch ("
                + tsDiscProp disc "j"
                + ") {\n"
                + arms
                + "\n      default: return dFail("
                + SourceLit.tsString ("unknown " + u.Name + " case: ")
                + " + "
                + tsDiscProp disc "j"
                + ");\n    }\n  }")

        // A transparent union also accepts its single-field case bare, and re-wraps
        // it so the encoder can flatten it back to the same bytes.
        let untagged =
            match TransparentUnion.tag tokens u with
            | Some ttag ->
                match u.Cases |> List.tryFind (fun c -> c.Tag = ttag) with
                | Some({ Fields = [ f ] }) ->
                    tsDecFn f.Type
                    |> Result.map (fun dfn ->
                        "  return { "
                        + tsDiscKey disc
                        + ": "
                        + SourceLit.tsString ttag
                        + ", "
                        + SourceLit.tsString f.Name
                        + ": "
                        + dfn
                        + "(j) };")
                | _ -> Error(transparentArity u.Name ttag)
            | None ->
                Ok(
                    "  return dFail("
                    + SourceLit.tsString ("expected a " + u.Name + " object")
                    + ");"
                )

        taggedR
        |> Result.bind (fun tagged ->
            untagged
            |> Result.map (fun untagged ->
                (u.Cases
                 |> List.map (fun c -> tsFieldAnnotationHeader (u.Name + "." + c.Tag) c.Fields)
                 |> String.concat "")
                + "function dec"
                + u.Name
                + "("
                + argList
                + ") {\n"
                + tagged
                + "\n"
                + untagged
                + "\n}"))

    let tsRecordDecoder (idl: Idl) (disc: string) (r: IdlRecord) =
        tsFieldObject idl disc [] r.Fields
        |> Result.map (fun obj ->
            tsFieldAnnotationHeader r.Name r.Fields
            + "function dec"
            + r.Name
            + "(j) {\n  const fs = dObj(j);\n  return "
            + obj
            + ";\n}")

    let tsSpecDecoder (idl: Idl) (disc: string) (k: IdlKind) =
        tsFieldObject idl disc [ tsDiscKey disc, SourceLit.tsString k.Tag ] k.Fields
        |> Result.map (fun obj ->
            tsKindAnnotationHeader k
            + "function dec"
            + k.Tag
            + "Spec(j) {\n  const fs = dObj(j);\n  return "
            + obj
            + ";\n}")

    /// The decode runtime prelude — the JS mirror of the F# `dObj`/`dReq`/… helpers.
    /// `isTagged` tests the DECLARED discriminator (Phase 108); the default key
    /// interpolates to exactly the pre-declarable bytes.
    let tsDecodePrelude (disc: string) =
        "const dFail = (m) => { throw new Error(m); };\n"
        + "const isTagged = (j) => j !== null && typeof j === 'object' && !Array.isArray(j) && "
        + SourceLit.tsStringSingle disc
        + " in j;"
        + """
const dObj = (j) => (j !== null && typeof j === 'object' && !Array.isArray(j)) ? j : dFail('expected an object');
const dStr = (j) => (typeof j === 'string') ? j : dFail('expected a string');
// An int slot is the interpreter's 32-bit int: an integral number outside it is refused here as
// the interpreter and the compiled F# host refuse it (Phase 304), never read as a wider value.
const dInt = (j) => (typeof j === 'number' && Number.isInteger(j) && j >= -2147483648 && j <= 2147483647) ? j : dFail('expected an int');
// §7 — a float slot also accepts the three quoted non-finite sentinels `encFloat` emits
// (§5), and decodes them to the NUMBER, never the string. `dInt` above is not widened:
// §7 stops at the float slot.
const dFloat = (j) => {
  if (typeof j === 'number') return j;
  if (j === 'NaN') return NaN;
  if (j === 'Infinity') return Infinity;
  if (j === '-Infinity') return -Infinity;
  return dFail('expected a number');
};
const dBool = (j) => (typeof j === 'boolean') ? j : dFail('expected a bool');
const dList = (dec) => (j) => Array.isArray(j) ? j.map(dec) : dFail('expected an array');
const dMap = (dec) => (j) => {
  const o = dObj(j);
  const out = {};
  for (const k of Object.keys(o)) out[k] = dec(o[k]);
  return out;
};
const dEnum = (name, cases) => (j) =>
  (typeof j === 'string' && cases.indexOf(j) >= 0) ? j : dFail('not a ' + name);
const dReq = (name, fs, dec) => (name in fs) ? dec(fs[name]) : dFail("missing required field '" + name + "'");
const dOpt = (name, fs, dec) => (name in fs) ? dec(fs[name]) : undefined;
const dDef = (name, fs, dec, dflt) => (name in fs) ? dec(fs[name]) : dflt;
// An optional closure/opaque field: the value is a sentinel carrying nothing, but
// its PRESENCE distinguishes present-from-absent and must survive the round trip.
const dPresent = (name, fs) => (name in fs) ? null : undefined;"""

    /// Phase 252 — the TypeScript check of a hosted slot's declared FORMAT, emitted only when
    /// the vocabulary declares one ([[declaresHostedFormat]]); the JS face of
    /// [[HostedFormat.admits]], held to the same answers by a test. Returns the value, so a
    /// decoder composes it in place.
    let tsFormatHelper =
        """// Phase 252 — a hosted slot's declared string format, checked as the F# host checks it.
const dFormatDay = (y, m, d) => {
  if (y < 1 || m < 1 || m > 12 || d < 1) return false;
  const last = new Date(0);
  last.setUTCFullYear(y, m, 0);
  return d <= last.getUTCDate();
};
const dFormat = (format, v) => {
  let ok = false;
  if (typeof v === 'string') {
    if (format === 'date') {
      const m = /^([0-9]{4})-([0-9]{2})-([0-9]{2})$/.exec(v);
      ok = m !== null && dFormatDay(+m[1], +m[2], +m[3]);
    } else if (format === 'date-time') {
      const m = /^([0-9]{4})-([0-9]{2})-([0-9]{2})[Tt]([0-9]{2}):([0-9]{2}):([0-9]{2})([.][0-9]+)?([Zz]|[+-]([0-9]{2}):([0-9]{2}))$/.exec(v);
      ok = m !== null && dFormatDay(+m[1], +m[2], +m[3]) && +m[4] <= 23 && +m[5] <= 59 && +m[6] <= 59
        && (m[9] === undefined || (+m[9] <= 23 && +m[10] <= 59));
    } else if (format === 'uuid') {
      ok = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/.test(v);
    }
  }
  return ok ? v : dFail("expected a '" + format + "' string");
};"""

    /// Emit a self-contained TypeScript (ESM) structural encoder for the named
    /// kinds — `encodeNode(n)` returns canonical wire byte-identical to the F#
    /// generated `encodeNode`. Plain JS string-building: no FSharp.Core, no .NET,
    /// no imports — a genuinely independent host.
    ///
    /// **Phase 124 — this returns a `Result` now, and the channel is the point.** The F# backend
    /// has refused an unemittable construct since the IDL's first codegen leg; this one had no
    /// error case anywhere, so an unrenderable default here could only be silently absorbed —
    /// the encoder emitted the key unconditionally and the decoder demanded it, in an artefact
    /// whose own IDL said the slot was omitted at its default. A backend that emits source for a
    /// declaration it cannot honour is worse than one that refuses.
    let typescriptModule (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        let kinds =
            kindTags
            |> List.choose (fun t -> idl.Kinds |> List.tryFind (fun k -> k.Tag = t))

        let _, unions, _ = referenced idl kinds

        // Runtime prelude — escaping mirrors Fuaran.Core.Canon.escape (WIRE_FORMAT §2
        // rule 6: only " and \ and control chars as \u00xx — NO \n/\r/\t shortcuts),
        // and object-field order is author order (Canon.render does not sort keys),
        // so the bytes match the F# host across ALL strings (incl. control chars).
        let prelude =
            """// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen (Phase 317 increment 8 — TS backend). Do not edit by hand.
const encStr = (s) => {
  let out = '"';
  for (const ch of s) {
    const code = ch.codePointAt(0);
    if (ch === '"') out += '\\"';
    else if (ch === '\\') out += '\\\\';
    else if (code < 0x20) out += '\\u' + code.toString(16).padStart(4, '0');
    else out += ch;
  }
  return out + '"';
};
const encInt = (n) => String(n);
const encBool = (b) => (b ? 'true' : 'false');
// §2 rule 5 — floats render in the .NET `ToString("R")` LAYOUT, which is not
// what JS `String(x)` produces: JS uses a lowercase `e`, an unsigned exponent,
// and a wider fixed-point threshold. The shortest-round-trip DIGITS agree (both
// .NET Core 3.0+ and V8 emit them); only the layout differs, so this normalises
// layout without touching the digits.
const formatFiniteDouble = (n) => {
  if (n === 0) return '0';
  const neg = n < 0;
  const s = Math.abs(n).toString();
  let digits;
  let exp;
  const eIdx = s.indexOf('e');
  if (eIdx >= 0) {
    const mant = s.slice(0, eIdx);
    const mantExp = parseInt(s.slice(eIdx + 1), 10);
    const dot = mant.indexOf('.');
    if (dot < 0) {
      digits = mant;
      exp = mantExp + (mant.length - 1);
    } else {
      digits = mant.slice(0, dot) + mant.slice(dot + 1);
      exp = mantExp + (dot - 1);
    }
  } else {
    const dot = s.indexOf('.');
    if (dot < 0) {
      digits = s;
      exp = s.length - 1;
    } else {
      const intPart = s.slice(0, dot);
      const fracPart = s.slice(dot + 1);
      if (intPart === '0') {
        const leadingZeros = fracPart.length - fracPart.replace(/^0+/, '').length;
        digits = fracPart.slice(leadingZeros);
        exp = -(leadingZeros + 1);
      } else {
        digits = intPart + fracPart;
        exp = intPart.length - 1;
      }
    }
  }
  digits = digits.replace(/0+$/, '') || '0';
  let out;
  if (exp >= -4 && exp <= 16) {
    if (exp >= 0) {
      out =
        digits.length <= exp + 1
          ? digits + '0'.repeat(exp + 1 - digits.length)
          : digits.slice(0, exp + 1) + '.' + digits.slice(exp + 1);
    } else {
      out = '0.' + '0'.repeat(-exp - 1) + digits;
    }
  } else {
    const mantissa = digits.length === 1 ? digits : digits[0] + '.' + digits.slice(1);
    out = mantissa + 'E' + (exp >= 0 ? '+' : '-') + Math.abs(exp).toString().padStart(2, '0');
  }
  return neg ? '-' + out : out;
};
const encFloat = (n) => {
  if (Number.isNaN(n)) return '"NaN"';
  if (n === Infinity) return '"Infinity"';
  if (n === -Infinity) return '"-Infinity"';
  return formatFiniteDouble(n);
};
// Phase 676 — arbitrary JSON rendered CANONICALLY: keys Ordinal-sorted, strings
// through the same escaper, numbers through the same float layout. Reusing those
// three is what stops a passthrough drifting from the rest of the wire.
const encJson = (v) => {
  if (v === null || v === undefined) return 'null';
  if (typeof v === 'string') return encStr(v);
  if (typeof v === 'boolean') return encBool(v);
  // Phase 303 — a SAFE integer only: a whole value at or past 2^53 takes the float layout,
  // where the canonical form switches to an exponent (1E+17) and String() does not (1e+21 at
  // the earliest, digits before that), so the verbatim passthrough wrote other bytes.
  if (typeof v === 'number') return Number.isSafeInteger(v) ? encInt(v) : encFloat(v);
  if (Array.isArray(v)) return '[' + v.map(encJson).join(',') + ']';
  const keys = Object.keys(v).sort();
  return '{' + keys.map((k) => encStr(k) + ':' + encJson(v[k])).join(',') + '}';
};
// Phase 698 — the Ordinal key sort is done HERE, at emission, not left to the
// caller's declaration order. `Canon.render` sorts every object's keys Ordinal on
// the F# side unconditionally, and JS `<` on strings compares UTF-16 code units,
// which is the same order — so sorting here is what makes the two hosts agree by
// construction. It previously relied on "pairs arrive Ordinal by convention", and
// the convention did not hold: the full-vocabulary sweep's very first vector
// diverged on `TextSource.I18n`, declared `key` then `args` and therefore emitted
// in that order against F#'s `args` then `key`. Every union case, spec and record
// whose fields are not already declared alphabetically had the same defect; no
// fixed fixture and no 8-kind sampled vector had ever contained one.
const ordinal = (a, b) => (a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0);"""
            // Phase 108 — `typed` writes the DECLARED discriminator. `$type`
            // (0x24) sorts before every data key, so the default's disc-first
            // build IS canonical order and interpolates to exactly the
            // pre-declarable bytes; any other key must be SORTED into place
            // among the pairs, or the emission diverges from `Canon.render`.
            //
            // Phase 111 — under DECLARED key order there is no sort at all: the
            // pairs arrive in declaration order and the discriminator leads, so
            // `typed` / `plain` preserve construction order, exactly as the F#
            // side's `Canon.renderOrdered` does.
            + (match idl.Wire.KeyOrder with
               | KeyOrder.Declared ->
                   // The canonical key text is computed HERE and spliced as one escaped
                   // literal, so the emitted prefix for a plain key is the bytes it always was.
                   "\nconst typed = (tag, pairs) =>\n  "
                   + SourceLit.tsStringSingle ("{" + Canon.render (JStr idl.Wire.Discriminator) + ":")
                   + " + encStr(tag) + pairs.filter((p) => p !== null).map(([k, v]) => ',' + encStr(k) + ':' + v).join('') + '}';\n"
               | KeyOrder.Sorted when idl.Wire.Discriminator = "$type" ->
                   "\nconst typed = (tag, pairs) =>\n  '{\"$type\":' + encStr(tag) + pairs.filter((p) => p !== null).sort(ordinal).map(([k, v]) => ',' + encStr(k) + ':' + v).join('') + '}';\n"
               | KeyOrder.Sorted ->
                   "\nconst typed = (tag, pairs) =>\n  plain([["
                   + SourceLit.tsStringSingle idl.Wire.Discriminator
                   + ", encStr(tag)]].concat(pairs));\n")
            + (match idl.Wire.KeyOrder with
               | KeyOrder.Sorted ->
                   """// `typed` without the discriminator: a plain object (a non-discriminated record, or
// the node envelope).
const plain = (pairs) =>
  '{' + pairs.filter((p) => p !== null).sort(ordinal).map(([k, v]) => encStr(k) + ':' + v).join(',') + '}';"""
               | KeyOrder.Declared ->
                   """// `typed` without the discriminator: a plain object (a non-discriminated record, or
// the node envelope). Declared key order — construction order is preserved.
const plain = (pairs) =>
  '{' + pairs.filter((p) => p !== null).map(([k, v]) => encStr(k) + ':' + v).join(',') + '}';""")

        let disc = idl.Wire.Discriminator
        let flat = idl.Wire.NodeEnvelope = NodeEnvelopeShape.FlatKind

        // Phase 111 — a TJson value under declared order is carried in its
        // authored order (the map encoder, a different receiver, stays sorted).
        let prelude =
            match idl.Wire.KeyOrder with
            | KeyOrder.Sorted -> prelude
            | KeyOrder.Declared ->
                prelude.Replace("const keys = Object.keys(v).sort();", "const keys = Object.keys(v);")

        let kindDispatch =
            if not flat then
                let arms =
                    kinds
                    |> List.map (fun k -> "    case " + SourceLit.tsString k.Tag + ": return enc" + k.Tag + "Spec(k);")
                    |> String.concat "\n"

                // Phase 690 — `id` / `kind` / the envelope, merged and sorted Ordinal so
                // the TS emission order matches F#'s canonical key sort. With no envelope
                // this is `id` then `kind`, i.e. exactly the previous hand-built literal.
                // Phase 111 — declared order keeps the construction order instead:
                // id, kind, then the envelope as declared.
                idl.NodeFields
                |> List.map (fun f -> tsSpecPieceOf idl disc "n" f |> Result.map (fun p -> f.Name, p))
                |> sequenceR
                |> Result.map (fun envelopePairs ->
                    let nodePairsUnsorted =
                        ("id", "[\"id\", encStr(n.id)]")
                        :: ("kind", "[\"kind\", encKind(n.kind)]")
                        :: envelopePairs

                    let nodePairs =
                        (match idl.Wire.KeyOrder with
                         | KeyOrder.Sorted ->
                             nodePairsUnsorted
                             |> List.sortWith (fun (a, _) (b, _) -> System.String.CompareOrdinal(a, b))
                         | KeyOrder.Declared -> nodePairsUnsorted)
                        |> List.map snd
                        |> String.concat ", "

                    "function encKind(k) {\n  switch ("
                    + tsDiscProp disc "k"
                    + ") {\n"
                    + arms
                    + "\n  }\n}\n\nfunction encodeNode(n) {\n  return plain(["
                    + nodePairs
                    + "]);\n}")
            else
                // Phase 109 — the FLAT shape: the in-memory node IS the flat wire
                // object, so the kind pairs are read from the node itself and
                // `typed` merges the discriminator, the id, the kind fields and the
                // envelope into the one object — in that order, which is the Phase
                // 111 declared order (under Sorted rendering `typed` re-sorts, so
                // the order is free there).
                let arms =
                    kinds
                    |> List.map (fun k ->
                        "    case "
                        + SourceLit.tsString k.Tag
                        + ": return enc"
                        + k.Tag
                        + "SpecPairs(n);")
                    |> String.concat "\n"

                let envelopeConcat =
                    match idl.NodeFields with
                    | [] -> Ok ""
                    | fields ->
                        fields
                        |> List.map (tsSpecPieceOf idl disc "n")
                        |> concatR ", "
                        |> Result.map (fun pieces -> ".concat([" + pieces + "])")

                envelopeConcat
                |> Result.map (fun envelope ->
                    "function encKindPairs(n) {\n  switch ("
                    + tsDiscProp disc "n"
                    + ") {\n"
                    + arms
                    + "\n  }\n}\n\nfunction encodeNode(n) {\n  return typed("
                    + tsDiscProp disc "n"
                    + ", [[\"id\", encStr(n.id)]].concat(encKindPairs(n))"
                    + envelope
                    + ");\n}")

        let enums, _, records = referenced idl kinds

        let kindDecodeDispatch =
            let arms =
                kinds
                |> List.map (fun k -> "    case " + SourceLit.tsString k.Tag + ": return dec" + k.Tag + "Spec(j);")
                |> String.concat "\n"

            let decKind =
                "function decKind(j) {\n  if (!isTagged(j)) return dFail('expected a kind object');\n  switch ("
                + tsDiscProp disc "j"
                + ") {\n"
                + arms
                + "\n    default: return dFail('unknown node kind: ' + "
                + tsDiscProp disc "j"
                + ");\n  }\n}\n\n"

            let envelopeDecoded =
                idl.NodeFields
                |> List.map (fun f ->
                    tsDecField idl disc f
                    |> Result.map (fun e -> ", " + SourceLit.tsKey f.Name + ": " + e))
                |> concatR ""

            let decNode =
                envelopeDecoded
                |> Result.map (fun envelope ->
                    if not flat then
                        "function decNode(j) {\n  const fs = dObj(j);\n  return { id: dReq('id', fs, dStr), kind: dReq('kind', fs, decKind)"
                        + envelope
                        + " };\n}\n\n"
                    else
                        // Phase 109 — flat: the node object is the kind body; the id
                        // (and envelope) merge beside the decoded spec's own keys.
                        "function decNode(j) {\n  const fs = dObj(j);\n  return Object.assign({}, decKind(j), { id: dReq('id', fs, dStr)"
                        + envelope
                        + " });\n}\n\n")

            decNode
            |> Result.map (fun decNode ->
                decKind
                + decNode
                + "// Structural decode. The policy layer (diagnostics, §16 lenient-accept, the\n"
                + "// reject set) composes ABOVE this — see the Phase 672 note in the generator.\n"
                + "function decodeNode(s) {\n  try {\n    return { ok: true, value: decNode(JSON.parse(s)) };\n  } catch (e) {\n    return { ok: false, error: String(e && e.message ? e.message : e) };\n  }\n}")

        [ [ Ok prelude ]
          records |> List.map (tsRecordEncoder idl disc)
          unions |> List.map (tsUnionEncoder idl disc idl.Harden)
          kinds |> List.map (tsSpecEncoder idl disc flat)
          [ kindDispatch ]
          Ok(tsDecodePrelude disc)
          :: (if declaresHostedFormat idl then
                  [ Ok tsFormatHelper ]
              else
                  [])
          enums |> List.map (tsEnumDecoder >> Ok)
          records |> List.map (tsRecordDecoder idl disc)
          unions |> List.map (tsUnionDecoder idl disc idl.Harden)
          kinds |> List.map (tsSpecDecoder idl disc)
          [ kindDecodeDispatch ]
          [ Ok "export { encodeNode, decodeNode };" ] ]
        |> List.concat
        |> concatR "\n\n"
        // Phase 129 — the emitted TypeScript is LF-terminated whatever this generator was built
        // from; the three preludes above are multi-line templates and would otherwise carry the
        // line endings of whichever checkout compiled them. See [[normalizeEol]].
        |> Result.map normalizeEol

    // -----------------------------------------------------------------------
    // Phase 252 — the TypeScript scaffold leg in the DECODER's shape, and the
    // module's type declarations.
    // -----------------------------------------------------------------------

    /// Phase 252 — a TypeScript value literal for an authored `IdlValue` of type `t`, in
    /// the SHAPE the generated decoder produces, which is the shape the generated encoder
    /// reads: every omit-at-default member is PRESENT, filled from its declared default
    /// as [[fsharpValue]] fills it; an absent optional and a host-only member are absent.
    ///
    /// BREAKING (Phase 252): it took the value alone (`typescriptValue v : string`), and a value
    /// alone cannot know a default, so a node it scaffolded left such a member `undefined` and
    /// the encoder then wrote `"quantity":undefined` (not JSON) or a default the wire omits.
    /// A caller passes the vocabulary and the slot's type (`TNode` for a node) and handles the
    /// [[CodegenError]] a value that does not fit its declaration now raises.
    ///
    /// Walks the IDL rather than the value, so a value that does not fit its declaration
    /// is refused (a [[CodegenError]]) rather than emitted. Respects the declared wire
    /// shape (discriminator key, node envelope) exactly as [[typescriptValueWith]] does.
    let typescriptValue (idl: Idl) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> =
        let disc = tsDiscKey idl.Wire.Discriminator

        let mismatch (what: string) =
            Error(
                CodegenError.UnsupportedConstruct(
                    what,
                    "GP5: the scaffold leg names what it cannot construct rather than emitting a value the encoder mis-writes",
                    "author the value against the vocabulary's declaration (Encode.encode refuses the same value)"
                )
            )

        let objectOf (pairs: (string * string) list) =
            "{ "
            + (pairs |> List.map (fun (k, e) -> k + ": " + e) |> String.concat ", ")
            + " }"

        let rec go (subst: Map<string, IdlType>) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> =
            match TypeParams.substitute subst t, v with
            | TStr, VStr s -> Ok(SourceLit.tsString s)
            | TInt, VInt i -> Ok(string i)
            | TBool, VBool b -> Ok(if b then "true" else "false")
            | TFloat, VFloat f -> Ok(invariantFloat f)
            | TFloat, VInt i -> Ok(string i)
            | TEnum n, VEnum wire ->
                match idl.Enums |> List.tryFind (fun e -> e.Name = n) with
                | Some e when List.contains wire e.WireCases -> Ok(SourceLit.tsString wire)
                | _ -> mismatch (sprintf "the wire string %A at enum '%s'" wire n)
            | TList inner, VList xs ->
                xs
                |> List.map (go subst inner)
                |> sequenceR
                |> Result.map (fun items -> "[" + String.concat ", " items + "]")
            | TMap vt, VMap entries ->
                entries
                |> List.map (fun (k, ev) -> go subst vt ev |> Result.map (fun e -> SourceLit.tsString k, e))
                |> sequenceR
                |> Result.map objectOf
            // A hosted slot that declares its wire form takes that type's decoded shape (a
            // record's omit-at-default members filled, as for any record); the JSON is read
            // through the interpreter's decoder, so a value outside the form is refused.
            | THosted { Wire = Some w }, VJson j ->
                match Decode.value idl w j with
                | Ok wv -> go subst w wv
                | Error m -> mismatch (sprintf "a hosted value outside its declared wire form (%s)" m)
            | (TJson | THosted _), VJson j -> Ok(Canon.render j)
            // The encoder never reads a sentinel slot; a PRESENT stand-in keeps an optional
            // one's presence test honest (see [[typescriptValueWith]]).
            | (TClosure | TFn _ | TOpaque), (VClosure | VOpaque) -> Ok "(() => undefined)"
            | TRecord n, VRecord authored ->
                match idl.Records |> List.tryFind (fun r -> r.Name = n) with
                | None -> mismatch (sprintf "a value of the undeclared record '%s'" n)
                | Some r -> members subst ("record '" + n + "'") r.Fields authored |> Result.map objectOf
            | TUnion(n, args), VUnion(tag, authored) ->
                match idl.Unions |> List.tryFind (fun u -> u.Name = n) with
                | None -> mismatch (sprintf "a value of the undeclared union '%s'" n)
                | Some u when List.length u.Params <> List.length args ->
                    mismatch (sprintf "union '%s' applied to %d type arguments" n (List.length args))
                | Some u ->
                    match u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                    | None -> mismatch (sprintf "the case '%s', which union '%s' does not declare" tag n)
                    | Some c ->
                        let caseSubst = TypeParams.bind u args |> Option.defaultValue Map.empty

                        members caseSubst (sprintf "union case '%s.%s'" n tag) c.Fields authored
                        |> Result.map (fun ms -> objectOf ((disc, SourceLit.tsString tag) :: ms))
            | TNode, VNode(id, kindTag, fields) -> node id [] kindTag fields
            | TNode, VNodeEnv(id, envelope, kindTag, fields) -> node id envelope kindTag fields
            | _, VAbsent -> mismatch "an absent value (VAbsent) in a value position"
            | t', _ -> mismatch (sprintf "a value that does not match IDL type %A" t')

        /// The members of an object under its declared fields' presence rules.
        and members
            (subst: Map<string, IdlType>)
            (where: string)
            (declared: IdlField list)
            (authored: (string * IdlValue) list)
            : Result<(string * string) list, CodegenError> =
            declared
            |> List.map (fun f ->
                let value =
                    authored
                    |> List.tryPick (fun (n, av) -> if n = f.Name && av <> VAbsent then Some av else None)

                match f.Opt, value with
                // Never on the wire, and `undefined` in the decoder's shape.
                | HostOnly, _ -> Ok None
                | _, Some av -> go subst f.Type av |> Result.map (fun e -> Some(SourceLit.tsKey f.Name, e))
                | Optional, None -> Ok None
                | OmitDefault d, None -> go subst f.Type d |> Result.map (fun e -> Some(SourceLit.tsKey f.Name, e))
                | Required, None -> mismatch (sprintf "%s without its required field '%s'" where f.Name))
            |> sequenceR
            |> Result.map (List.choose id)

        and node (id: string) envelope (kindTag: string) fields : Result<string, CodegenError> =
            match idl.Kinds |> List.tryFind (fun k -> k.Tag = kindTag) with
            | None -> mismatch (sprintf "a node of the undeclared kind '%s'" kindTag)
            | Some k ->
                match
                    members Map.empty ("kind '" + kindTag + "'") k.Fields fields,
                    members Map.empty "node envelope" idl.NodeFields envelope
                with
                | Error e, _
                | _, Error e -> Error e
                | Ok kindMembers, Ok envMembers ->
                    let tagged = (disc, SourceLit.tsString kindTag)

                    match idl.Wire.NodeEnvelope with
                    | NodeEnvelopeShape.NestedKind ->
                        Ok(
                            objectOf (
                                (("id", SourceLit.tsString id) :: envMembers)
                                @ [ "kind", objectOf (tagged :: kindMembers) ]
                            )
                        )
                    | NodeEnvelopeShape.FlatKind ->
                        Ok(objectOf ((tagged :: ("id", SourceLit.tsString id) :: envMembers) @ kindMembers))

        go Map.empty t v

    /// Phase 252 — TypeScript TYPE DECLARATIONS for the module [[typescriptModule]] emits
    /// over the same kinds: one declaration per enum, record, union and kind spec the
    /// module reaches, the `Node` type, and the signatures of `encodeNode` / `decodeNode`.
    /// Written beside the module (`generated.mjs` + `generated.d.mts`), it is what lets a
    /// TypeScript consumer's compiler catch a scaffolded value of the wrong shape — the
    /// module is untyped JavaScript, so nothing did.
    ///
    /// The types describe the DECODER's shape, which is the one the encoder reads: an
    /// omit-at-default member is present (not optional), an optional member is `?:`, a
    /// host-only member is `?:` (it is never on the wire), an enum is the union of its wire
    /// strings, and a sentinel, JSON or hosted slot is `unknown` (the TypeScript tier
    /// carries a hosted slot's JSON verbatim).
    let typescriptDeclarations (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        let kinds =
            kindTags
            |> List.choose (fun t -> idl.Kinds |> List.tryFind (fun k -> k.Tag = t))

        let enums, unions, records = referenced idl kinds
        let disc = tsDiscKey idl.Wire.Discriminator

        let rec tsType (t: IdlType) : Result<string, CodegenError> =
            match t with
            | TStr -> Ok "string"
            | TInt
            | TFloat -> Ok "number"
            | TBool -> Ok "boolean"
            | TEnum n
            | TRecord n
            | TUnion(n, []) -> Ok n
            | TUnion(n, args) ->
                args
                |> List.map tsType
                |> concatR ", "
                |> Result.map (fun a -> n + "<" + a + ">")
            | TVar v -> Ok v
            | TNode -> Ok "Node"
            | TList inner -> tsType inner |> Result.map (fun s -> "Array<" + s + ">")
            | TMap vt -> tsType vt |> Result.map (fun s -> "{ [key: string]: " + s + " }")
            // A hosted slot that declares its wire form IS that type on this side.
            | THosted { Wire = Some w } -> tsType w
            | TJson
            | THosted _
            | TClosure
            | TFn _
            | TOpaque -> Ok "unknown"
            | TKind
            | TOp -> Error(opVocabularySlot "the TypeScript declaration backend" t)

        let memberDecl (f: IdlField) : Result<string, CodegenError> =
            tsType f.Type
            |> Result.map (fun ty ->
                match f.Opt with
                | Optional
                | HostOnly -> SourceLit.tsKey f.Name + "?: " + ty
                | Required
                | OmitDefault _ -> SourceLit.tsKey f.Name + ": " + ty)

        let objectType (extra: string list) (fields: IdlField list) : Result<string, CodegenError> =
            fields
            |> List.map memberDecl
            |> sequenceR
            |> Result.map (fun ms ->
                match extra @ ms with
                | [] -> "{}"
                | all -> "{ " + String.concat "; " all + " }")

        let generic (ps: string list) =
            match ps with
            | [] -> ""
            | _ -> "<" + String.concat ", " ps + ">"

        let orNever (xs: string list) =
            match xs with
            | [] -> "never"
            | _ -> String.concat " | " xs

        let enumDecls =
            enums
            |> List.map (fun e ->
                Ok(
                    "export type "
                    + e.Name
                    + " = "
                    + orNever (e.WireCases |> List.map SourceLit.tsString)
                    + ";"
                ))

        let recordDecls =
            records
            |> List.map (fun r ->
                objectType [] r.Fields
                |> Result.map (fun o -> "export type " + r.Name + " = " + o + ";"))

        let unionDecls =
            unions
            |> List.map (fun u ->
                u.Cases
                |> List.map (fun c -> objectType [ disc + ": " + SourceLit.tsString c.Tag ] c.Fields)
                |> sequenceR
                |> Result.map (fun cases -> "export type " + u.Name + generic u.Params + " = " + orNever cases + ";"))

        let specDecls =
            kinds
            |> List.map (fun k ->
                objectType [ disc + ": " + SourceLit.tsString k.Tag ] k.Fields
                |> Result.map (fun o -> "export type " + k.Tag + "Spec = " + o + ";"))

        let envelope = objectType [ "id: string" ] idl.NodeFields
        let specs = kinds |> List.map (fun k -> k.Tag + "Spec")

        let nodeDecl =
            envelope
            |> Result.map (fun env ->
                match idl.Wire.NodeEnvelope with
                | NodeEnvelopeShape.NestedKind ->
                    "export type NodeKind = "
                    + orNever specs
                    + ";\n"
                    + "export type Node = "
                    + env.Substring(0, env.Length - 2)
                    + "; kind: NodeKind };"
                | NodeEnvelopeShape.FlatKind ->
                    "export type Node = "
                    + orNever (specs |> List.map (fun s -> "(" + s + " & " + env + ")"))
                    + ";")

        [ [ Ok(
                "// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen (Phase 252 — type declarations for the TS backend). Do not edit by hand."
            ) ]
          enumDecls
          recordDecls
          unionDecls
          specDecls
          [ nodeDecl ]
          [ Ok(
                "export declare function encodeNode(n: Node): string;\n"
                + "export declare function decodeNode(s: string): { ok: true; value: Node } | { ok: false; error: string };"
            ) ] ]
        |> List.concat
        |> concatR "\n\n"
        |> Result.map (fun s -> normalizeEol s + "\n")
