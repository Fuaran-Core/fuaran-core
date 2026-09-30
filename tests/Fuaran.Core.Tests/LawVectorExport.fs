namespace Fuaran.Core.Tests

// ============================================================================
//  The host-neutral export of the law vectors this repository owns: today the
//  `capabilityLaws` vectors (`laws/capability-laws.json`, Phase 235).
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
    //  writing
    // -----------------------------------------------------------------------

    let familyDir (corpusDir: string) : string = Path.Combine(corpusDir, familyDirName)

    let capabilityPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, Capabilities.fileName)

    /// Every law set this repository emits, as (path under `corpusDir`, rendered bytes) — the one
    /// list `write` walks, so a family added here cannot be rendered and then forgotten by the
    /// writer.
    let emitted (corpusDir: string) : (string * string) list =
        [ capabilityPath corpusDir, Capabilities.render () ]

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
