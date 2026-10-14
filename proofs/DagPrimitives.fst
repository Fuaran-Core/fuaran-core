(*
   DagPrimitives — the lane DAG's remaining boundaries, modelled (fuaran-core Phase 457).

   Phase 437 lifted the lane-DAG primitives to proved in `DagFold` sections 18 to 21 and named
   what its model left out. This module closes those boundaries. It opens `DagFold` and adds no
   premise of its own: every theorem below rests on that module's definitions and on its
   acyclicity witness.

   WHAT IS MODELLED, clause for clause with `src/Fuaran.Core.OpStream.Dag/DagOpStream.fs`:
     1. `Dag.mergeBase` as production computes it NOW — Phase 388's private `deepest`, which sizes
        only the MAXIMAL members of the common set when that set drains completely, and every
        member when it does not (`deepest`, `merge_base_now`).
     2. `Dag.commonBase` over N heads (`common_base`), on the same `deepest`.
     3. The reachability index's own queries — `Reach.mergeBase`, `Reach.commonBase`,
        `Reach.between` and `Reach.tryTopoOrder` — over section 20's slots, with the index's
        `Order` array read as the whole DAG's drain, the order `Reach.ofDag` builds it in.
     4. `cut`, the kept set of a compaction: `forwardFrom` (a depth-first walk along child edges,
        under fuel), `after`, the uncovered-node refusal, the side parents and the band.

   THE SLOT-AGREEMENT ABSTRACTION. A slot's bitset is read as the ids whose bits it sets (section
   20's `rbits`), `Count[s]` as the number of distinct ids in that set, and a scan over slot
   positions (`for j in 0 .. min sl sr`) as a pass over the first head's set. Production's word
   layout (`hasBit`, the 32-bit words, `popCount`) and the position bounds of the scans sit below
   that reading: an id's bit is set in a slot's words exactly when the id is in the set the model
   carries, and every set bit of slot `s` is at a position no greater than `s`. That is the stated
   premise; `Conformance.reachLaws` and the differential sample it.

   WHAT IS PROVED.
     - `merge_base_now_is_merge_base`: over a witnessed closure, production's current
       `mergeBase` answers what Phase 158's model answers, so `merge_base_maximal` is about the
       code that runs (`merge_base_now_maximal`).
     - `common_base_maximal`: `commonBase` over N heads either refuses exactly when no node is
       common to every head, or returns a node common to every head that no other such node
       descends from.
     - `reach_merge_base_agrees`, `reach_common_base_agrees`, `reach_try_topo_agrees`,
       `reach_between_agrees`: the index's four queries answer what the DAG-level functions
       answer.
     - `cut_keep_meets_contract`: the set `cut` keeps satisfies `truncation_ok`, so with
       `DagFold.truncation_preserves_replay` truncation is proved end to end; its refusal names a
       node that is neither behind nor after the checkpoint's node; and its fuel never runs out.

   WHAT IS NOT MODELLED. `Reach.extend`'s copy-and-shift of the order and position arrays (the
   index here is the one `Reach.ofDag` builds; an extended index's ORDER is sampled by
   `Conformance.reachLaws`), the word layout above, and which uncovered node production names
   beyond "the smallest id" (`Map.toList` order, read as `pick_min`).

   NO SMT PATTERNS, for DagFold section 11's reason.

   Apache-2.0, like everything beside it.
*)
module DagPrimitives

open DagFold

(* ======================================================================================
   1. `deepest`, and `Dag.mergeBase` as production computes it now (Phase 388)
   ====================================================================================== *)

(* F#: `common |> Set.toList |> List.collect (fun id -> dag.Nodes[id].Parents)`. *)
let rec parents_of_all (#op:eqtype) (d:dag op) (ids:list string) : Tot (list string) =
  match ids with
  | [] -> []
  | x :: t ->
    (match lookup d.nodes x with
     | Found n -> app n.nparents (parents_of_all d t)
     | Missing -> parents_of_all d t)

(* Every id of `l` is in `m`. *)
let rec all_in (l m:list string) : Tot bool =
  match l with
  | [] -> true
  | x :: t -> mem x m && all_in t m

(* F#: `deepest`. `drainNodes` over the common set's nodes leaving nothing unplaced is the drain
   placing every id of the set; then only the members outside the closure of every member's
   parents (`below`) are sized, otherwise every member is. `List.maxBy` over the key (closure
   size, id) is section 14.6's `max_by` at `deeper`. A non-empty set that drains completely always
   has a maximal member; the model's fallback for an empty candidate list is never reached there
   and is spelled only to keep the function total. *)
let not_in (below:list string) (x:string) : Tot bool = not (mem x below)

let deepest_cands (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op)) (d:dag op)
  (fuel:list (node op)) (common:list string) : Tot (list string) =
  let ns = closure_nodes d.nodes common in
  if all_in (ids_of ns) (drain_order lt kfuel ns) then
    (match fuel with
     | [] -> common
     | _ :: f' -> keep (not_in (ancestors_all d f' (parents_of_all d common))) common)
  else common

let deepest (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op)) (d:dag op)
  (fuel:list (node op)) (c0:string) (t:list string) : Tot string =
  match deepest_cands lt kfuel d fuel (c0 :: t) with
  | [] -> max_by (deeper lt d fuel) c0 t
  | b :: r -> max_by (deeper lt d fuel) b r

(* F#: `Dag.mergeBase`, as it reads since Phase 388. *)
let merge_base_now (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op)) (d:dag op)
  (fuel:list (node op)) (left right:string) : Tot (found string) =
  match inter (ancestors_of d fuel left) (ancestors_of d fuel right) with
  | [] -> Missing
  | c :: t -> Found (deepest lt kfuel d fuel c t)

(* ---- 1.1 the list facts ---- *)

let rec before_irrefl (x:string) (l:list string)
  : Lemma (requires distinct l) (ensures not (before x x l))
  = match l with
    | [] -> ()
    | _ :: t -> before_irrefl x t

let rec anc_all_app (#op:eqtype) (d:dag op) (f:list (node op)) (l m:list string) (x:string)
  : Lemma (ensures mem x (ancestors_all d f (app l m))
                   == (mem x (ancestors_all d f l) || mem x (ancestors_all d f m)))
          (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> anc_all_app d f t m x

(* `below` holds nothing that is not below some member: in the closure of that member's own
   parents. *)
let rec below_member (#op:eqtype) (d:dag op) (f:list (node op)) (cs:list string) (x:string)
  : Lemma (requires forall (c:string) (n:node op).
                      mem c cs /\ lookup d.nodes c == Found n ==> not (mem x (ancestors_all d f n.nparents)))
          (ensures not (mem x (ancestors_all d f (parents_of_all d cs))))
          (decreases cs)
  = match cs with
    | [] -> ()
    | c :: t ->
      (match lookup d.nodes c with
       | Found n ->
         anc_all_app d f n.nparents (parents_of_all d t) x;
         below_member d f t x
       | Missing -> below_member d f t x)

let rec len_same_members (l m:list string)
  : Lemma (requires distinct l /\ distinct m /\ (forall (x:string). mem x l == mem x m))
          (ensures len l == len m)
          (decreases l)
  = match l with
    | [] -> (match m with | [] -> () | y :: _ -> assert (mem y m))
    | x :: t ->
      let m' = remove_first x m in
      distinct_remove_first m x;
      let aux (z:string) : Lemma (mem z m' == mem z t) = mem_remove_first m x z in
      FStar.Classical.forall_intro aux;
      len_same_members t m';
      let rec len_rf (k:list string)
        : Lemma (requires mem x k) (ensures len (remove_first x k) + 1 == len k) (decreases k)
        = match k with
          | [] -> ()
          | h :: kt -> if h = x then () else len_rf kt
      in
      len_rf m

(* ---- 1.2 `max_by` under a strict total order is the top of the SET it ranges over ---- *)

let strict_total (better:string -> string -> bool) : prop =
  (forall (x:string). not (better x x)) /\
  (forall (x y z:string). better x y /\ better y z ==> better x z) /\
  (forall (x y:string). x =!= y ==> (better x y \/ better y x))

let max_by_members (better:string -> string -> bool) (b1:string) (l1:list string)
  (b2:string) (l2:list string)
  : Lemma (requires strict_total better /\
                    (forall (x:string). mem x (b1 :: l1) == mem x (b2 :: l2)))
          (ensures max_by better b1 l1 == max_by better b2 l2)
  = max_by_top better b1 l1;
    max_by_top better b2 l2

(* Two comparisons that agree on every pair of members pick the same member. *)
let rec max_by_congr (b1 b2:string -> string -> bool) (best:string) (l:list string)
  : Lemma (requires forall (x y:string). mem x (best :: l) /\ mem y (best :: l) ==> b1 x y == b2 x y)
          (ensures max_by b1 best l == max_by b2 best l)
          (decreases l)
  = match l with
    | [] -> ()
    | x :: t -> if b1 x best then max_by_congr b1 b2 x t else max_by_congr b1 b2 best t

(* ---- 1.3 over a witnessed closure, the winner is maximal, and `deepest` finds it ---- *)

let witnessed (#op:eqtype) (d:dag op) (fuel:list (node op)) (h:string) (w:list string) : prop =
  Cons? fuel /\ walk_ok d fuel h /\ is_topo_enum (closure_nodes d.nodes (ancestors_of d fuel h)) w

let within_closure (#op:eqtype) (d:dag op) (fuel:list (node op)) (h:string) (cs:list string) : prop =
  forall (x:string). mem x cs ==> mem x (ancestors_of d fuel h)

(* No member of a witnessed set other than `max_by`'s answer has that answer below it. *)
#push-options "--z3rlimit 100"
let max_by_maximal (#op:eqtype) (lt:string -> string -> bool) (d:dag op) (fuel:list (node op))
  (h:string) (w:list string) (c0:string) (t:list string) (c:string)
  : Lemma (requires total_order lt /\ witnessed d fuel h w /\ within_closure d fuel h (c0 :: t) /\
                    mem c (c0 :: t) /\ c =!= max_by (deeper lt d fuel) c0 t)
          (ensures not (mem (max_by (deeper lt d fuel) c0 t) (ancestors_of d fuel c)))
  = deeper_strict_total lt d fuel;
    max_by_top (deeper lt d fuel) c0 t;
    let b = max_by (deeper lt d fuel) c0 t in
    at_least_refl fuel;
    if mem b (ancestors_of d fuel c) then begin
      anc_found d fuel h c;
      let Found m = lookup d.nodes c in
      lookup_found d.nodes c m;
      walk_ok_anc d fuel fuel h c;
      off_cycle_in_witness d fuel h w m;
      strictly_deeper lt d fuel m b
    end
#pop-options

(* The member `max_by` picks is not below any member — so it survives `deepest`'s filter. *)
#push-options "--z3rlimit 150"
let top_not_below (#op:eqtype) (lt:string -> string -> bool) (d:dag op) (fuel:list (node op))
  (h:string) (w:list string) (c0:string) (t:list string)
  : Lemma (requires total_order lt /\ witnessed d fuel h w /\ within_closure d fuel h (c0 :: t))
          (ensures (match fuel with
                    | [] -> True
                    | _ :: f' ->
                      not (mem (max_by (deeper lt d fuel) c0 t)
                               (ancestors_all d f' (parents_of_all d (c0 :: t))))))
  = match fuel with
    | [] -> ()
    | _ :: f' ->
      let b = max_by (deeper lt d fuel) c0 t in
      deeper_strict_total lt d fuel;
      max_by_top (deeper lt d fuel) c0 t;
      let aux (c:string) (n:node op)
        : Lemma (mem c (c0 :: t) /\ lookup d.nodes c == Found n ==>
                 not (mem b (ancestors_all d f' n.nparents))) =
        if mem c (c0 :: t) && lookup d.nodes c = Found n && mem b (ancestors_all d f' n.nparents)
        then begin
          lookup_found d.nodes c n;
          at_least_refl fuel;
          walk_ok_anc d fuel fuel h c;
          assert (mem b (ancestors_of d fuel c));
          if c = b then begin
            at_least_tail fuel fuel;
            anc_all_before d fuel f' h w n n.nparents b;
            before_irrefl b w
          end
          else max_by_maximal lt d fuel h w c0 t c
        end
      in
      FStar.Classical.forall_intro_2 aux;
      below_member d f' (c0 :: t) b
#pop-options

#push-options "--z3rlimit 100"
let deepest_is_max_by (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (h:string) (w:list string) (c0:string) (t:list string)
  : Lemma (requires total_order lt /\ witnessed d fuel h w /\ within_closure d fuel h (c0 :: t))
          (ensures deepest lt kfuel d fuel c0 t == max_by (deeper lt d fuel) c0 t)
  = let better = deeper lt d fuel in
    let b = max_by better c0 t in
    deeper_strict_total lt d fuel;
    max_by_top better c0 t;
    top_not_below lt d fuel h w c0 t;
    let common = c0 :: t in
    let cands = deepest_cands lt kfuel d fuel common in
    let sub (y:string) : Lemma (mem y cands ==> mem y common) =
      match fuel with
      | [] -> ()
      | _ :: f' -> mem_keep (not_in (ancestors_all d f' (parents_of_all d common))) common y
    in
    FStar.Classical.forall_intro sub;
    let inb () : Lemma (mem b cands) =
      match fuel with
      | [] -> ()
      | _ :: f' -> mem_keep (not_in (ancestors_all d f' (parents_of_all d common))) common b
    in
    inb ();
    match cands with
    | [] -> ()
    | b' :: r ->
      let aux (y:string) : Lemma (mem y (b' :: r) /\ y =!= b ==> (better b y /\ not (better y b))) =
        () in
      FStar.Classical.forall_intro aux;
      max_by_is better b' r b
#pop-options

(* ---- 1.4 THEOREM: production's `mergeBase` is the modelled one, on a witnessed closure ---- *)

let merge_base_now_is_merge_base (#op:eqtype) (lt:string -> string -> bool)
  (kfuel:list (node op)) (d:dag op) (fuel:list (node op)) (left right:string) (wl:list string)
  : Lemma (requires total_order lt /\ witnessed d fuel left wl)
          (ensures merge_base_now lt kfuel d fuel left right == merge_base lt d fuel left right)
  = match inter (ancestors_of d fuel left) (ancestors_of d fuel right) with
    | [] -> ()
    | c :: t -> deepest_is_max_by lt kfuel d fuel left wl c t

(* THEOREM 5, restated over the code that runs. *)
let merge_base_now_maximal (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (left right:string) (wl:list string)
  : Lemma (requires total_order lt /\ witnessed d fuel left wl)
          (ensures (match merge_base_now lt kfuel d fuel left right with
                    | Missing -> forall (x:string). not (common_anc d fuel left right x)
                    | Found b ->
                      common_anc d fuel left right b /\
                      (forall (c:string). common_anc d fuel left right c /\ c =!= b
                                          ==> not (mem b (ancestors_of d fuel c)))))
  = merge_base_now_is_merge_base lt kfuel d fuel left right wl;
    merge_base_maximal lt d fuel left right wl

(* ======================================================================================
   2. `Dag.commonBase` over N heads (Phase 311)
   ====================================================================================== *)

(* F#: `List.distinct` — first occurrences kept, in order. *)
let rec distinct_go (seen l:list string) : Tot (list string) (decreases l) =
  match l with
  | [] -> []
  | x :: t -> if mem x seen then distinct_go seen t else x :: distinct_go (x :: seen) t

(* F#: the `List.fold (fun acc x -> Set.intersect acc (ancestorsOf dag x))`. *)
let rec inter_all (#op:eqtype) (d:dag op) (fuel:list (node op)) (acc:list string)
  (heads:list string) : Tot (list string) (decreases heads) =
  match heads with
  | [] -> acc
  | x :: t -> inter_all d fuel (inter acc (ancestors_of d fuel x)) t

(* F#: `Dag.commonBase`. *)
let common_base (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op)) (d:dag op)
  (fuel:list (node op)) (heads:list string) : Tot (found string) =
  match distinct_go [] heads with
  | [] -> Missing
  | h :: rest ->
    (match inter_all d fuel (ancestors_of d fuel h) rest with
     | [] -> Missing
     | c :: t -> Found (deepest lt kfuel d fuel c t))

(* `x` is in the closure of every head. *)
let rec in_all (#op:eqtype) (d:dag op) (fuel:list (node op)) (heads:list string) (x:string)
  : Tot bool =
  match heads with
  | [] -> true
  | h :: t -> mem x (ancestors_of d fuel h) && in_all d fuel t x

let rec in_all_mem (#op:eqtype) (d:dag op) (fuel:list (node op)) (heads:list string) (x h:string)
  : Lemma (requires in_all d fuel heads x /\ mem h heads) (ensures mem x (ancestors_of d fuel h))
  = match heads with
    | [] -> ()
    | g :: t -> if g = h then () else in_all_mem d fuel t x h

let rec in_all_intro (#op:eqtype) (d:dag op) (fuel:list (node op)) (heads hs:list string) (x:string)
  : Lemma (requires (forall (h:string). mem h heads ==> mem h hs) /\ in_all d fuel hs x)
          (ensures in_all d fuel heads x)
          (decreases heads)
  = match heads with
    | [] -> ()
    | h :: t -> in_all_mem d fuel hs x h; in_all_intro d fuel t hs x

let rec mem_distinct_go (seen l:list string) (x:string)
  : Lemma (ensures mem x (distinct_go seen l) == (mem x l && not (mem x seen))) (decreases l)
  = match l with
    | [] -> ()
    | y :: t -> mem_distinct_go seen t x; mem_distinct_go (y :: seen) t x

let rec mem_inter_all (#op:eqtype) (d:dag op) (fuel:list (node op)) (acc heads:list string)
  (x:string)
  : Lemma (ensures mem x (inter_all d fuel acc heads) == (mem x acc && in_all d fuel heads x))
          (decreases heads)
  = match heads with
    | [] -> ()
    | h :: t -> mem_inter_all d fuel (inter acc (ancestors_of d fuel h)) t x

(* The common set `common_base` ranks: exactly the nodes in every head's closure. *)
let common_members (#op:eqtype) (d:dag op) (fuel:list (node op)) (heads:list string) (x:string)
  : Lemma (requires Cons? (distinct_go [] heads))
          (ensures (let h :: rest = distinct_go [] heads in
                    mem x (inter_all d fuel (ancestors_of d fuel h) rest) == in_all d fuel heads x))
  = let hs = distinct_go [] heads in
    let h :: rest = hs in
    let same (y:string) : Lemma (mem y hs == mem y heads) = mem_distinct_go [] heads y in
    FStar.Classical.forall_intro same;
    mem_inter_all d fuel (ancestors_of d fuel h) rest x;
    if in_all d fuel heads x then in_all_intro d fuel hs heads x;
    if in_all d fuel hs x then in_all_intro d fuel heads hs x

let common_base_no_heads (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op))
  : Lemma (ensures common_base lt kfuel d fuel [] == Missing)
  = ()

(* THEOREM. `commonBase` over N heads: with any one head's closure witnessed acyclic, it refuses
   exactly when no node is common to every head, and otherwise returns a node common to every
   head that no other such node descends from. *)
#push-options "--z3rlimit 100"
let common_base_maximal (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (heads:list string) (h0:string) (w0:list string)
  : Lemma (requires total_order lt /\ mem h0 heads /\ witnessed d fuel h0 w0)
          (ensures (match common_base lt kfuel d fuel heads with
                    | Missing -> forall (x:string). not (in_all d fuel heads x)
                    | Found b ->
                      in_all d fuel heads b /\
                      (forall (c:string). in_all d fuel heads c /\ c =!= b
                                          ==> not (mem b (ancestors_of d fuel c)))))
  = mem_distinct_go [] heads h0;
    let h :: rest = distinct_go [] heads in
    let cm (x:string) : Lemma (mem x (inter_all d fuel (ancestors_of d fuel h) rest)
                               == in_all d fuel heads x) =
      common_members d fuel heads x in
    FStar.Classical.forall_intro cm;
    match inter_all d fuel (ancestors_of d fuel h) rest with
    | [] -> ()
    | c0 :: t ->
      let wc (x:string) : Lemma (mem x (c0 :: t) ==> mem x (ancestors_of d fuel h0)) =
        if mem x (c0 :: t) then in_all_mem d fuel heads x h0 in
      FStar.Classical.forall_intro wc;
      deepest_is_max_by lt kfuel d fuel h0 w0 c0 t;
      deeper_strict_total lt d fuel;
      max_by_top (deeper lt d fuel) c0 t;
      let mx (c:string)
        : Lemma (in_all d fuel heads c /\ c =!= max_by (deeper lt d fuel) c0 t
                 ==> not (mem (max_by (deeper lt d fuel) c0 t) (ancestors_of d fuel c))) =
        if in_all d fuel heads c && c <> max_by (deeper lt d fuel) c0 t then
          max_by_maximal lt d fuel h0 w0 c0 t c
      in
      FStar.Classical.forall_intro mx
#pop-options

(* ======================================================================================
   3. The reachability index's own queries (Phase 289), over section 20's slots
   ====================================================================================== *)

(* F#: `Count[s]` — the slot's set, each id once. Under the slot-agreement abstraction this is
   `popCount` of the slot's words. An unslotted id is never asked. *)
let reach_count (r:list rslot) (x:string) : Tot (list string) =
  match slot_of r x with
  | Found b -> dedup b
  | Missing -> []

(* F#: the scans' comparison `Count[j] > Count[best] || (Count[j] = Count[best] &&
   CompareOrdinal(Ids[j], Ids[best]) > 0)` — "j's key is greater", section 14.6's `deeper` read
   off the index. *)
let reach_deeper (lt:string -> string -> bool) (r:list rslot) (x y:string) : Tot bool =
  let cx = reach_count r x in
  let cy = reach_count r y in
  if shorter cy cx then true
  else if shorter cx cy then false
  else lt y x

(* F#: `Reach.mergeBase`. Both ids slotted: the slots set in both bitsets, ranked by the index's
   key; otherwise `Dag.mergeBase`. An id the DAG does not hold refuses first. *)
let reach_merge_base (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (r:list rslot) (left right:string) : Tot (found string) =
  match lookup d.nodes left with
  | Missing -> Missing
  | Found _ ->
    (match lookup d.nodes right with
     | Missing -> Missing
     | Found _ ->
       (match slot_of r left with
        | Missing -> merge_base_now lt kfuel d fuel left right
        | Found bl ->
          (match slot_of r right with
           | Missing -> merge_base_now lt kfuel d fuel left right
           | Found br ->
             (match inter bl br with
              | [] -> Missing
              | c :: t -> Found (max_by (reach_deeper lt r) c t)))))

let rec all_held (#op:eqtype) (d:dag op) (hs:list string) : Tot bool =
  match hs with
  | [] -> true
  | h :: t -> Found? (lookup d.nodes h) && all_held d t

let rec all_slotted (r:list rslot) (hs:list string) : Tot bool =
  match hs with
  | [] -> true
  | h :: t -> Found? (slot_of r h) && all_slotted r t

(* F#: `ss |> List.forall (fun s -> hasBit reach.Bits[s] j)`. *)
let rec in_all_bits (r:list rslot) (hs:list string) (x:string) : Tot bool =
  match hs with
  | [] -> true
  | h :: t ->
    (match slot_of r h with
     | Found b -> mem x b
     | Missing -> false) && in_all_bits r t x

(* F#: `Reach.commonBase`. The scan over slots `0 .. top` is read as a pass over the first head's
   set: a slot set in every head's bitset is set in the first one's. *)
let reach_common_base (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (r:list rslot) (heads:list string) : Tot (found string) =
  match distinct_go [] heads with
  | [] -> Missing
  | h :: rest ->
    if not (all_held d (h :: rest)) then Missing
    else if not (all_slotted r (h :: rest)) then common_base lt kfuel d fuel heads
    else
      (match slot_of r h with
       | Missing -> Missing
       | Found bh ->
         (match keep (in_all_bits r (h :: rest)) bh with
          | [] -> Missing
          | c :: t -> Found (max_by (reach_deeper lt r) c t)))

(* F#: `Dag.tryTopoOrder`'s answer. *)
type topo_result =
  | TopoOk : list string -> topo_result
  | TopoCyclic : string -> topo_result

(* F#: `Dag.tryTopoOrder` — the closure's drain, `Ok` when it placed every id of the closure
   (production compares the drain's length with the closure's size; the drain is distinct and
   inside the closure, so that is the same test). *)
let try_topo_order (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op)) (d:dag op)
  (fuel:list (node op)) (head:string) : Tot topo_result =
  let ord = topo_drain lt kfuel d fuel head in
  if all_in (ids_of (closure_of d fuel head)) ord then TopoOk ord else TopoCyclic head

(* F#: `Reach.tryTopoOrder` — `closureInOrder reach s (fun _ -> true)`: the index's drain order
   `ord` kept to the head's set; an unslotted head falls back to `Dag.tryTopoOrder`. *)
let reach_try_topo_order (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (r:list rslot) (ord:list string) (head:string)
  : Tot topo_result =
  match slot_of r head with
  | Found b -> TopoOk (keep (in_set b) ord)
  | Missing -> try_topo_order lt kfuel d fuel head

(* F#: `closureInOrder`'s `keep` for `between`: in the head's set and not in the base's. *)
let head_not_base (bh bb:list string) (x:string) : Tot bool = mem x bh && not (mem x bb)

(* F#: `Reach.between`. A slotted head whose base is slotted, or not held at all: the index's
   order kept to the head's set minus the base's; otherwise `Dag.between`. *)
let reach_between (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (r:list rslot) (ord:list string) (base_id head:string)
  : Tot (list (node op)) =
  match slot_of r head with
  | Missing -> between_drained lt kfuel d fuel base_id head
  | Found bh ->
    (match slot_of r base_id with
     | Found bb -> nodes_for d (keep (head_not_base bh bb) ord)
     | Missing ->
       (match lookup d.nodes base_id with
        | Missing -> nodes_for d (keep (in_set bh) ord)
        | Found _ -> between_drained lt kfuel d fuel base_id head))

(* ---- 3.1 a slot answers its closure ---- *)

let slot_closure (#op:eqtype) (d:dag op) (r:list rslot) (fuel:list (node op)) (x:string)
  (b:list string) (y:string)
  : Lemma (requires reach_valid d r /\ walk_ok d fuel x /\ slot_of r x == Found b)
          (ensures mem y b == mem y (ancestors_of d fuel x) /\ (mem y b ==> Found? (slot_of r y)))
  = slot_of_mem r x b;
    slot_sound_at d r ({ rid = x; rbits = b }) fuel y

(* The index's key is `deeper`'s, for two ids the walk completes on. *)
let reach_deeper_is_deeper (#op:eqtype) (lt:string -> string -> bool) (d:dag op)
  (r:list rslot) (fuel:list (node op)) (x y:string)
  : Lemma (requires reach_valid d r /\ walk_ok d fuel x /\ walk_ok d fuel y /\
                    Found? (slot_of r x) /\ Found? (slot_of r y))
          (ensures reach_deeper lt r x y == deeper lt d fuel x y)
  = let Found bx = slot_of r x in
    let Found b_y = slot_of r y in
    let ex (z:string) : Lemma (mem z (dedup bx) == mem z (closure_set d fuel x)) =
      slot_closure d r fuel x bx z; mem_dedup bx z; mem_dedup (ancestors_of d fuel x) z in
    let ey (z:string) : Lemma (mem z (dedup b_y) == mem z (closure_set d fuel y)) =
      slot_closure d r fuel y b_y z; mem_dedup b_y z; mem_dedup (ancestors_of d fuel y) z in
    FStar.Classical.forall_intro ex;
    FStar.Classical.forall_intro ey;
    distinct_dedup bx; distinct_dedup b_y;
    distinct_dedup (ancestors_of d fuel x); distinct_dedup (ancestors_of d fuel y);
    len_same_members (dedup bx) (closure_set d fuel x);
    len_same_members (dedup b_y) (closure_set d fuel y);
    shorter_len (dedup b_y) (dedup bx);
    shorter_len (dedup bx) (dedup b_y);
    shorter_len (closure_set d fuel y) (closure_set d fuel x);
    shorter_len (closure_set d fuel x) (closure_set d fuel y)

(* A set of slotted ids the walk completes on, ranked by the index, ranks as `deeper` does. *)
let reach_max_is_max (#op:eqtype) (lt:string -> string -> bool) (d:dag op) (r:list rslot)
  (fuel:list (node op)) (c:string) (t:list string)
  : Lemma (requires reach_valid d r /\
                    (forall (x:string). mem x (c :: t) ==> walk_ok d fuel x /\ Found? (slot_of r x)))
          (ensures max_by (reach_deeper lt r) c t == max_by (deeper lt d fuel) c t)
  = let aux (x y:string)
      : Lemma (mem x (c :: t) /\ mem y (c :: t) ==> reach_deeper lt r x y == deeper lt d fuel x y) =
      if mem x (c :: t) && mem y (c :: t) then reach_deeper_is_deeper lt d r fuel x y in
    FStar.Classical.forall_intro_2 aux;
    max_by_congr (reach_deeper lt r) (deeper lt d fuel) c t

(* ---- 3.2 THEOREM: `Reach.mergeBase` is `Dag.mergeBase` ---- *)

#push-options "--z3rlimit 100"
let reach_merge_base_agrees (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (r:list rslot) (left right:string) (wl:list string)
  : Lemma (requires total_order lt /\ reach_valid d r /\ witnessed d fuel left wl /\
                    walk_ok d fuel right)
          (ensures reach_merge_base lt kfuel d fuel r left right
                   == merge_base_now lt kfuel d fuel left right)
  = merge_base_now_is_merge_base lt kfuel d fuel left right wl;
    let al = ancestors_of d fuel left in
    let ar = ancestors_of d fuel right in
    match lookup d.nodes left with
    | Missing -> assert (al == [])
    | Found _ ->
      match lookup d.nodes right with
      | Missing ->
        assert (ar == []);
        (match inter al ar with | [] -> () | c :: _ -> mem_inter c al ar)
      | Found _ ->
        match slot_of r left with
        | Missing -> ()
        | Found bl ->
          match slot_of r right with
          | Missing -> ()
          | Found br ->
            at_least_refl fuel;
            let same (x:string) : Lemma (mem x (inter bl br) == mem x (inter al ar)) =
              slot_closure d r fuel left bl x; slot_closure d r fuel right br x;
              mem_inter x bl br; mem_inter x al ar in
            FStar.Classical.forall_intro same;
            match inter bl br with
            | [] -> (match inter al ar with | [] -> () | c :: _ -> assert (mem c (inter al ar)))
            | c :: t ->
              match inter al ar with
              | [] -> assert (mem c (inter bl br))
              | c' :: t' ->
                let ok (x:string) : Lemma (mem x (c :: t) ==> walk_ok d fuel x /\ Found? (slot_of r x)) =
                  if mem x (c :: t) then begin
                    slot_closure d r fuel left bl x;
                    mem_inter x bl br;
                    walk_ok_anc d fuel fuel left x
                  end in
                FStar.Classical.forall_intro ok;
                reach_max_is_max lt d r fuel c t;
                deeper_strict_total lt d fuel;
                max_by_members (deeper lt d fuel) c t c' t'
#pop-options

(* ---- 3.3 THEOREM: `Reach.commonBase` is `Dag.commonBase` ---- *)

let rec all_held_mem (#op:eqtype) (d:dag op) (hs:list string)
  : Lemma (ensures all_held d hs == (forall (h:string). mem h hs ==> Found? (lookup d.nodes h)))
  = match hs with
    | [] -> ()
    | _ :: t -> all_held_mem d t

let rec all_slotted_mem (r:list rslot) (hs:list string)
  : Lemma (ensures all_slotted r hs == (forall (h:string). mem h hs ==> Found? (slot_of r h)))
  = match hs with
    | [] -> ()
    | _ :: t -> all_slotted_mem r t

let rec in_all_bits_is_in_all (#op:eqtype) (d:dag op) (r:list rslot) (fuel:list (node op))
  (hs:list string) (x:string)
  : Lemma (requires reach_valid d r /\
                    (forall (h:string). mem h hs ==> walk_ok d fuel h /\ Found? (slot_of r h)))
          (ensures in_all_bits r hs x == in_all d fuel hs x)
  = match hs with
    | [] -> ()
    | h :: t ->
      let Found b = slot_of r h in
      slot_closure d r fuel h b x;
      in_all_bits_is_in_all d r fuel t x

let rec unheld_empty (#op:eqtype) (d:dag op) (fuel:list (node op)) (hs:list string) (x:string)
  : Lemma (requires Cons? fuel /\ not (all_held d hs)) (ensures not (in_all d fuel hs x))
  = match hs with
    | [] -> ()
    | h :: t -> if Found? (lookup d.nodes h) then unheld_empty d fuel t x else ()

#push-options "--z3rlimit 150"
let reach_common_base_agrees (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (r:list rslot) (heads:list string) (h0:string)
  (w0:list string)
  : Lemma (requires total_order lt /\ reach_valid d r /\ mem h0 heads /\ witnessed d fuel h0 w0 /\
                    (forall (h:string). mem h heads ==> walk_ok d fuel h))
          (ensures reach_common_base lt kfuel d fuel r heads == common_base lt kfuel d fuel heads)
  = mem_distinct_go [] heads h0;
    let hs = distinct_go [] heads in
    let h :: rest = hs in
    let same (y:string) : Lemma (mem y hs == mem y heads) = mem_distinct_go [] heads y in
    FStar.Classical.forall_intro same;
    let common = inter_all d fuel (ancestors_of d fuel h) rest in
    let cm (x:string) : Lemma (mem x common == in_all d fuel heads x) =
      common_members d fuel heads x in
    FStar.Classical.forall_intro cm;
    let hs_heads (x:string) : Lemma (in_all d fuel hs x == in_all d fuel heads x) =
      (if in_all d fuel heads x then in_all_intro d fuel hs heads x);
      (if in_all d fuel hs x then in_all_intro d fuel heads hs x) in
    FStar.Classical.forall_intro hs_heads;
    if not (all_held d hs) then begin
      let none (x:string) : Lemma (not (in_all d fuel hs x)) = unheld_empty d fuel hs x in
      FStar.Classical.forall_intro none;
      match common with
      | [] -> ()
      | c :: _ -> assert (mem c common)
    end
    else if not (all_slotted r hs) then ()
    else begin
      all_slotted_mem r hs;
      let Found bh = slot_of r h in
      at_least_refl fuel;
      let cands = keep (in_all_bits r hs) bh in
      let cc (x:string) : Lemma (mem x cands == in_all d fuel heads x) =
        mem_keep (in_all_bits r hs) bh x;
        in_all_bits_is_in_all d r fuel hs x;
        slot_closure d r fuel h bh x;
        (if in_all d fuel hs x then in_all_mem d fuel hs x h) in
      FStar.Classical.forall_intro cc;
      match cands with
      | [] -> (match common with | [] -> () | c :: _ -> assert (mem c common))
      | c :: t ->
        match common with
        | [] -> assert (mem c cands)
        | c' :: t' ->
          let ok (x:string) : Lemma (mem x (c :: t) ==> walk_ok d fuel x /\ Found? (slot_of r x)) =
            if mem x (c :: t) then begin
              mem_keep (in_all_bits r hs) bh x;
              slot_closure d r fuel h bh x;
              walk_ok_anc d fuel fuel h x
            end in
          FStar.Classical.forall_intro ok;
          reach_max_is_max lt d r fuel c t;
          let wc (x:string) : Lemma (mem x (c' :: t') ==> mem x (ancestors_of d fuel h0)) =
            if mem x (c' :: t') then in_all_mem d fuel heads x h0 in
          FStar.Classical.forall_intro wc;
          deepest_is_max_by lt kfuel d fuel h0 w0 c' t';
          deeper_strict_total lt d fuel;
          max_by_members (deeper lt d fuel) c t c' t'
    end
#pop-options

(* ---- 3.4 the index's order, kept to a closure, is that closure's drain ---- *)

let rec keep_ext (f g:string -> bool) (l:list string)
  : Lemma (requires forall (x:string). mem x l ==> f x == g x) (ensures keep f l == keep g l)
  = match l with
    | [] -> ()
    | _ :: t -> keep_ext f g t

let rec diff_is_keep (l m:list string) : Lemma (ensures diff l m == keep (not_in m) l) =
  match l with
  | [] -> ()
  | _ :: t -> diff_is_keep t m

let rec keep_head_not_base (bh bb:list string) (l:list string)
  : Lemma (ensures keep (head_not_base bh bb) l == keep (not_in bb) (keep (in_set bh) l))
  = match l with
    | [] -> ()
    | _ :: t -> keep_head_not_base bh bb t

let rec all_in_intro (l m:list string)
  : Lemma (requires forall (x:string). mem x l ==> mem x m) (ensures all_in l m)
  = match l with
    | [] -> ()
    | _ :: t -> all_in_intro t m

(* A slot `reach_build` hands out is one it was handed or one the order names. *)
let rec slotted_in_order (#op:eqtype) (d:dag op) (r:list rslot) (ord:list string) (x:string)
  : Lemma (requires Found? (slot_of (reach_build d r ord) x))
          (ensures Found? (slot_of r x) \/ mem x ord)
          (decreases ord)
  = match ord with
    | [] -> ()
    | id :: t -> slotted_in_order d (reach_step d r id) t x

(* `DagFold.drain_restricts` with the whole DAG in place of a head's closure: the whole DAG's
   drain — the index's `Order` — kept to a node's closure IS that closure's drain. *)
#push-options "--z3rlimit 150"
let order_restricts (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (h:string)
  : Lemma (requires total_order lt /\ distinct_ids d /\ walk_ok d fuel h /\
                    covers kfuel (ids_of d.nodes))
          (ensures keep (in_set (ids_of (closure_of d fuel h))) (drain_order lt kfuel d.nodes)
                   == drain_order lt kfuel (closure_of d fuel h))
  = let ah = ancestors_of d fuel h in
    let cnh = closure_of d fuel h in
    let cT = ids_of d.nodes in
    let cS = ids_of cnh in
    at_least_refl fuel;
    let sub_ids (x:string) : Lemma (mem x cS ==> mem x cT) =
      mem_ids_closure_nodes d.nodes ah x in
    FStar.Classical.forall_intro sub_ids;
    let ex (n:node op) : Lemma (mem n d.nodes ==> mem n.nid cS == mem n.nid ah) =
      if mem n d.nodes then begin mem_ids_of n d.nodes; mem_ids_closure_nodes d.nodes ah n.nid end in
    FStar.Classical.forall_intro ex;
    closure_nodes_ext d.nodes cS ah;
    assert (cnh == closure_nodes d.nodes cS);
    let dc (n:node op) (p:string)
      : Lemma (mem n d.nodes /\ mem n.nid cS /\ mem p n.nparents /\ mem p cT ==> mem p cS) =
      if mem n d.nodes && mem n.nid cS && mem p n.nparents && mem p cT then begin
        lookup_mem_distinct d.nodes n;
        mem_ids_closure_nodes d.nodes ah n.nid;
        lookup_of_mem_ids d.nodes p;
        let big = n :: n :: fuel in
        at_least_cons n fuel fuel;
        at_least_cons n (n :: fuel) fuel;
        assert (mem p (ancestors_of d (n :: fuel) p));
        anc_all_incl d (n :: fuel) n.nparents p p;
        assert (mem p (ancestors_of d big n.nid));
        anc_trans d fuel big h n.nid p;
        mem_ids_closure_nodes d.nodes ah p
      end
    in
    FStar.Classical.forall_intro_2 dc;
    covers_len kfuel cT;
    len_ids_of d.nodes;
    len_closure_nodes d.nodes ah;
    drain_restrict lt kfuel d.nodes cT [] kfuel cnh cS []
#pop-options

(* Over the index `Reach.ofDag` builds, a slotted node's set kept from the index's order is its
   closure's drain. *)
#push-options "--z3rlimit 100"
let slot_order_is_drain (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (h:string) (b:list string)
  : Lemma (requires total_order lt /\ distinct_ids d /\ walk_ok d fuel h /\
                    covers kfuel (ids_of d.nodes) /\
                    slot_of (reach_of_dag lt kfuel d) h == Found b)
          (ensures keep (in_set b) (drain_order lt kfuel d.nodes)
                   == drain_order lt kfuel (closure_of d fuel h) /\
                   all_in (ids_of (closure_of d fuel h)) (drain_order lt kfuel (closure_of d fuel h)))
  = let r = reach_of_dag lt kfuel d in
    let w = drain_order lt kfuel d.nodes in
    let ah = ancestors_of d fuel h in
    let cS = ids_of (closure_of d fuel h) in
    reach_build_valid d [] w;
    let same (x:string) : Lemma (in_set b x == in_set cS x) =
      slot_closure d r fuel h b x;
      mem_ids_closure_nodes d.nodes ah x;
      held_ids d x;
      if mem x ah then anc_found d fuel h x in
    FStar.Classical.forall_intro same;
    keep_ext (in_set b) (in_set cS) w;
    order_restricts lt kfuel d fuel h;
    let placed (x:string) : Lemma (mem x cS ==> mem x (keep (in_set cS) w)) =
      if mem x cS then begin
        slot_closure d r fuel h b x;
        slotted_in_order d [] w x;
        mem_keep (in_set cS) w x
      end in
    FStar.Classical.forall_intro placed;
    all_in_intro cS (drain_order lt kfuel (closure_of d fuel h))
#pop-options

(* ---- 3.5 THEOREM: `Reach.tryTopoOrder` is `Dag.tryTopoOrder` ---- *)

let reach_try_topo_agrees (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (head:string)
  : Lemma (requires total_order lt /\ distinct_ids d /\ walk_ok d fuel head /\
                    covers kfuel (ids_of d.nodes))
          (ensures reach_try_topo_order lt kfuel d fuel (reach_of_dag lt kfuel d)
                     (drain_order lt kfuel d.nodes) head
                   == try_topo_order lt kfuel d fuel head)
  = match slot_of (reach_of_dag lt kfuel d) head with
    | Missing -> ()
    | Found b -> slot_order_is_drain lt kfuel d fuel head b

(* ---- 3.6 THEOREM: `Reach.between` is `Dag.between` ---- *)

let rec diff_nil (l:list string) : Lemma (ensures diff l [] == l) =
  match l with
  | [] -> ()
  | _ :: t -> diff_nil t

#push-options "--z3rlimit 100"
let reach_between_agrees (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (base_id head:string)
  : Lemma (requires total_order lt /\ distinct_ids d /\ Cons? fuel /\ walk_ok d fuel head /\
                    walk_ok d fuel base_id /\ covers kfuel (ids_of d.nodes))
          (ensures reach_between lt kfuel d fuel (reach_of_dag lt kfuel d)
                     (drain_order lt kfuel d.nodes) base_id head
                   == between_drained lt kfuel d fuel base_id head)
  = let r = reach_of_dag lt kfuel d in
    let w = drain_order lt kfuel d.nodes in
    let td = topo_drain lt kfuel d fuel head in
    let ab = ancestors_of d fuel base_id in
    diff_is_keep td ab;
    match slot_of r head with
    | Missing -> ()
    | Found bh ->
      slot_order_is_drain lt kfuel d fuel head bh;
      assert (keep (in_set bh) w == td);
      match slot_of r base_id with
      | Found bb ->
        reach_build_valid d [] w;
        keep_head_not_base bh bb w;
        let same (x:string) : Lemma (not_in bb x == not_in ab x) =
          slot_closure d r fuel base_id bb x in
        FStar.Classical.forall_intro same;
        keep_ext (not_in bb) (not_in ab) td
      | Missing ->
        match lookup d.nodes base_id with
        | Missing -> assert (ab == []); diff_nil td
        | Found _ -> ()
#pop-options

(* ======================================================================================
   4. `cut` — the set a compaction keeps (Phase 288)
   ====================================================================================== *)

(* F#: `forwardFrom`'s `children` map, read at one id: every node naming `p` as a parent. The map
   is built in another order; the walk below reads it as a set. *)
let rec children_of (#op:eqtype) (ns:list (node op)) (p:string) : Tot (list string) =
  match ns with
  | [] -> []
  | n :: t -> if mem p n.nparents then n.nid :: children_of t p else children_of t p

(* F#: `forwardFrom`'s `go` — a depth-first walk along child edges keeping to `within`. The F#
   loop ends because every push adds an id to the accumulator; the model spends one step of
   `fuel` per id it adds and answers `Missing` if that ever runs out, which
   `cut_keep_meets_contract` proves it never does at the fuel `cut` is modelled with. *)
let rec fwd_go (#op:eqtype) (d:dag op) (within:string -> bool) (fuel:list (node op))
  (acc stack:list string) : Tot (found (list string)) (decreases %[fuel; stack]) =
  match stack with
  | [] -> Found acc
  | id :: rest ->
    if mem id acc || not (within id) then fwd_go d within fuel acc rest
    else
      (match fuel with
       | [] -> Missing
       | _ :: f' -> fwd_go d within f' (id :: acc) (app (children_of d.nodes id) rest))

(* F#: `forwardFrom dag within seeds`. *)
let forward_from (#op:eqtype) (d:dag op) (within:string -> bool) (fuel:list (node op))
  (seeds:list string) : Tot (found (list string)) =
  fwd_go d within fuel [] seeds

let any_id (_:string) : Tot bool = true

let not_eq (c x:string) : Tot bool = not (x = c)

(* F#: the `List.tryFind` predicate — neither behind the node nor after it. *)
let uncovered (behind later:list string) (x:string) : Tot bool =
  not (mem x behind) && not (mem x later)

(* F#: `side`'s filter — a parent other than the node itself, behind the node. *)
let side_parent (c:string) (behind:list string) (p:string) : Tot bool =
  not (p = c) && mem p behind

(* F#: `cut`'s answer. `CutUnderfuelled` is the model's alone and is proved unreachable. *)
type cut_result =
  | CutKeep : list string -> cut_result
  | CutUncovered : string -> cut_result
  | CutUnderfuelled : cut_result

(* F#: `cut`, the kept set rather than the DAG it filters to (that is `DagFold.truncate`). `behind`
   is `ancestorsOf dag node`, `later` is `after dag node` (`forwardFrom` from the node, the node
   removed), the refusal names the smallest uncovered id (`Map.toList` is in id order), and the
   band is `forwardFrom` from the side parents keeping to `behind`. *)
let cut_keep (#op:eqtype) (lt:string -> string -> bool) (d:dag op) (fuel:list (node op))
  (c:string) : Tot cut_result =
  let behind = ancestors_of d fuel c in
  match forward_from d any_id d.nodes [ c ] with
  | Missing -> CutUnderfuelled
  | Found fw ->
    let later = keep (not_eq c) fw in
    (match keep (uncovered behind later) (ids_of d.nodes) with
     | u :: us -> CutUncovered (pick_min lt (u :: us))
     | [] ->
       let side = keep (side_parent c behind) (parents_of_all d later) in
       (match forward_from d (in_set behind) d.nodes side with
        | Missing -> CutUnderfuelled
        | Found band -> CutKeep (app band (app later [ c ]))))

(* ---- 4.1 the walk is closed under child edges ---- *)

let rec mem_children_of (#op:eqtype) (ns:list (node op)) (p:string) (n:node op)
  : Lemma (requires mem n ns /\ mem p n.nparents) (ensures mem n.nid (children_of ns p))
  = match ns with
    | [] -> ()
    | m :: t -> if m = n then () else mem_children_of t p n

let rec children_held (#op:eqtype) (ns:list (node op)) (p x:string)
  : Lemma (requires mem x (children_of ns p)) (ensures mem x (ids_of ns))
  = match ns with
    | [] -> ()
    | m :: t -> if mem p m.nparents && m.nid = x then () else children_held t p x

(* Every child `within` of a member is a member. *)
let closed (#op:eqtype) (d:dag op) (within:string -> bool) (a:list string) : prop =
  forall (x:string) (n:node op).
    mem x a /\ mem n d.nodes /\ mem x n.nparents /\ within n.nid ==> mem n.nid a

(* The walk's invariant: such a child is a member, or still on the stack. *)
let fwd_inv (#op:eqtype) (d:dag op) (within:string -> bool) (a s:list string) : prop =
  forall (x:string) (n:node op).
    mem x a /\ mem n d.nodes /\ mem x n.nparents /\ within n.nid ==> mem n.nid a \/ mem n.nid s

#push-options "--z3rlimit 100"
let rec fwd_closed (#op:eqtype) (d:dag op) (within:string -> bool) (fuel:list (node op))
  (a s:list string)
  : Lemma (requires fwd_inv d within a s)
          (ensures (match fwd_go d within fuel a s with
                    | Missing -> True
                    | Found res ->
                      closed d within res /\ (forall (x:string). mem x a ==> mem x res) /\
                      (forall (x:string). mem x s /\ within x ==> mem x res)))
          (decreases %[fuel; s])
  = match s with
    | [] -> ()
    | id :: rest ->
      if mem id a || not (within id) then begin
        let inv (x:string) (n:node op)
          : Lemma (mem x a /\ mem n d.nodes /\ mem x n.nparents /\ within n.nid
                   ==> mem n.nid a \/ mem n.nid rest) = () in
        FStar.Classical.forall_intro_2 inv;
        fwd_closed d within fuel a rest
      end
      else
        (match fuel with
         | [] -> ()
         | _ :: f' ->
           let s' = app (children_of d.nodes id) rest in
           let inv (x:string) (n:node op)
             : Lemma (mem x (id :: a) /\ mem n d.nodes /\ mem x n.nparents /\ within n.nid
                      ==> mem n.nid (id :: a) \/ mem n.nid s') =
             if mem x (id :: a) && mem n d.nodes && mem x n.nparents && within n.nid then begin
               mem_app n.nid (children_of d.nodes id) rest;
               if x = id then mem_children_of d.nodes id n
             end
           in
           FStar.Classical.forall_intro_2 inv;
           fwd_closed d within f' (id :: a) s')
#pop-options

(* A walk that ran to completion from a member reaches only members, wherever the walk keeps to
   `within`. *)
let rec closed_up (#op:eqtype) (d:dag op) (within:string -> bool) (a:list string)
  (g:list (node op)) (z y:string)
  : Lemma (requires closed d within a /\ mem y a /\ mem y (ancestors_of d g z) /\
                    (forall (w:string). mem w (ancestors_of d g z) ==> within w))
          (ensures mem z a)
          (decreases %[g; (0 <: nat); ([] <: list string)])
  = match g with
    | [] -> ()
    | _ :: g' ->
      (match lookup d.nodes z with
       | Missing -> ()
       | Found n ->
         if y = z then ()
         else begin
           lookup_found d.nodes z n;
           closed_up_all d within a g' n n.nparents y
         end)

and closed_up_all (#op:eqtype) (d:dag op) (within:string -> bool) (a:list string)
  (g:list (node op)) (n:node op) (ps:list string) (y:string)
  : Lemma (requires closed d within a /\ mem y a /\ mem y (ancestors_all d g ps) /\
                    mem n d.nodes /\ within n.nid /\
                    (forall (p:string). mem p ps ==> mem p n.nparents) /\
                    (forall (w:string). mem w (ancestors_all d g ps) ==> within w))
          (ensures mem n.nid a)
          (decreases %[g; (1 <: nat); ps])
  = match ps with
    | [] -> ()
    | p :: t ->
      mem_app y (ancestors_of d g p) (ancestors_all d g t);
      if mem y (ancestors_of d g p) then begin
        let wp (w:string) : Lemma (mem w (ancestors_of d g p) ==> within w) =
          mem_app w (ancestors_of d g p) (ancestors_all d g t) in
        FStar.Classical.forall_intro wp;
        closed_up d within a g p y
      end
      else begin
        let wt (w:string) : Lemma (mem w (ancestors_all d g t) ==> within w) =
          mem_app w (ancestors_of d g p) (ancestors_all d g t) in
        FStar.Classical.forall_intro wt;
        closed_up_all d within a g n t y
      end

(* ---- 4.2 the walk never runs out of fuel ---- *)

#push-options "--z3rlimit 100"
let rec fwd_enough (#op:eqtype) (d:dag op) (within:string -> bool) (fuel:list (node op))
  (a s:list string)
  : Lemma (requires distinct (ids_of d.nodes) /\ distinct a /\
                    (forall (x:string). mem x a ==> mem x (ids_of d.nodes)) /\
                    (forall (x:string). mem x s /\ within x ==> mem x (ids_of d.nodes)) /\
                    len a + len fuel >= len d.nodes)
          (ensures Found? (fwd_go d within fuel a s))
          (decreases %[fuel; s])
  = match s with
    | [] -> ()
    | id :: rest ->
      if mem id a || not (within id) then fwd_enough d within fuel a rest
      else
        (match fuel with
         | [] ->
           pigeon a (ids_of d.nodes) id;
           shorter_len a (ids_of d.nodes);
           len_ids_of d.nodes
         | _ :: f' ->
           let s' = app (children_of d.nodes id) rest in
           let held (x:string) : Lemma (mem x s' /\ within x ==> mem x (ids_of d.nodes)) =
             mem_app x (children_of d.nodes id) rest;
             if mem x (children_of d.nodes id) then children_held d.nodes id x in
           FStar.Classical.forall_intro held;
           fwd_enough d within f' (id :: a) s')
#pop-options

(* ---- 4.3 THEOREM: `cut` meets the contract truncation is proved over ---- *)

let rec trunc_ok_intro (#op:eqtype) (d:dag op) (fuel:list (node op)) (c:string)
  (kept:list string) (ns:list (node op))
  : Lemma (requires forall (n:node op). mem n ns ==>
                      (if mem n.nid (ancestors_of d fuel c)
                       then (not (mem n.nid kept) || mem n.nid (ancestors_of (truncate d kept) fuel c))
                       else mem n.nid kept))
          (ensures trunc_ok_nodes d fuel c kept ns)
  = match ns with
    | [] -> ()
    | _ :: t -> trunc_ok_intro d fuel c kept t

#push-options "--z3rlimit 200"
let cut_keep_meets_contract (#op:eqtype) (lt:string -> string -> bool) (d:dag op)
  (fuel:list (node op)) (c:string)
  : Lemma (requires total_order lt /\ distinct_ids d /\ Cons? fuel /\ walk_ok d fuel c /\
                    Found? (lookup d.nodes c))
          (ensures (match cut_keep lt d fuel c with
                    | CutKeep kept -> truncation_ok d fuel c kept
                    | CutUncovered u ->
                      Found? (lookup d.nodes u) /\ not (mem u (ancestors_of d fuel c)) /\
                      (forall (g:list (node op)). not (mem c (ancestors_of d g u)))
                    | CutUnderfuelled -> False))
  = let behind = ancestors_of d fuel c in
    let ids = ids_of d.nodes in
    at_least_refl fuel;
    held_ids d c;
    assert (mem c behind);
    let held_behind (x:string) : Lemma (mem x behind ==> mem x ids) =
      if mem x behind then begin anc_found d fuel c x; held_ids d x end in
    FStar.Classical.forall_intro held_behind;
    fwd_enough d any_id d.nodes [] [ c ];
    fwd_closed d any_id d.nodes [] [ c ];
    let Found fw = forward_from d any_id d.nodes [ c ] in
    assert (mem c fw);
    let later = keep (not_eq c) fw in
    let in_fw (x:string) : Lemma (mem x fw == (x = c || mem x later)) = mem_keep (not_eq c) fw x in
    FStar.Classical.forall_intro in_fw;
    (* nothing reaches `c` from outside `fw` *)
    let not_after (g:list (node op)) (z:string)
      : Lemma (mem c (ancestors_of d g z) ==> mem z fw) =
      if mem c (ancestors_of d g z) then closed_up d any_id fw g z c in
    FStar.Classical.forall_intro_2 not_after;
    match keep (uncovered behind later) ids with
    | u :: us ->
      pick_min_mem lt (u :: us);
      let v = pick_min lt (u :: us) in
      mem_keep (uncovered behind later) ids v;
      held_ids d v
    | [] ->
      let cov (x:string) : Lemma (mem x ids ==> mem x behind \/ mem x later) =
        mem_keep (uncovered behind later) ids x in
      FStar.Classical.forall_intro cov;
      let side = keep (side_parent c behind) (parents_of_all d later) in
      let sh (x:string) : Lemma (mem x side /\ in_set behind x ==> mem x ids) = () in
      FStar.Classical.forall_intro sh;
      fwd_enough d (in_set behind) d.nodes [] side;
      fwd_closed d (in_set behind) d.nodes [] side;
      let Found band = forward_from d (in_set behind) d.nodes side in
      let kept = app band (app later [ c ]) in
      let in_kept (x:string) : Lemma (mem x kept == (mem x band || mem x later || x = c)) =
        mem_app x band (app later [ c ]); mem_app x later [ c ] in
      FStar.Classical.forall_intro in_kept;
      (* every node whose walk reaches a kept node is kept *)
      let above (y:string) (g:list (node op)) (z:string)
        : Lemma (mem y kept /\ at_least fuel g /\ mem y (ancestors_of d g z) ==> mem z kept) =
        if mem y kept && at_least fuel g && mem y (ancestors_of d g z) then begin
          anc_head_found d g z y;
          held_ids d z;
          if not (mem z behind) then ()
          else if mem y band then begin
            let wb (w:string) : Lemma (mem w (ancestors_of d g z) ==> in_set behind w) =
              if mem w (ancestors_of d g z) then begin
                anc_mono d g fuel z w;
                anc_trans d fuel fuel c z w
              end in
            FStar.Classical.forall_intro wb;
            closed_up d (in_set behind) band g z y
          end
          else closed_up d any_id fw g z y
        end
      in
      let node_ok (n:node op)
        : Lemma (mem n d.nodes ==>
                 (if mem n.nid behind
                  then (not (mem n.nid kept) || mem n.nid (ancestors_of (truncate d kept) fuel c))
                  else mem n.nid kept)) =
        if mem n d.nodes then begin
          mem_ids_of n d.nodes;
          if mem n.nid behind && mem n.nid kept then begin
            let ka (g:list (node op)) (z:string)
              : Lemma (at_least fuel g /\ mem n.nid (ancestors_of d g z) ==> mem z kept) =
              above n.nid g z in
            FStar.Classical.forall_intro_2 ka;
            assert (kept_above d fuel kept n.nid);
            anc_kept d fuel kept fuel c n.nid
          end
        end
      in
      FStar.Classical.forall_intro node_ok;
      trunc_ok_intro d fuel c kept d.nodes
#pop-options

(* THEOREM, end to end: a compaction preserves every replay at or after its checkpoint. `cut`'s
   kept set meets the contract, and `DagFold.truncation_preserves_replay` does the rest. *)
let compaction_preserves_replay (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (lt:string -> string -> bool)
  (kfuel:list (node op)) (d:dag op) (fuel:list (node op)) (kept:list string)
  (cp:checkpoint state) (h:string) (wh wh':list string)
  : Lemma (requires total_order lt /\ distinct_ids d /\ Cons? fuel /\ walk_ok d fuel h /\
                    Found? (lookup d.nodes h) /\
                    walk_ok d fuel cp.cnode /\ Found? (lookup d.nodes cp.cnode) /\
                    cut_keep lt d fuel cp.cnode == CutKeep kept /\
                    head_drains d fuel kfuel h wh /\
                    head_drains (truncate d kept) fuel kfuel h wh')
          (ensures (match replay_from apply lt kfuel d fuel cp h with
                    | Error (CUnreached _) | Error (CUncovered _) -> True
                    | r -> replay_from apply lt kfuel (truncate d kept) fuel cp h == r))
  = cut_keep_meets_contract lt d fuel cp.cnode;
    truncation_preserves_replay apply lt kfuel d fuel kept cp h wh wh'

(* ======================================================================================
   TWINS (Phase 309) — the extractor premise, sampled at this model. See DagFold's TWINS block;
   these fixtures reuse its DAGs and add one whose compaction keeps a band.
   ====================================================================================== *)

(* A chain a <- b <- c with `m` after `c` naming `a` as a side parent: compacting at `c` keeps the
   band a, b, c. *)
let twin_band : dag string =
  { nodes = [ { nid = "m"; nparents = [ "c"; "a" ]; nop = "m" };
              { nid = "c"; nparents = [ "b" ]; nop = "c" };
              { nid = "b"; nparents = [ "a" ]; nop = "b" };
              { nid = "a"; nparents = []; nop = "a" } ] }

let twin_band_fuel : list (node string) = app twin_band.nodes twin_band.nodes

let twin_reach : list rslot = reach_of_dag twin_lt twin_fuel twin_dag

let twin_order : list string = drain_order twin_lt twin_fuel twin_dag.nodes

let twin_kept (r:cut_result) (d:dag string) (fuel:list (node string)) (c:string)
  (want:list string) : Tot bool =
  match r with
  | CutKeep k -> truncation_ok d fuel c k && all_in want k && all_in k want
  | _ -> false

let twins : list twin = [
  { tname = "merge-base-now-of-a-criss-cross-is-the-modelled-one";
    tholds = (fun () ->
      merge_base_now twin_lt twin_cc_fuel twin_cc twin_cc_fuel "x" "y" = Found "c" &&
      merge_base_now twin_lt twin_cc_fuel twin_cc twin_cc_fuel "x" "zz" = Missing) };
  { tname = "common-base-over-three-heads";
    tholds = (fun () ->
      common_base twin_lt twin_cc_fuel twin_cc twin_cc_fuel [ "x"; "y"; "x" ] = Found "c" &&
      common_base twin_lt twin_fuel twin_dag twin_fuel [ "b"; "c"; "m" ] = Found "a" &&
      common_base twin_lt twin_cc_fuel twin_cc twin_cc_fuel [] = Missing &&
      common_base twin_lt twin_cc_fuel twin_cc twin_cc_fuel [ "x"; "zz" ] = Missing) };
  { tname = "reach-merge-base-and-common-base-answer-the-dag";
    tholds = (fun () ->
      reach_merge_base twin_lt twin_fuel twin_dag twin_fuel twin_reach "b" "c" = Found "a" &&
      reach_merge_base twin_lt twin_fuel twin_dag twin_fuel twin_reach "m" "c" = Found "c" &&
      reach_merge_base twin_lt twin_fuel twin_dag twin_fuel twin_reach "m" "zz" = Missing &&
      reach_common_base twin_lt twin_fuel twin_dag twin_fuel twin_reach [ "b"; "c"; "m" ] = Found "a") };
  { tname = "reach-try-topo-order-is-the-closure-drain";
    tholds = (fun () ->
      reach_try_topo_order twin_lt twin_fuel twin_dag twin_fuel twin_reach twin_order "m"
      = TopoOk [ "a"; "b"; "c"; "m" ] &&
      try_topo_order twin_lt twin_fuel twin_dag twin_fuel "c" = TopoOk [ "a"; "c" ] &&
      reach_try_topo_order twin_lt twin_fuel twin_dag twin_fuel twin_reach twin_order "c"
      = TopoOk [ "a"; "c" ]) };
  { tname = "reach-between-is-the-delta";
    tholds = (fun () ->
      ids_of (reach_between twin_lt twin_fuel twin_dag twin_fuel twin_reach twin_order "b" "m")
      = [ "c"; "m" ] &&
      ids_of (between_drained twin_lt twin_fuel twin_dag twin_fuel "b" "m") = [ "c"; "m" ] &&
      ids_of (reach_between twin_lt twin_fuel twin_dag twin_fuel twin_reach twin_order "zz" "c")
      = [ "a"; "c" ]) };
  { tname = "cut-keeps-what-follows-and-the-band";
    tholds = (fun () ->
      twin_kept (cut_keep twin_lt twin_chain twin_chain_fuel "q") twin_chain twin_chain_fuel "q"
        [ "q"; "r" ] &&
      twin_kept (cut_keep twin_lt twin_band twin_band_fuel "c") twin_band twin_band_fuel "c"
        [ "a"; "b"; "c"; "m" ]) };
  { tname = "cut-refuses-an-uncovered-node";
    tholds = (fun () -> cut_keep twin_lt twin_dag twin_fuel "b" = CutUncovered "c") } ]

let _ = assert_norm (twins_hold twins == true)
