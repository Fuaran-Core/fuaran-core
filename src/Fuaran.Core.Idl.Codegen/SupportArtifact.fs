namespace Fuaran.Core.Idl

open Fuaran.Core
open Fuaran.Core.Idl.Codegen.Types

// ---------------------------------------------------------------------------
// Phase 114 — the DECLARATION TRIPLE, carried as data.
//
// `Artifact` makes a vocabulary loadable. That alone does not let a domain
// regenerate its structural layer, because `Gen.fsharpModuleWith` takes a second
// argument: the declared-support record — the doc comments, the verbatim splices,
// the decode refinements and the host projections that are the domain's own
// authoring decisions rather than its wire. Until now that record existed only as
// F# source in whichever repo compiled the generator, which is precisely why a
// domain holding its own `idl.json` still could not emit: it had the vocabulary
// and not the support.
//
// So the triple a regenerate needs is:
//
//   1. the VOCABULARY      — `idl.json`, read by `Artifact.parse`
//   2. the SUPPORT record  — this document, read by `SupportArtifact.parse`
//   3. the HOST PRELUDE    — a source file compiled AHEAD of the generated module,
//                            named here rather than inlined (see below)
//
// all three of them files the domain holds, and nothing else beyond the packaged
// engine.
//
// **The prelude is NAMED, not inlined, and that is the whole design decision in
// this file.** The prelude is F# source the domain already compiles; copying its
// text into a JSON document would create a second copy of a compiled artefact,
// with no mechanism to keep the two the same — a drift hazard manufactured to
// satisfy the word "data". What the generator needs is not the prelude's text
// (it never reads it) but the KNOWLEDGE that a prelude exists, what module it
// declares, and where to find it — which is exactly what a `THosted` slot's
// `hostSurface` strings refer to and what a regenerating domain must place ahead
// of the emission. Naming it is the complete statement; inlining it would be a
// duplicate.
// ---------------------------------------------------------------------------

/// The host prelude of a vocabulary: the module a domain compiles AHEAD of its
/// generated structural layer, so the `THosted` codec expressions and `TFn`
/// placeholders the vocabulary names resolve.
///
/// `Path` is relative to the document that declares it, so the triple moves as a
/// directory and stays resolvable wherever the domain checks it out.
type HostPreludeRef =
    {
        /// The module name the prelude declares — what a `THosted` slot's `hostSurface`
        /// strings resolve against; the generator never reads the prelude's text.
        Module: string
        /// The prelude's source file, relative to the `support.json` that names it.
        Path: string
    }

/// The declared-support record plus the host-prelude declaration — members 2 and 3
/// of the triple, as one document beside the vocabulary.
///
/// Two members and not one: `GenSupport` is the generator's INPUT and the
/// generator has no use for a prelude reference, so bolting the reference onto it
/// would put a field on a contract that never reads it. The document is the thing
/// that carries both.
type SupportDocument =
    {
        /// The generator's input, passed to `Gen.fsharpModuleWith` unchanged.
        Support: GenSupport
        /// The prelude compiled ahead of the generated module, or `None` when none is
        /// declared — the rendered document then carries no `hostPrelude` key at all.
        HostPrelude: HostPreludeRef option
    }

    /// A vocabulary that declares no support at all — the shape every domain starts
    /// from, and what `Gen.fsharpModule` (the pre-945 entry) means.
    static member Empty =
        { Support = GenSupport.Empty
          HostPrelude = None }

/// The declared-support record rendered as a canonical data document —
/// `support.json` beside `idl.json`.
///
/// **Ordering contract**, matching `Artifact`'s and for the same reason (the
/// document exists to be diffed): entries are Ordinal-sorted by identity — docs by
/// declaration path, refinements by case, projections by kind — and the strings
/// WITHIN an entry are verbatim, because they are source text whose every byte is
/// significant. `Map` already enumerates in key order, so the sort is a property of
/// the model rather than a step here; it is restated in the round-trip law instead
/// of trusted.
///
/// Layout is `Artifact.renderJson`, not a second stringifier: one indented
/// canonical renderer for the whole family, so escaping and float layout cannot drift
/// between the two documents of one triple.
module SupportArtifact =

    /// The document ENCODING version — bumped when this module's output SHAPE
    /// changes, never when a domain's support content does.
    [<Literal>]
    let version = 1

    let private ordinal (a: string) (b: string) = System.String.CompareOrdinal(a, b)

    let private lines (xs: string list) : JVal = JArr(xs |> List.map JStr)

    /// An optional string slot contributes no key at all when it says nothing — the
    /// `annotations` posture, so a support-free document is minimal rather than a
    /// page of nulls.
    let private optKey (name: string) (v: string option) : (string * JVal) list =
        match v with
        | Some s -> [ name, JStr s ]
        | None -> []

    /// The document as a `JVal`.
    let json (doc: SupportDocument) : JVal =
        let s = doc.Support

        let docs =
            if Map.isEmpty s.Docs then
                []
            else
                [ "docs",
                  JArr(
                      s.Docs
                      |> Map.toList
                      |> List.sortWith (fun (a, _) (b, _) -> ordinal a b)
                      |> List.map (fun (path, ls) -> JObj [ "path", JStr path; "lines", lines ls ])
                  ) ]

        let splices =
            match
                optKey "type" s.TypeSplice
                @ optKey "encode" s.EncodeSplice
                @ optKey "decode" s.DecodeSplice
                @ optKey "accessor" s.AccessorSplice
            with
            | [] -> []
            | fields -> [ "splices", JObj fields ]

        let projections =
            if Map.isEmpty s.KindProjections then
                []
            else
                [ "kindProjections",
                  JArr(
                      s.KindProjections
                      |> Map.toList
                      |> List.sortWith (fun (a, _) (b, _) -> ordinal a b)
                      |> List.map (fun (kindTag, p) ->
                          JObj(
                              [ "kind", JStr kindTag
                                "specDecl", JStr p.SpecDecl
                                "encoder", JStr p.Encoder
                                "decoder", JStr p.Decoder ]
                              @ optKey "mk" p.Mk
                              // Phase 403 — both absent from a projection that declares neither, so
                              // a document written before them renders byte for byte as it did.
                              @ optKey "mapMsg" p.MapMsg
                              @ (match p.RecordFields with
                                 | Some fs -> [ "recordFields", CodegenLookup.fieldsJson fs ]
                                 | None -> [])
                          ))
                  ) ]

        let refines =
            if Map.isEmpty s.CaseRefines then
                []
            else
                [ "caseRefines",
                  JArr(
                      s.CaseRefines
                      |> Map.toList
                      |> List.sortWith (fun (a, _) (b, _) -> ordinal a b)
                      |> List.map (fun (case, expr) -> JObj [ "case", JStr case; "expression", JStr expr ])
                  ) ]

        let prelude =
            match doc.HostPrelude with
            | None -> []
            | Some p -> [ "hostPrelude", JObj [ "module", JStr p.Module; "path", JStr p.Path ] ]

        JObj(
            [ "version", JInt version
              "description",
              JStr(
                  "The declared-support record for a Fuaran IDL vocabulary — doc comments, verbatim "
                  + "source splices, per-case decode refinements and per-kind host projections — plus the "
                  + "host prelude the generated module is compiled after. Together with idl.json beside it "
                  + "and the prelude file it names, this is everything a domain needs to regenerate its "
                  + "structural layer against the packaged engine. Every string here is host-language "
                  + "source, not wire spec."
              ) ]
            @ docs
            @ splices
            @ refines
            @ projections
            @ prelude
        )

    /// The `support.json` bytes — indented, canonically ordered, newline-terminated.
    let render (doc: SupportDocument) : string = Artifact.renderJson (json doc)

    // ---- reading ----------------------------------------------------------
    //
    // Phase 526 — the reader is a `Decoder` on Core's typed decode layer (Phase 310), so a refusal
    // carries a `DecodeCode` and the path to the value at fault, as `Artifact`'s does, instead of a
    // sentence spelt here. The document stays OPEN to members it does not know (as it always was);
    // a member it does know is read strictly, so a `splices` block that is not an object is now
    // refused where it used to read as no splices at all.

    /// One more member into a record being decoded — the applicative step, so a reader with seven
    /// members is seven `<*>`s rather than seven nested binds. The first refusal wins.
    let private (<*>) (df: Decoder<'a -> 'b>) (da: Decoder<'a>) : Decoder<'b> = Decoder.pair df da (fun f a -> f a)

    let private str (name: string) : Decoder<string> = Decoder.field name Decoder.str

    let private optStr (name: string) : Decoder<string option> = Decoder.optField name Decoder.str

    /// A list-valued key that is OMITTED when empty reads back as empty; each entry is read under
    /// its index.
    let private entries (name: string) (entry: Decoder<string * 'v>) : Decoder<Map<string, 'v>> =
        Decoder.fieldOr name [] (Decoder.list entry) |> Decoder.map Map.ofList

    let private docEntry: Decoder<string * string list> =
        Decoder.succeed (fun path ls -> path, ls)
        <*> str "path"
        <*> Decoder.field "lines" Decoder.strings

    let private refine: Decoder<string * string> =
        Decoder.succeed (fun case expr -> case, expr)
        <*> str "case"
        <*> str "expression"

    /// A projection's declared record fields, in authored order — the vocabulary's own field
    /// reader, so a field reads alike in `idl.json` and here.
    let private projection: Decoder<string * KindProjection> =
        Decoder.succeed (fun kindTag spec enc dec mk map fields ->
            kindTag,
            { SpecDecl = spec
              Encoder = enc
              Decoder = dec
              Mk = mk
              MapMsg = map
              RecordFields = fields })
        <*> str "kind"
        <*> str "specDecl"
        <*> str "encoder"
        <*> str "decoder"
        <*> optStr "mk"
        <*> optStr "mapMsg"
        <*> Decoder.optField "recordFields" (Decoder.list CodegenLookup.readField)

    let private splices: Decoder<string option * string option * string option * string option> =
        Decoder.succeed (fun t e d a -> t, e, d, a)
        <*> optStr "type"
        <*> optStr "encode"
        <*> optStr "decode"
        <*> optStr "accessor"

    let private prelude: Decoder<HostPreludeRef> =
        Decoder.succeed (fun m path -> { Module = m; Path = path })
        <*> str "module"
        <*> str "path"

    /// The encoding version, refused by name when it is not this engine's (`OutOfRange`, as
    /// `Artifact`'s is): a support record that silently loses a projection emits a module that
    /// compiles and is wrong.
    let private encodingVersion: Decoder<unit> =
        Decoder.int
        |> Decoder.andThen (fun v ->
            if v = version then
                Ok()
            else
                Error(RootDecodeCode.OutOfRange, "encoding version " + string version, "encoding version " + string v))

    let private body: Decoder<SupportDocument> =
        Decoder.succeed (fun docs refines projections (ts, es, ds, accs) prelude ->
            { Support =
                { Docs = docs
                  TypeSplice = ts
                  EncodeSplice = es
                  DecodeSplice = ds
                  AccessorSplice = accs
                  CaseRefines = refines
                  KindProjections = projections }
              HostPrelude = prelude })
        <*> entries "docs" docEntry
        <*> entries "caseRefines" refine
        <*> entries "kindProjections" projection
        <*> Decoder.fieldOr "splices" (None, None, None, None) splices
        <*> Decoder.optField "hostPrelude" prelude

    /// The document's reader (Phase 526): the encoding version FIRST and alone — a document from
    /// another encoder may spell members this one does not know, and the version is the refusal
    /// that explains them — then every member it knows, each refusal at its path.
    let decoder: Decoder<SupportDocument> =
        Decoder.field "version" encodingVersion |> Decoder.bind (fun () -> body)

    /// Read the document from its parsed root — [[decoder]]'s refusal as one line in
    /// `DecodeError.render`'s form (code, path, sentence).
    let ofJson (root: JVal) : Result<SupportDocument, string> =
        decoder root |> Result.mapError DecodeError.render

    /// [[decoder]] over `support.json` bytes; text that is not JSON is refused at the root.
    let parseDetailed (text: string) : Result<SupportDocument, DecodeError> =
        Decoder.parse ReadOptions.strict text |> Result.bind decoder

    /// Read the declared-support document from `support.json` bytes — the inverse of
    /// [[render]]; [[parseDetailed]]'s refusal as one line, with its code and path.
    let parse (text: string) : Result<SupportDocument, string> =
        parseDetailed text |> Result.mapError DecodeError.render
