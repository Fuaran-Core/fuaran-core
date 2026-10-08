(*
   Diff — the apply engine's companion in the other direction: `Diff.toOps` derives a script that
   turns one tree into another, and this module is that derivation modelled clause for clause
   (fuaran-core Phase 141).

   WHAT IS MODELLED. `Diff.fs`'s `Diff.toOps` — the two refusals, the four passes and the order they
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
       `after`, to a node that HOLDS the addressed child and that `canHold` accepts. That is a
       statement about the ADDRESSES, in `after`; it is NOT "the script cannot be refused for
       containment at any step", which this header claimed until Phase 305 and which is FALSE —
       the script runs against `before`'s kinds, and the structural diff carries no content, so a
       survivor that is a leaf in `before` and a container in `after` refuses the inserts under it
       (section 12, `contained_script_refused_at_before_kinds`, the exact pair the second-pass
       review measured at 41% of drawn pairs). The content-aware `Diff.toOpsContainedWith` is the
       form that applies under the predicate it checked; its run theorem is section 13's
       `diff_applicable_contained_run` (Phase 305, when the phase was re-opened to finish): the two
       update blocks modelled, section 10's induction REUSED at the recoloured tree rather than
       restated, the contained engine shown to agree with the plain one at every structural step.

     - the POSITIONAL facts (section 9, Phase 162), over `TreeOps.preorder_parent_first`: an
       insert's parent precedes its own node in `after`'s preorder — the source comment's
       "top-down", mechanised — a move's destination precedes the moved node, and therefore a
       move's destination is NOT inside the moved subtree in `after`, which is the consequence
       Phase 141 named when it deferred the two theorems below.

     - THE RECONSTRUCTION (section 10, Phase 167), which is what the two facts above were for:
       `diff_applicable` — every emitted step is ACCEPTED in sequence by `apply`, against the tree
       IN HAND at that step — and `diff_reconstructs` — `apply_all (to_ops b a) b == Ok a`, the
       tree and not a tree like it. Phase 141 deferred both as 141.t1 and Phase 162, having proved
       the positional lemma and found the gap had changed shape rather than closed, re-deferred
       them as 162.t3; section 10 is the invariant both deferrals named, one per pass, about how
       much of `after`'s structure each PREFIX of the script has built. `diff_reconstructs` holds
       under `kinds_agree` and `diff_applicable` under nothing extra, and the hypothesis is proved
       NECESSARY rather than assumed — see `kinds_agree_is_necessary`, and section 10's header for
       why the unrestricted form is false rather than merely unproved.

   WHAT IS NOT CLAIMED. `Diff.toOpsMoved` (fuaran-core#63) — not shipped, so there is no function
   to model clause for clause, and section 10's four `_run` lemmas are stated over `toOps`' four
   passes by name; a second emission strategy would inherit `tree_ext` and `Preservation.fst`
   section 11 but not the induction. `Ops.normalize`, containment LEGALITY and rejection PAYLOADS,
   as the README's theorem 6 ladder records. What is proved here is everything the four passes
   guarantee about the SCRIPT, about `after`, and now about applying it; what the differential
   still measures over independently generated pairs is that the SHIPPED engine does the same,
   which is a claim about production and not about the model.

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
(* Phase 167. Section 10's induction reasons about the INTERMEDIATE trees a script prefix has
   built, which is `ins` and `rem_at` over trees — `Preservation.fst`'s cost class and not this
   module's, so its section 11 carries those lemmas and this module uses them. The opens are
   collision-free in both directions (checked when the section landed): no name of `Preservation`
   shadows one of `TreeOps`, `DagFold` or this module. `proofs/oracle/` already compiles
   `Preservation.fs` before `TreeDiff.fs`, so the extraction order was right before it was
   needed. *)
open Preservation

(* The modules this one opens carry ninety-odd lemmas between them, most with SMT patterns that
   would otherwise be live at every query here. Scoped with `#set-options`, as `TreeOps.fst`,
   `JsonParse.fst` and `Preservation.fst` each are, and for the reason recorded there: pruning
   changes which facts a query can see, so it is a per-module decision and never a leg-wide flag. *)
#set-options "--ext context_pruning"

(* ======================================================================================
   1. Why a diff could not be produced (F#: `Diff.DiffError<'Id>` in Diff.fs).

      Three cases, and the third is `toOpsContained`'s alone — the plain `toOps` cannot raise it,
      which section 5 proves rather than asserts, in the shape `Preservation.apply_total` uses for
      the two rejection classes `apply` cannot reach.
   ====================================================================================== *)

type diff_error =
  | RootIdMismatch      : before:string -> after:string -> diff_error
  | DuplicateIdInTree   : string -> diff_error
  | TargetNotAContainer : target:string -> kind_tag:string -> diff_error

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
   3. The four passes (F#: `Diff.fs`'s `toOps`, one function per numbered comment block).
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

(* THE ORDER PASSES 1-3 LEAVE A PARENT IN (Phase 305, 305.t1) is a function of the two trees
   alone: its kept survivors in BEFORE-order (a before-child that is still its child), then the
   inserted shells, then the moved-in survivors, the last two each in AFTER-order — because an
   insert and a move both APPEND, pass 1 walks `after`'s preorder (so a parent's new children
   arrive in its child order), pass 2 walks a parent's child list in order, and a removal or a
   move-out deletes in place. Section 10's `ord1`/`ord2`/`ord3` carry that order through the
   three passes step by step, and `settled_run` is the lemma. F#: `settled` in `emitWith`'s
   step 4, clause for clause — `in_list ak` is `Set.contains c aKidSet` over the before-children,
   `new_pred` is `not (bIds.Contains c)`, `moved_pred` is `bIds.Contains c && not (bKidSet.Contains c)`. *)
let in_list (l:list string) (c:string) : Tot bool = mem c l
let new_pred (b_ids:list string) (c:string) : Tot bool = not (mem c b_ids)
let moved_pred (b_ids bk:list string) (c:string) : Tot bool = mem c b_ids && not (mem c bk)

let settled_order (b_ids bk ak:list string) : Tot (list string) =
  app (keep (in_list ak) bk) (app (keep (new_pred b_ids) ak) (keep (moved_pred b_ids bk) ak))

(* PASS 4 — order, last of all: every parent now holds exactly its after-children, so naming the
   after-order is a legal permutation. One op per CHANGED parent, none at all for a parent with
   fewer than two children, where every permutation is the identity, and — since Phase 305 — none
   for a parent the three passes have already LEFT in after-order (`settled_order`): the reorder
   that used to trail every append restated an order the tree already held. `bk` is the parent's
   before-children off the kid map (F#: `bChildKeys`), `[]` for a parent `before` does not carry. *)
let rec pass_reorders (b_ids:list string) (b_kids:list (string & list string)) (after:tree)
                      (ps:list string) : Tot (list op) (decreases ps) =
  match ps with
  | [] -> []
  | pid :: r ->
    (match find_in pid after with
     | Some p ->
       let ak = kid_ids (kids_of p) in
       let bk = (match lookup_kids pid b_kids with Some bk -> bk | None -> []) in
       if more_than_one (kids_of p) && settled_order b_ids bk ak <> ak
       then ReorderChildren pid ak :: pass_reorders b_ids b_kids after r
       else pass_reorders b_ids b_kids after r
     | None -> pass_reorders b_ids b_kids after r)

(* ======================================================================================
   4. `Diff.toOps` itself (F#: `Diff.fs`, clause for clause), and `Diff.toOpsContained`.

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
   pass_reorders b_ids b_kids after (snd p2))

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

(* F#: `Ops.firstUncontained canHold w after` (Phase 228; before it, the same body inline), which is
   `Tree.preorder w after |> List.tryFind (fun p -> not (List.isEmpty (w.Children p)) && not
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

      `Diff.fs`'s doc comment says: "The emitted order is always applyable: added nodes go in as leaf
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

let rec pass_reorders_all (b_ids:list string) (b_kids:list (string & list string)) (after:tree)
                          (ps:list string)
  : Lemma (ensures all_ops is_reorder (pass_reorders b_ids b_kids after ps)) (decreases ps)
  = match ps with
    | [] -> ()
    | _ :: r -> pass_reorders_all b_ids b_kids after r

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
    pass_reorders_all (ids b) (kid_map b_nodes) a (snd p2)

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

let rec pass_reorders_shape (b_ids:list string) (b_kids:list (string & list string)) (after:tree)
                            (ps:list string)
  : Lemma (ensures all_ops (reorder_shape after) (pass_reorders b_ids b_kids after ps)) (decreases ps)
  = match ps with
    | [] -> ()
    | _ :: r -> pass_reorders_shape b_ids b_kids after r

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
    let p4 = pass_reorders (ids b) b_kids a (snd (pass_moves (ids b) b_par b_kids a_nodes)) in
    pass_inserts_all a_par (ids b) a_nodes;
    pass_inserts_shape a_par (ids b) a_nodes;
    pass_moves_all (ids b) b_par b_kids a_nodes;
    pass_moves_shape (ids b) b_par b_kids a_nodes;
    pass_removes_all (ids a) b_par b_nodes;
    pass_removes_shape (ids a) b_par b_nodes;
    pass_reorders_all (ids b) b_kids a (snd (pass_moves (ids b) b_par b_kids a_nodes));
    pass_reorders_shape (ids b) b_kids a (snd (pass_moves (ids b) b_par b_kids a_nodes));
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
                      TargetNotAContainer?.target (Error?._0 (to_ops_contained ch b a)) == tid_of n /\
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


(* ======================================================================================
   9. THE POSITIONAL FACTS, over `TreeOps.preorder_parent_first` (Phase 162).

      Phase 141 named one missing fact and left `diff_reconstructs` and the operational
      `diff_applicable` at level 2 on it: "a parent precedes its children in preorder, so by the
      time the second pass emits a move while processing the destination, every after-ancestor of
      that destination is already placed and it cannot be inside the moved subtree." Phase 162
      proves that fact — `TreeOps.preorder_parent_first`, section 19 there — and this section is
      it INSTANTIATED AT THE DIFF: what the emitted operations say about the order of `after`.

      WHAT THIS SECTION CLOSES, and it is worth being exact because the two remaining theorems are
      NOT closed by it. Every parent address the script names is positioned in the walk the passes
      iterate: an insert's parent precedes its own node, a move's destination precedes the moved
      node, and — the consequence 141 named — a move's destination is NOT inside the moved node's
      subtree in `after`. Those are the obligations the reconstruction argument discharges at the
      AFTER tree, and they are now theorems rather than the prose of the source comment.

      WHAT IS STILL OPEN. `diff_reconstructs` (`apply_all (to_ops b a) b == Ok a`) and the
      operational `diff_applicable` are statements about the INTERMEDIATE trees the script builds,
      which is a different quantifier: `apply` checks `WouldNestUnderSelf` against the tree in
      hand at step k, not against `after`, and the tree in hand at step k is the before-tree with
      k operations run on it. Relating the two is the induction that remains, and it needs an
      invariant about how much of `after`'s structure each prefix of the script has already built.
      Both claims are carried at level 2 by the differential over independently generated pairs
      exactly as Phase 141 left them; the README's theorem 6 carries the boundary and the price.
      The facts below are the after-tree half, and stating them separately is what makes the
      remaining half a named induction rather than an unexamined gap.
   ====================================================================================== *)

(* ---- the walk contains what the tree contains ---- *)

let rec pre_sub_ids (t n:tree) (y:string)
  : Lemma (requires mem n (pre t) /\ mem y (ids n)) (ensures mem y (ids t)) (decreases t)
  = match t with
    | TNode _ _ cs -> if n = t then () else pre_all_sub_ids cs n y
and pre_all_sub_ids (ts:list tree) (n:tree) (y:string)
  : Lemma (requires mem n (pre_all ts) /\ mem y (ids n)) (ensures mem y (ids_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r ->
      mem_app n (pre c) (pre_all r);
      (if mem n (pre c) then pre_sub_ids c n y else pre_all_sub_ids r n y);
      mem_app y (ids c) (ids_all r)

(* ---- a node of the walk precedes its own subtree in the walk's id list ----

   `TreeOps.node_precedes_its_subtree` says this of a tree about itself; this says it of any node
   the walk reaches, inside the tree the walk is of. Well-formedness is used at exactly one step —
   lifting past an ancestor's own id — and that is the step at which a repeated id would make the
   claim false rather than merely unproved. *)

let rec pre_precedes (t:tree) (n:tree) (y:string)
  : Lemma (requires wf t /\ mem n (pre t) /\ mem y (ids_all (kids_of n)))
          (ensures precedes (tid_of n) y (ids t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      if n = t then ()
      else begin
        pre_all_precedes cs n y;
        pre_all_sub_ids cs n (tid_of n);
        pre_all_sub_ids cs n y
      end
and pre_all_precedes (ts:list tree) (n:tree) (y:string)
  : Lemma (requires wf_all ts /\ mem n (pre_all ts) /\ mem y (ids_all (kids_of n)))
          (ensures precedes (tid_of n) y (ids_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r ->
      mem_app n (pre c) (pre_all r);
      if mem n (pre c) then begin
        pre_precedes c n y;
        precedes_app_left (tid_of n) y (ids c) (ids_all r)
      end
      else begin
        pre_all_precedes r n y;
        pre_all_sub_ids r n (tid_of n);
        pre_all_sub_ids r n y;
        inter_nil_iff (ids c) (ids_all r);
        precedes_app_right (tid_of n) y (ids c) (ids_all r)
      end

(* ---- and `Tree.tryFind`'s answer is a node of the walk ---- *)

let rec find_in_mem_pre (x:string) (t:tree) (n:tree)
  : Lemma (requires find_in x t == Some n) (ensures mem n (pre t)) (decreases t)
  = match t with
    | TNode i _ cs -> if i = x then () else (find_all_mem_pre_all x cs n; mem_app n (pre_all cs) [])
and find_all_mem_pre_all (x:string) (ts:list tree) (n:tree)
  : Lemma (requires find_all x ts == Some n) (ensures mem n (pre_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r ->
      (match find_in x c with
       | Some m -> find_in_mem_pre x c n
       | None -> find_all_mem_pre_all x r n);
      mem_app n (pre c) (pre_all r)

(* ---- THE BRIDGE: an after-parent precedes its child in the walk the passes iterate ---- *)

let rec after_parent_precedes_in (a:tree) (ns:list tree) (x np:string)
  : Lemma (requires wf a /\ is_after_child ns x np /\
                    (forall (n:tree). mem n ns ==> mem n (pre a)))
          (ensures precedes np x (ids a)) (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r ->
      if tid_of n = np && mem x (kid_ids (kids_of n))
      then (kid_ids_sub x (kids_of n); pre_precedes a n x)
      else after_parent_precedes_in a r x np

let after_parent_precedes (a:tree) (x np:string)
  : Lemma (requires wf a /\ is_after_child (pre a) x np)
          (ensures precedes np x (ids a))
  = after_parent_precedes_in a (pre a) x np

(* ---- the three theorems ---- *)

(* PASS 1 IS TOP-DOWN, mechanised. Every insert the script emits names a parent that precedes the
   inserted node in `after`'s own preorder — which is the walk pass 1 iterates, so by the time it
   reaches an added child it has already emitted its added parent. That is the source comment's
   "added nodes go in as leaf shells (top-down)", stated about the emitted operations rather than
   about the loop. *)
let diff_insert_parent_precedes (b a:tree) (o:op)
  : Lemma (requires wf a /\ Ok? (to_ops b a) /\ mem o (Ok?._0 (to_ops b a)) /\ InsertChild? o)
          (ensures precedes (InsertChild?.parent o)
                            (tid_of (InsertChild?.node o))
                            (ids a))
  = diff_inserts_are_new_leaf_shells b a o;
    match o with
    | InsertChild p n ->
      lookup_parent_map (pre a) (tid_of n) p;
      after_parent_precedes a (tid_of n) p

(* PASS 2's destination is likewise positioned: a move names a destination that precedes the moved
   node in `after`. `diff_moves_are_survivor_reattachments` already says the destination HOLDS the
   moved node in `after`; this is where that sits in the walk. *)
let diff_move_destination_precedes (b a:tree) (o:op)
  : Lemma (requires wf a /\ Ok? (to_ops b a) /\ mem o (Ok?._0 (to_ops b a)) /\ MoveNode? o)
          (ensures precedes (MoveNode?.new_parent o) (MoveNode?.target o) (ids a))
  = diff_moves_are_survivor_reattachments b a o;
    match o with
    | MoveNode x np -> after_parent_precedes a x np

(* AND THE CONSEQUENCE PHASE 141 NAMED: a move's destination is not inside the moved subtree.
   In `after` the destination is the moved node's parent, so it precedes it; a node inside the
   moved subtree is preceded BY it; and `precedes` is antisymmetric and — on an id-unique tree —
   irreflexive, so the two cannot both hold.

   READ THE QUANTIFIER. This is about `after`, which is the tree the diff derived the move FROM.
   `Ops.apply` checks `WouldNestUnderSelf` against the tree IN HAND when the move runs, which is
   the before-tree with the script's earlier operations applied. Carrying this fact across to that
   tree is the induction `diff_applicable` still needs; what is settled here is that the fact is
   true where the diff could see it, which is the half the diff can be responsible for — the same
   boundary `diff_applicable_contained` draws in section 8, for the same reason. *)
let diff_move_destination_is_outside (b a:tree) (xn:tree) (o:op)
  : Lemma (requires wf a /\ Ok? (to_ops b a) /\ mem o (Ok?._0 (to_ops b a)) /\ MoveNode? o /\
                    find_in (MoveNode?.target o) a == Some xn)
          (ensures not (mem (MoveNode?.new_parent o) (ids_all (kids_of xn))))
  = diff_move_destination_precedes b a o;
    match o with
    | MoveNode x np ->
      if mem np (ids_all (kids_of xn)) then begin
        find_in_mem_pre x a xn;
        find_in_id x a xn;
        pre_precedes a xn np;
        precedes_antisym np x (ids a);
        wf_iff_no_dups a;
        precedes_irrefl x (ids a)
      end
      else ()

(* ---- and the three are NOT VACUOUS, evaluated on a pair that exercises both ----

   `before` is a root with one child; `after` adds a container above it and moves the child
   inside. The diff emits exactly one insert and exactly one move, so both theorems above have a
   witness rather than a quantifier over nothing — and the last conjunct is the direction check:
   the moved node does NOT precede its destination, so the positional claim is an ordering fact
   and not a tautology. It goes red if the four passes ever stop emitting this pair's script. *)

let pos_before : tree = TNode "root" "doc" [ TNode "p" "para" [] ]

let pos_after : tree = TNode "root" "doc" [ TNode "q" "sec" [ TNode "p" "para" [] ] ]

let positional_facts_are_not_vacuous ()
  : Lemma (ensures wf pos_before /\ wf pos_after /\
                   to_ops pos_before pos_after ==
                     Ok [ InsertChild "root" (TNode "q" "sec" []); MoveNode "p" "q" ] /\
                   precedes "root" "q" (ids pos_after) /\
                   precedes "q" "p" (ids pos_after) /\
                   ~(precedes "p" "q" (ids pos_after)))
  = assert_norm (wf pos_before);
    assert_norm (wf pos_after);
    assert_norm (to_ops pos_before pos_after ==
                 Ok [ InsertChild "root" (TNode "q" "sec" []); MoveNode "p" "q" ]);
    assert_norm (precedes "root" "q" (ids pos_after));
    assert_norm (precedes "q" "p" (ids pos_after));
    assert_norm (not (precedes "p" "q" (ids pos_after)))

(* ======================================================================================
   10. THE RECONSTRUCTION, AT LEVEL 1 (Phase 167) — the induction two deferrals named.

       Section 9 proves the positional facts about `after`. What `apply` asks at each step is
       about the tree IN HAND — the before-tree with the script's earlier operations already
       run on it — and carrying one across to the other is what Phase 141 deferred as 141.t1
       and Phase 162, having proved the positional lemma and found the gap had changed shape
       rather than closed, re-deferred as 162.t3. This section is that induction.

       THE SHAPE. Four invariants, one per pass, each an `inv` predicate over the tree in hand
       and the suffix of that pass's worklist still to run, with a `_run` lemma stepping the
       whole pass and a `_to_` lemma handing the next pass its entry condition:

         inv1  (`inserts_run`)  every already-placed node of `after` is in the tree with its
                                after-parent, every not-yet-placed one is where `before` had it
         inv2  (`moves_run`)    every survivor is under its after-parent — the pass that walks
                                `after`'s preorder and reattaches, with a second worklist over
                                the kid list of the parent in hand
         inv3  (`removes_run`)  every node `after` does not carry is gone
         inv4  (`reorders_run`) every parent's child list is `after`'s, in `after`'s order

       `diff_run` chains the four over `apply_all_app`, and the theorems read off its conclusion:
       `diff_applicable` from `inv4` being reached at all (so every step was ACCEPTED in
       sequence, which is the operational statement, distinct from section 8's
       `diff_applicable_contained` about the addresses a contained script carries), and
       `diff_reconstructs` through `tree_ext` — a tree whose every node agrees with `after`'s on
       kind and child ids IS `after`, by induction on the tree rather than on the script.

       THE HYPOTHESIS, and it is NOT a convenience. `diff_reconstructs` requires `kinds_agree`:
       a node id the two trees share names the same kind in both. The unrestricted form is
       FALSE, not merely unproved — no skeleton operation edits a node, so a shared id whose
       kind differs is unreconstructible by ANY script this alphabet can express, and
       `kinds_agree_is_necessary` below pins a concrete such pair whose diff is accepted at
       every step and lands on a tree that is not `after`. This is the same precondition the
       differential's pool has carried since Phase 141 (a node's content is a function of its
       id) and the same one the README's level-3 ladder already records as assumed; what
       changed is that it is now a hypothesis in a proved statement instead of a property of a
       generator. `diff_applicable` needs no such hypothesis and carries none.

       `reconstruction_is_not_vacuous` evaluates a pair whose script carries one of each of the
       four operations, and re-evaluates section 9's positional pair, so neither theorem is a
       statement about the empty script.
   ====================================================================================== *)
let rec precedes_prefix (p x:string) (l m:list string)
  : Lemma (requires precedes p x (app l (x :: m)) /\ no_dups (app l (x :: m)))
          (ensures mem p l) (decreases l)
  = match l with
    | [] -> ()
    | h :: t ->
      mem_app x t (x :: m);
      if h = p then () else if h = x then () else precedes_prefix p x t m

let rec parent_map_app (l m:list tree)
  : Lemma (ensures parent_map (app l m) == app (parent_map l) (parent_map m)) (decreases l)
  = match l with
    | [] -> ()
    | p :: r ->
      parent_map_app r m;
      app_assoc (kid_pairs (tid_of p) (kids_of p)) (parent_map r) (parent_map m)

let rec lookup_kid_pairs_some (k pk:string) (cs:list tree)
  : Lemma (requires mem k (kid_ids cs)) (ensures Some? (lookup k (kid_pairs pk cs))) (decreases cs)
  = match cs with
    | [] -> ()
    | c :: r -> if mem k (kid_ids r) then lookup_kid_pairs_some k pk r else ()

let rec lookup_some (t:tree) (k:string)
  : Lemma (requires mem k (ids_all (kids_of t)))
          (ensures Some? (lookup k (parent_map (pre t)))) (decreases t)
  = match t with
    | TNode i _ cs ->
      lookup_app k (kid_pairs i cs) (parent_map (pre_all cs));
      lookup_some_all i cs k
and lookup_some_all (pk:string) (cs:list tree) (k:string)
  : Lemma (requires mem k (ids_all cs))
          (ensures Some? (lookup k (kid_pairs pk cs)) \/ Some? (lookup k (parent_map (pre_all cs))))
          (decreases cs)
  = match cs with
    | [] -> ()
    | c :: r ->
      mem_app k (ids c) (ids_all r);
      parent_map_app (pre c) (pre_all r);
      lookup_app k (parent_map (pre c)) (parent_map (pre_all r));
      if mem k (ids c) then begin
        match c with
        | TNode ci _ ccs ->
          if k = ci then lookup_kid_pairs_some k pk (c :: r)
          else lookup_some c k
      end
      else lookup_some_all pk r k


(* ================= bridges between the diff maps and the per-node view ================= *)

let rec pre_find (t:tree) (n:tree)
  : Lemma (requires wf t /\ mem n (pre t)) (ensures find_in (tid_of n) t == Some n) (decreases t)
  = match t with
    | TNode i _ cs ->
      if n = t then ()
      else begin
        pre_all_sub_ids cs n (tid_of n);
        pre_all_find cs n
      end
and pre_all_find (ts:list tree) (n:tree)
  : Lemma (requires wf_all ts /\ mem n (pre_all ts)) (ensures find_all (tid_of n) ts == Some n)
          (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r ->
      mem_app n (pre c) (pre_all r);
      inter_nil_iff (ids c) (ids_all r);
      find_in_some_iff (tid_of n) c;
      if mem n (pre c) then pre_find c n
      else begin
        pre_all_sub_ids r n (tid_of n);
        pre_all_find r n
      end

let kids_at_pre (a:tree) (n:tree)
  : Lemma (requires wf a /\ mem n (pre a))
          (ensures kids_at (tid_of n) a == kid_ids (kids_of n) /\
                   kind_at (tid_of n) a == Some (kind_of n))
  = pre_find a n

let kid_unique (t:tree) (q1 q2 c:string)
  : Lemma (requires wf t /\ mem c (kids_at q1 t) /\ mem c (kids_at q2 t)) (ensures q1 == q2)
  = (match find_in q1 t with Some m -> parent_kids q1 c t m | None -> ());
    (match find_in q2 t with Some m -> parent_kids q2 c t m | None -> ())

let kids_in_ids (t:tree) (q c:string)
  : Lemma (requires mem c (kids_at q t)) (ensures mem c (ids t) /\ mem q (ids t))
  = find_in_some_iff q t;
    match find_in q t with
    | Some m -> kid_ids_sub c (kids_of m); (match m with TNode _ _ _ -> ()); find_in_sub q c t m
    | None -> ()

let kid_not_root (t:tree) (q c:string)
  : Lemma (requires wf t /\ mem c (kids_at q t)) (ensures c <> tid_of t)
  = match find_in q t with
    | Some m ->
      parent_kids q c t m;
      (match t with
       | TNode i _ cs ->
         if has_kid c cs then (has_kid_mem c cs; kid_ids_sub c cs)
         else parent_all_mem c cs q)
    | None -> ()

(* the first node of a list holding `x` among its children, and the first carrying an id *)
let rec kid_holder (ns:list tree) (x:string) : Tot (option tree) (decreases ns) =
  match ns with
  | [] -> None
  | n :: r -> if mem x (kid_ids (kids_of n)) then Some n else kid_holder r x

let rec kid_holder_some (ns:list tree) (x:string) (h:tree)
  : Lemma (requires kid_holder ns x == Some h)
          (ensures mem h ns /\ mem x (kid_ids (kids_of h)) /\ is_after_child ns x (tid_of h))
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r -> if mem x (kid_ids (kids_of n)) then () else kid_holder_some r x h

let rec kid_holder_intro (ns:list tree) (x:string) (h:tree)
  : Lemma (requires mem h ns /\ mem x (kid_ids (kids_of h))) (ensures Some? (kid_holder ns x))
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r -> if mem x (kid_ids (kids_of n)) then () else kid_holder_intro r x h

let rec node_with (ns:list tree) (k:string) : Tot (option tree) (decreases ns) =
  match ns with
  | [] -> None
  | n :: r -> if tid_of n = k then Some n else node_with r k

let rec node_with_some (ns:list tree) (k:string)
  : Lemma (requires mem k (tids ns))
          (ensures (match node_with ns k with
                    | Some n -> mem n ns /\ tid_of n == k
                    | None -> False)) (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r -> if tid_of n = k then () else node_with_some r k

let rec tids_mem (ns:list tree) (n:tree)
  : Lemma (requires mem n ns) (ensures mem (tid_of n) (tids ns)) (decreases ns)
  = match ns with
    | [] -> ()
    | m :: r -> if m = n then () else tids_mem r n

let rec after_child_kids_at (a:tree) (ns:list tree) (c v:string)
  : Lemma (requires wf a /\ is_after_child ns c v /\ (forall (n:tree). mem n ns ==> mem n (pre a)))
          (ensures mem c (kids_at v a)) (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r ->
      if tid_of n = v && mem c (kid_ids (kids_of n)) then kids_at_pre a n
      else after_child_kids_at a r c v

(* M1: the last-wins parent map agrees with the tree, both ways *)
let lookup_is_parent (a:tree) (c v:string)
  : Lemma (requires wf a /\ lookup c (parent_map (pre a)) == Some v)
          (ensures mem c (kids_at v a))
  = lookup_parent_map (pre a) c v;
    after_child_kids_at a (pre a) c v

let parent_is_lookup (a:tree) (c v:string)
  : Lemma (requires wf a /\ mem c (kids_at v a))
          (ensures lookup c (parent_map (pre a)) == Some v)
  = kid_not_root a v c;
    kids_in_ids a v c;
    (match a with TNode _ _ _ -> lookup_some a c);
    match lookup c (parent_map (pre a)) with
    | Some v' -> lookup_is_parent a c v'; kid_unique a v v' c
    | None -> ()

(* M2: the last-wins child-key map agrees with the tree *)
let rec lookup_kids_sound (b:tree) (ns:list tree) (k:string) (v:list string)
  : Lemma (requires wf b /\ (forall (n:tree). mem n ns ==> mem n (pre b)) /\
                    lookup_kids k (kid_map ns) == Some v)
          (ensures v == kids_at k b) (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r ->
      (match lookup_kids k (kid_map r) with
       | Some _ -> lookup_kids_sound b r k v
       | None -> kids_at_pre b n)

let rec lookup_kids_some (ns:list tree) (k:string)
  : Lemma (requires mem k (tids ns)) (ensures Some? (lookup_kids k (kid_map ns))) (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r -> if mem k (tids r) then lookup_kids_some r k else ()

let lookup_kids_is (b:tree) (k:string)
  : Lemma (requires wf b /\ mem k (ids b))
          (ensures lookup_kids k (kid_map (pre b)) == Some (kids_at k b))
  = ids_is_pre b;
    lookup_kids_some (pre b) k;
    match lookup_kids k (kid_map (pre b)) with
    | Some v -> lookup_kids_sound b (pre b) k v
    | None -> ()

(* ================= the positional facts a SUFFIX of the walk satisfies ================= *)

let rec precedes_not_back (v k:string) (l m:list string)
  : Lemma (requires precedes v k (app l m) /\ mem k l /\ mem v m /\ no_dups (app l m))
          (ensures False) (decreases l)
  = match l with
    | [] -> ()
    | h :: t ->
      mem_app v t m;
      if h = v then () else if h = k then () else precedes_not_back v k t m

let rec is_after_child_app_r (l m:list tree) (x v:string)
  : Lemma (requires is_after_child m x v) (ensures is_after_child (app l m) x v) (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> is_after_child_app_r t m x v

let rec mem_app_r_tree (l m:list tree) (n:tree)
  : Lemma (requires mem n m) (ensures mem n (app l m)) (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> mem_app_r_tree t m n

(* a node that a suffix of the walk holds as a child is itself in that suffix, is not in the
   prefix, and is not the head of the suffix *)
let sfx_holder (a:tree) (done ns:list tree) (x:string) (h:tree)
  : Lemma (requires wf a /\ pre a == app done ns /\ kid_holder ns x == Some h)
          (ensures mem x (tids ns) /\ not (mem x (tids done)) /\
                   (match ns with n :: _ -> x <> tid_of n | [] -> True))
  = kid_holder_some ns x h;
    is_after_child_app_r done ns x (tid_of h);
    after_parent_precedes a x (tid_of h);
    ids_is_pre a;
    tids_app done ns;
    wf_iff_no_dups a;
    tids_mem ns h;
    mem_app_r_tree done ns h;
    kids_at_pre a h;
    kids_in_ids a (tid_of h) x;
    mem_app x (tids done) (tids ns);
    (if mem x (tids done) then precedes_not_back (tid_of h) x (tids done) (tids ns) else ());
    match ns with
    | [] -> ()
    | n :: r ->
      if x = tid_of n then begin
        precedes_prefix (tid_of h) x (tids done) (tids r);
        no_dups_app (tids done) (tids ns);
        inter_nil_iff (tids done) (tids ns)
      end
      else ()

let rec apply_all_app (l m:list op) (t:tree)
  : Lemma (ensures apply_all (app l m) t ==
                   (match apply_all l t with
                    | Ok t' -> apply_all m t'
                    | Error e -> Error e)) (decreases l)
  = match l with
    | [] -> ()
    | o :: r -> (match apply o t with
                 | Ok t' -> apply_all_app r m t'
                 | Error _ -> ())

(* ================= the list algebra the ORDER invariants stand on (Phase 305) ================= *)

(* Section 10's four invariants were written about MEMBERSHIP — which children a parent holds
   after a prefix of the script — because that is all `diff_applicable` and `diff_reconstructs`
   needed: pass 4 restated every changed parent's order, so no earlier pass had to be exact about
   it. Dropping the reorder that trails an append (305.t1) is what makes the order of passes 1-3
   load-bearing, and `ord1`/`ord2`/`ord3` below track it EXACTLY, beside the membership
   invariants rather than inside them, so the queries that were green stay the queries they were.
   Each is a `keep` over one of the two trees' child lists under a predicate that names the pass's
   worklist, and these are the facts about `keep` and `drop_id` the steps assemble with. *)

let rec keep_app (f:string -> bool) (l m:list string)
  : Lemma (ensures keep f (app l m) == app (keep f l) (keep f m)) (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> keep_app f t m

(* `DagFold.keep_ext` wants agreement everywhere; the invariants' predicates agree only on the
   list in hand, which is all `keep` reads. *)
let rec keep_ext_mem (f g:string -> bool) (l:list string)
  : Lemma (requires forall (c:string). mem c l ==> f c == g c)
          (ensures keep f l == keep g l) (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> keep_ext_mem f g t

let rec keep_none (f:string -> bool) (l:list string)
  : Lemma (requires forall (c:string). mem c l ==> not (f c)) (ensures keep f l == []) (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> keep_none f t

let rec keep_all (f:string -> bool) (l:list string)
  : Lemma (requires forall (c:string). mem c l ==> f c) (ensures keep f l == l) (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> keep_all f t

let rec drop_id_app (x:string) (l m:list string)
  : Lemma (ensures drop_id x (app l m) == app (drop_id x l) (drop_id x m)) (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> drop_id_app x t m

(* deleting one id from a kept list is keeping under the predicate that also excludes it *)
let rec keep_drop (f g:string -> bool) (k:string) (l:list string)
  : Lemma (requires forall (c:string). mem c l ==> g c == (f c && c <> k))
          (ensures drop_id k (keep f l) == keep g l) (decreases l)
  = match l with
    | [] -> ()
    | _ :: t -> keep_drop f g k t

let rec precedes_mem (p x:string) (l:list string)
  : Lemma (requires precedes p x l) (ensures mem p l /\ mem x l) (decreases l)
  = match l with
    | [] -> ()
    | h :: r -> if h = p then () else if h = x then () else precedes_mem p x r

(* the mirror of `precedes_prefix`: what an id precedes in a duplicate-free list is in the
   suffix after it *)
let rec precedes_suffix (k c:string) (l m:list string)
  : Lemma (requires precedes k c (app l (k :: m)) /\ no_dups (app l (k :: m)))
          (ensures mem c m) (decreases l)
  = match l with
    | [] -> ()
    | h :: t ->
      mem_app k t (k :: m);
      if h = k then () else if h = c then () else precedes_suffix k c t m

(* THE APPEND LEMMA. Widening a filter by exactly one id `k` of a duplicate-free list — where
   nothing the list holds AFTER `k` passes the wider filter — appends `k` to the narrower
   selection. It is how an insert's shell and a move's survivor land at the END of their
   segment: pass 1 reaches a parent's new children in child order, pass 2 reaches a parent's
   kids in child order, so at each step the ids later in the list are still on the worklist. *)
let rec keep_split_at (f g:string -> bool) (k:string) (l:list string)
  : Lemma (requires no_dups l /\ mem k l /\ not (f k) /\ g k /\
                    (forall (c:string). mem c l /\ c <> k ==> f c == g c) /\
                    (forall (c:string). mem c l /\ precedes k c l ==> not (g c)))
          (ensures keep g l == app (keep f l) [k]) (decreases l)
  = match l with
    | [] -> ()
    | h :: t ->
      if h = k then begin
        keep_none g t;
        keep_none f t
      end
      else begin
        let later (c:string)
          : Lemma (mem c t /\ precedes k c t ==> not (g c))
          = if mem c t && precedes k c t then precedes_mem k c t else ()
        in
        FStar.Classical.forall_intro later;
        keep_split_at f g k t
      end

(* ---- sibling order is preorder order ---- *)

(* `pre_precedes` carries a node's precedence over its SUBTREE up to the whole walk; this carries
   any precedence inside a node's child ids up the same way. The structure is `pre_precedes`'s
   exactly, including the one well-formedness step. *)
let rec kids_precede_all (cs:list tree) (c1 c2:string)
  : Lemma (requires wf_all cs /\ precedes c1 c2 (kid_ids cs))
          (ensures precedes c1 c2 (ids_all cs)) (decreases cs)
  = match cs with
    | [] -> ()
    | c :: r ->
      inter_nil_iff (ids c) (ids_all r);
      (match c with
       | TNode ci _ _ ->
         if ci = c1 then begin
           kid_ids_sub c2 r;
           precedes_app_split c1 c2 (ids c) (ids_all r)
         end
         else if ci = c2 then ()
         else begin
           kids_precede_all r c1 c2;
           precedes_mem c1 c2 (ids_all r);
           precedes_app_right c1 c2 (ids c) (ids_all r)
         end)

let rec pre_precedes_within (t:tree) (n:tree) (p x:string)
  : Lemma (requires wf t /\ mem n (pre t) /\ precedes p x (ids_all (kids_of n)))
          (ensures precedes p x (ids t)) (decreases t)
  = match t with
    | TNode i _ cs ->
      if n = t then precedes_mem p x (ids_all cs)
      else begin
        pre_all_precedes_within cs n p x;
        precedes_mem p x (ids_all cs)
      end
and pre_all_precedes_within (ts:list tree) (n:tree) (p x:string)
  : Lemma (requires wf_all ts /\ mem n (pre_all ts) /\ precedes p x (ids_all (kids_of n)))
          (ensures precedes p x (ids_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | c :: r ->
      mem_app n (pre c) (pre_all r);
      if mem n (pre c) then begin
        pre_precedes_within c n p x;
        precedes_app_left p x (ids c) (ids_all r)
      end
      else begin
        pre_all_precedes_within r n p x;
        precedes_mem p x (ids_all r);
        inter_nil_iff (ids c) (ids_all r);
        precedes_app_right p x (ids c) (ids_all r)
      end

(* two children of one node of `after`, in child order, are in that order in `after`'s walk *)
let kids_precede (a:tree) (n:tree) (c1 c2:string)
  : Lemma (requires wf a /\ mem n (pre a) /\ precedes c1 c2 (kid_ids (kids_of n)))
          (ensures precedes c1 c2 (ids a))
  = pre_find a n;
    find_in_wf (tid_of n) a n;
    (match n with TNode _ _ cs -> kids_precede_all cs c1 c2);
    pre_precedes_within a n c1 c2

(* ================= the prefix invariant, and pass 1 ================= *)

(* which tree a node's PARENT is currently read from: `after` once the script has placed it *)
let side (b a:tree) (placed:bool) (q c:string) : Tot bool =
  if placed then mem c (kids_at q a) else mem c (kids_at q b)

let kind_src (b a:tree) (q:string) : Tot (option string) =
  if mem q (ids b) then kind_at q b else kind_at q a

let same_kids (b a:tree) (q:string) : Tot bool =
  mem q (ids a) && mem q (ids b) && kids_at q b = kids_at q a

let cond1 (b a:tree) (ns:list tree) (c:string) : Tot bool =
  mem c (ids a) && not (mem c (ids b)) && not (mem c (tids ns))

let inv1 (b a:tree) (ns:list tree) (t:tree) : prop =
  wf t /\ tid_of t == tid_of a /\
  (forall (y:string). mem y (ids t) == (mem y (ids b) || (mem y (ids a) && not (mem y (tids ns))))) /\
  (forall (q c:string). mem c (kids_at q t) == side b a (cond1 b a ns c) q c) /\
  (forall (q:string). mem q (ids t) ==> kind_at q t == kind_src b a q) /\
  (forall (q:string). same_kids b a q ==> kids_at q t == kids_at q b)

let absent_no_kids (t:tree) (q:string)
  : Lemma (requires not (mem q (ids t))) (ensures kids_at q t == [] /\ kind_at q t == None)
  = find_in_some_iff q t

(* ---- the ORDER pass 1 leaves (Phase 305): before's children, then the shells placed so far,
   in after-order ---- *)

(* an added child the walk has already reached *)
let new1 (b_ids:list string) (ns:list tree) (c:string) : Tot bool =
  not (mem c b_ids) && not (mem c (tids ns))

[@@"opaque_to_smt"]
let ord1 (b a:tree) (ns:list tree) (t:tree) : prop =
  forall (q:string). mem q (ids a) ==>
    kids_at q t == app (kids_at q b) (keep (new1 (ids b) ns) (kids_at q a))

let ord1_init (b a:tree)
  : Lemma (requires wf a) (ensures ord1 b a (pre a) b)
  = reveal_opaque (`%ord1) (ord1 b a (pre a) b);
    ids_is_pre a;
    let aux (q:string)
      : Lemma (mem q (ids a) ==> kids_at q b == app (kids_at q b) (keep (new1 (ids b) (pre a)) (kids_at q a)))
      = if mem q (ids a) then begin
          FStar.Classical.forall_intro (FStar.Classical.move_requires (kids_in_ids a q));
          keep_none (new1 (ids b) (pre a)) (kids_at q a);
          app_nil_r (kids_at q b)
        end
        else ()
    in
    FStar.Classical.forall_intro aux

(* a node `before` carries is skipped by pass 1, and the worklist's head leaving changes the
   filter at that node alone, where it is false either side *)
let ord1_skip (b a:tree) (n:tree) (r:list tree) (t:tree)
  : Lemma (requires ord1 b a (n :: r) t /\ mem (tid_of n) (ids b)) (ensures ord1 b a r t)
  = reveal_opaque (`%ord1) (ord1 b a (n :: r) t);
    reveal_opaque (`%ord1) (ord1 b a r t);
    let aux (q:string)
      : Lemma (keep (new1 (ids b) (n :: r)) (kids_at q a) == keep (new1 (ids b) r) (kids_at q a))
      = keep_ext (new1 (ids b) (n :: r)) (new1 (ids b) r) (kids_at q a)
    in
    FStar.Classical.forall_intro aux

#push-options "--z3rlimit 200 --fuel 2 --ifuel 1"
(* the shell lands at the END of its parent's list, and that end is where `after` has it among
   the children placed so far: the children after it in `after`'s child order are later in the
   walk (`kids_precede`), so still on the worklist *)
let ins_ord_core (b a:tree) (done:list tree) (n:tree) (r:list tree) (t:tree) (pid:string)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done (n :: r) /\
                    inv1 b a (n :: r) t /\ ord1 b a (n :: r) t /\ not (mem (tid_of n) (ids b)) /\
                    lookup (tid_of n) (parent_map (pre a)) == Some pid /\
                    mem pid (ids t) /\ not (mem (tid_of n) (ids t)))
          (ensures ord1 b a r (ins pid (shell n) t))
  = reveal_opaque (`%ord1) (ord1 b a (n :: r) t);
    reveal_opaque (`%ord1) (ord1 b a r (ins pid (shell n) t));
    let k = tid_of n in
    let t' = ins pid (shell n) t in
    let f = new1 (ids b) (n :: r) in
    let g = new1 (ids b) r in
    ids_is_pre a;
    tids_app done (n :: r);
    wf_iff_no_dups a;
    no_dups_app (tids done) (tids (n :: r));
    inter_nil_iff (tids done) (tids (n :: r));
    mem_app_r_tree done (n :: r) n;
    lookup_is_parent a k pid;
    kids_in_ids a pid k;
    kids_at_pre a n;
    absent_no_kids b k;
    find_in_some_iff pid a;
    (match find_in pid a with
     | Some pn ->
       find_in_mem_pre pid a pn;
       kids_at_no_dups pid a;
       let later (c:string)
         : Lemma (mem c (kids_at pid a) /\ precedes k c (kids_at pid a) ==> not (g c))
         = if mem c (kids_at pid a) && precedes k c (kids_at pid a) then begin
             kids_precede a pn k c;
             precedes_suffix k c (tids done) (tids r)
           end
           else ()
       in
       FStar.Classical.forall_intro later;
       keep_split_at f g k (kids_at pid a);
       app_assoc (kids_at pid b) (keep f (kids_at pid a)) [k]
     | None -> ());
    let aux (q:string)
      : Lemma (mem q (ids a) ==> kids_at q t' == app (kids_at q b) (keep g (kids_at q a)))
      = if mem q (ids a) then begin
          if q = k then begin
            ins_view_inside pid (shell n) t k;
            let under (c:string)
              : Lemma (mem c (kids_at k a) ==> not (g c))
              = if mem c (kids_at k a) then sfx_holder a done (n :: r) c n else ()
            in
            FStar.Classical.forall_intro under;
            keep_none g (kids_at k a)
          end
          else begin
            ins_view pid (shell n) t q;
            if q = pid then ()
            else begin
              (if mem k (kids_at q a) then kid_unique a q pid k else ());
              keep_ext_mem f g (kids_at q a)
            end
          end
        end
        else ()
    in
    FStar.Classical.forall_intro aux

let ins_step (b a:tree) (done:list tree) (n:tree) (r:list tree) (t:tree) (pid:string)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done (n :: r) /\
                    inv1 b a (n :: r) t /\ ord1 b a (n :: r) t /\ not (mem (tid_of n) (ids b)) /\
                    lookup (tid_of n) (parent_map (pre a)) == Some pid)
          (ensures apply (InsertChild pid (shell n)) t == Ok (ins pid (shell n) t) /\
                   inv1 b a r (ins pid (shell n) t) /\ ord1 b a r (ins pid (shell n) t))
  = let k = tid_of n in
    let t' = ins pid (shell n) t in
    ids_is_pre a;
    tids_app done (n :: r);
    wf_iff_no_dups a;
    no_dups_app (tids done) (tids (n :: r));
    inter_nil_iff (tids done) (tids (n :: r));
    mem_app_r_tree done (n :: r) n;
    mem_app k (tids done) (tids (n :: r));
    lookup_is_parent a k pid;
    kids_in_ids a pid k;
    lookup_parent_map (pre a) k pid;
    after_parent_precedes a k pid;
    precedes_prefix pid k (tids done) (tids r);
    assert (mem pid (ids t));
    assert (not (mem k (ids t)));
    ins_ord_core b a done n r t pid;
    first_dup_none_iff (shell n) t;
    inter_nil_iff (ids (shell n)) (ids t);
    assert (apply (InsertChild pid (shell n)) t == Ok t');
    apply_preserves_wf (InsertChild pid (shell n)) t;
    ids_ins_mem_fa pid (shell n) t;
    tid_ins pid (shell n) t;
    kids_at_pre a n;
    absent_no_kids b k;
    let aux_k (q c:string)
      : Lemma (mem c (kids_at q t') == side b a (cond1 b a r c) q c)
      = if q = k then begin
          ins_view_inside pid (shell n) t k;
          if mem c (kid_ids (kids_of n)) then sfx_holder a done (n :: r) c n else ()
        end
        else begin
          ins_view pid (shell n) t q;
          mem_app c (kids_at q t) [k];
          if c = k then begin
            (if mem k (kids_at q t) then kids_in_ids t q k else ());
            (if mem k (kids_at q a) then kid_unique a q pid k else ())
          end
          else ()
        end
    in
    let aux_n (q:string)
      : Lemma (mem q (ids t') ==> kind_at q t' == kind_src b a q)
      = if q = k then ins_view_inside pid (shell n) t k
        else ins_view pid (shell n) t q
    in
    let aux_x (q:string)
      : Lemma (same_kids b a q ==> kids_at q t' == kids_at q b)
      = if same_kids b a q then begin
          ins_view pid (shell n) t q;
          if q = pid then kids_in_ids b pid k else ()
        end
        else ()
    in
    FStar.Classical.forall_intro_2 aux_k;
    FStar.Classical.forall_intro aux_n;
    FStar.Classical.forall_intro aux_x

let inv1_skip (b a:tree) (n:tree) (r:list tree) (t:tree)
  : Lemma (requires inv1 b a (n :: r) t /\ mem (tid_of n) (ids b))
          (ensures inv1 b a r t)
  = ()

let rec inserts_run (b a:tree) (done ns:list tree) (t:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done ns /\
                    inv1 b a ns t /\ ord1 b a ns t)
          (ensures (match apply_all (pass_inserts (parent_map (pre a)) (ids b) ns) t with
                    | Ok t1 -> inv1 b a [] t1 /\ ord1 b a [] t1
                    | Error _ -> False))
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r ->
      let k = tid_of n in
      app_assoc done [n] r;
      if mem k (ids b) then begin
        inv1_skip b a n r t;
        ord1_skip b a n r t;
        inserts_run b a (app done [n]) r t
      end
      else begin
        match lookup k (parent_map (pre a)) with
        | Some pid ->
          ins_step b a done n r t pid;
          inserts_run b a (app done [n]) r (ins pid (shell n) t)
        | None ->
          ids_is_pre a;
          tids_app done (n :: r);
          mem_app k (tids done) (tids (n :: r));
          (match a with
           | TNode ai _ acs -> if k = ai then () else lookup_some a k)
      end
#pop-options

let inv1_init (b a:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a) (ensures inv1 b a (pre a) b)
  = ids_is_pre a

(* ================= pass 2: the moves ================= *)

(* a survivor still waiting for its move: a child of the parent in hand (`rest`), or of a parent
   the walk has not reached yet (`ns`) *)
let cond2 (b a:tree) (rest:list string) (ns:list tree) (c:string) : Tot bool =
  mem c (ids a) && (not (mem c (ids b)) || not (mem c rest || Some? (kid_holder ns c)))

let inv2 (b a:tree) (rest:list string) (ns:list tree) (t:tree) : prop =
  wf t /\ tid_of t == tid_of a /\
  (forall (y:string). mem y (ids t) == (mem y (ids b) || mem y (ids a))) /\
  (forall (q c:string). mem c (kids_at q t) == side b a (cond2 b a rest ns c) q c) /\
  (forall (q:string). mem q (ids t) ==> kind_at q t == kind_src b a q) /\
  (forall (q:string). same_kids b a q ==> kids_at q t == kids_at q b)

let holder_exists (a:tree) (c:string)
  : Lemma (requires wf a /\ mem c (ids a) /\ c <> tid_of a) (ensures Some? (kid_holder (pre a) c))
  = (match a with TNode _ _ _ -> lookup_some a c);
    match lookup c (parent_map (pre a)) with
    | Some v ->
      lookup_is_parent a c v;
      (match find_in v a with
       | Some m -> find_in_mem_pre v a m; kid_holder_intro (pre a) c m
       | None -> ())
    | None -> ()

(* ---- the ORDER pass 2 leaves (Phase 305): before's children still in place, then the shells,
   then the survivors moved in so far, in after-order ---- *)

(* a before-child still in place: still its parent's child in `after`, or not yet moved out *)
let kept2 (b a:tree) (rest:list string) (ns:list tree) (q c:string) : Tot bool =
  mem c (kids_at q a) || not (cond2 b a rest ns c)

(* a survivor moved in from elsewhere, already *)
let moved2 (b a:tree) (rest:list string) (ns:list tree) (q c:string) : Tot bool =
  mem c (ids b) && not (mem c (kids_at q b)) && cond2 b a rest ns c

[@@"opaque_to_smt"]
let ord2 (b a:tree) (rest:list string) (ns:list tree) (t:tree) : prop =
  forall (q:string). mem q (ids a) ==>
    kids_at q t == app (keep (kept2 b a rest ns q) (kids_at q b))
                       (app (keep (new_pred (ids b)) (kids_at q a))
                            (keep (moved2 b a rest ns q) (kids_at q a)))

let ord1_to_ord2 (b a t:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ ord1 b a [] t)
          (ensures ord2 b a [] (pre a) t)
  = reveal_opaque (`%ord1) (ord1 b a [] t);
    reveal_opaque (`%ord2) (ord2 b a [] (pre a) t);
    let aux (q:string)
      : Lemma (mem q (ids a) ==>
               kids_at q t == app (keep (kept2 b a [] (pre a) q) (kids_at q b))
                                  (app (keep (new_pred (ids b)) (kids_at q a))
                                       (keep (moved2 b a [] (pre a) q) (kids_at q a))))
      = if mem q (ids a) then begin
          let still (c:string)
            : Lemma (mem c (kids_at q b) ==> kept2 b a [] (pre a) q c)
            = if mem c (kids_at q b) && not (mem c (kids_at q a)) then begin
                kids_in_ids b q c;
                kid_not_root b q c;
                if mem c (ids a) then holder_exists a c else ()
              end
              else ()
          in
          let none_yet (c:string)
            : Lemma (mem c (kids_at q a) ==> not (moved2 b a [] (pre a) q c))
            = if mem c (kids_at q a) && mem c (ids b) then begin
                kids_in_ids a q c;
                kid_not_root a q c;
                holder_exists a c
              end
              else ()
          in
          FStar.Classical.forall_intro still;
          FStar.Classical.forall_intro none_yet;
          keep_all (kept2 b a [] (pre a) q) (kids_at q b);
          keep_ext (new1 (ids b) []) (new_pred (ids b)) (kids_at q a);
          keep_none (moved2 b a [] (pre a) q) (kids_at q a);
          app_nil_r (keep (new_pred (ids b)) (kids_at q a))
        end
        else ()
    in
    FStar.Classical.forall_intro aux

let inv1_to_inv2 (b a t:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ inv1 b a [] t)
          (ensures inv2 b a [] (pre a) t)
  = let aux (q c:string)
      : Lemma (side b a (cond1 b a [] c) q c == side b a (cond2 b a [] (pre a) c) q c)
      = if mem c (ids a) && mem c (ids b) then begin
          if c = tid_of a then begin
            (if mem c (kids_at q a) then kid_not_root a q c else ());
            (if mem c (kids_at q b) then kid_not_root b q c else ())
          end
          else holder_exists a c
        end
        else ()
    in
    FStar.Classical.forall_intro_2 aux

(* the set the nesting argument closes over: after-nodes no parent still to come holds *)
let outside_sfx (a:tree) (sfx:list tree) (y:string) : Tot bool =
  mem y (ids a) && None? (kid_holder sfx y)

#push-options "--z3rlimit 200 --fuel 2 --ifuel 1"
let d_closed (b a:tree) (done:list tree) (p:tree) (ns:list tree) (rest:list string) (t:tree)
             (q c:string)
  : Lemma (requires wf a /\ pre a == app done (p :: ns) /\ inv2 b a rest ns t /\
                    (forall (z:string). mem z rest ==> mem z (kid_ids (kids_of p))) /\
                    mem c (kids_at q t) /\ outside_sfx a (p :: ns) c)
          (ensures outside_sfx a (p :: ns) q)
  = assert (cond2 b a rest ns c);
    assert (mem c (kids_at q a));
    kids_in_ids a q c;
    match kid_holder (p :: ns) q with
    | None -> ()
    | Some h ->
      sfx_holder a done (p :: ns) q h;
      node_with_some (p :: ns) q;
      (match node_with (p :: ns) q with
       | Some qn ->
         mem_app_r_tree done (p :: ns) qn;
         kids_at_pre a qn;
         kid_holder_intro (p :: ns) c qn
       | None -> ())

(* the moved survivor lands at the END of its new parent's list — and that is where `after` has
   it among the survivors moved in so far, because pass 2 walks the parent's child list in order
   (`kid_ids (kids_of p) == app dk (ck :: rest)`: the kids after `ck` are exactly `rest`, still
   waiting) — and leaves its old parent's kept segment, where it was never a kept child *)
#push-options "--z3rlimit 100 --fuel 1 --ifuel 1"
(* the kept segment of any after-parent: the two filters move only at `ck`, and there the moved
   survivor is no kept child of its old parent *)
let move_ord_kept (b a:tree) (ns:list tree) (ck pk:string) (rest:list string) (q:string)
  : Lemma (requires wf a /\ mem ck (ids a) /\ mem ck (kids_at pk a) /\ None? (kid_holder ns ck) /\
                    not (mem ck rest) /\ (mem ck (kids_at q b) ==> q <> pk))
          (ensures drop_id ck (keep (kept2 b a (ck :: rest) ns q) (kids_at q b))
                     == keep (kept2 b a rest ns q) (kids_at q b))
  = let kb = kids_at q b in
    let f1 = kept2 b a (ck :: rest) ns q in
    let g1 = kept2 b a rest ns q in
    let ag (c:string)
      : Lemma (mem c kb ==> g1 c == (f1 c && c <> ck))
      = if mem c kb && c = ck then (if mem ck (kids_at q a) then kid_unique a q pk ck else ()) else ()
    in
    FStar.Classical.forall_intro ag;
    keep_drop f1 g1 ck kb

(* the moved-in segment of the new parent gains `ck` at its END: the kids after it in the child
   list are `rest`, still waiting *)
let move_ord_in (b a:tree) (ns:list tree) (ck pk:string) (rest dk:list string)
  : Lemma (requires wf a /\ mem ck (ids a) /\ mem ck (ids b) /\ not (mem ck (kids_at pk b)) /\
                    None? (kid_holder ns ck) /\ not (mem ck rest) /\
                    kids_at pk a == app dk (ck :: rest) /\ no_dups (kids_at pk a))
          (ensures keep (moved2 b a rest ns pk) (kids_at pk a)
                     == app (keep (moved2 b a (ck :: rest) ns pk) (kids_at pk a)) [ck])
  = let ak = kids_at pk a in
    let f3 = moved2 b a (ck :: rest) ns pk in
    let g3 = moved2 b a rest ns pk in
    mem_app ck dk (ck :: rest);
    let later (c:string)
      : Lemma (mem c ak /\ precedes ck c ak ==> not (g3 c))
      = if mem c ak && precedes ck c ak then precedes_suffix ck c dk rest else ()
    in
    FStar.Classical.forall_intro later;
    keep_split_at f3 g3 ck ak

(* the moved-in segment of every other after-parent is untouched *)
let move_ord_other (b a:tree) (ns:list tree) (ck:string) (rest:list string) (q:string)
  : Lemma (requires not (mem ck (kids_at q a)))
          (ensures keep (moved2 b a (ck :: rest) ns q) (kids_at q a)
                     == keep (moved2 b a rest ns q) (kids_at q a))
  = keep_ext_mem (moved2 b a (ck :: rest) ns q) (moved2 b a rest ns q) (kids_at q a)

(* the worklist regrouping is the same filter: `kid_holder (p :: ns)` reads `p`'s kids first *)
let cond2_regroup (b a:tree) (p:tree) (ns:list tree) (c:string)
  : Lemma (ensures cond2 b a [] (p :: ns) c == cond2 b a (kid_ids (kids_of p)) ns c)
  = ()
#pop-options

#push-options "--z3rlimit 100 --fuel 1 --ifuel 1"
let move_ord_core (b a:tree) (done:list tree) (p:tree) (ns:list tree) (ck:string)
                  (rest dk:list string) (t:tree) (pid:string) (sub:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done (p :: ns) /\
                    inv2 b a (ck :: rest) ns t /\ ord2 b a (ck :: rest) ns t /\
                    no_dups (ck :: rest) /\ kid_ids (kids_of p) == app dk (ck :: rest) /\
                    mem ck (ids b) /\
                    (match lookup ck (parent_map (pre b)) with
                     | Some bp -> bp <> tid_of p
                     | None -> true) /\
                    parent_of ck t == Some pid /\ find_in ck t == Some sub /\
                    mem (tid_of p) (ids t) /\ not (mem (tid_of p) (ids sub)))
          (ensures ord2 b a rest ns (ins (tid_of p) sub (rem_at pid ck t)))
  = reveal_opaque (`%ord2) (ord2 b a (ck :: rest) ns t);
    reveal_opaque (`%ord2) (ord2 b a rest ns (ins (tid_of p) sub (rem_at pid ck t)));
    let pk = tid_of p in
    let t' = ins pk sub (rem_at pid ck t) in
    let ak = kid_ids (kids_of p) in
    mem_app_r_tree done (p :: ns) p;
    kids_at_pre a p;
    kids_in_ids a pk ck;
    ids_is_pre a;
    tids_app done (p :: ns);
    wf_iff_no_dups a;
    no_dups_app (tids done) (tids (p :: ns));
    kids_at_no_dups pk a;
    (if mem ck (kids_at pk b) then parent_is_lookup b ck pk else ());
    (match kid_holder ns ck with
     | Some h ->
       kid_holder_some ns ck h;
       mem_app_r_tree done (p :: ns) h;
       kids_at_pre a h;
       kid_unique a (tid_of h) pk ck;
       tids_mem ns h
     | None -> ());
    assert (None? (kid_holder ns ck));
    mem_app ck dk (ck :: rest);
    let aux (q:string)
      : Lemma (mem q (ids a) ==>
               kids_at q t' == app (keep (kept2 b a rest ns q) (kids_at q b))
                                   (app (keep (new_pred (ids b)) (kids_at q a))
                                        (keep (moved2 b a rest ns q) (kids_at q a))))
      = if mem q (ids a) then begin
          move_view ck pk t pid sub q;
          let kb = kids_at q b in
          let ka = kids_at q a in
          let f1 = kept2 b a (ck :: rest) ns q in
          let g1 = kept2 b a rest ns q in
          let f3 = moved2 b a (ck :: rest) ns q in
          let nn = keep (new_pred (ids b)) ka in
          drop_id_app ck (keep f1 kb) (app nn (keep f3 ka));
          drop_id_app ck nn (keep f3 ka);
          drop_id_absent ck nn;
          drop_id_absent ck (keep f3 ka);
          (if mem ck kb then parent_is_lookup b ck q else ());
          move_ord_kept b a ns ck pk rest q;
          if q = pk then begin
            move_ord_in b a ns ck pk rest dk;
            app_assoc (keep g1 kb) (app nn (keep f3 ak)) [ck];
            app_assoc nn (keep f3 ak) [ck]
          end
          else begin
            (if mem ck ka then kid_unique a q pk ck else ());
            move_ord_other b a ns ck rest q;
            app_nil_r (app (keep g1 kb) (app nn (keep (moved2 b a rest ns q) ka)))
          end
        end
        else ()
    in
    FStar.Classical.forall_intro aux
#pop-options

let move_step (b a:tree) (done:list tree) (p:tree) (ns:list tree) (ck:string) (rest dk:list string)
              (t:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done (p :: ns) /\
                    inv2 b a (ck :: rest) ns t /\ ord2 b a (ck :: rest) ns t /\
                    no_dups (ck :: rest) /\ kid_ids (kids_of p) == app dk (ck :: rest) /\
                    (forall (z:string). mem z (ck :: rest) ==> mem z (kid_ids (kids_of p))) /\
                    not (same_kids b a (tid_of p)) /\ mem ck (ids b) /\
                    (match lookup ck (parent_map (pre b)) with
                     | Some bp -> bp <> tid_of p
                     | None -> true))
          (ensures (match apply (MoveNode ck (tid_of p)) t with
                    | Ok t' -> inv2 b a rest ns t' /\ ord2 b a rest ns t'
                    | Error _ -> False))
  = let pk = tid_of p in
    mem_app_r_tree done (p :: ns) p;
    kids_at_pre a p;
    kid_not_root a pk ck;
    kids_in_ids a pk ck;
    ids_is_pre a;
    tids_app done (p :: ns);
    wf_iff_no_dups a;
    no_dups_app (tids done) (tids (p :: ns));
    find_in_some_iff ck t;
    match find_in ck t with
    | None -> ()
    | Some sub ->
      find_in_id ck t sub;
      let d = outside_sfx a (p :: ns) in
      let clo (q c:string)
        : Lemma (mem c (kids_at q t) /\ d c ==> d q)
        = FStar.Classical.move_requires (d_closed b a done p ns (ck :: rest) t q) c
      in
      FStar.Classical.forall_intro_2 clo;
      assert (not (d ck));
      closed_outside d t sub;
      (match kid_holder (p :: ns) pk with
       | Some h -> sfx_holder a done (p :: ns) pk h
       | None -> ());
      assert (d pk);
      assert (not (mem pk (ids sub)));
      move_accepted ck pk t;
      (match parent_of ck t with
       | None -> ()
       | Some pid ->
         let t' = ins pk sub (rem_at pid ck t) in
         apply_preserves_wf (MoveNode ck pk) t;
         tid_ins pk sub (rem_at pid ck t);
         tid_rem pid ck t;
         move_ord_core b a done p ns ck rest dk t pid sub;
         let aux_i (y:string) : Lemma (mem y (ids t') == (mem y (ids b) || mem y (ids a)))
           = move_ids ck pk t pid sub y
         in
         let aux_k (q c:string)
           : Lemma (mem c (kids_at q t') == side b a (cond2 b a rest ns c) q c)
           = if mem q (ids t) then begin
               move_view ck pk t pid sub q;
               mem_app c (drop_id ck (kids_at q t)) (if q = pk then [ck] else []);
               drop_id_mem c ck (kids_at q t);
               if c = ck then begin
                 (match kid_holder ns ck with
                  | Some h ->
                    kid_holder_some ns ck h;
                    mem_app_r_tree done (p :: ns) h;
                    kids_at_pre a h;
                    kid_unique a (tid_of h) pk ck;
                    tids_mem ns h
                  | None -> ());
                 (if mem ck (kids_at q a) then kid_unique a q pk ck else ())
               end
               else ()
             end
             else begin
               move_ids ck pk t pid sub q;
               absent_no_kids t' q;
               absent_no_kids a q;
               absent_no_kids b q
             end
         in
         let aux_n (q:string) : Lemma (mem q (ids t') ==> kind_at q t' == kind_src b a q)
           = move_ids ck pk t pid sub q;
             if mem q (ids t) then move_view ck pk t pid sub q else ()
         in
         let aux_x (q:string) : Lemma (same_kids b a q ==> kids_at q t' == kids_at q b)
           = if same_kids b a q then begin
               move_view ck pk t pid sub q;
               (if mem ck (kids_at q a) then kid_unique a q pk ck else ());
               drop_id_absent ck (kids_at q t);
               app_nil_r (kids_at q t)
             end
             else ()
         in
         FStar.Classical.forall_intro aux_i;
         FStar.Classical.forall_intro_2 aux_k;
         FStar.Classical.forall_intro aux_n;
         FStar.Classical.forall_intro aux_x)

let move_skip (b a:tree) (done:list tree) (p:tree) (ns:list tree) (ck:string) (rest:list string)
              (t:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done (p :: ns) /\
                    inv2 b a (ck :: rest) ns t /\ ord2 b a (ck :: rest) ns t /\
                    mem ck (kid_ids (kids_of p)) /\
                    not (mem ck (ids b) &&
                         (match lookup ck (parent_map (pre b)) with
                          | Some bp -> bp <> tid_of p
                          | None -> true)))
          (ensures inv2 b a rest ns t /\ ord2 b a rest ns t)
  = reveal_opaque (`%ord2) (ord2 b a (ck :: rest) ns t);
    reveal_opaque (`%ord2) (ord2 b a rest ns t);
    let pk = tid_of p in
    mem_app_r_tree done (p :: ns) p;
    kids_at_pre a p;
    let aux (q c:string)
      : Lemma (side b a (cond2 b a (ck :: rest) ns c) q c == side b a (cond2 b a rest ns c) q c)
      = if c = ck && mem ck (ids b) then begin
          lookup_is_parent b ck pk;
          (if mem ck (kids_at q a) then kid_unique a q pk ck else ());
          (if mem ck (kids_at q b) then kid_unique b q pk ck else ())
        end
        else ()
    in
    FStar.Classical.forall_intro_2 aux;
    (* the order: the filters move only at `ck`, and there they agree on both lists *)
    let aux_o (q:string)
      : Lemma (keep (kept2 b a (ck :: rest) ns q) (kids_at q b) == keep (kept2 b a rest ns q) (kids_at q b) /\
               keep (moved2 b a (ck :: rest) ns q) (kids_at q a) == keep (moved2 b a rest ns q) (kids_at q a))
      = let ag (c:string)
          : Lemma ((mem c (kids_at q b) ==> kept2 b a (ck :: rest) ns q c == kept2 b a rest ns q c) /\
                   (mem c (kids_at q a) ==> moved2 b a (ck :: rest) ns q c == moved2 b a rest ns q c))
          = if c = ck && mem ck (ids b) then begin
              lookup_is_parent b ck pk;
              (if mem ck (kids_at q a) then kid_unique a q pk ck else ());
              (if mem ck (kids_at q b) then kid_unique b q pk ck else ())
            end
            else ()
        in
        FStar.Classical.forall_intro ag;
        keep_ext_mem (kept2 b a (ck :: rest) ns q) (kept2 b a rest ns q) (kids_at q b);
        keep_ext_mem (moved2 b a (ck :: rest) ns q) (moved2 b a rest ns q) (kids_at q a)
    in
    FStar.Classical.forall_intro aux_o

let rec moves_under_run (b a:tree) (done:list tree) (p:tree) (ns:list tree) (dk:list string)
                        (cs:list tree) (t:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done (p :: ns) /\
                    inv2 b a (kid_ids cs) ns t /\ ord2 b a (kid_ids cs) ns t /\
                    no_dups (kid_ids cs) /\ kid_ids (kids_of p) == app dk (kid_ids cs) /\
                    (forall (z:string). mem z (kid_ids cs) ==> mem z (kid_ids (kids_of p))) /\
                    not (same_kids b a (tid_of p)))
          (ensures (match apply_all (moves_under (tid_of p) (ids b) (parent_map (pre b)) cs) t with
                    | Ok t' -> inv2 b a [] ns t' /\ ord2 b a [] ns t'
                    | Error _ -> False))
          (decreases cs)
  = match cs with
    | [] -> ()
    | c :: r ->
      let ck = tid_of c in
      let pk = tid_of p in
      let moved =
        mem ck (ids b) &&
        (match lookup ck (parent_map (pre b)) with
         | Some bp -> bp <> pk
         | None -> true) in
      app_assoc dk [ck] (kid_ids r);
      if moved then begin
        move_step b a done p ns ck (kid_ids r) dk t;
        (match apply (MoveNode ck pk) t with
         | Ok t' -> moves_under_run b a done p ns (app dk [ck]) r t'
         | Error _ -> ())
      end
      else begin
        move_skip b a done p ns ck (kid_ids r) t;
        moves_under_run b a done p ns (app dk [ck]) r t
      end

let inv2_regroup (b a:tree) (p:tree) (ns:list tree) (t:tree)
  : Lemma (requires inv2 b a [] (p :: ns) t /\ ord2 b a [] (p :: ns) t)
          (ensures inv2 b a (kid_ids (kids_of p)) ns t /\ ord2 b a (kid_ids (kids_of p)) ns t)
  = reveal_opaque (`%ord2) (ord2 b a [] (p :: ns) t);
    reveal_opaque (`%ord2) (ord2 b a (kid_ids (kids_of p)) ns t);
    FStar.Classical.forall_intro (cond2_regroup b a p ns);
    let aux_o (q:string)
      : Lemma (keep (kept2 b a [] (p :: ns) q) (kids_at q b) == keep (kept2 b a (kid_ids (kids_of p)) ns q) (kids_at q b) /\
               keep (moved2 b a [] (p :: ns) q) (kids_at q a) == keep (moved2 b a (kid_ids (kids_of p)) ns q) (kids_at q a))
      = keep_ext (kept2 b a [] (p :: ns) q) (kept2 b a (kid_ids (kids_of p)) ns q) (kids_at q b);
        keep_ext (moved2 b a [] (p :: ns) q) (moved2 b a (kid_ids (kids_of p)) ns q) (kids_at q a)
    in
    FStar.Classical.forall_intro aux_o

let inv2_unchanged (b a:tree) (done:list tree) (p:tree) (ns:list tree) (t:tree)
  : Lemma (requires wf b /\ wf a /\ pre a == app done (p :: ns) /\ inv2 b a [] (p :: ns) t /\
                    ord2 b a [] (p :: ns) t /\ same_kids b a (tid_of p))
          (ensures inv2 b a [] ns t /\ ord2 b a [] ns t)
  = reveal_opaque (`%ord2) (ord2 b a [] (p :: ns) t);
    reveal_opaque (`%ord2) (ord2 b a [] ns t);
    let pk = tid_of p in
    mem_app_r_tree done (p :: ns) p;
    kids_at_pre a p;
    let aux (q c:string)
      : Lemma (side b a (cond2 b a [] (p :: ns) c) q c == side b a (cond2 b a [] ns c) q c)
      = if mem c (kid_ids (kids_of p)) then begin
          (if mem c (kids_at q a) then kid_unique a q pk c else ());
          (if mem c (kids_at q b) then kid_unique b q pk c else ())
        end
        else ()
    in
    FStar.Classical.forall_intro_2 aux;
    (* the order: a kid of an unchanged parent is kept wherever it is a before-child and moved in
       nowhere, whichever worklist the filter reads *)
    let aux_o (q:string)
      : Lemma (keep (kept2 b a [] (p :: ns) q) (kids_at q b) == keep (kept2 b a [] ns q) (kids_at q b) /\
               keep (moved2 b a [] (p :: ns) q) (kids_at q a) == keep (moved2 b a [] ns q) (kids_at q a))
      = let ag (c:string)
          : Lemma ((mem c (kids_at q b) ==> kept2 b a [] (p :: ns) q c == kept2 b a [] ns q c) /\
                   (mem c (kids_at q a) ==> moved2 b a [] (p :: ns) q c == moved2 b a [] ns q c))
          = if mem c (kid_ids (kids_of p)) then begin
              (if mem c (kids_at q a) then kid_unique a q pk c else ());
              (if mem c (kids_at q b) then kid_unique b q pk c else ())
            end
            else ()
        in
        FStar.Classical.forall_intro ag;
        keep_ext_mem (kept2 b a [] (p :: ns) q) (kept2 b a [] ns q) (kids_at q b);
        keep_ext_mem (moved2 b a [] (p :: ns) q) (moved2 b a [] ns q) (kids_at q a)
    in
    FStar.Classical.forall_intro aux_o

let rec moves_run (b a:tree) (done ns:list tree) (t:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done ns /\
                    inv2 b a [] ns t /\ ord2 b a [] ns t)
          (ensures (match apply_all (fst (pass_moves (ids b) (parent_map (pre b)) (kid_map (pre b)) ns)) t with
                    | Ok t' -> inv2 b a [] [] t' /\ ord2 b a [] [] t'
                    | Error _ -> False))
          (decreases ns)
  = match ns with
    | [] -> ()
    | p :: r ->
      let pk = tid_of p in
      app_assoc done [p] r;
      mem_app_r_tree done (p :: r) p;
      kids_at_pre a p;
      pre_sub_ids a p pk;
      (if mem pk (ids b) then lookup_kids_is b pk else ());
      if same_kids b a pk then begin
        inv2_unchanged b a done p r t;
        moves_run b a (app done [p]) r t
      end
      else begin
        inv2_regroup b a p r t;
        kids_at_no_dups pk a;
        moves_under_run b a done p r [] (kids_of p) t;
        apply_all_app (moves_under pk (ids b) (parent_map (pre b)) (kids_of p))
                      (fst (pass_moves (ids b) (parent_map (pre b)) (kid_map (pre b)) r)) t;
        (match apply_all (moves_under pk (ids b) (parent_map (pre b)) (kids_of p)) t with
         | Ok t' -> moves_run b a (app done [p]) r t'
         | Error _ -> ())
      end
#pop-options

(* ================= pass 3: the removals ================= *)

(* the node pass 3 removes: `after` does not carry it, and its before-parent survives *)
let top (b a:tree) (y:string) : Tot bool =
  not (mem y (ids a)) &&
  (match lookup y (parent_map (pre b)) with
   | Some pid -> mem pid (ids a)
   | None -> false)

let side3 (b a:tree) (ns:list tree) (q c:string) : Tot bool =
  if mem c (ids a) then mem c (kids_at q a)
  else mem c (kids_at q b) && (not (mem q (ids a)) || mem c (tids ns))

let inv3 (b a:tree) (ns:list tree) (t:tree) : prop =
  wf t /\ tid_of t == tid_of a /\
  (forall (y:string). mem y (ids a) ==> mem y (ids t)) /\
  (forall (y:string). mem y (tids ns) /\ top b a y ==> mem y (ids t)) /\
  (forall (q c:string). mem q (ids t) ==> mem c (kids_at q t) == side3 b a ns q c) /\
  (forall (q:string). mem q (ids t) ==> kind_at q t == kind_src b a q) /\
  (forall (q:string). same_kids b a q ==> kids_at q t == kids_at q b)

(* ---- the ORDER pass 3 leaves (Phase 305): the before-children still in place — a kept child,
   or a removed one the pass has not reached — then the shells, then the moved-in survivors ---- *)

let kept3 (b a:tree) (ns:list tree) (q c:string) : Tot bool =
  mem c (kids_at q a) || (not (mem c (ids a)) && mem c (tids ns))

[@@"opaque_to_smt"]
let ord3 (b a:tree) (ns:list tree) (t:tree) : prop =
  forall (q:string). mem q (ids a) ==>
    kids_at q t == app (keep (kept3 b a ns q) (kids_at q b))
                       (app (keep (new_pred (ids b)) (kids_at q a))
                            (keep (moved_pred (ids b) (kids_at q b)) (kids_at q a)))

(* what the three passes leave — `settled_order` read at the two trees' child lists *)
[@@"opaque_to_smt"]
let predicted (b a:tree) (q:string) : Tot (list string) =
  settled_order (ids b) (kids_at q b) (kids_at q a)

#push-options "--z3rlimit 200 --fuel 2 --ifuel 1"
let ord2_to_ord3 (b a t:tree)
  : Lemma (requires wf b /\ wf a /\ ord2 b a [] [] t) (ensures ord3 b a (pre b) t)
  = reveal_opaque (`%ord2) (ord2 b a [] [] t);
    reveal_opaque (`%ord3) (ord3 b a (pre b) t);
    ids_is_pre b;
    let aux (q:string)
      : Lemma (keep (kept2 b a [] [] q) (kids_at q b) == keep (kept3 b a (pre b) q) (kids_at q b) /\
               keep (moved2 b a [] [] q) (kids_at q a) == keep (moved_pred (ids b) (kids_at q b)) (kids_at q a))
      = let ag (c:string)
          : Lemma ((mem c (kids_at q b) ==> kept2 b a [] [] q c == kept3 b a (pre b) q c) /\
                   (mem c (kids_at q a) ==> moved2 b a [] [] q c == moved_pred (ids b) (kids_at q b) c))
          = (if mem c (kids_at q b) then kids_in_ids b q c else ());
            (if mem c (kids_at q a) then kids_in_ids a q c else ())
        in
        FStar.Classical.forall_intro ag;
        keep_ext_mem (kept2 b a [] [] q) (kept3 b a (pre b) q) (kids_at q b);
        keep_ext_mem (moved2 b a [] [] q) (moved_pred (ids b) (kids_at q b)) (kids_at q a)
    in
    FStar.Classical.forall_intro aux

let ord3_to_predicted (b a t:tree)
  : Lemma (requires ord3 b a [] t)
          (ensures forall (q:string). mem q (ids a) ==> kids_at q t == predicted b a q)
  = reveal_opaque (`%ord3) (ord3 b a [] t);
    let aux (q:string)
      : Lemma (keep (kept3 b a [] q) (kids_at q b) == keep (in_list (kids_at q a)) (kids_at q b) /\
               predicted b a q == settled_order (ids b) (kids_at q b) (kids_at q a))
      = reveal_opaque (`%predicted) (predicted b a q);
        keep_ext (kept3 b a [] q) (in_list (kids_at q a)) (kids_at q b)
    in
    FStar.Classical.forall_intro aux

let inv2_to_inv3 (b a t:tree)
  : Lemma (requires wf b /\ wf a /\ inv2 b a [] [] t) (ensures inv3 b a (pre b) t)
  = ids_is_pre b;
    let aux (q c:string)
      : Lemma (side b a (cond2 b a [] [] c) q c == side3 b a (pre b) q c)
      = if mem c (kids_at q b) then kids_in_ids b q c else ()
    in
    FStar.Classical.forall_intro_2 aux

let rem_closed (b a:tree) (ns:list tree) (t:tree) (k:string) (q c:string)
  : Lemma (requires wf b /\ inv3 b a ns t /\ not (mem k (ids a)) /\
                    mem c (kids_at q t) /\ (mem c (ids a) || top b a c) /\ c <> k)
          (ensures (mem q (ids a) || top b a q) /\ q <> k)
  = kids_in_ids t q c;
    if mem c (ids a) then kids_in_ids a q c
    else parent_is_lookup b c q

(* a removed child leaves its before-parent's kept segment in place; it was never in the other
   two, which hold only nodes `after` carries *)
let rem_ord_core (b a:tree) (n:tree) (r:list tree) (t:tree) (pid:string) (sub:tree)
  : Lemma (requires wf b /\ wf a /\ inv3 b a (n :: r) t /\ ord3 b a (n :: r) t /\
                    no_dups (tids (n :: r)) /\ top b a (tid_of n) /\
                    parent_of (tid_of n) t == Some pid /\ find_in (tid_of n) t == Some sub /\
                    (forall (q:string). mem q (ids a) ==> not (mem q (ids sub))))
          (ensures ord3 b a r (rem_at pid (tid_of n) t))
  = reveal_opaque (`%ord3) (ord3 b a (n :: r) t);
    reveal_opaque (`%ord3) (ord3 b a r (rem_at pid (tid_of n) t));
    let k = tid_of n in
    let t' = rem_at pid k t in
    let aux (q:string)
      : Lemma (mem q (ids a) ==>
               kids_at q t' == app (keep (kept3 b a r q) (kids_at q b))
                                   (app (keep (new_pred (ids b)) (kids_at q a))
                                        (keep (moved_pred (ids b) (kids_at q b)) (kids_at q a))))
      = if mem q (ids a) then begin
          rem_view pid k t sub q;
          let kb = kids_at q b in
          let ka = kids_at q a in
          let f1 = kept3 b a (n :: r) q in
          let g1 = kept3 b a r q in
          let nn = keep (new_pred (ids b)) ka in
          let mm = keep (moved_pred (ids b) kb) ka in
          (if mem k ka then kids_in_ids a q k else ());
          drop_id_app k (keep f1 kb) (app nn mm);
          drop_id_app k nn mm;
          drop_id_absent k nn;
          drop_id_absent k mm;
          keep_drop f1 g1 k kb
        end
        else ()
    in
    FStar.Classical.forall_intro aux

let rem_step (b a:tree) (n:tree) (r:list tree) (t:tree)
  : Lemma (requires wf b /\ wf a /\ inv3 b a (n :: r) t /\ ord3 b a (n :: r) t /\
                    no_dups (tids (n :: r)) /\ top b a (tid_of n))
          (ensures (match apply (RemoveNode (tid_of n)) t with
                    | Ok t' -> inv3 b a r t' /\ ord3 b a r t'
                    | Error _ -> False))
  = let k = tid_of n in
    tid_in_ids a;
    find_in_some_iff k t;
    parent_exists k t;
    match find_in k t, parent_of k t with
    | Some sub, Some pid ->
      find_in_id k t sub;
      let d (y:string) : Tot bool = (mem y (ids a) || top b a y) && y <> k in
      let clo (q c:string)
        : Lemma (mem c (kids_at q t) /\ d c ==> d q)
        = FStar.Classical.move_requires (rem_closed b a (n :: r) t k q) c
      in
      FStar.Classical.forall_intro_2 clo;
      closed_outside d t sub;
      let outside (q:string) : Lemma (mem q (ids a) ==> not (mem q (ids sub))) = () in
      FStar.Classical.forall_intro outside;
      rem_ord_core b a n r t pid sub;
      let t' = rem_at pid k t in
      apply_preserves_wf (RemoveNode k) t;
      tid_rem pid k t;
      rem_kills pid k t sub;
      ids_rem_sub_fa pid k t;
      let aux_p (y:string)
        : Lemma ((mem y (ids a) \/ (mem y (tids r) /\ top b a y)) ==> mem y (ids t'))
        = if (mem y (ids a) || (mem y (tids r) && top b a y)) then rem_view pid k t sub y else ()
      in
      let aux_k (q c:string)
        : Lemma (mem q (ids t') ==> mem c (kids_at q t') == side3 b a r q c)
        = if mem q (ids t') then begin
            rem_view pid k t sub q;
            drop_id_mem c k (kids_at q t);
            if c = k then (if mem k (kids_at q b) then parent_is_lookup b k q else ())
            else ()
          end
          else ()
      in
      let aux_n (q:string) : Lemma (mem q (ids t') ==> kind_at q t' == kind_src b a q)
        = if mem q (ids t') then rem_view pid k t sub q else ()
      in
      let aux_x (q:string) : Lemma (same_kids b a q ==> kids_at q t' == kids_at q b)
        = if same_kids b a q then begin
            rem_view pid k t sub q;
            (if mem k (kids_at q a) then kids_in_ids a q k else ());
            drop_id_absent k (kids_at q t)
          end
          else ()
      in
      FStar.Classical.forall_intro aux_p;
      FStar.Classical.forall_intro_2 aux_k;
      FStar.Classical.forall_intro aux_n;
      FStar.Classical.forall_intro aux_x
    | _, _ -> ()

let rem_skip (b a:tree) (n:tree) (r:list tree) (t:tree)
  : Lemma (requires wf b /\ inv3 b a (n :: r) t /\ ord3 b a (n :: r) t /\ not (top b a (tid_of n)))
          (ensures inv3 b a r t /\ ord3 b a r t)
  = reveal_opaque (`%ord3) (ord3 b a (n :: r) t);
    reveal_opaque (`%ord3) (ord3 b a r t);
    let k = tid_of n in
    let aux (q c:string)
      : Lemma (mem q (ids t) ==> side3 b a (n :: r) q c == side3 b a r q c)
      = if c = k && mem k (kids_at q b) then parent_is_lookup b k q else ()
    in
    FStar.Classical.forall_intro_2 aux;
    (* the order: a skipped node that is some after-parent's before-child is one `after` keeps *)
    let aux_o (q:string)
      : Lemma (mem q (ids a) ==> keep (kept3 b a (n :: r) q) (kids_at q b) == keep (kept3 b a r q) (kids_at q b))
      = if mem q (ids a) then begin
          let ag (c:string)
            : Lemma (mem c (kids_at q b) ==> kept3 b a (n :: r) q c == kept3 b a r q c)
            = if c = k && mem k (kids_at q b) then parent_is_lookup b k q else ()
          in
          FStar.Classical.forall_intro ag;
          keep_ext_mem (kept3 b a (n :: r) q) (kept3 b a r q) (kids_at q b)
        end
        else ()
    in
    FStar.Classical.forall_intro aux_o

let rec removes_run (b a:tree) (ns:list tree) (t:tree)
  : Lemma (requires wf b /\ wf a /\ inv3 b a ns t /\ ord3 b a ns t /\ no_dups (tids ns))
          (ensures (match apply_all (pass_removes (ids a) (parent_map (pre b)) ns) t with
                    | Ok t' -> inv3 b a [] t' /\ ord3 b a [] t'
                    | Error _ -> False))
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r ->
      if top b a (tid_of n) then begin
        rem_step b a n r t;
        (match apply (RemoveNode (tid_of n)) t with
         | Ok t' -> removes_run b a r t'
         | Error _ -> ())
      end
      else begin
        rem_skip b a n r t;
        removes_run b a r t
      end
#pop-options

(* ================= pass 4: the reorders ================= *)

(* The last conjunct is Phase 305's: a listed parent not yet reached holds the order passes 1-3
   left it in (`predicted`) — or `after`'s, which is what lets the pass SKIP it when the two
   agree. A disjunction rather than `predicted` alone so that nothing here depends on the
   worklist being duplicate-free. *)
let inv4 (b a:tree) (ps:list string) (t:tree) : prop =
  wf t /\ tid_of t == tid_of a /\
  (forall (y:string). mem y (ids a) ==> mem y (ids t)) /\
  (forall (q c:string). mem q (ids t) ==> mem c (kids_at q t) == side3 b a [] q c) /\
  (forall (q:string). mem q (ids t) ==> kind_at q t == kind_src b a q) /\
  (forall (q:string). mem q (ids a) /\ not (mem q ps) ==> kids_at q t == kids_at q a) /\
  (forall (q:string). mem q (ids a) /\ mem q ps ==>
     (kids_at q t == predicted b a q \/ kids_at q t == kids_at q a))

let rec changed_listed (b a:tree) (ns:list tree) (q:string)
  : Lemma (requires wf b /\ wf a /\ (forall (n:tree). mem n ns ==> mem n (pre a)) /\
                    mem q (tids ns) /\ not (same_kids b a q))
          (ensures mem q (snd (pass_moves (ids b) (parent_map (pre b)) (kid_map (pre b)) ns)))
          (decreases ns)
  = match ns with
    | [] -> ()
    | p :: r ->
      let pk = tid_of p in
      kids_at_pre a p;
      pre_sub_ids a p pk;
      (if mem pk (ids b) then lookup_kids_is b pk else ());
      if pk = q then () else changed_listed b a r q

let short_eq (l m:list string)
  : Lemma (requires no_dups l /\ (forall (z:string). mem z l == mem z m) /\ not (more_than_one m))
          (ensures l == m)
  = match m with
    | [] -> (match l with
             | [] -> ()
             | h :: _ -> assert (mem h l))
    | [u] -> (match l with
              | [] -> assert (mem u m)
              | [h] -> assert (mem h l)
              | h1 :: h2 :: _ -> assert (mem h1 l); assert (mem h2 l))

#push-options "--z3rlimit 200 --fuel 2 --ifuel 1"
let inv4_kids_mem (b a:tree) (ps:list string) (t:tree) (pid:string) (z:string)
  : Lemma (requires inv4 b a ps t /\ mem pid (ids a))
          (ensures mem z (kids_at pid t) == mem z (kids_at pid a))
  = if mem z (ids a) then ()
    else (if mem z (kids_at pid a) then kids_in_ids a pid z else ())

let reorder_step (b a:tree) (pid:string) (r:list string) (t:tree) (pn:tree)
  : Lemma (requires wf a /\ inv4 b a (pid :: r) t /\ find_in pid a == Some pn)
          (ensures (match apply (ReorderChildren pid (kid_ids (kids_of pn))) t with
                    | Ok t' -> inv4 b a r t'
                    | Error _ -> False))
  = let ord = kid_ids (kids_of pn) in
    find_in_some_iff pid a;
    kids_at_no_dups pid t;
    kids_at_no_dups pid a;
    FStar.Classical.forall_intro (FStar.Classical.move_requires (inv4_kids_mem b a (pid :: r) t pid));
    no_dups_same_multiset (kids_at pid t) ord;
    let t' = reorder_at pid ord t in
    apply_preserves_wf (ReorderChildren pid ord) t;
    tid_reorder pid ord t;
    let aux_v (q:string)
      : Lemma (apply (ReorderChildren pid ord) t == Ok t' /\
               kids_at q t' == (if q = pid then ord else kids_at q t) /\
               kind_at q t' == kind_at q t /\
               mem q (ids t') == mem q (ids t))
      = reorder_view pid ord t q
    in
    FStar.Classical.forall_intro aux_v;
    aux_v pid

let reorder_short (b a:tree) (pid:string) (r:list string) (t:tree) (pn:tree)
  : Lemma (requires wf a /\ inv4 b a (pid :: r) t /\ find_in pid a == Some pn /\
                    not (more_than_one (kids_of pn)))
          (ensures inv4 b a r t)
  = find_in_some_iff pid a;
    kids_at_no_dups pid t;
    FStar.Classical.forall_intro (FStar.Classical.move_requires (inv4_kids_mem b a (pid :: r) t pid));
    (match kids_of pn with
     | [] -> ()
     | [_] -> ()
     | _ -> ());
    short_eq (kids_at pid t) (kids_at pid a)

(* THE DROP (Phase 305, 305.t1): the pass reads the parent's before-children off the kid map,
   which is `kids_at pid b` for a survivor and `[]` for a parent `before` does not carry, so
   `settled_order` there IS `predicted`; and a parent whose predicted order is `after`'s already
   holds `after`'s list, by `inv4`'s last conjunct either way round. *)
let reorder_settled (b a:tree) (pid:string) (r:list string) (t:tree) (pn:tree)
  : Lemma (requires wf b /\ wf a /\ inv4 b a (pid :: r) t /\ find_in pid a == Some pn /\
                    settled_order (ids b)
                                  (match lookup_kids pid (kid_map (pre b)) with Some bk -> bk | None -> [])
                                  (kid_ids (kids_of pn))
                      == kid_ids (kids_of pn))
          (ensures inv4 b a r t)
  = find_in_some_iff pid a;
    ids_is_pre b;
    let ak = kid_ids (kids_of pn) in
    let bk = (match lookup_kids pid (kid_map (pre b)) with Some bk -> bk | None -> []) in
    (if mem pid (ids b) then lookup_kids_is b pid
     else begin
       absent_no_kids b pid;
       (match lookup_kids pid (kid_map (pre b)) with
        | Some v -> lookup_kids_sound b (pre b) pid v
        | None -> ())
     end);
    reveal_opaque (`%predicted) (predicted b a pid);
    assert (bk == kids_at pid b);
    assert (kids_at pid a == ak);
    assert (predicted b a pid == ak)

let rec reorders_run (b a:tree) (ps:list string) (t:tree)
  : Lemma (requires wf b /\ wf a /\ inv4 b a ps t)
          (ensures (match apply_all (pass_reorders (ids b) (kid_map (pre b)) a ps) t with
                    | Ok t' -> inv4 b a [] t'
                    | Error _ -> False))
          (decreases ps)
  = match ps with
    | [] -> ()
    | pid :: r ->
      (match find_in pid a with
       | Some pn ->
         let ak = kid_ids (kids_of pn) in
         let bk = (match lookup_kids pid (kid_map (pre b)) with Some bk -> bk | None -> []) in
         if more_than_one (kids_of pn) && settled_order (ids b) bk ak <> ak then begin
           reorder_step b a pid r t pn;
           (match apply (ReorderChildren pid ak) t with
            | Ok t' -> reorders_run b a r t'
            | Error _ -> ())
         end
         else if more_than_one (kids_of pn) then begin
           reorder_settled b a pid r t pn;
           reorders_run b a r t
         end
         else begin
           reorder_short b a pid r t pn;
           reorders_run b a r t
         end
       | None ->
         find_in_some_iff pid a;
         reorders_run b a r t)
#pop-options

(* ================= two trees that agree node for node are the same tree ================= *)

let kid_found (t s c:tree)
  : Lemma (requires wf t /\ find_in (tid_of s) t == Some s /\ mem c (kids_of s))
          (ensures find_in (tid_of c) t == Some c)
  = find_in_wf (tid_of s) t s;
    (match s with
     | TNode si sk scs ->
       find_all_kid scs c;
       kid_ids_of_mem c scs;
       kid_ids_sub (tid_of c) scs);
    find_in_trans (tid_of s) (tid_of c) t s

let rec tree_ext (a f:tree) (s1 s2:tree)
  : Lemma (requires wf a /\ wf f /\ find_in (tid_of s1) a == Some s1 /\
                    find_in (tid_of s1) f == Some s2 /\
                    (forall (q:string). mem q (ids a) ==>
                       kids_at q f == kids_at q a /\ kind_at q f == kind_at q a))
          (ensures s1 == s2) (decreases s1)
  = find_in_some_iff (tid_of s1) a;
    find_in_id (tid_of s1) f s2;
    match s1, s2 with
    | TNode i k cs1, TNode i2 k2 cs2 -> tree_ext_all a f s1 s2 cs1 cs2
and tree_ext_all (a f:tree) (s1 s2:tree) (l1 l2:list tree)
  : Lemma (requires wf a /\ wf f /\ find_in (tid_of s1) a == Some s1 /\
                    find_in (tid_of s2) f == Some s2 /\ l1 << s1 /\
                    (forall (c:tree). mem c l1 ==> mem c (kids_of s1)) /\
                    (forall (c:tree). mem c l2 ==> mem c (kids_of s2)) /\
                    kid_ids l1 == kid_ids l2 /\
                    (forall (q:string). mem q (ids a) ==>
                       kids_at q f == kids_at q a /\ kind_at q f == kind_at q a))
          (ensures l1 == l2) (decreases l1)
  = match l1, l2 with
    | c1 :: r1, c2 :: r2 ->
      kid_found a s1 c1;
      kid_found f s2 c2;
      tree_ext a f c1 c2;
      tree_ext_all a f s1 s2 r1 r2
    | _, _ -> ()

(* ================= THE TWO THEOREMS ================= *)

#push-options "--z3rlimit 200 --fuel 2 --ifuel 1"
let diff_run (b a:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a)
          (ensures (match apply_all (script_of (diff_blocks b a)) b with
                    | Ok f -> inv4 b a [] f
                    | Error _ -> False))
  = let a_par = parent_map (pre a) in
    let b_par = parent_map (pre b) in
    let b_kids = kid_map (pre b) in
    let p1 = pass_inserts a_par (ids b) (pre a) in
    let pm = pass_moves (ids b) b_par b_kids (pre a) in
    let p2 = fst pm in
    let p3 = pass_removes (ids a) b_par (pre b) in
    let p4 = pass_reorders (ids b) b_kids a (snd pm) in
    apply_all_app p1 (app p2 (app p3 p4)) b;
    inv1_init b a;
    ord1_init b a;
    inserts_run b a [] (pre a) b;
    match apply_all p1 b with
    | Error _ -> ()
    | Ok t1 ->
      apply_all_app p2 (app p3 p4) t1;
      inv1_to_inv2 b a t1;
      ord1_to_ord2 b a t1;
      moves_run b a [] (pre a) t1;
      (match apply_all p2 t1 with
       | Error _ -> ()
       | Ok t2 ->
         apply_all_app p3 p4 t2;
         inv2_to_inv3 b a t2;
         ord2_to_ord3 b a t2;
         ids_is_pre b;
         wf_iff_no_dups b;
         removes_run b a (pre b) t2;
         (match apply_all p3 t2 with
          | Error _ -> ()
          | Ok t3 ->
            ids_is_pre a;
            ord3_to_predicted b a t3;
            let listed (q:string)
              : Lemma (mem q (ids a) /\ not (same_kids b a q) ==> mem q (snd pm))
              = FStar.Classical.move_requires (changed_listed b a (pre a)) q
            in
            FStar.Classical.forall_intro listed;
            assert (inv4 b a (snd pm) t3);
            reorders_run b a (snd pm) t3))
#pop-options

(* THE ORDER-PREDICTION LEMMA (Phase 305, 305.t1): after the first three blocks, every parent of
   `after` holds its kept survivors in `before`'s order, then the inserted shells, then the moved-in
   survivors, the last two each in `after`'s order — `settled_order`, the order pass 4 reads to
   decide that a parent needs no reorder. The three exact-order invariants assemble it. *)
#push-options "--z3rlimit 200 --fuel 2 --ifuel 1"
let diff_settles_order (b a:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a)
          (ensures (let (p1, p2, p3, _) = diff_blocks b a in
                    match apply_all (app p1 (app p2 p3)) b with
                    | Ok t3 -> forall (q:string). mem q (ids a) ==>
                                 kids_at q t3 == settled_order (ids b) (kids_at q b) (kids_at q a)
                    | Error _ -> False))
  = let a_par = parent_map (pre a) in
    let b_par = parent_map (pre b) in
    let b_kids = kid_map (pre b) in
    let p1 = pass_inserts a_par (ids b) (pre a) in
    let pm = pass_moves (ids b) b_par b_kids (pre a) in
    let p2 = fst pm in
    let p3 = pass_removes (ids a) b_par (pre b) in
    apply_all_app p1 (app p2 p3) b;
    inv1_init b a;
    ord1_init b a;
    inserts_run b a [] (pre a) b;
    match apply_all p1 b with
    | Error _ -> ()
    | Ok t1 ->
      apply_all_app p2 p3 t1;
      inv1_to_inv2 b a t1;
      ord1_to_ord2 b a t1;
      moves_run b a [] (pre a) t1;
      (match apply_all p2 t1 with
       | Error _ -> ()
       | Ok t2 ->
         inv2_to_inv3 b a t2;
         ord2_to_ord3 b a t2;
         ids_is_pre b;
         wf_iff_no_dups b;
         removes_run b a (pre b) t2;
         (match apply_all p3 t2 with
          | Error _ -> ()
          | Ok t3 ->
            ord3_to_predicted b a t3;
            let unfold_q (q:string)
              : Lemma (predicted b a q == settled_order (ids b) (kids_at q b) (kids_at q a))
              = reveal_opaque (`%predicted) (predicted b a q)
            in
            FStar.Classical.forall_intro unfold_q))
#pop-options

(* THE OPERATIONAL `diff_applicable`: `apply` accepts every step of the script `toOps` emits, in
   sequence, against the tree in hand at that step. *)
let diff_applicable (b a:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a)
          (ensures Ok? (to_ops b a) /\ Ok? (apply_all (Ok?._0 (to_ops b a)) b))
  = diff_ok_on_any_wf_pair b a;
    diff_run b a

(* the one premise reconstruction needs beyond the diff own: a shared id names the same KIND in
   both trees. Until Phase 305 this read "since no skeleton operation edits a node", which has been
   false since Phase 250's `UpdateNode`: the premise is needed because the STRUCTURAL diff emits no
   `UpdateNode` — it has no content accessor to see a change with — so a survivor keeps `before`'s
   kind whatever `after` says. `Diff.toOpsContainedWith encode` emits the rewrite, and the premise
   is what its run theorem drops; see section 12. *)
let kinds_agree (b a:tree) : prop =
  forall (q:string). mem q (ids a) /\ mem q (ids b) ==> kind_at q b == kind_at q a

(* `diff_reconstructs`: applying the script to `before` yields `after` - the tree, not a tree
   like it. *)
let diff_reconstructs (b a:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ kinds_agree b a)
          (ensures Ok? (to_ops b a) /\ apply_all (Ok?._0 (to_ops b a)) b == Ok a)
  = diff_ok_on_any_wf_pair b a;
    diff_run b a;
    match apply_all (script_of (diff_blocks b a)) b with
    | Error _ -> ()
    | Ok f ->
      find_in_self (tid_of a) a;
      find_in_self (tid_of a) f;
      tree_ext a f a f

(* ---- and the two are NOT VACUOUS: a pair whose diff carries an insert, a move, a remove and a
   reorder, reconstructed by evaluation ---- *)

let rec_before : tree =
  TNode "root" "doc" [ TNode "x" "sec" [ TNode "p" "para" [] ]; TNode "y" "para" [] ]

let rec_after : tree =
  TNode "root" "doc" [ TNode "q" "sec" [ TNode "p" "para" [] ]; TNode "y" "para" [] ]

let reconstruction_is_not_vacuous ()
  : Lemma (ensures to_ops rec_before rec_after ==
                     Ok [ InsertChild "root" (TNode "q" "sec" []);
                          MoveNode "p" "q";
                          RemoveNode "x";
                          ReorderChildren "root" [ "q"; "y" ] ] /\
                   apply_all (Ok?._0 (to_ops rec_before rec_after)) rec_before == Ok rec_after /\
                   apply_all (Ok?._0 (to_ops pos_before pos_after)) pos_before == Ok pos_after)
  = assert_norm (to_ops rec_before rec_after ==
                   Ok [ InsertChild "root" (TNode "q" "sec" []);
                        MoveNode "p" "q";
                        RemoveNode "x";
                        ReorderChildren "root" [ "q"; "y" ] ]);
    assert_norm (apply_all [ InsertChild "root" (TNode "q" "sec" []);
                             MoveNode "p" "q";
                             RemoveNode "x";
                             ReorderChildren "root" [ "q"; "y" ] ] rec_before == Ok rec_after);
    assert_norm (to_ops pos_before pos_after ==
                   Ok [ InsertChild "root" (TNode "q" "sec" []); MoveNode "p" "q" ]);
    assert_norm (apply_all [ InsertChild "root" (TNode "q" "sec" []); MoveNode "p" "q" ] pos_before
                 == Ok pos_after)

(* ---- and the hypothesis is NECESSARY, not a convenience: the unrestricted form is FALSE ----

   A pair of well-formed trees sharing a root id, differing ONLY in the kind one shared id
   carries. `toOps` emits the EMPTY script — there is nothing for it to emit, because the
   structural diff reads no content and so emits no `UpdateNode` (Phase 305; "no skeleton
   operation edits a node" until then, false since Phase 250) — so the script is applicable at
   every step (vacuously) and
   lands on `before`, which is not `after`. Every hypothesis of `diff_reconstructs` except
   `kinds_agree` holds here, so this is the exact counterexample to dropping it. It is also why
   `diff_applicable` is the STRONGER of the two theorems in one sense: applicability survives
   the kind disagreement, and only reconstruction fails. *)

let kind_before : tree = TNode "root" "doc" [ TNode "x" "sec" [] ]

let kind_after : tree = TNode "root" "doc" [ TNode "x" "para" [] ]

let kinds_agree_is_necessary ()
  : Lemma (ensures wf kind_before /\ wf kind_after /\
                   tid_of kind_before == tid_of kind_after /\
                   ~(kinds_agree kind_before kind_after) /\
                   to_ops kind_before kind_after == Ok [] /\
                   Ok? (apply_all (Ok?._0 (to_ops kind_before kind_after)) kind_before) /\
                   apply_all (Ok?._0 (to_ops kind_before kind_after)) kind_before
                     =!= Ok kind_after)
  = assert_norm (wf kind_before);
    assert_norm (wf kind_after);
    assert_norm (to_ops kind_before kind_after == Ok []);
    assert_norm (apply_all [] kind_before == Ok kind_before);
    assert_norm (kind_before =!= kind_after);
    assert_norm (mem "x" (ids kind_before));
    assert_norm (mem "x" (ids kind_after));
    assert_norm (kind_at "x" kind_before =!= kind_at "x" kind_after)

(* ======================================================================================
   12. THE PRE-FIX RULE'S COUNTEREXAMPLE (Phase 305) — a contained script IS refused for
       containment, at the step `diff_applicable_contained` cannot see.

      `diff_applicable_contained` (section 8) says every parent address a contained script
      carries resolves in `after` to a node `canHold` accepts. The script runs against `before`.
      Take `before = root(p:para)`, `after = root(p:section(q:para))` and `ch = kind <> "para"`:
      `after` nests nothing under a leaf, so `to_ops_contained` answers `Ok [InsertChild p q]`,
      and `apply_contained_all` refuses that one step with `NotAContainer p "para"`, because the
      `p` it finds is `before`'s. Nothing in the diff is wrong about `after`; what is wrong is the
      sentence this module's header carried until Phase 305 — "cannot be refused for containment
      at any step" — which this lemma pins as false, so it is not re-proved. The repair is
      production's content-aware `Diff.toOpsContainedWith`, which emits `UpdateNode p` FIRST
      (DECISIONS D103) so the insert meets a section; its run theorem —
      `wf b /\ wf a /\ child_blind ch ==> to_ops_contained_with ch b a = Ok s ==>
      apply_contained_all ch s b == Ok a` — needs the content-aware model and the section-10
      induction over six blocks, and it is section 13, below — not restated but REUSED: the
      first block takes `before` to the same tree with those survivors' kinds taken from `after`,
      the four passes emit the same script for that tree (they read no kind), `diff_run` gives
      the plain run there, and the contained engine agrees with it at every step because every
      parent an insert or a move addresses already carries `after`'s kind. The content-aware
      bridge in `ContentDiffTests.fs` and the oracle differential still sample it on the shipped
      code, as every theorem here is.
   ====================================================================================== *)

let pre_fix_before : tree = TNode "root" "doc" [ TNode "p" "para" [] ]

let pre_fix_after : tree = TNode "root" "doc" [ TNode "p" "section" [ TNode "q" "para" [] ] ]

let pre_fix_ch (t:tree) : bool = kind_of t <> "para"

let contained_script_refused_at_before_kinds ()
  : Lemma (ensures wf pre_fix_before /\ wf pre_fix_after /\
                   to_ops_contained pre_fix_ch pre_fix_before pre_fix_after
                     == Ok [ InsertChild "p" (TNode "q" "para" []) ] /\
                   apply_contained_all pre_fix_ch [ InsertChild "p" (TNode "q" "para" []) ] pre_fix_before
                     == Error (NotAContainer "p" "para"))
  = assert_norm (wf pre_fix_before);
    assert_norm (wf pre_fix_after);
    assert_norm (to_ops_contained pre_fix_ch pre_fix_before pre_fix_after
                   == Ok [ InsertChild "p" (TNode "q" "para" []) ]);
    assert_norm (apply_contained_all pre_fix_ch [ InsertChild "p" (TNode "q" "para" []) ] pre_fix_before
                   == Error (NotAContainer "p" "para"))

(* ======================================================================================
   13. THE CONTENT-AWARE RUN THEOREM (Phase 305) — `Diff.toOpsContainedWith` applies, at every
       step, under the predicate it checked, and lands on `after`, content included.

       Section 12 pinned why the structural contained script can be refused: it runs against
       `before`'s kinds. Production's repair (DECISIONS D103.1) emits an `UpdateNode` for every
       survivor whose content changed, in two blocks around the four structural passes — a
       rewrite whose new node `canHold` accepts FIRST, so the inserts and moves under it meet a
       container; every other rewrite LAST, once the children such a node is losing have left.
       The content the witness shows is the kind tag, so `changed_kind` is the injective encoder
       over the two shells, and the theorem is

         wf b /\ wf a /\ tid_of b == tid_of a /\ child_blind ch /\ to_ops_contained_with ch b a == Ok s
           ==>  apply_contained_all ch s b == Ok a

       with NO `kinds_agree`: the rewrites carry the kinds across. The proof is three runs and
       section 10's induction reused rather than restated: (13.4) the first block takes `before`
       to `recolour`, the same tree with those survivors' kinds taken from `after`; (13.6–13.7)
       the structural script is the SAME script for `recolour` as for `before` — the four passes
       read ids, parents and child lists and never a kind — so `diff_run` at `recolour` gives the
       plain run, and the contained engine agrees with the plain one at every step because every
       parent an insert or a move addresses already carries `after`'s kind, which `canHold`
       accepts (`first_non_container` answered `None`); (13.8) the last block rewrites the leaves
       that `canHold` refuses, each of which holds no children by then, and the tree is `after`
       node for node (`tree_ext`).
   ====================================================================================== *)

(* ---- 13.1 the two update blocks and the entry point, clause for clause ---- *)

(* F#: `differs b n` — the caller's encoder over the two shells, which at the witness level reads
   the kind tag. `Map.tryFind (key (w.Id n)) bix.ById` is `find_in` against `before`. *)
let changed_kind (b:tree) (n:tree) : Tot bool =
  match find_in (tid_of n) b with
  | Some bn -> kind_of bn <> kind_of n
  | None -> false

(* F#: block 0 — `UpdateNode n` for every survivor whose content changed and whose new node
   `canHold` accepts, in `after`'s preorder. The payload is the `after` node itself. *)
let rec pass_updates_first (ch:tree -> bool) (b:tree) (ns:list tree) : Tot (list op) (decreases ns) =
  match ns with
  | [] -> []
  | n :: r ->
    if changed_kind b n && ch n then UpdateNode n :: pass_updates_first ch b r
    else pass_updates_first ch b r

(* F#: block 5 — the rewrites `canHold` refuses, last of all. *)
let rec pass_updates_last (ch:tree -> bool) (b:tree) (ns:list tree) : Tot (list op) (decreases ns) =
  match ns with
  | [] -> []
  | n :: r ->
    if changed_kind b n && not (ch n) then UpdateNode n :: pass_updates_last ch b r
    else pass_updates_last ch b r

(* F#: `Diff.toOpsContainedWith canHold encode` — `toOpsContained`'s refusals, in its order, and
   the script with the two blocks around the four passes. *)
let to_ops_contained_with (ch:tree -> bool) (before after:tree) : Tot (outcome (list op) diff_error) =
  match first_non_container ch (pre after) with
  | Some p -> Error (TargetNotAContainer (tid_of p) (kind_of p))
  | None ->
    (match to_ops before after with
     | Error e -> Error e
     | Ok s -> Ok (app (pass_updates_first ch before (pre after))
                      (app s (pass_updates_last ch before (pre after)))))

let rec apply_contained_all_app (ch:tree -> bool) (l m:list op) (t:tree)
  : Lemma (ensures apply_contained_all ch (app l m) t ==
                   (match apply_contained_all ch l t with
                    | Ok t' -> apply_contained_all ch m t'
                    | Error e -> Error e)) (decreases l)
  = match l with
    | [] -> ()
    | o :: r -> (match apply_contained ch o t with
                 | Ok t' -> apply_contained_all_app ch r m t'
                 | Error _ -> ())

(* ---- 13.2 what the first block builds: `before`, recoloured from `after` ---- *)

(* The kind a node of `before` carries once the listed survivors have been rewritten: `after`'s
   where the id is listed, the kinds differ and `ch` accepts the after node; its own otherwise. *)
let recolour_kind (s:list string) (ch:tree -> bool) (a:tree) (i k0:string) : Tot string =
  if mem i s then
    (match find_in i a with
     | Some an -> if kind_of an <> k0 && ch an then kind_of an else k0
     | None -> k0)
  else k0

let rec recolour (s:list string) (ch:tree -> bool) (a:tree) (t:tree) : Tot tree (decreases t) =
  match t with
  | TNode i k0 cs -> TNode i (recolour_kind s ch a i k0) (recolour_all s ch a cs)
and recolour_all (s:list string) (ch:tree -> bool) (a:tree) (ts:list tree) : Tot (list tree) (decreases ts) =
  match ts with
  | [] -> []
  | t :: r -> recolour s ch a t :: recolour_all s ch a r

let tid_recolour (s:list string) (ch:tree -> bool) (a:tree) (t:tree)
  : Lemma (ensures tid_of (recolour s ch a t) == tid_of t) [SMTPat (tid_of (recolour s ch a t))]
  = match t with TNode _ _ _ -> ()

let rec ids_recolour (s:list string) (ch:tree -> bool) (a:tree) (t:tree)
  : Lemma (ensures ids (recolour s ch a t) == ids t) (decreases t)
  = match t with TNode _ _ cs -> ids_all_recolour s ch a cs
and ids_all_recolour (s:list string) (ch:tree -> bool) (a:tree) (ts:list tree)
  : Lemma (ensures ids_all (recolour_all s ch a ts) == ids_all ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> ids_recolour s ch a t; ids_all_recolour s ch a r

let rec wf_recolour (s:list string) (ch:tree -> bool) (a:tree) (t:tree)
  : Lemma (ensures wf (recolour s ch a t) == wf t) (decreases t)
  = match t with TNode _ _ cs -> ids_all_recolour s ch a cs; wf_all_recolour s ch a cs
and wf_all_recolour (s:list string) (ch:tree -> bool) (a:tree) (ts:list tree)
  : Lemma (ensures wf_all (recolour_all s ch a ts) == wf_all ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> wf_recolour s ch a t; wf_all_recolour s ch a r; ids_recolour s ch a t; ids_all_recolour s ch a r

let rec find_recolour (q:string) (s:list string) (ch:tree -> bool) (a:tree) (t:tree)
  : Lemma (ensures find_in q (recolour s ch a t) == (match find_in q t with
                                                     | None -> None
                                                     | Some m -> Some (recolour s ch a m))) (decreases t)
  = match t with
    | TNode i _ cs -> if i = q then () else find_all_recolour q s ch a cs
and find_all_recolour (q:string) (s:list string) (ch:tree -> bool) (a:tree) (ts:list tree)
  : Lemma (ensures find_all q (recolour_all s ch a ts) == (match find_all q ts with
                                                           | None -> None
                                                           | Some m -> Some (recolour s ch a m))) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      find_recolour q s ch a t;
      (match find_in q t with
       | Some _ -> ()
       | None -> find_all_recolour q s ch a r)

let rec kid_ids_recolour_all (s:list string) (ch:tree -> bool) (a:tree) (ts:list tree)
  : Lemma (ensures kid_ids (recolour_all s ch a ts) == kid_ids ts) (decreases ts)
  = match ts with
    | [] -> ()
    | _ :: r -> kid_ids_recolour_all s ch a r

(* The per-node view: children unchanged, the kind as `recolour_kind` says. *)
let recolour_view (s:list string) (ch:tree -> bool) (a:tree) (t:tree) (q:string)
  : Lemma (ensures kids_at q (recolour s ch a t) == kids_at q t /\
                   kind_at q (recolour s ch a t) == (match kind_at q t with
                                                     | None -> None
                                                     | Some k0 -> Some (recolour_kind s ch a q k0)))
  = find_recolour q s ch a t;
    match find_in q t with
    | None -> ()
    | Some m ->
      find_in_id q t m;
      (match m with TNode _ _ mcs -> kid_ids_recolour_all s ch a mcs)

(* Nothing listed, nothing recoloured. *)
let rec recolour_nil (ch:tree -> bool) (a:tree) (t:tree)
  : Lemma (ensures recolour [] ch a t == t) (decreases t)
  = match t with TNode _ _ cs -> recolour_all_nil ch a cs
and recolour_all_nil (ch:tree -> bool) (a:tree) (ts:list tree)
  : Lemma (ensures recolour_all [] ch a ts == ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> recolour_nil ch a t; recolour_all_nil ch a r

(* ---- 13.3 the four passes do not see a kind: the structural script is the same ---- *)

let rec recolour_all_app (s:list string) (ch:tree -> bool) (a:tree) (l m:list tree)
  : Lemma (ensures recolour_all s ch a (app l m) == app (recolour_all s ch a l) (recolour_all s ch a m))
          (decreases l)
  = match l with
    | [] -> ()
    | _ :: r -> recolour_all_app s ch a r m

let rec pre_recolour (s:list string) (ch:tree -> bool) (a:tree) (t:tree)
  : Lemma (ensures pre (recolour s ch a t) == recolour_all s ch a (pre t)) (decreases t)
  = match t with TNode _ _ cs -> pre_all_recolour s ch a cs
and pre_all_recolour (s:list string) (ch:tree -> bool) (a:tree) (ts:list tree)
  : Lemma (ensures pre_all (recolour_all s ch a ts) == recolour_all s ch a (pre_all ts)) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      pre_recolour s ch a t;
      pre_all_recolour s ch a r;
      recolour_all_app s ch a (pre t) (pre_all r)

let rec tids_recolour_all (s:list string) (ch:tree -> bool) (a:tree) (l:list tree)
  : Lemma (ensures tids (recolour_all s ch a l) == tids l) (decreases l)
  = match l with
    | [] -> ()
    | _ :: r -> tids_recolour_all s ch a r

let rec kid_pairs_recolour_all (p:string) (s:list string) (ch:tree -> bool) (a:tree) (cs:list tree)
  : Lemma (ensures kid_pairs p (recolour_all s ch a cs) == kid_pairs p cs) (decreases cs)
  = match cs with
    | [] -> ()
    | _ :: r -> kid_pairs_recolour_all p s ch a r

let rec parent_map_recolour_all (s:list string) (ch:tree -> bool) (a:tree) (l:list tree)
  : Lemma (ensures parent_map (recolour_all s ch a l) == parent_map l) (decreases l)
  = match l with
    | [] -> ()
    | p :: r ->
      (match p with TNode _ _ cs -> kid_pairs_recolour_all (tid_of p) s ch a cs);
      parent_map_recolour_all s ch a r

let rec kid_map_recolour_all (s:list string) (ch:tree -> bool) (a:tree) (l:list tree)
  : Lemma (ensures kid_map (recolour_all s ch a l) == kid_map l) (decreases l)
  = match l with
    | [] -> ()
    | n :: r ->
      (match n with TNode _ _ cs -> kid_ids_recolour_all s ch a cs);
      kid_map_recolour_all s ch a r

(* Pass 3 reads a node list through its ids alone. *)
let rec pass_removes_tids (a_ids:list string) (b_par:list (string & string)) (ns ns':list tree)
  : Lemma (requires tids ns == tids ns')
          (ensures pass_removes a_ids b_par ns == pass_removes a_ids b_par ns') (decreases ns)
  = match ns, ns' with
    | [], [] -> ()
    | _ :: r, _ :: r' -> pass_removes_tids a_ids b_par r r'
    | _, _ -> ()

let diff_blocks_recolour (s:list string) (ch:tree -> bool) (a b:tree)
  : Lemma (ensures diff_blocks (recolour s ch a b) a == diff_blocks b a)
  = pre_recolour s ch a b;
    ids_recolour s ch a b;
    parent_map_recolour_all s ch a (pre b);
    kid_map_recolour_all s ch a (pre b);
    tids_recolour_all s ch a (pre b);
    pass_removes_tids (ids a) (parent_map (pre b)) (recolour_all s ch a (pre b)) (pre b)

let to_ops_recolour (s:list string) (ch:tree -> bool) (a b:tree)
  : Lemma (ensures to_ops (recolour s ch a b) a == to_ops b a)
  = ids_recolour s ch a b;
    diff_blocks_recolour s ch a b

(* ---- 13.4 the first block, step by step ---- *)

(* A rewrite of a listed survivor whose after node `ch` accepts extends the recolouring by its id. *)
let rec upd_recolour_step (s:list string) (ch:tree -> bool) (a:tree) (k:string) (an:tree) (t:tree)
  : Lemma (requires find_in k a == Some an /\ ch an)
          (ensures upd k (kind_of an) (recolour s ch a t) == recolour (app s [k]) ch a t) (decreases t)
  = match t with
    | TNode i _ cs ->
      mem_app i s [k];
      upd_recolour_step_all s ch a k an cs
and upd_recolour_step_all (s:list string) (ch:tree -> bool) (a:tree) (k:string) (an:tree) (ts:list tree)
  : Lemma (requires find_in k a == Some an /\ ch an)
          (ensures upd_all k (kind_of an) (recolour_all s ch a ts) == recolour_all (app s [k]) ch a ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> upd_recolour_step s ch a k an t; upd_recolour_step_all s ch a k an r

(* Listing an id that is not in the tree changes nothing ... *)
let rec recolour_absent_ext (s:list string) (ch:tree -> bool) (a:tree) (k:string) (t:tree)
  : Lemma (requires not (mem k (ids t)))
          (ensures recolour (app s [k]) ch a t == recolour s ch a t) (decreases t)
  = match t with
    | TNode i _ cs -> mem_app i s [k]; recolour_absent_ext_all s ch a k cs
and recolour_absent_ext_all (s:list string) (ch:tree -> bool) (a:tree) (k:string) (ts:list tree)
  : Lemma (requires not (mem k (ids_all ts)))
          (ensures recolour_all (app s [k]) ch a ts == recolour_all s ch a ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      mem_app k (ids t) (ids_all r);
      recolour_absent_ext s ch a k t;
      recolour_absent_ext_all s ch a k r

(* ... nor one whose after node `ch` refuses ... *)
let rec recolour_skip_refused (s:list string) (ch:tree -> bool) (a:tree) (k:string) (an:tree) (t:tree)
  : Lemma (requires find_in k a == Some an /\ not (ch an))
          (ensures recolour (app s [k]) ch a t == recolour s ch a t) (decreases t)
  = match t with
    | TNode i _ cs -> mem_app i s [k]; recolour_skip_refused_all s ch a k an cs
and recolour_skip_refused_all (s:list string) (ch:tree -> bool) (a:tree) (k:string) (an:tree) (ts:list tree)
  : Lemma (requires find_in k a == Some an /\ not (ch an))
          (ensures recolour_all (app s [k]) ch a ts == recolour_all s ch a ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r -> recolour_skip_refused s ch a k an t; recolour_skip_refused_all s ch a k an r

(* ... nor one whose kind did not change. *)
let rec recolour_skip_same (s:list string) (ch:tree -> bool) (a:tree) (k:string) (an bn:tree) (t:tree)
  : Lemma (requires wf t /\ find_in k a == Some an /\ find_in k t == Some bn /\ kind_of bn == kind_of an)
          (ensures recolour (app s [k]) ch a t == recolour s ch a t) (decreases t)
  = match t with
    | TNode i _ cs ->
      mem_app i s [k];
      if i = k then recolour_absent_ext_all s ch a k cs
      else recolour_skip_same_all s ch a k an bn cs
and recolour_skip_same_all (s:list string) (ch:tree -> bool) (a:tree) (k:string) (an bn:tree) (ts:list tree)
  : Lemma (requires wf_all ts /\ find_in k a == Some an /\ find_all k ts == Some bn /\ kind_of bn == kind_of an)
          (ensures recolour_all (app s [k]) ch a ts == recolour_all s ch a ts) (decreases ts)
  = match ts with
    | [] -> ()
    | t :: r ->
      find_in_some_iff k t;
      inter_nil_iff (ids t) (ids_all r);
      (match find_in k t with
       | Some _ ->
         recolour_skip_same s ch a k an bn t;
         recolour_absent_ext_all s ch a k r
       | None ->
         recolour_absent_ext s ch a k t;
         recolour_skip_same_all s ch a k an bn r)

(* One accepted rewrite of the first block extends the recolouring by its id. *)
#push-options "--fuel 1 --ifuel 1"
let update_first_step (ch:tree -> bool) (b a:tree) (s:list string) (n:tree)
  : Lemma (requires child_blind ch /\ wf b /\ wf a /\ mem n (pre a) /\ changed_kind b n /\ ch n)
          (ensures apply_contained ch (UpdateNode n) (recolour s ch a b)
                   == Ok (recolour (app s [tid_of n]) ch a b))
  = let k = tid_of n in
    pre_find a n;
    let t = recolour s ch a b in
    ids_recolour s ch a b;
    find_in_some_iff k b;
    find_in_some_iff k t;
    find_recolour k s ch a b;
    match find_in k t, n with
    | Some ex, TNode _ kn ncs ->
      assert (ch (TNode k kn (kids_of ex)) == ch (TNode k kn ncs));
      upd_recolour_step s ch a k n b
    | _, _ -> ()

(* A node the first block skips leaves the recolouring where it was. *)
let update_first_skip (ch:tree -> bool) (b a:tree) (s:list string) (n:tree)
  : Lemma (requires wf b /\ wf a /\ mem n (pre a) /\ not (changed_kind b n && ch n))
          (ensures recolour (app s [tid_of n]) ch a b == recolour s ch a b)
  = let k = tid_of n in
    pre_find a n;
    find_in_some_iff k b;
    if ch n then
      (match find_in k b with
       | None -> recolour_absent_ext s ch a k b
       | Some bn -> recolour_skip_same s ch a k n bn b)
    else recolour_skip_refused s ch a k n b
#pop-options

let rec updates_first_run (ch:tree -> bool) (b a:tree) (done ns:list tree)
  : Lemma (requires child_blind ch /\ wf b /\ wf a /\ pre a == app done ns)
          (ensures apply_contained_all ch (pass_updates_first ch b ns) (recolour (tids done) ch a b)
                   == Ok (recolour (tids (app done ns)) ch a b))
          (decreases ns)
  = match ns with
    | [] -> app_nil_r done
    | n :: r ->
      app_assoc done [n] r;
      mem_app_r_tree done (n :: r) n;
      tids_app done [n];
      if changed_kind b n && ch n then update_first_step ch b a (tids done) n
      else update_first_skip ch b a (tids done) n;
      updates_first_run ch b a (app done [n]) r

(* ---- 13.5 the recoloured tree carries `after`'s kind at every after-parent ---- *)

let rec first_non_container_none_mem (ch:tree -> bool) (ns:list tree) (n:tree)
  : Lemma (requires first_non_container ch ns == None /\ mem n ns /\ Cons? (kids_of n))
          (ensures ch n) (decreases ns)
  = match ns with
    | [] -> ()
    | x :: r -> if x = n then () else first_non_container_none_mem ch r n

(* `parent_accepts` names a node of `after`; under `wf` it is the one `find_in` answers with. *)
let rec parent_accepts_find (ch:tree -> bool) (a:tree) (ns:list tree) (x np:string)
  : Lemma (requires wf a /\ parent_accepts ch ns x np /\ (forall (n:tree). mem n ns ==> mem n (pre a)))
          (ensures (match find_in np a with
                    | Some m -> ch m /\ mem x (kid_ids (kids_of m))
                    | None -> False)) (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r ->
      if tid_of n = np && mem x (kid_ids (kids_of n)) && ch n then pre_find a n
      else parent_accepts_find ch a r x np

(* The kind `kind_src` reads for an after-parent is `after`'s, whichever tree it reads it from. *)
let recolour_parent_kind (ch:tree -> bool) (b a:tree) (p:string)
  : Lemma (requires wf a /\ first_non_container ch (pre a) == None /\
                    mem p (ids a) /\ Cons? (kids_at p a))
          (ensures kind_src (recolour (ids a) ch a b) a p == kind_at p a)
  = let b1 = recolour (ids a) ch a b in
    ids_recolour (ids a) ch a b;
    recolour_view (ids a) ch a b p;
    find_in_some_iff p a;
    find_in_some_iff p b;
    match find_in p a with
    | Some an ->
      find_in_mem_pre p a an;
      find_in_id p a an;
      (match an with TNode _ _ acs -> first_non_container_none_mem ch (pre a) an)
    | None -> ()

(* ---- 13.6 the structural script keeps every surviving node's kind ---- *)

(* The kind invariant section 10 carries, stated on its own: every node of `t` reads its kind
   from `kind_src`. *)
let kinds_like (b1 a:tree) (t:tree) : prop =
  wf t /\ (forall (q:string). mem q (ids t) ==> kind_at q t == kind_src b1 a q)

(* An emitted insert's shell carries `after`'s kind for its id. *)
let after_kind (a:tree) (o:op) : Tot bool =
  match o with
  | InsertChild _ n -> kind_at (tid_of n) a = Some (kind_of n)
  | _ -> true

let rec pass_inserts_after_kind (a:tree) (a_par:list (string & string)) (b_ids:list string) (ns:list tree)
  : Lemma (requires wf a /\ (forall (n:tree). mem n ns ==> mem n (pre a)))
          (ensures all_ops (after_kind a) (pass_inserts a_par b_ids ns)) (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r ->
      kids_at_pre a n;
      pass_inserts_after_kind a a_par b_ids r

(* The four passes emit the four structural operations and nothing else. *)
let structural_op (o:op) : Tot bool =
  match o with
  | Batch _ | UpdateNode _ -> false
  | _ -> true

let rec all_ops_app_both (p:op -> bool) (l m:list op)
  : Lemma (requires all_ops p l /\ all_ops p m) (ensures all_ops p (app l m)) (decreases l)
  = match l with
    | [] -> ()
    | _ :: r -> all_ops_app_both p r m

let diff_script_after_kind (b a:tree)
  : Lemma (requires wf a /\ Ok? (to_ops b a))
          (ensures all_ops (after_kind a) (Ok?._0 (to_ops b a)))
  = let b_nodes = pre b in
    let a_nodes = pre a in
    let a_par = parent_map a_nodes in
    let b_par = parent_map b_nodes in
    let b_kids = kid_map b_nodes in
    let p1 = pass_inserts a_par (ids b) a_nodes in
    let pm = pass_moves (ids b) b_par b_kids a_nodes in
    let p2 = fst pm in
    let p3 = pass_removes (ids a) b_par b_nodes in
    let p4 = pass_reorders (ids b) b_kids a (snd pm) in
    pass_inserts_after_kind a a_par (ids b) a_nodes;
    pass_moves_all (ids b) b_par b_kids a_nodes;
    pass_removes_all (ids a) b_par b_nodes;
    pass_reorders_all (ids b) b_kids a (snd pm);
    all_ops_weaken is_move (after_kind a) p2;
    all_ops_weaken is_remove (after_kind a) p3;
    all_ops_weaken is_reorder (after_kind a) p4;
    all_ops_app_both (after_kind a) p3 p4;
    all_ops_app_both (after_kind a) p2 (app p3 p4);
    all_ops_app_both (after_kind a) p1 (app p2 (app p3 p4))

let diff_script_structural (b a:tree)
  : Lemma (requires Ok? (to_ops b a))
          (ensures all_ops structural_op (Ok?._0 (to_ops b a)))
  = let b_nodes = pre b in
    let a_nodes = pre a in
    let a_par = parent_map a_nodes in
    let b_par = parent_map b_nodes in
    let b_kids = kid_map b_nodes in
    let p1 = pass_inserts a_par (ids b) a_nodes in
    let pm = pass_moves (ids b) b_par b_kids a_nodes in
    let p2 = fst pm in
    let p3 = pass_removes (ids a) b_par b_nodes in
    let p4 = pass_reorders (ids b) b_kids a (snd pm) in
    pass_inserts_all a_par (ids b) a_nodes;
    pass_moves_all (ids b) b_par b_kids a_nodes;
    pass_removes_all (ids a) b_par b_nodes;
    pass_reorders_all (ids b) b_kids a (snd pm);
    all_ops_weaken is_insert structural_op p1;
    all_ops_weaken is_move structural_op p2;
    all_ops_weaken is_remove structural_op p3;
    all_ops_weaken is_reorder structural_op p4;
    all_ops_app_both structural_op p3 p4;
    all_ops_app_both structural_op p2 (app p3 p4);
    all_ops_app_both structural_op p1 (app p2 (app p3 p4))

(* A leaf's ids are its own id alone, and it answers for itself. *)
let ids_leaf (n:tree)
  : Lemma (requires Nil? (kids_of n))
          (ensures ids n == [tid_of n] /\ kind_at (tid_of n) n == Some (kind_of n))
  = match n with TNode _ _ _ -> ()

#push-options "--fuel 1 --ifuel 1 --z3rlimit 100"
(* One structural step keeps the invariant: a surviving node keeps its kind (the per-node views),
   and an inserted shell arrives with `after`'s. *)
let structural_step_kinds (b1 a:tree) (o:op) (t t':tree)
  : Lemma (requires wf a /\ kinds_like b1 a t /\ structural_op o /\ script_shape b1 a o /\
                    after_kind a o /\ apply o t == Ok t')
          (ensures kinds_like b1 a t')
  = apply_preserves_wf o t;
    match o with
    | InsertChild p n ->
      let k = tid_of n in
      ids_leaf n;
      first_dup_none_iff n t;
      inter_nil_iff (ids n) (ids t);
      find_in_some_iff p t;
      let aux (q:string) : Lemma (mem q (ids t') ==> kind_at q t' == kind_src b1 a q) =
        if mem q (ids t') then begin
          ids_ins_mem q p n t;
          if mem q (ids t) then ins_view p n t q
          else begin
            ins_view_inside p n t q;
            (match n with TNode _ _ _ -> ())
          end
        end
        else ()
      in
      FStar.Classical.forall_intro aux
    | RemoveNode x ->
      find_in_some_iff x t;
      parent_exists x t;
      (match parent_of x t, find_in x t with
       | Some pid, Some sub ->
         let aux (q:string) : Lemma (mem q (ids t') ==> kind_at q t' == kind_src b1 a q) =
           if mem q (ids t') then begin
             ids_rem_sub q pid x t;
             rem_kills pid x t sub;
             rem_view pid x t sub q
           end
           else ()
         in
         FStar.Classical.forall_intro aux
       | _, _ -> ())
    | MoveNode x np ->
      find_in_some_iff x t;
      parent_exists x t;
      (match find_in x t, parent_of x t with
       | Some sub, Some pid ->
         move_accepted x np t;
         let aux (q:string) : Lemma (mem q (ids t') ==> kind_at q t' == kind_src b1 a q) =
           if mem q (ids t') then begin
             move_ids x np t pid sub q;
             move_view x np t pid sub q
           end
           else ()
         in
         FStar.Classical.forall_intro aux
       | _, _ -> ())
    | ReorderChildren p ord ->
      find_in_some_iff p t;
      let aux (q:string) : Lemma (mem q (ids t') ==> kind_at q t' == kind_src b1 a q) =
        if mem q (ids t') then reorder_view p ord t q else ()
      in
      FStar.Classical.forall_intro aux
    | _ -> ()
#pop-options

(* ---- 13.7 the contained engine agrees with the plain one on the structural script ---- *)

(* Every parent an insert or a move of the diff addresses is an after-parent `ch` accepts. *)
let shape_to_contained (ch:tree -> bool) (b1 a:tree) (o:op)
  : Lemma (requires first_non_container ch (pre a) == None /\ script_shape b1 a o)
          (ensures contained_shape ch (pre a) o)
  = match o with
    | InsertChild p n -> lookup_parent_map (pre a) (tid_of n) p;
                        after_child_accepts ch (pre a) (tid_of n) p
    | MoveNode x np -> after_child_accepts ch (pre a) x np
    | _ -> ()

(* A childless graft has no interior to walk. *)
let first_uncontained_leaf (ch:tree -> bool) (n:tree)
  : Lemma (requires Nil? (kids_of n)) (ensures first_uncontained ch n == None)
  = match n with TNode _ _ _ -> ()

(* A list holding an element is not empty. *)
let mem_cons (x:string) (l:list string)
  : Lemma (requires mem x l) (ensures Cons? l)
  = ()

#push-options "--fuel 1 --ifuel 1 --z3rlimit 100"
(* One structural step: the contained engine takes it exactly as the plain one does, because the
   parent it addresses already carries `after`'s kind, which `ch` accepts. *)
let contained_step (ch:tree -> bool) (b1 a:tree) (o:op) (t:tree)
  : Lemma (requires child_blind ch /\ wf a /\ first_non_container ch (pre a) == None /\
                    kinds_like b1 a t /\ structural_op o /\ script_shape b1 a o /\
                    (forall (p:string). mem p (ids a) /\ Cons? (kids_at p a) ==> kind_src b1 a p == kind_at p a) /\
                    Ok? (apply o t))
          (ensures apply_contained ch o t == apply o t)
  = shape_to_contained ch b1 a o;
    match o with
    | InsertChild p n ->
      let k = tid_of n in
      find_in_some_iff p t;
      find_in_some_iff p a;
      first_uncontained_leaf ch n;
      parent_accepts_find ch a (pre a) k p;
      (match find_in p t, find_in p a with
       | Some pn, Some am ->
         find_in_id p t pn;
         find_in_id p a am;
         (match pn, am with
          | TNode _ pk pcs, TNode _ ak acs ->
            mem_cons k (kid_ids acs);
            assert (ch (TNode p ak acs) == ch (TNode p ak pcs)))
       | _, _ -> ())
    | MoveNode x np ->
      find_in_some_iff np t;
      find_in_some_iff np a;
      parent_accepts_find ch a (pre a) x np;
      (match find_in np t, find_in np a with
       | Some pn, Some am ->
         find_in_id np t pn;
         find_in_id np a am;
         (match pn, am with
          | TNode _ pk pcs, TNode _ ak acs ->
            mem_cons x (kid_ids acs);
            assert (ch (TNode np ak acs) == ch (TNode np ak pcs)))
       | _, _ -> ())
    | _ -> ()
#pop-options

let rec contained_run (ch:tree -> bool) (b1 a:tree) (s:list op) (t:tree)
  : Lemma (requires child_blind ch /\ wf a /\ first_non_container ch (pre a) == None /\
                    kinds_like b1 a t /\
                    all_ops structural_op s /\ all_ops (script_shape b1 a) s /\ all_ops (after_kind a) s /\
                    (forall (p:string). mem p (ids a) /\ Cons? (kids_at p a) ==> kind_src b1 a p == kind_at p a) /\
                    Ok? (apply_all s t))
          (ensures apply_contained_all ch s t == apply_all s t) (decreases s)
  = match s with
    | [] -> ()
    | o :: r ->
      contained_step ch b1 a o t;
      (match apply o t with
       | Ok t' ->
         structural_step_kinds b1 a o t t';
         contained_run ch b1 a r t'
       | Error _ -> ())

(* ---- 13.8 the last block: the leaves `ch` refuses, rewritten once their children have left ---- *)

(* The state after the structural script, as section 10 leaves it, read for this block: the
   children of every after node are `after`'s and the kinds are `kind_src`'s. *)
let settled (b1 a f:tree) (done:list string) (t:tree) : prop =
  wf t /\ tid_of t == tid_of a /\
  (forall (q:string). mem q (ids a) ==> mem q (ids t)) /\
  (forall (q:string). mem q (ids a) ==> kids_at q t == kids_at q a) /\
  (forall (q:string). mem q (ids a) ==>
     kind_at q t == (if mem q done then kind_at q a else kind_at q f))

(* A skipped after node already carries its kind: not a survivor, unchanged, or rewritten by the
   first block. *)
let skipped_kind_is_after (ch:tree -> bool) (b a f:tree) (n:tree)
  : Lemma (requires wf b /\ wf a /\ mem n (pre a) /\ not (changed_kind b n && not (ch n)) /\
                    kind_at (tid_of n) f == kind_src (recolour (ids a) ch a b) a (tid_of n))
          (ensures kind_at (tid_of n) f == kind_at (tid_of n) a)
  = let k = tid_of n in
    pre_find a n;
    find_in_some_iff k a;
    ids_is_pre a;
    ids_recolour (ids a) ch a b;
    recolour_view (ids a) ch a b k;
    find_in_some_iff k b;
    match find_in k b with
    | Some bn ->
      find_in_id k b bn;
      (match bn, n with
       | TNode _ k0 _, TNode _ kn _ ->
         assert (kind_at k b == Some k0);
         assert (kind_at k a == Some kn))
    | None -> ()

let mem_single (q k:string) : Lemma (ensures mem q [k] == (q = k)) = ()

#push-options "--fuel 1 --ifuel 1 --z3rlimit 100"
(* One rewrite of the last block: the node holds no children by now (it is a leaf of `after`
   that `ch` refuses), so the rewrite is accepted, and only its kind moves. *)
let update_last_step (ch:tree -> bool) (b a f:tree) (done:list string) (n:tree) (t:tree)
  : Lemma (requires wf a /\ first_non_container ch (pre a) == None /\ mem n (pre a) /\
                    changed_kind b n /\ not (ch n) /\
                    settled (recolour (ids a) ch a b) a f done t)
          (ensures (let k = tid_of n in
                    apply_contained ch (UpdateNode n) t == Ok (upd k (kind_of n) t) /\
                    settled (recolour (ids a) ch a b) a f (app done [k]) (upd k (kind_of n) t)))
  = let k = tid_of n in
    pre_find a n;
    find_in_some_iff k a;
    ids_is_pre a;
    kids_at_pre a n;
    (if Cons? (kids_of n) then first_non_container_none_mem ch (pre a) n else ());
    assert (kids_at k a == []);
    find_in_some_iff k t;
    match find_in k t with
    | Some ex ->
      find_in_id k t ex;
      assert (kids_at k t == kid_ids (kids_of ex));
      (match ex with
       | TNode _ _ [] -> ()
       | TNode _ _ (c :: _) -> ());
      upd_wf k (kind_of n) t;
      ids_upd k (kind_of n) t;
      let t' = upd k (kind_of n) t in
      let aux (q:string)
        : Lemma (kids_at q t' == kids_at q t /\
                 (mem q (ids a) ==>
                  kind_at q t' == (if mem q (app done [k]) then kind_at q a else kind_at q f)))
        = upd_view k (kind_of n) t q;
          find_in_some_iff q t;
          mem_app q done [k];
          mem_single q k
      in
      FStar.Classical.forall_intro aux
    | None -> ()

(* A node the last block skips already carries `after`'s kind. *)
let update_last_skip (ch:tree -> bool) (b a f:tree) (done:list string) (n:tree) (t:tree)
  : Lemma (requires wf b /\ wf a /\ mem n (pre a) /\ not (changed_kind b n && not (ch n)) /\
                    (forall (q:string). mem q (ids a) ==> kind_at q f == kind_src (recolour (ids a) ch a b) a q) /\
                    settled (recolour (ids a) ch a b) a f done t)
          (ensures settled (recolour (ids a) ch a b) a f (app done [tid_of n]) t)
  = let k = tid_of n in
    pre_find a n;
    find_in_some_iff k a;
    ids_is_pre a;
    skipped_kind_is_after ch b a f n;
    let aux (q:string)
      : Lemma (mem q (ids a) ==> kind_at q t == (if mem q (app done [k]) then kind_at q a else kind_at q f))
      = mem_app q done [k];
        mem_single q k
    in
    FStar.Classical.forall_intro aux
#pop-options

let rec updates_last_run (ch:tree -> bool) (b a f:tree) (done ns:list tree) (t:tree)
  : Lemma (requires child_blind ch /\ wf b /\ wf a /\ first_non_container ch (pre a) == None /\
                    pre a == app done ns /\
                    (forall (q:string). mem q (ids a) ==> kind_at q f == kind_src (recolour (ids a) ch a b) a q) /\
                    settled (recolour (ids a) ch a b) a f (tids done) t)
          (ensures (match apply_contained_all ch (pass_updates_last ch b ns) t with
                    | Ok g -> settled (recolour (ids a) ch a b) a f (tids (app done ns)) g
                    | Error _ -> False))
          (decreases ns)
  = match ns with
    | [] -> app_nil_r done
    | n :: r ->
      app_assoc done [n] r;
      mem_app_r_tree done (n :: r) n;
      tids_app done [n];
      if changed_kind b n && not (ch n) then begin
        update_last_step ch b a f (tids done) n t;
        updates_last_run ch b a f (app done [n]) r (upd (tid_of n) (kind_of n) t)
      end
      else begin
        update_last_skip ch b a f (tids done) n t;
        updates_last_run ch b a f (app done [n]) r t
      end

(* ---- 13.9 THE THEOREM ---- *)

#push-options "--fuel 1 --ifuel 1 --z3rlimit 100"
let diff_applicable_contained_run (ch:tree -> bool) (b a:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ child_blind ch /\
                    Ok? (to_ops_contained_with ch b a))
          (ensures apply_contained_all ch (Ok?._0 (to_ops_contained_with ch b a)) b == Ok a)
  = diff_ok_on_any_wf_pair b a;
    assert (first_non_container ch (pre a) == None);
    let s = Ok?._0 (to_ops b a) in
    let u1 = pass_updates_first ch b (pre a) in
    let u2 = pass_updates_last ch b (pre a) in
    assert (to_ops_contained_with ch b a == Ok (app u1 (app s u2)));
    assert (s == script_of (diff_blocks b a));
    let b1 = recolour (ids a) ch a b in
    ids_is_pre a;
    (* block 0: `before` to the recoloured tree *)
    recolour_nil ch a b;
    updates_first_run ch b a [] (pre a);
    assert (apply_contained_all ch u1 b == Ok b1);
    apply_contained_all_app ch u1 (app s u2) b;
    (* the structural script, at the recoloured tree: the same script, the plain run, and the
       contained engine agreeing with it *)
    wf_recolour (ids a) ch a b;
    ids_recolour (ids a) ch a b;
    to_ops_recolour (ids a) ch a b;
    diff_blocks_recolour (ids a) ch a b;
    diff_run b1 a;
    assert (kinds_like b1 a b1);
    diff_script_shape b1 a;
    diff_script_after_kind b1 a;
    diff_script_structural b1 a;
    let parents (p:string) : Lemma (mem p (ids a) /\ Cons? (kids_at p a) ==> kind_src b1 a p == kind_at p a) =
      FStar.Classical.move_requires (recolour_parent_kind ch b a) p
    in
    FStar.Classical.forall_intro parents;
    apply_contained_all_app ch s u2 b1;
    match apply_all s b1 with
    | Ok f ->
      contained_run ch b1 a s b1;
      assert (apply_contained_all ch s b1 == Ok f);
      (* block 5: the leaves `ch` refuses, and the tree is `after` *)
      assert (settled b1 a f [] f);
      updates_last_run ch b a f [] (pre a) f;
      (match apply_contained_all ch u2 f with
       | Ok g ->
         find_in_self (tid_of a) a;
         find_in_self (tid_of a) g;
         tree_ext a g a g;
         assert (g == a)
       | Error _ -> ())
    | Error _ -> ()
#pop-options

(* ---- 13.10 and the theorem is NOT VACUOUS: section 12's pair, repaired ---- *)

(* The exact pair the structural contained diff is refused on (section 12): the content-aware form
   rewrites `p` FIRST, so the insert under it meets a section, and the script lands on `after`. *)
let content_aware_script_applies_where_the_structural_one_is_refused ()
  : Lemma (ensures to_ops_contained_with pre_fix_ch pre_fix_before pre_fix_after
                     == Ok [ UpdateNode (TNode "p" "section" [ TNode "q" "para" [] ]);
                             InsertChild "p" (TNode "q" "para" []) ] /\
                   apply_contained_all pre_fix_ch
                     [ UpdateNode (TNode "p" "section" [ TNode "q" "para" [] ]);
                       InsertChild "p" (TNode "q" "para" []) ] pre_fix_before
                     == Ok pre_fix_after)
  = assert_norm (to_ops_contained_with pre_fix_ch pre_fix_before pre_fix_after
                   == Ok [ UpdateNode (TNode "p" "section" [ TNode "q" "para" [] ]);
                           InsertChild "p" (TNode "q" "para" []) ]);
    assert_norm (apply_contained_all pre_fix_ch
                   [ UpdateNode (TNode "p" "section" [ TNode "q" "para" [] ]);
                     InsertChild "p" (TNode "q" "para" []) ] pre_fix_before
                   == Ok pre_fix_after)

(* ======================================================================================
   14. Digests (F#: `Tree.ownDigest`, `Tree.digests`, `Tree.Digests.diff` in Tree.fs — Phase 314).

      WHAT IS MODELLED. The own digest of a node — the hash of the field encoding of its id, its
      kind and the caller's encoder over its SHELL — and the Merkle subtree digest: the hash of the
      field encoding of the own digest followed by each child's subtree digest, in order. The HASH
      `h` (F#: `Hash.sha256Hex`) and the FIELD ENCODING `cf` (F#: `Hash.canonicalFields`) enter as
      PARAMETERS with their injectivity as hypotheses: the hash's is the standing assumption every
      digest on the spine rests on (an unseen difference is a SHA-256 collision), the encoding's is
      proved (`invocation_key_injective`, `proofs/Query.fst`). The encoder `enc` enters with NO
      hypothesis — the id and the kind are fields of the own pre-image in their own right, so the
      STRUCTURE of a tree is recovered from its digest whatever the encoder says, and what the
      encoder adds is the content beyond the kind, which this model's tree does not carry;
      `own_digest_recovers_shell` is the clause a domain's `encoderInjectivityLaws` extends to it.

      WHAT IS PROVED.
        - `subtree_digest_injective` — two trees with one subtree digest are one tree: the Merkle
          rollup loses nothing of ids, kinds or shape. The document domain's roadmap had queued
          this obligation against its own copy of the rollup; it is discharged here once.
        - `digest_partition` — `Digests.diff`'s four lists, modelled over the id lists of the two
          trees, PARTITION the union of the two id sets: every id of either tree is in exactly one
          list, no other id is in any, and under `wf` each list carries an id once. The lists'
          ascending order is a rendering of the F# maps the model does not carry.
      Both are evaluated on concrete pairs below (`digest_theorems_are_not_vacuous`) and the diff
      is sampled by the twins at the end of the module.
   ====================================================================================== *)

(* F#: `Tree.ownDigest` — `sha256Hex (canonicalFields [ idKey; kindTag; encode shell ])`; `shell`
   is section 2's, the same `ReplaceChildren n []` the inserted graft is. *)
let own_digest (h:string -> string) (cf:list string -> string) (enc:tree -> string) (t:tree)
  : Tot string =
  h (cf [tid_of t; kind_of t; enc (shell t)])

(* F#: the `Subtree` map of `Tree.digests` at a node — the own digest, then each child's subtree
   digest in order, through the field encoding and the hash. *)
let rec subtree_digest (h:string -> string) (cf:list string -> string) (enc:tree -> string) (t:tree)
  : Tot string (decreases t) =
  match t with
  | TNode _ _ cs -> h (cf (own_digest h cf enc t :: subtree_digests h cf enc cs))
and subtree_digests (h:string -> string) (cf:list string -> string) (enc:tree -> string) (ts:list tree)
  : Tot (list string) (decreases ts) =
  match ts with
  | [] -> []
  | t :: r -> subtree_digest h cf enc t :: subtree_digests h cf enc r

(* One instance of an injectivity hypothesis, named so no query has to find the trigger. *)
let inj_at (#a #b:eqtype) (f:a -> b) (x y:a)
  : Lemma (requires injective f /\ f x == f y) (ensures x == y)
  = ()

(* ---- equal own digests recover the id, the kind and the encoded shell ---- *)
let own_digest_recovers_shell (h:string -> string) (cf:list string -> string) (enc:tree -> string)
                              (t u:tree)
  : Lemma (requires injective h /\ injective cf /\ own_digest h cf enc t == own_digest h cf enc u)
          (ensures tid_of t == tid_of u /\ kind_of t == kind_of u /\ enc (shell t) == enc (shell u))
  = inj_at h (cf [tid_of t; kind_of t; enc (shell t)]) (cf [tid_of u; kind_of u; enc (shell u)]);
    inj_at cf [tid_of t; kind_of t; enc (shell t)] [tid_of u; kind_of u; enc (shell u)]

(* ---- THE THEOREM: the Merkle digest is injective over trees ---- *)
let rec subtree_digest_injective (h:string -> string) (cf:list string -> string) (enc:tree -> string)
                                 (t u:tree)
  : Lemma (requires injective h /\ injective cf /\
                    subtree_digest h cf enc t == subtree_digest h cf enc u)
          (ensures t == u) (decreases t)
  = match t, u with
    | TNode _ _ cs, TNode _ _ cs' ->
      let l1 = own_digest h cf enc t :: subtree_digests h cf enc cs in
      let l2 = own_digest h cf enc u :: subtree_digests h cf enc cs' in
      inj_at h (cf l1) (cf l2);
      inj_at cf l1 l2;
      own_digest_recovers_shell h cf enc t u;
      subtree_digests_injective h cf enc cs cs'
and subtree_digests_injective (h:string -> string) (cf:list string -> string) (enc:tree -> string)
                              (ts us:list tree)
  : Lemma (requires injective h /\ injective cf /\
                    subtree_digests h cf enc ts == subtree_digests h cf enc us)
          (ensures ts == us) (decreases ts)
  = match ts, us with
    | [], [] -> ()
    | t :: tr, u :: ur ->
      subtree_digest_injective h cf enc t u;
      subtree_digests_injective h cf enc tr ur
    | _ -> ()

(* ---- F#: `Tree.Digests.diff`, over the id lists of the two trees ----

   A key is `mem` of an id list (the README's standing reading); `own_at` is the map lookup
   `Own[k]`. The four predicates are named so each `keep` carries the same function term the
   lemmas below instantiate. *)
let own_at (h:string -> string) (cf:list string -> string) (enc:tree -> string) (x:string) (t:tree)
  : Tot (option string) =
  match find_in x t with
  | Some n -> Some (own_digest h cf enc n)
  | None -> None

let not_in (a:tree) (x:string) : Tot bool = not (mem x (ids a))

let changed_at (h:string -> string) (cf:list string -> string) (enc:tree -> string) (a b:tree)
               (x:string) : Tot bool =
  mem x (ids a) && own_at h cf enc x a <> own_at h cf enc x b

let unchanged_at (h:string -> string) (cf:list string -> string) (enc:tree -> string) (a b:tree)
                 (x:string) : Tot bool =
  mem x (ids a) && own_at h cf enc x a = own_at h cf enc x b

let d_added (a b:tree) : Tot (list string) = keep (not_in a) (ids b)
let d_removed (a b:tree) : Tot (list string) = keep (not_in b) (ids a)

let d_changed (h:string -> string) (cf:list string -> string) (enc:tree -> string) (a b:tree)
  : Tot (list string) = keep (changed_at h cf enc a b) (ids b)

let d_unchanged (h:string -> string) (cf:list string -> string) (enc:tree -> string) (a b:tree)
  : Tot (list string) = keep (unchanged_at h cf enc a b) (ids b)

(* `keep` keeps `no_dups`. *)
let rec keep_no_dups (f:string -> bool) (l:list string)
  : Lemma (requires no_dups l) (ensures no_dups (keep f l))
  = match l with
    | [] -> ()
    | x :: t -> mem_keep f t x; keep_no_dups f t

(* ---- the partition, clause by clause ---- *)

(* Every id of either tree is in one of the four lists, and no other id is in any of them. *)
let digest_partition_covers (h:string -> string) (cf:list string -> string) (enc:tree -> string)
                            (a b:tree) (x:string)
  : Lemma (ensures (mem x (ids a) || mem x (ids b)) ==
                   (mem x (d_added a b) || mem x (d_removed a b) ||
                    mem x (d_changed h cf enc a b) || mem x (d_unchanged h cf enc a b)))
  = mem_keep (not_in a) (ids b) x;
    mem_keep (not_in b) (ids a) x;
    mem_keep (changed_at h cf enc a b) (ids b) x;
    mem_keep (unchanged_at h cf enc a b) (ids b) x

(* No id is in two of them. *)
let digest_partition_disjoint (h:string -> string) (cf:list string -> string) (enc:tree -> string)
                              (a b:tree) (x:string)
  : Lemma (ensures not (mem x (d_added a b) && mem x (d_removed a b)) /\
                   not (mem x (d_added a b) && mem x (d_changed h cf enc a b)) /\
                   not (mem x (d_added a b) && mem x (d_unchanged h cf enc a b)) /\
                   not (mem x (d_removed a b) && mem x (d_changed h cf enc a b)) /\
                   not (mem x (d_removed a b) && mem x (d_unchanged h cf enc a b)) /\
                   not (mem x (d_changed h cf enc a b) && mem x (d_unchanged h cf enc a b)))
  = mem_keep (not_in a) (ids b) x;
    mem_keep (not_in b) (ids a) x;
    mem_keep (changed_at h cf enc a b) (ids b) x;
    mem_keep (unchanged_at h cf enc a b) (ids b) x

(* Under `wf`, each list carries an id once. *)
let digest_partition_once (h:string -> string) (cf:list string -> string) (enc:tree -> string)
                          (a b:tree)
  : Lemma (requires wf a /\ wf b)
          (ensures no_dups (d_added a b) /\ no_dups (d_removed a b) /\
                   no_dups (d_changed h cf enc a b) /\ no_dups (d_unchanged h cf enc a b))
  = wf_iff_no_dups a;
    wf_iff_no_dups b;
    keep_no_dups (not_in a) (ids b);
    keep_no_dups (not_in b) (ids a);
    keep_no_dups (changed_at h cf enc a b) (ids b);
    keep_no_dups (unchanged_at h cf enc a b) (ids b)

(* THE THEOREM, as one statement a reader cites: added ⊎ removed ⊎ changed ⊎ unchanged is the
   union of the two id sets. *)
let digest_partition (h:string -> string) (cf:list string -> string) (enc:tree -> string) (a b:tree)
  : Lemma (requires wf a /\ wf b)
          (ensures (forall (x:string).
                      (mem x (ids a) || mem x (ids b)) ==
                      (mem x (d_added a b) || mem x (d_removed a b) ||
                       mem x (d_changed h cf enc a b) || mem x (d_unchanged h cf enc a b))) /\
                   (forall (x:string).
                      not (mem x (d_added a b) && mem x (d_removed a b)) /\
                      not (mem x (d_added a b) && mem x (d_changed h cf enc a b)) /\
                      not (mem x (d_added a b) && mem x (d_unchanged h cf enc a b)) /\
                      not (mem x (d_removed a b) && mem x (d_changed h cf enc a b)) /\
                      not (mem x (d_removed a b) && mem x (d_unchanged h cf enc a b)) /\
                      not (mem x (d_changed h cf enc a b) && mem x (d_unchanged h cf enc a b))) /\
                   no_dups (d_added a b) /\ no_dups (d_removed a b) /\
                   no_dups (d_changed h cf enc a b) /\ no_dups (d_unchanged h cf enc a b))
  = FStar.Classical.forall_intro (digest_partition_covers h cf enc a b);
    FStar.Classical.forall_intro (digest_partition_disjoint h cf enc a b);
    digest_partition_once h cf enc a b

(* ---- and the two are NOT VACUOUS: evaluated on a pair with every list non-empty ----

   The hash is the identity and the field encoding the second field, so an own digest is the
   kind: `a` keeps its id and changes kind (changed), `b` leaves (removed), `c` arrives (added),
   and the root keeps its kind (unchanged). The injectivity theorem is not evaluated — its
   hypotheses are about the functions, and no computable stand-in for the hash is injective. *)
let second_field (l:list string) : Tot string =
  match l with
  | _ :: k :: _ -> k
  | _ -> ""

let dg_before : tree = TNode "r" "d" [ TNode "a" "x" []; TNode "b" "y" [] ]
let dg_after : tree = TNode "r" "d" [ TNode "a" "z" []; TNode "c" "y" [] ]

let digest_theorems_are_not_vacuous ()
  : Lemma (ensures d_added dg_before dg_after == [ "c" ] /\
                   d_removed dg_before dg_after == [ "b" ] /\
                   d_changed (fun s -> s) second_field kind_of dg_before dg_after == [ "a" ] /\
                   d_unchanged (fun s -> s) second_field kind_of dg_before dg_after == [ "r" ])
  = assert_norm (d_added dg_before dg_after == [ "c" ]);
    assert_norm (d_removed dg_before dg_after == [ "b" ]);
    assert_norm (d_changed (fun s -> s) second_field kind_of dg_before dg_after == [ "a" ]);
    assert_norm (d_unchanged (fun s -> s) second_field kind_of dg_before dg_after == [ "r" ])

(* ======================================================================================
   TWINS (Phase 309) — the extractor premise, sampled at this model.

   The leg's extraction diff makes "the oracle is the model" a checked claim about TEXT. Nothing
   in it says the F# the extractor emits COMPUTES what this model means: a mis-extraction that
   compiles would pass every other step. Each fixture below applies this model's own functions to
   a concrete input and compares the result with the value the model means there, and the
   assertion at the end is discharged by NORMALISATION — F*'s normaliser evaluates every closure
   to `true` under the model's own semantics. The list is extracted with the rest of the model,
   and the `Proofs.Oracle` family runs the extracted closures against the extracted oracle
   ("twin evaluation"): a closure that comes back `false` there is the F# backend disagreeing with
   the normaliser on that input. Sampled, never proved: the discharge holds on these inputs, which
   is where the `tested` rows already live. The kit's TWIN step (`kit/check-proof-leg.ps1`, step
   2c) refuses an extracted model that declares no twins.
   ====================================================================================== *)

noeq type twin = { tname : string; tholds : unit -> bool }

let rec twins_hold (l:list twin) : Tot bool =
  match l with
  | [] -> true
  | t :: r -> t.tholds () && twins_hold r
let twins : list twin = [
  { tname = "to-ops-appends-with-no-trailing-reorder";
    tholds = (fun () ->
      to_ops (TNode "r" "doc" [ TNode "a" "sec" [] ]) (TNode "r" "doc" [ TNode "a" "sec" []; TNode "b" "para" [] ])
      = Ok [ InsertChild "r" (TNode "b" "para" []) ]) };
  { tname = "to-ops-prepends-then-reorders";
    tholds = (fun () ->
      to_ops (TNode "r" "doc" [ TNode "a" "sec" [] ]) (TNode "r" "doc" [ TNode "b" "para" []; TNode "a" "sec" [] ])
      = Ok [ InsertChild "r" (TNode "b" "para" []); ReorderChildren "r" [ "b"; "a" ] ]) };
  { tname = "to-ops-contained-with-rewrites-the-container-first";
    tholds = (fun () ->
      to_ops_contained_with pre_fix_ch pre_fix_before pre_fix_after
      = Ok [ UpdateNode (TNode "p" "section" [ TNode "q" "para" [] ]);
             InsertChild "p" (TNode "q" "para" []) ]) };
  { tname = "to-ops-refuses-a-root-id-mismatch";
    tholds = (fun () -> to_ops (TNode "r" "doc" []) (TNode "s" "doc" []) = Error (RootIdMismatch "r" "s")) };
  { tname = "dup-id-names-the-repeat";
    tholds = (fun () -> dup_id (TNode "r" "d" [ TNode "a" "x" []; TNode "a" "y" [] ]) = Some "a") };
  (* Phase 314 — the digest diff's four lists on section 14's pair: the hash the identity, the field
     encoding the second field, so an own digest is the kind. *)
  { tname = "digest-diff-partitions-the-ids";
    tholds = (fun () ->
      d_added dg_before dg_after = [ "c" ] && d_removed dg_before dg_after = [ "b" ] &&
      d_changed (fun s -> s) second_field kind_of dg_before dg_after = [ "a" ] &&
      d_unchanged (fun s -> s) second_field kind_of dg_before dg_after = [ "r" ]) };
  { tname = "digest-diff-over-one-tree-is-all-unchanged";
    tholds = (fun () ->
      d_added dg_before dg_before = [] && d_removed dg_before dg_before = [] &&
      d_changed (fun s -> s) second_field kind_of dg_before dg_before = [] &&
      d_unchanged (fun s -> s) second_field kind_of dg_before dg_before = [ "r"; "a"; "b" ]) } ]

let _ = assert_norm (twins_hold twins == true)
