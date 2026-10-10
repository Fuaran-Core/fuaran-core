namespace Fuaran.Core.Tests

// ============================================================================
//  The host-neutral export of the law vectors this repository owns: the
//  `capabilityLaws` vectors (`laws/capability-laws.json`, Phase 235), the
//  exact decimal's documents (`laws/decimal-laws.json`, Phase 276) and the
//  schema field's documents (`laws/field-laws.json`, Phase 427).
//
//  Until Phase 258 this module also exported `Conformance.transformLaws`'
//  reference answers (`laws/transform-laws.json`, fuaran#1479). Those answers
//  are what `Fuaran.Core.DataFrame.evalPipeline` says, and that evaluator left
//  this repository with the rest of the compute strand (DECISIONS.md D66), so
//  the file is now emitted, stamped and published by the repository that
//  produces the evaluator — https://github.com/Fuaran-Core/fuaran-core-compute
//  — from `0.33.0`, under the same name, with the same shape and the same
//  sample. The JSON renderer and the vector record below are the ones that
//  file was written with, so the two artefacts keep one framing.
// ============================================================================

module LawVectorExport =

    open System.IO
    open System.Reflection
    open System.Text
    open Fuaran.Core

    /// The family directory inside the shared corpus. The directory name is the interface — hosts
    /// resolve `laws/` — so it is named once here.
    let familyDirName = "laws"

    // -----------------------------------------------------------------------
    //  a small deterministic JSON renderer
    // -----------------------------------------------------------------------
    //  Hand-rolled rather than `Utf8JsonWriter`, for two reasons, both about the artefact being an
    //  ORACLE rather than merely valid JSON. The writer's indented mode emits
    //  `Environment.NewLine`, so the same run would produce different bytes on Windows and Linux.
    //  And its default string encoder escapes every character outside a conservative HTML-safe set
    //  — backticks, `+`, apostrophes and em-dashes all become `\uXXXX` — which is a
    //  framework-version-dependent choice this corpus should not inherit. The escaper below is the
    //  JSON minimum and nothing more: the two structural characters, the named short escapes, and
    //  the control range. Everything else is written as itself, in UTF-8.

    let private jstr (s: string) : string =
        let sb = StringBuilder()
        sb.Append('"') |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append("\\\"") |> ignore
            | '\\' -> sb.Append("\\\\") |> ignore
            | '\b' -> sb.Append("\\b") |> ignore
            | '\f' -> sb.Append("\\f") |> ignore
            | '\n' -> sb.Append("\\n") |> ignore
            | '\r' -> sb.Append("\\r") |> ignore
            | '\t' -> sb.Append("\\t") |> ignore
            | c when c < ' ' -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
            | c -> sb.Append(c) |> ignore

        sb.Append('"') |> ignore
        sb.ToString()

    let private jint (n: int) : string = string n

    let private jobj (members: (string * string) list) : string =
        "{ "
        + (members |> List.map (fun (k, v) -> jstr k + ": " + v) |> String.concat ", ")
        + " }"

    /// The pinned kit's version, read from the assembly rather than a literal: the version decides
    /// what the reference answers, so a file naming it from a literal could describe a kit that is
    /// not the one that produced the vectors. The `+<sha>` build metadata is dropped — it moves
    /// with every build, and the committed artefact must be stable across rebuilds of the same pin.
    let kitVersion () : string =
        let asm = typeof<LawResult>.Assembly

        match asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
        | null -> string (asm.GetName().Version)
        | attr -> attr.InformationalVersion.Split('+')[0]

    // -----------------------------------------------------------------------
    //  the vectors
    // -----------------------------------------------------------------------

    /// A single exported vector: what a host is given, and what the reference actually answered.
    type Vector =
        { Id: string
          Case: string
          Input: (string * string) list
          Expected: (string * string) list }

    let private renderVector (v: Vector) : string =
        jobj
            [ "id", jstr v.Id
              "case", jstr v.Case
              "input", jobj v.Input
              "expected", jobj v.Expected ]

    // -----------------------------------------------------------------------
    //  the capabilityLaws vectors (Phase 235)
    // -----------------------------------------------------------------------
    //  Moved here from the UI tier's exporter, which rendered them against whatever Core version it
    //  pinned — so the reference for Core behaviour was produced one repository and one pin away
    //  from Core, and a Core change that moved a capability vector could not re-emit it in the same
    //  change-set. The rendering is carried over UNCHANGED, member for member and character for
    //  character: the file's bytes are the interface five hosts read, and a move is not a licence
    //  to restyle it.
    //
    //  `capabilityLaws` is SELF-CONTAINED — it takes only `(seed, iterations)` and builds its own
    //  capabilities from the seed — so "its vectors" are the `(input, expected verdict)` pairs it
    //  DRAWS. `ConfRng` is public and the law's draw order is fixed (`intBelow 50` lo, `intBelow 50`
    //  span, `intBelow 1000` the captured value, three draws per iteration and nothing else), so the
    //  sample is reproduced here exactly rather than approximated from the law's own `LawResult`
    //  evidence, which says a law HELD — not something another host can re-run. Every `expected` is
    //  computed by CALLING the kit; `LawVectorTests` then asserts each computed verdict is the one
    //  `capabilityLaws` demands, so a vector that disagreed with the law fails before it is published.

    module Capabilities =

        let fileName = "capability-laws.json"

        /// Declared rather than reused from a law invocation, because a host re-running the family
        /// must reproduce the sample from the file alone.
        let seed = 20260904

        /// Far fewer than a law run (100): each vector carries a whole capability declaration, and a
        /// host does not need a hundred draws of the same six shapes to disagree with the reference.
        let iterations = 12

        /// One iteration of `capabilityLaws`' sample: the drawn value space, the capture value, and
        /// the two capabilities the law builds from them.
        type Draw =
            { Iteration: int
              Lo: int
              Hi: int
              Realized: int
              Cap: Capability
              CapB: Capability }

        /// Every non-empty determinism set over the three factors, in `capabilityLaws`' order:
        /// mask `1..7`, bit 0 the clock, bit 1 a random source, bit 2 the network. Iteration `i`
        /// declares the `(i mod 7)`th, so a twelve-iteration file carries all seven labels
        /// (`clock`, `random`, `clock+random`, `network`, `clock+network`, `random+network`,
        /// `clock+random+network`) — the rows the determinism set moved (Phase 319).
        let nonEmptySets: Set<DeterminismFactor> list =
            [ for mask in 1..7 ->
                  [ ClockFactor; RandomFactor; NetworkFactor ]
                  |> List.indexed
                  |> List.filter (fun (k, _) -> (mask >>> k) &&& 1 = 1)
                  |> List.map snd
                  |> Set.ofList ]

        let draws () : Draw list =
            let mutable rng = ConfRng.ofSeed seed

            [ for i in 0 .. iterations - 1 do
                  let lo, r1 = ConfRng.intBelow 50 rng
                  let span, r2 = ConfRng.intBelow 50 r1
                  let hi = lo + span + 1
                  let realized, r3 = ConfRng.intBelow 1000 r2
                  rng <- r3

                  let hole: SigEntry =
                      { Addr = "h0"
                        Name = "x"
                        Kind = ValueHole(IntRange(lo, hi))
                        Required = true }

                  let sg: Signature =
                      { Name = "cap" + string i
                        Holes = [ hole ]
                        Effect =
                          { Host = ReadsHost
                            Determinism = List.item (i % 7) nonEmptySets } }

                  yield
                      { Iteration = i
                        Lo = lo
                        Hi = hi
                        Realized = realized
                        Cap = Capability.create ("cap-" + string i) sg (ClientIsland Pyodide)
                        CapB = Capability.create ("cap-a" + string i) sg BuildTime } ]

        let private jarr (items: string list) : string = "[" + String.concat ", " items + "]"

        let private argsJson (args: (string * string) list) : string =
            args
            |> List.map (fun (addr, value) -> jobj [ "addr", jstr addr; "value", jstr value ])
            |> jarr

        /// The verdict the kit gave, in host-neutral words. A refusal outside the two the law
        /// distinguishes renders as `unexpected`; `LawVectorTests` fails on it, so it can never
        /// reach the corpus.
        let private verdictOf (r: Result<unit, InvokeError>) : (string * string) list =
            match r with
            | Ok() -> [ "verdict", jstr "accept" ]
            | Error(ArgOutOfSpace(addr, _, _)) ->
                [ "verdict", jstr "reject"; "error", jstr "argOutOfSpace"; "addr", jstr addr ]
            | Error(UnknownArg(addr, _)) -> [ "verdict", jstr "reject"; "error", jstr "unknownArg"; "addr", jstr addr ]
            | Error _ -> [ "verdict", jstr "reject"; "error", jstr "unexpected" ]

        /// Every vector for one drawn iteration — the four properties `capabilityLaws` certifies,
        /// each as an independently runnable case.
        let vectorsFor (d: Draw) : Vector list =
            let inSpace = string d.Lo
            let outOfSpace = string (d.Hi + 1)
            let declaration = CapabilityCodec.encode d.Cap
            let acceptArgs = [ "h0", inSpace ]

            let registryIds =
                match
                    CapabilityRegistry.empty
                    |> CapabilityRegistry.register d.Cap
                    |> Result.bind (CapabilityRegistry.register d.CapB)
                with
                | Ok r -> CapabilityRegistry.enumerate r |> List.map _.Id
                | Error _ -> []

            [ { Id = sprintf "capability-%d-accept" d.Iteration
                Case = "validateArgs"
                Input = [ "capability", jstr declaration; "args", argsJson acceptArgs ]
                Expected = verdictOf (Capability.validateArgs d.Cap acceptArgs) }

              { Id = sprintf "capability-%d-out-of-space" d.Iteration
                Case = "validateArgs"
                Input = [ "capability", jstr declaration; "args", argsJson [ "h0", outOfSpace ] ]
                Expected = verdictOf (Capability.validateArgs d.Cap [ "h0", outOfSpace ]) }

              { Id = sprintf "capability-%d-unknown-arg" d.Iteration
                Case = "validateArgs"
                Input = [ "capability", jstr declaration; "args", argsJson [ "nope", inSpace ] ]
                Expected = verdictOf (Capability.validateArgs d.Cap [ "nope", inSpace ]) }

              { Id = sprintf "capability-%d-invocation-key" d.Iteration
                Case = "invocationKey"
                Input = [ "capability", jstr declaration; "args", argsJson acceptArgs ]
                Expected =
                  [ "key", jstr (Capability.invocationKey d.Cap acceptArgs)
                    "determinismTag", jstr (Capability.determinismTag d.Cap)
                    "capturedValue", jint d.Realized ] }

              { Id = sprintf "capability-%d-declaration-round-trip" d.Iteration
                Case = "declarationRoundTrip"
                Input = [ "declaration", jstr declaration ]
                Expected =
                  [ "declaration",
                    jstr (
                        match CapabilityCodec.decode declaration with
                        | Ok c -> CapabilityCodec.encode c
                        | Error m -> "DECODE FAILED: " + m
                    ) ] }

              { Id = sprintf "capability-%d-registry-enumerate" d.Iteration
                Case = "registryEnumerate"
                Input = [ "declarations", jarr [ jstr declaration; jstr (CapabilityCodec.encode d.CapB) ] ]
                Expected = [ "ids", jarr (registryIds |> List.map jstr) ] } ]

        let allVectors () : Vector list = draws () |> List.collect vectorsFor

        let private description =
            "The (input, expected) pairs Fuaran.Core.Conformance.capabilityLaws draws from `seed` over "
            + "`iterations` iterations, computed by calling the pinned kit. A host reproduces the sample with its own "
            + "ConfRng: per iteration draw intBelow(50) = lo, intBelow(50) = span (hi = lo + span + 1), intBelow(1000) = "
            + "the captured value, and build one capability per iteration over a single required value hole `h0` in "
            + "IntRange(lo, hi) with effect readsHost and the determinism set of the (i mod 7)th non-empty subset of {clock, random, network} in mask order (mask 1..7, bit 0 clock, bit 1 random, bit 2 network; the label names the members in that order joined by `+`). `capability` and `declaration` members carry a canonical "
            + "capability declaration as a JSON STRING — decode it with the host's capability codec. `validateArgs` "
            + "vectors expect accept, or reject with a named error class and the offending address. `invocationKey` "
            + "vectors expect the effect-identity key a non-deterministic invocation is journalled under, its "
            + "determinism tag, and the value the replay must return byte-identically. `declarationRoundTrip` expects "
            + "decode-then-encode to return the input bytes. `registryEnumerate` expects id-sorted enumeration "
            + "regardless of insertion order (the declarations are given in insertion order)."

        /// Rendered with an explicit `kitVersion` so the byte-identity pin can render the file at the
        /// version a published copy was stamped with; `render ()` stamps this kit's own version.
        let renderAt (stamp: string) : string =
            let sb = StringBuilder()
            let line (s: string) = sb.Append(s).Append('\n') |> ignore

            line "{"
            line ("  \"family\": " + jstr "capabilityLaws" + ",")
            line ("  \"kitVersion\": " + jstr stamp + ",")
            line ("  \"seed\": " + jint seed + ",")
            line ("  \"iterations\": " + jint iterations + ",")
            line ("  \"description\": " + jstr description + ",")
            line "  \"vectors\": ["

            let rendered = allVectors () |> List.map renderVector
            let last = List.length rendered - 1

            rendered
            |> List.iteri (fun i v -> line ("    " + v + (if i = last then "" else ",")))

            line "  ]"
            line "}"
            sb.ToString()

        let render () : string = renderAt (kitVersion ())

    // -----------------------------------------------------------------------
    //  the decimal documents (Phase 276)
    // -----------------------------------------------------------------------
    //  The exact decimal (DECISIONS.md D72) is a wire commitment every host mirrors: the canonical
    //  form, the order, the sum, the nearest float, the string on the wire and the refusal of a
    //  fractional number token. A host certifies against these documents. They are AUTHORED inputs —
    //  the decimal has no law family that draws them, so there is no seed — each covering a branch
    //  D72 pins, and every `expected` is computed by CALLING the kit, never restated. The refusals
    //  are vectors like any other: a host that accepts what this kit refuses disagrees with it.
    //
    //  A cell is written as `{ "kind", "text" }` rather than as its `Cell.token`, because the token
    //  canonicalises, and the codec's refusal of a NON-canonical cell is one of the behaviours a host
    //  must reproduce.

    module Decimals =

        let fileName = "decimal-laws.json"

        let private jarr (items: string list) : string = "[" + String.concat ", " items + "]"

        let private accept (members: (string * string) list) = ("verdict", jstr "accept") :: members

        let private reject (error: string) =
            [ "verdict", jstr "reject"; "error", jstr error ]

        /// A cell as the documents write it.
        let private cellJson (c: Cell) : string =
            match c with
            | Null -> jobj [ "kind", jstr "null" ]
            | Int i -> jobj [ "kind", jstr "int"; "text", jstr (string i) ]
            | Decimal s -> jobj [ "kind", jstr "decimal"; "text", jstr s ]
            | other -> failwithf "the decimal documents write no %A cell" other

        let private columnErrorClass (e: ColumnError) : string =
            match e with
            | NotJson _ -> "NotJson"
            | MissingField _ -> "MissingField"
            | MalformedShape _ -> "MalformedShape"
            | UnknownType _ -> "UnknownType"
            | TypeMismatch _ -> "TypeMismatch"
            | LengthMismatch _ -> "LengthMismatch"
            | NonFiniteFloat _ -> "NonFiniteFloat"
            | Malformed _ -> "Malformed"
            | RaggedColumns _ -> "RaggedColumns"

        let private aggregateErrorClass (e: AggregateError) : string =
            match e with
            | IncompatibleAggType _ -> "IncompatibleAggType"
            | AggregateOverflow _ -> "AggregateOverflow"
            | CellOutsideType _ -> "CellOutsideType"

        let aggFnTag (fn: AggFn) : string =
            match fn with
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

        /// A one-column decimal document (column `c`) whose `values` array is `values`, raw JSON.
        let document (values: string list) : string =
            "{\"schema\":[{\"name\":\"c\",\"type\":\"decimal\"}],\"columns\":{\"c\":{\"values\":["
            + String.concat "," values
            + "],\"validity\":["
            + (values |> List.map (fun _ -> "true") |> String.concat ",")
            + "]}}}"

        /// The one-column decimal document over `cells`, or the construction's refusal. A cell of
        /// another type is refused by `Column.ofCells` as the same `TypeMismatch` `Table.validate`
        /// (and so `ColumnCodec.tryEncode`) used to answer for it (Phase 417), so the class a vector
        /// exports does not move.
        let source (cells: Cell list) : Result<DataSource, ColumnError> =
            Column.ofCells "c" DecimalType cells
            |> Result.map (fun col ->
                Embedded
                    { Schema = [ Field.create "c" DecimalType ]
                      Columns = [ col ] })

        /// `Column.aggregate` over a decimal column `c` holding `cells`. A cell of another type is
        /// refused at construction now (Phase 417); it maps to the `CellOutsideType` the aggregate
        /// itself answered for that cell before, so the class a vector exports does not move.
        let aggregate (fn: AggFn) (cells: Cell list) : Result<Cell, AggregateError> =
            match Column.ofCells "c" DecimalType cells with
            | Ok col -> Column.aggregate fn col
            | Error(TypeMismatch(name, ty, got)) -> Error(CellOutsideType(name, ty, got))
            | Error e -> failwithf "decimal column c did not build: %A" e

        // ---- the authored inputs ----

        /// `tryCanonical`: K4's refused forms and K3's normalisations.
        let canonicalInputs: (string * string) list =
            [ "refused-empty", ""
              "refused-lone-minus", "-"
              "refused-leading-plus", "+1"
              "refused-double-minus", "--1"
              "refused-bare-point-leading", ".5"
              "refused-bare-point-trailing", "5."
              "refused-minus-bare-point", "-.5"
              "refused-two-points", "1.2.3"
              "refused-exponent", "1e3"
              "refused-exponent-upper", "1.5E-2"
              "refused-separator-comma", "1,000"
              "refused-separator-underscore", "1_000"
              "refused-leading-space", " 1"
              "refused-trailing-space", "1 "
              "refused-hex", "0x10"
              "refused-nan", "NaN"
              "refused-arabic-indic-digit", "١"
              "already-canonical", "12.5"
              "zero", "0"
              "leading-zeros", "007"
              "trailing-zeros", "12.50"
              "zero-fraction", "3.000"
              "negative-zero", "-0"
              "negative-zero-fraction", "-0.000"
              "negative-both-sides", "-012.340"
              "thirty-one-places", "0.0000000000000000000000000000001"
              "forty-digits", "1234567890123456789012345678901234567890.50" ]

        /// `compare`: sign, place, equal spellings, values one float cannot tell apart, refusals.
        let compareInputs: (string * string * string) list =
            [ "negative-below-positive", "-1", "1"
              "signed-zeros-equal", "-0", "0.0"
              "negatives-reversed", "-2", "-10"
              "integer-width", "10", "9"
              "fraction-place", "0.1", "0.09"
              "negative-fraction-place", "-0.1", "-0.09"
              "equal-spellings", "1.50", "001.5"
              "one-float-tenth", "0.1", "0.1000000000000000055511151231257827"
              "one-float-past-2-53", "9007199254740993", "9007199254740992"
              "refused", "1e3", "1" ]

        /// `add`: every carry, borrow and sign branch, and scales no host decimal holds.
        let addInputs: (string * string * string) list =
            [ "carry-through-point", "0.99", "0.01"
              "widening-carry", "999", "1"
              "narrowing-borrow", "1000", "-0.001"
              "cancel-to-unsigned-zero", "1.5", "-1.50"
              "mixed-negative-larger-first", "-5", "3"
              "mixed-negative-larger-second", "3", "-5"
              "mixed-positive-larger-first", "5", "-3"
              "mixed-positive-larger-second", "-3", "5"
              "both-negative", "-1.5", "-2.75"
              "scale-past-host-decimal", "0.0000000000000000000000000000001", "1"
              "magnitude-past-host-decimal", "99999999999999999999999999999999999999", "1"
              "refused", "1", ".5" ]

        /// `tryToFloat`: the one place the type rounds (K7), and where it refuses to.
        let toFloatInputs: (string * string) list =
            [ "tenth", "0.10"
              "past-2-53", "9007199254740993"
              "past-float-range", "1" + String.replicate 399 "0"
              "refused", "1e3" ]

        /// The codec's decode (K5): `values` arrays, raw JSON.
        let decodeInputs: (string * string list) list =
            [ "canonicalises", [ "\"012.50\""; "\"-0\""; "\"3.000\"" ]
              "integer-token", [ "42"; "-7"; "0" ]
              "integer-token-past-int32", [ "3000000000"; "-9007199254740992" ]
              "whole-exponent-token", [ "3e9" ]
              "refuses-fractional-token", [ "3.5" ]
              "refuses-integer-token-past-2-53", [ "9007199254740994" ]
              "refuses-whole-float-past-2-53", [ "1e300" ]
              "refuses-exponent-text", [ "\"1e3\"" ]
              "refuses-plus-text", [ "\"+1\"" ]
              "refuses-bool", [ "true" ] ]

        /// The codec's guarded encode: the canonical bytes, and the refusal of a non-canonical cell.
        let encodeInputs: (string * Cell list) list =
            [ "canonical", [ Decimal "12.5"; Null; Decimal "-0.001"; Int 7 ]
              "refuses-non-canonical", [ Decimal "12.50" ] ]

        /// `Column.aggregate` over a decimal column (K7).
        let aggregateColumn: Cell list =
            [ Decimal "0.1"
              Decimal "0.2"
              Null
              Decimal "-0.3"
              Decimal "1.50"
              Decimal "1.5"
              Int 2 ]

        let aggregateInputs: (string * AggFn * Cell list) list =
            [ "sum", Sum, aggregateColumn
              "min", Min, aggregateColumn
              "max", Max, aggregateColumn
              "mean", Mean, aggregateColumn
              "median", Median, aggregateColumn
              "stddev", StdDev, aggregateColumn
              "count-distinct", CountDistinct, aggregateColumn
              "sum-tenths-exact", Sum, List.replicate 10 (Decimal "0.1")
              "mean-past-float", Mean, [ Decimal("1" + String.replicate 399 "0"); Decimal "0.5" ]
              "sum-not-decimal", Sum, [ Decimal "1"; Decimal "1e3" ] ]

        // ---- the vectors, computed by calling the kit ----

        let allVectors () : Vector list =
            [ for name, text in canonicalInputs ->
                  { Id = "canonical-" + name
                    Case = "canonical"
                    Input = [ "text", jstr text ]
                    Expected =
                      match DecimalText.tryCanonical text with
                      | Some c -> accept [ "canonical", jstr c ]
                      | None -> reject "notDecimal" }
              for name, a, b in compareInputs ->
                  { Id = "compare-" + name
                    Case = "compare"
                    Input = [ "a", jstr a; "b", jstr b ]
                    Expected =
                      match DecimalText.compare a b with
                      | Some c -> accept [ "order", jint c ]
                      | None -> reject "notDecimal" }
              for name, a, b in addInputs ->
                  { Id = "add-" + name
                    Case = "add"
                    Input = [ "a", jstr a; "b", jstr b ]
                    Expected =
                      match DecimalText.add a b with
                      | Some s -> accept [ "sum", jstr s ]
                      | None -> reject "notDecimal" }
              for name, text in toFloatInputs ->
                  { Id = "to-float-" + name
                    Case = "toFloat"
                    Input = [ "text", jstr text ]
                    Expected =
                      match DecimalText.tryToFloat text, DecimalText.tryCanonical text with
                      | Some f, _ -> accept [ "float", jstr (Canon.canonicalFloat f) ]
                      | None, Some _ -> reject "pastFloatRange"
                      | None, None -> reject "notDecimal" }
              for name, values in decodeInputs ->
                  let doc = document values

                  { Id = "decode-" + name
                    Case = "codecDecode"
                    Input = [ "document", jstr doc ]
                    Expected =
                      match ColumnCodec.decode doc with
                      | Ok src -> accept [ "canonical", jstr (ColumnCodec.encode src) ]
                      | Error e -> reject (columnErrorClass e) }
              for name, cells in encodeInputs ->
                  { Id = "encode-" + name
                    Case = "codecEncode"
                    Input = [ "cells", jarr (cells |> List.map cellJson) ]
                    Expected =
                      match source cells |> Result.bind ColumnCodec.tryEncode with
                      | Ok text -> accept [ "canonical", jstr text ]
                      | Error e -> reject (columnErrorClass e) }
              for name, fn, cells in aggregateInputs ->
                  { Id = "aggregate-" + name
                    Case = "aggregate"
                    Input = [ "fn", jstr (aggFnTag fn); "cells", jarr (cells |> List.map cellJson) ]
                    Expected =
                      match aggregate fn cells with
                      | Ok cell -> accept [ "token", jstr (Cell.token cell) ]
                      | Error e -> reject (aggregateErrorClass e) } ]

        let private description =
            "The exact decimal (D72): authored inputs over every behaviour a host mirrors, each `expected` computed "
            + "by calling the pinned kit. No seed: nothing is drawn. Every vector expects `verdict` accept or reject; "
            + "a reject names its class. `canonical` reads `text` (the grammar -?[0-9]+(\\.[0-9]+)? with ASCII digits "
            + "only) and expects the canonical form: no leading zero, no trailing fraction zero, no point on a whole "
            + "number, no sign on zero. `compare` expects the numeric `order` of `a` and `b` as -1, 0 or 1. `add` "
            + "expects the exact canonical `sum`. `toFloat` expects the nearest double in the canonical float layout, "
            + "and rejects a value past the float range rather than answering an infinity. `codecDecode` decodes a "
            + "one-column decimal `document` and expects its canonical re-encoding; a decimal is a JSON string on "
            + "the wire, an integer token is read exactly, and a fractional token or a whole token past 2^53 is "
            + "refused. `codecEncode` builds column `c` of type decimal from `cells` ({kind, text}; a null cell has "
            + "no text) and expects the guarded encode's canonical bytes, or the refusal of a cell whose text is not "
            + "canonical. `aggregate` folds `fn` over the same column and expects the result cell's token (`m:` a "
            + "decimal, `i:` an int, `f:` a float): Sum is exact, Min and Max return the winning cell as it stands, "
            + "Mean, Median and StdDev (the population form) read each value at its nearest double, and a value past "
            + "the float range is refused for them."

        /// Rendered with an explicit `kitVersion`, as `Capabilities.renderAt` is.
        let renderAt (stamp: string) : string =
            let sb = StringBuilder()
            let line (s: string) = sb.Append(s).Append('\n') |> ignore

            line "{"
            line ("  \"family\": " + jstr "decimal" + ",")
            line ("  \"kitVersion\": " + jstr stamp + ",")
            line ("  \"description\": " + jstr description + ",")
            line "  \"vectors\": ["

            let rendered = allVectors () |> List.map renderVector
            let last = List.length rendered - 1

            rendered
            |> List.iteri (fun i v -> line ("    " + v + (if i = last then "" else ",")))

            line "  ]"
            line "}"
            sb.ToString()

        let render () : string = renderAt (kitVersion ())

    // -----------------------------------------------------------------------
    //  the field vectors (Phase 427)
    // -----------------------------------------------------------------------
    //  A schema entry is a `Field`: a name, a type and the metadata that says what the column MEANS
    //  — an optional unit (Phase 426's `UnitOfMeasure`, as its canonical text on the wire), a label,
    //  a description and an extension map every host preserves verbatim. These vectors are the
    //  reference answers a host mirrors: the entry's canonical bytes (the members a field states and
    //  no other, so an entry without metadata is the pre-427 `{name, type}`), the decode's refusals
    //  and its unit canonicalisation, the schema fingerprint with and without metadata, the schema
    //  delta's `amended` member and its replay, and the compatibility verdict a unit change earns.
    //  AUTHORED inputs, as the decimal family's are: nothing is drawn.

    module Fields =

        let fileName = "field-laws.json"

        let private accept (members: (string * string) list) = ("verdict", jstr "accept") :: members

        let private reject (error: string) =
            [ "verdict", jstr "reject"; "error", jstr error ]

        let private columnErrorClass (e: ColumnError) : string =
            match e with
            | NotJson _ -> "NotJson"
            | MissingField _ -> "MissingField"
            | MalformedShape _ -> "MalformedShape"
            | UnknownType _ -> "UnknownType"
            | TypeMismatch _ -> "TypeMismatch"
            | LengthMismatch _ -> "LengthMismatch"
            | NonFiniteFloat _ -> "NonFiniteFloat"
            | Malformed _ -> "Malformed"
            | RaggedColumns _ -> "RaggedColumns"

        let private unit (text: string) : UnitOfMeasure =
            match Unit.parse text with
            | Ok u -> u
            | Error e -> failwithf "the field documents' unit %s did not parse: %A" text e

        /// A schema as the wire spells it — the entry objects, under `Canon` — the shape a host
        /// reads a schema in, so a vector's input needs no second schema notation.
        let schemaText (s: Schema) : string =
            Canon.render (JArr(s |> List.map ColumnCodec.fieldJson))

        /// The zero-row document over `schema`: every column present and empty, so the entries are
        /// the whole of what the bytes say.
        let document (schema: Schema) : string =
            let columns =
                schema
                |> List.map (fun f ->
                    match Column.ofCells f.Name f.Type [] with
                    | Ok c -> c
                    | Error e -> failwithf "an empty column %s did not build: %A" f.Name e)

            ColumnCodec.encode (Embedded { Schema = schema; Columns = columns })

        // ---- the authored inputs ----

        let private mass =
            Field.create "mass" FloatType
            |> Field.withUnit (unit "kg")
            |> Field.withLabel "Payload mass"
            |> Field.withDescription "The mass carried, measured at departure."
            |> Field.withExt "vendor.format" "0.00"

        /// `fieldEncode`: a field from its members, to the entry's canonical bytes.
        let encodeInputs: (string * Field) list =
            [ "bare", Field.create "n" IntType
              "unit-only", Field.create "d" FloatType |> Field.withUnit (unit "m")
              "every-member", mass
              "empty-label", Field.create "n" IntType |> Field.withLabel ""
              "currency-and-prefix", Field.create "price" DecimalType |> Field.withUnit (unit "c[GBP]")
              "derived-unit", Field.create "v" FloatType |> Field.withUnit (unit "m/s2")
              "dimensionless", Field.create "ratio" FloatType |> Field.withUnit Unit.dimensionless
              "ext-keys-sorted",
              Field.create "x" StringType
              |> Field.withExt "vendor.b" "2"
              |> Field.withExt "vendor.a" "1"
              |> Field.withExt "other.z" "3" ]

        /// `codecDecode`: a zero-row document whose entry carries members, raw JSON — the accepted
        /// forms re-encode canonically, the refused ones name their class.
        let decodeInputs: (string * string) list =
            let entry (members: string) =
                "{\"schema\":[{\"name\":\"c\",\"type\":\"float\""
                + members
                + "}],\"columns\":{\"c\":{\"values\":[],\"validity\":[]}}}"

            [ "bare", entry ""
              "every-member",
              entry
                  ",\"unit\":\"kg\",\"label\":\"Payload mass\",\"description\":\"A sentence.\",\"ext\":{\"vendor.format\":\"0.00\"}"
              "canonicalises-unit-order", entry ",\"unit\":\"s-1.m\""
              "canonicalises-unit-division", entry ",\"unit\":\"m/s/s\""
              "canonicalises-unit-alias", entry ",\"unit\":\"l\""
              "empty-ext", entry ",\"ext\":{}"
              "unknown-member-read-past", entry ",\"nullable\":true"
              "refuses-unknown-unit", entry ",\"unit\":\"furlong\""
              "refuses-empty-unit", entry ",\"unit\":\"\""
              "refuses-annotation-unit", entry ",\"unit\":\"m{tall}\""
              "refuses-affine-unit", entry ",\"unit\":\"Cel\""
              "refuses-unit-not-string", entry ",\"unit\":1"
              "refuses-label-not-string", entry ",\"label\":[]"
              "refuses-description-not-string", entry ",\"description\":7"
              "refuses-ext-not-object", entry ",\"ext\":[\"a\"]"
              "refuses-ext-member-not-string", entry ",\"ext\":{\"vendor.n\":1}" ]

        /// `fingerprint`: schemas with and without metadata — the metadata-free values are the ones
        /// the pre-427 kit answered.
        let fingerprintInputs: (string * Schema) list =
            [ "two-columns", [ Field.create "a" IntType; Field.create "b" FloatType ]
              "with-unit",
              [ Field.create "a" IntType
                Field.create "b" FloatType |> Field.withUnit (unit "kg") ]
              "with-label-kg", [ Field.create "a" IntType; Field.create "b" FloatType |> Field.withLabel "kg" ]
              "with-empty-label", [ Field.create "a" IntType |> Field.withLabel "" ]
              "every-member", [ mass ]
              "unicode-name", [ Field.create "café" FloatType |> Field.withDescription "日本語" ] ]

        /// `delta`: `(old, target)` pairs — the delta's canonical bytes and its replay.
        let deltaInputs: (string * Schema * Schema) list =
            let m = Field.create "m" FloatType |> Field.withUnit (unit "kg")

            [ "no-metadata",
              [ Field.create "a" IntType; Field.create "b" StringType ],
              [ Field.create "b" StringType; Field.create "a" FloatType ]
              "unit-changed", [ m ], [ m |> Field.withUnit (unit "[lb_av]") ]
              "unit-added-and-label", [ Field.create "m" FloatType ], [ m |> Field.withLabel "Mass" ]
              "unit-withdrawn-and-retyped", [ m ], [ Field.create "m" DecimalType ]
              "ext-moved",
              [ Field.create "m" FloatType |> Field.withExt "vendor.k" "1" ],
              [ Field.create "m" FloatType
                |> Field.withExt "vendor.k" "2"
                |> Field.withExt "vendor.j" "x" ]
              "added-with-metadata", [ Field.create "a" IntType ], [ Field.create "a" IntType; mass ] ]

        /// `classify`: a delta against a depended-on set, to its verdict.
        let classifyInputs: (string * Schema * Schema * string list) list =
            let m = Field.create "m" FloatType |> Field.withUnit (unit "kg")

            [ "unit-replaced-depended", [ m ], [ m |> Field.withUnit (unit "[lb_av]") ], [ "m" ]
              "unit-replaced-undepended", [ m ], [ m |> Field.withUnit (unit "[lb_av]") ], [ "x" ]
              "unit-withdrawn-depended", [ m ], [ Field.create "m" FloatType ], [ "m" ]
              "unit-stated-depended", [ Field.create "m" FloatType ], [ m ], [ "m" ]
              "label-changed-depended", [ m ], [ m |> Field.withLabel "Mass" ], [ "m" ]
              "ext-changed-depended", [ m ], [ m |> Field.withExt "vendor.k" "v" ], [ "m" ]
              "ext-changed-and-removed",
              [ m; Field.create "z" IntType ],
              [ m |> Field.withExt "vendor.k" "v" ],
              [ "m"; "z" ] ]

        let private compatTag (c: SchemaCompat) : string =
            match c with
            | SchemaCompat.Compatible -> "compatible"
            | SchemaCompat.Breaking _ -> "breaking"
            | SchemaCompat.Unknown _ -> "unknown"

        // ---- the vectors, computed by calling the kit ----

        let allVectors () : Vector list =
            [ for name, f in encodeInputs ->
                  let members =
                      [ "name", jstr f.Name; "type", jstr (ColumnType.tag f.Type) ]
                      @ (f.Unit
                         |> Option.map (fun u -> [ "unit", jstr (Unit.render u) ])
                         |> Option.defaultValue [])
                      @ (f.Label |> Option.map (fun l -> [ "label", jstr l ]) |> Option.defaultValue [])
                      @ (f.Description
                         |> Option.map (fun d -> [ "description", jstr d ])
                         |> Option.defaultValue [])
                      @ (if f.Ext.IsEmpty then
                             []
                         else
                             [ "ext", jobj (f.Ext |> Map.toList |> List.map (fun (k, v) -> k, jstr v)) ])

                  { Id = "encode-" + name
                    Case = "fieldEncode"
                    Input = members
                    Expected = accept [ "canonical", jstr (Canon.render (ColumnCodec.fieldJson f)) ] }
              for name, doc in decodeInputs ->
                  { Id = "decode-" + name
                    Case = "codecDecode"
                    Input = [ "document", jstr doc ]
                    Expected =
                      match ColumnCodec.decode doc with
                      | Ok src -> accept [ "canonical", jstr (ColumnCodec.encode src) ]
                      | Error e -> reject (columnErrorClass e) }
              for name, schema in fingerprintInputs ->
                  { Id = "fingerprint-" + name
                    Case = "fingerprint"
                    Input = [ "schema", schemaText schema ]
                    Expected = accept [ "fingerprint", jstr (Schema.fingerprint schema) ] }
              for name, old, target in deltaInputs ->
                  let delta = Schema.diff old target

                  { Id = "delta-" + name
                    Case = "delta"
                    Input = [ "old", schemaText old; "target", schemaText target ]
                    Expected =
                      match Schema.patch old delta with
                      | Ok patched ->
                          accept
                              [ "canonical", jstr (SchemaDeltaCodec.encode delta)
                                "patched", schemaText patched ]
                      | Error e -> failwithf "the delta %s did not replay: %A" name e }
              for name, old, target, deps in classifyInputs ->
                  { Id = "classify-" + name
                    Case = "classify"
                    Input =
                      [ "old", schemaText old
                        "target", schemaText target
                        "dependsOn", "[" + (deps |> List.map jstr |> String.concat ", ") + "]" ]
                    Expected = accept [ "compat", jstr (compatTag (Schema.classify deps (Schema.diff old target))) ] } ]

        let private description =
            "The schema field (Phase 427): a column's name and type, and the metadata that says what it means — "
            + "an optional unit (the canonical text of a unit the unit algebra parses), a label, a description, and an "
            + "extension object of string members a host preserves verbatim. Authored inputs, each `expected` computed "
            + "by calling the pinned kit; no seed. `fieldEncode` builds a field from its members (`name`, `type`, and "
            + "whichever of `unit`, `label`, `description`, `ext` are given) and expects the schema entry's canonical "
            + "bytes: the members the field states and no other, keys sorted, so an entry without metadata is `{name, "
            + "type}`. `codecDecode` decodes a zero-row `document` and expects its canonical re-encoding — a parseable "
            + "unit spelling is read to the unit and written back canonical, an empty `ext` is dropped, an unknown "
            + "member is read past — or the refusal's class: a unit the algebra does not parse, a non-string `unit`, "
            + "`label` or `description`, an `ext` that is not an object, and an `ext` member that is not a string are "
            + "each `MalformedShape`. `fingerprint` reads a `schema` (an array of entries) and expects its FNV-1a "
            + "fingerprint: a schema without metadata has the fingerprint it had before this phase, and metadata moves "
            + "it. `delta` reads `old` and `target` schemas and expects the canonical bytes of the delta between them "
            + "(`amended` present only when a member moved) and `patched`, the schema replaying that delta over `old` "
            + "yields, which is `target`. `classify` reads the same pair and `dependsOn` and expects `compat`: "
            + "`breaking` when a depended-on column's stated unit was replaced or withdrawn (or it was removed or "
            + "narrowed), `unknown` when only a depended-on column's extension member moved, else `compatible`."

        /// Rendered with an explicit `kitVersion`, as `Decimals.renderAt` is.
        let renderAt (stamp: string) : string =
            let sb = StringBuilder()
            let line (s: string) = sb.Append(s).Append('\n') |> ignore

            line "{"
            line ("  \"family\": " + jstr "fieldLaws" + ",")
            line ("  \"kitVersion\": " + jstr stamp + ",")
            line ("  \"description\": " + jstr description + ",")
            line "  \"vectors\": ["

            let rendered = allVectors () |> List.map renderVector
            let last = List.length rendered - 1

            rendered
            |> List.iteri (fun i v -> line ("    " + v + (if i = last then "" else ",")))

            line "  ]"
            line "}"
            sb.ToString()

        let render () : string = renderAt (kitVersion ())

    // -----------------------------------------------------------------------
    //  writing
    // -----------------------------------------------------------------------

    let familyDir (corpusDir: string) : string = Path.Combine(corpusDir, familyDirName)

    let capabilityPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, Capabilities.fileName)

    /// Every law set this repository emits, as (path under `corpusDir`, rendered bytes) — the one
    /// list `write` walks, so a family added here cannot be rendered and then forgotten by the
    /// writer.
    let decimalPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, Decimals.fileName)

    let fieldPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, Fields.fileName)

    let emitted (corpusDir: string) : (string * string) list =
        [ capabilityPath corpusDir, Capabilities.render ()
          decimalPath corpusDir, Decimals.render ()
          fieldPath corpusDir, Fields.render () ]

    // -----------------------------------------------------------------------
    //  Phase 394 — the corpus `laws/manifest.json` rows beside the files
    // -----------------------------------------------------------------------
    //  The corpus indexes every family in `laws/` in one hand-curated `manifest.json`, and each
    //  row Core's families own repeats members DERIVED from the file it names: the `kitVersion`
    //  stamp, the vector count, and (for a drawn family) the seed and iterations. Until this phase
    //  `write` left the manifest alone, so every `<Version>` move restamped the file and left its
    //  row naming the previous kit — a lag the corpus carried until someone hand-edited the row.
    //
    //  The reason the manifest was not written still holds for the INDEX: it is a shared file that
    //  lists families this repository does not own (`transformLaws` is the compute repository's), and
    //  a wholesale renderer would drop what it does not know. So the restamp is surgical: it edits
    //  exactly the derived members of exactly the rows of the families written here, refuses a row
    //  that is absent, repeated, or missing a derived member (adding a family to the shared index, or
    //  a member to a row, is an edit made once, by hand), and proves its own edit by parsing the
    //  result and requiring it to equal the original with only those members replaced.

    /// The corpus's index over every family in `laws/`.
    let manifestFileName = "manifest.json"

    let manifestPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, manifestFileName)

    /// Each emitted family's row id in the corpus manifest, with the file that row names.
    let manifestRows: (string * string) list =
        [ "capabilityLaws", Capabilities.fileName
          "decimal", Decimals.fileName
          "fieldLaws", Fields.fileName ]

    /// The members of a manifest row derived from the text of the file it names, in row order:
    /// `kitVersion` always; `seed` and `iterations` when the file declares them (a drawn family);
    /// `vectors` as the length of the file's `vectors` array.
    let derivedMembers (fileText: string) : Result<(string * JVal) list, string> =
        match Json.parse fileText with
        | Error e -> Error(sprintf "the law file is not JSON (%s)" e)
        | Ok(JObj fields) ->
            let find name =
                fields |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd

            match find "kitVersion", find "vectors" with
            | Some(JStr stamp), Some(JArr vectors) ->
                let drawn =
                    [ "seed"; "iterations" ]
                    |> List.choose (fun name ->
                        match find name with
                        | Some(JInt n) -> Some(name, JInt n)
                        | _ -> None)

                Ok([ "kitVersion", JStr stamp ] @ drawn @ [ "vectors", JInt(List.length vectors) ])
            | _ -> Error "the law file declares no string `kitVersion` and `vectors` array"
        | Ok _ -> Error "the law file is not a JSON object"

    let private renderMember (value: JVal) : string =
        match value with
        | JStr s -> jstr s
        | JInt n -> jint n
        | other -> Json.render other

    let private rowLine (id: string) =
        System.Text.RegularExpressions.Regex(
            "^\\s*\\{\\s*\"id\"\\s*:\\s*\""
            + System.Text.RegularExpressions.Regex.Escape id
            + "\"",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant
        )

    // A member's NAME in quotes immediately followed by a colon can only be a member: inside a JSON
    // string every quote is escaped, so `\"vectors\": 5` in a description never matches.
    let private memberPattern (name: string) =
        System.Text.RegularExpressions.Regex(
            "\"" + name + "\"\\s*:\\s*(\"(?:[^\"\\\\]|\\\\.)*\"|-?[0-9]+)",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant
        )

    let private familiesOf (manifest: JVal) : JVal list option =
        match manifest with
        | JObj fields ->
            match fields |> List.tryFind (fun (k, _) -> k = "families") with
            | Some(_, JArr rows) -> Some rows
            | _ -> None
        | _ -> None

    let private rowId (row: JVal) : string option =
        match row with
        | JObj fields ->
            match fields |> List.tryFind (fun (k, _) -> k = "id") with
            | Some(_, JStr id) -> Some id
            | _ -> None
        | _ -> None

    /// The parsed manifest with each named row's members replaced — the tree a correct restamp must
    /// produce, and the yardstick it is held to.
    let private substituted (manifest: JVal) (rows: (string * (string * JVal) list) list) : JVal =
        match manifest with
        | JObj fields ->
            fields
            |> List.map (fun (k, v) ->
                match k, v with
                | "families", JArr families ->
                    k,
                    JArr(
                        families
                        |> List.map (fun row ->
                            match rowId row, row with
                            | Some id, JObj members ->
                                match rows |> List.tryFind (fun (rid, _) -> rid = id) with
                                | Some(_, wanted) ->
                                    JObj(
                                        members
                                        |> List.map (fun (m, mv) ->
                                            match wanted |> List.tryFind (fun (w, _) -> w = m) with
                                            | Some(_, nv) -> m, nv
                                            | None -> m, mv)
                                    )
                                | None -> row
                            | _ -> row)
                    )
                | _ -> k, v)
            |> JObj
        | other -> other

    /// `manifestText` with each row in `rows` (id, derived members) restamped, or why not. Every byte
    /// outside the replaced member values is kept, so the shared file's hand-curated layout survives.
    let restampManifest (manifestText: string) (rows: (string * (string * JVal) list) list) : Result<string, string> =
        match Json.parse manifestText with
        | Error e -> Error(sprintf "laws/%s is not JSON (%s)" manifestFileName e)
        | Ok original ->
            let lines = manifestText.Split('\n')

            let edit (acc: Result<string[], string>) (id: string, members: (string * JVal) list) =
                acc
                |> Result.bind (fun (current: string[]) ->
                    let hits =
                        current
                        |> Array.indexed
                        |> Array.filter (fun (_, l) -> (rowLine id).IsMatch l)
                        |> Array.map fst

                    match hits with
                    | [| index |] ->
                        members
                        |> List.fold
                            (fun (line: Result<string, string>) (name, value) ->
                                line
                                |> Result.bind (fun (l: string) ->
                                    let pattern = memberPattern name

                                    match pattern.Matches(l).Count with
                                    | 1 ->
                                        Ok(
                                            pattern.Replace(
                                                l,
                                                (fun (m: System.Text.RegularExpressions.Match) ->
                                                    m.Value.Substring(0, m.Groups[1].Index - m.Index)
                                                    + renderMember value),
                                                1
                                            )
                                        )
                                    | 0 ->
                                        Error(
                                            sprintf
                                                "laws/%s row `%s` declares no `%s` member — add it to the row once, by hand"
                                                manifestFileName
                                                id
                                                name
                                        )
                                    | n ->
                                        Error(
                                            sprintf
                                                "laws/%s row `%s` carries `%s` %d times on its line"
                                                manifestFileName
                                                id
                                                name
                                                n
                                        )))
                            (Ok current[index])
                        |> Result.map (fun l ->
                            let next = Array.copy current
                            next[index] <- l
                            next)
                    | [||] ->
                        Error(
                            sprintf
                                "laws/%s carries no one-line row for `%s` — adding a family to the shared index is an edit made once, by hand"
                                manifestFileName
                                id
                        )
                    | many -> Error(sprintf "laws/%s carries %d rows for `%s`" manifestFileName many.Length id))

            rows
            |> List.fold edit (Ok lines)
            |> Result.bind (fun edited ->
                let text = String.concat "\n" edited

                match Json.parse text with
                | Error e -> Error(sprintf "the restamped laws/%s does not parse (%s)" manifestFileName e)
                | Ok restamped when restamped = substituted original rows -> Ok text
                | Ok _ ->
                    Error(
                        sprintf
                            "the restamped laws/%s differs from the original by more than the derived members — refused"
                            manifestFileName
                    ))

    /// Each emitted family's row against the text of the file it names: `[]` when every derived
    /// member agrees, else one finding per disagreement (or per row or file that cannot be read).
    let manifestFindings (manifestText: string) (files: (string * string option) list) : string list =
        match Json.parse manifestText with
        | Error e -> [ sprintf "laws/%s is not JSON (%s)" manifestFileName e ]
        | Ok manifest ->
            match familiesOf manifest with
            | None -> [ sprintf "laws/%s declares no `families` array" manifestFileName ]
            | Some families ->
                files
                |> List.collect (fun (id, fileText) ->
                    match families |> List.filter (fun r -> rowId r = Some id) with
                    | [ JObj row ] ->
                        match fileText with
                        | None -> [ sprintf "row `%s`: the file it names is absent beside it" id ]
                        | Some text ->
                            match derivedMembers text with
                            | Error e -> [ sprintf "row `%s`: %s" id e ]
                            | Ok derived ->
                                derived
                                |> List.choose (fun (name, want) ->
                                    match row |> List.tryFind (fun (k, _) -> k = name) with
                                    | Some(_, have) when have = want -> None
                                    | Some(_, have) ->
                                        Some(
                                            sprintf
                                                "row `%s`: `%s` is %s, the file beside it says %s"
                                                id
                                                name
                                                (renderMember have)
                                                (renderMember want)
                                        )
                                    | None -> Some(sprintf "row `%s`: declares no `%s` member" id name))
                    | [] -> [ sprintf "laws/%s carries no row for `%s`" manifestFileName id ]
                    | many -> [ sprintf "laws/%s carries %d rows for `%s`" manifestFileName many.Length id ])

    /// The rows `write` restamps, derived from the text it is about to write — the same bytes, so
    /// a row can never name a file other than the one beside it.
    let private writtenRows (corpusDir: string) : (string * (string * JVal) list) list =
        let texts = emitted corpusDir

        manifestRows
        |> List.map (fun (id, fileName) ->
            let text =
                texts |> List.find (fun (path, _) -> Path.GetFileName path = fileName) |> snd

            match derivedMembers text with
            | Ok members -> id, members
            | Error e -> failwithf "the %s this kit renders did not read back: %s" fileName e)

    /// Write the vectors with LF endings, whatever the host platform — the corpus is byte-compared
    /// by several hosts on three operating systems — and, where a family `manifest.json` sits
    /// beside them (the corpus; this repository's `conformance/laws/` carries none), restamp the
    /// rows of the families written here in the same act (Phase 394). A manifest that cannot be
    /// restamped fails the command BEFORE any file is written, so the files and their rows never
    /// part company.
    let write (corpusDir: string) : unit =
        let manifest = manifestPath corpusDir

        let restamped =
            if File.Exists manifest then
                match restampManifest (File.ReadAllText manifest) (writtenRows corpusDir) with
                | Ok text -> Some text
                | Error why -> failwithf "%s (%s) — nothing was written" why manifest
            else
                None

        Directory.CreateDirectory(familyDir corpusDir) |> ignore

        for path, text in emitted corpusDir do
            File.WriteAllText(path, text)

        match restamped with
        | Some text when text <> File.ReadAllText manifest -> File.WriteAllText(manifest, text)
        | _ -> ()
