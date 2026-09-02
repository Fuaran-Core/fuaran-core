namespace Fuaran.Core.Idl

open Fuaran.Core

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
type HostPreludeRef = { Module: string; Path: string }

/// The declared-support record plus the host-prelude declaration — members 2 and 3
/// of the triple, as one document beside the vocabulary.
///
/// Two members and not one: `Gen.GenSupport` is the generator's INPUT and the
/// generator has no use for a prelude reference, so bolting the reference onto it
/// would put a field on a contract that never reads it. The document is the thing
/// that carries both.
type SupportDocument =
    { Support: Gen.GenSupport
      HostPrelude: HostPreludeRef option }

    /// A vocabulary that declares no support at all — the shape every domain starts
    /// from, and what `Gen.fsharpModule` (the pre-945 entry) means.
    static member Empty =
        { Support = Gen.GenSupport.Empty
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
/// canonical renderer in the estate, so escaping and float layout cannot drift
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

    let private atKey (name: string) (v: JVal) : JVal option =
        match v with
        | JObj fields -> fields |> List.tryPick (fun (n, x) -> if n = name then Some x else None)
        | _ -> None

    let private strAt (name: string) (v: JVal) : Result<string, string> =
        match atKey name v with
        | Some(JStr s) -> Ok s
        | Some _ -> Error("'" + name + "' is not a string")
        | None -> Error("missing '" + name + "'")

    let private optStrAt (name: string) (v: JVal) : Result<string option, string> =
        match atKey name v with
        | None -> Ok None
        | Some(JStr s) -> Ok(Some s)
        | Some _ -> Error("'" + name + "' is not a string")

    let private sequence (results: Result<'a, string> list) : Result<'a list, string> =
        (Ok [], results)
        ||> List.fold (fun acc r ->
            match acc, r with
            | Error e, _ -> Error e
            | _, Error e -> Error e
            | Ok xs, Ok x -> Ok(x :: xs))
        |> Result.map List.rev

    /// A list-valued key that is OMITTED when empty reads back as empty.
    let private mapAt
        (name: string)
        (read: JVal -> Result<string * 'v, string>)
        (root: JVal)
        : Result<Map<string, 'v>, string> =
        match atKey name root with
        | None -> Ok Map.empty
        | Some(JArr xs) -> xs |> List.map read |> sequence |> Result.map Map.ofList
        | Some _ -> Error("'" + name + "' is not an array")

    let private readDocEntry (v: JVal) : Result<string * string list, string> =
        strAt "path" v
        |> Result.bind (fun path ->
            match atKey "lines" v with
            | Some(JArr ls) ->
                ls
                |> List.map (function
                    | JStr s -> Ok s
                    | _ -> Error("doc '" + path + "' has a non-string line"))
                |> sequence
                |> Result.map (fun ls -> path, ls)
            | Some _ -> Error("doc '" + path + "' has a non-array 'lines'")
            | None -> Error("doc '" + path + "' has no 'lines'"))

    let private readRefine (v: JVal) : Result<string * string, string> =
        strAt "case" v
        |> Result.bind (fun c -> strAt "expression" v |> Result.map (fun e -> c, e))

    let private readProjection (v: JVal) : Result<string * Gen.KindProjection, string> =
        strAt "kind" v
        |> Result.bind (fun kindTag ->
            strAt "specDecl" v
            |> Result.bind (fun spec ->
                strAt "encoder" v
                |> Result.bind (fun enc ->
                    strAt "decoder" v
                    |> Result.bind (fun dec ->
                        optStrAt "mk" v
                        |> Result.map (fun mk ->
                            kindTag,
                            { SpecDecl = spec
                              Encoder = enc
                              Decoder = dec
                              Mk = mk })))))

    let private readPrelude (root: JVal) : Result<HostPreludeRef option, string> =
        match atKey "hostPrelude" root with
        | None -> Ok None
        | Some p ->
            strAt "module" p
            |> Result.bind (fun m -> strAt "path" p |> Result.map (fun path -> Some { Module = m; Path = path }))

    /// Read the document from its parsed root. The encoding version is refused by
    /// name when it is not this engine's, for the reason `Artifact.ofJson` gives:
    /// a support record that silently loses a projection emits a module that
    /// compiles and is wrong.
    let ofJson (root: JVal) : Result<SupportDocument, string> =
        match atKey "version" root with
        | None -> Error "support.json has no 'version'"
        | Some(JInt v) when v <> version ->
            Error(
                "support.json declares encoding version "
                + string v
                + "; this engine reads version "
                + string version
            )
        | Some(JInt _) ->
            let splice name =
                match atKey "splices" root with
                | None -> Ok None
                | Some block -> optStrAt name block

            mapAt "docs" readDocEntry root
            |> Result.bind (fun docs ->
                mapAt "caseRefines" readRefine root
                |> Result.bind (fun refines ->
                    mapAt "kindProjections" readProjection root
                    |> Result.bind (fun projections ->
                        splice "type"
                        |> Result.bind (fun ts ->
                            splice "encode"
                            |> Result.bind (fun es ->
                                splice "decode"
                                |> Result.bind (fun ds ->
                                    splice "accessor"
                                    |> Result.bind (fun accs ->
                                        readPrelude root
                                        |> Result.map (fun prelude ->
                                            { Support =
                                                { Docs = docs
                                                  TypeSplice = ts
                                                  EncodeSplice = es
                                                  DecodeSplice = ds
                                                  AccessorSplice = accs
                                                  CaseRefines = refines
                                                  KindProjections = projections }
                                              HostPrelude = prelude }))))))))
        | Some _ -> Error "support.json 'version' is not an integer"

    /// Read the declared-support document from `support.json` bytes — the inverse of
    /// [[render]].
    let parse (text: string) : Result<SupportDocument, string> = Json.parse text |> Result.bind ofJson
