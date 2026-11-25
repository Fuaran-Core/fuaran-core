namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.Annotations
open Fuaran.Core.Idl.Emit.FSharpTypes
open Fuaran.Core.Idl.Emit.FSharpDefaults
open Fuaran.Core.Idl.Emit.FSharpCanonicalForm

/// The F# structural-layer ENCODER printers: the slot and member printers, the host-codec reads,
/// and the enum, union, kind-spec and record encoders [[FSharpCodec]] assembles.
module internal FSharpEncode =

    // -----------------------------------------------------------------------
    // fuaran#317 increment 3 — *feature-complete* code emission: emit a
    // self-contained, compiling F# encoder module for a set of kinds, handling
    // every feature class the interpreter does — Required + Optional fields
    // (omit-on-absence via List.choose), parameterised unions (`Binding<'T>`, by
    // codec-passing), lists, and node nesting (a recursive `encNode`). The proof
    // that the generator — not just the interpreter — covers the whole surface.
    // -----------------------------------------------------------------------

    /// The F# codec of a tuple, by its component codecs: an encoder over the host tuple, or a decoder
    /// reading an array of exactly that many items, each refusal under its index.
    let tupleCodec (encode: bool) (parts: string list) : string =
        let n = List.length parts
        let names prefix = List.init n (sprintf "%s%d" prefix)

        if encode then
            sprintf
                "(fun (%s) -> JArr [ %s ])"
                (names "__t" |> String.concat ", ")
                (List.map2 (fun e x -> sprintf "%s %s" e x) parts (names "__t")
                 |> String.concat "; ")
        else
            let rec bind (i: int) (rest: string list) =
                match rest with
                | [] -> sprintf "Ok(%s)" (names "__v" |> String.concat ", ")
                | d :: tail ->
                    sprintf
                        "%s __x%d |> dUnder (PathSegment.Index %d) |> Result.bind (fun __v%d -> %s)"
                        d
                        i
                        i
                        i
                        (bind (i + 1) tail)

            sprintf
                "(fun (__j: JVal) -> match __j with | JArr [ %s ] -> %s | JArr __xs -> dFail DecodeCode.OutOfRange \"an array of %d items\" (sprintf \"an array of %%d items\" (List.length __xs)) | __other -> Error(Decoder.wrongKind \"array\" __other))"
                (names "__x" |> String.concat "; ")
                (bind 0 parts)
                n

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
              Union = fun n args -> "(enc" + n + " " + String.concat " " (List.map snd args) + ")"
              List = sprintf "(fun __xs -> JArr(List.map %s __xs))"
              Map = sprintf "(fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, %s v)))"
              // A closure/opaque codec ignores its argument and emits the fixed sentinel.
              Closure = closure
              Fn = fun _ -> closure
              Opaque = opaque
              // fuaran#676 — the canonical renderer already sorts keys, escapes and lays floats
              // out, so identity inherits all three.
              Json = "id"
              // The named host encode expression, verbatim ('host -> JVal). Canonicality is
              // inherited: the host codec builds a JVal that renders through the same renderer.
              Hosted = fun h -> HostedSlot.Verbatim h.Encode
              // Phase 639 (D251) — the wider data universe ([[widerEncodeHelpers]]). A text-borne
              // exact type is its canonical text, written as it is held.
              Wider =
                fun t slot ->
                    match t with
                    | TInt64 -> Ok "encInt64"
                    | TBytes -> Ok "encBytes"
                    | TTuple cs -> cs |> List.map slot |> sequenceR |> Result.map (fun ps -> tupleCodec true ps)
                    | TSet e -> slot e |> Result.map (sprintf "(encSet %s)")
                    | TKeyedMap(k, v) ->
                        match slot k, slot v with
                        | Ok ek, Ok ev -> Ok(sprintf "(encKeyedMap %s %s)" ek ev)
                        | Error e, _
                        | _, Error e -> Error e
                    | _ -> Ok "JStr" }
          Apply = sprintf "%s %s"
          ListApplied = sprintf "JArr(List.map %s %s)"
          NodeApplied = sprintf "encNode %s"
          UnionApplied = fun n args var -> "(enc" + n + " " + String.concat " " args + ") " + var
          ClosureApplied = fun _ -> "JStr \"<closure>\""
          OpaqueApplied = fun _ -> "JStr \"<opaque>\"" }

    /// The applied JVal expression for a value of `t` bound to `var`.
    let encApplied (var: string) (t: IdlType) : Result<string, CodegenError> =
        SlotTraversal.encApplied encPrinter var t

    // -----------------------------------------------------------------------
    // Phase 550 (DECISIONS.md D198) — the host codecs' refusal, and the CASE-LEVEL host codec.
    // -----------------------------------------------------------------------

    /// A host decode expression applied to `arg`, answering a typed refusal: a typed codec's
    /// refusal as it is (its code and its path, which the caller's position then prefixes), a
    /// sentence codec's lifted by `dHosted` to `OutOfRange` where it was applied.
    let hostedRead (h: HostedCodec) (arg: string) : string =
        match h.Refusal with
        | HostedRefusal.Typed -> sprintf "((%s) %s)" h.Decode arg
        | HostedRefusal.Sentence -> sprintf "dHosted ((%s) %s)" h.Decode arg

    /// The host arity of a case-level codec's payload type: its top-level `*` components
    /// (`string * Cell` is two, `Cell` one), `unit` none. A `*` inside brackets is not top-level.
    let hostArity (fsType: string) : int =
        if fsType.Trim() = "unit" then
            0
        else
            let mutable depth = 0
            let mutable stars = 0

            for ch in fsType do
                match ch with
                | '('
                | '<'
                | '[' -> depth <- depth + 1
                | ')'
                | '>'
                | ']' -> depth <- depth - 1
                | '*' when depth = 0 -> stars <- stars + 1
                | _ -> ()

            stars + 1

    /// The binders of a host case's payload, `__p0 …`, as its pattern suffix and as one tuple value.
    let payloadBinders (h: HostedCodec) : string * string =
        match hostArity h.FSharp with
        | 0 -> "", "()"
        | 1 -> " __p0", "__p0"
        | n ->
            let ps = List.init n (sprintf "__p%d") |> String.concat ", "
            " (" + ps + ")", "(" + ps + ")"

    /// Whether any union the resolution reaches declares a case-level host codec.
    let declaresHostedCase (decl: Decl) : bool =
        decl.Unions
        |> List.exists (fun u -> u.Cases |> List.exists (fun c -> c.Hosted.IsSome))

    /// A derivation walks a case's members as its fields, and a case-level host codec's case holds a
    /// host payload instead: refused by name rather than derived wrongly.
    let hostedCaseDerivations (decl: Decl) (requests: Derivation list) : Result<unit, CodegenError> =
        match
            requests,
            decl.Unions
            |> List.tryPick (fun u -> u.Cases |> List.tryFind (fun c -> c.Hosted.IsSome) |> Option.map (fun c -> u, c))
        with
        | _ :: _, Some(u, c) ->
            Error(
                CodegenError.UnsupportedConstruct(
                    sprintf
                        "a structural derivation over case '%s.%s', which declares a case-level host codec"
                        u.Name
                        c.Tag,
                    "a derivation walks a case's members as its fields, and this case's host payload is not its members",
                    "request no derivation over a vocabulary reaching the case, or drop its case codec"
                )
            )
        | _ -> Ok()

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


    // Phase 474 — the pieces and reads of a field holding an INLINE declaration (see below).

    /// The member that a REQUIRED presence-keyed union is refused `MissingField` at when no case's
    /// key is present: its first case's key.
    let private keyedFirst (keyed: KeyedCase list) : string =
        match keyed with
        | k :: _ -> SourceLit.fsString k.Key
        | [] -> SourceLit.fsString ""

    /// The F# piece laying an inline field's members onto the object being written, read from `src`.
    let inlineEncPiece (idl: Idl) (src: string) (f: IdlField) : string option =
        match Decl.inlineSlot idl f.Type, f.Opt with
        | Some(InlineSlot.Record r), _ -> Some(sprintf "yield! encMembers%s %s" r.Decl.Name src)
        | Some(InlineSlot.Keyed(u, _)), Optional ->
            Some(sprintf "yield! (match %s with Some __v -> encMembers%s __v | None -> [])" src u.Name)
        | Some(InlineSlot.Keyed(u, _)), _ -> Some(sprintf "yield! encMembers%s %s" u.Name src)
        | None, _ -> None

    /// The F# expression reading an inline field from the object `o` its members sit on.
    let inlineDecRead (idl: Idl) (o: string) (f: IdlField) : string option =
        match Decl.inlineSlot idl f.Type, f.Opt with
        | Some(InlineSlot.Record r), _ -> Some(sprintf "decMembers%s %s" r.Decl.Name o)
        | Some(InlineSlot.Keyed(u, _)), Optional -> Some(sprintf "decMembers%s %s" u.Name o)
        | Some(InlineSlot.Keyed(u, keyed)), _ ->
            Some(
                sprintf
                    "(decMembers%s %s |> Result.bind (function Some __v -> Ok __v | None -> Error(Decoder.missing %s)))"
                    u.Name
                    o
                    (keyedFirst keyed)
            )
        | None, _ -> None

    /// The collecting twin of [[inlineDecRead]].
    let inlineColRead (idl: Idl) (o: string) (f: IdlField) : string option =
        match Decl.inlineSlot idl f.Type, f.Opt with
        | Some(InlineSlot.Record r), _ -> Some(sprintf "colMembers%s __ov j %s" r.Decl.Name o)
        | Some(InlineSlot.Keyed(u, _)), Optional -> Some(sprintf "colMembers%s __ov j %s" u.Name o)
        | Some(InlineSlot.Keyed(u, keyed)), _ ->
            Some(
                sprintf
                    "(colMembers%s __ov j %s |> Result.bind (function Some __v -> Ok __v | None -> Error [ Decoder.missing %s ]))"
                    u.Name
                    o
                    (keyedFirst keyed)
            )
        | None, _ -> None

    /// Phase 524 — the F# printer of a member WRITTEN under its presence
    /// ([[MemberEncTraversal.encMember]]): a `(string * JVal) option` piece for `List.choose id`.
    /// `src` is the expression the member's value is read from — `s.Field` in a spec or record
    /// encoder, `n.Field` on the node envelope (fuaran#690), the positional binding in a union case.
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
                    // A UNION default is tested by pattern-match, not `=`. fuaran#691: typing a
                    // closure slot gives its owning union a function-typed field, and F#
                    // functions support no equality, so the union stops supporting the
                    // `equality` constraint entirely — `CellFormat.Custom of (obj -> string)`
                    // broke `s.Format = CellFormat.None` for every column. A match is also
                    // simply the better test: it needs no constraint, and reads as what it is.
                    // Phase 124 — this is also why a value-carrying default works at all: the
                    // rendered literal is a legal PATTERN as well as a legal expression.
                    //
                    // Phase 656 — a TUPLE default is tested by pattern-match for the same reason: a
                    // component's literal is a legal pattern ([[LiteralTraversal]] prints its items as
                    // a union literal's members), and a component that reaches a closure has no `=`.
                    | TUnion _
                    | TTuple _ -> sprintf "(match %s with | %s -> None | _ -> %s)" src dexpr present
                    // fuaran#1080 — a LIST default is tested with `List.isEmpty`, never with
                    // `= []`, and for the same reason the union arm above exists: an element
                    // type that reaches a closure carries no equality, so `SrcSetEntry`
                    // (whose `src` is a `Binding`) fails the constraint the moment the
                    // generated encoder is compiled. `List.isEmpty` imposes none, and it is
                    // the better test anyway — it says what it means. (`fsDefaultLit` admits
                    // only the EMPTY list, so there is no non-empty case to answer for.)
                    | TList _ -> sprintf "(if List.isEmpty %s then None else %s)" src present
                    // Phase 539 — the empty-map default, tested as the list one is (`Map.isEmpty`
                    // needs no equality at the value type; [[Decl.literal]] admits only the EMPTY map).
                    | TMap _ -> sprintf "(if Map.isEmpty %s then None else %s)" src present
                    // Phase 656 — the empty set and the empty keyed map, tested as the map is.
                    | TSet _ -> sprintf "(if Set.isEmpty %s then None else %s)" src present
                    | TKeyedMap _ -> sprintf "(if Map.isEmpty %s then None else %s)" src present
                    // Phase 539 — a JSON default is compared as the interpreter compares it, by
                    // canonical bytes, so a value holding the default's members in another order is
                    // still at it.
                    | TJson ->
                        match d with
                        | VJson j ->
                            sprintf
                                "(if Canonical.write Rendering.CrossHost %s = %s then None else %s)"
                                src
                                (SourceLit.fsString (Canonical.write Rendering.CrossHost j))
                                present
                        | _ -> sprintf "(if %s = %s then None else %s)" src dexpr present
                    | _ -> sprintf "(if %s = %s then None else %s)" src dexpr present)
          // Phase 539 — a member whose presence condition fails is omitted whatever it holds, as the
          // interpreter omits it.
          When =
            fun fields srcOf self c ->
                presenceTest idl fields srcOf self c
                |> Result.map (fun test piece -> sprintf "(if %s then %s else None)" test piece)
          // Phase 474 — an inline field's members, spliced into the piece list.
          Inline = fun src f -> inlineEncPiece idl src f }

    /// A member read off the bound record variable `recv` (`s` for a spec/record encoder, `n` for
    /// the node envelope), as its piece of a `List.choose id` list.
    let specPieceOf (idl: Idl) (recv: string) (fields: IdlField list) (f: IdlField) : Result<string, CodegenError> =
        let p = memberEncPrinter idl

        MemberEncTraversal.encMember p fields (p.Access recv) f
        |> Result.map (MemberEncTraversal.print p)

    /// The members of a spec or record read off `s`, as the body of a `List.choose id` list.
    let objectPieces (idl: Idl) (fields: IdlField list) : Result<string, CodegenError> =
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
        // wire — identical unless the enum declares a mapping (fuaran#707).
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
                    match c.Hosted with
                    // Phase 550 — a case-level host codec writes the members beside the tag.
                    | Some h ->
                        let pattern, payload = payloadBinders h

                        docFn ("encarm:" + u.Name + "." + c.Tag) "    "
                        + sprintf
                            "    | %s.%s%s -> %s %s ((%s) %s)"
                            u.Name
                            c.Tag
                            pattern
                            typedName
                            (SourceLit.fsString (u.WireTagOf c.Tag))
                            h.Encode
                            payload
                    | None ->
                        let always =
                            pieces
                            |> List.choose (function
                                | MemberPiece.Always(key, e) -> Some(key + ", " + e)
                                | MemberPiece.Conditional _ -> None)

                        // All-required cases keep the simple literal list (byte-identical to the
                        // pre-optional emission); any other member switches to the `List.choose id`
                        // omit-on-absence form.
                        if List.length always = List.length pieces then
                            arm
                                c
                                (sprintf
                                    "%s %s [ %s ]"
                                    typedName
                                    (SourceLit.fsString (u.WireTagOf c.Tag))
                                    (String.concat "; " always))
                        else
                            arm
                                c
                                (sprintf
                                    "%s %s ([ %s ] |> List.choose id)"
                                    typedName
                                    (SourceLit.fsString (u.WireTagOf c.Tag))
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
