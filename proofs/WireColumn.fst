(*
   WireColumn — an F* model of the COLUMNAR CODEC, with its image and its round trip as machine-checked
   theorems (fuaran-core Phase 306).

   WHAT IS MODELLED. `src/Fuaran.Core.Column/Column.fs`, clause for clause: `ColumnType.tag` /
   `ofTag` / `widens`, `Cell.typeOf`, `DecimalText.tryCanonical` / `isCanonical`, `Table.validate`
   with its private `firstUncarriableCell`, and `ColumnCodec.encodeJson` / `decodeJson` /
   `tryEncode` with every private function those three reach (`absentSlot`, `cellJson`,
   `columnJson`, `schemaJson`, `decodeCell`, `decodeSchemaEntry`, `decodeSchema`, `columnParts`,
   `decodeColumn`, `uniqueColumnKeys`). The level is the `JVal`: `encodeJson` builds one and
   `decodeJson` reads one, and everything the codec decides it decides there. The strings either
   side of it are `Canon.render` and the parser, which `WireCanon.fst` and `JsonParse.fst` already
   model; section 9 says exactly how far this module's theorems compose with those, and where they
   stop.

   WHY THE PHASE EXISTS. The source makes two promises in doc comments. `Table.validate`: "Over
   what it accepts, `ColumnCodec.tryEncode` is exactly `Ok (encode src)` … and `ColumnCodec.decode`
   ends in it, so a table that encodes is a table that decodes." And the codec law the suite
   samples: a table that encodes comes back. A law samples; a quantifier does not. Two statements
   close the gap:

     - THE IMAGE (`decode_image_is_valid`, `encode_image_revalidates`). Whatever `decode_json`
       answers `Embedded t` for is a table `validate` accepts. Read off the last line of
       production's decode that is true by construction, so the content worth proving is the half
       that is NOT: the table decode REBUILDS from the encoding of a validated table — schema
       order, schema names, widened cells — is itself one `validate` accepts. Without that the
       final `validate` could refuse the encoder's own output and the round trip would be an
       error.
     - THE ROUND TRIP (`round_trip`). For every table `validate` accepts,
       `decode_json (encode_json (Embedded t)) == Good (Embedded (normal_table t))`.

   THE LITERAL ROUND TRIP IS FALSE, AND THE THEOREM SAYS WHAT IS TRUE INSTEAD. `decode (encode t)
   = Ok t` does not hold of the shipped codec, three ways, each proved in section 8 as a refutation
   rather than described:

     1. `literal_round_trip_fails_on_a_widened_float` — an `Int` cell in a `FloatType` column is a
        cell `validate` accepts (`ColumnType.widens`), is written as a JSON float, and is read
        back as a `Float`. The value is the same number; the `Cell` is a different case.
     2. `literal_round_trip_fails_on_a_widened_decimal` — the same for a `DecimalType` column: the
        `Int` is written as the string of its digits and read back as a `Decimal`.
     3. `literal_round_trip_fails_on_column_order` — `validate` checks that the schema's names and
        the columns' names are the same SET; it never checks that they are in the same ORDER. The
        encoder walks the schema and the decoder rebuilds in schema order, so a table whose
        `Columns` list is in another order comes back reordered.

   So the right-hand side is a NORMAL FORM: `normal_table` puts the columns in schema order and
   widens each `Int` into its column's type. It is what decode produces, it is a fixed point
   (`normal_table_is_idempotent`), and from the first decode on the round trip IS the literal one
   (`round_trip_is_exact_on_a_normal_table`, `second_round_trip_is_exact`). A `Ref` source
   round-trips exactly and unconditionally (`ref_round_trip`).

   AND THE LITERAL FORM IS TRUE OF EVERYTHING THE DECODER PRODUCES. Every table `decode_json`
   answers, for any document, is already its own normal form (`decoded_table_is_normal`), so a
   DECODED value round-trips exactly (`decoded_round_trip_is_exact`). That is the check a corpus
   runner makes of a round-trip case — decode the document, then round-trip what came back — and
   it is why a sampled law that only ever builds tables in schema order with no cell to widen
   passes while asserting the literal form: it is sampling inside the normal form.

   WHAT DECODE ACCEPTS THAT ENCODE NEVER WRITES. The converse direction — `encode (decode d) = d` —
   is not claimed and is false; section 8 exhibits why, because each is a fact about the shipped
   decoder worth a reader's attention: a `columns` member the schema does not name is read past —
   the must-ignore rule `decodeJson`'s doc comment states, where `validate` REFUSES the table that
   document would have meant (`decode_drops_a_column_outside_the_schema`,
   `validate_refuses_a_column_outside_the_schema`); a masked slot is never read, so any value may
   sit in it (`decode_never_reads_a_masked_slot`); under an empty schema `columns` need not be an
   object at all (`decode_never_reads_columns_under_an_empty_schema`); and a surplus root member
   is read past unless it is called `ref`, which wins over `columns`
   (`decode_ignores_a_surplus_root_member`, `ref_wins_over_columns`).

   WHAT IS NOT MODELLED, AND WHY THAT DOES NOT WEAKEN THE TWO THEOREMS.

     - THE LENIENT-INGEST ARMS. `decodeJson` reads five shapes the encoder never writes: a
       document with no `schema` (types inferred per `inferColumnType`), a column that is a bare
       array (`columnParts`), a wrapped column with no `validity` mask, an epoch NUMBER in a
       timestamp column (`epochToIso` / `isoOfEpochSeconds`), and a whole-valued JSON FLOAT in a
       decimal column. Where production enters one of those arms the model answers
       `Bad OutOfModel` — a verdict of the MODEL, not a refusal of the codec — so a differential
       over the model must skip exactly those documents. The theorems lose nothing by it: both are
       about the image of `encode_json`, which always writes the schema, always writes the wrapped
       `{values, validity}` form, writes a timestamp and a decimal as a string, and so never
       reaches an arm that answers `OutOfModel`. `round_trip` concluding `Good` is itself the proof
       of that. `decode_image_is_valid` holds for every input because `OutOfModel` is a `Bad`.
       One consequence is stated rather than left to be found: the model's float arm in a
       timestamp and a decimal column is `OutOfModel` WHICHEVER way production's guard falls (the
       guard is float arithmetic the model does not carry), so a fractional float there, which
       production refuses as a `TypeMismatch`, is also outside the differential.

     - THE NUMBERS ARE OPAQUE, as they are in `WireCanon.fst` and for the same reason: `jval` is
       parametric in the int and float carriers, and what a host computes about a number is a
       field of the `host` record — `to_float` (F#: `float i`), `int_text` (F#: `string i`),
       `finite` (F#: `JVal.nonFiniteToken f = None`). The two facts the codec needs about them are
       NAMED HYPOTHESES of the theorems, never assumptions of the module:
         * `int_text_canonical` — the decimal text of an int is canonical decimal text. It is what
           makes an `Int` in a decimal column decode at all.
         * `int_floats_finite` — the float of an int is finite. It is what lets the widened cell
           through the `validate` decode ends in.
       Both are facts about .NET's `Int32`, true of every one of its values, and sampled by the
       differential rather than proved here. Section 8 shows each is NECESSARY, not merely
       convenient: a host where one fails is a host where the round trip is an error.

     - THE CALENDAR. `TemporalText.isCanonicalDate` / `isCanonicalTimestamp` are fields of the
       `host` record, `is_date` and `is_timestamp`, and not modelled: digit arithmetic over a
       Gregorian calendar is exactly what the theorems do not need. What they need is that
       `validate` and `decodeCell` ask the SAME predicate of the same text, and that is a fact
       about which function each calls — which the model reproduces — not about what the function
       computes.

     - THE DECIMAL CANONICALISER IS MODELLED, not abstracted, because it is trimming and no
       arithmetic: `parts`, `render` and `tryCanonical` clause for clause over the character
       alphabet `WireCanon` already has. `DecimalText.compare` / `add` / `tryToFloat` are the
       aggregate's, not the codec's, and are not here.

     - `Column.aggregate`, `Schema.diff` / `classify` / `fingerprint`, `errorString`. None is on
       the path between a table and its encoding.

     - ERROR PROSE. `ColumnError`'s cases are kept and their free-text details are not: a
       `MalformedShape` here carries no message, a `TypeMismatch` its column and the expected
       type, a `LengthMismatch` and a `RaggedColumns` their column and not the two counts. The
       theorems are about the `Good` path; the classes are kept so a differential can compare
       WHICH refusal, first found in production's order, and the counts are dropped because the
       extraction carries no integers (README, finding 2).

     - THE ACCUMULATE-AND-REVERSE LOOPS. Production's three `go acc` loops (`decodeSchema`,
       `decodeColumn`, `decodeJson`) build a reversed list and reverse it on success. The model
       writes each as the direct recursion it is equal to: the first error in list order wins in
       both, and the success value is the list in order. That equality is sampled by the
       differential, not proved — it is a claim about `List.rev`, not about the codec.

     - SETS ARE LISTS. `Table.firstDuplicate` threads an F# `Set<string>`; the model threads a
       list and asks `mem`. The standing `sets-are-lists` bridge, used at membership only.

   WHY `WireCanon` IS OPENED. For `jval`, `ch`, `mem`, `app` and `keys_of` — so that the value this
   codec builds is the SAME type the canonical renderer is proved about, and section 9's
   composition is a statement rather than a coercion. It is the choice `WireVersioning.fst` makes
   and for its reason. Of `WireCanon`'s PROOFS, section 9 takes `read_render` and
   `normalise_keeps_keys`, and section 2 takes the one list fact `app_nil`; nothing else.

   WHY THE MODULE IS `WireColumn` AND NOT `Column`. The reason `WireVersioning.fst` gives for its
   own prefix: the extracted oracle is a TOP-LEVEL F# module, and the differential host opens
   `Fuaran.Core`, which already carries a type AND a module called `Column`. The two would shadow
   each other exactly where the differential needs both in one expression. The phase's shard names
   the unprefixed file; this paragraph is the correction.

   HOW TO READ IT. Every definition names its F# counterpart in the comment above it. Sections 1-5
   are the model (types, decimal text, validate, encode, decode), 6 the normal form and the two
   named hypotheses, 7 the two theorems and their corollaries, 8 the refutations, the necessity
   of the hypotheses and the decode-only acceptances, 9 the join to the canonical renderer. No
   `assume`, no `admit`; every fuel setting is scoped to the computations that need it.

   Apache-2.0, like everything beside it.
*)
module WireColumn

(* Opening `WireCanon` brings its whole context into every query here, and this module's queries
   are list inductions that need none of it. Pruned for the reason `WireVersioning.fst` gives at
   the same line, and scoped here rather than in the leg's flags for the reason `WireCanon.fst`
   gives at its. *)
#set-options "--ext context_pruning"

open WireCanon

(* ======================================================================================
   1. THE TYPES (F#: `ColumnType`, `Cell`, `Column`, `Schema`, `Table`, `DataSource`,
      `ColumnError`), and what a host computes about a number.
   ====================================================================================== *)

(* F#: `ColumnType`, in declaration order. *)
type column_type =
  | IntType
  | FloatType
  | BoolType
  | StringType
  | DateType
  | TimestampType
  | DecimalType

(* F#: `Cell`, in declaration order — `Decimal` after `Null`, as production has it so that every
   case published before it kept its tag. Text is a character list, as it is everywhere in
   `WireCanon`. *)
type cell (num flt: eqtype) =
  | Int       : i:num -> cell num flt
  | Float     : f:flt -> cell num flt
  | Bool      : b:bool -> cell num flt
  | Str       : s:list ch -> cell num flt
  | Date      : s:list ch -> cell num flt
  | Timestamp : s:list ch -> cell num flt
  | Null      : cell num flt
  | Decimal   : s:list ch -> cell num flt

(* F#: `{ Name: string; Type: ColumnType; Cells: Cell list }`. `type` is a keyword here. *)
type column (num flt: eqtype) = {
  name: list ch;
  ctype: column_type;
  cells: list (cell num flt);
}

(* F#: `{ Schema: Schema; Columns: Column list }`, with `Schema = (string * ColumnType) list`. *)
type table (num flt: eqtype) = {
  schema: list (list ch & column_type);
  columns: list (column num flt);
}

(* F#: `DataSource`. *)
type data_source (num flt: eqtype) =
  | Embedded : t:table num flt -> data_source num flt
  | Ref      : r:list ch -> data_source num flt

(* F#: the five details `Table.validate` and `uniqueColumnKeys` spell inside a `Malformed`. The
   prose is dropped and the fault is kept, with the name production puts in the message where it
   names one. *)
type table_fault =
  | DuplicateSchemaName : n:list ch -> table_fault
  | DuplicateColumnName : n:list ch -> table_fault
  | SchemaNameWithoutColumn
  | ColumnOutsideSchema
  | DuplicateColumnKey  : k:list ch -> table_fault

(* F#: `ColumnError`, less `NotJson` (the string level, which this module does not model) and less
   each case's prose — see the header. `MissingColumn n` is production's
   `MissingField ("columns." + n)`, kept apart so the model need not spell a concatenation.
   `OutOfModel` is NOT a production case: it is the model's verdict on a document that enters a
   lenient-ingest arm, and a differential skips every document it is answered for. *)
type column_error =
  | MissingField   : field:list ch -> column_error
  | MissingColumn  : col:list ch -> column_error
  | MalformedShape : column_error
  | UnknownType    : got:list ch -> column_error
  | TypeMismatch   : col:list ch -> expected:column_type -> column_error
  | LengthMismatch : col:list ch -> column_error
  | NonFiniteFloat : col:list ch -> column_error
  | Malformed      : fault:table_fault -> column_error
  | RaggedColumns  : col:list ch -> column_error
  | OutOfModel     : column_error

(* F#: `Result<'T, ColumnError>`. Not `WireCanon.outcome`, whose error is a string. *)
type res (a: Type) =
  | Good : v:a -> res a
  | Bad  : e:column_error -> res a

(* What the HOST computes about a number and about temporal text — every fact the codec reads and
   the model does not derive. See the header for why each is a parameter. *)
noeq
type host (num flt: eqtype) = {
  (* F#: `float i`. The `Int -> Float` widening. *)
  to_float     : num -> flt;
  (* F#: `string i`. The `Int -> Decimal` widening. The same function `WireCanon.wire`'s `int_str`
     names; a second field here only so that this record stands without a comparator and a reader
     it has no use for. *)
  int_text     : num -> list ch;
  (* F#: `JVal.nonFiniteToken f = None`. *)
  finite       : flt -> bool;
  (* F#: the literals `0` and `0.0` of `absentSlot`. *)
  zero_int     : num;
  zero_float   : flt;
  (* F#: `TemporalText.isCanonicalDate` / `isCanonicalTimestamp`. *)
  is_date      : list ch -> bool;
  is_timestamp : list ch -> bool;
}

(* ---- the member names and type tags the codec spells, in `WireCanon`'s alphabet ----

   Named constants for the reason `WireCanon`'s `true_chars` is one: a spelling the model must get
   exactly right is a definition, not a literal buried in a guard. `a`-`f` are hex-digit
   constructors and `u` is `CLu` in that alphabet, which is why these read oddly. *)

let schema_key : list ch =
  [CPlain "s"; CHexCh HDc; CPlain "h"; CHexCh HDe; CPlain "m"; CHexCh HDa]

let columns_key : list ch =
  [CHexCh HDc; CPlain "o"; CPlain "l"; CLu; CPlain "m"; CPlain "n"; CPlain "s"]

let ref_key : list ch = [CPlain "r"; CHexCh HDe; CHexCh HDf]

let name_key : list ch = [CPlain "n"; CHexCh HDa; CPlain "m"; CHexCh HDe]

let type_key : list ch = [CPlain "t"; CPlain "y"; CPlain "p"; CHexCh HDe]

let values_key : list ch =
  [CPlain "v"; CHexCh HDa; CPlain "l"; CLu; CHexCh HDe; CPlain "s"]

let validity_key : list ch =
  [CPlain "v"; CHexCh HDa; CPlain "l"; CPlain "i"; CHexCh HDd; CPlain "i"; CPlain "t"; CPlain "y"]

(* F#: `ColumnType.tag`. *)
let tag (t: column_type) : Tot (list ch) =
  match t with
  | IntType -> [CPlain "i"; CPlain "n"; CPlain "t"]
  | FloatType -> [CHexCh HDf; CPlain "l"; CPlain "o"; CHexCh HDa; CPlain "t"]
  | BoolType -> [CHexCh HDb; CPlain "o"; CPlain "o"; CPlain "l"]
  | StringType -> [CPlain "s"; CPlain "t"; CPlain "r"; CPlain "i"; CPlain "n"; CPlain "g"]
  | DateType -> [CHexCh HDd; CHexCh HDa; CPlain "t"; CHexCh HDe]
  | TimestampType ->
      [CPlain "t"; CPlain "i"; CPlain "m"; CHexCh HDe; CPlain "s"; CPlain "t"; CHexCh HDa;
       CPlain "m"; CPlain "p"]
  | DecimalType ->
      [CHexCh HDd; CHexCh HDe; CHexCh HDc; CPlain "i"; CPlain "m"; CHexCh HDa; CPlain "l"]

(* F#: `ColumnType.all`, in its order — which is `ofTag`'s search order. *)
let all_types : list column_type =
  [IntType; FloatType; BoolType; StringType; DateType; TimestampType; DecimalType]

(* F#: `List.tryFind (fun t -> tag t = s)`. *)
let rec find_tag (s: list ch) (ts: list column_type) : Tot (option column_type) (decreases ts) =
  match ts with
  | [] -> None
  | t :: rest -> if tag t = s then Some t else find_tag s rest

(* F#: `ColumnType.ofTag`. *)
let of_tag (s: list ch) : Tot (option column_type) = find_tag s all_types

(* F#: `ColumnType.widens`, clause for clause: the identity, `Int -> Float`, `Int -> Decimal`. *)
let widens (from target: column_type) : Tot bool =
  from = target
  || (from = IntType && target = FloatType)
  || (from = IntType && target = DecimalType)

(* F#: `Cell.typeOf`. *)
let type_of (#num #flt: eqtype) (c: cell num flt) : Tot (option column_type) =
  match c with
  | Int _ -> Some IntType
  | Float _ -> Some FloatType
  | Bool _ -> Some BoolType
  | Str _ -> Some StringType
  | Date _ -> Some DateType
  | Timestamp _ -> Some TimestampType
  | Decimal _ -> Some DecimalType
  | Null -> None

(* ======================================================================================
   2. DECIMAL TEXT (F#: `DecimalText.parts` / `render` / `tryCanonical` / `isCanonical` /
      `zero`).

      The grammar read is `-?[0-9]+(\.[0-9]+)?`; the form written has no leading zero, no
      trailing fraction zero, no point where the fraction is zero and no sign on zero. It is
      trimming over characters, so it is modelled rather than taken as a parameter.
   ====================================================================================== *)

let zero_ch : ch = CHexCh HD0

(* F#: `c >= '0' && c <= '9'`. *)
let is_digit (c: ch) : Tot bool =
  match c with
  | CHexCh d -> is_dec d
  | _ -> false

let rec all_digits (s: list ch) : Tot bool =
  match s with
  | [] -> true
  | c :: t -> is_digit c && all_digits t

(* F#: `isDigits` — non-empty and every character a digit. *)
let is_digits (s: list ch) : Tot bool = Cons? s && all_digits s

(* F#: `body.IndexOf '.'` with the two `Substring`s either side of it — `None` for `dot < 0`, and
   otherwise the text before the FIRST point and the text after it. *)
let rec split_dot (s: list ch) : Tot (option (list ch & list ch)) (decreases s) =
  match s with
  | [] -> None
  | c :: t ->
      if c = CDot then Some ([], t)
      else (match split_dot t with
            | None -> None
            | Some (before, after) -> Some (c :: before, after))

(* F#: `ip.TrimStart '0'`. *)
let rec trim_start (s: list ch) : Tot (list ch) (decreases s) =
  match s with
  | [] -> []
  | c :: t -> if c = zero_ch then trim_start t else s

(* F#: `fp.TrimEnd '0'`. *)
let rec trim_end (s: list ch) : Tot (list ch) (decreases s) =
  match s with
  | [] -> []
  | c :: t ->
      let r = trim_end t in
      if c = zero_ch && Nil? r then [] else c :: r

(* F#: the `(negative, ip, fp)` triple `parts` returns. A record rather than a tuple so the
   extraction names its fields. *)
type dparts = {
  negative: bool;
  ip: list ch;
  fp: list ch;
}

(* F#: `DecimalText.parts`, clause for clause. `isNull (box s) || s.Length = 0` is the empty list
   here — a character list has no null. *)
let parts (s: list ch) : Tot (option dparts) =
  match s with
  | [] -> None
  | c0 :: rest ->
      let neg = (c0 = CMinus) in
      let body = if neg then rest else s in
      (match split_dot body with
       | None ->
           if not (is_digits body) then None
           else
             let ip' = trim_start body in
             Some ({ negative = neg && Cons? ip'; ip = ip'; fp = [] })
       | Some (ip0, fp0) ->
           if not (is_digits ip0 && is_digits fp0) then None
           else
             let ip' = trim_start ip0 in
             let fp' = trim_end fp0 in
             let is_zero = Nil? ip' && Nil? fp' in
             Some ({ negative = neg && not is_zero; ip = ip'; fp = fp' }))

(* F#: `DecimalText.render`. *)
let render_parts (p: dparts) : Tot (list ch) =
  app (if p.negative then [CMinus] else [])
      (app (if Nil? p.ip then [zero_ch] else p.ip)
           (if Nil? p.fp then [] else CDot :: p.fp))

(* F#: `DecimalText.tryCanonical`. *)
let try_canonical (s: list ch) : Tot (option (list ch)) =
  match parts s with
  | None -> None
  | Some p -> Some (render_parts p)

(* F#: `DecimalText.isCanonical`. *)
let is_canonical (s: list ch) : Tot bool = try_canonical s = Some s

(* F#: `DecimalText.zero`. *)
let dec_zero : list ch = [zero_ch]

(* ---- the canonical form is a FIXED POINT of the canonicaliser ----

   `Cell`'s doc comment says a decimal is "carried as its CANONICAL text" and that "the codec does
   the same on decode". For that to mean anything, what `tryCanonical` WRITES must be text
   `isCanonical` ACCEPTS — otherwise the decoder would build a cell the `validate` it ends in
   refuses. It is a fact about trimming, and it is proved here rather than sampled. *)

[@@ noextract_to "FSharp"]
let rec trim_start_digits (s: list ch)
  : Lemma (requires all_digits s)
          (ensures all_digits (trim_start s) /\
                   (match trim_start s with
                    | [] -> True
                    | c :: _ -> c =!= zero_ch))
          (decreases s) =
  match s with
  | [] -> ()
  | c :: t -> if c = zero_ch then trim_start_digits t else ()

[@@ noextract_to "FSharp"]
let rec trim_end_digits (s: list ch)
  : Lemma (requires all_digits s)
          (ensures all_digits (trim_end s) /\ trim_end (trim_end s) == trim_end s)
          (decreases s) =
  match s with
  | [] -> ()
  | _ :: t -> trim_end_digits t

(* A run of digits carries no point, so the FIRST point of `digits ++ "." ++ fp` is that one. *)
[@@ noextract_to "FSharp"]
let rec split_dot_digits (d: list ch)
  : Lemma (requires all_digits d) (ensures None? (split_dot d)) (decreases d) =
  match d with
  | [] -> ()
  | _ :: t -> split_dot_digits t

[@@ noextract_to "FSharp"]
let rec split_dot_digits_dot (d fp: list ch)
  : Lemma (requires all_digits d) (ensures split_dot (app d (CDot :: fp)) == Some (d, fp))
          (decreases d) =
  match d with
  | [] -> ()
  | _ :: t -> split_dot_digits_dot t fp

(* PROOF-ONLY — what `parts` guarantees of the triple it returns: both digit strings are digits,
   the integer part has no leading zero, the fraction no trailing one, and zero carries no sign. *)
[@@ noextract_to "FSharp"]
let well_parted (p: dparts) : Tot bool =
  all_digits p.ip
  && (match p.ip with
      | [] -> true
      | c :: _ -> c <> zero_ch)
  && all_digits p.fp
  && trim_end p.fp = p.fp
  && (not p.negative || Cons? p.ip || Cons? p.fp)

[@@ noextract_to "FSharp"]
let parts_well_parted (s: list ch)
  : Lemma (requires Some? (parts s)) (ensures well_parted (Some?.v (parts s))) =
  match s with
  | [] -> ()
  | c0 :: rest ->
      let body = if c0 = CMinus then rest else s in
      (match split_dot body with
       | None -> trim_start_digits body
       | Some (ip0, fp0) -> trim_start_digits ip0; trim_end_digits fp0)

[@@ noextract_to "FSharp"]
let parts_of_render (p: dparts)
  : Lemma (requires well_parted p) (ensures parts (render_parts p) == Some p) =
  let ipz = if Nil? p.ip then [zero_ch] else p.ip in
  if Nil? p.fp then (app_nil ipz; split_dot_digits ipz)
  else split_dot_digits_dot ipz p.fp

(* THE FIXED POINT: whatever `tryCanonical` answers is itself canonical. *)
let try_canonical_is_idempotent (s: list ch)
  : Lemma (requires Some? (try_canonical s))
          (ensures is_canonical (Some?.v (try_canonical s))) =
  parts_well_parted s;
  parts_of_render (Some?.v (parts s))

(* F#: `DecimalText.zero` is canonical — so the absent slot of a decimal column is text the column
   would accept, which the date and timestamp columns' `""` is not. Nothing reads a masked slot
   (section 8), so neither matters to a decode; it is recorded because the two placeholders are
   not alike and a reader might assume they are. *)
let dec_zero_is_canonical () : Lemma (ensures is_canonical dec_zero) =
  assert_norm (is_canonical dec_zero)

(* ======================================================================================
   3. VALIDATE (F#: `Table.firstDuplicate`, `Table.firstUncarriableCell`, `Table.validate`).

      Every `List.map` / `filter` / `tryPick` / `tryFind` production composes is written out as
      the first-order recursion it is, so the prover sees a definition rather than a closure.
   ====================================================================================== *)

(* F#: `t.Schema |> List.map fst`. *)
let rec schema_names (s: list (list ch & column_type)) : Tot (list (list ch)) (decreases s) =
  match s with
  | [] -> []
  | (n, _) :: t -> n :: schema_names t

(* F#: `t.Columns |> List.map (fun c -> c.Name)`. *)
let rec column_names (#num #flt: eqtype) (cs: list (column num flt))
  : Tot (list (list ch)) (decreases cs) =
  match cs with
  | [] -> []
  | c :: t -> c.name :: column_names t

(* F#: `Table.firstDuplicate`'s inner `go`, with the `Set` a list (the header's sets-are-lists). *)
let rec first_dup_go (seen names: list (list ch)) : Tot (option (list ch)) (decreases names) =
  match names with
  | [] -> None
  | n :: rest -> if mem n seen then Some n else first_dup_go (n :: seen) rest

(* F#: `Table.firstDuplicate`. *)
let first_duplicate (names: list (list ch)) : Tot (option (list ch)) = first_dup_go [] names

(* F#: `names |> List.filter (fun n -> not (List.contains n against))` — `missing` and `extra`. *)
let rec not_in (names against: list (list ch)) : Tot (list (list ch)) (decreases names) =
  match names with
  | [] -> []
  | n :: rest -> if mem n against then not_in rest against else n :: not_in rest against

(* F#: `t.Columns |> List.tryFind (fun c -> c.Name = name)` — `Table.tryColumn`, and the lookup
   `validate` and `encodeJson` both write inline. The FIRST column of that name. *)
let rec find_column (#num #flt: eqtype) (n: list ch) (cs: list (column num flt))
  : Tot (option (column num flt)) (decreases cs) =
  match cs with
  | [] -> None
  | c :: t -> if c.name = n then Some c else find_column n t

(* F#: `validate`'s `typeFault` — schema order drives the check, and a schema name with no column
   is passed over (it was refused earlier, as `missing`). *)
let rec type_fault (#num #flt: eqtype) (s: list (list ch & column_type))
                   (cs: list (column num flt))
  : Tot (option column_error) (decreases s) =
  match s with
  | [] -> None
  | (n, ty) :: rest ->
      (match find_column n cs with
       | Some c -> if c.ctype <> ty then Some (TypeMismatch n ty) else type_fault rest cs
       | None -> type_fault rest cs)

(* F#: `List.length a = List.length b`, written structurally because the extraction carries no
   integers (README, finding 2). Production compares two counts; this compares the two lists the
   counts are of, and is true exactly when the counts are equal. *)
let rec same_len (#a #b: Type) (xs: list a) (ys: list b) : Tot bool (decreases xs) =
  match xs, ys with
  | [], [] -> true
  | _ :: xt, _ :: yt -> same_len xt yt
  | _ -> false

(* F#: `rest |> List.tryFind (fun c -> Column.length c <> len0) |> Option.map RaggedColumns`. *)
let rec first_ragged (#num #flt: eqtype) (cells0: list (cell num flt))
                     (rest: list (column num flt))
  : Tot (option column_error) (decreases rest) =
  match rest with
  | [] -> None
  | c :: t -> if same_len cells0 c.cells then first_ragged cells0 t else Some (RaggedColumns c.name)

(* F#: `validate`'s `ragged` — every column against the FIRST. *)
let ragged (#num #flt: eqtype) (cs: list (column num flt)) : Tot (option column_error) =
  match cs with
  | [] -> None
  | first :: rest -> first_ragged first.cells rest

(* F#: the body of `firstUncarriableCell`'s `tryPick`, for one cell, in its order: `Null` passes;
   a type that does not widen into the column's is a `TypeMismatch`; then a `Float` must be
   finite and a `Decimal` / `Date` / `Timestamp` must carry its canonical text. *)
let cell_fault (#num #flt: eqtype) (h: host num flt) (cname: list ch) (ty: column_type)
               (c: cell num flt)
  : Tot (option column_error) =
  match c with
  | Null -> None
  | _ ->
      let outside =
        (match type_of c with
         | Some t -> not (widens t ty)
         | None -> false) in
      if outside then Some (TypeMismatch cname ty)
      else
        (match c with
         | Float f -> if h.finite f then None else Some (NonFiniteFloat cname)
         | Decimal s -> if is_canonical s then None else Some MalformedShape
         | Date s -> if h.is_date s then None else Some MalformedShape
         | Timestamp s -> if h.is_timestamp s then None else Some MalformedShape
         | _ -> None)

(* F#: `firstUncarriableCell` — the first fault in row order. Taken over the column's three fields
   rather than the record, so that the lemmas below can speak of a cell list on its own. *)
let rec first_uncarriable (#num #flt: eqtype) (h: host num flt) (cname: list ch)
                          (ty: column_type) (cs: list (cell num flt))
  : Tot (option column_error) (decreases cs) =
  match cs with
  | [] -> None
  | c :: t ->
      (match cell_fault h cname ty c with
       | Some e -> Some e
       | None -> first_uncarriable h cname ty t)

(* F#: `validate`'s `cellFault` — schema order, then row order. *)
let rec cells_fault (#num #flt: eqtype) (h: host num flt) (s: list (list ch & column_type))
                    (cs: list (column num flt))
  : Tot (option column_error) (decreases s) =
  match s with
  | [] -> None
  | (n, _) :: rest ->
      (match find_column n cs with
       | Some c ->
           (match first_uncarriable h c.name c.ctype c.cells with
            | Some e -> Some e
            | None -> cells_fault h rest cs)
       | None -> cells_fault h rest cs)

(* F#: `Table.validate`, clause for clause and in its order (a)-(e). *)
let validate (#num #flt: eqtype) (h: host num flt) (t: table num flt) : Tot (res unit) =
  let sn = schema_names t.schema in
  let cn = column_names t.columns in
  match first_duplicate sn with
  | Some n -> Bad (Malformed (DuplicateSchemaName n))
  | None ->
      (match first_duplicate cn with
       | Some n -> Bad (Malformed (DuplicateColumnName n))
       | None ->
           if Cons? (not_in sn cn) then Bad (Malformed SchemaNameWithoutColumn)
           else if Cons? (not_in cn sn) then Bad (Malformed ColumnOutsideSchema)
           else
             (match type_fault t.schema t.columns with
              | Some e -> Bad e
              | None ->
                  (match ragged t.columns with
                   | Some e -> Bad e
                   | None ->
                       (match cells_fault h t.schema t.columns with
                        | Some e -> Bad e
                        | None -> Good ()))))

(* ======================================================================================
   4. ENCODE (F#: `ColumnCodec.absentSlot`, `cellJson`, `columnJson`, `schemaJson`,
      `encodeJson`, `tryEncode`).
   ====================================================================================== *)

(* F#: `absentSlot` — the value a `Null` cell's slot carries. A placeholder of the column's JSON
   kind and never a cell: the validity mask says the cell is absent. *)
let absent_slot (#num #flt: eqtype) (h: host num flt) (ty: column_type) : Tot (jval num flt) =
  match ty with
  | IntType -> JInt h.zero_int
  | FloatType -> JFloat h.zero_float
  | BoolType -> JBool false
  | StringType -> JStr []
  | DateType -> JStr []
  | TimestampType -> JStr []
  | DecimalType -> JStr dec_zero

(* F#: `cellJson`. An `Int` in a float or decimal column is written as THAT type — the two
   widenings — and every other cell is written as-is, whether or not `validate` would take it. *)
let cell_json (#num #flt: eqtype) (h: host num flt) (ty: column_type) (c: cell num flt)
  : Tot (jval num flt) =
  match c with
  | Null -> absent_slot h ty
  | Int i ->
      (match ty with
       | FloatType -> JFloat (h.to_float i)
       | DecimalType -> JStr (h.int_text i)
       | _ -> JInt i)
  | Float f -> JFloat f
  | Bool b -> JBool b
  | Str s -> JStr s
  | Date s -> JStr s
  | Timestamp s -> JStr s
  | Decimal s -> JStr s

(* F#: `c.Cells |> List.map (cellJson c.Type)`. *)
let rec values_json (#num #flt: eqtype) (h: host num flt) (ty: column_type)
                    (cs: list (cell num flt))
  : Tot (list (jval num flt)) (decreases cs) =
  match cs with
  | [] -> []
  | c :: t -> cell_json h ty c :: values_json h ty t

(* F#: `c.Cells |> List.map (fun cell -> JBool (not (Cell.isNull cell)))`. *)
let rec validity_json (#num #flt: eqtype) (cs: list (cell num flt))
  : Tot (list (jval num flt)) (decreases cs) =
  match cs with
  | [] -> []
  | c :: t -> JBool (not (Null? c)) :: validity_json t

(* F#: `columnJson`. *)
let column_json (#num #flt: eqtype) (h: host num flt) (c: column num flt) : Tot (jval num flt) =
  JObj [ (values_key, JArr (values_json h c.ctype c.cells));
         (validity_key, JArr (validity_json c.cells)) ]

(* F#: the `List.map` inside `schemaJson`. *)
let rec schema_json_items (#num #flt: eqtype) (s: list (list ch & column_type))
  : Tot (list (jval num flt)) (decreases s) =
  match s with
  | [] -> []
  | (n, ty) :: t -> JObj [ (name_key, JStr n); (type_key, JStr (tag ty)) ] :: schema_json_items t

(* F#: `List.tryFind (fun c -> c.Name = name) |> Option.defaultValue (Column.create name
   StringType [])` — the column `encodeJson` writes for a schema name, with the empty placeholder
   it papers a missing one over with. `validate` refuses the table that reaches the placeholder;
   it is modelled because `encode` does not. *)
let column_or_placeholder (#num #flt: eqtype) (n: list ch) (cs: list (column num flt))
  : Tot (column num flt) =
  match find_column n cs with
  | Some c -> c
  | None -> { name = n; ctype = StringType; cells = [] }

(* F#: the `t.Schema |> List.map (fun (name, _) -> …)` of `encodeJson` — one member per SCHEMA
   entry, keyed by the schema's name, in schema order. *)
let rec columns_json (#num #flt: eqtype) (h: host num flt) (s: list (list ch & column_type))
                     (cs: list (column num flt))
  : Tot (list (list ch & jval num flt)) (decreases s) =
  match s with
  | [] -> []
  | (n, _) :: t -> (n, column_json h (column_or_placeholder n cs)) :: columns_json h t cs

(* F#: `ColumnCodec.encodeJson`. *)
let encode_json (#num #flt: eqtype) (h: host num flt) (src: data_source num flt)
  : Tot (jval num flt) =
  match src with
  | Embedded t ->
      JObj [ (schema_key, JArr (schema_json_items t.schema));
             (columns_key, JObj (columns_json h t.schema t.columns)) ]
  | Ref r -> JObj [ (schema_key, JArr []); (ref_key, JStr r) ]

(* F#: `ColumnCodec.tryEncode`, at the `JVal` — `validate`, then `encodeJson`; a `ref` source
   carries no table and always encodes. *)
let try_encode_json (#num #flt: eqtype) (h: host num flt) (src: data_source num flt)
  : Tot (res (jval num flt)) =
  match src with
  | Ref _ -> Good (encode_json h src)
  | Embedded t ->
      (match validate h t with
       | Bad e -> Bad e
       | Good _ -> Good (encode_json h src))

(* ======================================================================================
   5. DECODE (F#: `Decode.tryProp` and the codec's `getField` / `asArr` / `asStr`,
      `decodeCell`, `decodeSchemaEntry`, `decodeSchema`, `columnParts`, `decodeColumn`,
      `uniqueColumnKeys`, `decodeJson`).
   ====================================================================================== *)

(* F#: `fields |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd` — the FIRST member of
   that name. *)
let rec find_kv (#num #flt: eqtype) (n: list ch) (fs: list (list ch & jval num flt))
  : Tot (option (jval num flt)) (decreases fs) =
  match fs with
  | [] -> None
  | (k, v) :: t -> if k = n then Some v else find_kv n t

(* F#: `Decode.tryProp` — `None` where `el` has no such member OR is not an object. *)
let try_prop (#num #flt: eqtype) (n: list ch) (el: jval num flt) : Tot (option (jval num flt)) =
  match el with
  | JObj fs -> find_kv n fs
  | _ -> None

(* F#: `getField` = `Decode.propWith (fault "")`. A missing member is `MissingField`; a
   non-object is `MalformedShape`. *)
let get_field (#num #flt: eqtype) (n: list ch) (el: jval num flt) : Tot (res (jval num flt)) =
  match el with
  | JObj fs ->
      (match find_kv n fs with
       | Some v -> Good v
       | None -> Bad (MissingField n))
  | _ -> Bad MalformedShape

(* F#: `asArr` = `Decode.arrayWith`. *)
let as_arr (#num #flt: eqtype) (el: jval num flt) : Tot (res (list (jval num flt))) =
  match el with
  | JArr xs -> Good xs
  | _ -> Bad MalformedShape

(* F#: `asStr` = `Decode.stringWith`. *)
let as_str (#num #flt: eqtype) (el: jval num flt) : Tot (res (list ch)) =
  match el with
  | JStr s -> Good s
  | _ -> Bad MalformedShape

(* F#: `decodeCell`, arm for arm. Three arms are LENIENT INGEST and answer `OutOfModel` — an
   epoch number in a timestamp column (as a `JInt`, or as a `JFloat` whichever way the guard
   falls) and a JSON float in a decimal column (likewise). Every other arm is production's:
   an int column reads a `JInt`; a float column reads a `JFloat`, or a `JInt` widened; a date, a
   timestamp and a decimal read their text and check it; anything else is a `TypeMismatch`. *)
let decode_cell (#num #flt: eqtype) (h: host num flt) (cname: list ch) (ty: column_type)
                (v: jval num flt)
  : Tot (res (cell num flt)) =
  match ty with
  | IntType ->
      (match v with
       | JInt i -> Good (Int i)
       | _ -> Bad (TypeMismatch cname ty))
  | FloatType ->
      (match v with
       | JFloat f -> Good (Float f)
       | JInt i -> Good (Float (h.to_float i))
       | _ -> Bad (TypeMismatch cname ty))
  | BoolType ->
      (match v with
       | JBool b -> Good (Bool b)
       | _ -> Bad (TypeMismatch cname ty))
  | StringType ->
      (match v with
       | JStr s -> Good (Str s)
       | _ -> Bad (TypeMismatch cname ty))
  | DateType ->
      (match v with
       | JStr s -> if h.is_date s then Good (Date s) else Bad MalformedShape
       | _ -> Bad (TypeMismatch cname ty))
  | TimestampType ->
      (match v with
       | JStr s -> if h.is_timestamp s then Good (Timestamp s) else Bad MalformedShape
       | JInt _ -> Bad OutOfModel
       | JFloat _ -> Bad OutOfModel
       | _ -> Bad (TypeMismatch cname ty))
  | DecimalType ->
      (match v with
       | JInt i -> Good (Decimal (h.int_text i))
       | JFloat _ -> Bad OutOfModel
       | JStr s ->
           (match try_canonical s with
            | Some canonical -> Good (Decimal canonical)
            | None -> Bad MalformedShape)
       | _ -> Bad (TypeMismatch cname ty))

(* F#: `decodeSchemaEntry`. *)
let decode_schema_entry (#num #flt: eqtype) (el: jval num flt)
  : Tot (res (list ch & column_type)) =
  match get_field name_key el with
  | Bad e -> Bad e
  | Good name_el ->
      (match as_str name_el with
       | Bad e -> Bad e
       | Good n ->
           (match get_field type_key el with
            | Bad e -> Bad e
            | Good type_el ->
                (match as_str type_el with
                 | Bad e -> Bad e
                 | Good tg ->
                     (match of_tag tg with
                      | Some ty -> Good (n, ty)
                      | None -> Bad (UnknownType tg)))))

(* F#: `decodeSchema`'s `go` — see the header on the accumulate-and-reverse loops. *)
let rec decode_schema_items (#num #flt: eqtype) (xs: list (jval num flt))
  : Tot (res (list (list ch & column_type))) (decreases xs) =
  match xs with
  | [] -> Good []
  | x :: rest ->
      (match decode_schema_entry x with
       | Bad e -> Bad e
       | Good entry ->
           (match decode_schema_items rest with
            | Bad e -> Bad e
            | Good entries -> Good (entry :: entries)))

(* F#: `decodeSchema`. *)
let decode_schema (#num #flt: eqtype) (el: jval num flt)
  : Tot (res (list (list ch & column_type))) =
  match as_arr el with
  | Bad e -> Bad e
  | Good xs -> decode_schema_items xs

(* F#: `columnParts`. The bare-array column and the wrapped column with no `validity` are the
   two lenient shorthands, and answer `OutOfModel`; the wrapped `{values, validity}` form — the
   only one the encoder writes — is production's. *)
let column_parts (#num #flt: eqtype) (col_el: jval num flt)
  : Tot (res (list (jval num flt) & list (jval num flt))) =
  match col_el with
  | JArr _ -> Bad OutOfModel
  | _ ->
      (match get_field values_key col_el with
       | Bad e -> Bad e
       | Good values_el ->
           (match as_arr values_el with
            | Bad e -> Bad e
            | Good values ->
                (match try_prop validity_key col_el with
                 | None -> Bad OutOfModel
                 | Some validity_el ->
                     (match as_arr validity_el with
                      | Bad e -> Bad e
                      | Good validity -> Good (values, validity)))))

(* F#: `decodeColumn`'s `go`, over the two co-indexed arrays. A slot whose mask is `false` is
   `Null` and ITS VALUE IS NOT READ — that is the first arm, and it is why the absent slot can be
   a placeholder. A mask that is not a bool is a `MalformedShape`; the last arm is the uneven
   exhaustion production also carries and its length check makes unreachable. *)
let rec decode_cells (#num #flt: eqtype) (h: host num flt) (cname: list ch) (ty: column_type)
                     (values validity: list (jval num flt))
  : Tot (res (list (cell num flt))) (decreases values) =
  match values, validity with
  | [], [] -> Good []
  | v :: vs, p :: ps ->
      (match p with
       | JBool present ->
           if not present then
             (match decode_cells h cname ty vs ps with
              | Bad e -> Bad e
              | Good cs -> Good (Null :: cs))
           else
             (match decode_cell h cname ty v with
              | Bad e -> Bad e
              | Good c ->
                  (match decode_cells h cname ty vs ps with
                   | Bad e -> Bad e
                   | Good cs -> Good (c :: cs)))
       | _ -> Bad MalformedShape)
  | _ -> Bad MalformedShape

(* F#: `decodeColumn`. The column is built with the SCHEMA's name and the SCHEMA's type. *)
let decode_column (#num #flt: eqtype) (h: host num flt) (columns_obj: jval num flt)
                  (n: list ch) (ty: column_type)
  : Tot (res (column num flt)) =
  match try_prop n columns_obj with
  | None -> Bad (MissingColumn n)
  | Some col_el ->
      (match column_parts col_el with
       | Bad e -> Bad e
       | Good (values, validity) ->
           if not (same_len values validity) then Bad (LengthMismatch n)
           else
             (match decode_cells h n ty values validity with
              | Bad e -> Bad e
              | Good cs -> Good ({ name = n; ctype = ty; cells = cs })))

(* F#: `decodeJson`'s inner `go` over the schema — one column per schema entry, in schema order. *)
let rec decode_columns (#num #flt: eqtype) (h: host num flt) (columns_obj: jval num flt)
                       (s: list (list ch & column_type))
  : Tot (res (list (column num flt))) (decreases s) =
  match s with
  | [] -> Good []
  | (n, ty) :: rest ->
      (match decode_column h columns_obj n ty with
       | Bad e -> Bad e
       | Good c ->
           (match decode_columns h columns_obj rest with
            | Bad e -> Bad e
            | Good cs -> Good (c :: cs)))

(* F#: `uniqueColumnKeys`. A non-object passes here and is met (or not) by whoever reads it. *)
let unique_column_keys (#num #flt: eqtype) (columns_obj: jval num flt)
  : Tot (res (jval num flt)) =
  match columns_obj with
  | JObj fs ->
      (match first_duplicate (keys_of fs) with
       | Some k -> Bad (Malformed (DuplicateColumnKey k))
       | None -> Good columns_obj)
  | _ -> Good columns_obj

(* F#: `ColumnCodec.decodeJson`, in its order: the schema is read first (and must be well-formed
   even on a `ref`, which then keeps none); a `ref` member wins over `columns`; an embedded source
   reads `columns`, refuses a repeated key, decodes one column per schema entry, and ENDS in
   `validate`. The one arm not production's is the omitted schema on an embedded source, which is
   type inference and answers `OutOfModel`. *)
let decode_json (#num #flt: eqtype) (h: host num flt) (el: jval num flt)
  : Tot (res (data_source num flt)) =
  let schema_r : res (option (list (list ch & column_type))) =
    (match try_prop schema_key el with
     | Some schema_el ->
         (match decode_schema schema_el with
          | Bad e -> Bad e
          | Good s -> Good (Some s))
     | None -> Good None) in
  match schema_r with
  | Bad e -> Bad e
  | Good schema_opt ->
      (match try_prop ref_key el with
       | Some ref_el ->
           (match as_str ref_el with
            | Bad e -> Bad e
            | Good r -> Good (Ref r))
       | None ->
           (match get_field columns_key el with
            | Bad e -> Bad e
            | Good columns_el ->
                (match unique_column_keys columns_el with
                 | Bad e -> Bad e
                 | Good columns_obj ->
                     (match schema_opt with
                      | None -> Bad OutOfModel
                      | Some s ->
                          (match decode_columns h columns_obj s with
                           | Bad e -> Bad e
                           | Good cs ->
                               let t : table num flt = { schema = s; columns = cs } in
                               (match validate h t with
                                | Bad e -> Bad e
                                | Good _ -> Good (Embedded t)))))))

(* ======================================================================================
   6. THE NORMAL FORM — what a validated table comes back as.

      Two things move and nothing else does. COLUMN ORDER: decode rebuilds one column per schema
      entry, in schema order, named and typed by the schema. WIDENING: an `Int` in a float column
      comes back the `Float` of it, and an `Int` in a decimal column the `Decimal` of its digits.
      Extractable, because it is the right-hand side a differential compares production's
      `decode (encode t)` against.
   ====================================================================================== *)

(* What `decodeCell` makes of what `cellJson` wrote, for a cell `validate` accepted. *)
let norm_cell (#num #flt: eqtype) (h: host num flt) (ty: column_type) (c: cell num flt)
  : Tot (cell num flt) =
  match c with
  | Int i ->
      (match ty with
       | FloatType -> Float (h.to_float i)
       | DecimalType -> Decimal (h.int_text i)
       | _ -> c)
  | _ -> c

let rec norm_cells (#num #flt: eqtype) (h: host num flt) (ty: column_type)
                   (cs: list (cell num flt))
  : Tot (list (cell num flt)) (decreases cs) =
  match cs with
  | [] -> []
  | c :: t -> norm_cell h ty c :: norm_cells h ty t

(* The column decode builds for the schema entry `(n, ty)`. *)
let normal_column (#num #flt: eqtype) (h: host num flt) (n: list ch) (ty: column_type)
                  (cs: list (column num flt))
  : Tot (column num flt) =
  { name = n; ctype = ty; cells = norm_cells h ty (column_or_placeholder n cs).cells }

let rec normal_columns (#num #flt: eqtype) (h: host num flt) (s: list (list ch & column_type))
                       (cs: list (column num flt))
  : Tot (list (column num flt)) (decreases s) =
  match s with
  | [] -> []
  | (n, ty) :: t -> normal_column h n ty cs :: normal_columns h t cs

let normal_table (#num #flt: eqtype) (h: host num flt) (t: table num flt) : Tot (table num flt) =
  { schema = t.schema; columns = normal_columns h t.schema t.columns }

(* A `ref` carries no table and is its own normal form. *)
let normal_source (#num #flt: eqtype) (h: host num flt) (src: data_source num flt)
  : Tot (data_source num flt) =
  match src with
  | Embedded t -> Embedded (normal_table h t)
  | Ref r -> Ref r

(* ---- THE TWO NAMED HYPOTHESES (the header's "the numbers are opaque") ---- *)

(* F#: `DecimalText.isCanonical (string i)` for every `int`. True of .NET's `Int32.ToString()` —
   an optional `-`, digits, no leading zero, no `-0` — and a fact about that layout, not about
   this codec. *)
let int_text_canonical (#num #flt: eqtype) (h: host num flt) : prop =
  forall (i: num). is_canonical (h.int_text i)

(* F#: `JVal.nonFiniteToken (float i) = None` for every `int`. True of every `Int32`. *)
let int_floats_finite (#num #flt: eqtype) (h: host num flt) : prop =
  forall (i: num). h.finite (h.to_float i)

let host_ok (#num #flt: eqtype) (h: host num flt) : prop =
  int_text_canonical h /\ int_floats_finite h

(* ======================================================================================
   7. THE TWO THEOREMS, and the list facts they stand on.
   ====================================================================================== *)

(* ---- 7a. what `validate` answering `Good` says, and what makes it answer so ---- *)

[@@ noextract_to "FSharp"]
let validate_good (#num #flt: eqtype) (h: host num flt) (t: table num flt)
  : Lemma (requires validate h t == Good ())
          (ensures None? (first_duplicate (schema_names t.schema)) /\
                   None? (first_duplicate (column_names t.columns)) /\
                   Nil? (not_in (schema_names t.schema) (column_names t.columns)) /\
                   Nil? (not_in (column_names t.columns) (schema_names t.schema)) /\
                   None? (type_fault t.schema t.columns) /\
                   None? (ragged t.columns) /\
                   None? (cells_fault h t.schema t.columns)) = ()

[@@ noextract_to "FSharp"]
let validate_intro (#num #flt: eqtype) (h: host num flt) (t: table num flt)
  : Lemma (requires None? (first_duplicate (schema_names t.schema)) /\
                    None? (first_duplicate (column_names t.columns)) /\
                    Nil? (not_in (schema_names t.schema) (column_names t.columns)) /\
                    Nil? (not_in (column_names t.columns) (schema_names t.schema)) /\
                    None? (type_fault t.schema t.columns) /\
                    None? (ragged t.columns) /\
                    None? (cells_fault h t.schema t.columns))
          (ensures validate h t == Good ()) = ()

(* ---- 7b. names: no duplicate means distinct, and distinct names mean one type per name ---- *)

(* PROOF-ONLY — the specification `firstDuplicate = None` computes. *)
[@@ noextract_to "FSharp"]
let rec distinct (l: list (list ch)) : Tot bool (decreases l) =
  match l with
  | [] -> true
  | x :: t -> not (mem x t) && distinct t

[@@ noextract_to "FSharp"]
let rec first_dup_none (seen names: list (list ch))
  : Lemma (requires None? (first_dup_go seen names))
          (ensures distinct names /\ (forall (x: list ch). mem x names ==> not (mem x seen)))
          (decreases names) =
  match names with
  | [] -> ()
  | n :: rest -> first_dup_none (n :: seen) rest

(* PROOF-ONLY — the type the schema gives a name: the FIRST entry of that name. *)
[@@ noextract_to "FSharp"]
let rec lookup_type (n: list ch) (s: list (list ch & column_type))
  : Tot (option column_type) (decreases s) =
  match s with
  | [] -> None
  | (k, ty) :: t -> if k = n then Some ty else lookup_type n t

[@@ noextract_to "FSharp"]
let rec mem_entry_name (n: list ch) (ty: column_type) (s: list (list ch & column_type))
  : Lemma (requires mem (n, ty) s) (ensures mem n (schema_names s)) (decreases s) =
  match s with
  | [] -> ()
  | (k, kty) :: t -> if k = n && kty = ty then () else mem_entry_name n ty t

(* With distinct names, an entry IS its name's lookup — the fact that lets a column found by name
   be the column its schema entry meant. *)
[@@ noextract_to "FSharp"]
let rec distinct_lookup (s: list (list ch & column_type)) (n: list ch) (ty: column_type)
  : Lemma (requires distinct (schema_names s) /\ mem (n, ty) s)
          (ensures lookup_type n s == Some ty) (decreases s) =
  match s with
  | [] -> ()
  | (k, kty) :: t ->
      if k = n then (if kty = ty then () else mem_entry_name n ty t)
      else distinct_lookup t n ty

(* PROOF-ONLY — every entry of `rest` is its name's lookup in `s`. *)
[@@ noextract_to "FSharp"]
let rec agrees (rest s: list (list ch & column_type)) : Tot bool (decreases rest) =
  match rest with
  | [] -> true
  | (n, ty) :: t -> lookup_type n s = Some ty && agrees t s

[@@ noextract_to "FSharp"]
let rec distinct_agrees (rest s: list (list ch & column_type))
  : Lemma (requires distinct (schema_names s) /\
                    (forall (e: (list ch & column_type)). mem e rest ==> mem e s))
          (ensures agrees rest s) (decreases rest) =
  match rest with
  | [] -> ()
  | (n, ty) :: t -> distinct_lookup s n ty; distinct_agrees t s

[@@ noextract_to "FSharp"]
let rec not_in_subset (names against: list (list ch))
  : Lemma (requires forall (x: list ch). mem x names ==> mem x against)
          (ensures Nil? (not_in names against)) (decreases names) =
  match names with
  | [] -> ()
  | _ :: rest -> not_in_subset rest against

(* ---- 7c. the rebuilt columns: their names, and what a lookup among them finds ---- *)

[@@ noextract_to "FSharp"]
let rec normal_column_names (#num #flt: eqtype) (h: host num flt)
                            (s: list (list ch & column_type)) (cs: list (column num flt))
  : Lemma (ensures column_names (normal_columns h s cs) == schema_names s) (decreases s) =
  match s with
  | [] -> ()
  | _ :: t -> normal_column_names h t cs

[@@ noextract_to "FSharp"]
let rec find_in_normal (#num #flt: eqtype) (h: host num flt) (n: list ch)
                       (s: list (list ch & column_type)) (cs: list (column num flt))
  : Lemma (ensures find_column n (normal_columns h s cs)
                   == (match lookup_type n s with
                       | Some ty -> Some (normal_column h n ty cs)
                       | None -> None))
          (decreases s) =
  match s with
  | [] -> ()
  | (k, _) :: t -> if k = n then () else find_in_normal h n t cs

(* ---- 7d. what `validate` established about each schema entry ---- *)

(* PROOF-ONLY — the schema entry `(n, ty)` has a column, of that type, whose cells all carry. *)
[@@ noextract_to "FSharp"]
let entry_ok (#num #flt: eqtype) (h: host num flt) (cs: list (column num flt)) (n: list ch)
             (ty: column_type)
  : Tot bool =
  match find_column n cs with
  | Some c -> c.ctype = ty && None? (first_uncarriable h c.name c.ctype c.cells)
  | None -> false

[@@ noextract_to "FSharp"]
let rec entries_ok (#num #flt: eqtype) (h: host num flt) (cs: list (column num flt))
                   (rest: list (list ch & column_type))
  : Tot bool (decreases rest) =
  match rest with
  | [] -> true
  | (n, ty) :: t -> entry_ok h cs n ty && entries_ok h cs t

[@@ noextract_to "FSharp"]
let rec mem_name_finds (#num #flt: eqtype) (n: list ch) (cs: list (column num flt))
  : Lemma (requires mem n (column_names cs)) (ensures Some? (find_column n cs)) (decreases cs) =
  match cs with
  | [] -> ()
  | c :: t -> if c.name = n then () else mem_name_finds n t

[@@ noextract_to "FSharp"]
let rec validated_entries_ok (#num #flt: eqtype) (h: host num flt) (cs: list (column num flt))
                             (rest: list (list ch & column_type))
  : Lemma (requires Nil? (not_in (schema_names rest) (column_names cs)) /\
                    None? (type_fault rest cs) /\
                    None? (cells_fault h rest cs))
          (ensures entries_ok h cs rest) (decreases rest) =
  match rest with
  | [] -> ()
  | (n, _) :: t -> mem_name_finds n cs; validated_entries_ok h cs t

(* ---- 7e. cells: a carriable cell stays carriable when widened, and decodes to its widening ---- *)

(* THE WIDENED CELL IS ONE `validate` ACCEPTS. This is where both hypotheses are spent: the float
   of an int must be finite, and the text of an int must be canonical decimal text. *)
[@@ noextract_to "FSharp"]
let norm_cell_carriable (#num #flt: eqtype) (h: host num flt) (cn cn': list ch)
                        (ty: column_type) (c: cell num flt)
  : Lemma (requires host_ok h /\ None? (cell_fault h cn ty c))
          (ensures None? (cell_fault h cn' ty (norm_cell h ty c))) = ()

[@@ noextract_to "FSharp"]
let rec norm_cells_carriable (#num #flt: eqtype) (h: host num flt) (cn cn': list ch)
                             (ty: column_type) (cs: list (cell num flt))
  : Lemma (requires host_ok h /\ None? (first_uncarriable h cn ty cs))
          (ensures None? (first_uncarriable h cn' ty (norm_cells h ty cs))) (decreases cs) =
  match cs with
  | [] -> ()
  | c :: t -> norm_cell_carriable h cn cn' ty c; norm_cells_carriable h cn cn' ty t

(* `decodeCell` INVERTS `cellJson` on a present cell `validate` accepted, up to the widening. The
   case analysis is the whole proof: each of the seven column types meets only the cell cases that
   widen into it, and for each the arm `cellJson` wrote is the arm `decodeCell` reads. *)
[@@ noextract_to "FSharp"]
let decode_cell_inverts (#num #flt: eqtype) (h: host num flt) (cn cn': list ch)
                        (ty: column_type) (c: cell num flt)
  : Lemma (requires host_ok h /\ None? (cell_fault h cn ty c) /\ not (Null? c))
          (ensures decode_cell h cn' ty (cell_json h ty c) == Good (norm_cell h ty c)) = ()

[@@ noextract_to "FSharp"]
let rec values_validity_same_len (#num #flt: eqtype) (h: host num flt) (ty: column_type)
                                 (cs: list (cell num flt))
  : Lemma (ensures same_len (values_json h ty cs) (validity_json cs)) (decreases cs) =
  match cs with
  | [] -> ()
  | _ :: t -> values_validity_same_len h ty t

[@@ noextract_to "FSharp"]
let rec decode_cells_inverts (#num #flt: eqtype) (h: host num flt) (cn cn': list ch)
                             (ty: column_type) (cs: list (cell num flt))
  : Lemma (requires host_ok h /\ None? (first_uncarriable h cn ty cs))
          (ensures decode_cells h cn' ty (values_json h ty cs) (validity_json cs)
                   == Good (norm_cells h ty cs))
          (decreases cs) =
  match cs with
  | [] -> ()
  | c :: t ->
      (if Null? c then () else decode_cell_inverts h cn cn' ty c);
      decode_cells_inverts h cn cn' ty t

(* ---- 7f. lengths: the rebuilt columns are as long as the ones they were built from ---- *)

(* PROOF-ONLY — every column is as long as `cells0`. *)
[@@ noextract_to "FSharp"]
let rec all_len (#num #flt: eqtype) (cells0: list (cell num flt)) (cols: list (column num flt))
  : Tot bool (decreases cols) =
  match cols with
  | [] -> true
  | c :: t -> same_len cells0 c.cells && all_len cells0 t

[@@ noextract_to "FSharp"]
let rec first_ragged_none (#num #flt: eqtype) (cells0: list (cell num flt))
                          (rest: list (column num flt))
  : Lemma (requires None? (first_ragged cells0 rest)) (ensures all_len cells0 rest)
          (decreases rest) =
  match rest with
  | [] -> ()
  | _ :: t -> first_ragged_none cells0 t

[@@ noextract_to "FSharp"]
let rec same_len_refl (#a: Type) (xs: list a)
  : Lemma (ensures same_len xs xs) (decreases xs) =
  match xs with
  | [] -> ()
  | _ :: t -> same_len_refl t

[@@ noextract_to "FSharp"]
let rec same_len_bridge (#a #b #c: Type) (xs: list a) (ys: list b) (zs: list c)
  : Lemma (requires same_len xs ys /\ same_len xs zs) (ensures same_len ys zs) (decreases xs) =
  match xs, ys, zs with
  | _ :: xt, _ :: yt, _ :: zt -> same_len_bridge xt yt zt
  | _ -> ()

[@@ noextract_to "FSharp"]
let rec same_len_norm (#a: Type) (#num #flt: eqtype) (h: host num flt) (ty: column_type)
                      (xs: list a) (cs: list (cell num flt))
  : Lemma (requires same_len xs cs) (ensures same_len xs (norm_cells h ty cs)) (decreases cs) =
  match xs, cs with
  | _ :: xt, _ :: ct -> same_len_norm h ty xt ct
  | _ -> ()

[@@ noextract_to "FSharp"]
let rec found_has_len (#num #flt: eqtype) (cells0: list (cell num flt)) (n: list ch)
                      (cs: list (column num flt))
  : Lemma (requires all_len cells0 cs /\ Some? (find_column n cs))
          (ensures same_len cells0 (Some?.v (find_column n cs)).cells) (decreases cs) =
  match cs with
  | [] -> ()
  | c :: t -> if c.name = n then () else found_has_len cells0 n t

[@@ noextract_to "FSharp"]
let rec normal_all_len (#num #flt: eqtype) (h: host num flt) (cells0: list (cell num flt))
                       (rest: list (list ch & column_type)) (cs: list (column num flt))
  : Lemma (requires all_len cells0 cs /\ entries_ok h cs rest)
          (ensures all_len cells0 (normal_columns h rest cs)) (decreases rest) =
  match rest with
  | [] -> ()
  | (n, ty) :: t ->
      found_has_len cells0 n cs;
      same_len_norm h ty cells0 (Some?.v (find_column n cs)).cells;
      normal_all_len h cells0 t cs

[@@ noextract_to "FSharp"]
let rec all_len_not_ragged (#num #flt: eqtype) (cells0 x: list (cell num flt))
                           (cols: list (column num flt))
  : Lemma (requires all_len cells0 cols /\ same_len cells0 x)
          (ensures None? (first_ragged x cols)) (decreases cols) =
  match cols with
  | [] -> ()
  | c :: t -> same_len_bridge cells0 x c.cells; all_len_not_ragged cells0 x t

[@@ noextract_to "FSharp"]
let normal_not_ragged (#num #flt: eqtype) (h: host num flt) (s: list (list ch & column_type))
                      (cs: list (column num flt))
  : Lemma (requires None? (ragged cs) /\ entries_ok h cs s)
          (ensures None? (ragged (normal_columns h s cs))) =
  match cs with
  | [] -> ()
  | first :: rest ->
      first_ragged_none first.cells rest;
      same_len_refl first.cells;
      normal_all_len h first.cells s cs;
      (match normal_columns h s cs with
       | [] -> ()
       | x :: xs -> all_len_not_ragged first.cells x.cells xs)

(* ---- 7g. the three per-schema checks of `validate`, over the rebuilt columns ---- *)

[@@ noextract_to "FSharp"]
let rec normal_type_fault (#num #flt: eqtype) (h: host num flt)
                          (rest s: list (list ch & column_type)) (cs: list (column num flt))
  : Lemma (requires agrees rest s)
          (ensures None? (type_fault rest (normal_columns h s cs))) (decreases rest) =
  match rest with
  | [] -> ()
  | (n, _) :: t -> find_in_normal h n s cs; normal_type_fault h t s cs

[@@ noextract_to "FSharp"]
let rec normal_cells_fault (#num #flt: eqtype) (h: host num flt)
                           (rest s: list (list ch & column_type)) (cs: list (column num flt))
  : Lemma (requires host_ok h /\ agrees rest s /\ entries_ok h cs rest)
          (ensures None? (cells_fault h rest (normal_columns h s cs))) (decreases rest) =
  match rest with
  | [] -> ()
  | (n, ty) :: t ->
      find_in_normal h n s cs;
      (match find_column n cs with
       | Some c -> norm_cells_carriable h c.name n ty c.cells
       | None -> ());
      normal_cells_fault h t s cs

(* ---- THEOREM 1, the half with content: THE ENCODER'S IMAGE RE-VALIDATES ----

   The table decode rebuilds from the encoding of a validated table — `normal_table` — is one
   `validate` accepts. Every clause of `validate` is re-established rather than inherited: the
   names are the schema's own, so they are distinct and neither side has a name the other lacks;
   each rebuilt column carries its schema entry's type; every rebuilt column is as long as the
   first column of the source; and every widened cell is one the codec carries, which is where
   the two hypotheses about the host are spent.

   This is what makes the `validate` at the end of `decodeJson` a check the encoder's own output
   always passes, and therefore what makes the round trip below a `Good` at all. *)
let encode_image_revalidates (#num #flt: eqtype) (h: host num flt) (t: table num flt)
  : Lemma (requires host_ok h /\ validate h t == Good ())
          (ensures validate h (normal_table h t) == Good ()) =
  let s = t.schema in
  let cs = t.columns in
  validate_good h t;
  normal_column_names h s cs;
  not_in_subset (schema_names s) (schema_names s);
  first_dup_none [] (schema_names s);
  distinct_agrees s s;
  normal_type_fault h s s cs;
  validated_entries_ok h cs s;
  normal_not_ragged h s cs;
  normal_cells_fault h s s cs;
  validate_intro h (normal_table h t)

(* ---- THEOREM 1, the half production states: DECODE'S IMAGE LIES INSIDE `validate` ----

   Whatever `decode_json` answers `Embedded t` for is a table `validate` accepts — for EVERY host
   and EVERY input, with no hypothesis, because the last thing the embedded arm does is ask. It is
   stated because it is the sentence `Table.validate`'s doc comment makes ("`ColumnCodec.decode`
   ends in it"), and a sentence in a comment is what this directory exists to replace. *)
let decode_image_is_valid (#num #flt: eqtype) (h: host num flt) (el: jval num flt)
                          (t: table num flt)
  : Lemma (requires decode_json h el == Good (Embedded t))
          (ensures validate h t == Good ()) = ()

(* ---- 7h. the decode side: each reader inverts what its writer wrote ---- *)

[@@ noextract_to "FSharp"]
let of_tag_inverts_tag (ty: column_type) : Lemma (ensures of_tag (tag ty) == Some ty) =
  match ty with
  | IntType -> assert_norm (of_tag (tag IntType) == Some IntType)
  | FloatType -> assert_norm (of_tag (tag FloatType) == Some FloatType)
  | BoolType -> assert_norm (of_tag (tag BoolType) == Some BoolType)
  | StringType -> assert_norm (of_tag (tag StringType) == Some StringType)
  | DateType -> assert_norm (of_tag (tag DateType) == Some DateType)
  | TimestampType -> assert_norm (of_tag (tag TimestampType) == Some TimestampType)
  | DecimalType -> assert_norm (of_tag (tag DecimalType) == Some DecimalType)

(* The member names one object carries are pairwise different — five facts, each by computation. *)
[@@ noextract_to "FSharp"]
let keys_differ ()
  : Lemma (ensures name_key <> type_key /\ values_key <> validity_key /\
                   schema_key <> columns_key /\ schema_key <> ref_key /\ columns_key <> ref_key) =
  assert_norm (name_key <> type_key);
  assert_norm (values_key <> validity_key);
  assert_norm (schema_key <> columns_key);
  assert_norm (schema_key <> ref_key);
  assert_norm (columns_key <> ref_key)

[@@ noextract_to "FSharp"]
let rec decode_schema_inverts (#num #flt: eqtype) (s: list (list ch & column_type))
  : Lemma (ensures decode_schema_items #num #flt (schema_json_items s) == Good s)
          (decreases s) =
  match s with
  | [] -> ()
  | (_, ty) :: t ->
      keys_differ ();
      of_tag_inverts_tag ty;
      decode_schema_inverts #num #flt t

[@@ noextract_to "FSharp"]
let rec columns_json_keys (#num #flt: eqtype) (h: host num flt)
                          (s: list (list ch & column_type)) (cs: list (column num flt))
  : Lemma (ensures keys_of (columns_json h s cs) == schema_names s) (decreases s) =
  match s with
  | [] -> ()
  | _ :: t -> columns_json_keys h t cs

(* The member the `columns` object carries for a name depends on the NAME alone, so a lookup by a
   schema name finds that name's column whether or not the schema repeats it. *)
[@@ noextract_to "FSharp"]
let rec find_in_columns_json (#num #flt: eqtype) (h: host num flt) (n: list ch)
                             (s: list (list ch & column_type)) (cs: list (column num flt))
  : Lemma (ensures find_kv n (columns_json h s cs)
                   == (if mem n (schema_names s)
                       then Some (column_json h (column_or_placeholder n cs))
                       else None))
          (decreases s) =
  match s with
  | [] -> ()
  | (k, _) :: t -> if k = n then () else find_in_columns_json h n t cs

[@@ noextract_to "FSharp"]
let decode_column_inverts (#num #flt: eqtype) (h: host num flt)
                          (s: list (list ch & column_type)) (cs: list (column num flt))
                          (n: list ch) (ty: column_type)
  : Lemma (requires host_ok h /\ mem n (schema_names s) /\ entry_ok h cs n ty)
          (ensures decode_column h (JObj (columns_json h s cs)) n ty
                   == Good (normal_column h n ty cs)) =
  let c = column_or_placeholder n cs in
  keys_differ ();
  find_in_columns_json h n s cs;
  values_validity_same_len h c.ctype c.cells;
  decode_cells_inverts h c.name n ty c.cells

[@@ noextract_to "FSharp"]
let rec decode_columns_inverts (#num #flt: eqtype) (h: host num flt)
                               (s: list (list ch & column_type)) (cs: list (column num flt))
                               (rest: list (list ch & column_type))
  : Lemma (requires host_ok h /\ entries_ok h cs rest /\
                    (forall (x: list ch). mem x (schema_names rest) ==> mem x (schema_names s)))
          (ensures decode_columns h (JObj (columns_json h s cs)) rest
                   == Good (normal_columns h rest cs))
          (decreases rest) =
  match rest with
  | [] -> ()
  | (n, ty) :: t ->
      decode_column_inverts h s cs n ty;
      decode_columns_inverts h s cs t

(* `decodeJson`'s embedded arm, as the composition it is: given what each step answers, what the
   whole answers. Stated once so that the two round-trip theorems — the authored document, and
   the one with its members sorted — spend their own proof on their own steps. *)
[@@ noextract_to "FSharp"]
let decode_json_embedded (#num #flt: eqtype) (h: host num flt)
                         (el schema_el columns_el: jval num flt)
                         (s: list (list ch & column_type)) (cs: list (column num flt))
  : Lemma (requires try_prop schema_key el == Some schema_el /\
                    decode_schema schema_el == Good s /\
                    None? (try_prop ref_key el) /\
                    get_field columns_key el == Good columns_el /\
                    unique_column_keys columns_el == Good columns_el /\
                    decode_columns h columns_el s == Good cs)
          (ensures decode_json h el
                   == (match validate h ({ schema = s; columns = cs }) with
                       | Bad e -> Bad e
                       | Good _ -> Good (Embedded ({ schema = s; columns = cs })))) = ()

(* ---- THEOREM 2: THE ROUND TRIP ----

   For every table `validate` accepts, decoding its encoding answers the table's NORMAL FORM —
   the columns in schema order, each `Int` widened into its column's type — and answers it as a
   `Good`, so no arm of the decoder refuses, and none that answers `OutOfModel` is reached.

   The proof is the decoder's own order: the schema array reads back as the schema; there is no
   `ref` member; `columns` is an object whose keys are the schema's names, which `validate` made
   distinct; each schema entry finds its column's member and reads its cells back widened; and the
   table so built is `normal_table t`, which theorem 1 says the final `validate` accepts. *)
let round_trip (#num #flt: eqtype) (h: host num flt) (t: table num flt)
  : Lemma (requires host_ok h /\ validate h t == Good ())
          (ensures decode_json h (encode_json h (Embedded t))
                   == Good (Embedded (normal_table h t))) =
  let s = t.schema in
  let cs = t.columns in
  let columns_el : jval num flt = JObj (columns_json h s cs) in
  validate_good h t;
  keys_differ ();
  decode_schema_inverts #num #flt s;
  columns_json_keys h s cs;
  validated_entries_ok h cs s;
  decode_columns_inverts h s cs s;
  encode_image_revalidates h t;
  decode_json_embedded h (encode_json h (Embedded t)) (JArr (schema_json_items s)) columns_el s
    (normal_columns h s cs)

(* A `ref` source round-trips EXACTLY, on every host and with no hypothesis: the encoder writes
   the name beside an empty schema, and the decoder reads the empty schema, meets the `ref`
   member before it looks for `columns`, and keeps the name. *)
let ref_round_trip (#num #flt: eqtype) (h: host num flt) (r: list ch)
  : Lemma (ensures decode_json h (encode_json h (Ref #num #flt r)) == Good (Ref r)) =
  keys_differ ()

(* The two together, at the GUARDED entry point — the statement a caller of `tryEncode` holds:
   whatever it answers `Good` for decodes to the source's normal form. *)
let round_trip_guarded (#num #flt: eqtype) (h: host num flt) (src: data_source num flt)
                       (v: jval num flt)
  : Lemma (requires host_ok h /\ try_encode_json h src == Good v)
          (ensures decode_json h v == Good (normal_source h src)) =
  match src with
  | Ref r -> ref_round_trip h r
  | Embedded t -> round_trip h t

(* F#: the law `Table.validate`'s doc comment cites — "over what it accepts, `tryEncode` is
   exactly `Ok (encode src)`". At the `JVal`, and by the definition. *)
let try_encode_is_encode_on_valid (#num #flt: eqtype) (h: host num flt) (t: table num flt)
  : Lemma (requires validate h t == Good ())
          (ensures try_encode_json h (Embedded t) == Good (encode_json h (Embedded t))) = ()

(* ---- 7i. the normal form is a FIXED POINT, so the round trip is exact from the first decode on ---- *)

[@@ noextract_to "FSharp"]
let rec norm_cells_idem (#num #flt: eqtype) (h: host num flt) (ty: column_type)
                        (cs: list (cell num flt))
  : Lemma (ensures norm_cells h ty (norm_cells h ty cs) == norm_cells h ty cs) (decreases cs) =
  match cs with
  | [] -> ()
  | _ :: t -> norm_cells_idem h ty t

[@@ noextract_to "FSharp"]
let rec normal_columns_idem (#num #flt: eqtype) (h: host num flt)
                            (rest s: list (list ch & column_type)) (cs: list (column num flt))
  : Lemma (requires agrees rest s)
          (ensures normal_columns h rest (normal_columns h s cs) == normal_columns h rest cs)
          (decreases rest) =
  match rest with
  | [] -> ()
  | (n, ty) :: t ->
      find_in_normal h n s cs;
      norm_cells_idem h ty (column_or_placeholder n cs).cells;
      normal_columns_idem h t s cs

(* Normalising twice is normalising once. It needs only that the schema's names are distinct,
   which `validate` establishes; stated under `validate` because that is what a caller holds. *)
let normal_table_is_idempotent (#num #flt: eqtype) (h: host num flt) (t: table num flt)
  : Lemma (requires validate h t == Good ())
          (ensures normal_table h (normal_table h t) == normal_table h t) =
  validate_good h t;
  first_dup_none [] (schema_names t.schema);
  distinct_agrees t.schema t.schema;
  normal_columns_idem h t.schema t.schema t.columns

(* THE LITERAL ROUND TRIP, where it is true: on a table that is already its own normal form. *)
let round_trip_is_exact_on_a_normal_table (#num #flt: eqtype) (h: host num flt)
                                          (t: table num flt)
  : Lemma (requires host_ok h /\ validate h t == Good () /\ normal_table h t == t)
          (ensures decode_json h (encode_json h (Embedded t)) == Good (Embedded t)) =
  round_trip h t

(* And every table the codec hands back is one: encode what decode produced and decode it again,
   and the answer is the same table. The normalisation happens ONCE. *)
let second_round_trip_is_exact (#num #flt: eqtype) (h: host num flt) (t: table num flt)
  : Lemma (requires host_ok h /\ validate h t == Good ())
          (ensures decode_json h (encode_json h (Embedded (normal_table h t)))
                   == Good (Embedded (normal_table h t))) =
  encode_image_revalidates h t;
  normal_table_is_idempotent h t;
  round_trip h (normal_table h t)

(* ---- 7j. EVERY table decode answers is already normal — so what the corpus runner checks holds ----

   `Corpus.runCase`'s `RoundTrip` kind decodes a document and then asks that the DECODED value
   round-trips literally. That is a different question from theorem 2's — it starts from a
   document, any document, not from a table somebody validated — and it has the literal answer:
   decode never builds an `Int` in a float or decimal column, and always builds the columns in
   schema order, so its output is its own normal form. *)

[@@ noextract_to "FSharp"]
let rec decode_cells_normal (#num #flt: eqtype) (h: host num flt) (cn: list ch)
                            (ty: column_type) (values validity: list (jval num flt))
                            (cs: list (cell num flt))
  : Lemma (requires decode_cells h cn ty values validity == Good cs)
          (ensures norm_cells h ty cs == cs) (decreases values) =
  match values, validity with
  | _ :: vs, JBool _ :: ps ->
      (match decode_cells h cn ty vs ps with
       | Good rest -> decode_cells_normal h cn ty vs ps rest
       | Bad _ -> ())
  | _ -> ()

(* PROOF-ONLY — `cs` is one column per schema entry, in order, named and typed by it, with no
   cell left to widen. What `decode_columns` builds. *)
[@@ noextract_to "FSharp"]
let rec shaped (#num #flt: eqtype) (h: host num flt) (s: list (list ch & column_type))
               (cs: list (column num flt))
  : Tot bool (decreases s) =
  match s, cs with
  | [], [] -> true
  | (n, ty) :: st, c :: ct ->
      c.name = n && c.ctype = ty && norm_cells h ty c.cells = c.cells && shaped h st ct
  | _ -> false

[@@ noextract_to "FSharp"]
let decode_column_shaped (#num #flt: eqtype) (h: host num flt) (columns_obj: jval num flt)
                         (n: list ch) (ty: column_type) (c: column num flt)
  : Lemma (requires decode_column h columns_obj n ty == Good c)
          (ensures c.name == n /\ c.ctype == ty /\ norm_cells h ty c.cells == c.cells) =
  match try_prop n columns_obj with
  | None -> ()
  | Some col_el ->
      (match column_parts col_el with
       | Bad _ -> ()
       | Good (values, validity) -> decode_cells_normal h n ty values validity c.cells)

[@@ noextract_to "FSharp"]
let rec decode_columns_shaped (#num #flt: eqtype) (h: host num flt) (columns_obj: jval num flt)
                              (s: list (list ch & column_type)) (cs: list (column num flt))
  : Lemma (requires decode_columns h columns_obj s == Good cs) (ensures shaped h s cs)
          (decreases s) =
  match s with
  | [] -> ()
  | (n, ty) :: rest ->
      (match decode_column h columns_obj n ty with
       | Bad _ -> ()
       | Good c ->
           decode_column_shaped h columns_obj n ty c;
           (match decode_columns h columns_obj rest with
            | Bad _ -> ()
            | Good ct -> decode_columns_shaped h columns_obj rest ct))

[@@ noextract_to "FSharp"]
let rec mem_column_name (#num #flt: eqtype) (c: column num flt) (cs: list (column num flt))
  : Lemma (requires mem c cs) (ensures mem c.name (column_names cs)) (decreases cs) =
  match cs with
  | [] -> ()
  | x :: t -> if x = c then () else mem_column_name c t

(* Among distinctly-named columns, a lookup by a column's own name finds THAT column. *)
[@@ noextract_to "FSharp"]
let rec find_own_name (#num #flt: eqtype) (c: column num flt) (cs: list (column num flt))
  : Lemma (requires distinct (column_names cs) /\ mem c cs)
          (ensures find_column c.name cs == Some c) (decreases cs) =
  match cs with
  | [] -> ()
  | x :: t ->
      if x.name = c.name then (if x = c then () else mem_column_name c t)
      else find_own_name c t

[@@ noextract_to "FSharp"]
let rec shaped_is_normal (#num #flt: eqtype) (h: host num flt)
                         (rest: list (list ch & column_type)) (crest cs: list (column num flt))
  : Lemma (requires shaped h rest crest /\ distinct (column_names cs) /\
                    (forall (c: column num flt). mem c crest ==> mem c cs))
          (ensures normal_columns h rest cs == crest) (decreases rest) =
  match rest, crest with
  | _ :: rt, c :: ct -> find_own_name c cs; shaped_is_normal h rt ct cs
  | _ -> ()

(* EVERY TABLE DECODE ANSWERS IS ITS OWN NORMAL FORM — for every host and every document, with
   no hypothesis. *)
let decoded_table_is_normal (#num #flt: eqtype) (h: host num flt) (el: jval num flt)
                            (t: table num flt)
  : Lemma (requires decode_json h el == Good (Embedded t))
          (ensures normal_table h t == t) =
  validate_good h t;
  first_dup_none [] (column_names t.columns);
  (match get_field columns_key el with
   | Good columns_el -> decode_columns_shaped h columns_el t.schema t.columns
   | Bad _ -> ());
  shaped_is_normal h t.schema t.columns t.columns

(* THE LITERAL ROUND TRIP, where it is true of EVERYTHING: on a value the decoder produced.
   Decode any document; encode what came back; decode that — the same value, exactly. This is the
   check `Corpus.runCase` makes of a `RoundTrip` case, as a theorem. *)
let decoded_round_trip_is_exact (#num #flt: eqtype) (h: host num flt) (el: jval num flt)
                                (src: data_source num flt)
  : Lemma (requires host_ok h /\ decode_json h el == Good src)
          (ensures decode_json h (encode_json h src) == Good src) =
  match src with
  | Ref r -> ref_round_trip h r
  | Embedded t ->
      decode_image_is_valid h el t;
      decoded_table_is_normal h el t;
      round_trip h t

(* The cell `decodeCell` builds is one `validate` accepts — for ANY JSON value, not only one the
   encoder wrote — with the single exception of a non-finite `JFloat`, which no parser produces
   (the wire has no token for one) but a caller holding a `JVal` can construct. So of the clauses
   of the `validate` that ends `decodeJson`, the per-cell one is reachable only through that
   value; what the final check is otherwise FOR is the table's shape — a schema naming a column
   twice, and columns of different lengths. *)
let decode_cell_image_is_carriable (#num #flt: eqtype) (h: host num flt) (cn: list ch)
                                   (ty: column_type) (v: jval num flt) (c: cell num flt)
  : Lemma (requires host_ok h /\ decode_cell h cn ty v == Good c /\
                    (match v with
                     | JFloat f -> h.finite f
                     | _ -> True))
          (ensures None? (cell_fault h cn ty c)) =
  match ty, v with
  | DecimalType, JStr s -> try_canonical_is_idempotent s
  | _ -> ()

(* ======================================================================================
   8. THE REFUTATIONS, the necessity of the two hypotheses, and what decode accepts that
      encode never writes.

      Each is stated for EVERY host and every name rather than exhibited at a contrived one,
      which is the stronger form: no instantiation escapes them.
   ====================================================================================== *)

(* The one-column table the first two refutations and the two necessity lemmas are about. *)
let single_column_table (#num #flt: eqtype) (n: list ch) (ty: column_type)
                        (cs: list (cell num flt))
  : Tot (table num flt) =
  { schema = [(n, ty)]; columns = [{ name = n; ctype = ty; cells = cs }] }

(* The table the third refutation is about: two empty int columns, held in the order `b`, `a`
   under a schema that says `a`, `b`. *)
let reordered_table (#num #flt: eqtype) (a b: list ch) : Tot (table num flt) =
  { schema = [(a, IntType); (b, IntType)];
    columns = [{ name = b; ctype = IntType; cells = [] }; { name = a; ctype = IntType; cells = [] }] }

(* ---- the one-column table, computed once ----

   Three facts about `single_column_table`, each a finite computation, proved here at the fuel a
   computation needs so that the refutations below can cite them at the default. *)

#push-options "--fuel 3 --ifuel 1"

(* `validate` of a one-cell column is that cell's own verdict. *)
[@@ noextract_to "FSharp"]
let single_column_validate (#num #flt: eqtype) (h: host num flt) (n: list ch) (ty: column_type)
                           (c: cell num flt)
  : Lemma (ensures validate h (single_column_table n ty [c])
                   == (match cell_fault h n ty c with
                       | Some e -> Bad e
                       | None -> Good ())) = ()

(* Its normal form is the one-cell column of the widened cell. *)
[@@ noextract_to "FSharp"]
let single_column_normal (#num #flt: eqtype) (h: host num flt) (n: list ch) (ty: column_type)
                         (c: cell num flt)
  : Lemma (ensures normal_table h (single_column_table n ty [c])
                   == single_column_table n ty [norm_cell h ty c]) = ()

(* And what decode makes of its encoding, for ANY host: whatever `decodeCell` says of the one
   slot, and then whatever `validate` says of the table holding the cell it read. *)
[@@ noextract_to "FSharp"]
let single_cell_decode (#num #flt: eqtype) (h: host num flt) (n: list ch) (ty: column_type)
                       (c: cell num flt)
  : Lemma (requires not (Null? c))
          (ensures decode_json h (encode_json h (Embedded (single_column_table n ty [c])))
                   == (match decode_cell h n ty (cell_json h ty c) with
                       | Bad e -> Bad e
                       | Good c' ->
                           (match validate h (single_column_table n ty [c']) with
                            | Bad e -> Bad e
                            | Good _ -> Good (Embedded (single_column_table n ty [c']))))) =
  let t = single_column_table #num #flt n ty [c] in
  let columns_el : jval num flt = JObj (columns_json h t.schema t.columns) in
  keys_differ ();
  decode_schema_inverts #num #flt t.schema;
  match decode_cell h n ty (cell_json h ty c) with
  | Bad e -> ()
  | Good c' ->
      assert (decode_columns h columns_el t.schema
              == Good [{ name = n; ctype = ty; cells = [c'] }]);
      decode_json_embedded h (encode_json h (Embedded t)) (JArr (schema_json_items t.schema))
        columns_el t.schema [{ name = n; ctype = ty; cells = [c'] }]

(* `validate` accepts the reordered table: the two names are different, and that is all it asks. *)
[@@ noextract_to "FSharp"]
let reordered_table_is_valid (#num #flt: eqtype) (h: host num flt) (a b: list ch)
  : Lemma (requires a <> b)
          (ensures validate h (reordered_table #num #flt a b) == Good () /\
                   normal_table h (reordered_table #num #flt a b)
                   == { schema = [(a, IntType); (b, IntType)];
                        columns = [{ name = a; ctype = IntType; cells = [] };
                                   { name = b; ctype = IntType; cells = [] }] }) = ()

#pop-options

(* 1. AN `Int` IN A FLOAT COLUMN COMES BACK A `Float`. `validate` accepts the table — `Int`
      widens into `FloatType` — and the round trip answers a DIFFERENT table. The documented
      widening, not a defect; but it is why the theorem has a normal form on its right. *)
[@@ noextract_to "FSharp"]
let literal_round_trip_fails_on_a_widened_float (#num #flt: eqtype) (h: host num flt)
                                                (n: list ch) (i: num)
  : Lemma (requires host_ok h)
          (ensures (let t = single_column_table #num #flt n FloatType [Int i] in
                    validate h t == Good () /\
                    decode_json h (encode_json h (Embedded t))
                      == Good (Embedded (single_column_table n FloatType [Float (h.to_float i)])) /\
                    ~(decode_json h (encode_json h (Embedded t)) == Good (Embedded t)))) =
  let t = single_column_table #num #flt n FloatType [Int i] in
  single_column_validate h n FloatType (Int #num #flt i);
  single_column_normal h n FloatType (Int #num #flt i);
  round_trip h t

(* 2. AN `Int` IN A DECIMAL COLUMN COMES BACK A `Decimal`, the text of its digits. *)
[@@ noextract_to "FSharp"]
let literal_round_trip_fails_on_a_widened_decimal (#num #flt: eqtype) (h: host num flt)
                                                  (n: list ch) (i: num)
  : Lemma (requires host_ok h)
          (ensures (let t = single_column_table #num #flt n DecimalType [Int i] in
                    validate h t == Good () /\
                    decode_json h (encode_json h (Embedded t))
                      == Good (Embedded (single_column_table n DecimalType
                                           [Decimal (h.int_text i)])) /\
                    ~(decode_json h (encode_json h (Embedded t)) == Good (Embedded t)))) =
  let t = single_column_table #num #flt n DecimalType [Int i] in
  single_column_validate h n DecimalType (Int #num #flt i);
  single_column_normal h n DecimalType (Int #num #flt i);
  round_trip h t

(* 3. COLUMN ORDER IS NOT PRESERVED, because `validate` never required it. `Table`'s doc comment
      says "column order follows the schema"; `validate` checks the two name lists are the same
      SET and stops. A table whose `Columns` are in another order is accepted, encodes, and comes
      back in SCHEMA order — a different value of the same type. *)
[@@ noextract_to "FSharp"]
let literal_round_trip_fails_on_column_order (#num #flt: eqtype) (h: host num flt)
                                             (a b: list ch)
  : Lemma (requires host_ok h /\ a <> b)
          (ensures (let t = reordered_table #num #flt a b in
                    validate h t == Good () /\
                    decode_json h (encode_json h (Embedded t))
                      == Good (Embedded ({ schema = t.schema;
                                           columns = [{ name = a; ctype = IntType; cells = [] };
                                                      { name = b; ctype = IntType; cells = [] }] })) /\
                    ~(decode_json h (encode_json h (Embedded t)) == Good (Embedded t)))) =
  reordered_table_is_valid #num #flt h a b;
  round_trip h (reordered_table #num #flt a b)

(* ---- the two hypotheses are NECESSARY ----

   Neither is a convenience of the proof. On a host where the float of some int is not finite,
   `validate` accepts the table holding that int in a float column and the decoder REFUSES its
   encoding, by name. *)
[@@ noextract_to "FSharp"]
let int_floats_finite_is_necessary (#num #flt: eqtype) (h: host num flt) (n: list ch) (i: num)
  : Lemma (requires not (h.finite (h.to_float i)))
          (ensures (let t = single_column_table #num #flt n FloatType [Int i] in
                    validate h t == Good () /\
                    decode_json h (encode_json h (Embedded t)) == Bad (NonFiniteFloat n))) =
  single_column_validate h n FloatType (Int #num #flt i);
  single_cell_decode h n FloatType (Int #num #flt i);
  single_column_validate h n FloatType (Float #num #flt (h.to_float i))

(* And on a host where the text of some int is not canonical decimal text, the table holding
   that int in a decimal column is accepted and its encoding does NOT decode to its normal form:
   the text is refused outright, or canonicalised into a different cell. *)
[@@ noextract_to "FSharp"]
let int_text_canonical_is_necessary (#num #flt: eqtype) (h: host num flt) (n: list ch) (i: num)
  : Lemma (requires not (is_canonical (h.int_text i)))
          (ensures (let t = single_column_table #num #flt n DecimalType [Int i] in
                    validate h t == Good () /\
                    ~(decode_json h (encode_json h (Embedded t))
                        == Good (Embedded (normal_table h t))))) =
  single_column_validate h n DecimalType (Int #num #flt i);
  single_column_normal h n DecimalType (Int #num #flt i);
  single_cell_decode h n DecimalType (Int #num #flt i)

#push-options "--fuel 3 --ifuel 1"

(* ---- WHAT DECODE ACCEPTS THAT ENCODE NEVER WRITES ----

   Documents no `encode_json` produces, each decoded to a `Good`. They are why the converse round
   trip, `encode (decode d) = d`, is not claimed: the decoder is not injective, and these are the
   ways it is not that do NOT pass through a lenient-ingest arm. *)

(* (i) A `columns` member the schema does not name is READ PAST, not refused. `decodeJson`'s doc
       comment states it — "surplus members are must-ignore" — and this is that sentence as a
       lemma: the decoder only ever looks a column up by a schema name, so a member nobody asks
       for is never seen. The asymmetry worth knowing is with `validate`, which REFUSES the
       corresponding table — a column absent from the schema is a `Malformed` — so the wire
       tolerates what the in-memory value may not hold. The document below declares no columns
       and carries one. *)
[@@ noextract_to "FSharp"]
let decode_drops_a_column_outside_the_schema (#num #flt: eqtype) (h: host num flt)
                                             (k: list ch) (col: jval num flt)
  : Lemma (ensures decode_json h (JObj [ (schema_key, JArr []);
                                        (columns_key, JObj [ (k, col) ]) ])
                   == Good (Embedded ({ schema = []; columns = [] }))) =
  keys_differ ()

(* …where the table that document would have meant is one `validate` refuses. *)
[@@ noextract_to "FSharp"]
let validate_refuses_a_column_outside_the_schema (#num #flt: eqtype) (h: host num flt)
                                                 (c: column num flt)
  : Lemma (ensures validate h ({ schema = []; columns = [c] })
                   == Bad (Malformed ColumnOutsideSchema)) = ()

(* (ii) A MASKED SLOT IS NEVER READ. Whatever sits under a `false` in the validity mask — a value
        of the wrong kind, an array, an object — decodes to `Null`. This is what lets `absentSlot`
        write `""` into a date column, and it is also why two documents that differ only under
        the mask are one table. *)
[@@ noextract_to "FSharp"]
let decode_never_reads_a_masked_slot (#num #flt: eqtype) (h: host num flt) (n: list ch)
                                     (ty: column_type) (junk: jval num flt)
  : Lemma (ensures decode_json h
                     (JObj [ (schema_key, JArr [ JObj [ (name_key, JStr n);
                                                        (type_key, JStr (tag ty)) ] ]);
                             (columns_key,
                              JObj [ (n, JObj [ (values_key, JArr [junk]);
                                                (validity_key, JArr [JBool false]) ]) ]) ])
                   == Good (Embedded (single_column_table n ty [Null]))) =
  keys_differ ();
  decode_schema_inverts #num #flt [(n, ty)]

(* (iii) UNDER AN EMPTY SCHEMA, `columns` IS NEVER READ — so it need not be an object. The member
         must be PRESENT (`getField` refuses its absence) and that is all that is asked of it. *)
[@@ noextract_to "FSharp"]
let decode_never_reads_columns_under_an_empty_schema (#num #flt: eqtype) (h: host num flt)
                                                     (b: bool)
  : Lemma (ensures decode_json h (JObj [ (schema_key, JArr []); (columns_key, JBool b) ])
                   == Good (Embedded ({ schema = []; columns = [] }))) =
  keys_differ ()

(* The must-ignore rule at the ROOT: a member after `schema` and `columns` is read past, whatever
   it is called — with ONE exception, which is why the lemma has a premise. A member named `ref`
   is not surplus: the decoder asks for `ref` BEFORE it asks for `columns`, so its presence turns
   the document into a `Ref` and the columns beside it are never read. *)
[@@ noextract_to "FSharp"]
let decode_ignores_a_surplus_root_member (#num #flt: eqtype) (h: host num flt)
                                         (schema_el columns_el: jval num flt) (k: list ch)
                                         (v: jval num flt)
  : Lemma (requires k <> ref_key)
          (ensures decode_json h (JObj [ (schema_key, schema_el); (columns_key, columns_el);
                                        (k, v) ])
                   == decode_json h (JObj [ (schema_key, schema_el); (columns_key, columns_el) ])) =
  keys_differ ()

(* …and the exception, exhibited: `ref` beside `columns` is a `Ref`, and the columns are dropped. *)
[@@ noextract_to "FSharp"]
let ref_wins_over_columns (#num #flt: eqtype) (h: host num flt) (columns_el: jval num flt)
                          (r: list ch)
  : Lemma (ensures decode_json h (JObj [ (schema_key, JArr []); (columns_key, columns_el);
                                        (ref_key, JStr r) ])
                   == Good (Ref r)) =
  keys_differ ()

#pop-options

(* ======================================================================================
   9. THE JOIN TO THE CANONICAL RENDERER — how far the STRING level follows, and where it
      stops.

      `ColumnCodec.encode` is `Canon.render (encodeJson src)`, and `WireCanon.fst` proves what a
      reader of those bytes gets back: `read (render v) == Ok (normalise v)` — the value with
      every object's members in canonical key order — on the subset it calls `canonical`. Two
      things are therefore needed to carry theorem 2 to the string, and this section proves the
      one that is about THIS codec and names the one that is not.

      PROVED: decode does not care about member order. `decode_json` reads every member by NAME,
      so it answers the same for the sorted document as for the authored one — for any
      comparator at all, with no premise about the key order (`round_trip_through_normalise`).

      THE BOUNDARY: `WireCanon`'s `canonical` admits a `JFloat` only where its layout carries a
      `.` or an `E`. A whole-valued float renders as the INTEGER token of the same digits and
      reads back as a `JInt` — that module's refutation 2, the wire's documented numeric
      normalisation. So `string_round_trip` below carries the premise `canonical w (encode_json
      …)`, and that premise EXCLUDES every table with a whole-valued float slot: a `Float 2.0`,
      an `Int` widened into a float column, and — the one a reader will not guess — any float
      column holding a `Null`, whose absent slot is `0.0`
      (`a_null_in_a_float_column_is_outside_canonical`). For those tables the string-level round
      trip is NOT proved here. What it needs is that a float column reads the integer token back
      as the same float, which is one arm of `decodeCell` and is proved for one cell
      (`float_column_reads_a_whole_token`); lifting that to a document needs a model of what the
      reader does to a whole-valued float token, which is `WireCanon`'s to supply and is not
      supplied there. It is a named gap, not a weakened theorem.
   ====================================================================================== *)

[@@ noextract_to "FSharp"]
let rec find_absent (#num #flt: eqtype) (n: list ch) (fs: list (list ch & jval num flt))
  : Lemma (requires not (mem n (keys_of fs))) (ensures None? (find_kv n fs)) (decreases fs) =
  match fs with
  | [] -> ()
  | _ :: t -> find_absent n t

(* Inserting a member does not change what a lookup by ANOTHER name finds, and a lookup by ITS
   name finds it — provided the list did not already carry that name. No premise about the
   comparator: wherever the member lands, it is the only one of its name. *)
[@@ noextract_to "FSharp"]
let rec find_in_inserted (#num #flt: eqtype) (w: wire num flt) (n: list ch)
                         (kv: (list ch & jval num flt)) (l: list (list ch & jval num flt))
  : Lemma (requires fst kv = n ==> None? (find_kv n l))
          (ensures find_kv n (insert_kv w kv l)
                   == (if fst kv = n then Some (snd kv) else find_kv n l))
          (decreases l) =
  match l with
  | [] -> ()
  | hd :: t -> if w.key_le (fst kv) (fst hd) then () else find_in_inserted w n kv t

[@@ noextract_to "FSharp"]
let rec find_in_sorted (#num #flt: eqtype) (w: wire num flt) (n: list ch)
                       (fs: list (list ch & jval num flt))
  : Lemma (requires distinct (keys_of fs))
          (ensures find_kv n (sort_kvs w fs) == find_kv n fs) (decreases fs) =
  match fs with
  | [] -> ()
  | kv :: t ->
      find_in_sorted w n t;
      (if fst kv = n then find_absent n t else ());
      find_in_inserted w n kv (sort_kvs w t)

[@@ noextract_to "FSharp"]
let rec find_in_normalised_kvs (#num #flt: eqtype) (w: wire num flt) (n: list ch)
                               (fs: list (list ch & jval num flt))
  : Lemma (ensures find_kv n (normalise_kvs w fs)
                   == (match find_kv n fs with
                       | Some v -> Some (normalise w v)
                       | None -> None))
          (decreases fs) =
  match fs with
  | [] -> ()
  | (k, _) :: t -> if k = n then () else find_in_normalised_kvs w n t

(* MEMBER LOOKUP COMMUTES WITH NORMALISATION, on an object whose keys are distinct. *)
[@@ noextract_to "FSharp"]
let find_in_normalised (#num #flt: eqtype) (w: wire num flt) (n: list ch)
                       (fs: list (list ch & jval num flt))
  : Lemma (requires distinct (keys_of fs))
          (ensures normalise w (JObj fs) == JObj (normalise_kvs w (sort_kvs w fs)) /\
                   find_kv n (normalise_kvs w (sort_kvs w fs))
                   == (match find_kv n fs with
                       | Some v -> Some (normalise w v)
                       | None -> None)) =
  find_in_sorted w n fs;
  find_in_normalised_kvs w n (sort_kvs w fs)

[@@ noextract_to "FSharp"]
let rec keys_of_inserted (#num #flt: eqtype) (w: wire num flt)
                         (kv: (list ch & jval num flt)) (l: list (list ch & jval num flt))
                         (x: list ch)
  : Lemma (ensures mem x (keys_of (insert_kv w kv l)) == (x = fst kv || mem x (keys_of l)))
          (decreases l) =
  match l with
  | [] -> ()
  | hd :: t -> if w.key_le (fst kv) (fst hd) then () else keys_of_inserted w kv t x

[@@ noextract_to "FSharp"]
let rec inserted_distinct (#num #flt: eqtype) (w: wire num flt)
                          (kv: (list ch & jval num flt)) (l: list (list ch & jval num flt))
  : Lemma (requires not (mem (fst kv) (keys_of l)) /\ distinct (keys_of l))
          (ensures distinct (keys_of (insert_kv w kv l))) (decreases l) =
  match l with
  | [] -> ()
  | hd :: t ->
      if w.key_le (fst kv) (fst hd) then ()
      else (keys_of_inserted w kv t (fst hd); inserted_distinct w kv t)

[@@ noextract_to "FSharp"]
let rec sorted_keys (#num #flt: eqtype) (w: wire num flt) (fs: list (list ch & jval num flt))
                    (x: list ch)
  : Lemma (ensures mem x (keys_of (sort_kvs w fs)) == mem x (keys_of fs)) (decreases fs) =
  match fs with
  | [] -> ()
  | kv :: t -> sorted_keys w t x; keys_of_inserted w kv (sort_kvs w t) x

(* Sorting moves members and repeats none, so distinct keys stay distinct. *)
[@@ noextract_to "FSharp"]
let rec sorted_distinct (#num #flt: eqtype) (w: wire num flt)
                        (fs: list (list ch & jval num flt))
  : Lemma (requires distinct (keys_of fs)) (ensures distinct (keys_of (sort_kvs w fs)))
          (decreases fs) =
  match fs with
  | [] -> ()
  | kv :: t ->
      sorted_distinct w t;
      sorted_keys w t (fst kv);
      inserted_distinct w kv (sort_kvs w t)

(* The converse of `first_dup_none`: distinct names carry no first duplicate. *)
[@@ noextract_to "FSharp"]
let rec distinct_no_first_dup (seen names: list (list ch))
  : Lemma (requires distinct names /\ (forall (x: list ch). mem x names ==> not (mem x seen)))
          (ensures None? (first_dup_go seen names)) (decreases names) =
  match names with
  | [] -> ()
  | n :: rest -> distinct_no_first_dup (n :: seen) rest

(* A cell's JSON is a scalar, and normalisation moves only object members. *)
[@@ noextract_to "FSharp"]
let rec normalise_values (#num #flt: eqtype) (w: wire num flt) (h: host num flt)
                         (ty: column_type) (cs: list (cell num flt))
  : Lemma (ensures normalise_items w (values_json h ty cs) == values_json h ty cs)
          (decreases cs) =
  match cs with
  | [] -> ()
  | _ :: t -> normalise_values w h ty t

[@@ noextract_to "FSharp"]
let rec normalise_validity (#num #flt: eqtype) (w: wire num flt) (cs: list (cell num flt))
  : Lemma (ensures normalise_items w (validity_json #num #flt cs) == validity_json cs)
          (decreases cs) =
  match cs with
  | [] -> ()
  | _ :: t -> normalise_validity w t

#push-options "--fuel 3 --ifuel 2"

[@@ noextract_to "FSharp"]
let column_parts_of_normalised (#num #flt: eqtype) (w: wire num flt) (h: host num flt)
                               (c: column num flt)
  : Lemma (ensures column_parts (normalise w (column_json h c)) == column_parts (column_json h c)) =
  let fs : list (list ch & jval num flt) =
    [ (values_key, JArr (values_json h c.ctype c.cells));
      (validity_key, JArr (validity_json c.cells)) ] in
  keys_differ ();
  find_in_normalised w values_key fs;
  find_in_normalised w validity_key fs;
  normalise_values w h c.ctype c.cells;
  normalise_validity w c.cells

[@@ noextract_to "FSharp"]
let rec decode_schema_of_normalised (#num #flt: eqtype) (w: wire num flt)
                                    (s: list (list ch & column_type))
  : Lemma (ensures decode_schema_items (normalise_items w (schema_json_items #num #flt s))
                   == Good s)
          (decreases s) =
  match s with
  | [] -> ()
  | (n, ty) :: t ->
      let fs : list (list ch & jval num flt) =
        [ (name_key, JStr n); (type_key, JStr (tag ty)) ] in
      keys_differ ();
      find_in_normalised w name_key fs;
      find_in_normalised w type_key fs;
      of_tag_inverts_tag ty;
      decode_schema_of_normalised #num #flt w t

#pop-options

[@@ noextract_to "FSharp"]
let decode_column_of_normalised (#num #flt: eqtype) (w: wire num flt) (h: host num flt)
                                (s: list (list ch & column_type)) (cs: list (column num flt))
                                (n: list ch) (ty: column_type)
  : Lemma (requires distinct (schema_names s))
          (ensures decode_column h (normalise w (JObj (columns_json h s cs))) n ty
                   == decode_column h (JObj (columns_json h s cs)) n ty) =
  columns_json_keys h s cs;
  find_in_normalised w n (columns_json h s cs);
  find_in_columns_json h n s cs;
  column_parts_of_normalised w h (column_or_placeholder n cs)

[@@ noextract_to "FSharp"]
let rec decode_columns_of_normalised (#num #flt: eqtype) (w: wire num flt) (h: host num flt)
                                     (s: list (list ch & column_type))
                                     (cs: list (column num flt))
                                     (rest: list (list ch & column_type))
  : Lemma (requires distinct (schema_names s))
          (ensures decode_columns h (normalise w (JObj (columns_json h s cs))) rest
                   == decode_columns h (JObj (columns_json h s cs)) rest)
          (decreases rest) =
  match rest with
  | [] -> ()
  | (n, ty) :: t ->
      decode_column_of_normalised w h s cs n ty;
      decode_columns_of_normalised w h s cs t

(* The three members `decodeJson` asks the root for, in the sorted document: the schema and the
   column map are there, normalised, and there is no `ref`. *)
#push-options "--fuel 3 --ifuel 2"

[@@ noextract_to "FSharp"]
let root_members_of_normalised (#num #flt: eqtype) (w: wire num flt) (h: host num flt)
                               (t: table num flt)
  : Lemma (ensures (let el = normalise w (encode_json h (Embedded t)) in
                    try_prop schema_key el
                    == Some (JArr (normalise_items w (schema_json_items t.schema))) /\
                    None? (try_prop ref_key el) /\
                    get_field columns_key el
                    == Good (normalise w (JObj (columns_json h t.schema t.columns))))) =
  let fs : list (list ch & jval num flt) =
    [ (schema_key, JArr (schema_json_items t.schema));
      (columns_key, JObj (columns_json h t.schema t.columns)) ] in
  keys_differ ();
  find_in_normalised w schema_key fs;
  find_in_normalised w ref_key fs;
  find_in_normalised w columns_key fs

(* The `ref` source likewise — two members, read by name. *)
let ref_round_trip_through_normalise (#num #flt: eqtype) (w: wire num flt) (h: host num flt)
                                     (r: list ch)
  : Lemma (ensures decode_json h (normalise w (encode_json h (Ref #num #flt r))) == Good (Ref r)) =
  let fs : list (list ch & jval num flt) = [ (schema_key, JArr []); (ref_key, JStr r) ] in
  keys_differ ();
  find_in_normalised w schema_key fs;
  find_in_normalised w ref_key fs

#pop-options

(* The sorted column map still names no column twice. *)
[@@ noextract_to "FSharp"]
let unique_keys_of_normalised (#num #flt: eqtype) (w: wire num flt)
                              (cols: list (list ch & jval num flt))
  : Lemma (requires distinct (keys_of cols))
          (ensures unique_column_keys (normalise w (JObj cols)) == Good (normalise w (JObj cols))) =
  sorted_distinct w cols;
  normalise_keeps_keys w (sort_kvs w cols);
  distinct_no_first_dup [] (keys_of (sort_kvs w cols))

(* ---- THEOREM 2, UP TO MEMBER ORDER ----

   The round trip survives `WireCanon.normalise`: decode the encoding with every object's members
   sorted into canonical key order — schema entries, the column map, each column's two arrays —
   and the answer is the one theorem 2 gives. For EVERY comparator; no premise about the key
   order is needed, because every member is read by name and `validate` made the names distinct. *)
let round_trip_through_normalise (#num #flt: eqtype) (w: wire num flt) (h: host num flt)
                                 (t: table num flt)
  : Lemma (requires host_ok h /\ validate h t == Good ())
          (ensures decode_json h (normalise w (encode_json h (Embedded t)))
                   == Good (Embedded (normal_table h t))) =
  let s = t.schema in
  let cs = t.columns in
  let cols = columns_json h s cs in
  validate_good h t;
  first_dup_none [] (schema_names s);
  root_members_of_normalised w h t;
  decode_schema_of_normalised #num #flt w s;
  columns_json_keys h s cs;
  unique_keys_of_normalised w cols;
  decode_columns_of_normalised w h s cs s;
  validated_entries_ok h cs s;
  decode_columns_inverts h s cs s;
  encode_image_revalidates h t;
  decode_json_embedded h (normalise w (encode_json h (Embedded t)))
    (JArr (normalise_items w (schema_json_items s))) (normalise w (JObj cols)) s
    (normal_columns h s cs)

(* ---- THEOREM 2 AT THE STRING, on `WireCanon`'s canonical subset ----

   Render the encoding canonically, read the bytes back, decode: the table's normal form. The
   first premise is `WireCanon`'s (the host's numeral reader inverts its numeral layout); the last
   is the boundary this section's header describes, and is NOT implied by `validate`. *)
let string_round_trip (#num #flt: eqtype) (w: wire num flt) (h: host num flt)
                      (t: table num flt)
  : Lemma (requires tok_read_ok w /\ host_ok h /\ validate h t == Good () /\
                    canonical w (encode_json h (Embedded t)))
          (ensures (match read w (render w (encode_json h (Embedded t))) with
                    | Ok (v, rest) ->
                        Nil? rest /\ decode_json h v == Good (Embedded (normal_table h t))
                    | Error _ -> False)) =
  read_render w (encode_json h (Embedded t));
  round_trip_through_normalise w h t

(* A `ref` source carries no float, so it is canonical on every wire and needs no such premise. *)
let ref_string_round_trip (#num #flt: eqtype) (w: wire num flt) (h: host num flt) (r: list ch)
  : Lemma (requires tok_read_ok w)
          (ensures (match read w (render w (encode_json h (Ref #num #flt r))) with
                    | Ok (v, rest) -> Nil? rest /\ decode_json h v == Good (Ref r)
                    | Error _ -> False)) =
  assert_norm (canonical w (encode_json h (Ref #num #flt r)));
  read_render w (encode_json h (Ref #num #flt r));
  ref_round_trip_through_normalise w h r

(* THE BOUNDARY, exhibited. Where the wire lays the absent slot's `0.0` out with no `.` and no
   `E` — which the shipped layout does: it is `0` — a float column holding ONE `Null` is a table
   `validate` accepts whose encoding is outside `canonical`, so `string_round_trip` says nothing
   about it. *)
[@@ noextract_to "FSharp"]
let a_null_in_a_float_column_is_outside_canonical (#num #flt: eqtype) (w: wire num flt)
                                                  (h: host num flt) (n: list ch)
  : Lemma (requires not (float_canonical w h.zero_float))
          (ensures (let t = single_column_table #num #flt n FloatType [Null] in
                    validate h t == Good () /\
                    not (canonical w (encode_json h (Embedded t))))) =
  let t = single_column_table #num #flt n FloatType [Null] in
  assert_norm (canonical w (encode_json h (Embedded t)) == float_canonical w h.zero_float)

(* What the gap needs, for one cell: a float column reads the INTEGER token a whole-valued float
   renders as back to the same float, wherever the host's `float` of that integer is that float.
   (It is not, for `-0.0`: the token is `0`, the integer is `0`, and `float 0` is `+0.0` — so at
   the string a `Float -0.0` cell comes back `Float 0.0`. Equal under F#'s `=`, a different bit
   pattern.) *)
let float_column_reads_a_whole_token (#num #flt: eqtype) (h: host num flt) (cn: list ch)
                                     (i: num) (f: flt)
  : Lemma (requires h.to_float i == f)
          (ensures decode_cell h cn FloatType (JInt i) == decode_cell h cn FloatType (JFloat f)) = ()
