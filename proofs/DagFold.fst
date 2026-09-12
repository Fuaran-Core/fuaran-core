(*
   DagFold — an F* model of Fuaran.Core's N-lane DAG fold, with fold confluence as a
   machine-checked theorem (fuaran-core Phase 131, the prover spike).

   WHAT IS MODELLED. The fold that `Fuaran.Core.OpStream.Dag.reconcileMany` performs and that
   `Fuaran.Core.Conformance.FoldConfluence.foldOnce` measures: N lane deltas (one op list per
   writer, off one shared base), checked pairwise for footprint interference exactly as
   `Dag.conflicts` checks them, then either HALTED with the conflict report and nothing applied,
   or composed in arrival order and REPLAYED through the domain reducer from the base state.

   WHAT IS PROVED. `fold_confluence`: for any two arrival orders of the same lane set — any two
   lists related by the inductive permutation relation `perm` — the outcomes are equivalent:
   the same folded state, or the same rejection, or the same canonical halt report (as an
   unordered set of shape × address × unordered op pair, which is the canonical form
   `FoldConfluence.canonicalConflictReport` renders). The one hypothesis is the domain's
   promise (`independence_sound`): ops whose footprints `independent` declares disjoint commute
   under `apply`. That is the promise Phase 78/80 certify for the tree algebra and that the
   Phase 100 pack certifies for a domain's own witness; nothing here proves it, everything
   here rests on it, and the README states that boundary in the claims-ladder form.

   WHAT IS NOT MODELLED. The DAG itself — content-addressed node ids, `betweenOps`, the
   topological order that recovers each lane's delta from a real `Dag.T`. The model starts
   where the lane deltas are known. The extracted oracle is therefore fed the lanes directly
   while the production side rebuilds them through a real DAG; that the two then agree is what
   the differential host certifies, and it is a claim about production `betweenOps` +
   `reconcileMany` together, not about the theorem.

   HOW TO READ IT. Every definition names its F# counterpart in the comment above it:
     - `Ops.fs`          — `Footprint`, `Ops.independent`
     - `DagOpStream.fs`  — `MergeConflictShape`, `MergeConflict`, `Dag.conflicts`,
                            `Dag.reconcileMany`
     - `FoldConfluence.fs` — `LaneFoldOutcome`, `foldOnce`, `canonicalConflictReport`
   List-as-set helpers replace F#'s `Set<string>`: membership is the meaning, and order and
   multiplicity are not (the differential host compares canonical reports, never raw lists).
   The helpers are defined here rather than taken from `FStar.List.Tot` so that the extracted
   oracle depends on `Prims` alone.

   Apache-2.0, like everything beside it.
*)
module DagFold

(* ======================================================================================
   0. Lists read as sets (F#: `Set<string>` and the `Set.*` calls in `Ops.independent` and
      `Dag.conflicts`).
   ====================================================================================== *)

let rec mem (#a:eqtype) (x:a) (l:list a) : Tot bool =
  match l with
  | [] -> false
  | y :: t -> x = y || mem x t

let rec app (#a:Type) (l m:list a) : Tot (list a) =
  match l with
  | [] -> m
  | x :: t -> x :: app t m

let rec concat (#a:Type) (ls:list (list a)) : Tot (list a) =
  match ls with
  | [] -> []
  | l :: t -> app l (concat t)

(* F#: `Set.intersect` *)
let rec inter (#a:eqtype) (x y:list a) : Tot (list a) =
  match x with
  | [] -> []
  | h :: t -> if mem h y then h :: inter t y else inter t y

(* F#: `Set.difference` *)
let rec diff (#a:eqtype) (x y:list a) : Tot (list a) =
  match x with
  | [] -> []
  | h :: t -> if mem h y then diff t y else h :: diff t y

(* F#: `Set.union` — membership union; duplicates are harmless under the set reading. *)
let union (#a:eqtype) (x y:list a) : Tot (list a) = app x y

(* F#: `Set.isEmpty` *)
let is_empty (#a:Type) (l:list a) : Tot bool =
  match l with
  | [] -> true
  | _ -> false

let disjoint (#a:eqtype) (x y:list a) : Tot bool = is_empty (inter x y)

(* ---- the membership algebra the proofs run on ---- *)

let rec mem_app (#a:eqtype) (x:a) (l m:list a)
  : Lemma (ensures mem x (app l m) == (mem x l || mem x m)) [SMTPat (mem x (app l m))]
  = match l with
    | [] -> ()
    | _ :: t -> mem_app x t m

let rec mem_inter (#a:eqtype) (x:a) (l m:list a)
  : Lemma (ensures mem x (inter l m) == (mem x l && mem x m)) [SMTPat (mem x (inter l m))]
  = match l with
    | [] -> ()
    | _ :: t -> mem_inter x t m

let rec mem_diff (#a:eqtype) (x:a) (l m:list a)
  : Lemma (ensures mem x (diff l m) == (mem x l && not (mem x m))) [SMTPat (mem x (diff l m))]
  = match l with
    | [] -> ()
    | _ :: t -> mem_diff x t m

let rec app_nil_r (#a:Type) (l:list a)
  : Lemma (ensures app l [] == l) [SMTPat (app l [])]
  = match l with
    | [] -> ()
    | _ :: t -> app_nil_r t

let rec app_assoc (#a:Type) (l m n:list a)
  : Lemma (ensures app (app l m) n == app l (app m n)) [SMTPat (app (app l m) n)]
  = match l with
    | [] -> ()
    | _ :: t -> app_assoc t m n

let app_nil_iff (#a:Type) (l m:list a)
  : Lemma (ensures is_empty (app l m) == (is_empty l && is_empty m)) [SMTPat (is_empty (app l m))]
  = match l with
    | [] -> ()
    | _ :: _ -> ()

(* An empty list has no member; a non-empty one has its head. *)
let empty_no_mem (#a:eqtype) (l:list a) (x:a)
  : Lemma (requires is_empty l) (ensures not (mem x l)) [SMTPat (is_empty l); SMTPat (mem x l)]
  = ()

let rec inter_nil_iff (#a:eqtype) (l m:list a)
  : Lemma (ensures is_empty (inter l m) <==> (forall (x:a). not (mem x l && mem x m)))
  = match l with
    | [] -> ()
    | h :: t -> inter_nil_iff t m

(* ======================================================================================
   1. Footprints and independence (F#: `Footprint`, `Ops.independent` in Ops.fs).
   ====================================================================================== *)

type footprint = {
  reads                 : list string;
  structure_writes      : list string;
  content_writes        : list string;
  unknown_parent_writes : list string
}

(* F#: `hasStructural` inside `Ops.independent`, and `writesStructure` beside `Dag.conflicts`
   — the two are the same predicate, and the model has one name for it. *)
let writes_structure (f:footprint) : Tot bool =
  not (is_empty f.structure_writes) || not (is_empty f.unknown_parent_writes)

(* F#: `Ops.independent`, clause for clause. *)
let independent (a b:footprint) : Tot bool =
  disjoint a.content_writes b.content_writes &&
  disjoint a.content_writes b.reads &&
  disjoint b.content_writes a.reads &&
  disjoint a.structure_writes b.structure_writes &&
  not (not (is_empty a.unknown_parent_writes) && writes_structure b) &&
  not (not (is_empty b.unknown_parent_writes) && writes_structure a)

(* ======================================================================================
   2. Merge conflicts (F#: `MergeConflictShape`, `MergeConflict<'Op>`, `Dag.conflicts`).
   ====================================================================================== *)

type shape =
  | ConcurrentUpdate
  | InsertPositionClash
  | MoveVsRemove

type conflict (op:eqtype) = {
  left    : op;
  right   : op;
  address : string;
  shape   : shape
}

(* F#: `concurrent` — a content-write overlapping the other's content-write or read. *)
let concurrent (fa fb:footprint) : Tot (list string) =
  union (inter fa.content_writes fb.content_writes)
        (union (inter fa.content_writes fb.reads)
               (inter fb.content_writes fa.reads))

(* F#: `insertClash` — a shared NAMED structural parent. *)
let insert_clash (fa fb:footprint) : Tot (list string) =
  inter fa.structure_writes fb.structure_writes

(* F#: `moveRemove` — a remove/move racing the other side's structural write, keyed by the
   removed/moved id. The pinned Phase 78 over-approximation. *)
let move_remove (fa fb:footprint) : Tot (list string) =
  union (if not (is_empty fa.unknown_parent_writes) && writes_structure fb
         then fa.unknown_parent_writes else [])
        (if not (is_empty fb.unknown_parent_writes) && writes_structure fa
         then fb.unknown_parent_writes else [])

(* F#: the three `for addr in cK do yield { Left = a; Right = b; Address = addr; Shape = … }`. *)
let rec tag (#op:eqtype) (a b:op) (s:shape) (addrs:list string) : Tot (list (conflict op)) =
  match addrs with
  | [] -> []
  | x :: t -> { left = a; right = b; address = x; shape = s } :: tag a b s t

(* F#: the body of `Dag.conflicts` for ONE op pair — one shape per shared address by priority
   (content > position > move/remove). *)
let pair_conflicts (#op:eqtype) (fp:op -> footprint) (a b:op) : Tot (list (conflict op)) =
  let fa = fp a in
  let fb = fp b in
  let c1 = concurrent fa fb in
  let c2 = diff (insert_clash fa fb) c1 in
  let c3 = diff (diff (move_remove fa fb) c1) c2 in
  app (tag a b ConcurrentUpdate c1)
      (app (tag a b InsertPositionClash c2)
           (tag a b MoveVsRemove c3))

(* F#: the inner `for b in deltaB` of `Dag.conflicts`. *)
let rec conflicts_with (#op:eqtype) (fp:op -> footprint) (a:op) (db:list op)
  : Tot (list (conflict op)) =
  match db with
  | [] -> []
  | b :: t -> app (pair_conflicts fp a b) (conflicts_with fp a t)

(* F#: `Dag.conflicts` — deltaA order × deltaB order. *)
let rec conflicts (#op:eqtype) (fp:op -> footprint) (da db:list op) : Tot (list (conflict op)) =
  match da with
  | [] -> []
  | a :: t -> app (conflicts_with fp a db) (conflicts fp t db)

(* ======================================================================================
   3. The N-lane fold (F#: `Dag.reconcileMany`), the replay and the outcome
      (F#: `FoldConfluence.foldOnce`, `LaneFoldOutcome`).
   ====================================================================================== *)

(* F#: `Result<'a, 'e>` as `StreamWitness.Apply` returns it. *)
type outcome (a e:Type) =
  | Ok    : a -> outcome a e
  | Error : e -> outcome a e

(* F#: `Result.bind`. *)
let bind (#state #rej:Type) (r:outcome state rej) (f:state -> outcome state rej)
  : Tot (outcome state rej) =
  match r with
  | Ok s -> f s
  | Error e -> Error e

(* F#: the `for (j, b) in deltas do if i < j` inner sweep of `reconcileMany` for the lane at i. *)
let rec lane_conflicts (#op:eqtype) (fp:op -> footprint) (d:list op) (rest:list (list op))
  : Tot (list (conflict op)) =
  match rest with
  | [] -> []
  | e :: t -> app (conflicts fp d e) (lane_conflicts fp d t)

(* F#: the `cs` list of `reconcileMany` — every UNORDERED lane pair, i < j, in that order. *)
let rec all_conflicts (#op:eqtype) (fp:op -> footprint) (ds:list (list op))
  : Tot (list (conflict op)) =
  match ds with
  | [] -> []
  | d :: rest -> app (lane_conflicts fp d rest) (all_conflicts fp rest)

(* F#: `Dag.reconcileMany` — `Ok` the concatenation of the lane deltas in the order given, or
   `Error` the concatenated reports with nothing applied. The model takes the deltas directly
   where production recovers them from the DAG with `betweenOps`. *)
let reconcile_many (#op:eqtype) (fp:op -> footprint) (ds:list (list op))
  : Tot (outcome (list op) (list (conflict op))) =
  match all_conflicts fp ds with
  | [] -> Ok (concat ds)
  | cs -> Error cs

(* F#: `script |> List.fold (fun acc op -> acc |> Result.bind (w.Apply op)) (Ok state0)`. *)
let rec replay (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (script:list op) (s:state)
  : Tot (outcome state rej) (decreases script) =
  match script with
  | [] -> Ok s
  | o :: t -> bind (apply o s) (replay apply t)

(* F#: `LaneFoldOutcome`. `LaneFolded` carries the state where F# carries the domain's hash of
   it; `LaneHalted` carries the report where F# carries its canonical rendering. Both are
   renderings of what is carried here. *)
type lane_outcome (op:eqtype) (state rej:Type) =
  | LaneFolded   : state -> lane_outcome op state rej
  | LaneHalted   : list (conflict op) -> lane_outcome op state rej
  | LaneRejected : rej -> lane_outcome op state rej

(* F#: `FoldConfluence.foldOnce`, from the point where the lane deltas are known. *)
let fold_once (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint)
  (s0:state) (lanes:list (list op))
  : Tot (lane_outcome op state rej) =
  match reconcile_many fp lanes with
  | Error cs -> LaneHalted cs
  | Ok script ->
    match replay apply script s0 with
    | Ok s -> LaneFolded s
    | Error r -> LaneRejected r

(* ======================================================================================
   4. The canonical reading of a halt report (F#: `canonicalConflictReport` — one entry per
      distinct shape × address × UNORDERED op pair). Membership up to the Left/Right swap.
   ====================================================================================== *)

let ueq (#op:eqtype) (c d:conflict op) : Tot bool =
  c.address = d.address && c.shape = d.shape &&
  ((c.left = d.left && c.right = d.right) || (c.left = d.right && c.right = d.left))

let rec mem_u (#op:eqtype) (c:conflict op) (cs:list (conflict op)) : Tot bool =
  match cs with
  | [] -> false
  | d :: t -> ueq c d || mem_u c t

let same_report (#op:eqtype) (cs1 cs2:list (conflict op)) : prop =
  forall (c:conflict op). mem_u c cs1 <==> mem_u c cs2

(* Two outcomes agree: the same state, the same rejection, or the same canonical report. *)
let outcome_equiv (#op:eqtype) (#state #rej:Type) (o1 o2:lane_outcome op state rej) : prop =
  match o1, o2 with
  | LaneFolded s1, LaneFolded s2 -> s1 == s2
  | LaneHalted c1, LaneHalted c2 -> same_report c1 c2
  | LaneRejected r1, LaneRejected r2 -> r1 == r2
  | _, _ -> False

(* ======================================================================================
   5. Arrival orders: the inductive permutation relation (Coq's `Permutation`). The theorem
      quantifies over it; `FoldConfluence.arrivalOrders` samples it.
   ====================================================================================== *)

[@@ noextract_to "FSharp"]  (* proof-only: the oracle never needs a permutation witness *)
noeq type perm (a:Type) : list a -> list a -> Type =
  | PNil   : perm a [] []
  | PSkip  : x:a -> l1:list a -> l2:list a -> perm a l1 l2 -> perm a (x :: l1) (x :: l2)
  | PSwap  : x:a -> y:a -> l:list a -> perm a (x :: y :: l) (y :: x :: l)
  | PTrans : l1:list a -> l2:list a -> l3:list a -> perm a l1 l2 -> perm a l2 l3 -> perm a l1 l3

let rec perm_mem (#a:eqtype) (l1 l2:list a) (p:perm a l1 l2) (x:a)
  : Lemma (ensures mem x l1 == mem x l2) (decreases p)
  = match p with
    | PNil -> ()
    | PSkip _ m1 m2 p' -> perm_mem m1 m2 p' x
    | PSwap _ _ _ -> ()
    | PTrans m1 m2 m3 p12 p23 -> perm_mem m1 m2 p12 x; perm_mem m2 m3 p23 x

(* ======================================================================================
   6. The domain's promise (F#: what Phase 80's `concurrencyLaws` certifies for the tree
      algebra and Phase 100's `laneFoldLaws` certifies for a domain's own witness).
   ====================================================================================== *)

let commutes (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (a b:op) : prop =
  forall (s:state). bind (apply a s) (apply b) == bind (apply b s) (apply a)

(* Footprint independence is SOUND for this domain: what `independent` declares disjoint
   commutes under `apply`, rejections included. *)
let independence_sound (#op:eqtype) (#state #rej:Type)
  (fp:op -> footprint) (apply:op -> state -> outcome state rej) : prop =
  forall (a b:op). independent (fp a) (fp b) ==> commutes apply a b

(* ======================================================================================
   7. Conflicts and independence agree (F#: the Phase 83 cross-validation
      `independent ⇒ conflicts = []`, and the Phase 64 promise that it fires iff not
      independent — both directions, mechanised).
   ====================================================================================== *)

let rec tag_nil_iff (#op:eqtype) (a b:op) (s:shape) (addrs:list string)
  : Lemma (ensures is_empty (tag a b s addrs) == is_empty addrs) [SMTPat (is_empty (tag a b s addrs))]
  = match addrs with
    | [] -> ()
    | _ :: t -> tag_nil_iff a b s t

let diff_nil_r (#a:eqtype) (l:list a)
  : Lemma (ensures diff l [] == l) [SMTPat (diff l [])]
  = let rec go (l:list a) : Lemma (ensures diff l [] == l) = match l with [] -> () | _ :: t -> go t in
    go l

let pair_conflicts_nil_iff (#op:eqtype) (fp:op -> footprint) (a b:op)
  : Lemma (ensures is_empty (pair_conflicts fp a b) == independent (fp a) (fp b))
  = let fa = fp a in
    let fb = fp b in
    inter_nil_iff fa.content_writes fb.content_writes;
    inter_nil_iff fa.content_writes fb.reads;
    inter_nil_iff fb.content_writes fa.reads;
    inter_nil_iff fa.structure_writes fb.structure_writes;
    (* `is_empty (concurrent fa fb)` is the conjunction of the three disjointness clauses. *)
    assert (is_empty (concurrent fa fb) ==
            (disjoint fa.content_writes fb.content_writes &&
             disjoint fa.content_writes fb.reads &&
             disjoint fb.content_writes fa.reads));
    if is_empty (concurrent fa fb) then begin
      (* c1 = [] so c2 = insert_clash, c3 = move_remove — the differences drop away. *)
      assert (concurrent fa fb == []);
      assert (diff (insert_clash fa fb) [] == insert_clash fa fb);
      if is_empty (insert_clash fa fb) then
        assert (diff (diff (move_remove fa fb) []) [] == move_remove fa fb)
      else ()
    end else ()

(* Membership in `conflicts`, both directions, as the pair it came from. *)
let rec mem_conflicts_with (#op:eqtype) (fp:op -> footprint) (a:op) (db:list op) (c:conflict op)
  : Lemma (ensures mem c (conflicts_with fp a db) <==> (exists (b:op). mem b db /\ mem c (pair_conflicts fp a b)))
  = match db with
    | [] -> ()
    | b :: t ->
      mem_conflicts_with fp a t c;
      if mem c (pair_conflicts fp a b) then assert (mem b db) else ()

let rec mem_conflicts (#op:eqtype) (fp:op -> footprint) (da db:list op) (c:conflict op)
  : Lemma (ensures mem c (conflicts fp da db) <==>
                   (exists (a b:op). mem a da /\ mem b db /\ mem c (pair_conflicts fp a b)))
  = match da with
    | [] -> ()
    | a :: t ->
      mem_conflicts_with fp a db c;
      mem_conflicts fp t db c;
      if mem c (conflicts_with fp a db) then assert (mem a da) else ()

(* `conflicts = []` is exactly pairwise independence. *)
let conflicts_nil_independent (#op:eqtype) (fp:op -> footprint) (da db:list op) (a b:op)
  : Lemma (requires is_empty (conflicts fp da db) /\ mem a da /\ mem b db)
          (ensures independent (fp a) (fp b))
  = pair_conflicts_nil_iff fp a b;
    match pair_conflicts fp a b with
    | [] -> ()
    | c :: _ ->
      mem_conflicts fp da db c;
      assert (mem c (pair_conflicts fp a b));
      assert (mem c (conflicts fp da db))

(* … and as the universally quantified statement `replay_swap_lanes` consumes. *)
let conflicts_nil_pairwise (#op:eqtype) (fp:op -> footprint) (x y:list op)
  : Lemma (requires is_empty (conflicts fp x y))
          (ensures forall (a b:op). mem a x ==> mem b y ==> independent (fp a) (fp b))
  = let aux (a b:op) : Lemma (mem a x ==> mem b y ==> independent (fp a) (fp b)) =
      if mem a x && mem b y then conflicts_nil_independent fp x y a b else ()
    in
    FStar.Classical.forall_intro_2 aux

(* ======================================================================================
   8. The halt report is arrival-order-invariant, read canonically.
   ====================================================================================== *)

let rec mem_u_app (#op:eqtype) (c:conflict op) (l m:list (conflict op))
  : Lemma (ensures mem_u c (app l m) == (mem_u c l || mem_u c m)) [SMTPat (mem_u c (app l m))]
  = match l with
    | [] -> ()
    | _ :: t -> mem_u_app c t m

(* Canonical membership in a tagged list is symmetric in the pair by construction. *)
let rec mem_u_tag (#op:eqtype) (c:conflict op) (a b:op) (s:shape) (addrs:list string)
  : Lemma (ensures mem_u c (tag a b s addrs) ==
                   (c.shape = s && mem c.address addrs &&
                    ((c.left = a && c.right = b) || (c.left = b && c.right = a))))
          [SMTPat (mem_u c (tag a b s addrs))]
  = match addrs with
    | [] -> ()
    | _ :: t -> mem_u_tag c a b s t

(* Each shape class is symmetric in (fa, fb) as a set of addresses. *)
let concurrent_sym (fa fb:footprint) (x:string)
  : Lemma (ensures mem x (concurrent fa fb) == mem x (concurrent fb fa))
          [SMTPat (mem x (concurrent fa fb))]
  = ()

let insert_clash_sym (fa fb:footprint) (x:string)
  : Lemma (ensures mem x (insert_clash fa fb) == mem x (insert_clash fb fa))
          [SMTPat (mem x (insert_clash fa fb))]
  = ()

let move_remove_sym (fa fb:footprint) (x:string)
  : Lemma (ensures mem x (move_remove fa fb) == mem x (move_remove fb fa))
          [SMTPat (mem x (move_remove fa fb))]
  = ()

let pair_conflicts_sym (#op:eqtype) (fp:op -> footprint) (a b:op) (c:conflict op)
  : Lemma (ensures mem_u c (pair_conflicts fp a b) == mem_u c (pair_conflicts fp b a))
          [SMTPat (mem_u c (pair_conflicts fp a b))]
  = ()

let rec mem_u_conflicts_with (#op:eqtype) (fp:op -> footprint) (a:op) (db:list op) (c:conflict op)
  : Lemma (ensures mem_u c (conflicts_with fp a db) <==> (exists (b:op). mem b db /\ mem_u c (pair_conflicts fp a b)))
  = match db with
    | [] -> ()
    | b :: t ->
      mem_u_conflicts_with fp a t c;
      if mem_u c (pair_conflicts fp a b) then assert (mem b db) else ()

let rec mem_u_conflicts (#op:eqtype) (fp:op -> footprint) (da db:list op) (c:conflict op)
  : Lemma (ensures mem_u c (conflicts fp da db) <==>
                   (exists (a b:op). mem a da /\ mem b db /\ mem_u c (pair_conflicts fp a b)))
  = match da with
    | [] -> ()
    | a :: t ->
      mem_u_conflicts_with fp a db c;
      mem_u_conflicts fp t db c;
      if mem_u c (conflicts_with fp a db) then assert (mem a da) else ()

(* `conflicts x y` and `conflicts y x` are the same report, read canonically — the fact
   `canonicalConflictReport` exists to make usable. *)
let conflicts_sym (#op:eqtype) (fp:op -> footprint) (x y:list op) (c:conflict op)
  : Lemma (ensures mem_u c (conflicts fp x y) <==> mem_u c (conflicts fp y x))
  = mem_u_conflicts fp x y c;
    mem_u_conflicts fp y x c

let rec mem_u_lane_conflicts (#op:eqtype) (fp:op -> footprint) (d:list op) (rest:list (list op)) (c:conflict op)
  : Lemma (ensures mem_u c (lane_conflicts fp d rest) <==> (exists (e:list op). mem e rest /\ mem_u c (conflicts fp d e)))
  = match rest with
    | [] -> ()
    | e :: t ->
      mem_u_lane_conflicts fp d t c;
      if mem_u c (conflicts fp d e) then assert (mem e rest) else ()

(* A lane's sweep over a permuted rest is the same report. *)
let lane_conflicts_perm (#op:eqtype) (fp:op -> footprint) (d:list op)
  (r1 r2:list (list op)) (p:perm (list op) r1 r2) (c:conflict op)
  : Lemma (ensures mem_u c (lane_conflicts fp d r1) <==> mem_u c (lane_conflicts fp d r2))
  = mem_u_lane_conflicts fp d r1 c;
    mem_u_lane_conflicts fp d r2 c;
    FStar.Classical.forall_intro (perm_mem r1 r2 p)

(* THEOREM (halt half): the full pairwise report of a lane set is the same canonical report
   under every arrival order. *)
let rec all_conflicts_perm (#op:eqtype) (fp:op -> footprint)
  (ls1 ls2:list (list op)) (p:perm (list op) ls1 ls2) (c:conflict op)
  : Lemma (ensures mem_u c (all_conflicts fp ls1) <==> mem_u c (all_conflicts fp ls2)) (decreases p)
  = match p with
    | PNil -> ()
    | PSkip x m1 m2 p' ->
      lane_conflicts_perm fp x m1 m2 p' c;
      all_conflicts_perm fp m1 m2 p' c
    | PSwap x y l ->
      conflicts_sym fp x y c
    | PTrans m1 m2 m3 p12 p23 ->
      all_conflicts_perm fp m1 m2 p12 c;
      all_conflicts_perm fp m2 m3 p23 c

(* Halting is a classification, and the classification does not move. *)
let ueq_refl (#op:eqtype) (c:conflict op) : Lemma (ensures ueq c c) [SMTPat (ueq c c)] = ()

let all_conflicts_perm_empty (#op:eqtype) (fp:op -> footprint)
  (ls1 ls2:list (list op)) (p:perm (list op) ls1 ls2)
  : Lemma (ensures is_empty (all_conflicts fp ls1) == is_empty (all_conflicts fp ls2))
  = FStar.Classical.forall_intro (all_conflicts_perm fp ls1 ls2 p);
    (match all_conflicts fp ls1 with
     | [] -> ()
     | h :: _ -> assert (mem_u h (all_conflicts fp ls1)));
    (match all_conflicts fp ls2 with
     | [] -> ()
     | h :: _ -> assert (mem_u h (all_conflicts fp ls2)))

(* ======================================================================================
   9. The replay of a conflict-free lane set is arrival-order-invariant.
   ====================================================================================== *)

let rec replay_app (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (p q:list op) (s:state)
  : Lemma (ensures replay apply (app p q) s == bind (replay apply p s) (replay apply q))
          (decreases p)
  = match p with
    | [] -> ()
    | o :: t ->
      match apply o s with
      | Ok s' -> replay_app apply t q s'
      | Error _ -> ()

(* One op independent of a whole script slides through it: `y; a` is `a; y`. *)
let rec replay_push_through (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint)
  (a:op) (y:list op) (s:state)
  : Lemma (requires independence_sound fp apply /\
                    (forall (b:op). mem b y ==> independent (fp a) (fp b)))
          (ensures bind (replay apply y s) (apply a) == bind (apply a s) (replay apply y))
          (decreases y)
  = match y with
    | [] ->
      (match apply a s with
       | Ok _ -> ()
       | Error _ -> ())
    | b :: y' ->
      assert (mem b y);
      assert (independent (fp a) (fp b));
      assert (commutes apply a b);
      assert (bind (apply a s) (apply b) == bind (apply b s) (apply a));
      (match apply b s with
       | Error _ ->
         (match apply a s with
          | Ok _ -> ()
          | Error _ -> ())
       | Ok s1 ->
         replay_push_through apply fp a y' s1;
         (match apply a s with
          | Ok s2 ->
            (match apply a s1 with
             | Ok _ -> ()
             | Error _ -> ())
          | Error _ -> ()))

(* Two pairwise-independent lanes replay the same in either order. *)
let rec replay_swap_lanes (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint)
  (x y:list op) (s:state)
  : Lemma (requires independence_sound fp apply /\
                    (forall (a b:op). mem a x ==> mem b y ==> independent (fp a) (fp b)))
          (ensures replay apply (app x y) s == replay apply (app y x) s)
          (decreases x)
  = match x with
    | [] -> ()
    | a :: x' ->
      assert (mem a x);
      assert (forall (b:op). mem b y ==> independent (fp a) (fp b));
      replay_push_through apply fp a y s;
      replay_app apply y (a :: x') s;
      (match apply a s with
       | Ok s0 ->
         replay_swap_lanes apply fp x' y s0;
         replay_app apply y x' s0;
         (match replay apply y s with
          | Ok _ -> ()
          | Error _ -> ())
       | Error _ ->
         (match replay apply y s with
          | Ok _ -> ()
          | Error _ -> ()))

(* THEOREM (fold half): a conflict-free lane set replays to the same outcome — state or
   rejection — under every arrival order. *)
let rec replay_perm (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint)
  (ls1 ls2:list (list op)) (p:perm (list op) ls1 ls2) (s:state)
  : Lemma (requires independence_sound fp apply /\ is_empty (all_conflicts fp ls1))
          (ensures replay apply (concat ls1) s == replay apply (concat ls2) s)
          (decreases p)
  = match p with
    | PNil -> ()
    | PSkip x m1 m2 p' ->
      replay_app apply x (concat m1) s;
      replay_app apply x (concat m2) s;
      (match replay apply x s with
       | Ok s1 -> replay_perm apply fp m1 m2 p' s1
       | Error _ -> ())
    | PSwap x y l ->
      (* all_conflicts (x :: y :: l) starts with conflicts x y, so x and y are independent. *)
      assert (is_empty (conflicts fp x y));
      conflicts_nil_pairwise fp x y;
      replay_app apply (app x y) (concat l) s;
      replay_app apply (app y x) (concat l) s;
      replay_swap_lanes apply fp x y s
    | PTrans m1 m2 m3 p12 p23 ->
      all_conflicts_perm_empty fp m1 m2 p12;
      replay_perm apply fp m1 m2 p12 s;
      replay_perm apply fp m2 m3 p23 s

(* ======================================================================================
   10. THE THEOREM — fold confluence (the Phase 100 law, mechanised).
       The same lanes fold to the same state however they arrive; a lane set that cannot
       fold halts with the same canonical report however it arrives; and no lane set folds
       under one order and halts under another.
   ====================================================================================== *)

val fold_confluence (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint) (s0:state)
  (ls1 ls2:list (list op)) (p:perm (list op) ls1 ls2)
  : Lemma (requires independence_sound fp apply)
          (ensures outcome_equiv (fold_once apply fp s0 ls1) (fold_once apply fp s0 ls2))

let fold_confluence #op #state #rej apply fp s0 ls1 ls2 p =
  all_conflicts_perm_empty fp ls1 ls2 p;
  match all_conflicts fp ls1 with
  | [] ->
    replay_perm apply fp ls1 ls2 p s0
  | _ ->
    FStar.Classical.forall_intro (all_conflicts_perm fp ls1 ls2 p)
