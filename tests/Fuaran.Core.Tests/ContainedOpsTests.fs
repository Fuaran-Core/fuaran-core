module Fuaran.Core.Tests.ContainedOpsTests

// Phase 251 — container-aware skeleton ops. `applyContained`/`canApplyContained` reject an
// insert/move under a leaf with `NotAContainer` instead of silently no-op'ing (the F1
// adoption finding); the plain `apply`/`canApply` are unchanged (every node can hold
// children). A leaf-bearing witness then certifies green through the container-aware path.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

/// In the reference domain, a "para" is a leaf; everything else can hold children.
let private canHold (n: RNode) = n.Kind <> "para"

/// root(doc) [ s(section)[], p(para) ] — a container and a leaf as siblings.
let private mixed () =
    RNode.node "root" "doc" [ RNode.node "s" "section" []; RNode.leaf "p" "para" "x" ]

[<Tests>]
let tests =
    testList
        "Ops.applyContained"
        [ testCase "rejects an insert under a leaf with NotAContainer"
          <| fun _ ->
              let op = InsertChild("p", RNode.leaf "c" "para" "")

              match Ops.applyContained canHold nodew idw op (mixed ()) with
              | Error(NotAContainer("p", "para")) -> ()
              | other -> failtestf "expected NotAContainer, got %A" other

          testCase "accepts an insert under a container"
          <| fun _ ->
              let op = InsertChild("s", RNode.leaf "c" "para" "hi")

              match Ops.applyContained canHold nodew idw op (mixed ()) with
              | Ok r -> Expect.isSome (Tree.tryFind nodew idw "c" r) "inserted under the container"
              | Error e -> failtestf "unexpected %A" e

          testCase "the plain apply is unchanged — no NotAContainer on the default path"
          <| fun _ ->
              // default `apply` treats every node as a container (back-compat); the insert
              // succeeds structurally (ReplaceChildren on the reference RNode is total).
              let op = InsertChild("p", RNode.leaf "c" "para" "")
              Expect.isOk (Ops.apply nodew idw op (mixed ())) "default apply still accepts it"

          testCase "MoveNode under a leaf is NotAContainer"
          <| fun _ ->
              match Ops.applyContained canHold nodew idw (MoveNode("s", "p")) (mixed ()) with
              | Error(NotAContainer("p", "para")) -> ()
              | other -> failtestf "expected NotAContainer, got %A" other

          testCase "canApplyContained mirrors applyContained"
          <| fun _ ->
              let op = InsertChild("p", RNode.leaf "c" "para" "")

              Expect.equal
                  (Ops.canApplyContained canHold nodew idw op (mixed ()))
                  (Ops.applyContained canHold nodew idw op (mixed ()) |> Result.map ignore)
                  "check ≡ apply (contained)"

          testCase "a leaf-bearing witness certifies green via the container-aware path (F1 resolved)"
          <| fun _ ->
              // a generator that emits leaf "para" nodes mixed with "section" containers —
              // the exact shape that silently no-op'd before Phase 251.
              let genTree (rng: ConfRng.T) : RNode * ConfRng.T =
                  let mutable counter = 0
                  let mutable r = rng

                  let freshId () =
                      let s = sprintf "n%d" counter
                      counter <- counter + 1
                      s

                  let rec build depth =
                      let id = freshId ()
                      let leafRoll, r1 = ConfRng.intBelow 2 r
                      r <- r1

                      if depth <= 0 || leafRoll = 0 then
                          RNode.leaf id "para" "v"
                      else
                          let nKids, r2 = ConfRng.intBelow 3 r
                          r <- r2
                          RNode.node id "section" [ for _ in 1..nKids -> build (depth - 1) ]

                  let rootId = freshId ()
                  let nKids, r2 = ConfRng.intBelow 3 r
                  r <- r2
                  RNode.node rootId "section" [ for _ in 1..nKids -> build 1 ], r

              let genFresh (existing: Set<string>) (rng: ConfRng.T) : RNode * ConfRng.T =
                  let mutable r = rng

                  let rec pick () =
                      let v, r' = ConfRng.next r
                      r <- r'
                      let id = sprintf "f%d" (v % 100000)
                      if existing.Contains id then pick () else id

                  RNode.leaf (pick ()) "para" "x", r

              let opGen: OpGen<RNode, string> =
                  { Tree = genTree
                    FreshNode = genFresh
                    CanHold = Some canHold }

              let results = Conformance.opAlgebra nodew idw opGen 4242 200
              let failed = results |> List.filter (fun r -> not r.Passed)

              if not (List.isEmpty failed) then
                  let msg =
                      failed
                      |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
                      |> String.concat "\n"

                  failtestf "leaf-bearing witness failed conformance:\n%s" msg ]
