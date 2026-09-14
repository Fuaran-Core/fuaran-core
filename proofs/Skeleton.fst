(*
   Skeleton — the composite: fold confluence for the skeleton-op tree algebra, with the domain
   hypothesis DISCHARGED rather than assumed (fuaran-core Phase 133).

   `DagFold.fold_confluence` proves that any two arrival orders of one lane set reach equivalent
   outcomes, under one domain hypothesis — `independence_diamond`, the promise that footprint-
   independent operations which both apply at a state commute there. Phase 78/80 certify that
   promise for this algebra by SAMPLING. `TreeOps.leaf_independence_diamond` proves it. This
   module is the composition, and it is deliberately thin: there is no new argument here, only the
   instantiation, which is the point — the two halves were designed to meet.

   WHAT THE COMPOSITE SAYS. For the skeleton ops over any tree the witness can show `Ops`, folded
   through `Ops.apply` and `Ops.footprint`: the same lanes reach the same tree however they
   arrive, a lane set that cannot fold halts with the same canonical report however it arrives,
   and none folds under one order and halts under another. No hypothesis about the domain remains.

   WHAT IT IS ABOUT, EXACTLY — two boundaries, both stated in `TreeOps` and neither hidden here:

     - the algebra is `TreeOps.wapply`, which is `Ops.apply` declining a step that would leave the
       tree carrying an id twice. `TreeOps.insert_breaks_wf` is a concrete accepted insert of that
       kind, so the two are not the same function today; `TreeOps.ins_wf` / `ins_wf_conv` say the
       class is EXACTLY the one Phase 137's validation refuses, so they will be.
     - the op alphabet is `TreeOps.leaf_op` — the four non-`Batch` skeleton ops. `Ops.apply`
       threads a `Batch` exactly as the fold threads a lane, so this removes no behaviour from the
       fold; it declines to nest one lane inside another. The nesting is the one pair shape
       `TreeOps.covered` leaves open.

   The HALT half needs neither boundary, and is stated separately so that "the halt half is
   unconditional" is machine-checked here too rather than inherited by assertion.

   Apache-2.0, like everything beside it.
*)
module Skeleton

open DagFold
open TreeOps

(* The fold at this algebra, with the domain's apply and footprint already supplied — the
   function the theorems below are about, and the one the differential host runs, so the oracle
   compared against production is the one the theorem names rather than a re-assembly of parts. *)
let skeleton_fold (s0:tree) (lanes:list (list leaf_op))
  : Tot (lane_outcome leaf_op tree rejection) =
  fold_once wapply leaf_fp s0 lanes

(* THE HALT HALF — no hypothesis at all. Whether a lane set halts, and the canonical report it
   halts with, are properties of `Ops.footprint` alone; nothing about `Ops.apply`, nothing about
   well-formedness, and nothing about the op alphabet is used. *)
val skeleton_fold_confluence_halt
  (s0:tree) (ls1 ls2:list (list leaf_op)) (p:perm (list leaf_op) ls1 ls2)
  : Lemma (requires not (is_empty (all_conflicts leaf_fp ls1)))
          (ensures outcome_equiv (skeleton_fold s0 ls1) (skeleton_fold s0 ls2))

let skeleton_fold_confluence_halt s0 ls1 ls2 p =
  fold_confluence_halt wapply leaf_fp s0 ls1 ls2 p

(* THE THEOREM. `DagFold.fold_confluence`'s domain hypothesis is discharged by
   `TreeOps.leaf_independence_diamond`, so what remains in the `requires` is `lanes_apply` — not a
   promise about the domain but a statement about the lane set in hand, that every lane applies
   cleanly from the base state, which is the lane set `FoldConfluence.foldOnce`'s generators
   produce and the only one the fold half was ever about. *)
val skeleton_fold_confluence
  (s0:tree) (ls1 ls2:list (list leaf_op)) (p:perm (list leaf_op) ls1 ls2)
  : Lemma (requires lanes_apply wapply ls1 s0)
          (ensures outcome_equiv (skeleton_fold s0 ls1) (skeleton_fold s0 ls2))

let skeleton_fold_confluence s0 ls1 ls2 p =
  leaf_independence_diamond ();
  fold_confluence wapply leaf_fp s0 ls1 ls2 p
