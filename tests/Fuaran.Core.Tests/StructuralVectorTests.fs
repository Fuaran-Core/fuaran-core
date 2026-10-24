module Fuaran.Core.Tests.StructuralVectorTests

open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.StructuralVectorExport

// ---------------------------------------------------------------------------
//  The four structural families over the skeleton witness (Phase 549): `chain/`, `dag/`,
//  `merge/` and `diff/` under `conformance/`. Their freshness (each committed file is what its
//  exporter row renders) is the `Exporters` family's; this family asks the rest:
//
//   1. Every committed vector is CORE'S ANSWER TO ITS OWN INPUT: read back from the file as a host
//      reads it, re-answered by calling Core, compared whole. An input that does not carry what its
//      answer needs is red here, not at a host.
//   2. The go-red: a forged expectation is named by vector id.
//   3. Each vector holds its family's LAW, checked by a route other than the one that answered it:
//      a recorded chain hash is recomputed from its pre-image; a recorded node id from its parents,
//      actor and op; a merge is the same tree under every arrival order of its heads; a diff
//      script, applied, lands on its after tree.
//   4. The sample is ADEQUATE: every outcome each family can reach over the witness is reached,
//      and the outcomes it cannot reach are named, so a later reader knows what is NOT pinned.
//   5. Two renders are the same bytes, with no CR.
// ---------------------------------------------------------------------------

let private committed (f: Family) : string =
    File.ReadAllText(vectorsPath f (OwnedConformance.root ()))

let private parsed (f: Family) : (string * string * JVal * JVal) list =
    match parseVectors (committed f) with
    | Ok vs -> vs
    | Error why -> failtestf "the committed %s vectors do not read: %s" f.Name why

let private family (name: string) : Family =
    tryFamily name
    |> Option.defaultWith (fun () -> failtestf "no structural family `%s`" name)

let private field (name: string) (v: JVal) : JVal =
    match v with
    | JObj ms ->
        ms
        |> List.tryPick (fun (k, x) -> if k = name then Some x else None)
        |> Option.defaultWith (fun () -> failtestf "no member `%s` in %A" name v)
    | _ -> failtestf "not an object: %A" v

let private str (name: string) (v: JVal) : string =
    match field name v with
    | JStr s -> s
    | other -> failtestf "`%s` is not a string: %A" name other

let private int' (name: string) (v: JVal) : int =
    match field name v with
    | JInt n -> n
    | other -> failtestf "`%s` is not an integer: %A" name other

let private arr (name: string) (v: JVal) : JVal list =
    match field name v with
    | JArr xs -> xs
    | other -> failtestf "`%s` is not an array: %A" name other

let private ok (r: Result<'a, string>) : 'a =
    match r with
    | Ok x -> x
    | Error why -> failtest why

let private has (name: string) (v: JVal) : bool =
    match v with
    | JObj ms -> ms |> List.exists (fun (k, _) -> k = name)
    | _ -> false

let private verdictOf (expected: JVal) : string = str "verdict" expected

/// A tagged value's tag: a string as it is, an object's `$type` (else its first member).
let private tag (v: JVal) : string =
    match v with
    | JStr s -> s
    | JObj ms ->
        match ms |> List.tryFind (fun (k, _) -> k = "$type") with
        | Some(_, JStr t) -> t
        | _ -> fst ms.Head
    | other -> sprintf "%A" other

/// The ids of the committed vectors of `f` a host answering through `answer` disagrees with: the
/// family's own reading (`checkFile`) with the answer replaced, so a planted defect is judged exactly
/// as Core's own answer is.
let private caughtBy (f: Family) (answer: string -> JVal -> Result<JVal, string>) : string list =
    checkFile { f with Answer = answer } (committed f)
    |> List.map (fun finding -> finding.Substring(f.Name.Length + 1).Split(':').[0])
    |> List.distinct

/// An object with one member's value replaced through `edit`, every other member as it is.
let private editMember (name: string) (edit: JVal -> JVal) (v: JVal) : JVal =
    match v with
    | JObj ms -> JObj(ms |> List.map (fun (k, x) -> if k = name then k, edit x else k, x))
    | other -> other

let rec private permutations (xs: 'a list) : 'a list list =
    match xs with
    | [] -> [ [] ]
    | _ ->
        [ for i in 0 .. xs.Length - 1 do
              let x = xs[i]
              let rest = xs |> List.removeAt i

              for p in permutations rest do
                  x :: p ]

let private chainLaws () =
    for id, case, input, expected in parsed (family "chain") do
        let anchor = ok (Chain.readAnchor input)

        match case with
        | "append" ->
            // The law: each record's hash is sha256(prevHash | payload), its payload the canonical
            // pre-image of its own entry, and each record links to the one before it.
            let entries = arr "entries" input
            let records = arr "records" expected
            Expect.equal records.Length entries.Length (id + ": one record per entry")

            records
            |> List.iteri (fun i r ->
                let prev = str "prevHash" r
                let payload = str "payload" r
                let entry = entries[i]
                let actor = ok (decodeActor (str "actor" entry))
                Expect.equal (int' "seq" r) (anchor.Seq + i) (sprintf "%s: record %d's seq" id i)

                Expect.equal
                    payload
                    (StreamConfig.canonical.Payload (anchor.Seq + i) actor (str "op" entry))
                    (sprintf "%s: record %d's payload is its entry's canonical pre-image" id i)

                Expect.equal
                    (str "hash" r)
                    (Hash.sha256Hex (prev + "|" + payload))
                    (sprintf "%s: record %d's hash recomputes" id i)

                let expectedPrev = if i = 0 then anchor.PrevHash else str "hash" records[i - 1]

                Expect.equal prev expectedPrev (sprintf "%s: record %d links to its predecessor" id i))

            let tip = field "tip" expected
            Expect.equal (int' "seq" tip) (anchor.Seq + records.Length) (id + ": the tip's seq")
        | "walk" ->
            // The law: from the genesis anchor, `intact` is exactly what the op stream's own verifier
            // answers over the same records.
            let records = arr "records" input |> List.map (Chain.readRecord >> ok)

            if anchor = Anchor.genesis StreamConfig.canonical then
                let opaque =
                    OpStream.canonical
                        hashFn
                        { Apply = (fun (_: string) (s: unit) -> Ok s)
                          Encode = (fun (op: string) -> op)
                          Decode = Ok }

                Expect.equal
                    (verdictOf expected = "intact")
                    (OpStream.verifyChain opaque records)
                    (id + ": the walk agrees with OpStream.verifyChain")
        | other -> failtestf "%s: unknown case %s" id other

let private nodeIdLaw (id: string) (input: JVal) (expected: JVal) =
    let _, writes = ok (readScenario input)
    let outcomes = arr "writes" expected
    let mutable ids: Map<string, string> = Map.empty

    for wr, outcome in List.zip writes outcomes do
        if has "id" outcome then
            let parents =
                wr.Parents
                |> List.map (fun p -> if p.StartsWith "@" then ids[p.Substring 1] else p)
                |> fun ps -> if wr.Verb = "mergeAll" then List.distinct ps else ps

            Expect.equal
                (str "id" outcome)
                (Dag.nodeId skeleton parents wr.Actor wr.Op)
                (sprintf "%s: write %s's id is Dag.nodeId of its parents, actor and op" id wr.Label)

            ids <- Map.add wr.Label (str "id" outcome) ids

    ids

let private dagLaws () =
    for id, case, input, expected in parsed (family "dag") do
        match case with
        | "build" ->
            let ids = nodeIdLaw id input expected
            let _, writes = ok (readScenario input)
            let b = ok (build writes)
            Expect.isTrue (Dag.verifyDag skeleton b.Dag) (id + ": the built DAG verifies")

            // Order: every node after each parent; ranks: 0 at a root, else one past the deepest parent.
            let order =
                arr "order" expected
                |> List.map (fun v ->
                    ids[(match v with
                         | JStr s -> s
                         | _ -> failtest "a label")])

            let position = order |> List.mapi (fun i n -> n, i) |> Map.ofList

            let ranks =
                match field "ranks" expected with
                | JObj ms ->
                    ms
                    |> List.map (fun (l, r) ->
                        ids[l],
                        (match r with
                         | JInt n -> n
                         | _ -> failtest "a rank"))
                    |> Map.ofList
                | _ -> failtest "ranks is an object"

            for nodeId in order do
                let parents = b.Dag.Nodes[nodeId].Parents

                for p in parents do
                    Expect.isLessThan position[p] position[nodeId] (id + ": a parent precedes its child")

                let expectedRank =
                    if parents.IsEmpty then
                        0
                    else
                        1 + (parents |> List.map (fun p -> ranks[p]) |> List.max)

                Expect.equal ranks[nodeId] expectedRank (id + ": a rank is a Lamport depth")
        | "write" ->
            // The law: a write's id is Dag.nodeId of its parents, actor and op, and a contentIdCollision
            // names exactly that id, which the vector's held nodes record for other content.
            let held = arr "nodes" input |> List.map (fun n -> str "id" n, n) |> Map.ofList

            let mutable ids: Map<string, string> = Map.empty

            for wr, outcome in List.zip (arr "writes" input) (arr "writes" expected) do
                let label = str "label" wr
                let actor = ok (decodeActor (str "actor" wr))
                let op = ok (ApplyVectorExport.decodeOp (str "op" wr))

                let parents =
                    arr "parents" wr
                    |> List.map (function
                        | JStr p when p.StartsWith "@" -> ids[p.Substring 1]
                        | JStr p -> p
                        | _ -> failtest "a parent")
                    |> fun ps -> if str "verb" wr = "mergeAll" then List.distinct ps else ps

                let computed = Dag.nodeId skeleton parents actor op

                if has "id" outcome then
                    Expect.equal (str "id" outcome) computed (sprintf "%s: write %s's id is Dag.nodeId" id label)
                    ids <- Map.add label computed ids
                else
                    let refused = ok (Json.parse (str "refused" outcome))

                    if tag refused = "contentIdCollision" then
                        Expect.equal
                            (str "nodeId" refused)
                            computed
                            (sprintf "%s: write %s's collision names its id" id label)

                        let holder = held[computed]

                        Expect.notEqual
                            (str "op" holder, str "actor" holder)
                            (ApplyVectorExport.encodeOp op, Actor.encode actor)
                            (sprintf "%s: the node held under %s is other content" id computed)
        | "verify" ->
            // The law: a vector is intact exactly when every node hashes to its id and every parent
            // is held - recomputed here node by node, without the walker.
            let nodes = arr "nodes" input
            let held = nodes |> List.map (str "id") |> Set.ofList

            let sound =
                nodes
                |> List.forall (fun n ->
                    let parents =
                        arr "parents" n
                        |> List.map (function
                            | JStr s -> s
                            | _ -> failtest "a parent id")

                    let actor = ok (decodeActor (str "actor" n))
                    let op = ok (ApplyVectorExport.decodeOp (str "op" n))

                    str "id" n = Dag.nodeId skeleton parents actor op
                    && parents |> List.forall held.Contains)

            Expect.equal (verdictOf expected = "intact") sound (id + ": intact exactly when every node is sound")
        | other -> failtestf "%s: unknown case %s" id other

let private mergeLaws () =
    for id, _, input, expected in parsed (family "merge") do
        let state0, writes = ok (readScenario input)
        let b = ok (build writes)

        let heads =
            arr "heads" input
            |> List.map (function
                | JStr l -> b.Ids[l]
                | _ -> failtest "a head label")

        match verdictOf expected with
        | "merged" ->
            // Confluence: under every arrival order of the distinct heads, the script lands on the
            // same tree.
            let baseId = b.Ids[str "base" expected]

            let baseState =
                match Dag.tryReplayTo skeleton.Witness state0 b.Dag baseId with
                | Ok s -> s
                | Error f -> failtestf "%s: the base does not replay: %A" id f

            for order in permutations (List.distinct heads) do
                match Dag.reconcileMany skeleton.Witness footprintOf b.Dag baseId baseState order with
                | Error f -> failtestf "%s: an arrival order refused: %A" id f
                | Ok script ->
                    match Merge.landOn baseState script with
                    | Error r -> failtestf "%s: an arrival order's script does not land: %A" id r
                    | Ok t ->
                        Expect.equal
                            (ApplyVectorExport.encodeTree t)
                            (str "tree" expected)
                            (id + ": every arrival order lands on the recorded tree")
        | "interfere" ->
            // Symmetry: the heads reversed report the same conflicts, each with its sides swapped.
            let baseId = b.Ids[str "base" expected]

            let key (left: JVal) (right: JVal) (rest: JVal) =
                let s =
                    function
                    | JStr x -> x
                    | other -> sprintf "%A" other

                set [ s left; s right ], rest

            let recorded =
                arr "conflicts" expected
                |> List.map (fun c ->
                    key (field "left" c) (field "right" c) (JArr [ field "address" c; field "shape" c ]))
                |> Set.ofList

            match Dag.reconcileMany skeleton.Witness footprintOf b.Dag baseId state0 (List.rev heads) with
            | Error(ReconcileFault.LanesInterfere cs) ->
                let reversed =
                    cs
                    |> List.map (fun c ->
                        let j = Merge.conflictJ c
                        key (field "left" j) (field "right" j) (JArr [ field "address" j; field "shape" j ]))
                    |> Set.ofList

                Expect.equal reversed recorded (id + ": the interference is symmetric in the heads")
            | other -> failtestf "%s: the reversed heads did not interfere: %A" id other
        | _ -> ()

let private diffLaws () =
    for id, _, input, expected in parsed (family "diff") do
        if verdictOf expected = "diff" then
            let before = ok (ApplyVectorExport.decodeTree (str "before" input))

            let script =
                arr "script" expected
                |> List.map (function
                    | JStr s -> ok (ApplyVectorExport.decodeOp s)
                    | _ -> failtest "an op")

            match Merge.landOn before script with
            | Error r -> failtestf "%s: the diff script does not apply: %A" id r
            | Ok t ->
                Expect.equal
                    (ApplyVectorExport.encodeTree t)
                    (str "after" input)
                    (id + ": the script, applied to before, lands on after")

/// Every value one member takes across a family's committed expectations.
let private reached (f: Family) (pick: JVal -> string list) : Set<string> =
    parsed f |> List.collect (fun (_, _, _, e) -> pick e) |> Set.ofList

[<Tests>]
let tests =
    testList
        "StructuralVectors"
        [ for f in families do
              yield
                  testCase (sprintf "each committed %s vector is Core's answer to its own input" f.Name)
                  <| fun _ ->
                      let found = checkFile f (committed f)
                      Expect.isEmpty found (String.concat "\n" found)

              yield
                  testCase (sprintf "go-red: a forged %s expectation is named by vector id" f.Name)
                  <| fun _ ->
                      let vectors = f.Vectors()
                      let victim = vectors.Head

                      let forged =
                          CorpusFile.render
                              { Head = [ "family", CorpusFile.str f.FamilyId ]
                                Items = "vectors"
                                Rows =
                                  vectors
                                  |> List.map (fun v ->
                                      CorpusCell.Row
                                          [ "id", CorpusFile.str v.Id
                                            "case", CorpusFile.str v.Case
                                            "description", CorpusFile.str v.Description
                                            "input", CorpusCell.Value v.Input
                                            "expected",
                                            CorpusCell.Value(
                                                if v.Id = victim.Id then
                                                    JObj [ "verdict", JStr "forged" ]
                                                else
                                                    expectationOf f v
                                            ) ]) }

                      match checkFile f forged with
                      | [ one ] -> Expect.stringContains one (f.Name + "/" + victim.Id) "the forged vector is named"
                      | other -> failtestf "expected exactly the forged vector to be named, got %A" other

              yield
                  testCase (sprintf "two renders of %s are the same bytes, with no CR" f.Name)
                  <| fun _ ->
                      for render in [ (fun () -> renderVectors f); (fun () -> renderManifest f) ] do
                          let once = render ()
                          Expect.equal (render ()) once "two renders, one text"
                          Expect.isFalse (once.Contains "\r") "no CR reaches a file hosts byte-compare"

          yield
              testCase
                  "chain: each record hashes its pre-image and links to the one before; a walk agrees with OpStream.verifyChain"
              <| fun _ -> chainLaws ()
          yield
              testCase
                  "dag: each id is Dag.nodeId of its write; the order is topological and the ranks Lamport depths; verify agrees with a recomputation"
              <| fun _ -> dagLaws ()
          yield
              testCase
                  "merge: a merge lands on the same tree under every arrival order; an interference is symmetric in the heads"
              <| fun _ -> mergeLaws ()
          yield
              testCase "diff: each script, applied to its before tree, lands on its after tree"
              <| fun _ -> diffLaws ()

          yield
              testCase
                  "adequacy: every outcome reachable over the witness is reached, and the unreachable ones are named"
              <| fun _ ->
                  let verdicts (name: string) =
                      reached (family name) (fun e -> if has "verdict" e then [ verdictOf e ] else [])

                  Expect.equal
                      (reached (family "chain") (fun e -> if has "reason" e then [ str "reason" e ] else []))
                      (set
                          [ ChainBreakReason.toString ChainBreakReason.SequenceMismatch
                            ChainBreakReason.toString ChainBreakReason.PrevHashLinkBroken
                            ChainBreakReason.toString ChainBreakReason.HashMismatch ])
                      "chain: every break reason a walk mints"

                  Expect.equal (verdicts "chain") (set [ "intact"; "break" ]) "chain: both walk verdicts"

                  // dag: every write refusal; `contentIdCollision` is reached by a write on a held, forged
                  // node (Phase 568), since a write from an empty DAG reaches it only by a SHA-256 collision.
                  Expect.equal
                      (reached (family "dag") (fun e ->
                          if has "writes" e then
                              arr "writes" e
                              |> List.choose (fun w ->
                                  if has "refused" w then
                                      Some(tag (ok (Json.parse (str "refused" w))))
                                  else
                                      None)
                          else
                              []))
                      (set [ "unknownParent"; "commaInParentId"; "emptyParentId"; "contentIdCollision" ])
                      "dag: every write refusal"

                  Expect.equal
                      (reached (family "dag") (fun e -> if has "reason" e then [ str "reason" e ] else []))
                      (set
                          [ DagBreakReason.toString DagBreakReason.ContentIdMismatch
                            DagBreakReason.toString DagBreakReason.MissingParent ])
                      "dag: both break reasons"

                  Expect.equal
                      (reached (family "dag") (fun e ->
                          if has "replays" e then
                              arr "replays" e |> List.map verdictOf
                          else
                              []))
                      (set [ "accept"; "reject" ])
                      "dag: a replay accepts and refuses"

                  // merge: every verdict but `unappliable`, which a merged script cannot reach (the lanes
                  // commute and each applies), and every conflict shape but `slotClash` (no skeleton op
                  // has a slot footprint).
                  Expect.equal
                      (verdicts "merge")
                      (set [ "merged"; "interfere"; "sharedRejected"; "lanesRejected"; "noBase" ])
                      "merge: every verdict reachable"

                  Expect.equal
                      (reached (family "merge") (fun e ->
                          if has "conflicts" e then
                              arr "conflicts" e |> List.map (fun c -> tag (field "shape" c))
                          else
                              []))
                      (set [ "concurrentUpdate"; "insertPositionClash"; "moveVsRemove" ])
                      "merge: every conflict shape a skeleton op can raise"

                  // diff: every change kind but `changed`, which needs content beyond the kind tag, and
                  // the envelope carries none.
                  Expect.equal
                      (reached (family "diff") (fun e ->
                          if has "changes" e then
                              arr "changes" e |> List.map (fun c -> tag (field "kind" c))
                          else
                              []))
                      (set [ "added"; "removed"; "moved"; "kindChanged"; "reordered" ])
                      "diff: every change kind the envelope can show"

                  Expect.equal (verdicts "diff") (set [ "diff"; "refuse" ]) "diff: both verdicts"

          // Phase 568 — the planted defects. Each is a host defect the spine ports found the families
          // could not see, planted here on Core's own answer exactly as a host would carry it, and each
          // must fail the vector that pins it.
          yield
              testCase "go-red: a host that spells contentIdCollision's id member `id` fails the dag family"
              <| fun _ ->
                  let dag = family "dag"

                  let respell (refused: JVal) =
                      match refused with
                      | JStr text ->
                          match ok (Json.parse text) with
                          | JObj ms when tag (JObj ms) = "contentIdCollision" ->
                              JStr(
                                  Canonical.write
                                      Rendering.CrossHost
                                      (JObj(ms |> List.map (fun (k, x) -> (if k = "nodeId" then "id" else k), x)))
                              )
                          | _ -> refused
                      | other -> other

                  let mutant case input =
                      dag.Answer case input
                      |> Result.map (
                          editMember "writes" (function
                              | JArr ws -> JArr(ws |> List.map (editMember "refused" respell))
                              | other -> other)
                      )

                  let caught = caughtBy dag mutant
                  printfn "the `id` spelling is caught by: %A" caught
                  Expect.contains caught "write-content-id-collision" "the collision vector pins the member's name"

          yield
              testCase
                  "go-red: a host that orders the rejected lanes by reversing, keeping or labelling the heads fails the merge family"
              <| fun _ ->
                  let merge = family "merge"

                  // Each planted host reports the same rejected lanes in its own order: the heads as
                  // named, reversed; as named; or by the head's label.
                  let reorder (rank: JVal -> string list -> string -> int) (input: JVal) (expected: JVal) =
                      let heads =
                          arr "heads" input
                          |> List.map (function
                              | JStr l -> l
                              | _ -> failtest "a head label")
                          |> List.distinct

                      if has "verdict" expected && verdictOf expected = "lanesRejected" then
                          editMember
                              "lanes"
                              (function
                              | JArr lanes -> JArr(lanes |> List.sortBy (fun l -> rank input heads (str "head" l)))
                              | other -> other)
                              expected
                      else
                          expected

                  let hosts =
                      [ "reversal", (fun _ (heads: string list) h -> -(List.findIndex ((=) h) heads))
                        "the heads' order", (fun _ (heads: string list) h -> List.findIndex ((=) h) heads)
                        "label order",
                        (fun _ (heads: string list) h ->
                            List.findIndex
                                ((=) h)
                                (List.sortWith (fun a b -> System.String.CompareOrdinal(a, b)) heads)) ]

                  for name, rank in hosts do
                      let caught =
                          caughtBy merge (fun case input -> merge.Answer case input |> Result.map (reorder rank input))

                      printfn "a host ordering the rejected lanes by %s is caught by: %A" name caught

                      Expect.contains
                          caught
                          "lanes-rejected-id-order"
                          (sprintf "the id-order vector pins the sort against %s" name)

          yield
              testCase "the four families are rows of the exporter table, one each"
              <| fun _ ->
                  let rows = Exporters.tableWith (fun () -> []) |> List.map _.Family

                  for f in families do
                      Expect.contains rows f.Name (sprintf "the `%s` family is a row" f.Name) ]
