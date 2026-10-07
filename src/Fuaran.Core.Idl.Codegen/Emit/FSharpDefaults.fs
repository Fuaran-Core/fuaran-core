namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.Annotations

/// The F# DEFAULT literals — the one renderer of an IDL-declared default (`fsDefaultLit`),
/// the host-only placeholder, the float literal — and the `mk<Kind>` smart constructors over
/// them.
module internal FSharpDefaults =

    /// The F# expression a HostOnly field takes on decode — its `TFn` placeholder.
    /// A host-only field must be a `TFn`, because that is what carries both the
    /// declared host type and the value to restore; anything else is an IDL defect
    /// the generator refuses rather than guesses at.
    let hostOnlyLit (f: IdlField) : Result<string, CodegenError> =
        match f.Type with
        | TFn sg -> Ok sg.Placeholder
        // Phase 195 — an IDL defect, refused as a value. It was the one refusal in the
        // decode leg that crashed the generator rather than reporting: a HostOnly slot is
        // wire-absent by declaration, so the placeholder is the ONLY thing that can put a
        // value back, and a slot that declares none has nothing for the generator to guess.
        | other ->
            Error(
                CodegenError.UnsupportedConstruct(
                    sprintf "the HostOnly field '%s', whose declared type is %A rather than a TFn" f.Name other,
                    "GP5: the refusal names the construct and thereby the set that IS supported",
                    "declare a HostOnly slot as a TFn — it is what carries both the host type and the decoder's placeholder"
                )
            )

    /// An F# FLOAT literal for a declared default. `ToString("R")` alone will not do: a
    /// whole-valued double renders as `0`, which F# reads as an INT literal and which then fails
    /// to type-check at a float slot — in a pattern position silently for the reader, loudly for
    /// the compiler. The `.0` suffix is added when the rendering carries no decimal point or
    /// exponent. Non-finite values are refused by the caller rather than spelled here: `nan` and
    /// `infinity` are F# identifiers, not literals, so they are illegal in the pattern position
    /// the encoder's omit test needs.
    let fsFloatLit (f: float) : string =
        let s = f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)

        if s.Contains "." || s.Contains "E" || s.Contains "e" then
            s
        else
            s + ".0"

    /// **The one admissibility rule BOTH default backends apply, written once.** A default whose
    /// case is the union's DECLARED TRANSPARENT case (`Harden.TransparentUnions` — the case that
    /// encodes BARE, without the `$type` discriminator) is refused by every backend.
    ///
    /// It is here, above both deciders, because it was written once and needed twice. Phase 124
    /// added the TypeScript refusal — a `$type`-tagged omit predicate would be about a value the
    /// JS encoder never writes — and added no F# counterpart, because the F# omit test is a
    /// pattern match on the HOST value, where the case is not transparent at all and renders
    /// perfectly. Each backend was locally right and the pair was wrong: a vocabulary declaring
    /// such a default generated in F# and refused in TypeScript, which is a generator that ships
    /// two hosts that do not agree about what the vocabulary means. Phase 125's generative
    /// agreement property found it; the 2026-09-13 ruling is that the backends must AGREE, and
    /// the narrower side wins because the wire is the thing both hosts have to share.
    ///
    /// One predicate rather than two matching checks is the point: `fsDefaultLit` and
    /// `tsIsDefault` already decide the REST of their admissibility separately (they must — F#
    /// has literals and patterns where JS has only `===`), and this is the one rule that is about
    /// the WIRE rather than about either host language. A rule about the wire that is spelled
    /// twice is a rule that drifts, and this one already had.
    let isDeclaredTransparentCase (idl: Idl) (u: IdlUnion) (tag: string) : bool =
        TransparentUnion.tag idl.Harden u = Some tag

    /// The F# source form of a declared default VALUE — enums (`ToneVariant.Default`), scalars,
    /// the EMPTY LIST (Phase 1080), a nullary union case (`CellFormat.None`) and, since Phase 124,
    /// a VALUE-CARRYING union case (`Slot.Fixed(0.0)`, `Binding.Static(Some 0)`) and a record,
    /// nested to any depth.
    ///
    /// **The admissible set is decided by a constraint that is easy to miss: the string is used in
    /// both an EXPRESSION and a PATTERN position.** The generated encoder tests a union default by
    /// `match s.X with | <lit> -> None | _ -> …` (Phase 691 — an element type that reaches a
    /// closure carries no equality, so the union arm cannot use `=`), while the decoder's `dDef`
    /// restore and the smart constructors use the very same string as an expression. Constants,
    /// a union-case application over constants, `Some`/`None`, a record literal and `[]` are all
    /// legal in both. That is why a non-empty list stays refused even though `[ a; b ]` renders
    /// perfectly well: it is legal in both positions, but the encoder's omit test for a LIST field
    /// is `List.isEmpty` (which has no non-empty analogue) precisely because equality may not
    /// compile at the element type.
    ///
    /// `Error` ⇒ the generator cannot render it, and the module REFUSES rather than emitting an
    /// artefact that contradicts its own declaration — see [[CodegenError.UnsupportedDefault]].
    let rec fsDefaultLit (idl: Idl) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> =
        let refuse () =
            Error(CodegenError.UnsupportedDefault(t, v))

        match t, v with
        // F# cannot spell an unpaired surrogate in a literal ([[SourceLit.isWellFormed]]); the
        // encoder refuses such a string too, so there is no value here to reproduce.
        | TStr, VStr s when not (SourceLit.isWellFormed s) -> refuse ()
        | TStr, VStr s -> Ok(SourceLit.fsString s)
        | TInt, VInt i -> Ok(string i)
        | TBool, VBool b -> Ok(if b then "true" else "false")
        // A non-finite double has no F# LITERAL (`nan` / `infinity` are identifiers), so it has
        // no pattern spelling either and is refused rather than mis-emitted — the §5 sentinel
        // Phase 1063 added is a WIRE spelling, and this is a host-source position.
        | TFloat, VFloat f when System.Double.IsFinite f -> Ok(fsFloatLit f)
        // A whole-valued float authored as an integer — the canonical artifact writes one that
        // way, so the emitter must read it back at a float slot (the same asymmetry `dFloat`
        // carries on the decode side).
        | TFloat, VInt i -> Ok(fsFloatLit (float i))
        // The wire string resolves through [[IdlEnum.CaseOf]] to the host case the generated
        // type declares, and a wire string the enum does not admit is refused (Phase 292) —
        // it used to fall through as the case name, emitting an identifier nothing declared.
        | TEnum n, VEnum c ->
            match
                idl.Enums
                |> List.tryFind (fun e -> e.Name = n)
                |> Option.bind (fun e -> e.CaseOf c)
            with
            | Some case -> Ok(n + "." + case)
            | None -> refuse ()
        | TList _, VList [] -> Ok "[]"
        | TRecord n, VRecord authored ->
            match CodegenLookup.tryRecord idl n with
            | None -> refuse ()
            | Some r ->
                r.Fields
                |> List.map (fun rf ->
                    fsDefaultField idl Map.empty rf authored
                    |> Result.map (fun e -> pascal rf.Name + " = " + e))
                |> sequenceR
                |> Result.map (recordLit r.Name)
        | TUnion(n, args), VUnion(tag, authored) ->
            match CodegenLookup.tryUnion idl n with
            | None -> refuse ()
            | Some u when List.length u.Params <> List.length args -> refuse ()
            // The shared wire rule — see [[isDeclaredTransparentCase]]. This arm is what the
            // 2026-09-13 ruling added: the case renders as an F# pattern and expression without
            // difficulty, and is refused anyway, because the TypeScript backend cannot test for
            // it on the wire and a default only one of the two hosts honours is not a default.
            | Some u when isDeclaredTransparentCase idl u tag -> refuse ()
            | Some u ->
                match u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                | None -> refuse ()
                | Some c ->
                    let subst = TypeParams.bind u args |> Option.defaultValue Map.empty

                    // The DECLARED fields decide the arity, never the authored ones: `Slot.Fixed`
                    // is a one-argument constructor whatever an `IdlValue` happens to carry, and
                    // emitting the bare tag for a case that takes arguments would emit a FUNCTION
                    // where a value belongs. That was reachable before Phase 124 — `VUnion(tag,
                    // [])` matched on any union — and is exactly the artefact-vs-declaration
                    // divergence this phase closes, so it refuses now.
                    c.Fields
                    |> List.map (fun cf -> fsDefaultField idl subst cf authored)
                    |> sequenceR
                    |> Result.map (fun parts ->
                        match parts with
                        | [] -> n + "." + tag
                        | ps -> sprintf "%s.%s(%s)" n tag (String.concat ", " ps))
        | _ -> refuse ()

    /// One declared field of a union case or a record, rendered from the authored value set —
    /// honouring the same presence rules the encoder writes it under. A HostOnly slot is refused
    /// rather than filled with its placeholder: the placeholder is an arbitrary host EXPRESSION
    /// (`ignore`), which is not legal in the pattern position the encoder's omit test needs.
    and fsDefaultField
        (idl: Idl)
        (subst: Map<string, IdlType>)
        (f: IdlField)
        (authored: (string * IdlValue) list)
        : Result<string, CodegenError> =
        let ft = TypeParams.substitute subst f.Type

        match authored |> List.tryFind (fun (n, _) -> n = f.Name) with
        | Some(_, av) when av <> VAbsent ->
            match f.Opt with
            | Optional -> fsDefaultLit idl ft av |> Result.map (fun e -> "Some(" + e + ")")
            | HostOnly -> Error(CodegenError.UnsupportedDefault(ft, av))
            | Required
            | OmitDefault _ -> fsDefaultLit idl ft av
        | _ ->
            match f.Opt with
            | Optional -> Ok "None"
            | OmitDefault d -> fsDefaultLit idl ft d
            | HostOnly
            | Required -> Error(CodegenError.UnsupportedDefault(ft, VAbsent))


    // -----------------------------------------------------------------------
    // Phase 317 increment 7 — the IDL-declared-defaults leg. Emit a smart
    // constructor per kind: required fields *without* a declared default are
    // parameters; IDL-declared defaults are filled (the Phase 307 ARIA / variant
    // case — a field the author shouldn't have to repeat), and other optionals
    // default to `None`. The authoring ergonomics half of the structural set.
    // -----------------------------------------------------------------------

    /// The F# source expression for a declared default value, in the context of the
    /// field's IDL type (so a `VEnum "Standard"` on a `HeadingVariant` field emits
    /// `HeadingVariant.Standard`).
    ///
    /// **Phase 124 — this IS [[fsDefaultLit]], and the alias is the point.** The two rendered
    /// overlapping-but-different sets from two separate match expressions: a smart constructor
    /// could fill a `TStr` default the encoder's omit test could not spell, and (the direction
    /// that mattered) neither could spell a value-carrying union. One contract, one renderer, so
    /// the smart constructor, the encoder's omit test and the decoder's restore cannot come apart.
    /// Phase 374 — the value a field takes when the CALLER passes none, or `None` when it has no
    /// such value and must be passed (a `Required` field with no declared default). This is the
    /// one rule both the `mk<Kind>` smart constructors and the derived default records apply, so a
    /// default record and a constructor called with the same arguments cannot disagree.
    let fieldValue (idl: Idl) (declared: IdlValue option) (f: IdlField) : Result<string, CodegenError> option =
        match declared, f.Opt with
        | Some v, Required -> Some(fsDefaultLit idl f.Type v)
        | Some v, Optional -> Some(fsDefaultLit idl f.Type v |> Result.map (fun e -> "Some(" + e + ")"))
        | None, Required -> None
        | None, Optional -> Some(Ok "None")
        // HostOnly: not a ctor param either — the field takes its placeholder.
        | _, HostOnly -> Some(hostOnlyLit f)
        // OmitDefault: not a ctor param — the field takes its identity default.
        | _, OmitDefault d -> Some(fsDefaultLit idl f.Type d)

    /// Emit the smart constructors (`mk<Kind>`) over the generated `Node`. `Error` on a kind whose
    /// IDL-declared default has no code emission (`fsDefaultLit` — GP4/GP5).
    let defaultsDecl
        (projections: Map<string, Projection>)
        (msg: Set<string>)
        (idl: Idl)
        (kinds: IdlKind list)
        : Result<string, CodegenError> =
        let nodeArgs = declParams msg "Node" []
        let fsType = fsTypeIn msg

        let defaultFor (kindTag: string) (fieldName: string) : IdlValue option =
            idl.Defaults
            |> List.tryPick (fun d ->
                if d.Kind = kindTag && d.Field = fieldName then
                    Some d.Value
                else
                    None)

        // Phase 690 — a smart constructor fills the envelope with its identity value, so the
        // common case stays `mkHeading "h" 2 text`. An envelope field that is neither optional
        // nor defaulted would have to become a parameter; none is, and the generator says so
        // rather than guessing.
        //
        // Phase 124 — an unrenderable envelope default was a THROW here, the one refusal in
        // this leg that was not a typed `CodegenError` even though the function it sat inside
        // already returned one. It is the same `UnsupportedDefault` as every other path now. The
        // envelope is also computed ONCE rather than per kind: it does not depend on the kind.
        //
        // Phase 195 — and a REQUIRED envelope member is no longer a throw either. It is
        // emitted when a default is declared for it, and refused as data when none is:
        // `RequiredEnvelopeField`, naming the member, its type and the two alternatives. This is
        // the shape the full node envelope needs — a required member that always carries a value
        // the smart constructor can fill — and it is why the refusal narrowed rather than moved:
        // it is now reserved for the member that is genuinely under-determined.
        //
        // The envelope has no kind tag, so a declared envelope default is addressed by the EMPTY
        // `IdlDefault.Kind`. A node kind's tag is its `$type` discriminator on the wire and can
        // never be the empty string, so the empty address is free and unambiguous — which is why
        // this needs no widening of the published `IdlDefault` record to express.
        let envelopeDefaultFor (fieldName: string) : IdlValue option = defaultFor "" fieldName

        let envelopeAssigns: Result<string, CodegenError> =
            idl.NodeFields
            |> List.map (fun f ->
                let assign e = sprintf "; %s = %s" (pascal f.Name) e

                match f.Opt with
                | Optional -> Ok(sprintf "; %s = None" (pascal f.Name))
                | OmitDefault d -> fsDefaultLit idl f.Type d |> Result.map assign
                | HostOnly -> hostOnlyLit f |> Result.map assign
                | Required ->
                    match envelopeDefaultFor f.Name with
                    | Some d -> fsDefaultLit idl f.Type d |> Result.map assign
                    | None ->
                        Error(
                            CodegenError.RequiredEnvelopeField(
                                f.Name,
                                f.Type,
                                "declare it Optional or OmitDefault, or carry a declared default for it (an IdlDefault whose Kind is the empty envelope address)"
                            )
                        ))
            |> concatR ""

        let ctor (k: IdlKind) : Result<string, CodegenError> =
            let parmsR =
                k.Fields
                |> List.filter (fun f -> f.Opt = Required && (defaultFor k.Tag f.Name).IsNone)
                |> List.map (fun f -> fsType f.Type |> Result.map (fun ty -> sprintf "(%s: %s)" (ident f.Name) ty))
                |> sequenceR
                |> Result.map (fun ps -> "(id: string)" :: ps |> String.concat " ")

            let fieldExpr (f: IdlField) : Result<string, CodegenError> =
                match fieldValue idl (defaultFor k.Tag f.Name) f with
                | Some e -> e
                | None -> Ok(ident f.Name)

            k.Fields
            |> List.map (fun f -> fieldExpr f |> Result.map (fun e -> sprintf "%s = %s" (pascal f.Name) e))
            |> sequenceR
            |> Result.map (recordLit (k.Tag + "Spec"))
            |> Result.bind (fun record ->
                parmsR
                |> Result.bind (fun parms ->
                    envelopeAssigns
                    |> Result.map (fun envelope ->
                        sprintf
                            "let mk%s %s : Node%s =\n    { Id = id; Kind = NodeKind.%s %s%s }"
                            k.Tag
                            parms
                            nodeArgs
                            k.Tag
                            record
                            envelope)))

        // Phase 124 — a PROJECTED kind emits no generated constructor (the projection supplies
        // its own), which before this phase meant its declared defaults were never rendered and
        // so never checked: an unrenderable default on a projected kind escaped `fsDefaultLit`
        // entirely and reached the encoder, where it fell back to always-emit. Validating them
        // here keeps "a declaration the generator cannot render refuses" true of a projected kind
        // too, without emitting a constructor for it.
        let projectedDefaultsChecked (k: IdlKind) : Result<unit, CodegenError> =
            k.Fields
            |> List.map (fun f ->
                match defaultFor k.Tag f.Name, f.Opt with
                | Some v, _ -> fsDefaultLit idl f.Type v |> Result.map ignore
                | None, OmitDefault d -> fsDefaultLit idl f.Type d |> Result.map ignore
                | None, _ -> Ok())
            |> sequenceR
            |> Result.map ignore

        kinds
        |> List.map (fun k ->
            // Phase 945 — a projected kind's ctor is the projection's own (or absent):
            // the generated one would construct the IDL-derived record, which under a
            // projection is not the record that exists.
            match projections.TryFind k.Tag with
            | Some p -> projectedDefaultsChecked k |> Result.map (fun () -> p.Mk |> Option.toList)
            | None -> ctor k |> Result.map List.singleton)
        |> sequenceR
        |> Result.map List.concat
        |> Result.map (fun ctors ->
            "// Smart constructors — required-without-default fields are parameters; IDL-declared\n// defaults are filled, other optionals default to None."
            + "\n\n"
            + String.concat "\n\n" ctors)
