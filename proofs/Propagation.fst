(*
   Propagation — incremental evaluation agrees with full evaluation: the compute strand's
   promise, `Fuaran.Core.Propagation`, modelled clause for clause and proved
   (fuaran-core Phase 186).

   WHAT IS MODELLED. `src/Fuaran.Core.Propagation/Propagation.fs` — the two halves the
   incremental promise is made of:

     - the DIRTY SET: `dependents` (the inverted dependency map), `dirtyFromChangedIds` with its
       private frontier loop `grow`, and the alias `staleSet`;
     - the DRIVER: `PropagationError`, `EvalOutcome`, the private `walk` with its loop `go`, the
       reference evaluator `eval`, and the incremental `evalFrom` with its unknown-change guard.

   Two things are PARAMETERS rather than clauses, exactly as Phase 135 made `float i` a
   parameter, Phase 176 the pipeline evaluator and Phase 177 the witness. The NODE EVALUATOR
   (`evalNode : (string -> 'v option) -> string -> Result<'v, string>`) is a function over an
   abstract value type: Core owns no evaluator, and nothing here says what a domain computes.
   And the ORDER the driver walks is a `topo_result` handed in where production computes
   `sort deps`: `sort` is Tarjan's algorithm over mutable dictionaries, the model does not
   restate it, and the ONE fact about its output the agreement theorem needs — `Order` holds no
   id twice — is a hypothesis of that theorem, a ladder row (`propagation-order-distinct`), and
   a check the differential makes on every generated graph, cyclic ones included.
   `dependencyMap`, `cycleThrough`, `touchedBy` and `dirtyFromOp` are outside the model: they
   build a dependency map or a change set from a tree, and the theorems start from both.

   WHAT IS PROVED, over any dependency map, any change set, any value type and any evaluator:

     - `dirty_sound` — the dirty set holds every changed id and every id DOWNSTREAM of one: for
       any read path `c <- n1 <- ... <- n` starting at a changed `c`, `n` is dirty. It is read
       off `dirty_closed` (the set contains the change and is closed under "reads"), and
       `dirty_least` says it is the LEAST such set — any set that contains the change and is
       closed under "reads" contains it — which is what "minimal" means in the production doc
       comment. `grow` is a total function HERE, which production's loop is only by argument:
       its termination measure (the ids of the dependents map not yet accumulated, then the
       frontier) is checked by the prover.
     - `evalfrom_agrees` — `evalFrom ev1 prior changed deps` equals `eval ev1 deps`: the whole
       `Result`, the values in the same order, the cyclic groups, and on a failing evaluator the
       SAME `EvalNodeFailed` at the same node. The premises are the contract the production doc
       comment states in prose and cannot enforce: `prior` is what `eval ev0` returned over the
       SAME dependency map, or fewer of its values (`prior_of` — a hole is recomputed); the evaluator reads other nodes only through its DECLARED reads
       (`local`); `changed` names every node whose evaluator differs between `ev0` and `ev1`
       (`agree_off`); and the walked order holds no id twice. NOT among them: that the order is
       a dependency order. Agreement holds over ANY duplicate-free order — dependency order is
       what makes the values mean something, not what makes the two paths agree.
     - `evalfrom_minimal` — a node outside the dirty set AND present in `prior` is not
       re-evaluated: `evalFrom`'s result is the same under any two evaluators that agree on the
       dirty ids and on the ids `prior` does not hold, which is what "is not evaluated" means
       for a pure function without instrumenting one. `walk_invoked` is the instrumented
       reading — the ids the walk hands to the evaluator, in order — and `invoked_only_stale`
       says each is dirty or absent from `prior`. The qualifier is the production contract,
       not a weakness of the proof: a node absent from `prior` is ALWAYS recomputed.
     - `evalfrom_unknown_refused` — a changed id the dependency map does not hold makes
       `evalFrom` the typed `EvalUnknownChange`, naming exactly the unknown ids, under every
       evaluator alike: nothing is evaluated.

   WHAT IS NOT CLAIMED. Anything about `sort` beyond the one hypothesis above — that `Order` is
   a dependency order, that `Cycles` are the strongly connected groups. Anything about a
   domain's evaluator: `local` and `agree_off` are domain obligations
   (`propagation-evaluator-local`, `propagation-change-set-complete`), and an evaluator that
   reads a node it did not declare makes `evalFrom` DISAGREE with `eval` on the shipped driver
   — the differential exhibits it. The ORDER of a set: production's `Set` and `Map` sort by
   id, the model holds both as lists, and every theorem about a set is about membership.

   HOW TO READ IT. Every definition names its F# counterpart, as in `ColumnOps.fst` and
   `Capability.fst`. The module is SELF-CONTAINED like them — it opens nothing, restates
   `outcome` and the list helpers it needs, and extracts beside the other models into the same
   oracle assembly sharing only `Prims.fs` and the `option` shim. Every walk a lemma has to
   name is a NAMED function (`always`, `in_set`, `resolve_in`), never a lambda written twice.

   Apache-2.0, like everything beside it.
*)
module Propagation

(* ======================================================================================
   0. The list helpers, self-contained, each naming the FSharp.Core function it stands for.
   ====================================================================================== *)

(* F#: `Result<'a, 'e>`. *)
type outcome (a e:Type) =
  | Ok    : a -> outcome a e
  | Error : e -> outcome a e

(* F#: `List.length`. *)
let rec len (#a:Type) (l:list a) : Tot nat =
  match l with
  | [] -> 0
  | _ :: t -> 1 + len t

(* F#: `@`. *)
let rec app (#a:Type) (l m:list a) : Tot (list a) =
  match l with
  | [] -> m
  | h :: t -> h :: app t m

(* F#: `Set.contains` / `List.contains`, over ids. *)
let rec mem (x:string) (l:list string) : Tot bool =
  match l with
  | [] -> false
  | h :: t -> x = h || mem x t

(* F#: `Map.tryFind`, a finite map read as its association list. *)
let rec assoc (#a:Type) (k:string) (l:list (string & a)) : Tot (option a) =
  match l with
  | [] -> None
  | (k', v) :: t -> if k = k' then Some v else assoc k t

(* F#: `Map.containsKey`. *)
let rec has_key (#a:Type) (k:string) (l:list (string & a)) : Tot bool =
  match l with
  | [] -> false
  | (k', _) :: t -> k = k' || has_key k t

(* No id twice — the one fact about `sort`'s `Order` the agreement theorem needs. *)
let rec distinct (l:list string) : Tot bool =
  match l with
  | [] -> true
  | h :: t -> not (mem h t) && distinct t

(* F#: `Set.isSubset`. *)
let rec subset (a b:list string) : Tot bool =
  match a with
  | [] -> true
  | h :: t -> mem h b && subset t b

(* F#: `Set.difference a b`. *)
let rec diff (a b:list string) : Tot (list string) =
  match a with
  | [] -> []
  | h :: t -> if mem h b then diff t b else h :: diff t b

(* F#: `Set.union a b`, read as membership: `a`, then what `b` adds. *)
let union (a b:list string) : Tot (list string) = app a (diff b a)

(* A set holds an id once: `Set.ofList`. Keeps the LAST occurrence; the order is not meaning. *)
let rec dedup (l:list string) : Tot (list string) =
  match l with
  | [] -> []
  | h :: t -> if mem h t then dedup t else h :: dedup t

let rec app_nil (#a:Type) (l:list a) : Lemma (app l [] == l) =
  match l with
  | [] -> ()
  | _ :: t -> app_nil t

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

(* ======================================================================================
   1. The dependency map and its inversion.
   ====================================================================================== *)

(* F#: `Map<string, Set<string>>` — each id and the ids it READS — as its association list. *)
type dmap = list (string & list string)

(* The ids `id` reads: `Map.tryFind id deps`, an absent id reading nothing. *)
let reads_of (deps:dmap) (id:string) : Tot (list string) =
  match assoc id deps with
  | Some rs -> rs
  | None -> []

(* `n` READS `r`: some entry of the map is `n`'s and holds `r`. The relation every theorem
   about the dirty set is stated over. *)
let rec edge (deps:dmap) (n r:string) : Tot bool =
  match deps with
  | [] -> false
  | (k, reads) :: t -> (k = n && mem r reads) || edge t n r

let rec reads_edge (deps:dmap) (id r:string)
  : Lemma (requires mem r (reads_of deps id)) (ensures edge deps id r) =
  match deps with
  | [] -> ()
  | (k, _) :: t -> if id = k then () else reads_edge t id r

(* F#: `for r in reads -> r, node`. *)
let rec pairs_of (node:string) (reads:list string) : Tot (list (string & string)) =
  match reads with
  | [] -> []
  | r :: t -> (r, node) :: pairs_of node t

(* F#: `[ for KeyValue(node, reads) in deps do for r in reads -> r, node ]`. *)
let rec pairs (deps:dmap) : Tot (list (string & string)) =
  match deps with
  | [] -> []
  | (node, reads) :: t -> app (pairs_of node reads) (pairs t)

(* F#: `List.map fst`. *)
let rec firsts (ps:list (string & string)) : Tot (list string) =
  match ps with
  | [] -> []
  | (r, _) :: t -> r :: firsts t

(* F#: one group of `List.groupBy fst`, mapped through `List.map snd`. *)
let rec seconds_for (k:string) (ps:list (string & string)) : Tot (list string) =
  match ps with
  | [] -> []
  | (r, n) :: t -> if r = k then n :: seconds_for k t else seconds_for k t

(* F#: `List.map (fun (k, vs) -> k, vs |> List.map snd |> Set.ofList)` over the group keys. *)
let rec group_from (ks:list string) (ps:list (string & string)) : Tot dmap =
  match ks with
  | [] -> []
  | k :: t -> (k, dedup (seconds_for k ps)) :: group_from t ps

(* F#: `dependents` — `id -> the ids that reference it`, the reverse edges the dirty loop walks. *)
let dependents (deps:dmap) : Tot dmap =
  let ps = pairs deps in
  group_from (dedup (firsts ps)) ps

(* `Map.tryFind node deps'`, an absent id having no dependents. *)
let dependents_of (d:dmap) (node:string) : Tot (list string) =
  match assoc node d with
  | Some ds -> ds
  | None -> []

let rec mem_pair (r n:string) (ps:list (string & string)) : Tot bool =
  match ps with
  | [] -> false
  | (r', n') :: t -> (r = r' && n = n') || mem_pair r n t

let rec mem_pair_app (r n:string) (p q:list (string & string))
  : Lemma (mem_pair r n (app p q) == (mem_pair r n p || mem_pair r n q)) =
  match p with
  | [] -> ()
  | _ :: t -> mem_pair_app r n t q

let rec mem_pairs_of (r n node:string) (reads:list string)
  : Lemma (mem_pair r n (pairs_of node reads) == (n = node && mem r reads)) =
  match reads with
  | [] -> ()
  | _ :: t -> mem_pairs_of r n node t

let rec pairs_edge (deps:dmap) (r n:string) : Lemma (mem_pair r n (pairs deps) == edge deps n r) =
  match deps with
  | [] -> ()
  | (node, reads) :: t ->
    mem_pair_app r n (pairs_of node reads) (pairs t);
    mem_pairs_of r n node reads;
    pairs_edge t r n

let rec seconds_pair (k n:string) (ps:list (string & string))
  : Lemma (mem n (seconds_for k ps) == mem_pair k n ps) =
  match ps with
  | [] -> ()
  | _ :: t -> seconds_pair k n t

let rec pair_first (r n:string) (ps:list (string & string))
  : Lemma (requires mem_pair r n ps) (ensures mem r (firsts ps)) =
  match ps with
  | [] -> ()
  | (r', n') :: t -> if r = r' && n = n' then () else pair_first r n t

let rec group_lookup (k:string) (ks:list string) (ps:list (string & string))
  : Lemma (assoc k (group_from ks ps) == (if mem k ks then Some (dedup (seconds_for k ps)) else None)) =
  match ks with
  | [] -> ()
  | _ :: t -> group_lookup k t ps

(* THE INVERSION IS EXACT: `n` is among `r`'s dependents exactly when `n` reads `r`. *)
let dependents_edge (deps:dmap) (r n:string)
  : Lemma (mem n (dependents_of (dependents deps) r) == edge deps n r) =
  let ps = pairs deps in
  group_lookup r (dedup (firsts ps)) ps;
  mem_dedup r (firsts ps);
  mem_dedup n (seconds_for r ps);
  seconds_pair r n ps;
  pairs_edge deps r n;
  if mem_pair r n ps then pair_first r n ps else ()

(* ======================================================================================
   2. The dirty set.
   ====================================================================================== *)

(* F#: `(Set.empty, frontier) ||> Set.fold (fun s node -> match Map.tryFind node deps' with
   | Some ds -> Set.union s ds | None -> s)` — every dependent of a frontier id. *)
let rec next_of (d:dmap) (frontier:list string) (s:list string) : Tot (list string) (decreases frontier) =
  match frontier with
  | [] -> s
  | node :: t ->
    next_of d t (match assoc node d with
                 | Some ds -> union s ds
                 | None -> s)

(* `x` is a dependent of SOME frontier id — the fold above, read as membership. *)
let rec any_dep (d:dmap) (frontier:list string) (x:string) : Tot bool =
  match frontier with
  | [] -> false
  | node :: t -> mem x (dependents_of d node) || any_dep d t x

let rec next_mem (d:dmap) (frontier s:list string) (x:string)
  : Lemma (ensures mem x (next_of d frontier s) == (mem x s || any_dep d frontier x)) (decreases frontier) =
  match frontier with
  | [] -> ()
  | node :: t ->
    (match assoc node d with
     | Some ds -> mem_union x s ds; next_mem d t (union s ds) x
     | None -> next_mem d t s x)

let rec any_dep_intro (d:dmap) (frontier:list string) (r x:string)
  : Lemma (requires mem r frontier /\ mem x (dependents_of d r)) (ensures any_dep d frontier x) =
  match frontier with
  | [] -> ()
  | node :: t -> if r = node then () else any_dep_intro d t r x

(* Every id a dependents map can ever contribute: the measure `grow` terminates by. *)
let rec range (d:dmap) : Tot (list string) =
  match d with
  | [] -> []
  | (_, vs) :: t -> app vs (range t)

let rec dependents_in_range (d:dmap) (node x:string)
  : Lemma (requires mem x (dependents_of d node)) (ensures mem x (range d)) =
  match d with
  | [] -> ()
  | (k, vs) :: t ->
    mem_app x vs (range t);
    if node = k then () else dependents_in_range t node x

let rec any_dep_in_range (d:dmap) (frontier:list string) (x:string)
  : Lemma (requires any_dep d frontier x) (ensures mem x (range d)) =
  match frontier with
  | [] -> ()
  | node :: t ->
    if mem x (dependents_of d node) then dependents_in_range d node x else any_dep_in_range d t x

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

(* WHY THE LOOP STOPS. A round that finds nothing fresh leaves the accumulator alone and hands
   on an empty frontier; a round that finds something strictly shrinks the ids of the map not
   yet accumulated. Production's `grow` carries this argument in a doc comment; here it is the
   function's termination proof. *)
let grow_measure (d:dmap) (frontier acc:list string)
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

(* F#: the private `grow` inside `dirtyFromChangedIds`. *)
let rec grow (d:dmap) (frontier acc:list string)
  : Tot (list string) (decreases %[len (diff (range d) acc); len frontier]) =
  match frontier with
  | [] -> acc
  | _ :: _ ->
    let next = next_of d frontier [] in
    let fresh = diff next acc in
    grow_measure d frontier acc;
    grow d fresh (union acc fresh)

(* F#: `dirtyFromChangedIds`. *)
let dirty_from_changed_ids (deps:dmap) (changed:list string) : Tot (list string) =
  grow (dependents deps) changed changed

(* F#: `staleSet` — the same set under the name the call site reads. *)
let stale_set (deps:dmap) (changed:list string) : Tot (list string) =
  dirty_from_changed_ids deps changed

(* A set closed under "reads": whoever reads a member is a member. *)
let closed (deps:dmap) (s:list string) : Tot prop =
  forall (n r:string). mem r s /\ edge deps n r ==> mem n s

(* The loop invariant: closed already, except at the frontier still to be expanded. *)
let closed_except (deps:dmap) (acc frontier:list string) : Tot prop =
  forall (n r:string). mem r acc /\ not (mem r frontier) /\ edge deps n r ==> mem n acc

let grow_step_closed (deps:dmap) (frontier acc:list string) (n r:string)
  : Lemma (requires closed_except deps acc frontier)
          (ensures (let fresh = diff (next_of (dependents deps) frontier []) acc in
                    mem r (union acc fresh) /\ not (mem r fresh) /\ edge deps n r ==> mem n (union acc fresh))) =
  let d = dependents deps in
  let next = next_of d frontier [] in
  let fresh = diff next acc in
  mem_union r acc fresh;
  mem_union n acc fresh;
  mem_diff n next acc;
  next_mem d frontier [] n;
  dependents_edge deps r n;
  if mem r frontier && edge deps n r then any_dep_intro d frontier r n else ()

let rec grow_closed (deps:dmap) (frontier acc:list string)
  : Lemma (requires closed_except deps acc frontier)
          (ensures closed deps (grow (dependents deps) frontier acc) /\
                   (forall (x:string). mem x acc ==> mem x (grow (dependents deps) frontier acc)))
          (decreases %[len (diff (range (dependents deps)) acc); len frontier]) =
  match frontier with
  | [] -> ()
  | _ :: _ ->
    let d = dependents deps in
    let fresh = diff (next_of d frontier []) acc in
    grow_measure d frontier acc;
    FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 (grow_step_closed deps frontier acc));
    let aux (x:string) : Lemma (mem x acc ==> mem x (union acc fresh)) = mem_union x acc fresh in
    FStar.Classical.forall_intro aux;
    grow_closed deps fresh (union acc fresh)

(* The dirty set CONTAINS the change and is CLOSED under "reads". *)
let dirty_closed (deps:dmap) (changed:list string)
  : Lemma (subset changed (dirty_from_changed_ids deps changed) /\
           closed deps (dirty_from_changed_ids deps changed)) =
  grow_closed deps changed changed;
  subset_intro changed (dirty_from_changed_ids deps changed)

(* A read path out of `c`: the first id reads `c`, each next id reads the one before it. *)
let rec downstream (deps:dmap) (c:string) (path:list string) : Tot bool (decreases path) =
  match path with
  | [] -> true
  | n :: t -> edge deps n c && downstream deps n t

(* Where a read path out of `c` ends — `c` itself for the empty path. *)
let rec path_end (c:string) (path:list string) : Tot string (decreases path) =
  match path with
  | [] -> c
  | n :: t -> path_end n t

let rec closed_downstream (deps:dmap) (s:list string) (c:string) (path:list string)
  : Lemma (requires closed deps s /\ mem c s /\ downstream deps c path)
          (ensures mem (path_end c path) s) (decreases path) =
  match path with
  | [] -> ()
  | n :: t -> closed_downstream deps s n t

(* THEOREM — dirty_sound. Every id whose input changed is dirty: a changed id, and every id at
   the end of a read path out of one. *)
let dirty_sound (deps:dmap) (changed:list string) (c:string) (path:list string)
  : Lemma (requires mem c changed /\ downstream deps c path)
          (ensures mem (path_end c path) (dirty_from_changed_ids deps changed)) =
  dirty_closed deps changed;
  subset_mem changed (dirty_from_changed_ids deps changed) c;
  closed_downstream deps (dirty_from_changed_ids deps changed) c path

let next_within (deps:dmap) (frontier s acc:list string) (x:string)
  : Lemma (requires closed deps s /\ (forall (y:string). mem y frontier ==> mem y s) /\
                    mem x (next_of (dependents deps) frontier acc) /\ not (mem x acc))
          (ensures mem x s) =
  let d = dependents deps in
  next_mem d frontier acc x;
  let rec walk (f:list string)
    : Lemma (requires (forall (y:string). mem y f ==> mem y s) /\ any_dep d f x) (ensures mem x s) =
    match f with
    | [] -> ()
    | node :: t -> if mem x (dependents_of d node) then dependents_edge deps node x else walk t
  in
  walk frontier

let rec grow_within (deps:dmap) (frontier acc s:list string)
  : Lemma (requires closed deps s /\ (forall (y:string). mem y frontier ==> mem y s) /\
                    (forall (y:string). mem y acc ==> mem y s))
          (ensures (forall (y:string). mem y (grow (dependents deps) frontier acc) ==> mem y s))
          (decreases %[len (diff (range (dependents deps)) acc); len frontier]) =
  match frontier with
  | [] -> ()
  | _ :: _ ->
    let d = dependents deps in
    let next = next_of d frontier [] in
    let fresh = diff next acc in
    grow_measure d frontier acc;
    let fresh_in (y:string) : Lemma (mem y fresh ==> mem y s) =
      mem_diff y next acc;
      if mem y fresh then next_within deps frontier s [] y else ()
    in
    FStar.Classical.forall_intro fresh_in;
    let acc_in (y:string) : Lemma (mem y (union acc fresh) ==> mem y s) = mem_union y acc fresh in
    FStar.Classical.forall_intro acc_in;
    grow_within deps fresh (union acc fresh) s

(* THEOREM — dirty_least. The dirty set is MINIMAL: any set that holds the change and is closed
   under "reads" holds all of it. An id not reverse-reachable from a change is never dirty. *)
let dirty_least (deps:dmap) (changed s:list string)
  : Lemma (requires subset changed s /\ closed deps s)
          (ensures subset (dirty_from_changed_ids deps changed) s) =
  let aux (y:string) : Lemma (mem y changed ==> mem y s) =
    if mem y changed then subset_mem changed s y else ()
  in
  FStar.Classical.forall_intro aux;
  grow_within deps changed changed s;
  subset_intro (dirty_from_changed_ids deps changed) s

(* ======================================================================================
   3. The driver.
   ====================================================================================== *)

(* F#: `TopoResult` — what `sort deps` returns. A PARAMETER here: see the header. *)
type topo_result = { order: list string; cycles: list (list string) }

(* F#: `PropagationError`. *)
type propagation_error =
  | EvalUnknownChange : list string -> propagation_error
  | EvalNodeFailed    : string -> string -> propagation_error

(* F#: `EvalOutcome<'v>`; `Values` a finite map read as its association list, newest first. *)
type eval_outcome (v:Type) = { values: list (string & v); cyclic: list (list string) }

(* F#: `evalNode : (string -> 'v option) -> string -> Result<'v, string>` — the domain's. *)
type evaluator (v:Type) = (string -> option v) -> string -> outcome v string

(* F#: `fun k -> Map.tryFind k results`. *)
let resolve_in (#v:Type) (results:list (string & v)) (k:string) : Tot (option v) = assoc k results

(* F#: `fun _ -> true` — `eval`'s recompute-everything predicate. *)
let always (_:string) : Tot bool = true

(* F#: `fun id -> Set.contains id dirty` — `evalFrom`'s predicate. *)
let in_set (s:list string) (id:string) : Tot bool = mem id s

(* F#: the guard `recompute id || not (Map.containsKey id prior)` TOGETHER WITH the
   `Map.find id prior` its else-branch reads, as one total function: `Some p` exactly when the
   guard is false (`reuse_is_guard`), and then `p` is the prior value. *)
let reuse (#v:Type) (recompute:string -> bool) (prior:list (string & v)) (id:string) : Tot (option v) =
  if recompute id then None else assoc id prior

let rec assoc_has_key (#a:Type) (k:string) (l:list (string & a)) : Lemma (Some? (assoc k l) == has_key k l) =
  match l with
  | [] -> ()
  | _ :: t -> assoc_has_key k t

let reuse_is_guard (#v:Type) (recompute:string -> bool) (prior:list (string & v)) (id:string)
  : Lemma (None? (reuse recompute prior id) == (recompute id || not (has_key id prior))) =
  assoc_has_key id prior

(* F#: the private `go` inside `walk`. *)
let rec go (#v:Type) (ev:evaluator v) (recompute:string -> bool) (prior:list (string & v))
           (cycles:list (list string)) (results:list (string & v)) (order:list string)
  : Tot (outcome (eval_outcome v) propagation_error) (decreases order) =
  match order with
  | [] -> Ok ({ values = results; cyclic = cycles })
  | id :: rest ->
    (match reuse recompute prior id with
     | None ->
       (match ev (resolve_in results) id with
        | Ok x -> go ev recompute prior cycles ((id, x) :: results) rest
        | Error m -> Error (EvalNodeFailed id m))
     | Some p -> go ev recompute prior cycles ((id, p) :: results) rest)

(* F#: the private `walk`, its `sort deps` handed in as `topo`. *)
let walk (#v:Type) (ev:evaluator v) (recompute:string -> bool) (prior:list (string & v)) (topo:topo_result)
  : Tot (outcome (eval_outcome v) propagation_error) =
  go ev recompute prior topo.cycles [] topo.order

(* F#: `eval` — `walk evalNode (fun _ -> true) Map.empty deps`. *)
let eval (#v:Type) (ev:evaluator v) (topo:topo_result) : Tot (outcome (eval_outcome v) propagation_error) =
  walk ev always [] topo

(* F#: `changed |> Set.filter (fun c -> not (Map.containsKey c deps))`. *)
let rec unknown_of (deps:dmap) (changed:list string) : Tot (list string) =
  match changed with
  | [] -> []
  | c :: t -> if has_key c deps then unknown_of deps t else c :: unknown_of deps t

(* F#: `evalFrom`. *)
let eval_from (#v:Type) (ev:evaluator v) (prior:list (string & v)) (changed:list string)
              (deps:dmap) (topo:topo_result)
  : Tot (outcome (eval_outcome v) propagation_error) =
  match unknown_of deps changed with
  | _ :: _ -> Error (EvalUnknownChange (unknown_of deps changed))
  | [] -> walk ev (in_set (dirty_from_changed_ids deps changed)) prior topo

(* The ids the walk hands to the evaluator, in order, up to and including a failing one — the
   instrumented reading of `go`, which the differential holds to production's own recorder. *)
let rec go_invoked (#v:Type) (ev:evaluator v) (recompute:string -> bool) (prior:list (string & v))
                   (results:list (string & v)) (order:list string)
  : Tot (list string) (decreases order) =
  match order with
  | [] -> []
  | id :: rest ->
    (match reuse recompute prior id with
     | None ->
       (match ev (resolve_in results) id with
        | Ok x -> id :: go_invoked ev recompute prior ((id, x) :: results) rest
        | Error _ -> [id])
     | Some p -> go_invoked ev recompute prior ((id, p) :: results) rest)

(* What `evalFrom` evaluates: nothing on an unknown change, else the walk's invocations. *)
let walk_invoked (#v:Type) (ev:evaluator v) (prior:list (string & v)) (changed:list string)
                 (deps:dmap) (topo:topo_result)
  : Tot (list string) =
  match unknown_of deps changed with
  | _ :: _ -> []
  | [] -> go_invoked ev (in_set (dirty_from_changed_ids deps changed)) prior [] topo.order

(* ======================================================================================
   4. The premises of the agreement theorem, each a contract production states in prose.
   ====================================================================================== *)

(* The evaluator reads other nodes ONLY through its declared reads: two resolvers that agree
   on `id`'s reads give `id` the same result. Production hands `evalNode` a resolver over
   EVERYTHING computed so far and cannot enforce this; it is the domain's obligation. *)
let local (#v:Type) (ev:evaluator v) (deps:dmap) : Tot prop =
  forall (id:string) (f g:string -> option v).
    (forall (r:string). mem r (reads_of deps id) ==> f r == g r) ==> ev f id == ev g id

(* The change set is COMPLETE: off it, the old evaluator and the new one are the same. *)
let agree_off (#v:Type) (ev0 ev1:evaluator v) (changed:list string) : Tot prop =
  forall (f:string -> option v) (id:string). not (mem id changed) ==> ev1 f id == ev0 f id

let local_elim (#v:Type) (ev:evaluator v) (deps:dmap) (id:string) (f g:string -> option v)
  : Lemma (requires local ev deps /\ (forall (r:string). mem r (reads_of deps id) ==> f r == g r))
          (ensures ev f id == ev g id) = ()

let agree_off_elim (#v:Type) (ev0 ev1:evaluator v) (changed:list string) (f:string -> option v) (id:string)
  : Lemma (requires agree_off ev0 ev1 changed /\ not (mem id changed)) (ensures ev1 f id == ev0 f id) = ()

(* ======================================================================================
   5. The agreement theorem.
   ====================================================================================== *)

(* A finished walk leaves an id it never reached exactly as it found it. *)
let rec go_keeps (#v:Type) (ev:evaluator v) (recompute:string -> bool) (prior:list (string & v))
                 (cycles:list (list string)) (results:list (string & v)) (order:list string)
                 (out:eval_outcome v) (k:string)
  : Lemma (requires go ev recompute prior cycles results order == Ok out /\ not (mem k order))
          (ensures assoc k out.values == assoc k results) (decreases order) =
  match order with
  | [] -> ()
  | id :: rest ->
    (match reuse recompute prior id with
     | None ->
       (match ev (resolve_in results) id with
        | Ok x -> go_keeps ev recompute prior cycles ((id, x) :: results) rest out k
        | Error _ -> ())
     | Some p -> go_keeps ev recompute prior cycles ((id, p) :: results) rest out k)

(* A clean node's reads are clean, so two result maps that agree on clean ids resolve them alike. *)
let clean_reads (#v:Type) (deps:dmap) (dirty:list string) (id:string) (r0 r1:list (string & v))
  : Lemma (requires closed deps dirty /\ not (mem id dirty) /\
                    (forall (k:string). not (mem k dirty) ==> assoc k r0 == assoc k r1))
          (ensures (forall (r:string). mem r (reads_of deps id) ==> resolve_in r1 r == resolve_in r0 r)) =
  let aux (r:string) : Lemma (mem r (reads_of deps id) ==> resolve_in r1 r == resolve_in r0 r) =
    if mem r (reads_of deps id) then reads_edge deps id r else ()
  in
  FStar.Classical.forall_intro aux

(* `prior` is the old evaluation's values OR FEWER: every value it holds is the one the old
   full walk computed. A prior with a HOLE in it is admitted, because production admits it — an
   id absent from `prior` is recomputed, dirty or not. *)
let prior_of (#v:Type) (prior old:list (string & v)) : Tot prop =
  forall (k:string). None? (assoc k prior) \/ assoc k prior == assoc k old

(* The induction. `r0` is where the OLD full walk stands, `r1` where BOTH new walks stand —
   the incremental one and the full one hold the same results at every step, which is the
   theorem; the old walk is carried beside them because it is where `prior` came from. *)
let rec go_agree (#v:Type) (ev0 ev1:evaluator v) (deps:dmap) (changed dirty:list string)
                 (cycles:list (list string)) (out0:eval_outcome v) (prior:list (string & v))
                 (r0 r1:list (string & v)) (rest:list string)
  : Lemma (requires local ev0 deps /\ agree_off ev0 ev1 changed /\ prior_of prior out0.values /\
                    subset changed dirty /\ closed deps dirty /\ distinct rest /\
                    (forall (k:string). not (mem k dirty) ==> assoc k r0 == assoc k r1) /\
                    go ev0 always [] cycles r0 rest == Ok out0)
          (ensures go ev1 (in_set dirty) prior cycles r1 rest == go ev1 always [] cycles r1 rest)
          (decreases rest) =
  match rest with
  | [] -> ()
  | id :: t ->
    (match ev0 (resolve_in r0) id with
     | Error _ -> ()
     | Ok v0 ->
       go_keeps ev0 always [] cycles ((id, v0) :: r0) t out0 id;
       if mem id dirty then
         (match ev1 (resolve_in r1) id with
          | Error _ -> ()
          | Ok v1 -> go_agree ev0 ev1 deps changed dirty cycles out0 prior ((id, v0) :: r0) ((id, v1) :: r1) t)
       else begin
         clean_reads deps dirty id r0 r1;
         local_elim ev0 deps id (resolve_in r1) (resolve_in r0);
         (if mem id changed then subset_mem changed dirty id else ());
         agree_off_elim ev0 ev1 changed (resolve_in r1) id;
         go_agree ev0 ev1 deps changed dirty cycles out0 prior ((id, v0) :: r0) ((id, v0) :: r1) t
       end)

(* THEOREM — evalfrom_agrees. Incremental evaluation over the dirty set equals full evaluation,
   as a whole `Result`: the same values in the same order, the same cyclic groups, and under a
   failing evaluator the same `EvalNodeFailed` at the same node. `out0` is what the OLD full
   evaluation returned over the same order, `prior` its values or fewer (`prior_of`), and `ev1`
   the evaluator after the change. `evalfrom_agrees_exact` is the reading with no hole. *)
let evalfrom_agrees (#v:Type) (ev0 ev1:evaluator v) (deps:dmap) (changed:list string)
                    (topo:topo_result) (out0:eval_outcome v) (prior:list (string & v))
  : Lemma (requires local ev0 deps /\ agree_off ev0 ev1 changed /\ distinct topo.order /\
                    Nil? (unknown_of deps changed) /\ eval ev0 topo == Ok out0 /\
                    prior_of prior out0.values)
          (ensures eval_from ev1 prior changed deps topo == eval ev1 topo) =
  dirty_closed deps changed;
  go_agree ev0 ev1 deps changed (dirty_from_changed_ids deps changed) topo.cycles out0 prior [] [] topo.order

let evalfrom_agrees_exact (#v:Type) (ev0 ev1:evaluator v) (deps:dmap) (changed:list string)
                          (topo:topo_result) (out0:eval_outcome v)
  : Lemma (requires local ev0 deps /\ agree_off ev0 ev1 changed /\ distinct topo.order /\
                    Nil? (unknown_of deps changed) /\ eval ev0 topo == Ok out0)
          (ensures eval_from ev1 out0.values changed deps topo == eval ev1 topo) =
  evalfrom_agrees ev0 ev1 deps changed topo out0 out0.values

(* ======================================================================================
   6. Minimal reuse, and the unknown-change refusal.
   ====================================================================================== *)

(* Two evaluators that differ only where `evalFrom` does NOT look: on ids that are neither
   dirty nor absent from `prior`. *)
let agree_on_stale (#v:Type) (ev ev':evaluator v) (dirty:list string) (prior:list (string & v)) : Tot prop =
  forall (f:string -> option v) (id:string). (mem id dirty \/ not (has_key id prior)) ==> ev f id == ev' f id

let agree_on_stale_elim (#v:Type) (ev ev':evaluator v) (dirty:list string) (prior:list (string & v))
                        (f:string -> option v) (id:string)
  : Lemma (requires agree_on_stale ev ev' dirty prior /\ (mem id dirty \/ not (has_key id prior)))
          (ensures ev f id == ev' f id) = ()

let rec go_minimal (#v:Type) (ev ev':evaluator v) (dirty:list string) (prior:list (string & v))
                   (cycles:list (list string)) (results:list (string & v)) (order:list string)
  : Lemma (requires agree_on_stale ev ev' dirty prior)
          (ensures go ev (in_set dirty) prior cycles results order == go ev' (in_set dirty) prior cycles results order)
          (decreases order) =
  match order with
  | [] -> ()
  | id :: rest ->
    (match reuse (in_set dirty) prior id with
     | None ->
       assoc_has_key id prior;
       agree_on_stale_elim ev ev' dirty prior (resolve_in results) id;
       (match ev (resolve_in results) id with
        | Ok x -> go_minimal ev ev' dirty prior cycles ((id, x) :: results) rest
        | Error _ -> ())
     | Some p -> go_minimal ev ev' dirty prior cycles ((id, p) :: results) rest)

(* THEOREM — evalfrom_minimal. A node outside the dirty set and present in `prior` is not
   re-evaluated: what `evalFrom` returns does not depend on what the evaluator would say there. *)
let evalfrom_minimal (#v:Type) (ev ev':evaluator v) (prior:list (string & v)) (changed:list string)
                     (deps:dmap) (topo:topo_result)
  : Lemma (requires agree_on_stale ev ev' (dirty_from_changed_ids deps changed) prior)
          (ensures eval_from ev prior changed deps topo == eval_from ev' prior changed deps topo) =
  match unknown_of deps changed with
  | _ :: _ -> ()
  | [] -> go_minimal ev ev' (dirty_from_changed_ids deps changed) prior topo.cycles [] topo.order

let rec go_invoked_stale (#v:Type) (ev:evaluator v) (dirty:list string) (prior:list (string & v))
                         (results:list (string & v)) (order:list string) (k:string)
  : Lemma (requires mem k (go_invoked ev (in_set dirty) prior results order))
          (ensures mem k order /\ (mem k dirty || not (has_key k prior))) (decreases order) =
  match order with
  | [] -> ()
  | id :: rest ->
    (match reuse (in_set dirty) prior id with
     | None ->
       assoc_has_key id prior;
       (match ev (resolve_in results) id with
        | Ok x -> if k = id then () else go_invoked_stale ev dirty prior ((id, x) :: results) rest k
        | Error _ -> ())
     | Some p -> go_invoked_stale ev dirty prior ((id, p) :: results) rest k)

(* The instrumented reading of the same fact: every id the walk hands to the evaluator is on
   the walked order, and is dirty or absent from `prior`. *)
let invoked_only_stale (#v:Type) (ev:evaluator v) (prior:list (string & v)) (changed:list string)
                       (deps:dmap) (topo:topo_result) (k:string)
  : Lemma (requires mem k (walk_invoked ev prior changed deps topo))
          (ensures mem k topo.order /\
                   (mem k (dirty_from_changed_ids deps changed) || not (has_key k prior))) =
  match unknown_of deps changed with
  | _ :: _ -> ()
  | [] -> go_invoked_stale ev (dirty_from_changed_ids deps changed) prior [] topo.order k

let rec unknown_exact (deps:dmap) (changed:list string) (c:string)
  : Lemma (mem c (unknown_of deps changed) == (mem c changed && not (has_key c deps))) =
  match changed with
  | [] -> ()
  | _ :: t -> unknown_exact deps t c

(* THEOREM — evalfrom_unknown_refused. A changed id the dependency map does not hold is the
   typed refusal naming exactly the unknown ids (`unknown_exact`), under every evaluator alike,
   and nothing is evaluated. *)
let evalfrom_unknown_refused (#v:Type) (ev ev':evaluator v) (prior:list (string & v)) (changed:list string)
                             (deps:dmap) (topo:topo_result) (c:string)
  : Lemma (requires mem c changed /\ not (has_key c deps))
          (ensures eval_from ev prior changed deps topo == Error (EvalUnknownChange (unknown_of deps changed)) /\
                   eval_from ev' prior changed deps topo == eval_from ev prior changed deps topo /\
                   walk_invoked ev prior changed deps topo == [] /\
                   mem c (unknown_of deps changed)) =
  unknown_exact deps changed c
