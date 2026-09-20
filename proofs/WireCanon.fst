(*
   WireCanon — an F* model of Fuaran.Core's CANONICAL ENCODER, with the canonical form's determinism
   and its converse as machine-checked theorems (fuaran-core Phase 149).

   WHAT IS MODELLED. `Fuaran.Core.Canon` (src/Fuaran.Core.Wire/Wire.fs), clause for clause:
   `Canon.escape` (WIRE_FORMAT §2 rule 6), `Canon.canonicalFloat` (rule 5, including the `-0`
   collapse and the three non-finite tokens), and `Canon.render` (rules 2, 3, 5, 6, 7 and the
   omitted-key corollary of rule 4). Beside it, a READER for exactly the grammar `render` emits —
   not `Json.parse`, which theorem 4 already models, but the left inverse whose existence IS the
   canonical form's converse.

   WHY THE PHASE EXISTS. §2 promises the twelve rules make the encoding DETERMINISTIC: structurally
   equal values render byte-for-byte identically. Every digest in the estate needs the property
   nobody states — that equal BYTES imply equal VALUES. The op-stream chain id, the DAG content id,
   the teleport digest (§17.3) and cross-host attestation all hash the canonical rendering and read
   hash equality as value equality, and Phase 145 leaves `op_codec_injective` a parameter precisely
   because a codec built on `Canon.render` inherits injectivity only if `Canon.render` has it.

   WHAT IS PROVED, AND THE FINDING THAT SHAPES IT.

   THE SHARD ASKED FOR `render_injective` UNCONDITIONALLY — `render a == render b ==> a == b` — AND
   THAT STATEMENT IS FALSE OF THE SHIPPED ENCODER, FOUR WAYS. Each is proved here as a refutation
   rather than described:

     1. `render_aliases_nan` / `_pos_inf` / `_neg_inf` — a non-finite float renders as the QUOTED
        STRING `"NaN"` / `"Infinity"` / `"-Infinity"` (rule 5, Wire.fs `canonicalFloat`), which is
        byte-for-byte what the STRING of those characters renders as. At Phase 149 `Canon.render`
        had no guarded counterpart, where `Json.render` has `Json.tryRender` beside it — see the
        finding at the foot of this header, and section 13 for the guard Phase 165 added.
     2. `render_aliases_integral_float` — a finite float whose layout carries no `.` and no `E`
        renders exactly as the integer of the same token. This is the wire's documented numeric
        normalisation (`JVal`'s own type doc: "render (JFloat 2.0) emits 2"), not a defect.
     3. `render_aliases_negative_zero` — rule 5 collapses `-0` to `0`, so the two zeroes are one
        byte sequence.
     4. `render_aliases_member_order` — rule 2 SORTS object keys, so two `JObj`s differing only in
        author order are one byte sequence. This one is not a loss at all: it is the whole point of
        a canonical form, and it is why the theorem below is stated up to member order.

   SO THE THEOREM IS THE CANONICAL-FORM STATEMENT, WHICH IS WHAT THE DIGEST CONSUMERS ACTUALLY
   NEED, AND IT IS AN IFF:

     - `render_total` — `render` reaches a rendering on every value (carried by its type, since F*
       admits a definition only after showing it total), and WHICH constructor produced a rendering
       is determined by its first character (`render_classifies`), so the encoding's shape
       dispatch is exhaustive rather than merely non-empty.
     - `read_render_roundtrip` — the reader is a LEFT INVERSE of `render` on the canonical subset,
       for every trailing context: `read (render v ++ rest) == Ok (normalise v, rest)`. This is the
       engine; everything below is a corollary of it.
     - `render_injective_up_to_key_order` — equal bytes imply equal normal forms. The converse §2
       never stated, and the one the digests rest on.
     - `render_deterministic` — equal normal forms imply equal bytes: §2's own promise, proved.
     - `canonical_form_iff` — the two together. `render a == render b <==> normalise a == normalise
       b`, which is what "canonical form" means.
     - `render_injective_on_sorted` — the literal `render a == render b ==> a == b`, on values
       whose object members are already in canonical key order. That is the form a consumer holding
       a re-decoded document has, since the reader returns members in that order.
     - `no_null_ever` — Phase 153, section 12: `render` never emits the token `null`, at any depth,
       for ANY value (not only the canonical subset). Stated lexically, because the string "null"
       legitimately renders those four characters inside a literal.
     - `tryrender_is_render_on_finite` / `tryrender_refuses_exactly_aliasing` — Phase 165, section
       13: the guarded `Canon.tryRender` IS the renderer wherever every float is finite, and
       refuses exactly where one is not — naming a float whose rendering refutation 1 says is a
       string's. The integer-shaped float of refutation 2 is proved NOT refused.

   THE FOUR RULE LEMMAS ARE THE PROOF'S OWN PARTS, not decoration:
     - rule 2 — `sort_is_a_canonical_choice`: under the comparator's total-order premises, sorting
       is idempotent and depends only on the members, so the key order carries no information.
     - rule 6 — `escape_injective`, via `read_str_inverts_escape`: the escaped body is uniquely
       decodable in any trailing context, which is the property injectivity needs and which plain
       injectivity of `escape` would not give.
     - rule 5 — `canonical_float_injective`: on the canonical subset the float layout is injective,
       from the round-trip premise the layout is DEFINED by ("the shortest digit sequence that
       round-trips").
     - rule 4 — `absence_is_structural`: an omitted key never aliases a present one; two objects
       whose renderings agree carry the same key set.

   WHAT IS NOT MODELLED, AND WHY — the theorem's boundary.

     - THE NUMERIC PAYLOADS ARE OPAQUE, as they are in theorem 1 and theorem 4. `jval` is
       parametric in the int and float carriers and the layout is a record of parameters
       (`int_str`, `float_str`, `fclass`, `is_zero`), because the F# extraction carries no floats
       and F*'s `int` is unbounded where .NET's `JInt` is Int32 (README, finding 2). What the model
       decides is the encoder's own logic — which clause fires, what it emits around the numeral —
       and the numeral is .NET's to compute. `Double.ToString("R")`'s round-trip property enters as
       a NAMED premise (`float_round_trips`), which is what the layout is defined by rather than an
       incidental fact about it.

     - THE COMPARATOR IS A PARAMETER, and the rule-2 premises say it is a total order. Nothing here
       proves that `System.String.CompareOrdinal` IS one, or that it is UTF-16 code-unit order —
       that is a fact about .NET, and the fact the corpus's `custom-nonascii-keys` fixture pins.
       What is proved is that a canonical form follows FROM a total order, which is the half a
       model can settle.

     - A CHARACTER IS A CONSTRUCTOR, exactly as in theorem 4, and for the same reason. `CPlain`
       carries the character verbatim, so two different ordinary characters are never identified;
       what the model needs to distinguish are the three classes rule 6 names (`"`, `\`, the
       controls) and the characters a numeric or literal token can carry. The bridge's faithfulness
       — that `CPlain` never carries a character one of the other constructors already denotes — is
       written down as `bridged` and proved to be what makes the spelling injective
       (`denot_injective_on_bridged`), rather than left in prose.

     - `Canon.renderOrdered` IS NOT MODELLED. It is the declared-key-order leg, where the ENCODER
       is the order authority and no sort runs; every theorem here is about `render` specifically.
       Its canonicity rests on a different argument (the IDL's `WireShape.KeyOrder`), and asserting
       this one carries to it would be exactly the over-reading this header exists to prevent.

   A FINDING ABOUT THE SHIPPED CODE, recorded because refutation 1 is the only one of the four that
   is not a documented design choice: `Json.render` has a guarded companion, `Json.tryRender`, which
   names a non-finite float as a typed `Error` "instead of producing un-parseable wire". `Canon` has
   no such companion. A non-finite float reaching `Canon.render` is not un-parseable — it is worse
   than that, because it silently becomes a STRING, and a digest over it collides with the digest
   over the string a reader would decode it back to. Nothing in `Canon` refuses it. That is an
   observation about an unguarded path and not a counterexample to any shipped claim, so it is
   recorded here and in the README's ladder rather than repaired in this phase: a repair is a
   refusal-class change to a shipped encoder and belongs to a phase chartered for it.

   THAT PHASE WAS 165, and the repair is section 13: `Canon.tryRender`, a guarded entry point
   BESIDE `render`, whose bytes did not move. Everything above stays true of `render` itself — a
   value that aliases under it still aliases under it — which is why the four refutations stand
   unedited.

   HOW TO READ IT. Every definition names its F# counterpart in the comment above it. Sections 0-3
   are the model, 4 the reader, 5-6 the theorems, 7 the refutations. (Those are the header's own
   groupings; the file's numbered sections run 0-13, with the refutations at 11, Phase 153's
   no-null lemma at 12 and Phase 165's guard at 13.) Helpers are defined here rather
   than taken from `FStar.List.Tot` so that the extracted oracle depends on `Prims` alone.

   Apache-2.0, like everything beside it.
*)
module WireCanon

(* The reader is a mutual group whose termination rides refinements in the return types, which is
   the shape theorem 4's parser has and the shape this directory's cost note names as expensive
   under the default SMT context. Scoped here rather than in `check.ps1`'s flags, for the reason
   `TreeOps.fst` gives at the same line: pruning changes which facts a query can see, so a module
   that has not been checked under it must not be switched to it as a side effect of another
   module's cost. *)
#set-options "--ext context_pruning"

open Limits

(* ======================================================================================
   0. Lists and the outcome, self-contained so the extraction needs only `Prims`
      (README, finding 2).
   ====================================================================================== *)

type outcome (a: Type) =
  | Ok    : v:a -> outcome a
  | Error : msg:string -> outcome a

(* PROOF-ONLY — erased at extraction, and the measure every refinement below is written in. *)
[@@ noextract_to "FSharp"]
let rec llen (#a: Type) (l: list a) : Tot nat =
  match l with
  | [] -> 0
  | _ :: t -> 1 + llen t

(* F#: `@` / `List.append`, and `String.concat` at the character level — the canonical renderer is
   a concatenation and nothing else. *)
let rec app (#a: Type) (l1 l2: list a) : Tot (list a) (decreases l1) =
  match l1 with
  | [] -> l2
  | x :: t -> x :: app t l2

let rec mem (#a: eqtype) (x: a) (l: list a) : Tot bool =
  match l with
  | [] -> false
  | y :: t -> x = y || mem x t

[@@ noextract_to "FSharp"]
let rec app_assoc (#a: Type) (l1 l2 l3: list a)
  : Lemma (ensures app (app l1 l2) l3 == app l1 (app l2 l3)) (decreases l1) =
  match l1 with
  | [] -> ()
  | _ :: t -> app_assoc t l2 l3

[@@ noextract_to "FSharp"]
let rec app_nil (#a: Type) (l: list a) : Lemma (ensures app l [] == l) (decreases l) =
  match l with
  | [] -> ()
  | _ :: t -> app_nil t

[@@ noextract_to "FSharp"]
let rec app_len (#a: Type) (l1 l2: list a)
  : Lemma (ensures llen (app l1 l2) == llen l1 + llen l2) (decreases l1) =
  match l1 with
  | [] -> ()
  | _ :: t -> app_len t l2

(* ======================================================================================
   1. The alphabet (F#: `char`).

      One constructor per character the CANONICAL ENCODER distinguishes — the three classes rule 6
      names, the punctuation the grammar emits, the sixteen characters `0`-`9` / `a`-`f` (every
      decimal digit a numeric token can carry, and every lower-case hex digit the `\u00xx` escape
      emits), and `COther`-style `CPlain`, which carries the character verbatim. See the header's
      note on the bridge.
   ====================================================================================== *)

type hexd =
  | HD0 | HD1 | HD2 | HD3 | HD4 | HD5 | HD6 | HD7
  | HD8 | HD9 | HDa | HDb | HDc | HDd | HDe | HDf

let hexd_str (d: hexd) : Tot string =
  match d with
  | HD0 -> "0" | HD1 -> "1" | HD2 -> "2" | HD3 -> "3"
  | HD4 -> "4" | HD5 -> "5" | HD6 -> "6" | HD7 -> "7"
  | HD8 -> "8" | HD9 -> "9"
  | HDa -> "a" | HDb -> "b" | HDc -> "c" | HDd -> "d" | HDe -> "e" | HDf -> "f"

(* The ten that are decimal digits — the only ones a numeric token can carry. *)
let is_dec (d: hexd) : Tot bool =
  match d with
  | HDa | HDb | HDc | HDd | HDe | HDf -> false
  | _ -> true

type ch =
  (* rule 6's two escaped punctuation characters *)
  | CQuote | CBackslash
  (* the structural punctuation the grammar emits *)
  | CLBrace | CRBrace | CLBrack | CRBrack | CColon | CComma
  (* rule 5's numeric punctuation: `-`, `+`, `.` and the uppercase exponent marker `E` *)
  | CMinus | CPlus | CDot | CUpE
  (* `0`-`9` and `a`-`f` *)
  | CHexCh : d:hexd -> ch
  (* `u`, the only letter the escape emits *)
  | CLu
  (* U+0000-U+001F, as its two hex nibbles: the high one is `0` or `1`, which is what `hi` says *)
  | CCtrl : hi:bool -> lo:hexd -> ch
  (* every other character, carried verbatim *)
  | CPlain : c:string -> ch

(* The one-character spelling of each constructor. F#: `string c`. *)
let denot (c: ch) : Tot string =
  match c with
  | CQuote -> "\"" | CBackslash -> "\\"
  | CLBrace -> "{" | CRBrace -> "}" | CLBrack -> "[" | CRBrack -> "]"
  | CColon -> ":" | CComma -> ","
  | CMinus -> "-" | CPlus -> "+" | CDot -> "." | CUpE -> "E"
  | CHexCh d -> hexd_str d
  | CLu -> "u"
  (* A control character has no printable spelling; the model names it by its code point, which is
     what the escape emits anyway and what the host's bridge reads back. *)
  | CCtrl hi lo -> (if hi then "\\u0001" else "\\u0000") ^ hexd_str lo
  | CPlain s -> s

(* The bridge's faithfulness condition, written down rather than left in prose: a `CPlain` must not
   carry a character one of the other constructors already denotes. The host's `toCh` is total and
   classifies before it falls through, so this holds of every bridged string by construction — but
   stating it is what makes the level-3 assumption checkable instead of merely asserted. *)
let reserved_spellings : list string =
  [ "\""; "\\"; "{"; "}"; "["; "]"; ":"; ","; "-"; "+"; "."; "E"; "u";
    "0"; "1"; "2"; "3"; "4"; "5"; "6"; "7"; "8"; "9";
    "a"; "b"; "c"; "d"; "e"; "f" ]

let bridged (c: ch) : Tot bool =
  match c with
  | CPlain s -> not (mem s reserved_spellings)
  | _ -> true

let rec bridged_all (s: list ch) : Tot bool =
  match s with
  | [] -> true
  | c :: t -> bridged c && bridged_all t

(* ======================================================================================
   2. The value model (F#: `JVal` in Wire.fs).

      Parametric in the two numeric carriers, as theorem 1's is and for the same reason; strings
      and keys are character lists, because rule 6 and rule 2 are both statements about characters.
   ====================================================================================== *)

type jval (num flt: eqtype) =
  | JStr   : s:list ch -> jval num flt
  | JInt   : i:num -> jval num flt
  | JBool  : b:bool -> jval num flt
  | JFloat : f:flt -> jval num flt
  | JArr   : items:list (jval num flt) -> jval num flt
  | JObj   : fields:list (list ch & jval num flt) -> jval num flt

(* PROOF-ONLY — the structural measure the mutual renderer and the normal form recurse on. *)
[@@ noextract_to "FSharp"]
let rec jsize (#num #flt: eqtype) (v: jval num flt) : Tot pos =
  match v with
  | JArr xs -> 1 + jsizes xs
  | JObj fs -> 1 + fsize fs
  | _ -> 1

and jsizes (#num #flt: eqtype) (xs: list (jval num flt)) : Tot nat =
  match xs with
  | [] -> 0
  | x :: t -> jsize x + jsizes t

and fsize (#num #flt: eqtype) (fs: list (list ch & jval num flt)) : Tot nat =
  match fs with
  | [] -> 0
  | (_, v) :: t -> jsize v + fsize t

[@@ noextract_to "FSharp"]
let jsize_at_least_one (#num #flt: eqtype) (v: jval num flt)
  : Lemma (ensures jsize v >= 1) [SMTPat (jsize v)] =
  match v with
  | JStr _ -> () | JInt _ -> () | JBool _ -> ()
  | JFloat _ -> () | JArr _ -> () | JObj _ -> ()

[@@ noextract_to "FSharp"]
let jsizes_cons (#num #flt: eqtype) (x: jval num flt) (t: list (jval num flt))
  : Lemma (ensures jsizes (x :: t) == jsize x + jsizes t) [SMTPat (jsizes (x :: t))] = ()

[@@ noextract_to "FSharp"]
let fsize_cons (#num #flt: eqtype) (k: list ch) (v: jval num flt)
               (t: list (list ch & jval num flt))
  : Lemma (ensures fsize ((k, v) :: t) == jsize v + fsize t) [SMTPat (fsize ((k, v) :: t))] = ()

(* The SYNTACTIC nesting depth — every `{` and `[` counts. This is the quantity WIRE_FORMAT §21.1
   bounds at `Limits.max_json_depth`, and it appears only in the §21 restatement at the foot of
   section 6. PROOF-ONLY: the model's own reader has no depth cap, and saying otherwise by carrying
   a counter here would be modelling a guard `Canon` does not have. *)
[@@ noextract_to "FSharp"]
let rec jdepth (#num #flt: eqtype) (v: jval num flt) : Tot nat =
  match v with
  | JArr xs -> 1 + jdepths xs
  | JObj fs -> 1 + fdepth fs
  | _ -> 0

and jdepths (#num #flt: eqtype) (xs: list (jval num flt)) : Tot nat =
  match xs with
  | [] -> 0
  | x :: t -> (let d = jdepth x in let r = jdepths t in if d > r then d else r)

and fdepth (#num #flt: eqtype) (fs: list (list ch & jval num flt)) : Tot nat =
  match fs with
  | [] -> 0
  | (_, v) :: t -> (let d = jdepth v in let r = fdepth t in if d > r then d else r)

(* ======================================================================================
   3. The layout and the comparator, as parameters (F#: `FloatLayout`, `canonicalFloat`'s
      three non-finite arms, and `System.String.CompareOrdinal`).
   ====================================================================================== *)

(* F#: the three-way test `canonicalFloat` opens with, plus "none of the above". *)
type fcls =
  | FNaN | FPosInf | FNegInf | FFinite

noeq
type wire (num flt: eqtype) = {
  (* F#: `string i` — .NET's Int32 decimal layout. Rule 5's integer arm. *)
  int_str   : num -> list ch;
  (* F#: `FloatLayout.finite` — `Double.ToString("R", InvariantCulture)` on .NET and the
     byte-identical JS re-layout under Fable. Rule 5's float arm. *)
  float_str : flt -> list ch;
  (* F#: `Double.IsNaN` / `IsPositiveInfinity` / `IsNegativeInfinity`, in that order. *)
  fclass    : flt -> fcls;
  (* F#: `f = 0.0`, which is TRUE of `-0.0` — that is exactly why the collapse below is needed. *)
  is_zero   : flt -> bool;
  (* F#: the literal `0.0` the collapse substitutes. *)
  pos_zero  : flt;
  (* F#: `System.String.CompareOrdinal(a, b) <= 0`. Rule 2's comparator. *)
  key_le    : list ch -> list ch -> bool;
  (* The numeral READ-BACK, which the model does not compute: given a numeric token, the value the
     wire denotes. A parameter for the reason `float_read` is one in theorem 4 — the numeral is
     .NET's to compute, and the differential compares it there. *)
  tok_read  : list ch -> outcome (jval num flt);
}

let key_lt (#num #flt: eqtype) (w: wire num flt) (a b: list ch) : Tot bool =
  w.key_le a b && not (w.key_le b a)

(* ---- rule 2's premises: the comparator is a TOTAL ORDER on keys ---- *)

let key_order_reflexive (#num #flt: eqtype) (w: wire num flt) : prop =
  forall (a: list ch). w.key_le a a

let key_order_total (#num #flt: eqtype) (w: wire num flt) : prop =
  forall (a b: list ch). w.key_le a b \/ w.key_le b a

let key_order_transitive (#num #flt: eqtype) (w: wire num flt) : prop =
  forall (a b c: list ch). (w.key_le a b /\ w.key_le b c) ==> w.key_le a c

let key_order_antisymmetric (#num #flt: eqtype) (w: wire num flt) : prop =
  forall (a b: list ch). (w.key_le a b /\ w.key_le b a) ==> a == b

let key_order_ok (#num #flt: eqtype) (w: wire num flt) : prop =
  key_order_reflexive w /\ key_order_total w /\ key_order_transitive w /\ key_order_antisymmetric w

(* ======================================================================================
   4. The encoder (F#: `Canon.escape`, `Canon.canonicalFloat`, `Canon.render`).
   ====================================================================================== *)

(* F#: `Canon.escape`'s per-character match. Rule 6, and ONLY rule 6: `"` and `\` and the controls,
   lower-case four-digit hex, no `\n`/`\r`/`\t` shortcuts, `/` untouched. Note this is NOT
   `Json.escape`, which carries the short escapes and is a different function. *)
let esc_ch (c: ch) : Tot (list ch) =
  match c with
  | CQuote      -> [CBackslash; CQuote]
  | CBackslash  -> [CBackslash; CBackslash]
  | CCtrl hi lo -> [CBackslash; CLu; CHexCh HD0; CHexCh HD0;
                    CHexCh (if hi then HD1 else HD0); CHexCh lo]
  | other       -> [other]

let rec escape (s: list ch) : Tot (list ch) (decreases s) =
  match s with
  | [] -> []
  | c :: t -> app (esc_ch c) (escape t)

(* F#: `"\"" + escape s + "\""`. *)
let quoted (s: list ch) : Tot (list ch) = CQuote :: app (escape s) [CQuote]

(* F#: the three fixed tokens `canonicalFloat` emits for a non-finite float, and the two boolean
   literals. Spelled out character by character, because whether they collide with a rendered
   STRING is exactly what refutation 1 is about. *)
let nan_chars : list ch = [CPlain "N"; CHexCh HDa; CPlain "N"]
let inf_chars : list ch =
  [CPlain "I"; CPlain "n"; CHexCh HDf; CPlain "i"; CPlain "n"; CPlain "i"; CPlain "t"; CPlain "y"]
let neg_inf_chars : list ch = CMinus :: inf_chars
let true_chars : list ch = [CPlain "t"; CPlain "r"; CLu; CHexCh HDe]
let false_chars : list ch = [CHexCh HDf; CHexCh HDa; CPlain "l"; CPlain "s"; CHexCh HDe]

(* F#: `Canon.canonicalFloat`, clause for clause — the three quoted tokens, then the `-0` collapse
   (the WIRE rule, applied BEFORE the layout, exactly as the source comment says), then the layout. *)
let canonical_float (#num #flt: eqtype) (w: wire num flt) (f: flt) : Tot (list ch) =
  match w.fclass f with
  | FNaN    -> quoted nan_chars
  | FPosInf -> quoted inf_chars
  | FNegInf -> quoted neg_inf_chars
  | FFinite -> w.float_str (if w.is_zero f then w.pos_zero else f)

(* F#: `List.sortWith (fun (a,_) (b,_) -> String.CompareOrdinal(a,b))`. F#'s sort is STABLE, so an
   equal key keeps author order; insertion AFTER the equals is what reproduces that. On the
   canonical subset keys are distinct and stability is unobservable, but the extracted oracle runs
   beside production byte for byte, so it is reproduced rather than approximated. *)
let rec insert_kv (#num #flt: eqtype) (w: wire num flt)
                  (kv: (list ch & jval num flt))
                  (l: list (list ch & jval num flt))
  : Tot (r: list (list ch & jval num flt) { fsize r == jsize (snd kv) + fsize l })
        (decreases l) =
  match l with
  | [] -> [kv]
  | hd :: t -> if w.key_le (fst kv) (fst hd) then kv :: l else hd :: insert_kv w kv t

let rec sort_kvs (#num #flt: eqtype) (w: wire num flt) (fs: list (list ch & jval num flt))
  : Tot (r: list (list ch & jval num flt) { fsize r == fsize fs }) (decreases fs) =
  match fs with
  | [] -> []
  | kv :: t -> insert_kv w kv (sort_kvs w t)

(* F#: `Canon.render`. Rules 2 (the sort), 3 (arrays keep source order), 5 (the two numeric
   layouts), 6 (the escape) and 7 (`true` / `false`); rule 4 is structural — a key that is not in
   the list is simply not emitted, and there is no clause for it, which is the point. *)
let rec render (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Tot (list ch) (decreases %[(jsize v <: nat); 0]) =
  match v with
  | JStr s   -> quoted s
  | JInt i   -> w.int_str i
  | JBool b  -> if b then true_chars else false_chars
  | JFloat f -> canonical_float w f
  | JArr xs  -> CLBrack :: render_items w xs
  | JObj fs  -> CLBrace :: render_kvs w (sort_kvs w fs)

and render_items (#num #flt: eqtype) (w: wire num flt) (xs: list (jval num flt))
  : Tot (list ch) (decreases %[(jsizes xs <: nat); 1]) =
  match xs with
  | [] -> [CRBrack]
  | [x] -> app (render w x) [CRBrack]
  | x :: t -> app (render w x) (CComma :: render_items w t)

and render_kvs (#num #flt: eqtype) (w: wire num flt) (fs: list (list ch & jval num flt))
  : Tot (list ch) (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> [CRBrace]
  | [(k, v)] -> app (quoted k) (CColon :: app (render w v) [CRBrace])
  | (k, v) :: t -> app (quoted k) (CColon :: app (render w v) (CComma :: render_kvs w t))

(* ======================================================================================
   5. The canonical subset, and the normal form.

      `canonical` is rule 5's OWN slot rule read as a predicate on a value: a float is canonical
      exactly when it is finite and its token carries a `.` or an `E`, which is the discriminator
      rule 5's last paragraph uses to decide whether a token "keeps integer identity". Outside it
      the collisions of section 7 are not defects but the documented normalisation.

      `normalise` is the value with every object's members in canonical key order — the value a
      reader gets back, and therefore the right-hand side of every statement below.
   ====================================================================================== *)

let has_marker (t: list ch) : Tot bool = mem CDot t || mem CUpE t

let float_canonical (#num #flt: eqtype) (w: wire num flt) (f: flt) : Tot bool =
  w.fclass f = FFinite && has_marker (w.float_str (if w.is_zero f then w.pos_zero else f))

let rec canonical (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Tot bool (decreases %[(jsize v <: nat); 0]) =
  match v with
  | JFloat f -> float_canonical w f
  | JArr xs -> canonical_items w xs
  | JObj fs -> canonical_kvs w fs
  | _ -> true

and canonical_items (#num #flt: eqtype) (w: wire num flt) (xs: list (jval num flt))
  : Tot bool (decreases %[(jsizes xs <: nat); 1]) =
  match xs with
  | [] -> true
  | x :: t -> canonical w x && canonical_items w t

and canonical_kvs (#num #flt: eqtype) (w: wire num flt) (fs: list (list ch & jval num flt))
  : Tot bool (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> true
  | (_, v) :: t -> canonical w v && canonical_kvs w t

let rec normalise (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Tot (jval num flt) (decreases %[(jsize v <: nat); 0]) =
  match v with
  | JArr xs -> JArr (normalise_items w xs)
  | JObj fs -> JObj (normalise_kvs w (sort_kvs w fs))
  | other -> other

and normalise_items (#num #flt: eqtype) (w: wire num flt) (xs: list (jval num flt))
  : Tot (list (jval num flt)) (decreases %[(jsizes xs <: nat); 1]) =
  match xs with
  | [] -> []
  | x :: t -> normalise w x :: normalise_items w t

and normalise_kvs (#num #flt: eqtype) (w: wire num flt) (fs: list (list ch & jval num flt))
  : Tot (list (list ch & jval num flt)) (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> []
  | (k, v) :: t -> (k, normalise w v) :: normalise_kvs w t

(* ======================================================================================
   6. The reader — a LEFT INVERSE for exactly the grammar `render` emits.

      This is deliberately NOT `Json.parse`: theorem 4 models that parser, with its depth
      counter, its twelve error classes and its two numeric guards, and re-modelling it here
      would prove the same thing twice at several times the prover cost. What injectivity needs
      is far smaller — a reader that recovers a value from a rendering, in any trailing context.
      Its EXISTENCE is the theorem; its behaviour on input `render` never emits is not claimed.
   ====================================================================================== *)

(* The characters rule 5's two layouts can emit: the decimal digits, and `-` `+` `.` `E`. A
   numeric token is a non-empty run of these, which is what makes it self-delimiting against a
   following `,` `]` `}` or end of input. *)
let num_ch (c: ch) : Tot bool =
  (CHexCh? c && is_dec (CHexCh?.d c)) || c = CMinus || c = CPlus || c = CDot || c = CUpE

let rec all_num (t: list ch) : Tot bool =
  match t with
  | [] -> true
  | c :: r -> num_ch c && all_num r

(* The separator condition a numeric token needs to be recoverable: what follows must not be
   another numeric character. Every position the grammar puts a number in satisfies it — a `,`, a
   `]`, a `}`, or the end of the document — and the top-level statement takes `rest = []`. It is
   the same self-delimitation `Chain.fst`'s splice argument turns on, at the token level. *)
let sep_ok (rest: list ch) : Tot bool =
  match rest with
  | [] -> true
  | c :: _ -> not (num_ch c)

let rec span_num (input: list ch)
  : Tot (r: (list ch & list ch) { app (fst r) (snd r) == input /\ all_num (fst r) /\ sep_ok (snd r) })
        (decreases input) =
  match input with
  | [] -> ([], [])
  | c :: t -> if num_ch c then (let tok, rest = span_num t in (c :: tok, rest)) else ([], input)

[@@ noextract_to "FSharp"]
let rec span_num_app (tok rest: list ch)
  : Lemma (requires all_num tok /\ sep_ok rest)
          (ensures span_num (app tok rest) == (tok, rest))
          (decreases tok) =
  match tok with
  | [] -> ()
  | _ :: t -> span_num_app t rest

(* The reader's own literal matcher, for rule 7's two tokens. *)
let rec strip (pfx input: list ch)
  : Tot (r: outcome (list ch) { Ok? r ==> llen (Ok?.v r) + llen pfx == llen input })
        (decreases pfx) =
  match pfx with
  | [] -> Ok input
  | a :: pt ->
      (match input with
       | [] -> Error "input ended inside a literal"
       | b :: it -> if a = b then strip pt it else Error "not the literal this position expects")

[@@ noextract_to "FSharp"]
let rec strip_app (pfx rest: list ch)
  : Lemma (ensures strip pfx (app pfx rest) == Ok rest) (decreases pfx) =
  match pfx with
  | [] -> ()
  | _ :: t -> strip_app t rest

(* The high nibble of a control character's escape is `0` or `1` and nothing else — U+0000-U+001F
   is the range rule 6 escapes. *)
let ctrl_hi (d: hexd) : Tot (outcome bool) =
  match d with
  | HD0 -> Ok false
  | HD1 -> Ok true
  | _ -> Error "a u00xx escape whose high nibble is neither 0 nor 1 is not one this encoder emits"

(* The inverse of `escape`, up to the closing quote. Rule 6's escape set and no other. *)
let rec read_str (input: list ch)
  : Tot (r: outcome (list ch & list ch) { Ok? r ==> llen (snd (Ok?.v r)) < llen input })
        (decreases (llen input)) =
  match input with
  | [] -> Error "unterminated string"
  | CQuote :: t -> Ok ([], t)
  | CBackslash :: CQuote :: t ->
      (match read_str t with
       | Ok (s, rest) -> Ok (CQuote :: s, rest)
       | Error m -> Error m)
  | CBackslash :: CBackslash :: t ->
      (match read_str t with
       | Ok (s, rest) -> Ok (CBackslash :: s, rest)
       | Error m -> Error m)
  | CBackslash :: CLu :: CHexCh HD0 :: CHexCh HD0 :: CHexCh h :: CHexCh l :: t ->
      (match ctrl_hi h with
       | Error m -> Error m
       | Ok hi ->
         (match read_str t with
          | Ok (s, rest) -> Ok (CCtrl hi l :: s, rest)
          | Error m -> Error m))
  | CBackslash :: _ -> Error "not an escape this encoder emits"
  | c :: t ->
      (match read_str t with
       | Ok (s, rest) -> Ok (c :: s, rest)
       | Error m -> Error m)

(* RULE 6, as the property injectivity actually needs. Plain injectivity of `escape` is not
   enough: an escaped body sits inside a document, so what must hold is that it is UNIQUELY
   DECODABLE IN ANY TRAILING CONTEXT — the closing quote is found in the same place whatever
   follows it. Unconditional: every `ch` the model can hold escapes and reads back. *)
[@@ noextract_to "FSharp"]
let rec read_str_inverts_escape (s rest: list ch)
  : Lemma (ensures read_str (app (escape s) (CQuote :: rest)) == Ok (s, rest)) (decreases s) =
  match s with
  | [] -> ()
  | c :: t ->
      read_str_inverts_escape t rest;
      app_assoc (esc_ch c) (escape t) (CQuote :: rest)

(* ---- the value reader ---- *)

let rec read (#num #flt: eqtype) (w: wire num flt) (input: list ch)
  : Tot (r: outcome (jval num flt & list ch) { Ok? r ==> llen (snd (Ok?.v r)) < llen input })
        (decreases %[(llen input <: nat); 0]) =
  match input with
  | [] -> Error "no value here"
  | CQuote :: t ->
      (match read_str t with
       | Ok (s, rest) -> Ok (JStr s, rest)
       | Error m -> Error m)
  | CLBrack :: t ->
      (match read_items w t with
       | Ok (xs, rest) -> Ok (JArr xs, rest)
       | Error m -> Error m)
  | CLBrace :: t ->
      (match read_kvs w t with
       | Ok (fs, rest) -> Ok (JObj fs, rest)
       | Error m -> Error m)
  (* The only canonical value whose first character is an ordinary letter is `true` (rule 7): a
     string is quoted, a number starts with a digit or `-`, and `false` starts with `f`. *)
  | CPlain _ :: _ ->
      (match strip true_chars input with
       | Ok rest -> Ok (JBool true, rest)
       | Error m -> Error m)
  | CHexCh HDf :: _ ->
      (match strip false_chars input with
       | Ok rest -> Ok (JBool false, rest)
       | Error m -> Error m)
  | _ ->
      let tok, rest = span_num input in
      app_len tok rest;
      (match tok with
       | [] -> Error "not the first character of any canonical value"
       | _ ->
         (match w.tok_read tok with
          | Ok v -> Ok (v, rest)
          | Error m -> Error m))

(* Rule 3 — a list is an ORDERED structure, so the reader keeps the order it finds. *)
and read_items (#num #flt: eqtype) (w: wire num flt) (input: list ch)
  : Tot (r: outcome (list (jval num flt) & list ch) { Ok? r ==> llen (snd (Ok?.v r)) < llen input })
        (decreases %[(llen input <: nat); 1]) =
  match input with
  | CRBrack :: t -> Ok ([], t)
  | _ ->
    (match read w input with
     | Error m -> Error m
     | Ok (v, r1) ->
       (match r1 with
        | CComma :: r2 ->
            (match read_items w r2 with
             | Ok (vs, r3) -> Ok (v :: vs, r3)
             | Error m -> Error m)
        | CRBrack :: r2 -> Ok ([v], r2)
        | _ -> Error "expected a comma or a closing bracket"))

(* Rule 2 in the other direction: the reader returns members in the order the bytes carry them,
   which for a canonical document is key order. RULE 4 is here too, and is structural — a key
   that is not in the bytes produces no member, and there is no clause that could invent one. *)
and read_kvs (#num #flt: eqtype) (w: wire num flt) (input: list ch)
  : Tot (r: outcome (list (list ch & jval num flt) & list ch)
             { Ok? r ==> llen (snd (Ok?.v r)) < llen input })
        (decreases %[(llen input <: nat); 1]) =
  match input with
  | CRBrace :: t -> Ok ([], t)
  | CQuote :: t ->
    (match read_str t with
     | Error m -> Error m
     | Ok (k, r1) ->
       (match r1 with
        | CColon :: r2 ->
          (match read w r2 with
           | Error m -> Error m
           | Ok (v, r3) ->
             (match r3 with
              | CComma :: r4 ->
                  (match read_kvs w r4 with
                   | Ok (kvs, r5) -> Ok ((k, v) :: kvs, r5)
                   | Error m -> Error m)
              | CRBrace :: r4 -> Ok ([(k, v)], r4)
              | _ -> Error "expected a comma or a closing brace"))
        | _ -> Error "expected a colon after a member key"))
  | _ -> Error "expected a member key or a closing brace"

(* ======================================================================================
   7. The premises, and the theorems.

      There is exactly ONE premise about the numerals, and it is the property `Double.
      ToString("R")` is DEFINED by rather than an incidental fact about it: WIRE_FORMAT rule 5
      pins the float layout as "the shortest digit sequence that ROUND-TRIPS (parse(toString(x))
      == x)", and the integer layout as plain decimal with no leading zeroes. `tok_read_ok` is
      those two sentences, and nothing else. Rule 5's injectivity lemma below is derived from it
      rather than assumed beside it — which is the point: two premises where one suffices is two
      chances to assume something false.
   ====================================================================================== *)

let numeric_token (t: list ch) : Tot bool = Cons? t && all_num t

let tok_read_ok (#num #flt: eqtype) (w: wire num flt) : prop =
  (forall (i: num). numeric_token (w.int_str i) /\ w.tok_read (w.int_str i) == Ok (JInt i)) /\
  (forall (f: flt). float_canonical w f ==>
       numeric_token (canonical_float w f) /\ w.tok_read (canonical_float w f) == Ok (JFloat f))

(* ---- RULE 5, derived: on the canonical subset the two layouts are injective, and they do not
        collide with each other. Both fall straight out of `tok_read` being a FUNCTION: two values
        with one token would have to read back as two different values from one input. ---- *)

[@@ noextract_to "FSharp"]
let int_layout_injective (#num #flt: eqtype) (w: wire num flt) (i j: num)
  : Lemma (requires tok_read_ok w /\ w.int_str i == w.int_str j) (ensures i == j) = ()

[@@ noextract_to "FSharp"]
let canonical_float_injective (#num #flt: eqtype) (w: wire num flt) (f g: flt)
  : Lemma (requires tok_read_ok w /\ float_canonical w f /\ float_canonical w g /\
                    canonical_float w f == canonical_float w g)
          (ensures f == g) = ()

[@@ noextract_to "FSharp"]
let int_and_float_layouts_disjoint (#num #flt: eqtype) (w: wire num flt) (i: num) (f: flt)
  : Lemma (requires tok_read_ok w /\ float_canonical w f)
          (ensures ~(w.int_str i == canonical_float w f)) = ()

(* ---- RULE 2, derived from the comparator's total-order premises: sorting is a CANONICAL
        choice — the result is ordered, and re-sorting it changes nothing, so the author's key
        order carries no information into the bytes. ---- *)

let rec sorted (#num #flt: eqtype) (w: wire num flt) (fs: list (list ch & jval num flt))
  : Tot bool (decreases fs) =
  match fs with
  | [] -> true
  | (k1, _) :: t ->
      (match t with
       | [] -> true
       | (k2, _) :: _ -> w.key_le k1 k2 && sorted w t)

[@@ noextract_to "FSharp"]
let insert_head (#num #flt: eqtype) (w: wire num flt)
                (kv: (list ch & jval num flt)) (l: list (list ch & jval num flt))
  : Lemma (ensures Cons? (insert_kv w kv l) /\
                   (Cons?.hd (insert_kv w kv l) == kv \/
                    (Cons? l /\ Cons?.hd (insert_kv w kv l) == Cons?.hd l))) = ()

[@@ noextract_to "FSharp"]
let rec insert_preserves_sorted (#num #flt: eqtype) (w: wire num flt)
                                (kv: (list ch & jval num flt))
                                (l: list (list ch & jval num flt))
  : Lemma (requires key_order_ok w /\ sorted w l)
          (ensures sorted w (insert_kv w kv l))
          (decreases l) =
  match l with
  | [] -> ()
  | hd :: t ->
      if w.key_le (fst kv) (fst hd) then ()
      else (insert_preserves_sorted w kv t; insert_head w kv t)

[@@ noextract_to "FSharp"]
let rec sort_produces_sorted (#num #flt: eqtype) (w: wire num flt)
                             (fs: list (list ch & jval num flt))
  : Lemma (requires key_order_ok w) (ensures sorted w (sort_kvs w fs)) (decreases fs) =
  match fs with
  | [] -> ()
  | kv :: t -> sort_produces_sorted w t; insert_preserves_sorted w kv (sort_kvs w t)

(* A list already in strict key order is its own sort. This is the half `render_deterministic`
   and `render_injective_on_sorted` both need, and it is where the STABILITY of `insert_kv`
   matters: insertion after an equal key is what reproduces `List.sortWith`, and a list with a
   REPEATED key is therefore not fixed by the sort in the way a strictly-ordered one is. *)
[@@ noextract_to "FSharp"]
let rec sorted_is_its_own_sort (#num #flt: eqtype) (w: wire num flt)
                               (fs: list (list ch & jval num flt))
  : Lemma (requires key_order_ok w /\ sorted w fs) (ensures sort_kvs w fs == fs) (decreases fs) =
  match fs with
  | [] -> ()
  | (k1, v1) :: t ->
      (match t with
       | [] -> ()
       | (k2, v2) :: _ -> sorted_is_its_own_sort w t)

(* Sorting moves members, never values, so it cannot take a canonical object out of the subset.
   Needed because `render` renders the SORTED members and the round trip is stated over the
   authored ones. *)
[@@ noextract_to "FSharp"]
let rec insert_preserves_canonical (#num #flt: eqtype) (w: wire num flt)
                                   (kv: (list ch & jval num flt))
                                   (l: list (list ch & jval num flt))
  : Lemma (requires canonical w (snd kv) /\ canonical_kvs w l)
          (ensures canonical_kvs w (insert_kv w kv l)) (decreases l) =
  match l with
  | [] -> ()
  | _ :: t -> insert_preserves_canonical w kv t

[@@ noextract_to "FSharp"]
let rec sort_preserves_canonical (#num #flt: eqtype) (w: wire num flt)
                                 (fs: list (list ch & jval num flt))
  : Lemma (requires canonical_kvs w fs)
          (ensures canonical_kvs w (sort_kvs w fs)) (decreases fs) =
  match fs with
  | [] -> ()
  | kv :: t -> sort_preserves_canonical w t; insert_preserves_canonical w kv (sort_kvs w t)

(* ======================================================================================
   8. `render_total`, and the shape dispatch that makes it exhaustive.
   ====================================================================================== *)

[@@ noextract_to "FSharp"]
let app_head (#a: Type) (l m: list a)
  : Lemma (requires Cons? l) (ensures Cons? (app l m) /\ Cons?.hd (app l m) == Cons?.hd l) = ()

[@@ noextract_to "FSharp"]
let num_head_is_not_structural (c: ch)
  : Lemma (requires num_ch c)
          (ensures ~(CQuote? c) /\ ~(CLBrack? c) /\ ~(CLBrace? c) /\ ~(CPlain? c) /\
                   ~(CRBrack? c) /\ ~(CRBrace? c) /\ ~(CComma? c) /\
                   ~(CHexCh? c /\ HDf? (CHexCh?.d c))) = ()

(* `render` is TOTAL by its type — F* admits a definition only after showing it defined on every
   input and terminating, so there is no partiality to rule out. What the lemma adds is that the
   rendering is never EMPTY and that its first character CLASSIFIES the constructor that produced
   it: a canonical value's first byte says whether a string, an array, an object, a boolean or a
   number follows, and it is never a closing or separating character. That is what makes the
   encoder's shape dispatch exhaustive rather than merely non-empty — the same distinction theorem
   1's `decode_total` draws on the decode side — and it is what the reader's own dispatch turns on
   at every position. *)
[@@ noextract_to "FSharp"]
let render_total (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Lemma (requires tok_read_ok w /\ canonical w v)
          (ensures Cons? (render w v) /\
                   (let h = Cons?.hd (render w v) in
                    ~(CRBrack? h) /\ ~(CRBrace? h) /\ ~(CComma? h) /\ ~(CColon? h) /\
                    (JStr? v ==> CQuote? h) /\
                    (JArr? v ==> CLBrack? h) /\
                    (JObj? v ==> CLBrace? h) /\
                    (JBool? v ==> (CPlain? h \/ (CHexCh? h /\ HDf? (CHexCh?.d h)))) /\
                    ((JInt? v \/ JFloat? v) ==> num_ch h))) =
  match v with
  | JStr _ -> ()
  | JBool _ -> ()
  | JArr _ -> ()
  | JObj _ -> ()
  | JInt i -> num_head_is_not_structural (Cons?.hd (w.int_str i))
  | JFloat f -> num_head_is_not_structural (Cons?.hd (canonical_float w f))

(* ======================================================================================
   9. The round trip — the reader is a LEFT INVERSE of `render` on the canonical subset, in
      every trailing context. Everything in section 10 is a corollary of this one lemma.
   ====================================================================================== *)

[@@ noextract_to "FSharp"]
let rec read_render_roundtrip (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
                              (rest: list ch)
  : Lemma (requires tok_read_ok w /\ canonical w v /\ sep_ok rest)
          (ensures read w (app (render w v) rest) == Ok (normalise w v, rest))
          (decreases %[(jsize v <: nat); 0]) =
  match v with
  | JStr s ->
      app_assoc (escape s) [CQuote] rest;
      read_str_inverts_escape s rest
  | JBool b -> if b then strip_app true_chars rest else strip_app false_chars rest
  | JInt i ->
      num_head_is_not_structural (Cons?.hd (w.int_str i));
      app_head (w.int_str i) rest;
      span_num_app (w.int_str i) rest
  | JFloat f ->
      num_head_is_not_structural (Cons?.hd (canonical_float w f));
      app_head (canonical_float w f) rest;
      span_num_app (canonical_float w f) rest
  | JArr xs -> read_items_roundtrip w xs rest
  | JObj fs -> sort_preserves_canonical w fs; read_kvs_roundtrip w (sort_kvs w fs) rest

and read_items_roundtrip (#num #flt: eqtype) (w: wire num flt) (xs: list (jval num flt))
                         (rest: list ch)
  : Lemma (requires tok_read_ok w /\ canonical_items w xs)
          (ensures read_items w (app (render_items w xs) rest) == Ok (normalise_items w xs, rest))
          (decreases %[(jsizes xs <: nat); 1]) =
  match xs with
  | [] -> ()
  | [x] ->
      render_total w x;
      app_assoc (render w x) [CRBrack] rest;
      app_head (render w x) (CRBrack :: rest);
      read_render_roundtrip w x (CRBrack :: rest)
  | x :: t ->
      render_total w x;
      app_assoc (render w x) (CComma :: render_items w t) rest;
      app_head (render w x) (CComma :: app (render_items w t) rest);
      read_render_roundtrip w x (CComma :: app (render_items w t) rest);
      read_items_roundtrip w t rest

and read_kvs_roundtrip (#num #flt: eqtype) (w: wire num flt)
                       (fs: list (list ch & jval num flt)) (rest: list ch)
  : Lemma (requires tok_read_ok w /\ canonical_kvs w fs)
          (ensures read_kvs w (app (render_kvs w fs) rest) == Ok (normalise_kvs w fs, rest))
          (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> ()
  | [(k, v)] ->
      app_assoc (quoted k) (CColon :: app (render w v) [CRBrace]) rest;
      app_assoc (escape k) [CQuote] (CColon :: app (app (render w v) [CRBrace]) rest);
      read_str_inverts_escape k (CColon :: app (app (render w v) [CRBrace]) rest);
      app_assoc (render w v) [CRBrace] rest;
      read_render_roundtrip w v (CRBrace :: rest)
  | (k, v) :: t ->
      app_assoc (quoted k) (CColon :: app (render w v) (CComma :: render_kvs w t)) rest;
      app_assoc (escape k) [CQuote]
                (CColon :: app (app (render w v) (CComma :: render_kvs w t)) rest);
      read_str_inverts_escape k
                (CColon :: app (app (render w v) (CComma :: render_kvs w t)) rest);
      app_assoc (render w v) (CComma :: render_kvs w t) rest;
      read_render_roundtrip w v (CComma :: app (render_kvs w t) rest);
      read_kvs_roundtrip w t rest

(* ======================================================================================
   10. The canonical form — the two directions, and the literal injectivity that follows
       on values a reader has already returned.
   ====================================================================================== *)

(* EQUAL BYTES IMPLY EQUAL NORMAL FORMS. The converse §2 never stated, and the one every digest
   in the estate rests on: an op-stream chain id, a DAG content id, a teleport digest and a
   cross-host attestation all hash this rendering and read hash equality as value equality.
   Straight out of the round trip, at the empty trailing context. *)
[@@ noextract_to "FSharp"]
let render_injective_up_to_key_order (#num #flt: eqtype) (w: wire num flt) (a b: jval num flt)
  : Lemma (requires tok_read_ok w /\ canonical w a /\ canonical w b /\ render w a == render w b)
          (ensures normalise w a == normalise w b) =
  app_nil (render w a);
  app_nil (render w b);
  read_render_roundtrip w a [];
  read_render_roundtrip w b []

let rec keys_of (#num #flt: eqtype) (fs: list (list ch & jval num flt)) : Tot (list (list ch)) =
  match fs with
  | [] -> []
  | (k, _) :: t -> k :: keys_of t

[@@ noextract_to "FSharp"]
let rec normalise_keeps_keys (#num #flt: eqtype) (w: wire num flt)
                             (fs: list (list ch & jval num flt))
  : Lemma (ensures keys_of (normalise_kvs w fs) == keys_of fs) (decreases fs) =
  match fs with
  | [] -> ()
  | _ :: t -> normalise_keeps_keys w t

(* RULE 4, as the property that makes it load-bearing rather than merely tidy. "`None` fields are
   EXCLUDED from object output" is only safe if an omitted key can never be confused with a
   present one — otherwise `{"a":1}` and a document that also carried an absent `b` could reach
   the same bytes, and a digest would identify two different documents. It cannot: two objects
   that render alike carry the same keys, in the same canonical order. *)
[@@ noextract_to "FSharp"]
let absence_is_structural (#num #flt: eqtype) (w: wire num flt)
                          (fs gs: list (list ch & jval num flt))
  : Lemma (requires tok_read_ok w /\ canonical_kvs w fs /\ canonical_kvs w gs /\
                    render w (JObj fs) == render w (JObj gs))
          (ensures keys_of (sort_kvs w fs) == keys_of (sort_kvs w gs)) =
  render_injective_up_to_key_order w (JObj fs) (JObj gs);
  normalise_keeps_keys w (sort_kvs w fs);
  normalise_keeps_keys w (sort_kvs w gs)

(* ---- the other direction: equal normal forms imply equal bytes, which is §2's own promise ---- *)

let rec normal (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Tot bool (decreases %[(jsize v <: nat); 0]) =
  match v with
  | JArr xs -> normal_items w xs
  | JObj fs -> sorted w fs && normal_kvs w fs
  | _ -> true

and normal_items (#num #flt: eqtype) (w: wire num flt) (xs: list (jval num flt))
  : Tot bool (decreases %[(jsizes xs <: nat); 1]) =
  match xs with
  | [] -> true
  | x :: t -> normal w x && normal_items w t

and normal_kvs (#num #flt: eqtype) (w: wire num flt) (fs: list (list ch & jval num flt))
  : Tot bool (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> true
  | (_, v) :: t -> normal w v && normal_kvs w t

[@@ noextract_to "FSharp"]
let rec sorted_survives_normalise (#num #flt: eqtype) (w: wire num flt)
                                  (fs: list (list ch & jval num flt))
  : Lemma (requires sorted w fs) (ensures sorted w (normalise_kvs w fs)) (decreases fs) =
  match fs with
  | [] -> ()
  | (k, v) :: t ->
      (match t with
       | [] -> ()
       | (k2, v2) :: _ -> sorted_survives_normalise w t)

(* `normalise` lands in the normal form and fixes it. The two together are what make "canonical
   form" more than a name: every value has one, and it is the same one however the members were
   authored. *)
[@@ noextract_to "FSharp"]
let rec normalise_is_normal (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Lemma (requires key_order_ok w) (ensures normal w (normalise w v))
          (decreases %[(jsize v <: nat); 0]) =
  match v with
  | JArr xs -> normalise_items_is_normal w xs
  | JObj fs ->
      sort_produces_sorted w fs;
      sorted_survives_normalise w (sort_kvs w fs);
      normalise_kvs_is_normal w (sort_kvs w fs)
  | _ -> ()

and normalise_items_is_normal (#num #flt: eqtype) (w: wire num flt) (xs: list (jval num flt))
  : Lemma (requires key_order_ok w) (ensures normal_items w (normalise_items w xs))
          (decreases %[(jsizes xs <: nat); 1]) =
  match xs with
  | [] -> ()
  | x :: t -> normalise_is_normal w x; normalise_items_is_normal w t

and normalise_kvs_is_normal (#num #flt: eqtype) (w: wire num flt)
                            (fs: list (list ch & jval num flt))
  : Lemma (requires key_order_ok w) (ensures normal_kvs w (normalise_kvs w fs))
          (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> ()
  | (k, v) :: t -> normalise_is_normal w v; normalise_kvs_is_normal w t

[@@ noextract_to "FSharp"]
let rec normalise_fixes_normal (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Lemma (requires key_order_ok w /\ normal w v) (ensures normalise w v == v)
          (decreases %[(jsize v <: nat); 0]) =
  match v with
  | JArr xs -> normalise_items_fixes_normal w xs
  | JObj fs -> sorted_is_its_own_sort w fs; normalise_kvs_fixes_normal w fs
  | _ -> ()

and normalise_items_fixes_normal (#num #flt: eqtype) (w: wire num flt) (xs: list (jval num flt))
  : Lemma (requires key_order_ok w /\ normal_items w xs)
          (ensures normalise_items w xs == xs) (decreases %[(jsizes xs <: nat); 1]) =
  match xs with
  | [] -> ()
  | x :: t -> normalise_fixes_normal w x; normalise_items_fixes_normal w t

and normalise_kvs_fixes_normal (#num #flt: eqtype) (w: wire num flt)
                               (fs: list (list ch & jval num flt))
  : Lemma (requires key_order_ok w /\ normal_kvs w fs)
          (ensures normalise_kvs w fs == fs) (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> ()
  | (k, v) :: t -> normalise_fixes_normal w v; normalise_kvs_fixes_normal w t

(* RULE 2, the half that makes the sort a CANONICAL choice rather than merely an order: a value
   and its normal form render identically, so the author's key order carries no information into
   the bytes at all. With the round trip above, this is the other direction of the iff. *)
[@@ noextract_to "FSharp"]
let rec render_ignores_key_order (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Lemma (requires key_order_ok w) (ensures render w (normalise w v) == render w v)
          (decreases %[(jsize v <: nat); 0]) =
  match v with
  | JArr xs -> render_items_ignores_key_order w xs
  | JObj fs ->
      sort_produces_sorted w fs;
      sorted_survives_normalise w (sort_kvs w fs);
      sorted_is_its_own_sort w (normalise_kvs w (sort_kvs w fs));
      render_kvs_ignores_key_order w (sort_kvs w fs)
  | _ -> ()

and render_items_ignores_key_order (#num #flt: eqtype) (w: wire num flt) (xs: list (jval num flt))
  : Lemma (requires key_order_ok w)
          (ensures render_items w (normalise_items w xs) == render_items w xs)
          (decreases %[(jsizes xs <: nat); 1]) =
  match xs with
  | [] -> ()
  | [x] -> render_ignores_key_order w x
  | x :: t -> render_ignores_key_order w x; render_items_ignores_key_order w t

and render_kvs_ignores_key_order (#num #flt: eqtype) (w: wire num flt)
                                 (fs: list (list ch & jval num flt))
  : Lemma (requires key_order_ok w)
          (ensures render_kvs w (normalise_kvs w fs) == render_kvs w fs)
          (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> ()
  | [(k, v)] -> render_ignores_key_order w v
  | (k, v) :: t -> render_ignores_key_order w v; render_kvs_ignores_key_order w t

(* §2's own promise, proved: structurally equal values — equal up to the member order rule 2
   normalises away — render byte for byte identically. *)
[@@ noextract_to "FSharp"]
let render_deterministic (#num #flt: eqtype) (w: wire num flt) (a b: jval num flt)
  : Lemma (requires key_order_ok w /\ normalise w a == normalise w b)
          (ensures render w a == render w b) =
  render_ignores_key_order w a;
  render_ignores_key_order w b

(* THE CANONICAL FORM, in one statement. This is what the phase is for: `Canon.render` is not
   merely deterministic, it is a canonical form — the bytes determine the value, and the value
   determines the bytes, over the subset rule 5's own slot rule describes. *)
[@@ noextract_to "FSharp"]
let canonical_form_iff (#num #flt: eqtype) (w: wire num flt) (a b: jval num flt)
  : Lemma (requires tok_read_ok w /\ key_order_ok w /\ canonical w a /\ canonical w b)
          (ensures (render w a == render w b) <==> (normalise w a == normalise w b)) =
  if render w a = render w b then render_injective_up_to_key_order w a b
  else if normalise w a = normalise w b then render_deterministic w a b
  else ()

(* THE LITERAL FORM the shard asked for, on the subset where it is true: a value whose objects
   are ALREADY in canonical key order. That is not a contrivance — it is the shape of every value
   a reader hands back, so it is what a consumer comparing two decoded documents actually holds. *)
[@@ noextract_to "FSharp"]
let render_injective_on_normal (#num #flt: eqtype) (w: wire num flt) (a b: jval num flt)
  : Lemma (requires tok_read_ok w /\ key_order_ok w /\ canonical w a /\ canonical w b /\
                    normal w a /\ normal w b /\ render w a == render w b)
          (ensures a == b) =
  render_injective_up_to_key_order w a b;
  normalise_fixes_normal w a;
  normalise_fixes_normal w b

(* The round trip at the whole document, which is the form every corollary below quotes. *)
[@@ noextract_to "FSharp"]
let read_render (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Lemma (requires tok_read_ok w /\ canonical w v)
          (ensures read w (render w v) == Ok (normalise w v, [])) =
  app_nil (render w v);
  read_render_roundtrip w v []

(* And the reader does hand back a normal value — which is what makes the restriction above a
   description of practice rather than a hedge. *)
[@@ noextract_to "FSharp"]
let read_returns_a_normal_value (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Lemma (requires tok_read_ok w /\ key_order_ok w /\ canonical w v)
          (ensures read w (render w v) == Ok (normalise w v, []) /\ normal w (normalise w v)) =
  read_render w v;
  normalise_is_normal w v

(* ---- the §21 restatement, which is the only thing `Limits` is imported for ---- *)

(* The round trip is stated against the MODEL's reader, which has no depth cap. Production's
   entry point is `Json.parse`, which does (`Json.defaultMaxDepth`), and WIRE_FORMAT §21.1 bounds
   a conformant document's syntactic nesting at `Limits.max_json_depth`. This is the same theorem
   with that bound carried, so the claims ladder can say which §21 limit this theorem depends on
   and a change to the table moves one premise rather than a paragraph of prose. Nothing in the
   proof uses the hypothesis — that is the point: within the format's own limits the round trip is
   unconditional, and outside them it is production's cap and not this model that decides. *)
[@@ noextract_to "FSharp"]
let read_render_roundtrip_within_limits (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Lemma (requires tok_read_ok w /\ canonical w v /\ jdepth v <= max_json_depth)
          (ensures read w (render w v) == Ok (normalise w v, [])) =
  read_render w v

(* ======================================================================================
   11. The four refutations — `render_injective` as the shard states it, disproved.

       Each is stated for EVERY wire rather than exhibited at a contrived one, which is the
       stronger form: no instantiation can escape them. Three of the four are documented design
       choices of the format; the first is the finding this phase records.
   ====================================================================================== *)

(* 1. A NON-FINITE FLOAT IS A STRING ON THE WIRE. `canonicalFloat` emits the QUOTED token `"NaN"`,
      and so does the string of those three characters. Two different values, one digest. `render`
      does not refuse the float; since Phase 165 `Canon.tryRender` does (section 13), and its
      predicate is this lemma's hypothesis and its two siblings', taken together. *)
[@@ noextract_to "FSharp"]
let render_aliases_nan (#num #flt: eqtype) (w: wire num flt) (f: flt)
  : Lemma (requires w.fclass f == FNaN)
          (ensures render w (JFloat f) == render w (JStr nan_chars) /\
                   ~(JFloat f == JStr #num #flt nan_chars)) = ()

[@@ noextract_to "FSharp"]
let render_aliases_pos_inf (#num #flt: eqtype) (w: wire num flt) (f: flt)
  : Lemma (requires w.fclass f == FPosInf)
          (ensures render w (JFloat f) == render w (JStr inf_chars) /\
                   ~(JFloat f == JStr #num #flt inf_chars)) = ()

[@@ noextract_to "FSharp"]
let render_aliases_neg_inf (#num #flt: eqtype) (w: wire num flt) (f: flt)
  : Lemma (requires w.fclass f == FNegInf)
          (ensures render w (JFloat f) == render w (JStr neg_inf_chars) /\
                   ~(JFloat f == JStr #num #flt neg_inf_chars)) = ()

(* 2. AN INTEGRAL FLOAT IS AN INTEGER ON THE WIRE — the documented numeric normalisation (`JVal`'s
      own type doc: "render (JFloat 2.0) emits 2, which parse reads back as JInt 2"). The
      hypothesis is exactly the negation of `float_canonical`'s marker clause, which is why the
      canonical subset is defined by that clause and not by something weaker. *)
[@@ noextract_to "FSharp"]
let render_aliases_integral_float (#num #flt: eqtype) (w: wire num flt) (i: num) (f: flt)
  : Lemma (requires w.fclass f == FFinite /\
                    w.float_str (if w.is_zero f then w.pos_zero else f) == w.int_str i)
          (ensures render w (JFloat f) == render w (JInt i) /\ ~(JFloat f == JInt #num #flt i)) = ()

(* 3. THE TWO ZEROES ARE ONE TOKEN — rule 5's `-0` collapse, applied before the layout. *)
[@@ noextract_to "FSharp"]
let render_aliases_negative_zero (#num #flt: eqtype) (w: wire num flt) (f g: flt)
  : Lemma (requires w.fclass f == FFinite /\ w.fclass g == FFinite /\ w.is_zero f /\ w.is_zero g)
          (ensures render w (JFloat f) == render w (JFloat g)) = ()

(* 4. MEMBER ORDER IS NOT OBSERVABLE — rule 2 sorts, so two objects differing only in the order
      their members were authored are one byte sequence. Unlike the three above this is not a loss:
      it is the whole purpose of a canonical form, and it is precisely why the theorem in section
      10 is stated up to member order rather than as the literal injectivity the shard asked for. *)
[@@ noextract_to "FSharp"]
let render_aliases_member_order (#num #flt: eqtype) (w: wire num flt)
                                (k1 k2: list ch) (v1 v2: jval num flt)
  : Lemma (requires key_order_ok w /\ key_lt w k1 k2)
          (ensures render w (JObj [(k1, v1); (k2, v2)]) == render w (JObj [(k2, v2); (k1, v1)]) /\
                   ~(JObj #num #flt [(k1, v1); (k2, v2)] == JObj [(k2, v2); (k1, v1)])) = ()

(* ======================================================================================
   12. NO NULL ON THE WAY OUT (Phase 153) — WIRE_FORMAT §2 rule 4, the encoder's half.

       `jval` has no null constructor, so `render` has no clause that could spell one — true, and
       until now prose. The lemma an assessor can be handed is about the BYTES: `render` never
       emits the token `null`, at the root or at any depth, for any value whatsoever.

       WHY THE STATEMENT IS LEXICAL, and not "the rendering does not contain n-u-l-l". That
       sentence is FALSE: the string "null" renders as `"null"`, which contains those four
       characters, and so does a member KEY spelled `null`. What JSON means by the null TOKEN is
       those characters OUTSIDE a string literal, so the model carries the three-state lexer every
       JSON reader has (outside a string / inside one / after a backslash) and states the claim
       there. It then proves something stronger than the absence of one token: outside a string
       literal `render` emits ONLY the structural punctuation, the characters of a numeral, and
       the letters of `true` and `false` (`bare_ok`). There is no bare `n` anywhere in a
       rendering — and therefore no `null`, no `NaN`, and no bare word of any kind a reader could
       be asked to interpret. (The three non-finite float tokens are QUOTED; that they alias
       strings is refutation 1 above, and is a different fact from this one.)

       THE ONE PREMISE is `layouts_numeric`: rule 5's two layouts are numerals — every character
       of `string i` and of the round-trip float layout is a digit, `-`, `+`, `.` or `E`. It is
       WEAKER than `tok_read_ok`, which says that and more of every integer and every canonical
       float; it is stated separately because this lemma is about EVERY value, including the
       integral floats section 5 puts outside the canonical subset, about whose layout
       `tok_read_ok` says nothing. The numerals themselves stay .NET's to compute, as everywhere
       in this model.

       WHAT IS NOT CLAIMED. `Canon.renderOrdered` is not modelled (header). And this is a theorem
       about the ENCODER: a host that writes JSON by some other route is not described by it.

       Every definition below is PROOF-ONLY and erased at extraction, so the oracle is unchanged.
   ====================================================================================== *)

(* A JSON lexer's string state: outside a literal, inside one, or inside one just after `\`. *)
[@@ noextract_to "FSharp"]
type lex =
  | LOut | LIn | LEsc

[@@ noextract_to "FSharp"]
let lex_step (st: lex) (c: ch) : Tot lex =
  match st with
  | LOut -> if CQuote? c then LIn else LOut
  | LIn -> (match c with
            | CQuote -> LOut
            | CBackslash -> LEsc
            | _ -> LIn)
  | LEsc -> LIn

[@@ noextract_to "FSharp"]
let rec lex_end (st: lex) (l: list ch) : Tot lex (decreases l) =
  match l with
  | [] -> st
  | c :: t -> lex_end (lex_step st c) t

(* The alphabet `render` is allowed OUTSIDE a string literal: the structural punctuation, the
   characters of a numeral, and the letters of `true` / `false` (`t r u e f a l s` — `u` is `CLu`
   and `e f a` are hex digits in this alphabet). Anything else verbatim is refused — in particular
   `n`, the first character of `null` and of `NaN`. *)
[@@ noextract_to "FSharp"]
let bare_ok (c: ch) : Tot bool =
  match c with
  | CBackslash -> false
  | CCtrl _ _ -> false
  | CPlain s -> s = "t" || s = "r" || s = "l" || s = "s"
  | _ -> true

(* Every character OUTSIDE a string literal is in that alphabet. *)
[@@ noextract_to "FSharp"]
let rec bare_clean (st: lex) (l: list ch) : Tot bool (decreases l) =
  match l with
  | [] -> true
  | c :: t -> (if LOut? st then bare_ok c else true) && bare_clean (lex_step st c) t

(* F#: the four characters of the JSON `null` token. *)
[@@ noextract_to "FSharp"]
let null_chars : list ch = [CPlain "n"; CLu; CPlain "l"; CPlain "l"]

(* Rule 5, the half this lemma needs: both layouts are numerals. *)
[@@ noextract_to "FSharp"]
let layouts_numeric (#num #flt: eqtype) (w: wire num flt) : prop =
  (forall (i: num). all_num (w.int_str i)) /\ (forall (f: flt). all_num (w.float_str f))

(* The lexer is compositional over concatenation — which is all a renderer that is "a concatenation
   and nothing else" needs. *)
[@@ noextract_to "FSharp"]
let rec lex_app (st: lex) (l m: list ch)
  : Lemma (ensures lex_end st (app l m) == lex_end (lex_end st l) m /\
                   bare_clean st (app l m) == (bare_clean st l && bare_clean (lex_end st l) m))
          (decreases l) =
  match l with
  | [] -> ()
  | c :: t -> lex_app (lex_step st c) t m

(* Rule 6 keeps a string body INSIDE the literal: the escape of any character returns the lexer to
   the in-string state, so the only quote that closes a literal is the one `quoted` appends. *)
[@@ noextract_to "FSharp"]
let esc_ch_stays_inside (c: ch)
  : Lemma (ensures lex_end LIn (esc_ch c) == LIn /\ bare_clean LIn (esc_ch c)) =
  match c with
  | CQuote -> assert_norm (lex_end LIn [CBackslash; CQuote] == LIn /\
                           bare_clean LIn [CBackslash; CQuote])
  | CBackslash -> assert_norm (lex_end LIn [CBackslash; CBackslash] == LIn /\
                               bare_clean LIn [CBackslash; CBackslash])
  | CCtrl hi lo ->
      let e = [CBackslash; CLu; CHexCh HD0; CHexCh HD0; CHexCh (if hi then HD1 else HD0); CHexCh lo] in
      assert_norm (lex_end LIn e == LIn /\ bare_clean LIn e)
  | _ -> ()

[@@ noextract_to "FSharp"]
let rec escape_stays_inside (s: list ch)
  : Lemma (ensures lex_end LIn (escape s) == LIn /\ bare_clean LIn (escape s)) (decreases s) =
  match s with
  | [] -> ()
  | c :: t ->
      esc_ch_stays_inside c;
      escape_stays_inside t;
      lex_app LIn (esc_ch c) (escape t)

[@@ noextract_to "FSharp"]
let quoted_is_one_literal (s: list ch)
  : Lemma (ensures lex_end LOut (quoted s) == LOut /\ bare_clean LOut (quoted s)) =
  escape_stays_inside s;
  lex_app LIn (escape s) [CQuote]

(* A numeral never opens a literal and is made of the numeric alphabet. *)
[@@ noextract_to "FSharp"]
let rec numeral_is_bare (t: list ch)
  : Lemma (requires all_num t) (ensures lex_end LOut t == LOut /\ bare_clean LOut t) (decreases t) =
  match t with
  | [] -> ()
  | _ :: r -> numeral_is_bare r

[@@ noextract_to "FSharp"]
let literals_are_bare (u: unit)
  : Lemma (ensures lex_end LOut true_chars == LOut /\ bare_clean LOut true_chars /\
                   lex_end LOut false_chars == LOut /\ bare_clean LOut false_chars) =
  assert_norm (lex_end LOut true_chars == LOut /\ bare_clean LOut true_chars);
  assert_norm (lex_end LOut false_chars == LOut /\ bare_clean LOut false_chars)

(* The renderer, clause for clause: every rendering leaves the lexer OUTSIDE a literal and is clean. *)
[@@ noextract_to "FSharp"]
let rec render_is_bare_clean (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Lemma (requires layouts_numeric w)
          (ensures lex_end LOut (render w v) == LOut /\ bare_clean LOut (render w v))
          (decreases %[(jsize v <: nat); 0]) =
  match v with
  | JStr s -> quoted_is_one_literal s
  | JInt i -> numeral_is_bare (w.int_str i)
  | JBool _ -> literals_are_bare ()
  | JFloat f ->
      (match w.fclass f with
       | FNaN -> quoted_is_one_literal nan_chars
       | FPosInf -> quoted_is_one_literal inf_chars
       | FNegInf -> quoted_is_one_literal neg_inf_chars
       | FFinite -> numeral_is_bare (w.float_str (if w.is_zero f then w.pos_zero else f)))
  | JArr xs -> render_items_is_bare_clean w xs
  | JObj fs -> render_kvs_is_bare_clean w (sort_kvs w fs)

and render_items_is_bare_clean (#num #flt: eqtype) (w: wire num flt) (xs: list (jval num flt))
  : Lemma (requires layouts_numeric w)
          (ensures lex_end LOut (render_items w xs) == LOut /\ bare_clean LOut (render_items w xs))
          (decreases %[(jsizes xs <: nat); 1]) =
  match xs with
  | [] -> ()
  | [x] ->
      render_is_bare_clean w x;
      lex_app LOut (render w x) [CRBrack]
  | x :: t ->
      render_is_bare_clean w x;
      render_items_is_bare_clean w t;
      lex_app LOut (render w x) (CComma :: render_items w t)

and render_kvs_is_bare_clean (#num #flt: eqtype) (w: wire num flt)
                             (fs: list (list ch & jval num flt))
  : Lemma (requires layouts_numeric w)
          (ensures lex_end LOut (render_kvs w fs) == LOut /\ bare_clean LOut (render_kvs w fs))
          (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> ()
  | [(k, v)] ->
      quoted_is_one_literal k;
      render_is_bare_clean w v;
      lex_app LOut (render w v) [CRBrace];
      lex_app LOut (quoted k) (CColon :: app (render w v) [CRBrace])
  | (k, v) :: t ->
      quoted_is_one_literal k;
      render_is_bare_clean w v;
      render_kvs_is_bare_clean w t;
      lex_app LOut (render w v) (CComma :: render_kvs w t);
      lex_app LOut (quoted k) (CColon :: app (render w v) (CComma :: render_kvs w t))

(* THE LEMMA (§2 rule 4, the encoder's half). For every value: the rendering is clean outside its
   string literals, so it is not the token `null`, and it does not CONTAIN the token `null` at any
   position that lies outside a literal — `pre` is everything before the position, and
   `lex_end LOut pre == LOut` is what "outside a literal" means. *)
[@@ noextract_to "FSharp"]
let no_null_ever (#num #flt: eqtype) (w: wire num flt) (v: jval num flt) (pre rest: list ch)
  : Lemma (requires layouts_numeric w)
          (ensures bare_clean LOut (render w v) /\
                   ~(render w v == null_chars) /\
                   (lex_end LOut pre == LOut ==> ~(render w v == app pre (app null_chars rest)))) =
  render_is_bare_clean w v;
  assert_norm (bare_clean LOut null_chars == false);
  lex_app LOut pre (app null_chars rest);
  lex_app LOut null_chars rest

(* … and the grammar's own reader has no arm that would accept one: the only bare word it reads
   that opens with an ordinary letter is `true`. *)
[@@ noextract_to "FSharp"]
let reader_refuses_null (#num #flt: eqtype) (w: wire num flt) (rest: list ch)
  : Lemma (ensures Error? (read w (app null_chars rest))) =
  assert_norm (app null_chars rest == CPlain "n" :: app [CLu; CPlain "l"; CPlain "l"] rest)

(* ======================================================================================
   13. THE GUARD (Phase 165) — `Canon.tryRender`, the refusal beside the renderer.

       Section 11's first refutation is the only one of the four that is not a documented design
       choice of the format: a non-finite float becomes a STRING on the wire, and nothing in
       `Canon` refused it. `Canon.tryRender` is the entry point that does, in the shape
       `Json.tryRender` gives `Json.render` — a typed refusal naming the first non-finite float
       by path, and otherwise exactly the renderer.

       WHAT IS MODELLED. `first_nonfinite` is `Canon.tryRender`'s `firstNonFinite`, clause for
       clause: document order, an array by index (`List.indexed`'s counter is the `i` here), an
       object's members in AUTHORED order — the scan runs before any sort, as production's does.
       `try_render` is the two-armed match on its result. The path is carried as DATA (`pstep`)
       rather than as production's rendered string, and the refusal carries the float rather
       than its token, so the theorems below are about where the guard points and what it points
       at; the host's differential renders both and compares the message production actually
       emits.

       WHAT IS PROVED.
         - `tryrender_is_render_on_finite` — wherever every float is finite the guard IS the
           renderer: `try_render w v == Rendered (render w v)`. `finite_all` is the FIRST clause
           of `float_canonical` and nothing else, lifted over a value.
         - `tryrender_is_render_on_canonical` — the corollary the acceptance asks for: the
           canonical subset of section 5 sits inside the accepted set.
         - `tryrender_refuses_exactly_aliasing` — the guard refuses IFF some float is not finite;
           and a refusal's path REACHES a float in the value whose class is one of the three
           non-finite ones and whose own rendering is, by section 11's first refutation, the
           rendering of a string. The guard and `render_aliases_nan` / `_pos_inf` / `_neg_inf`
           share one predicate: `w.fclass f <> FFinite`.
         - `nonfinite_anywhere_is_refused` — the converse at depth: a non-finite float reachable
           by ANY path is refused, so no position in a document hides one from the guard.
         - `tryrender_keeps_the_documented_normalisations` — refutations 2, 3 and 4 are NOT
           refused. An integer-shaped finite float, either zero, and a mis-ordered object all
           render, to exactly `render`'s bytes. The guard must not refuse what the format
           documents, and this is that sentence as a lemma.

       WHAT IS NOT PROVED, said plainly. (a) MINIMALITY — that the path named is the FIRST
       non-finite float in document order — is modelled (the scan is production's, clause for
       clause) and measured by the differential, but there is no theorem that every position
       before it is finite. (b) DOCUMENT-LEVEL aliasing — that a refused document renders
       identically to the document with the named float replaced by its string — is not proved
       here; it is a congruence through `sort_kvs` and is not needed by the guard, whose job is
       to refuse. What IS proved is that the float the refusal names aliases a string on its own.
       (c) `Canon.renderOrdered` is not modelled (header), and has no guarded companion.

       The scan, the guard and the three result types EXTRACT — the differential runs them
       beside production. Everything else below is PROOF-ONLY.
   ====================================================================================== *)

(* One step of the path the guard names. F#: `"[" + string i + "]"` for an array item and
   `"[\"" + escape k + "\"]"` for an object member. *)
type pstep =
  | PItem   : i:nat -> pstep
  | PMember : k:list ch -> pstep

(* F#: `firstNonFinite`'s `option` — spelled out so the extraction stays on `Prims` alone. *)
type scan (flt: eqtype) =
  | AllFinite : scan flt
  | NonFinite : path:list pstep -> f:flt -> scan flt

(* F#: `Canon.tryRender`'s `Result` — the rendering, or the refusal's path and the float it names. *)
type guarded (flt: eqtype) =
  | Rendered : bytes:list ch -> guarded flt
  | Refused  : path:list pstep -> f:flt -> guarded flt

(* F#: `firstNonFinite`. The guard `System.Double.IsNaN f || System.Double.IsInfinity f` is
   `w.fclass f <> FFinite` — the negation of `float_canonical`'s first clause, and of nothing more. *)
let rec first_nonfinite (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Tot (scan flt) (decreases %[(jsize v <: nat); 0]) =
  match v with
  | JFloat f -> if w.fclass f = FFinite then AllFinite else NonFinite [] f
  | JArr xs -> first_nonfinite_items w 0 xs
  | JObj fs -> first_nonfinite_kvs w fs
  | _ -> AllFinite

and first_nonfinite_items (#num #flt: eqtype) (w: wire num flt) (i: nat) (xs: list (jval num flt))
  : Tot (scan flt) (decreases %[(jsizes xs <: nat); 1]) =
  match xs with
  | [] -> AllFinite
  | x :: t ->
      (match first_nonfinite w x with
       | NonFinite p f -> NonFinite (PItem i :: p) f
       | AllFinite -> first_nonfinite_items w (i + 1) t)

and first_nonfinite_kvs (#num #flt: eqtype) (w: wire num flt) (fs: list (list ch & jval num flt))
  : Tot (scan flt) (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> AllFinite
  | (k, v) :: t ->
      (match first_nonfinite w v with
       | NonFinite p f -> NonFinite (PMember k :: p) f
       | AllFinite -> first_nonfinite_kvs w t)

(* F#: `Canon.tryRender`. `render` is called, never re-implemented: the guard adds a refusal and
   changes no byte. *)
let try_render (#num #flt: eqtype) (w: wire num flt) (v: jval num flt) : Tot (guarded flt) =
  match first_nonfinite w v with
  | NonFinite p f -> Refused p f
  | AllFinite -> Rendered (render w v)

(* ---- the predicate, stated independently of the scan ---- *)

(* `float_canonical`'s FIRST clause, lifted over a value. Deliberately not `canonical`: the marker
   clause is the documented numeric normalisation, and the guard must not refuse it. *)
[@@ noextract_to "FSharp"]
let rec finite_all (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Tot bool (decreases %[(jsize v <: nat); 0]) =
  match v with
  | JFloat f -> w.fclass f = FFinite
  | JArr xs -> finite_items w xs
  | JObj fs -> finite_kvs w fs
  | _ -> true

and finite_items (#num #flt: eqtype) (w: wire num flt) (xs: list (jval num flt))
  : Tot bool (decreases %[(jsizes xs <: nat); 1]) =
  match xs with
  | [] -> true
  | x :: t -> finite_all w x && finite_items w t

and finite_kvs (#num #flt: eqtype) (w: wire num flt) (fs: list (list ch & jval num flt))
  : Tot bool (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> true
  | (_, v) :: t -> finite_all w v && finite_kvs w t

(* What it means for a path to point at something. A RELATION rather than a lookup, on purpose:
   `JObj` is an association LIST, a repeated key is representable, and a by-key lookup would find
   the first member of that key where the scan may have named a later one. `reaches v p x` holds
   when SOME walk along `p` from `v` ends at `x`. *)
[@@ noextract_to "FSharp"]
let rec reaches (#num #flt: eqtype) (v: jval num flt) (p: list pstep) (x: jval num flt)
  : Tot bool (decreases %[(jsize v <: nat); 0]) =
  match p with
  | [] -> v = x
  | PItem i :: r -> (match v with | JArr xs -> reaches_item xs i r x | _ -> false)
  | PMember k :: r -> (match v with | JObj fs -> reaches_member fs k r x | _ -> false)

and reaches_item (#num #flt: eqtype) (xs: list (jval num flt)) (i: nat) (r: list pstep)
                 (x: jval num flt)
  : Tot bool (decreases %[(jsizes xs <: nat); 1]) =
  match xs with
  | [] -> false
  | y :: t -> if i = 0 then reaches y r x else reaches_item t (i - 1) r x

and reaches_member (#num #flt: eqtype) (fs: list (list ch & jval num flt)) (k: list ch)
                   (r: list pstep) (x: jval num flt)
  : Tot bool (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> false
  | (k', y) :: t -> (k' = k && reaches y r x) || reaches_member t k r x

(* ---- the scan decides `finite_all`, and what it names is really there ---- *)

[@@ noextract_to "FSharp"]
let rec scan_decides_finite (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Lemma (ensures AllFinite? (first_nonfinite w v) == finite_all w v)
          (decreases %[(jsize v <: nat); 0]) =
  match v with
  | JArr xs -> scan_items_decides_finite w 0 xs
  | JObj fs -> scan_kvs_decides_finite w fs
  | _ -> ()

and scan_items_decides_finite (#num #flt: eqtype) (w: wire num flt) (i: nat)
                              (xs: list (jval num flt))
  : Lemma (ensures AllFinite? (first_nonfinite_items w i xs) == finite_items w xs)
          (decreases %[(jsizes xs <: nat); 1]) =
  match xs with
  | [] -> ()
  | x :: t -> scan_decides_finite w x; scan_items_decides_finite w (i + 1) t

and scan_kvs_decides_finite (#num #flt: eqtype) (w: wire num flt)
                            (fs: list (list ch & jval num flt))
  : Lemma (ensures AllFinite? (first_nonfinite_kvs w fs) == finite_kvs w fs)
          (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> ()
  | (_, v) :: t -> scan_decides_finite w v; scan_kvs_decides_finite w t

[@@ noextract_to "FSharp"]
let rec scan_names_a_nonfinite_float (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Lemma (ensures (match first_nonfinite w v with
                    | AllFinite -> True
                    | NonFinite p f -> w.fclass f <> FFinite /\ reaches v p (JFloat f)))
          (decreases %[(jsize v <: nat); 0]) =
  match v with
  | JArr xs -> scan_items_names_a_nonfinite_float w 0 xs
  | JObj fs -> scan_kvs_names_a_nonfinite_float w fs
  | _ -> ()

and scan_items_names_a_nonfinite_float (#num #flt: eqtype) (w: wire num flt) (i: nat)
                                       (xs: list (jval num flt))
  : Lemma (ensures (match first_nonfinite_items w i xs with
                    | AllFinite -> True
                    | NonFinite p f ->
                        w.fclass f <> FFinite /\
                        (match p with
                         | PItem n :: r -> n >= i /\ reaches_item xs (n - i) r (JFloat f)
                         | _ -> False)))
          (decreases %[(jsizes xs <: nat); 1]) =
  match xs with
  | [] -> ()
  | x :: t ->
      scan_names_a_nonfinite_float w x;
      scan_items_names_a_nonfinite_float w (i + 1) t

and scan_kvs_names_a_nonfinite_float (#num #flt: eqtype) (w: wire num flt)
                                     (fs: list (list ch & jval num flt))
  : Lemma (ensures (match first_nonfinite_kvs w fs with
                    | AllFinite -> True
                    | NonFinite p f ->
                        w.fclass f <> FFinite /\
                        (match p with
                         | PMember k :: r -> reaches_member fs k r (JFloat f)
                         | _ -> False)))
          (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> ()
  | (k0, v) :: t ->
      scan_names_a_nonfinite_float w v;
      scan_kvs_names_a_nonfinite_float w t;
      (match first_nonfinite w v with
       | NonFinite p f -> assert (reaches_member fs k0 p (JFloat f))
       | AllFinite ->
           (match first_nonfinite_kvs w t with
            | NonFinite (PMember k :: r) f -> assert (reaches_member fs k r (JFloat f))
            | _ -> ()))

(* The converse at depth: a non-finite float at the end of ANY path breaks `finite_all`. *)
[@@ noextract_to "FSharp"]
let rec reached_nonfinite_breaks_finite (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
                                        (p: list pstep) (f: flt)
  : Lemma (requires reaches v p (JFloat f) /\ w.fclass f <> FFinite)
          (ensures not (finite_all w v))
          (decreases %[(jsize v <: nat); 0]) =
  match p with
  | [] -> ()
  | PItem i :: r -> (match v with | JArr xs -> reached_item_breaks_finite w xs i r f | _ -> ())
  | PMember k :: r -> (match v with | JObj fs -> reached_member_breaks_finite w fs k r f | _ -> ())

and reached_item_breaks_finite (#num #flt: eqtype) (w: wire num flt) (xs: list (jval num flt))
                               (i: nat) (r: list pstep) (f: flt)
  : Lemma (requires reaches_item xs i r (JFloat f) /\ w.fclass f <> FFinite)
          (ensures not (finite_items w xs))
          (decreases %[(jsizes xs <: nat); 1]) =
  match xs with
  | [] -> ()
  | y :: t ->
      if i = 0 then reached_nonfinite_breaks_finite w y r f
      else reached_item_breaks_finite w t (i - 1) r f

and reached_member_breaks_finite (#num #flt: eqtype) (w: wire num flt)
                                 (fs: list (list ch & jval num flt)) (k: list ch)
                                 (r: list pstep) (f: flt)
  : Lemma (requires reaches_member fs k r (JFloat f) /\ w.fclass f <> FFinite)
          (ensures not (finite_kvs w fs))
          (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> ()
  | (k', y) :: t ->
      if k' = k && reaches y r (JFloat f) then reached_nonfinite_breaks_finite w y r f
      else reached_member_breaks_finite w t k r f

(* The canonical subset of section 5 is inside the accepted set: `float_canonical` is `finite`
   AND a marker, and the guard keeps only the first conjunct. *)
[@@ noextract_to "FSharp"]
let rec canonical_is_finite (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Lemma (requires canonical w v) (ensures finite_all w v)
          (decreases %[(jsize v <: nat); 0]) =
  match v with
  | JArr xs -> canonical_items_is_finite w xs
  | JObj fs -> canonical_kvs_is_finite w fs
  | _ -> ()

and canonical_items_is_finite (#num #flt: eqtype) (w: wire num flt) (xs: list (jval num flt))
  : Lemma (requires canonical_items w xs) (ensures finite_items w xs)
          (decreases %[(jsizes xs <: nat); 1]) =
  match xs with
  | [] -> ()
  | x :: t -> canonical_is_finite w x; canonical_items_is_finite w t

and canonical_kvs_is_finite (#num #flt: eqtype) (w: wire num flt)
                            (fs: list (list ch & jval num flt))
  : Lemma (requires canonical_kvs w fs) (ensures finite_kvs w fs)
          (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> ()
  | (_, v) :: t -> canonical_is_finite w v; canonical_kvs_is_finite w t

(* ---- the theorems ---- *)

(* The token section 11's first refutation says a non-finite float of each class renders as. *)
[@@ noextract_to "FSharp"]
let alias_token (c: fcls) : Tot (list ch) =
  match c with
  | FNaN -> nan_chars
  | FPosInf -> inf_chars
  | FNegInf -> neg_inf_chars
  | FFinite -> []

(* THE GUARD AGREES WITH THE RENDERER WHEREVER IT ACCEPTS — and it accepts wherever every float
   is finite. Not "on the canonical subset": on the strictly larger set the first clause of
   `float_canonical` describes, which is what makes the corollary below a corollary. *)
[@@ noextract_to "FSharp"]
let tryrender_is_render_on_finite (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Lemma (requires finite_all w v) (ensures try_render w v == Rendered (render w v)) =
  scan_decides_finite w v

[@@ noextract_to "FSharp"]
let tryrender_is_render_on_canonical (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Lemma (requires canonical w v) (ensures try_render w v == Rendered (render w v)) =
  canonical_is_finite w v;
  tryrender_is_render_on_finite w v

(* THE GUARD REFUSES EXACTLY THE ALIASING FLOATS. It refuses iff some float is not finite; and what
   a refusal names is a float that is really at that path, whose class is one of the three
   non-finite ones, and whose own rendering is — by `render_aliases_nan` / `_pos_inf` /
   `_neg_inf`, which the last two conjuncts restate at the class the refusal carries — byte for
   byte the rendering of a STRING it is not equal to. *)
[@@ noextract_to "FSharp"]
let tryrender_refuses_exactly_aliasing (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
  : Lemma (ensures (Refused? (try_render w v) == not (finite_all w v)) /\
                   (match try_render w v with
                    | Rendered _ -> True
                    | Refused p f ->
                        w.fclass f <> FFinite /\
                        reaches v p (JFloat f) /\
                        render w (JFloat f) == render w (JStr (alias_token (w.fclass f))) /\
                        ~(JFloat f == JStr #num #flt (alias_token (w.fclass f))))) =
  scan_decides_finite w v;
  scan_names_a_nonfinite_float w v

[@@ noextract_to "FSharp"]
let nonfinite_anywhere_is_refused (#num #flt: eqtype) (w: wire num flt) (v: jval num flt)
                                  (p: list pstep) (f: flt)
  : Lemma (requires reaches v p (JFloat f) /\ w.fclass f <> FFinite)
          (ensures Refused? (try_render w v)) =
  reached_nonfinite_breaks_finite w v p f;
  scan_decides_finite w v

(* THE GUARD DOES NOT REFUSE WHAT THE FORMAT DOCUMENTS. Refutations 2, 3 and 4 of section 11, each
   under its own hypothesis, each ACCEPTED and rendered to `render`'s own bytes: an integer-shaped
   finite float, the two zeroes, and an object authored out of key order. *)
[@@ noextract_to "FSharp"]
let tryrender_keeps_the_documented_normalisations (#num #flt: eqtype) (w: wire num flt)
                                                  (i: num) (f g: flt) (k1 k2: list ch)
  : Lemma (ensures
            ((w.fclass f == FFinite /\
              w.float_str (if w.is_zero f then w.pos_zero else f) == w.int_str i) ==>
                try_render w (JFloat f) == Rendered (render w (JInt #num #flt i))) /\
            ((w.fclass f == FFinite /\ w.fclass g == FFinite /\ w.is_zero f /\ w.is_zero g) ==>
                try_render w (JFloat #num #flt f) == Rendered (render w (JFloat #num #flt g))) /\
            (try_render w (JObj #num #flt [(k2, JBool true); (k1, JBool false)]) ==
                Rendered (render w (JObj #num #flt [(k2, JBool true); (k1, JBool false)])))) = ()
