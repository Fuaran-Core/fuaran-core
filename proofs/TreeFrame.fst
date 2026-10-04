(*
   TreeFrame — the FRAME of the skeleton-op tree algebra: where an applied op writes, that the
   write gate's targets contain it, and that a gated apply leaves a locked id alone
   (fuaran-core Phase 362).

   WHAT IS MODELLED. Nothing new about the algebra: the tree, the ops, `apply` and the footprint are
   `TreeOps.fst`'s, clause for clause with `Ops.apply` and `Ops.footprint`, and the per-node view
   (`kids_at`, `kind_at`) and what each edit does to it are `Preservation.fst`'s. Added here, clause
   for clause with `src/Fuaran.Core.Ops/WriteGate.fs` as Phase 318 landed it:

     - `targets_of_one` / `targets_go` / `targets_of` — `WriteGate.targetsOfOne` and `targetsOf`:
       the footprint's structure, content and unknown-parent writes and its slot writes' nodes,
       plus the subtree a `RemoveNode` destroys, read from the tree; a `Batch` flattened onto the
       work list and taken op by op over the tree the ops before it produced, stopping at the first
       op that does not apply;
     - `chain_of`, `decide_one`, `decide_go` / `decide`, `apply_gated` — `chainOf`, `decideOne`,
       `decide` and `applyGated`, with `path_in` for `Tree.path`.

   WHAT "WRITTEN" MEANS, once. `written x t t'`: what the witness shows AT the id `x` differs
   between the two trees — its presence, its content (the kind tag, as `TreeOps.fst` reads
   `UpdateNode`) or its ordered list of child ids. That is the entry `Conformance.writeGateLaws`'
   cover law compares (`Map.tryFind id before <> Map.tryFind id after` over each node's child-id
   list), with the content beside it. An id's PARENT and its PLACE AMONG ITS SIBLINGS are not
   separate clauses: they are the parent's child list, and `placement_is_the_parents_entry` proves
   the reading loses nothing — an id whose parent or sibling order differs has a written parent.
   The definition is over the model's own tree and takes no parameter.

   WHAT IS PROVED.

     - `apply_frame` / `batch_frame` — THE FRAME. For every op (a `Batch` at any depth by the fold
       `fp_all` is defined by) and every well-formed tree at which it applies, every written id is
       an address the footprint NAMES (`named_writes`: structure, content, unknown-parent and
       slot-node writes) or one of the two tree facts an unknown-parent write stands for and the
       script cannot name, resolved at the tree the op lands on (`unnamed`): the SOURCE PARENT of a
       removed or moved node, and the SUBTREE a removal destroys.
     - `named_frame` — the pure-footprint corollary: an op whose footprint carries no
       unknown-parent write writes ONLY at addresses its footprint names. No tree is read.
     - `pure_frame_fails_for_a_remove`, `pure_frame_fails_for_a_move` — and for a relocating op the
       pure form is FALSE, with the witnesses: a removal destroys a descendant its footprint does
       not name and rewrites a parent it does not name, and a move rewrites its source parent.
       These are `Ops.footprint`'s two pinned over-approximations, machine-checked as the boundary
       of `named_frame` rather than left as prose; `unnamed_is_resolved` says the unnamed writes
       are exactly those two facts about an id the footprint does carry as an unknown-parent write.
     - `targets_cover_written` — every id an applied op writes is in `targets_of`, or is the parent,
       in the tree the op was applied to, of an id that is. This is the statement of the cover law,
       with content added; the second disjunct is the source parent, which `WriteGate.targetsOf`
       documents as the one written id it does not name.
     - `gated_apply_respects_lock` — when `apply_gated` succeeds, every locked id is exactly as it
       was: `find_in l` answers the same before and after, so a locked node the tree held is still
       there with its WHOLE SUBTREE unchanged, and a locked id it did not hold was not created.
       `nothing_under_a_lock_is_written` is its reading at each id under a lock. A locked id inside
       a destroyed subtree is the absent-after case, and it cannot happen.

   WHAT IS NOT CLAIMED, and two boundaries that are proved instead of promised.

     - That `WriteGate.targetsOf`, `decide` and `applyGated` compute what these models compute: the
       module is checked and not extracted, and `Conformance.writeGateLaws` is what holds
       production to the statements proved here. `decide_one` decides the VERDICT (allowed, locked
       or not writable); which of several offending targets production names first is its `Set`
       order, and the model's lists carry no order on ids.
     - `lock_does_not_pin_sibling_place` — a lock protects the locked node's own entry, not its
       index under an unlocked parent: reordering that parent is allowed and moves it.
     - `allow_list_does_not_cover_the_source_parent` — under an ALLOW-LIST the source-parent
       exception is not harmless as it is for a lock: removing an allowed node rewrites the child
       list of a parent the list does not cover. No allow-list theorem is stated for that reason.
     - Slot writes: no skeleton op writes a slot, so `nodes f.slot_writes` is empty for every
       footprint `op_fp` produces; the clause is carried because `targetsOfOne` carries it.
     - Ill-formed trees (an id twice), keyed positions and container capability, exactly as
       `TreeOps.fst`'s header says of `apply`.

   Apache-2.0, like everything beside it.
*)
module TreeFrame

open DagFold
open TreeOps
open Preservation

(* Context pruning for the reason `TreeOps.fst` gives. Fuel is fixed, not escalated: `--fuel 0
   --ifuel 1` is the module's standing level — no unfolding of a recursive function, one inversion
   of a datatype, which is what a definition by cases needs to be accepted at all — and every block
   of lemmas states the depth it needs. *)
#set-options "--ext context_pruning --fuel 0 --ifuel 1"

(* ======================================================================================
   1. Written — what the witness shows at an id, before and after.
   ====================================================================================== *)

(* F#: the cover law's `Map.tryFind id before <> Map.tryFind id after` over `childLists`, with the
   content the witness shows beside the child ids. An absent id shows `(None, [])`, so an id present
   in exactly one of the two trees is written through its kind. *)
let written (x:string) (t t':tree) : Tot bool =
  kind_at x t <> kind_at x t' || kids_at x t <> kids_at x t'

(* The relation composes: an id no step wrote was not written end to end. This is the whole of how
   a `Batch` is threaded. *)
let written_trans (x:string) (t t1 t2:tree)
  : Lemma (requires not (written x t t1) /\ not (written x t1 t2)) (ensures not (written x t t2))
  = ()

(* The same fact, in the direction an induction over a script consumes it: a write the first step
   did not make was made by the rest. *)
let written_split (x:string) (t t1 t2:tree)
  : Lemma (requires written x t t2 /\ not (written x t t1)) (ensures written x t1 t2)
  = ()

let written_sym (x:string) (t t':tree)
  : Lemma (ensures written x t t' == written x t' t)
  = ()

#push-options "--ifuel 1"
(* Presence is part of the view: an id in exactly one of the two trees is written. *)
let presence_is_written (x:string) (t t':tree)
  : Lemma (requires mem x (ids t) <> mem x (ids t')) (ensures written x t t')
  = find_in_some_iff x t; find_in_some_iff x t'

(* … and an unwritten id is in both or in neither. *)
let unwritten_keeps_presence (x:string) (t t':tree)
  : Lemma (requires not (written x t t')) (ensures mem x (ids t) == mem x (ids t'))
  = find_in_some_iff x t; find_in_some_iff x t'
#pop-options

(* ---- a child is its parent's entry ---- *)

#push-options "--fuel 1 --ifuel 1"
(* `Tree.parentOf` answers with a node that holds the child (the converse of
   `Preservation.parent_kids`). *)
let rec parent_holds_kid (y q:string) (t:tree)
  : Lemma (requires wf t /\ parent_of y t == Some q)
          (ensures (match find_in q t with
                    | Some n -> mem y (kid_ids (kids_of n))
                    | None -> False)) (decreases t)
  = match t with
    | TNode i _ cs ->
      if has_kid y cs then has_kid_is_mem y cs
      else (parent_all_mem y cs q; parent_holds_kid_all y q cs)
and parent_holds_kid_all (y q:string) (ts:list tree)
  : Lemma (requires wf_all ts /\ parent_all y ts == Some q)
          (ensures (match find_all q ts with
                    | Some n -> mem y (kid_ids (kids_of n))
                    | None -> False)) (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r ->
      inter_nil_iff (ids c) (ids_all r);
      find_in_some_iff q c;
      (match parent_of y c with
       | Some _ -> parent_holds_kid y q c
       | None -> parent_all_mem y r q; parent_holds_kid_all y q r)

let parent_is_entry (y q:string) (t:tree)
  : Lemma (requires wf t /\ parent_of y t == Some q) (ensures mem y (kids_at q t))
  = parent_holds_kid y q t

let entry_is_parent (q y:string) (t:tree)
  : Lemma (requires wf t /\ mem y (kids_at q t)) (ensures parent_of y t == Some q)
  = match find_in q t with
    | Some m -> parent_kids q y t m
    | None -> ()
#pop-options

(* THE READING LOSES NOTHING. If an id's parent is not written, the id has the same parent and the
   same siblings in the same order afterwards. Contrapositive: an id whose parent or whose place
   among its siblings differs has a WRITTEN parent — so "its parent or its position differs" is the
   parent's entry in `written`, not a clause of its own. *)
let placement_is_the_parents_entry (x p:string) (t t':tree)
  : Lemma (requires wf t /\ wf t' /\ parent_of x t == Some p /\ not (written p t t'))
          (ensures parent_of x t' == Some p /\ kids_at p t' == kids_at p t)
  = parent_is_entry x p t; entry_is_parent p x t'

(* ======================================================================================
   2. What a footprint names, and what an unknown-parent write stands for.
   ====================================================================================== *)

(* The addresses a footprint names as written, in the order `WriteGate.targetsOfOne` unions them. *)
let named_writes (f:footprint) : Tot (list string) =
  union f.structure_writes
        (union f.content_writes (union f.unknown_parent_writes (nodes f.slot_writes)))

let named_writes_union (a b:footprint) (q:string)
  : Lemma (ensures mem q (named_writes (union_fp a b)) ==
                   (mem q (named_writes a) || mem q (named_writes b)))
  = ()

(* The two tree facts the pure script cannot name (`Ops.footprint`'s pinned over-approximations (1)
   and (2)): the parent a node is removed or moved FROM, and the subtree a removal takes with it. *)
let source_parent (x:string) (t:tree) : Tot (list string) =
  match parent_of x t with
  | Some p -> [p]
  | None -> []

let subtree_ids (x:string) (t:tree) : Tot (list string) =
  match find_in x t with
  | Some n -> ids n
  | None -> []

#push-options "--fuel 2 --ifuel 1"
let source_parent_mem (x:string) (t:tree) (q:string)
  : Lemma (ensures mem q (source_parent x t) <==> parent_of x t == Some q)
  = ()
#pop-options

(* The writes of `o` on `t` that its footprint does not name, each resolved at the tree the op
   lands on: a `Batch` threads `apply`, as `fp_all` folds `op_fp`. An `UpdateNode` carries an
   unknown-parent write and writes nothing unnamed. *)
let rec unnamed (o:op) (t:tree) : Tot (list string) (decreases o) =
  match o with
  | RemoveNode x -> app (source_parent x t) (subtree_ids x t)
  | MoveNode x _ -> source_parent x t
  | Batch os -> unnamed_all os t
  | _ -> []
and unnamed_all (os:list op) (t:tree) : Tot (list string) (decreases os) =
  match os with
  | [] -> []
  | o :: r -> app (unnamed o t) (match apply o t with
                                 | Ok t1 -> unnamed_all r t1
                                 | Error _ -> [])

#push-options "--fuel 2 --ifuel 1"
(* An unnamed write is one of the two tree facts, about an id the footprint DOES carry — as an
   unknown-parent write. This is the model's reading of that address kind. *)
let unnamed_is_resolved (o:op) (t:tree) (q:string)
  : Lemma (requires is_leaf o /\ mem q (unnamed o t))
          (ensures exists (x:string). mem x (op_fp o).unknown_parent_writes /\
                                      (parent_of x t == Some q \/ mem q (subtree_ids x t)))
  = match o with
    | RemoveNode x -> source_parent_mem x t q; assert (mem x (op_fp o).unknown_parent_writes)
    | MoveNode x _ -> source_parent_mem x t q; assert (mem x (op_fp o).unknown_parent_writes)
    | _ -> ()

(* … and a footprint with no unknown-parent write leaves nothing unnamed. *)
let rec unnamed_nil (o:op) (t:tree)
  : Lemma (requires no_reloc o) (ensures unnamed o t == []) (decreases o)
  = match o with
    | Batch os -> unnamed_all_nil os t
    | _ -> ()
and unnamed_all_nil (os:list op) (t:tree)
  : Lemma (requires no_reloc_all os) (ensures unnamed_all os t == []) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      unnamed_nil o t;
      (match apply o t with
       | Ok t1 -> unnamed_all_nil r t1
       | Error _ -> ())
#pop-options

(* ======================================================================================
   3. THE FRAME, one op kind at a time. Each is `Preservation.fst`'s per-node view of the edit,
      read against the footprint.
   ====================================================================================== *)

#push-options "--fuel 2 --ifuel 1"

(* An insert writes the parent's child list and creates the graft: both named. *)
let insert_frame (p:string) (n:tree) (t t':tree) (q:string)
  : Lemma (requires wf t /\ apply (InsertChild p n) t == Ok t' /\ written q t t')
          (ensures mem q (named_writes (op_fp (InsertChild p n))))
  = if mem q (ids n) then () else ins_view p n t q

(* A removal destroys its target's subtree and rewrites the child list it is taken from. The
   footprint names the target alone; the rest is the tree's to say. *)
let remove_frame (x:string) (t t':tree) (q:string)
  : Lemma (requires wf t /\ apply (RemoveNode x) t == Ok t' /\ written q t t')
          (ensures mem q (unnamed (RemoveNode x) t))
  = find_in_some_iff x t;
    find_in_some_iff q t;
    find_in_some_iff q t';
    match parent_of x t, find_in x t with
    | Some pid, Some sub ->
      ids_rem_sub q pid x t;
      if not (mem q (ids t)) then ()
      else if mem q (ids sub) then ()
      else begin
        rem_view pid x t sub q;
        if q = pid then ()
        else begin
          (if mem x (kids_at q t) then entry_is_parent q x t else ());
          drop_id_absent x (kids_at q t)
        end
      end
    | _, _ -> ()

(* A move rewrites two child lists — the one it leaves, which the script cannot name, and the one
   it joins, which it can — and nothing else: the subtree travels intact. *)
let move_frame (x np:string) (t t':tree) (q:string)
  : Lemma (requires wf t /\ apply (MoveNode x np) t == Ok t' /\ written q t t')
          (ensures mem q (named_writes (op_fp (MoveNode x np))) \/
                   mem q (unnamed (MoveNode x np) t))
  = find_in_some_iff x t;
    find_in_some_iff q t;
    find_in_some_iff q t';
    match parent_of x t, find_in x t with
    | Some pid, Some sub ->
      move_ids x np t pid sub q;
      if not (mem q (ids t)) then ()
      else begin
        move_view x np t pid sub q;
        if q = np || q = pid then ()
        else begin
          (if mem x (kids_at q t) then entry_is_parent q x t else ());
          drop_id_absent x (kids_at q t)
        end
      end
    | _, _ -> ()

(* A reorder rewrites one named child list. *)
let reorder_frame (p:string) (ord:list string) (t t':tree) (q:string)
  : Lemma (requires wf t /\ apply (ReorderChildren p ord) t == Ok t' /\ written q t t')
          (ensures mem q (named_writes (op_fp (ReorderChildren p ord))))
  = find_in_some_iff p t;
    reorder_view p ord t q

(* A rewrite in place moves one node's content. *)
let update_frame (n:tree) (t t':tree) (q:string)
  : Lemma (requires wf t /\ apply (UpdateNode n) t == Ok t' /\ written q t t')
          (ensures mem q (named_writes (op_fp (UpdateNode n))))
  = upd_view (tid_of n) (kind_of n) t q

let leaf_frame (o:op) (t t':tree) (q:string)
  : Lemma (requires is_leaf o /\ wf t /\ apply o t == Ok t' /\ written q t t')
          (ensures mem q (named_writes (op_fp o)) \/ mem q (unnamed o t))
  = match o with
    | InsertChild p n -> insert_frame p n t t' q
    | RemoveNode x -> remove_frame x t t' q
    | MoveNode x np -> move_frame x np t t' q
    | ReorderChildren p ord -> reorder_frame p ord t t' q
    | UpdateNode n -> update_frame n t t' q

#pop-options

(* ======================================================================================
   4. THE FRAME THEOREM — every op, a `Batch` at any depth.
   ====================================================================================== *)

#push-options "--fuel 1 --ifuel 1"

(* THEOREM — apply_frame / batch_frame. On a well-formed tree, every id an applied op writes is an
   address its footprint names, or the source parent or destroyed subtree of one of its
   unknown-parent writes at the tree that op landed on. `batch_frame` is the same statement for a
   script, by the fold the footprint is defined by: the named half is `fp_all`'s union, the unnamed
   half threads `apply`, and well-formedness is carried by `apply_preserves_wf`. *)
let rec frame_op (o:op) (t t':tree) (q:string)
  : Lemma (requires wf t /\ apply o t == Ok t' /\ written q t t')
          (ensures mem q (named_writes (op_fp o)) \/ mem q (unnamed o t)) (decreases o)
  = match o with
    | Batch os -> frame_all os t t' q
    | _ -> leaf_frame o t t' q
and frame_all (os:list op) (t t':tree) (q:string)
  : Lemma (requires wf t /\ apply_all os t == Ok t' /\ written q t t')
          (ensures mem q (named_writes (fp_all os)) \/ mem q (unnamed_all os t)) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      (match apply o t with
       | Ok t1 ->
         apply_preserves_wf o t;
         named_writes_union (op_fp o) (fp_all r) q;
         if written q t t1 then frame_op o t t1 q
         else begin
           written_split q t t1 t';
           frame_all r t1 t' q
         end
       | Error _ -> ())

(* The two statements under the names the claims ladder cites: one op, and a script. *)
let apply_frame (o:op) (t t':tree) (q:string)
  : Lemma (requires wf t /\ apply o t == Ok t' /\ written q t t')
          (ensures mem q (named_writes (op_fp o)) \/ mem q (unnamed o t))
  = frame_op o t t' q

let batch_frame (os:list op) (t t':tree) (q:string)
  : Lemma (requires wf t /\ apply_all os t == Ok t' /\ written q t t')
          (ensures mem q (named_writes (fp_all os)) \/ mem q (unnamed_all os t))
  = frame_all os t t' q

(* THEOREM — named_frame. The pure-footprint form, where it is true: an op whose footprint carries
   no unknown-parent write writes only at addresses the footprint names. This is the bound a
   consumer that reads footprints without a tree may use. *)
let named_frame (o:op) (t t':tree) (q:string)
  : Lemma (requires wf t /\ apply o t == Ok t' /\ written q t t' /\ not (relocating o))
          (ensures mem q (named_writes (op_fp o)))
  = relocating_iff_not_no_reloc o;
    unnamed_nil o t;
    apply_frame o t t' q

#pop-options

(* ---- and where it is false: the two pinned over-approximations, as witnesses ----

   One well-formed tree. Removing `x` destroys `c`, which the removal's footprint does not name,
   and rewrites `root`'s child list, which it does not name either; moving `c` under `y` rewrites
   `x`'s child list, which the move's footprint does not name. Each id is in `unnamed`, so the
   frame theorem covers it; neither is in `named_writes`, so no reading of the footprint without
   the tree could. The footprint is not WRONG about these — `Ops.footprint` documents both, and
   `Ops.independent` serialises every relocating op for exactly this reason — but it is not a
   bound on the writes by itself, and nothing downstream may read it as one. *)

let frame_tree : tree =
  TNode "root" "doc" [ TNode "x" "sec" [ TNode "c" "para" [] ]; TNode "y" "sec" [] ]

let frame_removed : tree = TNode "root" "doc" [ TNode "y" "sec" [] ]

let frame_moved : tree =
  TNode "root" "doc" [ TNode "x" "sec" []; TNode "y" "sec" [ TNode "c" "para" [] ] ]

let pure_frame_fails_for_a_remove ()
  : Lemma (ensures wf frame_tree /\
                   apply (RemoveNode "x") frame_tree == Ok frame_removed /\
                   written "c" frame_tree frame_removed /\
                   not (mem "c" (named_writes (op_fp (RemoveNode "x")))) /\
                   mem "c" (unnamed (RemoveNode "x") frame_tree) /\
                   written "root" frame_tree frame_removed /\
                   not (mem "root" (named_writes (op_fp (RemoveNode "x")))) /\
                   mem "root" (unnamed (RemoveNode "x") frame_tree))
  = assert_norm (wf frame_tree);
    assert_norm (apply (RemoveNode "x") frame_tree == Ok frame_removed);
    assert_norm (written "c" frame_tree frame_removed);
    assert_norm (not (mem "c" (named_writes (op_fp (RemoveNode "x")))));
    assert_norm (mem "c" (unnamed (RemoveNode "x") frame_tree));
    assert_norm (written "root" frame_tree frame_removed);
    assert_norm (not (mem "root" (named_writes (op_fp (RemoveNode "x")))));
    assert_norm (mem "root" (unnamed (RemoveNode "x") frame_tree))

let pure_frame_fails_for_a_move ()
  : Lemma (ensures apply (MoveNode "c" "y") frame_tree == Ok frame_moved /\
                   written "x" frame_tree frame_moved /\
                   not (mem "x" (named_writes (op_fp (MoveNode "c" "y")))) /\
                   mem "x" (unnamed (MoveNode "c" "y") frame_tree))
  = assert_norm (apply (MoveNode "c" "y") frame_tree == Ok frame_moved);
    assert_norm (written "x" frame_tree frame_moved);
    assert_norm (not (mem "x" (named_writes (op_fp (MoveNode "c" "y")))));
    assert_norm (mem "x" (unnamed (MoveNode "c" "y") frame_tree))

(* ======================================================================================
   5. `WriteGate.targetsOf`, clause for clause.
   ====================================================================================== *)

(* F#: the `destroyed` binding of `targetsOfOne` — the subtree a `RemoveNode` takes, read from the
   tree; empty for every other op and for a target the tree does not hold. *)
let destroyed (o:op) (t:tree) : Tot (list string) =
  match o with
  | RemoveNode x -> (match find_in x t with
                     | Some n -> ids n
                     | None -> [])
  | _ -> []

(* F#: `targetsOfOne` — `Ops.footprint w idw [ op ]` is the fold over a one-op script, and the
   union is `Set.unionMany` over the five sets in its order. *)
let targets_of_one (o:op) (t:tree) : Tot (list string) =
  let fp = fp_all [o] in
  union fp.structure_writes
        (union fp.content_writes
               (union fp.unknown_parent_writes
                      (union (nodes fp.slot_writes) (destroyed o t))))

(* The measure `targetsOf`'s work list goes down by: flattening a `Batch` onto the list removes the
   `Batch` node itself. *)
let rec op_size (o:op) : Tot nat (decreases o) =
  match o with
  | Batch os -> 1 + ops_size os
  | _ -> 1
and ops_size (os:list op) : Tot nat (decreases os) =
  match os with
  | [] -> 0
  | o :: r -> op_size o + ops_size r

#push-options "--fuel 1 --ifuel 1"
let op_size_pos (o:op) : Lemma (ensures op_size o >= 1) = ()

let rec ops_size_app (a b:list op)
  : Lemma (ensures ops_size (app a b) == ops_size a + ops_size b) (decreases a)
  = match a with
    | [] -> ()
    | _ :: r -> ops_size_app r b
#pop-options

#push-options "--fuel 2 --ifuel 1"
(* F#: the `go` loop of `targetsOf` — `Batch inner :: rest -> go acc node (inner @ rest)`; any other
   op adds its targets and continues over the tree it produced, or stops where it does not apply. *)
let rec targets_go (acc:list string) (t:tree) (os:list op)
  : Tot (list string) (decreases (ops_size os)) =
  match os with
  | [] -> acc
  | Batch inner :: rest -> ops_size_app inner rest; targets_go acc t (app inner rest)
  | o :: rest ->
    op_size_pos o;
    let acc' = union acc (targets_of_one o t) in
    (match apply o t with
     | Ok t1 -> targets_go acc' t1 rest
     | Error _ -> acc')
#pop-options

(* F#: `WriteGate.targetsOf`. *)
let targets_of (o:op) (t:tree) : Tot (list string) = targets_go [] t [o]

#push-options "--fuel 2 --ifuel 1"
(* What `targets_of_one` holds: everything the footprint names, and the destroyed subtree. *)
let targets_of_one_mem (o:op) (t:tree) (q:string)
  : Lemma (ensures mem q (targets_of_one o t) ==
                   (mem q (named_writes (op_fp o)) || mem q (destroyed o t)))
  = ()

(* The accumulator only grows. *)
let rec targets_go_mono (acc:list string) (t:tree) (os:list op) (y:string)
  : Lemma (requires mem y acc) (ensures mem y (targets_go acc t os)) (decreases (ops_size os))
  = match os with
    | [] -> ()
    | Batch inner :: rest -> ops_size_app inner rest; targets_go_mono acc t (app inner rest) y
    | o :: rest ->
      op_size_pos o;
      (match apply o t with
       | Ok t1 -> targets_go_mono (union acc (targets_of_one o t)) t1 rest y
       | Error _ -> ())
#pop-options

(* ======================================================================================
   6. THE TARGETS COVER THE WRITES.
   ====================================================================================== *)

(* The node a leaf op takes out of its parent's child list, if it does. *)
let relocated (o:op) : Tot (option string) =
  match o with
  | RemoveNode x -> Some x
  | MoveNode x _ -> Some x
  | _ -> None

#push-options "--fuel 2 --ifuel 1"
(* The frame at one leaf, read against `targets_of_one`: a written id is a target, or it is the
   source parent of the node the op removes or moves — which IS a target. Dropping `destroyed` from
   `targets_of_one` falsifies this at a removal with a descendant. *)
let leaf_cover (o:op) (t t':tree) (q:string)
  : Lemma (requires is_leaf o /\ wf t /\ apply o t == Ok t' /\ written q t t')
          (ensures mem q (targets_of_one o t) \/
                   (match relocated o with
                    | Some x -> mem x (targets_of_one o t) /\ parent_of x t == Some q
                    | None -> False))
  = leaf_frame o t t' q;
    targets_of_one_mem o t q;
    match o with
    | RemoveNode x -> targets_of_one_mem o t x; source_parent_mem x t q
    | MoveNode x _ -> targets_of_one_mem o t x; source_parent_mem x t q
    | _ -> ()

(* A parent link the op leaves behind was there before, or its parent is a target: an op gives a
   node a new parent only by inserting it (under a named parent, or inside the graft) or moving it
   (under a named parent). *)
let parent_step (o:op) (t t1:tree) (y q:string)
  : Lemma (requires is_leaf o /\ wf t /\ apply o t == Ok t1 /\ parent_of y t1 == Some q)
          (ensures parent_of y t == Some q \/ mem q (targets_of_one o t))
  = apply_preserves_wf o t;
    parent_is_entry y q t1;
    targets_of_one_mem o t q;
    find_in_some_iff q t;
    find_in_some_iff q t1;
    match o with
    | InsertChild p n ->
      if mem q (ids n) || q = p then ()
      else (ins_view p n t q; entry_is_parent q y t)
    | RemoveNode x ->
      find_in_some_iff x t;
      (match parent_of x t, find_in x t with
       | Some pid, Some sub ->
         ids_rem_sub q pid x t;
         rem_kills pid x t sub;
         rem_view pid x t sub q;
         drop_id_mem y x (kids_at q t);
         entry_is_parent q y t
       | _, _ -> ())
    | MoveNode x np ->
      find_in_some_iff x t;
      (match parent_of x t, find_in x t with
       | Some pid, Some sub ->
         if q = np then ()
         else begin
           move_ids x np t pid sub q;
           move_view x np t pid sub q;
           drop_id_mem y x (kids_at q t);
           entry_is_parent q y t
         end
       | _, _ -> ())
    | ReorderChildren p ord ->
      find_in_some_iff p t;
      if q = p then ()
      else (reorder_view p ord t q; entry_is_parent q y t)
    | UpdateNode n ->
      upd_view (tid_of n) (kind_of n) t q;
      entry_is_parent q y t
#pop-options

(* What the cover law accepts of a written id, over a target set: it is a target, or it is the
   parent — in the tree the op was applied to — of one. *)
let covered (t0:tree) (targets:list string) (q:string) : prop =
  mem q targets \/ (exists (y:string). mem y targets /\ parent_of y t0 == Some q)

(* The invariant the work list carries: every parent link in the tree in hand was in the starting
   tree, or its parent is already a target. *)
let parent_inv (t0 t:tree) (acc:list string) : prop =
  forall (y p:string). parent_of y t == Some p ==> (parent_of y t0 == Some p \/ mem p acc)

let parent_inv_elim (t0 t:tree) (acc:list string) (y p:string)
  : Lemma (requires parent_inv t0 t acc /\ parent_of y t == Some p)
          (ensures parent_of y t0 == Some p \/ mem p acc)
  = ()

let covered_by_target (t0:tree) (targets:list string) (q:string)
  : Lemma (requires mem q targets) (ensures covered t0 targets q)
  = ()

let covered_by_child (t0:tree) (targets:list string) (q y:string)
  : Lemma (requires mem y targets /\ parent_of y t0 == Some q) (ensures covered t0 targets q)
  = ()

#push-options "--fuel 1 --ifuel 1"
let parent_inv_step (t0 t:tree) (acc:list string) (o:op) (t1:tree)
  : Lemma (requires is_leaf o /\ wf t /\ apply o t == Ok t1 /\ parent_inv t0 t acc)
          (ensures parent_inv t0 t1 (union acc (targets_of_one o t)))
  = let acc' = union acc (targets_of_one o t) in
    let aux (y p:string)
      : Lemma (parent_of y t1 == Some p ==> (parent_of y t0 == Some p \/ mem p acc'))
      = if parent_of y t1 = Some p then begin
          parent_step o t t1 y p;
          if parent_of y t = Some p then parent_inv_elim t0 t acc y p else ()
        end
        else ()
    in
    FStar.Classical.forall_intro_2 aux
#pop-options

#push-options "--fuel 2 --ifuel 1"
(* The cover, over `targetsOf`'s own loop. *)
let rec go_cover (t0:tree) (acc:list string) (t:tree) (os:list op) (t':tree) (q:string)
  : Lemma (requires wf t /\ apply_all os t == Ok t' /\ written q t t' /\ parent_inv t0 t acc)
          (ensures covered t0 (targets_go acc t os) q) (decreases (ops_size os))
  = match os with
    | [] -> ()
    | Batch inner :: rest ->
      ops_size_app inner rest;
      TreeDiff.apply_all_app inner rest t;
      go_cover t0 acc t (app inner rest) t' q
    | o :: rest ->
      op_size_pos o;
      (match apply o t with
       | Ok t1 ->
         let acc' = union acc (targets_of_one o t) in
         apply_preserves_wf o t;
         if written q t t1 then begin
           leaf_cover o t t1 q;
           if mem q (targets_of_one o t) then begin
             targets_go_mono acc' t1 rest q;
             covered_by_target t0 (targets_go acc' t1 rest) q
           end
           else begin
             match relocated o with
             | Some x ->
               parent_inv_elim t0 t acc x q;
               if mem q acc then begin
                 targets_go_mono acc' t1 rest q;
                 covered_by_target t0 (targets_go acc' t1 rest) q
               end
               else begin
                 targets_go_mono acc' t1 rest x;
                 covered_by_child t0 (targets_go acc' t1 rest) q x
               end
             | None -> ()
           end
         end
         else begin
           written_split q t t1 t';
           parent_inv_step t0 t acc o t1;
           go_cover t0 acc' t1 rest t' q
         end
       | Error _ -> ())

(* THEOREM — targets_cover_written. Every id an applied op writes — created, destroyed, rewritten,
   or its child list changed, a `Batch` included at any depth — is among `WriteGate.targetsOf`'s
   targets, or is the parent, in the tree the op was applied to, of an id that is. The second
   disjunct is the source parent of a removed or moved node: the one written id `targetsOf` does
   not name, and the exception the cover law states. *)
let targets_cover_written (o:op) (t t':tree) (q:string)
  : Lemma (requires wf t /\ apply o t == Ok t' /\ written q t t')
          (ensures mem q (targets_of o t) \/
                   (exists (y:string). mem y (targets_of o t) /\ parent_of y t == Some q))
  = go_cover t [] t [o] t' q
#pop-options

(* ======================================================================================
   7. The gate — `chainOf`, `decideOne`, `decide`, `applyGated`, clause for clause.
   ====================================================================================== *)

(* F#: `WriteGate` — `Locked` and the optional allow-list `Writable`, sets read as lists. *)
type gate = {
  locked   : list string;
  writable : option (list string)
}

(* F#: `WriteDenial`. *)
type denial =
  | Locked      : target:string -> locked_by:string -> denial
  | NotWritable : target:string -> allowed:list string -> denial

(* F#: `GatedApplyFailure`. *)
type gated_failure =
  | GateDenied   : denial -> gated_failure
  | GateRejected : rejection -> gated_failure

(* F#: `Tree.path` — the ids from the root down to the first preorder node carrying `x`. *)
let rec path_in (x:string) (t:tree) : Tot (option (list string)) (decreases t) =
  match t with
  | TNode i _ cs ->
    if i = x then Some [i]
    else (match path_all x cs with
          | Some p -> Some (i :: p)
          | None -> None)
and path_all (x:string) (ts:list tree) : Tot (option (list string)) (decreases ts) =
  match ts with
  | [] -> None
  | t :: r -> (match path_in x t with
               | Some p -> Some p
               | None -> path_all x r)

(* F#: `inTree` inside `chainOf` — `Tree.path |> Option.map List.rev`: nearest first. *)
let in_tree (x:string) (t:tree) : Tot (option (list string)) =
  match path_in x t with
  | Some p -> Some (rev p)
  | None -> None

(* F#: `chainOf` — the target and its ancestors; for a node an `InsertChild` creates, the chain
   through the graft and on up from the insert's parent. *)
let chain_of (o:op) (t:tree) (target:string) : Tot (list string) =
  match in_tree target t with
  | Some chain -> chain
  | None ->
    (match o with
     | InsertChild parent graft ->
       let in_graft = (match in_tree target graft with
                       | Some c -> c
                       | None -> [target]) in
       let above = (match in_tree parent t with
                    | Some c -> c
                    | None -> [parent]) in
       app in_graft above
     | _ -> [target])

(* F#: `List.tryFind gate.Locked.Contains`. *)
let rec first_in (l s:list string) : Tot (option string) =
  match l with
  | [] -> None
  | h :: r -> if mem h s then Some h else first_in r s

(* F#: the `locked` binding of `decideOne` — `List.tryPick` over the targets' chains. *)
let rec first_locked (lk:list string) (o:op) (t:tree) (targets:list string) : Tot (option denial) =
  match targets with
  | [] -> None
  | x :: r -> (match first_in (chain_of o t x) lk with
               | Some who -> Some (Locked x who)
               | None -> first_locked lk o t r)

(* F#: the allow-list arm — the first target none of whose chain is on the list. *)
let rec first_uncovered (w:list string) (o:op) (t:tree) (targets:list string) : Tot (option string) =
  match targets with
  | [] -> None
  | x :: r -> (match first_in (chain_of o t x) w with
               | None -> Some x
               | Some _ -> first_uncovered w o t r)

(* F#: `decideOne`. *)
let decide_one (g:gate) (o:op) (t:tree) : Tot (outcome unit denial) =
  let targets = targets_of_one o t in
  match first_locked g.locked o t targets, g.writable with
  | Some d, _ -> Error d
  | None, None -> Ok ()
  | None, Some w -> (match first_uncovered w o t targets with
                     | Some x -> Error (NotWritable x w)
                     | None -> Ok ())

#push-options "--fuel 2 --ifuel 1"
(* F#: the `go` loop of `decide` — a `Batch` flattened, each op judged where it lands, and the first
   op that does not apply ends the decision `Ok` (the reducer refuses it). *)
let rec decide_go (g:gate) (t:tree) (os:list op)
  : Tot (outcome unit denial) (decreases (ops_size os)) =
  match os with
  | [] -> Ok ()
  | Batch inner :: rest -> ops_size_app inner rest; decide_go g t (app inner rest)
  | o :: rest ->
    op_size_pos o;
    (match decide_one g o t with
     | Error d -> Error d
     | Ok () -> (match apply o t with
                 | Ok t1 -> decide_go g t1 rest
                 | Error _ -> Ok ()))
#pop-options

(* F#: `WriteGate.decide`. *)
let decide (g:gate) (o:op) (t:tree) : Tot (outcome unit denial) = decide_go g t [o]

(* F#: `WriteGate.applyGated` — the gate first; a denial returns before the reducer runs. *)
let apply_gated (g:gate) (o:op) (t:tree) : Tot (outcome tree gated_failure) =
  match decide g o t with
  | Error d -> Error (GateDenied d)
  | Ok () -> (match apply o t with
              | Ok t' -> Ok t'
              | Error r -> Error (GateRejected r))

(* ---- what a chain holds ---- *)

#push-options "--fuel 1 --ifuel 1"
let rec path_some_iff (x:string) (t:tree)
  : Lemma (ensures Some? (path_in x t) == mem x (ids t)) (decreases t)
  = match t with
    | TNode i _ cs -> if i = x then () else path_all_some_iff x cs
and path_all_some_iff (x:string) (ts:list tree)
  : Lemma (ensures Some? (path_all x ts) == mem x (ids_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> path_some_iff x t; path_all_some_iff x r

(* The path ends at its target … *)
let rec path_has_target (x:string) (t:tree)
  : Lemma (ensures (match path_in x t with
                    | Some p -> mem x p
                    | None -> True)) (decreases t)
  = match t with
    | TNode i _ cs -> if i = x then () else path_all_has_target x cs
and path_all_has_target (x:string) (ts:list tree)
  : Lemma (ensures (match path_all x ts with
                    | Some p -> mem x p
                    | None -> True)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> path_has_target x t; path_all_has_target x r

(* … and passes through its target's parent. *)
let rec parent_on_path (x p:string) (t:tree)
  : Lemma (requires wf t /\ parent_of x t == Some p)
          (ensures (match path_in x t with
                    | Some pa -> mem p pa
                    | None -> False)) (decreases t)
  = match t with
    | TNode i _ cs ->
      if has_kid x cs then begin
        has_kid_is_mem x cs;
        kid_ids_sub x cs;
        path_all_some_iff x cs
      end
      else begin
        parent_all_mem x cs p;
        parent_on_path_all x p cs
      end
and parent_on_path_all (x p:string) (ts:list tree)
  : Lemma (requires wf_all ts /\ parent_all x ts == Some p)
          (ensures (match path_all x ts with
                    | Some pa -> mem p pa
                    | None -> False)) (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r ->
      inter_nil_iff (ids c) (ids_all r);
      path_some_iff x c;
      (match parent_of x c with
       | Some _ -> parent_on_path x p c
       | None -> parent_all_mem x r p; parent_on_path_all x p r)
#pop-options

#push-options "--fuel 2 --ifuel 1"
(* A target is on its own chain: a lock on the target itself is seen. *)
let chain_has_target (o:op) (t:tree) (x:string)
  : Lemma (ensures mem x (chain_of o t x))
  = path_has_target x t;
    match in_tree x t with
    | Some _ -> ()
    | None ->
      (match o with
       | InsertChild _ graft -> path_has_target x graft
       | _ -> ())

(* A target's parent is on its chain: a lock on the source parent of a removed or moved node
   reaches the node, which is why `targetsOf` need not name that parent. *)
let chain_has_parent (o:op) (t:tree) (x p:string)
  : Lemma (requires wf t /\ parent_of x t == Some p) (ensures mem p (chain_of o t x))
  = parent_on_path x p t

let rec first_in_none (l s:list string) (y:string)
  : Lemma (requires first_in l s == None /\ mem y l) (ensures not (mem y s)) (decreases l)
  = match l with
    | [] -> ()
    | h :: r -> if h = y then () else first_in_none r s y

let rec first_locked_none (lk:list string) (o:op) (t:tree) (targets:list string) (x:string)
  : Lemma (requires first_locked lk o t targets == None /\ mem x targets)
          (ensures first_in (chain_of o t x) lk == None) (decreases targets)
  = match targets with
    | [] -> ()
    | h :: r -> if h = x then () else first_locked_none lk o t r x

(* What an `Ok` verdict means for locks: no locked id is on any target's chain. *)
let decide_one_clear (g:gate) (o:op) (t:tree) (x y:string)
  : Lemma (requires decide_one g o t == Ok () /\ mem x (targets_of_one o t) /\
                    mem y (chain_of o t x))
          (ensures not (mem y g.locked))
  = first_locked_none g.locked o t (targets_of_one o t) x;
    first_in_none (chain_of o t x) g.locked y

(* ======================================================================================
   8. A GATED APPLY LEAVES THE LOCKED IDS ALONE.
   ====================================================================================== *)

(* One allowed leaf op writes no locked id: a written id is a target, whose own chain the gate
   read, or the source parent of one, which is on that target's chain. *)
let leaf_respects_lock (g:gate) (o:op) (t t':tree) (l:string)
  : Lemma (requires is_leaf o /\ wf t /\ decide_one g o t == Ok () /\ apply o t == Ok t' /\
                    mem l g.locked)
          (ensures not (written l t t'))
  = if written l t t' then begin
      leaf_cover o t t' l;
      if mem l (targets_of_one o t) then begin
        chain_has_target o t l;
        decide_one_clear g o t l l
      end
      else begin
        match relocated o with
        | Some x -> chain_has_parent o t x l; decide_one_clear g o t x l
        | None -> ()
      end
    end
    else ()

let rec go_respects_lock (g:gate) (t:tree) (os:list op) (t':tree) (l:string)
  : Lemma (requires wf t /\ decide_go g t os == Ok () /\ apply_all os t == Ok t' /\
                    mem l g.locked)
          (ensures not (written l t t')) (decreases (ops_size os))
  = match os with
    | [] -> ()
    | Batch inner :: rest ->
      ops_size_app inner rest;
      TreeDiff.apply_all_app inner rest t;
      go_respects_lock g t (app inner rest) t' l
    | o :: rest ->
      op_size_pos o;
      (match apply o t with
       | Ok t1 ->
         apply_preserves_wf o t;
         leaf_respects_lock g o t t1 l;
         go_respects_lock g t1 rest t' l;
         written_trans l t t1 t'
       | Error _ -> ())

#pop-options

(* ---- a lock covers its subtree ---- *)

#push-options "--fuel 1 --ifuel 1"
(* A node's ancestors are on its path: the chain the gate reads for a target passes through every
   node whose subtree holds that target. *)
let rec ancestor_on_path (l x:string) (t s:tree)
  : Lemma (requires wf t /\ find_in l t == Some s /\ mem x (ids s))
          (ensures (match path_in x t with
                    | Some pa -> mem l pa
                    | None -> False)) (decreases t)
  = match t with
    | TNode i _ cs ->
      if i = l then (find_in_sub l x t s; path_some_iff x t)
      else begin
        find_all_sub l x cs s;
        ancestor_on_path_all l x cs s
      end
and ancestor_on_path_all (l x:string) (ts:list tree) (s:tree)
  : Lemma (requires wf_all ts /\ find_all l ts == Some s /\ mem x (ids s))
          (ensures (match path_all x ts with
                    | Some pa -> mem l pa
                    | None -> False)) (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r ->
      inter_nil_iff (ids c) (ids_all r);
      path_some_iff x c;
      (match find_in l c with
       | Some _ -> find_in_sub l x c s; ancestor_on_path l x c s
       | None -> find_all_sub l x r s; ancestor_on_path_all l x r s)

let chain_has_ancestor (o:op) (t:tree) (l x:string) (s:tree)
  : Lemma (requires wf t /\ find_in l t == Some s /\ mem x (ids s))
          (ensures mem l (chain_of o t x))
  = ancestor_on_path l x t s

(* A child of a node inside a subtree is inside it. *)
let kid_in_subtree (l x y:string) (t s:tree)
  : Lemma (requires wf t /\ find_in l t == Some s /\ mem x (ids s) /\ parent_of y t == Some x)
          (ensures mem y (ids s))
  = find_in_trans l x t s;
    parent_holds_kid y x t;
    match find_in x s with
    | Some n ->
      (match n with TNode _ _ cs -> kid_ids_sub y cs);
      find_in_sub x y s n
    | None -> ()

(* One allowed leaf op writes nothing inside a locked node's subtree: every id there has the
   locked node on its chain, and so does a child of it that the op removes or moves. *)
let leaf_respects_lock_under (g:gate) (o:op) (t t':tree) (l:string) (s:tree) (x:string)
  : Lemma (requires is_leaf o /\ wf t /\ decide_one g o t == Ok () /\ apply o t == Ok t' /\
                    mem l g.locked /\ find_in l t == Some s /\ mem x (ids s))
          (ensures not (written x t t'))
  = if written x t t' then begin
      leaf_cover o t t' x;
      if mem x (targets_of_one o t) then begin
        chain_has_ancestor o t l x s;
        decide_one_clear g o t x l
      end
      else begin
        match relocated o with
        | Some y ->
          kid_in_subtree l x y t s;
          chain_has_ancestor o t l y s;
          decide_one_clear g o t y l
        | None -> ()
      end
    end
    else ()

(* … so the locked node's subtree is the very subtree it was: the two trees show the same thing at
   every id in it, and a tree is determined by what it shows (`TreeDiff.tree_ext`). *)
let leaf_freezes_lock (g:gate) (o:op) (t t':tree) (l:string) (s:tree)
  : Lemma (requires is_leaf o /\ wf t /\ decide_one g o t == Ok () /\ apply o t == Ok t' /\
                    mem l g.locked /\ find_in l t == Some s)
          (ensures find_in l t' == Some s)
  = apply_preserves_wf o t;
    find_in_wf l t s;
    find_in_id l t s;
    find_in_self l s;
    leaf_respects_lock g o t t' l;
    let aux (q:string)
      : Lemma (mem q (ids s) ==> (kids_at q t' == kids_at q s /\ kind_at q t' == kind_at q s))
      = if mem q (ids s) then (leaf_respects_lock_under g o t t' l s q; find_in_trans l q t s)
        else ()
    in
    FStar.Classical.forall_intro aux;
    match find_in l t' with
    | Some s2 -> TreeDiff.tree_ext s t' s s2
    | None -> ()
#pop-options

#push-options "--fuel 2 --ifuel 1"
let rec go_freezes_lock (g:gate) (t:tree) (os:list op) (t':tree) (l:string) (s:tree)
  : Lemma (requires wf t /\ decide_go g t os == Ok () /\ apply_all os t == Ok t' /\
                    mem l g.locked /\ find_in l t == Some s)
          (ensures find_in l t' == Some s) (decreases (ops_size os))
  = match os with
    | [] -> ()
    | Batch inner :: rest ->
      ops_size_app inner rest;
      TreeDiff.apply_all_app inner rest t;
      go_freezes_lock g t (app inner rest) t' l s
    | o :: rest ->
      op_size_pos o;
      (match apply o t with
       | Ok t1 ->
         apply_preserves_wf o t;
         leaf_freezes_lock g o t t1 l s;
         go_freezes_lock g t1 rest t' l s
       | Error _ -> ())

(* THEOREM — gated_apply_respects_lock. When `applyGated` succeeds on a well-formed tree, every
   locked id is exactly as it was: a locked node the tree held is still there with its WHOLE SUBTREE
   unchanged — nothing in it was rewritten, reordered, added to, moved out or removed, on its own or
   inside a subtree a removal destroyed — and a locked id the tree did not hold was not created.
   `find_in l` answering the same is that sentence; `not (written l t t')` is its first consequence,
   stated because it is the form the cover law's vocabulary uses. *)
let gated_apply_respects_lock (g:gate) (o:op) (t t':tree) (l:string)
  : Lemma (requires wf t /\ apply_gated g o t == Ok t' /\ mem l g.locked)
          (ensures find_in l t' == find_in l t /\ not (written l t t'))
  = go_respects_lock g t [o] t' l;
    match find_in l t with
    | Some s -> go_freezes_lock g t [o] t' l s
    | None -> ()

(* … and its reading at every id UNDER a lock, which is what "a lock on a node locks its subtree"
   promises a caller: nothing there was written. *)
let nothing_under_a_lock_is_written (g:gate) (o:op) (t t':tree) (l x:string)
  : Lemma (requires wf t /\ apply_gated g o t == Ok t' /\ mem l g.locked /\
                    mem x (subtree_ids l t))
          (ensures not (written x t t'))
  = gated_apply_respects_lock g o t t' l;
    apply_preserves_wf o t;
    match find_in l t with
    | Some s -> find_in_trans l x t s; find_in_trans l x t' s
    | None -> ()
#pop-options

(* ---- the two boundaries, as witnesses ---- *)

let lock_tree : tree = TNode "root" "doc" [ TNode "a" "sec" []; TNode "b" "sec" [] ]

(* A LOCK IS ON THE NODE'S ENTRY, NOT ON ITS PLACE. `a` is locked and `root` is not; reordering
   `root`'s children is allowed, `a` is not written — and `a` is now second. The order of a child
   list is its parent's state, and it is the parent that is written. *)
let lock_gate : gate = { locked = ["a"]; writable = None }

let lock_reorder : op = ReorderChildren "root" ["b"; "a"]

let lock_reordered : tree = TNode "root" "doc" [ TNode "b" "sec" []; TNode "a" "sec" [] ]

let lock_does_not_pin_sibling_place ()
  : Lemma (ensures apply_gated lock_gate lock_reorder lock_tree == Ok lock_reordered /\
                   not (written "a" lock_tree lock_reordered) /\
                   written "root" lock_tree lock_reordered)
  = assert_norm (apply_gated lock_gate lock_reorder lock_tree == Ok lock_reordered);
    assert_norm (not (written "a" lock_tree lock_reordered));
    assert_norm (written "root" lock_tree lock_reordered)

(* THE ALLOW-LIST HAS NO SUCH THEOREM. The list allows `a` and nothing else; removing `a` is
   allowed, and it rewrites `root`'s child list — `root` is written, and nothing on `root`'s chain
   is on the list. A lock on a source parent reaches the removed node, so leaving that parent out
   of the targets costs a lock nothing; an allowance on the removed node does not reach its parent. *)
let allow_gate : gate = { locked = []; writable = Some ["a"] }

let allow_remove : op = RemoveNode "a"

let allow_removed : tree = TNode "root" "doc" [ TNode "b" "sec" [] ]

let allow_list_does_not_cover_the_source_parent ()
  : Lemma (ensures apply_gated allow_gate allow_remove lock_tree == Ok allow_removed /\
                   written "root" lock_tree allow_removed /\
                   first_in (chain_of allow_remove lock_tree "root") ["a"] == None)
  = assert_norm (apply_gated allow_gate allow_remove lock_tree == Ok allow_removed);
    assert_norm (written "root" lock_tree allow_removed);
    assert_norm (first_in (chain_of allow_remove lock_tree "root") ["a"] == None)
