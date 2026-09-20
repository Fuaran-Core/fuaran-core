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
   node's id directly (it is modelled since Phase 158, in section 14, for the shape where a
   divergence point does have to be located). And **how the topological order is CHOSEN**: production's `topoOrder` is
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

(* ======================================================================================
   12. The topological order on a spine is FORCED (Phase 142).

   Section 11 computes the whole DAG layer beneath the fold and leaves exactly one step ARGUED
   rather than proved. Production's `topoOrder` drains a ready frontier smallest-id-first; the
   model's `topo_of` reverses the parent walk; and "on a spine there is only one topological order
   anyway, so the two coincide" was a sentence in a comment. This section is that sentence,
   mechanised — over the same base-plus-N-chains model, with no premise beyond the id distinctness
   section 11 already names.

   THE TWO LEMMAS.
     - `spine_order_forced` — a DISTINCT enumeration of a spine's closure in which every node
       follows its own parent is that spine in append order. A list argument: it mentions no DAG,
       no hash and no production function. This is the "exactly one topological order exists" half.
     - `kahn_drain_is_such_an_enumeration` — the frontier drain (F#: the `while` loop of `topoCore`
       over `indeg` / `ready`) run over a spine's closure produces exactly that enumeration. This
       is the "and production's algorithm produces it" half.

   HOW THE TIE-BREAK IS TREATED, which is the one modelling decision worth reading twice.
   Production picks `List.head` of a SORTED ready list — smallest id. The model takes the selector
   as a PARAMETER `pick`, constrained only to return a member of the frontier it is handed
   (`picks_from_frontier`), and every result below is stated for EVERY such selector. That is the
   precise sense in which the tie-break is UNEXERCISED here rather than modelled:
   `kahn_frontier_singleton` proves the frontier is a ONE-element list at every step of a spine's
   drain, so every selector returns the same element and the choice cannot be observed — which is
   also why nothing here says a word about how ids compare. Modelling a smallest-id tie-break over
   a frontier WIDER than one is the merge-DAG case, and it stays out of scope exactly as Phase 134
   left it; a frontier wider than one is precisely where it would begin to matter.

   WHAT IT BUYS SECTION 11. `between_chain_any_order` is `between_chain` restated with the
   topological order UNIVERSALLY QUANTIFIED — any distinct, parent-respecting enumeration of the
   head's closure — where section 11 fixed it to `topo_of` by definition;
   `reconcile_many_dag_ordered_eq` and `fold_once_dag_ordered_eq` carry that up to the fold, so
   `Dag.reconcileMany` FROM the DAG is the deltas-first fold whatever order each head's recovery
   walked. Section 11's own statements are unchanged and now rest on a proved step rather than an
   argued one: the order `topo_of` takes is the only order there is.

   WHAT IS STILL NOT CLAIMED. The general topological order over a MERGE DAG, where a node has two
   parents, the frontier genuinely widens and the choice is genuinely a choice. `Dag.mergeBase`,
   which is not on this path at all. Both stay where Phase 134's own statement put them.
   (Both have since been taken: the order by section 13, Phase 156; `Dag.mergeBase` and the
   recovery over a merged head by section 14, Phase 158.)
   ====================================================================================== *)

(* ---- 12.1 what a topological order IS, as a property of a list ---- *)

(* `x` occurs in `l`, and `y` occurs strictly after it. F#: what "parents before children" means of
   the list `topoCore` emits — a comparison of positions, spelled without positions so the module
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
   `parents_chain` where it is needed. F#: the property `topoCore`'s emitted order has by
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

(* ---- 12.2 the list argument: such an enumeration is FORCED ---- *)

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

   F#: `Dag.between` filters `topoOrder dag head` against the base's closure and looks the ids up,
   so the ONLY freedom the recovery has is which topological order that first step produced. This
   says there is none to have. *)
let spine_order_forced (#op:eqtype) (ns:list (node op)) (q:string) (ord:list string)
  : Lemma (requires parents_chain ns q /\ distinct (q :: ids_of ns) /\ distinct ord /\
                    (forall (x:string). mem x ord == mem x (q :: ids_of ns)) /\
                    follows_parents ns ord)
          (ensures ord == q :: ids_of ns)
  = follows_parents_spine ns q ord;
    spine_ids_forced q (ids_of ns) ord

(* ---- 12.3 … and the append order IS such an enumeration ---- *)

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

(* ---- 12.4 Kahn's frontier drain (F#: `topoCore`) ---- *)

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
   every iteration of `topoCore`'s loop over this closure, so `List.sort` is the identity and
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

(* ---- 12.5 the bridge to a lane: `lane_nodes` IS a spine off the base ---- *)

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

(* ---- 12.6 the restatement: the recovery does not depend on the order ---- *)

(* What the ORDER's uniqueness needs of content addressing, and it is the same premise section 11
   already names rather than a new one: the lane's ids are distinct from one another and from the
   base's. `distinct_ids` / `resolves_of_distinct` state it for the whole DAG; this is that premise
   read at lane granularity, exactly as `lane_recovers`'s `resolves` is. It SUBSUMES
   `lane_recovers`'s third clause (the base id is not one of the lane's), which is its head. *)
let lane_ids_distinct (#op:eqtype) (mint:string -> string -> op -> string)
  (bn:node op) (ln:lane op) : Tot bool =
  distinct (bn.nid :: ids_of (lane_nodes mint ln.lactor bn.nid ln.lops))

(* F#: `Dag.between` from the point where the topological order has been chosen —
   `topoOrder dag head |> List.filter … |> List.map …` with the first step's RESULT taken as a
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

(* ======================================================================================
   13. The Kahn drain over an ABSTRACT DAG (Phase 156).

   Section 12 proves the order on a SPINE, which is the shape one writer's lane has. The shape a
   clone actually folds is the UNION of N lanes off a shared base, and there the ready frontier is
   N wide at its first step — so the tie-break section 12 could leave as an unexercised parameter
   is what decides the sequence, and "every clone computes the same order from the same node set"
   rests on it. This section is that sentence, mechanised, over an abstract node set rather than
   over the base-plus-N-chains shape: no `mint`, no lanes, no base — just nodes naming parents.

   THE DRAIN. `drain` is section 12's own `kahn` at the selector `pick_min lt` — the smallest id
   in the ready frontier — wrapped by the dangling-parent policy below. It is the same function
   and not a second one, deliberately: a re-modelled drain would have to be re-related to the one
   section 12's spine results are about, and the relating is the part that goes wrong.

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
     - `IgnoreDangling` — F#: `Dag.topoCore`'s closure walk (`match Map.tryFind id dag.Nodes with
       | Some n -> … | None -> collect acc rest`) with `Dag.ancestorsOf`'s `ContainsKey` guard
       beside it, and the `parentsIn` filter that follows. A parent the node set does not hold
       never enters the closure and is filtered out of the in-degree, so it constrains nothing and
       the drain proceeds. This is the policy on the FOLD path — `topoOrder` -> `between` ->
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
       that and section 12's `pick_head` is not. A drain taking the head of an unsorted frontier
       satisfies `picks_from_frontier` and FAILS this theorem, which is the precise sense in which
       smallest-id-first is load-bearing rather than decoration.

   HOW A CYCLE IS SURFACED, which is a claim about production and worth stating exactly.
   `topoCore` does not raise: a node inside a cycle never reaches in-degree zero, so the emitted
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
   have this property, and section 12 did not need it — on a spine the frontier is one element
   wide at every step. *)
let pick_min_same (lt:string -> string -> bool) (l1 l2:list string)
  : Lemma (requires total_order lt /\ Cons? l1 /\ Cons? l2 /\
                    (forall (x:string). mem x l1 == mem x l2))
          (ensures pick_min lt l1 == pick_min lt l2)
  = pick_min_mem lt l1;
    pick_min_mem lt l2;
    pick_min_least lt l1 (pick_min lt l2);
    pick_min_least lt l2 (pick_min lt l1)

(* … and it is a tie-break in section 12's sense, so every result there holds of it. *)
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

(* Section 12's `all_before` weakened by an ALREADY-PLACED set: a parent is satisfied either by
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

(* Every node's IN-SET parents precede it — section 12's `follows_parents` with `parents_in` in
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

(* F#: the nodes `Dag.firstBreak` would fault with `MissingParent`. It scans `Map.toList`, which is
   id-ordered, and its docstring says so — so its answer does not depend on insertion order, which
   is why the refusal below names the SMALLEST such id rather than the first in the work list, and
   why `drain_deterministic` can cover the refusing policy too. *)
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

(* F#: `topoCore`'s emitted order at the smallest-id tie-break, over the set as its own closure. *)
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
   F#: what `topoCore`'s emitted list has by construction — a node is emitted only once its
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
   permuted frontier, and only an order-invariant selector survives that. Section 12's `pick_head`
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
   existence. (`Dag.topoCore`'s `parentsIn` filter and `Dag.firstBreak`'s `tryFind` are asking the
   same question of the same data; they differ only in what they do with the answer.) *)
let drain_policies_agree (#op:eqtype) (lt:string -> string -> bool) (fuel:list (node op))
  (ns:list (node op))
  : Lemma (requires dangling_ids ns (ids_of ns) == [])
          (ensures drain IgnoreDangling lt fuel ns == drain RefuseDangling lt fuel ns)
  = ()

(* THEOREM. … and where they differ, they differ exactly on the presence of a parent outside the
   set. F#: `MissingParent`, which `verifyDag` raises before any drain runs and which `topoOrder`
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

(* ---- 13.12 section 12's spine case, as a corollary ---- *)

(* COROLLARY. Phase 142 quantified over every selector because on a spine the frontier is one
   element wide and the tie-break is unexercised. `pick_min lt` is such a selector
   (`pick_min_is_a_tie_break`), so the spine results instantiate at the id-ordered drain with no
   second argument: section 12's theorem is this section's special case, rather than a parallel
   one that has to be kept in step. *)
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

(* ======================================================================================
   14. Delta recovery over a MERGED HEAD (Phase 158).

   Section 11 proves `Dag.between` for ONE base node and N chains off it, and sections 12 and 13
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
   `topoCore`: the ancestor closure first, then the frontier loop over exactly those nodes.
   `between_drained` is section 12's `between_ordered` at that order. Nothing is re-modelled, so
   the three drain theorems apply as they stand, and `lt` and the drain's fuel stay the
   parameters they were.

   THE TWO THEOREMS, and the one beside them.
     - `between_merged` — over a merged head, `Dag.between m head` returns the head's OWN lane
       nodes in append order and nothing else: not the first round beneath `m`, and nothing from
       a sibling lane. The drain's order over the first round is a real choice and the theorem
       does not care which way it went, because the difference against `m`'s closure deletes
       every node the choice was about; what survives is a spine, and on a spine a
       parent-respecting order is forced (section 12's `spine_ids_forced`, consumed here over the
       difference rather than over the closure). `between_ops_merged` is the ops.
     - `reconcile_many_merged_eq` / `fold_once_merged_eq` — `Dag.reconcileMany` and
       `FoldConfluence.foldOnce` FROM the merged DAG equal the deltas-first fold on the lanes that
       were appended, as section 11 proved them over a spine.
     - `merge_base_is_divergence` — `Dag.mergeBase` of two second-round heads is `m`: the
       divergence point, not the original base and not anything else they share. `merge_base` is
       production's function clause for clause — the intersection of the two closures, then the
       maximum by (closure SIZE, id). The size comparison is `shorter` over the de-duplicated
       closures, so the oracle still holds no integer; that `m` wins is production's own docstring
       sentence ("a node strictly deeper than any of its own ancestors has a strictly larger
       closure"), PROVED: the closure is transitive (`anc_trans`), `m` is in no proper ancestor's
       closure (`off_cycle`), and a distinct list strictly inside another is `shorter`
       (`pigeon`). The id tie-break is therefore UNEXERCISED on this shape — the maximum is
       strict — and the theorem holds for every `lt` whatsoever, total order or not.

   THE PREMISES, which are sections 11 and 13's and no new one.
     - ID DISTINCTNESS, exactly as Phase 134 states it and read at this shape's granularity
       (`merged_recovers`, `lane_ids_distinct`): every lane node is the node the DAG holds under
       its own id, and no lane id is an id in `m`'s closure. Phase 134's third clause — "the
       base's id is not one of the lane's" — is this clause at a base whose closure is itself.
       `merge_base_is_divergence` also asks that the two lanes' ids be disjoint, which is what
       `foldOnce` folds the actor into every node id FOR.
     - ACYCLICITY, as section 13 takes it: a topological enumeration supplied as a witness, per
       head for the drain (`head_drains`) and of `m`'s closure for `mergeBase`.
       `merge_base_is_divergence` consumes it in the form it actually uses — `off_cycle`, "`m` is
       not its own proper ancestor" — and `off_cycle_of_witness` DERIVES that form from the
       witness (an ancestor stands before its descendant in any topological enumeration,
       `anc_before`), exactly as `resolves_of_distinct` bridges section 11's premise.
       `merge_base_is_divergence_acyclic` is the theorem with the bridge applied.
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

(* F#: the `anc` set of `topoCore`, as the nodes it names — the set the frontier loop runs over. *)
let rec closure_nodes (#op:eqtype) (ns:list (node op)) (closure:list string)
  : Tot (list (node op)) =
  match ns with
  | [] -> []
  | n :: t -> if mem n.nid closure then n :: closure_nodes t closure else closure_nodes t closure

(* F#: `topoOrder dag head` — the closure, then section 13's drain over exactly that node set. *)
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

(* ---- 14.6 `Dag.mergeBase` ---- *)

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

(* F#: `Dag.mergeBase`, clause for clause. `None` is `Missing`. *)
let merge_base (#op:eqtype) (lt:string -> string -> bool) (d:dag op) (fuel:list (node op))
  (left right:string) : Tot (found string) =
  match inter (ancestors_of d fuel left) (ancestors_of d fuel right) with
  | [] -> Missing
  | c :: t -> Found (max_by (deeper lt d fuel) c t)

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

(* THEOREM. `Dag.mergeBase` of two lane heads off a merged head is that merged head — the
   divergence point. The common ancestors of the two heads are exactly `m`'s closure (the lanes
   share no id with each other or with it), and `m` is strictly the deepest of them. *)
#push-options "--z3rlimit 150"
let merge_base_is_divergence (#op:eqtype) (lt:string -> string -> bool) (d:dag op)
  (mint:string -> string -> op -> string) (m:node op) (fuel:list (node op))
  (ln1 ln2:lane op)
  : Lemma (requires lookup d.nodes m.nid == Found m /\ Cons? fuel /\
                    merged_recovers d mint m fuel ln1 /\ merged_recovers d mint m fuel ln2 /\
                    off_cycle d fuel m /\
                    (forall (x:string).
                       mem x (ids_of (lane_nodes mint ln1.lactor m.nid ln1.lops)) ==>
                       not (mem x (ids_of (lane_nodes mint ln2.lactor m.nid ln2.lops)))))
          (ensures merge_base lt d fuel (lane_head mint ln1.lactor m.nid ln1.lops)
                                        (lane_head mint ln2.lactor m.nid ln2.lops)
                   == Found m.nid)
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
    assert (mem q common);
    (match common with
     | [] -> ()
     | c :: t ->
       let aux_d (y:string)
         : Lemma (mem y (c :: t) /\ y =!= q ==>
                  (deeper lt d fuel q y /\ not (deeper lt d fuel y q))) =
         if mem y (c :: t) && y <> q then strictly_deeper lt d fuel m y
       in
       FStar.Classical.forall_intro aux_d;
       max_by_is (deeper lt d fuel) c t q)
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

(* THEOREM, with the bridge applied: `mergeBase` finds the divergence point on sections 11 and
   13's premises alone. *)
let merge_base_is_divergence_acyclic (#op:eqtype) (lt:string -> string -> bool) (d:dag op)
  (mint:string -> string -> op -> string) (m:node op) (fuel:list (node op))
  (ln1 ln2:lane op) (wm:list string)
  : Lemma (requires lookup d.nodes m.nid == Found m /\ Cons? fuel /\
                    merged_recovers d mint m fuel ln1 /\ merged_recovers d mint m fuel ln2 /\
                    is_topo_enum (closure_nodes d.nodes (ancestors_of d fuel m.nid)) wm /\
                    (forall (x:string).
                       mem x (ids_of (lane_nodes mint ln1.lactor m.nid ln1.lops)) ==>
                       not (mem x (ids_of (lane_nodes mint ln2.lactor m.nid ln2.lops)))))
          (ensures merge_base lt d fuel (lane_head mint ln1.lactor m.nid ln1.lops)
                                        (lane_head mint ln2.lactor m.nid ln2.lops)
                   == Found m.nid)
  = at_least_drop fuel (rev ln1.lops);
    anc_stable d (drop_by fuel (rev ln1.lops)) fuel m.nid;
    off_cycle_of_witness d fuel m wm;
    merge_base_is_divergence lt d mint m fuel ln1 ln2
