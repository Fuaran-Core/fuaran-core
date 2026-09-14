(*
   Diff — the apply engine's companion in the other direction: `Diff.toOps` derives a script that
   turns one tree into another, and this module is that derivation modelled clause for clause
   (fuaran-core Phase 141).

   WHAT IS MODELLED. `Ops.fs`'s `Diff.toOps` — the two refusals, the four passes and the order they
   are emitted in — and `Diff.toOpsContained`, its `canHold`-aware mirror. The tree, the operations,
   the rejection envelope and the whole membership algebra are `TreeOps`'s, opened here rather than
   restated, exactly as `Preservation.fst` opens them; this module extracts beside both into the
   same oracle assembly.

   WHAT IS PROVED, over any two trees:

     - `diff_total` / `diff_refusals_exact` — `toOps` reaches exactly one outcome on every input,
       and WHICH: it refuses exactly when the two roots carry different ids or either tree repeats
       an id, in that precedence, and on nothing else. The converse half is the sentence the phase
       exists for, stated as `diff_ok_on_any_wf_pair`: for ANY two well-formed trees sharing a root
       id a script is produced, not only for a pair one of which was derived from the other by
       applying operations — which is the only pair `Conformance.diffLaws` ever samples.
     - `diff_emission_order` — the emitted script is `inserts ++ moves ++ removes ++ reorders`, four
       homogeneous blocks in that order. This is the argument the source comment carries in prose
       ("added nodes go in as leaf shells, every survivor is then reattached, removed regions are
       deleted **last**"), mechanised: it is a property of the four passes, and every applicability
       argument about the script rests on it.
     - the four block characterisations, each the clause of that argument its pass supplies:
       `diff_inserts_are_new_leaf_shells` (a graft is CHILDLESS and its id is one `before` does not
       carry, and the parent named is that node's after-parent),
       `diff_moves_are_survivor_reattachments` (a move names a survivor whose before-parent is not
       the destination, and the destination HOLDS it in `after`), `diff_removes_only_dead` —
       survivor preservation, which `Conformance.diffLaws` samples and this proves — and
       `diff_reorders_state_the_after_order`.
     - `diff_contained_diff` / `diff_contained_locates` / `diff_contained_at_total_is_plain` /
       `diff_applicable_contained` — the container-aware mirror, and the last is the one the
       phase's fourth task asks for: every parent address an emitted operation carries resolves, in
       `after`, to a node that HOLDS the addressed child and that `canHold` accepts. So a script
       `toOpsContained` returns cannot be refused for containment at any step, and the typed
       `TargetNotAContainer` is the exact price of that guarantee.

   WHAT IS NOT CLAIMED, and it is the larger half — see the README's theorem 6 for the full
   statement and the reason. `diff_reconstructs` (`apply_all (to_ops b a) b == Ok a`) and the
   operational `diff_applicable` (every emitted step is ACCEPTED in sequence by `apply`) are carried
   there as differentially TESTED rather than proved: both need the positional argument relating a
   preorder walk of `after` to the intermediate trees the script builds, which is a body of work
   about `ins` and `rem_at` this phase's time box did not contain. What is proved here is everything
   the four passes guarantee about the SCRIPT; what is tested, over independently generated pairs,
   is what applying it does.

   WHY `TreeDiff` AND NOT `Diff`. The same reason `TreeOps.fst` models `Ops.fs` and is not called
   `Ops`: the extracted oracle is a top-level F# module, and the differential host opens
   `Fuaran.Core`, so a model named for the production module it mirrors would put two `Diff`s in one
   scope. D35 (Phase 147) records what that costs — a name shared by two things in scope resolves by
   accident of open order and reads as a defect somewhere else entirely.

   HOW TO READ IT. Every definition names its F# counterpart, as in `DagFold.fst`, `TreeOps.fst`
   and `Preservation.fst`. Sets are lists read under `mem` (the README's standing reading), and
   F#'s `Map.ofList` is modelled as a LAST-WINS association lookup, which is what `Map.ofList` does
   with a repeated key — the same fidelity `TreeOps.pick_last` keeps for the `Map.ofList` inside
   `validateReorder`.

   Apache-2.0, like everything beside it.
*)
module TreeDiff

open DagFold
open TreeOps

(* The two modules this one opens carry ninety-odd lemmas between them, most with SMT patterns that
   would otherwise be live at every query here. Scoped with `#set-options`, as `TreeOps.fst`,
   `JsonParse.fst` and `Preservation.fst` each are, and for the reason recorded there: pruning
   changes which facts a query can see, so it is a per-module decision and never a leg-wide flag. *)
#set-options "--ext context_pruning"

(* ======================================================================================
   1. Why a diff could not be produced (F#: `Diff.DiffError<'Id>` in Ops.fs).

      Three cases, and the third is `toOpsContained`'s alone — the plain `toOps` cannot raise it,
      which section 5 proves rather than asserts, in the shape `Preservation.apply_total` uses for
      the two rejection classes `apply` cannot reach.
   ====================================================================================== *)

type diff_error =
  | RootIdMismatch      : before:string -> after:string -> diff_error
  | DuplicateIdInTree   : string -> diff_error
  | TargetNotAContainer : parent:string -> kind_tag:string -> diff_error

(* ======================================================================================
   2. The walk and the two maps `toOps` builds before its first pass.
   ====================================================================================== *)

(* F#: `Tree.preorder w root` — node then children, left to right. `TreeOps.ids` is this list's
   ids; the passes need the NODES, because each one's children are read. *)
let rec pre (t:tree) : Tot (list tree) (decreases t) =
  match t with
  | TNode _ _ cs -> t :: pre_all cs
and pre_all (ts:list tree) : Tot (list tree) (decreases ts) =
  match ts with
  | [] -> []
  | x :: r -> app (pre x) (pre_all r)

(* F#: `List.map w.Id` over a node list — the `key` of each. *)
let rec tids (ts:list tree) : Tot (list string) (decreases ts) =
  match ts with
  | [] -> []
  | t :: r -> tid_of t :: tids r

let rec tids_app (l m:list tree)
  : Lemma (ensures tids (app l m) == app (tids l) (tids m)) (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> tids_app t m

(* `Tree.ids` IS the ids of that walk. The two are written separately in F# (`preorder`, then
   `List.map w.Id`) and this is the bridge, so a fact about one transfers to the other. *)
let rec ids_is_pre (t:tree)
  : Lemma (ensures ids t == tids (pre t)) (decreases t)
  = match t with
    | TNode _ _ cs -> ids_all_is_pre_all cs
and ids_all_is_pre_all (ts:list tree)
  : Lemma (ensures ids_all ts == tids (pre_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | x :: r ->
      ids_is_pre x;
      ids_all_is_pre_all r;
      tids_app (pre x) (pre_all r)

(* F#: `Map.tryFind` against a `Map.ofList` — which keeps the LAST entry for a repeated key, so the
   lookup is last-wins. `TreeOps.pick_last` models the same `Map.ofList` the same way and for the
   same reason: under `wf` the key is unique and the two readings coincide, but a model that
   ASSUMED uniqueness would be modelling the tree the validator has already accepted rather than
   the function. *)
let rec lookup (k:string) (l:list (string & string)) : Tot (option string) (decreases l) =
  match l with
  | [] -> None
  | (a, b) :: r ->
    (match lookup k r with
     | Some v -> Some v
     | None -> if a = k then Some b else None)

let rec lookup_kids (k:string) (l:list (string & list string)) : Tot (option (list string)) (decreases l) =
  match l with
  | [] -> None
  | (a, b) :: r ->
    (match lookup_kids k r with
     | Some v -> Some v
     | None -> if a = k then Some b else None)

let rec lookup_app (k:string) (l m:list (string & string))
  : Lemma (ensures lookup k (app l m) ==
                   (match lookup k m with Some v -> Some v | None -> lookup k l))
          (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> lookup_app k t m

(* F#: `parentMap` — `[ for p in nodes do for c in w.Children p -> key (w.Id c), w.Id p ]`. *)
let rec kid_pairs (p:string) (cs:list tree) : Tot (list (string & string)) (decreases cs) =
  match cs with
  | [] -> []
  | c :: r -> (tid_of c, p) :: kid_pairs p r

let rec parent_map (ns:list tree) : Tot (list (string & string)) (decreases ns) =
  match ns with
  | [] -> []
  | p :: r -> app (kid_pairs (tid_of p) (kids_of p)) (parent_map r)

(* F#: `bChildKeys` — `beforeNodes |> List.map (fun n -> key (w.Id n), childKeysOf n)`. *)
let rec kid_map (ns:list tree) : Tot (list (string & list string)) (decreases ns) =
  match ns with
  | [] -> []
  | n :: r -> (tid_of n, kid_ids (kids_of n)) :: kid_map r

(* F#: `Tree.wellFormed w idw root`, read through `Diff`'s own `dupId` — one preorder scan naming
   the first id reached TWICE. Phase 139 re-pointed this at the named predicate; before that it was
   a `groupBy` of its own, and the one observable change was WHICH duplicate is named. *)
let dup_id (t:tree) : Tot (option string) = scan_dup [] (ids t)

(* ======================================================================================
   3. The four passes (F#: `Ops.fs`'s `toOps`, one function per numbered comment block).
   ====================================================================================== *)

(* F#: `w.ReplaceChildren n []` — the LEAF SHELL an added node goes in as. The witness law says a
   replace changes nothing but the children, which is why the shell keeps id and kind. *)
let shell (n:tree) : Tot tree = TNode (tid_of n) (kind_of n) []

(* F#: `List.length (w.Children p) > 1`, written without arithmetic — the same choice
   `TreeOps.same_multiset` makes about `List.sort`: model the DECISION, not a numeral the witness
   never needed. *)
let more_than_one (#a:Type) (l:list a) : Tot bool =
  match l with
  | [] -> false
  | [_] -> false
  | _ -> true

(* PASS 1 — added nodes go in as leaf shells under their after-parent, TOP-DOWN via the preorder,
   so an added parent exists before an added child. They append; pass 4 states the order. *)
let rec pass_inserts (a_par:list (string & string)) (b_ids:list string) (ns:list tree)
  : Tot (list op) (decreases ns) =
  match ns with
  | [] -> []
  | n :: r ->
    let k = tid_of n in
    if mem k b_ids then pass_inserts a_par b_ids r
    else
      (match lookup k a_par with
       | Some pid -> InsertChild pid (shell n) :: pass_inserts a_par b_ids r
       (* F#: `| None -> ()` — "an added root is impossible (roots match)". *)
       | None -> pass_inserts a_par b_ids r)

(* PASS 2, inner — a survivor whose before-parent differs from its after-parent must move. A
   newly-added node was already appended by pass 1. *)
let rec moves_under (pk:string) (b_ids:list string) (b_par:list (string & string)) (cs:list tree)
  : Tot (list op) (decreases cs) =
  match cs with
  | [] -> []
  | c :: r ->
    let ck = tid_of c in
    let moved =
      mem ck b_ids &&
      (match lookup ck b_par with
       | Some bp -> bp <> pk
       (* F#: `| None -> true` — no before-parent, so it was the root's own child set. *)
       | None -> true) in
    if moved then MoveNode ck pk :: moves_under pk b_ids b_par r
    else moves_under pk b_ids b_par r

(* PASS 2 — reattach every survivor, and REMEMBER the parent whose order must be restated once
   membership is final. A parent whose child-id list is unchanged is already correct, so it is
   skipped entirely: its children are never disturbed. *)
let rec pass_moves
  (b_ids:list string)
  (b_par:list (string & string))
  (b_kids:list (string & list string))
  (ns:list tree)
  : Tot (list op & list string) (decreases ns) =
  match ns with
  | [] -> ([], [])
  | p :: r ->
    let pk = tid_of p in
    let a_kid_keys = kid_ids (kids_of p) in
    let unchanged =
      mem pk b_ids &&
      (match lookup_kids pk b_kids with
       | Some bk -> bk = a_kid_keys
       | None -> false) in
    let rest = pass_moves b_ids b_par b_kids r in
    if unchanged then rest
    else (app (moves_under pk b_ids b_par (kids_of p)) (fst rest), pk :: (snd rest))

(* PASS 3 — removals LAST, so a removed region's surviving descendants have already been moved out
   and removing the region's top node drops only removed nodes. The top node is the removed node
   whose before-parent SURVIVES; a removed node under a removed parent is covered by removing the
   parent. *)
let rec pass_removes (a_ids:list string) (b_par:list (string & string)) (ns:list tree)
  : Tot (list op) (decreases ns) =
  match ns with
  | [] -> []
  | n :: r ->
    let k = tid_of n in
    if mem k a_ids then pass_removes a_ids b_par r
    else
      (match lookup k b_par with
       | Some pid ->
         if mem pid a_ids
         then RemoveNode k :: pass_removes a_ids b_par r
         else pass_removes a_ids b_par r
       | None -> pass_removes a_ids b_par r)

(* PASS 4 — order, last of all: every parent now holds exactly its after-children, so naming the
   after-order is a legal permutation. One op per CHANGED parent, and none at all for a parent with
   fewer than two children, where every permutation is the identity. *)
let rec pass_reorders (after:tree) (ps:list string) : Tot (list op) (decreases ps) =
  match ps with
  | [] -> []
  | pid :: r ->
    (match find_in pid after with
     | Some p ->
       if more_than_one (kids_of p)
       then ReorderChildren pid (kid_ids (kids_of p)) :: pass_reorders after r
       else pass_reorders after r
     | None -> pass_reorders after r)

(* ======================================================================================
   4. `Diff.toOps` itself (F#: `Ops.fs`, clause for clause), and `Diff.toOpsContained`.

      The four blocks are named rather than inlined, so that the emission-order theorem in section
      6 can be stated about THEM rather than about an existential. It is the same function either
      way; what changes is whether the theorem can say which block an operation came from.
   ====================================================================================== *)

let diff_blocks (before after:tree) : Tot (list op & list op & list op & list op) =
  let b_nodes = pre before in
  let a_nodes = pre after in
  let b_ids = ids before in
  let a_ids = ids after in
  let a_par = parent_map a_nodes in
  let b_par = parent_map b_nodes in
  let b_kids = kid_map b_nodes in
  let p2 = pass_moves b_ids b_par b_kids a_nodes in
  (pass_inserts a_par b_ids a_nodes,
   fst p2,
   pass_removes a_ids b_par b_nodes,
   pass_reorders after (snd p2))

let script_of (bl:(list op & list op & list op & list op)) : Tot (list op) =
  let (p1, p2, p3, p4) = bl in
  app p1 (app p2 (app p3 p4))

let to_ops (before after:tree) : Tot (outcome (list op) diff_error) =
  if tid_of before <> tid_of after then
    Error (RootIdMismatch (tid_of before) (tid_of after))
  else
    match dup_id before with
    | Some d -> Error (DuplicateIdInTree d)
    | None ->
      (match dup_id after with
       | Some d -> Error (DuplicateIdInTree d)
       | None -> Ok (script_of (diff_blocks before after)))

(* F#: `Tree.preorder w after |> List.tryFind (fun p -> not (List.isEmpty (w.Children p)) && not
   (canHold p))` — the first `after` node that HAS children and cannot hold them. *)
let rec first_non_container (ch:tree -> bool) (ns:list tree) : Tot (option tree) (decreases ns) =
  match ns with
  | [] -> None
  | n :: r -> if Cons? (kids_of n) && not (ch n) then Some n else first_non_container ch r

let to_ops_contained (ch:tree -> bool) (before after:tree) : Tot (outcome (list op) diff_error) =
  match first_non_container ch (pre after) with
  | Some p -> Error (TargetNotAContainer (tid_of p) (kind_of p))
  | None -> to_ops before after

(* ======================================================================================
   5. Totality, and the refusals EXACTLY.

      `to_ops` is `Tot`, which is the termination half of this phase's first task discharged by the
      prover rather than argued: every pass recurses on a list derived from one of the two trees by
      `pre`, and `pre` itself descends the tree. Nothing here is fuelled and nothing is partial.

      The content is the second lemma. `Preservation.apply_total` characterises which rejection each
      clause of `apply` can raise; this is the same shape for a function with two of them, and the
      direction that matters is the CONVERSE — a pair the diff refuses is one that genuinely cannot
      be diffed by a skeleton script, never merely one the algorithm could not handle.
   ====================================================================================== *)

let diff_total (b a:tree)
  : Lemma (ensures Ok? (to_ops b a) \/ Error? (to_ops b a))
  = ()

(* `dupId` decides `Tree.wellFormed`, which is `wf`. Two bridges already in `TreeOps`, composed:
   `scan_dup_none_iff` at the empty seen set, and `wf_iff_no_dups`. *)
let dup_id_none_iff (t:tree)
  : Lemma (ensures None? (dup_id t) <==> wf t)
  = scan_dup_none_iff [] (ids t);
    inter_nil_iff (ids t) [];
    wf_iff_no_dups t

(* The whole refusal surface in one statement: WHEN it refuses, WHICH class, in WHICH precedence,
   and — the half that makes it a characterisation rather than a list — that it refuses on nothing
   else. *)
let diff_refusals_exact (b a:tree)
  : Lemma (ensures
      (Error? (to_ops b a) <==> (tid_of b <> tid_of a \/ ~(wf b) \/ ~(wf a))) /\
      (tid_of b <> tid_of a ==> to_ops b a == Error (RootIdMismatch (tid_of b) (tid_of a))) /\
      (tid_of b == tid_of a /\ ~(wf b) ==>
         (exists (d:string). dup_id b == Some d /\ to_ops b a == Error (DuplicateIdInTree d))) /\
      (tid_of b == tid_of a /\ wf b /\ ~(wf a) ==>
         (exists (d:string). dup_id a == Some d /\ to_ops b a == Error (DuplicateIdInTree d))))
  = dup_id_none_iff b;
    dup_id_none_iff a

(* The sentence the phase exists for, which is that lemma's contrapositive: for ANY two well-formed
   trees carrying the same root id — not merely for a pair one of which was derived from the other
   by applying operations, which is the only pair `Conformance.diffLaws` ever samples — `toOps`
   produces a script. *)
let diff_ok_on_any_wf_pair (b a:tree)
  : Lemma (requires tid_of b == tid_of a /\ wf b /\ wf a)
          (ensures Ok? (to_ops b a))
  = dup_id_none_iff b;
    dup_id_none_iff a

(* And the third error class is `toOpsContained`'s alone: the plain diff never raises it, the
   diff-side analogue of `apply` never raising `NotAContainer`
   (`Preservation.apply_never_container_or_domain`). *)
let diff_never_target_not_a_container (b a:tree)
  : Lemma (ensures Ok? (to_ops b a) \/ ~(TargetNotAContainer? (Error?._0 (to_ops b a))))
  = ()

(* ======================================================================================
   6. The emission ORDER, which is the theorem.

      `Ops.fs`'s doc comment says: "The emitted order is always applyable: added nodes go in as leaf
      shells (top-down), every survivor is then reattached/reordered to its `after` position, and
      removed regions are deleted **last** (so a surviving child is pulled out before its old
      container is removed)." That is an argument in prose about a four-pass procedure, and every
      applicability claim about the script rests on it. Here it is a property of the emitted list:
      four homogeneous blocks, in that order, each block being exactly one pass's output.
   ====================================================================================== *)

let is_insert  (o:op) : Tot bool = InsertChild? o
let is_move    (o:op) : Tot bool = MoveNode? o
let is_remove  (o:op) : Tot bool = RemoveNode? o
let is_reorder (o:op) : Tot bool = ReorderChildren? o

let rec all_ops (p:op -> bool) (l:list op) : Tot bool (decreases l) =
  match l with
  | [] -> true
  | o :: r -> p o && all_ops p r

let rec all_ops_app (p:op -> bool) (l m:list op)
  : Lemma (ensures all_ops p (app l m) == (all_ops p l && all_ops p m)) (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> all_ops_app p t m

let rec all_ops_and (p q:op -> bool) (l:list op)
  : Lemma (requires all_ops p l /\ all_ops q l)
          (ensures all_ops (fun o -> p o && q o) l)
          (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> all_ops_and p q t

let rec all_ops_weaken (p q:op -> bool) (l:list op)
  : Lemma (requires all_ops p l /\ (forall (o:op). p o ==> q o))
          (ensures all_ops q l)
          (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> all_ops_weaken p q t

(* The per-member reading of an `all_ops`: what a caller wants when it holds one operation of the
   script and asks which pass could have emitted it. *)
let rec all_ops_mem (p:op -> bool) (l:list op) (o:op)
  : Lemma (requires all_ops p l /\ mem o l) (ensures p o) (decreases l)
  = match l with
    | [] -> ()
    | x :: r -> if x = o then () else all_ops_mem p r o

let rec pass_inserts_all (a_par:list (string & string)) (b_ids:list string) (ns:list tree)
  : Lemma (ensures all_ops is_insert (pass_inserts a_par b_ids ns)) (decreases ns)
  = match ns with
    | [] -> ()
    | _ :: r -> pass_inserts_all a_par b_ids r

let rec moves_under_all (pk:string) (b_ids:list string) (b_par:list (string & string)) (cs:list tree)
  : Lemma (ensures all_ops is_move (moves_under pk b_ids b_par cs)) (decreases cs)
  = match cs with
    | [] -> ()
    | _ :: r -> moves_under_all pk b_ids b_par r

let rec pass_moves_all
  (b_ids:list string) (b_par:list (string & string)) (b_kids:list (string & list string)) (ns:list tree)
  : Lemma (ensures all_ops is_move (fst (pass_moves b_ids b_par b_kids ns))) (decreases ns)
  = match ns with
    | [] -> ()
    | p :: r ->
      pass_moves_all b_ids b_par b_kids r;
      moves_under_all (tid_of p) b_ids b_par (kids_of p);
      all_ops_app is_move (moves_under (tid_of p) b_ids b_par (kids_of p))
                          (fst (pass_moves b_ids b_par b_kids r))

let rec pass_removes_all (a_ids:list string) (b_par:list (string & string)) (ns:list tree)
  : Lemma (ensures all_ops is_remove (pass_removes a_ids b_par ns)) (decreases ns)
  = match ns with
    | [] -> ()
    | _ :: r -> pass_removes_all a_ids b_par r

let rec pass_reorders_all (after:tree) (ps:list string)
  : Lemma (ensures all_ops is_reorder (pass_reorders after ps)) (decreases ps)
  = match ps with
    | [] -> ()
    | _ :: r -> pass_reorders_all after r

let diff_emission_order (b a:tree)
  : Lemma (requires Ok? (to_ops b a))
          (ensures (let (ins, mv, rm, ro) = diff_blocks b a in
                    Ok?._0 (to_ops b a) == app ins (app mv (app rm ro)) /\
                    all_ops is_insert  ins /\
                    all_ops is_move    mv  /\
                    all_ops is_remove  rm  /\
                    all_ops is_reorder ro))
  = let b_nodes = pre b in
    let a_nodes = pre a in
    let p2 = pass_moves (ids b) (parent_map b_nodes) (kid_map b_nodes) a_nodes in
    pass_inserts_all (parent_map a_nodes) (ids b) a_nodes;
    pass_moves_all (ids b) (parent_map b_nodes) (kid_map b_nodes) a_nodes;
    pass_removes_all (ids a) (parent_map b_nodes) b_nodes;
    pass_reorders_all a (snd p2)

(* ======================================================================================
   7. What each block GUARANTEES — the four clauses of the emission argument.

      Each predicate is written to hold VACUOUSLY on the operations the other passes emit, so the
      four compose over the whole script and a reader holding one operation gets the fact that
      belongs to it without having to know which block it came from.
   ====================================================================================== *)

(* ---- pass 1: a new node arrives as a childless shell under its after-parent ---- *)

let insert_shape (b_ids:list string) (a_par:list (string & string)) (o:op) : Tot bool =
  match o with
  | InsertChild p n ->
    Nil? (kids_of n) &&
    not (mem (tid_of n) b_ids) &&
    lookup (tid_of n) a_par = Some p
  | _ -> true

let rec pass_inserts_shape (a_par:list (string & string)) (b_ids:list string) (ns:list tree)
  : Lemma (ensures all_ops (insert_shape b_ids a_par) (pass_inserts a_par b_ids ns)) (decreases ns)
  = match ns with
    | [] -> ()
    | _ :: r -> pass_inserts_shape a_par b_ids r

(* ---- pass 2: a move names a survivor, its destination is not where it already was, and the
       destination HOLDS it in `after` ---- *)

(* `np` names a node of the list that carries `x` among its children — "the destination is the
   moved node's after-parent", stated as a membership fact rather than through the last-wins map,
   because the map's uniqueness is a consequence of `wf` and this holds without it. *)
let rec is_after_child (ns:list tree) (x:string) (np:string) : Tot bool (decreases ns) =
  match ns with
  | [] -> false
  | n :: r -> (tid_of n = np && mem x (kid_ids (kids_of n))) || is_after_child r x np

let move_shape (b_ids:list string) (b_par:list (string & string)) (ns:list tree) (o:op) : Tot bool =
  match o with
  | MoveNode x np ->
    mem x b_ids &&
    (match lookup x b_par with Some bp -> bp <> np | None -> true) &&
    is_after_child ns x np
  | _ -> true

let rec is_after_child_of_mem (ns:list tree) (p:tree) (x:string)
  : Lemma (requires mem p ns /\ mem x (kid_ids (kids_of p)))
          (ensures is_after_child ns x (tid_of p))
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r -> if n = p then () else is_after_child_of_mem r p x

let rec moves_under_shape
  (ns:list tree) (p:tree) (b_ids:list string) (b_par:list (string & string)) (cs:list tree)
  : Lemma (requires mem p ns /\ (forall (c:tree). mem c cs ==> mem (tid_of c) (kid_ids (kids_of p))))
          (ensures all_ops (move_shape b_ids b_par ns) (moves_under (tid_of p) b_ids b_par cs))
          (decreases cs)
  = match cs with
    | [] -> ()
    | c :: r ->
      moves_under_shape ns p b_ids b_par r;
      is_after_child_of_mem ns p (tid_of c)

let rec kid_ids_has_each (cs:list tree)
  : Lemma (ensures forall (c:tree). mem c cs ==> mem (tid_of c) (kid_ids cs)) (decreases cs)
  = match cs with
    | [] -> ()
    | _ :: r -> kid_ids_has_each r

let is_after_child_cons (n:tree) (ns:list tree) (x np:string)
  : Lemma (ensures is_after_child ns x np ==> is_after_child (n :: ns) x np)
  = ()

let rec pass_moves_shape
  (b_ids:list string) (b_par:list (string & string)) (b_kids:list (string & list string)) (ns:list tree)
  : Lemma (ensures all_ops (move_shape b_ids b_par ns) (fst (pass_moves b_ids b_par b_kids ns)))
          (decreases ns)
  = match ns with
    | [] -> ()
    | p :: r ->
      pass_moves_shape b_ids b_par b_kids r;
      (* the tail's operations were shown against `r`; `is_after_child` is monotone in the list *)
      all_ops_weaken (move_shape b_ids b_par r) (move_shape b_ids b_par (p :: r))
                     (fst (pass_moves b_ids b_par b_kids r));
      kid_ids_has_each (kids_of p);
      moves_under_shape (p :: r) p b_ids b_par (kids_of p);
      all_ops_app (move_shape b_ids b_par (p :: r))
                  (moves_under (tid_of p) b_ids b_par (kids_of p))
                  (fst (pass_moves b_ids b_par b_kids r))

(* ---- pass 3: a removal targets a node `after` does not carry, whose before-parent survives ---- *)

let remove_shape (a_ids:list string) (b_par:list (string & string)) (o:op) : Tot bool =
  match o with
  | RemoveNode x ->
    not (mem x a_ids) &&
    (match lookup x b_par with Some pid -> mem pid a_ids | None -> false)
  | _ -> true

let rec pass_removes_shape (a_ids:list string) (b_par:list (string & string)) (ns:list tree)
  : Lemma (ensures all_ops (remove_shape a_ids b_par) (pass_removes a_ids b_par ns)) (decreases ns)
  = match ns with
    | [] -> ()
    | _ :: r -> pass_removes_shape a_ids b_par r

(* ---- pass 4: a reorder states the after-order of a parent that has more than one child ---- *)

let reorder_shape (after:tree) (o:op) : Tot bool =
  match o with
  | ReorderChildren p ord ->
    (match find_in p after with
     | Some pn -> ord = kid_ids (kids_of pn) && more_than_one (kids_of pn)
     | None -> false)
  | _ -> true

let rec pass_reorders_shape (after:tree) (ps:list string)
  : Lemma (ensures all_ops (reorder_shape after) (pass_reorders after ps)) (decreases ps)
  = match ps with
    | [] -> ()
    | _ :: r -> pass_reorders_shape after r

(* ---- and the four, composed over the whole script ---- *)

let script_shape (b a:tree) (o:op) : Tot bool =
  insert_shape (ids b) (parent_map (pre a)) o &&
  move_shape (ids b) (parent_map (pre b)) (pre a) o &&
  remove_shape (ids a) (parent_map (pre b)) o &&
  reorder_shape a o

let diff_script_shape (b a:tree)
  : Lemma (requires Ok? (to_ops b a))
          (ensures all_ops (script_shape b a) (Ok?._0 (to_ops b a)))
  = let b_nodes = pre b in
    let a_nodes = pre a in
    let a_par = parent_map a_nodes in
    let b_par = parent_map b_nodes in
    let b_kids = kid_map b_nodes in
    let p1 = pass_inserts a_par (ids b) a_nodes in
    let p2 = fst (pass_moves (ids b) b_par b_kids a_nodes) in
    let p3 = pass_removes (ids a) b_par b_nodes in
    let p4 = pass_reorders a (snd (pass_moves (ids b) b_par b_kids a_nodes)) in
    pass_inserts_all a_par (ids b) a_nodes;
    pass_inserts_shape a_par (ids b) a_nodes;
    pass_moves_all (ids b) b_par b_kids a_nodes;
    pass_moves_shape (ids b) b_par b_kids a_nodes;
    pass_removes_all (ids a) b_par b_nodes;
    pass_removes_shape (ids a) b_par b_nodes;
    pass_reorders_all a (snd (pass_moves (ids b) b_par b_kids a_nodes));
    pass_reorders_shape a (snd (pass_moves (ids b) b_par b_kids a_nodes));
    all_ops_and is_insert (insert_shape (ids b) a_par) p1;
    all_ops_and is_move (move_shape (ids b) b_par a_nodes) p2;
    all_ops_and is_remove (remove_shape (ids a) b_par) p3;
    all_ops_and is_reorder (reorder_shape a) p4;
    all_ops_weaken (fun o -> is_insert o && insert_shape (ids b) a_par o) (script_shape b a) p1;
    all_ops_weaken (fun o -> is_move o && move_shape (ids b) b_par a_nodes o) (script_shape b a) p2;
    all_ops_weaken (fun o -> is_remove o && remove_shape (ids a) b_par o) (script_shape b a) p3;
    all_ops_weaken (fun o -> is_reorder o && reorder_shape a o) (script_shape b a) p4;
    all_ops_app (script_shape b a) p3 p4;
    all_ops_app (script_shape b a) p2 (app p3 p4);
    all_ops_app (script_shape b a) p1 (app p2 (app p3 p4))

(* ---- the four theorems a reader cites ---- *)

let diff_inserts_are_new_leaf_shells (b a:tree) (o:op)
  : Lemma (requires Ok? (to_ops b a) /\ mem o (Ok?._0 (to_ops b a)))
          (ensures insert_shape (ids b) (parent_map (pre a)) o)
  = diff_script_shape b a;
    all_ops_mem (script_shape b a) (Ok?._0 (to_ops b a)) o

let diff_moves_are_survivor_reattachments (b a:tree) (o:op)
  : Lemma (requires Ok? (to_ops b a) /\ mem o (Ok?._0 (to_ops b a)))
          (ensures move_shape (ids b) (parent_map (pre b)) (pre a) o)
  = diff_script_shape b a;
    all_ops_mem (script_shape b a) (Ok?._0 (to_ops b a)) o

(* SURVIVOR PRESERVATION — `Conformance.diffLaws`' third law, as a theorem: no `RemoveNode` the
   script emits targets an id both trees carry. A relocated survivor diffs to a `MoveNode`, never
   to a remove-and-reinsert, which is what makes a diff preserve identity across a restructure. *)
let diff_removes_only_dead (b a:tree) (o:op)
  : Lemma (requires Ok? (to_ops b a) /\ mem o (Ok?._0 (to_ops b a)) /\ RemoveNode? o)
          (ensures ~(mem (RemoveNode?.target o) (ids a)))
  = diff_script_shape b a;
    all_ops_mem (script_shape b a) (Ok?._0 (to_ops b a)) o

let diff_reorders_state_the_after_order (b a:tree) (o:op)
  : Lemma (requires Ok? (to_ops b a) /\ mem o (Ok?._0 (to_ops b a)))
          (ensures reorder_shape a o)
  = diff_script_shape b a;
    all_ops_mem (script_shape b a) (Ok?._0 (to_ops b a)) o

(* ======================================================================================
   8. The container-aware mirror (F#: `Diff.toOpsContained`, Phase 09; the diff-path analogue of
      `Ops.applyContained`).

      The relationship `apply` / `applyContained` have, in the diff direction:
      `toOpsContained (fun _ -> true)` IS `toOps`, and where it differs it differs by exactly one
      refusal class. Then the theorem the phase's fourth task asks for.
   ====================================================================================== *)

let rec first_non_container_some (ch:tree -> bool) (ns:list tree) (n:tree)
  : Lemma (requires first_non_container ch ns == Some n)
          (ensures mem n ns /\ Cons? (kids_of n) /\ ~(ch n))
          (decreases ns)
  = match ns with
    | [] -> ()
    | x :: r -> if Cons? (kids_of x) && not (ch x) then () else first_non_container_some ch r n

(* The offender a `TargetNotAContainer` names is a real node of `after` that really does carry
   children and really is refused by the predicate — `Preservation.not_a_container_locates`, in the
   diff direction. *)
let diff_contained_locates (ch:tree -> bool) (b a:tree)
  : Lemma (requires Error? (to_ops_contained ch b a) /\
                    TargetNotAContainer? (Error?._0 (to_ops_contained ch b a)))
          (ensures (exists (n:tree).
                      mem n (pre a) /\ Cons? (kids_of n) /\ ~(ch n) /\
                      TargetNotAContainer?.parent (Error?._0 (to_ops_contained ch b a)) == tid_of n /\
                      TargetNotAContainer?.kind_tag (Error?._0 (to_ops_contained ch b a)) == kind_of n))
  = match first_non_container ch (pre a) with
    | Some n -> first_non_container_some ch (pre a) n
    | None -> ()

(* The DIFFERENCE statement, in the shape Phase 140 found was the honest one for the apply side
   (`apply_contained_diff`): the container-aware diff is the plain diff unless it is a
   `TargetNotAContainer`. Stating it this way rather than as "the same unless the capability adds a
   refusal" is what 140's differential went red on and corrected. *)
let diff_contained_diff (ch:tree -> bool) (b a:tree)
  : Lemma (ensures to_ops_contained ch b a == to_ops b a \/
                   (Error? (to_ops_contained ch b a) /\
                    TargetNotAContainer? (Error?._0 (to_ops_contained ch b a))))
  = ()

let rec first_non_container_total (ns:list tree)
  : Lemma (ensures first_non_container (fun _ -> true) ns == None) (decreases ns)
  = match ns with
    | [] -> ()
    | _ :: r -> first_non_container_total r

(* `toOps` IS the `fun _ -> true` instance, which is what the F# doc comment claims and what makes
   the clause-for-clause model of the pair checkable rather than asserted
   (`Preservation.apply_contained_is_apply`, in the diff direction). *)
let diff_contained_at_total_is_plain (b a:tree)
  : Lemma (ensures to_ops_contained (fun _ -> true) b a == to_ops b a)
  = first_non_container_total (pre a)

(* ---- and the theorem: a contained script cannot be refused for containment ---- *)

(* `np` names a node of `after` that HOLDS `x` as a child AND that `ch` accepts — precisely what
   the container-aware apply asks when it resolves the parent address of an insert or a move. *)
let rec parent_accepts (ch:tree -> bool) (ns:list tree) (x:string) (np:string)
  : Tot bool (decreases ns) =
  match ns with
  | [] -> false
  | n :: r ->
    (tid_of n = np && mem x (kid_ids (kids_of n)) && ch n) || parent_accepts ch r x np

let kid_ids_nonempty (cs:list tree) (x:string)
  : Lemma (requires mem x (kid_ids cs)) (ensures Cons? cs) (decreases cs)
  = ()

let rec after_child_accepts (ch:tree -> bool) (ns:list tree) (x np:string)
  : Lemma (requires is_after_child ns x np /\ first_non_container ch ns == None)
          (ensures parent_accepts ch ns x np)
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r ->
      if tid_of n = np && mem x (kid_ids (kids_of n))
      then kid_ids_nonempty (kids_of n) x
      else after_child_accepts ch r x np

let contained_shape (ch:tree -> bool) (ns:list tree) (o:op) : Tot bool =
  match o with
  | InsertChild p n -> parent_accepts ch ns (tid_of n) p
  | MoveNode x np   -> parent_accepts ch ns x np
  | _ -> true

(* The parent of an emitted INSERT is an after-node holding it: `lookup` against the after
   `parent_map` succeeded, and every pair in that map came from some node's child list. *)
let rec lookup_kid_pairs (k:string) (pk:string) (cs:list tree) (v:string)
  : Lemma (requires lookup k (kid_pairs pk cs) == Some v)
          (ensures v == pk /\ mem k (kid_ids cs))
          (decreases cs)
  = match cs with
    | [] -> ()
    | c :: r -> (match lookup k (kid_pairs pk r) with
                 | Some _ -> lookup_kid_pairs k pk r v
                 | None -> ())

let rec lookup_parent_map (ns:list tree) (k v:string)
  : Lemma (requires lookup k (parent_map ns) == Some v)
          (ensures is_after_child ns k v)
          (decreases ns)
  = match ns with
    | [] -> ()
    | p :: r ->
      lookup_app k (kid_pairs (tid_of p) (kids_of p)) (parent_map r);
      (match lookup k (parent_map r) with
       | Some _ -> lookup_parent_map r k v
       | None -> lookup_kid_pairs k (tid_of p) (kids_of p) v)

(* THE FOURTH TASK'S THEOREM. Every parent address the contained script carries resolves, in
   `after`, to a node that holds the addressed child and that `canHold` accepts — so no step it
   emits can be refused for containment, and the typed `TargetNotAContainer` is the exact price of
   that guarantee. Note what it does NOT say, and could not: `canHold` may read a node's CHILD LIST
   (Phase 140's `child_blind` counterexample), so the node the engine resolves at apply time is the
   same node only up to the children the script has already moved. The statement is about the
   parents the diff ADDRESSES, which is the half the diff can be responsible for.

   That boundary is exactly why a `toOps`-emitted insert is safe under an interior containment
   check as well: `diff_inserts_are_new_leaf_shells` says the graft is CHILDLESS, so a validator
   that walks the graft's interior for non-containers finds nothing to walk. *)
let diff_applicable_contained (ch:tree -> bool) (b a:tree) (o:op)
  : Lemma (requires Ok? (to_ops_contained ch b a) /\ mem o (Ok?._0 (to_ops_contained ch b a)))
          (ensures contained_shape ch (pre a) o)
  = diff_script_shape b a;
    all_ops_mem (script_shape b a) (Ok?._0 (to_ops b a)) o;
    match o with
    | InsertChild p n ->
      lookup_parent_map (pre a) (tid_of n) p;
      after_child_accepts ch (pre a) (tid_of n) p
    | MoveNode x np -> after_child_accepts ch (pre a) x np
    | _ -> ()
