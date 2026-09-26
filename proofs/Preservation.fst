(*
   Preservation — the apply engine's own theorem: a legal operation applied to a valid tree yields
   a valid tree, and the three clauses around that sentence which no test or law states
   (fuaran-core Phase 138).

   WHAT IS MODELLED. Nothing new. `TreeOps.fst` (Phase 133, lifted by Phase 138 to the validator
   Phase 137 shipped) is imported rather than remodelled, and this module adds `Ops.canApply` and
   `Ops.invert` — the two surfaces of the engine that model did not reach — clause for clause.

   WHAT IS PROVED, over the six skeleton operations (Phase 250 added `UpdateNode`, an in-place
   rewrite of a node's content that keeps its children) and any tree:

     - `apply_total` — `Ops.apply` reaches exactly one outcome on every input, and WHICH rejection
       it can raise is characterised per clause. That characterisation is the content: it proves
       `NotAContainer` and `Rejected` unreachable from `apply`, which until now was a sentence in
       a README.
     - `reject_identity` — a refused step leaves the caller holding the input tree. For one
       operation that is a fact about persistent values; for a `Batch` it is the all-or-nothing
       discipline, and there the intermediate trees a prefix computed really are discarded however
       deep the failure lies.
     - `apply_preserves_wf` — an accepted operation preserves id uniqueness. Phase 133 proved the
       insert clause of this CONDITIONALLY (`TreeOps.ins_wf`) and refuted the unconditional form,
       because the validator of the day admitted a subtree that broke it. Phase 137 fixed the
       validator; this is the unconditional statement, for every operation, finally true.
     - `canapply_preserves` — every operation the dry run accepts is one `apply` accepts, and
       conversely. The corollary worth naming is that two defensive `UnknownNode` fallbacks in the
       shipped code are unreachable: `Tree.parentOf` cannot fail on a non-root node the tree holds.
     - `invert_applicable` — the inverse of an accepted operation is itself accepted at the result
       and restores the input, so `apply (invert o pre) (apply o pre) = pre` stops being a sampled
       law and becomes a theorem, for the leaf alphabet.

   AND, SINCE PHASE 167, SECTION 11: a per-node view of a tree (`kids_at` / `kind_at`) and what
   `ins` and `rem_at` do to it, one lemma per operation per node. Nothing there mentions the diff;
   they are here because `TreeDiff.fst`'s reconstruction induction needs facts about INTERMEDIATE
   trees, and inductions over `ins` and `rem_at` are this module's cost class rather than that
   one's. `TreeDiff.fst` opens this module for them.

   WHAT IS NOT CLAIMED. The witness laws themselves: `ReplaceChildren` is taken as an abstract
   function satisfying them, exactly as `DagFold.fst` takes the diamond, and a domain's own
   `ReplaceChildren` remains its promise (sampled by `Conformance.witnessLaws`). Vocabulary and
   schema validity, which live at the wire boundary and in domain rule families. `Ops.normalize`
   and `Diff`. `applyContained`'s container capability — `canHold` is `fun _ -> true` under
   `apply`, which is why `NotAContainer` is unreachable above rather than merely unmet.

   HOW TO READ IT. Every definition names its F# counterpart, as in `DagFold.fst` and
   `TreeOps.fst`. The tree, the operations, the rejection envelope and the whole membership algebra
   are `TreeOps`'s, opened here rather than restated — this module extracts beside it into the same
   oracle assembly.

   Apache-2.0, like everything beside it.
*)
module Preservation

open DagFold
open TreeOps

(* `TreeOps.fst`'s reason applies here unchanged and more so: this module's proofs run on that
   module's ninety-odd lemmas, every one of whose patterns would otherwise be live at every query.
   Scoped with `#set-options` rather than added to `check.ps1`'s flags, deliberately — pruning
   changes which facts a query can see, and a module that has not been checked under it must not
   be switched to it as a side effect of another module's cost. *)
#set-options "--ext context_pruning"

(* ======================================================================================
   1. Which rejection each clause of `Ops.apply` can raise.

      `Rejection<'Id>` has seven cases and `apply` reaches five of them. `NotAContainer` is
      `applyContained`'s alone — `apply` passes `canHold = fun _ -> true`, so the clause that
      raises it is dead there — and `Rejected` is the domain-side extension point Core never
      raises itself. Both are carried in the envelope so the vocabulary is complete.

      `raisable` writes that per-clause down as a predicate, and `apply_total` below proves it.
      Stating it this way rather than as five separate iffs is what makes it a CHARACTERISATION:
      the classes an operation cannot raise are exactly the ones the predicate refuses, so the
      README's "these two are unreachable" is a consequence rather than an assertion.
   ====================================================================================== *)

let rec raisable (o:op) (e:rejection) : Tot bool (decreases o) =
  match o, e with
  | InsertChild _ _, DuplicateId _        -> true
  | InsertChild _ _, UnknownNode _ _      -> true
  | RemoveNode _, CannotRemoveRoot        -> true
  | RemoveNode _, UnknownNode _ _         -> true
  | ReorderChildren _ _, UnknownNode _ _  -> true
  | ReorderChildren _ _, ReorderMismatch _ _ _ -> true
  | MoveNode _ _, CannotRemoveRoot        -> true
  | MoveNode _ _, UnknownNode _ _         -> true
  | MoveNode _ _, WouldNestUnderSelf _    -> true
  | UpdateNode _, UnknownNode _ _         -> true
  | Batch os, _                           -> raisable_all os e
  | _, _                                  -> false
and raisable_all (os:list op) (e:rejection) : Tot bool (decreases os) =
  match os with
  | [] -> false
  | o :: r -> raisable o e || raisable_all r e

(* THE FIRST LEMMA. F#: `Ops.apply` returns `Result<'Node, Rejection<'Id>>` and never throws —
   which in this model is the type of `apply`, discharged by construction — and the rejection it
   returns is one its own clause can produce. A `Batch` inherits the union of its members', and
   nothing else is reachable: in particular no operation raises `NotAContainer` or `Rejected`. *)
let rec apply_total (o:op) (t:tree)
  : Lemma (ensures (match apply o t with
                    | Ok _ -> True
                    | Error e -> raisable o e)) (decreases o)
  = match o with
    | Batch os -> apply_all_total os t
    | _ -> ()
and apply_all_total (os:list op) (t:tree)
  : Lemma (ensures (match apply_all os t with
                    | Ok _ -> True
                    | Error e -> raisable_all os e)) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      apply_total o t;
      (match apply o t with
       | Ok t' -> apply_all_total r t'
       | Error _ -> ())

(* The two unreachable classes, read off the characterisation. Kept as their own statement because
   it is the half a reader checks the envelope against. *)
let rec raisable_shape (o:op)
  : Lemma (ensures forall (e:rejection). raisable o e ==>
                     (match e with NotAContainer _ _ -> False | Rejected _ _ -> False | _ -> True))
          (decreases o)
  = match o with
    | Batch os -> raisable_all_shape os
    | _ -> ()
and raisable_all_shape (os:list op)
  : Lemma (ensures forall (e:rejection). raisable_all os e ==>
                     (match e with NotAContainer _ _ -> False | Rejected _ _ -> False | _ -> True))
          (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      raisable_shape o;
      raisable_all_shape r

let apply_never_container_or_domain (o:op) (t:tree)
  : Lemma (ensures (match apply o t with
                    | Error (NotAContainer _ _) -> False
                    | Error (Rejected _ _) -> False
                    | _ -> True))
  = apply_total o t;
    raisable_shape o

(* ======================================================================================
   2. A refused step leaves the input tree.

      F#: `apply` takes a `'Node` and returns a new one; the `Error` arm carries no tree, so the
      caller still holds what it passed in. On one operation that is a type-level fact of
      persistent values, and this lemma's only job is to NAME it — no test and no conformance law
      does.

      The `Batch` arm is not a type-level fact. `applyWith`'s `go` threads the tree through the
      script, so by the time a step fails, several intermediate trees have been built; the
      all-or-nothing discipline is the claim that none of them escapes. That is what
      `reject_identity`'s quantified half states, at an arbitrary failure position in an arbitrary
      script.
   ====================================================================================== *)

(* The state a caller holds after asking for `o` — the result when it was accepted, the input when
   it was not. F#: `match Ops.apply w idw op root with Ok t -> t | Error _ -> root`, which is what
   every caller in the estate writes. *)
let state_after (o:op) (t:tree) : Tot tree =
  match apply o t with
  | Ok t' -> t'
  | Error _ -> t

let rec reject_identity_batch (pre:list op) (o:op) (post:list op) (t m:tree)
  : Lemma (requires apply (Batch pre) t == Ok m /\ Error? (apply o m))
          (ensures apply (Batch (app pre (o :: post))) t == apply o m /\
                   state_after (Batch (app pre (o :: post))) t == t)
          (decreases pre)
  = match pre with
    | [] -> ()
    | q :: rest ->
      (match apply q t with
       | Ok t1 -> reject_identity_batch rest o post t1 m
       | Error _ -> ())

(* THE SECOND LEMMA. Both halves: the operation's own, and the script's. *)
let reject_identity (o:op) (t:tree)
  : Lemma (ensures (Error? (apply o t) ==> state_after o t == t) /\
                   (forall (pre:list op) (post:list op) (m:tree).
                      (apply (Batch pre) t == Ok m /\ Error? (apply o m)) ==>
                      (apply (Batch (app pre (o :: post))) t == apply o m /\
                       state_after (Batch (app pre (o :: post))) t == t)))
  = let aux (pre post:list op) (m:tree)
      : Lemma ((apply (Batch pre) t == Ok m /\ Error? (apply o m)) ==>
               (apply (Batch (app pre (o :: post))) t == apply o m /\
                state_after (Batch (app pre (o :: post))) t == t))
      = if apply (Batch pre) t = Ok m && Error? (apply o m) then
          reject_identity_batch pre o post t m
        else ()
    in
    FStar.Classical.forall_intro_3 aux

(* ======================================================================================
   3. What a REMOVE does to the id set — the machinery sections 4 and 5 rest on.

      Phase 133 needed the insert half of this and proved it (`TreeOps.ins_wf` and its converse);
      the remove half was never needed, because nothing then depended on a remove preserving the
      invariant. A `MoveNode` is a remove followed by an insert, and the insert's precondition is
      that the moved subtree's ids are ABSENT from the tree it lands in — which is a fact about
      what the remove took away. So the exclusion has to be proved, not assumed.
   ====================================================================================== *)

(* ---- `drop_kid` keeps a sublist, and drops exactly the children carrying the id ---- *)

let rec drop_kid_mem (x:string) (ts:list tree) (d:tree)
  : Lemma (requires mem d (drop_kid x ts)) (ensures mem d ts /\ tid_of d <> x) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> if tid_of t = x then drop_kid_mem x r d
                else (if t = d then () else drop_kid_mem x r d)

let rec ids_drop_kid_sub (y:string) (x:string) (ts:list tree)
  : Lemma (ensures mem y (ids_all (drop_kid x ts)) ==> mem y (ids_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> ids_drop_kid_sub y x r

let ids_drop_kid_sub_fa (x:string) (ts:list tree)
  : Lemma (ensures forall (y:string). mem y (ids_all (drop_kid x ts)) ==> mem y (ids_all ts))
  = let aux (y:string) : Lemma (mem y (ids_all (drop_kid x ts)) ==> mem y (ids_all ts))
      = ids_drop_kid_sub y x ts
    in
    FStar.Classical.forall_intro aux

let rec drop_kid_wf_all (x:string) (ts:list tree)
  : Lemma (requires wf_all ts) (ensures wf_all (drop_kid x ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      drop_kid_wf_all x r;
      ids_drop_kid_sub_fa x r;
      inter_nil_iff (ids t) (ids_all r);
      inter_nil_iff (ids t) (ids_all (drop_kid x r))

(* ---- a remove at a parent the tree does not hold is the identity (F#: `Tree.updateNode`
   returns `None`, and the caller keeps the tree) ---- *)

let rec rem_absent (pid x:string) (t:tree)
  : Lemma (requires not (mem pid (ids t))) (ensures rem_at pid x t == t) (decreases t)
  = match t with TNode _ _ cs -> rem_all_absent pid x cs
and rem_all_absent (pid x:string) (ts:list tree)
  : Lemma (requires not (mem pid (ids_all ts))) (ensures rem_all pid x ts == ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> rem_absent pid x t; rem_all_absent pid x r

(* ---- a remove invents no id ---- *)

let rec ids_rem_sub (y:string) (pid x:string) (t:tree)
  : Lemma (ensures mem y (ids (rem_at pid x t)) ==> mem y (ids t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      ids_rem_all_sub y pid x cs;
      ids_drop_kid_sub y x (rem_all pid x cs)
and ids_rem_all_sub (y:string) (pid x:string) (ts:list tree)
  : Lemma (ensures mem y (ids_all (rem_all pid x ts)) ==> mem y (ids_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> ids_rem_sub y pid x t; ids_rem_all_sub y pid x r

let ids_rem_sub_fa (pid x:string) (t:tree)
  : Lemma (ensures forall (y:string). mem y (ids (rem_at pid x t)) ==> mem y (ids t))
  = let aux (y:string) : Lemma (mem y (ids (rem_at pid x t)) ==> mem y (ids t))
      = ids_rem_sub y pid x t
    in
    FStar.Classical.forall_intro aux

let ids_rem_all_sub_fa (pid x:string) (ts:list tree)
  : Lemma (ensures forall (y:string). mem y (ids_all (rem_all pid x ts)) ==> mem y (ids_all ts))
  = let aux (y:string) : Lemma (mem y (ids_all (rem_all pid x ts)) ==> mem y (ids_all ts))
      = ids_rem_all_sub y pid x ts
    in
    FStar.Classical.forall_intro aux

(* ---- and therefore preserves well-formedness, outright: it only ever takes ids away ---- *)

let rec rem_wf (pid x:string) (t:tree)
  : Lemma (requires wf t) (ensures wf (rem_at pid x t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      rem_wf_all pid x cs;
      ids_rem_all_sub_fa pid x cs;
      drop_kid_wf_all x (rem_all pid x cs);
      ids_drop_kid_sub_fa x (rem_all pid x cs)
and rem_wf_all (pid x:string) (ts:list tree)
  : Lemma (requires wf_all ts) (ensures wf_all (rem_all pid x ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      rem_wf pid x t;
      rem_wf_all pid x r;
      ids_rem_sub_fa pid x t;
      ids_rem_all_sub_fa pid x r;
      inter_nil_iff (ids t) (ids_all r);
      inter_nil_iff (ids (rem_at pid x t)) (ids_all (rem_all pid x r))

(* ---- what `Tree.tryFind` hands back is a well-formed subtree of what it was asked about ---- *)

let rec find_in_wf (x:string) (t:tree) (sub:tree)
  : Lemma (requires wf t /\ find_in x t == Some sub)
          (ensures wf sub /\ (forall (y:string). mem y (ids sub) ==> mem y (ids t)))
          (decreases t)
  = match t with
    | TNode i _ cs -> if i = x then () else find_all_wf x cs sub
and find_all_wf (x:string) (ts:list tree) (sub:tree)
  : Lemma (requires wf_all ts /\ find_all x ts == Some sub)
          (ensures wf sub /\ (forall (y:string). mem y (ids sub) ==> mem y (ids_all ts)))
          (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> (match find_in x t with
                 | Some _ -> find_in_wf x t sub
                 | None -> find_all_wf x r sub)

(* ---- `Tree.parentOf`: absent, present, and where it looks ---- *)

let rec has_kid_mem (x:string) (ts:list tree)
  : Lemma (requires has_kid x ts) (ensures mem x (kid_ids ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> if tid_of t = x then () else has_kid_mem x r

let rec parent_absent (x:string) (t:tree)
  : Lemma (requires not (mem x (ids t))) (ensures parent_of x t == None) (decreases t)
  = match t with
    | TNode _ _ cs ->
      (if has_kid x cs then (has_kid_mem x cs; kid_ids_sub x cs) else ());
      parent_all_absent x cs
and parent_all_absent (x:string) (ts:list tree)
  : Lemma (requires not (mem x (ids_all ts))) (ensures parent_all x ts == None) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> parent_absent x t; parent_all_absent x r

(* THE FALLBACK `Ops.apply` CARRIES AND CANNOT REACH: `Tree.parentOf` returns `Some` for every
   non-root id the tree holds, so the `| None -> Error(UnknownNode …)` arm after the `parentOf`
   lookup in the `RemoveNode` and `MoveNode` clauses is dead code under `validateRemove`. *)
let rec parent_exists (x:string) (t:tree)
  : Lemma (requires mem x (ids t) /\ tid_of t <> x) (ensures Some? (parent_of x t)) (decreases t)
  = match t with
    | TNode i _ cs -> if has_kid x cs then () else parent_all_exists x cs
and parent_all_exists (x:string) (ts:list tree)
  : Lemma (requires mem x (ids_all ts) /\ not (has_kid x ts))
          (ensures Some? (parent_all x ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      if mem x (ids t) then parent_exists x t
      else (parent_absent x t; parent_all_exists x r)

#push-options "--z3rlimit 60"
let rec parent_all_mem (x:string) (ts:list tree) (p:string)
  : Lemma (requires parent_all x ts == Some p)
          (ensures mem x (ids_all ts) /\ mem p (ids_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> (match parent_of x t with
                 | Some q -> parent_of_mem x t q
                 | None -> parent_all_mem x r p)
and parent_of_mem (x:string) (t:tree) (p:string)
  : Lemma (requires parent_of x t == Some p)
          (ensures mem x (ids t) /\ mem p (ids t)) (decreases t)
  = match t with
    | TNode i _ cs -> if has_kid x cs then (has_kid_mem x cs; kid_ids_sub x cs)
                      else parent_all_mem x cs p
#pop-options

(* ---- the exclusion: a remove takes the whole subtree's ids with it ----

   This is the lemma the move clause turns on. Proved by the same descent `rem_at` performs,
   with well-formedness supplying at each level the fact the argument needs: the parent id does
   not occur below its own node, and sibling subtrees share no id, so the child that holds the
   target is the only one and the children that remain cannot carry any of its ids. *)

let rec kid_with (x:string) (ts:list tree) : Tot (option tree) (decreases ts) =
  match ts with
  | [] -> None
  | t :: r -> if tid_of t = x then Some t else kid_with x r

let rec kid_with_some (x:string) (ts:list tree)
  : Lemma (ensures Some? (kid_with x ts) == has_kid x ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> if tid_of t = x then () else kid_with_some x r

let rec kid_with_mem (x:string) (ts:list tree) (d:tree)
  : Lemma (requires kid_with x ts == Some d) (ensures mem d ts /\ tid_of d == x) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> if tid_of t = x then () else kid_with_mem x r d

(* `Tree.tryFind` stops at the node itself when the node IS the answer. *)
let find_in_self (x:string) (c:tree)
  : Lemma (requires tid_of c == x) (ensures find_in x c == Some c)
  = match c with TNode _ _ _ -> ()

let rec rem_kills (pid x:string) (t:tree) (sub:tree)
  : Lemma (requires wf t /\ parent_of x t == Some pid /\ find_in x t == Some sub)
          (ensures forall (y:string). mem y (ids sub) ==> not (mem y (ids (rem_at pid x t))))
          (decreases t)
  = match t with
    | TNode i k cs ->
      if has_kid x cs then begin
        (* `pid` is this node, and `Tree.updateNode` cannot descend into a child looking for it:
           well-formedness says the id does not occur below. So the whole edit is one `drop_kid`
           over this node's children. *)
        has_kid_mem x cs;
        kid_ids_sub x cs;
        rem_all_absent i x cs;
        find_all_id x cs sub;
        find_all_locate x cs;
        locate_mem x cs;
        wf_all_holder_unique x cs;
        kid_with_some x cs;
        (match kid_with x cs with
         | Some d ->
           kid_with_mem x cs d;
           tid_in_ids d;
           (match locate x cs with
            | Some c ->
              (* the holder is unique, so it is `d`, whose own id IS `x` — hence the lookup stopped
                 at it and `sub` is that child of this node *)
              find_in_self x c;
              let l = drop_kid x cs in
              let aux (e:tree) : Lemma (mem e l ==> (mem e cs /\ ~(e == sub))) =
                if mem e l then drop_kid_mem x cs e else ()
              in
              FStar.Classical.forall_intro aux;
              no_shared_ids sub cs l;
              (* and this node's OWN id is not one of the subtree's: well-formedness says an id
                 does not occur below its own node *)
              let aux2 (y:string) : Lemma (mem y (ids sub) ==> y <> i) =
                if mem y (ids sub) then mem_ids_all_intro y sub cs else ()
              in
              FStar.Classical.forall_intro aux2
            | None -> ())
         | None -> ())
      end
      else begin
        (* `pid` is somewhere below. It cannot be this node — well-formedness again — so the edit
           is the recursive one over the children, and `rem_all_kills` carries it. *)
        parent_all_mem x cs pid;
        find_all_wf x cs sub;
        rem_all_kills pid x cs sub
      end

and rem_all_kills (pid x:string) (ts:list tree) (sub:tree)
  : Lemma (requires wf_all ts /\ not (has_kid x ts) /\
                    parent_all x ts == Some pid /\ find_all x ts == Some sub)
          (ensures forall (y:string). mem y (ids sub) ==> not (mem y (ids_all (rem_all pid x ts))))
          (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      find_in_some_iff x t;
      if mem x (ids t) then begin
        parent_exists x t;
        rem_kills pid x t sub;
        find_in_wf x t sub;
        ids_rem_all_sub_fa pid x r;
        inter_nil_iff (ids t) (ids_all r)
      end
      else begin
        parent_absent x t;
        rem_all_kills pid x r sub;
        find_all_wf x r sub;
        ids_rem_sub_fa pid x t;
        inter_nil_iff (ids t) (ids_all r)
      end

(* ======================================================================================
   4. THE THIRD LEMMA — an accepted operation preserves id uniqueness.

      Phase 133 could not state this. Its `TreeOps.insert_breaks_wf` REFUTED the unconditional
      form against the validator of the day, and proved the conditional `ins_wf` beside it as the
      specification a fix would have to meet. Phase 137 landed that fix, Phase 138's section 5b of
      `TreeOps.fst` lifted the model to it, and `TreeOps.first_dup_none_iff` proves that what the
      shipped scan decides is EXACTLY `ins_wf`'s hypothesis — neither weaker (the invariant would
      still break) nor stronger (safe inserts would be refused). So the unconditional statement is
      available for the first time, and the two other structural clauses fall out of section 3.
   ====================================================================================== *)

(* ---- what an in-place rewrite does to a tree (Phase 250) ----

   `TreeOps.upd` rewrites the kind at every node carrying the id and keeps every child list, so the
   id set is untouched and so is well-formedness. On a well-formed tree the id is carried once, so
   the rewrite is exactly one node's, which is what the inverse and the container clause need. *)

let rec ids_upd (x k:string) (t:tree)
  : Lemma (ensures ids (upd x k t) == ids t) (decreases t)
  = match t with
    | TNode _ _ cs -> ids_upd_all x k cs
and ids_upd_all (x k:string) (ts:list tree)
  : Lemma (ensures ids_all (upd_all x k ts) == ids_all ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> ids_upd x k t; ids_upd_all x k r

let rec upd_wf (x k:string) (t:tree)
  : Lemma (ensures wf (upd x k t) == wf t) (decreases t)
  = match t with
    | TNode _ _ cs -> ids_upd_all x k cs; upd_wf_all x k cs
and upd_wf_all (x k:string) (ts:list tree)
  : Lemma (ensures wf_all (upd_all x k ts) == wf_all ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> upd_wf x k t; upd_wf_all x k r; ids_upd x k t; ids_upd_all x k r

(* A rewrite of an id the tree does not carry is the identity. *)
let rec upd_absent (x k:string) (t:tree)
  : Lemma (requires not (mem x (ids t))) (ensures upd x k t == t) (decreases t)
  = match t with
    | TNode _ _ cs -> upd_absent_all x k cs
and upd_absent_all (x k:string) (ts:list tree)
  : Lemma (requires not (mem x (ids_all ts))) (ensures upd_all x k ts == ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      mem_app x (ids t) (ids_all r);
      upd_absent x k t;
      upd_absent_all x k r

(* Two rewrites of one id: the second wins. *)
let rec upd_upd (x k1 k2:string) (t:tree)
  : Lemma (ensures upd x k2 (upd x k1 t) == upd x k2 t) (decreases t)
  = match t with
    | TNode _ _ cs -> upd_upd_all x k1 k2 cs
and upd_upd_all (x k1 k2:string) (ts:list tree)
  : Lemma (ensures upd_all x k2 (upd_all x k1 ts) == upd_all x k2 ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> upd_upd x k1 k2 t; upd_upd_all x k1 k2 r

(* On a well-formed tree, rewriting the node `find_in` answers with to the kind it already has is
   the identity — the half of the round trip the inverse relies on. *)
let rec upd_found_self (x:string) (t:tree) (ex:tree)
  : Lemma (requires wf t /\ find_in x t == Some ex) (ensures upd x (kind_of ex) t == t) (decreases t)
  = match t with
    | TNode i _ cs ->
      if i = x then upd_absent_all x (kind_of ex) cs
      else upd_found_self_all x cs ex
and upd_found_self_all (x:string) (ts:list tree) (ex:tree)
  : Lemma (requires wf_all ts /\ find_all x ts == Some ex)
          (ensures upd_all x (kind_of ex) ts == ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      find_in_some_iff x t;
      (match find_in x t with
       | Some _ ->
         upd_found_self x t ex;
         inter_nil_iff (ids t) (ids_all r);
         upd_absent_all x (kind_of ex) r
       | None -> upd_absent x (kind_of ex) t; upd_found_self_all x r ex)

let rec apply_preserves_wf (o:op) (t:tree)
  : Lemma (requires wf t)
          (ensures (match apply o t with Ok t' -> wf t' | Error _ -> True)) (decreases o)
  = match o with

    | InsertChild p n ->
      first_dup_none_iff n t;
      wf_iff_no_dups n;
      if None? (first_dup n t) && has_id p t then ins_wf p n t else ()

    | RemoveNode x ->
      (match parent_of x t with
       | Some pid -> rem_wf pid x t
       | None -> ())

    | ReorderChildren p order ->
      (match find_in p t with
       | None -> ()
       | Some n ->
         if same_multiset (kid_ids (kids_of n)) order then
           (wf_reorder_ok p order t; reorder_wf p order t)
         else ())

    | MoveNode x np ->
      if tid_of t <> x && has_id x t && has_id np t then
        (match find_in x t with
         | None -> ()
         | Some sub ->
           if not (mem np (ids sub)) then
             (match parent_of x t with
              | None -> ()
              | Some pid ->
                let removed = rem_at pid x t in
                rem_wf pid x t;
                find_in_wf x t sub;
                rem_kills pid x t sub;
                inter_nil_iff (ids sub) (ids removed);
                if has_id np removed then ins_wf np sub removed else ())
           else ())
      else ()

    | Batch os -> apply_all_preserves_wf os t

    | UpdateNode n -> upd_wf (tid_of n) (kind_of n) t

and apply_all_preserves_wf (os:list op) (t:tree)
  : Lemma (requires wf t)
          (ensures (match apply_all os t with Ok t' -> wf t' | Error _ -> True)) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      apply_preserves_wf o t;
      (match apply o t with
       | Ok t' -> apply_all_preserves_wf r t'
       | Error _ -> ())

(* ======================================================================================
   5. THE FOURTH LEMMA — the dry run and the mutating call agree.

      F#: `Ops.canApply` is `canApplyWith (fun _ -> true)`. Three clauses run the validator alone
      and build no tree; `MoveNode` and `Batch` are order-dependent — a later step sees an earlier
      step's tree — so they simulate through `apply` and discard the result. `Conformance.fs`
      samples the agreement as a law; here it is a theorem.

      The corollary is the one worth having. `applyWith`'s `RemoveNode` and `MoveNode` clauses each
      carry an `| None -> Error(UnknownNode …)` arm after the `Tree.parentOf` lookup, which
      `validateRemove` has already made unreachable — `parent_exists` says so. So the two
      surfaces cannot disagree by one of them taking a fallback the other does not.
   ====================================================================================== *)

let can_apply (o:op) (t:tree) : Tot (outcome unit rejection) =
  match o with

  (* F#: `validateInsert` — the same scan `apply` runs, and the same envelope. *)
  | InsertChild p n ->
    (match first_dup n t with
     | Some d -> Error (DuplicateId d)
     | None ->
       if not (has_id p t) then Error (UnknownNode p (ids t))
       else Ok ())

  (* F#: `validateRemove`. Note what it does NOT check: that the target has a parent. *)
  | RemoveNode x ->
    if tid_of t = x then Error CannotRemoveRoot
    else if not (has_id x t) then Error (UnknownNode x (ids t))
    else Ok ()

  (* F#: `validateReorder`. *)
  | ReorderChildren p order ->
    (match find_in p t with
     | None -> Error (UnknownNode p (ids t))
     | Some n ->
       let current = kid_ids (kids_of n) in
       if not (same_multiset current order) then Error (ReorderMismatch p current order)
       else Ok ())

  (* F#: `validateUpdate` (Phase 250) — the target must be in the tree. *)
  | UpdateNode n ->
    (match find_in (tid_of n) t with
     | None -> Error (UnknownNode (tid_of n) (ids t))
     | Some _ -> Ok ())

  (* F#: `MoveNode` and `Batch` simulate through `applyWith` and `Result.map ignore` the result. *)
  | MoveNode _ _
  | Batch _ -> (match apply o t with Ok _ -> Ok () | Error e -> Error e)

let canapply_preserves (o:op) (t:tree)
  : Lemma (requires wf t)
          (ensures (Ok? (can_apply o t) <==> Ok? (apply o t)) /\
                   (Ok? (can_apply o t) ==>
                    (match apply o t with Ok t' -> wf t' | Error _ -> False)))
  = apply_preserves_wf o t;
    match o with
    | RemoveNode x -> if tid_of t <> x && has_id x t then parent_exists x t else ()
    | _ -> ()

(* ======================================================================================
   6. `Ops.invert`, modelled — and the machinery the round trip needs.

      F#: `Ops.invert w idw op pre` derives the operation that undoes `op` AGAINST THE STATE IT
      SAW: insert↔remove, remove↔insert (capturing the removed subtree, its parent and the order
      it belonged to), move↔move-back, reorder↔reorder. It is total — a non-applyable operation
      has no inverse and its `Rejection` is returned — and it gates on `canApply`, which is why
      section 5 comes first.

      The defining law, `apply (invert op pre) (apply op pre) = pre`, is sampled by
      `Conformance.opAlgebra`. Below it is a theorem.
   ====================================================================================== *)

(* F#: `orderIn parentNode` — `parentNode |> w.Children |> List.map w.Id`. *)
let order_in (pnode:tree) : Tot (list string) = kid_ids (kids_of pnode)

(* F#: `List.tryLast order`. *)
let rec last_of (l:list string) : Tot (option string) (decreases l) =
  match l with
  | [] -> None
  | [x] -> Some x
  | _ :: r -> last_of r

(* F#: `restoring` — put the node back where it was. A single operation when it was already last,
   because `InsertChild`/`MoveNode` append and the append lands it correctly; otherwise the put
   plus the order it belonged to. The order is read from the PRE-state, exactly as the index used
   to be: the same information, named rather than counted. *)
let restoring (pnode:tree) (target:string) (put:op) : Tot op =
  let order = order_in pnode in
  if last_of order = Some target then put
  else Batch [put; ReorderChildren (tid_of pnode) order]

(* F#: `Ops.invert`'s non-`Batch` arm. The `Option.get`s there are total by `canApply` having
   already passed — `parent_exists` (section 3) is the proof of the one that is not obvious — and
   the model returns the envelope rather than being partial. *)
let invert_leaf (o:leaf_op) (pre:tree) : Tot (outcome op rejection) =
  match can_apply o pre with
  | Error e -> Error e
  | Ok () ->
    (match o with
     | InsertChild _ n -> Ok (RemoveNode (tid_of n))
     | RemoveNode x ->
       (match parent_of x pre with
        | None -> Error (UnknownNode x (ids pre))
        | Some pid ->
          (match find_in pid pre, find_in x pre with
           | Some pnode, Some sub -> Ok (restoring pnode x (InsertChild pid sub))
           | _, _ -> Error (UnknownNode pid (ids pre))))
     | MoveNode x _ ->
       (match parent_of x pre with
        | None -> Error (UnknownNode x (ids pre))
        | Some pid ->
          (match find_in pid pre with
           | Some pnode -> Ok (restoring pnode x (MoveNode x pid))
           | None -> Error (UnknownNode pid (ids pre))))
     | ReorderChildren p _ ->
       (match find_in p pre with
        | Some pn -> Ok (ReorderChildren p (order_in pn))
        | None -> Error (UnknownNode p (ids pre)))
     (* F#: `UpdateNode(Tree.tryFind (w.Id node) pre |> Option.get)` (Phase 250) — the pre-state
        node itself, whose content the undo restores and whose children it does not read. *)
     | UpdateNode n ->
       (match find_in (tid_of n) pre with
        | Some old -> Ok (UpdateNode old)
        | None -> Error (UnknownNode (tid_of n) (ids pre))))

(* ---- small facts about the child-list edits ---- *)

let rec kid_ids_app (xs ys:list tree)
  : Lemma (ensures kid_ids (app xs ys) == app (kid_ids xs) (kid_ids ys)) (decreases xs)
  = match xs with
    | [] -> ()
    | _ :: r -> kid_ids_app r ys

let rec drop_kid_keeps (x:string) (ts:list tree) (c:tree)
  : Lemma (requires mem c ts /\ tid_of c <> x) (ensures mem c (drop_kid x ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> if t = c then () else drop_kid_keeps x r c

let rec kid_ids_drop_kid (y:string) (x:string) (ts:list tree)
  : Lemma (ensures mem y (kid_ids (drop_kid x ts)) ==> (mem y (kid_ids ts) /\ y <> x))
          (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> kid_ids_drop_kid y x r

let rec drop_kid_none (x:string) (ts:list tree)
  : Lemma (requires not (mem x (kid_ids ts))) (ensures drop_kid x ts == ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> drop_kid_none x r

let rec drop_kid_app_last (x:string) (ts:list tree) (n:tree)
  : Lemma (requires not (mem x (kid_ids ts)) /\ tid_of n == x)
          (ensures drop_kid x (app ts [n]) == ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> drop_kid_app_last x r n

let rec has_kid_app_last (x:string) (ts:list tree) (n:tree)
  : Lemma (requires tid_of n == x) (ensures has_kid x (app ts [n])) (decreases ts)
  = match ts with
    | [] -> ()
    | _ :: r -> has_kid_app_last x r n

let rec find_all_app_last (x:string) (ts:list tree) (n:tree)
  : Lemma (requires not (mem x (ids_all ts)) /\ tid_of n == x)
          (ensures find_all x (app ts [n]) == Some n) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> find_in_some_iff x t; find_all_app_last x r n

let rec kid_ids_no_dups_drop (x:string) (ts:list tree)
  : Lemma (requires no_dups (kid_ids ts)) (ensures no_dups (kid_ids (drop_kid x ts)))
          (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      kid_ids_no_dups_drop x r;
      (if mem (tid_of t) (kid_ids (drop_kid x r)) then kid_ids_drop_kid (tid_of t) x r else ())

(* ---- `arrange` restores a list from its own id order ----

   The one lemma both the reorder round trip and the remove round trip turn on, and it is small
   because the induction hypothesis is weaker than it looks: `ts`'s elements only have to be
   AMONG `l`'s, not equal to them, so the recursive step does not have to remove anything. *)
let rec arrange_restore (ts l:list tree)
  : Lemma (requires no_dups (kid_ids l) /\ (forall (c:tree). mem c ts ==> mem c l))
          (ensures arrange (kid_ids ts) l == ts) (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r -> pick_last_unique c l; arrange_restore r l

(* ---- two no-duplicate lists with the same members ARE the same multiset ----

   `validateReorder` decides `same_multiset`, and everything the round trips establish about a
   rearranged child list is about its MEMBERS. `same_multiset_mem` (Phase 133) carries one
   direction; this is the other, which needs the no-duplicate hypothesis and did not exist. *)

let rec remove_first_spec (x:string) (l:list string)
  : Lemma (requires mem x l /\ no_dups l)
          (ensures (match remove_first x l with
                    | Some l' -> no_dups l' /\ (forall (z:string). mem z l' == (mem z l && z <> x))
                    | None -> False)) (decreases l)
  = match l with
    | [] -> ()
    | h :: t -> if h = x then () else remove_first_spec x t

let rec no_dups_same_multiset (xs ys:list string)
  : Lemma (requires no_dups xs /\ no_dups ys /\ (forall (z:string). mem z xs == mem z ys))
          (ensures same_multiset xs ys) (decreases xs)
  = match xs with
    | [] -> (match ys with [] -> () | _ :: _ -> ())
    | x :: r ->
      remove_first_spec x ys;
      (match remove_first x ys with
       | Some ys' -> no_dups_same_multiset r ys'
       | None -> ())

(* ---- and the child-id list is determined by the child SET ---- *)

let rec kid_ids_mem_iff (z:string) (l:list tree)
  : Lemma (ensures mem z (kid_ids l) == Some? (kid_with z l)) (decreases l)
  = match l with
    | [] -> ()
    | t :: r -> if tid_of t = z then () else kid_ids_mem_iff z r

let rec has_kid_of_mem (z:string) (ts:list tree) (c:tree)
  : Lemma (requires mem c ts /\ tid_of c == z) (ensures has_kid z ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> if t = c then () else has_kid_of_mem z r c

let kid_ids_transfer (z:string) (l1 l2:list tree)
  : Lemma (requires forall (c:tree). mem c l1 == mem c l2)
          (ensures mem z (kid_ids l1) == mem z (kid_ids l2))
  = kid_ids_mem_iff z l1;
    kid_ids_mem_iff z l2;
    (match kid_with z l1 with
     | Some c -> kid_with_mem z l1 c; has_kid_of_mem z l2 c; has_kid_mem z l2
     | None -> ());
    (match kid_with z l2 with
     | Some c -> kid_with_mem z l2 c; has_kid_of_mem z l1 c; has_kid_mem z l1
     | None -> ())

let rec kid_ids_drop_kid_conv (y x:string) (ts:list tree)
  : Lemma (requires mem y (kid_ids ts) /\ y <> x)
          (ensures mem y (kid_ids (drop_kid x ts))) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> if tid_of t = y then () else kid_ids_drop_kid_conv y x r

let rec last_of_mem (y:string) (l:list string)
  : Lemma (requires last_of l == Some y) (ensures mem y l) (decreases l)
  = match l with
    | [] -> ()
    | [_] -> ()
    | _ :: r -> last_of_mem y r

(* ---- the last-child case: dropping a list's last child and appending it back is the identity ---- *)
let rec drop_last_app (x:string) (cs:list tree) (sub:tree)
  : Lemma (requires no_dups (kid_ids cs) /\ mem sub cs /\ tid_of sub == x /\
                    last_of (kid_ids cs) == Some x)
          (ensures app (drop_kid x cs) [sub] == cs) (decreases cs)
  = match cs with
    | [] -> ()
    | c :: r ->
      if tid_of c = x then
        (* no_dups forbids a second `x` further along, so `x` being the LAST id forces `r` empty *)
        (match r with
         | [] -> ()
         | _ -> last_of_mem x (kid_ids r))
      else drop_last_app x r sub

(* ---- the whole of the remove round trip, at the ONE node that matters ----

   Kept as its own lemma with its own small context rather than inlined into the descent below.
   That is not tidiness: inlined, the descent's query carried this argument on top of the tree
   recursion's and the solver stopped terminating in any useful time. A children-level fact
   proved once at children-level scope is the difference. *)
let parent_node_restore (x:string) (cs:list tree) (sub:tree)
  : Lemma (requires wf_all cs /\ mem sub cs /\ tid_of sub == x)
          (ensures arrange (kid_ids cs) (app (drop_kid x cs) [sub]) == cs /\
                   same_multiset (kid_ids (app (drop_kid x cs) [sub])) (kid_ids cs) /\
                   (last_of (kid_ids cs) == Some x ==> app (drop_kid x cs) [sub] == cs))
  = wf_all_kid_ids_no_dups cs;
    let l = drop_kid x cs in
    kid_ids_app l [sub];
    kid_ids_no_dups_drop x cs;
    kid_ids_of_mem sub cs;
    let aux (y:string) : Lemma (mem y (kid_ids l) ==> y <> x) =
      if mem y (kid_ids l) then kid_ids_drop_kid y x cs else ()
    in
    FStar.Classical.forall_intro aux;
    inter_nil_iff (kid_ids l) (kid_ids [sub]);
    no_dups_app (kid_ids l) (kid_ids [sub]);
    let aux2 (e:tree) : Lemma (mem e cs ==> mem e (app l [sub])) =
      if mem e cs then
        (if tid_of e = x then
           (* two children with one id: `wf_all` says there are none, so `e` IS `sub` *)
           (pick_last_unique e cs; pick_last_unique sub cs; mem_app e l [sub])
         else (drop_kid_keeps x cs e; mem_app e l [sub]))
      else ()
    in
    FStar.Classical.forall_intro aux2;
    arrange_restore cs (app l [sub]);
    (* the reorder half of the undo is ACCEPTED: the child ids after putting the subtree back are
       the child ids that were there, as a multiset — which is what `validateReorder` decides *)
    let aux3 (z:string) : Lemma (mem z (kid_ids (app l [sub])) == mem z (kid_ids cs)) =
      if z = x then ()
      else (if mem z (kid_ids l) then kid_ids_drop_kid z x cs
            else (if mem z (kid_ids cs) then kid_ids_drop_kid_conv z x cs else ()))
    in
    FStar.Classical.forall_intro aux3;
    no_dups_same_multiset (kid_ids (app l [sub])) (kid_ids cs);
    (if last_of (kid_ids cs) = Some x then drop_last_app x cs sub else ())

(* Which child the lookup stopped at, when the target IS one of this node's children. *)
let find_in_kid_is_sub (x:string) (cs:list tree) (sub:tree)
  : Lemma (requires wf_all cs /\ has_kid x cs /\ find_all x cs == Some sub)
          (ensures mem sub cs /\ tid_of sub == x)
  = find_all_id x cs sub;
    find_all_locate x cs;
    locate_mem x cs;
    kid_with_some x cs;
    wf_all_holder_unique x cs;
    (match kid_with x cs with
     | Some d ->
       kid_with_mem x cs d;
       tid_in_ids d;
       (match locate x cs with
        | Some c -> find_in_self x c
        | None -> ())
     | None -> ())

(* ======================================================================================
   7. THE FIFTH LEMMA — the inverse of an accepted operation is accepted at the result and
      restores the input.
   ====================================================================================== *)

(* ---- an insert, undone by the remove of what it added ---- *)

let rec ins_rem_inverse (p:string) (n:tree) (t:tree)
  : Lemma (requires wf t /\ disjoint (ids n) (ids t))
          (ensures rem_at p (tid_of n) (ins p n t) == t) (decreases t)
  = inter_nil_iff (ids n) (ids t);
    match t with
    | TNode i k cs ->
      inter_nil_iff (ids n) (ids_all cs);
      if i = p then begin
        ins_all_absent p n cs;
        ids_all_app cs [n];
        rem_all_absent p (tid_of n) (app cs [n]);
        (if mem (tid_of n) (kid_ids cs) then kid_ids_sub (tid_of n) cs else ());
        drop_kid_app_last (tid_of n) cs n
      end
      else ins_rem_inverse_all p n cs
and ins_rem_inverse_all (p:string) (n:tree) (ts:list tree)
  : Lemma (requires wf_all ts /\ disjoint (ids n) (ids_all ts))
          (ensures rem_all p (tid_of n) (ins_all p n ts) == ts) (decreases ts)
  = inter_nil_iff (ids n) (ids_all ts);
    match ts with
    | [] -> ()
    | t :: r ->
      inter_nil_iff (ids n) (ids t);
      inter_nil_iff (ids n) (ids_all r);
      ins_rem_inverse p n t;
      ins_rem_inverse_all p n r

let rec parent_of_ins (p:string) (n:tree) (t:tree)
  : Lemma (requires wf t /\ mem p (ids t) /\ disjoint (ids n) (ids t))
          (ensures parent_of (tid_of n) (ins p n t) == Some p /\
                   find_in (tid_of n) (ins p n t) == Some n) (decreases t)
  = inter_nil_iff (ids n) (ids t);
    match t with
    | TNode i k cs ->
      inter_nil_iff (ids n) (ids_all cs);
      if i = p then begin
        ins_all_absent p n cs;
        has_kid_app_last (tid_of n) cs n;
        find_all_app_last (tid_of n) cs n
      end
      else begin
        kid_ids_ins_all p n cs;
        (if mem (tid_of n) (kid_ids cs) then kid_ids_sub (tid_of n) cs else ());
        (if has_kid (tid_of n) (ins_all p n cs) then has_kid_mem (tid_of n) (ins_all p n cs)
         else ());
        find_in_some_iff (tid_of n) t;
        parent_of_ins_all p n cs
      end
and parent_of_ins_all (p:string) (n:tree) (ts:list tree)
  : Lemma (requires wf_all ts /\ mem p (ids_all ts) /\ disjoint (ids n) (ids_all ts))
          (ensures parent_all (tid_of n) (ins_all p n ts) == Some p /\
                   find_all (tid_of n) (ins_all p n ts) == Some n) (decreases ts)
  = inter_nil_iff (ids n) (ids_all ts);
    match ts with
    | [] -> ()
    | t :: r ->
      inter_nil_iff (ids n) (ids t);
      inter_nil_iff (ids n) (ids_all r);
      if mem p (ids t) then parent_of_ins p n t
      else begin
        ins_absent p n t;
        parent_absent (tid_of n) t;
        find_in_some_iff (tid_of n) t;
        parent_of_ins_all p n r
      end

(* ---- a reorder, undone by the order it replaced ---- *)

let rec reorder_inverse (p:string) (ord back:list string) (t:tree)
  : Lemma (requires wf t /\ reorder_ok p ord t /\
                    (match find_in p t with
                     | Some pn -> back == order_in pn
                     | None -> True))
          (ensures reorder_at p back (reorder_at p ord t) == t) (decreases t)
  = match t with
    | TNode i k cs ->
      if i = p then begin
        reorder_all_absent p ord cs;
        wf_all_kid_ids_no_dups cs;
        same_multiset_no_dups (kid_ids cs) ord;
        arrange_wf_all ord cs;
        wf_all_kid_ids_no_dups (arrange ord cs);
        arrange_elems ord cs;
        ids_all_mem_transfer p (arrange ord cs) cs;
        reorder_all_absent p back (arrange ord cs);
        arrange_restore cs (arrange ord cs)
      end
      else begin
        find_all_some_iff p cs;
        reorder_inverse_all p ord back cs
      end
and reorder_inverse_all (p:string) (ord back:list string) (ts:list tree)
  : Lemma (requires wf_all ts /\ reorder_ok_all p ord ts /\
                    (match find_all p ts with
                     | Some pn -> back == order_in pn
                     | None -> True))
          (ensures reorder_all p back (reorder_all p ord ts) == ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      find_in_some_iff p t;
      if mem p (ids t) then begin
        reorder_inverse p ord back t;
        inter_nil_iff (ids t) (ids_all r);
        reorder_all_absent p ord r;
        reorder_all_absent p back r
      end
      else begin
        reorder_absent p ord t;
        reorder_absent p back t;
        reorder_inverse_all p ord back r
      end

(* ---- a remove, undone by putting the subtree back and restating the order ---- *)

let rec rem_ins_inverse (pid x:string) (t:tree) (sub:tree) (back:list string)
  : Lemma (requires wf t /\ parent_of x t == Some pid /\ find_in x t == Some sub /\
                    (match find_in pid t with
                     | Some pn -> back == order_in pn
                     | None -> False))
          (ensures reorder_at pid back (ins pid sub (rem_at pid x t)) == t /\
                   (match find_in pid (ins pid sub (rem_at pid x t)) with
                    | Some m -> same_multiset (kid_ids (kids_of m)) back
                    | None -> False) /\
                   (last_of back == Some x ==> ins pid sub (rem_at pid x t) == t))
          (decreases t)
  = match t with
    | TNode i k cs ->
      if has_kid x cs then begin
        (* `pid` is this node — well-formedness says the id does not occur below it — so all three
           edits happen in this node's child list, and `parent_node_restore` is the whole of it. *)
        has_kid_mem x cs;
        kid_ids_sub x cs;
        rem_all_absent i x cs;
        find_in_kid_is_sub x cs sub;
        let l = drop_kid x cs in
        ids_drop_kid_sub_fa x cs;
        ins_all_absent i sub l;
        ids_all_app l [sub];
        (if mem i (ids sub) then mem_ids_all_intro i sub cs else ());
        reorder_all_absent i back (app l [sub]);
        parent_node_restore x cs sub
      end
      else begin
        parent_all_mem x cs pid;
        find_all_wf x cs sub;
        find_all_some_iff pid cs;
        rem_ins_inverse_all pid x cs sub back
      end
and rem_ins_inverse_all (pid x:string) (ts:list tree) (sub:tree) (back:list string)
  : Lemma (requires wf_all ts /\ not (has_kid x ts) /\
                    parent_all x ts == Some pid /\ find_all x ts == Some sub /\
                    (match find_all pid ts with
                     | Some pn -> back == order_in pn
                     | None -> False))
          (ensures reorder_all pid back (ins_all pid sub (rem_all pid x ts)) == ts /\
                   (match find_all pid (ins_all pid sub (rem_all pid x ts)) with
                    | Some m -> same_multiset (kid_ids (kids_of m)) back
                    | None -> False) /\
                   (last_of back == Some x ==> ins_all pid sub (rem_all pid x ts) == ts))
          (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      find_in_some_iff x t;
      if mem x (ids t) then begin
        parent_exists x t;
        parent_of_mem x t pid;
        find_in_some_iff pid t;
        rem_ins_inverse pid x t sub back;
        inter_nil_iff (ids t) (ids_all r);
        rem_all_absent pid x r;
        ins_all_absent pid sub r;
        reorder_all_absent pid back r
      end
      else begin
        parent_absent x t;
        parent_all_mem x r pid;
        inter_nil_iff (ids t) (ids_all r);
        rem_absent pid x t;
        ins_absent pid sub t;
        reorder_absent pid back t;
        find_in_some_iff pid t;
        rem_ins_inverse_all pid x r sub back
      end

(* ---- the removal leaves the PARENT standing, which is both of the move round trip's two
   remaining guards: the inverse move needs its destination to still exist, and — with
   `rem_kills` beside it — that the destination is not inside the subtree being moved ---- *)

(* F#: `Tree.updateNode` rebuilds a node in place, so the root's id is never one of the ids an
   edit can move. `TreeOps.tid_ins` / `tid_reorder` say it for the other two edits. *)
let tid_rem (pid x:string) (t:tree)
  : Lemma (ensures tid_of (rem_at pid x t) == tid_of t)
  = match t with TNode _ _ _ -> ()

let rec rem_keeps_parent (pid x:string) (t:tree)
  : Lemma (requires wf t /\ parent_of x t == Some pid)
          (ensures mem pid (ids (rem_at pid x t))) (decreases t)
  = match t with
    | TNode i _ cs ->
      if has_kid x cs then ()
      else (parent_all_mem x cs pid; rem_keeps_parent_all pid x cs)
and rem_keeps_parent_all (pid x:string) (ts:list tree)
  : Lemma (requires wf_all ts /\ not (has_kid x ts) /\ parent_all x ts == Some pid)
          (ensures mem pid (ids_all (rem_all pid x ts))) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      if mem x (ids t) then (parent_exists x t; rem_keeps_parent pid x t)
      else (parent_absent x t; rem_keeps_parent_all pid x r)

(* ---- where the reorder's own undo lands, and that it is accepted ---- *)

let rec find_in_reorder_self (p:string) (ord:list string) (t:tree)
  : Lemma (requires wf t /\ reorder_ok p ord t)
          (ensures (match find_in p t with
                    | None -> True
                    | Some pn -> find_in p (reorder_at p ord t) ==
                                 Some (TNode p (kind_of pn) (arrange ord (kids_of pn)))))
          (decreases t)
  = match t with
    | TNode i k cs ->
      if i = p then reorder_all_absent p ord cs
      else (find_all_some_iff p cs; find_in_reorder_self_all p ord cs)
and find_in_reorder_self_all (p:string) (ord:list string) (ts:list tree)
  : Lemma (requires wf_all ts /\ reorder_ok_all p ord ts)
          (ensures (match find_all p ts with
                    | None -> True
                    | Some pn -> find_all p (reorder_all p ord ts) ==
                                 Some (TNode p (kind_of pn) (arrange ord (kids_of pn)))))
          (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      find_in_some_iff p t;
      if mem p (ids t) then
        (find_in_reorder_self p ord t;
         find_in_some_iff p (reorder_at p ord t);
         ids_reorder_mem p p ord t)
      else (reorder_absent p ord t; find_in_reorder_self_all p ord r)

(* the rearranged child list is the same child ids as a multiset — `validateReorder`'s test *)
let arrange_same_multiset (ord:list string) (cs:list tree)
  : Lemma (requires wf_all cs /\ same_multiset (kid_ids cs) ord)
          (ensures same_multiset (kid_ids (arrange ord cs)) (kid_ids cs))
  = wf_all_kid_ids_no_dups cs;
    same_multiset_no_dups (kid_ids cs) ord;
    arrange_wf_all ord cs;
    wf_all_kid_ids_no_dups (arrange ord cs);
    arrange_elems ord cs;
    let aux (z:string) : Lemma (mem z (kid_ids (arrange ord cs)) == mem z (kid_ids cs)) =
      kid_ids_transfer z (arrange ord cs) cs
    in
    FStar.Classical.forall_intro aux;
    no_dups_same_multiset (kid_ids (arrange ord cs)) (kid_ids cs)

(* ---- THE FIFTH LEMMA ----

   `apply (invert op pre) (apply op pre) = pre`, for the leaf alphabet. `Ops.invert`'s doc comment
   states it as the defining law and `Conformance.opAlgebra` samples it; here it is proved, in both
   halves at once — the inverse is ACCEPTED at the result (nothing about it can be refused there)
   and applying it RESTORES the input exactly.

   The alphabet is the four non-`Batch` operations, which is the alphabet `Skeleton.fst`'s
   composite theorem already takes and for the same reason: a `Batch`'s inverse is its members'
   inverses in reverse order, each derived against the state that member saw, so the lift is the
   one `DagFold.replay_diamond` performs at lane granularity rather than a new idea. *)
(* The four cases are four independent arguments in one query, and the theorem sits at the top of
   the whole module's context. Measured on the pinned prover it proves 78 goals; at the leg's
   default budget it is close enough to the ceiling that a different seed under `--quake 3` can
   exhaust it, which is a flake rather than a failure. The budget is raised here so the leg is
   deterministic — the README's finding on proof-cost management, applied. *)
#push-options "--z3rlimit 200"
let invert_applicable (o:leaf_op) (t:tree)
  : Lemma (requires wf t /\ Ok? (apply o t))
          (ensures (match invert_leaf o t, apply o t with
                    | Ok inv, Ok t' -> apply inv t' == Ok t
                    | _, _ -> False))
  = match o with

    | InsertChild p n ->
      (* undone by removing what it added: the append is the only place the id can be *)
      first_dup_none_iff n t;
      tid_in_ids t;
      tid_in_ids n;
      inter_nil_iff (ids n) (ids t);
      tid_ins p n t;
      ids_ins_mem (tid_of n) p n t;
      parent_of_ins p n t;
      ins_rem_inverse p n t

    | ReorderChildren p ord ->
      (* undone by the order it replaced, which `restoring` read from the pre-state *)
      (match find_in p t with
       | None -> ()
       | Some pn ->
         wf_reorder_ok p ord t;
         find_in_id p t pn;
         find_in_wf p t pn;
         find_in_reorder_self p ord t;
         arrange_same_multiset ord (kids_of pn);
         reorder_inverse p ord (order_in pn) t)

    | RemoveNode x ->
      (match parent_of x t with
       | None -> ()
       | Some pid ->
         parent_of_mem x t pid;
         find_in_some_iff pid t;
         find_in_some_iff x t;
         (match find_in pid t, find_in x t with
          | Some pnode, Some sub ->
            let removed = rem_at pid x t in
            let back = order_in pnode in
            (* the graft back is accepted: the subtree is id-unique in itself (it is a subtree of
               a well-formed tree) and its ids left with it (`rem_kills`) *)
            find_in_id pid t pnode;
            find_in_id x t sub;
            tid_rem pid x t;
            find_in_wf x t sub;
            wf_iff_no_dups sub;
            rem_kills pid x t sub;
            inter_nil_iff (ids sub) (ids removed);
            first_dup_none_iff sub removed;
            rem_keeps_parent pid x t;
            rem_ins_inverse pid x t sub back
          | _, _ -> ()))

    | MoveNode x np ->
      (match find_in x t, parent_of x t with
       | Some sub, Some pid ->
         parent_of_mem x t pid;
         find_in_some_iff pid t;
         (match find_in pid t with
          | None -> ()
          | Some pnode ->
            let removed = rem_at pid x t in
            let back = order_in pnode in
            let t2 = ins np sub removed in
            (* the state the move left: the subtree hanging under its new parent *)
            find_in_id x t sub;
            find_in_id pid t pnode;
            tid_rem pid x t;
            find_in_wf x t sub;
            wf_iff_no_dups sub;
            rem_wf pid x t;
            rem_kills pid x t sub;
            inter_nil_iff (ids sub) (ids removed);
            first_dup_none_iff sub removed;
            (* the move back: its destination survived the removal, and — since the removal took
               the whole subtree's ids with it — the destination is not inside the subtree *)
            rem_keeps_parent pid x t;
            tid_ins np sub removed;
            tid_in_ids t;
            ids_ins_mem pid np sub removed;
            ids_ins_mem x np sub removed;
            tid_in_ids sub;
            parent_of_ins np sub removed;
            ins_rem_inverse np sub removed;
            rem_ins_inverse pid x t sub back
          | _ -> ())
       | _, _ -> ())

    | UpdateNode n ->
      (* undone by the content it replaced: the old node's kind, rewritten over the new one, is the
         old tree, because on a well-formed tree the id is carried once *)
      let x = tid_of n in
      (match find_in x t with
       | None -> ()
       | Some old ->
         find_in_id x t old;
         ids_upd x (kind_of n) t;
         find_in_some_iff x t;
         find_in_some_iff x (upd x (kind_of n) t);
         upd_upd x (kind_of n) (kind_of old) t;
         upd_found_self x t old)

#pop-options

(* ======================================================================================
   8. THE CONTAINER CAPABILITY — `Ops.applyContained`, and the invariant it exists to keep
      (Phase 140).

      `Ops.apply` is `applyWith (fun _ -> true)`, which is why section 1 could prove
      `NotAContainer` UNREACHABLE from it: the clause that raises it is dead when every node can
      hold children. `Ops.applyContained canHold` is the variant every domain with a leaf actually
      runs, and it is the one where that clause is live. This section models it with `canHold`
      ABSTRACT and proves the three things the shipped code's doc comment asserts.

      ABSTRACT MEANS A PARAMETER, not an assumption. `ch` is a parameter of every definition and
      every theorem below, so each is universally quantified over it — a strictly stronger reading
      than one assumed constant, and the only one available in any case: the leg runs
      `--report_assumes error`, so an `assume val can_hold` would fail it, and rightly.

      WHERE THE PREDICATE IS CONSULTED, read off `Ops.fs` rather than off the doc comment:
      `validateInsert` applies it to the node `Tree.tryFind` returns for the PARENT, and — since
      Phase 161 — to every node of the INSERTED SUBTREE that holds children; the `MoveNode` arm
      applies it to the NEW PARENT. Nowhere else: not on remove, not on reorder, and not on the
      interior of a MOVED subtree, which is deliberate and is argued at the move clause of 8.5.

      ONE HYPOTHESIS, AND ONE THAT WAS DISCHARGED BY A CODE CHANGE. The sentence "`applyContained`
      preserves the invariant that every node with children satisfies `canHold`" was FALSE as
      stated of the function Phase 140 measured, in two independent ways. Section 8.6 exhibits
      both, and they now have different statuses:

        - `child_blind` — STILL A HYPOTHESIS, and it always will be. `canHold` has type
          `'Node -> bool`, so it may read the CHILD LIST, and a predicate that does can flip from
          admitting to refusing at the instant its node gains a child. No check placed anywhere in
          the engine repairs that, because the predicate's answer changes under the very edit the
          check licensed. Every domain writes it as a function of the kind tag (which is also what
          `NotAContainer` reports back), and this hypothesis is that habit made a premise — now a
          habit the domain CERTIFIES, by `Conformance.containerLaws`' child-perturbation law.
        - `contained_op` — DISCHARGED BY THE CODE (Phase 161, DECISIONS D38). The graft used not to
          be inspected, so inserting a subtree whose own interior node was a non-container carried
          the violation in with it; the operator's ruling was to inspect it. The hypothesis is gone
          from `contained_preserves`, and section 8.6's `nested_graft_refused` keeps the refutation
          evaluable against `apply_contained_pre161` beside the refusal the shipped engine now
          makes — so the reason the premise was needed survives the premise.

      Neither was a modelling convenience. Each was a real property of a real predicate that the
      engine's TYPE does not demand, and naming them is the difference between a theorem about
      `applyContained` and a theorem about a function nobody calls. What changed is that one of
      them is now a property of a real ENGINE instead.
   ====================================================================================== *)

(* ---- 8.1 the invariant, the operation's own trees, and the stability premise ---- *)

(* "every node with children satisfies `canHold`". A childless node is unconstrained: the predicate
   answers "can this node hold children AT ALL", so a leaf that never holds any says nothing. *)
let rec contained (ch:tree -> bool) (t:tree) : Tot bool (decreases t) =
  match t with
  | TNode _ _ cs -> (match cs with [] -> true | _ -> ch t) && contained_all ch cs
and contained_all (ch:tree -> bool) (ts:list tree) : Tot bool (decreases ts) =
  match ts with
  | [] -> true
  | t :: r -> contained ch t && contained_all ch r

(* The trees an operation CARRIES — only `InsertChild` carries one, and a `Batch` its members'. *)
let rec contained_op (ch:tree -> bool) (o:op) : Tot bool (decreases o) =
  match o with
  | InsertChild _ n -> contained ch n
  | Batch os -> contained_op_all ch os
  | _ -> true
and contained_op_all (ch:tree -> bool) (os:list op) : Tot bool (decreases os) =
  match os with
  | [] -> true
  | o :: r -> contained_op ch o && contained_op_all ch r

(* The predicate does not read the child list. Stated over the constructor rather than as "depends
   only on the kind tag", because that is exactly the fact every proof below needs and it leaves a
   predicate reading the ID lawful — which some domains' do. *)
let child_blind (ch:tree -> bool) : prop =
  forall (i k:string) (cs cs':list tree). ch (TNode i k cs) == ch (TNode i k cs')

(* ---- 8.1a the graft's own interior (Phase 161) ----

   F#: `Ops.firstUncontained`. The first node of a subtree, in preorder, that HOLDS children while
   the capability refuses it — the offender `validateInsert` now names. This is the decidable form
   of `contained`: the engine cannot branch on a proposition, so the clause it gained branches on
   this, and `first_uncontained_none` below is the bridge between the two. *)
let rec first_uncontained (ch:tree -> bool) (t:tree) : Tot (option tree) (decreases t) =
  match t with
  | TNode _ _ cs -> if Cons? cs && not (ch t) then Some t else first_uncontained_all ch cs
and first_uncontained_all (ch:tree -> bool) (ts:list tree) : Tot (option tree) (decreases ts) =
  match ts with
  | [] -> None
  | t :: r -> (match first_uncontained ch t with
               | Some o -> Some o
               | None -> first_uncontained_all ch r)

(* THE BRIDGE. `None` exactly when the subtree satisfies the invariant — so the clause the engine
   added is a decision procedure for `contained`, and the preservation proof can read `contained ch
   n` off a branch the engine actually took. Both directions, because both are used: the accept
   branch needs `None ==> contained`, and section 8.6's historical counterexample needs the
   converse to show the refusal is not over-eager. *)
let rec first_uncontained_none (ch:tree -> bool) (t:tree)
  : Lemma (ensures (None? (first_uncontained ch t) <==> contained ch t)) (decreases t)
  = match t with
    | TNode _ _ cs -> if Cons? cs && not (ch t) then () else first_uncontained_none_all ch cs
and first_uncontained_none_all (ch:tree -> bool) (ts:list tree)
  : Lemma (ensures (None? (first_uncontained_all ch ts) <==> contained_all ch ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> first_uncontained_none ch t;
                (match first_uncontained ch t with
                 | Some _ -> ()
                 | None -> first_uncontained_none_all ch r)

(* WHERE THE OFFENDER IS. The node the refusal names is one the GRAFT holds, it really does hold
   children, and the predicate really does refuse it. The counterpart of `not_a_container_locates`
   for the new site — and it has to be a separate statement, because the node is in the caller's own
   subtree rather than in the tree the operation was applied to. *)
let rec first_uncontained_some (ch:tree -> bool) (t:tree) (o:tree)
  : Lemma (requires first_uncontained ch t == Some o)
          (ensures mem (tid_of o) (ids t) /\ not (ch o) /\ Cons? (kids_of o)) (decreases t)
  = match t with
    | TNode _ _ cs -> if Cons? cs && not (ch t) then () else first_uncontained_some_all ch cs o
and first_uncontained_some_all (ch:tree -> bool) (ts:list tree) (o:tree)
  : Lemma (requires first_uncontained_all ch ts == Some o)
          (ensures mem (tid_of o) (ids_all ts) /\ not (ch o) /\ Cons? (kids_of o)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> (match first_uncontained ch t with
                 | Some _ -> first_uncontained_some ch t o
                 | None -> first_uncontained_some_all ch r o)

(* At `fun _ -> true` nothing is ever an offender, so the clause is INERT under `Ops.apply` and the
   plain engine is byte-for-byte what it was. Section 8.3's first theorem is where that is cashed
   in; the production-side assertion is the `Ops.applyContained` suite's
   "the plain apply is byte-for-byte unaffected by the interior walk". *)
let rec first_uncontained_trivial (t:tree)
  : Lemma (ensures first_uncontained (fun _ -> true) t == None) (decreases t)
  = match t with TNode _ _ cs -> first_uncontained_trivial_all cs
and first_uncontained_trivial_all (ts:list tree)
  : Lemma (ensures first_uncontained_all (fun _ -> true) ts == None) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> first_uncontained_trivial t; first_uncontained_trivial_all r

(* ---- 8.2 `Ops.applyContained`, clause for clause ---- *)

(* F#: `applyWith canHold w idw`, of which `Ops.apply` (section 6 of `TreeOps.fst`) is the
   instance at `fun _ -> true`. Every clause below is that model's clause with the container check
   spliced in at the two places `Ops.fs` splices it, in the ORDER it splices them — which matters:
   a move into a non-container that would also nest under itself earns `NotAContainer`, because
   the capability check comes first (Ops.fs:249 precedes the descendant test at Ops.fs:258).

   The two `| None ->` arms after a `find_in` guarded by `has_id` are the model's total form of a
   lookup the shipped code writes as a `match` with a fall-through: they are UNREACHABLE, by
   `find_in_some_iff`, and section 8.3's first theorem is where that is discharged rather than
   asserted. *)
let rec apply_contained (ch:tree -> bool) (o:op) (t:tree)
  : Tot (outcome tree rejection) (decreases o) =
  match o with

  (* F#: `validateInsert` — the Phase 137 scan, the parent's existence, the parent's capability,
     and THEN (Phase 161) the graft's own interior. The order is `Ops.fs`'s and it is D38's
     decision: the new clause is last, so no operation that was refused before Phase 161 changes
     its class. *)
  | InsertChild p n ->
    (match first_dup n t with
     | Some d -> Error (DuplicateId d)
     | None ->
       if not (has_id p t) then Error (UnknownNode p (ids t))
       else (match find_in p t with
             | None -> Error (UnknownNode p (ids t))
             | Some pn ->
               if not (ch pn) then Error (NotAContainer p (kind_of pn))
               else (match first_uncontained ch n with
                     | Some off -> Error (NotAContainer (tid_of off) (kind_of off))
                     | None -> Ok (ins p n t))))

  (* F#: `validateRemove` then the `parentOf` lookup — `canHold` is not consulted. *)
  | RemoveNode x ->
    if tid_of t = x then Error CannotRemoveRoot
    else if not (has_id x t) then Error (UnknownNode x (ids t))
    else (match parent_of x t with
          | None -> Error (UnknownNode x (ids t))
          | Some pid -> Ok (rem_at pid x t))

  (* F#: `validateReorder` — `canHold` is not consulted, and a reorder cannot give a childless
     node children. *)
  | ReorderChildren p order ->
    (match find_in p t with
     | None -> Error (UnknownNode p (ids t))
     | Some n ->
       let current = kid_ids (kids_of n) in
       if not (same_multiset current order) then Error (ReorderMismatch p current order)
       else Ok (reorder_at p order t))

  (* F#: the `MoveNode` arm, with the capability test on the NEW PARENT between the endpoint
     existence checks and the descendant test. *)
  | MoveNode x np ->
    if tid_of t = x then Error CannotRemoveRoot
    else if not (has_id x t) then Error (UnknownNode x (ids t))
    else if not (has_id np t) then Error (UnknownNode np (ids t))
    else (match find_in np t with
          | None -> Error (UnknownNode np (ids t))
          | Some np0 ->
            if not (ch np0) then Error (NotAContainer np (kind_of np0))
            else (match find_in x t with
                  | None -> Error (UnknownNode x (ids t))
                  | Some sub ->
                    if mem np (ids sub) then Error (WouldNestUnderSelf x)
                    else (match parent_of x t with
                          | None -> Error (UnknownNode x (ids t))
                          | Some pid ->
                            let removed = rem_at pid x t in
                            if not (has_id np removed) then Error (UnknownNode np (ids removed))
                            else Ok (ins np sub removed))))

  | Batch os -> apply_contained_all ch os t

  (* F#: `validateUpdate` (Phase 250) — the target must be in the tree, and when the node it
     rewrites holds children, the REWRITTEN node (the payload's content over those children) must
     be able to hold them. *)
  | UpdateNode n ->
    (match find_in (tid_of n) t with
     | None -> Error (UnknownNode (tid_of n) (ids t))
     | Some ex ->
       if Cons? (kids_of ex) && not (ch (TNode (tid_of n) (kind_of n) (kids_of ex)))
       then Error (NotAContainer (tid_of n) (kind_of n))
       else Ok (upd (tid_of n) (kind_of n) t))

and apply_contained_all (ch:tree -> bool) (os:list op) (t:tree)
  : Tot (outcome tree rejection) (decreases os) =
  match os with
  | [] -> Ok t
  | o :: r -> (match apply_contained ch o t with
               | Ok t' -> apply_contained_all ch r t'
               | Error e -> Error e)

(* F#: `canApplyWith canHold` — the same three validators, and the same simulation for the two
   order-dependent operations. `Ops.canApplyContained` is this; `Ops.canApply` is it at
   `fun _ -> true`. *)
let can_apply_contained (ch:tree -> bool) (o:op) (t:tree) : Tot (outcome unit rejection) =
  match o with
  | InsertChild p n ->
    (match first_dup n t with
     | Some d -> Error (DuplicateId d)
     | None ->
       if not (has_id p t) then Error (UnknownNode p (ids t))
       else (match find_in p t with
             | None -> Error (UnknownNode p (ids t))
             | Some pn ->
               if not (ch pn) then Error (NotAContainer p (kind_of pn))
               else (match first_uncontained ch n with
                     | Some off -> Error (NotAContainer (tid_of off) (kind_of off))
                     | None -> Ok ())))
  | RemoveNode x ->
    if tid_of t = x then Error CannotRemoveRoot
    else if not (has_id x t) then Error (UnknownNode x (ids t))
    else Ok ()
  | ReorderChildren p order ->
    (match find_in p t with
     | None -> Error (UnknownNode p (ids t))
     | Some n ->
       let current = kid_ids (kids_of n) in
       if not (same_multiset current order) then Error (ReorderMismatch p current order)
       else Ok ())
  | UpdateNode n ->
    (match find_in (tid_of n) t with
     | None -> Error (UnknownNode (tid_of n) (ids t))
     | Some ex ->
       if Cons? (kids_of ex) && not (ch (TNode (tid_of n) (kind_of n) (kids_of ex)))
       then Error (NotAContainer (tid_of n) (kind_of n))
       else Ok ())
  | MoveNode _ _
  | Batch _ -> (match apply_contained ch o t with Ok _ -> Ok () | Error e -> Error e)

(* F#: `Ops.canApplyAll` — and note what it threads. It is `apply`, not `applyWith canHold`
   (Ops.fs:382), as is `Ops.applyAll` (Ops.fs:363): there is NO container-aware sequence surface
   in the shipped code. The model drops the failing INDEX the production signature returns, which
   is the only thing it drops; section 8.6's last lemma is what that clause is here for. *)
let rec can_apply_all (os:list op) (t:tree) : Tot (outcome unit rejection) (decreases os) =
  match os with
  | [] -> Ok ()
  | o :: r -> (match apply o t with
               | Ok t' -> can_apply_all r t'
               | Error e -> Error e)

(* THE PRE-161 ENGINE, kept for the same reason `TreeOps.apply_pre137` is kept: the counterexample
   that motivated a change stays evaluable after the change, so the refutation can be RE-STATED as
   history rather than deleted along with the defect. Its insert clause is the one above without
   the graft walk — `canHold` on the parent and on nothing inside the subtree.

   Section 8.6 is where the pair is put to work: this engine admits a graft that breaks the
   invariant, and the shipped one refuses it naming the offender. Delete it and the ONLY record
   that the premise was ever needed is prose. *)
let apply_contained_pre161 (ch:tree -> bool) (o:op) (t:tree) : Tot (outcome tree rejection) =
  match o with
  | InsertChild p n ->
    (match first_dup n t with
     | Some d -> Error (DuplicateId d)
     | None ->
       if not (has_id p t) then Error (UnknownNode p (ids t))
       else (match find_in p t with
             | None -> Error (UnknownNode p (ids t))
             | Some pn ->
               if not (ch pn) then Error (NotAContainer p (kind_of pn))
               else Ok (ins p n t)))
  | _ -> apply_contained ch o t

(* THE GO-RED INSTRUMENT, and it is a definition rather than a test fixture for the same reason
   `TreeOps.apply_pre137` is: an instrument the model can evaluate is one the model can be held
   to. This is the engine a plausible reading of the doc comment describes — "an `InsertChild`
   under a leaf is a typed `NotAContainer`" — with the move half left out. Section 8.6 proves it
   admits a tree the real one refuses, so handing it to the differential is a measurement rather
   than a hope. *)
let rec apply_contained_insert_only (ch:tree -> bool) (o:op) (t:tree)
  : Tot (outcome tree rejection) (decreases o) =
  match o with
  | InsertChild _ _ -> apply_contained ch o t
  | Batch os -> apply_contained_insert_only_all ch os t
  | _ -> apply o t
and apply_contained_insert_only_all (ch:tree -> bool) (os:list op) (t:tree)
  : Tot (outcome tree rejection) (decreases os) =
  match os with
  | [] -> Ok t
  | o :: r -> (match apply_contained_insert_only ch o t with
               | Ok t' -> apply_contained_insert_only_all ch r t'
               | Error e -> Error e)

(* ---- 8.3 THE SIXTH LEMMA — `NotAContainer` is exactly the container refusal ----

   Three statements that together say what the class means, and the first is the one that makes
   the model's fidelity checkable rather than asserted. *)

(* At `fun _ -> true` the container-aware engine IS `Ops.apply`. Clause-for-clause fidelity to
   `TreeOps.apply`, discharged by the prover rather than by reading the two side by side — and
   with it the two unreachable `find_in` fall-throughs above. *)
let rec apply_contained_is_apply (o:op) (t:tree)
  : Lemma (ensures apply_contained (fun _ -> true) o t == apply o t) (decreases o)
  = match o with
    (* Phase 161's clause is INERT here: at `fun _ -> true` no node is ever an offender, so the
       graft walk answers `None` and the plain engine is byte-for-byte what it was. *)
    | InsertChild p n -> first_uncontained_trivial n;
                         if has_id p t then find_in_some_iff p t else ()
    | MoveNode x np ->
      if tid_of t <> x && has_id x t && has_id np t then find_in_some_iff np t else ()
    | Batch os -> apply_contained_all_is_apply os t
    | _ -> ()
and apply_contained_all_is_apply (os:list op) (t:tree)
  : Lemma (ensures apply_contained_all (fun _ -> true) os t == apply_all os t) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      apply_contained_is_apply o t;
      (match apply o t with
       | Ok t' -> apply_contained_all_is_apply r t'
       | Error _ -> ())

(* THE DIFFERENCE. The container-aware engine agrees with the plain one EXCEPT where it raises
   `NotAContainer` — one statement carrying both halves of "exactly": the capability can only ever
   refuse (never accept something `apply` refuses, never refuse it differently, never produce a
   different tree), and the refusal it makes is always this class.

   For a `Batch` the inheritance is what makes the statement worth having: a script whose third
   step lands under a leaf is refused as a whole, with the same envelope, and the two engines are
   otherwise indistinguishable on it. *)
let rec apply_contained_diff (ch:tree -> bool) (o:op) (t:tree)
  : Lemma (ensures (match apply_contained ch o t with
                    | Error (NotAContainer _ _) -> True
                    | r -> r == apply o t)) (decreases o)
  = match o with
    | InsertChild p n -> if has_id p t then find_in_some_iff p t else ()
    | MoveNode x np ->
      if tid_of t <> x && has_id x t && has_id np t then find_in_some_iff np t else ()
    | Batch os -> apply_contained_all_diff ch os t
    | _ -> ()
and apply_contained_all_diff (ch:tree -> bool) (os:list op) (t:tree)
  : Lemma (ensures (match apply_contained_all ch os t with
                    | Error (NotAContainer _ _) -> True
                    | r -> r == apply_all os t)) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      apply_contained_diff ch o t;
      (match apply_contained ch o t with
       | Ok t' -> apply_contained_all_diff ch r t'
       | Error _ -> ())

(* WHERE THE OFFENDER IS. A `NotAContainer` names a node that really holds children, reports THAT
   node's own kind tag, and the predicate really refuses it — so a caller handed the envelope can
   go to the node it names and find the fault there.

   Since Phase 161 it is a DISJUNCTION, and the disjunction is the content rather than a weakening.
   There are two capability sites and they name nodes in two different places: the (new) PARENT,
   which is a node of `t`; and an interior node of the SUBTREE an `InsertChild` carries, which is a
   node of the caller's own graft and is deliberately NOT in `t` — the duplicate-id scan has
   already refused a graft sharing any id with the tree, so a reader looking the named id up in `t`
   would find nothing. Stating it as one `find_in p t` lookup would therefore have been false of
   the second site, and saying so is the point of the split.

   Stated for the leaf alphabet: inside a `Batch` the offending node lives in an intermediate tree
   rather than in `t`, so the honest statement there is the inheritance above and not this. *)
let not_a_container_locates (ch:tree -> bool) (o:op) (t:tree)
  : Lemma (requires is_leaf o)
          (ensures (match apply_contained ch o t with
                    | Error (NotAContainer p k) ->
                      (* the parent site: a node of the tree *)
                      (mem p (ids t) /\
                       (match find_in p t with
                        | Some pn -> kind_of pn == k /\ not (ch pn)
                        | None -> False))
                      \/
                      (* the graft site (Phase 161): a node of the inserted subtree *)
                      (match o with
                       | InsertChild _ n ->
                         mem p (ids n) /\
                         (match first_uncontained ch n with
                          | Some off -> tid_of off == p /\ kind_of off == k /\
                                        not (ch off) /\ Cons? (kids_of off)
                          | None -> False)
                       | _ -> False)
                      \/
                      (* the rewrite site (Phase 250): the node of the tree an update names, as
                         the update would leave it — the payload's kind over the children it
                         already holds. The kind reported is the NEW one, because that is the
                         kind the predicate refused; the tree's own node still carries the old. *)
                      (match o with
                       | UpdateNode n ->
                         tid_of n == p /\ kind_of n == k /\
                         (match find_in p t with
                          | Some ex -> Cons? (kids_of ex) /\ not (ch (TNode p k (kids_of ex)))
                          | None -> False)
                       | _ -> False)
                    | _ -> True))
  = match o with
    | InsertChild _ n ->
      (match first_uncontained ch n with
       | Some off -> first_uncontained_some ch n off
       | None -> ())
    | _ -> ()

(* THE SIXTH LEMMA, as one statement: `apply` never raises the class (section 1), the
   container-aware engine differs from `apply` only by raising it, and where it raises it there is
   a node the predicate refuses. So `NotAContainer` is the container violation's refusal, it is the
   ONLY refusal a violation earns, and nothing else earns it. *)
let not_a_container_exact (ch:tree -> bool) (o:op) (t:tree)
  : Lemma (ensures (match apply o t with
                    | Error (NotAContainer _ _) -> False
                    | _ -> True) /\
                   (match apply_contained ch o t with
                    | Error (NotAContainer _ _) -> True
                    | r -> r == apply o t) /\
                   (is_leaf o ==>
                     (match apply_contained ch o t with
                      | Error (NotAContainer p k) ->
                        (match find_in p t with
                         | Some pn -> kind_of pn == k /\ not (ch pn)
                         | None -> False)
                        \/
                        (match o with
                         | InsertChild _ n ->
                           (match first_uncontained ch n with
                            | Some off -> tid_of off == p /\ kind_of off == k /\ not (ch off)
                            | None -> False)
                         | _ -> False)
                        \/
                        (match o with
                         | UpdateNode n ->
                           tid_of n == p /\ kind_of n == k /\
                           (match find_in p t with
                            | Some ex -> not (ch (TNode p k (kids_of ex)))
                            | None -> False)
                         | _ -> False)
                      | _ -> True)))
  = apply_never_container_or_domain o t;
    apply_contained_diff ch o t;
    if is_leaf o then not_a_container_locates ch o t else ()

(* ---- 8.4 the machinery the preservation argument needs ---- *)

let rec contained_all_elem (ch:tree -> bool) (ts:list tree) (c:tree)
  : Lemma (requires contained_all ch ts /\ mem c ts) (ensures contained ch c) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> if t = c then () else contained_all_elem ch r c

let rec contained_all_app (ch:tree -> bool) (xs ys:list tree)
  : Lemma (requires contained_all ch xs /\ contained_all ch ys)
          (ensures contained_all ch (app xs ys)) (decreases xs)
  = match xs with
    | [] -> ()
    | _ :: r -> contained_all_app ch r ys

(* A list whose members all come from a contained list is contained. Both the reorder clause (the
   rearranged children are the same children) and the remove clause (a sublist) turn on it. *)
let rec contained_all_of_members (ch:tree -> bool) (l l':list tree)
  : Lemma (requires contained_all ch l /\ (forall (c:tree). mem c l' ==> mem c l))
          (ensures contained_all ch l') (decreases l')
  = match l' with
    | [] -> ()
    | c :: r -> contained_all_elem ch l c; contained_all_of_members ch l r

let rec find_in_contained (ch:tree -> bool) (x:string) (t:tree) (sub:tree)
  : Lemma (requires contained ch t /\ find_in x t == Some sub) (ensures contained ch sub)
          (decreases t)
  = match t with
    | TNode i _ cs -> if i = x then () else find_all_contained ch x cs sub
and find_all_contained (ch:tree -> bool) (x:string) (ts:list tree) (sub:tree)
  : Lemma (requires contained_all ch ts /\ find_all x ts == Some sub) (ensures contained ch sub)
          (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> (match find_in x t with
                 | Some _ -> find_in_contained ch x t sub
                 | None -> find_all_contained ch x r sub)

(* ---- the capability, at every node carrying an id ----

   `find_in` answers with the FIRST node of that id, and `ins` edits EVERY node of that id (the
   `Tree.updateNode` fidelity `TreeOps.fst` section 4 records). Under `wf` those are the same node,
   and this predicate plus the lemma below is where that gap is closed rather than assumed. *)
let rec ch_at (ch:tree -> bool) (p:string) (t:tree) : Tot bool (decreases t) =
  match t with
  | TNode i _ cs -> (if i = p then ch t else true) && ch_at_all ch p cs
and ch_at_all (ch:tree -> bool) (p:string) (ts:list tree) : Tot bool (decreases ts) =
  match ts with
  | [] -> true
  | t :: r -> ch_at ch p t && ch_at_all ch p r

let rec ch_at_absent (ch:tree -> bool) (p:string) (t:tree)
  : Lemma (requires not (mem p (ids t))) (ensures ch_at ch p t) (decreases t)
  = match t with TNode _ _ cs -> ch_at_all_absent ch p cs
and ch_at_all_absent (ch:tree -> bool) (p:string) (ts:list tree)
  : Lemma (requires not (mem p (ids_all ts))) (ensures ch_at_all ch p ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> ch_at_absent ch p t; ch_at_all_absent ch p r

let rec ch_at_of_find (ch:tree -> bool) (p:string) (t:tree) (pn:tree)
  : Lemma (requires wf t /\ find_in p t == Some pn /\ ch pn) (ensures ch_at ch p t) (decreases t)
  = match t with
    | TNode i _ cs -> if i = p then ch_at_all_absent ch p cs
                      else (find_all_some_iff p cs; ch_at_all_of_find ch p cs pn)
and ch_at_all_of_find (ch:tree -> bool) (p:string) (ts:list tree) (pn:tree)
  : Lemma (requires wf_all ts /\ find_all p ts == Some pn /\ ch pn)
          (ensures ch_at_all ch p ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      find_in_some_iff p t;
      if mem p (ids t) then begin
        ch_at_of_find ch p t pn;
        inter_nil_iff (ids t) (ids_all r);
        ch_at_all_absent ch p r
      end
      else (ch_at_absent ch p t; ch_at_all_of_find ch p r pn)

let rec ch_at_drop_kid (ch:tree -> bool) (p:string) (x:string) (ts:list tree)
  : Lemma (requires ch_at_all ch p ts) (ensures ch_at_all ch p (drop_kid x ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | _ :: r -> ch_at_drop_kid ch p x r

(* A remove neither invents an id nor changes a kind, so the capability survives it — but only
   because the predicate does not read the child list, which is where `child_blind` earns its
   place in the move clause. *)
let rec rem_ch_at (ch:tree -> bool) (p:string) (pid x:string) (t:tree)
  : Lemma (requires child_blind ch /\ ch_at ch p t)
          (ensures ch_at ch p (rem_at pid x t)) (decreases t)
  = match t with
    | TNode _ _ cs -> rem_ch_at_all ch p pid x cs; ch_at_drop_kid ch p x (rem_all pid x cs)
and rem_ch_at_all (ch:tree -> bool) (p:string) (pid x:string) (ts:list tree)
  : Lemma (requires child_blind ch /\ ch_at_all ch p ts)
          (ensures ch_at_all ch p (rem_all pid x ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> rem_ch_at ch p pid x t; rem_ch_at_all ch p pid x r

(* ---- the three structural edits, against the invariant ---- *)

(* An insert under a node the capability admits, of a subtree that satisfies the invariant. Both
   hypotheses are used exactly once and section 8.6 refutes the statement without either. *)
let rec ins_contained (ch:tree -> bool) (p:string) (n:tree) (t:tree)
  : Lemma (requires child_blind ch /\ contained ch t /\ contained ch n /\ ch_at ch p t)
          (ensures contained ch (ins p n t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      ins_contained_all ch p n cs;
      if i = p then contained_all_app ch (ins_all p n cs) [n] else ()
and ins_contained_all (ch:tree -> bool) (p:string) (n:tree) (ts:list tree)
  : Lemma (requires child_blind ch /\ contained_all ch ts /\ contained ch n /\ ch_at_all ch p ts)
          (ensures contained_all ch (ins_all p n ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> ins_contained ch p n t; ins_contained_all ch p n r

let rec drop_kid_sub (x:string) (ts:list tree) (c:tree)
  : Lemma (ensures mem c (drop_kid x ts) ==> mem c ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> drop_kid_sub x r c

(* A remove can only take children away, and the invariant constrains nodes that HAVE children —
   so it survives, with `child_blind` carrying the node that lost one. *)
let rec rem_contained (ch:tree -> bool) (pid x:string) (t:tree)
  : Lemma (requires child_blind ch /\ contained ch t)
          (ensures contained ch (rem_at pid x t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      rem_contained_all ch pid x cs;
      let cs' = rem_all pid x cs in
      let aux (c:tree) : Lemma (mem c (drop_kid x cs') ==> mem c cs') = drop_kid_sub x cs' c in
      FStar.Classical.forall_intro aux;
      contained_all_of_members ch cs' (drop_kid x cs')
and rem_contained_all (ch:tree -> bool) (pid x:string) (ts:list tree)
  : Lemma (requires child_blind ch /\ contained_all ch ts)
          (ensures contained_all ch (rem_all pid x ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> rem_contained ch pid x t; rem_contained_all ch pid x r

let rec arrange_nil (order:list string) : Lemma (ensures arrange order [] == []) (decreases order)
  = match order with
    | [] -> ()
    | _ :: rest -> arrange_nil rest

(* A reorder invents no child and gives no childless node children, so the invariant survives it
   with no capability check at all — which is why `Ops.fs` does not make one. *)
let rec reorder_contained (ch:tree -> bool) (p:string) (ord:list string) (t:tree)
  : Lemma (requires child_blind ch /\ contained ch t)
          (ensures contained ch (reorder_at p ord t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      reorder_contained_all ch p ord cs;
      let cs' = reorder_all p ord cs in
      arrange_nil ord;
      let aux (c:tree) : Lemma (mem c (arrange ord cs') ==> mem c cs') =
        if mem c (arrange ord cs') then arrange_from ord cs' c else ()
      in
      FStar.Classical.forall_intro aux;
      contained_all_of_members ch cs' (arrange ord cs')
and reorder_contained_all (ch:tree -> bool) (p:string) (ord:list string) (ts:list tree)
  : Lemma (requires child_blind ch /\ contained_all ch ts)
          (ensures contained_all ch (reorder_all p ord ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> reorder_contained ch p ord t; reorder_contained_all ch p ord r

(* An in-place rewrite of the node `find_in` answers with keeps the invariant when that node, as
   rewritten, satisfies it — every other node is unchanged, and its children are unchanged too, which
   is where well-formedness enters: the id is carried once, so no child is also rewritten. The
   ancestors' capability survives through `child_blind`, since their child lists now hold the
   rewritten node. (Phase 250.) *)
let rec upd_contained (ch:tree -> bool) (x k:string) (t:tree) (ex:tree)
  : Lemma (requires child_blind ch /\ wf t /\ contained ch t /\ find_in x t == Some ex /\
                    (Nil? (kids_of ex) \/ ch (TNode x k (kids_of ex))))
          (ensures contained ch (upd x k t)) (decreases t)
  = match t with
    | TNode i ki cs ->
      if i = x then upd_absent_all x k cs
      else begin
        upd_contained_all ch x k cs ex;
        match cs with
        | [] -> ()
        | _ :: _ -> assert (ch (TNode i ki (upd_all x k cs)) == ch (TNode i ki cs))
      end
and upd_contained_all (ch:tree -> bool) (x k:string) (ts:list tree) (ex:tree)
  : Lemma (requires child_blind ch /\ wf_all ts /\ contained_all ch ts /\ find_all x ts == Some ex /\
                    (Nil? (kids_of ex) \/ ch (TNode x k (kids_of ex))))
          (ensures contained_all ch (upd_all x k ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      find_in_some_iff x t;
      (match find_in x t with
       | Some _ ->
         upd_contained ch x k t ex;
         inter_nil_iff (ids t) (ids_all r);
         upd_absent_all x k r
       | None -> upd_absent x k t; upd_contained_all ch x k r ex)

(* ---- 8.5 THE SEVENTH LEMMA — the container capability is preserved ---- *)

#push-options "--z3rlimit 120"
let rec contained_preserves (ch:tree -> bool) (o:op) (t:tree)
  : Lemma (requires child_blind ch /\ wf t /\ contained ch t)
          (ensures (match apply_contained ch o t with
                    | Ok t' -> contained ch t'
                    | Error _ -> True)) (decreases o)
  = match o with

    (* Phase 161. The `contained_op` hypothesis used to sit in the `requires` above and its whole
       work was here: `ins_contained` needs `contained ch n`, and nothing in the engine established
       it. The graft walk establishes it now — an accepted insert is one whose `first_uncontained`
       answered `None`, and `first_uncontained_none` turns that branch into the fact. So the
       premise is discharged by the code rather than carried by the theorem, which is what the
       operator's ruling bought (DECISIONS D38). *)
    | InsertChild p n ->
      (match first_dup n t with
       | Some _ -> ()
       | None ->
         if not (has_id p t) then ()
         else begin
           find_in_some_iff p t;
           match find_in p t with
           | None -> ()
           | Some pn ->
             if ch pn then
               (match first_uncontained ch n with
                | Some _ -> ()
                | None ->
                  first_uncontained_none ch n;
                  ch_at_of_find ch p t pn;
                  ins_contained ch p n t)
             else ()
         end)

    | RemoveNode x ->
      if tid_of t = x || not (has_id x t) then ()
      else (match parent_of x t with
            | None -> ()
            | Some pid -> rem_contained ch pid x t)

    | ReorderChildren p order ->
      (match find_in p t with
       | None -> ()
       | Some _ -> reorder_contained ch p order t)

    (* The one clause with real content. The moved subtree satisfies the invariant because it is a
       subtree of a tree that does; the removal preserves both the invariant and the capability at
       the destination (which is a DIFFERENT node from the one removed, and survives — `child_blind`
       is what lets the pre-removal check answer for the post-removal node); and the graft is then
       an insert under an admitted parent.

       AND THIS CLAUSE IS WHY `MoveNode` DOES NOT WALK (DECISIONS D38). `find_in_contained` derives
       the moved subtree's containment from the TREE's, with no hypothesis about the operation at
       all — a move relocates structure that is already there, so it introduces no interior the
       tree did not already hold, and a walk here would refuse an operation for a violation some
       earlier insert carried in. The walk belongs at `InsertChild`, where new structure enters,
       and that is exactly where the shipped engine puts it. *)
    | MoveNode x np ->
      if tid_of t = x || not (has_id x t) || not (has_id np t) then ()
      else begin
        find_in_some_iff np t;
        match find_in np t with
        | None -> ()
        | Some np0 ->
          if not (ch np0) then ()
          else (match find_in x t with
                | None -> ()
                | Some sub ->
                  if mem np (ids sub) then ()
                  else (match parent_of x t with
                        | None -> ()
                        | Some pid ->
                          let removed = rem_at pid x t in
                          if not (has_id np removed) then ()
                          else begin
                            find_in_contained ch x t sub;
                            rem_contained ch pid x t;
                            ch_at_of_find ch np t np0;
                            rem_ch_at ch np pid x t;
                            ins_contained ch np sub removed
                          end))
      end

    | Batch os -> contained_preserves_all ch os t

    | UpdateNode n ->
      (match find_in (tid_of n) t with
       | None -> ()
       | Some ex ->
         find_in_id (tid_of n) t ex;
         if Cons? (kids_of ex) && not (ch (TNode (tid_of n) (kind_of n) (kids_of ex))) then ()
         else upd_contained ch (tid_of n) (kind_of n) t ex)

and contained_preserves_all (ch:tree -> bool) (os:list op) (t:tree)
  : Lemma (requires child_blind ch /\ wf t /\ contained ch t)
          (ensures (match apply_contained_all ch os t with
                    | Ok t' -> contained ch t'
                    | Error _ -> True)) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      contained_preserves ch o t;
      (* well-formedness travels with the tree through the script, and it travels through the
         PLAIN engine's theorem: the two agree on every accepted step (`apply_contained_diff`). *)
      apply_contained_diff ch o t;
      apply_preserves_wf o t;
      (match apply_contained ch o t with
       | Ok t' -> contained_preserves_all ch r t'
       | Error _ -> ())
#pop-options

(* The dry run and the container-aware mutating call accept exactly the same operations, and
   refuse with the same envelope — F#'s `canApplyContained` is `canApplyWith canHold`, sharing the
   three validators with `applyWith` and simulating the two order-dependent clauses. Section 5's
   corollary carries over unchanged: the `parentOf` fallback the remove clause carries is
   unreachable, so the two surfaces cannot diverge by one taking a branch the other does not. *)
let can_apply_contained_agrees (ch:tree -> bool) (o:op) (t:tree)
  : Lemma (requires wf t)
          (ensures can_apply_contained ch o t ==
                   (match apply_contained ch o t with Ok _ -> Ok () | Error e -> Error e))
  = match o with
    | RemoveNode x -> if tid_of t <> x && has_id x t then parent_exists x t else ()
    | _ -> ()

(* ---- 8.6 the counterexamples ----

   Each is a concrete tree and a concrete predicate, decided by `assert_norm`, in the shape
   `TreeOps.insert_breaks_wf_pre137` established: a refutation that the module can evaluate rather
   than a caveat a reader has to believe. The first two are Phase 161's pair — the premise that WAS
   needed, restated against the engine that needed it beside the refusal that replaced it, and then
   the graft that is still admitted, which is what stops the refusal from being over-eager. The
   third is why section 8.5 still has `child_blind`. The fourth is the go-red instrument's teeth;
   the fifth is a finding about the shipped surface, not about the model. *)

(* "everything but a paragraph can hold children", the shape every domain writes. *)
let cx_doc_only (n:tree) : Tot bool = kind_of n = "doc"

(* the predicate that reads the CHILD LIST — lawful by the engine's type, and unstable *)
let cx_childless (n:tree) : Tot bool = match kids_of n with [] -> true | _ -> false

let cx_root_doc : tree = TNode "root" "doc" []

(* a graft whose OWN interior node is not a container: the engine never looks inside it *)
let cx_nested_graft : op = InsertChild "root" (TNode "a" "para" [TNode "b" "para" []])

(* a graft that is itself contained — so this one isolates the child-blindness premise *)
let cx_leaf_graft : op = InsertChild "root" (TNode "a" "para" [])

(* THE PREMISE THAT IS NOW HISTORY (Phase 161), and both halves are stated in one lemma so the
   second cannot be read without the first.

   Before this phase the theorem above carried `contained_op` — "the operation's own trees satisfy
   the invariant" — because `canHold` was applied to the PARENT and to nothing inside the subtree,
   so a graft whose own interior node was a non-container carried the violation in. The FIRST half
   below is that refutation, restated against `apply_contained_pre161` so it stays evaluable: the
   engine as it stood admits `cx_nested_graft` and the invariant breaks.

   The SECOND half is what the operator's ruling bought (DECISIONS D38): the shipped engine refuses
   the same operation, naming "a" — the graft's own root, which holds "b" while the predicate
   refuses it — and not "root", which is the parent and is perfectly able to hold children. That
   is the disjunction in `not_a_container_locates` at a concrete pair.

   Read the pair as the reason `contained_preserves` no longer has the hypothesis: not that the
   sentence became true of a cleaner function, but that the function changed and this is the
   difference, machine-checked. If the walk is ever removed, the second half goes red. *)
let nested_graft_refused ()
  : Lemma (ensures contained cx_doc_only cx_root_doc /\
                   not (contained_op cx_doc_only cx_nested_graft) /\
                   (match apply_contained_pre161 cx_doc_only cx_nested_graft cx_root_doc with
                    | Ok t' -> not (contained cx_doc_only t')
                    | Error _ -> False) /\
                   apply_contained cx_doc_only cx_nested_graft cx_root_doc ==
                     Error (NotAContainer "a" "para") /\
                   can_apply_contained cx_doc_only cx_nested_graft cx_root_doc ==
                     Error (NotAContainer "a" "para"))
  = assert_norm (contained cx_doc_only cx_root_doc);
    assert_norm (not (contained_op cx_doc_only cx_nested_graft));
    assert_norm (match apply_contained_pre161 cx_doc_only cx_nested_graft cx_root_doc with
                 | Ok t' -> not (contained cx_doc_only t')
                 | Error _ -> False);
    assert_norm (apply_contained cx_doc_only cx_nested_graft cx_root_doc ==
                 Error (NotAContainer "a" "para"));
    assert_norm (can_apply_contained cx_doc_only cx_nested_graft cx_root_doc ==
                 Error (NotAContainer "a" "para"))

(* AND THE WALK IS NOT OVER-EAGER. A graft that is itself contained still goes in — the clause
   refuses a subtree that PLACES CHILDREN under a node the predicate refuses, never a childless
   node the predicate refuses, because the invariant constrains nodes that have children. Without
   this half the first would be satisfied by an engine that refused every insert. *)
let contained_graft_still_admitted ()
  : Lemma (ensures contained_op cx_doc_only cx_leaf_graft /\
                   apply_contained cx_doc_only cx_leaf_graft cx_root_doc ==
                     Ok (TNode "root" "doc" [TNode "a" "para" []]))
  = assert_norm (contained_op cx_doc_only cx_leaf_graft);
    assert_norm (apply_contained cx_doc_only cx_leaf_graft cx_root_doc ==
                 Ok (TNode "root" "doc" [TNode "a" "para" []]))

(* WITHOUT `child_blind`: every other hypothesis holds — the tree is contained, the graft is
   contained, the parent is admitted at the moment it is checked — and the invariant breaks anyway,
   because the check was answered by a node that no longer exists in that shape. *)
let contained_needs_child_blind ()
  : Lemma (ensures contained cx_childless cx_root_doc /\
                   contained_op cx_childless cx_leaf_graft /\
                   ~(child_blind cx_childless) /\
                   (match apply_contained cx_childless cx_leaf_graft cx_root_doc with
                    | Ok t' -> not (contained cx_childless t')
                    | Error _ -> False))
  = assert_norm (contained cx_childless cx_root_doc);
    assert_norm (contained_op cx_childless cx_leaf_graft);
    assert_norm (cx_childless (TNode "r" "k" []) == true);
    assert_norm (cx_childless (TNode "r" "k" [TNode "c" "k" []]) == false);
    assert_norm (match apply_contained cx_childless cx_leaf_graft cx_root_doc with
                 | Ok t' -> not (contained cx_childless t')
                 | Error _ -> False)

(* ---- the move half, which is what the differential's go-red is about ---- *)

let cx_box (n:tree) : Tot bool = kind_of n = "box"

(* root(box) [ leaf(para), x(para) ] — a leaf and a movable node as siblings *)
let cx_box_tree : tree = TNode "root" "box" [TNode "leaf" "para" []; TNode "x" "para" []]

let cx_move_into_leaf : op = MoveNode "x" "leaf"

(* THE GO-RED'S TEETH. An engine that checks the capability on insert and not on move admits a
   move under a leaf, and the invariant breaks — while the real one refuses it, naming the leaf
   and its kind tag. Both halves in one statement, so the instrument is proved to be a real
   weakening before the differential is asked to lose against it. *)
let insert_only_breaks_contained ()
  : Lemma (ensures contained cx_box cx_box_tree /\
                   apply_contained cx_box cx_move_into_leaf cx_box_tree ==
                     Error (NotAContainer "leaf" "para") /\
                   (match apply_contained_insert_only cx_box cx_move_into_leaf cx_box_tree with
                    | Ok t' -> not (contained cx_box t')
                    | Error _ -> False))
  = assert_norm (contained cx_box cx_box_tree);
    assert_norm (apply_contained cx_box cx_move_into_leaf cx_box_tree ==
                 Error (NotAContainer "leaf" "para"));
    assert_norm (match apply_contained_insert_only cx_box cx_move_into_leaf cx_box_tree with
                 | Ok t' -> not (contained cx_box t')
                 | Error _ -> False)

(* THE FINDING, and it is about the shipped code rather than about this model. `Ops.canApplyAll`
   is the dry run for a SCRIPT, and it threads the plain `apply`: there is no container-aware
   sequence surface at all, so a script it certifies can be refused by `applyContained` at the
   first step. A caller that pre-flights with `canApplyAll` and then executes with
   `applyContained` — which is the combination the two doc comments invite — has a check that
   cannot see the refusal its executor will make. `Ops.applyAll` has the same shape. *)
let can_apply_all_ignores_containment ()
  : Lemma (ensures can_apply_all [cx_move_into_leaf] cx_box_tree == Ok () /\
                   (match apply_contained cx_box (Batch [cx_move_into_leaf]) cx_box_tree with
                    | Error (NotAContainer p k) -> p == "leaf" /\ k == "para"
                    | _ -> False))
  = assert_norm (can_apply_all [cx_move_into_leaf] cx_box_tree == Ok ());
    assert_norm (match apply_contained cx_box (Batch [cx_move_into_leaf]) cx_box_tree with
                 | Error (NotAContainer p k) -> p == "leaf" /\ k == "para"
                 | _ -> False)

(* ======================================================================================
   9. THE SEQUENCE SURFACE — `Ops.applyAllWith` / `Ops.canApplyAllWith` (Phase 160).

      Section 8's last lemma is a FINDING: `Ops.applyAll` and `Ops.canApplyAll` thread the plain
      `apply`, so there was no container-aware sequence surface at all and a script the dry run
      certified could be refused by `applyContained` at its first step. This section is the
      surface that closes it, modelled clause for clause, and the three statements that say the
      closure is real and that it cost no existing caller anything.

      WHAT A SCRIPT IS, AND IS NOT. A `Batch` is all-or-nothing INSIDE one operation — section
      8.2's `apply_contained_all` abandons the whole on the first refusal, and `apply_contained`
      returns that refusal, leaving the caller's original tree. A SCRIPT is not: it stops at the
      first refusal and hands back the tree the accepted prefix reached. Structurally the two
      folds are the same walk; they differ in what the failure CARRIES, and that difference is
      the whole of the semantics. So the failure payload is modelled rather than dropped — the
      index and the partial tree, exactly the `(int * Rejection * 'Node)` the F# returns — where
      section 8's `can_apply_all` dropped the index because nothing there turned on it. Here
      everything does: the index is what tells a caller WHICH step was refused, and the partial
      tree is what it is left holding.

      AND THAT PARTIAL TREE IS WHY THE PRESERVATION STATEMENT IS STRONGER HERE THAN PER OP. A
      per-op theorem may say nothing about a refusal, because a refused `applyContained` hands
      back nothing at all. A refused script hands back a tree the caller keeps, so a preservation
      theorem that spoke only of the accepted case would be silent about precisely the state a
      non-atomic surface exists to produce. `contained_preserves_all_with` covers BOTH arms.

      THE PLAIN PAIR DOES NOT MOVE, and it is proved rather than asserted. `Ops.applyAll` and
      `Ops.canApplyAll` are restated as the `fun _ -> true` instances of the new functions — the
      same relation `Ops.apply` has to `applyWith`, now at the sequence level — and
      `all_with_at_total_is_plain` is that restatement discharged against section 8's
      `can_apply_all` and `TreeOps.apply_all`: at the total predicate the new fold reaches the
      same verdict, the same tree and the same envelope as the fold that shipped before this
      phase. Every existing caller is therefore where it was, which is the one thing an additive
      API change has to earn.

      NAMING. `contained_preserves_all_with` is NOT a variant spelling of 8.5's
      `contained_preserves_all`, which is that theorem's mutual companion over a `Batch`'s member
      list (`apply_contained_all`). The theorem names here mirror the function names they are
      about, as they do throughout: `apply_all_with` takes `contained_preserves_all_with`, exactly
      as `apply_contained_all` takes `contained_preserves_all`.
   ====================================================================================== *)

(* ---- 9.1 the two sequence functions, clause for clause ---- *)

(* F#: `Ops.applyAllWith canHold w idw`. The accumulator `i` is the F#'s own — 0-based, counting
   steps OFFERED, so the reported index is the position of the refused operation rather than the
   number that succeeded. The tree in the failure arm is `t`, the state the refused step was
   offered against, which is the tree the accepted prefix reached. *)
let rec apply_all_with (ch:tree -> bool) (i:nat) (os:list op) (t:tree)
  : Tot (outcome tree (nat & rejection & tree)) (decreases os) =
  match os with
  | [] -> Ok t
  | o :: r -> (match apply_contained ch o t with
               | Ok t' -> apply_all_with ch (i + 1) r t'
               | Error e -> Error (i, e, t))

(* F#: `Ops.canApplyAllWith canHold w idw` — the same walk, discarding the materialised tree. Note
   it threads the MUTATING per-op call, as `Ops.canApplyAll` has since Phase 246: a sequence is
   order-dependent, so each step's check must see the prior step's tree and there is nothing to
   gain by checking without building. *)
let rec can_apply_all_with (ch:tree -> bool) (i:nat) (os:list op) (t:tree)
  : Tot (outcome unit (nat & rejection)) (decreases os) =
  match os with
  | [] -> Ok ()
  | o :: r -> (match apply_contained ch o t with
               | Ok t' -> can_apply_all_with ch (i + 1) r t'
               | Error e -> Error (i, e))

(* ---- 9.2 THE EIGHTH LEMMA — the invariant survives a script, refusal included ---- *)

#push-options "--z3rlimit 120"
(* Phase 161 removed `contained_op_all` from the `requires` here for the same reason it removed
   `contained_op` from 8.5: the engine inspects a graft now, so an accepted step's own tree
   satisfies the invariant by the step having been accepted. The sequence lemma inherits that
   through `contained_preserves`, which is the only place the premise was ever used. *)
let rec contained_preserves_all_with (ch:tree -> bool) (i:nat) (os:list op) (t:tree)
  : Lemma (requires child_blind ch /\ wf t /\ contained ch t)
          (ensures (match apply_all_with ch i os t with
                    | Ok t' -> contained ch t'
                    | Error (_, _, t') -> contained ch t')) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      contained_preserves ch o t;
      (* well-formedness travels with the tree through the script, and it travels through the
         PLAIN engine's theorem: the two agree on every accepted step (`apply_contained_diff`).
         The same step 8.5's batch companion takes, for the same reason. *)
      apply_contained_diff ch o t;
      apply_preserves_wf o t;
      (match apply_contained ch o t with
       | Ok t' -> contained_preserves_all_with ch (i + 1) r t'
       | Error _ -> ())
#pop-options

(* ---- 9.3 the dry run agrees with the mutating call, index and envelope included ----

   Note what this needs and 8.5's `can_apply_contained_agrees` did: nothing. That lemma needed
   `wf t` because the per-op dry run reaches the three validators directly and has to be shown not
   to take a branch the mutating call does not. The sequence pair cannot diverge that way by
   construction — both fold the SAME per-op call — so the agreement is structural, and saying so
   is the honest reading of why the shipped `canApplyAll` threads `apply` rather than `canApply`. *)
let rec can_apply_all_with_agrees (ch:tree -> bool) (i:nat) (os:list op) (t:tree)
  : Lemma (ensures can_apply_all_with ch i os t ==
                   (match apply_all_with ch i os t with
                    | Ok _ -> Ok ()
                    | Error (j, e, _) -> Error (j, e))) (decreases os)
  = match os with
    | [] -> ()
    | o :: r -> (match apply_contained ch o t with
                 | Ok t' -> can_apply_all_with_agrees ch (i + 1) r t'
                 | Error _ -> ())

(* ---- 9.4 and the plain pair IS the total instance ----

   `Ops.applyAll` is `applyAllWith (fun _ -> true)` and `Ops.canApplyAll` is
   `canApplyAllWith (fun _ -> true)`, so no caller of either moved when the capability arrived.
   Discharged against `TreeOps.apply_all` and section 8's `can_apply_all` — the two folds as they
   stood BEFORE this phase — so what is proved is agreement with the shipped behaviour rather than
   agreement with a restatement of itself. The index and the partial tree are the new surface's
   own and have no counterpart on the old side; everything the old side reports, the new one
   reports identically. *)
let rec all_with_at_total_is_plain (i:nat) (os:list op) (t:tree)
  : Lemma (ensures (match apply_all_with (fun _ -> true) i os t with
                    | Ok t' -> apply_all os t == Ok t'
                    | Error (_, e, _) -> apply_all os t == Error e) /\
                   (match can_apply_all_with (fun _ -> true) i os t with
                    | Ok () -> can_apply_all os t == Ok ()
                    | Error (_, e) -> can_apply_all os t == Error e)) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      apply_contained_is_apply o t;
      (match apply o t with
       | Ok t' -> all_with_at_total_is_plain (i + 1) r t'
       | Error _ -> ())

(* ---- 9.5 the gap, closed — the counterexample section 8.6 recorded, answered ----

   `can_apply_all_ignores_containment` above is KEPT, and it is worth being exact about why,
   because the obvious reading is that this phase should have retired it. It is still TRUE, and
   after this phase it is no longer only a finding: it is the pin that the plain pair's behaviour
   did NOT move. What 160 closes is the absence of ANY container-aware sequence surface, not the
   plain pair's blindness — the plain pair is blind BY CONSTRUCTION, being the `fun _ -> true`
   instance, and a phase that changed that would have broken every existing caller.

   So the two lemmas are read together: the same script, at the same tree. The plain dry run
   certifies it and always will; the container-aware one refuses it at index 0 with the envelope
   `applyAllWith` will raise, naming the leaf and its kind tag; and the mutating call hands back
   the tree untouched, because the refusal is at the first step and the accepted prefix is
   empty. *)
let can_apply_all_with_sees_containment ()
  : Lemma (ensures can_apply_all [cx_move_into_leaf] cx_box_tree == Ok () /\
                   can_apply_all_with cx_box 0 [cx_move_into_leaf] cx_box_tree ==
                     Error (0, NotAContainer "leaf" "para") /\
                   apply_all_with cx_box 0 [cx_move_into_leaf] cx_box_tree ==
                     Error (0, NotAContainer "leaf" "para", cx_box_tree))
  = assert_norm (can_apply_all [cx_move_into_leaf] cx_box_tree == Ok ());
    assert_norm (can_apply_all_with cx_box 0 [cx_move_into_leaf] cx_box_tree ==
                 Error (0, NotAContainer "leaf" "para"));
    assert_norm (apply_all_with cx_box 0 [cx_move_into_leaf] cx_box_tree ==
                 Error (0, NotAContainer "leaf" "para", cx_box_tree))


(* ======================================================================================
   10. THE PREORDER-POSITION LEMMA UNDER A REMOVE, AND UNDER ANY ACCEPTED OPERATION
       (Phase 162).

       `TreeOps` section 19 proves `preorder_parent_first`: on a well-formed tree, a parent
       precedes each of its children in `Tree.preorder`. It states the corollary for `ins` and for
       `reorder_at` there, off `ins_wf` and `reorder_wf`, and leaves the `rem_at` corollary here
       because `rem_wf` is this module's (section 3) and `TreeOps` cannot cite it — this module
       OPENS that one.

       WHAT "PRESERVATION UNDER A REMOVE" MEANS, since the naive reading is not a statement. A
       remove takes ids AWAY: the positions of a removed subtree do not move, they VANISH, so
       there is nothing to preserve about them, and a lemma quantifying over the ORIGINAL tree's
       nodes would be false on exactly the nodes the operation exists to destroy. The statement is
       therefore over the SURVIVORS — the tree the remove hands back — and it says that whatever
       parent relation survives is still respected by the walk. That is the shape a reconstruction
       argument wants, because what it asks about a removal is precisely what is still there.

       The general form is the one to cite. `apply_preserves_wf` (section 4) covers all five
       clauses including a nested `Batch`, so the corollary over any ACCEPTED operation is one
       line and subsumes the three per-edit ones. It is stated second, and it is the one a
       reconstruction theorem consumes.
   ====================================================================================== *)

(* ---- the remove, in its survivor form ---- *)

let rem_preserves_parent_first (pid x:string) (t:tree) (y q:string)
  : Lemma (requires wf t /\ parent_of y (rem_at pid x t) == Some q)
          (ensures precedes q y (ids (rem_at pid x t)))
  = rem_wf pid x t; preorder_parent_first (rem_at pid x t) y q

(* ---- and the general form: ANY accepted operation, including a batch ---- *)

let apply_preserves_parent_first (o:op) (t:tree) (t':tree) (y q:string)
  : Lemma (requires wf t /\ apply o t == Ok t' /\ parent_of y t' == Some q)
          (ensures precedes q y (ids t'))
  = apply_preserves_wf o t; preorder_parent_first t' y q

(* ======================================================================================
   11. THE INTERMEDIATE-TREE LEMMAS THE DIFF'S RECONSTRUCTION INDUCTION NEEDS (Phase 167).

       Theorem 6's boundary was never a missing FACT about `after` — Phase 162 proved every
       positional fact it named. It was that `apply` validates each step against the tree IN
       HAND, the before-tree with the script's earlier operations already run on it, and nothing
       here described those intermediate trees. That is what this section is: a per-node view of
       a tree (`kids_at` / `kind_at`), and what `ins` and `rem_at` do to it — one lemma per
       operation, per node, so an induction over a script prefix can carry an invariant about
       how much of `after` it has built and discharge each step locally.

       It sits in THIS module and not in `TreeDiff.fst` for the reason the README predicted
       before it was taken: these are inductions over `ins` and `rem_at` over trees, which is
       this module's cost class and not the diff's. `TreeDiff.fst` section 10 opens this module
       and uses them; nothing here mentions the diff.
   ====================================================================================== *)
(* ---- the per-node view ---- *)

let kids_at (q:string) (t:tree) : Tot (list string) =
  match find_in q t with
  | Some n -> kid_ids (kids_of n)
  | None -> []

let kind_at (q:string) (t:tree) : Tot (option string) =
  match find_in q t with
  | Some n -> Some (kind_of n)
  | None -> None

let rec drop_id (x:string) (l:list string) : Tot (list string) =
  match l with
  | [] -> []
  | h :: r -> if h = x then drop_id x r else h :: drop_id x r

let rec drop_id_absent (x:string) (l:list string)
  : Lemma (requires not (mem x l)) (ensures drop_id x l == l) (decreases l)
  = match l with
    | [] -> ()
    | _ :: r -> drop_id_absent x r

let rec drop_id_mem (y x:string) (l:list string)
  : Lemma (ensures mem y (drop_id x l) == (mem y l && y <> x)) (decreases l)
  = match l with
    | [] -> ()
    | _ :: r -> drop_id_mem y x r

let rec kid_ids_drop (x:string) (ts:list tree)
  : Lemma (ensures kid_ids (drop_kid x ts) == drop_id x (kid_ids ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | _ :: r -> kid_ids_drop x r

let rec kid_ids_rem_all (pid x:string) (ts:list tree)
  : Lemma (ensures kid_ids (rem_all pid x ts) == kid_ids ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> tid_rem pid x t; kid_ids_rem_all pid x r

(* ---- a lookup through a remove, for a node that SURVIVES it ---- *)

let rec find_all_drop (q x:string) (l:list tree)
  : Lemma (requires wf_all l /\ mem q (ids_all (drop_kid x l)))
          (ensures find_all q (drop_kid x l) == find_all q l) (decreases l)
  = match l with
    | [] -> ()
    | d :: r ->
      inter_nil_iff (ids d) (ids_all r);
      find_in_some_iff q d;
      if tid_of d = x then begin
        ids_drop_kid_sub q x r;
        find_all_drop q x r
      end
      else begin
        mem_app q (ids d) (ids_all (drop_kid x r));
        if mem q (ids d) then ()
        else find_all_drop q x r
      end

let rec find_rem (q pid x:string) (t:tree)
  : Lemma (requires wf t /\ mem q (ids (rem_at pid x t)))
          (ensures (match find_in q t with
                    | Some m -> find_in q (rem_at pid x t) == Some (rem_at pid x m)
                    | None -> False))
          (decreases t)
  = match t with
    | TNode i k cs ->
      if i = q then ()
      else begin
        rem_wf_all pid x cs;
        if i = pid then begin
          ids_drop_kid_sub q x (rem_all pid x cs);
          find_all_drop q x (rem_all pid x cs);
          find_rem_all q pid x cs
        end
        else find_rem_all q pid x cs
      end
and find_rem_all (q pid x:string) (ts:list tree)
  : Lemma (requires wf_all ts /\ mem q (ids_all (rem_all pid x ts)))
          (ensures (match find_all q ts with
                    | Some m -> find_all q (rem_all pid x ts) == Some (rem_at pid x m)
                    | None -> False))
          (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r ->
      mem_app q (ids (rem_at pid x c)) (ids_all (rem_all pid x r));
      inter_nil_iff (ids c) (ids_all r);
      find_in_some_iff q c;
      find_in_some_iff q (rem_at pid x c);
      if mem q (ids (rem_at pid x c)) then find_rem q pid x c
      else begin
        ids_rem_all_sub q pid x r;
        ids_rem_sub q pid x c;
        find_rem_all q pid x r
      end

(* ---- which nodes survive a remove: everything outside the removed subtree ---- *)

let rec drop_keeps_ids (q x:string) (cs:list tree) (sub:tree)
  : Lemma (requires wf_all cs /\ mem sub cs /\ tid_of sub == x /\
                    mem q (ids_all cs) /\ not (mem q (ids sub)))
          (ensures mem q (ids_all (drop_kid x cs))) (decreases cs)
  = match cs with
    | [] -> ()
    | d :: r ->
      inter_nil_iff (ids d) (ids_all r);
      mem_app q (ids d) (ids_all r);
      if tid_of d = x then begin
        (if d = sub then () else mem_ids_all_intro x sub r);
        (if mem x (kid_ids r) then kid_ids_sub x r else ());
        drop_kid_none x r
      end
      else begin
        mem_app q (ids d) (ids_all (drop_kid x r));
        if mem q (ids d) then () else drop_keeps_ids q x r sub
      end

let rec rem_survives (q pid x:string) (t:tree) (sub:tree)
  : Lemma (requires wf t /\ parent_of x t == Some pid /\ find_in x t == Some sub /\
                    mem q (ids t) /\ not (mem q (ids sub)))
          (ensures mem q (ids (rem_at pid x t))) (decreases t)
  = match t with
    | TNode i k cs ->
      if q = i then ()
      else begin
        parent_of_mem x t pid;
        if has_kid x cs then begin
          rem_all_absent i x cs;
          has_kid_mem x cs;
          kid_ids_sub x cs;
          find_in_kid_is_sub x cs sub;
          drop_keeps_ids q x cs sub
        end
        else begin
          parent_all_mem x cs pid;
          rem_survives_all q pid x cs sub
        end
      end
and rem_survives_all (q pid x:string) (ts:list tree) (sub:tree)
  : Lemma (requires wf_all ts /\ parent_all x ts == Some pid /\ find_all x ts == Some sub /\
                    mem q (ids_all ts) /\ not (mem q (ids sub)))
          (ensures mem q (ids_all (rem_all pid x ts))) (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r ->
      inter_nil_iff (ids c) (ids_all r);
      mem_app q (ids c) (ids_all r);
      mem_app q (ids (rem_at pid x c)) (ids_all (rem_all pid x r));
      find_in_some_iff x c;
      (match parent_of x c with
       | Some p ->
         parent_of_mem x c p;
         if mem q (ids c) then rem_survives q pid x c sub
         else rem_all_absent pid x r
       | None ->
         parent_all_mem x r pid;
         (if mem x (ids c) then (if tid_of c = x then () else parent_exists x c) else ());
         if mem q (ids c) then rem_absent pid x c
         else rem_survives_all q pid x r sub)

(* ---- a lookup INSIDE a grafted subtree, and inside a found one ---- *)

let rec find_ins_inside (q p:string) (n:tree) (t:tree)
  : Lemma (requires wf t /\ mem q (ids n) /\ not (mem q (ids t)) /\ mem p (ids t))
          (ensures find_in q (ins p n t) == find_in q n) (decreases t)
  = match t with
    | TNode i k cs ->
      if i = p then begin
        ins_all_absent p n cs;
        find_all_app q cs [n];
        find_all_some_iff q cs
      end
      else find_ins_inside_all q p n cs
and find_ins_inside_all (q p:string) (n:tree) (ts:list tree)
  : Lemma (requires wf_all ts /\ mem q (ids n) /\ not (mem q (ids_all ts)) /\ mem p (ids_all ts))
          (ensures find_all q (ins_all p n ts) == find_in q n) (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r ->
      mem_app q (ids c) (ids_all r);
      mem_app p (ids c) (ids_all r);
      find_in_some_iff q n;
      find_in_some_iff q c;
      if mem p (ids c) then find_ins_inside q p n c
      else begin
        ins_absent p n c;
        find_ins_inside_all q p n r
      end

let rec find_in_trans (x q:string) (t:tree) (sub:tree)
  : Lemma (requires wf t /\ find_in x t == Some sub /\ mem q (ids sub))
          (ensures find_in q t == find_in q sub) (decreases t)
  = match t with
    | TNode i k cs ->
      if i = x then ()
      else begin
        find_all_sub x q cs sub;
        find_all_trans x q cs sub
      end
and find_all_trans (x q:string) (ts:list tree) (sub:tree)
  : Lemma (requires wf_all ts /\ find_all x ts == Some sub /\ mem q (ids sub))
          (ensures find_all q ts == find_in q sub) (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r ->
      inter_nil_iff (ids c) (ids_all r);
      find_in_some_iff q sub;
      find_in_some_iff q c;
      (match find_in x c with
       | Some m -> find_in_trans x q c sub
       | None -> find_all_sub x q r sub; find_all_trans x q r sub)

(* ---- a node's child is parented by it ---- *)

let rec parent_kids (p c:string) (t:tree) (n:tree)
  : Lemma (requires wf t /\ find_in p t == Some n /\ mem c (kid_ids (kids_of n)))
          (ensures parent_of c t == Some p) (decreases t)
  = match t with
    | TNode i k cs ->
      if i = p then has_kid_is_mem c cs
      else parent_kids_all p c cs n
and parent_kids_all (p c:string) (ts:list tree) (n:tree)
  : Lemma (requires wf_all ts /\ find_all p ts == Some n /\ mem c (kid_ids (kids_of n)))
          (ensures parent_all c ts == Some p /\ not (has_kid c ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | d :: r ->
      inter_nil_iff (ids d) (ids_all r);
      (if has_kid c r then (has_kid_mem c r; kid_ids_sub c r) else ());
      (match find_in p d with
       | Some m ->
         parent_kids p c d n;
         parent_of_mem c d p;
         (match d with
          | TNode di _ dcs ->
            if has_kid c dcs then (has_kid_mem c dcs; kid_ids_sub c dcs)
            else (match parent_all c dcs with
                  | Some pp -> parent_all_mem c dcs pp
                  | None -> ()))
       | None ->
         parent_kids_all p c r n;
         parent_all_mem c r p;
         parent_absent c d)

(* ---- the edits, as the per-node view sees them ---- *)

let ins_view (p:string) (n:tree) (t:tree) (q:string)
  : Lemma (requires wf t /\ mem p (ids t) /\ not (mem q (ids n)))
          (ensures kids_at q (ins p n t) ==
                     (if q = p then app (kids_at q t) [tid_of n] else kids_at q t) /\
                   kind_at q (ins p n t) == kind_at q t)
  = find_ins q p n t;
    find_in_some_iff q t;
    match find_in q t with
    | None -> ()
    | Some m ->
      find_in_id q t m;
      (match m with
       | TNode mi mk mcs ->
         kid_ids_ins_all p n mcs;
         kid_ids_app (ins_all p n mcs) [n])

let ins_view_inside (p:string) (n:tree) (t:tree) (q:string)
  : Lemma (requires wf t /\ mem p (ids t) /\ mem q (ids n) /\ not (mem q (ids t)))
          (ensures kids_at q (ins p n t) == kids_at q n /\ kind_at q (ins p n t) == kind_at q n)
  = find_ins_inside q p n t

let rem_view (pid x:string) (t:tree) (sub:tree) (q:string)
  : Lemma (requires wf t /\ parent_of x t == Some pid /\ find_in x t == Some sub /\
                    mem q (ids t) /\ not (mem q (ids sub)))
          (ensures mem q (ids (rem_at pid x t)) /\
                   kids_at q (rem_at pid x t) == drop_id x (kids_at q t) /\
                   kind_at q (rem_at pid x t) == kind_at q t)
  = rem_survives q pid x t sub;
    find_rem q pid x t;
    match find_in q t with
    | None -> ()
    | Some m ->
      find_in_id q t m;
      (match m with
       | TNode mi mk mcs ->
         kid_ids_rem_all pid x mcs;
         kid_ids_drop x (rem_all pid x mcs);
         if q = pid then ()
         else begin
           (if mem x (kid_ids mcs) then parent_kids q x t m else ());
           drop_id_absent x (kid_ids mcs)
         end)

let move_view (x np:string) (t:tree) (pid:string) (sub:tree) (q:string)
  : Lemma (requires wf t /\ parent_of x t == Some pid /\ find_in x t == Some sub /\
                    mem np (ids t) /\ not (mem np (ids sub)) /\ mem q (ids t))
          (ensures (let t' = ins np sub (rem_at pid x t) in
                    kids_at q t' == app (drop_id x (kids_at q t)) (if q = np then [x] else []) /\
                    kind_at q t' == kind_at q t))
  = let removed = rem_at pid x t in
    rem_wf pid x t;
    rem_survives np pid x t sub;
    rem_kills pid x t sub;
    find_in_id x t sub;
    app_nil_r (drop_id x (kids_at q t));
    if mem q (ids sub) then begin
      ins_view_inside np sub removed q;
      find_in_trans x q t sub;
      (match find_in q t with
       | Some m ->
         if mem x (kid_ids (kids_of m))
         then (parent_kids q x t m; rem_keeps_parent pid x t)
         else ()
       | None -> ());
      drop_id_absent x (kids_at q t)
    end
    else begin
      rem_view pid x t sub q;
      ins_view np sub removed q
    end

let move_ids (x np:string) (t:tree) (pid:string) (sub:tree) (y:string)
  : Lemma (requires wf t /\ parent_of x t == Some pid /\ find_in x t == Some sub /\
                    mem np (ids t) /\ not (mem np (ids sub)))
          (ensures mem y (ids (ins np sub (rem_at pid x t))) == mem y (ids t))
  = rem_survives np pid x t sub;
    ids_ins_mem y np sub (rem_at pid x t);
    ids_rem_sub y pid x t;
    (if mem y (ids sub) then find_in_sub x y t sub
     else if mem y (ids t) then rem_survives y pid x t sub
     else ())

let move_accepted (x np:string) (t:tree)
  : Lemma (requires wf t /\ tid_of t <> x /\ mem x (ids t) /\ mem np (ids t) /\
                    (match find_in x t with
                     | Some sub -> not (mem np (ids sub))
                     | None -> True))
          (ensures (match find_in x t, parent_of x t with
                    | Some sub, Some pid ->
                      apply (MoveNode x np) t == Ok (ins np sub (rem_at pid x t))
                    | _, _ -> False))
  = find_in_some_iff x t;
    parent_exists x t;
    match find_in x t, parent_of x t with
    | Some sub, Some pid -> rem_survives np pid x t sub
    | _, _ -> ()

(* ---- a parent-closed set that excludes a node excludes its whole subtree ---- *)

let rec find_all_kid (cs:list tree) (c:tree)
  : Lemma (requires wf_all cs /\ mem c cs) (ensures find_all (tid_of c) cs == Some c)
          (decreases cs)
  = match cs with
    | [] -> ()
    | d :: r ->
      if d = c then find_in_self (tid_of c) c
      else begin
        inter_nil_iff (ids d) (ids_all r);
        mem_ids_all_intro (tid_of c) c r;
        find_in_some_iff (tid_of c) d;
        find_all_kid r c
      end

let rec closed_outside (d:string -> bool) (t:tree) (s:tree)
  : Lemma (requires wf t /\ find_in (tid_of s) t == Some s /\ not (d (tid_of s)) /\
                    (forall (q c:string). mem c (kids_at q t) /\ d c ==> d q))
          (ensures forall (y:string). mem y (ids s) ==> not (d y)) (decreases s)
  = match s with
    | TNode i k cs -> closed_outside_all d t s cs
and closed_outside_all (d:string -> bool) (t:tree) (s0:tree) (cs:list tree)
  : Lemma (requires wf t /\ find_in (tid_of s0) t == Some s0 /\ not (d (tid_of s0)) /\
                    cs << s0 /\
                    (forall (c:tree). mem c cs ==> mem c (kids_of s0)) /\
                    (forall (q c:string). mem c (kids_at q t) /\ d c ==> d q))
          (ensures forall (y:string). mem y (ids_all cs) ==> not (d y)) (decreases cs)
  = match cs with
    | [] -> ()
    | c :: r ->
      find_in_wf (tid_of s0) t s0;
      kid_ids_of_mem c (kids_of s0);
      (match s0 with
       | TNode si sk scs ->
         find_all_kid scs c;
         kid_ids_sub (tid_of c) scs);
      find_in_trans (tid_of s0) (tid_of c) t s0;
      assert (kids_at (tid_of s0) t == kid_ids (kids_of s0));
      assert (mem (tid_of c) (kids_at (tid_of s0) t));
      assert (d (tid_of c) ==> d (tid_of s0));
      closed_outside d t c;
      closed_outside_all d t s0 r;
      let aux (y:string) : Lemma (mem y (ids_all (c :: r)) ==> not (d y))
        = mem_app y (ids c) (ids_all r) in
      FStar.Classical.forall_intro aux

(* ---- and a reorder ---- *)

let rec pick_last_some (x:string) (ts:list tree)
  : Lemma (requires mem x (kid_ids ts)) (ensures Some? (pick_last x ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> if mem x (kid_ids r) then pick_last_some x r else ()

let rec arrange_kid_ids (ord:list string) (cs:list tree)
  : Lemma (requires forall (x:string). mem x ord ==> mem x (kid_ids cs))
          (ensures kid_ids (arrange ord cs) == ord) (decreases ord)
  = match ord with
    | [] -> ()
    | x :: rest ->
      pick_last_some x cs;
      (match pick_last x cs with
       | Some n -> pick_last_mem x cs n
       | None -> ());
      arrange_kid_ids rest cs

let reorder_view (p:string) (ord:list string) (t:tree) (q:string)
  : Lemma (requires wf t /\ mem p (ids t) /\ same_multiset (kids_at p t) ord)
          (ensures apply (ReorderChildren p ord) t == Ok (reorder_at p ord t) /\
                   kids_at q (reorder_at p ord t) == (if q = p then ord else kids_at q t) /\
                   kind_at q (reorder_at p ord t) == kind_at q t /\
                   mem q (ids (reorder_at p ord t)) == mem q (ids t))
  = find_in_some_iff p t;
    wf_reorder_ok p ord t;
    find_reorder q p ord t;
    ids_reorder_mem q p ord t;
    same_multiset_mem (kids_at p t) ord;
    match find_in q t with
    | None -> ()
    | Some m ->
      find_in_id q t m;
      (match m with
       | TNode mi mk mcs ->
         kid_ids_reorder_all p ord mcs;
         if q = p then arrange_kid_ids ord (reorder_all p ord mcs) else ())

(* the children of a node of a well-formed tree carry distinct ids *)
let kids_at_no_dups (q:string) (t:tree)
  : Lemma (requires wf t) (ensures no_dups (kids_at q t))
  = match find_in q t with
    | None -> ()
    | Some m ->
      find_in_wf q t m;
      (match m with TNode _ _ mcs -> wf_all_kid_ids_no_dups mcs)
