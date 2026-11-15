module Fuaran.Core.Tests.IdlContractDiffTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Tests

// Phase 644 (DECISIONS.md D260.16, D260.18) — the IDL differ classifies a contract change. Every
// pair is `ContractsIdl.diffCases`, rendered through the real artifact, so the tests read the bytes a
// committed `idl.json` is made of. The same pairs are the differ vectors of
// `conformance/idl/contracts.json`.

let private rows (before: Idl) (after: Idl) : Classification list =
    match Diff.parse (Artifact.render before) None, Diff.parse (Artifact.render after) None with
    | Ok b, Ok a -> Diff.changes b a |> List.map Diff.classify
    | Error e, _
    | _, Error e -> failtestf "artifact did not read back: %s" e

let private verdictOf (before: Idl) (after: Idl) : Verdict =
    match Diff.ofArtifacts (Artifact.render before, None) (Artifact.render after, None) with
    | Ok v -> v
    | Error e -> failtestf "idl-diff: %s" e

let private case (name: string) : Idl * Idl =
    match ContractsIdl.diffCases |> List.tryFind (fun (n, _, _, _) -> n = name) with
    | Some(_, before, after, _) -> before, after
    | None -> failtestf "no differ case named %s" name

let private rowsOf (name: string) : Classification list = case name ||> rows

let private only (name: string) : Classification =
    match rowsOf name with
    | [ one ] -> one
    | other -> failtestf "%s: expected one row, got %d" name other.Length

[<Tests>]
let tests =
    testList
        "IdlContractDiff"
        [ testCase "every differ case is two valid vocabularies, and no name repeats" (fun _ ->
              let names = ContractsIdl.diffCases |> List.map (fun (n, _, _, _) -> n)
              Expect.equal (List.distinct names) names "the names are distinct"

              for (name, before, after, _) in ContractsIdl.diffCases do
                  Expect.isEmpty (Declare.errors before) (name + ": the revision before is valid")
                  Expect.isEmpty (Declare.errors after) (name + ": the revision after is valid"))

          testList
              "each case is the class it is declared"
              [ for (name, before, after, expected) in ContractsIdl.diffCases ->
                    testCase name (fun _ ->
                        Expect.equal
                            (Diff.classLabel (Diff.verdictClass (verdictOf before after)))
                            expected
                            (name + ": the verdict's class")) ]

          testCase "two vocabularies that differ only in a contract are no longer unchanged" (fun _ ->
              // The gap this closed: the differ read no `contracts`, so such a pair had no row.
              let before, after = case "method-removed"

              Expect.equal
                  { after with
                      Contracts = before.Contracts }
                  before
                  "the pair differs in its contracts alone"

              Expect.isNonEmpty (rows before after) "and the differ reports it")

          testCase "the classes the phase states: additive" (fun _ ->
              Expect.equal (only "method-added").Change (MethodAdded "orders.export") "a new method"
              Expect.equal (only "method-added").Severity Additive "is additive"

              match (only "argument-added-with-default").Change with
              | ArgumentAdded("orders.get", x) ->
                  Expect.equal x.Name "verbose" "the argument"
                  Expect.equal x.Presence.Class "omitDefault" "declared with a default"
              | other -> failtestf "unexpected row %A" other

              Expect.equal (only "argument-added-with-default").Severity Additive "is additive (D259)"
              Expect.equal (only "argument-added-optional").Severity Additive "an optional one too")

          testCase "the classes the phase states: breaking" (fun _ ->
              Expect.equal (only "method-removed").Change (MethodRemoved "orders.export") "a removed method"
              Expect.equal (only "method-removed").Severity BreakingWire "is breaking"
              Expect.equal (only "argument-type-changed").Severity BreakingWire "a changed type"
              Expect.equal (only "constraint-narrowed").Severity BreakingWire "a narrowed constraint"

              Expect.equal
                  (only "authority-added").Change
                  (MethodAuthorityChanged("orders.get", [ "reader" ], [ "reader"; "writer" ]))
                  "a widened authority requirement"

              Expect.equal (only "authority-added").Severity BreakingWire "is breaking"
              Expect.equal (only "argument-added-required").Severity BreakingWire "a required argument")

          testCase "a contract that arrives or leaves is one row, not a row per method" (fun _ ->
              Expect.equal (only "contract-added").Change (ContractAdded "probe") "added"
              Expect.equal (only "contract-removed").Change (ContractRemoved "probe") "removed")

          testCase "a reorder names the arguments both revisions carry, in both orders" (fun _ ->
              Expect.equal
                  (only "arguments-reordered").Change
                  (ArgumentsReordered("orders.list", [ "paging"; "limit"; "status" ], [ "status"; "limit"; "paging" ]))
                  "the reorder"

              // An argument that arrives in the middle moves nobody's relative order.
              let before, _ = case "unchanged"

              let after =
                  ContractsIdl.withMethod "list" (fun m ->
                      { m with
                          Arguments =
                              m.Arguments.[..0]
                              @ [ { ContractsIdl.arg "cursor" TStr with
                                      Opt = Optional } ]
                              @ m.Arguments.[1..] })

              match rows before after with
              | [ one ] ->
                  match one.Change with
                  | ArgumentAdded("orders.list", x) -> Expect.equal x.Name "cursor" "the arrival, and no reorder"
                  | other -> failtestf "unexpected row %A" other
              | other -> failtestf "expected one row, got %d" other.Length)

          testCase "a type that moves across an erased slot is undecided, never called a break" (fun _ ->
              let row = only "argument-type-across-erased-slot"
              Expect.equal row.Severity Unclassifiable "undecided"
              Expect.stringContains row.Rationale "json" "and the rationale names the slot")

          testCase "a constraint is ordered within its kind only" (fun _ ->
              Expect.equal (only "constraint-widened").Severity Additive "wider bounds admit every old value"
              Expect.equal (only "constraint-withdrawn").Severity Additive "no bound admits them too"
              Expect.equal (only "constraint-declared").Severity BreakingWire "a new bound may refuse one"

              // Bounds that neither contain the other, and a bound of another kind, are breaking.
              let before, _ = case "unchanged"

              let shifted =
                  ContractsIdl.withMethod "list" (fun m ->
                      { m with
                          Arguments =
                              m.Arguments
                              |> List.map (fun a ->
                                  if a.Name = "limit" then
                                      { a with
                                          Constraint = Some(ArgumentConstraint.IntBetween(10, 200)) }
                                  else
                                      a) })

              Expect.equal (rows before shifted |> List.map _.Severity) [ BreakingWire ] "overlapping bounds")

          testCase "the promises beside the types are graded in their own direction (D260.18)" (fun _ ->
              let graded (name: string) =
                  let row = only name

                  match row.Change with
                  | MethodPromiseChanged _ -> row.Severity
                  | other -> failtestf "%s: unexpected row %A" name other

              Expect.equal (graded "destination-gained") BreakingWire "a destination gained"
              Expect.equal (graded "destination-lost") Additive "a destination lost"
              Expect.equal (graded "idempotent-lost") BreakingWire "idempotent lost"
              Expect.equal (graded "idempotent-gained") Additive "idempotent gained"
              Expect.equal (graded "read-policy-changed") BreakingWire "any move of the read policy"
              Expect.equal (graded "rate-limit-declared") BreakingWire "a limit declared"
              Expect.equal (graded "rate-limit-tightened") BreakingWire "a limit tightened"
              Expect.equal (graded "rate-limit-loosened") Additive "a limit loosened"
              Expect.equal (graded "rate-limit-withdrawn") Additive "a limit withdrawn")

          testCase "what only the host reads is host surface, one row a fact" (fun _ ->
              Expect.equal
                  (only "description-changed").Change
                  (ContractHostFactChanged("method orders.get", "doc", "One order by its id.", "One order, by id."))
                  "a description"

              let facts =
                  rowsOf "audit-kind-and-record-changed"
                  |> List.map (fun r ->
                      match r.Change with
                      | ContractHostFactChanged(subject, fact, _, _) -> subject, fact, r.Severity
                      | other -> failtestf "unexpected row %A" other)

              Expect.equal
                  facts
                  [ "method orders.place", "auditKind", HostSurfaceOnly
                    "method orders.place", "record", HostSurfaceOnly ]
                  "the audit kind and the record")

          testCase "a member the artifact omits at its default reads as that default" (fun _ ->
              // `export` writes `shape`; moving it to `call` omits the member, and the row names both.
              Expect.equal
                  (only "shape-changed").Change
                  (MethodShapeChanged("orders.export", "job", "call"))
                  "the omitted shape is `call`")

          testCase "a contract change moves no generated F# shape and obliges one contract row" (fun _ ->
              for (name, before, after, _) in ContractsIdl.diffCases do
                  for row in rows before after do
                      Expect.equal
                          (Diff.consequences row)
                          [ NoGeneratedShapeChange ]
                          (name + ": no F# is generated for a contract")

                      match Diff.obligations [] row with
                      | [ one ] -> Expect.stringContains one.Surface "contract" (name + ": the contract row")
                      | other -> failtestf "%s: expected one obligation, got %d" name other.Length)

          testCase
              "a consumer that ignores unknown members absorbs an argument it may leave out, and nothing else"
              (fun _ ->
                  let absorbed (name: string) =
                      rowsOf name |> List.forall Diff.absorbedWhenUnknownMembersIgnored

                  Expect.isTrue (absorbed "argument-added-optional") "an optional argument"
                  Expect.isTrue (absorbed "argument-added-with-default") "one with a default"
                  Expect.isFalse (absorbed "argument-added-required") "a required one is a break"
                  Expect.isFalse (absorbed "method-added") "a new method is not absorbed"
                  Expect.isFalse (absorbed "authority-removed") "nor is a loosened requirement") ]
