module Fuaran.Core.Tests.IndexedApplyTests

// Phase 659 — `Ops.apply` has an entry that reads a `Tree.Index`. The indexed entries
// (`IndexedTree.apply`, `canApply`, `applyAll`) thread an `IndexedTree` — a tree, its scope
// and the index that describes it — and answer exactly what `apply`, `canApply` and `applyAll`
// answer under the same policy and scope: the same tree, or the same `Rejection`. The index they
// hand back is `Tree.Index.build` of the tree they hand back. And the work of one insert does not
// grow with the tree. The law is a tested law, not a modelled one (DECISIONS.md D257).

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

/// A cursor over `ConfRng`, so a body reads `draw 6` rather than threading the state.
type private Rng(seed: int) =
    let mutable state = ConfRng.ofSeed seed

    member _.Below(n: int) : int =
        let v, s = ConfRng.intBelow n state
        state <- s
        v

    member r.Pick(xs: 'a list) : 'a = List.item (r.Below(List.length xs)) xs

    member _.Shuffle(xs: 'a list) : 'a list =
        let v, s = ConfRng.shuffle xs state
        state <- s
        v

/// `≡`: the same root and parent links, the same keys, and equal nodes at every key.
let private sameIndex (a: Tree.NodeIndex<'N, string>) (b: Tree.NodeIndex<'N, string>) : bool =
    a.Root = b.Root && a.ParentOf = b.ParentOf && a.ById = b.ById

let private built (r: Result<'a, IndexRefusal<string>>) : 'a =
    match r with
    | Ok x -> x
    | Error e -> failwithf "an id-unique tree indexes, but: %A" e

let private bump (k: string) (m: Map<string, int>) =
    Map.add k ((Map.tryFind k m |> Option.defaultValue 0) + 1) m

let private kindOf (op: SkeletonOp<'N, 'I>) =
    match op with
    | InsertChild _ -> "insert"
    | RemoveNode _ -> "remove"
    | MoveNode _ -> "move"
    | ReorderChildren _ -> "reorder"
    | Batch _ -> "batch"
    | UpdateNode _ -> "update"

// ---- the reference domain: boxes hold, leaves do not, and a value may refer to ids ---------------

/// A drawn tree of up to `size` nodes, each hung under a drawn earlier node; one node in four refers
/// to another node's id through its value (`StructuralIntegrityTests.refw`'s `ref:` convention).
let private genTree (rng: Rng) (size: int) : RNode =
    let n = rng.Below size + 1

    let parents =
        [ for k in 1 .. n - 1 -> k, rng.Below k ] |> List.groupBy snd |> Map.ofList

    let rec build (k: int) : RNode =
        let kids =
            Map.tryFind k parents |> Option.map (List.map fst) |> Option.defaultValue []

        let value =
            if rng.Below 4 = 0 then
                sprintf "ref:n%d" (rng.Below n)
            else
                "v"

        { RNode.node (sprintf "n%d" k) (if List.isEmpty kids then "leaf" else "box") (kids |> List.map build) with
            Value = value }

    build 0

let mutable private freshCounter = 0

/// A graft: fresh, or carrying an id the tree holds (`DuplicateId` against the tree), or repeating
/// an id within itself, or holding a child under a leaf (`NotAContainer` inside the graft), or of a
/// kind the grammar refuses.
let private genGraft (rng: Rng) (ids: string list) : RNode =
    freshCounter <- freshCounter + 1
    let stem = sprintf "f%d" freshCounter

    match rng.Below 7 with
    | 0 -> RNode.leaf (rng.Pick ids) "leaf" "v"
    | 1 -> RNode.node stem "box" [ RNode.leaf (stem + "a") "leaf" "x"; RNode.leaf (rng.Pick ids) "leaf" "y" ]
    | 2 -> RNode.node stem "box" [ RNode.leaf (stem + "a") "leaf" "x"; RNode.leaf (stem + "a") "leaf" "y" ]
    | 3 -> RNode.node stem "leaf" [ RNode.leaf (stem + "a") "leaf" "x" ]
    | 4 -> RNode.leaf stem "odd" "v"
    | 5 -> RNode.node stem "box" [ RNode.leaf (stem + "a") "leaf" "x"; RNode.leaf (stem + "b") "leaf" "y" ]
    | _ -> RNode.leaf stem "leaf" "v"

/// One op against `tree`, refusals as likely as acceptances: unknown ids, the root, a move under
/// itself, a reorder that is not a permutation, an update that changes a kind, and batches.
let rec private genOp (rng: Rng) (depth: int) (tree: RNode) : SkeletonOp<RNode, string> =
    let nodes = Tree.preorder nodew tree
    let ids = nodes |> List.map nodew.Id

    let anyId () =
        if rng.Below 8 = 0 then "absent" else rng.Pick ids

    match rng.Below(if depth > 0 then 6 else 7) with
    | 0 -> InsertChild(anyId (), genGraft rng ids)
    | 1 -> RemoveNode(anyId ())
    | 2 -> MoveNode(anyId (), anyId ())
    | 3 ->
        let p = rng.Pick nodes
        let order = rng.Shuffle(p.Children |> List.map nodew.Id)

        match rng.Below 4 with
        | 0 -> ReorderChildren(anyId (), order)
        | 1 -> ReorderChildren(p.Id, "stray" :: order)
        | _ -> ReorderChildren(p.Id, order)
    | 4
    | 5 ->
        let n = rng.Pick nodes

        UpdateNode
            { n with
                Id = (if rng.Below 10 = 0 then "absent" else n.Id)
                Kind =
                    (match rng.Below 4 with
                     | 0 -> "leaf"
                     | 1 -> "box"
                     | _ -> n.Kind)
                Value = (if rng.Below 3 = 0 then "v" else n.Value + "!")
                Children = [] }
    | _ -> Batch [ for _ in 0 .. rng.Below 3 -> genOp rng (depth + 1) tree ]

let private contained =
    OpScope.contained (fun (n: RNode) -> n.Kind <> "leaf") nodew idw

/// The grammar a box holds anything but `odd`; a leaf holds nothing.
let private grammar: Policy<RNode, string> =
    Policy.grammar (fun kind ->
        match kind with
        | "box" -> Some [ "box"; "leaf" ]
        | "leaf" -> Some []
        | _ -> None)

let private referenced: Policy<RNode, string> =
    Policy.referenced StructuralIntegrityTests.refw

let private cases: (string * Policy<RNode, string> * OpScope<RNode, string>) list =
    [ "plain, no policy", Policy.none, OpScope.plain nodew idw
      "contained, no policy", Policy.none, contained
      "contained, grammar", grammar, contained
      "contained, references", referenced, contained ]

/// The verdict of each pair of entries over one op, and the index the indexed one hands back.
let private agree
    (label: string)
    (policy: Policy<'N, string>)
    (scope: OpScope<'N, string>)
    (op: SkeletonOp<'N, string>)
    (pre: 'N)
    : Result<'N, Rejection<string>> =
    let ix = built (IndexedTree.ofTree scope pre)
    let plain = Ops.apply policy scope op pre
    let indexed = IndexedTree.apply policy op ix

    Expect.equal (indexed |> Result.map _.Tree) plain (sprintf "%s: IndexedTree.apply = Ops.apply for %A" label op)

    Expect.equal
        (IndexedTree.canApply policy op ix)
        (Ops.canApply policy scope op pre)
        (sprintf "%s: IndexedTree.canApply = Ops.canApply for %A" label op)

    match indexed with
    | Ok after ->
        let w =
            match scope.Keyed with
            | Some keyw -> Tree.traversal scope.Nodes keyw
            | None -> scope.Nodes

        match Tree.Index.build w idw after.Tree with
        | Ok rebuilt ->
            Expect.isTrue
                (sameIndex after.Index rebuilt)
                (sprintf "%s: the carried index is the rebuild after %A" label op)
        | Error d -> failtestf "%s: an accepted op left %s twice" label d
    | Error _ -> ()

    plain

[<Tests>]
let tests =
    testList
        "Phase 659 — the indexed apply entries"
        [ testCase
              "IndexedTree.apply and canApply answer what apply and canApply answer, over every op kind and both verdicts"
          <| fun () ->
              let rng = Rng 6591
              let mutable arms: Map<string, int> = Map.empty

              for i in 0..1499 do
                  let pre = genTree rng 14
                  let op = genOp rng 0 pre

                  for label, policy, scope in cases do
                      match agree (sprintf "draw %d, %s" i label) policy scope op pre with
                      | Ok _ -> arms <- bump (kindOf op + " accepted") arms
                      | Error r ->
                          arms <-
                              arms
                              |> bump (kindOf op + " refused")
                              |> bump (sprintf "%A" r |> fun s -> s.Split(' ').[0])

              for k in [ "insert"; "remove"; "move"; "reorder"; "update"; "batch" ] do
                  for verdict in [ "accepted"; "refused" ] do
                      Expect.isGreaterThan
                          (Map.tryFind (k + " " + verdict) arms |> Option.defaultValue 0)
                          20
                          (sprintf "%s %s was drawn: %A" k verdict arms)

              for r in
                  [ "DuplicateId"
                    "UnknownNode"
                    "NotAContainer"
                    "CannotRemoveRoot"
                    "WouldNestUnderSelf"
                    "ReorderMismatch"
                    "IllegalChild"
                    "StillReferenced" ] do
                  Expect.isGreaterThan
                      (Map.tryFind r arms |> Option.defaultValue 0)
                      5
                      (sprintf "%s was reached: %A" r arms)

          testCase
              "under a keyed scope the indexed entries answer what the keyed apply answers, keyed payloads included"
          <| fun () ->
              let rng = Rng 6592
              let mutable arms: Map<string, int> = Map.empty

              let scope =
                  OpScope.keyed ConformanceTests.keyw (fun _ -> true) ConformanceTests.knodew idw

              let k (id: string) (cases: (string * ConformanceTests.KNode) list) : ConformanceTests.KNode =
                  { Id = id
                    Kind = "para"
                    Children = []
                    Cases = cases }

              let mutable seed = ConfRng.ofSeed 6593

              for i in 0..799 do
                  let pre, s' = KeyedApplyTests.deepGen.Tree seed
                  seed <- s'
                  let walk = Tree.traversal ConformanceTests.knodew ConformanceTests.keyw
                  let nodes = Tree.preorder walk pre
                  let ids = nodes |> List.map _.Id

                  let pick () =
                      if rng.Below 10 = 0 then "absent" else rng.Pick ids

                  freshCounter <- freshCounter + 1
                  let fresh = sprintf "g%d" freshCounter

                  let op =
                      match rng.Below 6 with
                      | 0 ->
                          let graftCases =
                              if rng.Below 2 = 0 then
                                  [ "one", k (rng.Pick ids) [] ]
                              else
                                  [ "one", k (fresh + "c") [] ]

                          InsertChild(pick (), k fresh graftCases)
                      | 1 -> RemoveNode(pick ())
                      | 2 -> MoveNode(pick (), pick ())
                      | 3 ->
                          let p = rng.Pick nodes
                          ReorderChildren(p.Id, rng.Shuffle(p.Children |> List.map _.Id))
                      | _ ->
                          // an update whose payload carries keyed subtrees: the target's own keyed
                          // ids (which the rewrite replaces, so admitted), a fresh id, or an id the
                          // tree keeps elsewhere (`DuplicateId`)
                          let target = rng.Pick nodes

                          let reuse =
                              match target.Cases, rng.Below 3 with
                              | (_, c) :: _, 0 -> c.Id
                              | _, 1 -> rng.Pick ids
                              | _ -> fresh + "u"

                          UpdateNode
                              { target with
                                  Cases = [ "one", k reuse [] ] }

                  match agree (sprintf "keyed draw %d" i) Policy.none scope op pre with
                  | Ok _ -> arms <- bump (kindOf op + " accepted") arms
                  | Error r -> arms <- arms |> bump (kindOf op + " refused") |> bump ((sprintf "%A" r).Split(' ').[0])

              for a in
                  [ "insert accepted"
                    "update accepted"
                    "update refused"
                    "remove accepted"
                    "move accepted"
                    "KeyedPosition"
                    "DuplicateId" ] do
                  Expect.isGreaterThan
                      (Map.tryFind a arms |> Option.defaultValue 0)
                      5
                      (sprintf "%s was reached: %A" a arms)

          testCase
              "IndexedTree.applyAll answers what Ops.applyAll answers, and a refusal carries the prefix's indexed tree"
          <| fun () ->
              let rng = Rng 6594
              let mutable refused = 0
              let mutable completed = 0

              for i in 0..599 do
                  let pre = genTree rng 12

                  let ops =
                      // drawn against the tree each prefix reaches, so long scripts are mostly accepted
                      let mutable t = pre

                      [ for _ in 0 .. rng.Below 8 do
                            let op = genOp rng 1 t

                            match Ops.apply Policy.none contained op t with
                            | Ok next -> t <- next
                            | Error _ -> ()

                            yield op ]

                  let ix = built (IndexedTree.ofTree contained pre)

                  match Ops.applyAll Policy.none contained ops pre, IndexedTree.applyAll Policy.none ops ix with
                  | Ok a, Ok b ->
                      completed <- completed + 1
                      Expect.equal b.Tree a (sprintf "script %d: the same tree" i)

                      Expect.isTrue
                          (sameIndex b.Index (Result.defaultWith failwith (Tree.Index.build nodew idw a)))
                          "rebuilt"
                  | Error a, Error b ->
                      refused <- refused + 1

                      Expect.equal
                          (b.Applied, b.Rejection, b.Tree.Tree)
                          (a.Applied, a.Rejection, a.Tree)
                          (sprintf "script %d" i)

                      Expect.isTrue
                          (sameIndex b.Tree.Index (Result.defaultWith failwith (Tree.Index.build nodew idw a.Tree)))
                          (sprintf "script %d: the prefix's index" i)
                  | a, b ->
                      failtestf
                          "script %d: Ops.applyAll %A, IndexedTree.applyAll %A"
                          i
                          (Result.map ignore a)
                          (Result.map ignore b)

              Expect.isGreaterThan refused 50 "scripts were refused part-way"
              Expect.isGreaterThan completed 50 "scripts completed"

          testCase "a base carrying an id twice: IndexedTree.ofTree refuses it with the id apply refuses it with"
          <| fun () ->
              let twice =
                  RNode.node
                      "root"
                      "box"
                      [ RNode.leaf "a" "leaf" "v"
                        RNode.node "b" "box" [ RNode.leaf "a" "leaf" "w" ] ]

              let scope = OpScope.plain nodew idw

              Expect.equal
                  (Ops.apply Policy.none scope (RemoveNode "b") twice)
                  (Error(DuplicateId "a"))
                  "apply refuses the base"

              match IndexedTree.ofTree scope twice with
              | Error(IndexRefusal.RepeatedId d) -> Expect.equal d "a" "the same id"
              | other -> failtestf "IndexedTree.ofTree made %A" (Result.map ignore other)

          testCase "IndexedTree.adopt: a fresh index is adopted; a stale one is the typed refusal Stale, never read"
          <| fun () ->
              let scope = OpScope.plain nodew idw
              let t = sample ()
              let ix = Result.defaultWith failwith (Tree.Index.build nodew idw t)

              // fresh: adopted, and the indexed entry then answers what apply answers
              match IndexedTree.adopt scope encNode t ix with
              | Ok adopted ->
                  let op = InsertChild("b", RNode.leaf "b2" "para" "w")

                  Expect.equal
                      (IndexedTree.apply Policy.none op adopted |> Result.map _.Tree)
                      (Ops.apply Policy.none scope op t)
                      "the adopted index answers what apply answers"
              | Error e -> failtestf "a fresh index was refused: %A" e

              // stale three ways: a structural edit, a content edit, a tree the index was never built for
              let moved =
                  Ops.apply Policy.none scope (MoveNode("a1", "b")) t
                  |> Result.defaultWith (failwithf "%A")

              let revalued =
                  Ops.apply Policy.none scope (UpdateNode(RNode.leaf "a1" "para" "changed")) t
                  |> Result.defaultWith (failwithf "%A")

              for label, tree in
                  [ "moved", moved
                    "revalued", revalued
                    "another tree", RNode.leaf "root" "doc" "" ] do
                  match IndexedTree.adopt scope encNode tree ix with
                  | Error IndexRefusal.Stale -> ()
                  | other -> failtestf "%s: a stale index was not refused: %A" label (Result.map ignore other)

          testCase "a progressive build costs each insert the op: the children it reads do not grow with the tree"
          <| fun () ->
              // The witness counts how often a node's children are read — the measure of a walk,
              // deterministic where a clock is not. The inserts happen under `work`; `ballast` only grows.
              let calls = ref 0

              let counted: NodeWitness<RNode, string> =
                  { nodew with
                      Children =
                          fun n ->
                              calls.Value <- calls.Value + 1
                              n.Children }

              let scope = OpScope.plain counted idw

              let tree (ballast: int) =
                  RNode.node
                      "root"
                      "box"
                      [ RNode.node "work" "box" []
                        RNode.node
                            "ballast"
                            "box"
                            [ for k in 0 .. ballast - 1 ->
                                  RNode.node (sprintf "b%d" k) "box" [ RNode.leaf (sprintf "b%d-x" k) "leaf" "" ] ] ]

              let build (ballast: int) (indexedEntry: bool) =
                  let ops =
                      [ for k in 0..9 ->
                            InsertChild(
                                "work",
                                RNode.node (sprintf "w%d" k) "box" [ RNode.leaf (sprintf "w%d-x" k) "leaf" "" ]
                            ) ]

                  if indexedEntry then
                      let start = built (IndexedTree.ofTree scope (tree ballast))
                      calls.Value <- 0

                      let final =
                          (start, ops)
                          ||> List.fold (fun t op ->
                              IndexedTree.apply Policy.none op t |> Result.defaultWith (failwithf "%A"))

                      Expect.isTrue
                          (sameIndex final.Index (Result.defaultWith failwith (Tree.Index.build nodew idw final.Tree)))
                          "the carried index is the rebuild"
                  else
                      let start = tree ballast
                      calls.Value <- 0

                      (start, ops)
                      ||> List.fold (fun t op ->
                          Ops.apply Policy.none scope op t |> Result.defaultWith (failwithf "%A"))
                      |> ignore

                  calls.Value

              let small, large = 10, 400
              Expect.equal (build large true) (build small true) "the indexed build does not see the ballast"
              // the bound bites: the walking entry reads the ballast on every op
              Expect.isGreaterThan (build large false) (build small false + 10 * 2 * (large - small)) "apply walks it" ]
