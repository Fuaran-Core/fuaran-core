module Fuaran.Core.Tests.ValidatorTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

// Reference rule families (rule CONTENT is domain-side; the framework is core).
let private noEmptyPara =
    Validator.perNode "REF001" (fun w n ->
        if w.KindTag n = "para" && n.Value = "" then
            [ { Code = "REF001"
                Severity = Severity.Warning
                Message = "empty paragraph"
                Node = Some n.Id } ]
        else
            [])

let private sectionsNonEmpty =
    Validator.perNode "REF002" (fun w n ->
        if w.KindTag n = "section" && List.isEmpty (w.Children n) then
            [ { Code = "REF002"
                Severity = Severity.Error
                Message = "empty section"
                Node = Some n.Id } ]
        else
            [])

let private registry =
    Validator.empty
    |> Validator.register noEmptyPara
    |> Validator.register sectionsNonEmpty

[<Tests>]
let tests =
    testList
        "Validator"
        [ testCase "a clean tree produces no defects"
          <| fun _ -> Expect.isEmpty (Validator.runAll nodew registry (sample ())) "clean"

          testCase "perNode rule flags the offending node"
          <| fun _ ->
              let tree =
                  RNode.node "root" "doc" [ RNode.node "a" "section" [ RNode.leaf "a1" "para" "" ] ]

              let defects = Validator.runAll nodew registry tree
              Expect.equal (defects |> List.map (fun d -> d.Code)) [ "REF001" ] "one REF001"
              Expect.equal defects.[0].Node (Some "a1") "located at a1"

          testCase "families aggregate in registration order"
          <| fun _ ->
              let tree = RNode.node "root" "doc" [ RNode.node "empty" "section" [] ]
              let defects = Validator.runAll nodew registry tree
              Expect.equal (defects |> List.map (fun d -> d.Code)) [ "REF002" ] "empty section flagged"
              Expect.isTrue (Validator.hasErrors defects) "REF002 is an Error"

          testCase "canonicalCodes is sorted and order-independent (byte-parity surface)"
          <| fun _ ->
              let a: Defect<string> list =
                  [ { Code = "B"
                      Severity = Severity.Info
                      Message = ""
                      Node = None }
                    { Code = "A"
                      Severity = Severity.Info
                      Message = ""
                      Node = None } ]

              let b: Defect<string> list =
                  [ { Code = "A"
                      Severity = Severity.Info
                      Message = ""
                      Node = None }
                    { Code = "B"
                      Severity = Severity.Info
                      Message = ""
                      Node = None } ]

              Expect.equal (Validator.canonicalCodes a) "AB" "sorted, U+0001-joined"
              Expect.equal (Validator.canonicalCodes a) (Validator.canonicalCodes b) "order-independent"

          // Phase 25 — delimiter-safe canonicalCodes: a code containing the old ',' no longer aliases.
          testCase "canonicalCodes cannot be aliased by a comma in a code"
          <| fun _ ->
              let mk code : Defect<string> =
                  { Code = code
                    Severity = Severity.Info
                    Message = ""
                    Node = None }

              let aliasing = [ mk "A,B" ]
              let split = [ mk "A"; mk "B" ]

              Expect.notEqual
                  (Validator.canonicalCodes aliasing)
                  (Validator.canonicalCodes split)
                  "a comma-bearing single code is distinct from two codes"

          // Phase 25 — severity summary.
          testCase "summary counts defects by severity"
          <| fun _ ->
              let mk sev : Defect<string> =
                  { Code = "X"
                    Severity = sev
                    Message = ""
                    Node = None }

              let defects =
                  [ mk Severity.Error; mk Severity.Error; mk Severity.Warning; mk Severity.Info ]

              let s = Validator.summary defects
              Expect.equal s.Errors 2 "two errors"
              Expect.equal s.Warnings 1 "one warning"
              Expect.equal s.Infos 1 "one info"

          // ---- Phase 37: columnar validator surface ----

          testCase "ColumnValidator stock rules locate the faults they target"
          <| fun _ ->
              let t: Table =
                  { Schema = [ "id", IntType; "score", IntType; "name", StringType ]
                    Columns =
                      [ Column.create "id" IntType [ Int 1; Int 2; Int 2 ] // duplicate id at row 2
                        Column.create "score" IntType [ Int 50; Null; Int 200 ] // null + out-of-range
                        Column.create "name" StringType [ Str "a"; Str "b"; Str "c" ] ] }

              let reg =
                  ColumnValidator.empty
                  |> ColumnValidator.register (ColumnValidator.notNull "score")
                  |> ColumnValidator.register (ColumnValidator.inRange "score" 0.0 100.0)
                  |> ColumnValidator.register (ColumnValidator.ofType "name" StringType)
                  |> ColumnValidator.register (ColumnValidator.unique [ "id" ])

              let defects = ColumnValidator.validate reg t
              let codes = defects |> List.map (fun d -> d.Code)
              Expect.contains codes "COL-NOTNULL" "the null score is caught"
              Expect.contains codes "COL-INRANGE" "the 200 score is out of range"
              Expect.contains codes "COL-UNIQUE" "the duplicate id is caught"
              Expect.isFalse (List.contains "COL-OFTYPE" codes) "name is well-typed — no OFTYPE defect"

              // located: the out-of-range defect points at score#2
              let inRange = defects |> List.find (fun d -> d.Code = "COL-INRANGE")
              Expect.equal inRange.Node (Some "score#2") "located at score#2"

          testCase "ColumnValidator reuses the shared severity summary + canonical-codes parity"
          <| fun _ ->
              let t: Table =
                  { Schema = [ "a", IntType ]
                    Columns = [ Column.create "a" IntType [ Null; Int 5 ] ] }

              let reg =
                  ColumnValidator.empty |> ColumnValidator.register (ColumnValidator.notNull "a")

              let defects = ColumnValidator.validate reg t
              Expect.equal (Validator.summary defects).Errors 1 "one error via the shared summary"
              Expect.equal (Validator.canonicalCodes defects) "COL-NOTNULL" "canonical projection"

          testCase "columnarValidatorLaws certify determinism + soundness (Phase 37)"
          <| fun _ ->
              let results = Conformance.columnarValidatorLaws 4242 200
              Expect.equal (List.length results) 2 "determinism + soundness reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "columnarValidatorLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.columnarValidatorLaws 4242 200) results "same seed ⇒ identical report" ]
