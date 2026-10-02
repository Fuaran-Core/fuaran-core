namespace Fuaran.Core

// The mandatory two-axis effect signature of the artifact-function protocol (Phase 181): the host
// axis, the determinism factor set, their join, and the canonical determinism label.

/// Axis 1 — does the artifact touch the world.
type HostEffect =
    | Pure
    | ReadsHost
    | WritesHost

/// One atomic source of non-determinism a body may read beyond its explicit inputs. The
/// declaration order IS the canonical order (`Clock`, then `Random`, then `Network`) — the order
/// `Effect.determinismTag` renders a set in, held by an explicit list there rather than by the
/// derived comparison, so a reordered case cannot silently reorder a wire label.
type DeterminismFactor =
    | ClockFactor
    | RandomFactor
    | NetworkFactor

/// Axis 2 — which non-deterministic sources the artifact reads: a SET of factors, joined by union.
/// `Set.empty` is `Deterministic` (reproducible from inputs). Every factor is a fact: a body that
/// reads the clock AND a random source declares both, so a capture journal can say what a replay
/// must re-seed (Phase 319; the chain this replaced joined by maximum and dropped the clock beside
/// a random read).
type DeterminismSource = Set<DeterminismFactor>

/// The mandatory, total effect signature — two orthogonal axes, never optional.
type EffectClass =
    { Host: HostEffect
      Determinism: DeterminismSource }

/// The effect lattice + the composition join. `compose` propagates the join
/// componentwise (pure ∘ impure = impure; clock ∘ random = clock ∪ random).
module Effect =

    /// The determinism bottom: no factor read.
    let deterministic: DeterminismSource = Set.empty

    /// The determinism of a body that reads exactly the clock.
    let clock: DeterminismSource = Set.singleton ClockFactor

    /// The determinism of a body that reads exactly a random source.
    let random: DeterminismSource = Set.singleton RandomFactor

    /// The determinism of a body that reads exactly the network.
    let network: DeterminismSource = Set.singleton NetworkFactor

    let pureDeterministic =
        { Host = Pure
          Determinism = deterministic }

    let private hostRank =
        function
        | Pure -> 0
        | ReadsHost -> 1
        | WritesHost -> 2

    let private hostOf =
        function
        | 0 -> Pure
        | 1 -> ReadsHost
        | _ -> WritesHost

    /// Host axis: componentwise widest (a chain). Determinism axis: union — associative,
    /// commutative, idempotent, with `deterministic` the identity.
    let join (a: EffectClass) (b: EffectClass) : EffectClass =
        { Host = hostOf (max (hostRank a.Host) (hostRank b.Host))
          Determinism = Set.union a.Determinism b.Determinism }

    /// A declared effect must be at least as wide as the actual, on both axes: a host rank at
    /// least as high, and a determinism set that is a SUPERSET — the declaration names every
    /// factor the actual reads (a `Network`-only declaration no longer covers a clock read).
    let covers (declared: EffectClass) (actual: EffectClass) : bool =
        hostRank declared.Host >= hostRank actual.Host
        && Set.isSubset actual.Determinism declared.Determinism

    let private factorName =
        function
        | ClockFactor -> "clock"
        | RandomFactor -> "random"
        | NetworkFactor -> "network"

    /// The factors in canonical order — the order every rendering of a set uses.
    let private canonicalFactors = [ ClockFactor; RandomFactor; NetworkFactor ]

    /// The canonical wire label for a determinism set (Phase 27, restated by Phase 319) — the single
    /// source of truth the `Fuaran.Core.OpStream` determinism-capture seam keys on. The empty set →
    /// `"deterministic"` (no capture — reproducible from inputs); otherwise the member factors'
    /// names in the fixed order clock, random, network, joined by `+` (`"clock"`, `"clock+random"`,
    /// `"clock+random+network"`). A single factor keeps the label it always had, so a journal keyed
    /// by `"clock"` still names the same effect. Read-only projection. `OpStream` sits below
    /// `Function` and cannot reference `DeterminismSource`, so a consumer threads this label into
    /// `OpStream.captureEffect` / `replayEffect`.
    let determinismTag (d: DeterminismSource) : string =
        match canonicalFactors |> List.filter (fun f -> Set.contains f d) with
        | [] -> "deterministic"
        | fs -> fs |> List.map factorName |> String.concat "+"

    /// The inverse of `determinismTag`: the set a label names, or `None`. Only the CANONICAL label
    /// of a set is accepted — `"random+clock"`, `"clock+clock"`, `"deterministic+clock"`, an empty
    /// member and an unknown factor are all `None` — so a set has exactly one wire spelling.
    let tryDeterminismOfTag (tag: string) : DeterminismSource option =
        let parsed =
            if tag = "deterministic" then
                Some Set.empty
            else
                tag.Split('+')
                |> Array.toList
                |> List.fold
                    (fun acc name ->
                        match acc, canonicalFactors |> List.tryFind (fun f -> factorName f = name) with
                        | Some set, Some f -> Some(Set.add f set)
                        | _ -> None)
                    (Some Set.empty)

        parsed |> Option.filter (fun set -> determinismTag set = tag)
