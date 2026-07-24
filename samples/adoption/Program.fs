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
        Decode.strField "id" el
        |> Result.bind (fun id -> Decode.strField "text" el |> Result.map (fun t -> SetText(id, t))))

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

// ---- 4. certify + demonstrate the op-stream ----

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

    let green = (laws |> List.forall (fun r -> r.Passed)) && streamOk
    printfn "\nconformance: %s" (if green then "GREEN" else "FAILED")
    if green then 0 else 1
