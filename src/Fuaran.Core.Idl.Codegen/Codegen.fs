namespace Fuaran.Core.Idl

open Fuaran.Core

// ---------------------------------------------------------------------------
// The CODEGEN half of the IDL (Phase 97) — `Fuaran.Core.Idl.Codegen`.
//
// The split is by COMMITMENT, not by size. `Fuaran.Core.Idl` promises a model, a
// codec and a sampler: values in, values out, and a surface a consumer can pin.
// This package promises SOURCE — F#, TypeScript, a JSON Schema, a scaffold — and
// its real contract is therefore the shape of what it emits, which a consumer
// compiles and ships. That is a second, harder-to-version contract sitting on top
// of the first, and it is the reason the two are separately packaged rather than
// separately namespaced: a consumer that wants to decode a tree should not have to
// adopt a generator's output cadence to do it.
//
// It is also where every non-portable construct lives. `System.Text.StringBuilder`
// and `CultureInfo.InvariantCulture` serve the TypeScript source backend and appear
// nowhere in the half that must Fable-compile — which is what turns the portability
// claim below into something the smoke gate can check rather than something this
// comment asserts.
//
// The namespace stays `Fuaran.Core.Idl`: `Gen`, `Trust` and `Diff` keep their
// identity, so an existing call site changes its package reference and nothing else.
// ---------------------------------------------------------------------------

/// A code-generation failure on an IDL construct the generator cannot yet emit (GP4: a typed
/// value, not an exception; GP5: each case names the unsupported construct and thereby the set
/// the generator *does* support). Surfaced at *generation* time (build-time blast radius) from
/// `Gen.fsharpModule`, so an unsupported construct is a typed `Error` rather than a `failwith`
/// in the generator or a `failwith` emitted into the generated code.
type CodegenError =
    /// A field default whose (IDL type, value) pair has no emission — only scalars (`TStr`/`TInt`/
    /// `TBool`) and enums (`TEnum`) carry a default expression today; unions / nodes are a later
    /// leg. Names the offending type + value.
    | UnsupportedDefault of ty: IdlType * value: IdlValue
    /// A kind mixing a `Node list` field with other node-bearing fields — `witnessReplaceChildren`
    /// has no unambiguous positional split for it (several single-`Node` fields ARE generated,
    /// re-assigned positionally; no mixed kind exists in the current vocabulary). Names the kind tag.
    | MultiChildFieldKind of kindTag: string

/// The type-generation leg: emit illustrative F# type source from the IDL — the
/// "generate Types.fs" half of the inversion. Spike-grade (a source string, not a
/// compiled artefact); proves the IDL carries enough to project a host's types.
module Gen =

    /// Sequence a list of codegen results, short-circuiting on the first `CodegenError` (order
    /// preserved). The generator assembles source from many per-kind / per-field fragments; this
    /// threads a single typed failure up through the fragment lists without exceptions.
    let private sequenceR (results: Result<'a, CodegenError> list) : Result<'a list, CodegenError> =
        (Ok [], results)
        ||> List.fold (fun acc r ->
            match acc, r with
            | Error e, _ -> Error e
            | Ok _, Error e -> Error e
            | Ok xs, Ok x -> Ok(x :: xs))
        |> Result.map List.rev

    let private pascal (s: string) =
        if s.Length = 0 then
            s
        else
            string (System.Char.ToUpperInvariant s.[0]) + s.Substring 1

    /// F# reserved keywords that can collide with an IDL field name used *verbatim* as an
    /// identifier. Spec / record fields are `pascal`-cased (first letter upper — no F# keyword
    /// is upper-case), so they are always safe; **union-case fields are positional bindings used
    /// lower-case as-authored** (`| Value of ``default``: Scalar option * …`), so a keyword-named
    /// one (`default` on `HoleDecl.Value`) must be back-tick escaped in every identifier position
    /// (the field label, the match-pattern binding, the value reference) — but NOT in the wire
    /// *key string*, which stays the raw name. Back-tick quoting a non-keyword is harmless F#, so
    /// over-inclusion is safe; the set is the real reserved words so unaffected names stay bare.
    let private fsKeywords =
        set
            [ "abstract"
              "and"
              "as"
              "assert"
              "base"
              "begin"
              "class"
              "default"
              "delegate"
              "do"
              "done"
              "downcast"
              "downto"
              "elif"
              "else"
              "end"
              "exception"
              "extern"
              "false"
              "finally"
              "fixed"
              "for"
              "fun"
              "function"
              "global"
              "if"
              "in"
              "inherit"
              "inline"
              "interface"
              "internal"
              "lazy"
              "let"
              "match"
              "member"
              "module"
              "mutable"
              "namespace"
              "new"
              "null"
              "of"
              "open"
              "or"
              "override"
              // Reserved-for-future (FS0046 warns on bare use) — hit by
              // `Binding.Transform`'s `params` field; escaping is harmless.
              "params"
              "private"
              "public"
              "rec"
              "return"
              "sig"
              "static"
              "struct"
              "then"
              "to"
              "true"
              "try"
              "type"
              "upcast"
              "use"
              "val"
              "void"
              "when"
              "while"
              "with"
              "yield" ]

    /// A field name in F#-identifier position — back-tick-escaped if it is a reserved keyword.
    let private ident (s: string) : string =
        if fsKeywords.Contains s then "``" + s + "``" else s

    // -----------------------------------------------------------------------
    // Phase 689 — `'Msg` threading.
    //
    // A [[TFn]] slot whose `FSharp` signature mentions `'Msg` makes its owning
    // type generic in `'Msg`, and that propagates: `TabsSpec` carries a handler,
    // so `NodeKind` carries `TabsSpec`, so `Node` carries `NodeKind`. The set is
    // the least fixpoint of "mentions `'Msg` directly, or references something
    // that does". Computing it is what lets the generated declarations BE the
    // authoring types instead of a `'Msg`-erased projection of them.
    //
    // `Binding<'T>` is deliberately NOT in the set on the real UI IDL: the tier
    // obj-erases exactly where a `'Msg` parameter would be inconvenient
    // (`LocalBinding.OnCommit: 'T -> obj`, `Action.Call`'s `onResult: obj -> 'Msg`),
    // so the parameter stays confined to the kinds that genuinely dispatch.
    // -----------------------------------------------------------------------

    /// The type names emitted generic in `'Msg` — union names, record names,
    /// `<Tag>Spec` names, plus `NodeKind` / `Node` when any kind qualifies.
    let msgCarrying (idl: Idl) : Set<string> =
        let rec mentions (seen: Set<string>) (t: IdlType) =
            match t with
            | TFn s -> s.FSharp.Contains "'Msg"
            | TList inner -> mentions seen inner
            | TMap vt -> mentions seen vt
            | TNode -> seen.Contains "Node"
            | TUnion(n, args) -> seen.Contains n || args |> List.exists (mentions seen)
            | TRecord n -> seen.Contains n
            | _ -> false

        let step (seen: Set<string>) =
            let fieldsMention (fs: IdlField list) =
                fs |> List.exists (fun f -> mentions seen f.Type)

            let unions =
                idl.Unions
                |> List.filter (fun u -> u.Cases |> List.exists (fun c -> fieldsMention c.Fields))
                |> List.map _.Name

            let records =
                idl.Records |> List.filter (fun r -> fieldsMention r.Fields) |> List.map _.Name

            let kinds =
                idl.Kinds
                |> List.filter (fun k -> fieldsMention k.Fields)
                |> List.map (fun k -> k.Tag + "Spec")

            // `NodeKind` wraps every spec, and `Node` wraps `NodeKind` — so one
            // dispatching kind makes the whole tree generic. That is the point.
            let tree =
                if
                    idl.Kinds
                    |> List.exists (fun k -> Set.contains (k.Tag + "Spec") (Set.ofList kinds))
                then
                    [ "NodeKind"; "Node" ]
                else
                    []

            Set.unionMany
                [ seen
                  Set.ofList unions
                  Set.ofList records
                  Set.ofList kinds
                  Set.ofList tree ]

        let rec fix (seen: Set<string>) =
            let next = step seen
            if next = seen then seen else fix next

        fix Set.empty

    /// The `<…>` parameter list for a declaration, with `'Msg` appended when the
    /// type is msg-carrying. Declared params come first so an existing
    /// `Binding<'T>` keeps its shape if it ever gains a handler.
    let private declParams (msg: Set<string>) (name: string) (ps: string list) =
        let ps =
            (ps |> List.map (fun p -> "'" + p))
            @ (if msg.Contains name then [ "'Msg" ] else [])

        if List.isEmpty ps then
            ""
        else
            "<" + String.concat ", " ps + ">"

    /// The same suffix with `'Msg` instantiated to `obj` — the DECODER's shape.
    /// A closure cannot be rebuilt from `"<closure>"`, so a decoded tree is the
    /// storage shape (the tier's own `decodeNodeObj` / `WireTree` boundary), and a
    /// host re-attaches typed behaviour above it.
    let private objParams (msg: Set<string>) (name: string) (ps: string list) =
        let ps =
            (ps |> List.map (fun p -> "'" + p))
            @ (if msg.Contains name then [ "obj" ] else [])

        if List.isEmpty ps then
            ""
        else
            "<" + String.concat ", " ps + ">"

    let rec private fsTypeIn (msg: Set<string>) (t: IdlType) =
        let fsType = fsTypeIn msg

        let applied (n: string) (args: string list) =
            let args = args @ (if msg.Contains n then [ "'Msg" ] else [])

            if List.isEmpty args then
                n
            else
                n + "<" + String.concat ", " args + ">"

        match t with
        | TStr -> "string"
        | TInt -> "int"
        | TBool -> "bool"
        | TFloat -> "float"
        | TEnum n -> n
        | TUnion(n, args) -> applied n (args |> List.map fsType)
        | TVar v -> "'" + v
        | TNode -> applied "Node" []
        // Phase 703 models the OP vocabulary and certifies the interpreter leg
        // against the corpus; emitting an op family from the F# type emitter is a separate,
        // larger piece of work (`TreeOp` is msg-carrying through `TKind`/`TNode`,
        // so it lands as a generic type group). Nothing walks `idl.Ops` in this
        // backend yet, so these arms are unreachable today — explicit and loud so
        // that wiring ops in gets a precise signal instead of a match failure.
        | TKind
        | TOp ->
            failwithf
                "the F# type emitter does not emit the op vocabulary yet (Phase 703 leaves that leg unshipped): %A"
                t
        | TList inner -> fsType inner + " list"
        // Closure / opaque fields carry no observable data — the generated structural layer is
        // ENCODER-ONLY and `'Msg`-erased (Phase 317 real-tier boundary): a function-typed field
        // (`Binding.Query`'s accessor, every `onChange` / `onClick`) and an `obj`-erased field
        // (`Sparkline.source`'s seq, `Select.value`) both collapse to `unit`. There is no host
        // behaviour or CLR shape to reconstruct here — the encoder emits the fixed `"<closure>"` /
        // `"<opaque>"` sentinel regardless of the (unit) value, so authoring stays trivial (`()`).
        // The real `Fuaran.UI` `Types.fs` keeps the `'Msg`-generic closures; the switch-over
        // re-attaches behaviour on the domain side (documented in docs/migrations/317-*).
        | TClosure -> "unit"
        | TOpaque -> "unit"
        // Phase 689 — the declared host signature, verbatim. This is the whole
        // difference from `TClosure`, and the reason the generated layer can be
        // the authoring type: the encoder never reads the value, so the slot's
        // host type was always free.
        | TFn s -> "(" + s.FSharp + ")"
        // Phase 676 — a JSON slot is a real `JVal`, NOT erased to `unit`: it carries
        // data in both directions, which is the whole difference from `TOpaque`.
        | TJson -> "JVal"
        // A hosted slot declares the real host type — that is its whole point.
        | THosted h -> h.FSharp
        | TRecord n -> applied n []
        | TMap vt -> "Map<string, " + fsType vt + ">"

    /// Phase 945 — a per-kind HOST PROJECTION: the F# record, encoder and decoder for one
    /// node kind are supplied verbatim instead of being derived from the kind's IDL fields.
    /// The IDL fields REMAIN the wire truth (the artifact, the schema and the diff still
    /// read them); the projection changes only what the generated F# looks like — the
    /// escape for a kind whose ergonomic host shape is narrower than its wire shape
    /// (`Switch` merges the `on` / `stateKey` wire keys into one required `On` binding).
    type KindProjection =
        {
            /// Keyword-less type-group member body — the `and` keyword and RQA attribute
            /// are the assembler's.
            SpecDecl: string
            /// The full `and private enc<Tag>Spec …` member, verbatim.
            Encoder: string
            /// The full `and private dec<Tag>Spec …` member, verbatim.
            Decoder: string
            /// The full `let mk<Tag> …` smart constructor, or None to emit none — the
            /// generated ctor would construct the IDL-derived record, which under a
            /// projection is not the record that exists.
            Mk: string option
        }

    /// Phase 945 — the declared-support channel for `fsharpModuleWith`: doc comments,
    /// verbatim splices and host projections that were previously HAND-EDITS to the
    /// generated artefact (the "hand-added ahead of the IDL backfill" regions). Everything
    /// here is versioned data beside the IDL, so the emission is reproducible and the
    /// tier sync is a byte-copy again. `Docs` is keyed by declaration path —
    /// `type:Name` / `case:Union.Tag` / `field:Owner.Field` / `enc:Name` / `dec:Name` /
    /// `encarm:Union.Tag` / `decarm:Union.Tag` — each value the comment lines VERBATIM
    /// (including their `///` or `//` markers), indented by the emitter.
    type GenSupport =
        {
            Docs: Map<string, string list>
            /// Verbatim `and …` member(s) appended to the type-recursion group.
            TypeSplice: string option
            /// Verbatim `and private …` member(s) appended to the encoder group.
            EncodeSplice: string option
            /// Verbatim `and private …` member(s) appended to the decoder group.
            DecodeSplice: string option
            /// Verbatim module-level lets emitted after the JVal accessor block.
            AccessorSplice: string option
            /// `"Union.Tag"` → the full final expression replacing `Ok(Case(…))` in that
            /// case's decoder — decode POLICY (e.g. `SetState`'s value-XOR-valueFrom) that
            /// the structural inversion cannot express. Field binder names are in scope.
            CaseRefines: Map<string, string>
            KindProjections: Map<string, KindProjection>
        }

        static member Empty =
            { Docs = Map.empty
              TypeSplice = None
              EncodeSplice = None
              DecodeSplice = None
              AccessorSplice = None
              CaseRefines = Map.empty
              KindProjections = Map.empty }

    // -----------------------------------------------------------------------
    // Phase 113 — declared annotations, rendered into the generated F#.
    //
    // Two surfaces, and they answer different questions. The `///` block is for a
    // reader: it says what is true about the member and what to do instead. The
    // `System.Obsolete` attribute is for the compiler: it makes the fact reach a
    // consumer who never opens the generated file.
    // -----------------------------------------------------------------------

    /// An F# string literal — annotation prose reaches the generated source through
    /// an attribute argument, so a quote or backslash in it must not end the literal
    /// and a newline must not split the attribute across lines.
    let private fsAttrStr (s: string) : string =
        let b = System.Text.StringBuilder()
        b.Append('"') |> ignore

        for ch in s do
            match ch with
            | '"' -> b.Append("\\\"") |> ignore
            | '\\' -> b.Append("\\\\") |> ignore
            | '\n' -> b.Append("\\n") |> ignore
            | '\r' -> b.Append("\\r") |> ignore
            | '\t' -> b.Append("\\t") |> ignore
            | c -> b.Append(c) |> ignore

        b.Append('"').ToString()

    /// A declared string slot that actually says something. An annotation authored
    /// with an empty or whitespace replacement / message / version reads as UNSAID
    /// rather than emitting `use `` instead` into the generated source — refusing
    /// garbage at the point of emission is cheaper than a validator every caller has
    /// to remember to run, and there is nothing else the slot could have meant.
    let private said (v: string option) : string option =
        v |> Option.filter (fun x -> not (System.String.IsNullOrWhiteSpace x))

    /// The `///` doc lines for an annotation set, at the given indent. Empty for an
    /// empty set, which is what keeps an unannotated vocabulary's emission
    /// byte-identical.
    let private annotationDocLines (indent: string) (a: Annotations) : string list =
        [ match a.Deprecated with
          | Some d ->
              let replacement =
                  match said d.Replacement with
                  | Some r -> sprintf " Use `%s` instead." r
                  | None -> ""

              indent + "/// **Deprecated.**" + replacement

              match said d.Message with
              | Some m -> indent + "/// " + m
              | None -> ()
          | None -> ()

          if a.InProcessOnly then
              indent
              + "/// **In-process only** — this member has no wire projection: a value here"

              indent
              + "/// is carried inside one host process and is LOST across any wire boundary."

          match said a.Since with
          | Some v -> indent + sprintf "/// Since `%s`." v
          | None -> () ]

    /// The single `System.Obsolete` attribute an annotation set earns, or `None`.
    ///
    /// **One attribute, never two.** `System.ObsoleteAttribute` is not
    /// `AllowMultiple`, so a member that is both deprecated and in-process-only
    /// composes into one message; a second attribute would not compile.
    ///
    /// **`isError = false` is load-bearing.** The generated layer must not decide
    /// for its host that touching a marked member fails the build: FS0044 is a
    /// warning the host escalates (`--warnaserror:44`) or silences (`--nowarn:44`)
    /// on its own release schedule. An unconditional error would make the
    /// two-release retirement this annotation exists to enable impossible to run —
    /// the release that MARKS the member could never ship.
    ///
    /// `Since` earns no attribute: it is a fact about the past, and there is nothing
    /// for a compiler to say about it.
    let private obsoleteAttr (a: Annotations) : string option =
        let parts =
            [ match a.Deprecated with
              | Some d ->
                  "deprecated"
                  + (match said d.Replacement with
                     | Some r -> " — use `" + r + "` instead"
                     | None -> "")
                  + (match said d.Message with
                     | Some m -> ": " + m
                     | None -> "")
              | None -> ()

              if a.InProcessOnly then
                  "in-process only — no wire projection; a value here is lost across a wire boundary" ]

        match parts with
        | [] -> None
        | ps -> Some(sprintf "[<System.Obsolete(%s, false)>]" (fsAttrStr (String.concat "; " ps)))

    /// Whether ANY declaration in the vocabulary earns an `Obsolete` attribute — the
    /// condition for the generated module's `#nowarn "44"`. The generated structural
    /// layer constructs and matches every declared member, marked ones included, so
    /// the warning it raises is for CONSUMERS of the layer and never for the layer
    /// itself; without the suppression a vocabulary could not mark anything without
    /// making its own generated codec noisy.
    let private emitsObsolete (idl: Idl) : bool =
        let fieldSets =
            [ idl.NodeFields
              yield! idl.Kinds |> List.map _.Fields
              yield! idl.Ops |> List.map _.Fields
              yield! idl.Records |> List.map _.Fields
              yield! idl.Unions |> List.collect (fun u -> u.Cases |> List.map _.Fields) ]

        (idl.Unions
         |> List.exists (fun u -> u.Cases |> List.exists (fun c -> (obsoleteAttr c.Annotations).IsSome)))
        || (fieldSets
            |> List.exists (List.exists (fun f -> (obsoleteAttr f.Annotations).IsSome)))

    let private fsField (msg: Set<string>) (f: IdlField) =
        let fsType = fsTypeIn msg

        let ty =
            match f.Opt with
            | Optional -> fsType f.Type + " option"
            // OmitDefault fields always carry a value (the default is restored on
            // absence at decode) — a non-option field, like Required. HostOnly takes
            // the declared type verbatim: its `TFn` signature already says whether it
            // is an option (`Motion option`) or a bare value (`'Msg`).
            | Required
            | HostOnly
            | OmitDefault _ -> fsType f.Type

        // Phase 113 — a record field's attribute sits on its own line above it.
        let prefix =
            (annotationDocLines "      " f.Annotations
             @ (obsoleteAttr f.Annotations
                |> Option.map (fun a -> "      " + a)
                |> Option.toList))
            |> List.map (fun l -> l + "\n")
            |> String.concat ""

        prefix + sprintf "      %s: %s" (pascal f.Name) ty

    let private enumDecl (e: IdlEnum) =
        sprintf "type %s =\n%s" e.Name (e.Cases |> List.map (sprintf "    | %s") |> String.concat "\n")

    let private unionCaseDecl (msg: Set<string>) (c: IdlUnionCase) =
        let fsType = fsTypeIn msg

        let fieldDecl (f: IdlField) =
            let ty =
                match f.Opt with
                | Optional -> fsType f.Type + " option"
                | Required
                | HostOnly
                | OmitDefault _ -> fsType f.Type

            sprintf "%s: %s" (ident f.Name) ty

        let fields = c.Fields |> List.map fieldDecl |> String.concat " * "

        let body =
            if fields = "" then
                c.Tag
            else
                sprintf "%s of %s" c.Tag fields

        // Phase 113 — a union case's attribute sits INLINE after the bar
        // (`| [<Obsolete(...)>] Tag of ...`), which is where F# accepts it; the doc
        // block sits above the bar, like any other.
        let docs =
            annotationDocLines "    " c.Annotations
            |> List.map (fun l -> l + "\n")
            |> String.concat ""

        let attr =
            match obsoleteAttr c.Annotations with
            | Some a -> a + " "
            | None -> ""

        docs + "    | " + attr + body

    let private unionDecl (msg: Set<string>) (u: IdlUnion) =
        sprintf
            "type %s%s =\n%s"
            u.Name
            (declParams msg u.Name u.Params)
            (u.Cases |> List.map (unionCaseDecl msg) |> String.concat "\n")

    let private kindDecl (msg: Set<string>) (k: IdlKind) =
        sprintf
            "// %s\ntype %sSpec%s =\n    {\n%s\n    }"
            k.Category
            k.Tag
            (declParams msg (k.Tag + "Spec") [])
            (k.Fields |> List.map (fsField msg) |> String.concat "\n")

    /// Emit F# type declarations (enums, value-unions, per-kind spec records) from the IDL.
    let fsharpTypes (idl: Idl) : string =
        let msg = msgCarrying idl

        [ idl.Enums |> List.map enumDecl
          idl.Unions |> List.map (unionDecl msg)
          idl.Kinds |> List.map (kindDecl msg) ]
        |> List.concat
        |> String.concat "\n\n"

    // -----------------------------------------------------------------------
    // Phase 317 increment 3 — *feature-complete* code emission: emit a
    // self-contained, compiling F# encoder module for a set of kinds, handling
    // every feature class the interpreter does — Required + Optional fields
    // (omit-on-absence via List.choose), parameterised unions (`Binding<'T>`, by
    // codec-passing), lists, and node nesting (a recursive `encNode`). The proof
    // that the generator — not just the interpreter — covers the whole surface.
    // -----------------------------------------------------------------------

    /// Point-free encoder *function* for a type (`'a -> JVal`) — used where an encoder
    /// must be passed (a generic union's type-parameter codec).
    let rec private encFn (t: IdlType) : string =
        match t with
        | TStr -> "JStr"
        | TInt -> "JInt"
        | TBool -> "JBool"
        // NOT the bare `JFloat` constructor: a non-finite double has no JSON number
        // spelling and rides as a quoted sentinel string (`encodeHelpers` below).
        // `TInt` keeps its constructor — WIRE_FORMAT §7 truncates at a float slot.
        | TFloat -> "encFloat"
        | TEnum n -> "enc" + n
        | TVar v -> "enc" + v
        | TUnion(n, []) -> "enc" + n
        | TUnion(n, args) -> "(enc" + n + " " + (args |> List.map encFn |> String.concat " ") + ")"
        | TNode -> "encNode"
        // Phase 703 models the OP vocabulary and certifies the interpreter leg
        // against the corpus; emitting an op family from the F# encoder emitter is a separate,
        // larger piece of work (`TreeOp` is msg-carrying through `TKind`/`TNode`,
        // so it lands as a generic type group). Nothing walks `idl.Ops` in this
        // backend yet, so these arms are unreachable today — explicit and loud so
        // that wiring ops in gets a precise signal instead of a match failure.
        | TKind
        | TOp ->
            failwithf
                "the F# encoder emitter does not emit the op vocabulary yet (Phase 703 leaves that leg unshipped): %A"
                t
        | TList inner -> sprintf "(fun __xs -> JArr(List.map %s __xs))" (encFn inner)
        // A closure/opaque codec ignores its argument and emits the fixed sentinel.
        | TClosure
        | TFn _ -> "(fun _ -> JStr \"<closure>\")"
        | TOpaque -> "(fun _ -> JStr \"<opaque>\")"
        // Phase 676 — verbatim passthrough. `Canon.render` already sorts keys Ordinal,
        // escapes per rule 6 and lays floats out per rule 5, so identity inherits all
        // three rather than re-implementing them — the risk this phase named.
        | TJson -> "id"
        // The named host encode expression, verbatim ('host -> JVal). Canonicality is
        // inherited: the host codec builds a JVal that renders through the same Canon.
        | THosted h -> h.Encode
        | TRecord n -> "enc" + n
        | TMap vt -> sprintf "(fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, %s v)))" (encFn vt)

    /// The applied JVal expression for a value of `t` bound to `var`.
    let private encApplied (var: string) (t: IdlType) : string =
        match t with
        | TList inner -> sprintf "JArr(List.map %s %s)" (encFn inner) var
        | TNode -> sprintf "encNode %s" var
        | TClosure
        | TFn _ -> "JStr \"<closure>\""
        | TOpaque -> "JStr \"<opaque>\""
        | _ -> sprintf "%s %s" (encFn t) var

    /// The encode-side helper prelude, emitted once per module — the mirror of
    /// [[decodeHelpers]]. Only the float slot needs one: every other primitive is
    /// its `JVal` constructor.
    let private encodeHelpers (idl: Idl) : string =
        let base' =
            """// WIRE_FORMAT §5 — a non-finite double has no JSON *number* spelling, so it rides as
// one of the three quoted sentinel strings, which §7 requires a decoder to read back
// AT A FLOAT SLOT (`dFloat` below; `dInt` is deliberately not widened — §7 stops at
// the float slot, and an integer slot has no sentinel).
//
// Building the `JStr` HERE rather than leaving `Canon.render` to spell a non-finite
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
        // helper (its call sites reference `Canon.typed`), so its module is
        // byte-identical to every pre-declarable emission.
        if idl.Wire.Discriminator = "$type" then
            base'
        else
            base'
            + sprintf
                "\n\n// Phase 108 — `Canon.typed` under this vocabulary's DECLARED discriminator key.\nlet private typedTag (tag: string) (fields: (string * JVal) list) : JVal =\n    JObj((\"%s\", JStr tag) :: fields)"
                idl.Wire.Discriminator

    /// The host case name behind a `VEnum`'s WIRE string (Phase 707). `VEnum`
    /// carries the wire form like every other `IdlValue` case, so the F# emitter —
    /// alone among the backends — has to map back to the identifier it declared.
    /// Falls through to the wire string when the enum is unknown or declares no
    /// mapping, which is the identity every pre-707 declaration already had.
    let private fsEnumCase (enums: IdlEnum list) (enumName: string) (wire: string) : string =
        enums
        |> List.tryFind (fun e -> e.Name = enumName)
        |> Option.bind _.CaseOf(wire)
        |> Option.defaultValue wire

    /// The F# literal for an omit-when-default field's identity default — enums
    /// (`ToneVariant.Default`), nullary unions (`CellFormat.None`), booleans, and
    /// the EMPTY LIST (Phase 1080). `None` ⇒ the emitter can't render it, and the
    /// encoder falls back to always-emit.
    ///
    /// The empty-list case is the one default whose *absence* is expressible on the
    /// wire without ambiguity in the other direction: an absent list slot and an
    /// empty one denote the same document, so a repeated slot that is empty by
    /// default costs a key on every node that does not use it. `[]` is a legal F#
    /// literal at any list type and list equality is structural, so both halves —
    /// the encoder's `= []` omit test and the decoder's restore — fall out of the
    /// same one-line literal the enum case does.
    let private fsDefaultLit (enums: IdlEnum list) (t: IdlType) (v: IdlValue) : string option =
        match t, v with
        | TEnum n, VEnum c -> Some(n + "." + fsEnumCase enums n c)
        | TUnion(n, _), VUnion(tag, []) -> Some(n + "." + tag)
        | TBool, VBool b -> Some(if b then "true" else "false")
        | TList _, VList [] -> Some "[]"
        | _ -> None

    /// One field of a record-spec encoder, as a `(string * JVal) option` for `List.choose id`
    /// (Required → always `Some`; Optional → omit-on-`None`; OmitDefault → omit-at-default).
    /// The F# expression a HostOnly field takes on decode — its `TFn` placeholder.
    /// A host-only field must be a `TFn`, because that is what carries both the
    /// declared host type and the value to restore; anything else is an IDL defect
    /// the generator refuses rather than guesses at.
    let private hostOnlyLit (f: IdlField) : string =
        match f.Type with
        | TFn sg -> sg.Placeholder
        | _ -> failwithf "field '%s' is HostOnly but not a TFn — it declares no host type or placeholder" f.Name

    /// `recv` is the bound record variable — `s` for a spec/record encoder, `n` for
    /// the node envelope (Phase 690), which reuses this presence machinery unchanged.
    let private specPieceOf (enums: IdlEnum list) (recv: string) (f: IdlField) : string =
        let src = recv + "." + pascal f.Name

        match f.Opt with
        | Required -> sprintf "Some(\"%s\", %s)" f.Name (encApplied src f.Type)
        | Optional -> sprintf "(%s |> Option.map (fun v -> \"%s\", %s))" src f.Name (encApplied "v" f.Type)
        // Phase 691 — never on the wire, in any state.
        | HostOnly -> "None"
        | OmitDefault d ->
            match fsDefaultLit enums f.Type d with
            // A UNION default is tested by pattern-match, not `=`. Phase 691: typing a
            // closure slot gives its owning union a function-typed field, and F#
            // functions support no equality, so the union stops supporting the
            // `equality` constraint entirely — `CellFormat.Custom of (obj -> string)`
            // broke `s.Format = CellFormat.None` for every column. A match is also
            // simply the better test: it needs no constraint, and reads as what it is.
            | Some dexpr when
                (match f.Type with
                 | TUnion _ -> true
                 | _ -> false)
                ->
                sprintf "(match %s with | %s -> None | _ -> Some(\"%s\", %s))" src dexpr f.Name (encApplied src f.Type)
            // Phase 1080 — a LIST default is tested with `List.isEmpty`, never with
            // `= []`, and for the same reason the union arm above exists: an element
            // type that reaches a closure carries no equality, so `SrcSetEntry`
            // (whose `src` is a `Binding`) fails the constraint the moment the
            // generated encoder is compiled. `List.isEmpty` imposes none, and it is
            // the better test anyway — it says what it means.
            | Some _ when
                (match f.Type with
                 | TList _ -> true
                 | _ -> false)
                ->
                sprintf "(if List.isEmpty %s then None else Some(\"%s\", %s))" src f.Name (encApplied src f.Type)
            | Some dexpr ->
                sprintf "(if %s = %s then None else Some(\"%s\", %s))" src dexpr f.Name (encApplied src f.Type)
            | None -> sprintf "Some(\"%s\", %s)" f.Name (encApplied src f.Type)

    /// One `"key", <enc>` pair of a *required* union-case field (positional binding; the wire
    /// key is the raw field name, the value reference is keyword-escaped).
    let private casePair (f: IdlField) : string =
        sprintf "\"%s\", %s" f.Name (encApplied (ident f.Name) f.Type)

    /// One `(string * JVal) option` piece of a union-case encoder — `Some` for a required field,
    /// omit-on-`None` for an optional one (`CellFormat.Number`'s `decimals`, `Format.Percent`,
    /// `FormFieldKind.RangedNumber`'s `min`/`max`/`step`, `HoleDecl.Value`'s `default`). Mirrors
    /// [[specPieceOf]] for the `List.choose id` shape, but binds the *positional* case field.
    let private casePiece (enums: IdlEnum list) (f: IdlField) : string =
        let src = ident f.Name

        match f.Opt with
        | Required -> sprintf "Some(\"%s\", %s)" f.Name (encApplied src f.Type)
        | Optional -> sprintf "(%s |> Option.map (fun v -> \"%s\", %s))" src f.Name (encApplied "v" f.Type)
        // Phase 691 — never on the wire, in any state.
        | HostOnly -> "None"
        | OmitDefault d ->
            match fsDefaultLit enums f.Type d with
            // A UNION default is tested by pattern-match, not `=`. Phase 691: typing a
            // closure slot gives its owning union a function-typed field, and F#
            // functions support no equality, so the union stops supporting the
            // `equality` constraint entirely — `CellFormat.Custom of (obj -> string)`
            // broke `s.Format = CellFormat.None` for every column. A match is also
            // simply the better test: it needs no constraint, and reads as what it is.
            | Some dexpr when
                (match f.Type with
                 | TUnion _ -> true
                 | _ -> false)
                ->
                sprintf "(match %s with | %s -> None | _ -> Some(\"%s\", %s))" src dexpr f.Name (encApplied src f.Type)
            // Phase 1080 — a LIST default is tested with `List.isEmpty`, never with
            // `= []`, and for the same reason the union arm above exists: an element
            // type that reaches a closure carries no equality, so `SrcSetEntry`
            // (whose `src` is a `Binding`) fails the constraint the moment the
            // generated encoder is compiled. `List.isEmpty` imposes none, and it is
            // the better test anyway — it says what it means.
            | Some _ when
                (match f.Type with
                 | TList _ -> true
                 | _ -> false)
                ->
                sprintf "(if List.isEmpty %s then None else Some(\"%s\", %s))" src f.Name (encApplied src f.Type)
            | Some dexpr ->
                sprintf "(if %s = %s then None else Some(\"%s\", %s))" src dexpr f.Name (encApplied src f.Type)
            | None -> sprintf "Some(\"%s\", %s)" f.Name (encApplied src f.Type)

    let private enumEncoder (e: IdlEnum) =
        // The case name is the F# identifier, the wire string is what goes on the
        // wire — identical unless the enum declares a mapping (Phase 707).
        let arms =
            e.Cases
            |> List.map (fun c -> sprintf "    | %s.%s -> JStr \"%s\"" e.Name c (e.WireOf c))
            |> String.concat "\n"

        sprintf "let private enc%s (v: %s) : JVal =\n    match v with\n%s" e.Name e.Name arms

    /// A union encoder (an `and`-member of the recursive group). Generic unions take one
    /// `encX : 'X -> JVal` codec per type parameter. `typedName` is the emitted
    /// discriminated-object builder — `Canon.typed` on a default-shape IDL
    /// (byte-identical to every pre-declarable emission), the module-local
    /// `typedTag` when the vocabulary declares another key (Phase 108).
    let private unionEncoder
        (typedName: string)
        (docFn: string -> string -> string)
        (msg: Set<string>)
        (enums: IdlEnum list)
        (u: IdlUnion)
        : string =
        let encArgs =
            u.Params
            |> List.map (fun p -> sprintf " (enc%s: '%s -> JVal)" p p)
            |> String.concat ""

        let tyArgs = declParams msg u.Name u.Params

        let arm (c: IdlUnionCase) =
            let pat =
                match c.Fields with
                | [] -> ""
                | [ f ] -> " " + ident f.Name
                | fs -> " (" + (fs |> List.map (fun f -> ident f.Name) |> String.concat ", ") + ")"

            // A transparent case (TextSource.Literal) emits its single field's value BARE — no
            // `Canon.typed` wrapper — the Fuaran-UI 0.2.0 bare-string canonical literal.
            match TransparentUnion.tag u with
            | Some ttag when ttag = c.Tag ->
                match c.Fields with
                | [ f ] -> sprintf "    | %s.%s%s -> %s" u.Name c.Tag pat (encApplied (ident f.Name) f.Type)
                | _ -> failwithf "transparent union case '%s' must have exactly one field" c.Tag
            | _ ->
                // All-required cases keep the simple literal list (byte-identical to the pre-optional
                // emission); any optional field switches to the `List.choose id` omit-on-absence form.
                if c.Fields |> List.forall (fun f -> f.Opt = Required) then
                    let pairs = c.Fields |> List.map casePair |> String.concat "; "
                    sprintf "    | %s.%s%s -> %s \"%s\" [ %s ]" u.Name c.Tag pat typedName c.Tag pairs
                else
                    let pieces = c.Fields |> List.map (casePiece enums) |> String.concat "; "

                    sprintf
                        "    | %s.%s%s -> %s \"%s\" ([ %s ] |> List.choose id)"
                        u.Name
                        c.Tag
                        pat
                        typedName
                        c.Tag
                        pieces

        let arms =
            u.Cases
            |> List.map (fun c -> docFn ("encarm:" + u.Name + "." + c.Tag) "    " + arm c)
            |> String.concat "\n"
        // The explicit `<'T>` type-parameter list (not just `'T` free in the signature) is
        // load-bearing for a generic union: `Binding.Format.source` is a fixed `Binding<float>`
        // *inside* `Binding<'T>`, so the generated `encBinding` recurses at a concrete type ≠ the
        // ambient `'T` — **polymorphic recursion**, which F# permits only under an explicit
        // generic-parameter declaration. Without it, `encBinding` monomorphises to the first use
        // (string) and the `float` recursion fails to type-check.
        sprintf
            "and private enc%s%s%s (v: %s%s) : JVal =\n    match v with\n%s"
            u.Name
            tyArgs
            encArgs
            u.Name
            tyArgs
            arms

    let private specEncoder (typedName: string) (msg: Set<string>) (enums: IdlEnum list) (k: IdlKind) : string =
        // Single-line list literal — avoids F# offside-rule pitfalls in generated code.
        let pieces = k.Fields |> List.map (specPieceOf enums "s") |> String.concat "; "

        sprintf
            "and private enc%sSpec%s (s: %sSpec%s) : JVal =\n    %s \"%s\" ([ %s ] |> List.choose id)"
            k.Tag
            (declParams msg (k.Tag + "Spec") [])
            k.Tag
            (declParams msg (k.Tag + "Spec") [])
            typedName
            k.Tag
            pieces

    /// A non-discriminated *record* encoder — a plain `JObj` (no `$type`), fields via `List.choose
    /// id` (omit-on-absence for optionals). `Canon.render` Ordinal-sorts keys, so emission order is
    /// irrelevant. Reuses [[specPieceOf]] (`s.<Pascal>` field access). New for the real tier
    /// (`InvokeArg`, `FormField`, `FilterSpec`, `TabHeader`, `ColumnErased`, `ContentHash`, …).
    let private recordEncoder (msg: Set<string>) (enums: IdlEnum list) (r: IdlRecord) : string =
        let pieces = r.Fields |> List.map (specPieceOf enums "s") |> String.concat "; "
        let ps = declParams msg r.Name []
        sprintf "and private enc%s%s (s: %s%s) : JVal =\n    JObj([ %s ] |> List.choose id)" r.Name ps r.Name ps pieces

    // -----------------------------------------------------------------------
    // Phase 672 — the structural DECODER leg.
    //
    // The inverse of the encoder emitters above, and deliberately only the
    // STRUCTURAL half: which `$type` maps to which case, which fields, which
    // types. The decode-side *policy* a schema cannot describe — the canonical
    // diagnostic codes with `$`-rooted paths, §16 lenient-accept normalisation,
    // and the reject set — stays hand-written ABOVE this, exactly as the
    // `'Msg`-generic author facades sit above the generated encoder. So the
    // generated error is a plain string: enough to locate a structural fault,
    // deliberately not competing with the hand-written envelope.
    //
    // Two inversions are NOT symmetric with the encoder, and are the ones to get
    // right:
    //   * §16 sentinel omission — the no-information closure sentinels
    //     (`Binding.Query.accessor`, `Selection.accessor`, `Action.Dispatch.msg`)
    //     are OFF the wire, so a `TClosure`/`TOpaque` field decodes from
    //     *absence* and must never look for its key.
    //   * a whole-valued float renders without a decimal point, so it parses
    //     back as `JInt` — `dFloat` accepts both.
    // -----------------------------------------------------------------------

    /// The decoder expression for a type — a `JVal -> Result<'T, string>`.
    /// Mirrors [[encFn]] arm for arm.
    let rec private decFn (t: IdlType) : string =
        match t with
        | TStr -> "dStr"
        | TInt -> "dInt"
        | TBool -> "dBool"
        | TFloat -> "dFloat"
        | TEnum n -> "dec" + n
        | TVar v -> "dec" + v
        | TUnion(n, []) -> "dec" + n
        | TUnion(n, args) -> "(dec" + n + " " + (args |> List.map decFn |> String.concat " ") + ")"
        | TNode -> "decNode"
        // Phase 703 models the OP vocabulary and certifies the interpreter leg
        // against the corpus; emitting an op family from the F# decoder emitter is a separate,
        // larger piece of work (`TreeOp` is msg-carrying through `TKind`/`TNode`,
        // so it lands as a generic type group). Nothing walks `idl.Ops` in this
        // backend yet, so these arms are unreachable today — explicit and loud so
        // that wiring ops in gets a precise signal instead of a match failure.
        | TKind
        | TOp ->
            failwithf
                "the F# decoder emitter does not emit the op vocabulary yet (Phase 703 leaves that leg unshipped): %A"
                t
        | TList inner -> sprintf "(dList %s)" (decFn inner)
        | TClosure
        | TOpaque -> "dUnit"
        // Phase 689 — a `TFn` slot decodes to its declared placeholder. There is
        // nothing on the wire to rebuild a closure from, so the decoded tree is the
        // storage shape and the placeholder is what a host re-attaches over.
        | TFn s -> sprintf "(fun _ -> Ok (%s))" s.Placeholder
        // Phase 676 — accept any JSON verbatim; a shape check would contradict the
        // field's contract.
        | TJson -> "dJson"
        // The named host decode expression, verbatim (JVal -> Result<'host, string>).
        | THosted h -> h.Decode
        | TRecord n -> "dec" + n
        | TMap vt -> sprintf "(dMap %s)" (decFn vt)

    /// Reading one field back out, honouring the presence rules [[specPieceOf]] /
    /// [[casePiece]] wrote it under.
    let private decField (enums: IdlEnum list) (f: IdlField) : string =
        match f.Type with
        | TClosure
        | TOpaque ->
            // The VALUE is a sentinel and carries nothing, so it is never read. But an
            // OPTIONAL sentinel field's PRESENCE is real wire information — the encoder
            // omits the key when `None` and emits the sentinel when `Some ()`. Reading
            // presence is what makes the decode a structural inverse; a flat `Ok None`
            // silently drops the field (caught by the corpus round-trip gate on
            // `grid-1`'s optional `rowKey`).
            match f.Opt with
            | Optional -> sprintf "dPresent \"%s\" __fs" f.Name
            | _ -> "Ok()"
        // Phase 689 — same presence rule, but the slot is typed, so the value put
        // back is the declared placeholder rather than `()`.
        | TFn s ->
            match f.Opt with
            | Optional ->
                sprintf "(dPresent \"%s\" __fs |> Result.map (Option.map (fun () -> %s)))" f.Name s.Placeholder
            | _ -> sprintf "Ok (%s)" s.Placeholder
        | _ ->
            match f.Opt with
            | Required -> sprintf "dReq \"%s\" __fs %s" f.Name (decFn f.Type)
            | Optional -> sprintf "dOpt \"%s\" __fs %s" f.Name (decFn f.Type)
            // Never on the wire — nothing to read, so take the declared placeholder.
            | HostOnly -> sprintf "Ok (%s)" (hostOnlyLit f)
            | OmitDefault d ->
                match fsDefaultLit enums f.Type d with
                | Some dexpr -> sprintf "dDef \"%s\" __fs %s (%s)" f.Name (decFn f.Type) dexpr
                // The encoder fell back to always-emit, so decode is required too.
                | None -> sprintf "dReq \"%s\" __fs %s" f.Name (decFn f.Type)

    /// Nest one `Result.bind` per field over `final`, then close the lot. F# has
    /// no applicative sugar for this, and the generated file is Fantomas-exempt,
    /// so the nesting is emitted explicitly rather than prettified.
    let private bindChain
        (indent: string)
        (binders: (string * string) list)
        (final: string)
        (extraCloses: int)
        : string =
        let opens =
            binders
            |> List.map (fun (v, e) -> sprintf "%s%s |> Result.bind (fun %s ->" indent e v)

        let closes = String.replicate (List.length binders + extraCloses) ")"
        (opens @ [ indent + final + closes ]) |> String.concat "\n"

    let private fieldBinders (enums: IdlEnum list) (fs: IdlField list) =
        fs |> List.map (fun f -> ident f.Name, decField enums f)

    let private enumDecoder (e: IdlEnum) =
        let arms =
            e.Cases
            |> List.map (fun c -> sprintf "    | JStr \"%s\" -> Ok %s.%s" (e.WireOf c) e.Name c)
            |> String.concat "\n"

        sprintf
            "let private dec%s (j: JVal) : Result<%s, string> =\n    match j with\n%s\n    | _ -> Error \"not a %s\""
            e.Name
            e.Name
            arms
            e.Name

    /// A union decoder. Generic unions take one `decX` codec per type parameter,
    /// with the explicit type-parameter list [[unionEncoder]] needs for the same
    /// polymorphic-recursion reason (`Binding.Format.source` recurses at `float`).
    let private unionDecoder
        (docFn: string -> string -> string)
        (refines: Map<string, string>)
        (disc: string)
        (msg: Set<string>)
        (enums: IdlEnum list)
        (u: IdlUnion)
        : string =
        let decArgs =
            u.Params
            |> List.map (fun p -> sprintf " (dec%s: JVal -> Result<'%s, string>)" p p)
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

        let arm (c: IdlUnionCase) =
            // Phase 945 — a declared refine replaces the plain `Ok(Case(…))` final with a
            // policy expression (field binder names in scope); the binder chain around it
            // is untouched, so a refine cannot change WHICH fields decode, only what is
            // accepted once they have.
            let final =
                match refines.TryFind(u.Name + "." + c.Tag) with
                | Some r -> r
                | None -> sprintf "Ok(%s)" (ctor c)

            let body =
                if List.isEmpty c.Fields then
                    sprintf "        | \"%s\" -> Ok %s" c.Tag (ctor c)
                else
                    sprintf
                        "        | \"%s\" ->\n%s"
                        c.Tag
                        (bindChain "            " (fieldBinders enums c.Fields) final 0)

            docFn ("decarm:" + u.Name + "." + c.Tag) "        " + body

        // The transparent case (TextSource.Literal) is on the wire BARE, so it is
        // recognised by the ABSENCE of a `$type`, not by a tag.
        let transparent =
            match TransparentUnion.tag u with
            | Some ttag ->
                match u.Cases |> List.tryFind (fun c -> c.Tag = ttag) with
                | Some c when c.Fields.Length = 1 ->
                    let f = c.Fields.Head

                    Some(
                        sprintf
                            "    | __bare ->\n        %s __bare |> Result.bind (fun %s -> Ok(%s))"
                            (decFn f.Type)
                            (ident f.Name)
                            (ctor c)
                    )
                | _ -> None
            | None -> None

        let tagged =
            let arms = u.Cases |> List.map arm |> String.concat "\n"

            sprintf
                "    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = \"%s\")) ->\n        dTag __fs |> Result.bind (fun __t ->\n        match __t with\n%s\n        | __other -> Error (\"unknown %s case: \" + __other))"
                disc
                arms
                u.Name

        let fallthrough =
            match transparent with
            | Some t -> t
            | None -> sprintf "    | _ -> Error \"expected a %s object\"" u.Name

        sprintf
            "and private dec%s%s%s (j: JVal) : Result<%s%s, string> =\n    match j with\n%s\n%s"
            u.Name
            declArgs
            decArgs
            u.Name
            tyArgs
            tagged
            fallthrough

    let private specDecoder (msg: Set<string>) (enums: IdlEnum list) (k: IdlKind) : string =
        let assigns =
            k.Fields
            |> List.map (fun f -> sprintf "%s = %s" (pascal f.Name) (ident f.Name))
            |> String.concat "; "

        sprintf
            "and private dec%sSpec (j: JVal) : Result<%sSpec%s, string> =\n    dObj j |> Result.bind (fun __fs ->\n%s"
            k.Tag
            k.Tag
            (objParams msg (k.Tag + "Spec") [])
            (bindChain "    " (fieldBinders enums k.Fields) (sprintf "Ok { %s }" assigns) 1)

    let private recordDecoder (msg: Set<string>) (enums: IdlEnum list) (r: IdlRecord) : string =
        let assigns =
            r.Fields
            |> List.map (fun f -> sprintf "%s = %s" (pascal f.Name) (ident f.Name))
            |> String.concat "; "

        sprintf
            "and private dec%s (j: JVal) : Result<%s%s, string> =\n    dObj j |> Result.bind (fun __fs ->\n%s"
            r.Name
            r.Name
            (objParams msg r.Name [])
            (bindChain "    " (fieldBinders enums r.Fields) (sprintf "Ok { %s }" assigns) 1)

    /// The decode-side helper prelude, emitted once per module. `dTag` reads the
    /// DECLARED discriminator (Phase 108) — `"$type"` interpolates to exactly the
    /// pre-declarable bytes.
    let private decodeHelpers (disc: string) : string =
        String.concat
            "\n\n"
            [ "let private dObj (j: JVal) : Result<(string * JVal) list, string> =\n    match j with\n    | JObj fs -> Ok fs\n    | _ -> Error \"expected an object\""
              sprintf
                  "let private dTag (fs: (string * JVal) list) : Result<string, string> =\n    match fs |> List.tryFind (fun (k, _) -> k = \"%s\") with\n    | Some(_, JStr t) -> Ok t\n    | _ -> Error \"missing or non-string %s\""
                  disc
                  disc
              "let private dStr (j: JVal) : Result<string, string> =\n    match j with\n    | JStr s -> Ok s\n    | _ -> Error \"expected a string\""
              "let private dInt (j: JVal) : Result<int, string> =\n    match j with\n    | JInt i -> Ok i\n    | _ -> Error \"expected an int\""
              "let private dBool (j: JVal) : Result<bool, string> =\n    match j with\n    | JBool b -> Ok b\n    | _ -> Error \"expected a bool\""
              "// A whole-valued float renders without a decimal point, so it parses back as JInt.\n// WIRE_FORMAT §7 — a float slot also accepts the three quoted non-finite sentinels, which\n// is how §5 spells a number JSON has no literal for. The value decodes to the FLOAT, never\n// to the string: a host that answered the string would hand a consumer a different tree on\n// the second decode while the bytes stayed identical. `dInt` is NOT widened — §7 stops at\n// the float slot.\nlet private dFloat (j: JVal) : Result<float, string> =\n    match j with\n    | JFloat f -> Ok f\n    | JInt i -> Ok(float i)\n    | JStr \"NaN\" -> Ok System.Double.NaN\n    | JStr \"Infinity\" -> Ok System.Double.PositiveInfinity\n    | JStr \"-Infinity\" -> Ok System.Double.NegativeInfinity\n    | _ -> Error \"expected a number\""
              "let private dUnit (_: JVal) : Result<unit, string> = Ok()"
              "// Phase 676 — arbitrary JSON, kept verbatim. No shape check: the field's
// contract is that its content is not the schema's business.
let private dJson (j: JVal) : Result<JVal, string> = Ok j"
              "let private dList (dec: JVal -> Result<'T, string>) (j: JVal) : Result<'T list, string> =\n    match j with\n    | JArr xs ->\n        (Ok [], xs)\n        ||> List.fold (fun acc x ->\n            match acc with\n            | Error e -> Error e\n            | Ok items -> dec x |> Result.map (fun v -> v :: items))\n        |> Result.map List.rev\n    | _ -> Error \"expected an array\""
              "let private dMap (dec: JVal -> Result<'T, string>) (j: JVal) : Result<Map<string, 'T>, string> =\n    match j with\n    | JObj fs ->\n        (Ok [], fs)\n        ||> List.fold (fun acc (k, v) ->\n            match acc with\n            | Error e -> Error e\n            | Ok items -> dec v |> Result.map (fun d -> (k, d) :: items))\n        |> Result.map (List.rev >> Map.ofList)\n    | _ -> Error \"expected an object\""
              "let private dReq (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, string>) : Result<'T, string> =\n    match fs |> List.tryFind (fun (k, _) -> k = name) with\n    | Some(_, v) -> dec v\n    | None -> Error(\"missing required field '\" + name + \"'\")"
              "let private dOpt (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, string>) : Result<'T option, string> =\n    match fs |> List.tryFind (fun (k, _) -> k = name) with\n    | Some(_, v) -> dec v |> Result.map Some\n    | None -> Ok None"
              "let private dDef (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, string>) (dflt: 'T) : Result<'T, string> =\n    match fs |> List.tryFind (fun (k, _) -> k = name) with\n    | Some(_, v) -> dec v\n    | None -> Ok dflt"
              "// An optional closure / opaque field: the value is a sentinel carrying nothing,\n// but its PRESENCE distinguishes `Some ()` from `None` and must be read back.\nlet private dPresent (name: string) (fs: (string * JVal) list) : Result<unit option, string> =\n    Ok(fs |> List.tryFind (fun (k, _) -> k = name) |> Option.map (fun _ -> ()))" ]

    /// Transitive closure of the enum / union / record types referenced from a set of kinds —
    /// through union case fields, record fields, list elements, map value-types, and union
    /// type-args. Records ↔ unions are mutually recursive (`CellKindErased.ButtonGroup` holds a
    /// `ButtonGroupItem` record; `FormField` holds a `FormFieldKind` union), so the walk visits
    /// both. Returns each set filtered to IDL declaration order.
    let private referenced (idl: Idl) (kinds: IdlKind list) : IdlEnum list * IdlUnion list * IdlRecord list =
        let enums = System.Collections.Generic.HashSet<string>()
        let unions = System.Collections.Generic.HashSet<string>()
        let records = System.Collections.Generic.HashSet<string>()

        let rec visit (t: IdlType) =
            match t with
            | TEnum n -> enums.Add n |> ignore
            | TUnion(n, args) ->
                args |> List.iter visit

                if unions.Add n then
                    match idl.Unions |> List.tryFind (fun u -> u.Name = n) with
                    | Some u -> u.Cases |> List.iter (fun c -> c.Fields |> List.iter (fun f -> visit f.Type))
                    | None -> ()
            | TRecord n ->
                if records.Add n then
                    match idl.Records |> List.tryFind (fun r -> r.Name = n) with
                    | Some r -> r.Fields |> List.iter (fun f -> visit f.Type)
                    | None -> ()
            | TList inner -> visit inner
            | TMap vt -> visit vt
            // Phase 691 — a declared type NAMED IN A SIGNATURE is reachable. `TFn.FSharp`
            // is free text the walker cannot parse, so this searches it for the names the
            // IDL already declares. Crude, but self-limiting (it can only ever mark
            // something the IDL defines), and without it a type reached ONLY through a
            // signature — `Motion`, on the host-only node fields — is declared in the IDL
            // and then never emitted, so the generated module names a type it lacks.
            | TFn sg ->
                for e in idl.Enums do
                    if sg.FSharp.Contains e.Name then
                        enums.Add e.Name |> ignore

                for r in idl.Records do
                    if sg.FSharp.Contains r.Name then
                        visit (TRecord r.Name)

                for u in idl.Unions do
                    if sg.FSharp.Contains u.Name then
                        visit (TUnion(u.Name, []))
            // Same name-scan for a hosted slot: its type and codec expressions may
            // reference declared types AND generated codecs (`encRangePair`,
            // `decBinding`) — a type reached only that way must still be emitted.
            | THosted h ->
                let text = h.FSharp + " " + h.Encode + " " + h.Decode

                for e in idl.Enums do
                    if text.Contains e.Name then
                        enums.Add e.Name |> ignore

                for r in idl.Records do
                    if text.Contains r.Name then
                        visit (TRecord r.Name)

                for u in idl.Unions do
                    if text.Contains u.Name then
                        visit (TUnion(u.Name, []))
            | _ -> ()

        kinds |> List.iter (fun k -> k.Fields |> List.iter (fun f -> visit f.Type))
        // Phase 690 — the node envelope is a reachability ROOT too. Its records are
        // reachable from no kind (nothing nests a `SemanticStyle`), so walking only
        // the kinds emits a `Node` whose field types were never declared.
        idl.NodeFields |> List.iter (fun f -> visit f.Type)

        idl.Enums |> List.filter (fun e -> enums.Contains e.Name),
        idl.Unions |> List.filter (fun u -> unions.Contains u.Name),
        idl.Records |> List.filter (fun r -> records.Contains r.Name)

    // -----------------------------------------------------------------------
    // Phase 317 increment 5 — the Core witness-record leg. Emit a
    // `NodeWitness<Node, string>` for the generated `Node`, so the generated
    // structural layer plugs straight into `Fuaran.Core.Tree` / `.Validator` /
    // `.Observer` — the "serves every domain via the Core witness, not just UI"
    // promise. `Children` / `ReplaceChildren` are derived from the IDL: a field
    // is node-bearing iff its type is `TNode` or `TList TNode`.
    // -----------------------------------------------------------------------

    /// `Some (pascalName, isList)` when a field holds a `Node` (single) or a
    /// `Node list`; `None` otherwise.
    let private nodeBearing (f: IdlField) : (string * bool) option =
        match f.Type with
        | TNode -> Some(pascal f.Name, false)
        | TList TNode -> Some(pascal f.Name, true)
        | _ -> None

    /// Emit the `NodeWitness<Node, string>` + its three helper projections. Top-level
    /// `match` functions (not record-literal lambdas) to dodge offside pitfalls in
    /// generated code. `Error` on a kind mixing a `Node list` field with other node-bearing
    /// fields (`ReplaceChildren` not generable — GP4/GP5) rather than emitting a runtime
    /// `failwith` guard; kinds whose node-bearing fields are all single `Node` are generated
    /// with positional re-assignment.
    let private witnessDecl (msg: Set<string>) (kinds: IdlKind list) : Result<string, CodegenError> =
        let nodeArgs = declParams msg "Node" []

        let childBearing =
            kinds
            |> List.filter (fun k -> k.Fields |> List.exists (nodeBearing >> Option.isSome))

        let allBearing = List.length childBearing = List.length kinds

        let kindTagArms =
            kinds
            |> List.map (fun k -> sprintf "    | NodeKind.%s _ -> \"%s\"" k.Tag k.Tag)
            |> String.concat "\n"

        let childArm (k: IdlKind) =
            let exprs =
                k.Fields
                |> List.choose nodeBearing
                |> List.map (fun (name, isList) -> if isList then "s." + name else "[ s." + name + " ]")
                |> String.concat " @ "

            sprintf "    | NodeKind.%s s -> %s" k.Tag exprs

        let replaceArm (k: IdlKind) : Result<string, CodegenError> =
            match k.Fields |> List.choose nodeBearing with
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
                // refuses at generation time (GP4) rather than emitting a runtime `failwith`
                // guard into the generated code.
                Error(CodegenError.MultiChildFieldKind k.Tag)

        let childArms =
            (childBearing |> List.map childArm)
            @ (if allBearing then [] else [ "    | _ -> []" ])
            |> String.concat "\n"

        let replaceArms =
            childBearing
            |> List.map replaceArm
            |> sequenceR
            |> Result.map (fun arms -> (arms @ (if allBearing then [] else [ "    | _ -> n" ])) |> String.concat "\n")

        replaceArms
        |> Result.map (fun replaceArmsStr ->
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
    let private validatorDecl (msg: Set<string>) : string =
        let nodeArgs = declParams msg "Node" []

        String.concat
            "\n"
            [ "// Validator scaffold — register domain RuleFamilies into `reg`; rule content stays domain-side."
              sprintf
                  "let runValidator (reg: Validator.Registry<Node%s, string>) (root: Node%s) : Defect<string> list ="
                  nodeArgs
                  nodeArgs
              "    Validator.runAll nodeWitness reg root" ]

    // -----------------------------------------------------------------------
    // Phase 317 increment 7 — the IDL-declared-defaults leg. Emit a smart
    // constructor per kind: required fields *without* a declared default are
    // parameters; IDL-declared defaults are filled (the Phase 307 ARIA / variant
    // case — a field the author shouldn't have to repeat), and other optionals
    // default to `None`. The authoring ergonomics half of the structural set.
    // -----------------------------------------------------------------------

    /// The F# source expression for a declared default value, in the context of the
    /// field's IDL type (so a `VEnum "Standard"` on a `HeadingVariant` field emits
    /// `HeadingVariant.Standard`). Scalars + enums are supported — the spike's default
    /// classes; richer default values (unions / nodes) are a later leg.
    let private defaultExpr (enums: IdlEnum list) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> =
        match t, v with
        | TStr, VStr s -> Ok("\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")
        | TInt, VInt i -> Ok(string i)
        | TBool, VBool b -> Ok(if b then "true" else "false")
        | TEnum n, VEnum c -> Ok(n + "." + fsEnumCase enums n c)
        | TUnion(n, _), VUnion(tag, []) -> Ok(n + "." + tag)
        // Phase 1080 — the empty list, so a smart constructor can fill an
        // omitted-when-empty repeated slot without taking it as a parameter.
        | TList _, VList [] -> Ok "[]"
        | _ -> Error(CodegenError.UnsupportedDefault(t, v))

    /// Emit the smart constructors (`mk<Kind>`) over the generated `Node`. `Error` on a kind whose
    /// IDL-declared default has no code emission (`defaultExpr` — GP4/GP5).
    let private defaultsDecl
        (projections: Map<string, KindProjection>)
        (msg: Set<string>)
        (idl: Idl)
        (kinds: IdlKind list)
        : Result<string, CodegenError> =
        let nodeArgs = declParams msg "Node" []
        let fsType = fsTypeIn msg

        let defaultFor (kindTag: string) (fieldName: string) : IdlValue option =
            idl.Defaults
            |> List.tryPick (fun d ->
                if d.Kind = kindTag && d.Field = fieldName then
                    Some d.Value
                else
                    None)

        let ctor (k: IdlKind) : Result<string, CodegenError> =
            let parms =
                "(id: string)"
                :: (k.Fields
                    |> List.filter (fun f -> f.Opt = Required && (defaultFor k.Tag f.Name).IsNone)
                    |> List.map (fun f -> sprintf "(%s: %s)" (ident f.Name) (fsType f.Type)))
                |> String.concat " "

            let fieldExpr (f: IdlField) : Result<string, CodegenError> =
                match defaultFor k.Tag f.Name, f.Opt with
                | Some v, Required -> defaultExpr idl.Enums f.Type v
                | Some v, Optional -> defaultExpr idl.Enums f.Type v |> Result.map (fun e -> "Some(" + e + ")")
                | None, Required -> Ok(ident f.Name)
                | None, Optional -> Ok "None"
                // HostOnly: not a ctor param either — the field takes its placeholder.
                | _, HostOnly -> Ok(hostOnlyLit f)
                // OmitDefault: not a ctor param — the field takes its identity default.
                | _, OmitDefault d -> defaultExpr idl.Enums f.Type d

            k.Fields
            |> List.map (fun f -> fieldExpr f |> Result.map (fun e -> sprintf "%s = %s" (pascal f.Name) e))
            |> sequenceR
            |> Result.map (fun fieldStrs ->
                let record = String.concat "; " fieldStrs

                // Phase 690 — a smart constructor fills the envelope with its identity
                // value, so the common case stays `mkHeading "h" 2 text`. An envelope
                // field that is neither optional nor defaulted would have to become a
                // parameter; none is, and the generator says so rather than guessing.
                let envelope =
                    idl.NodeFields
                    |> List.map (fun f ->
                        match f.Opt with
                        | Optional -> sprintf "; %s = None" (pascal f.Name)
                        | OmitDefault d ->
                            match fsDefaultLit idl.Enums f.Type d with
                            | Some e -> sprintf "; %s = %s" (pascal f.Name) e
                            | None -> failwithf "node envelope field '%s' has an unrenderable default" f.Name
                        | HostOnly -> sprintf "; %s = %s" (pascal f.Name) (hostOnlyLit f)
                        | Required -> failwithf "node envelope field '%s' is Required — not yet supported" f.Name)
                    |> String.concat ""

                sprintf
                    "let mk%s %s : Node%s =\n    { Id = id; Kind = NodeKind.%s { %s }%s }"
                    k.Tag
                    parms
                    nodeArgs
                    k.Tag
                    record
                    envelope)

        kinds
        |> List.map (fun k ->
            // Phase 945 — a projected kind's ctor is the projection's own (or absent):
            // the generated one would construct the IDL-derived record, which under a
            // projection is not the record that exists.
            match projections.TryFind k.Tag with
            | Some p -> Ok(p.Mk |> Option.toList)
            | None -> ctor k |> Result.map List.singleton)
        |> sequenceR
        |> Result.map List.concat
        |> Result.map (fun ctors ->
            "// Smart constructors — required-without-default fields are parameters; IDL-declared\n// defaults are filled, other optionals default to None."
            + "\n\n"
            + String.concat "\n\n" ctors)

    /// Emit a compiling, self-contained F# encoder module (`moduleName`) for the named kinds,
    /// drawing in the enums/unions they transitively reference. `encodeNode : Node -> string`
    /// returns canonical wire via the shared `Canon.render`. Also emits a `nodeWitness`
    /// (`NodeWitness<Node, string>`) so the generated layer plugs into `Fuaran.Core.Tree`, a
    /// `runValidator` scaffold wiring `Fuaran.Core.Validator` over the generated `Node`, and
    /// `mk<Kind>` smart constructors applying the IDL-declared field defaults. `Error` on a
    /// construct the generator cannot yet emit (`CodegenError` — GP4/GP5), reported at generation
    /// time rather than as a `failwith`.
    let fsharpModuleWith
        (sup: GenSupport)
        (moduleName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        // Phase 945 — declared-doc lookup: a block of comment lines (markers included,
        // "///" or "//" alike) attached to the named declaration path, indented to the
        // emission site. Absent path ⇒ empty string, so an IDL with no docs emits
        // byte-identically to the pre-945 generator.
        let doc (path: string) (indent: string) : string =
            match sup.Docs.TryFind path with
            | Some lines -> (lines |> List.map (fun l -> indent + l) |> String.concat "\n") + "\n"
            | None -> ""

        // The same block as a type-group member's comment slot (no trailing newline —
        // the renderer adds it).
        let docOpt (path: string) : string option =
            sup.Docs.TryFind path |> Option.map (fun lines -> lines |> String.concat "\n")

        let kinds =
            kindTags
            |> List.choose (fun t -> idl.Kinds |> List.tryFind (fun k -> k.Tag = t))

        let enums, unions, records = referenced idl kinds

        // Phase 108 — the emitted discriminated-object builder: `Canon.typed` on a
        // default-shape IDL (byte-identical to every pre-declarable emission), the
        // module-local `typedTag` when the vocabulary declares another key.
        let typedName =
            if idl.Wire.Discriminator = "$type" then
                "Canon.typed"
            else
                "typedTag"

        // Phase 689 — which declarations are generic in `'Msg`. Empty unless the IDL
        // uses `TFn`, so an IDL that has not adopted it generates exactly as before.
        let msg = msgCarrying idl

        /// `"<'Msg>"` where the tree is msg-carrying, `""` otherwise — the suffix every
        /// emitted `Node` / `NodeKind` annotation needs.
        let nodeArgs = declParams msg "Node" []
        let kindArgs = declParams msg "NodeKind" []

        let rqaEnum (e: IdlEnum) =
            // Phase 945 — declared docs on the enum type and its cases.
            let cases =
                e.Cases
                |> List.map (fun c -> doc ("case:" + e.Name + "." + c) "    " + sprintf "    | %s" c)
                |> String.concat "\n"

            doc ("type:" + e.Name) ""
            + "[<RequireQualifiedAccess>]\n"
            + sprintf "type %s =\n%s" e.Name cases

        // Value-unions, non-discriminated records, per-kind specs, `NodeKind` and `Node` form ONE
        // type-recursion cycle in the real tier — a union can hold a record (`CellKindErased`
        // holds `ButtonGroupItem`) or a `Node` (`FragmentArg.SlotArg`), a record holds unions, a
        // spec holds `Node list`. So all of them are emitted as a single `type … and …` group
        // (enums stay standalone before it — they reference nothing). Unions + `NodeKind` are
        // `[<RequireQualifiedAccess>]` (case-name collisions across `Number` / `Text` / `Static` /
        // `Date` / … demand it); records are plain (their `pascal`-cased fields never collide with
        // a keyword, and construction sites disambiguate by annotation).
        let typeGroup =
            let unionBody (u: IdlUnion) =
                docOpt ("type:" + u.Name),
                true,
                sprintf
                    "%s%s =\n%s"
                    u.Name
                    (declParams msg u.Name u.Params)
                    (u.Cases
                     |> List.map (fun c -> doc ("case:" + u.Name + "." + c.Tag) "    " + unionCaseDecl msg c)
                     |> String.concat "\n")

            // Phase 945 — field docs land above the field line, at field indent.
            let fieldDecls (owner: string) (fields: IdlField list) =
                fields
                |> List.map (fun f -> doc ("field:" + owner + "." + pascal f.Name) "      " + fsField msg f)
                |> String.concat "\n"

            let recordBody (r: IdlRecord) =
                docOpt ("type:" + r.Name),
                false,
                sprintf "%s%s =\n    {\n%s\n    }" r.Name (declParams msg r.Name []) (fieldDecls r.Name r.Fields)

            let specBody (k: IdlKind) =
                let comment =
                    match docOpt ("type:" + k.Tag + "Spec") with
                    | Some d -> Some("// " + k.Category + "\n" + d)
                    | None -> Some("// " + k.Category)

                // Phase 945 — a projected kind's record body is the projection's, verbatim.
                match sup.KindProjections.TryFind k.Tag with
                | Some proj -> comment, false, proj.SpecDecl
                | None ->
                    comment,
                    false,
                    sprintf
                        "%sSpec%s =\n    {\n%s\n    }"
                        k.Tag
                        (declParams msg (k.Tag + "Spec") [])
                        (fieldDecls (k.Tag + "Spec") k.Fields)

            let nodeKindBody =
                None,
                true,
                "NodeKind"
                + declParams msg "NodeKind" []
                + " =\n"
                + (kinds
                   |> List.map (fun k ->
                       sprintf "    | %s of %sSpec%s" k.Tag k.Tag (declParams msg (k.Tag + "Spec") []))
                   |> String.concat "\n")

            // Phase 690 — `id` + `kind` + the declared envelope. An IDL declaring no
            // envelope keeps the original one-liner, so nothing about it changes.
            let nodeBody =
                if List.isEmpty idl.NodeFields then
                    None, false, sprintf "Node%s = { Id: string; Kind: NodeKind%s }" nodeArgs kindArgs
                else
                    let fields =
                        "      Id: string"
                        :: sprintf "      Kind: NodeKind%s" kindArgs
                        :: (idl.NodeFields |> List.map (fsField msg))

                    None, false, sprintf "Node%s =\n    {\n%s\n    }" nodeArgs (String.concat "\n" fields)

            // (comment, requiresQualifiedAccess, keyword-less body). The first member leads with
            // `type` (RQA attribute on its own preceding line); the rest are `and`-joined.
            let members =
                (unions |> List.map unionBody)
                @ (records |> List.map recordBody)
                @ (kinds |> List.map specBody)
                @ [ nodeKindBody; nodeBody ]

            let render i (comment: string option, rqa: bool, body: string) =
                let commentPrefix =
                    match comment with
                    | Some c -> c + "\n"
                    | None -> ""

                let keyword =
                    match i = 0, rqa with
                    | true, true -> "[<RequireQualifiedAccess>]\ntype"
                    | true, false -> "type"
                    | false, true -> "and [<RequireQualifiedAccess>]"
                    | false, false -> "and"

                commentPrefix + keyword + " " + body

            let rendered = members |> List.mapi render |> String.concat "\n\n"

            // Phase 945 — verbatim members appended to the SAME type-recursion group
            // (`and`-joined), so a spliced type may reference generated types freely.
            match sup.TypeSplice with
            | Some t -> rendered + "\n\n" + t
            | None -> rendered

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
            // kind's own object; `Canon.render` sorts keys, so splice order is
            // irrelevant. Nested emission is byte-identical to the pre-declarable form.
            let body =
                match idl.Wire.NodeEnvelope, idl.NodeFields with
                | NodeEnvelopeShape.NestedKind, [] -> "\n    JObj [ \"id\", JStr n.Id; \"kind\", kind ]"
                | NodeEnvelopeShape.NestedKind, _ ->
                    let pieces =
                        idl.NodeFields |> List.map (specPieceOf idl.Enums "n") |> String.concat "; "

                    sprintf "\n    JObj([ Some(\"id\", JStr n.Id); Some(\"kind\", kind); %s ] |> List.choose id)" pieces
                // Discriminator first, then id, kind fields, envelope — the
                // Phase 111 declared order (irrelevant under Sorted rendering,
                // where `Canon.render` re-sorts; normative under Declared).
                | NodeEnvelopeShape.FlatKind, [] ->
                    "\n    match kind with\n    | JObj(__d :: __kf) -> JObj(__d :: (\"id\", JStr n.Id) :: __kf)\n    | __other -> __other"
                | NodeEnvelopeShape.FlatKind, _ ->
                    let pieces =
                        idl.NodeFields |> List.map (specPieceOf idl.Enums "n") |> String.concat "; "

                    sprintf
                        "\n    match kind with\n    | JObj(__d :: __kf) -> JObj(__d :: (\"id\", JStr n.Id) :: (__kf @ ([ %s ] |> List.choose id)))\n    | __other -> __other"
                        pieces

            // Phase 694 — the kind dispatch is its own function (was inline in
            // encNode) so the JVal accessors below can expose it: a host codec
            // splicing a bare NodeKind (a TreeOp `EditNode.newKind`) reaches the
            // same single encoder the node envelope uses.
            sprintf "let rec private encNodeKind (k: NodeKind%s) : JVal =\n    match k with\n" nodeArgs
            + arms
            + sprintf "\n\nand private encNode (n: Node%s) : JVal =\n    let kind = encNodeKind n.Kind\n" nodeArgs
            + body

        // encNode + every union / record / spec encoder form one mutually-recursive group.
        let recGroup =
            (encNodeDecl :: (unions |> List.map (unionEncoder typedName doc msg idl.Enums))
             @ (records |> List.map (recordEncoder msg idl.Enums))
             @ (kinds
                |> List.map (fun k ->
                    // Phase 945 — a projected kind's encoder is the projection's, verbatim.
                    match sup.KindProjections.TryFind k.Tag with
                    | Some proj -> doc ("enc:" + k.Tag) "" + proj.Encoder
                    | None -> doc ("enc:" + k.Tag) "" + specEncoder typedName msg idl.Enums k))
             @ (sup.EncodeSplice |> Option.toList))
            |> String.concat "\n\n"

        let header =
            // Phase 113 — the generated layer constructs and matches EVERY declared
            // member, marked ones included, so a vocabulary that deprecates anything
            // would otherwise make its own codec noisy with FS0044. The suppression is
            // file-local and conditional: a vocabulary that marks nothing emits exactly
            // the header it always did, and a CONSUMER of this module still gets the
            // warning, which is the whole point of emitting the attribute.
            let nowarn =
                if emitsObsolete idl then
                    "\n#nowarn \"44\" // this layer implements every declared member, including deprecated ones"
                else
                    ""

            sprintf
                "// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen (Phase 317 increment 3). Do not edit by hand.\nmodule %s%s\n\nopen Fuaran.Core"
                moduleName
                nowarn

        // Phase 672: the decoder's mutually-recursive group, mirroring `recGroup`.
        // `decNodeKind` dispatches `$type` to the per-kind spec decoder; `decNode`
        // reads the `{ id, kind }` envelope `encNode` writes.
        let decGroup =
            let decNodeKindDecl =
                let arms =
                    kinds
                    |> List.map (fun k ->
                        sprintf "    | \"%s\" -> dec%sSpec j |> Result.map NodeKind.%s" k.Tag k.Tag k.Tag)
                    |> String.concat "\n"

                sprintf
                    "let rec private decNodeKind (j: JVal) : Result<NodeKind%s, string> =\n"
                    (objParams msg "NodeKind" [])
                + "    dObj j |> Result.bind (fun __fs ->\n"
                + "    dTag __fs |> Result.bind (fun __t ->\n"
                + "    match __t with\n"
                + arms
                + "\n    | __other -> Error (\"unknown node kind: \" + __other)))"

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
                    idl.NodeFields
                    |> List.map (fun f -> sprintf "; %s = %s" (pascal f.Name) (ident f.Name))
                    |> String.concat ""

                let final = sprintf "Ok { Id = id; Kind = kind%s }" envelopeAssigns

                let kindBinder =
                    match idl.Wire.NodeEnvelope with
                    | NodeEnvelopeShape.NestedKind -> "dReq \"kind\" __fs decNodeKind"
                    | NodeEnvelopeShape.FlatKind -> "decNodeKind j"

                let binders =
                    [ "id", "dReq \"id\" __fs dStr"; "kind", kindBinder ]
                    @ fieldBinders idl.Enums idl.NodeFields

                sprintf "and private decNode (j: JVal) : Result<Node%s, string> =\n" (objParams msg "Node" [])
                + "    dObj j |> Result.bind (fun __fs ->\n"
                + bindChain "    " binders final 1

            (decNodeKindDecl
             :: decNodeDecl
             :: (unions
                 |> List.map (unionDecoder doc sup.CaseRefines idl.Wire.Discriminator msg idl.Enums))
             @ (records |> List.map (recordDecoder msg idl.Enums))
             @ (kinds
                |> List.map (fun k ->
                    // Phase 945 — a projected kind's decoder is the projection's, verbatim.
                    match sup.KindProjections.TryFind k.Tag with
                    | Some proj -> doc ("dec:" + k.Tag) "" + proj.Decoder
                    | None -> doc ("dec:" + k.Tag) "" + specDecoder msg idl.Enums k))
             @ (sup.DecodeSplice |> Option.toList))
            |> String.concat "\n\n"

        match witnessDecl msg kinds, defaultsDecl sup.KindProjections msg idl kinds with
        | Ok witness, Ok defaults ->
            [ [ header ]
              enums |> List.map rqaEnum
              [ typeGroup ]
              enums |> List.map (fun e -> doc ("enc:" + e.Name) "" + enumEncoder e)
              [ encodeHelpers idl ]
              [ recGroup ]
              [ sprintf
                    "let encodeNode (n: Node%s) : string = %s (encNode n)"
                    nodeArgs
                    (match idl.Wire.KeyOrder with
                     | KeyOrder.Sorted -> "Canon.render"
                     | KeyOrder.Declared -> "Canon.renderOrdered") ]
              // Phase 694 — JVal-level accessors for host codecs that splice
              // generated encodings into a larger canonical document (the
              // tier's TreeOp codec re-points at these when the hand-written
              // node encoder is deleted). Node + kind always; the two envelope
              // records only when the vocabulary declares them (the spike
              // vocabulary has neither).
              [ sprintf
                    "/// JVal-level accessors (Phase 694) — for host codecs that splice generated\n/// encodings into a larger canonical document (e.g. a TreeOp codec).\nlet encodeNodeJson (n: Node%s) : JVal = encNode n"
                    nodeArgs
                sprintf "let encodeNodeKindJson (k: NodeKind%s) : JVal = encNodeKind k" nodeArgs ]
              (if records |> List.exists (fun r -> r.Name = "StateBehaviour") then
                   [ sprintf "let encodeStateBehaviourJson (s: StateBehaviour%s) : JVal = encStateBehaviour s" nodeArgs ]
               else
                   [])
              (if records |> List.exists (fun r -> r.Name = "SemanticStyle") then
                   [ "let encodeSemanticStyleJson (s: SemanticStyle) : JVal = encSemanticStyle s" ]
               else
                   [])
              (sup.AccessorSplice |> Option.toList)
              [ decodeHelpers idl.Wire.Discriminator ]
              enums |> List.map (fun e -> doc ("dec:" + e.Name) "" + enumDecoder e)
              [ decGroup ]
              [ sprintf
                    "/// Structural decode. The policy layer (diagnostics, §16 lenient-accept,\n/// the reject set) composes ABOVE this — see the Phase 672 note in the generator.\nlet decodeNode (s: string) : Result<Node%s, string> =\n    Json.parse s |> Result.bind decNode"
                    (objParams msg "Node" []) ]
              [ witness ]
              [ validatorDecl msg ]
              [ defaults ] ]
            |> List.concat
            |> String.concat "\n\n"
            |> Ok
        | Error e, _
        | _, Error e -> Error e

    /// The pre-945 entry — `fsharpModuleWith` under an empty declared-support record,
    /// emitting byte-identically to the generator before the support channel existed.
    let fsharpModule (moduleName: string) (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        fsharpModuleWith GenSupport.Empty moduleName idl kindTags

    // -----------------------------------------------------------------------
    // Phase 317 increment 4 — the `schema.json` leg: emit a Draft 2020-12 JSON
    // Schema describing the canonical wire, from the same IDL. The third of the
    // §11 "triple mirror" (encoder + decoder already IDL-driven), so one IDL now
    // drives all three.
    //
    // Phase 1068 — a GENERIC union is emitted once per INSTANTIATED type argument
    // rather than once, type-erased. JSON Schema has no type parameters, which is
    // why the leg originally emitted a generic union's `'T`-typed fields as the
    // permissive `{}`; but a schema needs no type parameters to state the
    // instantiations, because the IDL names every one of them at the slot. The
    // erased form's cost was measurable and was measured: a `Binding<float>` slot
    // accepted a boolean and a `Binding<int>` slot accepted a §7 non-finite
    // sentinel, both of which the decoder refuses.
    // -----------------------------------------------------------------------

    /// Substitute a generic union's type parameters into a case field's type
    /// (`'T` → the type argument). Mirrors the identically-shaped substitution the
    /// encode / decode legs perform; kept local because those are private to their
    /// own modules and a shared one would put a codegen detail on the Idl surface.
    let rec private substType (subst: Map<string, IdlType>) (t: IdlType) : IdlType =
        match t with
        | TVar v ->
            match Map.tryFind v subst with
            | Some r -> r
            | None -> t
        | TList inner -> TList(substType subst inner)
        | TMap inner -> TMap(substType subst inner)
        | TUnion(n, args) -> TUnion(n, List.map (substType subst) args)
        | other -> other

    /// The `$defs` NAME of a type — for a non-generic declaration its own name,
    /// and for an instantiated generic union the name plus its mangled arguments
    /// (`Binding_float`, `Binding_list_SelectOption`). Total over `IdlType`, which
    /// is what makes it safe for `schemaOf` to name a `$def` for any slot it meets:
    /// the instantiation walk below uses this same function, so the set of names
    /// EMITTED and the set of names REFERENCED are computed one way, and a dangling
    /// `$ref` (an ERROR to a strict validator, never a permissive skip) cannot arise
    /// from the two disagreeing.
    let rec private defName (t: IdlType) : string =
        match t with
        | TStr -> "str"
        | TInt -> "int"
        | TBool -> "bool"
        | TFloat -> "float"
        | TJson -> "json"
        | THosted _ -> "hosted"
        | TOpaque -> "opaque"
        | TClosure
        | TFn _ -> "closure"
        | TNode -> "Node"
        | TKind -> "NodeKind"
        | TOp -> "TreeOp"
        | TEnum n
        | TRecord n -> n
        | TVar v -> v
        | TList inner -> "list_" + defName inner
        | TMap vt -> "map_" + defName vt
        | TUnion(n, []) -> n
        | TUnion(n, args) -> n + "_" + (args |> List.map defName |> String.concat "_")

    let rec private schemaOf (t: IdlType) : JVal =
        match t with
        | TStr -> JObj [ "type", JStr "string" ]
        | TInt -> JObj [ "type", JStr "integer" ]
        | TBool -> JObj [ "type", JStr "boolean" ]
        // WIRE_FORMAT §5/§7 — a float slot admits a JSON number OR one of the three
        // quoted non-finite sentinels. `TInt` above is deliberately untouched: §7
        // stops at the float slot, so an integer slot still refuses them.
        | TFloat ->
            JObj
                [ "anyOf",
                  JArr
                      [ JObj [ "type", JStr "number" ]
                        JObj [ "enum", JArr [ JStr "NaN"; JStr "Infinity"; JStr "-Infinity" ] ] ] ]
        | TEnum n -> JObj [ "$ref", JStr("#/$defs/" + n) ]
        // Phase 1068 — an instantiated generic union names ITS OWN definition. A
        // non-generic one is `defName`'s identity case, so this line is unchanged
        // for every union that has no parameters.
        | TUnion _ -> JObj [ "$ref", JStr("#/$defs/" + defName t) ]
        // Reached only for a parameter no instantiation bound — a declaration read
        // outside any instantiation walk. The permissive `{}` is the pre-1068
        // behaviour, kept as the honest answer to "this slot's type is not yet known".
        | TVar _ -> JObj []
        | TNode -> JObj [ "$ref", JStr "#/$defs/Node" ]
        | TKind -> JObj [ "$ref", JStr "#/$defs/NodeKind" ]
        | TOp -> JObj [ "$ref", JStr "#/$defs/TreeOp" ]
        | TList inner -> JObj [ "type", JStr "array"; "items", schemaOf inner ]
        // Closure / opaque fields are sentinel strings on the wire.
        | TClosure
        | TFn _ -> JObj [ "type", JStr "string"; "const", JStr "<closure>" ]
        | TOpaque -> JObj [ "type", JStr "string"; "const", JStr "<opaque>" ]
        // Phase 676 — "any JSON": the schema deliberately does not constrain content
        // the encoder does not decompose, matching how the hand-written schema already
        // renders the rule-12 structured-payload positions. A hosted slot is the same
        // deliberate abstention: its content belongs to the host codec's own spec.
        | TJson
        | THosted _ -> JBool true
        | TRecord n -> JObj [ "$ref", JStr("#/$defs/" + n) ]
        | TMap vt -> JObj [ "type", JStr "object"; "additionalProperties", schemaOf vt ]

    /// The wire-visible fields of a declaration. A [[HostOnly]] field is never on
    /// the wire in any state (Phase 691), so it is not a property the schema
    /// describes — listing it would advertise a key no encoder emits.
    let private wireFields (fields: IdlField list) =
        fields |> List.filter (fun f -> f.Opt <> HostOnly)

    /// The property/required pair shared by every object-shaped schema.
    ///
    /// **`additionalProperties` is deliberately absent** (Phase 697). The decoder
    /// tolerates unknown keys — it looks fields up by name, `WIRE_FORMAT.md` §2.1
    /// rule 2 — and the published `schema.json` matches that tolerance. A generated
    /// schema that set `additionalProperties: false` would reject payloads the
    /// format accepts and the decoder round-trips, which makes it a fourth mirror
    /// disagreeing with the spec rather than a projection of it. Forward
    /// compatibility is the point: an older host validating a newer producer's
    /// output must not fail on a key it has not learned yet.
    let private objectBody (fields: IdlField list) =
        let wire = wireFields fields

        [ "required",
          JArr(
              wire
              |> List.filter (fun f -> f.Opt = Required)
              |> List.map (fun f -> JStr f.Name)
          )
          "properties", JObj(wire |> List.map (fun f -> f.Name, schemaOf f.Type)) ]

    /// An object schema with a discriminator const (for a kind / union case) + its
    /// fields — the key is the vocabulary's DECLARED discriminator (Phase 108).
    let private objectSchema (disc: string) (typeConst: string) (fields: IdlField list) : JVal =
        let wire = wireFields fields

        let props =
            (disc, JObj [ "const", JStr typeConst ])
            :: (wire |> List.map (fun f -> f.Name, schemaOf f.Type))

        let required =
            disc
            :: (wire |> List.filter (fun f -> f.Opt = Required) |> List.map (fun f -> f.Name))

        JObj
            [ "type", JStr "object"
              "required", JArr(required |> List.map JStr)
              "properties", JObj props ]

    /// A NON-discriminated object schema — a [[TRecord]] (`FormField`, `FilterSpec`,
    /// `TabHeader`, `ColumnErased`, …). No `$type` const: that is exactly what
    /// distinguishes a record from a union case on the wire.
    let private recordSchema (r: IdlRecord) : JVal =
        JObj(("type", JStr "object") :: objectBody r.Fields)

    /// Emit a Draft 2020-12 JSON Schema for the whole IDL's canonical wire.
    let jsonSchema (idl: Idl) : string =
        let enumDef (e: IdlEnum) =
            // The `enum` array is a WIRE contract — wire strings, not host case names.
            e.Name, JObj [ "type", JStr "string"; "enum", JArr(e.WireCases |> List.map JStr) ]

        /// One union definition, under a substitution for its type parameters
        /// (empty for a non-generic union, so its emission is byte-identical to
        /// what it always was).
        let unionDefWith (subst: Map<string, IdlType>) (name: string) (u: IdlUnion) =
            let fieldsOf (c: IdlUnionCase) =
                c.Fields |> List.map (fun f -> { f with Type = substType subst f.Type })

            let tagged =
                u.Cases
                |> List.map (fun c -> objectSchema idl.Wire.Discriminator c.Tag (fieldsOf c))

            // A transparent case is on the wire BARE — its single field's value with
            // no `$type` envelope (`TextSource.Literal`: `"x"`, not
            // `{"$type":"Literal","text":"x"}`). The codec legs already special-case
            // it; without reflecting it here the schema rejects the CANONICAL form of
            // every literal string in the corpus. The tagged branch stays: §16
            // lenient-accept admits the envelope on input.
            let bare =
                match TransparentUnion.tag u with
                | None -> []
                | Some ttag ->
                    u.Cases
                    |> List.tryFind (fun c -> c.Tag = ttag)
                    |> Option.map (fun c -> fieldsOf c |> List.map (fun f -> schemaOf f.Type))
                    |> Option.defaultValue []

            name, JObj [ "oneOf", JArr(bare @ tagged) ]

        let unionDef (u: IdlUnion) = unionDefWith Map.empty u.Name u

        // ── Phase 1068: the instantiations of every GENERIC union ─────────────
        //
        // A generic union has no single wire shape — `Binding<float>` and
        // `Binding<int>` accept different `Static` payloads — so it is emitted once
        // per instantiation the vocabulary actually names, and never under its bare
        // name (nothing would reference it, and its `'T` slots would be the
        // permissive `{}` that made the erasure invisible).
        //
        // The walk is a worklist over the same traversal `schemaOf` performs, so
        // the two agree by construction (see `defName`). It closes because an
        // instantiation's own case fields are substituted before being walked, and
        // the vocabulary's recursion is regular: `Binding<'T>.Local.initialFrom` is
        // `Binding<'T>` at the SAME argument (a self-reference, already visited) and
        // `Binding<'T>.Format.source` is the FIXED `Binding<float>`. A vocabulary
        // whose recursion grows its argument (`Binding<'T>` containing a
        // `Binding<'T list>`) has no finite `$defs`, and the bound below says so out
        // loud rather than hanging.
        let generics =
            idl.Unions
            |> List.filter (fun u -> not (List.isEmpty u.Params))
            |> List.map (fun u -> u.Name, u)
            |> Map.ofList

        let instantiations: Map<string, string * IdlUnion * Map<string, IdlType>> =
            let mutable found = Map.empty
            let mutable queue: IdlType list = []

            let rec discover (t: IdlType) =
                match t with
                | TList inner -> discover inner
                | TMap vt -> discover vt
                | TUnion(n, args) when not (List.isEmpty args) ->
                    args |> List.iter discover
                    let key = defName t

                    if not (Map.containsKey key found) then
                        match Map.tryFind n generics with
                        | None -> ()
                        | Some u when List.length u.Params <> List.length args -> ()
                        | Some u ->
                            let subst = Map.ofList (List.zip u.Params args)
                            found <- Map.add key (key, u, subst) found
                            queue <- t :: queue
                | _ -> ()

            let discoverFields (fields: IdlField list) =
                wireFields fields |> List.iter (fun f -> discover f.Type)

            for u in idl.Unions do
                if List.isEmpty u.Params then
                    for c in u.Cases do
                        discoverFields c.Fields

            for r in idl.Records do
                discoverFields r.Fields

            for k in idl.Kinds do
                discoverFields k.Fields

            for o in idl.Ops do
                discoverFields o.Fields

            discoverFields idl.NodeFields

            // Expand: an instantiation's own case fields can name further ones.
            let mutable guard = 0

            while not (List.isEmpty queue) do
                guard <- guard + 1

                if guard > 1000 then
                    failwith
                        "generic-union instantiation walk did not close after 1000 expansions — the vocabulary's recursion grows its type argument, which has no finite JSON-Schema `$defs`"

                let head = List.head queue
                queue <- List.tail queue

                match Map.tryFind (defName head) found with
                | None -> ()
                | Some(_, u, subst) ->
                    for c in u.Cases do
                        wireFields c.Fields |> List.iter (fun f -> discover (substType subst f.Type))

            found

        let genericDefs =
            instantiations
            |> Map.toList
            |> List.map (fun (_, (name, u, subst)) -> unionDefWith subst name u)

        let recordDef (r: IdlRecord) = r.Name, recordSchema r

        let kindDef (k: IdlKind) =
            k.Tag, objectSchema idl.Wire.Discriminator k.Tag k.Fields

        let nodeDef =
            match idl.Wire.NodeEnvelope with
            | NodeEnvelopeShape.NestedKind ->
                "Node",
                JObj
                    [ "type", JStr "object"
                      // Phase 690 — the envelope is optional by construction (`state` /
                      // `style` / `accessibility` are omitted when empty), so `required`
                      // stays `id` + `kind` unless an envelope field is declared Required.
                      "required",
                      JArr(
                          JStr "id"
                          :: JStr "kind"
                          :: (idl.NodeFields
                              |> List.filter (fun f -> f.Opt = Required)
                              |> List.map (fun f -> JStr f.Name))
                      )
                      "properties",
                      JObj(
                          [ "id", JObj [ "type", JStr "string" ]
                            "kind", JObj [ "$ref", JStr "#/$defs/NodeKind" ] ]
                          @ (wireFields idl.NodeFields |> List.map (fun f -> f.Name, schemaOf f.Type))
                      ) ]
            // Phase 109 — the FLAT shape: a node IS a kind object also carrying its
            // `id` (and any declared envelope), which `allOf` states exactly.
            | NodeEnvelopeShape.FlatKind ->
                "Node",
                JObj
                    [ "allOf",
                      JArr
                          [ JObj [ "$ref", JStr "#/$defs/NodeKind" ]
                            JObj
                                [ "type", JStr "object"
                                  "required",
                                  JArr(
                                      JStr "id"
                                      :: (idl.NodeFields
                                          |> List.filter (fun f -> f.Opt = Required)
                                          |> List.map (fun f -> JStr f.Name))
                                  )
                                  "properties",
                                  JObj(
                                      ("id", JObj [ "type", JStr "string" ])
                                      :: (wireFields idl.NodeFields |> List.map (fun f -> f.Name, schemaOf f.Type))
                                  ) ] ] ]

        // The kind alternation, named (Phase 703). It was inlined into `Node.kind`,
        // which is equivalent for a node but leaves `TKind` — `EditNode.newKind`'s
        // type — with nothing to reference. Naming it also matches the published
        // schema, which has always carried a `NodeKind` definition.
        let nodeKindDef =
            "NodeKind",
            JObj [ "oneOf", JArr(idl.Kinds |> List.map (fun k -> JObj [ "$ref", JStr("#/$defs/" + k.Tag) ])) ]

        let opDef (o: IdlKind) =
            o.Tag, objectSchema idl.Wire.Discriminator o.Tag o.Fields

        /// The op alternation. Absent when the domain declares no ops, so an
        /// op-free IDL's schema is exactly what it was.
        let treeOpDefs =
            if List.isEmpty idl.Ops then
                []
            else
                (idl.Ops |> List.map opDef)
                @ [ "TreeOp",
                    JObj [ "oneOf", JArr(idl.Ops |> List.map (fun o -> JObj [ "$ref", JStr("#/$defs/" + o.Tag) ])) ] ]

        // The wire has TWO roots once ops are declared (`WIRE_FORMAT.md` §3.4) —
        // a payload is a Node or a TreeOp. They are distinguishable on structure
        // (a node carries `id` + `kind`, an op a top-level `$type`), so `oneOf`
        // states it exactly.
        let root =
            if List.isEmpty idl.Ops then
                "$ref", JStr "#/$defs/Node"
            else
                "oneOf", JArr [ JObj [ "$ref", JStr "#/$defs/Node" ]; JObj [ "$ref", JStr "#/$defs/TreeOp" ] ]

        // Records join the assembly (Phase 697). Every `TRecord` slot emits a
        // `$ref` into `#/$defs/`, so omitting them left a dangling reference for
        // `FormField` / `FilterSpec` / `TabHeader` / `ColumnErased` / … — under a
        // strict validator an unresolvable `$ref` is an error, not a permissive
        // skip, so the leg could never have certified against the corpus.
        // A generic union contributes its INSTANTIATIONS (Phase 1068), never its
        // bare erased name. An unparameterised union is unchanged.
        let defs =
            (idl.Enums |> List.map enumDef)
            @ (idl.Unions |> List.filter (fun u -> List.isEmpty u.Params) |> List.map unionDef)
            @ genericDefs
            @ (idl.Records |> List.map recordDef)
            @ (idl.Kinds |> List.map kindDef)
            @ [ nodeKindDef; nodeDef ]
            @ treeOpDefs

        JObj
            [ "$schema", JStr "https://json-schema.org/draft/2020-12/schema"
              root
              "$defs", JObj defs ]
        |> Json.render

    // -----------------------------------------------------------------------
    // Phase 317 increment 8 — the SECOND BACKEND (TypeScript). The same IDL now
    // generates an *independent* host's structural encoder (no FSharp.Core /
    // .NET dependency — plain JS string-building). A node run of it over the
    // corpus is byte-identical to the F# generated encoder, establishing the
    // cross-host byte-identity that is the precondition for cross-host
    // attestation (Phase 320). Encode-only, mirroring the F# encoder leg.
    // -----------------------------------------------------------------------

    /// A JS double-quoted SOURCE-string literal (escapes for TS source).
    let private tsSourceStr (s: string) : string =
        let sb = System.Text.StringBuilder()
        sb.Append '"' |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append "\\\"" |> ignore
            | '\\' -> sb.Append "\\\\" |> ignore
            | '\n' -> sb.Append "\\n" |> ignore
            | '\r' -> sb.Append "\\r" |> ignore
            | '\t' -> sb.Append "\\t" |> ignore
            | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
            | c -> sb.Append c |> ignore

        sb.Append '"' |> ignore
        sb.ToString()

    let private invariantFloat (f: float) : string =
        f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)

    /// Whether a wire key is spellable as a bare JS identifier (`$type`, `kind`)
    /// — decides dotted-vs-bracket access and bare-vs-quoted literal keys, so a
    /// default-shape emission stays byte-identical to the pre-declarable one.
    let private tsIsIdent (s: string) =
        s.Length > 0
        && (System.Char.IsLetter s[0] || s[0] = '_' || s[0] = '$')
        && s |> Seq.forall (fun c -> System.Char.IsLetterOrDigit c || c = '_' || c = '$')

    /// Property access under the declared discriminator: `v.$type` / `v.kind`,
    /// or `v["odd key"]` for a key JS cannot spell bare.
    let private tsDiscProp (disc: string) (obj: string) =
        if tsIsIdent disc then
            obj + "." + disc
        else
            obj + "[" + tsSourceStr disc + "]"

    /// The declared discriminator in object-literal key position.
    let private tsDiscKey (disc: string) =
        if tsIsIdent disc then disc else tsSourceStr disc

    /// Emit a TypeScript value literal for an authored `IdlValue` under the
    /// vocabulary's DECLARED wire shape (Phases 108/109) — unions/nodes become
    /// discriminator-tagged objects (matching the generated encoder's dispatch),
    /// fields keyed by name; a flat vocabulary's node is ONE object. Builds the
    /// conformance fixtures the node harness runs.
    let rec typescriptValueWith (shape: WireShape) (v: IdlValue) : string =
        let go = typescriptValueWith shape
        let disc = tsDiscKey shape.Discriminator

        match v with
        | VStr s -> tsSourceStr s
        | VInt i -> string i
        | VBool b -> if b then "true" else "false"
        | VFloat f -> invariantFloat f
        | VEnum s -> tsSourceStr s
        | VUnion(tag, fields) ->
            "{ "
            + disc
            + ": "
            + tsSourceStr tag
            + (fields |> List.map (fun (n, fv) -> ", " + n + ": " + go fv) |> String.concat "")
            + " }"
        | VList xs -> "[" + (xs |> List.map go |> String.concat ", ") + "]"
        | VRecord fields ->
            "{ "
            + (fields |> List.map (fun (n, fv) -> n + ": " + go fv) |> String.concat ", ")
            + " }"
        | VMap entries ->
            "{ "
            + (entries
               |> List.map (fun (k, fv) -> tsSourceStr k + ": " + go fv)
               |> String.concat ", ")
            + " }"
        | VNode(id, kindTag, fields) ->
            match shape.NodeEnvelope with
            | NodeEnvelopeShape.NestedKind ->
                "{ id: "
                + tsSourceStr id
                + ", kind: { "
                + disc
                + ": "
                + tsSourceStr kindTag
                + (fields |> List.map (fun (n, fv) -> ", " + n + ": " + go fv) |> String.concat "")
                + " } }"
            | NodeEnvelopeShape.FlatKind ->
                "{ "
                + disc
                + ": "
                + tsSourceStr kindTag
                + ", id: "
                + tsSourceStr id
                + (fields |> List.map (fun (n, fv) -> ", " + n + ": " + go fv) |> String.concat "")
                + " }"
        // Phase 698 — the envelope sits BESIDE `kind` on the emitted object, which is
        // where the generated `encodeNode` reads it from (`n.style`, `n.state`, …).
        // Nothing else changes: the generated TS node encoder has read the envelope
        // since Phase 690; only the VALUE emitter could not express one.
        | VNodeEnv(id, envelope, kindTag, fields) ->
            match shape.NodeEnvelope with
            | NodeEnvelopeShape.NestedKind ->
                "{ id: "
                + tsSourceStr id
                + (envelope
                   |> List.map (fun (n, fv) -> ", " + n + ": " + go fv)
                   |> String.concat "")
                + ", kind: { "
                + disc
                + ": "
                + tsSourceStr kindTag
                + (fields |> List.map (fun (n, fv) -> ", " + n + ": " + go fv) |> String.concat "")
                + " } }"
            | NodeEnvelopeShape.FlatKind ->
                "{ "
                + disc
                + ": "
                + tsSourceStr kindTag
                + ", id: "
                + tsSourceStr id
                + (envelope
                   |> List.map (fun (n, fv) -> ", " + n + ": " + go fv)
                   |> String.concat "")
                + (fields |> List.map (fun (n, fv) -> ", " + n + ": " + go fv) |> String.concat "")
                + " }"
        | VAbsent -> "undefined"
        // Closure / opaque values carry no data the TS encoder reads — its codec
        // emits the sentinel regardless, so any placeholder operand serialises right.
        //
        // But it must not be `undefined`, which is how [[VAbsent]] says "this field
        // is NOT on the wire": `tsSpecPieceOf` omits an optional field on exactly that
        // test, so an OPTIONAL closure/opaque slot silently vanished from the TS
        // encoding while F# emitted its sentinel. Latent since the TS backend landed
        // — no fixture and no sampled vector had an optional sentinel field until the
        // Phase 689 spike added `Tabs.onSelect`, at which point the generative
        // conformance test failed at vector 6. A stand-in that is PRESENT keeps the
        // presence test honest without giving the codec anything to read.
        | VClosure
        | VOpaque -> "(() => undefined)"
        // Phase 676 — a JSON value emits as its canonical literal.
        | VJson j -> Canon.render j

    /// [[typescriptValueWith]] under the default shape — byte-identical to the
    /// pre-declarable emission.
    let typescriptValue (v: IdlValue) : string = typescriptValueWith WireShape.Default v

    /// The point-free TS encoder reference for a type (used where a codec must be
    /// passed — a generic union's type-parameter codec).
    let rec private tsEncFn (t: IdlType) : string =
        match t with
        | TStr -> "encStr"
        | TInt -> "encInt"
        | TBool -> "encBool"
        | TFloat -> "encFloat"
        | TEnum _ -> "encStr" // an enum value IS its wire string
        | TVar v -> "enc" + v
        | TNode -> "encodeNode"
        // Phase 703 models the OP vocabulary and certifies the interpreter leg
        // against the corpus; emitting an op family from the TypeScript encoder backend is a separate,
        // larger piece of work (`TreeOp` is msg-carrying through `TKind`/`TNode`,
        // so it lands as a generic type group). Nothing walks `idl.Ops` in this
        // backend yet, so these arms are unreachable today — explicit and loud so
        // that wiring ops in gets a precise signal instead of a match failure.
        | TKind
        | TOp ->
            failwithf
                "the TypeScript encoder backend does not emit the op vocabulary yet (Phase 703 leaves that leg unshipped): %A"
                t
        | TUnion(n, []) -> "enc" + n
        | TUnion(n, args) ->
            "((x) => enc"
            + n
            + "("
            + (args |> List.map tsEncFn |> String.concat ", ")
            + ", x))"
        | TList inner -> "((xs) => '[' + xs.map(" + tsEncFn inner + ").join(',') + ']')"
        // A closure/opaque codec ignores its argument and emits the fixed sentinel.
        | TClosure
        | TFn _ -> "(() => '\"<closure>\"')"
        | TOpaque -> "(() => '\"<opaque>\"')"
        // Phase 676 — `encJson` renders the parsed value canonically (see the prelude).
        // A hosted slot is verbatim JSON to the TS backend, like the interpreter.
        | TJson
        | THosted _ -> "encJson"
        | TRecord n -> "enc" + n
        | TMap vt ->
            "((m) => '{' + Object.keys(m).sort().map((k) => encStr(k) + ':' + ("
            + tsEncFn vt
            + ")(m[k])).join(',') + '}')"

    /// The applied TS encode expression for a value of `t` bound to `var`.
    let private tsEncApplied (var: string) (t: IdlType) : string =
        match t with
        | TList inner -> "'[' + " + var + ".map(" + tsEncFn inner + ").join(',') + ']'"
        | TUnion(n, (_ :: _ as args)) ->
            "enc"
            + n
            + "("
            + (args |> List.map tsEncFn |> String.concat ", ")
            + ", "
            + var
            + ")"
        | TNode -> "encodeNode(" + var + ")"
        | _ -> tsEncFn t + "(" + var + ")"

    /// One field of a spec encoder — a `[key, enc]` pair, or `null` (filtered) for
    /// an absent optional.
    /// TS boolean predicate: is `src` at the omit-when-default field's identity default?
    /// Enums render as wire strings (`s.tone === "Default"`); nullary unions as
    /// discriminator-tagged objects (`s.format.$type === "None"`).
    let private tsIsDefault (disc: string) (src: string) (t: IdlType) (d: IdlValue) : string option =
        match t, d with
        | TEnum _, VEnum c -> Some(src + " === " + tsSourceStr c)
        | TUnion _, VUnion(tag, []) -> Some(tsDiscProp disc src + " === " + tsSourceStr tag)
        // Phase 1080 — an omitted-when-empty repeated slot. `.length === 0` rather
        // than an equality test, because JS arrays compare by reference.
        | TList _, VList [] -> Some(src + ".length === 0")
        | TBool, VBool b -> Some(src + " === " + (if b then "true" else "false"))
        | _ -> None

    /// `recv` is the bound JS variable — `s` for a spec/record encoder, `n` for the
    /// node envelope (Phase 690), mirroring [[specPieceOf]] on the F# side.
    let private tsSpecPieceOf (disc: string) (recv: string) (f: IdlField) : string =
        let src = recv + "." + f.Name
        let pair = "[" + tsSourceStr f.Name + ", " + tsEncApplied src f.Type + "]"

        match f.Opt with
        | Required -> pair
        | Optional -> "(" + src + " === undefined ? null : " + pair + ")"
        | HostOnly -> "null"
        | OmitDefault d ->
            match tsIsDefault disc src f.Type d with
            | Some pred -> "(" + pred + " ? null : " + pair + ")"
            | None -> pair

    /// A union CASE field, honouring the same presence rules as a spec field.
    /// Phase 317 generative conformance caught this ignoring `f.Opt` entirely:
    /// TS emitted every optional union-case field unconditionally while F# omitted
    /// it, so `LayoutMode.Grid` without `templateColumns` diverged (and threw in
    /// the escaper). No fixed fixture carried that shape.
    let private tsCasePair (disc: string) (f: IdlField) : string =
        let src = "v." + f.Name
        let pair = "[" + tsSourceStr f.Name + ", " + tsEncApplied src f.Type + "]"

        match f.Opt with
        | Required -> pair
        | Optional -> "(" + src + " === undefined ? null : " + pair + ")"
        | HostOnly -> "null"
        | OmitDefault d ->
            match tsIsDefault disc src f.Type d with
            | Some pred -> "(" + pred + " ? null : " + pair + ")"
            | None -> pair

    // -----------------------------------------------------------------------
    // Phase 113 — declared annotations in the TypeScript backend.
    //
    // This backend emits plain JS: there is no per-field declaration to hang a
    // JSDoc block on, because a field is an inline entry in a one-line object
    // literal or pairs array. So an annotation renders as `//` line comments at the
    // closest line the emission actually has — the `case "<Tag>":` arm for a union
    // case, and the owning `function` for a field, NAMING the field.
    //
    // Line comments rather than a `/** … */` JSDoc block, deliberately: a
    // `@deprecated` JSDoc attached to `encTextSource` would tell every editor that
    // the ENCODER is deprecated, which is false. A comment that names its subject
    // says the true thing in a place tooling will not misread.
    // -----------------------------------------------------------------------

    /// The annotation lines for one member, at the given indent, each naming
    /// `subject`. Empty for an empty set — an unannotated vocabulary's emitted JS is
    /// byte-for-byte what it was.
    let private tsAnnotationLines (indent: string) (subject: string) (a: Annotations) : string list =
        [ match a.Deprecated with
          | Some d ->
              indent
              + "// @deprecated `"
              + subject
              + "`"
              + (match said d.Replacement with
                 | Some r -> " — use `" + r + "` instead."
                 | None -> " — marked for retirement.")
              + (match said d.Message with
                 | Some m -> " " + m
                 | None -> "")
          | None -> ()

          if a.InProcessOnly then
              indent
              + "// `"
              + subject
              + "` is in-process only: no wire projection, so a value here is lost across a wire boundary."

          match said a.Since with
          | Some v -> indent + "// `" + subject + "` — since " + v + "."
          | None -> () ]

    /// The comment block a generated function carries for its annotated FIELDS,
    /// each named `<owner>.<field>`. Empty string when nothing is annotated.
    let private tsFieldAnnotationHeader (owner: string) (fields: IdlField list) : string =
        let lines =
            fields
            |> List.collect (fun f -> tsAnnotationLines "" (owner + "." + f.Name) f.Annotations)

        match lines with
        | [] -> ""
        | ls -> (ls |> String.concat "\n") + "\n"

    let private tsUnionEncoder (disc: string) (u: IdlUnion) : string =
        let argList =
            match u.Params with
            | [] -> "v"
            | ps -> (ps |> List.map (fun p -> "enc" + p) |> String.concat ", ") + ", v"

        let arm (c: IdlUnionCase) =
            let ann =
                match tsAnnotationLines "    " c.Tag c.Annotations with
                | [] -> ""
                | ls -> (ls |> String.concat "\n") + "\n"

            ann
            + match TransparentUnion.tag u with
              | Some ttag when ttag = c.Tag ->
                  // Transparent case: return the single field's value bare (no `typed(...)`).
                  match c.Fields with
                  | [ f ] ->
                      "    case "
                      + tsSourceStr c.Tag
                      + ": return "
                      + tsEncApplied ("v." + f.Name) f.Type
                      + ";"
                  | _ -> failwithf "transparent union case '%s' must have exactly one field" c.Tag
              | _ ->
                  let pairs = c.Fields |> List.map (tsCasePair disc) |> String.concat ", "

                  "    case "
                  + tsSourceStr c.Tag
                  + ": return typed("
                  + tsSourceStr c.Tag
                  + ", ["
                  + pairs
                  + "]);"

        (u.Cases
         |> List.map (fun c -> tsFieldAnnotationHeader (u.Name + "." + c.Tag) c.Fields)
         |> String.concat "")
        + "function enc"
        + u.Name
        + "("
        + argList
        + ") {\n  switch ("
        + tsDiscProp disc "v"
        + ") {\n"
        + (u.Cases |> List.map arm |> String.concat "\n")
        + "\n  }\n}"

    /// A non-discriminated *record* encoder — a plain object, no discriminator,
    /// mirroring `recordEncoder` on the F# side. Added by Phase 690: the TS backend
    /// decoded records but could not ENCODE them, so `tsEncFn`'s `TRecord n -> "enc" + n`
    /// named a function that was never emitted. Harmless while the only IDL the TS
    /// backend ran on had no records, and a `ReferenceError` waiting for the first
    /// one — the node envelope is three of them.
    let private tsRecordEncoder (disc: string) (r: IdlRecord) : string =
        let pieces = r.Fields |> List.map (tsSpecPieceOf disc "s") |> String.concat ", "

        tsFieldAnnotationHeader r.Name r.Fields
        + "function enc"
        + r.Name
        + "(s) {\n  return plain(["
        + pieces
        + "]);\n}"

    /// A kind's spec encoder. Nested (the default): the discriminator-tagged
    /// object, byte-identical to the pre-declarable emission. Flat (Phase 109):
    /// the PAIRS alone — `encodeNode` merges them with the id and the envelope
    /// into the one flat object.
    let private tsSpecEncoder (disc: string) (flat: bool) (k: IdlKind) : string =
        let pieces = k.Fields |> List.map (tsSpecPieceOf disc "s") |> String.concat ", "
        let ann = tsFieldAnnotationHeader k.Tag k.Fields

        if flat then
            ann + "function enc" + k.Tag + "SpecPairs(s) {\n  return [" + pieces + "];\n}"
        else
            ann
            + "function enc"
            + k.Tag
            + "Spec(s) {\n  return typed("
            + tsSourceStr k.Tag
            + ", ["
            + pieces
            + "]);\n}"

    // ---- Phase 672 task 4: the TS decoder backend ----
    // The JS host's in-memory shape IS the wire shape (plain objects), so decoding
    // is validation plus rebuilding the positions the encoder writes implicitly:
    // omit-when-default fields (refilled so the encoder omits them again),
    // transparent union cases (re-wrapped so the encoder can re-flatten them), and
    // closure/opaque sentinels (whose PRESENCE is the only information they carry).

    /// The point-free TS decoder reference for a type.
    let rec private tsDecFn (t: IdlType) : string =
        match t with
        | TStr -> "dStr"
        | TInt -> "dInt"
        | TBool -> "dBool"
        | TFloat -> "dFloat"
        | TEnum n -> "dec" + n
        | TVar v -> "dec" + v
        | TNode -> "decNode"
        // Phase 703 models the OP vocabulary and certifies the interpreter leg
        // against the corpus; emitting an op family from the TypeScript decoder backend is a separate,
        // larger piece of work (`TreeOp` is msg-carrying through `TKind`/`TNode`,
        // so it lands as a generic type group). Nothing walks `idl.Ops` in this
        // backend yet, so these arms are unreachable today — explicit and loud so
        // that wiring ops in gets a precise signal instead of a match failure.
        | TKind
        | TOp ->
            failwithf
                "the TypeScript decoder backend does not emit the op vocabulary yet (Phase 703 leaves that leg unshipped): %A"
                t
        | TUnion(n, []) -> "dec" + n
        | TUnion(n, args) ->
            "((x) => dec"
            + n
            + "("
            + (args |> List.map tsDecFn |> String.concat ", ")
            + ", x))"
        | TList inner -> "dList(" + tsDecFn inner + ")"
        // The value carries nothing; only its presence matters (see tsDecField).
        // A `TFn` slot is the same on the wire — the TS tier has no `'Msg` to
        // rebuild into, so it stays `null` there regardless of the declared signature.
        | TClosure
        | TFn _
        | TOpaque -> "(() => null)"
        // Phase 676 — keep the parsed JSON as-is. Hosted slots identically.
        | TJson
        | THosted _ -> "((x) => x)"
        | TRecord n -> "dec" + n
        | TMap vt -> "dMap(" + tsDecFn vt + ")"

    /// The TS literal for an omit-when-default value, in its WIRE representation —
    /// refilled on decode so the encoder's omit test fires again and the bytes match.
    let private tsDefaultLit (disc: string) (t: IdlType) (d: IdlValue) : string option =
        match t, d with
        | TEnum _, VEnum c -> Some(tsSourceStr c)
        | TUnion _, VUnion(tag, []) -> Some("{ " + tsDiscKey disc + ": " + tsSourceStr tag + " }")
        | TBool, VBool b -> Some(if b then "true" else "false")
        // Phase 1080 — an absent repeated slot decodes to the EMPTY ARRAY, never to
        // `undefined` or `null`: the missing-list-field decode class, pinned here
        // rather than left to each host's own reading of an absent key.
        | TList _, VList [] -> Some "[]"
        | _ -> None

    let private tsDecField (disc: string) (f: IdlField) : string =
        let key = tsSourceStr f.Name

        match f.Type with
        | TClosure
        | TFn _
        | TOpaque ->
            match f.Opt with
            | Optional -> "dPresent(" + key + ", fs)"
            | _ -> "null"
        | _ ->
            match f.Opt with
            | Required -> "dReq(" + key + ", fs, " + tsDecFn f.Type + ")"
            | Optional -> "dOpt(" + key + ", fs, " + tsDecFn f.Type + ")"
            | HostOnly -> "undefined"
            | OmitDefault d ->
                match tsDefaultLit disc f.Type d with
                | Some lit -> "dDef(" + key + ", fs, " + tsDecFn f.Type + ", " + lit + ")"
                // `tsSpecPieceOf` fell back to always-emit, so decode is required too.
                | None -> "dReq(" + key + ", fs, " + tsDecFn f.Type + ")"

    let private tsFieldObject (disc: string) (extra: (string * string) list) (fields: IdlField list) : string =
        let pairs =
            (extra |> List.map (fun (k, v) -> k + ": " + v))
            @ (fields |> List.map (fun f -> tsSourceStr f.Name + ": " + tsDecField disc f))

        "{ " + String.concat ", " pairs + " }"

    let private tsEnumDecoder (e: IdlEnum) =
        // TS holds an enum AS its wire string — there is no second representation
        // on this side, so the decoder's closed set is the wire strings.
        let cases = e.WireCases |> List.map tsSourceStr |> String.concat ", "

        "const dec" + e.Name + " = dEnum(" + tsSourceStr e.Name + ", [" + cases + "]);"

    let private tsUnionDecoder (disc: string) (u: IdlUnion) =
        let argList =
            match u.Params with
            | [] -> "j"
            | ps -> (ps |> List.map (fun p -> "dec" + p) |> String.concat ", ") + ", j"

        let arm (c: IdlUnionCase) =
            let ann =
                match tsAnnotationLines "    " c.Tag c.Annotations with
                | [] -> ""
                | ls -> (ls |> String.concat "\n") + "\n"

            ann
            + "    case "
            + tsSourceStr c.Tag
            + ": return "
            + tsFieldObject disc [ tsDiscKey disc, tsSourceStr c.Tag ] c.Fields
            + ";"

        let tagged =
            "  if (isTagged(j)) {\n    const fs = j;\n    switch ("
            + tsDiscProp disc "j"
            + ") {\n"
            + (u.Cases |> List.map arm |> String.concat "\n")
            + "\n      default: return dFail("
            + tsSourceStr ("unknown " + u.Name + " case: ")
            + " + "
            + tsDiscProp disc "j"
            + ");\n    }\n  }"

        // A transparent union also accepts its single-field case bare, and re-wraps
        // it so the encoder can flatten it back to the same bytes.
        let untagged =
            match TransparentUnion.tag u with
            | Some ttag ->
                match u.Cases |> List.tryFind (fun c -> c.Tag = ttag) with
                | Some({ Fields = [ f ] }) ->
                    "  return { "
                    + tsDiscKey disc
                    + ": "
                    + tsSourceStr ttag
                    + ", "
                    + tsSourceStr f.Name
                    + ": "
                    + tsDecFn f.Type
                    + "(j) };"
                | _ -> failwithf "transparent union case '%s' must have exactly one field" ttag
            | None -> "  return dFail(" + tsSourceStr ("expected a " + u.Name + " object") + ");"

        (u.Cases
         |> List.map (fun c -> tsFieldAnnotationHeader (u.Name + "." + c.Tag) c.Fields)
         |> String.concat "")
        + "function dec"
        + u.Name
        + "("
        + argList
        + ") {\n"
        + tagged
        + "\n"
        + untagged
        + "\n}"

    let private tsRecordDecoder (disc: string) (r: IdlRecord) =
        tsFieldAnnotationHeader r.Name r.Fields
        + "function dec"
        + r.Name
        + "(j) {\n  const fs = dObj(j);\n  return "
        + tsFieldObject disc [] r.Fields
        + ";\n}"

    let private tsSpecDecoder (disc: string) (k: IdlKind) =
        tsFieldAnnotationHeader k.Tag k.Fields
        + "function dec"
        + k.Tag
        + "Spec(j) {\n  const fs = dObj(j);\n  return "
        + tsFieldObject disc [ tsDiscKey disc, tsSourceStr k.Tag ] k.Fields
        + ";\n}"

    /// The decode runtime prelude — the JS mirror of the F# `dObj`/`dReq`/… helpers.
    /// `isTagged` tests the DECLARED discriminator (Phase 108); the default key
    /// interpolates to exactly the pre-declarable bytes.
    let private tsDecodePrelude (disc: string) =
        "const dFail = (m) => { throw new Error(m); };\n"
        + "const isTagged = (j) => j !== null && typeof j === 'object' && !Array.isArray(j) && '"
        + disc
        + "' in j;"
        + """
const dObj = (j) => (j !== null && typeof j === 'object' && !Array.isArray(j)) ? j : dFail('expected an object');
const dStr = (j) => (typeof j === 'string') ? j : dFail('expected a string');
const dInt = (j) => (typeof j === 'number' && Number.isInteger(j)) ? j : dFail('expected an int');
// §7 — a float slot also accepts the three quoted non-finite sentinels `encFloat` emits
// (§5), and decodes them to the NUMBER, never the string. `dInt` above is not widened:
// §7 stops at the float slot.
const dFloat = (j) => {
  if (typeof j === 'number') return j;
  if (j === 'NaN') return NaN;
  if (j === 'Infinity') return Infinity;
  if (j === '-Infinity') return -Infinity;
  return dFail('expected a number');
};
const dBool = (j) => (typeof j === 'boolean') ? j : dFail('expected a bool');
const dList = (dec) => (j) => Array.isArray(j) ? j.map(dec) : dFail('expected an array');
const dMap = (dec) => (j) => {
  const o = dObj(j);
  const out = {};
  for (const k of Object.keys(o)) out[k] = dec(o[k]);
  return out;
};
const dEnum = (name, cases) => (j) =>
  (typeof j === 'string' && cases.indexOf(j) >= 0) ? j : dFail('not a ' + name);
const dReq = (name, fs, dec) => (name in fs) ? dec(fs[name]) : dFail("missing required field '" + name + "'");
const dOpt = (name, fs, dec) => (name in fs) ? dec(fs[name]) : undefined;
const dDef = (name, fs, dec, dflt) => (name in fs) ? dec(fs[name]) : dflt;
// An optional closure/opaque field: the value is a sentinel carrying nothing, but
// its PRESENCE distinguishes present-from-absent and must survive the round trip.
const dPresent = (name, fs) => (name in fs) ? null : undefined;"""

    /// Emit a self-contained TypeScript (ESM) structural encoder for the named
    /// kinds — `encodeNode(n)` returns canonical wire byte-identical to the F#
    /// generated `encodeNode`. Plain JS string-building: no FSharp.Core, no .NET,
    /// no imports — a genuinely independent host.
    let typescriptModule (idl: Idl) (kindTags: string list) : string =
        let kinds =
            kindTags
            |> List.choose (fun t -> idl.Kinds |> List.tryFind (fun k -> k.Tag = t))

        let _, unions, _ = referenced idl kinds

        // Runtime prelude — escaping mirrors Fuaran.Core.Canon.escape (WIRE_FORMAT §2
        // rule 6: only " and \ and control chars as \u00xx — NO \n/\r/\t shortcuts),
        // and object-field order is author order (Canon.render does not sort keys),
        // so the bytes match the F# host across ALL strings (incl. control chars).
        let prelude =
            """// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen (Phase 317 increment 8 — TS backend). Do not edit by hand.
const encStr = (s) => {
  let out = '"';
  for (const ch of s) {
    const code = ch.codePointAt(0);
    if (ch === '"') out += '\\"';
    else if (ch === '\\') out += '\\\\';
    else if (code < 0x20) out += '\\u' + code.toString(16).padStart(4, '0');
    else out += ch;
  }
  return out + '"';
};
const encInt = (n) => String(n);
const encBool = (b) => (b ? 'true' : 'false');
// §2 rule 5 — floats render in the .NET `ToString("R")` LAYOUT, which is not
// what JS `String(x)` produces: JS uses a lowercase `e`, an unsigned exponent,
// and a wider fixed-point threshold. The shortest-round-trip DIGITS agree (both
// .NET Core 3.0+ and V8 emit them); only the layout differs, so this normalises
// layout without touching the digits.
const formatFiniteDouble = (n) => {
  if (n === 0) return '0';
  const neg = n < 0;
  const s = Math.abs(n).toString();
  let digits;
  let exp;
  const eIdx = s.indexOf('e');
  if (eIdx >= 0) {
    const mant = s.slice(0, eIdx);
    const mantExp = parseInt(s.slice(eIdx + 1), 10);
    const dot = mant.indexOf('.');
    if (dot < 0) {
      digits = mant;
      exp = mantExp + (mant.length - 1);
    } else {
      digits = mant.slice(0, dot) + mant.slice(dot + 1);
      exp = mantExp + (dot - 1);
    }
  } else {
    const dot = s.indexOf('.');
    if (dot < 0) {
      digits = s;
      exp = s.length - 1;
    } else {
      const intPart = s.slice(0, dot);
      const fracPart = s.slice(dot + 1);
      if (intPart === '0') {
        const leadingZeros = fracPart.length - fracPart.replace(/^0+/, '').length;
        digits = fracPart.slice(leadingZeros);
        exp = -(leadingZeros + 1);
      } else {
        digits = intPart + fracPart;
        exp = intPart.length - 1;
      }
    }
  }
  digits = digits.replace(/0+$/, '') || '0';
  let out;
  if (exp >= -4 && exp <= 16) {
    if (exp >= 0) {
      out =
        digits.length <= exp + 1
          ? digits + '0'.repeat(exp + 1 - digits.length)
          : digits.slice(0, exp + 1) + '.' + digits.slice(exp + 1);
    } else {
      out = '0.' + '0'.repeat(-exp - 1) + digits;
    }
  } else {
    const mantissa = digits.length === 1 ? digits : digits[0] + '.' + digits.slice(1);
    out = mantissa + 'E' + (exp >= 0 ? '+' : '-') + Math.abs(exp).toString().padStart(2, '0');
  }
  return neg ? '-' + out : out;
};
const encFloat = (n) => {
  if (Number.isNaN(n)) return '"NaN"';
  if (n === Infinity) return '"Infinity"';
  if (n === -Infinity) return '"-Infinity"';
  return formatFiniteDouble(n);
};
// Phase 676 — arbitrary JSON rendered CANONICALLY: keys Ordinal-sorted, strings
// through the same escaper, numbers through the same float layout. Reusing those
// three is what stops a passthrough drifting from the rest of the wire.
const encJson = (v) => {
  if (v === null || v === undefined) return 'null';
  if (typeof v === 'string') return encStr(v);
  if (typeof v === 'boolean') return encBool(v);
  if (typeof v === 'number') return Number.isInteger(v) ? encInt(v) : encFloat(v);
  if (Array.isArray(v)) return '[' + v.map(encJson).join(',') + ']';
  const keys = Object.keys(v).sort();
  return '{' + keys.map((k) => encStr(k) + ':' + encJson(v[k])).join(',') + '}';
};
// Phase 698 — the Ordinal key sort is done HERE, at emission, not left to the
// caller's declaration order. `Canon.render` sorts every object's keys Ordinal on
// the F# side unconditionally, and JS `<` on strings compares UTF-16 code units,
// which is the same order — so sorting here is what makes the two hosts agree by
// construction. It previously relied on "pairs arrive Ordinal by convention", and
// the convention did not hold: the full-vocabulary sweep's very first vector
// diverged on `TextSource.I18n`, declared `key` then `args` and therefore emitted
// in that order against F#'s `args` then `key`. Every union case, spec and record
// whose fields are not already declared alphabetically had the same defect; no
// fixed fixture and no 8-kind sampled vector had ever contained one.
const ordinal = (a, b) => (a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0);"""
            // Phase 108 — `typed` writes the DECLARED discriminator. `$type`
            // (0x24) sorts before every data key, so the default's disc-first
            // build IS canonical order and interpolates to exactly the
            // pre-declarable bytes; any other key must be SORTED into place
            // among the pairs, or the emission diverges from `Canon.render`.
            //
            // Phase 111 — under DECLARED key order there is no sort at all: the
            // pairs arrive in declaration order and the discriminator leads, so
            // `typed` / `plain` preserve construction order, exactly as the F#
            // side's `Canon.renderOrdered` does.
            + (match idl.Wire.KeyOrder with
               | KeyOrder.Declared ->
                   "\nconst typed = (tag, pairs) =>\n  '{"
                   + Canon.render (JStr idl.Wire.Discriminator)
                   + ":' + encStr(tag) + pairs.filter((p) => p !== null).map(([k, v]) => ',' + encStr(k) + ':' + v).join('') + '}';\n"
               | KeyOrder.Sorted when idl.Wire.Discriminator = "$type" ->
                   "\nconst typed = (tag, pairs) =>\n  '{\"$type\":' + encStr(tag) + pairs.filter((p) => p !== null).sort(ordinal).map(([k, v]) => ',' + encStr(k) + ':' + v).join('') + '}';\n"
               | KeyOrder.Sorted ->
                   "\nconst typed = (tag, pairs) =>\n  plain([['"
                   + idl.Wire.Discriminator
                   + "', encStr(tag)]].concat(pairs));\n")
            + (match idl.Wire.KeyOrder with
               | KeyOrder.Sorted ->
                   """// `typed` without the discriminator: a plain object (a non-discriminated record, or
// the node envelope).
const plain = (pairs) =>
  '{' + pairs.filter((p) => p !== null).sort(ordinal).map(([k, v]) => encStr(k) + ':' + v).join(',') + '}';"""
               | KeyOrder.Declared ->
                   """// `typed` without the discriminator: a plain object (a non-discriminated record, or
// the node envelope). Declared key order — construction order is preserved.
const plain = (pairs) =>
  '{' + pairs.filter((p) => p !== null).map(([k, v]) => encStr(k) + ':' + v).join(',') + '}';""")

        let disc = idl.Wire.Discriminator
        let flat = idl.Wire.NodeEnvelope = NodeEnvelopeShape.FlatKind

        // Phase 111 — a TJson value under declared order is carried in its
        // authored order (the map encoder, a different receiver, stays sorted).
        let prelude =
            match idl.Wire.KeyOrder with
            | KeyOrder.Sorted -> prelude
            | KeyOrder.Declared ->
                prelude.Replace("const keys = Object.keys(v).sort();", "const keys = Object.keys(v);")

        let kindDispatch =
            if not flat then
                let arms =
                    kinds
                    |> List.map (fun k -> "    case " + tsSourceStr k.Tag + ": return enc" + k.Tag + "Spec(k);")
                    |> String.concat "\n"

                // Phase 690 — `id` / `kind` / the envelope, merged and sorted Ordinal so
                // the TS emission order matches F#'s canonical key sort. With no envelope
                // this is `id` then `kind`, i.e. exactly the previous hand-built literal.
                // Phase 111 — declared order keeps the construction order instead:
                // id, kind, then the envelope as declared.
                let nodePairsUnsorted =
                    ("id", "[\"id\", encStr(n.id)]")
                    :: ("kind", "[\"kind\", encKind(n.kind)]")
                    :: (idl.NodeFields |> List.map (fun f -> f.Name, tsSpecPieceOf disc "n" f))

                let nodePairs =
                    (match idl.Wire.KeyOrder with
                     | KeyOrder.Sorted ->
                         nodePairsUnsorted
                         |> List.sortWith (fun (a, _) (b, _) -> System.String.CompareOrdinal(a, b))
                     | KeyOrder.Declared -> nodePairsUnsorted)
                    |> List.map snd
                    |> String.concat ", "

                "function encKind(k) {\n  switch ("
                + tsDiscProp disc "k"
                + ") {\n"
                + arms
                + "\n  }\n}\n\nfunction encodeNode(n) {\n  return plain(["
                + nodePairs
                + "]);\n}"
            else
                // Phase 109 — the FLAT shape: the in-memory node IS the flat wire
                // object, so the kind pairs are read from the node itself and
                // `typed` merges the discriminator, the id, the kind fields and the
                // envelope into the one object — in that order, which is the Phase
                // 111 declared order (under Sorted rendering `typed` re-sorts, so
                // the order is free there).
                let arms =
                    kinds
                    |> List.map (fun k -> "    case " + tsSourceStr k.Tag + ": return enc" + k.Tag + "SpecPairs(n);")
                    |> String.concat "\n"

                let envelopeConcat =
                    match idl.NodeFields with
                    | [] -> ""
                    | fields ->
                        ".concat(["
                        + (fields |> List.map (tsSpecPieceOf disc "n") |> String.concat ", ")
                        + "])"

                "function encKindPairs(n) {\n  switch ("
                + tsDiscProp disc "n"
                + ") {\n"
                + arms
                + "\n  }\n}\n\nfunction encodeNode(n) {\n  return typed("
                + tsDiscProp disc "n"
                + ", [[\"id\", encStr(n.id)]].concat(encKindPairs(n))"
                + envelopeConcat
                + ");\n}"

        let enums, _, records = referenced idl kinds

        let kindDecodeDispatch =
            let arms =
                kinds
                |> List.map (fun k -> "    case " + tsSourceStr k.Tag + ": return dec" + k.Tag + "Spec(j);")
                |> String.concat "\n"

            let decKind =
                "function decKind(j) {\n  if (!isTagged(j)) return dFail('expected a kind object');\n  switch ("
                + tsDiscProp disc "j"
                + ") {\n"
                + arms
                + "\n    default: return dFail('unknown node kind: ' + "
                + tsDiscProp disc "j"
                + ");\n  }\n}\n\n"

            let decNode =
                if not flat then
                    "function decNode(j) {\n  const fs = dObj(j);\n  return { id: dReq('id', fs, dStr), kind: dReq('kind', fs, decKind)"
                    + (idl.NodeFields
                       |> List.map (fun f -> ", " + f.Name + ": " + tsDecField disc f)
                       |> String.concat "")
                    + " };\n}\n\n"
                else
                    // Phase 109 — flat: the node object is the kind body; the id
                    // (and envelope) merge beside the decoded spec's own keys.
                    "function decNode(j) {\n  const fs = dObj(j);\n  return Object.assign({}, decKind(j), { id: dReq('id', fs, dStr)"
                    + (idl.NodeFields
                       |> List.map (fun f -> ", " + f.Name + ": " + tsDecField disc f)
                       |> String.concat "")
                    + " });\n}\n\n"

            decKind
            + decNode
            + "// Structural decode. The policy layer (diagnostics, §16 lenient-accept, the\n"
            + "// reject set) composes ABOVE this — see the Phase 672 note in the generator.\n"
            + "function decodeNode(s) {\n  try {\n    return { ok: true, value: decNode(JSON.parse(s)) };\n  } catch (e) {\n    return { ok: false, error: String(e && e.message ? e.message : e) };\n  }\n}"

        [ [ prelude ]
          records |> List.map (tsRecordEncoder disc)
          unions |> List.map (tsUnionEncoder disc)
          kinds |> List.map (tsSpecEncoder disc flat)
          [ kindDispatch ]
          [ tsDecodePrelude disc ]
          enums |> List.map tsEnumDecoder
          records |> List.map (tsRecordDecoder disc)
          unions |> List.map (tsUnionDecoder disc)
          kinds |> List.map (tsSpecDecoder disc)
          [ kindDecodeDispatch ]
          [ "export { encodeNode, decodeNode };" ] ]
        |> List.concat
        |> String.concat "\n\n"

    // -----------------------------------------------------------------------
    // Phase 317 syntax-tree-emission leg + Phase 321 trust boundary. The
    // wire→source SCAFFOLD mode: emit host SOURCE that constructs a specific
    // node tree (the AI-emitted wire becomes compilable host code). This is the
    // only path where wire-derived VALUES land in source, so it is the
    // template-injection surface — every wire string goes through an escaped
    // literal (`fsStringLit`), and an unsupported feature ERRORS rather than
    // mis-emitting. A hostile string therefore cannot break out of a literal:
    // proven by the breakout tests (a value crafted to inject code emerges only
    // as escaped data). The encoder MODULES above carry no wire data (only
    // IDL-derived identifiers), so they have no injection surface at all.
    // -----------------------------------------------------------------------

    /// An escaped F# string literal — the injection-proof "escaped data node"
    /// for the scaffold mode.
    let private fsStringLit (s: string) : string =
        let sb = System.Text.StringBuilder()
        sb.Append '"' |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append "\\\"" |> ignore
            | '\\' -> sb.Append "\\\\" |> ignore
            | '\n' -> sb.Append "\\n" |> ignore
            | '\r' -> sb.Append "\\r" |> ignore
            | '\t' -> sb.Append "\\t" |> ignore
            | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
            | c -> sb.Append c |> ignore

        sb.Append '"' |> ignore
        sb.ToString()

    let rec private substG (subst: Map<string, IdlType>) (t: IdlType) : IdlType =
        match t with
        | TVar v ->
            match Map.tryFind v subst with
            | Some r -> r
            | None -> t
        | TList inner -> TList(substG subst inner)
        | TMap inner -> TMap(substG subst inner)
        | TUnion(n, args) -> TUnion(n, List.map (substG subst) args)
        | other -> other

    /// Does this authored value POPULATE a hosted slot ([[THosted]]) anywhere?
    ///
    /// A hosted slot's CONTENT belongs to the host codec's own specification — the
    /// IDL says only that the position carries verbatim JSON (see [[HostedCodec]]) —
    /// so a value-generic sampler cannot draw content a host codec is obliged to
    /// accept, and the real vocabulary's codecs are genuinely strict: an aria role
    /// is a string, a row feed is an array of row objects or the legacy sentinel, a
    /// `DataSource` is an object carrying `columns`. Any consumer that runs a
    /// sampled value THROUGH a host codec therefore has to know which of its own
    /// vectors it put beyond that codec's reach; this answers exactly that, so the
    /// answer is a stated boundary rather than a swallowed failure.
    ///
    /// Precise, not conservative: it walks the value ALONGSIDE its declared type and
    /// only reports a slot that is actually populated, so an optional hosted field
    /// sampled absent does not count.
    let usesHosted (idl: Idl) (v: IdlValue) : bool =
        let rec go (t: IdlType) (value: IdlValue) : bool =
            match t, value with
            | THosted _, _ -> true
            | TList inner, VList xs -> xs |> List.exists (go inner)
            | TMap vt, VMap entries -> entries |> List.exists (fun (_, ev) -> go vt ev)
            | TRecord n, VRecord fs ->
                match idl.Records |> List.tryFind (fun r -> r.Name = n) with
                | Some r -> fields r.Fields fs
                | None -> false
            | TUnion(n, args), VUnion(tag, fs) ->
                match idl.Unions |> List.tryFind (fun u -> u.Name = n) with
                | Some u when List.length u.Params = List.length args ->
                    match u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                    | Some c ->
                        let subst = Map.ofList (List.zip u.Params args)

                        fields (c.Fields |> List.map (fun f -> { f with Type = substG subst f.Type })) fs
                    | None -> false
                | _ -> false
            | TKind, VUnion(tag, fs) ->
                match idl.Kinds |> List.tryFind (fun k -> k.Tag = tag) with
                | Some k -> fields k.Fields fs
                | None -> false
            | TOp, VUnion(tag, fs) ->
                match idl.Ops |> List.tryFind (fun o -> o.Tag = tag) with
                | Some o -> fields o.Fields fs
                | None -> false
            | TNode, VNode(_, kindTag, fs) -> node kindTag fs []
            | TNode, VNodeEnv(_, envelope, kindTag, fs) -> node kindTag fs envelope
            | _ -> false

        and fields (declared: IdlField list) (authored: (string * IdlValue) list) : bool =
            declared
            |> List.exists (fun f ->
                match authored |> List.tryFind (fun (n, _) -> n = f.Name) with
                | Some(_, av) when av <> VAbsent -> go f.Type av
                | _ -> false)

        and node (kindTag: string) (kindFields: (string * IdlValue) list) (envelope: (string * IdlValue) list) : bool =
            fields idl.NodeFields envelope
            || (match idl.Kinds |> List.tryFind (fun k -> k.Tag = kindTag) with
                | Some k -> fields k.Fields kindFields
                | None -> false)

        go TNode v

    let private combineR (results: Result<string, string> list) : Result<string list, string> =
        (Ok [], results)
        ||> List.fold (fun acc r ->
            match acc, r with
            | Error e, _ -> Error e
            | Ok xs, Ok x -> Ok(xs @ [ x ])
            | Ok _, Error e -> Error e)

    /// Emit an F# value-construction expression for an authored `IdlValue` of
    /// type `t`, building values of the GENERATED types. Wire-derived strings
    /// route through `fsStringLit`; an unsupported shape ERRORS rather than
    /// mis-emitting (the syntax-tree-emission contract). `Result` so a hostile or
    /// malformed value is rejected, never silently mangled.
    let rec fsharpValue (idl: Idl) (t: IdlType) (v: IdlValue) : Result<string, string> =
        match t, v with
        | TStr, VStr s -> Ok(fsStringLit s)
        | TInt, VInt i -> Ok(string i)
        | TBool, VBool b -> Ok(if b then "true" else "false")
        | TFloat, VFloat f -> Ok(invariantFloat f)
        | TFloat, VInt i -> Ok(invariantFloat (float i))
        | TEnum name, VEnum case -> Ok(name + "." + case)
        | TUnion(name, args), VUnion(tag, fields) ->
            match idl.Unions |> List.tryFind (fun u -> u.Name = name) with
            | None -> Error(sprintf "fsharpValue: unknown union '%s'" name)
            | Some u when List.length u.Params <> List.length args ->
                Error(sprintf "fsharpValue: union '%s' arity mismatch" name)
            | Some u ->
                match u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                | None -> Error(sprintf "fsharpValue: union '%s' has no case '%s'" name tag)
                | Some c ->
                    let subst = Map.ofList (List.zip u.Params args)

                    let argResults =
                        c.Fields
                        |> List.map (fun f ->
                            match fields |> List.tryFind (fun (n, _) -> n = f.Name) with
                            | Some(_, fv) -> fsharpValue idl (substG subst f.Type) fv
                            | None -> Error(sprintf "fsharpValue: union case '%s' missing field '%s'" tag f.Name))

                    match combineR argResults with
                    | Error e -> Error e
                    | Ok [] -> Ok(name + "." + tag)
                    | Ok parts -> Ok(sprintf "%s.%s(%s)" name tag (String.concat ", " parts))
        | TNode, VNode(id, kindTag, fields) ->
            match idl.Kinds |> List.tryFind (fun k -> k.Tag = kindTag) with
            | None -> Error(sprintf "fsharpValue: unknown kind '%s'" kindTag)
            | Some k ->
                match fsAssignments idl ("kind '" + kindTag + "'") k.Fields fields with
                | Error e -> Error e
                | Ok recFields ->
                    Ok(
                        sprintf
                            "{ Id = %s; Kind = NodeKind.%s { %s } }"
                            (fsStringLit id)
                            kindTag
                            (String.concat "; " recFields)
                    )
        // Phase 698 — the enveloped form. The envelope's assignments sit on the
        // `Node` record itself (`Style = Some …`), the kind's on the spec record, and
        // both go through the SAME `fsAssignments`, so the presence rules cannot
        // drift between the two halves of a node.
        | TNode, VNodeEnv(id, envelope, kindTag, fields) ->
            match idl.Kinds |> List.tryFind (fun k -> k.Tag = kindTag) with
            | None -> Error(sprintf "fsharpValue: unknown kind '%s'" kindTag)
            | Some k ->
                match
                    fsAssignments idl ("kind '" + kindTag + "'") k.Fields fields,
                    fsAssignments idl "node envelope" idl.NodeFields envelope
                with
                | Error e, _
                | _, Error e -> Error e
                | Ok recFields, Ok envFields ->
                    Ok(
                        sprintf
                            "{ Id = %s; Kind = NodeKind.%s { %s }%s }"
                            (fsStringLit id)
                            kindTag
                            (String.concat "; " recFields)
                            (envFields |> List.map (fun a -> "; " + a) |> String.concat "")
                    )
        | TList inner, VList xs ->
            match combineR (xs |> List.map (fsharpValue idl inner)) with
            | Error e -> Error e
            | Ok items -> Ok("[ " + String.concat "; " items + " ]")
        | _, VAbsent -> Error "fsharpValue: VAbsent reached the emitter"
        | _ -> Error(sprintf "fsharpValue: value does not match IDL type %A" t)

    /// Record-field assignments (`Label = …; Icon = Some …`) for one declared field
    /// list against one authored field list, honouring every presence rule. Shared
    /// by a kind's spec record and (Phase 698) by the node envelope — `where`
    /// names the owner for the error messages only.
    and private fsAssignments
        (idl: Idl)
        (where: string)
        (declared: IdlField list)
        (authored: (string * IdlValue) list)
        : Result<string list, string> =
        declared
        |> List.map (fun f ->
            match authored |> List.tryFind (fun (n, _) -> n = f.Name) with
            | (None | Some(_, VAbsent)) ->
                match f.Opt with
                | Optional -> Ok(pascal f.Name + " = None")
                | HostOnly -> Ok(pascal f.Name + " = " + hostOnlyLit f)
                // OmitDefault absent → restore the identity default value.
                | OmitDefault d ->
                    match fsDefaultLit idl.Enums f.Type d with
                    | Some e -> Ok(pascal f.Name + " = " + e)
                    | None -> Error(sprintf "fsharpValue: unrenderable omit-default for '%s' on %s" f.Name where)
                | Required -> Error(sprintf "fsharpValue: %s missing required field '%s'" where f.Name)
            | Some(_, fv) ->
                match f.Opt, fsharpValue idl f.Type fv with
                | _, Error e -> Error e
                // A host-only field has no wire projection, so a wire value
                // at its name is not its value — take the placeholder.
                | HostOnly, Ok _ -> Ok(pascal f.Name + " = " + hostOnlyLit f)
                | Optional, Ok s -> Ok(pascal f.Name + " = Some(" + s + ")")
                // OmitDefault present → the raw (non-option) value, like Required.
                | OmitDefault _, Ok s
                | Required, Ok s -> Ok(pascal f.Name + " = " + s))
        |> combineR

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
