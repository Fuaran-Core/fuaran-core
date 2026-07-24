namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.AiSurface (Phase 59) — the generic AI-surface contract that
//  consolidates the drifted per-domain AiTools (Doc, Pres, Calc each grew their
//  own) into one seam every witness domain adopts. Four parts, parameterised
//  over a domain witness so the core carries the *shape*, never the content
//  (GP6):
//
//    1. Read tools        — the introspection projections (outline / index /
//                           unbound-reference queries) as a witness-supplied set,
//                           each rendering to canonical wire `JVal`.
//    2. Op catalogue      — the domain's mutation-op catalogue as JSON (the
//                           `Fuaran.Core.Function` `toSchema` discipline), so an
//                           orchestrator emits ops from a schema without touching
//                           the wire encoder. Reference: Pres's `catalogueJson`.
//    3. Pattern bank      — canonical edit-intents → op sequences, with a
//                           deterministic fast-path resolver so the common edits
//                           skip the model. Reference: Calc's `Patterns`.
//    4. Proposals         — a human-in-loop approval gate re-using the domain's
//                           policy, with agent-readable rejection guidance that
//                           enumerates the alternatives. Reference: Calc's
//                           `Proposals`.
//    5. Arbitration       — which subset of N op-script proposals can land
//                           together against one base tree (Phase 85): batch
//                           `canApply` + a greedy footprint-independence pass
//                           (Phase 78) — a deterministic, total partition with
//                           typed, actionable rejections.
//
//  All read-tool logic, pattern *content*, and policy *rules* stay domain-side;
//  the witness supplies them per call (GP1/GP2 — additive, no base type, no
//  module-level state). FSharp.Core + Fuaran.Core.Wire + Fuaran.Core.Ops (the
//  skeleton-op algebra arbitration runs on) only, Fable-clean.
// ============================================================================

/// A named read-only projection over the domain artifact — rendered as a
/// canonical wire `JVal` so a host serves the answer without a serializer
/// dependency. Idempotent and side-effect-free by contract (`aiSurfaceLaws`
/// samples it); the projection body is domain-supplied — typically a compact
/// `Fuaran.Core.Projection` scoped read (Phase 58) or a domain AiTools
/// projection (outline / index / unbound-reference queries).
type ReadTool<'State> =
    { Name: string
      Description: string
      Run: 'State -> JVal }

/// One mutation-op kind in the emission catalogue: the stable kind tag (the
/// `"kind"` the wire codec emits), a description, and the op's parameter
/// schema — a `JVal` the domain typically produces via the `Function.toSchema`
/// / `toJsonSchema` discipline (Phase 04), so an orchestrator emits the op
/// from the schema without touching the wire encoder.
type OpKindCard =
    { Kind: string
      Description: string
      Schema: JVal }

/// A canonical edit intent — the fast-path resolver's input: the natural-
/// language text plus typed key→value args. A pure value (no clock, no rng),
/// so resolution is a function of the intent alone.
type Intent =
    { Text: string
      Args: (string * string) list }

/// One pattern-bank entry: a stable name, the prompt anchors the resolver
/// matches against (literal segments; a `{...}` span is a wildcard), and the
/// domain's typed emission — args in, canonical op list out. The emission body
/// (the pattern *content*) stays domain-side; the core owns only matching and
/// resolution discipline. `Emit` must be deterministic (`aiSurfaceLaws` checks).
type PatternCard<'Op> =
    { Name: string
      Title: string
      PromptAnchors: string list
      Emit: (string * string) list -> Result<'Op list, string> }

/// The policy decision for one op by one actor — the shape of the domain's
/// write policy (the rules stay domain-side). Default-deny lives in the domain;
/// the core only routes the three outcomes.
type PolicyDecision =
    | Allow
    | NeedsApproval
    | Deny of reason: string

/// Agent-readable rejection guidance (the envelope discipline, GP5): what went
/// wrong plus the enumerated alternatives, so a refused agent can repair its
/// emission instead of guessing.
type RejectionGuidance =
    { Message: string
      Alternatives: string list }

/// The per-domain AI-surface seam — one witness bundling the four parts. The
/// core sees the artifact (`'State`), the op (`'Op`), and the rejection
/// (`'Rej`) only through these accessors; no base type, no default content.
///   - `ReadTools`  — the introspection projections.
///   - `OpKinds` / `KindOfOp` — the mutation catalogue + the op→kind projection
///     the completeness law joins them on.
///   - `Patterns`   — the pattern-bank entries (intent anchors → op generators).
///   - `Decide` / `Apply` / `Explain` — the policy/validator hooks for
///     proposals: actor→op decision, the domain reducer, and the rejection →
///     guidance rendering (typically built on the domain's `Fuaran.Core.Validator`
///     defects / policy envelope).
type AiSurfaceWitness<'State, 'Op, 'Rej> =
    { ReadTools: ReadTool<'State> list
      OpKinds: OpKindCard list
      KindOfOp: 'Op -> string
      Patterns: PatternCard<'Op> list
      Decide: string -> 'Op -> PolicyDecision
      Apply: 'Op -> 'State -> Result<'State, 'Rej>
      Explain: 'Rej -> RejectionGuidance }

/// The NL→op fast-path: deterministic anchor matching over the witness's
/// pattern bank. The resolver is a pure function of the intent — no clock, no
/// rng — so the same intent always resolves to the same emission (the token
/// lever: a matched intent skips the model entirely).
module PatternBank =

    /// The literal segments of an anchor: a `{...}` span is a wildcard, so
    /// `"look up {key}"` yields `[ "look up " ]`. An unterminated `{` treats
    /// the rest as consumed (defensive — anchors are domain-authored).
    let private literalSegments (anchor: string) : string list =
        let rec go (rest: string) (acc: string list) =
            match rest.IndexOf '{' with
            | -1 -> List.rev (if rest = "" then acc else rest :: acc)
            | opens ->
                let before = rest.Substring(0, opens)
                let acc' = if before = "" then acc else before :: acc

                match rest.IndexOf('}', opens) with
                | -1 -> List.rev acc'
                | closes -> go (rest.Substring(closes + 1)) acc'

        go anchor []

    /// Case-insensitive anchor match: every literal segment appears in the
    /// intent text, in order (wildcard spans match anything, including "").
    let matchesAnchor (anchor: string) (text: string) : bool =
        let lower (s: string) = s.ToLowerInvariant()
        let t = lower text

        let rec go (fromIdx: int) =
            function
            | [] -> true
            | (seg: string) :: rest ->
                match t.IndexOf(lower seg, fromIdx) with
                | -1 -> false
                | at -> go (at + seg.Length) rest

        go 0 (literalSegments anchor)

    /// The first pattern (bank order) with a matching anchor — bank order is
    /// part of the resolution contract, so a domain lists its most specific
    /// patterns first.
    let tryMatch (w: AiSurfaceWitness<'State, 'Op, 'Rej>) (intent: Intent) : PatternCard<'Op> option =
        w.Patterns
        |> List.tryFind (fun p -> p.PromptAnchors |> List.exists (fun a -> matchesAnchor a intent.Text))

    /// The fast-path resolver: `None` when no pattern matches (fall through to
    /// the model); `Some (Ok ops)` when a matched pattern emits; `Some (Error e)`
    /// when it matched but the args were unusable (the caller surfaces the
    /// emission failure, it does not fall through — a matched intent is the
    /// pattern's to answer). Deterministic: a pure function of the intent and
    /// the (fixed) bank.
    let resolve (w: AiSurfaceWitness<'State, 'Op, 'Rej>) (intent: Intent) : Result<'Op list, string> option =
        tryMatch w intent |> Option.map (fun p -> p.Emit intent.Args)

/// The proposal lifecycle over the witness's policy hooks: submit → apply |
/// park | deny; approval applies through the domain reducer with dual
/// attribution (proposer stays the author, approver is recorded); rejection is
/// recorded with a reason, never silently dropped. Calc's `Proposals`
/// generalised — the queue is a pure value the host owns and persists (GP2).
module Proposals =

    type ProposalStatus =
        | Pending
        | Approved of approver: string * at: string
        | Rejected of approver: string * at: string * reason: string

    /// A parked op sequence. `ProposedAt` is ISO-8601, caller-supplied (no
    /// clock reads in the core).
    type Proposal<'Op> =
        { Id: int
          Author: string
          ProposedAt: string
          Intent: string option
          Ops: 'Op list
          Status: ProposalStatus }

    type ProposalQueue<'Op> = { Proposals: Proposal<'Op> list }

    module Queue =

        let empty<'Op> : ProposalQueue<'Op> = { Proposals = [] }

        let pending (q: ProposalQueue<'Op>) : Proposal<'Op> list =
            q.Proposals |> List.filter (fun p -> p.Status = Pending)

    /// Park an op sequence for approval. Returns the queue and the assigned
    /// (1-based, queue-positional) id.
    let propose
        (author: string)
        (proposedAt: string)
        (intent: string option)
        (ops: 'Op list)
        (q: ProposalQueue<'Op>)
        : ProposalQueue<'Op> * int =
        let id = q.Proposals.Length + 1

        { Proposals =
            q.Proposals
            @ [ { Id = id
                  Author = author
                  ProposedAt = proposedAt
                  Intent = intent
                  Ops = ops
                  Status = Pending } ] },
        id

    /// Why an approval/rejection was refused — total, and it enumerates the
    /// pending ids where a closed set is expected (GP5).
    type ApprovalFailure<'Rej> =
        | UnknownProposal of id: int * pending: int list
        | NotPending of id: int * status: ProposalStatus
        /// The artifact moved since the proposal was parked and an op no longer
        /// applies — the rejection is surfaced and the proposal STAYS pending:
        /// repair-or-reject is the approver's call, never an automatic drop.
        | OpNoLongerApplies of id: int * rejection: 'Rej

    let private find (id: int) (q: ProposalQueue<'Op>) : Result<Proposal<'Op>, ApprovalFailure<'Rej>> =
        match q.Proposals |> List.tryFind (fun p -> p.Id = id) with
        | None -> Error(UnknownProposal(id, Queue.pending q |> List.map (fun p -> p.Id)))
        | Some p ->
            match p.Status with
            | Pending -> Ok p
            | status -> Error(NotPending(id, status))

    let private setStatus (id: int) (status: ProposalStatus) (q: ProposalQueue<'Op>) : ProposalQueue<'Op> =
        { Proposals =
            q.Proposals
            |> List.map (fun p -> if p.Id = id then { p with Status = status } else p) }

    /// Fold the proposal's ops through the domain reducer — the first rejection
    /// aborts (the state is never partially returned).
    let private applyAll
        (w: AiSurfaceWitness<'State, 'Op, 'Rej>)
        (ops: 'Op list)
        (state: 'State)
        : Result<'State, 'Rej> =
        ops |> List.fold (fun acc op -> acc |> Result.bind (w.Apply op)) (Ok state)

    /// Approve a pending proposal: its ops apply through the domain reducer. On
    /// success the proposal is marked approved (dual attribution — the proposer
    /// authored the ops, the approver signed off); if an op no longer applies
    /// the rejection is surfaced and the proposal stays pending.
    let approve
        (w: AiSurfaceWitness<'State, 'Op, 'Rej>)
        (approver: string)
        (at: string)
        (id: int)
        (q: ProposalQueue<'Op>)
        (state: 'State)
        : Result<ProposalQueue<'Op> * 'State, ApprovalFailure<'Rej>> =
        find id q
        |> Result.bind (fun p ->
            match applyAll w p.Ops state with
            | Error rejection -> Error(OpNoLongerApplies(id, rejection))
            | Ok next -> Ok(setStatus id (Approved(approver, at)) q, next))

    /// Reject a pending proposal with a reason. Recorded, never dropped — and
    /// the artifact is never touched (a denied proposal never mutates).
    let reject
        (approver: string)
        (at: string)
        (reason: string)
        (id: int)
        (q: ProposalQueue<'Op>)
        : Result<ProposalQueue<'Op>, ApprovalFailure<'Rej>> =
        find id q
        |> Result.map (fun _ -> setStatus id (Rejected(approver, at, reason)) q)

    /// Outcome of submitting an op sequence through the policy gate.
    type SubmitOutcome<'State, 'Op, 'Rej> =
        /// Policy allowed every op; they applied.
        | SubmitApplied of 'State
        /// Policy parked the sequence; the proposal id is in the queue.
        | SubmitProposed of ProposalQueue<'Op> * proposalId: int
        /// Policy refused an op.
        | SubmitDenied of reason: string
        /// Policy allowed it but the domain reducer rejected an op.
        | SubmitOpRejected of 'Rej

    /// The composed gated co-authoring loop — the single entry point an
    /// agent-facing surface calls: decide every op, then apply | park | deny
    /// the sequence as a unit. A `Deny` anywhere refuses the whole sequence
    /// (first reason wins); otherwise any `NeedsApproval` parks it whole (an
    /// approval decision covers what the agent proposed, not a fragment);
    /// otherwise the ops apply in order.
    let submit
        (w: AiSurfaceWitness<'State, 'Op, 'Rej>)
        (author: string)
        (at: string)
        (intent: string option)
        (ops: 'Op list)
        (q: ProposalQueue<'Op>)
        (state: 'State)
        : SubmitOutcome<'State, 'Op, 'Rej> =
        let decisions = ops |> List.map (w.Decide author)

        match
            decisions
            |> List.tryPick (function
                | Deny reason -> Some reason
                | _ -> None)
        with
        | Some reason -> SubmitDenied reason
        | None ->
            if decisions |> List.exists ((=) NeedsApproval) then
                let q', id = propose author at intent ops q
                SubmitProposed(q', id)
            else
                match applyAll w ops state with
                | Ok next -> SubmitApplied next
                | Error rejection -> SubmitOpRejected rejection

    /// Render guidance as agent-readable text: the message plus the enumerated
    /// alternatives (one per line), so a refused agent repairs instead of
    /// guessing. No alternatives ⇒ the message alone (the core invents none).
    let renderGuidance (g: RejectionGuidance) : string =
        match g.Alternatives with
        | [] -> g.Message
        | alts ->
            g.Message
            + "\nAlternatives:\n"
            + (alts |> List.map (fun a -> "- " + a) |> String.concat "\n")

    /// A rejection envelope → agent-readable guidance, through the witness's
    /// `Explain` hook (typically the domain's policy / validator envelope) —
    /// Fork-2 hygiene: the refusal names what was expected and enumerates the
    /// alternatives.
    let explainRejection (w: AiSurfaceWitness<'State, 'Op, 'Rej>) (rejection: 'Rej) : string =
        renderGuidance (w.Explain rejection)

// ---- proposal arbitration (Phase 85) ---------------------------------------

/// Why arbitration rejected one proposal — the AI-feedback protocol shape (GP5):
/// a rejected agent knows exactly what to repair or rebase against.
type ArbitrationRejection<'Id> =
    /// The proposal's script does not apply to the base tree: the op-algebra's
    /// own rejection envelope, plus the index of the failing op in the script.
    | Inapplicable of opIndex: int * rejection: Rejection<'Id>
    /// The script applies, but its footprint (Phase 78) interferes with the
    /// accepted set — the interfering accepted proposals' ids (computed against
    /// the FULL accepted set, in pinned order): exactly what to rebase against
    /// once they land. Non-empty by construction.
    | Conflicts of interfering: int list

/// The result of arbitrating N op-script proposals against one base tree
/// (Phase 85) — a deterministic, TOTAL partition. `Accepted` is mutually
/// independent (pairwise `Ops.independent`), listed in the pinned order
/// (ascending proposal id); by footprint soundness its scripts apply
/// confluently in ANY order. `MergedScript` is the accepted scripts composed
/// in the pinned order — one canonical serialisation of the accepted set.
/// `Rejected` pairs every non-accepted proposal with its typed reason (GP5);
/// nothing is ever silently dropped.
type Arbitration<'Node, 'Id> =
    { Accepted: Proposals.Proposal<SkeletonOp<'Node, 'Id>> list
      MergedScript: SkeletonOp<'Node, 'Id> list
      Rejected: (Proposals.Proposal<SkeletonOp<'Node, 'Id>> * ArbitrationRejection<'Id>) list }

/// The surface descriptor + read-tool dispatch — generic over the witness —
/// and proposal arbitration (Phase 85) over the skeleton-op algebra.
module AiSurface =

    /// One read tool's catalogue entry.
    let private toolJson (t: ReadTool<'State>) : JVal =
        JObj
            [ "name", JStr t.Name
              "description", JStr t.Description
              "readOnly", JBool true ]

    /// One op kind's catalogue entry — the schema travels verbatim.
    let private opJson (o: OpKindCard) : JVal =
        JObj [ "kind", JStr o.Kind; "description", JStr o.Description; "schema", o.Schema ]

    /// One pattern's catalogue entry — name, title, and the anchors an
    /// orchestrator may pre-match client-side.
    let private patternJson (p: PatternCard<'Op>) : JVal =
        JObj
            [ "name", JStr p.Name
              "title", JStr p.Title
              "promptAnchors", JArr(p.PromptAnchors |> List.map JStr) ]

    /// The surface descriptor: the read tools, the mutation-op catalogue (kind +
    /// schema each), and the pattern bank's cards — everything a host serves so
    /// an orchestrator enumerates the surface without an SDK. Deterministic for
    /// a fixed witness; entries travel in witness order.
    let catalogue (w: AiSurfaceWitness<'State, 'Op, 'Rej>) : JVal =
        Json.kindObj
            "aiSurface"
            [ "readTools", JArr(w.ReadTools |> List.map toolJson)
              "ops", JArr(w.OpKinds |> List.map opJson)
              "patterns", JArr(w.Patterns |> List.map patternJson) ]

    /// `catalogue` rendered to canonical wire JSON — the Pres `catalogueJson`
    /// generalised: a host serves the descriptor with no serializer dependency.
    let catalogueJson (w: AiSurfaceWitness<'State, 'Op, 'Rej>) : string = Json.render (catalogue w)

    /// Run a read tool by name — default-deny by shape: an unknown name is a
    /// named error enumerating the available tools (GP5), never a silent miss.
    let runTool (w: AiSurfaceWitness<'State, 'Op, 'Rej>) (name: string) (state: 'State) : Result<JVal, string> =
        match w.ReadTools |> List.tryFind (fun t -> t.Name = name) with
        | Some t -> Ok(t.Run state)
        | None ->
            let known = w.ReadTools |> List.map (fun t -> t.Name) |> String.concat ", "
            Error("unknown read tool '" + name + "'; available: " + known)

    /// Arbitrate N op-script proposals (Phase 59) against one base tree
    /// (Phase 85) — decide which subset can land together. A deterministic,
    /// total partition (GP4: analysis only — the base is never mutated, and no
    /// input throws):
    ///
    ///   1. **Pin the order.** Proposals are processed in ascending `Id`, so
    ///      the outcome is invariant under permutation of the input list. Ids
    ///      are queue-assigned and unique (the Phase 59 `Proposals.propose`
    ///      discipline); `arbitrate` stays total on duplicate-id input (the
    ///      stable sort breaks the tie by input order), but the permutation-
    ///      invariance guarantee assumes unique ids.
    ///   2. **Batch `canApply`.** `Ops.canApplyAll` against the base filters
    ///      the inapplicable — each rejection carries the op-algebra's own
    ///      envelope + the failing op index (GP5).
    ///   3. **Greedy independence.** Each remaining proposal is accepted iff
    ///      its footprint (Phase 78, `Ops.footprint`) is `Ops.independent` of
    ///      everything already accepted; otherwise it is rejected with
    ///      `Conflicts`, citing the interfering accepted ids (recomputed
    ///      against the FULL accepted set, so the citation is the complete
    ///      rebase target, not just the first collision).
    ///
    /// The accepted set is pairwise independent, so (footprint soundness,
    /// `Conformance.footprintLaws`) its scripts apply confluently in any
    /// order; `MergedScript` is one such order (the pinned one).
    ///
    /// **Honesty boundary (the Phase 52 discipline):** greedy-in-pinned-order
    /// yields *a maximal* mutually-independent set — nothing rejected could be
    /// added without a conflict — not *the maximum* one; a different order
    /// could accept more. The order is pinned, documented, deterministic. And
    /// independence is conservative (Phase 78): a "maybe" is a conflict, so
    /// `Conflicts` means "not PROVABLY coexistent", never "wrong". No ranking
    /// policy, no quality judgement, no evaluator (GP6): which proposal is
    /// *better* is the host's business; Core only says which ones *can
    /// coexist*.
    let arbitrate
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (baseTree: 'Node)
        (proposals: Proposals.Proposal<SkeletonOp<'Node, 'Id>> list)
        : Arbitration<'Node, 'Id> =
        // the pinned deterministic order — ascending proposal id.
        let pinned = proposals |> List.sortBy (fun p -> p.Id)

        // greedy pass: accepted accumulates (proposal, footprint) in reverse pinned
        // order; a conflict at decision time is provisional (re-cited below).
        let step (accepted, rejected) (p: Proposals.Proposal<SkeletonOp<'Node, 'Id>>) =
            match Ops.canApplyAll nodew idw p.Ops baseTree with
            | Error(i, rej) -> accepted, (p, Inapplicable(i, rej)) :: rejected
            | Ok() ->
                let fp = Ops.footprint nodew idw p.Ops

                if accepted |> List.forall (fun (_, afp) -> Ops.independent fp afp) then
                    (p, fp) :: accepted, rejected
                else
                    accepted, (p, Conflicts []) :: rejected

        let acceptedRev, rejectedRev = pinned |> List.fold step ([], [])
        let accepted = List.rev acceptedRev

        // Re-cite every conflict against the FULL accepted set (a later-accepted
        // proposal may also interfere) — the complete rebase target, pinned order.
        // Non-empty by construction: the interferer seen at decision time was
        // accepted before the conflict and stays accepted.
        let rejected =
            List.rev rejectedRev
            |> List.map (fun (p, reason) ->
                match reason with
                | Inapplicable _ -> p, reason
                | Conflicts _ ->
                    let fp = Ops.footprint nodew idw p.Ops

                    let interfering =
                        accepted
                        |> List.choose (fun (a, afp) -> if Ops.independent fp afp then None else Some a.Id)

                    p, Conflicts interfering)

        let acceptedProposals = accepted |> List.map fst

        { Accepted = acceptedProposals
          MergedScript = acceptedProposals |> List.collect (fun p -> p.Ops)
          Rejected = rejected }
