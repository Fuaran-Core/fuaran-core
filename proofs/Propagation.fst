(*
   Propagation — incremental evaluation agrees with full evaluation: the compute strand's
   promise, `Fuaran.Core.Propagation`, modelled clause for clause and proved
   (fuaran-core Phase 186; the resolver restricted and the declared-reads premise DISCHARGED by
   Phase 209).

   WHAT IS MODELLED. `src/Fuaran.Core.Propagation/Propagation.fs` — the two halves the
   incremental promise is made of:

     - the DIRTY SET: `dependents` (the inverted dependency map), `dirtyFromChangedIds` with its
       private frontier loop `grow` (its alias `staleSet` left at `1.0.0`, Phase 391);
     - the DRIVER: `PropagationError`, `EvalOutcome`, the private `walk` with its loop `go`, the
       reference evaluator `eval`, and the incremental `evalFrom` with its unknown-change guard —
       including the RESTRICTED resolver and its `EvalUndeclaredRead` refusal (Phase 209).

   TWO things are PARAMETERS rather than clauses, exactly as Phase 135 made `float i` a
   parameter, Phase 176 the pipeline evaluator and Phase 177 the witness. The NODE EVALUATOR
   (`evalNode : (string -> 'v option) -> string -> Result<'v, string>`) is a function over an
   abstract value type: Core owns no evaluator, and nothing here says what a domain computes.
   The ORDER the driver walks is COMPUTED, as production computes it (Phase 553): `sort deps`
   is `Relation.sort` over the map with its dangling reads removed — `Relation.fst`'s functional
   Tarjan pass — read into a `topo_result` as production reads `Relation.Topo` into
   `TopoResult`. The one fact about it the agreement theorems need, that `Order` holds no id
   twice, is a THEOREM (`sort_distinct`, off `Relation.sort_order_distinct`), so `evalfrom_agrees`
   and `evalfromwith_agrees` carry no order premise; the walk over an ARBITRARY order is kept
   beside them as `eval_over` and the `_over` family, because that is the stronger statement and
   the certificate section (8) is stated over it. Until this phase the order was the model's third
   parameter and that fact a ladder row's assumption.
   The READS THE EVALUATOR ACTUALLY MAKES are a `read_witness` handed in where production
   OBSERVES them: production's resolver is instrumented and records the ids it was asked for, an
   effect a `Tot` function cannot have, so the model takes the observation as given exactly as it
   takes the order. That parameter is what lets the model carry Phase 209's refusal instead of
   modelling a driver production no longer has (`propagation-read-witness`).
   `dependencyMap`, `cycleThrough`, `touchedBy` and `dirtyFromOp` are outside the model: they
   build a dependency map or a change set from a tree, and the theorems start from both.

   WHAT IS PROVED, AND WHAT IS NOT. The ladder states both, once: the rows of `../proofs.json`
   whose `evidence.model` is this file, each with its `statement` and its `boundary`. `README.md`
   renders them in this model's section.

   HOW TO READ IT. Every definition names its F# counterpart, as in `Preservation.fst` and
   `Capability.fst`. The module opens ONE other model, `Relation` (Phase 553): the list helpers
   (`assoc`, `subset`, `union`, …), the closure `reachable` that both the dirty set and the needed
   set are, and the sort are that module's, as production's are `Fuaran.Core.Relation`'s. It
   restates only `outcome`, and extracts beside the other models into the same oracle assembly.
   Every walk a lemma has to name is a NAMED function (`always`, `in_set`, `resolve_in`), never a
   lambda written twice.

   Apache-2.0, like everything beside it.
*)
module Propagation

open Twin
open ListAlgebra
open Relation

(* ======================================================================================
   0. `Result`, the one helper not `Relation.fst`'s.
   ====================================================================================== *)

(* F#: `Result<'a, 'e>`. *)
type outcome (a e:Type) =
  | Ok    : a -> outcome a e
  | Error : e -> outcome a e

(* ======================================================================================
   1. The dependency map and its inversion.
   ====================================================================================== *)

(* F#: `Map<string, Set<string>>` — each id and the ids it READS — as its association list. *)
type dmap = list (string & list string)

(* The ids `id` reads: `Map.tryFind id deps`, an absent id reading nothing — `Relation.succ_of`,
   named for the direction it is read in here. *)
let reads_of (deps:dmap) (id:string) : Tot (list string) = succ_of deps id

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

(* `Map.tryFind node deps'`, an absent id having no dependents — `Relation.succ_of` again, over
   the inverted map. *)
let dependents_of (d:dmap) (node:string) : Tot (list string) = succ_of d node

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

(* F#: `dirtyFromChangedIds` — `Relation.reachable (Relation.ofMap deps |> Relation.inverse)
   changed`: the closure over the dependents map (Phase 552). *)
let dirty_from_changed_ids (deps:dmap) (changed:list string) : Tot (list string) =
  reachable (dependents deps) changed

(* A set closed under "reads": whoever reads a member is a member. *)
let closed (deps:dmap) (s:list string) : Tot prop =
  forall (n r:string). mem r s /\ edge deps n r ==> mem n s

(* Closed under "reads" is closed under the dependents map: `dependents_edge`, read as sets. *)
let closed_is_closed_by (deps:dmap) (s:list string)
  : Lemma (closed deps s <==> closed_by (dependents deps) s) =
  let e (r n:string) : Lemma (mem n (succ_of (dependents deps) r) == edge deps n r) = dependents_edge deps r n in
  FStar.Classical.forall_intro_2 e

(* The dirty set CONTAINS the change and is CLOSED under "reads". *)
let dirty_closed (deps:dmap) (changed:list string)
  : Lemma (subset changed (dirty_from_changed_ids deps changed) /\
           closed deps (dirty_from_changed_ids deps changed)) =
  reachable_closed (dependents deps) changed;
  closed_is_closed_by deps (dirty_from_changed_ids deps changed)

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

(* THEOREM — dirty_least. The dirty set is MINIMAL: any set that holds the change and is closed
   under "reads" holds all of it. An id not reverse-reachable from a change is never dirty. *)
let dirty_least (deps:dmap) (changed s:list string)
  : Lemma (requires subset changed s /\ closed deps s)
          (ensures subset (dirty_from_changed_ids deps changed) s) =
  closed_is_closed_by deps s;
  reachable_least (dependents deps) changed s

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
let eval_over (#v:Type) (ev:evaluator v) (touches:read_witness) (deps:dmap) (topo:topo_result)
  : Tot (outcome (eval_outcome v) propagation_error) =
  walk ev touches deps always [] topo

(* F#: `changed |> Set.filter (fun c -> not (Map.containsKey c deps))`. *)
let rec unknown_of (deps:dmap) (changed:list string) : Tot (list string) =
  match changed with
  | [] -> []
  | c :: t -> if has_key c deps then unknown_of deps t else c :: unknown_of deps t

(* F#: `evalFrom`. *)
let eval_from_over (#v:Type) (ev:evaluator v) (touches:read_witness) (prior:list (string & v))
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
let walk_invoked_over (#v:Type) (ev:evaluator v) (touches:read_witness) (prior:list (string & v))
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

(* The same two premises NARROWED to the ids a walk visits (Phase 456). `agree_off` quantifies over
   every id, and so cannot be met by an evaluator read off a tree the edit REMOVED a node from: at
   the removed id the old evaluator answers and the new one does not. The walk only ever asks at the
   ids of its order, so that is all the induction needs — `agree_on … order`. And the read premise is
   needed only for what the walk DOES with the witness: at a clean node the old walk's declared-reads
   check passed, and the new walk's must too. `checks_on` says exactly that, and a witness that is the
   map's own declaration meets it outright (`first_undeclared_of_declared`), whatever order the old
   witness listed the same reads in. `touches_off` implies it. *)
let agree_on (#v:Type) (ev0 ev1:evaluator v) (changed ids:list string) : Tot prop =
  forall (f:string -> option v) (id:string). mem id ids /\ not (mem id changed) ==> ev1 f id == ev0 f id

let checks_on (deps:dmap) (t0 t1:read_witness) (changed ids:list string) : Tot prop =
  forall (id:string). mem id ids /\ not (mem id changed) /\ None? (first_undeclared deps id (t0 id))
                      ==> None? (first_undeclared deps id (t1 id))

let agree_off_on (#v:Type) (ev0 ev1:evaluator v) (changed ids:list string)
  : Lemma (requires agree_off ev0 ev1 changed) (ensures agree_on ev0 ev1 changed ids) = ()

let touches_off_checks (deps:dmap) (t0 t1:read_witness) (changed ids:list string)
  : Lemma (requires touches_off t0 t1 changed) (ensures checks_on deps t0 t1 changed ids) = ()

let agree_on_elim (#v:Type) (ev0 ev1:evaluator v) (changed ids:list string) (f:string -> option v) (id:string)
  : Lemma (requires agree_on ev0 ev1 changed ids /\ mem id ids /\ not (mem id changed))
          (ensures ev1 f id == ev0 f id) = ()

let checks_on_elim (deps:dmap) (t0 t1:read_witness) (changed ids:list string) (id:string)
  : Lemma (requires checks_on deps t0 t1 changed ids /\ mem id ids /\ not (mem id changed) /\
                    None? (first_undeclared deps id (t0 id)))
          (ensures None? (first_undeclared deps id (t1 id))) = ()

let agree_on_tail (#v:Type) (ev0 ev1:evaluator v) (changed:list string) (id:string) (ids:list string)
  : Lemma (requires agree_on ev0 ev1 changed (id :: ids)) (ensures agree_on ev0 ev1 changed ids) = ()

let checks_on_tail (deps:dmap) (t0 t1:read_witness) (changed:list string) (id:string) (ids:list string)
  : Lemma (requires checks_on deps t0 t1 changed (id :: ids)) (ensures checks_on deps t0 t1 changed ids) = ()

(* A witness that lists only declared reads passes the check, in whatever order it lists them. *)
let rec first_undeclared_of_declared (deps:dmap) (id:string) (touched:list string)
  : Lemma (requires subset touched (reads_of deps id)) (ensures None? (first_undeclared deps id touched))
          (decreases touched) =
  match touched with
  | [] -> ()
  | k :: t -> first_undeclared_of_declared deps id t

(* A change set every member of which the map holds is refused nowhere. *)
let rec unknown_of_nil (deps:dmap) (changed:list string)
  : Lemma (requires (forall (c:string). mem c changed ==> has_key c deps)) (ensures Nil? (unknown_of deps changed))
          (decreases changed) =
  match changed with
  | [] -> ()
  | _ :: t -> unknown_of_nil deps t

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
  : Lemma (requires agree_on ev0 ev1 changed rest /\ checks_on deps t0 t1 changed rest /\
                    prior_of prior out0.values /\
                    subset changed dirty /\ closed deps dirty /\ distinct rest /\
                    (forall (k:string). not (mem k dirty) ==> assoc k r0 == assoc k r1) /\
                    go ev0 t0 deps always [] cycles r0 rest == Ok out0)
          (ensures go ev1 t1 deps (in_set dirty) prior cycles r1 rest ==
                   go ev1 t1 deps always [] cycles r1 rest)
          (decreases rest) =
  match rest with
  | [] -> ()
  | id :: t ->
    agree_on_tail ev0 ev1 changed id t;
    checks_on_tail deps t0 t1 changed id t;
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
            checks_on_elim deps t0 t1 changed (id :: t) id
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
               agree_on_elim ev0 ev1 changed (id :: t) (resolve_in (lookups (reads_of deps id) r0)) id;
               go_agree ev0 ev1 t0 t1 deps changed dirty cycles out0 prior
                        ((id, v0) :: r0) ((id, v0) :: r1) t
             end)))

(* THEOREM — evalfrom_agrees_over. Incremental evaluation over the dirty set equals full evaluation,
   as a whole `Result`: the same values in the same order, the same cyclic groups, and under a
   failing evaluator the same `EvalNodeFailed` at the same node. `out0` is what the OLD full
   evaluation returned over the same order, `prior` its values or fewer (`prior_of`), and `ev1`
   the evaluator after the change. `evalfrom_agrees_exact` is the reading with no hole. *)
let evalfrom_agrees_on (#v:Type) (ev0 ev1:evaluator v) (t0 t1:read_witness) (deps:dmap)
                       (changed:list string)
                       (topo:topo_result) (out0:eval_outcome v) (prior:list (string & v))
  : Lemma (requires agree_on ev0 ev1 changed topo.order /\ checks_on deps t0 t1 changed topo.order /\
                    distinct topo.order /\
                    Nil? (unknown_of deps changed) /\ eval_over ev0 t0 deps topo == Ok out0 /\
                    prior_of prior out0.values)
          (ensures eval_from_over ev1 t1 prior changed deps topo == eval_over ev1 t1 deps topo) =
  dirty_closed deps changed;
  go_agree ev0 ev1 t0 t1 deps changed (dirty_from_changed_ids deps changed) topo.cycles out0 prior
           [] [] topo.order

let evalfrom_agrees_over (#v:Type) (ev0 ev1:evaluator v) (t0 t1:read_witness) (deps:dmap)
                    (changed:list string)
                    (topo:topo_result) (out0:eval_outcome v) (prior:list (string & v))
  : Lemma (requires agree_off ev0 ev1 changed /\ touches_off t0 t1 changed /\ distinct topo.order /\
                    Nil? (unknown_of deps changed) /\ eval_over ev0 t0 deps topo == Ok out0 /\
                    prior_of prior out0.values)
          (ensures eval_from_over ev1 t1 prior changed deps topo == eval_over ev1 t1 deps topo) =
  agree_off_on ev0 ev1 changed topo.order;
  touches_off_checks deps t0 t1 changed topo.order;
  evalfrom_agrees_on ev0 ev1 t0 t1 deps changed topo out0 prior

let evalfrom_agrees_exact (#v:Type) (ev0 ev1:evaluator v) (t0 t1:read_witness) (deps:dmap)
                          (changed:list string) (topo:topo_result) (out0:eval_outcome v)
  : Lemma (requires agree_off ev0 ev1 changed /\ touches_off t0 t1 changed /\ distinct topo.order /\
                    Nil? (unknown_of deps changed) /\ eval_over ev0 t0 deps topo == Ok out0)
          (ensures eval_from_over ev1 t1 out0.values changed deps topo == eval_over ev1 t1 deps topo) =
  evalfrom_agrees_over ev0 ev1 t0 t1 deps changed topo out0 out0.values

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
          (ensures eval_over ev touches deps topo == Error (EvalUndeclaredRead id r)) = ()

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
          (ensures eval_from_over ev touches prior changed deps topo == Error (EvalUndeclaredRead id r)) =
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
  : Lemma (requires eval_over ev touches deps topo == Ok out /\ mem id topo.order)
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
          (ensures eval_from_over ev touches prior changed deps topo ==
                   eval_from_over ev' touches prior changed deps topo) =
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
  : Lemma (requires mem k (walk_invoked_over ev touches prior changed deps topo))
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
          (ensures eval_from_over ev t prior changed deps topo == Error (EvalUnknownChange (unknown_of deps changed)) /\
                   eval_from_over ev' t' prior changed deps topo == eval_from_over ev t prior changed deps topo /\
                   walk_invoked_over ev t prior changed deps topo == [] /\
                   mem c (unknown_of deps changed)) =
  unknown_exact deps changed c

(* ======================================================================================
   7. The prior value, inside the contract (Phase 250).

      F#: `evalWith` / `evalFromWith` and the private `walkWith` they share with `eval` /
      `evalFrom`. A recomputed node is handed its OWN value from the evaluation that produced
      `prior` (`Map.tryFind id prior`) beside its resolver, so a domain that reuses work WITHIN a
      node — a table node refreshing only the rows an edit reached — carries that state through
      the driver instead of beside it.

      THE AGREEMENT THEOREM, RESTATED (`evalfromwith_agrees`). It is `evalfrom_agrees` at the
      evaluator's PRIOR-BLIND reading (`blind`: handed `None`), with every premise of that theorem
      unchanged, and ONE more: `prior_blind_along` — the evaluator's answer does not depend on the
      prior it is handed, at every node the incremental walk recomputes, under the resolver the walk
      hands it there. The prior is a hint for reusing work, never an input to the answer. It is a
      premise and not a lemma for the reason `agree_off` is one: it is about the evaluator, which is
      this model's PARAMETER. `Conformance.propagationEvaluatorLawsWith` samples it at a domain's own
      evaluator (`propagation-prior-blind`).

      And the refactor that introduced the shared walk is itself a lemma (`eval_is_walk_with`): the
      prior-blind entry points are the prior-aware walk of an evaluator that ignores its prior, so
      `eval` and `evalFrom` — and every theorem above about them — are unchanged by it.
   ====================================================================================== *)

(* F#: `evalNode : (string -> 'v option) -> 'v option -> string -> Result<'v, string>`. *)
type evaluator_with (v:Type) = (string -> option v) -> option v -> string -> outcome v string

(* F#: `fun resolve id -> evalNode resolve None id` — the reading `evalWith` walks. *)
let blind (#v:Type) (evw:evaluator_with v) : evaluator v = fun f id -> evw f None id

(* F#: `fun resolve _ id -> evalNode resolve id` — the evaluator `walk` hands `walkWith`. *)
let lift (#v:Type) (ev:evaluator v) : evaluator_with v = fun f _ id -> ev f id

(* F#: the private `walkWith`'s loop — `go`, with the recomputed node handed `Map.tryFind id prior`. *)
let rec go_with (#v:Type) (evw:evaluator_with v) (touches:read_witness) (deps:dmap)
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
          (match evw (resolve_in (lookups (reads_of deps id) results)) (assoc id prior) id with
           | Ok x -> go_with evw touches deps recompute prior cycles ((id, x) :: results) rest
           | Error m -> Error (EvalNodeFailed id m)))
     | Some p -> go_with evw touches deps recompute prior cycles ((id, p) :: results) rest)

(* F#: `walkWith`. *)
let walk_with (#v:Type) (evw:evaluator_with v) (touches:read_witness) (deps:dmap)
              (recompute:string -> bool) (prior:list (string & v)) (topo:topo_result)
  : Tot (outcome (eval_outcome v) propagation_error) =
  go_with evw touches deps recompute prior topo.cycles [] topo.order

(* F#: `evalWith` — `walkWith evalNode (fun _ -> true) Map.empty deps`. *)
let eval_with_over (#v:Type) (evw:evaluator_with v) (touches:read_witness) (deps:dmap) (topo:topo_result)
  : Tot (outcome (eval_outcome v) propagation_error) =
  walk_with evw touches deps always [] topo

(* F#: `evalFromWith` — `evalFrom`'s guard, then the prior-aware walk over the dirty set. *)
let eval_from_with_over (#v:Type) (evw:evaluator_with v) (touches:read_witness) (prior:list (string & v))
                   (changed:list string) (deps:dmap) (topo:topo_result)
  : Tot (outcome (eval_outcome v) propagation_error) =
  match unknown_of deps changed with
  | _ :: _ -> Error (EvalUnknownChange (unknown_of deps changed))
  | [] -> walk_with evw touches deps (in_set (dirty_from_changed_ids deps changed)) prior topo

(* The shared walk, handed an evaluator that ignores its prior, IS the prior-blind walk. *)
let rec go_with_lift (#v:Type) (ev:evaluator v) (touches:read_witness) (deps:dmap)
                     (recompute:string -> bool) (prior:list (string & v))
                     (cycles:list (list string)) (results:list (string & v)) (order:list string)
  : Lemma (ensures go_with (lift ev) touches deps recompute prior cycles results order ==
                   go ev touches deps recompute prior cycles results order) (decreases order) =
  match order with
  | [] -> ()
  | id :: rest ->
    (match reuse recompute prior id with
     | None ->
       (match first_undeclared deps id (touches id) with
        | Some _ -> ()
        | None ->
          (match ev (resolve_in (lookups (reads_of deps id) results)) id with
           | Ok x -> go_with_lift ev touches deps recompute prior cycles ((id, x) :: results) rest
           | Error _ -> ()))
     | Some p -> go_with_lift ev touches deps recompute prior cycles ((id, p) :: results) rest)

(* THE ADDED PREMISE. Along the walk the prior-blind reading takes — which, by the agreement
   theorem, is the walk the incremental driver takes — every node recomputed there answers the
   same handed its prior as handed none. Stated over the walk rather than over every resolver,
   because the prior is only ever handed beside the resolver that walk builds: an evaluator whose
   prior carries reuse state keyed to the inputs it was built from is entitled to trust that state
   there, and nowhere else. *)
let rec prior_blind_along (#v:Type) (evw:evaluator_with v) (touches:read_witness) (deps:dmap)
                          (recompute:string -> bool) (prior:list (string & v))
                          (results:list (string & v)) (order:list string)
  : Tot prop (decreases order) =
  match order with
  | [] -> True
  | id :: rest ->
    (match reuse recompute prior id with
     | Some p -> prior_blind_along evw touches deps recompute prior ((id, p) :: results) rest
     | None ->
       (match first_undeclared deps id (touches id) with
        | Some _ -> True
        | None ->
          evw (resolve_in (lookups (reads_of deps id) results)) (assoc id prior) id ==
            evw (resolve_in (lookups (reads_of deps id) results)) None id /\
          (match evw (resolve_in (lookups (reads_of deps id) results)) None id with
           | Ok x -> prior_blind_along evw touches deps recompute prior ((id, x) :: results) rest
           | Error _ -> True)))

(* Under the premise, the prior-aware walk is the prior-blind reading's walk, step for step. *)
let rec go_with_blind (#v:Type) (evw:evaluator_with v) (touches:read_witness) (deps:dmap)
                      (recompute:string -> bool) (prior:list (string & v))
                      (cycles:list (list string)) (results:list (string & v)) (order:list string)
  : Lemma (requires prior_blind_along evw touches deps recompute prior results order)
          (ensures go_with evw touches deps recompute prior cycles results order ==
                   go (blind evw) touches deps recompute prior cycles results order)
          (decreases order) =
  match order with
  | [] -> ()
  | id :: rest ->
    (match reuse recompute prior id with
     | Some p -> go_with_blind evw touches deps recompute prior cycles ((id, p) :: results) rest
     | None ->
       (match first_undeclared deps id (touches id) with
        | Some _ -> ()
        | None ->
          (match evw (resolve_in (lookups (reads_of deps id) results)) None id with
           | Ok x -> go_with_blind evw touches deps recompute prior cycles ((id, x) :: results) rest
           | Error _ -> ())))

(* A walk handed NO prior hands every node `None`, so the premise holds of it trivially. *)
let rec prior_blind_empty (#v:Type) (evw:evaluator_with v) (touches:read_witness) (deps:dmap)
                          (recompute:string -> bool) (results:list (string & v)) (order:list string)
  : Lemma (ensures prior_blind_along evw touches deps recompute [] results order) (decreases order) =
  match order with
  | [] -> ()
  | id :: rest ->
    (match reuse recompute ([] <: list (string & v)) id with
     | Some p -> prior_blind_empty evw touches deps recompute ((id, p) :: results) rest
     | None ->
       (match first_undeclared deps id (touches id) with
        | Some _ -> ()
        | None ->
          (match evw (resolve_in (lookups (reads_of deps id) results)) None id with
           | Ok x -> prior_blind_empty evw touches deps recompute ((id, x) :: results) rest
           | Error _ -> ())))

(* `evalWith` is `eval` of the prior-blind reading. *)
let eval_with_is_blind (#v:Type) (evw:evaluator_with v) (touches:read_witness) (deps:dmap)
                       (topo:topo_result)
  : Lemma (ensures eval_with_over evw touches deps topo == eval_over (blind evw) touches deps topo) =
  prior_blind_empty evw touches deps always [] topo.order;
  go_with_blind evw touches deps always [] topo.cycles [] topo.order

(* The two prior-blind entry points are the shared walk at `lift`: the refactor changed nothing. *)
let eval_is_walk_with (#v:Type) (ev:evaluator v) (touches:read_witness) (deps:dmap) (topo:topo_result)
  : Lemma (ensures eval_with_over (lift ev) touches deps topo == eval_over ev touches deps topo) =
  go_with_lift ev touches deps always [] topo.cycles [] topo.order

(* THEOREM — evalfromwith_agrees_over. `evalFromWith` over the dirty set equals `evalWith`, under
   `evalfrom_agrees`' premises for the prior-blind readings of the old and the new evaluator
   (`evw0`, which produced `out0`, and `evw1`, the evaluator after the change) and the one premise
   the prior adds, `prior_blind_along` along the incremental walk. *)
let evalfromwith_agrees_over (#v:Type) (evw0 evw1:evaluator_with v) (t0 t1:read_witness) (deps:dmap)
                        (changed:list string) (topo:topo_result) (out0:eval_outcome v)
                        (prior:list (string & v))
  : Lemma (requires agree_off (blind evw0) (blind evw1) changed /\ touches_off t0 t1 changed /\
                    distinct topo.order /\ Nil? (unknown_of deps changed) /\
                    eval_with_over evw0 t0 deps topo == Ok out0 /\ prior_of prior out0.values /\
                    prior_blind_along evw1 t1 deps (in_set (dirty_from_changed_ids deps changed))
                                      prior [] topo.order)
          (ensures eval_from_with_over evw1 t1 prior changed deps topo == eval_with_over evw1 t1 deps topo) =
  eval_with_is_blind evw0 t0 deps topo;
  eval_with_is_blind evw1 t1 deps topo;
  go_with_blind evw1 t1 deps (in_set (dirty_from_changed_ids deps changed)) prior topo.cycles [] topo.order;
  evalfrom_agrees_over (blind evw0) (blind evw1) t0 t1 deps changed topo out0 prior

(* ======================================================================================
   7. Pull: the needed set and demand-driven evaluation (Phase 317).

      F#: `neededFor`, `evalFor` / `evalForWith`, and the private `closureOver` /
      `restrictTopo` / `walkTopo` they run on. The DIRTY set closes a change set FORWARD over
      the dependents map; the NEEDED set closes a target set BACKWARD over the dependency map
      itself — the same loop, `Relation.reachable`, handed the other map, as production's
      `Relation.reachable` is handed the inverse for one and the map for the other (Phase 552).

      WHAT IS PROVED, over any dependency map and target set:
        - `needed_closed` / `needed_for_least` — the needed set holds the targets, is closed
          under "is read by a member" (every read of a member is a member), and is the LEAST
          such set. `Relation.reachable_closed` / `reachable_least` at the dependency map
          (`closed_by`), so they are the dirty set's `dirty_closed` / `dirty_least` read in the
          other direction.
        - `eval_for_agrees` — when the full evaluation `eval` succeeds, the demand-driven one
          over the SAME order restricted to the needed set succeeds with exactly its values on
          the needed set and exactly its cyclic groups that meet it. Its one premise is that the
          restriction set is closed, which `needed_closed` discharges, so as stated for `eval_for`
          it carries nothing beyond `eval`'s success. Like `evalfrom_agrees` it holds over ANY
          order and assumes nothing about `sort`.
        - `eval_for_with_agrees` — the same for the prior-aware pair, through
          `eval_with_is_blind`: no prior is handed, so the reading is prior-blind by construction.
        - `eval_for_invokes_needed` — the instrumented reading: every id the demand-driven walk
          hands the evaluator is needed. Nothing outside the needed set is evaluated.

      WHAT IS NOT CLAIMED. That `evalFor` agrees with `eval` when `eval` FAILS: a node that fails
      outside the needed set is never reached, so the pull can succeed where the full evaluation
      does not — which is what pulling is for.
   ====================================================================================== *)

(* F#: `neededFor` — `Relation.reachable (Relation.ofMap deps) targets`: the closure over the
   dependency map itself (Phase 552). `closed_by` — every out-edge of a member lands on a member
   — is `Relation.fst`'s; over the dependency map that is "every read of a member is a member",
   and `closed` above is the same relation over the dependents map, stated through `edge`. *)
let needed_for (deps:dmap) (targets:list string) : Tot (list string) =
  reachable deps targets

(* The needed set HOLDS the targets and is CLOSED under reads. *)
let needed_closed (deps:dmap) (targets:list string)
  : Lemma (subset targets (needed_for deps targets) /\ closed_by deps (needed_for deps targets)) =
  reachable_closed deps targets

(* THEOREM — needed_for_least. The needed set is the least set that holds the targets and is
   closed under reads: an id no target reads, directly or through another, is never needed. *)
let needed_for_least (deps:dmap) (targets s:list string)
  : Lemma (requires subset targets s /\ closed_by deps s)
          (ensures subset (needed_for deps targets) s) =
  reachable_least deps targets s

(* F#: `List.filter (fun id -> Set.contains id needed)` over `Order`. *)
let rec keep (s:list string) (l:list string) : Tot (list string) =
  match l with
  | [] -> []
  | h :: t -> if mem h s then h :: keep s t else keep s t

(* F#: `List.exists (fun id -> Set.contains id needed)` — a cyclic group that MEETS the set. *)
let rec meets (s:list string) (g:list string) : Tot bool =
  match g with
  | [] -> false
  | h :: t -> mem h s || meets s t

(* F#: `List.filter (List.exists (fun id -> Set.contains id needed))` over `Cycles`. *)
let rec keep_groups (s:list string) (gs:list (list string)) : Tot (list (list string)) =
  match gs with
  | [] -> []
  | g :: t -> if meets s g then g :: keep_groups s t else keep_groups s t

(* F#: the private `restrictTopo`. *)
let restrict_topo (s:list string) (topo:topo_result) : Tot topo_result =
  { order = keep s topo.order; cycles = keep_groups s topo.cycles }

(* The values a result map holds for the ids of `s` — the reading the agreement is stated in. *)
let rec keep_values (#v:Type) (s:list string) (l:list (string & v)) : Tot (list (string & v)) =
  match l with
  | [] -> []
  | (k, x) :: t -> if mem k s then (k, x) :: keep_values s t else keep_values s t

(* F#: `evalForWith` — the prior-aware walk, handed no prior, over the restricted order. *)
let eval_for_with_over (#v:Type) (evw:evaluator_with v) (touches:read_witness) (targets:list string)
                  (deps:dmap) (topo:topo_result)
  : Tot (outcome (eval_outcome v) propagation_error) =
  walk_with evw touches deps always [] (restrict_topo (needed_for deps targets) topo)

(* F#: `evalFor` — `evalForWith` over the evaluator that ignores its prior. *)
let eval_for_over (#v:Type) (ev:evaluator v) (touches:read_witness) (targets:list string) (deps:dmap)
             (topo:topo_result)
  : Tot (outcome (eval_outcome v) propagation_error) =
  eval_for_with_over (lift ev) touches targets deps topo

(* What `evalFor` evaluates: the walk's invocations over the restricted order. *)
let walk_for_invoked_over (#v:Type) (ev:evaluator v) (touches:read_witness) (targets:list string)
                     (deps:dmap) (topo:topo_result)
  : Tot (list string) =
  go_invoked ev touches deps always [] [] (keep (needed_for deps targets) topo.order)

let reads_are_dependents_of (deps:dmap) (id:string)
  : Lemma (reads_of deps id == dependents_of deps id) = ()

let rec assoc_keep_values (#v:Type) (s:list string) (l:list (string & v)) (k:string)
  : Lemma (requires mem k s) (ensures assoc k (keep_values s l) == assoc k l) =
  match l with
  | [] -> ()
  | _ :: t -> assoc_keep_values s t k

(* The two walks, step for step: over a CLOSED set, the restricted walk hands each kept node the
   same argument the full walk hands it, because every read of a kept node is kept. *)
let rec go_restrict (#v:Type) (ev:evaluator v) (touches:read_witness) (deps:dmap) (s:list string)
                    (cyc cyc':list (list string)) (results:list (string & v)) (order:list string)
  : Lemma (requires closed_by deps s)
          (ensures (match go ev touches deps always [] cyc results order with
                    | Ok o -> go ev touches deps always [] cyc' (keep_values s results) (keep s order) ==
                              Ok ({ values = keep_values s o.values; cyclic = cyc' })
                    | Error _ -> True))
          (decreases order) =
  match order with
  | [] -> ()
  | id :: rest ->
    (match first_undeclared deps id (touches id) with
     | Some _ -> ()
     | None ->
       if mem id s then begin
         reads_are_dependents_of deps id;
         let rs = reads_of deps id in
         let same (r:string) : Lemma (mem r rs ==> assoc r (keep_values s results) == assoc r results) =
           if mem r rs then assoc_keep_values s results r else ()
         in
         FStar.Classical.forall_intro same;
         lookups_eq rs (keep_values s results) results;
         (match ev (resolve_in (lookups rs results)) id with
          | Ok x -> go_restrict ev touches deps s cyc cyc' ((id, x) :: results) rest
          | Error _ -> ())
       end
       else
         (match ev (resolve_in (lookups (reads_of deps id) results)) id with
          | Ok x -> go_restrict ev touches deps s cyc cyc' ((id, x) :: results) rest
          | Error _ -> ()))

(* THEOREM — eval_for_agrees. When the full evaluation succeeds, the demand-driven evaluation
   succeeds with its values on the needed set and its cyclic groups that meet the needed set. *)
let eval_for_agrees (#v:Type) (ev:evaluator v) (touches:read_witness) (targets:list string)
                    (deps:dmap) (topo:topo_result) (out:eval_outcome v)
  : Lemma (requires eval_over ev touches deps topo == Ok out)
          (ensures (let s = needed_for deps targets in
                    eval_for_over ev touches targets deps topo ==
                      Ok ({ values = keep_values s out.values; cyclic = keep_groups s topo.cycles }))) =
  let s = needed_for deps targets in
  needed_closed deps targets;
  go_with_lift ev touches deps always [] (keep_groups s topo.cycles) [] (keep s topo.order);
  go_restrict ev touches deps s topo.cycles (keep_groups s topo.cycles) [] topo.order

(* THEOREM — eval_for_with_agrees. The same for the prior-aware pair: `evalForWith` hands no
   prior, so it is the prior-blind reading's demand-driven walk, and that reading's agreement. *)
let eval_for_with_agrees (#v:Type) (evw:evaluator_with v) (touches:read_witness) (targets:list string)
                         (deps:dmap) (topo:topo_result) (out:eval_outcome v)
  : Lemma (requires eval_with_over evw touches deps topo == Ok out)
          (ensures (let s = needed_for deps targets in
                    eval_for_with_over evw touches targets deps topo ==
                      Ok ({ values = keep_values s out.values; cyclic = keep_groups s topo.cycles }))) =
  let s = needed_for deps targets in
  eval_with_is_blind evw touches deps topo;
  needed_closed deps targets;
  prior_blind_empty evw touches deps always [] (keep s topo.order);
  go_with_blind evw touches deps always [] (keep_groups s topo.cycles) [] (keep s topo.order);
  go_restrict (blind evw) touches deps s topo.cycles (keep_groups s topo.cycles) [] topo.order

let rec go_invoked_within (#v:Type) (ev:evaluator v) (touches:read_witness) (deps:dmap)
                          (recompute:string -> bool) (prior:list (string & v))
                          (results:list (string & v)) (order:list string) (x:string)
  : Lemma (ensures mem x (go_invoked ev touches deps recompute prior results order) ==> mem x order)
          (decreases order) =
  match order with
  | [] -> ()
  | id :: rest ->
    (match reuse recompute prior id with
     | None ->
       (match first_undeclared deps id (touches id) with
        | Some _ -> ()
        | None ->
          (match ev (resolve_in (lookups (reads_of deps id) results)) id with
           | Ok y -> go_invoked_within ev touches deps recompute prior ((id, y) :: results) rest x
           | Error _ -> ()))
     | Some p -> go_invoked_within ev touches deps recompute prior ((id, p) :: results) rest x)

let rec mem_keep (s l:list string) (x:string) : Lemma (mem x (keep s l) ==> mem x s) =
  match l with
  | [] -> ()
  | _ :: t -> mem_keep s t x

(* THEOREM — eval_for_invokes_needed. Every id the demand-driven walk hands the evaluator is
   needed: nothing outside the needed set is evaluated. *)
let eval_for_invokes_needed (#v:Type) (ev:evaluator v) (touches:read_witness) (targets:list string)
                            (deps:dmap) (topo:topo_result) (x:string)
  : Lemma (ensures mem x (walk_for_invoked_over ev touches targets deps topo) ==> mem x (needed_for deps targets)) =
  let s = needed_for deps targets in
  go_invoked_within ev touches deps always [] [] (keep s topo.order) x;
  mem_keep s topo.order x

(* ======================================================================================
   8. The order by CERTIFICATE (Phase 308) — `Propagation.validTopo`, clause for clause.

   What this section models is the CHECKER production holds `sort`'s result to — a function the
   model can state exactly — and it proves that an order the checker accepts discharges the one
   premise the `_over` agreement theorems take of the walked order. Since Phase 553 `sort` is in
   the model too (section 9) and that premise is a theorem of it; since Phase 556 its third
   clause — dependencies-first — is a theorem of `Relation.fst` too (`components_deps_first`, the
   `relation-components-deps-first` row). The checker stays as the statement production holds
   `sort`'s result to, and the differential still holds `sort`'s result to it on every graph it
   draws: that guards the extraction and production, not the proof.

   The checker's third clause — every read of an `Order` id that the map holds appears earlier, or
   lies in a cycle — is what makes the walk's values MEAN something (an acyclic read is computed
   before its reader). No theorem in this model needs it: agreement holds over any duplicate-free
   order, as section 5 says. It is modelled so the extracted checker is production's, clause for
   clause, and the differential can hold the two to each other.
   ====================================================================================== *)

(* F#: the `Set.forall` over one node's reads — each is not held by the map, already walked, or
   cyclic. *)
let rec reads_ok (deps:dmap) (cyc seen:list string) (rs:list string) : Tot bool =
  match rs with
  | [] -> true
  | r :: rest -> (not (has_key r deps) || mem r seen || mem r cyc) && reads_ok deps cyc seen rest

(* F#: the private `ordered` loop, `seen` the ids of `Order` already passed. *)
let rec ordered (deps:dmap) (cyc seen order:list string) : Tot bool (decreases order) =
  match order with
  | [] -> true
  | id :: rest -> reads_ok deps cyc seen (reads_of deps id) && ordered deps cyc (id :: seen) rest

(* F#: `Propagation.validTopo`. The F# measures distinctness as `List.distinct` keeping the length,
   which is `distinct` here. *)
let valid_topo (deps:dmap) (topo:topo_result) : Tot bool =
  let cyc = concat topo.cycles in
  let all = app topo.order cyc in
  distinct all && same_set all (keys deps) && ordered deps cyc [] topo.order

(* THEOREM — valid_topo_distinct. An order the checker accepts holds no id twice: the premise
   `evalfrom_agrees`, `evalfromwith_agrees` and the pull theorems take of the walked order. *)
let valid_topo_distinct (deps:dmap) (topo:topo_result)
  : Lemma (requires valid_topo deps topo) (ensures distinct topo.order) =
  distinct_app_left topo.order (concat topo.cycles)

(* THEOREM — evalfrom_agrees_certified. The agreement theorem with its order premise replaced by
   the certificate: whatever produced the order, if `validTopo` accepts it the incremental walk
   agrees with the full one. *)
let evalfrom_agrees_certified (#v:Type) (ev0 ev1:evaluator v) (t0 t1:read_witness) (deps:dmap)
                              (changed:list string) (topo:topo_result) (out0:eval_outcome v)
                              (prior:list (string & v))
  : Lemma (requires agree_off ev0 ev1 changed /\ touches_off t0 t1 changed /\ valid_topo deps topo /\
                    Nil? (unknown_of deps changed) /\ eval_over ev0 t0 deps topo == Ok out0 /\
                    prior_of prior out0.values)
          (ensures eval_from_over ev1 t1 prior changed deps topo == eval_over ev1 t1 deps topo) =
  valid_topo_distinct deps topo;
  evalfrom_agrees_over ev0 ev1 t0 t1 deps changed topo out0 prior

(* THEOREM — evalfromwith_agrees_certified. The prior-aware reading, the same substitution. *)
let evalfromwith_agrees_certified (#v:Type) (evw0 evw1:evaluator_with v) (t0 t1:read_witness)
                                  (deps:dmap) (changed:list string) (topo:topo_result)
                                  (out0:eval_outcome v) (prior:list (string & v))
  : Lemma (requires agree_off (blind evw0) (blind evw1) changed /\ touches_off t0 t1 changed /\
                    valid_topo deps topo /\ Nil? (unknown_of deps changed) /\
                    eval_with_over evw0 t0 deps topo == Ok out0 /\ prior_of prior out0.values /\
                    prior_blind_along evw1 t1 deps (in_set (dirty_from_changed_ids deps changed))
                                      prior [] topo.order)
          (ensures eval_from_with_over evw1 t1 prior changed deps topo == eval_with_over evw1 t1 deps topo) =
  valid_topo_distinct deps topo;
  evalfromwith_agrees_over evw0 evw1 t0 t1 deps changed topo out0 prior


(* ======================================================================================
   9. The order, COMPUTED (Phase 553) — `Propagation.sort`, clause for clause, and the driver's
      entry points at production's signature.

      F#: `sort deps = Relation.ofMap known |> Relation.sort`, `known` the map with every read
      the map does not hold removed ("they were never nodes: an edge to an id the map does not
      hold is a validator defect, not a cycle", D205), read into `TopoResult`. `Relation.ofMap`
      prepares the keys ascending with their reads ascending; the model's `Relation.sort` visits
      the list as given, and the differential hands it the same ascending form, so the two orders
      are one order (`Proofs.Oracle`, the certificate case).

      WHAT IS PROVED. `sort_distinct`: `Order` holds no id twice — the premise every `_over`
      agreement theorem took of the walked order, now a theorem, so `evalfrom_agrees` and
      `evalfromwith_agrees` below carry none. `sort_same_set`: `Order` and the cycles' members
      together are exactly the map's keys. Those are `validTopo`'s first two clauses.

      The checker's third clause — every read of an `Order` id that the map holds appears
      earlier, or lies in a cycle — is dependencies-first, which `Relation.fst` states
      (`deps_first`) and, since Phase 556, proves of the pass (`components_deps_first`); the
      differential holds `sort` to it as before, guarding the extraction and production.
   ====================================================================================== *)

(* F#: `reads |> Set.filter (fun d -> Map.containsKey d deps)`. *)
let rec known_reads (deps:dmap) (rs:list string) : Tot (list string) =
  match rs with
  | [] -> []
  | r :: t -> if has_key r deps then r :: known_reads deps t else known_reads deps t

(* F#: `deps |> Map.map (fun _ reads -> ...)` — every entry's reads filtered against `all`. *)
let rec known_of (deps all:dmap) : Tot dmap =
  match deps with
  | [] -> []
  | (k, rs) :: t -> (k, known_reads all rs) :: known_of t all

(* F#: `known`. *)
let known (deps:dmap) : Tot dmap = known_of deps deps

(* F#: `Propagation.sort`. *)
let sort (deps:dmap) : Tot topo_result =
  let t = Relation.sort (known deps) in
  { order = t.torder; cycles = t.tcycles }

(* THEOREM — sort_distinct. `Order` holds no id twice: `Relation.sort_order_distinct` at the
   known map. The premise `propagation-order-distinct` assumed until Phase 553. *)
let sort_distinct (deps:dmap) : Lemma (distinct (sort deps).order) =
  sort_order_distinct (known deps)

let rec keys_known_of (deps all:dmap) : Lemma (keys (known_of deps all) == keys deps) =
  match deps with
  | [] -> ()
  | _ :: t -> keys_known_of t all

let rec known_reads_held (all:dmap) (rs:list string) (x:string)
  : Lemma (requires mem x (known_reads all rs)) (ensures has_key x all) =
  match rs with
  | [] -> ()
  | r :: t -> if x = r && has_key r all then () else known_reads_held all t x

let rec range_known_of (deps all:dmap) (x:string)
  : Lemma (requires mem x (range (known_of deps all))) (ensures has_key x all) =
  match deps with
  | [] -> ()
  | (k, rs) :: t ->
    mem_app x (known_reads all rs) (range (known_of t all));
    if mem x (known_reads all rs) then known_reads_held all rs x else range_known_of t all x

(* The known map's nodes are the map's keys: a read it keeps is a key, and the keys are kept. *)
let nodes_known (deps:dmap) : Lemma (same_set (nodes (known deps)) (keys deps)) =
  let k = known deps in
  keys_known_of deps deps;
  let a1 (x:string) : Lemma (mem x (nodes k) ==> mem x (keys deps)) =
    mem_nub x (app (keys k) (range k));
    mem_app x (keys k) (range k);
    if mem x (range k) then begin range_known_of deps deps x; has_key_keys x deps end
  in
  let a2 (x:string) : Lemma (mem x (keys deps) ==> mem x (nodes k)) =
    mem_nub x (app (keys k) (range k));
    mem_app x (keys k) (range k)
  in
  FStar.Classical.forall_intro a1;
  FStar.Classical.forall_intro a2;
  subset_intro (nodes k) (keys deps);
  subset_intro (keys deps) (nodes k)

(* THEOREM — sort_same_set. `Order` and the cycles' members, together, are exactly the map's
   keys, each once: `validTopo`'s second clause, off `Relation.sort_partition`. *)
let sort_same_set (deps:dmap)
  : Lemma (same_set (app (sort deps).order (concat (sort deps).cycles)) (keys deps)) =
  let k = known deps in
  sort_partition k;
  nodes_known deps;
  let all = app (sort deps).order (concat (sort deps).cycles) in
  let a1 (x:string) : Lemma (mem x all ==> mem x (keys deps)) =
    if mem x all then begin subset_mem all (nodes k) x; subset_mem (nodes k) (keys deps) x end
  in
  let a2 (x:string) : Lemma (mem x (keys deps) ==> mem x all) =
    if mem x (keys deps) then begin subset_mem (keys deps) (nodes k) x; subset_mem (nodes k) all x end
  in
  FStar.Classical.forall_intro a1;
  FStar.Classical.forall_intro a2;
  subset_intro all (keys deps);
  subset_intro (keys deps) all

(* F#: `eval` — the walk over `sort deps`. *)
let eval (#v:Type) (ev:evaluator v) (touches:read_witness) (deps:dmap)
  : Tot (outcome (eval_outcome v) propagation_error) =
  eval_over ev touches deps (sort deps)

(* F#: `evalFrom`. *)
let eval_from (#v:Type) (ev:evaluator v) (touches:read_witness) (prior:list (string & v))
              (changed:list string) (deps:dmap)
  : Tot (outcome (eval_outcome v) propagation_error) =
  eval_from_over ev touches prior changed deps (sort deps)

(* What `evalFrom` evaluates, in order. *)
let walk_invoked (#v:Type) (ev:evaluator v) (touches:read_witness) (prior:list (string & v))
                 (changed:list string) (deps:dmap)
  : Tot (list string) =
  walk_invoked_over ev touches prior changed deps (sort deps)

(* F#: `evalWith`. *)
let eval_with (#v:Type) (evw:evaluator_with v) (touches:read_witness) (deps:dmap)
  : Tot (outcome (eval_outcome v) propagation_error) =
  eval_with_over evw touches deps (sort deps)

(* F#: `evalFromWith`. *)
let eval_from_with (#v:Type) (evw:evaluator_with v) (touches:read_witness) (prior:list (string & v))
                   (changed:list string) (deps:dmap)
  : Tot (outcome (eval_outcome v) propagation_error) =
  eval_from_with_over evw touches prior changed deps (sort deps)

(* F#: `evalForWith`. *)
let eval_for_with (#v:Type) (evw:evaluator_with v) (touches:read_witness) (targets:list string)
                  (deps:dmap)
  : Tot (outcome (eval_outcome v) propagation_error) =
  eval_for_with_over evw touches targets deps (sort deps)

(* F#: `evalFor`. *)
let eval_for (#v:Type) (ev:evaluator v) (touches:read_witness) (targets:list string) (deps:dmap)
  : Tot (outcome (eval_outcome v) propagation_error) =
  eval_for_over ev touches targets deps (sort deps)

(* What `evalFor` evaluates. *)
let walk_for_invoked (#v:Type) (ev:evaluator v) (touches:read_witness) (targets:list string)
                     (deps:dmap)
  : Tot (list string) =
  walk_for_invoked_over ev touches targets deps (sort deps)

(* THEOREM — evalfrom_agrees. `evalFrom` over the dirty set equals `eval`, under the evaluator
   contract alone: the order premise `evalfrom_agrees_over` takes is `sort_distinct`. *)
let evalfrom_agrees (#v:Type) (ev0 ev1:evaluator v) (t0 t1:read_witness) (deps:dmap)
                    (changed:list string) (out0:eval_outcome v) (prior:list (string & v))
  : Lemma (requires agree_off ev0 ev1 changed /\ touches_off t0 t1 changed /\
                    Nil? (unknown_of deps changed) /\ eval ev0 t0 deps == Ok out0 /\
                    prior_of prior out0.values)
          (ensures eval_from ev1 t1 prior changed deps == eval ev1 t1 deps) =
  sort_distinct deps;
  evalfrom_agrees_over ev0 ev1 t0 t1 deps changed (sort deps) out0 prior

(* THEOREM — evalfromwith_agrees. The prior-aware reading, the same discharge. *)
let evalfromwith_agrees (#v:Type) (evw0 evw1:evaluator_with v) (t0 t1:read_witness) (deps:dmap)
                        (changed:list string) (out0:eval_outcome v) (prior:list (string & v))
  : Lemma (requires agree_off (blind evw0) (blind evw1) changed /\ touches_off t0 t1 changed /\
                    Nil? (unknown_of deps changed) /\ eval_with evw0 t0 deps == Ok out0 /\
                    prior_of prior out0.values /\
                    prior_blind_along evw1 t1 deps (in_set (dirty_from_changed_ids deps changed))
                                      prior [] (sort deps).order)
          (ensures eval_from_with evw1 t1 prior changed deps == eval_with evw1 t1 deps) =
  sort_distinct deps;
  evalfromwith_agrees_over evw0 evw1 t0 t1 deps changed (sort deps) out0 prior

(* ======================================================================================
   TWINS (Phase 309) — the extractor premise, sampled at this model. What a twin is, why it
   is here and how the list is evaluated are `Twin`'s (Phase 504), which every model opens.
   ====================================================================================== *)

let twin_deps : dmap = [ ("b", [ "a" ]); ("c", [ "b" ]) ]

let twins : list twin = [
  { tname = "dependents-inverts-the-edges";
    tholds = (fun () -> dependents twin_deps = [ ("a", [ "b" ]); ("b", [ "c" ]) ]) };
  { tname = "dirty-set-is-the-downstream-closure";
    tholds = (fun () -> dirty_from_changed_ids twin_deps [ "a" ] = [ "a"; "b"; "c" ]) };
  { tname = "an-edge-has-a-direction";
    tholds = (fun () -> edge twin_deps "b" "c" = false) };
  { tname = "the-certificate-accepts-a-dependency-order";
    tholds = (fun () -> valid_topo twin_deps ({ order = [ "b"; "c" ]; cycles = [] }) = true) };
  { tname = "the-certificate-refuses-a-reader-before-its-read";
    tholds = (fun () -> valid_topo twin_deps ({ order = [ "c"; "b" ]; cycles = [] }) = false) };
  { tname = "sort-computes-the-dependency-order-and-drops-a-dangling-read";
    tholds = (fun () -> sort twin_deps = ({ order = [ "b"; "c" ]; cycles = [] }) &&
                     known twin_deps = [ ("b", []); ("c", [ "b" ]) ]) } ]

let _ = assert_norm (twins_hold twins == true)
