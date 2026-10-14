(*
   OpStreamLoad — an F* model of Fuaran.Core's op-stream readers: the one JSONL line scanner and
   the stream loads built on it, with totality, locating faults and the write-then-load round trip
   proved (fuaran-core Phase 454).

   WHAT IS MODELLED, clause for clause, over `JsonParse.fst`'s alphabet and value spellings (`ch`,
   `och`: a character is what spells it, a decoded character is the spelling that produced it) and
   `Chain.fst`'s records (the linear walk `first_chain_break`, which the verified load composes):
     - `src/Fuaran.Core.OpStream/Jsonl.fs`, `OpStream.Jsonl` — the scanner: `skipString` and
       `decodeString` as ONE pass (`string_tail`: the decode is total on what the skip accepted, so
       the pass that does both answers the same fault at the same position), `isLiteral`,
       `skipValue` with its container walk, `checkRaw`, `membersOf` with its first-wins member
       list, `parseLine`, `topFields`, `rawSpan`, `unquote`, `lineNumber`, `lineText`, `refuse`,
       `tryRawField`, `rawField`, `stringField`, `intField`, `stringsField`, `actorField`,
       `scanRecords` with the `\r\n` normalisation and the blank-line skip.
     - the loads over it: `recordLine` / `toJsonl` / `tryToJsonl` (`checkedLines`), `recordOf`,
       `isSnapshotLine`, `scanJsonlWithSnapshots`, the carrying witness `carry`, `settle`,
       `readCarried`, `fromJsonlWithSnapshots`, `fromJsonl`, `fromJsonlLegacyActor` and
       `fromJsonlVerified` (Phase 416's typed `StreamLoadFault`, `Unreadable` / `Broken` /
       `Undecodable`, the three a load of one text answers).
     - `src/Fuaran.Core.OpStream/Actor.fs` — `Actor.encode` (`JsonString.quote`'s three escape
       classes: `"`, `\` and a control character as `\u00xx`), `Actor.validate`, `ofLegacyString`.

   WHAT IS DROPPED, AND WHY — the model's boundary, recorded as the `op-stream-load-model-bridge`
   row in `../proofs.json`:
     - The code unit a `\uXXXX` denotes and the character a short escape spells, exactly as
       `JsonParse.fst` drops them: a decoded string is a `list och`, so two spellings of one
       character are two strings here where production reads one (a key spelled `"seq"` is
       not `seq` in the model). The differential's generators spell keys plainly; the host renders
       a decoded string for the hash (`ren`, a parameter of the verified load) as the differential
       bridge renders it.
     - Which characters `String.Trim` strips. The blank-line test takes a host predicate `ws` for
       the characters the alphabet carries as `COther`; the four ASCII whitespace characters are
       blank by the model's own `is_ws`, and no other constructor is.
     - The sequence numeral's range. `intField` reads at most ten digits into an unbounded `int`
       and refuses outside Int32, as production does; the chain walk (`Chain.fst`) counts in a
       unary `pos`, so a sequence number OUTSIDE the stream's indices — negative, or at or past
       its length, which the scan admits and the walk can never match — is carried AT the
       stream's length (`pos_of_seq`), where no walk index reaches it. The walk's verdict agrees;
       the `got` text of that one break does not.
     - The re-encoding of a LOADED actor for the chain walk. `JsonString.quote` spells a decoded
       string by its code units (a control as `\u00xx`, `\u0041` back as `A`), which the model
       does not compute, so the walk's actor text is `Actor.encodeWith` over the host's own quote
       (`qh`) of the host's rendering (`ren`) — the model owns the shape, the host the escaping,
       exactly as it owns the hash. The model's own writer (`encode_actor`, `quote_str`) spells
       the decoded strings a host hands it, which are canonical (`wf_str`), and is what the
       round trip reads back.
     - The lane (`UndecodableOp.Lane` is `None` on every load of one text) and the two fault
       cases a load of one text never answers (`DuplicateLane`, `Collision`,
       `UnreadableCheckpoint`).
     - Production's `ResizeArray` accumulators, its `Replace("\r\n", "\n").Split('\n')` and its
       `HashSet` first-wins pass are written as the direct recursions they equal.

   WHAT IS PROVED — the three theorems Phase 454 states:
     1. TOTALITY (section 9). Every scanner and load is a `Tot` function: no exception path and no
        divergence, every walk terminating on the suffix it consumes. Production raises
        `JsonlScanFault` deep in the scan and catches it at `parseLine` / `checkRaw` / `unquote` /
        `stringsField` / `actorField`; the model has no exception, so every `fail` is a returned
        `SErr` and the boundaries that catch it are the ones that turn it into a `fault`. That
        production raises nothing on the generated inputs is the differential's.
     2. FAULTS LOCATE (section 10). A fault `scanRecords` answers names a line that exists in the
        input — one the scan did not skip as blank — and is the fault that line's parse or decode
        answered (`scan_fault_locates`); its position lies within that line (`parse_fault_in_line`,
        for the scanner's own faults). Every `Undecodable` site of a load names a line that exists,
        whose record the witness refused with the site's reason, by the hash the line carries
        (`site_names_its_op`). No fault is produced for a line the writer wrote
        (`record_line_parses`, which theorem 3 spends).
     3. ROUND TRIP (section 11). Loading what `tryToJsonl` wrote yields the records that were
        written — every record, in order, with its chain fields — so `verify` over the loaded
        stream is `verify` over the original: `fromJsonlWithSnapshots` answers them with no
        snapshot, `fromJsonl` answers them, and `fromJsonlVerified` answers them or the break the
        chain walk reports of the ORIGINAL records (`round_trip`, `verified_round_trip`). Under
        two premises a domain owes (named in `../proofs.json`): its witness reads back what it
        writes (`witness_rt`), and its records are well formed (`records_wf`: an actor that names
        somebody, strings a writer can spell, a sequence in Int32 — which `OpStream.append` keeps).

   HOW TO READ A POSITION. Production's positions are indices into the line; the model's scanner
   walks suffixes, and a fault carries the suffix it refused AT (`SErr why at`), so the position
   is `len line - len at` — recovered where production's `at` adds its offset (`pos_in`). An
   unterminated string or container is refused at its OPENING character, which production spells
   `fail start`; the model carries that suffix as `start`.
*)
module OpStreamLoad

open JsonParse
module C = Chain

(* ======================================================================================
   0. Results and lists — self-contained so the extraction needs only `Prims` and the two
      models it opens (README, finding 2).
   ====================================================================================== *)

(* F#: `Result<'T, 'E>`. *)
type res (a: Type) (e: Type) =
  | Ok    : v:a -> res a e
  | Error : err:e -> res a e

(* F#: `String.Length` / `List.length`, EXTRACTED — positions are computed from it. `llen` is
   JsonParse's proof-only measure; the two agree (`len_llen`). *)
let rec len (#a: Type) (l: list a) : Tot nat (decreases l) =
  match l with
  | [] -> 0
  | _ :: t -> 1 + len t

[@@ noextract_to "FSharp"]
let rec len_llen (#a: Type) (l: list a) : Lemma (ensures len l == llen l) [SMTPat (len l)] =
  match l with
  | [] -> ()
  | _ :: t -> len_llen t

(* Truncated subtraction: a position is never negative. *)
let sub (a b: nat) : Tot nat = if b <= a then a - b else 0

(* The index of the suffix `s` within `whole` — `input.Length - llen at`, as JsonParse recovers
   `Position`. *)
let pos_in (whole s: list ch) : Tot nat = sub (len whole) (len s)

(* F#: `s.Substring(i, n)` — the first `n` characters. *)
let rec take_n (n: nat) (s: list ch) : Tot (list ch) (decreases s) =
  match s with
  | [] -> []
  | c :: t -> if n = 0 then [] else c :: take_n (n - 1) t

let rec all (#a: Type) (p: a -> bool) (l: list a) : Tot bool (decreases l) =
  match l with
  | [] -> true
  | x :: t -> p x && all p t

let rec map (#a #b: Type) (f: a -> b) (l: list a) : Tot (list b) (decreases l) =
  match l with
  | [] -> []
  | x :: t -> f x :: map f t

(* F#: `String.concat sep` over a list of texts. *)
let rec join_sep (sep: ch) (ls: list (list ch)) : Tot (list ch) (decreases ls) =
  match ls with
  | [] -> []
  | [l] -> l
  | l :: rest -> app l (sep :: join_sep sep rest)

(* `map OLit` — the decoded form of a key spelled plainly. *)
let rec olit (k: list ch) : Tot (list och) (decreases k) =
  match k with
  | [] -> []
  | c :: t -> OLit c :: olit t

(* ======================================================================================
   1. The faults (F#: `JsonlFaultReason`, `JsonlFault`, `JsonlWriteFaultReason`,
      `JsonlWriteFault`, `UndecodableOp`, `StreamLoadFault`).
   ====================================================================================== *)

(* F#: `JsonlFaultReason`, case for case. A field name is the KEY as spelled on the line, so
   `actor.id` is the key with a `.` and the member's key after it. The three `Refused` forms are
   one production case (`Refused of reason: string`) whose text the model does not render: a
   witness refusal carries the witness's string, the two actor refusals are rendered by the host
   as production renders them (`"the actor carries no kind"`, `unknown actor kind "<kind>"`). *)
type reason =
  | NotAnObject
  | Truncated
  | ExpectedKey
  | ExpectedColon
  | MissingValue
  | ExpectedCommaOrBrace
  | TrailingContent
  | UnterminatedString
  | UnterminatedContainer
  | InvalidEscape : escape:list ch -> reason
  | InvalidLiteral : token:list ch -> reason
  | ExpectedString : field:list ch -> reason
  | ExpectedInteger : field:list ch -> reason
  | ExpectedStringArray : field:list ch -> reason
  | MissingField : field:list ch -> reason
  | SnapshotNotAtHead
  | Refused : why:string -> reason
  | RefusedNoKind
  | RefusedKind : kind:list och -> reason
  | ActorInvalid : field:list ch -> reason

(* F#: `JsonlFault` — the 1-based line, the position within it, the reason. *)
type fault = { fline: nat; fpos: nat; freason: reason }

let fault_at (line: nat) (pos: nat) (r: reason) : Tot fault =
  { fline = line; fpos = pos; freason = r }

(* F#: `JsonlWriteFaultReason` / `JsonlWriteFault` (Phase 301). *)
type wreason =
  | MultiLine : position:nat -> wreason
  | NotTrimStable
  | Unreadable : r:reason -> wreason

type wfault = { wline: nat; wmember: list ch; wreason: wreason }

(* F#: `UndecodableOp` without its lane (`None` on every load of one text). *)
type usite = { uline: nat; unode: list och; ureason: string }

(* F#: `StreamLoadFault<'Break>` — the three cases a load of one text answers. *)
type lfault (brk: Type) =
  | LUnreadable : f:fault -> lfault brk
  | LBroken : b:brk -> lfault brk
  | LUndecodable : sites:list usite -> lfault brk

(* The scanner's internal result (F#: the value returned, or `JsonlScanFault(pos, reason)`
   raised): the value and the remaining input, or the reason and the suffix refused at. *)
type sres (a: Type) =
  | SOk : v:a -> rest:list ch -> sres a
  | SErr : why:reason -> at:list ch -> sres a

(* ======================================================================================
   2. The string token (F#: `skipString` and `decodeString`, Phase 369 — one pass here).
   ====================================================================================== *)

(* Four hex digits and what follows them, or nothing. F#: `unicodeAt`'s accept test. *)
type hex4r =
  | H4 : a:ch -> b:ch -> c:ch -> d:ch -> rest:list ch -> hex4r
  | NoHex4 : hex4r

let hex4 (u: list ch) : Tot (o: hex4r { H4? o ==> llen (H4?.rest o) < llen u }) =
  match u with
  | a :: b :: c :: d :: r ->
    if is_hex a && is_hex b && is_hex c && is_hex d then H4 a b c d r else NoHex4
  | _ -> NoHex4

(* F#: the body of `skipString`'s loop from just past the opening quote at `start`, with
   `decodeString`'s decode folded in — each escape-free character is `OLit`, each short escape
   `OEsc` by its letter, each `\uXXXX` `OUni` by its digits; a high surrogate escape must be
   followed at once by a low one (both carried, in order), a lone low one is refused. Every fault
   is at production's own index: the opening quote for an unterminated token, the backslash for
   an escape. *)
let rec string_tail (start: list ch) (acc: list och) (s: list ch)
  : Tot (r: sres (list och) { SOk? r ==> llen (SOk?.rest r) < llen s }) (decreases (llen s)) =
  match s with
  | [] -> SErr UnterminatedString start
  | CQuote :: t -> SOk (rev acc) t
  | CBackslash :: t ->
    (match t with
     | [] -> SErr UnterminatedString start
     | CLu :: u ->
       (match hex4 u with
        | NoHex4 -> SErr (InvalidEscape (take_n 6 s)) s
        | H4 a b c d r ->
          (match uni_class a b with
           | SNone -> string_tail start (OUni a b c d :: acc) r
           | SLow -> SErr (InvalidEscape (take_n 6 s)) s
           | SHigh ->
             (match r with
              | CBackslash :: CLu :: u2 ->
                (match hex4 u2 with
                 | H4 a2 b2 c2 d2 r2 ->
                   if uni_class a2 b2 = SLow
                   then string_tail start (OUni a2 b2 c2 d2 :: OUni a b c d :: acc) r2
                   else SErr (InvalidEscape (take_n 12 s)) s
                 | NoHex4 -> SErr (InvalidEscape (take_n 12 s)) s)
              | _ -> SErr (InvalidEscape (take_n 12 s)) s)))
     | e :: u ->
       if is_short_escape e then string_tail start (OEsc e :: acc) u
       else SErr (InvalidEscape [CBackslash; e]) s)
  | c :: t -> string_tail start (OLit c :: acc) t

(* ======================================================================================
   3. A value (F#: `isLiteral`, `skipValue`, `checkRaw`).
   ====================================================================================== *)

let lit_true: list ch = [CLt; CLr; CLu; CLe]
let lit_false: list ch = [CLf; CLa; CLl; CLs; CLe]
let lit_null: list ch = [CLn; CLu; CLl; CLl]

(* F#: `isLiteral` — the three literals, or the JSON number grammar (`JsonParse.is_json_number`
   is `Json.isJsonNumber`, the same grammar in production's own three steps). *)
let is_literal (t: list ch) : Tot bool =
  t = lit_true || t = lit_false || t = lit_null || is_json_number t

(* F#: the `while` that cuts a bare token: at `,`, `}`, `]` or whitespace. *)
let is_terminator (c: ch) : Tot bool = c = CComma || c = CRBrace || c = CRBrack || is_ws c

let rec bare_token (s: list ch)
  : Tot (p: (list ch & list ch) { llen (snd p) <= llen s }) (decreases s) =
  match s with
  | [] -> ([], [])
  | c :: t ->
    if is_terminator c then ([], s)
    else (let (tok, r) = bare_token t in (c :: tok, r))

(* F#: the `while depth > 0` loop of `skipValue`, from just past the opening bracket at `start`.
   A string inside is skipped by `string_tail`, whose fault is the one reported. *)
let rec skip_container (start: list ch) (depth: pos) (s: list ch)
  : Tot (r: sres unit { SOk? r ==> llen (SOk?.rest r) < llen s }) (decreases (llen s)) =
  match s with
  | [] -> SErr UnterminatedContainer start
  | CQuote :: t ->
    (match string_tail s [] t with
     | SErr why at -> SErr why at
     | SOk _ r -> skip_container start depth r)
  | CLBrace :: t -> skip_container start (depth + 1) t
  | CLBrack :: t -> skip_container start (depth + 1) t
  | CRBrace :: t -> if depth = 1 then SOk () t else skip_container start (depth - 1) t
  | CRBrack :: t -> if depth = 1 then SOk () t else skip_container start (depth - 1) t
  | _ :: t -> skip_container start depth t

(* F#: `skipValue` at `start` (no leading whitespace). *)
let skip_value (s: list ch)
  : Tot (r: sres unit { SOk? r ==> llen (SOk?.rest r) <= llen s }) =
  match s with
  | [] -> SErr Truncated []
  | CQuote :: t ->
    (match string_tail s [] t with
     | SErr why at -> SErr why at
     | SOk _ r -> SOk () r)
  | CLBrace :: t -> skip_container s 1 t
  | CLBrack :: t -> skip_container s 1 t
  | CComma :: _ -> SErr MissingValue s
  | CRBrace :: _ -> SErr MissingValue s
  | CRBrack :: _ -> SErr MissingValue s
  | _ ->
    let (tok, r) = bare_token s in
    if is_literal tok then SOk () r else SErr (InvalidLiteral tok) s

(* F#: `checkRaw`'s first `\n` / `\r`. *)
let rec first_break (k: nat) (s: list ch) : Tot (option nat) (decreases s) =
  match s with
  | [] -> None
  | c :: t -> if c = CNewline || c = CReturn then Some k else first_break (k + 1) t

(* F#: `Jsonl.checkRaw` (Phase 301). *)
let check_raw (raw: list ch) : Tot (res unit wreason) =
  match first_break 0 raw with
  | Some k -> Error (MultiLine k)
  | None ->
    (match raw with
     | c :: _ ->
       if is_ws c then Error NotTrimStable
       else
         (match skip_value raw with
          | SErr why _ -> Error (Unreadable why)
          | SOk _ [] -> Ok ()
          | SOk _ rest -> if all is_ws rest then Error NotTrimStable else Error (Unreadable TrailingContent))
     | [] ->
       (match skip_value raw with
        | SErr why _ -> Error (Unreadable why)
        | SOk _ _ -> Ok ()))

(* ======================================================================================
   4. The members of a line (F#: `membersOf`, `parseLine`, `topFields`, `rawSpan`).
   ====================================================================================== *)

(* One top-level member: its decoded key, its raw span and the span's position. *)
type member = { mkey: list och; mspan: list ch; mpos: nat }

type jline = { lnum: nat; ltext: list ch; lfields: list member }

let rec mem_key (k: list och) (seen: list (list och)) : Tot bool (decreases seen) =
  match seen with
  | [] -> false
  | x :: t -> k = x || mem_key k t

(* F#: the `HashSet` pass — first wins on a repeated key (Phase 45). *)
let rec first_wins (seen: list (list och)) (ms: list member) : Tot (list member) (decreases ms) =
  match ms with
  | [] -> []
  | m :: t -> if mem_key m.mkey seen then first_wins seen t else m :: first_wins (m.mkey :: seen) t

(* F#: the `while go` loop of `membersOf`, at `off` within the line, over the suffix `s` of
   `whole`. A fault's position is `off + pos_in whole at`. *)
let rec members_loop (off: nat) (whole: list ch) (acc: list member) (s: list ch)
  : Tot (sres (list member)) (decreases (llen s)) =
  let w = skip_ws s in
  match w with
  | [] -> SErr Truncated []
  | CQuote :: t ->
    (match string_tail w [] t with
     | SErr why at -> SErr why at
     | SOk key r1 ->
       let w1 = skip_ws r1 in
       (match w1 with
        | [] -> SErr Truncated []
        | CColon :: t1 ->
          let w2 = skip_ws t1 in
          (match w2 with
           | [] -> SErr Truncated []
           | _ ->
             (match skip_value w2 with
              | SErr why at -> SErr why at
              | SOk _ r2 ->
                let m = { mkey = key; mspan = take_n (sub (len w2) (len r2)) w2; mpos = off + pos_in whole w2 } in
                let w3 = skip_ws r2 in
                (match w3 with
                 | [] -> SErr Truncated []
                 | CComma :: t3 -> members_loop off whole (m :: acc) t3
                 | CRBrace :: t3 -> SOk (rev (m :: acc)) t3
                 | _ -> SErr ExpectedCommaOrBrace w3)))
        | _ -> SErr ExpectedColon w1))
  | _ -> SErr ExpectedKey w

(* F#: the tail of `membersOf` — no content after the closing brace, then first wins. *)
let members_end (off: nat) (whole: list ch) (ms: list member) (r: list ch)
  : Tot (res (list member) (nat & reason)) =
  match skip_ws r with
  | [] -> Ok (first_wins [] ms)
  | w -> Error (off + pos_in whole w, TrailingContent)

(* F#: `membersOf offset s`. *)
let members_of (off: nat) (s: list ch) : Tot (res (list member) (nat & reason)) =
  let w = skip_ws s in
  match w with
  | CLBrace :: t ->
    let w1 = skip_ws t in
    (match w1 with
     | [] -> Error (off + pos_in s [], Truncated)
     | CRBrace :: t1 -> members_end off s [] t1
     | _ ->
       (match members_loop off s [] w1 with
        | SErr why at -> Error (off + pos_in s at, why)
        | SOk ms r -> members_end off s ms r))
  | _ -> Error (off + pos_in s w, NotAnObject)

(* F#: `parseLine number text`. *)
let parse_line (number: nat) (text: list ch) : Tot (res jline fault) =
  match members_of 0 text with
  | Ok ms -> Ok { lnum = number; ltext = text; lfields = ms }
  | Error (p, why) -> Error (fault_at number p why)

(* F#: `topFields`. *)
let top_fields (line: list ch) : Tot (res (list (list och & list ch)) fault) =
  match parse_line 1 line with
  | Ok l -> Ok (map (fun m -> (m.mkey, m.mspan)) l.lfields)
  | Error f -> Error f

(* F#: `List.tryPick` on the key. *)
let rec member_of (key: list ch) (ms: list member) : Tot (option (list ch & nat)) (decreases ms) =
  match ms with
  | [] -> None
  | m :: t -> if m.mkey = olit key then Some (m.mspan, m.mpos) else member_of key t

(* F#: `rawSpan`. *)
let raw_span (field: list ch) (line: list ch) : Tot (res (option (list ch)) fault) =
  match parse_line 1 line with
  | Ok l -> (match member_of field l.lfields with Some (v, _) -> Ok (Some v) | None -> Ok None)
  | Error f -> Error f

(* ======================================================================================
   5. The field readers (F#: `unquote`, `lineNumber`, `lineText`, `refuse`, `tryRawField`,
      `rawField`, `stringField`, `intField`, `stringsField`, `actorField`).
   ====================================================================================== *)

(* F#: `unquote` — one complete string token and nothing else. *)
let unquote (raw: list ch) : Tot (res (list och) reason) =
  match raw with
  | CQuote :: t ->
    (match t with
     | [] -> Error (ExpectedString [])
     | _ ->
       (match string_tail raw [] t with
        | SOk v [] -> Ok v
        | SOk _ _ -> Error (ExpectedString [])
        | SErr why _ -> Error why))
  | _ -> Error (ExpectedString [])

let line_number (l: jline) : Tot nat = l.lnum
let line_text (l: jline) : Tot (list ch) = l.ltext

let refuse (l: jline) (why: string) : Tot fault = fault_at l.lnum 0 (Refused why)

let try_raw_field (key: list ch) (l: jline) : Tot (option (list ch)) =
  match member_of key l.lfields with
  | Some (v, _) -> Some v
  | None -> None

let raw_field (key: list ch) (l: jline) : Tot (res (list ch) fault) =
  match member_of key l.lfields with
  | Some (v, _) -> Ok v
  | None -> Error (fault_at l.lnum 0 (MissingField key))

(* F#: `stringField` — a string fault is reported at the MEMBER's position: `unquote` drops the
   scanner's own, and `stringField` answers `p`. *)
let string_field (key: list ch) (l: jline) : Tot (res (list och) fault) =
  match member_of key l.lfields with
  | None -> Error (fault_at l.lnum 0 (MissingField key))
  | Some (v, p) ->
    (match unquote v with
     | Ok s -> Ok s
     | Error (ExpectedString _) -> Error (fault_at l.lnum p (ExpectedString key))
     | Error why -> Error (fault_at l.lnum p why))

let digit_val (c: ch) : Tot nat =
  match c with
  | CD0 -> 0 | CD1 -> 1 | CD2 -> 2 | CD3 -> 3 | CD4 -> 4
  | CD5 -> 5 | CD6 -> 6 | CD7 -> 7 | CD8 -> 8 | CD9 -> 9
  | _ -> 0

(* F#: the `Seq.fold` over the digits (Phase 306: read from the digits, never a host reader). *)
let rec fold_digits (acc: nat) (d: list ch) : Tot nat (decreases d) =
  match d with
  | [] -> acc
  | c :: t -> fold_digits (acc * 10 + digit_val c) t

let int32_max: nat = 2147483647
let int32_min_mag: nat = 2147483648

(* F#: `intField`'s grammar — a sign, then at most ten digits with no leading zero but `0`. *)
let read_int (v: list ch) : Tot (option int) =
  let negative = (match v with CMinus :: _ -> true | _ -> false) in
  let digits = (match v with CMinus :: t -> t | _ -> v) in
  let grammatical =
    Cons? digits && len digits <= 10 && all is_digit digits &&
    (digits = [CD0] || (match digits with CD0 :: _ -> false | _ -> true)) in
  if not grammatical then None
  else
    let magnitude = fold_digits 0 digits in
    if negative
    then (if magnitude <= int32_min_mag then Some (0 - magnitude) else None)
    else (if magnitude <= int32_max then Some magnitude else None)

let int_field (key: list ch) (l: jline) : Tot (res int fault) =
  match member_of key l.lfields with
  | None -> Error (fault_at l.lnum 0 (MissingField key))
  | Some (v, p) ->
    (match read_int v with
     | Some i -> Ok i
     | None -> Error (fault_at l.lnum p (ExpectedInteger key)))

(* F#: `stringsField`'s `skipWs ()`, bounded at `n - 1` — the final `]` is never skipped. *)
let rec skip_ws1 (s: list ch) : Tot (r: list ch { llen r <= llen s }) (decreases s) =
  match s with
  | c :: t -> (match t with [] -> s | _ -> if is_ws c then skip_ws1 t else s)
  | [] -> s

type sitems =
  | Items : xs:list (list och) -> sitems
  | Bad : sitems
  | ScanFault : at:nat -> why:reason -> sitems

let rec ends_with_rbrack (s: list ch) : Tot bool (decreases s) =
  match s with
  | [] -> false
  | [c] -> c = CRBrack
  | _ :: t -> ends_with_rbrack t

(* F#: the `while ok && i < n - 1` loop of `stringsField`, from just past the `[` at `p`, over
   the suffix `s` of the span `v` (whose last character is `]`). *)
let rec items_loop (p: nat) (v: list ch) (expect_item: bool) (acc: list (list och)) (s: list ch)
  : Tot sitems (decreases (llen s)) =
  match s with
  | _ :: _ :: _ ->
    let w = skip_ws1 s in
    (match w with
     | CQuote :: t ->
       if expect_item then
         (match string_tail w [] t with
          | SErr why at -> ScanFault (p + pos_in v at) why
          | SOk item r -> items_loop p v false (item :: acc) (skip_ws1 r))
       else Bad
     | CComma :: t -> if expect_item then Bad else items_loop p v true acc (skip_ws1 t)
     | _ -> Bad)
  | _ -> if expect_item then Bad else Items (rev acc)

let strings_field (key: list ch) (l: jline) : Tot (res (list (list och)) fault) =
  match member_of key l.lfields with
  | None -> Error (fault_at l.lnum 0 (MissingField key))
  | Some (v, p) ->
    let bad: res (list (list och)) fault = Error (fault_at l.lnum p (ExpectedStringArray key)) in
    (match v with
     | CLBrack :: t ->
       if ends_with_rbrack t then
         (match skip_ws1 t with
          | [CRBrack] -> Ok []
          | w ->
            (match items_loop p v true [] w with
             | Items xs -> Ok xs
             | Bad -> bad
             | ScanFault at why -> Error (fault_at l.lnum at why)))
       else bad
     | _ -> bad)

(* ---- the actor (F#: `Actor`, `Actor.encode`, `Actor.validate`, `Jsonl.actorField`) ---- *)

type actor =
  | AHuman : id:list och -> actor
  | AAgent : model:list och -> version:list och -> id:list och -> actor

(* The keys and literals, spelled as the alphabet spells them — a letter the parser does not
   distinguish is `COther` of itself (JsonParse, section 1). *)
let k_seq: list ch = [CLs; CLe; COther "q"]
let k_actor: list ch = [CLa; CLc; CLt; COther "o"; CLr]
let k_op: list ch = [COther "o"; COther "p"]
let k_prev_hash: list ch = [COther "p"; CLr; CLe; COther "v"; COther "H"; CLa; CLs; COther "h"]
let k_hash: list ch = [COther "h"; CLa; CLs; COther "h"]
let k_snapshot: list ch = [CLs; CLn; CLa; COther "p"; CLs; COther "h"; COther "o"; CLt]
let k_kind: list ch = [COther "k"; COther "i"; CLn; CLd]
let k_id: list ch = [COther "i"; CLd]
let k_model: list ch = [COther "m"; COther "o"; CLd; CLe; CLl]
let k_version: list ch = [COther "v"; CLe; CLr; CLs; COther "i"; COther "o"; CLn]
let lit_human: list ch = [COther "h"; CLu; COther "m"; CLa; CLn]
let lit_agent: list ch = [CLa; COther "g"; CLe; CLn; CLt]

(* `key.member` — the field a nested refusal names. *)
let dotted (key: list ch) (m: list ch) : Tot (list ch) = app key (CDot :: m)

(* F#: `Actor.validate` — the id names somebody. *)
let actor_id (a: actor) : Tot (list och) =
  match a with
  | AHuman id -> id
  | AAgent _ _ id -> id

let actor_valid (a: actor) : Tot bool = Cons? (actor_id a)

(* F#: `actorField`'s `str` — a string member of the actor object, its refusal re-keyed. *)
let actor_str (key: list ch) (m: list ch) (inner: jline) : Tot (res (list och) fault) =
  match string_field m inner with
  | Ok s -> Ok s
  | Error f ->
    (match f.freason with
     | MissingField x -> Error (fault_at f.fline f.fpos (MissingField (dotted key x)))
     | ExpectedString x -> Error (fault_at f.fline f.fpos (ExpectedString (dotted key x)))
     | _ -> Error f)

let named (key: list ch) (l: jline) (p: nat) (a: actor) : Tot (res actor fault) =
  if actor_valid a then Ok a else Error (fault_at l.lnum p (ActorInvalid key))

(* F#: `actorField` — the member's span scanned as an object at its own position, `kind` read
   first, then the case's members in declaration order. *)
let actor_field (key: list ch) (l: jline) : Tot (res actor fault) =
  match member_of key l.lfields with
  | None -> Error (fault_at l.lnum 0 (MissingField key))
  | Some (v, p) ->
    (match members_of p v with
     | Error (q, why) -> Error (fault_at l.lnum q why)
     | Ok ms ->
       let inner = { lnum = l.lnum; ltext = v; lfields = ms } in
       (match try_raw_field k_kind inner with
        | None -> Error (fault_at l.lnum 0 RefusedNoKind)
        | Some _ ->
          (match actor_str key k_kind inner with
           | Error f -> Error f
           | Ok kind ->
             if kind = olit lit_human then
               (match actor_str key k_id inner with
                | Error f -> Error f
                | Ok id -> named key l p (AHuman id))
             else if kind = olit lit_agent then
               (match actor_str key k_model inner with
                | Error f -> Error f
                | Ok model ->
                  (match actor_str key k_version inner with
                   | Error f -> Error f
                   | Ok version ->
                     (match actor_str key k_id inner with
                      | Error f -> Error f
                      | Ok id -> named key l p (AAgent model version id))))
             else Error (fault_at l.lnum 0 (RefusedKind kind)))))

(* ======================================================================================
   6. Lines (F#: `scanRecords` — `Replace("\r\n", "\n").Split('\n')`, the 1-based count over
      EVERY line, the blank-line skip, the first fault).
   ====================================================================================== *)

let rec split_lines (cur: list ch) (s: list ch) : Tot (list (list ch)) (decreases s) =
  match s with
  | [] -> [rev cur]
  | CReturn :: CNewline :: t -> rev cur :: split_lines [] t
  | CNewline :: t -> rev cur :: split_lines [] t
  | c :: t -> split_lines (c :: cur) t

let split (text: list ch) : Tot (list (list ch)) = split_lines [] text

(* F#: `text.Trim() <> ""` — `String.Trim` strips every Unicode whitespace character: the four
   the alphabet names, and whichever `COther` the host says (`ws`, a parameter). *)
let is_blank (ws: ch -> bool) (c: ch) : Tot bool =
  match c with
  | COther _ -> is_ws c || ws c
  | _ -> is_ws c

let blank_line (ws: ch -> bool) (l: list ch) : Tot bool = all (is_blank ws) l

let rec scan_from (#a: Type) (ws: ch -> bool) (dec: jline -> res a fault) (k: nat)
  (lines: list (list ch)) (acc: list a) : Tot (res (list a) fault) (decreases lines) =
  match lines with
  | [] -> Ok (rev acc)
  | l :: rest ->
    if blank_line ws l then scan_from ws dec (k + 1) rest acc
    else
      (match parse_line (k + 1) l with
       | Error f -> Error f
       | Ok line ->
         (match dec line with
          | Error f -> Error f
          | Ok v -> scan_from ws dec (k + 1) rest (v :: acc)))

(* F#: `scanRecords decode text`. *)
let scan_records (#a: Type) (ws: ch -> bool) (dec: jline -> res a fault) (text: list ch)
  : Tot (res (list a) fault) =
  scan_from ws dec 0 (split text) []

(* ======================================================================================
   7. Records and the writer (F#: `OpRecord`, `StreamWitness`, `JsonString.quote`,
      `Actor.encode`, `recordLine`, `toJsonl`, `checkedLines`, `tryToJsonl`).
   ====================================================================================== *)

(* F#: `OpRecord<'Op>`. The actor is the typed value; the two hashes are decoded strings. *)
type lrec (op: Type) = { lseq: int; lactor: actor; lop: op; lprev: list och; lhash: list och }

(* F#: `StreamWitness<'Op, _, _>`'s two codec seams. `Apply` plays no part in a load. *)
noeq type witness (op: Type) = { enc: op -> list ch; dec: list ch -> res op string }

(* F#: `JsonString.quote`'s body on a DECODED string — a short escape re-spelled, a `\uXXXX`
   re-spelled, and a copied character copied through unless it is one of the three classes
   production escapes on the way out: `"` as `\"`, `\` as `\\`, and a control character as
   `\u00xx` — the three controls the alphabet names (tab, line feed, carriage return) are spelled
   here; one it carries as `COther` is copied raw, which the model's boundary records. A string
   the host hands the writer never carries one of these as `OLit` (`toOchs` spells them escaped,
   as `wf_str` requires); a string LOADED from a line can, and is re-encoded as production does. *)
let rec quote_body (s: list och) : Tot (list ch) (decreases s) =
  match s with
  | [] -> []
  | OLit CQuote :: t -> CBackslash :: CQuote :: quote_body t
  | OLit CBackslash :: t -> CBackslash :: CBackslash :: quote_body t
  | OLit CTab :: t -> CBackslash :: CLu :: CD0 :: CD0 :: CD0 :: CD9 :: quote_body t
  | OLit CNewline :: t -> CBackslash :: CLu :: CD0 :: CD0 :: CD0 :: CLa :: quote_body t
  | OLit CReturn :: t -> CBackslash :: CLu :: CD0 :: CD0 :: CD0 :: CLd :: quote_body t
  | OLit c :: t -> c :: quote_body t
  | OEsc e :: t -> CBackslash :: e :: quote_body t
  | OUni a b c d :: t -> CBackslash :: CLu :: a :: b :: c :: d :: quote_body t

let quote_str (s: list och) : Tot (list ch) = CQuote :: app (quote_body s) [CQuote]

(* `"key":value`. *)
let member_text (k: list ch) (v: list ch) : Tot (list ch) =
  CQuote :: app k (CQuote :: CColon :: v)

(* `{m1,m2,...}`. *)
let obj_text (ms: list (list ch)) : Tot (list ch) = CLBrace :: app (join_sep CComma ms) [CRBrace]

(* F#: `Actor.encode` — `kind` first, then the case's members in declaration order. *)
let encode_actor (a: actor) : Tot (list ch) =
  match a with
  | AHuman id -> obj_text [member_text k_kind (quote_str (olit lit_human)); member_text k_id (quote_str id)]
  | AAgent model version id ->
    obj_text [member_text k_kind (quote_str (olit lit_agent));
              member_text k_model (quote_str model);
              member_text k_version (quote_str version);
              member_text k_id (quote_str id)]

let digit_ch (d: nat{d < 10}) : Tot ch =
  match d with
  | 0 -> CD0 | 1 -> CD1 | 2 -> CD2 | 3 -> CD3 | 4 -> CD4
  | 5 -> CD5 | 6 -> CD6 | 7 -> CD7 | 8 -> CD8 | _ -> CD9

(* F#: `string (n: int)` — decimal digits, no leading zero but `0`. *)
let rec digits_of_nat (n: nat) : Tot (list ch) (decreases n) =
  if n < 10 then [digit_ch n] else app (digits_of_nat (n / 10)) [digit_ch (n % 10)]

let show_int (s: int) : Tot (list ch) =
  if s < 0 then CMinus :: digits_of_nat (0 - s) else digits_of_nat s

(* F#: `recordLine r opJson` — the five members, in the one order both writers emit. Stated over
   the record's parts so that a line is the same text whatever the op's type. *)
let line_of (seq: int) (a: actor) (op_json: list ch) (prev hash: list och) : Tot (list ch) =
  obj_text [member_text k_seq (show_int seq);
            member_text k_actor (encode_actor a);
            member_text k_op op_json;
            member_text k_prev_hash (quote_str prev);
            member_text k_hash (quote_str hash)]

let record_line (#op: Type) (r: lrec op) (op_json: list ch) : Tot (list ch) =
  line_of r.lseq r.lactor op_json r.lprev r.lhash

(* F#: `toJsonl` — the unchecked writer. *)
let to_jsonl (#op: Type) (w: witness op) (rs: list (lrec op)) : Tot (list ch) =
  join_sep CNewline (map (fun r -> record_line r (w.enc r.lop)) rs)

(* F#: `checkedLines "op"` over the records — every span checked before any line is built, the
   first refusal by its 1-based line. *)
let rec checked_lines (#op: Type) (w: witness op) (i: nat) (rs: list (lrec op))
  : Tot (res (list (list ch)) wfault) (decreases rs) =
  match rs with
  | [] -> Ok []
  | r :: rest ->
    let span = w.enc r.lop in
    (match check_raw span with
     | Error why -> Error { wline = i + 1; wmember = k_op; wreason = why }
     | Ok _ ->
       (match checked_lines w (i + 1) rest with
        | Error f -> Error f
        | Ok ls -> Ok (record_line r span :: ls)))

(* F#: `tryToJsonl`. *)
let try_to_jsonl (#op: Type) (w: witness op) (rs: list (lrec op)) : Tot (res (list ch) wfault) =
  match checked_lines w 0 rs with
  | Error f -> Error f
  | Ok ls -> Ok (join_sep CNewline ls)

(* ======================================================================================
   8. The loads (F#: `recordOf`, `isSnapshotLine`, `scanJsonlWithSnapshots`, `carry`, `settle`,
      `readCarried`, `fromJsonlWithSnapshots`, `fromJsonl`, `fromJsonlLegacyActor`,
      `fromJsonlVerified`).
   ====================================================================================== *)

(* F#: `recordOf` — the members in line order, the `op` span handed to the witness. *)
let record_of (#op: Type) (actor_of: jline -> res actor fault) (w: witness op) (l: jline)
  : Tot (res (lrec op) fault) =
  match int_field k_seq l with
  | Error f -> Error f
  | Ok seq ->
    (match actor_of l with
     | Error f -> Error f
     | Ok a ->
       (match raw_field k_op l with
        | Error f -> Error f
        | Ok raw ->
          (match w.dec raw with
           | Error why -> Error (refuse l why)
           | Ok o ->
             (match string_field k_prev_hash l with
              | Error f -> Error f
              | Ok prev ->
                (match string_field k_hash l with
                 | Error f -> Error f
                 | Ok hash -> Ok { lseq = seq; lactor = a; lop = o; lprev = prev; lhash = hash })))))

(* F#: `isSnapshotLine` — `"snapshot":true` and NO `op`. *)
let is_snapshot_line (l: jline) : Tot bool =
  None? (try_raw_field k_op l) && try_raw_field k_snapshot l = Some lit_true

(* What one line of a stream decodes to: a record with its line, or a snapshot line. *)
type item (op: Type) =
  | Rec : line:nat -> r:lrec op -> item op
  | Snap : l:jline -> item op

let line_item (#op: Type) (actor_of: jline -> res actor fault) (w: witness op) (l: jline)
  : Tot (res (item op) fault) =
  if is_snapshot_line l then Ok (Snap l)
  else
    (match record_of actor_of w l with
     | Ok r -> Ok (Rec l.lnum r)
     | Error f -> Error f)

(* F#: the `go` loop — one snapshot line, and only as the first line. *)
let rec settle_items (#op: Type) (first: bool) (recs: list (nat & lrec op)) (snaps: list (list ch))
  (items: list (item op)) : Tot (res (list (nat & lrec op) & list (list ch)) fault) (decreases items) =
  match items with
  | [] -> Ok (rev recs, rev snaps)
  | Rec n r :: rest -> settle_items false ((n, r) :: recs) snaps rest
  | Snap l :: rest ->
    if first then settle_items false recs [l.ltext] rest
    else Error (fault_at l.lnum 0 SnapshotNotAtHead)

(* F#: `scanJsonlWithSnapshots`. *)
let scan_with_snapshots (#op: Type) (ws: ch -> bool) (actor_of: jline -> res actor fault)
  (w: witness op) (text: list ch) : Tot (res (list (nat & lrec op) & list (list ch)) fault) =
  match scan_records ws (line_item actor_of w) text with
  | Error f -> Error f
  | Ok items -> settle_items true [] [] items

(* F#: `carry` (Phase 416) — the witness that never refuses: each op's stored text beside the
   real witness's answer, re-encoded by the real witness when it decoded and as its text when it
   did not. *)
type carried (op: Type) = list ch & res op string

let carry (#op: Type) (w: witness op) : Tot (witness (carried op)) =
  { enc = (fun (c: carried op) -> (match snd c with Ok o -> w.enc o | Error _ -> fst c));
    dec = (fun raw -> Ok (raw, w.dec raw)) }

let uncarry (#op: Type) (r: lrec (carried op)) (o: op) : Tot (lrec op) =
  { lseq = r.lseq; lactor = r.lactor; lop = o; lprev = r.lprev; lhash = r.lhash }

let rec decoded_of (#op: Type) (reads: list (nat & lrec (carried op)))
  : Tot (list (res (lrec op) usite)) (decreases reads) =
  match reads with
  | [] -> []
  | (line, r) :: rest ->
    (match snd r.lop with
     | Ok o -> Ok (uncarry r o)
     | Error why -> Error { uline = line; unode = r.lhash; ureason = why }) :: decoded_of rest

let rec sites_of (#op: Type) (d: list (res (lrec op) usite)) : Tot (list usite) (decreases d) =
  match d with
  | [] -> []
  | Error s :: t -> s :: sites_of t
  | Ok _ :: t -> sites_of t

let rec oks_of (#op: Type) (d: list (res (lrec op) usite)) : Tot (list (lrec op)) (decreases d) =
  match d with
  | [] -> []
  | Ok r :: t -> r :: oks_of t
  | Error _ :: t -> oks_of t

(* F#: `settle` — the records decoded, or EVERY record the witness refused, by line and hash. *)
let settle (#op: Type) (#brk: Type) (reads: list (nat & lrec (carried op)))
  : Tot (res (list (lrec op)) (lfault brk)) =
  let d = decoded_of reads in
  match sites_of d with
  | [] -> Ok (oks_of d)
  | sites -> Error (LUndecodable sites)

(* F#: `readCarried`. *)
let read_carried (#op: Type) (#brk: Type) (ws: ch -> bool) (actor_of: jline -> res actor fault)
  (w: witness op) (text: list ch)
  : Tot (res (list (nat & lrec (carried op)) & list (list ch)) (lfault brk)) =
  match scan_with_snapshots ws actor_of (carry w) text with
  | Error f -> Error (LUnreadable f)
  | Ok v -> Ok v

let rec snd_all (#a #b: Type) (l: list (a & b)) : Tot (list b) (decreases l) =
  match l with
  | [] -> []
  | (_, y) :: t -> y :: snd_all t

(* F#: `fromJsonlWithSnapshots`. *)
let from_jsonl_with_snapshots (#op: Type) (ws: ch -> bool) (w: witness op) (text: list ch)
  : Tot (res (list (lrec op) & list (list ch)) (lfault C.cbreak)) =
  match read_carried ws (actor_field k_actor) w text with
  | Error f -> Error f
  | Ok (reads, snaps) ->
    (match settle reads with
     | Error f -> Error f
     | Ok recs -> Ok (recs, snaps))

(* F#: `fromJsonl`. *)
let from_jsonl (#op: Type) (ws: ch -> bool) (w: witness op) (text: list ch)
  : Tot (res (list (lrec op)) (lfault C.cbreak)) =
  match from_jsonl_with_snapshots ws w text with
  | Error f -> Error f
  | Ok (recs, _) -> Ok recs

(* F#: `fromJsonlLegacyActor` — the actor a bare string, lifted by `Actor.ofLegacyString`. *)
let legacy_actor (l: jline) : Tot (res actor fault) =
  match string_field k_actor l with
  | Ok s -> Ok (AHuman s)
  | Error f -> Error f

let from_jsonl_legacy_actor (#op: Type) (ws: ch -> bool) (w: witness op) (text: list ch)
  : Tot (res (list (lrec op)) (lfault C.cbreak)) =
  match read_carried ws legacy_actor w text with
  | Error f -> Error f
  | Ok (reads, _) -> settle reads

(* ---- the chain walk the verified load composes (Chain.fst, section 6) ---- *)

let rec pos_of_nat (n: nat) : Tot C.pos (decreases n) =
  if n = 0 then C.PZero else C.PSucc (pos_of_nat (n - 1))

let rec nat_of_pos (p: C.pos) : Tot nat (decreases p) =
  match p with
  | C.PZero -> 0
  | C.PSucc q -> 1 + nat_of_pos q

(* A sequence number as the walk counts: one of the stream's indices is itself; one outside
   them — negative, or at or past the length `n`, which the scan admits and no walk index
   equals — is carried AT the length, where no walk index reaches it. The walk's verdict is the
   same (`r.rseq = i` with `i < n` is false either way); its `got` text is not, and the unary
   carriage stays bounded by the stream rather than by Int32. *)
let pos_of_seq (n: nat) (s: int) : Tot C.pos =
  if 0 <= s && s < n then pos_of_nat s else pos_of_nat n

(* F#: `string : int -> string` on the walk's index. *)
let show_pos (p: C.pos) : Tot string = chs_str (digits_of_nat (nat_of_pos p))

(* F#: the carrying witness's `Encode`, as the walk's `enc_op` sees it. *)
let enc_carried (#op: Type) (w: witness op) (c: carried op) : Tot string =
  chs_str (match snd c with Ok o -> w.enc o | Error _ -> fst c)

(* F#: `Actor.encodeWith quote` over the host's strings — the actor as the chain walk hashes it:
   `kind` first, then the case's members, each quoted by the host's `JsonString.quote` (`qh`)
   over the host's rendering of the decoded string (`ren`). The shape is the model's; the
   escaping of a code unit the model does not compute is the host's, as the hash is. *)
let actor_string (qh: string -> string) (ren: list och -> string) (a: actor) : Tot string =
  match a with
  | AHuman id -> "{\"kind\":\"human\",\"id\":" ^ qh (ren id) ^ "}"
  | AAgent model version id ->
    "{\"kind\":\"agent\",\"model\":" ^ qh (ren model) ^ ",\"version\":" ^ qh (ren version) ^
    ",\"id\":" ^ qh (ren id) ^ "}"

(* A loaded record as `Chain.record`: the actor as the string `Actor.encode` produces, the
   hashes rendered by the host. *)
let chain_record (#op: eqtype) (qh: string -> string) (ren: list och -> string) (n: nat) (r: lrec op)
  : Tot (C.record op) =
  { C.rseq = pos_of_seq n r.lseq;
    C.ractor = actor_string qh ren r.lactor;
    C.rop = r.lop;
    C.rprev = ren r.lprev;
    C.rhash = ren r.lhash }

let rec chain_records_from (#op: eqtype) (qh: string -> string) (ren: list och -> string) (n: nat)
  (rs: list (lrec op)) : Tot (list (C.record op)) (decreases rs) =
  match rs with
  | [] -> []
  | r :: t -> chain_record qh ren n r :: chain_records_from qh ren n t

let chain_records (#op: eqtype) (qh: string -> string) (ren: list och -> string) (rs: list (lrec op))
  : Tot (list (C.record op)) =
  chain_records_from qh ren (len rs) rs

(* F#: `OpStream.firstChainBreak hashFn (carry w)` — the canonical config's genesis `""`. *)
let genesis: string = ""

(* F#: `fromJsonlVerified hashFn w text` — the chain walked BEFORE the ops are settled, over the
   carried records, so a decode failure stops neither the scan nor the verification. *)
let from_jsonl_verified (#op: eqtype) (h: string -> string -> string) (qh: string -> string)
  (ren: list och -> string) (ws: ch -> bool) (w: witness op) (text: list ch)
  : Tot (res (list (lrec op)) (lfault C.cbreak)) =
  match read_carried ws (actor_field k_actor) w text with
  | Error f -> Error f
  | Ok (reads, _) ->
    (match C.first_chain_break h show_pos (enc_carried w) genesis (chain_records qh ren (snd_all reads)) with
     | C.Found b -> Error (LBroken b)
     | C.Missing -> settle reads)

(* ======================================================================================
   9. THEOREM 1 — totality. Every scanner and load above is a `Tot` function of its input: the
      type system has already discharged it — no exception path, every walk terminating on the
      suffix it consumes (section 2's `llen` refinements are what the termination checker spent).
      The row's theorem states the half a reader can cite: every load ANSWERS, a value or a
      typed fault, on every text and every witness.
   ====================================================================================== *)

[@@ noextract_to "FSharp"]
let loads_total (#op: eqtype) (h: string -> string -> string) (qh: string -> string) (ren: list och -> string)
  (ws: ch -> bool) (w: witness op) (text: list ch)
  : Lemma (ensures (Ok? (from_jsonl ws w text) \/ Error? (from_jsonl ws w text)) /\
                   (Ok? (from_jsonl_with_snapshots ws w text) \/ Error? (from_jsonl_with_snapshots ws w text)) /\
                   (Ok? (from_jsonl_legacy_actor ws w text) \/ Error? (from_jsonl_legacy_actor ws w text)) /\
                   (Ok? (from_jsonl_verified h qh ren ws w text) \/ Error? (from_jsonl_verified h qh ren ws w text)) /\
                   (Ok? (check_raw text) \/ Error? (check_raw text)) /\
                   (Ok? (parse_line 1 text) \/ Error? (parse_line 1 text))) = ()

(* ======================================================================================
   10. THEOREM 2 — faults locate.
   ====================================================================================== *)

(* The `i`-th line of a split text (`[]` past the end — never reached by a located fault). *)
let rec line_at (ls: list (list ch)) (i: nat) : Tot (list ch) (decreases ls) =
  match ls with
  | [] -> []
  | l :: t -> if i = 0 then l else line_at t (i - 1)

(* A decoder whose every fault names the line it was handed — what every reader of section 5
   and every load of section 8 does (`line_item_faithful`). *)
[@@ noextract_to "FSharp"]
let faithful (#a: Type) (dec: jline -> res a fault) : Tot prop =
  forall (line: jline). {:pattern (dec line)}
    (match dec line with
     | Error g -> g.fline == line.lnum
     | Ok _ -> True)

(* A fault `scan_records` answers names a line of the text that the scan reached and did not
   skip — the 1-based number over EVERY line, blank lines counted — and is exactly the fault that
   line's parse, or its decode, answered. *)
#push-options "--fuel 2 --ifuel 2"
[@@ noextract_to "FSharp"]
let rec scan_from_locates (#a: Type) (ws: ch -> bool) (dec: jline -> res a fault) (k: nat)
  (lines: list (list ch)) (acc: list a) (f: fault)
  : Lemma (requires faithful dec /\ scan_from ws dec k lines acc == Error f)
          (ensures k < f.fline /\ f.fline <= k + len lines /\
                   (let l = line_at lines (f.fline - k - 1) in
                    not (blank_line ws l) /\
                    (match parse_line f.fline l with
                     | Error g -> g == f
                     | Ok line -> dec line == Error f)))
          (decreases lines) =
  match lines with
  | [] -> ()
  | l :: rest ->
    if blank_line ws l then scan_from_locates ws dec (k + 1) rest acc f
    else
      (match parse_line (k + 1) l with
       | Error _ -> ()
       | Ok line ->
         (match dec line with
          | Error g -> assert (dec line == Error g)
          | Ok v -> scan_from_locates ws dec (k + 1) rest (v :: acc) f))

[@@ noextract_to "FSharp"]
let scan_fault_locates (#a: Type) (ws: ch -> bool) (dec: jline -> res a fault) (text: list ch) (f: fault)
  : Lemma (requires faithful dec /\ scan_records ws dec text == Error f)
          (ensures 1 <= f.fline /\ f.fline <= len (split text) /\
                   (let l = line_at (split text) (f.fline - 1) in
                    not (blank_line ws l) /\
                    (match parse_line f.fline l with
                     | Error g -> g == f
                     | Ok line -> dec line == Error f))) =
  scan_from_locates ws dec 0 (split text) [] f
#pop-options

(* The scanner's own fault sits within the line it refused: at a character of it, or just past
   its last one (production's `fail (at i)` with `i >= n`). *)
[@@ noextract_to "FSharp"]
let parse_fault_in_line (n: nat) (text: list ch) (f: fault)
  : Lemma (requires parse_line n text == Error f) (ensures f.fline == n /\ f.fpos <= len text) = ()

(* An `Unreadable` fault of a load is one `scan_records` answered, or the one snapshot refusal,
   which names a line the scan read: either way the line number is in range. *)
[@@ noextract_to "FSharp"]
let rec memp (#a: Type) (x: a) (l: list a) : Tot prop (decreases l) =
  match l with
  | [] -> False
  | y :: t -> x == y \/ memp x t

[@@ noextract_to "FSharp"]
let rec memp_rev_app (#a: Type) (x: a) (l acc: list a)
  : Lemma (ensures memp x (rev_app l acc) <==> (memp x l \/ memp x acc)) (decreases l) =
  match l with
  | [] -> ()
  | y :: t -> memp_rev_app x t (y :: acc)

(* Where an item of the scan came from: the line it names exists, was not skipped, parsed, and
   decoded to it. *)
[@@ noextract_to "FSharp"]
let located (#a: Type) (ws: ch -> bool) (dec: jline -> res a fault) (k: nat) (lines: list (list ch))
  (v: a) (n: nat) : Tot prop =
  k < n /\ n <= k + len lines /\
  (let l = line_at lines (n - k - 1) in
   not (blank_line ws l) /\
   (match parse_line n l with
    | Error _ -> False
    | Ok line -> dec line == Ok v))

(* A decoder whose every item names the line it was decoded from. *)
[@@ noextract_to "FSharp"]
let numbered (#a: Type) (dec: jline -> res a fault) (num: a -> nat) : Tot prop =
  forall (line: jline). {:pattern (dec line)}
    (match dec line with
     | Ok v -> num v == line.lnum
     | Error _ -> True)

[@@ noextract_to "FSharp"]
let memp_rev_all (#a: Type) (vs: list a) (acc: list a)
  : Lemma (requires vs == rev acc) (ensures forall (v: a). memp v vs ==> memp v acc) =
  introduce forall (v: a). memp v vs ==> memp v acc
  with introduce _ ==> _
  with memp_rev_app v acc []

[@@ noextract_to "FSharp"]
let rec scan_from_items (#a: Type) (ws: ch -> bool) (dec: jline -> res a fault) (num: a -> nat)
  (k: nat) (lines: list (list ch)) (acc: list a) (vs: list a)
  : Lemma (requires numbered dec num /\ scan_from ws dec k lines acc == Ok vs)
          (ensures forall (v: a). memp v vs ==> (memp v acc \/ located ws dec k lines v (num v)))
          (decreases lines) =
  match lines with
  | [] -> memp_rev_all vs acc
  | l :: rest ->
    if blank_line ws l then scan_from_items ws dec num (k + 1) rest acc vs
    else
      (match parse_line (k + 1) l with
       | Error _ -> ()
       | Ok line ->
         (match dec line with
          | Error _ -> ()
          | Ok v0 ->
            scan_from_items ws dec num (k + 1) rest (v0 :: acc) vs;
            assert (dec line == Ok v0);
            assert (located ws dec k lines v0 (k + 1))))

(* ---- the loads' decoders name their lines, and `record_of`'s answer says what it read ---- *)

(* Every fault a field reader answers is at the line's own number (`fault_at l.lnum`), the actor
   reader's included — its inner line carries the outer number. *)
#push-options "--fuel 2 --ifuel 2"
[@@ noextract_to "FSharp"]
let actor_field_faithful (key: list ch) (l: jline) (g: fault)
  : Lemma (requires actor_field key l == Error g) (ensures g.fline == l.lnum) = ()

[@@ noextract_to "FSharp"]
let record_of_faithful (#op: Type) (actor_of: jline -> res actor fault) (w: witness op) (l: jline) (g: fault)
  : Lemma (requires (forall (h: fault). actor_of l == Error h ==> h.fline == l.lnum) /\
                    record_of actor_of w l == Error g)
          (ensures g.fline == l.lnum) = ()

[@@ noextract_to "FSharp"]
let line_item_faithful (#op: Type) (w: witness op)
  : Lemma (ensures faithful (line_item (actor_field k_actor) w) /\
                   faithful (line_item legacy_actor w) /\
                   numbered (line_item (actor_field k_actor) w) (fun (i: item op) -> (match i with Rec n _ -> n | Snap l -> l.lnum)) /\
                   numbered (line_item legacy_actor w) (fun (i: item op) -> (match i with Rec n _ -> n | Snap l -> l.lnum))) =
  introduce forall (l: jline). (match line_item (actor_field k_actor) w l with Error g -> g.fline == l.lnum | Ok _ -> True)
  with (match line_item (actor_field k_actor) w l with
        | Error g ->
          (match actor_field k_actor l with
           | Error h -> actor_field_faithful k_actor l h
           | Ok _ -> ());
          record_of_faithful (actor_field k_actor) w l g
        | Ok _ -> ());
  introduce forall (l: jline). (match line_item legacy_actor w l with Error g -> g.fline == l.lnum | Ok _ -> True)
  with (match line_item legacy_actor w l with
        | Error g -> record_of_faithful legacy_actor w l g
        | Ok _ -> ())

(* What a record `record_of` answered was read from: the `op` span the witness was handed and
   what it said, and the `hash` member. *)
[@@ noextract_to "FSharp"]
let record_of_inv (#op: Type) (actor_of: jline -> res actor fault) (w: witness op) (l: jline) (r: lrec op)
  : Lemma (requires record_of actor_of w l == Ok r)
          (ensures (match raw_field k_op l with
                    | Ok raw -> w.dec raw == Ok r.lop
                    | Error _ -> False) /\
                   string_field k_hash l == Ok r.lhash) = ()
#pop-options

(* ---- an `Undecodable` site names the line, the op and the hash it was read from ---- *)

(* The read a site was settled from, found again. *)
[@@ noextract_to "FSharp"]
let rec read_of_site (#op: Type) (reads: list (nat & lrec (carried op))) (s: usite)
  : Tot (option (nat & lrec (carried op))) (decreases reads) =
  match reads with
  | [] -> None
  | (n, r) :: t ->
    (match snd r.lop with
     | Error why -> if n = s.uline && r.lhash = s.unode && why = s.ureason then Some (n, r) else read_of_site t s
     | Ok _ -> read_of_site t s)

#push-options "--fuel 2 --ifuel 2"
[@@ noextract_to "FSharp"]
let rec site_from_read (#op: Type) (reads: list (nat & lrec (carried op))) (s: usite)
  : Lemma (requires memp s (sites_of (decoded_of reads)))
          (ensures (match read_of_site reads s with
                    | Some (n, r) -> memp (n, r) reads /\ n == s.uline /\ r.lhash == s.unode /\ snd r.lop == Error s.ureason
                    | None -> False))
          (decreases reads) =
  match reads with
  | [] -> ()
  | (n, r) :: t ->
    (match snd r.lop with
     | Error why -> if n = s.uline && r.lhash = s.unode && why = s.ureason then () else site_from_read t s
     | Ok _ -> site_from_read t s)

(* Every read `settle_items` answers was an item of the scan. *)
[@@ noextract_to "FSharp"]
let rec settle_items_inv (#op: Type) (first: bool) (recs: list (nat & lrec op)) (snaps: list (list ch))
  (items: list (item op)) (reads: list (nat & lrec op)) (snaps': list (list ch))
  : Lemma (requires settle_items first recs snaps items == Ok (reads, snaps'))
          (ensures forall (n: nat) (r: lrec op). memp (n, r) reads ==> (memp (n, r) recs \/ memp (Rec n r) items))
          (decreases items) =
  match items with
  | [] ->
    introduce forall (n: nat) (r: lrec op). memp (n, r) reads ==> (memp (n, r) recs \/ memp (Rec n r) items)
    with introduce _ ==> _
    with memp_rev_app (n, r) recs []
  | Rec n0 r0 :: rest -> settle_items_inv false ((n0, r0) :: recs) snaps rest reads snaps'
  | Snap l :: rest -> if first then settle_items_inv false recs [l.ltext] rest reads snaps' else ()

(* THEOREM 2, the decode half: every site of an `Undecodable` load names a line that exists in
   the text, which parsed to a record whose `op` span the witness refused with the site's reason,
   and whose `hash` member is the site's node id. *)
[@@ noextract_to "FSharp"]
let site_names_its_op (#op: Type) (ws: ch -> bool) (w: witness op) (text: list ch) (sites: list usite) (s: usite)
  : Lemma (requires from_jsonl_with_snapshots ws w text == Error (LUndecodable sites) /\ memp s sites)
          (ensures 1 <= s.uline /\ s.uline <= len (split text) /\
                   (let l = line_at (split text) (s.uline - 1) in
                    not (blank_line ws l) /\
                    (match parse_line s.uline l with
                     | Error _ -> False
                     | Ok line ->
                       (match raw_field k_op line with
                        | Ok raw -> w.dec raw == Error s.ureason
                        | Error _ -> False) /\
                       string_field k_hash line == Ok s.unode))) =
  let num = (fun (i: item (carried op)) -> (match i with Rec n _ -> n | Snap l -> l.lnum)) in
  line_item_faithful (carry w);
  match scan_records ws (line_item (actor_field k_actor) (carry w)) text with
  | Error _ -> ()
  | Ok items ->
    (match settle_items true [] [] items with
     | Error _ -> ()
     | Ok (reads, snaps) ->
       site_from_read reads s;
       (match read_of_site reads s with
        | None -> ()
        | Some (n, r) ->
          settle_items_inv true [] [] items reads snaps;
          scan_from_items ws (line_item (actor_field k_actor) (carry w)) num 0 (split text) [] items;
          assert (memp (Rec n r) items);
          assert (located ws (line_item (actor_field k_actor) (carry w)) 0 (split text) (Rec n r) n);
          (match parse_line n (line_at (split text) (n - 1)) with
           | Error _ -> ()
           | Ok line -> record_of_inv (actor_field k_actor) (carry w) line r)))
#pop-options

(* ======================================================================================
   11. THEOREM 3 — the write-then-load round trip.
   ====================================================================================== *)

(* ---- 11a. list facts ---- *)

[@@ noextract_to "FSharp"]
let rec app_nil_r (#a: Type) (l: list a) : Lemma (ensures app l [] == l) (decreases l) =
  match l with
  | [] -> ()
  | _ :: t -> app_nil_r t

[@@ noextract_to "FSharp"]
let rec app_assoc (#a: Type) (x y z: list a) : Lemma (ensures app (app x y) z == app x (app y z)) (decreases x) =
  match x with
  | [] -> ()
  | _ :: t -> app_assoc t y z

[@@ noextract_to "FSharp"]
let rec len_app (#a: Type) (x y: list a) : Lemma (ensures len (app x y) == len x + len y) (decreases x) =
  match x with
  | [] -> ()
  | _ :: t -> len_app t y

[@@ noextract_to "FSharp"]
let rec take_n_app (x y: list ch) : Lemma (ensures take_n (len x) (app x y) == x) (decreases x) =
  match x with
  | [] -> ()
  | _ :: t -> take_n_app t y

(* `rev (rev_app l acc) == rev_app acc l` — what every accumulate-and-reverse walk ends with. *)
[@@ noextract_to "FSharp"]
let rec rev_rev_app (#a: Type) (l acc: list a) : Lemma (ensures rev_app (rev_app l acc) [] == rev_app acc l) (decreases l) =
  match l with
  | [] -> ()
  | x :: t -> rev_rev_app t (x :: acc)

[@@ noextract_to "FSharp"]
let rec all_app (#a: Type) (p: a -> bool) (x y: list a) : Lemma (ensures all p (app x y) == (all p x && all p y)) (decreases x) =
  match x with
  | [] -> ()
  | _ :: t -> all_app p t y

(* ---- 11b. the string token: a decoded string a writer can spell reads back ---- *)

(* A decoded string as the writer leaves it and the scanner admits it: a copied character is
   none of the five the writer re-spells (quote, backslash, tab, line feed, carriage return —
   production escapes those on the way out, so the host hands them escaped); a short escape is
   one of the eight; a `\uXXXX` has four hex digits, and a high surrogate escape is followed at
   once by a low one. *)
[@@ noextract_to "FSharp"]
let rec wf_str (s: list och) : Tot bool (decreases s) =
  match s with
  | [] -> true
  | OLit c :: t -> c <> CQuote && c <> CBackslash && c <> CNewline && c <> CReturn && c <> CTab && wf_str t
  | OEsc e :: t -> is_short_escape e && wf_str t
  | OUni a b c d :: t ->
    is_hex a && is_hex b && is_hex c && is_hex d &&
    (match uni_class a b with
     | SNone -> wf_str t
     | SLow -> false
     | SHigh ->
       (match t with
        | OUni a2 b2 c2 d2 :: t2 ->
          is_hex a2 && is_hex b2 && is_hex c2 && is_hex d2 && uni_class a2 b2 = SLow && wf_str t2
        | _ -> false))

#push-options "--fuel 16 --ifuel 2"
[@@ noextract_to "FSharp"]
let rec string_tail_quoted (start: list ch) (acc: list och) (s: list och) (rest: list ch)
  : Lemma (requires wf_str s)
          (ensures string_tail start acc (app (quote_body s) (CQuote :: rest)) == SOk (rev_app acc s) rest)
          (decreases s) =
  match s with
  | [] -> ()
  | OLit c :: t -> string_tail_quoted start (OLit c :: acc) t rest
  | OEsc e :: t -> string_tail_quoted start (OEsc e :: acc) t rest
  | OUni a b c d :: t ->
    (match uni_class a b with
     | SNone -> string_tail_quoted start (OUni a b c d :: acc) t rest
     | SLow -> ()
     | SHigh ->
       (match t with
        | OUni a2 b2 c2 d2 :: t2 -> string_tail_quoted start (OUni a2 b2 c2 d2 :: OUni a b c d :: acc) t2 rest
        | _ -> ()))

[@@ noextract_to "FSharp"]
let unquote_quote (s: list och)
  : Lemma (requires wf_str s) (ensures unquote (quote_str s) == Ok s) =
  string_tail_quoted (quote_str s) [] s []

[@@ noextract_to "FSharp"]
let skip_value_quoted (s: list och) (rest: list ch)
  : Lemma (requires wf_str s) (ensures skip_value (app (quote_str s) rest) == SOk () rest) =
  app_assoc (quote_body s) [CQuote] rest;
  string_tail_quoted (app (quote_str s) rest) [] s rest
#pop-options

(* ---- 11c. a scan that consumed its input consumes that prefix of any extension ---- *)

#push-options "--fuel 16 --ifuel 2 --z3rlimit 80"
[@@ noextract_to "FSharp"]
let rec string_tail_ext (start start': list ch) (acc v: list och) (s r rest: list ch)
  : Lemma (requires string_tail start acc s == SOk v r)
          (ensures string_tail start' acc (app s rest) == SOk v (app r rest))
          (decreases (llen s)) =
  match s with
  | [] -> ()
  | CQuote :: t -> ()
  | CBackslash :: t ->
    (match t with
     | [] -> ()
     | CLu :: u ->
       (match hex4 u with
        | NoHex4 -> ()
        | H4 a b c d r1 ->
          assert (u == a :: b :: c :: d :: r1);
          (match uni_class a b with
           | SNone -> string_tail_ext start start' (OUni a b c d :: acc) v r1 r rest
           | SLow -> ()
           | SHigh ->
             (match r1 with
              | CBackslash :: CLu :: u2 ->
                (match hex4 u2 with
                 | H4 a2 b2 c2 d2 r2 ->
                   assert (u2 == a2 :: b2 :: c2 :: d2 :: r2);
                   assert (app s rest == CBackslash :: CLu :: a :: b :: c :: d :: CBackslash :: CLu :: a2 :: b2 :: c2 :: d2 :: app r2 rest);
                   if uni_class a2 b2 = SLow
                   then string_tail_ext start start' (OUni a2 b2 c2 d2 :: OUni a b c d :: acc) v r2 r rest
                   else ()
                 | NoHex4 -> ())
              | _ -> ())))
     | e :: u -> if is_short_escape e then string_tail_ext start start' (OEsc e :: acc) v u r rest else ())
  | c :: t -> string_tail_ext start start' (OLit c :: acc) v t r rest
#pop-options

#push-options "--fuel 2 --ifuel 2"
[@@ noextract_to "FSharp"]
let rec skip_container_ext (start start': list ch) (depth: pos) (s r rest: list ch)
  : Lemma (requires skip_container start depth s == SOk () r)
          (ensures skip_container start' depth (app s rest) == SOk () (app r rest))
          (decreases (llen s)) =
  match s with
  | [] -> ()
  | CQuote :: t ->
    (match string_tail s [] t with
     | SErr _ _ -> ()
     | SOk x r1 ->
       string_tail_ext s (app s rest) [] x t r1 rest;
       skip_container_ext start start' depth r1 r rest)
  | CLBrace :: t -> skip_container_ext start start' (depth + 1) t r rest
  | CLBrack :: t -> skip_container_ext start start' (depth + 1) t r rest
  | CRBrace :: t -> if depth = 1 then () else skip_container_ext start start' (depth - 1) t r rest
  | CRBrack :: t -> if depth = 1 then () else skip_container_ext start start' (depth - 1) t r rest
  | _ :: t -> skip_container_ext start start' depth t r rest

[@@ noextract_to "FSharp"]
let rec bare_token_split (s: list ch)
  : Lemma (ensures (let (tok, r) = bare_token s in app tok r == s)) (decreases s) =
  match s with
  | [] -> ()
  | x :: t -> if is_terminator x then () else bare_token_split t

[@@ noextract_to "FSharp"]
let rec bare_token_ext (s: list ch) (c: ch) (more: list ch)
  : Lemma (requires bare_token s == (s, []) /\ is_terminator c)
          (ensures bare_token (app s (c :: more)) == (s, c :: more))
          (decreases s) =
  match s with
  | [] -> ()
  | x :: t -> if is_terminator x then () else bare_token_ext t c more

[@@ noextract_to "FSharp"]
let skip_value_ext (s: list ch) (c: ch) (more: list ch)
  : Lemma (requires skip_value s == SOk () [] /\ is_terminator c)
          (ensures skip_value (app s (c :: more)) == SOk () (c :: more)) =
  match s with
  | [] -> ()
  | CQuote :: t ->
    (match string_tail s [] t with
     | SErr _ _ -> ()
     | SOk x r -> string_tail_ext s (app s (c :: more)) [] x t r (c :: more))
  | CLBrace :: t -> skip_container_ext s (app s (c :: more)) 1 t [] (c :: more)
  | CLBrack :: t -> skip_container_ext s (app s (c :: more)) 1 t [] (c :: more)
  | CComma :: _ -> ()
  | CRBrace :: _ -> ()
  | CRBrack :: _ -> ()
  | _ ->
    let (tok, r) = bare_token s in
    bare_token_split s;
    app_nil_r tok;
    bare_token_ext s c more
#pop-options

(* ---- 11d. a member the writer spelled is read as one member ---- *)

(* A key spelled plainly: no quote, no backslash, no whitespace to lead with. *)
[@@ noextract_to "FSharp"]
let plain_key (k: list ch) : Tot bool = all (fun c -> c <> CQuote && c <> CBackslash) k

#push-options "--fuel 2 --ifuel 2"
[@@ noextract_to "FSharp"]
let rec string_tail_plain (start: list ch) (acc: list och) (k: list ch) (rest: list ch)
  : Lemma (requires plain_key k)
          (ensures string_tail start acc (app k (CQuote :: rest)) == SOk (rev_app acc (olit k)) rest)
          (decreases k) =
  match k with
  | [] -> ()
  | c :: t -> string_tail_plain start (OLit c :: acc) t rest

(* The text of `"k":v` followed by `y`, re-associated to the scanner's steps. *)
[@@ noextract_to "FSharp"]
let member_text_app (k v: list ch) (y: list ch)
  : Lemma (ensures app (member_text k v) y == CQuote :: app k (CQuote :: CColon :: app v y)) =
  app_assoc k (CQuote :: CColon :: v) y

(* The member `members_loop` reads from `"k":v` when `c` follows. *)
[@@ noextract_to "FSharp"]
let member_read (off: nat) (whole: list ch) (k v: list ch) (c: ch) (more: list ch) : Tot member =
  { mkey = olit k; mspan = v; mpos = off + pos_in whole (app v (c :: more)) }

[@@ noextract_to "FSharp"]
let member_value_ok (v: list ch) : Tot prop =
  Cons? v /\ not (is_ws (Cons?.hd v)) /\ skip_value v == SOk () []

[@@ noextract_to "FSharp"]
let members_step (off: nat) (whole: list ch) (acc: list member) (k v: list ch) (c: ch) (more: list ch)
  : Lemma (requires plain_key k /\ member_value_ok v /\ (c == CComma \/ c == CRBrace))
          (ensures (let m = member_read off whole k v c more in
                    members_loop off whole acc (app (member_text k v) (c :: more)) ==
                    (if c = CComma then members_loop off whole (m :: acc) more else SOk (rev (m :: acc)) more))) =
  member_text_app k v (c :: more);
  string_tail_plain (app (member_text k v) (c :: more)) [] k (CColon :: app v (c :: more));
  skip_value_ext v c more;
  len_app v (c :: more);
  take_n_app v (c :: more)
#pop-options

(* ---- 11e. the sequence numeral reads back ---- *)

[@@ noextract_to "FSharp"]
let rec pow10 (k: nat) : Tot nat (decreases k) = if k = 0 then 1 else 10 * pow10 (k - 1)

[@@ noextract_to "FSharp"]
let rec digits_all (n: nat) : Lemma (ensures all is_digit (digits_of_nat n)) (decreases n) =
  if n < 10 then () else (digits_all (n / 10); all_app is_digit (digits_of_nat (n / 10)) [digit_ch (n % 10)])

[@@ noextract_to "FSharp"]
let rec fold_digits_app (acc: nat) (x y: list ch)
  : Lemma (ensures fold_digits acc (app x y) == fold_digits (fold_digits acc x) y) (decreases x) =
  match x with
  | [] -> ()
  | c :: t -> fold_digits_app (acc * 10 + digit_val c) t y

[@@ noextract_to "FSharp"]
let rec digits_value (n: nat) : Lemma (ensures fold_digits 0 (digits_of_nat n) == n) (decreases n) =
  if n < 10 then () else (digits_value (n / 10); fold_digits_app 0 (digits_of_nat (n / 10)) [digit_ch (n % 10)])

[@@ noextract_to "FSharp"]
let rec digits_head (n: nat)
  : Lemma (ensures (let d = digits_of_nat n in Cons? d /\ (d == [CD0] \/ Cons?.hd d <> CD0))) (decreases n) =
  if n < 10 then () else digits_head (n / 10)

[@@ noextract_to "FSharp"]
let rec digits_len (n: nat) (k: nat)
  : Lemma (requires k >= 1 /\ n < pow10 k) (ensures len (digits_of_nat n) <= k) (decreases n) =
  if n < 10 then ()
  else (digits_len (n / 10) (k - 1); len_app (digits_of_nat (n / 10)) [digit_ch (n % 10)])

[@@ noextract_to "FSharp"]
let read_int_show (s: int)
  : Lemma (requires 0 - int32_min_mag <= s /\ s <= int32_max) (ensures read_int (show_int s) == Some s) =
  let n: nat = (if s < 0 then 0 - s else s) in
  assert_norm (pow10 10 == 10000000000);
  digits_all n; digits_value n; digits_head n; digits_len n 10

(* The digits are one bare token, and that token is a JSON number. *)
[@@ noextract_to "FSharp"]
let rec bare_token_all (s: list ch)
  : Lemma (requires all (fun c -> not (is_terminator c)) s) (ensures bare_token s == (s, [])) (decreases s) =
  match s with
  | [] -> ()
  | _ :: t -> bare_token_all t

[@@ noextract_to "FSharp"]
let rec take_digits_all (s: list ch)
  : Lemma (requires all is_digit s) (ensures take_digits s == (s, [])) (decreases s) =
  match s with
  | [] -> ()
  | _ :: t -> take_digits_all t

[@@ noextract_to "FSharp"]
let rec all_digits_no_terminator (s: list ch)
  : Lemma (requires all is_digit s) (ensures all (fun c -> not (is_terminator c)) s) (decreases s) =
  match s with
  | [] -> ()
  | _ :: t -> all_digits_no_terminator t

#push-options "--fuel 4 --ifuel 2"
[@@ noextract_to "FSharp"]
let digits_number (n: nat) : Lemma (ensures is_json_number (digits_of_nat n)) =
  digits_all n; digits_head n;
  take_digits_all (digits_of_nat n);
  (match digits_of_nat n with
   | CD0 :: t -> ()
   | d :: t -> take_digits_all t)

[@@ noextract_to "FSharp"]
let skip_value_int (s: int) : Lemma (ensures member_value_ok (show_int s)) =
  let n: nat = (if s < 0 then 0 - s else s) in
  digits_all n; digits_head n; digits_number n;
  all_digits_no_terminator (digits_of_nat n);
  bare_token_all (digits_of_nat n);
  if s < 0 then bare_token_all (show_int s) else ()
#pop-options

(* ---- 11f. what `check_raw`'s `Ok` says of the span ---- *)

[@@ noextract_to "FSharp"]
let clean (s: list ch) : Tot bool = all (fun c -> c <> CNewline && c <> CReturn) s

[@@ noextract_to "FSharp"]
let rec first_break_none (k: nat) (s: list ch)
  : Lemma (requires first_break k s == None) (ensures clean s) (decreases s) =
  match s with
  | [] -> ()
  | _ :: t -> first_break_none (k + 1) t

#push-options "--fuel 2 --ifuel 2"
[@@ noextract_to "FSharp"]
let check_raw_ok (raw: list ch)
  : Lemma (requires check_raw raw == Ok ()) (ensures member_value_ok raw /\ clean raw) =
  first_break_none 0 raw
#pop-options

(* ---- 11g. the actor reads back ---- *)

[@@ noextract_to "FSharp"]
let actor_wf (a: actor) : Tot bool =
  actor_valid a &&
  (match a with
   | AHuman id -> wf_str id
   | AAgent model version id -> wf_str model && wf_str version && wf_str id)

#push-options "--fuel 6 --ifuel 2"
(* A quoted member inside a container is walked through: the key's string, the colon, the
   value's string. *)
[@@ noextract_to "FSharp"]
let skip_container_member (start: list ch) (depth: pos) (k: list ch) (v: list och) (rest: list ch)
  : Lemma (requires plain_key k /\ wf_str v)
          (ensures skip_container start depth (app (member_text k (quote_str v)) rest) == skip_container start depth rest) =
  member_text_app k (quote_str v) rest;
  let s = app (member_text k (quote_str v)) rest in
  string_tail_plain s [] k (CColon :: app (quote_str v) rest);
  app_assoc (quote_body v) [CQuote] rest;
  string_tail_quoted (app (quote_str v) rest) [] v rest
#pop-options

#push-options "--fuel 8 --ifuel 2"
[@@ noextract_to "FSharp"]
let keys_plain (_: unit)
  : Lemma (ensures plain_key k_seq /\ plain_key k_actor /\ plain_key k_op /\ plain_key k_prev_hash /\
                   plain_key k_hash /\ plain_key k_kind /\ plain_key k_id /\ plain_key k_model /\
                   plain_key k_version /\ wf_str (olit lit_human) /\ wf_str (olit lit_agent) /\
                   olit lit_human <> olit lit_agent /\ olit k_kind <> olit k_id /\
                   olit k_kind <> olit k_model /\ olit k_kind <> olit k_version /\
                   olit k_model <> olit k_version /\ olit k_model <> olit k_id /\ olit k_version <> olit k_id) =
  assert_norm (plain_key k_seq /\ plain_key k_actor /\ plain_key k_op /\ plain_key k_prev_hash /\
               plain_key k_hash /\ plain_key k_kind /\ plain_key k_id /\ plain_key k_model /\
               plain_key k_version /\ wf_str (olit lit_human) /\ wf_str (olit lit_agent) /\
               olit lit_human <> olit lit_agent /\ olit k_kind <> olit k_id /\
               olit k_kind <> olit k_model /\ olit k_kind <> olit k_version /\
               olit k_model <> olit k_version /\ olit k_model <> olit k_id /\ olit k_version <> olit k_id)

[@@ noextract_to "FSharp"]
let skip_value_actor (a: actor)
  : Lemma (requires actor_wf a) (ensures member_value_ok (encode_actor a)) =
  keys_plain ();
  match a with
  | AHuman id ->
    let b = member_text k_id (quote_str id) in
    app_assoc (member_text k_kind (quote_str (olit lit_human))) (CComma :: b) [CRBrace];
    skip_container_member (encode_actor a) 1 k_kind (olit lit_human) (CComma :: app b [CRBrace]);
    skip_container_member (encode_actor a) 1 k_id id [CRBrace]
  | AAgent model version id ->
    let m1 = member_text k_kind (quote_str (olit lit_agent)) in
    let m2 = member_text k_model (quote_str model) in
    let m3 = member_text k_version (quote_str version) in
    let m4 = member_text k_id (quote_str id) in
    app_assoc m1 (CComma :: app m2 (CComma :: app m3 (CComma :: m4))) [CRBrace];
    app_assoc m2 (CComma :: app m3 (CComma :: m4)) [CRBrace];
    app_assoc m3 (CComma :: m4) [CRBrace];
    skip_container_member (encode_actor a) 1 k_kind (olit lit_agent) (CComma :: app m2 (CComma :: app m3 (CComma :: app m4 [CRBrace])));
    skip_container_member (encode_actor a) 1 k_model model (CComma :: app m3 (CComma :: app m4 [CRBrace]));
    skip_container_member (encode_actor a) 1 k_version version (CComma :: app m4 [CRBrace]);
    skip_container_member (encode_actor a) 1 k_id id [CRBrace]
#pop-options

(* ---- 11h. the record line parses to its five members, and reads back ---- *)

[@@ noextract_to "FSharp"]
let has_member (k: list ch) (v: list ch) (ms: list member) : Tot prop =
  match member_of k ms with
  | Some (x, _) -> x == v
  | None -> False

#push-options "--fuel 8 --ifuel 2"
[@@ noextract_to "FSharp"]
let record_keys_distinct (_: unit)
  : Lemma (ensures olit k_seq <> olit k_actor /\ olit k_seq <> olit k_op /\ olit k_seq <> olit k_prev_hash /\
                   olit k_seq <> olit k_hash /\ olit k_seq <> olit k_snapshot /\
                   olit k_actor <> olit k_op /\ olit k_actor <> olit k_prev_hash /\ olit k_actor <> olit k_hash /\
                   olit k_actor <> olit k_snapshot /\
                   olit k_op <> olit k_prev_hash /\ olit k_op <> olit k_hash /\ olit k_op <> olit k_snapshot /\
                   olit k_prev_hash <> olit k_hash /\ olit k_prev_hash <> olit k_snapshot /\
                   olit k_hash <> olit k_snapshot) =
  assert_norm (olit k_seq <> olit k_actor /\ olit k_seq <> olit k_op /\ olit k_seq <> olit k_prev_hash /\
               olit k_seq <> olit k_hash /\ olit k_seq <> olit k_snapshot /\
               olit k_actor <> olit k_op /\ olit k_actor <> olit k_prev_hash /\ olit k_actor <> olit k_hash /\
               olit k_actor <> olit k_snapshot /\
               olit k_op <> olit k_prev_hash /\ olit k_op <> olit k_hash /\ olit k_op <> olit k_snapshot /\
               olit k_prev_hash <> olit k_hash /\ olit k_prev_hash <> olit k_snapshot /\
               olit k_hash <> olit k_snapshot)

(* `{a,b}` and `{a,b,c,d,e}`, re-associated to the member steps. *)
[@@ noextract_to "FSharp"]
let obj2 (a b: list ch)
  : Lemma (ensures obj_text [a; b] == CLBrace :: app a (CComma :: app b [CRBrace])) =
  app_assoc a (CComma :: b) [CRBrace]

[@@ noextract_to "FSharp"]
let obj4 (a b c d: list ch)
  : Lemma (ensures obj_text [a; b; c; d] == CLBrace :: app a (CComma :: app b (CComma :: app c (CComma :: app d [CRBrace])))) =
  app_assoc a (CComma :: app b (CComma :: app c (CComma :: d))) [CRBrace];
  app_assoc b (CComma :: app c (CComma :: d)) [CRBrace];
  app_assoc c (CComma :: d) [CRBrace]

[@@ noextract_to "FSharp"]
let obj5 (a b c d e: list ch)
  : Lemma (ensures obj_text [a; b; c; d; e] ==
                   CLBrace :: app a (CComma :: app b (CComma :: app c (CComma :: app d (CComma :: app e [CRBrace]))))) =
  app_assoc a (CComma :: app b (CComma :: app c (CComma :: app d (CComma :: e)))) [CRBrace];
  app_assoc b (CComma :: app c (CComma :: app d (CComma :: e))) [CRBrace];
  app_assoc c (CComma :: app d (CComma :: e)) [CRBrace];
  app_assoc d (CComma :: e) [CRBrace]
#pop-options

#push-options "--fuel 10 --ifuel 2 --z3rlimit 160"
(* The actor object parses to its members, and `actor_field` reads the actor back. *)
[@@ noextract_to "FSharp"]
let actor_field_reads (a: actor) (l: jline) (p: nat)
  : Lemma (requires actor_wf a /\ member_of k_actor l.lfields == Some (encode_actor a, p))
          (ensures actor_field k_actor l == Ok a) =
  keys_plain ();
  let v = encode_actor a in
  match a with
  | AHuman id ->
    let t1 = member_text k_kind (quote_str (olit lit_human)) in
    let t2 = member_text k_id (quote_str id) in
    obj2 t1 t2;
    app_nil_r (quote_str (olit lit_human));
    app_nil_r (quote_str id);
    skip_value_quoted (olit lit_human) [];
    skip_value_quoted id [];
    members_step p v [] k_kind (quote_str (olit lit_human)) CComma (app t2 [CRBrace]);
    let m1 = member_read p v k_kind (quote_str (olit lit_human)) CComma (app t2 [CRBrace]) in
    members_step p v [m1] k_id (quote_str id) CRBrace [];
    let m2 = member_read p v k_id (quote_str id) CRBrace [] in
    assert (members_loop p v [] (app t1 (CComma :: app t2 [CRBrace])) == SOk [m1; m2] []);
    assert (members_of p v == Ok [m1; m2]);
    unquote_quote (olit lit_human);
    unquote_quote id
  | AAgent model version id ->
    let t1 = member_text k_kind (quote_str (olit lit_agent)) in
    let t2 = member_text k_model (quote_str model) in
    let t3 = member_text k_version (quote_str version) in
    let t4 = member_text k_id (quote_str id) in
    obj4 t1 t2 t3 t4;
    app_nil_r (quote_str (olit lit_agent));
    app_nil_r (quote_str model);
    app_nil_r (quote_str version);
    app_nil_r (quote_str id);
    skip_value_quoted (olit lit_agent) [];
    skip_value_quoted model [];
    skip_value_quoted version [];
    skip_value_quoted id [];
    let r2 = app t2 (CComma :: app t3 (CComma :: app t4 [CRBrace])) in
    let r3 = app t3 (CComma :: app t4 [CRBrace]) in
    let r4 = app t4 [CRBrace] in
    members_step p v [] k_kind (quote_str (olit lit_agent)) CComma r2;
    let m1 = member_read p v k_kind (quote_str (olit lit_agent)) CComma r2 in
    members_step p v [m1] k_model (quote_str model) CComma r3;
    let m2 = member_read p v k_model (quote_str model) CComma r3 in
    members_step p v [m2; m1] k_version (quote_str version) CComma r4;
    let m3 = member_read p v k_version (quote_str version) CComma r4 in
    members_step p v [m3; m2; m1] k_id (quote_str id) CRBrace [];
    let m4 = member_read p v k_id (quote_str id) CRBrace [] in
    assert (members_loop p v [] (app t1 (CComma :: r2)) == SOk [m1; m2; m3; m4] []);
    assert (members_of p v == Ok [m1; m2; m3; m4]);
    let ms = [m1; m2; m3; m4] in
    let inner = { lnum = l.lnum; ltext = v; lfields = ms } in
    assert (member_of k_kind ms == Some (quote_str (olit lit_agent), m1.mpos));
    assert (member_of k_model ms == Some (quote_str model, m2.mpos));
    assert (member_of k_version ms == Some (quote_str version, m3.mpos));
    assert (member_of k_id ms == Some (quote_str id, m4.mpos));
    unquote_quote (olit lit_agent);
    unquote_quote model;
    unquote_quote version;
    unquote_quote id;
    assert (actor_str k_actor k_kind inner == Ok (olit lit_agent));
    assert (actor_str k_actor k_model inner == Ok model);
    assert (actor_str k_actor k_version inner == Ok version);
    assert (actor_str k_actor k_id inner == Ok id)

(* The line the writer spelled parses to exactly its five members. *)
[@@ noextract_to "FSharp"]
let line_members (seq: int) (a: actor) (raw: list ch) (prev hash: list och)
  : Lemma (requires actor_wf a /\ wf_str prev /\ wf_str hash /\ member_value_ok raw)
          (ensures (match members_of 0 (line_of seq a raw prev hash) with
                    | Ok ms ->
                      has_member k_seq (show_int seq) ms /\ has_member k_actor (encode_actor a) ms /\
                      has_member k_op raw ms /\ has_member k_prev_hash (quote_str prev) ms /\
                      has_member k_hash (quote_str hash) ms /\ member_of k_snapshot ms == None
                    | Error _ -> False)) =
  keys_plain (); record_keys_distinct ();
  let whole = line_of seq a raw prev hash in
  let t1 = member_text k_seq (show_int seq) in
  let t2 = member_text k_actor (encode_actor a) in
  let t3 = member_text k_op raw in
  let t4 = member_text k_prev_hash (quote_str prev) in
  let t5 = member_text k_hash (quote_str hash) in
  obj5 t1 t2 t3 t4 t5;
  skip_value_int seq;
  skip_value_actor a;
  app_nil_r (quote_str prev);
  app_nil_r (quote_str hash);
  skip_value_quoted prev [];
  skip_value_quoted hash [];
  let r2 = app t2 (CComma :: app t3 (CComma :: app t4 (CComma :: app t5 [CRBrace]))) in
  let r3 = app t3 (CComma :: app t4 (CComma :: app t5 [CRBrace])) in
  let r4 = app t4 (CComma :: app t5 [CRBrace]) in
  let r5 = app t5 [CRBrace] in
  members_step 0 whole [] k_seq (show_int seq) CComma r2;
  let m1 = member_read 0 whole k_seq (show_int seq) CComma r2 in
  members_step 0 whole [m1] k_actor (encode_actor a) CComma r3;
  let m2 = member_read 0 whole k_actor (encode_actor a) CComma r3 in
  members_step 0 whole [m2; m1] k_op raw CComma r4;
  let m3 = member_read 0 whole k_op raw CComma r4 in
  members_step 0 whole [m3; m2; m1] k_prev_hash (quote_str prev) CComma r5;
  let m4 = member_read 0 whole k_prev_hash (quote_str prev) CComma r5 in
  members_step 0 whole [m4; m3; m2; m1] k_hash (quote_str hash) CRBrace [];
  let m5 = member_read 0 whole k_hash (quote_str hash) CRBrace [] in
  assert (members_loop 0 whole [] (app t1 (CComma :: r2)) == SOk [m1; m2; m3; m4; m5] []);
  assert (members_of 0 whole == Ok [m1; m2; m3; m4; m5])
#pop-options

(* A record is the record of its fields. *)
[@@ noextract_to "FSharp"]
let lrec_eta (#op: Type) (r: lrec op)
  : Lemma (ensures ({ lseq = r.lseq; lactor = r.lactor; lop = r.lop; lprev = r.lprev; lhash = r.lhash } == r)) =
  match r with
  | Mklrec _ _ _ _ _ -> ()

#push-options "--fuel 2 --ifuel 2"
(* THEOREM 2's last clause and THEOREM 3's core: the line the writer spelled for a record parses,
   and `record_of` reads the record back — with whatever the witness it is read through says
   of the `op` span. *)
[@@ noextract_to "FSharp"]
let record_of_line (#op: Type) (w: witness op) (seq: int) (a: actor) (raw: list ch) (prev hash: list och)
  (o': op) (n: nat)
  : Lemma (requires actor_wf a /\ wf_str prev /\ wf_str hash /\ 0 - int32_min_mag <= seq /\ seq <= int32_max /\
                    member_value_ok raw /\ w.dec raw == Ok o')
          (ensures (match parse_line n (line_of seq a raw prev hash) with
                    | Ok line ->
                      line.lnum == n /\ not (is_snapshot_line line) /\
                      record_of (actor_field k_actor) w line ==
                        Ok ({ lseq = seq; lactor = a; lop = o'; lprev = prev; lhash = hash })
                    | Error _ -> False)) =
  line_members seq a raw prev hash;
  match parse_line n (line_of seq a raw prev hash) with
  | Error _ -> ()
  | Ok line ->
    read_int_show seq;
    (match member_of k_actor line.lfields with
     | Some (_, p) -> actor_field_reads a line p
     | None -> ());
    unquote_quote prev;
    unquote_quote hash
#pop-options

(* ---- 11i. the lines of a text, and the text of the lines ---- *)

#push-options "--fuel 2 --ifuel 2"
[@@ noextract_to "FSharp"]
let rec split_lines_clean (cur: list ch) (l: list ch) (s: list ch)
  : Lemma (requires clean l) (ensures split_lines cur (app l s) == split_lines (rev_app l cur) s) (decreases l) =
  match l with
  | [] -> ()
  | c :: t -> split_lines_clean (c :: cur) t s

[@@ noextract_to "FSharp"]
let rec split_join (ls: list (list ch))
  : Lemma (requires Cons? ls /\ all clean ls) (ensures split (join_sep CNewline ls) == ls) (decreases ls) =
  match ls with
  | [l] -> app_nil_r l; split_lines_clean [] l []; rev_rev_app l []
  | l :: rest -> split_join rest; split_lines_clean [] l (CNewline :: join_sep CNewline rest); rev_rev_app l []
#pop-options

#push-options "--fuel 16 --ifuel 2"
[@@ noextract_to "FSharp"]
let rec clean_quote (s: list och) : Lemma (requires wf_str s) (ensures clean (quote_body s)) (decreases s) =
  match s with
  | [] -> ()
  | OLit _ :: t -> clean_quote t
  | OEsc _ :: t -> clean_quote t
  | OUni a b _ _ :: t ->
    (match uni_class a b with
     | SNone -> clean_quote t
     | SLow -> ()
     | SHigh -> (match t with OUni _ _ _ _ :: t2 -> clean_quote t2 | _ -> ()))
#pop-options

[@@ noextract_to "FSharp"]
let rec clean_digits (s: list ch) : Lemma (requires all is_digit s) (ensures clean s) (decreases s) =
  match s with
  | [] -> ()
  | _ :: t -> clean_digits t

#push-options "--fuel 4 --ifuel 2"
[@@ noextract_to "FSharp"]
let clean_quoted (s: list och) : Lemma (requires wf_str s) (ensures clean (quote_str s)) =
  clean_quote s; all_app (fun c -> c <> CNewline && c <> CReturn) (quote_body s) [CQuote]

[@@ noextract_to "FSharp"]
let clean_member (k v: list ch) : Lemma (requires clean k /\ clean v) (ensures clean (member_text k v)) =
  all_app (fun c -> c <> CNewline && c <> CReturn) k (CQuote :: CColon :: v)

[@@ noextract_to "FSharp"]
let clean_show (s: int) : Lemma (ensures clean (show_int s)) =
  let n: nat = (if s < 0 then 0 - s else s) in
  digits_all n; clean_digits (digits_of_nat n)

[@@ noextract_to "FSharp"]
let keys_clean (_: unit)
  : Lemma (ensures clean k_seq /\ clean k_actor /\ clean k_op /\ clean k_prev_hash /\ clean k_hash /\
                   clean k_kind /\ clean k_id /\ clean k_model /\ clean k_version) =
  assert_norm (clean k_seq /\ clean k_actor /\ clean k_op /\ clean k_prev_hash /\ clean k_hash /\
               clean k_kind /\ clean k_id /\ clean k_model /\ clean k_version)

[@@ noextract_to "FSharp"]
let clean_actor (a: actor) : Lemma (requires actor_wf a) (ensures clean (encode_actor a)) =
  keys_clean (); keys_plain ();
  let p = (fun c -> c <> CNewline && c <> CReturn) in
  match a with
  | AHuman id ->
    let t1 = member_text k_kind (quote_str (olit lit_human)) in
    let t2 = member_text k_id (quote_str id) in
    clean_quoted (olit lit_human); clean_quoted id;
    clean_member k_kind (quote_str (olit lit_human)); clean_member k_id (quote_str id);
    all_app p t1 (CComma :: t2); all_app p (app t1 (CComma :: t2)) [CRBrace]
  | AAgent model version id ->
    let t1 = member_text k_kind (quote_str (olit lit_agent)) in
    let t2 = member_text k_model (quote_str model) in
    let t3 = member_text k_version (quote_str version) in
    let t4 = member_text k_id (quote_str id) in
    clean_quoted (olit lit_agent); clean_quoted model; clean_quoted version; clean_quoted id;
    clean_member k_kind (quote_str (olit lit_agent)); clean_member k_model (quote_str model);
    clean_member k_version (quote_str version); clean_member k_id (quote_str id);
    all_app p t3 (CComma :: t4);
    all_app p t2 (CComma :: app t3 (CComma :: t4));
    all_app p t1 (CComma :: app t2 (CComma :: app t3 (CComma :: t4)));
    all_app p (app t1 (CComma :: app t2 (CComma :: app t3 (CComma :: t4)))) [CRBrace]

#push-options "--fuel 8 --ifuel 2"
[@@ noextract_to "FSharp"]
let clean_line (seq: int) (a: actor) (raw: list ch) (prev hash: list och)
  : Lemma (requires actor_wf a /\ wf_str prev /\ wf_str hash /\ clean raw)
          (ensures clean (line_of seq a raw prev hash)) =
  keys_clean ();
  let p = (fun c -> c <> CNewline && c <> CReturn) in
  let t1 = member_text k_seq (show_int seq) in
  let t2 = member_text k_actor (encode_actor a) in
  let t3 = member_text k_op raw in
  let t4 = member_text k_prev_hash (quote_str prev) in
  let t5 = member_text k_hash (quote_str hash) in
  clean_show seq; clean_actor a; clean_quoted prev; clean_quoted hash;
  clean_member k_seq (show_int seq); clean_member k_actor (encode_actor a); clean_member k_op raw;
  clean_member k_prev_hash (quote_str prev); clean_member k_hash (quote_str hash);
  all_app p t4 (CComma :: t5);
  all_app p t3 (CComma :: app t4 (CComma :: t5));
  all_app p t2 (CComma :: app t3 (CComma :: app t4 (CComma :: t5)));
  all_app p t1 (CComma :: app t2 (CComma :: app t3 (CComma :: app t4 (CComma :: t5))));
  all_app p (app t1 (CComma :: app t2 (CComma :: app t3 (CComma :: app t4 (CComma :: t5))))) [CRBrace]
#pop-options
#pop-options

(* ---- 11j. the stream: written, scanned, settled, verified ---- *)

(* THE PREMISES a domain owes (named in `../proofs.json`): its witness reads back what it writes,
   and its records are well formed — an actor that names somebody, strings the writer can spell,
   a sequence number in Int32. *)
[@@ noextract_to "FSharp"]
let witness_rt (#op: Type) (w: witness op) : Tot prop =
  forall (o: op). {:pattern (w.enc o)} w.dec (w.enc o) == Ok o

[@@ noextract_to "FSharp"]
let rec_wf (#op: Type) (r: lrec op) : Tot bool =
  actor_wf r.lactor && wf_str r.lprev && wf_str r.lhash &&
  0 - int32_min_mag <= r.lseq && r.lseq <= int32_max

[@@ noextract_to "FSharp"]
let records_wf (#op: Type) (rs: list (lrec op)) : Tot bool = all rec_wf rs

(* The record the carrying witness reads for a written one: its op beside its text. *)
[@@ noextract_to "FSharp"]
let carry_rec (#op: Type) (w: witness op) (r: lrec op) : Tot (lrec (carried op)) =
  { lseq = r.lseq; lactor = r.lactor; lop = (w.enc r.lop, Ok r.lop); lprev = r.lprev; lhash = r.lhash }

[@@ noextract_to "FSharp"]
let rec items_of (#op: Type) (w: witness op) (k: nat) (rs: list (lrec op)) : Tot (list (item (carried op))) (decreases rs) =
  match rs with
  | [] -> []
  | r :: t -> Rec (k + 1) (carry_rec w r) :: items_of w (k + 1) t

[@@ noextract_to "FSharp"]
let rec reads_of (#op: Type) (w: witness op) (k: nat) (rs: list (lrec op)) : Tot (list (nat & lrec (carried op))) (decreases rs) =
  match rs with
  | [] -> []
  | r :: t -> (k + 1, carry_rec w r) :: reads_of w (k + 1) t

[@@ noextract_to "FSharp"]
let rec carried_list (#op: Type) (w: witness op) (rs: list (lrec op)) : Tot (list (lrec (carried op))) (decreases rs) =
  match rs with
  | [] -> []
  | r :: t -> carry_rec w r :: carried_list w t

[@@ noextract_to "FSharp"]
let rec oks_list (#op: Type) (rs: list (lrec op)) : Tot (list (res (lrec op) usite)) (decreases rs) =
  match rs with
  | [] -> []
  | r :: t -> Ok r :: oks_list t

#push-options "--fuel 2 --ifuel 2"
(* The checked writer's lines are the record lines, every one clean. *)
[@@ noextract_to "FSharp"]
let rec checked_lines_ok (#op: Type) (w: witness op) (k: nat) (rs: list (lrec op)) (ls: list (list ch))
  : Lemma (requires records_wf rs /\ checked_lines w k rs == Ok ls)
          (ensures all clean ls /\ len ls == len rs)
          (decreases rs) =
  match rs with
  | [] -> ()
  | r :: rest ->
    (match check_raw (w.enc r.lop) with
     | Error _ -> ()
     | Ok _ ->
       check_raw_ok (w.enc r.lop);
       clean_line r.lseq r.lactor (w.enc r.lop) r.lprev r.lhash;
       (match checked_lines w (k + 1) rest with
        | Error _ -> ()
        | Ok ls' -> checked_lines_ok w (k + 1) rest ls'))

(* The scan of the checked writer's lines is one item per record, numbered by its line. *)
[@@ noextract_to "FSharp"]
let rec scan_written (#op: Type) (ws: ch -> bool) (w: witness op) (k: nat) (rs: list (lrec op))
  (ls: list (list ch)) (acc: list (item (carried op)))
  : Lemma (requires witness_rt w /\ records_wf rs /\ checked_lines w k rs == Ok ls)
          (ensures scan_from ws (line_item (actor_field k_actor) (carry w)) k ls acc == Ok (rev_app acc (items_of w k rs)))
          (decreases rs) =
  match rs with
  | [] -> ()
  | r :: rest ->
    let span = w.enc r.lop in
    (match check_raw span with
     | Error _ -> ()
     | Ok _ ->
       (match checked_lines w (k + 1) rest with
        | Error _ -> ()
        | Ok ls' ->
          check_raw_ok span;
          assert (w.dec span == Ok r.lop);
          record_of_line (carry w) r.lseq r.lactor span r.lprev r.lhash (span, Ok r.lop) (k + 1);
          (match parse_line (k + 1) (record_line r span) with
           | Error _ -> ()
           | Ok line ->
             assert (record_of (actor_field k_actor) (carry w) line == Ok (carry_rec w r));
             scan_written ws w (k + 1) rest ls' (Rec (k + 1) (carry_rec w r) :: acc))))

[@@ noextract_to "FSharp"]
let rec settle_written (#op: Type) (w: witness op) (first: bool) (recs: list (nat & lrec (carried op)))
  (snaps: list (list ch)) (k: nat) (rs: list (lrec op))
  : Lemma (ensures settle_items first recs snaps (items_of w k rs) == Ok (rev_app recs (reads_of w k rs), rev snaps))
          (decreases rs) =
  match rs with
  | [] -> ()
  | r :: rest -> settle_written w false ((k + 1, carry_rec w r) :: recs) snaps (k + 1) rest

[@@ noextract_to "FSharp"]
let rec decoded_written (#op: Type) (w: witness op) (k: nat) (rs: list (lrec op))
  : Lemma (ensures decoded_of (reads_of w k rs) == oks_list rs) (decreases rs) =
  match rs with
  | [] -> ()
  | r :: rest -> lrec_eta r; decoded_written w (k + 1) rest

[@@ noextract_to "FSharp"]
let rec oks_settle (#op: Type) (rs: list (lrec op))
  : Lemma (ensures sites_of (oks_list rs) == [] /\ oks_of (oks_list rs) == rs) (decreases rs) =
  match rs with
  | [] -> ()
  | _ :: rest -> oks_settle rest

[@@ noextract_to "FSharp"]
let rec snd_reads (#op: Type) (w: witness op) (k: nat) (rs: list (lrec op))
  : Lemma (ensures snd_all (reads_of w k rs) == carried_list w rs /\ len (carried_list w rs) == len rs) (decreases rs) =
  match rs with
  | [] -> ()
  | _ :: rest -> snd_reads w (k + 1) rest

(* The carried scan of what the checked writer wrote: every record, carried, with no snapshot. *)
[@@ noextract_to "FSharp"]
let read_written (#op: Type) (#brk: Type) (ws: ch -> bool) (w: witness op) (rs: list (lrec op)) (text: list ch)
  : Lemma (requires witness_rt w /\ records_wf rs /\ try_to_jsonl w rs == Ok text)
          (ensures read_carried #op #brk ws (actor_field k_actor) w text == Ok (reads_of w 0 rs, [])) =
  match checked_lines w 0 rs with
  | Error _ -> ()
  | Ok ls ->
    checked_lines_ok w 0 rs ls;
    (match rs with
     | [] -> ()
     | _ ->
       split_join ls;
       scan_written ws w 0 rs ls [];
       settle_written w true [] [] 0 rs)

(* THEOREM 3. Loading what the checked writer wrote yields the records that were written. *)
[@@ noextract_to "FSharp"]
let round_trip (#op: Type) (ws: ch -> bool) (w: witness op) (rs: list (lrec op)) (text: list ch)
  : Lemma (requires witness_rt w /\ records_wf rs /\ try_to_jsonl w rs == Ok text)
          (ensures from_jsonl_with_snapshots ws w text == Ok (rs, []) /\ from_jsonl ws w text == Ok rs) =
  read_written #op #C.cbreak ws w rs text;
  decoded_written w 0 rs;
  oks_settle rs
#pop-options

(* The chain walk over carried records is the walk over the records they carry: the two agree
   on every field the walk reads, and the carrying witness re-encodes a decoded op by the real
   one. *)
#push-options "--fuel 2 --ifuel 2"
[@@ noextract_to "FSharp"]
let rec walk_carried (#op: eqtype) (h: string -> string -> string) (w: witness op) (qh: string -> string)
  (ren: list och -> string) (n: nat) (prev: string) (i: C.pos) (rs: list (lrec op))
  : Lemma (ensures C.first_chain_break_from h show_pos (enc_carried w) prev i (chain_records_from qh ren n (carried_list w rs)) ==
                   C.first_chain_break_from h show_pos (fun o -> chs_str (w.enc o)) prev i (chain_records_from qh ren n rs))
          (decreases rs) =
  match rs with
  | [] -> ()
  | r :: rest ->
    let c = chain_record qh ren n (carry_rec w r) in
    let o = chain_record qh ren n r in
    assert (c.C.rseq == o.C.rseq /\ c.C.ractor == o.C.ractor /\ c.C.rprev == o.C.rprev /\ c.C.rhash == o.C.rhash);
    assert (enc_carried w c.C.rop == chs_str (w.enc o.C.rop));
    walk_carried h w qh ren n (ren r.lhash) (C.PSucc i) rest

(* THEOREM 3, verified: the verified load answers the written records, or the break the chain
   walk reports of those very records — so `verify` over the loaded stream is `verify` over the
   original. *)
[@@ noextract_to "FSharp"]
let verified_round_trip (#op: eqtype) (h: string -> string -> string) (qh: string -> string)
  (ren: list och -> string) (ws: ch -> bool) (w: witness op) (rs: list (lrec op)) (text: list ch)
  : Lemma (requires witness_rt w /\ records_wf rs /\ try_to_jsonl w rs == Ok text)
          (ensures from_jsonl_verified h qh ren ws w text ==
                   (match C.first_chain_break h show_pos (fun o -> chs_str (w.enc o)) genesis (chain_records qh ren rs) with
                    | C.Found b -> Error (LBroken b)
                    | C.Missing -> Ok rs)) =
  read_written #op #C.cbreak ws w rs text;
  snd_reads w 0 rs;
  walk_carried h w qh ren (len rs) genesis C.PZero rs;
  decoded_written w 0 rs;
  oks_settle rs

[@@ noextract_to "FSharp"]
let verify_preserved (#op: eqtype) (h: string -> string -> string) (qh: string -> string)
  (ren: list och -> string) (ws: ch -> bool) (w: witness op) (rs: list (lrec op)) (text: list ch)
  : Lemma (requires witness_rt w /\ records_wf rs /\ try_to_jsonl w rs == Ok text)
          (ensures (match from_jsonl ws w text with
                    | Ok loaded ->
                      C.verify_chain h show_pos (fun o -> chs_str (w.enc o)) genesis (chain_records qh ren loaded) ==
                      C.verify_chain h show_pos (fun o -> chs_str (w.enc o)) genesis (chain_records qh ren rs)
                    | Error _ -> False)) =
  round_trip ws w rs text
#pop-options

(* ======================================================================================
   12. Twin evaluation (Phase 309) — fixtures the normaliser evaluates at the check and the
       oracle host re-evaluates on the extracted F#. Each applies a function above to a concrete
       input and compares with the value the model means there.
   ====================================================================================== *)

noeq type twin = { tname : string; tholds : unit -> bool }

let rec twins_hold (l: list twin) : Tot bool =
  match l with
  | [] -> true
  | t :: r -> t.tholds () && twins_hold r

(* `{"a":1}` *)
let twin_line1: list ch = [CLBrace; CQuote; CLa; CQuote; CColon; CD1; CRBrace]
(* `{"a":` *)
let twin_cut: list ch = [CLBrace; CQuote; CLa; CQuote; CColon]
(* `{"a":["x", "y"]}` *)
let twin_arr: list ch =
  [CLBrace; CQuote; CLa; CQuote; CColon; CLBrack; CQuote; COther "x"; CQuote; CComma; CSpace;
   CQuote; COther "y"; CQuote; CRBrack; CRBrace]
(* `{"actor":{"kind":"human","id":"u"}}` *)
let twin_actor_line: list ch =
  obj_text [member_text k_actor (encode_actor (AHuman [OLit CLu]))]

let twin_w: witness (list ch) = { enc = (fun o -> o); dec = (fun raw -> Ok raw) }
let twin_ws (_: ch) : Tot bool = false
let twin_rec: lrec (list ch) =
  { lseq = 0; lactor = AHuman [OLit CLu]; lop = [CLBrace; CRBrace]; lprev = []; lhash = [OLit CLa; OEsc CLn] }

(* The fixtures that read a parsed line are top-level readers rather than matches inside the
   list: the backend lays a `match` nested in a record literal out at column one, which F# does
   not parse (kit README, twin evaluation). *)
let twin_strings (_: unit) : Tot bool =
  match parse_line 1 twin_arr with
  | Ok l -> strings_field [CLa] l = Ok [ [OLit (COther "x")]; [OLit (COther "y")] ]
  | Error _ -> false

let twin_human (_: unit) : Tot bool =
  match parse_line 1 twin_actor_line with
  | Ok l -> actor_field k_actor l = Ok (AHuman [OLit CLu])
  | Error _ -> false

let twin_round_trip (_: unit) : Tot bool =
  match try_to_jsonl twin_w [twin_rec] with
  | Ok text -> from_jsonl twin_ws twin_w text = Ok [twin_rec]
  | Error _ -> false

let twin_second_line (_: unit) : Tot bool =
  match scan_records twin_ws (fun l -> Ok l.lnum) (app twin_line1 (CNewline :: twin_cut)) with
  | Error f -> f.fline = 2 && f.freason = Truncated
  | Ok _ -> false

let twins : list twin = [
  { tname = "parse-line-reads-one-member-at-its-position";
    tholds = (fun () ->
      parse_line 1 twin_line1
      = Ok ({ lnum = 1; ltext = twin_line1; lfields = [ { mkey = [OLit CLa]; mspan = [CD1]; mpos = 5 } ] })) };
  { tname = "a-truncated-line-is-refused-past-its-end";
    tholds = (fun () -> parse_line 3 twin_cut = Error ({ fline = 3; fpos = 5; freason = Truncated })) };
  { tname = "unquote-reads-back-what-quote-wrote";
    tholds = (fun () -> unquote (quote_str [OLit CLa; OEsc CLn; OUni CD0 CD0 CD0 CD1]) = Ok [OLit CLa; OEsc CLn; OUni CD0 CD0 CD0 CD1]) };
  { tname = "read-int-reads-a-negative-numeral";
    tholds = (fun () -> read_int [CMinus; CD4; CD2] = Some (0 - 42)) };
  { tname = "read-int-refuses-a-leading-zero";
    tholds = (fun () -> read_int [CD0; CD7] = None) };
  { tname = "check-raw-admits-an-object-and-refuses-trailing-space";
    tholds = (fun () -> check_raw [CLBrace; CRBrace] = Ok () && check_raw [CD1; CSpace] = Error NotTrimStable) };
  { tname = "split-lines-normalises-crlf";
    tholds = (fun () -> split [CLa; CReturn; CNewline; CLb] = [ [CLa]; [CLb] ]) };
  { tname = "strings-field-reads-an-array-of-strings"; tholds = twin_strings };
  { tname = "actor-field-reads-a-human"; tholds = twin_human };
  { tname = "from-jsonl-loads-what-try-to-jsonl-wrote"; tholds = twin_round_trip };
  { tname = "a-fault-on-the-second-line-names-it"; tholds = twin_second_line } ]

let _ = assert_norm (twins_hold twins == true)
