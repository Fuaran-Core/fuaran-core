namespace Fuaran.Core.Tests

// ============================================================================
//  The host-neutral export of the law vectors this repository owns: the
//  `capabilityLaws` vectors (`laws/capability-laws.json`, Phase 235) and the
//  exact decimal's documents (`laws/decimal-laws.json`, Phase 276).
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
                        Kind = "value"
                        Space = Some(IntRange(lo, hi))
                        Slot = None
                        Action = None
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
                    Registry.empty
                    |> Registry.register d.Cap
                    |> Result.bind (Registry.register d.CapB)
                with
                | Ok r -> Registry.enumerate r |> List.map (fun c -> c.Id)
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

        let source (cells: Cell list) : DataSource =
            Embedded
                { Schema = [ "c", DecimalType ]
                  Columns = [ Column.create "c" DecimalType cells ] }

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
                      match ColumnCodec.tryEncode (source cells) with
                      | Ok text -> accept [ "canonical", jstr text ]
                      | Error e -> reject (columnErrorClass e) }
              for name, fn, cells in aggregateInputs ->
                  { Id = "aggregate-" + name
                    Case = "aggregate"
                    Input = [ "fn", jstr (aggFnTag fn); "cells", jarr (cells |> List.map cellJson) ]
                    Expected =
                      match Column.aggregate fn (Column.create "c" DecimalType cells) with
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

    let emitted (corpusDir: string) : (string * string) list =
        [ capabilityPath corpusDir, Capabilities.render ()
          decimalPath corpusDir, Decimals.render () ]

    /// Write the vectors with LF endings, whatever the host platform — the corpus is byte-compared
    /// by several hosts on three operating systems.
    ///
    /// The family MANIFEST beside them is deliberately not written here. It indexes every family in
    /// `laws/`, and a wholesale renderer would silently drop whatever it does not know about.
    /// Moving a family between its `families` and `notExported` lists is an edit to a shared index,
    /// made once.
    let write (corpusDir: string) : unit =
        Directory.CreateDirectory(familyDir corpusDir) |> ignore

        for path, text in emitted corpusDir do
            File.WriteAllText(path, text)
