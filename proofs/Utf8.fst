(*
   Utf8 — an F* model of Fuaran.Core's UTF-8 ENCODER and its guarded form, with the encoding's
   injectivity on well-formed UTF-16 as a machine-checked theorem (fuaran-core Phase 306).

   WHY THE PHASE EXISTS. `WireCanon.fst` proves the canonical form at the level of CHARACTERS:
   equal renderings are equal normal forms. No digest is taken over characters. `Hash.sha256Hex`
   hashes the UTF-8 BYTES of the rendering, so the claim every attested head rests on — equal
   digests mean equal values, collisions of the hash aside — needs one more step that nothing
   stated: that two different renderings never share their bytes. They can. A UTF-16 string may
   hold a surrogate with no partner, such a unit has no code point, UTF-8 has no encoding for it,
   and every encoder substitutes something. Until Phase 290 this one read the next unit as the low
   half unchecked, so the units D801 D800 encoded as the four bytes of U+10000; since Phase 290 it
   writes the replacement character's bytes, as the platform does — and then a lone D800, a lone
   DFFF and U+FFFD itself are ONE byte string. Replacement only moves which strings collide.

   WHAT IS MODELLED. `Hash.utf8Bytes` (src/Fuaran.Core.Tree/Hash.fs), clause for clause and in its
   own branch order: one byte below U+0080, two below U+0800, the four-byte arm for a high
   surrogate FOLLOWED AT ONCE BY A LOW ONE, the three replacement bytes for any other surrogate,
   and three bytes for the rest of the BMP. Beside it `Hash.firstIllFormedUnit` and
   `Hash.tryUtf8Bytes`, the guarded form: the scan that names the first unpaired surrogate, and
   the encoder behind it.

   WHAT IS PROVED.
     - `utf8_injective` — two WELL-FORMED unit strings with one byte string are one string. This is
       the theorem; it is proved the way `WireCanon` proves its own, by exhibiting a left inverse
       (`unread`) rather than by case analysis on pairs of strings.
     - `try_utf8_is_utf8_on_well_formed` / `try_utf8_refuses_exactly_ill_formed` — the guard IS the
       encoder wherever the string is well-formed, and refuses exactly where it is not, naming a
       unit that is a surrogate and is at the index it gives.
     - `guarded_utf8_injective` — the two together, in the shape a caller holds: two strings the
       guard ACCEPTED, with equal bytes, are equal.
     - `replacement_is_not_injective` / `unpaired_pair_collides` — the refutations. The unguarded
       encoder maps three different one-unit strings to `EF BF BD`, and the units D801 D800 to the
       bytes of two replacement characters. Not defects of this encoder: they are what
       `System.Text.Encoding.UTF8` answers, and the unguarded path is pinned to it.
     - `well_formed_app`, `no_surrogates_well_formed` — well-formedness is closed under
       concatenation and holds of any surrogate-free string; what a renderer that concatenates
       well-formed pieces needs, and what `WireCanon`'s digest theorem takes from here.

   WHAT IS NOT MODELLED, AND WHY.
     - A UNIT IS AN INTEGER, and so is a byte. Production's `>>> 6` and `&&& 0x3F` are written as
       `/ 64` and `% 64`, which is what they are on a non-negative value; nothing here models a
       machine word. Integers are also why this module is CHECKED AND NOT EXTRACTED: F*'s `int`
       does not survive the extraction this directory uses (README, finding 2), so there is no
       oracle to run beside production. The bridge to the shipped encoder is instead the
       programme's first INDEPENDENT-oracle differential — `Hash.utf8Bytes` against the platform's
       own encoder over every code unit and every boundary pair, and the guard against the
       platform's strict encoder — which the ladder records as tested, beside the assumed row that
       says the model is the code.
     - SHA-256 IS NOT MODELLED. Injectivity of the pre-image is what a model can settle; that the
       digest of two different byte strings differs is the hash's collision resistance, a premise
       wherever a digest is read as an identity.
     - `unread` is a left inverse and nothing else. It validates nothing — on bytes no encoder here
       produced it returns garbage — and it is not a UTF-8 decoder anyone should ship.

   Apache-2.0, like everything beside it.
*)
module Utf8

(* ======================================================================================
   0. Lists, self-contained (README, finding 2 — and so the module opens nothing).
   ====================================================================================== *)

let rec app (#a: Type) (l1 l2: list a) : Tot (list a) (decreases l1) =
  match l1 with
  | [] -> l2
  | x :: t -> x :: app t l2

(* ======================================================================================
   1. Units, and what makes a string of them a string of characters.
   ====================================================================================== *)

(* F#: `c >= 0xD800 && c <= 0xDBFF`. *)
let is_high (c: int) : Tot bool = c >= 0xD800 && c <= 0xDBFF

(* F#: `c >= 0xDC00 && c <= 0xDFFF`. *)
let is_low (c: int) : Tot bool = c >= 0xDC00 && c <= 0xDFFF

let is_surrogate (c: int) : Tot bool = c >= 0xD800 && c <= 0xDFFF

(* A UTF-16 code unit: what a .NET `char` holds. *)
let is_unit (c: int) : Tot bool = c >= 0 && c < 65536

(* WELL-FORMED UTF-16: every unit is a unit, every high surrogate is followed at once by a low
   one, and every low one follows a high one. F#: `Hash.firstIllFormedUnit s = None`, and the rule
   the wire parser and the guarded canonical renderer refuse on. *)
let rec well_formed (s: list int) : Tot bool (decreases s) =
  match s with
  | [] -> true
  | c :: t ->
    if not (is_unit c) then false
    else if is_high c then
      (match t with
       | lo :: t2 -> is_low lo && well_formed t2
       | [] -> false)
    else if is_low c then false
    else well_formed t

(* ======================================================================================
   2. The encoder (F#: `Hash.utf8Bytes`).
   ====================================================================================== *)

(* F#: the `pairs` test — a high surrogate is half of a pair only when the NEXT unit is a low one. *)
let pairs (c: int) (t: list int) : Tot bool =
  is_high c && (match t with
                | lo :: _ -> is_low lo
                | [] -> false)

(* F#: `Hash.utf8Bytes`, in production's branch order. The four-byte arm consumes two units, as
   production's `i <- i + 1` inside it does. *)
let rec utf8 (s: list int) : Tot (list int) (decreases s) =
  match s with
  | [] -> []
  | c :: t ->
    if c < 0x80 then c :: utf8 t
    else if c < 0x800 then (0xC0 + c / 64) :: (0x80 + c % 64) :: utf8 t
    else if pairs c t then
      (match t with
       | lo :: t2 ->
         let cp = 0x10000 + (c - 0xD800) * 1024 + (lo - 0xDC00) in
         (0xF0 + cp / 262144) :: (0x80 + (cp / 4096) % 64) :: (0x80 + (cp / 64) % 64)
         :: (0x80 + cp % 64) :: utf8 t2
       | [] -> [])
    else if is_surrogate c then
      (* A lone or ill-ordered surrogate: U+FFFD, as the platform encoder writes it. *)
      0xEF :: 0xBF :: 0xBD :: utf8 t
    else (0xE0 + c / 4096) :: (0x80 + (c / 64) % 64) :: (0x80 + c % 64) :: utf8 t

(* ======================================================================================
   3. The guard (F#: `Hash.firstIllFormedUnit`, `Hash.tryUtf8Bytes`).
   ====================================================================================== *)

type ill_formed = { index: nat; code: int }

(* F#: `Hash.firstIllFormedUnit`'s loop, with `i` the index of the head of `s`. *)
let rec first_ill_formed (i: nat) (s: list int) : Tot (option ill_formed) (decreases s) =
  match s with
  | [] -> None
  | c :: t ->
    if is_high c then
      (match t with
       | lo :: t2 -> if is_low lo then first_ill_formed (i + 2) t2 else Some ({ index = i; code = c })
       | [] -> Some ({ index = i; code = c }))
    else if is_low c then Some ({ index = i; code = c })
    else first_ill_formed (i + 1) t

type guarded =
  | Encoded : bytes:list int -> guarded
  | Refused : at:ill_formed -> guarded

(* F#: `Hash.tryUtf8Bytes`. The encoder is called, never re-implemented. *)
let try_utf8 (s: list int) : Tot guarded =
  match first_ill_formed 0 s with
  | Some bad -> Refused bad
  | None -> Encoded (utf8 s)

(* Every element is a code unit — true of any .NET string, and the one thing the scan does not
   check because a `char` cannot be anything else. *)
let rec all_units (s: list int) : Tot bool (decreases s) =
  match s with
  | [] -> true
  | c :: t -> is_unit c && all_units t

(* ======================================================================================
   4. The left inverse — and the theorem.
   ====================================================================================== *)

(* Reads back what `utf8` wrote for a well-formed string, by the lead byte's class. PROOF-ONLY in
   spirit: it checks nothing about continuation bytes and must not be mistaken for a decoder. *)
let rec unread (bs: list int) : Tot (option (list int)) (decreases bs) =
  match bs with
  | [] -> Some []
  | b0 :: t ->
    if b0 < 0x80 then
      (match unread t with
       | Some r -> Some (b0 :: r)
       | None -> None)
    else if b0 < 0xE0 then
      (match t with
       | b1 :: t2 ->
         (match unread t2 with
          | Some r -> Some (((b0 - 0xC0) * 64 + (b1 - 0x80)) :: r)
          | None -> None)
       | _ -> None)
    else if b0 < 0xF0 then
      (match t with
       | b1 :: b2 :: t3 ->
         (match unread t3 with
          | Some r -> Some (((b0 - 0xE0) * 4096 + (b1 - 0x80) * 64 + (b2 - 0x80)) :: r)
          | None -> None)
       | _ -> None)
    else
      (match t with
       | b1 :: b2 :: b3 :: t4 ->
         (match unread t4 with
          | Some r ->
            let v = (b0 - 0xF0) * 262144 + (b1 - 0x80) * 4096 + (b2 - 0x80) * 64 + (b3 - 0x80)
                    - 0x10000 in
            Some ((0xD800 + v / 1024) :: (0xDC00 + v % 1024) :: r)
          | None -> None)
       | _ -> None)

(* THE ROUND TRIP. On a well-formed string the encoder's bytes read back to the string. One case
   per branch of the encoder; the arithmetic in each is division and remainder by constants. *)
#push-options "--z3rlimit 80 --fuel 2 --ifuel 2"
let rec unread_utf8 (s: list int)
  : Lemma (requires well_formed s) (ensures unread (utf8 s) == Some s) (decreases s) =
  match s with
  | [] -> ()
  | c :: t ->
    if c < 0x80 then unread_utf8 t
    else if c < 0x800 then unread_utf8 t
    else if is_high c then
      (match t with
       | lo :: t2 -> unread_utf8 t2
       | [] -> ())
    else unread_utf8 t
#pop-options

(* THE THEOREM. Equal bytes, equal strings — on the strings that have code points. *)
let utf8_injective (a b: list int)
  : Lemma (requires well_formed a /\ well_formed b /\ utf8 a == utf8 b) (ensures a == b) =
  unread_utf8 a;
  unread_utf8 b

(* ======================================================================================
   5. The guard decides well-formedness, and names what it refuses.
   ====================================================================================== *)

let rec scan_decides_well_formed (i: nat) (s: list int)
  : Lemma (requires all_units s)
          (ensures None? (first_ill_formed i s) == well_formed s)
          (decreases s) =
  match s with
  | [] -> ()
  | c :: t ->
    if is_high c then
      (match t with
       | lo :: t2 -> if is_low lo then scan_decides_well_formed (i + 2) t2 else ()
       | [] -> ())
    else if is_low c then ()
    else scan_decides_well_formed (i + 1) t

(* The unit at an index, for saying what a refusal points at. *)
let rec nth (s: list int) (n: nat) : Tot (option int) (decreases s) =
  match s with
  | [] -> None
  | c :: t -> if n = 0 then Some c else nth t (n - 1)

(* What the scan names is really there, and is a surrogate. *)
let rec scan_names_a_surrogate (i: nat) (s: list int)
  : Lemma (ensures (match first_ill_formed i s with
                    | None -> True
                    | Some bad ->
                      bad.index >= i /\ is_surrogate bad.code /\
                      nth s (bad.index - i) == Some bad.code))
          (decreases s) =
  match s with
  | [] -> ()
  | c :: t ->
    if is_high c then
      (match t with
       | lo :: t2 -> if is_low lo then scan_names_a_surrogate (i + 2) t2 else ()
       | [] -> ())
    else if is_low c then ()
    else scan_names_a_surrogate (i + 1) t

(* THE GUARD IS THE ENCODER WHEREVER IT ACCEPTS. *)
let try_utf8_is_utf8_on_well_formed (s: list int)
  : Lemma (requires all_units s /\ well_formed s) (ensures try_utf8 s == Encoded (utf8 s)) =
  scan_decides_well_formed 0 s

(* … AND IT REFUSES EXACTLY THE ILL-FORMED STRINGS, naming a surrogate that is at the index it
   gives. *)
let try_utf8_refuses_exactly_ill_formed (s: list int)
  : Lemma (requires all_units s)
          (ensures (Refused? (try_utf8 s) == not (well_formed s)) /\
                   (match try_utf8 s with
                    | Encoded _ -> True
                    | Refused bad -> is_surrogate bad.code /\ nth s bad.index == Some bad.code)) =
  scan_decides_well_formed 0 s;
  scan_names_a_surrogate 0 s

(* THE FORM A CALLER HOLDS. Two strings the guard accepted, with equal bytes, are one string —
   the injectivity the unguarded encoder cannot have. *)
let guarded_utf8_injective (a b: list int) (x: list int)
  : Lemma (requires all_units a /\ all_units b /\
                    try_utf8 a == Encoded x /\ try_utf8 b == Encoded x)
          (ensures a == b) =
  scan_decides_well_formed 0 a;
  scan_decides_well_formed 0 b;
  utf8_injective a b

(* ======================================================================================
   6. The refutations — what the unguarded encoder does to a string with no code points.
   ====================================================================================== *)

(* REPLACEMENT IS NOT INJECTIVE. A lone high surrogate, a lone low one and the replacement
   character itself are three strings and one byte string, `EF BF BD` — which is what the platform
   encoder answers for each, and why the unguarded path is described as platform parity and
   nothing more. *)
let replacement_is_not_injective (_: unit)
  : Lemma (ensures utf8 [0xD800] == [0xEF; 0xBF; 0xBD] /\
                   utf8 [0xDFFF] == [0xEF; 0xBF; 0xBD] /\
                   utf8 [0xFFFD] == [0xEF; 0xBF; 0xBD] /\
                   well_formed [0xFFFD] /\
                   not (well_formed [0xD800]) /\ not (well_formed [0xDFFF])) =
  assert_norm (utf8 [0xD800] == [0xEF; 0xBF; 0xBD]);
  assert_norm (utf8 [0xDFFF] == [0xEF; 0xBF; 0xBD]);
  assert_norm (utf8 [0xFFFD] == [0xEF; 0xBF; 0xBD])

(* The pair Phase 290 found: a high surrogate followed by a HIGH one. It was read as a pair and
   encoded as U+10000 until then; it is two replacement characters now, so it collides with the
   well-formed string of two U+FFFD instead of with the well-formed string of one U+10000. The
   collision moved; it did not go. Only the guard removes it. *)
let unpaired_pair_collides (_: unit)
  : Lemma (ensures utf8 [0xD801; 0xD800] == utf8 [0xFFFD; 0xFFFD] /\
                   well_formed [0xFFFD; 0xFFFD] /\
                   not (well_formed [0xD801; 0xD800]) /\
                   Refused? (try_utf8 [0xD801; 0xD800])) =
  assert_norm (utf8 [0xD801; 0xD800] == utf8 [0xFFFD; 0xFFFD]);
  assert_norm (Refused? (try_utf8 [0xD801; 0xD800]))

(* And the astral character that pair used to be mistaken for is itself well-formed and has its
   own four bytes — the encoding the collision was with. *)
let astral_pair_is_four_bytes (_: unit)
  : Lemma (ensures well_formed [0xD800; 0xDC00] /\
                   utf8 [0xD800; 0xDC00] == [0xF0; 0x90; 0x80; 0x80]) =
  assert_norm (utf8 [0xD800; 0xDC00] == [0xF0; 0x90; 0x80; 0x80])

(* ======================================================================================
   7. Closure — what a renderer that concatenates well-formed pieces needs.
   ====================================================================================== *)

let rec no_surrogates (s: list int) : Tot bool (decreases s) =
  match s with
  | [] -> true
  | c :: t -> is_unit c && not (is_surrogate c) && no_surrogates t

(* A string with no surrogate in it is well-formed: there is nothing to pair. *)
let rec no_surrogates_well_formed (s: list int)
  : Lemma (requires no_surrogates s) (ensures well_formed s) (decreases s) =
  match s with
  | [] -> ()
  | _ :: t -> no_surrogates_well_formed t

(* WELL-FORMEDNESS IS CLOSED UNDER CONCATENATION. A well-formed string ends on a whole character,
   so nothing in what follows can complete or break a pair in it. *)
let rec well_formed_app (a b: list int)
  : Lemma (requires well_formed a /\ well_formed b) (ensures well_formed (app a b))
          (decreases a) =
  match a with
  | [] -> ()
  | c :: t ->
    if is_high c then
      (match t with
       | _ :: t2 -> well_formed_app t2 b
       | [] -> ())
    else well_formed_app t b
