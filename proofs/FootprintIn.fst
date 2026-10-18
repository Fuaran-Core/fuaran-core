(*
   FootprintIn — the two theorems about Fuaran.Core's base-aware footprint (fuaran-core Phase
   500): `footprint_in_sound` and `footprint_in_refines`, over the model `TreeOps.fst` section 24
   gives of `Ops.footprintIn`.

   WHAT IS PROVED.

     `footprint_in_refines`: every pair of operations the pure footprint (`TreeOps.op_fp`) calls
     independent, the base-aware one (`TreeOps.op_fp_in t`) calls independent, at every tree. So a
     consumer that moves from `Ops.footprint` to `Ops.footprintIn` — from `Dag.reconcileMany` to
     `reconcileManyIn`, from `Arbitration.arbitrate` to `arbitrateIn` — loses no fold and no
     accepted proposal. The proof is section 8's elimination run the other way: a pure-independent
     pair either has no relocating side, where the base-aware footprint IS the pure one, or has
     one, where the other side is inert and its base-aware footprint is empty.

     `footprint_in_sound`: for every pair whose base-aware footprints `independent` declares
     disjoint and every well-formed tree at which both apply, each applies after the other and the
     two orders reach the same tree — `TreeOps.wstep`, the shape `tree_independence_diamond` has.
     Unlike the pure diamond, no pair here is closed without looking at the tree: the base-aware
     footprint frees exactly the pairs the unknown-parent clause used to refuse, so each of them is
     a commutation on the tree. Section 3 proves the IN-PLACE fragment — every pair of operations
     built from `InsertChild`, `ReorderChildren`, `UpdateNode` and `Batch` — which is the fragment
     the first adopting domain measured (its in-place edits, with the unknown-parent write erased),
     and section 4 lifts it along a batch's script as section 20 of `TreeOps.fst` lifts the pure
     diamond. The lift carries ONE fact the pure lift did not need, because the pure footprint is a
     function of the op alone: that an operation's base-aware footprint is STABLE under the
     application of an operation it is independent of (`fp_in_stable`), so what the fold read at
     the base is what holds at every state the fold reaches.

   WHAT IS NOT PROVED HERE, and is named rather than assumed. The relocating pairs — a `RemoveNode`
   or a `MoveNode` against anything but an inert batch — are outside section 3's fragment:
   `footprint_in_sound` quantifies over `in_place` operations, and `in_place` is the boundary
   written as a predicate, exactly as `TreeOps.covered` once was for the batch pairs (Phase 133,
   closed by Phase 162). The two acceptance theorems of the phase (section 3) are inside the
   fragment and stated outright. Section 6 lays the tree facts the remove pairs stand on — a
   removal commutes with a removal at another parent, with an insert and a reorder elsewhere, and
   with any rewrite, each proved unconditionally because every edit keeps a node's id and so
   commutes with `drop_kid` list by list; and the source parent's child list loses exactly the
   one subtree `find_in` answers with (`find_all_drop`). What the successor adds on top of them is
   what a removal leaves in place under id uniqueness (every id outside the destroyed subtree,
   found at the same node, under the same parent), the four remove pairs, the move pairs, and the
   fragment predicate retired.

   HOW TO READ IT. Everything is over `TreeOps`'s definitions, opened; `Preservation` is cited
   qualified for the id-uniqueness invariant at intermediate states, as `Arbitrate.fst` cites it.
   Proof-only: nothing here is extracted, and the differential against production is the section
   24 model's, in `ProofOracle.TreeOpsTests`.

   Apache-2.0, like everything beside it.
*)
module FootprintIn

open Twin
open ListAlgebra
open DagFold
open TreeOps

(* Opens TreeOps and DagFold, so every membership pattern those two declare is live at every
   query here — the reason their own entries give for pruning. *)
#set-options "--ext context_pruning"

(* ======================================================================================
   1. The model's own small facts.
   ====================================================================================== *)

(* An operation with no relocation and no rewrite in it is footprinted purely, at every tree and
   whatever the threading meets: the base-aware arms resolve nothing for an insert or a reorder. *)
let rec no_reloc_fp_in (t:tree) (o:op)
  : Lemma (requires no_reloc o) (ensures op_fp_in t o == op_fp o) (decreases o)
  = match o with
    | Batch os -> no_reloc_fp_in_all t os
    | _ -> ()
and no_reloc_fp_in_all (t:tree) (os:list op)
  : Lemma (requires no_reloc_all os) (ensures fp_in_all t os == fp_all os) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      no_reloc_fp_in t o;
      (match apply o t with
       | Ok t' -> no_reloc_fp_in_all t' r
       | Error _ -> ())

(* An inert operation's base-aware footprint is empty, as its pure one is. *)
let rec inert_fp_in (t:tree) (o:op)
  : Lemma (requires inert o) (ensures op_fp_in t o == empty_fp) (decreases o)
  = match o with
    | Batch os -> inert_fp_in_all t os
    | _ -> ()
and inert_fp_in_all (t:tree) (os:list op)
  : Lemma (requires inert_all os) (ensures fp_in_all t os == empty_fp) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      inert_fp_in t o;
      inert_is_identity o t;
      inert_fp_in_all t r

(* Nothing interferes with the empty footprint. *)
let independent_empty_right (f:footprint)
  : Lemma (ensures independent f empty_fp)
  = inter_nil_iff f.content_writes empty_fp.content_writes;
    inter_nil_iff f.content_writes empty_fp.reads;
    inter_nil_iff empty_fp.content_writes f.reads;
    inter_nil_iff f.structure_writes empty_fp.structure_writes;
    inter_nil_iff f.slot_writes empty_fp.slot_writes;
    inter_nil_iff f.slot_writes empty_fp.slot_reads;
    inter_nil_iff f.slot_reads empty_fp.slot_writes;
    inter_nil_iff (nodes f.slot_writes) (union empty_fp.content_writes (union empty_fp.reads empty_fp.structure_writes));
    inter_nil_iff (nodes f.slot_reads) empty_fp.content_writes;
    inter_nil_iff (nodes empty_fp.slot_writes) (union f.content_writes (union f.reads f.structure_writes));
    inter_nil_iff (nodes empty_fp.slot_reads) f.content_writes

let independent_empty_left (f:footprint)
  : Lemma (ensures independent empty_fp f)
  = independent_empty_right f; independent_sym f empty_fp

(* ======================================================================================
   2. REFINEMENT — a pair the pure footprint frees, the base-aware footprint frees.

      Section 8's elimination, run the other way. A pure-independent pair either has no
      relocating side — then neither side carries a remove, a move or a rewrite, and the
      base-aware footprint of each IS its pure footprint — or has one, and then the other side is
      inert (`relocating_forces_inert`), its base-aware footprint is empty, and nothing interferes
      with the empty footprint.
   ====================================================================================== *)

let footprint_in_refines (t:tree) (a b:op)
  : Lemma (requires independent (op_fp a) (op_fp b))
          (ensures independent (op_fp_in t a) (op_fp_in t b))
  = if relocating a then begin
      relocating_forces_inert a b;
      inert_fp_in t b;
      independent_empty_right (op_fp_in t a)
    end
    else if relocating b then begin
      independent_sym (op_fp a) (op_fp b);
      relocating_forces_inert b a;
      inert_fp_in t a;
      independent_empty_left (op_fp_in t b)
    end
    else begin
      relocating_iff_not_no_reloc a;
      relocating_iff_not_no_reloc b;
      no_reloc_fp_in t a;
      no_reloc_fp_in t b
    end

(* The same at the script level — what `Dag.conflictsIn` reads against `Dag.conflicts`: two
   scripts whose pure footprints are independent have independent base-aware footprints, each
   threaded from the base. *)
let footprint_in_refines_scripts (t:tree) (a b:list op)
  : Lemma (requires independent (fp_all a) (fp_all b))
          (ensures independent (fp_in_all t a) (fp_in_all t b))
  = footprint_in_refines t (Batch a) (Batch b)

(* ======================================================================================
   3. THE ACCEPTANCE, stated outright — the two cases the phase is for.
   ====================================================================================== *)

(* Two in-place rewrites of DIFFERENT present nodes are independent under the base-aware
   footprint, wherever the two nodes sit: each records a read and a content-write of its own
   target and nothing else. (The pure footprint refuses every such pair: each carries an
   unknown-parent write, and the other writes structure.) *)
let two_updates_independent (t:tree) (n m:tree)
  : Lemma (requires has_id (tid_of n) t /\ has_id (tid_of m) t /\ tid_of n <> tid_of m)
          (ensures independent (op_fp_in t (UpdateNode n)) (op_fp_in t (UpdateNode m))
                   /\ not (independent (op_fp (UpdateNode n)) (op_fp (UpdateNode m))))
  = let x = tid_of n in
    let y = tid_of m in
    let fa = op_fp_in t (UpdateNode n) in
    let fb = op_fp_in t (UpdateNode m) in
    inter_nil_iff fa.content_writes fb.content_writes;
    inter_nil_iff fa.content_writes fb.reads;
    inter_nil_iff fb.content_writes fa.reads;
    inter_nil_iff fa.structure_writes fb.structure_writes;
    inter_nil_iff fa.slot_writes fb.slot_writes;
    inter_nil_iff fa.slot_writes fb.slot_reads;
    inter_nil_iff fa.slot_reads fb.slot_writes;
    inter_nil_iff (nodes fa.slot_writes) (union fb.content_writes (union fb.reads fb.structure_writes));
    inter_nil_iff (nodes fa.slot_reads) fb.content_writes;
    inter_nil_iff (nodes fb.slot_writes) (union fa.content_writes (union fa.reads fa.structure_writes));
    inter_nil_iff (nodes fb.slot_reads) fa.content_writes

(* An in-place rewrite of a node inside the subtree a concurrent removal destroys is REFUSED: the
   removal content-writes every id of that subtree, and the rewrite reads its target. *)
let update_under_removal_refused (t:tree) (n:tree) (x:string) (sub:tree) (p:string)
  : Lemma (requires find_in x t == Some sub /\ parent_of x t == Some p
                    /\ has_id (tid_of n) t /\ mem (tid_of n) (ids sub))
          (ensures not (independent (op_fp_in t (UpdateNode n)) (op_fp_in t (RemoveNode x))))
  = let fa = op_fp_in t (UpdateNode n) in
    let fb = op_fp_in t (RemoveNode x) in
    inter_nil_iff fb.content_writes fa.reads

(* ======================================================================================
   4. THE IN-PLACE FRAGMENT — inserts, reorders, rewrites and batches of them.

      The pairs the base-aware footprint frees inside this fragment are the six that carry an
      `UpdateNode`, every one of which the pure footprint refused. None is closed by a footprint
      clause: a rewrite commutes with another rewrite of a different node, with an insert whose
      subtree does not carry its target, and with any reorder, and the three facts are proved on
      the tree. The insert/reorder pairs are section 12's, since the base-aware footprint of an
      insert or a reorder IS its pure footprint.
   ====================================================================================== *)

let rec in_place (o:op) : Tot bool (decreases o) =
  match o with
  | RemoveNode _ -> false
  | MoveNode _ _ -> false
  | Batch os -> in_place_all os
  | _ -> true
and in_place_all (os:list op) : Tot bool (decreases os) =
  match os with
  | [] -> true
  | o :: r -> in_place o && in_place_all r

(* ---- the tree facts: a rewrite commutes with a rewrite, an insert and a reorder ---- *)

let rec upd_all_app (x k:string) (l m:list tree)
  : Lemma (ensures upd_all x k (app l m) == app (upd_all x k l) (upd_all x k m))
  = match l with
    | [] -> ()
    | _ :: r -> upd_all_app x k r m

let rec upd_upd_comm (x k1 y k2:string) (t:tree)
  : Lemma (requires x <> y) (ensures upd x k1 (upd y k2 t) == upd y k2 (upd x k1 t)) (decreases t)
  = match t with TNode _ _ cs -> upd_upd_comm_all x k1 y k2 cs
and upd_upd_comm_all (x k1 y k2:string) (ts:list tree)
  : Lemma (requires x <> y) (ensures upd_all x k1 (upd_all y k2 ts) == upd_all y k2 (upd_all x k1 ts))
          (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> upd_upd_comm x k1 y k2 t; upd_upd_comm_all x k1 y k2 r

let rec upd_ins_comm (x k p:string) (n:tree) (t:tree)
  : Lemma (requires not (mem x (ids n)))
          (ensures upd x k (ins p n t) == ins p n (upd x k t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      upd_ins_comm_all x k p n cs;
      if i = p then begin
        upd_all_app x k (ins_all p n cs) [n];
        Preservation.upd_absent x k n
      end
      else ()
and upd_ins_comm_all (x k p:string) (n:tree) (ts:list tree)
  : Lemma (requires not (mem x (ids n)))
          (ensures upd_all x k (ins_all p n ts) == ins_all p n (upd_all x k ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> upd_ins_comm x k p n t; upd_ins_comm_all x k p n r

let rec kid_ids_upd_all (x k:string) (ts:list tree)
  : Lemma (ensures kid_ids (upd_all x k ts) == kid_ids ts)
  = match ts with
    | [] -> ()
    | t :: r -> (match t with TNode _ _ _ -> kid_ids_upd_all x k r)

let rec pick_last_upd (y x k:string) (ts:list tree)
  : Lemma (ensures pick_last y (upd_all x k ts) == (match pick_last y ts with
                                                    | Some m -> Some (upd x k m)
                                                    | None -> None))
  = match ts with
    | [] -> ()
    | t :: r -> (match t with TNode _ _ _ -> pick_last_upd y x k r)

let rec arrange_upd (o:list string) (x k:string) (ts:list tree)
  : Lemma (ensures arrange o (upd_all x k ts) == upd_all x k (arrange o ts))
  = match o with
    | [] -> ()
    | y :: rest -> pick_last_upd y x k ts; arrange_upd rest x k ts

let rec upd_reorder_comm (x k p:string) (o:list string) (t:tree)
  : Lemma (ensures upd x k (reorder_at p o t) == reorder_at p o (upd x k t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      upd_reorder_comm_all x k p o cs;
      if i = p then arrange_upd o x k (reorder_all p o cs) else ()
and upd_reorder_comm_all (x k p:string) (o:list string) (ts:list tree)
  : Lemma (ensures upd_all x k (reorder_all p o ts) == reorder_all p o (upd_all x k ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> upd_reorder_comm x k p o t; upd_reorder_comm_all x k p o r

(* `find_in` through a rewrite: the node found is the rewritten one. *)
let rec find_upd (y x k:string) (t:tree)
  : Lemma (ensures find_in y (upd x k t) == (match find_in y t with
                                             | None -> None
                                             | Some m -> Some (upd x k m))) (decreases t)
  = match t with
    | TNode i _ cs -> if i = y then () else find_upd_all y x k cs
and find_upd_all (y x k:string) (ts:list tree)
  : Lemma (ensures find_all y (upd_all x k ts) == (match find_all y ts with
                                                   | None -> None
                                                   | Some m -> Some (upd x k m))) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> find_upd y x k t; (match find_in y t with Some _ -> () | None -> find_upd_all y x k r)

(* ---- the shape the lift threads: the diamond, and the footprints' stability across it ---- *)

(* `wstep` with one clause more: when both operations apply, each one's base-aware footprint at
   the tree the OTHER leaves is its footprint at `t`. The pure lift never needed this, because the
   pure footprint is a function of the op alone; here it is what lets the fold read every footprint
   once, at the base, and stand on it at every state it reaches. *)
let wstep_in (a b:op) (t:tree) : prop =
  wstep a b t /\
  (match apply a t, apply b t with
   | Ok ta, Ok tb -> op_fp_in ta b == op_fp_in t b /\ op_fp_in tb a == op_fp_in t a
   | _, _ -> True)

let wstep_in_sym (a b:op) (t:tree)
  : Lemma (requires wstep_in a b t) (ensures wstep_in b a t)
  = wstep_sym a b t

(* A present rewrite's footprint; an absent one's is the pure footprint, which carries an
   unknown-parent write and is independent of nothing that writes structure. *)
let update_fp_in (t:tree) (n:tree)
  : Lemma (ensures op_fp_in t (UpdateNode n) ==
                   (if has_id (tid_of n) t
                    then { reads = [tid_of n]; structure_writes = []; content_writes = [tid_of n];
                           unknown_parent_writes = []; slot_reads = []; slot_writes = [] }
                    else op_fp (UpdateNode n)))
  = ()

(* ---- CASE U/U: two rewrites of different nodes ---- *)

let update_update_in (n m:tree) (t:tree)
  : Lemma (requires wf t /\ independent (op_fp_in t (UpdateNode n)) (op_fp_in t (UpdateNode m)))
          (ensures wstep_in (UpdateNode n) (UpdateNode m) t)
  = let x = tid_of n in
    let y = tid_of m in
    update_fp_in t n;
    update_fp_in t m;
    if has_id x t && has_id y t then begin
      inter_nil_iff [x] [y];
      Preservation.ids_upd x (kind_of n) t;
      Preservation.ids_upd y (kind_of m) t;
      find_in_some_iff x t;
      find_in_some_iff y t;
      find_in_some_iff y (upd x (kind_of n) t);
      find_in_some_iff x (upd y (kind_of m) t);
      upd_upd_comm x (kind_of n) y (kind_of m) t
    end
    else (find_in_some_iff x t; find_in_some_iff y t)

(* ---- CASE U/I: a rewrite beside an insert whose subtree does not carry its target ---- *)

let update_insert_in (n:tree) (p:string) (g:tree) (t:tree)
  : Lemma (requires wf t /\ independent (op_fp_in t (UpdateNode n)) (op_fp_in t (InsertChild p g)))
          (ensures wstep_in (UpdateNode n) (InsertChild p g) t)
  = let x = tid_of n in
    let k = kind_of n in
    update_fp_in t n;
    if has_id x t then begin
      inter_nil_iff [x] (p :: ids g);
      if Ok? (apply (InsertChild p g) t) then begin
        ids_ins_mem x p g t;
        find_in_some_iff x t;
        find_in_some_iff x (ins p g t);
        Preservation.ids_upd x k t;
        first_dup_none_iff g t;
        first_dup_none_iff g (upd x k t);
        upd_ins_comm x k p g t
      end
      else ()
    end
    else find_in_some_iff x t

(* ---- CASE U/O: a rewrite beside any reorder ---- *)

let update_reorder_in (n:tree) (p:string) (o:list string) (t:tree)
  : Lemma (requires wf t /\ independent (op_fp_in t (UpdateNode n)) (op_fp_in t (ReorderChildren p o)))
          (ensures wstep_in (UpdateNode n) (ReorderChildren p o) t)
  = let x = tid_of n in
    let k = kind_of n in
    update_fp_in t n;
    if has_id x t then begin
      if Ok? (apply (ReorderChildren p o) t) then begin
        find_upd p x k t;
        (match find_in p t with
         | Some m -> (match m with TNode _ _ cs -> kid_ids_upd_all x k cs)
         | None -> ());
        wf_reorder_ok p o t;
        ids_reorder_mem x p o t;
        find_in_some_iff x t;
        find_in_some_iff x (reorder_at p o t);
        upd_reorder_comm x k p o t
      end
      else ()
    end
    else find_in_some_iff x t

(* ---- every leaf pair of the fragment ---- *)

let in_place_leaf_in (a b:op) (t:tree)
  : Lemma (requires wf t /\ is_leaf a /\ is_leaf b /\ in_place a /\ in_place b
                    /\ independent (op_fp_in t a) (op_fp_in t b))
          (ensures wstep_in a b t)
  = match a, b with
    | UpdateNode n, UpdateNode m -> update_update_in n m t
    | UpdateNode n, InsertChild p g -> update_insert_in n p g t
    | InsertChild p g, UpdateNode n ->
      independent_sym (op_fp_in t a) (op_fp_in t b);
      update_insert_in n p g t;
      wstep_in_sym b a t
    | UpdateNode n, ReorderChildren p o -> update_reorder_in n p o t
    | ReorderChildren p o, UpdateNode n ->
      independent_sym (op_fp_in t a) (op_fp_in t b);
      update_reorder_in n p o t;
      wstep_in_sym b a t
    | InsertChild p1 n1, InsertChild p2 n2 -> ins_ins_step p1 n1 p2 n2 t
    | InsertChild p1 n1, ReorderChildren p2 o2 -> ins_reorder_step p1 n1 p2 o2 t
    | ReorderChildren p2 o2, InsertChild p1 n1 ->
      independent_sym (op_fp a) (op_fp b);
      ins_reorder_step p1 n1 p2 o2 t;
      wstep_sym b a t
    | ReorderChildren p1 o1, ReorderChildren p2 o2 -> reorder_reorder_step p1 o1 p2 o2 t

(* ======================================================================================
   5. THE LIFT — the fragment's diamond along a batch's script, and the theorem.

      Section 20 of `TreeOps.fst`, with one more fact threaded: the base-aware footprint of the
      operation that stands still is the same at every state the script reaches (`wstep_in`'s
      second clause), which is what lets the independence read at `t` be read at `t1`, `t2`, …
      without being recomputed. Each step rests on `Preservation.apply_preserves_wf` for the
      id-uniqueness invariant at the intermediate state, as `Arbitrate.fst` does.
   ====================================================================================== *)

(* A batch's base-aware footprint, one step unfolded, when its head applies. *)
let fp_in_cons (t:tree) (o:op) (r:list op) (t1:tree)
  : Lemma (requires apply o t == Ok t1)
          (ensures op_fp_in t (Batch (o :: r)) == union_fp (op_fp_in t o) (op_fp_in t1 (Batch r)))
  = ()

(* ---- move 2: a LEAF of the fragment against anything in it, lifting on the right ---- *)

let rec leaf_vs_any_in (a b:op) (t:tree)
  : Lemma (requires wf t /\ is_leaf a /\ in_place a /\ in_place b
                    /\ independent (op_fp_in t a) (op_fp_in t b))
          (ensures wstep_in a b t) (decreases b)
  = match b with
    | Batch os -> leaf_vs_script_in a os t
    | _ -> in_place_leaf_in a b t
and leaf_vs_script_in (a:op) (os:list op) (t:tree)
  : Lemma (requires wf t /\ is_leaf a /\ in_place a /\ in_place_all os
                    /\ independent (op_fp_in t a) (op_fp_in t (Batch os)))
          (ensures wstep_in a (Batch os) t) (decreases os)
  = match os with
    | [] -> ()
    | b1 :: bs ->
      (match apply b1 t with
       | Error _ -> ()
       | Ok t1 ->
         fp_in_cons t b1 bs t1;
         independent_union_right (op_fp_in t a) (op_fp_in t b1) (op_fp_in t1 (Batch bs));
         leaf_vs_any_in a b1 t;
         Preservation.apply_preserves_wf b1 t;
         (match apply a t with
          | Error _ -> ()
          | Ok ta ->
            (* a applies at t1 (the diamond with b1), with the footprint it had at t *)
            leaf_vs_script_in a bs t1;
            (match apply (Batch bs) t1 with
             | Error _ -> ()
             | Ok tb ->
               (match apply b1 ta with
                | Ok ta1 -> fp_in_cons ta b1 bs ta1
                | Error _ -> ()))))

(* ---- move 3: anything in the fragment against anything in it, lifting on the left ---- *)

let rec any_vs_any_in (a b:op) (t:tree)
  : Lemma (requires wf t /\ in_place a /\ in_place b /\ independent (op_fp_in t a) (op_fp_in t b))
          (ensures wstep_in a b t) (decreases a)
  = match a with
    | Batch os -> script_vs_any_in os b t
    | _ -> leaf_vs_any_in a b t
and script_vs_any_in (os:list op) (b:op) (t:tree)
  : Lemma (requires wf t /\ in_place_all os /\ in_place b
                    /\ independent (op_fp_in t (Batch os)) (op_fp_in t b))
          (ensures wstep_in (Batch os) b t) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      (match apply o t with
       | Error _ -> ()
       | Ok to ->
         fp_in_cons t o r to;
         independent_union_left (op_fp_in t o) (op_fp_in to (Batch r)) (op_fp_in t b);
         any_vs_any_in o b t;
         Preservation.apply_preserves_wf o t;
         (match apply b t with
          | Error _ -> ()
          | Ok tb ->
            script_vs_any_in r b to;
            (match apply (Batch r) to with
             | Error _ -> ()
             | Ok ta ->
               (match apply o tb with
                | Ok tb1 -> fp_in_cons tb o r tb1
                | Error _ -> ()))))

(* ---- THE THEOREM, over the fragment ----

   Every pair of in-place operations — inserts, reorders, rewrites and batches of them — whose
   base-aware footprints at `t` are independent commutes at `t`, in the shape
   `tree_independence_diamond` has, and each one's footprint is unchanged by the other. *)
let footprint_in_sound (a b:op) (t:tree)
  : Lemma (requires wf t /\ in_place a /\ in_place b /\ independent (op_fp_in t a) (op_fp_in t b))
          (ensures wstep a b t)
  = any_vs_any_in a b t

(* The script form — what `Dag.conflictsIn` reads: two in-place scripts whose threaded footprints
   are independent at `t` apply in either order from `t` and reach the same tree. *)
let footprint_in_sound_scripts (a b:list op) (t:tree)
  : Lemma (requires wf t /\ in_place_all a /\ in_place_all b
                    /\ independent (fp_in_all t a) (fp_in_all t b))
          (ensures wstep (Batch a) (Batch b) t)
  = footprint_in_sound (Batch a) (Batch b) t

(* And the stability the N-lane fold stands on: after one of two independent in-place
   operations applies, the other's footprint — and so its independence from a third — is what it
   was at `t`. *)
let footprint_in_stable (a b:op) (t:tree) (ta:tree)
  : Lemma (requires wf t /\ in_place a /\ in_place b /\ independent (op_fp_in t a) (op_fp_in t b)
                    /\ apply a t == Ok ta /\ Ok? (apply b t))
          (ensures op_fp_in ta b == op_fp_in t b)
  = any_vs_any_in a b t

(* ======================================================================================
   6. THE REMOVE FAMILY — the tree facts a removal's pairs stand on.

      Unlike a rewrite, a removal changes the id set, so the facts come in two kinds: the
      commutations, which hold unconditionally because every edit preserves a node's id and so
      commutes with `drop_kid` list by list; and what a removal leaves in place — every id outside
      the destroyed subtree, found at the same node (its own subtree edited), under the same
      parent — which need id uniqueness.
   ====================================================================================== *)

(* ---- `drop_kid` commutes with every per-node edit, since none of them moves a node's id ---- *)

let rec drop_kid_rem_all (x pid y:string) (ts:list tree)
  : Lemma (ensures drop_kid x (rem_all pid y ts) == rem_all pid y (drop_kid x ts))
  = match ts with
    | [] -> ()
    | t :: r -> (match t with TNode _ _ _ -> drop_kid_rem_all x pid y r)

let rec drop_kid_ins_all (x q:string) (n:tree) (ts:list tree)
  : Lemma (ensures drop_kid x (ins_all q n ts) == ins_all q n (drop_kid x ts))
  = match ts with
    | [] -> ()
    | t :: r -> (match t with TNode _ _ _ -> drop_kid_ins_all x q n r)

let rec drop_kid_reorder_all (x q:string) (o:list string) (ts:list tree)
  : Lemma (ensures drop_kid x (reorder_all q o ts) == reorder_all q o (drop_kid x ts))
  = match ts with
    | [] -> ()
    | t :: r -> (match t with TNode _ _ _ -> drop_kid_reorder_all x q o r)

let rec drop_kid_upd_all (x y k:string) (ts:list tree)
  : Lemma (ensures drop_kid x (upd_all y k ts) == upd_all y k (drop_kid x ts))
  = match ts with
    | [] -> ()
    | t :: r -> (match t with TNode _ _ _ -> drop_kid_upd_all x y k r)

let rec rem_all_app (pid x:string) (l m:list tree)
  : Lemma (ensures rem_all pid x (app l m) == app (rem_all pid x l) (rem_all pid x m))
  = match l with
    | [] -> ()
    | _ :: r -> rem_all_app pid x r m

let rec pick_last_rem (y pid x:string) (ts:list tree)
  : Lemma (ensures pick_last y (rem_all pid x ts) == (match pick_last y ts with
                                                      | Some m -> Some (rem_at pid x m)
                                                      | None -> None))
  = match ts with
    | [] -> ()
    | t :: r -> (match t with TNode _ _ _ -> pick_last_rem y pid x r)

let rec arrange_rem (o:list string) (pid x:string) (ts:list tree)
  : Lemma (ensures arrange o (rem_all pid x ts) == rem_all pid x (arrange o ts))
  = match o with
    | [] -> ()
    | y :: rest -> pick_last_rem y pid x ts; arrange_rem rest pid x ts

(* ---- the commutations ---- *)

let rec rem_rem_comm (p1 x1 p2 x2:string) (t:tree)
  : Lemma (requires p1 <> p2)
          (ensures rem_at p1 x1 (rem_at p2 x2 t) == rem_at p2 x2 (rem_at p1 x1 t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      rem_rem_comm_all p1 x1 p2 x2 cs;
      if i = p1 then drop_kid_rem_all x1 p2 x2 (rem_all p1 x1 cs)
      else if i = p2 then drop_kid_rem_all x2 p1 x1 (rem_all p2 x2 cs)
      else ()
and rem_rem_comm_all (p1 x1 p2 x2:string) (ts:list tree)
  : Lemma (requires p1 <> p2)
          (ensures rem_all p1 x1 (rem_all p2 x2 ts) == rem_all p2 x2 (rem_all p1 x1 ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> rem_rem_comm p1 x1 p2 x2 t; rem_rem_comm_all p1 x1 p2 x2 r

let rec ins_rem_comm (q:string) (n:tree) (pid x:string) (t:tree)
  : Lemma (requires q <> pid /\ not (mem pid (ids n)))
          (ensures ins q n (rem_at pid x t) == rem_at pid x (ins q n t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      ins_rem_comm_all q n pid x cs;
      if i = q then (rem_all_app pid x (ins_all q n cs) [n]; Preservation.rem_absent pid x n)
      else if i = pid then drop_kid_ins_all x q n (rem_all pid x cs)
      else ()
and ins_rem_comm_all (q:string) (n:tree) (pid x:string) (ts:list tree)
  : Lemma (requires q <> pid /\ not (mem pid (ids n)))
          (ensures ins_all q n (rem_all pid x ts) == rem_all pid x (ins_all q n ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> ins_rem_comm q n pid x t; ins_rem_comm_all q n pid x r

let rec reorder_rem_comm (q:string) (o:list string) (pid x:string) (t:tree)
  : Lemma (requires q <> pid)
          (ensures reorder_at q o (rem_at pid x t) == rem_at pid x (reorder_at q o t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      reorder_rem_comm_all q o pid x cs;
      if i = q then arrange_rem o pid x (reorder_all q o cs)
      else if i = pid then drop_kid_reorder_all x q o (rem_all pid x cs)
      else ()
and reorder_rem_comm_all (q:string) (o:list string) (pid x:string) (ts:list tree)
  : Lemma (requires q <> pid)
          (ensures reorder_all q o (rem_all pid x ts) == rem_all pid x (reorder_all q o ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> reorder_rem_comm q o pid x t; reorder_rem_comm_all q o pid x r

let rec upd_rem_comm (y k pid x:string) (t:tree)
  : Lemma (ensures upd y k (rem_at pid x t) == rem_at pid x (upd y k t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      upd_rem_comm_all y k pid x cs;
      if i = pid then drop_kid_upd_all x y k (rem_all pid x cs) else ()
and upd_rem_comm_all (y k pid x:string) (ts:list tree)
  : Lemma (ensures upd_all y k (rem_all pid x ts) == rem_all pid x (upd_all y k ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> upd_rem_comm y k pid x t; upd_rem_comm_all y k pid x r

(* ---- what a removal leaves in place, under id uniqueness ---- *)

(* The children of the source parent, after the drop: under `wf` exactly one child carries `x`,
   it is the subtree `find_in` answers with, and dropping it is dropping that one element. *)
let rec find_all_drop (y x:string) (ts:list tree) (sub:tree)
  : Lemma (requires wf_all ts /\ has_kid x ts /\ find_all x ts == Some sub /\ not (mem y (ids sub)))
          (ensures find_all y (drop_kid x ts) == find_all y ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      if tid_of t = x then begin
        (* t is the subtree: y is not in it, so the search passes over it either way *)
        Preservation.find_in_self x t;
        find_in_some_iff y t;
        (* and no later sibling carries x *)
        inter_nil_iff (ids t) (ids_all r);
        drop_kid_absent x r
      end
      else begin
        find_in_some_iff x t;
        if mem x (ids t) then begin
          (* the kid is below t? no: has_kid says a CHILD carries x, and t does not, so x is in a
             later sibling — but wf says t and r share no id, so x is not in t *)
          inter_nil_iff (ids t) (ids_all r);
          has_kid_is_mem x r;
          kid_ids_sub x r
        end
        else find_all_drop y x r sub
      end
and drop_kid_absent (x:string) (ts:list tree)
  : Lemma (requires not (mem x (ids_all ts))) (ensures drop_kid x ts == ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> tid_in_ids t; drop_kid_absent x r
