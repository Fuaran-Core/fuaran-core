module Fuaran.Core.Tests.IdlSpecDecoderTests

// ---------------------------------------------------------------------------
// Phase 377 — the collecting decoders and the public per-spec entries the F# generator emits on
// request (`Gen.Derivation.SpecDecoders`).
//
// Four kinds of evidence:
//   1. OPT-IN and REACH. Every in-repository vocabulary emits the decoders; the emission without
//      them is a prefix of the emission with them (so every committed module that does not
//      request them is unchanged); the committed reference-vocabulary module is what the generator
//      emits (the drift guard — the derivations module's own guard lives in IdlDeriveTests).
//   2. ONE MULTI-DEFECT INPUT PER FIELD SHAPE — missing, wrong type, nested, list element, map key —
//      over the compiled derivations module, each asserting the FULL defect list, codes, paths and
//      order. Each is also the GO-RED: the short-circuiting entry, on the same input, still answers
//      only the first of them.
//   3. THE CONSERVATIVE-EXTENSION LAW over every single and paired mutation of a valid document:
//      both forms agree on success, an `Ok` is the same tree, a refusal's first defect IS the
//      short-circuiting defect, and every defect's path resolves in the document.
//   4. THE SUPPORT CHANNEL over the compiled reference module: a host projection, a case refine and
//      a transparent case.
// ---------------------------------------------------------------------------

open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Tests.DeriveGenerated

module Ref = Fuaran.Core.Tests.SpecDecodersGenerated

let private parse (s: string) : JVal =
    match Json.parse s with
    | Ok j -> j
    | Error e -> failtestf "test input is not JSON: %s" e

/// A defect as the two fields a consumer branches on: its code and its rendered path.
let private shape (e: DecodeError) : string * string =
    DecodeError.codeName e.Code, DecodePath.render e.Path

let private shapes (es: DecodeError list) = es |> List.map shape

/// The collecting form's full list, refusing a success.
let private allOf (r: Result<'T, DecodeError list>) : DecodeError list =
    match r with
    | Error es -> es
    | Ok _ -> failtest "expected the collecting decoder to refuse"

/// The short-circuiting form's one defect, refusing a success.
let private firstOf (r: Result<'T, DecodeError>) : DecodeError =
    match r with
    | Error e -> e
    | Ok _ -> failtest "expected the short-circuiting decoder to refuse"

/// The field-shape check: the collecting list is exactly `expected`, in order, and the
/// short-circuiting form answers only its head (the go-red: one defect where there are several).
let private expectDefects
    (expected: (string * string) list)
    (collecting: Result<'T, DecodeError list>)
    (shortCircuit: Result<'T, DecodeError>)
    =
    let all = allOf collecting
    Expect.equal (shapes all) expected "the collecting decoder answers every defect, in the documented order"
    Expect.isGreaterThan expected.Length 1 "a multi-defect input"
    let first = firstOf shortCircuit
    Expect.equal first all.Head "the short-circuiting decoder answers the FIRST defect, and only that"
    Expect.notEqual [ shape first ] (shapes all) "go-red: the short-circuiting form does not collect"

let private missing (p: string) = "MissingField", p
let private wrongKind (p: string) = "WrongKind", p
let private unknownTag (p: string) = "UnknownTag", p

/// A valid derivations document touching every field shape: a node list, a case table, an
/// optional node, a map of nodes, the envelope's annotation, a self-recursive union through a
/// list, a map, an option and a record, a handler sentinel and an enum away from its default.
let private sampleText =
    """{"id":"root","annotation":{"id":"a1","kind":{"$type":"Note","text":"aside"}},"kind":{"$type":"Section","title":"Plan","owner":{"name":"lead"},"estimate":{"$type":"Hours","amount":2.5},"children":[{"id":"c1","kind":{"$type":"Choice","branches":[{"label":"a","body":{"id":"b1","kind":{"$type":"Note","text":"x"}}}],"otherwise":{"id":"o1","kind":{"$type":"Note","text":"y","priority":"High"}},"rule":{"$type":"AllOf","rules":[{"$type":"Always"},{"$type":"Equals","key":"k","value":"v"},{"$type":"Named","rules":{"m":{"$type":"Not","rule":{"$type":"Always"}}}},{"$type":"Guarded","guard":{"rule":{"$type":"Maybe","inner":{"$type":"Always"}},"note":"n"}}]}}},{"id":"t1","kind":{"$type":"Task","name":"T","due":{"$type":"Always"},"trigger":{"$type":"Timer","owner":{"name":"me"},"every":5,"fire":"<closure>"},"hint":{"id":"h1","kind":{"$type":"Note","text":"z"}},"extras":{"e":{"id":"x1","kind":{"$type":"Note","text":"w"}}},"owner":{"priority":"Low"}}}]}}"""

/// Every value position in a document, root-first (the root itself excluded).
let rec private positions (j: JVal) : PathSegment list list =
    match j with
    | JObj fs ->
        fs
        |> List.collect (fun (k, v) ->
            [ PathSegment.Key k ]
            :: (positions v |> List.map (fun p -> PathSegment.Key k :: p)))
    | JArr xs ->
        xs
        |> List.mapi (fun i v ->
            [ PathSegment.Index i ]
            :: (positions v |> List.map (fun p -> PathSegment.Index i :: p)))
        |> List.concat
    | _ -> []

/// The document with the value at `path` replaced by `f`'s answer (`None` removes a member, or
/// leaves an array item in place).
let rec private rewrite (path: PathSegment list) (f: JVal -> JVal option) (j: JVal) : JVal =
    match path, j with
    | [ PathSegment.Key k ], JObj fs ->
        JObj(
            fs
            |> List.choose (fun (n, v) ->
                if n = k then
                    f v |> Option.map (fun v' -> n, v')
                else
                    Some(n, v))
        )
    | PathSegment.Key k :: rest, JObj fs ->
        JObj(fs |> List.map (fun (n, v) -> if n = k then n, rewrite rest f v else n, v))
    | [ PathSegment.Index i ], JArr xs -> JArr(xs |> List.mapi (fun n v -> if n = i then defaultArg (f v) v else v))
    | PathSegment.Index i :: rest, JArr xs -> JArr(xs |> List.mapi (fun n v -> if n = i then rewrite rest f v else v))
    | _ -> j

let private isPrefix (a: PathSegment list) (b: PathSegment list) =
    a.Length <= b.Length && List.truncate a.Length b = a

/// The conservative-extension law at one document: the two forms agree on success, an `Ok` is the
/// same tree (compared through its encoding — a handler has no equality), and a refusal's first
/// defect is the short-circuiting defect; every collected defect's path resolves in the document.
let private agrees (doc: JVal) : string option =
    match decodeNodeJson doc, decodeNodeJsonAll doc with
    | Ok a, Ok b when encodeNodeJson a = encodeNodeJson b -> None
    | Ok _, Ok _ -> Some "both decoded, to different trees"
    | Ok _, Error es -> Some(sprintf "only the collecting form refused: %A" (shapes es))
    | Error e, Ok _ -> Some(sprintf "only the short-circuiting form refused: %A" (shape e))
    | Error _, Error [] -> Some "the collecting form refused with no defect"
    | Error e, Error(head :: _ as es) ->
        if head <> e then
            Some(sprintf "first defect %A, short-circuit %A" (shape head) (shape e))
        else
            match es |> List.tryFind (fun d -> not (DecodeError.resolvesIn doc d)) with
            | Some d -> Some(sprintf "a defect whose path does not resolve: %A" (shape d))
            | None -> None

/// The in-repository vocabularies, under the support each is emitted with.
let private vocabularies () : (string * Gen.GenSupport * Idl) list =
    let decodeIdl, _, _ = DecodeVectorsIdl.current ()

    [ "mini", Gen.GenSupport.Empty, MiniIdl.miniIdl
      "doc", Gen.GenSupport.Empty, SecondDomainSpike.docIdl
      "score", Gen.GenSupport.Empty, ScoreDomainSpike.scoreIdl
      "reference", ReferenceIdl.support.Support, ReferenceIdl.refIdl
      "decode-vectors", Gen.GenSupport.Empty, decodeIdl
      "refusal-corners", Gen.GenSupport.Empty, RefusalCornersIdl.idl
      "derive", Gen.GenSupport.Empty, DeriveIdl.deriveIdl ]

let private tags (idl: Idl) = idl.Kinds |> List.map _.Tag

let private emitted (r: Result<string, CodegenError>) : string =
    match r with
    | Ok s -> s
    | Error e -> failtestf "codegen refused: %A" e

[<Tests>]
let tests =
    testList
        "Phase 377 - collecting decoders and per-spec entries"
        [ testList
              "opt-in and reach"
              [ testCase "every vocabulary here emits a public pair per kind and the node entries" (fun _ ->
                    for name, sup, idl in vocabularies () do
                        let text =
                            emitted (
                                Gen.fsharpModuleDerived
                                    sup
                                    [ Gen.Derivation.SpecDecoders ]
                                    ("M." + name)
                                    idl
                                    (tags idl)
                            )

                        for k in idl.Kinds do
                            Expect.stringContains
                                text
                                (sprintf "let decode%sSpec (j: JVal)" k.Tag)
                                (name + ": " + k.Tag)

                            Expect.stringContains
                                text
                                (sprintf "let decode%sSpecAll (j: JVal)" k.Tag)
                                (name + ": " + k.Tag)

                        for entry in [ "let decodeNodeJson "; "let decodeNodeJsonAll "; "let decodeNodeAll " ] do
                            Expect.stringContains text entry (name + ": " + entry))

                testCase
                    "the decoders are appended: the emission without them is a prefix, on every vocabulary"
                    (fun _ ->
                        for name, sup, idl in vocabularies () do
                            let plain = emitted (Gen.fsharpModuleWith sup ("M." + name) idl (tags idl))

                            let withDecoders =
                                emitted (
                                    Gen.fsharpModuleDerived
                                        sup
                                        [ Gen.Derivation.SpecDecoders ]
                                        ("M." + name)
                                        idl
                                        (tags idl)
                                )

                            Expect.isTrue
                                (withDecoders.StartsWith(plain + "\n\n", System.StringComparison.Ordinal))
                                (name + ": the short-circuiting module must be unchanged ahead of the decoders"))

                testCase "the defect order is stated in the emitted module" (fun _ ->
                    let text = File.ReadAllText(Snapshots.repoFile DeriveIdl.generatedFile)

                    Expect.stringContains
                        text
                        "in FIELD DECLARATION ORDER"
                        "the order a consumer adopts is written down"

                    Expect.stringContains
                        text
                        "a list's items in index order, and a map's entries in document order"
                        "lists and maps"

                    Expect.stringContains
                        text
                        "The first defect of a collecting decoder is the defect its short-circuiting twin reports"
                        "the agreement the law below holds")

                testCase "the committed SpecDecodersGenerated.fs is what the generator emits (byte for byte)" (fun _ ->
                    let generated = emitted (SpecDecodersIdl.generate ())
                    let path = Snapshots.repoFile SpecDecodersIdl.generatedFile

                    if System.Environment.GetEnvironmentVariable "FUARAN_REGEN" = "1" then
                        File.WriteAllText(path, generated)

                    Expect.equal
                        generated
                        (File.ReadAllText path)
                        "regenerate with: dotnet run --project tests/Fuaran.Core.Tests -- --regen-snapshots") ]

          testList
              "one multi-defect input per field shape"
              [ testCase "missing: every absent required member, in declaration order" (fun _ ->
                    let j = parse """{"$type":"Task"}"""

                    expectDefects
                        [ missing "$[\"name\"]"
                          missing "$[\"due\"]"
                          missing "$[\"trigger\"]"
                          missing "$[\"extras\"]"
                          missing "$[\"owner\"]" ]
                        (decodeTaskSpecAll j)
                        (decodeTaskSpec j))

                testCase "wrong type: every member of the wrong JSON kind" (fun _ ->
                    let j =
                        parse """{"$type":"Task","name":1,"due":"x","trigger":true,"extras":[],"owner":5}"""

                    expectDefects
                        [ wrongKind "$[\"name\"]"
                          wrongKind "$[\"due\"]"
                          wrongKind "$[\"trigger\"]"
                          wrongKind "$[\"extras\"]"
                          wrongKind "$[\"owner\"]" ]
                        (decodeTaskSpecAll j)
                        (decodeTaskSpec j))

                testCase "nested: a member's own defects at its position, depth-first" (fun _ ->
                    let j =
                        parse
                            """{"$type":"Section","children":[],"owner":{"name":1,"priority":"Urgent"},"estimate":{"$type":"Hours","amount":"x"}}"""

                    expectDefects
                        [ missing "$[\"title\"]"
                          wrongKind "$[\"owner\"][\"name\"]"
                          unknownTag "$[\"owner\"][\"priority\"]"
                          wrongKind "$[\"estimate\"][\"amount\"]" ]
                        (decodeSectionSpecAll j)
                        (decodeSectionSpec j))

                testCase "list element: every item's defects, in index order" (fun _ ->
                    let j =
                        parse
                            """{"$type":"Choice","branches":[{"label":1,"body":{"id":"b","kind":{"$type":"Note","text":"t"}}},{"body":5},"oops"],"otherwise":{"id":"o","kind":{"$type":"Note","text":"t"}},"rule":{"$type":"AllOf","rules":[{"$type":"Always"},{"$type":"Nope"},{"$type":"Equals"}]}}"""

                    expectDefects
                        [ wrongKind "$[\"branches\"][0][\"label\"]"
                          missing "$[\"branches\"][1][\"label\"]"
                          wrongKind "$[\"branches\"][1][\"body\"]"
                          wrongKind "$[\"branches\"][2]"
                          unknownTag "$[\"rule\"][\"rules\"][1][\"$type\"]"
                          missing "$[\"rule\"][\"rules\"][2][\"key\"]"
                          missing "$[\"rule\"][\"rules\"][2][\"value\"]" ]
                        (decodeChoiceSpecAll j)
                        (decodeChoiceSpec j))

                testCase "map key: every entry's defects, in document order" (fun _ ->
                    let j =
                        parse
                            """{"$type":"Task","name":"n","due":{"$type":"Named","rules":{"a":{"$type":"Always"},"b":7,"c":{"$type":"Not"}}},"trigger":{"$type":"Manual","label":"go"},"extras":{"x":{"id":"x","kind":{"$type":"Note","text":"t"}},"y":1,"z":{"kind":{"$type":"Note","text":2}}},"owner":{}}"""

                    expectDefects
                        [ wrongKind "$[\"due\"][\"rules\"][\"b\"]"
                          missing "$[\"due\"][\"rules\"][\"c\"][\"rule\"]"
                          wrongKind "$[\"extras\"][\"y\"]"
                          missing "$[\"extras\"][\"z\"][\"id\"]"
                          wrongKind "$[\"extras\"][\"z\"][\"kind\"][\"text\"]" ]
                        (decodeTaskSpecAll j)
                        (decodeTaskSpec j))

                testCase "the node: id, kind, then the envelope; text in, a parser refusal is the one defect" (fun _ ->
                    let text = """{"kind":{"$type":"Note","text":1},"annotation":5}"""

                    let all =
                        match decodeNodeAll text with
                        | Error es -> es
                        | Ok _ -> failtest "expected a refusal"

                    Expect.equal
                        (shapes all)
                        [ missing "$[\"id\"]"
                          wrongKind "$[\"kind\"][\"text\"]"
                          wrongKind "$[\"annotation\"]" ]
                        "every defect of the node"

                    Expect.equal (firstOf (decodeNode text)) all.Head "decodeNode answers the first"

                    match decodeNodeAll "{\"id\":" with
                    | Error [ e ] -> Expect.equal e.Code DecodeCode.InvalidJson "a parser refusal is one InvalidJson"
                    | other -> failtestf "expected one parser refusal, got %A" other) ]

          testList
              "the conservative-extension law"
              [ testCase "the sample decodes, identically, in both forms" (fun _ ->
                    let doc = parse sampleText

                    match decodeNodeJsonAll doc with
                    | Ok _ -> ()
                    | Error es -> failtestf "the sample must be valid: %A" (shapes es)

                    Expect.isNone (agrees doc) "both forms agree on the valid sample")

                testCase "every single mutation: retyped, re-kinded or removed, at every position" (fun _ ->
                    let doc = parse sampleText
                    let paths = positions doc
                    Expect.isGreaterThan paths.Length 80 "the sample reaches enough positions to mean something"

                    let mutations: (string * (JVal -> JVal option)) list =
                        [ "int", (fun _ -> Some(JInt 7))
                          "string", (fun _ -> Some(JStr "x"))
                          "array", (fun _ -> Some(JArr []))
                          "object", (fun _ -> Some(JObj []))
                          "removed", (fun _ -> None) ]

                    let failures =
                        [ for p in paths do
                              for name, m in mutations do
                                  let mutated = rewrite p m doc

                                  match agrees mutated with
                                  | Some why -> yield sprintf "%s at %s: %s" name (DecodePath.render p) why
                                  | None -> () ]

                    Expect.isEmpty failures "the collecting form extends the short-circuiting one")

                testCase "every pair of independent mutations: both defects, in order, the first agreeing" (fun _ ->
                    let doc = parse sampleText
                    let paths = positions doc |> List.toArray
                    let mutable multi = 0

                    let failures =
                        [ for a in 0 .. paths.Length - 1 do
                              for b in a + 1 .. paths.Length - 1 do
                                  let pa, pb = paths[a], paths[b]

                                  if not (isPrefix pa pb) && not (isPrefix pb pa) then
                                      let mutated =
                                          doc
                                          |> rewrite pa (fun _ -> Some(JInt 7))
                                          |> rewrite pb (fun _ -> Some(JInt 7))

                                      match decodeNodeJsonAll mutated with
                                      | Error es when es.Length > 1 -> multi <- multi + 1
                                      | _ -> ()

                                      match agrees mutated with
                                      | Some why ->
                                          yield sprintf "%s + %s: %s" (DecodePath.render pa) (DecodePath.render pb) why
                                      | None -> () ]

                    Expect.isEmpty failures "the law holds over every pair"
                    Expect.isGreaterThan multi 1000 "the pairs exercise multi-defect refusals, not only single ones") ]

          testList
              "the support channel (reference vocabulary)"
              [ testCase "every reference fixture decodes identically in both forms" (fun _ ->
                    for name, _, wire in ReferenceIdl.nodeCases do
                        match Ref.decodeNode wire, Ref.decodeNodeAll wire with
                        | Ok a, Ok b -> Expect.equal (Ref.encodeNodeJson b) (Ref.encodeNodeJson a) name
                        | a, b -> failtestf "%s: short-circuit %A, collecting %A" name a b)

                testCase "a host projection reports its one defect" (fun _ ->
                    let j = parse """{"$type":"Note","body":5}"""
                    let all = allOf (Ref.decodeNoteSpecAll j)
                    Expect.equal (shapes all) [ wrongKind "$[\"body\"]" ] "the projection's decoder, lifted"
                    Expect.equal (firstOf (Ref.decodeNoteSpec j)) all.Head "and the short-circuit agrees")

                testCase "a refined case collects its members before its refine is consulted" (fun _ ->
                    let link =
                        ReferenceIdl.nodeCases
                        |> List.find (fun (n, _, _) -> n = "link-1")
                        |> fun (_, _, w) -> parse w

                    let withLabel (label: JVal) =
                        rewrite [ PathSegment.Key "kind"; PathSegment.Key "label" ] (fun _ -> Some label) link

                    let bad = withLabel (parse """{"$type":"Lookup","args":{"a":1,"b":"ok","c":2}}""")

                    let all =
                        match Ref.decodeNodeJsonAll bad with
                        | Error es -> es
                        | Ok _ -> failtest "expected a refusal"

                    Expect.equal
                        (shapes all)
                        [ wrongKind "$[\"kind\"][\"label\"][\"args\"][\"a\"]"
                          wrongKind "$[\"kind\"][\"label\"][\"args\"][\"c\"]"
                          missing "$[\"kind\"][\"label\"][\"key\"]" ]
                        "every member of the refined case"

                    Expect.equal (firstOf (Ref.decodeNodeJson bad)) all.Head "the short-circuit answers the first"

                    let good = withLabel (parse """{"$type":"Lookup","args":{"a":"x"},"key":"k"}""")

                    match Ref.decodeNodeJson good, Ref.decodeNodeJsonAll good with
                    | Ok a, Ok b ->
                        Expect.equal (Ref.encodeNodeJson b) (Ref.encodeNodeJson a) "the refine admits it in both"
                    | a, b -> failtestf "short-circuit %A, collecting %A" a b)

                testCase
                    "a transparent case decodes bare, and a bare value of the wrong kind is its one defect"
                    (fun _ ->
                        let link =
                            ReferenceIdl.nodeCases
                            |> List.find (fun (n, _, _) -> n = "link-1")
                            |> fun (_, _, w) -> parse w

                        let withLabel (label: JVal) =
                            rewrite [ PathSegment.Key "kind"; PathSegment.Key "label" ] (fun _ -> Some label) link

                        match Ref.decodeNodeJsonAll (withLabel (JStr "bare")) with
                        | Ok n ->
                            match n.Kind with
                            | Ref.NodeKind.Link s ->
                                Expect.equal s.Label (Ref.Text.Inline "bare") "the bare string is the transparent case"
                            | other -> failtestf "expected a Link, got %A" other
                        | Error es -> failtestf "a bare label must decode: %A" (shapes es)

                        let bad = withLabel (JInt 5)

                        Expect.equal
                            (shapes (allOf (Ref.decodeNodeJsonAll bad)))
                            [ wrongKind "$[\"kind\"][\"label\"]" ]
                            "one defect for the bare value"

                        Expect.isNone
                            (match Ref.decodeNodeJson bad, Ref.decodeNodeJsonAll bad with
                             | Error e, Error(h :: _) when e = h -> None
                             | a, b -> Some(sprintf "%A / %A" a b))
                            "and the short-circuit agrees") ] ]
