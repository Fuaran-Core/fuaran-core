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

/// The F# structural-layer DECODER printers, short-circuiting and collecting: the mirror of
/// [[FSharpEncode]], which [[FSharpCodec]] assembles beside it.
module internal FSharpDecode =

    // -----------------------------------------------------------------------
    // fuaran#672 — the structural DECODER leg.
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

    /// Phase 524 — the F# DECODER printer over the one slot traversal ([[SlotTraversal]]): each
    /// slot's `JVal -> Result<'T, DecodeError>`, the mirror of [[encPrinter]] arm for arm.
    let decPrinter: SlotPrinter =
        let backend = "the F# decoder emitter"

        { Backend = backend
          Str = "dStr"
          Int = "dInt"
          Bool = "dBool"
          Float = "dFloat"
          Enum = fun n -> "dec" + n
          Var = fun v -> "dec" + v
          Named = fun n -> "dec" + n
          Node = "decNode"
          Union = fun n args -> "(dec" + n + " " + String.concat " " (List.map snd args) + ")"
          List = sprintf "(dList %s)"
          Map = sprintf "(dMap %s)"
          // Phase 347 — the sentinel is checked (`dSentinel`), as the interpreter checks it.
          Closure = "(dSentinel \"<closure>\")"
          Opaque = "(dSentinel \"<opaque>\")"
          // Phase 689 — a `TFn` slot decodes to its declared placeholder. There is nothing on the
          // wire to rebuild a closure from, so the decoded tree is the storage shape and the
          // placeholder is what a host re-attaches over.
          //
          // Phase 460 — WHAT A PLACEHOLDER MAY NAME (contract). The placeholder is spliced
          // verbatim into the short-circuiting `dec*` decoder AND, under
          // `Derivation.SpecDecoders`, into its collecting `col*` twin (as a lifted leaf), so it
          // may reference only what both scopes bind:
          //   * the module's prelude and its generated `dec*` decoders (`decNode`, `dec<Union>`,
          //     `dec<Record>`, `dec<Enum>`);
          //   * inside a parameterised union, each type parameter's short-circuiting decoder,
          //     `dec<V>: JVal -> Result<'V, DecodeError>` — the short-circuiting decoder's
          //     parameter, and a local the collecting decoder binds under the same name.
          // `col<V>`, `__j`, `__fs`, `j` and every member binding are emitter internals, not
          // contract (some differ between the two decoders or between field positions); a
          // placeholder naming one is outside the contract.
          Fn =
            fun s ->
                sprintf "(fun (__j: JVal) -> dSentinel \"<closure>\" __j |> Result.map (fun () -> %s))" s.Placeholder
          // fuaran#676 — accept any JSON verbatim; a shape check would contradict the field's
          // contract.
          Json = "dJson"
          // The named host decode expression, verbatim (JVal -> Result<'host, string>). Phase 252 —
          // a declared wire form is checked FIRST (its type, then its format), so this host
          // refuses exactly what the interpreter and the TypeScript host refuse, whatever the
          // codec itself would admit. Phase 337 — the codec's own refusal is a SENTENCE (its
          // contract is unchanged), lifted by `dHosted` to `OutOfRange` at the slot.
          Hosted =
            fun h ->
                match hostedWireRefusal backend h with
                | Some e -> HostedSlot.Refused e
                | None ->
                    match h.Wire with
                    | None -> HostedSlot.Verbatim(sprintf "(fun (__j: JVal) -> %s)" (hostedRead h "__j"))
                    | Some w ->
                        let format =
                            match h.Format with
                            | Some f ->
                                sprintf
                                    " |> Result.bind (fun _ -> dFormat %s __j)"
                                    (SourceLit.fsString (HostedFormat.name f))
                            | None -> ""

                        HostedSlot.Through(
                            w,
                            fun wd ->
                                sprintf
                                    "(fun (__j: JVal) -> %s __j%s |> Result.bind (fun _ -> %s))"
                                    wd
                                    format
                                    (hostedRead h "__j")
                        )
          // Phase 639 (D251) — the wider data universe ([[widerDecodeHelpers]]). A set and a keyed
          // map are handed their element's (key's) ENCODER too: the canonical order is checked over
          // each decoded item's canonical encoding, as the interpreter checks it.
          Wider =
            fun t slot ->
                let enc (x: IdlType) = SlotTraversal.slotFn encPrinter.Slot x

                match t with
                | TInt64 -> Ok "dInt64"
                | TDecimal -> Ok "(dText \"decimal\" isDecimalText)"
                | TBytes -> Ok "dBytes"
                | TTimeOfDay -> Ok "(dText \"timeOfDay\" isTimeOfDayText)"
                | TDuration -> Ok "(dText \"duration\" isDurationText)"
                | TTuple cs -> cs |> List.map slot |> sequenceR |> Result.map (tupleCodec false)
                | TSet e ->
                    match enc e, slot e with
                    | Ok ee, Ok de -> Ok(sprintf "(dSet %s %s)" ee de)
                    | Error x, _
                    | _, Error x -> Error x
                | TKeyedMap(k, v) ->
                    match enc k, slot k, slot v with
                    | Ok ek, Ok dk, Ok dv -> Ok(sprintf "(dKeyedMap %s %s %s)" ek dk dv)
                    | Error x, _, _
                    | _, Error x, _
                    | _, _, Error x -> Error x
                | other -> Error(opVocabularySlot backend other) }

    /// Phase 524 — the F# printer of a member READ back out ([[MemberTraversal.decMember]]): a
    /// sentinel slot decodes to `()` or its `TFn` placeholder, a host-only member to its declared
    /// placeholder, an omit-at-default one to [[fsDefaultLit]]'s literal.
    let memberDecPrinter (idl: Idl) : MemberDecPrinter =
        { Slot = decPrinter
          Key = SourceLit.fsString
          // Phase 539 — a member declaring an alias or a presence condition reads its canonical view.
          Object = fun fields f -> memberView idl fields f |> Option.defaultValue "__fs"
          Req = sprintf "dReq %s %s %s"
          Opt = sprintf "dOpt %s %s %s"
          Def = sprintf "dDef %s %s %s (%s)"
          Placeholder =
            function
            | TFn s -> s.Placeholder
            | _ -> "()"
          HostOnlySentinel = sprintf "Ok (%s)"
          HostOnly = fun f -> hostOnlyLit f |> Result.map (sprintf "Ok (%s)")
          DefaultLit = fsDefaultLit idl
          // Phase 474 — an inline field read from the object its members sit on.
          Inline = fun o f -> inlineDecRead idl o f }

    /// Nest one `Result.bind` per field over `final`, then close the lot. F# has
    /// no applicative sugar for this, and the generated file is Fantomas-exempt,
    /// so the nesting is emitted explicitly rather than prettified.
    let bindChain (indent: string) (binders: (string * string) list) (final: string) (extraCloses: int) : string =
        let opens =
            binders
            |> List.map (fun (v, e) -> sprintf "%s%s |> Result.bind (fun %s ->" indent e v)

        let closes = String.replicate (List.length binders + extraCloses) ")"
        (opens @ [ indent + final + closes ]) |> String.concat "\n"

    /// [[bindChain]] for a case whose final is a declared case REFINE (fuaran#945): the last
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
        MemberTraversal.decMembers (memberDecPrinter idl) fs
        |> Result.map (List.map (fun (f, e) -> ident f.Name, e))

    /// An enum decoder (Phase 337): a string naming no case is `UnknownTag`, anything else
    /// `WrongKind` — the interpreter's split, and since Phase 520 the interpreter's detail, so the
    /// projected sentence is the interpreter's too.
    let enumDecoder (e: IdlEnum) =
        // Phase 539 — each declared alias decodes to its case, after the canonical arms.
        let arms =
            (e.Cases
             |> List.map (fun c -> sprintf "    | JStr %s -> Ok %s.%s" (SourceLit.fsString (e.WireOf c)) e.Name c))
            @ (e.Cases
               |> List.collect (fun c ->
                   e.AliasesOf c
                   |> List.map (fun a -> sprintf "    | JStr %s -> Ok %s.%s" (SourceLit.fsString a) e.Name c)))
            |> String.concat "\n"

        sprintf
            "let private dec%s (j: JVal) : Result<%s, DecodeError> =\n    match j with\n%s\n    | JStr __s -> dFail DecodeCode.UnknownTag %s (\"case '\" + __s + %s)\n    | __j -> Error(Decoder.wrongKind \"string\" __j)"
            e.Name
            e.Name
            arms
            (SourceLit.fsString (oneOf e.WireCases))
            (SourceLit.fsString ("' of enum '" + e.Name + "'"))

    /// A union decoder. Generic unions take one `decX` codec per type parameter,
    /// with the explicit type-parameter list [[unionEncoder]] needs for the same
    /// polymorphic-recursion reason (`Binding.Format.source` recurses at `float`).
    /// Phase 524 — the cases and the transparent fallthrough are [[UnionTraversal.decoder]]'s.
    let unionDecoder
        (docFn: string -> string -> string)
        (refines: Map<string, string>)
        (decl: Decl)
        (u: IdlUnion)
        : Result<string, CodegenError> =
        let msg, idl = decl.Msg, decl.Idl

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

        let arm (c: IdlUnionCase) (members: (IdlField * string) list) =
            // fuaran#945 — a declared refine replaces the plain `Ok(Case(…))` final with a
            // policy expression (field binder names in scope); the binder chain around it
            // is untouched, so a refine cannot change WHICH fields decode, only what is
            // accepted once they have.
            //
            // Phase 337 — a refine answers a SENTENCE, as it always has; `dRefine` binds the
            // case's last member and lifts the refine's refusal to `OutOfRange` at the case's
            // object (see [[bindChainRefined]]).
            let refine = refines.TryFind(u.Name + "." + c.Tag)
            let binders = members |> List.map (fun (f, e) -> ident f.Name, e)

            let final =
                match refine with
                | Some r -> r
                | None -> sprintf "Ok(%s)" (ctor c)

            let wire = SourceLit.fsString (u.WireTagOf c.Tag)

            let body =
                match c.Hosted with
                // Phase 550 — a case-level host codec reads the case object; a typed refusal keeps its
                // code and its path, which are the case's.
                | Some h ->
                    let pattern, payload = payloadBinders h

                    sprintf
                        "        | %s ->\n            %s |> Result.map (fun %s -> %s.%s%s)"
                        wire
                        (hostedRead h "j")
                        (if pattern = "" then "()" else payload)
                        u.Name
                        c.Tag
                        (if pattern = "" then "" else payload)
                | None when List.isEmpty c.Fields -> sprintf "        | %s -> Ok %s" wire (ctor c)
                | None ->
                    sprintf
                        "        | %s ->\n%s"
                        wire
                        (match refine with
                         | Some _ -> bindChainRefined "            " binders final
                         | None -> bindChain "            " binders final 0)

            docFn ("decarm:" + u.Name + "." + c.Tag) "        " + body

        // The declared transparent case is on the wire BARE, so it is recognised as a
        // value that is not an object. Every object goes to the tag dispatch, as the
        // interpreter's does (Phase 337: an object with no discriminator is `MissingField`
        // at it, never a payload; Phase 303 refuses a transparent case whose payload can be
        // an object, so no bare payload is lost).
        UnionTraversal.decoder
            { Read = MemberTraversal.decMembers (memberDecPrinter idl)
              Slot = decPrinter
              Arm = fun _ c members -> arm c members
              Bare =
                fun _ c f dfn ->
                    sprintf
                        "    | __bare ->\n        %s __bare |> Result.bind (fun %s -> Ok(%s))"
                        dfn
                        (ident f.Name)
                        (ctor c)
              NoBare = fun _ -> "    | __j -> Error(Decoder.wrongKind \"object\" __j)"
              Union =
                fun _ arms fall ->
                    let tagged =
                        sprintf
                            "    | JObj __fs ->\n        dTag __fs |> Result.bind (fun __t ->\n        match __t with\n%s\n        | __other -> dUnknown %s (%s + __other + \"'\"))"
                            (String.concat "\n" arms)
                            (SourceLit.fsString (oneOf (u.Cases |> List.map (fun c -> u.WireTagOf c.Tag))))
                            (SourceLit.fsString (idl.Wire.Discriminator + " '"))

                    sprintf
                        "and private dec%s%s%s (j: JVal) : Result<%s%s, DecodeError> =\n    match j with\n%s\n%s"
                        u.Name
                        declArgs
                        decArgs
                        u.Name
                        tyArgs
                        tagged
                        fall }
            decl
            u

    /// An object's decoder — a kind's spec record or a declared record: its members read in
    /// declaration order ([[fieldBinders]]), then the record built.
    let objectDecoder
        (decName: string)
        (typeName: string)
        (msg: Set<string>)
        (idl: Idl)
        (fields: IdlField list)
        : Result<string, CodegenError> =
        let assigns =
            fields |> List.map (fun f -> sprintf "%s = %s" (pascal f.Name) (ident f.Name))

        fieldBinders idl fields
        |> Result.map (fun binders ->
            sprintf
                "and private dec%s (j: JVal) : Result<%s%s, DecodeError> =\n    dObj j |> Result.bind (fun __fs ->\n%s"
                decName
                typeName
                (objParams msg typeName [])
                (bindChain "    " binders ("Ok " + recordLit typeName assigns) 1))

    let specDecoder (msg: Set<string>) (idl: Idl) (k: IdlKind) : Result<string, CodegenError> =
        objectDecoder (k.Tag + "Spec") (k.Tag + "Spec") msg idl k.Fields

    let recordDecoder (msg: Set<string>) (idl: Idl) (r: IdlRecord) : Result<string, CodegenError> =
        objectDecoder r.Name r.Name msg idl r.Fields

    // -----------------------------------------------------------------------
    // Phase 377 — the COLLECTING decoders and the public per-spec entries, emitted on request
    // (`Derivation.SpecDecoders`). Each `dec*` decoder above is a `Result.bind` chain and
    // reports ONE defect; its `col*` twin walks the same positions in the same order and answers
    // every defect as a `DecodeError list`. Mirroring the walk is what makes the twin a
    // conservative extension: its first defect is the short-circuiting decoder's defect, and on a
    // clean input both answer the same value — which `IdlSpecDecoderTests` holds.
    //
    // A LEAF — a scalar, an enum, a sentinel, a hosted slot, verbatim JSON — reports at most one
    // defect, so its generated collecting form is its short-circuiting decoder lifted (`cLift` /
    // `cOne`). Only an object, a list and a map have independent positions to collect over.
    // -----------------------------------------------------------------------

    /// Phase 524 — the F# COLLECTING decoder printer, derived from [[decPrinter]]
    /// ([[SlotTraversal.collecting]]): a leaf is the short-circuiting decoder lifted (`cLift`).
    ///
    /// Phase 541 — a node, a non-generic union and a record are read through the overridable
    /// decoders `__ov` (the emitted `CollectingDecoders` record, see [[collectingDecl]]), handed
    /// the same record, so a host's decoder at that type is the one applied at EVERY depth.
    ///
    /// Phase 562 (DECISIONS.md D230) — so are an enumeration and the four JSON scalars, each a field
    /// of the record whose generated value is the lifted leaf; and a generic union is read through
    /// its field's generic `Decode`, handed the record and its argument decoders, so one host decoder
    /// covers every instantiation. A sentinel, a hosted slot and verbatim JSON stay leaves: each is
    /// the vocabulary's or the host's own codec already.
    ///
    /// Phase 635 (DECISIONS.md D248) — each argument decoder is followed by the argument's witness
    /// ([[FSharpCollectingRecord.witnessOf]]), so the host's decoder can depend on the instantiation;
    /// `msg` names the message-carrying declarations, which no witness case names.
    let colPrinter (msg: Set<string>) : SlotPrinter =
        { SlotTraversal.collecting
              decPrinter
              (sprintf "(cLift %s)")
              { decPrinter with
                  Var = fun v -> "col" + v
                  Named = fun n -> "(__ov." + n + " __ov)"
                  Union =
                      fun n args ->
                          let passed =
                              args
                              |> List.collect (fun (t, dec) -> [ dec; FSharpCollectingRecord.witnessOf msg t ])

                          "(fun __j -> __ov."
                          + n
                          + ".Decode(__ov, "
                          + String.concat ", " passed
                          + ", __j))"
                  Node = "(__ov.Node __ov)"
                  List = sprintf "(cList %s)"
                  Map = sprintf "(cMap %s)" } with
            Str = "(__ov.String __ov)"
            Int = "(__ov.Int __ov)"
            Bool = "(__ov.Bool __ov)"
            Float = "(__ov.Float __ov)"
            Enum = fun n -> "(__ov." + n + " __ov)" }

    /// The F# collecting member reader: [[memberDecPrinter]]'s presence, read through
    /// [[colPrinter]].
    let colMemberPrinter (msg: Set<string>) (idl: Idl) : MemberDecPrinter =
        { memberDecPrinter idl with
            Slot = colPrinter msg
            // Phase 562 — a declared member is read beside its holder (`__holder`, bound by every
            // holder's decoder, see [[holderBinding]]) and the object `j`, so an absent one asks the
            // record's `Absent` first.
            Req = sprintf "cReqAt __ov __holder j %s %s %s"
            Opt = sprintf "cOptAt __ov __holder j %s %s %s"
            Def = sprintf "cDefAt __ov __holder j %s %s %s (%s)"
            Inline = fun o f -> inlineColRead idl o f }

    /// Phase 562 — the `__holder` a holder's member reads name to the record's `Absent`: the holder's
    /// F# name (`Owner`, `NoteSpec`, `Node`), or `Union.Case` for a union case. Bound only where a
    /// member is read.
    let holderBinding (indent: string) (holder: string) (members: (IdlField * string) list) : string =
        if List.isEmpty members then
            ""
        else
            sprintf "%slet __holder = %s\n" indent (SourceLit.fsString holder)

    /// Read every member (in declaration order — the order the defects are reported in), then
    /// build `final` when all of them decoded, else answer every member's defects, concatenated in
    /// that order, each member bound to its field name for `final`.
    let collectAll (indent: string) (binders: (string * string) list) (final: string) : string =
        match binders with
        | [] -> indent + final
        | _ ->
            let count = List.length binders
            let slot (i: int) = sprintf "__c%d" i

            [ yield! binders |> List.mapi (fun i (_, e) -> sprintf "%slet %s = %s" indent (slot i) e)
              yield sprintf "%smatch %s with" indent (List.init count slot |> String.concat ", ")
              yield sprintf "%s| %s ->" indent (binders |> List.map (fun (v, _) -> "Ok " + v) |> String.concat ", ")
              yield sprintf "%s    %s" indent final
              yield
                  sprintf
                      "%s| _ -> Error(List.concat [ %s ])"
                      indent
                      (List.init count (fun i -> "cErrs " + slot i) |> String.concat "; ") ]
            |> String.concat "\n"

    /// A refined case's collecting body (Phase 541): the members collected into one tuple, then
    /// the declared refine over them through `cRefine`. The refine sits at the column the
    /// short-circuiting decoder gives it (see [[bindChainRefined]]) — the first column of a `fun`
    /// body opened on the line above — because its own continuation lines are indented for that
    /// column. Every member is read through the collecting decoders, so a refined case is built
    /// from the overridable decoders' values exactly as an unrefined case is.
    let collectRefined (indent: string) (binders: (string * string) list) (refine: string) : string =
        let names = binders |> List.map fst |> String.concat ", "

        [ indent + "let __members ="
          collectAll (indent + "    ") binders (sprintf "Ok(%s)" names)
          sprintf "%s__members |> cRefine (fun (%s) ->" indent names
          indent + refine + ")" ]
        |> String.concat "\n"

    /// A union's collecting decoder: the discriminator, then the case's members collected. A case
    /// with a declared REFINE (fuaran#945) collects its members and, when every one decoded, applies
    /// the refine to them ([[collectRefined]]) — the refine is a sentence over the decoded members,
    /// so it adds at most one defect, only once they all decoded, and at the position the
    /// short-circuiting decoder reports it.
    let colUnionDecoder
        (refines: Map<string, string>)
        (decl: Decl)
        (read: IdlField list -> Result<(IdlField * string) list, CodegenError>)
        (u: IdlUnion)
        : Result<string, CodegenError> =
        let msg, idl = decl.Msg, decl.Idl

        // Phase 635 — each parameter's decoder, then its witness (D248).
        let colArgs =
            u.Params
            |> List.map (fun p ->
                sprintf
                    " (col%s: JVal -> Result<'%s, DecodeError list>) (inst%s: %s<'%s>)"
                    p
                    p
                    p
                    FSharpCollectingRecord.InstantiationType
                    p)
            |> String.concat ""

        let declArgs =
            if List.isEmpty u.Params then
                ""
            else
                "<" + (u.Params |> List.map (fun p -> "'" + p) |> String.concat ", ") + ">"

        let tyArgs = objParams msg u.Name u.Params

        // Phase 460 — each type parameter's SHORT-CIRCUITING decoder, bound under the name the
        // short-circuiting emitter gives its parameter (`dec<V>`), so a placeholder that names it
        // compiles in both decoders (the contract is stated at [[decPrinter]]'s `TFn` arm); the
        // refined case's twin below takes the same bindings.
        let decBindings =
            u.Params
            |> List.map (fun p -> sprintf "    let dec%s (__j: JVal) = cFirst (col%s __j)\n" p p)
            |> String.concat ""

        let ctor (c: IdlUnionCase) =
            match c.Fields with
            | [] -> sprintf "%s.%s" u.Name c.Tag
            | fs -> sprintf "%s.%s(%s)" u.Name c.Tag (fs |> List.map (fun f -> ident f.Name) |> String.concat ", ")

        let arm (c: IdlUnionCase) (members: (IdlField * string) list) =
            let wire = SourceLit.fsString (u.WireTagOf c.Tag)

            match c.Hosted with
            // Phase 550 — a case-level host codec: one read, lifted.
            | Some h ->
                let pattern, payload = payloadBinders h

                sprintf
                    "        | %s ->\n            cOne (%s) |> Result.map (fun %s -> %s.%s%s)"
                    wire
                    (hostedRead h "j")
                    (if pattern = "" then "()" else payload)
                    u.Name
                    c.Tag
                    (if pattern = "" then "" else payload)
            | None when List.isEmpty c.Fields -> sprintf "        | %s -> Ok %s" wire (ctor c)
            | None ->
                let binders = members |> List.map (fun (f, e) -> ident f.Name, e)

                let body =
                    match refines.TryFind(u.Name + "." + c.Tag) with
                    | Some r -> collectRefined "            " binders r
                    | None -> collectAll "            " binders (sprintf "Ok(%s)" (ctor c))

                sprintf "        | %s ->\n%s%s" wire (holderBinding "            " (u.Name + "." + c.Tag) members) body

        UnionTraversal.decoder
            { Read = read
              Slot = colPrinter msg
              Arm = fun _ c members -> arm c members
              Bare =
                fun _ c f cfn ->
                    sprintf
                        "    | __bare ->\n        %s __bare |> Result.bind (fun %s -> Ok(%s))"
                        cfn
                        (ident f.Name)
                        (ctor c)
              NoBare = fun _ -> "    | __j -> cOne (Error(Decoder.wrongKind \"object\" __j))"
              Union =
                fun _ arms fall ->
                    let tagged =
                        sprintf
                            "    | JObj __fs ->\n        dTag __fs |> cOne |> Result.bind (fun __t ->\n        match __t with\n%s\n        | __other -> cOne (dUnknown %s (%s + __other + \"'\")))"
                            (String.concat "\n" arms)
                            (SourceLit.fsString (oneOf (u.Cases |> List.map (fun c -> u.WireTagOf c.Tag))))
                            (SourceLit.fsString (idl.Wire.Discriminator + " '"))

                    sprintf
                        "and private col%s%s (__ov: CollectingDecoders)%s (j: JVal) : Result<%s%s, DecodeError list> =\n%s    match j with\n%s\n%s"
                        u.Name
                        declArgs
                        colArgs
                        u.Name
                        tyArgs
                        decBindings
                        tagged
                        fall }
            decl
            u

    /// An object's collecting decoder — a kind's spec record or a declared record — from its
    /// members as read.
    let colObjectDecoder
        (decName: string)
        (typeName: string)
        (msg: Set<string>)
        (fields: IdlField list)
        (members: (IdlField * string) list)
        : string =
        let assigns =
            fields |> List.map (fun f -> sprintf "%s = %s" (pascal f.Name) (ident f.Name))

        let binders = members |> List.map (fun (f, e) -> ident f.Name, e)

        sprintf
            "and private col%s (__ov: CollectingDecoders) (j: JVal) : Result<%s%s, DecodeError list> =\n%s    cObj j |> Result.bind (fun __fs ->\n%s)"
            decName
            typeName
            (objParams msg typeName [])
            (holderBinding "    " decName members)
            (collectAll "        " binders ("Ok " + recordLit typeName assigns))
