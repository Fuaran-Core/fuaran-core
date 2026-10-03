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
/// One place a mutation is NOT made, a documented boundary rather than a tolerance: a hosted slot
/// declaring no wire form (the reference vocabulary's `series`) — the interpreter and the TypeScript
/// host carry it verbatim and only the compiled host runs its codec (Phase 252).
///
/// Phase 347 — the sentinel exclusion is GONE (both generated hosts read a sentinel slot by value
/// now, as the interpreter does), and the mutations draw the reader's corners too: a whole number
/// rewritten as a float token, a literal past the double range or past 2^53, a member repeated with
/// a value of another kind before or after it, and a member renamed `__proto__`. The corners are
/// also pinned one by one over `RefusalCornersIdl`, a vocabulary with a slot of every type a corner
/// lives at, which joins the certification vocabularies in the mutation law.
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

/// A JS string literal holding `s` exactly, every unit outside printable ASCII escaped — so a
/// document that is ill-formed UTF-16 (a lone surrogate, which no JSON renderer will write) reaches
/// the TypeScript host as the same units the other two hosts read.
let private jsString (s: string) : string =
    "\""
    + (s
       |> String.collect (fun c ->
           if c < ' ' || c > '~' || c = '"' || c = '\\' then
               sprintf "\\u%04x" (int c)
           else
               string c))
    + "\""

/// The generated TypeScript module for every kind of a vocabulary.
let private tsModuleOf (idl: Idl) : string =
    match Gen.typescriptModule idl (idl.Kinds |> List.map _.Tag) with
    | Ok src -> src
    | Error e -> failtestf "TypeScript codegen refused: %s" (CodegenError.describe e)

/// The TypeScript host's answer for every document, in order; `None` when node is absent.
let private tsAnswers (idl: Idl) (docs: string list) : Answer list option =
    let tsModule = tsModuleOf idl

    let harness =
        tsModule
        + "\nconst __docs = ["
        + (docs |> List.map jsString |> String.concat ", ")
        + "]"
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
        Verbatim = [ "series" ] }
      // Phase 347 — a slot of every type a reader corner lives at.
      { Name = "corners"
        Idl = RefusalCornersIdl.idl
        Compiled = fun s -> ofResult (RefusalCornersGenerated.decodeNode s)
        Verbatim = [] } ]

/// Whether a vocabulary declares a wire-visible sentinel slot.
let private declaresSentinel (idl: Idl) =
    idl.Kinds
    |> List.exists (fun k ->
        k.Fields
        |> List.exists (fun f ->
            match f.Type, f.Opt with
            | _, HostOnly -> false
            | (TClosure | TOpaque | TFn _), _ -> true
            | _ -> false))

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

/// A string no sampled document carries. A mutation that writes RAW text at a position puts this
/// there, renders, and replaces it: `Canon.render` can spell neither a whole number as a float token
/// nor a repeated key, and those are two of the corners the law draws.
let private marker = "__mutation_347__"

let private withRaw (path: PathSegment list) (raw: string) (doc: JVal) : string =
    (edit path (fun _ -> Some(JStr marker)) doc |> Canon.render).Replace("\"" + marker + "\"", raw)

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
    | draw ->
        let doc =
            match Json.parse bytes with
            | Ok d -> d
            | Error e -> failtestf "a valid document did not parse: %s" e

        let spots = positions verbatim [] doc |> List.toArray
        let path, value = spots.[rng.Next spots.Length]

        match draw, value, List.rev path with
        // Phase 347 — a whole number written as a FLOAT token: an int slot refuses `7.0` and `7e0`
        // (the reader reads a float), a float slot reads them.
        | 2, JInt i, _ -> Some(withRaw path (string i + (if rng.Next 2 = 0 then ".0" else "e0")) doc)
        // Phase 347 — a literal the reader refuses wherever it stands: past the double range, or an
        // integer past 2^53 that is not the canonical layout of a double.
        | 3, _, _ :: _ -> Some(withRaw path (if rng.Next 2 = 0 then "1e400" else "-12345678901234567890") doc)
        // Phase 347 — a member repeated with a value of another kind, before or after it: every
        // member read takes the first, and every map entry is checked.
        | 4, _, PathSegment.Key k :: _ ->
            let others =
                replacements |> List.filter (fun r -> kindOf r <> kindOf value) |> List.toArray

            let mine = Canon.render value
            let other = Canon.render others.[rng.Next others.Length]
            let key = Canon.render (JStr k)

            Some(
                withRaw
                    path
                    (if rng.Next 2 = 0 then
                         mine + "," + key + ":" + other
                     else
                         other + "," + key + ":" + mine)
                    doc
            )
        // Phase 347 — a member renamed `__proto__`: a map entry like any other, a record's
        // undeclared member.
        | 5, JObj fields, _ when not fields.IsEmpty ->
            let name, _ = fields.[rng.Next fields.Length]

            if List.contains name verbatim then
                None
            else
                Some(
                    Canon.render (
                        edit
                            path
                            (fun _ ->
                                Some(
                                    JObj(fields |> List.map (fun (n, x) -> if n = name then "__proto__", x else n, x))
                                ))
                            doc
                    )
                )
        | _ ->
            let mutated =
                match rng.Next 3, value with
                // Delete a member of an object.
                | 0, JObj fields when not fields.IsEmpty ->
                    let name, _ = fields.[rng.Next fields.Length]

                    if List.contains name verbatim then
                        None
                    else
                        Some(edit (path @ [ PathSegment.Key name ]) (fun _ -> None) doc)
                // A string no case names — at a discriminator or an enum an unknown tag, at a
                // sentinel slot a value it does not take.
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
                Expect.contains codes code (sprintf "some mutation is refused as %s" code)

            // Phase 347 — and the corners are drawn: a sentinel slot given a string it does not
            // take, a literal the reader refuses, a `__proto__` member.
            if declaresSentinel c.Idl then
                Expect.contains codes "OutOfRange" "some mutation is refused at a sentinel slot as OutOfRange"

            Expect.isTrue
                (docs
                 |> List.exists (fun d -> d.Contains "1e400" || d.Contains "-12345678901234567890"))
                "some mutation writes a literal the reader refuses"

            Expect.isTrue
                (docs |> List.exists (fun d -> d.Contains "\"__proto__\""))
                "some mutation writes a __proto__ member")

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

// ---------------------------------------------------------------------------
// Phase 347 — the corners, one by one.
// ---------------------------------------------------------------------------

let private corner (name: string) (value: string option) = RefusalCornersIdl.withMember name value

let private refused (code: string) (path: PathSegment list) = Refused(code, DecodePath.render path)

let private member' (name: string) = [ PathSegment.Key name ]

/// The nesting past the reader's cap, as a JSON value.
let private deep = String.replicate 600 "[" + String.replicate 600 "]"

/// Each corner: what it is, the document, and the answer the interpreter gives — which both
/// generated hosts must give too. Every answer is the F# reader's and the interpreter's walk, and
/// one is not what the shard proposed: a literal past the double range is refused by the READER
/// (`InvalidJson` at the root), never as `OutOfRange` at its slot, because the F# reader refuses it
/// before any slot is read.
let private corners: (string * string * Answer) list =
    [ "a whole number written 1.0 at an int slot", corner "count" (Some "1.0"), refused "WrongKind" (member' "count")
      "a whole number written 1e0 at an int slot", corner "count" (Some "1e0"), refused "WrongKind" (member' "count")
      "-0.0 at an int slot", corner "count" (Some "-0.0"), refused "WrongKind" (member' "count")
      "the integer token -0 at an int slot", corner "count" (Some "-0"), Accepted
      "a whole float token at a float slot", corner "ratio" (Some "2.0"), Accepted
      "a whole float token as a list item at an int slot",
      corner "items" (Some "[1,2e0]"),
      refused "WrongKind" [ PathSegment.Key "items"; PathSegment.Index 1 ]
      "a whole float token as a map entry at an int slot",
      corner "counts" (Some """{"a":1,"b":3.0}"""),
      refused "WrongKind" [ PathSegment.Key "counts"; PathSegment.Key "b" ]
      "an integer past Int32 at an int slot", corner "count" (Some "3000000000"), refused "WrongKind" (member' "count")
      "a literal past the double range at a float slot", corner "ratio" (Some "1e400"), refused "InvalidJson" []
      "a literal past the double range inside a json slot",
      corner "meta" (Some """{"x":-1e400}"""),
      refused "InvalidJson" []
      "an integer past 2^53 that is no double's canonical layout",
      corner "ratio" (Some "9007199254740993"),
      refused "InvalidJson" []
      "an integer past 2^53 that is the canonical layout of its double",
      corner "ratio" (Some "10000000000000000"),
      Accepted
      "a 17-digit integer that is the canonical layout of its double",
      corner "ratio" (Some "12345678901234568"),
      Accepted
      "an 18-digit integer, which the canonical layout writes with an exponent",
      corner "ratio" (Some "100000000000000000"),
      refused "InvalidJson" []
      "a number token outside the JSON grammar", corner "ratio" (Some "1."), refused "InvalidJson" []
      "a repeated member whose FIRST value is of another kind",
      corner "count" (Some "\"x\",\"count\":1"),
      refused "WrongKind" (member' "count")
      "a repeated member whose SECOND value is of another kind", corner "count" (Some "1,\"count\":\"x\""), Accepted
      "a repeated map key whose second value is of another kind",
      corner "counts" (Some """{"a":1,"a":"x"}"""),
      refused "WrongKind" [ PathSegment.Key "counts"; PathSegment.Key "a" ]
      "map entries refused in document order when an index-like key comes last",
      corner "counts" (Some """{"b":"x","1":"y"}"""),
      refused "WrongKind" [ PathSegment.Key "counts"; PathSegment.Key "b" ]
      "a repeated discriminator", corner "kind" (Some "\"Corner\",\"kind\":\"Nope\""), Accepted
      "a __proto__ map key", corner "counts" (Some """{"__proto__":1}"""), Accepted
      "a __proto__ map key of the wrong kind",
      corner "counts" (Some """{"__proto__":"x"}"""),
      refused "WrongKind" [ PathSegment.Key "counts"; PathSegment.Key "__proto__" ]
      "a required closure absent", corner "onClick" None, refused "MissingField" (member' "onClick")
      "a required closure given the opaque sentinel",
      corner "onClick" (Some "\"<opaque>\""),
      refused "OutOfRange" (member' "onClick")
      "a required closure given another kind", corner "onClick" (Some "7"), refused "WrongKind" (member' "onClick")
      "an optional closure absent", corner "onHover" None, Accepted
      "an optional closure given another string",
      corner "onHover" (Some "\"nope\""),
      refused "OutOfRange" (member' "onHover")
      "a typed handler absent", corner "onPick" None, refused "MissingField" (member' "onPick")
      "a typed handler given another string", corner "onPick" (Some "\"x\""), refused "OutOfRange" (member' "onPick")
      "an opaque slot given the closure sentinel",
      corner "raw" (Some "\"<closure>\""),
      refused "OutOfRange" (member' "raw")
      "an opaque slot absent", corner "raw" None, refused "MissingField" (member' "raw")
      "a raw control character in a string", corner "meta" (Some("\"a" + string (char 1) + "b\"")), Accepted
      "a lone surrogate written as an escape", corner "meta" (Some "\"\\ud800\""), refused "InvalidJson" []
      "a lone surrogate written raw", corner "meta" (Some("\"" + string (char 0xDC00) + "\"")), refused "InvalidJson" []
      "null in a json slot", corner "meta" (Some "null"), refused "InvalidJson" []
      "nesting past the reader's cap", corner "meta" (Some deep), refused "LimitExceeded" []
      "a malformed number met before nesting past the cap",
      (corner "meta" (Some deep)).Replace("\"count\":1", "\"count\":01"),
      refused "InvalidJson" []
      "trailing characters", RefusalCornersIdl.valid + " x", refused "InvalidJson" [] ]

/// Run a script under node and parse its one line of JSON output; `None` when node is absent.
let private nodeJson (script: string) : JVal option =
    runNode script
    |> Option.map (fun stdout ->
        match Json.parse (stdout.Trim()) with
        | Ok j -> j
        | Error e -> failtestf "the harness printed a non-JSON line (%s): %s" e stdout)

[<Tests>]
let cornerTests =
    testList
        "Phase 347 — the refusal corners through the interpreter and both generated hosts"
        [ testCase "drift guard: the generator still reproduces the committed RefusalCornersGenerated.fs" (fun _ ->
              let path = Snapshots.repoFile RefusalCornersIdl.generatedFile

              match Gen.fsharpModule RefusalCornersIdl.moduleName RefusalCornersIdl.idl [ "Corner" ] with
              | Error e -> failtestf "codegen refused the corner vocabulary: %s" (CodegenError.describe e)
              | Ok src ->
                  Expect.equal
                      (File.ReadAllText(path).Replace("\r\n", "\n"))
                      src
                      "the committed module is not what the generator emits — regenerate it with: dotnet run --project tests/Fuaran.Core.Tests -- --regen-snapshots")

          testCase
              "the corner vocabulary is well-formed and its valid document is accepted by all three hosts"
              (fun _ ->
                  Expect.isEmpty (Declare.errors RefusalCornersIdl.idl) "Declare.errors finds nothing"
                  let doc = RefusalCornersIdl.valid
                  Expect.equal (ofResult (Decode.decodeDetailed RefusalCornersIdl.idl doc)) Accepted "the interpreter"
                  Expect.equal (ofResult (RefusalCornersGenerated.decodeNode doc)) Accepted "the compiled F# host"

                  match tsAnswers RefusalCornersIdl.idl [ doc ] with
                  | Some answers -> Expect.equal answers [ Accepted ] "the TypeScript host"
                  | None -> skiptest noNode)

          testCase "every corner gets the pinned answer — one code, one path — from all three hosts" (fun _ ->
              let idl = RefusalCornersIdl.idl

              let ts =
                  match tsAnswers idl (corners |> List.map (fun (_, doc, _) -> doc)) with
                  | Some answers -> answers
                  | None -> skiptest noNode

              let findings =
                  List.zip corners ts
                  |> List.collect (fun ((what, doc, want), tsAnswer) ->
                      [ for host, got in
                            [ "interpreter", ofResult (Decode.decodeDetailed idl doc)
                              "compiled F#", ofResult (RefusalCornersGenerated.decodeNode doc)
                              "TypeScript", tsAnswer ] do
                            if got <> want then
                                sprintf
                                    "%s: the %s host answered %s, the corner pins %s"
                                    what
                                    host
                                    (show got)
                                    (show want) ])

              Expect.isEmpty findings (String.concat "\n" findings))

          testCase "a __proto__ map key is an entry, and no document reaches Object.prototype" (fun _ ->
              let doc =
                  (corner "counts" (Some """{"__proto__":7,"constructor":8,"k":1}"""))
                      .Replace("\"meta\":{\"x\":1}", "\"meta\":{\"__proto__\":{\"polluted\":true}}")

              // The compiled host: the key is an entry like any other.
              match RefusalCornersGenerated.decodeNode doc with
              | Ok n ->
                  match n.Kind with
                  | RefusalCornersGenerated.NodeKind.Corner c ->
                      Expect.equal (Map.tryFind "__proto__" c.Counts) (Some 7) "the compiled host keeps the entry"
                      Expect.equal (RefusalCornersGenerated.encodeNode n) doc "and writes it back"
              | Error e -> failtestf "the compiled host refused: %s" (DecodeError.describe e)

              let script =
                  tsModuleOf RefusalCornersIdl.idl
                  + "\nconst r = decodeNode("
                  + Canon.render (JStr doc)
                  + ");\nconst m = r.value.counts;\nconsole.log(JSON.stringify({\n"
                  + "  ok: r.ok,\n"
                  + "  keys: Object.keys(m).sort(),\n"
                  + "  proto: m['__proto__'],\n"
                  + "  ctor: m['constructor'],\n"
                  + "  nullPrototype: Object.getPrototypeOf(m) === null,\n"
                  + "  metaOwn: Object.prototype.hasOwnProperty.call(r.value.meta, '__proto__'),\n"
                  + "  metaPrototype: Object.getPrototypeOf(r.value.meta) === Object.prototype,\n"
                  + "  clean: ({}).polluted === undefined && !('polluted' in Object.prototype),\n"
                  + "  bytes: encodeNode(r.value)\n"
                  + "}));\n"

              match nodeJson script with
              | None -> skiptest noNode
              | Some o ->
                  let get k =
                      Decoder.tryMember k o
                      |> Option.defaultWith (fun () -> failtestf "no %s in %A" k o)

                  Expect.equal (get "ok") (JBool true) "the TypeScript host accepts the document"

                  Expect.equal
                      (get "keys")
                      (JArr [ JStr "__proto__"; JStr "constructor"; JStr "k" ])
                      "`__proto__` and `constructor` are entries of the decoded map"

                  Expect.equal (get "proto") (JInt 7) "the `__proto__` entry holds its value"
                  Expect.equal (get "ctor") (JInt 8) "the `constructor` entry holds its value"
                  Expect.equal (get "nullPrototype") (JBool true) "the decoded map has no prototype to reach"
                  Expect.equal (get "metaOwn") (JBool true) "a json slot's `__proto__` member is an own member"
                  Expect.equal (get "metaPrototype") (JBool true) "and that object's prototype is untouched"
                  Expect.equal (get "clean") (JBool true) "Object.prototype is untouched"
                  Expect.equal (get "bytes") (JStr doc) "the TypeScript host writes the document back byte for byte")

          testCase
              "a repeated key keeps its first value on all three hosts, as a member read and as a map entry"
              (fun _ ->
                  let doc =
                      (corner "counts" (Some """{"a":1,"a":2}""")).Replace("\"count\":1", "\"count\":5,\"count\":6")

                  match Decode.decodeDetailed RefusalCornersIdl.idl doc with
                  | Ok(VNode(_, _, fields)) ->
                      Expect.equal
                          (fields |> List.tryFind (fun (k, _) -> k = "count") |> Option.map snd)
                          (Some(VInt 5))
                          "the interpreter reads the first member"

                      match fields |> List.tryFind (fun (k, _) -> k = "counts") with
                      | Some(_, VMap entries) ->
                          Expect.equal
                              (entries |> List.tryFind (fun (k, _) -> k = "a") |> Option.map snd)
                              (Some(VInt 1))
                              "and a map read finds the first entry"
                      | other -> failtestf "the interpreter's map: %A" other
                  | other -> failtestf "the interpreter: %A" other

                  match RefusalCornersGenerated.decodeNode doc with
                  | Ok n ->
                      match n.Kind with
                      | RefusalCornersGenerated.NodeKind.Corner c ->
                          Expect.equal c.Count 5 "the compiled host reads the first member"
                          Expect.equal (Map.tryFind "a" c.Counts) (Some 1) "and keeps the first map entry"
                  | Error e -> failtestf "the compiled host refused: %s" (DecodeError.describe e)

                  let script =
                      tsModuleOf RefusalCornersIdl.idl
                      + "\nconst r = decodeNode("
                      + Canon.render (JStr doc)
                      + ");\nconsole.log(JSON.stringify({ ok: r.ok, count: r.value.count, a: r.value.counts.a }));\n"

                  match nodeJson script with
                  | None -> skiptest noNode
                  | Some o ->
                      Expect.equal (Decoder.tryMember "ok" o) (Some(JBool true)) "the TypeScript host accepts it"

                      Expect.equal
                          (Decoder.tryMember "count" o)
                          (Some(JInt 5))
                          "the TypeScript host reads the first member"

                      Expect.equal (Decoder.tryMember "a" o) (Some(JInt 1)) "and keeps the first map entry") ]

// ---------------------------------------------------------------------------
// Phase 348 — the declaration file types the refusal as the object it is, and the names a
// generated TypeScript host cannot hold as data are the IDL's to refuse.
// ---------------------------------------------------------------------------

/// The generated declaration file for every kind of a vocabulary.
let private tsDeclarationsOf (idl: Idl) : string =
    match Gen.typescriptDeclarations idl (idl.Kinds |> List.map _.Tag) with
    | Ok src -> src
    | Error e -> failtestf "TypeScript declarations refused: %s" (CodegenError.describe e)

/// A fresh directory holding `files`, removed after `f` runs.
let private withDir (files: (string * string) list) (f: string -> 'a) : 'a =
    let dir =
        Path.Combine(Path.GetTempPath(), sprintf "fuaran-348-%s" (Guid.NewGuid().ToString "N"))

    Directory.CreateDirectory dir |> ignore

    for name, text in files do
        File.WriteAllText(Path.Combine(dir, name), text)

    try
        f dir
    finally
        try
            Directory.Delete(dir, true)
        with _ ->
            ()

/// Run `node <args>` in `dir`: exit code, stdout and stderr; `None` when node is not on PATH.
let private nodeIn (dir: string) (args: string) : (int * string * string) option =
    let psi = ChildProcess.redirected "node" args
    psi.WorkingDirectory <- dir

    match
        (try
            Some(Diagnostics.Process.Start psi)
         with _ ->
             None)
    with
    | None -> None
    | Some p ->
        let stdout = p.StandardOutput.ReadToEnd()
        let stderr = p.StandardError.ReadToEnd()
        p.WaitForExit()
        Some(p.ExitCode, stdout, stderr)

/// The TypeScript compiler the type-check leg runs: `tsc.js` (the `typescript` package's
/// `lib/tsc.js`) named by `FUARAN_CORE_TSC`. This repository carries no JavaScript dependency, so
/// the compiler is supplied by the environment; with none, the leg says so and skips.
let private tscJs () : string option =
    match Environment.GetEnvironmentVariable "FUARAN_CORE_TSC" with
    | null
    | "" -> None
    | p when File.Exists p -> Some p
    | p -> failtestf "FUARAN_CORE_TSC names %s, which does not exist" p

/// A consumer of the generated declarations: it reads every member of the refusal, narrows its
/// code over the closed set, and asserts — through `@ts-expect-error`, which is itself an error when
/// the line below it compiles — that the refusal is not typed as a string and that a code outside
/// the set does not type.
let private consumer =
    String.concat
        "\n"
        [ "import { decodeNode, encodeNode, type DecodeRefusal } from './generated.mjs';"
          "const r = decodeNode('{}');"
          "if (!r.ok) {"
          "  const e: DecodeRefusal = r.error;"
          "  const code: 'InvalidJson' | 'MissingField' | 'WrongKind' | 'UnknownTag' | 'OutOfRange' | 'UndeclaredMember' | 'LimitExceeded' | 'NotAdmitted' | 'SchemaFault' = e.code;"
          "  const steps: Array<string | number> = e.path;"
          "  const expected: string = e.expected;"
          "  const message: string = e.message;"
          "  // @ts-expect-error - the refusal is an object, not the string it was declared as before Phase 348"
          "  const asText: string = r.error;"
          "  // @ts-expect-error - a code outside the closed set does not type"
          "  const other: DecodeRefusal['code'] = 'Bogus';"
          "  console.log(JSON.stringify([code, steps, expected, message, asText, other]));"
          "} else {"
          "  console.log(encodeNode(r.value));"
          "}"
          "" ]

[<Tests>]
let declarationTests =
    testList
        "Phase 348 — the generated declarations type the decode refusal as it is"
        [ testCase "the declaration file exports DecodeRefusal and every decode signature names it" (fun _ ->
              let d = tsDeclarationsOf RefusalCornersIdl.idl

              let codes =
                  DecodeError.codes
                  |> List.map (fun c -> "\"" + DecodeError.codeName c + "\"")
                  |> String.concat " | "

              Expect.stringContains
                  d
                  ("export type DecodeRefusal = { code: "
                   + codes
                   + "; path: Array<string | number>; expected: string; message: string };")
                  "one exported refusal type, its code the closed set"

              Expect.stringContains
                  d
                  "export declare function decodeNode(s: string): { ok: true; value: Node } | { ok: false; error: DecodeRefusal };"
                  "decodeNode's refusal is the exported type"

              Expect.isFalse (d.Contains "error: string") "no decode signature types the refusal as a string")

          testCase "a vocabulary type spelled like a name the declaration file declares is refused, naming it" (fun _ ->
              for name in [ "DecodeRefusal"; "Node"; "CornerSpec" ] do
                  let idl =
                      { RefusalCornersIdl.idl with
                          Records = RefusalCornersIdl.idl.Records @ [ { Name = name; Fields = [] } ]
                          Kinds =
                              RefusalCornersIdl.idl.Kinds
                              |> List.map (fun k ->
                                  { k with
                                      Fields =
                                          k.Fields
                                          @ [ { Name = "extra"
                                                Type = TRecord name
                                                Opt = Optional
                                                Annotations = Annotations.Empty } ] }) }

                  match Gen.typescriptDeclarations idl [ "Corner" ] with
                  | Error(CodegenError.UnsupportedConstruct(construct, _, _)) ->
                      Expect.stringContains construct (sprintf "'%s'" name) "the refusal names the type"
                  | other -> failtestf "%s: expected a typed refusal, got %A" name other)

          testCase "the runtime refusal has exactly the declared members, of the declared types" (fun _ ->
              // What the declaration says, held to what the module returns: for refusals of several
              // codes, the object's own keys are the four declared, `code` is in the declared set,
              // `path` holds only strings and numbers, and the other two are strings.
              let docs =
                  [ "not json"
                    "{}"
                    RefusalCornersIdl.valid.Replace("\"count\":", "\"count\":\"x\",\"was\":") ]

              let script =
                  tsModuleOf RefusalCornersIdl.idl
                  + "\nconst __out = [];\nfor (const d of "
                  + Canon.render (JArr(docs |> List.map JStr))
                  + ") {\n"
                  + "  const r = decodeNode(d);\n"
                  + "  if (r.ok) { __out.push('accepted'); continue; }\n"
                  + "  const e = r.error;\n"
                  + "  __out.push({ keys: Object.keys(e).sort(), code: e.code, pathOk: Array.isArray(e.path) && e.path.every((s) => typeof s === 'string' || typeof s === 'number'), strings: typeof e.expected === 'string' && typeof e.message === 'string' });\n"
                  + "}\nconsole.log(JSON.stringify(__out));\n"

              match nodeJson script with
              | None -> skiptest noNode
              | Some(JArr answers) ->
                  let declared = DecodeError.codes |> List.map DecodeError.codeName |> Set.ofList
                  Expect.equal answers.Length docs.Length "one answer per document"

                  let codes =
                      [ for a in answers do
                            match a with
                            | JStr "accepted" -> failtest "a refusal document was accepted"
                            | o ->
                                Expect.equal
                                    (Decoder.tryMember "keys" o)
                                    (Some(JArr [ JStr "code"; JStr "expected"; JStr "message"; JStr "path" ]))
                                    "exactly the four declared members"

                                Expect.equal
                                    (Decoder.tryMember "pathOk" o)
                                    (Some(JBool true))
                                    "path: string | number steps"

                                Expect.equal
                                    (Decoder.tryMember "strings" o)
                                    (Some(JBool true))
                                    "expected and message are strings"

                                match Decoder.tryMember "code" o with
                                | Some(JStr c) ->
                                    Expect.isTrue (declared.Contains c) (sprintf "code %s is in the declared set" c)
                                    c
                                | other -> failtestf "code: %A" other ]

                  Expect.equal (List.distinct codes |> List.length) 3 "the probe drew three different codes"
              | Some other -> failtestf "the harness printed %A" other)

          testCase "a consumer type-checks against the generated declarations and reads the refusal's fields" (fun _ ->
              // The compiler leg. It needs a TypeScript compiler, which this repository does not carry
              // (FUARAN_CORE_TSC names one); the cases above hold the declaration's text and the
              // runtime shape without one, and what they cannot prove — that a compiler accepts the
              // declaration file and a consumer of it — is this case's alone.
              match tscJs () with
              | None -> skiptest "FUARAN_CORE_TSC names no TypeScript compiler — the type-check leg cannot run"
              | Some tsc ->
                  let files =
                      [ "generated.mjs", tsModuleOf RefusalCornersIdl.idl
                        "generated.d.mts", tsDeclarationsOf RefusalCornersIdl.idl
                        "consumer.mts", consumer ]

                  withDir files (fun dir ->
                      let args =
                          sprintf
                              "\"%s\" --noEmit --strict --target es2022 --module nodenext --moduleResolution nodenext consumer.mts"
                              tsc

                      match nodeIn dir args with
                      | None -> skiptest noNode
                      | Some(code, stdout, stderr) ->
                          Expect.equal code 0 (sprintf "tsc accepts the consumer: %s %s" stdout stderr)

                          // The falsifier: the same consumer against the declaration as it stood
                          // (`error: string`) fails to compile.
                          let old =
                              (tsDeclarationsOf RefusalCornersIdl.idl)
                                  .Replace("{ ok: false; error: DecodeRefusal }", "{ ok: false; error: string }")

                          File.WriteAllText(Path.Combine(dir, "generated.d.mts"), old)

                          match nodeIn dir args with
                          | Some(code, _, _) ->
                              Expect.notEqual code 0 "the pre-348 declaration fails the same consumer"
                          | None -> skiptest noNode)) ]

/// A one-kind vocabulary whose kind carries a required `a` and an optional member named `name` —
/// built in code, so `Declare.errors` (which refuses most of these names) never sees it.
let private oneOptional (name: string) : Idl =
    { RefusalCornersIdl.idl with
        Kinds =
            [ { Tag = "K"
                Category = "k"
                Annotations = Annotations.Empty
                Fields =
                  [ { Name = "a"
                      Type = TStr
                      Opt = Required
                      Annotations = Annotations.Empty }
                    { Name = name
                      Type = TStr
                      Opt = Optional
                      Annotations = Annotations.Empty } ] } ]
        Unions = []
        Enums = []
        Records = []
        Defaults = []
        NodeFields = [] }

[<Tests>]
let inheritedNameTests =
    testList
        "Phase 348 — a field name a generated TypeScript host cannot hold as data is refused"
        [ testCase "the evidence the rule rests on: what the TypeScript host does with each name" (fun _ ->
              // For each name, under node: a document carrying the member, decoded and re-encoded;
              // one without it; and a value built in code without it (what the declaration's `?:`
              // allows). A name is SAFE when all three give back the canonical bytes. This is the
              // measurement DECISIONS.md D114 records; if a host change ever makes a refused name
              // safe, this goes red and the rule is re-examined rather than left standing.
              let probe (name: string) : bool option =
                  let idl = oneOptional name
                  let disc = idl.Wire.Discriminator

                  let present =
                      Canon.render (JObj [ "id", JStr "x"; disc, JStr "K"; "a", JStr "s"; name, JStr "v" ])

                  let absent = Canon.render (JObj [ "id", JStr "x"; disc, JStr "K"; "a", JStr "s" ])

                  let script =
                      tsModuleOf idl
                      + "\nconst __rt = (d) => { try { const r = decodeNode(d); return r.ok && encodeNode(r.value) === d; } catch (e) { return false; } };\n"
                      + "const __built = (v) => { try { return encodeNode(v) === "
                      + Canon.render (JStr absent)
                      + "; } catch (e) { return false; } };\n"
                      + "const __absent = decodeNode("
                      + Canon.render (JStr absent)
                      + ");\n"
                      + "const __plain = __absent.ok ? JSON.parse(JSON.stringify(__absent.value)) : null;\n"
                      + "console.log(JSON.stringify(__rt("
                      + Canon.render (JStr present)
                      + ") && __rt("
                      + Canon.render (JStr absent)
                      + ") && __plain !== null && __built(__plain)));\n"

                  nodeJson script
                  |> Option.map (function
                      | JBool b -> b
                      | other -> failtestf "%s: the harness printed %A" name other)

              let verdicts =
                  [ "__proto__"
                    "constructor"
                    "toString"
                    "valueOf"
                    "hasOwnProperty"
                    "prototype"
                    "proto" ]
                  |> List.map (fun n -> n, probe n)

              if verdicts |> List.exists (fun (_, v) -> v.IsNone) then
                  skiptest noNode

              let safe = verdicts |> List.filter (fun (_, v) -> v = Some true) |> List.map fst

              Expect.equal
                  safe
                  [ "prototype"; "proto" ]
                  "exactly the admitted names round-trip through the TypeScript host") ]
