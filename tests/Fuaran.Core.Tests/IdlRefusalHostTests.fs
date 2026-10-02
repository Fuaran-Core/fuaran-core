/// Phase 337 — the generated decoders refuse with the typed decode error.
///
/// The cross-host REFUSAL law: one malformed document gets one `Code` and one `Path` from the
/// IDL interpreter (`Decode.decodeDetailed`), the compiled F# host (a generated module's
/// `decodeNode`) and the TypeScript host (the generated module's `decodeNode` under node). Two
/// sources of malformed documents:
///
/// - the `conformance/decode/` vectors, through the vocabulary `DecodeVectorsIdl` translates them
///   into — each covered vector's pinned code and path is what all three hosts must answer;
/// - sampled MUTATIONS of the certification vocabularies' valid documents (the three-way
///   differential's vocabularies) — a member deleted, a value replaced by one of another kind, a
///   string replaced by one no case names, the text truncated — where the interpreter is the
///   oracle and both hosts must answer as it does, refusal or acceptance.
///
/// Two places a mutation is NOT made, each a documented boundary rather than a tolerance: a
/// closure or opaque SENTINEL (`"<closure>"`, `"<opaque>"`) — the generated hosts read a sentinel
/// slot's presence only, never its value, which is the §16 posture this phase does not move; and a
/// hosted slot declaring no wire form (the reference vocabulary's `series`) — the interpreter and
/// the TypeScript host carry it verbatim and only the compiled host runs its codec (Phase 252).
module Fuaran.Core.Tests.IdlRefusalHostTests

open System
open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl

/// One host's answer for one document.
type private Answer =
    | Accepted
    | Refused of code: string * path: string

let private ofResult (r: Result<'T, DecodeError>) : Answer =
    match r with
    | Ok _ -> Accepted
    | Error e -> Refused(DecodeError.codeName e.Code, DecodePath.render e.Path)

let private show (a: Answer) =
    match a with
    | Accepted -> "accepted"
    | Refused(code, path) -> code + " at " + path

/// Run a script under node; `None` when node is not on PATH.
let private runNode (script: string) : string option =
    let tmp =
        Path.Combine(Path.GetTempPath(), sprintf "fuaran-337-%s.mjs" (Guid.NewGuid().ToString "N"))

    File.WriteAllText(tmp, script)

    try
        let proc =
            try
                Some(Diagnostics.Process.Start(ChildProcess.redirected "node" ("\"" + tmp + "\"")))
            with _ ->
                None

        match proc with
        | None -> None
        | Some p ->
            let stdout = p.StandardOutput.ReadToEnd()
            let stderr = p.StandardError.ReadToEnd()
            p.WaitForExit()

            if p.ExitCode <> 0 then
                failtestf "node failed running the refusal harness: %s" stderr

            Some stdout
    finally
        try
            File.Delete tmp
        with _ ->
            ()

/// The TypeScript host's answer for every document, in order; `None` when node is absent.
let private tsAnswers (idl: Idl) (docs: string list) : Answer list option =
    let tsModule =
        match Gen.typescriptModule idl (idl.Kinds |> List.map _.Tag) with
        | Ok src -> src
        | Error e -> failtestf "TypeScript codegen refused: %s" (CodegenError.describe e)

    let harness =
        tsModule
        + "\nconst __docs = "
        + Canon.render (JArr(docs |> List.map JStr))
        + ";\nfor (const d of __docs) {\n"
        + "  const r = decodeNode(d);\n"
        + "  console.log(JSON.stringify(r.ok ? { ok: true } : { ok: false, code: r.error.code, path: r.error.path, expected: r.error.expected, message: r.error.message }));\n"
        + "}\n"

    runNode harness
    |> Option.map (fun stdout ->
        stdout.Replace("\r\n", "\n").Split('\n')
        |> Array.filter (fun l -> l <> "")
        |> Array.map (fun line ->
            match Json.parse line with
            | Ok o ->
                match Decoder.tryMember "ok" o, Decoder.tryMember "code" o, Decoder.tryMember "path" o with
                | Some(JBool true), _, _ -> Accepted
                | _, Some(JStr code), Some path ->
                    match DecodePath.ofJson path with
                    | Some p -> Refused(code, DecodePath.render p)
                    | None -> failtestf "the TypeScript host's path is not a path: %s" line
                | _ -> failtestf "the TypeScript host's answer is malformed: %s" line
            | Error e -> failtestf "the TypeScript harness printed a non-JSON line (%s): %s" e line)
        |> Array.toList)

let private noNode =
    "node is not on PATH — the TypeScript leg of the refusal law cannot run"

// ---------------------------------------------------------------------------
// The decode vectors.
// ---------------------------------------------------------------------------

/// The vectors the IDL cannot declare, by name — pinned, so a vector added to the family is either
/// covered (and regenerates the compiled module) or named here with its reason.
let private expectedExclusions =
    [ "too-many-items"
      "member-key-needing-escape"
      "known-tag-not-admitted"
      "int-out-of-range"
      "undeclared-member"
      "undeclared-nested-member"
      "unresolved-shape"
      "unresolved-nested-shape" ]

let private pinned (v: DecodeVectorsIdl.DecodeVector) : Answer =
    match DecodeVectorsIdl.expectedRefusal v with
    | None -> Accepted
    | Some(code, path) -> Refused(code, DecodePath.render path)

[<Tests>]
let decodeVectors =
    testList
        "Phase 337 — the decode vectors through the interpreter and both generated hosts"
        [ testCase "drift guard: the generator still reproduces the committed DecodeVectorsGenerated.fs" (fun _ ->
              let idl, _, _ = DecodeVectorsIdl.current ()
              let path = Snapshots.repoFile DecodeVectorsIdl.generatedFile

              match Gen.fsharpModule DecodeVectorsIdl.moduleName idl (idl.Kinds |> List.map _.Tag) with
              | Error e -> failtestf "codegen refused the decode-vector vocabulary: %s" (CodegenError.describe e)
              | Ok src ->
                  Expect.equal
                      (File.ReadAllText(path).Replace("\r\n", "\n"))
                      src
                      "the committed module is not what the generator emits — regenerate it with: dotnet run --project tests/Fuaran.Core.Tests -- --regen-snapshots")

          testCase "the translated vocabulary is well-formed" (fun _ ->
              let idl, _, _ = DecodeVectorsIdl.current ()
              Expect.isEmpty (Declare.errors idl) "Declare.errors finds nothing")

          testCase "every vector is covered or excluded with the IDL's reason, and the exclusions are pinned" (fun _ ->
              let all = DecodeVectorsIdl.load ()
              let _, covered, excluded = DecodeVectorsIdl.current ()

              Expect.equal (covered.Length + excluded.Length) all.Length "every vector is accounted for"

              Expect.equal
                  (excluded |> List.map (fun (v, _) -> v.Name) |> List.sort)
                  (List.sort expectedExclusions)
                  "the excluded vectors are the pinned set"

              for v, reason in excluded do
                  Expect.isNotEmpty reason (sprintf "%s names its reason" v.Name)

              // Non-vacuity: the covered set reaches every code an IDL vocabulary can produce.
              let codes =
                  covered
                  |> List.choose (fun (_, v) -> DecodeVectorsIdl.expectedRefusal v |> Option.map fst)
                  |> Set.ofList

              for code in [ "InvalidJson"; "LimitExceeded"; "MissingField"; "WrongKind"; "UnknownTag" ] do
                  Expect.isTrue (codes.Contains code) (sprintf "some covered vector pins %s" code)

              Expect.isTrue
                  (covered |> List.exists (fun (_, v) -> v.Refusal.IsNone))
                  "and some covered vector is an accepted one")

          testCase
              "the interpreter, the compiled F# host and the TypeScript host answer every covered vector as it pins"
              (fun _ ->
                  let idl, covered, _ = DecodeVectorsIdl.current ()

                  let docs = covered |> List.map (fun (i, v) -> v, DecodeVectorsIdl.document i v)

                  let ts =
                      match tsAnswers idl (docs |> List.map snd) with
                      | Some answers -> answers
                      | None -> skiptest noNode

                  let findings =
                      List.zip docs ts
                      |> List.collect (fun ((v, doc), tsAnswer) ->
                          let want = pinned v

                          [ for host, got in
                                [ "interpreter", ofResult (Decode.decodeDetailed idl doc)
                                  "compiled F#", ofResult (DecodeVectorsGenerated.decodeNode doc)
                                  "TypeScript", tsAnswer ] do
                                if got <> want then
                                    sprintf
                                        "%s: the %s host answered %s, the vector pins %s"
                                        v.Name
                                        host
                                        (show got)
                                        (show want) ])

                  Expect.isEmpty findings (String.concat "\n" findings)) ]

// ---------------------------------------------------------------------------
// Sampled mutations of the certification vocabularies' documents.
// ---------------------------------------------------------------------------

type private Certified =
    {
        Name: string
        Idl: Idl
        Compiled: string -> Answer
        /// Members a mutation never reaches below, each a slot the three hosts do not read alike by
        /// design (see the module comment).
        Verbatim: string list
    }

let private certified: Certified list =
    [ { Name = "document"
        Idl = SecondDomainSpike.docIdl
        Compiled = fun s -> ofResult (DocGenerated.decodeNode s)
        Verbatim = [] }
      { Name = "score"
        Idl = ScoreDomainSpike.scoreIdl
        Compiled = fun s -> ofResult (ScoreGenerated.decodeNode s)
        Verbatim = [] }
      { Name = "reference"
        Idl = ReferenceIdl.refIdl
        Compiled = fun s -> ofResult (ReferenceGenerated.decodeNode s)
        Verbatim = [ "series" ] } ]

let private sentinel (v: JVal) =
    match v with
    | JStr "<closure>"
    | JStr "<opaque>" -> true
    | _ -> false

/// Every value in a document with its path, root first — except below a verbatim member.
let rec private positions (verbatim: string list) (path: PathSegment list) (v: JVal) =
    [ yield List.rev path, v
      match v with
      | JObj fields ->
          for k, x in fields do
              if not (List.contains k verbatim) then
                  yield! positions verbatim (PathSegment.Key k :: path) x
      | JArr items ->
          for i, x in List.indexed items do
              yield! positions verbatim (PathSegment.Index i :: path) x
      | _ -> () ]

/// The document with the value at `path` replaced by `f` of it (`None` deletes it, when its parent
/// is an object).
let rec private edit (path: PathSegment list) (f: JVal -> JVal option) (v: JVal) : JVal =
    match path, v with
    | [], _ -> f v |> Option.defaultValue v
    | [ PathSegment.Key k ], JObj fields ->
        JObj(
            fields
            |> List.choose (fun (n, x) ->
                if n = k then
                    f x |> Option.map (fun y -> n, y)
                else
                    Some(n, x))
        )
    | PathSegment.Key k :: rest, JObj fields ->
        JObj(fields |> List.map (fun (n, x) -> if n = k then n, edit rest f x else n, x))
    | PathSegment.Index i :: rest, JArr items ->
        JArr(items |> List.mapi (fun j x -> if j = i then edit rest f x else x))
    | _ -> v

let private kindOf (v: JVal) =
    match v with
    | JStr _ -> 0
    | JInt _ -> 1
    | JFloat _ -> 2
    | JBool _ -> 3
    | JArr _ -> 4
    | JObj _ -> 5

let private replacements =
    [ JStr "x"
      JInt 7
      JFloat 1.5
      JBool true
      JArr [ JInt 1 ]
      JObj [ "q", JInt 1 ] ]

/// One mutation of a valid document's bytes, or `None` where the drawn position is one the law
/// does not mutate.
let private mutate (rng: Random) (verbatim: string list) (bytes: string) : string option =
    match rng.Next 20 with
    | 0 ->
        // Truncated text — never between the two halves of a surrogate pair, which would make
        // the document ill-formed UTF-16 rather than truncated JSON.
        let cut = rng.Next(1, bytes.Length)

        let cut =
            if Char.IsHighSurrogate bytes.[cut - 1] then
                cut - 1
            else
                cut

        Some(bytes.Substring(0, max 1 cut))
    | 1 -> Some(bytes.Replace("{", "{\"__n\":null,"))
    | _ ->
        let doc =
            match Json.parse bytes with
            | Ok d -> d
            | Error e -> failtestf "a valid document did not parse: %s" e

        let spots = positions verbatim [] doc |> List.toArray
        let path, value = spots.[rng.Next spots.Length]

        if sentinel value then
            None
        else
            let mutated =
                match rng.Next 3, value with
                // Delete a member of an object — its value not a sentinel.
                | 0, JObj fields when not fields.IsEmpty ->
                    let name, member' = fields.[rng.Next fields.Length]

                    if sentinel member' || List.contains name verbatim then
                        None
                    else
                        Some(edit (path @ [ PathSegment.Key name ]) (fun _ -> None) doc)
                // A string no case names — at a discriminator or an enum, an unknown tag.
                | 1, JStr _ -> Some(edit path (fun _ -> Some(JStr "zz-unknown-337")) doc)
                // A value of another kind.
                | _ ->
                    let others =
                        replacements |> List.filter (fun r -> kindOf r <> kindOf value) |> List.toArray

                    Some(edit path (fun _ -> Some others.[rng.Next others.Length]) doc)

            mutated |> Option.map Canon.render

/// The mutated documents for one vocabulary, drawn from its sampled valid documents.
let private mutationsOf (c: Certified) (count: int) : string list =
    let rng = Random(337 + c.Name.Length)
    let tags = c.Idl.Kinds |> List.map _.Tag

    let valid =
        Sample.sampleNodes c.Idl tags (20261002 + c.Name.Length) 200
        |> List.choose (fun v -> Encode.encode c.Idl v |> Result.toOption)
        // The compiled host runs the hosted codec the other two do not; keep the documents all
        // three read, so a mutation is the only fault in it.
        |> List.filter (fun b -> c.Compiled b = Accepted)
        |> List.toArray

    seq {
        while true do
            yield mutate rng c.Verbatim valid.[rng.Next valid.Length]
    }
    |> Seq.choose id
    |> Seq.truncate count
    |> List.ofSeq

let private perVocabulary = 400

let private lawOn (c: Certified) =
    testCase
        (sprintf "%s: every sampled mutation gets one answer, one code and one path from all three hosts" c.Name)
        (fun _ ->
            let docs = mutationsOf c perVocabulary
            Expect.equal docs.Length perVocabulary "the mutation draw is the size asked for"

            let ts =
                match tsAnswers c.Idl docs with
                | Some answers -> answers
                | None -> skiptest noNode

            let answers =
                List.zip docs ts
                |> List.map (fun (doc, tsAnswer) ->
                    doc, ofResult (Decode.decodeDetailed c.Idl doc), c.Compiled doc, tsAnswer)

            let findings =
                answers
                |> List.collect (fun (doc, interp, fs, tsAnswer) ->
                    [ if fs <> interp then
                          sprintf "compiled F# %s, interpreter %s: %s" (show fs) (show interp) doc
                      if tsAnswer <> interp then
                          sprintf "TypeScript %s, interpreter %s: %s" (show tsAnswer) (show interp) doc ])

            Expect.isEmpty findings (String.concat "\n" (List.truncate 20 findings))

            // Non-vacuity: the draw refuses, and refuses for more than one reason.
            let codes =
                answers
                |> List.choose (fun (_, interp, _, _) ->
                    match interp with
                    | Refused(code, _) -> Some code
                    | Accepted -> None)

            Expect.isGreaterThan codes.Length (perVocabulary / 2) "most mutations are refusals"

            for code in [ "InvalidJson"; "MissingField"; "WrongKind" ] do
                Expect.contains codes code (sprintf "some mutation is refused as %s" code))

[<Tests>]
let mutationLaw =
    testList
        "Phase 337 — sampled mutations: the interpreter and both generated hosts refuse alike"
        [ for c in certified do
              lawOn c

          testCase "a refusal carries the interpreter's code, path and expectation, and the pre-337 sentence" (fun _ ->
              // A pinned example: an int slot given a string, two levels down a document node.
              let bytes = """{"id":"n","kind":"V05","v":{"a":"x"}}"""

              match
                  DecodeVectorsGenerated.decodeNode bytes,
                  Decode.decodeDetailed (let i, _, _ = DecodeVectorsIdl.current () in i) bytes
              with
              | Error fs, Error interp ->
                  Expect.equal fs.Code DecodeCode.WrongKind "the code"
                  Expect.equal fs.Path [ PathSegment.Key "v"; PathSegment.Key "a" ] "the path"

                  Expect.equal
                      (fs.Code, fs.Path, fs.Expected)
                      (interp.Code, interp.Path, interp.Expected)
                      "the interpreter's"

                  Expect.equal
                      (DecodeError.describe fs)
                      "expected an int"
                      "the generated layer's own sentence, unchanged"
              | other -> failtestf "both must refuse: %A" other) ]
