module Fuaran.Core.Tests.ExportersTests

// The exporter table's family (Phase 508): every committed file is what its row renders. The table
// itself is in the fixtures library (`Exporters`), which the tool renders through; this file holds it.

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
                            Committed = "  \"a\": 2,"
                            Rendered = "  \"a\": 1," })
                      "a moved byte is named by its line"

                  Expect.equal
                      (CorpusFile.firstDifference (rendered.TrimEnd('\n')) rendered)
                      (Some
                          { Line = 6
                            Committed = "<absent>"
                            Rendered = "" })
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
                  Expect.equal (List.distinct names) names "no family has two rows" ]
