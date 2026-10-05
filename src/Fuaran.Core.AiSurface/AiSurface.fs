namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.AiSurface — ONE seam an orchestrator drives a witness domain
//  through. It is the answer to a single question: what does a model need in
//  order to read a domain artifact and propose changes to it, stated once
//  instead of once per domain? Four parts, parameterised over a domain witness
//  so the core carries the *shape* and never the content (GP6):
//
//    1. Read tools        — the introspection projections (outline / index /
//                           unbound-reference queries) as a witness-supplied set,
//                           each rendering to canonical wire `JVal`.
//    2. Op catalogue      — the domain's mutation-op catalogue as JSON (the
//                           `Fuaran.Core.Function` `toSchema` discipline), so an
//                           orchestrator emits ops from a schema without touching
//                           the wire encoder.
//    3. Pattern bank      — canonical edit-intents → op sequences, with a
//                           deterministic fast-path resolver so the common edits
//                           skip the model.
//    4. Proposals         — a human-in-loop approval gate re-using the domain's
//                           policy, with agent-readable rejection guidance that
//                           enumerates the alternatives.
//
//  Those four are what an AI surface IS, and `AiSurfaceWitness` is frozen over
//  them (STABILITY's witness-record field freeze).
//
//  NOT here, and the omission is the point: deciding which of N op scripts can
//  land together. That is `Arbitration.arbitrate` in `Fuaran.Core.Ops` — the
//  other end of `Ops.footprint` / `Ops.independent`, the concurrency half of
//  the tree algebra, whose callers are schedulers rather than orchestrators. It
//  sat here until Phase 192 because the proposal type did, not because it
//  belonged. `Proposals.toOpScript` is the one line between the two: a domain
//  that arbitrates its pending queue projects through it.
//
//  All read-tool logic, pattern *content*, and policy *rules* stay domain-side;
//  the witness supplies them per call (GP1/GP2 — additive, no base type, no
//  module-level state). FSharp.Core + Fuaran.Core.Wire + Fuaran.Core.Ops (whose
//  `SkeletonOp` the proposal queue carries) only, Fable-clean.
// ============================================================================

/// A named read-only projection over the domain artifact — rendered as a
/// canonical wire `JVal` so a host serves the answer without a serializer
/// dependency. Idempotent and side-effect-free by contract (`Conformance.aiSurfaceLawsAt`
/// samples it); the projection body is domain-supplied — typically a compact
/// `Fuaran.Core.Projection` scoped read (Phase 58) or a domain AiTools
/// projection (outline / index / unbound-reference queries).
type ReadTool<'State> =
    {
        /// The key `AiSurface.runTool` dispatches on, compared exactly (case-sensitive); where two
        /// tools share a name the first in `ReadTools` wins.
        Name: string
        /// Prose the catalogue serves beside the name for a model choosing a tool; never parsed.
        Description: string
        /// The projection. Must be total and deterministic over a state (the AI-surface laws check
        /// both); `runTool` returns its `JVal` verbatim.
        Run: 'State -> JVal
    }

/// One mutation-op kind in the emission catalogue: the stable kind tag (the
/// `"kind"` the wire codec emits), a description, and the op's parameter
/// schema — a `JVal` the domain typically produces via the `Function.toSchema`
/// / `toJsonSchema` discipline (Phase 04), so an orchestrator emits the op
/// from the schema without touching the wire encoder.
type OpKindCard =
    {
        /// The tag `KindOfOp` returns for ops of this kind. Catalogue completeness joins on it: every
        /// kind an op can project to must be catalogued, and every catalogued kind must be emittable.
        Kind: string
        /// Prose the catalogue serves under `description`; never parsed.
        Description: string
        /// The op's parameter schema, served verbatim under `schema`; the core does not validate it.
        Schema: JVal
    }

/// A canonical edit intent — the fast-path resolver's input: the natural-
/// language text plus typed key→value args. A pure value (no clock, no rng),
/// so resolution is a function of the intent alone.
type Intent =
    {
        /// The request the anchors are matched against — case-insensitively and ordinally, each
        /// anchor's literal segments in order (`PatternBank.matchesAnchor`).
        Text: string
        /// Handed unchanged to the matched pattern's `Emit`; matching reads only `Text`, never these.
        Args: (string * string) list
    }

/// One pattern-bank entry: a stable name, the prompt anchors the resolver
/// matches against (literal segments; a `{...}` span is a wildcard), and the
/// domain's typed emission — args in, canonical op list out. The emission body
/// (the pattern *content*) stays domain-side; the core owns only matching and
/// resolution discipline. `Emit` must be deterministic (`Conformance.aiSurfaceLawsAt` checks).
type PatternCard<'Op> =
    {
        /// The pattern's stable identifier, served in the catalogue; resolution never reads it.
        Name: string
        /// A human-readable label served in the catalogue beside `Name`; resolution never reads it.
        Title: string
        /// Any ONE matching anchor selects the pattern. An empty list never matches, and an anchor
        /// with no literal segment is refused by the AI-surface laws because it would match everything.
        PromptAnchors: string list
        /// The emission from the intent's `Args`. An `Error` means the args were unusable, and
        /// `PatternBank.resolve` surfaces it as `Some (Error _)` rather than falling through to the model.
        Emit: (string * string) list -> Result<'Op list, string>
    }

// `PolicyDecision` — the policy decision for one op by one actor, whose `Deny` carries the
// `RejectionGuidance` a refused agent repairs from — is declared in `Fuaran.Core.Function` since
// Phase 318, beside the invocable registries' gate and with the join (`PolicyDecision.join` /
// `all` / `any`) a domain combines policies through. Same namespace, same cases, same
// constructors: a source that names it here still compiles unchanged. The witness's `Decide` is an
// instance of the registries' gate shape — a decision for an actor and an action — and `submit`
// combines a sequence's decisions with `PolicyDecision.all`.

// `RejectionGuidance` — the agent-readable guidance `Explain` returns — is declared in
// `Fuaran.Core.Ops` since Phase 315, beside `Rejection.explain`, which builds it. Same namespace,
// same fields: a source that names it here still compiles unchanged.

/// The canonical wire encoding of an `Ops` `Rejection` (Phase 315) — the envelope as a `$type`-tagged
/// canonical JSON object, so a host that forwards a refusal to an agent or a log writes the one
/// spelling every host reads, rather than its own. Every member of the case is carried (the
/// `addressable` ids of an `UnknownNode`, both orders of a `ReorderMismatch`), and the `$type` is
/// the case's: `Rejection.code`'s word, except that a reorder on a leaf stays `reorderMismatch` here
/// (its empty `expected` says so) and a domain's `Rejected` is `rejected` with its own `code` as a
/// member. Lives here rather than in `Fuaran.Core.Ops` because this package carries the wire.
[<RequireQualifiedAccess>]
module RejectionCodec =

    /// The rejection as a canonical wire value; `idText` renders an id.
    let encode (idText: 'Id -> string) (r: Rejection<'Id>) : JVal =
        let one (i: 'Id) = JStr(idText i)
        let many (xs: 'Id list) = JArr(xs |> List.map one)

        match r with
        | Rejection.UnknownNode(target, addressable) ->
            Canon.typed "unknownNode" [ "target", one target; "addressable", many addressable ]
        | DuplicateId d -> Canon.typed "duplicateId" [ "id", one d ]
        | CannotRemoveRoot -> Canon.typed "cannotRemoveRoot" []
        | WouldNestUnderSelf(target, relation) ->
            let rel =
                match relation with
                | NestRelation.Self -> "self"
                | NestRelation.Descendant -> "descendant"

            Canon.typed "wouldNestUnderSelf" [ "target", one target; "relation", JStr rel ]
        | NotAContainer(target, kindTag) ->
            Canon.typed "notAContainer" [ "target", one target; "kindTag", JStr kindTag ]
        | ReorderMismatch(parent, expected, got) ->
            Canon.typed "reorderMismatch" [ "parent", one parent; "expected", many expected; "got", many got ]
        | Rejected(code, message) -> Canon.typed "rejected" [ "code", JStr code; "message", JStr message ]
        | KeyedPosition(target, holder) -> Canon.typed "keyedPosition" [ "target", one target; "holder", one holder ]
        | IllegalChild(child, childKind, parent, parentKind, legal) ->
            Canon.typed
                "illegalChild"
                [ "child", one child
                  "childKind", JStr childKind
                  "parent", one parent
                  "parentKind", JStr parentKind
                  "legal", JArr(legal |> List.map JStr) ]
        | StillReferenced(target, referrers) ->
            Canon.typed "stillReferenced" [ "target", one target; "referrers", many referrers ]

    /// `encode` rendered canonically (`Canon.render`: sorted keys, the pinned escaping). A caller that
    /// must refuse an ill-formed id rather than write it through renders with `Canon.tryRender`.
    let render (idText: 'Id -> string) (r: Rejection<'Id>) : string = Canon.render (encode idText r)

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
    {
        /// The tools `AiSurface.runTool` dispatches over and the catalogue lists, in this order.
        ReadTools: ReadTool<'State> list
        /// The mutation catalogue, served under `ops`; it must hold exactly the kinds `KindOfOp`
        /// can return.
        OpKinds: OpKindCard list
        /// An op's catalogue `Kind` — the join the completeness law checks. Must be total.
        KindOfOp: 'Op -> string
        /// The pattern bank in resolution order: `PatternBank.resolve` takes the first pattern with a
        /// matching anchor, so the most specific patterns come first.
        Patterns: PatternCard<'Op> list
        /// The write policy for an actor and an op. `Proposals.submit` consults it for the author,
        /// and `Proposals.approve` consults it again for the approver, refusing on any `Deny`.
        Decide: string -> 'Op -> PolicyDecision
        /// The domain reducer. A proposal's ops fold through it in order, and the first `Error`
        /// aborts the whole sequence — a partially applied state is never returned.
        Apply: 'Op -> 'State -> Result<'State, 'Rej>
        /// A reducer rejection as the guidance `Proposals.explainRejection` renders; the AI-surface
        /// laws require the rendering to be non-empty.
        Explain: 'Rej -> RejectionGuidance
    }

/// The AI surface with what its proposals do to the WORLD declared (Phase 318) — a composing witness that EMBEDS the frozen `AiSurfaceWitness` and adds two accessors,
/// the evolution path STABILITY's witness-record freeze prescribes ("compose, never grow"). A domain
/// that builds none is untouched; one that builds it gets `Proposals.submitGuarded` /
/// `approveGuarded`, which consult the capability registry's gate for every capability an op
/// invokes and dry-run a whole sequence before the first `Apply`, and `Conformance.policyLawsAt`,
/// which certifies the domain's own policy never allows an unapproved write. Frozen at birth.
type GuardedSurfaceWitness<'State, 'Op, 'Rej> =
    {
        /// The surface this guards: its `Decide`, `Apply` and `Explain` are the ones run.
        Surface: AiSurfaceWitness<'State, 'Op, 'Rej>
        /// An effect-free check of one op against a state: the state the op WOULD produce, or the
        /// rejection `Apply` would give. Must run no capability body and perform no effect — it is
        /// what lets a sequence be refused before any op's effects run. Must agree with `Apply` on
        /// which ops apply (`Conformance.policyLawsAt` samples it).
        DryRun: 'Op -> 'State -> Result<'State, 'Rej>
        /// The capability invocations an op makes when it applies — `(capability id, arguments)`,
        /// in the order it makes them. An op that invokes nothing answers `[]`. Total.
        EffectsOf: 'Op -> (string * (string * string) list) list
    }

/// The NL→op fast-path: deterministic anchor matching over the witness's
/// pattern bank. The resolver is a pure function of the intent — no clock, no
/// rng — so the same intent always resolves to the same emission (the token
/// lever: a matched intent skips the model entirely).
module PatternBank =

    /// The literal segments of an anchor: a `{...}` span is a wildcard, so
    /// `"look up {key}"` yields `[ "look up " ]`. An unterminated `{` treats
    /// the rest as consumed (defensive — anchors are domain-authored). An anchor
    /// with NO literal segment matches every intent (`Conformance.aiSurfaceLawsAt` refuses one,
    /// Phase 298).
    let literalSegments (anchor: string) : string list =
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

    /// The first index at or after `from` where `seg` occurs in `text`, compared
    /// ORDINALLY — char for char, no culture, no ignorable characters — or -1. A
    /// loop rather than a platform `IndexOf` overload, so .NET and Fable answer
    /// alike by construction (Phase 298).
    let private ordinalIndexOf (text: string) (seg: string) (from: int) : int =
        if seg.Length = 0 then
            (if from <= text.Length then from else -1)
        else
            let last = text.Length - seg.Length
            let mutable i = from
            let mutable found = -1

            while found < 0 && i <= last do
                let mutable j = 0

                while j < seg.Length && text[i + j] = seg[j] do
                    j <- j + 1

                if j = seg.Length then found <- i else i <- i + 1

            found

    /// Case-insensitive anchor match: every literal segment appears in the
    /// intent text, in order (wildcard spans match anything, including "").
    ///
    /// **Ordinal over lowered copies (Phase 298).** Both sides are lowered
    /// (`ToLowerInvariant`) and compared char for char, and the cursor advances by
    /// the LOWERED segment's length — the length of what actually matched. The
    /// culture-sensitive `IndexOf` this replaces matched a zero-width or soft-hyphen
    /// segment everywhere (so such an anchor swallowed the bank), answered
    /// differently on .NET and Fable, and could match fewer chars than the segment
    /// held, so advancing by the segment's own length overran the text and threw.
    let matchesAnchor (anchor: string) (text: string) : bool =
        let t = text.ToLowerInvariant()

        let rec go (fromIdx: int) =
            function
            | [] -> true
            | (seg: string) :: rest ->
                let s = seg.ToLowerInvariant()

                match ordinalIndexOf t s fromIdx with
                | -1 -> false
                | at -> go (at + s.Length) rest

        go 0 (literalSegments anchor)

    /// The first pattern (bank order) with a matching anchor — bank order is
    /// part of the resolution contract, so a domain lists its most specific
    /// patterns first.
    let internal tryMatch (w: AiSurfaceWitness<'State, 'Op, 'Rej>) (intent: Intent) : PatternCard<'Op> option =
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

    /// Where a proposal stands. `RequireQualifiedAccess` (Phase 298): `Pending` and
    /// `Rejected` were also cases of `Function`'s `Deferred` and of `Ops`' `Rejection`,
    /// so a consumer opening the spine met one name for two cases — write
    /// `ProposalStatus.Pending`.
    [<RequireQualifiedAccess>]
    type ProposalStatus =
        /// Awaiting a decision — the only status `approve` and `reject` act on.
        | Pending
        /// The ops applied through the reducer; `approver` signed off at `at` (caller-supplied
        /// text), while the proposal's `Author` stays the proposer.
        | Approved of approver: string * at: string
        /// Refused by `approver` at `at` for `reason`. Recorded rather than dropped, and the
        /// artifact was never touched.
        | Rejected of approver: string * at: string * reason: string

    /// A parked op sequence. `ProposedAt` is ISO-8601, caller-supplied (no
    /// clock reads in the core).
    type Proposal<'Op> =
        {
            /// Unique within its queue: `proposeWithId` refuses a held id, and `propose` mints one
            /// past the largest held, so an id is never re-issued.
            Id: int
            /// The proposing actor. It can never approve its own proposal (`SelfApproval`).
            Author: string
            /// Carried verbatim; the core never parses it or orders proposals by it.
            ProposedAt: string
            /// The edit intent's text the ops answer, when there was one — kept for the approver,
            /// never read by the lifecycle.
            Intent: string option
            /// Applied in order and as a unit on approval: the first reducer rejection leaves the
            /// proposal pending and the state untouched.
            Ops: 'Op list
            /// Where the proposal stands; only a `Pending` one can be decided.
            Status: ProposalStatus
        }

    /// The proposal queue — a pure value the host owns and persists. Decided proposals stay in it
    /// with their status until the host prunes them.
    type ProposalQueue<'Op> =
        {
            /// Every proposal in the order it was parked, decided ones included; no two share an id.
            Proposals: Proposal<'Op> list
        }

    /// Reads over a `ProposalQueue`: the empty queue, the undecided view, and the next id.
    module Queue =

        /// A queue holding nothing; the first `propose` on it mints id `1`.
        let empty<'Op> : ProposalQueue<'Op> = { Proposals = [] }

        /// The proposals still awaiting a decision, in the order they were parked — the ids an
        /// `UnknownProposal` refusal enumerates.
        let pending (q: ProposalQueue<'Op>) : Proposal<'Op> list =
            q.Proposals |> List.filter (fun p -> p.Status = ProposalStatus.Pending)

        /// The id the next `propose` mints (Phase 298): one past the largest id the
        /// queue holds, `1` for an empty queue. For a queue that only ever grew this
        /// is the `Length + 1` it used to be; for one a host has pruned of decided
        /// proposals it is still fresh, where `Length + 1` re-issued a live id.
        let nextId (q: ProposalQueue<'Op>) : int =
            match q.Proposals with
            | [] -> 1
            | ps -> (ps |> List.map (fun p -> p.Id) |> List.max) + 1

    /// Why a proposal could not be parked under a caller-chosen id (Phase 298): the
    /// queue already holds that id; `held` enumerates the ids it holds.
    [<RequireQualifiedAccess>]
    type ProposeFailure =
        /// `id` is already held; `held` lists every id in the queue, in queue order, decided
        /// proposals included.
        | DuplicateProposal of id: int * held: int list

    /// One pending proposal appended under `id` — unchecked; both proposers check first.
    let private append id author proposedAt intent (ops: 'Op list) (q: ProposalQueue<'Op>) : ProposalQueue<'Op> =
        { Proposals =
            q.Proposals
            @ [ { Id = id
                  Author = author
                  ProposedAt = proposedAt
                  Intent = intent
                  Ops = ops
                  Status = ProposalStatus.Pending } ] }

    /// Park an op sequence for approval under the id `id` (Phase 298) — the form
    /// for a host that mints ids from its own source (a sequence it persists, a
    /// content hash of the proposal). REFUSED when the queue already holds `id`:
    /// two proposals under one id would make every later decision ambiguous.
    let proposeWithId
        (id: int)
        (author: string)
        (proposedAt: string)
        (intent: string option)
        (ops: 'Op list)
        (q: ProposalQueue<'Op>)
        : Result<ProposalQueue<'Op>, ProposeFailure> =
        if q.Proposals |> List.exists (fun p -> p.Id = id) then
            Error(ProposeFailure.DuplicateProposal(id, q.Proposals |> List.map (fun p -> p.Id)))
        else
            Ok(append id author proposedAt intent ops q)

    /// Park an op sequence for approval. Returns the queue and the assigned id —
    /// `Queue.nextId` (Phase 298: one past the largest held, so an id is never
    /// re-issued even after a host prunes decided proposals). `proposeWithId`
    /// takes the id from the caller instead.
    let propose
        (author: string)
        (proposedAt: string)
        (intent: string option)
        (ops: 'Op list)
        (q: ProposalQueue<'Op>)
        : ProposalQueue<'Op> * int =
        // `nextId` exceeds every id the queue holds, so the duplicate check has nothing to refuse
        let id = Queue.nextId q
        append id author proposedAt intent ops q, id

    /// Why an approval/rejection was refused — total, and it enumerates the
    /// pending ids where a closed set is expected (GP5).
    type ApprovalFailure<'Rej> =
        /// No proposal in the queue carries `id`; `pending` lists the ids still awaiting a
        /// decision (decided ones are not offered).
        | UnknownProposal of id: int * pending: int list
        /// The proposal was already decided — a second decision is refused, and `status` is the
        /// `Approved` or `Rejected` it holds.
        | NotPending of id: int * status: ProposalStatus
        /// The artifact moved since the proposal was parked and an op no longer
        /// applies — the rejection is surfaced and the proposal STAYS pending:
        /// repair-or-reject is the approver's call, never an automatic drop.
        | OpNoLongerApplies of id: int * rejection: 'Rej
        /// The approver is the proposal's author (Phase 298). Approval is the
        /// second pair of eyes; an author signing off their own proposal is no
        /// approval at all. The proposal stays pending.
        | SelfApproval of id: int * author: string
        /// The domain's policy DENIES the approver one of the proposal's ops
        /// (Phase 298): `approve` re-consults `Decide` for the approver, so
        /// parking a sequence and approving it can never apply what the
        /// approver's own policy refuses. The proposal stays pending; the
        /// guidance is the policy's denial. (`NeedsApproval` for the approver is
        /// not a refusal: approving IS that approval.)
        | ApprovalDenied of id: int * guidance: RejectionGuidance

    let private find (id: int) (q: ProposalQueue<'Op>) : Result<Proposal<'Op>, ApprovalFailure<'Rej>> =
        match q.Proposals |> List.tryFind (fun p -> p.Id = id) with
        | None -> Error(UnknownProposal(id, Queue.pending q |> List.map (fun p -> p.Id)))
        | Some p ->
            match p.Status with
            | ProposalStatus.Pending -> Ok p
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

    /// The approver's own policy over the proposal's ops (Phase 298): the guidance
    /// of the first op the policy DENIES `approver`, which `ApprovalDenied` carries.
    let private approverRefusal
        (w: AiSurfaceWitness<'State, 'Op, 'Rej>)
        (approver: string)
        (ops: 'Op list)
        : RejectionGuidance option =
        ops
        |> List.tryPick (fun op ->
            match w.Decide approver op with
            | PolicyDecision.Deny g -> Some g
            | PolicyDecision.Allow
            | PolicyDecision.NeedsApproval -> None)

    /// Approve a pending proposal: its ops apply through the domain reducer. On
    /// success the proposal is marked approved (dual attribution — the proposer
    /// authored the ops, the approver signed off); if an op no longer applies
    /// the rejection is surfaced and the proposal stays pending.
    ///
    /// **Two refusals before the reducer runs (Phase 298), each leaving the
    /// proposal pending:** an approver who is the proposal's author
    /// (`SelfApproval`), and an approver the domain's policy DENIES one of the ops
    /// (`ApprovalDenied`). Without the second, `propose` (public)
    /// then `approve` by the same or any actor applied ops a deny-all policy
    /// refused through `submit` — a policy bypass, not only a missing second pair
    /// of eyes.
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
            if p.Author = approver then
                Error(SelfApproval(id, p.Author))
            else
                match approverRefusal w approver p.Ops with
                | Some g -> Error(ApprovalDenied(id, g))
                | None ->
                    match applyAll w p.Ops state with
                    | Error rejection -> Error(OpNoLongerApplies(id, rejection))
                    | Ok next -> Ok(setStatus id (ProposalStatus.Approved(approver, at)) q, next))

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
        |> Result.map (fun _ -> setStatus id (ProposalStatus.Rejected(approver, at, reason)) q)

    /// Outcome of submitting an op sequence through the policy gate.
    type SubmitOutcome<'State, 'Op, 'Rej> =
        /// Policy allowed every op; they applied.
        | SubmitApplied of 'State
        /// Policy parked the sequence; the proposal id is in the queue.
        | SubmitProposed of ProposalQueue<'Op> * proposalId: int
        /// Policy refused an op: the policy's guidance — its message and the
        /// alternatives it names (Phase 298; a bare reason string before).
        | SubmitDenied of guidance: RejectionGuidance
        /// Policy allowed it but the domain reducer rejected an op.
        | SubmitOpRejected of 'Rej

    /// The composed gated co-authoring loop — the single entry point an
    /// agent-facing surface calls: decide every op, then apply | park | deny
    /// the sequence as a unit. A `Deny` anywhere refuses the whole sequence
    /// (the first denial's guidance wins); otherwise any `NeedsApproval` parks it
    /// whole (an approval decision covers what the agent proposed, not a
    /// fragment); otherwise the ops apply in order.
    let submit
        (w: AiSurfaceWitness<'State, 'Op, 'Rej>)
        (author: string)
        (at: string)
        (intent: string option)
        (ops: 'Op list)
        (q: ProposalQueue<'Op>)
        (state: 'State)
        : SubmitOutcome<'State, 'Op, 'Rej> =
        // Phase 318: the sequence's decision is the JOIN of its ops' decisions — the most
        // restrictive wins, and of several denials the first — which is exactly the rule this
        // function always applied by hand.
        match PolicyDecision.all (ops |> List.map (w.Decide author)) with
        | PolicyDecision.Deny g -> SubmitDenied g
        | PolicyDecision.NeedsApproval ->
            let q', id = propose author at intent ops q
            SubmitProposed(q', id)
        | PolicyDecision.Allow ->
            match applyAll w ops state with
            | Ok next -> SubmitApplied next
            | Error rejection -> SubmitOpRejected rejection

    /// The capabilities an op invokes, through the registry the actor acts through, joined with the
    /// domain's own decision for the op (Phase 318): `w.Decide actor op`
    /// joined with `CapabilityRegistry.decide registry id args` for every invocation
    /// `EffectsOf op` names — so an op that invokes an unregistered capability is denied naming the
    /// registered ones, an op whose arguments do not validate is denied, and the registry's gates
    /// refuse what they refuse, without the domain stating any of it in `Decide`. The registry is
    /// the one THIS actor acts through: an actor-scoped policy is a `restrict` or a `withGate` of the
    /// host's registry.
    let decideGuarded
        (gw: GuardedSurfaceWitness<'State, 'Op, 'Rej>)
        (registry: CapabilityRegistry)
        (actor: string)
        (op: 'Op)
        : PolicyDecision =
        gw.Surface.Decide actor op
        :: (gw.EffectsOf op
            |> List.map (fun (id, args) -> CapabilityRegistry.decide registry id args))
        |> PolicyDecision.all

    /// Dry-run the whole sequence through `DryRun` (Phase 318): the first op that
    /// would not apply, threaded through the states the earlier ones would produce. Runs no `Apply`.
    let private dryRunAll
        (gw: GuardedSurfaceWitness<'State, 'Op, 'Rej>)
        (ops: 'Op list)
        (state: 'State)
        : Result<unit, 'Rej> =
        ops
        |> List.fold (fun acc op -> acc |> Result.bind (gw.DryRun op)) (Ok state)
        |> Result.map ignore

    /// `submit` behind the registry's gate and a dry run (Phase 318). Every op's decision is
    /// `decideGuarded` — the domain's policy joined with the registry's decision for each capability
    /// the op invokes — and the sequence's is their join; a `Deny` refuses it before anything else.
    /// Then the WHOLE sequence is dry-run through `DryRun`, so an op that would not apply refuses the
    /// sequence (`SubmitOpRejected`) before the first `Apply` runs — and before it is parked, so a
    /// proposal an approver can never apply is not queued. Only then is it parked (`NeedsApproval`)
    /// or applied (`Allow`). An `Apply` that performs effects therefore never runs for a sequence a
    /// later op refuses.
    let submitGuarded
        (gw: GuardedSurfaceWitness<'State, 'Op, 'Rej>)
        (registry: CapabilityRegistry)
        (author: string)
        (at: string)
        (intent: string option)
        (ops: 'Op list)
        (q: ProposalQueue<'Op>)
        (state: 'State)
        : SubmitOutcome<'State, 'Op, 'Rej> =
        match PolicyDecision.all (ops |> List.map (decideGuarded gw registry author)) with
        | PolicyDecision.Deny g -> SubmitDenied g
        | decision ->
            match dryRunAll gw ops state with
            | Error rejection -> SubmitOpRejected rejection
            | Ok() ->
                match decision with
                | PolicyDecision.NeedsApproval ->
                    let q', id = propose author at intent ops q
                    SubmitProposed(q', id)
                | _ ->
                    match applyAll gw.Surface ops state with
                    | Ok next -> SubmitApplied next
                    | Error rejection -> SubmitOpRejected rejection

    /// `approve` behind the registry's gate and a dry run (Phase 318). The approver may not be the
    /// author (`SelfApproval`); every op's `decideGuarded` for the APPROVER through `registry` — the
    /// registry the approver acts through — must not be a `Deny` (`ApprovalDenied`, the first
    /// denial's guidance); the whole sequence is dry-run before the first `Apply`, and an op that no
    /// longer applies is `OpNoLongerApplies` with the proposal left pending and nothing applied.
    let approveGuarded
        (gw: GuardedSurfaceWitness<'State, 'Op, 'Rej>)
        (registry: CapabilityRegistry)
        (approver: string)
        (at: string)
        (id: int)
        (q: ProposalQueue<'Op>)
        (state: 'State)
        : Result<ProposalQueue<'Op> * 'State, ApprovalFailure<'Rej>> =
        find id q
        |> Result.bind (fun p ->
            if p.Author = approver then
                Error(SelfApproval(id, p.Author))
            else
                match PolicyDecision.all (p.Ops |> List.map (decideGuarded gw registry approver)) with
                | PolicyDecision.Deny g -> Error(ApprovalDenied(id, g))
                | _ ->
                    match dryRunAll gw p.Ops state with
                    | Error rejection -> Error(OpNoLongerApplies(id, rejection))
                    | Ok() ->
                        match applyAll gw.Surface p.Ops state with
                        | Error rejection -> Error(OpNoLongerApplies(id, rejection))
                        | Ok next -> Ok(setStatus id (ProposalStatus.Approved(approver, at)) q, next))

    /// Render guidance as agent-readable text: the message plus the enumerated
    /// alternatives (one per line), so a refused agent repairs instead of
    /// guessing. No alternatives ⇒ the message alone (the core invents none).
    /// A policy denial (`SubmitDenied`, `ApprovalDenied`) and a reducer
    /// rejection (`explainRejection`) render through this one function.
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

    /// This queue's proposal as the op layer needs it (Phase 192) — the id the
    /// pinned order sorts by, the party a decision is reported back to, and the
    /// script. The richer lifecycle fields (`ProposedAt`, `Intent`, `Status`)
    /// stay here: arbitration decides coexistence and reads none of them. A
    /// domain that arbitrates its pending queue projects through this and calls
    /// `Arbitration.arbitrate` over the result.
    let toOpScript (p: Proposal<SkeletonOp<'Node, 'Id>>) : OpScriptProposal<'Node, 'Id> =
        { Id = p.Id
          Holder = p.Author
          Ops = p.Ops }

/// The surface descriptor + read-tool dispatch — generic over the witness.
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
