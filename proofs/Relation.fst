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

   WHAT IS PROVED. The ladder states it, once: the rows of `../proofs.json` whose
   `evidence.model` is this file, each with its `statement` and its `boundary`. `README.md`
   renders them in this model's section. In one line here: the PASS is proved to emit a
   PARTITION of the nodes (Phase 553) and, since Phase 556, to be Tarjan's algorithm — every
   component a mutual-reachability class (`components_are_sccs`), the components
   dependencies-first (`components_deps_first`), so that `cycles_among` is exactly the
   self-reaching nodes (`cycles_among_meets_spec`) and `terminals` is its chain definition
   (`terminals_meets_spec`); the CLOSURE is proved sound and least; the INVERSE is proved to turn
   every edge round. The specification is stated concretely over the edge list (`mutual`,
   `on_cycle`, `deps_first`, `cycles_among_spec`, `terminals_spec`), and the differential still
   holds production, the extracted pass and the extracted specification together on every
   relation it draws: that guards the extraction and production, which no theorem reaches.
   The proof of the pass is section 4's strengthened invariant (clauses (h)-(k)), the FRAME
   invariant `frame_ok` the visit carries between a frame's successors, and section 7.

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
   3b. What the closure gives the bridge (Phase 556). The facts about `reachable` the proof of
       the pass reads: it is reflexive, holds every successor, is transitive, and the closure of
       a LIST of sources is the union of the closures of each (`reach_split`). The two list
       helpers here are not extracted: the oracle answers questions, the bridge argues about them.
   ====================================================================================== *)

let reach_refl (r:relation) (x:string) : Lemma (mem x (reachable r [x])) =
  reachable_closed r [x];
  subset_mem [x] (reachable r [x]) x

let reach_succ (r:relation) (x w:string)
  : Lemma (requires mem w (succ_of r x)) (ensures mem w (reachable r [x])) =
  reachable_closed r [x];
  reach_refl r x

let reach_trans (r:relation) (x y z:string)
  : Lemma (requires mem y (reachable r [x]) /\ mem z (reachable r [y])) (ensures mem z (reachable r [x])) =
  reachable_closed r [x];
  reachable_least r [y] (reachable r [x]);
  subset_mem (reachable r [y]) (reachable r [x]) z

(* By zero edges or more is: the node itself, or by one edge or more. *)
let reach_plus_char (r:relation) (x y:string)
  : Lemma (mem y (reachable r [x]) <==> (y = x \/ mem y (reach_plus r x))) =
  reachable_closed r [x];
  reachable_closed r (succ_of r x);
  reach_refl r x;
  subset_intro (succ_of r x) (reachable r [x]);
  reachable_least r (succ_of r x) (reachable r [x]);
  let s = x :: reach_plus r x in
  let closed (n w:string) : Lemma (requires mem n s /\ mem w (succ_of r n)) (ensures mem w s) =
    if n = x then subset_mem (succ_of r x) (reach_plus r x) w else ()
  in
  FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 closed);
  reachable_least r [x] s;
  (if mem y (reachable r [x]) then subset_mem (reachable r [x]) s y);
  (if mem y (reach_plus r x) then subset_mem (reach_plus r x) (reachable r [x]) y)

(* A node with no successors reaches itself alone, and nothing by one edge or more. *)
let reach_sink (r:relation) (v:string)
  : Lemma (requires succ_of r v == []) (ensures reachable r [v] == [v] /\ reach_plus r v == []) =
  (match assoc v r with Some l -> assert (l == []) | None -> ());
  assert (next_of r [v] [] == []);
  assert (reachable r [] == [])

(* `y` is reachable from SOME source in the list — the split of a closure over its sources. *)
[@@ noextract_to "FSharp"]
let rec any_reaches (r:relation) (ss:list string) (y:string) : Tot bool =
  match ss with
  | [] -> false
  | w :: t -> mem y (reachable r [w]) || any_reaches r t y

(* The source that reaches `y`, when one does. *)
[@@ noextract_to "FSharp"]
let rec reacher (r:relation) (ss:list string) (y:string) : Tot (option string) =
  match ss with
  | [] -> None
  | w :: t -> if mem y (reachable r [w]) then Some w else reacher r t y

let rec reacher_some (r:relation) (ss:list string) (y:string)
  : Lemma (requires any_reaches r ss y)
          (ensures (match reacher r ss y with Some w -> mem w ss /\ mem y (reachable r [w]) | None -> False)) =
  match ss with
  | [] -> ()
  | w :: t -> if mem y (reachable r [w]) then () else reacher_some r t y

let rec any_reaches_intro (r:relation) (ss:list string) (w y:string)
  : Lemma (requires mem w ss /\ mem y (reachable r [w])) (ensures any_reaches r ss y) =
  match ss with
  | [] -> ()
  | h :: t -> if h = w then () else any_reaches_intro r t w y

[@@ noextract_to "FSharp"]
let rec reach_all (r:relation) (ss:list string) : Tot (list string) =
  match ss with
  | [] -> []
  | w :: t -> app (reachable r [w]) (reach_all r t)

let rec mem_reach_all (r:relation) (ss:list string) (y:string)
  : Lemma (mem y (reach_all r ss) == any_reaches r ss y) =
  match ss with
  | [] -> ()
  | w :: t -> mem_app y (reachable r [w]) (reach_all r t); mem_reach_all r t y

let rec reach_all_closed (r:relation) (ss:list string) : Lemma (closed_by r (reach_all r ss)) =
  match ss with
  | [] -> ()
  | w :: t ->
    reach_all_closed r t;
    reachable_closed r [w];
    let aux (n x:string)
      : Lemma (requires mem n (reach_all r ss) /\ mem x (succ_of r n)) (ensures mem x (reach_all r ss)) =
      mem_app n (reachable r [w]) (reach_all r t);
      mem_app x (reachable r [w]) (reach_all r t)
    in
    FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 aux)

let rec reach_all_holds (r:relation) (ss:list string) (y:string)
  : Lemma (requires mem y ss) (ensures mem y (reach_all r ss)) =
  match ss with
  | [] -> ()
  | w :: t ->
    mem_app y (reachable r [w]) (reach_all r t);
    if y = w then reach_refl r w else reach_all_holds r t y

let rec any_reaches_within (r:relation) (ss l:list string) (y:string)
  : Lemma (requires any_reaches r l y /\ (forall (w:string). mem w l ==> mem w ss))
          (ensures mem y (reachable r ss)) =
  match l with
  | [] -> ()
  | w :: t ->
    if mem y (reachable r [w]) then begin
      reachable_closed r ss;
      subset_mem ss (reachable r ss) w;
      reachable_least r [w] (reachable r ss);
      subset_mem (reachable r [w]) (reachable r ss) y
    end
    else any_reaches_within r ss t y

(* THE SPLIT. What a list of sources reaches is what one of them reaches. *)
let reach_split (r:relation) (ss:list string) (y:string)
  : Lemma (mem y (reachable r ss) <==> any_reaches r ss y) =
  mem_reach_all r ss y;
  reach_all_closed r ss;
  FStar.Classical.forall_intro (FStar.Classical.move_requires (reach_all_holds r ss));
  subset_intro ss (reach_all r ss);
  reachable_least r ss (reach_all r ss);
  (if mem y (reachable r ss) then subset_mem (reachable r ss) (reach_all r ss) y);
  (if any_reaches r ss y then any_reaches_within r ss ss y)

let mutual_trans (r:relation) (x y z:string)
  : Lemma (requires mutual r x y /\ mutual r y z) (ensures mutual r x z) =
  reach_trans r x y z;
  reach_trans r z y x

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

(* Phase 556 — the segment of the stack ABOVE an id (what was pushed since it) and the stack
   UNDER it. Proof-only: the oracle pops with `pop_until`. *)
[@@ noextract_to "FSharp"]
let rec above_of (v:string) (s:list string) : Tot (list string) =
  match s with
  | [] -> []
  | w :: t -> if w = v then [] else w :: above_of v t

[@@ noextract_to "FSharp"]
let rec under_of (v:string) (s:list string) : Tot (list string) =
  match s with
  | [] -> []
  | w :: t -> if w = v then t else under_of v t

let rec above_under (v:string) (s:list string)
  : Lemma (requires mem v s)
          (ensures s == app (above_of v s) (v :: under_of v s) /\ not (mem v (above_of v s))) =
  match s with
  | [] -> ()
  | w :: t -> if w = v then () else above_under v t

let rec above_app (v:string) (a b:list string)
  : Lemma (requires not (mem v a)) (ensures above_of v (app a b) == app a (above_of v b)) =
  match a with
  | [] -> ()
  | _ :: t -> above_app v t b

(* The stack is pushed in index order: every id under one has a smaller index. *)
let rec stack_sorted (st:state) (s:list string) : Tot prop =
  match s with
  | [] -> True
  | a :: t -> (forall (b:string). mem b t ==> idx_of st b < idx_of st a) /\ stack_sorted st t

let rec sorted_same_idx (st st':state) (s:list string)
  : Lemma (requires stack_sorted st s /\ (forall (x:string). mem x s ==> idx_of st' x == idx_of st x))
          (ensures stack_sorted st' s) =
  match s with
  | [] -> ()
  | _ :: t -> sorted_same_idx st st' t

let rec sorted_under (st:state) (a s:list string)
  : Lemma (requires stack_sorted st (app a s)) (ensures stack_sorted st s) =
  match a with
  | [] -> ()
  | _ :: t -> sorted_under st t s

let rec sorted_split (st:state) (a:list string) (v:string) (b:list string)
  : Lemma (requires stack_sorted st (app a (v :: b)))
          (ensures (forall (m:string). mem m a ==> idx_of st v < idx_of st m) /\
                   (forall (y:string). mem y b ==> idx_of st y < idx_of st v)) =
  match a with
  | [] -> ()
  | h :: t -> mem_app v t (v :: b); sorted_split st t v b

(* The low-link WITNESS: an id on the stack reaches the id on the stack its low-link indexes —
   what `low[v] <- min low[v] order[w]` and `low[v] <- min low[v] low[w]` record, read as a path. *)
[@@"opaque_to_smt"]
let low_wit (r:relation) (st:state) (x:string) : Tot prop =
  exists (u:string). mem u st.stack /\ idx_of st u == low_of st x /\ mem u (reachable r [x])

(* An edge the pass has PROCESSED from `m`: its target is visited, and a target still on the
   stack either pulled `m`'s low-link down to its index or was pushed after `m`. *)
let edge_ok (st:state) (m w:string) : Tot prop =
  visited st w /\ (mem w st.stack ==> (low_of st m <= idx_of st w \/ idx_of st m < idx_of st w))

(* The components found come DEPENDENCIES-FIRST, newest first: every successor of a member lies
   in its own component or in one found before it. *)
let rec comps_ok (r:relation) (comps:list (list string)) : Tot prop =
  match comps with
  | [] -> True
  | c :: t -> succs_within r (app c (concat t)) c /\ comps_ok r t

(* Every component found is a mutual-reachability CLASS: its members reach one another, and
   whatever a member is mutually reachable with is a member. *)
[@@"opaque_to_smt"]
let sccs_ok (r:relation) (comps:list (list string)) : Tot prop =
  (forall (c:list string) (x y:string). mem c comps /\ mem x c /\ mem y c ==> mutual r x y) /\
  (forall (c:list string) (x y:string). mem c comps /\ mem x c /\ mutual r x y ==> mem y c)

(* A FINISHED DESCENDANT above the frame node `v`: its low-link is under its index (it did not
   pop) and at or above the frame's (folded in when it returned), it is reachable from the frame
   node, and every edge out of it is processed. *)
[@@"opaque_to_smt"]
let frame_member (r:relation) (st:state) (v m:string) : Tot prop =
  low_of st v <= low_of st m /\ low_of st m < idx_of st m /\ mem m (reachable r [v]) /\
  (forall (w:string). mem w (succ_of r m) ==> edge_ok st m w)

[@@"opaque_to_smt"]
let above_ok (r:relation) (st:state) (v:string) : Tot prop =
  forall (m:string). mem m (above_of v st.stack) ==> frame_member r st v m

(* Every successor the frame has consumed so far — the prefix of its successor list before the
   ones that remain — is a processed edge. *)
[@@"opaque_to_smt"]
let done_ok (r:relation) (st:state) (v:string) (succs:list string) : Tot prop =
  match prefix_to succs (succ_of r v) with
  | None -> True
  | Some done -> forall (w:string). mem w done ==> edge_ok st v w

(* THE FRAME INVARIANT of a node's loop over its successors, between two of them: every id above
   it on the stack is a finished descendant, and every successor consumed is processed. *)
[@@"opaque_to_smt"]
let frame_ok (r:relation) (st:state) (v:string) (succs:list string) : Tot prop =
  above_ok r st v /\ done_ok r st v succs

(* What `set_low` moves, and what it leaves: one low-link. *)
let set_low_self (st:state) (v:string) (n:nat) : Lemma (low_of (set_low st v n) v == n) = ()
let set_low_other (st:state) (v:string) (n:nat) (m:string)
  : Lemma (requires m <> v) (ensures low_of (set_low st v n) m == low_of st m) = ()
let set_low_rest (st:state) (v:string) (n:nat) (x:string)
  : Lemma ((set_low st v n).stack == st.stack /\ (set_low st v n).comps == st.comps /\
           (set_low st v n).idx == st.idx /\ (set_low st v n).next == st.next /\
           idx_of (set_low st v n) x == idx_of st x /\ visited (set_low st v n) x == visited st x) = ()

let rec above_mem (v:string) (s:list string) (m:string)
  : Lemma (requires mem m (above_of v s)) (ensures mem m s) =
  match s with
  | [] -> ()
  | w :: t -> if w = v then () else if m = w then () else above_mem v t m

let rec succs_within_mem (r:relation) (allowed ms:list string) (m w:string)
  : Lemma (requires succs_within r allowed ms /\ mem m ms /\ mem w (succ_of r m)) (ensures mem w allowed) =
  match ms with
  | [] -> ()
  | h :: t -> if h = m then subset_mem (succ_of r m) allowed w else succs_within_mem r allowed t m w

let rec succs_within_intro (r:relation) (allowed ms:list string)
  : Lemma (requires (forall (m w:string). mem m ms /\ mem w (succ_of r m) ==> mem w allowed))
          (ensures succs_within r allowed ms) =
  match ms with
  | [] -> ()
  | h :: t -> subset_intro (succ_of r h) allowed; succs_within_intro r allowed t

(* Dependencies-first components are CLOSED: no edge leaves what has been found. *)
let rec comps_ok_closed (r:relation) (comps:list (list string))
  : Lemma (requires comps_ok r comps) (ensures closed_by r (concat comps)) =
  match comps with
  | [] -> ()
  | c :: t ->
    comps_ok_closed r t;
    let aux (n w:string)
      : Lemma (requires mem n (concat comps) /\ mem w (succ_of r n)) (ensures mem w (concat comps)) =
      mem_app n c (concat t);
      mem_app w c (concat t);
      if mem n c then succs_within_mem r (app c (concat t)) c n w else ()
    in
    FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 aux)

let rec prefix_to_nil (l:list string) : Lemma (prefix_to [] l == Some l) =
  match l with
  | [] -> ()
  | _ :: t -> prefix_to_nil t

let rec len_app (#a:Type0) (l m:list a) : Lemma (len (app l m) == len l + len m) =
  match l with
  | [] -> ()
  | _ :: t -> len_app t m

let rec app_cancel_right (#a:eqtype) (x y s:list a)
  : Lemma (requires len x == len y /\ app x s == app y s) (ensures x == y) =
  match x, y with
  | hx :: tx, hy :: ty -> app_cancel_right tx ty s
  | _ -> ()

let rec app_is_suffix (a s:list string) : Lemma (suffix s (app a s)) =
  match a with
  | [] -> suffix_refl s
  | _ :: t -> app_is_suffix t s; suffix_cons s (app t s) (Cons?.hd a)

(* Consuming one successor: the processed prefix grows by it. *)
let prefix_to_step (w:string) (rest l:list string)
  : Lemma (requires suffix (w :: rest) l)
          (ensures suffix rest l /\ prefix_to rest l == Some (app (Some?.v (prefix_to (w :: rest) l)) [w])) =
  prefix_to_app (w :: rest) l;
  let done = Some?.v (prefix_to (w :: rest) l) in
  app_assoc done [w] rest;
  app_is_suffix (app done [w]) rest;
  prefix_to_app rest l;
  let done' = Some?.v (prefix_to rest l) in
  len_app done' rest;
  len_app (app done [w]) rest;
  app_cancel_right done' (app done [w]) rest

(* THE INVARIANT the pass keeps, clause by clause:
     (a) the stack and the components found hold no id twice between them;
     (b) an id is visited exactly when it is on the stack or in a component found;
     (c) a visited id is a node;
     (d) every index assigned is below `next`;
     (e) a visited id's low-link is at most its index;
     (f) every id on the stack has its low-link at or above the ROOT's index — the bottom of the
         stack — which is what makes a root pop: its low-link can never go below its own index;
     (g) no component found is empty;
   and, since Phase 556, the four clauses the bridge to the specification needs:
     (h) the stack is sorted by index, the newest push on top (`stack_sorted`);
     (i) every id on the stack has a low-link WITNESS: an id on the stack, at the index its
         low-link holds, that it reaches (`low_wit`);
     (j) the components found come dependencies-first (`comps_ok`);
     (k) every component found is a mutual-reachability class (`sccs_ok`). *)
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
  (forall (c:list string). mem c st.comps ==> Cons? c) /\
  stack_sorted st st.stack /\
  (forall (x:string). mem x st.stack ==> low_wit r st x) /\
  comps_ok r st.comps /\
  sccs_ok r st.comps

(* What a visit leaves alone: every id visited before it keeps its index and its low-link, except
   the frame's own node, whose low-link only falls; and every id found before it stays found. *)
let kept (st st':state) (frame:string) : Tot prop =
  (forall (x:string). visited st x ==> visited st' x /\ idx_of st' x == idx_of st x /\
                                   (x <> frame ==> low_of st' x == low_of st x)) /\
  (forall (x:string). mem x (concat st.comps) ==> mem x (concat st'.comps))

let inv_init (r:relation) : Lemma (inv r init) = reveal_opaque (`%sccs_ok) (sccs_ok r [])

(* Two steps that keep everything but one frame's low-link keep everything but that frame's; a
   child's step keeps the parent's low-link too, because the child was not visited before it. *)
let kept_trans (st st1 st2:state) (frame:string)
  : Lemma (requires kept st st1 frame /\ kept st1 st2 frame) (ensures kept st st2 frame) = ()

let kept_child (st st1 st2:state) (child frame:string)
  : Lemma (requires kept st st1 child /\ kept st1 st2 frame /\ not (visited st child))
          (ensures kept st st2 frame) = ()

(* The visit's measure never grows across a child's visit and the fold that follows it. *)
let measure_le (r:relation) (st st1 st2:state)
  : Lemma (requires (forall (x:string). visited st x ==> visited st1 x) /\ st2.idx == st1.idx)
          (ensures len (diff (nodes r) (keys st2.idx)) <= len (diff (nodes r) (keys st.idx))) =
  keys_grow st st1;
  diff_mono (nodes r) (keys st.idx) (keys st1.idx)

let last_cons (x:string) (l:list string) : Lemma (requires Cons? l) (ensures last (x :: l) == last l) =
  match l with
  | _ :: _ -> ()

let inv_enter (r:relation) (st:state) (v:string)
  : Lemma (requires inv r st /\ mem v (nodes r) /\ not (visited st v))
          (ensures inv r (enter st v) /\ kept st (enter st v) v) =
  let st' = enter st v in
  mem_app v st.stack (concat st.comps);
  (match last st.stack with
   | None -> if Cons? st.stack then last_mem st.stack
   | Some b ->
     last_mem st.stack;
     last_cons v st.stack;
     mem_app b st.stack (concat st.comps);
     let aux (x:string) : Lemma (mem x (v :: st.stack) ==> low_of st' x >= idx_of st' b) =
       if x = v then () else ()
     in
     FStar.Classical.forall_intro aux);
  (* (h): the pushed id's index is above every index assigned *)
  let idx_same (x:string) : Lemma (mem x st.stack ==> idx_of st' x == idx_of st x) =
    mem_app x st.stack (concat st.comps)
  in
  FStar.Classical.forall_intro idx_same;
  sorted_same_idx st st' st.stack;
  (* (i): the pushed id witnesses itself; every other keeps its witness *)
  let wit (x:string) : Lemma (requires mem x st'.stack) (ensures low_wit r st' x) =
    reveal_opaque (`%low_wit) (low_wit r st x); reveal_opaque (`%low_wit) (low_wit r st' x);
    if x = v then reach_refl r v
    else begin
      mem_app x st.stack (concat st.comps);
      eliminate exists (u:string). mem u st.stack /\ idx_of st u == low_of st x /\ mem u (reachable r [x])
      with begin
        mem_app u st.stack (concat st.comps);
        introduce exists (u':string). mem u' st'.stack /\ idx_of st' u' == low_of st' x /\ mem u' (reachable r [x])
        with u and ()
      end
    end
  in
  FStar.Classical.forall_intro (FStar.Classical.move_requires wit)

(* Lowering the frame node's low-link to a value at or above the root's index keeps the
   invariant, when the new value is witnessed: it is the old one, or the index of an id on the
   stack the frame node reaches. *)
let inv_set_low (r:relation) (st:state) (v:string) (n:nat)
  : Lemma (requires inv r st /\ mem v st.stack /\ n <= low_of st v /\
                    (match last st.stack with None -> true | Some b -> n >= idx_of st b) /\
                    (n == low_of st v \/
                     (exists (u:string). mem u st.stack /\ idx_of st u == n /\ mem u (reachable r [v]))))
          (ensures inv r (set_low st v n) /\ kept st (set_low st v n) v) =
  let st' = set_low st v n in
  mem_app v st.stack (concat st.comps);
  (match last st.stack with
   | None -> ()
   | Some b ->
     let aux (x:string) : Lemma (mem x st.stack ==> low_of st' x >= idx_of st' b) = () in
     FStar.Classical.forall_intro aux);
  sorted_same_idx st st' st.stack;
  let wit (x:string) : Lemma (requires mem x st'.stack) (ensures low_wit r st' x) =
    reveal_opaque (`%low_wit) (low_wit r st x); reveal_opaque (`%low_wit) (low_wit r st' x);
    if x = v && n <> low_of st v then begin
      eliminate exists (u:string). mem u st.stack /\ idx_of st u == n /\ mem u (reachable r [v])
      with begin
        introduce exists (u':string). mem u' st'.stack /\ idx_of st' u' == low_of st' v /\ mem u' (reachable r [v])
        with u and ()
      end
    end
    else begin
      eliminate exists (u:string). mem u st.stack /\ idx_of st u == low_of st x /\ mem u (reachable r [x])
      with begin
        introduce exists (u':string). mem u' st'.stack /\ idx_of st' u' == low_of st' x /\ mem u' (reachable r [x])
        with u and ()
      end
    end
  in
  FStar.Classical.forall_intro (FStar.Classical.move_requires wit)

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

(* Phase 556 — the shape of a ROOT at its pop. The root's segment is `above` and the root; the
   stack is sorted, so the segment's indices are above the root's and everything under the root
   is below it. *)
let root_shape (r:relation) (st:state) (v:string) (above below:list string)
  : Lemma (requires inv r st /\ st.stack == app above (v :: below) /\ not (mem v above))
          (ensures above_of v st.stack == above /\
                   (forall (m:string). mem m above ==> idx_of st v < idx_of st m) /\
                   (forall (y:string). mem y below ==> idx_of st y < idx_of st v)) =
  above_app v above (v :: below);
  app_nil above;
  sorted_split st above v below

(* At a root's pop, every edge out of the segment lands in the segment or in a component found:
   a target under the root would have pulled the root's low-link under its own index. *)
let pop_edges (r:relation) (st:state) (v:string) (above below:list string)
  : Lemma (requires inv r st /\ st.stack == app above (v :: below) /\ not (mem v above) /\
                    low_of st v == idx_of st v /\ frame_ok r st v [])
          (ensures (forall (m w:string). mem m (app above [v]) /\ mem w (succ_of r m) ==>
                                        mem w (app (app above [v]) (concat st.comps)))) =
  root_shape r st v above below;
  prefix_to_nil (succ_of r v);
  reveal_opaque (`%frame_ok) (frame_ok r st v []); reveal_opaque (`%above_ok) (above_ok r st v); reveal_opaque (`%done_ok) (done_ok r st v []);
  let aux (m w:string)
    : Lemma (requires mem m (app above [v]) /\ mem w (succ_of r m))
            (ensures mem w (app (app above [v]) (concat st.comps))) =
    mem_app m above [v];
    reveal_opaque (`%frame_member) (frame_member r st v m);
    mem_app w (app above [v]) (concat st.comps);
    mem_app w above [v];
    mem_app w st.stack (concat st.comps);
    mem_app w above (v :: below)
  in
  FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 aux)

(* The chain from a segment member down to its root: the member's low-link witness has a smaller
   index and lies at or above the root, so it reaches the root by the same argument; the member
   reaches the witness, hence the root. *)
let rec climbs (r:relation) (st:state) (v:string) (above below:list string) (m:string)
  : Lemma (requires inv r st /\ st.stack == app above (v :: below) /\ not (mem v above) /\
                    low_of st v == idx_of st v /\ frame_ok r st v [] /\ (mem m above \/ m == v))
          (ensures mem v (reachable r [m]))
          (decreases (idx_of st m)) =
  if m = v then reach_refl r v
  else begin
    root_shape r st v above below;
    reveal_opaque (`%frame_ok) (frame_ok r st v []); reveal_opaque (`%above_ok) (above_ok r st v); reveal_opaque (`%frame_member) (frame_member r st v m); reveal_opaque (`%low_wit) (low_wit r st m);
    mem_app m above (v :: below);
    eliminate exists (u:string). mem u st.stack /\ idx_of st u == low_of st m /\ mem u (reachable r [m])
    with begin
      mem_app u above (v :: below);
      climbs r st v above below u;
      reach_trans r m u v
    end
  end

(* THE CLASS. At a root's pop the segment is exactly what is mutually reachable with the root:
   each member is reachable from the root (a descendant) and reaches it (`climbs`); and whatever
   the root reaches lies in the segment or in a component found, where nothing reaches the root. *)
let pop_class (r:relation) (st:state) (v:string) (above below:list string)
  : Lemma (requires inv r st /\ st.stack == app above (v :: below) /\ not (mem v above) /\
                    low_of st v == idx_of st v /\ frame_ok r st v [])
          (ensures (forall (m:string). mem m (app above [v]) ==> mutual r v m) /\
                   (forall (y:string). mutual r v y ==> mem y (app above [v]))) =
  let members = app above [v] in
  let found = concat st.comps in
  root_shape r st v above below;
  pop_edges r st v above below;
  comps_ok_closed r st.comps;
  reveal_opaque (`%frame_ok) (frame_ok r st v []); reveal_opaque (`%above_ok) (above_ok r st v);
  let fwd (m:string) : Lemma (requires mem m members) (ensures mutual r v m) =
    mem_app m above [v];
    reveal_opaque (`%frame_member) (frame_member r st v m);
    (if m = v then reach_refl r v);
    climbs r st v above below m
  in
  FStar.Classical.forall_intro (FStar.Classical.move_requires fwd);
  let s = app members found in
  let closed (n w:string) : Lemma (requires mem n s /\ mem w (succ_of r n)) (ensures mem w s) =
    mem_app n members found;
    mem_app w members found
  in
  FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 closed);
  mem_app v members found;
  mem_app v above [v];
  reachable_least r [v] s;
  let bwd (y:string) : Lemma (requires mutual r v y) (ensures mem y members) =
    subset_mem (reachable r [v]) s y;
    mem_app y members found;
    if mem y found then begin
      reachable_least r [y] found;
      subset_mem (reachable r [y]) found v;
      distinct_app st.stack found;
      mem_app v above (v :: below)
    end
  in
  FStar.Classical.forall_intro (FStar.Classical.move_requires bwd)

(* Popping a root's component: the ids above and including `v` move from the stack to a new
   component; what was under `v` is the stack. Since Phase 556 the lemma also carries what the
   bridge needs of the new component: dependencies-first (`pop_edges`), a class (`pop_class`),
   the stack still sorted and every id left on it still witnessed. *)
let inv_pop (r:relation) (st:state) (v:string) (above below:list string)
  : Lemma (requires inv r st /\ st.stack == app above (v :: below) /\ not (mem v above) /\
                    low_of st v == idx_of st v /\ frame_ok r st v [])
          (ensures (let st' = { st with stack = below; comps = app above [v] :: st.comps } in
                    inv r st' /\ kept st st' v)) =
  let members = app above [v] in
  let found = concat st.comps in
  let st' = { st with stack = below; comps = members :: st.comps } in
  reveal_opaque (`%sccs_ok) (sccs_ok r st.comps); reveal_opaque (`%sccs_ok) (sccs_ok r st'.comps);
  app_last_cons above v;
  distinct_move above below found v;
  (match last below with
   | None -> ()
   | Some b ->
     last_app above (v :: below);
     last_cons v below;
     let aux2 (x:string) : Lemma (mem x below ==> low_of st' x >= idx_of st' b) =
       mem_app x above (v :: below)
     in
     FStar.Classical.forall_intro aux2);
  (* kept: found grows by the segment *)
  let grow (x:string) : Lemma (mem x found ==> mem x (concat st'.comps)) = mem_app x members found in
  FStar.Classical.forall_intro grow;
  (* (h) *)
  root_shape r st v above below;
  sorted_under st above (v :: below);
  sorted_same_idx st st' below;
  (* (i): a witness of an id left on the stack is under the root — its index is at most the id's *)
  let wit (x:string) : Lemma (requires mem x below) (ensures low_wit r st' x) =
    reveal_opaque (`%low_wit) (low_wit r st x); reveal_opaque (`%low_wit) (low_wit r st' x);
    mem_app x above (v :: below);
    mem_app x st.stack found;
    eliminate exists (u:string). mem u st.stack /\ idx_of st u == low_of st x /\ mem u (reachable r [x])
    with begin
      mem_app u above (v :: below);
      introduce exists (u':string). mem u' st'.stack /\ idx_of st' u' == low_of st' x /\ mem u' (reachable r [x])
      with u and ()
    end
  in
  FStar.Classical.forall_intro (FStar.Classical.move_requires wit);
  (* (j) *)
  pop_edges r st v above below;
  succs_within_intro r (app members found) members;
  (* (k) *)
  pop_class r st v above below;
  let pair (cc:list string) (x y:string)
    : Lemma (requires mem cc st'.comps /\ mem x cc /\ mem y cc) (ensures mutual r x y) =
    if cc = members then mutual_trans r x v y else ()
  in
  FStar.Classical.forall_intro_3 (FStar.Classical.move_requires_3 pair);
  let maximal (cc:list string) (x y:string)
    : Lemma (requires mem cc st'.comps /\ mem x cc /\ mutual r x y) (ensures mem y cc) =
    if cc = members then mutual_trans r v x y else ()
  in
  FStar.Classical.forall_intro_3 (FStar.Classical.move_requires_3 maximal)

let succs_are_nodes (r:relation) (v:string) : Lemma (forall (x:string). mem x (succ_of r v) ==> mem x (nodes r)) =
  FStar.Classical.forall_intro (FStar.Classical.move_requires (succ_is_node r v))

(* Phase 556 — the three ways a frame consumes a successor, each re-establishing the frame
   invariant for the successors that remain. *)

(* Found already: nothing moves, and the edge is processed because its target is off the stack. *)
let frame_skip (r:relation) (st:state) (v w:string) (rest:list string)
  : Lemma (requires inv r st /\ mem v st.stack /\ suffix (w :: rest) (succ_of r v) /\
                    frame_ok r st v (w :: rest) /\ visited st w /\ not (mem w st.stack))
          (ensures suffix rest (succ_of r v) /\ frame_ok r st v rest) =
  reveal_opaque (`%frame_ok) (frame_ok r st v (w :: rest)); reveal_opaque (`%frame_ok) (frame_ok r st v rest);
  reveal_opaque (`%done_ok) (done_ok r st v (w :: rest)); reveal_opaque (`%done_ok) (done_ok r st v rest);
  prefix_to_step w rest (succ_of r v);
  let done = Some?.v (prefix_to (w :: rest) (succ_of r v)) in
  let aux (w':string) : Lemma (mem w' (app done [w]) ==> edge_ok st v w') = mem_app w' done [w] in
  FStar.Classical.forall_intro aux

(* On the stack: the frame's low-link falls to the target's index — witnessed by the target,
   which the frame node reaches by that edge — and the edge is processed. *)
let frame_onstack (r:relation) (st:state) (v w:string) (rest:list string)
  : Lemma (requires inv r st /\ mem v st.stack /\ suffix (w :: rest) (succ_of r v) /\
                    frame_ok r st v (w :: rest) /\ mem w st.stack)
          (ensures (let n = min (low_of st v) (idx_of st w) in
                    n <= low_of st v /\
                    (match last st.stack with None -> true | Some b -> n >= idx_of st b) /\
                    (n == low_of st v \/
                     (exists (u:string). mem u st.stack /\ idx_of st u == n /\ mem u (reachable r [v]))) /\
                    suffix rest (succ_of r v) /\ frame_ok r (set_low st v n) v rest)) =
  let n = min (low_of st v) (idx_of st w) in
  let st' = set_low st v n in
  reveal_opaque (`%frame_ok) (frame_ok r st v (w :: rest)); reveal_opaque (`%frame_ok) (frame_ok r st' v rest);
  reveal_opaque (`%above_ok) (above_ok r st v); reveal_opaque (`%above_ok) (above_ok r st' v); reveal_opaque (`%done_ok) (done_ok r st v (w :: rest)); reveal_opaque (`%done_ok) (done_ok r st' v rest);
  mem_app v st.stack (concat st.comps);
  mem_app w st.stack (concat st.comps);
  last_mem st.stack;
  mem_app (Some?.v (last st.stack)) st.stack (concat st.comps);
  suffix_mem (w :: rest) (succ_of r v) w;
  reach_succ r v w;
  (if n <> low_of st v then
     introduce exists (u:string). mem u st.stack /\ idx_of st u == n /\ mem u (reachable r [v]) with w and ());
  prefix_to_step w rest (succ_of r v);
  let done = Some?.v (prefix_to (w :: rest) (succ_of r v)) in
  above_under v st.stack;
  let old (m:string) : Lemma (requires mem m (above_of v st.stack)) (ensures frame_member r st' v m) =
    reveal_opaque (`%frame_member) (frame_member r st v m); reveal_opaque (`%frame_member) (frame_member r st' v m);
    set_low_self st v n; set_low_other st v n m; set_low_rest st v n m;
    let e (w':string) : Lemma (requires mem w' (succ_of r m)) (ensures edge_ok st' m w') = set_low_rest st v n w' in
    FStar.Classical.forall_intro (FStar.Classical.move_requires e)
  in
  FStar.Classical.forall_intro (FStar.Classical.move_requires old);
  let dn (w':string) : Lemma (mem w' (app done [w]) ==> edge_ok st' v w') = mem_app w' done [w] in
  FStar.Classical.forall_intro dn

(* A processed edge stays processed across a step that keeps the target's record, grows the found
   set, and lowers (never raises) the frame's low-link. *)
let edge_kept (st st':state) (m w:string)
  : Lemma (requires edge_ok st m w /\
                    (visited st w ==> visited st' w /\ idx_of st' w == idx_of st w /\
                                      (mem w st.stack \/ mem w (concat st.comps))) /\
                    (mem w (concat st.comps) ==> mem w (concat st'.comps)) /\
                    (mem w st'.stack ==> not (mem w (concat st'.comps))) /\
                    idx_of st' m == idx_of st m /\ low_of st' m <= low_of st m)
          (ensures edge_ok st' m w) = ()

(* What a child's visit pushed and left: the segment above the child, and the child, on top of
   the stack the visit entered with — and none of it was on that stack. *)
let child_segment (st st1:state) (w:string)
  : Lemma (requires distinct st1.stack /\ suffix (w :: st.stack) st1.stack)
          (ensures st1.stack == app (app (above_of w st1.stack) [w]) st.stack /\
                   not (mem w st.stack) /\
                   (forall (x:string). mem x (app (above_of w st1.stack) [w]) ==> not (mem x st.stack))) =
  prefix_to_app (w :: st.stack) st1.stack;
  let p = Some?.v (prefix_to (w :: st.stack) st1.stack) in
  distinct_app p (w :: st.stack);
  above_app w p (w :: st.stack);
  app_nil p;
  app_assoc p [w] st.stack;
  let aux (x:string) : Lemma (mem x (app p [w]) ==> not (mem x st.stack)) = mem_app x p [w] in
  FStar.Classical.forall_intro aux

(* The folded low-link is witnessed: it is the frame's own, or the child's, whose witness the
   frame node reaches through the child. *)
let child_witness (r:relation) (st1:state) (v w:string) (n:nat)
  : Lemma (requires (forall (x:string). mem x st1.stack ==> low_wit r st1 x) /\
                    n == min (low_of st1 v) (low_of st1 w) /\ mem w (reachable r [v]) /\
                    (mem w st1.stack \/ low_of st1 w == idx_of st1 w) /\
                    low_of st1 v <= idx_of st1 v /\ idx_of st1 v < idx_of st1 w)
          (ensures n == low_of st1 v \/
                   (exists (u:string). mem u st1.stack /\ idx_of st1 u == n /\ mem u (reachable r [v]))) =
  if n = low_of st1 v then ()
  else begin
    reveal_opaque (`%low_wit) (low_wit r st1 w);
    eliminate exists (u:string). mem u st1.stack /\ idx_of st1 u == low_of st1 w /\ mem u (reachable r [w])
    with begin
      reach_trans r v w u;
      introduce exists (u':string). mem u' st1.stack /\ idx_of st1 u' == n /\ mem u' (reachable r [v])
      with u and ()
    end
  end

(* The frame's consumed successors, after the child's visit: the ones consumed before stay
   processed, and the edge to the child is processed because the child was pushed after the
   frame node. *)
let frame_child_done (r:relation) (st st1:state) (v w:string) (rest:list string) (n:nat)
  : Lemma (requires (forall (x:string). visited st x <==> mem x (app st.stack (concat st.comps))) /\
                    distinct (app st1.stack (concat st1.comps)) /\
                    kept st st1 w /\ visited st v /\ visited st1 w /\ idx_of st1 w == st.next /\
                    idx_of st v < st.next /\ v <> w /\
                    suffix (w :: rest) (succ_of r v) /\ done_ok r st v (w :: rest) /\ n <= low_of st1 v)
          (ensures done_ok r (set_low st1 v n) v rest) =
  let st2 = set_low st1 v n in
  reveal_opaque (`%done_ok) (done_ok r st v (w :: rest)); reveal_opaque (`%done_ok) (done_ok r st2 v rest);
  prefix_to_step w rest (succ_of r v);
  let done = Some?.v (prefix_to (w :: rest) (succ_of r v)) in
  distinct_app st1.stack (concat st1.comps);
  set_low_self st1 v n;
  let each (w':string) : Lemma (requires mem w' (app done [w])) (ensures edge_ok st2 v w') =
    mem_app w' done [w];
    set_low_rest st1 v n w';
    set_low_rest st1 v n v;
    if mem w' done then begin
      mem_app w' st.stack (concat st.comps);
      edge_kept st st2 v w'
    end
  in
  FStar.Classical.forall_intro (FStar.Classical.move_requires each)

(* A finished descendant of the frame node stays one across the child's visit and the fold: its
   record is kept (it is neither the child nor the frame node), and the frame's low-link only
   fell. *)
let frame_member_kept (r:relation) (st st1:state) (v w m:string) (n:nat)
  : Lemma (requires (forall (x:string). visited st x <==> mem x (app st.stack (concat st.comps))) /\
                    distinct (app st1.stack (concat st1.comps)) /\
                    kept st st1 w /\ visited st m /\ m <> v /\ m <> w /\ v <> w /\ n <= low_of st1 v /\
                    visited st v /\ frame_member r st v m)
          (ensures frame_member r (set_low st1 v n) v m) =
  let st2 = set_low st1 v n in
  reveal_opaque (`%frame_member) (frame_member r st v m); reveal_opaque (`%frame_member) (frame_member r st2 v m);
  distinct_app st1.stack (concat st1.comps);
  set_low_self st1 v n;
  set_low_other st1 v n m;
  set_low_rest st1 v n m;
  let e (w':string) : Lemma (requires mem w' (succ_of r m)) (ensures edge_ok st2 m w') =
    mem_app w' st.stack (concat st.comps);
    set_low_rest st1 v n w';
    edge_kept st st2 m w'
  in
  FStar.Classical.forall_intro (FStar.Classical.move_requires e)

(* A finished descendant of the CHILD is a finished descendant of the frame node once the child's
   low-link is folded in: the frame's low-link is at most the child's, the child is a successor,
   and nothing of the descendant's record moved. *)
let frame_member_lift (r:relation) (st1:state) (v w m:string) (n:nat)
  : Lemma (requires frame_member r st1 w m /\ mem w (reachable r [v]) /\ n <= low_of st1 w /\ m <> v)
          (ensures frame_member r (set_low st1 v n) v m) =
  let st2 = set_low st1 v n in
  reveal_opaque (`%frame_member) (frame_member r st1 w m); reveal_opaque (`%frame_member) (frame_member r st2 v m);
  set_low_self st1 v n;
  set_low_other st1 v n m;
  set_low_rest st1 v n m;
  reach_trans r v w m;
  let e (w':string) : Lemma (requires mem w' (succ_of r m)) (ensures edge_ok st2 m w') =
    set_low_rest st1 v n w'
  in
  FStar.Classical.forall_intro (FStar.Classical.move_requires e)

(* The child itself, left on the stack, is a finished descendant of the frame node. *)
let frame_child_member (r:relation) (st1:state) (v w:string) (n:nat)
  : Lemma (requires done_ok r st1 w [] /\ low_of st1 w < idx_of st1 w /\ mem w (reachable r [v]) /\
                    n <= low_of st1 w /\ w <> v)
          (ensures frame_member r (set_low st1 v n) v w) =
  let st2 = set_low st1 v n in
  reveal_opaque (`%done_ok) (done_ok r st1 w []); reveal_opaque (`%frame_member) (frame_member r st2 v w);
  set_low_self st1 v n;
  set_low_other st1 v n w;
  set_low_rest st1 v n w;
  prefix_to_nil (succ_of r w);
  let e (w':string) : Lemma (requires mem w' (succ_of r w)) (ensures edge_ok st2 w w') =
    set_low_rest st1 v n w'
  in
  FStar.Classical.forall_intro (FStar.Classical.move_requires e)

(* What is above the frame node after the child's visit and the fold: what the child left
   (lifted) and what was above before (kept). *)
let frame_child_above (r:relation) (st st1:state) (v w:string) (n:nat)
  : Lemma (requires inv r st /\ inv r st1 /\ kept st st1 w /\ mem v st.stack /\ not (visited st w) /\
                    mem w (reachable r [v]) /\ suffix st.stack st1.stack /\ above_ok r st v /\
                    n <= low_of st1 v /\ n <= low_of st1 w /\
                    (mem w st1.stack ==> low_of st1 w < idx_of st1 w /\ suffix (w :: st.stack) st1.stack /\
                                       above_ok r st1 w /\ done_ok r st1 w []) /\
                    (not (mem w st1.stack) ==> st1.stack == st.stack))
          (ensures above_ok r (set_low st1 v n) v) =
  let st2 = set_low st1 v n in
  reveal_opaque (`%above_ok) (above_ok r st v); reveal_opaque (`%above_ok) (above_ok r st2 v); reveal_opaque (`%above_ok) (above_ok r st1 w);
  mem_app v st.stack (concat st.comps);
  above_under v st.stack;
  distinct_app st1.stack (concat st1.comps);
  set_low_rest st1 v n v;
  let old (m:string) : Lemma (requires mem m (above_of v st.stack)) (ensures frame_member r st2 v m) =
    above_mem v st.stack m;
    mem_app m st.stack (concat st.comps);
    frame_member_kept r st st1 v w m n
  in
  if mem w st1.stack then begin
    child_segment st st1 w;
    let seg = app (above_of w st1.stack) [w] in
    above_app v seg st.stack;
    let newm (m:string) : Lemma (requires mem m (above_of v st2.stack)) (ensures frame_member r st2 v m) =
      mem_app m seg (above_of v st.stack);
      mem_app m (above_of w st1.stack) [w];
      if mem m (above_of w st1.stack) then frame_member_lift r st1 v w m n
      else if m = w then frame_child_member r st1 v w n
      else old m
    in
    FStar.Classical.forall_intro (FStar.Classical.move_requires newm)
  end
  else begin
    let newm (m:string) : Lemma (requires mem m (above_of v st2.stack)) (ensures frame_member r st2 v m) = old m in
    FStar.Classical.forall_intro (FStar.Classical.move_requires newm)
  end

(* Fresh: visited as a child, its low-link folded into the frame's when its frame finishes. What
   it pushed and left sits above the frame node, finished; the edge to it is processed. *)
let frame_child (r:relation) (st st1:state) (v w:string) (rest:list string)
  : Lemma (requires inv r st /\ mem v st.stack /\ suffix (w :: rest) (succ_of r v) /\
                    frame_ok r st v (w :: rest) /\ not (visited st w) /\
                    inv r st1 /\ kept st st1 w /\ visited st1 w /\ suffix st.stack st1.stack /\
                    idx_of st1 w == st.next /\
                    (mem w st1.stack ==> low_of st1 w < idx_of st1 w /\ suffix (w :: st.stack) st1.stack /\
                                       frame_ok r st1 w []) /\
                    (not (mem w st1.stack) ==> st1.stack == st.stack /\ low_of st1 w == idx_of st1 w))
          (ensures (let n = min (low_of st1 v) (low_of st1 w) in
                    n <= low_of st1 v /\
                    (match last st1.stack with None -> true | Some b -> n >= idx_of st1 b) /\
                    (n == low_of st1 v \/
                     (exists (u:string). mem u st1.stack /\ idx_of st1 u == n /\ mem u (reachable r [v]))) /\
                    suffix rest (succ_of r v) /\ frame_ok r (set_low st1 v n) v rest)) =
  let n = min (low_of st1 v) (low_of st1 w) in
  reveal_opaque (`%frame_ok) (frame_ok r st v (w :: rest)); reveal_opaque (`%frame_ok) (frame_ok r st1 w []); reveal_opaque (`%frame_ok) (frame_ok r (set_low st1 v n) v rest);
  mem_app v st.stack (concat st.comps);
  suffix_mem st.stack st1.stack v;
  mem_app v st1.stack (concat st1.comps);
  mem_app w st1.stack (concat st1.comps);
  last_mem st1.stack;
  mem_app (Some?.v (last st1.stack)) st1.stack (concat st1.comps);
  suffix_mem (w :: rest) (succ_of r v) w;
  reach_succ r v w;
  child_witness r st1 v w n;
  prefix_to_step w rest (succ_of r v);
  frame_child_done r st st1 v w rest n;
  frame_child_above r st st1 v w n

(* A fresh frame: nothing above the node just pushed, and no successor consumed yet. *)
let frame_fresh (r:relation) (st:state) (v:string) : Lemma (frame_ok r (enter st v) v (succ_of r v)) =
  let st1 = enter st v in
  reveal_opaque (`%frame_ok) (frame_ok r st1 v (succ_of r v));
  reveal_opaque (`%above_ok) (above_ok r st1 v);
  reveal_opaque (`%done_ok) (done_ok r st1 v (succ_of r v))

(* The visit. `visit` enters a fresh node and runs its frame; `visit_succs` is the frame's loop
   over the node's successors, in order: a fresh successor is visited and its low-link folded in
   when its frame finishes; one on the stack lowers the frame's low-link to its index; any other
   is skipped. `visit` then emits the component whose root the node is, when it is one.
   Termination: a visit enters one more node of the relation; a frame's loop consumes one
   successor per step and the nodes still unvisited never grow.
   Since Phase 556 both carry the FRAME INVARIANT (`frame_ok`): what a visit leaves above the
   stack it entered with is a finished, witnessed, processed segment under the node it visited. *)
let rec visit (r:relation) (st:state) (v:string)
  : Pure state
    (requires inv r st /\ mem v (nodes r) /\ not (visited st v))
    (ensures fun st' -> inv r st' /\ kept st st' v /\ visited st' v /\ suffix st.stack st'.stack /\
                     idx_of st' v == st.next /\
                     (mem v st'.stack ==> low_of st' v < idx_of st' v /\ suffix (v :: st.stack) st'.stack /\
                                        frame_ok r st' v []) /\
                     (not (mem v st'.stack) ==> st'.stack == st.stack /\ low_of st' v == idx_of st' v))
    (decreases %[len (diff (nodes r) (keys st.idx)); 0]) =
  inv_enter r st v;
  mem_app v st.stack (concat st.comps);
  let st1 = enter st v in
  has_key_keys v st.idx;
  diff_shrink (nodes r) (keys st.idx) (keys st1.idx) v;
  succs_are_nodes r v;
  suffix_refl (succ_of r v);
  frame_fresh r st v;
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
    (requires inv r st /\ mem v st.stack /\ (forall (x:string). mem x succs ==> mem x (nodes r)) /\
              suffix succs (succ_of r v) /\ frame_ok r st v succs)
    (ensures fun st' -> inv r st' /\ kept st st' v /\ suffix st.stack st'.stack /\
                     low_of st' v <= low_of st v /\ frame_ok r st' v [])
    (decreases %[len (diff (nodes r) (keys st.idx)); len succs + 1]) =
  match succs with
  | [] -> suffix_refl st.stack; st
  | w :: rest ->
    mem_app v st.stack (concat st.comps);
    mem_app w st.stack (concat st.comps);
    if not (visited st w) then begin
      let st1 = visit r st w in
      suffix_mem st.stack st1.stack v;
      frame_child r st st1 v w rest;
      inv_set_low r st1 v (min (low_of st1 v) (low_of st1 w));
      let st2 = set_low st1 v (min (low_of st1 v) (low_of st1 w)) in
      set_low_rest st1 v (min (low_of st1 v) (low_of st1 w)) v;
      measure_le r st st1 st2;
      let st3 = visit_succs r st2 v rest in
      suffix_trans st.stack st1.stack st3.stack;
      kept_child st st1 st2 w v;
      kept_trans st st2 st3 v;
      st3
    end
    else if mem w st.stack then begin
      frame_onstack r st v w rest;
      inv_set_low r st v (min (low_of st v) (idx_of st w));
      let st1 = set_low st v (min (low_of st v) (idx_of st w)) in
      set_low_rest st v (min (low_of st v) (idx_of st w)) v;
      let st2 = visit_succs r st1 v rest in
      kept_trans st st1 st2 v;
      st2
    end
    else begin
      frame_skip r st v w rest;
      visit_succs r st v rest
    end

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
   7. THEOREMS — the bridge to the specification (Phase 556): Tarjan's correctness, read off
      the invariant the pass carries. Each theorem is stated over the concrete relation and
      conditional on nothing.
   ====================================================================================== *)

(* THEOREM — components_are_sccs. Every component the pass emits is a mutual-reachability
   class: its members reach one another, and whatever a member is mutually reachable with is a
   member — clause (k) of the invariant, read at the end of the pass. *)
let components_are_sccs (r:relation)
  : Lemma ((forall (c:list string) (x y:string). mem c (components r) /\ mem x c /\ mem y c ==> mutual r x y) /\
           (forall (c:list string) (x y:string). mem c (components r) /\ mem x c /\ mutual r x y ==> mem y c)) =
  inv_init r;
  let st = roots r init (nodes r) in
  reveal_opaque (`%sccs_ok) (sccs_ok r st.comps);
  assert (components r == rev st.comps);
  let aux (c:list string) : Lemma (mem c (components r) == mem c st.comps) = rev_mem c st.comps in
  FStar.Classical.forall_intro aux

(* The component holding an id, when one does. *)
[@@ noextract_to "FSharp"]
let rec comp_of (comps:list (list string)) (x:string) : Tot (option (list string)) =
  match comps with
  | [] -> None
  | c :: t -> if mem x c then Some c else comp_of t x

let rec comp_of_some (comps:list (list string)) (x:string)
  : Lemma (requires mem x (concat comps))
          (ensures (match comp_of comps x with Some c -> mem c comps /\ mem x c | None -> False)) =
  match comps with
  | [] -> ()
  | c :: t -> mem_app x c (concat t); if mem x c then () else comp_of_some t x

(* COROLLARY — same_component_iff. For a node, "mutually reachable with" is "in the same
   component as": the whole guarantee, both directions, through the component the pass holds
   the node in. *)
let same_component_iff (r:relation) (x y:string)
  : Lemma (requires mem x (nodes r))
          (ensures (match comp_of (components r) x with
                    | Some c -> mem c (components r) /\ mem x c /\ (mutual r x y <==> mem y c)
                    | None -> False)) =
  components_partition r;
  components_are_sccs r;
  subset_mem (nodes r) (concat (components r)) x;
  comp_of_some (components r) x

(* `succs_within` and `deps_first` read their allowed set by membership alone. *)
let rec subset_same (l a b:list string)
  : Lemma (requires (forall (x:string). mem x a <==> mem x b)) (ensures subset l a == subset l b) =
  match l with
  | [] -> ()
  | _ :: t -> subset_same t a b

let rec succs_within_same (r:relation) (a b ms:list string)
  : Lemma (requires (forall (x:string). mem x a <==> mem x b)) (ensures succs_within r a ms == succs_within r b ms) =
  match ms with
  | [] -> ()
  | m :: t -> subset_same (succ_of r m) a b; succs_within_same r a b t

(* `deps_first` over a list ending in `c`: the earlier components as before, then `c` against
   all of them. *)
let rec deps_first_snoc (r:relation) (e:list string) (a:list (list string)) (c:list string)
  : Lemma (ensures deps_first r e (app a [c]) == (deps_first r e a && succs_within r (app c (app (concat a) e)) c))
          (decreases a) =
  match a with
  | [] -> ()
  | c1 :: t ->
    deps_first_snoc r (app c1 e) t c;
    let aux (x:string)
      : Lemma (mem x (app c (app (concat t) (app c1 e))) <==> mem x (app c (app (app c1 (concat t)) e))) =
      mem_app x c (app (concat t) (app c1 e)); mem_app x (concat t) (app c1 e); mem_app x c1 e;
      mem_app x c (app (app c1 (concat t)) e); mem_app x (app c1 (concat t)) e; mem_app x c1 (concat t)
    in
    FStar.Classical.forall_intro aux;
    succs_within_same r (app c (app (concat t) (app c1 e))) (app c (app (app c1 (concat t)) e)) c

(* Clause (j), newest first, is `deps_first` over the components in the order they were found. *)
let rec comps_ok_deps_first (r:relation) (comps:list (list string))
  : Lemma (requires comps_ok r comps) (ensures deps_first r [] (rev comps)) =
  match comps with
  | [] -> ()
  | c :: t ->
    comps_ok_deps_first r t;
    deps_first_snoc r [] (rev t) c;
    let aux (x:string) : Lemma (mem x (app c (app (concat (rev t)) [])) <==> mem x (app c (concat t))) =
      mem_app x c (app (concat (rev t)) []); app_nil (concat (rev t)); concat_rev_mem x t; mem_app x c (concat t)
    in
    FStar.Classical.forall_intro aux;
    succs_within_same r (app c (app (concat (rev t)) [])) (app c (concat t)) c

(* THEOREM — components_deps_first. The components come DEPENDENCIES-FIRST: every successor of a
   member lies in its own component or in one emitted earlier — the checker `deps_first`, which
   the differential holds the pass to, as a theorem of the pass. *)
let components_deps_first (r:relation) : Lemma (deps_first r [] (components r)) =
  inv_init r;
  let st = roots r init (nodes r) in
  comps_ok_deps_first r st.comps

(* The components a prefix of the order was emitted before: their members are where every
   successor of the next component's members lies, besides that component itself. *)
let rec deps_first_prefix (r:relation) (e:list string) (done:list (list string)) (c:list string) (t:list (list string))
  : Lemma (requires deps_first r e (app done (c :: t)))
          (ensures succs_within r (app c (app (concat done) e)) c)
          (decreases done) =
  match done with
  | [] -> ()
  | d :: rest ->
    deps_first_prefix r (app d e) rest c t;
    let aux (x:string)
      : Lemma (mem x (app c (app (concat rest) (app d e))) <==> mem x (app c (app (app d (concat rest)) e))) =
      mem_app x c (app (concat rest) (app d e)); mem_app x (concat rest) (app d e); mem_app x d e;
      mem_app x c (app (app d (concat rest)) e); mem_app x (app d (concat rest)) e; mem_app x d (concat rest)
    in
    FStar.Classical.forall_intro aux;
    succs_within_same r (app c (app (concat rest) (app d e))) (app c (app (app d (concat rest)) e)) c

(* ---- `cyclesAmong` ---- *)

let rec mem_cyclic_nodes (r:relation) (ns:list string) (x:string)
  : Lemma (mem x (cyclic_nodes r ns) == (mem x ns && on_cycle r x)) =
  match ns with
  | [] -> ()
  | _ :: t -> mem_cyclic_nodes r t x

let rec cyclic_of_sub (r:relation) (comps:list (list string)) (x:string)
  : Lemma (requires mem x (concat (cyclic_of r comps))) (ensures mem x (concat comps)) =
  match comps with
  | [] -> ()
  | c :: t ->
    mem_app x c (concat t);
    if is_cycle r c then begin
      mem_app x c (concat (cyclic_of r t));
      if mem x c then () else cyclic_of_sub r t x
    end
    else cyclic_of_sub r t x

(* An id is among the cycles' members exactly when its own component is a cycle. *)
let rec mem_cyclic_of (r:relation) (comps:list (list string)) (x:string)
  : Lemma (requires distinct (concat comps))
          (ensures mem x (concat (cyclic_of r comps)) <==>
                   (match comp_of comps x with Some c -> is_cycle r c | None -> False)) =
  match comps with
  | [] -> ()
  | c :: t ->
    distinct_app c (concat t);
    mem_app x c (concat t);
    (if is_cycle r c then mem_app x c (concat (cyclic_of r t)));
    if mem x c then (if mem x (concat (cyclic_of r t)) then cyclic_of_sub r t x)
    else mem_cyclic_of r t x

let rec concat_distinct_each (comps:list (list string)) (c:list string)
  : Lemma (requires distinct (concat comps) /\ mem c comps) (ensures distinct c) =
  match comps with
  | [] -> ()
  | h :: t -> distinct_app h (concat t); if h = c then () else concat_distinct_each t c

(* A member of a cycle component is on a cycle: by its self-edge, or through a second member it
   is mutually reachable with. *)
let cycle_member_on_cycle (r:relation) (c:list string) (x:string)
  : Lemma (requires distinct c /\ is_cycle r c /\ mem x c /\ (forall (y:string). mem y c ==> mutual r x y))
          (ensures on_cycle r x) =
  reachable_closed r (succ_of r x);
  match c with
  | [v] -> subset_mem (succ_of r v) (reach_plus r v) v
  | a :: b :: _ ->
    let y = if x = a then b else a in
    reach_plus_char r x y;
    reachable_least r [y] (reach_plus r x);
    subset_mem (reachable r [y]) (reach_plus r x) x

(* A node on a cycle sits in a cycle component: a singleton it is reached again from only
   through a successor mutually reachable with it, which the class then holds — itself. *)
let on_cycle_component (r:relation) (x:string) (c:list string)
  : Lemma (requires on_cycle r x /\ mem x c /\ (forall (y:string). mutual r x y ==> mem y c))
          (ensures is_cycle r c) =
  match c with
  | [] -> ()
  | [v] ->
    reach_split r (succ_of r v) v;
    reacher_some r (succ_of r v) v;
    (match reacher r (succ_of r v) v with
     | Some w -> reach_succ r v w
     | None -> ())
  | _ -> ()

(* THEOREM — cycles_among_meets_spec. `cyclesAmong` is exactly the nodes that reach themselves
   by one edge or more: each once, and as a set the specification's list. *)
let cycles_among_meets_spec (r:relation)
  : Lemma (distinct (cycles_among r) /\ same_set (cycles_among r) (cycles_among_spec r)) =
  components_partition r;
  components_are_sccs r;
  sort_partition r;
  distinct_app (sort r).torder (concat (sort r).tcycles);
  let comps = components r in
  assert (cycles_among r == concat (cyclic_of r comps));
  assert (cycles_among_spec r == cyclic_nodes r (nodes r));
  let each (x:string) : Lemma (mem x (cycles_among r) <==> mem x (cycles_among_spec r)) =
    mem_cyclic_nodes r (nodes r) x;
    mem_cyclic_of r comps x;
    if mem x (concat comps) then begin
      subset_mem (concat comps) (nodes r) x;
      comp_of_some comps x;
      match comp_of comps x with
      | Some c ->
        concat_distinct_each comps c;
        let pair (y:string) : Lemma (mem y c ==> mutual r x y) = () in
        FStar.Classical.forall_intro pair;
        let maxi (y:string) : Lemma (mutual r x y ==> mem y c) = () in
        FStar.Classical.forall_intro maxi;
        (if is_cycle r c then cycle_member_on_cycle r c x);
        (if on_cycle r x then on_cycle_component r x c)
      | None -> ()
    end
    else begin
      (if mem x (concat (cyclic_of r comps)) then cyclic_of_sub r comps x);
      (if mem x (nodes r) then subset_mem (nodes r) (concat comps) x)
    end
  in
  FStar.Classical.forall_intro each;
  subset_intro (cycles_among r) (cycles_among_spec r);
  subset_intro (cycles_among_spec r) (cycles_among r)

(* ---- `terminals` ---- *)

let rec any_on_cycle_intro (r:relation) (ns:list string) (y:string)
  : Lemma (requires mem y ns /\ on_cycle r y) (ensures any_on_cycle r ns) =
  match ns with
  | [] -> ()
  | n :: t -> if n = y then () else any_on_cycle_intro r t y

[@@ noextract_to "FSharp"]
let rec first_on_cycle (r:relation) (ns:list string) : Tot (option string) =
  match ns with
  | [] -> None
  | n :: t -> if on_cycle r n then Some n else first_on_cycle r t

let rec any_on_cycle_elim (r:relation) (ns:list string)
  : Lemma (requires any_on_cycle r ns)
          (ensures (match first_on_cycle r ns with Some y -> mem y ns /\ on_cycle r y | None -> False)) =
  match ns with
  | [] -> ()
  | n :: t -> if on_cycle r n then () else any_on_cycle_elim r t

let rec mem_sinks_among (r:relation) (ns:list string) (z:string)
  : Lemma (mem z (sinks_among r ns) == (mem z ns && Nil? (succ_of r z))) =
  match ns with
  | [] -> ()
  | _ :: t -> mem_sinks_among r t z

(* `bind_all` binds every member to the one answer and shadows no other id. *)
let rec bind_all_other (ms:list string) (a:option (list string)) (ends:list (string & option (list string))) (w:string)
  : Lemma (requires not (mem w ms)) (ensures ends_of (bind_all ms a ends) w == ends_of ends w) =
  match ms with
  | [] -> ()
  | m :: t -> bind_all_other t a ((m, a) :: ends) w

let rec bind_all_mem (ms:list string) (a:option (list string)) (ends:list (string & option (list string))) (w:string)
  : Lemma (requires mem w ms) (ensures ends_of (bind_all ms a ends) w == a) =
  match ms with
  | [] -> ()
  | m :: t -> if mem w t then bind_all_mem t a ((m, a) :: ends) w else bind_all_other t a ((m, a) :: ends) w

(* `join_ends`, read: `None` when any successor's end is, else the union of their ends. *)
[@@ noextract_to "FSharp"]
let rec any_none (ends:list (string & option (list string))) (ws:list string) : Tot bool =
  match ws with
  | [] -> false
  | w :: t -> None? (ends_of ends w) || any_none ends t

[@@ noextract_to "FSharp"]
let rec first_none (ends:list (string & option (list string))) (ws:list string) : Tot (option string) =
  match ws with
  | [] -> None
  | w :: t -> if None? (ends_of ends w) then Some w else first_none ends t

let rec any_none_intro (ends:list (string & option (list string))) (ws:list string) (w:string)
  : Lemma (requires mem w ws /\ None? (ends_of ends w)) (ensures any_none ends ws) =
  match ws with
  | [] -> ()
  | h :: t -> if h = w then () else any_none_intro ends t w

let rec any_none_elim (ends:list (string & option (list string))) (ws:list string)
  : Lemma (requires any_none ends ws)
          (ensures (match first_none ends ws with Some w -> mem w ws /\ None? (ends_of ends w) | None -> False)) =
  match ws with
  | [] -> ()
  | w :: t -> if None? (ends_of ends w) then () else any_none_elim ends t

[@@ noextract_to "FSharp"]
let rec any_has (ends:list (string & option (list string))) (ws:list string) (z:string) : Tot bool =
  match ws with
  | [] -> false
  | w :: t -> (match ends_of ends w with Some l -> mem z l | None -> false) || any_has ends t z

[@@ noextract_to "FSharp"]
let rec who_has (ends:list (string & option (list string))) (ws:list string) (z:string) : Tot (option string) =
  match ws with
  | [] -> None
  | w :: t -> if (match ends_of ends w with Some l -> mem z l | None -> false) then Some w else who_has ends t z

let rec any_has_intro (ends:list (string & option (list string))) (ws:list string) (z w:string)
  : Lemma (requires mem w ws /\ (match ends_of ends w with Some l -> mem z l | None -> False))
          (ensures any_has ends ws z) =
  match ws with
  | [] -> ()
  | h :: t -> if h = w then () else any_has_intro ends t z w

let rec any_has_elim (ends:list (string & option (list string))) (ws:list string) (z:string)
  : Lemma (requires any_has ends ws z)
          (ensures (match who_has ends ws z with
                    | Some w -> mem w ws /\ (match ends_of ends w with Some l -> mem z l | None -> False)
                    | None -> False)) =
  match ws with
  | [] -> ()
  | w :: t -> if (match ends_of ends w with Some l -> mem z l | None -> false) then () else any_has_elim ends t z

let rec join_ends_none (ends:list (string & option (list string))) (ws:list string) (acc:option (list string))
  : Lemma (None? (join_ends ends ws acc) == (None? acc || any_none ends ws)) =
  match ws with
  | [] -> ()
  | w :: t ->
    (match acc, ends_of ends w with
     | Some sofar, Some theirs -> join_ends_none ends t (Some (union sofar theirs))
     | _, _ -> join_ends_none ends t None)

let rec join_ends_some (ends:list (string & option (list string))) (ws:list string) (acc:option (list string)) (z:string)
  : Lemma (requires Some? (join_ends ends ws acc))
          (ensures Some? acc /\ (mem z (Some?.v (join_ends ends ws acc)) == (mem z (Some?.v acc) || any_has ends ws z))) =
  match ws with
  | [] -> ()
  | w :: t ->
    (match acc, ends_of ends w with
     | Some sofar, Some theirs -> mem_union z sofar theirs; join_ends_some ends t (Some (union sofar theirs)) z
     | _, _ -> join_ends_none ends t None)

(* The same answer, read as a set. *)
let opt_same (a b:option (list string)) : Tot prop =
  match a, b with
  | None, None -> True
  | Some x, Some y -> forall (z:string). mem z x <==> mem z y
  | _, _ -> False

(* Every id answered so far is answered as the specification answers it. *)
let ends_ok (r:relation) (done:list string) (ends:list (string & option (list string))) : Tot prop =
  forall (w:string). mem w done ==> opt_same (ends_of ends w) (terminals_spec r w)

(* A node off every cycle, with successors all answered: it reaches a cycle exactly when a
   successor's end is `None`. *)
let singleton_none (r:relation) (v:string) (ends:list (string & option (list string))) (done:list string)
  : Lemma (requires ends_ok r done ends /\ not (on_cycle r v) /\
                    (forall (w:string). mem w (succ_of r v) ==> mem w done))
          (ensures any_on_cycle r (reachable r [v]) == any_none ends (succ_of r v)) =
  let ss = succ_of r v in
  let reached = reachable r [v] in
  if any_on_cycle r reached then begin
    any_on_cycle_elim r reached;
    match first_on_cycle r reached with
    | Some y ->
      reach_plus_char r v y;
      reach_split r ss y;
      reacher_some r ss y;
      (match reacher r ss y with
       | Some w -> any_on_cycle_intro r (reachable r [w]) y; any_none_intro ends ss w
       | None -> ())
    | None -> ()
  end
  else begin
    if any_none ends ss then begin
      any_none_elim ends ss;
      match first_none ends ss with
      | Some w ->
        any_on_cycle_elim r (reachable r [w]);
        (match first_on_cycle r (reachable r [w]) with
         | Some y -> reach_succ r v w; reach_trans r v w y; any_on_cycle_intro r reached y
         | None -> ())
      | None -> ()
    end
  end

(* ... and when it reaches none, the sinks it reaches are the union of its successors' ends. *)
let singleton_some (r:relation) (v:string) (ends:list (string & option (list string))) (done:list string) (z:string)
  : Lemma (requires ends_ok r done ends /\ not (on_cycle r v) /\ Cons? (succ_of r v) /\
                    (forall (w:string). mem w (succ_of r v) ==> mem w done) /\
                    not (any_on_cycle r (reachable r [v])))
          (ensures any_has ends (succ_of r v) z == mem z (sinks_among r (reachable r [v]))) =
  let ss = succ_of r v in
  let reached = reachable r [v] in
  mem_sinks_among r reached z;
  if any_has ends ss z then begin
    any_has_elim ends ss z;
    match who_has ends ss z with
    | Some w ->
      (* the end of w is the specification's: z is a sink w reaches *)
      (match terminals_spec r w with
       | Some l -> mem_sinks_among r (reachable r [w]) z; reach_succ r v w; reach_trans r v w z
       | None -> ())
    | None -> ()
  end
  else begin
    if mem z reached && Nil? (succ_of r z) then begin
      reach_plus_char r v z;
      reach_split r ss z;
      reacher_some r ss z;
      match reacher r ss z with
      | Some w ->
        (* w's reach holds no cycle, else v's would *)
        (if any_on_cycle r (reachable r [w]) then begin
           any_on_cycle_elim r (reachable r [w]);
           match first_on_cycle r (reachable r [w]) with
           | Some y -> reach_succ r v w; reach_trans r v w y; any_on_cycle_intro r reached y
           | None -> ()
         end);
        mem_sinks_among r (reachable r [w]) z;
        any_has_intro ends ss z w
      | None -> ()
    end
  end

let singleton_answer (r:relation) (v:string) (ends:list (string & option (list string))) (done:list string)
  : Lemma (requires ends_ok r done ends /\ not (on_cycle r v) /\ Cons? (succ_of r v) /\
                    (forall (w:string). mem w (succ_of r v) ==> mem w done))
          (ensures opt_same (join_ends ends (succ_of r v) (Some [])) (terminals_spec r v)) =
  let ss = succ_of r v in
  singleton_none r v ends done;
  join_ends_none ends ss (Some []);
  if not (any_on_cycle r (reachable r [v])) then begin
    let each (z:string) : Lemma (mem z (Some?.v (join_ends ends ss (Some []))) <==> mem z (sinks_among r (reachable r [v]))) =
      join_ends_some ends ss (Some []) z;
      singleton_some r v ends done z
    in
    FStar.Classical.forall_intro each
  end

(* A name that is no node has no successors. *)
let rec assoc_none (#a:Type) (k:string) (l:list (string & a))
  : Lemma (requires not (has_key k l)) (ensures assoc k l == None) =
  match l with
  | [] -> ()
  | _ :: t -> assoc_none k t

let not_node_no_succ (r:relation) (x:string)
  : Lemma (requires not (mem x (nodes r))) (ensures succ_of r x == []) =
  has_key_keys x r;
  mem_app x (keys r) (range r);
  mem_nub x (app (keys r) (range r));
  assoc_none x r

(* THE LOOP INVARIANT of `answer_all`: with the components found so far answered as the
   specification answers them, the next component is too — a cycle is `None` for every member
   (each is on a cycle); a singleton with no successors is its own sink; a singleton with
   successors joins their ends, all of which were answered before it, dependencies-first. *)
let rec answer_all_ok (r:relation) (done comps:list (list string)) (ends:list (string & option (list string)))
  : Lemma (requires components r == app done comps /\ ends_ok r (concat done) ends)
          (ensures ends_ok r (concat (app done comps)) (answer_all r comps ends))
          (decreases comps) =
  match comps with
  | [] -> app_nil done
  | c :: t ->
    components_partition r;
    components_are_sccs r;
    components_deps_first r;
    concat_app done (c :: t);
    distinct_app (concat done) (app c (concat t));
    distinct_app c (concat t);
    mem_app_any c done (c :: t);
    concat_distinct_each (components r) c;
    deps_first_prefix r [] done c t;
    let a =
      if is_cycle r c then None
      else (match c with
            | [] -> None
            | v :: _ -> (match succ_of r v with
                        | [] -> Some [v]
                        | ss -> join_ends ends ss (Some [])))
    in
    let ends' = bind_all c a ends in
    concat_app done [c];
    app_nil c;
    let each (w:string) : Lemma (requires mem w (concat (app done [c]))) (ensures opt_same (ends_of ends' w) (terminals_spec r w)) =
      mem_app w (concat done) c;
      mem_app w c (concat t);
      if mem w (concat done) then bind_all_other c a ends w
      else begin
        bind_all_mem c a ends w;
        reach_refl r w;
        if is_cycle r c then begin
          cycle_member_on_cycle r c w;
          any_on_cycle_intro r (reachable r [w]) w
        end
        else begin
          (* c is the singleton [w] with no self-edge, and w is on no cycle *)
          (if on_cycle r w then on_cycle_component r w c);
          match succ_of r w with
          | [] -> reach_sink r w
          | _ ->
            let within (w':string) : Lemma (requires mem w' (succ_of r w)) (ensures mem w' (concat done)) =
              succs_within_mem r (app c (app (concat done) [])) c w w';
              mem_app w' c (app (concat done) []);
              app_nil (concat done)
            in
            FStar.Classical.forall_intro (FStar.Classical.move_requires within);
            singleton_answer r w ends (concat done)
        end
      end
    in
    FStar.Classical.forall_intro (FStar.Classical.move_requires each);
    app_assoc done [c] t;
    answer_all_ok r (app done [c]) t ends'

(* THEOREM — terminals_meets_spec. `terminals` is its chain definition: the sinks a node
   reaches, or `None` when it reaches a cycle, and a name that is no node ends at itself — as a
   set, for every name. *)
let terminals_meets_spec (r:relation) (x:string) : Lemma (opt_same (terminals r x) (terminals_spec r x)) =
  if mem x (nodes r) then begin
    components_partition r;
    answer_all_ok r [] (pass r) [];
    subset_mem (nodes r) (concat (components r)) x
  end
  else begin
    not_node_no_succ r x;
    reach_sink r x
  end

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
