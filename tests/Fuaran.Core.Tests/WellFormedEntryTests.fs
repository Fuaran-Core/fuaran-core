module Fuaran.Core.Tests.WellFormedEntryTests

// Phase 586 — the tree algebra's one input hypothesis, `tree-algebra-well-formed-states`, is
// discharged at the entry point. Every public entry a tree takes into the skeleton-op algebra
// refuses a base carrying one id twice, naming the first id the preorder carries twice, and an
// id-unique base answers exactly what it answered before: `Ops.apply` / `canApply` / `applyAll` /
// `canApplyAll` (this phase), `Tree.Index.build`, `Arbitration.arbitrate` / `arbitrateIn` and
// `Diff.toOps` (already so). The ladder row cites this list by name
// (`Suite:WellFormedEntryTests.wellFormedEntryTests`), so a case renamed here is a red ladder. The
// one entry that ADMITS such a base by design — `Arbitration.arbitrateWith` handed a caller's own
// `canApply` — is kept beside the refusals as the standing go-red: the same proposals the
// scope-taking entries refuse land there, which is what shows the refusals are the entries' doing.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ConformanceTests

/// `root(doc)[ a(section)[ x ], b(section)[ x ] ]` — `x` under two parents, so `Tree.wellFormed`
/// names `x` at its second preorder occurrence.
let private twice () =
    RNode.node
        "root"
        "doc"
        [ RNode.node "a" "section" [ RNode.leaf "x" "para" "1" ]
          RNode.node "b" "section" [ RNode.leaf "x" "para" "2" ] ]

let private plain = OpScope.plain nodew idw

let private contained =
    OpScope.contained (fun (n: RNode) -> n.Kind <> "para") nodew idw

/// Every op kind, each addressing only nodes the base holds and bringing in no repeated id, so a
/// refusal is the BASE's and not the op's. The same ops are accepted on `sample ()`, which holds
/// the same parents.
let private everyKind () : (string * SkeletonOp<RNode, string>) list =
    [ "insert", InsertChild("a", RNode.leaf "fresh" "para" "f")
      "remove", RemoveNode "b"
      "move", MoveNode("b", "a")
      "reorder", ReorderChildren("root", [ "b"; "a" ])
      "update", UpdateNode(RNode.node "b" "section" [])
      "batch", Batch [ RemoveNode "b"; InsertChild("a", RNode.leaf "fresh" "para" "f") ] ]

let private prop id ops : OpScriptProposal<RNode, string> =
    { Id = id
      Holder = sprintf "agent-%d" id
      Ops = ops }

/// The proposals the arbitration cases offer: each applies on its own to an id-unique base of the
/// same shape, and they are independent, so on `twice ()` nothing but the base can refuse them.
let private proposals () =
    [ prop 1 [ InsertChild("a", RNode.leaf "p1" "para" "1") ]
      prop 2 [ InsertChild("b", RNode.leaf "p2" "para" "2") ] ]

let private expectBaseRefused (what: string) (r: Result<'a, Rejection<string>>) =
    match r with
    | Error(DuplicateId "x") -> ()
    | other -> failtestf "%s: expected the base's DuplicateId \"x\", got %A" what other

[<Tests>]
let wellFormedEntryTests =
    testList
        "WellFormedEntry"
        [ testCase "Ops.apply refuses a base carrying one id twice, under every scope and for every op kind"
          <| fun _ ->
              for scopeName, scope in [ "plain", plain; "contained", contained ] do
                  for kind, op in everyKind () do
                      Ops.apply Policy.none scope op (twice ())
                      |> expectBaseRefused (sprintf "%s under %s" kind scopeName)

          testCase "an id-unique base is untouched: the same ops are accepted on sample()"
          <| fun _ ->
              for scopeName, scope in [ "plain", plain; "contained", contained ] do
                  for kind, op in everyKind () do
                      match Ops.apply Policy.none scope op (sample ()) with
                      | Ok after ->
                          Expect.isTrue
                              (Tree.isWellFormed nodew idw after)
                              (sprintf "%s under %s: the accepted tree is id-unique" kind scopeName)
                      | Error e -> failtestf "%s under %s: the id-unique base was refused: %A" kind scopeName e

          testCase "under a keyed scope the repeat is seen across the keyed walk, and only there"
          <| fun _ ->
              // `x` once as a structural child and once in the case table: the keyed walk carries it
              // twice, the structural walk once. The scope's walk decides, exactly as it decides
              // for `Diff.toOps` and `Arbitration.arbitrate`.
              let keyedTwice: KNode =
                  { Id = "root"
                    Kind = "doc"
                    Children =
                      [ { Id = "x"
                          Kind = "para"
                          Children = []
                          Cases = [] } ]
                    Cases =
                      [ "one",
                        { Id = "x"
                          Kind = "para"
                          Children = []
                          Cases = [] } ] }

              let fresh: KNode =
                  { Id = "fresh"
                    Kind = "para"
                    Children = []
                    Cases = [] }

              let op = InsertChild("root", fresh)
              let keyed = OpScope.keyed keyw (fun _ -> true) knodew idw

              Ops.apply Policy.none keyed op keyedTwice |> expectBaseRefused "keyed scope"

              match Ops.apply Policy.none (OpScope.contained (fun _ -> true) knodew idw) op keyedTwice with
              | Ok _ -> ()
              | Error e -> failtestf "the structural walk holds `x` once, so the contained scope accepts: got %A" e

          testCase "Ops.canApply, applyAll and canApplyAll refuse the same base at position 0, the tree untouched"
          <| fun _ ->
              let offered = twice ()

              Ops.canApply Policy.none plain (RemoveNode "b") offered
              |> expectBaseRefused "canApply"

              match Ops.applyAll Policy.none plain [ RemoveNode "b"; RemoveNode "a" ] offered with
              | Error r ->
                  Expect.equal r.Applied 0 "nothing applied"
                  Expect.equal r.Rejection (DuplicateId "x") "the base's id"
                  Expect.equal r.Tree offered "the tree offered is the tree returned"
              | Ok _ -> failtest "applyAll accepted a base carrying `x` twice"

              match Ops.canApplyAll Policy.none plain [ RemoveNode "b"; RemoveNode "a" ] offered with
              | Error(0, DuplicateId "x") -> ()
              | other -> failtestf "canApplyAll: expected (0, DuplicateId \"x\"), got %A" other

          testCase "Tree.Index.build refuses the base, naming the same id"
          <| fun _ ->
              match Tree.Index.build nodew idw (twice ()) with
              | Error "x" -> ()
              | other -> failtestf "expected Error \"x\", got %A" (Result.map ignore other)

          testCase
              "Arbitration.arbitrate and arbitrateIn refuse the base: every proposal Inapplicable at 0, nothing accepted"
          <| fun _ ->
              for name, r in
                  [ "arbitrate", Arbitration.arbitrate Policy.none plain (twice ()) (proposals ())
                    "arbitrateIn", Arbitration.arbitrateIn Policy.none plain (twice ()) (proposals ()) ] do
                  Expect.isEmpty r.Accepted (sprintf "%s: nothing accepted" name)
                  Expect.isEmpty r.MergedScript (sprintf "%s: no merged script" name)

                  Expect.equal
                      (r.Rejected |> List.map (fun (p, why) -> p.Id, why))
                      [ 1, Inapplicable(0, DuplicateId "x"); 2, Inapplicable(0, DuplicateId "x") ]
                      (sprintf "%s: every proposal refused by the base, at index 0" name)

          testCase "Diff.toOps refuses a repeated id on either side"
          <| fun _ ->
              Diff.toOps Policy.none plain (twice ()) (sample ())
              |> expectBaseRefused "before"

              Diff.toOps Policy.none plain (sample ()) (twice ()) |> expectBaseRefused "after"

          testCase
              "the standing go-red: arbitrateWith handed a blind canApply admits the base the scope-taking entries refuse"
          <| fun _ ->
              // The one entry that takes a caller's `canApply` and checks no base of its own: a
              // domain composing it directly checks its base itself (its doc says so). With a
              // `canApply` that reads nothing, the proposals `arbitrate` refuses above LAND — which
              // is what pins the refusals to the entries rather than to the proposals. Handed
              // `Ops.canApplyAll` instead, it refuses as `arbitrate` does, because the check is at
              // that entry too.
              let blind: SkeletonOp<RNode, string> list -> RNode -> Result<unit, int * Rejection<string>> =
                  fun _ _ -> Ok()

              let admitted =
                  Arbitration.arbitrateWith (Ops.footprint Policy.none plain) blind (twice ()) (proposals ())

              Expect.equal (admitted.Accepted |> List.map _.Id) [ 1; 2 ] "the blind entry admits both proposals"

              let checked =
                  Arbitration.arbitrateWith
                      (Ops.footprint Policy.none plain)
                      (Ops.canApplyAll Policy.none plain)
                      (twice ())
                      (proposals ())

              Expect.isEmpty checked.Accepted "handed the engine's own canApplyAll, the base is refused there" ]
