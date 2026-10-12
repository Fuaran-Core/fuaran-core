module Fuaran.Core.Tests.ProofOracle.TreeOpsTests

// The `Proofs.Oracle` differential for `proofs/TreeOps.fst`: the extracted model
// (`proofs/oracle/TreeOps.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.FoldFixtures
open Fuaran.Core.Tests.ProofOracle.Bridges

/// Membership is the meaning on both sides: production carries `Set<string>`, the model carries
/// lists read as sets (proofs/README.md, "sets are lists").
let private asSet (xs: string list) = xs |> List.distinct |> List.sort

let private slotKey (n: string, s: string) = n + "\u0000" + s

let private prodFpParts (f: Footprint) =
    [ asSet (Set.toList f.Reads)
      asSet (Set.toList f.StructureWrites)
      asSet (Set.toList f.ContentWrites)
      asSet (Set.toList f.UnknownParentWrites)
      asSet (f.SlotReads |> Set.toList |> List.map slotKey)
      asSet (f.SlotWrites |> Set.toList |> List.map slotKey) ]

let private modelFpParts (f: DagFold.footprint) =
    [ asSet f.reads
      asSet f.structure_writes
      asSet f.content_writes
      asSet f.unknown_parent_writes
      asSet (f.slot_reads |> List.map slotKey)
      asSet (f.slot_writes |> List.map slotKey) ]

type private TreeTally =
    {
        Diffs: string list
        Accepted: int
        Rejected: int
        Classes: Set<string>
        /// The op kinds that were ACCEPTED somewhere (Phase 309) — the pool's adequacy over the
        /// whole alphabet, `UpdateNode` and `Batch` included.
        AcceptedKinds: Set<string>
    }

let private emptyTreeTally =
    { Diffs = []
      Accepted = 0
      Rejected = 0
      Classes = Set.empty
      AcceptedKinds = Set.empty }

/// One (op, state) asked of both sides. `opBridge` carries the op across given the STATE it is
/// asked at — the faithful bridge ignores the state; the Phase 290 mis-nesting mutant reads it.
let private treeProbeWith
    (bridge: RNode -> TreeOps.tree)
    (opBridge: RNode -> SkeletonOp<RNode, string> -> TreeOps.op)
    (op: SkeletonOp<RNode, string>)
    (st: RNode)
    (acc: TreeTally)
    : TreeTally =
    let mop = opBridge st op
    let mst = bridge st
    let where = sprintf "op %s at tree %s" (treeW.Encode op) (prodTreeHash st)

    let fpDiff =
        let p = prodFpParts (Ops.footprint nodew idw [ op ])
        let m = modelFpParts (TreeOps.op_fp mop)

        if p <> m then
            [ sprintf "footprint differs — %s\n  production: %A\n  oracle:     %A" where p m ]
        else
            []

    let prod = Ops.apply nodew idw op st
    let model = TreeOps.apply mop mst

    let applyDiff, accepted, rejected, cls =
        match prod, model with
        | Ok pt, DagFold.Ok mt ->
            let ph = prodTreeHash pt
            let mh = modelTreeHash mt

            // Phase 302 — compared STRUCTURALLY; the digests stay in the message only.
            (if prodShape pt <> modelShape mt then
                 [ sprintf "accepted result differs — %s\n  production: %s\n  oracle:     %s" where ph mh ]
             else
                 []),
            1,
            0,
            None
        | Error pe, DagFold.Error me ->
            let pc = prodRejClass pe
            let mc = modelRejClass me

            (if pc <> mc then
                 [ sprintf "rejection class differs — %s\n  production: %s\n  oracle:     %s" where pc mc ]
             else
                 []),
            0,
            1,
            Some pc
        | Ok _, DagFold.Error me ->
            [ sprintf "production ACCEPTED but the oracle rejected (%s) — %s" (modelRejClass me) where ], 0, 0, None
        | Error pe, DagFold.Ok _ ->
            [ sprintf "production REJECTED (%s) but the oracle accepted — %s" (prodRejClass pe) where ], 0, 0, None

    { acc with
        Diffs = acc.Diffs @ fpDiff @ applyDiff
        Accepted = acc.Accepted + accepted
        Rejected = acc.Rejected + rejected
        Classes =
            match cls with
            | Some c -> Set.add c acc.Classes
            | None -> acc.Classes }

/// The op kind, for the tally — `LawKit.opKindOf`'s vocabulary.
let private skeletonKind (op: SkeletonOp<RNode, string>) : string =
    match op with
    | InsertChild _ -> "insert"
    | RemoveNode _ -> "remove"
    | MoveNode _ -> "move"
    | ReorderChildren _ -> "reorder"
    | Batch _ -> "batch"
    | UpdateNode _ -> "update"

/// Every op the generator yields, plus the refusals, asked at every state a prefix of the
/// generated pool reaches — the same construction the Phase 132 diamond family uses, and for the
/// same reason: a check at the base tree alone measures one instance.
let private treeDifferentialWith
    (bridge: RNode -> TreeOps.tree)
    (opBridge: RNode -> SkeletonOp<RNode, string> -> TreeOps.op)
    (seed: int)
    (trials: int)
    : TreeTally =
    let mutable r = ConfRng.ofSeed seed
    let mutable tally = emptyTreeTally

    for _ in 1..trials do
        let lanes, r' = treeLaneGen.Lanes 3 r
        r <- r'
        let generated = List.concat lanes

        let states =
            generated
            |> List.fold
                (fun (acc, cur) op ->
                    match Ops.apply nodew idw op cur with
                    | Ok t -> (acc @ [ t ]), t
                    | Error _ -> acc, cur)
                ([ treeBase ], treeBase)
            |> fst

        for op in generated @ drawnUpdatesAndBatches states generated @ treeRefusals do
            for st in states do
                let before = tally.Accepted
                tally <- treeProbeWith bridge opBridge op st tally

                if tally.Accepted > before then
                    tally <-
                        { tally with
                            AcceptedKinds = Set.add (skeletonKind op) tally.AcceptedKinds }

    tally

let private treeDifferential (bridge: RNode -> TreeOps.tree) (seed: int) (trials: int) : TreeTally =
    treeDifferentialWith bridge (fun _ -> toModelOpWith bridge) seed trials

/// The MIS-NESTING mutant (Phase 290) — the go-red instrument for the digest's shape claim. An
/// `InsertChild(p, n)` is carried across as an insert under the DEEPEST LAST DESCENDANT of `p`
/// instead of under `p` itself: the two results have the SAME preorder (the new node is still the
/// last thing visited after `p`'s subtree) and differ only in where it hangs. Until Phase 290
/// `Tree.encodeHash` folded the preorder alone, so this mutant AGREED with production on the
/// accepted-result hash at every insert — the differential could not see nesting — and the arity
/// fold is what makes it lose. Every other op crosses faithfully, so the verdicts still agree: what
/// has to fail is the result hash, and nothing else.
let private toModelOpMisNest (st: RNode) (op: SkeletonOp<RNode, string>) : TreeOps.op =
    let rec deepestLast (n: RNode) =
        match List.tryLast n.Children with
        | Some c -> deepestLast c
        | None -> n.Id

    match op with
    | InsertChild(p, node) ->
        match Tree.tryFind nodew idw p st with
        | Some parent -> TreeOps.InsertChild(deepestLast parent, toModelTree node)
        | None -> TreeOps.InsertChild(p, toModelTree node)
    | other -> toModelOpWith toModelTree other

/// The theorem's own instance on the EXTRACTED code: for every pair the model's `independent`
/// declares disjoint and every state where both halves of the guarded algebra accept, the two
/// orders agree. `oracleFp` is the footprint the check runs under — the real one in the green
/// run, a blind one in the go-red.
let private modelDiamondBreaks
    (oracleFp: TreeOps.op -> DagFold.footprint)
    (ops: TreeOps.op list)
    (states: TreeOps.tree list)
    : string list * int =
    let mutable met = 0
    let mutable breaks = []

    for a in ops do
        for b in ops do
            if DagFold.independent (oracleFp a) (oracleFp b) then
                for s in states do
                    match TreeOps.wapply a s, TreeOps.wapply b s with
                    | DagFold.Ok sa, DagFold.Ok sb ->
                        met <- met + 1

                        match TreeOps.wapply b sa, TreeOps.wapply a sb with
                        | DagFold.Ok t1, DagFold.Ok t2 when t1 = t2 -> ()
                        | _ ->
                            breaks <-
                                breaks
                                @ [ sprintf "the extracted model breaks the diamond at a well-formed tree" ]
                    | _ -> ()

    breaks, met

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- Phase 133: the TREE ALGEBRA model beside Ops.apply / Ops.footprint ----

          testCase "the tree oracle agrees with Ops.apply and Ops.footprint over the generated pool"
          <| fun _ ->
              let t = treeDifferential toModelTree 1330 30

              match t.Diffs with
              | d :: _ -> failtestf "the tree oracle and production DISAGREE\n%s" d
              | [] ->
                  // The pack's sample-adequacy posture: an agreement that never met a rejection
                  // certifies one arm only, and one that never met an acceptance certifies none.
                  Expect.isGreaterThan
                      t.Accepted
                      0
                      (sprintf "(op, state) pairs were ACCEPTED (accepted=%d rejected=%d)" t.Accepted t.Rejected)

                  Expect.isGreaterThan
                      t.Rejected
                      0
                      (sprintf "(op, state) pairs were REJECTED (accepted=%d rejected=%d)" t.Accepted t.Rejected)

                  // Every rejection class the plain `apply` can raise was reached — named rather
                  // than counted, because a count cannot say WHICH arm went unexercised.
                  // `NotAContainer` is `applyContained`'s alone and `Rejected` is the domain-side
                  // extension point Core never raises, so the reachable set is these five.
                  for cls in
                      [ "UnknownNode"
                        "DuplicateId"
                        "CannotRemoveRoot"
                        "WouldNestUnderSelf"
                        "ReorderMismatch" ] do
                      Expect.isTrue
                          (Set.contains cls t.Classes)
                          (sprintf "the sample reached a %s rejection (reached: %A)" cls t.Classes)

                  // Phase 309 — the whole alphabet was ACCEPTED somewhere, the two kinds the lane
                  // generator never draws included: an update and a batch reach the differential
                  // by generation, not by two hand-written cases.
                  for kind in [ "insert"; "remove"; "move"; "reorder"; "batch"; "update" ] do
                      Expect.isTrue
                          (Set.contains kind t.AcceptedKinds)
                          (sprintf "the pool ACCEPTED a %s somewhere (accepted kinds: %A)" kind t.AcceptedKinds)

          testCase "a tree oracle handed a blind bridge DISAGREES with Ops.apply"
          <| fun _ ->
              // The teeth. The blind bridge erases every kind tag, so the model is asked about a
              // different tree from the one production was asked about. The VERDICTS still agree —
              // no clause in the algebra reads a kind — so what has to lose is the accepted-result
              // hash, which is otherwise the half a coincidence could carry. A green report above
              // is therefore known to be a comparison that can fail.
              let t = treeDifferential toModelTreeBlind 1330 3

              Expect.isNonEmpty t.Diffs "a bridge that erases every kind tag must lose the result comparison"

              Expect.isTrue
                  (t.Diffs |> List.exists (fun d -> d.Contains "accepted result differs"))
                  (sprintf "the disagreement names the arm that moved — got:\n%s" (List.head t.Diffs))

          testCase "a tree oracle handed a MIS-NESTING op bridge DISAGREES with Ops.apply — the digest sees the shape"
          <| fun _ ->
              // Phase 290's teeth on the differential itself. The mutant re-nests every accepted
              // insert under the parent's deepest last descendant, which keeps the preorder and
              // moves the shape. Under the arity-free fold this comparison was GREEN — measured:
              // the preorder-only `Tree.encodeHash` hashed the two results equal — so a green
              // report above said nothing about nesting. The arity fold makes it lose, on the
              // result hash and on nothing else: every verdict still agrees.
              let t = treeDifferentialWith toModelTree toModelOpMisNest 1330 3

              Expect.isNonEmpty t.Diffs "a bridge that re-nests an insert must lose the result comparison"

              Expect.isTrue
                  (t.Diffs |> List.exists (fun d -> d.Contains "accepted result differs"))
                  (sprintf "the disagreement names the arm that moved — got:\n%s" (List.head t.Diffs))

              // The mutant re-parents the insert, so its FOOTPRINT names a different parent too —
              // expected, and not the claim. What must NOT move is any VERDICT: the mutant inserts
              // under a node that exists exactly when the original parent does, so accept/reject
              // agrees at every state, and the shape is visible ONLY through the result hash.
              let verdictMoved =
                  t.Diffs
                  |> List.filter (fun d ->
                      d.Contains "ACCEPTED but"
                      || d.Contains "REJECTED but"
                      || d.Contains "rejection class differs")

              Expect.isEmpty
                  verdictMoved
                  (sprintf "no verdict moved under the mis-nesting mutant:\n%s" (String.concat "\n" verdictMoved))

              // And the pair the shard names, by hand: `root(a(a1,a2), b(b1))` against
              // `root(a(a1,a2,b(b1)))` — one preorder, one `MoveNode` apart — hash distinctly
              // through production's own function on the reference node AND on the model's tree.
              let flat = treeBase

              let nested =
                  match Ops.apply nodew idw (MoveNode("b", "a")) treeBase with
                  | Ok t -> t
                  | Error e -> failtestf "MoveNode(b, a) was refused: %A" e

              Expect.equal
                  (Tree.preorder nodew flat |> List.map _.Id)
                  (Tree.preorder nodew nested |> List.map _.Id)
                  "the premise: one preorder"

              Expect.notEqual (prodTreeHash flat) (prodTreeHash nested) "production tells them apart"

              Expect.notEqual
                  (modelTreeHash (toModelTree flat))
                  (modelTreeHash (toModelTree nested))
                  "so does the model's witness"

          testCase "the digest pre-image production folds is the extracted model's shape, field for field"
          <| fun _ ->
              // Phase 290's bridge, measured: `preorder_arity_injective` is proved over
              // `TreeOps.shape` — each node as (id, kind, arity) in preorder — and production's
              // `Tree.encodePreimage` is `Hash.canonicalFields` over [label; arity] per node. The
              // two must agree at every state the generated pool reaches, with the model's
              // (id, kind) rendered exactly as the production label here renders it, so the
              // theorem is known to be about the string the memo keys on and not a lookalike.
              let render (i: string, k: string) = encWitnessNode i k
              let mutable r = ConfRng.ofSeed 1332
              let mutable states = 0

              for _ in 1..30 do
                  let lanes, r' = treeLaneGen.Lanes 3 r
                  r <- r'

                  let reached =
                      List.concat lanes
                      |> List.fold
                          (fun (acc, cur) op ->
                              match Ops.apply nodew idw op cur with
                              | Ok t -> (acc @ [ t ]), t
                              | Error _ -> acc, cur)
                          ([ treeBase ], treeBase)
                      |> fst

                  for st in reached do
                      states <- states + 1

                      Expect.equal
                          (Tree.encodePreimage nodew (fun n -> encWitnessNode n.Id n.Kind) st)
                          (Hash.canonicalFields (TreeOps.fields render string (TreeOps.shape (toModelTree st))))
                          (sprintf "production's pre-image is the model's rendered shape at tree %s" (prodTreeHash st))

              Expect.isGreaterThan states 30 (sprintf "states reached (%d)" states)

              // And the refutation the model evaluates, on production: the label preorder of the
              // shard's pair is one list, the pre-image is two.
              let nested =
                  match Ops.apply nodew idw (MoveNode("b", "a")) treeBase with
                  | Ok t -> t
                  | Error e -> failtestf "MoveNode(b, a) was refused: %A" e

              Expect.equal
                  (TreeOps.labels (toModelTree treeBase))
                  (TreeOps.labels (toModelTree nested))
                  "the model's label preorder aliases the pair"

              Expect.notEqual
                  (TreeOps.shape (toModelTree treeBase))
                  (TreeOps.shape (toModelTree nested))
                  "and its shape does not"

          testCase "the extracted tree model keeps the diamond its own theorem proves"
          <| fun _ ->
              // `TreeOps.leaf_independence_diamond` is proved on the model; this is that theorem's
              // instance on the EXTRACTED code, over the pool Phase 80's generator produces. The
              // alphabet is the non-`Batch` ops, which is the alphabet THAT theorem is stated over
              // — Phase 133's, and this case is Phase 133's evidence, unchanged. Since Phase 162
              // the model also carries `op_independence_diamond` over the whole alphabet, batches
              // included; its evidence is the case below rather than a widening of this one, so
              // that the two alphabets stay separately measured.
              let mutable r = ConfRng.ofSeed 1331
              let mutable breaks = []
              let mutable met = 0

              for _ in 1..60 do
                  let lanes, r' = treeLaneGen.Lanes 3 r
                  r <- r'
                  let ops = List.concat lanes

                  let states =
                      ops
                      |> List.fold
                          (fun (acc, cur) op ->
                              match Ops.apply nodew idw op cur with
                              | Ok t -> (acc @ [ t ]), t
                              | Error _ -> acc, cur)
                          ([ treeBase ], treeBase)
                      |> fst

                  let mops =
                      ops
                      |> List.filter (fun o ->
                          match o with
                          | Batch _ -> false
                          | _ -> true)
                      |> List.map (toModelOpWith toModelTree)

                  let b, m = modelDiamondBreaks TreeOps.op_fp mops (states |> List.map toModelTree)
                  breaks <- breaks @ b
                  met <- met + m

              match breaks with
              | why :: _ -> failtest why
              | [] ->
                  // The premise here is narrower than the Phase 132 family's: `wapply` demands a
                  // well-formed state AND a well-formed result, so a (pair, state) triple counts
                  // only where both halves of the guarded algebra accept. Measured at 60 trials:
                  // 155. The threshold is below that with room and is there to catch a generator
                  // that stops producing independent pairs, not to pin the number.
                  Expect.isGreaterThan met 100 (sprintf "the sample met the diamond's premise in earnest (met=%d)" met)

          testCase "a blind footprint BREAKS the extracted model's diamond — the measurement can fail"
          <| fun _ ->
              // The teeth for the family above, on a pair chosen so the break is not a matter of
              // luck: two inserts under the SAME parent both apply and do not commute (the appended
              // order differs), and `Ops.independent` correctly refuses them. A footprint that
              // declares every pair independent must therefore break the diamond here.
              let blind (_: TreeOps.op) : DagFold.footprint =
                  { DagFold.reads = []
                    DagFold.structure_writes = []
                    DagFold.content_writes = []
                    DagFold.unknown_parent_writes = []
                    DagFold.slot_reads = []
                    DagFold.slot_writes = [] }

              let x = TreeOps.InsertChild("a", TreeOps.TNode("x133", "para", []))
              let y = TreeOps.InsertChild("a", TreeOps.TNode("y133", "para", []))
              let s0 = toModelTree treeBase

              Expect.isFalse
                  (DagFold.independent (TreeOps.op_fp x) (TreeOps.op_fp y))
                  "the two same-parent inserts are NOT independent — the shared structural parent"

              let breaks, met = modelDiamondBreaks blind [ x; y ] [ s0 ]
              Expect.isGreaterThan met 0 "the blind run met the premise, so it had something to measure"

              Expect.isNonEmpty
                  breaks
                  "a footprint declaring EVERY pair independent must break the diamond — otherwise this measurement cannot lose"

          // ---- Phase 162 — the same diamond over the WHOLE alphabet, nested batches included ----

          testCase "the extracted model keeps the diamond on BATCH pairs — the shape Phase 133 left open"
          <| fun _ ->
              // `TreeOps.covered` named three pair shapes the Phase 133 diamond could not reach:
              // either side a `Batch` that neither does nothing nor relocates. Phase 162 lifted the
              // diamond along a batch's script, deleted `covered`, and stated
              // `op_independence_diamond` over the whole `SkeletonOp` alphabet. This is that
              // widening measured on the EXTRACTED code, and it is a case of its own rather than a
              // change to the one above, so Phase 133's evidence stays exactly what it was.
              //
              // The pool is built from the generated ops the case above filters out batches from,
              // wrapped three ways: a singleton batch (same footprint as its member, so it inherits
              // that member's independence and guarantees the premise is met at all), a paired
              // batch, and one doubly-nested batch — because "nested to any depth" is the part of
              // the claim a single wrapping would not exercise. Only inserts and reorders are
              // wrapped: a batch carrying a remove or a move RELOCATES, and such a pair was already
              // closed by Phase 133 without any lift.
              let mutable r = ConfRng.ofSeed 1620
              let mutable breaks = []
              let mutable met = 0
              let mutable batchMet = 0

              for _ in 1..20 do
                  let lanes, r' = treeLaneGen.Lanes 3 r
                  r <- r'
                  let ops = List.concat lanes

                  let states =
                      ops
                      |> List.fold
                          (fun (acc, cur) op ->
                              match Ops.apply nodew idw op cur with
                              | Ok t -> (acc @ [ t ]), t
                              | Error _ -> acc, cur)
                          ([ treeBase ], treeBase)
                      |> fst
                      |> List.map toModelTree

                  let leaves =
                      ops
                      |> List.filter (fun o ->
                          match o with
                          | Batch _ -> false
                          | _ -> true)
                      |> List.map (toModelOpWith toModelTree)

                  let liftable =
                      leaves
                      |> List.filter (fun o ->
                          match o with
                          | TreeOps.InsertChild _
                          | TreeOps.ReorderChildren _ -> true
                          | _ -> false)

                  let batches =
                      (liftable |> List.map (fun o -> TreeOps.Batch [ o ]))
                      @ (liftable |> List.chunkBySize 2 |> List.map TreeOps.Batch)
                      @ (match liftable with
                         | o :: _ -> [ TreeOps.Batch [ TreeOps.Batch [ o ] ] ]
                         | [] -> [])

                  let b, m = modelDiamondBreaks TreeOps.op_fp (leaves @ batches) states
                  breaks <- breaks @ b
                  met <- met + m

                  // The adequacy this family cannot read off `met`: how many of the met pairs
                  // carried a batch at all. Without it the pool could collapse to leaves and the
                  // widening would be measured by nothing while the case still passed. Measured at
                  // 20 trials, seed 1620: met=156 of which batchMet=91. The thresholds are below
                  // both with room and are there to catch a generator that stops producing
                  // independent pairs, not to pin the numbers.
                  for a in batches do
                      for bb in leaves @ batches do
                          if DagFold.independent (TreeOps.op_fp a) (TreeOps.op_fp bb) then
                              for s in states do
                                  match TreeOps.wapply a s, TreeOps.wapply bb s with
                                  | DagFold.Ok _, DagFold.Ok _ -> batchMet <- batchMet + 1
                                  | _ -> ()

              match breaks with
              | why :: _ -> failtest why
              | [] ->
                  Expect.isGreaterThan met 100 (sprintf "the sample met the diamond's premise in earnest (met=%d)" met)

                  Expect.isGreaterThan
                      batchMet
                      0
                      (sprintf
                          "the pool met the premise on pairs carrying a BATCH — the shape Phase 133 left open (batchMet=%d)"
                          batchMet)

          // ---- Phase 143 — the precision ceiling: the pinned unknown-parent clause is NECESSARY ----

          testCase "the pinned unknown-parent clause is necessary — both classes, on the model and on production"
          <| fun _ ->
              // `proofs/TreeOps.fst` section 18 proves this; this is that section's witness run on
              // the EXTRACTED model and, in the same case, on PRODUCTION `Ops.footprint` /
              // `Ops.independent` over the reference witness. Two classes are named:
              //
              //   NOW-INDEPENDENT-IF-THE-CLAUSE-WERE-DROPPED — a `MoveNode` and an `InsertChild`
              //     under a parent inside the moved subtree. They DO commute; only the pinned
              //     clause refuses them.
              //   STILL-REFUSED-AND-RIGHTLY — the same shape with a `RemoveNode` in it. It does NOT
              //     commute: the insert's parent goes with the destroyed subtree.
              //
              // And the reason the first cannot be freed: the two ops carry the SAME footprint, so
              // any predicate over the four address sets gives them one verdict. This case goes RED
              // if `Ops.independent` is ever tightened, which is the point of writing it down.

              // ---- the model half ----
              Expect.equal
                  (TreeOps.op_fp TreeOps.reloc_move)
                  (TreeOps.op_fp TreeOps.reloc_remove)
                  "the move and the remove-shaped batch are footprint-INDISTINGUISHABLE on the model"

              Expect.isFalse
                  (DagFold.independent (TreeOps.op_fp TreeOps.reloc_move) (TreeOps.op_fp TreeOps.reloc_insert))
                  "the model refuses the pair — the pinned clause is the only clause doing so"

              let modelOrder (first: TreeOps.op) (second: TreeOps.op) =
                  match TreeOps.apply first TreeOps.reloc_tree with
                  | DagFold.Ok t -> TreeOps.apply second t
                  | e -> e

              Expect.equal
                  (modelOrder TreeOps.reloc_move TreeOps.reloc_insert)
                  (modelOrder TreeOps.reloc_insert TreeOps.reloc_move)
                  "NOW-INDEPENDENT class: the move and the insert commute on the model"

              Expect.isTrue
                  (match modelOrder TreeOps.reloc_remove TreeOps.reloc_insert with
                   | DagFold.Error _ -> true
                   | _ -> false)
                  "STILL-REFUSED class: the remove-shaped batch destroys the insert's parent"

              // ---- the production half: the same witness over the reference witness ----
              let pTree =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node "x" "section" [ RNode.node "p" "section" [] ]
                        RNode.node "q" "section" [] ]

              let pMove: SkeletonOp<RNode, string> = MoveNode("x", "q")

              let pRemove: SkeletonOp<RNode, string> =
                  Batch [ RemoveNode "x"; ReorderChildren("q", []) ]

              let pInsert: SkeletonOp<RNode, string> = InsertChild("p", RNode.node "n" "para" [])

              let fp (o: SkeletonOp<RNode, string>) = Ops.footprint nodew idw [ o ]

              Expect.equal (fp pMove) (fp pRemove) "production agrees: the two ops carry the same four address sets"

              // every clause EXCEPT the pinned pair holds — so the pinned pair is load-bearing here
              let fpM, fpI = fp pMove, fp pInsert
              let disjoint (a: Set<string>) b = Set.isEmpty (Set.intersect a b)

              Expect.isTrue
                  (disjoint fpM.ContentWrites fpI.ContentWrites
                   && disjoint fpM.ContentWrites fpI.Reads
                   && disjoint fpI.ContentWrites fpM.Reads
                   && disjoint fpM.StructureWrites fpI.StructureWrites)
                  "every clause but the pinned relocation pair is satisfied — nothing else is refusing this"

              Expect.isFalse (Ops.independent fpM fpI) "production refuses the pair, by the pinned clause alone"

              let prodOrder first second =
                  match Ops.apply nodew idw first pTree with
                  | Ok t -> Ops.apply nodew idw second t
                  | e -> e

              Expect.equal
                  (prodOrder pMove pInsert)
                  (prodOrder pInsert pMove)
                  "NOW-INDEPENDENT class, in production: the move and the insert commute"

              Expect.isTrue
                  (match prodOrder pRemove pInsert with
                   | Error _ -> true
                   | Ok _ -> false)
                  "STILL-REFUSED class, in production: remove-then-insert cannot find the insert's parent"

              Expect.isTrue
                  (match Ops.apply nodew idw pRemove pTree, Ops.apply nodew idw pInsert pTree with
                   | Ok _, Ok _ -> true
                   | _ -> false)
                  "both halves apply at the tree on their own — so the divergence is the pair's, not one op's"

          testCase "a move pair nesting into each other's subtrees is refused, and no record could free it"
          <| fun _ ->
              // The second, independent reason the refused set is not one homogeneous class waiting
              // on a better footprint. `TreeOps.relocation_move_pair_also_fails` proves it; here it
              // is on the extracted model and on production.
              let modelStep (first: TreeOps.op) (second: TreeOps.op) =
                  match TreeOps.apply first TreeOps.cross_tree with
                  | DagFold.Ok t -> TreeOps.apply second t
                  | e -> e

              Expect.isTrue
                  (match
                      TreeOps.apply TreeOps.cross_a TreeOps.cross_tree, TreeOps.apply TreeOps.cross_b TreeOps.cross_tree
                   with
                   | DagFold.Ok _, DagFold.Ok _ -> true
                   | _ -> false)
                  "each move applies on its own"

              Expect.isTrue
                  (match modelStep TreeOps.cross_a TreeOps.cross_b with
                   | DagFold.Error _ -> true
                   | _ -> false)
                  "after either, the other would nest a node under its own subtree — the cycle check refuses it"

              let cTree =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node "x" "section" [ RNode.node "mp" "section" [] ]
                        RNode.node "y" "section" [ RNode.node "np" "section" [] ] ]

              let a: SkeletonOp<RNode, string> = MoveNode("x", "np")
              let b: SkeletonOp<RNode, string> = MoveNode("y", "mp")
              let fp (o: SkeletonOp<RNode, string>) = Ops.footprint nodew idw [ o ]

              Expect.isFalse (Ops.independent (fp a) (fp b)) "production refuses the move pair"

              Expect.isTrue
                  (match Ops.apply nodew idw a cTree with
                   | Ok t ->
                       match Ops.apply nodew idw b t with
                       | Error _ -> true
                       | Ok _ -> false
                   | Error _ -> false)
                  "and it is genuinely dependent — the obstruction is the cycle check, not the addresses" ]
