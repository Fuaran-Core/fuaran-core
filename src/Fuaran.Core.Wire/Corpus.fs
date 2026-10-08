namespace Fuaran.Core

/// Conformance-corpus tooling — manifest + round-trip/reject runner + coverage gate,
/// parameterised by a domain's codec. The methodology (not the per-kind cases) is the
/// reusable credibility asset. It only drives the `Codec` — portable, Fable-clean.
module Corpus =

    /// A domain's encode + total decode pair.
    type Codec<'T> =
        {
            /// Value to wire text. Must be total: the runners call it unguarded, so a throw aborts
            /// the whole run rather than failing one case.
            Encode: 'T -> string
            /// Wire text to value. The runners only ask whether it is `Ok` or `Error` — the sentence
            /// is copied into an `Outcome`, never checked.
            Decode: string -> Result<'T, string>
        }

    /// Which law a corpus `Case` is held to.
    type CaseKind =
        /// The JSON must decode, and the decoded value must survive encode-then-decode as an EQUAL
        /// value. The re-encoded text is not compared with the fixture, so a non-canonical fixture
        /// can pass.
        | RoundTrip
        /// The decoder must refuse the JSON. Any `Error` passes, whatever it says; `RejectVector`
        /// pins the code and the path.
        | Reject

    /// One corpus fixture: a `RoundTrip` JSON that must decode→encode→decode to an equal
    /// value, or a `Reject` JSON the decoder must refuse. `Tag` feeds the coverage gate.
    type Case =
        {
            /// The label the case's `Outcome` reports under.
            Name: string
            /// Which law `runCorpus` holds the case to.
            Kind: CaseKind
            /// The fixture text, handed to `Codec.Decode` as it stands.
            Json: string
            /// The kind or op tag the case exercises — counted by `coverageGate`, ignored by `runCorpus`.
            Tag: string
        }

    /// The verdict on one corpus `Case` or one `RejectVector`.
    type Outcome =
        {
            /// The `Case.Name` or `RejectVector.Label` it reports on.
            Name: string
            /// True when the case held its law. A failing case is reported here, never as an `Error`
            /// from the runner.
            Passed: bool
            /// `ok` or `rejected as expected` on a pass; on a failure, why — the decoder's own sentence
            /// where it refused.
            Detail: string
        }

    /// Value-level round-trip: `encode v` must decode back to a structurally-equal value.
    let roundTrip (codec: Codec<'T>) (v: 'T) : Result<unit, string> =
        match codec.Decode(codec.Encode v) with
        | Ok v2 when v2 = v -> Ok()
        | Ok _ -> Error "round-trip produced a different value"
        | Error m -> Error("re-decode failed: " + m)

    let internal runCase (codec: Codec<'T>) (c: Case) : Outcome =
        match c.Kind with
        | RoundTrip ->
            match codec.Decode c.Json with
            | Error m ->
                { Name = c.Name
                  Passed = false
                  Detail = "decode failed: " + m }
            | Ok v ->
                match roundTrip codec v with
                | Ok() ->
                    { Name = c.Name
                      Passed = true
                      Detail = "ok" }
                | Error m ->
                    { Name = c.Name
                      Passed = false
                      Detail = m }
        | Reject ->
            match codec.Decode c.Json with
            | Error _ ->
                { Name = c.Name
                  Passed = true
                  Detail = "rejected as expected" }
            | Ok _ ->
                { Name = c.Name
                  Passed = false
                  Detail = "expected reject but decoded" }

    /// Hold every case to its law through `codec`, one `Outcome` per case in input order. Never
    /// short-circuits: a failing case is an `Outcome` with `Passed = false`.
    let runCorpus (codec: Codec<'T>) (cases: Case list) : Outcome list = cases |> List.map (runCase codec)

    /// Coverage gate: every required kind/op tag must be exercised by at least one case.
    /// Surfaces silent corpus gaps (the forward-coupling discipline).
    let coverageGate (required: string list) (cases: Case list) : Result<unit, string> =
        let seen = cases |> List.map _.Tag |> Set.ofList
        let missing = required |> List.filter (fun t -> not (seen.Contains t))

        if List.isEmpty missing then
            Ok()
        else
            Error("corpus missing coverage for: " + String.concat ", " missing)

    // ---- generative round-trip fuzzing (Phase 18) ----
    // The fixed corpus runs hand-written fixtures; this generates a wide random sample of valid
    // `JVal` and asserts the parser and renderer stay mutually consistent — exactly the depth /
    // escaping / number-format coverage the fixtures cannot enumerate by hand. Self-contained: a
    // tiny uint32 LCG (the same arithmetic class as Conformance's `ConfRng`, inlined because
    // `Fuaran.Core.Wire` takes no dependency on `Fuaran.Core.Conformance`), seed-replayable so a
    // counterexample reproduces. Fable-clean.

    /// The fuzz alphabet, as ATOMS a generated string is a sequence of (Phase 299): ordinary
    /// characters, the two escaped structural characters, EVERY control character U+0000–U+001F
    /// (built, never written raw — a raw NUL in the source would make git treat this file as
    /// binary), a non-ASCII BMP character, and the surrogate classes — a well-formed pair, a lone
    /// high and a lone low (a low atom drawn before a high atom is the ill-ordered class). A string
    /// holding a lone or ill-ordered surrogate is not well-formed UTF-16, and the parser refuses it.
    ///
    /// THE TWO LONE SURROGATES ARE BUILT, like the controls, and never written as `\u` escapes
    /// (Phase 306). Written as literals they were wrong on both pipelines: the F# compiler
    /// replaces an unpaired surrogate escape in a string literal with U+FFFD, so on .NET this
    /// alphabet held two replacement characters and the fuzz never drew an ill-formed string;
    /// and the Fable compiler, which keeps the unit, could not write it into its output file and
    /// failed the compile of this package outright.
    let private fuzzAtoms: string[] =
        Array.append
            [| "a"
               "z"
               "0"
               " "
               "\""
               "\\"
               "/"
               "\u007F"
               "é"
               "😀"
               string (char 0xD800)
               string (char 0xDFFF) |]
            [| for k in 0x00..0x1F -> string (char k) |]

    /// Every string and member key of `v` is well-formed UTF-16 — the values the parser can hand
    /// back (`Json.firstIllFormedString`, the scan the guarded renderers refuse on).
    let private allStringsWellFormed (v: JVal) : bool = (Json.firstIllFormedString v).IsNone

    /// Generate one random valid `JVal` from `seed`, nesting no deeper than `maxDepth`.
    let private genJVal (seed: int) (maxDepth: int) : JVal =
        let mutable st = (uint32 seed * 2654435761u) + 1u

        let next () =
            st <- (st * 1664525u) + 1013904223u
            int (st >>> 1)

        let pick (n: int) = next () % n

        let randStr () =
            let len = pick 6

            Array.init len (fun _ -> fuzzAtoms[pick fuzzAtoms.Length]) |> String.concat ""

        let rec gen (depth: int) : JVal =
            // at the depth limit only scalars are generated (no further nesting)
            match pick (if depth >= maxDepth then 4 else 6) with
            | 0 -> JStr(randStr ())
            | 1 -> JInt(pick 20000 - 10000)
            | 2 -> JBool(pick 2 = 0)
            | 3 ->
                // a finite float (Phase 12 bars non-finite); the +0.25 keeps a fractional part,
                // though the string-idempotence law below tolerates integer-valued floats too.
                (float (pick 10000) + 0.25) * (if pick 2 = 0 then 1.0 else -1.0) |> JFloat
            | 4 -> JArr [ for _ in 0 .. pick 4 -> gen (depth + 1) ]
            | _ -> JObj [ for _ in 0 .. pick 4 -> randStr (), gen (depth + 1) ]

        gen 0

    /// Generative round-trip law: over `count` seed-replayable random `JVal`s, BOTH renderers —
    /// `Json.render` and, since Phase 299, `Canon.render` — must be idempotent under a `parse`
    /// round-trip: `parse (render v) |> Result.map render = Ok (render v)`. The string form is robust
    /// to the documented canonical normalisations (an integer-valued `JFloat` renders without a
    /// point and re-parses as `JInt`; `Canon.render` sorts keys); the rendered text still
    /// round-trips. A value holding a string that is NOT well-formed UTF-16 (a lone or ill-ordered
    /// surrogate — the alphabet draws them) has no string to round-trip to, and there the law is
    /// the refusal: `parse` must reject the rendered text as `BadEscape`, under both renderers.
    /// Returns the first counterexample's seed, renderer and offending output as an `Error`.
    let fuzzRoundTrip (seed: int) (count: int) (maxDepth: int) : Result<unit, string> =
        let check (name: string) (render: JVal -> string) (at: int) (v: JVal) : Result<unit, string> =
            let s = render v

            match Json.parseDetailed s, allStringsWellFormed v with
            | Ok v2, true when render v2 = s -> Ok()
            | Ok v2, true -> Error(sprintf "fuzz seed=%d: %s not idempotent (%s vs %s)" at name s (render v2))
            | Error e, true -> Error(sprintf "fuzz seed=%d: parse rejected %s output %s — %s" at name s e.Message)
            | Error e, false when e.Kind = BadEscape -> Ok()
            | Error e, false ->
                Error(
                    sprintf
                        "fuzz seed=%d: %s output %s carries an ill-formed string and was refused as %A, not BadEscape"
                        at
                        name
                        s
                        e.Kind
                )
            | Ok _, false ->
                Error(sprintf "fuzz seed=%d: %s output %s carries an ill-formed string and was accepted" at name s)

        let rec go i =
            if i >= count then
                Ok()
            else
                let v = genJVal (seed + i) maxDepth

                match check "Json.render" Json.render (seed + i) v with
                | Error m -> Error m
                | Ok() ->
                    match check "Canon.render" Canon.render (seed + i) v with
                    | Error m -> Error m
                    | Ok() -> go (i + 1)

        go 0

    /// Generative codec round-trip law (Phase 20): over `count` seed-replayable values from a
    /// domain's `gen` (seed → `'T`), `decode (encode v)` must reproduce a structurally-equal `'T`
    /// (`roundTrip`, which `'T` equality already backs). Generalises `fuzzRoundTrip` from raw
    /// `JVal` to a domain's own `Codec<'T>`, turning the hand-written corpus into property
    /// coverage and seeding the eval suite. Returns the first counterexample's seed as an `Error`.
    /// Self-contained — the generator is the caller's; no `Conformance` dependency.
    let codecLaws (codec: Codec<'T>) (gen: int -> 'T) (seed: int) (count: int) : Result<unit, string> =
        let rec go i =
            if i >= count then
                Ok()
            else
                match roundTrip codec (gen (seed + i)) with
                | Ok() -> go (i + 1)
                | Error m -> Error(sprintf "codecLaws seed=%d: %s" (seed + i) m)

        go 0

    // ---- refusals carry a code and a path (Phase 310) ----
    // `codecLaws` above is the acceptance half of a codec's laws; these are the refusal half. A
    // reject vector pins the code and the path a refusal must carry, so host twins mirror one error
    // contract rather than one sentence; and the refusal law holds every refusal a decoder raises
    // over a mutated document to a path that resolves in that document.

    /// A reject vector: the document, and the code and path the decoder's refusal must carry.
    type RejectVector =
        {
            /// The label the vector's `Outcome` reports under.
            Label: string
            /// The document text handed to the decoder.
            Input: string
            /// The code the refusal must carry, exactly.
            RefusedAs: DecodeCode
            /// The path the refusal must carry, exactly — root first, `[]` for the root.
            At: PathSegment list
        }

    /// Run reject vectors against a typed decoder over text: each must be refused with exactly its
    /// code and its path.
    let runRejects (decode: string -> Result<'T, DecodeError>) (vectors: RejectVector list) : Outcome list =
        vectors
        |> List.map (fun v ->
            match decode v.Input with
            | Ok _ ->
                { Name = v.Label
                  Passed = false
                  Detail = "expected reject but decoded" }
            | Error e when e.Code = v.RefusedAs && e.Path = v.At ->
                { Name = v.Label
                  Passed = true
                  Detail = "rejected as expected" }
            | Error e ->
                { Name = v.Label
                  Passed = false
                  Detail =
                    "expected "
                    + DecodeError.codeName v.RefusedAs
                    + " at "
                    + DecodePath.render v.At
                    + ", got "
                    + DecodeError.render e })

    /// Structural mutations of a document — the faults the refusal law provokes: every value
    /// replaced by a value of another kind (the root included), every member of every object
    /// removed, and one undeclared member added to every object. Each mutation is the WHOLE document
    /// with one change, so a refusal's path is read against exactly what the decoder saw.
    let mutations (doc: JVal) : JVal list =
        let otherKind (v: JVal) : JVal =
            match v with
            | JStr _ -> JInt 0
            | JInt _
            | JFloat _
            | JBool _ -> JStr "?"
            | JArr _ -> JObj []
            | JObj _ -> JArr []

        let replaceAt (i: int) (x: 'a) (xs: 'a list) : 'a list =
            xs |> List.mapi (fun j y -> if j = i then x else y)

        let rec go (v: JVal) : JVal list =
            let inner =
                match v with
                | JObj fields ->
                    let removed =
                        fields
                        |> List.mapi (fun i _ ->
                            fields
                            |> List.indexed
                            |> List.filter (fun (j, _) -> j <> i)
                            |> List.map snd
                            |> JObj)

                    let added = [ JObj(fields @ [ "$undeclared", JBool true ]) ]

                    let deeper =
                        fields
                        |> List.mapi (fun i (k, x) -> go x |> List.map (fun x2 -> JObj(replaceAt i (k, x2) fields)))
                        |> List.concat

                    removed @ added @ deeper
                | JArr xs ->
                    xs
                    |> List.mapi (fun i x -> go x |> List.map (fun x2 -> JArr(replaceAt i x2 xs)))
                    |> List.concat
                | _ -> []

            otherKind v :: inner

        go doc

    /// The refusal law (Phase 310): over `count` seed-replayable values from `gen`, the decoder
    /// accepts each value's encoding, and every structural mutation of it (`mutations`) that the
    /// decoder REFUSES is refused with a path that resolves in the mutated document
    /// (`DecodeError.resolvesIn`). A refusal whose path names nothing in the document it was raised
    /// over sends a repairing caller nowhere. Returns the first counterexample as an `Error`.
    let refusalLaws
        (encode: 'T -> JVal)
        (decode: Decoder<'T>)
        (gen: int -> 'T)
        (seed: int)
        (count: int)
        : Result<unit, string> =
        let rec go i =
            if i >= count then
                Ok()
            else
                let doc = encode (gen (seed + i))

                match decode doc with
                | Error e ->
                    Error(
                        sprintf
                            "refusalLaws seed=%d: the encoding itself was refused: %s"
                            (seed + i)
                            (DecodeError.render e)
                    )
                | Ok _ ->
                    let unresolved =
                        mutations doc
                        |> List.tryPick (fun m ->
                            match decode m with
                            | Error e when not (DecodeError.resolvesIn m e) -> Some(m, e)
                            | _ -> None)

                    match unresolved with
                    | Some(m, e) ->
                        Error(
                            sprintf
                                "refusalLaws seed=%d: the refusal %s does not resolve in %s"
                                (seed + i)
                                (DecodeError.render e)
                                (Json.render m)
                        )
                    | None -> go (i + 1)

        go 0
