module PresRecon

open DagFold
open TreeOps
open Preservation

#set-options "--ext context_pruning"

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
