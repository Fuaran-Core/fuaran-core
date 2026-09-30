namespace Fuaran.Core

/// The surface families (Phase 297 split): projections, the AI surface, and the witness-record field freeze.
module internal SurfaceLaws =

    // ---- projection laws (Phase 58) ----
    // The teeth on `Fuaran.Core.Projection` — the read-token-lever seam. A domain supplies its
    // `ProjectionWitness`, its re-import (`applyOps` — the ops→tree half of the round trip), its
    // wire encoder (the compactness baseline), and a tree generator; the kit certifies the
    // projection contract over a seed-replayable sample.

    /// The projection laws (Phase 58):
    ///
    ///  - **round-trip idempotence** — `project ∘ (applyOps ∘ parseBack ∘ render) ∘ project =
    ///    project`: parsing a projection back to ops and re-importing them yields a tree whose
    ///    whole projection is identical (reads stay cheap AND writes stay trackable);
    ///  - **scoped ⊆ whole** — a `ById` / `Subtree` projection's rendered lines all appear in the
    ///    whole projection, and `ChangedSince` over an unchanged tree is empty;
    ///  - **digest stability** — projecting is deterministic, and across draws a node's content
    ///    cell (`lineText`) changes iff its content digest changes (depth/indent is presentation:
    ///    a structural move never rewrites a line);
    ///  - **compactness** — the whole projection is strictly smaller than the wire form on every
    ///    drawn tree (the token-reduction floor; a floor, not a fixed ratio).
    ///
    /// `wireEncode` and `applyOps` are per-call parameters (GP2) — the kit takes no dependency on
    /// a domain codec. Assumes the witness `Encode` is injective (certify it separately with
    /// `encoderInjectivityLaws` — a lossy encoder can alias two contents into one digest).
    let projectionLaws
        (pw: ProjectionWitness<'Node, 'Id, 'Op>)
        (applyOps: 'Op list -> Result<'Node, string>)
        (wireEncode: 'Node -> string)
        (gen: ConfRng.T -> 'Node * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable roundTrip = None
        let mutable subset = None
        let mutable digestStable = None
        let mutable compact = None
        // digest-stability witness across draws: id string -> (digest, content cell)
        let mutable seen = Map.empty<string, string * string>

        for i in 0 .. iterations - 1 do
            let tree, r1 = gen rng
            rng <- r1
            let whole = Projection.project pw Whole tree

            // determinism: the same tree projects to the identical projection
            if Projection.project pw Whole tree <> whole && digestStable.IsNone then
                digestStable <- Some(sprintf "seed=%d iter=%d: projecting the same tree twice differs" seed i)

            // round-trip: render -> parseBack -> re-import -> project = the original projection
            match Projection.parseBack pw (Projection.render whole) with
            | Error e ->
                if roundTrip.IsNone then
                    roundTrip <- Some(sprintf "seed=%d iter=%d: parseBack rejected its own projection: %s" seed i e)
            | Ok ops ->
                match applyOps ops with
                | Error e ->
                    if roundTrip.IsNone then
                        roundTrip <- Some(sprintf "seed=%d iter=%d: re-import rejected the parsed ops: %s" seed i e)
                | Ok tree2 ->
                    if Projection.project pw Whole tree2 <> whole && roundTrip.IsNone then
                        roundTrip <- Some(sprintf "seed=%d iter=%d: re-imported tree projects differently" seed i)

            // scoped ⊆ whole, on a randomly-drawn id
            let ids = Tree.ids pw.Tree tree
            let target, r2 = ConfRng.choose ids rng
            rng <- r2

            let wholeRendered = whole.Lines |> List.map Projection.renderLine |> Set.ofList

            for scope in [ ById target; Subtree target ] do
                let scoped = Projection.project pw scope tree

                if
                    scoped.Lines
                    |> List.exists (fun l -> not (wholeRendered.Contains(Projection.renderLine l)))
                    && subset.IsNone
                then
                    subset <-
                        Some(
                            sprintf
                                "seed=%d iter=%d: a scoped line (target %s) is not in the whole projection"
                                seed
                                i
                                (pw.IdW.ToString target)
                        )

            if
                (Projection.project pw (ChangedSince(Projection.snapshot pw tree)) tree).Lines
                <> []
                && subset.IsNone
            then
                subset <- Some(sprintf "seed=%d iter=%d: ChangedSince over an unchanged tree is non-empty" seed i)

            // digest ⇔ content cell, across draws (same id, possibly different content)
            for l in whole.Lines do
                match Map.tryFind l.IdKey seen with
                | Some(digest, cell) ->
                    if (digest = l.Digest) <> (cell = Projection.lineText l) && digestStable.IsNone then
                        digestStable <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: node %s — line changed without a digest change (or vice versa)"
                                    seed
                                    i
                                    l.IdKey
                            )
                | None -> seen <- Map.add l.IdKey (l.Digest, Projection.lineText l) seen

            // compactness: strictly smaller than the wire form
            if Projection.sizeOf whole >= String.length (wireEncode tree) && compact.IsNone then
                compact <-
                    Some(
                        sprintf
                            "seed=%d iter=%d: projection (%d chars) is not smaller than the wire form (%d chars)"
                            seed
                            i
                            (Projection.sizeOf whole)
                            (String.length (wireEncode tree))
                    )

        [ { Law = "projection round-trip (project ∘ re-import ∘ parseBack ∘ project = project)"
            Passed = roundTrip.IsNone
            Counterexample = roundTrip }
          { Law = "a scoped projection is a subset of the whole (ById / Subtree / ChangedSince)"
            Passed = subset.IsNone
            Counterexample = subset }
          { Law = "digest stability (a projection line changes iff its content digest changes)"
            Passed = digestStable.IsNone
            Counterexample = digestStable }
          { Law = "compactness (the projection is strictly smaller than the wire form)"
            Passed = compact.IsNone
            Counterexample = compact } ]

    // ---- AI-surface laws (Phase 59) ----

    /// The AI-surface laws (Phase 59) — the teeth on `AiSurfaceWitness`. A domain supplies its
    /// witness, an op generator, and a base artifact state; over a seed-replayable sample the kit
    /// certifies the four parts of the surface:
    ///
    ///  - **catalogue completeness** — every generated op's `KindOfOp` is catalogued in `OpKinds`,
    ///    and every catalogued kind is emitted at least once over the run (the generator must cover
    ///    the catalogue — an uncovered kind is a completeness failure, not a sampling accident);
    ///  - **read-tool discipline** — every read tool is total (never throws) and deterministic
    ///    (same state ⇒ the same `JVal`), and `AiSurface.runTool` refuses an unknown tool name with
    ///    guidance enumerating the available tools (default-deny by shape);
    ///  - **pattern determinism** — an intent built from a pattern's own anchor resolves, and
    ///    resolving the same intent twice yields the identical emission (no clock, no rng);
    ///  - **proposal soundness** — an `Allow`ed submit applies exactly what the domain reducer
    ///    applies (byte-equal states); a `NeedsApproval` submit parks without touching the reducer,
    ///    and approving it applies to the same state as a direct apply (or surfaces
    ///    `OpNoLongerApplies` when the reducer rejects, leaving the proposal pending); a rejected
    ///    proposal and a `Deny`ed submit never invoke the reducer (denied never mutates); an unknown
    ///    proposal id and a double-decide are named failures; and a reducer rejection renders
    ///    non-empty agent-readable guidance through `Explain`.
    ///
    /// `'State` and `'Op` need equality.
    ///
    /// **The decision is the DOMAIN'S (Phase 246).** Every drawn op is submitted as the actor
    /// `"author"` through the domain's own `Decide`, and the proposal-soundness arm that runs is the
    /// one that policy chose. Until `0.32.0` the kit substituted its own `Decide` per draw, so the
    /// plumbing was certified for any policy and the domain's policy was sampled only for totality:
    /// a policy that allowed every write passed. Now the guard also counts the decisions the domain's
    /// policy reached — allowed, parked for approval, denied — and a policy that never parks or never
    /// denies anything the generator draws is starved, RED rather than green: the gate was never
    /// exercised at this domain. `aiSurfaceLawsUnderKitPolicy` is the old behaviour, named for what it
    /// does; it certifies the plumbing and says nothing about the domain's policy.
    let private aiSurfaceRun
        (family: string)
        (kitPolicy: bool)
        (w: AiSurfaceWitness<'State, 'Op, 'Rej>)
        (genOp: ConfRng.T -> 'Op * ConfRng.T)
        (state0: 'State)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable completeness = None
        let mutable readTools = None
        let mutable patterns = None
        let mutable proposals = None

        let catalogued = w.OpKinds |> List.map (fun o -> o.Kind) |> Set.ofList
        let mutable seenKinds = Set.empty
        // Phase 223 — the reducer's outcome populations over the caller's DRAWN ops:
        // `explainRejection` and the rejected arms of the allow / approve parity read a reducer
        // rejection, which a generator that draws only applicable ops never reaches.
        let mutable accepted = 0
        let mutable refused = 0
        // Phase 246 — the decisions the policy under test reached. Under the domain's policy these are
        // the gate's populations: an arm no drawn op reaches is an arm the family never tested.
        let mutable allowed = 0
        let mutable parked = 0
        let mutable denied = 0
        let author = "author"

        // ---- read-tool discipline (state-fixed — checked once, not per draw) ----

        (try
            for t in w.ReadTools do
                if t.Run state0 <> t.Run state0 && readTools.IsNone then
                    readTools <- Some(sprintf "seed=%d: read tool '%s' is not deterministic" seed t.Name)
         with ex ->
             if readTools.IsNone then
                 readTools <- Some(sprintf "seed=%d: a read tool threw: %s" seed ex.Message))

        if w.ReadTools |> List.forall (fun t -> t.Name <> "__no_such_tool__") then
            match AiSurface.runTool w "__no_such_tool__" state0 with
            | Ok _ ->
                if readTools.IsNone then
                    readTools <- Some(sprintf "seed=%d: an unknown read-tool name was not refused" seed)
            | Error msg ->
                if
                    not (w.ReadTools |> List.forall (fun t -> msg.Contains t.Name))
                    && readTools.IsNone
                then
                    readTools <-
                        Some(sprintf "seed=%d: the unknown-tool refusal does not enumerate the available tools" seed)

        // an intent text a pattern's own anchor matches: wildcard spans filled with a drawn token.
        let textOfAnchor (token: string) (anchor: string) : string =
            let sb = System.Text.StringBuilder()
            let mutable i = 0

            while i < anchor.Length do
                if anchor.[i] = '{' then
                    let close = anchor.IndexOf('}', i)
                    sb.Append token |> ignore
                    i <- (if close < 0 then anchor.Length else close + 1)
                else
                    sb.Append anchor.[i] |> ignore
                    i <- i + 1

            sb.ToString()

        for i in 0 .. iterations - 1 do
            let op, r1 = genOp rng
            rng <- r1

            // ---- catalogue completeness: emitted direction ----
            let kind = w.KindOfOp op
            seenKinds <- Set.add kind seenKinds

            if not (Set.contains kind catalogued) && completeness.IsNone then
                completeness <- Some(sprintf "seed=%d iter=%d: op kind '%s' is not in the catalogue" seed i kind)

            // ---- pattern determinism ----
            match w.Patterns with
            | [] -> ()
            | bank ->
                let card, r2 = ConfRng.choose bank rng
                rng <- r2

                match card.PromptAnchors with
                | [] ->
                    if patterns.IsNone then
                        patterns <- Some(sprintf "seed=%d iter=%d: pattern '%s' has no anchors" seed i card.Name)
                | anchors ->
                    let anchor, r3 = ConfRng.choose anchors rng
                    rng <- r3
                    let tok, r4 = ConfRng.intBelow 1000 rng
                    rng <- r4
                    let token = "v" + string tok

                    let intent =
                        { Text = "please " + textOfAnchor token anchor + " now"
                          Args = [ "value", token ] }

                    let a = PatternBank.resolve w intent

                    if a.IsNone && patterns.IsNone then
                        patterns <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: intent built from pattern '%s' anchor '%s' did not resolve"
                                    seed
                                    i
                                    card.Name
                                    anchor
                            )

                    if a <> PatternBank.resolve w intent && patterns.IsNone then
                        patterns <- Some(sprintf "seed=%d iter=%d: pattern resolution is not deterministic" seed i)

            // ---- proposal soundness ----
            // the kit drives the decision axis so all three outcomes are exercised for any policy;
            // the reducer is instrumented so "never mutates" is observable, not just typed away.
            // (a ref cell — a closure cannot capture a `let mutable` local.)
            let applyCalls = ref 0

            let wDriven (d: PolicyDecision) =
                { w with
                    Decide = fun _ _ -> d
                    Apply =
                        fun o s ->
                            applyCalls.Value <- applyCalls.Value + 1
                            w.Apply o s }

            // the domain's own Decide + Apply are total (sampled, never throw).
            let direct =
                try
                    let domainDecision = w.Decide (if kitPolicy then "conformance" else author) op
                    Some(w.Apply op state0, domainDecision)
                with ex ->
                    if proposals.IsNone then
                        proposals <- Some(sprintf "seed=%d iter=%d: Decide/Apply threw: %s" seed i ex.Message)

                    None

            match direct with
            | None -> ()
            | Some(direct, domainDecision) ->
                // a reducer rejection renders non-empty guidance through Explain.
                (match direct with
                 | Error rej ->
                     refused <- refused + 1

                     if Proposals.explainRejection w rej = "" && proposals.IsNone then
                         proposals <- Some(sprintf "seed=%d iter=%d: explainRejection rendered empty guidance" seed i)
                 | Ok _ -> accepted <- accepted + 1)

                // the decision under test: the kit's roll, or the domain's own policy (Phase 246).
                let decision =
                    if kitPolicy then
                        let dRoll, r5 = ConfRng.intBelow 3 rng
                        rng <- r5

                        match dRoll with
                        | 0 -> Allow
                        | 1 -> NeedsApproval
                        | _ -> Deny "policy says no"
                    else
                        domainDecision

                let wUnder =
                    if kitPolicy then
                        wDriven decision
                    else
                        { wDriven decision with
                            Decide = w.Decide }

                match decision with
                | Allow ->
                    allowed <- allowed + 1
                    // Allow: submit applies exactly what the reducer applies.
                    match Proposals.submit wUnder author "t0" None [ op ] Proposals.Queue.empty state0, direct with
                    | Proposals.SubmitApplied s', Ok sd ->
                        if s' <> sd && proposals.IsNone then
                            proposals <- Some(sprintf "seed=%d iter=%d: an allowed submit ≠ direct apply" seed i)
                    | Proposals.SubmitOpRejected _, Error _ -> ()
                    | other, _ ->
                        if proposals.IsNone then
                            proposals <-
                                Some(
                                    sprintf
                                        "seed=%d iter=%d: allowed submit disagreed with the reducer (%A)"
                                        seed
                                        i
                                        other
                                )
                | NeedsApproval ->
                    parked <- parked + 1
                    // NeedsApproval: parks without applying; approval applies (or stays pending).
                    let wi = wUnder

                    match Proposals.submit wi author "t0" (Some "intent") [ op ] Proposals.Queue.empty state0 with
                    | Proposals.SubmitProposed(q, id) ->
                        if applyCalls.Value <> 0 && proposals.IsNone then
                            proposals <- Some(sprintf "seed=%d iter=%d: parking a proposal invoked the reducer" seed i)

                        (match Proposals.approve wi "approver" "t1" id q state0, direct with
                         | Ok(q2, s'), Ok sd ->
                             if s' <> sd && proposals.IsNone then
                                 proposals <-
                                     Some(sprintf "seed=%d iter=%d: an approved proposal ≠ direct apply" seed i)

                             // double-decide is a named failure.
                             match Proposals.approve wi "approver" "t2" id q2 state0 with
                             | Error(Proposals.NotPending _) -> ()
                             | _ ->
                                 if proposals.IsNone then
                                     proposals <-
                                         Some(sprintf "seed=%d iter=%d: a decided proposal was re-decidable" seed i)
                         | Error(Proposals.OpNoLongerApplies _), Error _ -> ()
                         | other, _ ->
                             if proposals.IsNone then
                                 proposals <-
                                     Some(
                                         sprintf
                                             "seed=%d iter=%d: approval disagreed with the reducer (%A)"
                                             seed
                                             i
                                             other
                                     ))

                        // rejection never invokes the reducer.
                        let before = applyCalls.Value

                        (match Proposals.reject "approver" "t1" "not now" id q with
                         | Ok _ ->
                             if applyCalls.Value <> before && proposals.IsNone then
                                 proposals <-
                                     Some(sprintf "seed=%d iter=%d: rejecting a proposal invoked the reducer" seed i)
                         | Error _ ->
                             if proposals.IsNone then
                                 proposals <-
                                     Some(sprintf "seed=%d iter=%d: rejecting a pending proposal failed" seed i))

                        // an unknown id is a named failure.
                        (match Proposals.approve wi "approver" "t1" 9999 q state0 with
                         | Error(Proposals.UnknownProposal _) -> ()
                         | _ ->
                             if proposals.IsNone then
                                 proposals <-
                                     Some(sprintf "seed=%d iter=%d: an unknown proposal id was not refused" seed i))
                    | other ->
                        if proposals.IsNone then
                            proposals <-
                                Some(sprintf "seed=%d iter=%d: NeedsApproval did not park the submit (%A)" seed i other)
                | Deny _ ->
                    denied <- denied + 1
                    // Deny: refused, and the reducer is never invoked.
                    match Proposals.submit wUnder author "t0" None [ op ] Proposals.Queue.empty state0 with
                    | Proposals.SubmitDenied _ ->
                        if applyCalls.Value <> 0 && proposals.IsNone then
                            proposals <- Some(sprintf "seed=%d iter=%d: a denied submit invoked the reducer" seed i)
                    | other ->
                        if proposals.IsNone then
                            proposals <-
                                Some(sprintf "seed=%d iter=%d: Deny did not refuse the submit (%A)" seed i other)

        // ---- catalogue completeness: emittable direction ----
        let missing = Set.difference catalogued seenKinds

        if not (Set.isEmpty missing) && completeness.IsNone then
            completeness <-
                Some(
                    sprintf
                        "seed=%d: catalogued kinds never emitted by the generator: %s"
                        seed
                        (missing |> Set.toList |> String.concat ", ")
                )

        [ { Law = "catalogue completeness (every emitted op kind is catalogued; every catalogued kind is emittable)"
            Passed = completeness.IsNone
            Counterexample = completeness }
          { Law = "read tools are total + deterministic; an unknown tool is refused naming the alternatives"
            Passed = readTools.IsNone
            Counterexample = readTools }
          { Law = "pattern resolution is deterministic (an anchor-built intent resolves, identically every time)"
            Passed = patterns.IsNone
            Counterexample = patterns }
          { Law =
              if kitPolicy then
                  "proposal soundness (approved applies via the domain reducer; denied/rejected never mutates)"
              else
                  "proposal soundness under the domain's own policy (approved applies via the domain reducer; denied/rejected never mutates)"
            Passed = proposals.IsNone
            Counterexample = proposals }
          // Phase 223 — `Guarded ["accepted"; "refused"]`, after the subject laws. A generator that
          // draws no op the reducer rejects leaves the guidance law and the rejected-parity arms
          // certified by nothing; one that draws nothing applicable leaves the applied-parity arms so.
          SampleAdequacy.reached family "accepted op" seed [ "accepted", accepted ]
          SampleAdequacy.reached family "rejected op" seed [ "refused", refused ] ]
        // Phase 246 — under the domain's policy, the decisions it reached. The kit's roll reaches all
        // three by construction, so the kit-policy form carries no such line.
        @ (if kitPolicy then
               []
           else
               [ SampleAdequacy.reached
                     family
                     "policy decision"
                     seed
                     [ "allowed", allowed; "parked", parked; "denied", denied ] ])

    /// The AI-surface laws (Phase 59) at the domain's OWN policy — see `aiSurfaceRun` above for the
    /// four laws. Since `0.32.0` (Phase 246) the decision each drawn op meets is the witness's
    /// `Decide`, and the family is starved unless that policy allows, parks and denies something the
    /// generator draws. `aiSurfaceLawsUnderKitPolicy` is the pre-`0.32.0` behaviour.
    let aiSurfaceLaws
        (w: AiSurfaceWitness<'State, 'Op, 'Rej>)
        (genOp: ConfRng.T -> 'Op * ConfRng.T)
        (state0: 'State)
        (seed: int)
        (iterations: int)
        : LawResult list =
        aiSurfaceRun "Conformance.aiSurfaceLaws" false w genOp state0 seed iterations

    /// The AI-surface laws with the KIT'S policy swapped in for the domain's (Phase 246 names it;
    /// it is `aiSurfaceLaws` as it stood before `0.32.0`). Per draw the kit rolls `Allow`,
    /// `NeedsApproval` or `Deny` itself, so every proposal arm is exercised for ANY policy — which
    /// certifies the proposal plumbing and says nothing about the domain's `Decide`, sampled here
    /// only for totality. A policy that allows every write passes it. Run it beside `aiSurfaceLaws`
    /// when the plumbing is the question, never instead of it.
    let aiSurfaceLawsUnderKitPolicy
        (w: AiSurfaceWitness<'State, 'Op, 'Rej>)
        (genOp: ConfRng.T -> 'Op * ConfRng.T)
        (state0: 'State)
        (seed: int)
        (iterations: int)
        : LawResult list =
        aiSurfaceRun "Conformance.aiSurfaceLawsUnderKitPolicy" true w genOp state0 seed iterations

    // ---- Phase 232 — the witness-record field freeze, held by a law --------------------------------
    //
    // STABILITY.md's "Witness-record field freeze" names six public witness records whose field sets
    // freeze at 1.0, and until this family nothing mechanical held them: the Phase 183 surface gate
    // classes a field add as `record-widening` and refuses only an UNCLASSIFIED move, so a field add
    // landed with its baseline regenerated passed the gate and the freeze rested on a reviewer.
    //
    // The family takes NO witness. It reads the records the kit was compiled against by reflection
    // (`FSharpType.GetRecordFields`, in declaration order) and holds each to the pinned list below by
    // name and in order — so a domain that runs it certifies that the Core it compiled against
    // carries the frozen shape, which is also the check a host wants at a pin bump. A seventh law
    // holds the pin list itself complete: every public record named `…Witness` in the Fuaran.Core
    // assemblies the kit references is either frozen here or declared outside the freeze, with the
    // reason, in `unfrozenWitnesses`. A new witness is therefore a CLASSIFICATION someone makes in
    // the commit that adds it, never one nobody noticed.

    /// The six frozen witness records, each with the type the law reads and its pinned field list.
    /// The type arguments are placeholders — a record's field NAMES and ORDER do not depend on them.
    let private frozenWitnesses: (string * System.Type * string list) list =
        [ "IdWitness", typeof<IdWitness<obj>>, [ "ToString"; "OfString"; "Equals" ]
          "NodeWitness", typeof<NodeWitness<obj, obj>>, [ "Id"; "KindTag"; "Children"; "ReplaceChildren" ]
          "StreamWitness", typeof<StreamWitness<obj, obj, obj>>, [ "Apply"; "Encode"; "Decode" ]
          "ArtifactWitness", typeof<ArtifactWitness<obj, obj>>, [ "Tree"; "IdW"; "Holes"; "Effect"; "Bind" ]
          "AiSurfaceWitness",
          typeof<AiSurfaceWitness<obj, obj, obj>>,
          [ "ReadTools"; "OpKinds"; "KindOfOp"; "Patterns"; "Decide"; "Apply"; "Explain" ]
          "ProjectionWitness",
          typeof<ProjectionWitness<obj, obj, obj>>,
          [ "Tree"; "IdW"; "Encode"; "Snippet"; "ParseBack" ] ]

    /// The frozen witness records and their field sets, by name and in declaration order — the
    /// freeze STABILITY.md states, as data (Phase 232).
    ///
    /// **Widening one before 1.0 is a deliberate, named act, never a regenerated baseline.** The
    /// law fails until the record's list here is edited to match, and the commit that edits it
    /// carries a `STABILITY.md` entry naming the record, the field and why composition — a new
    /// witness record that EMBEDS the frozen one — could not express it. From 1.0 there is no such
    /// route: a frozen witness does not grow.
    let frozenWitnessFields: (string * string list) list =
        frozenWitnesses |> List.map (fun (name, _, fields) -> name, fields)

    /// The public records named `…Witness` that the freeze deliberately does NOT cover, each with
    /// why (Phase 232). They are the conformance kit's own INPUTS: a domain constructs one only to
    /// run the opt-in family that takes it, and each evolves with that family. Listing them is what
    /// lets `witnessSurfaceLaws` hold every public witness to a classification; extending the freeze
    /// to one of them is a decision recorded in STABILITY.md, made by moving it to
    /// `frozenWitnessFields`.
    let unfrozenWitnesses: (string * string) list =
        [ "CapabilitySeamWitness",
          "a conformance-kit input (Phase 246): constructed only to run `capabilityLawsWith`, and versioned with that family"
          "QuerySeamWitness",
          "a conformance-kit input (Phase 246): constructed only to run `queryLawsWith`, and versioned with that family"
          "CapabilityPipelineWitness",
          "a conformance-kit input (Phase 246): constructed only to run `capabilityPipelineLawsWith`, and versioned with that family"
          "ConstructWitness",
          "a conformance-kit input (Phase 126): constructed only to run `constructThenEncodeLaws`, and versioned with that family"
          "KeyedWitness",
          "a conformance-kit input (Phase 189): constructed only to run `keyedChildrenLaws`, and versioned with that family"
          "EvaluatorWitness",
          "a conformance-kit input (Phase 211): constructed only to run the two `propagationEvaluatorLaws` families, and versioned with them" ]

    /// A type's name without its generic arity suffix — the name a reader and STABILITY.md use.
    let private bareTypeName (t: System.Type) : string =
        let n = t.Name
        let tick = n.IndexOf '`'
        if tick < 0 then n else n.Substring(0, tick)

    /// One frozen record's law: `t`'s record fields, by name and in declaration order, equal
    /// `pinned`. The law and its counterexample name `record`, so a red gate says WHICH witness
    /// moved and how — the fields added and removed, and the route a deliberate widening takes.
    let witnessFieldsLaw (record: string) (pinned: string list) (t: System.Type) : LawResult =
        let law =
            "witness surface ("
            + record
            + "): the record carries exactly its frozen fields, in declaration order"

        if not (Microsoft.FSharp.Reflection.FSharpType.IsRecord t) then
            { Law = law
              Passed = false
              Counterexample =
                Some(
                    record
                    + " is no longer an F# record, so its field set cannot be read — the freeze names a record"
                ) }
        else
            let actual =
                Microsoft.FSharp.Reflection.FSharpType.GetRecordFields t
                |> Array.map (fun p -> p.Name)
                |> Array.toList

            if actual = pinned then
                { Law = law
                  Passed = true
                  Counterexample = None }
            else
                let render (xs: string list) =
                    if List.isEmpty xs then "none" else String.concat ", " xs

                let added = actual |> List.filter (fun f -> not (List.contains f pinned))
                let removed = pinned |> List.filter (fun f -> not (List.contains f actual))

                let reordered =
                    if List.isEmpty added && List.isEmpty removed then
                        " (the same fields, reordered)"
                    else
                        ""

                { Law = law
                  Passed = false
                  Counterexample =
                    Some(
                        record
                        + " declares ["
                        + String.concat "; " actual
                        + "] where the freeze pins ["
                        + String.concat "; " pinned
                        + "] — added: "
                        + render added
                        + "; removed: "
                        + render removed
                        + reordered
                        + ". A frozen witness does not grow: compose a new witness record that embeds it (STABILITY.md, \"Witness-record field freeze\"), or, before 1.0 only, widen it deliberately — edit its entry in Conformance.frozenWitnessFields and record the widening in STABILITY.md in the same commit."
                    ) }

    /// The seventh law: every record in `records` whose name ends in `Witness` is classified —
    /// frozen (`frozenWitnessFields`) or declared outside the freeze (`unfrozenWitnesses`) — and
    /// every classified name is a record `records` actually holds. Both directions, so a new
    /// witness cannot appear unfrozen AND a renamed or deleted one cannot leave a pin standing over
    /// nothing. A name in both lists is refused too: a witness is frozen or it is not.
    let witnessCoverageLaw (records: System.Type list) : LawResult =
        let found =
            records
            |> List.filter Microsoft.FSharp.Reflection.FSharpType.IsRecord
            |> List.map bareTypeName
            |> List.filter (fun n -> n.EndsWith("Witness", System.StringComparison.Ordinal))
            |> List.distinct
            |> List.sort

        let frozen = frozenWitnessFields |> List.map fst
        let unfrozen = unfrozenWitnesses |> List.map fst
        let classified = frozen @ unfrozen

        let unclassified = found |> List.filter (fun n -> not (List.contains n classified))

        let stale =
            classified |> List.filter (fun n -> not (List.contains n found)) |> List.sort

        let both = frozen |> List.filter (fun n -> List.contains n unfrozen)

        let problems =
            [ if not (List.isEmpty unclassified) then
                  "unclassified public witness record(s): "
                  + String.concat ", " unclassified
                  + " — freeze each in Conformance.frozenWitnessFields, or declare it outside the freeze, with why, in Conformance.unfrozenWitnesses"
              if not (List.isEmpty stale) then
                  "classified name(s) with no public record behind them: "
                  + String.concat ", " stale
                  + " — a frozen witness was renamed or removed, or a declaration went stale"
              if not (List.isEmpty both) then
                  "declared both frozen and outside the freeze: " + String.concat ", " both ]

        { Law =
            "witness surface: every public record named `…Witness` is frozen or declared outside the freeze, and every classified name exists"
          Passed = List.isEmpty problems
          Counterexample =
            if List.isEmpty problems then
                None
            else
                Some(String.concat "; " problems) }

#if !FABLE_COMPILER
    /// Every public type in the kit's own assembly and in the Fuaran.Core assemblies it references,
    /// transitively. A Fable-compiled program has no assembly to enumerate, which is why this and
    /// the law that reads it are .NET-only (see `witnessSurfaceLaws`).
    let private kitPublicTypes () : System.Type list =
        let isCore (name: string) =
            name = "Fuaran.Core"
            || name.StartsWith("Fuaran.Core.", System.StringComparison.Ordinal)

        let seen = System.Collections.Generic.HashSet<string>()
        let found = System.Collections.Generic.List<System.Type>()

        let rec visit (asm: System.Reflection.Assembly) =
            if seen.Add(asm.GetName().Name) then
                found.AddRange(asm.GetExportedTypes())

                for reference in asm.GetReferencedAssemblies() do
                    if isCore reference.Name && not (seen.Contains reference.Name) then
                        visit (System.Reflection.Assembly.Load reference)

        visit typeof<LawResult>.Assembly
        found |> Seq.sortBy (fun t -> t.FullName) |> Seq.toList
#endif

    /// Phase 232 — the witness-record field freeze, as a law family. Seven laws on .NET: one per
    /// frozen record (`witnessFieldsLaw` over `frozenWitnessFields`), and `witnessCoverageLaw` over
    /// every public record in the Fuaran.Core assemblies the kit references. No witness and no seed:
    /// the family certifies the Core it was COMPILED AGAINST, so a domain runs it deliberately —
    /// typically at a pin bump — rather than through `certify`, which certifies a witness.
    ///
    /// Under Fable the coverage law is absent rather than reported green: a transpiled program has
    /// no assemblies to enumerate, and the completeness of the pin list is a property of this
    /// repository's tree, which its own .NET gate holds. The six field laws run on both pipelines.
    let witnessSurfaceLaws () : LawResult list =
        let fields =
            frozenWitnesses
            |> List.map (fun (name, t, pinned) -> witnessFieldsLaw name pinned t)

#if FABLE_COMPILER
        fields
#else
        fields @ [ witnessCoverageLaw (kitPublicTypes ()) ]
#endif
