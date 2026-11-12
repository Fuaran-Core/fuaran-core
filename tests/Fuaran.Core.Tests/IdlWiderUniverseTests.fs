/// Phase 639 (DECISIONS.md D251) — the IDL's wider data universe: a 64-bit integer, a decimal, bytes, a
/// tuple, a map with non-string keys, a set, a time of day and a duration. What is held here: the
/// `idl.json` reading of every type and the named refusal of a date and time without an offset; each
/// scalar's one canonical spelling; the canonical order's laws; the encoder writing two equal sets as one
/// byte string and refusing a repeat; the declaration rules; the schema's patterns; the differ's classes;
/// and the conformance vectors (`conformance/idl/wider-universe.json`) answered alike by the interpreter,
/// the generated F# host and the generated TypeScript host.
module Fuaran.Core.Tests.IdlWiderUniverseTests

open System
open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen
open Fuaran.Core.Idl.Codegen.Types

let private field (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Aliases = []
      Keyed = false
      Presence = None
      Annotations = Annotations.Empty }

/// A vocabulary of one record holding `fields`.
let private recordIdl (fields: IdlField list) : Idl =
    { Kinds =
        [ { Tag = "K"
            Category = "probe"
            Annotations = Annotations.Empty
            Fields = fields } ]
      Unions = []
      Enums =
        [ { Name = "Day"
            Cases = [ "Monday"; "Tuesday" ]
            Wires = []
            CaseAnnotations = []
            Aliases = []
            Annotations = Annotations.Empty } ]
      Records =
        [ { Name = "R"
            Fields = fields
            Annotations = Annotations.Empty
            Inline = false } ]
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      Harden = HardenPolicy.Undeclared }

let private everyType: (string * IdlType) list =
    [ "int64", TInt64
      "decimal", TDecimal
      "bytes", TBytes
      "tuple", TTuple [ TInt; TStr ]
      "keyedMap", TKeyedMap(TEnum "Day", TDuration)
      "set", TSet(TTuple [ TBool; TInt64 ])
      "timeOfDay", TTimeOfDay
      "duration", TDuration ]

[<Tests>]
let artifact =
    testList
        "Phase 639 — idl.json reads every type of the wider data universe, and refuses a date and time without an offset"
        [ for tag, t in everyType do
              testCase (sprintf "a field of type '%s' reads back as it was declared" tag) (fun _ ->
                  let idl = recordIdl [ field "x" t Required ]

                  match Artifact.parse (Artifact.render idl) with
                  | Ok back -> Expect.equal back.Records idl.Records "the record reads back unchanged"
                  | Error e -> failtestf "refused: %s" e)

          testCase "a date and time without an offset is refused by name, with its reason, at its $type" (fun _ ->
              let text =
                  (Artifact.render (recordIdl [ field "x" TStr Required ]))
                      .Replace("\"$type\": \"str\"", "\"$type\": \"localDateTime\"")

              match Artifact.parseDetailed text with
              | Ok _ -> failtest "a date and time without an offset was admitted"
              | Error errors ->
                  let e = List.head errors
                  Expect.equal e.Code DecodeCode.NotAdmitted "the refusal is NotAdmitted"
                  Expect.equal (List.last e.Path) (PathSegment.Key "$type") "at the type's $type"
                  Expect.stringContains e.Message "offset" "the refusal says why") ]

/// Each string-borne scalar: the spellings admitted (each the one spelling of its value) and refused.
let private spellings: (string * (string -> bool) * string list * string list) list =
    [ "int64",
      ScalarText.isInt64,
      [ "0"
        "1"
        "-1"
        "9223372036854775807"
        "-9223372036854775808"
        "9007199254740993" ],
      [ ""
        "-0"
        "+1"
        "01"
        "1.0"
        "1e3"
        " 1"
        "9223372036854775808"
        "-9223372036854775809"
        "0x1" ]
      "decimal",
      ScalarText.isDecimal,
      [ "0"
        "-1"
        "12.25"
        "0.5"
        "-0.5"
        "79228162514264337593543950336"
        "0.0000000000000000000000000001" ],
      [ ""; "-0"; "00"; "1.50"; "1."; ".5"; "1e3"; "+1"; "0.0"; "1,5" ]
      "bytes",
      ScalarText.isBytes,
      [ ""; "AA=="; "AAE="; "AAEC"; "/+8="; "SGVsbG8gd29ybGQ=" ],
      [ "A"; "AA"; "AB=="; "AAF="; "A==="; "-_8="; "AA==AA=="; "AAE= " ]
      "timeOfDay",
      ScalarText.isTimeOfDay,
      [ "00:00:00"; "23:59:59"; "23:59:59.9999999"; "12:30:00.5"; "00:00:00.0000001" ],
      [ "24:00:00"
        "12:60:00"
        "12:00:60"
        "12:00"
        "12:00:00.0"
        "12:00:00.50"
        "12:00:00.00000001"
        "12:00:00Z"
        "1:00:00" ]
      "duration",
      ScalarText.isDuration,
      [ "0s"
        "1s"
        "-1s"
        "1.5s"
        "0.0000001s"
        "922337203685.4775807s"
        "-922337203685.4775808s" ],
      [ "-0s"
        "0"
        "1.0s"
        "01s"
        "1.00000001s"
        "922337203685.4775808s"
        "PT1S"
        "1m"
        "+1s"
        ".5s" ] ]

[<Tests>]
let canonicalSpellings =
    testList
        "Phase 639 — each string-borne scalar has one canonical spelling, and a reader admits it alone"
        [ for name, admits, good, bad in spellings do
              testCase (sprintf "%s: the canonical spellings are admitted and every other is refused" name) (fun _ ->
                  for s in good do
                      Expect.isTrue (admits s) (sprintf "%s should admit %A" name s)

                  for s in bad do
                      Expect.isFalse (admits s) (sprintf "%s should refuse %A" name s))

          testCase "the JSON Schema patterns admit exactly what the readers admit" (fun _ ->
              let byType =
                  [ "int64", TInt64
                    "decimal", TDecimal
                    "bytes", TBytes
                    "timeOfDay", TTimeOfDay
                    "duration", TDuration ]

              for name, admits, good, bad in spellings do
                  let t = byType |> List.find (fun (n, _) -> n = name) |> snd

                  let rec find (j: JVal) : string option =
                      match j with
                      | JObj ms ->
                          match ms |> List.tryPick (fun (k, v) -> if k = "pattern" then Some v else None) with
                          | Some(JStr p) -> Some p
                          | _ -> ms |> List.tryPick (snd >> find)
                      | JArr xs -> xs |> List.tryPick find
                      | _ -> None

                  let pattern =
                      match Gen.jsonSchema (recordIdl [ field "x" t Required ]) with
                      | Ok text ->
                          match Json.parse text |> Result.toOption |> Option.bind find with
                          | Some p -> p
                          | None -> failtestf "no pattern for %s" name
                      | Error e -> failtestf "schema refused: %s" (CodegenError.describe e)

                  let rx =
                      Text.RegularExpressions.Regex(pattern, Text.RegularExpressions.RegexOptions.ECMAScript)

                  for s in good @ bad do
                      Expect.equal (rx.IsMatch s) (admits s) (sprintf "%s pattern and reader disagree on %A" name s)) ]

/// The canonical texts of a sample of every orderable type the reference vocabulary reaches.
let private orderSample () : string list =
    let idl = ReferenceIdl.refIdl

    match Sample.trySampleNodes idl [ "Tally" ] 639 200 with
    | Error r -> failtestf "the sampler refused: %s" r.Describe
    | Ok nodes ->
        [ for n in nodes do
              match
                  Encode.encode idl n
                  |> Result.toOption
                  |> Option.bind (Json.parse >> Result.toOption)
              with
              | Some(JObj node) ->
                  match node |> List.tryFind (fun (k, _) -> k = "kind") with
                  | Some(_, JObj kind) ->
                      for name, v in kind do
                          match name, v with
                          | "tags", JArr xs -> yield! xs
                          | "grid", JArr ps ->
                              yield!
                                  ps
                                  |> List.choose (function
                                      | JArr [ k; _ ] -> Some k
                                      | _ -> None)
                          | _ -> ()
                  | _ -> ()
              | _ -> () ]
        |> List.map (Canonical.write Rendering.CrossHost)
        |> List.distinct

[<Tests>]
let canonicalOrder =
    testList
        "Phase 639 — the canonical order is a strict total order, the order of the texts' UTF-8 bytes"
        [ testCase "compare is reflexive-zero, antisymmetric and transitive, and zero exactly on equal texts" (fun _ ->
              let xs =
                  orderSample ()
                  @ [ "\"Z\""; "\"a\""; "\"￿\""; "\"\U0001F600\""; "[10,false]"; "[9,true]" ]

              let sign (n: int) = compare n 0

              for a in xs do
                  Expect.equal (ScalarText.compare a a) 0 "a text equals itself"

                  for b in xs do
                      let ab = ScalarText.compare a b
                      Expect.equal (sign ab) (-(sign (ScalarText.compare b a))) "antisymmetric"
                      Expect.equal (ab = 0) (a = b) "zero exactly on equal texts"

                      for c in xs do
                          if ab < 0 && ScalarText.compare b c < 0 then
                              Expect.isLessThan (ScalarText.compare a c) 0 "transitive")

          testCase "it is the order of the texts' UTF-8 bytes, a surrogate pair above U+FFFF" (fun _ ->
              let sign (n: int) = compare n 0
              let xs = orderSample () @ [ "\"￿\""; "\"\U0001F600\""; "\"\""; "\"é\"" ]

              let bytes (s: string) = Text.Encoding.UTF8.GetBytes s

              let byteOrder (a: string) (b: string) =
                  let x, y = bytes a, bytes b
                  let n = min x.Length y.Length

                  match Seq.zip x[.. n - 1] y[.. n - 1] |> Seq.tryFind (fun (p, q) -> p <> q) with
                  | Some(p, q) -> compare p q
                  | None -> compare x.Length y.Length

              for a in xs do
                  for b in xs do
                      Expect.equal (sign (ScalarText.compare a b)) (sign (byteOrder a b)) (sprintf "%A vs %A" a b))

          testCase
              "two equal sets authored in different orders encode to one byte string, and a repeat is refused"
              (fun _ ->
                  let idl =
                      recordIdl
                          [ field "x" (TSet TStr) Required
                            field "y" (TKeyedMap(TEnum "Day", TInt)) Required ]

                  let doc xs ys =
                      VRecord
                          [ "x", VList(List.map VStr xs)
                            "y", VList(ys |> List.map (fun (k, v) -> VList [ VEnum k; VInt v ])) ]

                  let enc v =
                      match v with
                      | VRecord fs -> Encode.encode idl (VNode("n", "K", fs))
                      | _ -> Error "not a record"

                  let a = enc (doc [ "b"; "a"; "\U0001F600"; "￿" ] [ "Tuesday", 2; "Monday", 1 ])
                  let b = enc (doc [ "￿"; "a"; "\U0001F600"; "b" ] [ "Monday", 1; "Tuesday", 2 ])
                  Expect.equal a b "one value, one byte string"

                  Expect.equal
                      a
                      (Ok
                          "{\"id\":\"n\",\"kind\":{\"$type\":\"K\",\"x\":[\"a\",\"b\",\"￿\",\"\U0001F600\"],\"y\":[[\"Monday\",1],[\"Tuesday\",2]]}}")
                      "in canonical order"

                  Expect.isError (enc (doc [ "a"; "a" ] [])) "a repeated element is refused"
                  Expect.isError (enc (doc [] [ "Monday", 1; "Monday", 2 ])) "a repeated key is refused") ]

[<Tests>]
let declaration =
    testList
        "Phase 639 — the declaration rules of the wider data universe"
        [ let refused (what: string) (t: IdlType) =
              testCase (sprintf "%s is refused at declaration" what) (fun _ ->
                  Expect.isNonEmpty (Declare.errors (recordIdl [ field "x" t Required ])) what)

          let admitted (what: string) (t: IdlType) =
              testCase (sprintf "%s is admitted" what) (fun _ ->
                  Expect.isEmpty (Declare.errors (recordIdl [ field "x" t Required ])) what)

          for tag, t in everyType do
              admitted (sprintf "a field of type '%s'" tag) t

          refused "a set of floats (NaN equals nothing)" (TSet TFloat)
          refused "a set of records (an object's text depends on a key order)" (TSet(TRecord "R"))
          refused "a map keyed by strings, which is `map`" (TKeyedMap(TStr, TInt))
          refused "a keyed map keyed by floats" (TKeyedMap(TFloat, TInt))
          refused "a tuple of one component" (TTuple [ TInt ])
          refused "a tuple holding a node" (TTuple [ TNode; TStr ])
          refused "a set of a tuple holding a float" (TSet(TTuple [ TInt; TFloat ])) ]

[<Tests>]
let differ =
    testList
        "Phase 639 — the differ classifies the wider data universe on the rules the other types follow"
        [ let classOf (before: IdlField list) (after: IdlField list) =
              match Diff.ofValues (recordIdl before, None) (recordIdl after, None) with
              | Ok v -> Diff.classLabel (Diff.verdictClass v)
              | Error e -> failtestf "classify: %s" e

          let breaking = Diff.classLabel VerdictClass.Breaking
          let additive = Diff.classLabel VerdictClass.Additive

          for tag, t in everyType do
              testCase (sprintf "adding an optional '%s' field is additive" tag) (fun _ ->
                  Expect.equal
                      (classOf [ field "a" TStr Required ] [ field "a" TStr Required; field "x" t Optional ])
                      additive
                      tag)

              testCase (sprintf "adding a required '%s' field is breaking" tag) (fun _ ->
                  Expect.equal
                      (classOf [ field "a" TStr Required ] [ field "a" TStr Required; field "x" t Required ])
                      breaking
                      tag)

          testCase
              "a 32-bit integer retyped as a 64-bit one is breaking: the wire spelling moves from a number to a string"
              (fun _ ->
                  Expect.equal
                      (classOf [ field "x" TInt Required ] [ field "x" TInt64 Required ])
                      breaking
                      "int to int64")

          testCase "a list retyped as a set is breaking: a reader now refuses a repeat and a disorder" (fun _ ->
              Expect.equal
                  (classOf [ field "x" (TList TStr) Required ] [ field "x" (TSet TStr) Required ])
                  breaking
                  "list to set") ]

/// The TypeScript host's answer for each document, as a JSON line: `{"ok": bytes}` or `{"code", "path"}`.
let private tsAnswers (docs: string list) : string list option =
    match Gen.typescriptModule ReferenceIdl.refIdl (ReferenceIdl.refIdl.Kinds |> List.map _.Tag) with
    | Error e -> failtestf "TypeScript codegen refused: %s" (CodegenError.describe e)
    | Ok src ->
        let harness =
            "\nconst __docs = ["
            + (docs |> List.map SourceLit.tsString |> String.concat ",\n")
            + "];\nfor (const d of __docs) {\n  const r = decodeNode(d);\n  console.log(JSON.stringify(r.ok ? { ok: encodeNode(r.value) } : { code: r.error.code, path: r.error.path }));\n}\n"

        let tmp =
            Path.Combine(Path.GetTempPath(), sprintf "fuaran-wider-%s.mjs" (Guid.NewGuid().ToString "N"))

        File.WriteAllText(tmp, src + harness)

        try
            match ChildProcess.run (ChildProcess.command "node" ("\"" + tmp + "\"")) with
            | Error _ -> None
            | Ok p ->
                if p.Exit <> 0 then
                    failtestf "node failed: %s" p.Stderr

                Some(
                    p.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map _.TrimEnd('\r')
                    |> List.ofArray
                )
        finally
            try
                File.Delete tmp
            with _ ->
                ()

let private expected (answer: Result<string, string * JVal list>) : string =
    match answer with
    | Ok bytes -> Canonical.write Rendering.CrossHost (JObj [ "ok", JStr bytes ])
    | Error(code, path) -> Canonical.write Rendering.CrossHost (JObj [ "code", JStr code; "path", JArr path ])

[<Tests>]
let vectors =
    testList
        "Phase 639 — the wider data universe's conformance vectors, answered alike on every host"
        [ testCase "the vectors carry round trips and refusals of every type" (fun _ ->
              let answers =
                  WiderUniverseCorpus.vectors |> List.map (snd >> WiderUniverseCorpus.answer)

              Expect.isGreaterThanOrEqual (answers |> List.filter Result.isOk |> List.length) 10 "round trips"
              Expect.isGreaterThanOrEqual (answers |> List.filter Result.isError |> List.length) 30 "refusals"

              for (name, doc), a in List.zip WiderUniverseCorpus.vectors answers do
                  match a with
                  | Ok bytes when name.StartsWith "every-type" ->
                      Expect.equal bytes doc "a canonical document re-encodes to itself"
                  | _ -> ())

          testCase "the generated F# host answers every vector as the interpreter does" (fun _ ->
              for name, doc in WiderUniverseCorpus.vectors do
                  let fsharp =
                      match ReferenceGenerated.decodeNode doc with
                      | Ok n -> Ok(ReferenceGenerated.encodeNode n)
                      | Error e ->
                          Error(
                              DecodeError.codeName e.Code,
                              e.Path
                              |> List.map (function
                                  | PathSegment.Key k -> JStr k
                                  | PathSegment.Index i -> JInt i)
                          )

                  Expect.equal (expected fsharp) (expected (WiderUniverseCorpus.answer doc)) name)

          testCase "the generated TypeScript host answers every vector as the interpreter does" (fun _ ->
              match tsAnswers (WiderUniverseCorpus.vectors |> List.map snd) with
              | None -> skiptest "node not on PATH — the TypeScript leg is not run here"
              | Some lines ->
                  Expect.equal lines.Length WiderUniverseCorpus.vectors.Length "one answer per vector"

                  for (name, doc), line in List.zip WiderUniverseCorpus.vectors lines do
                      let ts =
                          match Json.parse line with
                          | Ok j -> Canonical.write Rendering.CrossHost j
                          | Error e -> failtestf "%s: unreadable answer %s (%A)" name line e

                      Expect.equal ts (expected (WiderUniverseCorpus.answer doc)) name) ]
