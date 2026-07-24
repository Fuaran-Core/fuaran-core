module Fuaran.Core.Tests.FootprintTests

// Phase 78 — Ops.footprint / Ops.independent: per-op footprint cases, the pinned
// same-parent + unknown-parent conservative rules, concrete independence/commutation
// cases, and the generative footprintLaws (soundness / monotonicity / determinism).

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

let private setOf (xs: string list) = Set.ofList xs

/// A small flat tree: p(root) with leaf children a, b.
let private flat () =
    RNode.node "p" "root" [ RNode.leaf "a" "para" "1"; RNode.leaf "b" "para" "2" ]

[<Tests>]
let footprintTests =
    testList
        "Ops.footprint"
        [ testCase "InsertChild footprints the parent (structure + read) and the inserted subtree (content)"
          <| fun _ ->
              let fp =
                  Ops.footprint nodew idw [ InsertChild("a", 0, RNode.node "n" "para" [ RNode.leaf "n1" "para" "x" ]) ]

              Expect.equal fp.StructureWrites (setOf [ "a" ]) "the parent's child-list is a structure-write"
              Expect.equal fp.ContentWrites (setOf [ "n"; "n1" ]) "the whole inserted subtree is content-written"
              Expect.equal fp.Reads (setOf [ "a"; "n"; "n1" ]) "parent existence + inserted-id dup-check are reads"
              Expect.isEmpty fp.UnknownParentWrites "an insert names its parent — no unknown structural anchor"

          testCase "RemoveNode footprints the target as content + an UNKNOWN-parent structure-write"
          <| fun _ ->
              let fp = Ops.footprint nodew idw [ RemoveNode "a" ]
              Expect.equal fp.ContentWrites (setOf [ "a" ]) "the removed node is content-written"
              Expect.equal fp.UnknownParentWrites (setOf [ "a" ]) "its source parent is unknown from the script"
              Expect.isEmpty fp.StructureWrites "no NAMED structural anchor — the parent isn't in the op"

          testCase "MoveNode footprints the known destination + the unknown source"
          <| fun _ ->
              let fp = Ops.footprint nodew idw [ MoveNode("a", "b", 0) ]
              Expect.equal fp.StructureWrites (setOf [ "b" ]) "the destination child-list is a known structure-write"
              Expect.equal fp.ContentWrites (setOf [ "a" ]) "the relocated node is content-written"
              Expect.equal fp.UnknownParentWrites (setOf [ "a" ]) "the source parent is the unknown over-approx"
              Expect.equal fp.Reads (setOf [ "a"; "b" ]) "target + new parent are read"

          testCase "ReorderChildren footprints the parent (structure) and names its children (read)"
          <| fun _ ->
              let fp = Ops.footprint nodew idw [ ReorderChildren("p", [ "b"; "a" ]) ]
              Expect.equal fp.StructureWrites (setOf [ "p" ]) "the parent's child-list order is rewritten"
              Expect.equal fp.Reads (setOf [ "p"; "a"; "b" ]) "the parent + its named children are read"
              Expect.isEmpty fp.ContentWrites "a reorder touches no node's content"
              Expect.isEmpty fp.UnknownParentWrites "the reordered parent is named — no unknown anchor"

          testCase "Batch unions its inner ops' footprints"
          <| fun _ ->
              let fp =
                  Ops.footprint nodew idw [ Batch [ InsertChild("a", 0, RNode.leaf "x" "para" "v"); RemoveNode "b" ] ]

              Expect.equal fp.ContentWrites (setOf [ "x"; "b" ]) "insert + remove content-writes unioned"
              Expect.equal fp.StructureWrites (setOf [ "a" ]) "the insert's parent"
              Expect.equal fp.UnknownParentWrites (setOf [ "b" ]) "the remove's unknown parent"

          testCase "the empty script has the empty footprint (independent of everything)"
          <| fun _ ->
              let fp = Ops.footprint nodew idw []
              Expect.isEmpty fp.Reads "empty"
              Expect.isEmpty fp.StructureWrites "empty"
              Expect.isEmpty fp.ContentWrites "empty"
              Expect.isEmpty fp.UnknownParentWrites "empty"
              Expect.isTrue (Ops.independent fp (Ops.footprint nodew idw [ RemoveNode "a" ])) "empty ⊥ anything" ]

[<Tests>]
let independenceTests =
    testList
        "Ops.independent"
        [ testCase "inserts under DIFFERENT named parents are independent"
          <| fun _ ->
              let a = Ops.footprint nodew idw [ InsertChild("a", 0, RNode.leaf "x" "para" "v") ]
              let b = Ops.footprint nodew idw [ InsertChild("b", 0, RNode.leaf "y" "para" "w") ]
              Expect.isTrue (Ops.independent a b) "disjoint parents + disjoint new ids commute"

          testCase "two inserts under the SAME parent are NOT independent (the pinned same-parent rule)"
          <| fun _ ->
              let a = Ops.footprint nodew idw [ InsertChild("a", 0, RNode.leaf "x" "para" "v") ]
              let b = Ops.footprint nodew idw [ InsertChild("a", 1, RNode.leaf "y" "para" "w") ]
              Expect.isFalse (Ops.independent a b) "both shift a's siblings"

          testCase "a reorder and an insert under different parents are independent"
          <| fun _ ->
              let a = Ops.footprint nodew idw [ ReorderChildren("a", [ "a2"; "a1" ]) ]
              let b = Ops.footprint nodew idw [ InsertChild("b", 0, RNode.leaf "y" "para" "w") ]
              Expect.isTrue (Ops.independent a b) "reorder a's kids vs insert under b commute"

          testCase "a remove conflicts with any concurrent structural write (unknown-parent over-approx)"
          <| fun _ ->
              // disjoint by NAMED ids, but the removed node's parent is unknown — conservatively dependent.
              let a = Ops.footprint nodew idw [ RemoveNode "a1" ]
              let b = Ops.footprint nodew idw [ InsertChild("b", 0, RNode.leaf "y" "para" "w") ]
              Expect.isFalse (Ops.independent a b) "a remove is only independent of a structure-free script"

          testCase "creating a node the other script uses as a structural anchor conflicts (content-vs-read)"
          <| fun _ ->
              let a = Ops.footprint nodew idw [ InsertChild("a", 0, RNode.leaf "q" "para" "v") ]
              let b = Ops.footprint nodew idw [ InsertChild("q", 0, RNode.leaf "y" "para" "w") ]
              Expect.isFalse (Ops.independent a b) "A authors q; B reads q as its parent"

          testCase "inserting and removing the SAME id conflict (content-vs-content)"
          <| fun _ ->
              let a = Ops.footprint nodew idw [ InsertChild("a", 0, RNode.leaf "z" "para" "v") ]
              let b = Ops.footprint nodew idw [ RemoveNode "z" ]
              Expect.isFalse (Ops.independent a b) "both touch z's lifecycle" ]

[<Tests>]
let commutationTests =
    testList
        "Ops.footprint commutation"
        [ testCase "a declared-independent pair genuinely commutes under apply"
          <| fun _ ->
              let tree = sample () // root[a[a1,a2], b[b1]]
              let a = [ InsertChild("a", 0, RNode.leaf "x" "para" "v") ]
              let b = [ InsertChild("b", 0, RNode.leaf "y" "para" "w") ]
              Expect.isTrue (Ops.independent (Ops.footprint nodew idw a) (Ops.footprint nodew idw b)) "independent"

              let hashOf = Tree.encodeHash nodew encNode

              let ab =
                  Ops.applyAll nodew idw a tree
                  |> Result.bind (fun t -> Ops.applyAll nodew idw b t)
                  |> Result.map hashOf

              let ba =
                  Ops.applyAll nodew idw b tree
                  |> Result.bind (fun t -> Ops.applyAll nodew idw a t)
                  |> Result.map hashOf

              Expect.equal ab ba "both orders yield the same tree (content hash)"

          testCase "a declared-DEPENDENT remove/insert pair genuinely does NOT commute (justifies the flag)"
          <| fun _ ->
              // p[a,b]; remove a vs insert c at index 1 — index-sensitive, so order matters.
              let tree = flat ()
              let a = [ RemoveNode "a" ]
              let b = [ InsertChild("p", 1, RNode.leaf "c" "para" "3") ]
              Expect.isFalse (Ops.independent (Ops.footprint nodew idw a) (Ops.footprint nodew idw b)) "dependent"

              let hashOf = Tree.encodeHash nodew encNode

              let ab =
                  Ops.applyAll nodew idw a tree
                  |> Result.bind (fun t -> Ops.applyAll nodew idw b t)
                  |> Result.map hashOf

              let ba =
                  Ops.applyAll nodew idw b tree
                  |> Result.bind (fun t -> Ops.applyAll nodew idw a t)
                  |> Result.map hashOf

              Expect.notEqual ab ba "the two orders diverge — so footprint MUST call them dependent" ]

// ---- the generative footprintLaws (soundness / monotonicity / determinism) ----

let private genTree (rng: ConfRng.T) : RNode * ConfRng.T =
    let mutable counter = 0
    let mutable r = rng

    let freshId () =
        let id = sprintf "n%d" counter
        counter <- counter + 1
        id

    let rec build depth =
        let id = freshId ()

        if depth <= 0 then
            RNode.leaf id "para" "v"
        else
            let nKids, r' = ConfRng.intBelow 3 r
            r <- r'
            RNode.node id "section" [ for _ in 1..nKids -> build (depth - 1) ]

    let t = build 2
    t, r

let private genFresh (existing: Set<string>) (rng: ConfRng.T) : RNode * ConfRng.T =
    let mutable r = rng

    let rec pick () =
        let v, r' = ConfRng.next r
        r <- r'
        let id = sprintf "f%d" (v % 100000)
        if existing.Contains id then pick () else id

    RNode.leaf (pick ()) "para" "x", r

let private opGen: OpGen<RNode, string> =
    { Tree = genTree
      FreshNode = genFresh
      CanHold = None }

[<Tests>]
let footprintLawTests =
    testList
        "Conformance.footprintLaws"
        [ testCase "the reference witness certifies footprint soundness + monotonicity + determinism green"
          <| fun _ ->
              let results = Conformance.footprintLaws nodew idw opGen encNode 4242 300
              Expect.equal (List.length results) 3 "soundness + monotonicity + determinism laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "footprintLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism of the kit itself
              Expect.equal
                  (Conformance.footprintLaws nodew idw opGen encNode 4242 300)
                  results
                  "same seed ⇒ identical report" ]
