module Fuaran.Core.Tests.ContentDiffTests

// Phase 305 — the content-aware diff and the closed undo, measured on the shipped engine.
//
// The second-pass review found `Diff.toOps` under `OpScope.contained` reading `after`'s kinds for its container check
// while the script it returns runs against `before`'s kinds: on `before = root(p:para)`, `after =
// root(p:section(q:para))` under `canHold = kind <> "para"` it answered `Ok [InsertChild(p, q)]` and
// `Ops.applyAll` under `OpScope.contained` refused that script with `NotAContainer(p, para)`. The content-aware form emits an
// `UpdateNode` for every survivor whose content changed, placed by DECISIONS D103: a survivor whose
// new node `canHold` accepts is rewritten FIRST, every other one LAST. The bridges below measure the
// rule over independent pairs with kinds and a predicate drawn freely, keep the shipped structural
// script equal to the one the model proves things about, and hold `normalize`, `invertAll`,
// `arbitrate` and `Tree.Index` to the laws Phase 305 states for them.

open System
open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

/// An index of a tree the test knows is well-formed (`Tree.Index.build` refuses a repeated id since
/// Phase 518).
let private ok (r: Result<'a, string>) : 'a =
    match r with
    | Ok x -> x
    | Error d -> failwithf "a well-formed tree indexes, but %s repeats" d

// ---- a seeded generator over the reference node ------------------------------------------------

let private kinds = [ "section"; "para"; "aside"; "list" ]
let private values = [ "a"; "b"; "c" ]

/// A node's own content, as an encoder reads it: kind and value, never the children.
let private encode (n: RNode) = n.Kind + "|" + n.Value

let private withValue (v: string) (n: RNode) = { n with Value = v }
let private withKind (k: string) (n: RNode) = { n with Kind = k }

/// A random tree over `pool`: the root (`root`, kind `doc`) plus each id of a random subset of the
/// pool attached under a random node already placed, with a random kind and value. Ids are unique by
/// construction, so the tree is well-formed.
let drawTree (rng: Random) (pool: string list) : RNode =
    let chosen = pool |> List.filter (fun _ -> rng.Next(3) > 0)
    // parent id -> children (in insertion order), built bottom-up at the end
    let parentOf = Collections.Generic.Dictionary<string, string>()
    let placed = ResizeArray<string>([ "root" ])

    for id in chosen do
        parentOf[id] <- placed[rng.Next(placed.Count)]
        placed.Add id

    let kindOf =
        chosen |> List.map (fun id -> id, kinds[rng.Next(kinds.Length)]) |> Map.ofList

    let valueOf =
        chosen |> List.map (fun id -> id, values[rng.Next(values.Length)]) |> Map.ofList

    let rec build (id: string) : RNode =
        let children = chosen |> List.filter (fun c -> parentOf[c] = id) |> List.map build

        if id = "root" then
            RNode.node "root" "doc" children
        else
            let n = RNode.node id kindOf[id] children
            { n with Value = valueOf[id] }

    build "root"

let pool = [ for i in 1..10 -> sprintf "n%d" i ]

/// A drawn container predicate: `doc` always holds, each other kind holds by a coin.
let private drawCanHold (rng: Random) : RNode -> bool =
    let holding = kinds |> List.filter (fun _ -> rng.Next(2) = 0) |> Set.ofList
    fun (n: RNode) -> n.Kind = "doc" || holding.Contains n.Kind

let private isUpdate =
    function
    | UpdateNode _ -> true
    | _ -> false

let private applyAllWithOk canHold ops before =
    match Ops.applyAll Policy.none (OpScope.contained canHold nodew idw) ops before with
    | Ok t -> Ok t
    | Error { Applied = i; Rejection = e } -> Error(i, e)

// ---- the shard's pair ----------------------------------------------------------------------------

let private paraP = RNode.node "p" "para" []
let private shardBefore = RNode.node "root" "doc" [ paraP ]

let private shardAfter =
    RNode.node "root" "doc" [ RNode.node "p" "section" [ RNode.node "q" "para" [] ] ]

let private notPara (n: RNode) = n.Kind <> "para"

// ---- an applyable script with adjacent-collapse bias ---------------------------------------------

/// A random script every op of which applies in sequence to `tree`, biased so that an accepted op is
/// often followed by the op that collapses with it under `normalize` — an insert by its remove or a
/// rewrite of the inserted node, a move by another move of the same target, a reorder by another on
/// the same parent, a rewrite by another or by the node's remove. Returns the script and the tree it
/// reaches.
let drawScript (rng: Random) (tree: RNode) (length: int) : SkeletonOp<RNode, string> list * RNode =
    let ops = ResizeArray<SkeletonOp<RNode, string>>()
    let cur = ref tree
    let fresh = ref 0

    let pick (xs: 'a list) = xs[rng.Next(xs.Length)]

    let tryAdd (op: SkeletonOp<RNode, string>) =
        match Ops.apply Policy.none (OpScope.plain nodew idw) op cur.Value with
        | Ok t ->
            ops.Add op
            cur.Value <- t
            true
        | Error _ -> false

    let randomOp () =
        let nodes = Tree.preorder nodew cur.Value
        let n = pick nodes

        match rng.Next(5) with
        | 0 ->
            fresh.Value <- fresh.Value + 1
            let shell = RNode.node (sprintf "f%d" fresh.Value) (pick kinds) []
            InsertChild(n.Id, { shell with Value = pick values })
        | 1 -> RemoveNode n.Id
        | 2 -> MoveNode(n.Id, (pick nodes).Id)
        | 3 -> ReorderChildren(n.Id, n.Children |> List.map _.Id |> List.sortBy (fun _ -> rng.Next()))
        | _ -> UpdateNode(n |> withValue (pick values) |> withKind (pick kinds))

    let followUp (op: SkeletonOp<RNode, string>) =
        let nodes () = Tree.preorder nodew cur.Value

        match op with
        | InsertChild(_, n) ->
            if rng.Next(2) = 0 then
                RemoveNode n.Id
            else
                UpdateNode(n |> withValue (pick values))
        | MoveNode(t, _) -> MoveNode(t, (pick (nodes ())).Id)
        | ReorderChildren(p, order) -> ReorderChildren(p, order |> List.sortBy (fun _ -> rng.Next()))
        | UpdateNode n ->
            if rng.Next(2) = 0 then
                UpdateNode(n |> withValue (pick values))
            else
                RemoveNode n.Id
        | RemoveNode _
        | Batch _ -> randomOp ()

    while ops.Count < length do
        let op = randomOp ()

        if tryAdd op && rng.Next(10) < 6 then
            tryAdd (followUp op) |> ignore

    List.ofSeq ops, cur.Value

let private treeOf (ops: SkeletonOp<RNode, string> list) (tree: RNode) =
    match Ops.applyAll Policy.none (OpScope.plain nodew idw) ops tree with
    | Ok t -> t
    | Error { Applied = i; Rejection = e } -> failtestf "a drawn script did not apply at %d: %A" i e

/// A long flat script of distinct inserts under the root — nothing in it collapses.
let private flatInserts (count: int) : SkeletonOp<RNode, string> list =
    [ for i in 1..count -> InsertChild("root", RNode.node (sprintf "x%d" i) "para" []) ]

// ---- arbitration ---------------------------------------------------------------------------------

let private shuffled (rng: Random) (xs: 'a list) = xs |> List.sortBy (fun _ -> rng.Next())

[<Tests>]
let tests =
    testList
        "Phase 305 — content-aware diff and closed undo"
        [ testList
              "Diff"
              [ testCase
                    "the shard's pair: the structural contained diff is refused on the way back in, the content-aware one applies"
                <| fun () ->
                    let structural =
                        match Diff.toOps Policy.none (OpScope.contained notPara nodew idw) shardBefore shardAfter with
                        | Ok ops -> ops
                        | Error e -> failtestf "toOpsContained refused the pair: %A" e

                    Expect.equal
                        structural
                        [ InsertChild("p", RNode.node "q" "para" []) ]
                        "the structural script is the one insert, against `before`'s para"

                    match applyAllWithOk notPara structural shardBefore with
                    | Error(0, NotAContainer("p", "para")) -> ()
                    | other -> failtestf "the structural script's refusal moved: %A — re-read D103" other

                    let aware =
                        match
                            Diff.toOpsWith
                                encode
                                Policy.none
                                (OpScope.contained notPara nodew idw)
                                shardBefore
                                shardAfter
                        with
                        | Ok ops -> ops
                        | Error e -> failtestf "toOpsContainedWith refused the pair: %A" e

                    match aware with
                    | [ UpdateNode p; InsertChild("p", q) ] ->
                        Expect.equal p.Kind "section" "the rewrite carries the after content"
                        Expect.equal q.Id "q" "then the insert"
                    | other -> failtestf "unexpected content-aware script: %A" other

                    Expect.equal
                        (applyAllWithOk notPara aware shardBefore)
                        (Ok shardAfter)
                        "the content-aware script applies under the predicate it checked and lands on `after`"

                testCase
                    "content-aware bridge: over drawn kinds and a drawn canHold, applyAllWith refuses no script and every one lands on after"
                <| fun () ->
                    let rng = Random 305
                    let mutable asked = 0
                    let mutable structuralRefused = 0
                    let mutable appendedLastRefused = 0
                    let mutable contentChanging = 0
                    let refusals = ResizeArray<string>()

                    for _ in 1..6000 do
                        let before = drawTree rng pool
                        let after = drawTree rng pool
                        let canHold = drawCanHold rng

                        match Diff.toOpsWith encode Policy.none (OpScope.contained canHold nodew idw) before after with
                        | Error(NotAContainer _) -> () // `after` nests under a non-container: no legal script
                        | Error e -> failtestf "refused a well-formed pair with %A" e
                        | Ok ops ->
                            asked <- asked + 1

                            if ops |> List.exists isUpdate then
                                contentChanging <- contentChanging + 1

                            match applyAllWithOk canHold ops before with
                            | Ok t when t = after -> ()
                            | Ok t -> refusals.Add(sprintf "landed on %A, not after %A" t after)
                            | Error(i, e) ->
                                refusals.Add(sprintf "refused at %d: %A\n  before %A\n  after %A" i e before after)

                            // the two falsifiers: the structural script, and the content-aware
                            // script with every rewrite appended LAST — each is refused on a
                            // measurable share of the same pairs, which is what the placement
                            // rule is for
                            match Diff.toOps Policy.none (OpScope.contained canHold nodew idw) before after with
                            | Ok structural ->
                                match applyAllWithOk canHold structural before with
                                | Error _ -> structuralRefused <- structuralRefused + 1
                                | Ok _ -> ()
                            | Error _ -> ()

                            let appendedLast =
                                (ops |> List.filter (isUpdate >> not)) @ (ops |> List.filter isUpdate)

                            match applyAllWithOk canHold appendedLast before with
                            | Error _ -> appendedLastRefused <- appendedLastRefused + 1
                            | Ok _ -> ()

                    printfn
                        "Phase 305 bridge: %d pairs asked, %d content-changing, %d content-aware refused, %d structural refused, %d appended-last refused"
                        asked
                        contentChanging
                        refusals.Count
                        structuralRefused
                        appendedLastRefused

                    Expect.isGreaterThan asked 1000 "enough pairs were asked"
                    Expect.isGreaterThan contentChanging 500 "enough pairs carried a content change"

                    Expect.isEmpty
                        (List.ofSeq refusals |> List.truncate 3)
                        (sprintf "%d of %d content-aware scripts were refused or missed `after`" refusals.Count asked)

                    Expect.isGreaterThan
                        structuralRefused
                        0
                        "the premise: the structural contained script IS refused on some pairs (none refused means the finding that filed Phase 305 no longer reproduces)"

                    Expect.isGreaterThan
                        appendedLastRefused
                        0
                        "the placement rule is load-bearing: appending every rewrite last is still refused on some pairs"

                testCase "the structural part of the content-aware script is exactly the structural diff"
                <| fun () ->
                    // the bridge to the proved model: `TreeDiff.fst` models `toOps` / `Diff.toOps` under `OpScope.contained`,
                    // and the content-aware form adds rewrites around the same four blocks
                    let rng = Random 1305

                    for _ in 1..1000 do
                        let before = drawTree rng pool
                        let after = drawTree rng pool
                        let canHold = drawCanHold rng

                        let structural =
                            Diff.toOps Policy.none (OpScope.contained canHold nodew idw) before after
                            |> Result.map (List.filter (isUpdate >> not))

                        let aware =
                            Diff.toOpsWith encode Policy.none (OpScope.contained canHold nodew idw) before after
                            |> Result.map (List.filter (isUpdate >> not))

                        Expect.equal aware structural "same refusals, same structural blocks in the same order"

                testCase
                    "toOpsWith round-trips content under the plain engine and emits no rewrite for a node whose content is unchanged"
                <| fun () ->
                    let rng = Random 2305

                    for _ in 1..500 do
                        let before = drawTree rng pool
                        let after = drawTree rng pool

                        match Diff.toOpsWith encode Policy.none (OpScope.plain nodew idw) before after with
                        | Error e -> failtestf "toOpsWith refused %A" e
                        | Ok ops ->
                            Expect.equal (treeOf ops before) after "applyAll(toOpsWith) = after, content included"

                            for op in ops do
                                match op with
                                | UpdateNode n ->
                                    let b = Tree.tryFind nodew idw n.Id before |> Option.get

                                    Expect.notEqual
                                        (encode b)
                                        (encode n)
                                        "a rewrite is emitted only for a survivor whose own content moved"
                                | _ -> ()

                    // and the identity pair is the empty script
                    let t = drawTree rng pool
                    Expect.equal (Diff.toOpsWith encode Policy.none (OpScope.plain nodew idw) t t) (Ok []) "identity" ]

          testList
              "normalize"
              [ testCase "the three UpdateNode classes collapse"
                <| fun () ->
                    let a = RNode.node "a" "para" []
                    let a2 = a |> withValue "two"
                    let a3 = a |> withValue "three"
                    let held = RNode.node "a" "section" [ RNode.node "c" "para" [] ]

                    Expect.equal
                        (Ops.normalize nodew idw [ UpdateNode a2; UpdateNode a3 ])
                        [ UpdateNode a3 ]
                        "update then update of one id: last wins"

                    Expect.equal
                        (Ops.normalize nodew idw [ InsertChild("root", held); UpdateNode a2 ])
                        [ InsertChild("root", { a2 with Children = held.Children }) ]
                        "insert then update of the inserted id: one insert, the update's content over the insert's children"

                    Expect.equal
                        (Ops.normalize nodew idw [ UpdateNode a2; RemoveNode "a" ])
                        [ RemoveNode "a" ]
                        "update then remove of one id: the remove"

                    Expect.equal
                        (Ops.normalize
                            nodew
                            idw
                            [ InsertChild("root", a); UpdateNode a2; UpdateNode a3; RemoveNode "a" ])
                        []
                        "and the collapses chain through one pass"

                testCase "a collapse that newly adjoins two collapsible ops is caught in the one pass"
                <| fun () ->
                    let n = RNode.node "n" "para" []

                    Expect.equal
                        (Ops.normalize
                            nodew
                            idw
                            [ MoveNode("t", "a"); InsertChild("a", n); RemoveNode "n"; MoveNode("t", "b") ])
                        [ MoveNode("t", "b") ]
                        "the cancelled insert/remove between two same-target moves"

                testCase "a 2,000-op flat script normalises (the pre-fix stack overflow)"
                <| fun () ->
                    let ops = flatInserts 2000
                    Expect.equal (Ops.normalize nodew idw ops) ops "nothing collapses, nothing is lost"

                testCase "a 100,000-op script normalises in one linear pass"
                <| fun () ->
                    // half of it collapses: every insert is followed by its remove
                    let collapsing =
                        [ for i in 1..50000 do
                              yield InsertChild("root", RNode.node (sprintf "y%d" i) "para" [])
                              yield RemoveNode(sprintf "y%d" i) ]

                    let flat = flatInserts 100000
                    let sw = Diagnostics.Stopwatch.StartNew()
                    Expect.equal (Ops.normalize nodew idw collapsing) [] "100,000 ops that net to nothing"
                    Expect.equal (List.length (Ops.normalize nodew idw flat)) 100000 "100,000 ops that keep everything"
                    sw.Stop()

                    Expect.isLessThan
                        sw.Elapsed.TotalSeconds
                        20.0
                        "linear, not quadratic (a quadratic pass is minutes here)"

                testCase "differential with adjacent-collapse bias: preservation, idempotence, non-growth"
                <| fun () ->
                    let rng = Random 3305
                    let mutable collapsed = 0

                    for _ in 1..400 do
                        let tree = drawTree rng pool
                        let script, reached = drawScript rng tree 8
                        let normd = Ops.normalize nodew idw script

                        if List.length normd < List.length script then
                            collapsed <- collapsed + 1

                        Expect.equal
                            (Ops.applyAll Policy.none (OpScope.plain nodew idw) normd tree)
                            (Ok reached)
                            "applyAll(normalize s) = applyAll s"

                        Expect.equal (Ops.normalize nodew idw normd) normd "normalize is idempotent"
                        Expect.isLessThanOrEqual (List.length normd) (List.length script) "never longer"

                    Expect.isGreaterThan
                        collapsed
                        100
                        "the bias reached the collapses (a differential over scripts that never collapse measures nothing)" ]

          testList
              "invert"
              [ testCase "invert (UpdateNode old) carries the old content as a SHELL"
                <| fun () ->
                    let tree =
                        RNode.node "root" "doc" [ RNode.node "a" "section" [ RNode.node "c" "para" [] ] ]

                    let rewrite = UpdateNode(RNode.node "a" "aside" [])

                    match Ops.invert nodew idw rewrite tree with
                    | Ok(UpdateNode old) ->
                        Expect.equal old.Kind "section" "the old content"
                        Expect.isEmpty old.Children "and none of the children — the inverse never reads them"
                    | other -> failtestf "unexpected inverse %A" other

                    let after =
                        Ops.apply Policy.none (OpScope.plain nodew idw) rewrite tree
                        |> Result.defaultWith (fun e -> failtestf "%A" e)

                    Expect.equal
                        (Ops.apply
                            Policy.none
                            (OpScope.plain nodew idw)
                            (Ops.invert nodew idw rewrite tree
                             |> Result.defaultWith (fun e -> failtestf "%A" e))
                            after)
                        (Ok tree)
                        "and it restores the tree, children included"

                testCase "invertAll: applyAll (invertAll s pre) (applyAll s pre) = pre, and it refuses as applyAll does"
                <| fun () ->
                    let rng = Random 4305

                    for _ in 1..400 do
                        let tree = drawTree rng pool
                        let script, reached = drawScript rng tree 6

                        match Ops.invertAll nodew idw script tree with
                        | Error(i, e) -> failtestf "invertAll refused an applyable script at %d: %A" i e
                        | Ok undo ->
                            Expect.equal (List.length undo) (List.length script) "one inverse per op"

                            Expect.equal
                                (Ops.applyAll Policy.none (OpScope.plain nodew idw) undo reached)
                                (Ok tree)
                                "the undo script restores pre"

                        // the same script with a refused op spliced in: the index and envelope are
                        // `applyAll`'s
                        let broken = script @ [ RemoveNode "not-here" ]

                        match
                            Ops.invertAll nodew idw broken tree,
                            Ops.applyAll Policy.none (OpScope.plain nodew idw) broken tree
                        with
                        | Error(i, e), Error { Applied = j; Rejection = e' } ->
                            Expect.equal i j "same index"
                            Expect.equal (Rejection.code e) (Rejection.code e') "same envelope class"
                        | a, b -> failtestf "expected both to refuse: %A / %A" a b ]

          testList
              "arbitrate"
              [ testCase "a malformed base is refused with the named duplicate, every proposal Inapplicable at 0"
                <| fun () ->
                    let malformed =
                        RNode.node
                            "root"
                            "doc"
                            [ RNode.node "a" "para" []; RNode.node "b" "para" []; RNode.node "a" "para" [] ]

                    let proposals =
                        [ { Id = 2
                            Holder = "two"
                            Ops = [ InsertChild("b", RNode.node "n" "para" []) ] }
                          { Id = 1
                            Holder = "one"
                            Ops = [ RemoveNode "a" ] } ]

                    let result =
                        Arbitration.arbitrate Policy.none (OpScope.plain nodew idw) malformed proposals

                    Expect.isEmpty result.Accepted "nothing is accepted"
                    Expect.isEmpty result.MergedScript "nothing is merged"

                    Expect.equal
                        (result.Rejected |> List.map (fun (p, r) -> p.Id, r))
                        [ 1, Inapplicable(0, DuplicateId "a"); 2, Inapplicable(0, DuplicateId "a") ]
                        "each proposal, in pinned order, refused with the first repeated id"

                    // and the contained / grammar / referenced forms refuse the same base the same way
                    let contained =
                        Arbitration.arbitrate
                            Policy.none
                            (OpScope.contained (fun _ -> true) nodew idw)
                            malformed
                            proposals

                    Expect.equal contained.Rejected result.Rejected "arbitrateContained"

                testCase
                    "differential: the merged script applies, and the accepted scripts reach the same tree in any order"
                <| fun () ->
                    let rng = Random 5305
                    let mutable multi = 0

                    for _ in 1..600 do
                        let tree = drawTree rng pool

                        let proposals =
                            [ for i in 1..4 ->
                                  { Id = i
                                    Holder = sprintf "h%d" i
                                    Ops = fst (drawScript rng tree 2) } ]

                        let result =
                            Arbitration.arbitrate Policy.none (OpScope.plain nodew idw) tree proposals

                        let merged =
                            match Ops.applyAll Policy.none (OpScope.plain nodew idw) result.MergedScript tree with
                            | Ok t -> t
                            | Error { Applied = i; Rejection = e } ->
                                failtestf "the merged script was refused at %d: %A" i e

                        if List.length result.Accepted > 1 then
                            multi <- multi + 1

                        for _ in 1..4 do
                            let order = shuffled rng result.Accepted |> List.collect _.Ops

                            Expect.equal
                                (Ops.applyAll Policy.none (OpScope.plain nodew idw) order tree)
                                (Ok merged)
                                "a shuffled order of the accepted scripts applies and lands on the merged tree"

                    Expect.isGreaterThan multi 30 "enough arbitrations accepted more than one proposal" ]

          testList
              "Tree.Index"
              [ testCase "isFreshFor sees a content-only UpdateNode through its encoder"
                <| fun () ->
                    // Phase 305 found the check blind to content and added a content-aware pair beside
                    // it; Phase 447 (D150) made the one check exact and folded the encoder into it.
                    let tree = sample ()
                    let ix = ok (Tree.Index.build nodew idw tree)
                    Expect.isTrue (Tree.Index.isFreshFor nodew idw encode tree ix) "fresh on the unedited tree"

                    let a1 = Tree.tryFind nodew idw "a1" tree |> Option.get

                    let edited =
                        Ops.apply Policy.none (OpScope.plain nodew idw) (UpdateNode(a1 |> withValue "rewritten")) tree
                        |> Result.defaultWith (fun e -> failtestf "%A" e)

                    Expect.isFalse
                        (Tree.Index.isFreshFor nodew idw encode edited ix)
                        "stale: id, kind and children are unchanged, and the encoder reads the new value"

                    Expect.isTrue
                        (Tree.Index.isFreshFor
                            nodew
                            idw
                            encode
                            edited
                            (ok (Ops.Index.afterOp nodew idw (UpdateNode(a1 |> withValue "rewritten")) edited ix)))
                        "and the index carried through the edit is fresh"

                    let moved =
                        Ops.apply Policy.none (OpScope.plain nodew idw) (RemoveNode "a1") tree
                        |> Result.defaultWith (fun e -> failtestf "%A" e)

                    Expect.isFalse (Tree.Index.isFreshFor nodew idw encode moved ix) "a structural edit is stale too" ] ]

// ---- Phase 518: `Diff.changes` read off the script's own passes ---------------------------------
//
// `Conformance.changeLaws` (Phase 314) held `changes` — then a second walk of both trees — to the
// script `toOpsWith` emits. Since Phase 518 the two are one run of the emitter, so the family left
// the kit and its eight checks are this unit test of one function, over independent drawn pairs
// (the pairs that reach an added, a removed, a moved, a changed and a reordered id).

let private rankOf (k: Diff.ChangeKind<string>) =
    match k with
    | Diff.ChangeKind.Added -> 0
    | Diff.ChangeKind.Removed -> 1
    | Diff.ChangeKind.Moved _ -> 2
    | Diff.ChangeKind.KindChanged _ -> 3
    | Diff.ChangeKind.Changed -> 4
    | Diff.ChangeKind.Reordered -> 5

[<Tests>]
let changeTests =
    testList
        "Diff.changes is the script's classification (Phase 518, was Conformance.changeLaws)"
        [ testCase "over drawn pairs every kind names exactly the ids the script's ops name"
          <| fun _ ->
              let rng = Random 518
              let scope = OpScope.plain nodew idw
              let mutable reached = Map.empty<int, int>

              for i in 1..300 do
                  let before = drawTree rng pool
                  // the drawn trees place children in pool order, so half the pairs reverse every
                  // child list of `after`: the kept children then stand in another relative order
                  let after =
                      match drawTree rng pool with
                      | t when i % 2 = 0 ->
                          Tree.map
                              nodew
                              (fun n ->
                                  { n with
                                      Children = List.rev n.Children })
                              t
                      | t -> t

                  let at (what: string) =
                      sprintf "pair %d: %s\nbefore %A\nafter %A" i what before after

                  Expect.equal (Diff.changes encode Policy.none scope before before) (Ok []) (at "identity")

                  match
                      Diff.changes encode Policy.none scope before after,
                      Diff.toOpsWith encode Policy.none scope before after
                  with
                  | Ok cs, Ok ops ->
                      let bix = ok (Tree.Index.build nodew idw before)
                      let aix = ok (Tree.Index.build nodew idw after)
                      let bIds = bix.ById |> Map.keys |> Set.ofSeq
                      let aIds = aix.ById |> Map.keys |> Set.ofSeq

                      for c in cs do
                          reached <- reached |> Map.change (rankOf c.Kind) (fun n -> Some(defaultArg n 0 + 1))

                      let named (pick: Diff.ChangeKind<string> -> bool) =
                          cs |> List.filter (fun c -> pick c.Kind) |> List.map _.Id |> Set.ofList

                      let ofOps (pick: SkeletonOp<RNode, string> -> string option) =
                          ops |> List.choose pick |> Set.ofList

                      let addedIds = named ((=) Diff.ChangeKind.Added)
                      let removedIds = named ((=) Diff.ChangeKind.Removed)

                      let movedIds =
                          named (function
                              | Diff.ChangeKind.Moved _ -> true
                              | _ -> false)

                      let updateTargets =
                          ofOps (function
                              | UpdateNode n -> Some n.Id
                              | _ -> None)

                      Expect.isTrue
                          ((cs
                            |> List.map (fun c -> c.Id, rankOf c.Kind)
                            |> List.pairwise
                            |> List.forall (fun (x, y) -> x < y))
                           && cs |> List.forall (fun c -> Set.contains c.Id (Set.union bIds aIds)))
                          (at "entries ascend by (id key, kind rank) and name only ids of the two trees")

                      Expect.equal
                          addedIds
                          (ofOps (function
                              | InsertChild(_, n) -> Some n.Id
                              | _ -> None))
                          (at "Added is the grafts")

                      Expect.equal
                          movedIds
                          (ofOps (function
                              | MoveNode(t, _) -> Some t
                              | _ -> None))
                          (at "Moved is the MoveNode targets")

                      for c in cs do
                          match c.Kind with
                          | Diff.ChangeKind.Moved(f, t) ->
                              Expect.equal
                                  (Map.tryFind c.Id bix.ParentOf, Map.tryFind c.Id aix.ParentOf)
                                  (Some f, Some t)
                                  (at "a move names both parents")
                          | _ -> ()

                      // the encoder reads the kind, so a kind change is an update too
                      Expect.equal
                          (named (function
                              | Diff.ChangeKind.KindChanged _
                              | Diff.ChangeKind.Changed -> true
                              | _ -> false))
                          updateTargets
                          (at "KindChanged and Changed are the UpdateNode targets")

                      let removeTargets =
                          ofOps (function
                              | RemoveNode t -> Some t
                              | _ -> None)

                      let belowARemoval (k: string) =
                          Tree.Index.ancestors idw k bix
                          |> List.exists (fun p -> removeTargets.Contains p.Id)

                      Expect.isTrue
                          (removedIds = Set.difference bIds aIds
                           && Set.isSubset removeTargets removedIds
                           && removedIds |> Set.forall (fun k -> removeTargets.Contains k || belowARemoval k))
                          (at "Removed is before's minus after's, each a RemoveNode target or below one")

                      let reorderParents =
                          ofOps (function
                              | ReorderChildren(p, _) -> Some p
                              | _ -> None)

                      let reordered = named ((=) Diff.ChangeKind.Reordered)

                      let explained (p: string) =
                          reordered.Contains p
                          || (Map.tryFind p aix.ById
                              |> Option.exists (fun n ->
                                  n.Children
                                  |> List.exists (fun c -> addedIds.Contains c.Id || movedIds.Contains c.Id)))

                      Expect.isTrue
                          (Set.isSubset reordered reorderParents && reorderParents |> Set.forall explained)
                          (at "every Reordered id is a reorder's parent, and every reorder is explained")

                      match Ops.applyAll Policy.none scope ops before with
                      | Ok landed ->
                          Expect.equal
                              (Diff.changes encode Policy.none scope landed after)
                              (Ok [])
                              (at "the landing classifies as unchanged")
                      | Error e -> failtest (at (sprintf "the script does not apply: %A" e))
                  | answer -> failtest (at (sprintf "a drawn pair was refused: %A" answer))

              for rank, name in
                  [ 0, "added"
                    1, "removed"
                    2, "moved"
                    3, "kind"
                    4, "changed"
                    5, "reordered" ] do
                  Expect.isGreaterThan
                      (Map.tryFind rank reached |> Option.defaultValue 0)
                      10
                      (sprintf "the pairs reached %s ids" name) ]
