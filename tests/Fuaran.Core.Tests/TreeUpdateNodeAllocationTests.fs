module Fuaran.Core.Tests.TreeUpdateNodeAllocationTests

// Phase 657 — `Tree.updateNode` finds its target without a per-child allocation. A single-node write
// costs the root-to-target path it rebuilds, not the nodes its depth-first search visits on the way.
// The fixture is the shape the cost was first measured on: 241 nodes, 24 deep. The test pins one
// write's allocation at a bound the measurement justifies, and shows the bound bites by measuring
// the find this phase replaced, kept verbatim below, over the same tree.

open System
open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

/// The measured shape: a spine 24 levels deep under the root, with ten children at every level, the
/// spine child LAST among them, so the target (the deepest spine node) is the last node of the
/// preorder and the search visits every node of the tree before it: 1 + 24 × 10 = 241 nodes.
let private depth = 24
let private fanOut = 10

let private spineTree () : RNode =
    let rec level (d: int) : RNode =
        let leaves =
            List.init (fanOut - 1) (fun i -> RNode.leaf (sprintf "n%d-%d" d i) "para" "v")

        let spine =
            if d = depth then
                RNode.leaf (sprintf "s%d" d) "para" "v"
            else
                level (d + 1)

        RNode.node (sprintf "s%d" (d - 1)) "section" (leaves @ [ spine ])

    level 1

let private target = sprintf "s%d" depth

/// The pre-Phase-657 `updateNode`, verbatim: its find allocates a `mapi` cell, two tuples, an
/// ancestors cell and an `@` copy cell for every child of every node it visits.
let private updateNodeBefore
    (w: NodeWitness<'Node, 'Id>)
    (idw: IdWitness<'Id>)
    (target: 'Id)
    (f: 'Node -> 'Node)
    (root: 'Node)
    : 'Node option =
    let rec find (stack: ('Node * ('Node * int) list) list) =
        match stack with
        | [] -> None
        | (node, ancestors) :: rest ->
            if idw.Equals (w.Id node) target then
                Some(node, ancestors)
            else
                find ((w.Children node |> List.mapi (fun i c -> c, (node, i) :: ancestors)) @ rest)

    find [ root, [] ]
    |> Option.map (fun (node, ancestors) ->
        (f node, ancestors)
        ||> List.fold (fun child (parent, i) ->
            w.ReplaceChildren parent (w.Children parent |> List.mapi (fun j c -> if j = i then child else c))))

let private write (n: RNode) = { n with Value = "X" }

/// The fewest bytes one write allocated over several runs, after a warm-up, on this thread — the
/// minimum so a stray allocation by the runtime cannot read as the function's.
let private bytesOfOneWrite (update: string -> RNode -> RNode option) (tree: RNode) : int64 =
    for _ in 1..3 do
        update target tree |> ignore

    [ 1..5 ]
    |> List.map (fun _ ->
        let before = GC.GetAllocatedBytesForCurrentThread()
        let r = update target tree
        let bytes = GC.GetAllocatedBytesForCurrentThread() - before
        GC.KeepAlive r
        bytes)
    |> List.min

let private now (t: string) (n: RNode) = Tree.updateNode nodew idw t write n
let private before (t: string) (n: RNode) = updateNodeBefore nodew idw t write n

/// The pinned bound for one write on the 241-node, 24-deep tree. The rebuilt path alone is 24
/// ancestors, each a record copy and the cells of its new child list up to the changed child, and
/// the target's own copy; what the search adds is a per-level frame, not a per-visited-child one.
/// Measured on this repository at Phase 657, Debug and Release alike: 11,840 B, against 48,816 B for
/// the replaced search (the 0.43.0 ledger). The bound is that figure with headroom for a runtime's
/// collection growth, well inside the phase's 20 KB acceptance and far below the replaced search.
let private bound = 14_000L

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Tree.updateNode allocation (Phase 657)"
        [ testCase "the fixture is the measured shape: 241 nodes, the target 24 deep and last in preorder"
          <| fun _ ->
              let tree = spineTree ()
              let order = Tree.preorder nodew tree
              Expect.equal (List.length order) 241 "241 nodes"
              Expect.equal (List.last order).Id target "the target is the last node the search reaches"

              Expect.equal
                  (Tree.path nodew idw target tree |> Option.map List.length)
                  (Some(depth + 1))
                  "the target sits 24 levels below the root"

          testCase "the result is the replaced find's result, at every node and for an absent id"
          <| fun _ ->
              let tree = spineTree ()

              for n in Tree.preorder nodew tree do
                  Expect.equal (now n.Id tree) (before n.Id tree) (sprintf "the write at %s" n.Id)

              Expect.isNone (now "absent" tree) "an absent target is None"

          testCase "the first preorder match is the one rewritten when an id repeats"
          <| fun _ ->
              let dup =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node "a" "section" [ RNode.leaf "x" "para" "1" ]
                        RNode.node "b" "section" [ RNode.leaf "x" "para" "2" ] ]

              Expect.equal (now "x" dup) (before "x" dup) "the same node is rewritten"

          testCase "a deep spine does not overflow the stack"
          <| fun _ ->
              let deep =
                  (RNode.leaf "leaf" "para" "v", [ 1..100_000 ])
                  ||> List.fold (fun child i -> RNode.node (sprintf "d%d" i) "section" [ child ])

              let r = Tree.updateNode nodew idw "leaf" write deep
              Expect.isSome r "found at depth 100,000"

          testCase "one write allocates the path it rebuilds, within the pinned bound"
          <| fun _ ->
              let bytes = bytesOfOneWrite now (spineTree ())
              printfn "Phase 657: one updateNode on the 241-node, 24-deep tree allocates %d B (bound %d B)" bytes bound
              Expect.isLessThanOrEqual bytes bound "one write within the bound"

          testCase "the bound bites: the replaced find exceeds it on the same tree"
          <| fun _ ->
              let bytes = bytesOfOneWrite before (spineTree ())
              printfn "Phase 657: the replaced find allocates %d B for the same write" bytes
              Expect.isGreaterThan bytes bound "the replaced find is over the bound" ]
