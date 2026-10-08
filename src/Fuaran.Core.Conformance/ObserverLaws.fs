namespace Fuaran.Core

/// The observer family (Phase 298): the teeth on `Fuaran.Core.Observer`'s witness.
module internal ObserverLaws =

    /// The observer laws (Phase 298), over a domain's `ObserverWitness` and an input generator:
    ///
    ///  - **every emission is the derivation of its input** — a drawn registration script (nodes,
    ///    parents, inputs, then updates and an unregistration) driven through the witness functions
    ///    emits, and leaves as each node's snapshot, exactly `ObserverWitness.derive` of that node's
    ///    input, recomputed fresh — the determinism contract that lets a host's observer be checked
    ///    against a recomputation; and an unregistered node has no snapshot;
    ///  - **a cyclic parent declaration terminates** — nodes declared under each other in a ring
    ///    are walked once each from every member, and the walk ends.
    ///
    /// (Until `1.0.0` a first law held the witness functions equal to the `InMemoryObserver`
    /// adapter and a third held the adapter's subscribers isolated from re-entry; the adapter left
    /// at `1.0.0` — Phase 386 — and subscription is host state.)
    ///
    /// Every arm is built on every iteration, so the family carries no guard.
    let observerLaws<'Input, 'Flag when 'Input: equality and 'Flag: equality>
        (w: ObserverWitness<'Input, 'Flag>)
        (genInput: ConfRng.T -> 'Input * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let derived =
            LawKit.LawCell
                "every emission is the derivation of its input (each emitted observation and each snapshot is derive of its node's input; an unregistered node has none)"

        let cyclic =
            LawKit.LawCell "a cyclic parent declaration terminates (each ring member walked once)"

        LawKit.run iterations seed (fun rng _ at ->
            // ---- every emission is the derivation of its input ----
            let n = 2 + rng.IntBelow 5
            let ids = [ for k in 0 .. n - 1 -> "n" + string k ]

            // each node's parent: none, or any node of the script (a later one makes a forward
            // edge, the node itself or an earlier descendant a cycle — all legal declarations)
            let script =
                ids
                |> List.map (fun id ->
                    let parent =
                        match rng.IntBelow 3 with
                        | 0 -> None
                        | _ -> Some(rng.Choose ids)

                    id, parent, rng.Draw genInput)

            let updates =
                [ for _ in 1 .. 1 + rng.IntBelow 4 -> rng.Choose ids, rng.Draw genInput ]

            let dropped = rng.Choose ids

            let mutable st = ObserverWitness.empty
            let emitted = ResizeArray<string * Observation<'Input, 'Flag>>()

            for (id, parent, input) in script do
                let st', o = ObserverWitness.register w id input parent st
                st <- st'
                emitted.Add(id, o)

            for (id, input) in updates do
                let st', o = ObserverWitness.update w id input st
                st <- st'
                o |> Option.iter (fun o -> emitted.Add(id, o))

            st <- ObserverWitness.unregister dropped st

            for (id, o) in emitted do
                derived.Check(
                    (o = ObserverWitness.derive w id o.Input),
                    fun () -> at (sprintf "an emission for %s is not the derivation of its input" id)
                )

            for id in ids do
                match ObserverWitness.snapshot st id with
                | Some o ->
                    derived.Check(
                        (id <> dropped && o = ObserverWitness.derive w id o.Input),
                        fun () ->
                            at (
                                sprintf
                                    "the snapshot of %s is not the derivation of its input, or outlived its unregistration"
                                    id
                            )
                    )
                | None ->
                    derived.Check((id = dropped), fun () -> at (sprintf "the registered node %s has no snapshot" id))

            // ---- a cyclic parent declaration terminates ----
            let ring = 1 + rng.IntBelow 4
            let members = [ for k in 0 .. ring - 1 -> "r" + string k ]
            let input = rng.Draw genInput
            let mutable rs = ObserverWitness.empty

            members
            |> List.iteri (fun k id ->
                let parent = members[(k + 1) % ring]
                rs <- fst (ObserverWitness.register w id input (Some parent) rs))

            for id in members do
                let walked = ObserverWitness.observeTree rs id |> List.map (fun o -> o.NodeId)

                cyclic.Check(
                    (List.sort walked = List.sort members),
                    fun () -> at (sprintf "the walk from %s over a %d-ring returned %A" id ring walked)
                ))

        LawKit.results [ derived; cyclic ]
