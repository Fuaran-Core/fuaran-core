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
    /// A DAG node the kit builds itself — fresh actors, parents it just appended — cannot be refused
    /// (Phase 296); a refusal here is a defect in the kit, raised with the fault's own text.
    let dagBuilt (r: Result<string * Dag.T<'Op>, DagAppendFault>) : string * Dag.T<'Op> =
        match r with
        | Ok built -> built
        | Error f -> invalidOp ("the law kit built a DAG node the DAG refused: " + DagAppendFault.toString f)

    let failAt (seed: int) (i: int) (msg: string) : string =
        "seed=" + string seed + " iter=" + string i + ": " + msg

    /// The remedy a never-reached law names. It is the sample's fault and not the law's, which is
    /// why the words are `SampleAdequacy`'s.
    [<Literal>]
    let private unreachedRemedy =
        " — the law asserted NOTHING in this run: no iteration reached the arm that builds its evidence, so a pass here would certify nothing (Phase 302). WIDEN THE GENERATOR; raising the iteration count or hunting a seed until the arm is reached leaves the law certified by one trial"

    /// One law under test. The first counterexample is kept and every later one discarded — a law
    /// is reported once, with the earliest seed-stamped case — and the EVIDENCE count is what the
    /// non-degeneracy rule reads (Phase 302): a family cannot report a green verdict over a law it
    /// never asserted. That rule is the runner's shape rather than any family's discipline: the
    /// audit that opened Phase 302 found `certify` green under a constant encoder and `hashFnLaws`
    /// green under a constant `HashFn`, because a gated arm skipped and nothing counted the skip.
    /// Here the count is taken at the assertion.
    ///
    /// **Two cells, one rule — where the zero is REPORTED.** A law asserted only inside an arm the
    /// family's own adequacy guard COUNTS is constructed `LawCell(name, coveredBy = <the guard's
    /// dimension>)`: a starved arm is then reported ONCE, by the guard that names the dimension and
    /// the remedy (`SampleAdequacy.reached`, whose red is what `SampleAdequacy.cases` renders as
    /// `vacuous (<dimension>)`), and the covered cell reads through it — green with zero evidence,
    /// exactly as the families have reported a skipped arm since Phase 121, because the guard
    /// beside it is red and the family's verdict with it. A law whose arm NO guard counts is
    /// constructed `LawCell(name)` and is STRICT: at zero evidence it reds itself, naming the
    /// remedy, so an uncounted gate can never read green. The doctrine is the one Phase 121 set
    /// and this phase closes the hole in: every gated arm is either counted by a named guard or
    /// held by the cell — never neither.
    type LawCell(name: string, coveredBy: string option) =
        let mutable failure: string option = None
        let mutable evidence = 0

        /// A strict cell: no guard counts its arm, so it holds the zero itself.
        new(name: string) = LawCell(name, None)

        /// The law's text, as `LawResult.Law` will carry it.
        member _.Name = name

        /// The guard dimension that counts this law's arm, when one does.
        member _.CoveredBy = coveredBy

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

        /// The verdict. Red on a recorded counterexample; on zero evidence red, naming the remedy,
        /// unless a guard covers the arm (see the type's note); green otherwise.
        member _.Result: LawResult =
            match failure with
            | Some cx ->
                { Law = name
                  Passed = false
                  Counterexample = Some cx }
            | None when evidence <= 0 && coveredBy.IsNone ->
                { Law = name
                  Passed = false
                  Counterexample = Some(SampleAdequacy.neverReached + unreachedRemedy) }
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

    /// How many times a tamper or collision arm redraws its replacement before it gives up
    /// (Phase 302).
    [<Literal>]
    let redrawBound = 16

    /// Draw a REPLACEMENT until it differs from what it replaces, at most `redrawBound` times
    /// (Phase 302). A tamper arm is gated on the replacement being genuinely different — an op
    /// that encodes like the one it replaces is no forgery — and a single draw that happens to
    /// coincide skipped the arm. Redrawing makes the arm reached on every iteration the
    /// generator CAN distinguish; `None` means it could not in `redrawBound` draws, and the
    /// arm's guard or strict cell then reports the zero rather than a green over nothing.
    let drawDistinct (rng: Draws) (draw: ConfRng.T -> 'a * ConfRng.T) (differs: 'a -> bool) : 'a option =
        let mutable found = None
        let mutable k = 0

        while found.IsNone && k < redrawBound do
            let v = rng.Draw draw

            if differs v then
                found <- Some v

            k <- k + 1

        found

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

    /// The op-kind adequacy tally — `Note op` per drawn op, `Demands` for the guard. Every kind in
    /// `opKinds` is demanded, because `genOp` draws every kind: a family whose sample missed one
    /// drew too few ops to certify the laws over the algebra the engine ships. The demand list is
    /// FOLDED into each op-drawing family's existing guard (its dimension reads "… and op kind")
    /// rather than emitted as a guard of its own, so a reader that pins the family's result count
    /// sees no new result and the kinds are demanded all the same.
    type OpKindTally() =
        let mutable counts: Map<string, int> = Map.empty

        member _.Note(op: SkeletonOp<'Node, 'Id>) =
            let k = opKindOf op
            counts <- counts |> Map.add k ((counts |> Map.tryFind k |> Option.defaultValue 0) + 1)

        member _.Counts = counts

        /// Every kind with its count, in `opKinds` order.
        member _.Demands: (string * int) list =
            opKinds
            |> List.map (fun k -> k, (counts |> Map.tryFind k |> Option.defaultValue 0))

    /// Does the domain's per-node `encode` tell drawn nodes apart at all? — Phase 302. The four
    /// confluence families (`footprintLaws`, `concurrencyLawsWith`, `reconcileLawsWith`,
    /// `arbitrationLaws`) compare result trees through `Tree.encodeHash nodew encode`, so an
    /// `encode` that ignores its input collapses every comparison to tree SHAPE and the families
    /// stay green over a comparison that cannot fail on content. `NoteTree` per drawn tree; the
    /// count is of drawn nodes whose encoding differs from the first one the run saw, so a demand
    /// of one is "encode distinguished at least two drawn nodes". Folded into each family's existing
    /// guard, as `OpKindTally` is, so no reader that pins a result count sees a new result.
    type EncodeSpread<'Node, 'Id>(nodew: NodeWitness<'Node, 'Id>, encode: 'Node -> string) =
        let mutable first: string option = None
        let mutable distinguished = 0

        member _.Note(n: 'Node) =
            let e = encode n

            match first with
            | None -> first <- Some e
            | Some f ->
                if e <> f then
                    distinguished <- distinguished + 1

        member s.NoteTree(tree: 'Node) =
            for n in Tree.preorder nodew tree do
                s.Note n

        /// The demand, as a guard count.
        member _.Demand: string * int = "encode-distinguished node", distinguished

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

    /// Draw a pair of NON-EMPTY one-op scripts over `tree` that `footprintOf` declares independent,
    /// at most `redrawBound` times (Phase 302). The four-op scripts the confluence families draw
    /// almost always carry a remove, a move or an update, whose unknown-parent write interferes with
    /// every structure write — measured at this repository's reference witness, `footprintLaws` drew
    /// three hundred pairs and not one independent pair of non-empty scripts, so its soundness law
    /// had been asserted over empty scripts only. A law about independent pairs is given some to
    /// read; `None` means none was found, and the family's guard reports the zero.
    let drawIndependentPair
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (footprintOf: SkeletonOp<'Node, 'Id> list -> Footprint)
        (tree: 'Node)
        (rng: Draws)
        : (SkeletonOp<'Node, 'Id> list * SkeletonOp<'Node, 'Id> list) option =
        let mutable found = None
        let mutable k = 0

        while found.IsNone && k < redrawBound do
            let a = rng.Draw(collectScript None nodew idw gen 1 tree)
            let b = rng.Draw(collectScript None nodew idw gen 1 tree)

            if
                not (List.isEmpty a)
                && not (List.isEmpty b)
                && Ops.independent (footprintOf a) (footprintOf b)
            then
                found <- Some(a, b)

            k <- k + 1

        found

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

    // ---- the DAG builder -----------------------------------------------------------------------

    /// The fork-and-merge DAG the DAG families build per iteration, over four ops the caller has
    /// already drawn: a genesis `g` carrying `op0`; `a` and `b`, both children of `g`, carrying
    /// `opA` and `opB`; and `m`, the merge of `(a, b)` carrying `opM` — every node shape the walker
    /// meets, under the `Human "conf"` actor. `bFirst` appends `b` before `a` (the merge's parent
    /// order stays `(a, b)`), which is how `dagLaws` builds the same logical history twice and asks
    /// whether it converges. The ops are parameters rather than draws because the two families
    /// draw them differently (`dagLaws` through its `StreamGen`, `dagBreakReasonLaws` as ints), and
    /// each keeps its own draw order. Returns the four content ids and the DAG. Copied into both
    /// families before Phase 297.
    let randomDag
        (hashFn: HashFn)
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (bFirst: bool)
        (op0: 'Op)
        (opA: 'Op)
        (opB: 'Op)
        (opM: 'Op)
        : string * string * string * string * Dag.T<'Op> =
        let actor = Human "conf"
        let g, d1 = Dag.append hashFn sw actor op0 "" Dag.empty |> dagBuilt

        let a, b, d3 =
            if bFirst then
                let b, d2 = Dag.append hashFn sw actor opB g d1 |> dagBuilt
                let a, d3 = Dag.append hashFn sw actor opA g d2 |> dagBuilt
                a, b, d3
            else
                let a, d2 = Dag.append hashFn sw actor opA g d1 |> dagBuilt
                let b, d3 = Dag.append hashFn sw actor opB g d2 |> dagBuilt
                a, b, d3

        let m, dag = Dag.merge hashFn sw actor opM a b d3 |> dagBuilt
        g, a, b, m, dag

    // ---- the dispatch-seam runner ----------------------------------------------------------------

    /// The wording a dispatch-seam family renders its verdicts in — the half of `SeamAdapter` that is
    /// text and the typed error a body failure becomes. The two seams name the same three laws in
    /// their own words ("body" at a capability, "resolver" at a query) and answer a body failure with
    /// their own error case (`BodyFailed` / `ExecutionFailed`); every counterexample the runner
    /// writes is built from these so that each family reads exactly as it did before Phase 297.
    type SeamRendering<'Err> =
        {
            /// The family's roster id (`Conformance.<entry>`), which the adequacy guard is named by.
            Family: string
            /// The three law texts in result order: the three outcomes, the refusal preceding the
            /// body, and the host agreeing with its registry.
            Laws: string * string * string
            /// What the family calls the code a dispatch runs — "body" or "resolver".
            Runner: string
            /// The name of the typed error a body failure becomes — "BodyFailed" / "ExecutionFailed".
            BodyFailedName: string
            /// Read the body's own failure out of the seam's error, when the error is that case.
            TryBodyFailed: 'Err -> string option
            /// The typed error the registry answers a body failure with.
            BodyFailed: string -> 'Err
        }

    /// What the two dispatch-seam families differ in, so that ONE runner (`seamLaws`) certifies both
    /// the capability seam (`capabilityLawsWith`) and the query seam (`queryLawsWith`) — the body they
    /// shared, line for line, before Phase 297. `'Entry` is what the registry holds (a `Capability`, a
    /// `Query`), `'Args` a call's arguments, `'v` the body's payload and `'Err` the seam's typed
    /// refusal.
    type SeamAdapter<'Entry, 'Args, 'v, 'Err> =
        {
            /// The registry's lookup: the entry under an id, or the registry's own not-found refusal
            /// naming the ids it holds.
            Lookup: string -> Result<'Entry, 'Err>
            /// The registry's argument validation for an entry.
            Validate: 'Entry -> 'Args -> Result<unit, 'Err>
            /// The domain's body at a call's arguments, in the shape the runner counts: the runner
            /// wraps it so that every run is recorded before the answer is handed back.
            Body: 'Args -> 'Entry -> Deferred<'v>
            /// The host path: dispatch an id with arguments through the (counted) body the runner
            /// hands it.
            Dispatch: string -> 'Args -> ('Entry -> Deferred<'v>) -> Result<Deferred<'v>, 'Err>
            /// The drawn call: an id and its arguments, from the domain's generator.
            Gen: ConfRng.T -> (string * 'Args) * ConfRng.T
            /// The rendering the two families differ in.
            Rendering: SeamRendering<'Err>
        }

    /// The dispatch-seam laws at a DOMAIN'S seam (Phase 246), over whichever seam `adapter` names.
    /// Every call the generator draws goes through the adapter's `Dispatch` with its `Body`,
    /// counted, and the runner certifies the three laws the rendering names: every dispatch settles,
    /// stays pending or is refused typed (`Ok(Failed _)` never escapes, and the body-failure error
    /// carries the body's own failure); a typed refusal ran no body and a dispatched call ran it
    /// exactly once; and a call reaches the body iff the registry admits it, a refused call carrying
    /// the registry's own error. Guarded on the three outcomes ("dispatch outcome"); the second and
    /// third laws are asserted only on a dispatch that returned, so a run in which they assert
    /// nothing is one the guard counts nothing in, and they read through it (covered cells).
    let seamLaws (adapter: SeamAdapter<'Entry, 'Args, 'v, 'Err>) (seed: int) (iterations: int) : LawResult list =
        let r = adapter.Rendering
        let outcomesLaw, precedesLaw, agreementLaw = r.Laws
        let outcomes = LawCell outcomesLaw
        let precedes = LawCell(precedesLaw, Some "dispatch outcome")
        let agreement = LawCell(agreementLaw, Some "dispatch outcome")
        let mutable settled = 0
        let mutable pending = 0
        let mutable refused = 0

        run iterations seed (fun rng _ at ->
            let id, args = rng.Draw adapter.Gen

            let runs = ref 0
            let answered = ref None

            let counted (entry: 'Entry) =
                runs.Value <- runs.Value + 1
                let a = adapter.Body args entry
                answered.Value <- Some a
                a

            let outcome =
                try
                    Ok(adapter.Dispatch id args counted)
                with ex ->
                    Error ex.Message

            match outcome with
            | Error m -> outcomes.Check(false, fun () -> at (sprintf "dispatching %s threw: %s" id m))
            | Ok o ->
                // ---- three outcomes ----
                (match o with
                 | Ok(Failed m) ->
                     outcomes.Check(false, fun () -> at (sprintf "Ok(Failed %s) escaped the seam for %s" m id))
                 | Error e ->
                     (match r.TryBodyFailed e, answered.Value with
                      | Some m, Some(Failed fm) when m = fm -> outcomes.Saw()
                      | Some m, a ->
                          outcomes.Check(
                              false,
                              fun () ->
                                  at (sprintf "%s %s for %s, but the %s answered %A" r.BodyFailedName m id r.Runner a)
                          )
                      | None, _ -> outcomes.Saw())
                 | Ok _ -> outcomes.Saw())

                // ---- a refusal precedes the body ----
                let dispatchedOnce () =
                    precedes.Check(
                        (runs.Value = 1),
                        fun () ->
                            at (sprintf "%s %A was dispatched and the %s ran %d time(s)" id args r.Runner runs.Value)
                    )

                match o with
                | Ok(Ready _) ->
                    settled <- settled + 1
                    dispatchedOnce ()
                | Ok Pending ->
                    pending <- pending + 1
                    dispatchedOnce ()
                | Error e ->
                    (match r.TryBodyFailed e with
                     | Some _ -> dispatchedOnce ()
                     | None ->
                         refused <- refused + 1

                         precedes.Check(
                             (runs.Value = 0),
                             fun () ->
                                 at (
                                     sprintf
                                         "%s %A was refused (%A) after the %s ran %d time(s)"
                                         id
                                         args
                                         e
                                         r.Runner
                                         runs.Value
                                 )
                         ))
                | Ok(Failed _) -> ()

                // ---- the host is the registry's ----
                let admitted =
                    match adapter.Lookup id with
                    | Error e -> Error e
                    | Ok entry -> adapter.Validate entry args

                let expected =
                    match admitted, answered.Value with
                    | Error e, _ -> Some(Error e)
                    | Ok(), Some(Ready v) -> Some(Ok(Ready v))
                    | Ok(), Some Pending -> Some(Ok Pending)
                    | Ok(), Some(Failed m) -> Some(Error(r.BodyFailed m))
                    | Ok(), None -> None

                match expected with
                | Some e ->
                    agreement.Check(
                        (e = o),
                        fun () -> at (sprintf "%s %A — the registry answers %A, the host answered %A" id args e o)
                    )
                | None ->
                    agreement.Check(
                        false,
                        fun () ->
                            at (
                                sprintf
                                    "%s %A is admitted by the registry, and the host answered %A without running the %s"
                                    id
                                    args
                                    o
                                    r.Runner
                            )
                    ))

        results [ outcomes; precedes; agreement ]
        @ [ SampleAdequacy.reached
                r.Family
                "dispatch outcome"
                seed
                [ "settled", settled; "pending", pending; "refused", refused ] ]
