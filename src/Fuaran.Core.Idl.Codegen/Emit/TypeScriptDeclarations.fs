namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.Annotations
open Fuaran.Core.Idl.Emit.FSharpDefaults

/// The TypeScript backend's scaffold value emitter and its declarations (`TypeScript` until the Phase
/// 388 split along its banners).
module internal TypeScriptDeclarations =
    open TypeScriptCodec

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

        let mismatchOf (what: string) =
            CodegenError.UnsupportedConstruct(
                what,
                "GP5: the scaffold leg names what it cannot construct rather than emitting a value the encoder mis-writes",
                "author the value against the vocabulary's declaration (Encode.encode refuses the same value)"
            )

        let objectOf (pairs: (string * string) list) =
            "{ "
            + (pairs |> List.map (fun (k, e) -> k + ": " + e) |> String.concat ", ")
            + " }"

        let keyed (members: (IdlField * string) list) =
            members |> List.map (fun (f, e) -> SourceLit.tsKey f.Name, e)

        // Phase 524 — the TypeScript printer of an authored value ([[ValueTraversal]]).
        let printer: ValuePrinter =
            { Mismatch = mismatchOf
              Leaf =
                fun walk t v ->
                    match t, v with
                    | TStr, VStr s -> Some(Ok(SourceLit.tsString s))
                    | TInt, VInt i -> Some(Ok(string i))
                    | TBool, VBool b -> Some(Ok(if b then "true" else "false"))
                    | TFloat, VFloat f -> Some(Ok(invariantFloat f))
                    | TFloat, VInt i -> Some(Ok(string i))
                    | TMap vt, VMap entries ->
                        entries
                        |> List.map (fun (k, ev) -> walk.Value vt ev |> Result.map (fun e -> SourceLit.tsString k, e))
                        |> sequenceR
                        |> Result.map objectOf
                        |> Some
                    // A hosted slot that declares its wire form takes that type's decoded shape (a
                    // record's omit-at-default members filled, as for any record); the JSON is read
                    // through the interpreter's decoder, so a value outside the form is refused.
                    | THosted { Wire = Some w }, VJson j ->
                        match Decode.value idl w j with
                        | Ok wv -> Some(walk.Value w wv)
                        | Error m ->
                            Some(Error(mismatchOf (sprintf "a hosted value outside its declared wire form (%s)" m)))
                    | (TJson | THosted _), VJson j -> Some(Ok(Canonical.write Rendering.CrossHost j))
                    // The encoder never reads a sentinel slot; a PRESENT stand-in keeps an optional
                    // one's presence test honest (see [[typescriptValueWith]]).
                    | (TClosure | TFn _ | TOpaque), (VClosure | VOpaque) -> Some(Ok "(() => undefined)")
                    | _ -> None
              // A case IS its wire string on this host.
              Enum =
                fun _ v ->
                    match v with
                    | VEnum wire -> Ok(SourceLit.tsString wire)
                    | _ -> Error(mismatchOf "an enum value that is not a wire string")
              List = fun items -> "[" + String.concat ", " items + "]"
              // Every omit-at-default member is PRESENT, filled from its declared default; an
              // absent optional and a host-only member (never on the wire, and `undefined` in the
              // decoder's shape) are absent.
              Member =
                fun _ mv walk ->
                    match mv with
                    | MemberValue.Carries v
                    | MemberValue.Optional v
                    | MemberValue.IdentityDefault v -> walk v |> Result.map Some
                    | _ -> Ok None
              Record = fun _ members -> objectOf (keyed members)
              Union = fun u c members -> objectOf ((disc, SourceLit.tsString (u.WireTagOf c.Tag)) :: keyed members)
              Node =
                fun id k kindMembers envMembers ->
                    let tagged = (disc, SourceLit.tsString k.Tag)

                    match idl.Wire.NodeEnvelope with
                    | NodeEnvelopeShape.NestedKind ->
                        objectOf (
                            (("id", SourceLit.tsString id) :: keyed envMembers)
                            @ [ "kind", objectOf (tagged :: keyed kindMembers) ]
                        )
                    | NodeEnvelopeShape.FlatKind ->
                        objectOf (
                            (tagged :: ("id", SourceLit.tsString id) :: keyed envMembers)
                            @ keyed kindMembers
                        )
              Unmatched = fun t _ -> mismatchOf (sprintf "a value that does not match IDL type %A" t) }

        ValueTraversal.value printer idl t v

    /// The name the declaration file gives a decode refusal (Phase 348).
    let internal refusalTypeName = "DecodeRefusal"

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

    /// Phase 542 — a union's, record's or enum's OWN annotations as a JSDoc block above its
    /// `export type`: the authored doc first, then `@deprecated` / `@since` and the in-process
    /// note. A JSDoc tag is TRUE here, where it is not on a generated function (the argument
    /// at `tsAnnotationLines`): the declaration it sits on IS the vocabulary type. Each line is
    /// split and sanitised by [[SourceLit.tsCommentLines]], and a `*/` in authored text is
    /// broken so it cannot end the block. Empty for a type that says nothing, so an
    /// unannotated vocabulary's declarations are byte-identical.
    let internal tsTypeDocBlock (a: Annotations) : string =
        let text (s: string option) =
            s |> Option.map SourceLit.tsCommentLines |> Option.defaultValue []

        let lines =
            text a.Doc
            @ [ match a.Deprecated with
                | Some d ->
                    "@deprecated"
                    + (match said d.Replacement with
                       | Some r -> " Use `" + r + "` instead."
                       | None -> "")
                    + (match said d.Message with
                       | Some m -> " " + m
                       | None -> "")
                | None -> ()

                if a.InProcessOnly then
                    "In-process only: no wire projection, so a value of this type is lost across a wire boundary."

                match said a.Since with
                | Some v -> "@since " + v
                | None -> () ]
            |> List.collect SourceLit.tsCommentLines

        match lines with
        | [] -> ""
        | ls ->
            "/**\n"
            + (ls
               |> List.map (fun l -> if l = "" then " *" else " * " + l.Replace("*/", "*\\/"))
               |> String.concat "\n")
            + "\n */\n"

    /// The declared TypeScript type of an IDL type, as the declaration file spells it: a declared
    /// enum, record or union by its own name, a node as `Node`, and a sentinel, JSON or undeclared
    /// hosted slot as `unknown`. Shared by [[typescriptDeclarations]] and the derived members'
    /// declarations (Phase 381), so a member's type is spelled exactly as the file's own types are.
    let rec internal tsDeclType (t: IdlType) : Result<string, CodegenError> =
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
    ///
    /// Phase 505 — this is the file UNSTAMPED, so a caller that adds to it (the derivations)
    /// stamps the text it actually emits; [[typescriptDeclarations]] is it under its [[Stamp]].
    let typescriptDeclarationsBody (decl: Decl) : Result<string, CodegenError> =
        // Phase 511 — the declaration graph is the resolution's.
        let idl, kinds, enums, unions, records =
            decl.Idl, decl.Kinds, decl.Enums, decl.Unions, decl.Records

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
                    tsTypeDocBlock e.Annotations
                    + "export type "
                    + e.Name
                    + " = "
                    + orNever (e.WireCases |> List.map SourceLit.tsString)
                    + ";"
                ))

        let recordDecls =
            records
            |> List.map (fun r ->
                objectType [] r.Fields
                |> Result.map (fun o -> tsTypeDocBlock r.Annotations + "export type " + r.Name + " = " + o + ";"))

        let unionDecls =
            unions
            |> List.map (fun u ->
                u.Cases
                |> List.map (fun c -> objectType [ disc + ": " + SourceLit.tsString (u.WireTagOf c.Tag) ] c.Fields)
                |> sequenceR
                |> Result.map (fun cases ->
                    tsTypeDocBlock u.Annotations
                    + "export type "
                    + u.Name
                    + generic u.Params
                    + " = "
                    + orNever cases
                    + ";"))

        let specDecls =
            kinds
            |> List.map (fun k ->
                objectType [ disc + ": " + SourceLit.tsString k.Tag ] k.Fields
                |> Result.map (fun o -> "export type " + k.Tag + "Spec = " + o + ";"))

        let envelope = objectType [ "id: string" ] decl.Envelope.Members
        let specs = kinds |> List.map (fun k -> k.Tag + "Spec")

        let nodeDecl =
            envelope
            |> Result.map (fun env ->
                match decl.Envelope.Kind with
                | KindPlacement.NestedUnder key ->
                    "export type NodeKind = "
                    + orNever specs
                    + ";\n"
                    + "export type Node = "
                    + env.Substring(0, env.Length - 2)
                    + "; "
                    + key
                    + ": NodeKind };"
                | KindPlacement.Merged ->
                    "export type Node = "
                    + orNever (specs |> List.map (fun s -> "(" + s + " & " + env + ")"))
                    + ";")

        // Phase 348 — the names this file declares beside the vocabulary's own: a vocabulary type
        // spelled like one of them would be declared twice, which a consumer's compiler refuses
        // far from its cause. Refused here, naming it.
        let fixedNames =
            [ "Node"; refusalTypeName ]
            @ (match decl.Envelope.Kind with
               | KindPlacement.NestedUnder _ -> [ "NodeKind" ]
               | KindPlacement.Merged -> [])
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
            [ enumDecls
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

    /// Phase 505 — a TypeScript declaration file's [[Stamp]], over its normalised text.
    let tsDeclarationsStamp (idl: Idl) (body: string) : string =
        Stamp.generated (CommentStyle.Line "//") "TypeScript declarations" [ idl ] body

    /// The declarations [[typescriptDeclarationsBody]] emits, under their stamp.
    let typescriptDeclarations (decl: Decl) : Result<string, CodegenError> =
        typescriptDeclarationsBody decl |> Result.map (tsDeclarationsStamp decl.Idl)
