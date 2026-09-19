(*
   Propagation — incremental evaluation agrees with full evaluation: the compute strand's
   promise, `Fuaran.Core.Propagation`, modelled clause for clause and proved
   (fuaran-core Phase 186; the resolver restricted and the declared-reads premise DISCHARGED by
   Phase 209).

   WHAT IS MODELLED. `src/Fuaran.Core.Propagation/Propagation.fs` — the two halves the
   incremental promise is made of:

     - the DIRTY SET: `dependents` (the inverted dependency map), `dirtyFromChangedIds` with its
       private frontier loop `grow`, and the alias `staleSet`;
     - the DRIVER: `PropagationError`, `EvalOutcome`, the private `walk` with its loop `go`, the
       reference evaluator `eval`, and the incremental `evalFrom` with its unknown-change guard —
       including the RESTRICTED resolver and its `EvalUndeclaredRead` refusal (Phase 209).

   THREE things are PARAMETERS rather than clauses, exactly as Phase 135 made `float i` a
   parameter, Phase 176 the pipeline evaluator and Phase 177 the witness. The NODE EVALUATOR
   (`evalNode : (string -> 'v option) -> string -> Result<'v, string>`) is a function over an
   abstract value type: Core owns no evaluator, and nothing here says what a domain computes.
   The ORDER the driver walks is a `topo_result` handed in where production computes
   `sort deps`: `sort` is Tarjan's algorithm over mutable dictionaries, the model does not
   restate it, and the ONE fact about its output the agreement theorem needs — `Order` holds no
   id twice — is a hypothesis of that theorem, a ladder row (`propagation-order-distinct`), and
   a check the differential makes on every generated graph, cyclic ones included.
   And the READS THE EVALUATOR ACTUALLY MAKES are a `read_witness` handed in where production
   OBSERVES them: production's resolver is instrumented and records the ids it was asked for, an
   effect a `Tot` function cannot have, so the model takes the observation as given exactly as it
   takes the order. That parameter is what lets the model carry Phase 209's refusal instead of
   modelling a driver production no longer has (`propagation-read-witness`).
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
       SAME `EvalNodeFailed` at the same node. **It NO LONGER carries a declared-reads premise**
       (Phase 209): the driver builds the resolver FROM the node's declared reads (`lookups`), so
       two result maps that agree on those reads hand the evaluator the same argument and it
       answers alike (`lookups_eq`, `resolver_eq`) — where Phase 186 had to assume that of every
       domain evaluator as `local`, now deleted. What remains is the contract production still
       cannot enforce: `prior` is what `eval ev0` returned over the SAME dependency map, or fewer
       of its values (`prior_of` — a hole is recomputed); `changed` names every node whose
       evaluator differs between `ev0` and `ev1` (`agree_off`) and every node whose READS differ
       (`touches_off`, the same premise's other half); and the walked order holds no id twice.
       NOT among them: that the order is a dependency order. Agreement holds over ANY
       duplicate-free order — dependency order is what makes the values mean something, not what
       makes the two paths agree.
     - `undeclared_refused` (Phase 209) — a node that reads outside its declaration is refused as
       DATA: a walk whose first node reads an id `deps` does not declare for it returns
       `EvalUndeclaredRead` naming the node and the read, under every evaluator alike and whatever
       the reuse policy would do elsewhere, because the check reads the declaration and the
       observed reads and never the evaluator. `eval_refuses_undeclared` and
       `evalfrom_refuses_undeclared` are the two drivers' readings — `evalFrom`'s under the
       hypothesis that it RECOMPUTES that node (dirty, or absent from `prior`), which is
       production's own guard through `reuse_is_guard`.
     - `ok_implies_declared` / `eval_ok_declared` (Phase 209) — a FULL evaluation that succeeded
       checked every node on the walked order, so no node read outside its declaration. This is
       the sentence that makes `evalfrom_agrees`' `prior` premise carry the enforcement: a `prior`
       that came from `eval` over the same `deps` cannot have come from an evaluator that reads
       undeclared, because that `eval` refused instead of returning one.
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
   a dependency order, that `Cycles` are the strongly connected groups. That the `read_witness`
   is the evaluator's real read set: production instruments its resolver and records the ids it
   was ASKED for, and no pure model can observe a call that a pure function makes, so the
   parameter is taken on trust (`propagation-read-witness`, a `permanent` bridge — the semantic
   dependency set a pure model COULD define is not the same set, since an evaluator may ask for
   an id and ignore the answer, and production refuses on the asking). The two premises that
   remain the domain's: `agree_off` with `touches_off` (the change set is complete, about results
   and about reads) and `prior_of` (`propagation-change-set-and-prior`). The ORDER of a set:
   production's `Set` and `Map` sort by id, the model holds both as lists, and every theorem
   about a set is about membership.

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

(* F#: `PropagationError`. `EvalUndeclaredRead` is declared LAST, as it is there and for the same
   reason: a case's declaration order is its tag number. *)
type propagation_error =
  | EvalUnknownChange   : list string -> propagation_error
  | EvalNodeFailed      : string -> string -> propagation_error
  | EvalUndeclaredRead  : string -> string -> propagation_error

(* F#: `EvalOutcome<'v>`; `Values` a finite map read as its association list, newest first. *)
type eval_outcome (v:Type) = { values: list (string & v); cyclic: list (list string) }

(* F#: `evalNode : (string -> 'v option) -> string -> Result<'v, string>` — the domain's. *)
type evaluator (v:Type) = (string -> option v) -> string -> outcome v string

(* F#: `fun k -> Map.tryFind k results`, applied to the list the restriction below projects. *)
let resolve_in (#v:Type) (results:list (string & v)) (k:string) : Tot (option v) = assoc k results

(* F#: the RESTRICTED resolver `walk` builds for node `id` — `deps[id]`'s reads and nothing else
   (Phase 209). Rather than a lambda that tests membership, the model PROJECTS the declared reads
   that resolve into an association list and reads that: `resolve_in (lookups rs results)` answers
   `Some x` for a declared read with a value and `None` for a declared read without one OR for any
   undeclared read, which is exactly what production answers.

   The projection is the whole reason `local` could be deleted. A membership lambda over `results`
   would be POINTWISE equal to the one over a different `results` that agrees on `rs`, and pointwise
   equality of functions is not equality — concluding `ev f id == ev g id` from it needs functional
   extensionality, which is what `local` was standing in for. Routed through a DATA value the two
   resolvers are the SAME value (`lookups_eq`), and congruence finishes it. *)
let rec lookups (#v:Type) (rs:list string) (results:list (string & v)) : Tot (list (string & v)) =
  match rs with
  | [] -> []
  | r :: t ->
    (match assoc r results with
     | Some x -> (r, x) :: lookups t results
     | None -> lookups t results)

(* The ids the evaluator ACTUALLY reads at a node, as production's instrumented resolver observes
   them. A PARAMETER beside the evaluator, exactly as `topo_result` is a parameter beside `deps`:
   production reads it off the resolver's own calls, and nothing here computes it from `ev` — a
   `Tot` function cannot observe a call. See the header's `propagation-read-witness`. *)
type read_witness = string -> list string

(* F#: the `undeclared` cell inside `walk` — the FIRST id read outside `deps[id]`, or `None` when
   every read was declared. One read rather than all of them, as production does: one violation is
   what a domain fixes, and a read that follows it may only exist because the first answered
   nothing. *)
let rec first_undeclared (deps:dmap) (id:string) (touched:list string) : Tot (option string) =
  match touched with
  | [] -> None
  | k :: t -> if mem k (reads_of deps id) then first_undeclared deps id t else Some k

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

(* F#: the private `go` inside `walk`. The refusal is checked where production checks it — on a node
   it RECOMPUTES, never on one it reuses — and the model checks it before applying `ev` where
   production applies `ev` and then discards the result. The two are the same function: production's
   evaluator is pure, so running it and throwing the answer away is not observable. Where it IS
   observable — the ids the recorder saw — `go_invoked` mirrors production and lists the refused
   node, because production's recorder fires inside the evaluator. *)
let rec go (#v:Type) (ev:evaluator v) (touches:read_witness) (deps:dmap)
           (recompute:string -> bool) (prior:list (string & v))
           (cycles:list (list string)) (results:list (string & v)) (order:list string)
  : Tot (outcome (eval_outcome v) propagation_error) (decreases order) =
  match order with
  | [] -> Ok ({ values = results; cyclic = cycles })
  | id :: rest ->
    (match reuse recompute prior id with
     | None ->
       (match first_undeclared deps id (touches id) with
        | Some r -> Error (EvalUndeclaredRead id r)
        | None ->
          (match ev (resolve_in (lookups (reads_of deps id) results)) id with
           | Ok x -> go ev touches deps recompute prior cycles ((id, x) :: results) rest
           | Error m -> Error (EvalNodeFailed id m)))
     | Some p -> go ev touches deps recompute prior cycles ((id, p) :: results) rest)

(* F#: the private `walk`, its `sort deps` handed in as `topo`. *)
let walk (#v:Type) (ev:evaluator v) (touches:read_witness) (deps:dmap) (recompute:string -> bool)
         (prior:list (string & v)) (topo:topo_result)
  : Tot (outcome (eval_outcome v) propagation_error) =
  go ev touches deps recompute prior topo.cycles [] topo.order

(* F#: `eval` — `walk evalNode (fun _ -> true) Map.empty deps`. *)
let eval (#v:Type) (ev:evaluator v) (touches:read_witness) (deps:dmap) (topo:topo_result)
  : Tot (outcome (eval_outcome v) propagation_error) =
  walk ev touches deps always [] topo

(* F#: `changed |> Set.filter (fun c -> not (Map.containsKey c deps))`. *)
let rec unknown_of (deps:dmap) (changed:list string) : Tot (list string) =
  match changed with
  | [] -> []
  | c :: t -> if has_key c deps then unknown_of deps t else c :: unknown_of deps t

(* F#: `evalFrom`. *)
let eval_from (#v:Type) (ev:evaluator v) (touches:read_witness) (prior:list (string & v))
              (changed:list string) (deps:dmap) (topo:topo_result)
  : Tot (outcome (eval_outcome v) propagation_error) =
  match unknown_of deps changed with
  | _ :: _ -> Error (EvalUnknownChange (unknown_of deps changed))
  | [] -> walk ev touches deps (in_set (dirty_from_changed_ids deps changed)) prior topo

(* The ids the walk hands to the evaluator, in order, up to and including a failing one — the
   instrumented reading of `go`, which the differential holds to production's own recorder. A node
   REFUSED for an undeclared read is listed: production's recorder fires inside the evaluator, so
   the evaluator ran there and the walk stopped after it. *)
let rec go_invoked (#v:Type) (ev:evaluator v) (touches:read_witness) (deps:dmap)
                   (recompute:string -> bool) (prior:list (string & v))
                   (results:list (string & v)) (order:list string)
  : Tot (list string) (decreases order) =
  match order with
  | [] -> []
  | id :: rest ->
    (match reuse recompute prior id with
     | None ->
       (match first_undeclared deps id (touches id) with
        | Some _ -> [id]
        | None ->
          (match ev (resolve_in (lookups (reads_of deps id) results)) id with
           | Ok x -> id :: go_invoked ev touches deps recompute prior ((id, x) :: results) rest
           | Error _ -> [id]))
     | Some p -> go_invoked ev touches deps recompute prior ((id, p) :: results) rest)

(* What `evalFrom` evaluates: nothing on an unknown change, else the walk's invocations. *)
let walk_invoked (#v:Type) (ev:evaluator v) (touches:read_witness) (prior:list (string & v))
                 (changed:list string) (deps:dmap) (topo:topo_result)
  : Tot (list string) =
  match unknown_of deps changed with
  | _ :: _ -> []
  | [] -> go_invoked ev touches deps (in_set (dirty_from_changed_ids deps changed)) prior [] topo.order

(* ======================================================================================
   4. The premises of the agreement theorem, each a contract production states in prose.
   ====================================================================================== *)

(* WHAT WAS HERE AND IS GONE (Phase 209). Phase 186's first premise was `local`: two resolvers that
   agree on `id`'s declared reads give `id` the same result — an obligation on every domain
   evaluator, which production hands a resolver over EVERYTHING computed so far and cannot enforce.
   The driver now hands a resolver built from the declared reads, so the premise is discharged by
   construction and `local` is DELETED rather than kept as a hypothesis nobody supplies. The two
   lemmas below are what replaced it, and they quantify over nothing a domain does. *)

let rec lookups_eq (#v:Type) (rs:list string) (r0 r1:list (string & v))
  : Lemma (requires (forall (r:string). mem r rs ==> assoc r r0 == assoc r r1))
          (ensures lookups rs r0 == lookups rs r1) =
  match rs with
  | [] -> ()
  | _ :: t -> lookups_eq t r0 r1

(* THE DISCHARGE, and the replacement for `local_elim` at the one step that used it: two result maps
   that agree on `id`'s declared reads give the evaluator the SAME argument, so it answers alike.
   No hypothesis on `ev`. *)
let resolver_eq (#v:Type) (ev:evaluator v) (deps:dmap) (id:string) (r0 r1:list (string & v))
  : Lemma (requires (forall (r:string). mem r (reads_of deps id) ==> assoc r r0 == assoc r r1))
          (ensures ev (resolve_in (lookups (reads_of deps id) r0)) id ==
                   ev (resolve_in (lookups (reads_of deps id) r1)) id) =
  lookups_eq (reads_of deps id) r0 r1

(* The change set is COMPLETE: off it, the old evaluator and the new one are the same. *)
let agree_off (#v:Type) (ev0 ev1:evaluator v) (changed:list string) : Tot prop =
  forall (f:string -> option v) (id:string). not (mem id changed) ==> ev1 f id == ev0 f id

(* The same premise's other half, about READS rather than results: off the change set, the two
   evaluators read the same nodes. It is not a second obligation — two evaluators that ARE the same
   function at an id read the same ids there — and it is stated separately only because the read set
   is a parameter here rather than something the model computes from `ev`. It is load-bearing
   exactly once: at a CLEAN node the incremental walk reuses without checking, so the full walk's
   check there must be the check the OLD walk already passed. *)
let touches_off (t0 t1:read_witness) (changed:list string) : Tot prop =
  forall (id:string). not (mem id changed) ==> t1 id == t0 id

let agree_off_elim (#v:Type) (ev0 ev1:evaluator v) (changed:list string) (f:string -> option v) (id:string)
  : Lemma (requires agree_off ev0 ev1 changed /\ not (mem id changed)) (ensures ev1 f id == ev0 f id) = ()

let touches_off_elim (t0 t1:read_witness) (changed:list string) (id:string)
  : Lemma (requires touches_off t0 t1 changed /\ not (mem id changed)) (ensures t1 id == t0 id) = ()

(* ======================================================================================
   5. The agreement theorem.
   ====================================================================================== *)

(* A finished walk leaves an id it never reached exactly as it found it. *)
let rec go_keeps (#v:Type) (ev:evaluator v) (touches:read_witness) (deps:dmap)
                 (recompute:string -> bool) (prior:list (string & v))
                 (cycles:list (list string)) (results:list (string & v)) (order:list string)
                 (out:eval_outcome v) (k:string)
  : Lemma (requires go ev touches deps recompute prior cycles results order == Ok out /\ not (mem k order))
          (ensures assoc k out.values == assoc k results) (decreases order) =
  match order with
  | [] -> ()
  | id :: rest ->
    (match reuse recompute prior id with
     | None ->
       (match first_undeclared deps id (touches id) with
        | Some _ -> ()
        | None ->
          (match ev (resolve_in (lookups (reads_of deps id) results)) id with
           | Ok x -> go_keeps ev touches deps recompute prior cycles ((id, x) :: results) rest out k
           | Error _ -> ()))
     | Some p -> go_keeps ev touches deps recompute prior cycles ((id, p) :: results) rest out k)

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
let rec go_agree (#v:Type) (ev0 ev1:evaluator v) (t0 t1:read_witness) (deps:dmap)
                 (changed dirty:list string)
                 (cycles:list (list string)) (out0:eval_outcome v) (prior:list (string & v))
                 (r0 r1:list (string & v)) (rest:list string)
  : Lemma (requires agree_off ev0 ev1 changed /\ touches_off t0 t1 changed /\ prior_of prior out0.values /\
                    subset changed dirty /\ closed deps dirty /\ distinct rest /\
                    (forall (k:string). not (mem k dirty) ==> assoc k r0 == assoc k r1) /\
                    go ev0 t0 deps always [] cycles r0 rest == Ok out0)
          (ensures go ev1 t1 deps (in_set dirty) prior cycles r1 rest ==
                   go ev1 t1 deps always [] cycles r1 rest)
          (decreases rest) =
  match rest with
  | [] -> ()
  | id :: t ->
    (* The OLD full walk recomputed `id` and returned `Ok`, so its own declared-reads check passed
       there; a `Some` here contradicts the hypothesis. *)
    (match first_undeclared deps id (t0 id) with
     | Some _ -> ()
     | None ->
       (* The NEW walks check `t1`. Dirty: both recompute, so both refuse alike or both proceed.
          Clean: `touches_off` makes the check the one the old walk just passed. *)
       (match first_undeclared deps id (t1 id) with
        | Some _ ->
          if mem id dirty then
            ()
          else begin
            (if mem id changed then subset_mem changed dirty id else ());
            touches_off_elim t0 t1 changed id
          end
        | None ->
          (match ev0 (resolve_in (lookups (reads_of deps id) r0)) id with
           | Error _ -> ()
           | Ok v0 ->
             go_keeps ev0 t0 deps always [] cycles ((id, v0) :: r0) t out0 id;
             if mem id dirty then
               (match ev1 (resolve_in (lookups (reads_of deps id) r1)) id with
                | Error _ -> ()
                | Ok v1 ->
                  go_agree ev0 ev1 t0 t1 deps changed dirty cycles out0 prior
                           ((id, v0) :: r0) ((id, v1) :: r1) t)
             else begin
               clean_reads deps dirty id r0 r1;
               resolver_eq ev1 deps id r1 r0;
               (if mem id changed then subset_mem changed dirty id else ());
               agree_off_elim ev0 ev1 changed (resolve_in (lookups (reads_of deps id) r0)) id;
               go_agree ev0 ev1 t0 t1 deps changed dirty cycles out0 prior
                        ((id, v0) :: r0) ((id, v0) :: r1) t
             end)))

(* THEOREM — evalfrom_agrees. Incremental evaluation over the dirty set equals full evaluation,
   as a whole `Result`: the same values in the same order, the same cyclic groups, and under a
   failing evaluator the same `EvalNodeFailed` at the same node. `out0` is what the OLD full
   evaluation returned over the same order, `prior` its values or fewer (`prior_of`), and `ev1`
   the evaluator after the change. `evalfrom_agrees_exact` is the reading with no hole. *)
let evalfrom_agrees (#v:Type) (ev0 ev1:evaluator v) (t0 t1:read_witness) (deps:dmap)
                    (changed:list string)
                    (topo:topo_result) (out0:eval_outcome v) (prior:list (string & v))
  : Lemma (requires agree_off ev0 ev1 changed /\ touches_off t0 t1 changed /\ distinct topo.order /\
                    Nil? (unknown_of deps changed) /\ eval ev0 t0 deps topo == Ok out0 /\
                    prior_of prior out0.values)
          (ensures eval_from ev1 t1 prior changed deps topo == eval ev1 t1 deps topo) =
  dirty_closed deps changed;
  go_agree ev0 ev1 t0 t1 deps changed (dirty_from_changed_ids deps changed) topo.cycles out0 prior
           [] [] topo.order

let evalfrom_agrees_exact (#v:Type) (ev0 ev1:evaluator v) (t0 t1:read_witness) (deps:dmap)
                          (changed:list string) (topo:topo_result) (out0:eval_outcome v)
  : Lemma (requires agree_off ev0 ev1 changed /\ touches_off t0 t1 changed /\ distinct topo.order /\
                    Nil? (unknown_of deps changed) /\ eval ev0 t0 deps topo == Ok out0)
          (ensures eval_from ev1 t1 out0.values changed deps topo == eval ev1 t1 deps topo) =
  evalfrom_agrees ev0 ev1 t0 t1 deps changed topo out0 out0.values

(* ======================================================================================
   5b. The declared-reads refusal (Phase 209) — the premise `evalfrom_agrees` no longer carries,
       stated from the other side: what the driver DOES to an evaluator that breaks it.
   ====================================================================================== *)

(* THEOREM — undeclared_refused. A walk whose first node reads an id its declaration does not hold
   refuses with that node and that read, under EVERY evaluator alike and whatever reuse policy is in
   force, provided the node is recomputed there (`None? (reuse …)`, production's own guard through
   `reuse_is_guard`). The restriction to the FIRST node is what makes it evaluator-independent: a
   violator further along is reached only if the evaluator did not fail before it, which is a fact
   about the evaluator and not about the declaration. *)
let undeclared_refused (#v:Type) (ev:evaluator v) (touches:read_witness) (deps:dmap)
                       (recompute:string -> bool) (prior:list (string & v))
                       (cycles:list (list string)) (results:list (string & v))
                       (id r:string) (rest:list string)
  : Lemma (requires first_undeclared deps id (touches id) == Some r /\ None? (reuse recompute prior id))
          (ensures go ev touches deps recompute prior cycles results (id :: rest) ==
                   Error (EvalUndeclaredRead id r)) = ()

(* The full evaluator's reading: it recomputes every node, so it never fails to check one. *)
let eval_refuses_undeclared (#v:Type) (ev:evaluator v) (touches:read_witness) (deps:dmap)
                            (topo:topo_result) (id r:string) (rest:list string)
  : Lemma (requires topo.order == id :: rest /\ first_undeclared deps id (touches id) == Some r)
          (ensures eval ev touches deps topo == Error (EvalUndeclaredRead id r)) = ()

(* The incremental evaluator's reading, with the one qualifier production's doc comment states: the
   refusal is observed where the node is RECOMPUTED — dirty, or absent from `prior`. A violating
   node that is clean and present in `prior` is reused and not re-invoked, which is
   `evalfrom_minimal` and not a hole: `evalfrom_agrees`' `prior` premise says `prior` came from
   `eval` over the same map, and `eval_ok_declared` says such a `prior` cannot exist. *)
let evalfrom_refuses_undeclared (#v:Type) (ev:evaluator v) (touches:read_witness)
                                (prior:list (string & v)) (changed:list string) (deps:dmap)
                                (topo:topo_result) (id r:string) (rest:list string)
  : Lemma (requires topo.order == id :: rest /\ Nil? (unknown_of deps changed) /\
                    first_undeclared deps id (touches id) == Some r /\
                    (mem id (dirty_from_changed_ids deps changed) \/ not (has_key id prior)))
          (ensures eval_from ev touches prior changed deps topo == Error (EvalUndeclaredRead id r)) =
  reuse_is_guard (in_set (dirty_from_changed_ids deps changed)) prior id

(* THEOREM — ok_implies_declared. A full walk that returned `Ok` checked every node on the order, so
   none of them read outside its declaration. This is what makes the `prior` premise ENFORCE rather
   than merely assume: there is no successful full evaluation of a non-conforming evaluator to take
   a `prior` from. *)
let rec ok_implies_declared (#v:Type) (ev:evaluator v) (touches:read_witness) (deps:dmap)
                            (cycles:list (list string)) (results:list (string & v))
                            (order:list string) (out:eval_outcome v) (id:string)
  : Lemma (requires go ev touches deps always [] cycles results order == Ok out /\ mem id order)
          (ensures first_undeclared deps id (touches id) == None) (decreases order) =
  match order with
  | [] -> ()
  | h :: t ->
    (match first_undeclared deps h (touches h) with
     | Some _ -> ()
     | None ->
       (match ev (resolve_in (lookups (reads_of deps h) results)) h with
        | Ok x -> if id = h then () else ok_implies_declared ev touches deps cycles ((h, x) :: results) t out id
        | Error _ -> ()))

let eval_ok_declared (#v:Type) (ev:evaluator v) (touches:read_witness) (deps:dmap)
                     (topo:topo_result) (out:eval_outcome v) (id:string)
  : Lemma (requires eval ev touches deps topo == Ok out /\ mem id topo.order)
          (ensures first_undeclared deps id (touches id) == None) =
  ok_implies_declared ev touches deps topo.cycles [] topo.order out id

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

let rec go_minimal (#v:Type) (ev ev':evaluator v) (touches:read_witness) (deps:dmap)
                   (dirty:list string) (prior:list (string & v))
                   (cycles:list (list string)) (results:list (string & v)) (order:list string)
  : Lemma (requires agree_on_stale ev ev' dirty prior)
          (ensures go ev touches deps (in_set dirty) prior cycles results order ==
                   go ev' touches deps (in_set dirty) prior cycles results order)
          (decreases order) =
  match order with
  | [] -> ()
  | id :: rest ->
    (match reuse (in_set dirty) prior id with
     | None ->
       assoc_has_key id prior;
       (match first_undeclared deps id (touches id) with
        | Some _ -> ()
        | None ->
          agree_on_stale_elim ev ev' dirty prior (resolve_in (lookups (reads_of deps id) results)) id;
          (match ev (resolve_in (lookups (reads_of deps id) results)) id with
           | Ok x -> go_minimal ev ev' touches deps dirty prior cycles ((id, x) :: results) rest
           | Error _ -> ()))
     | Some p -> go_minimal ev ev' touches deps dirty prior cycles ((id, p) :: results) rest)

(* THEOREM — evalfrom_minimal. A node outside the dirty set and present in `prior` is not
   re-evaluated: what `evalFrom` returns does not depend on what the evaluator would say there. The
   refusal check does not weaken it — the check reads the declaration and the read witness, so two
   evaluators sharing one witness refuse identically. *)
let evalfrom_minimal (#v:Type) (ev ev':evaluator v) (touches:read_witness) (prior:list (string & v))
                     (changed:list string) (deps:dmap) (topo:topo_result)
  : Lemma (requires agree_on_stale ev ev' (dirty_from_changed_ids deps changed) prior)
          (ensures eval_from ev touches prior changed deps topo ==
                   eval_from ev' touches prior changed deps topo) =
  match unknown_of deps changed with
  | _ :: _ -> ()
  | [] ->
    go_minimal ev ev' touches deps (dirty_from_changed_ids deps changed) prior topo.cycles [] topo.order

let rec go_invoked_stale (#v:Type) (ev:evaluator v) (touches:read_witness) (deps:dmap)
                         (dirty:list string) (prior:list (string & v))
                         (results:list (string & v)) (order:list string) (k:string)
  : Lemma (requires mem k (go_invoked ev touches deps (in_set dirty) prior results order))
          (ensures mem k order /\ (mem k dirty || not (has_key k prior))) (decreases order) =
  match order with
  | [] -> ()
  | id :: rest ->
    (match reuse (in_set dirty) prior id with
     | None ->
       assoc_has_key id prior;
       (match first_undeclared deps id (touches id) with
        | Some _ -> ()
        | None ->
          (match ev (resolve_in (lookups (reads_of deps id) results)) id with
           | Ok x ->
             if k = id then () else go_invoked_stale ev touches deps dirty prior ((id, x) :: results) rest k
           | Error _ -> ()))
     | Some p -> go_invoked_stale ev touches deps dirty prior ((id, p) :: results) rest k)

(* The instrumented reading of the same fact: every id the walk hands to the evaluator is on
   the walked order, and is dirty or absent from `prior`. A node REFUSED for an undeclared read is
   among them, which is why the fact is stated over `go_invoked` and not over the values. *)
let invoked_only_stale (#v:Type) (ev:evaluator v) (touches:read_witness) (prior:list (string & v))
                       (changed:list string) (deps:dmap) (topo:topo_result) (k:string)
  : Lemma (requires mem k (walk_invoked ev touches prior changed deps topo))
          (ensures mem k topo.order /\
                   (mem k (dirty_from_changed_ids deps changed) || not (has_key k prior))) =
  match unknown_of deps changed with
  | _ :: _ -> ()
  | [] -> go_invoked_stale ev touches deps (dirty_from_changed_ids deps changed) prior [] topo.order k

let rec unknown_exact (deps:dmap) (changed:list string) (c:string)
  : Lemma (mem c (unknown_of deps changed) == (mem c changed && not (has_key c deps))) =
  match changed with
  | [] -> ()
  | _ :: t -> unknown_exact deps t c

(* THEOREM — evalfrom_unknown_refused. A changed id the dependency map does not hold is the
   typed refusal naming exactly the unknown ids (`unknown_exact`), under every evaluator alike,
   and nothing is evaluated. Note the contrast with `undeclared_refused` above, which holds under
   every evaluator only at the FIRST node: this one precedes all evaluation, so no evaluator can
   fail ahead of it and it holds under two read witnesses as well as two evaluators. *)
let evalfrom_unknown_refused (#v:Type) (ev ev':evaluator v) (t t':read_witness)
                             (prior:list (string & v)) (changed:list string)
                             (deps:dmap) (topo:topo_result) (c:string)
  : Lemma (requires mem c changed /\ not (has_key c deps))
          (ensures eval_from ev t prior changed deps topo == Error (EvalUnknownChange (unknown_of deps changed)) /\
                   eval_from ev' t' prior changed deps topo == eval_from ev t prior changed deps topo /\
                   walk_invoked ev t prior changed deps topo == [] /\
                   mem c (unknown_of deps changed)) =
  unknown_exact deps changed c
