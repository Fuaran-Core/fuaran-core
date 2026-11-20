module Fuaran.Core.Tests.ValidatorTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

/// A column of type `ty` over `cells`, which fit it (Phase 417: the typed column is built through
/// `Column.ofCells`, which refuses a cell of another type).
let private cellColumn (name: string) (ty: ColumnType) (cells: Cell list) : Column =
    match Column.ofCells name ty cells with
    | Ok c -> c
    | Error e -> failtestf "column %s did not build: %A" name e

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
                  { Schema =
                      [ Field.create "id" IntType
                        Field.create "score" IntType
                        Field.create "name" StringType ]
                    Columns =
                      [ cellColumn "id" IntType [ Int 1; Int 2; Int 2 ] // duplicate id at row 2
                        cellColumn "score" IntType [ Int 50; Null; Int 200 ] // null + out-of-range
                        cellColumn "name" StringType [ Str "a"; Str "b"; Str "c" ] ] }

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

          // Phase 497 — `ofType` read each cell's type back from its text, and a millisecond
          // column renders a whole-second instant with no fraction, which reads back as seconds:
          // every whole-second row of a column of the declared type was a COL-OFTYPE error (the
          // fixture run before the fix recorded 2 defects here, at c#0 and c#1). The rule is about the column.
          testCase "ofType over a millisecond column of whole-second instants reports nothing"
          <| fun _ ->
              let c =
                  Column.ofTimestamps
                      "c"
                      TimeUnit.Milliseconds
                      (Vector.ofList [ 0.0; 60.0; 120.0; 180.0 ])
                      (Some(Vector.ofList [ 0; 0; 250; 0 ]))
                      (Validity.ofList [ true; true; true; false ])

              let t: Table =
                  { Schema = [ Field.create "c" (TimestampType TimeUnit.Milliseconds) ]
                    Columns = [ c ] }

              Expect.equal ((ColumnValidator.ofType "c" (TimestampType TimeUnit.Milliseconds)).Run t) [] "no defect"

          testCase "ofType reports nothing on a column of its declared type, whatever its cells"
          <| fun _ ->
              let t: Table =
                  { Schema = [ Field.create "s" StringType ]
                    Columns = [ cellColumn "s" StringType [ Str "a"; Null; Str "" ] ] }

              Expect.equal ((ColumnValidator.ofType "s" StringType).Run t) [] "no defect"

          testCase "ofType reports one defect for a column of another type, at the column"
          <| fun _ ->
              let t: Table =
                  { Schema = [ Field.create "n" IntType ]
                    Columns = [ cellColumn "n" IntType [ Int 1; Int 2; Null; Int 4 ] ] }

              match (ColumnValidator.ofType "n" StringType).Run t with
              | [ d ] ->
                  Expect.equal d.Code "COL-OFTYPE" "code"
                  Expect.equal d.Severity Severity.Error "severity"
                  Expect.equal d.Node (Some "n") "located at the column, not at a row"
                  Expect.equal d.Message "column 'n' expected string, got int" "message"
              | ds -> failtestf "expected one defect, got %A" ds

              let ms =
                  Column.ofTimestamps "m" TimeUnit.Milliseconds (Vector.ofList [ 0.0 ]) None AllValid

              let tm: Table =
                  { Schema = [ Field.create "m" (TimestampType TimeUnit.Milliseconds) ]
                    Columns = [ ms ] }

              Expect.equal
                  ((ColumnValidator.ofType "m" (TimestampType TimeUnit.Seconds)).Run tm
                   |> List.map _.Node)
                  [ Some "m" ]
                  "a timestamp column of another unit is one defect"

          testCase "ColumnValidator reuses the shared severity summary + canonical-codes parity"
          <| fun _ ->
              let t: Table =
                  { Schema = [ Field.create "a" IntType ]
                    Columns = [ cellColumn "a" IntType [ Null; Int 5 ] ] }

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
                  let columnType = List.last results

                  Expect.contains
                      columnType.Reached
                      ("decimal cell", 0)
                      (sprintf "seed %d: the column-type guard counts the decimal cell it missed" seed)

                  Expect.stringContains
                      (defaultArg columnType.Counterexample "")
                      "decimal cell"
                      (sprintf "seed %d: and names it" seed)

          testCase "Phase 276 — twelve iterations reach both column types and every stratum of each, all green"
          <| fun _ ->
              // Every stratum at both types, including the decimal stratum's `100.01`, the fault a
              // reading that truncated or rounded to whole units would call in range.
              for seed in [ 1; 4242; 90210 ] do
                  for r in Conformance.columnarValidatorLaws seed 12 do
                      Expect.isTrue r.Passed (sprintf "seed %d: %s — %A" seed r.Law r.Counterexample) ]

// ---- Phase 521: `Rules<'Subject,'Id>` — one framework, one walker, the column rules on vectors ----

let private orFail521 (r: Result<'a, RegistrationError>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> failtestf "unexpected refusal %A" e

/// A rule over a plain string subject: the framework is subject-generic, a table or a tree root is one
/// instance among others.
let private wordRule (id: string) (word: string) : Validator.PackCheck<string, int> =
    { RuleId = id
      Run =
        fun text ->
            text.Split ' '
            |> Array.toList
            |> List.indexed
            |> List.choose (fun (i, w) ->
                if w = word then
                    Some(Defect.create "WORD" Severity.Warning ("the word " + word) (Some i))
                else
                    None) }

/// The `inRange` the rule was before Phase 521, over `Column.toCells` — the oracle the typed-storage
/// walk must agree with, row for row and code for code.
let private inRangeOverCells (column: string) (lo: float) (hi: float) (c: Column) : (string * string option) list =
    Column.toCells c
    |> List.indexed
    |> List.choose (fun (i, cell) ->
        let v =
            match cell with
            | Int n -> Some(Some(float n))
            | Float f when System.Double.IsNaN f -> Some None
            | Float f -> Some(Some f)
            | Decimal s ->
                match DecimalText.tryToFloat s, DecimalText.compare s DecimalText.zero with
                | Some f, _ -> Some(Some f)
                | None, Some c when c < 0 -> Some(Some -infinity)
                | None, Some _ -> Some(Some infinity)
                | None, None -> Some None
            | _ -> None

        match v with
        | Some None -> Some(ColumnValidator.NotANumberCode, Some(column + "#" + string i))
        | Some(Some x) when x < lo || x > hi -> Some(ColumnValidator.InRangeCode, Some(column + "#" + string i))
        | _ -> None)

[<Tests>]
let rulesTests =
    testList
        "Validator.Rules (Phase 521)"
        [ testCase "a registry over any subject: the lifecycle, refused in RegistrationError"
          <| fun _ ->
              let rs =
                  Validator.Rules.ofChecks [ wordRule "a" "x"; wordRule "b" "y" ] |> orFail521

              Expect.equal (Validator.Rules.enumerate rs) [ "a"; "b" ] "registration order"
              Expect.equal (Validator.Rules.tryFind "b" rs |> Option.map _.RuleId) (Some "b") "found"
              Expect.isNone (Validator.Rules.tryFind "z" rs) "not held"

              match Validator.Rules.register (wordRule "a" "q") rs with
              | Error(RegistrationError.DuplicateRule("a", [ "a"; "b" ])) -> ()
              | other -> failtestf "a repeated id must be refused, got %A" other

              match Validator.Rules.ofChecks [ wordRule "a" "x"; wordRule "a" "y" ] with
              | Error(RegistrationError.DuplicateRule("a", [ "a" ])) -> ()
              | other -> failtestf "ofChecks refuses the first repeat, got %A" other

              Expect.equal
                  (Validator.Rules.register (wordRule "c" "z") rs
                   |> Result.bind (Validator.Rules.unregister "c")
                   |> Result.map Validator.Rules.enumerate)
                  (Ok [ "a"; "b" ])
                  "unregister undoes register"

              match Validator.Rules.unregister "z" rs, Validator.Rules.replace (wordRule "z" "q") rs with
              | Error(RegistrationError.UnknownRule("z", [ "a"; "b" ])), Error(RegistrationError.UnknownRule("z", _)) ->
                  ()
              | other -> failtestf "an unheld id is UnknownRule, got %A" other

              let swapped = Validator.Rules.replace (wordRule "a" "y") rs |> orFail521
              Expect.equal (Validator.Rules.enumerate swapped) [ "a"; "b" ] "position kept"

              Expect.equal
                  (Validator.Rules.run swapped "y x y" |> List.map (fun (id, d) -> id, d.Node))
                  [ "a", Some 0; "a", Some 2; "b", Some 0; "b", Some 2 ]
                  "the replacement runs under its id"

              Expect.equal
                  (Validator.Rules.restrict (Set.ofList [ "b"; "z" ]) rs
                   |> Validator.Rules.enumerate)
                  [ "b" ]
                  "restrict never widens"

              Expect.equal
                  (Validator.Rules.union (Validator.Rules.ofChecks [ wordRule "c" "z" ] |> orFail521) rs
                   |> Result.map Validator.Rules.enumerate)
                  (Ok [ "c"; "a"; "b" ])
                  "union keeps each side's order"

              match Validator.Rules.union rs rs with
              | Error(RegistrationError.DuplicateRule("a", [ "a"; "b" ])) -> ()
              | other -> failtestf "a shared id is refused, got %A" other

              Expect.isEmpty (Validator.Rules.run Validator.Rules.empty "x") "the empty registry finds nothing"

          testCase "the one walker: stamped findings, a throwing rule is one RULE-FAULT and the rest still run"
          <| fun _ ->
              let throwing: Validator.PackCheck<string, int> =
                  { RuleId = "boom"
                    Run = fun _ -> failwith "kaput" }

              let rs = Validator.Rules.ofChecks [ throwing; wordRule "a" "x" ] |> orFail521

              match Validator.Rules.run rs "x" with
              | [ ("boom", fault); ("a", found) ] ->
                  Expect.equal fault.Code Validator.FamilyFaultCode "the fault"
                  Expect.equal fault.Family "boom" "stamped with the throwing rule"
                  Expect.equal found.Family "a" "stamped, whatever the body wrote"
              | other -> failtestf "expected a fault then a finding, got %A" other

          testCase "a tree family in Rules through asCheck finds what runAllTagged finds"
          <| fun _ ->
              let tree =
                  RNode.node "root" "doc" [ RNode.node "a" "section" []; RNode.leaf "p" "para" "" ]

              let rs =
                  Validator.Rules.ofChecks
                      [ Validator.asCheck nodew noEmptyPara
                        Validator.asCheck nodew sectionsNonEmpty ]
                  |> orFail521

              Expect.equal (Validator.Rules.run rs tree) (Validator.runAllTagged nodew registry tree) "one walk"

          testCase "the column registry IS Rules at the table: validateTagged is Rules.run"
          <| fun _ ->
              let t: Table =
                  { Schema = [ Field.create "a" IntType ]
                    Columns = [ cellColumn "a" IntType [ Null; Int 1; Int 7 ] ] }

              let reg =
                  ColumnValidator.ofRules [ ColumnValidator.notNull "a"; ColumnValidator.inRange "a" 0.0 5.0 ]
                  |> orFail521

              Expect.equal (ColumnValidator.validateTagged reg t) (Validator.Rules.run reg t) "one walk"

              Expect.equal
                  (ColumnValidator.validate reg t |> List.map (fun d -> d.Code, d.Node))
                  [ ColumnValidator.NotNullCode, Some "a#0"
                    ColumnValidator.InRangeCode, Some "a#2" ]
                  "verdicts unchanged"

          testCase "every stock column code is the literal it always spelled"
          <| fun _ ->
              Expect.equal
                  [ ColumnValidator.NoColumnCode
                    ColumnValidator.NotNullCode
                    ColumnValidator.OfTypeCode
                    ColumnValidator.InRangeCode
                    ColumnValidator.UniqueCode
                    ColumnValidator.BadRangeCode
                    ColumnValidator.NotANumberCode
                    ColumnValidator.RaggedCode ]
                  [ "COL-NOCOL"
                    "COL-NOTNULL"
                    "COL-OFTYPE"
                    "COL-INRANGE"
                    "COL-UNIQUE"
                    "COL-BADRANGE"
                    "COL-NAN"
                    "COL-RAGGED" ]
                  "the codes hosts compare are unchanged"

          testCase "inRange on the typed storage agrees with the cell walk it replaced"
          <| fun _ ->
              let columns =
                  [ cellColumn "c" IntType [ Int -3; Null; Int 0; Int 10; Int 11 ]
                    cellColumn "c" FloatType [ Float nan; Float 0.5; Null; Float infinity; Float -0.0; Int 4 ]
                    cellColumn
                        "c"
                        DecimalType
                        [ Decimal "1.5"
                          Null
                          Decimal "-2"
                          Decimal("9" + String.replicate 400 "9")
                          Decimal "10" ]
                    // a decimal column built by hand around text that is not decimal: unreadable, a finding
                    Column.ofDecimals "c" (Vector.ofArray [| "1"; "abc"; "-1" + String.replicate 400 "0" |]) AllValid
                    cellColumn "c" StringType [ Str "50"; Null ]
                    cellColumn "c" BoolType [ Bool true ]
                    cellColumn "c" DateType [ Date "2020-01-01" ] ]

              for c in columns do
                  let t: Table =
                      { Schema = [ Field.create "c" c.Type ]
                        Columns = [ c ] }

                  Expect.equal
                      ((ColumnValidator.inRange "c" 0.0 10.0).Run t
                       |> List.map (fun d -> d.Code, d.Node))
                      (inRangeOverCells "c" 0.0 10.0 c)
                      (sprintf "%s column" (ColumnType.tag c.Type))

          testCase "unique on the typed storage: absent key cells are one value, tokens decide equality"
          <| fun _ ->
              let t: Table =
                  { Schema = [ Field.create "k" DecimalType; Field.create "n" IntType ]
                    Columns =
                      [ cellColumn "k" DecimalType [ Decimal "1.5"; Decimal "1.50"; Null; Null; Decimal "2" ]
                        cellColumn "n" IntType [ Int 1; Int 1; Null; Null; Int 1 ] ] }

              Expect.equal
                  ((ColumnValidator.unique [ "k"; "n" ]).Run t
                   |> List.map (fun d -> d.Code, d.Node))
                  [ ColumnValidator.UniqueCode, Some "k,n#1"
                    ColumnValidator.UniqueCode, Some "k,n#3" ]
                  "1.5 = 1.50, and a repeated all-absent key is a repeat" ]

// ---- Phase 489: a finding carries the recovery its rule stated ----

/// `wordRule` stating its recovery: the remedy names the word to drop, the alternatives the other
/// words of the text.
let private guidedWordRule (id: string) (word: string) : Validator.GuidedCheck<string, int> =
    { RuleId = id
      Run =
        fun text ->
            let words = text.Split ' ' |> Array.toList

            (wordRule id word).Run text
            |> List.map (
                Validator.Finding.guided
                    { Message = "drop the word " + word
                      Alternatives = words |> List.filter ((<>) word) |> List.distinct }
            ) }

[<Tests>]
let findingTests =
    testList
        "Validator.Finding (Phase 489)"
        [ testCase "a guided rule's remedy and alternatives survive the walk, beside the defect run reports"
          <| fun _ ->
              let rs =
                  Validator.Rules.ofGuided [ guidedWordRule "g" "x"; Validator.asGuided (wordRule "p" "y") ]
                  |> orFail521

              let findings = Validator.Rules.runFindings rs "x y z x"

              Expect.equal
                  (findings |> List.map (fun (id, f) -> id, f.Defect))
                  (Validator.Rules.run rs "x y z x")
                  "run is the guided walk read back as defects"

              Expect.equal
                  (findings
                   |> List.map (fun (id, f) -> id, f.Defect.Node, f.Defect.Family, f.Guidance))
                  [ "g",
                    Some 0,
                    "g",
                    Some
                        { Message = "drop the word x"
                          Alternatives = [ "y"; "z" ] }
                    "g",
                    Some 3,
                    "g",
                    Some
                        { Message = "drop the word x"
                          Alternatives = [ "y"; "z" ] }
                    "p", Some 1, "p", None ]
                  "the guidance as stated, the plain check's defect unguided, every finding stamped"

              Expect.equal
                  (Validator.canonicalCodes (findings |> List.map (fun (_, f) -> f.Defect)))
                  (Validator.canonicalCodes (Validator.Rules.run rs "x y z x" |> List.map snd))
                  "one parity string"

          testCase "a guided rule that throws is one unguided RULE-FAULT and the rules after it still run"
          <| fun _ ->
              let throwing: Validator.GuidedCheck<string, int> =
                  { RuleId = "boom"
                    Run = fun _ -> failwith "kaput" }

              let rs = Validator.Rules.ofGuided [ throwing; guidedWordRule "a" "x" ] |> orFail521

              match Validator.Rules.runFindings rs "x" with
              | [ ("boom", fault); ("a", found) ] ->
                  Expect.equal fault.Defect.Code Validator.FamilyFaultCode "the fault"
                  Expect.equal fault.Defect.Family "boom" "stamped with the throwing rule"
                  Expect.isNone fault.Guidance "a fault states no guidance"
                  Expect.isSome found.Guidance "the next rule's guidance is kept"
              | other -> failtestf "expected a fault then a finding, got %A" other

          testCase "the lattice holds guided rules: refusals, position, and the guidance through every verb"
          <| fun _ ->
              let rs =
                  Validator.Rules.ofChecks [ wordRule "a" "x"; wordRule "b" "y" ] |> orFail521

              match Validator.Rules.registerGuided (guidedWordRule "a" "q") rs with
              | Error(RegistrationError.DuplicateRule("a", [ "a"; "b" ])) -> ()
              | other -> failtestf "expected DuplicateRule a, got %A" other

              match Validator.Rules.ofGuided [ guidedWordRule "a" "x"; guidedWordRule "a" "y" ] with
              | Error(RegistrationError.DuplicateRule("a", [ "a" ])) -> ()
              | other -> failtestf "expected the repeat refused, got %A" other

              match Validator.Rules.replaceGuided (guidedWordRule "z" "q") rs with
              | Error(RegistrationError.UnknownRule("z", [ "a"; "b" ])) -> ()
              | other -> failtestf "expected UnknownRule z, got %A" other

              let swapped = Validator.Rules.replaceGuided (guidedWordRule "a" "x") rs |> orFail521
              Expect.equal (Validator.Rules.enumerate swapped) [ "a"; "b" ] "position kept"

              Expect.equal
                  (Validator.Rules.runFindings swapped "x y"
                   |> List.map (fun (id, f) -> id, f.Guidance |> Option.map _.Message))
                  [ "a", Some "drop the word x"; "b", None ]
                  "the swapped-in rule's guidance, the plain rule's none"

              let kept =
                  Validator.Rules.union swapped (Validator.Rules.ofGuided [ guidedWordRule "c" "y" ] |> orFail521)
                  |> orFail521
                  |> Validator.Rules.restrict (Set.ofList [ "a"; "c" ])

              Expect.equal
                  (Validator.Rules.runFindings kept "x y"
                   |> List.map (fun (id, f) -> id, f.Guidance |> Option.map _.Message))
                  [ "a", Some "drop the word x"; "c", Some "drop the word y" ]
                  "union and restrict carry each rule's guidance"

              let registered =
                  Validator.Rules.registerGuided (guidedWordRule "c" "y") rs
                  |> Result.bind (Validator.Rules.unregister "c")
                  |> orFail521

              Expect.equal (Validator.Rules.enumerate registered) [ "a"; "b" ] "unregister undoes registerGuided"

          testCase "tryFind answers a rule's defects, tryFindGuided its guidance too"
          <| fun _ ->
              let rs =
                  Validator.Rules.ofGuided [ guidedWordRule "g" "x"; Validator.asGuided (wordRule "p" "y") ]
                  |> orFail521

              let plain = Validator.Rules.tryFind "g" rs |> Option.get
              let guided = Validator.Rules.tryFindGuided "g" rs |> Option.get
              let lifted = Validator.Rules.tryFindGuided "p" rs |> Option.get

              Expect.equal (plain.Run "x y") ((guidedWordRule "g" "x").Run "x y" |> List.map _.Defect) "the defects"

              Expect.equal (guided.Run "x y") ((guidedWordRule "g" "x").Run "x y") "the findings, guidance and all"

              Expect.equal
                  (lifted.Run "x y")
                  ((wordRule "p" "y").Run "x y" |> List.map Validator.Finding.ofDefect)
                  "a plain check comes back unguided" ]


// ---- Phase 709: the opaque family and the one walk ----

/// `nodew` with its `Children` reads counted — the unit a walk is made of (`Tree.preorder` reads
/// each node's children), so the count tells how many times a run walked the tree.
let private counting (w: NodeWitness<RNode, string>) : NodeWitness<RNode, string> * int ref =
    let reads = ref 0

    { w with
        Children =
            fun n ->
                reads.Value <- reads.Value + 1
                w.Children n },
    reads

/// `nodew`, except that reading the children of a `bad` node throws — a walk that fails mid-tree.
let private brittle: NodeWitness<RNode, string> =
    { nodew with
        Children =
            fun n ->
                if n.Kind = "bad" then
                    failwith ("no children at " + n.Id)
                else
                    n.Children }

let private kinds = [| "doc"; "section"; "para"; "note"; "bad" |]

/// A seeded tree: up to three children a node, `depth` levels, ids the path from the root.
let rec private grown (rng: System.Random) (depth: int) (id: string) : RNode =
    let width = if depth = 0 then 0 else rng.Next 4

    { RNode.node
          id
          kinds[rng.Next kinds.Length]
          [ for i in 0 .. width - 1 -> grown rng (depth - 1) (id + "." + string i) ] with
        Value = if rng.Next 3 = 0 then "" else "v" }

/// Family `k` of shape `shape`: three per-node shapes (one throws at some nodes after earlier
/// findings) and three whole-tree shapes (one walks the tree itself, one throws).
let private familyOf (k: int) (shape: int) : RuleFamily<RNode, string> =
    let id = sprintf "f%02d" k

    match shape with
    | 0 ->
        Validator.perNode id (fun w n ->
            if n.Value = "" then
                [ Defect.create "EMPTY" Severity.Warning (w.KindTag n) (Some n.Id) ]
            else
                [])
    | 1 ->
        Validator.perNode id (fun _ n ->
            if n.Kind = "note" then
                [ Defect.create "NOTE" Severity.Info n.Id (Some n.Id) ]
            else
                [])
    | 2 ->
        Validator.perNode id (fun _ n ->
            if n.Kind = "para" && n.Id.EndsWith "1" then
                failwithf "rule %s at %s" id n.Id
            else
                [ Defect.create "SEEN" Severity.Info n.Kind (Some n.Id) ])
    | 3 ->
        Validator.wholeTree id (fun w root ->
            [ Defect.create "COUNT" Severity.Info (string (List.length (Tree.preorder w root))) None ])
    | 4 -> Validator.wholeTree id (fun _ root -> [ Defect.create "ROOT" Severity.Error root.Kind (Some root.Id) ])
    | _ ->
        Validator.wholeTree id (fun _ root ->
            if root.Kind = "doc" then
                failwith "whole-tree rule exploded"
            else
                [])

let private registryOf (families: RuleFamily<RNode, string> list) : Validator.RuleRegistry<RNode, string> =
    Validator.ofFamilies families
    |> Result.defaultWith (fun e -> failtestf "registry: %A" e)

/// THE ORACLE: every family walked on its own, in registration order — the walk `runAll` made before
/// Phase 709, kept through `asCheck` (a family run alone) and `Rules.run` (the one guarded walker).
let private perFamilyWalk
    (w: NodeWitness<RNode, string>)
    (families: RuleFamily<RNode, string> list)
    (root: RNode)
    : (string * Defect<string>) list =
    match Validator.Rules.ofChecks (families |> List.map (Validator.asCheck w)) with
    | Ok rules -> Validator.Rules.run rules root
    | Error e -> failtestf "oracle registry: %A" e

[<Tests>]
let oneWalkTests =
    testList
        "Validator.RuleFamily (Phase 709)"
        [ testCase "a family has no public representation: no public constructor, field or case"
          <| fun _ ->
              let t = typeof<RuleFamily<RNode, string>>

              let flags =
                  System.Reflection.BindingFlags.Public
                  ||| System.Reflection.BindingFlags.Instance

              Expect.isEmpty (t.GetConstructors flags) "no public constructor"

              Expect.equal
                  (t.GetProperties flags |> Array.map _.Name |> Array.sort)
                  [| "Id" |]
                  "the id is the one public read"

              Expect.isEmpty
                  (t.GetMethods(System.Reflection.BindingFlags.Public ||| System.Reflection.BindingFlags.Static)
                   |> Array.filter (fun m -> m.Name.StartsWith "New"))
                  "no public case constructor"

          testCase "the constructors keep the id and run the body they were given"
          <| fun _ ->
              let whole =
                  Validator.wholeTree "w" (fun w root -> [ Defect.create "W" Severity.Info "" (Some(w.Id root)) ])

              let node =
                  Validator.perNode "n" (fun w n -> [ Defect.create "N" Severity.Info "" (Some(w.Id n)) ])

              let tree =
                  RNode.node "r" "doc" [ RNode.leaf "a" "para" "x"; RNode.leaf "b" "para" "y" ]

              Expect.equal [ whole.Id; node.Id ] [ "w"; "n" ] "the ids"

              Expect.equal
                  ((Validator.asCheck nodew whole).Run tree |> List.map _.Node)
                  [ Some "r" ]
                  "a whole-tree family runs once, at the root"

              Expect.equal
                  ((Validator.asCheck nodew node).Run tree |> List.map _.Node)
                  [ Some "r"; Some "a"; Some "b" ]
                  "a per-node family runs at every node, in preorder"

          testCase "the per-node families of a registry walk the tree once"
          <| fun _ ->
              let tree =
                  RNode.node
                      "r"
                      "doc"
                      [ for i in 0..19 ->
                            RNode.node
                                (sprintf "s%d" i)
                                "section"
                                [ RNode.leaf (sprintf "s%d.p" i) "para" "v"
                                  RNode.leaf (sprintf "s%d.n" i) "note" "" ] ]

              let once, onceReads = counting nodew
              Tree.preorder once tree |> ignore

              let w, reads = counting nodew
              let reg = registryOf [ for k in 0..29 -> familyOf k (k % 2) ]
              let found = Validator.runAll w reg tree

              Expect.isNonEmpty found "the families found something"
              Expect.equal reads.Value onceReads.Value "one preorder's reads, not thirty"

          testCase "a registry of whole-tree families only takes no preorder of its own (a guard: never red before)"
          <| fun _ ->
              let tree = grown (System.Random 7) 4 "r"
              let w, reads = counting nodew
              let reg = registryOf [ familyOf 0 4; familyOf 1 5 ]
              let found = Validator.runAllTagged w reg tree

              Expect.equal reads.Value 0 "no walk: neither family reads a child"
              Expect.equal found (perFamilyWalk nodew [ familyOf 0 4; familyOf 1 5 ] tree) "the per-family findings"

          testCase "an empty registry finds nothing and walks nothing (a guard)"
          <| fun _ ->
              let w, reads = counting nodew
              Expect.isEmpty (Validator.runAll w Validator.empty (grown (System.Random 1) 4 "r")) "nothing"
              Expect.equal reads.Value 0 "no walk"

          testCase "a throwing rule is one RULE-FAULT for its family and the others still run (a guard)"
          <| fun _ ->
              let tree =
                  RNode.node
                      "r"
                      "doc"
                      [ RNode.leaf "r.0" "para" ""
                        RNode.leaf "r.1" "para" "v"
                        RNode.leaf "r.2" "note" "" ]

              let families = [ familyOf 0 0; familyOf 1 2; familyOf 2 1; familyOf 3 4 ]
              let found = Validator.runAllTagged nodew (registryOf families) tree

              Expect.equal
                  (found |> List.map (fun (f, d) -> f, d.Code, d.Node))
                  [ "f00", "EMPTY", Some "r"
                    "f00", "EMPTY", Some "r.0"
                    "f00", "EMPTY", Some "r.2"
                    "f01", Validator.FamilyFaultCode, None
                    "f02", "NOTE", Some "r.2"
                    "f03", "ROOT", Some "r" ]
                  "f01's findings before the throw are dropped with it, as when it walked alone"

              Expect.equal (snd found[3]).Message "rule 'f01' failed: rule f01 at r.1" "the fault names the family"

          testCase
              "a walk that throws is a RULE-FAULT for each per-node family, whole-tree families still run (a guard)"
          <| fun _ ->
              let tree = RNode.node "r" "doc" [ RNode.node "r.0" "bad" [] ]
              let families = [ familyOf 0 0; familyOf 1 4; familyOf 2 1 ]
              let found = Validator.runAllTagged brittle (registryOf families) tree

              Expect.equal
                  (found |> List.map (fun (f, d) -> f, d.Code))
                  [ "f00", Validator.FamilyFaultCode
                    "f01", "ROOT"
                    "f02", Validator.FamilyFaultCode ]
                  "each per-node family faults; the whole-tree family answers"

              Expect.equal found (perFamilyWalk brittle families tree) "as when each walked for itself"

          testCase
              "the one walk and the per-family walk agree, finding for finding, over generated trees and registries"
          <| fun _ ->
              for seed in 1..300 do
                  let rng = System.Random seed
                  let tree = grown rng (rng.Next 6) "r"
                  let w = if seed % 3 = 0 then brittle else nodew
                  let families = [ for k in 0 .. rng.Next 9 - 1 -> familyOf k (rng.Next 6) ]
                  let reg = registryOf families
                  let expected = perFamilyWalk w families tree

                  Expect.equal
                      (Validator.runAllTagged w reg tree)
                      expected
                      (sprintf "seed %d: the tagged findings" seed)

                  Expect.equal
                      (Validator.runAll w reg tree)
                      (List.map snd expected)
                      (sprintf "seed %d: the findings" seed) ]
