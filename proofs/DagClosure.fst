(*
   DagClosure — the DAG beneath the fold: Fuaran.Core's lane DAG, its closure, its drain, the
   recovery of each lane's delta, merged heads, the reconcile's partition, replay, checkpoints,
   truncation and the reachability index, each modelled clause for clause with
   `src/Fuaran.Core.OpStream.Dag/DagOpStream.fs` (fuaran-core Phases 134 to 437; a module of its
   own since Phase 534).

   WHY IT IS A MODULE OF ITS OWN. `DagFold` proves fold confluence where the lane deltas are already
   known, in the style of a small algebra: lists read as sets, with SMT patterns doing the rewriting.
   Everything here starts one step earlier, at the content-addressed DAG, and is written in the
   opposite style — NO SMT PATTERNS (section 11 says why), every lemma called by name. The two
   halves were one 6,600-line file until Phase 534; split, each is checked in one proof style, and
   this module opens `DagFold` for the fold it lifts (`fold_once`, the footprints, the conflicts).

   THE SECTIONS keep the numbers they had in the one file, so every citation of "section 13" or
   "13.13" elsewhere still lands: 11 (the DAG beneath the fold), 13 (the Kahn drain over an
   abstract DAG; since Phase 534 it also carries the spine case Phase 142 proved, which was section
   12), 14 (delta recovery over a merged head), 15 (the reconcile's partition), 16 (`tryReplayTo`
   and the refusing `append`), 17 (slot writes commute), 18 (`mergeBase` over any two heads), 19
   (`conflictsOfHeads`), 20 (the reachability index), 21 (checkpoints). There is no section 12.

   ONE MODEL OF `Dag.mergeBase`, AND IT IS NOT HERE. Production's `mergeBase` is `DagPrimitives`'
   `merge_base_now`; this module carries the key it ranks by (`deeper`, `max_by`) and the facts
   about it (`strictly_deeper`, `max_common_is_maximal`, `merged_common_is_the_closure`), and the
   theorems about the function itself are stated over the code that runs, in `DagPrimitives`.

   Apache-2.0, like everything beside it.
*)
module DagClosure

open Twin
open ListAlgebra
open DagFold

(* ======================================================================================
   11. The DAG beneath the fold (Phase 134).

   Sections 0–10 (`DagFold`) start where the lane deltas are already known. Production does not:
   it holds a content-addressed DAG and rebuilds each lane's delta from it — `Dag.ancestorsOf`,
   the topological order `Dag.between` walks, `Dag.betweenOps` — and only then hands the deltas
   to the pairwise sweep. This section models that step for the shape `FoldConfluence.foldOnce`
   builds and every local-first deployment has: ONE shared base node and N linear chains, one
   per writer.

   WHAT STANDS IN FOR WHAT:
     - `node` / `dag`   — F#: `DagNode<'Op>` / `Dag.T<'Op>`. Production keys nodes by a
                          `Map<string, _>`; the model carries the same nodes as a list and
                          `lookup` is `Map.tryFind`. Membership is the meaning, as in section 0.
     - `mint`           — F#: `Dag.nodeHash`. ABSTRACTED, deliberately: nothing here hashes.
                          What content addressing buys the recovery is that the ids come out
                          DISTINCT, and that is a PREMISE (`distinct_ids`, and `resolves` below)
                          rather than something derived — the hash-collision assumption the
                          README names as exactly that.
     - `chain_rev` / `lane_nodes` — F#: the per-lane fold in `foldOnce`, `ops |> List.fold
                          (fun (h, dd) op -> Dag.append hashFn w actor op h dd) (baseId, d)`.
                          The chain is defined HEAD-FIRST because that is how a DAG is read: a
                          node names its parent, so the recovery walks down and the induction
                          aligns with the walk rather than against it.
     - `ancestors_of`   — F#: `Dag.ancestorsOf`, the transitive parent closure. Production drains
                          an explicit work-list against an already-seen `Set`; the model walks
                          with a LIST as fuel — one step per node, the same bound that makes the
                          work-list terminate. Fuel is a list rather than a number so that the
                          extracted oracle stays free of integer arithmetic, as the rest of the
                          module is.
     - `between` / `between_ops` — F#: `Dag.between` / `Dag.betweenOps`, clause for clause: the
                          head's closure in topological order, minus the base's closure, looked
                          up, then projected to ops.

   WHAT IS NOT MODELLED, and the one boundary worth reading twice. Hashing itself (above).
   `Dag.mergeBase`, which is not on this path at all — `foldOnce` hands `reconcileMany` the base
   node's id directly (it is modelled since Phase 158, in section 14, for the shape where a
   divergence point does have to be located). And **how the topological order is CHOSEN**: production's head drain (`drainClosure`) is
   Kahn's algorithm draining a ready frontier smallest-id-first, and `topo_of` below is the
   reverse of the parent walk. On the base-plus-N-chains shape the head's closure is a spine,
   every pair of its members is comparable under the ancestor relation, and a spine therefore
   admits exactly ONE topological order — so the two coincide. That last sentence was ARGUED here
   by Phase 134 and measured by the differential host against the real `Dag.betweenOps`; Phase
   142 mechanised it — a distinct enumeration of a spine's closure respecting each node's one
   parent is forced, and Kahn's drain produces such an enumeration (13.12). Everything downstream of the
   order — the closure, the difference against the base's closure, the lookup, and the fold built
   on them — is proved.
   ====================================================================================== *)

(* ---- list facts the recovery needs ---- *)

(* `diff l [e]` drops nothing when `e` is not there. F#: the `List.filter` in `Dag.between`. *)
let rec diff_no_mem (#a:eqtype) (l:list a) (e:a)
  : Lemma (requires not (mem e l)) (ensures diff l [e] == l)
  = match l with
    | [] -> ()
    | _ :: t -> diff_no_mem t e

(* ---- the DAG (F#: `DagNode<'Op>` and `Dag.T<'Op>` in DagOpStream.fs) ---- *)

type node (op:eqtype) = {
  nid      : string;
  nparents : list string;
  nop      : op
}

type dag (op:eqtype) = { nodes : list (node op) }

(* F#: `'a option`, as `Map.tryFind` returns it. Spelled LOCALLY rather than taken from
   `FStar.Pervasives.Native`: F*'s F# backend extracts that one as `FStar_Pervasives_Native.Some`
   and the release ships no F# runtime for it, so the oracle would not compile. The same finding
   the list helpers in section 0 are self-contained for — README, finding 2. *)
type found (a:Type) =
  | Missing : found a
  | Found   : a -> found a

(* F#: `Map.tryFind id dag.Nodes`. *)
let rec lookup (#op:eqtype) (ns:list (node op)) (id:string) : Tot (found (node op)) =
  match ns with
  | [] -> Missing
  | n :: t -> if n.nid = id then Found n else lookup t id

let rec ids_of (#op:eqtype) (ns:list (node op)) : Tot (list string) =
  match ns with
  | [] -> []
  | n :: t -> n.nid :: ids_of t

(* F#: the `List.map (fun n -> n.Op)` that makes `betweenOps` out of `between`. *)
let rec ops_of (#op:eqtype) (ns:list (node op)) : Tot (list op) =
  match ns with
  | [] -> []
  | n :: t -> n.nop :: ops_of t

(* THE PREMISE, in the form the recovery actually consumes: every node of `ns` is the node the
   DAG holds under its own id. Content addressing is what gives production this — `nodeHash` is
   injective on (sorted parents, actor, encoded op) unless the hash collides — and
   `resolves_of_distinct` below derives it from plain id distinctness. *)
let distinct_ids (#op:eqtype) (d:dag op) : Tot bool = distinct (ids_of d.nodes)

let resolves (#op:eqtype) (d:dag op) (ns:list (node op)) : prop =
  forall (n:node op). mem n ns ==> lookup d.nodes n.nid == Found n

let rec mem_ids_of (#op:eqtype) (n:node op) (ns:list (node op))
  : Lemma (ensures mem n ns ==> mem n.nid (ids_of ns))
  = match ns with
    | [] -> ()
    | _ :: t -> mem_ids_of n t

let rec lookup_mem_distinct (#op:eqtype) (ns:list (node op)) (n:node op)
  : Lemma (requires distinct (ids_of ns) /\ mem n ns) (ensures lookup ns n.nid == Found n)
  = match ns with
    | [] -> ()
    | m :: t ->
      if m.nid = n.nid then begin
        (* `n` cannot be in the tail: its id is `m`'s, and distinctness forbids that. *)
        mem_ids_of n t;
        assert (not (mem n t));
        assert (n == m)
      end
      else lookup_mem_distinct t n

(* Distinct ids in the DAG make every node it holds resolve — the bridge from the premise as
   stated to the premise as used. *)
let resolves_of_distinct (#op:eqtype) (d:dag op) (ns:list (node op))
  : Lemma (requires distinct_ids d /\ (forall (n:node op). mem n ns ==> mem n d.nodes))
          (ensures resolves d ns)
  = let aux (n:node op) : Lemma (mem n ns ==> lookup d.nodes n.nid == Found n) =
      if mem n ns then lookup_mem_distinct d.nodes n else ()
    in
    FStar.Classical.forall_intro aux

(* ---- the shape (F#: what `FoldConfluence.foldOnce` builds) ---- *)

(* A lane's head, from its ops in REVERSE — newest first. F#: the `h` the per-lane `List.fold`
   ends with. An EMPTY lane leaves its head AT the base id, which is exactly what production
   does: no node is appended, so `betweenOps base base` is `[]`. *)
let rec chain_head_rev (#op:eqtype) (mint:string -> string -> op -> string)
  (actor:string) (q:string) (rl:list op) : Tot string (decreases rl) =
  match rl with
  | [] -> q
  | o :: t -> mint (chain_head_rev mint actor q t) actor o

(* The lane's nodes, newest first. F#: the nodes `Dag.append` adds, each naming its parent. *)
let rec chain_rev (#op:eqtype) (mint:string -> string -> op -> string)
  (actor:string) (q:string) (rl:list op) : Tot (list (node op)) (decreases rl) =
  match rl with
  | [] -> []
  | o :: t ->
    let p = chain_head_rev mint actor q t in
    { nid = mint p actor o; nparents = [ p ]; nop = o } :: chain_rev mint actor q t

(* … and in append order, which is the order `between` returns them in. *)
let lane_nodes (#op:eqtype) (mint:string -> string -> op -> string)
  (actor:string) (q:string) (l:list op) : Tot (list (node op)) =
  rev (chain_rev mint actor q (rev l))

let lane_head (#op:eqtype) (mint:string -> string -> op -> string)
  (actor:string) (q:string) (l:list op) : Tot string =
  chain_head_rev mint actor q (rev l)

(* F#: the `List.fold` over `lanes |> List.indexed`. A lane is (actor, ops): `foldOnce` names its
   actors `lane-<i>` and folds the actor into every node id, which is what keeps two lanes
   carrying the SAME op sequence from converging to one chain. The model takes the actors as
   given — what the actor is FOR is distinctness, and distinctness is the premise. *)
(* One lane as `foldOnce` sees it: the actor its nodes are minted under, and its ops. A record
   rather than a pair, so the extracted oracle names no tuple type the `Prims` shim would have to
   supply. *)
type lane (op:eqtype) = { lactor : string; lops : list op }

let rec lanes_nodes (#op:eqtype) (mint:string -> string -> op -> string)
  (q:string) (lanes:list (lane op)) : Tot (list (node op)) =
  match lanes with
  | [] -> []
  | ln :: t -> app (lane_nodes mint ln.lactor q ln.lops) (lanes_nodes mint q t)

let rec lane_heads (#op:eqtype) (mint:string -> string -> op -> string)
  (q:string) (lanes:list (lane op)) : Tot (list string) =
  match lanes with
  | [] -> []
  | ln :: t -> lane_head mint ln.lactor q ln.lops :: lane_heads mint q t

let rec lane_ops (#op:eqtype) (lanes:list (lane op)) : Tot (list (list op)) =
  match lanes with
  | [] -> []
  | ln :: t -> ln.lops :: lane_ops t

(* The whole DAG: one base node with no parents, and one chain per lane. F#: `Dag.append hashFn w
   (Human "base") baseOp "" Dag.empty` followed by the per-lane fold. *)
let build_dag (#op:eqtype) (mint:string -> string -> op -> string)
  (base_id:string) (base_op:op) (lanes:list (lane op)) : Tot (dag op) =
  { nodes = { nid = base_id; nparents = []; nop = base_op } :: lanes_nodes mint base_id lanes }

(* ---- the closure and the delta (F#: `Dag.ancestorsOf`, `Dag.between`, `Dag.betweenOps`) ---- *)

let rec ancestors_of (#op:eqtype) (d:dag op) (fuel:list (node op)) (id:string)
  : Tot (list string) (decreases %[fuel; (0 <: nat); ([] <: list string)]) =
  match fuel with
  | [] -> []
  | _ :: fuel' ->
    (match lookup d.nodes id with
     | Missing -> []
     | Found n -> id :: ancestors_all d fuel' n.nparents)

and ancestors_all (#op:eqtype) (d:dag op) (fuel:list (node op)) (ids:list string)
  : Tot (list string) (decreases %[fuel; (1 <: nat); ids]) =
  match ids with
  | [] -> []
  | p :: t -> app (ancestors_of d fuel p) (ancestors_all d fuel t)

(* `fuel` allows one walk step per element of `l`, PLUS one for the node the walk ends at. *)
let rec covers (#a #b:Type) (fuel:list a) (l:list b) : Tot bool (decreases l) =
  match l with
  | [] -> Cons? fuel
  | _ :: t -> (match fuel with | [] -> false | _ :: f -> covers f t)

let rec drop_by (#a #b:Type) (fuel:list a) (l:list b) : Tot (list a) (decreases l) =
  match l with
  | [] -> fuel
  | _ :: t -> (match fuel with | [] -> [] | _ :: f -> drop_by f t)

let rec covers_drop (#a #b:Type) (fuel:list a) (l:list b)
  : Lemma (requires covers fuel l) (ensures Cons? (drop_by fuel l) /\ Cons? fuel) (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> (match fuel with | [] -> () | _ :: f -> covers_drop f t)

(* F#: `drainClosure dag [ head ]`'s placed nodes. On this shape the head's closure is a spine, so its topological
   order is the parent walk reversed. `topo_forced` below is why that is not a shortcut. *)
let topo_of (#op:eqtype) (d:dag op) (fuel:list (node op)) (head:string) : Tot (list string) =
  rev (ancestors_of d fuel head)

(* F#: the `List.map (fun id -> dag.Nodes.[id])` that ends `Dag.between`. *)
let rec nodes_for (#op:eqtype) (d:dag op) (l:list string) : Tot (list (node op)) =
  match l with
  | [] -> []
  | id :: t ->
    (match lookup d.nodes id with
     | Missing -> nodes_for d t
     | Found n -> n :: nodes_for d t)

(* F#: `Dag.between` — `drainClosure dag [ head ] |> fst |> List.filter (fun id -> not (Set.contains id
   baseClosure)) |> List.map (fun id -> dag.Nodes.[id])`. *)
let between (#op:eqtype) (d:dag op) (fuel:list (node op)) (base_id:string) (head:string)
  : Tot (list (node op)) =
  nodes_for d (diff (topo_of d fuel head) (ancestors_of d fuel base_id))

(* F#: `Dag.betweenOps`. *)
let between_ops (#op:eqtype) (d:dag op) (fuel:list (node op)) (base_id:string) (head:string)
  : Tot (list op) =
  ops_of (between d fuel base_id head)

(* ---- the algebra the recovery runs on ----

   These carry no SMT pattern, and that is deliberate rather than an omission: `rev`, `app`,
   `ids_of` and `ops_of` all rewrite into one another, and left to fire on their own they turn
   the delta-recovery query into one Z3 does not return from. Each is called by name where it is
   needed, which is also how the proof below reads as the calculation it is. *)

let rec ids_of_app (#op:eqtype) (l m:list (node op))
  : Lemma (ensures ids_of (app l m) == app (ids_of l) (ids_of m))
  = match l with
    | [] -> ()
    | _ :: t -> ids_of_app t m

let rec ids_of_rev (#op:eqtype) (ns:list (node op))
  : Lemma (ensures ids_of (rev ns) == rev (ids_of ns))
  = match ns with
    | [] -> ()
    | n :: t ->
      ids_of_rev t;
      ids_of_app (rev t) [ n ]

let rec ops_of_app (#op:eqtype) (l m:list (node op))
  : Lemma (ensures ops_of (app l m) == app (ops_of l) (ops_of m))
  = match l with
    | [] -> ()
    | _ :: t -> ops_of_app t m

let rec ops_of_rev (#op:eqtype) (ns:list (node op))
  : Lemma (ensures ops_of (rev ns) == rev (ops_of ns))
  = match ns with
    | [] -> ()
    | n :: t ->
      ops_of_rev t;
      ops_of_app (rev t) [ n ]

(* A chain carries its lane's ops and nothing else — newest first, as it is built. *)
let rec ops_of_chain_rev (#op:eqtype) (mint:string -> string -> op -> string)
  (actor:string) (q:string) (rl:list op)
  : Lemma (ensures ops_of (chain_rev mint actor q rl) == rl) (decreases rl)
  = match rl with
    | [] -> ()
    | _ :: t -> ops_of_chain_rev mint actor q t

(* F#: `List.map (fun id -> dag.Nodes.[id])` over the ids of nodes the DAG holds is those nodes. *)
let rec nodes_for_ids (#op:eqtype) (d:dag op) (ns:list (node op))
  : Lemma (requires resolves d ns) (ensures nodes_for d (ids_of ns) == ns)
  = match ns with
    | [] -> ()
    | n :: t ->
      assert (mem n ns);
      assert (resolves d t);
      nodes_for_ids d t

(* ---- the closure of a chain (F#: `Dag.ancestorsOf` on a lane head) ---- *)

(* The closure of a chain's head is the chain's ids, newest first, then the closure of the node
   the chain hangs off. The induction is structural in the lane's ops BECAUSE the chain is
   defined head-first: the walk and the construction run the same way. *)
let rec ancestors_chain (#op:eqtype) (d:dag op) (mint:string -> string -> op -> string)
  (actor:string) (q:string) (rl:list op) (fuel:list (node op))
  : Lemma (requires resolves d (chain_rev mint actor q rl) /\ covers fuel rl)
          (ensures ancestors_of d fuel (chain_head_rev mint actor q rl)
                   == app (ids_of (chain_rev mint actor q rl))
                          (ancestors_of d (drop_by fuel rl) q))
          (decreases rl)
  = match rl with
    | [] -> ()
    | o :: t ->
      (match fuel with
       | [] -> ()
       | _ :: fuel' ->
         let p = chain_head_rev mint actor q t in
         let n = { nid = mint p actor o; nparents = [ p ]; nop = o } in
         assert (mem n (chain_rev mint actor q rl));
         assert (lookup d.nodes n.nid == Found n);
         assert (resolves d (chain_rev mint actor q t));
         ancestors_chain d mint actor q t fuel')

(* The base node's closure is itself: it has no parent. F#: `ancestorsOf` of the genesis node,
   whose `Parents` is `[]` because `Dag.append` was given `""`. *)
let base_ancestors (#op:eqtype) (d:dag op) (bn:node op) (fuel:list (node op))
  : Lemma (requires lookup d.nodes bn.nid == Found bn /\ bn.nparents == [] /\ Cons? fuel)
          (ensures ancestors_of d fuel bn.nid == [ bn.nid ])
  = ()

(* ---- THE THEOREM (delta recovery): `between_chain` ---- *)

(* What the recovery needs of content addressing, per lane, and nothing more: every node of the
   lane's chain is the node the DAG holds under its own id (`resolves` — see
   `resolves_of_distinct`), the walk has fuel for the lane, and the base's id is not one of the
   lane's. All three are consequences of `Dag.nodeHash` being injective on
   (sorted parents, actor, encoded op) — the hash-collision assumption, taken as a premise. *)
let lane_recovers (#op:eqtype) (d:dag op) (mint:string -> string -> op -> string)
  (bn:node op) (fuel:list (node op)) (ln:lane op) : prop =
  resolves d (chain_rev mint ln.lactor bn.nid (rev ln.lops)) /\
  covers fuel (rev ln.lops) /\
  not (mem bn.nid (ids_of (lane_nodes mint ln.lactor bn.nid ln.lops)))

(* THEOREM. `Dag.between` over a linear lane off the base returns the lane's nodes, in order.
   The whole of the DAG layer beneath the fold — the ancestor closure of the head, the closure of
   the base, the topological order, the difference and the lookup — computed, and equal to the
   chain that was appended.

   Read the proof as a calculation, since that is what it is: the head's closure is the chain
   newest-first followed by the base (`ancestors_chain` + `base_ancestors`); reversing it puts
   the base first and the chain in append order (`rev_app`); the base's own closure is just
   itself, so the difference drops the base and nothing else (`diff_no_mem`, on the premise that
   no chain id IS the base id); and looking those ids back up returns the very nodes they name
   (`nodes_for_ids`, on the `resolves` premise). *)
#push-options "--z3rlimit 100"
let between_chain (#op:eqtype) (d:dag op) (mint:string -> string -> op -> string)
  (actor:string) (bn:node op) (l:list op) (fuel:list (node op))
  : Lemma (requires lookup d.nodes bn.nid == Found bn /\ bn.nparents == [] /\
                    lane_recovers d mint bn fuel ({ lactor = actor; lops = l }))
          (ensures between d fuel bn.nid (lane_head mint actor bn.nid l)
                   == lane_nodes mint actor bn.nid l)
  = let q = bn.nid in
    let rl = rev l in
    let c = chain_rev mint actor q rl in
    covers_drop fuel rl;
    ancestors_chain d mint actor q rl fuel;
    base_ancestors d bn (drop_by fuel rl);
    base_ancestors d bn fuel;
    rev_app (ids_of c) [ q ];
    ids_of_rev c;
    mem_rev q (ids_of c);
    diff_no_mem (rev (ids_of c)) q;
    nodes_for_ids d (rev c)
#pop-options

(* … and therefore `Dag.betweenOps` returns the lane's ops. This is the claim the ladder
   moves from level 2 to level 1 for the linear-lane shape. *)
#push-options "--z3rlimit 100"
let between_ops_chain (#op:eqtype) (d:dag op) (mint:string -> string -> op -> string)
  (actor:string) (bn:node op) (l:list op) (fuel:list (node op))
  : Lemma (requires lookup d.nodes bn.nid == Found bn /\ bn.nparents == [] /\
                    lane_recovers d mint bn fuel ({ lactor = actor; lops = l }))
          (ensures between_ops d fuel bn.nid (lane_head mint actor bn.nid l) == l)
  = between_chain d mint actor bn l fuel;
    ops_of_rev (chain_rev mint actor bn.nid (rev l));
    ops_of_chain_rev mint actor bn.nid (rev l);
    rev_rev l
#pop-options

(* ---- the fold, stated from the DAG (F#: `Dag.reconcileMany`, `FoldConfluence.foldOnce`) ---- *)

(* F#: `heads |> List.map (betweenOps dag baseId)` — the first line of `Dag.reconcileMany`. *)
let rec deltas_of (#op:eqtype) (d:dag op) (fuel:list (node op)) (base_id:string)
  (heads:list string) : Tot (list (list op)) =
  match heads with
  | [] -> []
  | h :: t -> between_ops d fuel base_id h :: deltas_of d fuel base_id t

(* F#: `Dag.reconcileMany` in full. Section 3's `reconcile_many` is this function from the point
   where the deltas are known; this is the same function from the point where the DAG is. *)
let reconcile_many_dag (#op:eqtype) (fp:op -> footprint) (d:dag op) (fuel:list (node op))
  (base_id:string) (heads:list string) : Tot (outcome (list op) (list (conflict op))) =
  reconcile_many fp (deltas_of d fuel base_id heads)

(* F#: `FoldConfluence.foldOnce` in full — from the DAG, not from the deltas. *)
let fold_once_dag (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint)
  (d:dag op) (fuel:list (node op)) (base_id:string) (s0:state) (heads:list string)
  : Tot (lane_outcome op state rej) =
  fold_once apply fp s0 (deltas_of d fuel base_id heads)

let rec lanes_recover (#op:eqtype) (d:dag op) (mint:string -> string -> op -> string)
  (bn:node op) (fuel:list (node op)) (lanes:list (lane op)) : Tot prop (decreases lanes) =
  match lanes with
  | [] -> True
  | ln :: t -> lane_recovers d mint bn fuel ln /\ lanes_recover d mint bn fuel t

(* Every lane's delta comes back, so the DAG's delta list IS the lane list. *)
let rec deltas_of_lanes (#op:eqtype) (d:dag op) (mint:string -> string -> op -> string)
  (bn:node op) (fuel:list (node op)) (lanes:list (lane op))
  : Lemma (requires lookup d.nodes bn.nid == Found bn /\ bn.nparents == [] /\
                    lanes_recover d mint bn fuel lanes)
          (ensures deltas_of d fuel bn.nid (lane_heads mint bn.nid lanes) == lane_ops lanes)
          (decreases lanes)
  = match lanes with
    | [] -> ()
    | ln :: t ->
      between_ops_chain d mint ln.lactor bn ln.lops fuel;
      deltas_of_lanes d mint bn fuel t

(* THEOREM. The fold over the DAG is the fold over the lane lists — `Dag.reconcileMany` from the
   DAG equals section 3's `reconcile_many` on the lanes that were appended. This is what lets the
   confluence theorem be stated from the DAG rather than from the deltas. *)
let reconcile_many_dag_eq (#op:eqtype) (fp:op -> footprint) (d:dag op)
  (mint:string -> string -> op -> string) (bn:node op) (fuel:list (node op))
  (lanes:list (lane op))
  : Lemma (requires lookup d.nodes bn.nid == Found bn /\ bn.nparents == [] /\
                    lanes_recover d mint bn fuel lanes)
          (ensures reconcile_many_dag fp d fuel bn.nid (lane_heads mint bn.nid lanes)
                   == reconcile_many fp (lane_ops lanes))
  = deltas_of_lanes d mint bn fuel lanes

let fold_once_dag_eq (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint) (s0:state)
  (d:dag op) (mint:string -> string -> op -> string) (bn:node op) (fuel:list (node op))
  (lanes:list (lane op))
  : Lemma (requires lookup d.nodes bn.nid == Found bn /\ bn.nparents == [] /\
                    lanes_recover d mint bn fuel lanes)
          (ensures fold_once_dag apply fp d fuel bn.nid s0 (lane_heads mint bn.nid lanes)
                   == fold_once apply fp s0 (lane_ops lanes))
  = deltas_of_lanes d mint bn fuel lanes

(* An arrival order of the HEADS is an arrival order of the deltas they recover. *)
[@@ noextract_to "FSharp"]  (* proof-only: the oracle never needs a permutation witness *)
let rec perm_deltas (#op:eqtype) (d:dag op) (fuel:list (node op)) (base_id:string)
  (hs1 hs2:list string) (p:perm string hs1 hs2)
  : Tot (perm (list op) (deltas_of d fuel base_id hs1) (deltas_of d fuel base_id hs2))
        (decreases p) =
  match p with
  | PNil -> PNil
  | PSkip x m1 m2 p' ->
    PSkip (between_ops d fuel base_id x) _ _ (perm_deltas d fuel base_id m1 m2 p')
  | PSwap x y l ->
    PSwap (between_ops d fuel base_id x) (between_ops d fuel base_id y) (deltas_of d fuel base_id l)
  | PTrans m1 m2 m3 p12 p23 ->
    PTrans _ _ _ (perm_deltas d fuel base_id m1 m2 p12) (perm_deltas d fuel base_id m2 m3 p23)

(* THEOREM. Fold confluence, stated from the DAG: the same heads over the same base fold to the
   same state — or halt with the same canonical report — however the heads arrive. Section 10's
   theorem is about lane deltas someone had already recovered; this one starts where production
   starts, at the content-addressed DAG, and recovers them. *)
val fold_confluence_dag (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint) (s0:state)
  (d:dag op) (fuel:list (node op)) (base_id:string)
  (hs1 hs2:list string) (p:perm string hs1 hs2)
  : Lemma (requires independence_diamond fp apply /\
                    lanes_apply apply (deltas_of d fuel base_id hs1) s0)
          (ensures outcome_equiv (fold_once_dag apply fp d fuel base_id s0 hs1)
                                 (fold_once_dag apply fp d fuel base_id s0 hs2))

let fold_confluence_dag #op #state #rej apply fp s0 d fuel base_id hs1 hs2 p =
  fold_confluence_total apply fp s0
    (deltas_of d fuel base_id hs1) (deltas_of d fuel base_id hs2)
    (perm_deltas d fuel base_id hs1 hs2 p)

(* ======================================================================================
   13. The Kahn drain over an ABSTRACT DAG (Phase 156).

   The spine case (Phase 142: 13.0 and 13.12 below) proves the order on a SPINE, which is the
   shape one writer's lane has. The shape a clone actually folds is the UNION of N lanes off a
   shared base, and there the ready frontier is N wide at its first step — so the tie-break the
   spine case could leave as an unexercised parameter is what decides the sequence, and "every clone computes the same order from the same node set"
   rests on it. This section is that sentence, mechanised, over an abstract node set rather than
   over the base-plus-N-chains shape: no `mint`, no lanes, no base — just nodes naming parents.

   THE DRAIN. `drain` is 13.0's own `kahn` at the selector `pick_min lt` — the smallest id
   in the ready frontier — wrapped by the dangling-parent policy below. It is the same function
   and not a second one, deliberately: a re-modelled drain would have to be re-related to the one
   the spine results (13.12) are about, and the relating is the part that goes wrong.

   THE ID ORDER IS A PARAMETER, and that is the stronger statement rather than a weaker one.
   Production compares ids with `List.sort`, which on strings is F#'s structural comparison and so
   `String.CompareOrdinal`. The model takes any `lt` satisfying `total_order` — irreflexive,
   transitive, trichotomous — and every result below holds for all of them. What the determinism
   claim needs is that all clones use the SAME order, never that the order is any particular one;
   quantifying over total orders says exactly that, and says it without the module acquiring the
   character arithmetic a concrete string comparison would need (README, finding 2). The
   differential host instantiates `lt` at ordinal comparison, which is what production's sort is.

   THE DANGLING-PARENT POLICY IS A PARAMETER, because the two production call sites differ on it
   and a theorem about "the drain" that did not say which would be a theorem about neither:
     - `IgnoreDangling` — F#: `Dag.drainClosure`'s closure walk (`match Map.tryFind id dag.Nodes with
       | Some n -> … | None -> collect acc rest`) with `Dag.ancestorsOf`'s `ContainsKey` guard
       beside it, and the `parentsIn` filter that follows. A parent the node set does not hold
       never enters the closure and is filtered out of the in-degree, so it constrains nothing and
       the drain proceeds. This is the policy on the FOLD path — `drainClosure` -> `between` ->
       `betweenOps` -> `reconcileMany` -> `foldOnce` — and on `replayTo`.
     - `RefuseDangling` — F#: `Dag.firstBreak` (`n.Parents |> List.tryFind (fun p -> not
       (dag.Nodes.ContainsKey p))` -> `MissingParent`), hence `verifyDag` and `fromJsonlVerified`,
       which refuse the whole set before any drain runs.
   `drain_policies_agree` proves the two are the same function on a set with no dangling parent, so
   the parameter costs the fold path nothing; `drain_refusal_characterised` proves the refusal
   fires exactly when a parent lies outside.

   THE THREE THEOREMS.
     - `drain_linear_extension` — every node the drain places is placed after every one of its
       parents that is inside the set. Stated over the nodes it PLACED rather than over all of
       them, because on a cyclic set it places only some, and a statement quantified over all of
       them would be false there rather than silent.
     - `drain_total_on_acyclic` — on an acyclic set the drain places every node exactly once, and
       what it produces is itself a topological enumeration.
     - `drain_deterministic` — the sequence is a function of the node SET: for every permutation of
       the node list (F#: every order the lanes arrived in), the same list comes back. This is
       where the tie-break does the work. `frontier` returns its answers in the work list's order,
       so a permuted node set hands the selector a permuted frontier; `pick_min` is invariant under
       that and 13.0's `pick_head` is not. A drain taking the head of an unsorted frontier
       satisfies `picks_from_frontier` and FAILS this theorem, which is the precise sense in which
       smallest-id-first is load-bearing rather than decoration.

   HOW A CYCLE IS SURFACED, which is a claim about production and worth stating exactly.
   `drainClosure` does not raise: a node inside a cycle never reaches in-degree zero, so the emitted
   list is strictly SHORTER than the closure, and `isAcyclic` / `tryTopoOrder` read that length
   comparison and surface it while `replayTo` and `between` fold the truncated prefix. The model
   says the same thing without lengths: `drain_total_on_acyclic` gives completeness from
   acyclicity, and `drain_complete_is_acyclic` reads the converse off the linear-extension theorem
   — a complete drain IS a topological enumeration, so it witnesses acyclicity. The two are an
   iff, which is exactly the claim `isAcyclic`'s comparison makes.

   ACYCLICITY IS "A TOPOLOGICAL ENUMERATION EXISTS", supplied as a WITNESS LIST rather than as an
   existential or as the absence of a self-reachable node. It is the standard characterisation of
   a finite acyclic digraph, it is not circular (the witness is any such list, never the drain's
   own output), and taking it as a parameter keeps these statements in the first-order fragment
   this module stays inside. `drain_complete_is_acyclic` is what makes it non-vacuous in the
   direction that matters: the drain's own output is such a witness whenever it is complete.

   WHAT IS NOT CLAIMED. That any particular id ordering is the one production uses — `lt` is a
   parameter, and the differential is what ties it to `String.CompareOrdinal`. The DIAGNOSTIC
   payload of a refusal: `Dag.firstBreak` reports the missing parent beside the node and the model
   reports only the node, because the node determines the parent (`first_outside` of its own
   parent list) and the model's subject is the ORDER. `Dag.mergeBase`, still. And any consumer's
   instantiation of this theorem for its own total order, which is that consumer's work and not
   this module's.
   (`Dag.mergeBase`, and the delta recovery over a merged head that this drain is the order
   FOR, are section 14 — Phase 158.)

   NO SMT PATTERNS, for section 11's reason: `rev`, `app`, `ids_of`, `remove_first` and
   `remove_id` rewrite into one another, and left to fire on their own they turn these queries
   into ones Z3 does not return from. Every lemma below is called by name.
   ====================================================================================== *)

(* ---- 13.0 what a topological order IS on a spine, and the frontier drain at EVERY selector
   (Phase 142; until Phase 534 the first half of section 12) ---- *)

(* `x` occurs in `l`, and `y` occurs strictly after it. F#: what "parents before children" means of
   the list `drainClosure` places — a comparison of positions, spelled without positions so the module
   stays free of the integer arithmetic the rest of it avoids. *)
let rec before (x y:string) (l:list string) : Tot bool =
  match l with
  | [] -> false
  | h :: t -> if h = x then mem y t
              else if h = y then false
              else before x y t

(* A SPINE hanging off `q`: the nodes in APPEND order, each naming the one before it. F#: exactly
   what the per-lane `List.fold` over `Dag.append` in `foldOnce` builds, one node at a time, off
   `baseId` — and, by `parents_chain_lane` below, what `lane_nodes` is. *)
let rec parents_chain (#op:eqtype) (ns:list (node op)) (q:string) : Tot bool =
  match ns with
  | [] -> true
  | n :: t -> n.nparents = [ q ] && parents_chain t n.nid

(* Every parent of a node precedes it. Stated over the node's whole parent LIST rather than over a
   single parent, so the property is the general one and single-parenthood comes from
   `parents_chain` where it is needed. F#: the property `drainClosure`'s placed order has by
   construction — a node is emitted only once its in-degree over the closure has reached zero, and
   the in-degree counts precisely the parents inside the closure that are still unemitted. *)
let rec all_before (ps:list string) (child:string) (ord:list string) : Tot bool =
  match ps with
  | [] -> true
  | p :: t -> before p child ord && all_before t child ord

let rec follows_parents (#op:eqtype) (ns:list (node op)) (ord:list string) : Tot bool =
  match ns with
  | [] -> true
  | n :: t -> all_before n.nparents n.nid ord && follows_parents t ord

(* The same property read off the SPINE rather than off the nodes: `q` before the first id, each id
   before the next. Equivalent to `follows_parents` on a `parents_chain` (both directions below),
   and the form the induction is on, because it is the shape of the thing being enumerated. *)
let rec follows_spine (root:string) (ids:list string) (ord:list string)
  : Tot bool (decreases ids) =
  match ids with
  | [] -> true
  | x :: t -> before root x ord && follows_spine x t ord

let rec follows_parents_spine (#op:eqtype) (ns:list (node op)) (q:string) (ord:list string)
  : Lemma (requires parents_chain ns q /\ follows_parents ns ord)
          (ensures follows_spine q (ids_of ns) ord)
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: t -> follows_parents_spine t n.nid ord

let rec follows_spine_parents (#op:eqtype) (ns:list (node op)) (q:string) (ord:list string)
  : Lemma (requires parents_chain ns q /\ follows_spine q (ids_of ns) ord)
          (ensures follows_parents ns ord)
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: t -> follows_spine_parents t n.nid ord


(* F#: `parentsIn id` — a node's parents that lie INSIDE the closure. Everything outside is
   invisible to the drain, exactly as `List.filter (fun p -> Set.contains p anc)` makes it. *)
let rec parents_in (ps:list string) (closure:list string) : Tot (list string) =
  match ps with
  | [] -> []
  | p :: t -> if mem p closure then p :: parents_in t closure else parents_in t closure

(* F#: `indeg.[id] = 0`. Production decrements a counter as each node is emitted; the model asks
   the equivalent question of the emitted list, which is what that counter counts. *)
let rec all_emitted (ps:list string) (emitted:list string) : Tot bool =
  match ps with
  | [] -> true
  | p :: t -> mem p emitted && all_emitted t emitted

(* F#: `ready` — the nodes whose every in-closure parent has been emitted. Production maintains it
   incrementally and keeps it sorted; the model recomputes it, which is the same set. *)
let rec frontier (#op:eqtype) (rest:list (node op)) (closure:list string) (emitted:list string)
  : Tot (list string) =
  match rest with
  | [] -> []
  | n :: t ->
    if all_emitted (parents_in n.nparents closure) emitted
    then n.nid :: frontier t closure emitted
    else frontier t closure emitted

(* F#: dropping the emitted id from the work set. *)
let rec remove_id (#op:eqtype) (ns:list (node op)) (id:string) : Tot (list (node op)) =
  match ns with
  | [] -> []
  | n :: t -> if n.nid = id then t else n :: remove_id t id

(* ALL the model asks of a tie-break: it returns a member of the frontier it is handed. That is
   what `List.head` of a non-empty sorted list does, and it is deliberately everything — nothing
   below mentions how ids compare, so `kahn` is quantified over EVERY tie-break rather than
   modelling production's. On a spine that is not a weakening: the frontier is a one-element list
   at every step (`kahn_frontier_singleton`), so all of them agree. *)
let picks_from_frontier (pick:list string -> string) : prop =
  forall (l:list string). Cons? l ==> mem (pick l) l

(* A witness that `picks_from_frontier` is satisfiable at all, so no theorem below is vacuously
   true of a hypothesis nothing meets: taking the head, which is exactly what production does once
   its ready list is sorted. It is not the smallest-id selector and is not meant to be — the point
   is that the results hold for every selector, and this exhibits one. *)
let pick_head (l:list string) : Tot string =
  match l with
  | [] -> ""
  | h :: _ -> h

let pick_head_is_a_tie_break () : Lemma (picks_from_frontier pick_head) = ()

(* F#: the `while not (List.isEmpty ready)` loop. Fuel is a list, one step per element, in the
   idiom `ancestors_of` already uses — production's loop terminates because each iteration emits a
   node and never re-adds one. *)
let rec kahn (#op:eqtype) (pick:list string -> string) (fuel:list (node op))
  (rest:list (node op)) (closure:list string) (emitted:list string)
  : Tot (list string) (decreases fuel) =
  match fuel with
  | [] -> []
  | _ :: fuel' ->
    (match frontier rest closure emitted with
     | [] -> []
     | f ->
       let id = pick f in
       id :: kahn pick fuel' (remove_id rest id) closure (app emitted [ id ]))


(* ---- 13.1 the id order, as a parameter ---- *)

(* F#: what `List.sort` gives a string list — ordinal comparison, a strict total order. Spelled as
   three properties of an abstract `lt` rather than as a comparison, so nothing here depends on how
   strings compare and the extracted oracle stays free of a character algebra. *)
let total_order (lt:string -> string -> bool) : prop =
  (forall (x:string). not (lt x x)) /\
  (forall (x y z:string). lt x y /\ lt y z ==> lt x z) /\
  (forall (x y:string). x =!= y ==> (lt x y \/ lt y x))

(* F#: `List.head (List.sort ready)` — the smallest id in the ready frontier, computed as a fold
   because the model needs the minimum and not the sorted list. *)
let rec pick_min (lt:string -> string -> bool) (l:list string) : Tot string =
  match l with
  | [] -> ""
  | [ x ] -> x
  | x :: t -> let m = pick_min lt t in if lt x m then x else m

let rec pick_min_mem (lt:string -> string -> bool) (l:list string)
  : Lemma (requires Cons? l) (ensures mem (pick_min lt l) l) (decreases l)
  = match l with
    | [] -> ()
    | [ _ ] -> ()
    | _ :: t -> pick_min_mem lt t

let rec pick_min_least (lt:string -> string -> bool) (l:list string) (y:string)
  : Lemma (requires total_order lt /\ mem y l)
          (ensures pick_min lt l == y \/ lt (pick_min lt l) y)
          (decreases l)
  = match l with
    | [] -> ()
    | [ _ ] -> ()
    | x :: t ->
      pick_min_mem lt t;
      let m = pick_min lt t in
      if y = x then assert (x == m \/ lt x m \/ lt m x)
      else begin
        pick_min_least lt t y;
        assert (lt x m /\ lt m y ==> lt x y)
      end

(* THE TIE-BREAK IS ORDER-INVARIANT, which is the whole of why the drain is deterministic: two
   frontiers holding the same ids in different orders have the same minimum. `pick_head` does not
   have this property, and the spine case did not need it — on a spine the frontier is one element
   wide at every step. *)
let pick_min_same (lt:string -> string -> bool) (l1 l2:list string)
  : Lemma (requires total_order lt /\ Cons? l1 /\ Cons? l2 /\
                    (forall (x:string). mem x l1 == mem x l2))
          (ensures pick_min lt l1 == pick_min lt l2)
  = pick_min_mem lt l1;
    pick_min_mem lt l2;
    pick_min_least lt l1 (pick_min lt l2);
    pick_min_least lt l2 (pick_min lt l1)

(* … and it is a tie-break in 13.0's sense, so every result there holds of it. *)
let pick_min_is_a_tie_break (lt:string -> string -> bool)
  : Lemma (ensures picks_from_frontier (pick_min lt))
  = let aux (l:list string) : Lemma (Cons? l ==> mem (pick_min lt l) l) =
      if Cons? l then pick_min_mem lt l else ()
    in
    FStar.Classical.forall_intro aux

(* ---- 13.2 the list algebra the drain's step needs ---- *)

(* Dropping one id. F#: what removing an emitted node from the work set does to any list that
   holds it once. *)
let rec remove_first (e:string) (l:list string) : Tot (list string) =
  match l with
  | [] -> []
  | x :: t -> if x = e then t else x :: remove_first e t

let rec mem_remove_first (l:list string) (e x:string)
  : Lemma (requires distinct l)
          (ensures mem x (remove_first e l) == (mem x l && not (x = e)))
          (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> mem_remove_first t e x

let rec distinct_remove_first (l:list string) (e:string)
  : Lemma (requires distinct l) (ensures distinct (remove_first e l)) (decreases l)
  = match l with
    | [] -> ()
    | h :: t ->
      distinct_remove_first t e;
      mem_remove_first t e h

(* `covers` forces a non-empty fuel whatever the list — its base case asks for one step past the
   end. Pulled out because every drain lemma below needs it before it can case on the fuel. *)
let covers_cons_fuel (#a #b:Type) (fuel:list a) (l:list b)
  : Lemma (requires covers fuel l) (ensures Cons? fuel)
  = match l with
    | [] -> ()
    | _ :: _ -> ()

let rec covers_remove_first (#a:Type) (fuel:list a) (l:list string) (e:string)
  : Lemma (requires covers fuel l /\ mem e l)
          (ensures Cons? fuel /\ covers (Cons?.tl fuel) (remove_first e l))
          (decreases l)
  = match l with
    | [] -> ()
    | x :: t ->
      (match fuel with
       | [] -> ()
       | _ :: f -> if x = e then () else covers_remove_first f t e)

(* `remove_id` on the nodes is `remove_first` on their ids — the bridge that lets every list lemma
   above serve the node list. Needs no distinctness: both functions drop the first match. *)
let rec ids_of_remove_id (#op:eqtype) (ns:list (node op)) (id:string)
  : Lemma (ensures ids_of (remove_id ns id) == remove_first id (ids_of ns)) (decreases ns)
  = match ns with
    | [] -> ()
    | n :: t -> if n.nid = id then () else ids_of_remove_id t id

let rec mem_remove_id (#op:eqtype) (ns:list (node op)) (id:string) (n:node op)
  : Lemma (requires distinct (ids_of ns))
          (ensures mem n (remove_id ns id) == (mem n ns && not (n.nid = id)))
          (decreases ns)
  = match ns with
    | [] -> ()
    | _ :: t ->
      mem_ids_of n t;
      mem_remove_id t id n

let rec remove_id_not_id (#op:eqtype) (ns:list (node op)) (id:string) (n:node op)
  : Lemma (requires distinct (ids_of ns) /\ mem n (remove_id ns id)) (ensures n.nid =!= id)
          (decreases ns)
  = match ns with
    | [] -> ()
    | m :: t ->
      if m.nid = id then mem_ids_of n t
      else if m = n then ()
      else remove_id_not_id t id n

let rec lookup_of_mem_ids (#op:eqtype) (ns:list (node op)) (id:string)
  : Lemma (requires mem id (ids_of ns))
          (ensures Found? (lookup ns id) /\ (Found?._0 (lookup ns id)).nid == id /\
                   mem (Found?._0 (lookup ns id)) ns)
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: t -> if n.nid = id then () else lookup_of_mem_ids t id

(* Two node lists with the same members have the same ids — one direction is `mem_ids_of`, the
   other needs a node to point at and `lookup` is what produces one without an existential. *)
let ids_same_of_nodes_same (#op:eqtype) (r1 r2:list (node op)) (y:string)
  : Lemma (requires forall (n:node op). mem n r1 == mem n r2)
          (ensures mem y (ids_of r1) == mem y (ids_of r2))
  = (if mem y (ids_of r1) then begin
       lookup_of_mem_ids r1 y;
       mem_ids_of (Found?._0 (lookup r1 y)) r2
     end
     else ());
    (if mem y (ids_of r2) then begin
       lookup_of_mem_ids r2 y;
       mem_ids_of (Found?._0 (lookup r2 y)) r1
     end
     else ())

(* ---- 13.3 what a topological enumeration IS, over an abstract set ---- *)

(* 13.0's `all_before` weakened by an ALREADY-PLACED set: a parent is satisfied either by
   having been emitted before this call began, or by standing earlier in the order. The weakening
   is what lets one induction carry the drain's own accumulator. *)
let rec all_before_or_emitted (ps:list string) (child:string) (emitted ord:list string) : Tot bool =
  match ps with
  | [] -> true
  | p :: t -> (mem p emitted || before p child ord) && all_before_or_emitted t child emitted ord

let rec follows_parents_or_emitted (#op:eqtype) (ns:list (node op)) (closure emitted ord:list string)
  : Tot bool =
  match ns with
  | [] -> true
  | n :: t -> all_before_or_emitted (parents_in n.nparents closure) n.nid emitted ord
              && follows_parents_or_emitted t closure emitted ord

(* Every node's IN-SET parents precede it — 13.0's `follows_parents` with `parents_in` in
   place of the whole parent list, which is the `IgnoreDangling` reading made explicit. *)
let follows_parents_in (#op:eqtype) (ns:list (node op)) (closure ord:list string) : Tot bool =
  follows_parents_or_emitted ns closure [] ord

(* The same, read only of the nodes the drain PLACED. On a cyclic set the drain places a prefix
   and says nothing about the rest, so this is the honest shape of the linear-extension claim. *)
let rec placed_follow_parents (#op:eqtype) (ns:list (node op)) (closure emitted ord:list string)
  : Tot bool =
  match ns with
  | [] -> true
  | n :: t ->
    (if mem n.nid ord
     then all_before_or_emitted (parents_in n.nparents closure) n.nid emitted ord
     else true)
    && placed_follow_parents t closure emitted ord

let rec sub_ids (l m:list string) : Tot bool =
  match l with
  | [] -> true
  | x :: t -> mem x m && sub_ids t m

let same_ids (l m:list string) : Tot bool = sub_ids l m && sub_ids m l

let rec sub_ids_mem (l m:list string) (x:string)
  : Lemma (requires sub_ids l m /\ mem x l) (ensures mem x m) (decreases l)
  = match l with
    | [] -> ()
    | h :: t -> if x = h then () else sub_ids_mem t m x

let rec sub_ids_of_mem (l m:list string)
  : Lemma (requires forall (x:string). mem x l ==> mem x m) (ensures sub_ids l m) (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> sub_ids_of_mem t m

(* THE ACYCLICITY WITNESS. A distinct enumeration of exactly this set's ids in which every node's
   in-set parents precede it. A finite digraph admits one iff it is acyclic; taking it as a
   parameter rather than asserting an existential keeps every statement below first-order, and
   `drain_complete_is_acyclic` produces one, so the hypothesis is never vacuous. *)
let is_topo_enum (#op:eqtype) (ns:list (node op)) (ord:list string) : Tot bool =
  distinct ord && same_ids ord (ids_of ns) && follows_parents_in ns (ids_of ns) ord

(* ---- 13.4 the dangling-parent policy, and the drain ---- *)

let rec first_outside (ps:list string) (closure:list string) : Tot (found string) =
  match ps with
  | [] -> Missing
  | p :: t -> if mem p closure then first_outside t closure else Found p

(* F#: the nodes `Dag.firstBreak` would fault with `MissingParent`. Its answer does not depend on
   insertion order — it scanned `Map.toList`, in id order, until Phase 311, and scans the whole
   DAG's drain since — which is why the refusal below names an ORDER-INVARIANT choice, the SMALLEST
   such id, rather than the first in the work list, and why `drain_deterministic` can cover the
   refusing policy too. Which node production names among several is not modelled: the subject
   here is that the refusal fires, and fires the same under every arrival order. *)
let rec dangling_ids (#op:eqtype) (ns:list (node op)) (closure:list string) : Tot (list string) =
  match ns with
  | [] -> []
  | n :: t ->
    (match first_outside n.nparents closure with
     | Missing -> dangling_ids t closure
     | Found _ -> n.nid :: dangling_ids t closure)

type dangling_policy =
  | IgnoreDangling
  | RefuseDangling

type drain_result =
  | Drained : list string -> drain_result
  | Refused : string -> drain_result

(* F#: `drainClosure`'s placed order at the smallest-id tie-break, over the set as its own closure. *)
let drain_order (#op:eqtype) (lt:string -> string -> bool) (fuel:list (node op))
  (ns:list (node op)) : Tot (list string) =
  kahn (pick_min lt) fuel ns (ids_of ns) []

let drain (#op:eqtype) (policy:dangling_policy) (lt:string -> string -> bool)
  (fuel:list (node op)) (ns:list (node op)) : Tot drain_result =
  match policy with
  | IgnoreDangling -> Drained (drain_order lt fuel ns)
  | RefuseDangling ->
    (match dangling_ids ns (ids_of ns) with
     | [] -> Drained (drain_order lt fuel ns)
     | ds -> Refused (pick_min lt ds))

(* ---- 13.5 the frontier, read as a set ---- *)

let rec frontier_sub (#op:eqtype) (rest:list (node op)) (closure emitted:list string) (id:string)
  : Lemma (requires mem id (frontier rest closure emitted)) (ensures mem id (ids_of rest))
          (decreases rest)
  = match rest with
    | [] -> ()
    | n :: t -> if n.nid = id then () else frontier_sub t closure emitted id

let rec frontier_contains (#op:eqtype) (rest:list (node op)) (closure emitted:list string)
  (n:node op)
  : Lemma (requires mem n rest /\ all_emitted (parents_in n.nparents closure) emitted)
          (ensures mem n.nid (frontier rest closure emitted))
          (decreases rest)
  = match rest with
    | [] -> ()
    | m :: t -> if m = n then () else frontier_contains t closure emitted n

(* A frontier entry came from a node that is READY — the converse of `frontier_contains`, and the
   only place this section consumes the id-distinctness premise on the drain's own path. *)
let rec frontier_ready (#op:eqtype) (rest:list (node op)) (closure emitted:list string) (n:node op)
  : Lemma (requires distinct (ids_of rest) /\ mem n rest /\
                    mem n.nid (frontier rest closure emitted))
          (ensures all_emitted (parents_in n.nparents closure) emitted)
          (decreases rest)
  = match rest with
    | [] -> ()
    | m :: t ->
      mem_ids_of n t;
      if m = n then begin
        if all_emitted (parents_in m.nparents closure) emitted then ()
        else frontier_sub t closure emitted n.nid
      end
      else frontier_ready t closure emitted n

(* The frontier is a function of the node SET: the readiness test reads one node and the ambient
   closure and emitted list, never the work list's order. *)
let rec frontier_transfer (#op:eqtype) (r1 r2:list (node op)) (closure emitted:list string)
  (id:string)
  : Lemma (requires (forall (n:node op). mem n r1 ==> mem n r2) /\
                    mem id (frontier r1 closure emitted))
          (ensures mem id (frontier r2 closure emitted))
          (decreases r1)
  = match r1 with
    | [] -> ()
    | n :: t ->
      if all_emitted (parents_in n.nparents closure) emitted && n.nid = id
      then frontier_contains r2 closure emitted n
      else frontier_transfer t r2 closure emitted id

(* … and of the closure only through its MEMBERS, which is what lets the determinism theorem below
   compare two permutations whose own `ids_of` lists are permutations too. *)
let rec parents_in_same (ps c1 c2:list string)
  : Lemma (requires forall (y:string). mem y c1 == mem y c2)
          (ensures parents_in ps c1 == parents_in ps c2)
          (decreases ps)
  = match ps with
    | [] -> ()
    | _ :: t -> parents_in_same t c1 c2

let rec frontier_same_closure (#op:eqtype) (rest:list (node op)) (c1 c2 emitted:list string)
  : Lemma (requires forall (y:string). mem y c1 == mem y c2)
          (ensures frontier rest c1 emitted == frontier rest c2 emitted)
          (decreases rest)
  = match rest with
    | [] -> ()
    | n :: t ->
      parents_in_same n.nparents c1 c2;
      frontier_same_closure t c1 c2 emitted

(* ---- 13.6 the step lemmas ---- *)

let rec all_before_or_emitted_none (ps:list string) (child:string) (emitted ord:list string)
  : Lemma (requires all_before_or_emitted ps child emitted ord /\
                    (forall (p:string). before p child ord == false))
          (ensures all_emitted ps emitted)
          (decreases ps)
  = match ps with
    | [] -> ()
    | _ :: t -> all_before_or_emitted_none t child emitted ord

let rec all_emitted_weakens (ps:list string) (child:string) (emitted ord:list string)
  : Lemma (requires all_emitted ps emitted)
          (ensures all_before_or_emitted ps child emitted ord)
          (decreases ps)
  = match ps with
    | [] -> ()
    | _ :: t -> all_emitted_weakens t child emitted ord

(* Nothing precedes the head of a distinct list. The force of the whole totality argument: the
   witness enumeration's head names a node no unemitted in-set parent blocks, so the frontier of a
   non-empty work set is never empty. *)
let before_nothing_at_head (p h:string) (wt:list string)
  : Lemma (requires distinct (h :: wt)) (ensures before p h (h :: wt) == false)
  = assert (not (mem h wt))

let rec before_mem_snd (x y:string) (l:list string)
  : Lemma (requires before x y l) (ensures mem y l) (decreases l)
  = match l with
    | [] -> ()
    | h :: t -> if h = x then () else if h = y then () else before_mem_snd x y t

let before_cons (x y h:string) (l:list string)
  : Lemma (requires before x y l /\ h =!= y) (ensures before x y (h :: l))
  = before_mem_snd x y l

let before_head (h y:string) (t:list string)
  : Lemma (requires mem y t) (ensures before h y (h :: t))
  = ()

let rec before_remove_first (x y e:string) (l:list string)
  : Lemma (requires before x y l /\ x =!= e /\ y =!= e /\ distinct l)
          (ensures before x y (remove_first e l))
          (decreases l)
  = match l with
    | [] -> ()
    | h :: t ->
      if h = x then mem_remove_first t e y
      else if h = y then ()
      else if h = e then ()
      else before_remove_first x y e t

let rec all_before_or_emitted_step (ps:list string) (child id:string) (emitted w:list string)
  : Lemma (requires all_before_or_emitted ps child emitted w /\ distinct w /\ child =!= id)
          (ensures all_before_or_emitted ps child (app emitted [ id ]) (remove_first id w))
          (decreases ps)
  = match ps with
    | [] -> ()
    | p :: t ->
      mem_app p emitted [ id ];
      (if p = id then ()
       else if mem p emitted then ()
       else before_remove_first p child id w);
      all_before_or_emitted_step t child id emitted w

let rec follows_remove_id (#op:eqtype) (ns:list (node op)) (closure emitted w:list string)
  (id:string)
  : Lemma (requires follows_parents_or_emitted ns closure emitted w)
          (ensures follows_parents_or_emitted (remove_id ns id) closure emitted w)
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: t -> if n.nid = id then () else follows_remove_id t closure emitted w id

let rec follows_step (#op:eqtype) (sub:list (node op)) (closure emitted w:list string) (id:string)
  : Lemma (requires follows_parents_or_emitted sub closure emitted w /\ distinct w /\
                    (forall (n:node op). mem n sub ==> n.nid =!= id))
          (ensures follows_parents_or_emitted sub closure (app emitted [ id ]) (remove_first id w))
          (decreases sub)
  = match sub with
    | [] -> ()
    | n :: t ->
      all_before_or_emitted_step (parents_in n.nparents closure) n.nid id emitted w;
      follows_step t closure emitted w id

let rec follows_mem (#op:eqtype) (ns:list (node op)) (closure emitted w:list string) (n:node op)
  : Lemma (requires follows_parents_or_emitted ns closure emitted w /\ mem n ns)
          (ensures all_before_or_emitted (parents_in n.nparents closure) n.nid emitted w)
          (decreases ns)
  = match ns with
    | [] -> ()
    | m :: t -> if m = n then () else follows_mem t closure emitted w n

(* ---- 13.7 the drain places a node AFTER ITS PARENTS ---- *)

let rec placed_follow_nil (#op:eqtype) (ns:list (node op)) (closure emitted:list string)
  : Lemma (ensures placed_follow_parents ns closure emitted []) (decreases ns)
  = match ns with
    | [] -> ()
    | _ :: t -> placed_follow_nil t closure emitted

let rec all_before_or_emitted_uncons (ps:list string) (child id:string) (emitted out:list string)
  : Lemma (requires all_before_or_emitted ps child (app emitted [ id ]) out /\ child =!= id /\
                    mem child out)
          (ensures all_before_or_emitted ps child emitted (id :: out))
          (decreases ps)
  = match ps with
    | [] -> ()
    | p :: t ->
      mem_app p emitted [ id ];
      (if mem p emitted then ()
       else if p = id then before_head id child out
       else before_cons p child id out);
      all_before_or_emitted_uncons t child id emitted out

let rec placed_follow_cons (#op:eqtype) (ns:list (node op)) (closure emitted:list string)
  (id:string) (out:list string)
  : Lemma (requires placed_follow_parents ns closure (app emitted [ id ]) out /\
                    (forall (n:node op). mem n ns ==> n.nid =!= id))
          (ensures placed_follow_parents ns closure emitted (id :: out))
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: t ->
      (if mem n.nid out
       then all_before_or_emitted_uncons (parents_in n.nparents closure) n.nid id emitted out
       else ());
      placed_follow_cons t closure emitted id out

(* The node just placed satisfies the claim because it was READY; every other node of `ns` is a
   node of `remove_id ns id` and satisfies it already. Factored out so the induction below reads
   as the step it is. *)
let rec placed_follow_step (#op:eqtype) (ns:list (node op)) (closure emitted:list string)
  (id:string) (out:list string) (nh:node op)
  : Lemma (requires placed_follow_parents (remove_id ns id) closure emitted (id :: out) /\
                    distinct (ids_of ns) /\ nh.nid == id /\ mem nh ns /\
                    all_emitted (parents_in nh.nparents closure) emitted)
          (ensures placed_follow_parents ns closure emitted (id :: out))
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: t ->
      mem_ids_of nh t;
      if n.nid = id then
        all_emitted_weakens (parents_in n.nparents closure) n.nid emitted (id :: out)
      else placed_follow_step t closure emitted id out nh

#push-options "--z3rlimit 200 --fuel 2 --ifuel 2"
let rec kahn_extends (#op:eqtype) (pick:list string -> string) (fuel:list (node op))
  (rest:list (node op)) (closure emitted:list string)
  : Lemma (requires picks_from_frontier pick /\ distinct (ids_of rest) /\
                    (forall (y:string). mem y (ids_of rest) ==> not (mem y emitted)))
          (ensures (let out = kahn pick fuel rest closure emitted in
                    distinct out /\
                    (forall (x:string). mem x out ==> mem x (ids_of rest)) /\
                    placed_follow_parents rest closure emitted out))
          (decreases fuel)
  = match fuel with
    | [] -> placed_follow_nil rest closure emitted
    | _ :: fuel' ->
      (match frontier rest closure emitted with
       | [] -> placed_follow_nil rest closure emitted
       | f ->
         let id = pick f in
         assert (mem id f);
         frontier_sub rest closure emitted id;
         let rest' = remove_id rest id in
         let emitted' = app emitted [ id ] in
         ids_of_remove_id rest id;
         distinct_remove_first (ids_of rest) id;
         let aux1 (y:string) : Lemma (mem y (ids_of rest') ==> not (mem y emitted')) =
           if mem y (ids_of rest') then begin
             mem_remove_first (ids_of rest) id y;
             mem_app y emitted [ id ]
           end
           else ()
         in
         FStar.Classical.forall_intro aux1;
         kahn_extends pick fuel' rest' closure emitted';
         let out' = kahn pick fuel' rest' closure emitted' in
         mem_remove_first (ids_of rest) id id;
         let aux2 (x:string) : Lemma (mem x out' ==> mem x (ids_of rest)) =
           if mem x out' then mem_remove_first (ids_of rest) id x else ()
         in
         FStar.Classical.forall_intro aux2;
         lookup_of_mem_ids rest id;
         let nh = Found?._0 (lookup rest id) in
         frontier_ready rest closure emitted nh;
         let aux3 (n:node op) : Lemma (mem n rest' ==> n.nid =!= id) =
           if mem n rest' then remove_id_not_id rest id n else ()
         in
         FStar.Classical.forall_intro aux3;
         placed_follow_cons rest' closure emitted id out';
         placed_follow_step rest closure emitted id out' nh)
#pop-options

(* ---- 13.8 … and on an ACYCLIC set it places every node ---- *)

#push-options "--z3rlimit 250 --fuel 2 --ifuel 2"
let rec kahn_complete (#op:eqtype) (pick:list string -> string) (fuel:list (node op))
  (rest:list (node op)) (closure emitted w:list string) (x:string)
  : Lemma (requires picks_from_frontier pick /\ distinct (ids_of rest) /\
                    covers fuel (ids_of rest) /\
                    (forall (y:string). mem y (ids_of rest) ==> not (mem y emitted)) /\
                    distinct w /\ (forall (y:string). mem y w == mem y (ids_of rest)) /\
                    follows_parents_or_emitted rest closure emitted w)
          (ensures mem x (kahn pick fuel rest closure emitted) == mem x (ids_of rest))
          (decreases fuel)
  = covers_cons_fuel fuel (ids_of rest);
    match fuel with
    | [] -> ()
    | _ :: fuel' ->
      (match w with
       | [] ->
         (* an empty witness enumerates an empty set, so there is nothing left to place *)
         (match rest with
          | [] -> ()
          | n :: _ -> mem_ids_of n rest)
       | h :: wt ->
         lookup_of_mem_ids rest h;
         let nh = Found?._0 (lookup rest h) in
         follows_mem rest closure emitted w nh;
         let auxb (p:string) : Lemma (before p h w == false) = before_nothing_at_head p h wt in
         FStar.Classical.forall_intro auxb;
         all_before_or_emitted_none (parents_in nh.nparents closure) nh.nid emitted w;
         frontier_contains rest closure emitted nh;
         (match frontier rest closure emitted with
          | [] -> ()
          | f ->
            let id = pick f in
            assert (mem id f);
            frontier_sub rest closure emitted id;
            let rest' = remove_id rest id in
            let emitted' = app emitted [ id ] in
            let w' = remove_first id w in
            ids_of_remove_id rest id;
            distinct_remove_first (ids_of rest) id;
            distinct_remove_first w id;
            covers_remove_first fuel (ids_of rest) id;
            let aux1 (y:string) : Lemma (mem y (ids_of rest') ==> not (mem y emitted')) =
              if mem y (ids_of rest') then begin
                mem_remove_first (ids_of rest) id y;
                mem_app y emitted [ id ]
              end
              else ()
            in
            FStar.Classical.forall_intro aux1;
            let aux2 (y:string) : Lemma (mem y w' == mem y (ids_of rest')) =
              mem_remove_first w id y;
              mem_remove_first (ids_of rest) id y
            in
            FStar.Classical.forall_intro aux2;
            follows_remove_id rest closure emitted w id;
            let aux3 (n:node op) : Lemma (mem n rest' ==> n.nid =!= id) =
              if mem n rest' then remove_id_not_id rest id n else ()
            in
            FStar.Classical.forall_intro aux3;
            follows_step rest' closure emitted w id;
            kahn_complete pick fuel' rest' closure emitted' w' x;
            mem_remove_first (ids_of rest) id x))
#pop-options

(* ---- 13.9 … and the sequence is a function of the node SET ---- *)

#push-options "--z3rlimit 250 --fuel 2 --ifuel 2"
let rec kahn_det (#op:eqtype) (lt:string -> string -> bool)
  (f1 f2 r1 r2:list (node op)) (c1 c2 emitted:list string)
  : Lemma (requires total_order lt /\
                    (forall (n:node op). mem n r1 == mem n r2) /\
                    (forall (y:string). mem y c1 == mem y c2) /\
                    distinct (ids_of r1) /\ distinct (ids_of r2) /\
                    covers f1 (ids_of r1) /\ covers f2 (ids_of r2))
          (ensures kahn (pick_min lt) f1 r1 c1 emitted == kahn (pick_min lt) f2 r2 c2 emitted)
          (decreases f1)
  = covers_cons_fuel f1 (ids_of r1);
    covers_cons_fuel f2 (ids_of r2);
    match f1 with
    | [] -> ()
    | _ :: g1 ->
      (match f2 with
       | [] -> ()
       | _ :: g2 ->
         frontier_same_closure r1 c1 c2 emitted;
         let fr1 = frontier r1 c2 emitted in
         let fr2 = frontier r2 c2 emitted in
         let auxf (y:string) : Lemma (mem y fr1 == mem y fr2) =
           (if mem y fr1 then frontier_transfer r1 r2 c2 emitted y else ());
           (if mem y fr2 then frontier_transfer r2 r1 c2 emitted y else ())
         in
         FStar.Classical.forall_intro auxf;
         (match fr1 with
          | [] -> (match fr2 with | [] -> () | y :: _ -> assert (mem y fr1))
          | _ :: _ ->
            (match fr2 with
             | [] -> (match fr1 with | [] -> () | y :: _ -> assert (mem y fr2))
             | _ :: _ ->
               pick_min_same lt fr1 fr2;
               let id = pick_min lt fr1 in
               pick_min_mem lt fr1;
               frontier_sub r1 c2 emitted id;
               frontier_sub r2 c2 emitted id;
               ids_of_remove_id r1 id;
               ids_of_remove_id r2 id;
               distinct_remove_first (ids_of r1) id;
               distinct_remove_first (ids_of r2) id;
               covers_remove_first f1 (ids_of r1) id;
               covers_remove_first f2 (ids_of r2) id;
               let auxn (n:node op) : Lemma (mem n (remove_id r1 id) == mem n (remove_id r2 id)) =
                 mem_remove_id r1 id n;
                 mem_remove_id r2 id n
               in
               FStar.Classical.forall_intro auxn;
               kahn_det lt g1 g2 (remove_id r1 id) (remove_id r2 id) c1 c2 (app emitted [ id ]))))
#pop-options

let rec first_outside_same (ps c1 c2:list string)
  : Lemma (requires forall (y:string). mem y c1 == mem y c2)
          (ensures first_outside ps c1 == first_outside ps c2)
          (decreases ps)
  = match ps with
    | [] -> ()
    | _ :: t -> first_outside_same t c1 c2

let rec dangling_contains (#op:eqtype) (ns:list (node op)) (closure:list string) (n:node op)
  : Lemma (requires mem n ns /\ Found? (first_outside n.nparents closure))
          (ensures mem n.nid (dangling_ids ns closure))
          (decreases ns)
  = match ns with
    | [] -> ()
    | m :: t -> if m = n then () else dangling_contains t closure n

let rec dangling_transfer (#op:eqtype) (r1 r2:list (node op)) (c1 c2:list string) (id:string)
  : Lemma (requires (forall (n:node op). mem n r1 ==> mem n r2) /\
                    (forall (y:string). mem y c1 == mem y c2) /\
                    mem id (dangling_ids r1 c1))
          (ensures mem id (dangling_ids r2 c2))
          (decreases r1)
  = match r1 with
    | [] -> ()
    | n :: t ->
      first_outside_same n.nparents c1 c2;
      if Found? (first_outside n.nparents c1) && n.nid = id
      then dangling_contains r2 c2 n
      else dangling_transfer t r2 c1 c2 id

(* A `placed_follow_parents` over an order that holds EVERY id is a `follows_parents_in`: the
   guard that made the claim conditional on placement is true of every node. *)
let rec placed_follow_all (#op:eqtype) (ns:list (node op)) (closure ord:list string)
  : Lemma (requires placed_follow_parents ns closure [] ord /\
                    (forall (y:string). mem y (ids_of ns) ==> mem y ord))
          (ensures follows_parents_in ns closure ord)
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: t ->
      assert (mem n.nid (ids_of ns));
      placed_follow_all t closure ord

(* ---- 13.10 THE THEOREMS ---- *)

(* THEOREM (task 1a). Every node the drain places stands after every one of its in-set parents.
   F#: what `drainClosure`'s placed list has by construction — a node is emitted only once its
   in-degree over the closure has reached zero, and that in-degree counts exactly the in-closure
   parents still unemitted. Quantified over the nodes it PLACED, which is the whole of them
   whenever the set is acyclic and a prefix of them when it is not. *)
let drain_linear_extension (#op:eqtype) (lt:string -> string -> bool) (fuel:list (node op))
  (ns:list (node op))
  : Lemma (requires distinct (ids_of ns))
          (ensures (let ord = drain_order lt fuel ns in
                    distinct ord /\
                    (forall (x:string). mem x ord ==> mem x (ids_of ns)) /\
                    placed_follow_parents ns (ids_of ns) [] ord))
  = pick_min_is_a_tie_break lt;
    kahn_extends (pick_min lt) fuel ns (ids_of ns) []

(* THEOREM (task 1b). On an ACYCLIC set — one admitting any topological enumeration `w` at all —
   the drain places every node exactly once, and what it produces is itself such an enumeration.
   So the drain does not merely terminate: it is TOTAL, and its output is a linear extension of
   the parent relation over the whole set. *)
#push-options "--z3rlimit 100"
let drain_total_on_acyclic (#op:eqtype) (lt:string -> string -> bool) (fuel:list (node op))
  (ns:list (node op)) (w:list string)
  : Lemma (requires distinct (ids_of ns) /\ covers fuel (ids_of ns) /\ is_topo_enum ns w)
          (ensures (let ord = drain_order lt fuel ns in
                    distinct ord /\ (forall (x:string). mem x ord == mem x (ids_of ns)) /\
                    is_topo_enum ns ord))
  = pick_min_is_a_tie_break lt;
    drain_linear_extension lt fuel ns;
    let ord = drain_order lt fuel ns in
    let auxw (y:string) : Lemma (mem y w == mem y (ids_of ns)) =
      (if mem y w then sub_ids_mem w (ids_of ns) y else ());
      (if mem y (ids_of ns) then sub_ids_mem (ids_of ns) w y else ())
    in
    FStar.Classical.forall_intro auxw;
    let auxm (x:string) : Lemma (mem x ord == mem x (ids_of ns)) =
      kahn_complete (pick_min lt) fuel ns (ids_of ns) [] w x
    in
    FStar.Classical.forall_intro auxm;
    sub_ids_of_mem ord (ids_of ns);
    sub_ids_of_mem (ids_of ns) ord;
    placed_follow_all ns (ids_of ns) ord
#pop-options


(* THEOREM. The converse, read off the linear-extension theorem rather than proved again: a drain
   that placed every node IS a topological enumeration, so it witnesses the set's acyclicity.
   With the theorem above this is an IFF — complete drain if and only if acyclic — and that iff is
   exactly the claim `Dag.isAcyclic` / `Dag.tryTopoOrder` make by comparing the emitted list's
   length against the closure's size. A cycle is therefore not "surfaced as an error" by the drain
   itself; it is surfaced as the drain being SHORT, which is what those two read. *)
let drain_complete_is_acyclic (#op:eqtype) (lt:string -> string -> bool) (fuel:list (node op))
  (ns:list (node op))
  : Lemma (requires distinct (ids_of ns) /\
                    (forall (y:string). mem y (ids_of ns) ==>
                                        mem y (drain_order lt fuel ns)))
          (ensures is_topo_enum ns (drain_order lt fuel ns))
  = drain_linear_extension lt fuel ns;
    let ord = drain_order lt fuel ns in
    sub_ids_of_mem ord (ids_of ns);
    sub_ids_of_mem (ids_of ns) ord;
    placed_follow_all ns (ids_of ns) ord

(* THEOREM (task 1c). The sequence is a function of the node SET: permute the work list — F#:
   receive the lanes in any order at all — and the same list comes back, under either policy.
   This is the claim every clone's determinism rests on, and it is the one that CONSUMES the
   tie-break: `frontier` answers in the work list's order, so a permuted set hands the selector a
   permuted frontier, and only an order-invariant selector survives that. 13.0's `pick_head`
   satisfies `picks_from_frontier` and does not survive it. *)
#push-options "--z3rlimit 150"
let drain_deterministic (#op:eqtype) (policy:dangling_policy) (lt:string -> string -> bool)
  (f1 f2 r1 r2:list (node op)) (p:perm (node op) r1 r2)
  : Lemma (requires total_order lt /\ distinct (ids_of r1) /\ distinct (ids_of r2) /\
                    covers f1 (ids_of r1) /\ covers f2 (ids_of r2))
          (ensures drain policy lt f1 r1 == drain policy lt f2 r2)
  = let auxn (n:node op) : Lemma (mem n r1 == mem n r2) = perm_mem r1 r2 p n in
    FStar.Classical.forall_intro auxn;
    let auxi (y:string) : Lemma (mem y (ids_of r1) == mem y (ids_of r2)) =
      ids_same_of_nodes_same r1 r2 y
    in
    FStar.Classical.forall_intro auxi;
    kahn_det lt f1 f2 r1 r2 (ids_of r1) (ids_of r2) [];
    match policy with
    | IgnoreDangling -> ()
    | RefuseDangling ->
      let d1 = dangling_ids r1 (ids_of r1) in
      let d2 = dangling_ids r2 (ids_of r2) in
      let auxd (y:string) : Lemma (mem y d1 == mem y d2) =
        (if mem y d1 then dangling_transfer r1 r2 (ids_of r1) (ids_of r2) y else ());
        (if mem y d2 then dangling_transfer r2 r1 (ids_of r2) (ids_of r1) y else ())
      in
      FStar.Classical.forall_intro auxd;
      (match d1 with
       | [] -> (match d2 with | [] -> () | y :: _ -> assert (mem y d1))
       | _ :: _ ->
         (match d2 with
          | [] -> (match d1 with | [] -> () | y :: _ -> assert (mem y d2))
          | _ :: _ -> pick_min_same lt d1 d2))
#pop-options

(* ---- 13.11 the two policies, and what each production caller gets ---- *)


(* THEOREM. The policy is invisible on a set that holds every parent it names: the two production
   call sites compute the same order, and the fold path pays nothing for the refusing one's
   existence. (`Dag.drainClosure`'s held-parent filter and `Dag.firstBreak`'s `tryFind` are asking the
   same question of the same data; they differ only in what they do with the answer.) *)
let drain_policies_agree (#op:eqtype) (lt:string -> string -> bool) (fuel:list (node op))
  (ns:list (node op))
  : Lemma (requires dangling_ids ns (ids_of ns) == [])
          (ensures drain IgnoreDangling lt fuel ns == drain RefuseDangling lt fuel ns)
  = ()

(* THEOREM. … and where they differ, they differ exactly on the presence of a parent outside the
   set. F#: `MissingParent`, which `verifyDag` raises before any drain runs and which `drainClosure`
   silently drops. *)
let drain_refusal_characterised (#op:eqtype) (lt:string -> string -> bool) (fuel:list (node op))
  (ns:list (node op)) (n:node op)
  : Lemma (requires mem n ns /\ Found? (first_outside n.nparents (ids_of ns)))
          (ensures Refused? (drain RefuseDangling lt fuel ns))
  = dangling_contains ns (ids_of ns) n

(* … and the refusal is not vacuous in the other direction either: a set with no outside parent is
   drained rather than refused, which is the clause `drain_policies_agree` rests on. *)
let drain_refusal_needs_a_dangler (#op:eqtype) (lt:string -> string -> bool) (fuel:list (node op))
  (ns:list (node op))
  : Lemma (requires Refused? (drain RefuseDangling lt fuel ns))
          (ensures Cons? (dangling_ids ns (ids_of ns)))
  = ()

(* ---- 13.12 the SPINE case (Phase 142): forced at every selector, and at the id order ---- *)

(* Section 11 computes the whole DAG layer beneath the fold and, as Phase 134 left it, ARGUED one
   step rather than proving it: production's `drainClosure` drains a ready frontier
   smallest-id-first, the model's `topo_of` reverses the parent walk, and "on a spine there is only
   one topological order anyway, so the two coincide" was a sentence in a comment. Phase 142
   mechanised that sentence over the same base-plus-N-chains model, with no premise beyond the id
   distinctness section 11 already names. It was section 12 until Phase 534; the drain it defines
   is 13.0, which the rest of this section is built on, and its results are here, beside the
   corollaries that re-derive them at the id order (13.12.6).

   THE TWO LEMMAS.
     - `spine_order_forced` (13.12.1) — a DISTINCT enumeration of a spine's closure in which every
       node follows its own parent is that spine in append order. A list argument: it mentions no
       DAG, no hash and no production function. The "exactly one topological order exists" half.
     - `kahn_drain_is_such_an_enumeration` (13.12.3) — the frontier drain (F#: the `while` loop of
       `drainNodes` under `drainClosure` over `indeg` / `ready`) run over a spine's closure produces
       exactly that enumeration. The "and production's algorithm produces it" half.

   AT EVERY SELECTOR. The selector is a PARAMETER `pick`, constrained only to return a member of the
   frontier it is handed (`picks_from_frontier`), and every result here is stated for EVERY such
   selector: `kahn_frontier_singleton` proves the frontier is a ONE-element list at every step of a
   spine's drain, so every selector returns the same element and the choice cannot be observed.
   The id order is section 13's subject, where the frontier is wider than one; 13.12.6 instantiates
   these results at it.

   WHAT IT BUYS SECTION 11. `between_chain_any_order` (13.12.5) is `between_chain` restated with
   the topological order UNIVERSALLY QUANTIFIED — any distinct, parent-respecting enumeration of
   the head's closure — where section 11 fixed it to `topo_of` by definition;
   `reconcile_many_dag_ordered_eq` and `fold_once_dag_ordered_eq` carry that up to the fold, so
   `Dag.reconcileMany` FROM the DAG is the deltas-first fold whatever order each head's recovery
   walked. *)

(* ---- 13.12.1 the list argument: such an enumeration is FORCED ---- *)

(* A distinct list whose members are one element is that one element. The base case, and the only
   place the "distinct" hypothesis does work on its own. *)
let singleton_enum (q:string) (ord:list string)
  : Lemma (requires distinct ord /\ (forall (x:string). mem x ord == mem x [ q ]))
          (ensures ord == [ q ])
  = match ord with
    | [] -> assert (mem q [ q ])
    | h :: t ->
      assert (mem h ord);
      assert (h == q);
      (match t with
       | [] -> ()
       | y :: _ ->
         assert (mem y t);
         assert (mem y ord);
         assert (y == q))

(* No spine id can be the HEAD of such an enumeration: every one of them has a parent that must
   come strictly before it, and nothing comes before the head. This is the whole force of the
   argument, and the only step where `follows_spine` is consumed rather than carried. *)
let rec follows_spine_not_head (q:string) (ids:list string) (h:string) (r:list string)
  : Lemma (requires follows_spine q ids (h :: r) /\ distinct (q :: ids) /\ mem h ids)
          (ensures False)
          (decreases ids)
  = match ids with
    | [] -> ()
    | x :: t ->
      if x = h then begin
        assert (not (mem q ids));
        assert (q =!= h);
        assert (before q x (h :: r) == false)
      end
      else follows_spine_not_head x t h r

(* Dropping a head the spine does not contain leaves the property intact. *)
let rec follows_spine_tail (h:string) (root:string) (ids:list string) (ord:list string)
  : Lemma (requires follows_spine root ids (h :: ord) /\ h =!= root /\ not (mem h ids))
          (ensures follows_spine root ids ord)
          (decreases ids)
  = match ids with
    | [] -> ()
    | x :: t -> follows_spine_tail h x t ord

(* … and prefixing one back is the same step read the other way. *)
let rec follows_spine_cons (h:string) (root:string) (ids:list string) (ord:list string)
  : Lemma (requires follows_spine root ids ord /\ h =!= root /\ not (mem h ids))
          (ensures follows_spine root ids (h :: ord))
          (decreases ids)
  = match ids with
    | [] -> ()
    | x :: t -> follows_spine_cons h x t ord

(* Membership transfers to the tails once the shared head is known distinct from both. *)
let members_tail (q:string) (r ids:list string)
  : Lemma (requires (forall (x:string). mem x (q :: r) == mem x (q :: ids)) /\
                    not (mem q r) /\ not (mem q ids))
          (ensures forall (x:string). mem x r == mem x ids)
  = let aux (x:string) : Lemma (mem x r == mem x ids) =
      if x = q then () else ()
    in
    FStar.Classical.forall_intro aux

(* THE LIST ARGUMENT. A distinct enumeration of a spine's ids plus its root, in which each id
   follows the one it hangs off, is the root followed by the spine in append order. Nothing about
   DAGs, hashes or production appears in it. *)
#push-options "--z3rlimit 100"
let rec spine_ids_forced (q:string) (ids:list string) (ord:list string)
  : Lemma (requires distinct (q :: ids) /\ distinct ord /\
                    (forall (x:string). mem x ord == mem x (q :: ids)) /\
                    follows_spine q ids ord)
          (ensures ord == q :: ids)
          (decreases ids)
  = match ids with
    | [] -> singleton_enum q ord
    | x :: t ->
      assert (mem q ord);
      (match ord with
       | [] -> ()
       | h :: r ->
         if mem h ids then follows_spine_not_head q ids h r
         else begin
           assert (mem h ord);
           assert (mem h (q :: ids));
           assert (h == q);
           members_tail q r ids;
           follows_spine_tail q x t r;
           spine_ids_forced x t r
         end)
#pop-options

(* THEOREM (task 1). The same statement over the nodes: a distinct enumeration of a spine's closure
   respecting each node's single parent is that spine, in append order, with the base first.

   F#: `Dag.between` filters `drainClosure dag [ head ]`'s placed nodes against the base's closure and looks the ids up,
   so the ONLY freedom the recovery has is which topological order that first step produced. This
   says there is none to have. *)
let spine_order_forced (#op:eqtype) (ns:list (node op)) (q:string) (ord:list string)
  : Lemma (requires parents_chain ns q /\ distinct (q :: ids_of ns) /\ distinct ord /\
                    (forall (x:string). mem x ord == mem x (q :: ids_of ns)) /\
                    follows_parents ns ord)
          (ensures ord == q :: ids_of ns)
  = follows_parents_spine ns q ord;
    spine_ids_forced q (ids_of ns) ord

(* ---- 13.12.2 … and the append order IS such an enumeration ---- *)

let rec follows_spine_append_order (q:string) (ids:list string)
  : Lemma (requires distinct (q :: ids))
          (ensures follows_spine q ids (q :: ids))
          (decreases ids)
  = match ids with
    | [] -> ()
    | x :: t ->
      follows_spine_append_order x t;
      follows_spine_cons q x t (x :: t)

let follows_parents_append_order (#op:eqtype) (ns:list (node op)) (q:string)
  : Lemma (requires parents_chain ns q /\ distinct (q :: ids_of ns))
          (ensures follows_parents ns (q :: ids_of ns))
  = follows_spine_append_order q (ids_of ns);
    follows_spine_parents ns q (q :: ids_of ns)

(* ---- 13.12.3 the frontier drain over a spine, at every selector ---- *)

(* Nothing in an unemitted spine is ready while its own root is unemitted — the tail of the
   frontier computation, and what makes the head of it the only entry. *)
let rec frontier_none (#op:eqtype) (ns:list (node op)) (r:string) (closure emitted:list string)
  : Lemma (requires parents_chain ns r /\ mem r closure /\ not (mem r emitted) /\
                    (forall (x:string). mem x (ids_of ns) ==> not (mem x emitted)) /\
                    (forall (x:string). mem x (ids_of ns) ==> mem x closure))
          (ensures frontier ns closure emitted == [])
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: t ->
      assert (parents_in n.nparents closure == [ r ]);
      assert (all_emitted [ r ] emitted == false);
      assert (mem n.nid (ids_of ns));
      frontier_none t n.nid closure emitted

(* THE TIE-BREAK IS UNEXERCISED. At every step of a spine's drain the ready frontier holds exactly
   one id — so `pick` is applied only to one-element lists, and `picks_from_frontier` pins its
   answer without anything being said about how ids compare. F#: `ready` is a one-element list on
   every iteration of `drainClosure`'s loop over this closure, so `List.sort` is the identity and
   `List.head` is forced. *)
let kahn_frontier_singleton (#op:eqtype) (n:node op) (t:list (node op)) (q:string)
  (closure emitted:list string)
  : Lemma (requires parents_chain (n :: t) q /\ mem q closure /\ mem q emitted /\
                    (forall (x:string). mem x (ids_of (n :: t)) ==> not (mem x emitted)) /\
                    (forall (x:string). mem x (ids_of (n :: t)) ==> mem x closure))
          (ensures frontier (n :: t) closure emitted == [ n.nid ])
  = assert (parents_in n.nparents closure == [ q ]);
    assert (all_emitted [ q ] emitted);
    assert (mem n.nid (ids_of (n :: t)));
    frontier_none t n.nid closure emitted

#push-options "--z3rlimit 100"
let rec kahn_spine (#op:eqtype) (pick:list string -> string) (fuel:list (node op))
  (ns:list (node op)) (q:string) (closure emitted:list string)
  : Lemma (requires picks_from_frontier pick /\ parents_chain ns q /\
                    mem q closure /\ mem q emitted /\ distinct (ids_of ns) /\
                    (forall (x:string). mem x (ids_of ns) ==> not (mem x emitted)) /\
                    (forall (x:string). mem x (ids_of ns) ==> mem x closure) /\
                    covers fuel (ids_of ns))
          (ensures kahn pick fuel ns closure emitted == ids_of ns)
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: t ->
      (match fuel with
       | [] -> ()
       | _ :: fuel' ->
         kahn_frontier_singleton n t q closure emitted;
         assert (mem (pick [ n.nid ]) [ n.nid ]);
         assert (pick [ n.nid ] == n.nid);
         assert (remove_id ns n.nid == t);
         let emitted' = app emitted [ n.nid ] in
         let aux (x:string) : Lemma (mem x (ids_of t) ==> not (mem x emitted')) =
           if mem x (ids_of t) then begin
             assert (mem x (ids_of ns));
             assert (not (mem n.nid (ids_of t)));
             assert (x =!= n.nid);
             mem_app x emitted [ n.nid ]
           end
           else ()
         in
         FStar.Classical.forall_intro aux;
         mem_app q emitted [ n.nid ];
         kahn_spine pick fuel' t n.nid closure emitted')
#pop-options

(* THEOREM (task 2). Kahn's drain over a spine's closure — base first, then the chain — produces
   the spine in append order, and that list is a distinct enumeration of the closure in which every
   node follows its parent. Which is to say: it is an enumeration of the kind `spine_order_forced`
   proves there is only one of. For EVERY tie-break selector. *)
#push-options "--z3rlimit 100"
let kahn_drain_is_such_an_enumeration (#op:eqtype) (pick:list string -> string)
  (fuel:list (node op)) (bn:node op) (ns:list (node op))
  : Lemma (requires picks_from_frontier pick /\ bn.nparents == [] /\
                    parents_chain ns bn.nid /\ distinct (bn.nid :: ids_of ns) /\
                    covers fuel (bn.nid :: ids_of ns))
          (ensures (let closure = bn.nid :: ids_of ns in
                    let ord = kahn pick fuel (bn :: ns) closure [] in
                    ord == closure /\ distinct ord /\ follows_parents ns ord))
  = let q = bn.nid in
    let closure = q :: ids_of ns in
    (match fuel with
     | [] -> ()
     | _ :: fuel' ->
       assert (parents_in bn.nparents closure == []);
       frontier_none ns q closure [];
       assert (frontier (bn :: ns) closure [] == [ q ]);
       assert (mem (pick [ q ]) [ q ]);
       assert (pick [ q ] == q);
       assert (remove_id (bn :: ns) q == ns);
       kahn_spine pick fuel' ns q closure [ q ];
       follows_parents_append_order ns q)
#pop-options

(* ---- 13.12.4 the bridge to a lane: `lane_nodes` IS a spine off the base ---- *)

(* Appending an op at the OLDEST end re-roots the rest of the chain — the calculation that turns
   `lane_nodes`, which is built newest-first, into a cons at the front. *)
let rec chain_head_rev_snoc (#op:eqtype) (mint:string -> string -> op -> string)
  (actor q:string) (o:op) (rl:list op)
  : Lemma (ensures chain_head_rev mint actor q (app rl [ o ]) ==
                   chain_head_rev mint actor (mint q actor o) rl)
          (decreases rl)
  = match rl with
    | [] -> ()
    | _ :: t -> chain_head_rev_snoc mint actor q o t

let rec chain_rev_snoc (#op:eqtype) (mint:string -> string -> op -> string)
  (actor q:string) (o:op) (rl:list op)
  : Lemma (ensures chain_rev mint actor q (app rl [ o ]) ==
                   app (chain_rev mint actor (mint q actor o) rl)
                       [ { nid = mint q actor o; nparents = [ q ]; nop = o } ])
          (decreases rl)
  = match rl with
    | [] -> ()
    | _ :: t ->
      chain_head_rev_snoc mint actor q o t;
      chain_rev_snoc mint actor q o t

let lane_nodes_cons (#op:eqtype) (mint:string -> string -> op -> string)
  (actor q:string) (o:op) (t:list op)
  : Lemma (ensures lane_nodes mint actor q (o :: t) ==
                   { nid = mint q actor o; nparents = [ q ]; nop = o }
                   :: lane_nodes mint actor (mint q actor o) t)
  = let n0 = { nid = mint q actor o; nparents = [ q ]; nop = o } in
    chain_rev_snoc mint actor q o (rev t);
    rev_app (chain_rev mint actor (mint q actor o) (rev t)) [ n0 ]

(* A lane's nodes, in append order, are a spine off the base id. *)
let rec parents_chain_lane (#op:eqtype) (mint:string -> string -> op -> string)
  (actor q:string) (l:list op)
  : Lemma (ensures parents_chain (lane_nodes mint actor q l) q) (decreases l)
  = match l with
    | [] -> ()
    | o :: t ->
      lane_nodes_cons mint actor q o t;
      parents_chain_lane mint actor (mint q actor o) t

(* The model's own topological order on a lane head, named: the base, then the lane in append
   order. Factored out of `between_chain`'s calculation, which computed it inline. *)
#push-options "--z3rlimit 100"
let topo_of_chain (#op:eqtype) (d:dag op) (mint:string -> string -> op -> string)
  (actor:string) (bn:node op) (l:list op) (fuel:list (node op))
  : Lemma (requires lookup d.nodes bn.nid == Found bn /\ bn.nparents == [] /\
                    resolves d (chain_rev mint actor bn.nid (rev l)) /\
                    covers fuel (rev l))
          (ensures topo_of d fuel (lane_head mint actor bn.nid l)
                   == bn.nid :: ids_of (lane_nodes mint actor bn.nid l))
  = let q = bn.nid in
    let rl = rev l in
    let c = chain_rev mint actor q rl in
    covers_drop fuel rl;
    ancestors_chain d mint actor q rl fuel;
    base_ancestors d bn (drop_by fuel rl);
    rev_app (ids_of c) [ q ];
    ids_of_rev c
#pop-options

(* ---- 13.12.5 the restatement: the recovery does not depend on the order ---- *)

(* What the ORDER's uniqueness needs of content addressing, and it is the same premise section 11
   already names rather than a new one: the lane's ids are distinct from one another and from the
   base's. `distinct_ids` / `resolves_of_distinct` state it for the whole DAG; this is that premise
   read at lane granularity, exactly as `lane_recovers`'s `resolves` is. It SUBSUMES
   `lane_recovers`'s third clause (the base id is not one of the lane's), which is its head. *)
let lane_ids_distinct (#op:eqtype) (mint:string -> string -> op -> string)
  (bn:node op) (ln:lane op) : Tot bool =
  distinct (bn.nid :: ids_of (lane_nodes mint ln.lactor bn.nid ln.lops))

(* F#: `Dag.between` from the point where the topological order has been chosen —
   `drainClosure dag [ head ] |> fst |> List.filter … |> List.map …` with the first step's RESULT taken as a
   parameter. `between d fuel base_id head` is this at `ord = topo_of d fuel head`, definitionally. *)
let between_ordered (#op:eqtype) (d:dag op) (fuel:list (node op)) (base_id:string)
  (ord:list string) : Tot (list (node op)) =
  nodes_for d (diff ord (ancestors_of d fuel base_id))

let between_ops_ordered (#op:eqtype) (d:dag op) (fuel:list (node op)) (base_id:string)
  (ord:list string) : Tot (list op) =
  ops_of (between_ordered d fuel base_id ord)

(* THEOREM (task 3). `between_chain`, restated with the topological order UNIVERSALLY QUANTIFIED.
   Section 11's statement fixes the order to `topo_of` — the reverse of the parent walk — by
   definition, and the claims ladder carried that choice as an assumption. Here the order is any
   distinct, parent-respecting enumeration of the head's closure whatsoever, and the recovery comes
   back the same, because by `spine_order_forced` there is only one such enumeration. Production's
   frontier drain is one of them (`kahn_drain_is_such_an_enumeration`), so this covers it. *)
#push-options "--z3rlimit 150"
let between_chain_any_order (#op:eqtype) (d:dag op) (mint:string -> string -> op -> string)
  (actor:string) (bn:node op) (l:list op) (fuel:list (node op)) (ord:list string)
  : Lemma (requires lookup d.nodes bn.nid == Found bn /\ bn.nparents == [] /\
                    lane_recovers d mint bn fuel ({ lactor = actor; lops = l }) /\
                    lane_ids_distinct mint bn ({ lactor = actor; lops = l }) /\
                    distinct ord /\
                    (forall (x:string).
                       mem x ord == mem x (topo_of d fuel (lane_head mint actor bn.nid l))) /\
                    follows_parents (lane_nodes mint actor bn.nid l) ord)
          (ensures between_ordered d fuel bn.nid ord == lane_nodes mint actor bn.nid l)
  = let q = bn.nid in
    let lns = lane_nodes mint actor q l in
    topo_of_chain d mint actor bn l fuel;
    parents_chain_lane mint actor q l;
    spine_order_forced lns q ord;
    between_chain d mint actor bn l fuel
#pop-options

#push-options "--z3rlimit 150"
let between_ops_chain_any_order (#op:eqtype) (d:dag op) (mint:string -> string -> op -> string)
  (actor:string) (bn:node op) (l:list op) (fuel:list (node op)) (ord:list string)
  : Lemma (requires lookup d.nodes bn.nid == Found bn /\ bn.nparents == [] /\
                    lane_recovers d mint bn fuel ({ lactor = actor; lops = l }) /\
                    lane_ids_distinct mint bn ({ lactor = actor; lops = l }) /\
                    distinct ord /\
                    (forall (x:string).
                       mem x ord == mem x (topo_of d fuel (lane_head mint actor bn.nid l))) /\
                    follows_parents (lane_nodes mint actor bn.nid l) ord)
          (ensures between_ops_ordered d fuel bn.nid ord == l)
  = between_chain_any_order d mint actor bn l fuel ord;
    ops_of_rev (chain_rev mint actor bn.nid (rev l));
    ops_of_chain_rev mint actor bn.nid (rev l);
    rev_rev l
#pop-options

(* THEOREM. Production's frontier drain and the model's parent-walk reversal are the SAME LIST, for
   every tie-break selector — the two halves joined. This is the sentence Phase 134's section 11
   argued, and the claims ladder's `topological-order-choice` row, as a theorem. *)
#push-options "--z3rlimit 150"
let topo_of_is_the_kahn_drain (#op:eqtype) (pick:list string -> string)
  (d:dag op) (mint:string -> string -> op -> string) (actor:string) (bn:node op) (l:list op)
  (fuel kfuel:list (node op))
  : Lemma (requires picks_from_frontier pick /\
                    lookup d.nodes bn.nid == Found bn /\ bn.nparents == [] /\
                    lane_recovers d mint bn fuel ({ lactor = actor; lops = l }) /\
                    lane_ids_distinct mint bn ({ lactor = actor; lops = l }) /\
                    covers kfuel (bn.nid :: ids_of (lane_nodes mint actor bn.nid l)))
          (ensures kahn pick kfuel
                        (bn :: lane_nodes mint actor bn.nid l)
                        (bn.nid :: ids_of (lane_nodes mint actor bn.nid l))
                        []
                   == topo_of d fuel (lane_head mint actor bn.nid l))
  = parents_chain_lane mint actor bn.nid l;
    kahn_drain_is_such_an_enumeration pick kfuel bn (lane_nodes mint actor bn.nid l);
    topo_of_chain d mint actor bn l fuel
#pop-options

(* ---- and the fold on top of it (F#: `Dag.reconcileMany`, `FoldConfluence.foldOnce`) ---- *)

(* F#: `heads |> List.map (betweenOps dag baseId)`, with each head's topological order supplied
   rather than computed — one order per head, in the same order as the heads. *)
let rec deltas_of_ordered (#op:eqtype) (d:dag op) (fuel:list (node op)) (base_id:string)
  (ords:list (list string)) : Tot (list (list op)) =
  match ords with
  | [] -> []
  | o :: t -> between_ops_ordered d fuel base_id o :: deltas_of_ordered d fuel base_id t

let reconcile_many_dag_ordered (#op:eqtype) (fp:op -> footprint) (d:dag op)
  (fuel:list (node op)) (base_id:string) (ords:list (list string))
  : Tot (outcome (list op) (list (conflict op))) =
  reconcile_many fp (deltas_of_ordered d fuel base_id ords)

let fold_once_dag_ordered (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint)
  (d:dag op) (fuel:list (node op)) (base_id:string) (s0:state) (ords:list (list string))
  : Tot (lane_outcome op state rej) =
  fold_once apply fp s0 (deltas_of_ordered d fuel base_id ords)

(* Every lane recovers, its ids are distinct, and the order its recovery walked is A topological
   order of its head's closure — whichever one. *)
let rec lanes_orders_ok (#op:eqtype) (d:dag op) (mint:string -> string -> op -> string)
  (bn:node op) (fuel:list (node op)) (lanes:list (lane op)) (ords:list (list string))
  : Tot prop (decreases lanes) =
  match lanes with
  | [] -> (match ords with | [] -> True | _ :: _ -> False)
  | ln :: lt ->
    (match ords with
     | [] -> False
     | o :: ot ->
       lane_recovers d mint bn fuel ln /\
       lane_ids_distinct mint bn ln /\
       distinct o /\
       (forall (x:string).
          mem x o == mem x (topo_of d fuel (lane_head mint ln.lactor bn.nid ln.lops))) /\
       follows_parents (lane_nodes mint ln.lactor bn.nid ln.lops) o /\
       lanes_orders_ok d mint bn fuel lt ot)

#push-options "--z3rlimit 100"
let rec deltas_of_ordered_lanes (#op:eqtype) (d:dag op) (mint:string -> string -> op -> string)
  (bn:node op) (fuel:list (node op)) (lanes:list (lane op)) (ords:list (list string))
  : Lemma (requires lookup d.nodes bn.nid == Found bn /\ bn.nparents == [] /\
                    lanes_orders_ok d mint bn fuel lanes ords)
          (ensures deltas_of_ordered d fuel bn.nid ords == lane_ops lanes)
          (decreases lanes)
  = match lanes with
    | [] -> ()
    | ln :: lt ->
      (match ords with
       | [] -> ()
       | o :: ot ->
         between_ops_chain_any_order d mint ln.lactor bn ln.lops fuel o;
         deltas_of_ordered_lanes d mint bn fuel lt ot)
#pop-options

(* THEOREM (task 3, the second half). `Dag.reconcileMany` FROM the DAG equals the deltas-first
   `reconcile_many` on the lanes that were appended — whatever topological order each head's
   recovery walked. Section 11's `reconcile_many_dag_eq` is the instance at the model's own order;
   this is it re-verified on top of a recovery that no longer chooses one. *)
let reconcile_many_dag_ordered_eq (#op:eqtype) (fp:op -> footprint) (d:dag op)
  (mint:string -> string -> op -> string) (bn:node op) (fuel:list (node op))
  (lanes:list (lane op)) (ords:list (list string))
  : Lemma (requires lookup d.nodes bn.nid == Found bn /\ bn.nparents == [] /\
                    lanes_orders_ok d mint bn fuel lanes ords)
          (ensures reconcile_many_dag_ordered fp d fuel bn.nid ords
                   == reconcile_many fp (lane_ops lanes))
  = deltas_of_ordered_lanes d mint bn fuel lanes ords

let fold_once_dag_ordered_eq (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint) (s0:state)
  (d:dag op) (mint:string -> string -> op -> string) (bn:node op) (fuel:list (node op))
  (lanes:list (lane op)) (ords:list (list string))
  : Lemma (requires lookup d.nodes bn.nid == Found bn /\ bn.nparents == [] /\
                    lanes_orders_ok d mint bn fuel lanes ords)
          (ensures fold_once_dag_ordered apply fp d fuel bn.nid s0 ords
                   == fold_once apply fp s0 (lane_ops lanes))
  = deltas_of_ordered_lanes d mint bn fuel lanes ords

(* ---- 13.12.6 … and at the id order: the spine case as a corollary of the drain ---- *)

(* COROLLARY. Phase 142 quantified over every selector because on a spine the frontier is one
   element wide and the tie-break is unexercised. `pick_min lt` is such a selector
   (`pick_min_is_a_tie_break`), so the spine results instantiate at the id-ordered drain with no
   second argument: the spine case (13.12.3, 13.12.5) is this section's special case, rather than
   a parallel one that has to be kept in step. *)
let spine_drain_is_the_parent_walk (#op:eqtype) (lt:string -> string -> bool)
  (d:dag op) (mint:string -> string -> op -> string) (actor:string) (bn:node op) (l:list op)
  (fuel kfuel:list (node op))
  : Lemma (requires lookup d.nodes bn.nid == Found bn /\ bn.nparents == [] /\
                    lane_recovers d mint bn fuel ({ lactor = actor; lops = l }) /\
                    lane_ids_distinct mint bn ({ lactor = actor; lops = l }) /\
                    covers kfuel (bn.nid :: ids_of (lane_nodes mint actor bn.nid l)))
          (ensures kahn (pick_min lt) kfuel
                        (bn :: lane_nodes mint actor bn.nid l)
                        (bn.nid :: ids_of (lane_nodes mint actor bn.nid l))
                        []
                   == topo_of d fuel (lane_head mint actor bn.nid l))
  = pick_min_is_a_tie_break lt;
    topo_of_is_the_kahn_drain (pick_min lt) d mint actor bn l fuel kfuel

(* COROLLARY. And the same instantiation of the other half: over a base node and a spine hanging
   off it, the id-ordered drain emits the base and then the spine in append order. Which is
   `drain_order` itself, because a spine's closure is its own node set. *)
let spine_drain_is_append_order (#op:eqtype) (lt:string -> string -> bool)
  (kfuel:list (node op)) (bn:node op) (ns:list (node op))
  : Lemma (requires bn.nparents == [] /\ parents_chain ns bn.nid /\
                    distinct (bn.nid :: ids_of ns) /\
                    covers kfuel (bn.nid :: ids_of ns))
          (ensures drain_order lt kfuel (bn :: ns) == bn.nid :: ids_of ns)
  = pick_min_is_a_tie_break lt;
    kahn_drain_is_such_an_enumeration (pick_min lt) kfuel bn ns

(* ---- 13.13 the drain at a KEY: `Dag.totalOrderBy` (Phase 311) ---- *)

(* F#: `Dag.totalOrderBy key dag` drains the WHOLE node set with the ready frontier ordered by
   `(key node, id)`, smallest first — `Dag.drainBy`'s `Set<'K * string>` frontier. Consumers order
   the union of a lane store three ways (the smallest id; a Lamport rank, then the lane, then the id;
   a domain rank, then the id), and each is this drain at its own key. This section is the sentence
   "whatever the key, the order is a linear extension of the parent relation, total on an acyclic
   set, and a function of the node set": it builds the comparison the frontier uses out of the key
   and the id order, proves it a total order (`by_key_total`), and then every section-13 theorem
   holds of it by instantiation — section 13 took the order as a parameter for exactly this.

   THE KEY IS READ THROUGH A NODE TABLE BY ID. The drain's selector compares ids, so the key is
   looked up: `by_key tbl key ltk lt a b` compares `a`'s and `b`'s keys where the table holds them,
   and an id the table does not hold sorts after every one it does, by `lt` among themselves. The
   drain never asks about such an id when the table is the drained set (every frontier id is a node
   of it); the clause is there so the comparison is total on ALL strings, which is what section
   13's `total_order` asks.

   THE KEY ORDER IS A PARAMETER, as the id order is: `ltk` is any strict total order on the key
   type. F#'s structural comparison on a key tuple — `(int, string)`, `(string, int, string)` — is
   one; the claim is that replicas sharing a key agree, never that a key is any particular one.

   NO TUPLES are matched in the extracted part, for the reason `found` is spelled locally (README,
   finding 2). *)

(* A strict total order on any equality type — `total_order`'s three properties, generic. *)
let strict_total (#a:eqtype) (lt:a -> a -> bool) : prop =
  (forall (x:a). not (lt x x)) /\
  (forall (x y z:a). lt x y /\ lt y z ==> lt x z) /\
  (forall (x y:a). x =!= y ==> (lt x y \/ lt y x))

(* F#: the frontier's comparison in `Dag.drainBy` — `(key node, id)` lexicographically. *)
let by_key (#op:eqtype) (#k:eqtype) (tbl:list (node op)) (key:node op -> k) (ltk:k -> k -> bool)
  (lt:string -> string -> bool) (a b:string) : Tot bool =
  match lookup tbl a with
  | Found na ->
    (match lookup tbl b with
     | Found nb -> ltk (key na) (key nb) || (key na = key nb && lt a b)
     | Missing -> true)
  | Missing ->
    (match lookup tbl b with
     | Found _ -> false
     | Missing -> lt a b)

(* The key comparison is a total order whenever the key order and the id order are. *)
#push-options "--z3rlimit 60"
let by_key_total (#op:eqtype) (#k:eqtype) (tbl:list (node op)) (key:node op -> k)
  (ltk:k -> k -> bool) (lt:string -> string -> bool)
  : Lemma (requires strict_total ltk /\ total_order lt)
          (ensures total_order (by_key tbl key ltk lt))
  = let r = by_key tbl key ltk lt in
    let irr (x:string) : Lemma (not (r x x)) =
      match lookup tbl x with
      | Found _ -> ()
      | Missing -> ()
    in
    let tri (x y:string) : Lemma (x =!= y ==> (r x y \/ r y x)) =
      match lookup tbl x with
      | Found nx ->
        (match lookup tbl y with
         | Found ny -> if key nx = key ny then () else assert (ltk (key nx) (key ny) \/ ltk (key ny) (key nx))
         | Missing -> ())
      | Missing ->
        (match lookup tbl y with
         | Found _ -> ()
         | Missing -> ())
    in
    let trans (x y z:string) : Lemma (r x y /\ r y z ==> r x z) =
      match lookup tbl x with
      | Found nx ->
        (match lookup tbl y with
         | Found ny ->
           (match lookup tbl z with
            | Found nz ->
              assert (ltk (key nx) (key ny) /\ ltk (key ny) (key nz) ==> ltk (key nx) (key nz))
            | Missing -> ())
         | Missing ->
           (match lookup tbl z with
            | Found _ -> ()
            | Missing -> ()))
      | Missing ->
        (match lookup tbl y with
         | Found _ -> ()
         | Missing ->
           (match lookup tbl z with
            | Found _ -> ()
            | Missing -> ()))
    in
    FStar.Classical.forall_intro irr;
    FStar.Classical.forall_intro_2 tri;
    FStar.Classical.forall_intro_3 trans
#pop-options

(* THEOREM (Phase 311). `Dag.totalOrderBy` at ANY key is section 13's drain at a total order, so
   what it places it places once, after every one of its in-set parents: a linear extension of the
   parent relation, whatever the key. *)
let total_order_by_is_a_linear_extension (#op:eqtype) (#k:eqtype) (key:node op -> k)
  (ltk:k -> k -> bool) (lt:string -> string -> bool) (fuel ns:list (node op))
  : Lemma (requires strict_total ltk /\ total_order lt /\ distinct (ids_of ns))
          (ensures (let r = by_key ns key ltk lt in
                    let ord = drain_order r fuel ns in
                    total_order r /\
                    distinct ord /\
                    (forall (x:string). mem x ord ==> mem x (ids_of ns)) /\
                    placed_follow_parents ns (ids_of ns) [] ord))
  = by_key_total ns key ltk lt;
    drain_linear_extension (by_key ns key ltk lt) fuel ns

(* THEOREM (Phase 311). … TOTAL on an acyclic set: every node placed exactly once, and the order a
   topological enumeration — `Dag.totalOrderBy` returns `Ok` exactly on such a set. *)
let total_order_by_total_on_acyclic (#op:eqtype) (#k:eqtype) (key:node op -> k)
  (ltk:k -> k -> bool) (lt:string -> string -> bool) (fuel ns:list (node op)) (w:list string)
  : Lemma (requires strict_total ltk /\ total_order lt /\ distinct (ids_of ns) /\
                    covers fuel (ids_of ns) /\ is_topo_enum ns w)
          (ensures (let ord = drain_order (by_key ns key ltk lt) fuel ns in
                    distinct ord /\ (forall (x:string). mem x ord == mem x (ids_of ns)) /\
                    is_topo_enum ns ord))
  = drain_total_on_acyclic (by_key ns key ltk lt) fuel ns w

(* THEOREM (Phase 311). … and a function of the node SET: two replicas holding the same nodes in any
   order — F#: the lane files read in any order — draw the same order at the same key. The key is
   read through one table `tbl` (the store both replicas loaded), so the comparison is one function
   and section 13's determinism applies to it unchanged. *)
let total_order_by_deterministic (#op:eqtype) (#k:eqtype) (tbl:list (node op)) (key:node op -> k)
  (ltk:k -> k -> bool) (lt:string -> string -> bool) (f1 f2 r1 r2:list (node op))
  (p:perm (node op) r1 r2)
  : Lemma (requires strict_total ltk /\ total_order lt /\ distinct (ids_of r1) /\ distinct (ids_of r2) /\
                    covers f1 (ids_of r1) /\ covers f2 (ids_of r2))
          (ensures drain IgnoreDangling (by_key tbl key ltk lt) f1 r1 ==
                   drain IgnoreDangling (by_key tbl key ltk lt) f2 r2)
  = by_key_total tbl key ltk lt;
    drain_deterministic IgnoreDangling (by_key tbl key ltk lt) f1 f2 r1 r2 p

(* ======================================================================================
   14. Delta recovery over a MERGED HEAD (Phase 158).

   Section 11 proves `Dag.between` for ONE base node and N chains off it, and section 13
   prove the order beneath it. The shape a clone holds after it has folded once and pulled again
   is not that one: the lanes of the second round hang off a node whose own closure is the whole
   first round — a merge node with N parents, every first-round lane beneath it, and the original
   base beneath those. A head's closure there is no spine, its topological order is a genuine
   choice, and the base the fold runs from is a divergence point that has to be LOCATED. This
   section is that shape, claimed at a rung.

   THE SHAPE IS ABSTRACT WHERE IT CAN BE. `m` is any node the DAG holds; NOTHING is asked of what
   lies beneath it — no `bn.nparents == []`, no lanes, no base — beyond its closure being acyclic
   (section 13's premise, in section 13's witness form). The lanes hanging off it are section 11's
   own `lane_nodes` / `lane_head`, rooted at `m.nid` rather than at a parentless base. So "a merged
   head" here means every node a fold can leave behind, and section 11's base is the instance
   whose closure is itself.

   THE ORDER IS SECTION 13'S DRAIN, by name. `topo_drain` is `drain_order` — the id-ordered Kahn
   drain — over the head's closure taken as a node set (`closure_nodes`), which is F#'s
   `drainClosure`: the ancestor closure first, then the frontier loop over exactly those nodes.
   `between_drained` is 13.12.5's `between_ordered` at that order. Nothing is re-modelled, so
   the three drain theorems apply as they stand, and `lt` and the drain's fuel stay the
   parameters they were.

   THE TWO THEOREMS, and the one beside them.
     - `between_merged` — over a merged head, `Dag.between m head` returns the head's OWN lane
       nodes in append order and nothing else: not the first round beneath `m`, and nothing from
       a sibling lane. The drain's order over the first round is a real choice and the theorem
       does not care which way it went, because the difference against `m`'s closure deletes
       every node the choice was about; what survives is a spine, and on a spine a
       parent-respecting order is forced (13.12.1's `spine_ids_forced`, consumed here over the
       difference rather than over the closure). `between_ops_merged` is the ops.
     - `reconcile_many_merged_eq` / `fold_once_merged_eq` — `Dag.reconcileMany` and
       `FoldConfluence.foldOnce` FROM the merged DAG equal the deltas-first fold on the lanes that
       were appended, as section 11 proved them over a spine.
     - `merged_common_is_the_closure` — the common ancestors of two second-round heads are exactly
       `m`'s closure, and `m` is strictly the deepest of them, so `Dag.mergeBase` of the two heads
       is `m`: the divergence point, not the original base and not anything else they share. The
       theorem saying so is `DagPrimitives.merge_base_now_is_divergence`, stated over the
       `mergeBase` that runs (Phase 534; Phase 158 stated it here over a second model of the
       function, which production has not computed since Phase 388). The key is (closure SIZE, id);
       the size comparison is `shorter` over the de-duplicated closures, so the oracle still holds
       no integer; that `m` wins is production's own docstring sentence ("a node strictly deeper than any of its own ancestors has a strictly larger
       closure"), PROVED: the closure is transitive (`anc_trans`), `m` is in no proper ancestor's
       closure (`off_cycle`), and a distinct list strictly inside another is `shorter`
       (`pigeon`). The id tie-break is therefore UNEXERCISED on this shape — the maximum is
       strict — and the theorem holds for every `lt` whatsoever, total order or not.

   THE PREMISES, which are sections 11 and 13's and no new one.
     - ID DISTINCTNESS, exactly as Phase 134 states it and read at this shape's granularity
       (`merged_recovers`, `lane_ids_distinct`): every lane node is the node the DAG holds under
       its own id, and no lane id is an id in `m`'s closure. Phase 134's third clause — "the
       base's id is not one of the lane's" — is this clause at a base whose closure is itself.
       `merged_common_is_the_closure` also asks that the two lanes' ids be disjoint, which is what
       `foldOnce` folds the actor into every node id FOR.
     - ACYCLICITY, as section 13 takes it: a topological enumeration supplied as a witness, per
       head for the drain (`head_drains`) and of `m`'s closure for `mergeBase`.
       `merged_common_is_the_closure` consumes it in the form it actually uses — `off_cycle`, "`m` is
       not its own proper ancestor" — and `off_cycle_of_witness` DERIVES that form from the
       witness (an ancestor stands before its descendant in any topological enumeration,
       `anc_before`), exactly as `resolves_of_distinct` bridges section 11's premise.
       `DagPrimitives.merge_base_now_is_divergence_acyclic` is the theorem with the bridge
       applied.
     - FUEL. `ancestors_of` walks with a list as fuel, and over a merged head the walk from a lane
       head reaches `m` with LESS of it than a walk that starts at `m`. `walk_ok` says a walk never
       ran out; `anc_stable` proves such a walk is the same list under any longer fuel. So the fuel
       premise is "the walk below the lane completed" and the two closures the recovery compares
       are one list. Production's work-list has no fuel; this is the model's bound, discharged.

   WHAT IS STILL NOT CLAIMED. Second-round lanes that themselves MERGE before the fold — a head
   whose own delta contains a two-parent node; the delta there is not a spine and its order is the
   drain's choice, which section 13 makes deterministic but which no theorem here identifies with
   an append order, because there is none. `Dag.mergeBase` over heads with SEVERAL maximal common
   ancestors (a criss-cross merge), where the (size, id) tie-break decides and what it decides is
   a policy rather than a fact. Hashing, still: distinctness is the premise.

   NO SMT PATTERNS, for section 11's reason. Every lemma below is called by name.
   ====================================================================================== *)

(* ---- 14.1 fuel: a walk that never ran out is the same walk under any longer fuel ---- *)

(* `ancestors_of` never reached its empty-fuel arm on a node the DAG holds. F#: nothing — the
   production work-list is unbounded. This is the statement that the model's bound did not bite,
   and it is computable, so the differential host evaluates it on every DAG it builds. *)

let rec walk_ok (#op:eqtype) (d:dag op) (fuel:list (node op)) (id:string)
  : Tot bool (decreases %[fuel; (0 <: nat); ([] <: list string)]) =
  match fuel with
  | [] -> Missing? (lookup d.nodes id)
  | _ :: fuel' ->
    (match lookup d.nodes id with
     | Missing -> true
     | Found n -> walk_all_ok d fuel' n.nparents)

and walk_all_ok (#op:eqtype) (d:dag op) (fuel:list (node op)) (ids:list string)
  : Tot bool (decreases %[fuel; (1 <: nat); ids]) =
  match ids with
  | [] -> true
  | p :: t -> walk_ok d fuel p && walk_all_ok d fuel t

(* `big` is at least as long as `small`. The walk reads its fuel's LENGTH and nothing else, so this
   is the whole relation between two fuels that matters — spelled structurally, without integers. *)
let rec at_least (#a:Type) (big small:list a) : Tot bool (decreases small) =
  match small with
  | [] -> true
  | _ :: s -> (match big with | [] -> false | _ :: b -> at_least b s)

let rec at_least_refl (#a:Type) (l:list a) : Lemma (ensures at_least l l) =
  match l with
  | [] -> ()
  | _ :: t -> at_least_refl t

let rec at_least_tail (#a:Type) (big small:list a)
  : Lemma (requires at_least big small /\ Cons? small)
          (ensures at_least big (Cons?.tl small))
          (decreases small)
  = match small with
    | [ _ ] -> ()
    | _ :: s ->
      (match big with
       | [] -> ()
       | _ :: b -> at_least_tail b s)

let rec at_least_cons (#a:Type) (x:a) (f small:list a)
  : Lemma (requires at_least f small) (ensures at_least (x :: f) small) (decreases small)
  = match small with
    | [] -> ()
    | _ :: s ->
      (match f with
       | [] -> ()
       | y :: f' -> at_least_cons y f' s)

let rec at_least_drop (#a #b:Type) (fuel:list a) (l:list b)
  : Lemma (ensures at_least fuel (drop_by fuel l)) (decreases l)
  = match l with
    | [] -> at_least_refl fuel
    | _ :: t ->
      (match fuel with
       | [] -> ()
       | x :: f ->
         at_least_drop f t;
         at_least_cons x f (drop_by f t))

(* THE FUEL LEMMA. A completed walk is the same list, and still complete, under any longer fuel. *)
let rec anc_stable (#op:eqtype) (d:dag op) (f big:list (node op)) (id:string)
  : Lemma (requires walk_ok d f id /\ at_least big f)
          (ensures ancestors_of d big id == ancestors_of d f id /\ walk_ok d big id)
          (decreases %[f; (0 <: nat); ([] <: list string)])
  = match f with
    | [] -> (match big with | [] -> () | _ :: _ -> ())
    | _ :: f' ->
      (match big with
       | [] -> ()
       | _ :: big' ->
         (match lookup d.nodes id with
          | Missing -> ()
          | Found n -> anc_all_stable d f' big' n.nparents))

and anc_all_stable (#op:eqtype) (d:dag op) (f big:list (node op)) (ids:list string)
  : Lemma (requires walk_all_ok d f ids /\ at_least big f)
          (ensures ancestors_all d big ids == ancestors_all d f ids /\ walk_all_ok d big ids)
          (decreases %[f; (1 <: nat); ids])
  = match ids with
    | [] -> ()
    | p :: t ->
      anc_stable d f big p;
      anc_all_stable d f big t

(* ---- 14.2 the closure as a node set, and the delta under the drain ---- *)

(* F#: the `anc` set of `drainClosure`, as the nodes it names — the set the frontier loop runs over. *)
let rec closure_nodes (#op:eqtype) (ns:list (node op)) (closure:list string)
  : Tot (list (node op)) =
  match ns with
  | [] -> []
  | n :: t -> if mem n.nid closure then n :: closure_nodes t closure else closure_nodes t closure

(* F#: `drainClosure dag [ head ]` — the closure, then section 13's drain over exactly that node set. *)
let topo_drain (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (head:string) : Tot (list string) =
  drain_order lt kfuel (closure_nodes d.nodes (ancestors_of d fuel head))

(* F#: `Dag.between`, with the order production actually computes rather than section 11's
   parent-walk reversal. `between` is this function wherever the closure is a spine
   (`spine_drain_is_the_parent_walk`); over a merged head only this one is production's. *)
let between_drained (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (base_id:string) (head:string) : Tot (list (node op)) =
  between_ordered d fuel base_id (topo_drain lt kfuel d fuel head)

(* F#: `Dag.betweenOps`. *)
let between_ops_drained (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (base_id:string) (head:string) : Tot (list op) =
  ops_of (between_drained lt kfuel d fuel base_id head)

let rec mem_closure_nodes (#op:eqtype) (ns:list (node op)) (closure:list string) (n:node op)
  : Lemma (ensures mem n (closure_nodes ns closure) == (mem n ns && mem n.nid closure))
  = match ns with
    | [] -> ()
    | _ :: t -> mem_closure_nodes t closure n

let rec mem_ids_closure_nodes (#op:eqtype) (ns:list (node op)) (closure:list string) (x:string)
  : Lemma (ensures mem x (ids_of (closure_nodes ns closure)) == (mem x (ids_of ns) && mem x closure))
  = match ns with
    | [] -> ()
    | _ :: t -> mem_ids_closure_nodes t closure x

let rec distinct_closure_nodes (#op:eqtype) (ns:list (node op)) (closure:list string)
  : Lemma (requires distinct (ids_of ns)) (ensures distinct (ids_of (closure_nodes ns closure)))
  = match ns with
    | [] -> ()
    | n :: t ->
      distinct_closure_nodes t closure;
      mem_ids_closure_nodes t closure n.nid

let rec lookup_found (#op:eqtype) (ns:list (node op)) (id:string) (n:node op)
  : Lemma (requires lookup ns id == Found n)
          (ensures n.nid == id /\ mem n ns /\ mem id (ids_of ns))
  = match ns with
    | [] -> ()
    | m :: t -> if m.nid = id then () else lookup_found t id n

(* ---- 14.3 list facts: `before` and `distinct` survive the difference ---- *)

let rec before_diff (x y:string) (ord a:list string)
  : Lemma (requires before x y ord /\ not (mem x a) /\ not (mem y a))
          (ensures before x y (diff ord a))
          (decreases ord)
  = match ord with
    | [] -> ()
    | h :: t ->
      if h = x then mem_diff y t a
      else if h = y then ()
      else before_diff x y t a

let rec distinct_diff (ord a:list string)
  : Lemma (requires distinct ord) (ensures distinct (diff ord a)) (decreases ord)
  = match ord with
    | [] -> ()
    | h :: t ->
      distinct_diff t a;
      mem_diff h t a

let rec follows_spine_diff (r:string) (ids ord a:list string)
  : Lemma (requires follows_spine r ids ord /\ not (mem r a) /\
                    (forall (x:string). mem x ids ==> not (mem x a)))
          (ensures follows_spine r ids (diff ord a))
          (decreases ids)
  = match ids with
    | [] -> ()
    | x :: t ->
      before_diff r x ord a;
      follows_spine_diff x t ord a

(* A spine whose nodes and root all lie in the drained set follows its parents in any topological
   enumeration of that set. *)
let rec spine_follows_in (#op:eqtype) (cn:list (node op)) (ns:list (node op)) (r:string)
  (ord:list string)
  : Lemma (requires parents_chain ns r /\ mem r (ids_of cn) /\
                    (forall (n:node op). mem n ns ==> mem n cn) /\
                    follows_parents_in cn (ids_of cn) ord)
          (ensures follows_spine r (ids_of ns) ord)
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: t ->
      assert (mem n cn);
      follows_mem cn (ids_of cn) [] ord n;
      mem_ids_of n cn;
      spine_follows_in cn t n.nid ord

(* ---- 14.4 THE THEOREM: `between_merged` ---- *)

(* What the recovery needs of content addressing per lane — `lane_recovers`, read at a merged head.
   Clauses one and two are section 11's. Clause three is the fuel premise (14.1). Clause four is
   section 11's "the base id is not one of the lane's", at a base whose closure is more than
   itself: no lane id is an id the merged head's closure already holds. *)
let merged_recovers (#op:eqtype) (d:dag op) (mint:string -> string -> op -> string)
  (m:node op) (fuel:list (node op)) (ln:lane op) : prop =
  resolves d (chain_rev mint ln.lactor m.nid (rev ln.lops)) /\
  covers fuel (rev ln.lops) /\
  walk_ok d (drop_by fuel (rev ln.lops)) m.nid /\
  (forall (x:string). mem x (ids_of (lane_nodes mint ln.lactor m.nid ln.lops))
                      ==> not (mem x (ancestors_of d fuel m.nid)))

(* The head's closure is the lane, newest first, then the merged head's closure at FULL fuel. *)
let merged_head_closure (#op:eqtype) (d:dag op) (mint:string -> string -> op -> string)
  (actor:string) (m:node op) (l:list op) (fuel:list (node op))
  : Lemma (requires merged_recovers d mint m fuel ({ lactor = actor; lops = l }))
          (ensures ancestors_of d fuel (lane_head mint actor m.nid l)
                   == app (ids_of (chain_rev mint actor m.nid (rev l)))
                          (ancestors_of d fuel m.nid))
  = let rl = rev l in
    ancestors_chain d mint actor m.nid rl fuel;
    at_least_drop fuel rl;
    anc_stable d (drop_by fuel rl) fuel m.nid

(* THEOREM. `Dag.between` from a merged head to a lane head hanging off it is that lane's nodes, in
   append order — under production's own drain, whatever it chose beneath `m`.

   Read it as the calculation it is: the head's closure is the lane then `m`'s closure
   (`merged_head_closure`); the drain over it is total and parent-respecting
   (`drain_total_on_acyclic`); every lane node is in the drained set, so the lane's ids follow one
   another in the drain's order (`spine_follows_in`); the difference against `m`'s closure keeps
   the lane's ids and only those, and keeps their order (`follows_spine_diff`, `distinct_diff`);
   a distinct parent-respecting enumeration of a spine is forced (`spine_ids_forced`, with `m.nid`
   put back at the front as its root); and the lookup returns the nodes (`nodes_for_ids`). *)
#push-options "--z3rlimit 150"
let between_merged (#op:eqtype) (lt:string -> string -> bool) (d:dag op)
  (mint:string -> string -> op -> string) (actor:string) (m:node op) (l:list op)
  (fuel kfuel:list (node op)) (w:list string)
  : Lemma (requires distinct_ids d /\ lookup d.nodes m.nid == Found m /\ Cons? fuel /\
                    merged_recovers d mint m fuel ({ lactor = actor; lops = l }) /\
                    lane_ids_distinct mint m ({ lactor = actor; lops = l }) /\
                    (let cn = closure_nodes d.nodes
                                (ancestors_of d fuel (lane_head mint actor m.nid l)) in
                     covers kfuel (ids_of cn) /\ is_topo_enum cn w))
          (ensures between_drained lt kfuel d fuel m.nid (lane_head mint actor m.nid l)
                   == lane_nodes mint actor m.nid l)
  = let q = m.nid in
    let rl = rev l in
    let c = chain_rev mint actor q rl in
    let lns = lane_nodes mint actor q l in
    let h = lane_head mint actor q l in
    let a = ancestors_of d fuel q in
    let hc = ancestors_of d fuel h in
    let cn = closure_nodes d.nodes hc in
    let ord = drain_order lt kfuel cn in
    let ids = ids_of lns in
    merged_head_closure d mint actor m l fuel;
    ids_of_rev c;
    distinct_closure_nodes d.nodes hc;
    drain_total_on_acyclic lt kfuel cn w;
    (* every lane node, and the merged head, is in the drained set *)
    lookup_found d.nodes q m;
    assert (mem q a);
    mem_ids_closure_nodes d.nodes hc q;
    let aux_n (n:node op) : Lemma (mem n lns ==> mem n cn) =
      if mem n lns then begin
        assert (mem n c);
        lookup_found d.nodes n.nid n;
        mem_ids_of n c;
        mem_closure_nodes d.nodes hc n
      end
    in
    FStar.Classical.forall_intro aux_n;
    parents_chain_lane mint actor q l;
    spine_follows_in cn lns q ord;
    (* the difference keeps exactly the lane's ids *)
    let dl = diff ord a in
    let aux_m (x:string) : Lemma (mem x (q :: dl) == mem x (q :: ids)) =
      mem_ids_closure_nodes d.nodes hc x;
      if mem x ids then begin
        lookup_of_mem_ids lns x;
        let n = Found?._0 (lookup lns x) in
        assert (mem n c);
        lookup_found d.nodes n.nid n
      end
    in
    FStar.Classical.forall_intro aux_m;
    distinct_diff ord a;
    (match ids with
     | [] -> ()
     | x1 :: t ->
       follows_spine_diff x1 t ord a;
       follows_spine_cons q x1 t dl);
    spine_ids_forced q ids (q :: dl);
    nodes_for_ids d lns
#pop-options

(* Section 13's premises for one head's drain: fuel for the closure, and an acyclicity witness. *)
let head_drains (#op:eqtype) (d:dag op) (fuel kfuel:list (node op)) (head:string)
  (w:list string) : Tot bool =
  let cn = closure_nodes d.nodes (ancestors_of d fuel head) in
  covers kfuel (ids_of cn) && is_topo_enum cn w

#push-options "--z3rlimit 100"
let between_ops_merged (#op:eqtype) (lt:string -> string -> bool) (d:dag op)
  (mint:string -> string -> op -> string) (actor:string) (m:node op) (l:list op)
  (fuel kfuel:list (node op)) (w:list string)
  : Lemma (requires distinct_ids d /\ lookup d.nodes m.nid == Found m /\ Cons? fuel /\
                    merged_recovers d mint m fuel ({ lactor = actor; lops = l }) /\
                    lane_ids_distinct mint m ({ lactor = actor; lops = l }) /\
                    head_drains d fuel kfuel (lane_head mint actor m.nid l) w)
          (ensures between_ops_drained lt kfuel d fuel m.nid (lane_head mint actor m.nid l) == l)
  = between_merged lt d mint actor m l fuel kfuel w;
    ops_of_rev (chain_rev mint actor m.nid (rev l));
    ops_of_chain_rev mint actor m.nid (rev l);
    rev_rev l
#pop-options

(* ---- 14.5 the fold over a merged head ---- *)

let rec deltas_of_drained (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (base_id:string) (heads:list string)
  : Tot (list (list op)) =
  match heads with
  | [] -> []
  | h :: t -> between_ops_drained lt kfuel d fuel base_id h
              :: deltas_of_drained lt kfuel d fuel base_id t

let reconcile_many_drained (#op:eqtype) (fp:op -> footprint) (lt:string -> string -> bool)
  (kfuel:list (node op)) (d:dag op) (fuel:list (node op)) (base_id:string) (heads:list string)
  : Tot (outcome (list op) (list (conflict op))) =
  reconcile_many fp (deltas_of_drained lt kfuel d fuel base_id heads)

let fold_once_drained (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint) (lt:string -> string -> bool)
  (kfuel:list (node op)) (d:dag op) (fuel:list (node op)) (base_id:string) (s0:state)
  (heads:list string) : Tot (lane_outcome op state rej) =
  fold_once apply fp s0 (deltas_of_drained lt kfuel d fuel base_id heads)

(* Every second-round lane recovers, and every head drains — one witness per head, in order. *)
let rec lanes_merged_ok (#op:eqtype) (d:dag op) (mint:string -> string -> op -> string)
  (m:node op) (fuel kfuel:list (node op)) (lanes:list (lane op)) (ws:list (list string))
  : Tot prop (decreases lanes) =
  match lanes with
  | [] -> (match ws with | [] -> True | _ :: _ -> False)
  | ln :: lt' ->
    (match ws with
     | [] -> False
     | w :: wt ->
       merged_recovers d mint m fuel ln /\
       lane_ids_distinct mint m ln /\
       head_drains d fuel kfuel (lane_head mint ln.lactor m.nid ln.lops) w /\
       lanes_merged_ok d mint m fuel kfuel lt' wt)

let rec deltas_of_merged_lanes (#op:eqtype) (lt:string -> string -> bool) (d:dag op)
  (mint:string -> string -> op -> string) (m:node op) (fuel kfuel:list (node op))
  (lanes:list (lane op)) (ws:list (list string))
  : Lemma (requires distinct_ids d /\ lookup d.nodes m.nid == Found m /\ Cons? fuel /\
                    lanes_merged_ok d mint m fuel kfuel lanes ws)
          (ensures deltas_of_drained lt kfuel d fuel m.nid (lane_heads mint m.nid lanes)
                   == lane_ops lanes)
          (decreases lanes)
  = match lanes with
    | [] -> ()
    | ln :: lt' ->
      (match ws with
       | [] -> ()
       | w :: wt ->
         between_ops_merged lt d mint ln.lactor m ln.lops fuel kfuel w;
         deltas_of_merged_lanes lt d mint m fuel kfuel lt' wt)

(* THEOREM. `Dag.reconcileMany` FROM a merged head equals the deltas-first `reconcile_many` on the
   lanes that were appended — section 11's `reconcile_many_dag_eq`, over the shape a clone folds
   after a pull, under the drain production runs. *)
let reconcile_many_merged_eq (#op:eqtype) (fp:op -> footprint) (lt:string -> string -> bool)
  (d:dag op) (mint:string -> string -> op -> string) (m:node op) (fuel kfuel:list (node op))
  (lanes:list (lane op)) (ws:list (list string))
  : Lemma (requires distinct_ids d /\ lookup d.nodes m.nid == Found m /\ Cons? fuel /\
                    lanes_merged_ok d mint m fuel kfuel lanes ws)
          (ensures reconcile_many_drained fp lt kfuel d fuel m.nid (lane_heads mint m.nid lanes)
                   == reconcile_many fp (lane_ops lanes))
  = deltas_of_merged_lanes lt d mint m fuel kfuel lanes ws

let fold_once_merged_eq (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint) (lt:string -> string -> bool)
  (s0:state) (d:dag op) (mint:string -> string -> op -> string) (m:node op)
  (fuel kfuel:list (node op)) (lanes:list (lane op)) (ws:list (list string))
  : Lemma (requires distinct_ids d /\ lookup d.nodes m.nid == Found m /\ Cons? fuel /\
                    lanes_merged_ok d mint m fuel kfuel lanes ws)
          (ensures fold_once_drained apply fp lt kfuel d fuel m.nid s0
                     (lane_heads mint m.nid lanes)
                   == fold_once apply fp s0 (lane_ops lanes))
  = deltas_of_merged_lanes lt d mint m fuel kfuel lanes ws

(* ---- 14.6 the key `Dag.mergeBase` ranks by, and the merged head is strictly the deepest ---- *)

(* F#: a `Set<string>` holds each id once; the model's closure is a walk and holds an id once per
   path to it. De-duplicated, its length is `Set.count`. *)
let rec dedup (l:list string) : Tot (list string) =
  match l with
  | [] -> []
  | x :: t -> if mem x t then dedup t else x :: dedup t

(* F#: `Set.count a < Set.count b`, as a comparison of two lists' lengths with no integer in it. *)
let rec shorter (#a #b:Type) (l:list a) (m:list b) : Tot bool (decreases m) =
  match m with
  | [] -> false
  | _ :: mt -> (match l with | [] -> true | _ :: lt' -> shorter lt' mt)

let closure_set (#op:eqtype) (d:dag op) (fuel:list (node op)) (id:string) : Tot (list string) =
  dedup (ancestors_of d fuel id)

(* F#: the `List.maxBy` key `(Set.count (ancestorsOf dag id), id)`, as the strict comparison
   "x's key is greater than y's" — closure size first, the id order second. *)
let deeper (#op:eqtype) (lt:string -> string -> bool) (d:dag op) (fuel:list (node op))
  (x y:string) : Tot bool =
  let cx = closure_set d fuel x in
  let cy = closure_set d fuel y in
  if shorter cy cx then true
  else if shorter cx cy then false
  else lt y x

(* F#: `List.maxBy`. Keys are distinct wherever ids are, so first-of-equals is never consulted. *)
let rec max_by (better:string -> string -> bool) (best:string) (l:list string)
  : Tot string (decreases l) =
  match l with
  | [] -> best
  | x :: t -> if better x best then max_by better x t else max_by better best t

let rec mem_dedup (l:list string) (x:string)
  : Lemma (ensures mem x (dedup l) == mem x l)
  = match l with
    | [] -> ()
    | _ :: t -> mem_dedup t x

let rec distinct_dedup (l:list string) : Lemma (ensures distinct (dedup l))
  = match l with
    | [] -> ()
    | x :: t -> distinct_dedup t; mem_dedup t x

let rec shorter_asym (#a #b:Type) (l:list a) (m:list b)
  : Lemma (requires shorter l m) (ensures not (shorter m l)) (decreases m)
  = match m with
    | [] -> ()
    | _ :: mt -> (match l with | [] -> () | _ :: lt' -> shorter_asym lt' mt)

let rec shorter_remove_first_cons (x h:string) (t mt:list string)
  : Lemma (requires mem x mt)
          (ensures shorter t (h :: remove_first x mt) == shorter t mt)
          (decreases t)
  = match t with
    | [] -> ()
    | _ :: t' ->
      (match mt with
       | [] -> ()
       | h2 :: mt2 -> if h2 = x then () else shorter_remove_first_cons x h2 t' mt2)

(* A distinct list strictly inside another is strictly shorter — the counting step of "a strictly
   deeper node has a strictly larger closure", with `remove_first` standing in for subtraction. *)
let rec pigeon (l m:list string) (y:string)
  : Lemma (requires distinct l /\ distinct m /\ (forall (x:string). mem x l ==> mem x m) /\
                    mem y m /\ not (mem y l))
          (ensures shorter l m)
          (decreases l)
  = match l with
    | [] -> ()
    | x :: t ->
      let m' = remove_first x m in
      distinct_remove_first m x;
      let aux (z:string) : Lemma (mem z m' == (mem z m && not (z = x))) = mem_remove_first m x z in
      FStar.Classical.forall_intro aux;
      pigeon t m' y;
      (match m with
       | [] -> ()
       | h :: mt -> if h = x then () else shorter_remove_first_cons x h t mt)

let rec max_by_is (better:string -> string -> bool) (best:string) (l:list string) (q:string)
  : Lemma (requires mem q (best :: l) /\
                    (forall (y:string). mem y (best :: l) /\ y =!= q ==>
                                        (better q y /\ not (better y q))))
          (ensures max_by better best l == q)
          (decreases l)
  = match l with
    | [] -> ()
    | x :: t ->
      if better x best then max_by_is better x t q
      else max_by_is better best t q

(* The closure is TRANSITIVE: an ancestor's ancestors are ancestors. Stated across two fuels,
   because the inner walk starts higher up than the outer one reached it. *)
let rec anc_trans (#op:eqtype) (d:dag op) (f big:list (node op)) (id x y:string)
  : Lemma (requires walk_ok d f id /\ at_least big f /\ mem x (ancestors_of d f id) /\
                    mem y (ancestors_of d big x))
          (ensures mem y (ancestors_of d f id))
          (decreases %[f; (0 <: nat); ([] <: list string)])
  = match f with
    | [] -> ()
    | _ :: f' ->
      (match lookup d.nodes id with
       | Missing -> ()
       | Found n ->
         if x = id then anc_stable d f big id
         else begin
           at_least_tail big f;
           anc_all_trans d f' big n.nparents x y
         end)

and anc_all_trans (#op:eqtype) (d:dag op) (f big:list (node op)) (ids:list string) (x y:string)
  : Lemma (requires walk_all_ok d f ids /\ at_least big f /\ mem x (ancestors_all d f ids) /\
                    mem y (ancestors_of d big x))
          (ensures mem y (ancestors_all d f ids))
          (decreases %[f; (1 <: nat); ids])
  = match ids with
    | [] -> ()
    | p :: t ->
      if mem x (ancestors_of d f p) then anc_trans d f big p x y
      else anc_all_trans d f big t x y

(* ACYCLICITY in the form `mergeBase` consumes: the merged head is not its own proper ancestor.
   `off_cycle_of_witness` (14.7) derives it from section 13's witness. *)
let off_cycle (#op:eqtype) (d:dag op) (fuel:list (node op)) (m:node op) : prop =
  forall (x:string). mem x (ancestors_of d fuel m.nid) /\ x =!= m.nid
                     ==> not (mem m.nid (ancestors_of d fuel x))

(* Production's docstring sentence, proved: every proper ancestor of `m` has a strictly smaller
   closure than `m`, so `m`'s key beats it on the FIRST component and the id order is not reached. *)
let strictly_deeper (#op:eqtype) (lt:string -> string -> bool) (d:dag op)
  (fuel:list (node op)) (m:node op) (y:string)
  : Lemma (requires lookup d.nodes m.nid == Found m /\ Cons? fuel /\ walk_ok d fuel m.nid /\
                    off_cycle d fuel m /\ mem y (ancestors_of d fuel m.nid) /\ y =!= m.nid)
          (ensures deeper lt d fuel m.nid y /\ not (deeper lt d fuel y m.nid))
  = let q = m.nid in
    let s = ancestors_of d fuel y in
    let mm = ancestors_of d fuel q in
    at_least_refl fuel;
    let aux (z:string) : Lemma (mem z (dedup s) ==> mem z (dedup mm)) =
      mem_dedup s z;
      mem_dedup mm z;
      if mem z s then anc_trans d fuel fuel q y z
    in
    FStar.Classical.forall_intro aux;
    distinct_dedup s;
    distinct_dedup mm;
    mem_dedup s q;
    mem_dedup mm q;
    assert (mem q mm);
    pigeon (dedup s) (dedup mm) q;
    shorter_asym (dedup s) (dedup mm)

(* The common ancestors of two lane heads off a merged head are exactly `m`'s closure (the lanes
   share no id with each other or with it), and `m` is strictly the deepest of them. What
   `Dag.mergeBase` finding the divergence point stands on: the theorem is
   `DagPrimitives.merge_base_now_is_divergence`, stated over the `mergeBase` that runs (Phase 534;
   until then this lemma was that theorem, about a second model of the function). *)
#push-options "--z3rlimit 150"
let merged_common_is_the_closure (#op:eqtype) (lt:string -> string -> bool) (d:dag op)
  (mint:string -> string -> op -> string) (m:node op) (fuel:list (node op))
  (ln1 ln2:lane op)
  : Lemma (requires lookup d.nodes m.nid == Found m /\ Cons? fuel /\
                    merged_recovers d mint m fuel ln1 /\ merged_recovers d mint m fuel ln2 /\
                    off_cycle d fuel m /\
                    (forall (x:string).
                       mem x (ids_of (lane_nodes mint ln1.lactor m.nid ln1.lops)) ==>
                       not (mem x (ids_of (lane_nodes mint ln2.lactor m.nid ln2.lops)))))
          (ensures (let q = m.nid in
                    let a = ancestors_of d fuel q in
                    let common = inter (ancestors_of d fuel (lane_head mint ln1.lactor q ln1.lops))
                                       (ancestors_of d fuel (lane_head mint ln2.lactor q ln2.lops)) in
                    mem q a /\ (forall (x:string). mem x common == mem x a) /\
                    (forall (y:string). mem y a /\ y =!= q ==>
                                        (deeper lt d fuel q y /\ not (deeper lt d fuel y q)))))
  = let q = m.nid in
    let a = ancestors_of d fuel q in
    let c1 = chain_rev mint ln1.lactor q (rev ln1.lops) in
    let c2 = chain_rev mint ln2.lactor q (rev ln2.lops) in
    let h1 = ancestors_of d fuel (lane_head mint ln1.lactor q ln1.lops) in
    let h2 = ancestors_of d fuel (lane_head mint ln2.lactor q ln2.lops) in
    merged_head_closure d mint ln1.lactor m ln1.lops fuel;
    merged_head_closure d mint ln2.lactor m ln2.lops fuel;
    ids_of_rev c1;
    ids_of_rev c2;
    at_least_drop fuel (rev ln1.lops);
    anc_stable d (drop_by fuel (rev ln1.lops)) fuel q;
    let common = inter h1 h2 in
    let aux_c (x:string) : Lemma (mem x common == mem x a) = () in
    FStar.Classical.forall_intro aux_c;
    assert (mem q a);
    let aux_d (y:string)
      : Lemma (mem y a /\ y =!= q ==> (deeper lt d fuel q y /\ not (deeper lt d fuel y q))) =
      if mem y a && y <> q then strictly_deeper lt d fuel m y
    in
    FStar.Classical.forall_intro aux_d
#pop-options

(* ---- 14.7 the bridge: an acyclicity WITNESS puts the merged head off every cycle ---- *)

let rec before_trans (x y z:string) (l:list string)
  : Lemma (requires before x y l /\ before y z l /\ distinct l) (ensures before x z l)
          (decreases l)
  = match l with
    | [] -> ()
    | h :: t ->
      if h = x then before_mem_snd y z t
      else if h = y then ()
      else if h = z then ()
      else before_trans x y z t

let rec before_asym (x y:string) (l:list string)
  : Lemma (requires before x y l /\ before y x l /\ distinct l) (ensures False) (decreases l)
  = match l with
    | [] -> ()
    | h :: t ->
      if h = x then ()
      else if h = y then ()
      else before_asym x y t

let rec all_before_mem (ps:list string) (child:string) (ord:list string) (p:string)
  : Lemma (requires all_before_or_emitted ps child [] ord /\ mem p ps)
          (ensures before p child ord)
          (decreases ps)
  = match ps with
    | [] -> ()
    | h :: t -> if h = p then () else all_before_mem t child ord p

let rec mem_parents_in (ps closure:list string) (p:string)
  : Lemma (ensures mem p (parents_in ps closure) == (mem p ps && mem p closure))
  = match ps with
    | [] -> ()
    | _ :: t -> mem_parents_in t closure p

let rec anc_all_incl (#op:eqtype) (d:dag op) (f:list (node op)) (ids:list string) (p y:string)
  : Lemma (requires mem p ids /\ mem y (ancestors_of d f p))
          (ensures mem y (ancestors_all d f ids))
          (decreases ids)
  = match ids with
    | [] -> ()
    | h :: t -> if h = p then () else anc_all_incl d f t p y

let rec walk_ok_anc (#op:eqtype) (d:dag op) (f big:list (node op)) (id x:string)
  : Lemma (requires walk_ok d f id /\ at_least big f /\ mem x (ancestors_of d f id))
          (ensures walk_ok d big x)
          (decreases %[f; (0 <: nat); ([] <: list string)])
  = match f with
    | [] -> ()
    | _ :: f' ->
      (match lookup d.nodes id with
       | Missing -> ()
       | Found n ->
         if x = id then anc_stable d f big id
         else begin
           at_least_tail big f;
           walk_ok_anc_all d f' big n.nparents x
         end)

and walk_ok_anc_all (#op:eqtype) (d:dag op) (f big:list (node op)) (ids:list string) (x:string)
  : Lemma (requires walk_all_ok d f ids /\ at_least big f /\ mem x (ancestors_all d f ids))
          (ensures walk_ok d big x)
          (decreases %[f; (1 <: nat); ids])
  = match ids with
    | [] -> ()
    | p :: t ->
      if mem x (ancestors_of d f p) then walk_ok_anc d f big p x
      else walk_ok_anc_all d f big t x

let rec walk_all_ok_mem (#op:eqtype) (d:dag op) (f:list (node op)) (ids:list string) (p:string)
  : Lemma (requires walk_all_ok d f ids /\ mem p ids) (ensures walk_ok d f p) (decreases ids)
  = match ids with
    | [] -> ()
    | h :: t -> if h = p then () else walk_all_ok_mem d f t p

(* What `anc_before` carries unchanged down its walk: `m`'s closure walked to completion, and a
   distinct parent-respecting enumeration of it. *)
let witness_ctx (#op:eqtype) (d:dag op) (fuel:list (node op)) (q:string) (wm:list string)
  : prop =
  walk_ok d fuel q /\ distinct wm /\
  (let cnm = closure_nodes d.nodes (ancestors_of d fuel q) in
   follows_parents_in cnm (ids_of cnm) wm)

(* An ancestor stands BEFORE its descendant in any topological enumeration of a closure that holds
   them both: one `follows_parents_in` step per edge of the walk, chained by `before_trans`. *)
#push-options "--z3rlimit 200 --fuel 2 --ifuel 2"
let rec anc_before (#op:eqtype) (d:dag op) (fuel f:list (node op)) (q:string)
  (wm:list string) (id z:string)
  : Lemma (requires witness_ctx d fuel q wm /\ walk_ok d f id /\ at_least fuel f /\
                    mem id (ancestors_of d fuel q) /\ mem z (ancestors_of d f id) /\ z =!= id)
          (ensures before z id wm)
          (decreases %[f; (0 <: nat); ([] <: list string)])
  = match f with
    | [] -> ()
    | _ :: f' ->
      (match lookup d.nodes id with
       | Missing -> ()
       | Found n ->
         lookup_found d.nodes id n;
         anc_stable d f fuel id;
         at_least_tail fuel f;
         anc_all_before d fuel f' q wm n n.nparents z)

and anc_all_before (#op:eqtype) (d:dag op) (fuel f:list (node op)) (q:string)
  (wm:list string) (n:node op) (ids:list string) (z:string)
  : Lemma (requires witness_ctx d fuel q wm /\ lookup d.nodes n.nid == Found n /\
                    mem n.nid (ancestors_of d fuel q) /\ walk_ok d fuel n.nid /\
                    (forall (p:string). mem p ids ==> mem p n.nparents) /\
                    walk_all_ok d f ids /\ at_least fuel f /\
                    mem z (ancestors_all d f ids))
          (ensures before z n.nid wm)
          (decreases %[f; (1 <: nat); ids])
  = match ids with
    | [] -> ()
    | p :: t ->
      if mem z (ancestors_of d f p) then begin
        let a = ancestors_of d fuel q in
        let cnm = closure_nodes d.nodes a in
        (match f with
         | [] -> ()
         | _ :: _ ->
           (match lookup d.nodes p with
            | Missing -> ()
            | Found pn ->
              lookup_found d.nodes p pn;
              (match fuel with
               | [] -> ()
               | _ :: fuel' ->
                 assert (walk_all_ok d fuel' n.nparents);
                 walk_all_ok_mem d fuel' n.nparents p;
                 (match fuel' with
                  | [] -> ()
                  | _ :: _ ->
                    assert (mem p (ancestors_of d fuel' p));
                    anc_all_incl d fuel' n.nparents p p));
              assert (mem p (ancestors_of d fuel n.nid));
              at_least_refl fuel;
              anc_trans d fuel fuel q n.nid p;
              mem_ids_closure_nodes d.nodes a p;
              lookup_found d.nodes n.nid n;
              mem_closure_nodes d.nodes a n;
              follows_mem cnm (ids_of cnm) [] wm n;
              mem_parents_in n.nparents (ids_of cnm) p;
              all_before_mem (parents_in n.nparents (ids_of cnm)) n.nid wm p;
              if z = p then ()
              else begin
                anc_before d fuel f q wm p z;
                before_trans z p n.nid wm
              end))
      end
      else anc_all_before d fuel f q wm n t z
#pop-options

(* THE BRIDGE. Section 13's acyclicity witness gives `off_cycle`: were `m` a proper ancestor of one
   of its own ancestors, each would stand before the other in a distinct list. *)
let off_cycle_of_witness (#op:eqtype) (d:dag op) (fuel:list (node op)) (m:node op)
  (wm:list string)
  : Lemma (requires walk_ok d fuel m.nid /\
                    is_topo_enum (closure_nodes d.nodes (ancestors_of d fuel m.nid)) wm)
          (ensures off_cycle d fuel m)
  = let q = m.nid in
    at_least_refl fuel;
    let aux (x:string)
      : Lemma (mem x (ancestors_of d fuel q) /\ x =!= q ==> not (mem q (ancestors_of d fuel x))) =
      if mem x (ancestors_of d fuel q) && x <> q && mem q (ancestors_of d fuel x) then begin
        anc_before d fuel fuel q wm q x;
        walk_ok_anc d fuel fuel q x;
        anc_before d fuel fuel q wm x q;
        before_asym x q wm
      end
    in
    FStar.Classical.forall_intro aux

(* ======================================================================================
   15. The reconcile's PARTITION (Phase 300) — `reconcile_sound` and the order-free fold.

   WHAT IS MODELLED. `Dag.reconcile` and `Dag.reconcileMany` no longer take each lane's delta as
   `between base head` and concatenate — which applied history two heads SHARE twice (a
   fast-forward, a duplicate head, a criss-cross over `mergeBase`'s tie-break). They PARTITION the
   region above the base by node id, and this section is that partition clause for clause (F#:
   the private `region` in DagOpStream.fs):

     - `owners x cs` — how many heads' closures hold `x` (F#: `owners`);
     - `shared_ids`  — the drain order, kept to the ids above the base held by two or more
                       closures (F#: `Shared`);
     - `excl_ids`    — one head's exclusive delta: above the base, in its closure, held by no other
                       (F#: one `Exclusive` entry);
     - `excl_lanes`  — one exclusive delta per closure, in head order (F#: `Exclusive`);
     - `reconcile_ids` — the ids of the `Ok` script: `shared ++ exclusive_1 ++ … ++ exclusive_n`.

   The closures (`cs`, one per deduplicated head) and the base closure (`base_c`) are the lists
   `ancestors_of` computes (section 11); `order` is the Kahn drain over the union of the heads'
   closures (F#: `drainClosure`), whose distinctness and completeness on an acyclic set are
   section 13's `drain_linear_extension` / `drain_total_on_acyclic`. Nothing here is a parameter
   standing in for the reconcile: the partition is the function production computes.

   WHAT IS PROVED.
     - `reconcile_sound` — for ANY duplicate-free enumeration `order`, the script's ids are
       duplicate-free (each node appears AT MOST ONCE, however the heads overlap, and even when a
       head is named twice), and a node is in the script EXACTLY when it is enumerated, is not in
       the base's closure, and is held by at least one head. So when `order` enumerates the union
       of the closures — the drain's own guarantee — the script is the region above the base, once.
     - `excl_is_exclusive` — every node of a head's exclusive delta is in that head's closure and in
       no other: the conflict sweep compares deltas that genuinely belong to one lane each.
     - `reconcile_fold_order_free` — permuting the heads leaves the shared region IDENTICAL and
       permutes the exclusive lanes; with `fold_confluence_total` the checked fold of those lanes
       is outcome-equivalent from whatever state the shared region reached. The N-lane reconcile
       is arrival-order-invariant from the DAG, under the diamond and nothing else.

   WHAT IS NOT PROVED, AND WHY (the shard's replay premise, refuted). "Replaying the script from
   `replayTo base` equals `replayTo` of a merge of the heads, under the diamond" is FALSE of a
   history whose merged branches do not commute: `replayTo` drains the merged closure by id, a
   tie-break, so such a history has no order-free replay for any script to equal. A fast-forward
   whose later head merged a side branch that writes the cell the shared history writes is the
   witness; `ReconcileShapeTests.fs` pins it. What IS true, and proved above, is the half no
   premise is needed for: each node once, exactly the region, arrival-order-invariant.
   ====================================================================================== *)

(* F#: the `owners` count in `region`. *)
let rec owners (x:string) (cs:list (list string)) : Tot nat =
  match cs with
  | [] -> 0
  | c :: t -> (if mem x c then 1 else 0) + owners x t

(* F#: `region`'s `Shared`. *)
let shared_pred (base_c:list string) (cs:list (list string)) (x:string) : Tot bool =
  not (mem x base_c) && owners x cs >= 2

let shared_ids (order base_c:list string) (cs:list (list string)) : Tot (list string) =
  keep (shared_pred base_c cs) order

(* F#: one entry of `region`'s `Exclusive`. `cs_all` is the whole closure list the count runs over. *)
let excl_pred (base_c:list string) (cs_all:list (list string)) (c:list string) (x:string) : Tot bool =
  not (mem x base_c) && mem x c && owners x cs_all = 1

let excl_ids (order base_c:list string) (cs_all:list (list string)) (c:list string)
  : Tot (list string) =
  keep (excl_pred base_c cs_all c) order

(* F#: `region`'s `Exclusive` — one lane per closure, in head order. *)
let rec excl_lanes (order base_c:list string) (cs_all cs:list (list string))
  : Tot (list (list string)) (decreases cs) =
  match cs with
  | [] -> []
  | c :: t -> excl_ids order base_c cs_all c :: excl_lanes order base_c cs_all t

(* F#: the ids of the `Ok` script, `opsOf dag r.Shared @ List.concat deltas`. *)
let reconcile_ids (order base_c:list string) (cs:list (list string)) : Tot (list string) =
  app (shared_ids order base_c cs) (concat (excl_lanes order base_c cs cs))

(* ---- 15.1 the list algebra ---- *)

let rec distinct_keep (f:string -> bool) (l:list string)
  : Lemma (requires distinct l) (ensures distinct (keep f l))
  = match l with
    | [] -> ()
    | _ :: t -> distinct_keep f t

let rec distinct_app_disjoint (l m:list string)
  : Lemma (requires distinct l /\ distinct m /\ (forall (x:string). mem x l ==> not (mem x m)))
          (ensures distinct (app l m))
  = match l with
    | [] -> ()
    | _ :: t -> distinct_app_disjoint t m

(* ---- 15.2 each node once, exactly the region ---- *)

let rec mem_excl_lanes (order base_c:list string) (cs_all cs:list (list string)) (x:string)
  : Lemma (ensures mem x (concat (excl_lanes order base_c cs_all cs)) <==>
                   (mem x order /\ not (mem x base_c) /\ owners x cs_all = 1 /\ owners x cs >= 1))
          (decreases cs)
  = match cs with
    | [] -> ()
    | _ :: t -> mem_excl_lanes order base_c cs_all t x

let rec distinct_excl_lanes (order base_c:list string) (cs_all cs:list (list string))
  : Lemma (requires distinct order /\ (forall (x:string). owners x cs <= owners x cs_all))
          (ensures distinct (concat (excl_lanes order base_c cs_all cs)))
          (decreases cs)
  = match cs with
    | [] -> ()
    | c :: t ->
      let e = excl_ids order base_c cs_all c in
      let rest = concat (excl_lanes order base_c cs_all t) in
      distinct_keep (excl_pred base_c cs_all c) order;
      distinct_excl_lanes order base_c cs_all t;
      (* a node exclusive to `c` is held once in all; one in a later lane is held by `t` too, so it
         would be held twice by `c :: t` — which `cs_all` bounds from above *)
      let aux (x:string) : Lemma (mem x e ==> not (mem x rest)) =
        mem_excl_lanes order base_c cs_all t x;
        assert (owners x cs <= owners x cs_all);
        assert (owners x cs == (if mem x c then 1 else 0) + owners x t)
      in
      FStar.Classical.forall_intro aux;
      distinct_app_disjoint e rest

(* THEOREM (Phase 300). Each node appears at most once in the reconcile's script, and the script's
   nodes are exactly the enumerated nodes above the base that some head holds — for ANY closures:
   overlapping, nested (a head that is an ancestor of another), or repeated (a head named twice). *)
val reconcile_sound (order base_c:list string) (cs:list (list string))
  : Lemma (requires distinct order)
          (ensures distinct (reconcile_ids order base_c cs) /\
                   (forall (x:string). mem x (reconcile_ids order base_c cs) <==>
                                       (mem x order /\ not (mem x base_c) /\ owners x cs >= 1)))

let reconcile_sound order base_c cs =
  distinct_keep (shared_pred base_c cs) order;
  distinct_excl_lanes order base_c cs cs;
  FStar.Classical.forall_intro (mem_excl_lanes order base_c cs cs);
  distinct_app_disjoint (shared_ids order base_c cs) (concat (excl_lanes order base_c cs cs))

(* A head's exclusive delta is its own: in its closure, in no other. *)
let excl_is_exclusive (order base_c:list string) (cs:list (list string)) (c:list string) (x:string)
  : Lemma (requires mem x (excl_ids order base_c cs c))
          (ensures mem x c /\ owners x cs = 1 /\ not (mem x base_c))
  = ()

(* ---- 15.3 arrival order: the partition is a function of the head SET ---- *)

let rec owners_perm (cs1 cs2:list (list string)) (p:perm (list string) cs1 cs2) (x:string)
  : Lemma (ensures owners x cs1 == owners x cs2) (decreases p)
  = match p with
    | PNil -> ()
    | PSkip _ m1 m2 p' -> owners_perm m1 m2 p' x
    | PSwap _ _ _ -> ()
    | PTrans m1 m2 m3 p12 p23 -> owners_perm m1 m2 p12 x; owners_perm m2 m3 p23 x

let rec excl_lanes_ext (order base_c:list string) (a1 a2 cs:list (list string))
  : Lemma (requires forall (x:string). owners x a1 == owners x a2)
          (ensures excl_lanes order base_c a1 cs == excl_lanes order base_c a2 cs)
          (decreases cs)
  = match cs with
    | [] -> ()
    | c :: t ->
      keep_ext (excl_pred base_c a1 c)
               (excl_pred base_c a2 c) order;
      excl_lanes_ext order base_c a1 a2 t

[@@ noextract_to "FSharp"]  (* proof-only, as `perm` itself *)
let rec perm_excl_lanes (order base_c:list string) (a cs1 cs2:list (list string))
  (p:perm (list string) cs1 cs2)
  : Tot (perm (list string) (excl_lanes order base_c a cs1) (excl_lanes order base_c a cs2))
        (decreases p)
  = match p with
    | PNil -> PNil
    | PSkip c m1 m2 p' ->
      PSkip (excl_ids order base_c a c) (excl_lanes order base_c a m1) (excl_lanes order base_c a m2)
            (perm_excl_lanes order base_c a m1 m2 p')
    | PSwap c1 c2 l ->
      PSwap (excl_ids order base_c a c1) (excl_ids order base_c a c2) (excl_lanes order base_c a l)
    | PTrans m1 m2 m3 p12 p23 ->
      PTrans (excl_lanes order base_c a m1) (excl_lanes order base_c a m2) (excl_lanes order base_c a m3)
             (perm_excl_lanes order base_c a m1 m2 p12) (perm_excl_lanes order base_c a m2 m3 p23)

(* F#: `opsOf dag` — each id to its node's op; here any function of the id. *)
let rec ops_by (#op:eqtype) (opof:string -> op) (ids:list string) : Tot (list op) =
  match ids with
  | [] -> []
  | x :: t -> opof x :: ops_by opof t

let rec lanes_by (#op:eqtype) (opof:string -> op) (ls:list (list string)) : Tot (list (list op)) =
  match ls with
  | [] -> []
  | l :: t -> ops_by opof l :: lanes_by opof t

[@@ noextract_to "FSharp"]  (* proof-only, as `perm` itself *)
let rec perm_lanes_by (#op:eqtype) (opof:string -> op) (l1 l2:list (list string))
  (p:perm (list string) l1 l2)
  : Tot (perm (list op) (lanes_by opof l1) (lanes_by opof l2)) (decreases p)
  = match p with
    | PNil -> PNil
    | PSkip l m1 m2 p' -> PSkip (ops_by opof l) (lanes_by opof m1) (lanes_by opof m2) (perm_lanes_by opof m1 m2 p')
    | PSwap x y l -> PSwap (ops_by opof x) (ops_by opof y) (lanes_by opof l)
    | PTrans m1 m2 m3 p12 p23 ->
      PTrans (lanes_by opof m1) (lanes_by opof m2) (lanes_by opof m3)
             (perm_lanes_by opof m1 m2 p12) (perm_lanes_by opof m2 m3 p23)

(* THEOREM (Phase 300). `Dag.reconcileMany` over the same heads named in two orders: the shared
   region is the SAME list, so it replays to the same state `s`; and from `s` the checked fold of
   the exclusive lanes — interference sweep, lanes-apply test, composed replay — is
   outcome-equivalent. The one hypothesis is the diamond. *)
val reconcile_fold_order_free (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint) (opof:string -> op) (s:state)
  (order base_c:list string) (cs1 cs2:list (list string)) (p:perm (list string) cs1 cs2)
  : Lemma (requires independence_diamond fp apply)
          (ensures shared_ids order base_c cs1 == shared_ids order base_c cs2 /\
                   outcome_equiv (fold_once apply fp s (lanes_by opof (excl_lanes order base_c cs1 cs1)))
                                 (fold_once apply fp s (lanes_by opof (excl_lanes order base_c cs2 cs2))))

let reconcile_fold_order_free #op #state #rej apply fp opof s order base_c cs1 cs2 p =
  FStar.Classical.forall_intro (owners_perm cs1 cs2 p);
  keep_ext (shared_pred base_c cs1)
           (shared_pred base_c cs2) order;
  excl_lanes_ext order base_c cs1 cs2 cs1;
  let pe = perm_excl_lanes order base_c cs2 cs1 cs2 p in
  fold_confluence_total apply fp s (lanes_by opof (excl_lanes order base_c cs2 cs1))
                                   (lanes_by opof (excl_lanes order base_c cs2 cs2))
                                   (perm_lanes_by opof _ _ pe)

(* ======================================================================================
   16. `Dag.tryReplayTo` and the refusing `append` (Phase 309).

   Every store the plane holds is replayed through `Dag.tryReplayTo`, and until this section it
   had no row: the drain beneath it (section 13) and the closure beside it (section 14) were
   theorems, and the fold over them was prose. This section is the fold, clause for clause, and
   the write-side refusal the content-id premise leans on.

   THE REPLAY. F#: `replayClosure` at one root, which `tryReplayTo` is. Three clauses, in
   production's order:
     1. a head the DAG does not hold is `UnknownHead` — the refusal Phase 296 added where the old
        `replayTo` returned the initial state as `Ok`;
     2. otherwise the head's closure is drained (section 14's `topo_drain`: the closure as a node
        set, section 13's id-ordered drain over it), and a drain that did not place every node
        of the closure is cyclic — production's `Cyclic` (Phase 529; `CyclicHistory` before),
        refused when the drain leaves any node of the closure unplaced, which on a
        duplicate-free drain is the membership test below (`drain_complete_is_acyclic` is why
        the two are the same test);
     3. otherwise each drained id's node op is applied in drain order, and the first rejection
        is `Rejected` naming its node.

   THE THREE THEOREMS.
     - `replay_to_unknown_head_is_refused` — `UnknownHead` is returned EXACTLY for a head the DAG
       does not hold: never the initial state, and never for a head it does hold.
     - `replay_to_is_fold_over_drain` — over an id-distinct DAG and a head whose closure is
       acyclic (section 13's witness form, `head_drains`), the replay is the section-3 fold over
       the ops of the nodes the PROVED drain emits, in that order: it never reports a cycle, the
       order is a topological enumeration of the closure, an `Ok` is `replay`'s state and a
       `Rejected` carries `replay`'s rejection.
     - `replay_to_deterministic` — two clones holding the same node set in different orders
       replay any head to the same outcome. This is `drain_deterministic` carried through the
       closure and the lookup, and it is the sentence "every clone replays to the same state"
       production's docstring makes.

   THE APPEND. F#: `addNode` behind `Dag.append` / `Dag.merge`. The DAG is a map from content id
   to node, and until Phase 296 a second node minting a held id REPLACED the first (`Map.add`)
   while `verifyDag` still passed — the shape the `node-ids-distinct` row hid. The model takes
   production's `sameNode` (parents modulo order, actor, encoded op) as a parameter `same`, and:
     - `append_refuses_differing_node` — an id the DAG holds for a node `same` does not accept is
       refused as a collision, and nothing is written;
     - `append_never_replaces` — whatever an accepted append returns, every node the DAG held
       before is the node it holds after, and an id-distinct DAG stays id-distinct. So id
       distinctness is a property of every store built through `append`, not a premise about
       it; what is left of the hash premise is that the hash is injective on the nodes a store
       actually holds, which `verifyDag` re-checks key by key on load.

   WHAT IS NOT CLAIMED. The diagnostic payload beyond the head: the model's `RCyclicHistory`
   names the head, production's `Cyclic` (Phase 529) names every node the drain left unplaced,
   which holds the head — a cyclic closure leaves its own head unplaced — and the differential
   reads exactly that. `replayClosure` over several roots
   (`checkpointFrom`, `Dag.mergeParentsState`): the union drain is the same drain over a larger
   node set, and no theorem here identifies it. That `same` is production's `sameNode`: it is a
   parameter, and the differential is what ties the two.

   NO SMT PATTERNS, for section 11's reason.
   ====================================================================================== *)

(* ---- 16.1 the replay ---- *)

(* F#: `ReplayFault<'Rej>` — `UnknownHead`, `Cyclic` (the head here; every unplaced node there), `Rejected (nodeId, reject)`. *)
type replay_fault (rej:Type) =
  | RUnknownHead : string -> replay_fault rej
  | RCyclicHistory : string -> replay_fault rej
  | RRejected : string -> rej -> replay_fault rej

(* F#: the `go` loop of `replayClosure` — `Map.find id dag.Nodes`, then `w.Apply n.Op`. A drained
   id is always held (it came out of the closure, which is made of held nodes), so the `Missing`
   arm is the model's total match where production's `Map.find` would raise; it is unreachable on
   every input `replay_to` hands it. *)
let rec replay_ids (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (ns:list (node op)) (ids:list string) (s:state)
  : Tot (outcome state (replay_fault rej)) (decreases ids) =
  match ids with
  | [] -> Ok s
  | id :: t ->
    (match lookup ns id with
     | Missing -> replay_ids apply ns t s
     | Found n ->
       (match apply n.nop s with
        | Ok s' -> replay_ids apply ns t s'
        | Error e -> Error (RRejected id e)))

(* F#: `Dag.tryReplayTo w state0 dag headId`. *)
let replay_to (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (lt:string -> string -> bool)
  (kfuel:list (node op)) (d:dag op) (fuel:list (node op)) (s0:state) (head:string)
  : Tot (outcome state (replay_fault rej)) =
  match lookup d.nodes head with
  | Missing -> Error (RUnknownHead head)
  | Found _ ->
    let cn = closure_nodes d.nodes (ancestors_of d fuel head) in
    let order = drain_order lt kfuel cn in
    if sub_ids (ids_of cn) order then replay_ids apply d.nodes order s0
    else Error (RCyclicHistory head)

let is_unknown_head (#state #rej:Type) (r:outcome state (replay_fault rej)) : Tot bool =
  match r with
  | Error (RUnknownHead _) -> true
  | _ -> false

let is_cyclic (#state #rej:Type) (r:outcome state (replay_fault rej)) : Tot bool =
  match r with
  | Error (RCyclicHistory _) -> true
  | _ -> false

(* THEOREM. `UnknownHead` exactly for a head the DAG does not hold, and naming it. *)
let replay_to_unknown_head_is_refused (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (lt:string -> string -> bool)
  (kfuel:list (node op)) (d:dag op) (fuel:list (node op)) (s0:state) (head:string)
  : Lemma (ensures
            (Missing? (lookup d.nodes head) ==>
               replay_to apply lt kfuel d fuel s0 head == Error (RUnknownHead head)) /\
            (is_unknown_head (replay_to apply lt kfuel d fuel s0 head) ==
               Missing? (lookup d.nodes head)))
  = match lookup d.nodes head with
    | Missing -> ()
    | Found _ ->
      let cn = closure_nodes d.nodes (ancestors_of d fuel head) in
      let order = drain_order lt kfuel cn in
      if sub_ids (ids_of cn) order then begin
        let rec never (ids:list string) (s:state)
          : Lemma (ensures not (is_unknown_head (replay_ids apply d.nodes ids s))) (decreases ids)
          = match ids with
            | [] -> ()
            | id :: t ->
              (match lookup d.nodes id with
               | Missing -> never t s
               | Found n -> (match apply n.nop s with | Ok s' -> never t s' | Error _ -> ()))
        in
        never order s0
      end
      else ()

(* The node-level loop is the section-3 fold over the ops of the same nodes: the same state on
   `Ok`, the same rejection under `Rejected`, and nothing else. *)
let rec replay_ids_is_replay (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (d:dag op) (ids:list string) (s:state)
  : Lemma (ensures
            (match replay_ids apply d.nodes ids s, replay apply (ops_of (nodes_for d ids)) s with
             | Ok a, Ok b -> a == b
             | Error (RRejected _ e), Error e' -> e == e'
             | _, _ -> False))
          (decreases ids)
  = match ids with
    | [] -> ()
    | id :: t ->
      (match lookup d.nodes id with
       | Missing -> replay_ids_is_replay apply d t s
       | Found n ->
         (match apply n.nop s with
          | Ok s' -> replay_ids_is_replay apply d t s'
          | Error _ -> ()))

(* THEOREM. Over an id-distinct DAG, a head it holds whose closure is acyclic replays as the fold
   over the proved drain: no cycle is reported, the order is a topological enumeration of the
   closure, and the outcome is `replay`'s over the ops of the drained nodes. *)
#push-options "--z3rlimit 100"
let replay_to_is_fold_over_drain (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (lt:string -> string -> bool)
  (kfuel:list (node op)) (d:dag op) (fuel:list (node op)) (s0:state) (head:string)
  (w:list string)
  : Lemma (requires distinct_ids d /\ Found? (lookup d.nodes head) /\
                    head_drains d fuel kfuel head w)
          (ensures (let order = topo_drain lt kfuel d fuel head in
                    let cn = closure_nodes d.nodes (ancestors_of d fuel head) in
                    is_topo_enum cn order /\
                    replay_to apply lt kfuel d fuel s0 head == replay_ids apply d.nodes order s0 /\
                    not (is_cyclic (replay_to apply lt kfuel d fuel s0 head)) /\
                    (match replay_to apply lt kfuel d fuel s0 head,
                           replay apply (ops_of (nodes_for d order)) s0 with
                     | Ok a, Ok b -> a == b
                     | Error (RRejected _ e), Error e' -> e == e'
                     | _, _ -> False)))
  = let closure = ancestors_of d fuel head in
    let cn = closure_nodes d.nodes closure in
    distinct_closure_nodes d.nodes closure;
    drain_total_on_acyclic lt kfuel cn w;
    let order = drain_order lt kfuel cn in
    sub_ids_of_mem (ids_of cn) order;
    replay_ids_is_replay apply d order s0;
    let rec never (ids:list string) (s:state)
      : Lemma (ensures not (is_cyclic (replay_ids apply d.nodes ids s))) (decreases ids)
      = match ids with
        | [] -> ()
        | id :: t ->
          (match lookup d.nodes id with
           | Missing -> never t s
           | Found n -> (match apply n.nop s with | Ok s' -> never t s' | Error _ -> ()))
    in
    never order s0
#pop-options

(* ---- 16.2 determinism: the node set, not its order ---- *)

[@@ noextract_to "FSharp"]  (* proof-only, as `perm` itself *)
let rec perm_refl (#a:Type) (l:list a) : Tot (perm a l l) (decreases l) =
  match l with
  | [] -> PNil
  | x :: t -> PSkip x t t (perm_refl t)

[@@ noextract_to "FSharp"]  (* proof-only, as `perm` itself *)
let rec closure_nodes_perm (#op:eqtype) (r1 r2:list (node op)) (p:perm (node op) r1 r2)
  (c:list string)
  : Tot (perm (node op) (closure_nodes r1 c) (closure_nodes r2 c)) (decreases p)
  = match p with
    | PNil -> PNil
    | PSkip x m1 m2 p' ->
      let q = closure_nodes_perm m1 m2 p' c in
      if mem x.nid c then PSkip x (closure_nodes m1 c) (closure_nodes m2 c) q else q
    | PSwap x y l ->
      let cl = closure_nodes l c in
      if mem x.nid c then
        (if mem y.nid c then PSwap x y cl
         else PSkip x cl cl (perm_refl cl))
      else
        (if mem y.nid c then PSkip y cl cl (perm_refl cl)
         else perm_refl cl)
    | PTrans m1 m2 m3 p12 p23 ->
      PTrans (closure_nodes m1 c) (closure_nodes m2 c) (closure_nodes m3 c)
             (closure_nodes_perm m1 m2 p12 c) (closure_nodes_perm m2 m3 p23 c)

(* `sub_ids` reads its left argument as a set. *)
let sub_ids_ext (l1 l2 m:list string)
  : Lemma (requires forall (y:string). mem y l1 == mem y l2)
          (ensures sub_ids l1 m == sub_ids l2 m)
  = if sub_ids l1 m then begin
      let aux (x:string) : Lemma (mem x l2 ==> mem x m) =
        if mem x l2 then sub_ids_mem l1 m x else ()
      in
      FStar.Classical.forall_intro aux;
      sub_ids_of_mem l2 m
    end
    else if sub_ids l2 m then begin
      let aux (x:string) : Lemma (mem x l1 ==> mem x m) =
        if mem x l1 then sub_ids_mem l2 m x else ()
      in
      FStar.Classical.forall_intro aux;
      sub_ids_of_mem l1 m
    end
    else ()

(* Two id-distinct node lists holding the same nodes resolve every id alike. *)
let lookup_same (#op:eqtype) (r1 r2:list (node op)) (x:string)
  : Lemma (requires distinct (ids_of r1) /\ distinct (ids_of r2) /\
                    (forall (n:node op). mem n r1 == mem n r2))
          (ensures lookup r1 x == lookup r2 x)
  = ids_same_of_nodes_same r1 r2 x;
    if mem x (ids_of r1) then begin
      lookup_of_mem_ids r1 x;
      lookup_mem_distinct r2 (Found?._0 (lookup r1 x))
    end
    else begin
      (match lookup r1 x with | Missing -> () | Found n -> lookup_found r1 x n);
      (match lookup r2 x with | Missing -> () | Found n -> lookup_found r2 x n)
    end

let rec anc_same (#op:eqtype) (d1 d2:dag op) (fuel:list (node op)) (id:string)
  : Lemma (requires forall (x:string). lookup d1.nodes x == lookup d2.nodes x)
          (ensures ancestors_of d1 fuel id == ancestors_of d2 fuel id)
          (decreases %[fuel; (0 <: nat); ([] <: list string)])
  = match fuel with
    | [] -> ()
    | _ :: fuel' ->
      (match lookup d1.nodes id with
       | Missing -> ()
       | Found n -> anc_all_same d1 d2 fuel' n.nparents)

and anc_all_same (#op:eqtype) (d1 d2:dag op) (fuel:list (node op)) (ids:list string)
  : Lemma (requires forall (x:string). lookup d1.nodes x == lookup d2.nodes x)
          (ensures ancestors_all d1 fuel ids == ancestors_all d2 fuel ids)
          (decreases %[fuel; (1 <: nat); ids])
  = match ids with
    | [] -> ()
    | p :: t -> anc_same d1 d2 fuel p; anc_all_same d1 d2 fuel t

let rec replay_ids_same (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (r1 r2:list (node op)) (ids:list string) (s:state)
  : Lemma (requires forall (x:string). lookup r1 x == lookup r2 x)
          (ensures replay_ids apply r1 ids s == replay_ids apply r2 ids s) (decreases ids)
  = match ids with
    | [] -> ()
    | id :: t ->
      (match lookup r1 id with
       | Missing -> replay_ids_same apply r1 r2 t s
       | Found n -> (match apply n.nop s with | Ok s' -> replay_ids_same apply r1 r2 t s' | Error _ -> ()))

(* THEOREM. Two clones holding the same id-distinct node set — received in any order — replay any
   head to the same outcome, rejection and refusal included. *)
#push-options "--z3rlimit 150"
let replay_to_deterministic (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (lt:string -> string -> bool)
  (k1 k2:list (node op)) (d1 d2:dag op) (fuel:list (node op)) (s0:state) (head:string)
  (p:perm (node op) d1.nodes d2.nodes)
  : Lemma (requires total_order lt /\ distinct_ids d1 /\ distinct_ids d2 /\
                    covers k1 (ids_of (closure_nodes d1.nodes (ancestors_of d1 fuel head))) /\
                    covers k2 (ids_of (closure_nodes d2.nodes (ancestors_of d2 fuel head))))
          (ensures replay_to apply lt k1 d1 fuel s0 head == replay_to apply lt k2 d2 fuel s0 head)
  = let auxn (n:node op) : Lemma (mem n d1.nodes == mem n d2.nodes) =
      perm_mem d1.nodes d2.nodes p n
    in
    FStar.Classical.forall_intro auxn;
    let auxl (x:string) : Lemma (lookup d1.nodes x == lookup d2.nodes x) =
      lookup_same d1.nodes d2.nodes x
    in
    FStar.Classical.forall_intro auxl;
    anc_same d1 d2 fuel head;
    let closure = ancestors_of d1 fuel head in
    let c1 = closure_nodes d1.nodes closure in
    let c2 = closure_nodes d2.nodes closure in
    distinct_closure_nodes d1.nodes closure;
    distinct_closure_nodes d2.nodes closure;
    let pc = closure_nodes_perm d1.nodes d2.nodes p closure in
    drain_deterministic IgnoreDangling lt k1 k2 c1 c2 pc;
    let auxc (n:node op) : Lemma (mem n c1 == mem n c2) = perm_mem c1 c2 pc n in
    FStar.Classical.forall_intro auxc;
    let auxi (y:string) : Lemma (mem y (ids_of c1) == mem y (ids_of c2)) =
      ids_same_of_nodes_same c1 c2 y
    in
    FStar.Classical.forall_intro auxi;
    let order = drain_order lt k1 c1 in
    assert (order == drain_order lt k2 c2);
    sub_ids_ext (ids_of c1) (ids_of c2) order;
    replay_ids_same apply d1.nodes d2.nodes order s0
#pop-options

(* ---- 16.3 the append ---- *)

(* F#: `addNode`'s three arms. `same` is `sameNode` — the parents modulo order, the actor, the op
   through its encoding — and is a parameter for the reason `lt` is one in section 13. *)
type append_result (op:eqtype) =
  | Appended : string -> dag op -> append_result op
  | Collision : string -> append_result op

let add_node (#op:eqtype) (same:node op -> node op -> bool) (n:node op) (d:dag op)
  : Tot (append_result op) =
  match lookup d.nodes n.nid with
  | Found m -> if same m n then Appended n.nid d else Collision n.nid
  | Missing -> Appended n.nid ({ nodes = n :: d.nodes })

(* THEOREM. An id the DAG holds for a node `same` does not accept is refused, by name. *)
let append_refuses_differing_node (#op:eqtype) (same:node op -> node op -> bool) (n:node op)
  (d:dag op) (m:node op)
  : Lemma (requires lookup d.nodes n.nid == Found m /\ not (same m n))
          (ensures add_node same n d == Collision n.nid)
  = ()

(* THEOREM. An accepted append never replaces: every node held before is the node held after, and
   an id-distinct DAG stays id-distinct. *)
let append_never_replaces (#op:eqtype) (same:node op -> node op -> bool) (n:node op) (d:dag op)
  : Lemma (ensures
            (match add_node same n d with
             | Collision _ -> True
             | Appended _ d' ->
               (forall (x:string). Found? (lookup d.nodes x) ==> lookup d'.nodes x == lookup d.nodes x) /\
               (distinct_ids d ==> distinct_ids d')))
  = match lookup d.nodes n.nid with
    | Found _ -> ()
    | Missing ->
      let aux (x:string) : Lemma (Found? (lookup d.nodes x) ==> x =!= n.nid) = () in
      FStar.Classical.forall_intro aux;
      if mem n.nid (ids_of d.nodes) then lookup_of_mem_ids d.nodes n.nid else ()

(* ======================================================================================
   17. Two writes to different slots of one node commute (Phase 340).

   The slot clauses of `independent` say a pair of slot writes is independent exactly when the
   slots differ. This section says that verdict is SOUND against a model of what a slot write
   does, so the precision the clauses buy is not bought with a lie. The state is a store of
   (slot, value) bindings, latest first (F#: a domain whose node holds named fields, or a keyed
   holder); a write binds its slot; a read finds the latest binding. Two writes to different
   slots reach stores that agree at EVERY slot whichever order they are made in — commutation up
   to lookup, which is all a reader of the store can observe — and two writes to ONE slot with
   different values do not: the last writer wins, the orders disagree at that slot, and the
   footprint refuses the pair (`SlotClash`). A whole-node write of the slot's node is refused
   against every slot write of it — the conservative default a four-set footprint keeps.
   ====================================================================================== *)

type slot_store = list (slot & string)

let rec lookup_slot (st:slot_store) (k:slot) : Tot (option string) =
  match st with
  | [] -> None
  | (k', v) :: t -> if k = k' then Some v else lookup_slot t k

(* F#: a domain op `write (node, slot) value`, lowered with `Footprint.slotEdit`. *)
let write_slot (k:slot) (v:string) (st:slot_store) : Tot slot_store = (k, v) :: st

(* F#: `Footprint.slotEdit node slot`. *)
let slot_write_fp (k:slot) : Tot footprint =
  { reads = []; structure_writes = []; content_writes = []; unknown_parent_writes = [];
    slot_reads = [k]; slot_writes = [k] }

(* F#: `Footprint.contentEdit node`. *)
let whole_node_write_fp (n:string) : Tot footprint =
  { reads = [n]; structure_writes = []; content_writes = [n]; unknown_parent_writes = [];
    slot_reads = []; slot_writes = [] }

(* THEOREM. Two slot writes are independent exactly when their slots differ. *)
let slot_writes_independent_iff (k1 k2:slot)
  : Lemma (ensures independent (slot_write_fp k1) (slot_write_fp k2) == (k1 <> k2))
  = ()

(* THEOREM. Writes to different slots commute at every slot of the store. *)
let slot_writes_commute (k1 k2:slot) (v1 v2:string) (st:slot_store) (q:slot)
  : Lemma (requires k1 <> k2)
          (ensures lookup_slot (write_slot k1 v1 (write_slot k2 v2 st)) q ==
                   lookup_slot (write_slot k2 v2 (write_slot k1 v1 st)) q)
  = ()

(* … and two writes to ONE slot with different values do not: the orders disagree at that slot,
   and the footprint refuses the pair. *)
let same_slot_writes_disagree (k:slot) (v1 v2:string) (st:slot_store)
  : Lemma (requires v1 <> v2)
          (ensures not (independent (slot_write_fp k) (slot_write_fp k)) /\
                   ~(lookup_slot (write_slot k v1 (write_slot k v2 st)) k ==
                     lookup_slot (write_slot k v2 (write_slot k v1 st)) k))
  = ()

(* A whole-node write is a write of every slot: refused against each, from either side. *)
let whole_node_refuses_every_slot (n s:string)
  : Lemma (ensures not (independent (whole_node_write_fp n) (slot_write_fp (n, s))) /\
                   not (independent (slot_write_fp (n, s)) (whole_node_write_fp n)))
  = ()

(* ======================================================================================
   18. `Dag.mergeBase` over ANY two heads — the criss-cross merge (Phase 437).

   WHAT IS MODELLED. Nothing new: the intersection of the two ancestor closures and the maximum
   over it by section 14.6's key (closure size, id) — `List.maxBy` over the whole common set, which
   is what production's `mergeBase` computed as Phase 158 cut it. Phase 388 made production's
   `deepest` size only the MAXIMAL members of the intersection; over an acyclic set only a maximal
   member can win (which is what this section proves), so the answer is the same, and
   `DagPrimitives.deepest_is_max_by` proves it.

   WHAT IS PROVED. `max_common_is_maximal`: over a head whose closure is acyclic (section 13's
   witness), the common set is either empty exactly when the heads share no ancestor, or its
   maximum by the key is a COMMON ancestor that no other common ancestor descends from. Section 14
   showed the base is the divergence point when the heads hang off one merged node; this is the
   general statement, and over a criss-cross — two merges of the same two lanes, two maximal common
   ancestors — it is what is true: the base is a maximal common ancestor. Which of the maximal
   ones is picked is the (size, id) tie-break, a policy (Phase 300), and deterministic because
   it is a function of the two closures. THE THEOREM about `Dag.mergeBase` itself is
   `DagPrimitives.merge_base_now_maximal`, stated over the function that runs (Phase 534: until
   then it was stated here, about a second model of the function, and `DagPrimitives` carried it
   across to production's by an equality between the two models).

   NO SMT PATTERNS, for section 11's reason.
   ====================================================================================== *)

(* ---- 18.1 `deeper` is a strict total order on ids ---- *)

let rec shorter_len (#a #b:Type) (l:list a) (m:list b)
  : Lemma (ensures shorter l m == (len l < len m)) (decreases m)
  = match m with
    | [] -> ()
    | _ :: mt -> (match l with | [] -> () | _ :: lt' -> shorter_len lt' mt)

let deeper_len (#op:eqtype) (lt:string -> string -> bool) (d:dag op) (fuel:list (node op))
  (x y:string)
  : Lemma (ensures deeper lt d fuel x y ==
                   (len (closure_set d fuel x) > len (closure_set d fuel y) ||
                    (len (closure_set d fuel x) = len (closure_set d fuel y) && lt y x)))
  = shorter_len (closure_set d fuel y) (closure_set d fuel x);
    shorter_len (closure_set d fuel x) (closure_set d fuel y)

let deeper_strict_total (#op:eqtype) (lt:string -> string -> bool) (d:dag op)
  (fuel:list (node op))
  : Lemma (requires total_order lt)
          (ensures (forall (x:string). not (deeper lt d fuel x x)) /\
                   (forall (x y z:string). deeper lt d fuel x y /\ deeper lt d fuel y z
                                           ==> deeper lt d fuel x z) /\
                   (forall (x y:string). x =!= y ==> (deeper lt d fuel x y \/ deeper lt d fuel y x)))
  = let aux (x y:string) : Lemma (deeper lt d fuel x y ==
                   (len (closure_set d fuel x) > len (closure_set d fuel y) ||
                    (len (closure_set d fuel x) = len (closure_set d fuel y) && lt y x))) =
      deeper_len lt d fuel x y in
    FStar.Classical.forall_intro_2 aux

(* `List.maxBy` under a strict total order returns a member nothing beats. *)
let rec max_by_top (better:string -> string -> bool) (best:string) (l:list string)
  : Lemma (requires (forall (x:string). not (better x x)) /\
                    (forall (x y z:string). better x y /\ better y z ==> better x z) /\
                    (forall (x y:string). x =!= y ==> (better x y \/ better y x)))
          (ensures mem (max_by better best l) (best :: l) /\
                   (forall (y:string). mem y (best :: l) ==> not (better y (max_by better best l))))
          (decreases l)
  = match l with
    | [] -> ()
    | x :: t -> if better x best then max_by_top better x t else max_by_top better best t

(* ---- 18.2 every id in a witnessed closure is off every cycle ---- *)

let rec anc_found (#op:eqtype) (d:dag op) (f:list (node op)) (id x:string)
  : Lemma (requires mem x (ancestors_of d f id)) (ensures Found? (lookup d.nodes x))
          (decreases %[f; (0 <: nat); ([] <: list string)])
  = match f with
    | [] -> ()
    | _ :: f' ->
      (match lookup d.nodes id with
       | Missing -> ()
       | Found n -> if x = id then () else anc_all_found d f' n.nparents x)

and anc_all_found (#op:eqtype) (d:dag op) (f:list (node op)) (ids:list string) (x:string)
  : Lemma (requires mem x (ancestors_all d f ids)) (ensures Found? (lookup d.nodes x))
          (decreases %[f; (1 <: nat); ids])
  = match ids with
    | [] -> ()
    | p :: t -> if mem x (ancestors_of d f p) then anc_found d f p x else anc_all_found d f t x

let off_cycle_in_witness (#op:eqtype) (d:dag op) (fuel:list (node op)) (q:string)
  (wm:list string) (m:node op)
  : Lemma (requires walk_ok d fuel q /\
                    is_topo_enum (closure_nodes d.nodes (ancestors_of d fuel q)) wm /\
                    mem m.nid (ancestors_of d fuel q))
          (ensures off_cycle d fuel m)
  = let x = m.nid in
    at_least_refl fuel;
    walk_ok_anc d fuel fuel q x;
    let aux (y:string)
      : Lemma (mem y (ancestors_of d fuel x) /\ y =!= x ==> not (mem x (ancestors_of d fuel y))) =
      if mem y (ancestors_of d fuel x) && y <> x && mem x (ancestors_of d fuel y) then begin
        anc_before d fuel fuel q wm x y;
        anc_trans d fuel fuel q x y;
        walk_ok_anc d fuel fuel q y;
        anc_before d fuel fuel q wm y x;
        before_asym x y wm
      end
    in
    FStar.Classical.forall_intro aux

(* ---- 18.3 the common set's maximum over ANY two heads, a criss-cross included ---- *)

let common_anc (#op:eqtype) (d:dag op) (fuel:list (node op)) (left right x:string) : Tot bool =
  mem x (ancestors_of d fuel left) && mem x (ancestors_of d fuel right)

(* What `DagPrimitives.merge_base_now_maximal` stands on: the maximum of the common
   set by the key is a common ancestor no other common ancestor descends from, and an empty common
   set is exactly two disjoint histories. *)
#push-options "--z3rlimit 100"
let max_common_is_maximal (#op:eqtype) (lt:string -> string -> bool) (d:dag op)
  (fuel:list (node op)) (left right:string) (wl:list string)
  : Lemma (requires total_order lt /\ Cons? fuel /\ walk_ok d fuel left /\
                    is_topo_enum (closure_nodes d.nodes (ancestors_of d fuel left)) wl)
          (ensures (match inter (ancestors_of d fuel left) (ancestors_of d fuel right) with
                    | [] -> forall (x:string). not (common_anc d fuel left right x)
                    | c0 :: t ->
                      let b = max_by (deeper lt d fuel) c0 t in
                      common_anc d fuel left right b /\
                      (forall (c:string). common_anc d fuel left right c /\ c =!= b
                                          ==> not (mem b (ancestors_of d fuel c)))))
  = match inter (ancestors_of d fuel left) (ancestors_of d fuel right) with
    | [] ->
      let aux (x:string) : Lemma (not (common_anc d fuel left right x)) =
        mem_inter x (ancestors_of d fuel left) (ancestors_of d fuel right) in
      FStar.Classical.forall_intro aux
    | c0 :: t ->
      deeper_strict_total lt d fuel;
      max_by_top (deeper lt d fuel) c0 t;
      let b = max_by (deeper lt d fuel) c0 t in
      at_least_refl fuel;
      let aux (c:string)
        : Lemma (common_anc d fuel left right c /\ c =!= b ==> not (mem b (ancestors_of d fuel c))) =
        if common_anc d fuel left right c && c <> b && mem b (ancestors_of d fuel c) then begin
          anc_found d fuel left c;
          let Found m = lookup d.nodes c in
          lookup_found d.nodes c m;
          walk_ok_anc d fuel fuel left c;
          off_cycle_in_witness d fuel left wl m;
          strictly_deeper lt d fuel m b;
          assert (mem c (c0 :: t))
        end
      in
      FStar.Classical.forall_intro aux
#pop-options

(* ======================================================================================
   19. `Dag.conflictsOfHeads` — the reconcile's interference, naming nodes (Phase 437).

   WHAT IS MODELLED. `Dag.conflictsOfNodes` (`conflicts` at `fun n -> footprintOf n.Op`) and
   `Dag.conflictsOfHeads`: the region above the base partitioned exactly as section 15's
   `excl_lanes` partitions it, each exclusive delta mapped to its nodes, and every unordered pair
   swept with `conflictsOfNodes`, in head order.

   WHAT IS PROVED. `conflicts_of_heads_is_the_sweep`: renaming every reported node to its op
   gives EXACTLY the report `reconcileMany` refuses with (`LanesInterfere`, the `all_conflicts`
   sweep over the same exclusive deltas' ops), conflict for conflict and in order; so it is empty
   exactly when that sweep is. The sweep itself is section 7/8's: empty exactly when the deltas
   are pairwise independent, and arrival-order-invariant read canonically.
   ====================================================================================== *)

(* ---- 19.1 renaming the ops of a report ---- *)

let rec map_list (#a #b:Type) (g:a -> b) (l:list a) : Tot (list b) =
  match l with
  | [] -> []
  | x :: t -> g x :: map_list g t

let map_conflict (#a #b:eqtype) (g:a -> b) (c:conflict a) : Tot (conflict b) =
  { left = g c.left; right = g c.right; address = c.address; shape = c.shape }

let rec map_list_app (#a #b:Type) (g:a -> b) (l m:list a)
  : Lemma (ensures map_list g (app l m) == app (map_list g l) (map_list g m))
  = match l with
    | [] -> ()
    | _ :: t -> map_list_app g t m

let rec map_tag (#a #b:eqtype) (g:a -> b) (x y:a) (s:shape) (addrs:list string)
  : Lemma (ensures map_list (map_conflict g) (tag x y s addrs) == tag (g x) (g y) s addrs)
  = match addrs with
    | [] -> ()
    | _ :: t -> map_tag g x y s t

let rec map_tag_slots (#a #b:eqtype) (g:a -> b) (x y:a) (slots:list slot)
  : Lemma (ensures map_list (map_conflict g) (tag_slots x y slots) == tag_slots (g x) (g y) slots)
  = match slots with
    | [] -> ()
    | _ :: t -> map_tag_slots g x y t

let map_pair_conflicts (#a #b:eqtype) (g:a -> b) (fp:b -> footprint) (fpa:a -> footprint) (x y:a)
  : Lemma (requires forall (n:a). fpa n == fp (g n))
          (ensures map_list (map_conflict g) (pair_conflicts fpa x y)
                   == pair_conflicts fp (g x) (g y))
  = let fa = fp (g x) in
    let fb = fp (g y) in
    let c1 = concurrent_or_slots fa fb in
    let c2 = diff (insert_clash fa fb) c1 in
    let c3 = diff (diff (move_remove fa fb) c1) c2 in
    let c4 = slot_clash fa fb in
    let mc = map_conflict g in
    map_list_app mc (tag x y ConcurrentUpdate c1)
      (app (tag x y InsertPositionClash c2) (app (tag x y MoveVsRemove c3) (tag_slots x y c4)));
    map_list_app mc (tag x y InsertPositionClash c2) (app (tag x y MoveVsRemove c3) (tag_slots x y c4));
    map_list_app mc (tag x y MoveVsRemove c3) (tag_slots x y c4);
    map_tag g x y ConcurrentUpdate c1;
    map_tag g x y InsertPositionClash c2;
    map_tag g x y MoveVsRemove c3;
    map_tag_slots g x y c4

let rec map_conflicts_with (#a #b:eqtype) (g:a -> b) (fp:b -> footprint) (fpa:a -> footprint) (x:a) (db:list a)
  : Lemma (requires forall (n:a). fpa n == fp (g n))
          (ensures map_list (map_conflict g) (conflicts_with fpa x db)
                   == conflicts_with fp (g x) (map_list g db))
  = match db with
    | [] -> ()
    | y :: t ->
      map_list_app (map_conflict g) (pair_conflicts fpa x y)
        (conflicts_with fpa x t);
      map_pair_conflicts g fp fpa x y;
      map_conflicts_with g fp fpa x t

let rec map_conflicts (#a #b:eqtype) (g:a -> b) (fp:b -> footprint) (fpa:a -> footprint) (da db:list a)
  : Lemma (requires forall (n:a). fpa n == fp (g n))
          (ensures map_list (map_conflict g) (conflicts fpa da db)
                   == conflicts fp (map_list g da) (map_list g db))
  = match da with
    | [] -> ()
    | x :: t ->
      map_list_app (map_conflict g) (conflicts_with fpa x db)
        (conflicts fpa t db);
      map_conflicts_with g fp fpa x db;
      map_conflicts g fp fpa t db

let rec map_lane_conflicts (#a #b:eqtype) (g:a -> b) (fp:b -> footprint) (fpa:a -> footprint) (d:list a)
  (rest:list (list a))
  : Lemma (requires forall (n:a). fpa n == fp (g n))
          (ensures map_list (map_conflict g) (lane_conflicts fpa d rest)
                   == lane_conflicts fp (map_list g d) (map_list (map_list g) rest))
  = match rest with
    | [] -> ()
    | e :: t ->
      map_list_app (map_conflict g) (conflicts fpa d e)
        (lane_conflicts fpa d t);
      map_conflicts g fp fpa d e;
      map_lane_conflicts g fp fpa d t

let rec map_all_conflicts (#a #b:eqtype) (g:a -> b) (fp:b -> footprint) (fpa:a -> footprint) (ds:list (list a))
  : Lemma (requires forall (n:a). fpa n == fp (g n))
          (ensures map_list (map_conflict g) (all_conflicts fpa ds)
                   == all_conflicts fp (map_list (map_list g) ds))
  = match ds with
    | [] -> ()
    | d :: rest ->
      map_list_app (map_conflict g) (lane_conflicts fpa d rest)
        (all_conflicts fpa rest);
      map_lane_conflicts g fp fpa d rest;
      map_all_conflicts g fp fpa rest

(* ---- 19.2 `Dag.conflictsOfNodes` and `Dag.conflictsOfHeads` ---- *)

let node_op (#op:eqtype) (n:node op) : Tot op = n.nop

let node_fp (#op:eqtype) (fp:op -> footprint) (n:node op) : Tot footprint = fp n.nop

(* F#: `Dag.conflictsOfNodes` — `conflicts` at `fun n -> footprintOf n.Op`. *)
let conflicts_of_nodes (#op:eqtype) (fp:op -> footprint) (da db:list (node op))
  : Tot (list (conflict (node op))) =
  conflicts (node_fp fp) da db

(* F#: the `for (i, a) ... for (j, b) ... if i < j then yield! conflictsOfNodes footprintOf a b`
   sweep of `Dag.conflictsOfHeads`. *)
let rec heads_lane_sweep (#op:eqtype) (fp:op -> footprint) (d:list (node op))
  (rest:list (list (node op))) : Tot (list (conflict (node op))) =
  match rest with
  | [] -> []
  | e :: t -> app (conflicts_of_nodes fp d e) (heads_lane_sweep fp d t)

let rec heads_sweep (#op:eqtype) (fp:op -> footprint) (ds:list (list (node op)))
  : Tot (list (conflict (node op))) =
  match ds with
  | [] -> []
  | d :: rest -> app (heads_lane_sweep fp d rest) (heads_sweep fp rest)

(* F#: `Dag.conflictsOfHeads`. *)
let conflicts_of_heads (#op:eqtype) (fp:op -> footprint) (d:dag op)
  (order base_c:list string) (cs:list (list string)) : Tot (list (conflict (node op))) =
  heads_sweep fp (map_list (nodes_for d) (excl_lanes order base_c cs cs))

(* F#: the `deltas` `reconcileRegion` sweeps — `opsOf dag ids` of each exclusive delta. *)
let excl_op_lanes (#op:eqtype) (d:dag op) (order base_c:list string) (cs:list (list string))
  : Tot (list (list op)) =
  map_list (fun ids -> ops_of (nodes_for d ids)) (excl_lanes order base_c cs cs)

let rec heads_lane_sweep_is (#op:eqtype) (fp:op -> footprint) (d:list (node op))
  (rest:list (list (node op)))
  : Lemma (ensures heads_lane_sweep fp d rest == lane_conflicts (node_fp fp) d rest)
  = match rest with
    | [] -> ()
    | _ :: t -> heads_lane_sweep_is fp d t

let rec heads_sweep_is (#op:eqtype) (fp:op -> footprint) (ds:list (list (node op)))
  : Lemma (ensures heads_sweep fp ds == all_conflicts (node_fp fp) ds)
  = match ds with
    | [] -> ()
    | d :: rest -> heads_lane_sweep_is fp d rest; heads_sweep_is fp rest

let rec ops_of_is_map (#op:eqtype) (ns:list (node op))
  : Lemma (ensures ops_of ns == map_list node_op ns)
  = match ns with
    | [] -> ()
    | _ :: t -> ops_of_is_map t

let rec map_map_nodes (#op:eqtype) (d:dag op) (ls:list (list string))
  : Lemma (ensures map_list (map_list node_op) (map_list (nodes_for d) ls)
                   == map_list (fun ids -> ops_of (nodes_for d ids)) ls)
  = match ls with
    | [] -> ()
    | ids :: t -> ops_of_is_map (nodes_for d ids); map_map_nodes d t

(* `conflictsOfHeads` is the pairwise sweep `reconcileMany` refuses with, node for op:
   renaming each reported node to its op gives EXACTLY the `LanesInterfere` report, conflict for
   conflict and in order — so it is empty exactly when that sweep is. *)
let conflicts_of_heads_is_the_sweep (#op:eqtype) (fp:op -> footprint) (d:dag op)
  (order base_c:list string) (cs:list (list string))
  : Lemma (ensures map_list (map_conflict node_op) (conflicts_of_heads fp d order base_c cs)
                   == all_conflicts fp (excl_op_lanes d order base_c cs) /\
                   (Nil? (conflicts_of_heads fp d order base_c cs)
                    <==> Nil? (all_conflicts fp (excl_op_lanes d order base_c cs))))
  = let ls = map_list (nodes_for d) (excl_lanes order base_c cs cs) in
    heads_sweep_is fp ls;
    map_all_conflicts node_op fp (node_fp fp) ls;
    map_map_nodes d (excl_lanes order base_c cs cs)

(* ======================================================================================
   20. The reachability index (Phase 289), lifted to proved (Phase 437).

   WHAT IS MODELLED. `Reach` as its slots: an id and its ancestor bitset, the bitset read as the
   ids whose bits it sets. `reach_step` is one step of `Reach.ofDag`'s pass over the drain (`drainNodes`, Phase 529) and
   `Reach.extend`'s leaf step at once: a node the DAG holds and no slot carries, whose held
   parents all carry one, gets a slot whose set is its own id over the union of its parents'
   (`closureBits`); `reach_of_dag` runs it over the whole DAG's drain. `reach_ancestors` and
   `reach_reaches` are `Reach.ancestors` and `Reach.reaches`, fallback to the unindexed walk
   included.

   WHAT IS PROVED. `reach_of_dag_agrees` (via `reach_build_valid` and `reach_agrees`): for every
   descendant the walk completes on, `Reach.reaches` answers exactly `Set.contains ancestor
   (ancestorsOf dag descendant)` and `Reach.ancestors` is exactly `ancestorsOf`. The invariant is
   per slot and is kept by EVERY step, so it holds over any order the steps run in — the drain,
   or a later extension onto the same DAG.

   WHAT IS NOT MODELLED HERE. The word layout (`hasBit`, the 32-bit words, `popCount`), the
   position arrays, and `Reach.extend`'s copy-and-shift of them and its rebuild under a dangling
   id stay `tested` (`Conformance.reachLaws`). The index's `mergeBase` / `commonBase` / `between` /
   `tryTopoOrder` are modelled over these slots in `DagPrimitives.fst` (Phase 457).
   ====================================================================================== *)

(* ---- 20.1 the index ---- *)

(* F#: one slot of `Reach` — `Ids[s]` and `Bits[s]`, the bitset read as the ids whose bits it
   sets. The word layout (`hasBit`, `closureBits`' word loop, `popCount`) is below the model. *)
type rslot = { rid : string; rbits : list string }

(* F#: `Map.tryFind id reach.Slot` then `reach.Bits[s]`. The newest slot is at the head. *)
let rec slot_of (r:list rslot) (id:string) : Tot (found (list string)) =
  match r with
  | [] -> Missing
  | e :: t -> if e.rid = id then Found e.rbits else slot_of t id

(* F#: `parentBits` — the bitsets of the parents that hold a slot, unioned (`closureBits`). *)
let rec parent_bits (r:list rslot) (ps:list string) : Tot (list string) =
  match ps with
  | [] -> []
  | p :: t ->
    (match slot_of r p with
     | Found b -> app b (parent_bits r t)
     | Missing -> parent_bits r t)

(* F#: `extend`'s `parentSlots |> List.exists Option.isNone` read as its negation: every parent the
   DAG holds has a slot. On `ofDag`'s drain it always holds — a node is drained after its held
   parents — so there it is idle; at `extend` it is the refusal to index a node under an
   unindexed parent. *)
let rec held_slotted (#op:eqtype) (d:dag op) (r:list rslot) (ps:list string) : Tot bool =
  match ps with
  | [] -> true
  | p :: t -> (Missing? (lookup d.nodes p) || Found? (slot_of r p)) && held_slotted d r t

(* F#: one step of `ofDag`'s pass over `drainNodes`' order, and `Reach.extend`: a node the DAG holds, not yet
   slotted, whose held parents are all slotted, gets the next slot with its own bit set over its
   parents' bitsets. *)
let reach_step (#op:eqtype) (d:dag op) (r:list rslot) (id:string) : Tot (list rslot) =
  match lookup d.nodes id with
  | Missing -> r
  | Found n ->
    if Found? (slot_of r id) then r
    else if held_slotted d r n.nparents then { rid = id; rbits = id :: parent_bits r n.nparents } :: r
    else r

(* F#: `Reach.ofDag` — the steps over an order (production: the whole DAG's drain). *)
let rec reach_build (#op:eqtype) (d:dag op) (r:list rslot) (ord:list string)
  : Tot (list rslot) (decreases ord) =
  match ord with
  | [] -> r
  | id :: t -> reach_build d (reach_step d r id) t

let reach_of_dag (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op)) (d:dag op)
  : Tot (list rslot) =
  reach_build d [] (drain_order lt kfuel d.nodes)

(* F#: `Reach.ancestors` — the slot's bitset, else `Dag.ancestorsOf` itself. *)
let reach_ancestors (#op:eqtype) (d:dag op) (fuel:list (node op)) (r:list rslot) (id:string)
  : Tot (list string) =
  match slot_of r id with
  | Found b -> b
  | Missing -> ancestors_of d fuel id

(* F#: `Reach.reaches` — one bit test where both hold a slot, `false` where only the descendant
   does, the unindexed walk where the descendant has none. *)
let reach_reaches (#op:eqtype) (d:dag op) (fuel:list (node op)) (r:list rslot)
  (anc dsc:string) : Tot bool =
  match slot_of r dsc with
  | Found b -> (match slot_of r anc with | Found _ -> mem anc b | Missing -> false)
  | Missing -> mem anc (ancestors_of d fuel dsc)

(* ---- 20.2 the invariant every step keeps ---- *)

let slot_sound (#op:eqtype) (d:dag op) (r:list rslot) (e:rslot) : prop =
  (forall (f:list (node op)) (x:string).
     walk_ok d f e.rid ==> (mem x e.rbits <==> mem x (ancestors_of d f e.rid))) /\
  (forall (x:string). mem x e.rbits ==> Found? (slot_of r x))

let slot_sound_at (#op:eqtype) (d:dag op) (r:list rslot) (e:rslot) (f:list (node op)) (x:string)
  : Lemma (requires slot_sound d r e /\ walk_ok d f e.rid)
          (ensures mem x e.rbits <==> mem x (ancestors_of d f e.rid))
  = ()

let reach_valid (#op:eqtype) (d:dag op) (r:list rslot) : prop =
  forall (e:rslot). mem e r ==> slot_sound d r e

let rec slot_of_mem (r:list rslot) (id:string) (b:list string)
  : Lemma (requires slot_of r id == Found b) (ensures mem ({ rid = id; rbits = b }) r)
  = match r with
    | [] -> ()
    | e :: t -> if e.rid = id then () else slot_of_mem t id b

let rec mem_parent_bits (#op:eqtype) (d:dag op) (r:list rslot) (ps:list string) (x:string)
  (f:list (node op))
  : Lemma (requires reach_valid d r /\ held_slotted d r ps /\ walk_all_ok d f ps)
          (ensures mem x (parent_bits r ps) <==> mem x (ancestors_all d f ps))
          (decreases ps)
  = match ps with
    | [] -> ()
    | p :: t ->
      mem_parent_bits d r t x f;
      (match slot_of r p with
       | Found b ->
         slot_of_mem r p b;
         assert (slot_sound d r ({ rid = p; rbits = b }));
         assert (walk_ok d f p);
         slot_sound_at d r ({ rid = p; rbits = b }) f x;
         mem_app x b (parent_bits r t);
         mem_app x (ancestors_of d f p) (ancestors_all d f t)
       | Missing ->
         (match f with
          | [] -> ()
          | _ :: _ -> ()))

let rec parent_bits_slotted (r:list rslot) (ps:list string) (x:string)
  : Lemma (requires (forall (e:rslot). mem e r ==> (forall (y:string). mem y e.rbits ==> Found? (slot_of r y))) /\
                    mem x (parent_bits r ps))
          (ensures Found? (slot_of r x))
          (decreases ps)
  = match ps with
    | [] -> ()
    | p :: t ->
      (match slot_of r p with
       | Found b ->
         slot_of_mem r p b;
         mem_app x b (parent_bits r t);
         if mem x b then () else parent_bits_slotted r t x
       | Missing -> parent_bits_slotted r t x)

#push-options "--z3rlimit 100"
let reach_step_valid (#op:eqtype) (d:dag op) (r:list rslot) (id:string)
  : Lemma (requires reach_valid d r) (ensures reach_valid d (reach_step d r id))
  = match lookup d.nodes id with
    | Missing -> ()
    | Found n ->
      if Found? (slot_of r id) then ()
      else if held_slotted d r n.nparents then begin
        let e = { rid = id; rbits = id :: parent_bits r n.nparents } in
        let r' = e :: r in
        let old_slotted (y:string) : Lemma (Found? (slot_of r y) ==> Found? (slot_of r' y)) = () in
        FStar.Classical.forall_intro old_slotted;
        let new_sound (f:list (node op)) (x:string)
          : Lemma (walk_ok d f id ==> (mem x e.rbits <==> mem x (ancestors_of d f id))) =
          match f with
          | [] -> ()
          | _ :: f' -> if walk_ok d f id then mem_parent_bits d r n.nparents x f'
        in
        FStar.Classical.forall_intro_2 new_sound;
        let new_closed (x:string) : Lemma (mem x e.rbits ==> Found? (slot_of r' x)) =
          if mem x e.rbits && x <> id then parent_bits_slotted r n.nparents x
        in
        FStar.Classical.forall_intro new_closed
      end
      else ()
#pop-options

let rec reach_build_valid (#op:eqtype) (d:dag op) (r:list rslot) (ord:list string)
  : Lemma (requires reach_valid d r) (ensures reach_valid d (reach_build d r ord)) (decreases ord)
  = match ord with
    | [] -> ()
    | id :: t -> reach_step_valid d r id; reach_build_valid d (reach_step d r id) t

(* ---- 20.3 the index answers what the walk answers ---- *)

let reach_agrees (#op:eqtype) (d:dag op) (r:list rslot) (fuel:list (node op)) (anc dsc:string)
  : Lemma (requires reach_valid d r /\ walk_ok d fuel dsc)
          (ensures reach_reaches d fuel r anc dsc == mem anc (ancestors_of d fuel dsc) /\
                   (forall (x:string). mem x (reach_ancestors d fuel r dsc)
                                       == mem x (ancestors_of d fuel dsc)))
  = match slot_of r dsc with
    | Found b ->
      slot_of_mem r dsc b;
      let e = { rid = dsc; rbits = b } in
      assert (slot_sound d r e);
      let aux (x:string) : Lemma (mem x b == mem x (ancestors_of d fuel dsc)) =
        slot_sound_at d r e fuel x in
      FStar.Classical.forall_intro aux;
      aux anc
    | Missing -> ()

let reach_of_dag_agrees (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (anc dsc:string)
  : Lemma (requires walk_ok d fuel dsc)
          (ensures (let r = reach_of_dag lt kfuel d in
                    reach_reaches d fuel r anc dsc == mem anc (ancestors_of d fuel dsc) /\
                    (forall (x:string). mem x (reach_ancestors d fuel r dsc)
                                        == mem x (ancestors_of d fuel dsc))))
  = reach_build_valid d [] (drain_order lt kfuel d.nodes);
    reach_agrees d (reach_of_dag lt kfuel d) fuel anc dsc

(* ======================================================================================
   21. Checkpoints on the lane DAG (Phase 288), lifted to proved (Phase 437).

   WHAT IS MODELLED. `Checkpoint` (node, state, seal), `sealAt` (the seal a parameter: production's
   is the strict snapshot hash), `checkpointAt`, `verifyCheckpoint`, `replayFrom` with its private
   `above` check, refusal for refusal, `checkpointFrom`, and the DAG `cut` returns
   (`Map.filter` to the kept set) together with the contract `cut` meets, `truncation_ok`, which
   is computable and which the differential evaluates on every DAG production compacts.

   WHAT IS PROVED.
     - `drain_restrict` / `drain_restricts` — the drain of a head's closure, kept to the closure of
       a node inside it, IS that node's own drain. Production's docstring has said this since
       Phase 311; it is now a theorem about the id-ordered drain of section 13.
     - THEOREM 1 `replay_from_is_replay_to` — replay from a checkpoint `checkpointAt` sealed equals
       replay from genesis (`tryReplayTo`), state for state and rejection for rejection, on every
       head `replayFrom` does not refuse as unreached or uncovered. `checkpoint_from_is_checkpoint_at`
       is its corollary for `checkpointFrom`.
     - THEOREM 2 `truncation_preserves_replay` — truncating behind the checkpoint's node to any
       kept set meeting `cut`'s contract preserves every such replay at or after it, whatever
       state the checkpoint carries.

   WHAT IS NOT MODELLED. The seal's hash; the JSONL sidecar; `firstBreakFrom` / `verifyDagFrom`.
   `cut`'s computation of the kept set (`forwardFrom`, the band) is modelled in `DagPrimitives.fst`
   (Phase 457) and proved to meet the contract, so truncation is proved end to end there.

   THE GO-RED. A `checkpointAt` that seals a node other than the one whose closure it folded
   fails theorem 1 at the leg (`cp.cnode == c` is unprovable): Phase 437 ran that perturbation.
   ====================================================================================== *)

(* ---- 21.1 the drain of a closure, restricted to a closure below it ---- *)

let in_set (c:list string) (x:string) : Tot bool = mem x c

(* The node set `cS` is DOWN-CLOSED in `cT` over the work list `rT`: a parent of a `cS` node that
   the drain over `cT` sees is itself in `cS`. An ancestor closure is down-closed in any closure
   holding it. *)
let down_closed (#op:eqtype) (rT:list (node op)) (cT cS:list string) : prop =
  forall (n:node op) (p:string).
    mem n rT /\ mem n.nid cS /\ mem p n.nparents /\ mem p cT ==> mem p cS

let rec parents_in_down (ps cT cS:list string)
  : Lemma (requires (forall (p:string). mem p ps /\ mem p cT ==> mem p cS) /\
                    (forall (x:string). mem x cS ==> mem x cT))
          (ensures parents_in ps cS == parents_in ps cT)
  = match ps with
    | [] -> ()
    | _ :: t -> parents_in_down t cT cS

let rec all_emitted_agree (l cS eS eT:list string)
  : Lemma (requires (forall (p:string). mem p l ==> mem p cS) /\
                    (forall (p:string). mem p cS ==> mem p eS == mem p eT))
          (ensures all_emitted l eS == all_emitted l eT)
  = match l with
    | [] -> ()
    | _ :: t -> all_emitted_agree t cS eS eT

let ready_agree (#op:eqtype) (n:node op) (cT cS eT eS:list string)
  : Lemma (requires (forall (p:string). mem p n.nparents /\ mem p cT ==> mem p cS) /\
                    (forall (x:string). mem x cS ==> mem x cT) /\
                    (forall (p:string). mem p cS ==> mem p eS == mem p eT))
          (ensures all_emitted (parents_in n.nparents cS) eS
                   == all_emitted (parents_in n.nparents cT) eT)
  = parents_in_down n.nparents cT cS;
    let aux (p:string) : Lemma (mem p (parents_in n.nparents cS) ==> mem p cS) =
      mem_parents_in n.nparents cS p in
    FStar.Classical.forall_intro aux;
    all_emitted_agree (parents_in n.nparents cS) cS eS eT

let rec frontier_restrict (#op:eqtype) (rT:list (node op)) (cT cS eT eS:list string)
  : Lemma (requires down_closed rT cT cS /\ (forall (x:string). mem x cS ==> mem x cT) /\
                    (forall (p:string). mem p cS ==> mem p eS == mem p eT))
          (ensures frontier (closure_nodes rT cS) cS eS == keep (in_set cS) (frontier rT cT eT))
          (decreases rT)
  = match rT with
    | [] -> ()
    | n :: t ->
      frontier_restrict t cT cS eT eS;
      if mem n.nid cS then ready_agree n cT cS eT eS else ()

let pick_min_keep (lt:string -> string -> bool) (f:list string) (cS:list string)
  : Lemma (requires total_order lt /\ Cons? f /\ mem (pick_min lt f) cS)
          (ensures Cons? (keep (in_set cS) f) /\ pick_min lt (keep (in_set cS) f) == pick_min lt f)
  = let x = pick_min lt f in
    pick_min_mem lt f;
    mem_keep (in_set cS) f x;
    let k = keep (in_set cS) f in
    pick_min_mem lt k;
    let m = pick_min lt k in
    mem_keep (in_set cS) f m;
    pick_min_least lt k x;
    pick_min_least lt f m

let rec closure_nodes_remove_in (#op:eqtype) (rT:list (node op)) (cS:list string) (x:string)
  : Lemma (requires mem x cS)
          (ensures closure_nodes (remove_id rT x) cS == remove_id (closure_nodes rT cS) x)
  = match rT with
    | [] -> ()
    | n :: t -> if n.nid = x then () else closure_nodes_remove_in t cS x

let rec closure_nodes_remove_out (#op:eqtype) (rT:list (node op)) (cS:list string) (x:string)
  : Lemma (requires not (mem x cS))
          (ensures closure_nodes (remove_id rT x) cS == closure_nodes rT cS)
  = match rT with
    | [] -> ()
    | n :: t -> if n.nid = x then () else closure_nodes_remove_out t cS x

let rec len_remove_id (#op:eqtype) (r:list (node op)) (x:string)
  : Lemma (requires mem x (ids_of r)) (ensures len (remove_id r x) + 1 == len r)
  = match r with
    | [] -> ()
    | n :: t -> if n.nid = x then () else len_remove_id t x

let rec mem_remove_id_sub (#op:eqtype) (r:list (node op)) (x:string) (n:node op)
  : Lemma (requires mem n (remove_id r x)) (ensures mem n r)
  = match r with
    | [] -> ()
    | m :: t -> if m.nid = x then () else if m = n then () else mem_remove_id_sub t x n

let rec len_closure_nodes (#op:eqtype) (r:list (node op)) (c:list string)
  : Lemma (ensures len (closure_nodes r c) <= len r)
  = match r with
    | [] -> ()
    | _ :: t -> len_closure_nodes t c

#push-options "--z3rlimit 120"
let rec drain_restrict (lt:string -> string -> bool) (#op:eqtype)
  (fT:list (node op)) (rT:list (node op)) (cT eT:list string)
  (fS:list (node op)) (rS:list (node op)) (cS eS:list string)
  : Lemma (requires total_order lt /\ rS == closure_nodes rT cS /\
                    (forall (x:string). mem x cS ==> mem x cT) /\
                    down_closed rT cT cS /\
                    (forall (p:string). mem p cS ==> mem p eS == mem p eT) /\
                    len fT >= len rT /\ len fS >= len rS)
          (ensures keep (in_set cS) (kahn (pick_min lt) fT rT cT eT)
                   == kahn (pick_min lt) fS rS cS eS)
          (decreases fT)
  = frontier_restrict rT cT cS eT eS;
    match fT with
    | [] ->
      assert (rT == []);
      (match fS with | [] -> () | _ :: _ -> ())
    | _ :: fT' ->
      let ft = frontier rT cT eT in
      (match ft with
       | [] -> (match fS with | [] -> () | _ :: _ -> ())
       | _ :: _ ->
         let x = pick_min lt ft in
         pick_min_mem lt ft;
         frontier_sub rT cT eT x;
         len_remove_id rT x;
         let rT' = remove_id rT x in
         let eT' = app eT [ x ] in
         let dc (n:node op) : Lemma (mem n rT' ==> mem n rT) =
           if mem n rT' then mem_remove_id_sub rT x n in
         FStar.Classical.forall_intro dc;
         assert (down_closed rT' cT cS);
         if mem x cS then begin
           pick_min_keep lt ft cS;
           mem_ids_closure_nodes rT cS x;
           len_remove_id rS x;
           closure_nodes_remove_in rT cS x;
           let eS' = app eS [ x ] in
           let em (p:string) : Lemma (mem p cS ==> mem p eS' == mem p eT') =
             mem_app p eS [ x ]; mem_app p eT [ x ] in
           FStar.Classical.forall_intro em;
           (match fS with
            | [] -> ()
            | _ :: fS' -> drain_restrict lt fT' rT' cT eT' fS' (remove_id rS x) cS eS')
         end
         else begin
           closure_nodes_remove_out rT cS x;
           let em (p:string) : Lemma (mem p cS ==> mem p eS == mem p eT') =
             mem_app p eT [ x ] in
           FStar.Classical.forall_intro em;
           drain_restrict lt fT' rT' cT eT' fS rS cS eS
         end)
#pop-options

(* ---- 21.2 one head's drain, restricted to a node of its closure ---- *)

let rec covers_len (#a #b:Type) (fuel:list a) (l:list b)
  : Lemma (requires covers fuel l) (ensures len fuel > len l) (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> (match fuel with | [] -> () | _ :: f -> covers_len f t)

let rec len_ids_of (#op:eqtype) (ns:list (node op)) : Lemma (ensures len (ids_of ns) == len ns) =
  match ns with
  | [] -> ()
  | _ :: t -> len_ids_of t

let rec closure_nodes_twice (#op:eqtype) (ns:list (node op)) (a c:list string)
  : Lemma (requires forall (n:node op). mem n ns /\ mem n.nid c ==> mem n.nid a)
          (ensures closure_nodes (closure_nodes ns a) c == closure_nodes ns c)
  = match ns with
    | [] -> ()
    | _ :: t -> closure_nodes_twice t a c

let rec closure_nodes_ext (#op:eqtype) (ns:list (node op)) (a c:list string)
  : Lemma (requires forall (n:node op). mem n ns ==> mem n.nid a == mem n.nid c)
          (ensures closure_nodes ns a == closure_nodes ns c)
  = match ns with
    | [] -> ()
    | _ :: t -> closure_nodes_ext t a c

let closure_of (#op:eqtype) (d:dag op) (fuel:list (node op)) (x:string) : Tot (list (node op)) =
  closure_nodes d.nodes (ancestors_of d fuel x)

#push-options "--z3rlimit 150"
let drain_restricts (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op))
  (d:dag op) (fuel:list (node op)) (c h:string)
  : Lemma (requires total_order lt /\ distinct_ids d /\ walk_ok d fuel h /\
                    mem c (ancestors_of d fuel h) /\ covers kfuel (ids_of (closure_of d fuel h)))
          (ensures keep (in_set (ids_of (closure_of d fuel c))) (drain_order lt kfuel (closure_of d fuel h))
                   == drain_order lt kfuel (closure_of d fuel c))
  = let ah = ancestors_of d fuel h in
    let ac = ancestors_of d fuel c in
    let cnh = closure_of d fuel h in
    let cnc = closure_of d fuel c in
    let cT = ids_of cnh in
    let cS = ids_of cnc in
    at_least_refl fuel;
    walk_ok_anc d fuel fuel h c;
    (* every id below c is below h *)
    let sub (x:string) : Lemma (mem x ac ==> mem x ah) =
      if mem x ac then anc_trans d fuel fuel h c x in
    FStar.Classical.forall_intro sub;
    let sub_ids (x:string) : Lemma (mem x cS ==> mem x cT) =
      mem_ids_closure_nodes d.nodes ac x; mem_ids_closure_nodes d.nodes ah x in
    FStar.Classical.forall_intro sub_ids;
    (* the S work list is the T work list kept to S *)
    let tw (n:node op) : Lemma (mem n d.nodes /\ mem n.nid cS ==> mem n.nid ah) =
      mem_ids_closure_nodes d.nodes ac n.nid in
    FStar.Classical.forall_intro tw;
    closure_nodes_twice d.nodes ah cS;
    let ex (n:node op) : Lemma (mem n d.nodes ==> mem n.nid cS == mem n.nid ac) =
      if mem n d.nodes then begin mem_ids_of n d.nodes; mem_ids_closure_nodes d.nodes ac n.nid end in
    FStar.Classical.forall_intro ex;
    closure_nodes_ext d.nodes cS ac;
    assert (cnc == closure_nodes cnh cS);
    (* down-closure *)
    let dc (n:node op) (p:string)
      : Lemma (mem n cnh /\ mem n.nid cS /\ mem p n.nparents /\ mem p cT ==> mem p cS) =
      if mem n cnh && mem n.nid cS && mem p n.nparents && mem p cT then begin
        mem_closure_nodes d.nodes ah n;
        lookup_mem_distinct d.nodes n;
        mem_ids_closure_nodes d.nodes ac n.nid;
        mem_ids_closure_nodes d.nodes ah p;
        lookup_of_mem_ids d.nodes p;
        let big = n :: n :: fuel in
        at_least_cons n fuel fuel;
        at_least_cons n (n :: fuel) fuel;
        assert (mem p (ancestors_of d (n :: fuel) p));
        anc_all_incl d (n :: fuel) n.nparents p p;
        assert (mem p (ancestors_of d big n.nid));
        anc_trans d fuel big c n.nid p;
        mem_ids_closure_nodes d.nodes ac p
      end
    in
    FStar.Classical.forall_intro_2 dc;
    covers_len kfuel cT;
    len_ids_of cnh;
    len_closure_nodes cnh cS;
    drain_restrict lt kfuel cnh cT [] kfuel cnc cS []
#pop-options

(* ---- 21.3 the checkpoint (F#: `Checkpoint`, `sealAt`, `checkpointAt`, `verifyCheckpoint`) ---- *)

noeq type checkpoint (state:Type) = { cnode : string; cstate : state; cseal : string }

(* F#: `CheckpointFault<'Rej>`. *)
type ck_fault (rej:Type) =
  | CUnknownNode : string -> ck_fault rej
  | CReplay : replay_fault rej -> ck_fault rej
  | CUnreached : string -> ck_fault rej
  | CUncovered : string -> ck_fault rej

(* F#: `sealAt` — the seal is `OpStream.Snapshots`' strict snapshot hash of the state at sequence
   zero with the node as its boundary hash; the model takes that hash as a parameter. *)
let seal_at (#state:Type) (seal:string -> state -> string) (c:string) (st:state)
  : Tot (checkpoint state) =
  { cnode = c; cstate = st; cseal = seal c st }

let lift (#state #rej:Type) (r:outcome state (replay_fault rej)) : Tot (outcome state (ck_fault rej)) =
  match r with
  | Ok s -> Ok s
  | Error f -> Error (CReplay f)

(* F#: `checkpointAt`. *)
let checkpoint_at (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (lt:string -> string -> bool)
  (kfuel:list (node op)) (d:dag op) (fuel:list (node op)) (seal:string -> state -> string)
  (s0:state) (c:string) : Tot (outcome (checkpoint state) (ck_fault rej)) =
  match lookup d.nodes c with
  | Missing -> Error (CUnknownNode c)
  | Found _ ->
    (match replay_to apply lt kfuel d fuel s0 c with
     | Error f -> Error (CReplay f)
     | Ok st -> Ok (seal_at seal c st))

(* F#: `verifyCheckpoint` — the node is held and the seal recomputes. *)
let verify_checkpoint (#op:eqtype) (#state:Type) (seal:string -> state -> string)
  (cp:checkpoint state) (d:dag op) : Tot bool =
  Found? (lookup d.nodes cp.cnode) && seal cp.cnode cp.cstate = cp.cseal

(* F#: `List.exists (fun p -> p = node || Set.contains p desc)`. *)
let rec exists_parent (ps:list string) (node:string) (desc:list string) : Tot bool =
  match ps with
  | [] -> false
  | p :: t -> p = node || mem p desc || exists_parent t node desc

(* F#: `above`'s `go` — the first id of the delta that does not descend from `node`, judging each
   by whether a parent IS `node` or was already judged a descendant. A delta id is always held
   (it came out of the head's closure); `Missing` is the model's total match. *)
let rec above_go (#op:eqtype) (d:dag op) (node:string) (desc:list string) (ids:list string)
  : Tot (found string) (decreases ids) =
  match ids with
  | [] -> Missing
  | id :: rest ->
    (match lookup d.nodes id with
     | Missing -> Found id
     | Found n -> if exists_parent n.nparents node desc then above_go d node (id :: desc) rest
                  else Found id)

(* F#: `replayFrom` (through `replayAbove`), clause for clause and refusal for refusal. *)
let replay_from (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (lt:string -> string -> bool)
  (kfuel:list (node op)) (d:dag op) (fuel:list (node op)) (cp:checkpoint state) (head:string)
  : Tot (outcome state (ck_fault rej)) =
  match lookup d.nodes cp.cnode with
  | Missing -> Error (CUnknownNode cp.cnode)
  | Found _ ->
    (match lookup d.nodes head with
     | Missing -> Error (CReplay (RUnknownHead head))
     | Found _ ->
       let cn = closure_nodes d.nodes (ancestors_of d fuel head) in
       let order = drain_order lt kfuel cn in
       if not (sub_ids (ids_of cn) order) then Error (CReplay (RCyclicHistory head))
       else if not (mem cp.cnode order) then Error (CUnreached head)
       else begin
         let delta = diff order (ancestors_of d fuel cp.cnode) in
         match above_go d cp.cnode [] delta with
         | Found id -> Error (CUncovered id)
         | Missing -> lift (replay_ids apply d.nodes delta cp.cstate)
       end)

(* ---- 21.4 the list algebra the theorem runs on ---- *)

let out_of (a:list string) (x:string) : Tot bool = not (mem x a)

let rec diff_is_keep (l a:list string) : Lemma (ensures diff l a == keep (out_of a) l) =
  match l with
  | [] -> ()
  | _ :: t -> diff_is_keep t a

let rec keep_ext_on (f g:string -> bool) (l:list string)
  : Lemma (requires forall (x:string). mem x l ==> f x == g x) (ensures keep f l == keep g l)
  = match l with
    | [] -> ()
    | _ :: t -> keep_ext_on f g t

let rec keep_none (f:string -> bool) (l:list string)
  : Lemma (requires forall (x:string). mem x l ==> not (f x)) (ensures keep f l == [])
  = match l with
    | [] -> ()
    | _ :: t -> keep_none f t

let rec keep_all (f:string -> bool) (l:list string)
  : Lemma (requires forall (x:string). mem x l ==> f x) (ensures keep f l == l)
  = match l with
    | [] -> ()
    | _ :: t -> keep_all f t

(* A list in which nothing outside `f` precedes anything inside it is its `f` part, then the rest. *)
let rec partition_before (f g:string -> bool) (l:list string)
  : Lemma (requires distinct l /\ (forall (x:string). mem x l ==> g x == not (f x)) /\
                    (forall (y z:string). mem y l /\ mem z l /\ not (f y) /\ f z ==> before z y l))
          (ensures l == app (keep f l) (keep g l))
  = match l with
    | [] -> ()
    | h :: t ->
      if f h then begin
        let aux (y z:string) : Lemma (mem y t /\ mem z t /\ not (f y) /\ f z ==> before z y t) =
          if mem y t && mem z t && not (f y) && f z then begin
            assert (before z y l);
            assert (z =!= h)
          end
        in
        FStar.Classical.forall_intro_2 aux;
        partition_before f g t
      end
      else begin
        let none (z:string) : Lemma (mem z t ==> not (f z)) =
          if mem z t && f z then assert (before z h l) in
        FStar.Classical.forall_intro none;
        keep_none f t;
        keep_all g t
      end

let rec replay_ids_app (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (ns:list (node op)) (a b:list string) (s:state)
  : Lemma (ensures replay_ids apply ns (app a b) s
                   == bind (replay_ids apply ns a s) (replay_ids apply ns b))
          (decreases a)
  = match a with
    | [] -> ()
    | id :: t ->
      (match lookup ns id with
       | Missing -> replay_ids_app apply ns t b s
       | Found n ->
         (match apply n.nop s with
          | Ok s' -> replay_ids_app apply ns t b s'
          | Error _ -> ()))

(* Every id the above-check passes descends from the checkpoint's node. *)
let rec exists_parent_anc (#op:eqtype) (d:dag op) (fuel f':list (node op)) (ps:list string)
  (c:string) (desc:list string)
  : Lemma (requires exists_parent ps c desc /\ walk_all_ok d f' ps /\ at_least fuel f' /\
                    Found? (lookup d.nodes c) /\
                    (forall (p:string). mem p desc ==> mem c (ancestors_of d fuel p)))
          (ensures mem c (ancestors_all d f' ps))
          (decreases ps)
  = match ps with
    | [] -> ()
    | p :: t ->
      if p = c then begin
        (match f' with
         | [] -> ()
         | _ :: _ -> assert (mem c (ancestors_of d f' c)))
      end
      else if mem p desc then begin
        anc_stable d f' fuel p;
        assert (mem c (ancestors_of d f' p))
      end
      else exists_parent_anc d fuel f' t c desc

let rec above_descends (#op:eqtype) (d:dag op) (fuel:list (node op)) (c:string)
  (desc ids:list string)
  : Lemma (requires above_go d c desc ids == Missing /\ Found? (lookup d.nodes c) /\
                    (forall (y:string). mem y ids ==> walk_ok d fuel y) /\
                    (forall (p:string). mem p desc ==> mem c (ancestors_of d fuel p)))
          (ensures forall (y:string). mem y ids ==> mem c (ancestors_of d fuel y))
          (decreases ids)
  = match ids with
    | [] -> ()
    | id :: rest ->
      (match lookup d.nodes id with
       | Missing -> ()
       | Found n ->
         (match fuel with
          | [] -> ()
          | x :: f' ->
            at_least_refl f';
            at_least_cons x f' f';
            exists_parent_anc d fuel f' n.nparents c desc;
            assert (mem c (ancestors_of d fuel id));
            above_descends d fuel c (id :: desc) rest))

(* THE SPLIT. When every id of the history above `c` descends from `c`, the head's drain is
   `c`'s own drain, then that history: nothing above the checkpoint is drained before any of
   its closure. *)
#push-options "--z3rlimit 200"
let drain_split (#op:eqtype) (lt:string -> string -> bool)
  (kfuel:list (node op)) (d:dag op) (fuel:list (node op)) (c h:string) (wh:list string)
  : Lemma (requires total_order lt /\ distinct_ids d /\ walk_ok d fuel h /\
                    head_drains d fuel kfuel h wh /\
                    mem c (ancestors_of d fuel h) /\
                    (forall (y:string). mem y (diff (drain_order lt kfuel (closure_of d fuel h))
                                                    (ancestors_of d fuel c))
                                        ==> mem c (ancestors_of d fuel y)))
          (ensures (let ordh = drain_order lt kfuel (closure_of d fuel h) in
                    is_topo_enum (closure_of d fuel h) ordh /\
                    (forall (x:string). mem x ordh == mem x (ids_of (closure_of d fuel h))) /\
                    ordh == app (drain_order lt kfuel (closure_of d fuel c))
                                (diff ordh (ancestors_of d fuel c))))
  = let cnh = closure_of d fuel h in
    let ah = ancestors_of d fuel h in
    let ac = ancestors_of d fuel c in
    let ordh = drain_order lt kfuel cnh in
    distinct_closure_nodes d.nodes ah;
    drain_total_on_acyclic lt kfuel cnh wh;
    let delta = diff ordh ac in
    at_least_refl fuel;
    let cnc = closure_of d fuel c in
    let cS = ids_of cnc in
    drain_restricts lt kfuel d fuel c h;
    let ordc = drain_order lt kfuel cnc in
    let memb (x:string) : Lemma (mem x ordh ==> out_of ac x == not (in_set cS x)) =
      if mem x ordh then begin
        mem_ids_closure_nodes d.nodes ah x;
        mem_ids_closure_nodes d.nodes ac x
      end in
    FStar.Classical.forall_intro memb;
    let ord_ok (y z:string)
      : Lemma (mem y ordh /\ mem z ordh /\ not (in_set cS y) /\ in_set cS z ==> before z y ordh) =
      if mem y ordh && mem z ordh && not (in_set cS y) && in_set cS z then begin
        mem_ids_closure_nodes d.nodes ah y;
        mem_ids_closure_nodes d.nodes ac z;
        mem_ids_closure_nodes d.nodes ac y;
        assert (mem y delta);
        walk_ok_anc d fuel fuel h y;
        anc_trans d fuel fuel y c z;
        anc_before d fuel fuel h ordh y z
      end in
    FStar.Classical.forall_intro_2 ord_ok;
    partition_before (in_set cS) (out_of ac) ordh;
    diff_is_keep ordh ac
#pop-options

(* THEOREM 1. Replay from a checkpoint `checkpointAt` sealed is replay from genesis: on every head
   `replayFrom` does not refuse as unreached or uncovered, its answer is `tryReplayTo`'s, the
   state on `Ok` and the rejecting node and reason on `Rejected`. *)
#push-options "--z3rlimit 200"
let replay_from_is_replay_to (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (lt:string -> string -> bool)
  (kfuel:list (node op)) (d:dag op) (fuel:list (node op)) (seal:string -> state -> string)
  (s0:state) (c h:string) (wc wh:list string) (cp:checkpoint state)
  : Lemma (requires total_order lt /\ distinct_ids d /\ walk_ok d fuel h /\
                    Found? (lookup d.nodes h) /\
                    head_drains d fuel kfuel h wh /\ head_drains d fuel kfuel c wc /\
                    checkpoint_at apply lt kfuel d fuel seal s0 c == Ok cp)
          (ensures (match replay_from apply lt kfuel d fuel cp h with
                    | Error (CUnreached _) | Error (CUncovered _) -> True
                    | r -> r == lift (replay_to apply lt kfuel d fuel s0 h)))
  = let cnh = closure_of d fuel h in
    let ah = ancestors_of d fuel h in
    let ac = ancestors_of d fuel c in
    let ordh = drain_order lt kfuel cnh in
    replay_to_is_fold_over_drain apply lt kfuel d fuel s0 h wh;
    distinct_closure_nodes d.nodes ah;
    drain_total_on_acyclic lt kfuel cnh wh;
    sub_ids_of_mem (ids_of cnh) ordh;
    if not (mem c ordh) then ()
    else begin
      mem_ids_closure_nodes d.nodes ah c;
      let delta = diff ordh ac in
      match above_go d c [] delta with
      | Found _ -> ()
      | Missing ->
        at_least_refl fuel;
        let wk (y:string) : Lemma (mem y delta ==> walk_ok d fuel y) =
          if mem y delta then begin
            mem_ids_closure_nodes d.nodes ah y;
            walk_ok_anc d fuel fuel h y
          end in
        FStar.Classical.forall_intro wk;
        lookup_of_mem_ids d.nodes c;
        above_descends d fuel c [] delta;
        replay_to_is_fold_over_drain apply lt kfuel d fuel s0 c wc;
        drain_split lt kfuel d fuel c h wh;
        let ordc = drain_order lt kfuel (closure_of d fuel c) in
        replay_ids_app apply d.nodes ordc delta s0
    end
#pop-options

(* F#: `checkpointFrom` — an absent node named as `checkpointAt` names it (where the origin's node
   is held), then `replayFrom` from the origin, sealed at the node. *)
let checkpoint_from (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (lt:string -> string -> bool)
  (kfuel:list (node op)) (d:dag op) (fuel:list (node op)) (seal:string -> state -> string)
  (origin:checkpoint state) (n:string) : Tot (outcome (checkpoint state) (ck_fault rej)) =
  if Found? (lookup d.nodes origin.cnode) && Missing? (lookup d.nodes n) then Error (CUnknownNode n)
  else
    match replay_from apply lt kfuel d fuel origin n with
    | Ok st -> Ok (seal_at seal n st)
    | Error f -> Error f

(* COROLLARY. A checkpoint taken from an earlier one is the checkpoint taken from genesis: the
   same node, the same state, the same seal. *)
let checkpoint_from_is_checkpoint_at (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (lt:string -> string -> bool)
  (kfuel:list (node op)) (d:dag op) (fuel:list (node op)) (seal:string -> state -> string)
  (s0:state) (c n:string) (wc wn:list string) (cp:checkpoint state)
  : Lemma (requires total_order lt /\ distinct_ids d /\ walk_ok d fuel n /\
                    Found? (lookup d.nodes n) /\
                    head_drains d fuel kfuel n wn /\ head_drains d fuel kfuel c wc /\
                    checkpoint_at apply lt kfuel d fuel seal s0 c == Ok cp)
          (ensures (match replay_from apply lt kfuel d fuel cp n with
                    | Error (CUnreached _) | Error (CUncovered _) -> True
                    | _ ->
                      (match checkpoint_from apply lt kfuel d fuel seal cp n,
                             checkpoint_at apply lt kfuel d fuel seal s0 n with
                       | Ok a, Ok b -> a.cnode == b.cnode /\ a.cstate == b.cstate /\ a.cseal == b.cseal
                       | Error f, Error g -> f == g
                       | _, _ -> False)))
  = replay_from_is_replay_to apply lt kfuel d fuel seal s0 c n wc wn cp

(* ---- 21.6 the drain after a prefix ---- *)

(* F#: nothing — the ids a drain has emitted, taken out of its work list one at a time. *)
let rec remove_all (#op:eqtype) (r:list (node op)) (p:list string) : Tot (list (node op)) (decreases p) =
  match p with
  | [] -> r
  | x :: t -> remove_all (remove_id r x) t

(* The work list with every id of `p` dropped. *)
let rec drop_nodes (#op:eqtype) (r:list (node op)) (p:list string) : Tot (list (node op)) =
  match r with
  | [] -> []
  | n :: t -> if mem n.nid p then drop_nodes t p else n :: drop_nodes t p

let rec len_drop_by (#a #b:Type) (f:list a) (p:list b)
  : Lemma (requires len f >= len p) (ensures len (drop_by f p) + len p == len f) (decreases p)
  = match p with
    | [] -> ()
    | _ :: t -> (match f with | [] -> () | _ :: f' -> len_drop_by f' t)

(* THE PREFIX LEMMA. Whatever a drain emitted first, the rest of its output is the drain resumed
   from the state that prefix left: those ids emitted and gone from the work list. *)
let rec kahn_after (#op:eqtype) (pick:list string -> string) (f:list (node op))
  (r:list (node op)) (c e p q:list string)
  : Lemma (requires picks_from_frontier pick /\ kahn pick f r c e == app p q)
          (ensures q == kahn pick (drop_by f p) (remove_all r p) c (app e p) /\
                   (len f >= len r ==> len (drop_by f p) >= len (remove_all r p)))
          (decreases p)
  = match p with
    | [] -> app_nil_r e
    | x :: p' ->
      (match f with
       | [] -> ()
       | _ :: f' ->
         let fr = frontier r c e in
         (match fr with
          | [] -> ()
          | _ :: _ ->
            let id = pick fr in
            assert (id == x);
            frontier_sub r c e x;
            len_remove_id r x;
            app_assoc e [ x ] p';
            kahn_after pick f' (remove_id r x) c (app e [ x ]) p' q))

let rec drop_nodes_ext (#op:eqtype) (r:list (node op)) (p p':list string)
  : Lemma (requires forall (n:node op). mem n r ==> mem n.nid p == mem n.nid p')
          (ensures drop_nodes r p == drop_nodes r p')
  = match r with
    | [] -> ()
    | _ :: t -> drop_nodes_ext t p p'

let rec drop_nodes_remove (#op:eqtype) (r:list (node op)) (x:string) (p:list string)
  : Lemma (requires distinct (ids_of r))
          (ensures drop_nodes (remove_id r x) p == drop_nodes r (x :: p))
  = match r with
    | [] -> ()
    | n :: t ->
      if n.nid = x then begin
        let aux (m:node op) : Lemma (mem m t ==> mem m.nid p == mem m.nid (x :: p)) =
          if mem m t then mem_ids_of m t in
        FStar.Classical.forall_intro aux;
        drop_nodes_ext t p (x :: p)
      end
      else drop_nodes_remove t x p

let rec remove_all_is_drop (#op:eqtype) (r:list (node op)) (p:list string)
  : Lemma (requires distinct (ids_of r)) (ensures remove_all r p == drop_nodes r p) (decreases p)
  = match p with
    | [] ->
      let rec none (r:list (node op)) : Lemma (ensures drop_nodes r [] == r) =
        match r with [] -> () | _ :: t -> none t in
      none r
    | x :: t ->
      ids_of_remove_id r x;
      distinct_remove_first (ids_of r) x;
      remove_all_is_drop (remove_id r x) t;
      drop_nodes_remove r x t

(* ---- 21.7 the drain reads only what is still pending, and not its fuel ---- *)

let pending (p:string) (c e:list string) : Tot bool = mem p c && not (mem p e)

let rec ready_pending (ps c1 e1 c2 e2:list string)
  : Lemma (requires forall (p:string). mem p ps ==> pending p c1 e1 == pending p c2 e2)
          (ensures all_emitted (parents_in ps c1) e1 == all_emitted (parents_in ps c2) e2)
  = match ps with
    | [] -> ()
    | _ :: t -> ready_pending t c1 e1 c2 e2

let rec frontier_pending (#op:eqtype) (r:list (node op)) (c1 e1 c2 e2:list string)
  : Lemma (requires forall (n:node op) (p:string). mem n r /\ mem p n.nparents
                                                   ==> pending p c1 e1 == pending p c2 e2)
          (ensures frontier r c1 e1 == frontier r c2 e2)
  = match r with
    | [] -> ()
    | n :: t -> ready_pending n.nparents c1 e1 c2 e2; frontier_pending t c1 e1 c2 e2

let rec kahn_pending (#op:eqtype) (pick:list string -> string) (f:list (node op))
  (r:list (node op)) (c1 e1 c2 e2:list string)
  : Lemma (requires forall (n:node op) (p:string). mem n r /\ mem p n.nparents
                                                   ==> pending p c1 e1 == pending p c2 e2)
          (ensures kahn pick f r c1 e1 == kahn pick f r c2 e2)
          (decreases f)
  = frontier_pending r c1 e1 c2 e2;
    match f with
    | [] -> ()
    | _ :: f' ->
      (match frontier r c1 e1 with
       | [] -> ()
       | fr ->
         let x = pick fr in
         let aux (n:node op) (p:string)
           : Lemma (mem n (remove_id r x) /\ mem p n.nparents
                    ==> pending p c1 (app e1 [ x ]) == pending p c2 (app e2 [ x ])) =
           if mem n (remove_id r x) && mem p n.nparents then begin
             mem_remove_id_sub r x n;
             mem_app p e1 [ x ]; mem_app p e2 [ x ]
           end in
         FStar.Classical.forall_intro_2 aux;
         kahn_pending pick f' (remove_id r x) c1 (app e1 [ x ]) c2 (app e2 [ x ]))

let rec kahn_fuel (#op:eqtype) (pick:list string -> string) (f g:list (node op))
  (r:list (node op)) (c e:list string)
  : Lemma (requires picks_from_frontier pick /\ len f >= len r /\ len g >= len r)
          (ensures kahn pick f r c e == kahn pick g r c e)
          (decreases f)
  = match f, g with
    | [], _ -> assert (r == []); (match g with | [] -> () | _ :: _ -> ())
    | _, [] -> assert (r == [])
    | _ :: f', _ :: g' ->
      (match frontier r c e with
       | [] -> ()
       | fr ->
         let x = pick fr in
         frontier_sub r c e x;
         len_remove_id r x;
         kahn_fuel pick f' g' (remove_id r x) c (app e [ x ]))

(* ---- 21.8 the truncation (F#: `cut`, `compactAt`) ---- *)

(* F#: the DAG `cut` returns — `dag.Nodes |> Map.filter (fun id _ -> Set.contains id keep)`. *)
let truncate (#op:eqtype) (d:dag op) (keep:list string) : Tot (dag op) =
  { nodes = closure_nodes d.nodes keep }

(* What `cut` guarantees of the set it keeps, read node by node so it is computable: every node
   NOT behind the checkpoint's node is kept (`cut` refuses a DAG holding a node neither behind nor
   after, and keeps everything after), and every node behind it that is kept still reaches it in
   the truncated DAG (the band: a side parent and the path from it to the node). *)
let rec trunc_ok_nodes (#op:eqtype) (d:dag op) (fuel:list (node op)) (c:string)
  (keep:list string) (ns:list (node op)) : Tot bool =
  match ns with
  | [] -> true
  | n :: t ->
    (if mem n.nid (ancestors_of d fuel c)
     then (not (mem n.nid keep) || mem n.nid (ancestors_of (truncate d keep) fuel c))
     else mem n.nid keep)
    && trunc_ok_nodes d fuel c keep t

let truncation_ok (#op:eqtype) (d:dag op) (fuel:list (node op)) (c:string) (keep:list string)
  : Tot bool =
  mem c keep && trunc_ok_nodes d fuel c keep d.nodes

let rec trunc_ok_at (#op:eqtype) (d:dag op) (fuel:list (node op)) (c:string)
  (keep:list string) (ns:list (node op)) (n:node op)
  : Lemma (requires trunc_ok_nodes d fuel c keep ns /\ mem n ns)
          (ensures (if mem n.nid (ancestors_of d fuel c)
                    then (not (mem n.nid keep) || mem n.nid (ancestors_of (truncate d keep) fuel c))
                    else mem n.nid keep))
  = match ns with
    | [] -> ()
    | m :: t -> if m = n then () else trunc_ok_at d fuel c keep t n

let rec lookup_closure_nodes (#op:eqtype) (ns:list (node op)) (k:list string) (y:string)
  : Lemma (ensures lookup (closure_nodes ns k) y == (if mem y k then lookup ns y else Missing))
  = match ns with
    | [] -> ()
    | n :: t -> lookup_closure_nodes t k y

let anc_head_found (#op:eqtype) (d:dag op) (f:list (node op)) (y x:string)
  : Lemma (requires mem x (ancestors_of d f y)) (ensures Found? (lookup d.nodes y))
  = match f with
    | [] -> ()
    | _ :: _ -> ()

(* The walk only grows with its fuel. *)
let rec anc_mono (#op:eqtype) (d:dag op) (f big:list (node op)) (y x:string)
  : Lemma (requires at_least big f /\ mem x (ancestors_of d f y))
          (ensures mem x (ancestors_of d big y))
          (decreases %[f; (0 <: nat); ([] <: list string)])
  = match f with
    | [] -> ()
    | _ :: f' ->
      (match big with
       | [] -> ()
       | _ :: big' ->
         (match lookup d.nodes y with
          | Missing -> ()
          | Found n -> if x = y then () else anc_all_mono d f' big' n.nparents x))

and anc_all_mono (#op:eqtype) (d:dag op) (f big:list (node op)) (ids:list string) (x:string)
  : Lemma (requires at_least big f /\ mem x (ancestors_all d f ids))
          (ensures mem x (ancestors_all d big ids))
          (decreases %[f; (1 <: nat); ids])
  = match ids with
    | [] -> ()
    | p :: t ->
      if mem x (ancestors_of d f p) then anc_mono d f big p x
      else anc_all_mono d f big t x

(* The truncated DAG's walk is inside the full one's. *)
let rec anc_sub (#op:eqtype) (d:dag op) (keep:list string) (f:list (node op)) (y x:string)
  : Lemma (requires mem x (ancestors_of (truncate d keep) f y))
          (ensures mem x (ancestors_of d f y))
          (decreases %[f; (0 <: nat); ([] <: list string)])
  = lookup_closure_nodes d.nodes keep y;
    match f with
    | [] -> ()
    | _ :: f' ->
      (match lookup (truncate d keep).nodes y with
       | Missing -> ()
       | Found n -> if x = y then () else anc_all_sub d keep f' n.nparents x)

and anc_all_sub (#op:eqtype) (d:dag op) (keep:list string) (f:list (node op)) (ids:list string)
  (x:string)
  : Lemma (requires mem x (ancestors_all (truncate d keep) f ids))
          (ensures mem x (ancestors_all d f ids))
          (decreases %[f; (1 <: nat); ids])
  = match ids with
    | [] -> ()
    | p :: t ->
      if mem x (ancestors_of (truncate d keep) f p) then anc_sub d keep f p x
      else anc_all_sub d keep f t x

let rec walk_ok_sub (#op:eqtype) (d:dag op) (keep:list string) (f:list (node op)) (y:string)
  : Lemma (requires walk_ok d f y) (ensures walk_ok (truncate d keep) f y)
          (decreases %[f; (0 <: nat); ([] <: list string)])
  = lookup_closure_nodes d.nodes keep y;
    match f with
    | [] -> ()
    | _ :: f' ->
      (match lookup (truncate d keep).nodes y with
       | Missing -> ()
       | Found n -> walk_all_ok_sub d keep f' n.nparents)

and walk_all_ok_sub (#op:eqtype) (d:dag op) (keep:list string) (f:list (node op)) (ids:list string)
  : Lemma (requires walk_all_ok d f ids) (ensures walk_all_ok (truncate d keep) f ids)
          (decreases %[f; (1 <: nat); ids])
  = match ids with
    | [] -> ()
    | p :: t -> walk_ok_sub d keep f p; walk_all_ok_sub d keep f t

(* A walk whose every step is kept survives the truncation. *)
let kept_above (#op:eqtype) (d:dag op) (fuel:list (node op)) (keep:list string) (x:string) : prop =
  forall (g:list (node op)) (z:string).
    at_least fuel g /\ mem x (ancestors_of d g z) ==> mem z keep

let rec anc_kept (#op:eqtype) (d:dag op) (fuel:list (node op)) (keep:list string)
  (f:list (node op)) (y x:string)
  : Lemma (requires kept_above d fuel keep x /\ at_least fuel f /\ mem x (ancestors_of d f y))
          (ensures mem x (ancestors_of (truncate d keep) f y))
          (decreases %[f; (0 <: nat); ([] <: list string)])
  = lookup_closure_nodes d.nodes keep y;
    match f with
    | [] -> ()
    | _ :: f' ->
      (match lookup d.nodes y with
       | Missing -> ()
       | Found n ->
         assert (mem y keep);
         if x = y then ()
         else begin
           at_least_tail fuel f;
           anc_all_kept d fuel keep f' n.nparents x
         end)

and anc_all_kept (#op:eqtype) (d:dag op) (fuel:list (node op)) (keep:list string)
  (f:list (node op)) (ids:list string) (x:string)
  : Lemma (requires kept_above d fuel keep x /\ at_least fuel f /\ mem x (ancestors_all d f ids))
          (ensures mem x (ancestors_all (truncate d keep) f ids))
          (decreases %[f; (1 <: nat); ids])
  = match ids with
    | [] -> ()
    | p :: t ->
      if mem x (ancestors_of d f p) then anc_kept d fuel keep f p x
      else anc_all_kept d fuel keep f t x

let rec above_go_keep (#op:eqtype) (d:dag op) (keep:list string) (c:string)
  (desc ids:list string)
  : Lemma (requires forall (y:string). mem y ids ==> mem y keep)
          (ensures above_go (truncate d keep) c desc ids == above_go d c desc ids)
          (decreases ids)
  = match ids with
    | [] -> ()
    | id :: rest ->
      lookup_closure_nodes d.nodes keep id;
      above_go_keep d keep c (id :: desc) rest

let rec replay_ids_keep (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (d:dag op) (keep:list string) (ids:list string)
  (s:state)
  : Lemma (requires forall (y:string). mem y ids ==> mem y keep)
          (ensures replay_ids apply (truncate d keep).nodes ids s == replay_ids apply d.nodes ids s)
          (decreases ids)
  = match ids with
    | [] -> ()
    | id :: t ->
      lookup_closure_nodes d.nodes keep id;
      (match lookup d.nodes id with
       | Missing -> replay_ids_keep apply d keep t s
       | Found n ->
         (match apply n.nop s with
          | Ok s' -> replay_ids_keep apply d keep t s'
          | Error _ -> ()))

let rec drop_closure_ext (#op:eqtype) (ns:list (node op)) (a p k a' p':list string)
  : Lemma (requires forall (n:node op). mem n ns ==>
                      (mem n.nid a && not (mem n.nid p))
                      == (mem n.nid k && mem n.nid a' && not (mem n.nid p')))
          (ensures drop_nodes (closure_nodes ns a) p
                   == drop_nodes (closure_nodes (closure_nodes ns k) a') p')
  = match ns with
    | [] -> ()
    | _ :: t -> drop_closure_ext t a p k a' p'

(* The region above the checkpoint is the same id set in the full DAG and the truncated one. *)
#push-options "--z3rlimit 150"
let region_same (#op:eqtype) (d:dag op) (fuel:list (node op)) (keep:list string)
  (c h:string) (x:string)
  : Lemma (requires Cons? fuel /\ walk_ok d fuel h /\ mem c (ancestors_of d fuel h) /\
                    truncation_ok d fuel c keep /\ Found? (lookup d.nodes x))
          (ensures (mem x (ancestors_of d fuel h) && not (mem x (ancestors_of d fuel c)))
                   == (mem x keep && mem x (ancestors_of (truncate d keep) fuel h) &&
                       not (mem x (ancestors_of (truncate d keep) fuel c))))
  = let d' = truncate d keep in
    let ah = ancestors_of d fuel h in
    let ac = ancestors_of d fuel c in
    at_least_refl fuel;
    walk_ok_anc d fuel fuel h c;
    let Found nx = lookup d.nodes x in
    lookup_found d.nodes x nx;
    trunc_ok_at d fuel c keep d.nodes nx;
    if mem x ah && not (mem x ac) then begin
      let ka (g:list (node op)) (z:string)
        : Lemma (at_least fuel g /\ mem x (ancestors_of d g z) ==> mem z keep) =
        if at_least fuel g && mem x (ancestors_of d g z) then begin
          anc_mono d g fuel z x;
          anc_head_found d g z x;
          let Found nz = lookup d.nodes z in
          lookup_found d.nodes z nz;
          trunc_ok_at d fuel c keep d.nodes nz;
          if not (mem z keep) then anc_trans d fuel fuel c z x
        end in
      FStar.Classical.forall_intro_2 ka;
      anc_kept d fuel keep fuel h x;
      if mem x (ancestors_of d' fuel c) then anc_sub d keep fuel c x
    end
    else if mem x keep && mem x (ancestors_of d' fuel h) && not (mem x (ancestors_of d' fuel c))
    then anc_sub d keep fuel h x
#pop-options

(* Every id that reaches `c` in the full DAG reaches it in the truncated one. *)
let reach_c_kept (#op:eqtype) (d:dag op) (fuel:list (node op)) (keep:list string)
  (c h:string) (wh:list string) (y:string)
  : Lemma (requires Cons? fuel /\ walk_ok d fuel h /\ mem c (ancestors_of d fuel h) /\
                    is_topo_enum (closure_nodes d.nodes (ancestors_of d fuel h)) wh /\
                    truncation_ok d fuel c keep /\ mem c (ancestors_of d fuel y))
          (ensures mem c (ancestors_of (truncate d keep) fuel y))
  = at_least_refl fuel;
    walk_ok_anc d fuel fuel h c;
    anc_found d fuel h c;
    let Found nc = lookup d.nodes c in
    lookup_found d.nodes c nc;
    off_cycle_in_witness d fuel h wh nc;
    let ka (g:list (node op)) (z:string)
      : Lemma (at_least fuel g /\ mem c (ancestors_of d g z) ==> mem z keep) =
      if at_least fuel g && mem c (ancestors_of d g z) then begin
        anc_mono d g fuel z c;
        anc_head_found d g z c;
        let Found nz = lookup d.nodes z in
        lookup_found d.nodes z nz;
        trunc_ok_at d fuel c keep d.nodes nz
      end in
    FStar.Classical.forall_intro_2 ka;
    anc_kept d fuel keep fuel y c

(* One DAG: the history above `c`, when all of it descends from `c`, is the drain resumed after
   `c`'s own drain — work list and emitted set named. *)
#push-options "--z3rlimit 150"
let above_is_resumed (#op:eqtype) (lt:string -> string -> bool)
  (kfuel:list (node op)) (d:dag op) (fuel:list (node op)) (c h:string) (wh:list string)
  : Lemma (requires total_order lt /\ distinct_ids d /\ walk_ok d fuel h /\
                    head_drains d fuel kfuel h wh /\ mem c (ancestors_of d fuel h) /\
                    (forall (y:string). mem y (diff (drain_order lt kfuel (closure_of d fuel h))
                                                    (ancestors_of d fuel c))
                                        ==> mem c (ancestors_of d fuel y)))
          (ensures (let cnh = closure_of d fuel h in
                    let ordh = drain_order lt kfuel cnh in
                    let ordc = drain_order lt kfuel (closure_of d fuel c) in
                    diff ordh (ancestors_of d fuel c)
                    == kahn (pick_min lt) (drop_by kfuel ordc) (drop_nodes cnh ordc) (ids_of cnh) ordc /\
                    len (drop_by kfuel ordc) >= len (drop_nodes cnh ordc) /\
                    (forall (x:string). mem x ordc == mem x (ids_of (closure_of d fuel c))) /\
                    (forall (x:string). mem x ordh == mem x (ids_of cnh))))
  = let cnh = closure_of d fuel h in
    let cnc = closure_of d fuel c in
    let ordh = drain_order lt kfuel cnh in
    let ordc = drain_order lt kfuel cnc in
    pick_min_is_a_tie_break lt;
    drain_split lt kfuel d fuel c h wh;
    drain_restricts lt kfuel d fuel c h;
    distinct_closure_nodes d.nodes (ancestors_of d fuel h);
    covers_len kfuel (ids_of cnh);
    len_ids_of cnh;
    kahn_after (pick_min lt) kfuel cnh (ids_of cnh) [] ordc (diff ordh (ancestors_of d fuel c));
    remove_all_is_drop cnh ordc;
    let memc (x:string) : Lemma (mem x ordc == mem x (ids_of cnc)) =
      mem_keep (in_set (ids_of cnc)) ordh x;
      at_least_refl fuel;
      walk_ok_anc d fuel fuel h c;
      mem_ids_closure_nodes d.nodes (ancestors_of d fuel c) x;
      mem_ids_closure_nodes d.nodes (ancestors_of d fuel h) x;
      if mem x (ancestors_of d fuel c) then anc_trans d fuel fuel h c x in
    FStar.Classical.forall_intro memc
#pop-options

(* Two drains over one work list, pending the same parents, with fuel enough, emit the same. *)
let kahn_same (#op:eqtype) (lt:string -> string -> bool) (f1 f2:list (node op))
  (r:list (node op)) (c1 e1 c2 e2:list string)
  : Lemma (requires len f1 >= len r /\ len f2 >= len r /\
                    (forall (p:string). pending p c1 e1 == pending p c2 e2))
          (ensures kahn (pick_min lt) f1 r c1 e1 == kahn (pick_min lt) f2 r c2 e2)
  = pick_min_is_a_tie_break lt;
    kahn_fuel (pick_min lt) f1 f2 r c1 e1;
    kahn_pending (pick_min lt) f2 r c1 e1 c2 e2

let ids_closure_trunc (#op:eqtype) (d:dag op) (keep a:list string) (x:string)
  : Lemma (ensures mem x (ids_of (closure_nodes (truncate d keep).nodes a))
                   == (mem x (ids_of d.nodes) && mem x keep && mem x a))
  = mem_ids_closure_nodes (truncate d keep).nodes a x;
    mem_ids_closure_nodes d.nodes keep x

let held_ids (#op:eqtype) (d:dag op) (x:string)
  : Lemma (ensures mem x (ids_of d.nodes) == Found? (lookup d.nodes x))
  = match lookup d.nodes x with
    | Missing -> if mem x (ids_of d.nodes) then lookup_of_mem_ids d.nodes x
    | Found n -> lookup_found d.nodes x n

let trunc_premises (#op:eqtype) (d:dag op) (fuel:list (node op)) (keep:list string) (c h:string)
  : prop =
  Cons? fuel /\ walk_ok d fuel h /\ mem c (ancestors_of d fuel h) /\ truncation_ok d fuel c keep /\
  distinct_ids d

#push-options "--z3rlimit 100"
let work_list_same (#op:eqtype) (d:dag op) (fuel:list (node op)) (keep:list string)
  (c h:string) (ordc ordc':list string) (n:node op)
  : Lemma (requires trunc_premises d fuel keep c h /\ mem n d.nodes /\
                    (forall (x:string). mem x ordc == mem x (ids_of (closure_of d fuel c))) /\
                    (forall (x:string). mem x ordc' == mem x (ids_of (closure_of (truncate d keep) fuel c))))
          (ensures (mem n.nid (ancestors_of d fuel h) && not (mem n.nid ordc))
                   == (mem n.nid keep && mem n.nid (ancestors_of (truncate d keep) fuel h) &&
                       not (mem n.nid ordc')))
  = mem_ids_of n d.nodes;
    held_ids d n.nid;
    mem_ids_closure_nodes d.nodes (ancestors_of d fuel c) n.nid;
    ids_closure_trunc d keep (ancestors_of (truncate d keep) fuel c) n.nid;
    region_same d fuel keep c h n.nid

let pending_same (#op:eqtype) (d:dag op) (fuel:list (node op)) (keep:list string)
  (c h:string) (ordc ordc':list string) (p:string)
  : Lemma (requires trunc_premises d fuel keep c h /\
                    (forall (x:string). mem x ordc == mem x (ids_of (closure_of d fuel c))) /\
                    (forall (x:string). mem x ordc' == mem x (ids_of (closure_of (truncate d keep) fuel c))))
          (ensures pending p (ids_of (closure_of d fuel h)) ordc
                   == pending p (ids_of (closure_of (truncate d keep) fuel h)) ordc')
  = held_ids d p;
    mem_ids_closure_nodes d.nodes (ancestors_of d fuel h) p;
    mem_ids_closure_nodes d.nodes (ancestors_of d fuel c) p;
    ids_closure_trunc d keep (ancestors_of (truncate d keep) fuel h) p;
    ids_closure_trunc d keep (ancestors_of (truncate d keep) fuel c) p;
    if Found? (lookup d.nodes p) then region_same d fuel keep c h p
#pop-options

(* The history above the checkpoint is the same list in both DAGs. *)
#push-options "--z3rlimit 150"
let above_same (#op:eqtype) (lt:string -> string -> bool) (kfuel:list (node op)) (d:dag op)
  (fuel:list (node op)) (keep:list string) (c h:string) (wh wh':list string)
  : Lemma (requires total_order lt /\ trunc_premises d fuel keep c h /\
                    head_drains d fuel kfuel h wh /\
                    head_drains (truncate d keep) fuel kfuel h wh' /\
                    (forall (y:string). mem y (diff (drain_order lt kfuel (closure_of d fuel h))
                                                    (ancestors_of d fuel c))
                                        ==> mem c (ancestors_of d fuel y)))
          (ensures mem c (ancestors_of (truncate d keep) fuel h) /\
                   diff (drain_order lt kfuel (closure_of (truncate d keep) fuel h))
                        (ancestors_of (truncate d keep) fuel c)
                   == diff (drain_order lt kfuel (closure_of d fuel h)) (ancestors_of d fuel c))
  = let d' = truncate d keep in
    let cnh = closure_of d fuel h in
    let cnh' = closure_of d' fuel h in
    let ordh = drain_order lt kfuel cnh in
    let ordh' = drain_order lt kfuel cnh' in
    let ordc = drain_order lt kfuel (closure_of d fuel c) in
    let ordc' = drain_order lt kfuel (closure_of d' fuel c) in
    let delta = diff ordh (ancestors_of d fuel c) in
    let delta' = diff ordh' (ancestors_of d' fuel c) in
    above_is_resumed lt kfuel d fuel c h wh;
    distinct_closure_nodes d.nodes keep;
    walk_ok_sub d keep fuel h;
    reach_c_kept d fuel keep c h wh h;
    let desc' (y:string) : Lemma (mem y delta' ==> mem c (ancestors_of d' fuel y)) =
      if mem y delta' then begin
        distinct_closure_nodes d'.nodes (ancestors_of d' fuel h);
        drain_total_on_acyclic lt kfuel cnh' wh';
        ids_closure_trunc d keep (ancestors_of d' fuel h) y;
        held_ids d y;
        region_same d fuel keep c h y;
        distinct_closure_nodes d.nodes (ancestors_of d fuel h);
        drain_total_on_acyclic lt kfuel cnh wh;
        mem_ids_closure_nodes d.nodes (ancestors_of d fuel h) y;
        assert (mem y delta);
        reach_c_kept d fuel keep c h wh y
      end in
    FStar.Classical.forall_intro desc';
    above_is_resumed lt kfuel d' fuel c h wh';
    let wl (n:node op)
      : Lemma (mem n d.nodes ==>
               (mem n.nid (ancestors_of d fuel h) && not (mem n.nid ordc))
               == (mem n.nid keep && mem n.nid (ancestors_of d' fuel h) && not (mem n.nid ordc'))) =
      if mem n d.nodes then work_list_same d fuel keep c h ordc ordc' n in
    FStar.Classical.forall_intro wl;
    drop_closure_ext d.nodes (ancestors_of d fuel h) ordc keep (ancestors_of d' fuel h) ordc';
    let pe (p:string)
      : Lemma (pending p (ids_of cnh) ordc == pending p (ids_of cnh') ordc') =
      pending_same d fuel keep c h ordc ordc' p in
    FStar.Classical.forall_intro pe;
    kahn_same lt (drop_by kfuel ordc) (drop_by kfuel ordc') (drop_nodes cnh ordc)
      (ids_of cnh) ordc (ids_of cnh') ordc'
#pop-options

(* THEOREM 2. Truncating behind a checkpoint preserves every replay at or after it: `replayFrom` on
   the truncated DAG answers what it answers on the full one, wherever the full one does not refuse
   the head as unreached or uncovered. That the head is KEPT is not a premise: a head the full
   replay resumes from the checkpoint is at or after its node, and the truncation keeps those. The checkpoint's
   state is any state at all — the truncation is the subject here; the seal is theorem 1's. *)
#push-options "--z3rlimit 150"
let truncation_preserves_replay (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (lt:string -> string -> bool)
  (kfuel:list (node op)) (d:dag op) (fuel:list (node op)) (keep:list string)
  (cp:checkpoint state) (h:string) (wh wh':list string)
  : Lemma (requires total_order lt /\ distinct_ids d /\ Cons? fuel /\ walk_ok d fuel h /\
                    Found? (lookup d.nodes h) /\
                    head_drains d fuel kfuel h wh /\
                    head_drains (truncate d keep) fuel kfuel h wh' /\
                    truncation_ok d fuel cp.cnode keep)
          (ensures (match replay_from apply lt kfuel d fuel cp h with
                    | Error (CUnreached _) | Error (CUncovered _) -> True
                    | r -> replay_from apply lt kfuel (truncate d keep) fuel cp h == r))
  = let c = cp.cnode in
    let d' = truncate d keep in
    lookup_closure_nodes d.nodes keep c;
    lookup_closure_nodes d.nodes keep h;
    match lookup d.nodes c with
    | Missing -> ()
    | Found _ ->
      let ah = ancestors_of d fuel h in
      let ac = ancestors_of d fuel c in
      let cnh = closure_of d fuel h in
      let ordh = drain_order lt kfuel cnh in
      distinct_closure_nodes d.nodes ah;
      drain_total_on_acyclic lt kfuel cnh wh;
      sub_ids_of_mem (ids_of cnh) ordh;
      if not (mem c ordh) then ()
      else begin
        mem_ids_closure_nodes d.nodes ah c;
        let delta = diff ordh ac in
        match above_go d c [] delta with
        | Found _ -> ()
        | Missing ->
          at_least_refl fuel;
          let wk (y:string) : Lemma (mem y delta ==> walk_ok d fuel y) =
            if mem y delta then begin
              mem_ids_closure_nodes d.nodes ah y;
              walk_ok_anc d fuel fuel h y
            end in
          FStar.Classical.forall_intro wk;
          lookup_of_mem_ids d.nodes c;
          above_descends d fuel c [] delta;
          above_same lt kfuel d fuel keep c h wh wh';
          (* the truncated side reaches the same branch *)
          let cnh' = closure_of d' fuel h in
          let ordh' = drain_order lt kfuel cnh' in
          distinct_closure_nodes d.nodes keep;
          distinct_closure_nodes d'.nodes (ancestors_of d' fuel h);
          drain_total_on_acyclic lt kfuel cnh' wh';
          sub_ids_of_mem (ids_of cnh') ordh';
          ids_closure_trunc d keep (ancestors_of d' fuel h) c;
          held_ids d c;
          assert (mem c ordh');
          let inkeep (y:string) : Lemma (mem y delta ==> mem y keep) =
            if mem y delta then begin
              mem_ids_closure_nodes d.nodes ah y;
              held_ids d y;
              region_same d fuel keep c h y
            end in
          FStar.Classical.forall_intro inkeep;
          above_go_keep d keep c [] delta;
          replay_ids_keep apply d keep delta cp.cstate
      end
#pop-options

(* ======================================================================================
   22. The lane-rejection report (Phase 582).

   WHAT IS MODELLED. Step 4 of `Dag.reconcileMany` as `reconcileRegion` emits it (F#: its
   `rejected` list): each deduplicated head with its exclusive delta, in head order; the delta
   replayed ON ITS OWN from the state the shared region reached (`fold w sharedState dag ids`,
   section 16's `replay_ids`); a lane that rejects contributes one `LaneRejection` — the head,
   the delta's ops (`opsOf dag ids`), the first node whose op the domain refused and the refusal
   — and the list is sorted by head id (`List.sortWith` over `String.CompareOrdinal`, a stable
   sort: section 11's `sort_by`). `Error (LanesRejected rs)` carries that list when it is
   non-empty; `fold_once`'s `LanesRejected` carries the same lanes and nothing about them, and
   this section says what.

   THE PARAMETERS. `lt` is the id order, assumed `total_order` (section 11's `order_on` at
   `string`); `apply` is any domain reducer — a lane's own replay is a property of the lane, so
   nothing is asked of it, not the diamond; the DAG is any. `heads` and `lanes` are read in step,
   as production zips them, so the theorems ask `len heads == len lanes`, which the partition
   gives by construction (`excl_lanes` is one lane per closure, one closure per deduplicated
   head); "each head once" asks `distinct heads`, which `distinct_go` gives. The rejection type
   has decidable equality (`rej:eqtype`) so that membership in the report can be stated — every
   domain's rejection is a plain value. `lanes_of d` spells the lanes as `fold_once` is handed
   them (`opsOf dag ids`); `lanes_of_is_lanes_by` is the bridge to section 15's `lanes_by opof`
   where `opof` reads the DAG and every id is held.

   WHAT IS PROVED.
     - `lane_rejections_are_the_rejecting_lanes` — the deltas of the report's entries, in head
       order, ARE `rejecting`'s lanes (section 3) over the lanes' ops; so `fold_once` answers
       `LanesRejected` exactly when the report is non-empty, with exactly those lanes
       (`fold_once_lanes_rejected`).
     - `lane_rejections_sound` / `lane_rejections_complete` — an entry is in the report exactly
       for a (head, lane) pair of the partition whose lane `replay_ids` refuses from the shared
       state, naming that refusal's node and rejection and the lane's ops: nothing lost, nothing
       invented.
     - `lane_rejections_heads_distinct` — each head once.
     - `lane_rejection_report_sorted`, `_mem`, `_heads_distinct`, `_unique` — the sorted report has
       the same entries, ascending by head, and is the ONE such list: what a host must emit.
   ====================================================================================== *)

(* F#: `LaneRejection<'Op, 'Rej>`. *)
type lane_rejection (op:eqtype) (rej:eqtype) = {
  lr_head   : string;
  lr_delta  : list op;
  lr_node   : string;
  lr_reject : rej
}

let lr_head_of (#op:eqtype) (#rej:eqtype) (r:lane_rejection op rej) : Tot string = r.lr_head
let lr_delta_of (#op:eqtype) (#rej:eqtype) (r:lane_rejection op rej) : Tot (list op) = r.lr_delta

(* F#: `deltas |> List.map (fun (_, _, ops) -> ops)` — the lanes `fold_once` is handed, each the
   ops of its ids (`opsOf dag ids`). *)
let rec lanes_of (#op:eqtype) (d:dag op) (ls:list (list string)) : Tot (list (list op)) =
  match ls with
  | [] -> []
  | l :: t -> ops_of (nodes_for d l) :: lanes_of d t

(* F#: the `choose` over `deltas` — each lane replayed alone from `s`; its first refusal, with
   the head and the delta, or nothing. *)
let rec lane_rejections (#op:eqtype) (#state:Type) (#rej:eqtype)
  (apply:op -> state -> outcome state rej) (d:dag op)
  (heads:list string) (lanes:list (list string)) (s:state)
  : Tot (list (lane_rejection op rej)) (decreases lanes) =
  match heads, lanes with
  | h :: hs, l :: ls ->
    (match replay_ids apply d.nodes l s with
     | Error (RRejected n e) ->
       { lr_head = h; lr_delta = ops_of (nodes_for d l); lr_node = n; lr_reject = e }
       :: lane_rejections apply d hs ls s
     | _ -> lane_rejections apply d hs ls s)
  | _, _ -> []

(* F#: `rejected`, sorted — `List.sortWith (fun a b -> String.CompareOrdinal(a.Head, b.Head))`. *)
let lane_rejection_report (#op:eqtype) (#state:Type) (#rej:eqtype)
  (apply:op -> state -> outcome state rej) (lt:string -> string -> bool) (d:dag op)
  (heads:list string) (lanes:list (list string)) (s:state)
  : Tot (list (lane_rejection op rej)) =
  sort_by lt lr_head_of (lane_rejections apply d heads lanes s)

(* F#: `List.zip`, as production pairs each head with its delta — truncating, as the model's
   walk does. *)
let rec pairs (hs:list string) (ls:list (list string)) : Tot (list (string & list string)) (decreases ls) =
  match hs, ls with
  | h :: hs', l :: ls' -> (h, l) :: pairs hs' ls'
  | _, _ -> []

(* ---- 22.1 the bridge to section 15's spelling of the lanes ---- *)

let rec ops_of_nodes_for_is_ops_by (#op:eqtype) (d:dag op) (opof:string -> op) (l:list string)
  : Lemma (requires forall (id:string). mem id l ==>
                      (Found? (lookup d.nodes id) /\ (Found?._0 (lookup d.nodes id)).nop == opof id))
          (ensures ops_of (nodes_for d l) == ops_by opof l)
  = match l with
    | [] -> ()
    | _ :: t -> ops_of_nodes_for_is_ops_by d opof t

(* Where `opof` reads the DAG and every id of every lane is held, the two spellings agree. *)
let rec lanes_of_is_lanes_by (#op:eqtype) (d:dag op) (opof:string -> op) (ls:list (list string))
  : Lemma (requires forall (id:string). mem id (concat ls) ==>
                      (Found? (lookup d.nodes id) /\ (Found?._0 (lookup d.nodes id)).nop == opof id))
          (ensures lanes_of d ls == lanes_by opof ls)
  = match ls with
    | [] -> ()
    | l :: t -> ops_of_nodes_for_is_ops_by d opof l; lanes_of_is_lanes_by d opof t

(* ---- 22.2 the report IS `fold_once`'s rejecting lanes, with their heads and refusals ---- *)

(* THEOREM. The deltas of the report's entries, in head order, are `rejecting`'s lanes. *)
let rec lane_rejections_are_the_rejecting_lanes (#op:eqtype) (#state:Type) (#rej:eqtype)
  (apply:op -> state -> outcome state rej) (d:dag op)
  (heads:list string) (lanes:list (list string)) (s:state)
  : Lemma (requires len heads == len lanes)
          (ensures map_list lr_delta_of (lane_rejections apply d heads lanes s) ==
                   rejecting apply s (lanes_of d lanes))
          (decreases lanes)
  = match heads, lanes with
    | h :: hs, l :: ls ->
      replay_ids_is_replay apply d l s;
      lane_rejections_are_the_rejecting_lanes apply d hs ls s
    | _, _ -> ()

(* THEOREM. `fold_once` over the partition's lanes, the sweep having passed: `LanesRejected`
   exactly when the report is non-empty, and the lanes it names are the report's deltas. *)
let fold_once_lanes_rejected (#op:eqtype) (#state:Type) (#rej:eqtype)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint) (d:dag op)
  (heads:list string) (lanes:list (list string)) (s:state)
  : Lemma (requires len heads == len lanes /\ all_conflicts fp (lanes_of d lanes) == [])
          (ensures (match fold_once apply fp s (lanes_of d lanes) with
                    | LanesRejected rs ->
                      Cons? rs /\ rs == map_list lr_delta_of (lane_rejections apply d heads lanes s)
                    | LaneHalted _ -> False
                    | _ -> lane_rejections apply d heads lanes s == []))
  = lane_rejections_are_the_rejecting_lanes apply d heads lanes s;
    match lane_rejections apply d heads lanes s with
    | [] -> ()
    | _ :: _ -> ()

(* THEOREM (nothing invented). Every entry names a (head, lane) pair of the partition whose lane
   `replay_ids` refuses from `s`, with that refusal's node and rejection and the lane's ops. *)
let rec lane_rejections_sound (#op:eqtype) (#state:Type) (#rej:eqtype)
  (apply:op -> state -> outcome state rej) (d:dag op)
  (heads:list string) (lanes:list (list string)) (s:state) (r:lane_rejection op rej)
  : Lemma (requires mem r (lane_rejections apply d heads lanes s))
          (ensures exists (l:list string).
                     mem (r.lr_head, l) (pairs heads lanes) /\
                     replay_ids apply d.nodes l s == Error (RRejected r.lr_node r.lr_reject) /\
                     r.lr_delta == ops_of (nodes_for d l))
          (decreases lanes)
  = match heads, lanes with
    | h :: hs, l :: ls ->
      (match replay_ids apply d.nodes l s with
       | Error (RRejected n e) ->
         if r = { lr_head = h; lr_delta = ops_of (nodes_for d l); lr_node = n; lr_reject = e }
         then assert (mem (r.lr_head, l) (pairs heads lanes))
         else lane_rejections_sound apply d hs ls s r
       | _ -> lane_rejections_sound apply d hs ls s r)
    | _, _ -> ()

(* THEOREM (nothing lost). Every (head, lane) pair whose lane refuses is an entry. *)
let rec lane_rejections_complete (#op:eqtype) (#state:Type) (#rej:eqtype)
  (apply:op -> state -> outcome state rej) (d:dag op)
  (heads:list string) (lanes:list (list string)) (s:state)
  (h:string) (l:list string) (n:string) (e:rej)
  : Lemma (requires mem (h, l) (pairs heads lanes) /\
                    replay_ids apply d.nodes l s == Error (RRejected n e))
          (ensures mem ({ lr_head = h; lr_delta = ops_of (nodes_for d l); lr_node = n; lr_reject = e })
                       (lane_rejections apply d heads lanes s))
          (decreases lanes)
  = match heads, lanes with
    | h' :: hs, l' :: ls ->
      if h = h' && l = l' then () else lane_rejections_complete apply d hs ls s h l n e
    | _, _ -> ()

(* ---- 22.3 each head once ---- *)

let rec lane_rejections_heads_sub (#op:eqtype) (#state:Type) (#rej:eqtype)
  (apply:op -> state -> outcome state rej) (d:dag op)
  (heads:list string) (lanes:list (list string)) (s:state) (x:string)
  : Lemma (requires mem x (keys_of lr_head_of (lane_rejections apply d heads lanes s)))
          (ensures mem x heads)
          (decreases lanes)
  = match heads, lanes with
    | h :: hs, l :: ls ->
      (match replay_ids apply d.nodes l s with
       | Error (RRejected _ _) -> if x = h then () else lane_rejections_heads_sub apply d hs ls s x
       | _ -> lane_rejections_heads_sub apply d hs ls s x)
    | _, _ -> ()

let rec lane_rejections_heads_distinct (#op:eqtype) (#state:Type) (#rej:eqtype)
  (apply:op -> state -> outcome state rej) (d:dag op)
  (heads:list string) (lanes:list (list string)) (s:state)
  : Lemma (requires distinct heads)
          (ensures distinct (keys_of lr_head_of (lane_rejections apply d heads lanes s)))
          (decreases lanes)
  = match heads, lanes with
    | h :: hs, l :: ls ->
      lane_rejections_heads_distinct apply d hs ls s;
      (match replay_ids apply d.nodes l s with
       | Error (RRejected _ _) ->
         if mem h (keys_of lr_head_of (lane_rejections apply d hs ls s))
         then lane_rejections_heads_sub apply d hs ls s h
         else ()
       | _ -> ())
    | _, _ -> ()

(* ---- 22.4 the sorted report ---- *)

let lane_rejection_report_mem (#op:eqtype) (#state:Type) (#rej:eqtype)
  (apply:op -> state -> outcome state rej) (lt:string -> string -> bool) (d:dag op)
  (heads:list string) (lanes:list (list string)) (s:state) (r:lane_rejection op rej)
  : Lemma (ensures mem r (lane_rejection_report apply lt d heads lanes s) ==
                   mem r (lane_rejections apply d heads lanes s))
  = mem_sort_by lt lr_head_of (lane_rejections apply d heads lanes s) r

let lane_rejection_report_sorted (#op:eqtype) (#state:Type) (#rej:eqtype)
  (apply:op -> state -> outcome state rej) (lt:string -> string -> bool) (d:dag op)
  (heads:list string) (lanes:list (list string)) (s:state)
  : Lemma (requires total_order lt)
          (ensures sorted_by lt lr_head_of (lane_rejection_report apply lt d heads lanes s))
  = assert (order_on lt);
    sorted_sort_by lt lr_head_of (lane_rejections apply d heads lanes s)

let lane_rejection_report_heads_distinct (#op:eqtype) (#state:Type) (#rej:eqtype)
  (apply:op -> state -> outcome state rej) (lt:string -> string -> bool) (d:dag op)
  (heads:list string) (lanes:list (list string)) (s:state)
  : Lemma (requires distinct heads)
          (ensures distinct (keys_of lr_head_of (lane_rejection_report apply lt d heads lanes s)))
  = lane_rejections_heads_distinct apply d heads lanes s;
    distinct_keys_sort_by lt lr_head_of (lane_rejections apply d heads lanes s)

(* THEOREM. The report is the ONE list of the entries ascending by head: a host that emits the
   entries sorted by head id emits this list and no other. *)
let lane_rejection_report_unique (#op:eqtype) (#state:Type) (#rej:eqtype)
  (apply:op -> state -> outcome state rej) (lt:string -> string -> bool) (d:dag op)
  (heads:list string) (lanes:list (list string)) (s:state) (m:list (lane_rejection op rej))
  : Lemma (requires total_order lt /\ distinct heads /\
                    sorted_by lt lr_head_of m /\ distinct (keys_of lr_head_of m) /\
                    (forall (r:lane_rejection op rej).
                       mem r m <==> mem r (lane_rejections apply d heads lanes s)))
          (ensures m == lane_rejection_report apply lt d heads lanes s)
  = assert (order_on lt);
    lane_rejection_report_sorted apply lt d heads lanes s;
    lane_rejection_report_heads_distinct apply lt d heads lanes s;
    FStar.Classical.forall_intro (lane_rejection_report_mem apply lt d heads lanes s);
    sorted_by_unique lt lr_head_of m (lane_rejection_report apply lt d heads lanes s)

(* ======================================================================================
   TWINS (Phase 309) — the extractor premise, sampled at this model. What a twin is, why it
   is here and how the list is evaluated are `Twin`'s (Phase 504), which every model opens.
   ====================================================================================== *)

(* Every conflict in a report is `c`, up to the Left/Right swap — the other half of "one
   conflict, read canonically" beside `mem_u`. *)
let rec all_ueq (#op:eqtype) (c:conflict op) (cs:list (conflict op)) : Tot bool =
  match cs with
  | [] -> true
  | d :: t -> ueq c d && all_ueq c t

let twin_lt (x y:string) : Tot bool =
  (x = "a" && not (y = "a")) || (x = "b" && (y = "c" || y = "m")) || (x = "c" && y = "m")

let twin_dag : dag string =
  { nodes = [ { nid = "m"; nparents = [ "b"; "c" ]; nop = "m" };
              { nid = "c"; nparents = [ "a" ]; nop = "c" };
              { nid = "b"; nparents = [ "a" ]; nop = "b" };
              { nid = "a"; nparents = []; nop = "a" } ] }

let twin_fuel : list (node string) = app twin_dag.nodes twin_dag.nodes

let twin_bool_lt (p q:bool) : Tot bool = not p && q

let twin_apply (o:string) (s:string) : Tot (outcome string string) =
  if o = "x" then Error "refused" else Ok (s ^ o)

(* Phase 437's fixtures: a criss-cross (two merges of the same two lanes), a chain to truncate,
   a footprint under which two lanes collide, and a seal. *)
let twin_cc : dag string =
  { nodes = [ { nid = "y"; nparents = [ "b"; "c" ]; nop = "y" };
              { nid = "x"; nparents = [ "b"; "c" ]; nop = "x" };
              { nid = "c"; nparents = [ "a" ]; nop = "c" };
              { nid = "b"; nparents = [ "a" ]; nop = "b" };
              { nid = "a"; nparents = []; nop = "a" } ] }

let twin_cc_fuel : list (node string) = app twin_cc.nodes twin_cc.nodes

let twin_chain : dag string =
  { nodes = [ { nid = "r"; nparents = [ "q" ]; nop = "r" };
              { nid = "q"; nparents = [ "p" ]; nop = "q" };
              { nid = "p"; nparents = []; nop = "p" } ] }

let twin_chain_fuel : list (node string) = app twin_chain.nodes twin_chain.nodes

let twin_fp (_:string) : Tot footprint = whole_node_write_fp "n"

let twin_seal (n:string) (s:string) : Tot string = n ^ "|" ^ s

(* A checkpoint was taken, at node `n`, carrying state `st` and the seal of the two. *)
let twin_sealed_at (n st:string) (r:outcome (checkpoint string) (ck_fault string)) : Tot bool =
  match r with
  | Ok cp -> cp.cnode = n && cp.cstate = st && cp.cseal = twin_seal n st
  | Error _ -> false

let twin_excl : list (list string) =
  excl_lanes (drain_order twin_lt twin_fuel twin_dag.nodes) (ancestors_of twin_dag twin_fuel "a")
    [ ancestors_of twin_dag twin_fuel "b"; ancestors_of twin_dag twin_fuel "c" ]
    [ ancestors_of twin_dag twin_fuel "b"; ancestors_of twin_dag twin_fuel "c" ]

let twin_heads_report : list (conflict (node string)) =
  conflicts_of_heads twin_fp twin_dag (drain_order twin_lt twin_fuel twin_dag.nodes)
    (ancestors_of twin_dag twin_fuel "a")
    [ ancestors_of twin_dag twin_fuel "b"; ancestors_of twin_dag twin_fuel "c" ]

let twins : list twin = [
  { tname = "independent-different-slots";
    tholds = (fun () -> independent (slot_write_fp ("n", "tier")) (slot_write_fp ("n", "thinking")) = true) };
  { tname = "dependent-same-slot";
    tholds = (fun () -> independent (slot_write_fp ("n", "tier")) (slot_write_fp ("n", "tier")) = false) };
  { tname = "whole-node-write-refuses-a-slot";
    tholds = (fun () -> independent (whole_node_write_fp "n") (slot_write_fp ("n", "tier")) = false) };
  (* Read canonically, as every conflict report is (section 4): the model's `slot_clash` is a
     membership union, so the one clashing slot is listed once per clause it fails, where
     production's set carries it once. The report is one conflict up to `ueq`, and nothing else. *)
  { tname = "slot-clash-is-tagged-at-the-slot";
    tholds = (fun () ->
      let expected = { left = ("n", "tier"); right = ("n", "tier"); address = "n"; shape = SlotClash "tier" } in
      let cs = pair_conflicts slot_write_fp ("n", "tier") ("n", "tier") in
      mem_u expected cs && all_ueq expected cs) };
  { tname = "different-slots-fold-clean";
    tholds = (fun () -> pair_conflicts slot_write_fp ("n", "tier") ("n", "thinking") = []) };
  { tname = "slot-writes-commute-at-every-slot";
    tholds = (fun () ->
      lookup_slot (write_slot ("n", "tier") "t2" (write_slot ("n", "thinking") "high" [])) ("n", "thinking")
      = lookup_slot (write_slot ("n", "thinking") "high" (write_slot ("n", "tier") "t2" [])) ("n", "thinking")) };
  { tname = "drain-orders-a-diamond-by-the-tie-break";
    tholds = (fun () -> drain_order twin_lt twin_fuel twin_dag.nodes = [ "a"; "b"; "c"; "m" ]) };
  { tname = "drain-refuses-a-dangling-parent";
    tholds = (fun () ->
      drain RefuseDangling twin_lt twin_fuel [ { nid = "c"; nparents = [ "z" ]; nop = "c" } ] = Refused "c") };
  { tname = "replay-to-folds-the-drained-closure";
    tholds = (fun () -> replay_to twin_apply twin_lt twin_fuel twin_dag twin_fuel "" "m" = Ok "abcm") };
  { tname = "replay-to-refuses-an-unknown-head";
    tholds = (fun () -> replay_to twin_apply twin_lt twin_fuel twin_dag twin_fuel "" "zz" = Error (RUnknownHead "zz")) };
  { tname = "add-node-refuses-a-differing-node";
    tholds = (fun () ->
      add_node (fun (p q:node string) -> p.nop = q.nop) ({ nid = "a"; nparents = []; nop = "z" }) twin_dag
      = Collision "a") };
  { tname = "total-order-by-takes-the-key-before-the-id";
    tholds = (fun () ->
      drain_order (by_key twin_dag.nodes (fun (n:node string) -> n.nid = "b") twin_bool_lt twin_lt) twin_fuel twin_dag.nodes
      = [ "a"; "c"; "b"; "m" ]) };
  { tname = "conflicts-of-heads-is-the-lane-sweep-node-for-op";
    tholds = (fun () ->
      not (twin_heads_report = []) &&
      map_list (map_conflict node_op) twin_heads_report
      = all_conflicts twin_fp (map_list (fun ids -> ops_of (nodes_for twin_dag ids)) twin_excl)) };
  { tname = "reach-index-answers-the-walk";
    tholds = (fun () ->
      let r = reach_of_dag twin_lt twin_fuel twin_dag in
      reach_reaches twin_dag twin_fuel r "a" "m" = true &&
      reach_reaches twin_dag twin_fuel r "b" "c" = false &&
      reach_reaches twin_dag twin_fuel r "zz" "m" = false &&
      reach_ancestors twin_dag twin_fuel r "c" = [ "c"; "a" ]) };
  { tname = "checkpoint-at-seals-the-node-it-folded";
    tholds = (fun () ->
      twin_sealed_at "b" "ab" (checkpoint_at twin_apply twin_lt twin_fuel twin_dag twin_fuel twin_seal "" "b")) };
  { tname = "replay-from-a-checkpoint-is-replay-from-genesis";
    tholds = (fun () ->
      replay_from twin_apply twin_lt twin_fuel twin_dag twin_fuel (seal_at twin_seal "a" "a") "m"
      = Ok "abcm") };
  { tname = "replay-from-refuses-a-branch-from-below-the-checkpoint";
    tholds = (fun () ->
      replay_from twin_apply twin_lt twin_fuel twin_dag twin_fuel (seal_at twin_seal "b" "ab") "m"
      = Error (CUncovered "c")) };
  { tname = "lane-rejection-report-is-sorted-by-head";
    tholds = (fun () ->
      (* heads named c then b; both lanes refuse at `x`; the report comes back b then c *)
      lane_rejection_report twin_apply twin_lt twin_cc [ "c"; "b" ] [ [ "c"; "x" ]; [ "x" ] ] "s"
      = [ { lr_head = "b"; lr_delta = [ "x" ]; lr_node = "x"; lr_reject = "refused" };
          { lr_head = "c"; lr_delta = [ "c"; "x" ]; lr_node = "x"; lr_reject = "refused" } ]) };
  { tname = "truncation-keeps-the-replay";
    tholds = (fun () ->
      let cp = seal_at twin_seal "q" "pq" in
      truncation_ok twin_chain twin_chain_fuel "q" [ "q"; "r" ] &&
      replay_from twin_apply twin_lt twin_chain_fuel (truncate twin_chain [ "q"; "r" ]) twin_chain_fuel cp "r"
      = Ok "pqr" &&
      replay_from twin_apply twin_lt twin_chain_fuel twin_chain twin_chain_fuel cp "r" = Ok "pqr") } ]

let _ = assert_norm (twins_hold twins == true)
