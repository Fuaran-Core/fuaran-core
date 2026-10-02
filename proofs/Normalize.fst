(*
   Normalize.fst — the model of `Ops.normalize` (Phase 305), the structural peephole that
   collapses adjacent redundant operations without the tree.

   WHAT IS MODELLED. `src/Fuaran.Core.Ops/Ops.fs`, `normalize`, clause for clause, as the single
   left fold with an output stack it has been since Phase 305: `collapse` is the six-row table
   that says what a committed top and an incoming operation net to (`None` keeps both, `Some []`
   drops both, `Some [y]` replaces the top with `y`), `push` is the loop that collapses against
   the top for as long as it collapses, `norm_go` is the fold, and `norm_step` is the one
   dispatch — a `Batch` is normalised inside first and dropped when empty. The script alphabet,
   `apply` and `apply_all` are `TreeOps`'s; the per-operation facts about the tree are
   `Preservation`'s; two trees that agree node for node are the same tree by
   `TreeDiff.tree_ext`, which is why that module is cited rather than restated here.

   WHAT IS PROVED. The doc comment of `Ops.normalize` makes three promises, and the theorems are
   their names:

     normalize_preserves      wf t /\ Ok? (apply_all s t)  ==>  apply_all (normalize s) t == apply_all s t
     normalize_idempotent     normalize (normalize s) == normalize s
     normalize_never_longer   len (normalize s) <= len s

   The first is the defining law `Conformance.normalizeLaws` samples. It is stated for an
   APPLYABLE script at a WELL-FORMED tree, and both hypotheses are the function's own: the doc
   comment says "normalise after validating, not before", because cancelling an insert/remove
   pair can turn a refused script into an accepted one (so the law cannot be an equality of
   outcomes); and three of the six collapses — a move superseded by a move, a reorder by a
   reorder, a rewrite erased by a remove — are sound only because the node they address occurs
   ONCE, which is `wf` (section 1 of `TreeOps.fst`; the assumed row
   `tree-algebra-well-formed-states`). The second and third are unconditional.

   HOW. Soundness is one invariant over the stack — "the stack, read bottom-up, takes the tree
   to the state the processed prefix reached" — carried through `push` by SIX pair lemmas, one
   per row of the table: insert-then-remove is `Preservation.ins_rem_inverse`; rewrite-then-
   rewrite is `Preservation.upd_upd`; insert-then-rewrite is a rewrite pushed through an insert
   (`upd_over_ins`, new); and the three that need uniqueness — move-move, reorder-reorder,
   rewrite-remove — are proved EXTENSIONALLY: both scripts reach a tree whose every node has the
   same children and the same kind (`Preservation.move_view` / `reorder_view` / `rem_view`, and
   `upd_view`, new), so the two trees are one tree. Idempotence is a stability invariant: the
   output has no adjacent collapsible pair and no empty or un-normalised `Batch` (section 4),
   and the fold is the identity on any such script.
*)
module Normalize

open DagFold
open TreeOps
open Preservation

(* Scoped with `#set-options` as the three modules this one opens do, and for the same reason:
   every membership pattern they declare is live at every query here. *)
#set-options "--ext context_pruning"

(* ======================================================================================
   1. `Ops.normalize`, clause for clause.
   ====================================================================================== *)

(* F#: `collapse top x` — what the committed top and the incoming op collapse to, if they do.
   `w.ReplaceChildren n' (w.Children n)` is the payload's content over the inserted node's
   children, which at the witness level is the kind tag over the child list. *)
let collapse (top x:op) : Tot (option (list op)) =
  match top, x with
  | InsertChild _ n, RemoveNode y -> if tid_of n = y then Some [] else None
  | MoveNode t1 _, MoveNode t2 _ -> if t1 = t2 then Some [x] else None
  | ReorderChildren p1 _, ReorderChildren p2 _ -> if p1 = p2 then Some [x] else None
  | UpdateNode a, UpdateNode a' -> if tid_of a = tid_of a' then Some [x] else None
  | InsertChild p n, UpdateNode n' ->
    if tid_of n = tid_of n' then Some [InsertChild p (TNode (tid_of n') (kind_of n') (kids_of n))]
    else None
  | UpdateNode n, RemoveNode y -> if tid_of n = y then Some [x] else None
  | _, _ -> None

(* F#: `push stack x` — the `while` loop, as the recursion it is: bounded by the stack's depth,
   popping at every turn. *)
let rec push (stack:list op) (x:op) : Tot (list op) (decreases stack) =
  match stack with
  | [] -> [x]
  | top :: rest ->
    (match collapse top x with
     | None -> x :: stack
     | Some [] -> rest
     | Some (y :: _) -> push rest y)

(* F#: the `List.fold` and its `Batch` arm — `norm_go` is the fold, `norm_step` the one dispatch,
   and the recursive call on a `Batch`'s members is the same fold from an empty stack, reversed. *)
let rec norm_go (stack:list op) (os:list op) : Tot (list op) (decreases os) =
  match os with
  | [] -> stack
  | o :: r -> norm_go (norm_step stack o) r
and norm_step (stack:list op) (o:op) : Tot (list op) (decreases o) =
  match o with
  | Batch inner ->
    (match rev (norm_go [] inner) with
     | [] -> stack
     | xs -> push stack (Batch xs))
  | _ -> push stack o

(* F#: `normalize w idw ops` — the fold from an empty stack, then `List.rev`. *)
let normalize (os:list op) : Tot (list op) = rev (norm_go [] os)

(* ======================================================================================
   2. The tree facts the six collapses stand on.
   ====================================================================================== *)

let rec drop_id_app (x:string) (l m:list string)
  : Lemma (ensures drop_id x (app l m) == app (drop_id x l) (drop_id x m)) (decreases l)
  = match l with
    | [] -> ()
    | _ :: r -> drop_id_app x r m

let rec drop_id_idem (x:string) (l:list string)
  : Lemma (ensures drop_id x (drop_id x l) == drop_id x l) (decreases l)
  = match l with
    | [] -> ()
    | _ :: r -> drop_id_idem x r

let drop_id_self (x:string) : Lemma (ensures drop_id x [x] == []) = ()

let rec upd_all_app (x k:string) (l m:list tree)
  : Lemma (ensures upd_all x k (app l m) == app (upd_all x k l) (upd_all x k m)) (decreases l)
  = match l with
    | [] -> ()
    | _ :: r -> upd_all_app x k r m

(* A rewrite of an id the tree does not carry, pushed through an insert, rewrites the graft. *)
let rec upd_over_ins (x k p:string) (n:tree) (t:tree)
  : Lemma (requires not (mem x (ids t)))
          (ensures upd x k (ins p n t) == ins p (upd x k n) t) (decreases t)
  = match t with
    | TNode i _ cs ->
      upd_over_ins_all x k p n cs;
      if i = p then upd_all_app x k (ins_all p n cs) [n] else ()
and upd_over_ins_all (x k p:string) (n:tree) (ts:list tree)
  : Lemma (requires not (mem x (ids_all ts)))
          (ensures upd_all x k (ins_all p n ts) == ins_all p (upd x k n) ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      mem_app x (ids t) (ids_all r);
      upd_over_ins x k p n t;
      upd_over_ins_all x k p n r

(* A rewrite of a graft's own id, whose id does not recur below it, rewrites its root alone. *)
let upd_root (x k:string) (n:tree)
  : Lemma (requires tid_of n == x /\ not (mem x (ids_all (kids_of n))))
          (ensures upd x k n == TNode x k (kids_of n))
  = match n with TNode _ _ cs -> upd_absent_all x k cs

(* ======================================================================================
   3. THE SIX COLLAPSES — each row of the table, sound on an applyable pair at a
      well-formed tree. Three are syntactic equalities; three are extensional.
   ====================================================================================== *)

(* Row 1 — insert then remove of what it added nets to nothing: the tree is back where it was. *)
let ins_rem_cancels (p:string) (n:tree) (w u v:tree)
  : Lemma (requires wf w /\ apply (InsertChild p n) w == Ok u /\
                    apply (RemoveNode (tid_of n)) u == Ok v)
          (ensures v == w)
  = first_dup_none_iff n w;
    parent_of_ins p n w;
    ins_rem_inverse p n w

(* Row 4 — two rewrites of one id: the second wins. *)
let upd_upd_sound (a a':tree) (w u v:tree)
  : Lemma (requires apply (UpdateNode a) w == Ok u /\ tid_of a == tid_of a' /\
                    apply (UpdateNode a') u == Ok v)
          (ensures apply (UpdateNode a') w == Ok v)
  = upd_upd (tid_of a) (kind_of a) (kind_of a') w

(* Row 5 — insert then rewrite of the inserted node: one insert, carrying the rewrite's content
   over the graft's children. *)
let ins_upd_sound (p:string) (n n':tree) (w u v:tree)
  : Lemma (requires wf w /\ apply (InsertChild p n) w == Ok u /\ tid_of n == tid_of n' /\
                    apply (UpdateNode n') u == Ok v)
          (ensures apply (InsertChild p (TNode (tid_of n') (kind_of n') (kids_of n))) w == Ok v)
  = let x = tid_of n in
    let k = kind_of n' in
    let n2 = TNode x k (kids_of n) in
    first_dup_none_iff n w;
    assert (ids n2 == ids n);
    first_dup_none_iff n2 w;
    inter_nil_iff (ids n) (ids w);
    upd_over_ins x k p n w;
    upd_root x k n

(* Row 2 — a move superseded by a move of the same target: only the net move survives. The two
   trees agree at every node (`move_view` three times), so they are one tree. Stated in three
   steps so that each query carries only the facts it needs: where the first move lands the
   target, what one node sees after each route, and the assembly through `tree_ext`. *)

#push-options "--fuel 1 --ifuel 1"
(* After a move the target hangs under the new parent, its subtree untouched, the tree still
   well-formed and holding the same ids. *)
let move_lands (x p1:string) (w:tree) (pid:string) (sub:tree)
  : Lemma (requires wf w /\ parent_of x w == Some pid /\ find_in x w == Some sub /\
                    mem p1 (ids w) /\ not (mem p1 (ids sub)))
          (ensures (let u = ins p1 sub (rem_at pid x w) in
                    wf u /\ parent_of x u == Some p1 /\ find_in x u == Some sub /\
                    tid_of u == tid_of w /\
                    (forall (y:string). mem y (ids u) == mem y (ids w))))
  = let removed = rem_at pid x w in
    rem_wf pid x w;
    rem_survives p1 pid x w sub;
    rem_kills pid x w sub;
    disjoint_via_mem (ids sub) (ids removed);
    parent_of_ins p1 sub removed;
    find_in_wf x w sub;
    find_in_id x w sub;
    ins_wf p1 sub removed;
    tid_rem pid x w;
    let aux (y:string) : Lemma (mem y (ids (ins p1 sub removed)) == mem y (ids w)) =
      move_ids x p1 w pid sub y
    in
    FStar.Classical.forall_intro aux

(* One node, two routes: via `p1` and then `p2`, or straight to `p2`. *)
let move_move_node (x p1 p2:string) (w:tree) (pid:string) (sub:tree) (q:string)
  : Lemma (requires wf w /\ parent_of x w == Some pid /\ find_in x w == Some sub /\
                    mem p1 (ids w) /\ not (mem p1 (ids sub)) /\
                    mem p2 (ids w) /\ not (mem p2 (ids sub)) /\ mem q (ids w))
          (ensures (let u = ins p1 sub (rem_at pid x w) in
                    let v = ins p2 sub (rem_at p1 x u) in
                    let v' = ins p2 sub (rem_at pid x w) in
                    kids_at q v' == kids_at q v /\ kind_at q v' == kind_at q v))
  = move_lands x p1 w pid sub;
    let u = ins p1 sub (rem_at pid x w) in
    move_view x p1 w pid sub q;
    move_view x p2 u p1 sub q;
    move_view x p2 w pid sub q;
    drop_id_app x (drop_id x (kids_at q w)) (if q = p1 then [x] else []);
    drop_id_idem x (kids_at q w);
    drop_id_self x;
    app_nil_r (drop_id x (kids_at q w))
#pop-options

#push-options "--fuel 1 --ifuel 1 --z3rlimit 100"
let move_move_sound (x p1 p2:string) (w u v:tree)
  : Lemma (requires wf w /\ apply (MoveNode x p1) w == Ok u /\ apply (MoveNode x p2) u == Ok v)
          (ensures apply (MoveNode x p2) w == Ok v)
  = find_in_some_iff x w;
    parent_exists x w;
    match find_in x w, parent_of x w with
    | Some sub, Some pid ->
      move_accepted x p1 w;
      move_lands x p1 w pid sub;
      move_accepted x p2 u;
      move_lands x p2 u p1 sub;
      move_accepted x p2 w;
      move_lands x p2 w pid sub;
      let v' = ins p2 sub (rem_at pid x w) in
      let aux (q:string)
        : Lemma (mem q (ids v) ==> (kids_at q v' == kids_at q v /\ kind_at q v' == kind_at q v))
        = if mem q (ids v) then move_move_node x p1 p2 w pid sub q else ()
      in
      FStar.Classical.forall_intro aux;
      find_in_self (tid_of v) v;
      find_in_self (tid_of v) v';
      TreeDiff.tree_ext v v' v v'
    | _, _ -> ()
#pop-options

(* Row 3 — a reorder superseded by a reorder of the same parent: a reorder sets the whole
   order, so the last one wins. *)

#push-options "--fuel 1 --ifuel 1"
(* The second order is a permutation of the ORIGINAL children too. *)
let reorder_twice_permutes (p:string) (o1 o2:list string) (w:tree)
  : Lemma (requires wf w /\ mem p (ids w) /\ same_multiset (kids_at p w) o1 /\ same_multiset o1 o2)
          (ensures same_multiset (kids_at p w) o2)
  = kids_at_no_dups p w;
    same_multiset_no_dups (kids_at p w) o1;
    same_multiset_no_dups o1 o2;
    same_multiset_mem (kids_at p w) o1;
    same_multiset_mem o1 o2;
    no_dups_same_multiset (kids_at p w) o2

let reorder_reorder_node (p:string) (o1 o2:list string) (w:tree) (q:string)
  : Lemma (requires wf w /\ mem p (ids w) /\ same_multiset (kids_at p w) o1 /\ same_multiset o1 o2 /\
                    mem q (ids w))
          (ensures (let u = reorder_at p o1 w in
                    kids_at q (reorder_at p o2 w) == kids_at q (reorder_at p o2 u) /\
                    kind_at q (reorder_at p o2 w) == kind_at q (reorder_at p o2 u)))
  = reorder_view p o1 w p;
    reorder_view p o1 w q;
    wf_reorder_ok p o1 w;
    reorder_wf p o1 w;
    let u = reorder_at p o1 w in
    reorder_view p o2 u q;
    reorder_twice_permutes p o1 o2 w;
    reorder_view p o2 w q
#pop-options

#push-options "--fuel 1 --ifuel 1 --z3rlimit 100"
let reorder_reorder_sound (p:string) (o1 o2:list string) (w u v:tree)
  : Lemma (requires wf w /\ apply (ReorderChildren p o1) w == Ok u /\
                    apply (ReorderChildren p o2) u == Ok v)
          (ensures apply (ReorderChildren p o2) w == Ok v)
  = find_in_some_iff p w;
    reorder_view p o1 w p;
    wf_reorder_ok p o1 w;
    reorder_wf p o1 w;
    reorder_view p o2 u p;
    reorder_twice_permutes p o1 o2 w;
    reorder_view p o2 w p;
    wf_reorder_ok p o2 u;
    reorder_wf p o2 u;
    wf_reorder_ok p o2 w;
    reorder_wf p o2 w;
    let v' = reorder_at p o2 w in
    let aux (q:string)
      : Lemma (mem q (ids v) ==> (kids_at q v' == kids_at q v /\ kind_at q v' == kind_at q v))
      = if mem q (ids v) then begin
          reorder_view p o2 u q;
          reorder_view p o1 w q;
          reorder_reorder_node p o1 o2 w q
        end
        else ()
    in
    FStar.Classical.forall_intro aux;
    find_in_self (tid_of v) v;
    find_in_self (tid_of v) v';
    TreeDiff.tree_ext v v' v v'
#pop-options

(* Row 6 — a rewrite of a node about to leave is unobservable: the remove alone reaches the
   same tree. *)

#push-options "--fuel 1 --ifuel 1"
let upd_rem_node (x k:string) (w:tree) (pid:string) (sub:tree) (q:string)
  : Lemma (requires wf w /\ parent_of x w == Some pid /\ find_in x w == Some sub /\
                    mem q (ids (rem_at pid x (upd x k w))))
          (ensures kids_at q (rem_at pid x w) == kids_at q (rem_at pid x (upd x k w)) /\
                   kind_at q (rem_at pid x w) == kind_at q (rem_at pid x (upd x k w)))
  = let u = upd x k w in
    upd_wf x k w;
    ids_upd x k w;
    parent_upd x x k w;
    find_upd x x k w;
    find_in_id x w sub;
    let sub' = upd x k sub in
    ids_upd x k sub;
    ids_rem_sub q pid x u;
    rem_kills pid x u sub';
    rem_view pid x u sub' q;
    rem_view pid x w sub q;
    upd_view x k w q
#pop-options

#push-options "--fuel 1 --ifuel 1 --z3rlimit 100"
let upd_rem_sound (n:tree) (w u v:tree)
  : Lemma (requires wf w /\ apply (UpdateNode n) w == Ok u /\ apply (RemoveNode (tid_of n)) u == Ok v)
          (ensures apply (RemoveNode (tid_of n)) w == Ok v)
  = let x = tid_of n in
    let k = kind_of n in
    find_in_some_iff x w;
    ids_upd x k w;
    parent_upd x x k w;
    parent_exists x w;
    match parent_of x w, find_in x w with
    | Some pid, Some sub ->
      upd_wf x k w;
      rem_wf pid x u;
      rem_wf pid x w;
      let v' = rem_at pid x w in
      let aux (q:string)
        : Lemma (mem q (ids v) ==> (kids_at q v' == kids_at q v /\ kind_at q v' == kind_at q v))
        = if mem q (ids v) then upd_rem_node x k w pid sub q else ()
      in
      FStar.Classical.forall_intro aux;
      tid_rem pid x u;
      tid_rem pid x w;
      find_in_self (tid_of v) v;
      find_in_self (tid_of v) v';
      TreeDiff.tree_ext v v' v v'
    | _, _ -> ()
#pop-options

(* The table, dispatched: a replacing row's replacement reaches the pair's result, and the
   cancelling row leaves the tree where the pair found it. *)
let collapse_sound (top x y:op) (w u v:tree)
  : Lemma (requires wf w /\ apply top w == Ok u /\ apply x u == Ok v /\
                    (match collapse top x with Some (z :: _) -> z == y | _ -> False))
          (ensures apply y w == Ok v)
  = match top, x with
    | MoveNode t1 q1, MoveNode _ p2 -> move_move_sound t1 q1 p2 w u v
    | ReorderChildren p1 o1, ReorderChildren _ o2 -> reorder_reorder_sound p1 o1 o2 w u v
    | UpdateNode a, UpdateNode a' -> upd_upd_sound a a' w u v
    | InsertChild p n, UpdateNode n' -> ins_upd_sound p n n' w u v
    | UpdateNode n, RemoveNode _ -> upd_rem_sound n w u v
    | _, _ -> ()

let collapse_cancels (top x:op) (w u v:tree)
  : Lemma (requires wf w /\ apply top w == Ok u /\ apply x u == Ok v /\ collapse top x == Some [])
          (ensures v == w)
  = match top, x with
    | InsertChild p n, RemoveNode _ -> ins_rem_cancels p n w u v
    | _, _ -> ()

(* ======================================================================================
   4. PRESERVATION — the stack invariant, through `push`, through the fold.
   ====================================================================================== *)

(* The stack read bottom-up — `apply_all (rev stack)` without the reversal. *)
let rec apply_stack (stack:list op) (t:tree) : Tot (outcome tree rejection) (decreases stack) =
  match stack with
  | [] -> Ok t
  | top :: rest -> (match apply_stack rest t with
                    | Ok u -> apply top u
                    | Error e -> Error e)

let rec apply_stack_is_rev (stack:list op) (t:tree)
  : Lemma (ensures apply_all (rev stack) t == apply_stack stack t) (decreases stack)
  = match stack with
    | [] -> ()
    | top :: rest ->
      TreeDiff.apply_all_app (rev rest) [top] t;
      apply_stack_is_rev rest t

let rec apply_stack_preserves_wf (stack:list op) (t:tree)
  : Lemma (requires wf t)
          (ensures (match apply_stack stack t with Ok u -> wf u | Error _ -> True)) (decreases stack)
  = match stack with
    | [] -> ()
    | top :: rest ->
      apply_stack_preserves_wf rest t;
      (match apply_stack rest t with
       | Ok u -> apply_preserves_wf top u
       | Error _ -> ())

(* Pushing an operation the current state accepts keeps the invariant, however many times it
   collapses on the way down. *)
let rec push_sound (stack:list op) (x:op) (t u v:tree)
  : Lemma (requires wf t /\ apply_stack stack t == Ok u /\ apply x u == Ok v)
          (ensures apply_stack (push stack x) t == Ok v) (decreases stack)
  = match stack with
    | [] -> ()
    | top :: rest ->
      apply_stack_preserves_wf rest t;
      (match apply_stack rest t with
       | Ok w ->
         (match collapse top x with
          | None -> ()
          | Some [] -> collapse_cancels top x w u v
          | Some (y :: _) ->
            collapse_sound top x y w u v;
            push_sound rest y t w v)
       | Error _ -> ())

let rec norm_go_sound (stack os:list op) (t u r:tree)
  : Lemma (requires wf t /\ apply_stack stack t == Ok u /\ apply_all os u == Ok r)
          (ensures apply_stack (norm_go stack os) t == Ok r) (decreases os)
  = match os with
    | [] -> ()
    | o :: rest ->
      (match apply o u with
       | Ok u' ->
         norm_step_sound stack o t u u';
         norm_go_sound (norm_step stack o) rest t u' r
       | Error _ -> ())
and norm_step_sound (stack:list op) (o:op) (t u v:tree)
  : Lemma (requires wf t /\ apply_stack stack t == Ok u /\ apply o u == Ok v)
          (ensures apply_stack (norm_step stack o) t == Ok v) (decreases o)
  = match o with
    | Batch inner ->
      apply_stack_preserves_wf stack t;
      norm_go_sound [] inner u u v;
      apply_stack_is_rev (norm_go [] inner) u;
      (match rev (norm_go [] inner) with
       | [] -> ()
       | xs -> push_sound stack (Batch xs) t u v)
    | _ -> push_sound stack o t u v

(* THE FIRST THEOREM — the defining law, as `Conformance.normalizeLaws` states it. *)
let normalize_preserves (os:list op) (t:tree)
  : Lemma (requires wf t /\ Ok? (apply_all os t))
          (ensures apply_all (normalize os) t == apply_all os t)
  = match apply_all os t with
    | Ok r -> norm_go_sound [] os t t r; apply_stack_is_rev (norm_go [] os) t
    | Error _ -> ()

(* ======================================================================================
   5. IDEMPOTENCE — the output is STABLE (no adjacent pair collapses, no `Batch` is empty or
      un-normalised), and the fold is the identity on a stable script.
   ====================================================================================== *)

let rec stable (l:list op) : Tot bool (decreases l) =
  match l with
  | [] -> true
  | a :: r ->
    stable_op a &&
    (match r with
     | [] -> true
     | b :: _ -> (match collapse a b with None -> true | Some _ -> false)) &&
    stable r
and stable_op (o:op) : Tot bool (decreases o) =
  match o with
  | Batch inner -> (match inner with [] -> false | _ -> true) && stable inner
  | _ -> true

(* The same predicate over the STACK, which is the output reversed: the element below is the
   one that precedes in the script. *)
let rec sstable (stack:list op) : Tot bool =
  match stack with
  | [] -> true
  | a :: r ->
    stable_op a &&
    (match r with
     | [] -> true
     | b :: _ -> (match collapse b a with None -> true | Some _ -> false)) &&
    sstable r

let rec last_op (l:list op) : Tot (option op) =
  match l with
  | [] -> None
  | [a] -> Some a
  | _ :: r -> last_op r

let rec stable_snoc (l:list op) (a:op)
  : Lemma (requires stable l /\ stable_op a /\
                    (match last_op l with None -> True | Some b -> None? (collapse b a)))
          (ensures stable (app l [a])) (decreases l)
  = match l with
    | [] -> ()
    | [_] -> ()
    | _ :: r -> stable_snoc r a

let rec last_snoc (l:list op) (a:op)
  : Lemma (ensures last_op (app l [a]) == Some a) (decreases l)
  = match l with
    | [] -> ()
    | _ :: r -> last_snoc r a

let rec stable_rev (stack:list op)
  : Lemma (requires sstable stack) (ensures stable (rev stack)) (decreases stack)
  = match stack with
    | [] -> ()
    | a :: r ->
      stable_rev r;
      (match r with
       | [] -> ()
       | b :: rr -> last_snoc (rev rr) b);
      stable_snoc (rev r) a

(* A replacement the table emits is never a `Batch`. *)
let collapse_leaf (top x:op)
  : Lemma (ensures (match collapse top x with Some (y :: _) -> stable_op y | _ -> True))
  = ()

let rec push_sstable (stack:list op) (x:op)
  : Lemma (requires sstable stack /\ stable_op x) (ensures sstable (push stack x)) (decreases stack)
  = match stack with
    | [] -> ()
    | top :: rest ->
      (match collapse top x with
       | None -> ()
       | Some [] -> ()
       | Some (y :: _) -> collapse_leaf top x; push_sstable rest y)

let rec norm_go_sstable (stack os:list op)
  : Lemma (requires sstable stack) (ensures sstable (norm_go stack os)) (decreases os)
  = match os with
    | [] -> ()
    | o :: r -> norm_step_sstable stack o; norm_go_sstable (norm_step stack o) r
and norm_step_sstable (stack:list op) (o:op)
  : Lemma (requires sstable stack) (ensures sstable (norm_step stack o)) (decreases o)
  = match o with
    | Batch inner ->
      norm_go_sstable [] inner;
      stable_rev (norm_go [] inner);
      (match rev (norm_go [] inner) with
       | [] -> ()
       | xs -> push_sstable stack (Batch xs))
    | _ -> push_sstable stack o

let normalize_stable (os:list op)
  : Lemma (ensures stable (normalize os))
  = norm_go_sstable [] os;
    stable_rev (norm_go [] os)

(* ... and the fold is the identity on a stable script. *)
let rec norm_go_fixed (stack os:list op)
  : Lemma (requires sstable stack /\ stable os /\
                    (match stack, os with
                     | a :: _, o :: _ -> None? (collapse a o)
                     | _, _ -> True))
          (ensures norm_go stack os == app (rev os) stack) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      norm_step_fixed stack o;
      norm_go_fixed (o :: stack) r;
      app_assoc (rev r) [o] stack
and norm_step_fixed (stack:list op) (o:op)
  : Lemma (requires sstable stack /\ stable_op o /\
                    (match stack with a :: _ -> None? (collapse a o) | [] -> True))
          (ensures norm_step stack o == o :: stack) (decreases o)
  = match o with
    | Batch inner ->
      norm_go_fixed [] inner;
      app_nil_r (rev inner);
      rev_rev inner
    | _ -> ()

let normalize_fixed (os:list op)
  : Lemma (requires stable os) (ensures normalize os == os)
  = norm_go_fixed [] os;
    app_nil_r (rev os);
    rev_rev os

(* THE SECOND THEOREM. *)
let normalize_idempotent (os:list op)
  : Lemma (ensures normalize (normalize os) == normalize os)
  = normalize_stable os;
    normalize_fixed (normalize os)

(* ======================================================================================
   6. NEVER LONGER — a push adds at most one, so the fold adds at most the script.
   ====================================================================================== *)

let rec len_app (#a:Type) (l m:list a)
  : Lemma (ensures len (app l m) == len l + len m) (decreases l)
  = match l with
    | [] -> ()
    | _ :: r -> len_app r m

let rec len_rev (#a:Type) (l:list a)
  : Lemma (ensures len (rev l) == len l) (decreases l)
  = match l with
    | [] -> ()
    | x :: r -> len_rev r; len_app (rev r) [x]

let rec push_len (stack:list op) (x:op)
  : Lemma (ensures len (push stack x) <= len stack + 1) (decreases stack)
  = match stack with
    | [] -> ()
    | top :: rest ->
      (match collapse top x with
       | None -> ()
       | Some [] -> ()
       | Some (y :: _) -> push_len rest y)

let rec norm_go_len (stack os:list op)
  : Lemma (ensures len (norm_go stack os) <= len stack + len os) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      (match o with
       | Batch inner ->
         (match rev (norm_go [] inner) with
          | [] -> ()
          | xs -> push_len stack (Batch xs))
       | _ -> push_len stack o);
      norm_go_len (norm_step stack o) r

(* THE THIRD THEOREM. *)
let normalize_never_longer (os:list op)
  : Lemma (ensures len (normalize os) <= len os)
  = norm_go_len [] os;
    len_rev (norm_go [] os)

(* ======================================================================================
   7. WHAT THE LAW DOES NOT SAY, pinned: a cancelled pair can turn a REFUSED script into an
      accepted one, which is why `normalize_preserves` is stated for an applyable script and
      the doc comment says to normalise after validating.
   ====================================================================================== *)

let cx_tree : tree = TNode "root" "doc" [ TNode "a" "sec" [] ]

(* `n` repeats an id the tree holds, so the insert is refused; its remove never runs; the
   normalised script is empty and applies. *)
let cx_script : list op = [ InsertChild "a" (TNode "a" "para" []); RemoveNode "a" ]

let cancellation_can_admit_a_refused_script ()
  : Lemma (ensures Error? (apply_all cx_script cx_tree) /\
                   normalize cx_script == [] /\
                   apply_all (normalize cx_script) cx_tree == Ok cx_tree)
  = assert_norm (Error? (apply_all cx_script cx_tree));
    assert_norm (normalize cx_script == []);
    assert_norm (apply_all (normalize cx_script) cx_tree == Ok cx_tree)

(* ======================================================================================
   TWINS (Phase 309) — the extractor premise, sampled at this model.

   The leg's extraction diff makes "the oracle is the model" a checked claim about TEXT. Nothing
   in it says the F# the extractor emits COMPUTES what this model means: a mis-extraction that
   compiles would pass every other step. Each fixture below applies this model's own functions to
   a concrete input and compares the result with the value the model means there, and the
   assertion at the end is discharged by NORMALISATION — F*'s normaliser evaluates every closure
   to `true` under the model's own semantics. The list is extracted with the rest of the model,
   and the `Proofs.Oracle` family runs the extracted closures against the extracted oracle
   ("twin evaluation"): a closure that comes back `false` there is the F# backend disagreeing with
   the normaliser on that input. Sampled, never proved: the discharge holds on these inputs, which
   is where the `tested` rows already live. The kit's TWIN step (`kit/check-proof-leg.ps1`, step
   2c) refuses an extracted model that declares no twins.
   ====================================================================================== *)

noeq type twin = { tname : string; tholds : unit -> bool }

let rec twins_hold (l:list twin) : Tot bool =
  match l with
  | [] -> true
  | t :: r -> t.tholds () && twins_hold r

let twins : list twin = [
  { tname = "normalize-cancels-an-insert-by-its-remove";
    tholds = (fun () ->
      normalize [ InsertChild "root" (TNode "n" "para" []); RemoveNode "n" ] = []) };
  { tname = "normalize-keeps-the-last-reorder-on-a-parent";
    tholds = (fun () ->
      normalize [ ReorderChildren "p" [ "b"; "a" ]; ReorderChildren "p" [ "a"; "b" ] ]
      = [ ReorderChildren "p" [ "a"; "b" ] ]) };
  { tname = "normalize-folds-a-rewrite-into-the-insert-it-follows";
    tholds = (fun () ->
      normalize [ InsertChild "root" (TNode "n" "para" [ TNode "c" "para" [] ]); UpdateNode (TNode "n" "aside" []) ]
      = [ InsertChild "root" (TNode "n" "aside" [ TNode "c" "para" [] ]) ]) };
  { tname = "normalize-catches-the-adjacency-a-cancellation-exposes";
    tholds = (fun () ->
      normalize [ MoveNode "x" "a"; InsertChild "a" (TNode "n" "para" []); RemoveNode "n"; MoveNode "x" "b" ]
      = [ MoveNode "x" "b" ]) };
  { tname = "normalize-drops-an-empty-batch-and-normalises-inside-one";
    tholds = (fun () ->
      normalize [ Batch []; Batch [ UpdateNode (TNode "a" "k1" []); UpdateNode (TNode "a" "k2" []) ] ]
      = [ Batch [ UpdateNode (TNode "a" "k2" []) ] ]) } ]

let _ = assert_norm (twins_hold twins == true)
