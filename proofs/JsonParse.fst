(*
   JsonParse — an F* model of Fuaran.Core's recursive-descent JSON PARSER, with the two guards and
   the error classification as machine-checked theorems (fuaran-core Phase 146 — the boundary
   Phase 135's theorem 1 named and left open).

   WHAT IS MODELLED. `Fuaran.Core.Json.parseDetailedWithPolicy` (src/Fuaran.Core.Wire/Wire.fs),
   clause for clause: `skipWs`, `expect`, `parseString` with its eight short escapes and the
   `\uXXXX` path, `parseNumber` with the Int32 and int53 token guards, `parseLiteral`, and the
   mutual `parseValue` / `parseObject` / `parseArray` with the explicit depth counter — including
   the `EraseMemberNull` fork, which lives in the member loop and nowhere else. The entry point's
   trailing-input check is modelled too, because `TrailingCharacters` is the one classified failure
   the parser does NOT raise through its internal exception.

   WHAT IS PROVED.
     - `parse_total` — the parser reaches exactly one outcome on every input: an accepted value or
       a CLASSIFIED refusal, never both and never neither. That it is `Tot` at all is the content:
       F* admits a recursive definition only once it has shown it terminates on every input, and a
       recursive-descent parser over a mutable index has no syntactic reason to.
     - `depth_bound_exact` — on the canonical nesting family, the parse succeeds exactly when the
       budget is at least the nesting, at every depth and for every suffix; with
       `depth_error_only_at_a_container` (carried in the parsers' RETURN TYPES) saying the error
       kind is never invented anywhere else, and `depth_bound_scalars_unaffected` saying an
       exhausted budget still accepts a scalar.
     - `int53_guard_exact` — every integer token the parser ACCEPTS is int53-safe by value, whether
       it landed on `JInt` or on `JFloat`; the guard as production spells it is SOUND (it never
       admits an unsafe integer) and CONSERVATIVE (`int53_guard_conservative` exhibits a safe value
       it refuses). See the finding below: "exact" is not available, and claiming it would be false.
     - `error_kind_exhaustive` — the twelve `JsonErrorKind` cases are confined to the layers that
       raise them (again in the return types, so the confinement is checked at every call site
       rather than asserted once) and every one of the twelve is REACHABLE, by a witness input.
       A classification with a dead case is not exhaustive, it is merely closed.

   WHAT IS NOT MODELLED, AND WHY — the theorem's boundary.

     - THE VALUE OF A `\uXXXX` ESCAPE. Its two GUARDS are modelled and proved — a truncated escape
       is `TruncatedUnicodeEscape`, a bad hex digit is `BadHexDigit`, and the position each reports
       is production's — but the code point the four digits denote is carried symbolically
       (`OUni`), not computed. Computing it needs integers, which the extraction deliberately does
       not carry (README, finding 2). This is the grammar path the phase's time box names: the
       classification is proved, the transliteration is sampled by the differential.

     - THE FLOAT READBACK IS OPAQUE. `System.Double.TryParse`'s verdict on a token — parses
       finitely, parses to a non-finite, does not parse — is a PARAMETER (`float_read`), exactly as
       Phase 135 made `float i` a parameter and Phase 136 made the hash one. Nothing in this model
       looks inside a float; what it decides is WHICH of the three outcomes each verdict leads to,
       and that is the parser's own logic rather than .NET's.

     - THE DEPTH CAP IS A LIST, and the rendered cap in the message is a string parameter. F*'s
       `int` does not survive this extraction (README, finding 2), so `depth >= maxDepth` is spelt
       as an exhausted `list unit` budget, one element per descent — the same device `Chain.fst`
       uses for a sequence number, for the same reason. Every non-negative cap is representable.

     - THE ALPHABET. A character is a constructor of `ch` — one per character the parser
       DISTINGUISHES, plus `COther`, which carries the character verbatim as a string. So an
       arbitrary character is not abstracted away (two different `COther`s are different), but the
       model reads a `list ch` where production reads a .NET string, and the differential's bridge
       is what says those are the same sequence.

     - WHAT THE PARSER PRODUCES FOR A NUMBER is its TOKEN, not its value: `JInt`/`JFloat` carry the
       characters production handed to `Int32.TryParse` / `Double.TryParse`. Which CONSTRUCTOR is
       chosen is the guard under test and is modelled exactly; the numeral's value is .NET's to
       compute, and the differential compares it there.

   A FINDING, recorded here because the phase's own task list assumes otherwise: THE INT53 GUARD IS
   NOT EXACT, AND CANNOT BE, BECAUSE THIS PARSER DOES NOT REJECT LEADING ZEROS. Wire.fs justifies
   comparing the digit string lexically with "JSON forbids leading zeros, so for equal length that
   IS the numeric order" — but `parseNumber`'s digit loop accepts `007`, and for a token `Int32`
   refuses, padding lengthens the string without changing the value. So `0009007199254740992` — 19
   characters, value exactly 2^53, comfortably safe — is refused as `MalformedNumber`. The error
   only ever runs that way: a padded reading is numerically smaller than an unpadded one of the
   same length, so no padding can make an unsafe integer look safe. `int53_guard_sound` and
   `int53_guard_conservative` are those two sentences, proved.

   HOW TO READ IT. Every definition names its F# counterpart in the comment above it, and error
   MESSAGES are reproduced verbatim rather than classified, because the differential compares them:
   a model agreeing on the kind alone would not notice a parser that named the wrong expectation.
   The POSITION each failure reports is modelled as the input SUFFIX at the raise point, from which
   the host recovers production's index by subtraction — the one faithful way to carry a position
   through a model that has no integers.

   TERMINATION, which is the theorem rather than a formality. Every consuming parser carries
   `llen (rest) < llen (input)` in its RETURN TYPE; that refinement, and nothing else, is what lets
   the mutual group be accepted as `Tot`. The lexicographic order is `%[remaining input; phase]`
   with `parse_items` above `parse_value` above `parse_object`/`parse_array`, because those are the
   three edges that recurse at an unchanged input position.

   Apache-2.0, like everything beside it.
*)
module JsonParse

(* The parser is a large mutual group over a 45-constructor alphabet, so the default context makes
   the depth induction's queries expensive — 128s for the module, against 72s with upstream's
   context pruning, which is the successor to the proof hints the pinned release removed (README
   finding 1). The leg runs `--quake 3` three times from a cold cache, so that difference is worth
   having. Scoped here rather than added to `check.ps1`'s flags, for the reason `TreeOps.fst` gives
   at the same line: pruning changes which facts a query can see, so a module that has not been
   checked under it must not be switched to it as a side effect of another module's cost. *)
#set-options "--ext context_pruning"

(* ======================================================================================
   0. Lists, self-contained so the extraction needs only `Prims` (README, finding 2).
   ====================================================================================== *)

(* PROOF-ONLY — erased at extraction, and the measure every refinement below is written in. *)
[@@ noextract_to "FSharp"]
let rec llen (#a: Type) (l: list a) : Tot nat =
  match l with
  | [] -> 0
  | _ :: t -> 1 + llen t

let rec rev_app (#a: Type) (l acc: list a) : Tot (list a) (decreases l) =
  match l with
  | [] -> acc
  | x :: t -> rev_app t (x :: acc)

(* F#: `List.rev` — the one every accumulate-and-reverse walk here ends with, which is the shape
   `ResizeArray` + `List.ofSeq` has in production. *)
let rev (#a: Type) (l: list a) : Tot (list a) = rev_app l []

(* ======================================================================================
   1. The alphabet (F#: `char`, one constructor per character `parseDetailedWithPolicy`
      distinguishes).

      `COther` carries the character itself, so a character the parser treats as ordinary is
      still ITSELF here — the model does not identify two of them. See the header's note.
   ====================================================================================== *)

type ch =
  (* whitespace — F#: `isWs` *)
  | CSpace | CTab | CNewline | CReturn
  (* structural *)
  | CQuote | CBackslash | CSlash
  | CLBrace | CRBrace | CLBrack | CRBrack | CColon | CComma
  (* numeric punctuation *)
  | CMinus | CPlus | CDot
  (* digits *)
  | CD0 | CD1 | CD2 | CD3 | CD4 | CD5 | CD6 | CD7 | CD8 | CD9
  (* the lowercase letters the parser reads: a-f are hex digits; l n r s t u spell the three
     literals and the short escapes *)
  | CLa | CLb | CLc | CLd | CLe | CLf | CLl | CLn | CLr | CLs | CLt | CLu
  (* the uppercase hex digits; CUe is also the exponent marker 'E' *)
  | CUa | CUb | CUc | CUd | CUe | CUf
  (* every other character, carried verbatim *)
  | COther : c:string -> ch

(* The one-character spelling, for the messages that name a character. F#: `string c`. *)
let ch_str (c: ch) : Tot string =
  match c with
  | CSpace -> " " | CTab -> "\t" | CNewline -> "\n" | CReturn -> "\r"
  | CQuote -> "\"" | CBackslash -> "\\" | CSlash -> "/"
  | CLBrace -> "{" | CRBrace -> "}" | CLBrack -> "[" | CRBrack -> "]"
  | CColon -> ":" | CComma -> ","
  | CMinus -> "-" | CPlus -> "+" | CDot -> "."
  | CD0 -> "0" | CD1 -> "1" | CD2 -> "2" | CD3 -> "3" | CD4 -> "4"
  | CD5 -> "5" | CD6 -> "6" | CD7 -> "7" | CD8 -> "8" | CD9 -> "9"
  | CLa -> "a" | CLb -> "b" | CLc -> "c" | CLd -> "d" | CLe -> "e" | CLf -> "f"
  | CLl -> "l" | CLn -> "n" | CLr -> "r" | CLs -> "s" | CLt -> "t" | CLu -> "u"
  | CUa -> "A" | CUb -> "B" | CUc -> "C" | CUd -> "D" | CUe -> "E" | CUf -> "F"
  | COther s -> s

let rec chs_str (l: list ch) : Tot string (decreases l) =
  match l with
  | [] -> ""
  | c :: t -> ch_str c ^ chs_str t

(* F#: `isWs`. *)
let is_ws (c: ch) : Tot bool = c = CSpace || c = CTab || c = CNewline || c = CReturn

let is_digit (c: ch) : Tot bool =
  c = CD0 || c = CD1 || c = CD2 || c = CD3 || c = CD4 ||
  c = CD5 || c = CD6 || c = CD7 || c = CD8 || c = CD9

(* F#: the accept set of `hexDigit`, whose reject arm is the `BadHexDigit` failure. The VALUE is
   not computed — see the header's note on `\uXXXX`. *)
let is_hex (c: ch) : Tot bool =
  is_digit c ||
  c = CLa || c = CLb || c = CLc || c = CLd || c = CLe || c = CLf ||
  c = CUa || c = CUb || c = CUc || c = CUd || c = CUe || c = CUf

(* F#: the eight arms of `parseString`'s escape match that are not `'u'`. *)
let is_short_escape (c: ch) : Tot bool =
  c = CQuote || c = CBackslash || c = CSlash ||
  c = CLn || c = CLr || c = CLt || c = CLb || c = CLf

(* F#: `peek () = 'e' || peek () = 'E'`. *)
let is_exp (c: ch) : Tot bool = c = CLe || c = CUe

(* ======================================================================================
   2. The value model (F#: `JVal`), and the classified failure (F#: `JsonErrorKind`).
   ====================================================================================== *)

(* A character of a DECODED string. `OLit` is a character copied through; `OEsc` is a short
   escape, keyed by the letter that spelled it; `OUni` carries a `\uXXXX`'s four hex digits
   rather than the code point they denote (header). *)
type och =
  | OLit : c:ch -> och
  | OEsc : e:ch -> och
  | OUni : h1:ch -> h2:ch -> h3:ch -> h4:ch -> och

(* F#: `JVal`. The two numeric cases carry the TOKEN — which constructor was chosen is the guard
   under test; the numeral's value is .NET's (header). *)
type jval =
  | JStr   : s:list och -> jval
  | JInt   : tok:list ch -> jval
  | JBool  : b:bool -> jval
  | JFloat : tok:list ch -> jval
  | JArr   : items:list jval -> jval
  | JObj   : fields:list (list och & jval) -> jval

(* F#: `JsonErrorKind` — all twelve cases, in declaration order. *)
type ekind =
  | UnexpectedChar
  | UnexpectedEndOfInput
  | ExpectedToken
  | UnterminatedString
  | UnterminatedEscape
  | TruncatedUnicodeEscape
  | BadEscape
  | BadHexDigit
  | MalformedNumber
  | NullNotRepresentable
  | MaxDepthExceeded
  | TrailingCharacters

(* F#: `NullPolicy`. *)
type policy =
  | RejectNull
  | EraseMemberNull

(* What a sub-parser returns: a value plus the REMAINING input, or a classified failure at the
   suffix production's `i` pointed at when it raised. F#: the `JVal` returned and `i` advanced, or
   `raise (JsonParseError(kind, msg, i))`. *)
type pres (a: Type) =
  | POk  : v:a -> rest:list ch -> pres a
  | PErr : k:ekind -> msg:string -> at:list ch -> pres a

(* F#: the entry point's `Result<JVal, JsonError>`. `at` is the suffix; the host recovers
   `JsonError.Position` as `input.Length - llen at`. *)
type jresult =
  | ROk  : v:jval -> jresult
  | RErr : k:ekind -> msg:string -> at:list ch -> jresult

(* ---- the messages, verbatim from Wire.fs ---- *)

let msg_expect_char (c: ch) : Tot string = "expected '" ^ ch_str c ^ "'"
let msg_expect_lit (l: string) : Tot string = "expected '" ^ l ^ "'"
let msg_unexpected_char (c: ch) : Tot string = "unexpected character '" ^ ch_str c ^ "'"
let msg_bad_escape (e: ch) : Tot string = "bad escape '\\" ^ ch_str e ^ "'"
let msg_malformed (tok: list ch) : Tot string = "malformed number: " ^ chs_str tok

let msg_nonfinite (tok: list ch) : Tot string =
  "number outside the finite double range; it cannot round-trip on the wire: " ^ chs_str tok

let msg_int53 (tok: list ch) : Tot string =
  "integer literal outside the int53 safe range (|n| > 2^53); it cannot round-trip without precision loss: "
  ^ chs_str tok

let msg_null_strict: string = "null is not representable in the Fuaran wire JVal model"

let msg_null_tolerant: string =
  "null is not representable in the Fuaran wire JVal model, and this position has no absence to erase it to (only an object-member null is erased)"

(* F#: `"max nesting depth " + string maxDepth + " exceeded"`. The rendered cap is a parameter for
   the same reason the cap itself is a list — see the header. *)
let msg_depth (rendered_cap: string) : Tot string =
  "max nesting depth " ^ rendered_cap ^ " exceeded"

(* ======================================================================================
   3. The two numeric guards, WITHOUT integers.

      Production compares the digit string against a decimal LITERAL; the model compares the digit
      LIST against that literal's digits. The `16` and the `10` disappear: each is the length of
      its own limit, which is exactly why production writes it.
   ====================================================================================== *)

let rec len_lt (x y: list ch) : Tot bool (decreases x) =
  match x, y with
  | [], [] -> false
  | [], _ :: _ -> true
  | _ :: _, [] -> false
  | _ :: xt, _ :: yt -> len_lt xt yt

let rec len_eq (x y: list ch) : Tot bool (decreases x) =
  match x, y with
  | [], [] -> true
  | [], _ :: _ -> false
  | _ :: _, [] -> false
  | _ :: xt, _ :: yt -> len_eq xt yt

[@@ noextract_to "FSharp"]
let rec len_lt_spec (x y: list ch)
  : Lemma (ensures len_lt x y == (llen x < llen y)) (decreases x) [SMTPat (len_lt x y)] =
  match x, y with
  | _ :: xt, _ :: yt -> len_lt_spec xt yt
  | _ -> ()

[@@ noextract_to "FSharp"]
let rec len_eq_spec (x y: list ch)
  : Lemma (ensures len_eq x y == (llen x = llen y)) (decreases x) [SMTPat (len_eq x y)] =
  match x, y with
  | _ :: xt, _ :: yt -> len_eq_spec xt yt
  | _ -> ()

let rec mem_ch (c: ch) (l: list ch) : Tot bool (decreases l) =
  match l with
  | [] -> false
  | x :: t -> x = c || mem_ch c t

(* The digits in ascending order, and `after` — the suffix strictly beyond a digit. Together they
   are the digit ORDER, spelled without a numeral. *)
let digits_asc: list ch = [CD0; CD1; CD2; CD3; CD4; CD5; CD6; CD7; CD8; CD9]

let rec after (c: ch) (l: list ch) : Tot (list ch) (decreases l) =
  match l with
  | [] -> []
  | x :: t -> if x = c then t else after c t

let digit_lt (a b: ch) : Tot bool = mem_ch b (after a digits_asc)

(* F#: `System.String.CompareOrdinal(digits, limit) <= 0`, which production only ever evaluates at
   equal lengths — and for equal-length digit strings ordinal order IS numeric order. *)
let rec dig_le (x y: list ch) : Tot bool (decreases x) =
  match x, y with
  | [], [] -> true
  | a :: xt, b :: yt -> if a = b then dig_le xt yt else digit_lt a b
  | _ -> false

(* Leading zeros removed, keeping one digit. This is what `Int32.TryParse` does to a digit string
   (it reads the VALUE), and it is precisely what production's int53 test does NOT do — which is
   the finding in the header. *)
let rec strip0 (d: list ch)
  : Tot (r: list ch { llen r <= llen d /\ (r == d \/ llen r < llen d) }) (decreases d) =
  match d with
  | CD0 :: t -> if Nil? t then d else strip0 t
  | _ -> d

(* 9007199254740992 = 2^53 — F#: the literal in `parseNumber`'s int53 comparison. *)
let lim_i53: list ch = [CD9; CD0; CD0; CD7; CD1; CD9; CD9; CD2; CD5; CD4; CD7; CD4; CD0; CD9; CD9; CD2]

(* 2147483647 / 2147483648 — the Int32 range `System.Int32.TryParse` accepts. *)
let lim_i32_pos: list ch = [CD2; CD1; CD4; CD7; CD4; CD8; CD3; CD6; CD4; CD7]
let lim_i32_neg: list ch = [CD2; CD1; CD4; CD7; CD4; CD8; CD3; CD6; CD4; CD8]

(* F#: `digits.Length < 16 || (digits.Length = 16 && CompareOrdinal(digits, "9007199254740992") <= 0)`
   — production's guard, on production's RAW digit string. *)
let int53_safe (d: list ch) : Tot bool =
  len_lt d lim_i53 || (len_eq d lim_i53 && dig_le d lim_i53)

(* The same test on the VALUE the digits denote. The two differ exactly on a leading-zero token,
   and that difference is `int53_guard_conservative` below. *)
let int53_safe_value (d: list ch) : Tot bool = int53_safe (strip0 d)

(* F#: `System.Int32.TryParse tok`, at the level this model works: does the token name a value in
   the Int32 range? A token with no digit at all (`-`) does not. *)
let int32_fits (neg: bool) (d: list ch) : Tot bool =
  let s = strip0 d in
  let lim = if neg then lim_i32_neg else lim_i32_pos in
  not (Nil? s) && (len_lt s lim || (len_eq s lim && dig_le s lim))

(* ======================================================================================
   4. The scanners (F#: `skipWs`, `expect`, `parseString`, `parseNumber`).

      Each carries its consumption in its RETURN TYPE — the termination argument for section 5,
      and the confinement of the error kinds it can raise, which is half of theorem 4.
   ====================================================================================== *)

(* The kinds `parseString` can raise. Every other kind arising inside a string would be a defect
   this refinement catches at the definition rather than in a test. *)
let str_kind (k: ekind) : Tot bool =
  k = ExpectedToken || k = UnterminatedString || k = UnterminatedEscape ||
  k = TruncatedUnicodeEscape || k = BadEscape || k = BadHexDigit

(* F#: `skipWs`. *)
let rec skip_ws (s: list ch) : Tot (r: list ch { llen r <= llen s }) (decreases s) =
  match s with
  | [] -> []
  | c :: t -> if is_ws c then skip_ws t else s

(* F#: `expect c` — the failure captures `i` UNADVANCED, so the suffix is the one it was given. *)
let expect (c: ch) (s: list ch)
  : Tot (r: pres unit { (POk? r ==> llen (POk?.rest r) < llen s) /\
                        (PErr? r ==> PErr?.k r == ExpectedToken) }) =
  match s with
  | x :: t -> if x = c then POk () t else PErr ExpectedToken (msg_expect_char c) s
  | [] -> PErr ExpectedToken (msg_expect_char c) s

(* F#: the body of `parseString`'s `while not fin` loop. The escape arms are in production's own
   order, and each failure's suffix is the one production's `i` pointed at when it raised: the
   truncated-escape and bad-hex-digit failures are AFTER `\u` (the two characters are consumed
   before the length test), and `BadEscape` is after the escape letter too. *)
let rec string_body (acc: list och) (s: list ch)
  : Tot (r: pres (list och) { (POk? r ==> llen (POk?.rest r) < llen s) /\
                              (PErr? r ==> str_kind (PErr?.k r)) })
        (decreases s) =
  match s with
  | [] -> PErr UnterminatedString "unterminated string" []
  | CQuote :: t -> POk (rev acc) t
  | CBackslash :: t ->
    (match t with
     | [] -> PErr UnterminatedEscape "unterminated escape" []
     | CLu :: u ->
       (match u with
        | a :: b :: c :: d :: r ->
          if is_hex a && is_hex b && is_hex c && is_hex d then
            string_body (OUni a b c d :: acc) r
          else
            (* F# evaluates the four `hexDigit` calls left to right and `fail` captures `i`, which
               `i <- i + 4` has not yet advanced — so the position is the FIRST hex digit whichever
               of the four is bad. *)
            PErr BadHexDigit "bad hex digit in \\u escape" u
        | _ -> PErr TruncatedUnicodeEscape "truncated \\u escape" u)
     | e :: u ->
       if is_short_escape e then string_body (OEsc e :: acc) u
       else PErr BadEscape (msg_bad_escape e) u)
  | c :: t -> string_body (OLit c :: acc) t

(* F#: `parseString`. *)
let parse_string (s: list ch)
  : Tot (r: pres (list och) { (POk? r ==> llen (POk?.rest r) < llen s) /\
                              (PErr? r ==> str_kind (PErr?.k r)) }) =
  match expect CQuote s with
  | PErr k m a -> PErr k m a
  | POk _ t -> string_body [] t

(* F#: the three `while … digit` loops of `parseNumber`. *)
let rec take_digits (s: list ch)
  : Tot (p: (list ch & list ch) { llen (snd p) <= llen s /\
                                  (Cons? (fst p) ==> llen (snd p) < llen s) })
        (decreases s) =
  match s with
  | d :: t ->
    if is_digit d then
      let (ds, r) = take_digits t in
      (d :: ds, r)
    else ([], s)
  | [] -> ([], s)

let rec app (#a: Type) (x y: list a) : Tot (list a) (decreases x) =
  match x with
  | [] -> y
  | h :: t -> h :: app t y

(* F#: the token scan of `parseNumber` — optional sign, integer digits, optional fraction,
   optional exponent — returning the token, whether it is a FLOAT token, and the rest. It consumes
   at least one character, because `parseValue` only reaches it on `-` or a digit. *)
let scan_number (s: list ch)
  : Tot (p: (list ch & bool & list ch) { let (_, _, r) = p in llen r <= llen s })
  =
  let (neg, s1) = (match s with CMinus :: t -> ([CMinus], t) | _ -> ([], s)) in
  let (ints, s2) = take_digits s1 in
  let (frac, isf1, s3) =
    (match s2 with
     | CDot :: t -> let (fs, r) = take_digits t in (CDot :: fs, true, r)
     | _ -> ([], false, s2)) in
  let (expo, isf2, s4) =
    (match s3 with
     | e :: t ->
       if is_exp e then
         (match t with
          | sg :: u ->
            if sg = CPlus || sg = CMinus then
              let (ds, r) = take_digits u in (e :: sg :: ds, true, r)
            else let (ds, r) = take_digits t in (e :: ds, true, r)
          | [] -> ([e], true, []))
       else ([], false, s3)
     | [] -> ([], false, s3)) in
  (app neg (app ints (app frac expo)), isf1 || isf2, s4)

(* The three verdicts `System.Double.TryParse` plus the finiteness gate can reach. A PARAMETER:
   see the header's note on opacity. *)
type freadv =
  | FFinite
  | FNonFinite
  | FUnparsable

(* What `parseNumber` decides once the token is scanned, in production's own branch order. This is
   the whole of the two numeric guards. *)
let classify_number (float_read: list ch -> freadv) (tok: list ch) (isf: bool)
  : Tot (r: pres jval { PErr? r ==> PErr?.k r == MalformedNumber }) =
  let (neg, digits) = (match tok with CMinus :: t -> (true, t) | _ -> (false, tok)) in
  if isf then
    (match float_read tok with
     | FFinite -> POk (JFloat tok) []
     | FNonFinite -> PErr MalformedNumber (msg_nonfinite tok) []
     | FUnparsable -> PErr MalformedNumber (msg_malformed tok) [])
  else if int32_fits neg digits then POk (JInt tok) []
  else if int53_safe digits then
    (* F#: the SECOND `Double.TryParse`, which has no finiteness gate — an int53-safe token cannot
       overflow a double, and the model reproduces the absence rather than repairing it. *)
    (match float_read tok with
     | FUnparsable -> PErr MalformedNumber (msg_malformed tok) []
     | _ -> POk (JFloat tok) [])
  else PErr MalformedNumber (msg_int53 tok) []

(* F#: `parseNumber` whole. Both failures fire AFTER the token scan, so their position is the end
   of the token — which is where production's `i` stands when `fail` captures it. *)
let parse_number (float_read: list ch -> freadv) (s: list ch)
  : Tot (r: pres jval { (POk? r ==> llen (POk?.rest r) <= llen s) /\
                        (PErr? r ==> PErr?.k r == MalformedNumber) }) =
  let (tok, isf, rest) = scan_number s in
  match classify_number float_read tok isf with
  | POk v _ -> POk v rest
  | PErr k m _ -> PErr k m rest

(* ======================================================================================
   5. The recursive descent (F#: `parseValue` / `parseObject` / `parseArray`), with the depth
      counter and the `EraseMemberNull` fork.

      Two refinements ride every signature. `llen rest < llen s` is the termination argument. And
      `MaxDepthExceeded ==> starts_container at` is the DEPTH GUARD'S CONFINEMENT: the kind is
      raised at a `{` or a `[` and nowhere else, checked at every call site by the type-checker
      rather than asserted once by a lemma.
   ====================================================================================== *)

let starts_container (s: list ch) : Tot bool =
  match s with
  | CLBrace :: _ -> true
  | CLBrack :: _ -> true
  | _ -> false

(* Every kind except `TrailingCharacters`, which the entry point raises and the descent cannot. *)
let val_kind (k: ekind) : Tot bool = k <> TrailingCharacters

let depth_confined (#a: Type) (r: pres a) : Tot bool =
  match r with
  | PErr k _ at -> val_kind k && (k <> MaxDepthExceeded || starts_container at)
  | POk _ _ -> true

(* F#: the `tolerateMemberNull && i + 4 <= n && input.Substring(i, 4) = "null"` test, and the
   `i <- i + 4` it guards — the ONE place in the parser that erases anything. *)
let drop_null4 (s: list ch) : Tot (o: option (list ch) { Some? o ==> llen (Some?.v o) < llen s }) =
  match s with
  | CLn :: CLu :: CLl :: CLl :: t -> Some t
  | _ -> None

let rec parse_value
  (float_read: list ch -> freadv) (cap: string) (tol: bool) (b: list unit) (s: list ch)
  : Tot (r: pres jval { (POk? r ==> llen (POk?.rest r) < llen s) /\ depth_confined r })
        (decreases %[llen s; 2])
  =
  let w = skip_ws s in
  match w with
  | [] -> PErr UnexpectedEndOfInput "unexpected end of input" []
  | CQuote :: _ ->
    (match parse_string w with
     | PErr k m a -> PErr k m a
     | POk cs r -> POk (JStr cs) r)
  | CLBrace :: _ -> parse_object float_read cap tol b w
  | CLBrack :: _ -> parse_array float_read cap tol b w
  (* F#: `parseLiteral "true" (JBool true)`, inlined — the model has the four characters where
     production has a `Substring` comparison, and the failure's position is the same. *)
  | CLt :: CLr :: CLu :: CLe :: t -> POk (JBool true) t
  | CLt :: _ -> PErr ExpectedToken (msg_expect_lit "true") w
  | CLf :: CLa :: CLl :: CLs :: CLe :: t -> POk (JBool false) t
  | CLf :: _ -> PErr ExpectedToken (msg_expect_lit "false") w
  (* F#: the `'n'` arm. Under the tolerant policy a MEMBER null never reaches here — the member
     loop absorbs it — so a null arriving here has no absence to erase it to, and says so. *)
  | CLn :: _ ->
    PErr NullNotRepresentable (if tol then msg_null_tolerant else msg_null_strict) w
  | c :: _ ->
    if c = CMinus || is_digit c then
      (match parse_number float_read w with
       | PErr k m a -> PErr k m a
       | POk v r -> POk v r)
    else PErr UnexpectedChar (msg_unexpected_char c) w

(* `w` is refined to a container position because `parseValue` only ever calls `parseObject` after
   matching `'{'` — and because that is what makes the depth error's CONFINEMENT checkable here
   rather than asserted about the caller. *)
and parse_object
  (float_read: list ch -> freadv) (cap: string) (tol: bool) (b: list unit)
  (w: list ch { starts_container w })
  : Tot (r: pres jval { (POk? r ==> llen (POk?.rest r) < llen w) /\ depth_confined r })
        (decreases %[llen w; 1])
  =
  (* F#: `if depth >= maxDepth then fail MaxDepthExceeded …`, BEFORE `expect '{'` — so the
     position is the brace itself, which is what `starts_container` above records. *)
  match b with
  | [] -> PErr MaxDepthExceeded (msg_depth cap) w
  | _ :: b' ->
    (match expect CLBrace w with
     | PErr k m a -> PErr k m a
     | POk _ t ->
       let t1 = skip_ws t in
       (match t1 with
        | CRBrace :: t2 -> POk (JObj []) t2
        | _ ->
          (match parse_members float_read cap tol b' [] t1 with
           | PErr k m a -> PErr k m a
           | POk fs r -> POk (JObj fs) r)))

and parse_array
  (float_read: list ch -> freadv) (cap: string) (tol: bool) (b: list unit)
  (w: list ch { starts_container w })
  : Tot (r: pres jval { (POk? r ==> llen (POk?.rest r) < llen w) /\ depth_confined r })
        (decreases %[llen w; 1])
  =
  match b with
  | [] -> PErr MaxDepthExceeded (msg_depth cap) w
  | _ :: b' ->
    (match expect CLBrack w with
     | PErr k m a -> PErr k m a
     | POk _ t ->
       let t1 = skip_ws t in
       (match t1 with
        | CRBrack :: t2 -> POk (JArr []) t2
        | _ ->
          (match parse_items float_read cap tol b' [] t1 with
           | PErr k m a -> PErr k m a
           | POk xs r -> POk (JArr xs) r)))

(* F#: the `while go` loop of `parseObject`. The budget it is given is already the CHILD's — the
   members' values sit one level down. *)
and parse_members
  (float_read: list ch -> freadv) (cap: string) (tol: bool) (b: list unit)
  (acc: list (list och & jval)) (s: list ch)
  : Tot (r: pres (list (list och & jval)) { (POk? r ==> llen (POk?.rest r) <= llen s) /\
                                            depth_confined r })
        (decreases %[llen s; 0])
  =
  let w = skip_ws s in
  match parse_string w with
  | PErr k m a -> PErr k m a
  | POk key t ->
    let t1 = skip_ws t in
    (match expect CColon t1 with
     | PErr k m a -> PErr k m a
     | POk _ t2 ->
       let t3 = skip_ws t2 in
       let erased = if tol then drop_null4 t3 else None in
       (match erased with
        | Some t4 ->
          (* The member is consumed and NOT added — the object reads as though it were omitted. *)
          let t5 = skip_ws t4 in
          (match t5 with
           | CComma :: t6 -> parse_members float_read cap tol b acc t6
           | CRBrace :: t6 -> POk (rev acc) t6
           | _ -> PErr ExpectedToken "expected ',' or '}'" t5)
        | None ->
          (match parse_value float_read cap tol b t3 with
           | PErr k m a -> PErr k m a
           | POk v t4 ->
             let t5 = skip_ws t4 in
             (match t5 with
              | CComma :: t6 -> parse_members float_read cap tol b ((key, v) :: acc) t6
              | CRBrace :: t6 -> POk (rev ((key, v) :: acc)) t6
              | _ -> PErr ExpectedToken "expected ',' or '}'" t5))))

(* F#: the `while go` loop of `parseArray`. Note it calls `parseValue` at the CURRENT position —
   the one edge in this group that recurses without consuming, which is why `parse_items` sits
   above `parse_value` in the termination order. *)
and parse_items
  (float_read: list ch -> freadv) (cap: string) (tol: bool) (b: list unit)
  (acc: list jval) (s: list ch)
  : Tot (r: pres (list jval) { (POk? r ==> llen (POk?.rest r) <= llen s) /\ depth_confined r })
        (decreases %[llen s; 3])
  =
  match parse_value float_read cap tol b s with
  | PErr k m a -> PErr k m a
  | POk v t ->
    let t1 = skip_ws t in
    (match t1 with
     | CComma :: t2 -> parse_items float_read cap tol b (v :: acc) t2
     | CRBrack :: t2 -> POk (rev (v :: acc)) t2
     | _ -> PErr ExpectedToken "expected ',' or ']'" t1)

(* F#: `parseDetailedWithPolicy`'s `try … with` — the value, then `skipWs`, then the leftover test.
   `TrailingCharacters` is the one classified failure NOT raised through `JsonParseError`: it is
   constructed here, from the leftover, which is why theorem 4 is stated about this entry point
   rather than about the raise sites. *)
let parse
  (float_read: list ch -> freadv) (cap: string) (pol: policy) (b: list unit) (s: list ch)
  : Tot jresult =
  let tol = EraseMemberNull? pol in
  match parse_value float_read cap tol b s with
  | PErr k m a -> RErr k m a
  | POk v r ->
    let r1 = skip_ws r in
    if Nil? r1 then ROk v else RErr TrailingCharacters "trailing characters" r1

(* ======================================================================================
   6. THEOREM — TOTALITY.

      That the parser is `Tot` is carried by its type: F* admits the mutual group above only after
      showing every function is defined on every input of its domain and terminates. That is the
      content of this theorem and not a formality — a recursive-descent parser over a mutable index
      has no syntactic reason to stop, and the refinements in section 5 are the argument that it
      does. What the lemma ADDS is that the outcome is exactly one of the two, so there is no input
      for which the parser is silent and none for which it both accepts and refuses.
   ====================================================================================== *)

let parse_total (float_read: list ch -> freadv) (cap: string) (pol: policy)
                (b: list unit) (s: list ch)
  : Lemma (ensures (let r = parse float_read cap pol b s in
                    (ROk? r \/ RErr? r) /\ ~(ROk? r /\ RErr? r)))
  = ()

(* ======================================================================================
   7. THEOREM — THE DEPTH BOUND.

      Three statements, because the obvious one-line form is false. "Input nested past the bound
      fails with MaxDepthExceeded and never otherwise" ignores that the parser is left to right and
      the FIRST failure wins: `[bad, [[[…]]]]` is an `UnexpectedChar` however deep it goes. What is
      true, and is proved here:

        1. the kind is CONFINED to a container position — `depth_confined`, carried in every
           signature in section 5 and re-checked at every call site;
        2. on the canonical nesting family the boundary is EXACT, at every depth and for every
           suffix — `depth_bound_exact`;
        3. the bound does not touch what is not nested — `depth_bound_scalars_unaffected`.
   ====================================================================================== *)

(* The canonical family, generalised over the SUFFIX — which is what makes it inductive. A model
   that only spoke about whole documents would need a prefix-determinism lemma; a model that
   threads the tail needs none.

   The innermost value is the empty STRING rather than a number, deliberately: a number scan is
   greedy (`take_digits` runs to the first non-digit), so a numeric innermost value would make the
   statement depend on what the suffix begins with. A string ends at its closing quote whatever
   follows, which is what lets the suffix be arbitrary. *)
[@@ noextract_to "FSharp"]
let rec nest_arr_at (k: list unit) (rest: list ch) : Tot (list ch) (decreases k) =
  match k with
  | [] -> CQuote :: CQuote :: rest
  | _ :: t -> CLBrack :: nest_arr_at t (CRBrack :: rest)

[@@ noextract_to "FSharp"]
let nest_arr (k: list unit) : Tot (list ch) = nest_arr_at k []

(* One level of nesting is three unfoldings — `parse_value` into `parse_array` into `parse_items`
   and back — so the two inductions below need more fuel than the default 2. Scoped, per the
   README's note on `--ext context_pruning`: never in `check.ps1`. *)
#push-options "--fuel 6 --ifuel 3 --z3rlimit 60"

(* Enough budget: the parse accepts and leaves EXACTLY the suffix, at every depth. *)
[@@ noextract_to "FSharp"]
let rec nest_accepts
  (float_read: list ch -> freadv) (cap: string) (tol: bool) (b k: list unit) (rest: list ch)
  : Lemma (requires llen b >= llen k)
          (ensures (let r = parse_value float_read cap tol b (nest_arr_at k rest) in
                    POk? r /\ POk?.rest r == rest))
          (decreases k)
  =
  match k with
  | [] -> ()
  | _ :: kt ->
    (match b with
     | _ :: bt -> nest_accepts float_read cap tol bt kt (CRBrack :: rest)
     | [] -> ())

(* Too little budget: the parse refuses, and refuses with THIS kind — not with some other failure
   that happens to fire first, because there is nothing else wrong with the document. *)
[@@ noextract_to "FSharp"]
let rec nest_refuses
  (float_read: list ch -> freadv) (cap: string) (tol: bool) (b k: list unit) (rest: list ch)
  : Lemma (requires llen b < llen k)
          (ensures (let r = parse_value float_read cap tol b (nest_arr_at k rest) in
                    PErr? r /\ PErr?.k r == MaxDepthExceeded))
          (decreases k)
  =
  match k with
  | [] -> ()
  | _ :: kt ->
    (match b with
     | [] -> ()
     | _ :: bt -> nest_refuses float_read cap tol bt kt (CRBrack :: rest))

#pop-options

(* THE BOUNDARY, EXACTLY. For every nesting depth and every cap, the document nested k deep is
   accepted iff the cap is at least k — and when it is not, the refusal is the named one. *)
let depth_bound_exact
  (float_read: list ch -> freadv) (cap: string) (pol: policy) (b k: list unit)
  : Lemma (ensures (let r = parse float_read cap pol b (nest_arr k) in
                    (llen b >= llen k ==> ROk? r) /\
                    (llen b < llen k ==> RErr? r /\ RErr?.k r == MaxDepthExceeded)))
  =
  let tol = EraseMemberNull? pol in
  if llen b >= llen k then nest_accepts float_read cap tol b k []
  else nest_refuses float_read cap tol b k []

(* … and the guard touches nothing that is not nested: a scalar parses at a cap of zero, where
   every container is refused. Both halves matter — the first says the bound is not a blanket
   refusal, the second says it is not vacuous. *)
let depth_bound_scalars_unaffected
  (float_read: list ch -> freadv) (cap: string) (pol: policy)
  : Lemma (requires float_read [CD0] == FFinite)
          (ensures ROk? (parse float_read cap pol [] [CD0]) /\
                   (let r = parse float_read cap pol [] [CLBrack; CRBrack] in
                    RErr? r /\ RErr?.k r == MaxDepthExceeded) /\
                   (let r = parse float_read cap pol [] [CLBrace; CRBrace] in
                    RErr? r /\ RErr?.k r == MaxDepthExceeded))
  = ()

(* ======================================================================================
   8. THEOREM — THE INT53 GUARD.

      What the phase's own task list calls `int53_guard_exact` is proved here in the strongest form
      that is TRUE, which is not exactness: see the finding in the header. Three results.
   ====================================================================================== *)

(* SOUNDNESS. Production's guard, on the raw digit string, never admits an integer whose VALUE is
   outside the int53 range. This is the half that matters: a guard that over-refuses costs a user
   an error message, a guard that under-refuses costs a corrupted identifier. *)
let int53_guard_sound (d: list ch)
  : Lemma (ensures int53_safe d ==> int53_safe_value d)
  = ()

(* CONSERVATISM, exhibited rather than described. `0009007199254740992` is exactly 2^53 — the
   largest int53-safe integer there is — and production refuses it, because its guard measures the
   string and the leading zeros make the string long. The comment justifying the lexical comparison
   says "JSON forbids leading zeros"; this parser's digit loop does not. *)
(* `0009007199254740992` — 2^53 with three leading zeros. *)
[@@ noextract_to "FSharp"]
let int53_conservative_witness: list ch =
  [CD0; CD0; CD0; CD9; CD0; CD0; CD7; CD1; CD9; CD9; CD2; CD5; CD4; CD7; CD4; CD0; CD9; CD9; CD2]

let int53_guard_conservative (_: unit)
  : Lemma (ensures int53_safe_value int53_conservative_witness /\
                   int53_safe int53_conservative_witness == false)
  =
  assert_norm (int53_safe_value int53_conservative_witness == true);
  assert_norm (int53_safe int53_conservative_witness == false)

(* The two limits' lengths — the `10` and the `16` production writes as literals, recovered here by
   computation from the limits themselves, which is the point of spelling them as digit lists. *)
[@@ noextract_to "FSharp"]
let limit_lengths (_: unit)
  : Lemma (ensures llen lim_i32_pos == 10 /\ llen lim_i32_neg == 10 /\ llen lim_i53 == 16)
  =
  assert_norm (llen lim_i32_pos == 10);
  assert_norm (llen lim_i32_neg == 10);
  assert_norm (llen lim_i53 == 16)

(* The Int32 range is inside the int53 range — so the branch that produces `JInt`, which runs no
   int53 test at all, needs none. *)
let int32_within_int53 (neg: bool) (d: list ch)
  : Lemma (ensures int32_fits neg d ==> int53_safe_value d)
  = limit_lengths ()

(* THE GUARD, STATED OVER THE PARSER. Every integer token the parser accepts — on either branch,
   `JInt` by the Int32 range or `JFloat` by the guard — denotes an int53-safe value. This is the
   claim worth having, and note WHERE it lives: the Int32 branch is not guarded and does not need
   to be, which is the correction the phase's brief needed. *)
let int53_guard_exact (float_read: list ch -> freadv) (tok: list ch)
  : Lemma (ensures (let digits = (match tok with CMinus :: t -> t | _ -> tok) in
                    POk? (classify_number float_read tok false) ==> int53_safe_value digits))
  =
  let digits = (match tok with CMinus :: t -> t | _ -> tok) in
  let neg = (match tok with CMinus :: _ -> true | _ -> false) in
  int32_within_int53 neg digits;
  int53_guard_sound digits

(* … and the refusal is classified: an integer token that is not int53-safe is refused, by name. *)
let int53_guard_refuses (float_read: list ch -> freadv) (tok: list ch)
  : Lemma (ensures (let digits = (match tok with CMinus :: t -> t | _ -> tok) in
                    let neg = (match tok with CMinus :: _ -> true | _ -> false) in
                    (not (int32_fits neg digits) /\ not (int53_safe digits)) ==>
                    (let r = classify_number float_read tok false in
                     PErr? r /\ PErr?.k r == MalformedNumber /\ PErr?.msg r == msg_int53 tok)))
  = ()

(* ======================================================================================
   9. THEOREM — THE ERROR CLASSIFICATION IS EXHAUSTIVE.

      Two halves, and the second is the one a closed union does not give you for free.

      CONFINED. Every signature in sections 4 and 5 carries the kinds its layer may raise, so the
      confinement is checked by the type-checker at every call site: `parse_string` cannot report a
      number failure, the descent cannot report `TrailingCharacters`, and `MaxDepthExceeded` cannot
      arise anywhere but at a `{` or a `[`. A lemma asserting this once would be weaker — it would
      hold of the model as written and say nothing about the next edit.

      REACHABLE. Every one of the twelve kinds is produced by some input. A classification whose
      cases are unreachable is closed but not exhaustive, and the difference is exactly what makes
      the differential's coverage mean anything.
   ====================================================================================== *)

(* Exactly one outcome, and the failure is always classified — the parser has no third answer and
   no unnamed refusal. *)
let error_kind_total (float_read: list ch -> freadv) (cap: string) (pol: policy)
                     (b: list unit) (s: list ch)
  : Lemma (ensures (let r = parse float_read cap pol b s in
                    ROk? r \/ (RErr? r /\ (let k = RErr?.k r in
                      k = UnexpectedChar || k = UnexpectedEndOfInput || k = ExpectedToken ||
                      k = UnterminatedString || k = UnterminatedEscape ||
                      k = TruncatedUnicodeEscape || k = BadEscape || k = BadHexDigit ||
                      k = MalformedNumber || k = NullNotRepresentable ||
                      k = MaxDepthExceeded || k = TrailingCharacters))))
  = ()

(* `TrailingCharacters` is the entry point's alone: the descent cannot produce it, which is the
   model's reading of production raising eleven kinds through `JsonParseError` and constructing the
   twelfth from the leftover-input test. *)
let trailing_is_the_entry_points_alone
  (float_read: list ch -> freadv) (cap: string) (tol: bool) (b: list unit) (s: list ch)
  : Lemma (ensures (let r = parse_value float_read cap tol b s in
                    PErr? r ==> PErr?.k r <> TrailingCharacters))
  = ()

(* `MaxDepthExceeded` is a container's alone. *)
let depth_error_only_at_a_container
  (float_read: list ch -> freadv) (cap: string) (tol: bool) (b: list unit) (s: list ch)
  : Lemma (ensures (let r = parse_value float_read cap tol b s in
                    (PErr? r /\ PErr?.k r == MaxDepthExceeded) ==> starts_container (PErr?.at r)))
  = ()

(* The witness parser: a cap of two, the strict policy, and a float reader that accepts every token
   it is shown. The witnesses below choose tokens whose classification does not depend on it. *)
[@@ noextract_to "FSharp"]
let fr_all_finite (_: list ch) : Tot freadv = FFinite

[@@ noextract_to "FSharp"]
let w_parse (s: list ch) : Tot jresult =
  parse fr_all_finite "2" RejectNull [(); ()] s

(* A 17-digit integer — outside the Int32 range and outside the int53 one, so it reaches
   `MalformedNumber` through the GUARD rather than through the float reader. A witness that leaned
   on the reader would be a witness about the parameter, not about the parser. *)
[@@ noextract_to "FSharp"]
let nines17: list ch =
  [CD9; CD9; CD9; CD9; CD9; CD9; CD9; CD9; CD9; CD9; CD9; CD9; CD9; CD9; CD9; CD9; CD9]

[@@ noextract_to "FSharp"]
let w_kind (s: list ch) (k: ekind) : Tot bool =
  match w_parse s with
  | RErr k' _ _ -> k' = k
  | ROk _ -> false

(* EVERY ONE OF THE TWELVE IS REACHABLE. Twelve inputs, one per case, each named by what it is:
   `%`, ``, `tru`, `"a`, `"\`, `"\u1`, `"\q`, `"\uzzzz`, `1e`, `null`, `[[[`, `0 0`. *)
let error_kind_exhaustive: unit -> Lemma (ensures
  w_kind [COther "%"] UnexpectedChar /\
  w_kind [] UnexpectedEndOfInput /\
  w_kind [CLt; CLr; CLu] ExpectedToken /\
  w_kind [CQuote; CLa] UnterminatedString /\
  w_kind [CQuote; CBackslash] UnterminatedEscape /\
  w_kind [CQuote; CBackslash; CLu; CD1] TruncatedUnicodeEscape /\
  w_kind [CQuote; CBackslash; COther "q"] BadEscape /\
  w_kind [CQuote; CBackslash; CLu; COther "z"; COther "z"; COther "z"; COther "z"] BadHexDigit /\
  w_kind nines17 MalformedNumber /\
  w_kind [CLn; CLu; CLl; CLl] NullNotRepresentable /\
  w_kind [CLBrack; CLBrack; CLBrack] MaxDepthExceeded /\
  w_kind [CD0; CSpace; CD0] TrailingCharacters)
  = fun () -> assert_norm (
      w_kind [COther "%"] UnexpectedChar /\
      w_kind [] UnexpectedEndOfInput /\
      w_kind [CLt; CLr; CLu] ExpectedToken /\
      w_kind [CQuote; CLa] UnterminatedString /\
      w_kind [CQuote; CBackslash] UnterminatedEscape /\
      w_kind [CQuote; CBackslash; CLu; CD1] TruncatedUnicodeEscape /\
      w_kind [CQuote; CBackslash; COther "q"] BadEscape /\
      w_kind [CQuote; CBackslash; CLu; COther "z"; COther "z"; COther "z"; COther "z"] BadHexDigit /\
      w_kind nines17 MalformedNumber /\
      w_kind [CLn; CLu; CLl; CLl] NullNotRepresentable /\
      w_kind [CLBrack; CLBrack; CLBrack] MaxDepthExceeded /\
      w_kind [CD0; CSpace; CD0] TrailingCharacters)

(* ======================================================================================
   10. The read policy at the parser, where Phase 135 said it lives.

       `WireDecode.fst` models the `NullPolicy` as a document-level normalisation and names, at
       level 3 of its ladder, the assumption that the parser's member-null absorption is equivalent
       to erasing member nulls from the strict tree. This model does not discharge that — relating
       two models is its own phase — but it does put the fork where production puts it, so the
       differential exercises both policies against the real parser, which is the evidence that
       assumption rests on.
   ====================================================================================== *)

(* The policies differ on a member null and agree everywhere else — stated here as the two facts
   the differential checks: the tolerant reader erases `{"a":null}` to `{}`, and the strict one
   refuses it by name. Both are about the ONE fork, which is the whole claim. *)
let member_null_erased (float_read: list ch -> freadv) (cap: string)
  : Lemma (ensures
      (let doc = [CLBrace; CQuote; CLa; CQuote; CColon; CLn; CLu; CLl; CLl; CRBrace] in
       parse float_read cap EraseMemberNull [(); ()] doc == ROk (JObj []) /\
       (let r = parse float_read cap RejectNull [(); ()] doc in
        RErr? r /\ RErr?.k r == NullNotRepresentable)))
  = ()

(* … and a null the tolerant policy declines to erase is still refused, in DIFFERENT words, which
   is the distinction Wire.fs makes deliberately ("since the remedy is different"). *)
let off_policy_null_still_refused (float_read: list ch -> freadv) (cap: string)
  : Lemma (ensures
      (let doc = [CLBrack; CLn; CLu; CLl; CLl; CRBrack] in
       (let r = parse float_read cap EraseMemberNull [(); ()] doc in
        RErr? r /\ RErr?.k r == NullNotRepresentable /\ RErr?.msg r == msg_null_tolerant) /\
       (let r = parse float_read cap RejectNull [(); ()] doc in
        RErr? r /\ RErr?.k r == NullNotRepresentable /\ RErr?.msg r == msg_null_strict)))
  = ()
