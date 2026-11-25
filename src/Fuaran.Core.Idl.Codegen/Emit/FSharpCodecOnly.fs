namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.FSharpCanonicalForm
open Fuaran.Core.Idl.Emit.FSharpEncode
open Fuaran.Core.Idl.Emit.FSharpDecode
open Fuaran.Core.Idl.Emit.FSharpInline
open Fuaran.Core.Idl.Emit.FSharpCodec

/// Phase 550 (DECISIONS.md D198) — the CODEC-ONLY F# module: encoders and decoders for named
/// declarations whose types the HOST declares, and no type declaration of its own. Each reached
/// declaration is an abbreviation of its host type (`type InvokeError = Fuaran.Core.InvokeError`),
/// so the encoder and decoder groups are exactly the ones the structural module prints — every case
/// is matched and built through the abbreviation, which F# resolves whether or not the host type
/// requires qualified access — and the module adds a public entry per root and a STRICT reader
/// beside each decoder.
///
/// Phase 722 (DECISIONS.md D285) — an inline declaration the roots reach is laid out on the object
/// holding it by the emitter the structural module prints it with ([[FSharpCodec.unionEncoderOf]]
/// and its three siblings), and the strict readers walk its members on that object, as the
/// interpreter's strict walk does. An inline declaration is no ROOT: it has no JSON value of its own.
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
        | _, Some r, _ -> Some(TRecord(r.Name, []))
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
                      Keyed = false
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
        | TRecord(n, _) -> "strict" + n
        | TUnion(n, []) -> "strict" + n
        | TUnion(n, args) -> "(strict" + n + " " + (args |> List.map walker |> String.concat " ") + ")"
        | TVar v -> "strict" + v
        | TList inner -> "(sList " + walker inner + ")"
        | TMap inner -> "(sMap " + walker inner + ")"
        // Phase 639 — a tuple's components and a keyed map's values may hold objects (a set's
        // elements and a map's keys are orderable, never one). Printed inline, so the strict helpers
        // every other module carries stay as they are.
        | TTuple cs when cs |> List.exists (fun c -> walker c <> "sNone") ->
            let n = List.length cs
            let binders = List.init n (sprintf "__s%d") |> String.concat "; "

            let walks =
                cs
                |> List.mapi (fun i c -> sprintf "%s __s%d |> dUnder (PathSegment.Index %d)" (walker c) i i)
                |> String.concat " |> Result.bind (fun () -> "

            sprintf
                "(fun (__j: JVal) -> match __j with | JArr [ %s ] -> %s%s | _ -> Ok())"
                binders
                walks
                (String.replicate (n - 1) ")")
        | TKeyedMap(_, v) when walker v <> "sNone" ->
            sprintf
                "(sList (fun (__e: JVal) -> match __e with | JArr [ _; __v ] -> %s __v |> dUnder (PathSegment.Index 1) | _ -> Ok()))"
                (walker v)
        | _ -> "sNone"

    /// The walks of an object's fields after its members check, bound in declared order.
    ///
    /// Phase 722 (D285) — an inline declaration's members are the object's own, so its fields are
    /// walked on the same object ([[Decode.valueStrictDetailed]]'s `strictFields`): an inline record's
    /// each, a presence-keyed union's those of the case `dPick` reads. A reading that refuses is left
    /// to the decoder that follows the check, which refuses it in its own words.
    let rec private fieldWalks (idl: Idl) (fields: IdlField list) : string =
        fields
        |> List.filter (fun f -> f.Opt <> HostOnly)
        |> List.map (fun f ->
            match Decl.inlineSlot idl f.Type with
            | Some(InlineSlot.Record r) -> fieldWalks idl r.Fields
            | Some(InlineSlot.Keyed(u, keyed)) ->
                let arms =
                    keyed
                    |> List.mapi (fun i k ->
                        u.Cases
                        |> List.tryFind (fun c -> c.Tag = k.Case)
                        |> Option.map (fun c -> i, fieldWalks idl c.Fields))
                    |> List.choose id
                    |> List.filter (fun (_, walks) -> walks <> "")
                    |> List.map (fun (i, walks) -> sprintf " | Ok(Some %d) -> Ok()%s" i walks)

                if List.isEmpty arms then
                    ""
                else
                    sprintf
                        " |> Result.bind (fun () -> match dPick __fs %s with%s | _ -> Ok())"
                        (pickRules keyed)
                        (String.concat "" arms)
            | None when walker f.Type <> "sNone" ->
                sprintf
                    " |> Result.bind (fun () -> sMember %s %s %s)"
                    (SourceLit.fsString f.Name)
                    (memberView idl fields f |> Option.defaultValue "__fs")
                    (walker f.Type)
            | None -> "")
        |> String.concat ""

    // D129 — it reaches `Fuaran.Core.Idl`'s internal lowering, so no caller may inline it.
    [<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
    let private rosterLit (idl: Idl) (extra: string list) (fields: IdlField list) : string =
        "[ "
        + (WireForm.roster idl extra fields
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
            (rosterLit idl [] r.Fields)
            (fieldWalks idl r.Fields)

    let private unionWalker (idl: Idl) (u: IdlUnion) : string =
        let disc = idl.Wire.Discriminator

        let arms =
            u.Cases
            |> List.map (fun c ->
                sprintf
                    "        | Some(_, JStr %s) -> Decoder.members %s j%s"
                    (SourceLit.fsString (u.WireTagOf c.Tag))
                    (rosterLit idl [ disc ] c.Fields)
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

    /// The entries of one root: public, except `encode<Name>` for a root `internalWriters` names,
    /// which is emitted `internal` (Phase 685) — its readers stay public.
    let private entries (decl: Decl) (internalWriters: string list) (name: string) : string =
        let msg = decl.Msg

        let writer =
            if List.contains name internalWriters then
                "let internal encode"
            else
                "let encode"

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
              sprintf "%s%s%s (v: %s) : JVal = enc%s%s v" writer name encParams ty name (codecArgs "enc" u.Params)
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
              sprintf "%s%s (v: %s) : JVal = enc%s v" writer name ty name
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
              sprintf "%s%s (v: %s) : JVal = enc%s v" writer name name name
              sprintf "/// `%s` read back; a refusal is the typed `DecodeError`." name
              sprintf "let decode%s (j: JVal) : Result<%s, DecodeError> = dec%s j" name name name
              sprintf "/// `decode%s` under the strict-members reading (an enum has no members)." name
              sprintf "let decode%sStrict (j: JVal) : Result<%s, DecodeError> = dec%s j" name name name ]
            |> String.concat "\n"

    /// The module's own doc comment (Phase 551): a codec-only module is published, so its module type
    /// is documented like every public member: which host types it carries codecs for (its roots, in
    /// request order), where they come from, and that it is regenerated, not edited.
    let private moduleDoc (request: CodecOnly) (internalWriters: string list) : string =
        let roots = request.Roots |> List.map (fun r -> "`" + r + "`") |> String.concat ", "

        let writers =
            internalWriters |> List.map (fun r -> "`encode" + r + "`") |> String.concat ", "

        "/// Generated codecs for "
        + roots
        + ", derived from their IDL declaration: per root\n"
        + "/// `encode<Name>`, `decode<Name>` and `decode<Name>Strict`. Regenerate it from its inputs; do not edit it."
        + (if List.isEmpty internalWriters then
               ""
           else
               "\n/// Internal, its readers public: " + writers + ".")

    /// The codec-only module, before end-of-line normalisation.
    let private moduleText
        (sup: GenSupport)
        (request: CodecOnly)
        (internalWriters: string list)
        (moduleName: string)
        (idl: Idl)
        =
        let roots = request.Roots |> List.map (fun n -> n, rootType idl n)

        // The resolution of the roots, asked only once every root is a declared type.
        let resolved () =
            resolveRoots idl (roots |> List.map (fun (n, t) -> n, Option.get t))

        match roots |> List.tryFind (snd >> Option.isNone) with
        | Some(n, _) ->
            refuse
                (sprintf "a codec-only root '%s'" n)
                "a root names a union, record or enum the vocabulary declares"
                "name a declared union, record or enum"
        | None when internalWriters |> List.exists (fun n -> not (List.contains n request.Roots)) ->
            refuse
                (sprintf
                    "an internal writer for '%s', which is not a root"
                    (internalWriters |> List.find (fun n -> not (List.contains n request.Roots))))
                "only a root has a public writer to keep internal"
                "name a root of the request"
        // Phase 722 (D285) — the one refusal an inline declaration keeps here, and it is the wire's:
        // a root's entries write and read ONE JSON value, and an inline declaration has none.
        | None when
            roots
            |> List.exists (fun (_, t) -> t |> Option.bind (Decl.inlineSlot idl) |> Option.isSome)
            ->
            refuse
                (sprintf
                    "a codec-only root '%s', which is an inline declaration"
                    (roots
                     |> List.find (fun (_, t) -> t |> Option.bind (Decl.inlineSlot idl) |> Option.isSome)
                     |> fst))
                "a root's entries write and read one JSON value, and an inline declaration has none of its own: its members are keys of the object that holds it (D220.1)"
                "name the record or union that holds it as a root"
        | None when not sup.KindProjections.IsEmpty || sup.TypeSplice.IsSome ->
            refuse
                "a kind projection or a type splice in a codec-only module"
                "a codec-only module declares no type: the host declares them all"
                "drop the type-declaring support, or generate the structural module"
        // Phase 722 — the roots reach an inline declaration the declaration check refuses: the one
        // question every backend asks before it lays one out ([[Decl.layoutFault]]).
        | None when (Decl.layoutFault (resolved ())).IsSome -> Decl.laidOut (resolved ()) |> Result.map (fun _ -> "")
        | None ->
            let decl = resolved ()

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
                    (decl.Unions |> List.map (unionEncoderOf typedName doc decl))
                    @ (decl.Records |> List.map (recordEncoderOf decl.Msg idl))
                    @ (sup.EncodeSplice |> Option.toList |> List.map Ok)
                    |> sequenceR

                let decoders =
                    (decl.Unions |> List.map (unionDecoderOf doc sup.CaseRefines decl))
                    @ (decl.Records |> List.map (recordDecoderOf decl.Msg idl))
                    @ (sup.DecodeSplice |> Option.toList |> List.map Ok)
                    |> sequenceR

                match encoders, decoders with
                | Error e, _
                | _, Error e -> Error e
                | Ok encs, Ok decs ->
                    // An inline declaration has no object, so no walker of its own: its holder's
                    // walker reads its members ([[fieldWalks]]).
                    let walkers =
                        (decl.Unions |> List.filter (Decl.isKeyed >> not) |> List.map (unionWalker idl))
                        @ (decl.Records
                           |> List.filter (fun r -> not r.Inline)
                           |> List.map (recordWalker idl))

                    [ [ sprintf "%s\nmodule %s\n\nopen Fuaran.Core" (moduleDoc request internalWriters) moduleName ]
                      [ "// Phase 550 — the host declares every type; each is named here by abbreviation."
                        + "\n"
                        + ((decl.Enums |> List.map (fun e -> abbreviation e.Name []))
                           @ (decl.Unions |> List.map (fun u -> abbreviation u.Name u.Params))
                           @ (decl.Records |> List.map (fun r -> abbreviation r.Name []))
                           |> String.concat "\n") ]
                      decl.Enums |> List.map enumEncoder
                      [ encodeHelpers idl ]
                      (if Decl.reachesWider idl then [ widerEncodeHelpers ] else [])
                      (if List.isEmpty encs then [] else [ recGroup encs ])
                      (sup.AccessorSplice |> Option.toList)
                      [ decodeHelpers
                            idl.Wire.Discriminator
                            (decl.Hosted || declaresHostedCase decl)
                            (not sup.CaseRefines.IsEmpty) ]
                      (if Decl.reachesWider idl then [ widerDecodeHelpers ] else [])
                      (if decl.HostedFormat then [ fsFormatHelper ] else [])
                      (if declaresCanonicalFormAnywhere idl then
                           [ canonicalFormHelpers ]
                       else
                           [])
                      (if decl.Unions |> List.exists Decl.isKeyed then
                           [ keyedHelpers ]
                       else
                           [])
                      decl.Enums |> List.map enumDecoder
                      (if List.isEmpty decs then [] else [ recGroup decs ])
                      [ strictHelpers ]
                      (if List.isEmpty walkers then [] else [ recGroup walkers ])
                      request.Roots |> List.map (entries decl internalWriters) ]
                    |> List.concat
                    |> String.concat "\n\n"
                    |> Ok

    /// The codec-only module (see the module's header) with each root `internalWriters` names
    /// emitting its writer `internal` (Phase 685): LF-terminated and stamped, as the structural
    /// module is.
    let fsharpCodecModuleWith
        (sup: GenSupport)
        (request: CodecOnly)
        (internalWriters: string list)
        (moduleName: string)
        (idl: Idl)
        : Result<string, CodegenError> =
        moduleText sup request internalWriters moduleName idl
        |> Result.map (
            normalizeEol
            >> Stamp.generated (CommentStyle.Line "//") "F# codec layer" [ idl ]
        )

    /// The codec-only module with every entry public.
    let fsharpCodecModule
        (sup: GenSupport)
        (request: CodecOnly)
        (moduleName: string)
        (idl: Idl)
        : Result<string, CodegenError> =
        fsharpCodecModuleWith sup request [] moduleName idl
