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
   1. Why a diff could not be produced (F#: `Diff.DiffError<'Id>` in Ops.fs).

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

#push-options "--z3rlimit 200 --fuel 2 --ifuel 1"
let ins_step (b a:tree) (done:list tree) (n:tree) (r:list tree) (t:tree) (pid:string)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done (n :: r) /\
                    inv1 b a (n :: r) t /\ not (mem (tid_of n) (ids b)) /\
                    lookup (tid_of n) (parent_map (pre a)) == Some pid)
          (ensures apply (InsertChild pid (shell n)) t == Ok (ins pid (shell n) t) /\
                   inv1 b a r (ins pid (shell n) t))
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
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done ns /\ inv1 b a ns t)
          (ensures (match apply_all (pass_inserts (parent_map (pre a)) (ids b) ns) t with
                    | Ok t1 -> inv1 b a [] t1
                    | Error _ -> False))
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: r ->
      let k = tid_of n in
      app_assoc done [n] r;
      if mem k (ids b) then begin
        inv1_skip b a n r t;
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

let move_step (b a:tree) (done:list tree) (p:tree) (ns:list tree) (ck:string) (rest:list string)
              (t:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done (p :: ns) /\
                    inv2 b a (ck :: rest) ns t /\ no_dups (ck :: rest) /\
                    (forall (z:string). mem z (ck :: rest) ==> mem z (kid_ids (kids_of p))) /\
                    not (same_kids b a (tid_of p)) /\ mem ck (ids b))
          (ensures (match apply (MoveNode ck (tid_of p)) t with
                    | Ok t' -> inv2 b a rest ns t'
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
                    inv2 b a (ck :: rest) ns t /\ mem ck (kid_ids (kids_of p)) /\
                    not (mem ck (ids b) &&
                         (match lookup ck (parent_map (pre b)) with
                          | Some bp -> bp <> tid_of p
                          | None -> true)))
          (ensures inv2 b a rest ns t)
  = let pk = tid_of p in
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
    FStar.Classical.forall_intro_2 aux

let rec moves_under_run (b a:tree) (done:list tree) (p:tree) (ns:list tree) (cs:list tree)
                        (t:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done (p :: ns) /\
                    inv2 b a (kid_ids cs) ns t /\ no_dups (kid_ids cs) /\
                    (forall (z:string). mem z (kid_ids cs) ==> mem z (kid_ids (kids_of p))) /\
                    not (same_kids b a (tid_of p)))
          (ensures (match apply_all (moves_under (tid_of p) (ids b) (parent_map (pre b)) cs) t with
                    | Ok t' -> inv2 b a [] ns t'
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
      if moved then begin
        move_step b a done p ns ck (kid_ids r) t;
        (match apply (MoveNode ck pk) t with
         | Ok t' -> moves_under_run b a done p ns r t'
         | Error _ -> ())
      end
      else begin
        move_skip b a done p ns ck (kid_ids r) t;
        moves_under_run b a done p ns r t
      end

let inv2_regroup (b a:tree) (p:tree) (ns:list tree) (t:tree)
  : Lemma (requires inv2 b a [] (p :: ns) t) (ensures inv2 b a (kid_ids (kids_of p)) ns t)
  = ()

let inv2_unchanged (b a:tree) (done:list tree) (p:tree) (ns:list tree) (t:tree)
  : Lemma (requires wf b /\ wf a /\ pre a == app done (p :: ns) /\ inv2 b a [] (p :: ns) t /\
                    same_kids b a (tid_of p))
          (ensures inv2 b a [] ns t)
  = let pk = tid_of p in
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
    FStar.Classical.forall_intro_2 aux

let rec moves_run (b a:tree) (done ns:list tree) (t:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a /\ pre a == app done ns /\
                    inv2 b a [] ns t)
          (ensures (match apply_all (fst (pass_moves (ids b) (parent_map (pre b)) (kid_map (pre b)) ns)) t with
                    | Ok t' -> inv2 b a [] [] t'
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
        moves_under_run b a done p r (kids_of p) t;
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

#push-options "--z3rlimit 200 --fuel 2 --ifuel 1"
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

let rem_step (b a:tree) (n:tree) (r:list tree) (t:tree)
  : Lemma (requires wf b /\ wf a /\ inv3 b a (n :: r) t /\ no_dups (tids (n :: r)) /\
                    top b a (tid_of n))
          (ensures (match apply (RemoveNode (tid_of n)) t with
                    | Ok t' -> inv3 b a r t'
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
  : Lemma (requires wf b /\ inv3 b a (n :: r) t /\ not (top b a (tid_of n)))
          (ensures inv3 b a r t)
  = let k = tid_of n in
    let aux (q c:string)
      : Lemma (mem q (ids t) ==> side3 b a (n :: r) q c == side3 b a r q c)
      = if c = k && mem k (kids_at q b) then parent_is_lookup b k q else ()
    in
    FStar.Classical.forall_intro_2 aux

let rec removes_run (b a:tree) (ns:list tree) (t:tree)
  : Lemma (requires wf b /\ wf a /\ inv3 b a ns t /\ no_dups (tids ns))
          (ensures (match apply_all (pass_removes (ids a) (parent_map (pre b)) ns) t with
                    | Ok t' -> inv3 b a [] t'
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

let inv4 (b a:tree) (ps:list string) (t:tree) : prop =
  wf t /\ tid_of t == tid_of a /\
  (forall (y:string). mem y (ids a) ==> mem y (ids t)) /\
  (forall (q c:string). mem q (ids t) ==> mem c (kids_at q t) == side3 b a [] q c) /\
  (forall (q:string). mem q (ids t) ==> kind_at q t == kind_src b a q) /\
  (forall (q:string). mem q (ids a) /\ not (mem q ps) ==> kids_at q t == kids_at q a)

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

let rec reorders_run (b a:tree) (ps:list string) (t:tree)
  : Lemma (requires wf a /\ inv4 b a ps t)
          (ensures (match apply_all (pass_reorders a ps) t with
                    | Ok t' -> inv4 b a [] t'
                    | Error _ -> False))
          (decreases ps)
  = match ps with
    | [] -> ()
    | pid :: r ->
      (match find_in pid a with
       | Some pn ->
         if more_than_one (kids_of pn) then begin
           reorder_step b a pid r t pn;
           (match apply (ReorderChildren pid (kid_ids (kids_of pn))) t with
            | Ok t' -> reorders_run b a r t'
            | Error _ -> ())
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
    let p4 = pass_reorders a (snd pm) in
    apply_all_app p1 (app p2 (app p3 p4)) b;
    inv1_init b a;
    inserts_run b a [] (pre a) b;
    match apply_all p1 b with
    | Error _ -> ()
    | Ok t1 ->
      apply_all_app p2 (app p3 p4) t1;
      inv1_to_inv2 b a t1;
      moves_run b a [] (pre a) t1;
      (match apply_all p2 t1 with
       | Error _ -> ()
       | Ok t2 ->
         apply_all_app p3 p4 t2;
         inv2_to_inv3 b a t2;
         ids_is_pre b;
         wf_iff_no_dups b;
         removes_run b a (pre b) t2;
         (match apply_all p3 t2 with
          | Error _ -> ()
          | Ok t3 ->
            ids_is_pre a;
            let listed (q:string)
              : Lemma (mem q (ids a) /\ not (same_kids b a q) ==> mem q (snd pm))
              = FStar.Classical.move_requires (changed_listed b a (pre a)) q
            in
            FStar.Classical.forall_intro listed;
            assert (inv4 b a (snd pm) t3);
            reorders_run b a (snd pm) t3))
#pop-options

(* THE OPERATIONAL `diff_applicable`: `apply` accepts every step of the script `toOps` emits, in
   sequence, against the tree in hand at that step. *)
let diff_applicable (b a:tree)
  : Lemma (requires wf b /\ wf a /\ tid_of b == tid_of a)
          (ensures Ok? (to_ops b a) /\ Ok? (apply_all (Ok?._0 (to_ops b a)) b))
  = diff_ok_on_any_wf_pair b a;
    diff_run b a

(* the one premise reconstruction needs beyond the diff own: a shared id names the same KIND in
   both trees, since no skeleton operation edits a node *)
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
   carries. `toOps` emits the EMPTY script — there is nothing for it to emit, because no
   skeleton operation edits a node — so the script is applicable at every step (vacuously) and
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
