(*
   Unit — an F* model of the UNIT ALGEBRA (fuaran-core Phase 428): `src/Fuaran.Core.Unit/Unit.fs`
   clause for clause, with the laws its tests sample stated as theorems.

   WHAT IS MODELLED. The whole of `Unit.fs`: the canonical product of prefixed atoms with non-zero
   integer exponents that `UnitOfMeasure` holds (`uom`), the admitted vocabulary (`UnitVocabulary`:
   the atom table with each atom's scale and dimension, the alias, the twenty prefixes, the non-ratio
   and arbitrary refusal sets, the currency shape), the factor merge `tryCombine` behind `Unit.mul` /
   `div` / `pow` with its exponent range, `Unit.compatible` through `dimension`, `Unit.conversionFactor`
   through `scale` and the exact `Ratio` in lowest terms, `Unit.render` (`UnitOfMeasure.ToString`),
   and `Unit.parse` — the iterative reader over its stack of open terms, every refusal class with the
   token and position it names (`UnitRefusal`, with `Malformed`'s prose `expected` as an enumeration).

   THE ALPHABET. Production reads a .NET string; this module reads a list of SYMBOLS (`uch`): one
   constructor per ASCII letter and digit, one per punctuation character the grammar or the
   vocabulary spells, and `Other` for every other character — which the grammar refuses on sight
   and the vocabulary never contains, so no theorem here depends on what `Other` is. `code` is the
   character's ASCII code, which is the ORDINAL order production's `compare` on strings is (F# string
   comparison is ordinal, and the factor order is the structural order of the `UnitAtom` record:
   symbol then prefix). The oracle host bridges a string to this alphabet character by character.

   WHAT IS NOT DERIVED, AND IS A PARAMETER OR A PREMISE. Nothing: the module opens no other model
   and takes no host record. Its integers are F*'s unbounded `int`; production's exponents are
   `int32`, and production's refusal of an exponent outside `±2147483647` (`invalidArg` from `mul` /
   `div` / `pow`, a `Malformed` refusal from `parse`) is modelled as `None` / the same refusal by the
   same bound (`max_exp`), checked where production checks it. `tryCombine` checks the bound at each
   fold step and this module checks it on the merged result (`try_combine`): the two agree because
   every list the merge is handed carries each atom once, so an atom's running sum is written exactly
   once — the note at `try_combine` says so. `scale` multiplies the factors' scales in one fold where
   production accumulates the numerator, the denominator and the power of ten separately and
   multiplies once; the two products are one rational, and `reduce` takes both to the same lowest
   terms (`reduce_exact`). `Map<UnitAtom, int64>` is a strictly sorted association list here
   (`smap`), which is what an F# map's `toList` is.

   THE FOUR THEOREMS.
     1. THE GROUP. Over well-formed factor lists (`wf`: strictly sorted, no zero exponent) the merge
        is commutative (`combine_comm`), associative (`combine_assoc`), has the empty product as its
        identity (`combine_unit`) and every element its inverse (`combine_inverse`), and `div` is
        multiplication by the inverse (`div_is_mul_inverse`): exponent vectors form an abelian group
        under multiplication, with the exponent of every atom read off by `exp_of` (`combine_exp`)
        and two well-formed lists equal exactly when every exponent agrees (`ext`).
     2. ONE TEXT. For every CANONICAL unit — well-formed, every exponent within production's bound,
        every atom admitted (its prefix-and-symbol token resolves to it, and lexes as one symbol
        token) — `parse (render u) == Ok u` (`parse_render`). Equal units have one text because
        `render` is a function; this is the half that is not free: the text reads back to the unit
        that wrote it, so two different canonical units have two texts. The theorem is about
        STRUCTURAL identity, as Phase 426 ruled it: `km/h` and `m/s` are different units, compatible
        with factor 5/18, each with its own text.
     3. COMPATIBILITY IS EQUALITY OF DIMENSION. `compatible a b` holds exactly when every base
        dimension's and every currency's exponent agrees (`compatible_iff`, through `dim_val`), and
        the dimension of a product is the sum (`dim_combine`), so compatibility is a congruence for
        the algebra.
     4. FACTORS COMPOSE AND ARE EXACT. The scale is multiplicative over the merge (`scale_combine`),
        so for pairwise compatible `a`, `b`, `c` the factor from `a` to `c` is the product of the
        factors through `b` as rationals (`factor_compose`), a unit's factor to itself is one
        (`factor_self`), and `reduce` — production's gcd to lowest terms — changes no value
        (`reduce_exact`): the `Ratio` a consumer receives IS the exact quotient of the two scales.
        That two lowest-terms pairs of one value are one pair (uniqueness of the reduced form) is a
        fact about gcd this module does not prove; the differential holds production's `Ratio` to
        the model's reduced pair over the drawn pool, and the ladder's row says so.

   PROOF DISCIPLINE. No `assume`, no `admit`; checked by the proof leg at the leg's flags
   (`--z3rlimit 40 --quake 3 --report_assumes error`). Default fuel; `--ifuel 2` scoped where a
   lemma splits two lists at once.

   THE TWINS (the last section) are the extractor premise sampled at this model — see
   `WireColumn.fst`'s twins section for what they are and why.
*)
module Unit

open FStar.Math.Lemmas
open FStar.Math.Euclid

(* ======================================================================================
   1. THE ALPHABET (F#: `char`), and the lists this module reads and writes.
   ====================================================================================== *)

type uch =
  | La | Lb | Lc | Ld | Le | Lf | Lg | Lh | Li | Lj | Lk | Ll | Lm | Ln | Lo | Lp
  | Lq | Lr | Ls | Lt | Lu | Lv | Lw | Lx | Ly | Lz | UA | UB | UC | UD | UE | UF
  | UG | UH | UI | UJ | UK | UL | UM | UN | UO | UP | UQ | UR | US | UT | UU | UV
  | UW | UX | UY | UZ | D0 | D1 | D2 | D3 | D4 | D5 | D6 | D7 | D8 | D9 | Dot | Slash
  | LPar | RPar | LBr | RBr | LCur | RCur | Pct | Under | Apos | Minus | Plus | Star
  | Caret
  | Other

let code (c: uch) : Tot nat =
  match c with
  | La -> 97
  | Lb -> 98
  | Lc -> 99
  | Ld -> 100
  | Le -> 101
  | Lf -> 102
  | Lg -> 103
  | Lh -> 104
  | Li -> 105
  | Lj -> 106
  | Lk -> 107
  | Ll -> 108
  | Lm -> 109
  | Ln -> 110
  | Lo -> 111
  | Lp -> 112
  | Lq -> 113
  | Lr -> 114
  | Ls -> 115
  | Lt -> 116
  | Lu -> 117
  | Lv -> 118
  | Lw -> 119
  | Lx -> 120
  | Ly -> 121
  | Lz -> 122
  | UA -> 65
  | UB -> 66
  | UC -> 67
  | UD -> 68
  | UE -> 69
  | UF -> 70
  | UG -> 71
  | UH -> 72
  | UI -> 73
  | UJ -> 74
  | UK -> 75
  | UL -> 76
  | UM -> 77
  | UN -> 78
  | UO -> 79
  | UP -> 80
  | UQ -> 81
  | UR -> 82
  | US -> 83
  | UT -> 84
  | UU -> 85
  | UV -> 86
  | UW -> 87
  | UX -> 88
  | UY -> 89
  | UZ -> 90
  | D0 -> 48
  | D1 -> 49
  | D2 -> 50
  | D3 -> 51
  | D4 -> 52
  | D5 -> 53
  | D6 -> 54
  | D7 -> 55
  | D8 -> 56
  | D9 -> 57
  | Dot -> 46
  | Slash -> 47
  | LPar -> 40
  | RPar -> 41
  | LBr -> 91
  | RBr -> 93
  | LCur -> 123
  | RCur -> 125
  | Pct -> 37
  | Under -> 95
  | Apos -> 39
  | Minus -> 45
  | Plus -> 43
  | Star -> 42
  | Caret -> 94
  | Other -> 128

let of_code (n: nat) : Tot uch =
  if n = 97 then La
  else if n = 98 then Lb
  else if n = 99 then Lc
  else if n = 100 then Ld
  else if n = 101 then Le
  else if n = 102 then Lf
  else if n = 103 then Lg
  else if n = 104 then Lh
  else if n = 105 then Li
  else if n = 106 then Lj
  else if n = 107 then Lk
  else if n = 108 then Ll
  else if n = 109 then Lm
  else if n = 110 then Ln
  else if n = 111 then Lo
  else if n = 112 then Lp
  else if n = 113 then Lq
  else if n = 114 then Lr
  else if n = 115 then Ls
  else if n = 116 then Lt
  else if n = 117 then Lu
  else if n = 118 then Lv
  else if n = 119 then Lw
  else if n = 120 then Lx
  else if n = 121 then Ly
  else if n = 122 then Lz
  else if n = 65 then UA
  else if n = 66 then UB
  else if n = 67 then UC
  else if n = 68 then UD
  else if n = 69 then UE
  else if n = 70 then UF
  else if n = 71 then UG
  else if n = 72 then UH
  else if n = 73 then UI
  else if n = 74 then UJ
  else if n = 75 then UK
  else if n = 76 then UL
  else if n = 77 then UM
  else if n = 78 then UN
  else if n = 79 then UO
  else if n = 80 then UP
  else if n = 81 then UQ
  else if n = 82 then UR
  else if n = 83 then US
  else if n = 84 then UT
  else if n = 85 then UU
  else if n = 86 then UV
  else if n = 87 then UW
  else if n = 88 then UX
  else if n = 89 then UY
  else if n = 90 then UZ
  else if n = 48 then D0
  else if n = 49 then D1
  else if n = 50 then D2
  else if n = 51 then D3
  else if n = 52 then D4
  else if n = 53 then D5
  else if n = 54 then D6
  else if n = 55 then D7
  else if n = 56 then D8
  else if n = 57 then D9
  else if n = 46 then Dot
  else if n = 47 then Slash
  else if n = 40 then LPar
  else if n = 41 then RPar
  else if n = 91 then LBr
  else if n = 93 then RBr
  else if n = 123 then LCur
  else if n = 125 then RCur
  else if n = 37 then Pct
  else if n = 95 then Under
  else if n = 39 then Apos
  else if n = 45 then Minus
  else if n = 43 then Plus
  else if n = 42 then Star
  else if n = 94 then Caret
  else Other


(* `of_code` reads a code back: the witness that `code` is injective, which is what makes the ordinal
   order below a strict total order. *)
let of_code_code (c: uch) : Lemma (of_code (code c) == c) =
  match c with
  | La -> assert_norm (of_code (code La) == La)
  | Lb -> assert_norm (of_code (code Lb) == Lb)
  | Lc -> assert_norm (of_code (code Lc) == Lc)
  | Ld -> assert_norm (of_code (code Ld) == Ld)
  | Le -> assert_norm (of_code (code Le) == Le)
  | Lf -> assert_norm (of_code (code Lf) == Lf)
  | Lg -> assert_norm (of_code (code Lg) == Lg)
  | Lh -> assert_norm (of_code (code Lh) == Lh)
  | Li -> assert_norm (of_code (code Li) == Li)
  | Lj -> assert_norm (of_code (code Lj) == Lj)
  | Lk -> assert_norm (of_code (code Lk) == Lk)
  | Ll -> assert_norm (of_code (code Ll) == Ll)
  | Lm -> assert_norm (of_code (code Lm) == Lm)
  | Ln -> assert_norm (of_code (code Ln) == Ln)
  | Lo -> assert_norm (of_code (code Lo) == Lo)
  | Lp -> assert_norm (of_code (code Lp) == Lp)
  | Lq -> assert_norm (of_code (code Lq) == Lq)
  | Lr -> assert_norm (of_code (code Lr) == Lr)
  | Ls -> assert_norm (of_code (code Ls) == Ls)
  | Lt -> assert_norm (of_code (code Lt) == Lt)
  | Lu -> assert_norm (of_code (code Lu) == Lu)
  | Lv -> assert_norm (of_code (code Lv) == Lv)
  | Lw -> assert_norm (of_code (code Lw) == Lw)
  | Lx -> assert_norm (of_code (code Lx) == Lx)
  | Ly -> assert_norm (of_code (code Ly) == Ly)
  | Lz -> assert_norm (of_code (code Lz) == Lz)
  | UA -> assert_norm (of_code (code UA) == UA)
  | UB -> assert_norm (of_code (code UB) == UB)
  | UC -> assert_norm (of_code (code UC) == UC)
  | UD -> assert_norm (of_code (code UD) == UD)
  | UE -> assert_norm (of_code (code UE) == UE)
  | UF -> assert_norm (of_code (code UF) == UF)
  | UG -> assert_norm (of_code (code UG) == UG)
  | UH -> assert_norm (of_code (code UH) == UH)
  | UI -> assert_norm (of_code (code UI) == UI)
  | UJ -> assert_norm (of_code (code UJ) == UJ)
  | UK -> assert_norm (of_code (code UK) == UK)
  | UL -> assert_norm (of_code (code UL) == UL)
  | UM -> assert_norm (of_code (code UM) == UM)
  | UN -> assert_norm (of_code (code UN) == UN)
  | UO -> assert_norm (of_code (code UO) == UO)
  | UP -> assert_norm (of_code (code UP) == UP)
  | UQ -> assert_norm (of_code (code UQ) == UQ)
  | UR -> assert_norm (of_code (code UR) == UR)
  | US -> assert_norm (of_code (code US) == US)
  | UT -> assert_norm (of_code (code UT) == UT)
  | UU -> assert_norm (of_code (code UU) == UU)
  | UV -> assert_norm (of_code (code UV) == UV)
  | UW -> assert_norm (of_code (code UW) == UW)
  | UX -> assert_norm (of_code (code UX) == UX)
  | UY -> assert_norm (of_code (code UY) == UY)
  | UZ -> assert_norm (of_code (code UZ) == UZ)
  | D0 -> assert_norm (of_code (code D0) == D0)
  | D1 -> assert_norm (of_code (code D1) == D1)
  | D2 -> assert_norm (of_code (code D2) == D2)
  | D3 -> assert_norm (of_code (code D3) == D3)
  | D4 -> assert_norm (of_code (code D4) == D4)
  | D5 -> assert_norm (of_code (code D5) == D5)
  | D6 -> assert_norm (of_code (code D6) == D6)
  | D7 -> assert_norm (of_code (code D7) == D7)
  | D8 -> assert_norm (of_code (code D8) == D8)
  | D9 -> assert_norm (of_code (code D9) == D9)
  | Dot -> assert_norm (of_code (code Dot) == Dot)
  | Slash -> assert_norm (of_code (code Slash) == Slash)
  | LPar -> assert_norm (of_code (code LPar) == LPar)
  | RPar -> assert_norm (of_code (code RPar) == RPar)
  | LBr -> assert_norm (of_code (code LBr) == LBr)
  | RBr -> assert_norm (of_code (code RBr) == RBr)
  | LCur -> assert_norm (of_code (code LCur) == LCur)
  | RCur -> assert_norm (of_code (code RCur) == RCur)
  | Pct -> assert_norm (of_code (code Pct) == Pct)
  | Under -> assert_norm (of_code (code Under) == Under)
  | Apos -> assert_norm (of_code (code Apos) == Apos)
  | Minus -> assert_norm (of_code (code Minus) == Minus)
  | Plus -> assert_norm (of_code (code Plus) == Plus)
  | Star -> assert_norm (of_code (code Star) == Star)
  | Caret -> assert_norm (of_code (code Caret) == Caret)
  | Other -> assert_norm (of_code (code Other) == Other)

let code_inj (x y: uch) : Lemma (requires code x = code y) (ensures x = y) =
  of_code_code x; of_code_code y

(* F#: `string` — the text a unit is written in. *)
type text = list uch

(* ---- the list functions this module uses, as every model here defines its own ---- *)

let rec len (#a: Type) (l: list a) : Tot nat =
  match l with
  | [] -> 0
  | _ :: t -> 1 + len t

let rec app (#a: Type) (l m: list a) : Tot (list a) =
  match l with
  | [] -> m
  | x :: t -> x :: app t m

let rec rev_acc (#a: Type) (l acc: list a) : Tot (list a) =
  match l with
  | [] -> acc
  | x :: t -> rev_acc t (x :: acc)

let rev (#a: Type) (l: list a) : Tot (list a) = rev_acc l []

let rec mem (#a: eqtype) (x: a) (l: list a) : Tot bool =
  match l with
  | [] -> false
  | y :: t -> x = y || mem x t

let rec assoc (#k: eqtype) (#v: Type) (x: k) (l: list (k & v)) : Tot (option v) =
  match l with
  | [] -> None
  | (y, w) :: t -> if x = y then Some w else assoc x t

let rec app_nil (#a: Type) (l: list a) : Lemma (app l [] == l) =
  match l with
  | [] -> ()
  | _ :: t -> app_nil t

let rec app_assoc (#a: Type) (l m n: list a) : Lemma (app (app l m) n == app l (app m n)) =
  match l with
  | [] -> ()
  | _ :: t -> app_assoc t m n

let rec len_app (#a: Type) (l m: list a) : Lemma (len (app l m) == len l + len m) =
  match l with
  | [] -> ()
  | _ :: t -> len_app t m

(* ---- the ordinal order (F#: `compare` on strings, which is ordinal) ---- *)

(* F#: `String.CompareOrdinal`, as a sign. Code unit by code unit, the shorter prefix first. *)
let rec cmp_text (a b: text) : Tot int (decreases a) =
  match a, b with
  | [], [] -> 0
  | [], _ -> -1
  | _, [] -> 1
  | x :: xs, y :: ys ->
      if code x < code y then -1 else if code x > code y then 1 else cmp_text xs ys

let rec cmp_text_refl (a: text) : Lemma (cmp_text a a == 0) =
  match a with
  | [] -> ()
  | _ :: t -> cmp_text_refl t

let rec cmp_text_zero (a b: text) : Lemma (requires cmp_text a b == 0) (ensures a == b) (decreases a) =
  match a, b with
  | x :: xs, y :: ys -> code_inj x y; cmp_text_zero xs ys
  | _ -> ()

let rec cmp_text_anti (a b: text) : Lemma (cmp_text b a == 0 - cmp_text a b) (decreases a) =
  match a, b with
  | x :: xs, y :: ys -> cmp_text_anti xs ys
  | _ -> ()

let rec cmp_text_trans (a b c: text)
  : Lemma (requires cmp_text a b < 0 /\ cmp_text b c < 0) (ensures cmp_text a c < 0) (decreases a) =
  match a, b, c with
  | x :: xs, y :: ys, z :: zs ->
      if code x = code y && code y = code z then cmp_text_trans xs ys zs else ()
  | _ -> ()

(* ======================================================================================
   2. THE VOCABULARY (F#: `UnitVocabulary`), the tables character for character.
   ====================================================================================== *)

(* F#: `UnitAtom` — an admitted symbol (or a currency written `[GBP]`) under an optional prefix.
   Ordered by symbol, then prefix, ordinally: the canonical factor order. *)
type atom = { symbol: text; prefix: text }

(* F#: `AtomDef` — whether SI prefixes apply, the scale relative to the coherent base (metre, GRAM,
   second, ampere, kelvin, mole, candela) as `num/den`, and the exponents over
   `[length; mass; time; current; temperature; amount; luminous intensity]`. *)
type atom_def = { metric: bool; num: pos; den: pos; dims: list int }

(* F#: `UnitVocabulary.atoms`, row for row, in the source's order; looked up by symbol. The four tables
   are OPAQUE to the SMT solver: no theorem here reads a row (the twins do, by normalisation), and a
   lookup the solver could unfold into a forty-five-row literal is a proof that times out for nothing. *)
[@@"opaque_to_smt"]
let atoms : list (text & atom_def) = [

  ([Lm], { metric = true; num = 1; den = 1; dims = [1; 0; 0; 0; 0; 0; 0] });
  ([Lg], { metric = true; num = 1; den = 1; dims = [0; 1; 0; 0; 0; 0; 0] });
  ([Ls], { metric = true; num = 1; den = 1; dims = [0; 0; 1; 0; 0; 0; 0] });
  ([UA], { metric = true; num = 1; den = 1; dims = [0; 0; 0; 1; 0; 0; 0] });
  ([UK], { metric = true; num = 1; den = 1; dims = [0; 0; 0; 0; 1; 0; 0] });
  ([Lm; Lo; Ll], { metric = true; num = 1; den = 1; dims = [0; 0; 0; 0; 0; 1; 0] });
  ([Lc; Ld], { metric = true; num = 1; den = 1; dims = [0; 0; 0; 0; 0; 0; 1] });
  ([Lr; La; Ld], { metric = true; num = 1; den = 1; dims = [0; 0; 0; 0; 0; 0; 0] });
  ([Ls; Lr], { metric = true; num = 1; den = 1; dims = [0; 0; 0; 0; 0; 0; 0] });
  ([UH; Lz], { metric = true; num = 1; den = 1; dims = [0; 0; -1; 0; 0; 0; 0] });
  ([UN], { metric = true; num = 1000; den = 1; dims = [1; 1; -2; 0; 0; 0; 0] });
  ([UP; La], { metric = true; num = 1000; den = 1; dims = [-1; 1; -2; 0; 0; 0; 0] });
  ([UJ], { metric = true; num = 1000; den = 1; dims = [2; 1; -2; 0; 0; 0; 0] });
  ([UW], { metric = true; num = 1000; den = 1; dims = [2; 1; -3; 0; 0; 0; 0] });
  ([UC], { metric = true; num = 1; den = 1; dims = [0; 0; 1; 1; 0; 0; 0] });
  ([UV], { metric = true; num = 1000; den = 1; dims = [2; 1; -3; -1; 0; 0; 0] });
  ([UF], { metric = true; num = 1; den = 1000; dims = [-2; -1; 4; 2; 0; 0; 0] });
  ([UO; Lh; Lm], { metric = true; num = 1000; den = 1; dims = [2; 1; -3; -2; 0; 0; 0] });
  ([US], { metric = true; num = 1; den = 1000; dims = [-2; -1; 3; 2; 0; 0; 0] });
  ([UW; Lb], { metric = true; num = 1000; den = 1; dims = [2; 1; -2; -1; 0; 0; 0] });
  ([UT], { metric = true; num = 1000; den = 1; dims = [0; 1; -2; -1; 0; 0; 0] });
  ([UH], { metric = true; num = 1000; den = 1; dims = [2; 1; -2; -2; 0; 0; 0] });
  ([Ll; Lm], { metric = true; num = 1; den = 1; dims = [0; 0; 0; 0; 0; 0; 1] });
  ([Ll; Lx], { metric = true; num = 1; den = 1; dims = [-2; 0; 0; 0; 0; 0; 1] });
  ([UB; Lq], { metric = true; num = 1; den = 1; dims = [0; 0; -1; 0; 0; 0; 0] });
  ([UG; Ly], { metric = true; num = 1; den = 1; dims = [2; 0; -2; 0; 0; 0; 0] });
  ([US; Lv], { metric = true; num = 1; den = 1; dims = [2; 0; -2; 0; 0; 0; 0] });
  ([Lk; La; Lt], { metric = true; num = 1; den = 1; dims = [0; 0; -1; 0; 0; 1; 0] });
  ([UL], { metric = true; num = 1; den = 1000; dims = [3; 0; 0; 0; 0; 0; 0] });
  ([Lt], { metric = true; num = 1000000; den = 1; dims = [0; 1; 0; 0; 0; 0; 0] });
  ([Lb; La; Lr], { metric = true; num = 100000000; den = 1; dims = [-1; 1; -2; 0; 0; 0; 0] });
  ([Lm; Li; Ln], { metric = false; num = 60; den = 1; dims = [0; 0; 1; 0; 0; 0; 0] });
  ([Lh], { metric = false; num = 3600; den = 1; dims = [0; 0; 1; 0; 0; 0; 0] });
  ([Ld], { metric = false; num = 86400; den = 1; dims = [0; 0; 1; 0; 0; 0; 0] });
  ([Lw; Lk], { metric = false; num = 604800; den = 1; dims = [0; 0; 1; 0; 0; 0; 0] });
  ([La], { metric = false; num = 31557600; den = 1; dims = [0; 0; 1; 0; 0; 0; 0] });
  ([Pct], { metric = false; num = 1; den = 100; dims = [0; 0; 0; 0; 0; 0; 0] });
  ([LBr; Lp; Lp; Lm; RBr], { metric = false; num = 1; den = 1000000; dims = [0; 0; 0; 0; 0; 0; 0] });
  ([LBr; Lp; Lp; Lb; RBr], { metric = false; num = 1; den = 1000000000; dims = [0; 0; 0; 0; 0; 0; 0] });
  ([LBr; Li; Ln; Under; Li; RBr], { metric = false; num = 127; den = 5000; dims = [1; 0; 0; 0; 0; 0; 0] });
  ([LBr; Lf; Lt; Under; Li; RBr], { metric = false; num = 381; den = 1250; dims = [1; 0; 0; 0; 0; 0; 0] });
  ([LBr; Ly; Ld; Under; Li; RBr], { metric = false; num = 1143; den = 1250; dims = [1; 0; 0; 0; 0; 0; 0] });
  ([LBr; Lm; Li; Under; Li; RBr], { metric = false; num = 201168; den = 125; dims = [1; 0; 0; 0; 0; 0; 0] });
  ([LBr; Ll; Lb; Under; La; Lv; RBr], { metric = false; num = 45359237; den = 100000; dims = [0; 1; 0; 0; 0; 0; 0] });
  ([LBr; Lo; Lz; Under; La; Lv; RBr], { metric = false; num = 45359237; den = 1600000; dims = [0; 1; 0; 0; 0; 0; 0] }) ]

[@@"opaque_to_smt"]
let prefixes : list (text & int) = [
  ([Ld; La], 1);
  ([UY], 24);
  ([UZ], 21);
  ([UE], 18);
  ([UP], 15);
  ([UT], 12);
  ([UG], 9);
  ([UM], 6);
  ([Lk], 3);
  ([Lh], 2);
  ([Ld], -1);
  ([Lc], -2);
  ([Lm], -3);
  ([Lu], -6);
  ([Ln], -9);
  ([Lp], -12);
  ([Lf], -15);
  ([La], -18);
  ([Lz], -21);
  ([Ly], -24) ]

[@@"opaque_to_smt"]
let non_ratio : list text = [
  [UC; Le; Ll];
  [LBr; Ld; Le; Lg; UF; RBr];
  [LBr; Ld; Le; Lg; UR; Le; RBr];
  [LBr; Lp; UH; RBr];
  [UN; Lp];
  [UB];
  [UB; LBr; US; UP; UL; RBr];
  [UB; LBr; UV; RBr];
  [UB; LBr; Lm; UV; RBr];
  [UB; LBr; Lu; UV; RBr];
  [UB; LBr; D1; D0; Dot; Ln; UV; RBr];
  [UB; LBr; UW; RBr];
  [UB; LBr; Lk; UW; RBr];
  [Lb; Li; Lt; Under; Ls];
  [LBr; Lp; Apos; Ld; Li; Lo; Lp; RBr];
  [Pct; LBr; Ls; Ll; Lo; Lp; Le; RBr];
  [LBr; Lh; Lp; Under; UX; RBr];
  [LBr; Lh; Lp; Under; UC; RBr];
  [LBr; Lh; Lp; Under; UM; RBr];
  [LBr; Lh; Lp; Under; UQ; RBr];
  [LBr; Lk; Lp; Under; UX; RBr];
  [LBr; Lk; Lp; Under; UC; RBr];
  [LBr; Lk; Lp; Under; UM; RBr];
  [LBr; Lk; Lp; Under; UQ; RBr] ]

[@@"opaque_to_smt"]
let arbitrary : list text = [
  [LBr; Li; UU; RBr];
  [LBr; UI; UU; RBr];
  [LBr; La; Lr; Lb; Apos; UU; RBr];
  [LBr; UU; US; UP; Apos; UU; RBr];
  [LBr; UG; UP; UL; Apos; UU; RBr];
  [LBr; UM; UP; UL; Apos; UU; RBr];
  [LBr; UA; UP; UL; Apos; UU; RBr];
  [LBr; Lb; Le; Lt; Lh; Apos; UU; RBr];
  [LBr; La; Ln; Lt; Li; Apos; UX; La; Apos; UU; RBr];
  [LBr; Lt; Lo; Ld; Ld; Apos; UU; RBr];
  [LBr; Ld; Ly; Le; Apos; UU; RBr];
  [LBr; Ls; Lm; Lg; Ly; Apos; UU; RBr];
  [LBr; Lb; Ld; Ls; Lk; Apos; UU; RBr];
  [LBr; Lk; La; Apos; UU; RBr];
  [LBr; Lk; Ln; Lk; Apos; UU; RBr];
  [LBr; Lm; Lc; Ll; Lg; Apos; UU; RBr];
  [LBr; Lt; Lb; Apos; UU; RBr];
  [LBr; UC; UC; UI; UD; Under; D5; D0; RBr];
  [LBr; UT; UC; UI; UD; Under; D5; D0; RBr];
  [LBr; UE; UI; UD; Under; D5; D0; RBr];
  [LBr; UP; UF; UU; RBr];
  [LBr; UF; UF; UU; RBr];
  [LBr; UC; UF; UU; RBr];
  [LBr; UI; UR; RBr];
  [LBr; UB; UA; UU; RBr];
  [LBr; UA; UU; RBr];
  [LBr; UA; Lm; Lb; Apos; La; Apos; D1; Apos; UU; RBr];
  [LBr; UP; UN; UU; RBr];
  [LBr; UL; Lf; RBr];
  [LBr; UD; Apos; La; Lg; Apos; UU; RBr];
  [LBr; UF; UE; UU; RBr];
  [LBr; UE; UL; UU; RBr];
  [LBr; UE; UU; RBr] ]

let find_atom (s: text) : Tot (option atom_def) = assoc s atoms

(* F#: `aliases` — UCUM's lower-case litre reads as `L`. *)
let aliases : list (text & text) = [ ([Ll], [UL]) ]

(* F#: `isCurrency` — `[` three ASCII capitals `]`, and not one of UCUM's arbitrary units. *)
let is_upper (c: uch) : Tot bool = 65 <= code c && code c <= 90

let is_currency (tok: text) : Tot bool =
  match tok with
  | [LBr; a; b; c; RBr] -> is_upper a && is_upper b && is_upper c && not (mem tok arbitrary)
  | _ -> false

(* F#: `tryAtom` — the canonical symbol of an admitted atom, through the aliases. *)
let try_atom (tok: text) : Tot (option text) =
  let canonical = (match assoc tok aliases with Some s -> s | None -> tok) in
  if Some? (find_atom canonical) then Some canonical else None

(* F#: `prefixPower` — the power of ten a prefix stands for; `0` for none. *)
let prefix_power (p: text) : Tot int =
  if p = [] then 0 else (match assoc p prefixes with Some n -> n | None -> 0)

(* ======================================================================================
   3. THE FACTOR MAP (F#: `Map<UnitAtom, int64>` inside `tryCombine`), as a strictly sorted
      association list — and the group it forms. Written over any key type with a strict total
      order, because the dimension (section 5) is the same map over another key.
   ====================================================================================== *)

type smap (k: eqtype) = list (k & int)

let strict_order (#k: eqtype) (lt: k -> k -> bool) : prop =
  (forall (x: k). not (lt x x)) /\
  (forall (x y z: k). lt x y /\ lt y z ==> lt x z) /\
  (forall (x y: k). x <> y ==> lt x y \/ lt y x)

(* F#: `Map.add atom sum m` with `sum` the existing exponent plus the delta — a sorted insert that
   SUMS on an equal key. *)
let rec insert (#k: eqtype) (lt: k -> k -> bool) (a: k) (e: int) (m: smap k) : Tot (smap k) (decreases m) =
  match m with
  | [] -> [(a, e)]
  | (b, f) :: t -> if lt a b then (a, e) :: m else if a = b then (b, e + f) :: t else (b, f) :: insert lt a e t

(* F#: the `List.fold` of `tryCombine` — every factor of `b`, scaled by `sign`, added into `m`. *)
let rec add_all (#k: eqtype) (lt: k -> k -> bool) (m: smap k) (sign: int) (b: smap k)
  : Tot (smap k) (decreases b) =
  match b with
  | [] -> m
  | (a, e) :: t -> add_all lt (insert lt a (sign * e) m) sign t

(* F#: `List.filter (fun (_, e) -> e <> 0L)`. *)
let rec drop_zero (#k: eqtype) (m: smap k) : Tot (smap k) =
  match m with
  | [] -> []
  | (a, e) :: t -> if e = 0 then drop_zero t else (a, e) :: drop_zero t

(* F#: `tryCombine` without its range check — the merged product. *)
let combine (#k: eqtype) (lt: k -> k -> bool) (a: smap k) (sign: int) (b: smap k) : Tot (smap k) =
  drop_zero (add_all lt a sign b)

(* ---- well-formedness: strictly sorted, no zero exponent — what an F# map's `toList` is after
   the filter, and what every unit holds ---- *)

let rec sorted (#k: eqtype) (lt: k -> k -> bool) (m: smap k) : Tot bool =
  match m with
  | [] -> true
  | (a, _) :: t -> (match t with [] -> true | (b, _) :: _ -> lt a b && sorted lt t)

let rec no_zero (#k: eqtype) (m: smap k) : Tot bool =
  match m with
  | [] -> true
  | (_, e) :: t -> e <> 0 && no_zero t

let wf (#k: eqtype) (lt: k -> k -> bool) (m: smap k) : Tot bool = sorted lt m && no_zero m

(* The exponent of `x` in `m`: the exponent vector read at one coordinate. *)
let rec exp_of (#k: eqtype) (m: smap k) (x: k) : Tot int =
  match m with
  | [] -> 0
  | (a, e) :: t -> if a = x then e else exp_of t x

(* Every key of `m` is above `a`. *)
let rec above (#k: eqtype) (lt: k -> k -> bool) (a: k) (m: smap k) : Tot bool =
  match m with
  | [] -> true
  | (b, _) :: t -> lt a b && above lt a t

let rec above_trans (#k: eqtype) (lt: k -> k -> bool) (x y: k) (m: smap k)
  : Lemma (requires strict_order lt /\ lt x y /\ above lt y m) (ensures above lt x m) =
  match m with
  | [] -> ()
  | _ :: t -> above_trans lt x y t

let rec sorted_tail (#k: eqtype) (lt: k -> k -> bool) (a: k) (e: int) (t: smap k)
  : Lemma (requires strict_order lt /\ sorted lt ((a, e) :: t)) (ensures sorted lt t /\ above lt a t) (decreases t) =
  match t with
  | [] -> ()
  | (b, f) :: t' -> sorted_tail lt b f t'; above_trans lt a b t'

let sorted_cons (#k: eqtype) (lt: k -> k -> bool) (a: k) (e: int) (t: smap k)
  : Lemma (requires strict_order lt /\ sorted lt t /\ above lt a t) (ensures sorted lt ((a, e) :: t)) =
  match t with
  | [] -> ()
  | _ -> ()

let rec above_exp (#k: eqtype) (lt: k -> k -> bool) (a: k) (m: smap k)
  : Lemma (requires strict_order lt /\ above lt a m) (ensures exp_of m a == 0) =
  match m with
  | [] -> ()
  | _ :: t -> above_exp lt a t

let rec above_lt_exp (#k: eqtype) (lt: k -> k -> bool) (x y: k) (m: smap k)
  : Lemma (requires strict_order lt /\ lt x y /\ above lt y m) (ensures exp_of m x == 0) =
  match m with
  | [] -> ()
  | _ :: t -> above_lt_exp lt x y t

let rec insert_above (#k: eqtype) (lt: k -> k -> bool) (x a: k) (e: int) (m: smap k)
  : Lemma (requires strict_order lt /\ above lt x m /\ lt x a) (ensures above lt x (insert lt a e m)) =
  match m with
  | [] -> ()
  | (b, _) :: t -> if lt a b then () else if a = b then () else insert_above lt x a e t

let rec insert_sorted (#k: eqtype) (lt: k -> k -> bool) (a: k) (e: int) (m: smap k)
  : Lemma (requires strict_order lt /\ sorted lt m) (ensures sorted lt (insert lt a e m)) =
  match m with
  | [] -> ()
  | (b, f) :: t ->
      sorted_tail lt b f t;
      if lt a b then ()
      else if a = b then sorted_cons lt b (e + f) t
      else (insert_sorted lt a e t; insert_above lt b a e t; sorted_cons lt b f (insert lt a e t))

let rec insert_exp (#k: eqtype) (lt: k -> k -> bool) (a: k) (e: int) (m: smap k) (y: k)
  : Lemma (requires strict_order lt /\ sorted lt m)
          (ensures exp_of (insert lt a e m) y == exp_of m y + (if y = a then e else 0)) =
  match m with
  | [] -> ()
  | (b, f) :: t ->
      sorted_tail lt b f t;
      if lt a b then above_lt_exp lt a b t
      else if a = b then ()
      else insert_exp lt a e t y

let rec add_all_sorted (#k: eqtype) (lt: k -> k -> bool) (m: smap k) (s: int) (b: smap k)
  : Lemma (requires strict_order lt /\ sorted lt m) (ensures sorted lt (add_all lt m s b)) (decreases b) =
  match b with
  | [] -> ()
  | (a, e) :: t -> insert_sorted lt a (s * e) m; add_all_sorted lt (insert lt a (s * e) m) s t

let rec add_all_exp (#k: eqtype) (lt: k -> k -> bool) (m: smap k) (s: int) (b: smap k) (y: k)
  : Lemma (requires strict_order lt /\ sorted lt m /\ sorted lt b)
          (ensures exp_of (add_all lt m s b) y == exp_of m y + s * exp_of b y) (decreases b) =
  match b with
  | [] -> ()
  | (a, e) :: t ->
      sorted_tail lt a e t;
      insert_sorted lt a (s * e) m;
      insert_exp lt a (s * e) m y;
      add_all_exp lt (insert lt a (s * e) m) s t y;
      if y = a then above_exp lt a t else ()

let rec drop_zero_above (#k: eqtype) (lt: k -> k -> bool) (a: k) (m: smap k)
  : Lemma (requires above lt a m) (ensures above lt a (drop_zero m)) =
  match m with
  | [] -> ()
  | _ :: t -> drop_zero_above lt a t

let rec drop_zero_sorted (#k: eqtype) (lt: k -> k -> bool) (m: smap k)
  : Lemma (requires strict_order lt /\ sorted lt m) (ensures sorted lt (drop_zero m)) =
  match m with
  | [] -> ()
  | (a, e) :: t ->
      sorted_tail lt a e t; drop_zero_sorted lt t;
      if e = 0 then () else (drop_zero_above lt a t; sorted_cons lt a e (drop_zero t))

let rec drop_zero_no_zero (#k: eqtype) (m: smap k) : Lemma (no_zero (drop_zero m)) =
  match m with
  | [] -> ()
  | _ :: t -> drop_zero_no_zero t

let rec drop_zero_exp (#k: eqtype) (lt: k -> k -> bool) (m: smap k) (y: k)
  : Lemma (requires strict_order lt /\ sorted lt m) (ensures exp_of (drop_zero m) y == exp_of m y) =
  match m with
  | [] -> ()
  | (a, e) :: t -> sorted_tail lt a e t; drop_zero_exp lt t y; if e = 0 && y = a then above_exp lt a t else ()

let rec drop_zero_id (#k: eqtype) (m: smap k) : Lemma (requires no_zero m) (ensures drop_zero m == m) =
  match m with
  | [] -> ()
  | _ :: t -> drop_zero_id t

let combine_wf (#k: eqtype) (lt: k -> k -> bool) (a: smap k) (s: int) (b: smap k)
  : Lemma (requires strict_order lt /\ sorted lt a) (ensures wf lt (combine lt a s b)) =
  add_all_sorted lt a s b; drop_zero_sorted lt (add_all lt a s b); drop_zero_no_zero (add_all lt a s b)

(* THE EXPONENT LAW: the merge adds exponent vectors. *)
let combine_exp (#k: eqtype) (lt: k -> k -> bool) (a: smap k) (s: int) (b: smap k) (y: k)
  : Lemma (requires strict_order lt /\ sorted lt a /\ sorted lt b)
          (ensures exp_of (combine lt a s b) y == exp_of a y + s * exp_of b y) =
  add_all_sorted lt a s b; drop_zero_exp lt (add_all lt a s b) y; add_all_exp lt a s b y

(* EXTENSIONALITY: two well-formed maps with one exponent vector are one list. *)
let rec ext (#k: eqtype) (lt: k -> k -> bool) (a b: smap k)
  : Lemma (requires strict_order lt /\ wf lt a /\ wf lt b /\ (forall (y: k). exp_of a y == exp_of b y))
          (ensures a == b) (decreases a) =
  match a, b with
  | [], [] -> ()
  | [], (y, _) :: _ -> assert (exp_of a y == 0)
  | (x, _) :: _, [] -> assert (exp_of b x == 0)
  | (x, e) :: ta, (y, f) :: tb ->
      sorted_tail lt x e ta; sorted_tail lt y f tb;
      above_exp lt x ta; above_exp lt y tb;
      if x = y then ext lt ta tb
      else if lt x y then above_lt_exp lt x y tb
      else above_lt_exp lt y x ta

(* ---- THE GROUP ---- *)

let combine_comm (#k: eqtype) (lt: k -> k -> bool) (a b: smap k)
  : Lemma (requires strict_order lt /\ wf lt a /\ wf lt b) (ensures combine lt a 1 b == combine lt b 1 a) =
  combine_wf lt a 1 b; combine_wf lt b 1 a;
  let aux (y: k) : Lemma (exp_of (combine lt a 1 b) y == exp_of (combine lt b 1 a) y) =
    combine_exp lt a 1 b y; combine_exp lt b 1 a y in
  FStar.Classical.forall_intro aux;
  ext lt (combine lt a 1 b) (combine lt b 1 a)

let combine_assoc (#k: eqtype) (lt: k -> k -> bool) (a b c: smap k)
  : Lemma (requires strict_order lt /\ wf lt a /\ wf lt b /\ wf lt c)
          (ensures combine lt (combine lt a 1 b) 1 c == combine lt a 1 (combine lt b 1 c)) =
  combine_wf lt a 1 b; combine_wf lt b 1 c;
  combine_wf lt (combine lt a 1 b) 1 c; combine_wf lt a 1 (combine lt b 1 c);
  let aux (y: k) : Lemma (exp_of (combine lt (combine lt a 1 b) 1 c) y == exp_of (combine lt a 1 (combine lt b 1 c)) y) =
    combine_exp lt a 1 b y; combine_exp lt b 1 c y;
    combine_exp lt (combine lt a 1 b) 1 c y; combine_exp lt a 1 (combine lt b 1 c) y in
  FStar.Classical.forall_intro aux;
  ext lt (combine lt (combine lt a 1 b) 1 c) (combine lt a 1 (combine lt b 1 c))

let combine_unit (#k: eqtype) (lt: k -> k -> bool) (a: smap k)
  : Lemma (requires strict_order lt /\ wf lt a) (ensures combine lt a 1 [] == a /\ combine lt [] 1 a == a) =
  drop_zero_id a;
  combine_wf lt [] 1 a;
  let aux (y: k) : Lemma (exp_of (combine lt [] 1 a) y == exp_of a y) = combine_exp lt [] 1 a y in
  FStar.Classical.forall_intro aux;
  ext lt (combine lt [] 1 a) a

let combine_inverse (#k: eqtype) (lt: k -> k -> bool) (a: smap k)
  : Lemma (requires strict_order lt /\ wf lt a) (ensures combine lt a (-1) a == []) =
  combine_wf lt a (-1) a;
  let aux (y: k) : Lemma (exp_of (combine lt a (-1) a) y == 0) = combine_exp lt a (-1) a y in
  FStar.Classical.forall_intro aux;
  ext lt (combine lt a (-1) a) []

(* F#: the `List.map` of `pow` — every exponent scaled by `n`. *)
let rec scale_exps (#k: eqtype) (m: smap k) (n: int) : Tot (smap k) =
  match m with
  | [] -> []
  | (a, e) :: t -> (a, e * n) :: scale_exps t n

let rec scale_exps_exp (#k: eqtype) (m: smap k) (n: int) (y: k)
  : Lemma (exp_of (scale_exps m n) y == exp_of m y * n) =
  match m with
  | [] -> ()
  | _ :: t -> scale_exps_exp t n y

let rec above_scaled (#k: eqtype) (lt: k -> k -> bool) (x: k) (l: smap k) (n: int)
  : Lemma (requires above lt x l) (ensures above lt x (scale_exps l n)) =
  match l with
  | [] -> ()
  | _ :: l' -> above_scaled lt x l' n

let rec scale_exps_wf (#k: eqtype) (lt: k -> k -> bool) (m: smap k) (n: int)
  : Lemma (requires strict_order lt /\ wf lt m /\ n <> 0) (ensures wf lt (scale_exps m n)) =
  match m with
  | [] -> ()
  | (a, e) :: t ->
      sorted_tail lt a e t; scale_exps_wf lt t n;
      above_scaled lt a t n; sorted_cons lt a (e * n) (scale_exps t n)

(* `div` is multiplication by the inverse: `combine a (-1) b == combine a 1 (pow b (-1))`. *)
let div_is_mul_inverse (#k: eqtype) (lt: k -> k -> bool) (a b: smap k)
  : Lemma (requires strict_order lt /\ wf lt a /\ wf lt b)
          (ensures combine lt a (-1) b == combine lt a 1 (scale_exps b (-1))) =
  scale_exps_wf lt b (-1);
  combine_wf lt a (-1) b; combine_wf lt a 1 (scale_exps b (-1));
  let aux (y: k) : Lemma (exp_of (combine lt a (-1) b) y == exp_of (combine lt a 1 (scale_exps b (-1))) y) =
    combine_exp lt a (-1) b y; combine_exp lt a 1 (scale_exps b (-1)) y; scale_exps_exp b (-1) y in
  FStar.Classical.forall_intro aux;
  ext lt (combine lt a (-1) b) (combine lt a 1 (scale_exps b (-1)))

(* ======================================================================================
   4. THE UNIT (F#: `UnitOfMeasure`, `Unit.dimensionless` / `mul` / `div` / `pow`).
   ====================================================================================== *)

(* F#: `compare` on `UnitAtom` — symbol, then prefix, ordinally. *)
let atom_cmp (a b: atom) : Tot int =
  let c = cmp_text a.symbol b.symbol in
  if c <> 0 then c else cmp_text a.prefix b.prefix

let atom_lt (a b: atom) : Tot bool = atom_cmp a b < 0

let atom_lt_irrefl (x: atom) : Lemma (not (atom_lt x x)) =
  cmp_text_refl x.symbol; cmp_text_refl x.prefix

let atom_lt_trans (x y z: atom) : Lemma (requires atom_lt x y /\ atom_lt y z) (ensures atom_lt x z) =
  let sxy = cmp_text x.symbol y.symbol in
  let syz = cmp_text y.symbol z.symbol in
  if sxy < 0 && syz < 0 then cmp_text_trans x.symbol y.symbol z.symbol
  else if sxy < 0 then cmp_text_zero y.symbol z.symbol
  else if syz < 0 then cmp_text_zero x.symbol y.symbol
  else (cmp_text_zero x.symbol y.symbol; cmp_text_zero y.symbol z.symbol; cmp_text_trans x.prefix y.prefix z.prefix)

let atom_lt_total (x y: atom) : Lemma (requires x <> y) (ensures atom_lt x y \/ atom_lt y x) =
  cmp_text_anti x.symbol y.symbol; cmp_text_anti x.prefix y.prefix;
  if cmp_text x.symbol y.symbol = 0 then (
    cmp_text_zero x.symbol y.symbol;
    if cmp_text x.prefix y.prefix = 0 then cmp_text_zero x.prefix y.prefix else ())
  else ()

let atom_lt_order () : Lemma (strict_order atom_lt) =
  FStar.Classical.forall_intro atom_lt_irrefl;
  let trans' (x y z: atom) : Lemma (atom_lt x y /\ atom_lt y z ==> atom_lt x z) =
    FStar.Classical.move_requires (atom_lt_trans x y) z in
  FStar.Classical.forall_intro_3 trans';
  let total' (x y: atom) : Lemma (x <> y ==> atom_lt x y \/ atom_lt y x) =
    FStar.Classical.move_requires (atom_lt_total x) y in
  FStar.Classical.forall_intro_2 total'

type factors = smap atom

(* F#: `UnitOfMeasure` — `{ Factors: (UnitAtom * int) list }`. *)
type uom = { factors: factors }

(* F#: `Int32.MaxValue`, the bound `checkedExp` and `tryCombine` hold every exponent to. *)
let max_exp : nat = 2147483647

let in_range (e: int) : Tot bool = e <= max_exp && 0 - max_exp <= e

let rec all_in_range (m: factors) : Tot bool =
  match m with
  | [] -> true
  | (_, e) :: t -> in_range e && all_in_range t

(* F#: `tryCombine` — `None` where an exponent leaves the range. Production checks the bound at
   every fold step and this checks the merged result: equal because each atom of `b` is written
   once into a map whose own exponents are in range, so the step's sum IS the result's exponent. *)
let try_combine (a: factors) (sign: int) (b: factors) : Tot (option factors) =
  let r = combine atom_lt a sign b in
  if all_in_range r then Some r else None

let of_factors (f: factors) : Tot uom = { factors = f }

(* F#: `Unit.dimensionless` — the identity of `mul`. *)
let dimensionless : uom = of_factors []

(* F#: `Unit.mul` / `div` — `None` is the `ArgumentException`. *)
let mul (a b: uom) : Tot (option uom) =
  match try_combine a.factors 1 b.factors with
  | Some f -> Some (of_factors f)
  | None -> None

let div (a b: uom) : Tot (option uom) =
  match try_combine a.factors (-1) b.factors with
  | Some f -> Some (of_factors f)
  | None -> None

(* F#: `Unit.pow` — `pow u 0` is `dimensionless`; otherwise every exponent scaled, `None` where one
   leaves the range (`checkedExp`). *)
let pow (u: uom) (n: int) : Tot (option uom) =
  if n = 0 then Some dimensionless
  else
    let f = scale_exps u.factors n in
    if all_in_range f then Some (of_factors f) else None

(* The structural well-formedness every unit production builds has, and what the theorems of
   sections 3 and 5 to 7 ask of their arguments. *)
let wf_unit (u: uom) : Tot bool = wf atom_lt u.factors && all_in_range u.factors

let mul_wf (a b: uom) : Lemma (requires wf_unit a /\ wf_unit b /\ Some? (mul a b)) (ensures wf_unit (Some?.v (mul a b))) =
  atom_lt_order (); combine_wf atom_lt a.factors 1 b.factors

let div_wf (a b: uom) : Lemma (requires wf_unit a /\ wf_unit b /\ Some? (div a b)) (ensures wf_unit (Some?.v (div a b))) =
  atom_lt_order (); combine_wf atom_lt a.factors (-1) b.factors

let pow_wf (u: uom) (n: int) : Lemma (requires wf_unit u /\ Some? (pow u n)) (ensures wf_unit (Some?.v (pow u n))) =
  atom_lt_order (); if n = 0 then () else scale_exps_wf atom_lt u.factors n

(* F#: `Result<'T, 'E>` — `Ok`, and the refusal. *)
type result (a e: Type) =
  | Ok      : v:a -> result a e
  | Refused : r:e -> result a e

(* ======================================================================================
   5. THE DIMENSION (F#: `DimKey`, `dimension`, `Unit.compatible`).
   ====================================================================================== *)

(* F#: `DimKey` — a base dimension by index, or a currency. *)
type dim_key =
  | Base     : i:nat -> dim_key
  | Currency : s:text -> dim_key

(* F#: `compare` on `DimKey` — the union's cases in declaration order, then the payload. *)
let dim_cmp (a b: dim_key) : Tot int =
  match a, b with
  | Base i, Base j -> if i < j then -1 else if i > j then 1 else 0
  | Base _, Currency _ -> -1
  | Currency _, Base _ -> 1
  | Currency s, Currency t -> cmp_text s t

let dim_lt (a b: dim_key) : Tot bool = dim_cmp a b < 0

let dim_lt_irrefl (x: dim_key) : Lemma (not (dim_lt x x)) =
  match x with
  | Currency s -> cmp_text_refl s
  | _ -> ()

let dim_lt_trans (x y z: dim_key) : Lemma (requires dim_lt x y /\ dim_lt y z) (ensures dim_lt x z) =
  match x, y, z with
  | Currency s, Currency t, Currency u -> cmp_text_trans s t u
  | _ -> ()

let dim_lt_total (x y: dim_key) : Lemma (requires x <> y) (ensures dim_lt x y \/ dim_lt y x) =
  match x, y with
  | Currency s, Currency t -> cmp_text_anti s t; if cmp_text s t = 0 then cmp_text_zero s t else ()
  | _ -> ()

let dim_lt_order () : Lemma (strict_order dim_lt) =
  FStar.Classical.forall_intro dim_lt_irrefl;
  let trans' (x y z: dim_key) : Lemma (dim_lt x y /\ dim_lt y z ==> dim_lt x z) =
    FStar.Classical.move_requires (dim_lt_trans x y) z in
  FStar.Classical.forall_intro_3 trans';
  let total' (x y: dim_key) : Lemma (x <> y ==> dim_lt x y \/ dim_lt y x) =
    FStar.Classical.move_requires (dim_lt_total x) y in
  FStar.Classical.forall_intro_2 total'

(* F#: the `List.mapi` inside `dimension` — the atom's exponents over the seven base dimensions,
   each times the factor's exponent, keyed by index. *)
let rec base_dims (ds: list int) (i: nat) (e: int) : Tot (list (dim_key & int)) =
  match ds with
  | [] -> []
  | x :: t -> (Base i, x * e) :: base_dims t (i + 1) e

(* F#: the `List.collect` — one currency entry, or the base entries. The `None` arm is unreachable
   for an atom `parse` admitted (every admitted symbol is in the table); production indexes the
   table and would throw. *)
let atom_dims (a: atom) (e: int) : Tot (list (dim_key & int)) =
  if is_currency a.symbol then [(Currency a.symbol, e)]
  else (match find_atom a.symbol with Some d -> base_dims d.dims 0 e | None -> [])

(* F#: the `List.fold` into the map — every entry added onto what is there. *)
let rec add_entries (m: smap dim_key) (es: list (dim_key & int)) : Tot (smap dim_key) (decreases es) =
  match es with
  | [] -> m
  | (k, x) :: t -> add_entries (insert dim_lt k x m) t

let rec dim_acc (m: smap dim_key) (fs: factors) : Tot (smap dim_key) (decreases fs) =
  match fs with
  | [] -> m
  | (a, e) :: t -> dim_acc (add_entries m (atom_dims a e)) t

(* F#: `dimension` — the non-zero entries. *)
let dimension (u: uom) : Tot (smap dim_key) = drop_zero (dim_acc [] u.factors)

(* F#: `Unit.compatible` — equality of dimension. *)
let compatible (a b: uom) : Tot bool = dimension a = dimension b

(* ---- the dimension read directly off the factors, without the map ---- *)

let rec entries_val (es: list (dim_key & int)) (k: dim_key) : Tot int =
  match es with
  | [] -> 0
  | (j, x) :: t -> (if j = k then x else 0) + entries_val t k

(* The exponent of base dimension or currency `k` in a product of factors: the sum over the
   factors of the atom's exponent at `k` times the factor's exponent. *)
let rec dim_val (fs: factors) (k: dim_key) : Tot int =
  match fs with
  | [] -> 0
  | (a, e) :: t -> entries_val (atom_dims a e) k + dim_val t k

let rec add_entries_exp (m: smap dim_key) (es: list (dim_key & int)) (k: dim_key)
  : Lemma (requires sorted dim_lt m)
          (ensures sorted dim_lt (add_entries m es) /\ exp_of (add_entries m es) k == exp_of m k + entries_val es k) (decreases es) =
  dim_lt_order ();
  match es with
  | [] -> ()
  | (j, x) :: t -> insert_sorted dim_lt j x m; insert_exp dim_lt j x m k; add_entries_exp (insert dim_lt j x m) t k

let rec dim_acc_exp (m: smap dim_key) (fs: factors) (k: dim_key)
  : Lemma (requires sorted dim_lt m)
          (ensures sorted dim_lt (dim_acc m fs) /\ exp_of (dim_acc m fs) k == exp_of m k + dim_val fs k) (decreases fs) =
  match fs with
  | [] -> ()
  | (a, e) :: t -> add_entries_exp m (atom_dims a e) k; dim_acc_exp (add_entries m (atom_dims a e)) t k

let dimension_exp (u: uom) (k: dim_key)
  : Lemma (wf dim_lt (dimension u) /\ exp_of (dimension u) k == dim_val u.factors k) =
  dim_lt_order (); dim_acc_exp [] u.factors k;
  drop_zero_sorted dim_lt (dim_acc [] u.factors); drop_zero_no_zero (dim_acc [] u.factors);
  drop_zero_exp dim_lt (dim_acc [] u.factors) k

(* THEOREM 3. `compatible` IS equality of dimension: every base dimension's and every currency's
   exponent agrees. *)
let compatible_iff (a b: uom)
  : Lemma (compatible a b <==> (forall (k: dim_key). dim_val a.factors k == dim_val b.factors k)) =
  dim_lt_order ();
  FStar.Classical.forall_intro (dimension_exp a);
  FStar.Classical.forall_intro (dimension_exp b);
  FStar.Classical.move_requires (ext dim_lt (dimension a)) (dimension b)

(* ---- the dimension of a product is the sum ---- *)

let rec base_dims_lin (ds: list int) (i: nat) (e f: int) (k: dim_key)
  : Lemma (entries_val (base_dims ds i (e + f)) k == entries_val (base_dims ds i e) k + entries_val (base_dims ds i f) k) =
  match ds with
  | [] -> ()
  | x :: t -> distributivity_add_right x e f; base_dims_lin t (i + 1) e f k

let entries_lin (a: atom) (e f: int) (k: dim_key)
  : Lemma (entries_val (atom_dims a (e + f)) k == entries_val (atom_dims a e) k + entries_val (atom_dims a f) k) =
  if is_currency a.symbol then ()
  else (match find_atom a.symbol with Some d -> base_dims_lin d.dims 0 e f k | None -> ())

let rec base_dims_scale (ds: list int) (i: nat) (s e: int) (k: dim_key)
  : Lemma (entries_val (base_dims ds i (s * e)) k == s * entries_val (base_dims ds i e) k) =
  match ds with
  | [] -> ()
  | x :: t ->
      base_dims_scale t (i + 1) s e k;
      paren_mul_right x s e; swap_mul x s; paren_mul_right s x e;
      distributivity_add_right s (if Base i = k then x * e else 0) (entries_val (base_dims t (i + 1) e) k)

let entries_scale (a: atom) (s e: int) (k: dim_key)
  : Lemma (entries_val (atom_dims a (s * e)) k == s * entries_val (atom_dims a e) k) =
  if is_currency a.symbol then ()
  else (match find_atom a.symbol with Some d -> base_dims_scale d.dims 0 s e k | None -> ())

let rec dim_val_insert (a: atom) (e: int) (m: factors) (k: dim_key)
  : Lemma (dim_val (insert atom_lt a e m) k == dim_val m k + entries_val (atom_dims a e) k) =
  match m with
  | [] -> ()
  | (b, f) :: t ->
      if atom_lt a b then ()
      else if a = b then entries_lin a e f k
      else dim_val_insert a e t k

let rec dim_val_add_all (m: factors) (s: int) (b: factors) (k: dim_key)
  : Lemma (ensures dim_val (add_all atom_lt m s b) k == dim_val m k + s * dim_val b k) (decreases b) =
  match b with
  | [] -> ()
  | (a, e) :: t ->
      dim_val_insert a (s * e) m k;
      entries_scale a s e k;
      dim_val_add_all (insert atom_lt a (s * e) m) s t k;
      distributivity_add_right s (entries_val (atom_dims a e) k) (dim_val t k)

let rec dim_val_drop_zero (m: factors) (k: dim_key) : Lemma (dim_val (drop_zero m) k == dim_val m k) =
  match m with
  | [] -> ()
  | (a, e) :: t -> dim_val_drop_zero t k; if e = 0 then entries_scale a 0 e k else ()

let dim_combine (a: factors) (s: int) (b: factors) (k: dim_key)
  : Lemma (dim_val (combine atom_lt a s b) k == dim_val a k + s * dim_val b k) =
  dim_val_drop_zero (add_all atom_lt a s b) k; dim_val_add_all a s b k

(* Compatibility is a congruence for the product: compatible factors multiply to compatible
   products, so a consumer that checks the operands has checked the result. *)
let compatible_mul (a b c d: uom)
  : Lemma (requires compatible a c /\ compatible b d /\ Some? (mul a b) /\ Some? (mul c d))
          (ensures compatible (Some?.v (mul a b)) (Some?.v (mul c d))) =
  compatible_iff a c; compatible_iff b d;
  compatible_iff (Some?.v (mul a b)) (Some?.v (mul c d));
  FStar.Classical.forall_intro (dim_combine a.factors 1 b.factors);
  FStar.Classical.forall_intro (dim_combine c.factors 1 d.factors)

(* ======================================================================================
   6. THE SCALE AND THE CONVERSION FACTOR (F#: `Ratio`, `scale`, `Unit.conversionFactor`).
   ====================================================================================== *)

(* F#: `powNat`, by squaring there; by multiplication here (the value, not the loop, is what the
   theorem is about). *)
let rec powi (b: pos) (n: nat) : Tot pos =
  if n = 0 then 1 else (let p = powi b (n - 1) in pos_times_pos_is_pos b p; b * p)

(* F#: `Ratio` — a positive rational as numerator and denominator; `reduce` takes it to lowest
   terms. `req` is equality of the VALUE, `=` of the representation. *)
type rat = { rn: pos; rd: pos }

let one : rat = { rn = 1; rd = 1 }

let rmul (p q: rat) : Tot rat =
  pos_times_pos_is_pos p.rn q.rn; pos_times_pos_is_pos p.rd q.rd;
  { rn = p.rn * q.rn; rd = p.rd * q.rd }

let rinv (p: rat) : Tot rat = { rn = p.rd; rd = p.rn }

let req (p q: rat) : Tot bool = p.rn * q.rd = q.rn * p.rd

(* `r` to an integer power: the inverse's magnitude for a negative one. *)
let rpow (r: rat) (e: int) : Tot rat =
  if e >= 0 then { rn = powi r.rn e; rd = powi r.rd e } else { rn = powi r.rd (0 - e); rd = powi r.rn (0 - e) }

let ten : rat = { rn = 10; rd = 1 }

(* F#: what one factor contributes to `scale` — the prefix's power of ten and, for an admitted atom,
   its `Num / Den` relative to the coherent base; a currency contributes only its prefix. The `None`
   arm is unreachable for an admitted atom, as at `atom_dims`. *)
let atom_base (a: atom) : Tot rat =
  let pre = rpow ten (prefix_power a.prefix) in
  if is_currency a.symbol then pre
  else (match find_atom a.symbol with Some d -> rmul pre { rn = d.num; rd = d.den } | None -> pre)

let factor_rat (a: atom) (e: int) : Tot rat = rpow (atom_base a) e

(* The product of the factors' scales: `scale` before its gcd. *)
let rec scale_rat (m: factors) : Tot rat =
  match m with
  | [] -> one
  | (a, e) :: t -> rmul (factor_rat a e) (scale_rat t)

(* F#: `gcd` — Euclid's. *)
let rec gcd (a: pos) (b: nat) : Tot pos (decreases b) = if b = 0 then a else gcd b (a % b)

let rec gcd_divides (a: pos) (b: nat) : Lemma (ensures a % gcd a b = 0 /\ b % gcd a b = 0) (decreases b) =
  if b = 0 then ()
  else (
    gcd_divides b (a % b);
    let g = gcd b (a % b) in
    euclidean_division_definition a b;
    mod_divides b g; mod_divides (a % b) g;
    divides_mult_right (a / b) b g;
    divides_plus ((a / b) * b) (a % b) g;
    divides_mod a g)

let div_pos (a g: pos) : Lemma (requires a % g = 0) (ensures a / g > 0) =
  euclidean_division_definition a g;
  if a / g <= 0 then lemma_mult_le_right g (a / g) 0 else ()

(* F#: the `{ Num = num / g; Den = den / g }` of `scale` — lowest terms. *)
let reduce (r: rat) : Tot rat =
  let g = gcd r.rn r.rd in
  gcd_divides r.rn r.rd; div_pos r.rn g; div_pos r.rd g;
  { rn = r.rn / g; rd = r.rd / g }

(* F#: `scale` — the unit's scale relative to the coherent base, in lowest terms. *)
let scale (u: uom) : Tot rat = reduce (scale_rat u.factors)

(* F#: `UnitConversionRefusal`. *)
type conv_refusal =
  | Incompatible : source:uom -> target:uom -> conv_refusal

(* F#: `Unit.conversionFactor` — the scale of `source / target` where the two are compatible;
   `None` is the `ArgumentException` `div` raises on an exponent outside the range. *)
let conversion_factor (source target: uom) : Tot (option (result rat conv_refusal)) =
  if compatible source target then
    (match div source target with
     | Some d -> Some (Ok (scale d))
     | None -> None)
  else Some (Refused (Incompatible source target))

(* ---- the rational algebra: `req` is an equivalence and a congruence for `rmul` ---- *)

let req_refl (p: rat) : Lemma (req p p) = ()

let req_sym (p q: rat) : Lemma (requires req p q) (ensures req q p) = ()

let req_trans (p q r: rat) : Lemma (requires req p q /\ req q r) (ensures req p r) =
  (* p.rn * q.rd = q.rn * p.rd and q.rn * r.rd = r.rn * q.rd give
     (p.rn * r.rd) * (q.rn * q.rd) = (r.rn * p.rd) * (q.rn * q.rd); cancel the positive factor. *)
  pos_times_pos_is_pos q.rn q.rd;
  assert ((p.rn * q.rd) * (q.rn * r.rd) = (q.rn * p.rd) * (r.rn * q.rd));
  paren_mul_right p.rn q.rd (q.rn * r.rd); paren_mul_right q.rn p.rd (r.rn * q.rd);
  assert (p.rn * (q.rd * (q.rn * r.rd)) = (p.rn * r.rd) * (q.rn * q.rd)) by (FStar.Tactics.Canon.canon ());
  assert (q.rn * (p.rd * (r.rn * q.rd)) = (r.rn * p.rd) * (q.rn * q.rd)) by (FStar.Tactics.Canon.canon ());
  lemma_cancel_mul (p.rn * r.rd) (r.rn * p.rd) (q.rn * q.rd)

let rmul_comm (p q: rat) : Lemma (rmul p q == rmul q p) = swap_mul p.rn q.rn; swap_mul p.rd q.rd

let rmul_assoc (p q r: rat) : Lemma (rmul (rmul p q) r == rmul p (rmul q r)) =
  paren_mul_right p.rn q.rn r.rn; paren_mul_right p.rd q.rd r.rd

let rmul_one (p: rat) : Lemma (rmul one p == p /\ rmul p one == p) = ()

let rmul_congr (p p' q q': rat) : Lemma (requires req p p' /\ req q q') (ensures req (rmul p q) (rmul p' q')) =
  assert ((p.rn * q.rn) * (p'.rd * q'.rd) = (p'.rn * q'.rn) * (p.rd * q.rd)) by (FStar.Tactics.Canon.canon ())

let rinv_cancel (p: rat) : Lemma (req (rmul (rinv p) p) one) = swap_mul p.rd p.rn

let rinv_rmul (p q: rat) : Lemma (rinv (rmul p q) == rmul (rinv p) (rinv q)) = ()

(* ---- powers ---- *)

let rec powi_add (b: pos) (m n: nat) : Lemma (powi b (m + n) == powi b m * powi b n) =
  if m = 0 then ()
  else (powi_add b (m - 1) n; paren_mul_right b (powi b (m - 1)) (powi b n))

(* The signed power's two legs. *)
let spn (r: rat) (e: int) : Tot pos = if e >= 0 then powi r.rn e else powi r.rd (0 - e)
let spd (r: rat) (e: int) : Tot pos = if e >= 0 then powi r.rd e else powi r.rn (0 - e)

let rpow_legs (r: rat) (e: int) : Lemma (rpow r e == { rn = spn r e; rd = spd r e }) = ()

(* `r^(e+f)` is `r^e * r^f` as a value. *)
let rpow_add (r: rat) (e f: int) : Lemma (req (rpow r (e + f)) (rmul (rpow r e) (rpow r f))) =
  let n = r.rn in let d = r.rd in
  if e >= 0 && f >= 0 then (powi_add n e f; powi_add d e f;
    assert ((powi n e * powi n f) * (powi d e * powi d f) = (powi n e * powi n f) * (powi d e * powi d f)) by (FStar.Tactics.Canon.canon ()))
  else if e < 0 && f < 0 then (powi_add d (0 - e) (0 - f); powi_add n (0 - e) (0 - f);
    assert ((powi d (0 - e) * powi d (0 - f)) * (powi n (0 - e) * powi n (0 - f)) = (powi d (0 - e) * powi d (0 - f)) * (powi n (0 - e) * powi n (0 - f))) by (FStar.Tactics.Canon.canon ()))
  else if e >= 0 && f < 0 then (
    if e + f >= 0 then (
      (* n^e = n^(e+f) n^(-f); d^e = d^(e+f) d^(-f) *)
      powi_add n (e + f) (0 - f); powi_add d (e + f) (0 - f);
      assert (powi n (e + f) * (powi d (e + f) * powi d (0 - f) * powi n (0 - f))
              = (powi n (e + f) * powi n (0 - f) * powi d (0 - f)) * powi d (e + f)) by (FStar.Tactics.Canon.canon ()))
    else (
      (* -f = e + (-(e+f)): n^(-f) = n^e n^(-(e+f)); d^(-f) = d^e d^(-(e+f)) *)
      powi_add n e (0 - (e + f)); powi_add d e (0 - (e + f));
      assert (powi d (0 - (e + f)) * (powi d e * (powi n e * powi n (0 - (e + f))))
              = (powi n e * (powi d e * powi d (0 - (e + f)))) * powi n (0 - (e + f))) by (FStar.Tactics.Canon.canon ())))
  else (
    (* e < 0 <= f: the mirror image *)
    if e + f >= 0 then (
      powi_add n (e + f) (0 - e); powi_add d (e + f) (0 - e);
      assert (powi n (e + f) * (powi n (0 - e) * (powi d (e + f) * powi d (0 - e)))
              = (powi d (0 - e) * (powi n (e + f) * powi n (0 - e))) * powi d (e + f)) by (FStar.Tactics.Canon.canon ()))
    else (
      powi_add n f (0 - (e + f)); powi_add d f (0 - (e + f));
      assert (powi d (0 - (e + f)) * ((powi n f * powi n (0 - (e + f))) * powi d f)
              = (powi d f * powi d (0 - (e + f)) * powi n f) * powi n (0 - (e + f))) by (FStar.Tactics.Canon.canon ())))

let rpow_zero (r: rat) : Lemma (rpow r 0 == one) = ()

(* `r^(-e)` is the inverse of `r^e`, on the nose. *)
let rpow_neg (r: rat) (e: int) : Lemma (rpow r (e * (-1)) == rinv (rpow r e)) = ()

(* ---- the scale is multiplicative over the merge ---- *)

let rec scale_insert (a: atom) (e: int) (m: factors)
  : Lemma (req (scale_rat (insert atom_lt a e m)) (rmul (factor_rat a e) (scale_rat m))) =
  match m with
  | [] -> ()
  | (b, f) :: t ->
      if atom_lt a b then ()
      else if a = b then (
        rpow_add (atom_base a) e f;
        rmul_congr (rpow (atom_base a) (e + f)) (rmul (factor_rat a e) (factor_rat a f)) (scale_rat t) (scale_rat t);
        rmul_assoc (factor_rat a e) (factor_rat a f) (scale_rat t))
      else (
        scale_insert a e t;
        rmul_congr (factor_rat b f) (factor_rat b f) (scale_rat (insert atom_lt a e t)) (rmul (factor_rat a e) (scale_rat t));
        rmul_assoc (factor_rat b f) (factor_rat a e) (scale_rat t);
        rmul_comm (factor_rat b f) (factor_rat a e);
        rmul_assoc (factor_rat a e) (factor_rat b f) (scale_rat t))

let rec scale_add_all (m: factors) (s: int) (b: factors)
  : Lemma (ensures req (scale_rat (add_all atom_lt m s b)) (rmul (scale_rat m) (scale_rat (scale_exps b s)))) (decreases b) =
  match b with
  | [] -> rmul_one (scale_rat m)
  | (a, e) :: t ->
      scale_add_all (insert atom_lt a (s * e) m) s t;
      scale_insert a (s * e) m;
      swap_mul s e;
      rmul_congr (scale_rat (insert atom_lt a (s * e) m)) (rmul (factor_rat a (e * s)) (scale_rat m))
                 (scale_rat (scale_exps t s)) (scale_rat (scale_exps t s));
      req_trans (scale_rat (add_all atom_lt m s b))
                (rmul (scale_rat (insert atom_lt a (s * e) m)) (scale_rat (scale_exps t s)))
                (rmul (rmul (factor_rat a (e * s)) (scale_rat m)) (scale_rat (scale_exps t s)));
      rmul_assoc (factor_rat a (e * s)) (scale_rat m) (scale_rat (scale_exps t s));
      rmul_comm (factor_rat a (e * s)) (scale_rat m);
      rmul_assoc (scale_rat m) (factor_rat a (e * s)) (scale_rat (scale_exps t s))

let rec scale_drop_zero (m: factors) : Lemma (req (scale_rat (drop_zero m)) (scale_rat m)) =
  match m with
  | [] -> ()
  | (a, e) :: t ->
      scale_drop_zero t;
      if e = 0 then (rpow_zero (atom_base a); rmul_one (scale_rat t))
      else rmul_congr (factor_rat a e) (factor_rat a e) (scale_rat (drop_zero t)) (scale_rat t)

let rec scale_exps_one (m: factors) : Lemma (scale_exps m 1 == m) =
  match m with
  | [] -> ()
  | _ :: t -> scale_exps_one t

let rec scale_neg (b: factors) : Lemma (scale_rat (scale_exps b (-1)) == rinv (scale_rat b)) =
  match b with
  | [] -> ()
  | (a, e) :: t -> scale_neg t; rpow_neg (atom_base a) e; rinv_rmul (factor_rat a e) (scale_rat t)

(* THEOREM 4, first half: the scale of a product is the product of the scales, and of a quotient
   the quotient. *)
let scale_combine (a b: factors) : Lemma (req (scale_rat (combine atom_lt a 1 b)) (rmul (scale_rat a) (scale_rat b))) =
  scale_drop_zero (add_all atom_lt a 1 b); scale_add_all a 1 b; scale_exps_one b;
  req_trans (scale_rat (combine atom_lt a 1 b)) (scale_rat (add_all atom_lt a 1 b)) (rmul (scale_rat a) (scale_rat b))

let scale_combine_div (a b: factors)
  : Lemma (req (scale_rat (combine atom_lt a (-1) b)) (rmul (scale_rat a) (rinv (scale_rat b)))) =
  scale_drop_zero (add_all atom_lt a (-1) b); scale_add_all a (-1) b; scale_neg b;
  req_trans (scale_rat (combine atom_lt a (-1) b)) (scale_rat (add_all atom_lt a (-1) b)) (rmul (scale_rat a) (rinv (scale_rat b)))

(* THEOREM 4, second half: lowest terms change no value. *)
let reduce_exact (r: rat) : Lemma (req (reduce r) r) =
  let g = gcd r.rn r.rd in
  gcd_divides r.rn r.rd;
  lemma_div_exact r.rn g; lemma_div_exact r.rd g;
  (* (rn/g) * rd = rn * (rd/g): both are (rn/g) * (rd/g) * g *)
  assert ((r.rn / g) * g = r.rn);
  assert ((r.rd / g) * g = r.rd);
  assert ((r.rn / g) * ((r.rd / g) * g) = ((r.rn / g) * g) * (r.rd / g)) by (FStar.Tactics.Canon.canon ())

(* The factor from `source` to `target`, as `conversionFactor` computes it when it answers. *)
let factor (source target: uom) : Tot rat = scale (of_factors (combine atom_lt source.factors (-1) target.factors))

let factor_value (s t: uom) : Lemma (req (factor s t) (rmul (scale_rat s.factors) (rinv (scale_rat t.factors)))) =
  reduce_exact (scale_rat (combine atom_lt s.factors (-1) t.factors));
  scale_combine_div s.factors t.factors;
  req_trans (factor s t) (scale_rat (combine atom_lt s.factors (-1) t.factors)) (rmul (scale_rat s.factors) (rinv (scale_rat t.factors)))

(* THEOREM 4: FACTORS COMPOSE. Through any `b`, the factor from `a` to `c` is the product of the two
   legs — as a value, which is what a consumer multiplies by. Compatibility is not needed for the
   value identity (the scales are defined on every unit); `conversionFactor` answers it exactly when
   the units are pairwise compatible. *)
let factor_compose (a b c: uom) : Lemma (req (factor a c) (rmul (factor a b) (factor b c))) =
  let sa = scale_rat a.factors in let sb = scale_rat b.factors in let sc = scale_rat c.factors in
  factor_value a c; factor_value a b; factor_value b c;
  rmul_congr (factor a b) (rmul sa (rinv sb)) (factor b c) (rmul sb (rinv sc));
  (* (sa * sb^-1) * (sb * sc^-1) == sa * sc^-1 *)
  rmul_assoc sa (rinv sb) (rmul sb (rinv sc));
  rmul_assoc (rinv sb) sb (rinv sc);
  rinv_cancel sb;
  rmul_congr (rmul (rinv sb) sb) one (rinv sc) (rinv sc);
  rmul_one (rinv sc);
  rmul_congr sa sa (rmul (rmul (rinv sb) sb) (rinv sc)) (rinv sc);
  req_trans (rmul (factor a b) (factor b c)) (rmul (rmul sa (rinv sb)) (rmul sb (rinv sc))) (rmul sa (rinv sc));
  req_sym (factor a c) (rmul sa (rinv sc));
  req_sym (rmul (factor a b) (factor b c)) (rmul sa (rinv sc));
  req_trans (factor a c) (rmul sa (rinv sc)) (rmul (factor a b) (factor b c))

(* A unit's factor to itself is one, and the two directions multiply to one. *)
let factor_self (a: uom) : Lemma (req (factor a a) one) =
  factor_value a a; rinv_cancel (scale_rat a.factors); rmul_comm (rinv (scale_rat a.factors)) (scale_rat a.factors);
  req_trans (factor a a) (rmul (scale_rat a.factors) (rinv (scale_rat a.factors))) one

let factor_inverse (a b: uom) : Lemma (req (rmul (factor a b) (factor b a)) one) =
  factor_compose a b a; factor_self a; req_sym (factor a a) (rmul (factor a b) (factor b a));
  req_trans (rmul (factor a b) (factor b a)) (factor a a) one

(* ======================================================================================
   7. RENDER (F#: `UnitOfMeasure.ToString`, which `Unit.render` forwards to).
   ====================================================================================== *)

let iabs (e: int) : Tot nat = if e < 0 then 0 - e else e

let digit_of (n: nat{n < 10}) : Tot uch =
  match n with
  | 0 -> D0 | 1 -> D1 | 2 -> D2 | 3 -> D3 | 4 -> D4
  | 5 -> D5 | 6 -> D6 | 7 -> D7 | 8 -> D8 | _ -> D9

(* F#: `string e` for a non-negative `int` — decimal digits, no sign, no leading zero. *)
let rec digits (n: nat) : Tot text (decreases n) =
  if n < 10 then [digit_of n] else app (digits (n / 10)) [digit_of (n % 10)]

(* F#: `atomText` — the prefix, the symbol, and the exponent's magnitude unless it is 1. *)
let atom_text (a: atom) (e: int) : Tot text =
  app a.prefix (app a.symbol (if iabs e = 1 then [] else digits (iabs e)))

let rec positives (m: factors) : Tot factors =
  match m with
  | [] -> []
  | (a, e) :: t -> if e > 0 then (a, e) :: positives t else positives t

let rec negatives (m: factors) : Tot factors =
  match m with
  | [] -> []
  | (a, e) :: t -> if e < 0 then (a, e) :: negatives t else negatives t

let rec atom_texts (m: factors) : Tot (list text) =
  match m with
  | [] -> []
  | (a, e) :: t -> atom_text a e :: atom_texts t

(* F#: `String.Join(".", num)`. *)
let rec join_dot (ts: list text) : Tot text =
  match ts with
  | [] -> []
  | t :: rest -> (match rest with [] -> t | _ -> app t (Dot :: join_dot rest))

(* F#: `String.Join("", den |> List.map (fun t -> "/" + t))`. *)
let rec slashes (ts: list text) : Tot text =
  match ts with
  | [] -> []
  | t :: rest -> Slash :: app t (slashes rest)

(* F#: `UnitOfMeasure.ToString` — `1` for the dimensionless unit, else the positive factors joined by
   `.` then each negative factor after a `/`. *)
let render (u: uom) : Tot text =
  let num = positives u.factors in
  let den = negatives u.factors in
  match num, den with
  | [], [] -> [D1]
  | _ -> app (join_dot (atom_texts num)) (slashes (atom_texts den))

(* ======================================================================================
   8. PARSE (F#: `Unit.parse`, `resolve`, the `Frame` stack, `UnitRefusal`).
   ====================================================================================== *)

(* F#: the `expected` prose of a `Malformed` refusal, one case per spelling the source has. *)
type expect =
  | ExpUnit                          (* "a unit" *)
  | ExpCloseAnnotation               (* "'}' closing the annotation" *)
  | ExpCloseSymbol                   (* "']' closing the symbol" *)
  | ExpExponentDigits                (* "the digits of an exponent" *)
  | ExpExponentRange                 (* "an exponent within ±2147483647" *)
  | ExpCloseParen : opened:nat -> expect  (* "')' closing the '(' at %d" *)
  | ExpSepOrEnd                      (* "'.', '/' or the end" *)
  | ExpSepOrClose                    (* "'.', '/' or ')'" *)

(* F#: `UnitRefusal`, case for case; every token as written and its zero-based position. *)
type refusal =
  | Empty
  | Malformed        : found:text -> position:nat -> expected:expect -> refusal
  | UnknownAtom      : token:text -> position:nat -> refusal
  | Annotation       : token:text -> position:nat -> refusal
  | ArbitraryUnit    : token:text -> position:nat -> refusal
  | NonRatioUnit     : token:text -> position:nat -> refusal
  | PrefixNotAllowed : token:text -> position:nat -> refusal
  | NumericFactor    : token:text -> position:nat -> refusal

let is_letter (c: uch) : Tot bool = (97 <= code c && code c <= 122) || (65 <= code c && code c <= 90)
let is_digit (c: uch) : Tot bool = 48 <= code c && code c <= 57
let digit_val (c: uch) : Tot nat = if is_digit c then code c - 48 else 0

let rec take (n: nat) (s: text) : Tot text =
  match s with
  | [] -> []
  | c :: t -> if n = 0 then [] else c :: take (n - 1) t

let rec drop (n: nat) (s: text) : Tot text =
  match s with
  | [] -> []
  | c :: t -> if n = 0 then s else drop (n - 1) t

(* F#: `text.IndexOf(c, pos)`, relative to the remaining text. *)
let rec index_of (c: uch) (s: text) : Tot (option nat) =
  match s with
  | [] -> None
  | x :: t -> if x = c then Some 0 else (match index_of c t with Some i -> Some (i + 1) | None -> None)

(* F#: `startsWith` — a PROPER prefix: `s.Length > prefix.Length && s.Substring(0, prefix.Length) = prefix`. *)
let rec starts_with (p s: text) : Tot bool =
  match p, s with
  | [], [] -> false
  | [], _ -> true
  | _, [] -> false
  | x :: p', y :: s' -> x = y && starts_with p' s'

(* F#: `resolve`'s `viaPrefix` — `List.tryPick` over the prefixes in table order. *)
let rec via_prefix (tok: text) (pos: nat) (ps: list (text & int)) : Tot (option (result atom refusal)) =
  match ps with
  | [] -> None
  | (p, _) :: ps' ->
      if starts_with p tok then
        let r = drop (len p) tok in
        (match try_atom r with
         | Some sym ->
             (match find_atom sym with
              | Some d -> if d.metric then Some (Ok { symbol = sym; prefix = p }) else Some (Refused (PrefixNotAllowed tok pos))
              | None -> Some (Refused (PrefixNotAllowed tok pos)))
         | None ->
             if mem r non_ratio then Some (Refused (NonRatioUnit tok pos))
             else if mem r arbitrary then Some (Refused (ArbitraryUnit tok pos))
             else if is_currency r then Some (Ok { symbol = r; prefix = p })
             else via_prefix tok pos ps')
      else via_prefix tok pos ps'

(* F#: `resolve` — one symbol token to an atom, or to the refusal naming its class. *)
let resolve (tok: text) (pos: nat) : Tot (result atom refusal) =
  match try_atom tok with
  | Some sym -> Ok { symbol = sym; prefix = [] }
  | None ->
      if mem tok non_ratio then Refused (NonRatioUnit tok pos)
      else if mem tok arbitrary then Refused (ArbitraryUnit tok pos)
      else if is_currency tok then Ok { symbol = tok; prefix = [] }
      else (match via_prefix tok pos prefixes with Some r -> r | None -> Refused (UnknownAtom tok pos))

(* F#: `Frame` — one open term: its product so far, the sign the next component takes, the sign the
   whole term takes in the term that encloses it, and where its `(` was. *)
type frame = { acc: factors; next: int; outer: int; opened: nat }

let initial_frame : frame = { acc = []; next = 1; outer = 1; opened = 0 }

let set_next (stack: list frame) (n: int) : Tot (list frame) =
  match stack with
  | top :: rest -> { top with next = n } :: rest
  | [] -> []

let top_next (stack: list frame) : Tot int =
  match stack with
  | top :: _ -> top.next
  | [] -> 1

(* ---- the lexer's three readers, each over the REMAINING text with the position it is at ---- *)

(* The text a reader consumed and what is left, with the position after it. *)
type lexed = { tok: text; rest: text; at: nat }

(* F#: `text.IndexOf(']', pos)` from a `[` — the bracketed run through its `]`, or nothing. *)
let rec skip_to_rbr (s: text) : Tot (option (r: lexed{len r.rest < len s})) =
  match s with
  | [] -> None
  | c :: t ->
      if c = RBr then Some ({ tok = [RBr]; rest = t; at = 1 })
      else (match skip_to_rbr t with
            | Some r -> Some ({ tok = c :: r.tok; rest = r.rest; at = r.at + 1 })
            | None -> None)

(* F#: the symbol loop — letters, `_`, `'`, `%`, and a `[...]` run skipped to its `]` (refused as
   `Malformed` naming the rest of the text where no `]` closes it). `acc` is what is read so far. *)
let rec read_sym (s: text) (pos: nat) (acc: text)
  : Tot (r: result lexed refusal{Ok? r ==> len (Ok?.v r).rest <= len s /\ (Ok?.v r).at >= pos}) (decreases (len s)) =
  match s with
  | [] -> Ok ({ tok = acc; rest = []; at = pos })
  | c :: t ->
      if is_letter c || c = Under || c = Apos || c = Pct then read_sym t (pos + 1) (app acc [c])
      else if c = LBr then
        (match skip_to_rbr t with
         | None -> Refused (Malformed s pos ExpCloseSymbol)
         | Some r -> read_sym r.rest (pos + 1 + r.at) (app acc (LBr :: r.tok)))
      else Ok ({ tok = acc; rest = s; at = pos })

(* F#: the digit runs of the numeric-factor arm. *)
let rec span_digits (s: text) : Tot (r: lexed{len r.rest <= len s}) =
  match s with
  | [] -> { tok = []; rest = []; at = 0 }
  | c :: t ->
      if is_digit c then (let r = span_digits t in { tok = c :: r.tok; rest = r.rest; at = r.at + 1 })
      else { tok = []; rest = s; at = 0 }

(* F#: the numeric-factor arm's token — digits, then optionally `*` or `^`, a sign and digits. *)
let read_number (s: text) : Tot (r: lexed{len r.rest <= len s}) =
  let d1 = span_digits s in
  match d1.rest with
  | c :: t ->
      if c = Star || c = Caret then
        let (sg, t2) = (match t with
                        | c2 :: t2 -> if c2 = Plus || c2 = Minus then ([c2], t2) else ([], t)
                        | [] -> ([], t)) in
        let d2 = span_digits t2 in
        { tok = app d1.tok (app [c] (app sg d2.tok)); rest = d2.rest; at = 0 }
      else d1
  | [] -> d1

(* The exponent an atom carries. *)
type exponent = { e: int; erest: text; eat: nat }

(* F#: the exponent's digit loop — the value accumulated until it passes the bound, after which
   `over` is set and the digits are still consumed. *)
let rec read_digits (s: text) (pos: nat) (value: nat) (over: bool)
  : Tot (r: (nat & bool & lexed){len (Mktuple3?._3 r).rest <= len s /\ (Mktuple3?._3 r).at >= pos}) (decreases s) =
  match s with
  | [] -> (value, over, { tok = []; rest = []; at = pos })
  | c :: t ->
      if is_digit c then
        (if over then read_digits t (pos + 1) value over
         else (let v = value * 10 + digit_val c in read_digits t (pos + 1) v (v > max_exp)))
      else (value, over, { tok = []; rest = s; at = pos })

(* F#: the exponent read after a symbol token — an optional sign, then digits. A sign with no digits
   is refused; a value past the bound is refused naming the exponent text; no exponent is 1. *)
let read_exp (s: text) (pos: nat)
  : Tot (r: result exponent refusal{Ok? r ==> len (Ok?.v r).erest <= len s /\ (Ok?.v r).eat >= pos}) =
  let exp_start = pos in
  let (sign, s1, pos1) = (match s with
                          | c :: t -> if c = Minus then (-1, t, pos + 1) else if c = Plus then (1, t, pos + 1) else (1, s, pos)
                          | [] -> (1, s, pos)) in
  let digits_start = pos1 in
  let (value, over, l) = read_digits s1 pos1 0 false in
  if l.at = digits_start && l.at > exp_start then
    Refused (Malformed (match l.rest with c :: _ -> [c] | [] -> []) l.at ExpExponentDigits)
  else if over then
    Refused (Malformed (take (l.at - exp_start) s) exp_start ExpExponentRange)
  else
    let e = if l.at = exp_start then 1 else sign * value in
    Ok ({ e = e; erest = l.rest; eat = l.at })

(* ---- the loop (F#: `while result.IsNone`), as three functions: the component arm
   (`expectComponent` true), the separator arm (`expectComponent` false), and what the component
   arm does once it has a symbol token. `s` is the text from `pos` on. ---- *)

let rec comp (s: text) (pos: nat) (stack: list frame) (term_start: bool)
  : Tot (result uom refusal) (decreases %[len s; 2]) =
  match s with
  | [] -> Refused (Malformed [] pos ExpUnit)
  | c :: t ->
      if term_start && c = Slash then comp t (pos + 1) (set_next stack (-1)) false
      else if c = LPar then
        comp t (pos + 1) ({ acc = []; next = 1; outer = top_next stack; opened = pos } :: stack) true
      else if is_digit c then
        (let r = read_number s in
         if r.tok = [D1] then sep r.rest (pos + r.at) stack
         else Refused (NumericFactor r.tok pos))
      else if c = LCur then
        (match index_of RCur s with
         | None -> Refused (Malformed s pos ExpCloseAnnotation)
         | Some i -> Refused (Annotation (take (i + 1) s) pos))
      else if is_letter c || c = Pct then
        (match read_sym t (pos + 1) [c] with
         | Refused r -> Refused r
         | Ok l -> after_token s pos stack l)
      else if c = LBr then
        (match skip_to_rbr t with
         | None -> Refused (Malformed s pos ExpCloseSymbol)
         | Some b ->
             (match read_sym b.rest (pos + 1 + b.at) (LBr :: b.tok) with
              | Refused r -> Refused r
              | Ok l -> after_token s pos stack l))
      else Refused (Malformed [c] pos ExpUnit)

(* F#: the tail of the symbol arm — `resolve`, the exponent, the fold into the innermost open
   term, then `expectComponent <- false`. `s` is the text at the token's start, `pos` its position. *)
and after_token (s: text) (pos: nat) (stack: list frame) (l: lexed{len l.rest < len s /\ l.at >= pos})
  : Tot (result uom refusal) (decreases %[len l.rest; 1]) =
  match resolve l.tok pos with
  | Refused r -> Refused r
  | Ok a ->
      (match read_exp l.rest l.at with
       | Refused r -> Refused r
       | Ok x ->
           let component = (if x.e = 0 then [] else [(a, x.e)]) in
           (match stack with
            | top :: others ->
                (match try_combine top.acc top.next component with
                 | Some acc -> sep x.erest x.eat ({ top with acc = acc } :: others)
                 | None -> Refused (Malformed (take (x.eat - pos) s) pos ExpExponentRange))
            | [] -> sep x.erest x.eat []))

and sep (s: text) (pos: nat) (stack: list frame) : Tot (result uom refusal) (decreases %[len s; 0]) =
  match s with
  | [] ->
      (match stack with
       | [top] -> Ok (of_factors top.acc)
       | top :: _ -> Refused (Malformed [] pos (ExpCloseParen top.opened))
       | [] -> Refused (Malformed [] pos ExpUnit))
  | c :: t ->
      if c = Dot then comp t (pos + 1) (set_next stack 1) false
      else if c = Slash then comp t (pos + 1) (set_next stack (-1)) false
      else if c = RPar then
        (match stack with
         | inner :: outer :: rest ->
             (match try_combine outer.acc inner.outer inner.acc with
              | Some acc -> sep t (pos + 1) ({ outer with acc = acc } :: rest)
              | None -> Refused (Malformed [RPar] pos ExpExponentRange))
         | _ -> Refused (Malformed [RPar] pos ExpSepOrEnd))
      else if c = LCur then
        (match index_of RCur s with
         | None -> Refused (Malformed s pos ExpCloseAnnotation)
         | Some i -> Refused (Annotation (take (i + 1) s) pos))
      else Refused (Malformed [c] pos (if len stack > 1 then ExpSepOrClose else ExpSepOrEnd))

(* F#: `Unit.parse` — the empty text is `Empty` (a null one too, at the host's bridge); otherwise the
   loop from the first character with one open term. *)
let parse (s: text) : Tot (result uom refusal) =
  if s = [] then Refused Empty else comp s 0 [initial_frame] true

(* ======================================================================================
   9. THEOREM 2 — ONE TEXT: `parse (render u) == Ok u` for every canonical unit.

   A canonical unit is one production builds: well-formed and in range (`wf_unit`), every atom
   ADMITTED — its token (prefix then symbol) is one the symbol loop reads whole, starts as a
   symbol starts, and `resolve`s to the atom itself. The lemmas follow the text left to right:
   the symbol loop reads exactly the token (`read_sym_app`), the exponent loop reads exactly the
   digits `render` wrote (`read_exp_digits`), one component is one fold into the open term
   (`comp_token`), the separator arm threads the rest (`loop`), and the folded product is the
   unit's own factor list because every atom is written once with its exponent (`ext`).
   ====================================================================================== *)

let sym_char (c: uch) : Tot bool = is_letter c || c = Under || c = Apos || c = Pct

(* A token the symbol loop reads whole: symbol characters, and every `[` closed by the next `]`. *)
let rec sym_token (t: text) : Tot bool (decreases (len t)) =
  match t with
  | [] -> true
  | c :: r ->
      if c = LBr then (match skip_to_rbr r with Some b -> sym_token b.rest | None -> false)
      else sym_char c && sym_token r

(* The first character of a symbol token, as the component arm dispatches on it. *)
let token_start (t: text) : Tot bool =
  match t with
  | c :: _ -> is_letter c || c = Pct || c = LBr
  | [] -> false

(* What follows a component in a rendered text: nothing, or a separator. *)
let sep_start (s: text) : Tot bool =
  match s with
  | [] -> true
  | c :: _ -> c = Dot || c = Slash

(* What stops the symbol loop: nothing, or a character that is neither a symbol character nor `[`. *)
let stops_token (s: text) : Tot bool =
  match s with
  | [] -> true
  | c :: _ -> not (sym_char c) && c <> LBr

(* No character of the text is one the alphabet does not spell: what lets a text cross to another
   model's alphabet and back (`WireColumn.fst`'s field codec carries a unit's text in its own). *)
let rec no_other (t: text) : Tot bool =
  match t with
  | [] -> true
  | c :: r -> c <> Other && no_other r

let tok_of (a: atom) : Tot text = app a.prefix a.symbol

(* ADMITTED: the atom's own token resolves to it. Every atom in the vocabulary under every prefix
   it takes satisfies this (the differential exhibits it over the table); the theorem is stated
   over the predicate so that it rests on `resolve` as modelled, not on a second copy of the table. *)
let admitted (a: atom) : Tot bool =
  sym_token (tok_of a) && token_start (tok_of a) && no_other (tok_of a) && resolve (tok_of a) 0 = Ok a

let rec admitted_all (m: factors) : Tot bool =
  match m with
  | [] -> true
  | (a, _) :: t -> admitted a && admitted_all t

(* THE CANONICAL UNIT: what production holds, and what `render` writes one text for. *)
let canonical (u: uom) : Tot bool = wf_unit u && admitted_all u.factors

(* ---- the bracket skipper reconstructs what it skipped ---- *)

let rec skip_props (r: text)
  : Lemma (requires Some? (skip_to_rbr r))
          (ensures (let b = Some?.v (skip_to_rbr r) in app b.tok b.rest == r /\ b.at == len b.tok)) =
  match r with
  | [] -> ()
  | c :: t -> if c = RBr then () else skip_props t

let rec skip_app (r rest: text)
  : Lemma (requires Some? (skip_to_rbr r))
          (ensures (let b = Some?.v (skip_to_rbr r) in
                    Some? (skip_to_rbr (app r rest)) /\
                    (let b' = Some?.v (skip_to_rbr (app r rest)) in
                     b'.tok == b.tok /\ b'.rest == app b.rest rest /\ b'.at == b.at))) =
  match r with
  | [] -> ()
  | c :: t -> if c = RBr then () else skip_app t rest

(* ---- the symbol loop reads exactly the token ---- *)

let rec read_sym_app (t rest: text) (pos: nat) (acc: text)
  : Lemma (requires sym_token t /\ stops_token rest)
          (ensures read_sym (app t rest) pos acc == Ok ({ tok = app acc t; rest = rest; at = pos + len t }))
          (decreases (len t)) =
  match t with
  | [] -> app_nil acc
  | c :: r ->
      if c = LBr then (
        let b = Some?.v (skip_to_rbr r) in
        skip_props r; skip_app r rest;
        len_app b.tok b.rest;
        read_sym_app b.rest rest (pos + 1 + b.at) (app acc (LBr :: b.tok));
        app_assoc acc (LBr :: b.tok) b.rest)
      else (
        read_sym_app r rest (pos + 1) (app acc [c]);
        app_assoc acc [c] r)

(* ---- the exponent loop reads exactly the digits ---- *)

let rec pow10 (k: nat) : Tot pos = if k = 0 then 1 else pow10 (k - 1) * 10

let digit_of_digit (n: nat{n < 10}) : Lemma (is_digit (digit_of n) /\ digit_val (digit_of n) == n) = ()

let rec digits_shape (m: nat) : Lemma (ensures (match digits m with c :: _ -> is_digit c | [] -> False)) (decreases m) =
  if m < 10 then digit_of_digit m else digits_shape (m / 10)

let rec digits_len (m: nat) : Lemma (ensures len (digits m) >= 1) (decreases m) =
  if m < 10 then () else (digits_len (m / 10); len_app (digits (m / 10)) [digit_of (m % 10)])

(* Reading the digits of `m` onto an accumulated `v` reads `v * 10^k + m`, never passing the bound
   on the way when the result is under it. *)
let rec read_digits_app (m: nat) (rest: text) (pos: nat) (v: nat)
  : Lemma (requires v * pow10 (len (digits m)) + m <= max_exp)
          (ensures read_digits (app (digits m) rest) pos v false
                   == read_digits rest (pos + len (digits m)) (v * pow10 (len (digits m)) + m) false)
          (decreases m) =
  if m < 10 then digit_of_digit m
  else (
    let q = m / 10 in
    let d = m % 10 in
    let k' = len (digits q) in
    len_app (digits q) [digit_of d];
    euclidean_division_definition m 10;
    assert (len (digits m) == k' + 1);
    assert (pow10 (k' + 1) == pow10 k' * 10);
    paren_mul_right v (pow10 k') 10;
    (* v * 10^(k'+1) + m = (v * 10^k' + q) * 10 + d *)
    assert (v * pow10 (k' + 1) + m == (v * pow10 k' + q) * 10 + d);
    app_assoc (digits q) [digit_of d] rest;
    read_digits_app q (digit_of d :: rest) pos v;
    digit_of_digit d)

let read_digits_stop (rest: text) (pos: nat) (v: nat) (over: bool)
  : Lemma (requires sep_start rest) (ensures read_digits rest pos v over == (v, over, { tok = []; rest = rest; at = pos })) = ()

let read_exp_digits (m: nat) (rest: text) (pos: nat)
  : Lemma (requires 2 <= m /\ m <= max_exp /\ sep_start rest)
          (ensures read_exp (app (digits m) rest) pos == Ok ({ e = m; erest = rest; eat = pos + len (digits m) })) =
  digits_shape m; digits_len m;
  read_digits_app m rest pos 0;
  read_digits_stop rest (pos + len (digits m)) m false

let read_exp_none (rest: text) (pos: nat)
  : Lemma (requires sep_start rest) (ensures read_exp rest pos == Ok ({ e = 1; erest = rest; eat = pos })) =
  read_digits_stop rest pos 0 false

(* ---- `resolve` answers the same atom at any position ---- *)

let rec via_prefix_pos (tok: text) (p q: nat) (ps: list (text & int))
  : Lemma (ensures (match via_prefix tok p ps with
                    | Some (Ok a) -> via_prefix tok q ps == Some (Ok a)
                    | _ -> True)) =
  match ps with
  | [] -> ()
  | _ :: ps' -> via_prefix_pos tok p q ps'

let resolve_pos (tok: text) (p q: nat)
  : Lemma (requires Ok? (resolve tok p)) (ensures resolve tok q == resolve tok p) =
  via_prefix_pos tok p q prefixes

(* ---- one component is one fold into the open term ---- *)

let rec len_pos_app (a b: text) : Lemma (len (app a b) == len a + len b) = len_app a b

let atom_text_split (a: atom) (e: int) (rest: text)
  : Lemma (app (atom_text a e) rest == app (tok_of a) (app (if iabs e = 1 then [] else digits (iabs e)) rest)
           /\ len (atom_text a e) == len (tok_of a) + len (if iabs e = 1 then [] else digits (iabs e))) =
  let ex = (if iabs e = 1 then [] else digits (iabs e)) in
  app_assoc a.prefix (app a.symbol ex) rest;
  app_assoc a.symbol ex rest;
  app_assoc a.prefix a.symbol (app ex rest);
  len_app a.prefix (app a.symbol ex); len_app a.symbol ex; len_app a.prefix a.symbol

#push-options "--z3rlimit 80"
let comp_token (a: atom) (e: int) (rest: text) (pos: nat) (top: frame) (others: list frame) (ts: bool)
  : Lemma (requires admitted a /\ e <> 0 /\ in_range e /\ sep_start rest /\ Some? (try_combine top.acc top.next [(a, iabs e)]))
          (ensures comp (app (atom_text a e) rest) pos (top :: others) ts
                   == sep rest (pos + len (atom_text a e))
                          ({ top with acc = Some?.v (try_combine top.acc top.next [(a, iabs e)]) } :: others)) =
  let tok = tok_of a in
  let ex = (if iabs e = 1 then [] else digits (iabs e)) in
  let tail = app ex rest in
  atom_text_split a e rest;
  len_app tok ex; len_app a.prefix a.symbol;
  (* the text is `tok @ tail`, and `tail` stops the symbol loop: a digit, a separator, or nothing *)
  if iabs e = 1 then () else digits_shape (iabs e);
  assert (stops_token tail);
  assert (app (atom_text a e) rest == app tok tail);
  (* the symbol loop reads exactly `tok` *)
  let l : lexed = { tok = tok; rest = tail; at = pos + len tok } in
  (match tok with
   | c :: r ->
       if c = LBr then (
         let b = Some?.v (skip_to_rbr r) in
         skip_props r; skip_app r tail; len_app b.tok b.rest;
         read_sym_app b.rest tail (pos + 1 + b.at) (LBr :: b.tok);
         assert (read_sym (app b.rest tail) (pos + 1 + b.at) (LBr :: b.tok) == Ok l))
       else (
         read_sym_app r tail (pos + 1) [c];
         assert (read_sym (app r tail) (pos + 1) [c] == Ok l))
   | [] -> ());
  assert (comp (app tok tail) pos (top :: others) ts == after_token (app tok tail) pos (top :: others) l);
  (* the token resolves to `a`, at this position as at any *)
  resolve_pos tok 0 pos;
  (* the exponent is the magnitude `render` wrote, or 1 when it wrote none *)
  (if iabs e = 1 then read_exp_none rest l.at else read_exp_digits (iabs e) rest l.at);
  let x : exponent = { e = iabs e; erest = rest; eat = pos + len (atom_text a e) } in
  assert (read_exp tail l.at == Ok x);
  assert (after_token (app tok tail) pos (top :: others) l
          == sep rest x.eat ({ top with acc = Some?.v (try_combine top.acc top.next [(a, iabs e)]) } :: others))
#pop-options

(* The fold of a component with its separator's sign and its magnitude is the fold of the signed
   exponent: `combine acc (-1) [(a, |e|)]` is `combine acc 1 [(a, e)]` for a negative `e`. *)
let combine_sign_abs (acc: factors) (a: atom) (e: int)
  : Lemma (requires wf atom_lt acc /\ e <> 0)
          (ensures combine atom_lt acc (if e > 0 then 1 else -1) [(a, iabs e)] == combine atom_lt acc 1 [(a, e)]) =
  atom_lt_order ();
  let s = (if e > 0 then 1 else -1) in
  combine_wf atom_lt acc s [(a, iabs e)]; combine_wf atom_lt acc 1 [(a, e)];
  let aux (y: atom) : Lemma (exp_of (combine atom_lt acc s [(a, iabs e)]) y == exp_of (combine atom_lt acc 1 [(a, e)]) y) =
    combine_exp atom_lt acc s [(a, iabs e)] y; combine_exp atom_lt acc 1 [(a, e)] y in
  FStar.Classical.forall_intro aux;
  ext atom_lt (combine atom_lt acc s [(a, iabs e)]) (combine atom_lt acc 1 [(a, e)])

(* ---- the rendered text, component by component ---- *)

(* The text after the first component: each factor behind its separator, `.` for a positive
   exponent and `/` for a negative one. *)
let rec render_tail (items: factors) : Tot text =
  match items with
  | [] -> []
  | (a, e) :: t -> (if e > 0 then Dot else Slash) :: app (atom_text a e) (render_tail t)

let rec all_pos (m: factors) : Tot bool = match m with [] -> true | (_, e) :: t -> e > 0 && all_pos t
let rec all_neg (m: factors) : Tot bool = match m with [] -> true | (_, e) :: t -> e < 0 && all_neg t

let rec render_tail_neg (den: factors) : Lemma (requires all_neg den) (ensures render_tail den == slashes (atom_texts den)) =
  match den with
  | [] -> ()
  | _ :: t -> render_tail_neg t

let rec join_split (num den: factors)
  : Lemma (requires all_pos num /\ all_neg den)
          (ensures app (join_dot (atom_texts num)) (slashes (atom_texts den))
                   == (match num with
                       | [] -> render_tail den
                       | (a, e) :: t -> app (atom_text a e) (render_tail (app t den)))) =
  match num with
  | [] -> render_tail_neg den
  | (a, e) :: t ->
      (match t with
       | [] -> render_tail_neg den
       | (b, f) :: t' ->
           join_split t den;
           app_assoc (atom_text a e) (Dot :: join_dot (atom_texts t)) (slashes (atom_texts den)))

let rec positives_pos (m: factors) : Lemma (all_pos (positives m)) =
  match m with [] -> () | _ :: t -> positives_pos t

let rec negatives_neg (m: factors) : Lemma (all_neg (negatives m)) =
  match m with [] -> () | _ :: t -> negatives_neg t

let render_eq (u: uom)
  : Lemma (ensures (let num = positives u.factors in
                    let den = negatives u.factors in
                    match num with
                    | [] -> (match den with [] -> render u == [D1] | _ -> render u == render_tail den)
                    | (a, e) :: t -> render u == app (atom_text a e) (render_tail (app t den)))) =
  positives_pos u.factors; negatives_neg u.factors;
  join_split (positives u.factors) (negatives u.factors)

(* ---- the separator arm threads the components, folding each into the one open term ---- *)

let rec fold_items (acc: factors) (items: factors) : Tot factors (decreases items) =
  match items with
  | [] -> acc
  | (a, e) :: t -> fold_items (combine atom_lt acc 1 [(a, e)]) t

(* Every fold stays in range: what `try_combine` asks at each step. *)
let rec steps_ok (acc: factors) (items: factors) : Tot bool (decreases items) =
  match items with
  | [] -> true
  | (a, e) :: t -> let acc' = combine atom_lt acc 1 [(a, e)] in all_in_range acc' && steps_ok acc' t

let rec all_nonzero (m: factors) : Tot bool = match m with [] -> true | (_, e) :: t -> e <> 0 && all_nonzero t

#push-options "--z3rlimit 80"
let rec loop (items: factors) (pos: nat) (top: frame)
  : Lemma (requires admitted_all items /\ all_nonzero items /\ all_in_range items /\ wf atom_lt top.acc /\ steps_ok top.acc items)
          (ensures sep (render_tail items) pos [top] == Ok (of_factors (fold_items top.acc items)))
          (decreases items) =
  atom_lt_order ();
  match items with
  | [] -> ()
  | (a, e) :: t ->
      let s = (if e > 0 then 1 else -1) in
      let top' = { top with next = s } in
      let acc' = combine atom_lt top.acc 1 [(a, e)] in
      combine_sign_abs top.acc a e;
      assert (try_combine top'.acc top'.next [(a, iabs e)] == Some acc');
      assert (sep (render_tail items) pos [top] == comp (app (atom_text a e) (render_tail t)) (pos + 1) [top'] false);
      comp_token a e (render_tail t) (pos + 1) top' [] false;
      combine_wf atom_lt top.acc 1 [(a, e)];
      loop t (pos + 1 + len (atom_text a e)) ({ top' with acc = acc' })
#pop-options

(* ---- the folded product is the unit itself ---- *)

(* The exponent of `y` summed over a factor LIST (no sortedness assumed). *)
let rec exp_sum (m: factors) (y: atom) : Tot int =
  match m with
  | [] -> 0
  | (a, e) :: t -> (if a = y then e else 0) + exp_sum t y

let rec exp_sum_above (a: atom) (t: factors) : Lemma (requires strict_order atom_lt /\ above atom_lt a t) (ensures exp_sum t a == 0) =
  match t with
  | [] -> ()
  | _ :: t' -> exp_sum_above a t'

let rec exp_sum_sorted (m: factors) (y: atom) : Lemma (requires strict_order atom_lt /\ sorted atom_lt m) (ensures exp_sum m y == exp_of m y) =
  match m with
  | [] -> ()
  | (a, e) :: t -> sorted_tail atom_lt a e t; exp_sum_sorted t y; if a = y then exp_sum_above a t else ()

let rec exp_sum_app (l m: factors) (y: atom) : Lemma (exp_sum (app l m) y == exp_sum l y + exp_sum m y) =
  match l with
  | [] -> ()
  | _ :: t -> exp_sum_app t m y

let rec exp_sum_split (m: factors) (y: atom)
  : Lemma (requires no_zero m) (ensures exp_sum (positives m) y + exp_sum (negatives m) y == exp_sum m y) =
  match m with
  | [] -> ()
  | _ :: t -> exp_sum_split t y

let rec fold_items_wf (acc items: factors)
  : Lemma (requires strict_order atom_lt /\ wf atom_lt acc) (ensures wf atom_lt (fold_items acc items)) (decreases items) =
  match items with
  | [] -> ()
  | (a, e) :: t -> combine_wf atom_lt acc 1 [(a, e)]; fold_items_wf (combine atom_lt acc 1 [(a, e)]) t

let rec fold_items_exp (acc items: factors) (y: atom)
  : Lemma (requires strict_order atom_lt /\ wf atom_lt acc) (ensures exp_of (fold_items acc items) y == exp_of acc y + exp_sum items y) (decreases items) =
  match items with
  | [] -> ()
  | (a, e) :: t ->
      combine_wf atom_lt acc 1 [(a, e)]; combine_exp atom_lt acc 1 [(a, e)] y;
      fold_items_exp (combine atom_lt acc 1 [(a, e)]) t y

(* ---- every step of the fold stays in range, because every atom is written once ---- *)

let rec mem_key (y: atom) (m: factors) : Tot bool =
  match m with
  | [] -> false
  | (a, _) :: t -> a = y || mem_key y t

let rec distinct_keys (m: factors) : Tot bool =
  match m with
  | [] -> true
  | (a, _) :: t -> not (mem_key a t) && distinct_keys t

let rec all_in_range_exp (m: factors) (y: atom) : Lemma (requires all_in_range m) (ensures in_range (exp_of m y)) =
  match m with
  | [] -> ()
  | _ :: t -> all_in_range_exp t y

let rec all_in_range_of_exp (m: factors)
  : Lemma (requires strict_order atom_lt /\ sorted atom_lt m /\ (forall (y: atom). in_range (exp_of m y))) (ensures all_in_range m) =
  match m with
  | [] -> ()
  | (a, e) :: t ->
      sorted_tail atom_lt a e t;
      assert (exp_of m a == e);
      let aux (y: atom) : Lemma (in_range (exp_of t y)) = if y = a then above_exp atom_lt a t else () in
      FStar.Classical.forall_intro aux;
      all_in_range_of_exp t

let rec above_not_mem (a: atom) (t: factors) : Lemma (requires strict_order atom_lt /\ above atom_lt a t) (ensures not (mem_key a t)) =
  match t with
  | [] -> ()
  | _ :: t' -> above_not_mem a t'

let rec sorted_distinct (m: factors) : Lemma (requires strict_order atom_lt /\ sorted atom_lt m) (ensures distinct_keys m) =
  match m with
  | [] -> ()
  | (a, e) :: t -> sorted_tail atom_lt a e t; above_not_mem a t; sorted_distinct t

let rec mem_positives (y: atom) (m: factors) : Lemma (requires mem_key y (positives m)) (ensures mem_key y m) =
  match m with
  | [] -> ()
  | (a, _) :: t -> if a = y then () else mem_positives y t

let rec mem_negatives (y: atom) (m: factors) : Lemma (requires mem_key y (negatives m)) (ensures mem_key y m) =
  match m with
  | [] -> ()
  | (a, _) :: t -> if a = y then () else mem_negatives y t

let rec distinct_positives (m: factors) : Lemma (requires distinct_keys m) (ensures distinct_keys (positives m)) =
  match m with
  | [] -> ()
  | (a, e) :: t -> distinct_positives t; if e > 0 then FStar.Classical.move_requires (mem_positives a) t else ()

let rec distinct_negatives (m: factors) : Lemma (requires distinct_keys m) (ensures distinct_keys (negatives m)) =
  match m with
  | [] -> ()
  | (a, e) :: t -> distinct_negatives t; if e < 0 then FStar.Classical.move_requires (mem_negatives a) t else ()

(* A key in the positives of a distinct list is not in its negatives. *)
let rec pos_not_neg (y: atom) (m: factors)
  : Lemma (requires distinct_keys m /\ mem_key y (positives m)) (ensures not (mem_key y (negatives m))) =
  match m with
  | [] -> ()
  | (a, e) :: t ->
      if a = y then (if e > 0 then FStar.Classical.move_requires (mem_negatives a) t else mem_positives y t)
      else pos_not_neg y t

let rec mem_key_app (y: atom) (p q: factors) : Lemma (mem_key y (app p q) == (mem_key y p || mem_key y q)) =
  match p with [] -> () | _ :: p' -> mem_key_app y p' q

let rec distinct_app (l m: factors)
  : Lemma (requires distinct_keys l /\ distinct_keys m /\ (forall (y: atom). mem_key y l ==> not (mem_key y m)))
          (ensures distinct_keys (app l m)) =
  match l with
  | [] -> ()
  | (a, _) :: t -> distinct_app t m; mem_key_app a t m

let rec mem_exp_sum (y: atom) (m: factors) : Lemma (requires not (mem_key y m)) (ensures exp_sum m y == 0) =
  match m with
  | [] -> ()
  | _ :: t -> mem_exp_sum y t

let rec steps_ok_lemma (acc items: factors)
  : Lemma (requires strict_order atom_lt /\ wf atom_lt acc /\ all_in_range acc /\
                    distinct_keys items /\ all_nonzero items /\ all_in_range items /\
                    (forall (y: atom). mem_key y items ==> exp_of acc y == 0))
          (ensures steps_ok acc items) (decreases items) =
  match items with
  | [] -> ()
  | (a, e) :: t ->
      let acc' = combine atom_lt acc 1 [(a, e)] in
      combine_wf atom_lt acc 1 [(a, e)];
      let aux (y: atom) : Lemma (exp_of acc' y == exp_of acc y + (if y = a then e else 0)) = combine_exp atom_lt acc 1 [(a, e)] y in
      FStar.Classical.forall_intro aux;
      FStar.Classical.forall_intro (all_in_range_exp acc);
      all_in_range_of_exp acc';
      steps_ok_lemma acc' t

(* ---- the positives and negatives of a well-formed list carry its exponents, once each ---- *)

let rec positives_sub (m: factors) : Lemma (requires all_nonzero m /\ all_in_range m) (ensures all_nonzero (positives m) /\ all_in_range (positives m)) =
  match m with [] -> () | _ :: t -> positives_sub t

let rec negatives_sub (m: factors) : Lemma (requires all_nonzero m /\ all_in_range m) (ensures all_nonzero (negatives m) /\ all_in_range (negatives m)) =
  match m with [] -> () | _ :: t -> negatives_sub t

let rec admitted_positives (m: factors) : Lemma (requires admitted_all m) (ensures admitted_all (positives m)) =
  match m with [] -> () | _ :: t -> admitted_positives t

let rec admitted_negatives (m: factors) : Lemma (requires admitted_all m) (ensures admitted_all (negatives m)) =
  match m with [] -> () | _ :: t -> admitted_negatives t

let rec no_zero_nonzero (m: factors) : Lemma (requires no_zero m) (ensures all_nonzero m) =
  match m with [] -> () | _ :: t -> no_zero_nonzero t

let rec app_props (l m: factors)
  : Lemma (requires admitted_all l /\ admitted_all m /\ all_nonzero l /\ all_nonzero m /\ all_in_range l /\ all_in_range m)
          (ensures admitted_all (app l m) /\ all_nonzero (app l m) /\ all_in_range (app l m)) =
  match l with [] -> () | _ :: t -> app_props t m

(* THEOREM 2. *)
#push-options "--z3rlimit 80"
let parse_render (u: uom) : Lemma (requires canonical u) (ensures parse (render u) == Ok u) =
  atom_lt_order ();
  let m = u.factors in
  let num = positives m in
  let den = negatives m in
  render_eq u;
  positives_pos m; negatives_neg m;
  no_zero_nonzero m;
  positives_sub m; negatives_sub m; admitted_positives m; admitted_negatives m;
  sorted_distinct m; distinct_positives m; distinct_negatives m;
  let pnn (y: atom) : Lemma (mem_key y num ==> not (mem_key y den)) = FStar.Classical.move_requires (pos_not_neg y) m in
  FStar.Classical.forall_intro pnn;
  distinct_app num den;
  app_props num den;
  (* the fold of every factor, once each, from the empty product *)
  let items = app num den in
  steps_ok_lemma [] items;
  fold_items_wf [] items;
  let value (y: atom) : Lemma (exp_of (fold_items [] items) y == exp_of m y) =
    fold_items_exp [] items y; exp_sum_app num den y; exp_sum_split m y; exp_sum_sorted m y in
  FStar.Classical.forall_intro value;
  ext atom_lt (fold_items [] items) m;
  (* the text: the first component through the component arm, the rest through the loop *)
  (match num with
   | [] ->
       (match den with
        | [] -> assert_norm (parse [D1] == Ok (of_factors []))
        | (a, e) :: _ ->
            (* a leading `/`: the component arm at a term's start reads it exactly as the separator arm does *)
            assert (render u == render_tail den);
            assert (parse (render u) == comp (render_tail den) 0 [initial_frame] true);
            assert (comp (render_tail den) 0 [initial_frame] true == sep (render_tail den) 0 [initial_frame]);
            loop den 0 initial_frame)
   | (a, e) :: t ->
       let acc1 = combine atom_lt [] 1 [(a, e)] in
       assert (fold_items [] items == fold_items acc1 (app t den));
       assert (render u == app (atom_text a e) (render_tail (app t den)));
       assert (parse (render u) == comp (app (atom_text a e) (render_tail (app t den))) 0 [initial_frame] true);
       combine_sign_abs [] a e;
       assert (try_combine initial_frame.acc initial_frame.next [(a, iabs e)] == Some acc1);
       comp_token a e (render_tail (app t den)) 0 initial_frame [] true;
       combine_wf atom_lt [] 1 [(a, e)];
       loop (app t den) (len (atom_text a e)) ({ initial_frame with acc = acc1 }));
  (* and the folded product is the unit *)
  (match u with | Mkuom _ -> ())
#pop-options

(* ---- a canonical unit's text is spelled entirely in the alphabet ---- *)

let rec no_other_app (l m: text) : Lemma (no_other (app l m) == (no_other l && no_other m)) =
  match l with
  | [] -> ()
  | _ :: t -> no_other_app t m

let rec digits_no_other (n: nat) : Lemma (ensures no_other (digits n)) (decreases n) =
  if n < 10 then () else (digits_no_other (n / 10); no_other_app (digits (n / 10)) [digit_of (n % 10)])

let atom_text_no_other (a: atom) (e: int) : Lemma (requires admitted a) (ensures no_other (atom_text a e)) =
  let ex = (if iabs e = 1 then [] else digits (iabs e)) in
  no_other_app a.prefix a.symbol;
  (if iabs e = 1 then () else digits_no_other (iabs e));
  no_other_app a.symbol ex; no_other_app a.prefix (app a.symbol ex)

let rec render_tail_no_other (items: factors) : Lemma (requires admitted_all items) (ensures no_other (render_tail items)) =
  match items with
  | [] -> ()
  | (a, e) :: t -> atom_text_no_other a e; render_tail_no_other t; no_other_app (atom_text a e) (render_tail t)

let rec admitted_app (l m: factors) : Lemma (requires admitted_all l /\ admitted_all m) (ensures admitted_all (app l m)) =
  match l with
  | [] -> ()
  | _ :: t -> admitted_app t m

let render_no_other (u: uom) : Lemma (requires canonical u) (ensures no_other (render u)) =
  let m = u.factors in
  render_eq u; admitted_positives m; admitted_negatives m;
  (match positives m with
   | [] -> (match negatives m with [] -> () | den -> render_tail_no_other den)
   | (a, e) :: t ->
       admitted_app t (negatives m);
       atom_text_no_other a e; render_tail_no_other (app t (negatives m));
       no_other_app (atom_text a e) (render_tail (app t (negatives m))))

(* ======================================================================================
   10. TWINS (Phase 309) — the extractor premise, sampled at this model. See `WireColumn.fst`'s
       twins section for what a twin is: a closure the normaliser discharges to `true` under the
       model's own semantics, which the oracle host then runs as extracted F#.
   ====================================================================================== *)

noeq type twin = { tname : string; tholds : unit -> bool }

let rec twins_hold (l: list twin) : Tot bool =
  match l with
  | [] -> true
  | t :: r -> t.tholds () && twins_hold r

let hour : atom = { symbol = [Lh]; prefix = [] }
let kilometre : atom = { symbol = [Lm]; prefix = [Lk] }
let metre : atom = { symbol = [Lm]; prefix = [] }
let second : atom = { symbol = [Ls]; prefix = [] }

(* `km/h`: the factors in canonical order (`h` before `m`), the hour under the quotient. *)
let km_per_h : uom = { factors = [ (hour, -1); (kilometre, 1) ] }
let m_per_s : uom = { factors = [ (metre, 1); (second, -1) ] }

let twins : list twin = [
  { tname = "parse-km-per-h";
    tholds = (fun () -> parse [Lk; Lm; Slash; Lh] = Ok km_per_h) };
  { tname = "render-km-per-h";
    tholds = (fun () -> render km_per_h = [Lk; Lm; Slash; Lh]) };
  { tname = "render-dimensionless-is-one";
    tholds = (fun () -> render dimensionless = [D1] && parse [D1] = Ok dimensionless) };
  { tname = "km-per-h-to-m-per-s-is-5-18";
    tholds = (fun () -> compatible km_per_h m_per_s && conversion_factor km_per_h m_per_s = Some (Ok ({ rn = 5; rd = 18 }))) };
  { tname = "prefix-not-allowed-on-min";
    tholds = (fun () -> parse [Lk; Lm; Li; Ln] = Refused (PrefixNotAllowed [Lk; Lm; Li; Ln] 0)) };
  { tname = "mul-merges-and-orders";
    tholds = (fun () -> mul m_per_s km_per_h = Some ({ factors = [ (hour, -1); (metre, 1); (kilometre, 1); (second, -1) ] })) } ]

let _ = assert_norm (twins_hold twins == true)
