(*
   PropagationOps — the change set of a structural edit is COMPLETE: `Propagation.changedForOp`
   joined with `Ops.apply` (fuaran-core Phase 308).

   WHAT IS MODELLED. `src/Fuaran.Core.Propagation/Propagation.fs` — the two functions that turn a
   structural edit into the change set `evalFrom` replays:

     - `touchedBy`, every arm, with Phase 308's two corrections: a `RemoveNode` touches the parent
       it removes from and a `MoveNode` the parent it leaves, and a `Batch` resolves each sub-op
       against the tree the sub-ops before it produced (`Ops.apply`, threaded; a refused sub-op is
       resolved against the tree in hand and the threading continues from it);
     - `changedForOp`, derived from the DIFF of the pre- and post-edit trees: the seeds are every
       post-edit id that is new, whose child ids differ, whose reads differ or that the op
       content-writes (`Ops.footprint`), every removed id, and `touchedBy`; closed over the PRE-edit
       dependency graph (`Propagation.dirtyFromChangedIds` over `dependencyMap`, here
       `Propagation.dirty_from_changed_ids` over `deps_of`) and restricted to the survivors.

   The tree, the ops, `apply` and the footprint are `TreeOps.fst`'s, clause for clause with
   `Ops.apply`; the closure is `Propagation.fst`'s. A node's CONTENT is what the witness shows of it,
   its kind tag, exactly as `TreeOps.fst` reads `UpdateNode`. The reads a node declares are a
   PARAMETER (`rd`, F#: the caller's `readsOf`), any function of the node and its subtree.

   WHAT IS PROVED.

     - `changed_for_op_complete` — for an op `apply` accepts on a well-formed tree, every surviving
       node that is NEW, whose content (kind) differs, whose child ids differ or whose declared reads
       differ, and every surviving node that declared a read of an id the edit REMOVED, is in the
       change set. `Batch` included, at any depth: the diff compares the two ends, and the one
       clause that needs the op — that content moves ONLY where the op content-writes — is proved
       over `apply` itself (`kind_kept`), threading a batch through every intermediate tree.

     That is the premise `evalfrom_agrees` takes of a change set (`agree_off` / `touches_off`) for
   every evaluator that is a function of a node's own content, its child ids and its resolved
   declared reads: such an evaluator moves only at a node whose content, children or reads moved,
   or whose read now resolves differently — a removed read (named here), or a read whose value moved
   (reached by `evalFrom`'s own closure over the post-edit graph, `dirty_sound`).

   WHAT IS NOT CLAIMED. That an evaluator a domain runs is in that class: one that reads a node's
   whole subtree without declaring the reads is not, and is what declared reads are for. That the
   content the domain's encoder distinguishes is the kind tag — the witness shows the core nothing
   else, and `Ops.apply` rewrites content nowhere but at an `UpdateNode` target or an inserted
   subtree, which the footprint names. That `post` is `apply`'s result for an op some OTHER engine
   accepted: the theorem is about `Ops.apply`, and a domain engine (contained, keyed) that accepts
   an op lands on the tree `apply` lands on whenever `apply` accepts it too. Over an ill-formed tree
   (an id twice) the statement is not made: `find` resolves to the first occurrence there, and the
   two ends can disagree about which node an id means.

   Apache-2.0, like everything beside it.
*)
module PropagationOps

open DagFold
open TreeOps
module P = Propagation

#set-options "--ext context_pruning"

(* ======================================================================================
   1. The bridge between the two models' membership. `Propagation.fst` is self-contained and
      restates `mem` over strings; it is the same function as `DagFold.mem` at strings.
   ====================================================================================== *)

let rec mem_bridge (x:string) (l:list string)
  : Lemma (ensures P.mem x l == mem x l) (decreases l) =
  match l with
  | [] -> ()
  | _ :: t -> mem_bridge x t

(* ======================================================================================
   2. The production functions, clause for clause.
   ====================================================================================== *)

(* F#: `List.filter`. *)
let rec filt (f:string -> bool) (l:list string) : Tot (list string) (decreases l) =
  match l with
  | [] -> []
  | h :: t -> if f h then h :: filt f t else filt f t

let rec mem_filt (f:string -> bool) (l:list string) (x:string)
  : Lemma (ensures mem x (filt f l) == (mem x l && f x)) (decreases l) =
  match l with
  | [] -> ()
  | _ :: t -> mem_filt f t x

(* F#: `Tree.preorder`. *)
let rec nodes (t:tree) : Tot (list tree) (decreases t) =
  match t with
  | TNode _ _ cs -> t :: nodes_all cs
and nodes_all (ts:list tree) : Tot (list tree) (decreases ts) =
  match ts with
  | [] -> []
  | t :: r -> app (nodes t) (nodes_all r)

(* F#: `Propagation.dependencyMap` — each node's id and the reads it declares. *)
let rec deps_of_list (rd:tree -> list string) (ns:list tree) : Tot P.dmap (decreases ns) =
  match ns with
  | [] -> []
  | n :: r -> (tid_of n, rd n) :: deps_of_list rd r

let deps_of (rd:tree -> list string) (t:tree) : Tot P.dmap = deps_of_list rd (nodes t)

(* F#: the `subtreeIds` and `parentIds` helpers inside `touchedBy`. *)
let subtree_ids (x:string) (t:tree) : Tot (list string) =
  match find_in x t with
  | Some n -> ids n
  | None -> [x]

let parent_ids (x:string) (t:tree) : Tot (list string) =
  match parent_of x t with
  | Some p -> [p]
  | None -> []

(* F#: the `next` tree a `Batch` threads — `apply`'s result, or the tree in hand on a refusal. *)
let next_tree (o:op) (t:tree) : Tot tree =
  match apply o t with
  | Ok t' -> t'
  | Error _ -> t

(* F#: `Propagation.touchedBy`. *)
let rec touched_by (t:tree) (o:op) : Tot (list string) (decreases o) =
  match o with
  | InsertChild p n -> p :: ids n
  | RemoveNode x -> app (subtree_ids x t) (parent_ids x t)
  | MoveNode x np -> app [x; np] (parent_ids x t)
  | ReorderChildren p _ -> [p]
  | Batch os -> touched_all t os
  | UpdateNode n -> [tid_of n]
and touched_all (t:tree) (os:list op) : Tot (list string) (decreases os) =
  match os with
  | [] -> []
  | o :: r -> app (touched_by t o) (touched_all (next_tree o t) r)

(* F#: the `kids` and `reads` comparisons inside `changedForOp`'s `local`. The reads are a `Set`
   there, so they are compared as sets here. *)
let same_reads (a b:list string) : Tot bool = P.subset a b && P.subset b a

(* F#: one row of `local` — is this post-edit id a seed? *)
let differs (rd:tree -> list string) (o:op) (pre post:tree) (x:string) : Tot bool =
  match find_in x pre, find_in x post with
  | None, _ -> true
  | Some m, Some n ->
    mem x (op_fp o).content_writes
    || kid_ids (kids_of m) <> kid_ids (kids_of n)
    || not (same_reads (rd m) (rd n))
  | Some _, None -> false

let survives (post:tree) (x:string) : Tot bool = has_id x post
let gone (post:tree) (x:string) : Tot bool = not (has_id x post)

(* F#: the `local` set, the `removed` list and the seed union. *)
let local (rd:tree -> list string) (o:op) (pre post:tree) : Tot (list string) =
  filt (differs rd o pre post) (ids post)

let removed (pre post:tree) : Tot (list string) = filt (gone post) (ids pre)

let seeds (rd:tree -> list string) (o:op) (pre post:tree) : Tot (list string) =
  app (local rd o pre post) (app (removed pre post) (touched_by pre o))

(* F#: `Propagation.changedForOp`. *)
let changed_for_op (rd:tree -> list string) (pre post:tree) (o:op) : Tot (list string) =
  filt (survives post) (P.dirty_from_changed_ids (deps_of rd pre) (seeds rd o pre post))

(* ======================================================================================
   3. Content moves only where the op content-writes.

      `kinds_of x t` is every kind a node carrying `x` has in `t`, in preorder — one entry on a
      well-formed tree. Each edit that does not content-write `x` leaves no kind for `x` that the
      tree before it did not have, and that composes through a `Batch` with no well-formedness
      needed at the intermediate trees; well-formedness of the START tree then pins the one kind.
   ====================================================================================== *)

let rec kinds_of (x:string) (t:tree) : Tot (list string) (decreases t) =
  match t with
  | TNode i k cs -> if i = x then k :: kinds_all x cs else kinds_all x cs
and kinds_all (x:string) (ts:list tree) : Tot (list string) (decreases ts) =
  match ts with
  | [] -> []
  | t :: r -> app (kinds_of x t) (kinds_all x r)

let rec kinds_all_app (x:string) (l m:list tree)
  : Lemma (ensures kinds_all x (app l m) == app (kinds_all x l) (kinds_all x m)) (decreases l) =
  match l with
  | [] -> ()
  | t :: r -> kinds_all_app x r m; app_assoc (kinds_of x t) (kinds_all x r) (kinds_all x m)

let rec kinds_absent (x:string) (t:tree)
  : Lemma (requires not (mem x (ids t))) (ensures kinds_of x t == []) (decreases t) =
  match t with
  | TNode _ _ cs -> kinds_absent_all x cs
and kinds_absent_all (x:string) (ts:list tree)
  : Lemma (requires not (mem x (ids_all ts))) (ensures kinds_all x ts == []) (decreases ts) =
  match ts with
  | [] -> ()
  | t :: r -> kinds_absent x t; kinds_absent_all x r

let rec kinds_member (x k:string) (c:tree) (cs:list tree)
  : Lemma (requires mem c cs /\ mem k (kinds_of x c)) (ensures mem k (kinds_all x cs)) (decreases cs) =
  match cs with
  | [] -> ()
  | t :: r -> if t = c then () else kinds_member x k c r

(* ---- an insert adds the inserted subtree's kinds and no others ---- *)

let rec kinds_ins (x k p:string) (n t:tree)
  : Lemma (requires mem k (kinds_of x (ins p n t)))
          (ensures mem k (kinds_of x t) || mem k (kinds_of x n)) (decreases t) =
  match t with
  | TNode i kk cs ->
    if i = p then begin
      kinds_all_app x (ins_all p n cs) [n];
      (if mem k (kinds_all x (ins_all p n cs)) then kinds_ins_all x k p n cs else ())
    end
    else (if i = x && k = kk then () else kinds_ins_all x k p n cs)
and kinds_ins_all (x k p:string) (n:tree) (ts:list tree)
  : Lemma (requires mem k (kinds_all x (ins_all p n ts)))
          (ensures mem k (kinds_all x ts) || mem k (kinds_of x n)) (decreases ts) =
  match ts with
  | [] -> ()
  | t :: r ->
    if mem k (kinds_of x (ins p n t)) then kinds_ins x k p n t else kinds_ins_all x k p n r

(* ---- a removal and a reorder add none ---- *)

let rec kinds_drop (x k y:string) (ts:list tree)
  : Lemma (requires mem k (kinds_all x (drop_kid y ts))) (ensures mem k (kinds_all x ts)) (decreases ts) =
  match ts with
  | [] -> ()
  | t :: r -> if tid_of t = y then kinds_drop x k y r
              else (if mem k (kinds_of x t) then () else kinds_drop x k y r)

let rec kinds_rem (x k pid y:string) (t:tree)
  : Lemma (requires mem k (kinds_of x (rem_at pid y t))) (ensures mem k (kinds_of x t)) (decreases t) =
  match t with
  | TNode i kk cs ->
    if i = x && k = kk then ()
    else if i = pid then (kinds_drop x k y (rem_all pid y cs); kinds_rem_all x k pid y cs)
    else kinds_rem_all x k pid y cs
and kinds_rem_all (x k pid y:string) (ts:list tree)
  : Lemma (requires mem k (kinds_all x (rem_all pid y ts))) (ensures mem k (kinds_all x ts)) (decreases ts) =
  match ts with
  | [] -> ()
  | t :: r ->
    if mem k (kinds_of x (rem_at pid y t)) then kinds_rem x k pid y t else kinds_rem_all x k pid y r

let rec kinds_arrange (x k:string) (order:list string) (ts:list tree)
  : Lemma (requires mem k (kinds_all x (arrange order ts))) (ensures mem k (kinds_all x ts))
          (decreases order) =
  match order with
  | [] -> ()
  | y :: rest ->
    (match pick_last y ts with
     | Some c ->
       if mem k (kinds_of x c) then (pick_last_mem y ts c; kinds_member x k c ts)
       else kinds_arrange x k rest ts
     | None -> kinds_arrange x k rest ts)

let rec kinds_reorder (x k p:string) (order:list string) (t:tree)
  : Lemma (requires mem k (kinds_of x (reorder_at p order t))) (ensures mem k (kinds_of x t)) (decreases t) =
  match t with
  | TNode i kk cs ->
    if i = x && k = kk then ()
    else if i = p then (kinds_arrange x k order (reorder_all p order cs); kinds_reorder_all x k p order cs)
    else kinds_reorder_all x k p order cs
and kinds_reorder_all (x k p:string) (order:list string) (ts:list tree)
  : Lemma (requires mem k (kinds_all x (reorder_all p order ts))) (ensures mem k (kinds_all x ts))
          (decreases ts) =
  match ts with
  | [] -> ()
  | t :: r ->
    if mem k (kinds_of x (reorder_at p order t)) then kinds_reorder x k p order t
    else kinds_reorder_all x k p order r

(* ---- an update moves only its own target's kind ---- *)

let rec kinds_upd (x y kn:string) (t:tree)
  : Lemma (requires x <> y) (ensures kinds_of x (upd y kn t) == kinds_of x t) (decreases t) =
  match t with
  | TNode _ _ cs -> kinds_upd_all x y kn cs
and kinds_upd_all (x y kn:string) (ts:list tree)
  : Lemma (requires x <> y) (ensures kinds_all x (upd_all y kn ts) == kinds_all x ts) (decreases ts) =
  match ts with
  | [] -> ()
  | t :: r -> kinds_upd x y kn t; kinds_upd_all x y kn r

(* ---- a found subtree carries only kinds the tree has ---- *)

let rec kinds_find (x k y:string) (t sub:tree)
  : Lemma (requires find_in y t == Some sub /\ mem k (kinds_of x sub)) (ensures mem k (kinds_of x t))
          (decreases t) =
  match t with
  | TNode i _ cs -> if i = y then () else kinds_find_all x k y cs sub
and kinds_find_all (x k y:string) (ts:list tree) (sub:tree)
  : Lemma (requires find_all y ts == Some sub /\ mem k (kinds_of x sub)) (ensures mem k (kinds_all x ts))
          (decreases ts) =
  match ts with
  | [] -> ()
  | t :: r ->
    (match find_in y t with
     | Some _ -> kinds_find x k y t sub
     | None -> kinds_find_all x k y r sub)

(* ---- the footprint's content writes, through a batch ---- *)

let fp_all_cons (x:string) (o:op) (r:list op)
  : Lemma (ensures mem x (fp_all (o :: r)).content_writes ==
                   (mem x (op_fp o).content_writes || mem x (fp_all r).content_writes)) =
  ()

(* THE STEP LEMMA. An op `apply` accepts that does not content-write `x` leaves no kind for `x`
   that the tree before it did not have. *)
let rec kinds_kept (x k:string) (o:op) (t t':tree)
  : Lemma (requires apply o t == Ok t' /\ not (mem x (op_fp o).content_writes) /\ mem k (kinds_of x t'))
          (ensures mem k (kinds_of x t)) (decreases o) =
  match o with
  | InsertChild p n -> kinds_ins x k p n t; kinds_absent x n
  | RemoveNode y ->
    (match parent_of y t with
     | Some pid -> kinds_rem x k pid y t
     | None -> ())
  | MoveNode y np ->
    (match find_in y t with
     | Some sub ->
       (match parent_of y t with
        | Some pid ->
          let rm = rem_at pid y t in
          kinds_ins x k np sub rm;
          if mem k (kinds_of x rm) then kinds_rem x k pid y t else kinds_find x k y t sub
        | None -> ())
     | None -> ())
  | ReorderChildren p order -> kinds_reorder x k p order t
  | Batch os -> kinds_kept_all x k os t t'
  | UpdateNode n -> kinds_upd x (tid_of n) (kind_of n) t
and kinds_kept_all (x k:string) (os:list op) (t t':tree)
  : Lemma (requires apply_all os t == Ok t' /\ not (mem x (fp_all os).content_writes) /\
                    mem k (kinds_of x t'))
          (ensures mem k (kinds_of x t)) (decreases os) =
  match os with
  | [] -> ()
  | o :: r ->
    fp_all_cons x o r;
    (match apply o t with
     | Ok tm -> kinds_kept_all x k r tm t'; kinds_kept x k o t tm
     | Error _ -> ())

(* ---- well-formedness pins one kind per id ---- *)

let rec kind_found (x:string) (t n:tree)
  : Lemma (requires find_in x t == Some n) (ensures mem (kind_of n) (kinds_of x t)) (decreases t) =
  match t with
  | TNode i _ cs -> if i = x then () else kind_found_all x cs n
and kind_found_all (x:string) (ts:list tree) (n:tree)
  : Lemma (requires find_all x ts == Some n) (ensures mem (kind_of n) (kinds_all x ts)) (decreases ts) =
  match ts with
  | [] -> ()
  | t :: r ->
    (match find_in x t with
     | Some _ -> kind_found x t n
     | None -> kind_found_all x r n)

let rec kinds_unique (x k1 k2:string) (t:tree)
  : Lemma (requires wf t /\ mem k1 (kinds_of x t) /\ mem k2 (kinds_of x t)) (ensures k1 = k2) (decreases t) =
  match t with
  | TNode i _ cs ->
    if i = x then kinds_absent_all x cs else kinds_unique_all x k1 k2 cs
and kinds_unique_all (x k1 k2:string) (ts:list tree)
  : Lemma (requires wf_all ts /\ mem k1 (kinds_all x ts) /\ mem k2 (kinds_all x ts)) (ensures k1 = k2)
          (decreases ts) =
  match ts with
  | [] -> ()
  | t :: r ->
    if mem x (ids t) then begin
      mem_inter x (ids t) (ids_all r);
      kinds_absent_all x r;
      kinds_unique x k1 k2 t
    end
    else (kinds_absent x t; kinds_unique_all x k1 k2 r)

(* THEOREM — kind_kept. On a well-formed tree, an op `apply` accepts changes the content of a node
   that survives it ONLY where the op content-writes that node. *)
let kind_kept (o:op) (pre post:tree) (x:string) (m n:tree)
  : Lemma (requires wf pre /\ apply o pre == Ok post /\ find_in x pre == Some m /\ find_in x post == Some n /\
                    not (mem x (op_fp o).content_writes))
          (ensures kind_of n = kind_of m) =
  kind_found x post n;
  kinds_kept x (kind_of n) o pre post;
  kind_found x pre m;
  kinds_unique x (kind_of n) (kind_of m) pre

(* ======================================================================================
   4. Completeness.
   ====================================================================================== *)

(* A node the change set must name, read off the two trees: new, or its content, child ids or
   declared reads differ. *)
let moved (rd:tree -> list string) (pre post:tree) (x:string) : Tot bool =
  match find_in x pre, find_in x post with
  | None, Some _ -> true
  | Some m, Some n ->
    kind_of m <> kind_of n || kid_ids (kids_of m) <> kid_ids (kids_of n) || not (same_reads (rd m) (rd n))
  | _, None -> false

let seed_named (rd:tree -> list string) (pre post:tree) (o:op) (x:string)
  : Lemma (requires has_id x post /\ P.mem x (seeds rd o pre post))
          (ensures P.mem x (changed_for_op rd pre post o)) =
  let s = seeds rd o pre post in
  let d = P.dirty_from_changed_ids (deps_of rd pre) s in
  P.dirty_sound (deps_of rd pre) s x [];
  mem_bridge x d;
  mem_filt (survives post) d x;
  mem_bridge x (changed_for_op rd pre post o)

(* THEOREM — changed_for_op_names_moved. Every surviving node whose content, child ids or declared
   reads the edit moved, or that the edit created, is in the change set. *)
let changed_for_op_names_moved (rd:tree -> list string) (o:op) (pre post:tree) (x:string)
  : Lemma (requires wf pre /\ apply o pre == Ok post /\ moved rd pre post x)
          (ensures P.mem x (changed_for_op rd pre post o)) =
  find_in_some_iff x post;
  (match find_in x pre, find_in x post with
   | Some m, Some n ->
     if mem x (op_fp o).content_writes then () else kind_kept o pre post x m n
   | _ -> ());
  mem_filt (differs rd o pre post) (ids post) x;
  let l = local rd o pre post in
  let rest = app (removed pre post) (touched_by pre o) in
  mem_app x l rest;
  mem_bridge x (seeds rd o pre post);
  seed_named rd pre post o x

(* THEOREM — changed_for_op_names_readers. Every surviving node that declared, in the PRE-edit
   tree, a read of an id the edit removed is in the change set. *)
let changed_for_op_names_readers (rd:tree -> list string) (o:op) (pre post:tree) (x r:string)
  : Lemma (requires has_id x post /\ has_id r pre /\ not (has_id r post) /\ P.edge (deps_of rd pre) x r)
          (ensures P.mem x (changed_for_op rd pre post o)) =
  let s = seeds rd o pre post in
  let d = P.dirty_from_changed_ids (deps_of rd pre) s in
  mem_filt (gone post) (ids pre) r;
  mem_app r (removed pre post) (touched_by pre o);
  mem_app r (local rd o pre post) (app (removed pre post) (touched_by pre o));
  mem_bridge r s;
  P.dirty_sound (deps_of rd pre) s r [x];
  mem_bridge x d;
  mem_filt (survives post) d x;
  mem_bridge x (changed_for_op rd pre post o)

(* THEOREM — changed_for_op_complete. For an op `apply` accepts on a well-formed tree — `Batch`
   included, at any depth — the change set names every survivor the edit moved (new, or its
   content, child ids or declared reads differ) and every survivor that read an id the edit
   removed. *)
let changed_for_op_complete (rd:tree -> list string) (o:op) (pre post:tree) (x r:string)
  : Lemma (requires wf pre /\ apply o pre == Ok post /\ has_id x post /\
                    (moved rd pre post x \/
                     (has_id r pre /\ not (has_id r post) /\ P.edge (deps_of rd pre) x r)))
          (ensures P.mem x (changed_for_op rd pre post o)) =
  if moved rd pre post x then changed_for_op_names_moved rd o pre post x
  else changed_for_op_names_readers rd o pre post x r
