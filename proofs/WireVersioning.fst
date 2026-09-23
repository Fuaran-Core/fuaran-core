(*
   Versioning — WIRE_FORMAT §15.4's evolution-policy table, as a theorem about the IDL diff
   (fuaran-core Phase 151).

   WHAT THIS IS. §15.4 classifies a vocabulary change by an IDL diff — no removed tags means
   additive, any removal or rename means breaking — and §15.3 promises that a `Behind` consumer
   PRESERVES what it does not understand. Since Phase 127 that classification is COMPUTED, by
   `Versioning.classify` / `Versioning.bump` in `src/Fuaran.Core.Wire/Wire.fs`, and it is exercised
   by tests that perturb the real vocabulary. What no test can say is that the classification is
   SOUND: that an "additive" verdict really does leave every old document decoding to the same
   value, and that preservation really does reproduce the producer's bytes. A test samples; a
   quantifier does not. This module carries the four statements that close that gap:

     - `classify_sound` — `classify` answers `Additive` ONLY when the before-vocabulary is a
       SUBSET of the after-vocabulary. This is the table's own rule stated as a fact about the
       function rather than about the prose, and it is what the other three stand on.
     - `additive_monotone` — given that verdict, every document well-formed under the OLD
       vocabulary decodes under the NEW one to exactly what it decoded to before. "Additive means
       old documents still work", with `still work` meaning `==` and not `approximately`.
     - `preserve_exact` — a document authored under the NEW vocabulary, carrying a tag the old
       consumer does not know, survives decode → re-encode → render with the PRODUCER'S BYTES
       intact. This is §15.3's must-ignore-but-preserve, and it is the reason Phase 149's
       canonical-form work is imported rather than restated: the claim is about bytes, and the
       renderer that produces them is already modelled and already proved injective on the
       canonical subset.
     - `unknown_transport_only` — no value an ENCODER produces decodes to `Unknown`, for a
       consumer that knows the vocabulary the encoder authors against. §15.3's "transport-only,
       un-constructible on encode" is a comment in the shipped source; here it is a theorem.

   WHAT THIS DELIBERATELY IS NOT. It is not a change to the policy, and it is not a claim that
   the policy is COMPLETE. A row of §15.4's table this module cannot support is a finding for the
   specification's owners, recorded in `proofs/README.md`'s theorem section and raised there —
   never a quiet rewrite of `classify` to make a theorem go through.

   ONE SUCH ROW WAS NAMED HERE AND IS NO LONGER ONE (Phase 200). The optional-field row was
   recorded as satisfied vacuously and PERMANENTLY so, on the argument that a field addition is
   invisible to `classify` and that widening `classify` to see fields would model a function this
   repository does not ship. The second half of that argument was wrong, and the correction is
   section 7: `Versioning.classify` takes two SUBJECT sets rather than kind tags, `Diff.evolution`
   is the shipped caller that builds them from each row's SEVERITY, and `Diff.classifyFieldAdd` is
   the shipped function that reads the optionality class. Modelling that composition needs no
   widening of anything — `classify` is used at its own signature, over its own caller's input —
   and the row then has content: an added optional field bumps the MINOR and leaves an old
   consumer `Behind`, where a host-only one moves no profile at all. Section 6 keeps the other
   half, that the tag delta alone cannot decide the row, because both halves are needed to state
   the policy correctly.

   WHY THE TAG TYPE IS `list ch` AND NOT A PARAMETER. Set algebra over tags does not care what a
   tag is, so the classification half would be happier parametric — but `additive_monotone` and
   `preserve_exact` JOIN that half to the decode half, where a tag is a value read by name out of
   a `jval` object and is therefore a `list ch` already. One tag type is what lets the two halves
   meet in one statement; two would make the join a coercion, and a theorem about a coercion is a
   theorem about the coercion. It also means the differential needs one bridge and not two, and
   the bridge it needs is the one Phase 149's family already has.

   WHY THE MODULE IS `WireVersioning` AND NOT `Versioning`. The same reason `WireCanon.fst` is not
   called `Canon` and `TreeOps.fst` is not called `Ops`: the extracted oracle is a TOP-LEVEL F#
   module, and the differential host opens `Fuaran.Core`, which already carries a `Versioning`.
   The two would shadow each other exactly where the differential needs both — and it needs both
   in the same expression, since the whole of this model's host step is `classify` beside
   `classify`. The `.fst`, the extracted `.fs`, the `$modules` entry and the budget entry all
   carry the prefixed name; the phase shard's key-files list names the unprefixed one, and this
   paragraph is the correction.

   WHY `WireCanon` IS OPENED. `preserve_exact` is a claim about BYTES. Phase 149 modelled
   `Canon.render` clause for clause, proved it deterministic up to the member-order rule, and
   proved the round trip; re-deriving any of that here would be a second renderer to keep in step
   with the first. What is taken is small and named: `jval`, `ch`, `wire`, `render`, `read`,
   `normalise`, `canonical`, `key_order_ok`, `tok_read_ok`, and two lemmas
   (`read_render`, `render_ignores_key_order`).

   Apache-2.0, like everything beside it.
*)
module WireVersioning

(* Opening `WireCanon` brings its whole context — some ninety patterned facts about rendering,
   sorting and reading — into every query here, and this module's own queries are small list
   inductions that need none of them. Pruned for the reason `TreeDiff.fst` gives at the same line,
   and scoped here rather than in the leg's flags for the reason `WireCanon.fst` gives at its. *)
#set-options "--ext context_pruning"

open WireCanon

(* ======================================================================================
   1. A VOCABULARY, and the delta between two of them.

      F#: `Versioning.classify (before: Set<string>) (after: Set<string>)`. Production holds a
      vocabulary as an F# `Set`, which is a sorted duplicate-free collection; the model holds one
      as a list, and every statement below is written with `mem` rather than with the list's
      shape, so nothing here depends on the order or on the absence of duplicates. That is not a
      weakening — it is what makes the theorems apply to the production function, whose `Set`
      hands back a SORTED list from `Set.toList` and whose caller may have built it from anything.
   ====================================================================================== *)

(* F#: `Set.difference xs ys |> Set.toList`. Order-preserving in `xs`, which is what makes the
   differential's comparison against `Set.toList` an equality rather than a set comparison: a
   sorted duplicate-free `xs` gives a sorted duplicate-free result, and that is the only shape
   production ever passes. *)
let rec diff (xs ys: list (list ch)) : Tot (list (list ch)) (decreases xs) =
  match xs with
  | [] -> []
  | h :: t -> if mem h ys then diff t ys else h :: diff t ys

(* The subset relation the table's "additive" row is ABOUT, written once so that three theorems
   can name the same thing. Deliberately a `prop` over `mem` and not a statement about lists. *)
let sub (v v': list (list ch)) : prop = forall (x: list ch). mem x v ==> mem x v'

(* F#: `Versioning.Evolution`. `Additive` carries what was added; `Breaking` carries what was
   removed BESIDE what was added, because a rename is exactly the pair and a reader of a breaking
   verdict needs both halves to write the migration shim. *)
type evolution =
  | Additive : added: list (list ch) -> evolution
  | Breaking : removed: list (list ch) -> added: list (list ch) -> evolution

(* F#: `Versioning.classify`, clause for clause. No removals ⇒ `Additive`; any removal ⇒
   `Breaking`. A RENAME surfaces as a removal plus an add and is therefore `Breaking` — which is
   a consequence of this definition and not a special case in it, and `rename_is_breaking` below
   is that consequence proved rather than asserted. *)
let classify (before after: list (list ch)) : Tot evolution =
  let added = diff after before in
  let removed = diff before after in
  if Nil? removed then Additive added else Breaking removed added

(* ======================================================================================
   2. THE PROFILE, and the bump the verdict drives.

      F#: `Versioning.Profile` / `Versioning.negotiate` / `Versioning.bump`. These are here
      because §15.4's table is a table of BUMPS — the classification is only interesting because
      of what it does to the profile, and a consumer's tolerance is decided by `negotiate`, not
      by the classifier. `unknown_transport_only` and `preserve_exact` are both statements about
      what a `Behind` consumer does, so the thing that decides `Behind` belongs in the model.
   ====================================================================================== *)

(* F#: `{ Name: string; Major: int; Minor: int }`. The two counters are `nat` rather than `int`
   for the reason `Limits.fst` gives for its constants: production's parser refuses a negative
   component (`Profile.tryParse` guards `v >= 0`), so a negative profile is not a value the wire
   can carry, and modelling it as one would put unreachable arms into every query below. *)
type profile = { name: list ch; major: nat; minor: nat }

(* F#: `Versioning.Compatibility`. *)
type compatibility =
  | Current
  | Behind  : authored: profile -> compatibility
  | Foreign : authored: profile -> compatibility

(* F#: `Versioning.negotiate`, clause for clause and in the same order — a different namespace or
   a different major is `Foreign` BEFORE the minor is looked at, which is what makes a major bump
   a hard boundary rather than a large minor one. *)
let negotiate (consumer authored: profile) : Tot compatibility =
  if authored.name <> consumer.name || authored.major <> consumer.major then Foreign authored
  else if authored.minor > consumer.minor then Behind authored
  else Current

(* F#: `Versioning.bump`. The empty additive is its own arm in production and is its own arm here:
   a change that added nothing bumps nothing, so the profile a no-op mints is the one it started
   with, and a consumer meeting it stays `Current`. *)
let bump (base_profile: profile) (ev: evolution) : Tot profile =
  match ev with
  | Additive [] -> base_profile
  | Additive _ -> { base_profile with minor = base_profile.minor + 1 }
  | Breaking _ _ -> { base_profile with major = base_profile.major + 1; minor = 0 }

(* ======================================================================================
   3. THE TOLERANT DECODE BOUNDARY.

      F#: `Versioning.UnknownKind` / `Decoded` / `decodeTolerant` / `reencode`. This is the whole
      forward-compatibility seam, and the shape of the production function is preserved exactly:
      the discriminator reader, the vocabulary test, the known-decoder and the required-profile
      reader are all PARAMETERS. They are parameters in production because the seam is generic
      over a domain's codec, and they are parameters here for the same reason plus one more — a
      theorem that quantifies over every `tag_of` is a theorem about the seam, where one that
      fixed a particular reader would be a theorem about that reader.
   ====================================================================================== *)

(* F#: `{ Kind: string; Payload: JVal; RequiredProfile: Profile option }`. `payload` is the
   VERBATIM parsed value — that field is the whole of preservation, and `preserve_exact` is a
   statement about it. *)
type unknown_kind (num flt: eqtype) = {
  kind: list ch;
  payload: jval num flt;
  required_profile: option profile;
}

(* F#: `Versioning.Decoded<'T>`. *)
type decoded (num flt: eqtype) (t: Type) =
  | Known   : v:t -> decoded num flt t
  | Unknown : u:unknown_kind num flt -> decoded num flt t

(* The vocabulary test a consumer supplies, for a consumer whose vocabulary is a list of tags.
   Named rather than written inline three times, so the three theorems below are visibly about
   the SAME consumer and the prover sees one definition rather than three lambdas. *)
let known_in (v: list (list ch)) : list ch -> Tot bool = fun (x: list ch) -> mem x v

(* F#: `Versioning.decodeTolerant`, clause for clause. The arms in production's order: a
   discriminator that does not read is an ERROR and stays one (a genuinely malformed object is
   not a forward-compatibility event); a tag the consumer knows goes to the known decoder, whose
   own error is also an error; and a tag it does not know is NOT an error — it becomes a
   transport-only `Unknown` carrying the verbatim value and whatever profile the artifact
   declared it needs.

   `read_required` is a parameter where production has a private `readRequiredProfile`. The
   difference is deliberate and is the model being HONEST rather than convenient: production's
   reader is a specific lookup that swallows a malformed profile to `None`, and none of the four
   theorems depends on which profile comes back or on whether one does. Fixing it here would put
   a reader into every query for a field no theorem reads. *)
let decode_tolerant
  (#num #flt: eqtype) (#t: Type)
  (tag_of: jval num flt -> outcome (list ch))
  (is_known: list ch -> bool)
  (decode_known: jval num flt -> outcome t)
  (read_required: jval num flt -> option profile)
  (el: jval num flt)
  : Tot (outcome (decoded num flt t)) =
  match tag_of el with
  | Error e -> Error e
  | Ok tag ->
    if is_known tag then
      (match decode_known el with
       | Ok v -> Ok (Known v)
       | Error e -> Error e)
    else
      Ok (Unknown ({ kind = tag; payload = el; required_profile = read_required el }))

(* F#: `Versioning.reencode`. The `Unknown` arm returns the preserved payload VERBATIM — one
   clause, and the one that makes must-ignore-but-preserve true by construction rather than by
   care. `preserve_exact` is that clause carried through `render`. *)
let reencode
  (#num #flt: eqtype) (#t: Type)
  (encode_known: t -> jval num flt)
  (d: decoded num flt t)
  : Tot (jval num flt) =
  match d with
  | Known v -> encode_known v
  | Unknown u -> u.payload

(* ======================================================================================
   4. THE LIST FACTS the four theorems stand on.

      Two, and both about `diff`. They are stated over `mem` rather than over list structure for
      the reason section 1 gives, and they are the only inductions in the module — everything
      below is a case split once these are in hand.
   ====================================================================================== *)

(* An empty difference means containment. This is the direction `classify_sound` needs: the
   function computes a LIST and the table talks about a SUBSET, and these are the same statement
   only because of this lemma. *)
[@@ noextract_to "FSharp"]
let rec diff_nil_implies_mem (a b: list (list ch)) (x: list ch)
  : Lemma (requires Nil? (diff a b) /\ mem x a) (ensures mem x b) (decreases a) =
  match a with
  | [] -> ()
  | h :: t ->
    (* `Nil? (diff (h :: t) b)` forces the `mem h b` branch, so `h` is in `b`; the tail's
       difference is empty too, which is the induction hypothesis' premise. *)
    if x = h then () else diff_nil_implies_mem t b x

(* And the converse direction, which is what makes a REMOVAL visible: a tag in `a` and not in `b`
   puts something in the difference, so `classify` cannot answer `Additive`. Without this,
   `rename_is_breaking` would be a claim about a function nobody had shown could ever return
   `Breaking` at all. *)
[@@ noextract_to "FSharp"]
let rec mem_not_mem_implies_diff_cons (a b: list (list ch)) (x: list ch)
  : Lemma (requires mem x a /\ not (mem x b)) (ensures Cons? (diff a b)) (decreases a) =
  match a with
  | [] -> ()
  | h :: t -> if h = x then () else mem_not_mem_implies_diff_cons t b x

(* ======================================================================================
   5. THE FOUR THEOREMS.
   ====================================================================================== *)

(* ---- THEOREM 1 (§15.4, the table's own rule): `classify` is SOUND ----

   `Additive` is answered ONLY when every tag of the before-vocabulary survives into the after-
   vocabulary. Stated per-tag rather than as `sub` so that it is usable as a rewrite wherever a
   single tag is in hand, which is how the next theorem uses it; `classify_additive_is_sub` just
   below packages the quantified form for a reader who wants the table's sentence literally. *)
let classify_sound (before after: list (list ch)) (added: list (list ch)) (x: list ch)
  : Lemma (requires classify before after == Additive added /\ mem x before)
          (ensures mem x after) =
  diff_nil_implies_mem before after x

(* The same fact as the table states it: an additive verdict IS the subset claim. *)
[@@ noextract_to "FSharp"]
let classify_additive_is_sub (before after: list (list ch)) (added: list (list ch))
  : Lemma (requires classify before after == Additive added) (ensures sub before after) =
  FStar.Classical.forall_intro
    (FStar.Classical.move_requires (classify_sound before after added))

(* The RENAME row, which §15.4 calls out by name because it is the one an author gets wrong: a
   rename is a removal and an add, and the removal is what decides. Nothing about the add enters
   the proof, which is the point — a `Breaking` verdict does not become additive by being
   accompanied by additions. *)
let rename_is_breaking (before after: list (list ch)) (removed_tag: list ch)
  : Lemma (requires mem removed_tag before /\ not (mem removed_tag after))
          (ensures Breaking? (classify before after)) =
  mem_not_mem_implies_diff_cons before after removed_tag

(* And the bump the verdict drives, which is the half of the table a reader actually acts on: a
   breaking verdict mints a new MAJOR, and a consumer on the old major is `Foreign` — it refuses
   rather than mis-decoding. Stated here because a soundness claim about `classify` that stopped
   short of `bump` would leave the table's consequence unproved. *)
let breaking_bump_is_foreign (base_profile: profile) (ev: evolution) (consumer: profile)
  : Lemma (requires Breaking? ev /\ consumer.name == base_profile.name /\
                    consumer.major == base_profile.major)
          (ensures Foreign? (negotiate consumer (bump base_profile ev))) = ()

(* The additive counterpart: an additive bump keeps the major, so the same consumer is `Behind`
   and not `Foreign` — it tolerates rather than refusing. Together with the lemma above this is
   §15.4's two-row table, as the two verdicts it produces. *)
let additive_bump_is_behind (base_profile: profile) (added: list (list ch)) (consumer: profile)
  : Lemma (requires Cons? added /\ consumer.name == base_profile.name /\
                    consumer.major == base_profile.major /\ consumer.minor == base_profile.minor)
          (ensures Behind? (negotiate consumer (bump base_profile (Additive added)))) = ()

(* ---- THEOREM 2 (§15.4's "additive" row, as a claim about DECODING): `additive_monotone` ----

   The row says an additive step is one an older consumer survives. The sharper statement, and
   the one worth proving, is that it does not merely survive it but is UNAFFECTED by it: a
   document whose tag the old vocabulary already carried decodes under the new vocabulary to
   exactly the same result — the same `Known`, the same payload, the same error if the known
   decoder errors. Nothing about the widening is observable to a document that predates it.

   Quantified over every discriminator reader, every known decoder and every required-profile
   reader, so it is a fact about the SEAM rather than about one domain's codec. *)
let additive_monotone
  (#num #flt: eqtype) (#t: Type)
  (before after added: list (list ch))
  (tag_of: jval num flt -> outcome (list ch))
  (decode_known: jval num flt -> outcome t)
  (read_required: jval num flt -> option profile)
  (el: jval num flt) (tg: list ch)
  : Lemma (requires classify before after == Additive added /\
                    tag_of el == Ok tg /\ mem tg before)
          (ensures decode_tolerant tag_of (known_in after) decode_known read_required el
                   == decode_tolerant tag_of (known_in before) decode_known read_required el) =
  classify_sound before after added tg

(* ---- THEOREM 3 (§15.3's must-ignore-but-preserve, at the BYTES): `preserve_exact` ----

   A producer authors `authored` under the NEW vocabulary and renders it; an OLD consumer, whose
   vocabulary is `consumer_vocab`, parses those bytes, meets a tag it does not know, tolerantly
   decodes, re-encodes and renders. The claim is that what comes out is what went in — not a
   value that means the same, but the same BYTES, which is the only thing that makes preservation
   verifiable on a hash chain.

   The hypotheses are Phase 149's and are named rather than assumed away: `tok_read_ok` and
   `key_order_ok` are that module's two premises about the host's numeral reader and its key
   comparator, and `canonical` is rule 5's slot rule. The `read` hypothesis is how the consumer
   gets from bytes to a value, and `read_render` is what turns it into an equation.

   Note what the proof does NOT need: nothing about `encode_known`, and nothing about the payload
   beyond its identity. That is the shape of a preservation claim that is true by construction —
   and it is why the corresponding production comment is safe to believe rather than merely
   plausible. *)
let preserve_exact
  (#num #flt: eqtype) (#t: Type)
  (w: wire num flt)
  (consumer_vocab: list (list ch))
  (tag_of: jval num flt -> outcome (list ch))
  (decode_known: jval num flt -> outcome t)
  (read_required: jval num flt -> option profile)
  (encode_known: t -> jval num flt)
  (authored parsed: jval num flt) (tg: list ch)
  : Lemma (requires tok_read_ok w /\ key_order_ok w /\ canonical w authored /\
                    read w (render w authored) == Ok (parsed, []) /\
                    tag_of parsed == Ok tg /\ not (mem tg consumer_vocab))
          (ensures (match decode_tolerant tag_of (known_in consumer_vocab)
                                          decode_known read_required parsed with
                    | Ok d -> render w (reencode encode_known d) == render w authored
                    | Error _ -> False)) =
  (* The reader hands back the producer's value with its object members in canonical key order. *)
  read_render w authored;
  (* And rule 2 says that reordering is exactly what the renderer cannot see. *)
  render_ignores_key_order w authored

(* ---- THEOREM 4 (§15.3's by-construction claim): `unknown_transport_only` ----

   `Unknown` is reachable on the decode boundary and nowhere else. The shipped source says so in
   a comment — "transport-only: it is reachable here and nowhere on the authoring/encode path" —
   and a comment is exactly what this module exists to replace. The statement is the composite a
   reader cares about: take ANY value the encoder produces, decode it tolerantly with the
   vocabulary the encoder authors against, and the result is never an `Unknown`.

   The one hypothesis is the one that makes it a theorem about the seam and not about a slip:
   the encoder's own tag must be in the consumer's vocabulary. That is what "the vocabulary the
   encoder authors against" MEANS, and where it fails the encoder and the consumer are at
   different profiles — which is `Behind`, which is theorem 3's case and not this one. *)
let unknown_transport_only
  (#num #flt: eqtype) (#t: Type)
  (vocab: list (list ch))
  (tag_of: jval num flt -> outcome (list ch))
  (decode_known: jval num flt -> outcome t)
  (read_required: jval num flt -> option profile)
  (encode_known: t -> jval num flt)
  (x: t) (tg: list ch)
  : Lemma (requires tag_of (encode_known x) == Ok tg /\ mem tg vocab)
          (ensures (match decode_tolerant tag_of (known_in vocab) decode_known read_required
                                          (encode_known x) with
                    | Ok (Unknown _) -> False
                    | _ -> True)) = ()

(* The other half of the same claim, and the reason it needs saying separately: `reencode` is the
   only function here that consumes a `decoded`, and on a `Known` it is exactly the encoder. So
   there is no round trip through the seam that can introduce an `Unknown` either. Together with
   the theorem above, every path from a `t` back to a `jval` is `Unknown`-free. *)
let reencode_known_is_encode
  (#num #flt: eqtype) (#t: Type)
  (encode_known: t -> jval num flt) (x: t)
  : Lemma (ensures reencode encode_known (Known #num #flt #t x) == encode_known x) = ()

(* ======================================================================================
   6. THE GO-RED, and what the tag delta alone cannot decide.
   ====================================================================================== *)

(* The instrument the differential must LOSE with: a classifier that computes the additions and
   never looks for removals. It is not a strawman — it is the classifier an author writes when
   they read §15.4's additive row and stop there, and it agrees with production on every purely
   additive change, which is most of them. A comparison that cannot tell this from the real thing
   is measuring nothing, and the differential's go-red case is what says it can. *)
let classify_ignoring_removals (before after: list (list ch)) : Tot evolution =
  Additive (diff after before)

(* And it is UNSOUND, exhibited rather than argued: one vocabulary, one tag, removed. The witness
   is concrete because a refutation that is not concrete is a claim about a claim. *)
[@@ noextract_to "FSharp"]
let classify_ignoring_removals_is_unsound ()
  : Lemma (ensures (exists (before after: list (list ch)) (x: list ch).
                      Additive? (classify_ignoring_removals before after) /\
                      mem x before /\ not (mem x after))) =
  let tag : list ch = [CPlain "a"] in
  let before : list (list ch) = [tag] in
  let after : list (list ch) = [] in
  assert (Additive? (classify_ignoring_removals before after));
  assert (mem tag before);
  assert (not (mem tag after))

(* And the real classifier is not: on the same pair it answers `Breaking`. Stated beside the
   refutation so that the two read as one sentence — the broken model says additive where the
   shipped one says breaking, which is exactly the disagreement the differential looks for. *)
[@@ noextract_to "FSharp"]
let classify_disagrees_with_the_broken_one ()
  : Lemma (ensures (let tag : list ch = [CPlain "a"] in
                    Breaking? (classify [tag] []) /\
                    Additive? (classify_ignoring_removals [tag] []))) = ()

(* ---- THE TAG DELTA IS BLIND TO FIELDS, which is why section 7 exists ----

   §15.4's table has a row for an ADDED OPTIONAL FIELD, and it is additive. Nothing ABOVE this
   line sees it, and nothing above it can: `classify` compares the two lists its caller hands it,
   and a caller that hands it KIND TAGS has already discarded the field, so the delta is empty and
   the verdict is `Additive []` — the no-op arm, reached for a reason unrelated to what the row is
   about.

   Until Phase 200 that was recorded as a permanent gap, on the argument that widening `classify`
   to see field sets would model a function this repository does not ship. The argument was wrong
   in one specific, checkable way, and section 7 is the correction. `Versioning.classify` is not a
   function over kind tags at all — it is a function over two SUBJECT SETS — and which subjects a
   revision RETIRES and which it INTRODUCES is decided by `Diff.evolution` in
   `src/Fuaran.Core.Idl.Codegen/Diff.fs` from the SEVERITY `Diff.classifyFieldAdd` gives each row.
   An added optional field is a subject that composition introduces, so the verdict is a NON-EMPTY
   `Additive`, the minor moves, and `Diff.bumpProfile` carries it through `Versioning.bump` — the
   shipped profile bump, not an advisory report. The row was never vacuous in production; it was
   vacuous in the MODEL, because the model stopped at the tag delta.

   The lemma stays, renamed for what it actually says: the tag delta alone cannot decide the row.
   That is what makes section 7 necessary rather than decorative, and it is also the reason the
   two sections must both be here — a reader who takes either half for the whole policy gets the
   wrong answer, in opposite directions. *)
let tag_delta_is_blind_to_field_additions (vocab: list (list ch)) (base_profile: profile)
  : Lemma (requires Nil? (diff vocab vocab))
          (ensures classify vocab vocab == Additive [] /\
                   bump base_profile (classify vocab vocab) == base_profile /\
                   negotiate base_profile (bump base_profile (classify vocab vocab)) == Current) = ()

(* ======================================================================================
   7. THE OPTIONAL-FIELD ROW, at the composition that actually decides it (Phase 200).

      F#: `Diff.Severity` / `Diff.classifyFieldAdd` / `Diff.evolution` in
      `src/Fuaran.Core.Idl.Codegen/Diff.fs`, and `Versioning.classify` / `bump` / `negotiate`
      already modelled above, which is what that composition ENDS at.

      Section 6 says the tag delta cannot see a field. This section models the thing that can.
      The shape is worth stating before the definitions, because it is the whole finding: a field
      addition is classified by its OPTIONALITY CLASS into a severity, the severity decides which
      of two subject sets the row joins, and the two subject sets are what `classify` compares.
      Nothing is widened and no function is merged — `classify` is used at exactly its shipped
      signature, over exactly the input its shipped caller passes it.

      The subject STRINGS stay abstract here. Production renders them with `Diff.summarise`, and
      a theorem quantified over every subject is a theorem about the partition rather than about
      one rendering — which is the same choice section 3 makes for the discriminator reader, and
      for the same reason.
   ====================================================================================== *)

(* F#: `Diff.Severity`, in production's declaration order. `SUnclassifiable` is carried although
   nothing below branches on it: it is the arm that stops the bump OUTRIGHT (`Diff.bumpProfile`
   returns `Undecided` rather than a profile), and a severity type missing it would let a reader
   conclude that every row this model admits produces a bump. *)
type severity =
  | SAdditive
  | SBreakingForEmitters
  | SBreakingWire
  | SHostSurfaceOnly
  | SUnclassifiable

(* The two reserved optionality spellings `classifyFieldAdd` branches on, in theorem 7's character
   alphabet. Written as named constants for the reason `WireCanon`'s `true_chars` and `null_chars`
   are: a spelling the model must get exactly right is a definition, not a literal buried in a
   guard. `"required"` and `"hostOnly"` — every other class (`optional`, `omitDefault`, and any
   the artifact grows) falls to the default arm, which is production's `| _ ->` and is why the
   model tests these two rather than enumerating the vocabulary. *)
let required_chars : list ch =
  [CPlain "r"; CHexCh HDe; CPlain "q"; CLu; CPlain "i"; CPlain "r"; CHexCh HDe; CHexCh HDd]

let host_only_chars : list ch =
  [CPlain "h"; CPlain "o"; CPlain "s"; CPlain "t"; CPlain "O"; CPlain "n"; CPlain "l"; CPlain "y"]

(* F#: `Diff.classifyFieldAdd`, clause for clause, keeping only the severity — the rationale and
   citation it also returns are prose for a report and decide nothing.

   `required` is `BreakingForEmitters` and NOT `BreakingWire`: every existing document still
   decodes, so the wire evolution is additive and the consumer is `Behind`; what breaks is the
   EMITTER, which the verdict carries on its own axis because a minor cannot express it. That
   split is the row an author gets wrong here in the same way a rename is the row they get wrong
   in section 5. *)
let classify_field_add (opt_class: list ch) : Tot severity =
  if opt_class = required_chars then SBreakingForEmitters
  else if opt_class = host_only_chars then SHostSurfaceOnly
  else SAdditive

(* F#: the two `subjects` predicates inside `Diff.evolution`. A severity either retires a subject,
   introduces one, or moves neither set — and `SHostSurfaceOnly` / `SUnclassifiable` moving
   neither is what makes the classification observable at all. *)
let retires (s: severity) : Tot bool = SBreakingWire? s

let introduces (s: severity) : Tot bool = SAdditive? s || SBreakingForEmitters? s

(* F#: `cs |> List.filter pick |> List.map (fun c -> summarise c.Change) |> Set.ofList`. The model
   holds the result as a list where production holds a `Set`, for the reason section 1 gives for
   vocabularies — every statement below is about which subjects are present, never about their
   order or multiplicity. *)
let rec subjects (p: severity -> bool) (rows: list (severity & list ch))
  : Tot (list (list ch)) (decreases rows) =
  match rows with
  | [] -> []
  | (s, subj) :: t -> if p s then subj :: subjects p t else subjects p t

(* F#: `Diff.evolution`. It DELEGATES to `classify` rather than restating the rule, which is the
   whole reason this section can be written without touching section 5. *)
let evolution_of (rows: list (severity & list ch)) : Tot evolution =
  classify (subjects retires rows) (subjects introduces rows)

(* ---- THEOREM 5 (§15.4's optional-field row, with content): the minor MOVES ----

   A revision whose only change is one added optional field introduces exactly that subject and
   retires nothing, so the verdict is `Additive [subject]` — the non-empty arm — the minor bumps,
   the major does not, and a consumer sitting at the base profile negotiates `Behind`: it
   tolerates the document and preserves what it does not understand, which is what §15.4's row
   promises and what the tag delta alone could not say.

   Quantified over every subject rendering and every optionality class that is neither of the two
   reserved spellings, so it is a fact about the row and not about one field somebody added. *)
let optional_field_addition_bumps_the_minor
  (subj: list ch) (opt_class: list ch) (base_profile: profile) (consumer: profile)
  : Lemma (requires opt_class <> required_chars /\ opt_class <> host_only_chars /\
                    consumer.name == base_profile.name /\
                    consumer.major == base_profile.major /\
                    consumer.minor == base_profile.minor)
          (ensures (let ev = evolution_of [(classify_field_add opt_class, subj)] in
                    ev == Additive [subj] /\
                    (bump base_profile ev).major == base_profile.major /\
                    (bump base_profile ev).minor == base_profile.minor + 1 /\
                    Behind? (negotiate consumer (bump base_profile ev)))) = ()

(* The REQUIRED-field row, which reads as a contradiction until you ask whose profile it is. It
   bumps the same minor: every document valid under the old contract is still valid, so the wire
   evolution is additive and an old CONSUMER is `Behind` rather than `Foreign`. What moved is the
   obligation on EMITTERS, and the verdict carries that on `BreaksEmitters` — a boolean beside the
   profile rather than folded into it, because a major would tell every consumer to refuse
   documents that decode perfectly. Stated here so that a reader cannot conclude from theorem 5
   that "additive minor" means "nothing to do". *)
let required_field_addition_is_behind_not_foreign
  (subj: list ch) (base_profile: profile) (consumer: profile)
  : Lemma (requires consumer.name == base_profile.name /\
                    consumer.major == base_profile.major /\
                    consumer.minor == base_profile.minor)
          (ensures (let ev = evolution_of [(classify_field_add required_chars, subj)] in
                    ev == Additive [subj] /\
                    (bump base_profile ev).major == base_profile.major /\
                    Behind? (negotiate consumer (bump base_profile ev)))) = ()

(* And the HOST-ONLY row, which is the one that genuinely moves nothing — WIRE_FORMAT §9's
   wire-omitted fields are on no document in either direction, so no profile can honestly move.
   It is stated beside the two above because it is what makes them a MEASUREMENT: a model in which
   every field addition bumped the minor would agree with production on two rows out of three and
   would be measuring the arrival of a field rather than its optionality class. *)
let host_only_field_addition_moves_no_profile (subj: list ch) (base_profile: profile)
  : Lemma (ensures (let ev = evolution_of [(classify_field_add host_only_chars, subj)] in
                    ev == Additive [] /\
                    bump base_profile ev == base_profile /\
                    negotiate base_profile (bump base_profile ev) == Current)) = ()

(* ---- THE GO-RED for this section ----

   The classifier an author writes who reads "an added field is additive" and stops: it answers
   `SAdditive` whatever the optionality class says. It agrees with production on the optional row,
   which is the commonest one, and it is wrong on both of the others — so a comparison that cannot
   separate it from the real thing is measuring the arrival of a field and nothing else. *)
let classify_field_add_ignoring_optionality (opt_class: list ch) : Tot severity = SAdditive

(* And the disagreement, exhibited at the level a reader acts on rather than at the severity: on a
   host-only field the broken classifier moves a profile that must not move. A refutation stated
   as "the severities differ" would be a claim about an internal; this one is a claim about the
   version somebody publishes. *)
[@@ noextract_to "FSharp"]
let ignoring_optionality_moves_a_profile_that_must_not (subj: list ch) (base_profile: profile)
  : Lemma (ensures evolution_of [(classify_field_add host_only_chars, subj)] == Additive [] /\
                   evolution_of [(classify_field_add_ignoring_optionality host_only_chars, subj)]
                     == Additive [subj] /\
                   bump base_profile (evolution_of [(classify_field_add host_only_chars, subj)])
                     == base_profile /\
                   (bump base_profile
                      (evolution_of [(classify_field_add_ignoring_optionality host_only_chars, subj)])).minor
                     == base_profile.minor + 1) = ()
