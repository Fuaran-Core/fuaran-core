(*
   Skeleton — the composite: fold confluence for the skeleton-op tree algebra, with the domain
   hypothesis DISCHARGED rather than assumed (fuaran-core Phase 133; widened to the whole
   operation alphabet by Phase 162).

   `DagFold.fold_confluence` proves that any two arrival orders of one lane set reach equivalent
   outcomes, under one domain hypothesis — `independence_diamond`, the promise that footprint-
   independent operations which both apply at a state commute there. Phase 78/80 certify that
   promise for this algebra by SAMPLING. `TreeOps.op_independence_diamond` proves it. This module
   is the composition, and it is deliberately thin: there is no new argument here, only the
   instantiation, which is the point — the two halves were designed to meet.

   WHAT THE COMPOSITE SAYS. For the skeleton ops over any tree the witness can show `Ops`, folded
   through `Ops.apply` and `Ops.footprint`: the same lanes reach the same tree however they
   arrive, a lane set that cannot fold halts with the same canonical report however it arrives,
   and none folds under one order and halts under another. No hypothesis about the domain remains.

   WHAT IT IS ABOUT, EXACTLY — ONE boundary now, stated in `TreeOps` and not hidden here. The
   algebra is `TreeOps.wapply`, which is `Ops.apply` declining a step that would leave the tree
   carrying an id twice — and by `TreeOps.ins_wf` / `ins_wf_conv` that class is EXACTLY the one
   Phase 137's validation refuses, so on the shipped algebra the guard declines nothing that
   `Ops.apply` accepts. `TreeOps.insert_breaks_wf_pre137` is the concrete accepted insert of that
   kind against the PRE-137 clause, kept as the record of what was wrong, and
   `TreeOps.cx_insert_refused_now` is the live validator refusing it.

   THE SECOND BOUNDARY IS GONE (Phase 162). Until then the op alphabet here was
   `TreeOps.leaf_op` — the four non-`Batch` skeleton ops — because the diamond had not been lifted
   along a batch's script. `TreeOps` section 20 performs that lift, so the alphabet is the whole
   of `TreeOps.op`, `Batch` included and nested to any depth, and nothing in this module is
   restricted any more. The leaf statement is kept in `TreeOps` beside the general one
   (`leaf_independence_diamond`), because it is what section 17 proves directly and a reader
   checking the widening wants both.

   The HALT half needs no boundary at all, and is stated separately so that "the halt half is
   unconditional" is machine-checked here too rather than inherited by assertion.

   Apache-2.0, like everything beside it.
*)
module Skeleton

open DagFold
open TreeOps

(* The fold at this algebra, with the domain's apply and footprint already supplied — the
   function the theorems below are about, and the one the differential host runs, so the oracle
   compared against production is the one the theorem names rather than a re-assembly of parts. *)
let skeleton_fold (s0:tree) (lanes:list (list op))
  : Tot (lane_outcome op tree rejection) =
  fold_once wapply op_fp s0 lanes

(* THE HALT HALF — no hypothesis at all. Whether a lane set halts, and the canonical report it
   halts with, are properties of `Ops.footprint` alone; nothing about `Ops.apply`, nothing about
   well-formedness, and nothing about the op alphabet is used. *)
val skeleton_fold_confluence_halt
  (s0:tree) (ls1 ls2:list (list op)) (p:perm (list op) ls1 ls2)
  : Lemma (requires not (is_empty (all_conflicts op_fp ls1)))
          (ensures outcome_equiv (skeleton_fold s0 ls1) (skeleton_fold s0 ls2))

let skeleton_fold_confluence_halt s0 ls1 ls2 p =
  fold_confluence_halt wapply op_fp s0 ls1 ls2 p

(* THE THEOREM. `DagFold.fold_confluence`'s domain hypothesis is discharged by
   `TreeOps.op_independence_diamond`, so what remains in the `requires` is `lanes_apply` — not a
   promise about the domain but a statement about the lane set in hand, that every lane applies
   cleanly from the base state, which is the lane set `FoldConfluence.foldOnce`'s generators
   produce and the only one the fold half was ever about. *)
val skeleton_fold_confluence
  (s0:tree) (ls1 ls2:list (list op)) (p:perm (list op) ls1 ls2)
  : Lemma (requires lanes_apply wapply ls1 s0)
          (ensures outcome_equiv (skeleton_fold s0 ls1) (skeleton_fold s0 ls2))

let skeleton_fold_confluence s0 ls1 ls2 p =
  op_independence_diamond ();
  fold_confluence wapply op_fp s0 ls1 ls2 p

(* THE WIDENING, EVALUATED. The alphabet really did grow: a lane carrying a nested `Batch` is a
   lane this module could not previously be stated about, and `skeleton_fold` now takes one. The
   witness is `TreeOps.lift_batch`, the concrete pair section 20 uses to show the lift is not
   vacuous, folded here as a one-op lane — so this goes red if the alphabet is ever narrowed
   back. *)
let batch_lanes_fold ()
  : Lemma (ensures LaneFolded? (skeleton_fold lift_tree [[lift_batch]; [lift_leaf]]))
  = assert_norm (LaneFolded? (skeleton_fold lift_tree [[lift_batch]; [lift_leaf]]))
