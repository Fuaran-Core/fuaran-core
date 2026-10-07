namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.FSharpDefaults

/// The F# scaffold leg — an authored node tree as compilable host source, every wire string
/// through an escaped literal — and the provenance header both scaffold legs carry.
module internal Scaffold =

    // -----------------------------------------------------------------------
    // Phase 317 syntax-tree-emission leg + Phase 321 trust boundary. The
    // wire→source SCAFFOLD mode: emit host SOURCE that constructs a specific
    // node tree (the AI-emitted wire becomes compilable host code). This is the
    // only path where wire-derived VALUES land in source, so it is the
    // template-injection surface — every wire string goes through an escaped
    // literal (`SourceLit.fsString`), and an unsupported feature ERRORS rather than
    // mis-emitting. A hostile string therefore cannot break out of a literal:
    // proven by the breakout tests (a value crafted to inject code emerges only
    // as escaped data). The encoder MODULES above carry no wire DATA, but they do
    // carry IDL-authored TEXT — the discriminator, enum wire strings, a category,
    // annotation prose — and an `idl.json` is untrusted input (DECISIONS D95). So
    // the modules' claim is the same as this leg's, made the same way: every
    // splice of that text goes through `SourceLit` (Phase 292), and every name
    // spliced as an IDENTIFIER is one `Declare.errors` admits.
    // -----------------------------------------------------------------------

    /// Phase 252 — the value emitter's refusals, typed. Every arm of [[fsharpValue]]
    /// that cannot render answers with one of these rather than with a sentence, so the
    /// scaffold leg refuses in Phase 195's shape like every module emitter already did.
    let valueRefusal (construct: string) (alternative: string) : CodegenError =
        CodegenError.UnsupportedConstruct(
            construct,
            "GP5: the scaffold leg names what it cannot construct rather than emitting source that does not compile",
            alternative
        )

    /// A value that does not fit its declared type, or a name the vocabulary does not
    /// declare — the authored value is wrong, not the generator.
    let valueMismatch (what: string) : CodegenError =
        valueRefusal what "author the value against the vocabulary's declaration (Encode.encode refuses the same value)"

    /// Phase 252 — an escaped F# expression constructing a `JVal`, for the hosted arm of
    /// [[fsharpValue]]. Every string (keys included) goes through `SourceLit.fsString`, so wire data
    /// stays data; a negative integer is parenthesised (`JInt(-5)`, not `JInt -5`). `None` for
    /// a non-finite float, which has no F# literal.
    let rec fsJValLit (j: JVal) : string option =
        match j with
        | JStr s -> Some("JStr " + SourceLit.fsString s)
        | JInt i -> Some(sprintf "JInt(%d)" i)
        | JBool b -> Some(if b then "JBool true" else "JBool false")
        | JFloat f when System.Double.IsFinite f -> Some("JFloat(" + fsFloatLit f + ")")
        | JFloat _ -> None
        | JArr xs ->
            let items = xs |> List.map fsJValLit

            if items |> List.forall Option.isSome then
                Some("JArr [ " + (items |> List.choose id |> String.concat "; ") + " ]")
            else
                None
        | JObj fs ->
            let members =
                fs
                |> List.map (fun (k, v) ->
                    fsJValLit v |> Option.map (fun e -> "(" + SourceLit.fsString k + ", " + e + ")"))

            if members |> List.forall Option.isSome then
                Some("JObj [ " + (members |> List.choose id |> String.concat "; ") + " ]")
            else
                None

    /// The body of [[fsharpValue]]: the construction expression, with each hosted slot's place
    /// a binder (`__h<i>`) and its `(decode, literal)` pair appended to `hosted` in emission order.
    let rec private emitValue
        (hosted: ResizeArray<string * string>)
        (idl: Idl)
        (t: IdlType)
        (v: IdlValue)
        : Result<string, CodegenError> =
        // Phase 292 — a scalar or enum is the DECLARED-DEFAULT literal ([[fsDefaultLit]]):
        // one spelling for a value in source, so the scaffold no longer writes a whole float
        // as `2` (an int literal, FS0001 at a float slot), and its string literal is the
        // [[SourceLit.fsString]] every other F# literal is.
        match t, v with
        | TStr, VStr _
        | TInt, VInt _
        | TBool, VBool _ -> fsDefaultLit idl t v
        | TFloat, (VFloat _ | VInt _) ->
            fsDefaultLit idl t v
            |> Result.mapError (fun _ -> valueMismatch "a non-finite float, which has no F# literal")
        | TEnum name, VEnum wire ->
            match CodegenLookup.tryEnum idl name with
            | None -> Error(valueMismatch (sprintf "a value of the undeclared enum '%s'" name))
            | Some e when (e.CaseOf wire).IsNone ->
                Error(valueMismatch (sprintf "the wire string %A, which enum '%s' does not admit" wire name))
            | Some _ -> fsDefaultLit idl t v
        | TUnion(name, args), VUnion(tag, fields) ->
            match CodegenLookup.tryUnion idl name with
            | None -> Error(valueMismatch (sprintf "a value of the undeclared union '%s'" name))
            | Some u when List.length u.Params <> List.length args ->
                Error(valueMismatch (sprintf "union '%s' applied to %d type arguments" name (List.length args)))
            | Some u ->
                match u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                | None -> Error(valueMismatch (sprintf "the case '%s', which union '%s' does not declare" tag name))
                | Some c ->
                    let subst = TypeParams.bind u args |> Option.defaultValue Map.empty

                    c.Fields
                    |> List.map (fun f ->
                        fsPositional
                            hosted
                            idl
                            (sprintf "union case '%s.%s'" name tag)
                            { f with
                                Type = TypeParams.substitute subst f.Type }
                            (fields |> List.tryFind (fun (n, _) -> n = f.Name) |> Option.map snd))
                    |> sequenceR
                    |> Result.map (fun parts ->
                        match parts with
                        | [] -> name + "." + tag
                        | ps -> sprintf "%s.%s(%s)" name tag (String.concat ", " ps))
        | TRecord name, VRecord fields ->
            match CodegenLookup.tryRecord idl name with
            | None -> Error(valueMismatch (sprintf "a value of the undeclared record '%s'" name))
            | Some r ->
                fsAssignments hosted idl ("record '" + name + "'") r.Fields fields
                |> Result.map (recordLit name)
        | TNode, VNode(id, kindTag, fields) ->
            match CodegenLookup.tryKind idl kindTag with
            | None -> Error(valueMismatch (sprintf "a node of the undeclared kind '%s'" kindTag))
            | Some k ->
                fsAssignments hosted idl ("kind '" + kindTag + "'") k.Fields fields
                |> Result.map (fun recFields ->
                    sprintf
                        "{ Id = %s; Kind = NodeKind.%s %s }"
                        (SourceLit.fsString id)
                        kindTag
                        (recordLit (kindTag + "Spec") recFields))
        // Phase 698 — the enveloped form. The envelope's assignments sit on the
        // `Node` record itself (`Style = Some …`), the kind's on the spec record, and
        // both go through the SAME `fsAssignments`, so the presence rules cannot
        // drift between the two halves of a node.
        | TNode, VNodeEnv(id, envelope, kindTag, fields) ->
            match CodegenLookup.tryKind idl kindTag with
            | None -> Error(valueMismatch (sprintf "a node of the undeclared kind '%s'" kindTag))
            | Some k ->
                match
                    fsAssignments hosted idl ("kind '" + kindTag + "'") k.Fields fields,
                    fsAssignments hosted idl "node envelope" idl.NodeFields envelope
                with
                | Error e, _
                | _, Error e -> Error e
                | Ok recFields, Ok envFields ->
                    Ok(
                        sprintf
                            "{ Id = %s; Kind = NodeKind.%s %s%s }"
                            (SourceLit.fsString id)
                            kindTag
                            (recordLit (kindTag + "Spec") recFields)
                            (envFields |> List.map (fun a -> "; " + a) |> String.concat "")
                    )
        | TList inner, VList xs ->
            xs
            |> List.map (emitValue hosted idl inner)
            |> sequenceR
            |> Result.map (fun items -> "[ " + String.concat "; " items + " ]")
        | _, VAbsent -> Error(valueMismatch "an absent value (VAbsent) in a value position")
        // Phase 252 — a hosted slot that DECLARES its wire form is scaffolded through its own
        // codec: the wire value (checked against the form first) is emitted as an escaped
        // `JVal` literal and handed to the slot's `Decode` expression, so the host value is
        // built by the one function that defines it. A value outside the form is refused here
        // rather than at the scaffold's run time.
        | THosted({ Wire = Some w } as h), VJson j ->
            let inForm =
                match Decode.value idl w j, h.Format, j with
                | Error m, _, _ -> Error m
                | Ok _, None, _ -> Ok()
                | Ok _, Some fmt, JStr s when HostedFormat.admits fmt s -> Ok()
                | Ok _, Some fmt, _ -> Error(sprintf "not a '%s' string" fmt)

            match inForm, fsJValLit j with
            | Error m, _ -> Error(valueMismatch (sprintf "a hosted value outside its declared wire form (%s)" m))
            | _, None -> Error(valueMismatch "a hosted value holding a non-finite float, which has no F# literal")
            | Ok(), Some lit ->
                // Phase 384 — the decode is HOISTED: the slot's place in the value is a binder,
                // and `fsharpValue` binds it through the slot's codec ahead of the value, so a
                // codec that refuses answers a `DecodeError` rather than raising (it used to emit
                // `failwith`).
                hosted.Add((h.Decode, lit))
                Ok(sprintf "__h%d" (hosted.Count - 1))
        // What remains is REFUSED by construct, never mis-emitted: a hosted slot's value is
        // a host type only its own codec knows how to build (unless its wire form is
        // declared, above), a closure is behaviour (human-bound, Phase 318), and a JSON /
        // map / sentinel / op slot has no scaffold arm yet.
        | THosted h, _ ->
            Error(
                valueRefusal
                    (sprintf "a value of the hosted slot '%s'" h.FSharp)
                    "construct the host value in hand-written source and assign it to the field; the scaffold carries wire data, and a hosted slot's value is the host codec's"
            )
        | (TFn _ | TClosure), _ ->
            Error(
                valueRefusal
                    "a closure-typed value"
                    "behaviour is human-bound (Phase 318): assign the closure in hand-written source"
            )
        | (TJson | TMap _ | TOpaque | TKind | TOp | TVar _), _ ->
            Error(
                valueRefusal
                    (sprintf "a value at %A, which the scaffold leg has no arm for" t)
                    "author the slot in hand-written source, or scaffold the surrounding node without it"
            )
        | _ -> Error(valueMismatch (sprintf "a value that does not match IDL type %A" t))

    /// One POSITIONAL union-case field, under the presence rules its generated declaration
    /// states (an `Optional` field is `T option`). `authored` is the value at the field's name,
    /// if any.
    and private fsPositional
        (hosted: ResizeArray<string * string>)
        (idl: Idl)
        (where: string)
        (f: IdlField)
        (authored: IdlValue option)
        : Result<string, CodegenError> =
        match authored with
        | None
        | Some VAbsent ->
            match f.Opt with
            | Optional -> Ok "None"
            | HostOnly -> hostOnlyLit f
            | OmitDefault d -> fsDefaultLit idl f.Type d
            | Required -> Error(valueMismatch (sprintf "%s without its required field '%s'" where f.Name))
        | Some fv ->
            match f.Opt with
            // A host-only field has no wire projection, so a wire value at its name is not
            // its value — take the placeholder.
            | HostOnly -> hostOnlyLit f
            | Optional -> emitValue hosted idl f.Type fv |> Result.map (fun s -> "Some(" + s + ")")
            | OmitDefault _
            | Required -> emitValue hosted idl f.Type fv

    /// Record-field assignments (`Label = …; Icon = Some …`) for one declared field
    /// list against one authored field list, honouring every presence rule. Shared
    /// by a kind's spec record, (Phase 698) the node envelope and (Phase 252) a
    /// non-discriminated record — `where` names the owner for the refusals only.
    and private fsAssignments
        (hosted: ResizeArray<string * string>)
        (idl: Idl)
        (where: string)
        (declared: IdlField list)
        (authored: (string * IdlValue) list)
        : Result<string list, CodegenError> =
        declared
        |> List.map (fun f ->
            fsPositional hosted idl where f (authored |> List.tryFind (fun (n, _) -> n = f.Name) |> Option.map snd)
            |> Result.map (fun e -> pascal f.Name + " = " + e))
        |> sequenceR


    /// Emit an F# value-construction expression for an authored `IdlValue` of
    /// type `t`, building values of the GENERATED types (`Gen.fsharpModule`'s). Wire-derived
    /// strings route through `SourceLit.fsString`; an unsupported shape is REFUSED rather than
    /// mis-emitted (the syntax-tree-emission contract), and the refusal is a [[CodegenError]]
    /// (Phase 252) — the same typed shape every module emitter returns.
    ///
    /// BREAKING (Phase 252): the error channel was a plain `string`. A caller that wants the
    /// sentence adapts with `|> Result.mapError CodegenError.describe`.
    ///
    /// Phase 252 also widened what it constructs: an enum literal resolves through
    /// [[IdlEnum.CaseOf]] to the HOST case the generated type declares (the wire string is
    /// not an F# identifier in general — `"documents"` against `Documents`), and a
    /// [[TRecord]] value emits a record expression under the same presence rules a kind's
    /// spec record uses. A union case's positional fields honour presence too: an
    /// `Optional` one is `Some(…)` / `None`, as its generated declaration says.
    ///
    /// **Total in the code it emits, too (Phase 384).** The expression is a
    /// `Result<'T, DecodeError>` — `Ok` of the constructed value — because a hosted slot's value
    /// is built at the scaffold's run time by the slot's own codec, which can refuse it. Each
    /// hosted value is decoded ahead of the construction and bound by name; a refusal is the
    /// generated decoders' own (D109): `OutOfRange`, "a value the slot's host codec admits",
    /// with the codec's sentence. The emitted source holds no `failwith`. Where nothing hosted
    /// is reached the expression is `Ok` of the construction, so its type does not depend on the
    /// value. `DecodeError` and `DecodeCode` are written fully qualified, so the expression
    /// needs no `open`.
    let fsharpValue (idl: Idl) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> =
        let hosted = ResizeArray<string * string>()

        emitValue hosted idl t v
        |> Result.map (fun body ->
            let bound =
                (("Ok(" + body + ")"), List.indexed (List.ofSeq hosted) |> List.rev)
                ||> List.fold (fun inner (i, (decode, lit)) ->
                    sprintf
                        "Result.bind (fun __h%d -> %s) ((%s) (%s) |> Result.mapError (fun (__e: string) -> Fuaran.Core.DecodeError.make Fuaran.Core.DecodeCode.OutOfRange \"a value the slot's host codec admits\" __e))"
                        i
                        inner
                        decode
                        lit)

            "(" + bound + " : Result<_, Fuaran.Core.DecodeError>)")

    /// A provenance-stamp header for AI-scaffolded source (Phase 321) — records
    /// the source wire hash + the typed actor so generated code is auditable +
    /// reproducible, and states the trust-split invariant: generated code is
    /// inert structure; behaviour is human-bound (named holes, Phase 318).
    let provenanceHeader (commentPrefix: string) (sourceWireHash: string) (actor: string) : string =
        String.concat
            "\n"
            [ commentPrefix
              + " AI-scaffolded by Fuaran.Core.Idl.Gen — INERT structure; behaviour is human-bound (named holes, Phase 318)."
              commentPrefix + " source-wire-hash: " + sourceWireHash
              commentPrefix + " actor: " + actor ]
