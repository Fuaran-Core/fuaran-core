namespace Fuaran.Core

open Fuaran.Core.Observer

/// The observer family (Phase 298): the teeth on `Fuaran.Core.Observer`'s witness.
module internal ObserverLaws =

    /// The observer laws (Phase 298), over a domain's `ObserverWitness` and an input generator:
    ///
    ///  - **in-memory equals live** — a drawn registration script (nodes, parents, inputs, then
    ///    updates and an unregistration) driven through the witness functions and through the
    ///    `InMemoryObserver` adapter yields the same emissions in the same order and the same
    ///    snapshot and tree walk from every id; and every snapshot's flags are the derivation of
    ///    its input (`ObserverWitness.derive`), recomputed fresh — the determinism contract that
    ///    lets a live observer be checked against the in-memory one;
    ///  - **a cyclic parent declaration terminates** — nodes declared under each other in a ring
    ///    are walked once each from every member, in both forms, and the walk ends;
    ///  - **re-entrant subscribers are isolated** — a subscriber that disposes itself and
    ///    subscribes another during its callback neither throws out of the registration nor
    ///    costs a subscriber present when the emission began its delivery.
    ///
    /// Every arm is built on every iteration, so the family carries no guard.
    let observerLaws<'Input, 'Flag when 'Input: equality and 'Flag: equality>
        (w: ObserverWitness<'Input, 'Flag>)
        (genInput: ConfRng.T -> 'Input * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let agree =
            LawKit.LawCell
                "in-memory equals live (the witness functions and the adapter agree, and every snapshot is the derivation of its input)"

        let cyclic =
            LawKit.LawCell "a cyclic parent declaration terminates (each ring member walked once)"

        let reentrant =
            LawKit.LawCell "re-entrant subscribers are isolated (no throw, no in-flight subscriber skipped)"

        LawKit.run iterations seed (fun rng _ at ->
            // ---- in-memory equals live ----
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

            let adapter = InMemoryObserver.createWith w.Derive w.Options
            let heard = ResizeArray<string * Observation<'Input, 'Flag>>()
            use _ = (adapter :> IObserver<_, _>).Subscribe(fun e -> heard.Add e)

            let mutable st = ObserverWitness.empty
            let emitted = ResizeArray<string * Observation<'Input, 'Flag>>()

            for (id, parent, input) in script do
                let st', o = ObserverWitness.register w id input parent st
                st <- st'
                emitted.Add(id, o)

                match parent with
                | Some p -> adapter.RegisterNode(id, input, p)
                | None -> adapter.RegisterNode(id, input)

            for (id, input) in updates do
                let st', o = ObserverWitness.update w id input st
                st <- st'
                o |> Option.iter (fun o -> emitted.Add(id, o))
                adapter.Update(id, input)

            st <- ObserverWitness.unregister dropped st
            (adapter :> IObserver<_, _>).Unregister dropped

            agree.Check(
                (List.ofSeq emitted = List.ofSeq heard),
                fun () -> at "the adapter's subscriber heard different emissions from the witness functions"
            )

            for id in ids do
                let live = (adapter :> IObserver<_, _>).Observe id

                agree.Check(
                    (ObserverWitness.snapshot st id = live),
                    fun () -> at (sprintf "the snapshot of %s differs between the witness and the adapter" id)
                )

                agree.Check(
                    (ObserverWitness.observeTree st id = (adapter :> IObserver<_, _>).ObserveTree id),
                    fun () -> at (sprintf "the tree walk from %s differs between the witness and the adapter" id)
                )

                match ObserverWitness.snapshot st id with
                | Some o ->
                    agree.Check(
                        (o = ObserverWitness.derive w id o.Input),
                        fun () -> at (sprintf "the snapshot of %s is not the derivation of its input" id)
                    )
                | None -> ()

            // ---- a cyclic parent declaration terminates ----
            let ring = 1 + rng.IntBelow 4
            let members = [ for k in 0 .. ring - 1 -> "r" + string k ]
            let input = rng.Draw genInput
            let ringAdapter = InMemoryObserver.createWith w.Derive w.Options
            let mutable rs = ObserverWitness.empty

            members
            |> List.iteri (fun k id ->
                let parent = members[(k + 1) % ring]
                rs <- fst (ObserverWitness.register w id input (Some parent) rs)
                ringAdapter.RegisterNode(id, input, parent))

            for id in members do
                let walked = ObserverWitness.observeTree rs id |> List.map (fun o -> o.NodeId)

                let walkedLive =
                    (ringAdapter :> IObserver<_, _>).ObserveTree id |> List.map (fun o -> o.NodeId)

                cyclic.Check(
                    (List.sort walked = List.sort members && walkedLive = walked),
                    fun () -> at (sprintf "the walk from %s over a %d-ring returned %A" id ring walked)
                )

            // ---- re-entrant subscribers are isolated ----
            let host = InMemoryObserver.createWith w.Derive w.Options
            let io = host :> IObserver<_, _>
            let calls = ResizeArray<string>()
            let mutable self: System.IDisposable option = None

            let selfish =
                io.Subscribe(fun _ ->
                    calls.Add "selfish"
                    self |> Option.iter (fun d -> d.Dispose())
                    io.Subscribe(fun _ -> calls.Add "late") |> ignore)

            self <- Some selfish
            use _ = io.Subscribe(fun _ -> calls.Add "steady")

            let threw =
                try
                    host.RegisterNode("x", rng.Draw genInput)
                    false
                with _ ->
                    true

            reentrant.Check(
                (not threw && List.ofSeq calls = [ "selfish"; "steady" ]),
                fun () ->
                    at (sprintf "a re-entrant subscriber threw=%b or reordered delivery: %A" threw (List.ofSeq calls))
            ))

        LawKit.results [ agree; cyclic; reentrant ]
