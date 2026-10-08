namespace Fuaran.Core

// ─── Fuaran.Core.Observer — the generic runtime-verification seam ───
//
// The runtime analogue of `Fuaran.Core.Validator`. Where the
// validator is the generic *build-time* framework (`RuleFamily` +
// `RuleRegistry` + `PackRule`, domain rule packs plugged in), this is the
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
// **The spine's shape (Phase 298).** A witness record (`ObserverWitness`)
// and module functions over a pure state value (`ObserverState`), in
// `namespace Fuaran.Core` like every other seam — the package was the one
// non-IDL package outside it, an OO interface plus a class keyed by raw
// strings. That OO surface (`Fuaran.Core.Observer.IObserver`,
// `InMemoryObserver`) stayed one draft as an adapter over these functions and
// left at `1.0.0` (Phase 386): subscription is host state, so a host keeps its
// subscriber list beside the `ObserverState` it threads.
//
// **FSharp.Core only + Fable-clean.** The state is immutable maps and
// lists.

/// One runtime snapshot for a single registered node: the raw input
/// the derivation saw, plus the derived domain flags. The `Input` is
/// retained (not just the flags) so a domain can project its own
/// richer observation shape — e.g. the UI `LayoutObservation` reads
/// width / height / viewport coordinates back off the input.
type Observation<'Input, 'Flag> =
    {
        /// The registered node the snapshot was taken at — the key the
        /// state and every subscriber callback use.
        NodeId: string
        /// The input the flags were derived from, retained verbatim so a
        /// domain can read its own metrics back off the observation.
        Input: 'Input
        /// `Derive Input`, in the order the derivation produced them; no
        /// de-duplication or sorting is applied.
        Flags: 'Flag list
    }

/// The pure derivation: a domain's metric envelope → its flag list.
/// MUST be a pure function — no clock, randomness, or network — so the
/// in-memory and live observers agree from identical inputs. That
/// determinism is the contract that makes an automatable verification
/// gate possible (InMemory result == live result for the same input),
/// and `Conformance.observerLaws` samples it.
type Derivation<'Input, 'Flag> = 'Input -> 'Flag list

/// Host-tunable emit policy, generic over the domain flag vocabulary.
type ObserverOptions<'Flag> =
    {
        /// When true, a node's emission fires only when its derived flag set
        /// differs from the previous emission. The initial registration
        /// emission always fires regardless.
        EmitOnFlagChangeOnly: bool
        /// Equality over flag sets used by the change-gate. Domains that
        /// want order-insensitive comparison plug it here; the default
        /// is structural list equality.
        FlagsEqual: 'Flag list -> 'Flag list -> bool
    }

/// Stock emit policies for `ObserverOptions`.
module ObserverOptions =
    /// Structural-equality, change-only defaults — the analogue of the
    /// UI `LayoutObserverOptions.defaults` emit policy.
    let defaults<'Flag when 'Flag: equality> : ObserverOptions<'Flag> =
        { EmitOnFlagChangeOnly = true
          FlagsEqual = (=) }

/// The per-domain observer witness (Phase 298): the domain's pure derivation
/// and its emit policy. The core composes registration, snapshots, the tree
/// walk and the change-gate from these; a domain supplies no class.
type ObserverWitness<'Input, 'Flag> =
    {
        /// The domain's pure input-to-flags derivation; it runs before any
        /// state is committed, so a throw leaves the state untouched.
        Derive: Derivation<'Input, 'Flag>
        /// The emit policy `update` consults; `register` ignores it and
        /// always emits.
        Options: ObserverOptions<'Flag>
    }

/// One registered node (Phase 298): its live input, the parent it was
/// declared under (`None` for a root), and the flags last derived from the
/// input — the change-gate's baseline.
type ObserverEntry<'Input, 'Flag> =
    {
        /// The most recent input given to `register` or `update`.
        Current: 'Input
        /// The parent named at registration; `update` never changes it, and
        /// it is not checked to be registered.
        ParentId: string option
        /// The flags derived from `Current`, refreshed on every `update`
        /// whether or not that update emitted.
        LastFlags: 'Flag list
    }

/// The observer's state as a value (Phase 298): the registered entries by node
/// id, and the ids in REGISTRATION order — re-registering an id keeps its
/// place, unregistering removes it. That order is what makes `observeTree`
/// deterministic: a `Dictionary`'s enumeration, which the class used, reuses
/// freed slots after a removal on .NET and does not under Fable.
type ObserverState<'Input, 'Flag> =
    {
        /// The registered nodes keyed by node id.
        Entries: Map<string, ObserverEntry<'Input, 'Flag>>
        /// Exactly the keys of `Entries`, oldest registration first.
        Order: string list
    }

/// How a hand-built `ObserverState` breaks its documented invariant — `Order` is exactly the keys
/// of `Entries`, each once (Phase 383). Both fields are public, so a state no `register` produced can
/// say anything; `ObserverWitness.tryObserveTree` names the first breach rather than walking it.
/// `RequireQualifiedAccess`: `ObserverDefect.UnregisteredInOrder`, …
[<RequireQualifiedAccess>]
type ObserverDefect =
    /// `Order` names `nodeId`, and `Entries` holds no entry for it.
    | UnregisteredInOrder of nodeId: string
    /// `Order` names `nodeId` more than once.
    | RepeatedInOrder of nodeId: string
    /// `Entries` holds `nodeId`, and `Order` does not name it.
    | UnorderedEntry of nodeId: string

/// The observer functions over the witness (Phase 298). Every function is
/// pure: a registration or update returns the new state and the emission it
/// produced, and the host delivers emissions to whatever subscribers it
/// keeps (the adapter below keeps a list).
module ObserverWitness =

    /// The witness for `derive` under the change-only structural defaults.
    let create<'Input, 'Flag when 'Flag: equality>
        (derive: Derivation<'Input, 'Flag>)
        : ObserverWitness<'Input, 'Flag> =
        { Derive = derive
          Options = ObserverOptions.defaults }

    /// The witness for `derive` under host-tunable options.
    let createWith
        (derive: Derivation<'Input, 'Flag>)
        (options: ObserverOptions<'Flag>)
        : ObserverWitness<'Input, 'Flag> =
        { Derive = derive; Options = options }

    /// The empty state: nothing registered.
    let empty<'Input, 'Flag> : ObserverState<'Input, 'Flag> =
        { Entries = Map.empty; Order = [] }

    /// The observation of `input` at `nodeId` — the derivation applied, nothing
    /// registered. What a live observer computes from the same input.
    let derive (w: ObserverWitness<'Input, 'Flag>) (nodeId: string) (input: 'Input) : Observation<'Input, 'Flag> =
        { NodeId = nodeId
          Input = input
          Flags = w.Derive input }

    /// Register (or replace) `nodeId` with `input` under `parent`. Always
    /// emits (the initial-emission rule), so the observation is returned beside
    /// the state. The derivation runs BEFORE anything is committed: a derivation
    /// that throws leaves `st` as it was, where the class wrote the entry first
    /// and a throw left a registered node no read could derive.
    let register
        (w: ObserverWitness<'Input, 'Flag>)
        (nodeId: string)
        (input: 'Input)
        (parent: string option)
        (st: ObserverState<'Input, 'Flag>)
        : ObserverState<'Input, 'Flag> * Observation<'Input, 'Flag> =
        let observation = derive w nodeId input

        let entry =
            { Current = input
              ParentId = parent
              LastFlags = observation.Flags }

        let order =
            if Map.containsKey nodeId st.Entries then
                st.Order
            else
                st.Order @ [ nodeId ]

        { Entries = Map.add nodeId entry st.Entries
          Order = order },
        observation

    /// Replace a registered node's input. The emission honours
    /// `EmitOnFlagChangeOnly`: `Some` when the derived flag set differs from the
    /// last one (or always, when the option is off), `None` otherwise — and
    /// `None` with the state unchanged when `nodeId` is not registered. The
    /// derivation runs before anything is committed.
    let update
        (w: ObserverWitness<'Input, 'Flag>)
        (nodeId: string)
        (input: 'Input)
        (st: ObserverState<'Input, 'Flag>)
        : ObserverState<'Input, 'Flag> * Observation<'Input, 'Flag> option =
        match Map.tryFind nodeId st.Entries with
        | None -> st, None
        | Some existing ->
            let observation = derive w nodeId input

            let emits =
                not w.Options.EmitOnFlagChangeOnly
                || not (w.Options.FlagsEqual observation.Flags existing.LastFlags)

            { st with
                Entries =
                    Map.add
                        nodeId
                        { existing with
                            Current = input
                            LastFlags = observation.Flags }
                        st.Entries },
            (if emits then Some observation else None)

    /// Unregister `nodeId`. Idempotent — an unknown id leaves the state as it is.
    let unregister (nodeId: string) (st: ObserverState<'Input, 'Flag>) : ObserverState<'Input, 'Flag> =
        if Map.containsKey nodeId st.Entries then
            { Entries = Map.remove nodeId st.Entries
              Order = st.Order |> List.filter (fun i -> i <> nodeId) }
        else
            st

    /// The observation of one registered node, `None` when it is not registered.
    let snapshot (st: ObserverState<'Input, 'Flag>) (nodeId: string) : Observation<'Input, 'Flag> option =
        Map.tryFind nodeId st.Entries
        |> Option.map (fun e ->
            { NodeId = nodeId
              Input = e.Current
              Flags = e.LastFlags })

    /// Every observation reachable from `root` (inclusive) over the declared
    /// parent graph, breadth-first — by level, then by registration order
    /// within a level. Empty when `root` is not registered.
    ///
    /// **Total over any declared graph (Phase 298).** The walk carries a
    /// visited set, so a cyclic parent declaration (`a` under `b`, `b` under
    /// `a`) yields each node once and terminates, where the class's walk looped
    /// forever; and the children map is built once per call in one pass over
    /// the registration order, with a two-list queue, where the class rebuilt
    /// it and appended the queue with `@`.
    ///
    /// **Total over any state value (Phase 383).** Both fields of `ObserverState` are public, so an
    /// `Order` id with no entry is read as unregistered — never walked, never a throw — exactly as
    /// `snapshot` reads it. `tryObserveTree` refuses such a state with the `ObserverDefect` it holds.
    let observeTree (st: ObserverState<'Input, 'Flag>) (root: string) : Observation<'Input, 'Flag> list =
        if not (Map.containsKey root st.Entries) then
            []
        else
            let children =
                (Map.empty, List.rev st.Order)
                ||> List.fold (fun (acc: Map<string, string list>) id ->
                    match Map.tryFind id st.Entries |> Option.bind (fun e -> e.ParentId) with
                    | Some p -> Map.add p (id :: (Map.tryFind p acc |> Option.defaultValue [])) acc
                    | None -> acc)

            // `front` is dequeued from; `back` collects reversed; `seen` holds every id ever enqueued
            let rec walk acc (front: string list) (back: string list) (seen: Set<string>) =
                match front, back with
                | [], [] -> List.rev acc
                | [], _ -> walk acc (List.rev back) [] seen
                | id :: rest, _ ->
                    let kids =
                        Map.tryFind id children
                        |> Option.defaultValue []
                        |> List.filter (fun k -> not (Set.contains k seen))

                    let seen' = (seen, kids) ||> List.fold (fun s k -> Set.add k s)
                    let back' = (back, kids) ||> List.fold (fun b k -> k :: b)

                    let acc' =
                        match snapshot st id with
                        | Some o -> o :: acc
                        | None -> acc

                    walk acc' rest back' seen'

            walk [] [ root ] [] (Set.singleton root)

    /// `observeTree` over a state checked first (Phase 383): `Error` with the first breach of the
    /// state's invariant — an `Order` id with no entry, an id `Order` repeats (both in `Order`'s
    /// order), then an entry `Order` omits (in id order) — and `Ok (observeTree st root)` for a state
    /// `register` / `update` / `unregister` could have produced.
    let tryObserveTree
        (st: ObserverState<'Input, 'Flag>)
        (root: string)
        : Result<Observation<'Input, 'Flag> list, ObserverDefect> =
        let rec scan (seen: Set<string>) (ids: string list) =
            match ids with
            | [] ->
                st.Entries
                |> Map.toList
                |> List.tryPick (fun (id, _) ->
                    if Set.contains id seen then
                        None
                    else
                        Some(ObserverDefect.UnorderedEntry id))
            | id :: rest ->
                if not (Map.containsKey id st.Entries) then
                    Some(ObserverDefect.UnregisteredInOrder id)
                elif Set.contains id seen then
                    Some(ObserverDefect.RepeatedInOrder id)
                else
                    scan (Set.add id seen) rest

        match scan Set.empty st.Order with
        | Some defect -> Error defect
        | None -> Ok(observeTree st root)
