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
open Fuaran.Core.Idl.Emit.FSharpInline

/// The F# structural-layer CODEC: the encoder and decoder groups, the witness, the validator
/// scaffold, and the module that assembles them around the type group.
module internal FSharpCodec =

    /// The resolution the codec groups print their ordinary encoders and decoders over: the
    /// reached declarations less the inline ones, which the inline printers above print.
    let private ordinary (decl: Decl) : Decl =
        { decl with
            Records = decl.Records |> List.filter (fun r -> not r.Inline)
            Unions = decl.Unions |> List.filter (Decl.isKeyed >> not) }

    /// Phase 377 — the collecting prelude, the collecting decoder group (mirroring the decoder
    /// group: node kind, node, unions, records, specs) and the public entries: per kind
    /// `decode<Tag>Spec` (short-circuiting) and `decode<Tag>SpecAll` (collecting) over the kind's
    /// object, and per node `decodeNodeJson` / `decodeNodeJsonAll` over a parsed value and
    /// `decodeNodeAll` over text.
    ///
    /// Phase 541 — the OVERRIDABLE decoders. The group reads every node, non-generic union,
    /// record and (unprojected) kind spec through one record of decoders, `CollectingDecoders`,
    /// with a field per type, each taking the record itself (open recursion: no recursive value,
    /// no reflection, Fable-clean). `collectingDecoders` is the record of the generated decoders;
    /// the `decode*All` entries read through it, unchanged, and their `decode*AllWith` twins take
    /// a host's record, so a host decoder at a type is applied at every depth that type occurs.
    /// The record itself is [[FSharpCollectingRecord.emit]]'s.
    let collectingDecl (sup: GenSupport) (decl: Decl) : Result<string, CodegenError> =
        let idl, msg, kinds, unions, records =
            decl.Idl, decl.Msg, decl.Kinds, decl.Unions, decl.Records

        let nodeTy = objParams msg "Node" []
        let projected (k: IdlKind) = sup.KindProjections.ContainsKey k.Tag

        let specTy (k: IdlKind) =
            k.Tag + "Spec" + objParams msg (k.Tag + "Spec") []

        let colNodeKindDecl =
            let arms =
                kinds
                |> List.map (fun k ->
                    if projected k then
                        sprintf
                            "    | %s -> col%sSpec j |> Result.map NodeKind.%s"
                            (SourceLit.fsString k.Tag)
                            k.Tag
                            k.Tag
                    else
                        sprintf
                            "    | %s -> __ov.%sSpec __ov j |> Result.map NodeKind.%s"
                            (SourceLit.fsString k.Tag)
                            k.Tag
                            k.Tag)
                |> String.concat "\n"

            sprintf
                "let rec private colNodeKind (__ov: CollectingDecoders) (j: JVal) : Result<NodeKind%s, DecodeError list> =\n"
                (objParams msg "NodeKind" [])
            + "    cObj j |> Result.bind (fun __fs ->\n"
            + "    dTag __fs |> cOne |> Result.bind (fun __t ->\n"
            + "    match __t with\n"
            + arms
            + sprintf
                "\n    | __other -> cOne (dUnknown %s (%s + __other + \"'\"))))"
                (SourceLit.fsString (oneOf (kinds |> List.map _.Tag)))
                (SourceLit.fsString (idl.Wire.Discriminator + " '"))

        let colNodeDecl (envelope: (IdlField * string) list) =
            let envelopeAssigns =
                decl.Envelope.Members
                |> List.map (fun f -> sprintf "; %s = %s" (pascal f.Name) (ident f.Name))
                |> String.concat ""

            let final = sprintf "Ok { Id = id; Kind = kind%s }" envelopeAssigns
            let kindBinder = envelopeKindBinder decl.Envelope "cReq" "(colNodeKind __ov)"

            let binders =
                [ "id", "cOne (dReq \"id\" __fs dStr)"; "kind", kindBinder ]
                @ (envelope |> List.map (fun (f, e) -> ident f.Name, e))

            sprintf
                "and private colNode (__ov: CollectingDecoders) (j: JVal) : Result<Node%s, DecodeError list> =\n"
                nodeTy
            + holderBinding "    " "Node" envelope
            + "    cObj j |> Result.bind (fun __fs ->\n"
            + collectAll "        " binders final
            + ")"

        let colRead =
            MemberTraversal.colMembers (colMemberPrinter msg idl) (colMemberPrinter msg idl) id

        let inlineCols =
            (unions
             |> List.choose (fun u ->
                 match u.Discrimination with
                 | UnionDiscrimination.ByPresence keyed -> Some(keyedColDecoder msg idl colRead u keyed)
                 | UnionDiscrimination.Tagged -> None))
            @ (records
               |> List.filter _.Inline
               |> List.map (fun r -> colRead r.Fields |> Result.map (inlineRecordColDecoder msg r)))
            |> sequenceR

        let groupR =
            CollectingTraversal.group
                // Phase 562 — a leaf member is read by the collecting reader too, so a scalar's and an
                // enumeration's go through their slots, and every declared member's absence through
                // `Absent`; nothing is lifted.
                { Dec = colMemberPrinter msg idl
                  Col = colMemberPrinter msg idl
                  Lift = id
                  Union = fun read u -> colUnionDecoder sup.CaseRefines decl read u
                  NodeKind = colNodeKindDecl
                  Node = colNodeDecl
                  Record = fun r members -> colObjectDecoder r.Name r.Name r.Params msg r.Fields members
                  // A projected kind's decoder is the projection's short-circuiting one, lifted.
                  ReadsSpec = fun k -> not (projected k)
                  Spec =
                    fun k members ->
                        match members with
                        | Some ms -> colObjectDecoder (k.Tag + "Spec") (k.Tag + "Spec") [] msg k.Fields ms
                        | None -> sprintf "and private col%sSpec (j: JVal) =\n    cOne (dec%sSpec j)" k.Tag k.Tag }
                (ordinary decl)
            |> Result.bind (fun group -> inlineCols |> Result.map (fun cols -> String.concat "\n\n" (group :: cols)))

        let entries =
            [ yield
                  "/// Phase 377 — the public per-spec decoders. `decode<Tag>Spec` reads one kind's object and\n/// answers its FIRST defect; `decode<Tag>SpecAll` reads the same object and answers EVERY defect,\n/// in the order stated above the collecting prelude. A consumer delegates one kind at a time.\n/// Phase 541 — `decode<Tag>SpecAllWith` reads it through a host's `CollectingDecoders`, so the\n/// host's decoder at a type applies at every depth; `decode<Tag>SpecAll` is its no-override case."
              for k in kinds do
                  if projected k then
                      yield sprintf "let decode%sSpec (j: JVal) = dec%sSpec j" k.Tag k.Tag
                      yield sprintf "let decode%sSpecAll (j: JVal) = col%sSpec j" k.Tag k.Tag
                  else
                      let ty = specTy k

                      yield sprintf "let decode%sSpec (j: JVal) : Result<%s, DecodeError> = dec%sSpec j" k.Tag ty k.Tag

                      yield
                          sprintf
                              "let decode%sSpecAll (j: JVal) : Result<%s, DecodeError list> = col%sSpec collectingDecoders j"
                              k.Tag
                              ty
                              k.Tag

                      yield
                          sprintf
                              "let decode%sSpecAllWith (decoders: CollectingDecoders) (j: JVal) : Result<%s, DecodeError list> =\n    decoders.%sSpec decoders j"
                              k.Tag
                              ty
                              k.Tag
              yield
                  sprintf
                      "/// The whole node over a parsed value: the first defect, or every defect.\nlet decodeNodeJson (j: JVal) : Result<Node%s, DecodeError> = decNode j"
                      nodeTy
              yield
                  sprintf
                      "let decodeNodeJsonAll (j: JVal) : Result<Node%s, DecodeError list> = colNode collectingDecoders j"
                      nodeTy
              yield
                  sprintf
                      "let decodeNodeJsonAllWith (decoders: CollectingDecoders) (j: JVal) : Result<Node%s, DecodeError list> =\n    decoders.Node decoders j"
                      nodeTy
              yield
                  sprintf
                      "/// `decodeNode`'s collecting twin: a parser refusal is the one defect, else every defect.\nlet decodeNodeAll (s: string) : Result<Node%s, DecodeError list> =\n    Decoder.parse ReadOptions.strict s |> cOne |> Result.bind (colNode collectingDecoders)"
                      nodeTy
              yield
                  sprintf
                      "let decodeNodeAllWith (decoders: CollectingDecoders) (s: string) : Result<Node%s, DecodeError list> =\n    Decoder.parse ReadOptions.strict s |> cOne |> Result.bind (decoders.Node decoders)"
                      nodeTy ]
            |> String.concat "\n"

        let helpers =
            if sup.CaseRefines.IsEmpty then
                [ Template.text "fs-collecting.fs.txt" ]
            else
                [ Template.text "fs-collecting.fs.txt"
                  Template.text "fs-collecting-refine.fs.txt" ]

        // Phase 562 — the record's type comes first: the prelude's member readers ask its `Absent`.
        FSharpCollectingRecord.emit sup decl
        |> Result.bind (fun record ->
            groupR
            |> Result.map (fun group ->
                [ record.TypeDecl ]
                @ helpers
                @ (if record.Defaults = "" then [] else [ record.Defaults ])
                @ [ group; record.Generated; entries ]
                |> String.concat "\n\n"))

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
        [ yield Template.text "fs-decode-helpers.fs.txt"
          if hosted then
              yield Template.text "fs-decode-hosted.fs.txt"
          if refines then
              yield Template.text "fs-decode-refine.fs.txt" ]
        |> String.concat "\n\n"
        |> fun text -> text.Replace("__DISC__", SourceLit.fsString disc)

    /// Phase 252 — the generated F# check of a hosted slot's declared FORMAT, emitted only
    /// when the vocabulary declares one ([[declaresHostedFormat]]). It restates
    /// [[HostedFormat.admits]] because the generated module carries no reference to this
    /// package; a test holds the two to the same answers.
    let fsFormatHelper = (Template.text "fs-format-helper.fs.txt")


    /// Phase 505 — the F# module's [[Stamp]], over its normalised text.
    let private fsharpStamp (idl: Idl) (body: string) : string =
        Stamp.generated (CommentStyle.Line "//") "F# structural layer" [ idl ] body

    // Phase 722 (DECISIONS.md D285) — which emitter prints a declaration, decided ONCE for the
    // structural and the codec-only module: an inline declaration (a presence-keyed union, an inline
    // record) writes and reads member pieces of its holder, any other declaration its own object.
    let unionEncoderOf typedName doc (decl: Decl) (u: IdlUnion) =
        match u.Discrimination with
        | UnionDiscrimination.ByPresence keyed -> keyedEncoder decl.Msg decl.Idl u keyed
        | UnionDiscrimination.Tagged -> unionEncoder typedName doc decl u

    let recordEncoderOf msg idl (r: IdlRecord) =
        (if r.Inline then inlineRecordEncoder else recordEncoder) msg idl r

    let unionDecoderOf doc refines (decl: Decl) (u: IdlUnion) =
        match u.Discrimination with
        | UnionDiscrimination.ByPresence keyed -> keyedDecoder decl.Msg decl.Idl u keyed
        | UnionDiscrimination.Tagged -> unionDecoder doc refines decl u

    let recordDecoderOf msg idl (r: IdlRecord) =
        (if r.Inline then inlineRecordDecoder else recordDecoder) msg idl r

    /// The F# module emission before [[normalizeEol]] — see `fsharpModuleWith`, the public
    /// entry point, which is this composed with it.
    let fsharpModuleUnnormalised
        (sup: GenSupport)
        (requests: Derivation list)
        (moduleName: string)
        (decl: Decl)
        : Result<string, CodegenError> =
        let decoders = List.contains Derivation.SpecDecoders requests
        // fuaran#945 — declared-doc lookup: a block of comment lines (markers included,
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

        // Phase 511 — the declaration graph is the resolution's; nothing below re-walks the IDL.
        let idl, kinds, enums, unions, records =
            decl.Idl, decl.Kinds, decl.Enums, decl.Unions, decl.Records

        // Phase 108 — the emitted discriminated-object builder: `Canonical.typed` on a
        // default-shape IDL (byte-identical to every pre-declarable emission), the
        // module-local `typedTag` when the vocabulary declares another key.
        let typedName =
            if idl.Wire.Discriminator = "$type" then
                "Canonical.typed"
            else
                "typedTag"

        // Phase 689 — which declarations are generic in `'Msg`. Empty unless the IDL
        // uses `TFn`, so an IDL that has not adopted it generates exactly as before.
        let msg = decl.Msg

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
        let typeGroup = FSharpTypes.typeGroup doc docOpt sup.TypeSplice decl

        let encNodeDecl =
            let arms =
                kinds
                |> List.map (fun k -> sprintf "    | NodeKind.%s s -> enc%sSpec s" k.Tag k.Tag)
                |> String.concat "\n"

            // fuaran#690 — the envelope rides the same `List.choose id` presence
            // machinery every spec field uses, with `s.` rebound to `n.`, so
            // omit-on-absence / omit-at-default behave identically on a node field
            // and on a kind field. No envelope ⇒ the original two-key literal.
            //
            // Phase 109 — the FLAT shape merges the id (and any envelope) into the
            // kind's own object; `Canonical.write Rendering.CrossHost` sorts keys, so splice order is
            // irrelevant. Nested emission is byte-identical to the pre-declarable form.
            let envelopePieces () =
                decl.Envelope.Members
                |> List.map (specPieceOf idl "n" decl.Envelope.Members)
                |> concatR "; "

            let body =
                match decl.Envelope.Kind, decl.Envelope.Members with
                | KindPlacement.NestedUnder key, [] ->
                    Ok(sprintf "\n    JObj [ \"id\", JStr n.Id; %s, kind ]" (SourceLit.fsString key))
                | KindPlacement.NestedUnder key, _ ->
                    envelopePieces ()
                    |> Result.map (
                        sprintf
                            "\n    JObj([ Some(\"id\", JStr n.Id); Some(%s, kind); %s ] |> List.choose id)"
                            (SourceLit.fsString key)
                    )
                // Discriminator first, then id, kind fields, envelope — the
                // Phase 111 declared order (irrelevant under Sorted rendering,
                // where `Canonical.write Rendering.CrossHost` re-sorts; normative under Declared).
                | KindPlacement.Merged, [] ->
                    Ok
                        "\n    match kind with\n    | JObj(__d :: __kf) -> JObj(__d :: (\"id\", JStr n.Id) :: __kf)\n    | __other -> __other"
                | KindPlacement.Merged, _ ->
                    envelopePieces ()
                    |> Result.map (
                        sprintf
                            "\n    match kind with\n    | JObj(__d :: __kf) -> JObj(__d :: (\"id\", JStr n.Id) :: (__kf @ ([ %s ] |> List.choose id)))\n    | __other -> __other"
                    )

            // fuaran#694 — the kind dispatch is its own function (was inline in
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
            (encNodeDecl :: (unions |> List.map (unionEncoderOf typedName doc decl))
             @ (records |> List.map (recordEncoderOf msg idl))
             @ (kinds
                |> List.map (fun k ->
                    // fuaran#945 — a projected kind's encoder is the projection's, verbatim.
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
                if decl.Obsolete then
                    "\n#nowarn \"44\" // this layer implements every declared member, including deprecated ones"
                else
                    ""

            // Phase 505 — the stamp above `module` is written by the public entry points, over the
            // normalised text, so it hashes exactly the bytes it heads.
            sprintf "module %s%s\n\nopen Fuaran.Core" moduleName nowarn

        // fuaran#672: the decoder's mutually-recursive group, mirroring `recGroup`.
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
                    "\n    | __other -> dUnknown %s (%s + __other + \"'\")))"
                    (SourceLit.fsString (oneOf (kinds |> List.map _.Tag)))
                    (SourceLit.fsString (idl.Wire.Discriminator + " '"))

            let decNodeDecl =
                // fuaran#690 — the envelope binds through the same `bindChain` /
                // `decField` machinery a spec record uses, so its presence rules are
                // the encoder's inverse by construction rather than by hand.
                //
                // Phase 109 — in the FLAT shape the node object IS the kind body, so
                // `decNodeKind` dispatches on the same object the id binds from
                // (spec decoders read only declared names, so the discriminator and
                // the id are tolerated as the extra keys they are). The nested
                // binder is byte-identical to the pre-declarable emission.
                let envelopeAssigns =
                    decl.Envelope.Members
                    |> List.map (fun f -> sprintf "; %s = %s" (pascal f.Name) (ident f.Name))
                    |> String.concat ""

                let final = sprintf "Ok { Id = id; Kind = kind%s }" envelopeAssigns

                let kindBinder = envelopeKindBinder decl.Envelope "dReq" "decNodeKind"

                fieldBinders idl decl.Envelope.Members
                |> Result.map (fun envelopeBinders ->
                    let binders =
                        [ "id", "dReq \"id\" __fs dStr"; "kind", kindBinder ] @ envelopeBinders

                    sprintf "and private decNode (j: JVal) : Result<Node%s, DecodeError> =\n" (objParams msg "Node" [])
                    + "    dObj j |> Result.bind (fun __fs ->\n"
                    + bindChain "    " binders final 1)

            (Ok decNodeKindDecl
             :: decNodeDecl
             :: (unions |> List.map (unionDecoderOf doc sup.CaseRefines decl))
             @ (records |> List.map (recordDecoderOf msg idl))
             @ (kinds
                |> List.map (fun k ->
                    // fuaran#945 — a projected kind's decoder is the projection's, verbatim.
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
            |> List.exists (fun r -> r = Derivation.StructuralAccess || r = Derivation.KeyedPositions)

        let witnessAndDerived =
            Decl.projectionsDeclared decl
            |> Result.bind (fun () -> hostedCaseDerivations decl requests)
            |> Result.bind (fun () -> FSharpWitness.witnessDecl publicAccess decl.Projections msg kinds)
            |> Result.bind (fun w ->
                FSharpDerive.derivedDecl decl sup.KindTables requests
                |> Result.map (fun ds -> w, ds))
            // Phase 377 — the collecting decoders and the public per-spec entries, on request, after
            // every other member (they compose with the decoder group and nothing reads them).
            |> Result.bind (fun (w, ds) ->
                if decoders then
                    collectingDecl sup decl |> Result.map (fun c -> w, ds @ [ c ])
                else
                    Ok(w, ds))

        // Phase 124 — the encoder and decoder groups joined the two declarations that could
        // already refuse. Every leg that renders a declared default now reports the same typed
        // `UnsupportedDefault` here, so there is no remaining path on which an unrenderable
        // default produces a module instead of a refusal.
        match witnessAndDerived, defaultsDecl decl, recGroup, decGroup, typeGroup with
        | Ok(witness, derived), Ok defaults, Ok recGroup, Ok decGroup, Ok typeGroup ->
            [ [ header ]
              enums |> List.map (rqaEnum doc)
              [ typeGroup ]
              enums |> List.map (fun e -> doc ("enc:" + e.Name) "" + enumEncoder e)
              [ encodeHelpers idl ]
              (if Decl.reachesWider idl then [ widerEncodeHelpers ] else [])
              [ recGroup ]
              [ sprintf
                    "let encodeNode (n: Node%s) : string = %s (encNode n)"
                    nodeArgs
                    (match idl.Wire.KeyOrder with
                     | KeyOrder.Sorted -> "Canonical.write Rendering.CrossHost"
                     | KeyOrder.Declared -> "Canonical.write Rendering.Declared") ]
              // fuaran#694 — JVal-level accessors for host codecs that splice
              // generated encodings into a larger canonical document (the
              // tier's TreeOp codec re-points at these when the hand-written
              // node encoder is deleted): the node and its kind, for every
              // vocabulary. An accessor for any other declaration is the
              // vocabulary's to declare through the accessor splice — Phase 524
              // removed the two this generic emitter wrote by one vocabulary's
              // record NAMES (C93).
              [ sprintf
                    "/// JVal-level accessors (Phase 694) — for host codecs that splice generated\n/// encodings into a larger canonical document (e.g. a TreeOp codec).\nlet encodeNodeJson (n: Node%s) : JVal = encNode n"
                    nodeArgs
                sprintf "let encodeNodeKindJson (k: NodeKind%s) : JVal = encNodeKind k" nodeArgs ]
              (sup.AccessorSplice |> Option.toList)
              decodeHelpers
                  idl.Wire.Discriminator
                  (decl.Hosted || declaresHostedCase decl)
                  (not sup.CaseRefines.IsEmpty)
              :: (if decl.HostedFormat then [ fsFormatHelper ] else [])
              @ (if declaresCanonicalFormAnywhere idl then
                     [ canonicalFormHelpers ]
                 else
                     [])
              @ (if unions |> List.exists Decl.isKeyed then
                     [ keyedHelpers ]
                 else
                     [])
              @ (if Decl.reachesWider idl then [ widerDecodeHelpers ] else [])
              enums |> List.map (fun e -> doc ("dec:" + e.Name) "" + enumDecoder e)
              [ decGroup ]
              // Phase 337 — the parser's refusal through `Decoder.parse` (under `ReadOptions.strict`
              // since Phase 519): `InvalidJson` at the root, or `LimitExceeded` at the path of the
              // container opened past the nesting cap.
              [ sprintf
                    "/// Structural decode. The policy layer (diagnostics, §16 lenient-accept,\n/// the reject set) composes ABOVE this — see the Phase 672 note in the generator.\n/// A refusal is Core's `DecodeError` (Phase 337): the code and path the IDL interpreter\n/// reports for the same document, and this layer's sentence (`DecodeError.describe`).\nlet decodeNode (s: string) : Result<Node%s, DecodeError> =\n    Decoder.parse ReadOptions.strict s |> Result.bind decNode"
                    (objParams msg "Node" []) ]
              [ witness ]
              [ FSharpWitness.validatorDecl msg ]
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
    /// returns canonical wire via the shared `Canonical.write Rendering.CrossHost`. Also emits a `nodeWitness`
    /// (`NodeWitness<Node, string>`) so the generated layer plugs into `Fuaran.Core.Tree`, a
    /// `runValidator` scaffold wiring `Fuaran.Core.Validator` over the generated `Node`, and
    /// `mk<Kind>` smart constructors applying the IDL-declared field defaults. `Error` on a
    /// construct the generator cannot yet emit (`CodegenError` — GP4/GP5), reported at generation
    /// time rather than as an exception.
    ///
    /// The emitted text is LF-terminated whatever the generator was built from and whatever the
    /// declared support carries — see [[normalizeEol]].
    let fsharpModuleWith (sup: GenSupport) (moduleName: string) (decl: Decl) : Result<string, CodegenError> =
        fsharpModuleUnnormalised sup [] moduleName decl
        |> Result.map (normalizeEol >> fsharpStamp decl.Idl)

    /// Phase 374 — `fsharpModuleWith` plus the requested structural derivations
    /// ([[FSharpDerive]]), and (Phase 377) the collecting decoders and public per-spec entries
    /// under `Derivation.SpecDecoders`. No request IS `fsharpModuleWith`, byte for byte.
    let fsharpModuleDerived
        (sup: GenSupport)
        (requests: Derivation list)
        (moduleName: string)
        (decl: Decl)
        : Result<string, CodegenError> =
        fsharpModuleUnnormalised sup requests moduleName decl
        |> Result.map (normalizeEol >> fsharpStamp decl.Idl)

    /// Phase 511 (S12) — THE admission of a derivation request, for every backend that derives:
    /// the projections' declared records name declarations the module emits, the PUBLIC node
    /// witness exists when structural access is requested (it refuses a kind mixing a node list
    /// with other children), and every requested derivation is derivable — each over the
    /// backend's own resolution. The F# module performs exactly these steps on its way to the
    /// texts; the TypeScript backend calls this, over a resolution with no host projections
    /// (a projection is F# source, so the TypeScript host declares none), and so admits exactly
    /// what the F# path admits for the vocabulary's wire surface.
    let admitDerivations (decl: Decl) (requests: Derivation list) : Result<unit, CodegenError> =
        let publicAccess =
            requests
            |> List.exists (fun r -> r = Derivation.StructuralAccess || r = Derivation.KeyedPositions)

        Decl.projectionsDeclared decl
        |> Result.bind (fun () -> hostedCaseDerivations decl requests)
        |> Result.bind (fun () ->
            if publicAccess then
                FSharpWitness.witnessDecl true decl.Projections decl.Msg decl.Kinds
                |> Result.map ignore
            else
                Ok())
        |> Result.bind (fun () -> FSharpDerive.derivedDecl decl Map.empty requests |> Result.map ignore)
