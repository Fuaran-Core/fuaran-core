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
        let roundTrip =
            LawKit.LawCell "projection round-trip (project ∘ re-import ∘ parseBack ∘ project = project)"

        let subset =
            LawKit.LawCell "a scoped projection is a subset of the whole (ById / Subtree / ChangedSince)"

        let digestStable =
            LawKit.LawCell "digest stability (a projection line changes iff its content digest changes)"

        let compact =
            LawKit.LawCell "compactness (the projection is strictly smaller than the wire form)"
        // digest-stability witness across draws: id string -> (digest, content cell)
        let mutable seen = Map.empty<string, string * string>

        LawKit.run iterations seed (fun rng _ at ->
            let tree = rng.Draw gen
            let whole = Projection.project pw Whole tree

            // determinism: the same tree projects to the identical projection
            digestStable.Check(
                (Projection.project pw Whole tree = whole),
                fun () -> at "projecting the same tree twice differs"
            )

            // round-trip: render -> parseBack -> re-import -> project = the original projection
            match Projection.parseBack pw (Projection.render whole) with
            | Error e -> roundTrip.Check(false, fun () -> at (sprintf "parseBack rejected its own projection: %s" e))
            | Ok ops ->
                match applyOps ops with
                | Error e -> roundTrip.Check(false, fun () -> at (sprintf "re-import rejected the parsed ops: %s" e))
                | Ok tree2 ->
                    roundTrip.Check(
                        (Projection.project pw Whole tree2 = whole),
                        fun () -> at "re-imported tree projects differently"
                    )

            // scoped ⊆ whole, on a randomly-drawn id
            let ids = Tree.ids pw.Tree tree
            let target = rng.Choose ids

            let wholeRendered = whole.Lines |> List.map Projection.renderLine |> Set.ofList

            for scope in [ ById target; Subtree target ] do
                let scoped = Projection.project pw scope tree

                subset.Check(
                    not (
                        scoped.Lines
                        |> List.exists (fun l -> not (wholeRendered.Contains(Projection.renderLine l)))
                    ),
                    fun () ->
                        at (
                            sprintf "a scoped line (target %s) is not in the whole projection" (pw.IdW.ToString target)
                        )
                )

            subset.Check(
                ((Projection.project pw (ChangedSince(Projection.snapshot pw tree)) tree).Lines = []),
                fun () -> at "ChangedSince over an unchanged tree is non-empty"
            )

            // digest ⇔ content cell, across draws (same id, possibly different content)
            for l in whole.Lines do
                match Map.tryFind l.IdKey seen with
                | Some(digest, cell) ->
                    digestStable.Check(
                        ((digest = l.Digest) = (cell = Projection.lineText l)),
                        fun () -> at (sprintf "node %s — line changed without a digest change (or vice versa)" l.IdKey)
                    )
                | None -> seen <- Map.add l.IdKey (l.Digest, Projection.lineText l) seen

            // compactness: strictly smaller than the wire form
            compact.Check(
                (Projection.sizeOf whole < String.length (wireEncode tree)),
                fun () ->
                    at (
                        sprintf
                            "projection (%d chars) is not smaller than the wire form (%d chars)"
                            (Projection.sizeOf whole)
                            (String.length (wireEncode tree))
                    )
            ))

        LawKit.results [ roundTrip; subset; digestStable; compact ]

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
        let completeness =
            LawKit.LawCell
                "catalogue completeness (every emitted op kind is catalogued; every catalogued kind is emittable)"

        let readTools =
            LawKit.LawCell "read tools are total + deterministic; an unknown tool is refused naming the alternatives"

        let patterns =
            LawKit.LawCell
                "pattern resolution is deterministic (an anchor-built intent resolves, identically every time)"

        let proposals =
            LawKit.LawCell(
                if kitPolicy then
                    "proposal soundness (approved applies via the domain reducer; denied/rejected never mutates)"
                else
                    "proposal soundness under the domain's own policy (approved applies via the domain reducer; denied/rejected never mutates)"
            )

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
                readTools.Check(
                    (t.Run state0 = t.Run state0),
                    fun () -> sprintf "seed=%d: read tool '%s' is not deterministic" seed t.Name
                )
         with ex ->
             readTools.Check(false, fun () -> sprintf "seed=%d: a read tool threw: %s" seed ex.Message))

        if w.ReadTools |> List.forall (fun t -> t.Name <> "__no_such_tool__") then
            match AiSurface.runTool w "__no_such_tool__" state0 with
            | Ok _ ->
                readTools.Check(false, fun () -> sprintf "seed=%d: an unknown read-tool name was not refused" seed)
            | Error msg ->
                readTools.Check(
                    (w.ReadTools |> List.forall (fun t -> msg.Contains t.Name)),
                    fun () -> sprintf "seed=%d: the unknown-tool refusal does not enumerate the available tools" seed
                )

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

        LawKit.run iterations seed (fun rng _ at ->
            let op = rng.Draw genOp

            // ---- catalogue completeness: emitted direction ----
            let kind = w.KindOfOp op
            seenKinds <- Set.add kind seenKinds

            completeness.Check(
                Set.contains kind catalogued,
                fun () -> at (sprintf "op kind '%s' is not in the catalogue" kind)
            )

            // ---- pattern determinism ----
            match w.Patterns with
            // An empty bank is checked WHOLE, not missed by the draw: there is no pattern to resolve
            // and no generator that could reach one, so the law holds over the entire declared bank
            // and is counted as asserted — the never-reached remedy (widen the generator) cannot
            // apply to it.
            | [] -> patterns.Saw()
            | bank ->
                let card = rng.Choose bank

                match card.PromptAnchors with
                | [] -> patterns.Check(false, fun () -> at (sprintf "pattern '%s' has no anchors" card.Name))
                | anchors ->
                    let anchor = rng.Choose anchors
                    let tok = rng.IntBelow 1000
                    let token = "v" + string tok

                    let intent =
                        { Text = "please " + textOfAnchor token anchor + " now"
                          Args = [ "value", token ] }

                    let a = PatternBank.resolve w intent

                    patterns.Check(
                        a.IsSome,
                        fun () ->
                            at (sprintf "intent built from pattern '%s' anchor '%s' did not resolve" card.Name anchor)
                    )

                    patterns.Check(
                        (a = PatternBank.resolve w intent),
                        fun () -> at "pattern resolution is not deterministic"
                    )

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
                    proposals.Check(false, fun () -> at (sprintf "Decide/Apply threw: %s" ex.Message))
                    None

            match direct with
            | None -> ()
            | Some(direct, domainDecision) ->
                // a reducer rejection renders non-empty guidance through Explain.
                (match direct with
                 | Error rej ->
                     refused <- refused + 1

                     proposals.Check(
                         (Proposals.explainRejection w rej <> ""),
                         fun () -> at "explainRejection rendered empty guidance"
                     )
                 | Ok _ -> accepted <- accepted + 1)

                // the decision under test: the kit's roll, or the domain's own policy (Phase 246).
                let decision =
                    if kitPolicy then
                        let dRoll = rng.IntBelow 3

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
                        proposals.Check((s' = sd), fun () -> at "an allowed submit ≠ direct apply")
                    | Proposals.SubmitOpRejected _, Error _ -> proposals.Saw()
                    | other, _ ->
                        proposals.Check(
                            false,
                            fun () -> at (sprintf "allowed submit disagreed with the reducer (%A)" other)
                        )
                | NeedsApproval ->
                    parked <- parked + 1
                    // NeedsApproval: parks without applying; approval applies (or stays pending).
                    let wi = wUnder

                    match Proposals.submit wi author "t0" (Some "intent") [ op ] Proposals.Queue.empty state0 with
                    | Proposals.SubmitProposed(q, id) ->
                        proposals.Check((applyCalls.Value = 0), fun () -> at "parking a proposal invoked the reducer")

                        (match Proposals.approve wi "approver" "t1" id q state0, direct with
                         | Ok(q2, s'), Ok sd ->
                             proposals.Check((s' = sd), fun () -> at "an approved proposal ≠ direct apply")

                             // double-decide is a named failure.
                             match Proposals.approve wi "approver" "t2" id q2 state0 with
                             | Error(Proposals.NotPending _) -> proposals.Saw()
                             | _ -> proposals.Check(false, fun () -> at "a decided proposal was re-decidable")
                         | Error(Proposals.OpNoLongerApplies _), Error _ -> proposals.Saw()
                         | other, _ ->
                             proposals.Check(
                                 false,
                                 fun () -> at (sprintf "approval disagreed with the reducer (%A)" other)
                             ))

                        // rejection never invokes the reducer.
                        let before = applyCalls.Value

                        (match Proposals.reject "approver" "t1" "not now" id q with
                         | Ok _ ->
                             proposals.Check(
                                 (applyCalls.Value = before),
                                 fun () -> at "rejecting a proposal invoked the reducer"
                             )
                         | Error _ -> proposals.Check(false, fun () -> at "rejecting a pending proposal failed"))

                        // an unknown id is a named failure.
                        (match Proposals.approve wi "approver" "t1" 9999 q state0 with
                         | Error(Proposals.UnknownProposal _) -> proposals.Saw()
                         | _ -> proposals.Check(false, fun () -> at "an unknown proposal id was not refused"))
                    | other ->
                        proposals.Check(
                            false,
                            fun () -> at (sprintf "NeedsApproval did not park the submit (%A)" other)
                        )
                | Deny _ ->
                    denied <- denied + 1
                    // Deny: refused, and the reducer is never invoked.
                    match Proposals.submit wUnder author "t0" None [ op ] Proposals.Queue.empty state0 with
                    | Proposals.SubmitDenied _ ->
                        proposals.Check((applyCalls.Value = 0), fun () -> at "a denied submit invoked the reducer")
                    | other ->
                        proposals.Check(false, fun () -> at (sprintf "Deny did not refuse the submit (%A)" other)))

        // ---- catalogue completeness: emittable direction ----
        let missing = Set.difference catalogued seenKinds

        completeness.Check(
            Set.isEmpty missing,
            fun () ->
                sprintf
                    "seed=%d: catalogued kinds never emitted by the generator: %s"
                    seed
                    (missing |> Set.toList |> String.concat ", ")
        )

        LawKit.results [ completeness; readTools; patterns; proposals ]
        // Phase 223 — `Guarded ["accepted"; "refused"]`, after the subject laws. A generator that
        // draws no op the reducer rejects leaves the guidance law and the rejected-parity arms
        // certified by nothing; one that draws nothing applicable leaves the applied-parity arms so.
        @ [ SampleAdequacy.reached family "accepted op" seed [ "accepted", accepted ]
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
    ///
    /// `family` labels the guards — `Conformance.aiSurfaceLawsAt`, or the obsolete bare name's own
    /// id for one draft (Phase 297's naming rule: the domain-witness form is `…At`).
    let aiSurfaceLawsAt
        (family: string)
        (w: AiSurfaceWitness<'State, 'Op, 'Rej>)
        (genOp: ConfRng.T -> 'Op * ConfRng.T)
        (state0: 'State)
        (seed: int)
        (iterations: int)
        : LawResult list =
        aiSurfaceRun family false w genOp state0 seed iterations

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
            LawKit.LawCell(
                "witness surface ("
                + record
                + "): the record carries exactly its frozen fields, in declaration order"
            )

        if not (Microsoft.FSharp.Reflection.FSharpType.IsRecord t) then
            law.Check(
                false,
                fun () ->
                    record
                    + " is no longer an F# record, so its field set cannot be read — the freeze names a record"
            )
        else
            let actual =
                Microsoft.FSharp.Reflection.FSharpType.GetRecordFields t
                |> Array.map (fun p -> p.Name)
                |> Array.toList

            let mismatch () =
                let render (xs: string list) =
                    if List.isEmpty xs then "none" else String.concat ", " xs

                let added = actual |> List.filter (fun f -> not (List.contains f pinned))
                let removed = pinned |> List.filter (fun f -> not (List.contains f actual))

                let reordered =
                    if List.isEmpty added && List.isEmpty removed then
                        " (the same fields, reordered)"
                    else
                        ""

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

            law.Check((actual = pinned), mismatch)

        law.Result

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

        let law =
            LawKit.LawCell
                "witness surface: every public record named `…Witness` is frozen or declared outside the freeze, and every classified name exists"

        law.Check(List.isEmpty problems, fun () -> String.concat "; " problems)
        law.Result

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
