(*
   Preservation — the apply engine's own theorem: a legal operation applied to a valid tree yields
   a valid tree, and the three clauses around that sentence which no test or law states
   (fuaran-core Phase 138).

   WHAT IS MODELLED. Nothing new. `TreeOps.fst` (Phase 133, lifted by Phase 138 to the validator
   Phase 137 shipped) is imported rather than remodelled, and this module adds `Ops.canApply` and
   `Ops.invert` — the two surfaces of the engine that model did not reach — clause for clause.

   WHAT IS PROVED, over the five skeleton operations and any tree:

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
        | None -> Error (UnknownNode p (ids pre))))

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

#pop-options
