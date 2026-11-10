module Fuaran.Core.Tests.ChildListSlotTests

// Phase 625 — a structure write is the parent's child-list slot.
//
// Phase 340 compared a slot access against a structure write of its node AT THE NODE, and against a
// read of its node too, so a restyle of a container — a write of one of its fields — collided with
// every insert into it, move into or out of it and removal under it: none of those change the
// container's fields, only its child list (an insert reads its parent, and a structure write was a
// whole-node touch). Phase 625 names the child list as a slot (`Footprint.childListSlot`): a
// structure write of `p` is compared at `(p, #children)` against a slot access, and a read of `p`
// (an existence read, which no slot write changes) no longer meets a slot write at all. The
// container domain below is the reference tree with two slot writes beside the skeleton ops — a
// restyle (a write of the node's `value` field, its children untouched) and a reversal of the node's
// structural child list, declared as a write of the child-list slot — so every claim is measured
// under an apply, at the pure footprint and at the base-aware one.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ConformanceTests

type private Skel = SkeletonOp<RNode, string>

/// The container domain's ops: the skeleton ops, and two slot writes.
type ContainerOp =
    | Tree of Skel
    /// Writes a node's `value` field and nothing else — the slot `(n, "value")`.
    | Restyle of node: string * value: string
    /// Reverses a node's structural child list — the slot `(n, Footprint.childListSlot)`.
    | Reverse of node: string

let private scope = OpScope.plain nodew idw

let private setOf (xs: 'a list) = Set.ofList xs

/// `f` applied at the node `id`, the tree rebuilt around it; `None` where `id` is absent.
let rec private updateAt (id: string) (f: RNode -> RNode) (t: RNode) : RNode option =
    if t.Id = id then
        Some(f t)
    else
        let rec go (before: RNode list) (rest: RNode list) =
            match rest with
            | [] -> None
            | c :: after ->
                match updateAt id f c with
                | Some c' -> Some(List.rev before @ c' :: after)
                | None -> go (c :: before) after

        go [] t.Children |> Option.map (fun cs -> { t with Children = cs })

/// The domain's apply, with the restyle's behaviour a parameter so a falsifier can make it touch
/// the children too.
let private applyWith (restyle: string -> RNode -> RNode) (op: ContainerOp) (t: RNode) : Result<RNode, string> =
    let at id f =
        match updateAt id f t with
        | Some t' -> Ok t'
        | None -> Error("absent " + id)

    match op with
    | Tree s -> Ops.apply Policy.none scope s t |> Result.mapError (sprintf "%A")
    | Restyle(n, v) -> at n (restyle v)
    | Reverse n ->
        at n (fun x ->
            { x with
                Children = List.rev x.Children })

let private restyleValue (v: string) (x: RNode) = { x with Value = v }

/// A restyle that ALSO reverses the node's children, declared as if it wrote the field alone.
let private restyleReversing (v: string) (x: RNode) =
    { x with
        Value = v
        Children = List.rev x.Children }

let private witnessWith (restyle: string -> RNode -> RNode) : StreamWitness<ContainerOp, RNode, string> =
    { Apply = applyWith restyle
      Encode = sprintf "%A"
      Decode = fun _ -> Error "ChildListSlotTests: decode is unused by the families" }

let private containerW = witnessWith restyleValue

let private hashTree: RNode -> string = Tree.encodeHash nodew encNode

/// The domain's footprint over a skeleton footprint: each slot write names its slot.
let private footprintOver (skeleton: Skel -> Footprint) (op: ContainerOp) : Footprint =
    match op with
    | Tree s -> skeleton s
    | Restyle(n, _) -> Footprint.slotEdit n "value"
    | Reverse n -> Footprint.slotEdit n Footprint.childListSlot

let private footprintOf: ContainerOp -> Footprint =
    footprintOver (fun s -> Ops.footprint Policy.none scope [ s ])

let private footprintIn (t: RNode) : ContainerOp -> Footprint =
    footprintOver (fun s -> Ops.footprintIn Policy.none scope t [ s ])

/// The reversal declared at a slot of its own name rather than the child list — the falsifier of
/// the colliding arm: an insert beside it reads as free, and does not commute.
let private reverseMisnamed (skeleton: Skel -> Footprint) (op: ContainerOp) : Footprint =
    match op with
    | Reverse n -> Footprint.slotEdit n "order"
    | _ -> footprintOver skeleton op

let private ids = [ "root"; "a"; "a1"; "a2"; "b"; "b1"; "n0"; "n1" ]

/// A state-blind draw, weighted to restyles so a restyle routinely meets a structure write of its
/// node. `withRemoves` adds removals: the pure footprint of a slot write carries no unknown-parent
/// write, so it cannot see the removal of an ancestor (`Footprint.slotEdit`'s obligation), and the
/// pure family is run without them; the base-aware footprint content-writes the removed subtree,
/// so its family draws them.
let private genOp (withRemoves: bool) (rng: ConfRng.T) : ContainerOp * ConfRng.T =
    let kind, r1 = ConfRng.intBelow (if withRemoves then 9 else 8) rng

    match kind with
    | 0
    | 1 ->
        let n, r2 = ConfRng.choose ids r1
        let v, r3 = ConfRng.intBelow 3 r2
        Restyle(n, "s" + string v), r3
    | 2 ->
        let n, r2 = ConfRng.choose ids r1
        Reverse n, r2
    | 3
    | 4 ->
        let p, r2 = ConfRng.choose ids r1
        let k, r3 = ConfRng.intBelow 2 r2
        Tree(InsertChild(p, RNode.leaf ("n" + string k) "para" "")), r3
    | 5 ->
        let x, r2 = ConfRng.choose ids r1
        let np, r3 = ConfRng.choose ids r2
        Tree(MoveNode(x, np)), r3
    | 6 ->
        let p, order, r2 =
            match ConfRng.intBelow 3 r1 with
            | 0, r -> "a", [ "a2"; "a1" ], r
            | 1, r -> "a", [ "a1"; "a2" ], r
            | _, r -> "b", [ "b1" ], r

        Tree(ReorderChildren(p, order)), r2
    | 7 ->
        let n, r2 = ConfRng.choose ids r1
        Tree(UpdateNode(RNode.leaf n "para" "u")), r2
    | _ ->
        let n, r2 = ConfRng.choose ids r1
        Tree(RemoveNode n), r2

let private pureGen: StreamGen<ContainerOp, RNode> =
    { State0 = sample (); Op = genOp false }

let private baseGen: StreamGen<ContainerOp, RNode> =
    { State0 = sample (); Op = genOp true }

let private failedNamed (fragment: string) (results: LawResult list) =
    results |> List.filter (fun r -> not r.Passed && r.Law.Contains fragment)

let private childListLawText = "compared at its parent's child-list slot"

/// The guard demanded `name` and the sample reached it at least once.
let private reached (guard: LawResult) (name: string) (message: string) =
    match guard.Reached |> List.tryFind (fun (n, _) -> n = name) with
    | Some(_, count) -> Expect.isGreaterThan count 0 message
    | None -> failtestf "%s: the guard does not demand %s (it reached %A)" message name guard.Reached

let private skelIn (ops: Skel list) =
    Ops.footprintIn Policy.none scope (sample ()) ops

let private skelPure (ops: Skel list) = Ops.footprint Policy.none scope ops

let private slotShapes (fa: Footprint) (fb: Footprint) =
    Dag.conflicts id [ fa ] [ fb ]
    |> List.choose (fun c ->
        match c.Shape with
        | MergeConflictShape.SlotClash slot -> Some(c.Address, slot)
        | _ -> None)

[<Tests>]
let tests =
    testList
        "Phase 625 — a structure write is the parent's child-list slot"
        [ testList
              "the acceptance"
              [ testCase
                    "a restyle of a container is independent of an insert into it, a move into or out of it and a removal under it"
                <| fun _ ->
                    let restyle = Footprint.slotEdit "a" "style"

                    let structural =
                        [ "insert into (builder)", Footprint.insertUnder "a" "c"
                          "move into (builder)", Footprint.moveTo "b1" "a"
                          "insert into", skelPure [ InsertChild("a", RNode.leaf "c" "para" "") ]
                          "move into", skelPure [ MoveNode("b1", "a") ]
                          "reorder of it", skelPure [ ReorderChildren("a", [ "a2"; "a1" ]) ]
                          "removal under (pure)", skelPure [ RemoveNode "a1" ]
                          "move out of (pure)", skelPure [ MoveNode("a1", "b") ]
                          "insert into (base-aware)", skelIn [ InsertChild("a", RNode.leaf "c" "para" "") ]
                          "move into (base-aware)", skelIn [ MoveNode("b1", "a") ]
                          "removal under (base-aware)", skelIn [ RemoveNode "a1" ]
                          "move out of (base-aware)", skelIn [ MoveNode("a1", "b") ] ]

                    for name, f in structural do
                        Expect.isEmpty (Ops.interference restyle f) (name + ": no clause fails")
                        Expect.isEmpty (Ops.interference f restyle) (name + ": nor mirrored")
                        Expect.isEmpty (Dag.conflicts id [ restyle ] [ f ]) (name + ": the fold reports nothing")

                testCase "two structure writes of one parent still collide, once, on the same-parent rule"
                <| fun _ ->
                    let a = Footprint.insertUnder "p" "c"
                    let b = Footprint.insertUnder "p" "d"

                    Expect.equal
                        (Ops.interference a b)
                        [ Interference.SameParent(setOf [ "p" ]) ]
                        "no slot clash beside it"

                    Expect.isEmpty (slotShapes a b) "the fold reports no child-list SlotClash beside the position clash"

                testCase "a child-list slot access beside a structure write clashes AT the child-list slot"
                <| fun _ ->
                    let slot = ("p", Footprint.childListSlot)
                    let insert = Footprint.insertUnder "p" "c"

                    for name, access in
                        [ "a write of it", Footprint.slotEdit "p" Footprint.childListSlot
                          "a read of it", Footprint.readingSlot "p" Footprint.childListSlot ] do
                        Expect.equal
                            (Ops.interference access insert)
                            [ Interference.SlotClash(setOf [ slot ]) ]
                            (name + ": one clause, naming the slot")

                        Expect.equal
                            (Ops.interference insert access)
                            [ Interference.SlotClash(setOf [ slot ]) ]
                            (name + ": the same from the structure write's side")

                        Expect.equal
                            (slotShapes access insert)
                            [ "p", Footprint.childListSlot ]
                            (name + ": the fold tags it at the parent, the slot on the shape")

                testCase "against the node sets nothing moved: destruction and whole-node writes still collide"
                <| fun _ ->
                    // the parent's destruction against an insert under it — the insert reads its parent
                    Expect.isFalse
                        (Ops.independent (Footprint.removeNode "p") (Footprint.insertUnder "p" "c"))
                        "removal of the parent"
                    // a whole-node write is a write of every slot
                    Expect.isFalse
                        (Ops.independent (Footprint.contentEdit "p") (Footprint.slotEdit "p" "style"))
                        "a whole-node write against a slot write"

                    Expect.isFalse
                        (Ops.independent (Footprint.contentEdit "p") (Footprint.readingSlot "p" "style"))
                        "a whole-node write against a slot read"

                testCase "a read of a node is of its existence: a slot write of the node commutes with it"
                <| fun _ ->
                    Expect.isTrue
                        (Ops.independent (Footprint.reading [ "p" ]) (Footprint.slotEdit "p" "style"))
                        "a reference to p beside a restyle of p"

                    Expect.isTrue
                        (Ops.independent (Footprint.reading [ "p" ]) (Footprint.slotEdit "p" Footprint.childListSlot))
                        "the child list is a slot like any other against an existence read"

                testCase "the restyle and the insert commute under the domain's Apply, in both orders, from one tree"
                <| fun _ ->
                    let a = Restyle("a", "s1")
                    let b = Tree(InsertChild("a", RNode.leaf "c" "para" ""))

                    let run ops =
                        ops
                        |> List.fold (fun acc op -> Result.bind (containerW.Apply op) acc) (Ok(sample ()))

                    match run [ a; b ], run [ b; a ] with
                    | Ok x, Ok y -> Expect.equal (hashTree x) (hashTree y) "both orders land on one tree"
                    | other -> failtestf "an order refused: %A" other

                    Expect.isTrue (Ops.independent (footprintOf a) (footprintOf b)) "and the footprint frees them" ]

          testList
              "the law families"
              [ testCase "Conformance.footprintLawsAt certifies the container domain green, the child-list arms reached"
                <| fun _ ->
                    let results =
                        Conformance.footprintLawsAt containerW footprintOf hashTree pureGen 6250 300

                    for r in results do
                        Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

                    let guard =
                        guardNamed "Conformance.footprintLawsAt" "script-pair independence and slot granularity" results

                    reached guard "child-list-freed pair" "the freed pairs are demanded and drawn"
                    reached guard "child-list clash pair" "and the clash pairs"

                testCase
                    "Conformance.footprintInLawsAt certifies the container domain green, the child-list arms reached"
                <| fun _ ->
                    let results =
                        Conformance.footprintInLawsAt containerW footprintOf footprintIn hashTree baseGen 6251 300

                    for r in results do
                        Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

                    let guard =
                        guardNamed
                            "Conformance.footprintInLawsAt"
                            "script-pair independence and base-aware precision"
                            results

                    reached guard "child-list-freed pair" "the freed pairs are demanded and drawn"

                testCase
                    "falsifier: a restyle that also rewrites the child list, declared as a field write, is RED on the child-list law"
                <| fun _ ->
                    let w = witnessWith restyleReversing

                    let pure = Conformance.footprintLawsAt w footprintOf hashTree pureGen 6252 300
                    Expect.isNonEmpty (failedNamed childListLawText pure) "the pure family names the freed pair"

                    let based =
                        Conformance.footprintInLawsAt w footprintOf footprintIn hashTree baseGen 6253 300

                    Expect.isNonEmpty (failedNamed childListLawText based) "and so does the base-aware family"

                testCase "falsifier: a child-list rewrite declared under another slot name is RED on the child-list law"
                <| fun _ ->
                    let misnamed = reverseMisnamed (fun s -> Ops.footprint Policy.none scope [ s ])

                    let results =
                        Conformance.footprintLawsAt containerW misnamed hashTree pureGen 6254 300

                    Expect.isNonEmpty
                        (failedNamed childListLawText results)
                        "an insert beside it reads as free and does not commute"

                testCase "falsifier: the Phase 340 node-level comparison starves the child-list arm"
                <| fun _ ->
                    // Every slot access lifted to its node — the four-set footprint — declares no slot,
                    // so no pair is freed at the child-list slot and the arm says so by declaration.
                    let lifted (op: ContainerOp) =
                        let f = footprintOf op

                        { f with
                            Reads = Set.union f.Reads (Footprint.slotNodes f.SlotReads)
                            ContentWrites = Set.union f.ContentWrites (Footprint.slotNodes f.SlotWrites)
                            SlotReads = Set.empty
                            SlotWrites = Set.empty }

                    let results =
                        Conformance.footprintLawsAt containerW lifted hashTree pureGen 6255 300

                    for r in results do
                        Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

                    let guard =
                        guardNamed "Conformance.footprintLawsAt" "script-pair independence" results

                    Expect.isFalse
                        (guard.Reached |> List.exists (fun (name, _) -> name = "child-list-freed pair"))
                        "no child-list demand where no slot is declared" ] ]
