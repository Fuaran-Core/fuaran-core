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

     `footprint_in_sound`: for every pair of operations — every skeleton op, `MoveNode` included,
     and every batch of them — whose base-aware footprints `independent` declares disjoint, and
     every well-formed tree at which both apply, each applies after the other and the two orders
     reach the same tree: `TreeOps.wstep`, the shape `tree_independence_diamond` has. Unlike the
     pure diamond, no pair here is closed without looking at the tree: the base-aware footprint
     frees exactly the pairs the unknown-parent clause used to refuse, so each of them is a
     commutation on the tree. Section 4 proves the IN-PLACE fragment (inserts, reorders,
     rewrites), section 5 lifts it along a batch's script as section 20 of `TreeOps.fst` lifts the
     pure diamond, sections 6 to 8 add the REMOVE pairs - a removal commutes with a removal at
     another parent, with an insert and a reorder elsewhere and with any rewrite, and leaves every
     id outside the destroyed subtree in place, found at the same node, under the same parent -
     and restate the lift over everything but a move (`no_move`), and sections 9 to 11 add the
     MOVE pairs and restate the lift over every operation. A move is two edits with a cycle check
     between them, and its base-aware footprint reads the destination's ancestors for that check:
     the five pairs it forms (with a rewrite, an insert, a reorder, a removal and a move) stand on
     each edit distributing over the graft at another parent, on the path to a node being kept by
     every edit that does not relocate it, and on what a move leaves in place - every id, every
     other node found at the same node with the move applied inside it, every parent but the
     target's. The lift carries ONE fact the pure lift did not need, because the pure footprint is
     a function of the op alone: that an operation's base-aware footprint is STABLE under the
     application of an operation it is independent of (`wstep_in`, `footprint_in_stable`), so what
     the fold read at the base is what holds at every state the fold reaches.

   WHAT IS NOT PROVED HERE. Nothing of the two theorems is left to the oracle: the `no_move`
   predicate of section 8 survives only as the name of the intermediate theorems
   (`footprint_in_sound_no_move`), as `in_place` does for sections 4 and 5. What stays outside
   is what every theorem of the pack leaves outside — the tree is the witness view, ids are keys,
   and the correspondence to production is the differential's and the extractor's.

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
   5. THE LIFT — the in-place fragment's diamond along a batch's script, and its theorem.

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

(* ---- THE THEOREM over the in-place fragment (section 8 restates it over everything but a move) ----

   Every pair of in-place operations — inserts, reorders, rewrites and batches of them — whose
   base-aware footprints at `t` are independent commutes at `t`, in the shape
   `tree_independence_diamond` has, and each one's footprint is unchanged by the other. *)
let footprint_in_sound_in_place (a b:op) (t:tree)
  : Lemma (requires wf t /\ in_place a /\ in_place b /\ independent (op_fp_in t a) (op_fp_in t b))
          (ensures wstep a b t)
  = any_vs_any_in a b t

(* The script form — what `Dag.conflictsIn` reads: two in-place scripts whose threaded footprints
   are independent at `t` apply in either order from `t` and reach the same tree. *)
let footprint_in_sound_in_place_scripts (a b:list op) (t:tree)
  : Lemma (requires wf t /\ in_place_all a /\ in_place_all b
                    /\ independent (fp_in_all t a) (fp_in_all t b))
          (ensures wstep (Batch a) (Batch b) t)
  = footprint_in_sound_in_place (Batch a) (Batch b) t

(* And the stability the N-lane fold stands on: after one of two independent in-place
   operations applies, the other's footprint — and so its independence from a third — is what it
   was at `t`. *)
let footprint_in_stable_in_place (a b:op) (t:tree) (ta:tree)
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

(* ======================================================================================
   7. WHAT A REMOVAL LEAVES IN PLACE, and what an insert and a reorder leave in place — the
      facts under id uniqueness: every id outside the destroyed subtree is still there, found at
      the same node (its own subtree edited), under the same parent; an insert elsewhere and a
      reorder anywhere change nobody's parent.
   ====================================================================================== *)

let rec has_kid_drop (y x:string) (ts:list tree)
  : Lemma (requires y <> x) (ensures has_kid y (drop_kid x ts) == has_kid y ts)
  = match ts with
    | [] -> ()
    | t :: r -> has_kid_drop y x r

let rec has_kid_rem_all (y pid x:string) (ts:list tree)
  : Lemma (ensures has_kid y (rem_all pid x ts) == has_kid y ts)
  = match ts with
    | [] -> ()
    | t :: r -> (match t with TNode _ _ _ -> has_kid_rem_all y pid x r)

let rec has_kid_ins_all (y q:string) (n:tree) (ts:list tree)
  : Lemma (ensures has_kid y (ins_all q n ts) == has_kid y ts)
  = match ts with
    | [] -> ()
    | t :: r -> (match t with TNode _ _ _ -> has_kid_ins_all y q n r)

let rec has_kid_app_last (y:string) (ts:list tree) (n:tree)
  : Lemma (requires tid_of n <> y) (ensures has_kid y (app ts [n]) == has_kid y ts)
  = match ts with
    | [] -> ()
    | _ :: r -> has_kid_app_last y r n

let rec parent_all_app_last (y:string) (ts:list tree) (n:tree)
  : Lemma (requires not (mem y (ids n))) (ensures parent_all y (app ts [n]) == parent_all y ts)
  = match ts with
    | [] -> Preservation.parent_absent y n
    | t :: r -> (match parent_of y t with Some _ -> () | None -> parent_all_app_last y r n)

(* ---- the source parent's children lose exactly one subtree ---- *)

let rec ids_drop_keeps (y x:string) (ts:list tree) (sub:tree)
  : Lemma (requires wf_all ts /\ has_kid x ts /\ find_all x ts == Some sub
                    /\ mem y (ids_all ts) /\ not (mem y (ids sub)))
          (ensures mem y (ids_all (drop_kid x ts))) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      if tid_of t = x then begin
        Preservation.find_in_self x t;
        inter_nil_iff (ids t) (ids_all r);
        drop_kid_absent x r
      end
      else begin
        find_in_some_iff x t;
        if mem x (ids t) then (inter_nil_iff (ids t) (ids_all r); has_kid_is_mem x r; kid_ids_sub x r)
        else if mem y (ids t) then ()
        else ids_drop_keeps y x r sub
      end

let rec parent_all_drop (y x:string) (ts:list tree) (sub:tree)
  : Lemma (requires wf_all ts /\ has_kid x ts /\ find_all x ts == Some sub /\ not (mem y (ids sub)))
          (ensures parent_all y (drop_kid x ts) == parent_all y ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      if tid_of t = x then begin
        Preservation.find_in_self x t;
        Preservation.parent_absent y t;
        inter_nil_iff (ids t) (ids_all r);
        drop_kid_absent x r
      end
      else begin
        find_in_some_iff x t;
        if mem x (ids t) then (inter_nil_iff (ids t) (ids_all r); has_kid_is_mem x r; kid_ids_sub x r)
        else parent_all_drop y x r sub
      end

(* ---- every id outside the destroyed subtree survives a removal ---- *)

let rec rem_keeps_id (y pid x:string) (t:tree) (sub:tree)
  : Lemma (requires wf t /\ tid_of t <> x /\ find_in x t == Some sub /\ parent_of x t == Some pid
                    /\ mem y (ids t) /\ not (mem y (ids sub)))
          (ensures mem y (ids (rem_at pid x t))) (decreases t)
  = match t with
    | TNode i _ cs ->
      if has_kid x cs then begin
        Preservation.rem_all_absent pid x cs;
        if i = y then () else ids_drop_keeps y x cs sub
      end
      else begin
        Preservation.parent_all_mem x cs pid;
        if i = y then () else rem_keeps_id_all y pid x cs sub
      end
and rem_keeps_id_all (y pid x:string) (ts:list tree) (sub:tree)
  : Lemma (requires wf_all ts /\ not (has_kid x ts) /\ find_all x ts == Some sub
                    /\ parent_all x ts == Some pid /\ mem y (ids_all ts) /\ not (mem y (ids sub)))
          (ensures mem y (ids_all (rem_all pid x ts))) (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r ->
      find_in_some_iff x c;
      if mem x (ids c) then begin
        Preservation.parent_exists x c;
        parent_of_ids x pid c;
        inter_nil_iff (ids c) (ids_all r);
        Preservation.rem_all_absent pid x r;
        if mem y (ids c) then rem_keeps_id y pid x c sub else ()
      end
      else begin
        Preservation.parent_absent x c;
        Preservation.parent_all_mem x r pid;
        inter_nil_iff (ids c) (ids_all r);
        Preservation.rem_absent pid x c;
        if mem y (ids c) then () else rem_keeps_id_all y pid x r sub
      end

(* ---- and keeps its parent ---- *)

let rec parent_rem (y pid x:string) (t:tree) (sub:tree)
  : Lemma (requires wf t /\ tid_of t <> x /\ find_in x t == Some sub /\ parent_of x t == Some pid
                    /\ not (mem y (ids sub)))
          (ensures parent_of y (rem_at pid x t) == parent_of y t) (decreases t)
  = find_in_id x t sub;
    match t with
    | TNode i _ cs ->
      if has_kid x cs then begin
        Preservation.rem_all_absent pid x cs;
        has_kid_drop y x cs;
        if has_kid y cs then () else parent_all_drop y x cs sub
      end
      else begin
        Preservation.parent_all_mem x cs pid;
        has_kid_rem_all y pid x cs;
        if has_kid y cs then () else parent_rem_all y pid x cs sub
      end
and parent_rem_all (y pid x:string) (ts:list tree) (sub:tree)
  : Lemma (requires wf_all ts /\ not (has_kid x ts) /\ find_all x ts == Some sub
                    /\ parent_all x ts == Some pid /\ not (mem y (ids sub)))
          (ensures parent_all y (rem_all pid x ts) == parent_all y ts) (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r ->
      find_in_some_iff x c;
      if mem x (ids c) then begin
        Preservation.parent_exists x c;
        parent_of_ids x pid c;
        inter_nil_iff (ids c) (ids_all r);
        Preservation.rem_all_absent pid x r;
        parent_rem y pid x c sub
      end
      else begin
        Preservation.parent_absent x c;
        Preservation.parent_all_mem x r pid;
        inter_nil_iff (ids c) (ids_all r);
        Preservation.rem_absent pid x c;
        parent_rem_all y pid x r sub
      end

(* ---- an insert elsewhere changes nobody's parent ---- *)

let rec parent_ins (y q:string) (n:tree) (t:tree)
  : Lemma (requires not (mem y (ids n))) (ensures parent_of y (ins q n t) == parent_of y t) (decreases t)
  = match t with
    | TNode i _ cs ->
      parent_ins_all y q n cs;
      has_kid_ins_all y q n cs;
      if i = q then begin
        tid_in_ids n;
        has_kid_app_last y (ins_all q n cs) n;
        parent_all_app_last y (ins_all q n cs) n
      end
      else ()
and parent_ins_all (y q:string) (n:tree) (ts:list tree)
  : Lemma (requires not (mem y (ids n))) (ensures parent_all y (ins_all q n ts) == parent_all y ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> parent_ins y q n t; parent_ins_all y q n r

(* ---- and a reorder changes nobody's parent ---- *)

(* Under uniqueness, with no child rooted at `y`, the parent is found in the one child that holds
   `y` — so the list's order does not matter. *)
let rec parent_all_locate (y:string) (ts:list tree)
  : Lemma (requires wf_all ts /\ not (has_kid y ts))
          (ensures parent_all y ts == (match locate y ts with None -> None | Some c -> parent_of y c))
          (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      if mem y (ids t) then Preservation.parent_exists y t
      else (Preservation.parent_absent y t; parent_all_locate y r)

let parent_all_order_free (y:string) (l1 l2:list tree)
  : Lemma (requires wf_all l1 /\ wf_all l2 /\ not (has_kid y l1) /\ not (has_kid y l2)
                    /\ (forall (c:tree). mem c l1 == mem c l2))
          (ensures parent_all y l1 == parent_all y l2)
  = parent_all_locate y l1;
    parent_all_locate y l2;
    wf_all_holder_unique y l1;
    locate_order_free y l1 l2

let rec parent_reorder (y q:string) (o:list string) (t:tree)
  : Lemma (requires wf t /\ reorder_ok q o t)
          (ensures parent_of y (reorder_at q o t) == parent_of y t) (decreases t)
  = match t with
    | TNode i _ cs ->
      parent_reorder_all y q o cs;
      let cs' = reorder_all q o cs in
      has_kid_reorder_all y q o cs;
      if i = q then begin
        (* the children are the same set in another order *)
        reorder_all_wf q o cs;
        kid_ids_reorder_all q o cs;
        wf_all_kid_ids_no_dups cs';
        same_multiset_mem (kid_ids cs') o;
        arrange_elems o cs';
        Preservation.arrange_kid_ids o cs';
        has_kid_is_mem y (arrange o cs');
        has_kid_is_mem y cs';
        if has_kid y cs' then ()
        else begin
          same_multiset_no_dups (kid_ids cs') o;
          arrange_wf_all o cs';
          parent_all_order_free y (arrange o cs') cs'
        end
      end
      else ()
and parent_reorder_all (y q:string) (o:list string) (ts:list tree)
  : Lemma (requires wf_all ts /\ reorder_ok_all q o ts)
          (ensures parent_all y (reorder_all q o ts) == parent_all y ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> parent_reorder y q o t; parent_reorder_all y q o r
and has_kid_reorder_all (y q:string) (o:list string) (ts:list tree)
  : Lemma (ensures has_kid y (reorder_all q o ts) == has_kid y ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> (match t with TNode _ _ _ -> has_kid_reorder_all y q o r)
and reorder_all_wf (q:string) (o:list string) (ts:list tree)
  : Lemma (requires wf_all ts /\ reorder_ok_all q o ts) (ensures wf_all (reorder_all q o ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      reorder_wf q o t;
      reorder_all_wf q o r;
      ids_reorder_mem_fa q o t;
      ids_reorder_mem_all_fa q o r;
      inter_nil_iff (ids t) (ids_all r);
      inter_nil_iff (ids (reorder_at q o t)) (ids_all (reorder_all q o r))

(* ======================================================================================
   8. THE REMOVE PAIRS — a removal beside a removal at another parent, an insert elsewhere, a
      reorder elsewhere and any rewrite, each in `wstep_in`'s shape; then the fragment widened
      to everything but a move (`no_move`), and the lift and the theorem restated over it.
   ====================================================================================== *)

(* A present removal's footprint, and an absent one's. *)
let remove_fp_in (t:tree) (x:string)
  : Lemma (ensures op_fp_in t (RemoveNode x) ==
                   (match find_in x t, parent_of x t with
                    | Some sub, Some p ->
                      { reads = [x; p]; structure_writes = [p]; content_writes = ids sub;
                        unknown_parent_writes = []; slot_reads = []; slot_writes = [] }
                    | _, _ -> op_fp (RemoveNode x)))
  = ()

(* ---- CASE R/R ---- *)

let remove_remove_in (x1 x2:string) (t:tree)
  : Lemma (requires wf t /\ independent (op_fp_in t (RemoveNode x1)) (op_fp_in t (RemoveNode x2)))
          (ensures wstep_in (RemoveNode x1) (RemoveNode x2) t)
  = if Ok? (apply (RemoveNode x1) t) && Ok? (apply (RemoveNode x2) t) then begin
      match find_in x1 t, parent_of x1 t, find_in x2 t, parent_of x2 t with
      | Some s1, Some p1, Some s2, Some p2 ->
        inter_nil_iff (ids s1) (ids s2);
        inter_nil_iff (ids s1) [x2; p2];
        inter_nil_iff (ids s2) [x1; p1];
        inter_nil_iff [p1] [p2];
        find_in_id x1 t s1; find_in_id x2 t s2;
        tid_in_ids s1; tid_in_ids s2;
        rem_keeps_id x2 p1 x1 t s1;
        parent_rem x2 p1 x1 t s1;
        Preservation.find_rem x2 p1 x1 t;
        Preservation.rem_absent p1 x1 s2;
        Preservation.tid_rem p1 x1 t;
        rem_keeps_id x1 p2 x2 t s2;
        parent_rem x1 p2 x2 t s2;
        Preservation.find_rem x1 p2 x2 t;
        Preservation.rem_absent p2 x2 s1;
        Preservation.tid_rem p2 x2 t;
        rem_rem_comm p1 x1 p2 x2 t
      | _ -> ()
    end
    else ()

(* ---- CASE I/R ---- *)

let insert_remove_in (q:string) (n:tree) (x:string) (t:tree)
  : Lemma (requires wf t /\ independent (op_fp_in t (InsertChild q n)) (op_fp_in t (RemoveNode x)))
          (ensures wstep_in (InsertChild q n) (RemoveNode x) t)
  = if Ok? (apply (InsertChild q n) t) && Ok? (apply (RemoveNode x) t) then begin
      match find_in x t, parent_of x t with
      | Some sub, Some p ->
        inter_nil_iff (q :: ids n) (ids sub);
        inter_nil_iff (ids n) [x; p];
        inter_nil_iff [q] [p];
        find_in_id x t sub; tid_in_ids sub;
        first_dup_none_iff n t;
        inter_nil_iff (ids n) (ids t);
        let tr = rem_at p x t in
        rem_keeps_id q p x t sub;
        Preservation.ids_rem_sub_fa p x t;
        inter_nil_iff (ids n) (ids tr);
        first_dup_none_iff n tr;
        ids_ins_mem x q n t;
        parent_ins x q n t;
        find_ins x q n t;
        ins_absent q n sub;
        ins_rem_comm q n p x t
      | _ -> ()
    end
    else ()

(* ---- CASE O/R ---- *)

let reorder_remove_in (q:string) (o:list string) (x:string) (t:tree)
  : Lemma (requires wf t /\ independent (op_fp_in t (ReorderChildren q o)) (op_fp_in t (RemoveNode x)))
          (ensures wstep_in (ReorderChildren q o) (RemoveNode x) t)
  = if Ok? (apply (ReorderChildren q o) t) && Ok? (apply (RemoveNode x) t) then begin
      match find_in x t, parent_of x t, find_in q t with
      | Some sub, Some p, Some m ->
        inter_nil_iff (q :: o) (ids sub);
        inter_nil_iff [q] [p];
        find_in_id x t sub; tid_in_ids sub; find_in_id q t m;
        find_in_some_iff q t;
        wf_reorder_ok q o t;
        rem_keeps_id q p x t sub;
        Preservation.find_rem q p x t;
        (match m with TNode _ _ cs -> Preservation.kid_ids_rem_all p x cs);
        Preservation.tid_rem p x t;
        tid_reorder q o t;
        ids_reorder_mem x q o t;
        parent_reorder x q o t;
        find_reorder x q o t;
        reorder_absent q o sub;
        reorder_rem_comm q o p x t
      | _ -> ()
    end
    else ()

(* ---- CASE U/R ---- *)

let update_remove_in (n:tree) (x:string) (t:tree)
  : Lemma (requires wf t /\ independent (op_fp_in t (UpdateNode n)) (op_fp_in t (RemoveNode x)))
          (ensures wstep_in (UpdateNode n) (RemoveNode x) t)
  = let y = tid_of n in
    let k = kind_of n in
    update_fp_in t n;
    find_in_some_iff y t;
    if not (has_id y t) then ()
    else if not (Ok? (apply (RemoveNode x) t)) then ()
    else begin
      find_in_some_iff x t;
      match find_in x t, parent_of x t with
      | Some sub, Some p ->
        inter_nil_iff [y] (ids sub);
        inter_nil_iff [y] [x; p];
        find_in_id x t sub; tid_in_ids sub;
        rem_keeps_id y p x t sub;
        find_in_some_iff y (rem_at p x t);
        Preservation.tid_rem p x t;
        Preservation.tid_upd y k t;
        Preservation.ids_upd y k t;
        Preservation.parent_upd x y k t;
        Preservation.find_upd x y k t;
        Preservation.upd_absent y k sub;
        upd_rem_comm y k p x t
      | _ -> ()
    end

(* ---- the fragment widened: everything but a move ---- *)

let rec no_move (o:op) : Tot bool (decreases o) =
  match o with
  | MoveNode _ _ -> false
  | Batch os -> no_move_all os
  | _ -> true
and no_move_all (os:list op) : Tot bool (decreases os) =
  match os with
  | [] -> true
  | o :: r -> no_move o && no_move_all r

let rec in_place_no_move (o:op)
  : Lemma (requires in_place o) (ensures no_move o) (decreases o)
  = match o with
    | Batch os -> in_place_no_move_all os
    | _ -> ()
and in_place_no_move_all (os:list op)
  : Lemma (requires in_place_all os) (ensures no_move_all os) (decreases os)
  = match os with
    | [] -> ()
    | o :: r -> in_place_no_move o; in_place_no_move_all r

(* ---- every leaf pair of the widened fragment ---- *)

let no_move_leaf_in (a b:op) (t:tree)
  : Lemma (requires wf t /\ is_leaf a /\ is_leaf b /\ no_move a /\ no_move b
                    /\ independent (op_fp_in t a) (op_fp_in t b))
          (ensures wstep_in a b t)
  = match a, b with
    | RemoveNode x1, RemoveNode x2 -> remove_remove_in x1 x2 t
    | InsertChild q n, RemoveNode x -> insert_remove_in q n x t
    | RemoveNode x, InsertChild q n ->
      independent_sym (op_fp_in t a) (op_fp_in t b); insert_remove_in q n x t; wstep_in_sym b a t
    | ReorderChildren q o, RemoveNode x -> reorder_remove_in q o x t
    | RemoveNode x, ReorderChildren q o ->
      independent_sym (op_fp_in t a) (op_fp_in t b); reorder_remove_in q o x t; wstep_in_sym b a t
    | UpdateNode n, RemoveNode x -> update_remove_in n x t
    | RemoveNode x, UpdateNode n ->
      independent_sym (op_fp_in t a) (op_fp_in t b); update_remove_in n x t; wstep_in_sym b a t
    | _, _ -> in_place_leaf_in a b t

(* ---- the lift again, over the widened fragment ---- *)

let rec leaf_vs_any_nm (a b:op) (t:tree)
  : Lemma (requires wf t /\ is_leaf a /\ no_move a /\ no_move b
                    /\ independent (op_fp_in t a) (op_fp_in t b))
          (ensures wstep_in a b t) (decreases b)
  = match b with
    | Batch os -> leaf_vs_script_nm a os t
    | _ -> no_move_leaf_in a b t
and leaf_vs_script_nm (a:op) (os:list op) (t:tree)
  : Lemma (requires wf t /\ is_leaf a /\ no_move a /\ no_move_all os
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
         leaf_vs_any_nm a b1 t;
         Preservation.apply_preserves_wf b1 t;
         (match apply a t with
          | Error _ -> ()
          | Ok ta ->
            leaf_vs_script_nm a bs t1;
            (match apply (Batch bs) t1 with
             | Error _ -> ()
             | Ok tb ->
               (match apply b1 ta with
                | Ok ta1 -> fp_in_cons ta b1 bs ta1
                | Error _ -> ()))))

let rec any_vs_any_nm (a b:op) (t:tree)
  : Lemma (requires wf t /\ no_move a /\ no_move b /\ independent (op_fp_in t a) (op_fp_in t b))
          (ensures wstep_in a b t) (decreases a)
  = match a with
    | Batch os -> script_vs_any_nm os b t
    | _ -> leaf_vs_any_nm a b t
and script_vs_any_nm (os:list op) (b:op) (t:tree)
  : Lemma (requires wf t /\ no_move_all os /\ no_move b
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
         any_vs_any_nm o b t;
         Preservation.apply_preserves_wf o t;
         (match apply b t with
          | Error _ -> ()
          | Ok tb ->
            script_vs_any_nm r b to;
            (match apply (Batch r) to with
             | Error _ -> ()
             | Ok ta ->
               (match apply o tb with
                | Ok tb1 -> fp_in_cons tb o r tb1
                | Error _ -> ()))))

(* ---- THE THEOREM over everything but a move (section 11 restates it over every operation) ---- *)

let footprint_in_sound_no_move (a b:op) (t:tree)
  : Lemma (requires wf t /\ no_move a /\ no_move b /\ independent (op_fp_in t a) (op_fp_in t b))
          (ensures wstep a b t)
  = any_vs_any_nm a b t

let footprint_in_sound_no_move_scripts (a b:list op) (t:tree)
  : Lemma (requires wf t /\ no_move_all a /\ no_move_all b
                    /\ independent (fp_in_all t a) (fp_in_all t b))
          (ensures wstep (Batch a) (Batch b) t)
  = footprint_in_sound_no_move (Batch a) (Batch b) t

let footprint_in_stable_no_move (a b:op) (t:tree) (ta:tree)
  : Lemma (requires wf t /\ no_move a /\ no_move b /\ independent (op_fp_in t a) (op_fp_in t b)
                    /\ apply a t == Ok ta /\ Ok? (apply b t))
          (ensures op_fp_in ta b == op_fp_in t b)
  = any_vs_any_nm a b t

(* ======================================================================================
   9. THE MOVE FAMILY — the tree facts a move's pairs stand on.

      A move is two edits: the removal of the target from its source parent, then the graft of
      the removed subtree under the destination (`ins np sub (rem_at p x t)`). The facts come in
      three kinds: each edit DISTRIBUTES over a graft whose parent is not the edit's own anchor,
      so an edit made after a move is the move of the edit (unconditional, list by list); the
      PATH to a node is unchanged by any edit that does not relocate it, which is what keeps the
      ancestors a move's footprint reads where they were; and what a move leaves in place — every
      id, every node outside the moved subtree found at the same node, every parent but the
      target's — which needs id uniqueness, as section 7's facts did.
   ====================================================================================== *)

(* ---- each edit distributes over a graft at another parent ---- *)

let rec ins_ins_gen (q:string) (g:tree) (np:string) (s:tree) (r:tree)
  : Lemma (requires q <> np /\ not (mem np (ids g)))
          (ensures ins q g (ins np s r) == ins np (ins q g s) (ins q g r)) (decreases r)
  = match r with
    | TNode i _ cs ->
      ins_ins_gen_all q g np s cs;
      if i = np then ins_all_app q g (ins_all np s cs) [s]
      else if i = q then (ins_all_app np (ins q g s) (ins_all q g cs) [g]; ins_absent np (ins q g s) g)
      else ()
and ins_ins_gen_all (q:string) (g:tree) (np:string) (s:tree) (ts:list tree)
  : Lemma (requires q <> np /\ not (mem np (ids g)))
          (ensures ins_all q g (ins_all np s ts) == ins_all np (ins q g s) (ins_all q g ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: rr -> ins_ins_gen q g np s t; ins_ins_gen_all q g np s rr

let rec reorder_ins_gen (q:string) (o:list string) (np:string) (s:tree) (r:tree)
  : Lemma (requires q <> np)
          (ensures reorder_at q o (ins np s r) == ins np (reorder_at q o s) (reorder_at q o r)) (decreases r)
  = match r with
    | TNode i _ cs ->
      reorder_ins_gen_all q o np s cs;
      if i = np then reorder_all_app q o (ins_all np s cs) [s]
      else if i = q then arrange_ins o np (reorder_at q o s) (reorder_all q o cs)
      else ()
and reorder_ins_gen_all (q:string) (o:list string) (np:string) (s:tree) (ts:list tree)
  : Lemma (requires q <> np)
          (ensures reorder_all q o (ins_all np s ts) == ins_all np (reorder_at q o s) (reorder_all q o ts))
          (decreases ts)
  = match ts with
    | [] -> ()
    | t :: rr -> reorder_ins_gen q o np s t; reorder_ins_gen_all q o np s rr

let rec rem_ins_gen (py y:string) (np:string) (s:tree) (r:tree)
  : Lemma (requires py <> np)
          (ensures rem_at py y (ins np s r) == ins np (rem_at py y s) (rem_at py y r)) (decreases r)
  = match r with
    | TNode i _ cs ->
      rem_ins_gen_all py y np s cs;
      if i = np then rem_all_app py y (ins_all np s cs) [s]
      else if i = py then drop_kid_ins_all y np (rem_at py y s) (rem_all py y cs)
      else ()
and rem_ins_gen_all (py y:string) (np:string) (s:tree) (ts:list tree)
  : Lemma (requires py <> np)
          (ensures rem_all py y (ins_all np s ts) == ins_all np (rem_at py y s) (rem_all py y ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: rr -> rem_ins_gen py y np s t; rem_ins_gen_all py y np s rr

let rec upd_ins_gen (y k:string) (np:string) (s:tree) (r:tree)
  : Lemma (ensures upd y k (ins np s r) == ins np (upd y k s) (upd y k r)) (decreases r)
  = match r with
    | TNode i _ cs ->
      upd_ins_gen_all y k np s cs;
      if i = np then upd_all_app y k (ins_all np s cs) [s] else ()
and upd_ins_gen_all (y k:string) (np:string) (s:tree) (ts:list tree)
  : Lemma (ensures upd_all y k (ins_all np s ts) == ins_all np (upd y k s) (upd_all y k ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: rr -> upd_ins_gen y k np s t; upd_ins_gen_all y k np s rr

(* ---- the path to a node: found exactly where the node is, never empty, order-free ---- *)

let rec path_some_iff (z:string) (t:tree)
  : Lemma (ensures Some? (ix_path_walk z t) == mem z (ids t)) (decreases t)
  = match t with TNode i _ cs -> if i = z then () else path_some_iff_all z cs
and path_some_iff_all (z:string) (ts:list tree)
  : Lemma (ensures Some? (ix_path_walk_all z ts) == mem z (ids_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> path_some_iff z t; path_some_iff_all z r

let rec path_cons (z:string) (t:tree)
  : Lemma (ensures (match ix_path_walk z t with Some l -> Cons? l | None -> True)) (decreases t)
  = match t with TNode i _ cs -> if i = z then () else path_cons_all z cs
and path_cons_all (z:string) (ts:list tree)
  : Lemma (ensures (match ix_path_walk_all z ts with Some l -> Cons? l | None -> True)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> path_cons z t; path_cons_all z r

let rec path_all_app (z:string) (l m:list tree)
  : Lemma (ensures ix_path_walk_all z (app l m) == (match ix_path_walk_all z l with
                                                   | Some p -> Some p
                                                   | None -> ix_path_walk_all z m)) (decreases l)
  = match l with
    | [] -> ()
    | t :: r -> (match ix_path_walk z t with Some _ -> () | None -> path_all_app z r m)

let rec path_all_locate (z:string) (ts:list tree)
  : Lemma (ensures ix_path_walk_all z ts == (match locate z ts with
                                             | None -> None
                                             | Some c -> ix_path_walk z c)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> path_some_iff z t; path_all_locate z r

let path_all_order_free (z:string) (l1 l2:list tree)
  : Lemma (requires wf_all l1 /\ (forall (c:tree). mem c l1 == mem c l2))
          (ensures ix_path_walk_all z l1 == ix_path_walk_all z l2)
  = path_all_locate z l1;
    path_all_locate z l2;
    wf_all_holder_unique z l1;
    locate_order_free z l1 l2

(* ---- the path through a rewrite, a graft elsewhere, a removal elsewhere and a reorder ---- *)

let rec path_upd (z y k:string) (t:tree)
  : Lemma (ensures ix_path_walk z (upd y k t) == ix_path_walk z t) (decreases t)
  = match t with TNode _ _ cs -> path_upd_all z y k cs
and path_upd_all (z y k:string) (ts:list tree)
  : Lemma (ensures ix_path_walk_all z (upd_all y k ts) == ix_path_walk_all z ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> path_upd z y k t; path_upd_all z y k r

let rec path_ins (z q:string) (g:tree) (t:tree)
  : Lemma (requires not (mem z (ids g)))
          (ensures ix_path_walk z (ins q g t) == ix_path_walk z t) (decreases t)
  = match t with
    | TNode i _ cs ->
      path_ins_all z q g cs;
      if i = q then (path_all_app z (ins_all q g cs) [g]; path_some_iff z g) else ()
and path_ins_all (z q:string) (g:tree) (ts:list tree)
  : Lemma (requires not (mem z (ids g)))
          (ensures ix_path_walk_all z (ins_all q g ts) == ix_path_walk_all z ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> path_ins z q g t; path_ins_all z q g r

let rec path_all_drop (z x:string) (ts:list tree) (sub:tree)
  : Lemma (requires wf_all ts /\ has_kid x ts /\ find_all x ts == Some sub /\ not (mem z (ids sub)))
          (ensures ix_path_walk_all z (drop_kid x ts) == ix_path_walk_all z ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      if tid_of t = x then begin
        Preservation.find_in_self x t;
        path_some_iff z t;
        inter_nil_iff (ids t) (ids_all r);
        drop_kid_absent x r
      end
      else begin
        find_in_some_iff x t;
        if mem x (ids t) then (inter_nil_iff (ids t) (ids_all r); has_kid_is_mem x r; kid_ids_sub x r)
        else path_all_drop z x r sub
      end

let rec path_rem (z pid x:string) (t:tree) (sub:tree)
  : Lemma (requires wf t /\ tid_of t <> x /\ find_in x t == Some sub /\ parent_of x t == Some pid
                    /\ not (mem z (ids sub)))
          (ensures ix_path_walk z (rem_at pid x t) == ix_path_walk z t) (decreases t)
  = match t with
    | TNode i _ cs ->
      if has_kid x cs then begin
        Preservation.rem_all_absent pid x cs;
        path_all_drop z x cs sub
      end
      else begin
        Preservation.parent_all_mem x cs pid;
        path_rem_all z pid x cs sub
      end
and path_rem_all (z pid x:string) (ts:list tree) (sub:tree)
  : Lemma (requires wf_all ts /\ not (has_kid x ts) /\ find_all x ts == Some sub
                    /\ parent_all x ts == Some pid /\ not (mem z (ids sub)))
          (ensures ix_path_walk_all z (rem_all pid x ts) == ix_path_walk_all z ts) (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r ->
      find_in_some_iff x c;
      if mem x (ids c) then begin
        Preservation.parent_exists x c;
        parent_of_ids x pid c;
        inter_nil_iff (ids c) (ids_all r);
        Preservation.rem_all_absent pid x r;
        path_rem z pid x c sub
      end
      else begin
        Preservation.parent_absent x c;
        Preservation.parent_all_mem x r pid;
        inter_nil_iff (ids c) (ids_all r);
        Preservation.rem_absent pid x c;
        path_rem_all z pid x r sub
      end

let rec path_reorder (z q:string) (o:list string) (t:tree)
  : Lemma (requires wf t /\ reorder_ok q o t)
          (ensures ix_path_walk z (reorder_at q o t) == ix_path_walk z t) (decreases t)
  = match t with
    | TNode i _ cs ->
      path_reorder_all z q o cs;
      if i = q then begin
        let cs' = reorder_all q o cs in
        reorder_all_wf q o cs;
        kid_ids_reorder_all q o cs;
        wf_all_kid_ids_no_dups cs';
        arrange_elems o cs';
        same_multiset_no_dups (kid_ids cs') o;
        arrange_wf_all o cs';
        path_all_order_free z (arrange o cs') cs'
      end
      else ()
and path_reorder_all (z q:string) (o:list string) (ts:list tree)
  : Lemma (requires wf_all ts /\ reorder_ok_all q o ts)
          (ensures ix_path_walk_all z (reorder_all q o ts) == ix_path_walk_all z ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> path_reorder z q o t; path_reorder_all z q o r

(* ---- the ancestors of a node BELOW a found subtree include the subtree's root ---- *)

let rec anc_through (x z:string) (t:tree) (sub:tree)
  : Lemma (requires wf t /\ find_in x t == Some sub /\ mem z (ids sub) /\ z <> x)
          (ensures (match ix_path_walk z t with Some l -> mem x (ix_init l) | None -> False)) (decreases t)
  = match t with
    | TNode i _ cs ->
      if i = x then begin
        path_some_iff_all z cs;
        path_cons_all z cs
      end
      else begin
        find_all_sub x z cs sub;
        anc_through_all x z cs sub;
        path_cons_all z cs
      end
and anc_through_all (x z:string) (ts:list tree) (sub:tree)
  : Lemma (requires wf_all ts /\ find_all x ts == Some sub /\ mem z (ids sub) /\ z <> x)
          (ensures (match ix_path_walk_all z ts with Some l -> mem x (ix_init l) | None -> False))
          (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r ->
      (match find_in x c with
       | Some _ -> anc_through x z c sub
       | None ->
         find_all_sub x z r sub;
         inter_nil_iff (ids c) (ids_all r);
         path_some_iff z c;
         anc_through_all x z r sub)

let anc_of_through (x z:string) (t:tree) (sub:tree)
  : Lemma (requires wf t /\ find_in x t == Some sub /\ mem z (ids sub) /\ z <> x)
          (ensures mem x (anc_of z t))
  = anc_through x z t sub

(* ---- a parent is a node one of whose children carries the id (the converse of
   `Preservation.parent_kids`) ---- *)

let rec parent_of_kids (z q:string) (t:tree)
  : Lemma (requires wf t /\ parent_of z t == Some q)
          (ensures mem z (Preservation.kids_at q t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      if has_kid z cs then Preservation.has_kid_mem z cs
      else begin
        Preservation.parent_all_mem z cs q;
        parent_of_kids_all z q cs
      end
and parent_of_kids_all (z q:string) (ts:list tree)
  : Lemma (requires wf_all ts /\ parent_all z ts == Some q)
          (ensures (match find_all q ts with Some n -> mem z (kid_ids (kids_of n)) | None -> False))
          (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r ->
      (match parent_of z c with
       | Some _ -> parent_of_kids z q c; Preservation.parent_of_mem z c q; find_in_some_iff q c
       | None ->
         Preservation.parent_all_mem z r q;
         inter_nil_iff (ids c) (ids_all r);
         find_in_some_iff q c;
         parent_of_kids_all z q r)

(* A reorder accepted at the tree is accepted at every found subtree. *)
let rec reorder_ok_sub (x q:string) (o:list string) (t:tree) (sub:tree)
  : Lemma (requires reorder_ok q o t /\ find_in x t == Some sub) (ensures reorder_ok q o sub) (decreases t)
  = match t with
    | TNode i _ cs -> if i = x then () else reorder_ok_sub_all x q o cs sub
and reorder_ok_sub_all (x q:string) (o:list string) (ts:list tree) (sub:tree)
  : Lemma (requires reorder_ok_all q o ts /\ find_all x ts == Some sub) (ensures reorder_ok q o sub)
          (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r -> (match find_in x c with
                 | Some _ -> reorder_ok_sub x q o c sub
                 | None -> reorder_ok_sub_all x q o r sub)

(* ---- what a move leaves in place ---- *)

(* The premise every fact below shares: the move applies at `t`, with `sub` its subtree and `p`
   its source parent (`Preservation.move_accepted` is the bridge from `apply`). *)
let moves (x np:string) (t:tree) (sub:tree) (p:string) : prop =
  wf t /\ tid_of t <> x /\ find_in x t == Some sub /\ parent_of x t == Some p
  /\ mem np (ids t) /\ not (mem np (ids sub))

let move_applies (x np:string) (t sub:tree) (p:string)
  : Lemma (requires moves x np t sub p)
          (ensures apply (MoveNode x np) t == Ok (ins np sub (rem_at p x t))
                   /\ wf (ins np sub (rem_at p x t))
                   /\ tid_of (ins np sub (rem_at p x t)) == tid_of t)
  = find_in_some_iff x t;
    Preservation.move_accepted x np t;
    Preservation.apply_preserves_wf (MoveNode x np) t;
    tid_ins np sub (rem_at p x t);
    Preservation.tid_rem p x t

(* The source parent is not inside the subtree it parents. *)
let parent_outside (x:string) (t sub:tree) (p:string)
  : Lemma (requires wf t /\ find_in x t == Some sub /\ parent_of x t == Some p)
          (ensures not (mem p (ids sub)))
  = Preservation.rem_keeps_parent p x t;
    Preservation.rem_kills p x t sub

(* The ids of a grafted-and-pruned node come from the node or the graft. *)
let graft_ids (np:string) (s:tree) (p x:string) (m:tree) (y:string)
  : Lemma (requires mem y (ids (ins np s (rem_at p x m)))) (ensures mem y (ids m) \/ mem y (ids s))
  = ids_ins_mem y np s (rem_at p x m);
    Preservation.ids_rem_sub y p x m

(* Every other node is found after the move, with the move applied to its own subtree. *)
let move_find (x np:string) (t sub:tree) (p:string) (z:string) (m:tree)
  : Lemma (requires moves x np t sub p /\ z <> x /\ find_in z t == Some m)
          (ensures find_in z (ins np sub (rem_at p x t)) == Some (ins np sub (rem_at p x m)))
  = let r = rem_at p x t in
    Preservation.rem_wf p x t;
    Preservation.rem_survives np p x t sub;
    find_in_some_iff z t;
    if mem z (ids sub) then begin
      Preservation.rem_kills p x t sub;
      Preservation.find_ins_inside z np sub r;
      Preservation.find_in_trans x z t sub;
      parent_outside x t sub p;
      (if mem p (ids m) then find_in_sub z p sub m else ());
      (if mem np (ids m) then find_in_sub z np sub m else ());
      Preservation.rem_absent p x m;
      ins_absent np sub m
    end
    else begin
      Preservation.rem_survives z p x t sub;
      Preservation.find_rem z p x t;
      find_ins z np sub r
    end

(* Every node but the target keeps its parent. *)
let move_parent (x np:string) (t sub:tree) (p:string) (z:string)
  : Lemma (requires moves x np t sub p /\ z <> x /\ mem z (ids t) /\ tid_of t <> z)
          (ensures parent_of z (ins np sub (rem_at p x t)) == parent_of z t)
  = let tm = ins np sub (rem_at p x t) in
    move_applies x np t sub p;
    Preservation.parent_exists z t;
    match parent_of z t with
    | Some q ->
      parent_of_kids z q t;
      Preservation.parent_of_mem z t q;
      Preservation.move_view x np t p sub q;
      Preservation.drop_id_mem z x (Preservation.kids_at q t);
      mem_app z (Preservation.drop_id x (Preservation.kids_at q t)) (if q = np then [x] else []);
      (match find_in q tm with
       | Some n -> Preservation.parent_kids q z tm n
       | None -> ())
    | None -> ()

(* And the path to a node outside the moved subtree is what it was: the removal keeps it (the
   node is not in the destroyed subtree) and the graft keeps it (the node is not in the graft). *)
let move_path (x np:string) (t sub:tree) (p:string) (z:string)
  : Lemma (requires moves x np t sub p /\ not (mem z (ids sub)))
          (ensures ix_path_walk z (ins np sub (rem_at p x t)) == ix_path_walk z t)
  = path_rem z p x t sub;
    path_ins z np sub (rem_at p x t)

(* Every id of the tree survives a move, quantified. *)
let move_ids_fa (x np:string) (t sub:tree) (p:string)
  : Lemma (requires moves x np t sub p)
          (ensures forall (y:string). mem y (ids (ins np sub (rem_at p x t))) == mem y (ids t))
  = let aux (y:string) : Lemma (mem y (ids (ins np sub (rem_at p x t))) == mem y (ids t))
      = Preservation.move_ids x np t p sub y
    in
    FStar.Classical.forall_intro aux

(* What an accepted move says, as the premise section 9's facts take. *)
let move_ok (x np:string) (t:tree) (sub:tree) (p:string)
  : Lemma (requires wf t /\ Ok? (apply (MoveNode x np) t) /\ find_in x t == Some sub /\ parent_of x t == Some p)
          (ensures moves x np t sub p)
  = ()

(* ======================================================================================
   10. THE MOVE PAIRS — a move beside a rewrite, an insert, a reorder, a removal and a move,
       each in `wstep_in`'s shape. Each pair is three small facts and their join: what the two
       footprints' independence says of the ids (the `_facts` lemma); that the move applies at
       the other operation's result, to the edited subtree, with its footprint unchanged (the
       `move_after_` lemma — the other operation keeps the target found, parented and its
       destination present, by sections 7 and 9); and that the other operation applies at the
       moved tree with its footprint unchanged (the `_after_move` lemma). The join is then the
       distribution of the other operation over the graft (section 9) and its commutation with
       the removal (section 6). Kept in small lemmas on purpose: the join as one query sat at
       the pinned rlimit's edge.
   ====================================================================================== *)

(* A present move's footprint, and an absent one's. *)
let move_fp_in (t:tree) (x np:string)
  : Lemma (ensures op_fp_in t (MoveNode x np) ==
                   (match find_in x t, parent_of x t with
                    | Some _, Some p ->
                      if has_id np t
                      then { reads = x :: np :: p :: anc_of np t; structure_writes = [p; np];
                             content_writes = [x]; unknown_parent_writes = []; slot_reads = []; slot_writes = [] }
                      else op_fp (MoveNode x np)
                    | _, _ -> op_fp (MoveNode x np)))
  = ()

(* ---- CASE M/U ---- *)

let move_after_update (x np:string) (t sub:tree) (p:string) (n:tree)
  : Lemma (requires moves x np t sub p /\ Ok? (apply (UpdateNode n) t))
          (ensures (let tu = upd (tid_of n) (kind_of n) t in
                    apply (MoveNode x np) tu == Ok (ins np (upd (tid_of n) (kind_of n) sub) (rem_at p x tu))
                    /\ op_fp_in tu (MoveNode x np) == op_fp_in t (MoveNode x np)))
  = let y = tid_of n in
    let k = kind_of n in
    let tu = upd y k t in
    Preservation.upd_wf y k t;
    Preservation.tid_upd y k t;
    Preservation.ids_upd y k t;
    Preservation.ids_upd y k sub;
    find_upd x y k t;
    Preservation.parent_upd x y k t;
    move_applies x np tu (upd y k sub) p;
    move_fp_in t x np;
    move_fp_in tu x np;
    path_upd np y k t

let update_after_move (x np:string) (t sub:tree) (p:string) (n:tree)
  : Lemma (requires moves x np t sub p /\ Ok? (apply (UpdateNode n) t))
          (ensures (let tm = ins np sub (rem_at p x t) in
                    apply (UpdateNode n) tm == Ok (upd (tid_of n) (kind_of n) tm)
                    /\ op_fp_in tm (UpdateNode n) == op_fp_in t (UpdateNode n)))
  = let y = tid_of n in
    let tm = ins np sub (rem_at p x t) in
    find_in_some_iff y t;
    Preservation.move_ids x np t p sub y;
    find_in_some_iff y tm;
    update_fp_in t n;
    update_fp_in tm n

let move_update_in (x np:string) (n:tree) (t:tree)
  : Lemma (requires wf t /\ independent (op_fp_in t (MoveNode x np)) (op_fp_in t (UpdateNode n)))
          (ensures wstep_in (MoveNode x np) (UpdateNode n) t)
  = if Ok? (apply (MoveNode x np) t) && Ok? (apply (UpdateNode n) t) then begin
      match find_in x t, parent_of x t with
      | Some sub, Some p ->
        move_ok x np t sub p;
        move_applies x np t sub p;
        move_after_update x np t sub p n;
        update_after_move x np t sub p n;
        upd_ins_gen (tid_of n) (kind_of n) np sub (rem_at p x t);
        upd_rem_comm (tid_of n) (kind_of n) p x t
      | _ -> ()
    end
    else ()

(* ---- CASE M/I ---- *)

let move_insert_facts (x np:string) (t sub:tree) (p:string) (q:string) (g:tree)
  : Lemma (requires find_in x t == Some sub /\ parent_of x t == Some p /\ has_id np t
                    /\ independent (op_fp_in t (MoveNode x np)) (op_fp_in t (InsertChild q g)))
          (ensures q <> p /\ q <> np /\ x <> q
                   /\ not (mem x (ids g)) /\ not (mem np (ids g)) /\ not (mem p (ids g)))
  = let fa = op_fp_in t (MoveNode x np) in
    let fb = op_fp_in t (InsertChild q g) in
    move_fp_in t x np;
    inter_nil_iff fa.structure_writes fb.structure_writes;
    inter_nil_iff fb.content_writes fa.reads;
    inter_nil_iff fa.content_writes fb.reads

let move_after_insert (x np:string) (t sub:tree) (p:string) (q:string) (g:tree)
  : Lemma (requires moves x np t sub p /\ Ok? (apply (InsertChild q g) t)
                    /\ not (mem x (ids g)) /\ not (mem np (ids g)))
          (ensures (let ti = ins q g t in
                    apply (MoveNode x np) ti == Ok (ins np (ins q g sub) (rem_at p x ti))
                    /\ op_fp_in ti (MoveNode x np) == op_fp_in t (MoveNode x np)))
  = let ti = ins q g t in
    Preservation.apply_preserves_wf (InsertChild q g) t;
    tid_ins q g t;
    find_ins x q g t;
    parent_ins x q g t;
    ids_ins_mem np q g t;
    ids_ins_mem np q g sub;
    move_applies x np ti (ins q g sub) p;
    move_fp_in t x np;
    move_fp_in ti x np;
    path_ins np q g t

let insert_after_move (x np:string) (t sub:tree) (p:string) (q:string) (g:tree)
  : Lemma (requires moves x np t sub p /\ Ok? (apply (InsertChild q g) t))
          (ensures (let tm = ins np sub (rem_at p x t) in
                    apply (InsertChild q g) tm == Ok (ins q g tm)))
  = let tm = ins np sub (rem_at p x t) in
    first_dup_none_iff g t;
    move_ids_fa x np t sub p;
    inter_nil_iff (ids g) (ids t);
    inter_nil_iff (ids g) (ids tm);
    first_dup_none_iff g tm

let move_insert_in (x np:string) (q:string) (g:tree) (t:tree)
  : Lemma (requires wf t /\ independent (op_fp_in t (MoveNode x np)) (op_fp_in t (InsertChild q g)))
          (ensures wstep_in (MoveNode x np) (InsertChild q g) t)
  = if Ok? (apply (MoveNode x np) t) && Ok? (apply (InsertChild q g) t) then begin
      match find_in x t, parent_of x t with
      | Some sub, Some p ->
        move_ok x np t sub p;
        move_applies x np t sub p;
        move_insert_facts x np t sub p q g;
        move_after_insert x np t sub p q g;
        insert_after_move x np t sub p q g;
        ins_ins_gen q g np sub (rem_at p x t);
        ins_rem_comm q g p x t
      | _ -> ()
    end
    else ()

(* ---- CASE M/O ---- *)

let move_reorder_facts (x np:string) (t sub:tree) (p:string) (q:string) (o:list string)
  : Lemma (requires find_in x t == Some sub /\ parent_of x t == Some p /\ has_id np t
                    /\ independent (op_fp_in t (MoveNode x np)) (op_fp_in t (ReorderChildren q o)))
          (ensures q <> p /\ q <> np /\ x <> q)
  = let fa = op_fp_in t (MoveNode x np) in
    let fb = op_fp_in t (ReorderChildren q o) in
    move_fp_in t x np;
    inter_nil_iff fa.structure_writes fb.structure_writes;
    inter_nil_iff fa.content_writes fb.reads

let move_after_reorder (x np:string) (t sub:tree) (p:string) (q:string) (o:list string)
  : Lemma (requires moves x np t sub p /\ Ok? (apply (ReorderChildren q o) t))
          (ensures (let tr = reorder_at q o t in
                    apply (MoveNode x np) tr == Ok (ins np (reorder_at q o sub) (rem_at p x tr))
                    /\ op_fp_in tr (MoveNode x np) == op_fp_in t (MoveNode x np)))
  = let tr = reorder_at q o t in
    wf_reorder_ok q o t;
    Preservation.apply_preserves_wf (ReorderChildren q o) t;
    tid_reorder q o t;
    find_reorder x q o t;
    parent_reorder x q o t;
    ids_reorder_mem np q o t;
    Preservation.find_in_wf x t sub;
    reorder_ok_sub x q o t sub;
    ids_reorder_mem np q o sub;
    move_applies x np tr (reorder_at q o sub) p;
    move_fp_in t x np;
    move_fp_in tr x np;
    path_reorder np q o t

(* The reorder's parent keeps its children through a move elsewhere, so the reorder is accepted. *)
let reorder_after_move (x np:string) (t sub:tree) (p:string) (q:string) (o:list string)
  : Lemma (requires moves x np t sub p /\ Ok? (apply (ReorderChildren q o) t) /\ q <> p /\ q <> np)
          (ensures (let tm = ins np sub (rem_at p x t) in
                    apply (ReorderChildren q o) tm == Ok (reorder_at q o tm)))
  = let tm = ins np sub (rem_at p x t) in
    find_in_some_iff q t;
    match find_in q t with
    | Some m ->
      Preservation.move_ids x np t p sub q;
      find_in_some_iff q tm;
      Preservation.move_view x np t p sub q;
      app_nil_r (Preservation.drop_id x (Preservation.kids_at q t));
      (if mem x (Preservation.kids_at q t) then Preservation.parent_kids q x t m else ());
      Preservation.drop_id_absent x (Preservation.kids_at q t)
    | None -> ()

let move_reorder_in (x np:string) (q:string) (o:list string) (t:tree)
  : Lemma (requires wf t /\ independent (op_fp_in t (MoveNode x np)) (op_fp_in t (ReorderChildren q o)))
          (ensures wstep_in (MoveNode x np) (ReorderChildren q o) t)
  = if Ok? (apply (MoveNode x np) t) && Ok? (apply (ReorderChildren q o) t) then begin
      match find_in x t, parent_of x t with
      | Some sub, Some p ->
        move_ok x np t sub p;
        move_applies x np t sub p;
        move_reorder_facts x np t sub p q o;
        move_after_reorder x np t sub p q o;
        reorder_after_move x np t sub p q o;
        reorder_ins_gen q o np sub (rem_at p x t);
        reorder_rem_comm q o p x t
      | _ -> ()
    end
    else ()

(* ---- CASE M/R ---- *)

let move_remove_facts (x np:string) (t sub:tree) (p:string) (y:string) (suby:tree) (py:string)
  : Lemma (requires find_in x t == Some sub /\ parent_of x t == Some p /\ has_id np t
                    /\ find_in y t == Some suby /\ parent_of y t == Some py
                    /\ independent (op_fp_in t (MoveNode x np)) (op_fp_in t (RemoveNode y)))
          (ensures py <> p /\ py <> np /\ x <> y /\ x <> py
                   /\ not (mem x (ids suby)) /\ not (mem np (ids suby)) /\ not (mem p (ids suby)))
  = let fa = op_fp_in t (MoveNode x np) in
    let fb = op_fp_in t (RemoveNode y) in
    move_fp_in t x np;
    remove_fp_in t y;
    inter_nil_iff fa.structure_writes fb.structure_writes;
    inter_nil_iff fa.content_writes fb.reads;
    inter_nil_iff fb.content_writes fa.reads

let move_after_remove (x np:string) (t sub:tree) (p:string) (y:string) (suby:tree) (py:string)
  : Lemma (requires moves x np t sub p /\ tid_of t <> y /\ find_in y t == Some suby /\ parent_of y t == Some py
                    /\ not (mem x (ids suby)) /\ not (mem np (ids suby)))
          (ensures (let tr = rem_at py y t in
                    apply (MoveNode x np) tr == Ok (ins np (rem_at py y sub) (rem_at p x tr))
                    /\ op_fp_in tr (MoveNode x np) == op_fp_in t (MoveNode x np)))
  = let tr = rem_at py y t in
    find_in_some_iff x t;
    Preservation.rem_wf py y t;
    Preservation.tid_rem py y t;
    Preservation.rem_survives x py y t suby;
    Preservation.find_rem x py y t;
    parent_rem x py y t suby;
    Preservation.rem_survives np py y t suby;
    Preservation.ids_rem_sub np py y sub;
    move_applies x np tr (rem_at py y sub) p;
    move_fp_in t x np;
    move_fp_in tr x np;
    path_rem np py y t suby

let remove_after_move (x np:string) (t sub:tree) (p:string) (y:string) (suby:tree) (py:string)
  : Lemma (requires moves x np t sub p /\ tid_of t <> y /\ find_in y t == Some suby /\ parent_of y t == Some py
                    /\ x <> y /\ not (mem np (ids suby)) /\ not (mem p (ids suby)))
          (ensures (let tm = ins np sub (rem_at p x t) in
                    apply (RemoveNode y) tm == Ok (rem_at py y tm)
                    /\ op_fp_in tm (RemoveNode y) == op_fp_in t (RemoveNode y)))
  = let tm = ins np sub (rem_at p x t) in
    move_applies x np t sub p;
    Preservation.parent_of_mem y t py;
    Preservation.move_ids x np t p sub y;
    move_parent x np t sub p y;
    remove_fp_in t y;
    remove_fp_in tm y;
    move_find x np t sub p y suby;
    Preservation.rem_absent p x suby;
    ins_absent np sub suby

let move_remove_in (x np:string) (y:string) (t:tree)
  : Lemma (requires wf t /\ independent (op_fp_in t (MoveNode x np)) (op_fp_in t (RemoveNode y)))
          (ensures wstep_in (MoveNode x np) (RemoveNode y) t)
  = if Ok? (apply (MoveNode x np) t) && Ok? (apply (RemoveNode y) t) then begin
      find_in_some_iff y t;
      match find_in x t, parent_of x t, find_in y t, parent_of y t with
      | Some sub, Some p, Some suby, Some py ->
        move_ok x np t sub p;
        move_applies x np t sub p;
        move_remove_facts x np t sub p y suby py;
        move_after_remove x np t sub p y suby py;
        remove_after_move x np t sub p y suby py;
        rem_ins_gen py y np sub (rem_at p x t);
        rem_rem_comm py y p x t
      | _, _, _, _ -> ()
    end
    else ()

(* ---- CASE M/M ---- *)

(* Neither destination is inside the other's subtree, because the move READS its destination's
   ancestors and the other move content-writes its target. *)
let move_move_facts (x1 np1:string) (t sub1:tree) (p1:string) (x2 np2:string) (sub2:tree) (p2:string)
  : Lemma (requires wf t /\ find_in x1 t == Some sub1 /\ parent_of x1 t == Some p1 /\ has_id np1 t
                    /\ find_in x2 t == Some sub2 /\ parent_of x2 t == Some p2 /\ has_id np2 t
                    /\ independent (op_fp_in t (MoveNode x1 np1)) (op_fp_in t (MoveNode x2 np2)))
          (ensures p1 <> p2 /\ p1 <> np2 /\ np1 <> p2 /\ np1 <> np2 /\ x1 <> x2
                   /\ not (mem np2 (ids sub1)) /\ not (mem np1 (ids sub2)))
  = let fa = op_fp_in t (MoveNode x1 np1) in
    let fb = op_fp_in t (MoveNode x2 np2) in
    move_fp_in t x1 np1;
    move_fp_in t x2 np2;
    inter_nil_iff fa.structure_writes fb.structure_writes;
    inter_nil_iff fa.content_writes fb.reads;
    inter_nil_iff fb.content_writes fa.reads;
    (if mem np2 (ids sub1) then anc_of_through x1 np2 t sub1 else ());
    (if mem np1 (ids sub2) then anc_of_through x2 np1 t sub2 else ())

(* The first move at the second's result: it applies, to its subtree with the second move
   applied inside it, and its footprint stands. *)
let move_after_move (x1 np1:string) (t sub1:tree) (p1:string) (x2 np2:string) (sub2:tree) (p2:string)
  : Lemma (requires moves x1 np1 t sub1 p1 /\ moves x2 np2 t sub2 p2 /\ x1 <> x2
                    /\ not (mem np1 (ids sub2)) /\ not (mem np2 (ids sub1)))
          (ensures (let tm2 = ins np2 sub2 (rem_at p2 x2 t) in
                    let s1 = ins np2 sub2 (rem_at p2 x2 sub1) in
                    apply (MoveNode x1 np1) tm2 == Ok (ins np1 s1 (rem_at p1 x1 tm2))
                    /\ op_fp_in tm2 (MoveNode x1 np1) == op_fp_in t (MoveNode x1 np1)))
  = let tm2 = ins np2 sub2 (rem_at p2 x2 t) in
    let s1 = ins np2 sub2 (rem_at p2 x2 sub1) in
    move_applies x2 np2 t sub2 p2;
    find_in_some_iff x1 t;
    move_find x2 np2 t sub2 p2 x1 sub1;
    move_parent x2 np2 t sub2 p2 x1;
    Preservation.move_ids x2 np2 t p2 sub2 np1;
    (if mem np1 (ids s1) then graft_ids np2 sub2 p2 x2 sub1 np1 else ());
    move_applies x1 np1 tm2 s1 p1;
    move_fp_in t x1 np1;
    move_fp_in tm2 x1 np1;
    move_path x2 np2 t sub2 p2 np1

(* The tree equation of two moves, given what the footprint and the two applications say. *)
let move_move_commute (x1 np1 p1:string) (sub1:tree) (x2 np2 p2:string) (sub2:tree) (t:tree)
  : Lemma (requires p1 <> p2 /\ p1 <> np2 /\ np1 <> p2 /\ np1 <> np2
                    /\ not (mem np1 (ids sub1)) /\ not (mem np1 (ids sub2))
                    /\ not (mem np2 (ids sub1)) /\ not (mem np2 (ids sub2)))
          (ensures (let r1 = rem_at p1 x1 t in
                    let r2 = rem_at p2 x2 t in
                    ins np2 (ins np1 sub1 (rem_at p1 x1 sub2)) (rem_at p2 x2 (ins np1 sub1 r1)) ==
                    ins np1 (ins np2 sub2 (rem_at p2 x2 sub1)) (rem_at p1 x1 (ins np2 sub2 r2))))
  = let r1 = rem_at p1 x1 t in
    let r2 = rem_at p2 x2 t in
    let sub1' = rem_at p2 x2 sub1 in
    let sub2' = rem_at p1 x1 sub2 in
    Preservation.ids_rem_sub np2 p2 x2 sub1;
    Preservation.ids_rem_sub np1 p1 x1 sub2;
    (* the subtrees each move finds at the other's result are the pruned ones *)
    ins_absent np1 sub1 sub2';
    ins_absent np2 sub2 sub1';
    (* move 2 after move 1 *)
    rem_ins_gen p2 x2 np1 sub1 r1;
    ins_ins_gen np2 sub2' np1 sub1' (rem_at p2 x2 r1);
    ins_absent np2 sub2' sub1';
    (* move 1 after move 2 *)
    rem_ins_gen p1 x1 np2 sub2 r2;
    rem_rem_comm p1 x1 p2 x2 t

let move_move_in (x1 np1 x2 np2:string) (t:tree)
  : Lemma (requires wf t /\ independent (op_fp_in t (MoveNode x1 np1)) (op_fp_in t (MoveNode x2 np2)))
          (ensures wstep_in (MoveNode x1 np1) (MoveNode x2 np2) t)
  = if Ok? (apply (MoveNode x1 np1) t) && Ok? (apply (MoveNode x2 np2) t) then begin
      match find_in x1 t, parent_of x1 t, find_in x2 t, parent_of x2 t with
      | Some sub1, Some p1, Some sub2, Some p2 ->
        move_ok x1 np1 t sub1 p1;
        move_ok x2 np2 t sub2 p2;
        move_applies x1 np1 t sub1 p1;
        move_applies x2 np2 t sub2 p2;
        move_move_facts x1 np1 t sub1 p1 x2 np2 sub2 p2;
        move_after_move x1 np1 t sub1 p1 x2 np2 sub2 p2;
        move_after_move x2 np2 t sub2 p2 x1 np1 sub1 p1;
        move_move_commute x1 np1 p1 sub1 x2 np2 p2 sub2 t
      | _, _, _, _ -> ()
    end
    else ()

(* ---- every leaf pair ---- *)

let all_leaf_in (a b:op) (t:tree)
  : Lemma (requires wf t /\ is_leaf a /\ is_leaf b /\ independent (op_fp_in t a) (op_fp_in t b))
          (ensures wstep_in a b t)
  = match a, b with
    | MoveNode x1 np1, MoveNode x2 np2 -> move_move_in x1 np1 x2 np2 t
    | MoveNode x np, InsertChild q g -> move_insert_in x np q g t
    | InsertChild q g, MoveNode x np ->
      independent_sym (op_fp_in t a) (op_fp_in t b); move_insert_in x np q g t; wstep_in_sym b a t
    | MoveNode x np, ReorderChildren q o -> move_reorder_in x np q o t
    | ReorderChildren q o, MoveNode x np ->
      independent_sym (op_fp_in t a) (op_fp_in t b); move_reorder_in x np q o t; wstep_in_sym b a t
    | MoveNode x np, UpdateNode n -> move_update_in x np n t
    | UpdateNode n, MoveNode x np ->
      independent_sym (op_fp_in t a) (op_fp_in t b); move_update_in x np n t; wstep_in_sym b a t
    | MoveNode x np, RemoveNode y -> move_remove_in x np y t
    | RemoveNode y, MoveNode x np ->
      independent_sym (op_fp_in t a) (op_fp_in t b); move_remove_in x np y t; wstep_in_sym b a t
    | _, _ -> no_move_leaf_in a b t

(* ======================================================================================
   11. THE LIFT, over every operation — section 5's two moves with no fragment predicate — and
       THE THEOREM, as the module header states it. `footprint_in_sound_no_move` (section 8) and
       `footprint_in_sound_in_place` (section 5) are the same theorem over the fragments the
       proof grew through.
   ====================================================================================== *)

let rec leaf_vs_any_all (a b:op) (t:tree)
  : Lemma (requires wf t /\ is_leaf a /\ independent (op_fp_in t a) (op_fp_in t b))
          (ensures wstep_in a b t) (decreases b)
  = match b with
    | Batch os -> leaf_vs_script_all a os t
    | _ -> all_leaf_in a b t
and leaf_vs_script_all (a:op) (os:list op) (t:tree)
  : Lemma (requires wf t /\ is_leaf a /\ independent (op_fp_in t a) (op_fp_in t (Batch os)))
          (ensures wstep_in a (Batch os) t) (decreases os)
  = match os with
    | [] -> ()
    | b1 :: bs ->
      (match apply b1 t with
       | Error _ -> ()
       | Ok t1 ->
         fp_in_cons t b1 bs t1;
         independent_union_right (op_fp_in t a) (op_fp_in t b1) (op_fp_in t1 (Batch bs));
         leaf_vs_any_all a b1 t;
         Preservation.apply_preserves_wf b1 t;
         (match apply a t with
          | Error _ -> ()
          | Ok ta ->
            leaf_vs_script_all a bs t1;
            (match apply (Batch bs) t1 with
             | Error _ -> ()
             | Ok tb ->
               (match apply b1 ta with
                | Ok ta1 -> fp_in_cons ta b1 bs ta1
                | Error _ -> ()))))

let rec any_vs_any_all (a b:op) (t:tree)
  : Lemma (requires wf t /\ independent (op_fp_in t a) (op_fp_in t b))
          (ensures wstep_in a b t) (decreases a)
  = match a with
    | Batch os -> script_vs_any_all os b t
    | _ -> leaf_vs_any_all a b t
and script_vs_any_all (os:list op) (b:op) (t:tree)
  : Lemma (requires wf t /\ independent (op_fp_in t (Batch os)) (op_fp_in t b))
          (ensures wstep_in (Batch os) b t) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      (match apply o t with
       | Error _ -> ()
       | Ok to ->
         fp_in_cons t o r to;
         independent_union_left (op_fp_in t o) (op_fp_in to (Batch r)) (op_fp_in t b);
         any_vs_any_all o b t;
         Preservation.apply_preserves_wf o t;
         (match apply b t with
          | Error _ -> ()
          | Ok tb ->
            script_vs_any_all r b to;
            (match apply (Batch r) to with
             | Error _ -> ()
             | Ok ta ->
               (match apply o tb with
                | Ok tb1 -> fp_in_cons tb o r tb1
                | Error _ -> ()))))

let footprint_in_sound (a b:op) (t:tree)
  : Lemma (requires wf t /\ independent (op_fp_in t a) (op_fp_in t b))
          (ensures wstep a b t)
  = any_vs_any_all a b t

let footprint_in_sound_scripts (a b:list op) (t:tree)
  : Lemma (requires wf t /\ independent (fp_in_all t a) (fp_in_all t b))
          (ensures wstep (Batch a) (Batch b) t)
  = footprint_in_sound (Batch a) (Batch b) t

let footprint_in_stable (a b:op) (t:tree) (ta:tree)
  : Lemma (requires wf t /\ independent (op_fp_in t a) (op_fp_in t b)
                    /\ apply a t == Ok ta /\ Ok? (apply b t))
          (ensures op_fp_in ta b == op_fp_in t b)
  = any_vs_any_all a b t
