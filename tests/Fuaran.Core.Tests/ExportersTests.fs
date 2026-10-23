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

          // Phase 477 — every self-enumerated manifest this repository commits reads as the record schema.
          yield
              testCase "every committed family manifest carries a record per host, and a measured one counts its cases"
              <| fun _ ->
                  let manifests =
                      [ for e in table do
                            for path, _ in e.Files(e.Root()) do
                                if Path.GetFileName path = "manifest.json" && File.Exists path then
                                    yield path ]
                      |> List.distinct

                  Expect.isGreaterThanOrEqual manifests.Length 7 "apply, refusals, decode, chain, dag, merge and diff"

                  for path in manifests do
                      match CorpusFile.readAdoption (File.ReadAllText path) with
                      | Error e -> failtestf "%s: %s" path e
                      | Ok families ->
                          Expect.isNonEmpty families (sprintf "%s indexes a family" path)

                          for id, hosts in families do
                              Expect.isNonEmpty hosts (sprintf "%s: `%s` records adoption for its roster" path id)

          yield
              testCase "go-red: the adoption reader refuses every entry that claims what was not measured"
              <| fun _ ->
                  let entry (adoption: string) =
                      "{\n  \"version\": 1,\n  \"families\": [\n    { \"id\": \"f\", \"vectors\": 3, \"adoption\": { \"h\": "
                      + adoption
                      + " } }\n  ]\n}\n"

                  let refused (adoption: string) (says: string) =
                      match CorpusFile.readAdoption (entry adoption) with
                      | Ok read -> failtestf "%s was read as %A" adoption read
                      | Error e ->
                          Expect.stringContains e "family `f`: adoption.h" "names the family and the host"
                          Expect.stringContains e says (sprintf "%s is refused, saying why" adoption)

                  let reads (adoption: string) (expected: HostAdoption) =
                      Expect.equal
                          (CorpusFile.readAdoption (entry adoption))
                          (Ok [ "f", [ "h", expected ] ])
                          (sprintf "%s reads" adoption)

                  refused "\"adopted\"" "bare string"
                  refused "\"adopted\"" "render <family>"
                  refused "{ \"status\": \"adopted\", \"cases\": 0 }" "must read `vacuous`"
                  refused "{ \"status\": \"adopted\" }" "carries no `cases`"
                  refused "{ \"status\": \"vacuous\", \"cases\": 2 }" "zero-case reading"
                  refused "{ \"status\": \"adopted\", \"cases\": 4 }" "holds 3 vectors"
                  refused "{ \"status\": \"proposed\", \"cases\": 3 }" "nothing is claimed"
                  refused "{ \"status\": \"adopted\", \"cases\": 3, \"sha\": \"abcdef1\" }" "without the other"

                  refused
                      "{ \"status\": \"adopted\", \"cases\": 3, \"kitVersion\": \"0.41.0\", \"sha\": \"XYZ\" }"
                      "hex"

                  refused "{ \"status\": \"green\" }" "one of proposed, adopted, vacuous, declined"
                  refused "{ \"status\": \"proposed\", \"note\": \"x\" }" "carries the member \"note\""

                  reads "{ \"status\": \"proposed\" }" HostAdoption.Proposed
                  reads "{ \"status\": \"declined\" }" HostAdoption.Declined
                  reads "{ \"status\": \"vacuous\", \"cases\": 0 }" (HostAdoption.Measured(0, AdoptionSource.Here))

                  reads
                      "{ \"status\": \"adopted\", \"cases\": 3, \"kitVersion\": \"0.41.0\", \"sha\": \"abcdef1\" }"
                      (HostAdoption.Measured(3, AdoptionSource.Host("0.41.0", "abcdef1")))

          yield
              testCase
                  "the writer derives the status, so a zero-case leg is written `vacuous`, and reads back as written"
              <| fun _ ->
                  let written =
                      [ HostAdoption.Proposed
                        HostAdoption.Declined
                        HostAdoption.Measured(0, AdoptionSource.Here)
                        HostAdoption.Measured(2, AdoptionSource.Here)
                        HostAdoption.Measured(
                            3,
                            AdoptionSource.Host("0.41.0", "0123456789abcdef0123456789abcdef01234567")
                        ) ]

                  Expect.equal
                      (written |> List.map HostAdoption.status)
                      [ "proposed"; "declined"; "vacuous"; "adopted"; "adopted" ]
                      "the status is a function of the measurement"

                  let text =
                      CorpusFile.render (
                          CorpusFile.manifest
                              { Version = 1
                                Description = "d"
                                Families =
                                  [ { Id = "f"
                                      Kind = "k"
                                      File = "f.json"
                                      Vectors = 3
                                      Adoption = written |> List.mapi (fun i a -> sprintf "h%d" i, a)
                                      Description = "d" } ] }
                      )

                  Expect.stringContains
                      text
                      "\"h2\": { \"status\": \"vacuous\", \"cases\": 0 }"
                      "a zero-case measurement is written vacuous"

                  Expect.equal
                      (CorpusFile.readAdoption text)
                      (Ok [ "f", written |> List.mapi (fun i a -> sprintf "h%d" i, a) ])
                      "every record the writer emits, the reader reads back unchanged"

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
