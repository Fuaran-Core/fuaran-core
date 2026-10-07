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
            match IdlLookup.tryRecord idl n with
            | None -> refuse ()
            | Some r ->
                r.Fields
                |> List.map (fun rf -> tsIsDefaultField idl disc src Map.empty rf authored)
                |> concatR " && "
        | TUnion(n, args), VUnion(tag, authored) ->
            match IdlLookup.tryUnion idl n with
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
    // closure/opaque sentinels (which carry nothing, but are read by value — Phase 347).

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
        // The value carries nothing, so it decodes to `null`; but it is READ (Phase 347): the
        // slot holds its one sentinel string, as the interpreter checks. A `TFn` slot is the same
        // on the wire — the TS tier has no `'Msg` to rebuild into, so it stays `null` there
        // regardless of the declared signature.
        | TClosure
        | TFn _ -> Ok "dSentinel('<closure>')"
        | TOpaque -> Ok "dSentinel('<opaque>')"
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
        // Phase 347 — a sentinel slot is read like any other: a required one absent is
        // `MissingField`, a present one is checked BY VALUE (`dSentinel`), and an optional one's
        // presence is what decodes `null` rather than `undefined`. Until 347 a required slot was
        // not read at all and an optional one was read for its presence only, so both hosts
        // accepted documents the interpreter refuses.
        | TClosure
        | TFn _
        | TOpaque ->
            tsDecFn f.Type
            |> Result.map (fun d ->
                match f.Opt with
                | Required -> "dReq(" + key + ", fs, " + d + ")"
                | Optional -> "dOpt(" + key + ", fs, " + d + ")"
                | OmitDefault _ -> "dDef(" + key + ", fs, " + d + ", null)"
                | HostOnly -> "null")
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
                // Phase 337 — every object is a tagged one, as the interpreter reads it: an
                // absent or non-string discriminator is refused AT it (`dTag`), and an unknown
                // case is `UnknownTag` there.
                "  if (isObj(j)) {\n    const fs = j;\n    switch (dTag(j)) {\n"
                + arms
                + "\n      default: return dUnknown("
                + SourceLit.tsString (oneOf (u.Cases |> List.map (fun c -> c.Tag)))
                + ", "
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
                    "  return dFail('WrongKind', 'object', "
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
    /// `dTag` reads the DECLARED discriminator (Phase 108).
    ///
    /// Phase 337 — a refusal is thrown as a `DecodeFault` carrying the interpreter's CODE and
    /// EXPECTED, and its PATH grows as it leaves a member or an item (`dAt`); `decodeNode`
    /// answers it as `{ code, path, expected, message }`, `DecodeError.toJson`'s members. A
    /// member is read only when it is the object's OWN: `in` also finds a prototype's
    /// (`toString`, `constructor`), which reads an absent member as present.
    ///
    /// Phase 347 — a member or an item is read through `dRead`, which tells an int slot whether
    /// the reader ([[tsParseLeg]]) met that number as a FLOAT token; a map is decoded in document
    /// order into a null-prototype object, so a key is data whatever it spells; and a sentinel
    /// slot is read by value (`dSentinel`).
    let tsDecodePrelude (disc: string) =
        let discLit = SourceLit.tsStringSingle disc

        """// Phase 337 — a refusal: the code (the closed DecodeCode set), the path from the document root
// (member keys and item indices), what the position expected, and a sentence.
class DecodeFault extends Error {
  constructor(code, expected, message) {
    super(message);
    this.code = code;
    this.path = [];
    this.expected = expected;
  }
}
const dFail = (code, expected, m) => { throw new DecodeFault(code, expected, m); };
// One step further from the root — what a refusal gains as it leaves a member or an item.
const dAt = (step, f) => {
  try {
    return f();
  } catch (e) {
    if (e instanceof DecodeFault) e.path.unshift(step);
    throw e;
  }
};
const dMissing = (name, m) => {
  const f = new DecodeFault('MissingField', "a member '" + name + "'", m);
  f.path.push(name);
  throw f;
};
const hasOwn = (o, k) => Object.prototype.hasOwnProperty.call(o, k);
const isObj = (j) => j !== null && typeof j === 'object' && !Array.isArray(j);
// Phase 347 — what the reader (`dParse`) knows and a JS value cannot carry, kept beside the value
// for the decode that reads it: per object or array, the members and items whose WHOLE number was
// written as a float token (an int slot refuses it); and per object whose JS form loses its document
// order, its members as written — a repeated key (the object holds the first) or an index-like key
// (the object lists those first).
const dFloatAt = new WeakMap();
const dMembersOf = new WeakMap();
// Whether the value being decoded was read as a float token: set by `dRead` as each member or item
// is handed to its decoder, and read by `dInt` before any other value is.
let dFloatTok = false;
const dRead = (c, k) => {
  const at = dFloatAt.get(c);
  dFloatTok = at !== undefined && at.has(k);
  return c[k];
};
const dDisc = __DISC__;
// The discriminator: absent is MissingField naming it, a non-string WrongKind at it.
const dTag = (j) => {
  if (!hasOwn(j, dDisc)) return dMissing(dDisc, 'missing or non-string ' + dDisc);
  const t = j[dDisc];
  return (typeof t === 'string') ? t : dAt(dDisc, () => dFail('WrongKind', 'string', 'missing or non-string ' + dDisc));
};
// A tag naming no case this decoder knows: UnknownTag at the discriminator.
const dUnknown = (expected, m) => dAt(dDisc, () => dFail('UnknownTag', expected, m));
const dObj = (j) => isObj(j) ? j : dFail('WrongKind', 'object', 'expected an object');
const dStr = (j) => (typeof j === 'string') ? j : dFail('WrongKind', 'string', 'expected a string');
// An int slot is the interpreter's 32-bit int: an integral number outside it is refused here as
// the interpreter and the compiled F# host refuse it (Phase 304), never read as a wider value.
// Phase 347 — and only a number the reader met as an INTEGER token: `1.0` and `1e0` are float
// tokens, which the F# reader reads as a float and an int slot refuses, though their value is whole.
const dInt = (j) => (typeof j === 'number' && !dFloatTok && Number.isInteger(j) && j >= -2147483648 && j <= 2147483647) ? j : dFail('WrongKind', 'int', 'expected an int');
// §7 — a float slot also accepts the three quoted non-finite sentinels `encFloat` emits
// (§5), and decodes them to the NUMBER, never the string. `dInt` above is not widened:
// §7 stops at the float slot.
const dFloat = (j) => {
  if (typeof j === 'number') return j;
  if (j === 'NaN') return NaN;
  if (j === 'Infinity') return Infinity;
  if (j === '-Infinity') return -Infinity;
  return dFail('WrongKind', 'number', 'expected a number');
};
const dBool = (j) => (typeof j === 'boolean') ? j : dFail('WrongKind', 'bool', 'expected a bool');
const dList = (dec) => (j) => Array.isArray(j) ? j.map((_, i) => dAt(i, () => dec(dRead(j, i)))) : dFail('WrongKind', 'array', 'expected an array');
// Phase 347 — a map decodes into a NULL-PROTOTYPE object: a key is data whatever it spells, so
// `__proto__` is an entry like any other (on a plain object it replaced the prototype, and every
// later read of the map went through the document's object), and no key reads one the prototype
// has (`constructor`, `toString`). Its entries are read in DOCUMENT order, as the interpreter and
// the F# host read them — an object lists an index-like key first — and a repeated key keeps its
// first value, as every member read does, after each entry is checked.
const dMap = (dec) => (j) => {
  const o = dObj(j);
  const out = Object.create(null);
  const members = dMembersOf.get(o);
  if (members === undefined) {
    for (const k of Object.keys(o)) out[k] = dAt(k, () => dec(dRead(o, k)));
  } else {
    for (const [k, v, floatTok] of members) {
      const d = dAt(k, () => { dFloatTok = floatTok; return dec(v); });
      if (!hasOwn(out, k)) out[k] = d;
    }
  }
  return out;
};
const dEnum = (name, cases) => (j) => {
  if (typeof j !== 'string') return dFail('WrongKind', 'string', 'not a ' + name);
  return (cases.indexOf(j) >= 0) ? j : dFail('UnknownTag', 'one of ' + cases.map((c) => "'" + c + "'").join(', '), 'not a ' + name);
};
const dReq = (name, fs, dec) => hasOwn(fs, name) ? dAt(name, () => dec(dRead(fs, name))) : dMissing(name, "missing required field '" + name + "'");
const dOpt = (name, fs, dec) => hasOwn(fs, name) ? dAt(name, () => dec(dRead(fs, name))) : undefined;
const dDef = (name, fs, dec, dflt) => hasOwn(fs, name) ? dAt(name, () => dec(dRead(fs, name))) : dflt;
// Phase 347 — a closure / opaque slot holds one fixed sentinel string and is read BY VALUE, as the
// interpreter reads it: another string is `OutOfRange` (a string, but not the one value the slot
// takes), any other kind `WrongKind`. It decodes to `null` — the sentinel carries nothing — and an
// optional slot's presence is still what tells `null` from `undefined`.
const dSentinel = (sentinel) => (j) => {
  if (j === sentinel) return null;
  return (typeof j === 'string')
    ? dFail('OutOfRange', 'string', 'expected the sentinel ' + sentinel)
    : dFail('WrongKind', 'string', 'expected a string');
};"""
            .Replace("__DISC__", discLit)

    /// Phase 337 — the parse leg of the TypeScript `decodeNode`, held to the F# reader's
    /// answers (`Decoder.parse`): text the reader cannot parse is `InvalidJson` at the root, and
    /// a container opened past its nesting cap is `LimitExceeded` there.
    ///
    /// Phase 347 — the leg IS a reader now, the F# reader's twin (`Json.parse`), not `JSON.parse`
    /// behind a scan. `JSON.parse` answers five questions differently from it, and each answer
    /// reached the decode: it keeps the LAST of a repeated member (the F# reader keeps every
    /// member, and a member read takes the first); it reads `1.0` and `1e0` as the number `1`, so
    /// an int slot cannot see they were float tokens; it reads a literal past the double range as
    /// `Infinity` and an integer past 2^53 by rounding it, where the F# reader refuses both; it
    /// refuses a raw control character in a string and admits a lone surrogate, where the F#
    /// reader does the opposite; and it reads `null` and any depth. This reader holds the text to
    /// the F# grammar in the F# order — the first fault met is the one reported, so a malformed
    /// prefix still outranks a container past the cap — and it records, beside the value, what a
    /// JS value cannot carry (`dFloatAt`, `dMembersOf`). An object is built with every key as
    /// data: `__proto__` becomes an own member, never the object's prototype.
    let tsParseLeg =
        """const dMaxDepth = __CAP__;
const dParse = (s) => {
  const n = s.length;
  let i = 0;
  // Whether the value just read was a WHOLE number written as a float token.
  let floatTok = false;
  const bad = (m) => { throw new DecodeFault('InvalidJson', 'JSON text', 'not valid JSON: ' + m); };
  const ws = () => {
    while (i < n) {
      const c = s.charCodeAt(i);
      if (c === 32 || c === 9 || c === 10 || c === 13) i++;
      else break;
    }
  };
  const expect = (c) => { if (i < n && s[i] === c) i++; else bad("expected '" + c + "'"); };
  const markFloat = (c, k) => {
    let at = dFloatAt.get(c);
    if (at === undefined) {
      at = new Set();
      dFloatAt.set(c, at);
    }
    at.add(k);
  };
  // A string is well-formed UTF-16 or refused, whichever spelling a unit arrived in; any other
  // character, a control character included, is read as itself.
  const str = () => {
    expect('"');
    let out = '';
    let run = i;
    let high = false;
    const unit = (u) => {
      const low = u >= 0xDC00 && u <= 0xDFFF;
      if (high && !low) bad('ill-formed string: a high surrogate not followed by a low surrogate');
      if (!high && low) bad('ill-formed string: a low surrogate with no high surrogate before it');
      high = u >= 0xD800 && u <= 0xDBFF;
      out += String.fromCharCode(u);
    };
    for (;;) {
      if (i >= n) bad('unterminated string');
      const c = s.charCodeAt(i);
      if (!high && c !== 34 && c !== 92 && (c < 0xD800 || c > 0xDFFF)) {
        i++;
        continue;
      }
      out += s.slice(run, i);
      i++;
      if (c === 34) {
        if (high) bad('ill-formed string: a high surrogate not followed by a low surrogate');
        return out;
      }
      if (c !== 92) unit(c);
      else {
        if (i >= n) bad('unterminated escape');
        const e = s[i++];
        if (e === '"') unit(34);
        else if (e === '\\') unit(92);
        else if (e === '/') unit(47);
        else if (e === 'n') unit(10);
        else if (e === 'r') unit(13);
        else if (e === 't') unit(9);
        else if (e === 'b') unit(8);
        else if (e === 'f') unit(12);
        else if (e === 'u') {
          if (i + 4 > n) bad('truncated \\u escape');
          const h = s.slice(i, i + 4);
          if (!/^[0-9a-fA-F]{4}$/.test(h)) bad('bad hex digit in \\u escape');
          i += 4;
          unit(parseInt(h, 16));
        } else bad("bad escape '\\" + e + "'");
      }
      run = i;
    }
  };
  // The F# reader's number: the JSON grammar exactly; a float token finite; an integer token an
  // int within Int32, else a float while |n| <= 2^53, else only the canonical layout of its double
  // (which writes a 16- or 17-digit whole double in full, and every longer one with an exponent).
  const num = () => {
    const start = i;
    let isFloat = false;
    const digits = () => { while (i < n && s[i] >= '0' && s[i] <= '9') i++; };
    if (s[i] === '-') i++;
    digits();
    if (s[i] === '.') {
      isFloat = true;
      i++;
      digits();
    }
    if (s[i] === 'e' || s[i] === 'E') {
      isFloat = true;
      i++;
      if (s[i] === '+' || s[i] === '-') i++;
      digits();
    }
    const tok = s.slice(start, i);
    if (!/^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$/.test(tok)) bad('malformed number: ' + tok);
    const v = Number(tok);
    if (isFloat) {
      if (!Number.isFinite(v)) bad('number outside the finite double range; it cannot round-trip on the wire: ' + tok);
      floatTok = Number.isInteger(v) && v >= -2147483648 && v <= 2147483647;
      return v;
    }
    if (v >= -2147483648 && v <= 2147483647) return v;
    const mag = tok[0] === '-' ? tok.slice(1) : tok;
    if (mag.length < 16 || (mag.length === 16 && mag <= '9007199254740992')) return v;
    if (mag.length <= 17 && String(v) === tok) return v;
    return bad('integer literal outside the int53 safe range (|n| > 2^53); it cannot round-trip without precision loss: ' + tok);
  };
  const lit = (word, v) => {
    if (s.startsWith(word, i)) {
      i += word.length;
      return v;
    }
    return bad("expected '" + word + "'");
  };
  const deep = (depth) => {
    if (depth >= dMaxDepth) throw new DecodeFault('LimitExceeded', "nesting within the parser's cap", 'not valid JSON: max nesting depth ' + dMaxDepth + ' exceeded');
  };
  const obj = (depth) => {
    deep(depth);
    expect('{');
    ws();
    const o = {};
    const members = [];
    let reordered = false;
    if (s[i] === '}') {
      i++;
      return o;
    }
    for (;;) {
      ws();
      const k = str();
      ws();
      expect(':');
      ws();
      const v = val(depth + 1);
      const f = floatTok;
      members.push([k, v, f]);
      if (hasOwn(o, k)) reordered = true;
      else {
        if (k === '__proto__') Object.defineProperty(o, k, { value: v, writable: true, enumerable: true, configurable: true });
        else o[k] = v;
        if (f) markFloat(o, k);
        if (/^(0|[1-9][0-9]*)$/.test(k)) reordered = true;
      }
      ws();
      if (s[i] === ',') i++;
      else if (s[i] === '}') {
        i++;
        break;
      } else bad("expected ',' or '}'");
    }
    if (reordered) dMembersOf.set(o, members);
    return o;
  };
  const arr = (depth) => {
    deep(depth);
    expect('[');
    ws();
    const a = [];
    if (s[i] === ']') {
      i++;
      return a;
    }
    for (;;) {
      const v = val(depth + 1);
      if (floatTok) markFloat(a, a.length);
      a.push(v);
      ws();
      if (s[i] === ',') i++;
      else if (s[i] === ']') {
        i++;
        break;
      } else bad("expected ',' or ']'");
    }
    return a;
  };
  const val = (depth) => {
    ws();
    floatTok = false;
    if (i >= n) return bad('unexpected end of input');
    const c = s[i];
    let v;
    if (c === '"') v = str();
    else if (c === '{') v = obj(depth);
    else if (c === '[') v = arr(depth);
    else if (c === 't') v = lit('true', true);
    else if (c === 'f') v = lit('false', false);
    else if (c === 'n') return bad('null is not representable in the Fuaran wire JVal model');
    else if (c === '-' || (c >= '0' && c <= '9')) return num();
    else return bad("unexpected character '" + c + "'");
    floatTok = false;
    return v;
  };
  const root = val(0);
  ws();
  if (i !== n) bad('trailing characters');
  return root;
};"""
            .Replace("__CAP__", string Json.defaultMaxDepth)

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
  return ok ? v : dFail('OutOfRange', "a '" + format + "' string", "expected a '" + format + "' string");
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
        let kinds = kindTags |> List.choose (fun t -> IdlLookup.tryKind idl t)

        let _, unions, _ = referenced idl kinds

        // Runtime prelude — escaping mirrors Fuaran.Core.Canon.escape (WIRE_FORMAT §2
        // rule 6: only " and \ and control chars as \u00xx — NO \n/\r/\t shortcuts),
        // and object-field order is author order (Canon.render does not sort keys),
        // so the bytes match the F# host across ALL strings (incl. control chars).
        // Phase 370 — `encStr` mirrors Phase 365's fast path: one regex test for an escapable
        // character (`"`, `\`, U+0000–U+001F, matched per UTF-16 code unit, so a lone surrogate
        // is never escapable on either host) and the input returned whole between quotes when
        // there is none. The escaping loop below it is unchanged, so the bytes are too
        // (`StringEscapeTests` runs the emitted function against the .NET escaper under node).
        let prelude =
            """// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen (Phase 317 increment 8 — TS backend). Do not edit by hand.
const encStr = (s) => {
  if (!/["\\\u0000-\u001f]/.test(s)) return '"' + s + '"';
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
                "function decKind(j) {\n  if (!isObj(j)) return dFail('WrongKind', 'object', 'expected a kind object');\n  switch (dTag(j)) {\n"
                + arms
                + "\n    default: return dUnknown("
                + SourceLit.tsString (oneOf (kinds |> List.map (fun k -> k.Tag)))
                + ", 'unknown node kind: ' + "
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
                        // Phase 337 — the id is read BEFORE the kind, the order the
                        // interpreter and the compiled F# host read them in, so a node
                        // missing both is refused at the id by all three.
                        "function decNode(j) {\n  const fs = dObj(j);\n  const id = dReq('id', fs, dStr);\n  return Object.assign({}, decKind(j), { id: id"
                        + envelope
                        + " });\n}\n\n")

            decNode
            |> Result.map (fun decNode ->
                decKind
                + decNode
                + tsParseLeg
                + "\n\n// Structural decode. The policy layer (diagnostics, §16 lenient-accept, the\n"
                + "// reject set) composes ABOVE this — see the Phase 672 note in the generator.\n"
                + "// Phase 337 — a refusal is `{ code, path, expected, message }`: the code and path the IDL\n"
                + "// interpreter reports for the same document, and this layer's sentence.\n"
                + "function decodeNode(s) {\n  try {\n    const root = dParse(s);\n    dFloatTok = false;\n    return { ok: true, value: decNode(root) };\n  } catch (e) {\n    if (!(e instanceof DecodeFault)) throw e;\n    return { ok: false, error: { code: e.code, path: e.path, expected: e.expected, message: e.message } };\n  }\n}")

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
                match IdlLookup.tryEnum idl n with
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
                match IdlLookup.tryRecord idl n with
                | None -> mismatch (sprintf "a value of the undeclared record '%s'" n)
                | Some r -> members subst ("record '" + n + "'") r.Fields authored |> Result.map objectOf
            | TUnion(n, args), VUnion(tag, authored) ->
                match IdlLookup.tryUnion idl n with
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
            match IdlLookup.tryKind idl kindTag with
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

    /// The name the declaration file gives a decode refusal (Phase 348).
    let private refusalTypeName = "DecodeRefusal"

    /// Phase 348 — the decode refusal's declared shape: the object `decodeNode` answers with since
    /// Phase 337, `DecodeError.toJson`'s members — the code from the closed set (spelled as every
    /// host spells it, read off [[DecodeError.codes]] so a code added there is declared here), the
    /// path from the document root (a member key or an item index per step), what the position
    /// expected, and the sentence. One exported type, which every decode signature names.
    let private refusalDecl: string =
        "/** A decode refusal: what kind of fault, where (root-first steps: a member key or an item index), what the position admits, and a sentence. */\n"
        + "export type "
        + refusalTypeName
        + " = { code: "
        + (DecodeError.codes
           |> List.map (DecodeError.codeName >> SourceLit.tsString)
           |> String.concat " | ")
        + "; path: Array<string | number>; expected: string; message: string };"

    /// The declared TypeScript type of an IDL type, as the declaration file spells it: a declared
    /// enum, record or union by its own name, a node as `Node`, and a sentinel, JSON or undeclared
    /// hosted slot as `unknown`. Shared by [[typescriptDeclarations]] and the derived members'
    /// declarations (Phase 381), so a member's type is spelled exactly as the file's own types are.
    let rec private tsDeclType (t: IdlType) : Result<string, CodegenError> =
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
            |> List.map tsDeclType
            |> concatR ", "
            |> Result.map (fun a -> n + "<" + a + ">")
        | TVar v -> Ok v
        | TNode -> Ok "Node"
        | TList inner -> tsDeclType inner |> Result.map (fun s -> "Array<" + s + ">")
        | TMap vt -> tsDeclType vt |> Result.map (fun s -> "{ [key: string]: " + s + " }")
        // A hosted slot that declares its wire form IS that type on this side.
        | THosted { Wire = Some w } -> tsDeclType w
        | TJson
        | THosted _
        | TClosure
        | TFn _
        | TOpaque -> Ok "unknown"
        | TKind
        | TOp -> Error(opVocabularySlot "the TypeScript declaration backend" t)

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
    ///
    /// Phase 348 — `decodeNode`'s refusal is declared as the object it is, the exported
    /// `DecodeRefusal` (it read `error: string` after Phase 337 made it an object), and a
    /// vocabulary type spelled like a name the file declares for itself (`Node`, `NodeKind`, a
    /// `<Kind>Spec`, `DecodeRefusal`) is refused as `UnsupportedConstruct` rather than declared
    /// twice.
    let typescriptDeclarations (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        let kinds = kindTags |> List.choose (fun t -> IdlLookup.tryKind idl t)

        let enums, unions, records = referenced idl kinds
        let disc = tsDiscKey idl.Wire.Discriminator
        let tsType = tsDeclType

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

        // Phase 348 — the names this file declares beside the vocabulary's own: a vocabulary type
        // spelled like one of them would be declared twice, which a consumer's compiler refuses
        // far from its cause. Refused here, naming it.
        let fixedNames =
            [ "Node"; refusalTypeName ]
            @ (match idl.Wire.NodeEnvelope with
               | NodeEnvelopeShape.NestedKind -> [ "NodeKind" ]
               | NodeEnvelopeShape.FlatKind -> [])
            @ specs

        let clash =
            (enums |> List.map _.Name)
            @ (unions |> List.map _.Name)
            @ (records |> List.map _.Name)
            |> List.tryFind (fun n -> List.contains n fixedNames)

        match clash with
        | Some name ->
            Error(
                CodegenError.UnsupportedConstruct(
                    sprintf "a vocabulary type named '%s' in the TypeScript declarations" name,
                    "the declaration file already declares that name for the node, a kind spec or the decode refusal",
                    "rename the type"
                )
            )
        | None ->
            [ [ Ok(
                    "// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen (Phase 252 — type declarations for the TS backend). Do not edit by hand."
                ) ]
              enumDecls
              recordDecls
              unionDecls
              specDecls
              [ nodeDecl ]
              [ Ok(
                    refusalDecl
                    + "\n\n"
                    + "export declare function encodeNode(n: Node): string;\n"
                    + "export declare function decodeNode(s: string): { ok: true; value: Node } | { ok: false; error: "
                    + refusalTypeName
                    + " };"
                ) ] ]
            |> List.concat
            |> concatR "\n\n"
            |> Result.map (fun s -> normalizeEol s + "\n")

    // -----------------------------------------------------------------------
    // Phase 380 — the STRUCTURAL DERIVATIONS (Phase 374) and the COLLECTING DECODERS (Phase 377)
    // in the TypeScript host. Every derivation is opt-in exactly as on the F# side, and is stated
    // over the SAME analysis rather than a second one: which positions are structural children
    // (`FSharpDerive.structuralField`), which declarations hold a node (`FSharpDerive.nodeHolders`),
    // which fields a fold descends (`FSharpDerive.foldSelfIn`), when a defect is a leaf's
    // (`FSharpCodec.collectsOver`). ADMISSIBILITY IS THE F# PATH'S: the requests are run through
    // the F# derivation first and its refusal is this path's refusal, so the two hosts accept and
    // refuse the same requests with the same typed error.
    //
    // This host holds a value in its WIRE shape — plain objects keyed by wire field name, a union
    // case tagged by the discriminator, an enum as its wire string, an absent optional as
    // `undefined`, a map as an object — so every derived member reads and rebuilds that shape. A
    // map is walked in Ordinal key order, the order the F# host's `Map` iterates in, so the two
    // hosts list the nodes a map holds, and fold over its values, in the same order.
    //
    // `MapMsg` has no TypeScript counterpart: this host holds a handler slot (`TFn`) as its
    // sentinel's `null` and declares no message type, so a message map has nothing to rewrite. The
    // request is still checked — a vocabulary the F# path refuses is refused here — and emits
    // nothing.
    // -----------------------------------------------------------------------

    /// `obj.name`, or `obj["odd name"]` for a key JavaScript cannot spell bare.
    let private tsProp (obj: string) (name: string) = tsDiscProp name obj

    /// `[...a, ...b]`, a single operand as itself, or `[]`.
    let private tsAppend (xs: string list) =
        match xs with
        | [] -> "[]"
        | [ x ] -> x
        | _ -> "[" + (xs |> List.map (fun x -> "..." + x) |> String.concat ", ") + "]"

    /// The kind object of the node bound to `n` — the node itself under the flat envelope.
    let private tsKindOf (flat: bool) (n: string) = if flat then n else n + ".kind"

    /// The structural children of a kind, by wire field name: `(name, isList)`.
    let private tsStructuralFields (k: IdlKind) : (string * bool) list =
        k.Fields
        |> List.choose (fun f -> FSharpDerive.structuralField f |> Option.map (fun (_, isList) -> f.Name, isList))

    /// The `switch` over the kind tag of `k` — one arm per entry, then `fallback`.
    let private tsKindSwitch (disc: string) (indent: string) (arms: (string * string) list) (fallback: string) =
        [ yield indent + "switch (" + tsDiscProp disc "k" + ") {"
          for tag, body in arms do
              yield indent + "  case " + SourceLit.tsString tag + ": " + body
          yield indent + "  default: " + fallback
          yield indent + "}" ]
        |> String.concat "\n"

    /// Emission 1 — public `wireTag`, `allWireTags`, `children`, `withChildren` (kids first, so
    /// `withChildren(children(n), n)` is `n` rebuilt), and `nodeWitness` over them: the four members
    /// of Core's `NodeWitness`, as a plain object, since this host has no Core runtime to type it.
    let private tsStructuralDecl (disc: string) (flat: bool) (kinds: IdlKind list) : string * string list =
        let bearing =
            kinds |> List.filter (fun k -> not (List.isEmpty (tsStructuralFields k)))

        let rebuild (assigns: string) =
            if flat then
                sprintf "{ ...n, %s }" assigns
            else
                sprintf "{ ...n, kind: { ...k, %s } }" assigns

        let childArms =
            bearing
            |> List.map (fun k ->
                let items =
                    tsStructuralFields k
                    |> List.map (fun (name, isList) -> (if isList then "..." else "") + tsProp "k" name)
                    |> String.concat ", "

                k.Tag, sprintf "return [%s];" items)

        let replaceArms =
            bearing
            |> List.map (fun k ->
                let assigns =
                    match tsStructuralFields k with
                    | [ (name, true) ] -> SourceLit.tsKey name + ": kids"
                    | fs ->
                        fs
                        |> List.mapi (fun i (name, _) -> sprintf "%s: kids[%d]" (SourceLit.tsKey name) i)
                        |> String.concat ", "

                k.Tag, sprintf "return %s;" (rebuild assigns))

        let allTags =
            "const allWireTags = ["
            + (kinds |> List.map (fun k -> SourceLit.tsString k.Tag) |> String.concat ", ")
            + "];"

        [ "// Phase 380 — STRUCTURAL ACCESS. The kind's wire tag — the discriminator it is encoded under."
          "function wireTag(n) {"
          "  return " + tsDiscProp disc (tsKindOf flat "n") + ";"
          "}"
          ""
          "// Every wire tag this module's kinds are encoded under, in declaration order."
          allTags
          ""
          "// The node's ordered structural children, in field order: a node or node list a kind always"
          "// carries. An optional node is a keyed position, not a child."
          "function children(n) {"
          "  const k = " + tsKindOf flat "n" + ";"
          tsKindSwitch disc "  " childArms "return [];"
          "}"
          ""
          "// The node with exactly this structural child list, its id and kind kept."
          "function withChildren(kids, n) {"
          "  const k = " + tsKindOf flat "n" + ";"
          tsKindSwitch disc "  " replaceArms "return n;"
          "}"
          ""
          "// The structural witness — Core's `NodeWitness` members over this module's node."
          "const nodeWitness = {"
          "  id: (n) => n.id,"
          "  kindTag: wireTag,"
          "  children: children,"
          "  replaceChildren: (n, kids) => withChildren(kids, n),"
          "};" ]
        |> String.concat "\n",
        [ "wireTag"; "allWireTags"; "children"; "withChildren"; "nodeWitness" ]

    /// Emission 2 — the keyed walk: per-declaration node listers and rebuilders, the node-level
    /// `keyedChildren` / `withKeyedChildren` (arity-preserving), the full walk, and `keyedWitness`
    /// (Core's `KeyedWitness` members). Every node position that is not a structural child: an
    /// optional node, a node in a record, a union case, a list or a map, and the node envelope.
    let private tsKeyedDecl (disc: string) (flat: bool) (ctx: FSharpDerive.Ctx) : string * string list =
        let hs = FSharpDerive.nodeHolders ctx
        let holdsF (f: IdlField) = FSharpDerive.holdsIn hs f.Type

        // A node held through a generic union's type argument never reaches here: the F# analysis
        // refused it first.
        let rec collect (t: IdlType) (e: string) (d: int) : string =
            let x = sprintf "__x%d" d

            match t with
            | TNode -> sprintf "[%s]" e
            | TList i -> sprintf "%s.flatMap((%s) => %s)" e x (collect i x (d + 1))
            | TMap i ->
                sprintf "Object.keys(%s).sort().flatMap((%s) => %s)" e x (collect i (sprintf "%s[%s]" e x) (d + 1))
            | TRecord n
            | TUnion(n, _) -> sprintf "nodesIn%s(%s)" n e
            | _ -> "[]"

        let rec mapNodes (t: IdlType) (e: string) (d: int) : string =
            let x = sprintf "__x%d" d

            match t with
            | TNode -> sprintf "f(%s)" e
            | TList i -> sprintf "%s.map((%s) => %s)" e x (mapNodes i x (d + 1))
            | TMap i -> sprintf "keyedMapEntries(%s, (%s) => %s)" e x (mapNodes i x (d + 1))
            | TRecord n
            | TUnion(n, _) -> sprintf "mapNodesIn%s(f, %s)" n e
            | _ -> e

        let collectField (f: IdlField) (e: string) =
            match f.Opt with
            | Optional -> sprintf "(%s === undefined ? [] : %s)" e (collect f.Type e 0)
            | _ -> collect f.Type e 0

        let mapField (f: IdlField) (e: string) =
            match f.Opt with
            | Optional -> sprintf "(%s === undefined ? undefined : %s)" e (mapNodes f.Type e 0)
            | _ -> mapNodes f.Type e 0

        /// `const __m<i> = …;` per field, then the object of the rebuilt members.
        let rebuilt (indent: string) (fs: IdlField list) (access: IdlField -> string) =
            let lets =
                fs
                |> List.mapi (fun i f -> sprintf "%sconst __m%d = %s;\n" indent i (mapField f (access f)))
                |> String.concat ""

            let assigns =
                fs
                |> List.mapi (fun i f -> sprintf "%s: __m%d" (SourceLit.tsKey f.Name) i)
                |> String.concat ", "

            lets, assigns

        let recordHelpers (r: IdlRecord) =
            let fs = r.Fields |> List.filter holdsF
            let lets, assigns = rebuilt "  " fs (fun f -> tsProp "v" f.Name)

            [ sprintf
                  "function nodesIn%s(v) {\n  return %s;\n}"
                  r.Name
                  (tsAppend (fs |> List.map (fun f -> collectField f (tsProp "v" f.Name))))
              sprintf "function mapNodesIn%s(f, v) {\n%s  return { ...v, %s };\n}" r.Name lets assigns ]

        let unionHelpers (u: IdlUnion) =
            let holding = u.Cases |> List.filter (fun c -> c.Fields |> List.exists holdsF)

            let tag = tsDiscProp ctx.Idl.Wire.Discriminator "v"

            let listerArms =
                holding
                |> List.map (fun c ->
                    let fs = c.Fields |> List.filter holdsF

                    sprintf
                        "    case %s: return %s;"
                        (SourceLit.tsString c.Tag)
                        (tsAppend (fs |> List.map (fun f -> collectField f (tsProp "v" f.Name)))))

            let mapperArms =
                holding
                |> List.map (fun c ->
                    let fs = c.Fields |> List.filter holdsF
                    let lets, assigns = rebuilt "      " fs (fun f -> tsProp "v" f.Name)

                    sprintf
                        "    case %s: {\n%s      return { ...v, %s };\n    }"
                        (SourceLit.tsString c.Tag)
                        lets
                        assigns)

            let lister =
                sprintf "function nodesIn%s(v) {\n  switch (%s) {\n" u.Name tag
                + String.concat "\n" (listerArms @ [ "    default: return [];" ])
                + "\n  }\n}"

            let mapper =
                sprintf "function mapNodesIn%s(f, v) {\n  switch (%s) {\n" u.Name tag
                + String.concat "\n" (mapperArms @ [ "    default: return v;" ])
                + "\n  }\n}"

            [ lister; mapper ]

        let helpers =
            (ctx.Records
             |> List.filter (fun r -> hs.Contains r.Name)
             |> List.collect recordHelpers)
            @ (ctx.Unions
               |> List.filter (fun u -> hs.Contains u.Name)
               |> List.collect unionHelpers)

        let keyedFields (k: IdlKind) =
            k.Fields
            |> List.filter (fun f -> holdsF f && (FSharpDerive.structuralField f).IsNone)

        let keyedKinds =
            ctx.Kinds |> List.filter (fun k -> not (List.isEmpty (keyedFields k)))

        let envFields = ctx.Idl.NodeFields |> List.filter holdsF
        let disc = ctx.Idl.Wire.Discriminator

        let listerArms =
            keyedKinds
            |> List.map (fun k ->
                k.Tag,
                sprintf
                    "ofKind = %s; break;"
                    (tsAppend (keyedFields k |> List.map (fun f -> collectField f (tsProp "k" f.Name)))))

        let envListers = envFields |> List.map (fun f -> collectField f (tsProp "n" f.Name))

        let mapperArms =
            keyedKinds
            |> List.map (fun k ->
                let lets, assigns = rebuilt "      " (keyedFields k) (fun f -> tsProp "k" f.Name)
                k.Tag, sprintf "{\n%s      kind = { %s };\n      break;\n    }" lets assigns)

        let envLets, envAssigns =
            let lets =
                envFields
                |> List.mapi (fun i f -> sprintf "  const __e%d = %s;\n" i (mapField f (tsProp "n" f.Name)))
                |> String.concat ""

            let assigns =
                envFields
                |> List.mapi (fun i f -> sprintf ", %s: __e%d" (SourceLit.tsKey f.Name) i)
                |> String.concat ""

            lets, assigns

        let mapKeyedReturn =
            if flat then
                sprintf "  return { ...n, ...kind%s };" envAssigns
            else
                sprintf "  return { ...n, kind: { ...k, ...kind }%s };" envAssigns

        let text =
            [ yield
                  "// Phase 380 — KEYED POSITIONS. A map is rebuilt entry by entry in Ordinal key order — the\n// order `keyedChildren` lists its nodes in.\nconst keyedMapEntries = (m, f) => {\n  const out = Object.create(null);\n  for (const key of Object.keys(m).sort()) out[key] = f(m[key]);\n  return out;\n};"
              yield! helpers
              yield
                  [ "// The nodes this node holds in keyed, non-structural positions, in declaration order."
                    "function keyedChildren(n) {"
                    "  const k = " + tsKindOf flat "n" + ";"
                    "  let ofKind;"
                    tsKindSwitch disc "  " listerArms "ofKind = [];"
                    "  return " + tsAppend ("ofKind" :: envListers) + ";"
                    "}" ]
                  |> String.concat "\n"
              yield
                  [ "function mapKeyed(f, n) {"
                    "  const k = " + tsKindOf flat "n" + ";"
                    "  let kind = {};"
                    tsKindSwitch disc "  " mapperArms "break;"
                    envLets + mapKeyedReturn
                    "}" ]
                  |> String.concat "\n"
              yield
                  [ "// The node with these nodes in its keyed positions, position for position (arity-preserving)."
                    "function withKeyedChildren(kids, n) {"
                    "  let i = 0;"
                    "  return mapKeyed((old) => (i < kids.length ? kids[i++] : old), n);"
                    "}"
                    ""
                    "// The generated full walk: every node position the vocabulary declares, structural and keyed."
                    "function idsUniqueFullWalk(root) {"
                    "  const seen = new Set();"
                    "  const stack = [root];"
                    "  while (stack.length > 0) {"
                    "    const x = stack.pop();"
                    "    if (seen.has(x.id)) return false;"
                    "    seen.add(x.id);"
                    "    stack.push(...children(x), ...keyedChildren(x));"
                    "  }"
                    "  return true;"
                    "}"
                    ""
                    "// The keyed witness — Core's `KeyedWitness` members over this module's node."
                    "const keyedWitness = {"
                    "  surface: \"the generated full walk over every node position the vocabulary declares\","
                    "  keyedChildren: keyedChildren,"
                    "  replaceKeyedChildren: (n, kids) => withKeyedChildren(kids, n),"
                    "  placeKeyedChild: (n, id) => {"
                    "    const ks = keyedChildren(n);"
                    "    return ks.length === 0 ? undefined : withKeyedChildren([{ ...ks[0], id: id }, ...ks.slice(1)], n);"
                    "  },"
                    "  idsUnique: idsUniqueFullWalk,"
                    "};" ]
                  |> String.concat "\n" ]
            |> String.concat "\n\n"

        text, [ "keyedChildren"; "withKeyedChildren"; "keyedWitness" ]

    /// Emission 3 — `slotsOf<T>(n)`: every `[field name, value]` pair the node holds at the declared
    /// type `T` (directly, optional or in a list), kind fields first, then the envelope's.
    let private tsSlotsDecl
        (disc: string)
        (flat: bool)
        (ctx: FSharpDerive.Ctx)
        (typeName: string)
        : string * string list =
        let isT (t: IdlType) =
            match t with
            | TEnum n
            | TRecord n
            | TUnion(n, _) -> n = typeName
            | _ -> false

        let ofField (owner: string) (f: IdlField) : string option =
            let e = tsProp owner f.Name
            let key = SourceLit.tsString f.Name

            let direct =
                match f.Type with
                | t when isT t -> Some(sprintf "[[%s, %s]]" key e)
                | TList t when isT t -> Some(sprintf "%s.map((__v) => [%s, __v])" e key)
                | _ -> None

            match f.Opt with
            | Optional -> direct |> Option.map (sprintf "(%s === undefined ? [] : %s)" e)
            | _ -> direct

        let arms =
            ctx.Kinds
            |> List.choose (fun k ->
                match k.Fields |> List.choose (ofField "k") with
                | [] -> None
                | xs -> Some(k.Tag, sprintf "ofKind = %s; break;" (tsAppend xs)))

        let env = ctx.Idl.NodeFields |> List.choose (ofField "n")
        let name = "slotsOf" + typeName

        [ sprintf
              "// Every [field name, value] pair this node holds at the declared type `%s`, kind fields first."
              typeName
          sprintf "function %s(n) {" name
          "  const k = " + tsKindOf flat "n" + ";"
          "  let ofKind;"
          tsKindSwitch disc "  " arms "ofKind = [];"
          "  return " + tsAppend ("ofKind" :: env) + ";"
          "}" ]
        |> String.concat "\n",
        [ name ]

    /// Emissions 5 and 6 — a union's fold and its field projections, the members of one object
    /// exported under the union's name (`Rule.fold`, `Trigger.owner`), as the F# host's `module`
    /// is. The object is declared as `<Union>$` and exported `as <Union>`, so a union named like a
    /// global the module reads (`Map`, `Object`, `Error`) shadows nothing inside the module.
    let private tsUnionModulesDecl
        (ctx: FSharpDerive.Ctx)
        (requests: FSharpDerive.Request list)
        : (string * string list) list =
        let disc = ctx.Idl.Wire.Discriminator

        let named =
            requests
            |> List.choose (fun r ->
                match r with
                | FSharpDerive.Request.Fold n -> Some n
                | FSharpDerive.Request.Projections(n, _) -> Some n
                | _ -> None)
            |> List.distinct

        let recursesIn (u: IdlUnion) (seen: Set<string>) (t: IdlType) =
            FSharpDerive.foldSelfIn ctx u seen t = Ok true

        let foldMember (u: IdlUnion) =
            let self = u.Name + "$"

            let rec foldE (t: IdlType) (e: string) (st: string) (d: int) : string =
                let x = sprintf "__x%d" d
                let s = sprintf "__s%d" d

                match t with
                | TUnion(n, _) when n = u.Name -> sprintf "%s.fold(folder, %s, %s)" self st e
                | TList i -> sprintf "%s.reduce((%s, %s) => %s, %s)" e s x (foldE i x s (d + 1)) st
                | TMap i ->
                    sprintf
                        "Object.keys(%s).sort().reduce((%s, %s) => %s, %s)"
                        e
                        s
                        x
                        (foldE i (sprintf "%s[%s]" e x) s (d + 1))
                        st
                | TRecord n ->
                    let fs =
                        ctx.Records
                        |> List.tryFind (fun r -> r.Name = n)
                        |> Option.map _.Fields
                        |> Option.defaultValue []
                        |> List.filter (fun f -> recursesIn u (Set.singleton n) f.Type)

                    foldFields fs (fun f -> tsProp e f.Name) st (d + 1)
                | _ -> st

            and foldField (f: IdlField) (e: string) (st: string) (d: int) =
                match f.Opt with
                | Optional -> sprintf "(%s === undefined ? %s : %s)" e st (foldE f.Type e st (d + 1))
                | _ -> foldE f.Type e st d

            /// Thread the state through several fields, left to right.
            and foldFields (fs: IdlField list) (access: IdlField -> string) (st: string) (d: int) =
                match fs with
                | [] -> st
                | [ f ] -> foldField f (access f) st d
                | _ ->
                    let c = sprintf "__c%d" d

                    let steps =
                        fs
                        |> List.map (fun f -> sprintf "%s = %s; " c (foldField f (access f) c (d + 1)))
                        |> String.concat ""

                    sprintf "(() => { let %s = %s; %sreturn %s; })()" c st steps c

            let arms =
                u.Cases
                |> List.choose (fun c ->
                    match c.Fields |> List.filter (fun f -> recursesIn u Set.empty f.Type) with
                    | [] -> None
                    | fs ->
                        Some(
                            sprintf
                                "      case %s: return %s;"
                                (SourceLit.tsString c.Tag)
                                (foldFields fs (fun f -> tsProp "v" f.Name) "state" 0)
                        ))

            [ sprintf "  // Fold `folder` over this value and every nested `%s` it holds, in preorder." u.Name
              "  fold(folder, state, v) {"
              "    state = folder(state, v);"
              "    switch (" + tsDiscProp disc "v" + ") {" ]
            @ arms
            @ [ "      default: return state;"; "    }"; "  }," ]
            |> String.concat "\n"

        let projectionMember (u: IdlUnion) (field: string) =
            let carriers =
                u.Cases
                |> List.filter (fun c -> c.Fields |> List.exists (fun f -> f.Name = field))

            let total = List.length carriers = List.length u.Cases

            [ yield
                  sprintf
                      "  // The `%s` field, %s."
                      field
                      (if total then
                           "which every case carries"
                       else
                           "where the case carries one (`undefined` where it does not)")
              yield "  " + SourceLit.tsKey field + "(v) {"
              yield "    switch (" + tsDiscProp disc "v" + ") {"
              for c in carriers do
                  yield sprintf "      case %s: return %s;" (SourceLit.tsString c.Tag) (tsProp "v" field)
              yield "      default: return undefined;"
              yield "    }"
              yield "  }," ]
            |> String.concat "\n"

        named
        |> List.choose (fun name -> ctx.Unions |> List.tryFind (fun u -> u.Name = name))
        |> List.map (fun u ->
            let folds = requests |> List.contains (FSharpDerive.Request.Fold u.Name)

            let fields =
                requests
                |> List.collect (fun r ->
                    match r with
                    | FSharpDerive.Request.Projections(n, fs) when n = u.Name -> fs
                    | _ -> [])
                |> List.distinct

            let members =
                (if folds then [ foldMember u ] else [])
                @ (fields |> List.map (projectionMember u))

            sprintf "// Phase 380 — derived members of `%s`, exported as `%s`.\n" u.Name u.Name
            + sprintf "const %s$ = {\n" u.Name
            + String.concat "\n\n" members
            + "\n};",
            [ sprintf "%s$ as %s" u.Name u.Name ])

    /// Emission 7 — `default<Tag>Spec` / `default<Record>` for every kind and record whose every
    /// field has a value without the caller, in the shape the generated decoder produces: an
    /// omitted-at-default member filled as the decoder refills it, a declared default rendered as
    /// the scaffold renders it, an absent optional and a host-only member absent.
    let private tsDefaultRecordsDecl
        (idl: Idl)
        (disc: string)
        (ctx: FSharpDerive.Ctx)
        : Result<string * string list, CodegenError> =
        let defaultFor (tag: string) (field: string) =
            idl.Defaults
            |> List.tryPick (fun d ->
                if d.Kind = tag && d.Field = field then
                    Some d.Value
                else
                    None)

        let sentinel (t: IdlType) =
            match t with
            | TClosure
            | TFn _
            | TOpaque -> true
            | _ -> false

        /// `None`: no value without the caller. `Some(Ok None)`: the member is absent.
        let fieldValue (declared: IdlValue option) (f: IdlField) : Result<string option, CodegenError> option =
            match declared, f.Opt with
            | _, OmitDefault d ->
                if sentinel f.Type then
                    Some(Ok(Some "null"))
                else
                    Some(tsDefaultLit idl disc f.Type d |> Result.map Some)
            | Some v, Required
            | Some v, Optional -> Some(typescriptValue idl f.Type v |> Result.map Some)
            | None, Required -> None
            | None, Optional
            | _, HostOnly -> Some(Ok None)

        let value
            (name: string)
            (extra: (string * string) list)
            (declared: IdlField -> IdlValue option)
            (fs: IdlField list)
            =
            let parts = fs |> List.map (fun f -> f, fieldValue (declared f) f)

            if parts |> List.exists (fun (_, v) -> v.IsNone) then
                None
            else
                parts
                |> List.map (fun (f, v) ->
                    v.Value
                    |> Result.map (Option.map (fun lit -> SourceLit.tsString f.Name + ": " + lit)))
                |> sequenceR
                |> Result.map (fun pieces ->
                    let pairs = (extra |> List.map (fun (k, v) -> k + ": " + v)) @ List.choose id pieces

                    sprintf
                        "// `%s` with every field at the value a caller need not pass.\nconst default%s = { %s };"
                        name
                        name
                        (String.concat ", " pairs),
                    "default" + name)
                |> Some

        let kinds =
            ctx.Kinds
            |> List.choose (fun k ->
                value
                    (k.Tag + "Spec")
                    [ tsDiscKey disc, SourceLit.tsString k.Tag ]
                    (fun f -> defaultFor k.Tag f.Name)
                    k.Fields)

        let records =
            ctx.Records |> List.choose (fun r -> value r.Name [] (fun _ -> None) r.Fields)

        (kinds @ records)
        |> sequenceR
        |> Result.map (fun xs -> xs |> List.map fst |> String.concat "\n\n", xs |> List.map snd)

    /// Emission 8 — `kindCategories`, `kindFieldNames`, `opFieldNames` as `Map`s of `Set`s and
    /// `envelopeFieldNames` as a `Set`, each in Ordinal order (the order the F# host's `Map` and
    /// `Set` iterate in).
    let private tsConstantsDecl (ctx: FSharpDerive.Ctx) : string * string list =
        let ordinal (xs: string list) =
            xs
            |> List.distinct
            |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b))

        let setLit (xs: string list) =
            match ordinal xs with
            | [] -> "new Set()"
            | ys -> "new Set([" + (ys |> List.map SourceLit.tsString |> String.concat ", ") + "])"

        let mapLit (entries: (string * string list) list) =
            match
                entries
                |> List.sortWith (fun (a, _) (b, _) -> System.String.CompareOrdinal(a, b))
            with
            | [] -> "new Map()"
            | es ->
                "new Map(["
                + (es
                   |> List.map (fun (k, vs) -> sprintf "[%s, %s]" (SourceLit.tsString k) (setLit vs))
                   |> String.concat ", ")
                + "])"

        let categories =
            ctx.Kinds
            |> List.map (fun k -> k.Category)
            |> List.distinct
            |> List.map (fun c -> c, ctx.Kinds |> List.filter (fun k -> k.Category = c) |> List.map _.Tag)

        let fieldsOf (ks: IdlKind list) =
            ks |> List.map (fun k -> k.Tag, k.Fields |> List.map _.Name)

        [ "// Phase 380 — VOCABULARY CONSTANTS. The kind tags of each declared category."
          "const kindCategories = " + mapLit categories + ";"
          ""
          "// The wire field names of each kind."
          "const kindFieldNames = " + mapLit (fieldsOf ctx.Kinds) + ";"
          ""
          "// The wire field names of the node envelope."
          "const envelopeFieldNames = "
          + setLit (ctx.Idl.NodeFields |> List.map _.Name)
          + ";"
          ""
          "// The wire field names of each tree op."
          "const opFieldNames = " + mapLit (fieldsOf ctx.Idl.Ops) + ";" ]
        |> String.concat "\n",
        [ "kindCategories"; "kindFieldNames"; "envelopeFieldNames"; "opFieldNames" ]

    // ---- the collecting decoders (Phase 377's `SpecDecoders`) ----

    /// The collecting decoder reference for a type — [[tsDecFn]]'s, arm for arm, where the type has
    /// independent positions to collect over; a leaf is [[tsDecFn]]'s decoder, which refuses once.
    let rec private tsColFn (t: IdlType) : Result<string, CodegenError> =
        match t with
        | TVar v -> Ok("col" + v)
        | TUnion(n, []) -> Ok("col" + n)
        | TUnion(n, args) ->
            args
            |> List.map tsColFn
            |> concatR ", "
            |> Result.map (fun a -> "((x) => col" + n + "(" + a + ", x))")
        | TNode -> Ok "colNode"
        | TList inner -> tsColFn inner |> Result.map (fun c -> "cList(" + c + ")")
        | TMap vt -> tsColFn vt |> Result.map (fun c -> "cMap(" + c + ")")
        | TRecord n -> Ok("col" + n)
        | _ -> tsDecFn t

    /// One member read back under [[tsDecField]]'s presence rules, collecting. A leaf member (and a
    /// host-only one) IS [[tsDecField]]'s expression, so its presence rules are the short-circuiting
    /// decoder's by construction.
    let private tsColField (idl: Idl) (disc: string) (f: IdlField) : Result<string, CodegenError> =
        let key = SourceLit.tsString f.Name

        match f.Opt with
        | HostOnly -> tsDecField idl disc f
        | _ when not (FSharpCodec.collectsOver f.Type) -> tsDecField idl disc f
        | Required -> tsColFn f.Type |> Result.map (fun c -> "cReq(" + key + ", fs, " + c + ")")
        | Optional -> tsColFn f.Type |> Result.map (fun c -> "cOpt(" + key + ", fs, " + c + ")")
        | OmitDefault d ->
            tsDefaultLit idl disc f.Type d
            |> Result.bind (fun lit ->
                tsColFn f.Type
                |> Result.map (fun c -> "cDef(" + key + ", fs, " + c + ", " + lit + ")"))

    /// Read every member in declaration order, then build the object [[tsFieldObject]] builds — the
    /// same keys in the same order — or throw every member's refusals, in that order.
    let private tsColObject
        (idl: Idl)
        (disc: string)
        (indent: string)
        (extra: (string * string) list)
        (fields: IdlField list)
        : Result<string, CodegenError> =
        fields
        |> List.map (tsColField idl disc)
        |> sequenceR
        |> Result.map (fun reads ->
            let pairs =
                (extra |> List.map (fun (k, v) -> k + ": " + v))
                @ (fields
                   |> List.mapi (fun i f -> SourceLit.tsString f.Name + ": v[" + string i + "]"))

            let literal = "{ " + String.concat ", " pairs + " }"

            match reads with
            | [] -> indent + "return " + literal + ";"
            | _ ->
                indent
                + "const v = cAll(["
                + (reads |> List.map (fun r -> "() => " + r) |> String.concat ", ")
                + "]);\n"
                + indent
                + "return "
                + literal
                + ";")

    let private tsColUnion (idl: Idl) (disc: string) (tokens: HardenPolicy) (u: IdlUnion) =
        let argList =
            match u.Params with
            | [] -> "j"
            | ps -> (ps |> List.map (fun p -> "col" + p) |> String.concat ", ") + ", j"

        let arm (c: IdlUnionCase) =
            tsColObject idl disc "        " [ tsDiscKey disc, SourceLit.tsString c.Tag ] c.Fields
            |> Result.map (fun body -> "      case " + SourceLit.tsString c.Tag + ": {\n" + body + "\n      }")

        let taggedR =
            u.Cases
            |> List.map arm
            |> concatR "\n"
            |> Result.map (fun arms ->
                "  if (isObj(j)) {\n    const fs = j;\n    switch (dTag(j)) {\n"
                + arms
                + "\n      default: return dUnknown("
                + SourceLit.tsString (oneOf (u.Cases |> List.map (fun c -> c.Tag)))
                + ", "
                + SourceLit.tsString ("unknown " + u.Name + " case: ")
                + " + "
                + tsDiscProp disc "j"
                + ");\n    }\n  }")

        // A transparent union reads its single-field case bare, collecting through it.
        let untagged =
            match TransparentUnion.tag tokens u with
            | Some ttag ->
                match u.Cases |> List.tryFind (fun c -> c.Tag = ttag) with
                | Some({ Fields = [ f ] }) ->
                    tsColFn f.Type
                    |> Result.map (fun cfn ->
                        "  return { "
                        + tsDiscKey disc
                        + ": "
                        + SourceLit.tsString ttag
                        + ", "
                        + SourceLit.tsString f.Name
                        + ": "
                        + cfn
                        + "(j) };")
                | _ -> Error(transparentArity u.Name ttag)
            | None ->
                Ok(
                    "  return dFail('WrongKind', 'object', "
                    + SourceLit.tsString ("expected a " + u.Name + " object")
                    + ");"
                )

        taggedR
        |> Result.bind (fun tagged ->
            untagged
            |> Result.map (fun untagged ->
                "function col"
                + u.Name
                + "("
                + argList
                + ") {\n"
                + tagged
                + "\n"
                + untagged
                + "\n}"))

    /// The collecting prelude: the refusal LIST a collecting decoder throws, and the readers that
    /// gather it. It states the defect order, which a test pins.
    let private tsCollectingPrelude =
        """// ---------------------------------------------------------------------------
// Phase 380 — COLLECTING DECODERS. Beside every short-circuiting `dec*` decoder above, a `col*`
// decoder answers EVERY defect it finds, in one deterministic order — the F# host's:
//   - an object's members in FIELD DECLARATION ORDER (a node: `id`, its kind, then its envelope
//     fields), each member's defects at that member's position, its nested defects included
//     (depth-first);
//   - a list's items in index order, and a map's entries in document order;
//   - a leaf (a scalar, an enum, a sentinel, a hosted slot, verbatim JSON) reports at most one
//     defect, and so does a value of the wrong kind or an absent or unknown discriminator, whose
//     members are never read.
// The first defect of a collecting decoder is the defect its short-circuiting twin reports, and on
// a clean input both answer the same value. Codes and paths are Core's `DecodeError`'s.
// ---------------------------------------------------------------------------
// Several refusals at once — what a collecting decoder throws when a position it read refused.
class DecodeFaults extends Error {
  constructor(faults) {
    super(faults.length + ' decode faults');
    this.faults = faults;
  }
}
// One read: its value, or its refusals (one for a short-circuiting refusal, every one for a
// collecting one).
const cRun = (read) => {
  try {
    return { ok: true, value: read() };
  } catch (e) {
    if (e instanceof DecodeFault) return { ok: false, faults: [e] };
    if (e instanceof DecodeFaults) return { ok: false, faults: e.faults };
    throw e;
  }
};
// Every read, in order: their values when all decoded, else every read's refusals, in that order.
const cAll = (reads) => {
  const results = reads.map(cRun);
  const faults = results.flatMap((r) => (r.ok ? [] : r.faults));
  if (faults.length > 0) throw new DecodeFaults(faults);
  return results.map((r) => r.value);
};
// One step further from the root, for every refusal a member or an item raised.
const cAt = (step, f) => {
  try {
    return f();
  } catch (e) {
    if (e instanceof DecodeFault) e.path.unshift(step);
    else if (e instanceof DecodeFaults) for (const x of e.faults) x.path.unshift(step);
    throw e;
  }
};
const cList = (dec) => (j) => {
  if (!Array.isArray(j)) return dFail('WrongKind', 'array', 'expected an array');
  return cAll(j.map((_, i) => () => cAt(i, () => dec(dRead(j, i)))));
};
// Every entry is checked, in document order; a repeated key keeps its FIRST value, as `dMap` does.
const cMap = (dec) => (j) => {
  const o = dObj(j);
  const members = dMembersOf.get(o);
  const entries = (members === undefined)
    ? Object.keys(o).map((k) => [k, () => cAt(k, () => dec(dRead(o, k)))])
    : members.map(([k, v, floatTok]) => [k, () => cAt(k, () => { dFloatTok = floatTok; return dec(v); })]);
  const values = cAll(entries.map(([, read]) => read));
  const out = Object.create(null);
  entries.forEach(([k], i) => { if (!hasOwn(out, k)) out[k] = values[i]; });
  return out;
};
const cReq = (name, fs, dec) => hasOwn(fs, name) ? cAt(name, () => dec(dRead(fs, name))) : dMissing(name, "missing required field '" + name + "'");
const cOpt = (name, fs, dec) => hasOwn(fs, name) ? cAt(name, () => dec(dRead(fs, name))) : undefined;
const cDef = (name, fs, dec, dflt) => hasOwn(fs, name) ? cAt(name, () => dec(dRead(fs, name))) : dflt;"""

    /// The collecting prelude, the collecting decoders (node kind, node, unions, records, specs)
    /// and the public entries: per kind `decode<Tag>Spec` / `decode<Tag>SpecAll` over the kind's
    /// object, per node `decodeNodeJson` / `decodeNodeJsonAll` over a parsed value and
    /// `decodeNodeAll` over text — each answering `decodeNode`'s `{ ok, value }` / `{ ok, error }`,
    /// the collecting ones `{ ok: false, errors }`.
    let private tsCollectingDecl
        (idl: Idl)
        (disc: string)
        (flat: bool)
        (kinds: IdlKind list)
        (unions: IdlUnion list)
        (records: IdlRecord list)
        : Result<string * string list, CodegenError> =
        let colKind =
            "function colKind(j) {\n  if (!isObj(j)) return dFail('WrongKind', 'object', 'expected a kind object');\n  switch (dTag(j)) {\n"
            + (kinds
               |> List.map (fun k -> "    case " + SourceLit.tsString k.Tag + ": return col" + k.Tag + "Spec(j);")
               |> String.concat "\n")
            + "\n    default: return dUnknown("
            + SourceLit.tsString (oneOf (kinds |> List.map (fun k -> k.Tag)))
            + ", 'unknown node kind: ' + "
            + tsDiscProp disc "j"
            + ");\n  }\n}"

        let colNode =
            idl.NodeFields
            |> List.map (tsColField idl disc)
            |> sequenceR
            |> Result.map (fun envelope ->
                let kindRead = if flat then "colKind(j)" else "cReq('kind', fs, colKind)"

                let reads =
                    [ "dReq('id', fs, dStr)"; kindRead ] @ envelope
                    |> List.map (fun r -> "() => " + r)
                    |> String.concat ", "

                let envelopeAssigns =
                    idl.NodeFields
                    |> List.mapi (fun i f -> sprintf ", %s: v[%d]" (SourceLit.tsKey f.Name) (i + 2))
                    |> String.concat ""

                let build =
                    if flat then
                        "Object.assign({}, v[1], { id: v[0]" + envelopeAssigns + " })"
                    else
                        "{ id: v[0], kind: v[1]" + envelopeAssigns + " }"

                "function colNode(j) {\n  const fs = dObj(j);\n  const v = cAll(["
                + reads
                + "]);\n  return "
                + build
                + ";\n}")

        let objectDecoder (name: string) (extra: (string * string) list) (fields: IdlField list) =
            tsColObject idl disc "  " extra fields
            |> Result.map (fun body -> "function col" + name + "(j) {\n  const fs = dObj(j);\n" + body + "\n}")

        let group =
            [ Ok colKind; colNode ]
            @ (unions |> List.map (tsColUnion idl disc idl.Harden))
            @ (records |> List.map (fun r -> objectDecoder r.Name [] r.Fields))
            @ (kinds
               |> List.map (fun k ->
                   objectDecoder (k.Tag + "Spec") [ tsDiscKey disc, SourceLit.tsString k.Tag ] k.Fields))
            |> concatR "\n\n"

        let entries =
            [ yield
                  """// The public per-spec decoders. `decode<Tag>Spec` reads one kind's object (a parsed value) and
// answers its FIRST defect, as `{ ok: false, error }`; `decode<Tag>SpecAll` reads the same object and
// answers EVERY defect, as `{ ok: false, errors }`, in the order stated above. Each defect carries
// `decodeNode`'s members: `{ code, path, expected, message }`.
const cDefect = (e) => ({ code: e.code, path: e.path, expected: e.expected, message: e.message });
const cFirstOf = (read) => {
  try {
    dFloatTok = false;
    return { ok: true, value: read() };
  } catch (e) {
    if (!(e instanceof DecodeFault)) throw e;
    return { ok: false, error: cDefect(e) };
  }
};
const cEveryOf = (read) => {
  try {
    dFloatTok = false;
    return { ok: true, value: read() };
  } catch (e) {
    if (e instanceof DecodeFault) return { ok: false, errors: [cDefect(e)] };
    if (e instanceof DecodeFaults) return { ok: false, errors: e.faults.map(cDefect) };
    throw e;
  }
};"""
              for k in kinds do
                  yield sprintf "function decode%sSpec(j) {\n  return cFirstOf(() => dec%sSpec(j));\n}" k.Tag k.Tag
                  yield sprintf "function decode%sSpecAll(j) {\n  return cEveryOf(() => col%sSpec(j));\n}" k.Tag k.Tag
              yield
                  "// The whole node over a parsed value: the first defect, or every defect.\nfunction decodeNodeJson(j) {\n  return cFirstOf(() => decNode(j));\n}"
              yield "function decodeNodeJsonAll(j) {\n  return cEveryOf(() => colNode(j));\n}"
              yield
                  "// `decodeNode`'s collecting twin: a parser refusal is the one defect, else every defect.\nfunction decodeNodeAll(s) {\n  return cEveryOf(() => {\n    const root = dParse(s);\n    dFloatTok = false;\n    return colNode(root);\n  });\n}" ]
            |> String.concat "\n\n"

        let names =
            (kinds
             |> List.collect (fun k -> [ "decode" + k.Tag + "Spec"; "decode" + k.Tag + "SpecAll" ]))
            @ [ "decodeNodeJson"; "decodeNodeJsonAll"; "decodeNodeAll" ]

        group
        |> Result.map (fun g -> [ tsCollectingPrelude; g; entries ] |> String.concat "\n\n", names)

    // ---- Phase 381 — the derived members' DECLARATIONS ----

    /// One derived emission: the module text, the names its `export` names, and one declaration
    /// per exported name, for the declaration file. Built in ONE place for both files, so a member
    /// the module emits and the declaration the file writes for it are chosen by the same request
    /// and typed from the same analysis.
    type private DerivedPart =
        { Text: string
          Names: string list
          Decls: string list }

    /// The union of a kind list's wire tags as TypeScript string-literal types, `never` for none.
    let private tsTagUnion (kinds: IdlKind list) =
        match kinds with
        | [] -> "never"
        | _ -> kinds |> List.map (fun k -> SourceLit.tsString k.Tag) |> String.concat " | "

    /// `<A, B>` for a declaration's type parameters, or nothing.
    let private tsGeneric (ps: string list) =
        match ps with
        | [] -> ""
        | _ -> "<" + String.concat ", " ps + ">"

    /// `StructuralAccess` — the four access members and the witness object over them.
    let private structuralDecls (kinds: IdlKind list) : string list =
        let tag = tsTagUnion kinds

        [ "export declare function wireTag(n: Node): " + tag + ";"
          "export declare const allWireTags: ReadonlyArray<" + tag + ">;"
          "export declare function children(n: Node): Array<Node>;"
          "export declare function withChildren(kids: Array<Node>, n: Node): Node;"
          "export declare const nodeWitness: { id(n: Node): string; kindTag(n: Node): "
          + tag
          + "; children(n: Node): Array<Node>; replaceChildren(n: Node, kids: Array<Node>): Node };" ]

    /// `KeyedPositions` — the keyed walk and its witness object.
    let private keyedDecls: string list =
        [ "export declare function keyedChildren(n: Node): Array<Node>;"
          "export declare function withKeyedChildren(kids: Array<Node>, n: Node): Node;"
          "export declare const keyedWitness: { surface: string; keyedChildren(n: Node): Array<Node>; replaceKeyedChildren(n: Node, kids: Array<Node>): Node; placeKeyedChild(n: Node, id: string): Node | undefined; idsUnique(root: Node): boolean };" ]

    /// `SlotsOf T` — the pairs hold a `T`; a generic union's instantiations differ field by field,
    /// so its pairs hold `unknown`, the F# host's `obj`, for the same reason.
    let private slotsDecls (ctx: FSharpDerive.Ctx) (typeName: string) : string list =
        let generic =
            ctx.Unions
            |> List.exists (fun u -> u.Name = typeName && not (List.isEmpty u.Params))

        let elem = if generic then "unknown" else typeName

        [ sprintf "export declare function slotsOf%s(n: Node): Array<[string, %s]>;" typeName elem ]

    /// `Fold U` / `Projections (U, fields)` — the object they are exported as: `fold<S>(folder,
    /// state, v)` and one method per projected field, `T` where every case carries the field and
    /// none optionally, `T | undefined` otherwise. A generic union's parameters are each method's.
    let private unionModuleDecls (u: IdlUnion) (folds: bool) (fields: string list) : Result<string list, CodegenError> =
        let self = u.Name + tsGeneric u.Params

        let rec stateName (s: string) =
            if List.contains s u.Params then stateName (s + "_") else s

        let st = stateName "S"

        let foldDecl =
            sprintf
                "  fold%s(folder: (state: %s, v: %s) => %s, state: %s, v: %s): %s;"
                (tsGeneric (st :: u.Params))
                st
                self
                st
                st
                self
                st

        let projectionDecl (field: string) =
            let carried =
                u.Cases
                |> List.choose (fun c -> c.Fields |> List.tryFind (fun f -> f.Name = field))

            let alwaysPresent =
                List.length carried = List.length u.Cases
                && carried
                   |> List.forall (fun f ->
                       match f.Opt with
                       | Optional
                       | HostOnly -> false
                       | Required
                       | OmitDefault _ -> true)

            // The F# path refused a projection no case carries before this is reached.
            let ty =
                match carried with
                | f :: _ -> tsDeclType f.Type
                | [] -> Ok "never"

            ty
            |> Result.map (fun ty ->
                sprintf
                    "  %s%s(v: %s): %s;"
                    (SourceLit.tsKey field)
                    (tsGeneric u.Params)
                    self
                    (if alwaysPresent then ty else ty + " | undefined"))

        (if folds then [ Ok foldDecl ] else []) @ (fields |> List.map projectionDecl)
        |> sequenceR
        |> Result.map (fun members ->
            [ sprintf "export declare const %s: {\n%s\n};" u.Name (String.concat "\n" members) ])

    /// `VocabularyConstants` — `Map`s of `Set`s and a `Set`, declared read-only.
    let private constantsDecls (kinds: IdlKind list) : string list =
        let tag = tsTagUnion kinds

        [ "export declare const kindCategories: ReadonlyMap<string, ReadonlySet<"
          + tag
          + ">>;"
          "export declare const kindFieldNames: ReadonlyMap<"
          + tag
          + ", ReadonlySet<string>>;"
          "export declare const envelopeFieldNames: ReadonlySet<string>;"
          "export declare const opFieldNames: ReadonlyMap<string, ReadonlySet<string>>;" ]

    /// `SpecDecoders` — the public decoders' answers: the first defect as `decodeNode`'s `error`,
    /// or every defect, in order, as `errors`.
    let private collectingDecls (kinds: IdlKind list) : string list =
        let first v =
            "{ ok: true; value: " + v + " } | { ok: false; error: " + refusalTypeName + " }"

        let every v =
            "{ ok: true; value: "
            + v
            + " } | { ok: false; errors: Array<"
            + refusalTypeName
            + "> }"

        [ for k in kinds do
              yield sprintf "export declare function decode%sSpec(j: unknown): %s;" k.Tag (first (k.Tag + "Spec"))
              yield sprintf "export declare function decode%sSpecAll(j: unknown): %s;" k.Tag (every (k.Tag + "Spec"))
          yield "export declare function decodeNodeJson(j: unknown): " + first "Node" + ";"
          yield "export declare function decodeNodeJsonAll(j: unknown): " + every "Node" + ";"
          yield "export declare function decodeNodeAll(s: string): " + every "Node" + ";" ]

    /// Phase 380/381 — the requested derivations as parts, admitted exactly when the F# path
    /// admits them. `None` when nothing is requested: the plain module and the plain declarations.
    let private tsDerivedParts
        (requests: FSharpDerive.Request list)
        (decoders: bool)
        (idl: Idl)
        (kindTags: string list)
        : Result<DerivedPart list option, CodegenError> =
        if List.isEmpty requests && not decoders then
            Ok None
        else
            let kinds = kindTags |> List.choose (fun t -> IdlLookup.tryKind idl t)

            let _, unions, records = referenced idl kinds
            let msg = msgCarrying idl
            let disc = idl.Wire.Discriminator
            let flat = idl.Wire.NodeEnvelope = NodeEnvelopeShape.FlatKind

            let ctx: FSharpDerive.Ctx =
                { Idl = idl
                  Msg = msg
                  Kinds = kinds
                  Unions = unions
                  Records = records
                  Projected = Set.empty }

            let has r = List.contains r requests

            let publicAccess =
                has FSharpDerive.Request.StructuralAccess
                || has FSharpDerive.Request.KeyedPositions

            // The F# path decides admissibility: its structural witness (which refuses a kind
            // mixing a node list with other children) and its derivations, texts discarded.
            let admitted =
                (if publicAccess then
                     FSharpCodec.witnessDecl true msg kinds |> Result.map ignore
                 else
                     Ok())
                |> Result.bind (fun () -> FSharpDerive.derivedDecl ctx requests |> Result.map ignore)

            let slots =
                requests
                |> List.choose (fun r ->
                    match r with
                    | FSharpDerive.Request.SlotsOf t -> Some t
                    | _ -> None)
                |> List.distinct

            let part (text: string, names: string list) (decls: string list) =
                { Text = text
                  Names = names
                  Decls = decls }

            // `tsUnionModulesDecl` answers one entry per requested union, in this same order.
            let unionModules () =
                let named =
                    requests
                    |> List.choose (fun r ->
                        match r with
                        | FSharpDerive.Request.Fold n -> Some n
                        | FSharpDerive.Request.Projections(n, _) -> Some n
                        | _ -> None)
                    |> List.distinct
                    |> List.choose (fun name -> ctx.Unions |> List.tryFind (fun u -> u.Name = name))

                let fieldsOf (u: IdlUnion) =
                    requests
                    |> List.collect (fun r ->
                        match r with
                        | FSharpDerive.Request.Projections(n, fs) when n = u.Name -> fs
                        | _ -> [])
                    |> List.distinct

                List.zip named (tsUnionModulesDecl ctx requests)
                |> List.map (fun (u, emitted) ->
                    unionModuleDecls u (has (FSharpDerive.Request.Fold u.Name)) (fieldsOf u)
                    |> Result.map (part emitted))

            // `default<N>` is a value of the declared type `N`: a `<Tag>Spec` or a record.
            let defaultDecls (names: string list) =
                names
                |> List.map (fun n -> sprintf "export declare const %s: %s;" n (n.Substring "default".Length))

            admitted
            |> Result.bind (fun () ->
                [ (if publicAccess then
                       [ Ok(part (tsStructuralDecl disc flat kinds) (structuralDecls kinds)) ]
                   else
                       [])
                  (if has FSharpDerive.Request.KeyedPositions then
                       [ Ok(part (tsKeyedDecl disc flat ctx) keyedDecls) ]
                   else
                       [])
                  slots
                  |> List.map (fun t -> Ok(part (tsSlotsDecl disc flat ctx t) (slotsDecls ctx t)))
                  (if has FSharpDerive.Request.DefaultRecords then
                       [ tsDefaultRecordsDecl idl disc ctx
                         |> Result.map (fun (text, names) -> part (text, names) (defaultDecls names)) ]
                   else
                       [])
                  (if has FSharpDerive.Request.VocabularyConstants then
                       [ Ok(part (tsConstantsDecl ctx) (constantsDecls kinds)) ]
                   else
                       [])
                  unionModules ()
                  (if decoders then
                       [ tsCollectingDecl idl disc flat kinds unions records
                         |> Result.map (fun emitted -> part emitted (collectingDecls kinds)) ]
                   else
                       []) ]
                |> List.concat
                |> sequenceR
                |> Result.map Some)

    /// Phase 380 — `typescriptModule` plus the requested derivations, appended after its members
    /// with one further `export` naming them; no request and no decoders IS `typescriptModule`,
    /// byte for byte. A request the F# path refuses is refused here with the F# path's error.
    let typescriptModuleDerived
        (requests: FSharpDerive.Request list)
        (decoders: bool)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        typescriptModule idl kindTags
        |> Result.bind (fun baseText ->
            tsDerivedParts requests decoders idl kindTags
            |> Result.map (fun parts ->
                match parts with
                | None -> baseText
                | Some parts ->
                    let texts = parts |> List.map _.Text

                    let exported =
                        match parts |> List.collect _.Names with
                        | [] -> []
                        | names -> [ "export { " + String.concat ", " names + " };" ]

                    String.concat "\n\n" ((baseText :: texts) @ exported) |> normalizeEol))

    /// Phase 381 — `typescriptDeclarations` plus one declaration per member the derived module
    /// exports, appended after the file's own; no request and no decoders IS
    /// `typescriptDeclarations`, byte for byte, and a request is refused exactly as
    /// [[typescriptModuleDerived]] refuses it. The parts are the module's own, so the members the
    /// module exports and the members this file declares are chosen by one request list.
    ///
    /// A requested `MapMsg` is not declared, because the module exports nothing for it; the file's
    /// header comment says why.
    let typescriptDeclarationsDerived
        (requests: FSharpDerive.Request list)
        (decoders: bool)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        typescriptDeclarations idl kindTags
        |> Result.bind (fun baseText ->
            tsDerivedParts requests decoders idl kindTags
            |> Result.map (fun parts ->
                match parts with
                | None -> baseText
                | Some parts ->
                    let header =
                        [ yield
                              "// Phase 381 — the derived members the module was generated with are declared after its own, one declaration per exported name."
                          if List.contains FSharpDerive.Request.MapMsg requests then
                              yield
                                  "// `mapMsg` is not declared: the module emits no `mapMsg`, because this host holds a handler slot as its sentinel's `null` and carries no message type, so a message map has nothing to rewrite." ]
                        |> String.concat "\n"

                    // The base file's first line is its header comment; the derived lines join it.
                    let firstBreak = baseText.IndexOf '\n'

                    let headed =
                        baseText.Substring(0, firstBreak)
                        + "\n"
                        + header
                        + baseText.Substring firstBreak

                    let decls =
                        parts
                        |> List.map (fun p -> String.concat "\n" p.Decls)
                        |> List.filter (fun s -> s <> "")

                    String.concat "\n\n" (headed.TrimEnd '\n' :: decls) + "\n" |> normalizeEol))
