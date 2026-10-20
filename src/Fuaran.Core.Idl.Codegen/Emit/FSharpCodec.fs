namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Idl.Emit.Core
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

    /// Phase 524 — the F# ENCODER printer over the one slot traversal ([[SlotTraversal]]): `JVal`
    /// constructors and `enc<Name>` functions.
    let encPrinter: AppliedPrinter =
        let closure = "(fun _ -> JStr \"<closure>\")"
        let opaque = "(fun _ -> JStr \"<opaque>\")"

        { Slot =
            { Backend = "the F# encoder emitter"
              Str = "JStr"
              Int = "JInt"
              Bool = "JBool"
              // NOT the bare `JFloat` constructor: a non-finite double has no JSON number spelling
              // and rides as a quoted sentinel string (`encodeHelpers`). `TInt` keeps its
              // constructor — WIRE_FORMAT §7 truncates at a float slot.
              Float = "encFloat"
              Enum = fun n -> "enc" + n
              Var = fun v -> "enc" + v
              Named = fun n -> "enc" + n
              Node = "encNode"
              Union = fun n args -> "(enc" + n + " " + String.concat " " args + ")"
              List = sprintf "(fun __xs -> JArr(List.map %s __xs))"
              Map = sprintf "(fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, %s v)))"
              // A closure/opaque codec ignores its argument and emits the fixed sentinel.
              Closure = closure
              Fn = fun _ -> closure
              Opaque = opaque
              // Phase 676 — the canonical renderer already sorts keys, escapes and lays floats
              // out, so identity inherits all three.
              Json = "id"
              // The named host encode expression, verbatim ('host -> JVal). Canonicality is
              // inherited: the host codec builds a JVal that renders through the same renderer.
              Hosted = fun h -> HostedSlot.Verbatim h.Encode }
          Apply = sprintf "%s %s"
          ListApplied = sprintf "JArr(List.map %s %s)"
          NodeApplied = sprintf "encNode %s"
          UnionApplied = fun n args var -> "(enc" + n + " " + String.concat " " args + ") " + var
          ClosureApplied = fun _ -> "JStr \"<closure>\""
          OpaqueApplied = fun _ -> "JStr \"<opaque>\"" }

    /// Point-free encoder *function* for a type (`'a -> JVal`) — used where an encoder
    /// must be passed (a generic union's type-parameter codec).
    let encFn (t: IdlType) : Result<string, CodegenError> = SlotTraversal.slotFn encPrinter.Slot t

    /// The applied JVal expression for a value of `t` bound to `var`.
    let encApplied (var: string) (t: IdlType) : Result<string, CodegenError> =
        SlotTraversal.encApplied encPrinter var t

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
// Building the `JStr` HERE rather than leaving `Canonical.write Rendering.CrossHost` to spell a non-finite
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
        // helper (its call sites reference `Canonical.typed`), so its module is
        // byte-identical to every pre-declarable emission.
        if idl.Wire.Discriminator = "$type" then
            base'
        else
            base'
            + sprintf
                "\n\n// Phase 108 — `Canonical.typed` under this vocabulary's DECLARED discriminator key.\nlet private typedTag (tag: string) (fields: (string * JVal) list) : JVal =\n    JObj((%s, JStr tag) :: fields)"
                (SourceLit.fsString idl.Wire.Discriminator)


    /// Phase 524 — the F# printer of a member WRITTEN under its presence
    /// ([[MemberEncTraversal.encMember]]): a `(string * JVal) option` piece for `List.choose id`.
    /// `src` is the expression the member's value is read from — `s.Field` in a spec or record
    /// encoder, `n.Field` on the node envelope (Phase 690), the positional binding in a union case.
    let memberEncPrinter (idl: Idl) : MemberEncPrinter =
        { Applied = encPrinter
          Key = SourceLit.fsString
          Access = fun recv f -> recv + "." + pascal f.Name
          Present = sprintf "Some(%s, %s)"
          OptionalVar = fun _ -> "v"
          Optional = sprintf "(%s |> Option.map (fun v -> %s, %s))"
          Absent = "None"
          // The omit-at-default test, from the ONE declared-default literal the decoder restores
          // ([[fsDefaultLit]]), so the two halves cannot drift apart under a widening like
          // Phase 124's.
          OmitAt =
            fun src t d ->
                fsDefaultLit idl t d
                |> Result.map (fun dexpr present ->
                    match t with
                    // A UNION default is tested by pattern-match, not `=`. Phase 691: typing a
                    // closure slot gives its owning union a function-typed field, and F#
                    // functions support no equality, so the union stops supporting the
                    // `equality` constraint entirely — `CellFormat.Custom of (obj -> string)`
                    // broke `s.Format = CellFormat.None` for every column. A match is also
                    // simply the better test: it needs no constraint, and reads as what it is.
                    // Phase 124 — this is also why a value-carrying default works at all: the
                    // rendered literal is a legal PATTERN as well as a legal expression.
                    | TUnion _ -> sprintf "(match %s with | %s -> None | _ -> %s)" src dexpr present
                    // Phase 1080 — a LIST default is tested with `List.isEmpty`, never with
                    // `= []`, and for the same reason the union arm above exists: an element
                    // type that reaches a closure carries no equality, so `SrcSetEntry`
                    // (whose `src` is a `Binding`) fails the constraint the moment the
                    // generated encoder is compiled. `List.isEmpty` imposes none, and it is
                    // the better test anyway — it says what it means. (`fsDefaultLit` admits
                    // only the EMPTY list, so there is no non-empty case to answer for.)
                    | TList _ -> sprintf "(if List.isEmpty %s then None else %s)" src present
                    | _ -> sprintf "(if %s = %s then None else %s)" src dexpr present) }

    /// A member read off the bound record variable `recv` (`s` for a spec/record encoder, `n` for
    /// the node envelope), as its piece of a `List.choose id` list.
    let specPieceOf (idl: Idl) (recv: string) (f: IdlField) : Result<string, CodegenError> =
        let p = memberEncPrinter idl

        MemberEncTraversal.encMember p (p.Access recv f) f
        |> Result.map (MemberEncTraversal.print p)

    /// The members of a spec or record read off `s`, as the body of a `List.choose id` list.
    let private objectPieces (idl: Idl) (fields: IdlField list) : Result<string, CodegenError> =
        let p = memberEncPrinter idl

        MemberEncTraversal.encMembers p "s" fields
        |> Result.map (List.map (MemberEncTraversal.print p) >> String.concat "; ")

    /// Phase 511 — a decoder's binder for the node's kind, read from the envelope layout: the kind
    /// object under its key (`<req> "kind" __fs <dispatch>`), or — merged — the node object itself,
    /// which the spec decoders read by declared name, tolerating the id and discriminator.
    let envelopeKindBinder (env: EnvelopeLayout) (req: string) (dispatch: string) : string =
        match env.Kind with
        | KindPlacement.NestedUnder key -> sprintf "%s %s __fs %s" req (SourceLit.fsString key) dispatch
        | KindPlacement.Merged -> dispatch + " j"

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
    /// discriminated-object builder — `Canonical.typed` on a default-shape IDL
    /// (byte-identical to every pre-declarable emission), the module-local
    /// `typedTag` when the vocabulary declares another key (Phase 108). Phase 524 — the cases,
    /// their layouts and their members are [[UnionEncTraversal.encoder]]'s.
    let unionEncoder
        (typedName: string)
        (docFn: string -> string -> string)
        (decl: Decl)
        (u: IdlUnion)
        : Result<string, CodegenError> =
        let msg, idl = decl.Msg, decl.Idl

        let encArgs =
            u.Params
            |> List.map (fun p -> sprintf " (enc%s: '%s -> JVal)" p p)
            |> String.concat ""

        let tyArgs = declParams msg u.Name u.Params

        let pat (c: IdlUnionCase) =
            match c.Fields with
            | [] -> ""
            | [ f ] -> " " + ident f.Name
            | fs -> " (" + (fs |> List.map (fun f -> ident f.Name) |> String.concat ", ") + ")"

        let member' = memberEncPrinter idl

        let arm (c: IdlUnionCase) (body: string) =
            docFn ("encarm:" + u.Name + "." + c.Tag) "    "
            + sprintf "    | %s.%s%s -> %s" u.Name c.Tag (pat c) body

        UnionEncTraversal.encoder
            { Member = member'
              CaseVar = fun f -> ident f.Name
              // A DECLARED transparent case emits its single field's value BARE — no
              // `Canonical.typed` wrapper — the bare-string canonical literal shape.
              Bare = fun _ c e -> arm c e
              Tagged =
                fun _ c pieces ->
                    let always =
                        pieces
                        |> List.choose (function
                            | MemberPiece.Always(key, e) -> Some(key + ", " + e)
                            | MemberPiece.Conditional _ -> None)

                    // All-required cases keep the simple literal list (byte-identical to the
                    // pre-optional emission); any other member switches to the `List.choose id`
                    // omit-on-absence form.
                    if List.length always = List.length pieces then
                        arm c (sprintf "%s %s [ %s ]" typedName (SourceLit.fsString c.Tag) (String.concat "; " always))
                    else
                        arm
                            c
                            (sprintf
                                "%s %s ([ %s ] |> List.choose id)"
                                typedName
                                (SourceLit.fsString c.Tag)
                                (pieces |> List.map (MemberEncTraversal.print member') |> String.concat "; "))
              // The explicit `<'T>` type-parameter list (not just `'T` free in the signature) is
              // load-bearing for a generic union: `Binding.Format.source` is a fixed
              // `Binding<float>` *inside* `Binding<'T>`, so the generated `encBinding` recurses at a
              // concrete type ≠ the ambient `'T` — **polymorphic recursion**, which F# permits only
              // under an explicit generic-parameter declaration. Without it, `encBinding`
              // monomorphises to the first use (string) and the `float` recursion fails to
              // type-check.
              Union =
                fun _ arms ->
                    sprintf
                        "and private enc%s%s%s (v: %s%s) : JVal =\n    match v with\n%s"
                        u.Name
                        tyArgs
                        encArgs
                        u.Name
                        tyArgs
                        (String.concat "\n" arms) }
            decl
            u

    let specEncoder (typedName: string) (msg: Set<string>) (idl: Idl) (k: IdlKind) : Result<string, CodegenError> =
        // Single-line list literal — avoids F# offside-rule pitfalls in generated code.
        objectPieces idl k.Fields
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
    /// id` (omit-on-absence for optionals). `Canonical.write Rendering.CrossHost` Ordinal-sorts keys, so emission order is
    /// irrelevant. New for the real tier (`InvokeArg`, `FormField`, `FilterSpec`, `TabHeader`,
    /// `ColumnErased`, `ContentHash`, …).
    let recordEncoder (msg: Set<string>) (idl: Idl) (r: IdlRecord) : Result<string, CodegenError> =
        let ps = declParams msg r.Name []

        objectPieces idl r.Fields
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
          Union = fun n args -> "(dec" + n + " " + String.concat " " args + ")"
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
          // Phase 676 — accept any JSON verbatim; a shape check would contradict the field's
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
                    | None -> HostedSlot.Verbatim(sprintf "(fun (__j: JVal) -> dHosted ((%s) __j))" h.Decode)
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
                                    "(fun (__j: JVal) -> %s __j%s |> Result.bind (fun _ -> dHosted ((%s) __j)))"
                                    wd
                                    format
                                    h.Decode
                        ) }

    /// The decoder expression for a type — a `JVal -> Result<'T, DecodeError>`.
    let decFn (t: IdlType) : Result<string, CodegenError> = SlotTraversal.slotFn decPrinter t

    /// Phase 524 — the F# printer of a member READ back out ([[MemberTraversal.decMember]]): a
    /// sentinel slot decodes to `()` or its `TFn` placeholder, a host-only member to its declared
    /// placeholder, an omit-at-default one to [[fsDefaultLit]]'s literal.
    let memberDecPrinter (idl: Idl) : MemberDecPrinter =
        { Slot = decPrinter
          Key = SourceLit.fsString
          Req = sprintf "dReq %s __fs %s"
          Opt = sprintf "dOpt %s __fs %s"
          Def = sprintf "dDef %s __fs %s (%s)"
          Placeholder =
            function
            | TFn s -> s.Placeholder
            | _ -> "()"
          HostOnlySentinel = sprintf "Ok (%s)"
          HostOnly = fun f -> hostOnlyLit f |> Result.map (sprintf "Ok (%s)")
          DefaultLit = fsDefaultLit idl }

    /// Reading one field back out, honouring the presence rule it was written under ([[MemberEncTraversal.encMember]]).
    let decField (idl: Idl) (f: IdlField) : Result<string, CodegenError> =
        MemberTraversal.decMember (memberDecPrinter idl) f

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
        MemberTraversal.decMembers (memberDecPrinter idl) fs
        |> Result.map (List.map (fun (f, e) -> ident f.Name, e))

    /// An enum decoder (Phase 337): a string naming no case is `UnknownTag`, anything else
    /// `WrongKind` — the interpreter's split, and since Phase 520 the interpreter's detail, so the
    /// projected sentence is the interpreter's too.
    let enumDecoder (e: IdlEnum) =
        let arms =
            e.Cases
            |> List.map (fun c -> sprintf "    | JStr %s -> Ok %s.%s" (SourceLit.fsString (e.WireOf c)) e.Name c)
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
            // Phase 945 — a declared refine replaces the plain `Ok(Case(…))` final with a
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

            let body =
                if List.isEmpty c.Fields then
                    sprintf "        | %s -> Ok %s" (SourceLit.fsString c.Tag) (ctor c)
                else
                    sprintf
                        "        | %s ->\n%s"
                        (SourceLit.fsString c.Tag)
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
                            (SourceLit.fsString (oneOf (u.Cases |> List.map _.Tag)))
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
    // defect, so its collecting form is its short-circuiting decoder lifted (`cLift` / `cOne`). Only
    // an object, a list and a map have independent positions to collect over.
    // -----------------------------------------------------------------------

    /// Phase 524 — the F# COLLECTING decoder printer, derived from [[decPrinter]]
    /// ([[SlotTraversal.collecting]]): a leaf is the short-circuiting decoder lifted (`cLift`).
    ///
    /// Phase 541 — a node, a non-generic union and a record are read through the overridable
    /// decoders `__ov` (the emitted `CollectingDecoders` record, see [[collectingDecl]]), handed
    /// the same record, so a host's decoder at that type is the one applied at EVERY depth. A
    /// generic union has no monomorphic slot in the record: its own decoder is called with the
    /// record threaded through, so every position beneath it is still read through `__ov`.
    let colPrinter: SlotPrinter =
        SlotTraversal.collecting
            decPrinter
            (sprintf "(cLift %s)")
            { decPrinter with
                Var = fun v -> "col" + v
                Named = fun n -> "(__ov." + n + " __ov)"
                Union = fun n args -> "(col" + n + " __ov " + String.concat " " args + ")"
                Node = "(__ov.Node __ov)"
                List = sprintf "(cList %s)"
                Map = sprintf "(cMap %s)" }

    /// The collecting decoder expression for a type — a `JVal -> Result<'T, DecodeError list>`.
    let colFn (t: IdlType) : Result<string, CodegenError> = SlotTraversal.slotFn colPrinter t

    /// The F# collecting member reader: [[memberDecPrinter]]'s presence, read through
    /// [[colPrinter]].
    let colMemberPrinter (idl: Idl) : MemberDecPrinter =
        { memberDecPrinter idl with
            Slot = colPrinter
            Req = sprintf "cReq %s __fs %s"
            Opt = sprintf "cOpt %s __fs %s"
            Def = sprintf "cDef %s __fs %s (%s)" }

    /// Every member read collecting ([[MemberTraversal.colMembers]]), bound to its field name. A
    /// leaf member (and a host-only one, which reads nothing) is [[decField]]'s expression lifted
    /// (`cOne`), so its presence rules are the short-circuiting decoder's by construction.
    let colBinders (idl: Idl) (fs: IdlField list) : Result<(string * string) list, CodegenError> =
        MemberTraversal.colMembers (memberDecPrinter idl) (colMemberPrinter idl) (sprintf "cOne (%s)") fs
        |> Result.map (List.map (fun (f, e) -> ident f.Name, e))

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
    /// with a declared REFINE (Phase 945) collects its members and, when every one decoded, applies
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
            if List.isEmpty c.Fields then
                sprintf "        | %s -> Ok %s" (SourceLit.fsString c.Tag) (ctor c)
            else
                let binders = members |> List.map (fun (f, e) -> ident f.Name, e)

                let body =
                    match refines.TryFind(u.Name + "." + c.Tag) with
                    | Some r -> collectRefined "            " binders r
                    | None -> collectAll "            " binders (sprintf "Ok(%s)" (ctor c))

                sprintf "        | %s ->\n%s" (SourceLit.fsString c.Tag) body

        UnionTraversal.decoder
            { Read = read
              Slot = colPrinter
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
                            (SourceLit.fsString (oneOf (u.Cases |> List.map _.Tag)))
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
            "and private col%s (__ov: CollectingDecoders) (j: JVal) : Result<%s%s, DecodeError list> =\n    cObj j |> Result.bind (fun __fs ->\n%s)"
            decName
            typeName
            (objParams msg typeName [])
            (collectAll "        " binders ("Ok " + recordLit typeName assigns))

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
    let collectingDecl (sup: GenSupport) (decl: Decl) : Result<string, CodegenError> =
        let idl, msg, kinds, unions, records =
            decl.Idl, decl.Msg, decl.Kinds, decl.Unions, decl.Records

        let nodeTy = objParams msg "Node" []
        let projected (k: IdlKind) = sup.KindProjections.ContainsKey k.Tag

        let specTy (k: IdlKind) =
            k.Tag + "Spec" + objParams msg (k.Tag + "Spec") []

        // A generic union has no monomorphic field: its instantiations are read through the
        // record by its own decoder (see [[colFn]]). A projected kind's decoder is the host
        // projection's, a leaf with no position beneath it.
        let slots =
            [ yield "Node", "Node" + nodeTy
              for u in unions do
                  if List.isEmpty u.Params then
                      yield u.Name, u.Name + objParams msg u.Name []
              for r in records do
                  yield r.Name, r.Name + objParams msg r.Name []
              for k in kinds do
                  if not (projected k) then
                      yield k.Tag + "Spec", specTy k ]

        let recordTypeDecl =
            "/// Phase 541 — the collecting decoders a host may override, one per type the vocabulary reaches\n"
            + "/// through a nested position. Each takes the record it is read from, so a decoder that delegates\n"
            + "/// to `collectingDecoders.<Type> self` keeps every position beneath it reading `self`.\n"
            + "type CollectingDecoders =\n"
            + (slots
               |> List.mapi (fun i (field, ty) ->
                   sprintf
                       "%s%s: CollectingDecoders -> JVal -> Result<%s, DecodeError list>"
                       (if i = 0 then "    { " else "      ")
                       field
                       ty)
               |> String.concat "\n")
            + " }"

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
            + "    cObj j |> Result.bind (fun __fs ->\n"
            + collectAll "        " binders final
            + ")"

        let groupR =
            CollectingTraversal.group
                { Dec = memberDecPrinter idl
                  Col = colMemberPrinter idl
                  Lift = sprintf "cOne (%s)"
                  Union = fun read u -> colUnionDecoder sup.CaseRefines decl read u
                  NodeKind = colNodeKindDecl
                  Node = colNodeDecl
                  Record = fun r members -> colObjectDecoder r.Name r.Name msg r.Fields members
                  // A projected kind's decoder is the projection's short-circuiting one, lifted.
                  ReadsSpec = fun k -> not (projected k)
                  Spec =
                    fun k members ->
                        match members with
                        | Some ms -> colObjectDecoder (k.Tag + "Spec") (k.Tag + "Spec") msg k.Fields ms
                        | None -> sprintf "and private col%sSpec (j: JVal) =\n    cOne (dec%sSpec j)" k.Tag k.Tag }
                decl

        let generatedDecl =
            "/// Phase 541 — the generated collecting decoders: what every `decode*All` entry reads through, and\n"
            + "/// the record a host copies (`{ collectingDecoders with <Type> = … }`) to override one type.\n"
            + "let collectingDecoders: CollectingDecoders =\n"
            + (slots
               |> List.mapi (fun i (field, _) ->
                   sprintf "%s%s = col%s" (if i = 0 then "    { " else "      ") field field)
               |> String.concat "\n")
            + " }"

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

        groupR
        |> Result.map (fun group ->
            helpers @ [ recordTypeDecl; group; generatedDecl; entries ]
            |> String.concat "\n\n")

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


    // -----------------------------------------------------------------------
    // Phase 317 increment 5 — the Core witness-record leg. Emit a
    // `NodeWitness<Node, string>` for the generated `Node`, so the generated
    // structural layer plugs straight into `Fuaran.Core.Tree` / `.Validator` /
    // `.Observer` — the "serves every domain via the Core witness, not just UI"
    // promise. `Children` / `ReplaceChildren` are derived from the IDL: a field
    // is a child position iff `FSharpDerive.structuralField` says so — a node or
    // node list the kind always carries (Phase 545: an optional node is a keyed
    // position, on both the private and the public emission).
    // -----------------------------------------------------------------------

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
    /// Phase 545 — both emissions read child positions through the one classifier,
    /// `FSharpDerive.structuralField`. The private witness used to read them by type alone, so an
    /// optional node field became `[ s.Hint ]` — a `Node option` where a `Node` is expected — and
    /// a module requesting no StructuralAccess failed to compile (FS0001).
    ///
    /// Phase 403 — a projected kind's children are read from its declared record fields
    /// (`Projection.RecordFields`); one that declares none and whose wire holds a node directly is
    /// refused by name, because nothing says which member of the host record holds it.
    let witnessDecl
        (publicAccess: bool)
        (projections: Map<string, KindProjection>)
        (msg: Set<string>)
        (kinds: IdlKind list)
        : Result<string, CodegenError> =
        let nodeArgs = declParams msg "Node" []

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
            |> List.filter (fun k -> fieldsOf k |> List.exists (FSharpDerive.structuralField >> Option.isSome))

        let allBearing = List.length childBearing = List.length kinds

        let kindTagArms =
            kinds
            |> List.map (fun k -> sprintf "    | NodeKind.%s _ -> %s" k.Tag (SourceLit.fsString k.Tag))
            |> String.concat "\n"

        let childArm (k: IdlKind) =
            let exprs =
                fieldsOf k
                |> List.choose FSharpDerive.structuralField
                |> List.map (fun (name, isList) -> if isList then "s." + name else "[ s." + name + " ]")
                |> String.concat " @ "

            sprintf "    | NodeKind.%s s -> %s" k.Tag exprs

        let replaceArm (k: IdlKind) : Result<string, CodegenError> =
            match fieldsOf k |> List.choose FSharpDerive.structuralField with
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


    /// Phase 505 — the F# module's [[Stamp]], over its normalised text.
    let private fsharpStamp (idl: Idl) (body: string) : string =
        Stamp.generated (CommentStyle.Line "//") "F# structural layer" [ idl ] body

    /// The F# module emission before [[normalizeEol]] — see `fsharpModuleWith`, the public
    /// entry point, which is this composed with it.
    let fsharpModuleUnnormalised
        (sup: GenSupport)
        (requests: Derivation list)
        (moduleName: string)
        (decl: Decl)
        : Result<string, CodegenError> =
        let decoders = List.contains Derivation.SpecDecoders requests
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

            // Phase 690 — the envelope rides the same `List.choose id` presence
            // machinery every spec field uses, with `s.` rebound to `n.`, so
            // omit-on-absence / omit-at-default behave identically on a node field
            // and on a kind field. No envelope ⇒ the original two-key literal.
            //
            // Phase 109 — the FLAT shape merges the id (and any envelope) into the
            // kind's own object; `Canonical.write Rendering.CrossHost` sorts keys, so splice order is
            // irrelevant. Nested emission is byte-identical to the pre-declarable form.
            let envelopePieces () =
                decl.Envelope.Members |> List.map (specPieceOf idl "n") |> concatR "; "

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
            (encNodeDecl :: (unions |> List.map (unionEncoder typedName doc decl))
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
                if decl.Obsolete then
                    "\n#nowarn \"44\" // this layer implements every declared member, including deprecated ones"
                else
                    ""

            // Phase 505 — the stamp above `module` is written by the public entry points, over the
            // normalised text, so it hashes exactly the bytes it heads.
            sprintf "module %s%s\n\nopen Fuaran.Core" moduleName nowarn

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
                    "\n    | __other -> dUnknown %s (%s + __other + \"'\")))"
                    (SourceLit.fsString (oneOf (kinds |> List.map _.Tag)))
                    (SourceLit.fsString (idl.Wire.Discriminator + " '"))

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
             :: (unions |> List.map (unionDecoder doc sup.CaseRefines decl))
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
            |> List.exists (fun r -> r = Derivation.StructuralAccess || r = Derivation.KeyedPositions)

        let witnessAndDerived =
            Decl.projectionsDeclared decl
            |> Result.bind (fun () -> witnessDecl publicAccess decl.Projections msg kinds)
            |> Result.bind (fun w -> FSharpDerive.derivedDecl decl requests |> Result.map (fun ds -> w, ds))
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
              [ recGroup ]
              [ sprintf
                    "let encodeNode (n: Node%s) : string = %s (encNode n)"
                    nodeArgs
                    (match idl.Wire.KeyOrder with
                     | KeyOrder.Sorted -> "Canonical.write Rendering.CrossHost"
                     | KeyOrder.Declared -> "Canonical.write Rendering.Declared") ]
              // Phase 694 — JVal-level accessors for host codecs that splice
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
              decodeHelpers idl.Wire.Discriminator decl.Hosted (not sup.CaseRefines.IsEmpty)
              :: (if decl.HostedFormat then [ fsFormatHelper ] else [])
              enums |> List.map (fun e -> doc ("dec:" + e.Name) "" + enumDecoder e)
              [ decGroup ]
              // Phase 337 — the parser's refusal through `Decoder.parse` (under `ReadOptions.strict`
              // since Phase 519): `InvalidJson` at the root, or `LimitExceeded` at the path of the
              // container opened past the nesting cap.
              [ sprintf
                    "/// Structural decode. The policy layer (diagnostics, §16 lenient-accept,\n/// the reject set) composes ABOVE this — see the Phase 672 note in the generator.\n/// A refusal is Core's `DecodeError` (Phase 337): the code and path the IDL interpreter\n/// reports for the same document, and this layer's sentence (`DecodeError.describe`).\nlet decodeNode (s: string) : Result<Node%s, DecodeError> =\n    Decoder.parse ReadOptions.strict s |> Result.bind decNode"
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
        |> Result.bind (fun () ->
            if publicAccess then
                witnessDecl true decl.Projections decl.Msg decl.Kinds |> Result.map ignore
            else
                Ok())
        |> Result.bind (fun () -> FSharpDerive.derivedDecl decl requests |> Result.map ignore)
