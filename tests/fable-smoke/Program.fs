module FableSmoke.Program

// The Fable-compile smoke (Phase 54). Touch one public symbol per Fuaran.Core package so the Fable
// compiler pulls every package's source into the compilation — a clean compile here proves the
// encode + decode surface is Fable-clean (GP3 / STABILITY.md). The values are never run for output;
// the COMPILE is the gate.

open Fuaran.Core
open Fuaran.Core.Idl

// Tree — content hashing + the portable FNV-1a.
let private treeTouch = Hash.fnv1a "smoke"

// Hash — the pinned pure SHA-256. Both public forms, because the byte form and the hex form take
// different paths out of the compression pass and only the hex one would otherwise be compiled.
// This is the primitive whose Fable-cleanliness is load-bearing rather than incidental: a digest
// taken by a server has to verify in a browser, so a construct that does not transpile here is a
// cross-host verification failure, not a missing feature. The vectors themselves are pinned in
// `Fuaran.Core.Tests.HashTests`; what this file adds is that the code reaches the browser at all.
let private sha256Touch =
    sprintf "%s/%d" (Hash.sha256Hex "smoke") (Hash.sha256Bytes (Hash.utf8Bytes "smoke")).Length

// Ops — reference the skeleton-op type so Ops.fs compiles.
let private opsTouch: SkeletonOp<int, string> option = None

// Wire — canonical render / parse round-trip + the Phase-55 canonical float encoder.
let private wireTouch =
    let s = Json.render (JObj [ "k", JStr "v"; "n", JInt 1; "f", JFloat 1.5 ])

    match Json.parse s with
    | Ok _ -> Canon.canonicalFloat 1.5
    | Error e -> e

// OpStream + DAG — the empty stream / DAG + the default hash.
let private opStreamTouch = OpStream.empty
let private dagTouch = Dag.empty
let private hashTouch = OpStream.defaultHash

// Validator — forces Validator.fs (incl. ColumnValidator.cellToken's float layout) to compile.
let private validatorTouch = Validator.canonicalCodes ([]: Defect<string> list)

// Function — signature → JSON-schema projection (+ pulls in applyMemo / observedEffect / registry).
let private functionTouch =
    let sg: Signature =
        { Name = "s"
          Holes = []
          Effect = Effect.pureDeterministic }

    Function.toJsonSchema sg |> Json.render

// Column + DataFrame — the cell-type probe + the Phase-55 float cell-string.
let private columnTouch = Cell.typeOf (Int 1)
let private dataFrameTouch = DataFrame.cellString (Float 1.5)

// Delta (Phase 98) — the delta algebra's encode AND decode, plus the identity witness. Named here
// rather than left to ride along on the DataFrame project reference: the point of this file is that
// each surface is reached DELIBERATELY, so a package that stops being Fable-clean is a compile error
// attached to the line that names it.
let private deltaTouch =
    let d =
        Delta.compose
            (Delta.ofRows (RowIdentity.byColumn "id").Scheme [ ByKey "s:a", RowAdded ])
            (Delta.ofColumns (RowIdentity.byColumn "id").Scheme [ "amount" ])

    match DeltaCodec.decode (DeltaCodec.encode d) with
    | Ok back -> Delta.toChange back
    | Error e -> Some(ColumnValuesChanged(ColumnCodec.errorString e))

// Incremental (Phase 99) — the classification, a primed state, and a delta-driven refresh. Named
// deliberately for the same reason `deltaTouch` is: the seam is the piece a browser-side consumer
// most wants (a re-render that re-evaluates only the rows that moved), so a package that stopped
// being Fable-clean here would be discovered by the consumer rather than by this file.
let private incrementalTouch =
    let idw = RowIdentity.byColumn "id"

    let t: Table =
        { Schema = [ "id", StringType; "a", IntType ]
          Columns =
            [ Column.create "id" StringType [ Str "r0"; Str "r1" ]
              Column.create "a" IntType [ Int 1; Int 2 ] ] }

    let pipeline = [ Filter(Binary(Gt, Col "a", Lit(Int 0))) ]
    let declared = Incremental.isIncremental (Incremental.plan pipeline)

    match Incremental.primeOn idw pipeline t with
    | Ok primed ->
        match Incremental.refreshOn idw pipeline primed (Delta.empty idw.Scheme) t with
        | Ok next -> declared, Incremental.footprintString (Incremental.footprint next)
        | Error e -> declared, DataFrame.errorString e
    | Error e -> declared, DataFrame.errorString e

// Query — declaration codec round-trip.
let private queryTouch =
    let q: Query =
        { Id = "q"
          Params = []
          ResultSchema = [ "n", IntType ]
          Effect =
            { Host = ReadsHost
              Determinism = Network }
          Source = Ref "src"
          TimeoutMs = Some 5000
          PageSize = None }

    QueryCodec.encode q

// Conformance — a self-contained law (also exercises Wire's canonical float under Fable).
let private conformanceTouch = Conformance.canonicalFloatLaws 1 3

// Projection (Phase 58) — project / render / parseBack over a tiny inline witness: the
// whole encode path (the seam ships source for Fable) must be Fable-clean.
type private SmokeNode = { Id: string; Kids: SmokeNode list }

let private projectionTouch =
    let pw: ProjectionWitness<SmokeNode, string, string> =
        { Tree =
            { Id = _.Id
              KindTag = fun _ -> "n"
              Children = _.Kids
              ReplaceChildren = fun n cs -> { n with Kids = cs } }
          IdW =
            { ToString = id
              OfString = id
              Equals = (=) }
          Encode = _.Id
          Snippet = fun _ -> ""
          ParseBack = fun _ -> Ok [] }

    let root =
        { Id = "r"
          Kids = [ { Id = "c"; Kids = [] } ] }

    let p = Projection.project pw Whole root
    let snap = Projection.snapshot pw root
    let changed = Projection.project pw (ChangedSince snap) root

    match Projection.parseBack pw (Projection.render p) with
    | Ok _ -> sprintf "%d/%d" (Projection.sizeOf p) (List.length changed.Lines)
    | Error e -> e

// Propagation (Phase 68) — dependency map + topological sort (Tarjan SCC) + dirty propagation over an
// inline witness: the change-propagation surface (incl. the SCC collections) must be Fable-clean.
let private propagationTouch =
    let tw: NodeWitness<SmokeNode, string> =
        { Id = _.Id
          KindTag = fun _ -> "n"
          Children = _.Kids
          ReplaceChildren = fun n cs -> { n with Kids = cs } }

    let tidw: IdWitness<string> =
        { ToString = id
          OfString = id
          Equals = (=) }

    let root =
        { Id = "r"
          Kids = [ { Id = "a"; Kids = [] }; { Id = "b"; Kids = [] } ] }

    let readsOf (n: SmokeNode) =
        if n.Id = "a" then Seq.singleton "b" else Seq.empty

    let deps = Propagation.dependencyMap tw tidw readsOf root
    let sorted = Propagation.sort deps
    let dirty = Propagation.dirtyFromChangedIds deps (Set.singleton "b")
    let touched = Propagation.touchedBy tw tidw root (RemoveNode "a")
    // Phase 69 — the incremental recompute driver (evalFrom over the dirty subgraph) must be Fable-clean.
    let evalNode (resolve: string -> int option) id =
        Ok(
            (if id = "b" then 1 else 0)
            + (Map.find id deps
               |> Set.fold (fun s r -> s + (resolve r |> Option.defaultValue 0)) 0)
        )

    let recomputed =
        match Propagation.eval evalNode deps with
        | Ok prior ->
            match Propagation.evalFrom evalNode prior.Values (Set.singleton "b") deps with
            | Ok outcome -> Map.count outcome.Values
            | Error _ -> -1
        | Error _ -> -1

    sprintf
        "%d/%d/%d/%d/%d"
        (List.length sorted.Order)
        (List.length sorted.Cycles)
        (Set.count dirty)
        (Set.count touched)
        recomputed

// AiSurface (Phase 59) — catalogue + fast-path resolve + the proposal lifecycle over an
// inline witness: the whole surface must be Fable-clean.
let private aiSurfaceTouch =
    let w: AiSurfaceWitness<string list, string, string> =
        { ReadTools =
            [ { Name = "count"
                Description = "how many"
                Run = fun s -> JInt(List.length s) } ]
          OpKinds =
            [ { Kind = "add"
                Description = "append"
                Schema = Json.kindObj "signature" [] } ]
          KindOfOp = fun _ -> "add"
          Patterns =
            [ { Name = "add"
                Title = "Add"
                PromptAnchors = [ "add {x}" ]
                Emit = fun _ -> Ok [ "op" ] } ]
          Decide = fun _ _ -> Allow
          Apply = fun op s -> Ok(s @ [ op ])
          Explain = fun m -> { Message = m; Alternatives = [] } }

    let resolved = PatternBank.resolve w { Text = "add one"; Args = [] }

    match Proposals.submit w "a" "t" None [ "op" ] Proposals.Queue.empty [] with
    | Proposals.SubmitApplied s -> AiSurface.catalogueJson w + sprintf "%d/%A" (List.length s) resolved
    | other -> sprintf "%A" other

// Idl (Phase 97) — the domain-neutral half of the IDL engine: declare a two-kind vocabulary, then
// touch every leg a consumer can reach without the generator. This is the leg the split exists for:
// the browser hosts are Fable, so "the model, codec, sampler and sanitisation floor are portable" was
// an unprovable claim while the emitters sat in the same project and dragged
// `CultureInfo.InvariantCulture` in with them. Sampling is here rather than only encode/decode
// because the sampler's LCG is 64-bit arithmetic, which is exactly the shape that transpiles
// differently — compile-checked here, value-checked by the suite's seeded vectors on .NET.
let private idlTouch =
    let vocab: Idl =
        { Kinds =
            [ { Tag = "Note"
                Category = "leaf"
                Fields =
                  [ { Name = "text"
                      Type = TStr
                      Opt = Required }
                    { Name = "weight"
                      Type = TFloat
                      Opt = Optional } ] }
              { Tag = "Box"
                Category = "container"
                Fields =
                  [ { Name = "children"
                      Type = TList TNode
                      Opt = Required } ] } ]
          Unions = []
          Enums = [ Declare.enumOf "Tone" [ "Calm"; "Loud" ] ]
          Records = []
          Defaults = []
          NodeFields = []
          Ops = [] }

    let sampled = Sample.sampleNodes vocab [ "Note"; "Box" ] 20260821 4

    let roundTripped =
        sampled
        |> List.map (fun v ->
            match Encode.encode vocab v with
            | Error e -> e
            | Ok json ->
                match Decode.decode vocab json with
                | Ok _ -> json
                | Error e -> e)
        |> String.concat "|"

    sprintf
        "%d/%s/%s/%s/%d"
        (String.length (Artifact.render vocab))
        (Sanitize.sanitizeUrlOrBlank "javascript:alert(1)")
        (Sanitize.scrubMarkdown "[x](javascript:alert(1))")
        roundTripped
        (Map.count (Sanitize.sanitizeAttributes (Map.ofList [ "onclick", "x"; "title", "t" ])))

// OpStream.Attributed (Phase 81) — the attributed-stream lift + envelope codec must be Fable-clean on
// encode AND decode (GP3): liftWitness derives an attributed witness, whose Encode/Decode wrap the inner
// codec in an attribution envelope decoded via the self-contained JSONL scanner.
let private attributedTouch =
    let sw: StreamWitness<int, int, string> =
        { Apply = fun op st -> Ok(st + op)
          Encode = fun op -> Json.render (JInt op)
          Decode = fun s -> Decode.parse s |> Result.bind Decode.asInt }

    let lifted = OpStream.Attributed.liftWitness sw

    let a: Attributed<int> =
        { Actor = "a"
          Session = "s"
          Turn = Some 1
          At = "t"
          Op = 5 }

    match lifted.Decode(lifted.Encode a) with
    | Ok a' -> sprintf "%d/%A" a'.Op a'.Turn
    | Error e -> e

[<EntryPoint>]
let main _ =
    // Reference each touch so nothing is dead-code-eliminated before the compiler sees it.
    [ treeTouch
      sha256Touch
      attributedTouch
      (sprintf "%A" opsTouch)
      wireTouch
      (sprintf "%A" opStreamTouch)
      (sprintf "%A" dagTouch)
      (sprintf "%A" hashTouch)
      validatorTouch
      functionTouch
      (sprintf "%A" columnTouch)
      dataFrameTouch
      // `deltaTouch` was defined by Phase 98 but never referenced here, so the whole point of the
      // file — a compile error attached to the line that names the surface — did not apply to it.
      (sprintf "%A" deltaTouch)
      (sprintf "%A" incrementalTouch)
      queryTouch
      (sprintf "%A" (List.length conformanceTouch))
      projectionTouch
      propagationTouch
      aiSurfaceTouch
      idlTouch ]
    |> List.iter (printfn "%s")

    0
