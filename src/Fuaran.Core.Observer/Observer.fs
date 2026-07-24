namespace Fuaran.Core.Observer

// ─── Fuaran.Core.Observer — the generic runtime-verification seam ───
//
// The runtime analogue of `Fuaran.Core.Validator`. Where the
// validator is the generic *build-time* framework (`RuleFamily` +
// `Registry` + `PackRule`, domain rule packs plugged in), this is the
// generic *runtime* observer: register node → snapshot input → derive
// flags → subscribe. It lifts the pattern that the UI `LayoutObserver`
// established (register element → snapshot geometry → derive layout
// flags → subscribe) out of the UI tier into Core, so every domain
// gets a runtime-verification seam by supplying its own metric
// envelope (`'Input`) + flag vocabulary (`'Flag`) + a pure derivation.
//
// **All flag content stays domain-side.** Core owns no `'Flag` cases
// and no `'Input` shape — exactly as `Core.Validator` owns no rule
// content. A *verification pack* (Music voice-leading drift, Calc
// recompute drift, UI layout breakage) is the same monetizable unit
// as a content pack: a domain flag vocabulary + a derivation.
//
// **FSharp.Core only + Fable-clean.** `Dictionary` / `ResizeArray` /
// `IDisposable` all compile under Fable, so the same engine drives the
// in-memory .NET test substrate and a Fable-compiled host.

open System
open System.Collections.Generic

/// One runtime snapshot for a single registered node: the raw input
/// the derivation saw, plus the derived domain flags. The `Input` is
/// retained (not just the flags) so a domain can project its own
/// richer observation shape — e.g. the UI `LayoutObservation` reads
/// width / height / viewport coordinates back off the input.
type Observation<'Input, 'Flag> =
    { NodeId: string
      Input: 'Input
      Flags: 'Flag list }

/// The pure derivation: a domain's metric envelope → its flag list.
/// MUST be a pure function — no clock, randomness, or network — so the
/// in-memory and live observers agree from identical inputs. That
/// determinism is the contract that makes an automatable verification
/// gate possible (InMemory result == live result for the same input).
type Derivation<'Input, 'Flag> = 'Input -> 'Flag list

/// Host-tunable emit policy, generic over the domain flag vocabulary.
type ObserverOptions<'Flag> =
    {
        /// When true, a node's subscriber callback fires only when its
        /// derived flag set differs from the previous emission. The
        /// initial registration emission always fires regardless.
        EmitOnFlagChangeOnly: bool
        /// Equality over flag sets used by the change-gate. Domains that
        /// want order-insensitive comparison plug it here; the default
        /// is structural list equality.
        FlagsEqual: 'Flag list -> 'Flag list -> bool
    }

module ObserverOptions =
    /// Structural-equality, change-only defaults — the analogue of the
    /// UI `LayoutObserverOptions.defaults` emit policy.
    let defaults<'Flag when 'Flag: equality> : ObserverOptions<'Flag> =
        { EmitOnFlagChangeOnly = true
          FlagsEqual = (=) }

/// The generic runtime-observer contract — three reads (single-node,
/// tree, live subscription) + two registry calls. Domains satisfy it
/// with the in-memory engine below, or with a live host-bound observer
/// (e.g. a browser `ResizeObserver`-backed instance) that feeds the
/// same `'Input` through the same `Derivation`.
type IObserver<'Input, 'Flag> =
    /// Snapshot the observation for a single registered node. `None`
    /// when the node is not currently registered.
    abstract Observe: nodeId: string -> Observation<'Input, 'Flag> option

    /// Snapshot every observation reachable from `rootNodeId`
    /// (inclusive), walking the parent-pointer graph declared at
    /// registration. Empty list when the root is not registered.
    abstract ObserveTree: rootNodeId: string -> Observation<'Input, 'Flag> list

    /// Subscribe to live observation deltas. The handler is invoked per
    /// the configured emit policy with the (nodeId, observation) tuple.
    /// Dispose the returned `IDisposable` to remove the handler.
    abstract Subscribe: handler: (string * Observation<'Input, 'Flag> -> unit) -> IDisposable

    /// Register a node for observation with its current input.
    /// Idempotent-on-key — re-registering replaces the entry.
    abstract Register: nodeId: string * input: 'Input -> unit

    /// Unregister a node. Idempotent — unregistering an unknown NodeId
    /// is a no-op.
    abstract Unregister: nodeId: string -> unit

/// One registry entry: the live input + an optional parent NodeId for
/// the `ObserveTree` walk.
type private Entry<'Input> =
    { Input: 'Input; Parent: string option }

/// In-memory runtime observer — pure-.NET, deterministic, the headless
/// substrate for tests and the automatable verification gate. Generic
/// over a domain's `'Input` envelope + `'Flag` vocabulary; construct
/// with the domain's pure `Derivation` + emit options. Drive
/// `RegisterNode` / `Update`; read via `Observe` / `ObserveTree` /
/// subscriber callbacks. The faithful generic lift of the UI
/// `InMemoryLayoutObserver`.
type InMemoryObserver<'Input, 'Flag>(derive: Derivation<'Input, 'Flag>, options: ObserverOptions<'Flag>) =
    let registry = Dictionary<string, Entry<'Input>>()
    let lastFlagSet = Dictionary<string, 'Flag list>()
    let subscribers = ResizeArray<string * Observation<'Input, 'Flag> -> unit>()

    let toObservation (nodeId: string) (entry: Entry<'Input>) : Observation<'Input, 'Flag> =
        { NodeId = nodeId
          Input = entry.Input
          Flags = derive entry.Input }

    let emit (nodeId: string) (observation: Observation<'Input, 'Flag>) =
        for subscriber in subscribers do
            try
                subscriber (nodeId, observation)
            with ex ->
                // A subscriber throwing must not poison sibling
                // subscribers — mirror the UI observer's isolation.
                ignore ex

    /// Register (or replace) a node with its input and an optional
    /// parent NodeId. Always fires an initial emission, regardless of
    /// `EmitOnFlagChangeOnly` (the initial-emission rule).
    member this.RegisterNode(nodeId: string, input: 'Input, ?parent: string) : unit =
        let entry = { Input = input; Parent = parent }
        registry[nodeId] <- entry
        let observation = toObservation nodeId entry
        lastFlagSet[nodeId] <- observation.Flags
        emit nodeId observation

    /// Replace a registered node's input. Honours
    /// `EmitOnFlagChangeOnly` — fires only when the derived flag set
    /// differs from the previous emission (or always, when the option
    /// is false). No-op if `nodeId` isn't registered.
    member this.Update(nodeId: string, input: 'Input) : unit =
        match registry.TryGetValue(nodeId) with
        | false, _ -> ()
        | true, existing ->
            let next = { existing with Input = input }
            registry[nodeId] <- next
            let observation = toObservation nodeId next

            let previousFlags =
                match lastFlagSet.TryGetValue(nodeId) with
                | true, flags -> flags
                | false, _ -> []

            lastFlagSet[nodeId] <- observation.Flags

            let shouldEmit =
                if options.EmitOnFlagChangeOnly then
                    not (options.FlagsEqual observation.Flags previousFlags)
                else
                    true

            if shouldEmit then
                emit nodeId observation

    interface IObserver<'Input, 'Flag> with
        member _.Observe(nodeId: string) : Observation<'Input, 'Flag> option =
            match registry.TryGetValue(nodeId) with
            | true, entry -> Some(toObservation nodeId entry)
            | false, _ -> None

        member _.ObserveTree(rootNodeId: string) : Observation<'Input, 'Flag> list =
            // Walk children via parent pointers. BFS-shaped so the
            // result is deterministic by tree level then registration
            // order — the same shape the UI observer produces.
            match registry.TryGetValue(rootNodeId) with
            | false, _ -> []
            | true, _ ->
                let children =
                    registry
                    |> Seq.choose (fun kvp ->
                        match kvp.Value.Parent with
                        | Some p -> Some(p, kvp.Key)
                        | None -> None)
                    |> Seq.groupBy fst
                    |> Seq.map (fun (parent, pairs) -> parent, pairs |> Seq.map snd |> Seq.toList)
                    |> Map.ofSeq

                let rec walk (acc: Observation<'Input, 'Flag> list) (queue: string list) =
                    match queue with
                    | [] -> List.rev acc
                    | nodeId :: rest ->
                        let observation =
                            match registry.TryGetValue(nodeId) with
                            | true, entry -> [ toObservation nodeId entry ]
                            | false, _ -> []

                        let kids = Map.tryFind nodeId children |> Option.defaultValue []
                        walk (List.rev observation @ acc) (rest @ kids)

                walk [] [ rootNodeId ]

        member _.Subscribe(handler: string * Observation<'Input, 'Flag> -> unit) : IDisposable =
            subscribers.Add(handler)

            { new IDisposable with
                member _.Dispose() = subscribers.Remove(handler) |> ignore }

        member this.Register(nodeId: string, input: 'Input) : unit = this.RegisterNode(nodeId, input)

        member _.Unregister(nodeId: string) : unit =
            registry.Remove(nodeId) |> ignore
            lastFlagSet.Remove(nodeId) |> ignore

module InMemoryObserver =
    /// Construct with the change-only structural-equality defaults.
    let create<'Input, 'Flag when 'Flag: equality>
        (derive: Derivation<'Input, 'Flag>)
        : InMemoryObserver<'Input, 'Flag> =
        InMemoryObserver(derive, ObserverOptions.defaults)

    /// Construct with host-tunable options.
    let createWith
        (derive: Derivation<'Input, 'Flag>)
        (options: ObserverOptions<'Flag>)
        : InMemoryObserver<'Input, 'Flag> =
        InMemoryObserver(derive, options)
