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
/// `Gen.fsharpModule`, so an unsupported construct is a typed `Error` rather than an exception
/// raised in the generator, or one emitted into the generated code.
type CodegenError =
    /// A field default whose (IDL type, value) pair has no emission. Scalars, enums, the empty
    /// list, a nullary union case and — since Phase 124 — a VALUE-CARRYING union case, a record
    /// and their nestings all carry one; a node, a map, arbitrary JSON, a sentinel slot and a
    /// non-empty list do not. Names the offending type + value.
    ///
    /// **The whole point of this case is that it arrives at GENERATION time.** Before Phase 124 an
    /// unrenderable default was answered `None` by the literal emitters on four of the six paths
    /// that consult them, and each of those fell back to always-emit (encoder) or `dReq`
    /// (decoder) — so the artefact silently contradicted its own IDL, and it did so with a green
    /// build. There is now no such path: every emitter that needs a default literal propagates
    /// this case up to `Gen.fsharpModuleWith` / `Gen.typescriptModule`, which refuse the module.
    | UnsupportedDefault of ty: IdlType * value: IdlValue
    /// A kind mixing a `Node list` field with other node-bearing fields — `witnessReplaceChildren`
    /// has no unambiguous positional split for it (several single-`Node` fields ARE generated,
    /// re-assigned positionally; no mixed kind exists in the current vocabulary). Names the kind tag.
    | MultiChildFieldKind of kindTag: string
    /// Phase 150 — the F\* PROOF-MODEL target's refusal: an IDL construct with no wire-level
    /// meaning in a model over the `jval` value model (a closure in a wire-visible slot, an
    /// `obj`-erased sentinel, a bare-kind or tree-op slot, an unresolved type parameter, a
    /// declared default the opaque numeric carriers cannot spell). Names the construct and the
    /// declaration it was reached through.
    ///
    /// **It is a REFUSAL rather than a dropped member, and that is the whole of its value.** A
    /// model that silently omitted the member it could not express would prove a round trip for
    /// a document nobody sends, and the theorem would read exactly as it reads now. The F\*
    /// target's own kind partition consumes this case: a kind whose closure raises it is named
    /// in the emitted header, with this description as its reason, instead of being absent.
    | UnmodellableInFStar of construct: string * where: string
    /// Phase 178 — the hardening boundary's refusal: a [[HardenPolicy]] member the run
    /// NEEDS is undeclared (empty). Names the member and what needed it.
    ///
    /// **It names the member and the NEED rather than the vocabulary**, which is what
    /// Phase 178's shard asked for, because an `Idl` carries no identity to name: it has
    /// `Kinds`, `Unions`, `Wire`, `Harden` and no name, description or version. Adding
    /// one to say "vocabulary X" in a refusal would be a breaking widening of the
    /// central published record to improve a message — the exact trade this phase
    /// stopped a flip over. The need is the more useful half anyway: `"the gate"`,
    /// `"a declared URL field"` and `"a declared markdown field"` tell an author which
    /// of their own decisions made the member necessary, which the vocabulary's name
    /// would not.
    ///
    /// Reached only from `Trust.hardenOrRefuse` / `Trust.checkHardenPolicy` — the
    /// opt-in entry points. `Trust.harden` cannot raise it: its signature returns a
    /// value, and changing that would break every caller of a published function to
    /// deliver a refusal only an undeclared policy can trigger.
    | UndeclaredHardenToken of member_: string * needed: string
    /// Phase 195 — a node-envelope field declared [[Optionality.Required]] with no default
    /// to fill it from. A smart constructor fills the envelope with an identity value so the
    /// common call stays `mkHeading "h" 2 text`; a member that is neither optional, nor
    /// omit-at-default, nor host-only, nor carrying a declared default has no value the
    /// generator may invent. Names the member, its declared type, and the alternatives.
    ///
    /// **A Required envelope member WITH a declared default is emitted, not refused** — that
    /// is the shape the full node envelope needs, and it is why this case exists instead of
    /// the throw that stood here: the refusal is now reserved for the one case that is
    /// genuinely under-determined, and it arrives as data a caller can match on rather than
    /// as a crash carrying a sentence.
    | RequiredEnvelopeField of field: string * ty: IdlType * alternative: string
    /// Phase 195 — an IDL construct no backend of this generator emits. Names the construct,
    /// the guiding principle the alternative would breach, and the declared alternative.
    ///
    /// **It is the typed form of every remaining "cannot yet emit" throw.** Three classes reach
    /// it: an op-vocabulary slot (`TKind` / `TOp`) in a field type, which no F# or TypeScript
    /// backend emits; a [[Optionality.HostOnly]] field whose type is not a [[TFn]], so it
    /// declares neither a host type nor a placeholder to restore; and a declared transparent
    /// union case that does not carry exactly one field, so its bare wire form is ambiguous.
    /// Since Phase 303 a fourth: a caller's harden entry naming a field the sanitisation floor
    /// cannot reach (`Trust.checkHardenPolicy`), which would otherwise pass unsanitised.
    /// Each was a THROW before this phase — a build-time crash carrying prose, from a
    /// generator whose every other refusal was already a value.
    | UnsupportedConstruct of construct: string * principle: string * alternative: string

/// Rendering for [[CodegenError]] — the one place a codegen refusal becomes prose.
[<RequireQualifiedAccess>]
module CodegenError =

    /// A one-line human rendering of a codegen refusal. It exists so that a leg whose published
    /// channel is a plain `string` (`Trust.scaffoldFSharp`, the scaffold mode) reports the SAME typed
    /// case as the module emitters rather than an ad-hoc sentence of its own — before Phase 124 an
    /// unrenderable default was two unrelated refusals depending on which leg met it.
    let describe (e: CodegenError) : string =
        match e with
        | UnsupportedDefault(ty, value) -> sprintf "no default literal for an IDL value of type %A: %A" ty value
        | MultiChildFieldKind kindTag ->
            sprintf "kind '%s' mixes a 'Node list' field with other node-bearing fields" kindTag
        | UnmodellableInFStar(construct, where) ->
            sprintf "the F* proof model cannot express %s (at %s)" construct where
        | UndeclaredHardenToken(member_, needed) ->
            sprintf "the vocabulary's HardenPolicy leaves '%s' undeclared, and %s needs it" member_ needed
        | RequiredEnvelopeField(field, ty, alternative) ->
            sprintf "node envelope field '%s' (%A) is Required with no declared default — %s" field ty alternative
        | UnsupportedConstruct(construct, principle, alternative) ->
            sprintf "the generator cannot emit %s (%s) — %s" construct principle alternative

namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl

/// Phase 293 — the emitter CORE every backend compiles after: the error channel's helpers, the
/// one `Result` sequencer, the naming and mangling rules (`pascal`, `ident`, the keyword set),
/// the msg-carrying analysis and the F# type spelling, plus the declared-support mirror types
/// the F# codec takes and the name-to-declaration index. `internal`: the public face of every
/// backend is `Gen`, a facade of one-line forwards, so nothing here is a published surface.
module internal Core =

    /// The package version the generator was built as, for the emitted header — the
    /// informational version with any build-metadata suffix dropped, so a regenerated artefact
    /// names the release that produced it rather than a phase label frozen at first emission.
    let generatorVersion: string =
        let asm = typeof<CodegenError>.Assembly

        let informational =
            asm.GetCustomAttributes(typeof<System.Reflection.AssemblyInformationalVersionAttribute>, false)
            |> Seq.tryHead
            |> Option.map (fun a ->
                (a :?> System.Reflection.AssemblyInformationalVersionAttribute).InformationalVersion)

        let v =
            match informational with
            | Some v when v <> "" -> v
            | _ -> string (asm.GetName().Version)

        match v.IndexOf '+' with
        | -1 -> v
        | i -> v.Substring(0, i)

    /// Phase 129 — the emitted text's line endings are the GENERATOR's, never its inputs'.
    ///
    /// Two inputs can carry a CR into an emission and neither is under the caller's control. The
    /// multi-line `"""…"""` templates below bake whatever line ending the compiled `Codegen.fs`
    /// happened to have on disk, so a build from a CRLF working copy emits CRLF where a build from
    /// an LF one emits LF — same version, same declaration, different bytes. And `GenSupport`'s
    /// doc blocks and verbatim splices are authored data that may arrive from a `support.json`
    /// written on any machine. Either way a consumer regenerating from the packaged generator gets
    /// bytes that depend on where the generator was built, which its own regeneration guard then
    /// reports as drift it cannot explain.
    ///
    /// Applied at each module emitter's boundary rather than at the template literals, so the
    /// property holds for every path into the output — including ones added later — and is
    /// falsifiable in one place: a declaration whose authored text carries a CR must still emit an
    /// artefact that carries none. A raw CR is never wanted in emitted F# or TypeScript source; a
    /// carriage return inside generated *data* travels as a six-character escape sequence, which
    /// this leaves untouched because it is text rather than a control byte.
    let normalizeEol (s: string) : string =
        s.Replace("\r\n", "\n").Replace("\r", "\n")

    /// Sequence a list of codegen results, short-circuiting on the first `CodegenError` (order
    /// preserved). The generator assembles source from many per-kind / per-field fragments; this
    /// threads a single typed failure up through the fragment lists without exceptions.
    let sequenceR (results: Result<'a, CodegenError> list) : Result<'a list, CodegenError> =
        (Ok [], results)
        ||> List.fold (fun acc r ->
            match acc, r with
            | Error e, _ -> Error e
            | Ok _, Error e -> Error e
            | Ok xs, Ok x -> Ok(x :: xs))
        |> Result.map List.rev

    /// [[sequenceR]] followed by `String.concat` — the shape almost every emitter below needs,
    /// since a generated declaration is a list of per-field / per-case fragments joined by a
    /// separator and any one of them may now refuse.
    let concatR (sep: string) (results: Result<string, CodegenError> list) : Result<string, CodegenError> =
        sequenceR results |> Result.map (String.concat sep)

    /// Phase 195 — the typed refusal every backend returns on an OP-VOCABULARY slot
    /// (`TKind` / `TOp`) in a field type.
    ///
    /// Phase 703 models the op vocabulary and certifies the interpreter leg against the
    /// corpus; emitting an op family from a SOURCE backend is a separate, larger piece of
    /// work (`TreeOp` is msg-carrying through `TKind`/`TNode`, so it lands as a generic type
    /// group). Nothing walks `idl.Ops` in these backends yet — so the slot is refused as a
    /// value naming the backend that met it, where each backend used to throw a sentence.
    let opVocabularySlot (backend: string) (t: IdlType) : CodegenError =
        CodegenError.UnsupportedConstruct(
            sprintf "an op-vocabulary slot (%A) in a field type, reached from %s" t backend,
            "GP4: a typed value, not an exception",
            "declare the slot with a type this backend models; the op-emission leg is unshipped (nothing walks the IDL's `Ops` here yet)"
        )

    /// Phase 252 — the typed refusal for a hosted slot whose declared wire form no
    /// backend can read without the host codec (another erased slot, a node, a type
    /// variable), or whose format is not one [[HostedFormat]] knows or sits on a
    /// non-string wire. `None` ⇒ the declaration is well-formed. The per-slot face of
    /// [[Declare.hostedWireErrors]]; a backend that would emit the slot asks it first.
    let hostedWireRefusal (backend: string) (h: HostedCodec) : CodegenError option =
        let rec unreadable (t: IdlType) =
            match t with
            | TStr
            | TInt
            | TBool
            | TFloat
            | TEnum _
            | TRecord _ -> false
            | TUnion(_, args) -> args |> List.exists unreadable
            | TList inner
            | TMap inner -> unreadable inner
            | _ -> true

        let refuse what =
            Some(
                CodegenError.UnsupportedConstruct(
                    sprintf "the hosted slot '%s' %s, reached from %s" h.FSharp what backend,
                    "GP5: a declared wire form every leg can read, or none",
                    "declare the wire form as a scalar, enum, record, union, list or map, and a format only on a string wire (date, date-time, uuid)"
                )
            )

        match h.Wire, h.Format with
        | Some w, _ when unreadable w -> refuse (sprintf "declaring the wire form %A" w)
        | w, Some f when w <> Some TStr ->
            refuse (sprintf "declaring the format '%s' on a non-string wire" (HostedFormat.name f))
        | _ -> None

    /// `one of 'a', 'b'` — what an `UnknownTag` refusal says the position expected, spelled as
    /// the interpreter spells it (Phase 337), for both generated decoders.
    let oneOf (names: string list) : string =
        "one of " + (names |> List.map (fun n -> "'" + n + "'") |> String.concat ", ")

    /// Whether any hosted slot in the vocabulary satisfies `pick`.
    let private declaresHostedWhere (pick: HostedCodec -> bool) (idl: Idl) : bool =
        let rec has (t: IdlType) =
            match t with
            | THosted h -> pick h
            | TList inner
            | TMap inner -> has inner
            | TUnion(_, args) -> args |> List.exists has
            | _ -> false

        [ idl.NodeFields
          yield! idl.Kinds |> List.map _.Fields
          yield! idl.Ops |> List.map _.Fields
          yield! idl.Records |> List.map _.Fields
          yield! idl.Unions |> List.collect (fun u -> u.Cases |> List.map _.Fields) ]
        |> List.exists (List.exists (fun f -> has f.Type))

    /// Whether any hosted slot in the vocabulary declares a FORMAT — the condition the
    /// F# and TypeScript decoder preludes emit their `dFormat` helper on, so a vocabulary
    /// that declares none emits byte-for-byte what it did.
    let declaresHostedFormat (idl: Idl) : bool = declaresHostedWhere _.Format.IsSome idl

    /// Whether the vocabulary declares any hosted slot — the condition the F# decoder prelude
    /// emits `dHosted` on (Phase 337), the lift of a host codec's sentence-refusal.
    let declaresHosted (idl: Idl) : bool = declaresHostedWhere (fun _ -> true) idl

    /// Phase 195 — the typed refusal for a DECLARED transparent union case that does not
    /// carry exactly one field. A transparent case is on the wire BARE, so its single field
    /// IS the encoding; a case with none or several has no unambiguous bare form.
    let transparentArity (unionName: string) (caseTag: string) : CodegenError =
        CodegenError.UnsupportedConstruct(
            sprintf "the declared transparent case '%s.%s', which does not carry exactly one field" unionName caseTag,
            "GP5: the refusal names the construct and thereby the set that IS supported",
            "give the transparent case exactly one field, or declare no transparent case for this union"
        )

    /// Phase 303 — a RECORD value literal for the type `typeName` (a record, or a kind's
    /// `<Tag>Spec`), from its `Field = expr` assignments. A declaration with no fields is the
    /// single-case MARKER type the type emitter declares for it (`R = | R`, F# having no empty
    /// record — `{ }` is FS3863), and its one value is spelled qualified, `R.R`, which reads the
    /// same whatever the module opens. Every F# site that writes a record value goes through
    /// here, so the declaration and its values cannot disagree about the shape.
    let recordLit (typeName: string) (assigns: string list) : string =
        match assigns with
        | [] -> typeName + "." + typeName
        | xs -> "{ " + String.concat "; " xs + " }"

    let pascal (s: string) =
        if s.Length = 0 then
            s
        else
            string (System.Char.ToUpperInvariant s[0]) + s.Substring 1

    /// F# reserved keywords that can collide with an IDL field name used *verbatim* as an
    /// identifier. Spec / record fields are `pascal`-cased (first letter upper — no F# keyword
    /// is upper-case), so they are always safe; **union-case fields are positional bindings used
    /// lower-case as-authored** (`| Value of ``default``: Scalar option * …`), so a keyword-named
    /// one (`default` on `HoleDecl.Value`) must be back-tick escaped in every identifier position
    /// (the field label, the match-pattern binding, the value reference) — but NOT in the wire
    /// *key string*, which stays the raw name. Back-tick quoting a non-keyword is harmless F#, so
    /// over-inclusion is safe; the set is the real reserved words so unaffected names stay bare.
    let fsKeywords =
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
    let ident (s: string) : string =
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
    let internal msgCarrying (idl: Idl) : Set<string> =
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
    let declParams (msg: Set<string>) (name: string) (ps: string list) =
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
    let objParams (msg: Set<string>) (name: string) (ps: string list) =
        let ps =
            (ps |> List.map (fun p -> "'" + p))
            @ (if msg.Contains name then [ "obj" ] else [])

        if List.isEmpty ps then
            ""
        else
            "<" + String.concat ", " ps + ">"

    let rec fsTypeIn (msg: Set<string>) (t: IdlType) : Result<string, CodegenError> =
        let fsType = fsTypeIn msg

        let applied (n: string) (args: string list) =
            let args = args @ (if msg.Contains n then [ "'Msg" ] else [])

            if List.isEmpty args then
                n
            else
                n + "<" + String.concat ", " args + ">"

        match t with
        | TStr -> Ok "string"
        | TInt -> Ok "int"
        | TBool -> Ok "bool"
        | TFloat -> Ok "float"
        | TEnum n -> Ok n
        | TUnion(n, args) -> args |> List.map fsType |> sequenceR |> Result.map (applied n)
        | TVar v -> Ok("'" + v)
        | TNode -> Ok(applied "Node" [])
        // Phase 195 — the op vocabulary is REFUSED AS DATA rather than thrown at. See
        // [[opVocabularySlot]] for why the arm exists and what it says.
        | TKind
        | TOp -> Error(opVocabularySlot "the F# type emitter" t)
        | TList inner -> fsType inner |> Result.map (fun s -> s + " list")
        // Closure / opaque fields carry no observable data — the generated structural layer is
        // ENCODER-ONLY and `'Msg`-erased (Phase 317 real-tier boundary): a function-typed field
        // (`Binding.Query`'s accessor, every `onChange` / `onClick`) and an `obj`-erased field
        // (`Sparkline.source`'s seq, `Select.value`) both collapse to `unit`. There is no host
        // behaviour or CLR shape to reconstruct here — the encoder emits the fixed `"<closure>"` /
        // `"<opaque>"` sentinel regardless of the (unit) value, so authoring stays trivial (`()`).
        // The real `Fuaran.UI` `Types.fs` keeps the `'Msg`-generic closures; the switch-over
        // re-attaches behaviour on the domain side (documented in docs/migrations/317-*).
        | TClosure -> Ok "unit"
        | TOpaque -> Ok "unit"
        // Phase 689 — the declared host signature, verbatim. This is the whole
        // difference from `TClosure`, and the reason the generated layer can be
        // the authoring type: the encoder never reads the value, so the slot's
        // host type was always free.
        | TFn s -> Ok("(" + s.FSharp + ")")
        // Phase 676 — a JSON slot is a real `JVal`, NOT erased to `unit`: it carries
        // data in both directions, which is the whole difference from `TOpaque`.
        | TJson -> Ok "JVal"
        // A hosted slot declares the real host type — that is its whole point.
        | THosted h -> Ok h.FSharp
        | TRecord n -> Ok(applied n [])
        | TMap vt -> fsType vt |> Result.map (fun s -> "Map<string, " + s + ">")

    // -----------------------------------------------------------------------
    // Phase 293 — the declared-support records as the EMITTERS see them. `Gen.GenSupport` and
    // `Gen.KindProjection` are the published shapes and stay declared in the facade, which
    // compiles after every emitter; the facade converts, field for field.
    // -----------------------------------------------------------------------

    /// The emitters' view of `Gen.KindProjection`.
    type Projection =
        { SpecDecl: string
          Encoder: string
          Decoder: string
          Mk: string option
          MapMsg: string option
          RecordFields: IdlField list option }

    /// The emitters' view of `Gen.GenSupport`.
    type Support =
        { Docs: Map<string, string list>
          TypeSplice: string option
          EncodeSplice: string option
          DecodeSplice: string option
          AccessorSplice: string option
          CaseRefines: Map<string, string>
          KindProjections: Map<string, Projection> }

    // -----------------------------------------------------------------------
    // Phase 293 — ONE name-to-declaration index. Every backend used to walk `idl.Unions` with
    // `List.tryFind` at its own call sites; the finders below are the one spelling, and the
    // index is what a walk builds once when it looks up by name in a loop.
    // -----------------------------------------------------------------------

    type IdlIndex =
        { Kinds: Map<string, IdlKind>
          Unions: Map<string, IdlUnion>
          Enums: Map<string, IdlEnum>
          Records: Map<string, IdlRecord> }

    module IdlIndex =
        let ofIdl (idl: Idl) : IdlIndex =
            { Kinds = idl.Kinds |> List.map (fun k -> k.Tag, k) |> Map.ofList
              Unions = idl.Unions |> List.map (fun u -> u.Name, u) |> Map.ofList
              Enums = idl.Enums |> List.map (fun e -> e.Name, e) |> Map.ofList
              Records = idl.Records |> List.map (fun r -> r.Name, r) |> Map.ofList }
