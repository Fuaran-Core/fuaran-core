module Fuaran.Core.Tests.PlacementTests

// Phase 312 — the placement algebra (`TreePlacement`), tree lowering (`Ops.lower` /
// `skeletonRoot` / `shellOf`) and fresh ids (`FreshIds`).
//
// Three kinds of test, each answering a different question:
//
//   * the KIT families (`placementLaws`, `loweringLaws`, `freshIdLaws`) green at the reference
//     witness, with a go-red per family showing each has teeth;
//   * worked scripts on a fixed fixture, so the exact lowering — which op, which order — is pinned
//     rather than only its effect;
//   * the ACCEPTANCE: the two shapes consumers re-derived — a host's anchor-relative placement verbs
//     (append-then-reorder, the reorder leg dropped when appending already gives the order) and a
//     document domain's index-bearing insert and move (`List.insertAt` into the post-removal child
//     list, refused out of `0 .. count`) — are re-stated HERE as small reference functions over the
//     same fixture, and the Core lowering is held to them: the same script for the anchor-relative
//     verbs, the same tree or the same refusal for every index the index-bearing ops can be handed.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

let private leaf id = RNode.leaf id "para" "v"
let private sec id kids = RNode.node id "section" kids
let private setId (i: string) (n: RNode) = { n with Id = i }
let private canHold (n: RNode) = n.Kind <> "para"

/// root ─ a(a1, a2) ─ b ─ c(c1)
let private fixture () =
    sec "root" [ sec "a" [ leaf "a1"; leaf "a2" ]; leaf "b"; sec "c" [ leaf "c1" ] ]

let private kidsOf (id: string) (t: RNode) =
    Tree.tryFind nodew idw id t
    |> Option.map (fun n -> n.Children |> List.map _.Id)
    |> Option.defaultValue []

let private applied (script: SkeletonOp<RNode, string> list) (t: RNode) =
    match Ops.applyAll nodew idw script t with
    | Ok t' -> t'
    | Error(i, e, _) -> failtestf "the script %A was refused at step %d: %A" script i e

let private okScript (r: Result<SkeletonOp<RNode, string> list, PlaceError<string>>) =
    match r with
    | Ok s -> s
    | Error e -> failtestf "expected a script, got %A" e

// ---------------------------------------------------------------------------
//  the reference re-statements the acceptance holds Core to
// ---------------------------------------------------------------------------

/// A host's anchor-relative verbs, as shipped before this phase: compute the post-op membership,
/// reposition the node among the rest (an unknown anchor refused), and emit the bare op when
/// appending already gives that order, else `Batch [op; ReorderChildren]`.
module private AnchorRelative =

    let reposition (order: string list) (moved: string) (anchor: Anchor<string>) : string list option =
        let rest = order |> List.filter (fun id -> id <> moved)

        let anchored (a: string) offset =
            rest
            |> List.tryFindIndex (fun id -> id = a)
            |> Option.map (fun i -> (List.truncate (i + offset) rest) @ [ moved ] @ (List.skip (i + offset) rest))

        match anchor with
        | Anchor.Last -> Some(rest @ [ moved ])
        | Anchor.First -> Some(moved :: rest)
        | Anchor.Before a -> anchored a 0
        | Anchor.After a -> anchored a 1
        | Anchor.Index _ -> None

    let placeOp (root: RNode) (child: RNode) (parent: string) (anchor: Anchor<string>) =
        let siblings = kidsOf parent root
        let appended = siblings @ [ child.Id ]

        reposition appended child.Id anchor
        |> Option.map (fun wanted ->
            let insert = InsertChild(parent, child)

            if wanted = appended then
                [ insert ]
            else
                [ Batch [ insert; ReorderChildren(parent, wanted) ] ])

    let moveOp (root: RNode) (moved: string) (parent: string) (anchor: Anchor<string>) =
        let siblings = kidsOf parent root
        let appended = (siblings |> List.filter (fun id -> id <> moved)) @ [ moved ]

        reposition appended moved anchor
        |> Option.map (fun wanted ->
            let move = MoveNode(moved, parent)

            if wanted = appended then
                [ move ]
            else
                [ Batch [ move; ReorderChildren(parent, wanted) ] ])

/// A document domain's index-bearing ops, as shipped before this phase: insert at an index into the
/// parent's children, and move by removing first and then inserting at an index into the NEW
/// parent's post-removal children; an index outside `0 .. count` is refused naming
/// `(parent, index, count)`.
module private IndexBearing =

    type Outcome =
        | Tree of RNode
        | OutOfRange of parent: string * index: int * count: int
        | Other

    let private withKids (parent: string) (f: RNode list -> RNode list) (t: RNode) =
        Tree.updateNode nodew idw parent (fun p -> { p with Children = f p.Children }) t

    let insertAt (parent: string) (index: int) (node: RNode) (t: RNode) : Outcome =
        match Tree.tryFind nodew idw parent t with
        | None -> Other
        | Some p ->
            let count = List.length p.Children

            if index < 0 || index > count then
                OutOfRange(parent, index, count)
            else
                withKids parent (List.insertAt index node) t
                |> Option.map Tree
                |> Option.defaultValue Other

    let moveAt (target: string) (parent: string) (index: int) (t: RNode) : Outcome =
        match Tree.tryFind nodew idw target t, Tree.parentOf nodew idw target t with
        | Some moving, Some source ->
            match withKids source.Id (List.filter (fun c -> c.Id <> target)) t with
            | None -> Other
            | Some removed ->
                match Tree.tryFind nodew idw parent removed with
                | None -> Other
                | Some p ->
                    let count = List.length p.Children

                    if index < 0 || index > count then
                        OutOfRange(parent, index, count)
                    else
                        withKids parent (List.insertAt index moving) removed
                        |> Option.map Tree
                        |> Option.defaultValue Other
        | _ -> Other

    /// What Core's lowering answers for the same request, in the same vocabulary.
    let ofCore (t: RNode) (r: Result<SkeletonOp<RNode, string> list, PlaceError<string>>) : Outcome =
        match r with
        | Ok script ->
            match Ops.applyAll nodew idw script t with
            | Ok t' -> Tree t'
            | Error _ -> Other
        | Error(PlaceError.IndexOutOfRange(p, i, n)) -> OutOfRange(p, i, n)
        | Error _ -> Other

let private anchorsOver (others: string list) : Anchor<string> list =
    [ Anchor.First; Anchor.Last ]
    @ (others |> List.collect (fun o -> [ Anchor.Before o; Anchor.After o ]))

[<Tests>]
let tests =
    testList
        "Phase 312 — placement, lowering and fresh ids"
        [

          // ---- the kit families at the reference witness, and their go-reds ----

          testCase "placementLaws hold at the reference witness, plain and contained"
          <| fun () ->
              for gen in [ ConformanceTests.opGen; ConformanceTests.containedGen ] do
                  for r in Conformance.placementLaws nodew idw gen 312 200 do
                      Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

          testCase "loweringLaws hold at the reference witness, plain and contained"
          <| fun () ->
              for gen in [ ConformanceTests.opGen; ConformanceTests.containedGen ] do
                  for r in Conformance.loweringLaws nodew idw gen 312 200 do
                      Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

          testCase "freshIdLaws hold for both shipped strategies"
          <| fun () ->
              for mint in [ FreshIds.derived idw; FreshIds.sequential idw "n" ] do
                  for r in Conformance.freshIdLaws nodew idw ConformanceTests.containedGen setId mint 312 200 do
                      Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

          testCase "freshIdLaws go red on a strategy that mints a taken id, and on a setId that drops the children"
          <| fun () ->
              let lazyMint (i: string) (_: Set<string>) = i

              let red =
                  Conformance.freshIdLaws nodew idw ConformanceTests.opGen setId lazyMint 312 100
                  |> List.filter (fun r -> not r.Passed)
                  |> List.map _.Law

              Expect.contains red "a minted id's key is absent from the taken set it was minted against" "taken id"

              let lossySetId (i: string) (n: RNode) = { n with Id = i; Children = [] }

              let red2 =
                  Conformance.freshIdLaws nodew idw ConformanceTests.opGen lossySetId (FreshIds.derived idw) 312 100
                  |> List.filter (fun r -> not r.Passed)
                  |> List.map _.Law

              Expect.contains red2 "setId sets the id and keeps the kind and the children" "lossy setId"

          testCase "loweringLaws and placementLaws go red under a witness whose rebuild reverses the children"
          <| fun () ->
              // A witness that stores what it is handed reversed: appending is then prepending, so
              // every rebuilt order is wrong — the round trip and the landing law must both see it.
              let rev: NodeWitness<RNode, string> =
                  { nodew with
                      ReplaceChildren = fun n cs -> { n with Children = List.rev cs } }

              let failed (rs: LawResult list) =
                  rs |> List.exists (fun r -> not r.Passed)

              Expect.isTrue (failed (Conformance.loweringLaws rev idw ConformanceTests.opGen 312 100)) "lowering"
              Expect.isTrue (failed (Conformance.placementLaws rev idw ConformanceTests.opGen 312 100)) "placement"

          // ---- worked scripts on the fixture ----

          testCase "place lowers to the bare insert when last, else to one insert plus one reorder"
          <| fun () ->
              let t = fixture ()
              let n = leaf "n"

              Expect.equal
                  (okScript (TreePlacement.place nodew idw "root" Anchor.Last n t))
                  [ InsertChild("root", n) ]
                  "last"

              Expect.equal
                  (okScript (TreePlacement.place nodew idw "root" (Anchor.Before "b") n t))
                  [ Batch [ InsertChild("root", n); ReorderChildren("root", [ "a"; "n"; "b"; "c" ]) ] ]
                  "before b"

              Expect.equal
                  (okScript (TreePlacement.place nodew idw "root" (Anchor.Index 3) n t))
                  [ InsertChild("root", n) ]
                  "index = count is last"

              Expect.equal
                  (kidsOf "root" (applied (okScript (TreePlacement.place nodew idw "root" Anchor.First n t)) t))
                  [ "n"; "a"; "b"; "c" ]
                  "first"

          testCase "place refuses by name: an unknown anchor, an out-of-range index, and the engine's own refusals"
          <| fun () ->
              let t = fixture ()
              let n = leaf "n"

              Expect.equal
                  (TreePlacement.place nodew idw "root" (Anchor.After "a1") n t)
                  (Error(PlaceError.UnknownAnchor("root", "a1", [ "a"; "b"; "c" ])))
                  "a grandchild is not a sibling"

              Expect.equal
                  (TreePlacement.place nodew idw "a" (Anchor.Index 3) n t)
                  (Error(PlaceError.IndexOutOfRange("a", 3, 2)))
                  "past the end"

              Expect.equal
                  (TreePlacement.place nodew idw "a" (Anchor.Index(-1)) n t)
                  (Error(PlaceError.IndexOutOfRange("a", -1, 2)))
                  "before the start"

              Expect.equal
                  (TreePlacement.place nodew idw "zz" Anchor.Last n t)
                  (Error(PlaceError.Refused(UnknownNode("zz", Tree.ids nodew t))))
                  "absent parent"

              Expect.equal
                  (TreePlacement.place nodew idw "root" Anchor.Last (leaf "b") t)
                  (Error(PlaceError.Refused(DuplicateId "b")))
                  "duplicate id"

              Expect.equal
                  (TreePlacement.placeContained canHold nodew idw "b" Anchor.Last n t)
                  (Error(PlaceError.Refused(NotAContainer("b", "para"))))
                  "a leaf cannot hold the node"

          testCase "move: across parents is MoveNode (+ reorder); within a parent it is a bare reorder, or nothing"
          <| fun () ->
              let t = fixture ()

              Expect.equal
                  (okScript (TreePlacement.move nodew idw "b" "a" Anchor.Last t))
                  [ MoveNode("b", "a") ]
                  "across, last"

              Expect.equal
                  (okScript (TreePlacement.move nodew idw "b" "a" (Anchor.After "a1") t))
                  [ Batch [ MoveNode("b", "a"); ReorderChildren("a", [ "a1"; "b"; "a2" ]) ] ]
                  "across, between"

              Expect.equal
                  (okScript (TreePlacement.move nodew idw "c" "root" Anchor.First t))
                  [ ReorderChildren("root", [ "c"; "a"; "b" ]) ]
                  "within"

              Expect.equal (okScript (TreePlacement.move nodew idw "b" "root" (Anchor.Index 1) t)) [] "already there"

              Expect.equal
                  (TreePlacement.move nodew idw "a" "a1" Anchor.Last t)
                  (Error(PlaceError.Refused(WouldNestUnderSelf("a", NestRelation.Descendant))))
                  "into its own subtree"

              Expect.equal
                  (TreePlacement.move nodew idw "b" "root" (Anchor.Before "b") t)
                  (Error(PlaceError.UnknownAnchor("root", "b", [ "a"; "c" ])))
                  "a node is not its own anchor"

          testCase "clone copies the subtree under fresh derived ids and places the copy"
          <| fun () ->
              let t = fixture ()

              let script =
                  okScript (TreePlacement.clone nodew idw setId (FreshIds.derived idw) "a" "c" Anchor.First t)

              let t' = applied script t
              Expect.equal (kidsOf "c" t') [ "a-copy"; "c1" ] "the copy leads c"
              Expect.equal (kidsOf "a-copy" t') [ "a1-copy"; "a2-copy" ] "the copy's own ids"
              Expect.equal (kidsOf "a" t') [ "a1"; "a2" ] "the source is untouched"
              Expect.isTrue (Tree.isWellFormed nodew idw t') "well-formed"

              let twice =
                  okScript (TreePlacement.clone nodew idw setId (FreshIds.derived idw) "a" "c" Anchor.Last t')

              Expect.equal (kidsOf "c" (applied twice t')) [ "a-copy"; "c1"; "a-copy-2" ] "the second copy probes"

          // ---- the acceptance: consumers' shapes expressed over TreePlacement ----

          testCase "the anchor-relative placed insert IS TreePlacement.place, script for script"
          <| fun () ->
              let t = fixture ()
              let n = leaf "n"

              for parent in [ "root"; "a"; "c" ] do
                  for anchor in anchorsOver (kidsOf parent t) do
                      let theirs = AnchorRelative.placeOp t n parent anchor |> Option.get
                      let ours = okScript (TreePlacement.place nodew idw parent anchor n t)
                      Expect.equal ours theirs (sprintf "place under %s at %A" parent anchor)

          testCase
              "the anchor-relative move IS TreePlacement.move: the same script across parents, the same tree within one"
          <| fun () ->
              let t = fixture ()

              for target in [ "a1"; "a2"; "b"; "c"; "c1" ] do
                  for parent in [ "root"; "a"; "c" ] do
                      if
                          not (List.contains parent (Tree.ids nodew (Tree.tryFind nodew idw target t |> Option.get)))
                      then
                          let others = kidsOf parent t |> List.filter (fun i -> i <> target)

                          for anchor in anchorsOver others do
                              let theirs = AnchorRelative.moveOp t target parent anchor |> Option.get
                              let ours = okScript (TreePlacement.move nodew idw target parent anchor t)
                              let within = List.contains target (kidsOf parent t)

                              if not within then
                                  Expect.equal ours theirs (sprintf "move %s under %s at %A" target parent anchor)

                              Expect.equal
                                  (applied ours t)
                                  (applied theirs t)
                                  (sprintf "the tree: move %s under %s at %A" target parent anchor)

          testCase
              "the index-bearing insert and move ARE TreePlacement at Anchor.Index, for every index either side of the range"
          <| fun () ->
              let t = fixture ()
              let n = leaf "n"

              for parent in [ "root"; "a"; "c" ] do
                  for i in -1 .. List.length (kidsOf parent t) + 1 do
                      Expect.equal
                          (IndexBearing.ofCore t (TreePlacement.place nodew idw parent (Anchor.Index i) n t))
                          (IndexBearing.insertAt parent i n t)
                          (sprintf "insert under %s at %d" parent i)

              for target in [ "a1"; "a2"; "b"; "c"; "c1" ] do
                  for parent in [ "root"; "a"; "c" ] do
                      if
                          not (List.contains parent (Tree.ids nodew (Tree.tryFind nodew idw target t |> Option.get)))
                      then
                          let others = kidsOf parent t |> List.filter (fun x -> x <> target)

                          for i in -1 .. List.length others + 1 do
                              Expect.equal
                                  (IndexBearing.ofCore t (TreePlacement.move nodew idw target parent (Anchor.Index i) t))
                                  (IndexBearing.moveAt target parent i t)
                                  (sprintf "move %s under %s at %d" target parent i)

          // ---- lowering ----

          testCase "lower emits each child's shell in preorder and rebuilds the fixture from its skeleton"
          <| fun () ->
              let t = fixture ()
              let script = Ops.lower nodew t

              Expect.equal
                  script
                  [ InsertChild("root", sec "a" [])
                    InsertChild("a", leaf "a1")
                    InsertChild("a", leaf "a2")
                    InsertChild("root", leaf "b")
                    InsertChild("root", sec "c" [])
                    InsertChild("c", leaf "c1") ]
                  "the script"

              Expect.equal (Ops.skeletonRoot nodew t) (sec "root" []) "the skeleton"
              Expect.equal (Ops.applyAll nodew idw script (Ops.skeletonRoot nodew t)) (Ok t) "round trip"
              Expect.equal (Ops.applyAllWith canHold nodew idw script (Ops.skeletonRoot nodew t)) (Ok t) "contained"

          testCase "under canHold, a tree outside the containment invariant is refused at its first offender"
          <| fun () ->
              let t = sec "root" [ RNode.node "p" "para" [ leaf "x" ]; leaf "y" ]
              let script = Ops.lower nodew t

              match Ops.applyAllWith canHold nodew idw script (Ops.skeletonRoot nodew t) with
              | Error(1, NotAContainer("p", "para"), _) -> ()
              | other -> failtestf "expected NotAContainer at step 1, got %A" other

          testCase "lower and repairDuplicates are stack-safe on a deep tree"
          <| fun () ->
              let depth = 20000

              let rec chain i acc =
                  if i < 0 then
                      acc
                  else
                      chain (i - 1) (sec (sprintf "d%d" i) [ acc ])

              let deep = chain (depth - 1) (leaf "bottom")
              Expect.equal (List.length (Ops.lower nodew deep)) depth "one insert per node below the root"
              let doubled = sec "top" [ deep; leaf "bottom" ]

              let fixedTree, mapping =
                  FreshIds.repairDuplicates nodew idw setId (FreshIds.derived idw) Set.empty doubled

              Expect.equal mapping [ "bottom", "bottom-copy" ] "the later occurrence"
              Expect.isTrue (Tree.isWellFormed nodew idw fixedTree) "well-formed"

          // ---- fresh ids ----

          testCase "derived and sequential probe past the taken set, and read no ambient state"
          <| fun () ->
              let taken = set [ "a-copy"; "a-copy-2"; "n-1" ]
              Expect.equal (FreshIds.derived idw "a" taken) "a-copy-3" "derived"
              Expect.equal (FreshIds.derived idw "a" Set.empty) "a-copy" "derived, nothing taken"
              Expect.equal (FreshIds.sequential idw "n" "ignored" taken) "n-2" "sequential"
              Expect.equal (FreshIds.sequential idw "n" "ignored" taken) "n-2" "no hidden counter"

          testCase "repairDuplicates renames the later occurrence and every taken id, and returns the mapping"
          <| fun () ->
              let t = sec "root" [ leaf "x"; sec "s" [ leaf "x"; leaf "y" ]; leaf "x" ]

              let fixedTree, mapping =
                  FreshIds.repairDuplicates nodew idw setId (FreshIds.derived idw) Set.empty t

              Expect.equal (Tree.ids nodew fixedTree) [ "root"; "x"; "s"; "x-copy"; "y"; "x-copy-2" ] "ids"
              Expect.equal mapping [ "x", "x-copy"; "x", "x-copy-2" ] "mapping, in preorder"

              let pasted, m2 =
                  FreshIds.repairDuplicates
                      nodew
                      idw
                      setId
                      (FreshIds.sequential idw "p")
                      (set [ "y" ])
                      (sec "s" [ leaf "y" ])

              Expect.equal (Tree.ids nodew pasted) [ "s"; "p-1" ] "a taken id is renamed even at its first occurrence"
              Expect.equal m2 [ "y", "p-1" ] "its mapping"

              let clean = fixture ()

              Expect.equal
                  (FreshIds.repairDuplicates nodew idw setId (FreshIds.derived idw) Set.empty clean)
                  (clean, [])
                  "identity" ]
