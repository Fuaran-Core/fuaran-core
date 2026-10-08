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
                Node = Some n.Id
                Family = ""
                Related = [] } ]
        else
            [])

let private sectionsNonEmpty =
    Validator.perNode "REF002" (fun w n ->
        if w.KindTag n = "section" && List.isEmpty (w.Children n) then
            [ { Code = "REF002"
                Severity = Severity.Error
                Message = "empty section"
                Node = Some n.Id
                Family = ""
                Related = [] } ]
        else
            [])

let private registry =
    Validator.ofFamilies [ noEmptyPara; sectionsNonEmpty ]
    |> Result.defaultWith (fun e -> failwithf "registry: %A" e)


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
              Expect.equal (defects |> List.map _.Code) [ "REF001" ] "one REF001"
              Expect.equal defects[0].Node (Some "a1") "located at a1"

          testCase "families aggregate in registration order"
          <| fun _ ->
              let tree = RNode.node "root" "doc" [ RNode.node "empty" "section" [] ]
              let defects = Validator.runAll nodew registry tree
              Expect.equal (defects |> List.map _.Code) [ "REF002" ] "empty section flagged"
              Expect.isTrue (Validator.hasErrors defects) "REF002 is an Error"

          testCase "canonicalCodes is sorted and order-independent (byte-parity surface)"
          <| fun _ ->
              let a: Defect<string> list =
                  [ { Code = "B"
                      Severity = Severity.Info
                      Message = ""
                      Node = None
                      Family = ""
                      Related = [] }
                    { Code = "A"
                      Severity = Severity.Info
                      Message = ""
                      Node = None
                      Family = ""
                      Related = [] } ]

              let b: Defect<string> list =
                  [ { Code = "A"
                      Severity = Severity.Info
                      Message = ""
                      Node = None
                      Family = ""
                      Related = [] }
                    { Code = "B"
                      Severity = Severity.Info
                      Message = ""
                      Node = None
                      Family = ""
                      Related = [] } ]

              Expect.equal
                  (Validator.canonicalCodes a)
                  "A\u0001B\u0001"
                  "sorted, each code escaped and U+0001-terminated (Phase 290)"

              Expect.equal (Validator.canonicalCodes a) (Validator.canonicalCodes b) "order-independent"

          // Phase 25 — delimiter-safe canonicalCodes: a code containing the old ',' no longer aliases.
          testCase "canonicalCodes cannot be aliased by a comma in a code"
          <| fun _ ->
              let mk code : Defect<string> =
                  { Code = code
                    Severity = Severity.Info
                    Message = ""
                    Node = None
                    Family = ""
                    Related = [] }

              let aliasing = [ mk "A,B" ]
              let split = [ mk "A"; mk "B" ]

              Expect.notEqual
                  (Validator.canonicalCodes aliasing)
                  (Validator.canonicalCodes split)
                  "a comma-bearing single code is distinct from two codes"

          // Phase 290 — the U+0001 join itself was a bare join: a code spelling the separator aliased
          // two codes. Through `Hash.canonicalFields` the projection is injective over sorted lists.
          testCase "canonicalCodes cannot be aliased by the separator in a code (Phase 290)"
          <| fun _ ->
              let mk code : Defect<string> =
                  { Code = code
                    Severity = Severity.Info
                    Message = ""
                    Node = None
                    Family = ""
                    Related = [] }

              Expect.notEqual
                  (Validator.canonicalCodes [ mk ("A" + Hash.foldSep + "B") ])
                  (Validator.canonicalCodes [ mk "A"; mk "B" ])
                  "a separator-bearing single code is distinct from two codes"

              Expect.notEqual
                  (Validator.canonicalCodes [ mk ("A" + Hash.fieldEsc) ])
                  (Validator.canonicalCodes [ mk "A" ])
                  "the escape character is a code character like any other"

          // Phase 25 — severity summary.
          testCase "summary counts defects by severity"
          <| fun _ ->
              let mk sev : Defect<string> =
                  { Code = "X"
                    Severity = sev
                    Message = ""
                    Node = None
                    Family = ""
                    Related = [] }

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
                  ColumnValidator.ofRules
                      [ ColumnValidator.notNull "score"
                        ColumnValidator.inRange "score" 0.0 100.0
                        ColumnValidator.ofType "name" StringType
                        ColumnValidator.unique [ "id" ] ]
                  |> Result.defaultWith (fun e -> failwithf "registry: %A" e)

              let defects = ColumnValidator.validate reg t
              let codes = defects |> List.map _.Code
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
                  ColumnValidator.ofRules [ ColumnValidator.notNull "a" ]
                  |> Result.defaultWith (fun e -> failwithf "registry: %A" e)

              let defects = ColumnValidator.validate reg t
              Expect.equal (Validator.summary defects).Errors 1 "one error via the shared summary"

              Expect.equal
                  (Validator.canonicalCodes defects)
                  (Hash.canonicalFields [ "COL-NOTNULL" ])
                  "canonical projection"

          testCase "columnarValidatorLaws certify determinism + soundness (Phase 37)"
          <| fun _ ->
              let results = Conformance.columnarValidatorLaws 4242 200

              Expect.equal
                  (List.length results)
                  5
                  "determinism + soundness, the two Phase 223 guards, and the Phase 276 column-type guard"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "columnarValidatorLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.columnarValidatorLaws 4242 200) results "same seed ⇒ identical report"

          testCase
              "Phase 223 — a fault-free sample turns columnarValidatorLaws RED, on both fault guards and (Phase 276) the column-type guard alone"
          <| fun _ ->
              // The kit draws this family's sample itself, so the refusal-free generator is the
              // roll's own clean stratum: iteration 0 is a table with every cell in range and none
              // null, by construction, at any seed. One iteration is therefore a run whose soundness
              // law holds as 0 = 0 for both rules — green on every subject law, and exactly what the
              // two guards exist to refuse. It is also an INT table (Phase 276: the decimal tables are
              // the odd iterations), so the column-type guard refuses it too, naming the decimal cell.
              for seed in [ 1; 4242; 90210 ] do
                  let results = Conformance.columnarValidatorLaws seed 1

                  Expect.equal
                      (results |> List.filter (fun r -> not r.Passed) |> List.map _.Law)
                      [ SampleAdequacy.lawPrefix "Conformance.columnarValidatorLaws"
                        + "the sample reached every injected null the laws distinguish"
                        SampleAdequacy.lawPrefix "Conformance.columnarValidatorLaws"
                        + "the sample reached every injected out-of-range value the laws distinguish"
                        SampleAdequacy.lawPrefix "Conformance.columnarValidatorLaws"
                        + "the sample reached every column type the laws distinguish" ]
                      (sprintf "seed %d: both fault guards and the column-type guard red, nothing else" seed)

                  // The clean stratum can also be EMPTY (every drawn cell null, and nulls dropped), so
                  // the int cell may be missed beside it; the decimal one always is.
                  let cx = (List.last results).Counterexample |> Option.defaultValue ""
                  let missed = cx.Substring(max 0 (cx.IndexOf "never reached"))

                  Expect.stringContains
                      missed
                      "decimal cell"
                      (sprintf "seed %d: the column-type guard names the decimal cell it missed" seed)

          testCase "Phase 276 — twelve iterations reach both column types and every stratum of each, all green"
          <| fun _ ->
              // Every stratum at both types, including the decimal stratum's `100.01`, the fault a
              // reading that truncated or rounded to whole units would call in range.
              for seed in [ 1; 4242; 90210 ] do
                  for r in Conformance.columnarValidatorLaws seed 12 do
                      Expect.isTrue r.Passed (sprintf "seed %d: %s — %A" seed r.Law r.Counterexample) ]
