(*
   ColumnRefinement — the typed-vector column is the cell-list column it replaces: a refinement
   theorem that carries the column proof ladder across the representation change (fuaran-core
   Phase 419).

   WHAT IS MODELLED. The column `src/Fuaran.Core.Column/Column.fs` has held since Phase 417, clause
   for clause: `Validity` and `Validity.isPresent`, the `ColumnData` union (one typed vector and a
   mask per case — since Phase 420 the MATERIALISED mask, `Validity.toMask` of the column's two-case
   `Validity`, whose own model and the three facts that reading rests on are section 7), `Column` with its derived `Type`, `Column.cell`, `Column.toCells`,
   `Column.ofCells` with its private `storageOfCells` (the per-type `pick`, the `fill` loop and the
   type's zero at an absent row), `ColumnData.Equals` through `ColumnStorage.presentEqual`, the
   typed form `Table.firstUncarriableCell` took in that phase (a mask that is not the values'
   length first, then one scan per case at the present rows) with `Table.validate` over it, and
   `ColumnCodec.columnJson`'s two walks off the typed storage, with `encodeJson` / `tryEncode` over
   them. A `Vector<'T>` is read through `Vector.toArray` and nothing else: the model sees a list of
   the elements a vector holds, never its storage — no offset, no sharing, no backing array.

   WHY THE PHASE EXISTS. `WireColumn.fst` (Phase 306) models the codec over a column whose cells are
   a `Cell list`, and proves the codec's image and its round trip there. After Phase 417 the column
   is a typed vector and a mask, and every theorem stated over the list model says nothing about
   the code that runs unless the two representations are shown to be one value. The cheapest sound
   route is one refinement theorem rather than a re-proof: a map from the typed column to the list
   column — `to_list_column`, the column's name, the case's type and `to_cells` — under which
   `validate` and `encode` agree, and which is a bijection on the columns `validate` accepts. Every
   list-level theorem then transfers unchanged, and section 6 states the two of `WireColumn` of the
   typed column through it.

   THE FOUR THEOREMS.

     - `to_cells_of_cells` — `toCells (ofCells n ty cs)`, when it builds, is `norm_cells ty cs`:
       the cells with each widened `Int` normalised EXACTLY as `WireColumn`'s decode normalises it
       (the same `norm_cell`). And it builds exactly when no present cell is outside the column's
       type (`of_cells_good_iff`), so a list column `validate`'s clause (e) passes is one `ofCells`
       builds (`validated_cells_build`).
     - `of_cells_to_cells` — `ofCells n ty (toCells c)` is `Good` of a column EQUAL to `c` under
       `ColumnData.Equals` — one mask, equal at every present row — for every well-formed typed
       column (mask and values one length), and is `c` itself when the absent rows hold the type's
       zero, which is how every column `ofCells` or decode builds holds them (`of_cells_to_cells_exact`).
       Well-formedness is NEEDED (`of_cells_to_cells_needs_wf`): a column whose mask is shorter
       than its values reads its tail as absent, and comes back with a longer mask.
     - `validate_agrees` — the typed `Table.validate` and the list model's agree through the map
       on every table whose columns are well-formed; and a typed table the typed `validate` accepts
       IS well-formed (`validate_t_good_wf`), since clause (e) names a mask of the wrong length
       first. That clause is the one place the two validators differ, and it is reached only by a
       column the list model cannot express — a list column has one length.
     - `encode_agrees` — `columnJson`'s walks off the typed storage write exactly what `cellJson`
       wrote over the cells, so `encodeJson` agrees through the map on EVERY typed table, valid or
       not, well-formed or not: both walk the values' length and read the mask the same way.

   AND WHAT FOLLOWS, FOR THE TYPED COLUMN (section 6). `norm_cells_vacuous`: the widening half of
   `WireColumn`'s normal form is the IDENTITY on a typed column — an `Int` cell comes only from an
   `Ints` column, whose type is `IntType`, where `norm_cell` changes nothing — so the normal form of
   a typed table moves its column ORDER and nothing else (`normal_table_t`, whose columns are the
   table's own, found by schema name: `normal_columns_are_the_columns`). Hence
   `typed_round_trip`: for every well-formed typed table the typed `validate` accepts,
   `decode_json (encode_json_t (TEmbedded t))` is `Good (Embedded (to_list_table (normal_table_t t)))`;
   `typed_round_trip_in_schema_order`: when the table's columns are already in schema order, that
   is `to_list_table t` itself — the LITERAL round trip, which was false of the list column three
   ways and is false of the typed column one way. And `typed_decode_image_is_valid`: a well-formed
   typed table whose image is what decode answered is one the typed `validate` accepts. The decode
   side's own bridge — that the typed column production's decode builds through the typed readers
   has `toCells` equal to the model's decoded cells — is sampled by the differential, as it was
   before this phase: `ProofOracleTests.fs` reads every production column through `Column.toCells`.

   WHAT IS NOT MODELLED, AND WHY IT COSTS THE THEOREMS NOTHING.

     - THE OWNERSHIP CONTRACT. That a vector's contents never change after construction — no public
       member writes, `adopt` takes a fresh array, `Vector.Unsafe.borrow` lends under a written
       rule — is out of this model's reach: the model has no mutation to speak of, so the sentence
       "the vector is its `toArray`" is an assumption here and a law family there. Phase 418 states
       the contract in `STABILITY.md` and ships the consumer-runnable family that hashes every
       column before and after an operation and names the column whose bytes moved; this ladder's
       `column-vector-is-its-contents` row points at it.
     - THE ELEMENT IDENTITY. `VectorElements.equal` reads every NaN as one value and `-0.0` as
       `0.0`; here `flt` is an `eqtype` and `=` is its equality. Sampled, with the host record's
       other fields, as `WireColumn` already samples them.
     - COUNTS. `Validity.isPresent` compares an index with a length; `presentEqual` compares two
       lengths; `firstUncarriableCell` compares the mask's with the values'. Each is written here
       as the structural walk that is true exactly when the counts agree (`same_len`, as
       `WireColumn` writes `ragged`), because the extraction carries no integers (README, finding
       2). The one index the model keeps, `cell_at`'s, is proof-only.
     - `Column.aggregate`, the typed builders and readers (`Column.ofInts` … `Column.tryDecimals`,
       which check nothing and convert nothing), `Vector`'s own module, and the `float` guard
       `firstUncarriableCell` asks before `JVal.nonFiniteToken` (the token is total on what the
       scan found, so the model asks the host's `finite` once).
     - THE TEMPORAL ENCODINGS. Dates and timestamps are canonical text in this slot; Phase 422 makes
       them integers and gives a timestamp a unit, and its model lives in this module when it lands.

   WHY `WireColumn` IS OPENED. For the cell, the column, the table, the host record, the error
   type and the normal form the theorems are stated against — so that the refinement is a statement
   about THAT model and not a coercion between two. Of its PROOFS, section 6 takes `round_trip`,
   `decode_image_is_valid` and `first_dup_none`; nothing else.

   HOW TO READ IT. Every definition names its F# counterpart in the comment above it. Section 1 is
   the typed column and the map, 2 `toCells` and `ofCells`, 3 the typed `validate`, 4 the typed
   encoder, 5 the four theorems, 6 what follows for the typed column, 7 the two-case validity
   (Phase 420), 8 the twins. No `assume`, no
   `admit`; every fuel setting is scoped to the computations that need it.

   Apache-2.0, like everything beside it.
*)
module ColumnRefinement

#set-options "--ext context_pruning"

open WireCanon
open WireColumn

(* ======================================================================================
   1. THE TYPED COLUMN (F#: `Validity`, `ColumnData`, `Column`), and the map to the list column.
   ====================================================================================== *)

(* F#: the MATERIALISED mask, `Validity.toMask (Column.length c) (Column.validity c)` (`Column.mask`),
   read through `Vector.toArray`: one bool per row, `true` where the row is present. Phase 420 made
   F#'s `Validity` two cases, `AllValid` (no mask held) or a `Mask`; section 7 models those cases and
   proves that reading them through `to_mask` is sound. *)
type validity = list bool

(* F#: `Validity.isPresent i (Mask v)` = `i >= 0 && i < v.Length && v[i]`, read over the
   materialised mask — total, `false` off the end.
   PROOF-ONLY: the one index the model keeps, for the statement that the O(1) read is the list read. *)
[@@ noextract_to "FSharp"]
let rec is_present (i: nat) (v: validity) : Tot bool (decreases v) =
  match v with
  | [] -> false
  | b :: t -> if i = 0 then b else is_present (i - 1) t

(* F#: `ColumnData`, case for case and in declaration order. Each case is its `values` vector read
   through `toArray` — the list of its elements — beside its mask. *)
type column_data (num flt: eqtype) =
  | Ints       : values:list num       -> mask:validity -> column_data num flt
  | Floats     : values:list flt       -> mask:validity -> column_data num flt
  | Bools      : values:list bool      -> mask:validity -> column_data num flt
  | Strs       : values:list (list ch) -> mask:validity -> column_data num flt
  | Dates      : values:list (list ch) -> mask:validity -> column_data num flt
  | Timestamps : values:list (list ch) -> mask:validity -> column_data num flt
  | Decimals   : values:list (list ch) -> mask:validity -> column_data num flt

(* F#: `ColumnData.Type` — the case. *)
let data_type (#num #flt: eqtype) (d: column_data num flt) : Tot column_type =
  match d with
  | Ints _ _ -> IntType
  | Floats _ _ -> FloatType
  | Bools _ _ -> BoolType
  | Strs _ _ -> StringType
  | Dates _ _ -> DateType
  | Timestamps _ _ -> TimestampType
  | Decimals _ _ -> DecimalType

(* F#: `ColumnData.Validity`. *)
let data_mask (#num #flt: eqtype) (d: column_data num flt) : Tot validity =
  match d with
  | Ints _ v -> v
  | Floats _ v -> v
  | Bools _ v -> v
  | Strs _ v -> v
  | Dates _ v -> v
  | Timestamps _ v -> v
  | Decimals _ v -> v

(* F#: `List.length xs` as a list — the count the extraction cannot carry, written as the shape
   `same_len` compares (the header's "counts"). *)
let rec units (#a: Type) (xs: list a) : Tot (list unit) (decreases xs) =
  match xs with
  | [] -> []
  | _ :: t -> () :: units t

(* F#: `ColumnData.Length` — the values vector's length, as a list of that length. *)
let data_len (#num #flt: eqtype) (d: column_data num flt) : Tot (list unit) =
  match d with
  | Ints xs _ -> units xs
  | Floats xs _ -> units xs
  | Bools xs _ -> units xs
  | Strs xs _ -> units xs
  | Dates xs _ -> units xs
  | Timestamps xs _ -> units xs
  | Decimals xs _ -> units xs

(* The mask is the values' length — what `Table.validate`'s clause (e) names first when it is not
   (`LengthMismatch`), and the premise under which the map is a bijection. *)
let data_wf (#num #flt: eqtype) (d: column_data num flt) : Tot bool =
  match d with
  | Ints xs v -> same_len xs v
  | Floats xs v -> same_len xs v
  | Bools xs v -> same_len xs v
  | Strs xs v -> same_len xs v
  | Dates xs v -> same_len xs v
  | Timestamps xs v -> same_len xs v
  | Decimals xs v -> same_len xs v

(* F#: `Column = { Name; Data }`; `Column.Type` is `Data.Type`. *)
type typed_column (num flt: eqtype) = {
  col_name: list ch;
  col_data: column_data num flt;
}

let col_type (#num #flt: eqtype) (c: typed_column num flt) : Tot column_type = data_type c.col_data
let col_mask (#num #flt: eqtype) (c: typed_column num flt) : Tot validity = data_mask c.col_data
let wf (#num #flt: eqtype) (c: typed_column num flt) : Tot bool = data_wf c.col_data

(* F#: `{ Schema; Columns }` over typed columns, and `DataSource` over it. *)
type typed_table (num flt: eqtype) = {
  tschema: list (list ch & column_type);
  tcolumns: list (typed_column num flt);
}

type typed_source (num flt: eqtype) =
  | TEmbedded : t:typed_table num flt -> typed_source num flt
  | TRef      : r:list ch -> typed_source num flt

let rec all_wf (#num #flt: eqtype) (cs: list (typed_column num flt)) : Tot bool (decreases cs) =
  match cs with
  | [] -> true
  | c :: t -> wf c && all_wf t

(* ---- F#: `Column.toCells` ----

   `for i in length c - 1 .. -1 .. 0 do acc <- cell i c :: acc` — one cell per element of the
   values vector, in order: the element in its case where the mask says the row is present, `Null`
   where it says absent, and `Null` where the mask has ended (`isPresent` is `false` off its end).
   Written as the walk over the two lists it is equal to. *)
let rec cells_of (#num #flt: eqtype) (#a: Type) (mk: a -> cell num flt) (xs: list a) (v: validity)
  : Tot (list (cell num flt)) (decreases xs) =
  match xs with
  | [] -> []
  | x :: xt ->
      (match v with
       | true :: vt -> mk x :: cells_of mk xt vt
       | false :: vt -> Null :: cells_of mk xt vt
       | [] -> Null :: cells_of mk xt [])

let to_cells (#num #flt: eqtype) (c: typed_column num flt) : Tot (list (cell num flt)) =
  match c.col_data with
  | Ints xs v -> cells_of Int xs v
  | Floats xs v -> cells_of Float xs v
  | Bools xs v -> cells_of Bool xs v
  | Strs xs v -> cells_of Str xs v
  | Dates xs v -> cells_of Date xs v
  | Timestamps xs v -> cells_of Timestamp xs v
  | Decimals xs v -> cells_of Decimal xs v

(* THE REFINEMENT MAP. The typed column as the list column `WireColumn` models: its name, the type
   its case carries, and its cells. *)
let to_list_column (#num #flt: eqtype) (c: typed_column num flt) : Tot (column num flt) =
  { name = c.col_name; ctype = col_type c; cells = to_cells c }

let rec to_list_columns (#num #flt: eqtype) (cs: list (typed_column num flt))
  : Tot (list (column num flt)) (decreases cs) =
  match cs with
  | [] -> []
  | c :: t -> to_list_column c :: to_list_columns t

let to_list_table (#num #flt: eqtype) (t: typed_table num flt) : Tot (table num flt) =
  { schema = t.tschema; columns = to_list_columns t.tcolumns }

let to_list_source (#num #flt: eqtype) (src: typed_source num flt) : Tot (data_source num flt) =
  match src with
  | TEmbedded t -> Embedded (to_list_table t)
  | TRef r -> Ref r

(* ---- F#: `Column.cell i c` — the O(1) read: one mask read, one vector read. PROOF-ONLY. ---- *)

[@@ noextract_to "FSharp"]
let rec nth (#a: Type) (xs: list a) (i: nat) : Tot (option a) (decreases xs) =
  match xs with
  | [] -> None
  | x :: t -> if i = 0 then Some x else nth t (i - 1)

[@@ noextract_to "FSharp"]
let cell_at (#num #flt: eqtype) (i: nat) (c: typed_column num flt) : Tot (cell num flt) =
  if not (is_present i (col_mask c)) then Null
  else
    (match c.col_data with
     | Ints xs _ -> (match nth xs i with Some x -> Int x | None -> Null)
     | Floats xs _ -> (match nth xs i with Some x -> Float x | None -> Null)
     | Bools xs _ -> (match nth xs i with Some x -> Bool x | None -> Null)
     | Strs xs _ -> (match nth xs i with Some x -> Str x | None -> Null)
     | Dates xs _ -> (match nth xs i with Some x -> Date x | None -> Null)
     | Timestamps xs _ -> (match nth xs i with Some x -> Timestamp x | None -> Null)
     | Decimals xs _ -> (match nth xs i with Some x -> Decimal x | None -> Null))

(* The indexed read IS the list read: at every row the vector has, `cell i c` is the i-th of
   `toCells c`. (A present row past the vector's end is production's out-of-range read, which the
   mask of a well-formed column never says; the model answers `Null` there and the lemma does not
   speak of it.) *)
[@@ noextract_to "FSharp"]
let rec cells_of_nth (#num #flt: eqtype) (#a: Type) (mk: a -> cell num flt) (xs: list a)
                     (v: validity) (i: nat)
  : Lemma (ensures nth (cells_of mk xs v) i ==
                   (match nth xs i with
                    | None -> None
                    | Some x -> Some (if is_present i v then mk x else Null)))
          (decreases xs) =
  match xs with
  | [] -> ()
  | x :: xt ->
      if i = 0 then ()
      else
        (match v with
         | _ :: vt -> cells_of_nth mk xt vt (i - 1)
         | [] -> cells_of_nth mk xt [] (i - 1))

[@@ noextract_to "FSharp"]
let cell_at_is_nth_to_cells (#num #flt: eqtype) (c: typed_column num flt) (i: nat)
  : Lemma (requires Some? (nth (to_cells c) i))
          (ensures nth (to_cells c) i == Some (cell_at i c)) =
  match c.col_data with
  | Ints xs v -> cells_of_nth (Int #num #flt) xs v i
  | Floats xs v -> cells_of_nth (Float #num #flt) xs v i
  | Bools xs v -> cells_of_nth (Bool #num #flt) xs v i
  | Strs xs v -> cells_of_nth (Str #num #flt) xs v i
  | Dates xs v -> cells_of_nth (Date #num #flt) xs v i
  | Timestamps xs v -> cells_of_nth (Timestamp #num #flt) xs v i
  | Decimals xs v -> cells_of_nth (Decimal #num #flt) xs v i

(* ======================================================================================
   2. `Column.ofCells` (F#: `storageOfCells` — `outside`, the per-type `pick`, `fill`) and
      `ColumnData.Equals` (F#: `ColumnStorage.presentEqual`).
   ====================================================================================== *)

(* F#: what one arm's `pick` answers for a cell — `Ok (Some v)`, the typed value of a present cell
   that fits (a widened `Int` already converted); `Ok None`, an absent row; `Error t`, the type of
   a present cell that does not fit. *)
type picked (a: Type) =
  | Fits    : v:a -> picked a
  | Absent  : picked a
  | Outside : t:column_type -> picked a

(* F#: `outside` — `Cell.typeOf c`: `Some t` is the type that does not fit, `None` (only `Null`)
   an absent row. *)
let outside (#a: Type) (#num #flt: eqtype) (c: cell num flt) : Tot (picked a) =
  match type_of c with
  | Some t -> Outside t
  | None -> Absent

(* F#: the seven `pick` closures, arm for arm. *)
let pick_int (#num #flt: eqtype) (c: cell num flt) : Tot (picked num) =
  match c with
  | Int i -> Fits i
  | _ -> outside c

let pick_float (#num #flt: eqtype) (h: host num flt) (c: cell num flt) : Tot (picked flt) =
  match c with
  | Float f -> Fits f
  | Int i -> Fits (h.to_float i)
  | _ -> outside c

let pick_bool (#num #flt: eqtype) (c: cell num flt) : Tot (picked bool) =
  match c with
  | Bool b -> Fits b
  | _ -> outside c

let pick_str (#num #flt: eqtype) (c: cell num flt) : Tot (picked (list ch)) =
  match c with
  | Str s -> Fits s
  | _ -> outside c

let pick_date (#num #flt: eqtype) (c: cell num flt) : Tot (picked (list ch)) =
  match c with
  | Date s -> Fits s
  | _ -> outside c

let pick_timestamp (#num #flt: eqtype) (c: cell num flt) : Tot (picked (list ch)) =
  match c with
  | Timestamp s -> Fits s
  | _ -> outside c

let pick_decimal (#num #flt: eqtype) (h: host num flt) (c: cell num flt) : Tot (picked (list ch)) =
  match c with
  | Decimal s -> Fits s
  | Int i -> Fits (h.int_text i)
  | _ -> outside c

(* F#: `fill` — row by row, stopping at the first cell that does not fit (`TypeMismatch`, with the
   type that did not fit dropped as every error's prose is): the two arrays it writes, the value
   or the type's zero per row and the mask bit per row. *)
let rec fill (#a: Type) (#num #flt: eqtype) (name: list ch) (ty: column_type) (zero: a)
             (pick: cell num flt -> picked a) (cs: list (cell num flt))
  : Tot (res (list a & validity)) (decreases cs) =
  match cs with
  | [] -> Good ([], [])
  | c :: t ->
      (match pick c with
       | Outside _ -> Bad (TypeMismatch name ty)
       | Fits v ->
           (match fill name ty zero pick t with
            | Good (xs, m) -> Good (v :: xs, true :: m)
            | Bad e -> Bad e)
       | Absent ->
           (match fill name ty zero pick t with
            | Good (xs, m) -> Good (zero :: xs, false :: m)
            | Bad e -> Bad e))

(* F#: `storageOfCells`, arm for arm; the zero of each arm is `absentSlot`'s value. *)
let storage_of_cells (#num #flt: eqtype) (h: host num flt) (name: list ch) (ty: column_type)
                     (cs: list (cell num flt))
  : Tot (res (column_data num flt)) =
  match ty with
  | IntType ->
      (match fill name ty h.zero_int pick_int cs with
       | Good (xs, m) -> Good (Ints xs m)
       | Bad e -> Bad e)
  | FloatType ->
      (match fill name ty h.zero_float (pick_float h) cs with
       | Good (xs, m) -> Good (Floats xs m)
       | Bad e -> Bad e)
  | BoolType ->
      (match fill name ty false pick_bool cs with
       | Good (xs, m) -> Good (Bools xs m)
       | Bad e -> Bad e)
  | StringType ->
      (match fill name ty [] pick_str cs with
       | Good (xs, m) -> Good (Strs xs m)
       | Bad e -> Bad e)
  | DateType ->
      (match fill name ty [] pick_date cs with
       | Good (xs, m) -> Good (Dates xs m)
       | Bad e -> Bad e)
  | TimestampType ->
      (match fill name ty [] pick_timestamp cs with
       | Good (xs, m) -> Good (Timestamps xs m)
       | Bad e -> Bad e)
  | DecimalType ->
      (match fill name ty dec_zero (pick_decimal h) cs with
       | Good (xs, m) -> Good (Decimals xs m)
       | Bad e -> Bad e)

(* F#: `Column.ofCells`. *)
let of_cells (#num #flt: eqtype) (h: host num flt) (name: list ch) (ty: column_type)
             (cs: list (cell num flt))
  : Tot (res (typed_column num flt)) =
  match storage_of_cells h name ty cs with
  | Good d -> Good ({ col_name = name; col_data = d })
  | Bad e -> Bad e

(* F#: `presentEqual`'s loop — equal at every row the mask says is present, `false` off the mask's
   end as `isPresent` is. *)
let rec eq_at_present (#a: eqtype) (xs ys: list a) (v: validity) : Tot bool (decreases xs) =
  match xs, ys with
  | x :: xt, y :: yt ->
      (match v with
       | true :: vt -> x = y && eq_at_present xt yt vt
       | false :: vt -> eq_at_present xt yt vt
       | [] -> true)
  | _ -> true

(* F#: `ColumnStorage.presentEqual` — one length, one mask, equal at every present row. *)
let present_equal (#a: eqtype) (xs: list a) (vx: validity) (ys: list a) (vy: validity) : Tot bool =
  same_len xs ys && vx = vy && eq_at_present xs ys vx

(* F#: `ColumnData.Equals` — the same case, and `presentEqual` over it. *)
let data_eq (#num #flt: eqtype) (d e: column_data num flt) : Tot bool =
  match d, e with
  | Ints xs vx, Ints ys vy -> present_equal xs vx ys vy
  | Floats xs vx, Floats ys vy -> present_equal xs vx ys vy
  | Bools xs vx, Bools ys vy -> present_equal xs vx ys vy
  | Strs xs vx, Strs ys vy -> present_equal xs vx ys vy
  | Dates xs vx, Dates ys vy -> present_equal xs vx ys vy
  | Timestamps xs vx, Timestamps ys vy -> present_equal xs vx ys vy
  | Decimals xs vx, Decimals ys vy -> present_equal xs vx ys vy
  | _ -> false

(* F#: the record's structural `=` over `Column`: the name, and `Data.Equals`. *)
let col_eq (#num #flt: eqtype) (c d: typed_column num flt) : Tot bool =
  c.col_name = d.col_name && data_eq c.col_data d.col_data

(* ======================================================================================
   3. THE TYPED `Table.validate` (F#: `firstUncarriableCell` as Phase 417 left it, and
      `validate`'s clauses (a)-(e) over typed columns).
   ====================================================================================== *)

(* F#: `firstPresent xs bad` — is there a present row whose element `bad` names? `isPresent` is
   `false` off the mask's end, so the scan stops reading there. *)
let rec first_present_bad (#a: Type) (bad: a -> bool) (xs: list a) (v: validity)
  : Tot bool (decreases xs) =
  match xs with
  | [] -> false
  | x :: xt ->
      (match v with
       | true :: vt -> if bad x then true else first_present_bad bad xt vt
       | false :: vt -> first_present_bad bad xt vt
       | [] -> false)

(* F#: the four `bad` predicates the scans ask, named so that the lemmas below ask the same term:
   `IsNaN f || IsInfinity f` (asked here of the host's `finite`, the one reading the model has), and
   `not (isCanonical s)` for the three texts. *)
let not_finite (#num #flt: eqtype) (h: host num flt) (f: flt) : Tot bool = not (h.finite f)
let not_canonical (s: list ch) : Tot bool = not (is_canonical s)
let not_date (#num #flt: eqtype) (h: host num flt) (s: list ch) : Tot bool = not (h.is_date s)
let not_timestamp (#num #flt: eqtype) (h: host num flt) (s: list ch) : Tot bool = not (h.is_timestamp s)

(* F#: `Table.firstUncarriableCell` — a mask that is not the values' length first
   (`LengthMismatch`), then the case's scan at the present rows: an int, bool or string column has
   nothing to refuse; a float column a non-finite value (`NonFiniteFloat`); a decimal, date or
   timestamp column a text that is not its canonical form (`MalformedShape`). A cell of another type
   is not a case: the storage cannot hold one. *)
let first_uncarriable_t (#num #flt: eqtype) (h: host num flt) (c: typed_column num flt)
  : Tot (option column_error) =
  if not (wf c) then Some (LengthMismatch c.col_name)
  else
    (match c.col_data with
     | Ints _ _ -> None
     | Bools _ _ -> None
     | Strs _ _ -> None
     | Floats xs v ->
         if first_present_bad (not_finite h) xs v then Some (NonFiniteFloat c.col_name) else None
     | Decimals xs v ->
         if first_present_bad not_canonical xs v then Some MalformedShape else None
     | Dates xs v ->
         if first_present_bad (not_date h) xs v then Some MalformedShape else None
     | Timestamps xs v ->
         if first_present_bad (not_timestamp h) xs v then Some MalformedShape else None)

(* F#: `t.Columns |> List.map _.Name`. *)
let rec t_column_names (#num #flt: eqtype) (cs: list (typed_column num flt))
  : Tot (list (list ch)) (decreases cs) =
  match cs with
  | [] -> []
  | c :: t -> c.col_name :: t_column_names t

(* F#: `t.Columns |> List.tryFind (fun c -> c.Name = name)`. *)
let rec t_find_column (#num #flt: eqtype) (n: list ch) (cs: list (typed_column num flt))
  : Tot (option (typed_column num flt)) (decreases cs) =
  match cs with
  | [] -> None
  | c :: t -> if c.col_name = n then Some c else t_find_column n t

(* F#: `validate`'s `typeFault` — `c.Type <> ty`, the type the case carries against the schema's. *)
let rec t_type_fault (#num #flt: eqtype) (s: list (list ch & column_type))
                     (cs: list (typed_column num flt))
  : Tot (option column_error) (decreases s) =
  match s with
  | [] -> None
  | (n, ty) :: rest ->
      (match t_find_column n cs with
       | Some c -> if col_type c <> ty then Some (TypeMismatch n ty) else t_type_fault rest cs
       | None -> t_type_fault rest cs)

(* F#: `validate`'s `ragged` — `Column.length c <> len0`, every column's values' length against
   the first's. *)
let rec t_first_ragged (#num #flt: eqtype) (len0: list unit) (rest: list (typed_column num flt))
  : Tot (option column_error) (decreases rest) =
  match rest with
  | [] -> None
  | c :: t ->
      if same_len len0 (data_len c.col_data) then t_first_ragged len0 t
      else Some (RaggedColumns c.col_name)

let t_ragged (#num #flt: eqtype) (cs: list (typed_column num flt)) : Tot (option column_error) =
  match cs with
  | [] -> None
  | first :: rest -> t_first_ragged (data_len first.col_data) rest

(* F#: `validate`'s `cellFault` — schema order, then `firstUncarriableCell`. *)
let rec t_cells_fault (#num #flt: eqtype) (h: host num flt) (s: list (list ch & column_type))
                      (cs: list (typed_column num flt))
  : Tot (option column_error) (decreases s) =
  match s with
  | [] -> None
  | (n, _) :: rest ->
      (match t_find_column n cs with
       | Some c ->
           (match first_uncarriable_t h c with
            | Some e -> Some e
            | None -> t_cells_fault h rest cs)
       | None -> t_cells_fault h rest cs)

(* F#: `Table.validate` over typed columns, clause for clause and in its order (a)-(e). Clauses
   (a)-(d) read a column's name, its type and its length, which the representation change did not
   move; clause (e) is the typed scan above. *)
let validate_t (#num #flt: eqtype) (h: host num flt) (t: typed_table num flt) : Tot (res unit) =
  let sn = schema_names t.tschema in
  let cn = t_column_names t.tcolumns in
  match first_duplicate sn with
  | Some n -> Bad (Malformed (DuplicateSchemaName n))
  | None ->
      (match first_duplicate cn with
       | Some n -> Bad (Malformed (DuplicateColumnName n))
       | None ->
           if Cons? (not_in sn cn) then Bad (Malformed SchemaNameWithoutColumn)
           else if Cons? (not_in cn sn) then Bad (Malformed ColumnOutsideSchema)
           else
             (match t_type_fault t.tschema t.tcolumns with
              | Some e -> Bad e
              | None ->
                  (match t_ragged t.tcolumns with
                   | Some e -> Bad e
                   | None ->
                       (match t_cells_fault h t.tschema t.tcolumns with
                        | Some e -> Bad e
                        | None -> Good ()))))

(* ======================================================================================
   4. THE TYPED ENCODER (F#: `ColumnCodec.columnJson`'s two walks off the typed storage, and
      `encodeJson` / `tryEncode` over them).
   ====================================================================================== *)

(* F#: `List.init n (fun i -> if present i then <wrap> xs[i] else absentSlot ty)` — the element's
   JSON value at a present row, the absent slot at an absent one and off the mask's end, never what
   the vector holds there. *)
let rec values_json_t (#a: Type) (#num #flt: eqtype) (h: host num flt) (ty: column_type)
                      (wrap: a -> jval num flt) (xs: list a) (v: validity)
  : Tot (list (jval num flt)) (decreases xs) =
  match xs with
  | [] -> []
  | x :: xt ->
      (match v with
       | true :: vt -> wrap x :: values_json_t h ty wrap xt vt
       | false :: vt -> absent_slot h ty :: values_json_t h ty wrap xt vt
       | [] -> absent_slot h ty :: values_json_t h ty wrap xt [])

(* F#: `List.init n (fun i -> JBool (present i))`, over the values' length. *)
let rec validity_json_t (#num #flt: eqtype) (n: list unit) (v: validity)
  : Tot (list (jval num flt)) (decreases n) =
  match n with
  | [] -> []
  | _ :: nt ->
      (match v with
       | b :: vt -> JBool b :: validity_json_t nt vt
       | [] -> JBool false :: validity_json_t nt [])

(* F#: `columnJson`. *)
let column_json_t (#num #flt: eqtype) (h: host num flt) (c: typed_column num flt)
  : Tot (jval num flt) =
  let values : list (jval num flt) =
    (match c.col_data with
     | Ints xs v -> values_json_t h IntType JInt xs v
     | Floats xs v -> values_json_t h FloatType JFloat xs v
     | Bools xs v -> values_json_t h BoolType JBool xs v
     | Strs xs v -> values_json_t h StringType JStr xs v
     | Dates xs v -> values_json_t h DateType JStr xs v
     | Timestamps xs v -> values_json_t h TimestampType JStr xs v
     | Decimals xs v -> values_json_t h DecimalType JStr xs v) in
  JObj [ (values_key, JArr values);
         (validity_key, JArr (validity_json_t (data_len c.col_data) (col_mask c))) ]

(* F#: the column `encodeJson` writes for a schema name, and the empty string column it papers a
   missing one over with (`Column.ofStrs name Vector.empty Vector.empty`). *)
let t_column_or_placeholder (#num #flt: eqtype) (n: list ch) (cs: list (typed_column num flt))
  : Tot (typed_column num flt) =
  match t_find_column n cs with
  | Some c -> c
  | None -> { col_name = n; col_data = Strs [] [] }

(* F#: the `t.Schema |> List.map (fun (name, _) -> …)` of `encodeJson`. *)
let rec columns_json_t (#num #flt: eqtype) (h: host num flt) (s: list (list ch & column_type))
                       (cs: list (typed_column num flt))
  : Tot (list (list ch & jval num flt)) (decreases s) =
  match s with
  | [] -> []
  | (n, _) :: t -> (n, column_json_t h (t_column_or_placeholder n cs)) :: columns_json_t h t cs

(* F#: `ColumnCodec.encodeJson`. *)
let encode_json_t (#num #flt: eqtype) (h: host num flt) (src: typed_source num flt)
  : Tot (jval num flt) =
  match src with
  | TEmbedded t ->
      JObj [ (schema_key, JArr (schema_json_items t.tschema));
             (columns_key, JObj (columns_json_t h t.tschema t.tcolumns)) ]
  | TRef r -> JObj [ (schema_key, JArr []); (ref_key, JStr r) ]

(* F#: `ColumnCodec.tryEncode`, at the `JVal`. *)
let try_encode_json_t (#num #flt: eqtype) (h: host num flt) (src: typed_source num flt)
  : Tot (res (jval num flt)) =
  match src with
  | TRef _ -> Good (encode_json_t h src)
  | TEmbedded t ->
      (match validate_t h t with
       | Bad e -> Bad e
       | Good _ -> Good (encode_json_t h src))

(* ======================================================================================
   5. THE FOUR THEOREMS.
   ====================================================================================== *)

(* ---- 5a. list facts: `same_len` is length equality, and the walks keep the values' length ---- *)

[@@ noextract_to "FSharp"]
let rec same_len_is_length (#a #b: Type) (xs: list a) (ys: list b)
  : Lemma (ensures same_len xs ys <==> List.Tot.length xs = List.Tot.length ys) (decreases xs) =
  match xs, ys with
  | _ :: xt, _ :: yt -> same_len_is_length xt yt
  | _ -> ()

[@@ noextract_to "FSharp"]
let rec units_length (#a: Type) (xs: list a)
  : Lemma (ensures List.Tot.length (units xs) = List.Tot.length xs) (decreases xs) =
  match xs with
  | [] -> ()
  | _ :: t -> units_length t

[@@ noextract_to "FSharp"]
let rec cells_of_length (#num #flt: eqtype) (#a: Type) (mk: a -> cell num flt) (xs: list a)
                        (v: validity)
  : Lemma (ensures List.Tot.length (cells_of mk xs v) = List.Tot.length xs) (decreases xs) =
  match xs with
  | [] -> ()
  | _ :: xt ->
      (match v with
       | _ :: vt -> cells_of_length mk xt vt
       | [] -> cells_of_length mk xt [])

(* `toCells` has the values' length — the length every clause of `validate` compares. *)
[@@ noextract_to "FSharp"]
let to_cells_length (#num #flt: eqtype) (c: typed_column num flt)
  : Lemma (ensures List.Tot.length (to_cells c) = List.Tot.length (data_len c.col_data)) =
  match c.col_data with
  | Ints xs v -> cells_of_length (Int #num #flt) xs v; units_length xs
  | Floats xs v -> cells_of_length (Float #num #flt) xs v; units_length xs
  | Bools xs v -> cells_of_length (Bool #num #flt) xs v; units_length xs
  | Strs xs v -> cells_of_length (Str #num #flt) xs v; units_length xs
  | Dates xs v -> cells_of_length (Date #num #flt) xs v; units_length xs
  | Timestamps xs v -> cells_of_length (Timestamp #num #flt) xs v; units_length xs
  | Decimals xs v -> cells_of_length (Decimal #num #flt) xs v; units_length xs

(* ---- 5b. THEOREM `to_cells_of_cells`: `toCells (ofCells n ty cs)` is `norm_cells ty cs` ----

   For each arm, what `pick` answers and what the case wraps it back into agree with
   `WireColumn.norm_cell`: a cell that fits comes back as its normal form, an absent row is `Null`. *)

[@@ noextract_to "FSharp"]
let pick_agrees (#a: Type) (#num #flt: eqtype) (h: host num flt) (ty: column_type)
                (pick: cell num flt -> picked a) (mk: a -> cell num flt) (c: cell num flt) : prop =
  (match pick c with
   | Fits v -> mk v == norm_cell h ty c
   | Absent -> c == Null
   | Outside _ -> True)

[@@ noextract_to "FSharp"]
let rec fill_cells_of (#a: Type) (#num #flt: eqtype) (h: host num flt) (name: list ch)
                      (ty: column_type) (zero: a) (pick: cell num flt -> picked a)
                      (mk: a -> cell num flt) (cs: list (cell num flt))
  : Lemma (requires (forall (c: cell num flt). pick_agrees h ty pick mk c))
          (ensures (match fill name ty zero pick cs with
                    | Good (xs, m) -> cells_of mk xs m == norm_cells h ty cs
                    | Bad _ -> True))
          (decreases cs) =
  match cs with
  | [] -> ()
  | c :: t -> fill_cells_of h name ty zero pick mk t

#push-options "--ifuel 2"
[@@ noextract_to "FSharp"]
let pick_int_agrees (#num #flt: eqtype) (h: host num flt) (c: cell num flt)
  : Lemma (ensures pick_agrees h IntType (pick_int #num #flt) Int c) = ()

[@@ noextract_to "FSharp"]
let pick_float_agrees (#num #flt: eqtype) (h: host num flt) (c: cell num flt)
  : Lemma (ensures pick_agrees h FloatType (pick_float h) Float c) = ()

[@@ noextract_to "FSharp"]
let pick_bool_agrees (#num #flt: eqtype) (h: host num flt) (c: cell num flt)
  : Lemma (ensures pick_agrees h BoolType (pick_bool #num #flt) Bool c) = ()

[@@ noextract_to "FSharp"]
let pick_str_agrees (#num #flt: eqtype) (h: host num flt) (c: cell num flt)
  : Lemma (ensures pick_agrees h StringType (pick_str #num #flt) Str c) = ()

[@@ noextract_to "FSharp"]
let pick_date_agrees (#num #flt: eqtype) (h: host num flt) (c: cell num flt)
  : Lemma (ensures pick_agrees h DateType (pick_date #num #flt) Date c) = ()

[@@ noextract_to "FSharp"]
let pick_timestamp_agrees (#num #flt: eqtype) (h: host num flt) (c: cell num flt)
  : Lemma (ensures pick_agrees h TimestampType (pick_timestamp #num #flt) Timestamp c) = ()

[@@ noextract_to "FSharp"]
let pick_decimal_agrees (#num #flt: eqtype) (h: host num flt) (c: cell num flt)
  : Lemma (ensures pick_agrees h DecimalType (pick_decimal h) Decimal c) = ()
#pop-options

(* THEOREM. When `ofCells n ty cs` builds, its `toCells` are the cells with each widened `Int`
   normalised exactly as decode normalises it — `WireColumn.norm_cells`, the same function. *)
[@@ noextract_to "FSharp"]
let to_cells_of_cells (#num #flt: eqtype) (h: host num flt) (name: list ch) (ty: column_type)
                      (cs: list (cell num flt))
  : Lemma (ensures (match of_cells h name ty cs with
                    | Good c -> to_cells c == norm_cells h ty cs
                    | Bad _ -> True)) =
  match ty with
  | IntType ->
      FStar.Classical.forall_intro (pick_int_agrees #num #flt h);
      fill_cells_of h name ty h.zero_int pick_int Int cs
  | FloatType ->
      FStar.Classical.forall_intro (pick_float_agrees h);
      fill_cells_of h name ty h.zero_float (pick_float h) Float cs
  | BoolType ->
      FStar.Classical.forall_intro (pick_bool_agrees #num #flt h);
      fill_cells_of h name ty false pick_bool Bool cs
  | StringType ->
      FStar.Classical.forall_intro (pick_str_agrees #num #flt h);
      fill_cells_of h name ty [] pick_str Str cs
  | DateType ->
      FStar.Classical.forall_intro (pick_date_agrees #num #flt h);
      fill_cells_of h name ty [] pick_date Date cs
  | TimestampType ->
      FStar.Classical.forall_intro (pick_timestamp_agrees #num #flt h);
      fill_cells_of h name ty [] pick_timestamp Timestamp cs
  | DecimalType ->
      FStar.Classical.forall_intro (pick_decimal_agrees h);
      fill_cells_of h name ty dec_zero (pick_decimal h) Decimal cs

(* ---- and WHEN it builds: exactly when no present cell is outside the column's type ---- *)

(* A cell the column's type can hold: `Null`, or a type that widens into it (`ColumnType.widens`). *)
let fits (#num #flt: eqtype) (ty: column_type) (c: cell num flt) : Tot bool =
  match type_of c with
  | None -> true
  | Some t -> widens t ty

let rec all_fit (#num #flt: eqtype) (ty: column_type) (cs: list (cell num flt))
  : Tot bool (decreases cs) =
  match cs with
  | [] -> true
  | c :: t -> fits ty c && all_fit ty t

(* Each arm's `pick` answers `Outside` for exactly the cells that do not fit. *)
[@@ noextract_to "FSharp"]
let pick_outside (#a: Type) (#num #flt: eqtype) (ty: column_type)
                 (pick: cell num flt -> picked a) (c: cell num flt) : prop =
  Outside? (pick c) <==> not (fits ty c)

#push-options "--ifuel 2"
[@@ noextract_to "FSharp"]
let pick_int_outside (#num #flt: eqtype) (c: cell num flt)
  : Lemma (ensures pick_outside IntType (pick_int #num #flt) c) = ()

[@@ noextract_to "FSharp"]
let pick_float_outside (#num #flt: eqtype) (h: host num flt) (c: cell num flt)
  : Lemma (ensures pick_outside FloatType (pick_float h) c) = ()

[@@ noextract_to "FSharp"]
let pick_bool_outside (#num #flt: eqtype) (c: cell num flt)
  : Lemma (ensures pick_outside BoolType (pick_bool #num #flt) c) = ()

[@@ noextract_to "FSharp"]
let pick_str_outside (#num #flt: eqtype) (c: cell num flt)
  : Lemma (ensures pick_outside StringType (pick_str #num #flt) c) = ()

[@@ noextract_to "FSharp"]
let pick_date_outside (#num #flt: eqtype) (c: cell num flt)
  : Lemma (ensures pick_outside DateType (pick_date #num #flt) c) = ()

[@@ noextract_to "FSharp"]
let pick_timestamp_outside (#num #flt: eqtype) (c: cell num flt)
  : Lemma (ensures pick_outside TimestampType (pick_timestamp #num #flt) c) = ()

[@@ noextract_to "FSharp"]
let pick_decimal_outside (#num #flt: eqtype) (h: host num flt) (c: cell num flt)
  : Lemma (ensures pick_outside DecimalType (pick_decimal h) c) = ()
#pop-options

[@@ noextract_to "FSharp"]
let rec fill_good_iff (#a: Type) (#num #flt: eqtype) (name: list ch) (ty: column_type) (zero: a)
                      (pick: cell num flt -> picked a) (cs: list (cell num flt))
  : Lemma (requires (forall (c: cell num flt). pick_outside ty pick c))
          (ensures (Good? (fill name ty zero pick cs) <==> all_fit ty cs) /\
                   (not (all_fit ty cs) ==> fill name ty zero pick cs == Bad (TypeMismatch name ty)))
          (decreases cs) =
  match cs with
  | [] -> ()
  | c :: t -> fill_good_iff name ty zero pick t

(* THEOREM. `ofCells n ty cs` builds exactly when every present cell's type widens into `ty`, and
   refuses otherwise as the `TypeMismatch` naming the column and its type. *)
[@@ noextract_to "FSharp"]
let of_cells_good_iff (#num #flt: eqtype) (h: host num flt) (name: list ch) (ty: column_type)
                      (cs: list (cell num flt))
  : Lemma (ensures (Good? (of_cells h name ty cs) <==> all_fit ty cs) /\
                   (not (all_fit ty cs) ==> of_cells h name ty cs == Bad (TypeMismatch name ty))) =
  match ty with
  | IntType ->
      FStar.Classical.forall_intro (pick_int_outside #num #flt);
      fill_good_iff name ty h.zero_int pick_int cs
  | FloatType ->
      FStar.Classical.forall_intro (pick_float_outside h);
      fill_good_iff name ty h.zero_float (pick_float h) cs
  | BoolType ->
      FStar.Classical.forall_intro (pick_bool_outside #num #flt);
      fill_good_iff name ty false pick_bool cs
  | StringType ->
      FStar.Classical.forall_intro (pick_str_outside #num #flt);
      fill_good_iff name ty [] pick_str cs
  | DateType ->
      FStar.Classical.forall_intro (pick_date_outside #num #flt);
      fill_good_iff name ty [] pick_date cs
  | TimestampType ->
      FStar.Classical.forall_intro (pick_timestamp_outside #num #flt);
      fill_good_iff name ty [] pick_timestamp cs
  | DecimalType ->
      FStar.Classical.forall_intro (pick_decimal_outside h);
      fill_good_iff name ty dec_zero (pick_decimal h) cs

(* `WireColumn`'s clause (e) asks `outside` before anything else, so a list column it passes is one
   every cell of fits. *)
[@@ noextract_to "FSharp"]
let rec first_uncarriable_none_fits (#num #flt: eqtype) (h: host num flt) (name: list ch)
                                    (ty: column_type) (cs: list (cell num flt))
  : Lemma (requires None? (first_uncarriable h name ty cs))
          (ensures all_fit ty cs)
          (decreases cs) =
  match cs with
  | [] -> ()
  | c :: t -> first_uncarriable_none_fits h name ty t

(* COROLLARY. On a VALIDATED list column — one `validate`'s clause (e) passes — `ofCells` builds,
   and `toCells` of what it builds is the column's normal form. *)
[@@ noextract_to "FSharp"]
let validated_cells_build (#num #flt: eqtype) (h: host num flt) (name: list ch) (ty: column_type)
                          (cs: list (cell num flt))
  : Lemma (requires None? (first_uncarriable h name ty cs))
          (ensures (match of_cells h name ty cs with
                    | Good c -> c.col_name == name /\ col_type c == ty /\ to_cells c == norm_cells h ty cs
                    | Bad _ -> False)) =
  first_uncarriable_none_fits h name ty cs;
  of_cells_good_iff h name ty cs;
  to_cells_of_cells h name ty cs

(* ---- 5c. THEOREM `of_cells_to_cells`: `ofCells n ty (toCells c)` is `c`, up to the absent rows ---- *)

(* The absent rows hold `zero` — true of every column `ofCells` or decode builds. *)
let rec zeroed (#a: eqtype) (zero: a) (xs: list a) (v: validity) : Tot bool (decreases xs) =
  match xs with
  | [] -> true
  | x :: xt ->
      (match v with
       | true :: vt -> zeroed zero xt vt
       | false :: vt -> x = zero && zeroed zero xt vt
       | [] -> x = zero && zeroed zero xt [])

(* For one arm: refilling the cells a case's walk produced gives back the mask and the present
   elements, and the elements themselves where the absent rows held the zero. *)
[@@ noextract_to "FSharp"]
let rec fill_cells_of_inverts (#a: eqtype) (#num #flt: eqtype) (name: list ch) (ty: column_type)
                              (zero: a) (pick: cell num flt -> picked a) (mk: a -> cell num flt)
                              (xs: list a) (v: validity)
  : Lemma (requires same_len xs v /\
                    (forall (x: a). pick (mk x) == Fits x) /\
                    pick (Null #num #flt) == Absent)
          (ensures (match fill name ty zero pick (cells_of mk xs v) with
                    | Good (ys, m) ->
                        m == v /\ same_len ys xs /\ eq_at_present ys xs v /\
                        (zeroed zero xs v ==> ys == xs)
                    | Bad _ -> False))
          (decreases xs) =
  match xs, v with
  | x :: xt, _ :: vt -> fill_cells_of_inverts name ty zero pick mk xt vt
  | _ -> ()

(* THEOREM. For every well-formed typed column, `ofCells` of its `toCells` is a column EQUAL to it
   under `ColumnData.Equals` — one name, one case, one mask, equal at every present row. *)
#push-options "--ifuel 2"
[@@ noextract_to "FSharp"]
let of_cells_to_cells (#num #flt: eqtype) (h: host num flt) (c: typed_column num flt)
  : Lemma (requires wf c)
          (ensures (match of_cells h c.col_name (col_type c) (to_cells c) with
                    | Good d -> col_eq d c
                    | Bad _ -> False)) =
  match c.col_data with
  | Ints xs v -> fill_cells_of_inverts c.col_name IntType h.zero_int (pick_int #num #flt) (Int #num #flt) xs v
  | Floats xs v -> fill_cells_of_inverts c.col_name FloatType h.zero_float (pick_float h) (Float #num #flt) xs v
  | Bools xs v -> fill_cells_of_inverts c.col_name BoolType false (pick_bool #num #flt) (Bool #num #flt) xs v
  | Strs xs v -> fill_cells_of_inverts c.col_name StringType [] (pick_str #num #flt) (Str #num #flt) xs v
  | Dates xs v -> fill_cells_of_inverts c.col_name DateType [] (pick_date #num #flt) (Date #num #flt) xs v
  | Timestamps xs v -> fill_cells_of_inverts c.col_name TimestampType [] (pick_timestamp #num #flt) (Timestamp #num #flt) xs v
  | Decimals xs v -> fill_cells_of_inverts c.col_name DecimalType dec_zero (pick_decimal h) (Decimal #num #flt) xs v

(* The absent rows hold the type's zero — `absentSlot`'s value, which is what `ofCells` and decode
   write there. *)
let zeroed_col (#num #flt: eqtype) (h: host num flt) (c: typed_column num flt) : Tot bool =
  match c.col_data with
  | Ints xs v -> zeroed h.zero_int xs v
  | Floats xs v -> zeroed h.zero_float xs v
  | Bools xs v -> zeroed false xs v
  | Strs xs v -> zeroed [] xs v
  | Dates xs v -> zeroed [] xs v
  | Timestamps xs v -> zeroed [] xs v
  | Decimals xs v -> zeroed dec_zero xs v

(* THEOREM, the exact form: on a well-formed column whose absent rows hold the zero, `ofCells` of
   its `toCells` is the column itself. *)
[@@ noextract_to "FSharp"]
let of_cells_to_cells_exact (#num #flt: eqtype) (h: host num flt) (c: typed_column num flt)
  : Lemma (requires wf c /\ zeroed_col h c)
          (ensures of_cells h c.col_name (col_type c) (to_cells c) == Good c) =
  match c.col_data with
  | Ints xs v -> fill_cells_of_inverts c.col_name IntType h.zero_int (pick_int #num #flt) (Int #num #flt) xs v
  | Floats xs v -> fill_cells_of_inverts c.col_name FloatType h.zero_float (pick_float h) (Float #num #flt) xs v
  | Bools xs v -> fill_cells_of_inverts c.col_name BoolType false (pick_bool #num #flt) (Bool #num #flt) xs v
  | Strs xs v -> fill_cells_of_inverts c.col_name StringType [] (pick_str #num #flt) (Str #num #flt) xs v
  | Dates xs v -> fill_cells_of_inverts c.col_name DateType [] (pick_date #num #flt) (Date #num #flt) xs v
  | Timestamps xs v -> fill_cells_of_inverts c.col_name TimestampType [] (pick_timestamp #num #flt) (Timestamp #num #flt) xs v
  | Decimals xs v -> fill_cells_of_inverts c.col_name DecimalType dec_zero (pick_decimal h) (Decimal #num #flt) xs v
#pop-options

(* Every column `ofCells` builds is well-formed and zeroed — so the exact form covers it. *)
[@@ noextract_to "FSharp"]
let rec fill_wf_zeroed (#a: eqtype) (#num #flt: eqtype) (name: list ch) (ty: column_type) (zero: a)
                       (pick: cell num flt -> picked a) (cs: list (cell num flt))
  : Lemma (ensures (match fill name ty zero pick cs with
                    | Good (xs, m) -> same_len xs m /\ zeroed zero xs m
                    | Bad _ -> True))
          (decreases cs) =
  match cs with
  | [] -> ()
  | _ :: t -> fill_wf_zeroed name ty zero pick t

[@@ noextract_to "FSharp"]
let of_cells_wf_zeroed (#num #flt: eqtype) (h: host num flt) (name: list ch) (ty: column_type)
                       (cs: list (cell num flt))
  : Lemma (ensures (match of_cells h name ty cs with
                    | Good c -> wf c /\ zeroed_col h c
                    | Bad _ -> True)) =
  match ty with
  | IntType -> fill_wf_zeroed name ty h.zero_int (pick_int #num #flt) cs
  | FloatType -> fill_wf_zeroed name ty h.zero_float (pick_float h) cs
  | BoolType -> fill_wf_zeroed name ty false (pick_bool #num #flt) cs
  | StringType -> fill_wf_zeroed name ty [] (pick_str #num #flt) cs
  | DateType -> fill_wf_zeroed name ty [] (pick_date #num #flt) cs
  | TimestampType -> fill_wf_zeroed name ty [] (pick_timestamp #num #flt) cs
  | DecimalType -> fill_wf_zeroed name ty dec_zero (pick_decimal h) cs

(* WELL-FORMEDNESS IS NEEDED, exhibited: a one-element int column with an EMPTY mask reads its one
   row as absent, and `ofCells` of that hands back a mask of one bit — not the column's mask, so not
   its equal. *)
#push-options "--fuel 2 --ifuel 1"
[@@ noextract_to "FSharp"]
let of_cells_to_cells_needs_wf (#num #flt: eqtype) (h: host num flt) (n: list ch) (i: num)
  : Lemma (ensures (let c : typed_column num flt = { col_name = n; col_data = Ints [i] [] } in
                    not (wf c) /\
                    (match of_cells h c.col_name (col_type c) (to_cells c) with
                     | Good d -> col_mask d == [false] /\ not (col_eq d c)
                     | Bad _ -> False))) = ()
#pop-options

(* ---- 5d. THEOREM `validate_agrees`: the typed `validate` and the list model's agree ---- *)

(* names, lookup, types: the map keeps each. *)
[@@ noextract_to "FSharp"]
let rec column_names_agree (#num #flt: eqtype) (cs: list (typed_column num flt))
  : Lemma (ensures column_names (to_list_columns cs) == t_column_names cs) (decreases cs) =
  match cs with
  | [] -> ()
  | _ :: t -> column_names_agree t

[@@ noextract_to "FSharp"]
let rec find_column_agree (#num #flt: eqtype) (n: list ch) (cs: list (typed_column num flt))
  : Lemma (ensures find_column n (to_list_columns cs) ==
                   (match t_find_column n cs with
                    | Some c -> Some (to_list_column c)
                    | None -> None))
          (decreases cs) =
  match cs with
  | [] -> ()
  | c :: t -> if c.col_name = n then () else find_column_agree n t

[@@ noextract_to "FSharp"]
let rec type_fault_agree (#num #flt: eqtype) (s: list (list ch & column_type))
                         (cs: list (typed_column num flt))
  : Lemma (ensures type_fault s (to_list_columns cs) == t_type_fault s cs) (decreases s) =
  match s with
  | [] -> ()
  | (n, _) :: rest -> find_column_agree n cs; type_fault_agree rest cs

(* lengths: `ragged` compares cell lists, `t_ragged` the values' lengths; `toCells` has that length. *)
[@@ noextract_to "FSharp"]
let rec first_ragged_agree (#num #flt: eqtype) (cells0: list (cell num flt)) (len0: list unit)
                           (rest: list (typed_column num flt))
  : Lemma (requires List.Tot.length cells0 = List.Tot.length len0)
          (ensures first_ragged cells0 (to_list_columns rest) == t_first_ragged len0 rest)
          (decreases rest) =
  match rest with
  | [] -> ()
  | c :: t ->
      to_cells_length c;
      same_len_is_length cells0 (to_cells c);
      same_len_is_length len0 (data_len c.col_data);
      first_ragged_agree cells0 len0 t

[@@ noextract_to "FSharp"]
let ragged_agree (#num #flt: eqtype) (cs: list (typed_column num flt))
  : Lemma (ensures ragged (to_list_columns cs) == t_ragged cs) =
  match cs with
  | [] -> ()
  | first :: rest -> to_cells_length first; first_ragged_agree (to_cells first) (data_len first.col_data) rest

(* the scan: over one case's cells, `WireColumn`'s `first_uncarriable` is the typed scan ---- *)

[@@ noextract_to "FSharp"]
let scan_agrees (#a: Type) (#num #flt: eqtype) (h: host num flt) (name: list ch) (ty: column_type)
                (mk: a -> cell num flt) (bad: a -> bool) (e: column_error) (x: a) : prop =
  cell_fault h name ty (mk x) == (if bad x then Some e else None)

(* Off the mask's end the scan reads nothing; and a predicate that is never true finds nothing. *)
[@@ noextract_to "FSharp"]
let rec first_present_bad_nil (#a: Type) (bad: a -> bool) (xs: list a)
  : Lemma (ensures first_present_bad bad xs [] == false) (decreases xs) =
  match xs with
  | [] -> ()
  | _ :: xt -> first_present_bad_nil bad xt

(* the predicate an int, bool or string column's scan asks: nothing is ever bad *)
let never (#a: Type) (_: a) : Tot bool = false

[@@ noextract_to "FSharp"]
let rec first_present_bad_never (#a: Type) (xs: list a) (v: validity)
  : Lemma (ensures first_present_bad never xs v == false) (decreases xs) =
  match xs with
  | [] -> ()
  | _ :: xt ->
      (match v with
       | _ :: vt -> first_present_bad_never xt vt
       | [] -> first_present_bad_never xt [])

[@@ noextract_to "FSharp"]
let rec first_uncarriable_scan (#a: Type) (#num #flt: eqtype) (h: host num flt) (name: list ch)
                               (ty: column_type) (mk: a -> cell num flt) (bad: a -> bool)
                               (e: column_error) (xs: list a) (v: validity)
  : Lemma (requires (forall (x: a). scan_agrees h name ty mk bad e x))
          (ensures first_uncarriable h name ty (cells_of mk xs v) ==
                   (if first_present_bad bad xs v then Some e else None))
          (decreases xs) =
  match xs with
  | [] -> ()
  | x :: xt ->
      (match v with
       | true :: vt -> first_uncarriable_scan h name ty mk bad e xt vt
       | false :: vt -> first_uncarriable_scan h name ty mk bad e xt vt
       | [] -> first_uncarriable_scan h name ty mk bad e xt []; first_present_bad_nil bad xt)

#push-options "--ifuel 2"
[@@ noextract_to "FSharp"]
let int_scan (#num #flt: eqtype) (h: host num flt) (name: list ch) (x: num)
  : Lemma (ensures scan_agrees h name IntType Int (never #num) MalformedShape x) = ()

[@@ noextract_to "FSharp"]
let bool_scan (#num #flt: eqtype) (h: host num flt) (name: list ch) (x: bool)
  : Lemma (ensures scan_agrees h name BoolType (Bool #num #flt) (never #bool) MalformedShape x) = ()

[@@ noextract_to "FSharp"]
let str_scan (#num #flt: eqtype) (h: host num flt) (name: list ch) (x: list ch)
  : Lemma (ensures scan_agrees h name StringType (Str #num #flt) (never #(list ch)) MalformedShape x) = ()

[@@ noextract_to "FSharp"]
let float_scan (#num #flt: eqtype) (h: host num flt) (name: list ch) (x: flt)
  : Lemma (ensures scan_agrees h name FloatType Float (not_finite h) (NonFiniteFloat name) x) = ()

[@@ noextract_to "FSharp"]
let decimal_scan (#num #flt: eqtype) (h: host num flt) (name: list ch) (x: list ch)
  : Lemma (ensures scan_agrees h name DecimalType (Decimal #num #flt) not_canonical MalformedShape x) = ()

[@@ noextract_to "FSharp"]
let date_scan (#num #flt: eqtype) (h: host num flt) (name: list ch) (x: list ch)
  : Lemma (ensures scan_agrees h name DateType (Date #num #flt) (not_date h) MalformedShape x) = ()

[@@ noextract_to "FSharp"]
let timestamp_scan (#num #flt: eqtype) (h: host num flt) (name: list ch) (x: list ch)
  : Lemma (ensures scan_agrees h name TimestampType (Timestamp #num #flt) (not_timestamp h) MalformedShape x) = ()
#pop-options

(* On a well-formed column the typed scan is the list model's clause (e) — the one arm the list
   model has and the typed scan does not, a cell outside the column's type, is never reached:
   `toCells` writes a cell of the column's own type at every present row. *)
[@@ noextract_to "FSharp"]
let first_uncarriable_agree (#num #flt: eqtype) (h: host num flt) (c: typed_column num flt)
  : Lemma (requires wf c)
          (ensures first_uncarriable h c.col_name (col_type c) (to_cells c) == first_uncarriable_t h c) =
  let n = c.col_name in
  match c.col_data with
  | Ints xs v ->
      FStar.Classical.forall_intro (int_scan h n);
      first_uncarriable_scan h n IntType Int (never #num) MalformedShape xs v;
      first_present_bad_never xs v
  | Bools xs v ->
      FStar.Classical.forall_intro (bool_scan #num #flt h n);
      first_uncarriable_scan h n BoolType Bool (never #bool) MalformedShape xs v;
      first_present_bad_never xs v
  | Strs xs v ->
      FStar.Classical.forall_intro (str_scan #num #flt h n);
      first_uncarriable_scan h n StringType Str (never #(list ch)) MalformedShape xs v;
      first_present_bad_never xs v
  | Floats xs v ->
      FStar.Classical.forall_intro (float_scan h n);
      first_uncarriable_scan h n FloatType Float (not_finite h) (NonFiniteFloat n) xs v
  | Decimals xs v ->
      FStar.Classical.forall_intro (decimal_scan #num #flt h n);
      first_uncarriable_scan h n DecimalType Decimal not_canonical MalformedShape xs v
  | Dates xs v ->
      FStar.Classical.forall_intro (date_scan #num #flt h n);
      first_uncarriable_scan h n DateType Date (not_date h) MalformedShape xs v
  | Timestamps xs v ->
      FStar.Classical.forall_intro (timestamp_scan #num #flt h n);
      first_uncarriable_scan h n TimestampType Timestamp (not_timestamp h) MalformedShape xs v

[@@ noextract_to "FSharp"]
let rec find_column_wf (#num #flt: eqtype) (n: list ch) (cs: list (typed_column num flt))
  : Lemma (requires all_wf cs)
          (ensures (match t_find_column n cs with Some c -> wf c | None -> True))
          (decreases cs) =
  match cs with
  | [] -> ()
  | c :: t -> if c.col_name = n then () else find_column_wf n t

[@@ noextract_to "FSharp"]
let rec cells_fault_agree (#num #flt: eqtype) (h: host num flt) (s: list (list ch & column_type))
                          (cs: list (typed_column num flt))
  : Lemma (requires all_wf cs)
          (ensures cells_fault h s (to_list_columns cs) == t_cells_fault h s cs)
          (decreases s) =
  match s with
  | [] -> ()
  | (n, _) :: rest ->
      find_column_agree n cs;
      find_column_wf n cs;
      (match t_find_column n cs with
       | Some c -> first_uncarriable_agree h c
       | None -> ());
      cells_fault_agree h rest cs

(* THEOREM. On every typed table whose columns are well-formed, the typed `validate` answers what
   the list model's `validate` answers of the table's image. *)
[@@ noextract_to "FSharp"]
let validate_agrees (#num #flt: eqtype) (h: host num flt) (t: typed_table num flt)
  : Lemma (requires all_wf t.tcolumns)
          (ensures validate_t h t == validate h (to_list_table t)) =
  column_names_agree t.tcolumns;
  type_fault_agree t.tschema t.tcolumns;
  ragged_agree t.tcolumns;
  cells_fault_agree h t.tschema t.tcolumns

(* ---- and a table the typed `validate` accepts IS well-formed: clause (e) said so, column by column ---- *)

[@@ noextract_to "FSharp"]
let rec mem_name_found (#num #flt: eqtype) (n: list ch) (cs: list (typed_column num flt))
  : Lemma (requires mem n (t_column_names cs))
          (ensures Some? (t_find_column n cs))
          (decreases cs) =
  match cs with
  | [] -> ()
  | c :: t -> if c.col_name = n then () else mem_name_found n t

[@@ noextract_to "FSharp"]
let rec not_in_nil_mem (names against: list (list ch))
  : Lemma (requires Nil? (not_in names against))
          (ensures (forall (x: list ch). mem x names ==> mem x against))
          (decreases names) =
  match names with
  | [] -> ()
  | n :: rest -> not_in_nil_mem rest against

(* A column found by a schema name passed clause (e), so it is well-formed. *)
[@@ noextract_to "FSharp"]
let rec cells_fault_none_wf (#num #flt: eqtype) (h: host num flt) (s: list (list ch & column_type))
                            (cs: list (typed_column num flt)) (n: list ch)
  : Lemma (requires None? (t_cells_fault h s cs) /\ mem n (schema_names s))
          (ensures (match t_find_column n cs with Some c -> wf c | None -> True))
          (decreases s) =
  match s with
  | [] -> ()
  | (m, _) :: rest ->
      if m = n then ()
      else cells_fault_none_wf h rest cs n

(* A column of the list has its name among the list's names. *)
[@@ noextract_to "FSharp"]
let rec mem_in_names (#num #flt: eqtype) (cs: list (typed_column num flt)) (c: typed_column num flt)
  : Lemma (requires List.Tot.memP c cs)
          (ensures mem c.col_name (t_column_names cs))
          (decreases cs) =
  match cs with
  | [] -> ()
  | d :: t -> if d = c then () else mem_in_names t c

(* Every column of the list is found by its own name when the names are distinct — the first
   column of that name is this one. *)
[@@ noextract_to "FSharp"]
let rec own_name_finds (#num #flt: eqtype) (cs: list (typed_column num flt)) (c: typed_column num flt)
  : Lemma (requires distinct (t_column_names cs) /\ List.Tot.memP c cs)
          (ensures t_find_column c.col_name cs == Some c)
          (decreases cs) =
  match cs with
  | [] -> ()
  | d :: t ->
      if d = c then ()
      else (mem_in_names t c; own_name_finds t c)

[@@ noextract_to "FSharp"]
let rec all_wf_of_each (#num #flt: eqtype) (cs: list (typed_column num flt))
  : Lemma (requires (forall (c: typed_column num flt). List.Tot.memP c cs ==> wf c))
          (ensures all_wf cs)
          (decreases cs) =
  match cs with
  | [] -> ()
  | _ :: t -> all_wf_of_each t

(* `validate_t` answering `Good` passed every clause. *)
[@@ noextract_to "FSharp"]
let validate_t_good (#num #flt: eqtype) (h: host num flt) (t: typed_table num flt)
  : Lemma (requires validate_t h t == Good ())
          (ensures None? (first_duplicate (schema_names t.tschema)) /\
                   None? (first_duplicate (t_column_names t.tcolumns)) /\
                   Nil? (not_in (schema_names t.tschema) (t_column_names t.tcolumns)) /\
                   Nil? (not_in (t_column_names t.tcolumns) (schema_names t.tschema)) /\
                   None? (t_type_fault t.tschema t.tcolumns) /\
                   None? (t_ragged t.tcolumns) /\
                   None? (t_cells_fault h t.tschema t.tcolumns)) = ()

(* THEOREM. A typed table the typed `validate` accepts has well-formed columns: so the agreement
   above covers every table either validator accepts, and the map is a bijection there. *)
[@@ noextract_to "FSharp"]
let validate_t_good_wf (#num #flt: eqtype) (h: host num flt) (t: typed_table num flt)
  : Lemma (requires validate_t h t == Good ())
          (ensures all_wf t.tcolumns) =
  let cs = t.tcolumns in
  let s = t.tschema in
  validate_t_good h t;
  first_dup_none [] (t_column_names cs);
  not_in_nil_mem (t_column_names cs) (schema_names s);
  let each (c: typed_column num flt)
    : Lemma (requires List.Tot.memP c cs) (ensures wf c) =
    mem_in_names cs c;
    own_name_finds cs c;
    cells_fault_none_wf h s cs c.col_name in
  FStar.Classical.forall_intro (FStar.Classical.move_requires each);
  all_wf_of_each cs

(* ---- 5e. THEOREM `encode_agrees`: the typed encoder writes what the cell encoder wrote ---- *)

[@@ noextract_to "FSharp"]
let wrap_agrees (#a: Type) (#num #flt: eqtype) (h: host num flt) (ty: column_type)
                (mk: a -> cell num flt) (wrap: a -> jval num flt) (x: a) : prop =
  cell_json h ty (mk x) == wrap x /\ not (Null? (mk x))

[@@ noextract_to "FSharp"]
let rec values_json_agree (#a: Type) (#num #flt: eqtype) (h: host num flt) (ty: column_type)
                          (mk: a -> cell num flt) (wrap: a -> jval num flt) (xs: list a) (v: validity)
  : Lemma (requires (forall (x: a). wrap_agrees h ty mk wrap x))
          (ensures values_json h ty (cells_of mk xs v) == values_json_t h ty wrap xs v)
          (decreases xs) =
  match xs with
  | [] -> ()
  | _ :: xt ->
      (match v with
       | _ :: vt -> values_json_agree h ty mk wrap xt vt
       | [] -> values_json_agree h ty mk wrap xt [])

[@@ noextract_to "FSharp"]
let rec validity_json_agree (#a: Type) (#num #flt: eqtype) (mk: a -> cell num flt) (xs: list a)
                            (v: validity)
  : Lemma (requires (forall (x: a). not (Null? (mk x))))
          (ensures validity_json #num #flt (cells_of mk xs v) == validity_json_t (units xs) v)
          (decreases xs) =
  match xs with
  | [] -> ()
  | _ :: xt ->
      (match v with
       | _ :: vt -> validity_json_agree mk xt vt
       | [] -> validity_json_agree mk xt [])

#push-options "--ifuel 2"
[@@ noextract_to "FSharp"]
let int_wrap (#num #flt: eqtype) (h: host num flt) (x: num)
  : Lemma (ensures wrap_agrees h IntType Int (JInt #num #flt) x) = ()
[@@ noextract_to "FSharp"]
let float_wrap (#num #flt: eqtype) (h: host num flt) (x: flt)
  : Lemma (ensures wrap_agrees h FloatType Float (JFloat #num #flt) x) = ()
[@@ noextract_to "FSharp"]
let bool_wrap (#num #flt: eqtype) (h: host num flt) (x: bool)
  : Lemma (ensures wrap_agrees h BoolType (Bool #num #flt) (JBool #num #flt) x) = ()
[@@ noextract_to "FSharp"]
let str_wrap (#num #flt: eqtype) (h: host num flt) (x: list ch)
  : Lemma (ensures wrap_agrees h StringType (Str #num #flt) (JStr #num #flt) x) = ()
[@@ noextract_to "FSharp"]
let date_wrap (#num #flt: eqtype) (h: host num flt) (x: list ch)
  : Lemma (ensures wrap_agrees h DateType (Date #num #flt) (JStr #num #flt) x) = ()
[@@ noextract_to "FSharp"]
let timestamp_wrap (#num #flt: eqtype) (h: host num flt) (x: list ch)
  : Lemma (ensures wrap_agrees h TimestampType (Timestamp #num #flt) (JStr #num #flt) x) = ()
[@@ noextract_to "FSharp"]
let decimal_wrap (#num #flt: eqtype) (h: host num flt) (x: list ch)
  : Lemma (ensures wrap_agrees h DecimalType (Decimal #num #flt) (JStr #num #flt) x) = ()
#pop-options

(* The column: one case at a time. Well-formedness is NOT needed — both walks run the values'
   length and read the mask the same way, off its end included. *)
[@@ noextract_to "FSharp"]
let column_json_agree (#num #flt: eqtype) (h: host num flt) (c: typed_column num flt)
  : Lemma (ensures column_json h (to_list_column c) == column_json_t h c) =
  match c.col_data with
  | Ints xs v ->
      FStar.Classical.forall_intro (int_wrap h);
      values_json_agree h IntType Int JInt xs v;
      validity_json_agree #num #num #flt Int xs v
  | Floats xs v ->
      FStar.Classical.forall_intro (float_wrap h);
      values_json_agree h FloatType Float JFloat xs v;
      validity_json_agree #flt #num #flt Float xs v
  | Bools xs v ->
      FStar.Classical.forall_intro (bool_wrap #num #flt h);
      values_json_agree h BoolType Bool JBool xs v;
      validity_json_agree #bool #num #flt Bool xs v
  | Strs xs v ->
      FStar.Classical.forall_intro (str_wrap #num #flt h);
      values_json_agree h StringType Str JStr xs v;
      validity_json_agree #(list ch) #num #flt Str xs v
  | Dates xs v ->
      FStar.Classical.forall_intro (date_wrap #num #flt h);
      values_json_agree h DateType Date JStr xs v;
      validity_json_agree #(list ch) #num #flt Date xs v
  | Timestamps xs v ->
      FStar.Classical.forall_intro (timestamp_wrap #num #flt h);
      values_json_agree h TimestampType Timestamp JStr xs v;
      validity_json_agree #(list ch) #num #flt Timestamp xs v
  | Decimals xs v ->
      FStar.Classical.forall_intro (decimal_wrap #num #flt h);
      values_json_agree h DecimalType Decimal JStr xs v;
      validity_json_agree #(list ch) #num #flt Decimal xs v

[@@ noextract_to "FSharp"]
let placeholder_agree (#num #flt: eqtype) (n: list ch) (cs: list (typed_column num flt))
  : Lemma (ensures column_or_placeholder n (to_list_columns cs) ==
                   to_list_column (t_column_or_placeholder n cs)) =
  find_column_agree n cs

[@@ noextract_to "FSharp"]
let rec columns_json_agree (#num #flt: eqtype) (h: host num flt) (s: list (list ch & column_type))
                           (cs: list (typed_column num flt))
  : Lemma (ensures columns_json h s (to_list_columns cs) == columns_json_t h s cs) (decreases s) =
  match s with
  | [] -> ()
  | (n, _) :: rest ->
      placeholder_agree n cs;
      column_json_agree h (t_column_or_placeholder n cs);
      columns_json_agree h rest cs

(* THEOREM. `encodeJson` agrees through the map on EVERY typed source — valid or not, well-formed
   or not. *)
[@@ noextract_to "FSharp"]
let encode_agrees (#num #flt: eqtype) (h: host num flt) (src: typed_source num flt)
  : Lemma (ensures encode_json_t h src == encode_json h (to_list_source src)) =
  match src with
  | TEmbedded t -> columns_json_agree h t.tschema t.tcolumns
  | TRef _ -> ()

(* And the guarded entry point, on the tables the two validators agree about. *)
[@@ noextract_to "FSharp"]
let try_encode_agrees (#num #flt: eqtype) (h: host num flt) (src: typed_source num flt)
  : Lemma (requires (match src with TEmbedded t -> all_wf t.tcolumns | TRef _ -> True))
          (ensures try_encode_json_t h src == try_encode_json h (to_list_source src)) =
  encode_agrees h src;
  (match src with
   | TEmbedded t -> validate_agrees h t
   | TRef _ -> ())

(* ======================================================================================
   6. WHAT FOLLOWS FOR THE TYPED COLUMN: the widening half of the normal form is vacuous, the
      normal form of a typed table is a reordering, and `WireColumn`'s two theorems restated.
   ====================================================================================== *)

(* An `Int` cell comes only from an `Ints` column, whose type is `IntType`, where `norm_cell` is
   the identity; every other case's cells `norm_cell` never touches. *)
[@@ noextract_to "FSharp"]
let rec norm_cells_fixed (#a: Type) (#num #flt: eqtype) (h: host num flt) (ty: column_type)
                         (mk: a -> cell num flt) (xs: list a) (v: validity)
  : Lemma (requires (forall (x: a). norm_cell h ty (mk x) == mk x))
          (ensures norm_cells h ty (cells_of mk xs v) == cells_of mk xs v)
          (decreases xs) =
  match xs with
  | [] -> ()
  | _ :: xt ->
      (match v with
       | _ :: vt -> norm_cells_fixed h ty mk xt vt
       | [] -> norm_cells_fixed h ty mk xt [])

(* THEOREM. On a typed column the widening normalisation is the identity: there is no `Int` in a
   float or decimal column to widen. *)
[@@ noextract_to "FSharp"]
let norm_cells_vacuous (#num #flt: eqtype) (h: host num flt) (c: typed_column num flt)
  : Lemma (ensures norm_cells h (col_type c) (to_cells c) == to_cells c) =
  match c.col_data with
  | Ints xs v -> norm_cells_fixed h IntType Int xs v
  | Floats xs v -> norm_cells_fixed h FloatType Float xs v
  | Bools xs v -> norm_cells_fixed h BoolType Bool xs v
  | Strs xs v -> norm_cells_fixed h StringType Str xs v
  | Dates xs v -> norm_cells_fixed h DateType Date xs v
  | Timestamps xs v -> norm_cells_fixed h TimestampType Timestamp xs v
  | Decimals xs v -> norm_cells_fixed h DecimalType Decimal xs v

(* The normal form of a typed table: one column per schema entry, the table's own column of that
   name (the placeholder where there is none, which `validate` never lets through). Nothing is
   widened, because nothing can be. *)
let rec normal_columns_t (#num #flt: eqtype) (s: list (list ch & column_type))
                         (cs: list (typed_column num flt))
  : Tot (list (typed_column num flt)) (decreases s) =
  match s with
  | [] -> []
  | (n, _) :: t -> t_column_or_placeholder n cs :: normal_columns_t t cs

let normal_table_t (#num #flt: eqtype) (t: typed_table num flt) : Tot (typed_table num flt) =
  { tschema = t.tschema; tcolumns = normal_columns_t t.tschema t.tcolumns }

(* Each column of the typed normal form is a column of the table (or the placeholder). *)
[@@ noextract_to "FSharp"]
let rec find_column_mem (#num #flt: eqtype) (n: list ch) (cs: list (typed_column num flt))
  : Lemma (ensures (match t_find_column n cs with
                    | Some c -> List.Tot.memP c cs /\ c.col_name == n
                    | None -> True))
          (decreases cs) =
  match cs with
  | [] -> ()
  | c :: t -> if c.col_name = n then () else find_column_mem n t

[@@ noextract_to "FSharp"]
let rec normal_columns_are_the_columns (#num #flt: eqtype) (s: list (list ch & column_type))
                                       (cs: list (typed_column num flt))
  : Lemma (ensures (forall (c: typed_column num flt).
                      List.Tot.memP c (normal_columns_t s cs) ==>
                      (List.Tot.memP c cs \/ c.col_data == Strs [] [])))
          (decreases s) =
  match s with
  | [] -> ()
  | (n, _) :: t -> find_column_mem n cs; normal_columns_are_the_columns t cs

(* Under the list model's normal form, the typed normal form's image: where every schema name has
   its column (clause (b) passed) and the schema's type is the column's own (clause (c) passed),
   `normal_column` widens nothing and renames nothing. *)
[@@ noextract_to "FSharp"]
let rec normal_columns_agree (#num #flt: eqtype) (h: host num flt) (s: list (list ch & column_type))
                             (cs: list (typed_column num flt))
  : Lemma (requires None? (t_type_fault s cs) /\
                    (forall (n: list ch). mem n (schema_names s) ==> Some? (t_find_column n cs)))
          (ensures normal_columns h s (to_list_columns cs) == to_list_columns (normal_columns_t s cs))
          (decreases s) =
  match s with
  | [] -> ()
  | (n, ty) :: rest ->
      placeholder_agree n cs;
      find_column_mem n cs;
      (match t_find_column n cs with
       | Some c -> norm_cells_vacuous h c
       | None -> ());
      normal_columns_agree h rest cs

(* Clause (b), read as a lookup: every schema name finds a column. *)
[@@ noextract_to "FSharp"]
let names_found (#num #flt: eqtype) (s: list (list ch & column_type)) (cs: list (typed_column num flt))
  : Lemma (requires Nil? (not_in (schema_names s) (t_column_names cs)))
          (ensures (forall (n: list ch). mem n (schema_names s) ==> Some? (t_find_column n cs))) =
  not_in_nil_mem (schema_names s) (t_column_names cs);
  let found (n: list ch)
    : Lemma (requires mem n (t_column_names cs)) (ensures Some? (t_find_column n cs)) =
    mem_name_found n cs in
  FStar.Classical.forall_intro (FStar.Classical.move_requires found)

[@@ noextract_to "FSharp"]
let normal_table_agree (#num #flt: eqtype) (h: host num flt) (t: typed_table num flt)
  : Lemma (requires None? (t_type_fault t.tschema t.tcolumns) /\
                    Nil? (not_in (schema_names t.tschema) (t_column_names t.tcolumns)))
          (ensures normal_table h (to_list_table t) == to_list_table (normal_table_t t)) =
  names_found t.tschema t.tcolumns;
  normal_columns_agree h t.tschema t.tcolumns

(* ---- THEOREM 1 OF `WireColumn`, OF THE TYPED COLUMN: decode's image re-validates typed ---- *)

(* A well-formed typed table whose image is what decode answered is one the typed `validate`
   accepts: `decode_image_is_valid` through `validate_agrees`. *)
[@@ noextract_to "FSharp"]
let typed_decode_image_is_valid (#num #flt: eqtype) (h: host num flt) (el: jval num flt)
                                (t: typed_table num flt)
  : Lemma (requires all_wf t.tcolumns /\ decode_json h el == Good (Embedded (to_list_table t)))
          (ensures validate_t h t == Good ()) =
  decode_image_is_valid h el (to_list_table t);
  validate_agrees h t

(* ---- THEOREM 2 OF `WireColumn`, OF THE TYPED COLUMN: the round trip ---- *)

(* For every typed table the typed `validate` accepts, decoding the typed encoder's output answers
   the typed table's normal form — its own columns, in schema order, nothing widened — read through
   the map. `round_trip` through `encode_agrees`, `validate_agrees` and `normal_table_agree`. *)
[@@ noextract_to "FSharp"]
let typed_round_trip (#num #flt: eqtype) (h: host num flt) (t: typed_table num flt)
  : Lemma (requires host_ok h /\ validate_t h t == Good ())
          (ensures decode_json h (encode_json_t h (TEmbedded t))
                   == Good (Embedded (to_list_table (normal_table_t t)))) =
  validate_t_good_wf h t;
  validate_agrees h t;
  encode_agrees h (TEmbedded t);
  validate_t_good h t;
  normal_table_agree h t;
  round_trip h (to_list_table t)

(* ---- and the LITERAL round trip, in schema order ---- *)

(* A column whose name no schema entry of `st` carries is skipped by every lookup over `st`. *)
[@@ noextract_to "FSharp"]
let rec skip_all (#num #flt: eqtype) (st: list (list ch & column_type)) (c: typed_column num flt)
                 (ct: list (typed_column num flt))
  : Lemma (requires not (mem c.col_name (schema_names st)))
          (ensures normal_columns_t st (c :: ct) == normal_columns_t st ct)
          (decreases st) =
  match st with
  | [] -> ()
  | (m, _) :: rest -> skip_all rest c ct

(* With distinct names, a column list in schema order IS its normal form: each schema name finds
   the column at its own position. *)
[@@ noextract_to "FSharp"]
let rec in_order_is_normal (#num #flt: eqtype) (s: list (list ch & column_type))
                           (cs: list (typed_column num flt))
  : Lemma (requires t_column_names cs == schema_names s /\ distinct (schema_names s))
          (ensures normal_columns_t s cs == cs)
          (decreases s) =
  match s, cs with
  | (n, _) :: st, c :: ct ->
      in_order_is_normal st ct;
      skip_all st c ct
  | _ -> ()

(* THEOREM. A typed table in schema order round-trips LITERALLY: `decode (encode t)` is `t` itself,
   read through the map. Of the three ways the literal round trip failed for the list column, the
   two widenings cannot arise in a typed column (`norm_cells_vacuous`) and the third — column order
   — is the one that remains; this is its complement. *)
[@@ noextract_to "FSharp"]
let typed_round_trip_in_schema_order (#num #flt: eqtype) (h: host num flt) (t: typed_table num flt)
  : Lemma (requires host_ok h /\ validate_t h t == Good () /\
                    t_column_names t.tcolumns == schema_names t.tschema)
          (ensures decode_json h (encode_json_t h (TEmbedded t)) == Good (Embedded (to_list_table t))) =
  typed_round_trip h t;
  validate_t_good h t;
  first_dup_none [] (schema_names t.tschema);
  in_order_is_normal t.tschema t.tcolumns

(* ======================================================================================
   7. THE TWO-CASE VALIDITY (Phase 420): a null-free column carries no mask. F#'s `Validity` is
      `AllValid | Mask of Vector<bool>`, and sections 1 to 6 read it MATERIALISED — `validity`
      above is the mask `Validity.toMask` answers (`Column.mask`), which is what `cell`, `toCells`,
      the typed `validate` and the encoder's walks see. This section models the two cases and that
      materialisation, and proves the three facts the materialised reading rests on:
        - `all_valid_reads_all_true`: `AllValid` reads as the all-true mask of the column's length —
          every row present, none past the end — so an `AllValid` column is always well-formed
          (`all_valid_wf`) and `data_wf` is a claim about `Mask` columns alone;
        - `of_mask_to_mask`: the normalising constructors (`Validity.ofArray` / `ofList` /
          `ofVector`, and through them `ofCells` and decode) lose nothing — materialising what they
          built at the mask's own length gives the mask back — and never build an all-true `Mask`
          (`of_mask_normal`);
        - `same_mask_is_mask_equality`: `ColumnStorage.sameMask`, which compares two validities
          WITHOUT materialising either, is equality of the materialised masks — so `ColumnData`'s
          equality is the `present_equal` of section 2 over materialised masks, an `AllValid` column
          and a hand-built all-true `Mask` of its length are one value, and nothing else is.
   ====================================================================================== *)

(* F# (Phase 420): `Validity = AllValid | Mask of present: Vector<bool>`, a `Mask` read through
   `Vector.toArray`. *)
type validity_rep =
  | AllValid : validity_rep
  | Mask     : present:validity -> validity_rep

(* F#: the vector `Vector.init n (fun _ -> true)` builds — `n` rows, every bit set, `n` written as a
   list of that length (the header's "counts"). *)
let rec all_true (n: list unit) : Tot validity (decreases n) =
  match n with
  | [] -> []
  | _ :: t -> true :: all_true t

(* F#: `Validity.toMask n v` — a `Mask`'s own vector, and for `AllValid` the all-true mask of `n`. *)
let to_mask (n: list unit) (v: validity_rep) : Tot validity =
  match v with
  | AllValid -> all_true n
  | Mask m -> m

(* F#: `Array.forall id` / `not (Vector.exists not m)` — no bit clear. *)
let rec all_set (m: validity) : Tot bool (decreases m) =
  match m with
  | [] -> true
  | b :: t -> b && all_set t

(* F#: `Validity.ofVector` (and `ofArray` / `ofList` over their copy) — `AllValid` when no bit is
   clear, else a `Mask` over the bits. *)
let of_mask (m: validity) : Tot validity_rep =
  if all_set m then AllValid else Mask m

(* F#: `ColumnStorage.sameMask n vx vy` — the two arms that mix the cases ask whether the `Mask` is
   all set AND of the column's length. *)
let same_mask (n: list unit) (a b: validity_rep) : Tot bool =
  match a, b with
  | AllValid, AllValid -> true
  | AllValid, Mask m -> same_len m n && all_set m
  | Mask m, AllValid -> same_len m n && all_set m
  | Mask x, Mask y -> x = y

[@@ noextract_to "FSharp"]
let rec all_true_len (n: list unit)
  : Lemma (ensures same_len (all_true n) n) (decreases n) =
  match n with
  | [] -> ()
  | _ :: t -> all_true_len t

[@@ noextract_to "FSharp"]
let rec all_true_present (n: list unit) (i: nat)
  : Lemma (ensures is_present i (all_true n) == (i < List.Tot.length n)) (decreases n) =
  match n with
  | [] -> ()
  | _ :: t -> if i = 0 then () else all_true_present t (i - 1)

[@@ noextract_to "FSharp"]
let rec same_len_trans_units (#a: Type) (xs: list a) (m: validity)
  : Lemma (requires same_len m (units xs)) (ensures same_len xs m) (decreases xs) =
  match xs, m with
  | _ :: xt, _ :: mt -> same_len_trans_units xt mt
  | _ -> ()

(* THEOREM — `AllValid` reads as the all-true mask of the column's length: one bit per row, and
   row `i` present exactly when it is a row. *)
[@@ noextract_to "FSharp"]
let all_valid_reads_all_true (n: list unit) (i: nat)
  : Lemma (ensures to_mask n AllValid == all_true n /\
                   same_len (to_mask n AllValid) n /\
                   is_present i (to_mask n AllValid) == (i < List.Tot.length n)) =
  all_true_len n;
  all_true_present n i

(* So an `AllValid` column is well-formed whatever its values: `data_wf` of any case over the
   materialised mask holds. *)
[@@ noextract_to "FSharp"]
let all_valid_wf (#a: Type) (xs: list a)
  : Lemma (ensures same_len xs (to_mask (units xs) AllValid)) =
  all_true_len (units xs);
  same_len_trans_units xs (all_true (units xs))

[@@ noextract_to "FSharp"]
let rec all_set_is_all_true (m: validity)
  : Lemma (requires all_set m) (ensures all_true (units m) == m) (decreases m) =
  match m with
  | [] -> ()
  | _ :: t -> all_set_is_all_true t

(* THEOREM — the normalising constructors lose nothing: materialised at the mask's own length, what
   `of_mask` built IS the mask. *)
[@@ noextract_to "FSharp"]
let of_mask_to_mask (m: validity)
  : Lemma (ensures to_mask (units m) (of_mask m) == m) =
  if all_set m then all_set_is_all_true m else ()

(* And they never build an all-true `Mask`: a `Mask` they answer has a clear bit. *)
[@@ noextract_to "FSharp"]
let of_mask_normal (m: validity)
  : Lemma (ensures (match of_mask m with
                    | AllValid -> all_set m
                    | Mask m' -> m' == m /\ not (all_set m'))) =
  ()

[@@ noextract_to "FSharp"]
let rec all_true_eq (n: list unit) (m: validity)
  : Lemma (ensures (all_true n = m) == (same_len m n && all_set m)) (decreases n) =
  match n, m with
  | [], [] -> ()
  | [], _ :: _ -> ()
  | _ :: _, [] -> ()
  | _ :: nt, b :: mt -> all_true_eq nt mt

(* THEOREM — `sameMask` is equality of the materialised masks, on every pair of validities and at
   every length: so `AllValid` equals a `Mask` exactly when that mask is all set and of the
   column's length, and two `Mask`s exactly when they are one mask. *)
[@@ noextract_to "FSharp"]
let same_mask_is_mask_equality (n: list unit) (a b: validity_rep)
  : Lemma (ensures same_mask n a b == (to_mask n a = to_mask n b)) =
  match a, b with
  | AllValid, AllValid -> ()
  | AllValid, Mask m -> all_true_eq n m
  | Mask m, AllValid -> all_true_eq n m
  | Mask _, Mask _ -> ()

(* ======================================================================================
   8. TWINS (Phase 309) — the extractor premise, sampled at this model. See `WireColumn.fst`'s
      section of the same name for what the list is and why the kit refuses a model without one.
      The host here is the simplest the record admits — both number carriers `nat`, the text of a
      number empty, every float finite, every text a date — because what is sampled is the walks,
      not the host.
   ====================================================================================== *)

let twin_host : host nat nat = {
  to_float = (fun i -> i);
  int_text = (fun _ -> []);
  finite = (fun _ -> true);
  zero_int = 0;
  zero_float = 0;
  is_date = (fun _ -> true);
  is_timestamp = (fun _ -> true);
}

noeq type twin = { tname : string; tholds : unit -> bool }

let rec twins_hold (l:list twin) : Tot bool =
  match l with
  | [] -> true
  | t :: r -> t.tholds () && twins_hold r
let twins : list twin = [
  { tname = "to-cells-reads-the-mask";
    tholds = (fun () ->
      to_cells ({ col_name = []; col_data = Ints #nat #nat [1; 2; 3] [true; false] })
      = [Int 1; Null; Null]) };
  { tname = "of-cells-widens-an-int-into-a-float-column";
    tholds = (fun () ->
      of_cells twin_host [] FloatType [Int 3; Null; Float 4]
      = Good ({ col_name = []; col_data = Floats #nat #nat [3; 0; 4] [true; false; true] })) };
  { tname = "of-cells-refuses-the-first-cell-outside";
    tholds = (fun () ->
      of_cells twin_host [] BoolType [Null; Int 1; Bool true] = Bad (TypeMismatch [] BoolType)) };
  { tname = "first-uncarriable-names-the-mask-first";
    tholds = (fun () ->
      first_uncarriable_t twin_host ({ col_name = []; col_data = Ints #nat #nat [1] [] })
      = Some (LengthMismatch [])) };
  { tname = "column-json-writes-the-absent-slot-not-the-element";
    tholds = (fun () ->
      column_json_t twin_host ({ col_name = []; col_data = Ints #nat #nat [7; 9] [false; true] })
      = JObj [ (values_key, JArr [JInt #nat #nat 0; JInt #nat #nat 9]); (validity_key, JArr [JBool false; JBool true]) ]) };
  { tname = "of-mask-normalises-an-all-set-mask";
    tholds = (fun () ->
      of_mask [true; true] = AllValid && of_mask [true; false] = Mask [true; false]
      && of_mask [] = AllValid) };
  { tname = "same-mask-reads-all-valid-as-the-all-true-mask-of-the-length";
    tholds = (fun () ->
      same_mask [(); ()] AllValid (Mask [true; true])
      && not (same_mask [(); ()] AllValid (Mask [true; true; true]))
      && not (same_mask [(); ()] (Mask [true; false]) AllValid)
      && to_mask [(); ()] AllValid = [true; true]) };
  { tname = "data-eq-ignores-an-absent-element";
    tholds = (fun () ->
      data_eq (Ints #nat #nat [7; 9] [false; true]) (Ints #nat #nat [0; 9] [false; true])
      && not (data_eq (Ints #nat #nat [7; 9] [true; true]) (Ints #nat #nat [0; 9] [true; true]))) } ]

let _ = assert_norm (twins_hold twins == true)
