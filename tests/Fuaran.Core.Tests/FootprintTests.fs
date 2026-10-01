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
        [ testCase "dropping the ordinal did NOT move the footprint — it never read one"
          <| fun _ ->
              // Pinned deliberately. When `InsertChild` / `MoveNode` lost their integer
              // position, `footprint` was the one part of the op algebra that needed no
              // change at all: it records WHICH parent's child-list an op writes, never
              // where in it. A future reader comparing the before/after shapes should not
              // conclude the analysis was left behind — this asserts it was already
              // id-only, so there was nothing to update.
              let ins = Ops.footprint nodew idw [ InsertChild("a", RNode.leaf "n" "para" "x") ]

              let mov = Ops.footprint nodew idw [ MoveNode("a", "b") ]

              Expect.equal ins.StructureWrites (setOf [ "a" ]) "insert writes the named parent's child-list"
              Expect.equal mov.StructureWrites (setOf [ "b" ]) "move writes the destination child-list"

              Expect.equal
                  mov.UnknownParentWrites
                  (setOf [ "a" ])
                  "the move's SOURCE parent stays the conservative unknown — unchanged by this phase"

          testCase "InsertChild footprints the parent (structure + read) and the inserted subtree (content)"
          <| fun _ ->
              let fp =
                  Ops.footprint nodew idw [ InsertChild("a", RNode.node "n" "para" [ RNode.leaf "n1" "para" "x" ]) ]

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
              let fp = Ops.footprint nodew idw [ MoveNode("a", "b") ]
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
                  Ops.footprint nodew idw [ Batch [ InsertChild("a", RNode.leaf "x" "para" "v"); RemoveNode "b" ] ]

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
              let a = Ops.footprint nodew idw [ InsertChild("a", RNode.leaf "x" "para" "v") ]
              let b = Ops.footprint nodew idw [ InsertChild("b", RNode.leaf "y" "para" "w") ]
              Expect.isTrue (Ops.independent a b) "disjoint parents + disjoint new ids commute"

          testCase "two inserts under the SAME parent are NOT independent (the pinned same-parent rule)"
          <| fun _ ->
              let a = Ops.footprint nodew idw [ InsertChild("a", RNode.leaf "x" "para" "v") ]
              let b = Ops.footprint nodew idw [ InsertChild("a", RNode.leaf "y" "para" "w") ]
              Expect.isFalse (Ops.independent a b) "both shift a's siblings"

          testCase "a reorder and an insert under different parents are independent"
          <| fun _ ->
              let a = Ops.footprint nodew idw [ ReorderChildren("a", [ "a2"; "a1" ]) ]
              let b = Ops.footprint nodew idw [ InsertChild("b", RNode.leaf "y" "para" "w") ]
              Expect.isTrue (Ops.independent a b) "reorder a's kids vs insert under b commute"

          testCase "a remove conflicts with any concurrent structural write (unknown-parent over-approx)"
          <| fun _ ->
              // disjoint by NAMED ids, but the removed node's parent is unknown — conservatively dependent.
              let a = Ops.footprint nodew idw [ RemoveNode "a1" ]
              let b = Ops.footprint nodew idw [ InsertChild("b", RNode.leaf "y" "para" "w") ]
              Expect.isFalse (Ops.independent a b) "a remove is only independent of a structure-free script"

          testCase "creating a node the other script uses as a structural anchor conflicts (content-vs-read)"
          <| fun _ ->
              let a = Ops.footprint nodew idw [ InsertChild("a", RNode.leaf "q" "para" "v") ]
              let b = Ops.footprint nodew idw [ InsertChild("q", RNode.leaf "y" "para" "w") ]
              Expect.isFalse (Ops.independent a b) "A authors q; B reads q as its parent"

          testCase "inserting and removing the SAME id conflict (content-vs-content)"
          <| fun _ ->
              let a = Ops.footprint nodew idw [ InsertChild("a", RNode.leaf "z" "para" "v") ]
              let b = Ops.footprint nodew idw [ RemoveNode "z" ]
              Expect.isFalse (Ops.independent a b) "both touch z's lifecycle" ]

[<Tests>]
let commutationTests =
    testList
        "Ops.footprint commutation"
        [ testCase "a declared-independent pair genuinely commutes under apply"
          <| fun _ ->
              let tree = sample () // root[a[a1,a2], b[b1]]
              let a = [ InsertChild("a", RNode.leaf "x" "para" "v") ]
              let b = [ InsertChild("b", RNode.leaf "y" "para" "w") ]
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

          testCase "a declared-DEPENDENT pair of appends genuinely does NOT commute (justifies the flag)"
          <| fun _ ->
              // Two inserts appending to the same parent: membership commutes, ORDER
              // does not — whichever lands second is last. So `independent = false` is
              // not merely conservative here, it is required. This is the append
              // analogue of the index-sensitive pair it replaces.
              let tree = flat ()
              let a = [ InsertChild("p", RNode.leaf "c" "para" "3") ]
              let b = [ InsertChild("p", RNode.leaf "d" "para" "4") ]
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

              Expect.notEqual ab ba "the two orders diverge — so footprint MUST call them dependent"

          testCase "removing the ordinal made remove/insert COMMUTE — and independent stays conservatively false"
          <| fun _ ->
              // This pair was the old exhibit for genuine non-commutation: `p[a,b]`,
              // remove `a` against insert `c` AT INDEX 1. It diverged only because the
              // insert named a position that the remove shifted underneath it.
              //
              // With the ordinal gone the insert appends, and the two orders now yield
              // the same tree. That is a real gain for concurrent editing: a class of
              // pairs that used to conflict genuinely commutes.
              //
              // `footprint` still declares them dependent, because it is a deliberate
              // over-approximation keyed on the parent id and does not look at the tree.
              // Pinned here so the conservatism is a recorded choice rather than a
              // suspected bug the next reader tries to "fix".
              let tree = flat ()
              let a = [ RemoveNode "a" ]
              let b = [ InsertChild("p", RNode.leaf "c" "para" "3") ]

              Expect.isFalse
                  (Ops.independent (Ops.footprint nodew idw a) (Ops.footprint nodew idw b))
                  "still declared dependent — conservative, and safe"

              let hashOf = Tree.encodeHash nodew encNode

              let ab =
                  Ops.applyAll nodew idw a tree
                  |> Result.bind (fun t -> Ops.applyAll nodew idw b t)
                  |> Result.map hashOf

              let ba =
                  Ops.applyAll nodew idw b tree
                  |> Result.bind (fun t -> Ops.applyAll nodew idw a t)
                  |> Result.map hashOf

              Expect.equal ab ba "append semantics make the two orders agree" ]

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

              Expect.equal
                  (List.length results)
                  4
                  "soundness + monotonicity + determinism laws + the Phase 121 independence adequacy guard reported"

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

// ---- Phase 248 — Ops.interference: the clause behind a dependent verdict ----

/// `Ops.independent` exactly as it read before Phase 248 redefined it over `interference` — the
/// clause-by-clause conjunction, kept here so the redefinition is held to the verdict it replaced.
let private independentBefore248 (a: Footprint) (b: Footprint) =
    let disjoint x y = Set.isEmpty (Set.intersect x y)

    let hasStructural (f: Footprint) =
        not (Set.isEmpty f.StructureWrites && Set.isEmpty f.UnknownParentWrites)

    disjoint a.ContentWrites b.ContentWrites
    && disjoint a.ContentWrites b.Reads
    && disjoint b.ContentWrites a.Reads
    && disjoint a.StructureWrites b.StructureWrites
    && not (not (Set.isEmpty a.UnknownParentWrites) && hasStructural b)
    && not (not (Set.isEmpty b.UnknownParentWrites) && hasStructural a)

/// Every footprint over a two-address universe: each of the four sets is one of the four subsets of
/// {x, y}, so every overlap shape a clause can see (none, one address, both) occurs — 256 footprints.
let private everyFootprint =
    let subsets = [ Set.empty; setOf [ "x" ]; setOf [ "y" ]; setOf [ "x"; "y" ] ]

    [ for r in subsets do
          for s in subsets do
              for c in subsets do
                  for u in subsets do
                      { Reads = r
                        StructureWrites = s
                        ContentWrites = c
                        UnknownParentWrites = u } ]

/// The clause as the other side reads it: overlaps are their own mirror, directional clauses swap.
let private mirror (i: Interference) =
    match i with
    | Interference.SameTarget t -> Interference.SameTarget t
    | Interference.LeftWritesRightReads xs -> Interference.RightWritesLeftReads xs
    | Interference.RightWritesLeftReads xs -> Interference.LeftWritesRightReads xs
    | Interference.SameParent ps -> Interference.SameParent ps
    | Interference.LeftUnknownParent(relocated, structural) -> Interference.RightUnknownParent(structural, relocated)
    | Interference.RightUnknownParent(structural, relocated) -> Interference.LeftUnknownParent(relocated, structural)

let private clauseName (i: Interference) =
    match i with
    | Interference.SameTarget _ -> "SameTarget"
    | Interference.LeftWritesRightReads _ -> "LeftWritesRightReads"
    | Interference.RightWritesLeftReads _ -> "RightWritesLeftReads"
    | Interference.SameParent _ -> "SameParent"
    | Interference.LeftUnknownParent _ -> "LeftUnknownParent"
    | Interference.RightUnknownParent _ -> "RightUnknownParent"

[<Tests>]
let interferenceTests =
    testList
        "Ops.interference"
        [ testCase "independent is interference = [] and agrees with the pre-248 conjunction on every pair"
          <| fun _ ->
              let mutable pairs = 0
              let mutable reached = Set.empty

              for a in everyFootprint do
                  for b in everyFootprint do
                      pairs <- pairs + 1
                      let clauses = Ops.interference a b
                      reached <- clauses |> List.map clauseName |> Set.ofList |> Set.union reached

                      if Ops.independent a b <> List.isEmpty clauses then
                          failtestf "independent disagrees with interference at %A / %A" a b

                      if Ops.independent a b <> independentBefore248 a b then
                          failtestf "the redefinition moved the verdict at %A / %A" a b

              Expect.equal pairs 65536 "every ordered pair of the 256 footprints was judged"

              // the sweep is not vacuous: every clause fired somewhere in it.
              Expect.equal
                  reached
                  (setOf
                      [ "SameTarget"
                        "LeftWritesRightReads"
                        "RightWritesLeftReads"
                        "SameParent"
                        "LeftUnknownParent"
                        "RightUnknownParent" ])
                  "every clause was reached"

          testCase "each clause carries exactly the addresses its definition names, at most once, in order"
          <| fun _ ->
              let structural (f: Footprint) =
                  Set.union f.StructureWrites f.UnknownParentWrites

              for a in everyFootprint do
                  for b in everyFootprint do
                      let clauses = Ops.interference a b

                      let expected =
                          [ let st = Set.intersect a.ContentWrites b.ContentWrites

                            if not st.IsEmpty then
                                Interference.SameTarget st

                            let lw = Set.intersect a.ContentWrites b.Reads

                            if not lw.IsEmpty then
                                Interference.LeftWritesRightReads lw

                            let rw = Set.intersect a.Reads b.ContentWrites

                            if not rw.IsEmpty then
                                Interference.RightWritesLeftReads rw

                            let sp = Set.intersect a.StructureWrites b.StructureWrites

                            if not sp.IsEmpty then
                                Interference.SameParent sp

                            if not a.UnknownParentWrites.IsEmpty && not (structural b).IsEmpty then
                                Interference.LeftUnknownParent(a.UnknownParentWrites, structural b)

                            if not b.UnknownParentWrites.IsEmpty && not (structural a).IsEmpty then
                                Interference.RightUnknownParent(structural a, b.UnknownParentWrites) ]

                      if clauses <> expected then
                          failtestf "interference %A / %A = %A, expected %A" a b clauses expected

          testCase "swapping the sides mirrors the clauses (interference b a = mirror of interference a b)"
          <| fun _ ->
              for a in everyFootprint do
                  for b in everyFootprint do
                      let forward = Ops.interference a b |> List.map mirror |> Set.ofList
                      let backward = Ops.interference b a |> Set.ofList

                      if forward <> backward then
                          failtestf "not mirror-symmetric at %A / %A" a b

          testCase "concrete scripts name the clause and the addresses"
          <| fun _ ->
              let fp ops = Ops.footprint nodew idw ops

              let ins parent id =
                  InsertChild(parent, RNode.leaf id "para" "v")

              // two inserts under one parent: the pinned same-parent rule, on that parent.
              Expect.equal
                  (Ops.interference (fp [ ins "a" "x" ]) (fp [ ins "a" "y" ]))
                  [ Interference.SameParent(setOf [ "a" ]) ]
                  "same parent, named"

              // a remove against an insert elsewhere: the unknown-parent clause, both sides' addresses.
              Expect.equal
                  (Ops.interference (fp [ RemoveNode "b1" ]) (fp [ ins "a" "x" ]))
                  [ Interference.LeftUnknownParent(setOf [ "b1" ], setOf [ "a" ]) ]
                  "the remove relocates b1; the insert writes a's child list"

              // one script authors q, the other inserts under q: content against read, on q.
              Expect.equal
                  (Ops.interference (fp [ ins "a" "q" ]) (fp [ ins "q" "r" ]))
                  [ Interference.LeftWritesRightReads(setOf [ "q" ]) ]
                  "A authors q; B reads q as its parent"

              // independent scripts explain nothing.
              Expect.isEmpty (Ops.interference (fp [ ins "a" "x" ]) (fp [ ins "b" "y" ])) "disjoint inserts" ]
