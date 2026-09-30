namespace Fuaran.Core

/// The integrity families (Phase 297 split): encoding and hashing, attestation, attribution, the construct-then-encode round trip.
module internal IntegrityLaws =

    // ---- canonical float encoder (Phase 55) ----
    // The teeth on `Wire.Canon.canonicalFloat` — the single cross-host float→string encoder every
    // float→wire / float→key path routes through, so the bytes match across the .NET / Fable / TS /
    // Python hosts.

    /// The canonical-float laws (Phase 55). Self-contained (it draws floats from the seed); over a
    /// seed-replayable sample certifies: **determinism** (the same float always renders identically),
    /// **finite round-trip** (a finite float's canonical string re-parses through the wire parser to the
    /// same numeric value — the cross-host parity contract; an integer-valued float legitimately
    /// re-parses as a `JInt` of the same value, per `WIRE_FORMAT`), and **stable non-finite tokens**
    /// (`NaN` / `±Infinity` render to fixed, distinct tokens, never host-/locale-specific text).
    let canonicalFloatLaws (seed: int) (iterations: int) : LawResult list =
        let determinism =
            LawKit.LawCell "canonicalFloat is deterministic (same float ⇒ same string)"

        let roundtrip =
            LawKit.LawCell "a finite float round-trips through the wire parser to the same numeric value"

        let nonFinite =
            LawKit.LawCell "non-finite floats render to stable, distinct tokens (NaN / ±Infinity)"

        let numericOf (s: string) : float option =
            match Json.parse s with
            | Ok(JInt i) -> Some(float i)
            | Ok(JFloat f) -> Some f
            | _ -> None

        LawKit.run iterations seed (fun rng _ at ->
            let a = rng.IntBelow 2000000
            let b = rng.IntBelow 1000
            // a spread of finite magnitudes, positive and negative.
            let f = float (a - 1000000) / float (b + 1)

            let s1 = Canon.canonicalFloat f
            let s2 = Canon.canonicalFloat f

            determinism.Check((s1 = s2), fun () -> at (sprintf "canonicalFloat not deterministic for %g" f))

            match numericOf s1 with
            | Some v when v = f -> roundtrip.Saw()
            | other ->
                roundtrip.Check(
                    false,
                    fun () -> at (sprintf "finite round-trip ≠ original for %g (got %A from %s)" f other s1)
                ))

        // stable, distinct non-finite tokens (fixed text, not host-/locale-specific).
        let nan = Canon.canonicalFloat System.Double.NaN
        let pinf = Canon.canonicalFloat System.Double.PositiveInfinity
        let ninf = Canon.canonicalFloat System.Double.NegativeInfinity

        nonFinite.Check(
            nan = "\"NaN\""
            && pinf = "\"Infinity\""
            && ninf = "\"-Infinity\""
            && nan <> pinf
            && pinf <> ninf,
            fun () ->
                sprintf "seed=%d: non-finite tokens not stable/distinct (nan=%s pinf=%s ninf=%s)" seed nan pinf ninf
        )

        LawKit.results [ determinism; roundtrip; nonFinite ]

    // ---- memo encoder injectivity (Phase 56) ----
    // The teeth on `applyMemo`'s silent precondition: its content-addressed key is
    // `Tree.encodeHash w.Tree encode node`, so a non-injective `encode` (two structurally-distinct trees
    // → the same string) would make the cache serve the WRONG tree. This certifies a domain's encoder is
    // collision-free over its generator.

    /// The encoder-injectivity law (Phase 56). A domain supplies its witness `w`, the node-encoder
    /// `encode` it passes to `applyMemo`, and a tree generator `gen`; over a seed-replayable sample the
    /// kit certifies that distinct trees never share a content hash
    /// (`Tree.encodeHash a = Tree.encodeHash b ⇒ a = b`) — the precondition that makes the memo cache
    /// sound. A lossy encoder fails with a reproducible `(tree, tree)` counterexample (the two colliding
    /// trees). `'Node` needs equality. Mirrors the `Corpus.codecLaws` "certify-your-codec" posture.
    ///
    /// Phase 297 — the search is only as good as the draw. It hashes one drawn tree per iteration
    /// against the trees seen so far, so a generator that mints ONE tree compares nothing and the
    /// law passes green; the guard beside it counts the distinct trees the draw produced and how
    /// many of them were compared against a prior distinct tree, and is red when no distinct pair
    /// was ever compared. The search stops at the first collision, as it always did.
    let encoderInjectivityLaws
        (w: ArtifactWitness<'Node, 'Id>)
        (encode: 'Node -> string)
        (gen: ConfRng.T -> 'Node * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let collision =
            LawKit.LawCell(
                "the node-encoder is collision-free (distinct trees ⇒ distinct content hash) — memo-key soundness",
                Some "distinct tree"
            )

        let mutable seen = Map.empty<string, 'Node>
        let mutable distinct = 0
        let mutable compared = 0

        LawKit.run iterations seed (fun rng _ at ->
            if not collision.Failed then
                let tree = rng.Draw gen
                let h = Tree.encodeHash w.Tree encode tree

                match Map.tryFind h seen with
                | Some prior when prior <> tree ->
                    // the colliding tree is a distinct tree the search compared — and lost.
                    distinct <- distinct + 1
                    compared <- compared + 1

                    collision.Check(
                        false,
                        fun () -> at (sprintf "distinct trees share content hash %s (%A vs %A)" h prior tree)
                    )
                | Some _ -> () // the same tree re-drawn — not a collision
                | None ->
                    distinct <- distinct + 1

                    // a new distinct tree looked up against a non-empty map is one distinct pair compared.
                    if not (Map.isEmpty seen) then
                        compared <- compared + 1
                        collision.Saw()

                    seen <- Map.add h tree seen)

        LawKit.results [ collision ]
        @ [ SampleAdequacy.reached
                "Conformance.encoderInjectivityLaws"
                "distinct tree"
                seed
                [ "seen", distinct; "compared", compared ] ]

    // ---- op-codec injectivity (Phase 145) ----
    // The fourth premise of the content-id theorem, and the one that is a DOMAIN's rather than this
    // library's. `Dag.nodeHash` hashes `Actor.encode actor + "|" + w.Encode op`, so a node's content
    // id determines its op only if the codec is injective: two distinct ops that encode alike mint
    // ONE id, and a tamper between them is invisible to `Dag.firstBreak` and to
    // `OpStream.verifyChain` alike — the walker is not weak there, the pre-image simply does not
    // distinguish them. `proofs/Chain.fst` takes `op_codec_injective` as a parameter for exactly that
    // reason (`Encode` belongs to whoever brings the op type), and this is the law a witness
    // certifies it with — the same division of labour the fold theorem's `independence_diamond`
    // already runs on with `FoldConfluence.laneFoldLaws`.

    /// The op-codec injectivity laws (Phase 145). Three laws over one seed-replayable draw of the
    /// domain's own ops, and the first two are NOT the same claim:
    ///
    ///  - **no collision was drawn** — two distinct ops never share an encoding. Sampled evidence,
    ///    and only as good as the draw: it can report a collision only if it draws the pair.
    ///  - **the codec has a left inverse** — `Decode (Encode op) = Ok op` on every drawn op. This is
    ///    the arm that carries the weight, because a codec with a total left inverse *is* injective;
    ///    one drawn op exercises it, where the first arm needs a colliding PAIR to come up.
    ///  - **the draw searched more than one encoding** — a generator that mints one op makes the
    ///    first law pass having compared nothing.
    ///
    /// Deliberately NOT folded into `certify` / `certifyStream`, on the same reasoning as the
    /// snapshot and DAG surfaces above: the second law demands a working `Decode`, and a witness that
    /// legitimately stubs it (a domain that never reads a stream back) would go red for something
    /// that is not about its op algebra. A domain calls this beside its base certification. `'Op`
    /// needs equality.
    let codecInjectivityLaws
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let collision =
            LawKit.LawCell
                "the op codec is collision-free over the drawn population (distinct ops ⇒ distinct encodings)"

        let inverse =
            LawKit.LawCell
                "the op codec has a left inverse (Decode ∘ Encode = Ok) — which makes it injective, not merely un-collided"

        let narrow =
            LawKit.LawCell "the draw searched more than one encoding, so the collision law compared something"

        let mutable seen = Map.empty<string, 'Op>
        let mutable distinct = 0

        LawKit.run iterations seed (fun rng _ at ->
            let op = rng.Draw gen.Op
            let enc = w.Encode op

            if not collision.Failed then
                match Map.tryFind enc seen with
                | Some prior when prior <> op ->
                    collision.Check(
                        false,
                        fun () ->
                            at (
                                sprintf
                                    "two DISTINCT ops encode alike — %A and %A both encode to %s, so a DAG node carrying either mints one content id"
                                    prior
                                    op
                                    enc
                            )
                    )
                | Some _ -> collision.Saw() // the same op re-drawn — not a collision
                | None ->
                    distinct <- distinct + 1
                    seen <- Map.add enc op seen
                    collision.Saw()

            if not inverse.Failed then
                match w.Decode enc with
                | Ok back when back = op -> inverse.Saw()
                | Ok back ->
                    inverse.Check(
                        false,
                        fun () ->
                            at (
                                sprintf
                                    "Decode (Encode %A) = Ok %A — the codec's own decoder is not a left inverse of its encoder, so nothing here bounds what else the encoder aliases"
                                    op
                                    back
                            )
                    )
                | Error e ->
                    inverse.Check(
                        false,
                        fun () ->
                            at (
                                sprintf
                                    "Decode refused the encoding this witness's own Encode produced for %A: %s"
                                    op
                                    e
                            )
                    ))

        narrow.Check(
            distinct >= 2,
            fun () ->
                sprintf
                    "seed=%d: the draw produced %d distinct encoding(s) over %d iterations — a collision search over one value compares nothing; widen the generator"
                    seed
                    distinct
                    iterations
        )

        LawKit.results [ collision; inverse; narrow ]

    // ---- integrity & provenance conformance (Wave 17) ----
    // Rebuild a chain's hashes from its `(seq, actor, op)` pre-images under the canonical binding —
    // the "adversary rewrites history and recomputes every hash" operation. The result verifies under
    // `verifyChain` (it is internally consistent), so it is the forgery a bare hash chain re-accepts;
    // what catches it is either a moved head vs an external commitment (Phase 65) or an attestation
    // signed over the original head (Phase 60). Uses only the public `canonicalConfig` payload binding.
    let private reforgeCanonical
        (hashFn: HashFn)
        (encode: 'Op -> string)
        (records: OpRecord<'Op> list)
        : OpRecord<'Op> list =
        (([], OpStream.canonicalConfig.Genesis), records)
        ||> List.fold (fun (acc, prev) r ->
            let payload = OpStream.canonicalConfig.Payload r.Seq r.Actor (encode r.Op)
            let h = hashFn prev payload
            acc @ [ { r with PrevHash = prev; Hash = h } ], h)
        |> fst

    /// The attestation / replay-as-provenance laws (Phase 60) — the teeth on `IAttestationSink` +
    /// `attestHead` / `verifyAttestation` and the typed-`Actor`-in-hash posture (Phase 320). Over a
    /// seed-replayable sample of chains built from `gen` under a supplied `StreamWitness` + test sink,
    /// certifies the three-stage guarantee **integrity → attestation → deterministic replay**:
    ///
    ///  - **checkpoint round-trip** — a signed head verifies against the chain it was taken over
    ///    (`verifyAttestation sink (attestHead sink recs) recs`);
    ///  - **prefix attestation** — a head signed over the length-`n` prefix verifies against exactly that
    ///    prefix and NOT against a different-length one: one signature is bound to the whole prefix state
    ///    (O(commits), not O(ops) — the hash-chain already folds every prior op into the head);
    ///  - **replay-equivalence** — `replay` of the attested op log reproduces the exact live state the
    ///    signed head was taken over, so that state is provably the deterministic replay of its log;
    ///  - **falsification** — an op-tamper AND an actor-re-attribution, each *rehashed under the same
    ///    `HashFn` so `verifyChain` re-accepts the forged chain* (the plain hash-chain defence defeated),
    ///    are still caught by `verifyAttestation`: the forgery moved the head, and the signature covers
    ///    only the original one. This is exactly what attestation adds over a bare hash chain — and it
    ///    holds under a *cryptographic* `HashFn` too (run the kit with the keyed / wide stand-in), since
    ///    a re-hashed forgery cannot be re-signed without the host key.
    ///
    /// Under the `noAttestation` default every branch is **vacuous** (`Sign ⇒ None ⇒` nothing to verify
    /// or falsify), and since Phase 196 the family's signing-outcome guard reports that run RED rather
    /// than green. Adopting the kit still never forces a sink on a host: a host with none runs
    /// `noAttestationVacuityLaws` instead.
    /// Opt-in like `snapshotLaws` / `dagLaws`. `'State` needs equality (replay-equivalence).
    let attestationLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (sink: IAttestationSink)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        // The four laws that run only where the sink signed read through the guard below (Phase 297):
        // it is the guard that reports the never-signed run, not each cell four times over.
        let coveredBy = Some "signing outcome and op tamper"

        let roundTrip =
            LawKit.LawCell("attestation checkpoint round-trip (a signed head verifies against its chain)", coveredBy)

        let prefix =
            LawKit.LawCell(
                "attestation is bound to its exact prefix (a head attests its whole prefix — O(commits))",
                coveredBy
            )

        let replayEq =
            LawKit.LawCell "replay-equivalence (replay of the attested log = the live state the head was taken over)"

        let opTamper =
            LawKit.LawCell(
                "attestation catches a rehashed op-forgery (verifyChain re-accepts; verifyAttestation rejects)",
                coveredBy
            )

        let actorTamper =
            LawKit.LawCell(
                "attestation catches a rehashed actor-re-attribution (attribution is inside the hash)",
                coveredBy
            )
        // Phase 196 — the two arms whose evidence is DRAWN rather than built. Four of the five
        // laws below run only where the sink actually signed, and `OpStream.noAttestation` signs
        // nothing: under it this family reports five greens over zero exercised cases, which is
        // the exact shape a consumer's census used to render as "adopted". Counting is what lets
        // the census say `vacuous` instead, so the counters are not diagnostics — they are the
        // measurement the family exists to be able to report. Phase 297 adds the third: the
        // op-forgery is built only when the drawn replacement op ENCODES differently from the one
        // it replaces, so a generator that mints one op never forges, and the count says so.
        let mutable signed = 0
        let mutable falsified = 0
        let mutable opTampered = 0

        LawKit.run iterations seed (fun rng _ at ->
            let state, recs, _ = LawKit.buildChain hashFn sw gen rng

            // checkpoint round-trip: a signed head verifies against its own chain (vacuous under noAttestation).
            (match OpStream.attestHead sink recs with
             | Some att ->
                 signed <- signed + 1

                 roundTrip.Check(
                     OpStream.verifyAttestation sink att recs,
                     fun () -> at "a signed head failed verifyAttestation"
                 )
             | None -> ())

            // prefix attestation: a head signs its whole prefix — the signature is bound to exactly that
            // prefix and not to a different-length one (whose head, folding a different op set, differs).
            let len = List.length recs
            let n = rng.IntBelow(len + 1)
            let pfx = recs |> List.truncate n

            (match OpStream.attestHead sink pfx with
             | Some attN ->
                 prefix.Check(
                     OpStream.verifyAttestation sink attN pfx,
                     fun () -> at "a prefix head failed to verify against its own prefix"
                 )

                 let n2 = if n = len then max 0 (n - 1) else len
                 let pfx2 = recs |> List.truncate n2

                 if n2 <> n && OpStream.head pfx2 <> OpStream.head pfx then
                     prefix.Check(
                         not (OpStream.verifyAttestation sink attN pfx2),
                         fun () -> at "a prefix signature covered a different-length prefix"
                     )
             | None -> ())

            // replay-equivalence: the attested op log replays to the live state the head was taken over.
            (match OpStream.replay sw gen.State0 recs with
             | Ok s when s = state -> replayEq.Saw()
             | other ->
                 replayEq.Check(false, fun () -> at (sprintf "replay of the attested log ≠ live state (got %A)" other)))

            // falsification — only when the sink actually signs (vacuous under noAttestation).
            match recs, OpStream.attestHead sink recs with
            | [], _
            | _, None -> ()
            | _, Some att ->
                falsified <- falsified + 1
                // op-tamper + full rehash: verifyChain re-accepts, verifyAttestation must reject.
                let tIdx = rng.IntBelow(List.length recs)
                let newOp = rng.Draw gen.Op
                let orig = List.item tIdx recs

                if sw.Encode orig.Op <> sw.Encode newOp then
                    opTampered <- opTampered + 1

                    let tampered =
                        recs |> List.mapi (fun j r -> if j = tIdx then { r with Op = newOp } else r)

                    let forged = reforgeCanonical hashFn sw.Encode tampered

                    opTamper.Check(
                        not (
                            OpStream.verifyChain hashFn sw forged
                            && OpStream.verifyAttestation sink att forged
                        ),
                        fun () -> at "a rehashed op-forgery passed verifyAttestation"
                    )

                // actor-tamper + full rehash: re-attribution moves the head (the actor is in the hash since 320).
                let aIdx = rng.IntBelow(List.length recs)
                let origA = List.item aIdx recs

                let newActor =
                    match origA.Actor with
                    | Human _ -> Agent("m", "v", "mallory")
                    | Agent _ -> Human "mallory"

                let reattr =
                    recs
                    |> List.mapi (fun j r -> if j = aIdx then { r with Actor = newActor } else r)

                let forgedA = reforgeCanonical hashFn sw.Encode reattr

                actorTamper.Check(
                    not (
                        OpStream.verifyChain hashFn sw forgedA
                        && OpStream.verifyAttestation sink att forgedA
                    ),
                    fun () -> at "a rehashed actor-re-attribution passed verifyAttestation"
                ))

        LawKit.results [ roundTrip; prefix; replayEq; opTamper; actorTamper ]
        // Phase 196. A sink that never signs leaves four of the five laws above asserting
        // nothing, and every one of them still reports green — so the guard is the only thing
        // that can tell a certified attestation seam from an unexercised one. Running this
        // family at `OpStream.noAttestation` is therefore RED here by design: the vacuous path
        // has its own family (`noAttestationVacuityLaws`), which is what a host with no sink
        // should be running. Phase 297 folds the op-tamper count into the same guard (the
        // family's result count is pinned) rather than emitting a second one.
        @ [ SampleAdequacy.reached
                "Conformance.attestationLaws"
                "signing outcome and op tamper"
                seed
                [ "signed", signed; "falsified", falsified; "op-tampered", opTampered ] ]

    /// The `noAttestation` vacuity laws (Phase 60) — the default no-op sink issues no attestation
    /// (`attestHead noAttestation ⇒ None`) and verifies nothing (`verifyAttestation noAttestation _ ⇒
    /// false`), and — because attestation is a read-only side-band — the chain is byte-identical whether
    /// or not a host ever attests. So adopting the seam is free: no sink ⇒ exactly the pre-attestation
    /// path — which is why a host with no sink runs THIS family, and not `attestationLaws
    /// OpStream.noAttestation`, whose guard reds the vacuous run (Phase 196). Self-contained over a supplied
    /// witness + stream generator; `'State` is not compared.
    let noAttestationVacuityLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let noSign = LawKit.LawCell "noAttestation issues no attestation (Sign ⇒ None)"
        let noVerify = LawKit.LawCell "noAttestation verifies nothing (Verify ⇒ false)"

        let unchanged =
            LawKit.LawCell "attesting leaves the un-attested chain unchanged (read-only side-band)"

        LawKit.run iterations seed (fun rng _ at ->
            let _, recs, _ = LawKit.buildChain hashFn sw gen rng

            noSign.Check(
                OpStream.attestHead OpStream.noAttestation recs = None,
                fun () -> at "noAttestation issued an attestation"
            )

            // even a well-formed attestation over the real head is rejected by the no-op sink.
            let plausible: Attestation =
                { Head = OpStream.head recs
                  KeyId = "k"
                  Signature = "sig" }

            noVerify.Check(
                not (OpStream.verifyAttestation OpStream.noAttestation plausible recs),
                fun () -> at "noAttestation verified an attestation"
            )

            // attesting is read-only: the head is stable and the chain still verifies across an attest call.
            let before = OpStream.head recs
            OpStream.attestHead OpStream.noAttestation recs |> ignore

            unchanged.Check(
                OpStream.head recs = before && OpStream.verifyChain hashFn sw recs,
                fun () -> at "attesting altered the un-attested chain"
            ))

        LawKit.results [ noSign; noVerify; unchanged ]

    /// The pluggable-`HashFn` parity laws (Phase 65) — the teeth on the `HashFn` seam + `verifyChain`,
    /// certifying that a chain hash is a pure function of the canonical wire pre-image (the cross-host
    /// parity contract STABILITY.md states). Over a seed-replayable sample of chains built from `gen`
    /// under a supplied `HashFn`:
    ///
    ///  - **determinism** — the same op sequence produces the identical chain across independent builds
    ///    (no clock / culture leakage in the pre-image; the Phase-55 canonical-float discipline);
    ///  - **pre-image parity** — a chain built incrementally (`append`) and one rebuilt in bulk from the
    ///    same `(seq, actor, op)` pre-images agree hash-for-hash, so the hash keys on the canonical
    ///    pre-image and NOTHING about the construction path — the foundation of cross-host parity (two
    ///    hosts on the same `HashFn` + same pre-image get byte-identical chains);
    ///  - **tamper-detection** — a reorder, a dropped link, and a bit-flip are each caught by
    ///    `verifyChain` under the supplied fn.
    ///
    /// The crypto posture (a re-hashed forgery is caught under a collision-resistant fn but not under the
    /// default FNV-1a) is a separate branch — see `hashFnAdversarialLaws`. `'State` is not compared.
    ///
    /// Phase 297 — the three tamper arms are gated on the chain's LENGTH (reorder needs two records,
    /// an interior drop three, a bit-flip one), and the length is the domain's doing: a generator
    /// whose ops are mostly refused builds short chains and skips the arms. The guard beside the
    /// laws counts how often each arm ran, and the tamper law reads through it.
    let hashFnLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let chainHashes (rs: OpRecord<'Op> list) = rs |> List.map (fun r -> r.Hash)

        let determinism =
            LawKit.LawCell "hash determinism (the same op sequence hashes to the same chain across builds)"

        let parity =
            LawKit.LawCell "pre-image parity (chain = f(canonical pre-image) only — incremental and bulk builds agree)"

        let tamper =
            LawKit.LawCell(
                "tamper-detection (reorder / drop / bit-flip caught by verifyChain under the supplied HashFn)",
                Some "tamper arm"
            )

        let mutable reorders = 0
        let mutable drops = 0
        let mutable flips = 0

        LawKit.run iterations seed (fun rng _ at ->
            // build the chain twice from the same start: same start ⇒ identical chain (determinism).
            // The cursor is left where the FIRST build left it, as it always was.
            let start = rng.State
            let _, recs, _ = LawKit.buildChain hashFn sw gen rng
            let after = rng.State
            rng.State <- start
            let _, recs2, _ = LawKit.buildChain hashFn sw gen rng
            rng.State <- after

            determinism.Check(
                chainHashes recs = chainHashes recs2,
                fun () -> at "the same op sequence hashed to a different chain"
            )

            // pre-image parity: an incremental build and a bulk reforge of the same pre-images agree.
            let reforged = reforgeCanonical hashFn sw.Encode recs

            parity.Check(
                chainHashes recs = chainHashes reforged,
                fun () -> at "incremental and bulk builds disagreed (hash keys on more than the pre-image)"
            )

            // tamper-detection under the supplied fn: reorder (need ≥2) / drop (need ≥3) + bit-flip (need ≥1).
            let len = List.length recs

            if len >= 2 then
                reorders <- reorders + 1

                let swapped =
                    recs
                    |> List.mapi (fun j r ->
                        if j = 0 then List.item 1 recs
                        elif j = 1 then List.item 0 recs
                        else r)

                tamper.Check(
                    not (OpStream.verifyChain hashFn sw swapped),
                    fun () -> at "a reordered chain passed verifyChain"
                )

            // The dropped record must be INTERIOR (needs ≥3): dropping the tail record is a
            // truncation, and a hash chain authenticates prefixes — every truncation IS a valid
            // shorter chain, undetectable by chain verification alone (by design; pinning a head
            // against truncation is the attestation seam's job, Phase 320). At len = 2 the only
            // index-1 drop is exactly that tail truncation, so this branch would assert something
            // cryptographically impossible. Surfaced by the UI adoption's rejection-heavy stream
            // generator, which legitimately builds 2-record chains (2026-07-05).
            if len >= 3 then
                drops <- drops + 1

                let dropped =
                    recs
                    |> List.mapi (fun j r -> j, r)
                    |> List.filter (fun (j, _) -> j <> 1)
                    |> List.map snd

                tamper.Check(
                    not (OpStream.verifyChain hashFn sw dropped),
                    fun () -> at "a chain with a dropped link passed verifyChain"
                )

            if len >= 1 then
                let tIdx = rng.IntBelow len
                let victim = List.item tIdx recs

                let flippedHash =
                    if victim.Hash.Length = 0 then
                        "x"
                    else
                        let c = victim.Hash.[0]
                        let c' = if c = '0' then '1' else '0'
                        string c' + victim.Hash.Substring(1)

                if flippedHash <> victim.Hash then
                    flips <- flips + 1

                    let flipped =
                        recs
                        |> List.mapi (fun j r -> if j = tIdx then { r with Hash = flippedHash } else r)

                    tamper.Check(
                        not (OpStream.verifyChain hashFn sw flipped),
                        fun () -> at "a bit-flipped hash passed verifyChain"
                    ))

        LawKit.results [ determinism; parity; tamper ]
        @ [ SampleAdequacy.reached
                "Conformance.hashFnLaws"
                "tamper arm"
                seed
                [ "reorder", reorders; "drop", drops; "bit-flip", flips ] ]

    /// The `HashFn` crypto-posture law (Phase 65) — pins the documented *"the default FNV-1a is not
    /// cryptographic; supply a collision-resistant `HashFn` for adversarial tamper-evidence"* contract
    /// (STABILITY.md "Hash-chain integrity posture"). A *re-hashed forgery* — rewriting history and
    /// recomputing every hash — is always internally consistent, so `verifyChain` re-accepts it; the only
    /// thing between an adversary and a forged chain that still matches an externally-committed head is
    /// the hash's **collision resistance**. This law exhibits that difference directly, at the `HashFn`
    /// level, over a deterministic pre-image enumeration:
    ///
    ///  - the supplied `cryptoHf` (a collision-resistant, wide stand-in) admits **no** pre-image
    ///    collision within `budget` — a forger cannot land a chosen head, so a re-hashed forgery is
    ///    caught (its head moves);
    ///  - `OpStream.defaultHash` (32-bit FNV-1a) **does** admit a collision within the same budget —
    ///    two distinct pre-images share a chain hash, the forgery primitive the posture documents.
    ///
    /// The law PASSES when the crypto stand-in resists and the default admits (the documented posture);
    /// it FAILS only on a regression (the default silently widened, or the stand-in collided in-budget).
    /// No cryptographic hash ships in Core — `cryptoHf` is a host-side / test stand-in (GP3).
    let hashFnAdversarialLaws (cryptoHf: HashFn) (budget: int) (seed: int) : LawResult list =
        let resist =
            LawKit.LawCell "a collision-resistant HashFn resists a re-hashed forgery (no in-budget pre-image collision)"

        let admit =
            LawKit.LawCell
                "the default FNV-1a admits a re-hashed forgery (an in-budget collision — documented non-crypto posture)"

        // The first two distinct pre-images sharing a hash — the re-hashed-forgery primitive. The
        // enumeration is `seed + k` for `k` below the budget: no draw, so no cursor — the budget IS
        // the sample, and the seed only offsets where it starts.
        let firstCollision (hf: HashFn) : (string * string) option =
            let seen = System.Collections.Generic.Dictionary<string, string>()
            let mutable found = None
            let mutable k = 0

            while found.IsNone && k < budget do
                let payload = "forge-" + string (seed + k)
                let h = hf "" payload

                match seen.TryGetValue h with
                | true, prior when prior <> payload -> found <- Some(prior, payload)
                | true, _ -> ()
                | false, _ -> seen.[h] <- payload

                k <- k + 1

            found

        (match firstCollision cryptoHf with
         | None -> resist.Saw()
         | Some(a, b) ->
             resist.Check(
                 false,
                 fun () ->
                     sprintf
                         "seed=%d: the crypto stand-in collided in-budget (%s / %s) — too weak for the posture"
                         seed
                         a
                         b
             ))

        (match firstCollision OpStream.defaultHash with
         | Some(a, b) when a <> b && OpStream.defaultHash "" a = OpStream.defaultHash "" b -> admit.Saw()
         | Some(a, b) ->
             admit.Check(false, fun () -> sprintf "seed=%d: an FNV-1a 'collision' did not check out (%s / %s)" seed a b)
         | None ->
             admit.Check(
                 false,
                 fun () ->
                     sprintf
                         "seed=%d: no FNV-1a collision within budget=%d — the default may have silently widened (posture regression)"
                         seed
                         budget
             ))

        LawKit.results [ resist; admit ]

    /// The attributed-stream lift laws (Phase 81) — the teeth on `OpStream.Attributed.liftWitness` and
    /// the "attribution rides inside the chained op, so the hash chain covers it" claim. Over a
    /// seed-replayable sample it builds two parallel chains from the same op sequence — a bare inner
    /// chain and an attributed chain through the lifted witness (each op wrapped in a synthesized
    /// actor/session/turn/timestamp envelope) — and certifies:
    ///
    ///  - **lift preserves replay** — the attributed stream replays to exactly the state the inner ops
    ///    replay to (attribution is provenance, never state — `Apply` delegates to the inner reducer);
    ///  - **the chain covers attribution** — re-attributing a chained op (mutating its actor field)
    ///    breaks `verifyChain`, because the envelope rides inside the hashed op encoding: attribution is
    ///    tamper-evident on the same footing as op-tampering, with no new witness field (GP2);
    ///  - **envelope round-trip** — the attributed chain survives `toJsonl` → `fromJsonl` byte-for-byte
    ///    (the envelope codec is exercised through the real persistence path) and still `verifyChain`s.
    ///
    /// No new witness field — the lift is a derived value over the existing `StreamWitness`. `'State`
    /// and `'Op` need equality (replay + round-trip comparison). Opt-in like `snapshotLaws` / `dagLaws`.
    ///
    /// Phase 297 — the re-attribution arm runs only over a NON-EMPTY attributed chain, and whether
    /// the chain is non-empty is the domain's doing (a drawn op it refuses does not extend it). The
    /// guard beside the laws counts the chains actually re-attributed, and the tamper law reads
    /// through it.
    let attributedLaws
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let lifted = OpStream.Attributed.liftWitness w

        let replayCx =
            LawKit.LawCell "attributed lift preserves replay (a lifted stream replays to its inner-op state)"

        let tamperCx =
            LawKit.LawCell(
                "the chain covers attribution (re-attributing a chained op breaks verifyChain)",
                Some "tampered chain"
            )

        let roundTripCx =
            LawKit.LawCell "attribution envelope round-trips through JSONL (byte-identical + still verifies)"

        let mutable reattributed = 0

        LawKit.run iterations seed (fun rng _ at ->
            let mutable innerState = gen.State0
            let mutable innerRecs = OpStream.empty
            let mutable attrState = gen.State0
            let mutable attrRecs = OpStream.empty

            for j in 0..5 do
                let op = rng.Draw gen.Op
                let ab = rng.IntBelow 3
                let sb = rng.IntBelow 2
                let tb = rng.IntBelow 4

                let attr: Attributed<'Op> =
                    { Actor = "actor-" + string ab
                      Session = "sess-" + string sb
                      Turn = (if tb = 0 then None else Some tb)
                      At = "t" + string j
                      Op = op }

                // The SAME op is appended to both chains; the lifted Apply delegates to the inner Apply on
                // .Op, so both accept/reject identically and stay structurally parallel.
                match OpStream.append hashFn w (Human "conf") op innerState innerRecs with
                | Ok(s, rs) ->
                    innerState <- s
                    innerRecs <- rs
                | Error _ -> ()

                match OpStream.append hashFn lifted (Human "conf") attr attrState attrRecs with
                | Ok(s, rs) ->
                    attrState <- s
                    attrRecs <- rs
                | Error _ -> ()

            // replay parity: the attributed stream replays to the inner-op state (from origin AND live).
            (match OpStream.replay lifted gen.State0 attrRecs, OpStream.replay w gen.State0 innerRecs with
             | Ok a, Ok b when a = b && a = attrState && b = innerState -> replayCx.Saw()
             | other -> replayCx.Check(false, fun () -> at (sprintf "attributed replay ≠ inner replay (%A)" other)))

            // chain covers attribution: mutate a chained op's actor field ⇒ verifyChain must reject.
            (match attrRecs with
             | [] -> ()
             | _ ->
                 reattributed <- reattributed + 1
                 let tIdx = rng.IntBelow(List.length attrRecs)

                 let tampered =
                     attrRecs
                     |> List.mapi (fun k r ->
                         if k = tIdx then
                             { r with
                                 Op = { r.Op with Actor = r.Op.Actor + "~" } }
                         else
                             r)

                 tamperCx.Check(
                     not (OpStream.verifyChain hashFn lifted tampered),
                     fun () -> at "re-attributing a chained op was not detected"
                 ))

            // envelope round-trip: the attributed chain survives toJsonl/fromJsonl byte-for-byte + verifies.
            match OpStream.toJsonl lifted attrRecs |> OpStream.fromJsonl lifted with
            | Ok restored when restored = attrRecs && OpStream.verifyChain hashFn lifted restored -> roundTripCx.Saw()
            | other ->
                roundTripCx.Check(false, fun () -> at (sprintf "attributed JSONL round-trip ≠ original (%A)" other)))

        LawKit.results [ replayCx; tamperCx; roundTripCx ]
        @ [ SampleAdequacy.reached "Conformance.attributedLaws" "tampered chain" seed [ "re-attributed", reattributed ] ]

    // ---- construct-then-encode (Phase 126) ----
    // Every family above that touches a codec certifies `decode` and `encode` against each other.
    // That certifies the CODEC and says nothing about the AUTHORING surface a program actually
    // writes against: the smart constructors, the builders, the fluent factory. The two are
    // different functions into the same type, and only one of them is exercised by a round-trip
    // suite.
    //
    // The gap is not hypothetical. In the `@fuaran-ui/ui` 0.26.0 release (2026-09-11, recorded as
    // fuaran#1661) one field widened in memory to a richer shape; the decoder-encoder suite stayed
    // green over thousands of vectors, because nothing in it ever built a value the way an author
    // builds one - and the only author-direction consumer broke on the pin bump.
    // A round-trip law cannot see that by construction: it starts from bytes and ends at bytes, and
    // the authoring surface is not on that path.
    //
    // So this family runs the corpus through the authoring surface: decode a corpus document,
    // REBUILD it through the domain's own constructors, and re-encode. A domain opts in by
    // supplying a `ConstructWitness`; one that supplies none is reported BY NAME as not adopted,
    // never silently skipped and never counted as passed.

    /// The construct-then-encode laws (Phase 126) - the authoring surface certified, not only the
    /// codec. Over a domain's own conformance corpus (its `Corpus.Case` list; a `Reject` case is not
    /// a document, so only `RoundTrip` ones are read):
    ///
    ///  - **non-vacuity** - the corpus offers at least one round-trip document. A family with no
    ///    document certifies nothing, and would otherwise report the same green as one that
    ///    certified a thousand;
    ///  - **acceptance** - every document decodes, and the authoring surface accepts the decoded
    ///    value. A constructor that REFUSES a value the domain's own codec just produced is a
    ///    finding about the surface, distinct from one that builds a different value;
    ///  - **the law** - `encode (construct (decode b)) = encode (decode b)` for every document: what
    ///    an author builds encodes exactly as what the codec decoded. The counterexample also states
    ///    whether the plain codec round-trip passes over that same document, because it usually does
    ///    - that is the whole finding this family exists for, and a reader meeting the red for the
    ///    first time should not have to establish it.
    ///
    /// **Why the right-hand side is `encode (decode b)` and not the literal bytes `b`.** The law is
    /// naturally stated as `encode (construct (decode b)) = b`, and on a corpus whose documents are
    /// written in their codec's own canonical form that is exactly what this computes. But a
    /// `Corpus.Case`'s JSON is not required to be canonical - `Corpus.roundTrip` compares VALUES, so
    /// a legal corpus may spell a document with a different key order or spacing - and comparing
    /// against its literal bytes would then redden on the corpus's formatting rather than on the
    /// authoring surface. Comparing against the codec's own encoding of the same decoded value
    /// isolates the one subject this family has, on any corpus.
    ///
    /// `'T` needs no equality: the comparison is between two encodings, which the codec already
    /// promises are strings.
    let constructThenEncodeLaws
        (domain: string)
        (codec: Corpus.Codec<'T>)
        (witness: ConstructWitness<'T> option)
        (corpus: Corpus.Case list)
        : LawResult list =
        let documents = corpus |> List.filter (fun c -> c.Kind = Corpus.RoundTrip)

        match witness with
        | None ->
            // A `LawResult` has two states and no third, and widening it is a compile-breaking
            // change for every consumer that constructs one - so "not adopted" is reported as NOT
            // PASSED, which is the honest reading: a family asked to certify an authoring surface it
            // was never given has certified nothing. A domain that has decided the family does not
            // apply records that in its own conformance census as a reasoned non-use; it does not
            // run the family with no witness and read the green.
            let adopted =
                LawKit.LawCell(
                    "construct-then-encode ("
                    + domain
                    + "): NOT ADOPTED - no ConstructWitness supplied"
                )

            adopted.Check(
                false,
                fun () ->
                    domain
                    + " supplies no ConstructWitness, so its authoring surface is uncertified: the codec round-trip laws beside this one prove only that bytes survive decode and encode, never that the smart constructors an author calls rebuild a corpus document to the same bytes. Supply a witness, or record the family as a reasoned non-use in the domain's conformance census rather than running it with none."
            )

            LawKit.results [ adopted ]
        | Some w ->
            // The sample is the caller's corpus rather than a draw, so there is no seed and no
            // cursor; the non-vacuity law is the family's own guard on that sample, and the two
            // laws that run only over a document read through it (Phase 297) — an empty corpus is
            // reported once, by the law that names it.
            let nonVacuity =
                LawKit.LawCell(
                    "construct-then-encode ("
                    + domain
                    + "): the corpus offers at least one round-trip document to rebuild"
                )

            let accepted =
                LawKit.LawCell(
                    "construct-then-encode ("
                    + domain
                    + "): every corpus document decodes, and "
                    + w.Surface
                    + " accepts the decoded value",
                    Some "round-trip document"
                )

            let reencoded =
                LawKit.LawCell(
                    "construct-then-encode ("
                    + domain
                    + "): encode (construct (decode b)) = encode (decode b) - what "
                    + w.Surface
                    + " builds encodes as what the codec decoded",
                    Some "round-trip document"
                )

            for c in documents do
                match codec.Decode c.Json with
                | Error m ->
                    accepted.Check(
                        false,
                        fun () ->
                            "case "
                            + c.Name
                            + ": the corpus document did not decode ("
                            + m
                            + ") - this family reads the codec's output, so certify the codec first (`Corpus.runCorpus`)"
                    )
                | Ok decoded ->
                    match w.Construct decoded with
                    | Error m ->
                        accepted.Check(
                            false,
                            fun () ->
                                "case "
                                + c.Name
                                + ": "
                                + w.Surface
                                + " refused a value the domain's own codec decoded ("
                                + m
                                + ") - the corpus and the authoring surface disagree about what is constructible"
                        )
                    | Ok built ->
                        accepted.Saw()
                        let canonical = codec.Encode decoded
                        let authored = codec.Encode built

                        reencoded.Check(
                            (authored = canonical),
                            fun () ->
                                let codecVerdict =
                                    match Corpus.roundTrip codec decoded with
                                    | Ok() ->
                                        "the plain codec round-trip PASSES over this same document, so the defect is in the authoring surface and not in the codec"
                                    | Error m ->
                                        "the plain codec round-trip also fails over this document ("
                                        + m
                                        + "), so certify the codec first"

                                "case "
                                + c.Name
                                + ": "
                                + w.Surface
                                + " re-encodes to "
                                + authored
                                + " where the decoded document encodes to "
                                + canonical
                                + " - "
                                + codecVerdict
                        )

            nonVacuity.Check(
                not (List.isEmpty documents),
                fun () ->
                    "the corpus carries "
                    + string (List.length corpus)
                    + " case(s) and none of them is a round-trip document, so every law below holds vacuously"
            )

            LawKit.results [ nonVacuity; accepted; reencoded ]
