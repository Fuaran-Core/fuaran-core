namespace Fuaran.Core

// ============================================================================
//  LawKit (Phase 297) — the kit's own runner. Every law family in this package
//  is written over it, so a kit-wide guarantee (a first-counterexample guard, a
//  seed-stamped message, the non-degeneracy count Phase 302 asks for, an op
//  generator that draws every kind) is threaded through ONE module rather than
//  through sixty copies.
//
//  Internal: nothing here is contract. A domain reads `LawResult`s and calls the
//  families; how the families keep their evidence is the kit's own business,
//  which is what lets the shape change under every family at once.
//
//  FSharp.Core only, Fable-clean: one class with mutable fields per cell, no
//  reflection, no `uint64`, no 32-bit multiply (`ConfRng` holds that line).
// ============================================================================

/// The runner.
module internal LawKit =

    /// The stamp every counterexample opens with: the seed and the iteration that reproduce it.
    let failAt (seed: int) (i: int) (msg: string) : string =
        "seed=" + string seed + " iter=" + string i + ": " + msg

    /// The remedy a never-reached law names. It is the sample's fault and not the law's, which is
    /// why the words are `SampleAdequacy`'s.
    [<Literal>]
    let private unreachedRemedy =
        " — the law asserted NOTHING in this run: no iteration reached the arm that builds its evidence, so a pass here would certify nothing (Phase 302). WIDEN THE GENERATOR; raising the iteration count or hunting a seed until the arm is reached leaves the law certified by one trial"

    /// One law under test. The first counterexample is kept and every later one discarded — a law
    /// is reported once, with the earliest seed-stamped case — and the EVIDENCE count is what the
    /// non-degeneracy rule reads: a law that was never reached cannot report `Passed`, whatever
    /// its failure slot says. That rule is the runner's shape rather than any family's discipline:
    /// the audit that opened Phase 302 found `certify` green under a constant encoder and
    /// `hashFnLaws` green under a constant `HashFn`, because a gated arm skipped and nothing counted
    /// the skip. Here the count is taken at the assertion, so a family cannot pass a law it never
    /// asserted.
    type LawCell(name: string) =
        let mutable failure: string option = None
        let mutable evidence = 0

        /// The law's text, as `LawResult.Law` will carry it.
        member _.Name = name

        /// How many times the law was asserted in this run.
        member _.Evidence = evidence

        /// `true` once a counterexample has been recorded.
        member _.Failed = failure.IsSome

        /// Record one assertion of the law — the evidence a pass stands on. `Check` calls it; a
        /// family calls it directly only where the assertion and the evidence are taken apart (an
        /// arm whose evidence is the arm having RUN, with its failure recorded elsewhere).
        member _.Saw() = evidence <- evidence + 1

        /// Record a counterexample. The first one is kept; every later call is a no-op, so a loop
        /// need not guard the slot itself.
        member _.Fail(counterexample: string) =
            if failure.IsNone then
                failure <- Some counterexample

        /// Assert the law once: evidence is taken, and a false `holds` records `counterexample`
        /// (built lazily — most assertions hold, and the message is often the expensive part).
        member c.Check(holds: bool, counterexample: unit -> string) =
            c.Saw()

            if not holds then
                c.Fail(counterexample ())

        /// The verdict. Red on a recorded counterexample; red, naming the remedy, on zero evidence;
        /// green otherwise.
        member _.Result: LawResult =
            match failure with
            | Some cx ->
                { Law = name
                  Passed = false
                  Counterexample = Some cx }
            | None when evidence <= 0 ->
                { Law = name
                  Passed = false
                  Counterexample = Some("never reached" + unreachedRemedy) }
            | None ->
                { Law = name
                  Passed = true
                  Counterexample = None }

    /// The verdicts of several cells, in the order given — the tail of every family.
    let results (cells: LawCell list) : LawResult list = cells |> List.map (fun c -> c.Result)

    /// A cursor over `ConfRng`: the one mutable draw position a family threads through its
    /// iteration, so a body reads `let tree = rng.Draw gen.Tree` where it used to read
    /// `let tree, r1 = gen.Tree rng` and `rng <- r1`. Draw ORDER is what makes a seed replay, so a
    /// family ported onto it draws in exactly the order it drew before.
    type Draws(seed: int) =
        let mutable state = ConfRng.ofSeed seed

        /// The underlying state — for a helper that takes and returns `ConfRng.T` itself.
        member _.State
            with get () = state
            and set (v: ConfRng.T) = state <- v

        /// Draw once through `f`, advancing the cursor.
        member _.Draw(f: ConfRng.T -> 'a * ConfRng.T) : 'a =
            let v, s = f state
            state <- s
            v

        member d.IntBelow(n: int) : int = d.Draw(ConfRng.intBelow n)

        member d.Choose(xs: 'a list) : 'a = d.Draw(ConfRng.choose xs)

        member d.Shuffle(xs: 'a list) : 'a list = d.Draw(ConfRng.shuffle xs)

    /// The loop every family runs: `iterations` bodies over one cursor seeded from `seed`, each
    /// handed the cursor, its index and the counterexample stamp for that index.
    let run (iterations: int) (seed: int) (body: Draws -> int -> (string -> string) -> unit) : unit =
        let rng = Draws seed

        for i in 0 .. iterations - 1 do
            body rng i (failAt seed i)

    /// The container capability a generator declares, total: `None` means every node can hold
    /// children (Phase 251).
    let canHoldOf (gen: OpGen<'Node, 'Id>) : 'Node -> bool =
        gen.CanHold |> Option.defaultValue (fun _ -> true)

    // ---- the op generator ----------------------------------------------------------------------

    /// The op kinds `genOp` draws, in the order it rolls them — the dimension the op-kind guard
    /// counts. `update` is the identity update unless an `UpdateGen` is supplied.
    let opKinds: string list =
        [ "insert"; "remove"; "move"; "reorder"; "batch"; "update" ]

    /// The kind a drawn op is, in the vocabulary of `opKinds`.
    let opKindOf (op: SkeletonOp<'Node, 'Id>) : string =
        match op with
        | InsertChild _ -> "insert"
        | RemoveNode _ -> "remove"
        | MoveNode _ -> "move"
        | ReorderChildren _ -> "reorder"
        | Batch _ -> "batch"
        | UpdateNode _ -> "update"

    /// One STRUCTURAL op against `tree` — the four kinds that need no content: an insert of a fresh
    /// node under a drawn parent, a remove, a move, a reorder. Possibly invalid (a remove of the
    /// root, a move under a descendant): the laws handle Ok and Error, and the refusals are what a
    /// `Guarded` family counts.
    let genStructuralOp
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (tree: 'Node)
        (rng: ConfRng.T)
        : SkeletonOp<'Node, 'Id> * ConfRng.T =
        let ids = Tree.preorder nodew tree |> List.map nodew.Id
        let idKeys = ids |> List.map idw.ToString |> Set.ofList
        let kind, r1 = ConfRng.intBelow 4 rng

        match kind with
        | 0 ->
            let parent, r2 = ConfRng.choose ids r1
            let fresh, r3 = gen.FreshNode idKeys r2
            InsertChild(parent, fresh), r3
        | 1 ->
            let target, r2 = ConfRng.choose ids r1
            RemoveNode target, r2
        | 2 ->
            let target, r2 = ConfRng.choose ids r1
            let np, r3 = ConfRng.choose ids r2
            MoveNode(target, np), r3
        | _ ->
            let parent, r2 = ConfRng.choose ids r1

            match Tree.tryFind nodew idw parent tree with
            | Some p ->
                let kids = nodew.Children p |> List.map nodew.Id
                let shuffled, r3 = ConfRng.shuffle kids r2
                ReorderChildren(parent, shuffled), r3
            | None -> ReorderChildren(parent, []), r2

    /// Generate one (possibly-invalid) op against `tree`, drawing EVERY kind (Phase 297): the four
    /// structural kinds, a `Batch` of one to three structural ops, and an `UpdateNode` — a domain
    /// content edit through `updates`, or the identity update of a drawn node when the domain
    /// supplies none. Until this phase only the four structural kinds were drawn, so `opAlgebra`
    /// never certified `apply ∘ invert` for `UpdateNode`, `footprintLaws` never reached
    /// `ContentWrites`, and `mergeConflictLaws` never reached `ConcurrentUpdate`.
    ///
    /// A `Batch` is built from ops drawn against the SAME pre-state, not threaded, so its later
    /// members may be refused by the tree its earlier members leave — which is exactly the
    /// atomicity a batch is about, and one more refusal population the guards count.
    let genOpWith
        (updates: UpdateGen<'Node> option)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (tree: 'Node)
        (rng: ConfRng.T)
        : SkeletonOp<'Node, 'Id> * ConfRng.T =
        let kind, r1 = ConfRng.intBelow 6 rng

        match kind with
        | 0
        | 1
        | 2
        | 3 ->
            // The structural roll is re-drawn so that `genStructuralOp` keeps one draw sequence
            // whether it is reached from here or called directly.
            genStructuralOp nodew idw gen tree r1
        | 4 ->
            let n, r2 = ConfRng.intBelow 3 r1
            let mutable r = r2
            let mutable ops = []

            for _ in 0..n do
                let op, r' = genStructuralOp nodew idw gen tree r
                r <- r'
                ops <- ops @ [ op ]

            Batch ops, r
        | _ ->
            let nodes = Tree.preorder nodew tree
            let target, r2 = ConfRng.choose nodes r1

            match updates with
            | Some u ->
                let edited, r3 = u.Update target r2
                UpdateNode edited, r3
            | None -> UpdateNode target, r2

    /// `genOpWith None` — every kind, with the identity update standing in for a content edit.
    let genOp
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (tree: 'Node)
        (rng: ConfRng.T)
        : SkeletonOp<'Node, 'Id> * ConfRng.T =
        genOpWith None nodew idw gen tree rng

    /// The op-kind adequacy guard: the counts of each kind a family's sample drew, as the
    /// `reached` law keyed to the family's roster id. Every kind in `opKinds` is demanded, because
    /// `genOp` draws every kind — a family whose sample missed one drew too few ops to certify it.
    let opKindGuard (family: string) (seed: int) (counts: Map<string, int>) : LawResult =
        SampleAdequacy.reached
            family
            "op kind"
            seed
            (opKinds
             |> List.map (fun k -> k, (counts |> Map.tryFind k |> Option.defaultValue 0)))

    /// A mutable op-kind tally — `Note op` per drawn op, `Counts` for the guard.
    type OpKindTally() =
        let mutable counts: Map<string, int> = Map.empty

        member _.Note(op: SkeletonOp<'Node, 'Id>) =
            let k = opKindOf op
            counts <- counts |> Map.add k ((counts |> Map.tryFind k |> Option.defaultValue 0) + 1)

        member _.Counts = counts

    // ---- shared builders -----------------------------------------------------------------------

    /// Thread up to `n` random ops through the container-aware `apply`, keeping the accepted ones —
    /// an applyable script over `tree`. Copied five times before Phase 297 (footprint, merge
    /// conflict, reconcile, concurrency, arbitration); one copy now. `tally`, when given, counts
    /// the kind of every op DRAWN — accepted or not — for the op-kind guard.
    let collectScript
        (tally: OpKindTally option)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (n: int)
        (tree: 'Node)
        (r0: ConfRng.T)
        : SkeletonOp<'Node, 'Id> list * ConfRng.T =
        let canHold = canHoldOf gen
        let mutable cur = tree
        let mutable accepted = []
        let mutable r = r0

        for _ in 1..n do
            let op, r' = genOp nodew idw gen cur r
            r <- r'

            match tally with
            | Some t -> t.Note op
            | None -> ()

            match Ops.applyContained canHold nodew idw op cur with
            | Ok t' ->
                cur <- t'
                accepted <- accepted @ [ op ]
            | Error _ -> ()

        accepted, r

    /// `n` drawn ops threaded through `OpStream.append` from `gen.State0` under the `Human "conf"`
    /// actor: the chain a stream family builds per iteration. Returns the live state, the records
    /// and how many appends the domain ACCEPTED — a refused op does not extend the chain, and the
    /// count is what a `Guarded` family's accepted-op guard reads. Six ops (`buildChain`) was the
    /// shape copied into eight families before Phase 297.
    let buildChainN
        (n: int)
        (hashFn: HashFn)
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (rng: Draws)
        : 'State * OpRecord<'Op> list * int =
        let mutable state = gen.State0
        let mutable recs = OpStream.empty
        let mutable accepted = 0

        for _ in 1..n do
            let op = rng.Draw gen.Op

            match OpStream.append hashFn sw (Human "conf") op state recs with
            | Ok(s', recs') ->
                accepted <- accepted + 1
                state <- s'
                recs <- recs'
            | Error _ -> ()

        state, recs, accepted

    /// `buildChainN 6`.
    let buildChain
        (hashFn: HashFn)
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (rng: Draws)
        : 'State * OpRecord<'Op> list * int =
        buildChainN 6 hashFn sw gen rng

    // ---- the int witness and codec -------------------------------------------------------------

    /// The kit's own fixture stream witness — an int state whose ops add to it, encoded as the
    /// decimal text of the op. The seed-only fixture families (the chain-break and DAG-break
    /// reasons) build their chains over it; each declared its own copy before Phase 297.
    let intWitness: StreamWitness<int, int, string> =
        { Apply = fun op state -> Ok(state + op)
          Encode = string
          Decode =
            fun s ->
                match System.Int32.TryParse s with
                | true, v -> Ok v
                | false, _ -> Error("not an int: " + s) }
