(*
   Arbitrate — an F* model of Fuaran.Core's proposal arbitration, with the three promises its
   doc comment makes proved rather than sampled (fuaran-core Phase 157).

   WHAT IS MODELLED. `Arbitration.arbitrate` in `src/Fuaran.Core.Ops/Arbitration.fs`, clause for
   clause: the pinned order (a STABLE ascending sort on the proposal id), the batch dry run
   (`Ops.canApplyAll`, failing index and envelope included), the greedy footprint-independence
   pass, the re-citation of every conflict against the FULL accepted set, and the three fields of
   the result. It is a model OVER `TreeOps` (Phase 133): a script is a `list TreeOps.op`, the dry
   run threads `TreeOps.apply`, the footprint is `TreeOps.fp_all` and independence is
   `DagFold.independent` — the same relation the fold theorem is about, imported, not restated.

   WHAT IS PROVED. The three promises, each under the name the roadmap gave it:

     1. `accepted_pairwise_independent` — the accepted set is pairwise `Ops.independent`. With
        `accepted_pair_commutes` beside it: any two accepted scripts both apply at the base, and
        (by `TreeOps.tree_independence_diamond`, at a well-formed base) each applies after the
        other and the two orders reach the same tree. That is the doc comment's "by footprint
        soundness, confluent in any order", for a pair, as a theorem.
     2. `accepted_maximal` — every rejection is JUSTIFIED: an `Inapplicable` carries exactly the
        dry run's index and envelope, and a `Conflicts` names a script that does apply, is NOT
        independent of the accepted set, and cites a NON-EMPTY list that is exactly the accepted
        ids it interferes with (`conflict_cites_an_accepted_interferer` resolves each cited id to
        an accepted proposal that genuinely interferes). Nothing rejected could be added.
     3. `arbitrate_deterministic` — under every arrival order (`DagFold.perm`) of a proposal list
        whose ids are DISTINCT, the whole result is equal: accepted set, merged script, and every
        rejection with its reason.

   THE ID-UNIQUENESS HYPOTHESIS IS NEEDED, AND THAT IS A FINDING. `arbitrate` is total on any
   input — every function here is `Tot`, duplicates included — but `duplicate_ids_break_invariance`
   exhibits two proposals sharing an id whose two arrival orders accept DIFFERENT proposals. The
   stable sort breaks the tie by input order, exactly as the F# doc comment says, so invariance is
   a property of id-unique input and of nothing weaker that the function can see. The hypothesis
   is on the theorem, by name (`distinct_ids`), and `Arbitration.duplicateIds` is the shipped total
   check for it.

   WHAT IS NOT CLAIMED. MAXIMUM. Greedy-in-pinned-order yields A maximal independent set, not THE
   largest one: `maximal_is_not_maximum` is a three-proposal witness where the pinned order accepts
   one proposal and a different order would accept two. The pinned order is a POLICY choice —
   documented, deterministic, and not argued for here. Nor is anything claimed about which
   proposal is better, and independence stays conservative (Phase 78): `Conflicts` means "not
   provably coexistent", never "wrong".

   ONE SIMPLIFICATION, NAMED. The F# carries each accepted proposal's footprint beside it
   (`(p, fp) :: accepted`) so it is computed once. A footprint is a pure function of the script,
   so the model recomputes `fp_of p` where the F# reads the cached value; the differential in
   `ProofOracleTests.fs` is what holds the two to the same verdicts.

   HOW TO READ IT. Every definition names its F# counterpart, as in `TreeOps.fst`. The
   list-as-set reading, `rev`, `perm` and the membership algebra are `DagFold`'s.

   Apache-2.0, like everything beside it.
*)
module Arbitrate

open DagFold
open TreeOps

(* Scoped here for the reason `TreeOps.fst` gives: this module opens that one, so every membership
   pattern it declares is live at every query below, and upstream's context pruning is what keeps
   the solver's context proportional to the query rather than to the import. *)
#set-options "--ext context_pruning"

(* ======================================================================================
   0. The types (F#: `OpScriptProposal`, `ArbitrationRejection`, `Arbitration`).
   ====================================================================================== *)

(* F#: `OpScriptProposal<'Node,'Id>` — `Id`, `Holder`, `Ops`. *)
type proposal = {
  pid    : int;
  holder : string;
  script : list op
}

(* F#: `ArbitrationRejection<'Id>`. *)
type arb_rejection =
  | Inapplicable : op_index:nat -> rej:rejection -> arb_rejection
  | Conflicts    : interfering:list int -> arb_rejection

(* F#: `Arbitration<'Node,'Id>` — `Accepted`, `MergedScript`, `Rejected`. *)
type arbitration = {
  accepted : list proposal;
  merged   : list op;
  rejected : list (proposal & arb_rejection)
}

(* F#: `Ops.footprint nodew idw p.Ops`. *)
let fp_of (p:proposal) : Tot footprint = fp_all p.script

(* ======================================================================================
   1. The dry run (F#: `Ops.canApplyAll` = `canApplyAllWith (fun _ -> true)`).

      `go i node` threading the MUTATING per-op call and discarding the tree; the index is
      0-based and counts steps offered. `TreeOps.apply` IS `applyWith (fun _ -> true)`, so this is
      the shipped function and not an approximation of it. `Preservation.fst` section 9 models
      the capability-threaded general form; this module needs only the total instance and does
      not open that one for it.
   ====================================================================================== *)

let rec can_script (i:nat) (os:list op) (t:tree)
  : Tot (outcome unit (nat & rejection)) (decreases os) =
  match os with
  | [] -> Ok ()
  | o :: r -> (match apply o t with
               | Ok t' -> can_script (i + 1) r t'
               | Error e -> Error (i, e))

(* The dry run agrees with `TreeOps.apply_all` — the fold a `Batch` runs — on the verdict. This
   is the link that lets an accepted script be read as the single op `Batch script`, which is the
   alphabet `tree_independence_diamond` is stated over. *)
let rec can_script_is_apply_all (i:nat) (os:list op) (t:tree)
  : Lemma (ensures Ok? (can_script i os t) == Ok? (apply_all os t)) (decreases os)
  = match os with
    | [] -> ()
    | o :: r -> (match apply o t with
                 | Ok t' -> can_script_is_apply_all (i + 1) r t'
                 | Error _ -> ())

(* ======================================================================================
   2. The pinned order (F#: `proposals |> List.sortBy (fun p -> p.Id)`).

      `List.sortBy` is a STABLE sort, and stability is observable here: it is what decides the
      outcome on duplicate ids. Insertion from the right, placing the new element BEFORE the
      first one whose key is not smaller, is the stable ascending sort — the inserted element
      came earlier in the input than everything already placed.
   ====================================================================================== *)

let rec insert_by_id (p:proposal) (l:list proposal) : Tot (list proposal) =
  match l with
  | [] -> [p]
  | q :: r -> if p.pid <= q.pid then p :: l else q :: insert_by_id p r

let rec pin (ps:list proposal) : Tot (list proposal) =
  match ps with
  | [] -> []
  | p :: r -> insert_by_id p (pin r)

(* ======================================================================================
   3. The greedy pass (F#: `step` and `List.fold step ([], [])`).
   ====================================================================================== *)

(* F#: `accepted |> List.forall (fun (_, afp) -> Ops.independent fp afp)`. *)
let rec all_independent (fp:footprint) (acc:list proposal) : Tot bool =
  match acc with
  | [] -> true
  | a :: r -> independent fp (fp_of a) && all_independent fp r

(* F#: the fold state — accepted and rejected, both in REVERSE pinned order. *)
type verdicts = list proposal & list (proposal & arb_rejection)

(* F#: `step`. A conflict at decision time is provisional — `Conflicts []` — and is re-cited
   below against the full accepted set. *)
let step (base:tree) (st:verdicts) (p:proposal) : Tot verdicts =
  match st with
  | (acc, rej) ->
    (match can_script 0 p.script base with
     | Error (i, e) -> (acc, (p, Inapplicable i e) :: rej)
     | Ok () ->
       if all_independent (fp_of p) acc then (p :: acc, rej)
       else (acc, (p, Conflicts []) :: rej))

(* F#: `List.fold step`. *)
let rec fold_step (base:tree) (st:verdicts) (ps:list proposal) : Tot verdicts (decreases ps) =
  match ps with
  | [] -> st
  | p :: r -> fold_step base (step base st p) r

(* ======================================================================================
   4. The re-citation, and the result (F#: the `List.map` over `List.rev rejectedRev`, then
      the record).
   ====================================================================================== *)

(* F#: `accepted |> List.choose (fun (a, afp) -> if Ops.independent fp afp then None else Some a.Id)`. *)
let rec interfering_ids (fp:footprint) (acc:list proposal) : Tot (list int) =
  match acc with
  | [] -> []
  | a :: r -> if independent fp (fp_of a) then interfering_ids fp r
              else a.pid :: interfering_ids fp r

(* F#: the body of the re-citing `List.map`. *)
let recite (accepted:list proposal) (pr:proposal & arb_rejection) : Tot (proposal & arb_rejection) =
  match pr with
  | (p, Inapplicable i e) -> (p, Inapplicable i e)
  | (p, Conflicts _) -> (p, Conflicts (interfering_ids (fp_of p) accepted))

let rec recite_all (accepted:list proposal) (l:list (proposal & arb_rejection))
  : Tot (list (proposal & arb_rejection)) =
  match l with
  | [] -> []
  | pr :: r -> recite accepted pr :: recite_all accepted r

(* F#: `acceptedProposals |> List.collect (fun p -> p.Ops)`. *)
let rec collect_scripts (ps:list proposal) : Tot (list op) =
  match ps with
  | [] -> []
  | p :: r -> app p.script (collect_scripts r)

(* What the result is, given the pinned list — split out so the determinism theorem can say
   "equal pinned lists, equal results" in one step. *)
let arbitrate_pinned (base:tree) (pinned:list proposal) : Tot arbitration =
  match fold_step base ([], []) pinned with
  | (acc_rev, rej_rev) ->
    let accepted = rev acc_rev in
    { accepted = accepted;
      merged   = collect_scripts accepted;
      rejected = recite_all accepted (rev rej_rev) }

(* F#: `Arbitration.arbitrate nodew idw baseTree proposals`. *)
let arbitrate (base:tree) (ps:list proposal) : Tot arbitration =
  arbitrate_pinned base (pin ps)

(* ======================================================================================
   5. THEOREM 1 — the accepted set is pairwise independent.

      Pairwise is POSITIONAL: every member against every LATER member. A proposal need not be
      independent of itself (any structural write conflicts with itself), so the membership
      reading "any two members" would be false of a list carrying one proposal value twice, and
      the positional one is what the greedy pass actually establishes. `independent` is
      symmetric (`TreeOps.independent_sym`), so "every earlier against every later" is the
      whole relation.
   ====================================================================================== *)

let rec pairwise_independent (l:list proposal) : Tot bool =
  match l with
  | [] -> true
  | p :: r -> all_independent (fp_of p) r && pairwise_independent r

let rec all_independent_app (fp:footprint) (l m:list proposal)
  : Lemma (ensures all_independent fp (app l m) == (all_independent fp l && all_independent fp m))
  = match l with
    | [] -> ()
    | _ :: t -> all_independent_app fp t m

let rec all_independent_rev (fp:footprint) (l:list proposal)
  : Lemma (ensures all_independent fp (rev l) == all_independent fp l)
  = match l with
    | [] -> ()
    | x :: t -> all_independent_rev fp t; all_independent_app fp (rev t) [x]

(* Appending one proposal that is independent of everything already there keeps the list
   pairwise independent. Symmetry is used exactly once, here: the list's members must each be
   independent of the NEW one, and the hypothesis says the new one is independent of them. *)
let rec pairwise_snoc (l:list proposal) (x:proposal)
  : Lemma (requires pairwise_independent l /\ all_independent (fp_of x) l)
          (ensures pairwise_independent (app l [x]))
  = match l with
    | [] -> ()
    | a :: r ->
      independent_sym (fp_of x) (fp_of a);
      all_independent_app (fp_of a) r [x];
      pairwise_snoc r x

let rec pairwise_rev (l:list proposal)
  : Lemma (requires pairwise_independent l) (ensures pairwise_independent (rev l))
  = match l with
    | [] -> ()
    | x :: t ->
      pairwise_rev t;
      all_independent_rev (fp_of x) t;
      pairwise_snoc (rev t) x

(* The greedy pass keeps its accumulator pairwise independent: a proposal joins only when it is
   independent of everything already accepted, and it joins at the head. *)
let rec fold_keeps_pairwise (base:tree) (st:verdicts) (ps:list proposal)
  : Lemma (requires pairwise_independent (fst st))
          (ensures pairwise_independent (fst (fold_step base st ps))) (decreases ps)
  = match ps with
    | [] -> ()
    | p :: r -> fold_keeps_pairwise base (step base st p) r

(* THEOREM 1. *)
let accepted_pairwise_independent (base:tree) (ps:list proposal)
  : Lemma (ensures pairwise_independent (arbitrate base ps).accepted)
  = fold_keeps_pairwise base ([], []) (pin ps);
    pairwise_rev (fst (fold_step base ([], []) (pin ps)))

(* ---- what pairwise independence buys: any two accepted scripts commute ---- *)

let rec all_independent_mem (fp:footprint) (l:list proposal) (b:proposal)
  : Lemma (requires all_independent fp l /\ mem b l) (ensures independent fp (fp_of b))
  = match l with
    | [] -> ()
    | a :: r -> if a = b then () else all_independent_mem fp r b

(* The positional reading, spelled out: `a` anywhere in the list, `b` anywhere AFTER it. *)
let rec pairwise_split (l1:list proposal) (a:proposal) (l2:list proposal) (b:proposal)
  : Lemma (requires pairwise_independent (app l1 (a :: l2)) /\ mem b l2)
          (ensures independent (fp_of a) (fp_of b))
  = match l1 with
    | [] -> all_independent_mem (fp_of a) l2 b
    | _ :: t -> pairwise_split t a l2 b

(* Every accepted script passed the dry run at the base. *)
let rec all_applicable (base:tree) (l:list proposal) : Tot bool =
  match l with
  | [] -> true
  | p :: r -> Ok? (can_script 0 p.script base) && all_applicable base r

let rec all_applicable_app (base:tree) (l m:list proposal)
  : Lemma (ensures all_applicable base (app l m) == (all_applicable base l && all_applicable base m))
  = match l with
    | [] -> ()
    | _ :: t -> all_applicable_app base t m

let rec all_applicable_rev (base:tree) (l:list proposal)
  : Lemma (ensures all_applicable base (rev l) == all_applicable base l)
  = match l with
    | [] -> ()
    | x :: t -> all_applicable_rev base t; all_applicable_app base (rev t) [x]

let rec fold_keeps_applicable (base:tree) (st:verdicts) (ps:list proposal)
  : Lemma (requires all_applicable base (fst st))
          (ensures all_applicable base (fst (fold_step base st ps))) (decreases ps)
  = match ps with
    | [] -> ()
    | p :: r -> fold_keeps_applicable base (step base st p) r

let accepted_all_applicable (base:tree) (ps:list proposal)
  : Lemma (ensures all_applicable base (arbitrate base ps).accepted)
  = fold_keeps_applicable base ([], []) (pin ps);
    all_applicable_rev base (fst (fold_step base ([], []) (pin ps)))

let rec all_applicable_mem (base:tree) (l:list proposal) (b:proposal)
  : Lemma (requires all_applicable base l /\ mem b l) (ensures Ok? (can_script 0 b.script base))
  = match l with
    | [] -> ()
    | a :: r -> if a = b then () else all_applicable_mem base r b

let rec mem_app_right (#a:eqtype) (x:a) (l m:list a)
  : Lemma (requires mem x m) (ensures mem x (app l m))
  = match l with
    | [] -> ()
    | _ :: t -> mem_app_right x t m

(* COROLLARY — footprint soundness reaches the accepted set. Any two accepted proposals, read as
   the single ops `Batch a.script` and `Batch b.script`: both apply at the base, and at a
   well-formed base each applies after the other and the two orders reach the same tree
   (`TreeOps.wstep`, discharged by `tree_independence_diamond`). The N-script any-order claim is
   `DagFold.fold_confluence` at `Skeleton`'s instantiation; this is the pair that theorem's
   hypothesis asks for, shown to hold of what `arbitrate` accepts. *)
let accepted_pair_commutes (base:tree) (ps:list proposal)
                           (l1:list proposal) (a:proposal) (l2:list proposal) (b:proposal)
  : Lemma (requires (arbitrate base ps).accepted == app l1 (a :: l2) /\ mem b l2)
          (ensures Ok? (apply (Batch a.script) base) /\
                   Ok? (apply (Batch b.script) base) /\
                   wstep (Batch a.script) (Batch b.script) base)
  = accepted_pairwise_independent base ps;
    accepted_all_applicable base ps;
    pairwise_split l1 a l2 b;
    mem_app_right a l1 (a :: l2);
    mem_app_right b l1 (a :: l2);
    all_applicable_mem base (arbitrate base ps).accepted a;
    all_applicable_mem base (arbitrate base ps).accepted b;
    can_script_is_apply_all 0 a.script base;
    can_script_is_apply_all 0 b.script base;
    tree_independence_diamond (Batch a.script) (Batch b.script) base

(* ======================================================================================
   6. THEOREM 2 — the accepted set is maximal: every rejection is justified.

      Two layers, because the F# decides in two passes. At DECISION time a conflict is known
      only to interfere with the accepted set AS IT THEN STOOD (`provisional`); the accepted set
      only grows, so the interference survives to the end; and the re-citation then names the
      interferers against the FULL set (`justified`). "Non-empty by construction", the comment
      in the F# says — `interfering_nil_iff` is that sentence.
   ====================================================================================== *)

(* What the fold knows of a rejection when it records it. *)
let provisional (base:tree) (acc:list proposal) (pr:proposal & arb_rejection) : Tot bool =
  match pr with
  | (p, Inapplicable i e) -> can_script 0 p.script base = Error (i, e)
  | (p, Conflicts _) -> Ok? (can_script 0 p.script base) && not (all_independent (fp_of p) acc)

let rec all_provisional (base:tree) (acc:list proposal) (l:list (proposal & arb_rejection))
  : Tot bool =
  match l with
  | [] -> true
  | pr :: r -> provisional base acc pr && all_provisional base acc r

(* The accepted set only grows, and interference with a subset is interference with the set. *)
let rec provisional_grows (base:tree) (acc:list proposal) (q:proposal)
                          (l:list (proposal & arb_rejection))
  : Lemma (requires all_provisional base acc l) (ensures all_provisional base (q :: acc) l)
  = match l with
    | [] -> ()
    | _ :: r -> provisional_grows base acc q r

let rec fold_keeps_provisional (base:tree) (st:verdicts) (ps:list proposal)
  : Lemma (requires all_provisional base (fst st) (snd st))
          (ensures all_provisional base (fst (fold_step base st ps)) (snd (fold_step base st ps)))
          (decreases ps)
  = match ps with
    | [] -> ()
    | p :: r ->
      (match can_script 0 p.script base with
       | Error _ -> ()
       | Ok () ->
         if all_independent (fp_of p) (fst st) then provisional_grows base (fst st) p (snd st)
         else ());
      fold_keeps_provisional base (step base st p) r

(* Reversing the accepted list changes no verdict — `all_independent` reads it as a set. *)
let rec provisional_rev_acc (base:tree) (acc:list proposal) (l:list (proposal & arb_rejection))
  : Lemma (requires all_provisional base acc l) (ensures all_provisional base (rev acc) l)
  = match l with
    | [] -> ()
    | (p, _) :: r -> all_independent_rev (fp_of p) acc; provisional_rev_acc base acc r

let rec all_provisional_app (base:tree) (acc:list proposal) (l m:list (proposal & arb_rejection))
  : Lemma (ensures all_provisional base acc (app l m) ==
                   (all_provisional base acc l && all_provisional base acc m))
  = match l with
    | [] -> ()
    | _ :: t -> all_provisional_app base acc t m

let rec all_provisional_rev (base:tree) (acc:list proposal) (l:list (proposal & arb_rejection))
  : Lemma (ensures all_provisional base acc (rev l) == all_provisional base acc l)
  = match l with
    | [] -> ()
    | x :: t -> all_provisional_rev base acc t; all_provisional_app base acc (rev t) [x]

(* What a rejection in the RESULT says. F#: `arbitrationLaws`' "rejection actionability" law,
   with the citation pinned to the exact list rather than to "a non-empty subset". *)
let justified (base:tree) (accepted:list proposal) (pr:proposal & arb_rejection) : Tot bool =
  match pr with
  | (p, Inapplicable i e) -> can_script 0 p.script base = Error (i, e)
  | (p, Conflicts ids) ->
    Ok? (can_script 0 p.script base) &&
    not (all_independent (fp_of p) accepted) &&
    ids = interfering_ids (fp_of p) accepted &&
    not (is_empty ids)

let rec all_justified (base:tree) (accepted:list proposal) (l:list (proposal & arb_rejection))
  : Tot bool =
  match l with
  | [] -> true
  | pr :: r -> justified base accepted pr && all_justified base accepted r

(* The citation is empty exactly when there is nothing to cite. *)
let rec interfering_nil_iff (fp:footprint) (acc:list proposal)
  : Lemma (ensures is_empty (interfering_ids fp acc) == all_independent fp acc)
  = match acc with
    | [] -> ()
    | _ :: r -> interfering_nil_iff fp r

let rec recite_justifies (base:tree) (accepted:list proposal) (l:list (proposal & arb_rejection))
  : Lemma (requires all_provisional base accepted l)
          (ensures all_justified base accepted (recite_all accepted l))
  = match l with
    | [] -> ()
    | (p, _) :: r -> interfering_nil_iff (fp_of p) accepted; recite_justifies base accepted r

(* THEOREM 2. Every rejected proposal either does not apply — and says exactly where and why —
   or applies and cannot join the accepted set, and says exactly which accepted proposals stand
   in its way. Nothing rejected could be added without a conflict. *)
let accepted_maximal (base:tree) (ps:list proposal)
  : Lemma (ensures all_justified base (arbitrate base ps).accepted (arbitrate base ps).rejected)
  = let st = fold_step base ([], []) (pin ps) in
    fold_keeps_provisional base ([], []) (pin ps);
    provisional_rev_acc base (fst st) (snd st);
    all_provisional_rev base (rev (fst st)) (snd st);
    recite_justifies base (rev (fst st)) (rev (snd st))

(* The membership reading of theorem 2, for a caller holding one rejection. *)
let rec justified_mem (base:tree) (accepted:list proposal) (l:list (proposal & arb_rejection))
                      (pr:proposal & arb_rejection)
  : Lemma (requires all_justified base accepted l /\ mem pr l) (ensures justified base accepted pr)
  = match l with
    | [] -> ()
    | x :: r -> if x = pr then () else justified_mem base accepted r pr

(* ---- the citation is sound and complete: it IS the rebase target ---- *)

(* The accepted proposal a cited id stands for: the first one carrying that id that interferes. *)
let rec interferer (id:int) (fp:footprint) (acc:list proposal) : Tot (option proposal) =
  match acc with
  | [] -> None
  | a :: r -> if a.pid = id && not (independent fp (fp_of a)) then Some a else interferer id fp r

(* SOUND — every cited id resolves to an ACCEPTED proposal that genuinely interferes. *)
let rec conflict_cites_an_accepted_interferer (id:int) (fp:footprint) (acc:list proposal)
  : Lemma (requires mem id (interfering_ids fp acc))
          (ensures (match interferer id fp acc with
                    | Some a -> mem a acc /\ a.pid = id /\ not (independent fp (fp_of a))
                    | None -> False))
  = match acc with
    | [] -> ()
    | a :: r ->
      if a.pid = id && not (independent fp (fp_of a)) then ()
      else conflict_cites_an_accepted_interferer id fp r

(* COMPLETE — every accepted proposal that interferes is cited: "the complete rebase target, not
   just the first collision". *)
let rec every_interferer_is_cited (fp:footprint) (acc:list proposal) (a:proposal)
  : Lemma (requires mem a acc /\ not (independent fp (fp_of a)))
          (ensures mem a.pid (interfering_ids fp acc))
  = match acc with
    | [] -> ()
    | x :: r -> if x = a then () else every_interferer_is_cited fp r a

(* ---- and the partition is TOTAL: nothing is dropped, nothing is duplicated ---- *)

let rec len (#a:Type) (l:list a) : Tot nat =
  match l with
  | [] -> 0
  | _ :: t -> 1 + len t

let rec len_app (#a:Type) (l m:list a)
  : Lemma (ensures len (app l m) == len l + len m)
  = match l with
    | [] -> ()
    | _ :: t -> len_app t m

let rec len_rev (#a:Type) (l:list a)
  : Lemma (ensures len (rev l) == len l)
  = match l with
    | [] -> ()
    | x :: t -> len_rev t; len_app (rev t) [x]

let rec len_insert (p:proposal) (l:list proposal)
  : Lemma (ensures len (insert_by_id p l) == 1 + len l)
  = match l with
    | [] -> ()
    | _ :: r -> len_insert p r

let rec len_pin (ps:list proposal)
  : Lemma (ensures len (pin ps) == len ps)
  = match ps with
    | [] -> ()
    | p :: r -> len_pin r; len_insert p (pin r)

let rec len_recite (accepted:list proposal) (l:list (proposal & arb_rejection))
  : Lemma (ensures len (recite_all accepted l) == len l)
  = match l with
    | [] -> ()
    | _ :: r -> len_recite accepted r

let rec len_fold (base:tree) (st:verdicts) (ps:list proposal)
  : Lemma (ensures len (fst (fold_step base st ps)) + len (snd (fold_step base st ps)) ==
                   len (fst st) + len (snd st) + len ps) (decreases ps)
  = match ps with
    | [] -> ()
    | p :: r -> len_fold base (step base st p) r

(* Every proposal lands in exactly one bucket, counted: `accepted` and `rejected` together are as
   long as the input. With the two theorems above that is the whole of "a deterministic, TOTAL
   partition … nothing is ever silently dropped". *)
let arbitrate_is_total (base:tree) (ps:list proposal)
  : Lemma (ensures len (arbitrate base ps).accepted + len (arbitrate base ps).rejected == len ps)
  = let st = fold_step base ([], []) (pin ps) in
    len_fold base ([], []) (pin ps);
    len_pin ps;
    len_rev (fst st);
    len_rev (snd st);
    len_recite (rev (fst st)) (rev (snd st))

(* ======================================================================================
   7. THEOREM 3 — determinism: the result is a function of the proposal SET.

      `arbitrate` reads its input only through `pin`, so the theorem is `pin`'s: two arrival
      orders of an id-distinct list pin to the same list. The three structural cases of
      `DagFold.perm` are an induction, one commutation of two inserts, and a transitivity that
      needs the hypothesis carried across (`perm_distinct`).
   ====================================================================================== *)

let rec pids (l:list proposal) : Tot (list int) =
  match l with
  | [] -> []
  | p :: r -> p.pid :: pids r

(* THE HYPOTHESIS, by name. F#: "Ids are expected unique (a queue assigns them …)" — and
   `Arbitration.duplicateIds proposals = []` is the shipped total check for it. *)
let distinct_ids (ps:list proposal) : Tot bool = distinct (pids ps)

(* Two inserts with DIFFERENT keys commute, into any list at all — sortedness is not needed. *)
let rec insert_comm (x y:proposal) (s:list proposal)
  : Lemma (requires x.pid <> y.pid)
          (ensures insert_by_id x (insert_by_id y s) == insert_by_id y (insert_by_id x s))
  = match s with
    | [] -> ()
    | _ :: r -> insert_comm x y r

let rec perm_pids (l1 l2:list proposal) (p:perm proposal l1 l2) (k:int)
  : Lemma (ensures mem k (pids l1) == mem k (pids l2)) (decreases p)
  = match p with
    | PNil -> ()
    | PSkip _ m1 m2 p' -> perm_pids m1 m2 p' k
    | PSwap _ _ _ -> ()
    | PTrans m1 m2 m3 p12 p23 -> perm_pids m1 m2 p12 k; perm_pids m2 m3 p23 k

let rec perm_distinct (l1 l2:list proposal) (p:perm proposal l1 l2)
  : Lemma (requires distinct_ids l1) (ensures distinct_ids l2) (decreases p)
  = match p with
    | PNil -> ()
    | PSkip x m1 m2 p' -> perm_pids m1 m2 p' x.pid; perm_distinct m1 m2 p'
    | PSwap _ _ _ -> ()
    | PTrans m1 m2 m3 p12 p23 -> perm_distinct m1 m2 p12; perm_distinct m2 m3 p23

let rec pin_perm (l1 l2:list proposal) (p:perm proposal l1 l2)
  : Lemma (requires distinct_ids l1) (ensures pin l1 == pin l2) (decreases p)
  = match p with
    | PNil -> ()
    | PSkip _ m1 m2 p' -> pin_perm m1 m2 p'
    | PSwap x y l -> insert_comm x y (pin l)
    | PTrans m1 m2 m3 p12 p23 ->
      perm_distinct m1 m2 p12;
      pin_perm m1 m2 p12;
      pin_perm m2 m3 p23

(* THEOREM 3. Under every arrival order of an id-distinct proposal list the WHOLE result is
   equal — the accepted set, the merged script, and every rejection with its reason. *)
let arbitrate_deterministic (base:tree) (ps1 ps2:list proposal) (p:perm proposal ps1 ps2)
  : Lemma (requires distinct_ids ps1)
          (ensures arbitrate base ps1 == arbitrate base ps2)
  = pin_perm ps1 ps2 p

(* ---- and the pinned order IS ascending id ---- *)

let rec sorted_ids (l:list proposal) : Tot bool =
  match l with
  | [] -> true
  | [_] -> true
  | a :: b :: r -> a.pid <= b.pid && sorted_ids (b :: r)

let rec insert_sorted (p:proposal) (l:list proposal)
  : Lemma (requires sorted_ids l) (ensures sorted_ids (insert_by_id p l))
  = match l with
    | [] -> ()
    | [_] -> ()
    | _ :: b :: r -> insert_sorted p (b :: r)

let rec pin_sorted (ps:list proposal)
  : Lemma (ensures sorted_ids (pin ps))
  = match ps with
    | [] -> ()
    | p :: r -> pin_sorted r; insert_sorted p (pin r)

(* ======================================================================================
   8. THE FINDING — the hypothesis is NEEDED.

      Two proposals sharing id 1, each inserting under the same parent: they interfere (both
      write `root`'s structure), so exactly one is accepted, and the stable sort leaves them in
      arrival order — so WHICH one is accepted is the arrival order. `[dup_a; dup_b]` and
      `[dup_b; dup_a]` are one `PSwap` apart, so this is a counterexample to
      `arbitrate_deterministic` with its `requires` deleted, not merely to some stronger claim.

      `arbitrate` is nonetheless TOTAL on such input: every function above is `Tot`, and
      theorems 1 and 2 and `arbitrate_is_total` carry no hypothesis about ids at all. What
      duplicate ids cost is invariance, and nothing else.
   ====================================================================================== *)

let dup_base : tree = TNode "root" "doc" []

let dup_a : proposal =
  { pid = 1; holder = "a"; script = [ InsertChild "root" (TNode "x" "para" []) ] }

let dup_b : proposal =
  { pid = 1; holder = "b"; script = [ InsertChild "root" (TNode "y" "para" []) ] }

let duplicate_ids_break_invariance ()
  : Lemma (ensures not (distinct_ids [dup_a; dup_b]) /\
                   (arbitrate dup_base [dup_a; dup_b]).accepted == [dup_a] /\
                   (arbitrate dup_base [dup_b; dup_a]).accepted == [dup_b])
  = assert_norm (not (distinct_ids [dup_a; dup_b]));
    assert_norm ((arbitrate dup_base [dup_a; dup_b]).accepted == [dup_a]);
    assert_norm ((arbitrate dup_base [dup_b; dup_a]).accepted == [dup_b])

(* ======================================================================================
   9. NOT CLAIMED — maximal is not maximum, and the witness that it is not.

      Proposal 1 writes under both `a` and `b`; proposals 2 and 3 write under one each. In the
      pinned order 1 is accepted and 2 and 3 are both rejected against it — an accepted set of
      ONE — while `[mx_2; mx_3]` is itself applicable and pairwise independent, a set of TWO.
      Renumber proposal 1 to come last and that larger set is what is accepted. Both outcomes
      are maximal; only one is maximum; which one the caller gets is decided by the ids. That
      is the honesty boundary in the F# doc comment, and it is why the pinned order is filed
      as policy.
   ====================================================================================== *)

let mx_base : tree = TNode "root" "doc" [ TNode "a" "sec" []; TNode "b" "sec" [] ]

let mx_both : list op =
  [ InsertChild "a" (TNode "n1" "para" []); InsertChild "b" (TNode "n2" "para" []) ]

let mx_1 : proposal = { pid = 1; holder = "one"; script = mx_both }
let mx_2 : proposal = { pid = 2; holder = "two"; script = [ InsertChild "a" (TNode "n3" "para" []) ] }
let mx_3 : proposal = { pid = 3; holder = "three"; script = [ InsertChild "b" (TNode "n4" "para" []) ] }
let mx_1_last : proposal = { pid = 4; holder = "one"; script = mx_both }

let maximal_is_not_maximum ()
  : Lemma (ensures (arbitrate mx_base [mx_1; mx_2; mx_3]).accepted == [mx_1] /\
                   all_applicable mx_base [mx_2; mx_3] /\
                   pairwise_independent [mx_2; mx_3] /\
                   (arbitrate mx_base [mx_1_last; mx_2; mx_3]).accepted == [mx_2; mx_3])
  = assert_norm ((arbitrate mx_base [mx_1; mx_2; mx_3]).accepted == [mx_1]);
    assert_norm (all_applicable mx_base [mx_2; mx_3]);
    assert_norm (pairwise_independent [mx_2; mx_3]);
    assert_norm ((arbitrate mx_base [mx_1_last; mx_2; mx_3]).accepted == [mx_2; mx_3])

(* ======================================================================================
   11. THEOREM 4 — the merged script APPLIES at a well-formed base, and the accepted set
       reaches the same tree under every order (Phase 305).

       `arbitrate`'s doc comment: "by footprint soundness its scripts apply confluently in ANY
       order". Section 5's `accepted_pair_commutes` is the PAIR; this is the N-script statement
       the comment makes, and it is read off `DagFold.replay_perm` at `Skeleton`'s
       instantiation rather than proved again: the accepted scripts are the fold's LANES, the
       pairwise independence theorem 1 proves is an EMPTY conflict sweep over them
       (`all_conflicts_nil`), every lane applies at the base (`accepted_all_applicable`), the
       domain hypothesis is `TreeOps.op_independence_diamond`, and at a well-formed base the
       guarded `wapply` the composite folds IS `apply` (`replay_wapply_is_apply_all`, because
       every accepted step keeps `wf` — `Preservation.apply_preserves_wf`, cited rather than
       opened). So the fold's replay of the concatenation is `apply_all` of `merged`.

       WHY `wf base` IS THE RIGHT HYPOTHESIS AND NOT A GAP. Below it the pair diamond is not
       available (`TreeOps.wstep` is stated at `wf`), and since Phase 305 production does not
       arbitrate there at all: `Arbitration.arbitrate` checks `Tree.wellFormed` on the base
       first and refuses every proposal as `Inapplicable(0, DuplicateId d)` (DECISIONS.md
       D103.4). The hypothesis is therefore a property of every base the shipped function lets
       through, which is D102's first form, and the model keeps the pre-305 clause-for-clause
       shape it has always had — the refusal is a production guard in front of the function
       this module models, not a change to it.
   ====================================================================================== *)

(* The accepted scripts as the fold's lanes. *)
let rec scripts_of (ps:list proposal) : Tot (list (list op)) =
  match ps with
  | [] -> []
  | p :: r -> p.script :: scripts_of r

(* `merged` IS the concatenation of the lanes. *)
let rec collect_is_concat (ps:list proposal)
  : Lemma (ensures collect_scripts ps == concat (scripts_of ps))
  = match ps with
    | [] -> ()
    | _ :: r -> collect_is_concat r

(* From a well-formed tree the guarded replay is `apply_all`: an accepted step keeps `wf`, so
   neither of `wapply`'s guards ever fires. *)
let rec replay_wapply_is_apply_all (os:list op) (t:tree)
  : Lemma (requires wf t) (ensures replay wapply os t == apply_all os t) (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      Preservation.apply_preserves_wf o t;
      (match apply o t with
       | Ok t' -> replay_wapply_is_apply_all r t'
       | Error _ -> ())

(* ---- pairwise independence of the proposals is an empty conflict sweep over their scripts ---- *)

(* Independence of a script's union footprint descends to each of its ops. *)
let rec fp_all_independent_mem (x:list op) (fb:footprint) (a:op)
  : Lemma (requires independent (fp_all x) fb /\ mem a x) (ensures independent (op_fp a) fb)
  = match x with
    | [] -> ()
    | o :: r ->
      independent_union_left (op_fp o) (fp_all r) fb;
      if o = a then () else fp_all_independent_mem r fb a

let rec conflicts_with_nil (a:op) (y:list op)
  : Lemma (requires forall (b:op). mem b y ==> independent (op_fp a) (op_fp b))
          (ensures conflicts_with op_fp a y == [])
  = match y with
    | [] -> ()
    | b :: t -> pair_conflicts_nil_iff op_fp a b; conflicts_with_nil a t

let rec conflicts_nil_of_independent (x y:list op)
  : Lemma (requires independent (fp_all x) (fp_all y)) (ensures conflicts op_fp x y == [])
  = match x with
    | [] -> ()
    | a :: t ->
      independent_union_left (op_fp a) (fp_all t) (fp_all y);
      let aux (b:op) : Lemma (mem b y ==> independent (op_fp a) (op_fp b)) =
        if mem b y then begin
          independent_sym (op_fp a) (fp_all y);
          fp_all_independent_mem y (op_fp a) b;
          independent_sym (op_fp b) (op_fp a)
        end
        else ()
      in
      FStar.Classical.forall_intro aux;
      conflicts_with_nil a y;
      conflicts_nil_of_independent t y

let rec lane_conflicts_nil (d:list op) (rest:list proposal)
  : Lemma (requires all_independent (fp_all d) rest)
          (ensures lane_conflicts op_fp d (scripts_of rest) == [])
  = match rest with
    | [] -> ()
    | p :: r -> conflicts_nil_of_independent d p.script; lane_conflicts_nil d r

let rec all_conflicts_nil (ps:list proposal)
  : Lemma (requires pairwise_independent ps) (ensures all_conflicts op_fp (scripts_of ps) == [])
  = match ps with
    | [] -> ()
    | p :: r -> lane_conflicts_nil p.script r; all_conflicts_nil r

(* ---- every accepted lane applies at the base, as the fold reads it ---- *)

let rec lanes_apply_of_applicable (base:tree) (ps:list proposal)
  : Lemma (requires wf base /\ all_applicable base ps)
          (ensures lanes_apply wapply (scripts_of ps) base)
  = match ps with
    | [] -> ()
    | p :: r ->
      can_script_is_apply_all 0 p.script base;
      replay_wapply_is_apply_all p.script base;
      lanes_apply_of_applicable base r

(* ---- the concatenation of a conflict-free, applying lane set applies ---- *)

let app_nil_both (#a:Type) (l m:list a)
  : Lemma (requires app l m == []) (ensures l == [] /\ m == [])
  = match l with
    | [] -> ()
    | _ :: _ -> ()

let rec concat_applies (ls:list (list op)) (s:tree)
  : Lemma (requires independence_diamond op_fp wapply /\
                    all_conflicts op_fp ls == [] /\ lanes_apply wapply ls s)
          (ensures Ok? (replay wapply (concat ls) s)) (decreases ls)
  = match ls with
    | [] -> ()
    | d :: rest ->
      app_nil_both (lane_conflicts op_fp d rest) (all_conflicts op_fp rest);
      (match replay wapply d s with
       | Ok s1 ->
         lanes_apply_after wapply op_fp d rest s s1;
         concat_applies rest s1;
         replay_app wapply d (concat rest) s
       | Error _ -> ())

(* ---- a permutation of the proposals is a permutation of their scripts ---- *)

[@@ noextract_to "FSharp"]  (* proof-only, as `DagFold.perm` is *)
let rec perm_scripts (l1 l2:list proposal) (p:perm proposal l1 l2)
  : Tot (perm (list op) (scripts_of l1) (scripts_of l2)) (decreases p)
  = match p with
    | PNil -> PNil
    | PSkip x m1 m2 p' -> PSkip x.script (scripts_of m1) (scripts_of m2) (perm_scripts m1 m2 p')
    | PSwap x y l -> PSwap x.script y.script (scripts_of l)
    | PTrans m1 m2 m3 p12 p23 ->
      PTrans (scripts_of m1) (scripts_of m2) (scripts_of m3)
             (perm_scripts m1 m2 p12) (perm_scripts m2 m3 p23)

(* THEOREM 4. At a well-formed base the merged script applies, and applying the accepted
   proposals' scripts in ANY order reaches the tree it reaches — so no order of the accepted
   set is refused, and none reaches a different tree. *)
let merged_applies_and_order_free (base:tree) (ps:list proposal)
                                  (order:list proposal) (p:perm proposal (arbitrate base ps).accepted order)
  : Lemma (requires wf base)
          (ensures (let a = arbitrate base ps in
                    a.merged == collect_scripts a.accepted /\
                    Ok? (apply_all a.merged base) /\
                    apply_all (collect_scripts order) base == apply_all a.merged base))
  = let a = arbitrate base ps in
    accepted_pairwise_independent base ps;
    accepted_all_applicable base ps;
    op_independence_diamond ();
    all_conflicts_nil a.accepted;
    lanes_apply_of_applicable base a.accepted;
    concat_applies (scripts_of a.accepted) base;
    replay_perm wapply op_fp (scripts_of a.accepted) (scripts_of order) (perm_scripts a.accepted order p) base;
    collect_is_concat a.accepted;
    collect_is_concat order;
    replay_wapply_is_apply_all (concat (scripts_of a.accepted)) base;
    replay_wapply_is_apply_all (concat (scripts_of order)) base

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
let twin_base : tree = TNode "root" "doc" [ TNode "x" "sec" []; TNode "y" "sec" [] ]
let twin_p1 : proposal = { pid = 1; holder = "A"; script = [ InsertChild "x" (TNode "n1" "para" []) ] }
let twin_p2 : proposal = { pid = 2; holder = "B"; script = [ InsertChild "y" (TNode "n2" "para" []) ] }
let twin_p2x : proposal = { pid = 2; holder = "B"; script = [ InsertChild "x" (TNode "n2" "para" []) ] }

let twins : list twin = [
  { tname = "arbitrate-pins-and-accepts-disjoint-proposals";
    tholds = (fun () ->
      arbitrate twin_base [ twin_p2; twin_p1 ]
      = { accepted = [ twin_p1; twin_p2 ];
          merged = [ InsertChild "x" (TNode "n1" "para" []); InsertChild "y" (TNode "n2" "para" []) ];
          rejected = [] }) };
  { tname = "arbitrate-rejects-an-interfering-proposal";
    tholds = (fun () -> (arbitrate twin_base [ twin_p1; twin_p2x ]).rejected = [ (twin_p2x, Conflicts [ 1 ]) ]) } ]

let _ = assert_norm (twins_hold twins == true)
