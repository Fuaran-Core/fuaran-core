module Fuaran.Core.Tests.ExportersTests

// The exporter table's family (Phase 508): every committed file is what its row renders. The table
// itself is in the fixtures library (`Exporters`), which the tool renders through; this file holds it.

open System
open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Exporters

/// The table as the tool renders it: the `families` row measured by this suite's reference run.
let table = tableWith ConformanceVacuityTests.cases

[<Tests>]
let tests =
    testList
        "Exporters"
        [ for e in table do
              yield
                  testCase (sprintf "the committed `%s` files are what the row renders" e.Family)
                  <| fun _ ->
                      match findings e with
                      | [] -> ()
                      | found -> failtest (String.concat "\n" found)

          yield
              testCase "go-red: a perturbed committed file is named, with its first differing line"
              <| fun _ ->
                  let rendered = "{\n  \"a\": 1,\n  \"b\": [\n  ]\n}\n"

                  Expect.isNone (CorpusFile.firstDifference rendered rendered) "a file equal to its render is fresh"

                  Expect.isNone
                      (CorpusFile.firstDifference (rendered.Replace("\n", "\r\n")) rendered)
                      "CRLF line endings are the one difference the reading forgives"

                  Expect.equal
                      (CorpusFile.firstDifference (rendered.Replace("1", "2")) rendered)
                      (Some
                          { Line = 2
                            Committed = Some "  \"a\": 2,"
                            Rendered = Some "  \"a\": 1," })
                      "a moved byte is named by its line"

                  Expect.equal
                      (CorpusFile.firstDifference (rendered.TrimEnd('\n')) rendered)
                      (Some
                          { Line = 6
                            Committed = None
                            Rendered = Some "" })
                      "a lost trailing newline is a difference, not forgiven"

          yield
              testCase
                  "the one writer lays a document out a member and an item per line, escaping through Canonical.write Rendering.CrossHost"
              <| fun _ ->
                  let doc =
                      { Head = [ "family", CorpusFile.str "x\n\"y\""; "count", CorpusFile.num 2 ]
                        Items = "vectors"
                        Rows =
                          [ CorpusCell.Row [ "id", CorpusFile.str "a"; "input", CorpusCell.Row [] ]
                            CorpusCell.Value(JObj [ "b", JInt 1; "a", JInt 2 ])
                            CorpusCell.List [ CorpusFile.str "p"; CorpusFile.num 3 ] ] }

                  Expect.equal
                      (CorpusFile.render doc)
                      ("{\n  \"family\": \"x\\u000a\\\"y\\\"\",\n  \"count\": 2,\n  \"vectors\": [\n"
                       + "    { \"id\": \"a\", \"input\": {} },\n    {\"a\":2,\"b\":1},\n    [\"p\", 3]\n  ]\n}\n")
                      "author order and spacing in a Row, Canonical.write Rendering.CrossHost's sorted compact form in a Value"

                  Expect.equal
                      (CorpusFile.render { doc with Rows = [] })
                      "{\n  \"family\": \"x\\u000a\\\"y\\\"\",\n  \"count\": 2,\n  \"vectors\": [\n  ]\n}\n"
                      "an empty array closes on the next line"

          yield
              testCase "every family is one row, named once"
              <| fun _ ->
                  let names = table |> List.map _.Family
                  Expect.equal (List.distinct names) names "no family has two rows"

          // Phase 560 — outside this repository a family writes only what the registry declares there.
          yield
              testCase
                  "render <family> <dir> writes only the declared copies and shared indexes; Core's own home gets every file"
              <| fun _ ->
                  let rows = Approval.registry.Value
                  let laws = table |> List.find (fun e -> e.Family = "laws")

                  let scratch =
                      Path.Combine(Path.GetTempPath(), "fuaran-core-560-render-" + Guid.NewGuid().ToString("N"))

                  let names (paths: string list) =
                      paths |> List.map Path.GetFileName |> List.sort

                  try
                      let corpus = Path.Combine(scratch, "corpus")
                      let home = Path.Combine(scratch, "home")

                      Expect.equal (targetOf laws (Some(laws.Root()))) (Own(laws.Root())) "naming the home is the home"

                      let written = writeTo rows None laws (Elsewhere corpus)

                      let declaredNames =
                          declared rows laws |> Set.toList |> List.map Path.GetFileName |> List.sort

                      Expect.equal (names written) declaredNames "the declared copies, and nothing else, are written"

                      Expect.equal
                          (Directory.GetFiles(Path.Combine(corpus, "laws")) |> Array.toList |> names)
                          declaredNames
                          "and nothing else lands on disk (no canonical-form-laws.json)"

                      let all = writeTo rows None laws (Own home)

                      Expect.equal
                          (names all)
                          (laws.Files home |> List.map fst |> names)
                          "the home gets every file the row emits"

                      Expect.contains
                          (names all)
                          CanonicalFormVectorExport.fileName
                          "including the file no corpus declares"

                      // go-red: a copy the registry stops declaring stops landing.
                      let without =
                          rows
                          |> List.map (fun r ->
                              if r.Path.EndsWith "field-laws.json" then
                                  { r with Copies = [] }
                              else
                                  r)

                      let narrowed =
                          writeTo without None laws (Elsewhere(Path.Combine(scratch, "narrow")))

                      Expect.isFalse
                          (names narrowed |> List.contains "field-laws.json")
                          "an undeclared copy is not written"

                      Expect.equal narrowed.Length (written.Length - 1) "and only it is dropped"

                      // go-red: the apply writer writes its two files together or refuses.
                      let apply = table |> List.find (fun e -> e.Family = "apply")

                      let noVectors =
                          rows
                          |> List.map (fun r ->
                              if r.Path.EndsWith "skeleton-apply.json" then
                                  { r with Copies = [] }
                              else
                                  r)

                      Expect.throwsC
                          (fun () ->
                              writeTo noVectors None apply (Elsewhere(Path.Combine(scratch, "apply")))
                              |> ignore)
                          (fun ex -> Expect.stringContains ex.Message "writes both or neither" "named")

                      Expect.isFalse (Directory.Exists(Path.Combine(scratch, "apply"))) "and nothing was written"
                  finally
                      if Directory.Exists scratch then
                          Directory.Delete(scratch, true) ]
