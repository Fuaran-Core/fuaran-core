(*
   Chain — an F* model of Fuaran.Core's two integrity walkers, with tamper detection as a
   machine-checked theorem under one named hash assumption (fuaran-core Phase 136).

   WHAT IS MODELLED. Both shapes the estate's ledgers rest on, side by side:

     - the CONTENT-ADDRESSED DAG of `Fuaran.Core.OpStream.Dag` — a node is (parents, actor, op),
       its id is `Dag.nodeHash` of the SORTED parents and the encoded actor-and-op, and
       `Dag.firstBreak` / `Dag.verifyDag` recompute that id per node and check that every named
       parent is present;
     - the LINEAR hash chain of `Fuaran.Core.OpStream` — a record is (seq, actor, op, prevHash,
       hash), and `OpStream.firstChainBreak` / `verifyChain` walk it checking the sequence, the
       prev-link, and the record's own recomputed hash.

   WHAT IS PROVED. Two results per shape, and a characterisation each is a corollary of:

     - `intact_verifies` / `intact_chain_verifies` — a DAG grown by `append`/`merge` alone whose
       every named parent was present when it was named, and a chain grown by `append` alone, have
       no break at all.
     - `tamper_detected` / `chain_tamper_detected` — changing ONE node's op, actor or parent
       multiset, or ONE record's seq, actor or op, while leaving its ADDRESS as it was, is found.

   THE ONE ASSUMPTION, and it is the point of the phase. Both detection theorems take the
   injectivity of the hash **as an explicit parameter** — a lemma the caller supplies, not an
   `assume` (`--report_assumes error` is on and this module carries no `assume`, no `admit`, no
   `assume val`). It says: two contents that hash to one id ARE the same content. That is the
   collision-resistance assumption every content-addressed store makes; what is unusual is only
   that it is written down beside the code that needs it.

   Three things about that parameter are worth reading twice.

   1. **It is stated MODULO THE PARENT SORT**, because production sorts a node's parents before
      hashing (Phase 64.1, so `merge(A,B)` and `merge(B,A)` converge to one id). So the strongest
      true statement is "same id implies the same actor, the same op, and the same parents UP TO
      ORDER" — and `parent_reorder_undetected` below states the consequence as a theorem rather
      than leaving it to prose: **re-ordering a node's parents is not a detectable tamper.** It is
      not meant to be. `merge_id_parent_order_independent` proves the convergence that buys.
   2. **It bundles more than the hash.** `nodeHash` is `hashFn (join "," sortedParents)
      (actor ^ "|" ^ encodedOp)`, so "the id determines the content" needs the hash injective AND
      the two splices unambiguous (no comma inside a parent id, no `|` splitting the actor
      encoding from the op encoding at a second place) AND the op codec injective. Those are
      named in `proofs/README.md` under the assumption; the model does not decompose the premise
      into them, because string concatenation is opaque here and an argument the model cannot
      check is better stated than half-mechanised.
   3. **The chain's SEQUENCE and PREV-LINK breaks need none of it.** `chain_tamper_seq_detected`
      and `chain_tamper_prev_detected` are proved with no hypothesis at all: those two checks
      compare stored data against the walk's own running values and never consult the hash. Only
      the third check — the record's recomputed hash — rests on the premise. That split is the
      sharpest thing the mechanisation says about the linear walker.

   WHAT IS NOT MODELLED. Signatures: an `Attestation` over a head is a composition this says
   nothing about, and it lives on the coordination plane's own side. A REWRITE — a tamper that
   also re-mints every descendant's id — is outside every theorem here, deliberately: it produces
   a perfectly intact structure, and what catches it is a signed head, not a walker. Cycles: a DAG
   whose ids all recompute cannot carry one (a node's id folds its parents', so a cycle needs a
   collision), and `Dag.isAcyclic` is a separate check on a structurally-loaded DAG. And the
   `StreamConfig` migration path (`legacyActorConfig`, `rehash`) — the model carries the canonical
   payload only, with the payload's shape a parameter so a second config is a second instantiation
   rather than a second model.

   HOW TO READ IT. Every definition names its F# counterpart in the comment above it:
     - `DagOpStream.fs` — `DagNode`, `Dag.T`, `Dag.nodeHash`, `Dag.append`, `Dag.merge`,
                          `Dag.firstBreak`, `Dag.verifyDag`, `DagBreak`
     - `OpStream.fs`    — `OpRecord`, `StreamConfig`, `OpStream.appendWith`,
                          `OpStream.firstChainBreakWith`, `verifyChain`, `ChainBreak`,
                          `ChainBreakReason`
   The reasons a break carries are reproduced VERBATIM rather than classified, as the decode model
   beside this one reproduces its messages: naming which check failed is most of what a localising
   verifier is for, and a differential that compared only the class would not notice a walker
   reporting the wrong one.

   Two encodings are forced by the extraction and are stated where they are made, both of them
   findings the model beside this one recorded first (`proofs/README.md`, findings 2 and 4): a
   locally-spelled `found` where F# has `option`, and a Peano `pos` where F# has `int`, so that
   the extracted oracle needs nothing of `Prims` beyond the five names the shim supplies.

   Apache-2.0, like everything beside it.
*)
module Chain

(* ======================================================================================
   0. The Prims-only idiom.

   Self-contained, exactly as the two models beside this one are, so the extracted oracle depends
   on `oracle/Prims.fs` alone — which supplies five names: `list`, `string`, `bool`, `op_Equals`
   and `strcat`. Nothing here reaches for `FStar.List.Tot`, for `option`, or for integers.
   ====================================================================================== *)

(* F#: `List.contains`. Membership is all this model asks of a key list. *)
let rec mem (#a: eqtype) (x: a) (l: list a) : Tot bool =
  match l with
  | [] -> false
  | y :: t -> x = y || mem x t

(* F#: `'a option`, as `Map.tryFind` and `List.tryPick` return it. Spelled LOCALLY rather than
   taken from `FStar.Pervasives.Native`: F*'s F# backend extracts that one as
   `FStar_Pervasives_Native.Some` and the release ships no F# runtime for it, so the oracle would
   not compile (README, finding 2 — the same reason section 11 of `DagFold.fst` spells its own). *)
type found (a: Type) =
  | Missing : found a
  | Found : a -> found a

(* ======================================================================================
   1. The content id (F#: `Dag.nodeHash`, DagOpStream.fs).

       let private nodeHash hashFn encode parents actor op =
           let sorted = parents |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))
           hashFn (String.concat "," sorted) (Actor.encode actor + "|" + encode op)

   Three parameters stand in for three seams production already has: `h` is the pluggable
   `HashFn`; `enc_op` is `StreamWitness.Encode`; and `le` is the ordinal comparison, standing for
   `String.CompareOrdinal(a, b) <= 0`. The ACTOR is carried as a string throughout this model,
   because the string `Actor.encode` produces is the only thing the pre-image ever sees of it —
   modelling the `Human | Agent` shape would add a case split that no walker branches on.
   ====================================================================================== *)

(* F#: `List.sortWith (fun a b -> String.CompareOrdinal(a, b))`. Insertion sort, and stable —
   though stability is unobservable here, since two strings that tie under an ordinal comparison
   ARE the same string (`total_order` below says exactly that, as antisymmetry). *)
let rec insert (le: string -> string -> bool) (x: string) (l: list string) : Tot (list string) =
  match l with
  | [] -> [x]
  | y :: t -> if le x y then x :: y :: t else y :: insert le x t

let rec isort (le: string -> string -> bool) (l: list string) : Tot (list string) =
  match l with
  | [] -> []
  | x :: t -> insert le x (isort le t)

(* What `le` is assumed to be: a total order. Both halves hold of `CompareOrdinal(a,b) <= 0` —
   any two strings compare, and two that compare both ways are equal. Only
   `merge_id_parent_order_independent` needs it; the detection theorems do not. *)
let total_order (le: string -> string -> bool) : prop =
  (forall (x: string) (y: string). le x y \/ le y x) /\
  (forall (x: string) (y: string). (le x y /\ le y x) ==> x == y)

(* F#: `String.concat ","`. Empty list to the empty string, a singleton to itself — the same
   three clauses, so the joined pre-image is the one production hashes. *)
let rec join_comma (l: list string) : Tot string =
  match l with
  | [] -> ""
  | [x] -> x
  | x :: t -> x ^ "," ^ join_comma t

(* F#: `Dag.nodeHash`, clause for clause. *)
let node_hash
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (parents: list string)
  (actor: string)
  (o: op)
  : Tot string =
  h (join_comma (isort le parents)) (actor ^ "|" ^ enc_op o)

(* THE CRYPTOGRAPHIC PREMISE, as an explicit parameter rather than an `assume`: a lemma the
   caller hands in, saying that two contents which mint one id are the same content — the parents
   up to the sort, the actor and the op exactly. Every detection theorem below that needs it takes
   it; every theorem that does not, does not.

   `isort le p1 == isort le p2` rather than `p1 == p2` is not a weakening for convenience: it is
   the strongest statement that is TRUE of production, which sorts before hashing so that a merge
   node's identity does not depend on which head the reconciler happened to call left. *)
[@@ noextract_to "FSharp"]
let node_injective
  (op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  : Type =
  p1: list string -> a1: string -> o1: op -> p2: list string -> a2: string -> o2: op ->
  Lemma
    (requires node_hash h enc_op le p1 a1 o1 == node_hash h enc_op le p2 a2 o2)
    (ensures isort le p1 == isort le p2 /\ a1 == a2 /\ o1 == o2)

(* ======================================================================================
   2. The DAG (F#: `DagNode<'Op>` and `Dag.T<'Op>`).

   Production keys nodes by a `Map<string, DagNode<'Op>>`, and `firstBreak` recomputes each
   node's id and compares it against the MAP KEY — never against the node's own `Id` field, which
   the walk does not read. The model therefore carries the key beside the node as an `entry`, and
   an `entry` list stands in for the map: membership is the meaning, as in the models beside this
   one. Production walks `Map.toList`, which is ascending key order; the model walks its list in
   the order it is given, and the differential host hands it exactly the list `Map.toList`
   produced, so the order under test is production's own.
   ====================================================================================== *)

type dnode (op: eqtype) = { dparents: list string; dactor: string; dop: op }

type entry (op: eqtype) = { ekey: string; enode: dnode op }

let rec keys_of (#op: eqtype) (es: list (entry op)) : Tot (list string) =
  match es with
  | [] -> []
  | e :: t -> e.ekey :: keys_of t

(* F#: `Map.tryFind id dag.Nodes`. *)
let rec lookup_node (#op: eqtype) (es: list (entry op)) (k: string) : Tot (found (dnode op)) =
  match es with
  | [] -> Missing
  | e :: t -> if e.ekey = k then Found e.enode else lookup_node t k

(* F#: `n.Parents |> List.tryFind (fun p -> not (dag.Nodes.ContainsKey p))` — the first parent, in
   STORED order (the sort is the hash pre-image's, not the stored list's), that the DAG lacks. *)
let rec first_absent (keys: list string) (ps: list string) : Tot (found string) =
  match ps with
  | [] -> Missing
  | p :: t -> if mem p keys then first_absent keys t else Found p

(* F#: `DagBreak`. *)
type dbreak = { bnode: string; breason: string; bexpected: string; bgot: string }

let break_content (key: string) (recomputed: string) : Tot dbreak =
  { bnode = key;
    breason = "content-id mismatch (tampered node)";
    bexpected = recomputed;
    bgot = key }

let break_parent (key: string) (missing: string) : Tot dbreak =
  { bnode = key; breason = "missing parent"; bexpected = ""; bgot = missing }

(* F#: `Dag.firstBreak`, clause for clause — the id check first, the parent check second, so a
   node that is both mis-keyed and dangling reports the id mismatch, exactly as production does.
   `keys` is the WHOLE DAG's key list, computed once and carried down the walk, because that is
   what `dag.Nodes.ContainsKey` consults at every step. *)
let rec first_break_from
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (keys: list string)
  (es: list (entry op))
  : Tot (found dbreak) =
  match es with
  | [] -> Missing
  | e :: t ->
    let recomputed = node_hash h enc_op le e.enode.dparents e.enode.dactor e.enode.dop in
    if not (e.ekey = recomputed)
    then Found (break_content e.ekey recomputed)
    else
      match first_absent keys e.enode.dparents with
      | Found p -> Found (break_parent e.ekey p)
      | Missing -> first_break_from h enc_op le keys t

let first_break
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (es: list (entry op))
  : Tot (found dbreak) =
  first_break_from h enc_op le (keys_of es) es

(* F#: `Dag.verifyDag` — `firstBreak … |> Option.isNone`. One definition of integrity, localising
   or boolean, exactly as production has it. *)
let verify_dag
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (es: list (entry op))
  : Tot bool =
  match first_break h enc_op le es with
  | Missing -> true
  | Found _ -> false

(* ---- what "no break" means, as two predicates the walk is equivalent to ---- *)

let entry_ok
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (e: entry op)
  : Tot bool =
  e.ekey = node_hash h enc_op le e.enode.dparents e.enode.dactor e.enode.dop

let rec all_ok
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (es: list (entry op))
  : Tot bool =
  match es with
  | [] -> true
  | e :: t -> entry_ok h enc_op le e && all_ok h enc_op le t

let rec parents_present (#op: eqtype) (keys: list string) (es: list (entry op)) : Tot bool =
  match es with
  | [] -> true
  | e :: t ->
    match first_absent keys e.enode.dparents with
    | Found _ -> false
    | Missing -> parents_present keys t

(* The characterisation: the walk finds nothing EXACTLY when every entry's key is its content
   hash and every named parent is present. Both theorems below are corollaries of it, which is
   what keeps them statements about the walker rather than about a re-description of it. *)
let rec break_none_iff_from
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (keys: list string)
  (es: list (entry op))
  : Lemma
    (ensures
      (match first_break_from h enc_op le keys es with
       | Missing -> true
       | Found _ -> false) ==
      (all_ok h enc_op le es && parents_present keys es)) =
  match es with
  | [] -> ()
  | _ :: t -> break_none_iff_from h enc_op le keys t

let break_none_iff
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (es: list (entry op))
  : Lemma
    (ensures
      verify_dag h enc_op le es == (all_ok h enc_op le es && parents_present (keys_of es) es)) =
  break_none_iff_from h enc_op le (keys_of es) es

(* ======================================================================================
   3. Intactness — a DAG grown by `append` and `merge` alone has no break.

   The id half is free: `append` mints the id from the content it is storing, so it cannot be
   wrong. The PARENT half is not free, and saying why is half the value of stating this theorem.
   `Dag.append` does not check that `parentId` names a node the DAG holds — it takes the string
   and stores it — so "every parent is present" is a precondition on HOW A DAG WAS GROWN, not a
   property of the constructor. `steps_well_parented` is that precondition, and it is checked
   against the DAG as it stood when each step ran.
   ====================================================================================== *)

(* F#: an `append` (`""` for genesis, so `parents = []`) or a two-parent `merge`. *)
type step (op: eqtype) =
  | SAppend : parent: string -> actor: string -> o: op -> step op
  | SMerge : left: string -> right: string -> actor: string -> o: op -> step op

let step_parents (#op: eqtype) (s: step op) : Tot (list string) =
  match s with
  | SAppend p _ _ -> if p = "" then [] else [p]
  | SMerge l r _ _ -> [l; r]

let step_actor (#op: eqtype) (s: step op) : Tot string =
  match s with
  | SAppend _ a _ -> a
  | SMerge _ _ a _ -> a

let step_op (#op: eqtype) (s: step op) : Tot op =
  match s with
  | SAppend _ _ o -> o
  | SMerge _ _ _ o -> o

(* F#: `Map.add id node` — replace under an existing key, else add. Two appends of the same
   (parent, actor, op) mint the same id and are ONE node, which is the convergence content
   addressing is for; the model must not carry them as two entries. *)
let rec upsert (#op: eqtype) (es: list (entry op)) (e: entry op) : Tot (list (entry op)) =
  match es with
  | [] -> [e]
  | x :: t -> if x.ekey = e.ekey then e :: t else x :: upsert t e

let apply_step
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (es: list (entry op))
  (s: step op)
  : Tot (list (entry op)) =
  let ps = step_parents s in
  let a = step_actor s in
  let o = step_op s in
  upsert es
    ({ ekey = node_hash h enc_op le ps a o; enode = { dparents = ps; dactor = a; dop = o } })

let rec build
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (es: list (entry op))
  (ss: list (step op))
  : Tot (list (entry op)) (decreases ss) =
  match ss with
  | [] -> es
  | s :: t -> build h enc_op le (apply_step h enc_op le es s) t

let rec steps_well_parented
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (es: list (entry op))
  (ss: list (step op))
  : Tot bool (decreases ss) =
  match ss with
  | [] -> true
  | s :: t ->
    match first_absent (keys_of es) (step_parents s) with
    | Found _ -> false
    | Missing -> steps_well_parented h enc_op le (apply_step h enc_op le es s) t

(* ---- the invariants `upsert` keeps ---- *)

let rec upsert_all_ok
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (es: list (entry op))
  (e: entry op)
  : Lemma
    (requires all_ok h enc_op le es /\ entry_ok h enc_op le e)
    (ensures all_ok h enc_op le (upsert es e)) =
  match es with
  | [] -> ()
  | x :: t -> if x.ekey = e.ekey then () else upsert_all_ok h enc_op le t e

let rec upsert_keys_mono (#op: eqtype) (es: list (entry op)) (e: entry op) (k: string)
  : Lemma (requires mem k (keys_of es)) (ensures mem k (keys_of (upsert es e))) =
  match es with
  | [] -> ()
  | x :: t -> if x.ekey = e.ekey then () else if k = x.ekey then () else upsert_keys_mono t e k

let rec first_absent_mono (keys: list string) (keys': list string) (ps: list string)
  : Lemma
    (requires
      first_absent keys ps == Missing /\ (forall (k: string). mem k keys ==> mem k keys'))
    (ensures first_absent keys' ps == Missing) =
  match ps with
  | [] -> ()
  | _ :: t -> first_absent_mono keys keys' t

let rec parents_present_mono
  (#op: eqtype)
  (keys: list string)
  (keys': list string)
  (es: list (entry op))
  : Lemma
    (requires parents_present keys es /\ (forall (k: string). mem k keys ==> mem k keys'))
    (ensures parents_present keys' es) =
  match es with
  | [] -> ()
  | e :: t ->
    first_absent_mono keys keys' e.enode.dparents;
    parents_present_mono keys keys' t

let rec upsert_parents_present
  (#op: eqtype)
  (keys: list string)
  (es: list (entry op))
  (e: entry op)
  : Lemma
    (requires parents_present keys es /\ first_absent keys e.enode.dparents == Missing)
    (ensures parents_present keys (upsert es e)) =
  match es with
  | [] -> ()
  | x :: t -> if x.ekey = e.ekey then () else upsert_parents_present keys t e

let step_keeps_the_invariants
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (es: list (entry op))
  (s: step op)
  : Lemma
    (requires
      all_ok h enc_op le es /\ parents_present (keys_of es) es /\
      first_absent (keys_of es) (step_parents s) == Missing)
    (ensures
      all_ok h enc_op le (apply_step h enc_op le es s) /\
      parents_present (keys_of (apply_step h enc_op le es s)) (apply_step h enc_op le es s)) =
  let ps = step_parents s in
  let a = step_actor s in
  let o = step_op s in
  let e: entry op =
    { ekey = node_hash h enc_op le ps a o; enode = { dparents = ps; dactor = a; dop = o } }
  in
  upsert_all_ok h enc_op le es e;
  upsert_parents_present (keys_of es) es e;
  let mono (k: string) : Lemma (mem k (keys_of es) ==> mem k (keys_of (upsert es e))) =
    if mem k (keys_of es) then upsert_keys_mono es e k else ()
  in
  FStar.Classical.forall_intro mono;
  parents_present_mono (keys_of es) (keys_of (upsert es e)) (upsert es e)

let rec intact_verifies_from
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (es: list (entry op))
  (ss: list (step op))
  : Lemma
    (requires
      all_ok h enc_op le es /\ parents_present (keys_of es) es /\
      steps_well_parented h enc_op le es ss)
    (ensures verify_dag h enc_op le (build h enc_op le es ss))
    (decreases ss) =
  match ss with
  | [] -> break_none_iff h enc_op le es
  | s :: t ->
    step_keeps_the_invariants h enc_op le es s;
    intact_verifies_from h enc_op le (apply_step h enc_op le es s) t

(* THEOREM. A DAG grown from nothing by `append` and `merge`, each naming parents the DAG already
   held, verifies: `Dag.firstBreak` finds neither a content-id mismatch nor a missing parent. *)
let intact_verifies
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (ss: list (step op))
  : Lemma
    (requires steps_well_parented h enc_op le [] ss)
    (ensures verify_dag h enc_op le (build h enc_op le [] ss)) =
  intact_verifies_from h enc_op le [] ss

(* ======================================================================================
   4. Tamper detection over the DAG.

   A TAMPER is one node's content changed with its ADDRESS left as it was — the whole DAG
   otherwise untouched, every other id exactly as it was minted. That is the threat the content
   id exists to catch, and it is precisely NOT a rewrite: an attacker who re-mints the tampered
   node's id and then every descendant's produces a DAG that is intact by construction, and the
   claims ladder says so.
   ====================================================================================== *)

(* What the id is minted from — the parents only up to the sort, per section 1. Two nodes for
   which this is true are indistinguishable to `Dag.nodeHash`, so a change that keeps it is not a
   tamper this walker can find, and one that breaks it is. *)
let same_preimage (#op: eqtype) (le: string -> string -> bool) (n: dnode op) (n': dnode op)
  : Tot bool =
  isort le n.dparents = isort le n'.dparents && n.dactor = n'.dactor && n.dop = n'.dop

(* F#: replacing a node in the map under the SAME key — `Map.add k tamperedNode dag.Nodes`. *)
let rec retarget (#op: eqtype) (es: list (entry op)) (k: string) (n': dnode op)
  : Tot (list (entry op)) =
  match es with
  | [] -> []
  | e :: t -> if e.ekey = k then { ekey = e.ekey; enode = n' } :: t else e :: retarget t k n'

let tamper_changes_the_id
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (inj: node_injective op h enc_op le)
  (n: dnode op)
  (n': dnode op)
  : Lemma
    (requires not (same_preimage le n n'))
    (ensures
      ~(node_hash h enc_op le n'.dparents n'.dactor n'.dop ==
        node_hash h enc_op le n.dparents n.dactor n.dop)) =
  if node_hash h enc_op le n'.dparents n'.dactor n'.dop =
     node_hash h enc_op le n.dparents n.dactor n.dop
  then inj n'.dparents n'.dactor n'.dop n.dparents n.dactor n.dop
  else ()

let rec retarget_breaks_all_ok
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (es: list (entry op))
  (k: string)
  (n: dnode op)
  (n': dnode op)
  : Lemma
    (requires
      lookup_node es k == Found n /\
      ~(node_hash h enc_op le n'.dparents n'.dactor n'.dop == k))
    (ensures not (all_ok h enc_op le (retarget es k n'))) =
  match es with
  | [] -> ()
  | e :: t -> if e.ekey = k then () else retarget_breaks_all_ok h enc_op le t k n n'

(* THEOREM. Change one node's op, actor or parent multiset while leaving its id as it was, in a
   DAG whose node under that id was intact, and `Dag.firstBreak` reports a break — under the
   injectivity premise and nothing else. *)
let tamper_detected
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (inj: node_injective op h enc_op le)
  (es: list (entry op))
  (k: string)
  (n: dnode op)
  (n': dnode op)
  : Lemma
    (requires
      lookup_node es k == Found n /\
      k == node_hash h enc_op le n.dparents n.dactor n.dop /\ not (same_preimage le n n'))
    (ensures not (verify_dag h enc_op le (retarget es k n'))) =
  tamper_changes_the_id h enc_op le inj n n';
  retarget_breaks_all_ok h enc_op le es k n n';
  break_none_iff h enc_op le (retarget es k n')

(* The three tampers named one at a time, because "the pre-image differs" is the right general
   statement and the wrong one to read a threat model off. Each is the theorem above with the
   differing field supplying the premise. *)

let tamper_op_detected
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (inj: node_injective op h enc_op le)
  (es: list (entry op))
  (k: string)
  (n: dnode op)
  (o': op)
  : Lemma
    (requires
      lookup_node es k == Found n /\
      k == node_hash h enc_op le n.dparents n.dactor n.dop /\ ~(o' == n.dop))
    (ensures
      not
        (verify_dag h enc_op le
          (retarget es k ({ dparents = n.dparents; dactor = n.dactor; dop = o' })))) =
  tamper_detected h enc_op le inj es k n
    ({ dparents = n.dparents; dactor = n.dactor; dop = o' })

let tamper_actor_detected
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (inj: node_injective op h enc_op le)
  (es: list (entry op))
  (k: string)
  (n: dnode op)
  (a': string)
  : Lemma
    (requires
      lookup_node es k == Found n /\
      k == node_hash h enc_op le n.dparents n.dactor n.dop /\ ~(a' == n.dactor))
    (ensures
      not
        (verify_dag h enc_op le
          (retarget es k ({ dparents = n.dparents; dactor = a'; dop = n.dop })))) =
  tamper_detected h enc_op le inj es k n
    ({ dparents = n.dparents; dactor = a'; dop = n.dop })

(* Re-parenting is detected exactly when it moves the parent MULTISET — which is the honest
   statement, and the one `parent_reorder_undetected` below is the other half of. *)
let tamper_parents_detected
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (inj: node_injective op h enc_op le)
  (es: list (entry op))
  (k: string)
  (n: dnode op)
  (ps': list string)
  : Lemma
    (requires
      lookup_node es k == Found n /\
      k == node_hash h enc_op le n.dparents n.dactor n.dop /\
      ~(isort le n.dparents == isort le ps'))
    (ensures
      not
        (verify_dag h enc_op le
          (retarget es k ({ dparents = ps'; dactor = n.dactor; dop = n.dop })))) =
  tamper_detected h enc_op le inj es k n
    ({ dparents = ps'; dactor = n.dactor; dop = n.dop })

(* ---- the second break class: a parent the DAG does not hold ---- *)

let rec dangling_parent_absent
  (#op: eqtype)
  (keys: list string)
  (es: list (entry op))
  (e: entry op)
  (p: string)
  : Lemma
    (requires mem e es /\ mem p e.enode.dparents /\ not (mem p keys))
    (ensures not (parents_present keys es)) =
  match es with
  | [] -> ()
  | x :: t ->
    let rec absent (ps: list string) : Lemma (requires mem p ps) (ensures ~(first_absent keys ps == Missing)) =
      match ps with
      | [] -> ()
      | q :: r -> if q = p then () else absent r
    in
    if x = e
    then absent e.enode.dparents
    else
      match first_absent keys x.enode.dparents with
      | Found _ -> ()
      | Missing -> dangling_parent_absent keys t e p

(* THEOREM (no hypothesis at all). Dropping a node another node names — or naming a parent that
   was never there — is found by the second clause of the walk. The hash is not consulted. *)
let dangling_parent_detected
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (es: list (entry op))
  (e: entry op)
  (p: string)
  : Lemma
    (requires mem e es /\ mem p e.enode.dparents /\ not (mem p (keys_of es)))
    (ensures not (verify_dag h enc_op le es)) =
  dangling_parent_absent (keys_of es) es e p;
  break_none_iff h enc_op le es

(* ======================================================================================
   5. The boundary the sort creates — stated as theorems, not as prose.
   ====================================================================================== *)

(* NOT a tamper: re-ordering a node's parents leaves its id alone, because the pre-image is
   sorted. Nothing here detects it, and nothing should — see the next theorem for what the sort
   buys, and the claims ladder for what it costs. *)
let parent_reorder_undetected
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (ps: list string)
  (ps': list string)
  (a: string)
  (o: op)
  : Lemma
    (requires isort le ps == isort le ps')
    (ensures node_hash h enc_op le ps a o == node_hash h enc_op le ps' a o) = ()

(* … and what the sort buys, which is Phase 64.1's whole point: `merge(A,B)` and `merge(B,A)`
   converge to one content id, so two hosts reconciling the same pair mint the same node. Needs
   only that the comparison is a total order — nothing about the hash. Production has at most two
   parents (`append` gives none or one, `merge` exactly two), so this is the general case. *)
let merge_id_parent_order_independent
  (#op: eqtype)
  (h: string -> string -> string)
  (enc_op: op -> string)
  (le: string -> string -> bool)
  (x: string)
  (y: string)
  (a: string)
  (o: op)
  : Lemma
    (requires total_order le)
    (ensures node_hash h enc_op le [x; y] a o == node_hash h enc_op le [y; x] a o) = ()

(* ======================================================================================
   6. The linear chain (F#: `OpRecord<'Op>` and the `OpStream` walkers).

   `Seq` is an `int` in production and a Peano numeral here, for the reason section 11 of
   `DagFold.fst` uses a list as its walk fuel: F*'s F# backend extracts integers to `Prims.int`
   and arithmetic to `Prims` operators the oracle shim does not supply, and the shim exists to be
   five lines rather than a runtime. The walk only ever needs zero and successor, which is what a
   numeral is. `show` renders one — the model does not own `string (i: int)` any more than it owns
   the hash.
   ====================================================================================== *)

type pos =
  | PZero : pos
  | PSucc : pos -> pos

type record (op: eqtype) =
  { rseq: pos; ractor: string; rop: op; rprev: string; rhash: string }

(* F#: `OpStream.canonicalConfig`'s `payloadOf` — the `{seq, actor, op}` envelope, with the actor
   the typed object `Actor.encode` emits (Phase 320, which folded attribution into the chain). *)
let rec_payload
  (#op: eqtype)
  (show: pos -> string)
  (enc_op: op -> string)
  (s: pos)
  (a: string)
  (o: op)
  : Tot string =
  "{\"seq\":" ^ show s ^ ",\"actor\":" ^ a ^ ",\"op\":" ^ enc_op o ^ "}"

(* F#: `hashFn prev (cfg.Payload r.Seq r.Actor (w.Encode r.Op))`. *)
let rec_hash
  (#op: eqtype)
  (h: string -> string -> string)
  (show: pos -> string)
  (enc_op: op -> string)
  (prev: string)
  (s: pos)
  (a: string)
  (o: op)
  : Tot string =
  h prev (rec_payload show enc_op s a o)

(* The linear side's premise, in the same shape and for the same reason as the DAG's. Note it is
   injective in FOUR components, the predecessor's hash among them: that is what makes a chain
   re-cut from a different history detectable rather than merely different. *)
[@@ noextract_to "FSharp"]
let rec_injective
  (op: eqtype)
  (h: string -> string -> string)
  (show: pos -> string)
  (enc_op: op -> string)
  : Type =
  p1: string -> s1: pos -> a1: string -> o1: op ->
  p2: string -> s2: pos -> a2: string -> o2: op ->
  Lemma
    (requires rec_hash h show enc_op p1 s1 a1 o1 == rec_hash h show enc_op p2 s2 a2 o2)
    (ensures p1 == p2 /\ s1 == s2 /\ a1 == a2 /\ o1 == o2)

(* F#: `ChainBreak`, with `ChainBreakReason.toString`'s spellings verbatim. `Index` is the walk's
   own position, which is where production takes it from too. *)
type cbreak = { cindex: pos; creason: string; cexpected: string; cgot: string }

(* F#: `OpStream.firstChainBreakWith`, clause for clause: sequence, then prev-link, then the
   record's own recomputed hash — and the hash is computed only after the two cheap checks pass,
   as production's comment says it is. *)
let rec first_chain_break_from
  (#op: eqtype)
  (h: string -> string -> string)
  (show: pos -> string)
  (enc_op: op -> string)
  (prev: string)
  (i: pos)
  (rs: list (record op))
  : Tot (found cbreak) (decreases rs) =
  match rs with
  | [] -> Missing
  | r :: rest ->
    if not (r.rseq = i)
    then
      Found
        ({ cindex = i;
           creason = "sequence-number mismatch";
           cexpected = show i;
           cgot = show r.rseq })
    else if not (r.rprev = prev)
    then
      Found
        ({ cindex = i; creason = "prev-hash link broken"; cexpected = prev; cgot = r.rprev })
    else
      let expected = rec_hash h show enc_op prev r.rseq r.ractor r.rop in
      if not (r.rhash = expected)
      then
        Found
          ({ cindex = i;
             creason = "hash mismatch (tampered op/actor/seq)";
             cexpected = expected;
             cgot = r.rhash })
      else first_chain_break_from h show enc_op r.rhash (PSucc i) rest

(* F#: `OpStream.firstChainBreak` — the canonical config's genesis is `""`, and the walk starts at
   sequence zero. `genesis` stays a parameter because `StreamConfig` makes it one. *)
let first_chain_break
  (#op: eqtype)
  (h: string -> string -> string)
  (show: pos -> string)
  (enc_op: op -> string)
  (genesis: string)
  (rs: list (record op))
  : Tot (found cbreak) =
  first_chain_break_from h show enc_op genesis PZero rs

(* F#: `OpStream.verifyChain`. *)
let verify_chain
  (#op: eqtype)
  (h: string -> string -> string)
  (show: pos -> string)
  (enc_op: op -> string)
  (genesis: string)
  (rs: list (record op))
  : Tot bool =
  match first_chain_break h show enc_op genesis rs with
  | Missing -> true
  | Found _ -> false

let rec chain_ok_from
  (#op: eqtype)
  (h: string -> string -> string)
  (show: pos -> string)
  (enc_op: op -> string)
  (prev: string)
  (i: pos)
  (rs: list (record op))
  : Tot bool (decreases rs) =
  match rs with
  | [] -> true
  | r :: rest ->
    r.rseq = i && r.rprev = prev &&
    r.rhash = rec_hash h show enc_op prev r.rseq r.ractor r.rop &&
    chain_ok_from h show enc_op r.rhash (PSucc i) rest

let rec chain_break_none_iff
  (#op: eqtype)
  (h: string -> string -> string)
  (show: pos -> string)
  (enc_op: op -> string)
  (prev: string)
  (i: pos)
  (rs: list (record op))
  : Lemma
    (ensures
      (match first_chain_break_from h show enc_op prev i rs with
       | Missing -> true
       | Found _ -> false) == chain_ok_from h show enc_op prev i rs)
    (decreases rs) =
  match rs with
  | [] -> ()
  | r :: rest ->
    if not (r.rseq = i) then ()
    else if not (r.rprev = prev) then ()
    else if not (r.rhash = rec_hash h show enc_op prev r.rseq r.ractor r.rop) then ()
    else chain_break_none_iff h show enc_op r.rhash (PSucc i) rest

(* ---- intactness ---- *)

(* One `append`: the actor and the op. F#: `OpStream.appendWith`, whose `seq` is the record count
   and whose `prev` is the last record's hash (the genesis sentinel when there is none). The
   domain reducer is orthogonal to the chain — `append` refuses on a rejection and chains nothing
   — so what the model builds is the chain shape of a run of SUCCESSFUL appends. *)
type cstep (op: eqtype) = { cactor: string; cop: op }

let append_rec
  (#op: eqtype)
  (h: string -> string -> string)
  (show: pos -> string)
  (enc_op: op -> string)
  (prev: string)
  (i: pos)
  (a: string)
  (o: op)
  : Tot (record op) =
  { rseq = i; ractor = a; rop = o; rprev = prev; rhash = rec_hash h show enc_op prev i a o }

let rec build_chain
  (#op: eqtype)
  (h: string -> string -> string)
  (show: pos -> string)
  (enc_op: op -> string)
  (prev: string)
  (i: pos)
  (cs: list (cstep op))
  : Tot (list (record op)) (decreases cs) =
  match cs with
  | [] -> []
  | c :: t ->
    let r = append_rec h show enc_op prev i c.cactor c.cop in
    r :: build_chain h show enc_op r.rhash (PSucc i) t

let rec intact_chain_from
  (#op: eqtype)
  (h: string -> string -> string)
  (show: pos -> string)
  (enc_op: op -> string)
  (prev: string)
  (i: pos)
  (cs: list (cstep op))
  : Lemma
    (ensures chain_ok_from h show enc_op prev i (build_chain h show enc_op prev i cs))
    (decreases cs) =
  match cs with
  | [] -> ()
  | c :: t ->
    let r = append_rec h show enc_op prev i c.cactor c.cop in
    intact_chain_from h show enc_op r.rhash (PSucc i) t

(* THEOREM. A chain grown from genesis by `append` alone verifies. *)
let intact_chain_verifies
  (#op: eqtype)
  (h: string -> string -> string)
  (show: pos -> string)
  (enc_op: op -> string)
  (genesis: string)
  (cs: list (cstep op))
  : Lemma
    (ensures
      verify_chain h show enc_op genesis (build_chain h show enc_op genesis PZero cs)) =
  intact_chain_from h show enc_op genesis PZero cs;
  chain_break_none_iff h show enc_op genesis PZero (build_chain h show enc_op genesis PZero cs)

(* ---- tamper detection ---- *)

let rec record_at (#op: eqtype) (rs: list (record op)) (n: pos) : Tot (found (record op)) (decreases rs) =
  match rs, n with
  | [], _ -> Missing
  | r :: _, PZero -> Found r
  | _ :: t, PSucc m -> record_at t m

let rec replace_at (#op: eqtype) (rs: list (record op)) (n: pos) (r': record op)
  : Tot (list (record op)) (decreases rs) =
  match rs, n with
  | [], _ -> []
  | _ :: t, PZero -> r' :: t
  | r :: t, PSucc m -> r :: replace_at t m r'

(* A tamper on the linear side, in the same shape as the DAG's: the record's ADDRESSING — its
   stored hash and its prev-link — is left exactly as it was, and its content moves. *)
let same_content (#op: eqtype) (r: record op) (r': record op) : Tot bool =
  r.rseq = r'.rseq && r.ractor = r'.ractor && r.rop = r'.rop

(* THEOREM. Change one record's sequence, actor or op and leave its two hash fields alone, in a
   chain that was intact, and `OpStream.firstChainBreak` reports a break. The sequence arm needs
   nothing; the other two are where the premise is spent. *)
let rec chain_tamper_detected
  (#op: eqtype)
  (h: string -> string -> string)
  (show: pos -> string)
  (enc_op: op -> string)
  (inj: rec_injective op h show enc_op)
  (prev: string)
  (i: pos)
  (rs: list (record op))
  (n: pos)
  (r: record op)
  (r': record op)
  : Lemma
    (requires
      chain_ok_from h show enc_op prev i rs /\ record_at rs n == Found r /\
      r'.rprev == r.rprev /\ r'.rhash == r.rhash /\ not (same_content r r'))
    (ensures not (chain_ok_from h show enc_op prev i (replace_at rs n r')))
    (decreases rs) =
  match rs, n with
  | [], _ -> ()
  | x :: t, PZero ->
    if r'.rseq = i
    then
      begin
        if r'.rhash = rec_hash h show enc_op prev r'.rseq r'.ractor r'.rop
        then inj prev r'.rseq r'.ractor r'.rop prev r.rseq r.ractor r.rop
        else ()
      end
    else ()
  | x :: t, PSucc m ->
    chain_tamper_detected h show enc_op inj x.rhash (PSucc i) t m r r'

(* THEOREM (no hypothesis at all). Re-pointing one record's `PrevHash` is found by the prev-link
   check, which compares stored data against the walk's own running value and never consults the
   hash. This is what makes a re-ordered or spliced chain detectable independently of the hash's
   strength. *)
let rec chain_tamper_prev_detected
  (#op: eqtype)
  (h: string -> string -> string)
  (show: pos -> string)
  (enc_op: op -> string)
  (prev: string)
  (i: pos)
  (rs: list (record op))
  (n: pos)
  (r: record op)
  (r': record op)
  : Lemma
    (requires
      chain_ok_from h show enc_op prev i rs /\ record_at rs n == Found r /\
      r'.rseq == r.rseq /\ ~(r'.rprev == r.rprev))
    (ensures not (chain_ok_from h show enc_op prev i (replace_at rs n r')))
    (decreases rs) =
  match rs, n with
  | [], _ -> ()
  | _ :: _, PZero -> ()
  | x :: t, PSucc m -> chain_tamper_prev_detected h show enc_op x.rhash (PSucc i) t m r r'

(* THEOREM (no hypothesis at all). Re-numbering one record is found by the sequence check, for the
   same reason. *)
let rec chain_tamper_seq_detected
  (#op: eqtype)
  (h: string -> string -> string)
  (show: pos -> string)
  (enc_op: op -> string)
  (prev: string)
  (i: pos)
  (rs: list (record op))
  (n: pos)
  (r: record op)
  (r': record op)
  : Lemma
    (requires
      chain_ok_from h show enc_op prev i rs /\ record_at rs n == Found r /\
      ~(r'.rseq == r.rseq))
    (ensures not (chain_ok_from h show enc_op prev i (replace_at rs n r')))
    (decreases rs) =
  match rs, n with
  | [], _ -> ()
  | _ :: _, PZero -> ()
  | x :: t, PSucc m -> chain_tamper_seq_detected h show enc_op x.rhash (PSucc i) t m r r'

(* The three above are stated over `chain_ok_from`, which the characterisation makes the same
   statement about the walker itself. Spelled out once, at the entry point production's callers
   use, so the theorem a reader takes away is about `verifyChain`. *)
let chain_tamper_detected_verify
  (#op: eqtype)
  (h: string -> string -> string)
  (show: pos -> string)
  (enc_op: op -> string)
  (inj: rec_injective op h show enc_op)
  (genesis: string)
  (rs: list (record op))
  (n: pos)
  (r: record op)
  (r': record op)
  : Lemma
    (requires
      verify_chain h show enc_op genesis rs /\ record_at rs n == Found r /\
      r'.rprev == r.rprev /\ r'.rhash == r.rhash /\ not (same_content r r'))
    (ensures not (verify_chain h show enc_op genesis (replace_at rs n r'))) =
  chain_break_none_iff h show enc_op genesis PZero rs;
  chain_tamper_detected h show enc_op inj genesis PZero rs n r r';
  chain_break_none_iff h show enc_op genesis PZero (replace_at rs n r')
