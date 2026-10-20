module Fuaran.Core.Tests.ProofOracle.TreeDiffTests

// The `Proofs.Oracle` differential for `proofs/TreeDiff.fst`: the extracted model
// (`proofs/oracle/TreeDiff.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges

// ---------------------------------------------------------------------------
//  Phase 141 — THE DIFF, over PAIRS NOBODY DERIVED FROM ONE ANOTHER.
//
//  `Conformance.diffLaws` has certified `Diff.toOps` since Phase 03, and its sample is narrower
//  than its claim: it builds `before`, then derives `after` by APPLYING random ops to it. So every
//  pair it has ever diffed is one the algebra can already reach, and the interesting half of the
//  claim — that a diff exists between two trees nobody built from one another — was never sampled.
//  This generator draws the two trees INDEPENDENTLY over a shared id space, which is what the
//  theorem in `proofs/TreeDiff.fst` quantifies over.
//
//  ONE CONSTRAINT ON THE PAIR, AND IT IS A PROPERTY OF THE PROBLEM RATHER THAN OF THE GENERATOR.
//  A node's CONTENT is a function of its id, so the two trees agree on every id they share. Skeleton
//  ops relocate and delete nodes; they cannot edit one (`Core.Ops`' remit, stated in `toOps`' own doc
//  comment), so a pair whose shared id carries a different kind in each tree is unreconstructible by
//  ANY skeleton script and asking for one is asking the wrong question. Measured before it was
//  believed: a first generator that redrew each tree's kinds independently reconstructed 1,738 of
//  4,000 pairs; with content keyed to the id, 20,000 of 20,000.
//
//  SIX COMPARISONS, per (before, after, predicate):
//    1. `Diff.toOps` vs the extracted `TreeDiff.to_ops` — verdict, error class, and the SCRIPT
//       operation for operation, not merely its length.
//    2. RECONSTRUCTION through production's own `Tree.encodeHash`: `applyAll (toOps b a) b` is `a`.
//    3. APPLICABILITY: `canApplyAll` accepts every emitted step.
//    4. THE PROVED SHAPE, asserted on the shipped script: the extracted `script_shape` — the
//       conjunction of the four block characterisations `diff_script_shape` proves — is asked of
//       every operation production emitted. That is the theorem held to the engine rather than to
//       the model of it.
//    5. `Diff.toOps` under `OpScope.contained` vs `TreeDiff.to_ops_contained` under a DRAWN predicate, and where it
//       produces a script, that script certified through `Ops.canApplyAll` under `OpScope.contained` / `Ops.applyAll` under `OpScope.contained` — the
//       container-aware pair Phase 160 added, which is the executor a contained script belongs to.
//       This is `diff_applicable_contained` asked of the shipped engine.
//    6. where a `TargetNotAContainer` is raised, that the node it names is one `after` really
//       carries, that really has children, and that the predicate really refuses.
//
//  TWO GO-REDS, one per direction the phase claims something.
//    * THE ORDER. `diff_emission_order` proves the script is four homogeneous blocks —
//      inserts, moves, removes, reorders — so a stable partition by operation kind RECOVERS those
//      blocks exactly, and reassembling them with the removes FIRST is "the same four passes in the
//      wrong order". If that permutation reconstructed as often as the real one, the order the
//      source comment argues for would not be load-bearing and the theorem would be about nothing.
//    * THE CONTAINER CHECK. Substituting the model's PLAIN `to_ops` into the contained slot is an
//      engine that "emits an insert under a non-container" because it never looked, and it must
//      disagree with the shipped `Diff.toOps` under `OpScope.contained` on exactly the pairs whose `after` nests under a
//      leaf.
// ---------------------------------------------------------------------------

/// The shared id space a pair is drawn from. Eight ids plus the root: small enough that two
/// independent draws overlap heavily (so survivors, additions and removals all occur in one pair),
/// large enough that the shapes are not enumerable by accident.
let private diffPool = [ for i in 1..8 -> sprintf "d%d" i ]

/// A node's content, as a function of its id alone — see the constraint above. Deliberately NOT
/// `GetHashCode`: .NET randomises string hashing per process, so a generator keyed off it would
/// draw a different pool on every run and a failure would not reproduce from its seed.
let private diffKindOf (i: string) =
    if i = "root" then
        "doc"
    else
        containerKinds[(int (i.Substring 1)) % List.length containerKinds]

/// One well-formed tree over a random SUBSET of the pool, grown by attaching each drawn id under a
/// uniformly random already-placed node. Nothing about it consults the other tree of the pair.
let private genPairTree (r0: ConfRng.T) : RNode * ConfRng.T =
    let mutable r = r0
    let arr = List.toArray diffPool

    for i in (arr.Length - 1) .. -1 .. 1 do
        let j, r' = ConfRng.intBelow (i + 1) r
        r <- r'
        let t = arr[i]
        arr[i] <- arr[j]
        arr[j] <- t

    let take, r1 = ConfRng.intBelow (arr.Length + 1) r
    r <- r1
    let chosen = arr |> Array.truncate take |> Array.toList

    let mutable placed = [ "root" ]
    let mutable kidsOf = Map.ofList [ "root", ([]: string list) ]

    for c in chosen do
        let pi, r' = ConfRng.intBelow (List.length placed) r
        r <- r'
        let p = placed[pi]
        kidsOf <- kidsOf |> Map.add p (kidsOf[p] @ [ c ]) |> Map.add c []
        placed <- placed @ [ c ]

    let rec build i =
        { RNode.leaf i (diffKindOf i) "v" with
            Children = kidsOf[i] |> List.map build }

    build "root", r

/// The contained-diff seam, so the go-red can be substituted for it.
type private ContainedDiff =
    (TreeOps.tree -> bool) -> TreeOps.tree -> TreeOps.tree -> DagFold.outcome<TreeOps.op list, TreeDiff.diff_error>

/// The container go-red: the model's own PLAIN diff, lifted into the contained signature by
/// discarding the predicate. It is a weakening by construction — `to_ops_contained` at a predicate
/// that refuses nothing IS `to_ops` (`diff_contained_at_total_is_plain`), so this is that instance
/// handed a predicate that refuses something.
let private diffGoRedContained: ContainedDiff = fun _ b a -> TreeDiff.to_ops b a

/// The order go-red: the same four blocks with the REMOVES FIRST. A stable partition by operation
/// kind recovers the blocks because `diff_emission_order` proves there are exactly four of them, in
/// that order — so this is a permutation of the passes and not a different algorithm.
let private removesFirst (ops: SkeletonOp<RNode, string> list) =
    let pick f = ops |> List.filter f

    pick (function
        | RemoveNode _ -> true
        | _ -> false)
    @ pick (function
        | InsertChild _ -> true
        | _ -> false)
    @ pick (function
        | MoveNode _ -> true
        | _ -> false)
    @ pick (function
        | ReorderChildren _ -> true
        | _ -> false)

/// Phase 167 — the go-red for `diff_reconstructs`' HYPOTHESIS. `diffKindOf` keys a node's content
/// to its id, which since Phase 167 is the theorem's own `kinds_agree` precondition and not merely
/// a property of this generator. This is a SECOND function of the same id: a shared id then names a
/// different kind in each tree, which is the one shape no STRUCTURAL diff script expresses — not
/// because no operation edits a node (`UpdateNode` has since Phase 250) but because `Diff.toOps`
/// reads no content and so never emits one; the content-aware `Diff.toOpsWith` does (Phase 305,
/// `ContentDiffTests`). `TreeDiff.kinds_agree_is_necessary` pins that
/// counterexample in the model; the test below measures it on the shipped engine. The root is left
/// at "doc" in both trees deliberately — a pair whose ROOTS disagree would fail to reconstruct
/// without ever reaching a shared child, which would measure the wrong thing.
let private diffKindShifted (i: string) =
    if i = "root" then
        "doc"
    else
        containerKinds[((int (i.Substring 1)) + 1) % List.length containerKinds]

/// Re-kind every node of a generated tree through `kindOf`, leaving the shape and the ids exactly
/// as the generator drew them — so the probe differs from the green run in the KINDS and in nothing
/// else.
let rec private reKind (kindOf: string -> string) (n: RNode) : RNode =
    { n with
        Kind = kindOf n.Id
        Children = n.Children |> List.map (reKind kindOf) }

let rec private idsOfTree (n: RNode) : string list =
    n.Id :: (n.Children |> List.collect idsOfTree)

let private prodDiffRender (r: Result<SkeletonOp<RNode, string> list, Rejection<string>>) =
    match r with
    | Ok ops -> "ok:" + (ops |> List.map renderProdOp |> String.concat ";")
    | Error(RootIdMismatch(b, a)) -> sprintf "err:RootIdMismatch(%s,%s)" b a
    | Error(DuplicateId d) -> sprintf "err:DuplicateIdInTree(%s)" d
    | Error(NotAContainer(p, k)) -> sprintf "err:TargetNotAContainer(%s,%s)" p k
    // Phase 313: raised only by `Diff.toOps` under `Policy.grammar`, which the oracle does not run.
    | Error(IllegalChild(c, _, p, _, _)) -> sprintf "err:IllegalChildInTree(%s,%s)" c p
    | Error other -> sprintf "err:%A" other

let private modelDiffRender (r: DagFold.outcome<TreeOps.op list, TreeDiff.diff_error>) =
    match r with
    | DagFold.Ok ops -> "ok:" + (ops |> List.map renderModelOp |> String.concat ";")
    | DagFold.Error(TreeDiff.RootIdMismatch(b, a)) -> sprintf "err:RootIdMismatch(%s,%s)" b a
    | DagFold.Error(TreeDiff.DuplicateIdInTree d) -> sprintf "err:DuplicateIdInTree(%s)" d
    | DagFold.Error(TreeDiff.TargetNotAContainer(p, k)) -> sprintf "err:TargetNotAContainer(%s,%s)" p k

type private DiffTally =
    {
        Diffs: string list
        Pairs: int
        /// pairs whose script carries at least one operation of each kind — counted per kind,
        /// because a pool that never removed anything could not have met the order go-red
        Inserted: int
        Removed: int
        Moved: int
        Reordered: int
        /// pairs the two draws made identical — the empty script, worth reaching at all
        Empty: int
        /// contained diffs that produced a script, and ones refused for containment
        Contained: int
        ContainerRefused: int
        /// pairs where the removes-first permutation of the SAME four blocks fails to reconstruct
        OrderBroken: int
        Predicates: Set<string>
    }

let private emptyDiffTally =
    { Diffs = []
      Pairs = 0
      Inserted = 0
      Removed = 0
      Moved = 0
      Reordered = 0
      Empty = 0
      Contained = 0
      ContainerRefused = 0
      OrderBroken = 0
      Predicates = Set.empty }

let private diffProbe
    (containedDiff: ContainedDiff)
    (kinds: Set<string>)
    (before: RNode)
    (after: RNode)
    (acc: DiffTally)
    : DiffTally =
    let canHold (n: RNode) = kinds.Contains(nodew.KindTag n)
    let mCanHold (t: TreeOps.tree) = kinds.Contains(mKind t)
    let mb = toModelTree before
    let ma = toModelTree after

    let where =
        sprintf "pair %s -> %s under canHold={%s}" (prodTreeHash before) (prodTreeHash after) (showKinds kinds)

    let prod = Diff.toOps Policy.none (OpScope.plain nodew idw) before after
    let model = TreeDiff.to_ops mb ma

    // 1. the verdict and the script, operation for operation
    let scriptDiff =
        if prodDiffRender prod <> modelDiffRender model then
            [ sprintf
                  "toOps differs — %s\n  production: %s\n  oracle:     %s"
                  where
                  (prodDiffRender prod)
                  (modelDiffRender model) ]
        else
            []

    // 2-4. reconstruction, applyability, and the PROVED shape asked of the shipped script
    let roundTripDiff, ins, rem, mov, reo, emptyScript, orderBroken =
        match prod with
        | Error e ->
            [ sprintf "toOps REFUSED an independently generated well-formed pair (%A) — %s" e where ], 0, 0, 0, 0, 0, 0
        | Ok ops ->
            let rebuilt = Ops.applyAll Policy.none (OpScope.plain nodew idw) ops before

            let reconstruction =
                match rebuilt with
                | Ok t when prodTreeHash t = prodTreeHash after -> []
                | Ok t -> [ sprintf "applyAll(toOps) did NOT reconstruct — %s\n  got: %s" where (prodTreeHash t) ]
                | Error { Applied = i; Rejection = e } ->
                    [ sprintf "applyAll(toOps) was REFUSED at step %d (%s) — %s" i (prodRejClass e) where ]

            let applyability =
                match Ops.canApplyAll Policy.none (OpScope.plain nodew idw) ops before with
                | Ok() -> []
                | Error(i, e) ->
                    [ sprintf "canApplyAll REFUSED the emitted script at step %d (%s) — %s" i (prodRejClass e) where ]

            // the theorem, asked of the engine: `script_shape` is the conjunction of the four block
            // characterisations `diff_script_shape` proves, evaluated over production's own output
            let shape =
                let mops = ops |> List.map (toModelOpWith toModelTree)

                if TreeDiff.all_ops (TreeDiff.script_shape mb ma) mops then
                    []
                else
                    [ sprintf
                          "the shipped script violates the PROVED block shape — %s\n  script: %s"
                          where
                          (ops |> List.map renderProdOp |> String.concat ";") ]

            let has f = ops |> List.exists f

            let broken =
                match Ops.applyAll Policy.none (OpScope.plain nodew idw) (removesFirst ops) before with
                | Ok t when prodTreeHash t = prodTreeHash after -> 0
                | _ -> 1

            reconstruction @ applyability @ shape,
            (if
                 has (function
                     | InsertChild _ -> true
                     | _ -> false)
             then
                 1
             else
                 0),
            (if
                 has (function
                     | RemoveNode _ -> true
                     | _ -> false)
             then
                 1
             else
                 0),
            (if
                 has (function
                     | MoveNode _ -> true
                     | _ -> false)
             then
                 1
             else
                 0),
            (if
                 has (function
                     | ReorderChildren _ -> true
                     | _ -> false)
             then
                 1
             else
                 0),
            (if List.isEmpty ops then 1 else 0),
            broken

    // 5-6. the container-aware mirror, and the executor a contained script belongs to
    let prodC =
        Diff.toOps Policy.none (OpScope.contained canHold nodew idw) before after

    let modelC = containedDiff mCanHold mb ma

    let containedDiffs, contained, refused =
        let agreement =
            if prodDiffRender prodC <> modelDiffRender modelC then
                [ sprintf
                      "toOpsContained differs — %s\n  production: %s\n  oracle:     %s"
                      where
                      (prodDiffRender prodC)
                      (modelDiffRender modelC) ]
            else
                []

        match prodC with
        | Ok ops ->
            let recon =
                match Ops.applyAll Policy.none (OpScope.contained canHold nodew idw) ops before with
                | Ok t when prodTreeHash t = prodTreeHash after -> []
                | Ok t ->
                    [ sprintf "applyAllWith(toOpsContained) did NOT reconstruct — %s\n  got: %s" where (prodTreeHash t) ]
                | Error { Applied = i; Rejection = e } ->
                    [ sprintf "applyAllWith REFUSED a contained script at step %d (%s) — %s" i (prodRejClass e) where ]

            let dry =
                match Ops.canApplyAll Policy.none (OpScope.contained canHold nodew idw) ops before with
                | Ok() -> []
                | Error(i, e) ->
                    [ sprintf
                          "canApplyAllWith REFUSED a contained script at step %d (%s) — the emitted parents were supposed to be containers — %s"
                          i
                          (prodRejClass e)
                          where ]

            agreement @ recon @ dry, 1, 0
        | Error(NotAContainer(p, k)) ->
            let located =
                match Tree.tryFind nodew idw p after with
                | Some n when not (List.isEmpty (nodew.Children n)) && not (canHold n) && nodew.KindTag n = k -> []
                | _ ->
                    [ sprintf
                          "TargetNotAContainer(%s,%s) does not locate a childful non-container of `after` — %s"
                          p
                          k
                          where ]

            agreement @ located, 0, 1
        | Error e ->
            agreement
            @ [ sprintf "toOpsContained refused with %A on a well-formed pair — %s" e where ],
            0,
            0

    { Diffs = acc.Diffs @ scriptDiff @ roundTripDiff @ containedDiffs
      Pairs = acc.Pairs + 1
      Inserted = acc.Inserted + ins
      Removed = acc.Removed + rem
      Moved = acc.Moved + mov
      Reordered = acc.Reordered + reo
      Empty = acc.Empty + emptyScript
      Contained = acc.Contained + contained
      ContainerRefused = acc.ContainerRefused + refused
      OrderBroken = acc.OrderBroken + orderBroken
      Predicates = Set.add (showKinds kinds) acc.Predicates }

let private diffDifferential' (containedDiff: ContainedDiff) (seed: int) (trials: int) : DiffTally =
    let mutable r = ConfRng.ofSeed seed
    let mutable tally = emptyDiffTally

    for _ in 1..trials do
        let before, r1 = genPairTree r
        let after, r2 = genPairTree r1
        let kinds, r3 = drawCanHold r2
        r <- r3
        tally <- diffProbe containedDiff kinds before after tally

    tally

let private diffDifferential (seed: int) (trials: int) : DiffTally =
    diffDifferential' TreeDiff.to_ops_contained seed trials

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- Phase 305: the content-aware diff oracle — `Diff.toOpsWith` under `OpScope.contained` beside the model ----
          //
          // `TreeDiff.diff_applicable_contained_run` is about the extracted `to_ops_contained_with`; this
          // holds that function to the shipped one. Pairs are the Phase 141 generator's independent
          // draws under a drawn predicate, as the structural diff differential above; the encoder is
          // the kind tag, which is the content the witness shows and the model's `changed_kind` reads.
          // Compared per pair: the verdict and the script, operation for operation, through the same
          // renderers as the structural differential; then the theorem instantiated on both sides —
          // the extracted `apply_contained_all` lands the extracted script on the model of `after`,
          // and `Ops.applyAll` under `OpScope.contained` lands production's on `after`'s shape. Agreement is over the
          // pool drawn, never over all inputs.

          testCase
              "the content-aware diff oracle agrees with Diff.toOpsContainedWith under a drawn predicate, and both scripts apply under it"
          <| fun _ ->
              // The Phase 141 generator keys every node's kind off its id, so a survivor never changes
              // kind and the two update blocks would be empty on every pair; re-kind a third of
              // `after`'s non-root nodes, seeded and replayable, so the blocks are reached.
              let rec rekind (r0: ConfRng.T) (t: RNode) : RNode * ConfRng.T =
                  let flip, r1 = ConfRng.intBelow 3 r0

                  let kind, r2 =
                      if t.Id <> "root" && flip = 0 then
                          let i, r' = ConfRng.intBelow (List.length containerKinds) r1
                          containerKinds[i], r'
                      else
                          t.Kind, r1

                  let children, r3 =
                      t.Children
                      |> List.fold
                          (fun (acc, r) c ->
                              let c', r' = rekind r c
                              acc @ [ c' ], r')
                          ([], r2)

                  { t with
                      Kind = kind
                      Children = children },
                  r3

              let mutable r = ConfRng.ofSeed 30507
              let mutable accepted = 0
              let mutable withUpdate = 0
              let mutable disagreements = []

              for i in 1..800 do
                  let before, r1 = genPairTree r
                  let after, r2 = genPairTree r1
                  let after, r2 = rekind r2 after
                  let kinds, r3 = drawCanHold r2
                  r <- r3
                  let canHold (n: RNode) = kinds.Contains(nodew.KindTag n)
                  let mCanHold (t: TreeOps.tree) = kinds.Contains(mKind t)
                  let encode (n: RNode) = nodew.KindTag n
                  let mb = toModelTree before
                  let ma = toModelTree after

                  let prod =
                      Diff.toOpsWith encode Policy.none (OpScope.contained canHold nodew idw) before after

                  let model = TreeDiff.to_ops_contained_with mCanHold mb ma

                  if prodDiffRender prod <> modelDiffRender model then
                      disagreements <-
                          sprintf
                              "#%d under canHold={%s}\n  production: %s\n  oracle:     %s"
                              i
                              (showKinds kinds)
                              (prodDiffRender prod)
                              (modelDiffRender model)
                          :: disagreements

                  match prod, model with
                  | Ok pops, DagFold.Ok mops ->
                      accepted <- accepted + 1

                      if
                          pops
                          |> List.exists (fun o ->
                              match o with
                              | UpdateNode _ -> true
                              | _ -> false)
                      then
                          withUpdate <- withUpdate + 1

                      Expect.equal
                          (Preservation.apply_contained_all mCanHold mops mb)
                          (DagFold.Ok ma)
                          (sprintf
                              "#%d: the extracted script applies under the predicate on the model and lands on after"
                              i)

                      match Ops.applyAll Policy.none (OpScope.contained canHold nodew idw) pops before with
                      | Ok t ->
                          Expect.equal
                              (prodShape t)
                              (prodShape after)
                              (sprintf "#%d: production's script lands on after" i)
                      | Error { Applied = j; Rejection = e } ->
                          failtestf "#%d: production's content-aware script was REFUSED at %d: %A" i j e
                  | _ -> ()

              match disagreements with
              | [] -> ()
              | ds ->
                  failtestf
                      "Diff.toOpsContainedWith and the extracted model DISAGREE on %d pair(s):\n%s"
                      (List.length ds)
                      (String.concat "\n" (List.rev ds))

              Expect.isGreaterThan accepted 100 "the pool produced scripts, not only refusals"

              Expect.isGreaterThan
                  withUpdate
                  50
                  "the pool reached content changes (a differential over pairs with no rewrite measures nothing of the two blocks)"

          testCase "a bridge that erases every kind loses against Diff.toOpsContainedWith — the measurement can fail"
          <| fun _ ->
              // The teeth. Handed trees whose kinds are all erased, the model sees no content change
              // and emits no rewrite; on every pair where production emitted one the comparison must
              // lose. (The predicate reads the kind too, so the blind model may also refuse where
              // production accepts — either way a disagreement, which is what is asserted.)
              // The Phase 141 generator keys every node's kind off its id, so a survivor never changes
              // kind and the two update blocks would be empty on every pair; re-kind a third of
              // `after`'s non-root nodes, seeded and replayable, so the blocks are reached.
              let rec rekind (r0: ConfRng.T) (t: RNode) : RNode * ConfRng.T =
                  let flip, r1 = ConfRng.intBelow 3 r0

                  let kind, r2 =
                      if t.Id <> "root" && flip = 0 then
                          let i, r' = ConfRng.intBelow (List.length containerKinds) r1
                          containerKinds[i], r'
                      else
                          t.Kind, r1

                  let children, r3 =
                      t.Children
                      |> List.fold
                          (fun (acc, r) c ->
                              let c', r' = rekind r c
                              acc @ [ c' ], r')
                          ([], r2)

                  { t with
                      Kind = kind
                      Children = children },
                  r3

              let mutable r = ConfRng.ofSeed 30508
              let mutable withUpdate = 0
              let mutable lost = 0

              for _ in 1..1000 do
                  let before, r1 = genPairTree r
                  let after, r2 = genPairTree r1
                  let after, r2 = rekind r2 after
                  let kinds, r3 = drawCanHold r2
                  r <- r3
                  let canHold (n: RNode) = kinds.Contains(nodew.KindTag n)
                  let mCanHold (t: TreeOps.tree) = kinds.Contains(mKind t)
                  let encode (n: RNode) = nodew.KindTag n

                  let prod =
                      Diff.toOpsWith encode Policy.none (OpScope.contained canHold nodew idw) before after

                  let model =
                      TreeDiff.to_ops_contained_with mCanHold (toModelTreeBlind before) (toModelTreeBlind after)

                  match prod with
                  | Ok pops when
                      pops
                      |> List.exists (fun o ->
                          match o with
                          | UpdateNode _ -> true
                          | _ -> false)
                      ->
                      withUpdate <- withUpdate + 1

                      if prodDiffRender prod <> modelDiffRender model then
                          lost <- lost + 1
                  | _ -> ()

              Expect.isGreaterThan withUpdate 50 "the sample reached pairs whose script carries a rewrite"
              Expect.equal lost withUpdate "every such pair lost under the blind bridge"

          // ---- the diff, over pairs nobody derived from one another (Phase 141) ----

          testCase "the diff oracle agrees with Diff.toOps over INDEPENDENTLY generated pairs"
          <| fun _ ->
              // The widening this phase exists for. Every pair `Conformance.diffLaws` has ever
              // diffed was built by applying ops to `before`, so the algebra could always reach it;
              // these two trees are drawn independently over a shared id space and share only what
              // the draw happened to give them.
              let t = diffDifferential 1410 240

              Expect.isEmpty t.Diffs (sprintf "the diff oracle disagreed with production:\n%s" (renderDiffs t.Diffs))

              // adequacy — a run that never removed anything, or never reordered, would certify a
              // pass that never fired. MEASURED at 240 pairs, seed 1410: 179 scripts insert, 146
              // remove, 140 move, 168 reorder, 3 pairs came out identical, 80 contained diffs
              // produced a script and 160 were refused for containment, all 8 predicates drawn.
              // Every threshold below is under its measurement with headroom and above zero.
              Expect.isGreaterThan t.Inserted 40 (sprintf "pairs whose script INSERTS (%d)" t.Inserted)
              Expect.isGreaterThan t.Removed 40 (sprintf "pairs whose script REMOVES (%d)" t.Removed)
              Expect.isGreaterThan t.Moved 40 (sprintf "pairs whose script MOVES (%d)" t.Moved)
              Expect.isGreaterThan t.Reordered 20 (sprintf "pairs whose script REORDERS (%d)" t.Reordered)

              Expect.isGreaterThan
                  t.Contained
                  20
                  (sprintf
                      "contained diffs that produced a script and were certified through applyAllWith (%d)"
                      t.Contained)

              Expect.isGreaterThan
                  t.ContainerRefused
                  20
                  (sprintf
                      "contained diffs REFUSED for containment (%d) — otherwise the refusal half of the mirror is never reached"
                      t.ContainerRefused)

              Expect.isGreaterThan
                  (Set.count t.Predicates)
                  5
                  (sprintf "several DIFFERENT canHold predicates were drawn (%A)" t.Predicates)

          testCase "the four-pass ORDER is load-bearing — removes before inserts loses"
          <| fun _ ->
              // The go-red for `diff_emission_order`. The theorem proves the script is four
              // homogeneous blocks in one order, so partitioning by operation kind recovers those
              // blocks and reassembling them removes-first is the SAME four passes, reordered. If
              // this permutation reconstructed as reliably as the real order, the order argument
              // the source comment carries — and the theorem that mechanises it — would be about
              // nothing. It must lose, and it does: a removed region's surviving descendants are
              // still inside it when the remove runs.
              let t = diffDifferential 1410 240

              Expect.isGreaterThan
                  t.Removed
                  40
                  (sprintf "the run reached scripts that remove at all (%d) — otherwise it proves nothing" t.Removed)

              Expect.isGreaterThan
                  t.OrderBroken
                  10
                  (sprintf
                      "an emission order with the REMOVES FIRST must fail to reconstruct (broken=%d of %d pairs) — the same four blocks, permuted"
                      t.OrderBroken
                      t.Pairs)

          // MEASURED: 52 of 240 pairs. Not all of them, and that is the honest shape of the
          // claim — the permutation is harmless on a pair whose removals happen to hold no
          // survivor, which is most of them. What the order buys is correctness on the rest.

          testCase "a contained diff that never checks the after tree loses — the measurement can fail"
          <| fun _ ->
              // The go-red for the container half: the model's own plain `to_ops` in the contained
              // slot, which is exactly "an oracle that emits an insert under a non-container"
              // because it never looked. `diff_contained_at_total_is_plain` proves it is the
              // predicate-refuses-nothing instance, so handing it a predicate that refuses
              // something is a proved weakening before it is a measured one.
              let t = diffDifferential' diffGoRedContained 1410 240

              Expect.isGreaterThan
                  t.ContainerRefused
                  20
                  (sprintf
                      "the go-red run met pairs whose `after` nests under a non-container (%d) — otherwise it proves nothing"
                      t.ContainerRefused)

              Expect.isNonEmpty
                  t.Diffs
                  "a contained diff that never checks the after tree MUST disagree with Diff.toOpsContained"

              Expect.isTrue
                  (t.Diffs |> List.exists (fun d -> d.Contains "toOpsContained differs"))
                  (sprintf
                      "the disagreement is the one this phase is about — production refuses the pair up front, the unchecked one emits a script. Got:\n%s"
                      (List.head t.Diffs))

          testCase "the reconstruction hypothesis is NECESSARY — a pair whose shared id changes KIND loses"
          <| fun _ ->
              // Phase 167's go-red, and the one this phase owes. `diff_reconstructs` is proved
              // under `kinds_agree` — a node id the two trees share names the same kind in both —
              // and a hypothesis nobody can see fail is indistinguishable from one that was never
              // needed. The pool above has ENFORCED that condition since Phase 141 (a node's
              // content is a function of its id), so the green run can never exercise it; this is
              // the same generator, the same seed and the same shapes with the `after` tree's kinds
              // drawn by a second function of the id.
              //
              // PRODUCTION ONLY — no model is in the loop. What is measured is that the SHIPPED
              // structural diff cannot express the change (the content-aware `Diff.toOpsWith` can,
              // Phase 305), not that the model agrees with it about anything;
              // the model's own counterexample is `TreeDiff.kinds_agree_is_necessary`, proved.
              let mutable r = ConfRng.ofSeed 1410
              let mutable shared = 0
              let mutable lost = 0
              let mutable refused = 0

              for _ in 1..240 do
                  let before, r1 = genPairTree r
                  let after0, r2 = genPairTree r1
                  // drawn and discarded, so the RNG stream matches the green run's pair for pair
                  let _, r3 = drawCanHold r2
                  r <- r3
                  let after = reKind diffKindShifted after0

                  let sharedIds =
                      Set.intersect (Set.ofList (idsOfTree before)) (Set.ofList (idsOfTree after))
                      |> Set.remove "root"

                  if not (Set.isEmpty sharedIds) then
                      shared <- shared + 1

                      match Diff.toOps Policy.none (OpScope.plain nodew idw) before after with
                      | Error e ->
                          // `diff_refusals_exact` says the only refusals are a root-id mismatch and
                          // a repeated id, and this pair has neither — a kind disagreement is not a
                          // refusal, which is half of why it is dangerous.
                          failtestf "toOps REFUSED a well-formed pair over a kind disagreement (%A)" e
                      | Ok ops ->
                          match Ops.canApplyAll Policy.none (OpScope.plain nodew idw) ops before with
                          | Error _ -> refused <- refused + 1
                          | Ok() -> ()

                          match Ops.applyAll Policy.none (OpScope.plain nodew idw) ops before with
                          | Ok t when prodTreeHash t = prodTreeHash after -> ()
                          | _ -> lost <- lost + 1

              // MEASURED at 240 pairs, seed 1410: 163 pairs share a non-root id, all 163 fail to
              // reconstruct, 0 fail applicability. Run the other way — `reKind diffKindOf`, which
              // restores the constraint — it is 0 of 163, so the probe distinguishes the two
              // directions rather than failing for any reason at all.
              //
              // Adequacy first, and it is not a formality: "every pair lost" over a pool that never
              // drew a shared id would be a green assertion measuring nothing at all.
              Expect.isGreaterThan
                  shared
                  100
                  (sprintf "pairs that share a non-root id, and so actually break `kinds_agree` (%d of 240)" shared)

              Expect.equal
                  lost
                  shared
                  (sprintf
                      "EVERY pair whose shared id carries a different kind must fail to reconstruct (%d of %d) — no skeleton operation edits a node, so the survivor keeps `before`'s kind whatever the script does"
                      lost
                      shared)

              // The asymmetry the two theorems predict: `diff_applicable` carries NO hypothesis, so
              // applicability survives the disagreement and only reconstruction fails. If this ever
              // counts above zero, one of the two theorems is about a different function.
              Expect.equal
                  refused
                  0
                  (sprintf
                      "APPLICABILITY survives a kind disagreement (%d refusals) — `diff_applicable` holds without `kinds_agree`, and only `diff_reconstructs` needs it"
                      refused) ]
