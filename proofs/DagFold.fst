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
   the same folded state, or the same canonical halt report (as an unordered set of shape ×
   address × unordered op pair, which is the canonical form
   `FoldConfluence.canonicalConflictReport` renders). The one DOMAIN hypothesis is the
   diamond (`independence_diamond`): ops whose footprints `independent` declares disjoint and
   which BOTH APPLY at a state each apply after the other and reach the same state. That is
   exactly what Phase 78/80 certify for the tree algebra and what the Phase 100 pack certifies
   for a domain's own witness — nothing here proves it, everything in the fold half rests on
   it, and `ProofOracleTests.fs` measures it on the reference witness. The fold half also asks
   that the lane set in hand be one whose lanes all apply from the base state (`lanes_apply`),
   which is the lane set `foldOnce`'s generators produce. The HALT half (`fold_confluence_halt`)
   asks for neither: it is a property of the footprints alone. The README states the whole
   boundary in the claims-ladder form.

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

      Phase 131 stated this as `independence_sound`: independent ops commute under `apply`
      at EVERY state, REJECTIONS INCLUDED. That premise is false of the reference witness,
      so the theorem was sound about a domain nobody has. `Ops.Rejection.UnknownNode`
      carries `addressable` — the whole id set of the tree it was raised against
      (`Tree.ids`) — and `ReorderMismatch` carries the parent's current children, so two
      independent ops one of which rejects reject with DIFFERENT envelopes depending on
      whether the other ran first. Both orders reject; they do not reject identically.

      What Phase 80 certifies, and what this hypothesis states, is the DIAMOND: independent
      ops that BOTH APPLY at a state each apply after the other and the two orders reach the
      same state. Nothing is claimed where either op rejects — the README's claims ladder
      says so under "not claimed", and `ProofOracleTests.fs` holds a witness that the
      stronger clause really is unavailable here.
   ====================================================================================== *)

(* The diamond for one pair of ops, at one state. `bind (apply a s) (apply b)` IS "b after
   a" when a applied, so `Ok?` of it is "b still applies", and the equality is the two
   orders agreeing. F#: Phase 80's `a @ b` / `b @ a` interleavings, at script length one. *)
let diamond (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (a b:op) : prop =
  forall (s:state).
    Ok? (apply a s) ==> Ok? (apply b s) ==>
    (Ok? (bind (apply a s) (apply b)) /\
     bind (apply a s) (apply b) == bind (apply b s) (apply a))

(* Footprint independence keeps the DIAMOND for this domain: what `independent` declares
   disjoint commutes under `apply` wherever both ops apply. This is the theorem's one
   domain hypothesis. *)
let independence_diamond (#op:eqtype) (#state #rej:Type)
  (fp:op -> footprint) (apply:op -> state -> outcome state rej) : prop =
  forall (a b:op). independent (fp a) (fp b) ==> diamond apply a b

(* The lane set the fold half is about: every lane applies cleanly from the base state —
   which is exactly what `foldOnce`'s generators produce, and what Phase 80 builds its
   pairs from (`collectScript` keeps only accepted ops). A lane set with a REJECTING lane
   is outside the fold claim, as it is outside Phase 80's. *)
let lanes_apply (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (ls:list (list op)) (s:state) : prop =
  forall (l:list op). mem l ls ==> Ok? (replay apply l s)

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
   9. The replay of a conflict-free lane set whose lanes all apply from the base state is
      arrival-order-invariant.
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

(* One op independent of a whole script slides through it WHERE BOTH APPLY: `y; a` is
   `a; y`, and both are `Ok`. The diamond, extended along `y` by induction. *)
let rec replay_push_through (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint)
  (a:op) (y:list op) (s:state)
  : Lemma (requires independence_diamond fp apply /\
                    (forall (b:op). mem b y ==> independent (fp a) (fp b)) /\
                    Ok? (apply a s) /\ Ok? (replay apply y s))
          (ensures Ok? (bind (apply a s) (replay apply y)) /\
                   bind (replay apply y s) (apply a) == bind (apply a s) (replay apply y))
          (decreases y)
  = match y with
    | [] -> ()
    | b :: y' ->
      assert (mem b y);
      assert (independent (fp a) (fp b));
      assert (diamond apply a b);
      (* `replay (b :: y') s` is `Ok`, so `b` applies at `s` and the tail applies after it. *)
      assert (Ok? (apply b s));
      assert (Ok? (bind (apply a s) (apply b)) /\
              bind (apply a s) (apply b) == bind (apply b s) (apply a));
      (match apply a s, apply b s with
       | Ok sa, Ok s1 ->
         assert (apply b sa == apply a s1);
         (match apply b sa with
          | Ok t1 ->
            assert (apply a s1 == Ok t1);
            assert (Ok? (replay apply y' s1));
            replay_push_through apply fp a y' s1;
            assert (bind (replay apply y' s1) (apply a) == bind (apply a s1) (replay apply y'));
            assert (bind (apply a s1) (replay apply y') == replay apply y' t1);
            assert (bind (apply a s) (replay apply y) == replay apply y' t1);
            assert (bind (replay apply y s) (apply a) == bind (replay apply y' s1) (apply a))
          | Error _ -> ())
       | _, _ -> ())

(* Two pairwise-independent scripts that both apply from `s` compose to the same state in
   either order, and that state exists — the diamond lifted from ops to scripts. F#: Phase
   80's `a @ b` / `b @ a` confluence at full script length, which is the law this whole
   proof rests on and the only one a shipped witness certifies. *)
let rec replay_diamond (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint)
  (x y:list op) (s:state)
  : Lemma (requires independence_diamond fp apply /\
                    (forall (a b:op). mem a x ==> mem b y ==> independent (fp a) (fp b)) /\
                    Ok? (replay apply x s) /\ Ok? (replay apply y s))
          (ensures Ok? (replay apply (app x y) s) /\
                   replay apply (app x y) s == replay apply (app y x) s)
          (decreases x)
  = match x with
    | [] -> app_nil_r y
    | a :: x' ->
      assert (mem a x);
      assert (forall (b:op). mem b y ==> independent (fp a) (fp b));
      assert (Ok? (apply a s));
      replay_push_through apply fp a y s;
      (match apply a s, replay apply y s with
       | Ok sa, Ok sy ->
         assert (Ok? (replay apply y sa));
         assert (apply a sy == replay apply y sa);
         assert (Ok? (replay apply x' sa));
         replay_diamond apply fp x' y sa;
         replay_app apply y (a :: x') s;
         replay_app apply y x' sa;
         (match replay apply y sa with
          | Ok t1 ->
            assert (apply a sy == Ok t1);
            assert (replay apply (app y (a :: x')) s == replay apply x' t1);
            assert (replay apply (app y x') sa == replay apply x' t1);
            assert (replay apply (app x y) s == replay apply (app x' y) sa)
          | Error _ -> ())
       | _, _ -> ())

(* A lane that applies from `s` still applies once an independent sibling lane has run. *)
let lane_applies_after (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint)
  (d e:list op) (s s1:state)
  : Lemma (requires independence_diamond fp apply /\
                    (forall (a b:op). mem a d ==> mem b e ==> independent (fp a) (fp b)) /\
                    replay apply d s == Ok s1 /\ Ok? (replay apply e s))
          (ensures Ok? (replay apply e s1))
  = replay_diamond apply fp d e s;
    replay_app apply d e s

(* A lane pair inside a conflict-free sweep is itself conflict-free. *)
let lane_conflicts_nil_each (#op:eqtype) (fp:op -> footprint)
  (d:list op) (rest:list (list op)) (e:list op)
  : Lemma (requires is_empty (lane_conflicts fp d rest) /\ mem e rest)
          (ensures is_empty (conflicts fp d e))
  = match conflicts fp d e with
    | [] -> ()
    | c :: _ ->
      mem_u_lane_conflicts fp d rest c;
      assert (mem_u c (conflicts fp d e));
      assert (mem_u c (lane_conflicts fp d rest));
      assert (lane_conflicts fp d rest == [])

(* … so EVERY remaining lane still applies once the first of a conflict-free set has run.
   This is what carries `lanes_apply` down the induction: the hypothesis is about the BASE
   state, and each step moves the state on. *)
let lanes_apply_after (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint)
  (d:list op) (rest:list (list op)) (s s1:state)
  : Lemma (requires independence_diamond fp apply /\
                    is_empty (lane_conflicts fp d rest) /\
                    replay apply d s == Ok s1 /\
                    (forall (e:list op). mem e rest ==> Ok? (replay apply e s)))
          (ensures forall (e:list op). mem e rest ==> Ok? (replay apply e s1))
  = let aux (e:list op) : Lemma (mem e rest ==> Ok? (replay apply e s1)) =
      if mem e rest then begin
        lane_conflicts_nil_each fp d rest e;
        conflicts_nil_pairwise fp d e;
        lane_applies_after apply fp d e s s1
      end
      else ()
    in
    FStar.Classical.forall_intro aux

(* THEOREM (fold half): a conflict-free lane set whose lanes all apply from the base state
   replays to the same state under every arrival order. *)
let rec replay_perm (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint)
  (ls1 ls2:list (list op)) (p:perm (list op) ls1 ls2) (s:state)
  : Lemma (requires independence_diamond fp apply /\
                    is_empty (all_conflicts fp ls1) /\
                    lanes_apply apply ls1 s)
          (ensures replay apply (concat ls1) s == replay apply (concat ls2) s)
          (decreases p)
  = match p with
    | PNil -> ()
    | PSkip x m1 m2 p' ->
      assert (mem x (x :: m1));
      assert (Ok? (replay apply x s));
      (match replay apply x s with
       | Ok s1 ->
         lanes_apply_after apply fp x m1 s s1;
         replay_perm apply fp m1 m2 p' s1;
         replay_app apply x (concat m1) s;
         replay_app apply x (concat m2) s
       | Error _ -> ())
    | PSwap x y l ->
      (* all_conflicts (x :: y :: l) starts with conflicts x y, so x and y are independent. *)
      assert (mem x (x :: y :: l));
      assert (mem y (x :: y :: l));
      assert (is_empty (conflicts fp x y));
      conflicts_nil_pairwise fp x y;
      replay_diamond apply fp x y s;
      app_assoc x y (concat l);
      app_assoc y x (concat l);
      replay_app apply (app x y) (concat l) s;
      replay_app apply (app y x) (concat l) s
    | PTrans m1 m2 m3 p12 p23 ->
      all_conflicts_perm_empty fp m1 m2 p12;
      FStar.Classical.forall_intro (perm_mem m1 m2 p12);
      replay_perm apply fp m1 m2 p12 s;
      replay_perm apply fp m2 m3 p23 s

(* ======================================================================================
   10. THE THEOREM — fold confluence (the Phase 100 law, mechanised).
       The same lanes fold to the same state however they arrive; a lane set that cannot
       fold halts with the same canonical report however it arrives; and no lane set folds
       under one order and halts under another.
   ====================================================================================== *)

(* The HALT half, with NO domain hypothesis at all — not the diamond, and nothing about
   `apply`. Whether a lane set halts, and the canonical report it halts with, are
   properties of the footprints alone. This half therefore holds for every domain,
   including one whose lanes reject; it is stated separately so that "the halt half is
   unconditional" is machine-checked rather than asserted in prose. *)
val fold_confluence_halt (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint) (s0:state)
  (ls1 ls2:list (list op)) (p:perm (list op) ls1 ls2)
  : Lemma (requires not (is_empty (all_conflicts fp ls1)))
          (ensures outcome_equiv (fold_once apply fp s0 ls1) (fold_once apply fp s0 ls2))

let fold_confluence_halt #op #state #rej apply fp s0 ls1 ls2 p =
  all_conflicts_perm_empty fp ls1 ls2 p;
  FStar.Classical.forall_intro (all_conflicts_perm fp ls1 ls2 p)

(* The theorem. Its one DOMAIN hypothesis is `independence_diamond` — the promise Phase 80
   certifies and `ProofOracleTests.fs` measures on the reference witness. `lanes_apply` is
   not a domain promise but a statement about the lane set in hand: the lanes all apply
   from the base state, which is the lane set `foldOnce`'s generators produce and the only
   one the fold half was ever about. A lane set with a rejecting lane is outside the claim
   (the halt half above still covers it whenever it halts). *)
val fold_confluence (#op:eqtype) (#state #rej:Type)
  (apply:op -> state -> outcome state rej) (fp:op -> footprint) (s0:state)
  (ls1 ls2:list (list op)) (p:perm (list op) ls1 ls2)
  : Lemma (requires independence_diamond fp apply /\ lanes_apply apply ls1 s0)
          (ensures outcome_equiv (fold_once apply fp s0 ls1) (fold_once apply fp s0 ls2))

let fold_confluence #op #state #rej apply fp s0 ls1 ls2 p =
  match all_conflicts fp ls1 with
  | [] ->
    all_conflicts_perm_empty fp ls1 ls2 p;
    replay_perm apply fp ls1 ls2 p s0
  | _ ->
    fold_confluence_halt apply fp s0 ls1 ls2 p

(* ======================================================================================
   11. The DAG beneath the fold (Phase 134).

   Sections 0–10 start where the lane deltas are already known. Production does not: it holds a
   content-addressed DAG and rebuilds each lane's delta from it — `Dag.ancestorsOf`, the
   topological order `Dag.between` walks, `Dag.betweenOps` — and only then hands the deltas to
   the pairwise sweep. This section models that step for the shape `FoldConfluence.foldOnce`
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
   node's id directly. And **how the topological order is CHOSEN**: production's `topoOrder` is
   Kahn's algorithm draining a ready frontier smallest-id-first, and `topo_of` below is the
   reverse of the parent walk. On the base-plus-N-chains shape the head's closure is a spine,
   every pair of its members is comparable under the ancestor relation, and a spine therefore
   admits exactly ONE topological order — so the two coincide. That last sentence is ARGUED here
   and measured by the differential host against the real `Dag.betweenOps`; it is not mechanised.
   Mechanising it means proving that a distinct enumeration of a spine's closure respecting each
   node's one parent is forced, and then that Kahn's drain produces such an enumeration — a
   separate piece of work, and the honest successor to this one. Everything downstream of the
   order — the closure, the difference against the base's closure, the lookup, and the fold built
   on them — is proved.
   ====================================================================================== *)

(* ---- list helpers the recovery needs, in the module's own Prims-only idiom ---- *)

let rec rev (#a:Type) (l:list a) : Tot (list a) =
  match l with
  | [] -> []
  | x :: t -> app (rev t) [x]

let rec distinct (#a:eqtype) (l:list a) : Tot bool =
  match l with
  | [] -> true
  | x :: t -> not (mem x t) && distinct t

let rec rev_app (#a:Type) (l m:list a)
  : Lemma (ensures rev (app l m) == app (rev m) (rev l))
  = match l with
    | [] -> ()
    | _ :: t -> rev_app t m

let rec rev_rev (#a:Type) (l:list a)
  : Lemma (ensures rev (rev l) == l)
  = match l with
    | [] -> ()
    | x :: t ->
      rev_rev t;
      rev_app (rev t) [x]

let rec mem_rev (#a:eqtype) (x:a) (l:list a)
  : Lemma (ensures mem x (rev l) == mem x l) [SMTPat (mem x (rev l))]
  = match l with
    | [] -> ()
    | y :: t ->
      mem_rev x t;
      mem_app x (rev t) [ y ]

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

(* F#: `topoOrder dag head`. On this shape the head's closure is a spine, so its topological
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

(* F#: `Dag.between` — `topoOrder head |> List.filter (fun id -> not (Set.contains id
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

(* … and therefore `Dag.betweenOps` returns the lane's ops. This is the claim the README's ladder
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
  fold_confluence apply fp s0
    (deltas_of d fuel base_id hs1) (deltas_of d fuel base_id hs2)
    (perm_deltas d fuel base_id hs1 hs2 p)
