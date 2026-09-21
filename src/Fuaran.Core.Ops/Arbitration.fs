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
