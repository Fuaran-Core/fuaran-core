module Fuaran.Core.Tests.TotalityTests

// Phase 306 — totality against the machine in Wire and Column. The properties pinned here are the
// ones a proof over the model cannot see, because each is about the machine the code runs on: the
// stack a renderer is given, the float range an aggregate's intermediates live in, the strings a
// reader takes that its writer never wrote, and the UTF-16 units that are not characters.

open System
open System.Threading
open Expecto
open Fuaran.Core

// ---- the aggregate as it stood before the one-pass fold ----
//
// A VERBATIM COPY of `Column.aggregate` and its private helpers as they were before Phase 306 —
// three intermediate lists, the plain formulas. It is the other side of the differential below:
// the one-pass fold must answer what this answers, to the bit, wherever this answered a finite
// value or a refusal. Kept here rather than in the package because it is evidence, not surface.
//
// Since Phase 417 it reads the column it is handed through a record of its own — the name, the
// type and the `Cell list` the column held then — so it needs nothing of the typed `Column` and
// can still be asked about a cell list the new storage refuses to hold.
module private Before =
    /// The column as `Before` read it: its name, its declared type and its cells.
    type CellColumn =
        { Name: string
          Type: ColumnType
          Cells: Cell list }

    let private aggAsNum (c: Cell) : float option =
        match c with
        | Int i -> Some(float i)
        | Float f -> Some f
        | Decimal s -> DecimalText.tryToFloat s
        | _ -> None

    let private aggAsDecimal (c: Cell) : string option =
        match c with
        | Decimal s -> DecimalText.tryCanonical s
        | Int i -> Some(string i)
        | _ -> None


    let private compareFloat (a: float) (b: float) : int =
        match System.Double.IsNaN a, System.Double.IsNaN b with
        | true, true -> 0
        | true, false -> 1
        | false, true -> -1
        | false, false ->
            if a < b then -1
            elif a > b then 1
            else 0

    let private floatToken (f: float) : string =
        match JVal.nonFiniteToken f with
        | Some tok -> tok
        | None -> Canon.canonicalFloat f

    let private aggCompare (a: Cell) (b: Cell) : int option =
        match a, b with
        | (Int _ | Float _), (Int _ | Float _) -> Option.map2 compareFloat (aggAsNum a) (aggAsNum b)
        | (Decimal _ | Int _), (Decimal _ | Int _) ->
            Option.map2 (fun x y -> DecimalText.compare x y) (aggAsDecimal a) (aggAsDecimal b)
            |> Option.flatten
        | Bool x, Bool y -> Some(compare x y)
        | Str x, Str y -> Some(System.String.CompareOrdinal(x, y))
        | Date x, Date y -> Some(System.String.CompareOrdinal(x, y))
        | Timestamp x, Timestamp y -> Some(System.String.CompareOrdinal(x, y))
        | _ -> None

    let private aggFnTag =
        function
        | Sum -> "sum"
        | Mean -> "mean"
        | Min -> "min"
        | Max -> "max"
        | Count -> "count"
        | Median -> "median"
        | StdDev -> "stddev"
        | First -> "first"
        | Last -> "last"
        | CountDistinct -> "countDistinct"

    let private distinctToken (c: Cell) : string =
        match c with
        | Int i -> "i:" + string i
        | Float f -> "f:" + floatToken f
        | Bool b -> "b:" + (if b then "1" else "0")
        | Str s -> "s:" + s
        | Date s -> "d:" + s
        | Timestamp s -> "t:" + s
        | Decimal s -> "m:" + (DecimalText.tryCanonical s |> Option.defaultValue s)
        | Null -> "n:"

    let private admit (col: CellColumn) (c: Cell) : Result<Cell, AggregateError> =
        match c with
        | Null -> Ok Null
        | Decimal s when col.Type = DecimalType ->
            match DecimalText.tryCanonical s with
            | Some canonical -> Ok(Decimal canonical)
            | None -> Error(CellOutsideType(col.Name, col.Type, "decimal text '" + s + "' (not decimal)"))
        | _ ->
            match Cell.typeOf c with
            | Some t when ColumnType.widens t col.Type -> Ok c
            | Some t -> Error(CellOutsideType(col.Name, col.Type, ColumnType.tag t))
            | None -> Ok c

    let private admitAll (col: CellColumn) : Result<Cell list, AggregateError> =
        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | c :: rest ->
                match admit col c with
                | Ok c' -> go (c' :: acc) rest
                | Error e -> Error e

        go [] col.Cells

    let private checkedSumInt (r: int64) : Result<Cell, AggregateError> =
        if r >= int64 System.Int32.MinValue && r <= int64 System.Int32.MaxValue then
            Ok(Int(int r))
        else
            Error(AggregateOverflow("sum overflowed int32: " + string r))

    let aggregateCells (fn: AggFn) (col: CellColumn) : Result<Cell, AggregateError> =
        admitAll col
        |> Result.bind (fun cells ->
            let present () =
                cells |> List.filter (fun c -> not (Cell.isNull c))

            let nums () : Result<float list, AggregateError> =
                let rec go acc =
                    function
                    | [] -> Ok(List.rev acc)
                    | Null :: rest -> go acc rest
                    | c :: rest ->
                        match aggAsNum c with
                        | Some f -> go (f :: acc) rest
                        | None ->
                            let text =
                                match c with
                                | Decimal s -> s
                                | other -> sprintf "%A" other

                            Error(
                                AggregateOverflow(
                                    col.Name
                                    + ": the decimal "
                                    + text
                                    + " is past the float range, and "
                                    + aggFnTag fn
                                    + " is a float"
                                )
                            )

                go [] cells

            let isNumeric = col.Type = IntType || col.Type = FloatType || col.Type = DecimalType

            let requireNumeric (k: unit -> Result<Cell, AggregateError>) =
                if isNumeric then
                    k ()
                else
                    Error(IncompatibleAggType(fn, col.Type, [ IntType; FloatType; DecimalType ]))

            match fn with
            | Count -> Ok(Int(List.length (present ())))
            | CountDistinct -> Ok(Int(present () |> List.map distinctToken |> List.distinct |> List.length))
            | First ->
                Ok(
                    match cells with
                    | [] -> Null
                    | c :: _ -> c
                )
            | Last ->
                Ok(
                    match cells with
                    | [] -> Null
                    | _ -> List.last cells
                )
            | Sum ->
                requireNumeric (fun () ->
                    match col.Type with
                    | DecimalType ->
                        match cells |> List.choose aggAsDecimal with
                        | [] -> Ok Null
                        | first :: rest ->
                            rest
                            |> List.fold (fun acc d -> DecimalText.add acc d |> Option.defaultValue acc) first
                            |> Decimal
                            |> Ok
                    | IntType ->
                        match
                            cells
                            |> List.choose (fun c ->
                                match c with
                                | Int i -> Some(int64 i)
                                | _ -> None)
                        with
                        | [] -> Ok Null
                        | ints -> checkedSumInt (List.sum ints)
                    | _ ->
                        nums ()
                        |> Result.map (fun ns -> if List.isEmpty ns then Null else Float(List.sum ns)))
            | Mean ->
                requireNumeric (fun () ->
                    nums ()
                    |> Result.map (fun ns ->
                        if List.isEmpty ns then
                            Null
                        else
                            Float(List.sum ns / float (List.length ns))))
            | StdDev ->
                requireNumeric (fun () ->
                    nums ()
                    |> Result.map (fun ns ->
                        if List.isEmpty ns then
                            Null
                        else
                            let n = float (List.length ns)
                            let mean = List.sum ns / n
                            let var = (ns |> List.sumBy (fun x -> (x - mean) * (x - mean))) / n
                            Float(sqrt var)))
            | Median ->
                requireNumeric (fun () ->
                    nums ()
                    |> Result.map (fun ns ->
                        match List.sortWith compareFloat ns with
                        | [] -> Null
                        | sorted ->
                            let n = List.length sorted
                            let mid = n / 2

                            if n % 2 = 1 then
                                Float(List.item mid sorted)
                            else
                                Float((List.item (mid - 1) sorted + List.item mid sorted) / 2.0)))
            | Min
            | Max ->
                match present () with
                | [] -> Ok Null
                | first :: rest ->
                    let pick a b =
                        match aggCompare a b with
                        | Some c -> if (fn = Min) = (c <= 0) then a else b
                        | None -> a

                    Ok(List.fold pick first rest))

    /// The aggregate over a typed column, read back as the cells it holds.
    let aggregate (fn: AggFn) (col: Column) : Result<Cell, AggregateError> =
        aggregateCells
            fn
            { Name = col.Name
              Type = col.Type
              Cells = Column.toCells col }

// ---- helpers ----

/// A column of cells that fit its type (Phase 417: `Column.ofCells`, which refuses one that does not).
let private column (name: string) (ty: ColumnType) (cells: Cell list) : Column =
    match Column.ofCells name ty cells with
    | Ok c -> c
    | Error e -> failtestf "column %s did not build: %A" name e

/// Run `f` on a thread with a 1 MB stack and hand back what it returned — the stack the recursive
/// renderers died on at a nesting depth of about 1,400.
let private onOneMegabyteStack (f: unit -> 'T) : 'T =
    let result = ref Unchecked.defaultof<'T>
    let failure: exn option ref = ref None

    let thread =
        Thread(
            (fun () ->
                try
                    result.Value <- f ()
                with e ->
                    failure.Value <- Some e),
            1024 * 1024
        )

    thread.Start()
    thread.Join()

    match failure.Value with
    | Some e -> raise e
    | None -> result.Value

/// A string from its UTF-16 units. A lone surrogate is never written as a `\u` escape in this
/// file: the F# compiler replaces an unpaired surrogate escape in a string literal with U+FFFD, so
/// a literal `"\uD800"` is a well-formed one-character string and tests nothing about ill-formed
/// input. Building the string from `char` values keeps the unit.
let private units (codes: int list) : string =
    String(codes |> List.map char |> Array.ofList)

// Built by a loop, and never compared structurally: equality on a value this deep recurses.
let private nestedArrays (depth: int) (leaf: JVal) : JVal =
    let mutable v = leaf

    for _ in 1..depth do
        v <- JArr [ v ]

    v

let private nestedObjects (depth: int) (leaf: JVal) : JVal =
    let mutable v = leaf

    for _ in 1..depth do
        v <- JObj [ "k", v ]

    v

let private arraysText (depth: int) (leaf: string) =
    String.replicate depth "[" + leaf + String.replicate depth "]"

let private objectsText (depth: int) (leaf: string) =
    String.replicate depth "{\"k\":" + leaf + String.replicate depth "}"

let private allAggregates =
    [ Sum; Mean; Min; Max; Count; Median; StdDev; First; Last; CountDistinct ]

/// An aggregate's answer as text, a float by its BITS — so `-0` and `0`, and two floats one ulp
/// apart, are different answers. Every NaN is one answer: which NaN an operation yields is the
/// processor's, and both folds run the same operations.
let private answer (r: Result<Cell, AggregateError>) : string =
    match r with
    | Ok(Float f) when Double.IsNaN f -> "float nan"
    | Ok(Float f) -> "float bits " + string (BitConverter.DoubleToInt64Bits f)
    | other -> sprintf "%A" other

let private isFinite (f: float) =
    not (Double.IsNaN f || Double.IsInfinity f)

/// A seed-replayable column of any type, as its type and the cells drawn for it: nulls, the float
/// edge values (NaN, both zeroes, an infinity, magnitudes whose sum or square leaves the range, a
/// denormal), ints in float and decimal columns, decimal text canonical and not, a decimal past the
/// float range, and now and then a cell outside the column's type — which, since Phase 417, no
/// column can hold, so the law builds through `Column.ofCells` and meets that cell as a refusal.
let private genColumn (seed: int) : ColumnType * Cell list =
    let mutable st = (uint32 seed * 2654435761u) + 1u

    let next () =
        st <- (st * 1664525u) + 1013904223u
        int (st >>> 1)

    let pick (n: int) = next () % n
    let ty = ColumnType.all[pick ColumnType.all.Length]

    let floats =
        [| Float 0.0
           Float -0.0
           Float nan
           Float infinity
           Float 1e308
           Float -1e308
           Float 1.7e308
           Float 1e200
           Float -1e200
           Float 1e-320 |]

    let decimals =
        [| "1.5"; "1.50"; "-0"; "0.05"; "12"; "x"; String.replicate 400 "9"; "-3.25" |]

    let cellOf () : Cell =
        if pick 5 = 0 then
            Null
        elif pick 14 = 0 then
            [| Bool true; Float 1.5; Str "x"; Decimal "2" |][pick 4]
        else
            match ty with
            | IntType ->
                if pick 9 = 0 then
                    Int Int32.MaxValue
                else
                    Int(pick 2001 - 1000)
            | FloatType ->
                match pick 4 with
                | 0 -> floats[pick floats.Length]
                | 1 -> Int(pick 100)
                | _ -> Float(float (pick 4000 - 2000) / 8.0)
            | BoolType -> Bool(pick 2 = 0)
            | StringType -> Str(string (pick 5))
            | DateType -> Date(sprintf "2026-01-%02d" (1 + pick 28))
            | TimestampType _ -> Timestamp(sprintf "2026-01-01T00:00:%02dZ" (pick 60))
            | DecimalType ->
                if pick 3 = 0 then
                    Int(pick 50)
                else
                    Decimal decimals[pick decimals.Length]

    ty, [ for _ in 1 .. pick 9 -> cellOf () ]

/// FNV-1a over any sequence of units, in 64-bit arithmetic reduced mod 2^32 — a reference that
/// shares no line with `Hash.fnv1a`'s split multiply.
let private fnvOver (units: seq<uint32>) : string =
    let mutable h = 2166136261UL

    for u in units do
        h <- ((h ^^^ uint64 u) * 16777619UL) % 4294967296UL

    (uint32 h).ToString "x8"

[<Tests>]
let tests =
    testList
        "Totality (Phase 306)"
        [
          // ---- the renderers against the stack ----

          testCase "a value 10,000 deep renders through every renderer on a 1 MB stack"
          <| fun _ ->
              // 1,400 is the depth the recursive renderers died at; 10,000 is the phase's bound.
              for depth in [ 1400; 10000 ] do
                  let rendered =
                      onOneMegabyteStack (fun () ->
                          let arrays = nestedArrays depth (JInt 1)
                          let objects = nestedObjects depth (JInt 1)

                          [ "Json.render arrays", Ok(Json.render arrays), arraysText depth "1"
                            "Canon.render arrays", Ok(Canon.render arrays), arraysText depth "1"
                            "Canon.renderOrdered arrays", Ok(Canon.renderOrdered arrays), arraysText depth "1"
                            "Json.tryRender arrays", Json.tryRender arrays, arraysText depth "1"
                            "Canon.tryRender arrays", Canon.tryRender arrays, arraysText depth "1"
                            "Json.render objects", Ok(Json.render objects), objectsText depth "1"
                            "Canon.render objects", Ok(Canon.render objects), objectsText depth "1"
                            "Canon.renderOrdered objects", Ok(Canon.renderOrdered objects), objectsText depth "1"
                            "Json.tryRender objects", Json.tryRender objects, objectsText depth "1"
                            "Canon.tryRender objects", Canon.tryRender objects, objectsText depth "1" ])

                  for (name, got, expected) in rendered do
                      Expect.isTrue (got = Ok expected) (sprintf "%s at depth %d" name depth)

          testCase "… and the guarded renderers name a fault at the bottom of it rather than dying"
          <| fun _ ->
              let nonFinite, illFormed, jsonNonFinite =
                  onOneMegabyteStack (fun () ->
                      Canon.tryRender (nestedArrays 10000 (JFloat nan)),
                      Canon.tryRender (nestedObjects 10000 (JStr(units [ 0xD800 ]))),
                      Json.tryRender (nestedArrays 10000 (JFloat infinity)))

              match nonFinite with
              | Error m ->
                  Expect.stringContains m "NaN at $[0][0]" "the non-finite float is named with its path"

                  Expect.equal
                      m.Length
                      ("non-finite float has no canonical rendering of its own: NaN at $".Length
                       + 30000)
                      "and the path is the whole path"
              | Ok _ -> failtest "a NaN 10,000 deep was rendered"

              match illFormed with
              | Error m ->
                  Expect.stringContains m "a string holds the unpaired surrogate U+D800 at unit 0 at $[\"k\"]" "named"
              | Ok _ -> failtest "a lone surrogate 10,000 deep was rendered"

              Expect.equal
                  jsonNonFinite
                  (Error "non-finite float is not representable on the Fuaran wire: Infinity")
                  "Json.tryRender keeps its message"

          testCase "the text of a value nested past the parse cap is refused by name, not by the stack"
          <| fun _ ->
              let kind =
                  onOneMegabyteStack (fun () ->
                      match Json.parseDetailed (Json.render (nestedArrays 10000 (JInt 1))) with
                      | Error e -> Some e.Kind
                      | Ok _ -> None)

              Expect.equal kind (Some MaxDepthExceeded) "the read side's cap answers"

          // ---- ill-formed UTF-16, wherever a digest depends on it ----

          testCase "the two documents that shared a digest no longer do, because one is refused"
          <| fun _ ->
              // The units D801 D800 encoded as the four bytes of U+10000 until Phase 290, so a document
              // holding them under `k` and one holding U+10000 there parsed to two values with one digest.
              let illFormed = units [ 0xD801; 0xD800 ]
              let astral = "\U00010000"

              Expect.isError (Json.parse ("{\"k\":\"" + illFormed + "\"}")) "the parser refuses the ill-formed one"

              Expect.equal
                  (Json.parse ("{\"k\":\"" + astral + "\"}"))
                  (Ok(JObj [ "k", JStr astral ]))
                  "and reads the other"

              match Canon.tryRender (JObj [ "k", JStr illFormed ]) with
              | Error m ->
                  Expect.equal
                      m
                      "ill-formed string has no canonical rendering of its own: a string holds the unpaired surrogate U+D801 at unit 0 at $[\"k\"]"
                      "the guarded canonical render refuses it, by path"
              | Ok s -> failtestf "rendered %s" s

              let good = JObj [ "k", JStr astral ]
              Expect.equal (Canon.tryRender good) (Ok(Canon.render good)) "a well-formed value is exactly render"

              Expect.equal
                  (Hash.trySha256Hex illFormed)
                  (Error({ Index = 0; Unit = 0xD801 }: IllFormedUtf16))
                  "the guarded digest refuses it"

              Expect.equal
                  (Hash.trySha256Hex astral)
                  (Ok(Hash.sha256Hex astral))
                  "and is sha256Hex over a well-formed string"

              Expect.equal (Hash.tryUtf8Bytes astral) (Ok(Hash.utf8Bytes astral)) "as the guarded encoder is utf8Bytes"

              // The unguarded path still answers, and says why it is not injective: replacement
              // gives the ill-formed string the bytes of two U+FFFD.
              Expect.equal
                  (Hash.sha256Hex illFormed)
                  (Hash.sha256Hex "��")
                  "the unguarded digest keeps the platform's second pre-image"

          testCase
              "a member key is held to the rule, a non-finite float is named first, and the scan is in document order"
          <| fun _ ->
              match Canon.tryRender (JObj [ units [ 0xDC00 ], JStr(units [ 0xD800 ]) ]) with
              | Error m ->
                  Expect.stringContains
                      m
                      "a member key holds the unpaired surrogate U+DC00 at unit 0"
                      "the key, before its value"
              | Ok s -> failtestf "rendered %s" s

              match Json.tryRender (JArr [ JStr "ok"; JStr("a" + units [ 0xD83D ]) ]) with
              | Error m ->
                  Expect.equal
                      m
                      "ill-formed string is not representable on the Fuaran wire: a string holds the unpaired surrogate U+D83D at unit 1 at $[1]"
                      "Json.tryRender refuses it too"
              | Ok s -> failtestf "rendered %s" s

              match Canon.tryRender (JArr [ JStr(units [ 0xD800 ]); JFloat nan ]) with
              | Error m -> Expect.stringContains m "NaN at $[1]" "a non-finite float keeps the refusal it always had"
              | Ok s -> failtestf "rendered %s" s

              Expect.equal
                  (Json.firstIllFormedString (
                      JArr
                          [ JArr [ JStr(units [ 0xD83D; 0xDE00 ]) ]
                            JObj [ "a", JStr(units [ 0xDE00 ]) ] ]
                  ))
                  (Some("$[1][\"a\"]", "a string holds the unpaired surrogate U+DE00 at unit 0"))
                  "a pair is a character; the first unpaired unit in document order is the finding"

          testCase "well-formedness is the strict platform encoder's, over every unit and every boundary pair"
          <| fun _ ->
              // An INDEPENDENT oracle: .NET's UTF-8 encoder with throwOnInvalidBytes refuses exactly
              // the strings that have no code points.
              let strict = Text.UTF8Encoding(false, true)

              let strictBytes (s: string) : byte[] option =
                  try
                      Some(strict.GetBytes s)
                  with :? Text.EncoderFallbackException ->
                      None

              let check (s: string) =
                  let expected = strictBytes s
                  Expect.equal (Json.isWellFormedUtf16 s) expected.IsSome (sprintf "Json.isWellFormedUtf16 %A" s)

                  match Hash.tryUtf8Bytes s, expected with
                  | Ok got, Some bytes -> Expect.equal got bytes (sprintf "the bytes of %A" s)
                  | Error bad, None ->
                      Expect.equal (Some bad.Index) (Json.firstIllFormedUnit s) (sprintf "the two scans agree on %A" s)
                      Expect.equal bad.Unit (int s[bad.Index]) "and the unit is the one at that index"
                  | got, _ -> failtestf "tryUtf8Bytes %A = %A against the strict encoder's %A" s got expected

              for u in 0..0xFFFF do
                  check (string (char u))

              let boundary = [ 0x41; 0xD7FF; 0xD800; 0xDBFF; 0xDC00; 0xDFFF; 0xE000; 0xFFFF ]

              for a in boundary do
                  for b in boundary do
                      check (String([| char a; char b |]))

                      for c in boundary do
                          check (String([| char a; char b; char c |]))

          // ---- the aggregates against the float range ----

          testCase "the one-pass fold answers what the three-list fold answered, to the bit, for every aggregate"
          <| fun _ ->
              let mutable agreed = 0
              let mutable rescued = 0
              let mutable refused = 0

              for seed in 1..6000 do
                  let ty, cells = genColumn seed

                  match Column.ofCells "c" ty cells with
                  | Error(TypeMismatch("c", t, tag)) when t = ty ->
                      // A cell outside the column's type (Phase 417): construction refuses it, where
                      // the fold refused it at `admit`. The differential stays honest by asking the
                      // old fold about the same cells: it must have refused every aggregate, naming
                      // that cell — or, in a decimal column, an EARLIER decimal text that is not
                      // decimal, which it met first and `ofCells` holds as found.
                      let old: Before.CellColumn = { Name = "c"; Type = ty; Cells = cells }

                      for fn in allAggregates do
                          // An agreement like any other: the old fold refused where construction does.
                          agreed <- agreed + 1
                          refused <- refused + 1

                          match Before.aggregateCells fn old with
                          | Error(CellOutsideType("c", t', cell)) when
                              t' = ty
                              && (cell = tag || (ty = DecimalType && cell.StartsWith "decimal text '"))
                              ->
                              ()
                          | other ->
                              failtestf
                                  "seed %d, %A over %A %A: ofCells refused %s, the old fold answered %A"
                                  seed
                                  fn
                                  ty
                                  cells
                                  tag
                                  other
                  | Error e -> failtestf "seed %d: ofCells %A %A refused with %A" seed ty cells e
                  | Ok col ->
                      let inputsFinite =
                          Column.toCells col
                          |> List.forall (fun c ->
                              match c with
                              | Float f -> isFinite f
                              | _ -> true)

                      for fn in allAggregates do
                          let before = Before.aggregate fn col
                          let after = Column.aggregate fn col

                          match before with
                          | Ok(Float f) when inputsFinite && not (isFinite f) ->
                              // The one place the two are MEANT to differ: the old fold answered an
                              // infinity (or a NaN built from two) over finite input.
                              rescued <- rescued + 1

                              match after with
                              | Ok(Float g) when isFinite g -> ()
                              | Error(AggregateOverflow _) -> ()
                              | other -> failtestf "seed %d, %A over %A: was %A, is %A" seed fn col before other
                          | _ ->
                              agreed <- agreed + 1

                              if answer before <> answer after then
                                  failtestf "seed %d, %A over %A: was %A, is %A" seed fn col before after

              Expect.isGreaterThan agreed 50000 "the differential compared a real population"
              Expect.isGreaterThan rescued 20 "and the pool reached the overflow the phase is about"
              Expect.isGreaterThan refused 5000 "and the cells outside the type, refused at construction"

          testCase "the probes that answered an infinity answer the value, and a float Sum past the range is named"
          <| fun _ ->
              let floats (xs: float list) =
                  column "f" FloatType (xs |> List.map Float)

              let near (expected: float) (r: Result<Cell, AggregateError>) (label: string) =
                  match r with
                  | Ok(Float f) when isFinite f && abs (f - expected) <= abs expected * 1e-15 -> ()
                  | other -> failtestf "%s: expected about %g, got %A" label expected other

              Expect.equal
                  (Column.aggregate Median (floats [ 1e308; 1e308 ]))
                  (Ok(Float 1e308))
                  "Median halves before it adds"

              near (1.7e308 / 3.0) (Column.aggregate Mean (floats [ 1.7e308; 1.7e308; -1.7e308 ])) "Mean"
              near 1e200 (Column.aggregate StdDev (floats [ 1e200; -1e200 ])) "StdDev"
              near 0.0 (Column.aggregate StdDev (floats [ 1.7e308; 1.7e308 ])) "StdDev of two equal values at the edge"

              for xs in [ [ 1e308; 1e308 ]; [ 1.7e308; 1.7e308; -1.7e308 ] ] do
                  match Column.aggregate Sum (floats xs) with
                  | Error(AggregateOverflow m) -> Expect.equal m "f: sum overflowed the float range" "named"
                  | other -> failtestf "Sum %A = %A" xs other

              // A column that HOLDS a non-finite value is not an overflow: IEEE answers.
              Expect.equal
                  (Column.aggregate Sum (floats [ infinity; 1.0 ]))
                  (Ok(Float infinity))
                  "an infinity in is an infinity out"

              match Column.aggregate Mean (floats [ nan; 1.0 ]) with
              | Ok(Float f) -> Expect.isTrue (Double.IsNaN f) "a NaN in is a NaN out"
              | other -> failtestf "Mean = %A" other

          testCase "StdDev is the population form"
          <| fun _ ->
              let col =
                  column "f" FloatType ([ 2.0; 4.0; 4.0; 4.0; 5.0; 5.0; 7.0; 9.0 ] |> List.map Float)

              Expect.equal
                  (Column.aggregate StdDev col)
                  (Ok(Float 2.0))
                  "divides by n: the sample form would give 2.138…"

              Expect.equal
                  (Column.aggregate StdDev (column "f" FloatType [ Float 3.0 ]))
                  (Ok(Float 0.0))
                  "one value deviates by nothing"

          testCase "the refusals keep their precedence in one pass"
          <| fun _ ->
              let huge = String.replicate 400 "9"

              // A cell outside the type outranks everything an aggregate could say: since Phase 417
              // it is refused at construction, before any aggregate runs — so a later `Float` in a
              // decimal column still outranks an earlier decimal past the float range.
              match Column.ofCells "m" DecimalType [ Decimal huge; Float 1.0 ] with
              | Error(TypeMismatch("m", DecimalType, "float")) -> ()
              | other -> failtestf "a later cell outside the type outranks an earlier decimal past the range: %A" other

              match
                  Column.aggregate Mean (column "m" DecimalType [ Decimal "1"; Decimal huge; Decimal(huge + "9") ])
              with
              | Error(AggregateOverflow m) ->
                  Expect.stringContains
                      m
                      ("the decimal " + huge + " is past the float range")
                      "the FIRST one past the range"
              | other -> failtestf "%A" other

              // ... and outranks a non-numeric column's refusal the same way: refused at construction.
              match Column.ofCells "s" StringType [ Str "a"; Bool true ] with
              | Error(TypeMismatch("s", StringType, "bool")) -> ()
              | other -> failtestf "a cell outside the type outranks a non-numeric column: %A" other

              match Column.aggregate Sum (column "s" StringType [ Str "a" ]) with
              | Error(IncompatibleAggType(Sum, StringType, _)) -> ()
              | other -> failtestf "%A" other

          // ---- the profile grammar ----

          testCase "the profile grammar is a bijection on canonical strings"
          <| fun _ ->
              let atoms =
                  [| "core"
                     "a"
                     "Z"
                     "@"
                     "."
                     "0"
                     "1"
                     "9"
                     "01"
                     "+"
                     "-"
                     "_"
                     " "
                     "é"
                     "2147483647"
                     "2147483648"
                     string (char 0) |]

              let mutable st = 306u

              let pick (n: int) =
                  st <- (st * 1664525u) + 1013904223u
                  int (st >>> 1) % n

              let mutable accepted = 0

              for _ in 1..60000 do
                  // The shape of a profile, each slot drawn from the whole alphabet: most draws
                  // are near misses, and enough are profiles that the accepting side is measured.
                  let slot () =
                      String.concat "" [ for _ in 0 .. pick 2 -> atoms[pick atoms.Length] ]

                  let s =
                      slot () + "@" + slot () + "." + slot () + (if pick 6 = 0 then slot () else "")

                  match Versioning.Profile.tryParse s with
                  | Ok p ->
                      accepted <- accepted + 1
                      Expect.equal (Versioning.Profile.render p) s (sprintf "render (tryParse %A)" s)
                      Expect.isTrue (Versioning.Profile.isValid p) (sprintf "%A parsed to a valid profile" s)
                      Expect.equal (Versioning.Profile.tryRender p) (Ok s) "and the guarded render agrees"
                  | Error _ -> ()

              Expect.isGreaterThan accepted 50 "the fuzz reached the accepting side"

              for name in [ "core"; "a"; "Fuaran-UI.v2_x"; "z9" ] do
                  for major in [ 0; 1; 10; Int32.MaxValue ] do
                      for minor in [ 0; 7; Int32.MaxValue ] do
                          let p: Versioning.Profile =
                              { Name = name
                                Major = major
                                Minor = minor }

                          Expect.equal
                              (Versioning.Profile.tryParse (Versioning.Profile.render p))
                              (Ok p)
                              (sprintf "%A" p)

          testCase "tryParse refuses what render never writes"
          <| fun _ ->
              let nul = string (char 0)

              for s in
                  [ "core@01.0"
                    "core@1.00"
                    "core@+1.0"
                    "core@-0.0"
                    "core@1.-0"
                    "core@1.0" + nul
                    "core@1" + nul + ".0"
                    "core@1.0 "
                    " core@1.0"
                    "co re@1.0"
                    "c" + nul + "@1.0"
                    "1core@1.0"
                    "-core@1.0"
                    "café@1.0"
                    "a@b@1.0"
                    "core@1.0.0"
                    "core@1."
                    "core@.0"
                    "core@2147483648.0"
                    "core@1.٣" ] do
                  Expect.isError (Versioning.Profile.tryParse s) (sprintf "%A" s)

              for s in [ "core@0.0"; "core@2147483647.2147483647"; "Fuaran-UI.v2_x@10.20" ] do
                  Expect.isOk (Versioning.Profile.tryParse s) s

          testCase "tryRender refuses a profile tryParse could not read back, and the envelope is guarded by it"
          <| fun _ ->
              let profile (name: string) (major: int) (minor: int) : Versioning.Profile =
                  { Name = name
                    Major = major
                    Minor = minor }

              for p in
                  [ profile "" 1 0
                    profile "a b" 1 0
                    profile "a@b" 1 0
                    profile "core" -1 0
                    profile "core" 1 -1
                    profile null 1 0 ] do
                  Expect.isFalse (Versioning.Profile.isValid p) (sprintf "%A is not valid" p)
                  Expect.isError (Versioning.Profile.tryRender p) (sprintf "%A is refused" p)

                  Expect.isError
                      (Versioning.tryRender { Profile = p; Payload = JObj [] })
                      "and so is an envelope carrying it"

              let env: Versioning.Envelope =
                  { Profile = Versioning.Profile.coreV1
                    Payload = JObj [ "a", JInt 1 ] }

              Expect.equal (Versioning.tryRender env) (Ok(Versioning.render env)) "a valid envelope is exactly render"

              Expect.isError
                  (Versioning.tryRender
                      { env with
                          Payload = JStr(units [ 0xD800 ]) })
                  "the payload is held to the guarded canonical render"

          testCase "bump saturates at Int32.MaxValue, and tryBump refuses there"
          <| fun _ ->
              let top: Versioning.Profile =
                  { Name = "core"
                    Major = Int32.MaxValue
                    Minor = Int32.MaxValue }

              let additive = Versioning.Evolution.Additive [ "x" ]
              let breaking = Versioning.Evolution.Breaking([ "x" ], [])

              Expect.equal (Versioning.bump top additive) top "the minor stays, it does not wrap negative"
              Expect.equal (Versioning.bump top breaking) top "the major stays"
              Expect.isError (Versioning.tryBump top additive) "and the refusing form says so"
              Expect.isError (Versioning.tryBump top breaking) "for both counters"

              Expect.equal
                  (Versioning.tryBump top (Versioning.Evolution.Additive []))
                  (Ok top)
                  "a no-op bumps nothing and refuses nothing"

              let below =
                  { top with
                      Major = 1
                      Minor = Int32.MaxValue - 1 }

              Expect.equal
                  (Versioning.tryBump below additive)
                  (Ok { below with Minor = Int32.MaxValue })
                  "the last minor is reachable"

              Expect.equal
                  (Versioning.tryBump { below with Minor = Int32.MaxValue } breaking)
                  (Ok { below with Major = 2; Minor = 0 })
                  "a breaking bump resets a minor at the edge"

              Expect.isTrue
                  (Versioning.Profile.isValid (Versioning.bump top additive))
                  "what bump returns is a profile the wire can carry"

          // ---- the small print ----

          testCase "readInt32 takes a sign and digits, and nothing after them"
          <| fun _ ->
              let nul = string (char 0)

              for tok in
                  [ ""
                    "-"
                    "+"
                    "7" + nul
                    "7" + nul + nul
                    "7 "
                    "7a"
                    "٣"
                    "2147483648"
                    "-2147483649" ] do
                  Expect.equal (Json.readInt32 tok) None (sprintf "%A" tok)

              Expect.equal (Json.readInt32 "5") (Some 5) "digits"
              Expect.equal (Json.readInt32 "-2147483648") (Some Int32.MinValue) "the low edge"
              Expect.equal (Json.readInt32 "2147483647") (Some Int32.MaxValue) "the high edge"
              Expect.equal (Json.readInt32 "007") (Some 7) "leading zeros are the grammar's to refuse, not the reader's"

          testCase "the JSONL scanner reads an integer field the same under every culture, and never throws"
          <| fun _ ->
              // It read through `Int64.Parse` under the current culture, which THROWS on `-5` where
              // the negative sign is not U+002D.
              let read (text: string) =
                  OpStream.Jsonl.parseLine 1 text
                  |> Result.bind (OpStream.Jsonl.intField "n")
                  |> Result.mapError _.Reason

              let documents =
                  [ "{\"n\":-5}", Ok -5
                    "{\"n\":0}", Ok 0
                    "{\"n\":2147483647}", Ok Int32.MaxValue
                    "{\"n\":-2147483648}", Ok Int32.MinValue
                    "{\"n\":2147483648}", Error(JsonlFaultReason.ExpectedInteger "n")
                    "{\"n\":-2147483649}", Error(JsonlFaultReason.ExpectedInteger "n")
                    "{\"n\":01}", Error(JsonlFaultReason.InvalidLiteral "01") ]

              for culture in [ "en-US"; "fa-IR"; "he-IL" ] do
                  let saved = Globalization.CultureInfo.CurrentCulture
                  Globalization.CultureInfo.CurrentCulture <- Globalization.CultureInfo culture

                  try
                      for (text, expected) in documents do
                          Expect.equal (read text) expected (sprintf "%s under %s" text culture)
                  finally
                      Globalization.CultureInfo.CurrentCulture <- saved

          testCase "Json.render of negative zero is the one divergence from Canon that a parse collapses"
          <| fun _ ->
              Expect.equal (Json.render (JFloat -0.0)) "-0" "the author-ordered layout keeps the sign"
              Expect.equal (Canon.render (JFloat -0.0)) "0" "the canonical one collapses it"
              Expect.equal (Json.parse "-0") (Ok(JInt 0)) "-0 is an integer token"
              Expect.equal (Json.render (JInt 0)) "0" "so the second render is 0 …"

              Expect.equal (Json.parse "0" |> Result.map Json.render) (Ok "0") "… and every later one is a fixed point"

          testCase "surplus members of a source and of its columns are read past, and are not re-encoded"
          <| fun _ ->
              let wire =
                  "{\"$type\":\"x\",\"columns\":{\"a\":{\"validity\":[true],\"values\":[1]},\"zz\":{\"validity\":[true],\"values\":[true]}},\"extra\":1,\"schema\":[{\"name\":\"a\",\"type\":\"int\"}]}"

              let expected =
                  Embedded
                      { Schema = [ Field.create "a" IntType ]
                        Columns = [ column "a" IntType [ Int 1 ] ] }

              Expect.equal (ColumnCodec.decode wire) (Ok expected) "the table is the schema's"

              Expect.equal
                  (ColumnCodec.encode expected)
                  "{\"columns\":{\"a\":{\"validity\":[true],\"values\":[1]}},\"schema\":[{\"name\":\"a\",\"type\":\"int\"}]}"
                  "and the surplus members are gone from its encoding"

          testCase "fnv1a folds UTF-16 code units: not bytes, and not code points"
          <| fun _ ->
              let astral = "\U0001F600"

              Expect.equal
                  (Hash.fnv1a astral)
                  (fnvOver [ 0xD83Du; 0xDE00u ])
                  "an astral character is two steps, its surrogates"

              Expect.notEqual (Hash.fnv1a astral) (fnvOver [ 0x1F600u ]) "the code-point reading is another value"

              Expect.notEqual
                  (Hash.fnv1a astral)
                  (fnvOver (Text.Encoding.UTF8.GetBytes astral |> Seq.map uint32))
                  "and so is the byte reading"

              Expect.equal (Hash.fnv1a "abc") (fnvOver ("abc" |> Seq.map uint32)) "all three agree on ASCII" ]
