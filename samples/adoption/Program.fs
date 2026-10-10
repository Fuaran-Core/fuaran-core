module Adoption.Program

// The reference adoption (Phase 256): a tiny "outline" domain re-expressed over
// Fuaran.Core.* end-to-end — the copy-from template `docs/ADOPTION.md` walks through.
// `Section`s are containers; `Note`s are leaves (so `ReplaceChildren` is partial — the F1
// case). Run it: `dotnet run --project samples/adoption` → prints a conformance report and
// exits non-zero if any law fails.

open Fuaran.Core

// ---- the tiny domain (a closed kind set, string ids) ----

type Kind =
    | Section
    | Note

type Item =
    { Id: string
      Kind: Kind
      Text: string
      Children: Item list }

// ---- 1. the witnesses ----

let idw: IdWitness<string> =
    { ToString = id
      OfString = id
      Equals = (=) }

let kindTag (i: Item) =
    match i.Kind with
    | Section -> "section"
    | Note -> "note"

let nodew: NodeWitness<Item, string> =
    { Id = fun i -> i.Id
      KindTag = kindTag
      Children = fun i -> i.Children
      // a Note is a leaf — ReplaceChildren is a no-op on it (the F1 partiality), so the
      // container capability below is load-bearing.
      ReplaceChildren =
        fun i cs ->
            match i.Kind with
            | Section -> { i with Children = cs }
            | Note -> i }

let canHold (i: Item) = i.Kind = Section

// ---- 2. a generator for the conformance kit ----

let private genTree (rng: ConfRng.T) : Item * ConfRng.T =
    let mutable counter = 0
    let mutable r = rng

    let freshId () =
        let s = sprintf "n%d" counter
        counter <- counter + 1
        s

    let rec build depth =
        let id = freshId ()
        let leafRoll, r1 = ConfRng.intBelow 2 r
        r <- r1

        if depth <= 0 || leafRoll = 0 then
            { Id = id
              Kind = Note
              Text = ""
              Children = [] }
        else
            let nKids, r2 = ConfRng.intBelow 3 r
            r <- r2

            { Id = id
              Kind = Section
              Text = ""
              Children = [ for _ in 1..nKids -> build (depth - 1) ] }

    let rootId = freshId ()
    let nKids, r2 = ConfRng.intBelow 3 r
    r <- r2

    { Id = rootId
      Kind = Section
      Text = ""
      Children = [ for _ in 1..nKids -> build 1 ] },
    r

let private genFresh (existing: Set<string>) (rng: ConfRng.T) : Item * ConfRng.T =
    let mutable r = rng

    let rec pick () =
        let v, r' = ConfRng.next r
        r <- r'
        let id = sprintf "f%d" (v % 100000)
        if existing.Contains id then pick () else id

    { Id = pick ()
      Kind = Note
      Text = ""
      Children = [] },
    r

let opGen: OpGen<Item, string> =
    { Tree = genTree
      FreshNode = genFresh
      CanHold = Some canHold } // F1: a leaf-bearing witness certifies via the container path

// ---- 3. a domain op + reducer + wire codec (the op-stream seam) ----

type DomainOp = SetText of id: string * text: string

let applyOp (SetText(id, text)) (tree: Item) : Result<Item, string> =
    match Tree.updateNode nodew idw id (fun n -> { n with Text = text }) tree with
    | Some t -> Ok t
    | None -> Error("no item " + id) // a rejection that names the failure

let encodeOp (SetText(id, text)) =
    Json.render (Json.kindObj "setText" [ "id", JStr id; "text", JStr text ])

let decodeOp (s: string) : Result<DomainOp, string> =
    Decode.parse s
    |> Result.bind (fun el ->
        Decoder.describing (Decoder.field "id" Decoder.str) el
        |> Result.bind (fun id ->
            Decoder.describing (Decoder.field "text" Decoder.str) el
            |> Result.map (fun t -> SetText(id, t))))

let streamW: StreamWitness<DomainOp, Item, string> =
    { Apply = applyOp
      Encode = encodeOp
      Decode = decodeOp }

let sampleTree =
    { Id = "root"
      Kind = Section
      Text = ""
      Children =
        [ { Id = "a"
            Kind = Section
            Text = ""
            Children = [] }
          { Id = "b"
            Kind = Note
            Text = ""
            Children = [] } ] }

let genDomainOp (rng: ConfRng.T) : DomainOp * ConfRng.T =
    let id, r1 = ConfRng.choose [ "root"; "a"; "b"; "ghost" ] rng
    let t, r2 = ConfRng.intBelow 3 r1
    SetText(id, sprintf "t%d" t), r2

// ---- 5. the invocable seams: an artifact with holes, a capability registry, a query registry ----
//
// Phase 254. The same domain, offered to a caller (a model, a UI) as something to INVOKE rather
// than to edit: a template outline whose `?name` notes are holes, a capability that fills them, and
// a query that reads notes back. Same shape as above — certify the seam at your own registry and
// body, then use exactly the dispatch path you certified.

/// A Note whose text is `?name` is a hole: an integer reading from 0 to 100. Its ADDRESS is the
/// id-path from the root (`report/temp`), which this witness mints; arguments are keyed by it.
let private holesOf (tree: Item) : HoleDecl list =
    let rec walk (prefix: string) (i: Item) : HoleDecl list =
        let addr = if prefix = "" then i.Id else prefix + "/" + i.Id

        let own: HoleDecl list =
            if i.Kind = Note && i.Text.StartsWith "?" then
                [ { Addr = addr
                    Name = i.Text.Substring 1
                    Kind = ValueHole(IntRange(0, 100)) } ]
            else
                []

        own @ List.collect (walk addr) i.Children

    walk "" tree

let artifactW: ArtifactWitness<Item, string> =
    { Tree = nodew
      IdW = idw
      Holes = holesOf
      Effect = fun _ -> Effect.pureDeterministic
      Bind =
        fun addr arg tree ->
            let id = addr.Substring(addr.LastIndexOf '/' + 1)

            match arg with
            | ValueArg v ->
                match Tree.updateNode nodew idw id (fun n -> { n with Text = v }) tree with
                | Some t -> Ok t
                | None -> Error("no item at " + addr)
            | SlotArg _ -> Error "this domain declares no slot holes" }

let template =
    { Id = "report"
      Kind = Section
      Text = "Reading"
      Children =
        [ { Id = "temp"
            Kind = Note
            Text = "?celsius"
            Children = [] } ] }

let celsiusAddr = "report/temp"
let fillId = "outline.fill"

/// The capability: the template's signature (its holes, their spaces, its effect), placed on a server.
let fillCapability =
    Capability.create fillId (Function.signature artifactW "fill-reading" template) Server

/// Default deny is this registry's shape: only what is registered here can be dispatched.
let capabilities =
    CapabilityRegistry.register fillCapability CapabilityRegistry.empty
    |> Result.defaultWith (fun e -> failwith (InvokeError.describe e))

/// The host's body. It runs only for a call the registry accepted. A reading above 90 goes to a
/// slower checker that has not answered yet — the PENDING outcome; anything else settles with the
/// filled note.
let fillBody (args: (string * string) list) : Deferred<string> =
    match args |> List.tryFind (fun (a, _) -> a = celsiusAddr) with
    | Some(_, v) when
        (match System.Int32.TryParse v with
         | true, n -> n > 90
         | _ -> false)
        ->
        Pending
    | _ ->
        let bound = args |> List.map (fun (a, v) -> a, ValueArg v) |> Map.ofList

        match Function.apply artifactW bound template with
        | Ok filled -> Ready(filled.Children |> List.map _.Text |> String.concat " ")
        | Error e -> Failed(sprintf "%A" e)

/// The calls a model could make — settled, pending and every refusal, including a hole's NAME
/// where its address belongs.
let genCall (rng: ConfRng.T) : (string * (string * string) list) * ConfRng.T =
    let pick, r1 = ConfRng.intBelow 6 rng
    let v, r2 = ConfRng.intBelow 91 r1

    let call =
        match pick with
        | 0 -> fillId, [ celsiusAddr, string v ] // settles
        | 1 -> fillId, [ celsiusAddr, string (91 + v % 10) ] // pending
        | 2 -> fillId, [ celsiusAddr, "500" ] // refused: out of the hole's space
        | 3 -> fillId, [ "celsius", "20" ] // refused: a hole's NAME is not its address
        | 4 -> fillId, [] // refused: the required hole is unbound
        | _ -> "outline.delete", [ celsiusAddr, "20" ] // refused: not registered

    call, r2

/// The body the capability seam laws run, per call: the arguments first, then the capability.
let capabilityBody (args: (string * string) list) (_: Capability) : Deferred<string> = fillBody args

let capabilitySeam: CapabilitySeamWitness<string> =
    { Registry = capabilities
      Dispatch = CapabilityRegistry.dispatch capabilities // the host path: delegate to Core's dispatch
      GenCall = genCall }

/// The query: the notes under a section, by the section's id. Parameters are keyed by NAME.
let notesQuery: Query =
    { Id = "outline.notes"
      Params =
        [ ({ Name = "section"
             Type = StringType
             Required = true }
          : QueryParam) ]
      ResultSchema = [ "text", StringType ]
      Effect = Effect.pureDeterministic
      Source = Ref "outline"
      TimeoutMs = None
      PageSize = None
      Where = []
      OrderBy = [] }

let queries =
    QueryRegistry.register notesQuery QueryRegistry.empty
    |> Result.defaultWith (fun e -> failwith (QueryError.describe e))

/// The resolver: a table of the declared schema, or PENDING for the archive, which is fetched
/// elsewhere and has not arrived.
let notesResolver (args: (string * Cell) list) (_: Query) : Deferred<QueryResult> =
    match args |> List.tryPick (fun (n, c) -> if n = "section" then Some c else None) with
    | Some(Str "archive") -> Pending
    | Some(Str section) ->
        let texts =
            match Tree.tryFind nodew idw section sampleTree with
            | Some s -> s.Children |> List.filter (fun c -> c.Kind = Note) |> List.map (fun c -> c.Text)
            | None -> []

        Ready
            { Rows =
                { Schema = [ "text", StringType ]
                  Columns = [ Column.ofStrs "text" (Vector.ofList texts) AllValid ] }
              PageNum = 0
              TotalRowCount = Some texts.Length
              NextPageToken = None }
    | _ -> Failed "no section was named"

let genQuery (rng: ConfRng.T) : (string * (string * Cell) list) * ConfRng.T =
    let pick, r1 = ConfRng.intBelow 6 rng
    let section, r2 = ConfRng.choose [ "root"; "a"; "ghost" ] r1

    let call =
        match pick with
        | 0 -> "outline.notes", [ "section", Str section ] // settles
        | 1 -> "outline.notes", [ "section", Str "archive" ] // pending
        | 2 -> "outline.notes", [ "section", Int 3 ] // refused: the wrong type
        | 3 -> "outline.notes", [] // refused: the required parameter is unbound
        | 4 -> "outline.notes", [ "section", Null ] // refused: bound to no value
        | _ -> "outline.drop", [ "section", Str section ] // refused: not registered

    call, r2

let querySeam: QuerySeamWitness =
    { Queries = queries
      Dispatch = QueryRegistry.dispatch queries
      GenQuery = genQuery }

let private outcome (r: Result<Deferred<'v>, string>) : string =
    match r with
    | Ok(Ready v) -> sprintf "settled: %A" v
    | Ok Pending -> "pending"
    | Ok(Failed m) -> "unreachable: " + m
    | Error why -> why // the refusal reads as one sentence: "Refused: ..."

// ---- 4. certify + demonstrate the op-stream (and, 5, the seams) ----

[<EntryPoint>]
let main _ =
    printfn "Fuaran.Core adoption sample — a tiny 'outline' domain (Section containers / Note leaves)\n"

    let laws =
        Conformance.witnessLaws nodew idw opGen 1 200 // 253: the witness is well-formed
        @ Conformance.opAlgebra nodew idw opGen 2 200 // 251: skeleton ops over the container subset
        @ Conformance.reducer
            applyOp
            { State0 = sampleTree
              Op = genDomainOp }
            None
            3
            200 // 254: the domain reducer

    for r in laws do
        printfn "  [%s] %s" (if r.Passed then "PASS" else "FAIL") r.Law

    // the op-stream re-expression: append → replay → verifyChain → portable JSONL round-trip
    let streamOk =
        let built =
            (Ok(sampleTree, OpStream.empty), [ SetText("a", "hello"); SetText("b", "world") ])
            ||> List.fold (fun acc op ->
                acc
                |> Result.bind (fun (st, recs) ->
                    OpStream.append OpStream.defaultHash streamW (Human "demo") op st recs))

        match built with
        | Ok(state, recs) ->
            let verified = OpStream.verifyChain OpStream.defaultHash streamW recs

            let roundTrips =
                match OpStream.toJsonl streamW recs |> OpStream.fromJsonl streamW with
                | Ok restored -> OpStream.replay streamW sampleTree restored = Ok state
                | Error _ -> false

            verified && roundTrips
        | Error _ -> false

    printfn
        "  [%s] op-stream re-expression (append / replay / verifyChain + portable JSONL)"
        (if streamOk then "PASS" else "FAIL")

    // the seams: certify at YOUR registry, body and host path, then dispatch through that same path
    let seamLaws =
        Conformance.capabilityLawsAt capabilitySeam capabilityBody 4 200
        @ Conformance.queryLawsAt querySeam notesResolver 5 200

    for r in seamLaws do
        printfn "  [%s] %s" (if r.Passed then "PASS" else "FAIL") r.Law

    printfn
        "\n  the capability's arguments are keyed by hole address: %s"
        (Json.render (Function.toJsonSchema fillCapability.Signature))

    printfn "  its invocation key for 20 degrees: %s" (Capability.invocationKey fillCapability [ celsiusAddr, "20" ])

    let call id args =
        CapabilityRegistry.dispatch capabilities id args (fun _ () -> fillBody args)
        |> Result.mapError InvokeError.describe
        |> outcome

    printfn "  fill 20      -> %s" (call fillId [ celsiusAddr, "20" ])
    printfn "  fill 95      -> %s" (call fillId [ celsiusAddr, "95" ])
    printfn "  fill by name -> %s" (call fillId [ "celsius", "20" ])

    let notes section =
        let args = [ "section", Str section ]

        QueryRegistry.dispatch queries "outline.notes" args (notesResolver args)
        |> Result.map (Deferred.map (fun r -> r.Rows.Columns |> List.collect Column.toCells))
        |> Result.mapError QueryError.describe
        |> outcome

    printfn "  notes(root)  -> %s" (notes "root")
    printfn "  notes(archive) -> %s" (notes "archive")

    let green =
        (laws |> List.forall _.Passed) && streamOk && (seamLaws |> List.forall _.Passed)

    printfn "\nconformance: %s" (if green then "GREEN" else "FAILED")
    if green then 0 else 1
