namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.Ops — proposal arbitration (Phase 85; moved here from
//  `Fuaran.Core.AiSurface` by Phase 192). Which subset of N op-script proposals
//  can land together against one base tree: batch `canApply` + a greedy
//  footprint-independence pass, a deterministic total partition with typed,
//  actionable rejections.
//
//  It lives beside `Ops.footprint` / `Ops.independent` because it is the other
//  end of them — the concurrency half of the tree algebra, not an AI surface.
//  Its callers are schedulers deciding what may land together; none of them
//  drives a model. FSharp.Core + Fuaran.Core.Tree only (the same reference set
//  `Ops.fs` has), Fable-clean.
// ============================================================================

/// One op-script proposal, as arbitration needs it: the id the pinned order
/// sorts by, the party the decision is reported back to, and the script itself.
/// Deliberately minimal — the partition reads `Id` and `Ops`, and carries
/// `Holder` through so a rejection names whom to tell. A richer queue (author,
/// timestamp, approval status, intent) is a host concern: `AiSurface.Proposals`
/// keeps one and projects to this record with `Proposals.toOpScript`.
type OpScriptProposal<'Node, 'Id> =
    { Id: int
      Holder: string
      Ops: SkeletonOp<'Node, 'Id> list }

/// Why arbitration rejected one proposal — the AI-feedback protocol shape (GP5):
/// a rejected agent knows exactly what to repair or rebase against.
type ArbitrationRejection<'Id> =
    /// The proposal's script does not apply to the base tree: the op-algebra's
    /// own rejection envelope, plus the index of the failing op in the script.
    /// A stale script's envelope is `UnknownNode`, whose `addressable` is every id
    /// in the base; `Arbitration.stale` is its bounded form for reporting (Phase 248).
    | Inapplicable of opIndex: int * rejection: Rejection<'Id>
    /// The script applies, but its footprint (Phase 78) interferes with the
    /// accepted set — the interfering accepted proposals' ids (computed against
    /// the FULL accepted set, in pinned order): exactly what to rebase against
    /// once they land. Non-empty by construction.
    ///
    /// `interference` (Phase 248) says HOW each one interferes: every cited id, in
    /// the same order, paired with the `Ops.interference` clauses its footprint and
    /// the proposal's fail (the proposal on the left), each list non-empty. It is
    /// exactly `Arbitration.interference nodew idw accepted proposal`, computed by the
    /// same function, so `interfering = List.map fst interference` always holds.
    | Conflicts of interfering: int list * interference: (int * Interference list) list

/// The bounded report of a stale proposal (Phase 248): the id its script named that the tree
/// does not hold, where in the script, how many ids the tree does hold, and a sample of them
/// capped at `Arbitration.staleSampleSize`. Its size is fixed by the error, never by the
/// document — what a scheduler sends each of N refused proposers, where the op-algebra's own
/// `UnknownNode` would send the whole base's ids N times.
type StaleProposal<'Id> =
    {
        /// The index of the failing op in the proposal's script.
        OpIndex: int
        /// The id the failing op named that the tree does not hold at that op — the base, as the
        /// script's earlier ops left it.
        Missing: 'Id
        /// How many ids that tree does hold — the length of `UnknownNode`'s `addressable`.
        AddressableCount: int
        /// The first `Arbitration.staleSampleSize` of those ids, in the tree's pre-order (the root
        /// first): enough to show the id shape the base uses, never the document.
        Sample: 'Id list
    }

/// The result of arbitrating N op-script proposals against one base tree
/// (Phase 85) — a deterministic, TOTAL partition. `Accepted` is mutually
/// independent (pairwise `Ops.independent`), listed in the pinned order
/// (ascending proposal id); by footprint soundness its scripts apply
/// confluently in ANY order. `MergedScript` is the accepted scripts composed
/// in the pinned order — one canonical serialisation of the accepted set.
/// `Rejected` pairs every non-accepted proposal with its typed reason (GP5);
/// nothing is ever silently dropped.
type Arbitration<'Node, 'Id> =
    { Accepted: OpScriptProposal<'Node, 'Id> list
      MergedScript: SkeletonOp<'Node, 'Id> list
      Rejected: (OpScriptProposal<'Node, 'Id> * ArbitrationRejection<'Id>) list }

/// Proposal arbitration over the skeleton-op algebra — the concurrency half of
/// `Ops.footprint` / `Ops.independent`. (`ModuleSuffix` so the module coexists
/// with the `Arbitration` type above.)
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Arbitration =

    /// The proposal ids carried by MORE THAN ONE proposal — ascending, each listed once; empty
    /// exactly when the ids are unique (Phase 157). A TOTAL check, never an assigner: it reads
    /// `Id` and nothing else, mints nothing, and renumbers nothing.
    ///
    /// It exists because `arbitrate`'s permutation invariance is a theorem about id-UNIQUE input
    /// and about nothing weaker (`proofs/Arbitrate.fst`, `arbitrate_deterministic`; the witness
    /// that the hypothesis is needed is `duplicate_ids_break_invariance` beside it). The function
    /// that assumes uniqueness and the callers that mint ids sit in different packages, so the
    /// assumption is checkable here, by whoever holds the list: `duplicateIds proposals = []` is
    /// the hypothesis, and a non-empty answer names the ids to repair. `arbitrate` itself stays
    /// total either way and does NOT call this — what a duplicate costs is invariance under
    /// arrival order, never the partition, its independence or its justifications.
    let duplicateIds (proposals: OpScriptProposal<'Node, 'Id> list) : int list =
        proposals
        |> List.countBy (fun p -> p.Id)
        |> List.filter (fun (_, n) -> n > 1)
        |> List.map fst
        |> List.sort

    /// The cap on `StaleProposal.Sample` (Phase 248): at most this many addressable ids ride a stale
    /// proposal's bounded report, whatever the document's size.
    let staleSampleSize = 8

    /// The bounded report of a stale proposal's rejection (Phase 248): `Some` for an `Inapplicable`
    /// whose envelope is `UnknownNode` — the script named an id the base does not hold — and `None`
    /// for every other rejection, which carries no document-sized payload to bound. Total.
    let stale (rejection: ArbitrationRejection<'Id>) : StaleProposal<'Id> option =
        match rejection with
        | Inapplicable(opIndex, UnknownNode(missing, addressable)) ->
            Some
                { OpIndex = opIndex
                  Missing = missing
                  AddressableCount = List.length addressable
                  Sample = List.truncate staleSampleSize addressable }
        | _ -> None

    // The accepted proposals a footprint interferes with, in the given order, each with the clauses
    // it fails — the one computation behind both `Conflicts`' citation and `interference` below.
    let private interferingWith
        (fp: Footprint)
        (accepted: (OpScriptProposal<'Node, 'Id> * Footprint) list)
        : (int * Interference list) list =
        accepted
        |> List.choose (fun (a, afp) ->
            match Ops.interference fp afp with
            | [] -> None
            | clauses -> Some(a.Id, clauses))

    /// Why `proposal` cannot join `accepted` (Phase 248): each accepted proposal it interferes with,
    /// in the order given, paired with `Ops.interference` of the two footprints — the proposal on
    /// the left, the accepted one on the right. Handed an arbitration's `Accepted` and one of its
    /// `Conflicts`-rejected proposals, it is exactly that `Conflicts`' `interference` member (its ids
    /// the `interfering` citation, in the same order), because `arbitrate` computes both with this
    /// function; every clause list is non-empty. It stays the query surface for a party that holds
    /// a proposal and an accepted set but no rejection. Empty exactly when the proposal is
    /// independent of every accepted one. Total.
    let interference
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (accepted: OpScriptProposal<'Node, 'Id> list)
        (proposal: OpScriptProposal<'Node, 'Id>)
        : (int * Interference list) list =
        accepted
        |> List.map (fun a -> a, Ops.footprint nodew idw a.Ops)
        |> interferingWith (Ops.footprint nodew idw proposal.Ops)

    /// Arbitrate N op-script proposals against one base tree (Phase 85) —
    /// decide which subset can land together. A deterministic, total partition
    /// (GP4: analysis only — the base is never mutated, and no input throws):
    ///
    ///   1. **Pin the order.** Proposals are processed in ascending `Id`, so
    ///      the outcome is invariant under permutation of the input list. Ids
    ///      are expected unique (a queue assigns them — the Phase 59
    ///      `Proposals.propose` discipline is one such assigner); `arbitrate`
    ///      stays total on duplicate-id input (the stable sort breaks the tie
    ///      by input order), but the permutation-invariance guarantee assumes
    ///      unique ids — `duplicateIds proposals = []` is that assumption as a
    ///      total check, and `proofs/Arbitrate.fst` proves both the guarantee
    ///      under it and that it cannot be dropped.
    ///   2. **Batch `canApply`.** `Ops.canApplyAll` against the base filters
    ///      the inapplicable — each rejection carries the op-algebra's own
    ///      envelope + the failing op index (GP5).
    ///   3. **Greedy independence.** Each remaining proposal is accepted iff
    ///      its footprint (Phase 78, `Ops.footprint`) is `Ops.independent` of
    ///      everything already accepted; otherwise it is rejected with
    ///      `Conflicts`, citing the interfering accepted ids (recomputed
    ///      against the FULL accepted set, so the citation is the complete
    ///      rebase target, not just the first collision) and, since Phase 248,
    ///      each one's `Ops.interference` clauses.
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
        (proposals: OpScriptProposal<'Node, 'Id> list)
        : Arbitration<'Node, 'Id> =
        // the pinned deterministic order — ascending proposal id.
        let pinned = proposals |> List.sortBy (fun p -> p.Id)

        // greedy pass: accepted accumulates (proposal, footprint) in reverse pinned
        // order; a conflict at decision time is provisional (re-cited below).
        let step (accepted, rejected) (p: OpScriptProposal<'Node, 'Id>) =
            match Ops.canApplyAll nodew idw p.Ops baseTree with
            | Error(i, rej) -> accepted, (p, Inapplicable(i, rej)) :: rejected
            | Ok() ->
                let fp = Ops.footprint nodew idw p.Ops

                if accepted |> List.forall (fun (_, afp) -> Ops.independent fp afp) then
                    (p, fp) :: accepted, rejected
                else
                    accepted, (p, Conflicts([], [])) :: rejected

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
                    let explained = interferingWith (Ops.footprint nodew idw p.Ops) accepted
                    p, Conflicts(List.map fst explained, explained))

        let acceptedProposals = accepted |> List.map fst

        { Accepted = acceptedProposals
          MergedScript = acceptedProposals |> List.collect (fun p -> p.Ops)
          Rejected = rejected }
