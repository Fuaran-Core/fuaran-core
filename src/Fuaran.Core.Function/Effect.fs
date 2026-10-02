namespace Fuaran.Core

// The mandatory two-axis effect signature of the artifact-function protocol (Phase 181): the host
// axis, the determinism factor set, their join, and the canonical determinism label.

/// Axis 1 — does the artifact touch the world.
type HostEffect =
    /// Touches nothing outside its inputs; the bottom of the host chain.
    | Pure
    /// Reads state outside its inputs but changes none; wider than `Pure`.
    | ReadsHost
    /// Changes state outside its inputs; the top of the host chain, wider than `ReadsHost`.
    | WritesHost

/// One atomic source of non-determinism a body may read beyond its explicit inputs. The
/// declaration order IS the canonical order (`Clock`, then `Random`, then `Network`) — the order
/// `Effect.determinismTag` renders a set in, held by an explicit list there rather than by the
/// derived comparison, so a reordered case cannot silently reorder a wire label.
type DeterminismFactor =
    /// Reads the wall clock; labelled `clock` on the wire.
    | ClockFactor
    /// Reads a random source; labelled `random` on the wire.
    | RandomFactor
    /// Reads the network; labelled `network` on the wire.
    | NetworkFactor

/// Axis 2 — which non-deterministic sources the artifact reads: a SET of factors, joined by union.
/// `Set.empty` is `Deterministic` (reproducible from inputs). Every factor is a fact: a body that
/// reads the clock AND a random source declares both, so a capture journal can say what a replay
/// must re-seed (Phase 319; the chain this replaced joined by maximum and dropped the clock beside
/// a random read).
type DeterminismSource = Set<DeterminismFactor>

/// The mandatory, total effect signature — two orthogonal axes, never optional.
type EffectClass =
    {
        /// Axis 1: how far the artifact reaches into the host; joined by the widest.
        Host: HostEffect
        /// Axis 2: every non-deterministic source read; joined by union, `Set.empty` when reproducible.
        Determinism: DeterminismSource
    }

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

    /// The bottom of the lattice on both axes — the identity of `join`, and covered by every
    /// declaration.
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

/// THE wire codec for an effect class (Phase 295): one writer and one reader, which `toSchema`,
/// `toJsonSchema`, `CapabilityCodec` and `QueryCodec` all go through. An effect is a two-member
/// object, `{"host": …, "determinism": …}` — untagged, because it is never a document of its own,
/// only a member of one — with the host as `pure` / `readsHost` / `writesHost` and the
/// determinism as its canonical label (`Effect.determinismTag`). It was written three times and
/// read twice before Phase 295, and the two readers had drifted to two sentences for an unknown
/// determinism label; the sentence kept is the capability codec's.
module EffectCodec =

    /// The wire spelling of the host axis.
    let hostTag (h: HostEffect) : string =
        match h with
        | Pure -> "pure"
        | ReadsHost -> "readsHost"
        | WritesHost -> "writesHost"

    let private hosts = [ Pure; ReadsHost; WritesHost ]

    /// The host axis, read back; an unknown spelling is `UnknownTag` naming the three.
    let hostDecoder: Decoder<HostEffect> =
        Decoder.str
        |> Decoder.andThen (fun s ->
            match hosts |> List.tryFind (fun h -> hostTag h = s) with
            | Some h -> Ok h
            | None ->
                Error(
                    DecodeError.make
                        DecodeCode.UnknownTag
                        ("one of "
                         + (hosts |> List.map (fun h -> "'" + hostTag h + "'") |> String.concat ", "))
                        ("unknown host effect: " + s)
                ))

    /// The determinism axis, read back from its canonical label only (`Effect.tryDeterminismOfTag`).
    let determinismDecoder: Decoder<DeterminismSource> =
        Decoder.str
        |> Decoder.andThen (fun tag ->
            match Effect.tryDeterminismOfTag tag with
            | Some set -> Ok set
            | None -> Error(DecodeError.make DecodeCode.UnknownTag "a determinism tag" ("unknown determinism: " + tag)))

    /// The members an effect object carries — what a strict reader admits.
    let members: string list = [ "host"; "determinism" ]

    /// Write an effect class.
    let toJson (e: EffectClass) : JVal =
        JObj
            [ "host", JStr(hostTag e.Host)
              "determinism", JStr(Effect.determinismTag e.Determinism) ]

    /// Read an effect class.
    let decoder: Decoder<EffectClass> =
        Decoder.field "host" hostDecoder
        |> Decoder.bind (fun host ->
            Decoder.field "determinism" determinismDecoder
            |> Decoder.map (fun det -> { Host = host; Determinism = det }))
