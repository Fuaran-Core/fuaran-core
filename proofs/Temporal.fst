(*
   Temporal — an F* model of the TEMPORAL ENCODINGS (fuaran-core Phase 430, the model Phase 419
   deferred and Phase 422 introduced): `src/Fuaran.Core.Column/TemporalText.fs` clause for clause,
   with the laws the column suite samples stated as theorems.

   WHAT IS MODELLED. `TimeUnit` and its module (`digits`, `scale`, `ofDigits`, `widens`); the
   calendar in integers — `TemporalText.daysOfCivil` / `civilOfDays` (Hinnant's days-from-civil and
   civil-from-days, era-shifted exactly as production shifts them), `isLeap`, `daysIn`, `MinDay` /
   `MaxDay`, `isDayInRange`; the two canonical texts and their readers — `tryDays` / `dateText` for a
   date, `tryInstant` / `instantText` / `unitOf` / `isCanonicalTimestamp` for an instant, over the
   private `digitsAt`, `datePart`, `fractionPart`, `parseInstant`, `pad` and `pow10`; the range check
   `isInstantInRange`; and `compareInstants`, THE order over timestamp text that `Cell.compare` is.

   THE ALPHABET. A text is a `list ch`, the character alphabet `WireCanon.fst` models the canonical
   encoder over, because the column models (`WireColumn.fst`, `ColumnRefinement.fst`) hold a cell's
   text in it: the decimal digits are its sixteen hex characters' first ten, `-` `:` `.` are its
   punctuation, and `T` and `Z` are `CPlain` letters. A date or an instant is read SEQUENTIALLY —
   `read_digits` consumes a run of digits and answers what follows — where production indexes a
   string; the two agree because every index production reads is the position the sequential read
   has reached, and the length checks production makes (`Length <> 10`, `Length < 20`, `20`, `22`
   to `30`) are the shape checks the sequential read makes (nothing follows the day; `T` follows
   the day; `Z` follows the second, or a point, one to nine digits and `Z`).

   THE INTEGERS. F*'s unbounded `int`, where production holds a day count in an `int32`, an
   epoch second in an integer-valued `float` and a fraction in an `int32`. Production's seconds are
   floats so that a year-9999 second (315 billion) fits a Fable number; here they are integers,
   and `isInstantInRange`'s clause `second = floor second` is the bridge: an integer-valued float IS
   its integer, and a float that is not whole is a value no builder of the column makes and
   `Table.validate` refuses — the oracle host carries whole seconds only. DIVISION: F*'s `/` is
   Euclidean (the floor, for a positive divisor) and the F# backend extracts it to a TRUNCATING
   `/`; production's `int` division truncates too, and production's own design is that every
   dividend is non-negative on the canonical range ("every division is of a non-negative dividend,
   so truncation and floor agree on every host"). This module keeps that design — `days_of_civil`
   and `civil_of_days` are production's shifted forms verbatim — and writes `instantText`'s one
   float floor, `floor (second / 86400.0)`, as the shifted integer form `(second - min_second) / 86400
   + min_day`, which is that floor for every second from `0000-01-01` on and a non-negative dividend
   there. `%` is F*'s Euclidean remainder, which the backend writes as `mod_f`.

   THE THEOREMS (sections 7 to 10).
     1. THE CALENDAR IS A BIJECTION. `civil_of_days` answers a civil date that exists (`civil_valid`:
        a month `1`..`12`, a day `1`..`daysIn`), `days_of_civil` of it is the day count it was asked
        for (`days_civil_days`), and `civil_of_days` of `days_of_civil y m d` is `{y; m; d}` for every
        civil date that exists (`civil_days_civil`). Strictly monotone: a later civil date is a larger
        day count (`days_of_civil_mono`), so the integers order chronologically; and the canonical
        range `0000-01-01`..`9999-12-31` is exactly `min_day`..`max_day` (`year_in_range`).
     2. A DATE HAS ONE TEXT. For every day in range `try_days (date_text d) == Some d`
        (`try_days_date_text`); whatever `try_days` reads back to a day is `date_text` of that day,
        in range (`date_text_try_days`); and `date_text` is canonical exactly on the range
        (`date_text_canonical`).
     3. AN INSTANT HAS ONE TEXT PER UNIT, AND THE SAME TEXT IN EVERY UNIT. For every unit and every
        pair in range `try_instant u (instant_text u s f) == Some (s, f)` (`try_instant_instant_text`);
        whatever `try_instant u` reads back is `instant_text u` of the pair, in range
        (`instant_text_try_instant`); widening a coarser unit into a finer one keeps the text — the
        pair scales by the units' ratio and `instant_text` writes the same characters
        (`widen_keeps_instant`); and `type_of`'s unit, `unit_of`, is the coarsest unit that reads the
        text (`unit_of_coarsest`).
     4. THE ORDER IS CHRONOLOGICAL. For two canonical texts read at one unit, `compare_instants` is
        the order of the pairs, second first then fraction (`compare_instants_chronological`): text
        order that is NOT ordinal string order once fraction lengths vary, proved to be the order of
        the instants it spells.

   PROOF DISCIPLINE. No `assume`, no `admit`; checked by the proof leg at the leg's flags
   (`--z3rlimit 40 --quake 3 --report_assumes error`). The calendar's one hard fact — that
   Hinnant's `yoe` quotient recovers the year — is proved from the monotonicity of the corrected
   quotient and its value at the two ends of every year of an era, those 800 values computed by
   normalisation (`year_ends_ok`): a theorem about 146,097 day counts that the solver never has to
   search.

   THE TWINS (the last section) are the extractor premise sampled at this model — see
   `WireColumn.fst`'s twins section for what they are and why.

   Apache-2.0, like everything beside it.
*)
module Temporal

#set-options "--ext context_pruning"

open WireCanon

(* ======================================================================================
   1. THE UNIT (F#: `TimeUnit`, `TimeUnit.digits` / `scale` / `ofDigits` / `widens`).
   ====================================================================================== *)

type time_unit =
  | Seconds
  | Milliseconds
  | Microseconds
  | Nanoseconds

(* F#: `TimeUnit.digits`. *)
let unit_digits (u: time_unit) : Tot nat =
  match u with
  | Seconds -> 0
  | Milliseconds -> 3
  | Microseconds -> 6
  | Nanoseconds -> 9

(* F#: `TimeUnit.scale`. *)
let unit_scale (u: time_unit) : Tot pos =
  match u with
  | Seconds -> 1
  | Milliseconds -> 1000
  | Microseconds -> 1000000
  | Nanoseconds -> 1000000000

(* F#: `TimeUnit.ofDigits`. *)
let of_digits (d: int) : Tot time_unit =
  if d <= 0 then Seconds
  else if d <= 3 then Milliseconds
  else if d <= 6 then Microseconds
  else Nanoseconds

(* F#: `TimeUnit.widens`. *)
let unit_widens (from target: time_unit) : Tot bool = unit_digits from <= unit_digits target

(* F#: `pow10`. *)
let rec pow10 (k: nat) : Tot pos = if k = 0 then 1 else 10 * pow10 (k - 1)

(* ======================================================================================
   2. THE CALENDAR (F#: `isLeap`, `daysIn`, `daysOfCivil`, `civilOfDays`, `MinDay`, `MaxDay`,
      `isDayInRange`).
   ====================================================================================== *)

(* F#: `isLeap`. Asked of a parsed year, `0`..`9999`. *)
let is_leap (y: int) : Tot bool = y % 4 = 0 && (y % 100 <> 0 || y % 400 = 0)

(* F#: `daysIn`. *)
let days_in (y m: int) : Tot int =
  if m = 2 then (if is_leap y then 29 else 28)
  else if m = 4 || m = 6 || m = 9 || m = 11 then 30
  else 31

(* F#: `daysOfCivil`, line for line. *)
let days_of_civil (y m d: int) : Tot int =
  let y = if m <= 2 then y - 1 else y in
  let y4 = y + 400 in
  let era = y4 / 400 in
  let yoe = y4 - era * 400 in
  let mp = (m + 9) % 12 in
  let doy = (153 * mp + 2) / 5 + d - 1 in
  let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy in
  (era - 1) * 146097 + doe - 719468

(* F#: `CivilDate`. *)
type civil = { year: int; month: int; day: int }

(* F#: `civilOfDays`, line for line. *)
let civil_of_days (days: int) : Tot civil =
  let z = days + 719468 + 146097 in
  let era = z / 146097 in
  let doe = z - era * 146097 in
  let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365 in
  let doy = doe - (365 * yoe + yoe / 4 - yoe / 100) in
  let mp = (5 * doy + 2) / 153 in
  let d = doy - (153 * mp + 2) / 5 + 1 in
  let m = if mp < 10 then mp + 3 else mp - 9 in
  { year = yoe + (era - 1) * 400 + (if m <= 2 then 1 else 0); month = m; day = d }

(* F#: `MinDay` / `MaxDay`. *)
let min_day : int = -719528
let max_day : int = 2932896

(* F#: `isDayInRange`. *)
let is_day_in_range (days: int) : Tot bool = min_day <= days && days <= max_day

(* A civil date that exists: the month and the day in range. What `datePart` admits. *)
let civil_valid (c: civil) : Tot bool =
  1 <= c.month && c.month <= 12 && 1 <= c.day && c.day <= days_in c.year c.month

(* ======================================================================================
   3. DIGITS AND TEXT (F#: `digitsAt`, `pad`, `string n`).
   ====================================================================================== *)

let rec len (#a: Type) (l: list a) : Tot nat =
  match l with
  | [] -> 0
  | _ :: t -> 1 + len t

(* F#: the digit character of `0`..`9`. *)
let dec_ch (n: int) : Tot ch =
  match n with
  | 0 -> CHexCh HD0 | 1 -> CHexCh HD1 | 2 -> CHexCh HD2 | 3 -> CHexCh HD3 | 4 -> CHexCh HD4
  | 5 -> CHexCh HD5 | 6 -> CHexCh HD6 | 7 -> CHexCh HD7 | 8 -> CHexCh HD8 | _ -> CHexCh HD9

(* F#: `c >= '0' && c <= '9'`, with the digit's value. *)
let dec_val (c: ch) : Tot (option int) =
  match c with
  | CHexCh HD0 -> Some 0 | CHexCh HD1 -> Some 1 | CHexCh HD2 -> Some 2 | CHexCh HD3 -> Some 3
  | CHexCh HD4 -> Some 4 | CHexCh HD5 -> Some 5 | CHexCh HD6 -> Some 6 | CHexCh HD7 -> Some 7
  | CHexCh HD8 -> Some 8 | CHexCh HD9 -> Some 9
  | _ -> None

(* F#: `string n` for a non-negative `n` — its decimal digits, no leading zero. *)
let rec nat_text (n: nat) : Tot (list ch) (decreases n) =
  if n < 10 then [dec_ch n] else app (nat_text (n / 10)) [dec_ch (n % 10)]

(* F#: `string n` — a `-` before the digits of a negative. *)
let int_text (n: int) : Tot (list ch) =
  if n < 0 then CMinus :: nat_text (0 - n) else nat_text n

let rec zeros (k: nat) : Tot (list ch) = if k = 0 then [] else CHexCh HD0 :: zeros (k - 1)

(* F#: `pad width n` = `(string n).PadLeft(width, '0')`. *)
let pad (width: nat) (n: int) : Tot (list ch) =
  let s = int_text n in
  if len s >= width then s else app (zeros (width - len s)) s

(* F#: `digitsAt s from count` — `count` digits read in sequence, their value and what follows;
   `None` where a character is not a digit or the text ends first. *)
let rec read_digits (s: list ch) (count: nat) (acc: int)
  : Tot (option (int & list ch)) (decreases count) =
  if count = 0 then Some (acc, s)
  else
    (match s with
     | c :: t ->
         (match dec_val c with
          | Some v -> read_digits t (count - 1) (acc * 10 + v)
          | None -> None)
     | [] -> None)

(* ======================================================================================
   4. DATES (F#: `datePart`, `tryDays`, `isCanonicalDate`, `dateText`).
   ====================================================================================== *)

(* F#: `datePart` — four digits, `-`, two digits, `-`, two digits, the month and the day naming
   a day that exists; and what follows the ten characters. *)
let date_part (s: list ch) : Tot (option (int & int & int & list ch)) =
  match read_digits s 4 0 with
  | None -> None
  | Some (y, r1) ->
      (match r1 with
       | CMinus :: r2 ->
           (match read_digits r2 2 0 with
            | None -> None
            | Some (m, r3) ->
                (match r3 with
                 | CMinus :: r4 ->
                     (match read_digits r4 2 0 with
                      | None -> None
                      | Some (d, r5) ->
                          if 1 <= m && m <= 12 && 1 <= d && d <= days_in y m
                          then Some (y, m, d, r5) else None)
                 | _ -> None))
       | _ -> None)

(* F#: `tryDays` — exactly ten characters, the date part. *)
let try_days (s: list ch) : Tot (option int) =
  match date_part s with
  | Some (y, m, d, []) -> Some (days_of_civil y m d)
  | _ -> None

(* F#: `isCanonicalDate`. *)
let is_canonical_date (s: list ch) : Tot bool = Some? (try_days s)

(* F#: the body of `dateText` over a civil date. *)
let civil_text (c: civil) : Tot (list ch) =
  app (pad 4 c.year) (CMinus :: app (pad 2 c.month) (CMinus :: pad 2 c.day))

(* F#: `dateText`. *)
let date_text (days: int) : Tot (list ch) = civil_text (civil_of_days days)

(* ======================================================================================
   5. INSTANTS (F#: `fractionPart`, `parseInstant`, `isCanonicalTimestamp`, `unitOf`,
      `tryInstant`, `minSecond` / `maxSecond`, `isInstantInRange`, `instantText`).
   ====================================================================================== *)

let ch_T : ch = CPlain "T"
let ch_Z : ch = CPlain "Z"

(* F#: `fractionPart`'s digit arm — after the point, one to nine digits and the final `Z`, the last
   digit not `0`: their count and value. `last` is the digit before the character being read. *)
let rec read_fraction (s: list ch) (n: nat) (acc: int) (last: int)
  : Tot (option (nat & int)) (decreases s) =
  match s with
  | [] -> None
  | [c] -> if c = ch_Z && 1 <= n && n <= 9 && last <> 0 then Some (n, acc) else None
  | c :: t ->
      (match dec_val c with
       | Some v -> read_fraction t (n + 1) (acc * 10 + v) v
       | None -> None)

(* F#: `fractionPart` — what follows the second: `Z` alone (no fraction), or a point, the digits
   and `Z`. *)
let fraction_part (s: list ch) : Tot (option (nat & int)) =
  match s with
  | [c] -> if c = ch_Z then Some (0, 0) else None
  | CDot :: t -> read_fraction t 0 0 0
  | _ -> None

(* F#: the rest of `parseInstant` after `datePart` — `T`, two digits, `:`, two digits, `:`, two
   digits, the fraction part; the clock's seconds of day, with the fraction's digit count and
   value. *)
let read_clock (r0: list ch) : Tot (option (int & nat & int)) =
  match r0 with
  | c :: r1 ->
      if c <> ch_T then None
      else
        (match read_digits r1 2 0 with
         | None -> None
         | Some (h, r2) ->
             (match r2 with
              | CColon :: r3 ->
                  (match read_digits r3 2 0 with
                   | None -> None
                   | Some (mi, r4) ->
                       (match r4 with
                        | CColon :: r5 ->
                            (match read_digits r5 2 0 with
                             | None -> None
                             | Some (se, r6) ->
                                 (match fraction_part r6 with
                                  | None -> None
                                  | Some (n, f) ->
                                      if h <= 23 && mi <= 59 && se <= 59
                                      then Some (h * 3600 + mi * 60 + se, n, f)
                                      else None))
                        | _ -> None))
              | _ -> None))
  | [] -> None

(* F#: `parseInstant` — the floor epoch second of a canonical instant text and its fraction as
   `(digits, value)`: the day's second plus the clock's. *)
let parse_instant (s: list ch) : Tot (option (int & nat & int)) =
  match date_part s with
  | None -> None
  | Some (y, m, d, r0) ->
      (match read_clock r0 with
       | None -> None
       | Some (sod, n, f) -> Some (days_of_civil y m d * 86400 + sod, n, f))

(* F#: `isCanonicalTimestamp`. *)
let is_canonical_timestamp (s: list ch) : Tot bool = Some? (parse_instant s)

(* F#: `unitOf` — the coarsest unit holding the text's fraction; seconds for any other text. *)
let unit_of (s: list ch) : Tot time_unit =
  match parse_instant s with
  | Some (_, n, _) -> of_digits n
  | None -> Seconds

(* F#: `tryInstant`. *)
let try_instant (u: time_unit) (s: list ch) : Tot (option (int & int)) =
  match parse_instant s with
  | Some (second, n, f) ->
      if n <= unit_digits u then Some (second, f * pow10 (unit_digits u - n)) else None
  | None -> None

(* F#: `minSecond` / `maxSecond`. *)
let min_second : int = min_day * 86400
let max_second : int = max_day * 86400 + 86399

(* F#: `isInstantInRange` — less its `second = floor second` clause: see the header. *)
let is_instant_in_range (u: time_unit) (second fraction: int) : Tot bool =
  min_second <= second && second <= max_second && 0 <= fraction && fraction < unit_scale u

(* F#: `TrimEnd '0'`. *)
let rec trim_zeros (s: list ch) : Tot (list ch) =
  match s with
  | [] -> []
  | c :: t ->
      let r = trim_zeros t in
      if c = CHexCh HD0 && Nil? r then [] else c :: r

(* F#: the fraction `instantText` writes — nothing for zero, else a point and the unit's digits
   with the trailing zeros trimmed. *)
let fraction_text (u: time_unit) (fraction: int) : Tot (list ch) =
  if fraction = 0 then [] else CDot :: trim_zeros (pad (unit_digits u) fraction)

(* F#: the rest of `instantText` after the date — `T`, the clock, the fraction, `Z`. *)
let clock_text (u: time_unit) (sod fraction: int) : Tot (list ch) =
  ch_T :: app (pad 2 (sod / 3600))
              (CColon :: app (pad 2 (sod % 3600 / 60))
                             (CColon :: app (pad 2 (sod % 60)) (app (fraction_text u fraction) [ch_Z])))

(* F#: `instantText`, with `floor (second / 86400.0)` in its shifted integer form (header). *)
let instant_text (u: time_unit) (second fraction: int) : Tot (list ch) =
  let day = (second - min_second) / 86400 + min_day in
  let sod = second - day * 86400 in
  app (date_text day) (clock_text u sod fraction)

(* ======================================================================================
   6. THE ORDER (F#: `compareInstants`).

      Production compares the fixed-width `YYYY-MM-DDThh:mm:ss` prefix ordinally, then the
      fraction digits ordinally. An ordinal comparison is by UTF-16 code; `ch_rank` is that code
      for every character of this alphabet but `CPlain`, whose character the alphabet does not
      carry (two different `CPlain`s compare equal here). No canonical instant text puts two
      different plain characters at one position — `T` and `Z` stand where every canonical text has
      them — so on the two-canonical arm, the one the theorem is about and the only one a column's
      cells reach, the rank is production's order exactly.
   ====================================================================================== *)

let hexd_rank (d: hexd) : Tot int =
  match d with
  | HD0 -> 48 | HD1 -> 49 | HD2 -> 50 | HD3 -> 51 | HD4 -> 52 | HD5 -> 53 | HD6 -> 54 | HD7 -> 55
  | HD8 -> 56 | HD9 -> 57 | HDa -> 97 | HDb -> 98 | HDc -> 99 | HDd -> 100 | HDe -> 101 | HDf -> 102

let hexd_val (d: hexd) : Tot int = if hexd_rank d < 97 then hexd_rank d - 48 else hexd_rank d - 87

let ch_rank (c: ch) : Tot int =
  match c with
  | CQuote -> 34 | CBackslash -> 92
  | CLBrace -> 123 | CRBrace -> 125 | CLBrack -> 91 | CRBrack -> 93 | CColon -> 58 | CComma -> 44
  | CMinus -> 45 | CPlus -> 43 | CDot -> 46 | CUpE -> 69
  | CHexCh d -> hexd_rank d
  | CLu -> 117
  | CCtrl hi lo -> (if hi then 16 else 0) + hexd_val lo
  | CPlain _ -> 0 - 1

(* F#: `sign (CompareOrdinal a b)` at one character. *)
let cmp_ch (a b: ch) : Tot int =
  if a = b then 0
  else if ch_rank a < ch_rank b then 0 - 1
  else if ch_rank a > ch_rank b then 1
  else 0

(* F#: `sign (String.CompareOrdinal a b)` — the first differing character decides; a proper prefix
   sorts first. *)
let rec cmp_text (a b: list ch) : Tot int (decreases a) =
  match a, b with
  | [], [] -> 0
  | [], _ -> 0 - 1
  | _, [] -> 1
  | x :: xt, y :: yt -> let c = cmp_ch x y in if c <> 0 then c else cmp_text xt yt

let rec take (#a: Type) (k: nat) (l: list a) : Tot (list a) =
  if k = 0 then []
  else (match l with
        | [] -> []
        | x :: t -> x :: take (k - 1) t)

let rec drop (#a: Type) (k: nat) (l: list a) : Tot (list a) =
  if k = 0 then l
  else (match l with
        | [] -> []
        | _ :: t -> drop (k - 1) t)

(* F#: the fraction digits of a canonical text — `""` at length 20, else `Substring(20, Length - 21)`:
   what stands between the point after the second and the final `Z`. *)
let fraction_digits (s: list ch) : Tot (list ch) =
  match drop 19 s with
  | CDot :: t -> (match t with [] -> [] | _ -> take (len t - 1) t)
  | _ -> []

(* F#: `compareInstants`, arm for arm. *)
let compare_instants (a b: list ch) : Tot int =
  match is_canonical_timestamp a, is_canonical_timestamp b with
  | true, true ->
      let head = cmp_text (take 19 a) (take 19 b) in
      if head <> 0 then head else cmp_text (fraction_digits a) (fraction_digits b)
  | true, false -> 0 - 1
  | false, true -> 1
  | false, false -> cmp_text a b

(* THE order on the pair an instant is stored as: the second, then the fraction. *)
let cmp_pair (s1 f1 s2 f2: int) : Tot int =
  if s1 < s2 then 0 - 1
  else if s1 > s2 then 1
  else if f1 < f2 then 0 - 1
  else if f1 > f2 then 1
  else 0

(* ======================================================================================
   7. THEOREM 1 — THE CALENDAR IS A BIJECTION.

      The one fact with content is that Hinnant's corrected quotient recovers the year of the
      era: `yoe = g doe / 365` for `doe` in the year's own span, where `g doe = doe - doe/1460 +
      doe/36524 - doe/146096`. It is proved from two things the solver CAN see — `g` never
      decreases, and the year's first and last day both quotient to it — with the 800 end values
      of the 400 years of an era computed by normalisation.
   ====================================================================================== *)

(* The day of the era a year of the era starts on (F#: `yoe * 365 + yoe / 4 - yoe / 100`). *)
let year_base (yoe: int) : Tot int = yoe * 365 + yoe / 4 - yoe / 100

(* Is the SHIFTED year `yoe` of the era (March to February) a leap year — the civil year it ends in,
   `yoe + 1` of the era, is. *)
let shifted_leap (yoe: int) : Tot bool =
  (yoe + 1) % 4 = 0 && ((yoe + 1) % 100 <> 0 || yoe + 1 = 400)

let year_len (yoe: int) : Tot int = if shifted_leap yoe then 366 else 365

(* Hinnant's corrected quotient. *)
let g (doe: int) : Tot int = doe - doe / 1460 + doe / 36524 - doe / 146096

[@@ noextract_to "FSharp"]
let year_base_succ (yoe: int)
  : Lemma (requires 0 <= yoe /\ yoe <= 398)
          (ensures year_base (yoe + 1) == year_base yoe + year_len yoe) =
  FStar.Math.Lemmas.euclidean_division_definition yoe 4;
  FStar.Math.Lemmas.euclidean_division_definition (yoe + 1) 4;
  FStar.Math.Lemmas.lemma_mod_lt yoe 4;
  FStar.Math.Lemmas.lemma_mod_lt (yoe + 1) 4;
  FStar.Math.Lemmas.euclidean_division_definition yoe 100;
  FStar.Math.Lemmas.euclidean_division_definition (yoe + 1) 100;
  FStar.Math.Lemmas.lemma_mod_lt yoe 100;
  FStar.Math.Lemmas.lemma_mod_lt (yoe + 1) 100

[@@ noextract_to "FSharp"]
let g_step (x: int) : Lemma (requires 0 <= x) (ensures g x <= g (x + 1) /\ g (x + 1) <= g x + 2) = ()

[@@ noextract_to "FSharp"]
let rec g_mono (x y: int)
  : Lemma (requires 0 <= x /\ x <= y) (ensures g x <= g y) (decreases (y - x)) =
  if x = y then () else (g_step x; g_mono (x + 1) y)

(* The two ends of every year of the era quotient to the year: 800 values, computed. *)
let rec year_ends_ok (k: nat) : Tot bool (decreases k) =
  if k = 0 then true
  else
    (let yoe = k - 1 in
     g (year_base yoe) / 365 = yoe
     && g (year_base yoe + year_len yoe - 1) / 365 = yoe
     && year_ends_ok yoe)

[@@ noextract_to "FSharp"]
let rec year_ends_ok_at (k: nat) (yoe: int)
  : Lemma (requires year_ends_ok k /\ 0 <= yoe /\ yoe < k)
          (ensures g (year_base yoe) / 365 == yoe /\
                   g (year_base yoe + year_len yoe - 1) / 365 == yoe)
          (decreases k) =
  if yoe = k - 1 then () else year_ends_ok_at (k - 1) yoe

[@@ noextract_to "FSharp"]
let year_ends_computed () : Lemma (ensures year_ends_ok 400) =
  assert_norm (year_ends_ok 400 == true)

(* THE YEAR OF THE ERA IS RECOVERED: for a day of the era inside year `yoe`'s span. *)
[@@ noextract_to "FSharp"]
let yoe_recovered (yoe doy: int)
  : Lemma (requires 0 <= yoe /\ yoe <= 399 /\ 0 <= doy /\ doy < year_len yoe)
          (ensures g (year_base yoe + doy) / 365 == yoe) =
  year_ends_computed ();
  year_ends_ok_at 400 yoe;
  g_mono (year_base yoe) (year_base yoe + doy);
  g_mono (year_base yoe + doy) (year_base yoe + year_len yoe - 1)

(* The last year of the era ends where the era does: the 400-year leap day is the era's, not the
   year formula's, which is why `year_base_succ` stops at 398. *)
[@@ noextract_to "FSharp"]
let year_base_end () : Lemma (ensures year_base 399 + year_len 399 == 146097) =
  assert_norm (year_base 399 + year_len 399 == 146097)

(* And conversely: a day of the era whose quotient is `yoe` lies in that year's span. *)

[@@ noextract_to "FSharp"]
let doe_in_year (doe: int)
  : Lemma (requires 0 <= doe /\ doe <= 146096)
          (ensures (let yoe = g doe / 365 in
                    0 <= yoe /\ yoe <= 399 /\
                    year_base yoe <= doe /\ doe < year_base yoe + year_len yoe)) =
  let yoe = g doe / 365 in
  assert (0 <= yoe /\ yoe <= 399);
  year_ends_computed ();
  year_ends_ok_at 400 yoe;
  year_base_end ();
  (if doe < year_base yoe then
     (year_base_succ (yoe - 1);
      year_ends_ok_at 400 (yoe - 1);
      g_mono doe (year_base yoe - 1))
   else ());
  (if doe >= year_base yoe + year_len yoe then
     (if yoe < 399 then (year_base_succ yoe; year_ends_ok_at 400 (yoe + 1); g_mono (year_base (yoe + 1)) doe) else ())
   else ())

(* The day of the shifted year a month starts on (F#: `(153 * mp + 2) / 5`), and the twelve values. *)
let month_start (mp: int) : Tot int = (153 * mp + 2) / 5

[@@ noextract_to "FSharp"]
let month_start_table ()
  : Lemma (ensures month_start 0 == 0 /\ month_start 1 == 31 /\ month_start 2 == 61 /\
                   month_start 3 == 92 /\ month_start 4 == 122 /\ month_start 5 == 153 /\
                   month_start 6 == 184 /\ month_start 7 == 214 /\ month_start 8 == 245 /\
                   month_start 9 == 275 /\ month_start 10 == 306 /\ month_start 11 == 337 /\
                   month_start 12 == 367) =
  assert_norm (month_start 0 == 0); assert_norm (month_start 1 == 31);
  assert_norm (month_start 2 == 61); assert_norm (month_start 3 == 92);
  assert_norm (month_start 4 == 122); assert_norm (month_start 5 == 153);
  assert_norm (month_start 6 == 184); assert_norm (month_start 7 == 214);
  assert_norm (month_start 8 == 245); assert_norm (month_start 9 == 275);
  assert_norm (month_start 10 == 306); assert_norm (month_start 11 == 337);
  assert_norm (month_start 12 == 367)

(* The civil month of a shifted month, and the shifted month of a civil month. *)
let month_of_mp (mp: int) : Tot int = if mp < 10 then mp + 3 else mp - 9
let mp_of_month (m: int) : Tot int = (m + 9) % 12

(* A shifted month's length is its civil month's, for every month but February. *)
[@@ noextract_to "FSharp"]
let month_len (y mp: int)
  : Lemma (requires 0 <= mp /\ mp <= 10)
          (ensures month_start (mp + 1) - month_start mp == days_in y (month_of_mp mp)) =
  month_start_table ()

(* The shifted month a day of the shifted year falls in (F#: `(5 * doy + 2) / 153`). *)
[@@ noextract_to "FSharp"]
let mp_of_doy (mp doy: int)
  : Lemma (requires 0 <= mp /\ mp <= 11 /\ month_start mp <= doy /\
                    (if mp <= 10 then doy < month_start (mp + 1) else doy <= 365))
          (ensures (5 * doy + 2) / 153 == mp) =
  month_start_table ()

(* The leap rule travels through the shift: the civil year a February belongs to is the shifted
   year plus one, and is leap exactly when `shifted_leap` says. *)
[@@ noextract_to "FSharp"]
let leap_shift (era yoe: int)
  : Lemma (requires 0 <= yoe /\ yoe <= 399)
          (ensures is_leap (yoe + (era - 1) * 400 + 1) == shifted_leap yoe) =
  FStar.Math.Lemmas.lemma_mod_plus (yoe + 1) (era - 1) 400;
  FStar.Math.Lemmas.lemma_mod_plus (yoe + 1) ((era - 1) * 4) 100;
  FStar.Math.Lemmas.lemma_mod_plus (yoe + 1) ((era - 1) * 100) 4;
  (if yoe + 1 = 400 then () else FStar.Math.Lemmas.small_mod (yoe + 1) 400)

(* THEOREM — `civil_of_days` answers a civil date that exists, for every day count. *)
#push-options "--z3rlimit 80"
[@@ noextract_to "FSharp"]
let civil_valid_days (z: int) : Lemma (ensures civil_valid (civil_of_days z)) =
  let z' = z + 719468 + 146097 in
  let era = z' / 146097 in
  let doe = z' - era * 146097 in
  FStar.Math.Lemmas.euclidean_division_definition z' 146097;
  assert (0 <= doe /\ doe <= 146096);
  doe_in_year doe;
  let yoe = g doe / 365 in
  let doy = doe - year_base yoe in
  let mp = (5 * doy + 2) / 153 in
  month_start_table ();
  assert (0 <= mp /\ mp <= 11);
  assert (month_start mp <= doy);
  (if mp <= 10 then month_len 0 mp else leap_shift era yoe)
#pop-options

(* THEOREM — `days_of_civil` of `civil_of_days z` is `z`, for every day count. *)
#push-options "--z3rlimit 80"
[@@ noextract_to "FSharp"]
let days_civil_days (z: int)
  : Lemma (ensures (let c = civil_of_days z in days_of_civil c.year c.month c.day == z)) =
  let z' = z + 719468 + 146097 in
  let era = z' / 146097 in
  let doe = z' - era * 146097 in
  FStar.Math.Lemmas.euclidean_division_definition z' 146097;
  doe_in_year doe;
  let yoe = g doe / 365 in
  let doy = doe - year_base yoe in
  let mp = (5 * doy + 2) / 153 in
  month_start_table ();
  assert (0 <= mp /\ mp <= 11);
  FStar.Math.Lemmas.lemma_div_plus yoe era 400;
  FStar.Math.Lemmas.small_div yoe 400;
  FStar.Math.Lemmas.lemma_mod_plus mp 1 12;
  FStar.Math.Lemmas.small_mod mp 12
#pop-options

(* The shifted year of a civil date, and where it starts: `days_of_civil` is the year's start plus
   the day of the shifted year. *)
let shifted_year (y m: int) : Tot int = if m <= 2 then y - 1 else y

let era_of (yp: int) : Tot int = (yp + 400) / 400
let yoe_of (yp: int) : Tot int = (yp + 400) - era_of yp * 400

let year_start (yp: int) : Tot int = (era_of yp - 1) * 146097 + year_base (yoe_of yp) - 719468

[@@ noextract_to "FSharp"]
let yoe_of_range (yp: int) : Lemma (ensures 0 <= yoe_of yp /\ yoe_of yp <= 399) =
  FStar.Math.Lemmas.euclidean_division_definition (yp + 400) 400

[@@ noextract_to "FSharp"]
let days_of_civil_decomp (y m d: int)
  : Lemma (ensures days_of_civil y m d
                   == year_start (shifted_year y m) + month_start (mp_of_month m) + d - 1) = ()

(* The next shifted year starts one year's length later — across an era boundary too. *)
[@@ noextract_to "FSharp"]
let year_start_succ (yp: int)
  : Lemma (ensures year_start (yp + 1) == year_start yp + year_len (yoe_of yp)) =
  yoe_of_range yp;
  FStar.Math.Lemmas.euclidean_division_definition (yp + 400) 400;
  FStar.Math.Lemmas.euclidean_division_definition (yp + 401) 400;
  year_base_end ();
  (if yoe_of yp < 399 then year_base_succ (yoe_of yp) else ());
  (if yoe_of yp = 399
   then (FStar.Math.Lemmas.lemma_div_plus 0 (era_of yp + 1) 400;
         FStar.Math.Lemmas.small_div 0 400)
   else (FStar.Math.Lemmas.lemma_div_plus (yoe_of yp + 1) (era_of yp) 400;
         FStar.Math.Lemmas.small_div (yoe_of yp + 1) 400))

[@@ noextract_to "FSharp"]
let rec year_start_mono (a b: int)
  : Lemma (requires a < b) (ensures year_start a + year_len (yoe_of a) <= year_start b)
          (decreases (b - a)) =
  year_start_succ a;
  (if a + 1 = b then ()
   else (year_start_mono (a + 1) b; yoe_of_range (a + 1)))

(* A civil date that exists has its day of the shifted year inside that year's span. *)
[@@ noextract_to "FSharp"]
let doy_in_year (y m d: int)
  : Lemma (requires civil_valid ({ year = y; month = m; day = d }))
          (ensures (let doy = month_start (mp_of_month m) + d - 1 in
                    0 <= doy /\ doy < year_len (yoe_of (shifted_year y m)))) =
  let yp = shifted_year y m in
  yoe_of_range yp;
  month_start_table ();
  FStar.Math.Lemmas.euclidean_division_definition (yp + 400) 400;
  (if m = 2 then leap_shift (era_of yp) (yoe_of yp) else ())

(* The lexicographic order on civil dates — year, then month, then day. *)
let lex_lt (a b: civil) : Tot bool =
  a.year < b.year
  || (a.year = b.year && (a.month < b.month || (a.month = b.month && a.day < b.day)))

(* THEOREM — a later civil date is a larger day count. *)
#push-options "--z3rlimit 80"
[@@ noextract_to "FSharp"]
let days_of_civil_mono (a b: civil)
  : Lemma (requires civil_valid a /\ civil_valid b /\ lex_lt a b)
          (ensures days_of_civil a.year a.month a.day < days_of_civil b.year b.month b.day) =
  days_of_civil_decomp a.year a.month a.day;
  days_of_civil_decomp b.year b.month b.day;
  doy_in_year a.year a.month a.day;
  doy_in_year b.year b.month b.day;
  month_start_table ();
  let ya = shifted_year a.year a.month in
  let yb = shifted_year b.year b.month in
  if ya < yb then year_start_mono ya yb else ()
#pop-options

(* THEOREM — `civil_of_days` inverts `days_of_civil` on every civil date that exists. By the two
   theorems above and the order: the day count's civil date is valid and maps back to the same
   count, and two valid dates with one count are one date. *)
[@@ noextract_to "FSharp"]
let civil_days_civil (y m d: int)
  : Lemma (requires civil_valid ({ year = y; month = m; day = d }))
          (ensures civil_of_days (days_of_civil y m d) == { year = y; month = m; day = d }) =
  let a = { year = y; month = m; day = d } in
  let z = days_of_civil y m d in
  let c = civil_of_days z in
  civil_valid_days z;
  days_civil_days z;
  (if lex_lt a c then days_of_civil_mono a c
   else if lex_lt c a then days_of_civil_mono c a
   else ())

(* THEOREM — the canonical range: a civil date that exists with a four-digit year is a day count
   in `min_day`..`max_day`, and the civil date of a day count in that range has a four-digit year. *)
[@@ noextract_to "FSharp"]
let range_ends ()
  : Lemma (ensures days_of_civil 0 1 1 == min_day /\ days_of_civil 9999 12 31 == max_day) =
  assert_norm (days_of_civil 0 1 1 == min_day);
  assert_norm (days_of_civil 9999 12 31 == max_day)

[@@ noextract_to "FSharp"]
let year_in_range (c: civil)
  : Lemma (requires civil_valid c)
          (ensures is_day_in_range (days_of_civil c.year c.month c.day) == (0 <= c.year && c.year <= 9999)) =
  range_ends ();
  let lo = { year = 0; month = 1; day = 1 } in
  let hi = { year = 9999; month = 12; day = 31 } in
  assert (civil_valid lo /\ civil_valid hi);
  (if lex_lt c lo then days_of_civil_mono c lo
   else if lex_lt hi c then days_of_civil_mono hi c
   else ())

[@@ noextract_to "FSharp"]
let day_in_range_year (z: int)
  : Lemma (ensures is_day_in_range z == (let c = civil_of_days z in 0 <= c.year && c.year <= 9999)) =
  civil_valid_days z;
  days_civil_days z;
  year_in_range (civil_of_days z)

(* ======================================================================================
   8. THEOREM 2 — A DATE HAS ONE TEXT. First the digit facts every text theorem stands on: a run
      of digits reads back to its value, a value has one zero-padded spelling of a given width,
      and `pad` of a value in range is that spelling.
   ====================================================================================== *)

let rec all_dec (s: list ch) : Tot bool =
  match s with
  | [] -> true
  | c :: t -> Some? (dec_val c) && all_dec t

let dv (c: ch) : Tot int = match dec_val c with Some v -> v | None -> 0

(* The value of a digit run, folded onto `acc` as `read_digits` folds it. *)
let rec dval (s: list ch) (acc: int) : Tot int =
  match s with
  | [] -> acc
  | c :: t -> dval t (acc * 10 + dv c)

let cmp_int (a b: int) : Tot int = if a < b then 0 - 1 else if a > b then 1 else 0

[@@ noextract_to "FSharp"]
let rec pow10_add (a b: nat) : Lemma (ensures pow10 (a + b) == pow10 a * pow10 b) (decreases a) =
  if a = 0 then () else pow10_add (a - 1) b

[@@ noextract_to "FSharp"]
let rec len_app (#a: Type) (x y: list a) : Lemma (ensures len (app x y) == len x + len y) (decreases x) =
  match x with
  | [] -> ()
  | _ :: t -> len_app t y

[@@ noextract_to "FSharp"]
let rec dval_acc (s: list ch) (acc: int)
  : Lemma (ensures dval s acc == acc * pow10 (len s) + dval s 0) (decreases s) =
  match s with
  | [] -> ()
  | c :: t ->
      dval_acc t (acc * 10 + dv c);
      dval_acc t (dv c);
      FStar.Math.Lemmas.distributivity_add_left (acc * 10) (dv c) (pow10 (len t));
      FStar.Math.Lemmas.paren_mul_right acc 10 (pow10 (len t))

[@@ noextract_to "FSharp"]
let dec_ch_val (n: int) : Lemma (requires 0 <= n /\ n <= 9) (ensures dec_val (dec_ch n) == Some n) = ()

[@@ noextract_to "FSharp"]
let dv_range (c: ch) : Lemma (requires Some? (dec_val c)) (ensures 0 <= dv c /\ dv c <= 9) = ()

[@@ noextract_to "FSharp"]
let rec dval_bound (s: list ch)
  : Lemma (requires all_dec s) (ensures 0 <= dval s 0 /\ dval s 0 < pow10 (len s)) (decreases s) =
  match s with
  | [] -> ()
  | c :: t ->
      dval_bound t;
      dval_acc t (dv c);
      dv_range c;
      FStar.Math.Lemmas.lemma_mult_le_right (pow10 (len t)) (dv c) 9

[@@ noextract_to "FSharp"]
let rec read_digits_app (ds rest: list ch) (acc: int)
  : Lemma (requires all_dec ds)
          (ensures read_digits (app ds rest) (len ds) acc == Some (dval ds acc, rest))
          (decreases ds) =
  match ds with
  | [] -> ()
  | c :: t -> read_digits_app t rest (acc * 10 + dv c)

[@@ noextract_to "FSharp"]
let rec read_digits_take (s: list ch) (k: nat) (acc n: int) (rest: list ch)
  : Lemma (requires read_digits s k acc == Some (n, rest))
          (ensures (let ds = take k s in
                    s == app ds rest /\ len ds == k /\ all_dec ds /\ dval ds acc == n))
          (decreases k) =
  if k = 0 then ()
  else
    (match s with
     | c :: t -> read_digits_take t (k - 1) (acc * 10 + dv c) n rest
     | [] -> ())

(* Two digits with one value are one character. *)
[@@ noextract_to "FSharp"]
let dec_ch_dv (c: ch) : Lemma (requires Some? (dec_val c)) (ensures dec_ch (dv c) == c) = ()

[@@ noextract_to "FSharp"]
let dec_val_inj (c d: ch)
  : Lemma (requires Some? (dec_val c) /\ Some? (dec_val d) /\ dv c == dv d) (ensures c == d) =
  dec_ch_dv c; dec_ch_dv d

[@@ noextract_to "FSharp"]
let pow10_small () : Lemma (ensures pow10 2 == 100 /\ pow10 4 == 10000) =
  assert_norm (pow10 2 == 100); assert_norm (pow10 4 == 10000)

[@@ noextract_to "FSharp"]
let digit_split (c: ch) (t: list ch)
  : Lemma (requires Some? (dec_val c) /\ all_dec t)
          (ensures dval (c :: t) 0 / pow10 (len t) == dv c /\ dval (c :: t) 0 % pow10 (len t) == dval t 0) =
  dval_acc t (dv c);
  dval_bound t;
  FStar.Math.Lemmas.lemma_div_plus (dval t 0) (dv c) (pow10 (len t));
  FStar.Math.Lemmas.small_div (dval t 0) (pow10 (len t));
  FStar.Math.Lemmas.lemma_mod_plus (dval t 0) (dv c) (pow10 (len t));
  FStar.Math.Lemmas.small_mod (dval t 0) (pow10 (len t))

[@@ noextract_to "FSharp"]
let rec dval_unique (x y: list ch)
  : Lemma (requires all_dec x /\ all_dec y /\ len x == len y /\ dval x 0 == dval y 0)
          (ensures x == y) (decreases x) =
  match x, y with
  | [], [] -> ()
  | c :: xt, d :: yt ->
      digit_split c xt;
      digit_split d yt;
      dec_val_inj c d;
      dval_unique xt yt
  | _ -> ()

[@@ noextract_to "FSharp"]
let rec all_dec_app (x y: list ch)
  : Lemma (ensures all_dec (app x y) == (all_dec x && all_dec y)) (decreases x) =
  match x with
  | [] -> ()
  | _ :: t -> all_dec_app t y

[@@ noextract_to "FSharp"]
let rec dval_app (x y: list ch) (acc: int)
  : Lemma (ensures dval (app x y) acc == dval y (dval x acc)) (decreases x) =
  match x with
  | [] -> ()
  | c :: t -> dval_app t y (acc * 10 + dv c)

[@@ noextract_to "FSharp"]
let rec nat_text_dec (n: nat)
  : Lemma (ensures all_dec (nat_text n) /\ dval (nat_text n) 0 == n /\ Cons? (nat_text n)) (decreases n) =
  if n < 10 then dec_ch_val n
  else
    (nat_text_dec (n / 10);
     dec_ch_val (n % 10);
     all_dec_app (nat_text (n / 10)) [dec_ch (n % 10)];
     dval_app (nat_text (n / 10)) [dec_ch (n % 10)] 0)

[@@ noextract_to "FSharp"]
let rec nat_text_len (n: nat) (w: nat)
  : Lemma (requires 1 <= w /\ n < pow10 w) (ensures len (nat_text n) <= w) (decreases w) =
  if n < 10 then ()
  else
    (nat_text_len (n / 10) (w - 1);
     len_app (nat_text (n / 10)) [dec_ch (n % 10)])

[@@ noextract_to "FSharp"]
let rec nat_text_len_lower (n: nat) (w: nat)
  : Lemma (requires pow10 w <= n) (ensures len (nat_text n) >= w + 1) (decreases w) =
  if w = 0 then ()
  else
    (nat_text_len_lower (n / 10) (w - 1);
     len_app (nat_text (n / 10)) [dec_ch (n % 10)])

[@@ noextract_to "FSharp"]
let rec zeros_dec (k: nat) : Lemma (ensures all_dec (zeros k) /\ len (zeros k) == k) (decreases k) =
  if k = 0 then () else zeros_dec (k - 1)

[@@ noextract_to "FSharp"]
let rec dval_zeros (k: nat) (acc: int) : Lemma (ensures dval (zeros k) acc == acc * pow10 k) (decreases k) =
  if k = 0 then ()
  else (dval_zeros (k - 1) (acc * 10); FStar.Math.Lemmas.paren_mul_right acc 10 (pow10 (k - 1)))

(* THE SPELLING. A value below `10^w` pads to exactly `w` digits reading back to it. *)
[@@ noextract_to "FSharp"]
let pad_props (w: nat) (n: int)
  : Lemma (requires 1 <= w /\ 0 <= n /\ n < pow10 w)
          (ensures len (pad w n) == w /\ all_dec (pad w n) /\ dval (pad w n) 0 == n) =
  nat_text_dec n;
  nat_text_len n w;
  let s = nat_text n in
  if len s >= w then ()
  else
    (zeros_dec (w - len s);
     dval_zeros (w - len s) 0;
     len_app (zeros (w - len s)) s;
     all_dec_app (zeros (w - len s)) s;
     dval_app (zeros (w - len s)) s 0)

[@@ noextract_to "FSharp"]
let read_pad (w: nat) (n: int) (rest: list ch)
  : Lemma (requires 1 <= w /\ 0 <= n /\ n < pow10 w)
          (ensures read_digits (app (pad w n) rest) w 0 == Some (n, rest)) =
  pad_props w n;
  read_digits_app (pad w n) rest 0

[@@ noextract_to "FSharp"]
let pad_of_read (s: list ch) (w: nat) (n: int) (rest: list ch)
  : Lemma (requires 1 <= w /\ read_digits s w 0 == Some (n, rest))
          (ensures s == app (pad w n) rest /\ 0 <= n /\ n < pow10 w) =
  read_digits_take s w 0 n rest;
  dval_bound (take w s);
  pad_props w n;
  dval_unique (take w s) (pad w n)

(* A negative pads to a text with a `-` inside the width, which no digit read admits. *)
[@@ noextract_to "FSharp"]
let rec read_digits_zeros_minus (k: nat) (t: list ch) (count: nat) (acc: int)
  : Lemma (requires k < count)
          (ensures read_digits (app (zeros k) (CMinus :: t)) count acc == None) (decreases k) =
  if k = 0 then () else read_digits_zeros_minus (k - 1) t (count - 1) (acc * 10)

[@@ noextract_to "FSharp"]
let read_pad_negative (w: nat) (n: int) (rest: list ch)
  : Lemma (requires n < 0 /\ 1 <= w) (ensures read_digits (app (pad w n) rest) w 0 == None) =
  nat_text_dec (0 - n);
  let s = int_text n in
  if len s >= w then ()
  else
    (app_assoc (zeros (w - len s)) s rest;
     read_digits_zeros_minus (w - len s) (app (nat_text (0 - n)) rest) w 0)

(* A value of five or more digits pads to itself, and a four-digit read leaves a digit in front. *)
[@@ noextract_to "FSharp"]
let rec take_drop (#a: Type) (k: nat) (l: list a)
  : Lemma (requires k <= len l) (ensures app (take k l) (drop k l) == l /\ len (take k l) == k) (decreases k) =
  if k = 0 then () else (match l with _ :: t -> take_drop (k - 1) t)

[@@ noextract_to "FSharp"]
let rec all_dec_take_drop (k: nat) (s: list ch)
  : Lemma (requires all_dec s /\ k <= len s) (ensures all_dec (take k s) /\ all_dec (drop k s)) (decreases k) =
  if k = 0 then () else (match s with _ :: t -> all_dec_take_drop (k - 1) t)

[@@ noextract_to "FSharp"]
let read_pad_big (n: int) (rest: list ch)
  : Lemma (requires n >= 10000)
          (ensures (match read_digits (app (pad 4 n) rest) 4 0 with
                    | Some (_, r) -> Cons? r /\ Some? (dec_val (Cons?.hd r))
                    | None -> True)) =
  nat_text_dec n;
  nat_text_len_lower n 4;
  let s = nat_text n in
  take_drop 4 s;
  all_dec_take_drop 4 s;
  app_assoc (take 4 s) (drop 4 s) rest;
  read_digits_app (take 4 s) (app (drop 4 s) rest) 0;
  (match drop 4 s with
   | c :: _ -> ()
   | [] -> len_app (take 4 s) (drop 4 s))

[@@ noextract_to "FSharp"]
let civil_text_app (c: civil) (rest: list ch)
  : Lemma (ensures app (civil_text c) rest
                   == app (pad 4 c.year) (CMinus :: app (pad 2 c.month) (CMinus :: app (pad 2 c.day) rest))) =
  app_assoc (pad 4 c.year) (CMinus :: app (pad 2 c.month) (CMinus :: pad 2 c.day)) rest;
  app_assoc (pad 2 c.month) (CMinus :: pad 2 c.day) rest

(* THE DATE PART OF A CIVIL TEXT: read back exactly when the year has four digits. *)
[@@ noextract_to "FSharp"]
let date_part_civil_text (c: civil) (rest: list ch)
  : Lemma (requires civil_valid c)
          (ensures date_part (app (civil_text c) rest)
                   == (if 0 <= c.year && c.year <= 9999 then Some (c.year, c.month, c.day, rest) else None)) =
  pow10_small ();
  civil_text_app c rest;
  let tail = CMinus :: app (pad 2 c.month) (CMinus :: app (pad 2 c.day) rest) in
  if c.year < 0 then read_pad_negative 4 c.year tail
  else if c.year > 9999 then read_pad_big c.year tail
  else
    (read_pad 4 c.year tail;
     read_pad 2 c.month (CMinus :: app (pad 2 c.day) rest);
     read_pad 2 c.day rest)

(* THEOREM — every day in range has `date_text` as its one canonical text, and reads back. *)
[@@ noextract_to "FSharp"]
let try_days_date_text (z: int)
  : Lemma (requires is_day_in_range z) (ensures try_days (date_text z) == Some z) =
  civil_valid_days z;
  days_civil_days z;
  day_in_range_year z;
  app_nil (civil_text (civil_of_days z));
  date_part_civil_text (civil_of_days z) []

(* THEOREM — whatever `try_days` reads to a day is `date_text` of that day, and it is in range. *)
[@@ noextract_to "FSharp"]
let date_text_try_days (s: list ch) (z: int)
  : Lemma (requires try_days s == Some z) (ensures date_text z == s /\ is_day_in_range z) =
  pow10_small ();
  match read_digits s 4 0 with
  | Some (y, r1) ->
      pad_of_read s 4 y r1;
      (match r1 with
       | CMinus :: r2 ->
           (match read_digits r2 2 0 with
            | Some (m, r3) ->
                pad_of_read r2 2 m r3;
                (match r3 with
                 | CMinus :: r4 ->
                     (match read_digits r4 2 0 with
                      | Some (d, r5) ->
                          pad_of_read r4 2 d r5;
                          app_nil (pad 2 d);
                          let c = { year = y; month = m; day = d } in
                          civil_days_civil y m d;
                          year_in_range c
                      | None -> ())
                 | _ -> ())
            | None -> ())
       | _ -> ())
  | None -> ()

(* THEOREM — `date_text` is canonical exactly on the range. *)
[@@ noextract_to "FSharp"]
let date_text_canonical (z: int) : Lemma (ensures is_canonical_date (date_text z) == is_day_in_range z) =
  civil_valid_days z;
  day_in_range_year z;
  app_nil (civil_text (civil_of_days z));
  date_part_civil_text (civil_of_days z) []

(* ======================================================================================
   9. THEOREM 3 — AN INSTANT HAS ONE TEXT PER UNIT, AND THE SAME TEXT IN EVERY UNIT.
   ====================================================================================== *)

[@@ noextract_to "FSharp"]
let pow10_scale (u: time_unit) : Lemma (ensures pow10 (unit_digits u) == unit_scale u) =
  assert_norm (pow10 3 == 1000);
  assert_norm (pow10 6 == 1000000);
  assert_norm (pow10 9 == 1000000000)

let rec last_ch (s: list ch) : Tot ch =
  match s with
  | [] -> ch_Z
  | [c] -> c
  | _ :: t -> last_ch t

let rec init (s: list ch) : Tot (list ch) =
  match s with
  | [] -> []
  | [_] -> []
  | c :: t -> c :: init t

[@@ noextract_to "FSharp"]
let rec read_fraction_app (ds: list ch) (n: nat) (acc last: int)
  : Lemma (requires all_dec ds /\ Cons? ds)
          (ensures read_fraction (app ds [ch_Z]) n acc last
                   == (if 1 <= n + len ds && n + len ds <= 9 && dv (last_ch ds) <> 0
                       then Some (n + len ds, dval ds acc) else None))
          (decreases ds) =
  match ds with
  | [c] -> ()
  | c :: t -> read_fraction_app t (n + 1) (acc * 10 + dv c) (dv c)

[@@ noextract_to "FSharp"]
let rec read_fraction_split (t: list ch) (n: nat) (acc last: int) (fn: nat) (fv: int)
  : Lemma (requires read_fraction t n acc last == Some (fn, fv))
          (ensures (let ds = init t in
                    t == app ds [ch_Z] /\ all_dec ds /\ fn == n + len ds /\ fv == dval ds acc /\
                    1 <= fn /\ fn <= 9 /\
                    (Cons? ds ==> dv (last_ch ds) <> 0) /\ (Nil? ds ==> last <> 0)))
          (decreases t) =
  match t with
  | [] -> ()
  | [c] -> ()
  | c :: r ->
      (match dec_val c with
       | Some v -> read_fraction_split r (n + 1) (acc * 10 + v) v fn fv
       | None -> ())

(* `dval` of a run ending in a digit has that digit as its last decimal digit. *)
[@@ noextract_to "FSharp"]
let rec dval_last (ds: list ch)
  : Lemma (requires all_dec ds /\ Cons? ds) (ensures dval ds 0 % 10 == dv (last_ch ds)) (decreases ds) =
  match ds with
  | [c] -> dv_range c; FStar.Math.Lemmas.small_mod (dv c) 10
  | c :: t ->
      dval_last t;
      dval_acc t (dv c);
      (* dval ds 0 = dv c * 10^len t + dval t 0, and 10 divides 10^len t *)
      assert (pow10 (len t) == 10 * pow10 (len t - 1));
      FStar.Math.Lemmas.paren_mul_right (dv c) 10 (pow10 (len t - 1));
      FStar.Math.Lemmas.swap_mul (dv c) 10;
      FStar.Math.Lemmas.paren_mul_right 10 (dv c) (pow10 (len t - 1));
      FStar.Math.Lemmas.lemma_mod_plus (dval t 0) (dv c * pow10 (len t - 1)) 10

[@@ noextract_to "FSharp"]
let rec zeros_app_dval (ds: list ch) (k: nat)
  : Lemma (ensures dval (app ds (zeros k)) 0 == dval ds 0 * pow10 k /\ all_dec (app ds (zeros k)) == all_dec ds /\
                   len (app ds (zeros k)) == len ds + k) =
  zeros_dec k;
  dval_zeros k (dval ds 0);
  dval_app ds (zeros k) 0;
  all_dec_app ds (zeros k);
  len_app ds (zeros k)

(* A run that is not all zeros splits as its trimmed form and the zeros it drops. *)
[@@ noextract_to "FSharp"]
let rec trim_nil (t: list ch)
  : Lemma (requires all_dec t /\ Nil? (trim_zeros t)) (ensures t == zeros (len t) /\ dval t 0 == 0) (decreases t) =
  match t with
  | [] -> ()
  | c :: r -> trim_nil r; zeros_dec (len r); dval_zeros (len r) 0; dval_acc r (dv c)

[@@ noextract_to "FSharp"]
let rec dval_zero_trim (t: list ch)
  : Lemma (requires all_dec t /\ dval t 0 == 0) (ensures Nil? (trim_zeros t)) (decreases t) =
  match t with
  | [] -> ()
  | c :: r ->
      dval_acc r (dv c);
      dval_bound r;
      dv_range c;
      FStar.Math.Lemmas.lemma_mult_le_right (pow10 (len r)) 0 (dv c);
      (if dv c = 0 then dec_ch_dv c else ());
      dval_zero_trim r

[@@ noextract_to "FSharp"]
let rec trim_zeros_of_zeros (k: nat) : Lemma (ensures Nil? (trim_zeros (zeros k))) (decreases k) =
  if k = 0 then () else trim_zeros_of_zeros (k - 1)

[@@ noextract_to "FSharp"]
let rec trim_decomp (s: list ch)
  : Lemma (requires all_dec s /\ dval s 0 <> 0)
          (ensures (let ds = trim_zeros s in
                    Cons? ds /\ all_dec ds /\ dv (last_ch ds) <> 0 /\ len ds <= len s /\
                    s == app ds (zeros (len s - len ds)) /\
                    dval s 0 == dval ds 0 * pow10 (len s - len ds)))
          (decreases s) =
  match s with
  | c :: t ->
      let r = trim_zeros t in
      dval_acc t (dv c);
      (if Nil? r then
         (trim_nil t;
          zeros_dec (len t);
          dv_range c)
       else
         ((if dval t 0 = 0 then dval_zero_trim t else ());
          trim_decomp t;
          let k = len t - len r in
          dval_acc r (dv c);
          pow10_add (len r) k;
          FStar.Math.Lemmas.distributivity_add_left (dv c * pow10 (len r)) (dval r 0) (pow10 k);
          FStar.Math.Lemmas.paren_mul_right (dv c) (pow10 (len r)) (pow10 k)))
  | [] -> ()

[@@ noextract_to "FSharp"]
let rec trim_app_zeros (ds: list ch) (k: nat)
  : Lemma (requires Cons? ds /\ dv (last_ch ds) <> 0 /\ all_dec ds)
          (ensures trim_zeros (app ds (zeros k)) == ds) (decreases ds) =
  match ds with
  | [c] -> trim_zeros_of_zeros k; dec_ch_dv c
  | c :: t -> trim_app_zeros t k

[@@ noextract_to "FSharp"]
let pad_split (du: nat) (ds: list ch) (k: nat)
  : Lemma (requires Cons? ds /\ all_dec ds /\ len ds + k == du)
          (ensures pad du (dval ds 0 * pow10 k) == app ds (zeros k)) =
  zeros_app_dval ds k;
  dval_bound ds;
  pow10_add (len ds) k;
  FStar.Math.Lemmas.lemma_mult_lt_right (pow10 k) (dval ds 0) (pow10 (len ds));
  pad_props du (dval ds 0 * pow10 k);
  dval_unique (pad du (dval ds 0 * pow10 k)) (app ds (zeros k))

(* The seconds-of-day arithmetic `instant_text` and `parse_instant` share. *)
[@@ noextract_to "FSharp"]
let sod_split (sod: int)
  : Lemma (requires 0 <= sod /\ sod < 86400)
          (ensures (let h = sod / 3600 in let mi = sod % 3600 / 60 in let se = sod % 60 in
                    0 <= h /\ h <= 23 /\ 0 <= mi /\ mi <= 59 /\ 0 <= se /\ se <= 59 /\
                    h * 3600 + mi * 60 + se == sod)) =
  FStar.Math.Lemmas.euclidean_division_definition sod 3600;
  FStar.Math.Lemmas.euclidean_division_definition (sod % 3600) 60;
  FStar.Math.Lemmas.modulo_modulo_lemma sod 60 60

[@@ noextract_to "FSharp"]
let sod_join (h mi se: int)
  : Lemma (requires 0 <= h /\ h <= 23 /\ 0 <= mi /\ mi <= 59 /\ 0 <= se /\ se <= 59)
          (ensures (let sod = h * 3600 + mi * 60 + se in
                    sod / 3600 == h /\ sod % 3600 / 60 == mi /\ sod % 60 == se /\ 0 <= sod /\ sod < 86400)) =
  let sod = h * 3600 + mi * 60 + se in
  FStar.Math.Lemmas.lemma_div_plus (mi * 60 + se) h 3600;
  FStar.Math.Lemmas.small_div (mi * 60 + se) 3600;
  FStar.Math.Lemmas.lemma_mod_plus (mi * 60 + se) h 3600;
  FStar.Math.Lemmas.small_mod (mi * 60 + se) 3600;
  FStar.Math.Lemmas.lemma_div_plus se mi 60;
  FStar.Math.Lemmas.small_div se 60;
  FStar.Math.Lemmas.lemma_mod_plus se (h * 60 + mi) 60;
  FStar.Math.Lemmas.small_mod se 60

(* The day of an epoch second, and the day a civil date's second falls on. *)
[@@ noextract_to "FSharp"]
let day_of_second (second: int)
  : Lemma (requires min_second <= second)
          (ensures (let day = (second - min_second) / 86400 + min_day in
                    let sod = second - day * 86400 in
                    0 <= sod /\ sod < 86400 /\ min_day <= day /\
                    (second <= max_second ==> day <= max_day))) =
  FStar.Math.Lemmas.euclidean_division_definition (second - min_second) 86400

[@@ noextract_to "FSharp"]
let second_of_day (day sod: int)
  : Lemma (requires min_day <= day /\ 0 <= sod /\ sod < 86400)
          (ensures (day * 86400 + sod - min_second) / 86400 + min_day == day) =
  FStar.Math.Lemmas.lemma_div_plus sod (day - min_day) 86400;
  FStar.Math.Lemmas.small_div sod 86400

(* The fraction text reads back: its digit count and value, and the value scaled to the unit is
   the fraction written. *)
[@@ noextract_to "FSharp"]
let fraction_text_reads (u: time_unit) (fraction: int)
  : Lemma (requires 0 <= fraction /\ fraction < unit_scale u)
          (ensures (match fraction_part (app (fraction_text u fraction) [ch_Z]) with
                    | Some (n, f) -> n <= unit_digits u /\ f * pow10 (unit_digits u - n) == fraction
                    | None -> False)) =
  pow10_scale u;
  if fraction = 0 then ()
  else
    (pad_props (unit_digits u) fraction;
     trim_decomp (pad (unit_digits u) fraction);
     read_fraction_app (trim_zeros (pad (unit_digits u) fraction)) 0 0 0)

(* The clock text reads back to its seconds of day and its fraction. *)
[@@ noextract_to "FSharp"]
let clock_text_reads (u: time_unit) (sod fraction: int)
  : Lemma (requires 0 <= sod /\ sod < 86400 /\ 0 <= fraction /\ fraction < unit_scale u)
          (ensures (match read_clock (clock_text u sod fraction) with
                    | Some (sod', n, f) -> sod' == sod /\ n <= unit_digits u /\ f * pow10 (unit_digits u - n) == fraction
                    | None -> False)) =
  pow10_small ();
  sod_split sod;
  let tail = app (fraction_text u fraction) [ch_Z] in
  read_pad 2 (sod / 3600) (CColon :: app (pad 2 (sod % 3600 / 60)) (CColon :: app (pad 2 (sod % 60)) tail));
  read_pad 2 (sod % 3600 / 60) (CColon :: app (pad 2 (sod % 60)) tail);
  read_pad 2 (sod % 60) tail;
  fraction_text_reads u fraction

(* THEOREM — every pair in range reads back from its text, in its unit. *)
[@@ noextract_to "FSharp"]
let try_instant_instant_text (u: time_unit) (second fraction: int)
  : Lemma (requires is_instant_in_range u second fraction)
          (ensures try_instant u (instant_text u second fraction) == Some (second, fraction)) =
  let day = (second - min_second) / 86400 + min_day in
  let sod = second - day * 86400 in
  day_of_second second;
  civil_valid_days day;
  days_civil_days day;
  day_in_range_year day;
  date_part_civil_text (civil_of_days day) (clock_text u sod fraction);
  clock_text_reads u sod fraction

(* What a date part that reads says of the text: it opens with the civil text of a date that exists,
   with a four-digit year. *)
[@@ noextract_to "FSharp"]
let date_part_split (s: list ch) (y m d: int) (r0: list ch)
  : Lemma (requires date_part s == Some (y, m, d, r0))
          (ensures (let c = { year = y; month = m; day = d } in
                    s == app (civil_text c) r0 /\ civil_valid c /\ 0 <= y /\ y <= 9999)) =
  pow10_small ();
  match read_digits s 4 0 with
  | Some (y', r1) ->
      pad_of_read s 4 y' r1;
      (match r1 with
       | CMinus :: r2 ->
           (match read_digits r2 2 0 with
            | Some (m', r3) ->
                pad_of_read r2 2 m' r3;
                (match r3 with
                 | CMinus :: r4 ->
                     (match read_digits r4 2 0 with
                      | Some (d', r5) ->
                          pad_of_read r4 2 d' r5;
                          civil_text_app ({ year = y; month = m; day = d }) r0
                      | None -> ())
                 | _ -> ())
            | None -> ())
       | _ -> ())
  | None -> ()

(* What a fraction part that reads says of the text — and that the fraction `tryInstant` scales
   from it writes that text back. *)
[@@ noextract_to "FSharp"]
let fraction_part_inverts (u: time_unit) (r6: list ch) (n: nat) (f: int)
  : Lemma (requires fraction_part r6 == Some (n, f) /\ n <= unit_digits u)
          (ensures (let fraction = f * pow10 (unit_digits u - n) in
                    app (fraction_text u fraction) [ch_Z] == r6 /\
                    0 <= fraction /\ fraction < unit_scale u)) =
  pow10_scale u;
  match r6 with
  | [cz] -> ()
  | CDot :: t ->
      read_fraction_split t 0 0 0 n f;
      let ds = init t in
      dval_bound ds;
      dval_last ds;
      FStar.Math.Lemmas.lemma_mult_le_right (pow10 (unit_digits u - n)) 1 (dval ds 0);
      pow10_add (len ds) (unit_digits u - n);
      FStar.Math.Lemmas.lemma_mult_lt_right (pow10 (unit_digits u - n)) f (pow10 n);
      pad_split (unit_digits u) ds (unit_digits u - n);
      trim_app_zeros ds (unit_digits u - n)
  | _ -> ()

(* What a clock that reads says of the text: it is the clock text of its seconds of day, over the
   fraction text that reads to its fraction. *)
[@@ noextract_to "FSharp"]
let read_clock_inverts (u: time_unit) (r0: list ch) (sod: int) (n: nat) (f: int)
  : Lemma (requires read_clock r0 == Some (sod, n, f) /\ n <= unit_digits u)
          (ensures (let fraction = f * pow10 (unit_digits u - n) in
                    r0 == clock_text u sod fraction /\ 0 <= sod /\ sod < 86400 /\
                    0 <= fraction /\ fraction < unit_scale u)) =
  pow10_small ();
  match r0 with
  | c :: r1 ->
      (match read_digits r1 2 0 with
       | Some (h, r2) ->
           pad_of_read r1 2 h r2;
           (match r2 with
            | CColon :: r3 ->
                (match read_digits r3 2 0 with
                 | Some (mi, r4) ->
                     pad_of_read r3 2 mi r4;
                     (match r4 with
                      | CColon :: r5 ->
                          (match read_digits r5 2 0 with
                           | Some (se, r6) ->
                               pad_of_read r5 2 se r6;
                               (match fraction_part r6 with
                                | Some (n', f') ->
                                    sod_join h mi se;
                                    fraction_part_inverts u r6 n f
                                | None -> ())
                           | None -> ())
                      | _ -> ())
                 | None -> ())
            | _ -> ())
       | None -> ())
  | [] -> ()

(* THEOREM — whatever `try_instant u` reads to a pair is `instant_text u` of that pair, in range. *)
[@@ noextract_to "FSharp"]
let instant_text_try_instant (u: time_unit) (s: list ch) (second fraction: int)
  : Lemma (requires try_instant u s == Some (second, fraction))
          (ensures instant_text u second fraction == s /\ is_instant_in_range u second fraction) =
  match date_part s with
  | Some (y, m, d, r0) ->
      (match read_clock r0 with
       | Some (sod, n, f) ->
           date_part_split s y m d r0;
           read_clock_inverts u r0 sod n f;
           let cv = { year = y; month = m; day = d } in
           let dc = days_of_civil y m d in
           civil_days_civil y m d;
           year_in_range cv;
           second_of_day dc sod
       | None -> ())
  | None -> ()

(* THEOREM — widening a coarser unit into a finer one keeps the instant and its text. *)
[@@ noextract_to "FSharp"]
let widen_keeps_instant (u v: time_unit) (s: list ch) (second fraction: int)
  : Lemma (requires unit_widens u v /\ try_instant u s == Some (second, fraction))
          (ensures (let scaled = fraction * pow10 (unit_digits v - unit_digits u) in
                    try_instant v s == Some (second, scaled) /\
                    instant_text v second scaled == s /\
                    is_instant_in_range v second scaled)) =
  (match parse_instant s with
   | Some (_, n, f) ->
       pow10_add (unit_digits u - n) (unit_digits v - unit_digits u);
       FStar.Math.Lemmas.paren_mul_right f (pow10 (unit_digits u - n)) (pow10 (unit_digits v - unit_digits u))
   | None -> ());
  instant_text_try_instant v s second (fraction * pow10 (unit_digits v - unit_digits u))

(* THEOREM — `unit_of` is the coarsest unit that reads the text: a unit reads it exactly when the
   coarsest widens into it. *)
[@@ noextract_to "FSharp"]
let fraction_part_bound (r: list ch) (n: nat) (f: int)
  : Lemma (requires fraction_part r == Some (n, f)) (ensures n <= 9) =
  match r with
  | [_] -> ()
  | CDot :: t -> read_fraction_split t 0 0 0 n f
  | _ -> ()

[@@ noextract_to "FSharp"]
let read_clock_bound (r0: list ch) (sod: int) (n: nat) (f: int)
  : Lemma (requires read_clock r0 == Some (sod, n, f)) (ensures n <= 9) =
  match r0 with
  | c :: r1 ->
      (match read_digits r1 2 0 with
       | Some (_, CColon :: r3) ->
           (match read_digits r3 2 0 with
            | Some (_, CColon :: r5) ->
                (match read_digits r5 2 0 with
                 | Some (_, r6) ->
                     (match fraction_part r6 with
                      | Some (n', f') -> fraction_part_bound r6 n' f'
                      | None -> ())
                 | _ -> ())
            | _ -> ())
       | _ -> ())
  | [] -> ()

[@@ noextract_to "FSharp"]
let unit_of_coarsest (u: time_unit) (s: list ch)
  : Lemma (requires is_canonical_timestamp s)
          (ensures Some? (try_instant u s) == unit_widens (unit_of s) u) =
  match date_part s with
  | Some (_, _, _, r0) ->
      (match read_clock r0 with
       | Some (sod, n, f) -> read_clock_bound r0 sod n f
       | None -> ())
  | None -> ()

(* THEOREM — with a fraction in range, `instant_text` is canonical exactly when the second is in
   range (a fraction out of range may spell another instant: it is `Table.validate`'s to refuse). *)
[@@ noextract_to "FSharp"]
let instant_text_canonical (u: time_unit) (second fraction: int)
  : Lemma (requires 0 <= fraction /\ fraction < unit_scale u)
          (ensures is_canonical_timestamp (instant_text u second fraction)
                   == (min_second <= second && second <= max_second)) =
  if min_second <= second && second <= max_second then try_instant_instant_text u second fraction
  else
    (let day = (second - min_second) / 86400 + min_day in
     FStar.Math.Lemmas.euclidean_division_definition (second - min_second) 86400;
     civil_valid_days day;
     day_in_range_year day;
     let c = civil_of_days day in
     let sod = second - day * 86400 in
     date_part_civil_text c (clock_text u sod fraction))

(* ======================================================================================
   10. THEOREM 4 — THE ORDER IS CHRONOLOGICAL. Ordinal comparison of two same-width digit runs is
       the order of their values; the fixed-width prefix of an instant text is such runs around
       fixed separators; and the fraction digits, trimmed of trailing zeros, compare as the padded
       fraction does. So `compare_instants` on two canonical texts read at one unit is the order of
       the pairs — second first, then fraction.
   ====================================================================================== *)

[@@ noextract_to "FSharp"]
let cmp_ch_digits (c d: ch)
  : Lemma (requires Some? (dec_val c) /\ Some? (dec_val d))
          (ensures cmp_ch c d == cmp_int (dv c) (dv d)) =
  dec_ch_dv c; dec_ch_dv d

[@@ noextract_to "FSharp"]
let rec cmp_text_app (x y r1 r2: list ch)
  : Lemma (requires len x == len y)
          (ensures cmp_text (app x r1) (app y r2)
                   == (if cmp_text x y <> 0 then cmp_text x y else cmp_text r1 r2))
          (decreases x) =
  match x, y with
  | a :: xt, b :: yt -> cmp_text_app xt yt r1 r2
  | _ -> ()

[@@ noextract_to "FSharp"]
let rec cmp_text_digits (x y: list ch)
  : Lemma (requires all_dec x /\ all_dec y /\ len x == len y)
          (ensures cmp_text x y == cmp_int (dval x 0) (dval y 0)) (decreases x) =
  match x, y with
  | c :: xt, d :: yt ->
      cmp_ch_digits c d;
      cmp_text_digits xt yt;
      dval_acc xt (dv c);
      dval_acc yt (dv d);
      dval_bound xt;
      dval_bound yt;
      (if dv c < dv d then FStar.Math.Lemmas.lemma_mult_le_right (pow10 (len xt)) (dv c + 1) (dv d)
       else if dv c > dv d then FStar.Math.Lemmas.lemma_mult_le_right (pow10 (len xt)) (dv d + 1) (dv c)
       else dec_val_inj c d)
  | _ -> ()

[@@ noextract_to "FSharp"]
let cmp_pad (w: nat) (n m: int)
  : Lemma (requires 1 <= w /\ 0 <= n /\ n < pow10 w /\ 0 <= m /\ m < pow10 w)
          (ensures cmp_text (pad w n) (pad w m) == cmp_int n m) =
  pad_props w n; pad_props w m; cmp_text_digits (pad w n) (pad w m)

(* Trimming trailing zeros from two runs of one length keeps their order. *)
[@@ noextract_to "FSharp"]
let rec cmp_trim (x y: list ch)
  : Lemma (requires all_dec x /\ all_dec y /\ len x == len y)
          (ensures cmp_text (trim_zeros x) (trim_zeros y) == cmp_text x y) (decreases x) =
  match x, y with
  | c :: xt, d :: yt ->
      cmp_trim xt yt;
      cmp_ch_digits c d;
      dec_ch_dv c; dec_ch_dv d;
      (if Nil? (trim_zeros xt) then trim_nil xt else ());
      (if Nil? (trim_zeros yt) then trim_nil yt else ());
      (* equal-length all-zero runs are one run; a zero run sorts below any other of its length *)
      (if Nil? (trim_zeros xt) && Nil? (trim_zeros yt) then (zeros_dec (len xt); cmp_text_digits xt yt)
       else if Nil? (trim_zeros xt) then (dval_zeros (len xt) 0; cmp_text_digits xt yt; dval_bound yt;
                                          (if dval yt 0 = 0 then dval_zero_trim yt else ()))
       else if Nil? (trim_zeros yt) then (dval_zeros (len yt) 0; cmp_text_digits xt yt; dval_bound xt;
                                          (if dval xt 0 = 0 then dval_zero_trim xt else ()))
       else ())
  | _ -> ()

(* The fraction digits a text carries, by the fraction written: nothing for zero, else the trimmed
   padded digits. *)
let frac_digits (u: time_unit) (fraction: int) : Tot (list ch) =
  if fraction = 0 then [] else trim_zeros (pad (unit_digits u) fraction)

[@@ noextract_to "FSharp"]
let cmp_frac_digits (u: time_unit) (f1 f2: int)
  : Lemma (requires 0 <= f1 /\ f1 < unit_scale u /\ 0 <= f2 /\ f2 < unit_scale u)
          (ensures cmp_text (frac_digits u f1) (frac_digits u f2) == cmp_int f1 f2) =
  pow10_scale u;
  if f1 = 0 && f2 = 0 then ()
  else if f1 = 0 then (pad_props (unit_digits u) f2; trim_decomp (pad (unit_digits u) f2))
  else if f2 = 0 then (pad_props (unit_digits u) f1; trim_decomp (pad (unit_digits u) f1))
  else
    (pad_props (unit_digits u) f1; pad_props (unit_digits u) f2;
     cmp_trim (pad (unit_digits u) f1) (pad (unit_digits u) f2);
     cmp_pad (unit_digits u) f1 f2)

[@@ noextract_to "FSharp"]
let rec take_app (#a: Type) (x r: list a) : Lemma (ensures take (len x) (app x r) == x) (decreases x) =
  match x with
  | [] -> ()
  | _ :: t -> take_app t r

[@@ noextract_to "FSharp"]
let rec drop_app (#a: Type) (x r: list a) : Lemma (ensures drop (len x) (app x r) == r) (decreases x) =
  match x with
  | [] -> ()
  | _ :: t -> drop_app t r

(* The first nineteen characters of an instant text: the civil date and the clock. *)
let prefix19 (c: civil) (sod: int) : Tot (list ch) =
  app (civil_text c)
      (ch_T :: app (pad 2 (sod / 3600)) (CColon :: app (pad 2 (sod % 3600 / 60)) (CColon :: pad 2 (sod % 60))))

[@@ noextract_to "FSharp"]
let civil_text_len (c: civil)
  : Lemma (requires civil_valid c /\ 0 <= c.year /\ c.year <= 9999) (ensures len (civil_text c) == 10) =
  pow10_small ();
  pad_props 4 c.year; pad_props 2 c.month; pad_props 2 c.day;
  len_app (pad 4 c.year) (CMinus :: app (pad 2 c.month) (CMinus :: pad 2 c.day));
  len_app (pad 2 c.month) (CMinus :: pad 2 c.day)

[@@ noextract_to "FSharp"]
let prefix19_len (c: civil) (sod: int)
  : Lemma (requires civil_valid c /\ 0 <= c.year /\ c.year <= 9999 /\ 0 <= sod /\ sod < 86400)
          (ensures len (prefix19 c sod) == 19) =
  pow10_small ();
  civil_text_len c;
  sod_split sod;
  pad_props 2 (sod / 3600); pad_props 2 (sod % 3600 / 60); pad_props 2 (sod % 60);
  len_app (civil_text c) (ch_T :: app (pad 2 (sod / 3600)) (CColon :: app (pad 2 (sod % 3600 / 60)) (CColon :: pad 2 (sod % 60))));
  len_app (pad 2 (sod / 3600)) (CColon :: app (pad 2 (sod % 3600 / 60)) (CColon :: pad 2 (sod % 60)));
  len_app (pad 2 (sod % 3600 / 60)) (CColon :: pad 2 (sod % 60))

(* An instant text is its prefix followed by the fraction text and `Z`. *)
[@@ noextract_to "FSharp"]
let instant_text_split (u: time_unit) (second fraction: int)
  : Lemma (ensures (let day = (second - min_second) / 86400 + min_day in
                    let sod = second - day * 86400 in
                    instant_text u second fraction
                    == app (prefix19 (civil_of_days day) sod) (app (fraction_text u fraction) [ch_Z]))) =
  let day = (second - min_second) / 86400 + min_day in
  let sod = second - day * 86400 in
  let c = civil_of_days day in
  let tail = app (fraction_text u fraction) [ch_Z] in
  let a = pad 2 (sod / 3600) in
  let b = pad 2 (sod % 3600 / 60) in
  let d = pad 2 (sod % 60) in
  app_assoc (civil_text c) (ch_T :: app a (CColon :: app b (CColon :: d))) tail;
  app_assoc a (CColon :: app b (CColon :: d)) tail;
  app_assoc b (CColon :: d) tail

[@@ noextract_to "FSharp"]
let instant_text_parts (u: time_unit) (second fraction: int)
  : Lemma (requires is_instant_in_range u second fraction)
          (ensures (let day = (second - min_second) / 86400 + min_day in
                    let sod = second - day * 86400 in
                    take 19 (instant_text u second fraction) == prefix19 (civil_of_days day) sod /\
                    fraction_digits (instant_text u second fraction) == frac_digits u fraction)) =
  let day = (second - min_second) / 86400 + min_day in
  let sod = second - day * 86400 in
  day_of_second second;
  civil_valid_days day;
  day_in_range_year day;
  let c = civil_of_days day in
  prefix19_len c sod;
  instant_text_split u second fraction;
  let tail = app (fraction_text u fraction) [ch_Z] in
  take_app (prefix19 c sod) tail;
  drop_app (prefix19 c sod) tail;
  pow10_scale u;
  if fraction = 0 then ()
  else
    (pad_props (unit_digits u) fraction;
     trim_decomp (pad (unit_digits u) fraction);
     let ds = trim_zeros (pad (unit_digits u) fraction) in
     len_app ds [ch_Z];
     take_app ds [ch_Z])

(* Two civil texts in front of two rests compare lexicographically — year, month, day — then the
   rests; so, through the calendar's order, as their day counts. *)
[@@ noextract_to "FSharp"]
let cmp_civil_text (c1 c2: civil) (r1 r2: list ch)
  : Lemma (requires civil_valid c1 /\ civil_valid c2 /\
                    0 <= c1.year /\ c1.year <= 9999 /\ 0 <= c2.year /\ c2.year <= 9999)
          (ensures cmp_text (app (civil_text c1) r1) (app (civil_text c2) r2)
                   == (if lex_lt c1 c2 then 0 - 1 else if lex_lt c2 c1 then 1 else cmp_text r1 r2)) =
  pow10_small ();
  civil_text_app c1 r1;
  civil_text_app c2 r2;
  pad_props 4 c1.year; pad_props 4 c2.year;
  pad_props 2 c1.month; pad_props 2 c2.month;
  pad_props 2 c1.day; pad_props 2 c2.day;
  cmp_pad 4 c1.year c2.year;
  cmp_pad 2 c1.month c2.month;
  cmp_pad 2 c1.day c2.day;
  cmp_text_app (pad 4 c1.year) (pad 4 c2.year)
               (CMinus :: app (pad 2 c1.month) (CMinus :: app (pad 2 c1.day) r1))
               (CMinus :: app (pad 2 c2.month) (CMinus :: app (pad 2 c2.day) r2));
  cmp_text_app (pad 2 c1.month) (pad 2 c2.month)
               (CMinus :: app (pad 2 c1.day) r1) (CMinus :: app (pad 2 c2.day) r2);
  cmp_text_app (pad 2 c1.day) (pad 2 c2.day) r1 r2

(* The clock part of the prefix compares as the seconds of day. *)
[@@ noextract_to "FSharp"]
let cmp_clock (sod1 sod2: int)
  : Lemma (requires 0 <= sod1 /\ sod1 < 86400 /\ 0 <= sod2 /\ sod2 < 86400)
          (ensures cmp_text (ch_T :: app (pad 2 (sod1 / 3600)) (CColon :: app (pad 2 (sod1 % 3600 / 60)) (CColon :: pad 2 (sod1 % 60))))
                            (ch_T :: app (pad 2 (sod2 / 3600)) (CColon :: app (pad 2 (sod2 % 3600 / 60)) (CColon :: pad 2 (sod2 % 60))))
                   == cmp_int sod1 sod2) =
  pow10_small ();
  sod_split sod1; sod_split sod2;
  pad_props 2 (sod1 / 3600); pad_props 2 (sod2 / 3600);
  pad_props 2 (sod1 % 3600 / 60); pad_props 2 (sod2 % 3600 / 60);
  cmp_pad 2 (sod1 / 3600) (sod2 / 3600);
  cmp_pad 2 (sod1 % 3600 / 60) (sod2 % 3600 / 60);
  cmp_pad 2 (sod1 % 60) (sod2 % 60);
  cmp_text_app (pad 2 (sod1 / 3600)) (pad 2 (sod2 / 3600))
               (CColon :: app (pad 2 (sod1 % 3600 / 60)) (CColon :: pad 2 (sod1 % 60)))
               (CColon :: app (pad 2 (sod2 % 3600 / 60)) (CColon :: pad 2 (sod2 % 60)));
  cmp_text_app (pad 2 (sod1 % 3600 / 60)) (pad 2 (sod2 % 3600 / 60))
               (CColon :: pad 2 (sod1 % 60)) (CColon :: pad 2 (sod2 % 60))

(* The prefixes of two in-range instants compare as their seconds. *)
[@@ noextract_to "FSharp"]
let cmp_prefix (second1 second2: int)
  : Lemma (requires min_second <= second1 /\ second1 <= max_second /\
                    min_second <= second2 /\ second2 <= max_second)
          (ensures (let day1 = (second1 - min_second) / 86400 + min_day in
                    let day2 = (second2 - min_second) / 86400 + min_day in
                    cmp_text (prefix19 (civil_of_days day1) (second1 - day1 * 86400))
                             (prefix19 (civil_of_days day2) (second2 - day2 * 86400))
                    == cmp_int second1 second2)) =
  let day1 = (second1 - min_second) / 86400 + min_day in
  let day2 = (second2 - min_second) / 86400 + min_day in
  let sod1 = second1 - day1 * 86400 in
  let sod2 = second2 - day2 * 86400 in
  day_of_second second1; day_of_second second2;
  let c1 = civil_of_days day1 in
  let c2 = civil_of_days day2 in
  civil_valid_days day1; civil_valid_days day2;
  day_in_range_year day1; day_in_range_year day2;
  days_civil_days day1; days_civil_days day2;
  cmp_civil_text c1 c2
    (ch_T :: app (pad 2 (sod1 / 3600)) (CColon :: app (pad 2 (sod1 % 3600 / 60)) (CColon :: pad 2 (sod1 % 60))))
    (ch_T :: app (pad 2 (sod2 / 3600)) (CColon :: app (pad 2 (sod2 % 3600 / 60)) (CColon :: pad 2 (sod2 % 60))));
  cmp_clock sod1 sod2;
  (if lex_lt c1 c2 then days_of_civil_mono c1 c2
   else if lex_lt c2 c1 then days_of_civil_mono c2 c1
   else ())

(* THEOREM — `compare_instants` on two canonical texts read at one unit is the order of the pairs:
   the second, then the fraction. *)
[@@ noextract_to "FSharp"]
let compare_instants_chronological (u: time_unit) (a b: list ch) (s1 f1 s2 f2: int)
  : Lemma (requires try_instant u a == Some (s1, f1) /\ try_instant u b == Some (s2, f2))
          (ensures compare_instants a b == cmp_pair s1 f1 s2 f2) =
  instant_text_try_instant u a s1 f1;
  instant_text_try_instant u b s2 f2;
  instant_text_parts u s1 f1;
  instant_text_parts u s2 f2;
  cmp_prefix s1 s2;
  cmp_frac_digits u f1 f2

(* A seconds column holds no fraction: a text `try_instant Seconds` reads has no fraction digits,
   and a zero-digit fraction is zero. *)
[@@ noextract_to "FSharp"]
let fraction_zero_digits (r: list ch) (n: nat) (f: int)
  : Lemma (requires fraction_part r == Some (n, f) /\ n == 0) (ensures f == 0) =
  match r with
  | [_] -> ()
  | CDot :: t -> read_fraction_split t 0 0 0 n f
  | _ -> ()

[@@ noextract_to "FSharp"]
let try_instant_seconds (s: list ch) (second fraction: int)
  : Lemma (requires try_instant Seconds s == Some (second, fraction)) (ensures fraction == 0) =
  match date_part s with
  | Some (_, _, _, r0) ->
      (match r0 with
       | c :: r1 ->
           (match read_digits r1 2 0 with
            | Some (_, CColon :: r3) ->
                (match read_digits r3 2 0 with
                 | Some (_, CColon :: r5) ->
                     (match read_digits r5 2 0 with
                      | Some (_, r6) ->
                          (match fraction_part r6 with
                           | Some (n, f) -> if n = 0 then fraction_zero_digits r6 n f else ()
                           | None -> ())
                      | _ -> ())
                 | _ -> ())
            | _ -> ())
       | [] -> ())
  | None -> ()

(* ======================================================================================
   11. TWINS (Phase 309) — the extractor premise, sampled at this model. See `WireColumn.fst`'s
       section of the same name for what the list is and why the kit refuses a model without one.
   ====================================================================================== *)

noeq type twin = { tname : string; tholds : unit -> bool }

let rec twins_hold (l:list twin) : Tot bool =
  match l with
  | [] -> true
  | t :: r -> t.tholds () && twins_hold r

(* `2026-02-28` and `2026-02-28T12:34:56.5Z`, spelled in the alphabet. *)
let twin_date : list ch =
  [CHexCh HD2; CHexCh HD0; CHexCh HD2; CHexCh HD6; CMinus; CHexCh HD0; CHexCh HD2; CMinus; CHexCh HD2; CHexCh HD8]

let twin_instant : list ch =
  app twin_date [ch_T; CHexCh HD1; CHexCh HD2; CColon; CHexCh HD3; CHexCh HD4; CColon;
                 CHexCh HD5; CHexCh HD6; CDot; CHexCh HD5; ch_Z]

let twins : list twin = [
  { tname = "days-of-civil-and-civil-of-days-invert-at-the-epoch";
    tholds = (fun () -> days_of_civil 1970 1 1 = 0 && civil_of_days 0 = { year = 1970; month = 1; day = 1 }) };
  { tname = "date-text-reads-back";
    tholds = (fun () -> try_days twin_date = Some 20512 && date_text 20512 = twin_date) };
  { tname = "instant-text-reads-back-in-every-unit-at-least-as-fine";
    tholds = (fun () ->
      try_instant Milliseconds twin_instant = Some (20512 * 86400 + 45296, 500)
      && try_instant Nanoseconds twin_instant = Some (20512 * 86400 + 45296, 500000000)
      && try_instant Seconds twin_instant = None
      && instant_text Milliseconds (20512 * 86400 + 45296) 500 = twin_instant
      && instant_text Nanoseconds (20512 * 86400 + 45296) 500000000 = twin_instant
      && unit_of twin_instant = Milliseconds) };
  { tname = "compare-instants-is-chronological-not-ordinal";
    tholds = (fun () ->
      compare_instants twin_instant (instant_text Milliseconds (20512 * 86400 + 45296) 0) = 1
      && compare_instants (instant_text Milliseconds (20512 * 86400 + 45296) 250) twin_instant = 0 - 1
      && compare_instants twin_instant twin_instant = 0) };
  { tname = "pad-and-trim";
    tholds = (fun () ->
      pad 3 7 = [CHexCh HD0; CHexCh HD0; CHexCh HD7]
      && pad 2 123 = [CHexCh HD1; CHexCh HD2; CHexCh HD3]
      && pad 4 (0 - 1) = [CHexCh HD0; CHexCh HD0; CMinus; CHexCh HD1]
      && trim_zeros [CHexCh HD5; CHexCh HD0; CHexCh HD0] = [CHexCh HD5]) } ]

let _ = assert_norm (twins_hold twins == true)
