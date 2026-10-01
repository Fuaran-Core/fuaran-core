module Fuaran.Core.Tests.KeyedSlotFoldTests

// Phase 334 — the premise check, kept as a regression pin. The phase set out to add a fifth
// `Footprint` set and a `KeyedSlotClash` conflict shape on the premise that two lanes placing
// DIFFERENT nodes into ONE keyed slot fold clean through `Dag.reconcile`, leaving two children
// under one key. Measured against the tree, that premise does not hold, and these cases say why:
//
//   - In the skeleton vocabulary a keyed slot is written only by rewriting its holder
//     (`UpdateNode` of the holder, whose payload carries the keyed positions) or by authoring the
//     holder (`InsertChild` of a subtree containing it). Both content-write the holder, under
//     `Ops.footprint` and `Ops.footprintKeyed` alike, so two lanes writing one slot always fail
//     `Ops.independent` with `SameTarget` on the holder and `Dag.reconcile` halts with
//     `ConcurrentUpdate` there. The engine cannot even address a slot otherwise: `KeyedWitness`
//     names no key, and the keyed engine refuses to vacate or relocate a keyed node
//     (`KeyedPosition`).
//   - A domain op that writes a slot by key builds its footprint itself, and every builder Core
//     publishes for it names the holder: `Footprint.insertUnder holder child` halts the pair as
//     `InsertPositionClash` at the holder, `Footprint.contentEdit holder` as `ConcurrentUpdate`.
//     Only a footprint that names neither the holder nor the slot folds the pair clean, and that is
//     a footprint missing a write, not a gap in the fold.
//
// What remains true of the phase's motivation is the LABEL: the clash is reported at the holder,
// under a shape that does not say "slot". That is a reporting refinement rather than a soundness
// defect, and it is left to a decision rather than shipped as a breaking change on a false premise.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ConformanceTests

let private k (id: string) (children: KNode list) (cases: (string * KNode) list) : KNode =
    { Id = id
      Kind = (if List.isEmpty children then "para" else "section")
      Children = children
      Cases = cases }

let private leaf (id: string) = k id [] []

let private canHoldAll (_: KNode) = true

/// root [ body [ sw { on: c0 } ] ] — one holder, one keyed slot `on` holding `c0`.
let private doc () =
    k "root" [ k "body" [ k "sw" [] [ "on", leaf "c0" ] ] [] ] []

/// The holder `sw` rewritten in place with `id` in its `on` slot — how a skeleton script places a
/// node into a keyed slot.
let private placeInSlot (id: string) : SkeletonOp<KNode, string> = UpdateNode(k "sw" [] [ "on", leaf id ])

let private applyAll (ops: SkeletonOp<KNode, string> list) (root: KNode) =
    ops
    |> List.fold (fun acc op -> acc |> Result.bind (Ops.applyContainedKeyed keyw canHoldAll knodew idw op)) (Ok root)

let rec private findNode (id: string) (n: KNode) : KNode option =
    if n.Id = id then
        Some n
    else
        (n.Children @ (n.Cases |> List.map snd)) |> List.tryPick (findNode id)

let private slotOf (root: KNode) =
    findNode "sw" root
    |> Option.map (fun sw -> sw.Cases |> List.map (fun (key, c) -> key, c.Id))

let private sw: StreamWitness<SkeletonOp<KNode, string>, KNode, Rejection<string>> =
    { Apply = fun op st -> Ops.applyContainedKeyed keyw canHoldAll knodew idw op st
      Encode = sprintf "%A"
      Decode = fun _ -> Error "unused" }

/// Two lanes forked off one base node, each a single-op chain under its own actor.
let private forkDag (a: SkeletonOp<KNode, string> list) (b: SkeletonOp<KNode, string> list) =
    let h = OpStream.defaultHash

    let chain actor ops parent d0 =
        ops
        |> List.fold
            (fun (head, d) op ->
                let id, d' = Dag.append h sw (Human actor) op head d |> Reference.built
                id, d')
            (parent, d0)

    let baseId, d1 =
        Dag.append h sw (Human "base") (Batch []) "" Dag.empty |> Reference.built

    let headA, d2 = chain "lane-a" a baseId d1
    let headB, dag = chain "lane-b" b baseId d2
    baseId, headA, headB, dag

let private shapes (cs: MergeConflict<'Op> list) =
    cs |> List.map (fun c -> c.Shape, c.Address)

[<Tests>]
let tests =
    testList
        "Phase 334 — a keyed-slot clash already halts the fold"
        [ testCase "each lane places a different node into the one slot, and the two orders disagree"
          <| fun _ ->
              let a = [ placeInSlot "a" ]
              let b = [ placeInSlot "b" ]

              Expect.equal
                  (applyAll a (doc ()) |> Result.map slotOf)
                  (Ok(Some [ "on", "a" ]))
                  "lane a alone puts a in the slot"

              Expect.equal
                  (applyAll b (doc ()) |> Result.map slotOf)
                  (Ok(Some [ "on", "b" ]))
                  "lane b alone puts b in the slot"

              Expect.notEqual
                  (applyAll (a @ b) (doc ()) |> Result.map slotOf)
                  (applyAll (b @ a) (doc ()) |> Result.map slotOf)
                  "only one can win: the sequential orders leave different nodes in the slot"

          testCase "Dag.reconcile halts the pair at the holder under the keyed footprint"
          <| fun _ ->
              let baseId, headA, headB, dag = forkDag [ placeInSlot "a" ] [ placeInSlot "b" ]

              let fpK op =
                  Ops.footprintKeyed keyw knodew idw [ op ]

              match Dag.reconcile fpK dag baseId headA headB with
              | Ok script -> failtestf "two writes to one keyed slot folded clean: %A" script
              | Error cs ->
                  Expect.contains
                      (shapes cs)
                      (MergeConflictShape.ConcurrentUpdate, "sw")
                      "reported as a concurrent update of the holder"

          testCase "and under the unkeyed footprint too: the holder is content-written either way"
          <| fun _ ->
              let baseId, headA, headB, dag = forkDag [ placeInSlot "a" ] [ placeInSlot "b" ]
              let fp op = Ops.footprint knodew idw [ op ]

              match Dag.reconcile fp dag baseId headA headB with
              | Ok script -> failtestf "two writes to one keyed slot folded clean: %A" script
              | Error cs ->
                  Expect.contains (shapes cs) (MergeConflictShape.ConcurrentUpdate, "sw") "halted at the holder"

              Expect.isFalse
                  (Ops.independent (fp (placeInSlot "a")) (fp (placeInSlot "b")))
                  "arbitration and the fold agree: the pair is not independent"

          testCase "the footprint builders a domain op lowers a slot write into all name the holder"
          <| fun _ ->
              // A domain op `place child into (holder, key)`, encoded with the builders Core publishes.
              let asInsert child = Footprint.insertUnder "sw" child

              let asEdit child =
                  Footprint.union (Footprint.contentEdit "sw") (Footprint.reading [ child ])

              Expect.equal
                  (shapes (Dag.conflicts id [ asInsert "a" ] [ asInsert "b" ]))
                  [ MergeConflictShape.InsertPositionClash, "sw" ]
                  "as an insert under the holder: a position clash at the holder"

              Expect.equal
                  (shapes (Dag.conflicts id [ asEdit "a" ] [ asEdit "b" ]))
                  [ MergeConflictShape.ConcurrentUpdate, "sw" ]
                  "as an edit of the holder: a concurrent update of the holder"

          testCase "teeth: a footprint that drops the holder folds the pair clean, so the cases above can see it"
          <| fun _ ->
              // The falsifier the cases above are measured against: erase the holder from every
              // address set and the same DAG reconciles, to a script whose result depends on the
              // order the lanes happened to be composed in.
              let baseId, headA, headB, dag = forkDag [ placeInSlot "a" ] [ placeInSlot "b" ]

              let blind op =
                  let f = Ops.footprintKeyed keyw knodew idw [ op ]

                  { f with
                      Reads = Set.remove "sw" f.Reads
                      ContentWrites = Set.remove "sw" f.ContentWrites
                      UnknownParentWrites = Set.remove "sw" f.UnknownParentWrites }

              match Dag.reconcile blind dag baseId headA headB with
              | Ok script ->
                  Expect.equal
                      (applyAll script (doc ()) |> Result.map slotOf)
                      (Ok(Some [ "on", "b" ]))
                      "the last lane wins"
              | Error cs -> failtestf "the blind footprint still halted: %A" cs ]
