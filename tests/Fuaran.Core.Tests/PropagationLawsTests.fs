module Fuaran.Core.Tests.PropagationLawsTests

// Phase 331 — the tests of the kit's propagation laws (`PropagationLaws.fs`), certified against the
// in-repo formula sheet that stays in `ConformanceTests.fs`, moved verbatim from there.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.Reference2
open Fuaran.Core.Tests.Reference.Counter
open Fuaran.Core.Tests.ConformanceTests

let private evaluatorLaw (prefix: string) (results: LawResult list) =
    results |> List.find (fun r -> r.Law.StartsWith prefix)

[<Tests>]
let propagationEvaluatorLawTests =
    testList
        "Conformance.propagationEvaluatorLaws"
        [ testCase "the formula sheet is the first certifier — every law green, every arm reached"
          <| fun _ ->
              let results = Conformance.propagationEvaluatorLaws sheetw 2110 200
              let failed = results |> List.filter (fun r -> not r.Passed)

              if not (List.isEmpty failed) then
                  let msg =
                      failed
                      |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
                      |> String.concat "\n"

                  failtestf "the formula sheet failed propagationEvaluatorLaws:\n%s" msg

              Expect.equal (List.length results) 4 "three laws + the adequacy guard"

              Expect.equal (Conformance.propagationEvaluatorLaws sheetw 2110 200) results "same seed ⇒ identical report"

          testCase "the census calls it Guarded, and the run reports the arms it reached"
          <| fun _ ->
              match
                  KitRoster.census
                  |> List.tryFind (fun (n, _) -> n = "Conformance.propagationEvaluatorLaws")
              with
              | Some(_, Guarded _) -> ()
              | Some(_, Unconditional why) -> failtestf "censused Unconditional (%s) but it emits a guard" why
              | None -> failtest "Conformance.propagationEvaluatorLaws is missing from SampleAdequacy.census"

              let adequacy =
                  Conformance.propagationEvaluatorLaws sheetw 2110 200
                  |> List.filter (fun r -> r.Law.StartsWith "sample adequacy")

              Expect.equal (List.length adequacy) 1 "exactly one adequacy law"
              Expect.isTrue (List.head adequacy).Passed "the formula sheet reaches every arm"

          testCase "go-red: an IMPURE evaluator — one that reads a mutable cell — loses the purity law"
          <| fun _ ->
              // A clock the evaluator advances and consults on every call. Nothing it reads through
              // the resolver moved, so Phase 209's restriction cannot see it; this law can.
              let clock = ref 0

              let impure =
                  { sheetw with
                      EvalNode =
                          fun s resolve id ->
                              clock.Value <- clock.Value + 1
                              sheetEvalNode s resolve id |> Result.map (fun v -> v + clock.Value % 2) }

              let law =
                  Conformance.propagationEvaluatorLaws impure 2110 200
                  |> evaluatorLaw "the domain's evaluator is a function of what it reads"

              Expect.isFalse law.Passed "an evaluator consulting ambient state must be refused"

          testCase
              "go-red: a DISHONEST change set — the edit moved a cell the set does not name — loses honesty and agreement"
          <| fun _ ->
              // The sheet edits one cell and names a DIFFERENT one: the evaluator differs off the
              // named ids, which is the clause `agree_off` states and nothing else checks.
              let dishonest =
                  { sheetw with
                      Change =
                          fun s r ->
                              let s', edited, r = sheetEdit s r

                              let other = s |> Map.toList |> List.map fst |> List.find (fun id -> id <> edited)

                              (s', Set.singleton other), r }

              let results = Conformance.propagationEvaluatorLaws dishonest 2110 200

              Expect.isFalse
                  (evaluatorLaw "off the change set the domain names" results).Passed
                  "a change set that omits the edited cell must be refused"

              Expect.isFalse
                  (evaluatorLaw "evalFrom of the edited evaluator" results).Passed
                  "and the replay it licenses disagrees with a full evaluation"

              Expect.isTrue
                  (evaluatorLaw "the domain's evaluator is a function of what it reads" results).Passed
                  "the evaluator itself is still pure — the defect is the change set's"

          testCase "go-red: an edit that moves only what an unnamed cell ASKS for loses honesty and nothing else"
          <| fun _ ->
              // The premise's other half (`touches_off`): the edit leaves every value where it was
              // and makes one cell it does not name ask for every read it declares before it
              // evaluates. The replay still agrees — no value moved — so only the reads half of the
              // honesty law can see it, which is what shows that half has teeth.
              let spy: EvaluatorWitness<RefSheet * string option, int> =
                  { Surface = "the reference sheet, one unnamed cell asking reads it ignores"
                    Model =
                      fun r ->
                          let s, r = genSheet r
                          (s, None), r
                    Deps = fun (s, _) -> sheetw.Deps s
                    EvalNode =
                      fun (s, spyAt) resolve id ->
                          if spyAt = Some id then
                              for d in refsOf (Map.find id s) do
                                  resolve d |> ignore

                          sheetEvalNode s resolve id
                    Change =
                      fun (s, _) r ->
                          match s |> Map.filter (fun _ f -> not (Set.isEmpty (refsOf f))) |> Map.toList with
                          | [] -> ((s, None), Set.empty), r
                          | withReads ->
                              let id, r = ConfRng.choose (List.map fst withReads) r
                              ((s, Some id), Set.empty), r }

              let results = Conformance.propagationEvaluatorLaws spy 2110 200

              Expect.isFalse
                  (evaluatorLaw "off the change set the domain names" results).Passed
                  "a change set that omits a cell whose asked reads moved must be refused"

              Expect.isTrue (evaluatorLaw "evalFrom of the edited evaluator" results).Passed "no value moved"

              Expect.isTrue
                  (evaluatorLaw "the domain's evaluator is a function of what it reads" results).Passed
                  "and the evaluator is pure"

          testCase "go-red: an evaluator that never fails starves the guard, naming the arm"
          <| fun _ ->
              // Every failure mapped to a value: pure, honest, and green on all three laws — and
              // the whole-`Result` comparison never met an `Error`, which is what the guard is for.
              let neverFails =
                  { sheetw with
                      EvalNode =
                          fun s resolve id ->
                              match sheetEvalNode s resolve id with
                              | Error _ -> Ok 0
                              | ok -> ok }

              let results = Conformance.propagationEvaluatorLaws neverFails 2110 200
              let adequacy = evaluatorLaw "sample adequacy" results
              Expect.isFalse adequacy.Passed "an arm nothing reached must be reported"

              let why = adequacy.Counterexample |> Option.defaultValue ""
              // The counterexample renders every count and then the arms it never reached; the arm
              // list must be exactly the one this witness starves.
              Expect.stringContains why "never reached failing evaluator —" "it names that arm, and only that arm" ]
