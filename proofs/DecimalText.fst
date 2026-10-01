(*
   DecimalText — the exact decimal's text arithmetic: `Fuaran.Core.DecimalText`'s canonical
   form, order and sum, modelled clause for clause and proved (fuaran-core Phase 279).

   WHAT IS MODELLED. `src/Fuaran.Core.Column/Column.fs`, module `DecimalText` — the carrier of a
   `Decimal` cell (DECISIONS.md D72) and the only arithmetic the column layer does over it:

     - the READ, `parts`: the optional minus, the first point, the two digit runs, the two trims
       and the sign-on-zero clause, over a text read as its SYMBOLS — the minus, the point, a
       digit by its value, and `Other` for every character the grammar does not name, because
       `parts` refuses a text holding one wherever it stands, so the class is all a theorem needs;
     - the WRITE, `render`, and `tryCanonical` / `isCanonical` / `zero` over the two;
     - the MAGNITUDES: `aligned` (the fractions padded to one scale on the right, the whole padded
       to one width on the left), the right-to-left loops `addMagnitudes` and `subMagnitudes` as
       the recursion over the reversed digit list they are (`add_lsb` / `sub_lsb`, the carry and
       the borrow carried as the loop carries them), and `sign (CompareOrdinal ma mb)` over two
       equal-width digit strings as the lexicographic order of their digits (`lex`);
     - `compare` and `add`, each as `parts` on both texts and then its `Some` arm over the two
       records (`compare_parts` / `add_parts`), which is the shape the F# has.

   `tryToFloat` is NOT modelled: it is the one place the type rounds, and it is the aggregates'
   business (D72 K7). Nothing here opens another module; the list helpers are restated and the
   extraction shares only `Prims.fs` and the option shim with the models beside it.

   WHAT A TEXT DENOTES (section 5, which does not extract). A `parts` record `p` denotes the
   rational `numer p / 10^(scale p)` — `numer` the signed value of its digits read as one run,
   `scale` the length of its fraction. It is carried as a NUMERATOR AT A SCALE, `denotes p m`
   for any `m` at or above the record's own scale, so two texts are compared at a common scale
   in unbounded integers alone, and `denotes_scale` says the `m` chosen reads nothing into it.
   `scale_up x k` is `x * 10^k` written as `k` multiplications by the literal 10: every
   arithmetic query in this file is LINEAR, which is why the module discharges at the leg's own
   rlimit with no options scoped anywhere.

   WHAT IS PROVED, over every text:

     - CANONICAL FORM. `canonical_idempotent` — what `tryCanonical` writes it reads back to the
       same text, through `parts_normal` (every record `parts` returns is NORMAL: no leading
       zero on the integer digits, no trailing zero on the fraction, no sign on zero) and
       `render_parts` (`parts (render p) == Some p` for every normal `p`).
       `same_number_iff_canonical` — D72 K3: two accepted texts denote one number if and only if
       their canonical forms are one text; the core is `normal_injective`, a normal record is
       DETERMINED by its numerator at the common scale. `zero_unique` — zero has exactly one
       canonical text, whatever sign, zeros or point it was written with.
     - ORDER. `compare_denotes` — `compare` answers the sign of the difference of the numbers
       denoted, read at the common scale; so it is a total order (`compare_antisymmetric`,
       `compare_transitive`) that answers 0 exactly where the canonical forms agree
       (`compare_zero_iff_canonical`), and `1.50` and `1.5` compare equal because they denote
       one number.
     - SUM. `add_denotes` — the sum is the sum: `add a b` renders a normal record at or below
       the common scale whose numerator there is the sum of the two numerators, through the
       carry (`add_lsb_value`), the borrow (`sub_lsb_value`, discarded only where it is zero)
       and the two trims. `add_canonical` — it always answers canonical text. And because one
       number has one canonical text, the laws are corollaries rather than separate proofs:
       `add_commutative`, `add_associative` (through a three-way common scale) and
       `add_zero_identity` (adding zero canonicalises and does nothing else).
     - REFUSAL. `parts_iff_grammar` — the accepted set is EXACTLY the grammar D72 K4 states,
       `-?[0-9]+(\.[0-9]+)?`, written as a recogniser that shares nothing with `parts`; and
       `refusal_exact` — `tryCanonical`, `compare` and `add` each answer `None` exactly off it.

   THE FINDINGS. None. Every clause of the shipped module was modelled as written and every
   theorem the phase asked for holds of it; the two places a finding was looked for and not
   found are recorded so they are not looked for again. The sign of zero: `parts` drops the
   minus on a zero magnitude in BOTH arms (`-0` and `-0.00` alike), and `add` drops it again on a
   cancelled sum, so `zero_unique` holds with no clause added. The discarded borrow:
   `subMagnitudes` throws away the borrow its last step leaves, which is a defect in general and
   is not one here, because production reaches it only past the `CompareOrdinal` that orders the
   magnitudes, and `sub_magnitudes_mag` is stated — and needed — over exactly that premise.

   WHAT IS NOT CLAIMED. `tryToFloat`. Anything about a .NET string beyond the symbol reading the
   differential's bridge performs — that `'0'..'9'` are ten consecutive code units and that
   `string` on a digit writes the character the model's `of_digits` stands for; both are host
   facts the `Proofs.Oracle` differential holds, not premises stated here. Anything about the
   column layer's USE of this module (`Column.aggregate`, the token, the capture key), which the
   models beside this one state in their own terms.

   HOW TO READ IT. Every definition names its F# counterpart. Sections 0–4 extract and are the
   oracle the differential runs beside production; sections 5–10 are the specification and the
   proofs, marked `noextract_to "FSharp"` where they define anything.

   Apache-2.0, like everything beside it.
*)
module DecimalText

(* ======================================================================================
   0. The list helpers, self-contained, each naming the FSharp.Core function it stands for.
   ====================================================================================== *)

(* F#: `List.length` / `String.Length`. *)
let rec len (#a:Type0) (l: list a) : Tot nat =
  match l with
  | [] -> 0
  | _ :: t -> 1 + len t

(* F#: `@` / string `+`. *)
let rec app (#a:Type0) (l m: list a) : Tot (list a) =
  match l with
  | [] -> m
  | x :: t -> x :: app t m

(* F#: `List.rev` — the loops `addMagnitudes` and `subMagnitudes` walk from `a.Length - 1` down
   to `0`, which is a walk over the REVERSED digit list; the model names that walk as one. *)
let rec rev (#a:Type0) (l: list a) : Tot (list a) =
  match l with
  | [] -> []
  | x :: t -> app (rev t) [x]

(* F#: `List.take` / `Substring(0, n)`. *)
let rec take (#a:Type0) (n: nat) (l: list a) : Tot (list a) =
  if n = 0 then []
  else (match l with
        | [] -> []
        | x :: t -> x :: take (n - 1) t)

(* F#: `List.skip` / `Substring n`. *)
let rec drop (#a:Type0) (n: nat) (l: list a) : Tot (list a) =
  if n = 0 then l
  else (match l with
        | [] -> []
        | _ :: t -> drop (n - 1) t)

(* F#: `max`. *)
let max (a b: nat) : Tot nat = if a >= b then a else b

(* ======================================================================================
   1. The alphabet. A text is read as its symbols: the minus, the point, a digit by its value,
      and OTHER for every character the grammar does not name — `parts` refuses a text holding
      one wherever it stands, so the class is all a theorem needs of it.
   ====================================================================================== *)

type digit = d:nat{d < 10}

type sym =
  | Minus
  | Dot
  | Digit : digit -> sym
  | Other

(* F#: `'0'` runs — `PadRight(scale, '0')` / `PadLeft(width, '0')`. *)
let rec zeros (k: nat) : Tot (list digit) =
  if k = 0 then [] else 0 :: zeros (k - 1)

(* F#: `c >= '0' && c <= '9'`. *)
let is_digit (c: sym) : Tot bool = Digit? c

let rec all_digits (s: list sym) : Tot bool =
  match s with
  | [] -> true
  | c :: t -> is_digit c && all_digits t

(* F#: `isDigits` — non-empty and every character a digit. *)
let is_digits (s: list sym) : Tot bool = Cons? s && all_digits s

(* F#: `digit c` = `int c - int '0'`, over a run `isDigits` accepted. Total, so that the
   extraction carries no precondition: a symbol that is not a digit reads as 0, and every call
   below sits under an `is_digits` check that rules one out. *)
let rec to_digits (s: list sym) : Tot (list digit) =
  match s with
  | [] -> []
  | Digit d :: t -> d :: to_digits t
  | _ :: t -> 0 :: to_digits t

(* F#: `string` on a digit, concatenated (`digitsText`), and the text a digit run is read from. *)
let rec of_digits (l: list digit) : Tot (list sym) =
  match l with
  | [] -> []
  | d :: t -> Digit d :: of_digits t

(* F#: `body.IndexOf '.'` with the two `Substring`s either side of it — `None` for `dot < 0`, and
   otherwise the text before the FIRST point and the text after it. *)
let rec split_dot (s: list sym) : Tot (option (list sym & list sym)) =
  match s with
  | [] -> None
  | c :: t ->
      if c = Dot then Some ([], t)
      else (match split_dot t with
            | None -> None
            | Some (before, after) -> Some (c :: before, after))

(* F#: `ip.TrimStart '0'`. *)
let rec trim_start (l: list digit) : Tot (list digit) =
  match l with
  | [] -> []
  | d :: t -> if d = 0 then trim_start t else l

(* F#: `fp.TrimEnd '0'`. *)
let rec trim_end (l: list digit) : Tot (list digit) =
  match l with
  | [] -> []
  | d :: t ->
      let r = trim_end t in
      if d = 0 && Nil? r then [] else d :: r

(* ======================================================================================
   2. `parts`, `render`, `tryCanonical` (F#: `DecimalText.parts` / `render` / `tryCanonical` /
      `isCanonical` / `zero`).
   ====================================================================================== *)

(* F#: the `(negative, ip, fp)` triple `parts` returns, the digits read as their values. A record
   rather than a tuple so the extraction names its fields. *)
type dparts = {
  negative: bool;
  ip: list digit;
  fp: list digit;
}

(* F#: `DecimalText.parts`, clause for clause. `isNull (box s) || s.Length = 0` is the empty list
   here — a symbol list has no null. *)
let parts (s: list sym) : Tot (option dparts) =
  match s with
  | [] -> None
  | c0 :: rest ->
      let neg = (c0 = Minus) in
      let body = if neg then rest else s in
      (match split_dot body with
       | None ->
           if not (is_digits body) then None
           else
             let ip' = trim_start (to_digits body) in
             Some ({ negative = neg && Cons? ip'; ip = ip'; fp = [] })
       | Some (ip0, fp0) ->
           if not (is_digits ip0 && is_digits fp0) then None
           else
             let ip' = trim_start (to_digits ip0) in
             let fp' = trim_end (to_digits fp0) in
             let is_zero = Nil? ip' && Nil? fp' in
             Some ({ negative = neg && not is_zero; ip = ip'; fp = fp' }))

(* F#: `DecimalText.render`. *)
let render (p: dparts) : Tot (list sym) =
  app (if p.negative then [Minus] else [])
      (app (of_digits (if Nil? p.ip then [0] else p.ip))
           (if Nil? p.fp then [] else Dot :: of_digits p.fp))

(* F#: `DecimalText.tryCanonical`. *)
let try_canonical (s: list sym) : Tot (option (list sym)) =
  match parts s with
  | None -> None
  | Some p -> Some (render p)

(* F#: `DecimalText.isCanonical`. *)
let is_canonical (s: list sym) : Tot bool = try_canonical s = Some s

(* F#: `DecimalText.zero`. *)
let zero_text : list sym = [Digit 0]

(* ======================================================================================
   3. The magnitudes: `aligned`, the digit-wise sum and difference, the ordinal order
      (F#: `aligned` / `addMagnitudes` / `subMagnitudes` / `System.String.CompareOrdinal`).
   ====================================================================================== *)

(* F#: `fp.PadRight(n, '0')`. *)
let pad_right (l: list digit) (n: nat) : Tot (list digit) =
  if len l >= n then l else app l (zeros (n - len l))

(* F#: `s.PadLeft(n, '0')`. *)
let pad_left (l: list digit) (n: nat) : Tot (list digit) =
  if len l >= n then l else app (zeros (n - len l)) l

(* F#: `DecimalText.aligned` — the fractions padded to one scale on the right, the whole padded
   to one width on the left. *)
let aligned (a b: dparts) : Tot (list digit & list digit & nat) =
  let scale = max (len a.fp) (len b.fp) in
  let ma = app a.ip (pad_right a.fp scale) in
  let mb = app b.ip (pad_right b.fp scale) in
  let width = max (len ma) (len mb) in
  (pad_left ma width, pad_left mb width, scale)

(* F#: the loop body of `addMagnitudes` — `out.[i + 1] <- d % 10; carry <- d / 10` for a `d`
   that is at most 19, which is `if d < 10 then (d, 0) else (d - 10, 1)`. *)
let add_digit (x y: digit) (c: nat{c <= 1}) : Tot (digit & nat) =
  let d = x + y + c in
  if d < 10 then (d, 0) else (d - 10, 1)

(* F#: `addMagnitudes`'s loop, from `a.Length - 1` down to `0`, as a recursion over the REVERSED
   (least-significant-first) digit lists; the carry left at the end is the `out.[0]` digit. *)
let rec add_lsb (a: list digit) (b: list digit{len b = len a}) (c: nat{c <= 1})
  : Tot (list digit) (decreases a) =
  match a, b with
  | x :: xs, y :: ys ->
      let (d, c') = add_digit x y c in
      d :: add_lsb xs ys c'
  | _, _ -> [c]

(* F#: the loop body of `subMagnitudes` — `if d < 0 then (d + 10, 1) else (d, 0)`. *)
let sub_digit (x y: digit) (br: nat{br <= 1}) : Tot (digit & nat) =
  let d = x - y - br in
  if d < 0 then (d + 10, 1) else (d, 0)

(* F#: `subMagnitudes`'s loop, likewise over the reversed lists; the borrow left at the end is
   what production DISCARDS (the loop runs over `a >= b`, and the theorems say so). *)
let rec sub_lsb (a: list digit) (b: list digit{len b = len a}) (br: nat{br <= 1})
  : Tot (list digit & nat) (decreases a) =
  match a, b with
  | x :: xs, y :: ys ->
      let (d, br') = sub_digit x y br in
      let (rest, out) = sub_lsb xs ys br' in
      (d :: rest, out)
  | _, _ -> ([], br)

(* `List.length` over `@`, `rev` and the two pads — the facts the refinements below need. *)
let rec len_app (#a:Type0) (l m: list a)
  : Lemma (ensures len (app l m) == len l + len m) [SMTPat (len (app l m))] =
  match l with
  | [] -> ()
  | _ :: t -> len_app t m

let rec rev_len (#a:Type0) (l: list a) : Lemma (ensures len (rev l) == len l) [SMTPat (len (rev l))] =
  match l with
  | [] -> ()
  | x :: t -> rev_len t; len_app (rev t) [x]

let rec zeros_len (k: nat) : Lemma (ensures len (zeros k) == k) [SMTPat (len (zeros k))] =
  if k = 0 then () else zeros_len (k - 1)

let pad_right_len (l: list digit) (n: nat)
  : Lemma (ensures len (pad_right l n) == max (len l) n) [SMTPat (len (pad_right l n))] = ()

let pad_left_len (l: list digit) (n: nat)
  : Lemma (ensures len (pad_left l n) == max (len l) n) [SMTPat (len (pad_left l n))] = ()

(* F#: `addMagnitudes a b` over equal-width magnitudes. *)
let add_magnitudes (a: list digit) (b: list digit{len b = len a}) : Tot (list digit) =
  rev (add_lsb (rev a) (rev b) 0)

(* F#: `subMagnitudes a b` over equal-width magnitudes, `a >= b`. *)
let sub_magnitudes (a: list digit) (b: list digit{len b = len a}) : Tot (list digit) =
  let (digits, _) = sub_lsb (rev a) (rev b) 0 in
  rev digits

(* F#: `sign (System.String.CompareOrdinal(ma, mb))` over two equal-width digit strings — the
   ordinal order of digit characters is the order of their values, so this is the lexicographic
   order of the digit lists, already a sign. *)
let rec lex (a b: list digit) : Tot int =
  match a, b with
  | [], [] -> 0
  | [], _ -> 0 - 1
  | _, [] -> 1
  | x :: xs, y :: ys -> if x < y then 0 - 1 else if x > y then 1 else lex xs ys

(* ======================================================================================
   4. `compare` and `add` (F#: `DecimalText.compare` / `add`).
   ====================================================================================== *)

(* F#: the `Some` arm of `DecimalText.compare`, over the two parts it read. *)
let compare_parts (pa pb: dparts) : Tot int =
  if pa.negative <> pb.negative then (if pa.negative then 0 - 1 else 1)
  else
    let (ma, mb, _) = aligned pa pb in
    let magnitude = lex ma mb in
    if pa.negative then 0 - magnitude else magnitude

(* F#: `DecimalText.compare`. *)
let compare (a b: list sym) : Tot (option int) =
  match parts a, parts b with
  | Some pa, Some pb -> Some (compare_parts pa pb)
  | _, _ -> None

(* F#: the `Some` arm of `DecimalText.add`, over the two parts it read, up to the `render`. *)
let add_parts (pa pb: dparts) : Tot dparts =
  let (ma, mb, scale) = aligned pa pb in
  let (negative, magnitude) =
    if pa.negative = pb.negative then (pa.negative, add_magnitudes ma mb)
    else
      let c = lex ma mb in
      if c = 0 then (false, [])
      else if c > 0 then (pa.negative, sub_magnitudes ma mb)
      else (pb.negative, sub_magnitudes mb ma) in
  let magnitude = pad_left magnitude (scale + 1) in
  let ip = take (len magnitude - scale) magnitude in
  let fp = drop (len magnitude - scale) magnitude in
  let ip' = trim_start ip in
  let fp' = trim_end fp in
  let is_zero = Nil? ip' && Nil? fp' in
  { negative = negative && not is_zero; ip = ip'; fp = fp' }

(* F#: `DecimalText.add`. *)
let add (a b: list sym) : Tot (option (list sym)) =
  match parts a, parts b with
  | Some pa, Some pb -> Some (render (add_parts pa pb))
  | _, _ -> None

(* ======================================================================================
   5. THE SPECIFICATION — what a text DENOTES. Nothing here extracts: it is the meaning the
      theorems hold the clauses above to.

      A digit run is read least-significant-first by `value`, and text order through `rev` by
      `mag`. The number a `parts` record denotes is the rational `numer p / 10^(scale p)`; it is
      carried here as its NUMERATOR AT A SCALE — `denotes p m`, the integer `numer p * 10^(m -
      scale p)` for any `m` at or above the record's own scale — so that two texts are compared
      at a common scale with integers alone, and `denotes_scale` says the choice of `m` reads
      nothing into the number. `scale_up x k` is `x * 10^k` written as `k` multiplications by
      the literal 10, which keeps every arithmetic query LINEAR.
   ====================================================================================== *)

[@@ noextract_to "FSharp"]
let rec value (l: list digit) : Tot nat =
  match l with
  | [] -> 0
  | d :: t -> d + 10 * value t

[@@ noextract_to "FSharp"]
let mag (l: list digit) : Tot nat = value (rev l)

[@@ noextract_to "FSharp"]
let rec scale_up (x: int) (k: nat) : Tot int =
  if k = 0 then x else 10 * scale_up x (k - 1)

[@@ noextract_to "FSharp"]
let pow10 (k: nat) : Tot int = scale_up 1 k

[@@ noextract_to "FSharp"]
let numer (p: dparts) : Tot int =
  if p.negative then 0 - mag (app p.ip p.fp) else mag (app p.ip p.fp)

[@@ noextract_to "FSharp"]
let scale (p: dparts) : Tot nat = len p.fp

[@@ noextract_to "FSharp"]
let denotes (p: dparts) (m: nat{m >= scale p}) : Tot int = scale_up (numer p) (m - scale p)

[@@ noextract_to "FSharp"]
let sign (n: int) : Tot int = if n < 0 then 0 - 1 else if n > 0 then 1 else 0

(* The NORMAL FORM a `parts` record is in: no leading zero on the integer digits, no trailing
   zero on the fraction digits, and no sign on zero. It is what `render` writes canonical text
   from, and `parts_normal` says every record `parts` returns is in it. *)
[@@ noextract_to "FSharp"]
let normal_ip (l: list digit) : Tot bool =
  match l with
  | [] -> true
  | d :: _ -> d <> 0

[@@ noextract_to "FSharp"]
let rec last_nonzero (l: list digit) : Tot bool =
  match l with
  | [] -> true
  | [d] -> d <> 0
  | _ :: t -> last_nonzero t

[@@ noextract_to "FSharp"]
let normal (p: dparts) : Tot bool =
  normal_ip p.ip && last_nonzero p.fp && (not p.negative || Cons? p.ip || Cons? p.fp)

(* ---- 5a. `scale_up` ---- *)

let rec scale_up_zero (k: nat) : Lemma (ensures scale_up 0 k == 0) =
  if k = 0 then () else scale_up_zero (k - 1)

let rec scale_up_add (x: int) (a b: nat)
  : Lemma (ensures scale_up x (a + b) == scale_up (scale_up x a) b) (decreases b) =
  if b = 0 then () else scale_up_add x a (b - 1)

let rec scale_up_plus (x y: int) (k: nat)
  : Lemma (ensures scale_up (x + y) k == scale_up x k + scale_up y k) =
  if k = 0 then () else scale_up_plus x y (k - 1)

let rec scale_up_minus (x y: int) (k: nat)
  : Lemma (ensures scale_up (x - y) k == scale_up x k - scale_up y k) =
  if k = 0 then () else scale_up_minus x y (k - 1)

let rec scale_up_lt (x y: int) (k: nat)
  : Lemma (ensures (scale_up x k < scale_up y k <==> x < y)) =
  if k = 0 then () else scale_up_lt x y (k - 1)

let scale_up_le (x y: int) (k: nat)
  : Lemma (ensures (scale_up x k <= scale_up y k <==> x <= y)) =
  scale_up_lt x y k; scale_up_lt y x k

let scale_up_inj (x y: int) (k: nat)
  : Lemma (requires scale_up x k == scale_up y k) (ensures x == y) =
  scale_up_lt x y k; scale_up_lt y x k

let scale_up_sign (x: int) (k: nat)
  : Lemma (ensures sign (scale_up x k) == sign x) =
  scale_up_zero k; scale_up_lt x 0 k; scale_up_lt 0 x k

let pow10_pos (k: nat) : Lemma (ensures pow10 k >= 1) = scale_up_lt 0 1 k; scale_up_zero k

let rec pow10_mono (a b: nat)
  : Lemma (requires a <= b) (ensures pow10 a <= pow10 b) (decreases b) =
  if a = b then () else (pow10_mono a (b - 1); pow10_pos (b - 1))

(* `x <> 0` scaled by `k` is at least `10^k` away from zero. *)
let rec scale_up_abs (x: int) (k: nat)
  : Lemma (requires x <> 0)
          (ensures (x > 0 ==> scale_up x k >= pow10 k) /\ (x < 0 ==> scale_up x k <= 0 - pow10 k)) =
  if k = 0 then () else scale_up_abs x (k - 1)

let rec scale_up_mono (x y: int) (k: nat)
  : Lemma (requires x <= y) (ensures scale_up x k <= scale_up y k) =
  if k = 0 then () else scale_up_mono x y (k - 1)

(* ---- 5b. the list facts ---- *)

let rec app_nil (#a:Type0) (l: list a) : Lemma (ensures app l [] == l) [SMTPat (app l [])] =
  match l with
  | [] -> ()
  | _ :: t -> app_nil t

let rec app_assoc (#a:Type0) (l m n: list a)
  : Lemma (ensures app (app l m) n == app l (app m n)) =
  match l with
  | [] -> ()
  | _ :: t -> app_assoc t m n

let rec rev_app (#a:Type0) (l m: list a)
  : Lemma (ensures rev (app l m) == app (rev m) (rev l)) =
  match l with
  | [] -> ()
  | x :: t -> rev_app t m; app_assoc (rev m) (rev t) [x]

let rec rev_rev (#a:Type0) (l: list a) : Lemma (ensures rev (rev l) == l) =
  match l with
  | [] -> ()
  | x :: t -> rev_rev t; rev_app (rev t) [x]

let rec zeros_app (a b: nat) : Lemma (ensures app (zeros a) (zeros b) == zeros (a + b)) =
  if a = 0 then () else zeros_app (a - 1) b

let rec rev_zeros (k: nat) : Lemma (ensures rev (zeros k) == zeros k) =
  if k = 0 then () else (rev_zeros (k - 1); zeros_app (k - 1) 1)

let rec take_drop (#a:Type0) (n: nat) (l: list a)
  : Lemma (ensures app (take n l) (drop n l) == l) =
  if n = 0 then ()
  else (match l with
        | [] -> ()
        | _ :: t -> take_drop (n - 1) t)

let rec drop_len (#a:Type0) (n: nat) (l: list a)
  : Lemma (requires n <= len l) (ensures len (drop n l) == len l - n) =
  if n = 0 then ()
  else (match l with
        | [] -> ()
        | _ :: t -> drop_len (n - 1) t)

(* ---- 5c. `value` and `mag` ---- *)

let rec value_bound (l: list digit) : Lemma (ensures value l < pow10 (len l)) =
  match l with
  | [] -> ()
  | _ :: t -> value_bound t

let rec value_app (l m: list digit)
  : Lemma (ensures value (app l m) == value l + scale_up (value m) (len l)) =
  match l with
  | [] -> ()
  | _ :: t -> value_app t m

let rec value_zeros (k: nat) : Lemma (ensures value (zeros k) == 0) =
  if k = 0 then () else value_zeros (k - 1)

let value_app_zeros (l: list digit) (k: nat) : Lemma (ensures value (app l (zeros k)) == value l) =
  value_app l (zeros k); value_zeros k; scale_up_zero (len l)

let value_zeros_app (l: list digit) (k: nat)
  : Lemma (ensures value (app (zeros k) l) == scale_up (value l) k) =
  value_app (zeros k) l; value_zeros k

let rec value_inj (a b: list digit)
  : Lemma (requires len a == len b /\ value a == value b) (ensures a == b) =
  match a, b with
  | x :: xs, y :: ys -> value_inj xs ys
  | _, _ -> ()

(* A run ending in a non-zero digit is at least `10^(len - 1)`. *)
let value_snoc_pos (l: list digit) (d: digit)
  : Lemma (requires d >= 1) (ensures value (app l [d]) >= pow10 (len l)) =
  value_app l [d]; scale_up_mono 1 d (len l)

let mag_zeros_app (l: list digit) (k: nat) : Lemma (ensures mag (app (zeros k) l) == mag l) =
  rev_app (zeros k) l; rev_zeros k; value_app_zeros (rev l) k

let mag_app_zeros (l: list digit) (k: nat)
  : Lemma (ensures mag (app l (zeros k)) == scale_up (mag l) k) =
  rev_app l (zeros k); rev_zeros k; value_zeros_app (rev l) k

let mag_app (l m: list digit)
  : Lemma (ensures mag (app l m) == mag m + scale_up (mag l) (len m)) =
  rev_app l m; value_app (rev m) (rev l)

let mag_pad_left (l: list digit) (n: nat) : Lemma (ensures mag (pad_left l n) == mag l) =
  if len l >= n then () else mag_zeros_app l (n - len l)

let mag_pad_right_app (ip fp: list digit) (sc: nat)
  : Lemma (requires sc >= len fp)
          (ensures mag (app ip (pad_right fp sc)) == scale_up (mag (app ip fp)) (sc - len fp)) =
  if len fp >= sc then ()
  else (app_assoc ip fp (zeros (sc - len fp)); mag_app_zeros (app ip fp) (sc - len fp))

let mag_bound (l: list digit) : Lemma (ensures mag l < pow10 (len l)) = value_bound (rev l)

(* ---- 5d. the trims ---- *)

let rec trim_start_split (l: list digit)
  : Lemma (ensures len (trim_start l) <= len l /\
                   l == app (zeros (len l - len (trim_start l))) (trim_start l) /\
                   normal_ip (trim_start l)) =
  match l with
  | [] -> ()
  | d :: t -> if d = 0 then trim_start_split t else ()

let trim_start_normal (l: list digit)
  : Lemma (requires normal_ip l) (ensures trim_start l == l) = ()

let rec trim_end_split (l: list digit)
  : Lemma (ensures len (trim_end l) <= len l /\
                   l == app (trim_end l) (zeros (len l - len (trim_end l))) /\
                   last_nonzero (trim_end l)) =
  match l with
  | [] -> ()
  | d :: t -> trim_end_split t

let rec trim_end_len (l: list digit)
  : Lemma (ensures len (trim_end l) <= len l) [SMTPat (len (trim_end l))] =
  match l with
  | [] -> ()
  | _ :: t -> trim_end_len t

let rec trim_end_normal (l: list digit)
  : Lemma (requires last_nonzero l) (ensures trim_end l == l) =
  match l with
  | [] -> ()
  | [_] -> ()
  | _ :: t -> trim_end_normal t

let mag_app_trim_start (ip fp: list digit)
  : Lemma (ensures mag (app (trim_start ip) fp) == mag (app ip fp)) =
  trim_start_split ip;
  let k = len ip - len (trim_start ip) in
  app_assoc (zeros k) (trim_start ip) fp;
  mag_zeros_app (app (trim_start ip) fp) k

let mag_app_trim_end (ip fp: list digit)
  : Lemma (ensures mag (app ip fp) == scale_up (mag (app ip (trim_end fp))) (len fp - len (trim_end fp))) =
  trim_end_split fp;
  let k = len fp - len (trim_end fp) in
  app_assoc ip (trim_end fp) (zeros k);
  mag_app_zeros (app ip (trim_end fp)) k

(* The head of the reversed run is its last digit. *)
let rec rev_last (l: list digit)
  : Lemma (requires Cons? l /\ last_nonzero l)
          (ensures (match rev l with
                    | d :: _ -> d <> 0
                    | [] -> False)) =
  match l with
  | [_] -> ()
  | _ :: t -> rev_last t

let mag_normal_pos (p: dparts)
  : Lemma (requires normal p /\ (Cons? p.ip \/ Cons? p.fp)) (ensures mag (app p.ip p.fp) >= 1) =
  if Cons? p.fp then (rev_last p.fp; rev_app p.ip p.fp)
  else (match p.ip with
        | d :: t -> value_snoc_pos (rev t) d; pow10_pos (len t))

let numer_sign (p: dparts)
  : Lemma (requires normal p)
          (ensures (numer p == 0 <==> (Nil? p.ip /\ Nil? p.fp)) /\
                   (p.negative ==> numer p < 0) /\
                   (not p.negative ==> numer p >= 0)) =
  if Cons? p.ip || Cons? p.fp then mag_normal_pos p else ()

let normal_ip_inj (a b: list digit)
  : Lemma (requires normal_ip a /\ normal_ip b /\ mag a == mag b) (ensures a == b) =
  match a, b with
  | [], [] -> ()
  | [], d :: t -> value_snoc_pos (rev t) d; pow10_pos (len t)
  | d :: t, [] -> value_snoc_pos (rev t) d; pow10_pos (len t)
  | d :: t, e :: u ->
      value_snoc_pos (rev t) d; value_snoc_pos (rev u) e;
      mag_bound a; mag_bound b;
      if len t < len u then pow10_mono (len a) (len u)
      else if len u < len t then pow10_mono (len b) (len t)
      else (value_inj (rev a) (rev b); rev_rev a; rev_rev b)

(* ======================================================================================
   6. CANONICAL FORM — `parts` reads a normal record, `render` writes text `parts` reads back
      to that record, and a normal record is DETERMINED by the number it denotes.
   ====================================================================================== *)

let rec of_digits_all (l: list digit)
  : Lemma (ensures all_digits (of_digits l) /\ len (of_digits l) == len l) =
  match l with
  | [] -> ()
  | _ :: t -> of_digits_all t

let rec to_of_digits (l: list digit) : Lemma (ensures to_digits (of_digits l) == l) =
  match l with
  | [] -> ()
  | _ :: t -> to_of_digits t

let rec split_dot_digits (d: list sym)
  : Lemma (requires all_digits d) (ensures None? (split_dot d)) =
  match d with
  | [] -> ()
  | _ :: t -> split_dot_digits t

let rec split_dot_digits_dot (d fp: list sym)
  : Lemma (requires all_digits d) (ensures split_dot (app d (Dot :: fp)) == Some (d, fp)) =
  match d with
  | [] -> ()
  | _ :: t -> split_dot_digits_dot t fp

(* THEOREM (canonical form, half 1): every record `parts` returns is normal. *)
let parts_normal (s: list sym) (p: dparts)
  : Lemma (requires parts s == Some p) (ensures normal p) =
  match s with
  | c0 :: rest ->
      let body = if c0 = Minus then rest else s in
      (match split_dot body with
       | None -> trim_start_split (to_digits body)
       | Some (ip0, fp0) -> trim_start_split (to_digits ip0); trim_end_split (to_digits fp0))

(* THEOREM (canonical form, half 2): `parts` reads `render p` back to `p` for every normal `p`
   — the rendered text is in the grammar and the trims are the identity on it. *)
let render_parts (p: dparts)
  : Lemma (requires normal p) (ensures parts (render p) == Some p) =
  let ipd = if Nil? p.ip then [0] else p.ip in
  let ipS = of_digits ipd in
  of_digits_all ipd; to_of_digits ipd; of_digits_all p.fp; to_of_digits p.fp;
  trim_start_normal p.ip; trim_end_normal p.fp;
  if Nil? p.fp then (app_nil ipS; split_dot_digits ipS)
  else split_dot_digits_dot ipS (of_digits p.fp)

(* THEOREM `canonical_idempotent` — `tryCanonical` is idempotent: what it writes, it reads back
   to the same text. *)
let canonical_idempotent (s c: list sym)
  : Lemma (requires try_canonical s == Some c) (ensures try_canonical c == Some c) =
  match parts s with
  | Some p -> parts_normal s p; render_parts p

(* `render` is injective on normal records — two canonical texts that agree are one record. *)
let render_injective (p q: dparts)
  : Lemma (requires normal p /\ normal q /\ render p == render q) (ensures p == q) =
  render_parts p; render_parts q

(* The common scale two records are compared at. *)
[@@ noextract_to "FSharp"]
let scale_witness (p q: dparts) : Tot (m: nat{m >= scale p /\ m >= scale q}) = max (scale p) (scale q)

(* The fraction digits of a normal record carry no trailing zero, so its magnitude is NOT a
   multiple of ten when the fraction is non-empty. *)
let numer_not_multiple (p: dparts)
  : Lemma (requires normal p /\ Cons? p.fp)
          (ensures (match rev (app p.ip p.fp) with
                    | d :: _ -> d <> 0
                    | [] -> False)) =
  rev_last p.fp; rev_app p.ip p.fp

let mag_eq_normal (p q: dparts)
  : Lemma (requires normal p /\ normal q /\ scale p == scale q /\
                    mag (app p.ip p.fp) == mag (app q.ip q.fp))
          (ensures p.ip == q.ip /\ p.fp == q.fp) =
  mag_app p.ip p.fp; mag_app q.ip q.fp;
  mag_bound p.fp; mag_bound q.fp;
  scale_up_minus (mag p.ip) (mag q.ip) (scale p);
  (if mag p.ip <> mag q.ip then scale_up_abs (mag p.ip - mag q.ip) (scale p));
  normal_ip_inj p.ip q.ip;
  value_inj (rev p.fp) (rev q.fp); rev_rev p.fp; rev_rev q.fp

let scaled_not_multiple (p: dparts) (x: nat) (k: nat)
  : Lemma (requires normal p /\ Cons? p.fp /\ k >= 1)
          (ensures scale_up x k <> mag (app p.ip p.fp)) =
  numer_not_multiple p; scale_up_add x (k - 1) 1

(* THEOREM `normal_injective` — one number, one normal record. *)
let normal_injective (p q: dparts)
  : Lemma (requires normal p /\ normal q /\
                    denotes p (scale_witness p q) == denotes q (scale_witness p q))
          (ensures p == q) =
  let m = scale_witness p q in
  numer_sign p; numer_sign q;
  scale_up_sign (numer p) (m - scale p); scale_up_sign (numer q) (m - scale q);
  if numer p = 0 then ()
  else begin
    let mp = mag (app p.ip p.fp) in
    let mq = mag (app q.ip q.fp) in
    scale_up_minus 0 mp (m - scale p); scale_up_zero (m - scale p);
    scale_up_minus 0 mq (m - scale q); scale_up_zero (m - scale q);
    assert (scale_up mp (m - scale p) == scale_up mq (m - scale q));
    if scale p < scale q then scaled_not_multiple q mp (m - scale p)
    else if scale q < scale p then scaled_not_multiple p mq (m - scale q)
    else mag_eq_normal p q
  end

(* THEOREM `same_number_iff_canonical` — two accepted texts denote one number if and only if
   their canonical forms are one text (D72 K3, proved). *)
let same_number_iff_canonical (a b: list sym) (pa pb: dparts)
  : Lemma (requires parts a == Some pa /\ parts b == Some pb)
          (ensures (denotes pa (scale_witness pa pb) == denotes pb (scale_witness pa pb) <==>
                    try_canonical a == try_canonical b)) =
  parts_normal a pa; parts_normal b pb;
  FStar.Classical.move_requires (normal_injective pa) pb;
  FStar.Classical.move_requires (render_injective pa) pb

(* THEOREM `zero_unique` — zero has exactly one canonical text: an accepted text that denotes
   zero canonicalises to `zero_text`, and `zero_text` reads as the zero record. *)
let zero_unique (s: list sym) (p: dparts)
  : Lemma (requires parts s == Some p /\ numer p == 0) (ensures try_canonical s == Some zero_text) =
  parts_normal s p; numer_sign p

let zero_text_parts () : Lemma (ensures parts zero_text == Some ({ negative = false; ip = []; fp = [] })) = ()

(* ======================================================================================
   7. THE ALIGNED MAGNITUDES, THE ORDER AND THE SUM — what `aligned`, `lex`, `addMagnitudes`
      and `subMagnitudes` compute, in terms of `denotes`.
   ====================================================================================== *)

(* `denotes` reads nothing into the scale it is taken at: a higher scale is the same numerator
   scaled up, so a claim at one common scale is a claim at every scale above it. *)
let denotes_scale (p: dparts) (m: nat{m >= scale p}) (m': nat{m' >= m})
  : Lemma (ensures denotes p m' == scale_up (denotes p m) (m' - m)) =
  scale_up_add (numer p) (m - scale p) (m' - m)

(* `aligned` returns two equal-width magnitudes at the common scale, and each is the absolute
   value of its record's numerator at that scale. *)
let aligned_spec (a b: dparts)
  : Lemma (ensures (let (ma, mb, sc) = aligned a b in
                    len ma == len mb /\ sc == scale_witness a b /\
                    denotes a sc == (if a.negative then 0 - mag ma else mag ma) /\
                    denotes b sc == (if b.negative then 0 - mag mb else mag mb))) =
  let sc = max (len a.fp) (len b.fp) in
  let ma0 = app a.ip (pad_right a.fp sc) in
  let mb0 = app b.ip (pad_right b.fp sc) in
  let width = max (len ma0) (len mb0) in
  mag_pad_left ma0 width; mag_pad_left mb0 width;
  mag_pad_right_app a.ip a.fp sc; mag_pad_right_app b.ip b.fp sc;
  scale_up_minus 0 (mag (app a.ip a.fp)) (sc - len a.fp); scale_up_zero (sc - len a.fp);
  scale_up_minus 0 (mag (app b.ip b.fp)) (sc - len b.fp); scale_up_zero (sc - len b.fp)

(* ---- 7a. the ordinal order of equal-width digit runs is the order of their values ---- *)

[@@ noextract_to "FSharp"]
let rec cmp_lsb (a b: list digit) : Tot int =
  match a, b with
  | x :: xs, y :: ys ->
      let r = cmp_lsb xs ys in
      if r <> 0 then r else if x < y then 0 - 1 else if x > y then 1 else 0
  | _, _ -> 0

let rec cmp_lsb_sign (a b: list digit)
  : Lemma (requires len a == len b) (ensures cmp_lsb a b == sign (value a - value b)) =
  match a, b with
  | x :: xs, y :: ys -> cmp_lsb_sign xs ys
  | _, _ -> ()

let rec cmp_lsb_snoc (l l': list digit) (x y: digit)
  : Lemma (requires len l == len l')
          (ensures cmp_lsb (app l [x]) (app l' [y]) ==
                   (if x < y then 0 - 1 else if x > y then 1 else cmp_lsb l l')) =
  match l, l' with
  | a :: t, b :: t' -> cmp_lsb_snoc t t' x y
  | _, _ -> ()

let rec lex_rev (a b: list digit)
  : Lemma (requires len a == len b) (ensures lex a b == cmp_lsb (rev a) (rev b)) =
  match a, b with
  | x :: xs, y :: ys -> lex_rev xs ys; cmp_lsb_snoc (rev xs) (rev ys) x y
  | _, _ -> ()

(* THEOREM `lex_sign` — `CompareOrdinal` over two equal-width digit strings is the sign of the
   difference of the numbers they write. *)
let lex_sign (a b: list digit)
  : Lemma (requires len a == len b) (ensures lex a b == sign (mag a - mag b)) =
  lex_rev a b; cmp_lsb_sign (rev a) (rev b)

(* ---- 7b. the digit-wise sum and difference ---- *)

let rec add_lsb_value (a: list digit) (b: list digit{len b = len a}) (c: nat{c <= 1})
  : Lemma (ensures value (add_lsb a b c) == value a + value b + c) (decreases a) =
  match a, b with
  | x :: xs, y :: ys ->
      let (_, c') = add_digit x y c in
      add_lsb_value xs ys c'
  | _, _ -> ()

let rec sub_lsb_value (a: list digit) (b: list digit{len b = len a}) (br: nat{br <= 1})
  : Lemma (ensures (let (r, out) = sub_lsb a b br in
                    out <= 1 /\ len r == len a /\
                    value r == value a - value b - br + (if out = 1 then pow10 (len a) else 0)))
          (decreases a) =
  match a, b with
  | x :: xs, y :: ys ->
      let (_, br') = sub_digit x y br in
      sub_lsb_value xs ys br'
  | _, _ -> ()

(* THEOREM `add_magnitudes_mag` — the digit-wise sum writes the sum. *)
let add_magnitudes_mag (a: list digit) (b: list digit{len b = len a})
  : Lemma (ensures mag (add_magnitudes a b) == mag a + mag b) =
  add_lsb_value (rev a) (rev b) 0; rev_rev (add_lsb (rev a) (rev b) 0)

(* THEOREM `sub_magnitudes_mag` — the digit-wise difference writes the difference, over `a >= b`,
   which is the only way production calls it; the borrow it discards is zero there. *)
let sub_magnitudes_mag (a: list digit) (b: list digit{len b = len a})
  : Lemma (requires mag a >= mag b) (ensures mag (sub_magnitudes a b) == mag a - mag b) =
  sub_lsb_value (rev a) (rev b) 0;
  let (r, _) = sub_lsb (rev a) (rev b) 0 in
  value_bound r; rev_rev r

(* ======================================================================================
   8. ORDER — `compare` is the sign of the difference of the numbers denoted, and so a total
      order that answers 0 exactly where the canonical forms agree.
   ====================================================================================== *)

(* THEOREM `compare_denotes` — `compare` answers the sign of `a - b`, read at the common scale. *)
let compare_parts_denotes (pa pb: dparts)
  : Lemma (requires normal pa /\ normal pb)
          (ensures compare_parts pa pb ==
                   sign (denotes pa (scale_witness pa pb) - denotes pb (scale_witness pa pb))) =
  aligned_spec pa pb;
  numer_sign pa; numer_sign pb;
  let m = scale_witness pa pb in
  scale_up_sign (numer pa) (m - scale pa); scale_up_sign (numer pb) (m - scale pb);
  let (ma, mb, _) = aligned pa pb in
  lex_sign ma mb

let compare_denotes (a b: list sym) (pa pb: dparts)
  : Lemma (requires parts a == Some pa /\ parts b == Some pb)
          (ensures compare a b ==
                   Some (sign (denotes pa (scale_witness pa pb) - denotes pb (scale_witness pa pb)))) =
  parts_normal a pa; parts_normal b pb; compare_parts_denotes pa pb

(* THEOREM `compare_zero_iff_canonical` — `compare` answers 0 exactly where the canonical forms
   are one text. *)
let compare_zero_iff_canonical (a b: list sym) (pa pb: dparts)
  : Lemma (requires parts a == Some pa /\ parts b == Some pb)
          (ensures (compare a b == Some 0 <==> try_canonical a == try_canonical b)) =
  compare_denotes a b pa pb; same_number_iff_canonical a b pa pb

(* THEOREM `compare_antisymmetric` — reversing the arguments negates the answer. *)
let compare_antisymmetric (a b: list sym) (pa pb: dparts) (r: int)
  : Lemma (requires parts a == Some pa /\ parts b == Some pb /\ compare a b == Some r)
          (ensures compare b a == Some (0 - r)) =
  compare_denotes a b pa pb; compare_denotes b a pb pa

(* THEOREM `compare_transitive` — `a < b` and `b < c` give `a < c`, through a three-way common
   scale. *)
let compare_transitive (a b c: list sym) (pa pb pc: dparts)
  : Lemma (requires parts a == Some pa /\ parts b == Some pb /\ parts c == Some pc /\
                    compare a b == Some (0 - 1) /\ compare b c == Some (0 - 1))
          (ensures compare a c == Some (0 - 1)) =
  compare_denotes a b pa pb; compare_denotes b c pb pc; compare_denotes a c pa pc;
  let m = max (scale pa) (max (scale pb) (scale pc)) in
  let mab = scale_witness pa pb in
  let mbc = scale_witness pb pc in
  let mac = scale_witness pa pc in
  denotes_scale pa mab m; denotes_scale pb mab m;
  denotes_scale pb mbc m; denotes_scale pc mbc m;
  denotes_scale pa mac m; denotes_scale pc mac m;
  scale_up_lt (denotes pa mab) (denotes pb mab) (m - mab);
  scale_up_lt (denotes pb mbc) (denotes pc mbc) (m - mbc);
  scale_up_lt (denotes pa mac) (denotes pc mac) (m - mac)

(* ======================================================================================
   9. SUM — `add` denotes the sum, always answers canonical text, and is commutative and
      associative with zero as its identity BECAUSE of that: a canonical text is determined by
      the number it denotes (`normal_injective`), so two sums that denote one number are one text.
   ====================================================================================== *)

(* THEOREM `add_parts_denotes` — the record `add` renders is normal, sits at or below the common
   scale, and denotes the sum at that scale. *)
let add_parts_denotes (pa pb: dparts)
  : Lemma (requires normal pa /\ normal pb)
          (ensures (let pc = add_parts pa pb in
                    let m = scale_witness pa pb in
                    normal pc /\ scale pc <= m /\
                    denotes pc m == denotes pa m + denotes pb m)) =
  aligned_spec pa pb;
  let (ma, mb, sc) = aligned pa pb in
  lex_sign ma mb;
  let (negative, magnitude) =
    if pa.negative = pb.negative then (pa.negative, add_magnitudes ma mb)
    else
      let c = lex ma mb in
      if c = 0 then (false, [])
      else if c > 0 then (pa.negative, sub_magnitudes ma mb)
      else (pb.negative, sub_magnitudes mb ma) in
  (* the magnitude written is |a + b| at the common scale, and its sign is `negative` *)
  (if pa.negative = pb.negative then add_magnitudes_mag ma mb
   else if lex ma mb > 0 then sub_magnitudes_mag ma mb
   else if lex ma mb < 0 then sub_magnitudes_mag mb ma
   else ());
  let m = scale_witness pa pb in
  assert (denotes pa m + denotes pb m == (if negative then 0 - mag magnitude else mag magnitude));
  let magnitude' = pad_left magnitude (sc + 1) in
  mag_pad_left magnitude (sc + 1);
  let ip = take (len magnitude' - sc) magnitude' in
  let fp = drop (len magnitude' - sc) magnitude' in
  take_drop (len magnitude' - sc) magnitude';
  drop_len (len magnitude' - sc) magnitude';
  let ip' = trim_start ip in
  let fp' = trim_end fp in
  trim_start_split ip; trim_end_split fp;
  mag_app_trim_start ip fp;
  mag_app_trim_end ip' fp;
  let pc = { negative = negative && not (Nil? ip' && Nil? fp'); ip = ip'; fp = fp' } in
  assert (scale_up (mag (app ip' fp')) (sc - len fp') == mag magnitude);
  (if Nil? ip' && Nil? fp' then ()
   else (mag_normal_pos pc; scale_up_sign (mag (app ip' fp')) (sc - len fp')));
  scale_up_minus 0 (mag (app ip' fp')) (sc - len fp'); scale_up_zero (sc - len fp')

let add_denotes (a b: list sym) (pa pb: dparts)
  : Lemma (requires parts a == Some pa /\ parts b == Some pb)
          (ensures (let pc = add_parts pa pb in
                    let m = scale_witness pa pb in
                    normal pc /\ add a b == Some (render pc) /\ parts (render pc) == Some pc /\
                    scale pc <= m /\ denotes pc m == denotes pa m + denotes pb m)) =
  parts_normal a pa; parts_normal b pb; add_parts_denotes pa pb; render_parts (add_parts pa pb)

(* THEOREM `add_canonical` — `add` always answers canonical text. *)
let add_canonical (a b c: list sym)
  : Lemma (requires add a b == Some c) (ensures is_canonical c) =
  match parts a, parts b with
  | Some pa, Some pb -> add_denotes a b pa pb

(* Two normal records that denote one number at ANY common scale are one record — the scale
   can be cancelled down to the records' own witness. *)
let normal_injective_at (p q: dparts) (m: nat{m >= scale p /\ m >= scale q})
  : Lemma (requires normal p /\ normal q /\ denotes p m == denotes q m) (ensures p == q) =
  let w = scale_witness p q in
  denotes_scale p w m; denotes_scale q w m;
  scale_up_inj (denotes p w) (denotes q w) (m - w);
  normal_injective p q

(* THEOREM `add_commutative`. *)
let add_commutative (a b: list sym)
  : Lemma (ensures add a b == add b a) =
  match parts a, parts b with
  | Some pa, Some pb ->
      add_denotes a b pa pb; add_denotes b a pb pa;
      normal_injective_at (add_parts pa pb) (add_parts pb pa) (scale_witness pa pb)
  | _, _ -> ()

(* THEOREM `add_associative` — over three accepted texts, `(a + b) + c` and `a + (b + c)` are one
   text; each inner sum is accepted (it is canonical), and the two outer sums denote one number
   at the three-way common scale. *)
let add_associative (a b c: list sym) (pa pb pc: dparts)
  : Lemma (requires parts a == Some pa /\ parts b == Some pb /\ parts c == Some pc)
          (ensures (match add a b, add b c with
                    | Some ab, Some bc -> add ab c == add a bc
                    | _, _ -> False)) =
  add_denotes a b pa pb; add_denotes b c pb pc;
  let pab = add_parts pa pb in
  let pbc = add_parts pb pc in
  let ab = render pab in
  let bc = render pbc in
  add_denotes ab c pab pc; add_denotes a bc pa pbc;
  let pabc = add_parts pab pc in
  let pabc' = add_parts pa pbc in
  let m = max (scale pa) (max (scale pb) (scale pc)) in
  let mab = scale_witness pa pb in
  let mbc = scale_witness pb pc in
  let mabc = scale_witness pab pc in
  let mabc' = scale_witness pa pbc in
  denotes_scale pa mab m; denotes_scale pb mab m; denotes_scale pab mab m;
  scale_up_plus (denotes pa mab) (denotes pb mab) (m - mab);
  denotes_scale pb mbc m; denotes_scale pc mbc m; denotes_scale pbc mbc m;
  scale_up_plus (denotes pb mbc) (denotes pc mbc) (m - mbc);
  denotes_scale pab mabc m; denotes_scale pc mabc m; denotes_scale pabc mabc m;
  scale_up_plus (denotes pab mabc) (denotes pc mabc) (m - mabc);
  denotes_scale pa mabc' m; denotes_scale pbc mabc' m; denotes_scale pabc' mabc' m;
  scale_up_plus (denotes pa mabc') (denotes pbc mabc') (m - mabc');
  normal_injective_at pabc pabc' m

(* THEOREM `add_zero_identity` — adding zero canonicalises and nothing else. *)
let add_zero_identity (a: list sym) (pa: dparts)
  : Lemma (requires parts a == Some pa) (ensures add a zero_text == try_canonical a) =
  zero_text_parts ();
  let pz = { negative = false; ip = []; fp = [] } in
  parts_normal a pa;
  add_denotes a zero_text pa pz;
  assert (scale pz == 0);
  assert (scale_witness pa pz == scale pa);
  assert (scale (add_parts pa pz) <= scale pa);
  scale_up_zero (scale pa);
  normal_injective_at (add_parts pa pz) pa (scale pa)

(* ======================================================================================
   10. REFUSAL — the accepted set is exactly the grammar `-?[0-9]+(\.[0-9]+)?` (D72 K4), and
       every function answers `None` exactly off it.
   ====================================================================================== *)

(* The grammar, as a recogniser over the symbols: `[0-9]+` ... *)
[@@ noextract_to "FSharp"]
let rec frac (s: list sym) : Tot bool =
  match s with
  | [Digit _] -> true
  | Digit _ :: t -> frac t
  | _ -> false

(* ... `[0-9]+(\.[0-9]+)?` ... *)
[@@ noextract_to "FSharp"]
let rec intpart (s: list sym) : Tot bool =
  match s with
  | [Digit _] -> true
  | Digit _ :: Dot :: t -> frac t
  | Digit _ :: t -> intpart t
  | _ -> false

(* ... and `-?[0-9]+(\.[0-9]+)?`. *)
[@@ noextract_to "FSharp"]
let grammar (s: list sym) : Tot bool =
  match s with
  | Minus :: t -> intpart t
  | _ -> intpart s

let rec frac_is_digits (s: list sym) : Lemma (ensures (frac s <==> is_digits s)) =
  match s with
  | Digit _ :: t -> frac_is_digits t
  | _ -> ()

let rec intpart_split (s: list sym)
  : Lemma (ensures (intpart s <==> (match split_dot s with
                                   | None -> is_digits s
                                   | Some (a, b) -> is_digits a && is_digits b))) =
  match s with
  | Digit _ :: Dot :: t -> frac_is_digits t
  | Digit _ :: t -> intpart_split t
  | _ -> ()

(* THEOREM `parts_iff_grammar` — `parts` accepts exactly the grammar. *)
let parts_iff_grammar (s: list sym) : Lemma (ensures (Some? (parts s) <==> grammar s)) =
  match s with
  | Minus :: t -> intpart_split t
  | _ -> intpart_split s

(* THEOREM `refusal_exact` — each function answers `None` exactly off the grammar. *)
let refusal_exact (a b: list sym)
  : Lemma (ensures (None? (try_canonical a) <==> not (grammar a)) /\
                   (None? (compare a b) <==> not (grammar a && grammar b)) /\
                   (None? (add a b) <==> not (grammar a && grammar b))) =
  parts_iff_grammar a; parts_iff_grammar b

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
  { tname = "try-canonical-trims-a-negative";
    tholds = (fun () ->
      try_canonical [ Minus; Digit 0; Digit 0; Dot; Digit 5; Digit 0 ] = Some [ Minus; Digit 0; Dot; Digit 5 ]) };
  { tname = "add-carries-across-the-point";
    tholds = (fun () -> add [ Digit 1; Dot; Digit 5 ] [ Digit 2; Dot; Digit 5 ] = Some [ Digit 4 ]) };
  { tname = "compare-orders-numerically";
    tholds = (fun () -> compare [ Digit 1; Dot; Digit 5 ] [ Digit 2 ] = Some (-1)) } ]

let _ = assert_norm (twins_hold twins == true)
