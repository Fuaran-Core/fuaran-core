namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.Reach
open Fuaran.Core.Idl.Emit.Annotations
open Fuaran.Core.Idl.Emit.FSharpTypes
open Fuaran.Core.Idl.Emit.FSharpDefaults

/// The F# structural-layer CODEC: the encoder and decoder groups, the witness, the validator
/// scaffold, and the module that assembles them around the type group.
module internal FSharpCodec =

    // -----------------------------------------------------------------------
    // Phase 317 increment 3 — *feature-complete* code emission: emit a
    // self-contained, compiling F# encoder module for a set of kinds, handling
    // every feature class the interpreter does — Required + Optional fields
    // (omit-on-absence via List.choose), parameterised unions (`Binding<'T>`, by
    // codec-passing), lists, and node nesting (a recursive `encNode`). The proof
    // that the generator — not just the interpreter — covers the whole surface.
    // -----------------------------------------------------------------------

    /// Point-free encoder *function* for a type (`'a -> JVal`) — used where an encoder
    /// must be passed (a generic union's type-parameter codec).
    let rec encFn (t: IdlType) : Result<string, CodegenError> =
        match t with
        | TStr -> Ok "JStr"
        | TInt -> Ok "JInt"
        | TBool -> Ok "JBool"
        // NOT the bare `JFloat` constructor: a non-finite double has no JSON number
        // spelling and rides as a quoted sentinel string (`encodeHelpers` below).
        // `TInt` keeps its constructor — WIRE_FORMAT §7 truncates at a float slot.
        | TFloat -> Ok "encFloat"
        | TEnum n -> Ok("enc" + n)
        | TVar v -> Ok("enc" + v)
        | TUnion(n, []) -> Ok("enc" + n)
        | TUnion(n, args) ->
            args
            |> List.map encFn
            |> concatR " "
            |> Result.map (fun a -> "(enc" + n + " " + a + ")")
        | TNode -> Ok "encNode"
        // Phase 195 — the op vocabulary is REFUSED AS DATA rather than thrown at. See
        // [[opVocabularySlot]] for why the arm exists and what it says.
        | TKind
        | TOp -> Error(opVocabularySlot "the F# encoder emitter" t)
        | TList inner -> encFn inner |> Result.map (sprintf "(fun __xs -> JArr(List.map %s __xs))")
        // A closure/opaque codec ignores its argument and emits the fixed sentinel.
        | TClosure
        | TFn _ -> Ok "(fun _ -> JStr \"<closure>\")"
        | TOpaque -> Ok "(fun _ -> JStr \"<opaque>\")"
        // Phase 676 — verbatim passthrough. `Canon.render` already sorts keys Ordinal,
        // escapes per rule 6 and lays floats out per rule 5, so identity inherits all
        // three rather than re-implementing them — the risk this phase named.
        | TJson -> Ok "id"
        // The named host encode expression, verbatim ('host -> JVal). Canonicality is
        // inherited: the host codec builds a JVal that renders through the same Canon.
        | THosted h -> Ok h.Encode
        | TRecord n -> Ok("enc" + n)
        | TMap vt ->
            encFn vt
            |> Result.map (sprintf "(fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, %s v)))")

    /// The applied JVal expression for a value of `t` bound to `var`.
    let encApplied (var: string) (t: IdlType) : Result<string, CodegenError> =
        match t with
        | TList inner -> encFn inner |> Result.map (fun e -> sprintf "JArr(List.map %s %s)" e var)
        | TNode -> Ok(sprintf "encNode %s" var)
        | TClosure
        | TFn _ -> Ok "JStr \"<closure>\""
        | TOpaque -> Ok "JStr \"<opaque>\""
        | _ -> encFn t |> Result.map (fun e -> sprintf "%s %s" e var)

    /// The encode-side helper prelude, emitted once per module — the mirror of
    /// [[decodeHelpers]]. Only the float slot needs one: every other primitive is
    /// its `JVal` constructor.
    let encodeHelpers (idl: Idl) : string =
        let base' =
            """// WIRE_FORMAT §5 — a non-finite double has no JSON *number* spelling, so it rides as
// one of the three quoted sentinel strings, which §7 requires a decoder to read back
// AT A FLOAT SLOT (`dFloat` below; `dInt` is deliberately not widened — §7 stops at
// the float slot, and an integer slot has no sentinel).
//
// Building the `JStr` HERE rather than leaving `Canon.render` to spell a non-finite
// `JFloat` is what keeps the emitted `JVal` renderable by the GUARDED
// `Fuaran.Core.Wire.tryRender`, which refuses a non-finite `JFloat` outright. The core
// wire model still has no non-finite float — the sentinel is a string, which it carries
// perfectly — so this widens the generated float slot's spelling, not the model.
let private encFloat (f: float) : JVal =
    if System.Double.IsNaN f then JStr "NaN"
    elif System.Double.IsPositiveInfinity f then JStr "Infinity"
    elif System.Double.IsNegativeInfinity f then JStr "-Infinity"
    else JFloat f"""

        // Phase 108 — the declared discriminator. A default-shape IDL emits no
        // helper (its call sites reference `Canon.typed`), so its module is
        // byte-identical to every pre-declarable emission.
        if idl.Wire.Discriminator = "$type" then
            base'
        else
            base'
            + sprintf
                "\n\n// Phase 108 — `Canon.typed` under this vocabulary's DECLARED discriminator key.\nlet private typedTag (tag: string) (fields: (string * JVal) list) : JVal =\n    JObj((%s, JStr tag) :: fields)"
                (SourceLit.fsString idl.Wire.Discriminator)


    /// The omit-at-default `(string * JVal) option` piece, shared by the spec/record encoder and
    /// the union-case encoder — they differ only in how the value is BOUND (`s.Field` vs a
    /// positional binding), never in how the default is tested, and keeping one copy is what
    /// stops the two halves from drifting apart under a widening like Phase 124's.
    let omitPiece (idl: Idl) (src: string) (f: IdlField) (d: IdlValue) : Result<string, CodegenError> =
        fsDefaultLit idl f.Type d
        |> Result.bind (fun dexpr ->
            encApplied src f.Type
            |> Result.map (fun enc ->
                match f.Type with
                // A UNION default is tested by pattern-match, not `=`. Phase 691: typing a
                // closure slot gives its owning union a function-typed field, and F#
                // functions support no equality, so the union stops supporting the
                // `equality` constraint entirely — `CellFormat.Custom of (obj -> string)`
                // broke `s.Format = CellFormat.None` for every column. A match is also
                // simply the better test: it needs no constraint, and reads as what it is.
                // Phase 124 — this is also why a value-carrying default works at all: the
                // rendered literal is a legal PATTERN as well as a legal expression.
                | TUnion _ ->
                    sprintf
                        "(match %s with | %s -> None | _ -> Some(%s, %s))"
                        src
                        dexpr
                        (SourceLit.fsString f.Name)
                        enc
                // Phase 1080 — a LIST default is tested with `List.isEmpty`, never with
                // `= []`, and for the same reason the union arm above exists: an element
                // type that reaches a closure carries no equality, so `SrcSetEntry`
                // (whose `src` is a `Binding`) fails the constraint the moment the
                // generated encoder is compiled. `List.isEmpty` imposes none, and it is
                // the better test anyway — it says what it means. (`fsDefaultLit` admits
                // only the EMPTY list, so there is no non-empty case to answer for.)
                | TList _ ->
                    sprintf "(if List.isEmpty %s then None else Some(%s, %s))" src (SourceLit.fsString f.Name) enc
                | _ -> sprintf "(if %s = %s then None else Some(%s, %s))" src dexpr (SourceLit.fsString f.Name) enc))

    /// One field of a record-spec encoder, as a `(string * JVal) option` for `List.choose id`
    /// (Required → always `Some`; Optional → omit-on-`None`; OmitDefault → omit-at-default).
    /// `recv` is the bound record variable — `s` for a spec/record encoder, `n` for
    /// the node envelope (Phase 690), which reuses this presence machinery unchanged.
    let specPieceOf (idl: Idl) (recv: string) (f: IdlField) : Result<string, CodegenError> =
        let src = recv + "." + pascal f.Name

        match f.Opt with
        | Required ->
            encApplied src f.Type
            |> Result.map (fun e -> sprintf "Some(%s, %s)" (SourceLit.fsString f.Name) e)
        | Optional ->
            encApplied "v" f.Type
            |> Result.map (fun e -> sprintf "(%s |> Option.map (fun v -> %s, %s))" src (SourceLit.fsString f.Name) e)
        // Phase 691 — never on the wire, in any state.
        | HostOnly -> Ok "None"
        | OmitDefault d -> omitPiece idl src f d

    /// One `"key", <enc>` pair of a *required* union-case field (positional binding; the wire
    /// key is the raw field name, the value reference is keyword-escaped).
    let casePair (f: IdlField) : Result<string, CodegenError> =
        encApplied (ident f.Name) f.Type
        |> Result.map (fun e -> sprintf "%s, %s" (SourceLit.fsString f.Name) e)

    /// One `(string * JVal) option` piece of a union-case encoder — `Some` for a required field,
    /// omit-on-`None` for an optional one (`CellFormat.Number`'s `decimals`, `Format.Percent`,
    /// `FormFieldKind.RangedNumber`'s `min`/`max`/`step`, `HoleDecl.Value`'s `default`). Mirrors
    /// [[specPieceOf]] for the `List.choose id` shape, but binds the *positional* case field.
    let casePiece (idl: Idl) (f: IdlField) : Result<string, CodegenError> =
        let src = ident f.Name

        match f.Opt with
        | Required ->
            encApplied src f.Type
            |> Result.map (fun e -> sprintf "Some(%s, %s)" (SourceLit.fsString f.Name) e)
        | Optional ->
            encApplied "v" f.Type
            |> Result.map (fun e -> sprintf "(%s |> Option.map (fun v -> %s, %s))" src (SourceLit.fsString f.Name) e)
        // Phase 691 — never on the wire, in any state.
        | HostOnly -> Ok "None"
        | OmitDefault d -> omitPiece idl src f d

    let enumEncoder (e: IdlEnum) =
        // The case name is the F# identifier, the wire string is what goes on the
        // wire — identical unless the enum declares a mapping (Phase 707).
        let arms =
            e.Cases
            |> List.map (fun c -> sprintf "    | %s.%s -> JStr %s" e.Name c (SourceLit.fsString (e.WireOf c)))
            |> String.concat "\n"

        sprintf "let private enc%s (v: %s) : JVal =\n    match v with\n%s" e.Name e.Name arms

    /// A union encoder (an `and`-member of the recursive group). Generic unions take one
    /// `encX : 'X -> JVal` codec per type parameter. `typedName` is the emitted
    /// discriminated-object builder — `Canon.typed` on a default-shape IDL
    /// (byte-identical to every pre-declarable emission), the module-local
    /// `typedTag` when the vocabulary declares another key (Phase 108).
    let unionEncoder
        (typedName: string)
        (docFn: string -> string -> string)
        (tokens: HardenPolicy)
        (msg: Set<string>)
        (idl: Idl)
        (u: IdlUnion)
        : Result<string, CodegenError> =
        let encArgs =
            u.Params
            |> List.map (fun p -> sprintf " (enc%s: '%s -> JVal)" p p)
            |> String.concat ""

        let tyArgs = declParams msg u.Name u.Params

        let arm (c: IdlUnionCase) =
            let pat =
                match c.Fields with
                | [] -> ""
                | [ f ] -> " " + ident f.Name
                | fs -> " (" + (fs |> List.map (fun f -> ident f.Name) |> String.concat ", ") + ")"

            // A DECLARED transparent case emits its single field's value BARE — no
            // `Canon.typed` wrapper — the bare-string canonical literal shape.
            match TransparentUnion.tag tokens u with
            | Some ttag when ttag = c.Tag ->
                match c.Fields with
                | [ f ] ->
                    encApplied (ident f.Name) f.Type
                    |> Result.map (fun e -> sprintf "    | %s.%s%s -> %s" u.Name c.Tag pat e)
                | _ -> Error(transparentArity u.Name c.Tag)
            | _ ->
                // All-required cases keep the simple literal list (byte-identical to the pre-optional
                // emission); any optional field switches to the `List.choose id` omit-on-absence form.
                if c.Fields |> List.forall (fun f -> f.Opt = Required) then
                    c.Fields
                    |> List.map casePair
                    |> concatR "; "
                    |> Result.map (fun pairs ->
                        sprintf
                            "    | %s.%s%s -> %s %s [ %s ]"
                            u.Name
                            c.Tag
                            pat
                            typedName
                            (SourceLit.fsString c.Tag)
                            pairs)
                else
                    c.Fields
                    |> List.map (casePiece idl)
                    |> concatR "; "
                    |> Result.map (fun pieces ->
                        sprintf
                            "    | %s.%s%s -> %s %s ([ %s ] |> List.choose id)"
                            u.Name
                            c.Tag
                            pat
                            typedName
                            (SourceLit.fsString c.Tag)
                            pieces)

        let armsR =
            u.Cases
            |> List.map (fun c ->
                arm c
                |> Result.map (fun a -> docFn ("encarm:" + u.Name + "." + c.Tag) "    " + a))
            |> concatR "\n"
        // The explicit `<'T>` type-parameter list (not just `'T` free in the signature) is
        // load-bearing for a generic union: `Binding.Format.source` is a fixed `Binding<float>`
        // *inside* `Binding<'T>`, so the generated `encBinding` recurses at a concrete type ≠ the
        // ambient `'T` — **polymorphic recursion**, which F# permits only under an explicit
        // generic-parameter declaration. Without it, `encBinding` monomorphises to the first use
        // (string) and the `float` recursion fails to type-check.
        armsR
        |> Result.map (fun arms ->
            sprintf
                "and private enc%s%s%s (v: %s%s) : JVal =\n    match v with\n%s"
                u.Name
                tyArgs
                encArgs
                u.Name
                tyArgs
                arms)

    let specEncoder (typedName: string) (msg: Set<string>) (idl: Idl) (k: IdlKind) : Result<string, CodegenError> =
        // Single-line list literal — avoids F# offside-rule pitfalls in generated code.
        k.Fields
        |> List.map (specPieceOf idl "s")
        |> concatR "; "
        |> Result.map (fun pieces ->
            sprintf
                "and private enc%sSpec%s (s: %sSpec%s) : JVal =\n    %s %s ([ %s ] |> List.choose id)"
                k.Tag
                (declParams msg (k.Tag + "Spec") [])
                k.Tag
                (declParams msg (k.Tag + "Spec") [])
                typedName
                (SourceLit.fsString k.Tag)
                pieces)

    /// A non-discriminated *record* encoder — a plain `JObj` (no `$type`), fields via `List.choose
    /// id` (omit-on-absence for optionals). `Canon.render` Ordinal-sorts keys, so emission order is
    /// irrelevant. Reuses [[specPieceOf]] (`s.<Pascal>` field access). New for the real tier
    /// (`InvokeArg`, `FormField`, `FilterSpec`, `TabHeader`, `ColumnErased`, `ContentHash`, …).
    let recordEncoder (msg: Set<string>) (idl: Idl) (r: IdlRecord) : Result<string, CodegenError> =
        let ps = declParams msg r.Name []

        r.Fields
        |> List.map (specPieceOf idl "s")
        |> concatR "; "
        |> Result.map (fun pieces ->
            sprintf
                "and private enc%s%s (s: %s%s) : JVal =\n    JObj([ %s ] |> List.choose id)"
                r.Name
                ps
                r.Name
                ps
                pieces)

    // -----------------------------------------------------------------------
    // Phase 672 — the structural DECODER leg.
    //
    // The inverse of the encoder emitters above, and deliberately only the
    // STRUCTURAL half: which `$type` maps to which case, which fields, which
    // types. The decode-side *policy* a schema cannot describe — the canonical
    // diagnostic codes with `$`-rooted paths, §16 lenient-accept normalisation,
    // and the reject set — stays hand-written ABOVE this, exactly as the
    // `'Msg`-generic author facades sit above the generated encoder.
    //
    // Phase 337 — the generated refusal is Core's typed `DecodeError` (Phase 310): a
    // `DecodeCode`, the path from the value the outermost decoder was handed, what the
    // position expected, and a sentence. The CODE and the PATH are the interpreter's
    // (`Idl.Decode.decodeDetailed`) for the same document, so a host built from this
    // module and a host running the interpreter refuse one malformed document the same
    // way; the three-way differential holds them to it. The SENTENCE is this layer's own
    // and is the string it returned before the phase, so `DecodeError.describe` reads a
    // refusal exactly as the pre-337 decoder worded it — except where the refusal itself
    // moved to the interpreter's (an object with no discriminator at a union slot).
    //
    // Two inversions are NOT symmetric with the encoder, and are the ones to get
    // right:
    //   * a closure / opaque slot carries a sentinel that holds no information, so
    //     the decoded value is `()` or the declared placeholder rather than anything
    //     read off the wire — but the slot IS read (Phase 347): present, it must be
    //     its sentinel, and required, it must be present, as the interpreter checks.
    //   * a whole-valued float renders without a decimal point, so it parses
    //     back as `JInt` — `dFloat` accepts both.
    // -----------------------------------------------------------------------

    /// The decoder expression for a type — a `JVal -> Result<'T, DecodeError>`.
    /// Mirrors [[encFn]] arm for arm.
    let rec decFn (t: IdlType) : Result<string, CodegenError> =
        match t with
        | TStr -> Ok "dStr"
        | TInt -> Ok "dInt"
        | TBool -> Ok "dBool"
        | TFloat -> Ok "dFloat"
        | TEnum n -> Ok("dec" + n)
        | TVar v -> Ok("dec" + v)
        | TUnion(n, []) -> Ok("dec" + n)
        | TUnion(n, args) ->
            args
            |> List.map decFn
            |> concatR " "
            |> Result.map (fun a -> "(dec" + n + " " + a + ")")
        | TNode -> Ok "decNode"
        // Phase 195 — the op vocabulary is REFUSED AS DATA rather than thrown at. See
        // [[opVocabularySlot]] for why the arm exists and what it says.
        | TKind
        | TOp -> Error(opVocabularySlot "the F# decoder emitter" t)
        | TList inner -> decFn inner |> Result.map (sprintf "(dList %s)")
        // Phase 347 — the sentinel is checked (`dSentinel`), as the interpreter checks it.
        | TClosure -> Ok "(dSentinel \"<closure>\")"
        | TOpaque -> Ok "(dSentinel \"<opaque>\")"
        // Phase 689 — a `TFn` slot decodes to its declared placeholder. There is
        // nothing on the wire to rebuild a closure from, so the decoded tree is the
        // storage shape and the placeholder is what a host re-attaches over.
        | TFn s ->
            Ok(sprintf "(fun (__j: JVal) -> dSentinel \"<closure>\" __j |> Result.map (fun () -> %s))" s.Placeholder)
        // Phase 676 — accept any JSON verbatim; a shape check would contradict the
        // field's contract.
        | TJson -> Ok "dJson"
        // The named host decode expression, verbatim (JVal -> Result<'host, string>).
        // Phase 252 — a declared wire form is checked FIRST (its type, then its format),
        // so this host refuses exactly what the interpreter and the TypeScript host refuse,
        // whatever the codec itself would admit.
        // Phase 337 — the codec's own refusal is a SENTENCE (its contract is unchanged), lifted
        // by `dHosted` to `OutOfRange` at the slot.
        | THosted h ->
            match hostedWireRefusal "the F# decoder emitter" h with
            | Some e -> Error e
            | None ->
                match h.Wire with
                | None -> Ok(sprintf "(fun (__j: JVal) -> dHosted ((%s) __j))" h.Decode)
                | Some w ->
                    decFn w
                    |> Result.map (fun wd ->
                        let format =
                            match h.Format with
                            | Some f -> sprintf " |> Result.bind (fun _ -> dFormat %s __j)" (SourceLit.fsString f)
                            | None -> ""

                        sprintf
                            "(fun (__j: JVal) -> %s __j%s |> Result.bind (fun _ -> dHosted ((%s) __j)))"
                            wd
                            format
                            h.Decode)
        | TRecord n -> Ok("dec" + n)
        | TMap vt -> decFn vt |> Result.map (sprintf "(dMap %s)")

    /// Reading one field back out, honouring the presence rules [[specPieceOf]] /
    /// [[casePiece]] wrote it under.
    let decField (idl: Idl) (f: IdlField) : Result<string, CodegenError> =
        match f.Type with
        // The VALUE is a sentinel and carries nothing, so what decodes is `()` — or, for a
        // `TFn` slot (Phase 689), its declared placeholder. An OPTIONAL slot's PRESENCE is real
        // wire information — the encoder omits the key when `None` and emits the sentinel when
        // `Some` — so reading it is what makes the decode a structural inverse (a flat `Ok None`
        // silently dropped the field, caught by the corpus round-trip gate on `grid-1`'s
        // optional `rowKey`).
        //
        // Phase 347 — and the slot is READ like any other, as the interpreter reads it: a
        // required one absent is `MissingField`, a present one must be its sentinel (`dSentinel`:
        // another string is `OutOfRange`, another kind `WrongKind`). Until 347 a required slot
        // was never looked for and an optional one was read for its presence only.
        | TClosure
        | TOpaque
        | TFn _ ->
            let name = SourceLit.fsString f.Name

            let placeholder =
                match f.Type with
                | TFn s -> s.Placeholder
                | _ -> "()"

            decFn f.Type
            |> Result.map (fun d ->
                match f.Opt with
                | Required -> sprintf "dReq %s __fs %s" name d
                | Optional -> sprintf "dOpt %s __fs %s" name d
                | OmitDefault _ -> sprintf "dDef %s __fs %s (%s)" name d placeholder
                // Never on the wire — nothing to read, so take the placeholder.
                | HostOnly -> sprintf "Ok (%s)" placeholder)
        | _ ->
            match f.Opt with
            | Required ->
                decFn f.Type
                |> Result.map (fun d -> sprintf "dReq %s __fs %s" (SourceLit.fsString f.Name) d)
            | Optional ->
                decFn f.Type
                |> Result.map (fun d -> sprintf "dOpt %s __fs %s" (SourceLit.fsString f.Name) d)
            // Never on the wire — nothing to read, so take the declared placeholder.
            | HostOnly -> hostOnlyLit f |> Result.map (sprintf "Ok (%s)")
            // Phase 124 — the decoder's optional arm and the encoder's omit test are now
            // rendered from ONE literal, so they cannot disagree. Before this, an
            // unrenderable default fell through to `dReq` here and to always-emit there —
            // consistent with each other and with nothing else, least of all the IDL.
            | OmitDefault d ->
                fsDefaultLit idl f.Type d
                |> Result.bind (fun dexpr ->
                    decFn f.Type
                    |> Result.map (fun dfn -> sprintf "dDef %s __fs %s (%s)" (SourceLit.fsString f.Name) dfn dexpr))

    /// Nest one `Result.bind` per field over `final`, then close the lot. F# has
    /// no applicative sugar for this, and the generated file is Fantomas-exempt,
    /// so the nesting is emitted explicitly rather than prettified.
    let bindChain (indent: string) (binders: (string * string) list) (final: string) (extraCloses: int) : string =
        let opens =
            binders
            |> List.map (fun (v, e) -> sprintf "%s%s |> Result.bind (fun %s ->" indent e v)

        let closes = String.replicate (List.length binders + extraCloses) ")"
        (opens @ [ indent + final + closes ]) |> String.concat "\n"

    /// [[bindChain]] for a case whose final is a declared case REFINE (Phase 945): the last
    /// binder is `dRefine` rather than `Result.bind`, because a refine answers a sentence
    /// (`Result<_, string>`) and every other step a typed refusal (Phase 337). Binding the
    /// last step that way leaves the refine's text exactly where it always sat — its own
    /// continuation lines are indented for that column, so it cannot be wrapped in place.
    let bindChainRefined (indent: string) (binders: (string * string) list) (final: string) : string =
        let count = List.length binders

        let opens =
            binders
            |> List.mapi (fun i (v, e) ->
                let bind = if i = count - 1 then "dRefine" else "Result.bind"
                sprintf "%s%s |> %s (fun %s ->" indent e bind v)

        (opens @ [ indent + final + String.replicate count ")" ]) |> String.concat "\n"

    let fieldBinders (idl: Idl) (fs: IdlField list) : Result<(string * string) list, CodegenError> =
        fs
        |> List.map (fun f -> decField idl f |> Result.map (fun e -> ident f.Name, e))
        |> sequenceR

    /// An enum decoder (Phase 337): a string naming no case is `UnknownTag`, anything else
    /// `WrongKind` — the interpreter's split. Both keep the pre-337 sentence.
    let enumDecoder (e: IdlEnum) =
        let arms =
            e.Cases
            |> List.map (fun c -> sprintf "    | JStr %s -> Ok %s.%s" (SourceLit.fsString (e.WireOf c)) e.Name c)
            |> String.concat "\n"

        let sentence = SourceLit.fsString ("not a " + e.Name)

        sprintf
            "let private dec%s (j: JVal) : Result<%s, DecodeError> =\n    match j with\n%s\n    | JStr _ -> dFail DecodeCode.UnknownTag %s %s\n    | _ -> dFail DecodeCode.WrongKind \"string\" %s"
            e.Name
            e.Name
            arms
            (SourceLit.fsString (oneOf e.WireCases))
            sentence
            sentence

    /// A union decoder. Generic unions take one `decX` codec per type parameter,
    /// with the explicit type-parameter list [[unionEncoder]] needs for the same
    /// polymorphic-recursion reason (`Binding.Format.source` recurses at `float`).
    let unionDecoder
        (docFn: string -> string -> string)
        (refines: Map<string, string>)
        (disc: string)
        (tokens: HardenPolicy)
        (msg: Set<string>)
        (idl: Idl)
        (u: IdlUnion)
        : Result<string, CodegenError> =
        let decArgs =
            u.Params
            |> List.map (fun p -> sprintf " (dec%s: JVal -> Result<'%s, DecodeError>)" p p)
            |> String.concat ""

        // Declared params stay generic; `'Msg` alone is pinned to `obj`.
        let declArgs =
            if List.isEmpty u.Params then
                ""
            else
                "<" + (u.Params |> List.map (fun p -> "'" + p) |> String.concat ", ") + ">"

        let tyArgs = objParams msg u.Name u.Params

        let ctor (c: IdlUnionCase) =
            match c.Fields with
            | [] -> sprintf "%s.%s" u.Name c.Tag
            | fs -> sprintf "%s.%s(%s)" u.Name c.Tag (fs |> List.map (fun f -> ident f.Name) |> String.concat ", ")

        let arm (c: IdlUnionCase) =
            // Phase 945 — a declared refine replaces the plain `Ok(Case(…))` final with a
            // policy expression (field binder names in scope); the binder chain around it
            // is untouched, so a refine cannot change WHICH fields decode, only what is
            // accepted once they have.
            //
            // Phase 337 — a refine answers a SENTENCE, as it always has; `dRefine` binds the
            // case's last member and lifts the refine's refusal to `OutOfRange` at the case's
            // object (see [[bindChainRefined]]).
            let refine = refines.TryFind(u.Name + "." + c.Tag)

            let final =
                match refine with
                | Some r -> r
                | None -> sprintf "Ok(%s)" (ctor c)

            let body =
                if List.isEmpty c.Fields then
                    Ok(sprintf "        | %s -> Ok %s" (SourceLit.fsString c.Tag) (ctor c))
                else
                    fieldBinders idl c.Fields
                    |> Result.map (fun binders ->
                        sprintf
                            "        | %s ->\n%s"
                            (SourceLit.fsString c.Tag)
                            (match refine with
                             | Some _ -> bindChainRefined "            " binders final
                             | None -> bindChain "            " binders final 0))

            body
            |> Result.map (fun b -> docFn ("decarm:" + u.Name + "." + c.Tag) "        " + b)

        // The declared transparent case is on the wire BARE, so it is recognised as a
        // value that is not an object. Every object goes to the tag dispatch, as the
        // interpreter's does (Phase 337: an object with no discriminator is `MissingField`
        // at it, never a payload; Phase 303 refuses a transparent case whose payload can be
        // an object, so no bare payload is lost).
        let transparent: Result<string option, CodegenError> =
            match TransparentUnion.tag tokens u with
            | Some ttag ->
                match u.Cases |> List.tryFind (fun c -> c.Tag = ttag) with
                | Some c when c.Fields.Length = 1 ->
                    let f = c.Fields.Head

                    decFn f.Type
                    |> Result.map (fun dfn ->
                        Some(
                            sprintf
                                "    | __bare ->\n        %s __bare |> Result.bind (fun %s -> Ok(%s))"
                                dfn
                                (ident f.Name)
                                (ctor c)
                        ))
                | _ -> Ok None
            | None -> Ok None

        let taggedR =
            u.Cases
            |> List.map arm
            |> concatR "\n"
            |> Result.map (fun arms ->
                sprintf
                    "    | JObj __fs ->\n        dTag __fs |> Result.bind (fun __t ->\n        match __t with\n%s\n        | __other -> dUnknown %s (%s + __other))"
                    arms
                    (SourceLit.fsString (oneOf (u.Cases |> List.map _.Tag)))
                    (SourceLit.fsString ("unknown " + u.Name + " case: ")))

        let fallthrough =
            transparent
            |> Result.map (function
                | Some t -> t
                | None ->
                    sprintf
                        "    | _ -> dFail DecodeCode.WrongKind \"object\" %s"
                        (SourceLit.fsString ("expected a " + u.Name + " object")))

        taggedR
        |> Result.bind (fun tagged ->
            fallthrough
            |> Result.map (fun fall ->
                sprintf
                    "and private dec%s%s%s (j: JVal) : Result<%s%s, DecodeError> =\n    match j with\n%s\n%s"
                    u.Name
                    declArgs
                    decArgs
                    u.Name
                    tyArgs
                    tagged
                    fall))

    let specDecoder (msg: Set<string>) (idl: Idl) (k: IdlKind) : Result<string, CodegenError> =
        let assigns =
            k.Fields |> List.map (fun f -> sprintf "%s = %s" (pascal f.Name) (ident f.Name))

        fieldBinders idl k.Fields
        |> Result.map (fun binders ->
            sprintf
                "and private dec%sSpec (j: JVal) : Result<%sSpec%s, DecodeError> =\n    dObj j |> Result.bind (fun __fs ->\n%s"
                k.Tag
                k.Tag
                (objParams msg (k.Tag + "Spec") [])
                (bindChain "    " binders ("Ok " + recordLit (k.Tag + "Spec") assigns) 1))

    let recordDecoder (msg: Set<string>) (idl: Idl) (r: IdlRecord) : Result<string, CodegenError> =
        let assigns =
            r.Fields |> List.map (fun f -> sprintf "%s = %s" (pascal f.Name) (ident f.Name))

        fieldBinders idl r.Fields
        |> Result.map (fun binders ->
            sprintf
                "and private dec%s (j: JVal) : Result<%s%s, DecodeError> =\n    dObj j |> Result.bind (fun __fs ->\n%s"
                r.Name
                r.Name
                (objParams msg r.Name [])
                (bindChain "    " binders ("Ok " + recordLit r.Name assigns) 1))

    // -----------------------------------------------------------------------
    // Phase 377 — the COLLECTING decoders and the public per-spec entries, emitted on request
    // (`Gen.Derivation.SpecDecoders`). Each `dec*` decoder above is a `Result.bind` chain and
    // reports ONE defect; its `col*` twin walks the same positions in the same order and answers
    // every defect as a `DecodeError list`. Mirroring the walk is what makes the twin a
    // conservative extension: its first defect is the short-circuiting decoder's defect, and on a
    // clean input both answer the same value — which `IdlSpecDecoderTests` holds.
    //
    // A LEAF — a scalar, an enum, a sentinel, a hosted slot, verbatim JSON — reports at most one
    // defect, so its collecting form is its short-circuiting decoder lifted (`cLift` / `cOne`). Only
    // an object, a list and a map have independent positions to collect over.
    // -----------------------------------------------------------------------

    /// Whether a type has independent positions a collecting decoder walks (an object, a list, a
    /// map, a type parameter's codec); every other type is a leaf.
    let collectsOver (t: IdlType) : bool =
        match t with
        | TList _
        | TMap _
        | TRecord _
        | TUnion _
        | TNode
        | TVar _ -> true
        | _ -> false

    /// The collecting decoder expression for a type — a `JVal -> Result<'T, DecodeError list>`.
    /// Mirrors [[decFn]] arm for arm; a leaf is [[decFn]]'s decoder, lifted.
    let rec colFn (t: IdlType) : Result<string, CodegenError> =
        match t with
        | TVar v -> Ok("col" + v)
        | TUnion(n, []) -> Ok("col" + n)
        | TUnion(n, args) ->
            args
            |> List.map colFn
            |> concatR " "
            |> Result.map (fun a -> "(col" + n + " " + a + ")")
        | TNode -> Ok "colNode"
        | TList inner -> colFn inner |> Result.map (sprintf "(cList %s)")
        | TMap vt -> colFn vt |> Result.map (sprintf "(cMap %s)")
        | TRecord n -> Ok("col" + n)
        | _ -> decFn t |> Result.map (sprintf "(cLift %s)")

    /// Reading one field back out under [[decField]]'s presence rules, collecting. A leaf field (and
    /// a host-only one, which reads nothing) is [[decField]]'s expression lifted, so its presence
    /// rules are the short-circuiting decoder's by construction.
    let colField (idl: Idl) (f: IdlField) : Result<string, CodegenError> =
        let name = SourceLit.fsString f.Name

        match f.Opt with
        | HostOnly -> decField idl f |> Result.map (sprintf "cOne (%s)")
        | _ when not (collectsOver f.Type) -> decField idl f |> Result.map (sprintf "cOne (%s)")
        | Required -> colFn f.Type |> Result.map (sprintf "cReq %s __fs %s" name)
        | Optional -> colFn f.Type |> Result.map (sprintf "cOpt %s __fs %s" name)
        | OmitDefault d ->
            fsDefaultLit idl f.Type d
            |> Result.bind (fun dexpr ->
                colFn f.Type
                |> Result.map (fun c -> sprintf "cDef %s __fs %s (%s)" name c dexpr))

    let colBinders (idl: Idl) (fs: IdlField list) : Result<(string * string) list, CodegenError> =
        fs
        |> List.map (fun f -> colField idl f |> Result.map (fun e -> ident f.Name, e))
        |> sequenceR

    /// Read every member (in declaration order — the order the defects are reported in), then
    /// build `final` when all of them decoded, else answer every member's defects, concatenated in
    /// that order. `bindNames` binds each member to its field name for `final`; a final that
    /// re-reads the object (a refined case) binds none.
    let collectAll (indent: string) (binders: (string * string) list) (bindNames: bool) (final: string) : string =
        match binders with
        | [] -> indent + final
        | _ ->
            let count = List.length binders
            let slot (i: int) = sprintf "__c%d" i

            [ yield! binders |> List.mapi (fun i (_, e) -> sprintf "%slet %s = %s" indent (slot i) e)
              yield sprintf "%smatch %s with" indent (List.init count slot |> String.concat ", ")
              yield
                  sprintf
                      "%s| %s ->"
                      indent
                      (binders
                       |> List.map (fun (v, _) -> if bindNames then "Ok " + v else "Ok _")
                       |> String.concat ", ")
              yield sprintf "%s    %s" indent final
              yield
                  sprintf
                      "%s| _ -> Error(List.concat [ %s ])"
                      indent
                      (List.init count (fun i -> "cErrs " + slot i) |> String.concat "; ") ]
            |> String.concat "\n"

    /// A union's collecting decoder: the discriminator, then the case's members collected. A case
    /// with a declared REFINE (Phase 945) collects its members and, when every one decoded, answers
    /// what the short-circuiting decoder does on the same object — the refine is a sentence over
    /// the decoded members, so it adds at most one defect and only once they all decoded.
    let colUnionDecoder
        (refines: Map<string, string>)
        (tokens: HardenPolicy)
        (msg: Set<string>)
        (idl: Idl)
        (u: IdlUnion)
        : Result<string, CodegenError> =
        let colArgs =
            u.Params
            |> List.map (fun p -> sprintf " (col%s: JVal -> Result<'%s, DecodeError list>)" p p)
            |> String.concat ""

        let declArgs =
            if List.isEmpty u.Params then
                ""
            else
                "<" + (u.Params |> List.map (fun p -> "'" + p) |> String.concat ", ") + ">"

        let tyArgs = objParams msg u.Name u.Params

        // The short-circuiting twin, handed each collecting codec's first defect.
        let decApplied =
            "dec"
            + u.Name
            + (u.Params
               |> List.map (fun p -> sprintf " (fun __j -> cFirst (col%s __j))" p)
               |> String.concat "")

        let ctor (c: IdlUnionCase) =
            match c.Fields with
            | [] -> sprintf "%s.%s" u.Name c.Tag
            | fs -> sprintf "%s.%s(%s)" u.Name c.Tag (fs |> List.map (fun f -> ident f.Name) |> String.concat ", ")

        let arm (c: IdlUnionCase) =
            if List.isEmpty c.Fields then
                Ok(sprintf "        | %s -> Ok %s" (SourceLit.fsString c.Tag) (ctor c))
            else
                colBinders idl c.Fields
                |> Result.map (fun binders ->
                    let body =
                        match refines.TryFind(u.Name + "." + c.Tag) with
                        | Some _ -> collectAll "            " binders false (sprintf "cOne (%s j)" decApplied)
                        | None -> collectAll "            " binders true (sprintf "Ok(%s)" (ctor c))

                    sprintf "        | %s ->\n%s" (SourceLit.fsString c.Tag) body)

        let transparent: Result<string option, CodegenError> =
            match TransparentUnion.tag tokens u with
            | Some ttag ->
                match u.Cases |> List.tryFind (fun c -> c.Tag = ttag) with
                | Some c when c.Fields.Length = 1 ->
                    let f = c.Fields.Head

                    colFn f.Type
                    |> Result.map (fun cfn ->
                        Some(
                            sprintf
                                "    | __bare ->\n        %s __bare |> Result.bind (fun %s -> Ok(%s))"
                                cfn
                                (ident f.Name)
                                (ctor c)
                        ))
                | _ -> Ok None
            | None -> Ok None

        let taggedR =
            u.Cases
            |> List.map arm
            |> concatR "\n"
            |> Result.map (fun arms ->
                sprintf
                    "    | JObj __fs ->\n        dTag __fs |> cOne |> Result.bind (fun __t ->\n        match __t with\n%s\n        | __other -> cOne (dUnknown %s (%s + __other)))"
                    arms
                    (SourceLit.fsString (oneOf (u.Cases |> List.map _.Tag)))
                    (SourceLit.fsString ("unknown " + u.Name + " case: ")))

        let fallthrough =
            transparent
            |> Result.map (function
                | Some t -> t
                | None ->
                    sprintf
                        "    | _ -> cOne (dFail DecodeCode.WrongKind \"object\" %s)"
                        (SourceLit.fsString ("expected a " + u.Name + " object")))

        taggedR
        |> Result.bind (fun tagged ->
            fallthrough
            |> Result.map (fun fall ->
                sprintf
                    "and private col%s%s%s (j: JVal) : Result<%s%s, DecodeError list> =\n    match j with\n%s\n%s"
                    u.Name
                    declArgs
                    colArgs
                    u.Name
                    tyArgs
                    tagged
                    fall))

    /// An object's collecting decoder — a kind's spec record or a declared record.
    let colObjectDecoder
        (decName: string)
        (typeName: string)
        (msg: Set<string>)
        (idl: Idl)
        (fields: IdlField list)
        : Result<string, CodegenError> =
        let assigns =
            fields |> List.map (fun f -> sprintf "%s = %s" (pascal f.Name) (ident f.Name))

        colBinders idl fields
        |> Result.map (fun binders ->
            sprintf
                "and private col%s (j: JVal) : Result<%s%s, DecodeError list> =\n    cObj j |> Result.bind (fun __fs ->\n%s)"
                decName
                typeName
                (objParams msg typeName [])
                (collectAll "        " binders true ("Ok " + recordLit typeName assigns)))

    /// The collecting prelude, emitted once per module that requests the collecting decoders. It
    /// states the defect order, which a test pins.
    let collectingHelpers: string =
        """// ---------------------------------------------------------------------------
// Phase 377 — COLLECTING DECODERS. Beside every short-circuiting `dec*` decoder above, a `col*`
// decoder answers EVERY defect it finds, as a `DecodeError list`, in one deterministic order:
//   - an object's members in FIELD DECLARATION ORDER (a node: `id`, its kind, then its envelope
//     fields), each member's defects at that member's position, its nested defects included
//     (depth-first);
//   - a list's items in index order, and a map's entries in document order;
//   - a leaf (a scalar, an enum, a sentinel, a hosted slot, verbatim JSON) reports at most one
//     defect, and so does a value of the wrong kind or an absent or unknown discriminator, whose
//     members are never read.
// The first defect of a collecting decoder is the defect its short-circuiting twin reports, and on
// a clean input both answer the same value. A case refine and a host projection supply only a
// short-circuiting decoder, so each adds at most one defect of its own. Codes and paths are Core's
// `DecodeError`; a consumer whose specification orders or names defects differently maps them at
// its own seam.
// ---------------------------------------------------------------------------
let private cOne (r: Result<'T, DecodeError>) : Result<'T, DecodeError list> =
    match r with
    | Ok v -> Ok v
    | Error e -> Error [ e ]

let private cLift (dec: JVal -> Result<'T, DecodeError>) (j: JVal) : Result<'T, DecodeError list> = cOne (dec j)

let private cErrs (r: Result<'T, DecodeError list>) : DecodeError list =
    match r with
    | Ok _ -> []
    | Error es -> es

// One step further from the root, for every defect a member or an item answered.
let private cUnder (step: PathSegment) (r: Result<'T, DecodeError list>) : Result<'T, DecodeError list> =
    match r with
    | Ok v -> Ok v
    | Error es -> Error(es |> List.map (DecodeError.under step))

// The first defect — how a short-circuiting decoder is handed a collecting codec.
let private cFirst (r: Result<'T, DecodeError list>) : Result<'T, DecodeError> =
    match r with
    | Ok v -> Ok v
    | Error(e :: _) -> Error e
    | Error [] -> dFail DecodeCode.SchemaFault "a refusal" "a collecting decoder answered no defect and no value"

let private cObj (j: JVal) : Result<(string * JVal) list, DecodeError list> = cOne (dObj j)

let private cList (dec: JVal -> Result<'T, DecodeError list>) (j: JVal) : Result<'T list, DecodeError list> =
    match j with
    | JArr xs ->
        let results = xs |> List.mapi (fun i x -> dec x |> cUnder (PathSegment.Index i))

        match results |> List.collect cErrs with
        | [] -> Ok(results |> List.choose (function Ok v -> Some v | Error _ -> None))
        | errors -> Error errors
    | _ -> cOne (dFail DecodeCode.WrongKind "array" "expected an array")

// Every entry is checked, in document order; a repeated key keeps its FIRST value, as `dMap` does.
let private cMap (dec: JVal -> Result<'T, DecodeError list>) (j: JVal) : Result<Map<string, 'T>, DecodeError list> =
    match j with
    | JObj fs ->
        let results = fs |> List.map (fun (k, v) -> k, (dec v |> cUnder (PathSegment.Key k)))

        match results |> List.collect (snd >> cErrs) with
        | [] ->
            (Map.empty, results)
            ||> List.fold (fun items (k, r) ->
                match r with
                | Ok d when not (Map.containsKey k items) -> Map.add k d items
                | _ -> items)
            |> Ok
        | errors -> Error errors
    | _ -> cOne (dFail DecodeCode.WrongKind "object" "expected an object")

// An absent required member is `dReq`'s refusal, sentence and path included.
let private cReq (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, DecodeError list>) : Result<'T, DecodeError list> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v |> cUnder (PathSegment.Key name)
    | None -> cOne (dReq name fs (fun _ -> dFail DecodeCode.SchemaFault "never read" "never read"))

let private cOpt (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, DecodeError list>) : Result<'T option, DecodeError list> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v |> cUnder (PathSegment.Key name) |> Result.map Some
    | None -> Ok None

let private cDef (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, DecodeError list>) (dflt: 'T) : Result<'T, DecodeError list> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v |> cUnder (PathSegment.Key name)
    | None -> Ok dflt"""

    /// Phase 377 — the collecting prelude, the collecting decoder group (mirroring the decoder
    /// group: node kind, node, unions, records, specs) and the public entries: per kind
    /// `decode<Tag>Spec` (short-circuiting) and `decode<Tag>SpecAll` (collecting) over the kind's
    /// object, and per node `decodeNodeJson` / `decodeNodeJsonAll` over a parsed value and
    /// `decodeNodeAll` over text.
    let collectingDecl
        (sup: Support)
        (idl: Idl)
        (msg: Set<string>)
        (kinds: IdlKind list)
        (unions: IdlUnion list)
        (records: IdlRecord list)
        : Result<string, CodegenError> =
        let nodeTy = objParams msg "Node" []

        let colNodeKindDecl =
            let arms =
                kinds
                |> List.map (fun k ->
                    sprintf "    | %s -> col%sSpec j |> Result.map NodeKind.%s" (SourceLit.fsString k.Tag) k.Tag k.Tag)
                |> String.concat "\n"

            sprintf
                "let rec private colNodeKind (j: JVal) : Result<NodeKind%s, DecodeError list> =\n"
                (objParams msg "NodeKind" [])
            + "    cObj j |> Result.bind (fun __fs ->\n"
            + "    dTag __fs |> cOne |> Result.bind (fun __t ->\n"
            + "    match __t with\n"
            + arms
            + sprintf
                "\n    | __other -> cOne (dUnknown %s (\"unknown node kind: \" + __other))))"
                (SourceLit.fsString (oneOf (kinds |> List.map _.Tag)))

        let colNodeDecl =
            let envelopeAssigns =
                idl.NodeFields
                |> List.map (fun f -> sprintf "; %s = %s" (pascal f.Name) (ident f.Name))
                |> String.concat ""

            let final = sprintf "Ok { Id = id; Kind = kind%s }" envelopeAssigns

            let kindBinder =
                match idl.Wire.NodeEnvelope with
                | NodeEnvelopeShape.NestedKind -> "cReq \"kind\" __fs colNodeKind"
                | NodeEnvelopeShape.FlatKind -> "colNodeKind j"

            colBinders idl idl.NodeFields
            |> Result.map (fun envelopeBinders ->
                let binders =
                    [ "id", "cOne (dReq \"id\" __fs dStr)"; "kind", kindBinder ] @ envelopeBinders

                sprintf "and private colNode (j: JVal) : Result<Node%s, DecodeError list> =\n" nodeTy
                + "    cObj j |> Result.bind (fun __fs ->\n"
                + collectAll "        " binders true final
                + ")")

        let specDecl (k: IdlKind) =
            // A projected kind's decoder is the projection's short-circuiting one, lifted.
            if sup.KindProjections.ContainsKey k.Tag then
                Ok(sprintf "and private col%sSpec (j: JVal) =\n    cOne (dec%sSpec j)" k.Tag k.Tag)
            else
                colObjectDecoder (k.Tag + "Spec") (k.Tag + "Spec") msg idl k.Fields

        let groupR =
            (Ok colNodeKindDecl
             :: colNodeDecl
             :: (unions |> List.map (colUnionDecoder sup.CaseRefines idl.Harden msg idl))
             @ (records |> List.map (fun r -> colObjectDecoder r.Name r.Name msg idl r.Fields))
             @ (kinds |> List.map specDecl))
            |> concatR "\n\n"

        let entries =
            [ yield
                  "/// Phase 377 — the public per-spec decoders. `decode<Tag>Spec` reads one kind's object and\n/// answers its FIRST defect; `decode<Tag>SpecAll` reads the same object and answers EVERY defect,\n/// in the order stated above the collecting prelude. A consumer delegates one kind at a time."
              for k in kinds do
                  if sup.KindProjections.ContainsKey k.Tag then
                      yield sprintf "let decode%sSpec (j: JVal) = dec%sSpec j" k.Tag k.Tag
                      yield sprintf "let decode%sSpecAll (j: JVal) = col%sSpec j" k.Tag k.Tag
                  else
                      let ty = k.Tag + "Spec" + objParams msg (k.Tag + "Spec") []

                      yield sprintf "let decode%sSpec (j: JVal) : Result<%s, DecodeError> = dec%sSpec j" k.Tag ty k.Tag

                      yield
                          sprintf
                              "let decode%sSpecAll (j: JVal) : Result<%s, DecodeError list> = col%sSpec j"
                              k.Tag
                              ty
                              k.Tag
              yield
                  sprintf
                      "/// The whole node over a parsed value: the first defect, or every defect.\nlet decodeNodeJson (j: JVal) : Result<Node%s, DecodeError> = decNode j"
                      nodeTy
              yield sprintf "let decodeNodeJsonAll (j: JVal) : Result<Node%s, DecodeError list> = colNode j" nodeTy
              yield
                  sprintf
                      "/// `decodeNode`'s collecting twin: a parser refusal is the one defect, else every defect.\nlet decodeNodeAll (s: string) : Result<Node%s, DecodeError list> =\n    Decoder.parse s |> cOne |> Result.bind colNode"
                      nodeTy ]
            |> String.concat "\n"

        groupR
        |> Result.map (fun group -> [ collectingHelpers; group; entries ] |> String.concat "\n\n")

    /// The decode-side helper prelude, emitted once per module. `dTag` reads the
    /// DECLARED discriminator (Phase 108) — `"$type"` interpolates to exactly the
    /// pre-declarable bytes.
    ///
    /// Phase 337 — every helper answers Core's `DecodeError`, with the CODE, PATH and
    /// EXPECTED the interpreter's walk (`Idl.Decode`) reports for the same position and the
    /// SENTENCE this prelude has always returned. `dHosted` is emitted when the vocabulary
    /// declares a hosted slot and `dRefine` when the support declares a case refine — the two
    /// places a verbatim expression answers a sentence rather than a typed refusal.
    let decodeHelpers (disc: string) (hosted: bool) (refines: bool) : string =
        let discLit = SourceLit.fsString disc
        let discSentence = SourceLit.fsString ("missing or non-string " + disc)
        // A triple-quoted template cannot END in a quote character; the helpers whose last
        // token is a string literal close it with this.
        let q = "\""

        [ yield
              """// Phase 337 — a refusal is Core's `DecodeError`: a code from the closed `DecodeCode` set, the
// path from the value the outermost decoder was handed, what the position expected, and a
// sentence. The code and the path are the ones the IDL interpreter reports for the same
// document; the sentence is this layer's own.
let private dFail (code: DecodeCode) (expected: string) (message: string) : Result<'T, DecodeError> =
    Error(DecodeError.make code expected message)"""
          yield
              """// One step further from the root — what a refusal gains as it leaves a member or an item.
let private dUnder (step: PathSegment) (r: Result<'T, DecodeError>) : Result<'T, DecodeError> =
    match r with
    | Ok v -> Ok v
    | Error e -> Error(DecodeError.under step e)"""
          yield
              """let private dObj (j: JVal) : Result<(string * JVal) list, DecodeError> =
    match j with
    | JObj fs -> Ok fs
    | _ -> dFail DecodeCode.WrongKind "object" "expected an object"""
              + q
          yield
              """// The discriminator: absent is `MissingField` naming it, a non-string `WrongKind` at it.
let private dTag (fs: (string * JVal) list) : Result<string, DecodeError> =
    match fs |> List.tryFind (fun (k, _) -> k = __DISC__) with
    | Some(_, JStr t) -> Ok t
    | Some _ -> dFail DecodeCode.WrongKind "string" __SENTENCE__ |> dUnder (PathSegment.Key __DISC__)
    | None ->
        Error
            { Decoder.missing __DISC__ with
                Message = __SENTENCE__ }"""
                  .Replace("__DISC__", discLit)
                  .Replace("__SENTENCE__", discSentence)
          yield
              """// A tag naming no case this decoder knows: `UnknownTag` at the discriminator.
let private dUnknown (expected: string) (message: string) : Result<'T, DecodeError> =
    dFail DecodeCode.UnknownTag expected message |> dUnder (PathSegment.Key __DISC__)"""
                  .Replace("__DISC__", discLit)
          yield
              """let private dStr (j: JVal) : Result<string, DecodeError> =
    match j with
    | JStr s -> Ok s
    | _ -> dFail DecodeCode.WrongKind "string" "expected a string"""
              + q
          yield
              """let private dInt (j: JVal) : Result<int, DecodeError> =
    match j with
    | JInt i -> Ok i
    | _ -> dFail DecodeCode.WrongKind "int" "expected an int"""
              + q
          yield
              """let private dBool (j: JVal) : Result<bool, DecodeError> =
    match j with
    | JBool b -> Ok b
    | _ -> dFail DecodeCode.WrongKind "bool" "expected a bool"""
              + q
          yield
              """// A whole-valued float renders without a decimal point, so it parses back as JInt.
// WIRE_FORMAT §7 — a float slot also accepts the three quoted non-finite sentinels, which
// is how §5 spells a number JSON has no literal for. The value decodes to the FLOAT, never
// to the string: a host that answered the string would hand a consumer a different tree on
// the second decode while the bytes stayed identical. `dInt` is NOT widened — §7 stops at
// the float slot.
let private dFloat (j: JVal) : Result<float, DecodeError> =
    match j with
    | JFloat f -> Ok f
    | JInt i -> Ok(float i)
    | JStr "NaN" -> Ok System.Double.NaN
    | JStr "Infinity" -> Ok System.Double.PositiveInfinity
    | JStr "-Infinity" -> Ok System.Double.NegativeInfinity
    | _ -> dFail DecodeCode.WrongKind "number" "expected a number"""
              + q
          yield
              """// Phase 347 — a closure / opaque slot holds one fixed sentinel string, read BY VALUE as the
// interpreter reads it: another string is `OutOfRange` (a string, but not the one value the slot
// takes), any other kind `WrongKind`. The sentinel carries nothing, so it decodes to `()`.
let private dSentinel (sentinel: string) (j: JVal) : Result<unit, DecodeError> =
    match j with
    | JStr s when s = sentinel -> Ok()
    | JStr _ -> dFail DecodeCode.OutOfRange "string" ("expected the sentinel " + sentinel)
    | _ -> dFail DecodeCode.WrongKind "string" "expected a string"""
              + q
          yield
              """// Phase 676 — arbitrary JSON, kept verbatim. No shape check: the field's
// contract is that its content is not the schema's business.
let private dJson (j: JVal) : Result<JVal, DecodeError> = Ok j"""
          yield
              """let private dList (dec: JVal -> Result<'T, DecodeError>) (j: JVal) : Result<'T list, DecodeError> =
    match j with
    | JArr xs ->
        let rec go (i: int) (acc: 'T list) (rest: JVal list) =
            match rest with
            | [] -> Ok(List.rev acc)
            | x :: tail ->
                match dec x with
                | Ok v -> go (i + 1) (v :: acc) tail
                | Error e -> Error(DecodeError.under (PathSegment.Index i) e)

        go 0 [] xs
    | _ -> dFail DecodeCode.WrongKind "array" "expected an array"""
              + q
          yield
              """// Every entry is checked, in document order; a repeated key keeps its FIRST value, as every
// member read does (Phase 347 — `Map.ofList` kept the last, as `JSON.parse` does).
let private dMap (dec: JVal -> Result<'T, DecodeError>) (j: JVal) : Result<Map<string, 'T>, DecodeError> =
    match j with
    | JObj fs ->
        (Ok Map.empty, fs)
        ||> List.fold (fun acc (k, v) ->
            match acc with
            | Error e -> Error e
            | Ok items ->
                dec v
                |> dUnder (PathSegment.Key k)
                |> Result.map (fun d -> if Map.containsKey k items then items else Map.add k d items))
    | _ -> dFail DecodeCode.WrongKind "object" "expected an object"""
              + q
          yield
              """let private dReq (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, DecodeError>) : Result<'T, DecodeError> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v |> dUnder (PathSegment.Key name)
    | None ->
        Error
            { Decoder.missing name with
                Message = "missing required field '" + name + "'" }"""
          yield
              """let private dOpt (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, DecodeError>) : Result<'T option, DecodeError> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v |> dUnder (PathSegment.Key name) |> Result.map Some
    | None -> Ok None"""
          yield
              """let private dDef (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, DecodeError>) (dflt: 'T) : Result<'T, DecodeError> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v |> dUnder (PathSegment.Key name)
    | None -> Ok dflt"""
          if hosted then
              yield
                  """// A hosted slot's codec answers a SENTENCE (`JVal -> Result<'host, string>`): its refusal is
// `OutOfRange` at the slot — any declared wire form has already been checked, so the value is
// of the kind the slot takes and the codec does not admit it.
let private dHosted (r: Result<'T, string>) : Result<'T, DecodeError> =
    match r with
    | Ok v -> Ok v
    | Error m -> dFail DecodeCode.OutOfRange "a value the slot's host codec admits" m"""
          if refines then
              yield
                  """// A declared case refine answers a SENTENCE over the members it reads: `dRefine` binds the
// case's last member and lifts the refine's refusal to `OutOfRange` at the case's object —
// every member decoded, and together they are a value the case does not admit.
let private dRefine (f: 'T -> Result<'U, string>) (r: Result<'T, DecodeError>) : Result<'U, DecodeError> =
    match r with
    | Error e -> Error e
    | Ok v ->
        match f v with
        | Ok u -> Ok u
        | Error m -> dFail DecodeCode.OutOfRange "a case its refinement admits" m""" ]
        |> String.concat "\n\n"

    /// Phase 252 — the generated F# check of a hosted slot's declared FORMAT, emitted only
    /// when the vocabulary declares one ([[declaresHostedFormat]]). It restates
    /// [[HostedFormat.admits]] because the generated module carries no reference to this
    /// package; a test holds the two to the same answers.
    let fsFormatHelper =
        """// Phase 252 — a hosted slot's declared string format, checked before its host codec runs,
// exactly as the interpreter and the TypeScript host check it.
let private dFormat (format: string) (j: JVal) : Result<unit, DecodeError> =
    let day (y: int) (m: int) (d: int) =
        y >= 1 && m >= 1 && m <= 12 && d >= 1 && d <= System.DateTime.DaysInMonth(y, m)

    let num (m: System.Text.RegularExpressions.Match) (i: int) = int m.Groups[i].Value

    let ok =
        match format, j with
        | "date", JStr s ->
            let m = System.Text.RegularExpressions.Regex.Match(s, "^([0-9]{4})-([0-9]{2})-([0-9]{2})$")
            m.Success && day (num m 1) (num m 2) (num m 3)
        | "date-time", JStr s ->
            let m =
                System.Text.RegularExpressions.Regex.Match(
                    s,
                    "^([0-9]{4})-([0-9]{2})-([0-9]{2})[Tt]([0-9]{2}):([0-9]{2}):([0-9]{2})([.][0-9]+)?([Zz]|[+-]([0-9]{2}):([0-9]{2}))$"
                )

            m.Success
            && day (num m 1) (num m 2) (num m 3)
            && num m 4 <= 23
            && num m 5 <= 59
            && num m 6 <= 59
            && (not m.Groups[9].Success || (num m 9 <= 23 && num m 10 <= 59))
        | "uuid", JStr s ->
            System.Text.RegularExpressions.Regex.IsMatch(
                s,
                "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"
            )
        | _ -> false

    if ok then
        Ok()
    else
        dFail DecodeCode.OutOfRange ("a '" + format + "' string") ("expected a '" + format + "' string")"""


    // -----------------------------------------------------------------------
    // Phase 317 increment 5 — the Core witness-record leg. Emit a
    // `NodeWitness<Node, string>` for the generated `Node`, so the generated
    // structural layer plugs straight into `Fuaran.Core.Tree` / `.Validator` /
    // `.Observer` — the "serves every domain via the Core witness, not just UI"
    // promise. `Children` / `ReplaceChildren` are derived from the IDL: a field
    // is node-bearing iff its type is `TNode` or `TList TNode`.
    // -----------------------------------------------------------------------

    /// `Some (pascalName, isList)` when a field holds a `Node` (single) or a
    /// `Node list`; `None` otherwise.
    let nodeBearing (f: IdlField) : (string * bool) option =
        match f.Type with
        | TNode -> Some(pascal f.Name, false)
        | TList TNode -> Some(pascal f.Name, true)
        | _ -> None

    /// Emit the `NodeWitness<Node, string>` + its three helper projections. Top-level
    /// `match` functions (not record-literal lambdas) to dodge offside pitfalls in
    /// generated code. `Error` on a kind mixing a `Node list` field with other node-bearing
    /// fields (`ReplaceChildren` not generable — GP4/GP5) rather than emitting a runtime
    /// throwing guard; kinds whose node-bearing fields are all single `Node` are generated
    /// with positional re-assignment.
    ///
    /// Phase 374 — `publicAccess` is the derived structural-access emission: the same arms as
    /// PUBLIC functions (`wireTag`, `allWireTags`, `children`, `withChildren`, kids first so
    /// `withChildren (children n) n = n` reads as written) with `nodeWitness` built on them, and
    /// an OPTIONAL node field is a keyed position rather than a child (it is not an ordered list
    /// a structural edit may rebuild). `false` is the emission every module had before, byte for
    /// byte.
    ///
    /// Phase 403 — a projected kind's children are read from its declared record fields
    /// (`Projection.RecordFields`); one that declares none and whose wire holds a node directly is
    /// refused by name, because nothing says which member of the host record holds it.
    let witnessDecl
        (publicAccess: bool)
        (projections: Map<string, Projection>)
        (msg: Set<string>)
        (kinds: IdlKind list)
        : Result<string, CodegenError> =
        let nodeArgs = declParams msg "Node" []

        let nodeBearing =
            if publicAccess then
                FSharpDerive.structuralField
            else
                nodeBearing

        let construct, request =
            if publicAccess then
                "the structural children", "do not request StructuralAccess"
            else
                "the node witness's children", "do not select the kind"

        let kindFields =
            kinds
            |> List.map (fun k ->
                FSharpDerive.nodeFieldsOf
                    projections
                    (fun f ->
                        match f.Type with
                        | TNode
                        | TList TNode -> true
                        | _ -> false)
                    construct
                    request
                    k
                |> Result.map (fun fs -> k.Tag, fs))
            |> sequenceR
            |> Result.map Map.ofList

        let fieldsOf (k: IdlKind) =
            kindFields |> Result.map (fun m -> m[k.Tag]) |> Result.defaultValue []

        let childBearing =
            kinds
            |> List.filter (fun k -> fieldsOf k |> List.exists (nodeBearing >> Option.isSome))

        let allBearing = List.length childBearing = List.length kinds

        let kindTagArms =
            kinds
            |> List.map (fun k -> sprintf "    | NodeKind.%s _ -> %s" k.Tag (SourceLit.fsString k.Tag))
            |> String.concat "\n"

        let childArm (k: IdlKind) =
            let exprs =
                fieldsOf k
                |> List.choose nodeBearing
                |> List.map (fun (name, isList) -> if isList then "s." + name else "[ s." + name + " ]")
                |> String.concat " @ "

            sprintf "    | NodeKind.%s s -> %s" k.Tag exprs

        let replaceArm (k: IdlKind) : Result<string, CodegenError> =
            match fieldsOf k |> List.choose nodeBearing with
            | [ (name, true) ] ->
                Ok(sprintf "    | NodeKind.%s s -> { n with Kind = NodeKind.%s { s with %s = kids } }" k.Tag k.Tag name)
            | [ (name, false) ] ->
                Ok(
                    sprintf
                        "    | NodeKind.%s s -> { n with Kind = NodeKind.%s { s with %s = List.head kids } }"
                        k.Tag
                        k.Tag
                        name
                )
            | fields when fields |> List.forall (fun (_, isList) -> not isList) ->
                // Several single-`Node` fields (real tier: `ErrorBoundary` has `child` + `fallback`).
                // `witnessChildren` returns them in field order, so re-assign `kids` positionally.
                let assigns =
                    fields
                    |> List.mapi (fun i (name, _) -> sprintf "%s = List.item %d kids" name i)
                    |> String.concat "; "

                Ok(sprintf "    | NodeKind.%s s -> { n with Kind = NodeKind.%s { s with %s } }" k.Tag k.Tag assigns)
            | _ ->
                // A kind mixing a `Node list` field with other node-bearing fields has no
                // unambiguous positional split; none exists in the vocabulary, so the generator
                // refuses at generation time (GP4) rather than emitting a runtime throwing
                // guard into the generated code.
                Error(CodegenError.MultiChildFieldKind k.Tag)

        let childArms =
            (childBearing |> List.map childArm)
            @ (if allBearing then [] else [ "    | _ -> []" ])
            |> String.concat "\n"

        let replaceArms =
            kindFields
            |> Result.bind (fun _ -> childBearing |> List.map replaceArm |> sequenceR)
            |> Result.map (fun arms -> (arms @ (if allBearing then [] else [ "    | _ -> n" ])) |> String.concat "\n")

        let publicDecl (replaceArmsStr: string) =
            String.concat
                "\n"
                [ "/// The kind's wire tag — the discriminator it is encoded under."
                  sprintf "let wireTag (n: Node%s) : string =" nodeArgs
                  "    match n.Kind with"
                  kindTagArms
                  ""
                  "/// Every wire tag this module's kinds are encoded under, in declaration order."
                  sprintf
                      "let allWireTags: string list = [ %s ]"
                      (kinds |> List.map (fun k -> SourceLit.fsString k.Tag) |> String.concat "; ")
                  ""
                  "/// The node's ordered structural children, in field order."
                  sprintf "let children (n: Node%s) : Node%s list =" nodeArgs nodeArgs
                  "    match n.Kind with"
                  childArms
                  ""
                  "/// The node with exactly this structural child list, its id and kind kept."
                  sprintf "let withChildren (kids: Node%s list) (n: Node%s) : Node%s =" nodeArgs nodeArgs nodeArgs
                  "    match n.Kind with"
                  replaceArmsStr
                  ""
                  sprintf "let nodeWitness: NodeWitness<Node%s, string> =" nodeArgs
                  "    { Id = fun n -> n.Id"
                  "      KindTag = wireTag"
                  "      Children = children"
                  "      ReplaceChildren = fun n kids -> withChildren kids n }" ]

        replaceArms
        |> Result.map (fun replaceArmsStr ->
            if publicAccess then
                publicDecl replaceArmsStr
            else
                String.concat
                    "\n"
                    [ sprintf "let private witnessKindTag (n: Node%s) : string =" nodeArgs
                      "    match n.Kind with"
                      kindTagArms
                      ""
                      sprintf "let private witnessChildren (n: Node%s) : Node%s list =" nodeArgs nodeArgs
                      "    match n.Kind with"
                      childArms
                      ""
                      sprintf
                          "let private witnessReplaceChildren (n: Node%s) (kids: Node%s list) : Node%s ="
                          nodeArgs
                          nodeArgs
                          nodeArgs
                      "    match n.Kind with"
                      replaceArmsStr
                      ""
                      sprintf "let nodeWitness: NodeWitness<Node%s, string> =" nodeArgs
                      "    { Id = fun n -> n.Id"
                      "      KindTag = witnessKindTag"
                      "      Children = witnessChildren"
                      "      ReplaceChildren = witnessReplaceChildren }" ])

    // -----------------------------------------------------------------------
    // Phase 317 increment 6 — the validator-rule-scaffold leg. Emit a
    // `Fuaran.Core.Validator`-ready entry point wired through the generated
    // `nodeWitness`. Rule *content* stays domain-side (that is the whole point
    // of `Core.Validator` — `RuleFamily` packs are domain-supplied); what the
    // generator owns is the scaffold: a `runValidator` that runs any registry
    // over the generated `Node` via the witness. A domain registers its own
    // families and gets build-time verification over generated nodes for free.
    // -----------------------------------------------------------------------

    /// Emit the validator scaffold — independent of the kind set (it wires the
    /// generic `Validator.runAll` to the generated `Node` + `nodeWitness`).
    let validatorDecl (msg: Set<string>) : string =
        let nodeArgs = declParams msg "Node" []

        String.concat
            "\n"
            [ "// Validator scaffold — register domain RuleFamilies into `reg`; rule content stays domain-side."
              sprintf
                  "let runValidator (reg: Validator.RuleRegistry<Node%s, string>) (root: Node%s) : Defect<string> list ="
                  nodeArgs
                  nodeArgs
              "    Validator.runAll nodeWitness reg root" ]


    /// The F# module emission before [[normalizeEol]] — see `fsharpModuleWith`, the public
    /// entry point, which is this composed with it.
    let fsharpModuleUnnormalised
        (sup: Support)
        (requests: FSharpDerive.Request list)
        (decoders: bool)
        (moduleName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        // Phase 945 — declared-doc lookup: a block of comment lines (markers included,
        // "///" or "//" alike) attached to the named declaration path, indented to the
        // emission site. Absent path ⇒ empty string, so an IDL with no docs emits
        // byte-identically to the pre-945 generator.
        //
        // Phase 293 — every path the emission CONSULTS is recorded, and a declared doc whose
        // path was never consulted is a refusal at the end rather than a silent drop: a typo
        // in `type:Heading` used to emit a module with no comment and no complaint.
        let consulted = System.Collections.Generic.HashSet<string>()

        let doc (path: string) (indent: string) : string =
            consulted.Add path |> ignore

            match sup.Docs.TryFind path with
            | Some lines -> (lines |> List.map (fun l -> indent + l) |> String.concat "\n") + "\n"
            | None -> ""

        // The same block as a type-group member's comment slot (no trailing newline —
        // the renderer adds it).
        let docOpt (path: string) : string option =
            consulted.Add path |> ignore
            sup.Docs.TryFind path |> Option.map (fun lines -> lines |> String.concat "\n")

        let kinds = kindTags |> List.choose (fun t -> CodegenLookup.tryKind idl t)

        let enums, unions, records = referenced idl kinds

        // Phase 108 — the emitted discriminated-object builder: `Canon.typed` on a
        // default-shape IDL (byte-identical to every pre-declarable emission), the
        // module-local `typedTag` when the vocabulary declares another key.
        let typedName =
            if idl.Wire.Discriminator = "$type" then
                "Canon.typed"
            else
                "typedTag"

        // Phase 689 — which declarations are generic in `'Msg`. Empty unless the IDL
        // uses `TFn`, so an IDL that has not adopted it generates exactly as before.
        let msg = msgCarrying idl

        /// `"<'Msg>"` where the tree is msg-carrying, `""` otherwise — the suffix every
        /// emitted `Node` / `NodeKind` annotation needs.
        let nodeArgs = declParams msg "Node" []
        let kindArgs = declParams msg "NodeKind" []

        // Value-unions, non-discriminated records, per-kind specs, `NodeKind` and `Node` form ONE
        // type-recursion cycle in the real tier — a union can hold a record (`CellKindErased`
        // holds `ButtonGroupItem`) or a `Node` (`FragmentArg.SlotArg`), a record holds unions, a
        // spec holds `Node list`. So all of them are emitted as a single `type … and …` group
        // (enums stay standalone before it — they reference nothing). Unions + `NodeKind` are
        // `[<RequireQualifiedAccess>]` (case-name collisions across `Number` / `Text` / `Static` /
        // `Date` / … demand it); records are plain (their `pascal`-cased fields never collide with
        // a keyword, and construction sites disambiguate by annotation).
        let typeGroup =
            FSharpTypes.typeGroup doc docOpt sup.KindProjections sup.TypeSplice idl kinds unions records

        let encNodeDecl =
            let arms =
                kinds
                |> List.map (fun k -> sprintf "    | NodeKind.%s s -> enc%sSpec s" k.Tag k.Tag)
                |> String.concat "\n"

            // Phase 690 — the envelope rides the same `List.choose id` presence
            // machinery every spec field uses, with `s.` rebound to `n.`, so
            // omit-on-absence / omit-at-default behave identically on a node field
            // and on a kind field. No envelope ⇒ the original two-key literal.
            //
            // Phase 109 — the FLAT shape merges the id (and any envelope) into the
            // kind's own object; `Canon.render` sorts keys, so splice order is
            // irrelevant. Nested emission is byte-identical to the pre-declarable form.
            let envelopePieces () =
                idl.NodeFields |> List.map (specPieceOf idl "n") |> concatR "; "

            let body =
                match idl.Wire.NodeEnvelope, idl.NodeFields with
                | NodeEnvelopeShape.NestedKind, [] -> Ok "\n    JObj [ \"id\", JStr n.Id; \"kind\", kind ]"
                | NodeEnvelopeShape.NestedKind, _ ->
                    envelopePieces ()
                    |> Result.map (
                        sprintf "\n    JObj([ Some(\"id\", JStr n.Id); Some(\"kind\", kind); %s ] |> List.choose id)"
                    )
                // Discriminator first, then id, kind fields, envelope — the
                // Phase 111 declared order (irrelevant under Sorted rendering,
                // where `Canon.render` re-sorts; normative under Declared).
                | NodeEnvelopeShape.FlatKind, [] ->
                    Ok
                        "\n    match kind with\n    | JObj(__d :: __kf) -> JObj(__d :: (\"id\", JStr n.Id) :: __kf)\n    | __other -> __other"
                | NodeEnvelopeShape.FlatKind, _ ->
                    envelopePieces ()
                    |> Result.map (
                        sprintf
                            "\n    match kind with\n    | JObj(__d :: __kf) -> JObj(__d :: (\"id\", JStr n.Id) :: (__kf @ ([ %s ] |> List.choose id)))\n    | __other -> __other"
                    )

            // Phase 694 — the kind dispatch is its own function (was inline in
            // encNode) so the JVal accessors below can expose it: a host codec
            // splicing a bare NodeKind (a TreeOp `EditNode.newKind`) reaches the
            // same single encoder the node envelope uses.
            body
            |> Result.map (fun b ->
                sprintf "let rec private encNodeKind (k: NodeKind%s) : JVal =\n    match k with\n" nodeArgs
                + arms
                + sprintf "\n\nand private encNode (n: Node%s) : JVal =\n    let kind = encNodeKind n.Kind\n" nodeArgs
                + b)

        // encNode + every union / record / spec encoder form one mutually-recursive group.
        let recGroup =
            (encNodeDecl
             :: (unions |> List.map (unionEncoder typedName doc idl.Harden msg idl))
             @ (records |> List.map (recordEncoder msg idl))
             @ (kinds
                |> List.map (fun k ->
                    // Phase 945 — a projected kind's encoder is the projection's, verbatim.
                    match sup.KindProjections.TryFind k.Tag with
                    | Some proj -> Ok(doc ("enc:" + k.Tag) "" + proj.Encoder)
                    | None ->
                        specEncoder typedName msg idl k
                        |> Result.map (fun e -> doc ("enc:" + k.Tag) "" + e)))
             @ (sup.EncodeSplice |> Option.toList |> List.map Ok))
            |> concatR "\n\n"

        let header =
            // Phase 113 — the generated layer constructs and matches EVERY declared
            // member, marked ones included, so a vocabulary that deprecates anything
            // would otherwise make its own codec noisy with FS0044. The suppression is
            // file-local and conditional: a vocabulary that marks nothing emits exactly
            // the header it always did, and a CONSUMER of this module still gets the
            // warning, which is the whole point of emitting the attribute.
            let nowarn =
                if emitsObsolete idl then
                    "\n#nowarn \"44\" // this layer implements every declared member, including deprecated ones"
                else
                    ""

            sprintf
                "// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen %s. Do not edit by hand.\nmodule %s%s\n\nopen Fuaran.Core"
                generatorVersion
                moduleName
                nowarn

        // Phase 672: the decoder's mutually-recursive group, mirroring `recGroup`.
        // `decNodeKind` dispatches `$type` to the per-kind spec decoder; `decNode`
        // reads the `{ id, kind }` envelope `encNode` writes.
        let decGroup =
            let decNodeKindDecl =
                let arms =
                    kinds
                    |> List.map (fun k ->
                        sprintf
                            "    | %s -> dec%sSpec j |> Result.map NodeKind.%s"
                            (SourceLit.fsString k.Tag)
                            k.Tag
                            k.Tag)
                    |> String.concat "\n"

                // Phase 337 — an unknown kind is `UnknownTag` at the discriminator, listing
                // the kinds this module decodes.
                sprintf
                    "let rec private decNodeKind (j: JVal) : Result<NodeKind%s, DecodeError> =\n"
                    (objParams msg "NodeKind" [])
                + "    dObj j |> Result.bind (fun __fs ->\n"
                + "    dTag __fs |> Result.bind (fun __t ->\n"
                + "    match __t with\n"
                + arms
                + sprintf
                    "\n    | __other -> dUnknown %s (\"unknown node kind: \" + __other)))"
                    (SourceLit.fsString (oneOf (kinds |> List.map _.Tag)))

            let decNodeDecl =
                // Phase 690 — the envelope binds through the same `bindChain` /
                // `decField` machinery a spec record uses, so its presence rules are
                // the encoder's inverse by construction rather than by hand.
                //
                // Phase 109 — in the FLAT shape the node object IS the kind body, so
                // `decNodeKind` dispatches on the same object the id binds from
                // (spec decoders read only declared names, so the discriminator and
                // the id are tolerated as the extra keys they are). The nested
                // binder is byte-identical to the pre-declarable emission.
                let envelopeAssigns =
                    idl.NodeFields
                    |> List.map (fun f -> sprintf "; %s = %s" (pascal f.Name) (ident f.Name))
                    |> String.concat ""

                let final = sprintf "Ok { Id = id; Kind = kind%s }" envelopeAssigns

                let kindBinder =
                    match idl.Wire.NodeEnvelope with
                    | NodeEnvelopeShape.NestedKind -> "dReq \"kind\" __fs decNodeKind"
                    | NodeEnvelopeShape.FlatKind -> "decNodeKind j"

                fieldBinders idl idl.NodeFields
                |> Result.map (fun envelopeBinders ->
                    let binders =
                        [ "id", "dReq \"id\" __fs dStr"; "kind", kindBinder ] @ envelopeBinders

                    sprintf "and private decNode (j: JVal) : Result<Node%s, DecodeError> =\n" (objParams msg "Node" [])
                    + "    dObj j |> Result.bind (fun __fs ->\n"
                    + bindChain "    " binders final 1)

            (Ok decNodeKindDecl
             :: decNodeDecl
             :: (unions
                 |> List.map (unionDecoder doc sup.CaseRefines idl.Wire.Discriminator idl.Harden msg idl))
             @ (records |> List.map (recordDecoder msg idl))
             @ (kinds
                |> List.map (fun k ->
                    // Phase 945 — a projected kind's decoder is the projection's, verbatim.
                    match sup.KindProjections.TryFind k.Tag with
                    | Some proj -> Ok(doc ("dec:" + k.Tag) "" + proj.Decoder)
                    | None -> specDecoder msg idl k |> Result.map (fun d -> doc ("dec:" + k.Tag) "" + d)))
             @ (sup.DecodeSplice |> Option.toList |> List.map Ok))
            |> concatR "\n\n"

        // Phase 374 — the requested structural derivations. Keyed positions are defined against
        // the PUBLIC structural children, so requesting them requests the structural access too.
        // An empty request list leaves the witness private and appends nothing.
        let publicAccess =
            requests
            |> List.exists (fun r ->
                r = FSharpDerive.Request.StructuralAccess
                || r = FSharpDerive.Request.KeyedPositions)

        let derivedCtx: FSharpDerive.Ctx =
            { Idl = idl
              Msg = msg
              Kinds = kinds
              Unions = unions
              Records = records
              Projections = sup.KindProjections }

        let witnessAndDerived =
            FSharpDerive.recordFieldsDeclared derivedCtx
            |> Result.bind (fun () -> witnessDecl publicAccess sup.KindProjections msg kinds)
            |> Result.bind (fun w -> FSharpDerive.derivedDecl derivedCtx requests |> Result.map (fun ds -> w, ds))
            // Phase 377 — the collecting decoders and the public per-spec entries, on request, after
            // every other member (they compose with the decoder group and nothing reads them).
            |> Result.bind (fun (w, ds) ->
                if decoders then
                    collectingDecl sup idl msg kinds unions records
                    |> Result.map (fun c -> w, ds @ [ c ])
                else
                    Ok(w, ds))

        // Phase 124 — the encoder and decoder groups joined the two declarations that could
        // already refuse. Every leg that renders a declared default now reports the same typed
        // `UnsupportedDefault` here, so there is no remaining path on which an unrenderable
        // default produces a module instead of a refusal.
        match witnessAndDerived, defaultsDecl sup.KindProjections msg idl kinds, recGroup, decGroup, typeGroup with
        | Ok(witness, derived), Ok defaults, Ok recGroup, Ok decGroup, Ok typeGroup ->
            [ [ header ]
              enums |> List.map (rqaEnum doc)
              [ typeGroup ]
              enums |> List.map (fun e -> doc ("enc:" + e.Name) "" + enumEncoder e)
              [ encodeHelpers idl ]
              [ recGroup ]
              [ sprintf
                    "let encodeNode (n: Node%s) : string = %s (encNode n)"
                    nodeArgs
                    (match idl.Wire.KeyOrder with
                     | KeyOrder.Sorted -> "Canon.render"
                     | KeyOrder.Declared -> "Canon.renderOrdered") ]
              // Phase 694 — JVal-level accessors for host codecs that splice
              // generated encodings into a larger canonical document (the
              // tier's TreeOp codec re-points at these when the hand-written
              // node encoder is deleted). Node + kind always; the two envelope
              // records only when the vocabulary declares them (the spike
              // vocabulary has neither).
              [ sprintf
                    "/// JVal-level accessors (Phase 694) — for host codecs that splice generated\n/// encodings into a larger canonical document (e.g. a TreeOp codec).\nlet encodeNodeJson (n: Node%s) : JVal = encNode n"
                    nodeArgs
                sprintf "let encodeNodeKindJson (k: NodeKind%s) : JVal = encNodeKind k" nodeArgs ]
              (if records |> List.exists (fun r -> r.Name = "StateBehaviour") then
                   [ sprintf "let encodeStateBehaviourJson (s: StateBehaviour%s) : JVal = encStateBehaviour s" nodeArgs ]
               else
                   [])
              (if records |> List.exists (fun r -> r.Name = "SemanticStyle") then
                   [ "let encodeSemanticStyleJson (s: SemanticStyle) : JVal = encSemanticStyle s" ]
               else
                   [])
              (sup.AccessorSplice |> Option.toList)
              decodeHelpers idl.Wire.Discriminator (declaresHosted idl) (not sup.CaseRefines.IsEmpty)
              :: (if declaresHostedFormat idl then [ fsFormatHelper ] else [])
              enums |> List.map (fun e -> doc ("dec:" + e.Name) "" + enumDecoder e)
              [ decGroup ]
              // Phase 337 — the parser's refusal through `Decoder.parse`: `InvalidJson` at the root,
              // or `LimitExceeded` past the nesting cap, with `Json.parse`'s sentence.
              [ sprintf
                    "/// Structural decode. The policy layer (diagnostics, §16 lenient-accept,\n/// the reject set) composes ABOVE this — see the Phase 672 note in the generator.\n/// A refusal is Core's `DecodeError` (Phase 337): the code and path the IDL interpreter\n/// reports for the same document, and this layer's sentence (`DecodeError.describe`).\nlet decodeNode (s: string) : Result<Node%s, DecodeError> =\n    Decoder.parse s |> Result.bind decNode"
                    (objParams msg "Node" []) ]
              [ witness ]
              [ validatorDecl msg ]
              [ defaults ]
              derived ]
            |> List.concat
            |> String.concat "\n\n"
            |> fun text ->
                // Phase 293 — the declared-support keys are held to the vocabulary. A doc path
                // the emission never consulted, a case refine on no referenced union case, a
                // projection on no selected kind: each is a key the generator would otherwise
                // drop without a word, and a typo there is an authored intent that never reached
                // the artefact.
                let unconsulted =
                    sup.Docs
                    |> Map.toList
                    |> List.map fst
                    |> List.filter (fun p -> not (consulted.Contains p))

                let caseKeys =
                    unions
                    |> List.collect (fun u -> u.Cases |> List.map (fun c -> u.Name + "." + c.Tag))
                    |> Set.ofList

                let unknownRefines =
                    sup.CaseRefines
                    |> Map.toList
                    |> List.map fst
                    |> List.filter (fun k -> not (caseKeys.Contains k))

                let kindTagSet = kinds |> List.map _.Tag |> Set.ofList

                let unknownProjections =
                    sup.KindProjections
                    |> Map.toList
                    |> List.map fst
                    |> List.filter (fun k -> not (kindTagSet.Contains k))

                match unconsulted, unknownRefines, unknownProjections with
                | [], [], [] -> Ok text
                | p :: _, _, _ ->
                    Error(
                        CodegenError.UnsupportedConstruct(
                            sprintf "a declared support doc at path '%s'" p,
                            "a doc path names a declaration this emission renders — `type:Name`, `case:Union.Tag`, `field:Owner.Field`, `enc:Name`, `dec:Name`, `encarm:Union.Tag`, `decarm:Union.Tag` — and a path it never consults is a doc that silently reaches nothing",
                            "spell the path as the emitter does, or remove the entry"
                        )
                    )
                | _, k :: _, _ ->
                    Error(
                        CodegenError.UnsupportedConstruct(
                            sprintf "a declared case refine for '%s'" k,
                            "a case refine is keyed `Union.Tag` on a union case this emission's kinds reach, and one on no such case would be dropped without a word",
                            "name a declared case of a referenced union, or remove the entry"
                        )
                    )
                | _, _, k :: _ ->
                    Error(
                        CodegenError.UnsupportedConstruct(
                            sprintf "a declared kind projection for '%s'" k,
                            "a kind projection is keyed by a kind tag among the kinds this emission selects, and one on no such kind would be dropped without a word",
                            "name a selected kind, or remove the entry"
                        )
                    )
        | Error e, _, _, _, _
        | _, Error e, _, _, _
        | _, _, Error e, _, _
        | _, _, _, Error e, _
        | _, _, _, _, Error e -> Error e

    /// Emit a compiling, self-contained F# encoder module (`moduleName`) for the named kinds,
    /// drawing in the enums/unions they transitively reference. `encodeNode : Node -> string`
    /// returns canonical wire via the shared `Canon.render`. Also emits a `nodeWitness`
    /// (`NodeWitness<Node, string>`) so the generated layer plugs into `Fuaran.Core.Tree`, a
    /// `runValidator` scaffold wiring `Fuaran.Core.Validator` over the generated `Node`, and
    /// `mk<Kind>` smart constructors applying the IDL-declared field defaults. `Error` on a
    /// construct the generator cannot yet emit (`CodegenError` — GP4/GP5), reported at generation
    /// time rather than as an exception.
    ///
    /// The emitted text is LF-terminated whatever the generator was built from and whatever the
    /// declared support carries — see [[normalizeEol]].
    let fsharpModuleWith
        (sup: Support)
        (moduleName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        fsharpModuleUnnormalised sup [] false moduleName idl kindTags
        |> Result.map normalizeEol

    /// Phase 374 — `fsharpModuleWith` plus the requested structural derivations
    /// ([[FSharpDerive]]), and (Phase 377) the collecting decoders and public per-spec entries
    /// when `decoders` is set. No request and no decoders IS `fsharpModuleWith`, byte for byte.
    let fsharpModuleDerived
        (sup: Support)
        (requests: FSharpDerive.Request list)
        (decoders: bool)
        (moduleName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        fsharpModuleUnnormalised sup requests decoders moduleName idl kindTags
        |> Result.map normalizeEol
