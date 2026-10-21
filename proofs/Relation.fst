(*
   Relation — questions over a FINITE relation, answered once and totally: `Fuaran.Core.Relation`
   (fuaran-core Phase 552) modelled clause for clause, its specification stated concretely over a
   finite edge list, and what Tarjan's pass is proved to emit — the piece `Propagation.fst` took as
   a PARAMETER until Phase 553 (fuaran-core Phase 553).

   WHAT IS MODELLED. `src/Fuaran.Core.Relation/Relation.fs` — the PREPARED relation and every
   question asked of it:

     - the relation itself: `succ_of` (`Relation.successors`), `nodes` (`Relation.nodes`),
       `inverse` (`Relation.inverse`);
     - the PASS: Tarjan's strongly-connected-components pass (`pass`), written as the recursive
       visit production's explicit frame stack replays "exactly — successors in ascending order, a
       child's low-link folded into its parent's when the child's frame finishes, a component
       emitted when its root's frame does" (its doc comment), so the components, their order and
       the order of each one's members are the F#'s, item for item; and what reads it:
       `components`, `cycles`, `cycles_among`, `sort` (`Relation.sort`, a `topo`) and `terminals`;
     - the CLOSURE: `reachable` (`Relation.reachable`) — the frontier loop `grow`, which
       `Propagation.fst` carried as the private `grow` of `dirtyFromChangedIds` until this phase and
       now opens from here, as production's `Propagation` delegates both closures to
       `Relation.reachable` (Phase 552).

   HOW THE RELATION IS READ. The F# prepares a relation ONCE (`ofMap`): the nodes sorted ascending,
   each node's distinct successors ascending, every successor a node. The model takes that prepared
   form as an association list `relation` — the nodes in the LIST's order, each with its successor
   list — and visits nodes and successors in list order. It sorts nothing and compares no two node
   names: order is the input's, which is what makes the model `Type0`-plain and the differential
   exact — the host hands it `Relation.toMap`, whose keys ascend and whose successor sets list
   ascending, so the model's visit is production's visit. A successor that is no key reads as a
   node with no successors (`succ_of`), exactly as `ofMap` adds it; `nodes` lists the keys and then
   every such successor, once each, so for a prepared relation it is the key list.

   WHAT IS PROVED, AND WHAT IS NOT. The ladder states both, once: the rows of `../proofs.json`
   whose `evidence.model` is this file, each with its `statement` and its `boundary`. `README.md`
   renders them in this model's section. In one line here, because the division is the point of
   the model: the PASS is proved to emit a PARTITION of the nodes — every node in exactly one
   component, no component empty, so `sort`'s `Order` holds no id twice and `Order` with the
   cycles' members is the node set — which is what `Propagation.fst`'s agreement theorems take of
   the walked order; the CLOSURE is proved sound and least; the INVERSE is proved to turn every
   edge round. That a component is a mutual-reachability class and that the components come
   dependencies-first — Tarjan's correctness theorem — is STATED concretely here (`mutual`,
   `on_cycle`, `deps_first`, `terminals_spec`) and held by the differential on every relation it
   draws, three ways (production, the extracted pass, the extracted specification), and is not
   proved: see the `relation-components-are-sccs` row's boundary.

   HOW TO READ IT. Every definition names its F# counterpart, as in `Propagation.fst`. The module
   opens only the shared `Twin` and `ListAlgebra`; `Propagation.fst` and `Capability.fst` open it
   for the list helpers (`assoc`, `subset`, `union`, …) and the closure that used to be
   Propagation's own. State is a record of association lists read newest-first, so "`low[v] <-`"
   is one cons and `low_of` reads the newest binding.

   Apache-2.0, like everything beside it.
*)
module Relation

open Twin
open ListAlgebra

(* ======================================================================================
   0. The list helpers, each naming the FSharp.Core function it stands for (moved here from
      `Propagation.fst` by Phase 553; one definition each, opened by the models that use them).
   ====================================================================================== *)

(* F#: `Map.tryFind`, a finite map read as its association list — the NEWEST binding wins. *)
let rec assoc (#a:Type) (k:string) (l:list (string & a)) : Tot (option a) =
  match l with
  | [] -> None
  | (k', v) :: t -> if k = k' then Some v else assoc k t

(* F#: `Map.containsKey`. *)
let rec has_key (#a:Type) (k:string) (l:list (string & a)) : Tot bool =
  match l with
  | [] -> false
  | (k', _) :: t -> k = k' || has_key k t

(* F#: `Map.toList |> List.map fst`. *)
let rec keys (#a:Type) (l:list (string & a)) : Tot (list string) =
  match l with
  | [] -> []
  | (k, _) :: t -> k :: keys t

(* F#: `Set.isSubset`. *)
let rec subset (a b:list string) : Tot bool =
  match a with
  | [] -> true
  | h :: t -> mem h b && subset t b

(* F#: `Set.union a b`, read as membership: `a`, then what `b` adds. *)
let union (a b:list string) : Tot (list string) = app a (diff b a)

(* A set holds an id once: `Set.ofList`. Keeps the LAST occurrence; the order is not meaning. *)
let rec dedup (l:list string) : Tot (list string) =
  match l with
  | [] -> []
  | h :: t -> if mem h t then dedup t else h :: dedup t

(* F#: `List.concat`. *)
let rec concat (gs:list (list string)) : Tot (list string) =
  match gs with
  | [] -> []
  | g :: r -> app g (concat r)

(* Two lists hold the same ids: `Set.ofList a = Set.ofList b`. *)
let same_set (a b:list string) : Tot bool = subset a b && subset b a

let rec app_nil (#a:Type) (l:list a) : Lemma (app l [] == l) =
  match l with
  | [] -> ()
  | _ :: t -> app_nil t

let rec app_assoc (#a:Type) (l m n:list a) : Lemma (app (app l m) n == app l (app m n)) =
  match l with
  | [] -> ()
  | _ :: t -> app_assoc t m n

let rec mem_app (x:string) (l m:list string) : Lemma (mem x (app l m) == (mem x l || mem x m)) =
  match l with
  | [] -> ()
  | _ :: t -> mem_app x t m

let rec mem_diff (x:string) (a b:list string) : Lemma (mem x (diff a b) == (mem x a && not (mem x b))) =
  match a with
  | [] -> ()
  | _ :: t -> mem_diff x t b

let mem_union (x:string) (a b:list string) : Lemma (mem x (union a b) == (mem x a || mem x b)) =
  mem_app x a (diff b a);
  mem_diff x b a

let rec mem_dedup (x:string) (l:list string) : Lemma (mem x (dedup l) == mem x l) =
  match l with
  | [] -> ()
  | _ :: t -> mem_dedup x t

let rec mem_concat (x:string) (gs:list (list string))
  : Lemma (mem x (concat gs) <==> (exists (g:list string). mem g gs /\ mem x g)) =
  match gs with
  | [] -> ()
  | g :: r -> mem_app x g (concat r); mem_concat x r

let rec subset_mem (a b:list string) (x:string)
  : Lemma (requires subset a b /\ mem x a) (ensures mem x b) =
  match a with
  | [] -> ()
  | h :: t -> if x = h then () else subset_mem t b x

let rec subset_intro (a b:list string)
  : Lemma (requires (forall (x:string). mem x a ==> mem x b)) (ensures subset a b) =
  match a with
  | [] -> ()
  | _ :: t -> subset_intro t b

let rec len_diff_le (a b:list string) : Lemma (len (diff a b) <= len a) =
  match a with
  | [] -> ()
  | _ :: t -> len_diff_le t b

let rec diff_mono (rng a a':list string)
  : Lemma (requires (forall (y:string). mem y a ==> mem y a')) (ensures len (diff rng a') <= len (diff rng a)) =
  match rng with
  | [] -> ()
  | _ :: t -> diff_mono t a a'

let rec diff_shrink (rng a a':list string) (x:string)
  : Lemma (requires (forall (y:string). mem y a ==> mem y a') /\ mem x rng /\ not (mem x a) /\ mem x a')
          (ensures len (diff rng a') < len (diff rng a)) =
  match rng with
  | [] -> ()
  | h :: t -> if h = x then diff_mono t a a' else diff_shrink t a a' x

(* `distinct` over an append: both halves distinct and disjoint. The reading every stack-to-
   components move below is proved in. *)
let rec distinct_app (a b:list string)
  : Lemma (distinct (app a b) <==> (distinct a /\ distinct b /\ (forall (x:string). mem x a ==> not (mem x b)))) =
  match a with
  | [] -> ()
  | h :: t -> mem_app h t b; distinct_app t b

(* ======================================================================================
   1. The relation.
   ====================================================================================== *)

(* F#: the prepared `Relation<'N>`, read as nodes in list order with their successor lists. *)
type relation = list (string & list string)

(* F#: `Relation.successors` — empty for a node with none, and for a node the relation does not
   hold. *)
let succ_of (r:relation) (n:string) : Tot (list string) =
  match assoc n r with
  | Some l -> l
  | None -> []

(* Every successor any key names. *)
let rec range (r:relation) : Tot (list string) =
  match r with
  | [] -> []
  | (_, vs) :: t -> app vs (range t)

(* Each id once, keeping the FIRST occurrence — the order a prepared relation lists its nodes in. *)
let rec nub (l:list string) : Tot (list string) (decreases (len l)) =
  match l with
  | [] -> []
  | h :: t -> len_diff_le t [h]; h :: nub (diff t [h])

let rec mem_nub (x:string) (l:list string) : Lemma (ensures mem x (nub l) == mem x l) (decreases (len l)) =
  match l with
  | [] -> ()
  | h :: t -> len_diff_le t [h]; mem_diff x t [h]; mem_nub x (diff t [h])

let rec distinct_nub (l:list string) : Lemma (ensures distinct (nub l)) (decreases (len l)) =
  match l with
  | [] -> ()
  | h :: t -> len_diff_le t [h]; mem_nub h (diff t [h]); mem_diff h t [h]; distinct_nub (diff t [h])

(* F#: `Relation.nodes` — every key and every successor, each once: the keys in their order, then
   the successors no key names, in the order they are first named. For a PREPARED relation every
   successor is a key, so this is the key list. *)
let nodes (r:relation) : Tot (list string) = nub (app (keys r) (range r))

let rec succ_in_range (r:relation) (n x:string)
  : Lemma (requires mem x (succ_of r n)) (ensures mem x (range r)) =
  match r with
  | [] -> ()
  | (k, vs) :: t ->
    mem_app x vs (range t);
    if n = k then () else succ_in_range t n x

(* A successor is a node. *)
let succ_is_node (r:relation) (n x:string)
  : Lemma (requires mem x (succ_of r n)) (ensures mem x (nodes r)) =
  succ_in_range r n x;
  mem_app x (keys r) (range r);
  mem_nub x (app (keys r) (range r))

(* F#: `Relation.inverse` — `b` succeeds `a` here exactly when `a` succeeds `b` in `r`; the nodes
   are the same, each one's predecessors in node order. *)
let rec preds_of (r:relation) (ns:list string) (b:string) : Tot (list string) =
  match ns with
  | [] -> []
  | a :: t -> if mem b (succ_of r a) then a :: preds_of r t b else preds_of r t b

let rec inverse_over (r:relation) (ns:list string) : Tot relation =
  match ns with
  | [] -> []
  | b :: t -> (b, preds_of r (nodes r) b) :: inverse_over r t

let inverse (r:relation) : Tot relation = inverse_over r (nodes r)

let rec mem_preds (r:relation) (ns:list string) (a b:string)
  : Lemma (mem a (preds_of r ns b) == (mem a ns && mem b (succ_of r a))) =
  match ns with
  | [] -> ()
  | h :: t -> mem_preds r t a b

let rec assoc_inverse_over (r:relation) (ns:list string) (b:string)
  : Lemma (assoc b (inverse_over r ns) == (if mem b ns then Some (preds_of r (nodes r) b) else None)) =
  match ns with
  | [] -> ()
  | h :: t -> assoc_inverse_over r t b

(* THEOREM — inverse_edge. The inverse turns every edge round and adds none: `a` is a successor
   of `b` in the inverse exactly when `b` is a successor of `a` in the relation (over the nodes;
   a name that is no node has no successors in either). *)
let inverse_edge (r:relation) (a b:string)
  : Lemma (mem a (succ_of (inverse r) b) == (mem a (nodes r) && mem b (nodes r) && mem b (succ_of r a))) =
  assoc_inverse_over r (nodes r) b;
  mem_preds r (nodes r) a b;
  if mem b (succ_of r a) && not (mem b (nodes r)) then succ_is_node r a b else ()

(* ======================================================================================
   2. The closure — `Relation.reachable`, the frontier loop `Propagation.fst` carried as the
      private `grow` of `dirtyFromChangedIds` (Phase 186) and `neededFor` (Phase 317), one loop
      handed either map; moved here whole by Phase 553, as production's loop moved in Phase 552.
   ====================================================================================== *)

(* F#: the `Stack` of indices still to pop — every successor of a frontier id. *)
let rec next_of (d:relation) (frontier:list string) (s:list string) : Tot (list string) (decreases frontier) =
  match frontier with
  | [] -> s
  | node :: t ->
    next_of d t (match assoc node d with
                 | Some ds -> union s ds
                 | None -> s)

(* `x` is a successor of SOME frontier id — the fold above, read as membership. *)
let rec any_dep (d:relation) (frontier:list string) (x:string) : Tot bool =
  match frontier with
  | [] -> false
  | node :: t -> mem x (succ_of d node) || any_dep d t x

let rec next_mem (d:relation) (frontier s:list string) (x:string)
  : Lemma (ensures mem x (next_of d frontier s) == (mem x s || any_dep d frontier x)) (decreases frontier) =
  match frontier with
  | [] -> ()
  | node :: t ->
    (match assoc node d with
     | Some ds -> mem_union x s ds; next_mem d t (union s ds) x
     | None -> next_mem d t s x)

let rec any_dep_intro (d:relation) (frontier:list string) (r x:string)
  : Lemma (requires mem r frontier /\ mem x (succ_of d r)) (ensures any_dep d frontier x) =
  match frontier with
  | [] -> ()
  | node :: t -> if r = node then () else any_dep_intro d t r x

let rec any_dep_in_range (d:relation) (frontier:list string) (x:string)
  : Lemma (requires any_dep d frontier x) (ensures mem x (range d)) =
  match frontier with
  | [] -> ()
  | node :: t ->
    if mem x (succ_of d node) then succ_in_range d node x else any_dep_in_range d t x

(* WHY THE LOOP STOPS. A round that finds nothing fresh leaves the accumulator alone and hands
   on an empty frontier; a round that finds something strictly shrinks the ids of the map not
   yet accumulated. Production's doc comment carries this argument; here it is the function's
   termination proof. *)
let grow_measure (d:relation) (frontier acc:list string)
  : Lemma (ensures (let fresh = diff (next_of d frontier []) acc in
                    (Nil? fresh ==> union acc fresh == acc) /\
                    (Cons? fresh ==> len (diff (range d) (union acc fresh)) < len (diff (range d) acc)))) =
  let next = next_of d frontier [] in
  let fresh = diff next acc in
  match fresh with
  | [] -> app_nil acc
  | x :: _ ->
    mem_diff x next acc;
    next_mem d frontier [] x;
    any_dep_in_range d frontier x;
    let aux (y:string) : Lemma (mem y acc ==> mem y (union acc fresh)) = mem_union y acc fresh in
    FStar.Classical.forall_intro aux;
    mem_union x acc fresh;
    diff_shrink (range d) acc (union acc fresh) x

(* F#: the `while frontier.Count > 0` loop of `reachable` — `seen` is the accumulator. *)
let rec grow (d:relation) (frontier acc:list string)
  : Tot (list string) (decreases %[len (diff (range d) acc); len frontier]) =
  match frontier with
  | [] -> acc
  | _ :: _ ->
    let next = next_of d frontier [] in
    let fresh = diff next acc in
    grow_measure d frontier acc;
    grow d fresh (union acc fresh)

(* F#: `Relation.reachable` — the sources themselves (a source that is no node included, as a
   node with no successors) and every node reachable from one by one edge or more. *)
let reachable (d:relation) (sources:list string) : Tot (list string) = grow d sources sources

(* A set CLOSED under the relation: every successor of a member is a member. *)
let closed_by (d:relation) (s:list string) : Tot prop =
  forall (n x:string). mem n s /\ mem x (succ_of d n) ==> mem x s

(* The loop invariant: closed already, except at the frontier still to be expanded. *)
let closed_by_except (d:relation) (acc frontier:list string) : Tot prop =
  forall (n x:string). mem n acc /\ not (mem n frontier) /\ mem x (succ_of d n) ==> mem x acc

let grow_step_closed_by (d:relation) (frontier acc:list string) (n x:string)
  : Lemma (requires closed_by_except d acc frontier)
          (ensures (let fresh = diff (next_of d frontier []) acc in
                    mem n (union acc fresh) /\ not (mem n fresh) /\ mem x (succ_of d n) ==>
                    mem x (union acc fresh))) =
  let next = next_of d frontier [] in
  let fresh = diff next acc in
  mem_union n acc fresh;
  mem_union x acc fresh;
  mem_diff n next acc;
  mem_diff x next acc;
  next_mem d frontier [] x;
  if mem n frontier && mem x (succ_of d n) then any_dep_intro d frontier n x else ()

let rec grow_closed_by (d:relation) (frontier acc:list string)
  : Lemma (requires closed_by_except d acc frontier)
          (ensures closed_by d (grow d frontier acc) /\
                   (forall (x:string). mem x acc ==> mem x (grow d frontier acc)))
          (decreases %[len (diff (range d) acc); len frontier]) =
  match frontier with
  | [] -> ()
  | _ :: _ ->
    let fresh = diff (next_of d frontier []) acc in
    grow_measure d frontier acc;
    FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 (grow_step_closed_by d frontier acc));
    let aux (x:string) : Lemma (mem x acc ==> mem x (union acc fresh)) = mem_union x acc fresh in
    FStar.Classical.forall_intro aux;
    grow_closed_by d fresh (union acc fresh)

let next_within_by (d:relation) (frontier s acc:list string) (x:string)
  : Lemma (requires closed_by d s /\ (forall (y:string). mem y frontier ==> mem y s) /\
                    mem x (next_of d frontier acc) /\ not (mem x acc))
          (ensures mem x s) =
  next_mem d frontier acc x;
  let rec walk (f:list string)
    : Lemma (requires (forall (y:string). mem y f ==> mem y s) /\ any_dep d f x) (ensures mem x s) =
    match f with
    | [] -> ()
    | node :: t -> if mem x (succ_of d node) then () else walk t
  in
  walk frontier

let rec grow_within_by (d:relation) (frontier acc s:list string)
  : Lemma (requires closed_by d s /\ (forall (y:string). mem y frontier ==> mem y s) /\
                    (forall (y:string). mem y acc ==> mem y s))
          (ensures (forall (y:string). mem y (grow d frontier acc) ==> mem y s))
          (decreases %[len (diff (range d) acc); len frontier]) =
  match frontier with
  | [] -> ()
  | _ :: _ ->
    let next = next_of d frontier [] in
    let fresh = diff next acc in
    grow_measure d frontier acc;
    let fresh_in (y:string) : Lemma (mem y fresh ==> mem y s) =
      mem_diff y next acc;
      if mem y fresh then next_within_by d frontier s [] y else ()
    in
    FStar.Classical.forall_intro fresh_in;
    let acc_in (y:string) : Lemma (mem y (union acc fresh) ==> mem y s) = mem_union y acc fresh in
    FStar.Classical.forall_intro acc_in;
    grow_within_by d fresh (union acc fresh) s

(* THEOREM — reachable_closed. The closure HOLDS the sources and is CLOSED under the relation:
   every successor of a member is a member. *)
let reachable_closed (d:relation) (sources:list string)
  : Lemma (subset sources (reachable d sources) /\ closed_by d (reachable d sources)) =
  grow_closed_by d sources sources;
  subset_intro sources (reachable d sources)

(* THEOREM — reachable_least. The closure is the LEAST closed superset of the sources: any set
   that holds the sources and is closed under the relation holds all of it, so a node no source
   reaches is never in it. *)
let reachable_least (d:relation) (sources s:list string)
  : Lemma (requires subset sources s /\ closed_by d s)
          (ensures subset (reachable d sources) s) =
  let aux (y:string) : Lemma (mem y sources ==> mem y s) =
    if mem y sources then subset_mem sources s y else ()
  in
  FStar.Classical.forall_intro aux;
  grow_within_by d sources sources s;
  subset_intro (reachable d sources) s

(* ======================================================================================
   3. The specification, stated concretely over the edge list — what the pass is held to.
   ====================================================================================== *)

(* Reachable from `x` by ONE edge or more: the closure of its successors. *)
let reach_plus (r:relation) (x:string) : Tot (list string) = reachable r (succ_of r x)

(* A node ON A CYCLE: it reaches itself by one edge or more (a self-edge is one). *)
let on_cycle (r:relation) (x:string) : Tot bool = mem x (reach_plus r x)

(* Each reaches the other, by zero edges or more. *)
let mutual (r:relation) (x y:string) : Tot bool =
  mem y (reachable r [x]) && mem x (reachable r [y])

(* The nodes on a cycle, by the definition: `Relation.cyclesAmong`'s specification. *)
let rec cyclic_nodes (r:relation) (ns:list string) : Tot (list string) =
  match ns with
  | [] -> []
  | n :: t -> if on_cycle r n then n :: cyclic_nodes r t else cyclic_nodes r t

let cycles_among_spec (r:relation) : Tot (list string) = cyclic_nodes r (nodes r)

let rec any_on_cycle (r:relation) (ns:list string) : Tot bool =
  match ns with
  | [] -> false
  | n :: t -> on_cycle r n || any_on_cycle r t

let rec sinks_among (r:relation) (ns:list string) : Tot (list string) =
  match ns with
  | [] -> []
  | n :: t -> if Nil? (succ_of r n) then n :: sinks_among r t else sinks_among r t

(* `Relation.terminals`' chain definition: the sinks a node reaches, or `None` when it reaches a
   cycle — "a chain that loops ends nowhere". A name that is no node reaches only itself, a sink. *)
let terminals_spec (r:relation) (x:string) : Tot (option (list string)) =
  let reached = reachable r [x] in
  if any_on_cycle r reached then None else Some (sinks_among r reached)

(* Components DEPENDENCIES-FIRST: every successor of a member lies in its own component or in one
   listed EARLIER. The checker the differential holds the pass's order to. *)
let rec succs_within (r:relation) (allowed:list string) (ms:list string) : Tot bool =
  match ms with
  | [] -> true
  | m :: t -> subset (succ_of r m) allowed && succs_within r allowed t

let rec deps_first (r:relation) (earlier:list string) (comps:list (list string)) : Tot bool (decreases comps) =
  match comps with
  | [] -> true
  | c :: t -> succs_within r (app c earlier) c && deps_first r (app c earlier) t


(* ======================================================================================
   4. The pass — Tarjan's algorithm as production runs it, with the state its arrays hold.
   ====================================================================================== *)

(* F#: the pass's arrays `order` (the discovery index) and `low`, its `stack` (top first, with
   `onStack` as membership), the `components` found so far (newest first) and `next`. *)
type state = {
  idx: list (string & nat);
  low: list (string & nat);
  stack: list string;
  comps: list (list string);
  next: nat }

let init : state = { idx = []; low = []; stack = []; comps = []; next = 0 }

(* F#: `order[v] >= 0`. *)
let visited (st:state) (v:string) : Tot bool = has_key v st.idx

let idx_of (st:state) (v:string) : Tot nat =
  match assoc v st.idx with
  | Some n -> n
  | None -> 0

let low_of (st:state) (v:string) : Tot nat =
  match assoc v st.low with
  | Some n -> n
  | None -> 0

let min (a b:nat) : Tot nat = if a <= b then a else b

(* F#: `low[v] <- n`. *)
let set_low (st:state) (v:string) (n:nat) : Tot state = { st with low = (v, n) :: st.low }

(* F#: `enter v`. *)
let enter (st:state) (v:string) : Tot state =
  { idx = (v, st.next) :: st.idx; low = (v, st.next) :: st.low;
    stack = v :: st.stack; comps = st.comps; next = st.next + 1 }

let rec has_key_keys (#a:Type) (k:string) (l:list (string & a)) : Lemma (has_key k l == mem k (keys l)) =
  match l with
  | [] -> ()
  | _ :: t -> has_key_keys k t

(* The visited ids only grow, read as the index map's keys — the measure every call shrinks or keeps. *)
let keys_grow (st st':state)
  : Lemma (requires (forall (x:string). visited st x ==> visited st' x))
          (ensures (forall (y:string). mem y (keys st.idx) ==> mem y (keys st'.idx))) =
  let aux (y:string) : Lemma (mem y (keys st.idx) ==> mem y (keys st'.idx)) =
    has_key_keys y st.idx; has_key_keys y st'.idx
  in
  FStar.Classical.forall_intro aux

(* F#: the `while w <> v do w <- stack.Pop()` loop — the members popped, top first and `v` last,
   and the stack left under them. *)
let rec pop_until (v:string) (stack:list string) : Tot (list string & list string) (decreases stack) =
  match stack with
  | [] -> ([], [])
  | w :: rest -> if w = v then ([v], rest) else (let (ms, below) = pop_until v rest in (w :: ms, below))

let rec pop_until_app (v:string) (above s:list string)
  : Lemma (requires not (mem v above))
          (ensures pop_until v (app above (v :: s)) == (app above [v], s)) =
  match above with
  | [] -> ()
  | _ :: t -> pop_until_app v t s

let rec app_last_cons (a:list string) (v:string) : Lemma (Cons? (app a [v])) =
  match a with
  | [] -> ()
  | _ :: t -> app_last_cons t v

(* The stack entered with is a SUFFIX of the stack returned with: `prefix_to s l` is what the
   visit pushed above it, when `s` is a suffix of `l`. *)
let rec prefix_to (s l:list string) : Tot (option (list string)) (decreases l) =
  if s = l then Some [] else
  match l with
  | [] -> None
  | h :: t -> (match prefix_to s t with Some a -> Some (h :: a) | None -> None)

let suffix (s l:list string) : Tot bool = Some? (prefix_to s l)

let rec prefix_to_app (s l:list string)
  : Lemma (requires suffix s l) (ensures l == app (Some?.v (prefix_to s l)) s) (decreases l) =
  if s = l then () else
  match l with
  | [] -> ()
  | h :: t -> prefix_to_app s t

let suffix_refl (l:list string) : Lemma (suffix l l) = ()

let suffix_cons (s l:list string) (x:string)
  : Lemma (requires suffix s l) (ensures suffix s (x :: l)) = ()

let rec suffix_trans (a b c:list string)
  : Lemma (requires suffix a b /\ suffix b c) (ensures suffix a c) (decreases c) =
  if b = c then () else
  match c with
  | [] -> ()
  | _ :: t -> suffix_trans a b t

let suffix_mem (s l:list string) (x:string)
  : Lemma (requires suffix s l /\ mem x s) (ensures mem x l) =
  prefix_to_app s l;
  mem_app x (Some?.v (prefix_to s l)) s

(* The bottom of the stack — the root of the visit in progress. *)
let rec last (l:list string) : Tot (option string) =
  match l with
  | [] -> None
  | [x] -> Some x
  | _ :: t -> last t

let rec last_app (a s:list string)
  : Lemma (requires Cons? s) (ensures last (app a s) == last s) =
  match a with
  | [] -> ()
  | [_] -> ()
  | _ :: t -> last_app t s

let suffix_last (s l:list string)
  : Lemma (requires suffix s l /\ Cons? s) (ensures last l == last s) =
  prefix_to_app s l;
  last_app (Some?.v (prefix_to s l)) s

let rec last_mem (l:list string)
  : Lemma (requires Cons? l) (ensures Some? (last l) /\ mem (Some?.v (last l)) l) =
  match l with
  | [x] -> ()
  | _ :: t -> last_mem t

(* THE INVARIANT the pass keeps, clause by clause:
     (a) the stack and the components found hold no id twice between them;
     (b) an id is visited exactly when it is on the stack or in a component found;
     (c) a visited id is a node;
     (d) every index assigned is below `next`;
     (e) a visited id's low-link is at most its index;
     (f) every id on the stack has its low-link at or above the ROOT's index — the bottom of the
         stack — which is what makes a root pop: its low-link can never go below its own index;
     (g) no component found is empty. *)
let bottom_ok (st:state) : Tot prop =
  match last st.stack with
  | None -> True
  | Some b -> forall (x:string). mem x st.stack ==> low_of st x >= idx_of st b

let inv (r:relation) (st:state) : Tot prop =
  distinct (app st.stack (concat st.comps)) /\
  (forall (x:string). visited st x <==> mem x (app st.stack (concat st.comps))) /\
  (forall (x:string). visited st x ==> mem x (nodes r)) /\
  (forall (x:string). visited st x ==> idx_of st x < st.next) /\
  (forall (x:string). visited st x ==> low_of st x <= idx_of st x) /\
  bottom_ok st /\
  (forall (c:list string). mem c st.comps ==> Cons? c)

(* What a visit leaves alone: every id visited before it keeps its index and its low-link, except
   the frame's own node, whose low-link only falls. *)
let kept (st st':state) (frame:string) : Tot prop =
  forall (x:string). visited st x ==> visited st' x /\ idx_of st' x == idx_of st x /\
                                  (x <> frame ==> low_of st' x == low_of st x)

let inv_init (r:relation) : Lemma (inv r init) = ()

let last_cons (x:string) (l:list string) : Lemma (requires Cons? l) (ensures last (x :: l) == last l) =
  match l with
  | _ :: _ -> ()

let inv_enter (r:relation) (st:state) (v:string)
  : Lemma (requires inv r st /\ mem v (nodes r) /\ not (visited st v))
          (ensures inv r (enter st v) /\ kept st (enter st v) v) =
  let st' = enter st v in
  mem_app v st.stack (concat st.comps);
  match last st.stack with
  | None -> if Cons? st.stack then last_mem st.stack
  | Some b ->
    last_mem st.stack;
    last_cons v st.stack;
    mem_app b st.stack (concat st.comps);
    let aux (x:string) : Lemma (mem x (v :: st.stack) ==> low_of st' x >= idx_of st' b) =
      if x = v then () else ()
    in
    FStar.Classical.forall_intro aux

(* Lowering the frame node's low-link to a value at or above the root's index keeps the invariant. *)
let inv_set_low (r:relation) (st:state) (v:string) (n:nat)
  : Lemma (requires inv r st /\ mem v st.stack /\ n <= low_of st v /\
                    (match last st.stack with None -> true | Some b -> n >= idx_of st b))
          (ensures inv r (set_low st v n) /\ kept st (set_low st v n) v) =
  let st' = set_low st v n in
  mem_app v st.stack (concat st.comps);
  match last st.stack with
  | None -> ()
  | Some b ->
    let aux (x:string) : Lemma (mem x st.stack ==> low_of st' x >= idx_of st' b) = () in
    FStar.Classical.forall_intro aux

(* Moving the segment above and including `v` off the stack into a component keeps every id once
   and moves no membership: the stack-to-components move, as list algebra. *)
let distinct_move (above below c:list string) (v:string)
  : Lemma (requires distinct (app (app above (v :: below)) c))
          (ensures distinct (app below (app (app above [v]) c)) /\
                   (forall (x:string). mem x (app below (app (app above [v]) c)) ==
                                       mem x (app (app above (v :: below)) c))) =
  let members = app above [v] in
  let s = app above (v :: below) in
  distinct_app s c;
  distinct_app above (v :: below);
  distinct_app above [v];
  let mem_members (x:string) : Lemma (mem x members == (mem x above || x = v)) = mem_app x above [v] in
  let mem_s (x:string) : Lemma (mem x s == (mem x above || x = v || mem x below)) = mem_app x above (v :: below) in
  FStar.Classical.forall_intro mem_members;
  FStar.Classical.forall_intro mem_s;
  distinct_app members c;
  let mem_mc (x:string) : Lemma (mem x (app members c) == (mem x members || mem x c)) = mem_app x members c in
  FStar.Classical.forall_intro mem_mc;
  distinct_app below (app members c);
  let mem_all (x:string) : Lemma (mem x (app below (app members c)) == (mem x below || mem x members || mem x c)) =
    mem_app x below (app members c)
  in
  FStar.Classical.forall_intro mem_all;
  let mem_sc (x:string) : Lemma (mem x (app s c) == (mem x s || mem x c)) = mem_app x s c in
  FStar.Classical.forall_intro mem_sc

(* Popping a root's component: the ids above and including `v` move from the stack to a new
   component; what was under `v` is the stack. *)
let inv_pop (r:relation) (st:state) (v:string) (above below:list string)
  : Lemma (requires inv r st /\ st.stack == app above (v :: below))
          (ensures (let st' = { st with stack = below; comps = app above [v] :: st.comps } in
                    inv r st' /\ kept st st' v)) =
  let members = app above [v] in
  let st' = { st with stack = below; comps = members :: st.comps } in
  let c = concat st.comps in
  app_last_cons above v;
  distinct_move above below c v;
  match last below with
  | None -> ()
  | Some b ->
    last_app above (v :: below);
    last_cons v below;
    let aux2 (x:string) : Lemma (mem x below ==> low_of st' x >= idx_of st' b) =
      mem_app x above (v :: below)
    in
    FStar.Classical.forall_intro aux2

let succs_are_nodes (r:relation) (v:string) : Lemma (forall (x:string). mem x (succ_of r v) ==> mem x (nodes r)) =
  FStar.Classical.forall_intro (FStar.Classical.move_requires (succ_is_node r v))

(* The visit. `visit` enters a fresh node and runs its frame; `visit_succs` is the frame's loop
   over the node's successors, in order: a fresh successor is visited and its low-link folded in
   when its frame finishes; one on the stack lowers the frame's low-link to its index; any other
   is skipped. `visit` then emits the component whose root the node is, when it is one.
   Termination: a visit enters one more node of the relation; a frame's loop consumes one
   successor per step and the nodes still unvisited never grow. *)
let rec visit (r:relation) (st:state) (v:string)
  : Pure state
    (requires inv r st /\ mem v (nodes r) /\ not (visited st v))
    (ensures fun st' -> inv r st' /\ kept st st' v /\ visited st' v /\ suffix st.stack st'.stack /\
                     idx_of st' v == st.next /\
                     (mem v st'.stack ==> low_of st' v < idx_of st' v /\ suffix (v :: st.stack) st'.stack) /\
                     (not (mem v st'.stack) ==> st'.stack == st.stack /\ low_of st' v == idx_of st' v))
    (decreases %[len (diff (nodes r) (keys st.idx)); 0]) =
  inv_enter r st v;
  mem_app v st.stack (concat st.comps);
  let st1 = enter st v in
  has_key_keys v st.idx;
  diff_shrink (nodes r) (keys st.idx) (keys st1.idx) v;
  succs_are_nodes r v;
  let st2 = visit_succs r st1 v (succ_of r v) in
  if low_of st2 v = idx_of st2 v then begin
    prefix_to_app (v :: st.stack) st2.stack;
    let above = Some?.v (prefix_to (v :: st.stack) st2.stack) in
    distinct_app st2.stack (concat st2.comps);
    distinct_app above (v :: st.stack);
    assert (not (mem v above));
    pop_until_app v above st.stack;
    inv_pop r st2 v above st.stack;
    suffix_refl st.stack;
    let (members, below) = pop_until v st2.stack in
    { st2 with stack = below; comps = members :: st2.comps }
  end else begin
    suffix_mem (v :: st.stack) st2.stack v;
    suffix_refl st.stack;
    suffix_cons st.stack st.stack v;
    suffix_trans st.stack (v :: st.stack) st2.stack;
    st2
  end

and visit_succs (r:relation) (st:state) (v:string) (succs:list string)
  : Pure state
    (requires inv r st /\ mem v st.stack /\ (forall (x:string). mem x succs ==> mem x (nodes r)))
    (ensures fun st' -> inv r st' /\ kept st st' v /\ suffix st.stack st'.stack /\
                     low_of st' v <= low_of st v)
    (decreases %[len (diff (nodes r) (keys st.idx)); len succs + 1]) =
  match succs with
  | [] -> suffix_refl st.stack; st
  | w :: rest ->
    mem_app v st.stack (concat st.comps);
    mem_app w st.stack (concat st.comps);
    last_mem st.stack;
    mem_app (Some?.v (last st.stack)) st.stack (concat st.comps);
    if not (visited st w) then begin
      let st1 = visit r st w in
      keys_grow st st1;
      diff_mono (nodes r) (keys st.idx) (keys st1.idx);
      suffix_mem st.stack st1.stack v;
      suffix_last st.stack st1.stack;
      inv_set_low r st1 v (min (low_of st1 v) (low_of st1 w));
      let st2 = set_low st1 v (min (low_of st1 v) (low_of st1 w)) in
      let st3 = visit_succs r st2 v rest in
      suffix_trans st.stack st1.stack st3.stack;
      st3
    end
    else if mem w st.stack then begin
      inv_set_low r st v (min (low_of st v) (idx_of st w));
      let st1 = set_low st v (min (low_of st v) (idx_of st w)) in
      visit_succs r st1 v rest
    end
    else visit_succs r st v rest

(* F#: the `for root in 0 .. n - 1 do if order[root] < 0 then enter root ...` loop. A root always
   pops its component: its low-link is at or above its own index (clause (f), with the root the
   bottom of the stack), so the stack is empty again after each. *)
let rec roots (r:relation) (st:state) (ns:list string)
  : Pure state
    (requires inv r st /\ (forall (x:string). mem x ns ==> mem x (nodes r)) /\ st.stack == [])
    (ensures fun st' -> inv r st' /\ st'.stack == [] /\
                     (forall (x:string). visited st x ==> visited st' x) /\
                     (forall (x:string). mem x ns ==> visited st' x))
    (decreases ns) =
  match ns with
  | [] -> st
  | v :: t ->
    if visited st v then roots r st t
    else begin
      let st1 = visit r st v in
      (if mem v st1.stack then begin
         prefix_to_app (v :: []) st1.stack;
         last_app (Some?.v (prefix_to [v] st1.stack)) [v]
       end);
      roots r st1 t
    end

(* F#: the private `pass` — the components in the order the pass finished them. *)
let pass (r:relation) : Tot (list (list string)) =
  inv_init r;
  rev (roots r init (nodes r)).comps

(* ======================================================================================
   5. The questions read off the pass.
   ====================================================================================== *)

(* F#: `isCycle` — more than one member, or one member with an edge to itself. (A component is
   never empty, `components_partition`; the clause is there because the type does not say so.) *)
let is_cycle (r:relation) (members:list string) : Tot bool =
  match members with
  | [] -> false
  | [v] -> mem v (succ_of r v)
  | _ -> true

(* F#: `Relation.components`. *)
let components (r:relation) : Tot (list (list string)) = pass r

let rec cyclic_of (r:relation) (comps:list (list string)) : Tot (list (list string)) =
  match comps with
  | [] -> []
  | c :: t -> if is_cycle r c then c :: cyclic_of r t else cyclic_of r t

(* F#: `Relation.cycles`. *)
let cycles (r:relation) : Tot (list (list string)) = cyclic_of r (pass r)

(* F#: `Relation.cyclesAmong`. *)
let cycles_among (r:relation) : Tot (list string) = concat (cycles r)

(* F#: `Relation.Topo` — field names of its own so that `Propagation.fst`'s `topo_result`, which
   copies them as production's `TopoResult` does, can be in scope beside it. *)
type topo = { torder: list string; tcycles: list (list string) }

let rec order_of (r:relation) (comps:list (list string)) : Tot (list string) =
  match comps with
  | [] -> []
  | c :: t -> if is_cycle r c then order_of r t else (match c with v :: _ -> v :: order_of r t | [] -> order_of r t)

(* F#: `Relation.sort`. *)
let sort (r:relation) : Tot topo =
  let found = pass r in
  { torder = order_of r found; tcycles = cyclic_of r found }

(* F#: `ends[w]` — an index not yet answered reads as `None`, as the array is initialised. *)
let ends_of (ends:list (string & option (list string))) (w:string) : Tot (option (list string)) =
  match assoc w ends with
  | Some a -> a
  | None -> None

(* F#: the `Array.fold` over a node's successors. *)
let rec join_ends (ends:list (string & option (list string))) (ws:list string) (acc:option (list string))
  : Tot (option (list string)) =
  match ws with
  | [] -> acc
  | w :: t ->
    join_ends ends t (match acc, ends_of ends w with
                      | Some sofar, Some theirs -> Some (union sofar theirs)
                      | _, _ -> None)

let rec bind_all (ms:list string) (a:option (list string)) (ends:list (string & option (list string)))
  : Tot (list (string & option (list string))) =
  match ms with
  | [] -> ends
  | m :: t -> bind_all t a ((m, a) :: ends)

(* F#: the `for members in found.Components do` loop. *)
let rec answer_all (r:relation) (comps:list (list string)) (ends:list (string & option (list string)))
  : Tot (list (string & option (list string))) =
  match comps with
  | [] -> ends
  | members :: t ->
    let a =
      if is_cycle r members then None
      else (match members with
            | [] -> None
            | v :: _ -> (match succ_of r v with
                        | [] -> Some [v]
                        | ss -> join_ends ends ss (Some [])))
    in
    answer_all r t (bind_all members a ends)

(* F#: `Relation.terminals`. *)
let terminals (r:relation) : Tot (string -> option (list string)) =
  let ends = answer_all r (pass r) [] in
  fun node -> if mem node (nodes r) then ends_of ends node else Some [node]

(* ======================================================================================
   6. THEOREMS — what the pass emits.
   ====================================================================================== *)

let rec concat_app (a b:list (list string)) : Lemma (concat (app a b) == app (concat a) (concat b)) =
  match a with
  | [] -> ()
  | h :: r -> concat_app r b; app_assoc h (concat r) (concat b)

let rec concat_rev_mem (x:string) (gs:list (list string))
  : Lemma (mem x (concat (rev gs)) == mem x (concat gs)) =
  match gs with
  | [] -> ()
  | g :: t ->
    concat_rev_mem x t;
    mem_app x g (concat t);
    concat_app (rev t) [g];
    mem_app x (concat (rev t)) (concat [g]);
    app_nil g;
    mem_app x g []

let rec concat_rev_distinct (gs:list (list string))
  : Lemma (requires distinct (concat gs)) (ensures distinct (concat (rev gs))) =
  match gs with
  | [] -> ()
  | g :: t ->
    distinct_app g (concat t);
    concat_rev_distinct t;
    concat_app (rev t) [g];
    app_nil g;
    distinct_app (concat (rev t)) g;
    let aux (x:string) : Lemma (mem x (concat (rev t)) ==> not (mem x g)) = concat_rev_mem x t in
    FStar.Classical.forall_intro aux

let rec mem_app_any (#a:eqtype) (x:a) (p q:list a) : Lemma (mem x (app p q) == (mem x p || mem x q)) =
  match p with
  | [] -> ()
  | _ :: s -> mem_app_any x s q

let rec rev_mem (#a:eqtype) (x:a) (l:list a) : Lemma (mem x (rev l) == mem x l) =
  match l with
  | [] -> ()
  | h :: t -> rev_mem x t; mem_app_any x (rev t) [h]

(* THEOREM — components_partition. The components PARTITION the nodes: no id in two of them, every
   node in one, nothing that is not a node, and no component empty. *)
let components_partition (r:relation)
  : Lemma (distinct (concat (components r)) /\ same_set (concat (components r)) (nodes r) /\
           (forall (c:list string). mem c (components r) ==> Cons? c)) =
  inv_init r;
  let st = roots r init (nodes r) in
  assert (components r == rev st.comps);
  concat_rev_distinct st.comps;
  let aux (x:string) : Lemma (mem x (concat (rev st.comps)) == mem x (nodes r)) =
    concat_rev_mem x st.comps
  in
  FStar.Classical.forall_intro aux;
  subset_intro (concat (rev st.comps)) (nodes r);
  subset_intro (nodes r) (concat (rev st.comps));
  let ne (c:list string) : Lemma (mem c (rev st.comps) ==> Cons? c) = rev_mem c st.comps in
  FStar.Classical.forall_intro ne

(* `Order` and the cycles' members, together, are the components' members: `sort` reads each
   component once, a singleton without a self-edge into `Order` and every other into `Cycles`. *)
let rec sort_reads_each_mem (r:relation) (comps:list (list string))
  : Lemma (requires (forall (c:list string). mem c comps ==> Cons? c))
          (ensures (forall (x:string). mem x (app (order_of r comps) (concat (cyclic_of r comps))) == mem x (concat comps))) =
  match comps with
  | [] -> ()
  | c :: t ->
    sort_reads_each_mem r t;
    let o = order_of r t in
    let cy = concat (cyclic_of r t) in
    if is_cycle r c then begin
      let aux (x:string) : Lemma (mem x (app o (app c cy)) == mem x (app c (concat t))) =
        mem_app x o (app c cy); mem_app x c cy; mem_app x o cy; mem_app x c (concat t)
      in
      FStar.Classical.forall_intro aux
    end
    else begin
      match c with
      | [v] ->
        let aux (x:string) : Lemma (mem x (app (v :: o) cy) == mem x (app c (concat t))) = mem_app x o cy in
        FStar.Classical.forall_intro aux
      | _ -> ()
    end

let rec sort_reads_each_distinct (r:relation) (comps:list (list string))
  : Lemma (requires (forall (c:list string). mem c comps ==> Cons? c) /\ distinct (concat comps))
          (ensures distinct (app (order_of r comps) (concat (cyclic_of r comps)))) =
  match comps with
  | [] -> ()
  | c :: t ->
    distinct_app c (concat t);
    sort_reads_each_distinct r t;
    sort_reads_each_mem r t;
    let o = order_of r t in
    let cy = concat (cyclic_of r t) in
    distinct_app o cy;
    if is_cycle r c then begin
      distinct_app c cy;
      distinct_app o (app c cy);
      let aux (x:string) : Lemma (mem x o ==> not (mem x (app c cy))) = mem_app x c cy; mem_app x o cy in
      FStar.Classical.forall_intro aux;
      let aux2 (x:string) : Lemma (mem x c ==> not (mem x cy)) = mem_app x o cy in
      FStar.Classical.forall_intro aux2
    end
    else begin
      match c with
      | [v] -> mem_app v o cy
      | _ -> ()
    end

(* THEOREM — sort_partition. `sort`'s `Order` holds no id twice — the premise `Propagation.fst`'s
   agreement theorems take of the walked order, DISCHARGED — and `Order` with the cycles' members
   is exactly the node set, each once. *)
let sort_partition (r:relation)
  : Lemma (distinct (app (sort r).torder (concat (sort r).tcycles)) /\
           same_set (app (sort r).torder (concat (sort r).tcycles)) (nodes r)) =
  components_partition r;
  sort_reads_each_mem r (pass r);
  sort_reads_each_distinct r (pass r);
  let all = app (sort r).torder (concat (sort r).tcycles) in
  let a1 (x:string) : Lemma (mem x all ==> mem x (nodes r)) =
    if mem x all then subset_mem (concat (components r)) (nodes r) x
  in
  let a2 (x:string) : Lemma (mem x (nodes r) ==> mem x all) =
    if mem x (nodes r) then subset_mem (nodes r) (concat (components r)) x
  in
  FStar.Classical.forall_intro a1;
  FStar.Classical.forall_intro a2;
  subset_intro all (nodes r);
  subset_intro (nodes r) all

let rec distinct_app_left (a b:list string)
  : Lemma (requires distinct (app a b)) (ensures distinct a) (decreases a) =
  match a with
  | [] -> ()
  | h :: t -> mem_app h t b; distinct_app_left t b

(* THEOREM — sort_order_distinct. The corollary the driver needs by name. *)
let sort_order_distinct (r:relation) : Lemma (distinct (sort r).torder) =
  sort_partition r;
  distinct_app_left (sort r).torder (concat (sort r).tcycles)

(* ======================================================================================
   TWINS (Phase 309) — the extractor premise, sampled at this model. See `Twin`.
   ====================================================================================== *)

let twin_rel : relation = [ ("a", [ "b" ]); ("b", [ "a"; "c" ]); ("c", []) ]

let twins : list twin = [
  { tname = "components-come-dependencies-first-in-pop-order";
    tholds = (fun () -> components twin_rel = [ [ "c" ]; [ "b"; "a" ] ]) };
  { tname = "sort-splits-singletons-from-cycles";
    tholds = (fun () -> sort twin_rel = ({ torder = [ "c" ]; tcycles = [ [ "b"; "a" ] ] })) };
  { tname = "cycles-among-is-the-cyclic-members";
    tholds = (fun () -> cycles_among twin_rel = [ "b"; "a" ] && cycles_among_spec twin_rel = [ "a"; "b" ]) };
  { tname = "reachable-closes-forward";
    tholds = (fun () -> reachable twin_rel [ "c" ] = [ "c" ] && reachable twin_rel [ "b" ] = [ "b"; "a"; "c" ]) };
  { tname = "terminals-end-at-sinks-or-nowhere";
    tholds = (fun () -> terminals twin_rel "a" = None && terminals twin_rel "c" = Some [ "c" ] &&
                     terminals twin_rel "z" = Some [ "z" ] && terminals_spec twin_rel "a" = None) };
  { tname = "inverse-turns-every-edge-round";
    tholds = (fun () -> inverse twin_rel = [ ("a", [ "b" ]); ("b", [ "a" ]); ("c", [ "b" ]) ]) };
  { tname = "the-specification-reads-the-twin-as-the-pass-does";
    tholds = (fun () -> deps_first twin_rel [] (components twin_rel) && mutual twin_rel "a" "b" &&
                     not (on_cycle twin_rel "c")) } ]

let _ = assert_norm (twins_hold twins == true)
