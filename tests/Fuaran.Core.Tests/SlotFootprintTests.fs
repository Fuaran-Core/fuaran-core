module Fuaran.Core.Tests.SlotFootprintTests

// Phase 340 — a footprint names the slot it writes.
//
// The four node-granular sets make any write to PART of a node a write to the whole node, so two
// ops touching different fields of one node interfere and the fold halts on a pair that commutes.
// The premise test at the top pins that imprecision as it stood (a field write lowered with
// `Footprint.contentEdit`); everything after it is the slot granularity: `Footprint.slotEdit` and
// `Footprint.readingSlot`, the three slot clauses of `Ops.interference`, `Dag.conflicts`'
// `SlotClash` shape carrying the slot, and the conservative rule that a whole-node write is a
// write of every slot. The record domain below is the smallest state that HAS slots — nodes with
// named fields — so the commuting claims are measured under an apply, not only at the footprint.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ConformanceTests

let private setOf (xs: 'a list) = Set.ofList xs

// ---------------------------------------------------------------------------
//  The record domain — nodes with named fields, a slot write and a whole-node write
// ---------------------------------------------------------------------------

type RecOp =
    /// Writes ONE field of a node — a slot write, lowered with `Footprint.slotEdit`.
    | SetField of node: string * field: string * value: string
    /// Replaces EVERY field of a node — a whole-node write, lowered with `Footprint.contentEdit`.
    | Rewrite of node: string * fields: (string * string) list

type Records = Map<string, Map<string, string>>

let private applyRec (op: RecOp) (st: Records) : Result<Records, string> =
    match op with
    | SetField(n, f, v) ->
        let fields = st |> Map.tryFind n |> Option.defaultValue Map.empty
        Ok(Map.add n (Map.add f v fields) st)
    | Rewrite(n, fields) -> Ok(Map.add n (Map.ofList fields) st)

let private encRec (op: RecOp) : string =
    match op with
    | SetField(n, f, v) -> "set|" + n + "|" + f + "|" + v
    | Rewrite(n, fields) ->
        "rewrite|"
        + n
        + "|"
        + (fields |> List.map (fun (f, v) -> f + "=" + v) |> String.concat ",")

let recordW: StreamWitness<RecOp, Records, string> =
    { Apply = applyRec
      Encode = encRec
      Decode = fun _ -> Error "SlotFootprintTests: decode is unused by the family" }

let recordHash (st: Records) : string =
    st
    |> Map.toList
    |> List.map (fun (n, fields) ->
        n
        + "{"
        + (fields
           |> Map.toList
           |> List.map (fun (f, v) -> f + "=" + v)
           |> String.concat ",")
        + "}")
    |> String.concat ";"

/// The footprint that is kept: a field write names its slot; a rewrite names the node whole.
let recordFootprint (op: RecOp) : Footprint =
    match op with
    | SetField(n, f, _) -> Footprint.slotEdit n f
    | Rewrite(n, _) -> Footprint.contentEdit n

/// The four-set lowering every domain used before Phase 340: a field write is a write of the node.
let private liftedFootprint (op: RecOp) : Footprint =
    match op with
    | SetField(n, _, _) -> Footprint.contentEdit n
    | Rewrite(n, _) -> Footprint.contentEdit n

/// The unsound narrowing: a rewrite declared as a write of one slot, so it reads as commuting with
/// a field write of another slot — and does not.
let private rewriteAsSlot (op: RecOp) : Footprint =
    match op with
    | SetField(n, f, _) -> Footprint.slotEdit n f
    | Rewrite(n, _) -> Footprint.slotEdit n "*"

let private genRecOp (rng: ConfRng.T) : RecOp * ConfRng.T =
    let n, r1 = ConfRng.choose [ "n1"; "n2" ] rng
    let kind, r2 = ConfRng.intBelow 5 r1

    if kind = 0 then
        let v, r3 = ConfRng.intBelow 3 r2
        Rewrite(n, [ "a", string v; "b", string v ]), r3
    else
        let f, r3 = ConfRng.choose [ "a"; "b"; "c" ] r2
        let v, r4 = ConfRng.intBelow 4 r3
        SetField(n, f, string v), r4

let recordGen: StreamGen<RecOp, Records> =
    { State0 = Map.ofList [ "n1", Map.ofList [ "a", "0"; "b", "0" ]; "n2", Map.empty ]
      Op = genRecOp }

let private applyAll (ops: RecOp list) (st: Records) =
    ops |> List.fold (fun acc op -> acc |> Result.bind (applyRec op)) (Ok st)

/// Two lanes forked off one base node, each a single-op chain under its own actor.
let private forkDag (a: RecOp list) (b: RecOp list) =
    let h = OpStream.defaultHash

    let chain actor ops parent d0 =
        ops
        |> List.fold
            (fun (head, d) op ->
                let id, d' = Dag.append h recordW (Human actor) op head d |> Reference.built
                id, d')
            (parent, d0)

    let baseId, d1 =
        Dag.append h recordW (Human "base") (Rewrite("base", [])) "" Dag.empty
        |> Reference.built

    let headA, d2 = chain "lane-a" a baseId d1
    let headB, dag = chain "lane-b" b baseId d2
    baseId, headA, headB, dag

let private shapes (cs: MergeConflict<'Op> list) =
    cs |> List.map (fun c -> c.Shape, c.Address)

let private failed (results: LawResult list) =
    results |> List.filter (fun r -> not r.Passed)

let private failedNamed (fragment: string) (results: LawResult list) =
    failed results |> List.filter (fun r -> r.Law.Contains fragment)

// ---------------------------------------------------------------------------
//  The exhaustive universe — every slot clause fires, mirrors, and agrees with the fold
// ---------------------------------------------------------------------------

/// Every footprint over two nodes and three slots: the three node sets a slot clause reads are
/// each ∅ or {x}; the two slot sets are every subset of {(x,s), (x,t), (y,s)} — 512 footprints,
/// so every overlap shape a slot clause can see occurs, at the node and at the slot.
let private everySlotFootprint =
    let nodeSets = [ Set.empty; setOf [ "x" ] ]

    let slotSets =
        let slots = [ ("x", "s"); ("x", "t"); ("y", "s") ]

        [ for mask in 0..7 ->
              slots
              |> List.indexed
              |> List.filter (fun (i, _) -> (mask >>> i) &&& 1 = 1)
              |> List.map snd
              |> Set.ofList ]

    [ for r in nodeSets do
          for s in nodeSets do
              for c in nodeSets do
                  for sr in slotSets do
                      for sw in slotSets do
                          { Footprint.empty with
                              Reads = r
                              StructureWrites = s
                              ContentWrites = c
                              SlotReads = sr
                              SlotWrites = sw } ]

/// The slot clauses as their definition states them, computed here a second time so the test
/// does not read the implementation back to itself.
let private expectedSlotClauses (a: Footprint) (b: Footprint) =
    let nodes (ps: Set<string * string>) = ps |> Set.map fst

    let clash =
        Set.unionMany
            [ Set.intersect a.SlotWrites b.SlotWrites
              Set.intersect a.SlotWrites b.SlotReads
              Set.intersect a.SlotReads b.SlotWrites ]

    let against (p: Footprint) (q: Footprint) =
        Set.union
            (Set.intersect (nodes p.SlotWrites) (Set.unionMany [ q.ContentWrites; q.Reads; q.StructureWrites ]))
            (Set.intersect (nodes p.SlotReads) q.ContentWrites)

    [ if not clash.IsEmpty then
          Interference.SlotClash clash
      if not (against a b).IsEmpty then
          Interference.LeftSlotsRightNode(against a b)
      if not (against b a).IsEmpty then
          Interference.RightSlotsLeftNode(against b a) ]

let private isSlotClause (i: Interference) =
    match i with
    | Interference.SlotClash _
    | Interference.LeftSlotsRightNode _
    | Interference.RightSlotsLeftNode _ -> true
    | _ -> false

let private mirror (i: Interference) =
    match i with
    | Interference.SameTarget t -> Interference.SameTarget t
    | Interference.LeftWritesRightReads xs -> Interference.RightWritesLeftReads xs
    | Interference.RightWritesLeftReads xs -> Interference.LeftWritesRightReads xs
    | Interference.SameParent ps -> Interference.SameParent ps
    | Interference.LeftUnknownParent(relocated, structural) -> Interference.RightUnknownParent(structural, relocated)
    | Interference.RightUnknownParent(structural, relocated) -> Interference.LeftUnknownParent(relocated, structural)
    | Interference.SlotClash slots -> Interference.SlotClash slots
    | Interference.LeftSlotsRightNode nodes -> Interference.RightSlotsLeftNode nodes
    | Interference.RightSlotsLeftNode nodes -> Interference.LeftSlotsRightNode nodes

[<Tests>]
let tests =
    testList
        "Phase 340 — a footprint names the slot it writes"
        [ testList
              "the premise, pinned"
              [ testCase "the four-set lowering of a field write interferes with every other field write of the node"
                <| fun _ ->
                    // Before this phase a domain lowered `set n.a` and `set n.b` both to
                    // `contentEdit n`, and the pair was refused on the node — the imprecision the
                    // roadmap engine met twice (the 2026-09-14 and 2026-10-01 halts) and had to
                    // classify around by hand.
                    let a = Footprint.contentEdit "n"
                    let b = Footprint.contentEdit "n"

                    Expect.equal
                        (Ops.interference a b)
                        [ Interference.SameTarget(setOf [ "n" ])
                          Interference.LeftWritesRightReads(setOf [ "n" ])
                          Interference.RightWritesLeftReads(setOf [ "n" ]) ]
                        "refused at the node, three clauses, none naming a field"

                    let baseId, headA, headB, dag =
                        forkDag [ SetField("n", "a", "1") ] [ SetField("n", "b", "2") ]

                    match Dag.reconcile liftedFootprint dag baseId headA headB with
                    | Ok script -> failtestf "the lifted footprint folded the pair: %A" script
                    | Error cs ->
                        Expect.contains (shapes cs) (MergeConflictShape.ConcurrentUpdate, "n") "halted at the node"

                    // … and the pair COMMUTES, which is what makes the halt a cost rather than a catch.
                    let ab =
                        applyAll [ SetField("n", "a", "1"); SetField("n", "b", "2") ] recordGen.State0

                    let ba =
                        applyAll [ SetField("n", "b", "2"); SetField("n", "a", "1") ] recordGen.State0

                    Expect.equal ab ba "the two field writes reach the same records in either order"

                testCase "no skeleton op writes a slot: Ops.footprint and Ops.footprintKeyed leave both slot sets empty"
                <| fun _ ->
                    // An `UpdateNode` rewrites its target whole and a pure script cannot say which
                    // part of the payload changed, so the skeleton footprints never narrow to a slot
                    // — a domain with no slot-writing op of its own folds exactly as before.
                    let ops =
                        [ InsertChild("a", RNode.leaf "z" "para" "v")
                          RemoveNode "b1"
                          MoveNode("a1", "b")
                          ReorderChildren("a", [ "a2"; "a1" ])
                          UpdateNode(RNode.leaf "a1" "para" "w")
                          Batch [ RemoveNode "a2"; InsertChild("b", RNode.leaf "q" "para" "v") ] ]

                    for op in ops do
                        let f = Ops.footprint nodew idw [ op ]
                        Expect.isEmpty f.SlotReads (sprintf "%A reads no slot" op)
                        Expect.isEmpty f.SlotWrites (sprintf "%A writes no slot" op)

                        Expect.isEmpty
                            (Ops.interference f f |> List.filter isSlotClause)
                            (sprintf "%A carries no slot clause against itself" op)

                    for a in ops do
                        for b in ops do
                            Expect.isEmpty
                                (Dag.conflicts (fun op -> Ops.footprint nodew idw [ op ]) [ a ] [ b ]
                                 |> List.filter (fun c ->
                                     match c.Shape with
                                     | MergeConflictShape.SlotClash _ -> true
                                     | _ -> false))
                                "the fold reports no slot clash between skeleton ops" ]

          testList
              "the slot granularity"
              [ testCase "two writes to different slots of one node are independent and fold clean"
                <| fun _ ->
                    let a = Footprint.slotEdit "n" "a"
                    let b = Footprint.slotEdit "n" "b"
                    Expect.isTrue (Ops.independent a b) "independent"
                    Expect.isEmpty (Ops.interference a b) "no clause fails"

                    let baseId, headA, headB, dag =
                        forkDag [ SetField("n", "a", "1") ] [ SetField("n", "b", "2") ]

                    match Dag.reconcile recordFootprint dag baseId headA headB with
                    | Error cs -> failtestf "two different slots halted the fold: %A" cs
                    | Ok script ->
                        let folded = applyAll script recordGen.State0

                        Expect.equal
                            folded
                            (applyAll [ SetField("n", "b", "2"); SetField("n", "a", "1") ] recordGen.State0)
                            "the merged script reaches the records either sequential order reaches"

                testCase
                    "two writes to one slot with different payloads clash AT THE SLOT, named alike by arbitration and the fold"
                <| fun _ ->
                    let a = Footprint.slotEdit "n" "a"
                    let b = Footprint.slotEdit "n" "a"

                    Expect.equal
                        (Ops.interference a b)
                        [ Interference.SlotClash(setOf [ ("n", "a") ]) ]
                        "one clause, carrying the node and the slot"

                    let baseId, headA, headB, dag =
                        forkDag [ SetField("n", "a", "1") ] [ SetField("n", "a", "2") ]

                    match Dag.reconcile recordFootprint dag baseId headA headB with
                    | Ok script -> failtestf "two writes to one slot folded clean: %A" script
                    | Error cs ->
                        Expect.equal
                            (shapes cs)
                            [ MergeConflictShape.SlotClash "a", "n" ]
                            "the fold reports the slot clash at the node, slot on the shape"

                    // … and the clash is real: the orders disagree at that slot.
                    Expect.notEqual
                        (applyAll [ SetField("n", "a", "1"); SetField("n", "a", "2") ] recordGen.State0)
                        (applyAll [ SetField("n", "a", "2"); SetField("n", "a", "1") ] recordGen.State0)
                        "last writer wins"

                testCase "a whole-node write is a write of every slot, from either side"
                <| fun _ ->
                    let whole = Footprint.contentEdit "n"
                    let slot = Footprint.slotEdit "n" "a"

                    Expect.equal
                        (Ops.interference whole slot)
                        [ Interference.RightSlotsLeftNode(setOf [ "n" ]) ]
                        "the right accesses a slot of the node the left writes whole"

                    Expect.equal
                        (Ops.interference slot whole)
                        [ Interference.LeftSlotsRightNode(setOf [ "n" ]) ]
                        "mirrored from the slot's side"

                    Expect.equal
                        (shapes (Dag.conflicts id [ whole ] [ slot ]))
                        [ MergeConflictShape.ConcurrentUpdate, "n" ]
                        "the fold reports it at the node: a concurrent update"

                    // the other whole-node accesses Core's builders publish
                    for name, other in
                        [ "removeNode", Footprint.removeNode "n"
                          "insertUnder", Footprint.insertUnder "n" "c"
                          "reading", Footprint.reading [ "n" ]
                          "moveTo", Footprint.moveTo "n" "p" ] do
                        Expect.isFalse (Ops.independent slot other) (name + " of the node refuses the slot write")

                    // … and the pair does not commute, which is why the rule is kept.
                    let ab =
                        applyAll [ SetField("n1", "a", "7"); Rewrite("n1", [ "a", "1"; "b", "1" ]) ] recordGen.State0

                    let ba =
                        applyAll [ Rewrite("n1", [ "a", "1"; "b", "1" ]); SetField("n1", "a", "7") ] recordGen.State0

                    Expect.notEqual ab ba "a rewrite and a field write of one node do not commute"

                testCase "a slot read depends on its slot and on the whole node, and on nothing else"
                <| fun _ ->
                    let reader = Footprint.readingSlot "n" "a"
                    Expect.isTrue (Ops.independent reader (Footprint.slotEdit "n" "b")) "a write to another slot"
                    Expect.isTrue (Ops.independent reader (Footprint.readingSlot "n" "a")) "another read of the slot"

                    Expect.isTrue
                        (Ops.independent reader (Footprint.insertUnder "n" "c"))
                        "a structure write under the node, which leaves its fields alone"

                    Expect.equal
                        (Ops.interference reader (Footprint.slotEdit "n" "a"))
                        [ Interference.SlotClash(setOf [ ("n", "a") ]) ]
                        "a write to the slot it reads"

                    Expect.equal
                        (Ops.interference reader (Footprint.contentEdit "n"))
                        [ Interference.LeftSlotsRightNode(setOf [ "n" ]) ]
                        "a whole-node write of the node"

                    // where `reading [ n ]` depends on every slot write of the node
                    Expect.isFalse
                        (Ops.independent (Footprint.reading [ "n" ]) (Footprint.slotEdit "n" "b"))
                        "the whole-node read is the conservative form"

                testCase "union carries the slot sets, so growing a footprint never frees a pair"
                <| fun _ ->
                    let u = Footprint.union (Footprint.slotEdit "n" "a") (Footprint.readingSlot "m" "k")
                    Expect.equal u.SlotWrites (setOf [ ("n", "a") ]) "writes"
                    Expect.equal u.SlotReads (setOf [ ("n", "a"); ("m", "k") ]) "reads"

                    Expect.isFalse
                        (Ops.independent u (Footprint.slotEdit "m" "k"))
                        "the union still depends on what either side depended on" ]

          testList
              "every slot clause, exhaustively"
              [ testCase
                    "each slot clause carries exactly its definition's set, mirrors under a swap, and never fires without a slot"
                <| fun _ ->
                    let mutable fired = Set.empty

                    for a in everySlotFootprint do
                        for b in everySlotFootprint do
                            let clauses = Ops.interference a b
                            let slotClauses = clauses |> List.filter isSlotClause

                            if slotClauses <> expectedSlotClauses a b then
                                failtestf
                                    "slot clauses at %A / %A = %A, expected %A"
                                    a
                                    b
                                    slotClauses
                                    (expectedSlotClauses a b)

                            fired <-
                                slotClauses
                                |> List.map (function
                                    | Interference.SlotClash _ -> "SlotClash"
                                    | Interference.LeftSlotsRightNode _ -> "LeftSlotsRightNode"
                                    | _ -> "RightSlotsLeftNode")
                                |> Set.ofList
                                |> Set.union fired

                            let forward = clauses |> List.map mirror |> Set.ofList
                            let backward = Ops.interference b a |> Set.ofList

                            if forward <> backward then
                                failtestf "not mirror-symmetric at %A / %A" a b

                            if Ops.independent a b <> List.isEmpty clauses then
                                failtestf "independent disagrees with interference at %A / %A" a b

                    Expect.equal
                        fired
                        (setOf [ "SlotClash"; "LeftSlotsRightNode"; "RightSlotsLeftNode" ])
                        "every slot clause fired somewhere in the sweep"

                testCase "Dag.conflicts agrees with Ops.independent over the universe, and tags each clashing slot once"
                <| fun _ ->
                    let mutable slotReports = 0

                    for a in everySlotFootprint do
                        for b in everySlotFootprint do
                            let cs = Dag.conflicts id [ a ] [ b ]

                            if List.isEmpty cs <> Ops.independent a b then
                                failtestf "conflicts and independent disagree at %A / %A" a b

                            let reportedSlots =
                                cs
                                |> List.choose (fun c ->
                                    match c.Shape with
                                    | MergeConflictShape.SlotClash slot -> Some(c.Address, slot)
                                    | _ -> None)

                            if reportedSlots |> List.distinct |> List.length <> List.length reportedSlots then
                                failtestf "a slot reported twice at %A / %A: %A" a b reportedSlots

                            if Set.ofList reportedSlots <> Footprint.slotClash a b then
                                failtestf
                                    "the fold's slot clashes at %A / %A are %A, not %A"
                                    a
                                    b
                                    reportedSlots
                                    (Footprint.slotClash a b)

                            slotReports <- slotReports + List.length reportedSlots

                            let concurrentAt =
                                cs
                                |> List.choose (fun c ->
                                    if c.Shape = MergeConflictShape.ConcurrentUpdate then
                                        Some c.Address
                                    else
                                        None)
                                |> Set.ofList

                            let against =
                                Set.union (Footprint.slotsAgainstNode a b) (Footprint.slotsAgainstNode b a)

                            if not (Set.isSubset against concurrentAt) then
                                failtestf
                                    "a slot-against-node collision at %A / %A was not reported as a concurrent update"
                                    a
                                    b

                    Expect.isGreaterThan slotReports 0 "slot clashes were reported in the sweep" ]

          testList
              "Conformance.footprintLawsAt at a domain with slots"
              [ testCase "the slot footprint passes, the slot demands reached, and every slot clash named at the slot"
                <| fun _ ->
                    let results =
                        Conformance.footprintLawsAt recordW recordFootprint recordHash recordGen 3400 300

                    Expect.isEmpty (failed results) (sprintf "%A" (failed results))

                    let guard =
                        guardNamed "Conformance.footprintLawsAt" "script-pair independence and slot granularity" results

                    Expect.isTrue guard.Passed "the guard counts the slot dimension"

                testCase "the four-set lowering of the same domain is STARVED of the pairs only slots free"
                <| fun _ ->
                    // Every field write is a write of the node, so no pair is independent for a
                    // reason the slots supply — and the footprint declares no slot at all, so the
                    // family says so BY DECLARATION rather than demanding what cannot be drawn.
                    let results =
                        Conformance.footprintLawsAt recordW liftedFootprint recordHash recordGen 3400 300

                    Expect.isEmpty (failed results) (sprintf "%A" (failed results))

                    let guard =
                        results
                        |> List.find (fun r -> r.Law.StartsWith(SampleAdequacy.lawPrefix "Conformance.footprintLawsAt"))

                    Expect.stringContains guard.Law "vacuous BY DECLARATION" "the slot arms are declared vacuous"

                testCase
                    "teeth: a rewrite declared as a slot write is RED on soundness — the whole-node rule is load-bearing"
                <| fun _ ->
                    let results =
                        Conformance.footprintLawsAt recordW rewriteAsSlot recordHash recordGen 3400 300

                    let soundness = failedNamed "footprint soundness" results
                    Expect.equal (List.length soundness) 1 (sprintf "the soundness law goes red: %A" results)

                    let cx = soundness |> List.head |> (fun r -> defaultArg r.Counterexample "")
                    Expect.stringContains cx "did not commute" "the counterexample names the break"

                testCase
                    "teeth: a field write declared as a slot READ is RED — two writes to one slot read as independent and do not commute"
                <| fun _ ->
                    // The slot-level counterpart of dropping a content write: the footprint names
                    // the right slot but the wrong access, so a same-slot pair fails no clause, is
                    // drawn as independent, and lands apart under Apply.
                    let readOnly (op: RecOp) : Footprint =
                        match op with
                        | SetField(n, f, _) -> Footprint.readingSlot n f
                        | Rewrite(n, _) -> Footprint.contentEdit n

                    let results =
                        Conformance.footprintLawsAt recordW readOnly recordHash recordGen 3400 300

                    Expect.isNonEmpty (failed results) "a footprint that reads where it writes cannot pass" ] ]
