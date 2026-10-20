namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.Annotations
open Fuaran.Core.Idl.Emit.FSharpDefaults

/// The TypeScript backend's structural encoder/decoder module (`TypeScript` until the Phase 388 split
/// along its banners).
module internal TypeScriptCodec =
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
        | VJson j -> Canonical.write Rendering.CrossHost j

    /// Phase 524 — the TypeScript ENCODER printer over the one slot traversal ([[SlotTraversal]]):
    /// `enc<Name>` functions writing canonical JSON text.
    let tsEncPrinter: AppliedPrinter =
        let backend = "the TypeScript encoder backend"
        let closure = "(() => '\"<closure>\"')"
        let opaque = "(() => '\"<opaque>\"')"

        { Slot =
            { Backend = backend
              Str = "encStr"
              Int = "encInt"
              Bool = "encBool"
              Float = "encFloat"
              // An enum value IS its wire string.
              Enum = fun _ -> "encStr"
              Var = fun v -> "enc" + v
              Named = fun n -> "enc" + n
              Node = "encodeNode"
              Union = fun n args -> "((x) => enc" + n + "(" + String.concat ", " args + ", x))"
              List = fun e -> "((xs) => '[' + xs.map(" + e + ").join(',') + ']')"
              Map =
                fun e ->
                    "((m) => '{' + Object.keys(m).sort().map((k) => encStr(k) + ':' + ("
                    + e
                    + ")(m[k])).join(',') + '}')"
              Closure = closure
              Fn = fun _ -> closure
              Opaque = opaque
              // `encJson` renders the parsed value canonically (see the prelude).
              Json = "encJson"
              // Phase 252 — a hosted slot that declares its wire form is written THROUGH it, so the
              // bytes are that type's canonical ones; one that does not is verbatim JSON to this
              // backend, as to the interpreter.
              Hosted =
                fun h ->
                    match h.Wire with
                    | Some wire ->
                        match hostedWireRefusal backend h with
                        | Some e -> HostedSlot.Refused e
                        | None -> HostedSlot.Through(wire, id)
                    | None -> HostedSlot.Verbatim "encJson" }
          Apply = fun e var -> e + "(" + var + ")"
          ListApplied = fun e var -> "'[' + " + var + ".map(" + e + ").join(',') + ']'"
          NodeApplied = fun var -> "encodeNode(" + var + ")"
          UnionApplied = fun n args var -> "enc" + n + "(" + String.concat ", " args + ", " + var + ")"
          ClosureApplied = fun var -> closure + "(" + var + ")"
          OpaqueApplied = fun var -> opaque + "(" + var + ")" }

    /// The applied TS encode expression for a value of `t` bound to `var`.
    let tsEncApplied (var: string) (t: IdlType) : Result<string, CodegenError> =
        SlotTraversal.encApplied tsEncPrinter var t

    /// Phase 524 — the TypeScript printer of a declared literal as a TEST
    /// ([[LiteralTraversal]]): a boolean predicate that the value read from `src` is the literal.
    /// Enums render as wire strings (`s.tone === "Default"`); nullary unions as
    /// discriminator-tagged objects (`s.format.$type === "None"`); since Phase 124 a
    /// VALUE-CARRYING union case conjoins a test per declared field
    /// (`s.value.$type === "Fixed" && s.value.value === 0`), nested to any depth. An absent
    /// optional is a real conjunct (`s.value.name === undefined`), not a skip: the encoder omits
    /// that key, so a value carrying it is NOT at the default and must not be.
    ///
    /// JS has no structural equality, so every admitted shape reduces to a chain of `===` (plus
    /// the one `.length === 0` an empty array needs, arrays comparing by reference). What is
    /// admitted is [[Decl.literal]]'s, the admissibility the F# backend prints from too, so a
    /// default the F# host honours is one this host can test for.
    let tsPredicatePrinter (disc: string) : LiteralPrinter =
        { Str = fun src s -> src + " === " + SourceLit.tsString s
          Int = fun src i -> src + " === " + string i
          Float = fun src f -> src + " === " + invariantFloat f
          Bool = fun src b -> src + " === " + (if b then "true" else "false")
          // A `VEnum` carries the WIRE string, which is what this host holds.
          Enum = fun src _ _ wire -> src + " === " + SourceLit.tsString wire
          // Phase 1080 — an omitted-when-empty repeated slot.
          EmptyList = fun src -> src + ".length === 0"
          // Phase 539 — the empty-map default, and a JSON default compared by canonical bytes, as the
          // F# backend and the interpreter compare it.
          EmptyMap = fun src -> "Object.keys(" + src + ").length === 0"
          Json =
            fun src j ->
                "encJson("
                + src
                + ") === "
                + SourceLit.tsString (Canonical.write Rendering.CrossHost j)
          Record = fun _ _ members -> members |> List.map snd |> String.concat " && "
          Union =
            fun src u c members ->
                (tsDiscProp disc src + " === " + SourceLit.tsString (u.WireTagOf c.Tag))
                :: (members |> List.map snd)
                |> String.concat " && "
          Some = id
          Absent = fun src -> src + " === undefined"
          Path = fun src f -> src + "." + f.Name }

    /// TS boolean predicate: is `src` at the omit-when-default field's identity default? Printed by
    /// [[tsPredicatePrinter]]. **This predicate decides what the TS backend admits as a default**,
    /// and [[tsDefaultLit]] takes its admissibility from here: an encoder that cannot TEST for a
    /// default must not have a decoder that RESTORES it (Phase 124).
    let tsIsDefault (idl: Idl) (disc: string) (src: string) (t: IdlType) (d: IdlValue) : Result<string, CodegenError> =
        LiteralTraversal.print (tsPredicatePrinter disc) idl src t d

    /// Phase 524 — the TypeScript printer of a member WRITTEN under its presence
    /// ([[MemberEncTraversal.encMember]]): a `[key, enc]` pair, or `null` (filtered) where the
    /// member is absent. `src` is the JS expression the value is read from (`s.f` in a spec or
    /// record encoder, `n.f` on the node envelope, `v.f` in a union case). Phase 317's generative
    /// conformance once caught the union-case copy ignoring presence entirely; one rule is what
    /// keeps that from recurring.
    let tsMemberEncPrinter (idl: Idl) (disc: string) : MemberEncPrinter =
        { Applied = tsEncPrinter
          Key = SourceLit.tsString
          Access = fun recv f -> recv + "." + f.Name
          Present = fun key e -> "[" + key + ", " + e + "]"
          OptionalVar = id
          Optional = fun src key e -> "(" + src + " === undefined ? null : [" + key + ", " + e + "])"
          Absent = "null"
          OmitAt =
            fun src t d ->
                tsIsDefault idl disc src t d
                |> Result.map (fun pred present -> "(" + pred + " ? null : " + present + ")")
          // Phase 539 — this codec does not apply a presence condition; [[Gen.typescriptModule]]
          // refuses a vocabulary that declares one before any member is printed, and this refusal
          // names it should one ever reach here.
          When =
            fun _ _ _ ->
                Error(
                    CodegenError.UnsupportedConstruct(
                        "a presence condition",
                        "GP5: a backend applies a canonical-form declaration or refuses it by name, never ignores it",
                        "generate this vocabulary's codec with the F# backend, which applies it"
                    )
                ) }

    /// A member read off the bound JS variable `recv` (`s` for a spec/record encoder, `n` for the
    /// node envelope) — the F# side's `specPieceOf`.
    let tsSpecPieceOf
        (idl: Idl)
        (disc: string)
        (recv: string)
        (fields: IdlField list)
        (f: IdlField)
        : Result<string, CodegenError> =
        let p = tsMemberEncPrinter idl disc

        MemberEncTraversal.encMember p fields (p.Access recv) f
        |> Result.map (MemberEncTraversal.print p)

    /// The members of a spec or record read off `s`, as the body of a pairs array.
    let private tsObjectPieces (idl: Idl) (disc: string) (fields: IdlField list) : Result<string, CodegenError> =
        let p = tsMemberEncPrinter idl disc

        MemberEncTraversal.encMembers p "s" fields
        |> Result.map (List.map (MemberEncTraversal.print p) >> String.concat ", ")

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

    let tsUnionEncoder (decl: Decl) (disc: string) (u: IdlUnion) =
        let idl = decl.Idl
        let member' = tsMemberEncPrinter idl disc

        let argList =
            match u.Params with
            | [] -> "v"
            | ps -> (ps |> List.map (fun p -> "enc" + p) |> String.concat ", ") + ", v"

        let arm (c: IdlUnionCase) (body: string) =
            let ann =
                match tsAnnotationLines "    " c.Tag c.Annotations with
                | [] -> ""
                | ls -> (ls |> String.concat "\n") + "\n"

            ann
            + "    case "
            + SourceLit.tsString (u.WireTagOf c.Tag)
            + ": return "
            + body
            + ";"

        UnionEncTraversal.encoder
            { Member = member'
              CaseVar = fun f -> "v." + f.Name
              // Transparent case: return the single field's value bare (no `typed(...)`).
              Bare = fun _ c e -> arm c e
              Tagged =
                fun _ c pieces ->
                    arm
                        c
                        ("typed("
                         + SourceLit.tsString (u.WireTagOf c.Tag)
                         + ", ["
                         + (pieces |> List.map (MemberEncTraversal.print member') |> String.concat ", ")
                         + "])")
              Union =
                fun _ arms ->
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
                    + String.concat "\n" arms
                    + "\n  }\n}" }
            decl
            u

    /// A non-discriminated *record* encoder — a plain object, no discriminator,
    /// mirroring `recordEncoder` on the F# side. Added by Phase 690: the TS backend
    /// decoded records but could not ENCODE them, so `tsEncFn`'s `TRecord n -> "enc" + n`
    /// named a function that was never emitted. Harmless while the only IDL the TS
    /// backend ran on had no records, and a `ReferenceError` waiting for the first
    /// one — the node envelope is three of them.
    let tsRecordEncoder (idl: Idl) (disc: string) (r: IdlRecord) =
        tsObjectPieces idl disc r.Fields
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

        tsObjectPieces idl disc k.Fields
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

    /// Phase 524 — the TypeScript DECODER printer over the one slot traversal ([[SlotTraversal]]).
    let tsDecPrinter: SlotPrinter =
        let backend = "the TypeScript decoder backend"

        { Backend = backend
          Str = "dStr"
          Int = "dInt"
          Bool = "dBool"
          Float = "dFloat"
          Enum = fun n -> "dec" + n
          Var = fun v -> "dec" + v
          Named = fun n -> "dec" + n
          Node = "decNode"
          Union = fun n args -> "((x) => dec" + n + "(" + String.concat ", " args + ", x))"
          List = fun d -> "dList(" + d + ")"
          Map = fun d -> "dMap(" + d + ")"
          // The value carries nothing, so it decodes to `null`; but it is READ (Phase 347): the
          // slot holds its one sentinel string, as the interpreter checks. A `TFn` slot is the same
          // on the wire — the TS tier has no `'Msg` to rebuild into, so it stays `null` there
          // regardless of the declared signature.
          Closure = "dSentinel('<closure>')"
          Fn = fun _ -> "dSentinel('<closure>')"
          Opaque = "dSentinel('<opaque>')"
          // Phase 676 — keep the parsed JSON as-is. Hosted slots identically.
          Json = "((x) => x)"
          // Phase 252 — a hosted slot that declares its wire form is CHECKED against it (its
          // type, then its format), which is what makes this host refuse what the F# host's
          // codec refuses.
          Hosted =
            fun h ->
                match h.Wire with
                | Some wire ->
                    match hostedWireRefusal backend h with
                    | Some e -> HostedSlot.Refused e
                    | None ->
                        HostedSlot.Through(
                            wire,
                            fun d ->
                                match h.Format with
                                | Some f ->
                                    "((x) => dFormat("
                                    + SourceLit.tsString (HostedFormat.name f)
                                    + ", "
                                    + d
                                    + "(x)))"
                                | None -> d
                        )
                | None -> HostedSlot.Verbatim "((x) => x)" }

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

    /// Phase 524 — the TypeScript printer of a member READ back out
    /// ([[MemberTraversal.decMember]]): a sentinel slot decodes to `null`, a host-only member to
    /// `undefined`, an omit-at-default one to [[tsDefaultLit]]'s literal.
    let tsMemberDecPrinter (idl: Idl) (disc: string) : MemberDecPrinter =
        { Slot = tsDecPrinter
          Key = SourceLit.tsString
          Object = fun _ _ -> "fs"
          Req = fun key o d -> "dReq(" + key + ", " + o + ", " + d + ")"
          Opt = fun key o d -> "dOpt(" + key + ", " + o + ", " + d + ")"
          Def = fun key o d lit -> "dDef(" + key + ", " + o + ", " + d + ", " + lit + ")"
          Placeholder = fun _ -> "null"
          HostOnlySentinel = id
          HostOnly = fun _ -> Ok "undefined"
          DefaultLit = tsDefaultLit idl disc }

    let tsDecField (idl: Idl) (disc: string) (fields: IdlField list) (f: IdlField) : Result<string, CodegenError> =
        MemberTraversal.decMember (tsMemberDecPrinter idl disc) fields f

    /// The object a TS decoder builds: `extra` (the discriminator, where one is written), then
    /// every member as read, in declaration order.
    let tsObjectLit (extra: (string * string) list) (members: (IdlField * string) list) : string =
        let pairs =
            (extra |> List.map (fun (k, v) -> k + ": " + v))
            @ (members |> List.map (fun (f, e) -> SourceLit.tsString f.Name + ": " + e))

        "{ " + String.concat ", " pairs + " }"

    let tsFieldObject
        (idl: Idl)
        (disc: string)
        (extra: (string * string) list)
        (fields: IdlField list)
        : Result<string, CodegenError> =
        MemberTraversal.decMembers (tsMemberDecPrinter idl disc) fields
        |> Result.map (tsObjectLit extra)

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

    let tsUnionDecoder (decl: Decl) (disc: string) (u: IdlUnion) =
        let idl = decl.Idl

        let argList =
            match u.Params with
            | [] -> "j"
            | ps -> (ps |> List.map (fun p -> "dec" + p) |> String.concat ", ") + ", j"

        let arm (c: IdlUnionCase) (members: (IdlField * string) list) =
            let ann =
                match tsAnnotationLines "    " c.Tag c.Annotations with
                | [] -> ""
                | ls -> (ls |> String.concat "\n") + "\n"

            ann
            + "    case "
            + SourceLit.tsString (u.WireTagOf c.Tag)
            + ": return "
            + tsObjectLit [ tsDiscKey disc, SourceLit.tsString (u.WireTagOf c.Tag) ] members
            + ";"

        UnionTraversal.decoder
            { Read = MemberTraversal.decMembers (tsMemberDecPrinter idl disc)
              Slot = tsDecPrinter
              Arm = fun _ c members -> arm c members
              // A transparent union also accepts its single-field case bare, and re-wraps
              // it so the encoder can flatten it back to the same bytes.
              Bare =
                fun _ c f dfn ->
                    "  return { "
                    + tsDiscKey disc
                    + ": "
                    + SourceLit.tsString (u.WireTagOf c.Tag)
                    + ", "
                    + SourceLit.tsString f.Name
                    + ": "
                    + dfn
                    + "(j) };"
              NoBare =
                fun _ ->
                    "  return dFail('WrongKind', 'object', "
                    + SourceLit.tsString ("expected a " + u.Name + " object")
                    + ");"
              Union =
                fun _ arms untagged ->
                    // Phase 337 — every object is a tagged one, as the interpreter reads it: an
                    // absent or non-string discriminator is refused AT it (`dTag`), and an unknown
                    // case is `UnknownTag` there.
                    let tagged =
                        "  if (isObj(j)) {\n    const fs = j;\n    switch (dTag(j)) {\n"
                        + String.concat "\n" arms
                        + "\n      default: return dUnknown("
                        + SourceLit.tsString (oneOf (u.Cases |> List.map (fun c -> u.WireTagOf c.Tag)))
                        + ", "
                        + SourceLit.tsString ("unknown " + u.Name + " case: ")
                        + " + "
                        + tsDiscProp disc "j"
                        + ");\n    }\n  }"

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
                    + "\n}" }
            decl
            u

    /// An object's decoder — a declared record, or a kind's spec under its discriminator.
    let tsObjectDecoder
        (idl: Idl)
        (disc: string)
        (header: string)
        (name: string)
        (extra: (string * string) list)
        (fields: IdlField list)
        =
        tsFieldObject idl disc extra fields
        |> Result.map (fun obj ->
            header
            + "function dec"
            + name
            + "(j) {\n  const fs = dObj(j);\n  return "
            + obj
            + ";\n}")

    let tsRecordDecoder (idl: Idl) (disc: string) (r: IdlRecord) =
        tsObjectDecoder idl disc (tsFieldAnnotationHeader r.Name r.Fields) r.Name [] r.Fields

    let tsSpecDecoder (idl: Idl) (disc: string) (k: IdlKind) =
        tsObjectDecoder
            idl
            disc
            (tsKindAnnotationHeader k)
            (k.Tag + "Spec")
            [ tsDiscKey disc, SourceLit.tsString k.Tag ]
            k.Fields

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

        (Template.text "ts-decode-prelude.js").Replace("__DISC__", discLit)

    /// Phase 337 — the parse leg of the TypeScript `decodeNode`, held to the F# reader's
    /// answers (`Decoder.parse`): text the reader cannot parse is `InvalidJson` at the root, and
    /// a container opened past its nesting cap is `LimitExceeded` at that container's path (Phase
    /// 519: the reader keeps the path of the value it is reading, as the F# scanner's frames do).
    ///
    /// Phase 347 — the leg IS a reader now, the F# reader's twin (`Json.parse`), not `JSON.parse`
    /// behind a scan. `JSON.parse` answers five questions differently from it, and each answer
    /// reached the decode: it keeps the LAST of a repeated member (the F# reader refuses one,
    /// since Phase 462); it reads `1.0` and `1e0` as the number `1`, so
    /// an int slot cannot see they were float tokens; it reads a literal past the double range as
    /// `Infinity` and an integer past 2^53 by rounding it, where the F# reader refuses both; it
    /// admits a lone surrogate, where the F# reader refuses it; and it reads `null` and any depth.
    /// (A raw control character in a string both refuse, since Phase 462.) This reader holds the text to
    /// the F# grammar in the F# order — the first fault met is the one reported, so a malformed
    /// prefix still outranks a container past the cap — and it records, beside the value, what a
    /// JS value cannot carry (`dFloatAt`, `dMembersOf`). An object is built with every key as
    /// data: `__proto__` becomes an own member, never the object's prototype.
    let tsParseLeg =
        (Template.text "ts-parse-leg.js").Replace("__CAP__", string Json.defaultMaxDepth)

    /// Phase 252 — the TypeScript check of a hosted slot's declared FORMAT, emitted only when
    /// the vocabulary declares one ([[declaresHostedFormat]]); the JS face of
    /// [[HostedFormat.admits]], held to the same answers by a test. Returns the value, so a
    /// decoder composes it in place.
    let tsFormatHelper = (Template.text "ts-format-helper.js")

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
    ///
    /// Phase 505 — this is the module UNSTAMPED, so a caller that appends to it (the derivations)
    /// stamps the text it actually emits; [[typescriptModule]] is it under its [[Stamp]].
    let typescriptModuleBody (decl: Decl) : Result<string, CodegenError> =
        // Phase 511 — the declaration graph is the resolution's (this body used to walk it twice).
        let idl, kinds, enums, unions, records =
            decl.Idl, decl.Kinds, decl.Enums, decl.Unions, decl.Records

        // Runtime prelude — escaping mirrors Fuaran.Core.Json.escape (WIRE_FORMAT §2
        // rule 6: only " and \ and control chars as \u00xx — NO \n/\r/\t shortcuts),
        // and object-field order is author order (Canonical.write Rendering.CrossHost does not sort keys),
        // so the bytes match the F# host across ALL strings (incl. control chars).
        // Phase 370 — `encStr` mirrors Phase 365's fast path: one regex test for an escapable
        // character (`"`, `\`, U+0000–U+001F, matched per UTF-16 code unit, so a lone surrogate
        // is never escapable on either host) and the input returned whole between quotes when
        // there is none. The escaping loop below it is unchanged, so the bytes are too
        // (`StringEscapeTests` runs the emitted function against the .NET escaper under node).
        let prelude =
            """const encStr = (s) => {
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
// caller's declaration order. `Canonical.write Rendering.CrossHost` sorts every object's keys Ordinal on
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
            // among the pairs, or the emission diverges from `Canonical.write Rendering.CrossHost`.
            //
            // Phase 111 — under DECLARED key order there is no sort at all: the
            // pairs arrive in declaration order and the discriminator leads, so
            // `typed` / `plain` preserve construction order, exactly as the F#
            // side's `Canonical.write Rendering.Declared` does.
            + (match idl.Wire.KeyOrder with
               | KeyOrder.Declared ->
                   // The canonical key text is computed HERE and spliced as one escaped
                   // literal, so the emitted prefix for a plain key is the bytes it always was.
                   "\nconst typed = (tag, pairs) =>\n  "
                   + SourceLit.tsStringSingle (
                       "{" + Canonical.write Rendering.CrossHost (JStr idl.Wire.Discriminator) + ":"
                   )
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
        let flat = decl.Envelope.Kind = KindPlacement.Merged

        // Phase 111 — a TJson value under declared order is carried in its
        // authored order (the map encoder, a different receiver, stays sorted).
        let prelude =
            match idl.Wire.KeyOrder with
            | KeyOrder.Sorted -> prelude
            | KeyOrder.Declared ->
                prelude.Replace("const keys = Object.keys(v).sort();", "const keys = Object.keys(v);")

        let kindDispatch =
            match decl.Envelope.Kind with
            | KindPlacement.NestedUnder key ->
                let arms =
                    kinds
                    |> List.map (fun k -> "    case " + SourceLit.tsString k.Tag + ": return enc" + k.Tag + "Spec(k);")
                    |> String.concat "\n"

                // Phase 690 — `id` / `kind` / the envelope, merged and sorted Ordinal so
                // the TS emission order matches F#'s canonical key sort. With no envelope
                // this is `id` then `kind`, i.e. exactly the previous hand-built literal.
                // Phase 111 — declared order keeps the construction order instead:
                // id, kind, then the envelope as declared.
                decl.Envelope.Members
                |> List.map (fun f ->
                    tsSpecPieceOf idl disc "n" decl.Envelope.Members f
                    |> Result.map (fun p -> f.Name, p))
                |> sequenceR
                |> Result.map (fun envelopePairs ->
                    let nodePairsUnsorted =
                        ("id", "[\"id\", encStr(n.id)]")
                        :: (key, "[" + SourceLit.tsString key + ", encKind(n.kind)]")
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
            | KindPlacement.Merged ->
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
                    match decl.Envelope.Members with
                    | [] -> Ok ""
                    | fields ->
                        fields
                        |> List.map (tsSpecPieceOf idl disc "n" fields)
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

        let kindDecodeDispatch =
            let arms =
                kinds
                |> List.map (fun k -> "    case " + SourceLit.tsString k.Tag + ": return dec" + k.Tag + "Spec(j);")
                |> String.concat "\n"

            let decKind =
                "function decKind(j) {\n  if (!isObj(j)) return dFail('WrongKind', 'object', 'expected a kind object');\n  switch (dTag(j)) {\n"
                + arms
                + "\n    default: return dUnknown("
                + SourceLit.tsString (oneOf (kinds |> List.map _.Tag))
                + ", 'unknown node kind: ' + "
                + tsDiscProp disc "j"
                + ");\n  }\n}\n\n"

            let envelopeDecoded =
                decl.Envelope.Members
                |> List.map (fun f ->
                    tsDecField idl disc decl.Envelope.Members f
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
          unions |> List.map (tsUnionEncoder decl disc)
          kinds |> List.map (tsSpecEncoder idl disc flat)
          [ kindDispatch ]
          Ok(tsDecodePrelude disc)
          :: (if decl.HostedFormat then [ Ok tsFormatHelper ] else [])
          enums |> List.map (tsEnumDecoder >> Ok)
          records |> List.map (tsRecordDecoder idl disc)
          unions |> List.map (tsUnionDecoder decl disc)
          kinds |> List.map (tsSpecDecoder idl disc)
          [ kindDecodeDispatch ]
          [ Ok "export { encodeNode, decodeNode };" ] ]
        |> List.concat
        |> concatR "\n\n"
        // Phase 129 — the emitted TypeScript is LF-terminated whatever this generator was built
        // from; the three preludes above are multi-line templates and would otherwise carry the
        // line endings of whichever checkout compiled them. See [[normalizeEol]].
        |> Result.map normalizeEol

    /// Phase 505 — a TypeScript module's [[Stamp]], over its normalised text.
    let tsModuleStamp (idl: Idl) (body: string) : string =
        Stamp.generated (CommentStyle.Line "//") "TypeScript module" [ idl ] body

    /// The module [[typescriptModuleBody]] emits, under its stamp.
    let typescriptModule (decl: Decl) : Result<string, CodegenError> =
        typescriptModuleBody decl |> Result.map (tsModuleStamp decl.Idl)
