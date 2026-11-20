namespace Fuaran.Core.Tests

// ============================================================================
//  The host-neutral export of the law vectors this repository owns: the
//  `capabilityLaws` vectors (`laws/capability-laws.json`, Phase 235), the
//  exact decimal's documents (`laws/decimal-laws.json`, Phase 276), the
//  schema field's documents (`laws/field-laws.json`, Phase 427) and the
//  propagation vectors (`laws/propagation-laws.json`, Phase 538, rendered by
//  `PropagationVectorExport`), the capability gate family
//  (`laws/capability-gate-laws.json`, Phase 650), the document family
//  (`laws/capability-document-laws.json`, Phase 644), the query grant
//  family (`laws/query-grant-laws.json`, Phase 651) and the query
//  declaration family (`laws/query-declaration-laws.json`, Phase 680).
//
//  Until Phase 258 this module also exported `Conformance.transformLaws`'
//  reference answers (`laws/transform-laws.json`, fuaran#1479). Those answers
//  are what the dataframe reference evaluator says, and that evaluator left
//  this repository with the rest of the compute strand (DECISIONS.md D66), so
//  the file is now emitted, stamped and published by the repository that
//  produces the evaluator — https://github.com/Fuaran-Core/fuaran-core-compute
//  — from `0.33.0`, under the same name, with the same shape and the same
//  sample. The JSON renderer and the vector record below are the ones that
//  file was written with, so the two artefacts keep one framing.
// ============================================================================

module LawVectorExport =

    // The extracted query and capability models, named before `Fuaran.Core` is opened: production's
    // `Query` and `Capability` modules share their names (the query declaration family asks them).
    module ModelQuery = Query
    module ModelCap = Capability

    open System.IO
    open System.Reflection
    open System.Text
    open Fuaran.Core

    /// The family directory inside the shared corpus. The directory name is the interface — hosts
    /// resolve `laws/` — so it is named once here.
    let familyDirName = "laws"

    // -----------------------------------------------------------------------
    //  the cells the files are written in
    // -----------------------------------------------------------------------
    //  These files are ORACLES, so their bytes are the interface five hosts read. Since Phase 508 they
    //  are written by the one writer every conformance file goes through, `CorpusFile.render`, which
    //  escapes through `Canonical.write Rendering.CrossHost` — the spine's one string rule — and lays a file out a header
    //  member and an item per line, LF endings whatever the platform. The hand-rolled renderer these
    //  files were written with before it spelled the short escapes (`\n`, `\t`, …) where the spine
    //  spells `\u000a`; no committed vector carries a control character, so the move changed no byte,
    //  and the file-for-file comparison against the committed corpus is what says so. The four
    //  helpers below build cells and write nothing.

    let private jstr (s: string) : CorpusCell = CorpusFile.str s

    let private jint (n: int) : CorpusCell = CorpusFile.num n

    let private jobj (members: (string * CorpusCell) list) : CorpusCell = CorpusCell.Row members

    let private jarr (items: CorpusCell list) : CorpusCell = CorpusCell.List items

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

    /// A single exported vector: what a host is given, what the reference actually answered, and —
    /// where an extracted model asks the same question — the model's answer to the same input, which
    /// the render holds equal to `Expected` before it stamps the vector (Phase 478).
    type internal Vector =
        { Id: string
          Case: string
          Input: (string * CorpusCell) list
          Expected: (string * CorpusCell) list
          Model: VectorOracle.Answer option }

    let private renderVector (v: Vector) : CorpusCell =
        jobj
            [ "id", jstr v.Id
              "case", jstr v.Case
              "input", jobj v.Input
              "expected", jobj v.Expected
              VectorOracle.stamp v.Id (jobj v.Expected) v.Model ]

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

                  // Phase 642: the declared result alternates as `capabilityLaws`' does — the captured
                  // value's space on an even iteration, nothing on an odd one — so the file carries both
                  // spellings of the `result` member.
                  let result =
                      if i % 2 = 0 then
                          Returns.Value(IntRange(0, 999))
                      else
                          Returns.Nothing

                  yield
                      { Iteration = i
                        Lo = lo
                        Hi = hi
                        Realized = realized
                        Cap = Capability.create ("cap-" + string i) sg (ClientIsland Pyodide) result
                        CapB = Capability.create ("cap-a" + string i) sg BuildTime result } ]

        let private argsJson (args: (string * string) list) : CorpusCell =
            args
            |> List.map (fun (name, value) -> jobj [ "name", jstr name; "value", jstr value ])
            |> jarr

        /// A verdict in host-neutral words — the reference's or the model's, through the one shape
        /// (`CapabilityOracle.Verdict`). A refusal outside the two the law distinguishes renders as
        /// `unexpected`; `LawVectorTests` fails on it, so it can never reach the corpus.
        let private verdictCells (v: CapabilityOracle.Verdict) : (string * CorpusCell) list =
            match v with
            | CapabilityOracle.Verdict.Accept -> [ "verdict", jstr "accept" ]
            | CapabilityOracle.Verdict.OutOfSpace name ->
                [ "verdict", jstr "reject"; "error", jstr "argOutOfSpace"; "name", jstr name ]
            | CapabilityOracle.Verdict.UnknownArg name ->
                [ "verdict", jstr "reject"; "error", jstr "unknownArg"; "name", jstr name ]
            | CapabilityOracle.Verdict.Unexpected -> [ "verdict", jstr "reject"; "error", jstr "unexpected" ]

        /// One `validateArgs` vector: the reference's verdict, and the model's on the same input.
        let private validates
            (id: string)
            (cap: Capability)
            (declaration: string)
            (args: (string * string) list)
            : Vector =
            { Id = id
              Case = "validateArgs"
              Model = CapabilityOracle.answer (verdictCells (CapabilityOracle.validateArgs cap args))
              Input = [ "capability", jstr declaration; "args", argsJson args ]
              Expected = verdictCells (CapabilityOracle.verdictOf (Capability.validateArgs cap args)) }

        /// Every vector for one drawn iteration — the four properties `capabilityLaws` certifies,
        /// each as an independently runnable case.
        let internal vectorsFor (d: Draw) : Vector list =
            let inSpace = string d.Lo
            let outOfSpace = string (d.Hi + 1)
            let declaration = CapabilityCodec.encode d.Cap
            let acceptArgs = [ "x", inSpace ]

            let registryIds =
                match
                    CapabilityRegistry.empty
                    |> CapabilityRegistry.register d.Cap
                    |> Result.bind (CapabilityRegistry.register d.CapB)
                with
                | Ok r -> CapabilityRegistry.enumerate r |> List.map _.Id
                | Error _ -> []

            [ validates (sprintf "capability-%d-accept" d.Iteration) d.Cap declaration acceptArgs
              validates (sprintf "capability-%d-out-of-space" d.Iteration) d.Cap declaration [ "x", outOfSpace ]
              // Phase 568 — each bound of the range is pinned by a pair, one at it and one past it (the
              // D226 shape): `accept` sits at the lower bound and `out-of-space` past the upper, and these
              // two are the other halves, so a host one unit off either bound, early or late, fails one.
              validates (sprintf "capability-%d-at-upper-bound" d.Iteration) d.Cap declaration [ "x", string d.Hi ]
              validates
                  (sprintf "capability-%d-past-lower-bound" d.Iteration)
                  d.Cap
                  declaration
                  [ "x", string (d.Lo - 1) ]
              validates (sprintf "capability-%d-unknown-arg" d.Iteration) d.Cap declaration [ "nope", inSpace ]

              // The captured value is the draw itself: neither side computes it, and both carry it.
              { Id = sprintf "capability-%d-invocation-key" d.Iteration
                Case = "invocationKey"
                Model =
                  CapabilityOracle.answer
                      [ "key", jstr (CapabilityOracle.invocationKey d.Cap acceptArgs)
                        "determinismTag", jstr (CapabilityOracle.determinismTag d.Cap)
                        "capturedValue", jint d.Realized ]
                Input = [ "capability", jstr declaration; "args", argsJson acceptArgs ]
                Expected =
                  [ "key", jstr (Capability.invocationKey d.Cap acceptArgs)
                    "determinismTag", jstr (Capability.determinismTag d.Cap)
                    "capturedValue", jint d.Realized ] }

              // No model answers this: the model has the declaration's encoder and no decoder.
              { Id = sprintf "capability-%d-declaration-round-trip" d.Iteration
                Case = "declarationRoundTrip"
                Model = None
                Input = [ "declaration", jstr declaration ]
                Expected =
                  [ "declaration",
                    jstr (
                        match CapabilityCodec.decode declaration with
                        | Ok c -> CapabilityCodec.encode c
                        | Error m -> "DECODE FAILED: " + m
                    ) ] }

              // No model answers this: the model's `enumerate` answers which ids are registered, not
              // the id order the vector pins (`proofs/Capability.fst`, `enumerate`).
              { Id = sprintf "capability-%d-registry-enumerate" d.Iteration
                Case = "registryEnumerate"
                Model = None
                Input = [ "declarations", jarr [ jstr declaration; jstr (CapabilityCodec.encode d.CapB) ] ]
                Expected = [ "ids", jarr (registryIds |> List.map jstr) ] } ]

        /// The declaration with its `result` member replaced (`Some`) or removed (`None`), re-rendered
        /// canonically.
        let private reResulted (declaration: string) (result: JVal option) : string =
            match Json.parse declaration with
            | Ok(JObj fs) ->
                let fs' =
                    fs
                    |> List.choose (fun (k, v) ->
                        if k <> "result" then
                            Some(k, v)
                        else
                            result |> Option.map (fun r -> k, r))

                Canonical.write Rendering.CrossHost (JObj fs')
            | other -> failwithf "not a declaration: %A" other

        /// A refusal's cells: its code and its path, each segment as written.
        let internal refusalCells (code: DecodeCode) (path: string list) : (string * CorpusCell) list =
            [ "verdict", jstr "reject"
              "code", jstr (string code)
              "path", jarr (path |> List.map jstr) ]

        let internal segText (s: PathSegment) =
            match s with
            | PathSegment.Key k -> k
            | PathSegment.Index i -> string i

        /// One `declarationRefused` vector (Phase 642): the reference reader's refusal of a declaration,
        /// and the model's (`capability_of_j`) on the same text.
        let private refusedDeclaration (id: string) (declaration: string) : Vector =
            let expected =
                match CapabilityCodec.decodeDetailedWith ReadPolicy.Lenient declaration with
                | Error e -> refusalCells e.Code (e.Path |> List.map segText)
                | Ok _ -> [ "verdict", jstr "accept" ]

            let model =
                match CapabilityOracle.declarationRefusal declaration with
                | Some(code, path) -> refusalCells code path
                | None -> [ "verdict", jstr "accept" ]

            { Id = id
              Case = "declarationRefused"
              Model = CapabilityOracle.answer model
              Input = [ "declaration", jstr declaration ]
              Expected = expected }

        /// One `pipelineTypeCheck` vector (Phase 642): a one-node pipeline calling the drawn capability
        /// with its declared output, type-checked against a registry holding the capability alone. No
        /// model is stamped here: the model's check runs beside production in the pipeline
        /// differential (`capability-sections-differential`), which carries its float readers.
        let private pipelineCheck
            (id: string)
            (cap: Capability)
            (args: (string * ArgSource) list)
            (declared: ValueSpace)
            : Vector =
            let pipeline = { Nodes = [ Invoke("call", cap.Id, declared, args) ] }

            let lookup =
                match CapabilityRegistry.register cap CapabilityRegistry.empty with
                | Ok r -> CapabilityLookup.ofRegistry r
                | Error e -> failwithf "the drawn capability was refused: %A" e

            let expected =
                match CapabilityPipeline.typeCheck lookup pipeline with
                | Ok() -> [ "verdict", jstr "accept" ]
                | Error(PipelineError.PipelineOutputContradicts(node, cap, _, _)) ->
                    [ "verdict", jstr "reject"
                      "error", jstr "pipelineOutputContradicts"
                      "node", jstr node
                      "capability", jstr cap ]
                | Error _ -> [ "verdict", jstr "reject"; "error", jstr "unexpected" ]

            { Id = id
              Case = "pipelineTypeCheck"
              Model = None
              Input =
                [ "capability", jstr (CapabilityCodec.encode cap)
                  "pipeline", jstr (CapabilityPipeline.encode pipeline) ]
              Expected = expected }

        /// The Phase 642 vectors for one draw: the two new refusals, each beside its acceptance.
        let internal resultVectorsFor (d: Draw) : Vector list =
            let declaration = CapabilityCodec.encode d.Cap
            let x (d: Draw) = [ "x", Literal(string d.Lo) ]

            [ refusedDeclaration (sprintf "capability-%d-without-result" d.Iteration) (reResulted declaration None)
              refusedDeclaration
                  (sprintf "capability-%d-empty-result" d.Iteration)
                  (reResulted declaration (Some(SpaceCodec.toJson (Enum []))))
              pipelineCheck (sprintf "capability-%d-pipeline-output-admits" d.Iteration) d.Cap (x d) AnyString
              pipelineCheck
                  (sprintf "capability-%d-pipeline-output-narrower" d.Iteration)
                  d.Cap
                  (x d)
                  (IntRange(d.Lo, d.Hi)) ]

        /// The capability the document vectors are stated over (Phase 644): one required hole in the
        /// document space of a named type, answering a document of another. It is fixed, not drawn: a
        /// document space has no bound to draw, and what the vectors pin is the membership rule, the
        /// one spelling a document is keyed by, and the family's place in the relation.
        let documentCap: Capability =
            Capability.create
                "cap-document"
                { Name = "capDocument"
                  Holes =
                    [ { Addr = "d0"
                        Name = "order"
                        Kind = ValueHole(Document(Some "Order"))
                        Required = true } ]
                  Effect =
                    { Host = ReadsHost
                      Determinism = Effect.clock } }
                Server
                (Returns.Value(Document(Some "Receipt")))

        /// One document in its canonical text, and the same document spelled a second way: members
        /// out of order, white space, a float with a trailing zero, a control character by its short
        /// escape.
        let documentCanonical = "{\"a\":1,\"b\":[true,\"x\\u000ay\",2.5]}"

        let documentRespelled = "{ \"b\" : [ true, \"x\\ny\", 2.50 ],\n  \"a\" : 1 }"

        /// The document vectors (Phase 644), through the cases a host already replays: membership by
        /// shape (`validateArgs`), one key for two spellings of one document (`invocationKey`), the
        /// declaration's bytes (`declarationRoundTrip`) and the family's relation (`pipelineTypeCheck`).
        /// They are built here, beside the helpers they share with the drawn vectors, and published as
        /// a family of their own (`Documents`): a host that reads `capability-laws.json` today refuses
        /// a value space it does not know, so a section of that file would turn it red (D255.1).
        let internal documentVectors () : Vector list =
            let cap = documentCap
            let declaration = CapabilityCodec.encode cap
            let order (value: string) = [ "order", value ]

            let key (id: string) (args: (string * string) list) : Vector =
                { Id = id
                  Case = "invocationKey"
                  Model =
                    CapabilityOracle.answer
                        [ "key", jstr (CapabilityOracle.invocationKey cap args)
                          "determinismTag", jstr (CapabilityOracle.determinismTag cap) ]
                  Input = [ "capability", jstr declaration; "args", argsJson args ]
                  Expected =
                    [ "key", jstr (Capability.invocationKey cap args)
                      "determinismTag", jstr (Capability.determinismTag cap) ] }

            let unconstrained =
                CapabilityCodec.encode
                    { cap with
                        Id = "cap-document-any"
                        Result = Returns.Value(Document None) }

            let roundTrip (id: string) (text: string) : Vector =
                { Id = id
                  Case = "declarationRoundTrip"
                  Model = None
                  Input = [ "declaration", jstr text ]
                  Expected =
                    [ "declaration",
                      jstr (
                          match CapabilityCodec.decode text with
                          | Ok c -> CapabilityCodec.encode c
                          | Error m -> "DECODE FAILED: " + m
                      ) ] }

            let call = [ "order", Literal documentCanonical ]

            [ validates "document-accept-canonical" cap declaration (order documentCanonical)
              validates "document-accept-respelled" cap declaration (order documentRespelled)
              validates "document-accept-list" cap declaration (order "[1,2,3]")
              validates "document-accept-scalar" cap declaration (order "\"text\"")
              validates "document-reject-cut-short" cap declaration (order "{\"a\":1")
              validates "document-reject-repeated-member" cap declaration (order "{\"a\":1,\"a\":2}")
              validates "document-reject-null" cap declaration (order "null")
              validates "document-reject-bare-word" cap declaration (order "order")
              validates "document-unknown-arg" cap declaration [ "nope", documentCanonical ]
              key "document-key-canonical" (order documentCanonical)
              key "document-key-respelled" (order documentRespelled)
              roundTrip "document-declaration-round-trip" declaration
              roundTrip "document-declaration-round-trip-unconstrained" unconstrained
              pipelineCheck "document-pipeline-output-same-type" cap call (Document(Some "Receipt"))
              pipelineCheck "document-pipeline-output-unconstrained" cap call (Document None)
              pipelineCheck "document-pipeline-output-other-type" cap call (Document(Some "Order"))
              pipelineCheck "document-pipeline-output-scalar" cap call AnyString
              pipelineCheck "document-pipeline-output-tree" cap call (SlotTree None) ]

        let internal allVectors () : Vector list =
            draws () |> List.collect (fun d -> vectorsFor d @ resultVectorsFor d)

        let private description =
            "The (input, expected) pairs Fuaran.Core.Conformance.capabilityLaws draws from `seed` over "
            + "`iterations` iterations, computed by calling the pinned kit. A host reproduces the sample with its own "
            + "ConfRng: per iteration draw intBelow(50) = lo, intBelow(50) = span (hi = lo + span + 1), intBelow(1000) = "
            + "the captured value, and build one capability per iteration over a single required value hole at address `h0` named `x` in "
            + "IntRange(lo, hi) with effect readsHost and the determinism set of the (i mod 7)th non-empty subset of {clock, random, network} in mask order (mask 1..7, bit 0 clock, bit 1 random, bit 2 network; the label names the members in that order joined by `+`). `capability` and `declaration` members carry a canonical "
            + "capability declaration as a JSON STRING — decode it with the host's capability codec. Each argument is `{\"name\", \"value\"}`, keyed by the "
            + "qualified name of the hole it binds (Phase 712, DECISIONS.md D281), never by the hole's address: the vectors bind `x`, and the address `h0` "
            + "binds nothing. `validateArgs` "
            + "vectors expect accept, or reject with a named error class and the `name` of the offending argument; each bound of the range is pinned at it and one past it (`accept` at lo, `at-upper-bound` at hi, `past-lower-bound` at lo - 1, `out-of-space` at hi + 1: the range is inclusive at both ends). `invocationKey` "
            + "vectors expect the effect-identity key a non-deterministic invocation is journalled under, its "
            + "determinism tag, and the value the replay must return byte-identically. `declarationRoundTrip` expects "
            + "decode-then-encode to return the input bytes. `registryEnumerate` expects id-sorted enumeration "
            + "regardless of insertion order (the declarations are given in insertion order). Since Phase 642 every "
            + "capability declares its `result`: IntRange(0, 999) on an even iteration and `{\"$type\":\"nothing\"}` on an "
            + "odd one. `declarationRefused` vectors expect the declaration reader to reject with a decode `code` and "
            + "the `path` to the fault: a declaration without `result` is MissingField at [result], and one whose result "
            + "space is empty is OutOfRange at [result]. `pipelineTypeCheck` vectors register the capability alone and "
            + "type-check the given one-node pipeline: accept, or reject `pipelineOutputContradicts` naming the node "
            + "and the capability when the node's declared output does not admit the capability's result (or the "
            + "capability answers nothing)."

        /// Rendered with an explicit `kitVersion` so the byte-identity pin can render the file at the
        /// version a published copy was stamped with; `render ()` stamps this kit's own version.
        let renderAt (stamp: string) : string =
            CorpusFile.render
                { Head =
                    [ "family", jstr "capabilityLaws"
                      "kitVersion", jstr stamp
                      "seed", jint seed
                      "iterations", jint iterations
                      "description", jstr description ]
                  Items = "vectors"
                  Rows = allVectors () |> List.map renderVector }

        let render () : string = renderAt (kitVersion ())

    // -----------------------------------------------------------------------
    //  the capability document family (Phase 644)
    // -----------------------------------------------------------------------
    //  The document value space (DECISIONS.md D260) as vectors a port replays with the cases it
    //  already has for `capability-laws.json`. A file of its own, in this repository's corpus, for
    //  D255.1's reason: the hosts that read `capability-laws.json` decode every declaration in it and
    //  refuse a value space they do not know, and each pins that file's vector count.

    module Documents =

        let fileName = "capability-document-laws.json"

        let familyId = "capabilityDocumentLaws"

        let internal allVectors () : Vector list = Capabilities.documentVectors ()

        let private description =
            "The document value space (Phase 644, DECISIONS.md D260), through the cases of capability-laws.json: `validateArgs`, `invocationKey`, "
            + "`declarationRoundTrip` and `pipelineTypeCheck`, each read exactly as that file describes it. Authored inputs, each `expected` computed by calling "
            + "the pinned kit; no seed. Every vector is stated over one capability, `cap-document`: a single required hole at address `d0` named `order` in the document space of type "
            + "`Order`, answering a document of type `Receipt`. `capability` and `declaration` members carry a canonical capability declaration as a JSON STRING, "
            + "in which a document space is `{\"$type\":\"document\"}` with an optional `documentType`. A document argument is a member exactly when it is "
            + "well-formed JSON with a canonical rendering of its own: it parses with no `null`, and holds no repeated member, no ill-formed string and no number a "
            + "finite float cannot spell. The type name is carried and never read. An argument is keyed by its hole's qualified name, `order`, never by the address `d0` (Phase 712, DECISIONS.md D281). An argument that is no document is `argOutOfSpace` naming it. The two "
            + "`document-key-*` vectors present ONE document in two spellings (members out of order, white space, a float with a trailing zero, a control character "
            + "by its short escape) and expect one key: an argument in a document space is keyed by its cross-host canonical text (members sorted, no white space, "
            + "the canonical float layout, every control character as `\\u00xx`). They carry no `capturedValue`. The `document-pipeline-output-*` vectors pin the "
            + "space relation: a document result is admitted by a declared output of the same type or of no type, and by no other document type, no scalar space and "
            + "no tree space."

        /// Rendered with an explicit `kitVersion`, as every law file is (Phase 560).
        let renderAt (stamp: string) : string =
            CorpusFile.render
                { Head =
                    [ "family", jstr familyId
                      "kitVersion", jstr stamp
                      "description", jstr description ]
                  Items = "vectors"
                  Rows = allVectors () |> List.map renderVector }

        let render () : string = renderAt (kitVersion ())

    // -----------------------------------------------------------------------
    //  the capability gate family (Phase 650)
    // -----------------------------------------------------------------------
    //  Phase 638 gave a gate a decision context and let a dispatch present a grant (DECISIONS.md
    //  D249), and its laws hold in the conformance package; none of it was in the emitted vectors, so a
    //  port had nothing to certify the grant against. This family emits it, in a file of its own
    //  (D255.1): every host that reads `capability-laws.json` today fails on a `case` it does not know,
    //  so a section of that file would turn them red.
    //
    //  A gate is a function and a vector is data, so the family DECLARES its gates: a table a port reads
    //  and decides from. A gate answers its rule's decision for a (context, capability) pair it has a
    //  rule for, and `allow` for every other; the registry joins the gates in order. The contexts are
    //  two opaque labels, and the registry is three capabilities of the kit's own, over an integer, a
    //  number and a string hole. Every `expected` is computed by CALLING the kit: the identity by
    //  `Capability.invocationPreimage`, a decision by `CapabilityRegistry.decideNamed`, a dispatch by
    //  `CapabilityRegistry.dispatch` with a performer that records whether it ran. No seed: nothing is
    //  drawn.

    module Gates =

        let fileName = "capability-gate-laws.json"

        let familyId = "capabilityGateLaws"

        let private hole (addr: string) (space: ValueSpace) : SigEntry =
            { Addr = addr
              Name = addr
              Kind = ValueHole space
              Required = true }

        let private cap (id: string) (holes: SigEntry list) (result: Returns) : Capability =
            Capability.create
                id
                { Name = id
                  Holes = holes
                  Effect = Effect.pureDeterministic }
                Server
                result

        /// The registry's three capabilities, in id order.
        let capabilities: Capability list =
            [ cap "erase" [ hole "n" (IntRange(0, 99)) ] Returns.Nothing
              cap "read" [ hole "n" (IntRange(0, 99)) ] (Returns.Value(IntRange(0, 99)))
              cap
                  "write"
                  [ hole "n" (IntRange(0, 99))
                    hole "x" (FloatRange(0.0, 10.0))
                    hole "s" AnyString ]
                  Returns.Nothing ]

        let private capOf (id: string) =
            capabilities |> List.find (fun c -> c.Id = id)

        /// The two decision contexts, as the opaque labels the gate table names.
        let contexts = [ "trusted"; "untrusted" ]

        /// One row of a gate's table: the decision it gives for one context and one capability.
        type Rule =
            { Context: string
              Capability: string
              Decision: PolicyDecision }

        /// The gate table, in the order the registry runs the gates. Chosen so that every decision
        /// appears under each context, a later gate of a higher rank takes the name (`erase`, trusted),
        /// and two gates of one rank leave it with the first (`write`, untrusted).
        let gates: (string * Rule list) list =
            let rule ctx capability decision =
                { Context = ctx
                  Capability = capability
                  Decision = decision }

            [ "ceiling",
              [ rule "untrusted" "write" PolicyDecision.NeedsApproval
                rule "untrusted" "erase" (PolicyDecision.denyWith "erasing is not open to this context" [ "read" ])
                rule "trusted" "erase" PolicyDecision.NeedsApproval ]
              "audit",
              [ rule "untrusted" "write" PolicyDecision.NeedsApproval
                rule "untrusted" "erase" PolicyDecision.NeedsApproval
                rule "trusted" "write" PolicyDecision.NeedsApproval
                rule "trusted" "erase" (PolicyDecision.deny "an erase is held for the audit") ] ]

        /// The production registry the table declares: the three capabilities, one gate per table, each
        /// deciding its rule for the context and the capability and `Allow` otherwise.
        let registry: CapabilityRegistry<string> =
            let registered =
                capabilities
                |> List.fold (fun r c -> r |> Result.bind (CapabilityRegistry.register c)) (Ok CapabilityRegistry.empty)
                |> Result.defaultWith (fun e -> failwithf "the gate family's registry did not build: %A" e)

            gates
            |> List.fold
                (fun r (policy, rules) ->
                    r
                    |> CapabilityRegistry.withGate
                        { Policy = policy
                          Decide =
                            fun ctx c _ ->
                                rules
                                |> List.tryFind (fun x -> x.Context = ctx && x.Capability = c.Id)
                                |> Option.map _.Decision
                                |> Option.defaultValue PolicyDecision.Allow })
                registered

        let private decisionCells (d: PolicyDecision) : (string * CorpusCell) list =
            match d with
            | PolicyDecision.Allow -> [ "decision", jstr "allow" ]
            | PolicyDecision.NeedsApproval -> [ "decision", jstr "needsApproval" ]
            | PolicyDecision.Deny g ->
                [ "decision", jstr "deny"
                  "message", jstr g.Message
                  "alternatives", jarr (g.Alternatives |> List.map jstr) ]

        let private argsJson (args: (string * string) list) : CorpusCell =
            args
            |> List.map (fun (name, value) -> jobj [ "name", jstr name; "value", jstr value ])
            |> jarr

        /// The `grant` member: the identity a presented grant carries, and no member when none is
        /// presented (the wire carries no null). The query families present a grant the same way.
        let internal grantMember (grant: Grant option) : (string * CorpusCell) list =
            match grant with
            | Some g -> [ "grant", jstr g.Invocation ]
            | None -> []

        /// The refusals a named read or a dispatch answers, in the seam codec's member names.
        let private refusalCells (e: InvokeError) : (string * CorpusCell) list =
            match e with
            | NoSuchCapability(id, known) ->
                [ "error", jstr "noSuchCapability"
                  "id", jstr id
                  "known", jarr (known |> List.map jstr) ]
            | ArgOutOfSpace(name, _, _) -> [ "error", jstr "argOutOfSpace"; "name", jstr name ]
            | PolicyRefused(policy, reason, allowed) ->
                [ "error", jstr "policyRefused"
                  "policy", jstr policy
                  "reason", jstr reason
                  "allowed", jarr (allowed |> List.map jstr) ]
            | ApprovalRequired policy -> [ "error", jstr "approvalRequired"; "policy", jstr policy ]
            | other -> [ "error", jstr "unexpected"; "detail", jstr (InvokeError.describe other) ]

        // ---- the identity (t2): model-stamped, `invocation_identity` answers the whole of it ----

        let private identity (id: string) (capability: string) (args: (string * string) list) : Vector =
            let c = capOf capability
            let reference = Capability.invocationPreimage c args

            { Id = id
              Case = "invocationIdentity"
              Model = CapabilityOracle.answer [ "identity", jstr (CapabilityOracle.invocationIdentity c args) ]
              Input = [ "capability", jstr capability; "args", argsJson args ]
              Expected = [ "identity", jstr reference ] }

        /// The arguments every decision and dispatch vector presents, per capability.
        let argsFor (capability: string) : (string * string) list =
            match capability with
            | "write" -> [ "n", "7"; "x", "1.5"; "s", "a" ]
            | _ -> [ "n", "3" ]

        /// The same invocation with its integer binding moved by one: another invocation.
        let otherArgsFor (capability: string) : (string * string) list =
            argsFor capability
            |> List.map (fun (a, v) -> if a = "n" then a, string (int v + 1) else a, v)

        let internal identityVectors () : Vector list =
            let w = argsFor "write"

            [ identity "gate-identity-write" "write" w
              identity "gate-identity-integer-spelling" "write" [ "n", "07"; "x", "1.5"; "s", "a" ]
              identity "gate-identity-number-spelling" "write" [ "n", "7"; "x", "1.50"; "s", "a" ]
              identity "gate-identity-out-of-name-order" "write" [ "s", "a"; "x", "1.5"; "n", "7" ]
              identity "gate-identity-holds-the-separator" "write" [ "n", "7"; "x", "1.5"; "s", "a\u0001b" ]
              identity "gate-identity-holds-the-escape" "write" [ "n", "7"; "x", "1.5"; "s", "a\u0010\u0001" ]
              identity "gate-identity-one-binding-differs" "write" (otherArgsFor "write")
              identity "gate-identity-read" "read" (argsFor "read")
              identity "gate-identity-other-capability" "erase" (argsFor "erase") ]

        // ---- the grant (t3/t4): model-stamped, `granted` answers the whole of it ----

        let private covers (id: string) (capability: string) (args: (string * string) list) (grant: Grant option) =
            let c = capOf capability

            { Id = id
              Case = "grantCovers"
              Model =
                CapabilityOracle.answer
                    [ "covers",
                      CorpusCell.Value(JBool(CapabilityOracle.granted c args (grant |> Option.map _.Invocation))) ]
              Input = [ "capability", jstr capability; "args", argsJson args ] @ grantMember grant
              Expected =
                [ "covers",
                  CorpusCell.Value(JBool(Grant.covers (Capability.invocationPreimage c args) (Option.toList grant))) ] }

        let internal grantVectors () : Vector list =
            let w = argsFor "write"
            let own = Capability.grant (capOf "write") w

            [ covers "gate-grant-own" "write" w (Some own)
              // the same invocation, spelled otherwise: the grant minted for it covers it
              covers "gate-grant-own-other-spelling" "write" [ "n", "07"; "x", "1.50"; "s", "a" ] (Some own)
              covers
                  "gate-grant-other-arguments"
                  "write"
                  w
                  (Some(Capability.grant (capOf "write") (otherArgsFor "write")))
              covers
                  "gate-grant-other-capability"
                  "read"
                  (argsFor "read")
                  (Some(Capability.grant (capOf "erase") (argsFor "erase")))
              covers "gate-grant-none" "write" w None ]

        // ---- the decision and the dispatch (t3): reference-only ----
        //  The model states the gates' join (`decide_named`), the lift (`grant_lifted`) and the gated
        //  dispatch (`dispatch_gated`) and proves them, but none is extracted, so no oracle function can
        //  answer these vectors; they are declared reference-only in the file's head with that reason.

        /// The three grants a decision is asked under: none, the grant for this invocation, and a grant
        /// for another invocation of the same capability.
        let private grantsFor (capability: string) : (string * Grant option) list =
            let c = capOf capability

            [ "none", None
              "own", Some(Capability.grant c (argsFor capability))
              "other", Some(Capability.grant c (otherArgsFor capability)) ]

        let private namedCells (named: Result<string * PolicyDecision, InvokeError>) : (string * CorpusCell) list =
            match named with
            | Ok(policy, d) -> [ "verdict", jstr "decided"; "policy", jstr policy ] @ decisionCells d
            | Error e -> ("verdict", jstr "refused") :: refusalCells e

        let private decision
            (id: string)
            (ctx: string)
            (capability: string)
            (args: (string * string) list)
            (grant: Grant option)
            : Vector =
            { Id = id
              Case = "decideNamed"
              Model = None
              Input =
                [ "context", jstr ctx; "capability", jstr capability; "args", argsJson args ]
                @ grantMember grant
              Expected = namedCells (CapabilityRegistry.decideNamed registry ctx grant capability args) }

        let internal decisionVectors () : Vector list =
            [ for ctx in contexts do
                  for c in capabilities do
                      for label, grant in grantsFor c.Id do
                          yield
                              decision
                                  (sprintf "gate-decide-%s-%s-grant-%s" ctx c.Id label)
                                  ctx
                                  c.Id
                                  (argsFor c.Id)
                                  grant
              yield
                  decision
                      "gate-decide-unregistered"
                      "untrusted"
                      "missing"
                      (argsFor "read")
                      (Some(Capability.grant (capOf "read") (argsFor "read")))
              yield decision "gate-decide-invalid-arguments" "untrusted" "erase" [ "n", "100" ] None ]

        let private dispatch
            (id: string)
            (ctx: string)
            (capability: string)
            (args: (string * string) list)
            (grant: Grant option)
            : Vector =
            let ran = ref 0

            let outcome =
                CapabilityRegistry.dispatch registry ctx grant capability args (fun _ () ->
                    ran.Value <- ran.Value + 1
                    Ready "performed")

            let expected =
                match outcome with
                | Ok(Ready _) when ran.Value = 1 -> [ "outcome", jstr "performed" ]
                | Error e when ran.Value = 0 -> ("outcome", jstr "refused") :: refusalCells e
                | other ->
                    [ "outcome", jstr "unexpected"
                      "detail", jstr (sprintf "%A after %d run(s)" other ran.Value) ]

            { Id = id
              Case = "dispatch"
              Model = None
              Input =
                [ "context", jstr ctx; "capability", jstr capability; "args", argsJson args ]
                @ grantMember grant
              Expected = expected }

        let internal dispatchVectors () : Vector list =
            let own c =
                Some(Capability.grant (capOf c) (argsFor c))

            let other c =
                Some(Capability.grant (capOf c) (otherArgsFor c))

            [ dispatch "gate-dispatch-denied" "untrusted" "erase" (argsFor "erase") (own "erase")
              dispatch "gate-dispatch-denied-trusted" "trusted" "erase" (argsFor "erase") (own "erase")
              dispatch "gate-dispatch-parked" "untrusted" "write" (argsFor "write") None
              dispatch "gate-dispatch-parked-other-grant" "untrusted" "write" (argsFor "write") (other "write")
              dispatch "gate-dispatch-granted" "untrusted" "write" (argsFor "write") (own "write")
              dispatch "gate-dispatch-allowed" "trusted" "read" (argsFor "read") None
              dispatch "gate-dispatch-unregistered" "untrusted" "missing" (argsFor "write") (own "write") ]

        let internal allVectors () : Vector list =
            identityVectors () @ grantVectors () @ decisionVectors () @ dispatchVectors ()

        /// The cases no extracted model answers, each with its reason: a host reads it beside the
        /// `answeredBy` stamp each such vector carries.
        let referenceOnly: (string * string) list =
            [ "decideNamed",
              "the model states the gates' join and the grant's lift (decide_named, grant_lifted in proofs/Capability.fst) and proves them, but neither is extracted, so no oracle function answers a decision"
              "dispatch",
              "the model's gated dispatch (dispatch_gated) is stated and proved over the same unextracted join, and no oracle function runs it" ]

        let private ruleCell (r: Rule) : CorpusCell =
            jobj (
                [ "context", jstr r.Context; "capability", jstr r.Capability ]
                @ decisionCells r.Decision
            )

        let private description =
            "The capability registry's gate, decision context and grant (Phase 638, DECISIONS.md D249), as data a port decides from (Phase 650, D255). "
            + "Authored inputs, each `expected` computed by calling the pinned kit; no seed. `registry` holds the three capability declarations, each a canonical "
            + "declaration as a JSON string. `contexts` names the two decision contexts, opaque labels. Each argument is `{\"name\", \"value\"}`, keyed by the "
            + "qualified name of the hole it binds (Phase 712, DECISIONS.md D281); each hole here is named as it is addressed. `gates` is the gate table in the order the registry runs it: "
            + "a gate answers the `decision` of its rule for the (context, capability) pair, and `allow` for any pair it has no rule for. The registry's decision is "
            + "the join over every gate in order (allow < needsApproval < deny, a deny keeping its message and alternatives), and the gate that made it is the FIRST "
            + "gate whose decision has the greatest rank; `policy` is \"\" when every gate allows. `invocationIdentity` vectors expect the identity a grant is bound "
            + "to: the fields id, then for each binding its hole's qualified name and its value's canonical spelling in its hole's space (an integer without sign or leading zeros, "
            + "a number in its shortest round-trip form, a string as given), the bindings sorted by name (ordinal), each field written with every U+0010 and U+0001 "
            + "it holds preceded by U+0010, then terminated by U+0001; no hash is taken. `grantCovers` vectors expect whether a presented grant (the `grant` member, an identity; "
            + "absent when none is presented) is for exactly this invocation: it is when the two identities are equal as strings. `decideNamed` vectors expect the registry's named decision for "
            + "an invocation under a context with a grant presented: `decided` with the deciding gate and the decision, where a needsApproval a covering grant lifts "
            + "is `allow` and keeps the gate's name, and no grant lifts a deny; or, where no gate decided, `refused` with the refusal a dispatch gives (an unregistered "
            + "id is `noSuchCapability` with the registered ids, an argument out of its space `argOutOfSpace` with its `name`). `dispatch` vectors expect `performed` when the performer "
            + "ran, exactly once, or `refused` with the refusal: `policyRefused` naming the gate with its reason and alternatives for a deny, `approvalRequired` naming "
            + "the gate for a needsApproval no grant lifts, `noSuchCapability` for an unregistered id whatever is presented; a refused dispatch never runs the performer. "
            + "`referenceOnly` names the cases no extracted model answers, with the reason."

        /// Rendered with an explicit `kitVersion`, as every law file is (Phase 560).
        let renderAt (stamp: string) : string =
            CorpusFile.render
                { Head =
                    [ "family", jstr familyId
                      "kitVersion", jstr stamp
                      "description", jstr description
                      "registry", jarr (capabilities |> List.map (CapabilityCodec.encode >> jstr))
                      "contexts", jarr (contexts |> List.map jstr)
                      "gates",
                      jarr (
                          gates
                          |> List.map (fun (policy, rules) ->
                              jobj [ "policy", jstr policy; "rules", jarr (rules |> List.map ruleCell) ])
                      )
                      "referenceOnly",
                      jarr (
                          referenceOnly
                          |> List.map (fun (case, why) -> jobj [ "case", jstr case; "reason", jstr why ])
                      ) ]
                  Items = "vectors"
                  Rows = allVectors () |> List.map renderVector }

        let render () : string = renderAt (kitVersion ())

    // -----------------------------------------------------------------------
    //  the query seam's identity and grant family (Phase 651)
    // -----------------------------------------------------------------------
    //  Phase 651 carried the query registry across to the decision context and the grant (DECISIONS.md
    //  D261). A grant for a read is bound to the read's identity, `Query.invocationPreimage`, and a port
    //  that follows the query registry certifies that identity and a grant's coverage against these
    //  vectors. They are a family of their own (D261.8), for D255.1's reason one step on: the Rust port
    //  reads `capability-gate-laws.json` and fails on a `case` it does not know, so the gate family gains
    //  no case. The gate's decision and dispatch are the seam's one function at both registries, which
    //  the gate family already pins, so this family carries the identity and the grant alone. Every
    //  `expected` is computed by CALLING the kit: the identity by `Query.invocationPreimage`, coverage by
    //  `Grant.covers`; and the extracted query model answers every vector (`invocation_identity`,
    //  `granted`). No seed: nothing is drawn.

    module QueryGrants =

        let fileName = "query-grant-laws.json"

        let familyId = "queryGrantLaws"

        let internal param (name: string) (t: ColumnType) (required: bool) : QueryParam =
            { Name = name
              Type = t
              Required = required }

        let private query (id: string) (ps: QueryParam list) (where: ColumnPredicate list) (order: SortKey list) =
            { Id = id
              Params = ps
              ResultSchema = [ Field.create "n" IntType; Field.create "region" StringType ]
              Effect =
                { Host = ReadsHost
                  Determinism = Effect.network }
              Source = Ref id
              TimeoutMs = None
              PageSize = PositiveInt.tryCreate 50
              Where = where
              OrderBy = order }

        /// The queries the vectors read, in id order: `ledger` and `summary` with one required integer,
        /// `every` with one optional parameter of each column type, and `shaped`, which declares a filter
        /// and an order.
        let queries: Query list =
            [ query
                  "every"
                  [ param "a" IntType false
                    param "b" FloatType false
                    param "c" BoolType false
                    param "d" StringType false
                    param "e" DateType false
                    param "f" (TimestampType TimeUnit.Milliseconds) false
                    param "g" DecimalType false ]
                  []
                  []
              query "ledger" [ param "n" IntType true ] [] []
              query
                  "shaped"
                  [ param "n" IntType true ]
                  [ ColumnPredicate.Contains("region", "eu")
                    ColumnPredicate.AtLeast("n", Int 2) ]
                  [ { Column = "n"
                      Direction = SortDirection.Descending } ]
              query "summary" [ param "n" IntType true ] [] [] ]

        let private queryOf (id: string) =
            queries |> List.find (fun q -> q.Id = id)

        /// A cell as the vectors spell it: its kind and its text, and no text for a null. A float's text
        /// is its shortest round-trip form; a boolean's is `true` or `false`.
        let internal cellJson (c: Cell) : CorpusCell =
            match c with
            | Null -> jobj [ "kind", jstr "null" ]
            | Int i -> jobj [ "kind", jstr "int"; "text", jstr (string i) ]
            | Float f -> jobj [ "kind", jstr "float"; "text", jstr (FloatLayout.canonical f) ]
            | Bool b -> jobj [ "kind", jstr "bool"; "text", jstr (if b then "true" else "false") ]
            | Str s -> jobj [ "kind", jstr "string"; "text", jstr s ]
            | Date s -> jobj [ "kind", jstr "date"; "text", jstr s ]
            | Timestamp s -> jobj [ "kind", jstr "timestamp"; "text", jstr s ]
            | Decimal s -> jobj [ "kind", jstr "decimal"; "text", jstr s ]

        let internal argsJson (args: (string * Cell) list) : CorpusCell =
            args
            |> List.map (fun (name, cell) -> jobj [ "name", jstr name; "cell", cellJson cell ])
            |> jarr

        // ---- the identity ----

        let private identity (id: string) (queryId: string) (args: (string * Cell) list) : Vector =
            let q = queryOf queryId

            { Id = id
              Case = "invocationIdentity"
              Model = QueryOracle.queryAnswer [ "identity", jstr (QueryOracle.queryIdentity q args) ]
              Input = [ "query", jstr queryId; "args", argsJson args ]
              Expected = [ "identity", jstr (Query.invocationPreimage (QuerySeamLaws.admitted q) args) ] }

        /// One binding of each kind, in parameter order.
        let everyKind: (string * Cell) list =
            [ "a", Int -7
              "b", Float 1.5
              "c", Bool true
              "d", Str "eu-west"
              "e", Date "2026-11-15"
              "f", Timestamp "2026-11-15T09:30:00.250Z"
              "g", Decimal "12.50" ]

        let internal identityVectors () : Vector list =
            [ identity "query-identity-ledger" "ledger" [ "n", Int 5 ]
              identity "query-identity-other-query" "summary" [ "n", Int 5 ]
              identity "query-identity-every-kind" "every" everyKind
              identity "query-identity-out-of-name-order" "every" (List.rev everyKind)
              identity "query-identity-null" "every" [ "d", Null; "a", Int 1 ]
              identity "query-identity-no-arguments" "every" []
              identity "query-identity-an-integer-for-a-float" "every" [ "b", Int 5 ]
              identity "query-identity-a-float-for-a-float" "every" [ "b", Float 5.0 ]
              identity "query-identity-shaped" "shaped" [ "n", Int 5 ]
              identity "query-identity-holds-the-separator" "every" [ "d", Str "a\u0001b" ]
              identity "query-identity-holds-the-escape" "every" [ "d", Str "a\u0010\u0001" ] ]

        // ---- the grant ----

        let private covers
            (id: string)
            (queryId: string)
            (args: (string * Cell) list)
            (pageToken: string option)
            (grant: Grant option)
            : Vector =
            let q = queryOf queryId

            { Id = id
              Case = "grantCovers"
              Model =
                QueryOracle.queryAnswer
                    [ "covers",
                      CorpusCell.Value(JBool(QueryOracle.queryGranted q args (grant |> Option.map _.Invocation))) ]
              Input =
                [ "query", jstr queryId; "args", argsJson args ]
                @ (match pageToken with
                   | Some t -> [ "pageToken", jstr t ]
                   | None -> [])
                @ Gates.grantMember grant
              Expected =
                [ "covers",
                  CorpusCell.Value(
                      JBool(
                          Grant.covers (Query.invocationPreimage (QuerySeamLaws.admitted q) args) (Option.toList grant)
                      )
                  ) ] }

        let internal grantVectors () : Vector list =
            let args = [ "n", Int 5 ]
            let ledger = QuerySeamLaws.admitted (queryOf "ledger")
            let own = Query.grant ledger args

            [ covers "query-grant-own" "ledger" args None (Some own)
              // a later page of the same read: the token is in neither identity (D261.4)
              covers "query-grant-own-a-later-page" "ledger" args (Some "page-2") (Some own)
              covers "query-grant-other-arguments" "ledger" args None (Some(Query.grant ledger [ "n", Int 6 ]))
              covers
                  "query-grant-other-query"
                  "ledger"
                  args
                  None
                  (Some(Query.grant (QuerySeamLaws.admitted (queryOf "summary")) args))
              covers
                  "query-grant-the-capture-key"
                  "ledger"
                  args
                  None
                  (Some(Grant.ofInvocation (Query.invocationKey ledger args)))
              covers
                  "query-grant-a-page-key"
                  "ledger"
                  args
                  (Some "page-2")
                  (Some(Grant.ofInvocation (Query.invocationKeyPage ledger args (Some "page-2"))))
              covers
                  "query-grant-the-float-for-an-integer"
                  "every"
                  [ "b", Int 5 ]
                  None
                  (Some(Query.grant (QuerySeamLaws.admitted (queryOf "every")) [ "b", Float 5.0 ]))
              covers "query-grant-none" "ledger" args None None ]

        let internal allVectors () : Vector list = identityVectors () @ grantVectors ()

        let private description =
            "The query registry's grant (Phase 651, DECISIONS.md D261): the identity a grant for a read is bound to, and whether a presented grant covers a read. "
            + "Authored inputs, each `expected` computed by calling the pinned kit; no seed. `registry` holds the queries the vectors read, each a canonical "
            + "declaration as a JSON string. A binding is `{name, cell}`, a cell `{kind, text}` with no text for a null (a float's text is its shortest "
            + "round-trip form, a boolean's `true` or `false`). `invocationIdentity` vectors expect the identity: the fields id; then, when the declaration "
            + "declares a filter, the three fields \"\", `w` and the canonical text of its `where` member, and when it declares an order, \"\", `o` and the "
            + "canonical text of its `orderBy` member, filter first; then for each binding, sorted by name (ordinal), its name, its kind's tag (`i` int, "
            + "`f` float, `b` bool, `s` string, `d` date, `t` timestamp, `m` decimal, `n` null) and its payload (an integer's decimal digits, a float's "
            + "shortest round-trip text, `1` or `0` for a boolean, a string, date, timestamp or decimal as given, empty for a null); each field written with "
            + "every U+0010 and U+0001 it holds preceded by U+0010, then terminated by U+0001; no hash is taken. A cell is identified by its kind, so an "
            + "integer bound to a float parameter and the float of the same value are two reads. `grantCovers` vectors expect whether a presented grant "
            + "(the `grant` member, an identity; absent when none is presented) is for exactly this read: it is when the two identities are equal as strings. "
            + "A `pageToken` member names the page the read is dispatched for: it is in neither identity, so a grant for a read covers every page of it, "
            + "and a page's capture key is no grant. The gate's decision and dispatch are the gate family's (capability-gate-laws.json): one function decides "
            + "at both registries."

        /// Rendered with an explicit `kitVersion`, as every law file is (Phase 560).
        let renderAt (stamp: string) : string =
            CorpusFile.render
                { Head =
                    [ "family", jstr familyId
                      "kitVersion", jstr stamp
                      "description", jstr description
                      "registry", jarr (queries |> List.map (QuerySeamLaws.admitted >> QueryCodec.encode >> jstr)) ]
                  Items = "vectors"
                  Rows = allVectors () |> List.map renderVector }

        let render () : string = renderAt (kitVersion ())

    // -----------------------------------------------------------------------
    //  the query declaration's refusals (Phase 680)
    // -----------------------------------------------------------------------
    //  DECISIONS.md D263. The refusals a query declaration and a read of it can meet: the declaration
    //  reader's, on code and path (`declarationRefused`, as `capabilityLaws` pins a capability
    //  declaration's); the admission gate's, held to a declaration stated as DATA, because two of its
    //  refusals no declaration text can carry (`admissionFault`); registration's (`register`); argument
    //  validation's (`validateParams`); and the registry's under a gate table (`dispatch`). Every refusal
    //  outside the reader is pinned as the query-error document the seam codec writes. A family of its
    //  own (D263.2): no file a host reads gains a case. Every `expected` is computed by CALLING the kit,
    //  and the extracted query model answers every vector it states (D263.5). No seed: nothing is drawn.

    module QueryDeclarations =

        let fileName = "query-declaration-laws.json"

        let familyId = "queryDeclarationLaws"

        let private declaration (id: string) (ps: QueryParam list) (schema: Schema) : Query =
            { Id = id
              Params = ps
              ResultSchema = schema
              Effect =
                { Host = ReadsHost
                  Determinism = Effect.network }
              Source = Ref id
              TimeoutMs = None
              PageSize = None
              Where = []
              OrderBy = [] }

        /// The registry the vectors read, in id order: `bare`, with no parameter and no column; `ledger`
        /// and `payroll`, one required integer each, which the gate parks and denies; and `sales`, four
        /// parameters and six result columns, one of each type a literal is held to.
        let queries: Query list =
            [ declaration "bare" [] []
              declaration "ledger" [ QueryGrants.param "n" IntType true ] [ Field.create "n" IntType ]
              declaration "payroll" [ QueryGrants.param "n" IntType true ] [ Field.create "n" IntType ]
              { declaration
                    "sales"
                    [ QueryGrants.param "n" IntType true
                      QueryGrants.param "rate" FloatType false
                      QueryGrants.param "tag" StringType false
                      QueryGrants.param "cap" DecimalType false ]
                    [ Field.create "n" IntType
                      Field.create "region" StringType
                      Field.create "at" (TimestampType TimeUnit.Milliseconds)
                      Field.create "amount" DecimalType
                      Field.create "day" DateType
                      Field.create "score" FloatType ] with
                  PageSize = PositiveInt.tryCreate 50 } ]

        let private queryOf (id: string) =
            queries |> List.find (fun q -> q.Id = id)

        /// The one gate the registry runs (`ceiling`): it parks every read of `ledger`, denies every read
        /// of `payroll` naming `ledger` as the alternative, and allows the rest.
        let gate: string * (string * PolicyDecision) list =
            "ceiling",
            [ "ledger", PolicyDecision.NeedsApproval
              "payroll", PolicyDecision.denyWith "payroll is closed to this reader" [ "ledger" ] ]

        /// The production registry the head declares: the four queries, under the gate.
        let registry: QueryRegistry<unit> =
            let policy, rules = gate

            queries
            |> List.fold (fun r q -> r |> Result.bind (QueryRegistry.register q)) (Ok QueryRegistry.empty)
            |> Result.defaultWith (fun e -> failwithf "the declaration family's registry did not build: %A" e)
            |> QueryRegistry.withGate
                { Policy = policy
                  Decide =
                    fun () q _ ->
                        rules
                        |> List.tryPick (fun (id, d) -> if id = q.Id then Some d else None)
                        |> Option.defaultValue PolicyDecision.Allow }

        /// The model's registry over the same queries (no gate: the model's gated dispatch is not
        /// extracted, D263.5).
        let private modelRegistry: ModelQuery.registry =
            queries
            |> List.fold
                (fun r q ->
                    match ModelQuery.register (QueryOracle.queryToModel q) r with
                    | ModelQuery.Ok r' -> r'
                    | ModelQuery.Error e -> failwithf "the model refused %s: %A" q.Id e)
                ModelQuery.empty

        // ---- the refusal, as the seam codec writes it ----

        let private errorCell (e: QueryError) : CorpusCell =
            CorpusCell.Value(QuerySeamCodec.encodeQueryError e)

        let private modelErrorCell (e: ModelQuery.query_error) : CorpusCell =
            CorpusCell.Value(
                CapabilityOracle.jvalOfModel (
                    ModelQuery.query_error_json QueryOracle.columnCodec (ModelQuery.QRefused e)
                )
            )

        /// The query model's answer under the ladder row that holds its codec (the reader and the gate it
        /// runs) to production.
        let private codecAnswer (cells: (string * CorpusCell) list) : VectorOracle.Answer option =
            Some
                { Model = QueryOracle.QueryModel
                  LadderRow = QueryOracle.Seams.QueryRow
                  Cell = jobj cells }

        let private modelSegText (s: ModelCap.path_seg) : string =
            match s with
            | ModelCap.Key k -> k
            | ModelCap.Index n -> string n

        /// The reference's admission gate, `QueryRegistry.admissionFaultAt`: the refusal with the path to the
        /// member at fault. It is internal to its package (the registry and the reader are its callers), so
        /// it is called by reflection, which is the reference's own function and no restatement of it.
        let admissionFaultAt (q: Query) : (PathSegment list * QueryError) option =
            let gateFn =
                typeof<QueryRegistry<unit>>.Assembly
                    .GetType("Fuaran.Core.QueryRegistryModule", true)
                    .GetMethod(
                        "admissionFaultAt",
                        BindingFlags.Static ||| BindingFlags.NonPublic ||| BindingFlags.Public
                    )

            gateFn.Invoke(null, [| box q |])
            |> unbox<(PathSegment list * QueryError) option>

        // ---- a declaration stated as data (D263.4) ----

        /// A predicate's tag, as the wire spells it.
        let private predicateTag (p: ColumnPredicate) : string =
            match p with
            | ColumnPredicate.EqualTo _ -> "equalTo"
            | ColumnPredicate.GreaterThan _ -> "greaterThan"
            | ColumnPredicate.AtLeast _ -> "atLeast"
            | ColumnPredicate.LessThan _ -> "lessThan"
            | ColumnPredicate.AtMost _ -> "atMost"
            | ColumnPredicate.Contains _ -> "contains"
            | ColumnPredicate.IsNull _ -> "isNull"
            | ColumnPredicate.IsNotNull _ -> "isNotNull"

        let private predicateData (p: ColumnPredicate) : CorpusCell =
            let head c =
                [ "predicate", jstr (predicateTag p); "column", jstr c ]

            match p with
            | ColumnPredicate.EqualTo(c, v)
            | ColumnPredicate.GreaterThan(c, v)
            | ColumnPredicate.AtLeast(c, v)
            | ColumnPredicate.LessThan(c, v)
            | ColumnPredicate.AtMost(c, v) -> jobj (head c @ [ "cell", QueryGrants.cellJson v ])
            | ColumnPredicate.Contains(c, t) -> jobj (head c @ [ "text", jstr t ])
            | ColumnPredicate.IsNull c
            | ColumnPredicate.IsNotNull c -> jobj (head c)

        let private paramData (p: QueryParam) : CorpusCell =
            jobj
                [ "name", jstr p.Name
                  "type", jstr (ColumnType.tag p.Type)
                  "required", CorpusCell.Value(JBool p.Required) ]

        let private keyData (k: SortKey) : CorpusCell =
            jobj
                [ "column", jstr k.Column
                  "direction",
                  jstr (
                      match k.Direction with
                      | SortDirection.Ascending -> "ascending"
                      | SortDirection.Descending -> "descending"
                  ) ]

        /// An embedded table a vector states as data (Phase 688, DECISIONS.md D266.10 point 5): its schema
        /// entries and its columns, each a list so that a name can repeat; a column carries its own type
        /// and its cells, every one present. The faults only typed storage holds (a mask of another
        /// length, an instant its text cannot spell) are not stated: Core's own tests hold them.
        type SourceTable =
            { Schema: (string * ColumnType) list
              Columns: (string * ColumnType * Cell list) list }

        /// A declaration a vector states: a declaration of the registry, with the members given replacing
        /// its own.
        type Stated =
            { Base: string
              Id: string option
              Params: QueryParam list option
              Where: ColumnPredicate list option
              OrderBy: SortKey list option
              Source: SourceTable option }

        let private on (baseId: string) : Stated =
            { Base = baseId
              Id = None
              Params = None
              Where = None
              OrderBy = None
              Source = None }

        /// A stated column, built by the typed builder of its type, which checks nothing, so a cell the
        /// column layer refuses is held as stated.
        let private columnOf (name: string, ty: ColumnType, cells: Cell list) : Column =
            let values (pick: Cell -> 'T option) =
                cells
                |> List.map (fun c ->
                    pick c
                    |> Option.defaultWith (fun () -> failwithf "a %A column states the cell %A" ty c))
                |> Vector.ofList

            match ty with
            | IntType ->
                Column.ofInts
                    name
                    (values (function
                        | Int n -> Some n
                        | _ -> None))
                    AllValid
            | FloatType ->
                Column.ofFloats
                    name
                    (values (function
                        | Float x -> Some x
                        | _ -> None))
                    AllValid
            | StringType ->
                Column.ofStrs
                    name
                    (values (function
                        | Str x -> Some x
                        | _ -> None))
                    AllValid
            | DecimalType ->
                Column.ofDecimals
                    name
                    (values (function
                        | Decimal x -> Some x
                        | _ -> None))
                    AllValid
            | other -> failwithf "the family states no %A column" other

        let private tableOf (t: SourceTable) : Table =
            { Schema = t.Schema |> List.map (fun (n, ty) -> Field.create n ty)
              Columns = t.Columns |> List.map columnOf }

        let private build (s: Stated) : Query =
            let b = queryOf s.Base

            { b with
                Id = Option.defaultValue b.Id s.Id
                Params = Option.defaultValue b.Params s.Params
                Where = Option.defaultValue b.Where s.Where
                OrderBy = Option.defaultValue b.OrderBy s.OrderBy
                Source = s.Source |> Option.map (tableOf >> Embedded) |> Option.defaultValue b.Source }

        let private sourceData (t: SourceTable) : CorpusCell =
            jobj
                [ "schema",
                  jarr (
                      t.Schema
                      |> List.map (fun (n, ty) -> jobj [ "name", jstr n; "type", jstr (ColumnType.tag ty) ])
                  )
                  "columns",
                  jarr (
                      t.Columns
                      |> List.map (fun (n, ty, cells) ->
                          jobj
                              [ "name", jstr n
                                "type", jstr (ColumnType.tag ty)
                                "cells", jarr (cells |> List.map QueryGrants.cellJson) ])
                  ) ]

        let private statedInput (s: Stated) : (string * CorpusCell) list =
            [ "query", jstr s.Base ]
            @ (s.Id |> Option.map (fun id -> "id", jstr id) |> Option.toList)
            @ (s.Params
               |> Option.map (fun ps -> "params", jarr (ps |> List.map paramData))
               |> Option.toList)
            @ (s.Where
               |> Option.map (fun w -> "where", jarr (w |> List.map predicateData))
               |> Option.toList)
            @ (s.OrderBy
               |> Option.map (fun o -> "orderBy", jarr (o |> List.map keyData))
               |> Option.toList)
            @ (s.Source |> Option.map (fun t -> "source", sourceData t) |> Option.toList)

        let private salesParams = (queryOf "sales").Params

        let private eq (c: string) (v: Cell) = ColumnPredicate.EqualTo(c, v)

        let private key (c: string) (d: SortDirection) : SortKey = { Column = c; Direction = d }

        /// The admission vectors over a source stated as data (Phase 688, DECISIONS.md D266.10 point 5): eight
        /// tables `Table.validate` refuses, each refused `illFormedSource` at `[source]` before anything else
        /// (the ragged one beside a parameter declared twice), and a valid table in schema order and out of
        /// it, each admitted.
        let private sourceAdmissions: (string * Stated) list =
            let sales = on "sales"
            let ab = [ "a", IntType; "b", StringType ]

            let stated (schema: (string * ColumnType) list) (columns: (string * ColumnType * Cell list) list) =
                Some { Schema = schema; Columns = columns }

            [ "admission-a-source-naming-a-schema-entry-twice",
              { sales with
                  Source = stated [ "a", IntType; "a", IntType ] [ "a", IntType, [ Int 1 ] ] }
              "admission-a-source-naming-a-column-twice",
              { sales with
                  Source = stated [ "a", IntType ] [ "a", IntType, [ Int 1 ]; "a", IntType, [ Int 2 ] ] }
              "admission-a-source-schema-entry-with-no-column",
              { sales with
                  Source = stated ab [ "a", IntType, [ Int 1 ] ] }
              "admission-a-source-column-absent-from-the-schema",
              { sales with
                  Source = stated [ "a", IntType ] [ "a", IntType, [ Int 1 ]; "z", IntType, [ Int 9 ] ] }
              "admission-a-source-column-of-another-type",
              { sales with
                  Source = stated [ "a", FloatType ] [ "a", IntType, [ Int 1; Int 2 ] ] }
              "admission-a-ragged-source-before-a-parameter-declared-twice",
              { sales with
                  Params = Some(salesParams @ [ QueryGrants.param "rate" StringType false ])
                  Source = stated ab [ "a", IntType, [ Int 1; Int 2 ]; "b", StringType, [ Str "x" ] ] }
              "admission-a-source-float-not-finite",
              { sales with
                  Source = stated [ "a", FloatType ] [ "a", FloatType, [ Float nan ] ] }
              "admission-a-source-decimal-text-not-canonical",
              { sales with
                  Source = stated [ "a", DecimalType ] [ "a", DecimalType, [ Decimal "12.50" ] ] }
              "admission-a-source-in-schema-order",
              { sales with
                  Source = stated ab [ "a", IntType, [ Int 1 ]; "b", StringType, [ Str "x" ] ] }
              "admission-a-source-out-of-schema-order",
              { sales with
                  Source = stated ab [ "b", StringType, [ Str "x" ]; "a", IntType, [ Int 1 ] ] } ]

        /// The admission vectors' declarations, each with its id. The ones whose literal its column cannot
        /// carry are `uncarried`: no text reaches them, and the model does not restate the clause.
        let private admissions: (string * Stated) list =
            let sales = on "sales"

            [ "admission-a-parameter-declared-twice",
              { sales with
                  Params = Some(salesParams @ [ QueryGrants.param "rate" StringType false ])
                  Where = Some [ ColumnPredicate.IsNull "nope" ] }
              "admission-an-undeclared-column",
              { sales with
                  Where = Some [ ColumnPredicate.IsNull "nope" ] }
              "admission-an-undeclared-column-of-no-columns",
              { on "bare" with
                  Where = Some [ ColumnPredicate.IsNotNull "n" ] }
              "admission-contains-on-an-undeclared-column",
              { sales with
                  Where = Some [ ColumnPredicate.Contains("nope", "x") ] }
              "admission-contains-off-a-string-column",
              { sales with
                  Where = Some [ ColumnPredicate.Contains("n", "x") ] }
              "admission-contains-on-a-string-column",
              { sales with
                  Where = Some [ ColumnPredicate.Contains("region", "eu") ] }
              "admission-a-null-literal",
              { sales with
                  Where = Some [ eq "n" Null ] }
              "admission-a-literal-of-another-type",
              { sales with
                  Where = Some [ eq "region" (Int 1) ] }
              "admission-an-integer-on-a-decimal-column",
              { sales with
                  Where = Some [ eq "amount" (Int 1) ] }
              "admission-an-integer-on-a-float-column",
              { sales with
                  Where = Some [ eq "score" (Int 1) ] }
              "admission-a-timestamp-coarser-than-its-column",
              { sales with
                  Where = Some [ eq "at" (Timestamp "2026-11-15T09:30:00Z") ] }
              "admission-a-timestamp-finer-than-its-column",
              { sales with
                  Where = Some [ eq "at" (Timestamp "2026-11-15T09:30:00.000001Z") ] }
              "admission-a-date-that-names-no-day",
              { sales with
                  Where = Some [ eq "day" (Date "2026-02-30") ] }
              "admission-a-timestamp-not-canonical",
              { sales with
                  Where = Some [ eq "at" (Timestamp "2026-11-15 09:30:00Z") ] }
              "admission-decimal-text-not-canonical",
              { sales with
                  Where = Some [ eq "amount" (Decimal "01.50") ] }
              "admission-a-float-not-finite",
              { sales with
                  Where = Some [ eq "score" (Float nan) ] }
              // Phase 685 (D264.8 point 7): the gate's clause for three literals the Rust port's adoption
              // found no vector pinning: the reason a negative infinity is refused with, a date not in
              // canonical form, and decimal text with a trailing zero.
              "admission-a-float-negative-infinity",
              { sales with
                  Where = Some [ eq "score" (Float -infinity) ] }
              "admission-a-date-not-in-canonical-form",
              { sales with
                  Where = Some [ eq "day" (Date "2026-2-1") ] }
              "admission-decimal-text-with-a-trailing-zero",
              { sales with
                  Where = Some [ eq "amount" (Decimal "12.50") ] }
              "admission-the-second-predicate",
              { sales with
                  Where = Some [ ColumnPredicate.IsNull "n"; ColumnPredicate.Contains("n", "x") ] }
              "admission-an-order-on-an-undeclared-column",
              { sales with
                  OrderBy = Some [ key "nope" SortDirection.Ascending ] }
              "admission-an-order-naming-a-column-twice",
              { sales with
                  OrderBy =
                      Some
                          [ key "n" SortDirection.Ascending
                            key "region" SortDirection.Ascending
                            key "n" SortDirection.Descending ] }
              "admission-the-filter-before-the-order",
              { sales with
                  Where = Some [ ColumnPredicate.IsNull "nope" ]
                  OrderBy = Some [ key "nope" SortDirection.Ascending ] }
              "admission-admitted",
              { sales with
                  Where =
                      Some
                          [ ColumnPredicate.Contains("region", "eu")
                            ColumnPredicate.AtLeast("amount", Decimal "12.5")
                            ColumnPredicate.LessThan("day", Date "2026-11-15")
                            ColumnPredicate.IsNotNull "score" ]
                  OrderBy = Some [ key "at" SortDirection.Descending; key "n" SortDirection.Ascending ] } ]
            @ sourceAdmissions

        /// The admission vectors whose literal its column cannot carry: the model's `predicate_fault`
        /// does not restate `whereFault`'s carriability clause (proofs/Query.fst), so these are the
        /// reference's alone.
        let uncarried: string list =
            [ "admission-a-date-that-names-no-day"
              "admission-a-timestamp-not-canonical"
              "admission-decimal-text-not-canonical"
              "admission-a-float-not-finite"
              "admission-a-float-negative-infinity"
              "admission-a-date-not-in-canonical-form"
              "admission-decimal-text-with-a-trailing-zero" ]

        let private gateCells (fault: (string list * CorpusCell) option) : (string * CorpusCell) list =
            match fault with
            | None -> [ "verdict", jstr "admitted" ]
            | Some(at, error) -> [ "verdict", jstr "refused"; "at", jarr (at |> List.map jstr); "error", error ]

        let private admissionVector (id: string, s: Stated) : Vector =
            let q = build s

            let reference =
                admissionFaultAt q
                |> Option.map (fun (at, e) -> at |> List.map Capabilities.segText, errorCell e)

            let model =
                if List.contains id uncarried then
                    None
                else
                    ModelQuery.admission_fault_at (QueryOracle.queryToModel q)
                    |> VectorOracle.ofMOpt
                    |> Option.map (fun (at, e) -> at |> List.map modelSegText, modelErrorCell e)
                    |> gateCells
                    |> codecAnswer

            { Id = id
              Case = "admissionFault"
              Model = model
              Input = statedInput s
              Expected = gateCells reference }

        // ---- the reader, over a declaration's text ----

        let private salesText = QueryCodec.encode (QuerySeamLaws.admitted (queryOf "sales"))

        /// The `sales` declaration's text with `member` set to `raw` (JSON text), or removed for `None`.
        let private edited (edits: (string * string option) list) : string =
            match Json.parse salesText with
            | Ok(JObj ms) ->
                let apply (ms: (string * JVal) list) (name: string, raw: string option) =
                    match raw with
                    | None -> ms |> List.filter (fun (k, _) -> k <> name)
                    | Some text ->
                        let v =
                            match Json.parse text with
                            | Ok v -> v
                            | Error e -> failwithf "an edit is not JSON: %s (%s)" text e

                        if ms |> List.exists (fun (k, _) -> k = name) then
                            ms |> List.map (fun (k, x) -> if k = name then k, v else k, x)
                        else
                            ms @ [ name, v ]

                Canonical.write Rendering.CrossHost (JObj(List.fold apply ms edits))
            | _ -> failwith "the sales declaration does not read back as an object"

        let private readerCells (text: string) : (string * CorpusCell) list =
            match QueryCodec.decodeDetailedWith ReadPolicy.Lenient text with
            | Error e -> Capabilities.refusalCells e.Code (e.Path |> List.map Capabilities.segText)
            | Ok _ -> [ "verdict", jstr "accept" ]

        /// The model's reader: the parser model, then `query_of_j` over production's column codec.
        let private modelReaderCells (text: string) : (string * CorpusCell) list =
            match VectorOracle.JsonModel.readStrict text with
            | Error _ -> Capabilities.refusalCells DecodeCode.InvalidJson []
            | Ok j ->
                match ModelQuery.query_of_j QueryOracle.columnCodec (CapabilityOracle.jvalToModel j) with
                | ModelCap.Ok _ -> [ "verdict", jstr "accept" ]
                | ModelCap.Error e ->
                    Capabilities.refusalCells
                        (CapabilityOracle.modelCodeOf e.d_code)
                        (e.d_path |> List.map modelSegText)

        let private refused (id: string) (text: string) : Vector =
            { Id = id
              Case = "declarationRefused"
              Model = codecAnswer (modelReaderCells text)
              Input = [ "declaration", jstr text ]
              Expected = readerCells text }

        /// The text of an admission vector's declaration (one whose text reaches the gate), written by
        /// edit (`QueryOracle.declarationText`, Phase 685: no writer writes a declaration the gate refuses).
        let private textOf (admission: string) =
            admissions
            |> List.find (fun (id, _) -> id = admission)
            |> snd
            |> build
            |> QueryOracle.declarationText

        let internal declarationVectors () : Vector list =
            [ refused "declaration-not-json" "{"
              refused "declaration-a-null" (salesText.Replace("\"pageSize\":50", "\"pageSize\":null"))
              refused
                  "declaration-a-number-past-the-float-range"
                  (salesText.Replace("\"pageSize\":50", "\"pageSize\":1e999"))
              refused "declaration-not-an-object" "[]"
              refused "declaration-no-type" (edited [ "$type", None ])
              refused "declaration-another-type" (edited [ "$type", Some "\"capability\"" ])
              refused "declaration-an-id-not-a-string" (edited [ "id", Some "1" ])
              refused "declaration-the-id-before-the-params" (edited [ "id", None; "params", Some "7" ])
              refused "declaration-params-not-a-list" (edited [ "params", Some "{}" ])
              refused
                  "declaration-a-parameter-without-a-name"
                  (edited [ "params", Some """[{"required":true,"type":"int"}]""" ])
              refused
                  "declaration-a-parameter-type-unknown"
                  (edited [ "params", Some """[{"name":"n","required":true,"type":"integer"}]""" ])
              refused
                  "declaration-a-parameter-required-not-a-boolean"
                  (edited [ "params", Some """[{"name":"n","required":"yes","type":"int"}]""" ])
              refused "declaration-no-result-schema" (edited [ "resultSchema", None ])
              refused
                  "declaration-a-result-column-unread"
                  (edited [ "resultSchema", Some """[{"name":"n","type":"integer"}]""" ])
              refused
                  "declaration-an-effect-host-unknown"
                  (edited [ "effect", Some """{"determinism":"network","host":"everywhere"}""" ])
              refused "declaration-a-timeout-with-a-fraction" (edited [ "timeoutMs", Some "1.5" ])
              refused "declaration-a-page-size-as-a-string" (edited [ "pageSize", Some "\"50\"" ])
              refused "declaration-a-filter-not-a-list" (edited [ "where", Some "{}" ])
              refused
                  "declaration-a-predicate-tag-unknown"
                  (edited [ "where", Some """[{"$type":"like","column":"region"}]""" ])
              refused
                  "declaration-a-literal-its-column-cannot-carry"
                  (edited
                      [ "where", Some """[{"$type":"equalTo","column":"day","type":"date","value":"2026-02-30"}]""" ])
              refused
                  "declaration-a-literal-not-of-its-stated-type"
                  (edited [ "where", Some """[{"$type":"equalTo","column":"n","type":"int","value":1.5}]""" ])
              refused
                  "declaration-an-order-direction-unknown"
                  (edited [ "orderBy", Some """[{"column":"n","direction":"up"}]""" ])
              refused "declaration-the-filter-before-the-source" (edited [ "where", Some "7"; "source", None ])
              refused "declaration-no-source" (edited [ "source", None ])
              refused "declaration-a-source-unread" (edited [ "source", Some "5" ])
              refused
                  "declaration-the-source-before-the-gate"
                  (edited [ "where", Some """[{"$type":"isNull","column":"nope"}]"""; "source", Some "5" ])
              refused "declaration-gate-a-parameter-declared-twice" (textOf "admission-a-parameter-declared-twice")
              refused "declaration-gate-an-undeclared-column" (textOf "admission-an-undeclared-column")
              refused "declaration-gate-contains-off-a-string-column" (textOf "admission-contains-off-a-string-column")
              refused "declaration-gate-a-literal-of-another-type" (textOf "admission-a-literal-of-another-type")
              refused "declaration-gate-the-second-predicate" (textOf "admission-the-second-predicate")
              refused
                  "declaration-gate-an-order-on-an-undeclared-column"
                  (textOf "admission-an-order-on-an-undeclared-column")
              refused
                  "declaration-gate-an-order-naming-a-column-twice"
                  (textOf "admission-an-order-naming-a-column-twice")
              refused "declaration-gate-the-filter-before-the-order" (textOf "admission-the-filter-before-the-order")
              refused
                  "declaration-decimal-text-read-canonical"
                  (edited
                      [ "where", Some """[{"$type":"equalTo","column":"amount","type":"decimal","value":"01.50"}]""" ])
              refused "declaration-admitted" (textOf "admission-admitted")
              // Phase 685 (D264.7 point 3, D264.8 point 7): a budget is a positive integer, refused
              // OutOfRange at its member at 0 and below; 1 is the boundary and reads.
              refused "declaration-a-timeout-of-zero" (edited [ "timeoutMs", Some "0" ])
              refused "declaration-a-timeout-below-zero" (edited [ "timeoutMs", Some "-1" ])
              refused "declaration-a-page-size-of-zero" (edited [ "pageSize", Some "0" ])
              refused "declaration-a-page-size-below-zero" (edited [ "pageSize", Some "-1" ])
              refused "declaration-budgets-of-one" (edited [ "timeoutMs", Some "1"; "pageSize", Some "1" ])
              // ... and the reader's clauses the Rust port's adoption found no vector pinning.
              refused "declaration-no-params" (edited [ "params", None ])
              refused "declaration-a-result-schema-not-a-list" (edited [ "resultSchema", Some "{}" ])
              refused "declaration-no-effect" (edited [ "effect", None ])
              refused
                  "declaration-a-date-not-in-canonical-form"
                  (edited [ "where", Some """[{"$type":"equalTo","column":"day","type":"date","value":"2026-2-1"}]""" ])
              refused
                  "declaration-decimal-text-with-a-trailing-zero-read-canonical"
                  (edited
                      [ "where", Some """[{"$type":"equalTo","column":"amount","type":"decimal","value":"12.50"}]""" ]) ]

        let internal admissionVectors () : Vector list = admissions |> List.map admissionVector

        // ---- registration ----

        let private registerVector (id: string) (s: Stated) : Vector =
            let q = build s

            let cells (r: Result<unit, CorpusCell>) =
                match r with
                | Ok() -> [ "verdict", jstr "registered" ]
                | Error e -> [ "verdict", jstr "refused"; "error", e ]

            let model =
                match ModelQuery.register (QueryOracle.queryToModel q) modelRegistry with
                | ModelQuery.Ok _ -> Ok()
                | ModelQuery.Error e -> Error(modelErrorCell e)

            { Id = id
              Case = "register"
              Model = QueryOracle.queryAnswer (cells model)
              Input = statedInput s
              Expected =
                cells (
                    QueryRegistry.register q registry
                    |> Result.map ignore
                    |> Result.mapError errorCell
                ) }

        let internal registerVectors () : Vector list =
            let sales = on "sales"

            [ registerVector "register-a-held-id" (on "ledger")
              registerVector
                  "register-a-held-id-before-the-gate"
                  { sales with
                      Where = Some [ ColumnPredicate.IsNull "nope" ] }
              registerVector
                  "register-a-new-id-held-to-the-gate"
                  { sales with
                      Id = Some "sales-eu"
                      Where = Some [ ColumnPredicate.IsNull "nope" ] }
              registerVector "register-a-new-id" { sales with Id = Some "sales-eu" } ]

        // ---- argument validation ----

        let private validate (id: string) (queryId: string) (args: (string * Cell) list) : Vector =
            let q = queryOf queryId

            let cells (r: Result<unit, CorpusCell>) =
                match r with
                | Ok() -> [ "verdict", jstr "accept" ]
                | Error e -> [ "verdict", jstr "reject"; "error", e ]

            let model =
                match ModelQuery.validate_params (QueryOracle.queryToModel q) (QueryOracle.qArgsToModel args) with
                | ModelQuery.Ok _ -> Ok()
                | ModelQuery.Error e -> Error(modelErrorCell e)

            { Id = id
              Case = "validateParams"
              Model = QueryOracle.queryAnswer (cells model)
              Input = [ "query", jstr queryId; "args", QueryGrants.argsJson args ]
              Expected = cells (Query.validateParams q args |> Result.mapError errorCell) }

        let internal validationVectors () : Vector list =
            [ validate "validate-accepted" "sales" [ "n", Int 1 ]
              validate "validate-an-integer-fills-a-float" "sales" [ "n", Int 1; "rate", Int 2 ]
              validate "validate-an-integer-fills-a-decimal" "sales" [ "n", Int 1; "cap", Int 2 ]
              validate "validate-a-null-binds-an-optional" "sales" [ "n", Int 1; "tag", Null ]
              validate "validate-a-name-bound-twice" "sales" [ "n", Int 1; "nope", Int 1; "n", Int 2 ]
              validate "validate-a-null-then-a-value-is-twice" "sales" [ "n", Null; "n", Int 1 ]
              validate "validate-an-unknown-name-before-a-later-type" "sales" [ "nope", Int 1; "n", Str "x" ]
              validate "validate-a-type-before-a-later-unknown-name" "sales" [ "n", Str "x"; "nope", Int 1 ]
              validate "validate-a-float-for-an-integer" "sales" [ "n", Float 1.0 ]
              validate "validate-a-decimal-for-a-float" "sales" [ "n", Int 1; "rate", Decimal "1.5" ]
              validate "validate-a-required-name-left-out" "sales" [ "rate", Float 1.5 ]
              validate "validate-a-type-before-a-name-left-out" "sales" [ "rate", Str "x" ]
              validate "validate-a-required-name-bound-to-null" "sales" [ "n", Null ]
              validate "validate-a-query-with-no-parameters" "bare" [ "x", Int 1 ] ]

        // ---- the registry ----

        let private dispatch
            (id: string)
            (queryId: string)
            (args: (string * Cell) list)
            (grant: Grant option)
            : Vector =
            let mutable ran = 0

            let outcome =
                QueryRegistry.dispatch registry () grant queryId args (fun _ ->
                    ran <- ran + 1
                    Deferred.Pending)

            let expected =
                match outcome with
                | Ok _ when ran = 1 -> [ "verdict", jstr "performed" ]
                | Ok _ -> failwithf "%s: the resolver ran %d times" id ran
                | Error e when ran = 0 -> [ "verdict", jstr "refused"; "error", errorCell e ]
                | Error _ -> failwithf "%s: a refused dispatch ran the resolver" id

            { Id = id
              Case = "dispatch"
              Model = None
              Input =
                [ "query", jstr queryId; "args", QueryGrants.argsJson args ]
                @ Gates.grantMember grant
              Expected = expected }

        let internal dispatchVectors () : Vector list =
            let own q args =
                Some(Query.grant (QuerySeamLaws.admitted (queryOf q)) args)

            [ dispatch "dispatch-unregistered" "nope" [] None
              dispatch "dispatch-unregistered-with-a-grant" "nope" [ "n", Int 7 ] (own "ledger" [ "n", Int 7 ])
              dispatch "dispatch-validated-before-the-gate" "payroll" [ "n", Str "9" ] None
              dispatch "dispatch-denied" "payroll" [ "n", Int 9 ] None
              dispatch "dispatch-denied-with-its-own-grant" "payroll" [ "n", Int 9 ] (own "payroll" [ "n", Int 9 ])
              dispatch "dispatch-parked" "ledger" [ "n", Int 7 ] None
              dispatch "dispatch-parked-with-another-reads-grant" "ledger" [ "n", Int 7 ] (own "ledger" [ "n", Int 6 ])
              dispatch "dispatch-granted" "ledger" [ "n", Int 7 ] (own "ledger" [ "n", Int 7 ])
              dispatch "dispatch-allowed" "sales" [ "n", Int 1 ] None ]

        let internal allVectors () : Vector list =
            declarationVectors ()
            @ admissionVectors ()
            @ registerVectors ()
            @ validationVectors ()
            @ dispatchVectors ()

        /// The vectors no extracted model answers, with the reason (D263.5).
        let referenceOnly: (string * string list * string) list =
            [ "admissionFault",
              uncarried,
              "a literal its column cannot carry: the model's predicate_fault does not restate whereFault's carriability clause (Table.validate's), which its comment in proofs/Query.fst says"
              "dispatch",
              [],
              "the model states the query registry's gated dispatch and proves it (proofs/Query.fst section 11), but it is not extracted, so no oracle function runs a gate" ]

        /// The cases of `QueryError` no vector raises, each with what raises it and why the family does
        /// not (D263.6).
        let unreached: (string * string * string) list =
            let resolver =
                "the host's resolver, through Query.invokeWithArgs' projection of a ResolveFault"

            let why =
                "a declaration and a typed call's validation never raise it: it is the host's resolver's refusal, which Core only renames"

            [ "unreadableArgs",
              "QueryCodec.decodeArgs, the reader of a model's argument document",
              "a declaration and a typed call never raise it: it is a refusal of another document's reader, which reads arguments from JSON text"
              "sourceNotResolved", resolver, why
              "executionFailed", resolver, why
              "timeout", resolver, why
              "predicateNotHonoured", resolver, why
              "orderNotHonoured", resolver, why ]

        let private description =
            "The refusals a query declaration and a read of it meet (Phase 680, DECISIONS.md D263). Authored inputs, each `expected` computed by "
            + "calling the pinned kit; no seed. `registry` holds the queries the vectors read, each a canonical declaration as a JSON string, in "
            + "id order. `gates` is the gate table the registry runs: a gate answers the `decision` of its rule for a query, and `allow` for a "
            + "query it has no rule for. Every refusal but the reader's is pinned as its query-error document, an object under `error`, as the "
            + "seam codec writes it (the document seam-refusals.json certifies a reader of); no sentence is pinned. `declarationRefused` vectors "
            + "read `declaration`, a declaration's text, with the declaration reader (lenient: an unknown member is read past) and expect `accept`, "
            + "or `reject` with the decode `code` and the `path` to the fault: the members are read in the order $type, id, params, resultSchema, "
            + "effect, timeoutMs, pageSize, where, orderBy, source, and the admission gate last, whose refusal is OutOfRange at the gate's path; "
            + "timeoutMs and pageSize are positive integers, and one that is not is OutOfRange at its member (Phase 685); "
            + "the parser refuses every null and every number past the finite double range, InvalidJson at the root. `admissionFault` and "
            + "`register` vectors state a declaration as DATA, because two of the gate's refusals no text can carry: `query` names a declaration "
            + "of `registry`, and `id` (a string), `params` (a list of {name, type, required}), `where` (a list of {predicate, column}, with "
            + "`cell` for a comparison and `text` for contains), `orderBy` (a list of {column, direction}) and `source` (Phase 688: an "
            + "embedded table, `schema` a list of {name, type} and `columns` a list of {name, type, cells}, every cell present, a name free to "
            + "repeat in either list, each column held as stated by its type's builder), when present, replace its own. A "
            + "cell is {kind, text}, no text for a null, its text as it stands and never canonicalised (a float's is its shortest round-trip "
            + "form, a non-finite float's the token in quotes, as the canonical float layout writes it). `admissionFault` vectors hold the "
            + "declaration to the admission gate and expect `admitted`, or `refused` with `at`, the path to the member at fault, and `error`: a "
            + "source table the column layer refuses at [source], illFormedSource with that layer's reason (a valid table in any column order "
            + "is admitted); then a parameter declared twice at [params]; then each predicate in turn at [where, i] (an undeclared column, then contains off a string "
            + "column, then a null literal, then a literal of another type, with no widening and a timestamp fitting a column at least as fine "
            + "as its own unit, then a literal its column cannot carry, with the column layer's reason); then each order key at [orderBy, i] (an "
            + "undeclared column, then a column named before). `register` vectors register the declaration into the registry and expect "
            + "`registered`, or `refused` with `error`: an id the registry holds is refused before the gate. `validateParams` vectors hold `args` "
            + "(a list of {name, cell}) to `query`'s parameters and expect `accept`, or `reject` with the first refusal: a name bound twice; then "
            + "per argument in order an unknown name or a cell that does not widen to its parameter's type (an int fills a float or a decimal, a "
            + "null fills any); then every required name left out; then every required name bound only to null. `dispatch` vectors dispatch a "
            + "read through the registry, a `grant` (an identity, as query-grant-laws.json writes one) presented or not, and expect `performed` "
            + "when the resolver ran, exactly once, or `refused` with `error`: an unregistered id whatever is presented, then validation, then "
            + "the gate, a deny lifted by no grant and a needsApproval by a grant for exactly this read only. `referenceOnly` names the vectors "
            + "no extracted model answers (a case whole, or the `vectors` it lists), with the reason; every other vector's `answeredBy` names the query model. `unreached` names each "
            + "case of QueryError no vector raises, with what raises it and why."

        /// Rendered with an explicit `kitVersion`, as every law file is (Phase 560).
        let renderAt (stamp: string) : string =
            let policy, rules = gate

            let ruleCell (queryId: string, d: PolicyDecision) =
                let decision =
                    match d with
                    | PolicyDecision.Allow -> [ "decision", jstr "allow" ]
                    | PolicyDecision.NeedsApproval -> [ "decision", jstr "needsApproval" ]
                    | PolicyDecision.Deny g ->
                        [ "decision", jstr "deny"
                          "message", jstr g.Message
                          "alternatives", jarr (g.Alternatives |> List.map jstr) ]

                jobj ([ "query", jstr queryId ] @ decision)

            CorpusFile.render
                { Head =
                    [ "family", jstr familyId
                      "kitVersion", jstr stamp
                      "description", jstr description
                      "registry", jarr (queries |> List.map (QuerySeamLaws.admitted >> QueryCodec.encode >> jstr))
                      "gates", jarr [ jobj [ "policy", jstr policy; "rules", jarr (rules |> List.map ruleCell) ] ]
                      "referenceOnly",
                      jarr (
                          referenceOnly
                          |> List.map (fun (case, vectors, why) ->
                              jobj (
                                  [ "case", jstr case ]
                                  @ (if List.isEmpty vectors then
                                         []
                                     else
                                         [ "vectors", jarr (vectors |> List.map jstr) ])
                                  @ [ "reason", jstr why ]
                              ))
                      )
                      "unreached",
                      jarr (
                          unreached
                          |> List.map (fun (error, by, why) ->
                              jobj [ "error", jstr error; "raisedBy", jstr by; "reason", jstr why ])
                      ) ]
                  Items = "vectors"
                  Rows = allVectors () |> List.map renderVector }

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

        let private accept (members: (string * CorpusCell) list) = ("verdict", jstr "accept") :: members

        let private reject (error: string) =
            [ "verdict", jstr "reject"; "error", jstr error ]

        /// A cell as the documents write it.
        let private cellJson (c: Cell) : CorpusCell =
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

        /// A codec outcome as these vectors record it: the canonical bytes, or the refusal's class
        /// (`NotJson` for a parse refusal, whichever parse fault it was).
        let private codecCells (o: ColumnOracle.Outcome) : CorpusCell =
            match o with
            | Ok canonical -> jobj (accept [ "canonical", jstr canonical ])
            | Error cls -> jobj (reject (cls.Split('/')[0]))

        /// The `codecDecode` vectors the column model declines (the not-answered rule, D232): a JSON
        /// number in the decimal column reaches the decoder as a float — a fraction, an exponent, or a
        /// whole number past the 32-bit integer the parser reads as an int — and the decoder's
        /// lenient-ingest arm for it is outside the model (`proofs/WireColumn.fst`, `decode_cell`).
        let decodeOutOfModel: Map<string, string> =
            [ "decode-integer-token-past-int32", "a whole number past 2^31 is read as a JSON float"
              "decode-whole-exponent-token", "an exponent token is read as a JSON float"
              "decode-refuses-fractional-token", "a fraction token is a JSON float"
              "decode-refuses-integer-token-past-2-53", "a whole number past 2^53 is read as a JSON float"
              "decode-refuses-whole-float-past-2-53", "an exponent token is read as a JSON float" ]
            |> Map.ofList

        // ---- the vectors, computed by calling the kit ----

        let internal allVectors () : Vector list =
            [ for name, text in canonicalInputs ->
                  { Id = "canonical-" + name
                    Case = "canonical"
                    Model =
                      VectorOracle.DecimalModel.answer (
                          match VectorOracle.DecimalModel.tryCanonical text with
                          | Some c -> accept [ "canonical", jstr c ]
                          | None -> reject "notDecimal"
                      )
                    Input = [ "text", jstr text ]
                    Expected =
                      match DecimalText.tryCanonical text with
                      | Some c -> accept [ "canonical", jstr c ]
                      | None -> reject "notDecimal" }
              for name, a, b in compareInputs ->
                  { Id = "compare-" + name
                    Case = "compare"
                    Model =
                      VectorOracle.DecimalModel.answer (
                          match VectorOracle.DecimalModel.compare a b with
                          | Some c -> accept [ "order", jint c ]
                          | None -> reject "notDecimal"
                      )
                    Input = [ "a", jstr a; "b", jstr b ]
                    Expected =
                      match DecimalText.compare a b with
                      | Some c -> accept [ "order", jint c ]
                      | None -> reject "notDecimal" }
              for name, a, b in addInputs ->
                  { Id = "add-" + name
                    Case = "add"
                    Model =
                      VectorOracle.DecimalModel.answer (
                          match VectorOracle.DecimalModel.add a b with
                          | Some s -> accept [ "sum", jstr s ]
                          | None -> reject "notDecimal"
                      )
                    Input = [ "a", jstr a; "b", jstr b ]
                    Expected =
                      match DecimalText.add a b with
                      | Some s -> accept [ "sum", jstr s ]
                      | None -> reject "notDecimal" }
              for name, text in toFloatInputs ->
                  { Id = "to-float-" + name
                    Case = "toFloat"
                    Model = None
                    Input = [ "text", jstr text ]
                    Expected =
                      match DecimalText.tryToFloat text, DecimalText.tryCanonical text with
                      | Some f, _ -> accept [ "float", jstr (FloatLayout.canonical f) ]
                      | None, Some _ -> reject "pastFloatRange"
                      | None, None -> reject "notDecimal" }
              for name, values in decodeInputs ->
                  let doc = document values

                  { Id = "decode-" + name
                    Case = "codecDecode"
                    Model =
                      ColumnOracle.decode ColumnOracle.CodecRow codecCells doc
                      |> VectorOracle.admit decodeOutOfModel ("decode-" + name)
                    Input = [ "document", jstr doc ]
                    Expected =
                      match ColumnCodec.decode doc with
                      | Ok src -> accept [ "canonical", jstr (ColumnCodec.encode src) ]
                      | Error e -> reject (columnErrorClass e) }
              for name, cells in encodeInputs ->
                  { Id = "encode-" + name
                    Case = "codecEncode"
                    Model =
                      match source cells with
                      | Ok(Embedded t) ->
                          ColumnOracle.encode codecCells t
                          |> VectorOracle.admit Map.empty ("encode-" + name)
                      | other -> failwithf "the decimal column for encode-%s did not build: %A" name other
                    Input = [ "cells", jarr (cells |> List.map cellJson) ]
                    Expected =
                      match source cells |> Result.bind ColumnCodec.tryEncode with
                      | Ok text -> accept [ "canonical", jstr text ]
                      | Error e -> reject (columnErrorClass e) }
              for name, fn, cells in aggregateInputs ->
                  { Id = "aggregate-" + name
                    Case = "aggregate"
                    Model = None
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
            CorpusFile.render
                { Head =
                    [ "family", jstr "decimal"
                      "kitVersion", jstr stamp
                      "description", jstr description ]
                  Items = "vectors"
                  Rows = allVectors () |> List.map renderVector }

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

        let private accept (members: (string * CorpusCell) list) = ("verdict", jstr "accept") :: members

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
            Canonical.write Rendering.CrossHost (JArr(s |> List.map ColumnCodec.fieldJson))

        /// `schemaText`'s value as a cell: the schema as a compact canonical array.
        let private schemaCell (s: Schema) : CorpusCell =
            CorpusCell.Value(JArr(s |> List.map ColumnCodec.fieldJson))

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

        /// A codec outcome as these vectors record it (the decimal documents' shape).
        let private codecCells (o: ColumnOracle.Outcome) : CorpusCell =
            match o with
            | Ok canonical -> jobj (accept [ "canonical", jstr canonical ])
            | Error cls -> jobj (reject (cls.Split('/')[0]))

        // ---- the vectors, computed by calling the kit ----

        let internal allVectors () : Vector list =
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
                    Model =
                      Some
                          { Model = ColumnOracle.Model
                            LadderRow = ColumnOracle.FieldRow
                            Cell = jobj (accept [ "canonical", jstr (ColumnOracle.fieldEntry f) ]) }
                    Input = members
                    Expected =
                      accept [ "canonical", jstr (Canonical.write Rendering.CrossHost (ColumnCodec.fieldJson f)) ] }
              for name, doc in decodeInputs ->
                  { Id = "decode-" + name
                    Case = "codecDecode"
                    Model =
                      ColumnOracle.decode ColumnOracle.FieldRow codecCells doc
                      |> VectorOracle.admit Map.empty ("decode-" + name)
                    Input = [ "document", jstr doc ]
                    Expected =
                      match ColumnCodec.decode doc with
                      | Ok src -> accept [ "canonical", jstr (ColumnCodec.encode src) ]
                      | Error e -> reject (columnErrorClass e) }
              for name, schema in fingerprintInputs ->
                  { Id = "fingerprint-" + name
                    Case = "fingerprint"
                    Model = None
                    Input = [ "schema", schemaCell schema ]
                    Expected = accept [ "fingerprint", jstr (Schema.fingerprint schema) ] }
              for name, old, target in deltaInputs ->
                  let delta = Schema.diff old target

                  { Id = "delta-" + name
                    Case = "delta"
                    Model = None
                    Input = [ "old", schemaCell old; "target", schemaCell target ]
                    Expected =
                      match Schema.patch old delta with
                      | Ok patched ->
                          accept
                              [ "canonical", jstr (SchemaDeltaCodec.encode delta)
                                "patched", schemaCell patched ]
                      | Error e -> failwithf "the delta %s did not replay: %A" name e }
              for name, old, target, deps in classifyInputs ->
                  { Id = "classify-" + name
                    Case = "classify"
                    Model = None
                    Input =
                      [ "old", schemaCell old
                        "target", schemaCell target
                        "dependsOn", jarr (List.map jstr deps) ]
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
            CorpusFile.render
                { Head =
                    [ "family", jstr "fieldLaws"
                      "kitVersion", jstr stamp
                      "description", jstr description ]
                  Items = "vectors"
                  Rows = allVectors () |> List.map renderVector }

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

    let propagationPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, PropagationVectorExport.fileName)

    let canonicalFormPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, CanonicalFormVectorExport.fileName)

    let gatePath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, Gates.fileName)

    let documentPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, Documents.fileName)

    let queryGrantPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, QueryGrants.fileName)

    let queryDeclarationPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, QueryDeclarations.fileName)

    /// Every law file under `corpusDir`, rendered with `stamp` as its `kitVersion` (Phase 560): opening
    /// a version slot stamps the version it opens, which is not yet the version this kit was built at.
    let emittedAt (stamp: string) (corpusDir: string) : (string * string) list =
        [ capabilityPath corpusDir, Capabilities.renderAt stamp
          decimalPath corpusDir, Decimals.renderAt stamp
          fieldPath corpusDir, Fields.renderAt stamp
          propagationPath corpusDir, PropagationVectorExport.renderAt stamp
          canonicalFormPath corpusDir, CanonicalFormVectorExport.renderAt stamp
          gatePath corpusDir, Gates.renderAt stamp
          documentPath corpusDir, Documents.renderAt stamp
          queryGrantPath corpusDir, QueryGrants.renderAt stamp
          queryDeclarationPath corpusDir, QueryDeclarations.renderAt stamp ]

    /// Every law file under `corpusDir`, stamped with this kit's own version.
    let emitted (corpusDir: string) : (string * string) list = emittedAt (kitVersion ()) corpusDir

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
    ///
    /// `canonicalFormLaws` (Phase 539) is emitted and not yet listed: the corpus indexes it in the
    /// change-set that first carries the file, and a row named here before the index holds it would refuse
    /// every `render laws <corpus dir>` and redden the asked manifest leg until then.
    let manifestRows: (string * string) list =
        [ "capabilityLaws", Capabilities.fileName
          "decimal", Decimals.fileName
          "fieldLaws", Fields.fileName
          "propagationLaws", PropagationVectorExport.fileName ]

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
        Canonical.write Rendering.CrossHost value

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

    /// The rows `writeFiles` restamps: one per manifest family among `files`, derived from the text
    /// about to be written — the same bytes, so a row can never name a file other than the one beside
    /// it. A family whose file is not handed keeps its row as it stands.
    let private writtenRows (files: (string * string) list) : (string * (string * JVal) list) list =
        manifestRows
        |> List.choose (fun (id, fileName) ->
            files
            |> List.tryFind (fun (path, _) -> Path.GetFileName path = fileName)
            |> Option.map (fun (_, text) ->
                match derivedMembers text with
                | Ok members -> id, members
                | Error e -> failwithf "the %s this kit renders did not read back: %s" fileName e))

    // -----------------------------------------------------------------------
    //  Phase 564 — this repository's own law-family index, with the adoption record
    // -----------------------------------------------------------------------
    //  Every other family Core emits carries a self-enumerated manifest whose rows hold the D222
    //  adoption record; the law families had none, so a host's measured law cases had nowhere to be
    //  written but a decision's prose. `conformance/laws/manifest.json` is that index, in the one
    //  manifest schema, one row per law file this repository emits. It is NOT the corpus's
    //  `laws/manifest.json`, which is a hand-curated shared index spanning another producer's
    //  families and whose Core rows are restamped above: the two share a file name as the apply
    //  family's two indexes do (D211), and the corpus never receives this one.

    /// The codec twins that read the law files from the shared corpus, as the refusal and decode
    /// families record them (`RefusalCorpus.hostRoster`, which compiles after this module).
    let private lawRoster = "fuaran" :: ApplyVectorExport.hostRoster

    /// The index over the law files `files` renders, each row counted from the text it names.
    let internal ownManifest (files: (string * string) list) : CorpusManifest =
        let row (path: string, text: string) =
            match Json.parse text with
            | Ok(JObj fields) ->
                match
                    fields |> List.tryFind (fun (k, _) -> k = "family"),
                    fields |> List.tryFind (fun (k, _) -> k = "vectors")
                with
                | Some(_, JStr id), Some(_, JArr vectors) ->
                    { Id = id
                      Kind = "law-vectors"
                      File = Path.GetFileName path
                      Vectors = List.length vectors
                      Adoption =
                        (lawRoster |> List.map (fun h -> h, HostAdoption.Proposed))
                        @ SpineAdoption.adoptions id
                      Description =
                        match fields |> List.tryFind (fun (k, _) -> k = "description") with
                        | Some(_, JStr d) -> d
                        | _ -> failwithf "%s declares no description" path }
                | _ -> failwithf "%s declares no string `family` and `vectors` array" path
            | _ -> failwithf "%s is not a JSON object" path

        { Version = 1
          Description =
            "Law-vector families this repository emits (Phase 564). SELF-ENUMERATED, as apply/ and refusals/ are, and distinct from the shared corpus's laws/manifest.json, which indexes these files beside another producer's and carries no adoption. `adoption` is per host and is DATA: a host whose entry reads `proposed` reports the family BY NAME rather than skipping it, and flips its own entry in the change-set that lands its leg. This manifest is authoritative for the vector counts."
          Families = files |> List.map row }

    /// The index, rendered over the law files as this kit stamps them (no row member carries the stamp).
    let renderOwnManifest () : string =
        CorpusFile.render (CorpusFile.manifest (ownManifest (emitted "")))

    /// Write `files` (law files under `corpusDir`, as `emittedAt` renders them) with LF endings,
    /// whatever the host platform — the corpus is byte-compared by several hosts on three operating
    /// systems. Where `files` carries the family `manifest.json` (this repository's own index) it is
    /// written whole like any other file; where it does not and one sits beside them (the corpus's
    /// shared index), the rows of the families written are restamped in the same act (Phase 394). A
    /// manifest that cannot be restamped fails the command BEFORE any file is written, so the files
    /// and their rows never part company. Only the files handed are written (Phase 560: a corpus
    /// checkout receives only the copies it declares).
    let writeFiles (corpusDir: string) (files: (string * string) list) : unit =
        let manifest = manifestPath corpusDir

        let ownIndex =
            files
            |> List.exists (fun (p, _) -> Path.GetFullPath p = Path.GetFullPath manifest)

        let restamped =
            if not ownIndex && File.Exists manifest then
                match restampManifest (File.ReadAllText manifest) (writtenRows files) with
                | Ok text -> Some text
                | Error why -> failwithf "%s (%s) — nothing was written" why manifest
            else
                None

        Directory.CreateDirectory(familyDir corpusDir) |> ignore

        for path, text in files do
            File.WriteAllText(path, text)

        match restamped with
        | Some text when text <> File.ReadAllText manifest -> File.WriteAllText(manifest, text)
        | _ -> ()
