namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.FSharpCanonicalForm
open Fuaran.Core.Idl.Emit.FSharpCodec

/// Phase 550 (DECISIONS.md D198) — the CODEC-ONLY F# module: encoders and decoders for named
/// declarations whose types the HOST declares, and no type declaration of its own. Each reached
/// declaration is an abbreviation of its host type (`type InvokeError = Fuaran.Core.InvokeError`),
/// so the encoder and decoder groups are exactly the ones the structural module prints — every case
/// is matched and built through the abbreviation, which F# resolves whether or not the host type
/// requires qualified access — and the module adds a public entry per root and a STRICT reader
/// beside each decoder.
module internal FSharpCodecOnly =

    let private refuse (construct: string) (principle: string) (alternative: string) : Result<'a, CodegenError> =
        Error(CodegenError.UnsupportedConstruct(construct, principle, alternative))

    /// The tag of the synthetic kind the roots are resolved through: the one reach walk
    /// ([[Decl.resolve]]) starts at kinds, so the roots ride a kind that is dropped again at once.
    [<Literal>]
    let private rootKind = "__codecOnlyRoots"

    /// The declared type of a root named `name` — a union applied to its own parameters.
    let private rootType (idl: Idl) (name: string) : IdlType option =
        match CodegenLookup.tryUnion idl name, CodegenLookup.tryRecord idl name, CodegenLookup.tryEnum idl name with
        | Some u, _, _ -> Some(TUnion(u.Name, u.Params |> List.map TVar))
        | _, Some r, _ -> Some(TRecord r.Name)
        | _, _, Some e -> Some(TEnum e.Name)
        | _ -> None

    /// The resolution of what the roots reach, with no kinds.
    let private resolveRoots (idl: Idl) (roots: (string * IdlType) list) : Decl =
        let carrier: IdlKind =
            { Tag = rootKind
              Category = ""
              Fields =
                roots
                |> List.map (fun (n, t) ->
                    { Name = n
                      Type = t
                      Opt = Required
                      Aliases = []
                      Presence = None
                      Annotations = Annotations.Empty })
              Annotations = Annotations.Empty }

        let decl =
            Decl.resolve
                { idl with
                    Kinds = [ carrier ]
                    NodeFields = [] }
                Map.empty
                [ rootKind ]

        { decl with Idl = idl; Kinds = [] }

    /// A codec-only module holds no node: a type reaching one needs the node layer this mode does not
    /// print.
    let rec private nodeIn (t: IdlType) : bool =
        match t with
        | TNode
        | TKind
        | TOp -> true
        | TList inner
        | TMap inner -> nodeIn inner
        | TUnion(_, args) -> args |> List.exists nodeIn
        | THosted h -> h.Wire |> Option.exists nodeIn
        | _ -> false

    // -----------------------------------------------------------------------
    // The strict walkers: one `JVal -> Result<unit, DecodeError>` per reached union and record,
    // checking each object's members against [[WireForm.roster]] and then each field's value in
    // declared order — the interpreter's [[Decode.valueStrictDetailed]] walk, printed.
    // -----------------------------------------------------------------------

    /// The strict walker of a value of `t`.
    let rec private walker (t: IdlType) : string =
        match t with
        | TRecord n -> "strict" + n
        | TUnion(n, []) -> "strict" + n
        | TUnion(n, args) -> "(strict" + n + " " + (args |> List.map walker |> String.concat " ") + ")"
        | TVar v -> "strict" + v
        | TList inner -> "(sList " + walker inner + ")"
        | TMap inner -> "(sMap " + walker inner + ")"
        | _ -> "sNone"

    /// The walks of an object's fields after its members check, bound in declared order.
    let private fieldWalks (idl: Idl) (fields: IdlField list) : string =
        fields
        |> List.filter (fun f -> f.Opt <> HostOnly && walker f.Type <> "sNone")
        |> List.map (fun f ->
            sprintf
                " |> Result.bind (fun () -> sMember %s %s %s)"
                (SourceLit.fsString f.Name)
                (memberView idl fields f |> Option.defaultValue "__fs")
                (walker f.Type))
        |> String.concat ""

    // D129 — it reaches `Fuaran.Core.Idl`'s internal lowering, so no caller may inline it.
    [<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
    let private rosterLit (extra: string list) (fields: IdlField list) : string =
        "[ "
        + (WireForm.roster extra fields
           |> List.map SourceLit.fsString
           |> String.concat "; ")
        + " ]"

    let private walkerParams (ps: string list) : string =
        ps
        |> List.map (fun p -> sprintf " (strict%s: JVal -> Result<unit, DecodeError>)" p)
        |> String.concat ""

    let private recordWalker (idl: Idl) (r: IdlRecord) : string =
        sprintf
            "and private strict%s (j: JVal) : Result<unit, DecodeError> =\n    match j with\n    | JObj __fs -> Decoder.members %s j%s\n    | _ -> Ok()"
            r.Name
            (rosterLit [] r.Fields)
            (fieldWalks idl r.Fields)

    let private unionWalker (idl: Idl) (u: IdlUnion) : string =
        let disc = idl.Wire.Discriminator

        let arms =
            u.Cases
            |> List.map (fun c ->
                sprintf
                    "        | Some(_, JStr %s) -> Decoder.members %s j%s"
                    (SourceLit.fsString (u.WireTagOf c.Tag))
                    (rosterLit [ disc ] c.Fields)
                    (fieldWalks idl c.Fields))
            |> String.concat "\n"

        let bare =
            match
                TransparentUnion.tag idl.Harden u
                |> Option.bind (fun t -> u.Cases |> List.tryFind (fun c -> c.Tag = t))
            with
            | Some { Fields = [ single ] } -> sprintf "    | __bare -> %s __bare" (walker single.Type)
            | _ -> "    | _ -> Ok()"

        sprintf
            "and private strict%s%s (j: JVal) : Result<unit, DecodeError> =\n    match j with\n    | JObj __fs ->\n        match __fs |> List.tryFind (fun (k, _) -> k = %s) with\n%s\n        | _ -> Ok()\n%s"
            u.Name
            (walkerParams u.Params)
            (SourceLit.fsString disc)
            arms
            bare

    let private strictHelpers =
        """// Phase 550 — the STRICT-MEMBERS reading: a members check over the document before the decode,
// every declared object held to the members its declaration names (`Decoder.members`, so an
// undeclared one is `UndeclaredMember` at it). A slot with no declared shape inside is not entered.
let private sNone (_: JVal) : Result<unit, DecodeError> = Ok()

let private sList (w: JVal -> Result<unit, DecodeError>) (j: JVal) : Result<unit, DecodeError> =
    match j with
    | JArr xs ->
        xs
        |> List.indexed
        |> List.fold (fun acc (i, x) -> acc |> Result.bind (fun () -> w x |> dUnder (PathSegment.Index i))) (Ok())
    | _ -> Ok()

let private sMap (w: JVal -> Result<unit, DecodeError>) (j: JVal) : Result<unit, DecodeError> =
    match j with
    | JObj fs -> fs |> List.fold (fun acc (k, v) -> acc |> Result.bind (fun () -> w v |> dUnder (PathSegment.Key k))) (Ok())
    | _ -> Ok()

let private sMember (name: string) (fs: (string * JVal) list) (w: JVal -> Result<unit, DecodeError>) : Result<unit, DecodeError> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> w v |> dUnder (PathSegment.Key name)
    | None -> Ok()"""

    /// `let rec` for the first member of an `and`-joined group.
    let private recGroup (members: string list) : string =
        match members with
        | [] -> ""
        | first :: rest ->
            let opened =
                if first.StartsWith "and " then
                    "let rec " + first.Substring 4
                else
                    first

            String.concat "\n\n" (opened :: rest)

    let private codecArgs (prefix: string) (ps: string list) : string =
        ps |> List.map (fun p -> " " + prefix + p) |> String.concat ""

    /// The public entries of one root.
    let private entries (decl: Decl) (name: string) : string =
        let msg = decl.Msg

        match Decl.tryUnion decl name, Decl.tryRecord decl name with
        | Some u, _ ->
            let ty = u.Name + declParams msg u.Name u.Params

            let encParams =
                u.Params
                |> List.map (fun p -> sprintf " (enc%s: '%s -> JVal)" p p)
                |> String.concat ""

            let decParams =
                u.Params
                |> List.map (fun p -> sprintf " (dec%s: JVal -> Result<'%s, DecodeError>)" p p)
                |> String.concat ""

            let noWalks = u.Params |> List.map (fun _ -> " sNone") |> String.concat ""

            [ sprintf "/// `%s` as a JSON value." name
              sprintf "let encode%s%s (v: %s) : JVal = enc%s%s v" name encParams ty name (codecArgs "enc" u.Params)
              sprintf "/// `%s` read back; a refusal is the typed `DecodeError`." name
              sprintf
                  "let decode%s%s (j: JVal) : Result<%s, DecodeError> = dec%s%s j"
                  name
                  decParams
                  ty
                  name
                  (codecArgs "dec" u.Params)
              sprintf
                  "/// `decode%s` under the strict-members reading: an undeclared member is `UndeclaredMember`\n/// at it. A type parameter's payload is its decoder's to police."
                  name
              sprintf
                  "let decode%sStrict%s (j: JVal) : Result<%s, DecodeError> =\n    strict%s%s j |> Result.bind (fun () -> dec%s%s j)"
                  name
                  decParams
                  ty
                  name
                  noWalks
                  name
                  (codecArgs "dec" u.Params) ]
            |> String.concat "\n"
        | _, Some r ->
            let ty = r.Name + declParams msg r.Name []

            [ sprintf "/// `%s` as a JSON value." name
              sprintf "let encode%s (v: %s) : JVal = enc%s v" name ty name
              sprintf "/// `%s` read back; a refusal is the typed `DecodeError`." name
              sprintf "let decode%s (j: JVal) : Result<%s, DecodeError> = dec%s j" name ty name
              sprintf "/// `decode%s` under the strict-members reading." name
              sprintf
                  "let decode%sStrict (j: JVal) : Result<%s, DecodeError> =\n    strict%s j |> Result.bind (fun () -> dec%s j)"
                  name
                  ty
                  name
                  name ]
            |> String.concat "\n"
        | _ ->
            [ sprintf "/// `%s` as a JSON value." name
              sprintf "let encode%s (v: %s) : JVal = enc%s v" name name name
              sprintf "/// `%s` read back; a refusal is the typed `DecodeError`." name
              sprintf "let decode%s (j: JVal) : Result<%s, DecodeError> = dec%s j" name name name
              sprintf "/// `decode%s` under the strict-members reading (an enum has no members)." name
              sprintf "let decode%sStrict (j: JVal) : Result<%s, DecodeError> = dec%s j" name name name ]
            |> String.concat "\n"

    /// The module's own doc comment (Phase 551): a codec-only module is published, so its module type
    /// is documented like every public member: which host types it carries codecs for (its roots, in
    /// request order), where they come from, and that it is regenerated, not edited.
    let private moduleDoc (request: CodecOnly) : string =
        let roots = request.Roots |> List.map (fun r -> "`" + r + "`") |> String.concat ", "

        "/// Generated codecs for "
        + roots
        + ", derived from their IDL declaration: per root\n"
        + "/// `encode<Name>`, `decode<Name>` and `decode<Name>Strict`. Regenerate it from its inputs; do not edit it."

    /// The codec-only module, before end-of-line normalisation.
    let private moduleText (sup: GenSupport) (request: CodecOnly) (moduleName: string) (idl: Idl) =
        let roots = request.Roots |> List.map (fun n -> n, rootType idl n)

        match roots |> List.tryFind (snd >> Option.isNone) with
        | Some(n, _) ->
            refuse
                (sprintf "a codec-only root '%s'" n)
                "a root names a union, record or enum the vocabulary declares"
                "name a declared union, record or enum"
        | None when not sup.KindProjections.IsEmpty || sup.TypeSplice.IsSome ->
            refuse
                "a kind projection or a type splice in a codec-only module"
                "a codec-only module declares no type: the host declares them all"
                "drop the type-declaring support, or generate the structural module"
        | None ->
            let decl = resolveRoots idl (roots |> List.map (fun (n, t) -> n, Option.get t))

            let names =
                (decl.Enums |> List.map _.Name)
                @ (decl.Unions |> List.map _.Name)
                @ (decl.Records |> List.map _.Name)

            let fieldTypes =
                (decl.Unions |> List.collect (fun u -> u.Cases |> List.collect _.Fields))
                @ (decl.Records |> List.collect _.Fields)

            match
                names |> List.tryFind (fun n -> not (request.HostTypes.ContainsKey n)),
                fieldTypes |> List.tryFind (fun f -> nodeIn f.Type)
            with
            | Some n, _ ->
                refuse
                    (sprintf "a codec-only module reaching '%s' with no host type" n)
                    "every declaration a codec-only module reaches is the host's, so each names its host type"
                    (sprintf "add '%s' to the request's host types" n)
            | None, Some f ->
                refuse
                    (sprintf "a codec-only module reaching a node through field '%s'" f.Name)
                    "a codec-only module prints no node layer"
                    "generate the structural module for a vocabulary that holds nodes"
            | None, None ->
                let typedName =
                    if idl.Wire.Discriminator = "$type" then
                        "Canonical.typed"
                    else
                        "typedTag"

                let doc (path: string) (indent: string) =
                    match sup.Docs.TryFind path with
                    | Some lines -> (lines |> List.map (fun l -> indent + l) |> String.concat "\n") + "\n"
                    | None -> ""

                let abbreviation (name: string) (ps: string list) =
                    let args = declParams decl.Msg name ps
                    sprintf "type %s%s = %s%s" name args request.HostTypes[name] args

                let encoders =
                    (decl.Unions |> List.map (unionEncoder typedName doc decl))
                    @ (decl.Records |> List.map (recordEncoder decl.Msg idl))
                    @ (sup.EncodeSplice |> Option.toList |> List.map Ok)
                    |> sequenceR

                let decoders =
                    (decl.Unions |> List.map (unionDecoder doc sup.CaseRefines decl))
                    @ (decl.Records |> List.map (recordDecoder decl.Msg idl))
                    @ (sup.DecodeSplice |> Option.toList |> List.map Ok)
                    |> sequenceR

                match encoders, decoders with
                | Error e, _
                | _, Error e -> Error e
                | Ok encs, Ok decs ->
                    let walkers =
                        (decl.Unions |> List.map (unionWalker idl))
                        @ (decl.Records |> List.map (recordWalker idl))

                    [ [ sprintf "%s\nmodule %s\n\nopen Fuaran.Core" (moduleDoc request) moduleName ]
                      [ "// Phase 550 — the host declares every type; each is named here by abbreviation."
                        + "\n"
                        + ((decl.Enums |> List.map (fun e -> abbreviation e.Name []))
                           @ (decl.Unions |> List.map (fun u -> abbreviation u.Name u.Params))
                           @ (decl.Records |> List.map (fun r -> abbreviation r.Name []))
                           |> String.concat "\n") ]
                      decl.Enums |> List.map enumEncoder
                      [ encodeHelpers idl ]
                      (if List.isEmpty encs then [] else [ recGroup encs ])
                      (sup.AccessorSplice |> Option.toList)
                      [ decodeHelpers
                            idl.Wire.Discriminator
                            (decl.Hosted || declaresHostedCase decl)
                            (not sup.CaseRefines.IsEmpty) ]
                      (if decl.HostedFormat then [ fsFormatHelper ] else [])
                      (if declaresCanonicalFormAnywhere idl then
                           [ canonicalFormHelpers ]
                       else
                           [])
                      decl.Enums |> List.map enumDecoder
                      (if List.isEmpty decs then [] else [ recGroup decs ])
                      [ strictHelpers ]
                      (if List.isEmpty walkers then [] else [ recGroup walkers ])
                      request.Roots |> List.map (entries decl) ]
                    |> List.concat
                    |> String.concat "\n\n"
                    |> Ok

    /// The codec-only module (see the module's header): LF-terminated and stamped, as the structural
    /// module is.
    let fsharpCodecModule
        (sup: GenSupport)
        (request: CodecOnly)
        (moduleName: string)
        (idl: Idl)
        : Result<string, CodegenError> =
        moduleText sup request moduleName idl
        |> Result.map (
            normalizeEol
            >> Stamp.generated (CommentStyle.Line "//") "F# codec layer" [ idl ]
        )
