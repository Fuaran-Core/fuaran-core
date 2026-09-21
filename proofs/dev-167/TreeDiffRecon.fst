module TreeDiffRecon

open DagFold
open TreeOps
open Preservation
open TreeDiff
open PresRecon

#set-options "--ext context_pruning"

let rec precedes_prefix (p x:string) (l m:list string)
  : Lemma (requires precedes p x (app l (x :: m)) /\ no_dups (app l (x :: m)))
          (ensures mem p l) (decreases l)
  = match l with
    | [] -> ()
    | h :: t ->
      mem_app x t (x :: m);
      if h = p then () else if h = x then () else precedes_prefix p x t m

let rec parent_map_app (l m:list tree)
  : Lemma (ensures parent_map (app l m) == app (parent_map l) (parent_map m)) (decreases l)
  = match l with
    | [] -> ()
    | p :: r ->
      parent_map_app r m;
      app_assoc (kid_pairs (tid_of p) (kids_of p)) (parent_map r) (parent_map m)

let rec lookup_kid_pairs_some (k pk:string) (cs:list tree)
  : Lemma (requires mem k (kid_ids cs)) (ensures Some? (lookup k (kid_pairs pk cs))) (decreases cs)
  = match cs with
    | [] -> ()
    | c :: r -> if mem k (kid_ids r) then lookup_kid_pairs_some k pk r else ()

let rec lookup_some (t:tree) (k:string)
  : Lemma (requires mem k (ids_all (kids_of t)))
          (ensures Some? (lookup k (parent_map (pre t)))) (decreases t)
  = match t with
    | TNode i _ cs ->
      lookup_app k (kid_pairs i cs) (parent_map (pre_all cs));
      lookup_some_all i cs k
and lookup_some_all (pk:string) (cs:list tree) (k:string)
  : Lemma (requires mem k (ids_all cs))
          (ensures Some? (lookup k (kid_pairs pk cs)) \/ Some? (lookup k (parent_map (pre_all cs))))
          (decreases cs)
  = match cs with
    | [] -> ()
    | c :: r ->
      mem_app k (ids c) (ids_all r);
      parent_map_app (pre c) (pre_all r);
      lookup_app k (parent_map (pre c)) (parent_map (pre_all r));
      if mem k (ids c) then begin
        match c with
        | TNode ci _ ccs ->
          if k = ci then lookup_kid_pairs_some k pk (c :: r)
          else lookup_some c k
      end
      else lookup_some_all pk r k


(* ================= bridges between the diff maps and the per-node view ================= *)

let rec pre_find (t:tree) (n:tree)
  : Lemma (requires wf t /\ mem n (pre t)) (ensures find_in (tid_of n) t == Some n) (decreases t)
  = match t with
    | TNode i _ cs ->
      if n = t then ()
      else begin
        pre_all_sub_ids cs n (tid_of n);
        pre_all_find cs n
      end
and pre_all_find (ts:list tree) (n:tree)
  : Lemma (requires wf_all ts /\ mem n (pre_all ts)) (ensures find_all (tid_of n) ts == Some n)
          (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r ->
      mem_app n (pre c) (pre_all r);
      inter_nil_iff (ids c) (ids_all r);
      find_in_some_iff (tid_of n) c;
      if mem n (pre c) then pre_find c n
      else begin
        pre_all_sub_ids r n (tid_of n);
        pre_all_find r n
      end

let kids_at_pre (a:tree) (n:tree)
  : Lemma (requires wf a /\ mem n (pre a))
          (ensures kids_at (tid_of n) a == kid_ids (kids_of n) /\
                   kind_at (tid_of n) a == Some (kind_of n))
  = pre_find a n

let kid_unique (t:tree) (q1 q2 c:string)
  : Lemma (requires wf t /\ mem c (kids_at q1 t) /\ mem c (kids_at q2 t)) (ensures q1 == q2)
  = (match find_in q1 t with Some m -> parent_kids q1 c t m | None -> ());
    (match find_in q2 t with Some m -> parent_kids q2 c t m | None -> ())

let kids_in_ids (t:tree) (q c:string)
  : Lemma (requires mem c (kids_at q t)) (ensures mem c (ids t) /\ mem q (ids t))
  = find_in_some_iff q t;
    match find_in q t with
    | Some m -> kid_ids_sub c (kids_of m); (match m with TNode _ _ _ -> ()); find_in_sub q c t m
    | None -> ()

let kid_not_root (t:tree) (q c:string)
  : Lemma (requires wf t /\ mem c (kids_at q t)) (ensures c <> tid_of t)
  = match find_in q t with
    | Some m ->
      parent_kids q c t m;
      (match t with
       | TNode i _ cs ->
         if has_kid c cs then (has_kid_mem c cs; kid_ids_sub c cs)
         else parent_all_mem c cs q)
    | None -> ()

(* the first node of a list holding `x` among its children, and the first carrying an id *)
let rec kid_holder (ns:list tree) (x:string) : Tot (option tree) (decreases ns) =
  match ns with
  | [] -> None
  | n :: r -> if mem x (kid_ids (kids_of n)) then Some n else kid_holder r x

let rec kid_holder_some (ns:list tree) (x:string) (h:tree)
  : Lemma (requires kid_holder ns x == Some h)
          (ensures mem h ns /\ mem x (kid_ids (kids_of h)) /\ is_after_child ns x (tid_of h))
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r -> if mem x (kid_ids (kids_of n)) then () else kid_holder_some r x h

let rec kid_holder_intro (ns:list tree) (x:string) (h:tree)
  : Lemma (requires mem h ns /\ mem x (kid_ids (kids_of h))) (ensures Some? (kid_holder ns x))
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r -> if mem x (kid_ids (kids_of n)) then () else kid_holder_intro r x h

let rec node_with (ns:list tree) (k:string) : Tot (option tree) (decreases ns) =
  match ns with
  | [] -> None
  | n :: r -> if tid_of n = k then Some n else node_with r k

let rec node_with_some (ns:list tree) (k:string)
  : Lemma (requires mem k (tids ns))
          (ensures (match node_with ns k with
                    | Some n -> mem n ns /\ tid_of n == k
                    | None -> False)) (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r -> if tid_of n = k then () else node_with_some r k

let rec tids_mem (ns:list tree) (n:tree)
  : Lemma (requires mem n ns) (ensures mem (tid_of n) (tids ns)) (decreases ns)
  = match ns with
    | [] -> ()
    | m :: r -> if m = n then () else tids_mem r n

let rec after_child_kids_at (a:tree) (ns:list tree) (c v:string)
  : Lemma (requires wf a /\ is_after_child ns c v /\ (forall (n:tree). mem n ns ==> mem n (pre a)))
          (ensures mem c (kids_at v a)) (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r ->
      if tid_of n = v && mem c (kid_ids (kids_of n)) then kids_at_pre a n
      else after_child_kids_at a r c v

(* M1: the last-wins parent map agrees with the tree, both ways *)
let lookup_is_parent (a:tree) (c v:string)
  : Lemma (requires wf a /\ lookup c (parent_map (pre a)) == Some v)
          (ensures mem c (kids_at v a))
  = lookup_parent_map (pre a) c v;
    after_child_kids_at a (pre a) c v

let parent_is_lookup (a:tree) (c v:string)
  : Lemma (requires wf a /\ mem c (kids_at v a))
          (ensures lookup c (parent_map (pre a)) == Some v)
  = kid_not_root a v c;
    kids_in_ids a v c;
    (match a with TNode _ _ _ -> lookup_some a c);
    match lookup c (parent_map (pre a)) with
    | Some v' -> lookup_is_parent a c v'; kid_unique a v v' c
    | None -> ()

(* M2: the last-wins child-key map agrees with the tree *)
let rec lookup_kids_sound (b:tree) (ns:list tree) (k:string) (v:list string)
  : Lemma (requires wf b /\ (forall (n:tree). mem n ns ==> mem n (pre b)) /\
                    lookup_kids k (kid_map ns) == Some v)
          (ensures v == kids_at k b) (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r ->
      (match lookup_kids k (kid_map r) with
       | Some _ -> lookup_kids_sound b r k v
       | None -> kids_at_pre b n)

let rec lookup_kids_some (ns:list tree) (k:string)
  : Lemma (requires mem k (tids ns)) (ensures Some? (lookup_kids k (kid_map ns))) (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r -> if mem k (tids r) then lookup_kids_some r k else ()

let lookup_kids_is (b:tree) (k:string)
  : Lemma (requires wf b /\ mem k (ids b))
          (ensures lookup_kids k (kid_map (pre b)) == Some (kids_at k b))
  = ids_is_pre b;
    lookup_kids_some (pre b) k;
    match lookup_kids k (kid_map (pre b)) with
    | Some v -> lookup_kids_sound b (pre b) k v
    | None -> ()

(* ================= the positional facts a SUFFIX of the walk satisfies ================= *)

let rec precedes_not_back (v k:string) (l m:list string)
  : Lemma (requires precedes v k (app l m) /\ mem k l /\ mem v m /\ no_dups (app l m))
          (ensures False) (decreases l)
  = match l with
    | [] -> ()
    | h :: t ->
      mem_app v t m;
      if h = v then () else if h = k then () else precedes_not_back v k t m

let rec is_after_child_app_r (l m:list tree) (x v:string)
  : Lemma (requires is_after_child m x v) (ensures is_after_child (app l m) x v) (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> is_after_child_app_r t m x v

let rec mem_app_r_tree (l m:list tree) (n:tree)
  : Lemma (requires mem n m) (ensures mem n (app l m)) (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> mem_app_r_tree t m n

(* a node that a suffix of the walk holds as a child is itself in that suffix, is not in the
   prefix, and is not the head of the suffix *)
let sfx_holder (a:tree) (done ns:list tree) (x:string) (h:tree)
  : Lemma (requires wf a /\ pre a == app done ns /\ kid_holder ns x == Some h)
          (ensures mem x (tids ns) /\ not (mem x (tids done)) /\
                   (match ns with n :: _ -> x <> tid_of n | [] -> True))
  = kid_holder_some ns x h;
    is_after_child_app_r done ns x (tid_of h);
    after_parent_precedes a x (tid_of h);
    ids_is_pre a;
    tids_app done ns;
    wf_iff_no_dups a;
    tids_mem ns h;
    mem_app_r_tree done ns h;
    kids_at_pre a h;
    kids_in_ids a (tid_of h) x;
    mem_app x (tids done) (tids ns);
    (if mem x (tids done) then precedes_not_back (tid_of h) x (tids done) (tids ns) else ());
    match ns with
    | [] -> ()
    | n :: r ->
      if x = tid_of n then begin
        precedes_prefix (tid_of h) x (tids done) (tids r);
        no_dups_app (tids done) (tids ns);
        inter_nil_iff (tids done) (tids ns)
      end
      else ()

let rec apply_all_app (l m:list op) (t:tree)
  : Lemma (ensures apply_all (app l m) t ==
                   (match apply_all l t with
                    | Ok t' -> apply_all m t'
                    | Error e -> Error e)) (decreases l)
  = match l with
    | [] -> ()
    | o :: r -> (match apply o t with
                 | Ok t' -> apply_all_app r m t'
                 | Error _ -> ())

(* ================= the prefix invariant, and pass 1 ================= *)

(* which tree a node's PARENT is currently read from: `after` once the script has placed it *)
let side (b a:tree) (placed:bool) (q c:string) : Tot bool =
  if placed then mem c (kids_at q a) else mem c (kids_at q b)

let kind_src (b a:tree) (q:string) : Tot (option string) =
  if mem q (ids b) then kind_at q b else kind_at q a

let same_kids (b a:tree) (q:string) : Tot bool =
  mem q (ids a) && mem q (ids b) && kids_at q b = kids_at q a

let cond1 (b a:tree) (ns:list tree) (c:string) : Tot bool =
  mem c (ids a) && not (mem c (ids b)) && not (mem c (tids ns))

let inv1 (b a:tree) (ns:list tree) (t:tree) : prop =
  wf t /\ tid_of t == tid_of a /\
  (forall (y:string). mem y (ids t) == (mem y (ids b) || (mem y (ids a) && not (mem y (tids ns))))) /\
  (forall (q c:string). mem c (kids_at q t) == side b a (cond1 b a ns c) q c) /\
  (forall (q:string). mem q (ids t) ==> kind_at q t == kind_src b a q) /\
  (forall (q:string). same_kids b a q ==> kids_at q t == kids_at q b)

let absent_no_kids (t:tree) (q:string)
  : Lemma (requires not (mem q (ids t))) (ensures kids_at q t == [] /\ kind_at q t == None)
  = find_in_some_iff q t

#push-options "--z3rlimit 200 --fuel 2 --ifuel 1"
let ins_step (b a:tree) (done:list tree) (n:tree) (r:list tree) (t:tree) (pid:string)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done (n :: r) /\
                    inv1 b a (n :: r) t /\ not (mem (tid_of n) (ids b)) /\
                    lookup (tid_of n) (parent_map (pre a)) == Some pid)
          (ensures apply (InsertChild pid (shell n)) t == Ok (ins pid (shell n) t) /\
                   inv1 b a r (ins pid (shell n) t))
  = let k = tid_of n in
    let t' = ins pid (shell n) t in
    ids_is_pre a;
    tids_app done (n :: r);
    wf_iff_no_dups a;
    no_dups_app (tids done) (tids (n :: r));
    inter_nil_iff (tids done) (tids (n :: r));
    mem_app_r_tree done (n :: r) n;
    mem_app k (tids done) (tids (n :: r));
    lookup_is_parent a k pid;
    kids_in_ids a pid k;
    lookup_parent_map (pre a) k pid;
    after_parent_precedes a k pid;
    precedes_prefix pid k (tids done) (tids r);
    assert (mem pid (ids t));
    assert (not (mem k (ids t)));
    first_dup_none_iff (shell n) t;
    inter_nil_iff (ids (shell n)) (ids t);
    assert (apply (InsertChild pid (shell n)) t == Ok t');
    apply_preserves_wf (InsertChild pid (shell n)) t;
    ids_ins_mem_fa pid (shell n) t;
    tid_ins pid (shell n) t;
    kids_at_pre a n;
    absent_no_kids b k;
    let aux_k (q c:string)
      : Lemma (mem c (kids_at q t') == side b a (cond1 b a r c) q c)
      = if q = k then begin
          ins_view_inside pid (shell n) t k;
          if mem c (kid_ids (kids_of n)) then sfx_holder a done (n :: r) c n else ()
        end
        else begin
          ins_view pid (shell n) t q;
          mem_app c (kids_at q t) [k];
          if c = k then begin
            (if mem k (kids_at q t) then kids_in_ids t q k else ());
            (if mem k (kids_at q a) then kid_unique a q pid k else ())
          end
          else ()
        end
    in
    let aux_n (q:string)
      : Lemma (mem q (ids t') ==> kind_at q t' == kind_src b a q)
      = if q = k then ins_view_inside pid (shell n) t k
        else ins_view pid (shell n) t q
    in
    let aux_x (q:string)
      : Lemma (same_kids b a q ==> kids_at q t' == kids_at q b)
      = if same_kids b a q then begin
          ins_view pid (shell n) t q;
          if q = pid then kids_in_ids b pid k else ()
        end
        else ()
    in
    FStar.Classical.forall_intro_2 aux_k;
    FStar.Classical.forall_intro aux_n;
    FStar.Classical.forall_intro aux_x

let inv1_skip (b a:tree) (n:tree) (r:list tree) (t:tree)
  : Lemma (requires inv1 b a (n :: r) t /\ mem (tid_of n) (ids b))
          (ensures inv1 b a r t)
  = ()

let rec inserts_run (b a:tree) (done ns:list tree) (t:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done ns /\ inv1 b a ns t)
          (ensures (match apply_all (pass_inserts (parent_map (pre a)) (ids b) ns) t with
                    | Ok t1 -> inv1 b a [] t1
                    | Error _ -> False))
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r ->
      let k = tid_of n in
      app_assoc done [n] r;
      if mem k (ids b) then begin
        inv1_skip b a n r t;
        inserts_run b a (app done [n]) r t
      end
      else begin
        match lookup k (parent_map (pre a)) with
        | Some pid ->
          ins_step b a done n r t pid;
          inserts_run b a (app done [n]) r (ins pid (shell n) t)
        | None ->
          ids_is_pre a;
          tids_app done (n :: r);
          mem_app k (tids done) (tids (n :: r));
          (match a with
           | TNode ai _ acs -> if k = ai then () else lookup_some a k)
      end
#pop-options

let inv1_init (b a:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a) (ensures inv1 b a (pre a) b)
  = ids_is_pre a

(* ================= pass 2: the moves ================= *)

(* a survivor still waiting for its move: a child of the parent in hand (`rest`), or of a parent
   the walk has not reached yet (`ns`) *)
let cond2 (b a:tree) (rest:list string) (ns:list tree) (c:string) : Tot bool =
  mem c (ids a) && (not (mem c (ids b)) || not (mem c rest || Some? (kid_holder ns c)))

let inv2 (b a:tree) (rest:list string) (ns:list tree) (t:tree) : prop =
  wf t /\ tid_of t == tid_of a /\
  (forall (y:string). mem y (ids t) == (mem y (ids b) || mem y (ids a))) /\
  (forall (q c:string). mem c (kids_at q t) == side b a (cond2 b a rest ns c) q c) /\
  (forall (q:string). mem q (ids t) ==> kind_at q t == kind_src b a q) /\
  (forall (q:string). same_kids b a q ==> kids_at q t == kids_at q b)

let holder_exists (a:tree) (c:string)
  : Lemma (requires wf a /\ mem c (ids a) /\ c <> tid_of a) (ensures Some? (kid_holder (pre a) c))
  = (match a with TNode _ _ _ -> lookup_some a c);
    match lookup c (parent_map (pre a)) with
    | Some v ->
      lookup_is_parent a c v;
      (match find_in v a with
       | Some m -> find_in_mem_pre v a m; kid_holder_intro (pre a) c m
       | None -> ())
    | None -> ()

let inv1_to_inv2 (b a t:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ inv1 b a [] t)
          (ensures inv2 b a [] (pre a) t)
  = let aux (q c:string)
      : Lemma (side b a (cond1 b a [] c) q c == side b a (cond2 b a [] (pre a) c) q c)
      = if mem c (ids a) && mem c (ids b) then begin
          if c = tid_of a then begin
            (if mem c (kids_at q a) then kid_not_root a q c else ());
            (if mem c (kids_at q b) then kid_not_root b q c else ())
          end
          else holder_exists a c
        end
        else ()
    in
    FStar.Classical.forall_intro_2 aux

(* the set the nesting argument closes over: after-nodes no parent still to come holds *)
let outside_sfx (a:tree) (sfx:list tree) (y:string) : Tot bool =
  mem y (ids a) && None? (kid_holder sfx y)

#push-options "--z3rlimit 200 --fuel 2 --ifuel 1"
let d_closed (b a:tree) (done:list tree) (p:tree) (ns:list tree) (rest:list string) (t:tree)
             (q c:string)
  : Lemma (requires wf a /\ pre a == app done (p :: ns) /\ inv2 b a rest ns t /\
                    (forall (z:string). mem z rest ==> mem z (kid_ids (kids_of p))) /\
                    mem c (kids_at q t) /\ outside_sfx a (p :: ns) c)
          (ensures outside_sfx a (p :: ns) q)
  = assert (cond2 b a rest ns c);
    assert (mem c (kids_at q a));
    kids_in_ids a q c;
    match kid_holder (p :: ns) q with
    | None -> ()
    | Some h ->
      sfx_holder a done (p :: ns) q h;
      node_with_some (p :: ns) q;
      (match node_with (p :: ns) q with
       | Some qn ->
         mem_app_r_tree done (p :: ns) qn;
         kids_at_pre a qn;
         kid_holder_intro (p :: ns) c qn
       | None -> ())

let move_step (b a:tree) (done:list tree) (p:tree) (ns:list tree) (ck:string) (rest:list string)
              (t:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done (p :: ns) /\
                    inv2 b a (ck :: rest) ns t /\ no_dups (ck :: rest) /\
                    (forall (z:string). mem z (ck :: rest) ==> mem z (kid_ids (kids_of p))) /\
                    not (same_kids b a (tid_of p)) /\ mem ck (ids b))
          (ensures (match apply (MoveNode ck (tid_of p)) t with
                    | Ok t' -> inv2 b a rest ns t'
                    | Error _ -> False))
  = let pk = tid_of p in
    mem_app_r_tree done (p :: ns) p;
    kids_at_pre a p;
    kid_not_root a pk ck;
    kids_in_ids a pk ck;
    ids_is_pre a;
    tids_app done (p :: ns);
    wf_iff_no_dups a;
    no_dups_app (tids done) (tids (p :: ns));
    find_in_some_iff ck t;
    match find_in ck t with
    | None -> ()
    | Some sub ->
      find_in_id ck t sub;
      let d = outside_sfx a (p :: ns) in
      let clo (q c:string)
        : Lemma (mem c (kids_at q t) /\ d c ==> d q)
        = FStar.Classical.move_requires (d_closed b a done p ns (ck :: rest) t q) c
      in
      FStar.Classical.forall_intro_2 clo;
      assert (not (d ck));
      closed_outside d t sub;
      (match kid_holder (p :: ns) pk with
       | Some h -> sfx_holder a done (p :: ns) pk h
       | None -> ());
      assert (d pk);
      assert (not (mem pk (ids sub)));
      move_accepted ck pk t;
      (match parent_of ck t with
       | None -> ()
       | Some pid ->
         let t' = ins pk sub (rem_at pid ck t) in
         apply_preserves_wf (MoveNode ck pk) t;
         tid_ins pk sub (rem_at pid ck t);
         tid_rem pid ck t;
         let aux_i (y:string) : Lemma (mem y (ids t') == (mem y (ids b) || mem y (ids a)))
           = move_ids ck pk t pid sub y
         in
         let aux_k (q c:string)
           : Lemma (mem c (kids_at q t') == side b a (cond2 b a rest ns c) q c)
           = if mem q (ids t) then begin
               move_view ck pk t pid sub q;
               mem_app c (drop_id ck (kids_at q t)) (if q = pk then [ck] else []);
               drop_id_mem c ck (kids_at q t);
               if c = ck then begin
                 (match kid_holder ns ck with
                  | Some h ->
                    kid_holder_some ns ck h;
                    mem_app_r_tree done (p :: ns) h;
                    kids_at_pre a h;
                    kid_unique a (tid_of h) pk ck;
                    tids_mem ns h
                  | None -> ());
                 (if mem ck (kids_at q a) then kid_unique a q pk ck else ())
               end
               else ()
             end
             else begin
               move_ids ck pk t pid sub q;
               absent_no_kids t' q;
               absent_no_kids a q;
               absent_no_kids b q
             end
         in
         let aux_n (q:string) : Lemma (mem q (ids t') ==> kind_at q t' == kind_src b a q)
           = move_ids ck pk t pid sub q;
             if mem q (ids t) then move_view ck pk t pid sub q else ()
         in
         let aux_x (q:string) : Lemma (same_kids b a q ==> kids_at q t' == kids_at q b)
           = if same_kids b a q then begin
               move_view ck pk t pid sub q;
               (if mem ck (kids_at q a) then kid_unique a q pk ck else ());
               drop_id_absent ck (kids_at q t);
               app_nil_r (kids_at q t)
             end
             else ()
         in
         FStar.Classical.forall_intro aux_i;
         FStar.Classical.forall_intro_2 aux_k;
         FStar.Classical.forall_intro aux_n;
         FStar.Classical.forall_intro aux_x)

let move_skip (b a:tree) (done:list tree) (p:tree) (ns:list tree) (ck:string) (rest:list string)
              (t:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done (p :: ns) /\
                    inv2 b a (ck :: rest) ns t /\ mem ck (kid_ids (kids_of p)) /\
                    not (mem ck (ids b) &&
                         (match lookup ck (parent_map (pre b)) with
                          | Some bp -> bp <> tid_of p
                          | None -> true)))
          (ensures inv2 b a rest ns t)
  = let pk = tid_of p in
    mem_app_r_tree done (p :: ns) p;
    kids_at_pre a p;
    let aux (q c:string)
      : Lemma (side b a (cond2 b a (ck :: rest) ns c) q c == side b a (cond2 b a rest ns c) q c)
      = if c = ck && mem ck (ids b) then begin
          lookup_is_parent b ck pk;
          (if mem ck (kids_at q a) then kid_unique a q pk ck else ());
          (if mem ck (kids_at q b) then kid_unique b q pk ck else ())
        end
        else ()
    in
    FStar.Classical.forall_intro_2 aux

let rec moves_under_run (b a:tree) (done:list tree) (p:tree) (ns:list tree) (cs:list tree)
                        (t:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done (p :: ns) /\
                    inv2 b a (kid_ids cs) ns t /\ no_dups (kid_ids cs) /\
                    (forall (z:string). mem z (kid_ids cs) ==> mem z (kid_ids (kids_of p))) /\
                    not (same_kids b a (tid_of p)))
          (ensures (match apply_all (moves_under (tid_of p) (ids b) (parent_map (pre b)) cs) t with
                    | Ok t' -> inv2 b a [] ns t'
                    | Error _ -> False))
          (decreases cs)
  = match cs with
    | [] -> ()
    | c :: r ->
      let ck = tid_of c in
      let pk = tid_of p in
      let moved =
        mem ck (ids b) &&
        (match lookup ck (parent_map (pre b)) with
         | Some bp -> bp <> pk
         | None -> true) in
      if moved then begin
        move_step b a done p ns ck (kid_ids r) t;
        (match apply (MoveNode ck pk) t with
         | Ok t' -> moves_under_run b a done p ns r t'
         | Error _ -> ())
      end
      else begin
        move_skip b a done p ns ck (kid_ids r) t;
        moves_under_run b a done p ns r t
      end

let inv2_regroup (b a:tree) (p:tree) (ns:list tree) (t:tree)
  : Lemma (requires inv2 b a [] (p :: ns) t) (ensures inv2 b a (kid_ids (kids_of p)) ns t)
  = ()

let inv2_unchanged (b a:tree) (done:list tree) (p:tree) (ns:list tree) (t:tree)
  : Lemma (requires wf b /\ wf a /\ pre a == app done (p :: ns) /\ inv2 b a [] (p :: ns) t /\
                    same_kids b a (tid_of p))
          (ensures inv2 b a [] ns t)
  = let pk = tid_of p in
    mem_app_r_tree done (p :: ns) p;
    kids_at_pre a p;
    let aux (q c:string)
      : Lemma (side b a (cond2 b a [] (p :: ns) c) q c == side b a (cond2 b a [] ns c) q c)
      = if mem c (kid_ids (kids_of p)) then begin
          (if mem c (kids_at q a) then kid_unique a q pk c else ());
          (if mem c (kids_at q b) then kid_unique b q pk c else ())
        end
        else ()
    in
    FStar.Classical.forall_intro_2 aux

let rec moves_run (b a:tree) (done ns:list tree) (t:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done ns /\
                    inv2 b a [] ns t)
          (ensures (match apply_all (fst (pass_moves (ids b) (parent_map (pre b)) (kid_map (pre b)) ns)) t with
                    | Ok t' -> inv2 b a [] [] t'
                    | Error _ -> False))
          (decreases ns)
  = match ns with
    | [] -> ()
    | p :: r ->
      let pk = tid_of p in
      app_assoc done [p] r;
      mem_app_r_tree done (p :: r) p;
      kids_at_pre a p;
      pre_sub_ids a p pk;
      (if mem pk (ids b) then lookup_kids_is b pk else ());
      if same_kids b a pk then begin
        inv2_unchanged b a done p r t;
        moves_run b a (app done [p]) r t
      end
      else begin
        inv2_regroup b a p r t;
        kids_at_no_dups pk a;
        moves_under_run b a done p r (kids_of p) t;
        apply_all_app (moves_under pk (ids b) (parent_map (pre b)) (kids_of p))
                      (fst (pass_moves (ids b) (parent_map (pre b)) (kid_map (pre b)) r)) t;
        (match apply_all (moves_under pk (ids b) (parent_map (pre b)) (kids_of p)) t with
         | Ok t' -> moves_run b a (app done [p]) r t'
         | Error _ -> ())
      end
#pop-options

(* ================= pass 3: the removals ================= *)

(* the node pass 3 removes: `after` does not carry it, and its before-parent survives *)
let top (b a:tree) (y:string) : Tot bool =
  not (mem y (ids a)) &&
  (match lookup y (parent_map (pre b)) with
   | Some pid -> mem pid (ids a)
   | None -> false)

let side3 (b a:tree) (ns:list tree) (q c:string) : Tot bool =
  if mem c (ids a) then mem c (kids_at q a)
  else mem c (kids_at q b) && (not (mem q (ids a)) || mem c (tids ns))

let inv3 (b a:tree) (ns:list tree) (t:tree) : prop =
  wf t /\ tid_of t == tid_of a /\
  (forall (y:string). mem y (ids a) ==> mem y (ids t)) /\
  (forall (y:string). mem y (tids ns) /\ top b a y ==> mem y (ids t)) /\
  (forall (q c:string). mem q (ids t) ==> mem c (kids_at q t) == side3 b a ns q c) /\
  (forall (q:string). mem q (ids t) ==> kind_at q t == kind_src b a q) /\
  (forall (q:string). same_kids b a q ==> kids_at q t == kids_at q b)

#push-options "--z3rlimit 200 --fuel 2 --ifuel 1"
let inv2_to_inv3 (b a t:tree)
  : Lemma (requires wf b /\ wf a /\ inv2 b a [] [] t) (ensures inv3 b a (pre b) t)
  = ids_is_pre b;
    let aux (q c:string)
      : Lemma (side b a (cond2 b a [] [] c) q c == side3 b a (pre b) q c)
      = if mem c (kids_at q b) then kids_in_ids b q c else ()
    in
    FStar.Classical.forall_intro_2 aux

let rem_closed (b a:tree) (ns:list tree) (t:tree) (k:string) (q c:string)
  : Lemma (requires wf b /\ inv3 b a ns t /\ not (mem k (ids a)) /\
                    mem c (kids_at q t) /\ (mem c (ids a) || top b a c) /\ c <> k)
          (ensures (mem q (ids a) || top b a q) /\ q <> k)
  = kids_in_ids t q c;
    if mem c (ids a) then kids_in_ids a q c
    else parent_is_lookup b c q

let rem_step (b a:tree) (n:tree) (r:list tree) (t:tree)
  : Lemma (requires wf b /\ wf a /\ inv3 b a (n :: r) t /\ no_dups (tids (n :: r)) /\
                    top b a (tid_of n))
          (ensures (match apply (RemoveNode (tid_of n)) t with
                    | Ok t' -> inv3 b a r t'
                    | Error _ -> False))
  = let k = tid_of n in
    tid_in_ids a;
    find_in_some_iff k t;
    parent_exists k t;
    match find_in k t, parent_of k t with
    | Some sub, Some pid ->
      find_in_id k t sub;
      let d (y:string) : Tot bool = (mem y (ids a) || top b a y) && y <> k in
      let clo (q c:string)
        : Lemma (mem c (kids_at q t) /\ d c ==> d q)
        = FStar.Classical.move_requires (rem_closed b a (n :: r) t k q) c
      in
      FStar.Classical.forall_intro_2 clo;
      closed_outside d t sub;
      let t' = rem_at pid k t in
      apply_preserves_wf (RemoveNode k) t;
      tid_rem pid k t;
      rem_kills pid k t sub;
      ids_rem_sub_fa pid k t;
      let aux_p (y:string)
        : Lemma ((mem y (ids a) \/ (mem y (tids r) /\ top b a y)) ==> mem y (ids t'))
        = if (mem y (ids a) || (mem y (tids r) && top b a y)) then rem_view pid k t sub y else ()
      in
      let aux_k (q c:string)
        : Lemma (mem q (ids t') ==> mem c (kids_at q t') == side3 b a r q c)
        = if mem q (ids t') then begin
            rem_view pid k t sub q;
            drop_id_mem c k (kids_at q t);
            if c = k then (if mem k (kids_at q b) then parent_is_lookup b k q else ())
            else ()
          end
          else ()
      in
      let aux_n (q:string) : Lemma (mem q (ids t') ==> kind_at q t' == kind_src b a q)
        = if mem q (ids t') then rem_view pid k t sub q else ()
      in
      let aux_x (q:string) : Lemma (same_kids b a q ==> kids_at q t' == kids_at q b)
        = if same_kids b a q then begin
            rem_view pid k t sub q;
            (if mem k (kids_at q a) then kids_in_ids a q k else ());
            drop_id_absent k (kids_at q t)
          end
          else ()
      in
      FStar.Classical.forall_intro aux_p;
      FStar.Classical.forall_intro_2 aux_k;
      FStar.Classical.forall_intro aux_n;
      FStar.Classical.forall_intro aux_x
    | _, _ -> ()

let rem_skip (b a:tree) (n:tree) (r:list tree) (t:tree)
  : Lemma (requires wf b /\ inv3 b a (n :: r) t /\ not (top b a (tid_of n)))
          (ensures inv3 b a r t)
  = let k = tid_of n in
    let aux (q c:string)
      : Lemma (mem q (ids t) ==> side3 b a (n :: r) q c == side3 b a r q c)
      = if c = k && mem k (kids_at q b) then parent_is_lookup b k q else ()
    in
    FStar.Classical.forall_intro_2 aux

let rec removes_run (b a:tree) (ns:list tree) (t:tree)
  : Lemma (requires wf b /\ wf a /\ inv3 b a ns t /\ no_dups (tids ns))
          (ensures (match apply_all (pass_removes (ids a) (parent_map (pre b)) ns) t with
                    | Ok t' -> inv3 b a [] t'
                    | Error _ -> False))
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r ->
      if top b a (tid_of n) then begin
        rem_step b a n r t;
        (match apply (RemoveNode (tid_of n)) t with
         | Ok t' -> removes_run b a r t'
         | Error _ -> ())
      end
      else begin
        rem_skip b a n r t;
        removes_run b a r t
      end
#pop-options

(* ================= pass 4: the reorders ================= *)

let inv4 (b a:tree) (ps:list string) (t:tree) : prop =
  wf t /\ tid_of t == tid_of a /\
  (forall (y:string). mem y (ids a) ==> mem y (ids t)) /\
  (forall (q c:string). mem q (ids t) ==> mem c (kids_at q t) == side3 b a [] q c) /\
  (forall (q:string). mem q (ids t) ==> kind_at q t == kind_src b a q) /\
  (forall (q:string). mem q (ids a) /\ not (mem q ps) ==> kids_at q t == kids_at q a)

let rec changed_listed (b a:tree) (ns:list tree) (q:string)
  : Lemma (requires wf b /\ wf a /\ (forall (n:tree). mem n ns ==> mem n (pre a)) /\
                    mem q (tids ns) /\ not (same_kids b a q))
          (ensures mem q (snd (pass_moves (ids b) (parent_map (pre b)) (kid_map (pre b)) ns)))
          (decreases ns)
  = match ns with
    | [] -> ()
    | p :: r ->
      let pk = tid_of p in
      kids_at_pre a p;
      pre_sub_ids a p pk;
      (if mem pk (ids b) then lookup_kids_is b pk else ());
      if pk = q then () else changed_listed b a r q

let short_eq (l m:list string)
  : Lemma (requires no_dups l /\ (forall (z:string). mem z l == mem z m) /\ not (more_than_one m))
          (ensures l == m)
  = match m with
    | [] -> (match l with
             | [] -> ()
             | h :: _ -> assert (mem h l))
    | [u] -> (match l with
              | [] -> assert (mem u m)
              | [h] -> assert (mem h l)
              | h1 :: h2 :: _ -> assert (mem h1 l); assert (mem h2 l))

#push-options "--z3rlimit 200 --fuel 2 --ifuel 1"
let inv4_kids_mem (b a:tree) (ps:list string) (t:tree) (pid:string) (z:string)
  : Lemma (requires inv4 b a ps t /\ mem pid (ids a))
          (ensures mem z (kids_at pid t) == mem z (kids_at pid a))
  = if mem z (ids a) then ()
    else (if mem z (kids_at pid a) then kids_in_ids a pid z else ())

let reorder_step (b a:tree) (pid:string) (r:list string) (t:tree) (pn:tree)
  : Lemma (requires wf a /\ inv4 b a (pid :: r) t /\ find_in pid a == Some pn)
          (ensures (match apply (ReorderChildren pid (kid_ids (kids_of pn))) t with
                    | Ok t' -> inv4 b a r t'
                    | Error _ -> False))
  = let ord = kid_ids (kids_of pn) in
    find_in_some_iff pid a;
    kids_at_no_dups pid t;
    kids_at_no_dups pid a;
    FStar.Classical.forall_intro (FStar.Classical.move_requires (inv4_kids_mem b a (pid :: r) t pid));
    no_dups_same_multiset (kids_at pid t) ord;
    let t' = reorder_at pid ord t in
    apply_preserves_wf (ReorderChildren pid ord) t;
    tid_reorder pid ord t;
    let aux_v (q:string)
      : Lemma (apply (ReorderChildren pid ord) t == Ok t' /\
               kids_at q t' == (if q = pid then ord else kids_at q t) /\
               kind_at q t' == kind_at q t /\
               mem q (ids t') == mem q (ids t))
      = reorder_view pid ord t q
    in
    FStar.Classical.forall_intro aux_v;
    aux_v pid

let reorder_short (b a:tree) (pid:string) (r:list string) (t:tree) (pn:tree)
  : Lemma (requires wf a /\ inv4 b a (pid :: r) t /\ find_in pid a == Some pn /\
                    not (more_than_one (kids_of pn)))
          (ensures inv4 b a r t)
  = find_in_some_iff pid a;
    kids_at_no_dups pid t;
    FStar.Classical.forall_intro (FStar.Classical.move_requires (inv4_kids_mem b a (pid :: r) t pid));
    (match kids_of pn with
     | [] -> ()
     | [_] -> ()
     | _ -> ());
    short_eq (kids_at pid t) (kids_at pid a)

let rec reorders_run (b a:tree) (ps:list string) (t:tree)
  : Lemma (requires wf a /\ inv4 b a ps t)
          (ensures (match apply_all (pass_reorders a ps) t with
                    | Ok t' -> inv4 b a [] t'
                    | Error _ -> False))
          (decreases ps)
  = match ps with
    | [] -> ()
    | pid :: r ->
      (match find_in pid a with
       | Some pn ->
         if more_than_one (kids_of pn) then begin
           reorder_step b a pid r t pn;
           (match apply (ReorderChildren pid (kid_ids (kids_of pn))) t with
            | Ok t' -> reorders_run b a r t'
            | Error _ -> ())
         end
         else begin
           reorder_short b a pid r t pn;
           reorders_run b a r t
         end
       | None ->
         find_in_some_iff pid a;
         reorders_run b a r t)
#pop-options

(* ================= two trees that agree node for node are the same tree ================= *)

let kid_found (t s c:tree)
  : Lemma (requires wf t /\ find_in (tid_of s) t == Some s /\ mem c (kids_of s))
          (ensures find_in (tid_of c) t == Some c)
  = find_in_wf (tid_of s) t s;
    (match s with
     | TNode si sk scs ->
       find_all_kid scs c;
       kid_ids_of_mem c scs;
       kid_ids_sub (tid_of c) scs);
    find_in_trans (tid_of s) (tid_of c) t s

let rec tree_ext (a f:tree) (s1 s2:tree)
  : Lemma (requires wf a /\ wf f /\ find_in (tid_of s1) a == Some s1 /\
                    find_in (tid_of s1) f == Some s2 /\
                    (forall (q:string). mem q (ids a) ==>
                       kids_at q f == kids_at q a /\ kind_at q f == kind_at q a))
          (ensures s1 == s2) (decreases s1)
  = find_in_some_iff (tid_of s1) a;
    find_in_id (tid_of s1) f s2;
    match s1, s2 with
    | TNode i k cs1, TNode i2 k2 cs2 -> tree_ext_all a f s1 s2 cs1 cs2
and tree_ext_all (a f:tree) (s1 s2:tree) (l1 l2:list tree)
  : Lemma (requires wf a /\ wf f /\ find_in (tid_of s1) a == Some s1 /\
                    find_in (tid_of s2) f == Some s2 /\ l1 << s1 /\
                    (forall (c:tree). mem c l1 ==> mem c (kids_of s1)) /\
                    (forall (c:tree). mem c l2 ==> mem c (kids_of s2)) /\
                    kid_ids l1 == kid_ids l2 /\
                    (forall (q:string). mem q (ids a) ==>
                       kids_at q f == kids_at q a /\ kind_at q f == kind_at q a))
          (ensures l1 == l2) (decreases l1)
  = match l1, l2 with
    | c1 :: r1, c2 :: r2 ->
      kid_found a s1 c1;
      kid_found f s2 c2;
      tree_ext a f c1 c2;
      tree_ext_all a f s1 s2 r1 r2
    | _, _ -> ()

(* ================= THE TWO THEOREMS ================= *)

#push-options "--z3rlimit 200 --fuel 2 --ifuel 1"
let diff_run (b a:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a)
          (ensures (match apply_all (script_of (diff_blocks b a)) b with
                    | Ok f -> inv4 b a [] f
                    | Error _ -> False))
  = let a_par = parent_map (pre a) in
    let b_par = parent_map (pre b) in
    let b_kids = kid_map (pre b) in
    let p1 = pass_inserts a_par (ids b) (pre a) in
    let pm = pass_moves (ids b) b_par b_kids (pre a) in
    let p2 = fst pm in
    let p3 = pass_removes (ids a) b_par (pre b) in
    let p4 = pass_reorders a (snd pm) in
    apply_all_app p1 (app p2 (app p3 p4)) b;
    inv1_init b a;
    inserts_run b a [] (pre a) b;
    match apply_all p1 b with
    | Error _ -> ()
    | Ok t1 ->
      apply_all_app p2 (app p3 p4) t1;
      inv1_to_inv2 b a t1;
      moves_run b a [] (pre a) t1;
      (match apply_all p2 t1 with
       | Error _ -> ()
       | Ok t2 ->
         apply_all_app p3 p4 t2;
         inv2_to_inv3 b a t2;
         ids_is_pre b;
         wf_iff_no_dups b;
         removes_run b a (pre b) t2;
         (match apply_all p3 t2 with
          | Error _ -> ()
          | Ok t3 ->
            ids_is_pre a;
            let listed (q:string)
              : Lemma (mem q (ids a) /\ not (same_kids b a q) ==> mem q (snd pm))
              = FStar.Classical.move_requires (changed_listed b a (pre a)) q
            in
            FStar.Classical.forall_intro listed;
            assert (inv4 b a (snd pm) t3);
            reorders_run b a (snd pm) t3))
#pop-options

(* THE OPERATIONAL `diff_applicable`: `apply` accepts every step of the script `toOps` emits, in
   sequence, against the tree in hand at that step. *)
let diff_applicable (b a:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a)
          (ensures Ok? (to_ops b a) /\ Ok? (apply_all (Ok?._0 (to_ops b a)) b))
  = diff_ok_on_any_wf_pair b a;
    diff_run b a

(* the one premise reconstruction needs beyond the diff own: a shared id names the same KIND in
   both trees, since no skeleton operation edits a node *)
let kinds_agree (b a:tree) : prop =
  forall (q:string). mem q (ids a) /\ mem q (ids b) ==> kind_at q b == kind_at q a

(* `diff_reconstructs`: applying the script to `before` yields `after` - the tree, not a tree
   like it. *)
let diff_reconstructs (b a:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ kinds_agree b a)
          (ensures Ok? (to_ops b a) /\ apply_all (Ok?._0 (to_ops b a)) b == Ok a)
  = diff_ok_on_any_wf_pair b a;
    diff_run b a;
    match apply_all (script_of (diff_blocks b a)) b with
    | Error _ -> ()
    | Ok f ->
      find_in_self (tid_of a) a;
      find_in_self (tid_of a) f;
      tree_ext a f a f

(* ---- and the two are NOT VACUOUS: a pair whose diff carries an insert, a move, a remove and a
   reorder, reconstructed by evaluation ---- *)

let rec_before : tree =
  TNode "root" "doc" [ TNode "x" "sec" [ TNode "p" "para" [] ]; TNode "y" "para" [] ]

let rec_after : tree =
  TNode "root" "doc" [ TNode "q" "sec" [ TNode "p" "para" [] ]; TNode "y" "para" [] ]

let reconstruction_is_not_vacuous ()
  : Lemma (ensures to_ops rec_before rec_after ==
                     Ok [ InsertChild "root" (TNode "q" "sec" []);
                          MoveNode "p" "q";
                          RemoveNode "x";
                          ReorderChildren "root" [ "q"; "y" ] ] /\
                   apply_all (Ok?._0 (to_ops rec_before rec_after)) rec_before == Ok rec_after /\
                   apply_all (Ok?._0 (to_ops pos_before pos_after)) pos_before == Ok pos_after)
  = assert_norm (to_ops rec_before rec_after ==
                   Ok [ InsertChild "root" (TNode "q" "sec" []);
                        MoveNode "p" "q";
                        RemoveNode "x";
                        ReorderChildren "root" [ "q"; "y" ] ]);
    assert_norm (apply_all [ InsertChild "root" (TNode "q" "sec" []);
                             MoveNode "p" "q";
                             RemoveNode "x";
                             ReorderChildren "root" [ "q"; "y" ] ] rec_before == Ok rec_after);
    assert_norm (to_ops pos_before pos_after ==
                   Ok [ InsertChild "root" (TNode "q" "sec" []); MoveNode "p" "q" ]);
    assert_norm (apply_all [ InsertChild "root" (TNode "q" "sec" []); MoveNode "p" "q" ] pos_before
                 == Ok pos_after)
