namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.Annotations
open Fuaran.Core.Idl.Emit.FSharpTypes
open Fuaran.Core.Idl.Emit.FSharpDefaults
open Fuaran.Core.Idl.Emit.FSharpCanonicalForm
open Fuaran.Core.Idl.Emit.FSharpEncode
open Fuaran.Core.Idl.Emit.FSharpDecode

/// The F# printers of the INLINE declarations (an inline record, a presence-keyed union) and the
/// helpers a module takes only when its vocabulary reaches them.
module internal FSharpInline =

    // -----------------------------------------------------------------------
    // Phase 474 (DECISIONS.md D220) — the INLINE declarations. An inline record and a presence-keyed
    // union have no object of their own: each is written as a list of member pieces the holding
    // object splices in (`yield! encMembers<Name> …`), and read from the holding object's members
    // (`decMembers<Name> __fs`, `colMembers<Name> __ov __fs`). A presence-keyed union's reading picks
    // its case with `dPick` and checks each synthesised member with `dSynth`, the interpreter's
    // reading (`Idl.Decode`) defect for defect.
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // Phase 639 (DECISIONS.md D251) — the wider data universe's helpers, emitted only when the
    // vocabulary reaches one of its types, so every other module's bytes are unchanged. The
    // canonical-spelling predicates and the code-point comparison are the interpreter's
    // (`ScalarText`) written for a module that references only Fuaran.Core; the conformance vectors
    // under `conformance/idl/` hold the two to one reading.
    // -----------------------------------------------------------------------

    /// The encode-side helpers of the wider data universe.
    let widerEncodeHelpers: string = Template.text "fs-wider-encode.fs.txt"

    /// The decode-side helpers of the wider data universe (after [[decodeHelpers]], whose `dFail`,
    /// `dUnder` and `dList` they read; `canonicalCompare` is [[widerEncodeHelpers]]'s).
    let widerDecodeHelpers: string = Template.text "fs-wider-decode.fs.txt"

    /// The decode helpers a presence-keyed union's reading needs, emitted only when one is reached:
    /// `dPick` picks the case (the candidates whose key is present, less those whose key another
    /// candidate synthesises; two left is `OutOfRange` at the second key), `dSynth` checks a
    /// synthesised member is present and canonically equal to its declared value.
    let keyedHelpers: string =
        [ "// Phase 474 — a presence-keyed union's case: the cases whose key is present, less those whose key"
          "// another present case synthesises. None left is `None`; two left is the both-present refusal,"
          "// `OutOfRange` at the second one's key. `rules` is each case's key and the members it synthesises."
          "let private dPick (fs: (string * JVal) list) (rules: (string * string list) list) : Result<int option, DecodeError> ="
          "    let has (k: string) = fs |> List.exists (fun (n, _) -> n = k)"
          "    let present = rules |> List.indexed |> List.filter (fun (_, (k, _)) -> has k)"
          "    let explained (i: int, (k: string, _: string list)) ="
          "        present |> List.exists (fun (j, (_, syn)) -> j <> i && List.contains k syn)"
          "    match present |> List.filter (explained >> not) with"
          "    | [] -> Ok None"
          "    | [ (i, _) ] -> Ok(Some i)"
          "    | (_, (a, _)) :: (_, (b, _)) :: _ ->"
          "        dFail DecodeCode.OutOfRange (\"at most one of \" + (rules |> List.map (fun (k, _) -> \"'\" + k + \"'\") |> String.concat \", \")) (\"'\" + b + \"' beside '\" + a + \"'\")"
          "        |> dUnder (PathSegment.Key b)"
          ""
          "// Phase 474 — a member a presence-keyed case synthesises: present, and canonically its declared value."
          "let private dSynth (name: string) (expected: string) (fs: (string * JVal) list) : Result<unit, DecodeError> ="
          "    match fs |> List.tryFind (fun (k, _) -> k = name) with"
          "    | None -> Error(Decoder.missing name)"
          "    | Some(_, j) when Canonical.write Rendering.CrossHost j = expected -> Ok()"
          "    | Some(_, j) -> dFail DecodeCode.OutOfRange expected (Canonical.write Rendering.CrossHost j) |> dUnder (PathSegment.Key name)" ]
        |> String.concat "\n"

    /// The `dPick` rules literal of a presence-keyed union.
    let pickRules (keyed: KeyedCase list) : string =
        "[ "
        + (keyed
           |> List.map (fun k ->
               match k.Synthesised with
               | [] -> sprintf "(%s, [])" (SourceLit.fsString k.Key)
               | synthesised ->
                   sprintf
                       "(%s, [ %s ])"
                       (SourceLit.fsString k.Key)
                       (synthesised |> List.map (fst >> SourceLit.fsString) |> String.concat "; "))
           |> String.concat "; ")
        + " ]"

    /// A keyed case's constructor over its members' binders.
    let private keyedCtor (u: IdlUnion) (c: IdlUnionCase) : string =
        match c.Fields with
        | [] -> sprintf "%s.%s" u.Name c.Tag
        | fs -> sprintf "%s.%s(%s)" u.Name c.Tag (fs |> List.map (fun f -> ident f.Name) |> String.concat ", ")

    /// The synthesised members of a keyed case as `(binder, read)` pairs: each checked by `dSynth`.
    let private synthBinders (idl: Idl) (u: IdlUnion) (k: KeyedCase) : Result<(string * string) list, CodegenError> =
        Decl.synthesisedJson idl u k
        |> Result.map (
            List.map (fun (name, j) ->
                "()",
                sprintf
                    "dSynth %s %s __fs"
                    (SourceLit.fsString name)
                    (SourceLit.fsString (Canonical.write Rendering.CrossHost j)))
        )

    /// An inline record's member writer: its members' pieces, read off `s`.
    let inlineRecordEncoder (msg: Set<string>) (idl: Idl) (r: IdlRecord) : Result<string, CodegenError> =
        let ps = declParams msg r.Name []

        objectPieces idl r.Fields
        |> Result.map (fun pieces ->
            sprintf
                "and private encMembers%s%s (s: %s%s) : (string * JVal) option list =\n    [ %s ]"
                r.Name
                ps
                r.Name
                ps
                pieces)

    /// A presence-keyed union's member writer: per case, its members and its synthesised members,
    /// in the union's roster order.
    let keyedEncoder
        (msg: Set<string>)
        (idl: Idl)
        (u: IdlUnion)
        (keyed: KeyedCase list)
        : Result<string, CodegenError> =
        let ps = declParams msg u.Name []
        let member' = memberEncPrinter idl
        let roster = Decl.keyedRoster u

        u.Cases
        |> List.map (fun c ->
            let k =
                keyed
                |> List.tryFind (fun k -> k.Case = c.Tag)
                |> Option.defaultValue
                    { Case = c.Tag
                      Key = ""
                      Synthesised = [] }

            let own =
                c.Fields
                |> List.map (fun f ->
                    MemberEncTraversal.encMember member' c.Fields (fun g -> ident g.Name) f
                    |> Result.map (fun piece -> f.Name, MemberEncTraversal.print member' piece))
                |> sequenceR

            match own, Decl.synthesisedJson idl u k with
            | Error e, _
            | _, Error e -> Error e
            | Ok own, Ok synth ->
                let all =
                    own
                    @ (synth
                       |> List.map (fun (n, j) ->
                           n, sprintf "Some(%s, %s)" (SourceLit.fsString n) (FSharpDefaults.fsJValLit j)))

                let pieces =
                    roster
                    |> List.choose (fun n -> all |> List.tryFind (fun (m, _) -> m = n) |> Option.map snd)
                    |> String.concat "; "

                let pat =
                    match c.Fields with
                    | [] -> ""
                    | [ f ] -> " " + ident f.Name
                    | fs -> " (" + (fs |> List.map (fun f -> ident f.Name) |> String.concat ", ") + ")"

                Ok(sprintf "    | %s.%s%s -> [ %s ]" u.Name c.Tag pat pieces))
        |> sequenceR
        |> Result.map (fun arms ->
            sprintf
                "and private encMembers%s%s (v: %s%s) : (string * JVal) option list =\n    match v with\n%s"
                u.Name
                ps
                u.Name
                ps
                (String.concat "\n" arms))

    /// An inline record's member reader: its members read from the holding object `__fs`.
    let inlineRecordDecoder (msg: Set<string>) (idl: Idl) (r: IdlRecord) : Result<string, CodegenError> =
        let assigns =
            r.Fields |> List.map (fun f -> sprintf "%s = %s" (pascal f.Name) (ident f.Name))

        fieldBinders idl r.Fields
        |> Result.map (fun binders ->
            sprintf
                "and private decMembers%s (__fs: (string * JVal) list) : Result<%s%s, DecodeError> =\n%s"
                r.Name
                r.Name
                (objParams msg r.Name [])
                (bindChain "    " binders ("Ok " + recordLit r.Name assigns) 0))

    /// A presence-keyed union's reader: the case `dPick` names, its members, then its synthesised
    /// members; `None` when no case's key is present.
    let keyedDecoder
        (msg: Set<string>)
        (idl: Idl)
        (u: IdlUnion)
        (keyed: KeyedCase list)
        : Result<string, CodegenError> =
        u.Cases
        |> List.mapi (fun i c ->
            match keyed |> List.tryFind (fun k -> k.Case = c.Tag) with
            | None -> Ok ""
            | Some k ->
                match fieldBinders idl c.Fields, synthBinders idl u k with
                | Error e, _
                | _, Error e -> Error e
                | Ok binders, Ok synth ->
                    Ok(
                        sprintf
                            "    | Some %d ->\n%s"
                            i
                            (bindChain "        " (binders @ synth) (sprintf "Ok(Some(%s))" (keyedCtor u c)) 0)
                    ))
        |> sequenceR
        |> Result.map (fun arms ->
            sprintf
                "and private decMembers%s (__fs: (string * JVal) list) : Result<%s%s option, DecodeError> =\n    dPick __fs %s |> Result.bind (fun __i ->\n    match __i with\n%s\n    | _ -> Ok None)"
                u.Name
                u.Name
                (objParams msg u.Name [])
                (pickRules keyed)
                (arms |> List.filter (fun a -> a <> "") |> String.concat "\n"))

    /// An inline record's collecting member reader.
    let inlineRecordColDecoder (msg: Set<string>) (r: IdlRecord) (members: (IdlField * string) list) : string =
        let assigns =
            r.Fields |> List.map (fun f -> sprintf "%s = %s" (pascal f.Name) (ident f.Name))

        let binders = members |> List.map (fun (f, e) -> ident f.Name, e)

        sprintf
            "and private colMembers%s (__ov: CollectingDecoders) (j: JVal) (__fs: (string * JVal) list) : Result<%s%s, DecodeError list> =\n%s%s"
            r.Name
            r.Name
            (objParams msg r.Name [])
            (holderBinding "    " r.Name members)
            (collectAll "    " binders ("Ok " + recordLit r.Name assigns))

    /// A presence-keyed union's collecting reader: [[keyedDecoder]]'s walk, every defect collected.
    let keyedColDecoder
        (msg: Set<string>)
        (idl: Idl)
        (read: IdlField list -> Result<(IdlField * string) list, CodegenError>)
        (u: IdlUnion)
        (keyed: KeyedCase list)
        : Result<string, CodegenError> =
        u.Cases
        |> List.mapi (fun i c ->
            match keyed |> List.tryFind (fun k -> k.Case = c.Tag) with
            | None -> Ok ""
            | Some k ->
                match read c.Fields, synthBinders idl u k with
                | Error e, _
                | _, Error e -> Error e
                | Ok members, Ok synth ->
                    let binders =
                        (members |> List.map (fun (f, e) -> ident f.Name, e))
                        @ (synth |> List.map (fun (v, e) -> v, sprintf "cOne (%s)" e))

                    Ok(
                        sprintf
                            "    | Some %d ->\n%s%s"
                            i
                            (holderBinding "        " (u.Name + "." + c.Tag) members)
                            (collectAll "        " binders (sprintf "Ok(Some(%s))" (keyedCtor u c)))
                    ))
        |> sequenceR
        |> Result.map (fun arms ->
            sprintf
                "and private colMembers%s (__ov: CollectingDecoders) (j: JVal) (__fs: (string * JVal) list) : Result<%s%s option, DecodeError list> =\n    dPick __fs %s |> cOne |> Result.bind (fun __i ->\n    match __i with\n%s\n    | _ -> Ok None)"
                u.Name
                u.Name
                (objParams msg u.Name [])
                (pickRules keyed)
                (arms |> List.filter (fun a -> a <> "") |> String.concat "\n"))
