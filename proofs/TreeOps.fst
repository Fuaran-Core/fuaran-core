(*
   TreeOps — an F* model of Fuaran.Core's skeleton-op tree algebra, with the fold theorem's one
   DOMAIN HYPOTHESIS proved for it rather than sampled (fuaran-core Phase 133).

   WHAT IS MODELLED. `Ops.apply` over the `NodeWitness`/`IdWitness` view of a tree: a node is an
   id, a kind tag and an ordered child list, and nothing else is visible to the algebra. The six
   skeleton ops (`InsertChild`, `RemoveNode`, `MoveNode`, `ReorderChildren`, `Batch`, and since
   Phase 250 `UpdateNode`), each validation clause and each `Rejection` it raises, and
   `Ops.footprint` clause for clause. `UpdateNode`'s content is the one piece of a node the witness
   shows — its kind tag — so an in-place rewrite is modelled as a rewrite of the kind, children
   kept, which is everything the shipped op can do that the algebra can see.

   WHAT IS PROVED. `tree_independence_diamond`: for every pair of ops whose footprints
   `Ops.independent` declares disjoint, and every WELL-FORMED tree at which both apply, each
   applies after the other and the two orders reach the same tree. That is exactly
   `DagFold.independence_diamond` at this domain, and `Skeleton.fst` composes the two into
   `skeleton_fold_confluence` — fold confluence for `SkeletonOp` with no domain hypothesis left.

   The proof is not fifteen bespoke cases. It is three facts:

     1. `relocating_forces_inert` — the pinned over-approximation does most of the work. A
        `RemoveNode` or a `MoveNode` writes an UNKNOWN parent, and `Ops.independent` refuses
        independence between an unknown-parent write and ANY structural write. Every skeleton op
        except a structure-free `Batch` writes structure, so a remove or a move is independent
        only of an op that does nothing at all. NINE of the fifteen unordered pairs are closed
        by this lemma alone, and the tree is never looked at. Phase 250's `UpdateNode` carries an
        unknown-parent write too — REQUIRED, not cautious: an update of `x` and a remove of an
        ancestor of `x` share no address the script can name and do not commute — so all six of
        the pairs it adds close the same way (`update_is_relocating`).
     2. `diamond_sym` — the diamond's conclusion is symmetric in the pair, so the remaining
        ordered cases halve.
     3. Three concrete commutation equalities on the tree — insert/insert, insert/reorder,
        reorder/reorder — plus the lift of the leaf diamond along a `Batch`'s script (section 20,
        Phase 162), in the shape `DagFold.replay_diamond` already uses at lane granularity. The
        alphabet is therefore the WHOLE of `SkeletonOp`, `Batch` included and nested to any
        depth; Phase 133 shipped this module with that one shape open and `covered` naming it,
        and Phase 162 closed it.

   WHY WELL-FORMEDNESS. `Tree.tryFind` and `Tree.parentOf` resolve an id to the FIRST node in
   preorder, and `ReorderChildren` validates against the children of the node they resolve to.
   `reorder_at` permutes a parent's children, which moves preorder positions — so on a tree
   carrying one id twice, two footprint-independent reorders can validate differently depending
   on which ran first. The diamond is therefore a statement about ID-UNIQUE trees. `Ops.apply` is
   faithful to that; the refinement is where the model says it out loud.

   WHAT IS NOT MODELLED. `applyContained`'s container capability (`canHold` is `fun _ -> true`
   under `apply`, so `NotAContainer` is unreachable here and is carried in the rejection
   vocabulary only so the envelope set is complete), `Ops.invert`, `Ops.normalize` and `Diff`.
   The per-node payload a domain hangs off a node is invisible to the witness and so to this
   model.

   HOW TO READ IT. Every definition names its F# counterpart, as in `DagFold.fst`. The
   list-as-set reading and the membership algebra are that module's, opened here rather than
   restated — `TreeOps` extracts beside `DagFold` into the same oracle assembly.

   Apache-2.0, like everything beside it.
*)
module TreeOps

open DagFold

(* This model is an order of magnitude larger than `DagFold.fst` — some ninety definitions and
   lemmas where that one has thirty — and the SMT context grows with it: every membership lemma's
   pattern is live at every subsequent query. Measured on the pinned prover, checking it takes
   300s with the default context and 56s with upstream's context pruning, which is the successor
   to the proof hints the pinned release removed (README finding 1). The leg runs `--quake 3`
   three times from a cold cache, so that difference is forty minutes of CI against seven.
   Scoped here rather than added to `check.ps1`'s flags: pruning changes which facts a query can
   see, so a module that has not been checked under it must not be switched to it as a side
   effect of another module's cost. *)
#set-options "--ext context_pruning"

(* ======================================================================================
   0. The tree as the witness sees it (F#: `NodeWitness<'Node,'Id>` in Tree.fs — `Id`,
      `KindTag`, `Children`, `ReplaceChildren`, and nothing else).

      Ids are strings because `IdWitness.ToString` is the key form every address in
      `Footprint` is written in; the algebra never demands `comparison` of an id, only
      `Equals`, which is `=` here.
   ====================================================================================== *)

type tree =
  | TNode : tid:string -> kind:string -> kids:list tree -> tree

(* F#: `w.Id`, `w.KindTag`, `w.Children`. *)
let tid_of (t:tree) : Tot string = match t with TNode i _ _ -> i
let kind_of (t:tree) : Tot string = match t with TNode _ k _ -> k
let kids_of (t:tree) : Tot (list tree) = match t with TNode _ _ cs -> cs

(* F#: `Tree.preorder |> List.map w.Id` — `Tree.ids`. Node then children, left to right. *)
let rec ids (t:tree) : Tot (list string) (decreases t) =
  match t with
  | TNode i _ cs -> i :: ids_all cs
and ids_all (ts:list tree) : Tot (list string) (decreases ts) =
  match ts with
  | [] -> []
  | t :: r -> app (ids t) (ids_all r)

(* The ids of a child LIST at one level (F#: `w.Children p |> List.map w.Id`, the list
   `validateReorder` compares against the proposed order). *)
let rec kid_ids (ts:list tree) : Tot (list string) =
  match ts with
  | [] -> []
  | t :: r -> tid_of t :: kid_ids r

(* F#: `Tree.exists`. *)
let has_id (x:string) (t:tree) : Tot bool = mem x (ids t)

(* F#: `Tree.tryFind` — the FIRST preorder match. *)
let rec find_in (x:string) (t:tree) : Tot (option tree) (decreases t) =
  match t with
  | TNode i _ cs -> if i = x then Some t else find_all x cs
and find_all (x:string) (ts:list tree) : Tot (option tree) (decreases ts) =
  match ts with
  | [] -> None
  | t :: r -> (match find_in x t with Some n -> Some n | None -> find_all x r)

(* F#: `Tree.parentOf`, as its id — the first preorder node one of whose CHILDREN carries `x`. *)
let rec has_kid (x:string) (ts:list tree) : Tot bool =
  match ts with
  | [] -> false
  | t :: r -> tid_of t = x || has_kid x r

let rec parent_of (x:string) (t:tree) : Tot (option string) (decreases t) =
  match t with
  | TNode i _ cs -> if has_kid x cs then Some i else parent_all x cs
and parent_all (x:string) (ts:list tree) : Tot (option string) (decreases ts) =
  match ts with
  | [] -> None
  | t :: r -> (match parent_of x t with Some p -> Some p | None -> parent_all x r)

(* ======================================================================================
   1. Well-formedness (F#: the invariant `Ops.apply` ASSUMES and never states — `Tree.updateNode`
      rebuilds every node whose id matches, `tryFind`/`parentOf` resolve to the first).

      Written structurally rather than as `no_dups (ids t)` — the two are the same predicate,
      and this shape is the one the preservation arguments are about: a node's id is not in its
      own subtree below it, and sibling subtrees share no id. Membership and disjointness only,
      so the whole of DagFold's membership algebra applies to it directly.
   ====================================================================================== *)

let rec wf (t:tree) : Tot bool (decreases t) =
  match t with
  | TNode i _ cs -> not (mem i (ids_all cs)) && wf_all cs
and wf_all (ts:list tree) : Tot bool (decreases ts) =
  match ts with
  | [] -> true
  | t :: r -> wf t && wf_all r && disjoint (ids t) (ids_all r)

(* ======================================================================================
   2. The rejection envelope (F#: `Rejection<'Id>` in Ops.fs).

      Seven cases there, FIVE of them reachable from `Ops.apply`: `NotAContainer` is raised only
      by `applyContained`, whose `canHold` `apply` fixes at `fun _ -> true`, and `Rejected` is
      the domain-side extension point Core itself never raises. Both are carried so the envelope
      vocabulary is complete and the differential can compare by class over the whole of it.
   ====================================================================================== *)

type rejection =
  | UnknownNode        : target:string -> addressable:list string -> rejection
  | DuplicateId        : string -> rejection
  | CannotRemoveRoot   : rejection
  | WouldNestUnderSelf : string -> rejection
  | NotAContainer      : target:string -> kind_tag:string -> rejection
  | ReorderMismatch    : parent:string -> expected:list string -> got:list string -> rejection
  | Rejected           : code:string -> message:string -> rejection

(* ======================================================================================
   3. The skeleton ops (F#: `SkeletonOp<'Node,'Id>`). `UpdateNode` is declared LAST, as it is
      there (Phase 250): a case's declaration order is its tag number.
   ====================================================================================== *)

type op =
  | InsertChild     : parent:string -> node:tree -> op
  | RemoveNode      : target:string -> op
  | MoveNode        : target:string -> new_parent:string -> op
  | ReorderChildren : parent:string -> order:list string -> op
  | Batch           : list op -> op
  | UpdateNode      : node:tree -> op

(* ======================================================================================
   4. The three structural edits `Tree.updateNode` performs, first-order.

      `Tree.updateNode w idw target f root` rebuilds bottom-up and applies `f` at EVERY node whose
      id is `target` (the `hit` flag records only whether there was one). Under `wf` that is one
      node; the model is faithful to the general case, which is what lets the differential run on
      whatever the generator produces.
   ====================================================================================== *)

(* F#: the `InsertChild` arm — `updateNode parent (fun p -> ReplaceChildren p (Children p @ [node]))`.
   The inserted subtree is appended RAW: the rebuild has already passed over the parent's children
   by the time `f` runs, so nothing descends into `n`. *)
let rec ins (p:string) (n:tree) (t:tree) : Tot tree (decreases t) =
  match t with
  | TNode i k cs ->
    let cs' = ins_all p n cs in
    if i = p then TNode i k (app cs' [n]) else TNode i k cs'
and ins_all (p:string) (n:tree) (ts:list tree) : Tot (list tree) (decreases ts) =
  match ts with
  | [] -> []
  | t :: r -> ins p n t :: ins_all p n r

(* F#: `List.filter (fun c -> not (eq (w.Id c) target))` inside the `RemoveNode` arm — every
   child carrying the id goes, not merely the first. *)
let rec drop_kid (x:string) (ts:list tree) : Tot (list tree) =
  match ts with
  | [] -> []
  | t :: r -> if tid_of t = x then drop_kid x r else t :: drop_kid x r

(* F#: the `RemoveNode` arm — `updateNode (w.Id p) (fun p -> ReplaceChildren p (filtered))`, where
   `p` is the SOURCE PARENT `Tree.parentOf` found. *)
let rec rem_at (pid:string) (x:string) (t:tree) : Tot tree (decreases t) =
  match t with
  | TNode i k cs ->
    let cs' = rem_all pid x cs in
    if i = pid then TNode i k (drop_kid x cs') else TNode i k cs'
and rem_all (pid:string) (x:string) (ts:list tree) : Tot (list tree) (decreases ts) =
  match ts with
  | [] -> []
  | t :: r -> rem_at pid x t :: rem_all pid x r

(* F#: `let byId = … |> Map.ofList in order |> List.map (fun i -> byId.[key i])`. `Map.ofList`
   keeps the LAST entry for a repeated key, so the lookup is last-wins; and the indexer would
   throw on a missing key, which `validateReorder` has already ruled out — the model drops such an
   id instead of being partial, and the validation below makes the two agree. *)
let rec pick_last (x:string) (ts:list tree) : Tot (option tree) =
  match ts with
  | [] -> None
  | t :: r ->
    (match pick_last x r with
     | Some n -> Some n
     | None -> if tid_of t = x then Some t else None)

let rec arrange (order:list string) (ts:list tree) : Tot (list tree) =
  match order with
  | [] -> []
  | x :: rest ->
    (match pick_last x ts with
     | Some n -> n :: arrange rest ts
     | None -> arrange rest ts)

(* F#: the `ReorderChildren` arm — `updateNode parent (fun p -> ReplaceChildren p reordered)`. *)
let rec reorder_at (p:string) (order:list string) (t:tree) : Tot tree (decreases t) =
  match t with
  | TNode i k cs ->
    let cs' = reorder_all p order cs in
    if i = p then TNode i k (arrange order cs') else TNode i k cs'
and reorder_all (p:string) (order:list string) (ts:list tree) : Tot (list tree) (decreases ts) =
  match ts with
  | [] -> []
  | t :: r -> reorder_at p order t :: reorder_all p order r

(* F#: the `UpdateNode` arm (Phase 250) — `updateNode (w.Id node) (fun existing -> ReplaceChildren
   node (Children existing))`: the node takes the payload's content and keeps its own children. The
   content the witness shows is the kind tag, so that is what moves; the payload's children are
   not read, which is why the argument is a kind and not a tree. Applied at EVERY node carrying the
   id, as `Tree.updateNode` does. *)
let rec upd (x:string) (k:string) (t:tree) : Tot tree (decreases t) =
  match t with
  | TNode i k0 cs ->
    let cs' = upd_all x k cs in
    if i = x then TNode i k cs' else TNode i k0 cs'
and upd_all (x:string) (k:string) (ts:list tree) : Tot (list tree) (decreases ts) =
  match ts with
  | [] -> []
  | t :: r -> upd x k t :: upd_all x k r

(* ======================================================================================
   5. `validateReorder`'s permutation test.

      F# compares `List.sort (List.map key current)` with `List.sort (List.map key order)` — two
      sorted lists are equal exactly when the two lists are equal as MULTISETS, which is what this
      decides, without needing a total order on ids (`IdWitness` exposes only `Equals`, so a model
      that sorted would be modelling something the witness does not have).
   ====================================================================================== *)

let rec remove_first (x:string) (l:list string) : Tot (option (list string)) =
  match l with
  | [] -> None
  | h :: t -> if h = x then Some t
              else (match remove_first x t with None -> None | Some t' -> Some (h :: t'))

let rec same_multiset (xs ys:list string) : Tot bool (decreases xs) =
  match xs with
  | [] -> is_empty ys
  | x :: r -> (match remove_first x ys with
               | None -> false
               | Some ys' -> same_multiset r ys')

(* ======================================================================================
   5b. `Ops.firstDuplicateId` — the insert graft's uniqueness scan (Phase 137, modelled by
       Phase 138).

       F#: one scan over `Tree.ids w node` in preorder, seeded from `Tree.ids w root`, naming the
       FIRST id already seen. The seen set is a `Set<string>` there and a list read under `mem`
       here, which is the pack's standing list-for-set reading (README, "sets are lists"); the
       decision is membership only, so nothing the comparison observes is lost.

       Before Phase 137 the check read the inserted node's own id alone. Section 13 keeps that
       older clause under its own name and keeps its refutation with it, so the record of what
       was wrong survives the fix rather than being deleted by it.
   ====================================================================================== *)

let rec scan_dup (seen:list string) (l:list string) : Tot (option string) (decreases l) =
  match l with
  | [] -> None
  | i :: rest -> if mem i seen then Some i else scan_dup (i :: seen) rest

(* F#: `firstDuplicateId w idw node root`. *)
let first_dup (n:tree) (t:tree) : Tot (option string) = scan_dup (ids t) (ids n)

(* ======================================================================================
   6. `Ops.apply` (F#: `applyWith (fun _ -> true) w idw`, clause for clause).
   ====================================================================================== *)

let rec apply (o:op) (t:tree) : Tot (outcome tree rejection) (decreases o) =
  match o with

  (* F#: `validateInsert` — SINCE PHASE 137 the whole inserted subtree is scanned, against the
     tree and against itself, and the first offender in `Tree.ids` order is named; then the parent
     must exist. `Tree.ids` is preorder, so its head is the inserted node's own id and the widened
     scan subsumes the pre-137 check while keeping its precedence over `UnknownNode`. *)
  | InsertChild p n ->
    (match first_dup n t with
     | Some d -> Error (DuplicateId d)
     | None ->
       if not (has_id p t) then Error (UnknownNode p (ids t))
       else Ok (ins p n t))

  (* F#: `validateRemove` then the `parentOf` lookup. *)
  | RemoveNode x ->
    if tid_of t = x then Error CannotRemoveRoot
    else if not (has_id x t) then Error (UnknownNode x (ids t))
    else (match parent_of x t with
          | None -> Error (UnknownNode x (ids t))
          | Some pid -> Ok (rem_at pid x t))

  (* F#: `validateReorder`. *)
  | ReorderChildren p order ->
    (match find_in p t with
     | None -> Error (UnknownNode p (ids t))
     | Some n ->
       let current = kid_ids (kids_of n) in
       if not (same_multiset current order) then Error (ReorderMismatch p current order)
       else Ok (reorder_at p order t))

  (* F#: the `MoveNode` arm — root guard, both endpoints must exist, the new parent must not be
     inside the moved subtree (`Tree.ids w sub` INCLUDES the target, so a self-move is caught
     here), then remove-and-append. *)
  | MoveNode x np ->
    if tid_of t = x then Error CannotRemoveRoot
    else if not (has_id x t) then Error (UnknownNode x (ids t))
    else if not (has_id np t) then Error (UnknownNode np (ids t))
    else (match find_in x t with
          | None -> Error (UnknownNode x (ids t))
          | Some sub ->
            if mem np (ids sub) then Error (WouldNestUnderSelf x)
            else (match parent_of x t with
                  | None -> Error (UnknownNode x (ids t))
                  | Some pid ->
                    let removed = rem_at pid x t in
                    if not (has_id np removed) then Error (UnknownNode np (ids removed))
                    else Ok (ins np sub removed)))

  (* F#: `Batch` — all-or-nothing, threading the tree and abandoning the whole on first failure. *)
  | Batch os -> apply_all os t

  (* F#: `validateUpdate` under `apply` (Phase 250) — the target is the payload's own id and must
     be in the tree; the container clause is `applyContained`'s and `canHold` is `fun _ -> true`
     here. *)
  | UpdateNode n ->
    (match find_in (tid_of n) t with
     | None -> Error (UnknownNode (tid_of n) (ids t))
     | Some _ -> Ok (upd (tid_of n) (kind_of n) t))

and apply_all (os:list op) (t:tree) : Tot (outcome tree rejection) (decreases os) =
  match os with
  | [] -> Ok t
  | o :: r -> (match apply o t with
               | Ok t' -> apply_all r t'
               | Error e -> Error e)

(* ======================================================================================
   7. `Ops.footprint` (F#: `ofOp`, clause for clause, folded over a `Batch`).

      The four address kinds are `DagFold.footprint`'s, read as sets. Note what
      `InsertChild` and `ReorderChildren` both do: the structural parent id lands in `Reads` as
      well as in `StructureWrites`. That is not decoration — it is exactly what makes
      `independent` guarantee that neither op's inserted subtree carries the other's structural
      anchor, which is the fact the commutation lemmas below run on.
   ====================================================================================== *)

let empty_fp : footprint =
  { reads = []; structure_writes = []; content_writes = []; unknown_parent_writes = [] }

let union_fp (a b:footprint) : Tot footprint =
  { reads                 = union a.reads b.reads;
    structure_writes      = union a.structure_writes b.structure_writes;
    content_writes        = union a.content_writes b.content_writes;
    unknown_parent_writes = union a.unknown_parent_writes b.unknown_parent_writes }

let rec op_fp (o:op) : Tot footprint (decreases o) =
  match o with
  | InsertChild p n ->
    let inserted = ids n in
    { reads = p :: inserted; structure_writes = [p];
      content_writes = inserted; unknown_parent_writes = [] }
  | RemoveNode x ->
    { reads = [x]; structure_writes = [];
      content_writes = [x]; unknown_parent_writes = [x] }
  | MoveNode x np ->
    { reads = [x; np]; structure_writes = [np];
      content_writes = [x]; unknown_parent_writes = [x] }
  | ReorderChildren p order ->
    { reads = p :: order; structure_writes = [p];
      content_writes = []; unknown_parent_writes = [] }
  | Batch inner -> fp_all inner
  | UpdateNode n ->
    let x = tid_of n in
    { reads = [x]; structure_writes = [];
      content_writes = [x]; unknown_parent_writes = [x] }
and fp_all (os:list op) : Tot footprint (decreases os) =
  match os with
  | [] -> empty_fp
  | o :: r -> union_fp (op_fp o) (fp_all r)

(* ======================================================================================
   8. The pinned over-approximation does most of the work.

      `Ops.independent`'s last two clauses refuse independence between an op with a NON-EMPTY
      `UnknownParentWrites` and any op that writes structure at all. A `RemoveNode` or a
      `MoveNode` always has one — its own target, whose source parent the script cannot name —
      and every skeleton op except a structure-free `Batch` writes structure. So a remove or a
      move is independent only of an op that DOES NOTHING, and the diamond for those pairs holds
      without ever looking at a tree.

      That is nine of the fifteen unordered pairs: remove/insert, remove/remove, remove/move,
      remove/reorder, move/insert, move/move, move/reorder, and remove/batch and move/batch
      wherever the batch is not inert. The commuting content of this algebra lives entirely in
      the other six.
   ====================================================================================== *)

(* An op with no structural effect at all: the empty `Batch`, and nests of them. Every other
   skeleton op writes at least one structural address. *)
let rec inert (o:op) : Tot bool (decreases o) =
  match o with
  | Batch os -> inert_all os
  | _ -> false
and inert_all (os:list op) : Tot bool (decreases os) =
  match os with
  | [] -> true
  | o :: r -> inert o && inert_all r

let relocating (o:op) : Tot bool = not (is_empty (op_fp o).unknown_parent_writes)

(* Structure-free is exactly inert — the model's link between the FOOTPRINT's verdict and the
   op's shape, and what turns `independent`'s conservative clause into a case elimination. *)
let rec structure_free_iff_inert (o:op)
  : Lemma (ensures not (writes_structure (op_fp o)) == inert o) (decreases o)
  = match o with
    | Batch os -> structure_free_iff_inert_all os
    | _ -> ()
and structure_free_iff_inert_all (os:list op)
  : Lemma (ensures not (writes_structure (fp_all os)) == inert_all os) (decreases os)
  = match os with
    | [] -> ()
    | o :: r -> structure_free_iff_inert o; structure_free_iff_inert_all r

(* An inert op is the identity — nothing to thread, nothing to reject. *)
let rec inert_is_identity (o:op) (t:tree)
  : Lemma (requires inert o) (ensures apply o t == Ok t) (decreases o)
  = match o with
    | Batch os -> inert_all_is_identity os t
    | _ -> ()
and inert_all_is_identity (os:list op) (t:tree)
  : Lemma (requires inert_all os) (ensures apply_all os t == Ok t) (decreases os)
  = match os with
    | [] -> ()
    | o :: r -> inert_is_identity o t; inert_all_is_identity r t

(* THE ELIMINATION. A relocating op — any op carrying a `RemoveNode` or a `MoveNode` — is
   independent only of an inert one. *)
let relocating_forces_inert (a b:op)
  : Lemma (requires independent (op_fp a) (op_fp b) /\ relocating a)
          (ensures inert b)
  = structure_free_iff_inert b

(* … and a remove or a move IS relocating, so the elimination reaches the four leaf shapes it
   names. Stated separately because it is the half a reader checks against `Ops.footprint`. *)
let remove_is_relocating (x:string) : Lemma (ensures relocating (RemoveNode x)) = ()
let move_is_relocating (x np:string) : Lemma (ensures relocating (MoveNode x np)) = ()

(* … and so is an in-place rewrite (Phase 250), which is what closes all six pairs it adds. *)
let update_is_relocating (n:tree) : Lemma (ensures relocating (UpdateNode n)) = ()

(* ======================================================================================
   9. The commuting half — the algebra of the two edits that survive the elimination.

      Everything here is about `ins` and `reorder_at` alone: section 8 has already retired every
      pair in which a `RemoveNode` or a `MoveNode` appears without the other side being inert.
   ====================================================================================== *)

(* Neither edit ever changes a node's id — which is what lets a reorder, which addresses children
   BY id, be blind to an insert that happened inside them. *)
let tid_ins (p:string) (n:tree) (t:tree)
  : Lemma (ensures tid_of (ins p n t) == tid_of t) [SMTPat (tid_of (ins p n t))]
  = match t with TNode _ _ _ -> ()

let tid_reorder (p:string) (order:list string) (t:tree)
  : Lemma (ensures tid_of (reorder_at p order t) == tid_of t)
          [SMTPat (tid_of (reorder_at p order t))]
  = match t with TNode _ _ _ -> ()

(* An edit whose anchor is not in the tree is the identity on it. *)
let rec ins_absent (p:string) (n:tree) (t:tree)
  : Lemma (requires not (mem p (ids t))) (ensures ins p n t == t) (decreases t)
  = match t with TNode _ _ cs -> ins_all_absent p n cs
and ins_all_absent (p:string) (n:tree) (ts:list tree)
  : Lemma (requires not (mem p (ids_all ts))) (ensures ins_all p n ts == ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> ins_absent p n t; ins_all_absent p n r

let rec reorder_absent (p:string) (order:list string) (t:tree)
  : Lemma (requires not (mem p (ids t))) (ensures reorder_at p order t == t) (decreases t)
  = match t with TNode _ _ cs -> reorder_all_absent p order cs
and reorder_all_absent (p:string) (order:list string) (ts:list tree)
  : Lemma (requires not (mem p (ids_all ts))) (ensures reorder_all p order ts == ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> reorder_absent p order t; reorder_all_absent p order r

(* Both edits are per-element maps over a child list, so both distribute over `app` — which is how
   the appended subtree at an insert's own parent is peeled off. *)
let rec ins_all_app (p:string) (n:tree) (xs ys:list tree)
  : Lemma (ensures ins_all p n (app xs ys) == app (ins_all p n xs) (ins_all p n ys)) (decreases xs)
  = match xs with
    | [] -> ()
    | _ :: r -> ins_all_app p n r ys

let rec reorder_all_app (p:string) (order:list string) (xs ys:list tree)
  : Lemma (ensures reorder_all p order (app xs ys) ==
                   app (reorder_all p order xs) (reorder_all p order ys)) (decreases xs)
  = match xs with
    | [] -> ()
    | _ :: r -> reorder_all_app p order r ys

(* `arrange` addresses children by id and both edits preserve ids, so a lookup through an edited
   list is the edit of the lookup through the original. *)
let rec pick_last_ins (x p:string) (n:tree) (ts:list tree)
  : Lemma (ensures pick_last x (ins_all p n ts) ==
                   (match pick_last x ts with None -> None | Some c -> Some (ins p n c)))
          (decreases ts)
  = match ts with
    | [] -> ()
    | _ :: r -> pick_last_ins x p n r

let rec pick_last_reorder (x p:string) (order:list string) (ts:list tree)
  : Lemma (ensures pick_last x (reorder_all p order ts) ==
                   (match pick_last x ts with None -> None | Some c -> Some (reorder_at p order c)))
          (decreases ts)
  = match ts with
    | [] -> ()
    | _ :: r -> pick_last_reorder x p order r

(* … hence a reorder of a parent's children and an edit INSIDE those children commute. This is
   the lemma the whole reorder side rests on: stating an order by naming ids is stable under any
   edit that leaves the ids alone. *)
let rec arrange_ins (order:list string) (p:string) (n:tree) (ts:list tree)
  : Lemma (ensures arrange order (ins_all p n ts) == ins_all p n (arrange order ts))
          (decreases order)
  = match order with
    | [] -> ()
    | x :: rest -> pick_last_ins x p n ts; arrange_ins rest p n ts

let rec arrange_reorder (ord:list string) (p:string) (order:list string) (ts:list tree)
  : Lemma (ensures arrange ord (reorder_all p order ts) == reorder_all p order (arrange ord ts))
          (decreases ord)
  = match ord with
    | [] -> ()
    | x :: rest -> pick_last_reorder x p order ts; arrange_reorder rest p order ts

(* ---- COMMUTATION 1 of 3: two inserts under different parents. ----
   The two side conditions are exactly what `Ops.independent` buys: the parent id of each insert
   is in its own `Reads`, and the other insert's whole subtree is its `ContentWrites`, so
   content-vs-read disjointness says neither subtree carries the other's anchor. Without that an
   insert could land INSIDE the subtree the other one just added, in one order only. *)
let rec ins_ins_comm (p1:string) (n1:tree) (p2:string) (n2:tree) (t:tree)
  : Lemma (requires p1 <> p2 /\ not (mem p1 (ids n2)) /\ not (mem p2 (ids n1)))
          (ensures ins p2 n2 (ins p1 n1 t) == ins p1 n1 (ins p2 n2 t))
          (decreases t)
  = match t with
    | TNode i _ cs ->
      ins_ins_comm_all p1 n1 p2 n2 cs;
      if i = p1 then (ins_all_app p2 n2 (ins_all p1 n1 cs) [n1]; ins_absent p2 n2 n1)
      else if i = p2 then (ins_all_app p1 n1 (ins_all p2 n2 cs) [n2]; ins_absent p1 n1 n2)
      else ()
and ins_ins_comm_all (p1:string) (n1:tree) (p2:string) (n2:tree) (ts:list tree)
  : Lemma (requires p1 <> p2 /\ not (mem p1 (ids n2)) /\ not (mem p2 (ids n1)))
          (ensures ins_all p2 n2 (ins_all p1 n1 ts) == ins_all p1 n1 (ins_all p2 n2 ts))
          (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> ins_ins_comm p1 n1 p2 n2 t; ins_ins_comm_all p1 n1 p2 n2 r

(* ---- COMMUTATION 2 of 3: an insert and a reorder of a different parent. ----
   `not (mem p2 (ids n1))` is again content-vs-read disjointness: the reorder's parent is in its
   `Reads`, the inserted subtree is the insert's `ContentWrites`. *)
let rec ins_reorder_comm (p1:string) (n1:tree) (p2:string) (order:list string) (t:tree)
  : Lemma (requires p1 <> p2 /\ not (mem p2 (ids n1)))
          (ensures reorder_at p2 order (ins p1 n1 t) == ins p1 n1 (reorder_at p2 order t))
          (decreases t)
  = match t with
    | TNode i _ cs ->
      ins_reorder_comm_all p1 n1 p2 order cs;
      if i = p1 then
        (reorder_all_app p2 order (ins_all p1 n1 cs) [n1]; reorder_absent p2 order n1)
      else if i = p2 then
        arrange_ins order p1 n1 (reorder_all p2 order cs)
      else ()
and ins_reorder_comm_all (p1:string) (n1:tree) (p2:string) (order:list string) (ts:list tree)
  : Lemma (requires p1 <> p2 /\ not (mem p2 (ids n1)))
          (ensures reorder_all p2 order (ins_all p1 n1 ts) ==
                   ins_all p1 n1 (reorder_all p2 order ts))
          (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> ins_reorder_comm p1 n1 p2 order t; ins_reorder_comm_all p1 n1 p2 order r

(* ---- COMMUTATION 3 of 3: two reorders of different parents. ----
   The ONLY side condition is that the parents differ — which is `Ops.independent`'s shared-named-
   structural-parent clause. A reorder writes no content at all, so nothing else can interfere. *)
let rec reorder_reorder_comm (p1:string) (o1:list string) (p2:string) (o2:list string) (t:tree)
  : Lemma (requires p1 <> p2)
          (ensures reorder_at p2 o2 (reorder_at p1 o1 t) == reorder_at p1 o1 (reorder_at p2 o2 t))
          (decreases t)
  = match t with
    | TNode i _ cs ->
      reorder_reorder_comm_all p1 o1 p2 o2 cs;
      if i = p1 then arrange_reorder o1 p2 o2 (reorder_all p1 o1 cs)
      else if i = p2 then arrange_reorder o2 p1 o1 (reorder_all p2 o2 cs)
      else ()
and reorder_reorder_comm_all (p1:string) (o1:list string) (p2:string) (o2:list string)
                             (ts:list tree)
  : Lemma (requires p1 <> p2)
          (ensures reorder_all p2 o2 (reorder_all p1 o1 ts) ==
                   reorder_all p1 o1 (reorder_all p2 o2 ts))
          (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> reorder_reorder_comm p1 o1 p2 o2 t; reorder_reorder_comm_all p1 o1 p2 o2 r

(* ======================================================================================
   10. Lookups, and why they need well-formedness.

       `Tree.tryFind` returns the FIRST preorder match, so what it returns is a fact about the
       tree's ORDER as well as its contents. A reorder moves preorder positions. Under id
       uniqueness that cannot matter — there is only one node to find — and this section is the
       machinery that says so.
   ====================================================================================== *)

let rec no_dups (l:list string) : Tot bool =
  match l with
  | [] -> true
  | x :: r -> not (mem x r) && no_dups r

(* ---- what `Ops.firstDuplicateId` DECIDES (Phase 138) ----

   The validator is a scan and the invariant is a pair of set facts, so nothing downstream can use
   the one until it is expressed as the other. `scan_dup seen l` finds nothing exactly when `l` has
   no repeat and shares nothing with `seen`; at `seen = ids t` that is precisely the hypothesis
   `ins_wf` (section 13) asks for. This lemma is the whole bridge between Phase 137's code and
   Phase 133's specification, which is why it is stated as an `<==>` rather than as the one
   direction the preservation argument happens to need: the converse is what makes the refusal
   sharp — an insert this validator refuses is one that genuinely would have broken the invariant,
   never merely one it could not prove safe. *)
let rec scan_dup_none_iff (seen l:list string)
  : Lemma (ensures (None? (scan_dup seen l)) <==> (no_dups l /\ disjoint l seen)) (decreases l)
  = inter_nil_iff l seen;
    match l with
    | [] -> ()
    | i :: rest ->
      if mem i seen then ()
      else begin
        scan_dup_none_iff (i :: seen) rest;
        inter_nil_iff rest (i :: seen);
        inter_nil_iff rest seen
      end

let first_dup_none_iff (n:tree) (t:tree)
  : Lemma (ensures (None? (first_dup n t)) <==> (no_dups (ids n) /\ disjoint (ids n) (ids t)))
  = scan_dup_none_iff (ids t) (ids n)

(* ---- and that `wf` IS `no_dups (ids _)` (Phase 138) ----

   Section 1 says the structural predicate and the flat one are the same predicate; until now that
   was a sentence in a comment. The validator decides the FLAT one and every preservation argument
   in this module is stated over the STRUCTURAL one, so the sentence became load-bearing the moment
   Phase 137's check was modelled, and it is proved here rather than believed. *)
let rec no_dups_app (x y:list string)
  : Lemma (ensures no_dups (app x y) <==> (no_dups x /\ no_dups y /\ disjoint x y)) (decreases x)
  = inter_nil_iff x y;
    match x with
    | [] -> ()
    | h :: t ->
      no_dups_app t y;
      inter_nil_iff t y;
      mem_app h t y

let rec wf_iff_no_dups (t:tree)
  : Lemma (ensures wf t <==> no_dups (ids t)) (decreases t)
  = match t with
    | TNode _ _ cs -> wf_all_iff_no_dups cs
and wf_all_iff_no_dups (ts:list tree)
  : Lemma (ensures wf_all ts <==> no_dups (ids_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      wf_iff_no_dups t;
      wf_all_iff_no_dups r;
      no_dups_app (ids t) (ids_all r)

(* An id is in a child list's ids exactly when it is in one of the children's. *)
let rec mem_ids_all_intro (x:string) (c:tree) (ts:list tree)
  : Lemma (requires mem c ts /\ mem x (ids c)) (ensures mem x (ids_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> if t = c then () else mem_ids_all_intro x c r

(* `tryFind` succeeds exactly where `exists` says it should (F#: `Tree.exists` IS
   `tryFind |> Option.isSome`). *)
let rec find_in_some_iff (x:string) (t:tree)
  : Lemma (ensures Some? (find_in x t) == mem x (ids t)) (decreases t)
  = match t with
    | TNode i _ cs -> if i = x then () else find_all_some_iff x cs
and find_all_some_iff (x:string) (ts:list tree)
  : Lemma (ensures Some? (find_all x ts) == mem x (ids_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> find_in_some_iff x t; find_all_some_iff x r

(* The child subtree the preorder walk descends into — `find_all` factored into "which child"
   and "where inside it". *)
let rec locate (x:string) (ts:list tree) : Tot (option tree) =
  match ts with
  | [] -> None
  | t :: r -> if mem x (ids t) then Some t else locate x r

let rec find_all_locate (x:string) (ts:list tree)
  : Lemma (ensures find_all x ts == (match locate x ts with
                                     | None -> None
                                     | Some c -> find_in x c)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> find_in_some_iff x t; find_all_locate x r

let rec locate_mem (x:string) (ts:list tree)
  : Lemma (ensures (match locate x ts with
                    | None -> forall (c:tree). mem c ts ==> not (mem x (ids c))
                    | Some c -> mem c ts /\ mem x (ids c))) (decreases ts)
  = match ts with
    | [] -> ()
    | _ :: r -> locate_mem x r

(* Under a uniqueness hypothesis the answer does not depend on the order the list is in — which
   is the whole reason a reorder is invisible to a lookup elsewhere in the tree. *)
let rec locate_unique (x:string) (c:tree) (ts:list tree)
  : Lemma (requires mem c ts /\ mem x (ids c) /\
                    (forall (d:tree). mem d ts ==> mem x (ids d) ==> d == c))
          (ensures locate x ts == Some c) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> if mem x (ids t) then () else locate_unique x c r

let locate_order_free (x:string) (l1 l2:list tree)
  : Lemma (requires (forall (c:tree). mem c l1 == mem c l2) /\
                    (forall (c d:tree). mem c l1 ==> mem d l1 ==>
                                        mem x (ids c) ==> mem x (ids d) ==> c == d))
          (ensures locate x l1 == locate x l2)
  = locate_mem x l1; locate_mem x l2;
    match locate x l1 with
    | None -> (match locate x l2 with
               | None -> ()
               | Some c -> ())
    | Some c -> locate_unique x c l2

let find_all_order_free (x:string) (l1 l2:list tree)
  : Lemma (requires (forall (c:tree). mem c l1 == mem c l2) /\
                    (forall (c d:tree). mem c l1 ==> mem d l1 ==>
                                        mem x (ids c) ==> mem x (ids d) ==> c == d))
          (ensures find_all x l1 == find_all x l2)
  = find_all_locate x l1; find_all_locate x l2; locate_order_free x l1 l2

(* Well-formedness delivers that uniqueness hypothesis: sibling subtrees share no id. *)
let rec wf_all_holder_unique (x:string) (ts:list tree)
  : Lemma (requires wf_all ts)
          (ensures forall (c d:tree). mem c ts ==> mem d ts ==>
                                      mem x (ids c) ==> mem x (ids d) ==> c == d)
          (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      wf_all_holder_unique x r;
      let aux (c d:tree)
        : Lemma (mem c ts ==> mem d ts ==> mem x (ids c) ==> mem x (ids d) ==> c == d)
        = if mem c ts && mem d ts && mem x (ids c) && mem x (ids d) then begin
            if c = t && d = t then ()
            else if c = t then mem_ids_all_intro x d r
            else if d = t then mem_ids_all_intro x c r
            else ()
          end
          else ()
      in
      FStar.Classical.forall_intro_2 aux

(* … and that a well-formed parent's children carry distinct ids. *)
let rec kid_ids_sub (x:string) (ts:list tree)
  : Lemma (requires mem x (kid_ids ts)) (ensures mem x (ids_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> (match t with TNode i _ _ -> if i = x then () else kid_ids_sub x r)

let rec wf_all_kid_ids_no_dups (ts:list tree)
  : Lemma (requires wf_all ts) (ensures no_dups (kid_ids ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      wf_all_kid_ids_no_dups r;
      if mem (tid_of t) (kid_ids r) then (kid_ids_sub (tid_of t) r;
                                          (match t with TNode i _ _ -> ()))
      else ()

(* ======================================================================================
   11. What a lookup sees through an edit.

       The validation half of the diamond: each op must still be ACCEPTED after the other has
       run. For an insert that is membership only (`Tree.exists`), and membership is blind to
       order. For a reorder it is `Tree.tryFind`, and this is where the work is.
   ====================================================================================== *)

(* ---- the permutation test, at membership ---- *)

let rec remove_first_mem (x:string) (l l':list string)
  : Lemma (requires remove_first x l == Some l')
          (ensures forall (z:string). mem z l == (z = x || mem z l')) (decreases l)
  = match l with
    | [] -> ()
    | h :: t -> if h = x then ()
                else (match remove_first x t with
                      | None -> ()
                      | Some t' -> remove_first_mem x t t')

let rec same_multiset_mem (xs ys:list string)
  : Lemma (requires same_multiset xs ys)
          (ensures forall (z:string). mem z xs == mem z ys) (decreases xs)
  = match xs with
    | [] -> ()
    | x :: r -> (match remove_first x ys with
                 | None -> ()
                 | Some ys' -> remove_first_mem x ys ys'; same_multiset_mem r ys')

(* ---- `arrange` keeps exactly the children it was given ---- *)

let rec kid_ids_of_mem (c:tree) (ts:list tree)
  : Lemma (requires mem c ts) (ensures mem (tid_of c) (kid_ids ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> if t = c then () else kid_ids_of_mem c r

let rec pick_last_mem (x:string) (ts:list tree) (c:tree)
  : Lemma (requires pick_last x ts == Some c) (ensures mem c ts /\ tid_of c == x) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> (match pick_last x r with
                 | Some n -> pick_last_mem x r c
                 | None -> ())

let rec pick_last_unique (c:tree) (ts:list tree)
  : Lemma (requires mem c ts /\ no_dups (kid_ids ts))
          (ensures pick_last (tid_of c) ts == Some c) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      if t = c then
        (match pick_last (tid_of c) r with
         | Some d -> pick_last_mem (tid_of c) r d; kid_ids_of_mem d r
         | None -> ())
      else pick_last_unique c r

let rec arrange_from (order:list string) (ts:list tree) (c:tree)
  : Lemma (requires mem c (arrange order ts)) (ensures mem c ts) (decreases order)
  = match order with
    | [] -> ()
    | x :: rest ->
      (match pick_last x ts with
       | Some n -> if n = c then pick_last_mem x ts n else arrange_from rest ts c
       | None -> arrange_from rest ts c)

let rec arrange_contains (order:list string) (ts:list tree) (c:tree)
  : Lemma (requires mem (tid_of c) order /\ pick_last (tid_of c) ts == Some c)
          (ensures mem c (arrange order ts)) (decreases order)
  = match order with
    | [] -> ()
    | x :: rest -> if x = tid_of c then () else arrange_contains rest ts c

(* The element set is preserved: nothing invented (`arrange_from`) and nothing dropped
   (`same_multiset` says every child id is named, and `no_dups` says the name resolves to it). *)
let arrange_elems (order:list string) (ts:list tree)
  : Lemma (requires same_multiset (kid_ids ts) order /\ no_dups (kid_ids ts))
          (ensures forall (c:tree). mem c ts == mem c (arrange order ts))
  = same_multiset_mem (kid_ids ts) order;
    let aux (c:tree) : Lemma (mem c ts == mem c (arrange order ts)) =
      if mem c ts then (kid_ids_of_mem c ts; pick_last_unique c ts; arrange_contains order ts c)
      else (if mem c (arrange order ts) then arrange_from order ts c else ())
    in
    FStar.Classical.forall_intro aux

(* ---- the ids a lookup answer can carry ---- *)

let rec find_in_sub (x y:string) (t:tree) (n:tree)
  : Lemma (requires find_in x t == Some n /\ mem y (ids n)) (ensures mem y (ids t)) (decreases t)
  = match t with
    | TNode i _ cs -> if i = x then () else find_all_sub x y cs n
and find_all_sub (x y:string) (ts:list tree) (n:tree)
  : Lemma (requires find_all x ts == Some n /\ mem y (ids n)) (ensures mem y (ids_all ts))
          (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> (match find_in x t with
                 | Some m -> find_in_sub x y t n
                 | None -> find_all_sub x y r n)

let rec find_all_app (x:string) (l m:list tree)
  : Lemma (ensures find_all x (app l m) == (match find_all x l with
                                            | Some n -> Some n
                                            | None -> find_all x m)) (decreases l)
  = match l with
    | [] -> ()
    | t :: r -> (match find_in x t with Some _ -> () | None -> find_all_app x r m)

(* ---- a lookup through an INSERT: membership only, so no order question arises ---- *)

let rec find_ins (x p:string) (n:tree) (t:tree)
  : Lemma (requires not (mem x (ids n)))
          (ensures find_in x (ins p n t) == (match find_in x t with
                                             | None -> None
                                             | Some m -> Some (ins p n m))) (decreases t)
  = match t with
    | TNode i _ cs ->
      if i = x then ()
      else begin
        find_ins_all x p n cs;
        if i = p then (find_all_app x (ins_all p n cs) [n]; find_in_some_iff x n) else ()
      end
and find_ins_all (x p:string) (n:tree) (ts:list tree)
  : Lemma (requires not (mem x (ids n)))
          (ensures find_all x (ins_all p n ts) == (match find_all x ts with
                                                   | None -> None
                                                   | Some m -> Some (ins p n m))) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> find_ins x p n t; find_ins_all x p n r

(* ---- the reorder own validation, at every node rather than at the first ----
   `validateReorder` checks the FIRST node carrying the parent id. Under well-formedness that is
   the only one, so the check holds everywhere — which is what the induction below needs, since
   it meets the parent id wherever it happens to sit. *)

let rec reorder_ok (p:string) (o:list string) (t:tree) : Tot bool (decreases t) =
  match t with
  | TNode i _ cs -> (if i = p then same_multiset (kid_ids cs) o else true) && reorder_ok_all p o cs
and reorder_ok_all (p:string) (o:list string) (ts:list tree) : Tot bool (decreases ts) =
  match ts with
  | [] -> true
  | t :: r -> reorder_ok p o t && reorder_ok_all p o r

let rec absent_reorder_ok (p:string) (o:list string) (t:tree)
  : Lemma (requires not (mem p (ids t))) (ensures reorder_ok p o t) (decreases t)
  = match t with TNode _ _ cs -> absent_reorder_ok_all p o cs
and absent_reorder_ok_all (p:string) (o:list string) (ts:list tree)
  : Lemma (requires not (mem p (ids_all ts))) (ensures reorder_ok_all p o ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> absent_reorder_ok p o t; absent_reorder_ok_all p o r

let rec wf_reorder_ok (p:string) (o:list string) (t:tree)
  : Lemma (requires wf t /\ (match find_in p t with
                             | None -> True
                             | Some n -> same_multiset (kid_ids (kids_of n)) o))
          (ensures reorder_ok p o t) (decreases t)
  = match t with
    | TNode i _ cs ->
      if i = p then (find_all_some_iff p cs; absent_reorder_ok_all p o cs)
      else wf_reorder_ok_all p o cs
and wf_reorder_ok_all (p:string) (o:list string) (ts:list tree)
  : Lemma (requires wf_all ts /\ (match find_all p ts with
                                  | None -> True
                                  | Some n -> same_multiset (kid_ids (kids_of n)) o))
          (ensures reorder_ok_all p o ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      (match find_in p t with
       | Some n ->
         wf_reorder_ok p o t;
         find_in_some_iff p t;
         find_all_some_iff p r;
         wf_reorder_ok_all p o r
       | None -> wf_reorder_ok p o t; wf_reorder_ok_all p o r)

(* ---- a lookup through a REORDER. Under well-formedness the parent id does not occur below its
   own node, so `reorder_all` is the identity on its children and `arrange` is applied to the
   ORIGINAL child list — which is the list whose ids are known distinct. ---- *)

let rec find_reorder (x p:string) (o:list string) (t:tree)
  : Lemma (requires wf t /\ reorder_ok p o t)
          (ensures find_in x (reorder_at p o t) == (match find_in x t with
                                                    | None -> None
                                                    | Some m -> Some (reorder_at p o m)))
          (decreases t)
  = match t with
    | TNode i _ cs ->
      if i = p then begin
        reorder_all_absent p o cs;
        if i = x then ()
        else begin
          wf_all_kid_ids_no_dups cs;
          arrange_elems o cs;
          wf_all_holder_unique x cs;
          find_all_order_free x (arrange o cs) cs;
          (match find_all x cs with
           | None -> ()
           | Some m ->
             (* the answer sits inside `cs`, where `wf` says the parent id does not occur — so
                the reorder is the identity on it *)
             (if mem p (ids m) then find_all_sub x p cs m else ());
             reorder_absent p o m)
        end
      end
      else (if i = x then () else find_reorder_all x p o cs)
and find_reorder_all (x p:string) (o:list string) (ts:list tree)
  : Lemma (requires wf_all ts /\ reorder_ok_all p o ts)
          (ensures find_all x (reorder_all p o ts) == (match find_all x ts with
                                                       | None -> None
                                                       | Some m -> Some (reorder_at p o m)))
          (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> find_reorder x p o t; find_reorder_all x p o r

(* ---- and the id sets each edit leaves behind ---- *)

let rec kid_ids_ins_all (p:string) (n:tree) (ts:list tree)
  : Lemma (ensures kid_ids (ins_all p n ts) == kid_ids ts) (decreases ts)
  = match ts with
    | [] -> ()
    | _ :: r -> kid_ids_ins_all p n r

let rec kid_ids_reorder_all (p:string) (o:list string) (ts:list tree)
  : Lemma (ensures kid_ids (reorder_all p o ts) == kid_ids ts) (decreases ts)
  = match ts with
    | [] -> ()
    | _ :: r -> kid_ids_reorder_all p o r

let rec ids_all_app (xs ys:list tree)
  : Lemma (ensures ids_all (app xs ys) == app (ids_all xs) (ids_all ys)) (decreases xs)
  = match xs with
    | [] -> ()
    | _ :: r -> ids_all_app r ys

let rec ids_ins_mem (x p:string) (n:tree) (t:tree)
  : Lemma (ensures mem x (ids (ins p n t)) ==
                   (mem x (ids t) || (mem p (ids t) && mem x (ids n)))) (decreases t)
  = match t with
    | TNode i _ cs ->
      ids_ins_mem_all x p n cs;
      if i = p then ids_all_app (ins_all p n cs) [n] else ()
and ids_ins_mem_all (x p:string) (n:tree) (ts:list tree)
  : Lemma (ensures mem x (ids_all (ins_all p n ts)) ==
                   (mem x (ids_all ts) || (mem p (ids_all ts) && mem x (ids n)))) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> ids_ins_mem x p n t; ids_ins_mem_all x p n r

let rec ids_all_sub (x:string) (l1 l2:list tree)
  : Lemma (requires (forall (c:tree). mem c l1 ==> mem c l2) /\ mem x (ids_all l1))
          (ensures mem x (ids_all l2)) (decreases l1)
  = match l1 with
    | [] -> ()
    | t :: r -> if mem x (ids t) then mem_ids_all_intro x t l2 else ids_all_sub x r l2

let ids_all_mem_transfer (x:string) (l1 l2:list tree)
  : Lemma (requires (forall (c:tree). mem c l1 == mem c l2))
          (ensures mem x (ids_all l1) == mem x (ids_all l2))
  = (if mem x (ids_all l1) then ids_all_sub x l1 l2 else ());
    (if mem x (ids_all l2) then ids_all_sub x l2 l1 else ())

let rec ids_reorder_mem (x p:string) (o:list string) (t:tree)
  : Lemma (requires wf t /\ reorder_ok p o t)
          (ensures mem x (ids (reorder_at p o t)) == mem x (ids t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      if i = p then begin
        reorder_all_absent p o cs;
        wf_all_kid_ids_no_dups cs;
        arrange_elems o cs;
        ids_all_mem_transfer x (arrange o cs) cs
      end
      else ids_reorder_mem_all x p o cs
and ids_reorder_mem_all (x p:string) (o:list string) (ts:list tree)
  : Lemma (requires wf_all ts /\ reorder_ok_all p o ts)
          (ensures mem x (ids_all (reorder_all p o ts)) == mem x (ids_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> ids_reorder_mem x p o t; ids_reorder_mem_all x p o r

(* The same two facts QUANTIFIED — the shape both the diamond's insert cases (section 12, since
   Phase 138 widened the insert validator into a statement about a whole id SET) and the
   disjointness arguments of section 13 consume. They sat in section 13 until Phase 138 and moved
   here unchanged; nothing but their position is different. *)
let ids_ins_mem_fa (p:string) (n:tree) (t:tree)
  : Lemma (ensures forall (y:string). mem y (ids (ins p n t)) ==
                     (mem y (ids t) || (mem p (ids t) && mem y (ids n))))
  = let aux (y:string)
      : Lemma (mem y (ids (ins p n t)) ==
               (mem y (ids t) || (mem p (ids t) && mem y (ids n))))
      = ids_ins_mem y p n t
    in
    FStar.Classical.forall_intro aux

let ids_ins_mem_all_fa (p:string) (n:tree) (ts:list tree)
  : Lemma (ensures forall (y:string). mem y (ids_all (ins_all p n ts)) ==
                     (mem y (ids_all ts) || (mem p (ids_all ts) && mem y (ids n))))
  = let aux (y:string)
      : Lemma (mem y (ids_all (ins_all p n ts)) ==
               (mem y (ids_all ts) || (mem p (ids_all ts) && mem y (ids n))))
      = ids_ins_mem_all y p n ts
    in
    FStar.Classical.forall_intro aux

let ids_reorder_mem_fa (p:string) (o:list string) (t:tree)
  : Lemma (requires wf t /\ reorder_ok p o t)
          (ensures forall (y:string). mem y (ids (reorder_at p o t)) == mem y (ids t))
  = let aux (y:string) : Lemma (mem y (ids (reorder_at p o t)) == mem y (ids t))
      = ids_reorder_mem y p o t
    in
    FStar.Classical.forall_intro aux

let ids_reorder_mem_all_fa (p:string) (o:list string) (ts:list tree)
  : Lemma (requires wf_all ts /\ reorder_ok_all p o ts)
          (ensures forall (y:string). mem y (ids_all (reorder_all p o ts)) == mem y (ids_all ts))
  = let aux (y:string) : Lemma (mem y (ids_all (reorder_all p o ts)) == mem y (ids_all ts))
      = ids_reorder_mem_all y p o ts
    in
    FStar.Classical.forall_intro aux

(* ======================================================================================
   12. The diamond at one state, for one pair.

       `DagFold.diamond` quantified at a single state, with well-formedness as the standing
       hypothesis. Six shapes reach this section — insert/insert, insert/reorder, reorder/reorder
       and their swaps — because section 8 has already retired the nine that carry a relocating
       op, and `wstep_sym` retires the swaps.
   ====================================================================================== *)

let wstep (a b:op) (s:tree) : prop =
  wf s ==> Ok? (apply a s) ==> Ok? (apply b s) ==>
  (Ok? (bind (apply a s) (apply b)) /\
   bind (apply a s) (apply b) == bind (apply b s) (apply a))

(* The conclusion is an equation between the two orders and an `Ok?` on one side of it, so it
   already carries the swapped statement. *)
let wstep_sym (a b:op) (s:tree)
  : Lemma (requires wstep a b s) (ensures wstep b a s) = ()

(* An inert op is the identity, so anything commutes with it. This is the whole of the nine
   relocating pairs once `relocating_forces_inert` has fired. *)
let inert_wstep_right (a b:op) (s:tree)
  : Lemma (requires inert b) (ensures wstep a b s)
  = inert_is_identity b s;
    (match apply a s with
     | Ok sa -> inert_is_identity b sa
     | Error _ -> ())

let inert_wstep_left (a b:op) (s:tree)
  : Lemma (requires inert a) (ensures wstep a b s)
  = inert_is_identity a s;
    (match apply b s with
     | Ok sb -> inert_is_identity a sb
     | Error _ -> ())

(* ---- small facts the three cases below share ---- *)

let tid_in_ids (t:tree)
  : Lemma (ensures mem (tid_of t) (ids t)) [SMTPat (mem (tid_of t) (ids t))]
  = match t with TNode _ _ _ -> ()

let rec find_in_id (x:string) (t:tree) (m:tree)
  : Lemma (requires find_in x t == Some m) (ensures tid_of m == x) (decreases t)
  = match t with
    | TNode i _ cs -> if i = x then () else find_all_id x cs m
and find_all_id (x:string) (ts:list tree) (m:tree)
  : Lemma (requires find_all x ts == Some m) (ensures tid_of m == x) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> (match find_in x t with
                 | Some q -> find_in_id x t m
                 | None -> find_all_id x r m)

(* ---- CASE 1: two inserts ---- *)

let ins_ins_step (p1:string) (n1:tree) (p2:string) (n2:tree) (s:tree)
  : Lemma (requires independent (op_fp (InsertChild p1 n1)) (op_fp (InsertChild p2 n2)))
          (ensures wstep (InsertChild p1 n1) (InsertChild p2 n2) s)
  = inter_nil_iff (ids n1) (p2 :: ids n2);
    inter_nil_iff (ids n2) (p1 :: ids n1);
    inter_nil_iff [p1] [p2];
    ids_ins_mem (tid_of n2) p1 n1 s;
    ids_ins_mem p2 p1 n1 s;
    ids_ins_mem (tid_of n1) p2 n2 s;
    ids_ins_mem p1 p2 n2 s;
    (* Phase 138: acceptance is now a statement about the whole inserted id SET rather than about
       one id, so each side's still-accepted-after-the-other step needs the set facts, not the two
       point instances above. Independence supplies `ids n1 ∩ ids n2 = ∅` (each subtree's ids are
       in the other's `reads`), and an insert adds exactly its own ids — so neither subtree's ids
       can appear in the tree the other insert left behind, and internal uniqueness is a property
       of the subtree alone and is untouched by either. *)
    first_dup_none_iff n1 s;
    first_dup_none_iff n2 s;
    first_dup_none_iff n2 (ins p1 n1 s);
    first_dup_none_iff n1 (ins p2 n2 s);
    ids_ins_mem_fa p1 n1 s;
    ids_ins_mem_fa p2 n2 s;
    inter_nil_iff (ids n1) (ids s);
    inter_nil_iff (ids n2) (ids s);
    inter_nil_iff (ids n1) (ids n2);
    inter_nil_iff (ids n2) (ids (ins p1 n1 s));
    inter_nil_iff (ids n1) (ids (ins p2 n2 s));
    ins_ins_comm p1 n1 p2 n2 s

(* ---- CASE 2: an insert and a reorder ---- *)

let ins_reorder_step (p1:string) (n1:tree) (p2:string) (o2:list string) (s:tree)
  : Lemma (requires independent (op_fp (InsertChild p1 n1)) (op_fp (ReorderChildren p2 o2)))
          (ensures wstep (InsertChild p1 n1) (ReorderChildren p2 o2) s)
  = inter_nil_iff (ids n1) (p2 :: o2);
    inter_nil_iff [p1] [p2];
    if wf s && Ok? (apply (InsertChild p1 n1) s) && Ok? (apply (ReorderChildren p2 o2) s) then
      begin
        (* the reorder is still accepted after the insert: the insert cannot have moved the
           parent it names, nor changed that parent's child ids *)
        find_ins p2 p1 n1 s;
        (match find_in p2 s with
         | None -> ()
         | Some m2 ->
           find_in_id p2 s m2;
           (match m2 with TNode _ _ cs2 -> kid_ids_ins_all p1 n1 cs2));
        (* the insert is still accepted after the reorder: its checks are membership of the whole
           inserted subtree (Phase 138) plus the parent's, and a reorder moves no id in or out of
           the tree — so the id SET the scan is against is unchanged, and internal uniqueness of
           the subtree is not a fact about the tree at all *)
        wf_reorder_ok p2 o2 s;
        ids_reorder_mem (tid_of n1) p2 o2 s;
        ids_reorder_mem p1 p2 o2 s;
        first_dup_none_iff n1 s;
        first_dup_none_iff n1 (reorder_at p2 o2 s);
        ids_reorder_mem_fa p2 o2 s;
        inter_nil_iff (ids n1) (ids s);
        inter_nil_iff (ids n1) (ids (reorder_at p2 o2 s));
        ins_reorder_comm p1 n1 p2 o2 s
      end
    else ()

(* ---- CASE 3: two reorders ---- *)

let reorder_reorder_step (p1:string) (o1:list string) (p2:string) (o2:list string) (s:tree)
  : Lemma (requires independent (op_fp (ReorderChildren p1 o1)) (op_fp (ReorderChildren p2 o2)))
          (ensures wstep (ReorderChildren p1 o1) (ReorderChildren p2 o2) s)
  = inter_nil_iff [p1] [p2];
    if wf s && Ok? (apply (ReorderChildren p1 o1) s) && Ok? (apply (ReorderChildren p2 o2) s) then
      begin
        wf_reorder_ok p1 o1 s;
        wf_reorder_ok p2 o2 s;
        find_reorder p2 p1 o1 s;
        find_reorder p1 p2 o2 s;
        (match find_in p2 s with
         | None -> ()
         | Some m2 ->
           find_in_id p2 s m2;
           (match m2 with TNode _ _ cs2 -> kid_ids_reorder_all p1 o1 cs2));
        (match find_in p1 s with
         | None -> ()
         | Some m1 ->
           find_in_id p1 s m1;
           (match m1 with TNode _ _ cs1 -> kid_ids_reorder_all p2 o2 cs1));
        reorder_reorder_comm p1 o1 p2 o2 s
      end
    else ()

(* ---- the leaf diamond: every ordered pair of NON-BATCH ops ---- *)

(* `Ops.independent` is symmetric — every clause is either self-symmetric or paired with its
   mirror — which is why only three of the six remaining shapes need a proof of their own. *)
let disjoint_sym (#a:eqtype) (x y:list a)
  : Lemma (ensures disjoint x y == disjoint y x)
  = inter_nil_iff x y; inter_nil_iff y x

let independent_sym (fa fb:footprint)
  : Lemma (requires independent fa fb) (ensures independent fb fa)
  = disjoint_sym fa.content_writes fb.content_writes;
    disjoint_sym fa.structure_writes fb.structure_writes

let is_leaf (o:op) : Tot bool = match o with Batch _ -> false | _ -> true

let leaf_wstep (a b:op) (s:tree)
  : Lemma (requires is_leaf a /\ is_leaf b /\ independent (op_fp a) (op_fp b))
          (ensures wstep a b s)
  = match a, b with
    (* a relocating op is independent only of an inert one, and no leaf is inert *)
    | RemoveNode _, _ | MoveNode _ _, _ | UpdateNode _, _ ->
      relocating_forces_inert a b; inert_wstep_right a b s
    | _, RemoveNode _ | _, MoveNode _ _ | _, UpdateNode _ ->
      relocating_forces_inert b a; inert_wstep_left a b s
    | InsertChild p1 n1, InsertChild p2 n2 -> ins_ins_step p1 n1 p2 n2 s
    | InsertChild p1 n1, ReorderChildren p2 o2 -> ins_reorder_step p1 n1 p2 o2 s
    | ReorderChildren p2 o2, InsertChild p1 n1 ->
      independent_sym (op_fp a) (op_fp b);
      ins_reorder_step p1 n1 p2 o2 s;
      wstep_sym (InsertChild p1 n1) (ReorderChildren p2 o2) s
    | ReorderChildren p1 o1, ReorderChildren p2 o2 -> reorder_reorder_step p1 o1 p2 o2 s

(* ======================================================================================
   13. Well-formedness under an accepted op — the refutation Phase 133 recorded, and the fix
       Phase 137 landed, kept side by side.

       PHASE 133 FOUND: `Ops.validateInsert` checked the inserted node's OWN id against the tree
       and nothing else, so an inserted SUBTREE carrying a descendant id the tree already held —
       or carrying one twice itself — was accepted and the result had a repeated id. The
       unconditional statement "every accepted op preserves well-formedness" was FALSE of the
       shipped algebra, and 133 proved that rather than asserting it.

       PHASE 137 FIXED IT, and Phase 138 lifted this model to the fixed validator (section 5b).
       The refutation is therefore no longer a claim about the LIVE algebra, and carrying it as
       one would have been false — but DELETING it would throw away the machine-checked record of
       what was wrong, which is the part a reader a year from now needs most. So it is restated
       rather than removed: `validate_insert_pre137` / `apply_pre137` name the OLD clause
       explicitly, `insert_breaks_wf_pre137` is its counterexample unchanged, and
       `cx_insert_refused_now` proves the LIVE model refuses that very insert. The pair reads as
       one sentence — this was admitted, and it is not any more — and the second half is a go-red
       by construction: revert the validator and it stops verifying.

       What was true throughout is the conditional form, `ins_wf`: an insert whose subtree is
       internally id-unique and disjoint from the tree preserves the invariant. That was the
       specification the fix had to meet, and `first_dup_none_iff` (section 10) is the proof that
       what the fix decides IS that condition, neither weaker nor stronger.
   ====================================================================================== *)

(* ---- the pre-137 clause, named, and its counterexample ---- *)

let cx_tree : tree = TNode "root" "doc" [TNode "a" "section" []]

(* an id the inserted node's own id-check cannot see: "root" is a DESCENDANT of "fresh" *)
let cx_insert : op = InsertChild "a" (TNode "fresh" "section" [TNode "root" "para" []])

(* F#, BEFORE Phase 137: `validateInsert`'s first clause read `has_id (tid_of n) t` — the
   inserted node's own id and no descendant of it. Preserved here as a definition rather than as
   prose so the refutation below is a statement about something the module can still evaluate. *)
let validate_insert_pre137 (n:tree) (t:tree) : Tot bool = has_id (tid_of n) t

let apply_pre137 (o:op) (t:tree) : Tot (outcome tree rejection) =
  match o with
  | InsertChild p n ->
    if validate_insert_pre137 n t then Error (DuplicateId (tid_of n))
    else if not (has_id p t) then Error (UnknownNode p (ids t))
    else Ok (ins p n t)
  | _ -> apply o t

(* Phase 133's counterexample, unchanged, now explicitly about the pre-137 algebra. *)
let insert_breaks_wf_pre137 ()
  : Lemma (ensures wf cx_tree /\
                   (match apply_pre137 cx_insert cx_tree with
                    | Ok t' -> not (wf t')
                    | Error _ -> False))
  = assert_norm (wf cx_tree);
    assert_norm (match apply_pre137 cx_insert cx_tree with
                 | Ok t' -> not (wf t')
                 | Error _ -> False)

(* … and the other half of the sentence: the LIVE validator refuses exactly that insert, naming
   the descendant id as the offender. This is the fix machine-checked at the point where it was
   broken, and it goes red if the validator is ever narrowed back. *)
let cx_insert_refused_now ()
  : Lemma (ensures apply cx_insert cx_tree == Error (DuplicateId "root"))
  = assert_norm (apply cx_insert cx_tree == Error (DuplicateId "root"))

(* ---- the conditional form, which is what Phase 137's validation buys ---- *)

let disjoint_via_mem (#a:eqtype) (x y:list a)
  : Lemma (requires forall (z:a). mem z x ==> not (mem z y)) (ensures disjoint x y)
  = inter_nil_iff x y

let rec inter_nil_r (#a:eqtype) (l:list a)
  : Lemma (ensures inter l [] == []) [SMTPat (inter l [])]
  = match l with
    | [] -> ()
    | _ :: t -> inter_nil_r t

let nonempty_sub (#a:eqtype) (x y:list a)
  : Lemma (requires (forall (z:a). mem z x ==> mem z y) /\ not (is_empty x))
          (ensures not (is_empty y))
  = match x with
    | [] -> ()
    | h :: _ -> ()

let rec wf_all_elem (ts:list tree) (c:tree)
  : Lemma (requires wf_all ts /\ mem c ts) (ensures wf c) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> if t = c then () else wf_all_elem r c

let rec wf_all_app (xs ys:list tree)
  : Lemma (requires wf_all xs /\ wf_all ys /\ disjoint (ids_all xs) (ids_all ys))
          (ensures wf_all (app xs ys)) (decreases xs)
  = inter_nil_iff (ids_all xs) (ids_all ys);
    match xs with
    | [] -> ()
    | t :: r ->
      inter_nil_iff (ids_all r) (ids_all ys);
      disjoint_via_mem (ids_all r) (ids_all ys);
      wf_all_app r ys;
      ids_all_app r ys;
      inter_nil_iff (ids t) (ids_all r);
      disjoint_via_mem (ids t) (ids_all (app r ys))

(* (the two membership characterisations of section 11 are quantified at the end of that section
   since Phase 138 — `ids_ins_mem_fa` / `ids_ins_mem_all_fa`, where the diamond can reach them too) *)

let rec ins_wf (p:string) (n:tree) (t:tree)
  : Lemma (requires wf t /\ wf n /\ disjoint (ids n) (ids t))
          (ensures wf (ins p n t)) (decreases t)
  = inter_nil_iff (ids n) (ids t);
    match t with
    | TNode i _ cs ->
      inter_nil_iff (ids n) (ids_all cs);
      disjoint_via_mem (ids n) (ids_all cs);
      if i = p then begin
        (* `wf t` says the parent id does not occur below its own node, so the rebuild leaves the
           existing children alone and the inserted subtree is simply appended *)
        ins_all_absent p n cs;
        ids_all_app cs [n];
        inter_nil_iff (ids_all cs) (ids n);
        disjoint_via_mem (ids_all cs) (ids n);
        wf_all_app cs [n]
      end
      else begin
        ins_wf_all p n cs;
        ids_ins_mem_all_fa p n cs
      end
and ins_wf_all (p:string) (n:tree) (ts:list tree)
  : Lemma (requires wf_all ts /\ wf n /\ disjoint (ids n) (ids_all ts))
          (ensures wf_all (ins_all p n ts)) (decreases ts)
  = inter_nil_iff (ids n) (ids_all ts);
    match ts with
    | [] -> ()
    | t :: r ->
      inter_nil_iff (ids n) (ids t);
      disjoint_via_mem (ids n) (ids t);
      inter_nil_iff (ids n) (ids_all r);
      disjoint_via_mem (ids n) (ids_all r);
      ins_wf p n t;
      ins_wf_all p n r;
      ids_ins_mem_fa p n t;
      ids_ins_mem_all_fa p n r;
      inter_nil_iff (ids t) (ids_all r);
      disjoint_via_mem (ids (ins p n t)) (ids_all (ins_all p n r))

(* ======================================================================================
   14. THE DOMAIN HYPOTHESIS — the three pair CLASSES the leaf argument settles.

       `DagFold.independence_diamond` at this domain, restricted to well-formed states (section
       0's `WHY WELL-FORMEDNESS`):

         - EITHER side relocating (a `RemoveNode` or a `MoveNode` anywhere in it) — nine of the
           fifteen unordered pairs, closed by `relocating_forces_inert` with no tree involved;
         - EITHER side inert — every remaining pair against an empty `Batch`;
         - BOTH sides leaves — insert/insert, insert/reorder and reorder/reorder, the three
           genuinely-commuting cases, plus their swaps.

       WHAT PHASE 133 COULD NOT REACH, AND PHASE 162 DID. A pair in which one side is a NON-inert,
       NON-relocating `Batch` fell outside all three classes, and Phase 133 wrote that boundary
       out as a predicate, `covered`, so the theorem stated its own scope. Lifting the leaf
       diamond along a batch's script needed the well-formedness invariant at each intermediate
       state of the script, which the algebra of the day did not give (section 13's refutation);
       Phase 137 fixed the validator, Phase 138 proved the invariant, and PHASE 162 PERFORMED THE
       LIFT — section 20. `covered` is therefore gone, along with the `covered`-restricted
       statement that lived here, and `tree_independence_diamond` is the unconditional theorem
       stated at the end of that section. This section keeps the three classes because they are
       still what the argument is made of, and because the class analysis is the useful reading
       of it; the fourth class is one induction away and is written there.
   ====================================================================================== *)

let covered_classes (a b:op) : Tot bool =
  (is_leaf a && is_leaf b) || inert a || inert b || relocating a || relocating b

(* The three classes, discharged. Section 20's `tree_independence_diamond` drops the hypothesis by
   supplying the fourth; this is the part of it that needs no induction over a script, kept under
   its own name so a reader can see which half of the theorem each argument carries. *)
let classed_tree_independence_diamond (a b:op) (s:tree)
  : Lemma (requires independent (op_fp a) (op_fp b) /\ covered_classes a b)
          (ensures wstep a b s)
  = if relocating a then (relocating_forces_inert a b; inert_wstep_right a b s)
    else if relocating b then
      (independent_sym (op_fp a) (op_fp b); relocating_forces_inert b a; inert_wstep_left a b s)
    else if inert a then inert_wstep_left a b s
    else if inert b then inert_wstep_right a b s
    else leaf_wstep a b s

(* ======================================================================================
   15. The rest of the invariant: a reorder preserves it outright, and an insert whose result
       is well-formed was a fresh one all along.

       These two are what turn the single-step diamond of section 14 into a statement about a
       WHOLE FOLD: `DagFold.replay_perm` threads the state through a script, so the invariant the
       pair cases rest on has to survive each step. A reorder never touches an id, so it survives
       unconditionally. An insert survives exactly when it was fresh — and `ins_wf_conv` says the
       converse too, so "the result is well-formed" IS "the insert was fresh", which is what lets
       the guard in section 16 stand in for the validation Phase 137 will add.
   ====================================================================================== *)

(* ---- the permutation test again: a no-duplicate list can only match a no-duplicate one ---- *)

let rec remove_first_no_dups (x:string) (l l':list string)
  : Lemma (requires remove_first x l == Some l' /\ no_dups l' /\ not (mem x l'))
          (ensures no_dups l) (decreases l)
  = match l with
    | [] -> ()
    | h :: t -> if h = x then ()
                else (match remove_first x t with
                      | None -> ()
                      | Some t' -> remove_first_no_dups x t t'; remove_first_mem x t t')

let rec same_multiset_no_dups (xs ys:list string)
  : Lemma (requires same_multiset xs ys /\ no_dups xs) (ensures no_dups ys) (decreases xs)
  = match xs with
    | [] -> ()
    | x :: r -> (match remove_first x ys with
                 | None -> ()
                 | Some ys' ->
                   same_multiset_no_dups r ys';
                   same_multiset_mem r ys';
                   remove_first_no_dups x ys ys')

(* ---- `arrange` picks each child at most once, so the rearranged list is still well-formed ---- *)

let rec arrange_tids (ord:list string) (ts:list tree) (d:tree)
  : Lemma (requires mem d (arrange ord ts)) (ensures mem (tid_of d) ord) (decreases ord)
  = match ord with
    | [] -> ()
    | x :: rest ->
      (match pick_last x ts with
       | Some n -> if n = d then pick_last_mem x ts n else arrange_tids rest ts d
       | None -> arrange_tids rest ts d)

let wf_all_unique_all (ts:list tree)
  : Lemma (requires wf_all ts)
          (ensures forall (y:string) (c d:tree).
                     mem c ts ==> mem d ts ==> mem y (ids c) ==> mem y (ids d) ==> c == d)
  = let aux (y:string)
      : Lemma (forall (c d:tree).
                 mem c ts ==> mem d ts ==> mem y (ids c) ==> mem y (ids d) ==> c == d)
      = wf_all_holder_unique y ts
    in
    FStar.Classical.forall_intro aux

let rec no_shared_id (c:tree) (ts:list tree) (l:list tree) (y:string)
  : Lemma (requires wf_all ts /\ mem c ts /\ mem y (ids c) /\
                    (forall (d:tree). mem d l ==> mem d ts /\ ~(d == c)))
          (ensures not (mem y (ids_all l))) (decreases l)
  = match l with
    | [] -> ()
    | d :: r ->
      assert (mem d l);
      assert (mem d ts /\ ~(d == c));
      wf_all_holder_unique y ts;
      assert (not (mem y (ids d)));
      no_shared_id c ts r y;
      assert (ids_all l == app (ids d) (ids_all r))

let no_shared_ids (c:tree) (ts:list tree) (l:list tree)
  : Lemma (requires wf_all ts /\ mem c ts /\
                    (forall (d:tree). mem d l ==> mem d ts /\ ~(d == c)))
          (ensures forall (y:string). mem y (ids c) ==> not (mem y (ids_all l)))
  = let aux (y:string) : Lemma (mem y (ids c) ==> not (mem y (ids_all l))) =
      if mem y (ids c) then no_shared_id c ts l y else ()
    in
    FStar.Classical.forall_intro aux

let rec arrange_wf_all (o:list string) (cs:list tree)
  : Lemma (requires wf_all cs /\ no_dups o) (ensures wf_all (arrange o cs)) (decreases o)
  = match o with
    | [] -> ()
    | x :: rest ->
      arrange_wf_all rest cs;
      (match pick_last x cs with
       | None -> ()
       | Some n ->
         pick_last_mem x cs n;
         wf_all_elem cs n;
         let l = arrange rest cs in
         let aux (d:tree) : Lemma (mem d l ==> (mem d cs /\ ~(d == n)))
           = if mem d l then (arrange_from rest cs d; arrange_tids rest cs d) else ()
         in
         FStar.Classical.forall_intro aux;
         no_shared_ids n cs l;
         disjoint_via_mem (ids n) (ids_all l))

(* (the two membership characterisations of a reorder are quantified at the end of section 11
   since Phase 138 — `ids_reorder_mem_fa` / `ids_reorder_mem_all_fa`) *)

(* ---- a reorder preserves well-formedness, outright ---- *)

let rec reorder_wf (p:string) (o:list string) (t:tree)
  : Lemma (requires wf t /\ reorder_ok p o t) (ensures wf (reorder_at p o t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      if i = p then begin
        reorder_all_absent p o cs;
        wf_all_kid_ids_no_dups cs;
        same_multiset_no_dups (kid_ids cs) o;
        arrange_elems o cs;
        arrange_wf_all o cs;
        ids_all_mem_transfer i (arrange o cs) cs
      end
      else (reorder_wf_all p o cs; ids_reorder_mem_all_fa p o cs)
and reorder_wf_all (p:string) (o:list string) (ts:list tree)
  : Lemma (requires wf_all ts /\ reorder_ok_all p o ts)
          (ensures wf_all (reorder_all p o ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      reorder_wf p o t;
      reorder_wf_all p o r;
      ids_reorder_mem_fa p o t;
      ids_reorder_mem_all_fa p o r;
      inter_nil_iff (ids t) (ids_all r);
      disjoint_via_mem (ids (reorder_at p o t)) (ids_all (reorder_all p o r))

(* ---- and an accepted insert whose RESULT is well-formed was fresh: the converse of `ins_wf`,
   which is what makes "the result is id-unique" an exact stand-in for Phase 137's check ---- *)

let rec wf_all_app_conv (xs ys:list tree)
  : Lemma (requires wf_all (app xs ys))
          (ensures wf_all xs /\ wf_all ys /\ disjoint (ids_all xs) (ids_all ys)) (decreases xs)
  = match xs with
    | [] -> (inter_nil_iff (ids_all ([] <: list tree)) (ids_all ys);
             disjoint_via_mem (ids_all ([] <: list tree)) (ids_all ys))
    | t :: r ->
      wf_all_app_conv r ys;
      ids_all_app r ys;
      inter_nil_iff (ids t) (ids_all (app r ys));
      inter_nil_iff (ids_all r) (ids_all ys);
      disjoint_via_mem (ids t) (ids_all r);
      disjoint_via_mem (ids_all (t :: r)) (ids_all ys)

let rec ins_wf_conv (p:string) (n:tree) (t:tree)
  : Lemma (requires wf (ins p n t) /\ mem p (ids t))
          (ensures wf n /\ disjoint (ids n) (ids t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      ids_ins_mem_all_fa p n cs;
      if i = p then begin
        wf_all_app_conv (ins_all p n cs) [n];
        mem_app n (ins_all p n cs) [n];
        wf_all_elem (app (ins_all p n cs) [n]) n;
        ids_all_app (ins_all p n cs) [n];
        inter_nil_iff (ids_all (ins_all p n cs)) (ids n);
        disjoint_via_mem (ids n) (ids t)
      end
      else begin
        ins_wf_all_conv p n cs;
        inter_nil_iff (ids n) (ids_all cs);
        disjoint_via_mem (ids n) (ids t)
      end
and ins_wf_all_conv (p:string) (n:tree) (ts:list tree)
  : Lemma (requires wf_all (ins_all p n ts) /\ mem p (ids_all ts))
          (ensures wf n /\ disjoint (ids n) (ids_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      ids_ins_mem_fa p n t;
      ids_ins_mem_all_fa p n r;
      inter_nil_iff (ids (ins p n t)) (ids_all (ins_all p n r));
      if mem p (ids t) then begin
        ins_wf_conv p n t;
        inter_nil_iff (ids n) (ids t);
        disjoint_via_mem (ids n) (ids_all (t :: r))
      end
      else begin
        ins_absent p n t;
        ins_wf_all_conv p n r;
        inter_nil_iff (ids n) (ids_all r);
        disjoint_via_mem (ids n) (ids_all (t :: r))
      end

(* ======================================================================================
   16. The algebra the composite theorem is about.

       `wapply` is `Ops.apply` with two guards, and each is a statement about the boundary of the
       claim rather than a change of semantics:

         - it declines at a tree that is not id-unique. Section 0 says why: `Tree.tryFind`
           resolves to the FIRST preorder match, so on such a tree the answer is a fact about the
           order as well as the contents, and a reorder moves the order. Nothing in the estate
           produces such a tree — `Diff.toOps` refuses one outright with `DuplicateIdInTree` —
           and declining is how a total function says "outside the claim".
         - it declines a step whose RESULT is not id-unique. By `ins_wf` and `ins_wf_conv` that is
           EXACTLY the class Phase 137 refuses: an insert whose subtree carries an id the tree
           already holds, or carries one twice. `insert_breaks_wf` is a member of that class, so
           the guard is not vacuous; `wapply_is_apply` is the statement that it is the only
           difference.
   ====================================================================================== *)

let wapply (o:op) (t:tree) : Tot (outcome tree rejection) =
  if not (wf t) then Error (Rejected "state-not-id-unique" "the tree carries an id twice")
  else match apply o t with
       | Ok t' -> if wf t' then Ok t'
                  else Error (Rejected "would-duplicate-an-id"
                                       "the inserted subtree carries an id the tree already holds")
       | Error e -> Error e

(* On a well-formed tree, `wapply` IS `Ops.apply` wherever the result is well-formed — so the
   two differ on exactly the accepted steps that break id uniqueness, and on nothing else. *)
let wapply_is_apply (o:op) (t:tree)
  : Lemma (requires wf t /\ (match apply o t with Ok t' -> wf t' | Error _ -> True))
          (ensures wapply o t == apply o t)
  = ()

(* ---- well-formedness IS preserved by `wapply`, by construction ---- *)

let wapply_preserves_wf (o:op) (t:tree)
  : Lemma (ensures (match wapply o t with Ok t' -> wf t /\ wf t' | Error _ -> True))
  = ()

(* ======================================================================================
   17. THE DOMAIN HYPOTHESIS for the leaf alphabet, discharged.

       `DagFold.independence_diamond` instantiated at the tree algebra over the NON-BATCH
       skeleton ops. A `Batch` is a list of ops written as one op — `Ops.apply` threads it
       exactly as the fold threads a lane — so restricting the op alphabet here removed no
       behaviour from the fold, it only declined to nest one lane inside another.

       THAT RESTRICTION IS GONE (Phase 162): section 20 lifts the diamond along a batch's script
       and `op_independence_diamond` states it over the whole alphabet, which is what
       `Skeleton.fst` now composes. This section is KEPT rather than replaced, because it is the
       direct argument — the one that reads off the three commutation equalities with no
       induction over a script in the way — and a reader checking the widening wants to see both
       statements and the difference between them.
   ====================================================================================== *)

type leaf_op = o:op{is_leaf o}

let leaf_fp (o:leaf_op) : Tot footprint = op_fp o

let leaf_diamond (a b:leaf_op) (s:tree)
  : Lemma (requires independent (leaf_fp a) (leaf_fp b))
          (ensures Ok? (wapply a s) ==> Ok? (wapply b s) ==>
                   (Ok? (bind (wapply a s) (wapply b)) /\
                    bind (wapply a s) (wapply b) == bind (wapply b s) (wapply a)))
  = if wf s && Ok? (apply a s) && Ok? (apply b s) then begin
      leaf_wstep a b s;
      match apply a s, apply b s with
      | Ok sa, Ok sb ->
        if wf sa && wf sb then begin
          (* the two orders reach one tree; all that is left is that it is still id-unique *)
          match a, b with
          | RemoveNode _, _ | MoveNode _ _, _ | UpdateNode _, _ ->
            relocating_forces_inert a b; inert_is_identity b sa; inert_is_identity b s
          | _, RemoveNode _ | _, MoveNode _ _ | _, UpdateNode _ ->
            independent_sym (op_fp a) (op_fp b);
            relocating_forces_inert b a; inert_is_identity a sb; inert_is_identity a s
          | InsertChild p1 n1, InsertChild p2 n2 ->
            inter_nil_iff (ids n1) (ids n2);
            ins_wf_conv p1 n1 s;
            ins_wf_conv p2 n2 s;
            ids_ins_mem_fa p1 n1 s;
            inter_nil_iff (ids n2) (ids s);
            disjoint_via_mem (ids n2) (ids sa);
            ins_wf p2 n2 sa
          | InsertChild p1 n1, ReorderChildren p2 o2 ->
            wf_reorder_ok p2 o2 s;
            ins_wf_conv p1 n1 s;
            ids_reorder_mem_fa p2 o2 s;
            inter_nil_iff (ids n1) (ids s);
            disjoint_via_mem (ids n1) (ids sb);
            ins_wf p1 n1 sb
          | ReorderChildren p1 o1, InsertChild p2 n2 ->
            wf_reorder_ok p1 o1 s;
            ins_wf_conv p2 n2 s;
            ids_reorder_mem_fa p1 o1 s;
            inter_nil_iff (ids n2) (ids s);
            disjoint_via_mem (ids n2) (ids sa);
            ins_wf p2 n2 sa
          | ReorderChildren p1 o1, ReorderChildren p2 o2 ->
            wf_reorder_ok p1 o1 s;
            wf_reorder_ok p2 o2 s;
            reorder_wf p1 o1 s;
            (match apply b sa with
             | Ok r -> wf_reorder_ok p2 o2 sa; reorder_wf p2 o2 sa
             | Error _ -> ())
        end
        else ()
      | _, _ -> ()
    end
    else ()

let leaf_independence_diamond ()
  : Lemma (ensures independence_diamond #leaf_op #tree #rejection leaf_fp wapply)
  = let aux (a b:leaf_op) : Lemma (independent (leaf_fp a) (leaf_fp b) ==> diamond wapply a b) =
      if independent (leaf_fp a) (leaf_fp b) then
        let per_state (s:tree)
          : Lemma (Ok? (wapply a s) ==> Ok? (wapply b s) ==>
                   (Ok? (bind (wapply a s) (wapply b)) /\
                    bind (wapply a s) (wapply b) == bind (wapply b s) (wapply a)))
          = leaf_diamond a b s
        in
        FStar.Classical.forall_intro per_state
      else ()
    in
    FStar.Classical.forall_intro_2 aux

(* ======================================================================================
   18. THE PRECISION CEILING of the footprint record — the pinned unknown-parent clause is
       NECESSARY, not merely conservative (Phase 143).

       Phase 78 pinned `Ops.independent`'s last two clauses as conservative-not-tight, and
       section 8 above measured what that costs: a `RemoveNode` or a `MoveNode` is independent
       only of an op that does nothing. Phase 143 set out to tighten it — a relocation ought to
       commute with a structural write under an unrelated parent — with Phase 138's preservation
       theorem now available as the invariant such an argument needs.

       IT DOES NOT GO THROUGH, and the reason is a property of the RECORD rather than of the
       argument. This section proves that, because a ceiling nobody has proved is one the next
       phase spends its budget rediscovering.

       THE THREE FACTS, at one well-formed tree and three concrete ops:

         1. `relocation_disjoint_diamond` — a `MoveNode` and an `InsertChild` under a parent
            INSIDE the moved subtree DO commute. This is the diamond the tightening was after,
            and it is real: the subtree travels intact, so an edit within it arrives at the same
            place whichever order it is made in. Every clause of `independent` except the pinned
            pair already holds of this pair, so the pinned clause is the only thing refusing it.

         2. `relocation_diamond_fails_for_a_remove` — swap the move for a `RemoveNode` and the
            diamond BREAKS. Both ops apply at the tree; `remove` then `insert` is
            `UnknownNode`, because the insert's parent went with the destroyed subtree, while
            `insert` then `remove` succeeds. This is pinned over-approximation (2) of
            STABILITY.md's "Op-script footprint + independence" biting exactly where that entry
            says it would: a `RemoveNode`'s `ContentWrites` records the target id and not its
            tree-unknown subtree, and it is SOUND only because clause (1) — the one this phase
            proposed to drop — already serialises the pair.

         3. `relocation_footprints_coincide` — and the two ops carry THE SAME FOOTPRINT. A move
            and a batch that removes and then reorders produce byte-identical records across all
            four address sets, because `union_fp` is a union and neither the op's shape nor the
            direction of its structural write survives the fold.

       Together (`relocation_clause_is_necessary`) they say: no predicate over the four address
       sets can free the safe pair without also freeing the fatal one. A single witness refutes a
       universal, and this is that witness — so the tightening needs a footprint that can NAME
       the difference (a fifth address kind carrying the relocation's kind, or a destroyed-subtree
       set the script cannot compute), which is a change to the record and to every host that
       reads it, not a change to a clause.

       `relocation_move_pair_also_fails` adds the second, independent reason. Two moves whose
       destinations sit inside each other's subtrees each apply alone and reject each other with
       `WouldNestUnderSelf`; that pair could not be freed by ANY record, because the obstruction
       is the validation and not the addresses. So the refused set is not one homogeneous class
       waiting on a better footprint — part of it is genuinely dependent.

       WHAT THIS SECTION DOES NOT CLAIM. That the general move-versus-structural-write theorem is
       false: it is not, and fact 1 is an instance of it. What is claimed is that proving it in
       general would buy nothing today, because `Ops.independent` could not consume it — which is
       why this phase does not prove it. `relocating_forces_inert` (section 8) therefore STANDS,
       live and unamended, and `still_refused` restates its reach as the enumeration the pinned
       clause governs.
   ====================================================================================== *)

(* Every clause of `independent` EXCEPT the pinned relocation pair — the predicate the tightening
   would have reduced `independent` to for a relocating op. Named so the witnesses below can say
   "only the pinned clause refuses this" as a checkable statement rather than as prose. *)
let but_for_relocation (a b:footprint) : Tot bool =
  disjoint a.content_writes b.content_writes &&
  disjoint a.content_writes b.reads &&
  disjoint b.content_writes a.reads &&
  disjoint a.structure_writes b.structure_writes

(* ---- the witness: one tree, a move, a remove-shaped batch with the SAME footprint, and a
   structural write under a parent inside the relocated subtree ---- *)

let reloc_tree : tree =
  TNode "root" "doc" [ TNode "x" "sec" [ TNode "p" "sec" [] ]; TNode "q" "sec" [] ]

let reloc_move : op = MoveNode "x" "q"

(* The remove-shaped op. A bare `RemoveNode "x"` would carry no structure write and so would be
   distinguishable from the move; this batch carries one, and reaches the same four sets. *)
let reloc_remove : op = Batch [ RemoveNode "x"; ReorderChildren "q" [] ]

let reloc_insert : op = InsertChild "p" (TNode "n" "para" [])

let reloc_tree_wf () : Lemma (ensures wf reloc_tree) = assert_norm (wf reloc_tree)

(* ---- FACT 1: the diamond the tightening was after — and it holds ---- *)

let relocation_disjoint_diamond ()
  : Lemma (ensures wf reloc_tree /\
                   but_for_relocation (op_fp reloc_move) (op_fp reloc_insert) /\
                   not (independent (op_fp reloc_move) (op_fp reloc_insert)) /\
                   Ok? (apply reloc_move reloc_tree) /\
                   Ok? (apply reloc_insert reloc_tree) /\
                   Ok? (bind (apply reloc_move reloc_tree) (apply reloc_insert)) /\
                   bind (apply reloc_move reloc_tree) (apply reloc_insert) ==
                   bind (apply reloc_insert reloc_tree) (apply reloc_move))
  = assert_norm (wf reloc_tree);
    assert_norm (but_for_relocation (op_fp reloc_move) (op_fp reloc_insert));
    assert_norm (not (independent (op_fp reloc_move) (op_fp reloc_insert)));
    assert_norm (Ok? (apply reloc_move reloc_tree));
    assert_norm (Ok? (apply reloc_insert reloc_tree));
    assert_norm (Ok? (bind (apply reloc_move reloc_tree) (apply reloc_insert)));
    assert_norm (bind (apply reloc_move reloc_tree) (apply reloc_insert) ==
                 bind (apply reloc_insert reloc_tree) (apply reloc_move))

(* ---- FACT 2: the same shape with a REMOVE in it breaks the diamond ---- *)

let relocation_diamond_fails_for_a_remove ()
  : Lemma (ensures but_for_relocation (op_fp reloc_remove) (op_fp reloc_insert) /\
                   Ok? (apply reloc_remove reloc_tree) /\
                   Ok? (apply reloc_insert reloc_tree) /\
                   Error? (bind (apply reloc_remove reloc_tree) (apply reloc_insert)))
  = assert_norm (but_for_relocation (op_fp reloc_remove) (op_fp reloc_insert));
    assert_norm (Ok? (apply reloc_remove reloc_tree));
    assert_norm (Ok? (apply reloc_insert reloc_tree));
    assert_norm (Error? (bind (apply reloc_remove reloc_tree) (apply reloc_insert)))

(* ---- FACT 3: and the footprint cannot tell them apart ---- *)

let relocation_footprints_coincide ()
  : Lemma (ensures op_fp reloc_move == op_fp reloc_remove)
  = assert_norm (op_fp reloc_move == op_fp reloc_remove)

(* ---- THE CEILING: the three facts as one statement ----
   A predicate over footprints alone assigns ONE verdict to `op_fp reloc_move`, which is also
   `op_fp reloc_remove`. Freeing the pair frees both; one of them breaks the diamond. So the
   pinned clause is not a placeholder for a sharper clause over these sets — there is no sharper
   clause over these sets. *)
let relocation_clause_is_necessary ()
  : Lemma (ensures op_fp reloc_move == op_fp reloc_remove /\
                   but_for_relocation (op_fp reloc_move) (op_fp reloc_insert) /\
                   not (independent (op_fp reloc_move) (op_fp reloc_insert)) /\
                   wstep reloc_move reloc_insert reloc_tree /\
                   ~(wstep reloc_remove reloc_insert reloc_tree))
  = relocation_disjoint_diamond ();
    relocation_diamond_fails_for_a_remove ();
    relocation_footprints_coincide ();
    assert_norm (wf reloc_tree)

(* ---- the second, independent reason: part of the refused set is GENUINELY dependent ----
   Two moves whose destinations sit inside each other's subtrees. Each applies alone; after
   either, the other is `WouldNestUnderSelf`. Their footprints satisfy every clause but the
   pinned pair, and no footprint record could rescue them: the obstruction is the cycle check,
   which is a fact about the tree's shape at the moment the second op runs. *)

let cross_tree : tree =
  TNode "root" "doc"
        [ TNode "x" "sec" [ TNode "mp" "sec" [] ]; TNode "y" "sec" [ TNode "np" "sec" [] ] ]

let cross_a : op = MoveNode "x" "np"
let cross_b : op = MoveNode "y" "mp"

let relocation_move_pair_also_fails ()
  : Lemma (ensures wf cross_tree /\
                   but_for_relocation (op_fp cross_a) (op_fp cross_b) /\
                   not (independent (op_fp cross_a) (op_fp cross_b)) /\
                   Ok? (apply cross_a cross_tree) /\
                   Ok? (apply cross_b cross_tree) /\
                   Error? (bind (apply cross_a cross_tree) (apply cross_b)))
  = assert_norm (wf cross_tree);
    assert_norm (but_for_relocation (op_fp cross_a) (op_fp cross_b));
    assert_norm (not (independent (op_fp cross_a) (op_fp cross_b)));
    assert_norm (Ok? (apply cross_a cross_tree));
    assert_norm (Ok? (apply cross_b cross_tree));
    assert_norm (Error? (bind (apply cross_a cross_tree) (apply cross_b)))

(* ---- the pairs the clause still refuses, as one lemma ----

   `relocating_forces_inert` (section 8) reads forward: independence plus a relocation gives an
   inert partner. This is its contrapositive, which is the shape a reader asking "what is still
   refused, exactly?" wants: the refused set is precisely {relocating} x {not inert}, and by
   `structure_free_iff_inert` "not inert" is "writes some structure". Enumerated at the leaf
   alphabet that is remove-or-move against insert, remove, move and reorder — nine of the fifteen
   unordered pairs, unchanged from Phase 133 — of which:

     - remove x insert     REFUSED, and `relocation_diamond_fails_for_a_remove` shows a member
                           that genuinely does not commute;
     - move x move         REFUSED, and `relocation_move_pair_also_fails` shows the same;
     - move x insert       REFUSED, and `relocation_disjoint_diamond` shows a member that DOES
     - move x reorder      commute — these are refused for want of a discriminator the record
                           does not carry, not because they interfere;
     - remove x remove     REFUSED; not examined by this phase, and no claim is made here about
     - remove x move       whether their members commute. Section 8's argument is what refuses
     - remove x reorder    them, and it is unchanged.

   The three genuinely-commuting pairs (insert/insert, insert/reorder, reorder/reorder) are
   section 12's and are unaffected. *)
let still_refused (a b:op)
  : Lemma (requires relocating a /\ not (inert b))
          (ensures not (independent (op_fp a) (op_fp b)))
  = if independent (op_fp a) (op_fp b) then relocating_forces_inert a b else ()

(* ======================================================================================
   19. THE PREORDER-POSITION LEMMA — a parent precedes its children in the walk (Phase 162).

       Phase 141 left `diff_reconstructs` and the operational `diff_applicable` at level 2 naming
       exactly one missing fact: "a parent precedes its children in preorder, so by the time the
       second pass emits a move while processing the destination, every after-ancestor of that
       destination is already placed". This section is that fact, stated over the model's own
       `ids` — which IS `Tree.preorder |> List.map w.Id` (section 0).

       WHAT IT TURNED OUT TO BE, which is worth saying plainly because it changes where the cost
       of the reconstruction theorem actually sits. `ids (TNode i _ cs)` is `i :: ids_all cs`: the
       walk emits a node BEFORE its subtree by construction, so the property is a theorem of the
       walk rather than an invariant an edit could break, and it needs `wf` only to know that the
       parent's occurrence is the FIRST one — which is what makes "precedes" well defined when an
       id could otherwise appear twice.

       So "preservation by `ins`, `rem_at` and `reorder_at`" is the COROLLARY that each edit
       preserves `wf`: `ins_wf` and `reorder_wf` are above, `rem_wf` is `Preservation.fst`'s and
       the `rem_at` corollary is stated there beside it, in its survivor form. Nothing here is a
       fresh induction over an edit, and a reader expecting one should read that as the shape of
       the result rather than as a gap.

       WHY "PRECEDES" AND NOT AN INDEX. The tree induction composes along `app`, and an
       index-based reading would carry an arithmetic obligation at every step where this one
       carries a membership. The decision is the same one `same_multiset` makes about `List.sort`
       and `more_than_one` makes about `List.length`: model the ordering, not a numeral.
   ====================================================================================== *)

(* `p` precedes `x` in `l`: walking left to right, `p` is met first and `x` is still to come.
   False when `x` comes first, and false when either is absent. *)
let rec precedes (p x:string) (l:list string) : Tot bool (decreases l) =
  match l with
  | [] -> false
  | h :: r -> if h = p then mem x r
              else if h = x then false
              else precedes p x r

(* ---- how it composes along `app`, which is how `ids_all` is built ---- *)

let rec precedes_app_left (p x:string) (l m:list string)
  : Lemma (requires precedes p x l) (ensures precedes p x (app l m)) (decreases l)
  = match l with
    | [] -> ()
    | h :: r -> if h = p then mem_app x r m
                else if h = x then ()
                else precedes_app_left p x r m

let rec precedes_app_right (p x:string) (l m:list string)
  : Lemma (requires not (mem p l) /\ not (mem x l) /\ precedes p x m)
          (ensures precedes p x (app l m)) (decreases l)
  = match l with
    | [] -> ()
    | _ :: r -> precedes_app_right p x r m

(* The crossing case: the parent is in the left segment and the child in the right. This is the
   one that carries the content — it is why a node's own id, emitted at the head of its subtree's
   segment, precedes every id of every LATER sibling subtree as well as of its own. *)
let rec precedes_app_split (p x:string) (l m:list string)
  : Lemma (requires mem p l /\ not (mem x l) /\ mem x m)
          (ensures precedes p x (app l m)) (decreases l)
  = match l with
    | [] -> ()
    | h :: r -> if h = p then mem_app x r m
                else if h = x then ()
                else precedes_app_split p x r m

(* ---- and the two ordering facts a consumer closes an argument with ----

   An ordering predicate is only useful if it can CONTRADICT: what a positional argument does with
   "the parent comes first" is rule out the arrangement in which it comes second. These are the
   two halves of that. Antisymmetry holds for any list; irreflexivity needs id-uniqueness, and
   that is not an accident of the definition — on a list carrying an id twice, the id genuinely
   does precede its own second occurrence. *)

let rec precedes_antisym (p x:string) (l:list string)
  : Lemma (requires precedes p x l /\ precedes x p l) (ensures p == x) (decreases l)
  = match l with
    | [] -> ()
    | h :: r -> if h = p then () else if h = x then () else precedes_antisym p x r

let rec precedes_irrefl (p:string) (l:list string)
  : Lemma (requires no_dups l) (ensures not (precedes p p l)) (decreases l)
  = match l with
    | [] -> ()
    | h :: r -> if h = p then () else precedes_irrefl p r

(* WHICH WAY ROUND IT READS, pinned by evaluation. An ordering predicate written backwards makes
   every theorem stated over it true and none of them meaningful, and nothing else in this module
   would notice — so the direction is asserted rather than left to the reader of the definition.
   The third conjunct is the one that matters: an id absent from the list precedes nothing. *)
let precedes_reads_left_to_right ()
  : Lemma (ensures precedes "a" "b" ["a"; "b"] /\
                   not (precedes "b" "a" ["a"; "b"]) /\
                   not (precedes "a" "z" ["a"; "b"]))
  = assert_norm (precedes "a" "b" ["a"; "b"]);
    assert_norm (not (precedes "b" "a" ["a"; "b"]));
    assert_norm (not (precedes "a" "z" ["a"; "b"]))

(* ---- `parent_of` answers with two ids the tree really carries ---- *)

let rec has_kid_is_mem (x:string) (ts:list tree)
  : Lemma (ensures has_kid x ts == mem x (kid_ids ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | _ :: r -> has_kid_is_mem x r

let rec parent_of_ids (x p:string) (t:tree)
  : Lemma (requires parent_of x t == Some p)
          (ensures mem p (ids t) /\ mem x (ids t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      if has_kid x cs then (has_kid_is_mem x cs; kid_ids_sub x cs)
      else parent_all_ids x p cs
and parent_all_ids (x p:string) (ts:list tree)
  : Lemma (requires parent_all x ts == Some p)
          (ensures mem p (ids_all ts) /\ mem x (ids_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      (match parent_of x t with
       | Some q -> parent_of_ids x p t; mem_app p (ids t) (ids_all r); mem_app x (ids t) (ids_all r)
       | None -> parent_all_ids x p r; mem_app p (ids t) (ids_all r); mem_app x (ids t) (ids_all r))

(* ---- THE LEMMA ---- *)

let rec preorder_parent_first (t:tree) (x p:string)
  : Lemma (requires wf t /\ parent_of x t == Some p)
          (ensures precedes p x (ids t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      if has_kid x cs then (has_kid_is_mem x cs; kid_ids_sub x cs)
      else begin
        parent_all_ids x p cs;
        preorder_parent_first_all cs x p
      end
and preorder_parent_first_all (ts:list tree) (x p:string)
  : Lemma (requires wf_all ts /\ parent_all x ts == Some p)
          (ensures precedes p x (ids_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      (match parent_of x t with
       | Some q ->
         preorder_parent_first t x p;
         precedes_app_left p x (ids t) (ids_all r)
       | None ->
         preorder_parent_first_all r x p;
         parent_all_ids x p r;
         inter_nil_iff (ids t) (ids_all r);
         precedes_app_right p x (ids t) (ids_all r))

(* The whole-subtree form, which needs no hypothesis at all: a node's id is the head of its own
   segment of the walk, so it precedes every id below it. The parent lemma above is the instance a
   caller holding a `parent_of` answer wants; this one is the instance a caller walking `after`
   top-down wants. *)
let node_precedes_its_subtree (t:tree) (x:string)
  : Lemma (requires mem x (ids_all (kids_of t)))
          (ensures precedes (tid_of t) x (ids t))
  = match t with TNode _ _ _ -> ()

(* ---- preservation, as the corollaries it is ---- *)

let ins_preserves_parent_first (p:string) (n:tree) (t:tree) (x q:string)
  : Lemma (requires wf t /\ wf n /\ disjoint (ids n) (ids t) /\
                    parent_of x (ins p n t) == Some q)
          (ensures precedes q x (ids (ins p n t)))
  = ins_wf p n t; preorder_parent_first (ins p n t) x q

let reorder_preserves_parent_first (p:string) (o:list string) (t:tree) (x q:string)
  : Lemma (requires wf t /\ reorder_ok p o t /\ parent_of x (reorder_at p o t) == Some q)
          (ensures precedes q x (ids (reorder_at p o t)))
  = reorder_wf p o t; preorder_parent_first (reorder_at p o t) x q

(* ======================================================================================
   20. THE BATCH LIFT — `covered` retired, and the diamond stated over the WHOLE alphabet
       (Phase 162, closing Phase 133's task t3).

       Phase 133 proved the diamond for twelve of the fifteen unordered pairs and named the three
       it could not: either side a `Batch` that neither does nothing nor relocates.
       `TreeOps.covered` was that boundary written as a predicate. Phase 138 named what it waited
       on — the id-uniqueness invariant at each INTERMEDIATE state of a batch's script — and
       supplied it as `Preservation.apply_preserves_wf`, leaving the induction undone. This is the
       induction, and `covered` is gone: the diamond now quantifies over every pair, and
       `Skeleton.fst` composes over the whole `SkeletonOp` alphabet.

       WHAT THE LIFT ACTUALLY NEEDS, which is less than the unconditional invariant.
       `Preservation.fst` OPENS this module, so `apply_preserves_wf` cannot be cited here — and it
       does not have to be. The residue `covered` left out is exactly the pairs where NEITHER side
       relocates (a relocating side forces the other inert, section 8, and that case was already
       closed), and a non-relocating op carries no `RemoveNode` and no `MoveNode` at any depth: it
       is built from inserts, reorders and nests of them. `ins_wf` (section 13) and `reorder_wf`
       (section 15) are already here, so `no_reloc_preserves_wf` below is a dozen lines rather
       than a module inversion. The unconditional statement remains `Preservation`'s; this is its
       non-relocating fragment, proved where the lift needs it.

       THE ARGUMENT, in three moves and no new mathematics — which is what Phase 133 predicted
       ("the argument `DagFold.replay_diamond` already performs at lane granularity — no new idea
       is required"):

         1. INDEPENDENCE DESCENDS. `fp_all` is a union, so a footprint declared independent of a
            batch's is independent of each element's. Every clause is monotone in the right
            direction, the two pinned relocation clauses included.
         2. THE RIGHT LIFT. If an operation commutes with each step of a script at every state the
            script reaches, it commutes with the script — by induction, threading the invariant
            with `no_reloc_preserves_wf` at each step. This is the whole content.
         3. THE LEFT LIFT is the same induction on the other side. It is written out rather than
            obtained from `wstep_sym`, because the recursion has to descend into the head
            operation — which may itself be a batch — and the symmetric route would make the
            termination measure circular.

       WHAT THIS DOES NOT CHANGE. Section 18's precision ceiling is untouched: that section is
       about which pairs `independent` DECLARES disjoint, and this lift is about pairs it already
       declares disjoint. `still_refused` stands verbatim.
   ====================================================================================== *)

(* ---- the non-relocating fragment, structurally. The shape `inert` has, for the same reason: a
   predicate over the OP is what a recursion can carry, where a fact about its footprint is not.
   ---- *)

let rec no_reloc (o:op) : Tot bool (decreases o) =
  match o with
  | RemoveNode _ -> false
  | MoveNode _ _ -> false
  | UpdateNode _ -> false
  | Batch os -> no_reloc_all os
  | _ -> true
and no_reloc_all (os:list op) : Tot bool (decreases os) =
  match os with
  | [] -> true
  | o :: r -> no_reloc o && no_reloc_all r

(* … and it IS the footprint's verdict — the link `structure_free_iff_inert` is for inertness. *)
let rec relocating_iff_not_no_reloc (o:op)
  : Lemma (ensures relocating o == not (no_reloc o)) (decreases o)
  = match o with
    | Batch os -> relocating_all_iff_not_no_reloc_all os
    | _ -> ()
and relocating_all_iff_not_no_reloc_all (os:list op)
  : Lemma (ensures not (is_empty (fp_all os).unknown_parent_writes) == not (no_reloc_all os))
          (decreases os)
  = match os with
    | [] -> ()
    | o :: r -> relocating_iff_not_no_reloc o; relocating_all_iff_not_no_reloc_all r

(* ---- the invariant, for the fragment the lift meets ---- *)

let rec no_reloc_preserves_wf (o:op) (t:tree)
  : Lemma (requires wf t /\ no_reloc o)
          (ensures (match apply o t with Ok t' -> wf t' | Error _ -> True)) (decreases o)
  = match o with
    | InsertChild p n ->
      first_dup_none_iff n t;
      wf_iff_no_dups n;
      if None? (first_dup n t) && has_id p t then ins_wf p n t else ()
    | ReorderChildren p order ->
      (match find_in p t with
       | None -> ()
       | Some n ->
         if same_multiset (kid_ids (kids_of n)) order then
           (wf_reorder_ok p order t; reorder_wf p order t)
         else ())
    | Batch os -> no_reloc_all_preserves_wf os t
    | RemoveNode _ -> ()
    | MoveNode _ _ -> ()
    | UpdateNode _ -> ()
and no_reloc_all_preserves_wf (os:list op) (t:tree)
  : Lemma (requires wf t /\ no_reloc_all os)
          (ensures (match apply_all os t with Ok t' -> wf t' | Error _ -> True)) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      no_reloc_preserves_wf o t;
      (match apply o t with
       | Ok t' -> no_reloc_all_preserves_wf r t'
       | Error _ -> ())

(* ---- move 1: independence descends through a union ---- *)

let independent_union_left (fo fr fb:footprint)
  : Lemma (requires independent (union_fp fo fr) fb)
          (ensures independent fo fb /\ independent fr fb)
  = let fu = union_fp fo fr in
    inter_nil_iff fu.content_writes fb.content_writes;
    inter_nil_iff fo.content_writes fb.content_writes;
    inter_nil_iff fr.content_writes fb.content_writes;
    inter_nil_iff fu.content_writes fb.reads;
    inter_nil_iff fo.content_writes fb.reads;
    inter_nil_iff fr.content_writes fb.reads;
    inter_nil_iff fb.content_writes fu.reads;
    inter_nil_iff fb.content_writes fo.reads;
    inter_nil_iff fb.content_writes fr.reads;
    inter_nil_iff fu.structure_writes fb.structure_writes;
    inter_nil_iff fo.structure_writes fb.structure_writes;
    inter_nil_iff fr.structure_writes fb.structure_writes

let independent_union_right (fa fo fr:footprint)
  : Lemma (requires independent fa (union_fp fo fr))
          (ensures independent fa fo /\ independent fa fr)
  = independent_sym fa (union_fp fo fr);
    independent_union_left fo fr fa;
    independent_sym fo fa;
    independent_sym fr fa

(* ---- move 2: a LEAF against anything, lifting on the right ---- *)

let rec leaf_vs_any (a b:op) (s:tree)
  : Lemma (requires is_leaf a /\ no_reloc b /\ independent (op_fp a) (op_fp b))
          (ensures wstep a b s) (decreases b)
  = match b with
    | Batch os -> leaf_vs_script a os s
    | _ -> leaf_wstep a b s
and leaf_vs_script (a:op) (os:list op) (s:tree)
  : Lemma (requires is_leaf a /\ no_reloc_all os /\ independent (op_fp a) (fp_all os))
          (ensures wstep a (Batch os) s) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      independent_union_right (op_fp a) (op_fp o) (fp_all r);
      leaf_vs_any a o s;
      if wf s then begin
        no_reloc_preserves_wf o s;
        match apply o s with
        | Ok s1 -> leaf_vs_script a r s1
        | Error _ -> ()
      end
      else ()

(* ---- move 3: anything against anything, lifting on the left ---- *)

let rec any_vs_any (a b:op) (s:tree)
  : Lemma (requires no_reloc a /\ no_reloc b /\ independent (op_fp a) (op_fp b))
          (ensures wstep a b s) (decreases a)
  = match a with
    | Batch os -> script_vs_any os b s
    | _ -> leaf_vs_any a b s
and script_vs_any (os:list op) (b:op) (s:tree)
  : Lemma (requires no_reloc_all os /\ no_reloc b /\ independent (fp_all os) (op_fp b))
          (ensures wstep (Batch os) b s) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      independent_union_left (op_fp o) (fp_all r) (op_fp b);
      any_vs_any o b s;
      if wf s then begin
        no_reloc_preserves_wf o s;
        match apply o s with
        | Ok s1 -> script_vs_any r b s1
        | Error _ -> ()
      end
      else ()

(* ---- THE DOMAIN HYPOTHESIS, unconditionally ----

   The successor of section 14's `covered`-restricted statement, which is deleted: every pair of
   skeleton operations whose footprints `Ops.independent` declares disjoint commutes at every
   well-formed tree at which both apply. Nine of the fifteen unordered pairs are still closed by
   `relocating_forces_inert` without looking at a tree, three are section 12's genuinely-commuting
   leaf cases, and the three that carry a batch are the induction above. *)
let tree_independence_diamond (a b:op) (s:tree)
  : Lemma (requires independent (op_fp a) (op_fp b))
          (ensures wstep a b s)
  = if relocating a then (relocating_forces_inert a b; inert_wstep_right a b s)
    else if relocating b then
      (independent_sym (op_fp a) (op_fp b); relocating_forces_inert b a; inert_wstep_left a b s)
    else begin
      relocating_iff_not_no_reloc a;
      relocating_iff_not_no_reloc b;
      any_vs_any a b s
    end

(* ---- and the same over the GUARDED algebra, which is what the composite folds ----

   `wapply` declines a step whose result is not id-unique, so the diamond over it has one thing
   left to say beyond the equation: that the common tree the two orders reach is itself
   well-formed, and so is not declined. Section 17's `leaf_diamond` does this case by case for the
   leaf alphabet; over the whole alphabet the three branches below cover it — a relocating side
   forces the other to be the identity, and otherwise `no_reloc_preserves_wf` answers directly. *)
let full_diamond (a b:op) (s:tree)
  : Lemma (requires independent (op_fp a) (op_fp b))
          (ensures Ok? (wapply a s) ==> Ok? (wapply b s) ==>
                   (Ok? (bind (wapply a s) (wapply b)) /\
                    bind (wapply a s) (wapply b) == bind (wapply b s) (wapply a)))
  = tree_independence_diamond a b s;
    if wf s && Ok? (apply a s) && Ok? (apply b s) then
      match apply a s, apply b s with
      | Ok sa, Ok sb ->
        if wf sa && wf sb then begin
          if relocating a then
            (relocating_forces_inert a b; inert_is_identity b s; inert_is_identity b sa)
          else if relocating b then
            (independent_sym (op_fp a) (op_fp b); relocating_forces_inert b a;
             inert_is_identity a s; inert_is_identity a sb)
          else begin
            relocating_iff_not_no_reloc b;
            no_reloc_preserves_wf b sa
          end
        end
        else ()
      | _, _ -> ()
    else ()

(* THE COMPOSITE'S HYPOTHESIS, over the whole alphabet. `Skeleton.fst` consumes this in place of
   `leaf_independence_diamond`, which is kept beside it: the leaf statement is what section 17's
   `leaf_diamond` proves directly, and a reader checking the widening wants to see both. *)
let op_independence_diamond ()
  : Lemma (ensures independence_diamond #op #tree #rejection op_fp wapply)
  = let aux (a b:op) : Lemma (independent (op_fp a) (op_fp b) ==> diamond wapply a b) =
      if independent (op_fp a) (op_fp b) then
        let per_state (s:tree)
          : Lemma (Ok? (wapply a s) ==> Ok? (wapply b s) ==>
                   (Ok? (bind (wapply a s) (wapply b)) /\
                    bind (wapply a s) (wapply b) == bind (wapply b s) (wapply a)))
          = full_diamond a b s
        in
        FStar.Classical.forall_intro per_state
      else ()
    in
    FStar.Classical.forall_intro_2 aux

(* ---- and the widening is NOT VACUOUS ----

   A theorem that drops a hypothesis has to be checked for the possibility that the hypothesis was
   never doing anything, and here it plainly was: `covered` named three pair shapes and Phase 133
   wrote them out because it could not close them. This is one of them, concrete and evaluated —
   a batch that inserts under one child and reorders the root, against an insert under the other
   child. The two footprints are independent, `covered_classes` REFUSES the pair, both sides apply
   at the tree, and the two orders reach the same tree. It goes red if `covered_classes` is ever
   widened to admit the shape it is here to exclude, and it is the evaluated instance of the
   theorem beside the proved one. *)

let lift_tree : tree = TNode "root" "doc" [ TNode "x" "sec" []; TNode "y" "sec" [] ]

let lift_batch : op =
  Batch [ InsertChild "x" (TNode "n1" "para" []); ReorderChildren "root" ["y"; "x"] ]

let lift_leaf : op = InsertChild "y" (TNode "n2" "para" [])

let batch_lift_is_not_vacuous ()
  : Lemma (ensures wf lift_tree /\
                   independent (op_fp lift_batch) (op_fp lift_leaf) /\
                   not (covered_classes lift_batch lift_leaf) /\
                   Ok? (wapply lift_batch lift_tree) /\
                   Ok? (wapply lift_leaf lift_tree) /\
                   Ok? (bind (wapply lift_batch lift_tree) (wapply lift_leaf)) /\
                   bind (wapply lift_batch lift_tree) (wapply lift_leaf) ==
                   bind (wapply lift_leaf lift_tree) (wapply lift_batch))
  = assert_norm (wf lift_tree);
    assert_norm (independent (op_fp lift_batch) (op_fp lift_leaf));
    assert_norm (not (covered_classes lift_batch lift_leaf));
    assert_norm (Ok? (wapply lift_batch lift_tree));
    assert_norm (Ok? (wapply lift_leaf lift_tree));
    assert_norm (Ok? (bind (wapply lift_batch lift_tree) (wapply lift_leaf)));
    assert_norm (bind (wapply lift_batch lift_tree) (wapply lift_leaf) ==
                 bind (wapply lift_leaf lift_tree) (wapply lift_batch))
