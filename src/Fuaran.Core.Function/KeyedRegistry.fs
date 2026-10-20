namespace Fuaran.Core

open System.Runtime.CompilerServices

// ============================================================================
//  The keyed-registry lattice (Phase 316) and the seam dispatch prologue
//  (Phase 509) — ONE implementation of what the invocable seams' registries
//  share: `CapabilityRegistry`, `FunctionRegistry` and `QueryRegistry` each hold
//  their entries as a map keyed by id, and each used to write `register`
//  (refusing a duplicate) / `tryFind` / `enumerate` over it by hand, with
//  nothing to remove, replace, narrow or combine.
//
//  A registry is a LATTICE here, not an append log: `restrict` narrows it to a
//  set of ids (the meet with that set), `union` joins two registries whose ids
//  are disjoint and refuses a collision (no silent overwrite, as `register`),
//  `unregister` removes one id it holds and refuses one it does not, and
//  `replace` swaps the entry under an id it holds, through the seam's own
//  admission gate. Every refusal is the SEAM's own typed error, built by the
//  function the caller passes — so the duplicate and unknown refusals stay
//  `DuplicateCapability`/`NoSuchCapability` on the capability seams and
//  `DuplicateQuery`/`NoSuchQuery` on the query seam.
//
//  Phase 509 widened it from the lattice to DISPATCH (DECISIONS.md D172,
//  amending D117.2): the prologue "resolve the id, else `NoSuch… (id, known)`;
//  validate; admit through the policy; settle the body" was written out at
//  eleven verbs over the three registries, with the `known` list built inline
//  fourteen times and `decide` three times. It is written once below, over a
//  `SeamVocabulary`, with the two journalled shapes beside it, and the
//  registries' verbs forward. The shape is PUBLISHED as `Seam` (D172, amending
//  D118.7), so a registry outside this package is built on the same gate
//  rather than beside it.
//
//  Its registration-ordered twin is `Validator.Ordered` in `Fuaran.Core.Validator`
//  (Phase 521): the same four lifecycle verbs and the same laws over a LIST,
//  because a validator registry's order is its run order (D117.2). It carries
//  the two list-ordered registries, `Validator.RuleRegistry` and `Validator.Rules`
//  (whose instance at a column table is `ColumnValidator.Registry`), so the five
//  registries stand on one lattice in two orders and on no hand-written verb.
//
//  FSharp.Core only, Fable-clean, total.
// ============================================================================

/// A dispatch journalled in the keyed capture journal (Phase 391; a positional triple before
/// `0.37.0`): what `CapabilityRegistry.dispatchCaptured` and the `QueryRegistry.dispatch…Captured`
/// verbs answer. `'e` is the seam's refusal — `InvokeError` for a capability, `QueryError` for a
/// query.
type CapturedDispatch<'v, 'e> =
    {
        /// Exactly what the un-journalled dispatch answers.
        Outcome: Result<Deferred<'v>, 'e>
        /// The invocation key and occurrence the attempt was journalled under — the ticket
        /// `KeyedCapture.settleEffectKeyed` settles a `Pending` answer with later. `None` when nothing
        /// was journalled: a refusal before the body, or a deterministic declaration.
        Ticket: (string * int) option
        /// The journal, with the attempt and its settlement appended when one was journalled.
        Journal: KeyedCapture list
    }

/// Why a replayed dispatch did not answer a value (Phase 391; a nested `Result` before `0.37.0`): the
/// seam's own refusal, which a live dispatch would have answered too, or the journal's inability
/// to answer the invocation.
[<RequireQualifiedAccess>]
type ReplayFailure<'e> =
    /// The seam refused, exactly as the live dispatch refuses — an id it does not hold, arguments
    /// that do not validate, a policy refusal — or the journal recorded the invocation's refusal,
    /// which replays as the refusal it was answered live.
    | Refused of 'e
    /// The journal cannot answer the invocation — `NoCapture`, `Exhausted`, a label mismatch, an
    /// undecodable value. Never answered by a live call.
    | Unanswered of KeyedCaptureFault

/// A dispatch replayed from the keyed capture journal (Phase 391; a nested `Result` and a pair
/// before `0.37.0`): what `CapabilityRegistry.dispatchReplayed` and the
/// `QueryRegistry.dispatchReplayed…` verbs answer.
type ReplayedDispatch<'v, 'e> =
    {
        /// The replayed answer: a value as `Ready`, an attempt that never settled as `Pending`, or
        /// the failure.
        Outcome: Result<Deferred<'v>, ReplayFailure<'e>>
        /// The replay cursor after this invocation — advanced past every journal record the replay
        /// consumed (a recorded refusal included), unchanged when the seam refused before the
        /// journal or the journal could not answer. Thread it into the next replay.
        Cursor: Map<string, int>
    }


/// The vocabulary a seam answers in (Phase 509): how it names a declaration, the typed refusal for
/// each way an invocation or a registration is refused, and the two checks a declaration and an
/// invocation pass. A `Seam` is built from one (`Seam.create`) and every verb answers in it — so the
/// capability seam refuses `NoSuchCapability` / `PolicyRefused` and the query seam `NoSuchQuery` /
/// `QueryPolicyRefused`, from one dispatch path.
type SeamVocabulary<'Decl, 'Args, 'E> =
    {
        /// The id a declaration registers under.
        Id: 'Decl -> string
        /// An invocation names an id the seam does not hold; the list is every id it does hold, in
        /// id order.
        NoSuch: string -> string list -> 'E
        /// A registration names an id the seam already holds.
        Duplicate: string -> 'E
        /// The admission gate a declaration passes at `register` and `replace`; `None` admits it.
        Admission: 'Decl -> 'E option
        /// The arguments of one invocation, checked against the resolved declaration — before the
        /// policy, so a gate only ever sees arguments that validate.
        Validate: 'Decl -> 'Args -> Result<unit, 'E>
        /// A gate answered `Deny`: the gate's name and its guidance.
        Refused: string -> RejectionGuidance -> 'E
        /// A gate answered `NeedsApproval`: the gate's name.
        Approval: string -> 'E
        /// The sentence a refusal reads as — what `decide` puts in a `Deny`'s guidance.
        Describe: 'E -> string
    }

/// How a seam journals an invocation in the keyed capture journal (Phase 509; the shape the
/// capability and query seams have journalled in since Phase 318). Apart from the vocabulary because
/// a seam that never journals needs none of it, and because a key may read more than the arguments
/// (a query's page token).
type SeamCapture<'Decl, 'Args, 'E> =
    {
        /// The determinism label an invocation is journalled under; the `deterministic` label
        /// journals nothing and replays live.
        Label: 'Decl -> string
        /// The invocation key — what a replay looks the invocation up by.
        Key: 'Decl -> 'Args -> string
        /// A refusal the body answered, as the reason the journal records.
        Record: 'E -> string
        /// A recorded reason, read back as the refusal it records.
        Read: string -> 'E
    }

/// The lifecycle algebra over a registry's id-keyed map (Phase 316) and the dispatch prologue over it
/// (Phase 509). Each function is total and leaves the map unchanged on a refusal. The dispatch verbs
/// hold entries of type `'V` and gate a declaration of type `'Decl` read off each by `gated` — the
/// identity for a `Seam`, `_.Capability` for `FunctionRegistry`, whose entries carry a result kind
/// beside the capability its gates read.
module internal KeyedRegistry =

    /// The ids the map holds, in id order — what an unknown-id refusal enumerates.
    let ids (m: Map<string, 'V>) : string list = m |> Map.toList |> List.map fst

    /// Add `v` under `id`: refused `duplicate id` when the map holds `id`, refused `admit v`'s
    /// fault when the seam's admission gate refuses `v`, and the map extended by exactly one entry
    /// otherwise. The duplicate check runs first, as every registry's `register` always has.
    let register
        (duplicate: string -> 'E)
        (admit: 'V -> 'E option)
        (id: string)
        (v: 'V)
        (m: Map<string, 'V>)
        : Result<Map<string, 'V>, 'E> =
        if Map.containsKey id m then
            Error(duplicate id)
        else
            match admit v with
            | Some fault -> Error fault
            | None -> Ok(Map.add id v m)

    /// Remove `id`: refused `unknown id held` (the ids the map holds, in id order) when the map
    /// does not hold it. `unregister id (register id v m) = Ok m` for an id `m` does not hold.
    let unregister
        (unknown: string -> string list -> 'E)
        (id: string)
        (m: Map<string, 'V>)
        : Result<Map<string, 'V>, 'E> =
        if Map.containsKey id m then
            Ok(Map.remove id m)
        else
            Error(unknown id (ids m))

    /// Swap the entry under `id` for `v`: refused `unknown` when the map does not hold `id`, and
    /// refused the admission gate's fault when it refuses `v` — so a replacement is held to exactly
    /// what a registration is. The id is kept; only its entry moves.
    let replace
        (unknown: string -> string list -> 'E)
        (admit: 'V -> 'E option)
        (id: string)
        (v: 'V)
        (m: Map<string, 'V>)
        : Result<Map<string, 'V>, 'E> =
        if not (Map.containsKey id m) then
            Error(unknown id (ids m))
        else
            match admit v with
            | Some fault -> Error fault
            | None -> Ok(Map.add id v m)

    /// The entries whose id is in `keep` — the meet with that set. An id in `keep` the map does not
    /// hold is ignored, so `restrict` never widens: what it returns is a sub-map of what it is given.
    let restrict (keep: Set<string>) (m: Map<string, 'V>) : Map<string, 'V> =
        m |> Map.filter (fun id _ -> keep.Contains id)

    /// The join of two maps whose ids are disjoint; refused `duplicate id` naming the FIRST id, in id
    /// order, that both hold — no silent overwrite, as `register`. Associative: either association
    /// of three maps is refused exactly when two of them share an id, and agrees otherwise.
    let union (duplicate: string -> 'E) (a: Map<string, 'V>) (b: Map<string, 'V>) : Result<Map<string, 'V>, 'E> =
        match b |> Map.toList |> List.tryFind (fun (id, _) -> Map.containsKey id a) with
        | Some(id, _) -> Error(duplicate id)
        | None -> Ok(Map.fold (fun acc id v -> Map.add id v acc) a b)

    // ---- the dispatch prologue (Phase 509): written once, for every seam ----

    /// The entry under `id`, or the vocabulary's `NoSuch` naming every id the map holds.
    let resolve (v: SeamVocabulary<'Decl, 'Args, 'E>) (id: string) (m: Map<string, 'V>) : Result<'V, 'E> =
        match Map.tryFind id m with
        | Some x -> Ok x
        | None -> Error(v.NoSuch id (ids m))

    /// Validate the arguments, then run the policy's gates — a policy refusal reaching every
    /// `onDenied` observer, in the vocabulary's `Refused` / `Approval`. `Ok ()` admits the invocation.
    let admit
        (v: SeamVocabulary<'Decl, 'Args, 'E>)
        (policy: RegistryPolicy<'Decl, 'Args>)
        (decl: 'Decl)
        (args: 'Args)
        : Result<unit, 'E> =
        v.Validate decl args
        |> Result.bind (fun () -> RegistryPolicy.admit v.Refused v.Approval policy (v.Id decl) decl args)

    /// Resolve, then admit: the entry an admitted invocation runs against.
    let private admitted
        (v: SeamVocabulary<'Decl, 'Args, 'E>)
        (gated: 'V -> 'Decl)
        (policy: RegistryPolicy<'Decl, 'Args>)
        (id: string)
        (args: 'Args)
        (m: Map<string, 'V>)
        : Result<'V, 'E> =
        resolve v id m
        |> Result.bind (fun x -> admit v policy (gated x) args |> Result.map (fun () -> x))

    /// What the seam would decide for an invocation without dispatching it: an id the map does not
    /// hold is a `Deny` whose guidance is the `NoSuch` sentence with the held ids as alternatives;
    /// arguments that do not validate a `Deny` with the refusal's sentence; otherwise the gates'
    /// join. No observer is told.
    let decide
        (v: SeamVocabulary<'Decl, 'Args, 'E>)
        (gated: 'V -> 'Decl)
        (policy: RegistryPolicy<'Decl, 'Args>)
        (id: string)
        (args: 'Args)
        (m: Map<string, 'V>)
        : PolicyDecision =
        match Map.tryFind id m with
        | None ->
            let known = ids m
            PolicyDecision.denyWith (v.Describe(v.NoSuch id known)) known
        | Some x ->
            let decl = gated x

            match v.Validate decl args with
            | Error e -> PolicyDecision.deny (v.Describe e)
            | Ok() -> RegistryPolicy.decide policy decl args

    /// Resolve the id (default-deny), validate, admit through the policy, and only then `run` the
    /// resolved entry; `run`'s refusal is the seam's own.
    let dispatch
        (v: SeamVocabulary<'Decl, 'Args, 'E>)
        (gated: 'V -> 'Decl)
        (policy: RegistryPolicy<'Decl, 'Args>)
        (id: string)
        (args: 'Args)
        (run: 'V -> Result<Deferred<'r>, 'E>)
        (m: Map<string, 'V>)
        : Result<Deferred<'r>, 'E> =
        admitted v gated policy id args m |> Result.bind run

    /// `dispatch`, journalled in the KEYED capture journal under `c.Key` and `c.Label`: the attempt
    /// after the invocation was admitted and before `run`; `run`'s answer settles it — `Ready` as
    /// `Completed` (`encode`), a refusal as `Refused` with `c.Record`'s reason — and `Pending` leaves
    /// it open under the returned `Ticket`. A refusal before `run` journals nothing, and neither does
    /// the `deterministic` label. The `Outcome` is exactly `dispatch`'s.
    let dispatchCaptured
        (v: SeamVocabulary<'Decl, 'Args, 'E>)
        (c: SeamCapture<'Decl, 'Args, 'E>)
        (hashFn: HashFn)
        (encode: 'r -> string)
        (gated: 'V -> 'Decl)
        (policy: RegistryPolicy<'Decl, 'Args>)
        (id: string)
        (args: 'Args)
        (run: 'V -> Result<Deferred<'r>, 'E>)
        (journal: KeyedCapture list)
        (m: Map<string, 'V>)
        : CapturedDispatch<'r, 'E> =
        let unjournalled outcome =
            { Outcome = outcome
              Ticket = None
              Journal = journal }

        match admitted v gated policy id args m with
        | Error e -> unjournalled (Error e)
        | Ok x ->
            let decl = gated x
            let det = c.Label decl

            if det = EffectCapture.deterministicTag then
                unjournalled (run x)
            else
                let key = c.Key decl args
                let mutable answered: Result<Deferred<'r>, 'E> = Ok Pending

                let captured =
                    KeyedCapture.captureEffectKeyed
                        StreamConfig.canonical
                        hashFn
                        encode
                        det
                        key
                        (fun () ->
                            answered <- run x

                            match answered with
                            | Ok d -> Deferred.settled d
                            | Error e -> Some(Error(c.Record e)))
                        journal

                { Outcome = answered
                  Ticket = Some(key, captured.Occurrence)
                  Journal = captured.Journal }

    /// REPLAY an admitted invocation from the keyed journal instead of running it: resolved,
    /// validated and admitted exactly as `dispatch` (a refusal there consults no journal); then a
    /// non-deterministic declaration is answered by `c.Key` — a completion as `Ready` (`decode`), a
    /// recorded refusal as `c.Read` of its reason, an unsettled attempt as `Pending` — and a
    /// deterministic one runs `run` live. A journal that cannot answer is `Unanswered`, never a live
    /// call.
    let dispatchReplayed
        (v: SeamVocabulary<'Decl, 'Args, 'E>)
        (c: SeamCapture<'Decl, 'Args, 'E>)
        (decode: string -> Result<'r, string>)
        (gated: 'V -> 'Decl)
        (policy: RegistryPolicy<'Decl, 'Args>)
        (id: string)
        (args: 'Args)
        (run: 'V -> Result<Deferred<'r>, 'E>)
        (cursor: Map<string, int>)
        (journal: KeyedCapture list)
        (m: Map<string, 'V>)
        : ReplayedDispatch<'r, 'E> =
        match admitted v gated policy id args m with
        | Error e ->
            { Outcome = Error(ReplayFailure.Refused e)
              Cursor = cursor }
        | Ok x ->
            let decl = gated x
            let det = c.Label decl

            if det = EffectCapture.deterministicTag then
                { Outcome = run x |> Result.mapError ReplayFailure.Refused
                  Cursor = cursor }
            else
                match KeyedCapture.replayEffectKeyed decode det (c.Key decl args) (fun () -> None) cursor journal with
                | Error fault ->
                    { Outcome = Error(ReplayFailure.Unanswered fault)
                      Cursor = cursor }
                | Ok(answer, cursor') ->
                    { Outcome =
                        match answer with
                        | Some(Ok r) -> Ok(Ready r)
                        | Some(Error reason) -> Error(ReplayFailure.Refused(c.Read reason))
                        | None -> Ok Pending
                      Cursor = cursor' }

/// An invocable registry with its default-deny dispatch (Phase 509; DECISIONS.md D172): the
/// declarations it holds by id, the policy every invocation passes, and the vocabulary it answers
/// in. OPAQUE: `register` — and its admission gate — is the only way a declaration gets in, and
/// every dispatch verb resolves, validates and admits through the policy before the body runs, so a
/// registry built on it cannot skip the gate. `CapabilityRegistry` and `QueryRegistry` are each one
/// of these; `FunctionRegistry` dispatches through the same prologue.
///
/// Equal when the declarations, the policies (`RegistryPolicy`'s equality) and the vocabularies —
/// the same VALUE, by reference, as a policy's gates are compared — are.
[<CustomEquality; NoComparison>]
type Seam<'Decl, 'Args, 'E> =
    private
        { Entries: Map<string, 'Decl>
          Policy: RegistryPolicy<'Decl, 'Args>
          Vocabulary: SeamVocabulary<'Decl, 'Args, 'E> }

    /// Equal declarations (structurally), an equal policy and the same vocabulary value (by reference).
    override s.Equals(o: obj) =
        match o with
        | :? Seam<'Decl, 'Args, 'E> as t ->
            Unchecked.equals s.Entries t.Entries
            && s.Policy.Equals t.Policy
            && obj.ReferenceEquals(s.Vocabulary, t.Vocabulary)
        | _ -> false

    /// Consistent with `Equals`: the declaration count and the policy's hash; the vocabulary is not hashed.
    override s.GetHashCode() =
        s.Entries.Count * 31 + s.Policy.GetHashCode()

/// Build, gate and dispatch through a `Seam` (Phase 509). Every verb is total and every refusal is
/// the seam vocabulary's. `NoInlining` throughout: each body reaches this package's internals, and a
/// body the optimiser copied into a calling package would carry that reach with it (DECISIONS.md D129).
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Seam =

    /// The seam holding nothing, under no gate: every dispatch is `NoSuch` with an empty `known` list.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let create (vocabulary: SeamVocabulary<'Decl, 'Args, 'E>) : Seam<'Decl, 'Args, 'E> =
        { Entries = Map.empty
          Policy = RegistryPolicy.none
          Vocabulary = vocabulary }

    /// Register a declaration under its id — refused `Duplicate` when the seam holds the id, and the
    /// `Admission` gate's refusal when it refuses the declaration; the seam unchanged on a refusal.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let register (decl: 'Decl) (s: Seam<'Decl, 'Args, 'E>) : Result<Seam<'Decl, 'Args, 'E>, 'E> =
        let v = s.Vocabulary

        KeyedRegistry.register v.Duplicate v.Admission (v.Id decl) decl s.Entries
        |> Result.map (fun m -> { s with Entries = m })

    /// The declaration registered under exactly `id`, or `None`. A bare lookup: it validates nothing
    /// and admits nothing — what runs a declaration is `dispatch`.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let tryFind (id: string) (s: Seam<'Decl, 'Args, 'E>) : 'Decl option = Map.tryFind id s.Entries

    /// Every declaration, in id order — the discovery surface; the order is part of the contract.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let enumerate (s: Seam<'Decl, 'Args, 'E>) : 'Decl list = s.Entries |> Map.toList |> List.map snd

    /// The ids the seam holds, in id order — what a `NoSuch` refusal names.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let ids (s: Seam<'Decl, 'Args, 'E>) : string list = KeyedRegistry.ids s.Entries

    /// Remove the declaration under `id` — refused `NoSuch` naming every held id when the seam does
    /// not hold it. `unregister` undoes a `register` of an id the seam did not hold.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let unregister (id: string) (s: Seam<'Decl, 'Args, 'E>) : Result<Seam<'Decl, 'Args, 'E>, 'E> =
        KeyedRegistry.unregister s.Vocabulary.NoSuch id s.Entries
        |> Result.map (fun m -> { s with Entries = m })

    /// Swap the declaration under its id — refused `NoSuch` when the seam does not hold the id, and
    /// held to the `Admission` gate `register` runs; the seam unchanged on a refusal.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let replace (decl: 'Decl) (s: Seam<'Decl, 'Args, 'E>) : Result<Seam<'Decl, 'Args, 'E>, 'E> =
        let v = s.Vocabulary

        KeyedRegistry.replace v.NoSuch v.Admission (v.Id decl) decl s.Entries
        |> Result.map (fun m -> { s with Entries = m })

    /// The seam narrowed to the ids in `keep`; an id it does not hold is ignored, so `restrict` never
    /// widens. The policy is kept.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let restrict (keep: Set<string>) (s: Seam<'Decl, 'Args, 'E>) : Seam<'Decl, 'Args, 'E> =
        { s with
            Entries = KeyedRegistry.restrict keep s.Entries }

    /// The join of two seams whose ids are disjoint — refused `Duplicate` naming the first id, in id
    /// order, that both hold. The join runs BOTH policies' gates and tells both policies' observers
    /// (`RegistryPolicy.combine`), and answers in `a`'s vocabulary. Associative.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let union (a: Seam<'Decl, 'Args, 'E>) (b: Seam<'Decl, 'Args, 'E>) : Result<Seam<'Decl, 'Args, 'E>, 'E> =
        KeyedRegistry.union a.Vocabulary.Duplicate a.Entries b.Entries
        |> Result.map (fun m ->
            { a with
                Entries = m
                Policy = RegistryPolicy.combine a.Policy b.Policy })

    /// `s` with `gate` added to the gates every dispatch runs, after validation and before the body.
    /// The decision is the join over every gate, so adding one can only refuse more.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let withGate (gate: PolicyGate<'Decl, 'Args>) (s: Seam<'Decl, 'Args, 'E>) : Seam<'Decl, 'Args, 'E> =
        { s with
            Policy = RegistryPolicy.withGate gate s.Policy }

    /// `s` with `observe` told of every invocation its policy refuses, before the refusal returns. A
    /// refusal for any other reason (an unknown id, arguments that do not validate) reaches no observer.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let onDenied (observe: PolicyDenial<'Args> -> unit) (s: Seam<'Decl, 'Args, 'E>) : Seam<'Decl, 'Args, 'E> =
        { s with
            Policy = RegistryPolicy.onDenied observe s.Policy }

    /// What the seam would decide for an invocation without dispatching it — an unknown id and
    /// arguments that do not validate are denials, otherwise the gates' join. No observer is told.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let decide (s: Seam<'Decl, 'Args, 'E>) (id: string) (args: 'Args) : PolicyDecision =
        KeyedRegistry.decide s.Vocabulary (fun (d: 'Decl) -> d) s.Policy id args s.Entries

    /// Dispatch an invocation: resolve the id (default-deny — an id the seam does not hold is
    /// `NoSuch`), validate the arguments, run the policy's gates (a `Deny` is `Refused`, a
    /// `NeedsApproval` is `Approval`, and either reaches every observer), and only then `run` the
    /// resolved declaration, in the `Deferred` envelope.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let dispatch
        (s: Seam<'Decl, 'Args, 'E>)
        (id: string)
        (args: 'Args)
        (run: 'Decl -> Result<Deferred<'r>, 'E>)
        : Result<Deferred<'r>, 'E> =
        KeyedRegistry.dispatch s.Vocabulary (fun (d: 'Decl) -> d) s.Policy id args run s.Entries

    /// `dispatch`, journalled in the keyed capture journal as `capture` says: the attempt after the
    /// invocation was admitted and before `run`, settled by `run`'s answer; a `Pending` answer leaves
    /// it open under the returned `Ticket`, which `KeyedCapture.settleEffectKeyed` settles later. A
    /// refusal before `run`, or the `deterministic` label, journals nothing. The `Outcome` is exactly
    /// `dispatch`'s.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let dispatchCaptured
        (hashFn: HashFn)
        (encode: 'r -> string)
        (capture: SeamCapture<'Decl, 'Args, 'E>)
        (s: Seam<'Decl, 'Args, 'E>)
        (id: string)
        (args: 'Args)
        (run: 'Decl -> Result<Deferred<'r>, 'E>)
        (journal: KeyedCapture list)
        : CapturedDispatch<'r, 'E> =
        KeyedRegistry.dispatchCaptured
            s.Vocabulary
            capture
            hashFn
            encode
            (fun (d: 'Decl) -> d)
            s.Policy
            id
            args
            run
            journal
            s.Entries

    /// REPLAY an invocation from the keyed capture journal: admitted exactly as `dispatch` (a refusal
    /// there consults no journal), then answered from the journal by `capture.Key` — a completion as
    /// `Ready`, a recorded refusal as `capture.Read` of its reason, an unsettled attempt as `Pending`
    /// — or, under the `deterministic` label, by `run` live. A journal that cannot answer is
    /// `ReplayFailure.Unanswered`, never a live call.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let dispatchReplayed
        (decode: string -> Result<'r, string>)
        (capture: SeamCapture<'Decl, 'Args, 'E>)
        (s: Seam<'Decl, 'Args, 'E>)
        (id: string)
        (args: 'Args)
        (run: 'Decl -> Result<Deferred<'r>, 'E>)
        (cursor: Map<string, int>)
        (journal: KeyedCapture list)
        : ReplayedDispatch<'r, 'E> =
        KeyedRegistry.dispatchReplayed
            s.Vocabulary
            capture
            decode
            (fun (d: 'Decl) -> d)
            s.Policy
            id
            args
            run
            cursor
            journal
            s.Entries
