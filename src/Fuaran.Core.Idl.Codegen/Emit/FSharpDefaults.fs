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

    /// Phase 524 (511.t4) — the F# PRINTER of a [[MemberValue]] (the one presence rule,
    /// [[Decl.memberValue]]): `value` prints a carried value (a declared literal, or a scaffolded
    /// authored value), `identity` an omit-default member's identity default, `refuse` spells the
    /// site's refusal. `None` is a constructor parameter.
    let fsMember
        (value: IdlValue -> Result<string, CodegenError>)
        (identity: IdlValue -> Result<string, CodegenError>)
        (refuse: IdlValue -> CodegenError)
        (f: IdlField)
        (mv: MemberValue)
        : Result<string, CodegenError> option =
        match mv with
        | MemberValue.Carries v -> Some(value v)
        | MemberValue.Optional v -> Some(value v |> Result.map (fun e -> "Some(" + e + ")"))
        | MemberValue.IdentityDefault d -> Some(identity d)
        | MemberValue.Absent -> Some(Ok "None")
        | MemberValue.Placeholder -> Some(hostOnlyLit f)
        | MemberValue.Parameter -> None
        | MemberValue.Refused v -> Some(Error(refuse v))

    /// Phase 524 — the F# printer of a declared literal ([[LiteralTraversal]]): the F# source form
    /// of a declared default VALUE — enums (`ToneVariant.Default`), scalars, the EMPTY LIST (Phase
    /// 1080), a nullary union case (`CellFormat.None`) and, since Phase 124, a VALUE-CARRYING union
    /// case (`Slot.Fixed(0.0)`, `Binding.Static(Some 0)`) and a record, nested to any depth.
    let fsLiteralPrinter: LiteralPrinter =
        { Str = fun _ s -> SourceLit.fsString s
          Int = fun _ i -> string i
          Float = fun _ f -> fsFloatLit f
          Bool = fun _ b -> if b then "true" else "false"
          Enum = fun _ n case _ -> n + "." + case
          EmptyList = fun _ -> "[]"
          Record =
            fun _ r members ->
                members
                |> List.map (fun (f, e) -> pascal f.Name + " = " + e)
                |> recordLit r.Name
          Union =
            fun _ u c members ->
                match members with
                | [] -> u.Name + "." + c.Tag
                | ps -> sprintf "%s.%s(%s)" u.Name c.Tag (ps |> List.map snd |> String.concat ", ")
          Some = fun e -> "Some(" + e + ")"
          Absent = fun _ -> "None"
          Path = fun _ _ -> "" }

    /// The F# source form of a declared default value, printed by [[fsLiteralPrinter]].
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
    /// Phase 525 — what is ADMITTED is [[Decl.literal]]'s, the one admissibility the TypeScript
    /// and F* targets read too: an unpaired surrogate, a non-finite double (`nan` / `infinity` are
    /// identifiers, so they have no pattern spelling), an enum spelling [[IdlEnum.CaseOf]] does not
    /// resolve (Phase 292), and the union's declared transparent case (the 2026-09-13 ruling) are
    /// refused there. `Error` ⇒ the module REFUSES rather than emitting an artefact that
    /// contradicts its own declaration — see [[CodegenError.UnsupportedDefault]].
    let fsDefaultLit (idl: Idl) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> =
        LiteralTraversal.print fsLiteralPrinter idl "" t v

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
    /// Phase 524 (511.t4) — the CONSTRUCTOR site of the one presence rule ([[Decl.memberValue]]).
    let fieldValue (idl: Idl) (declared: IdlValue option) (f: IdlField) : Result<string, CodegenError> option =
        let lit = fsDefaultLit idl f.Type

        Decl.memberValue ValueSite.Constructor f.Opt declared
        |> fsMember lit lit (fun v -> CodegenError.UnsupportedDefault(f.Type, v)) f

    /// Emit the smart constructors (`mk<Kind>`) over the generated `Node`. `Error` on a kind whose
    /// IDL-declared default has no code emission (`fsDefaultLit` — GP4/GP5).
    let defaultsDecl (decl: Decl) : Result<string, CodegenError> =
        let projections, msg, idl, kinds = decl.Projections, decl.Msg, decl.Idl, decl.Kinds

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
                // Phase 524 (511.t4) — the envelope member takes the CONSTRUCTOR site's value, as a
                // kind's field does ([[fieldValue]]); the one difference is the parameter, which
                // the envelope has no place for and refuses by name.
                match fieldValue idl (envelopeDefaultFor f.Name) f with
                | Some r -> r |> Result.map (sprintf "; %s = %s" (pascal f.Name))
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
