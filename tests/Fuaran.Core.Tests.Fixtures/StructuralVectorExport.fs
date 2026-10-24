namespace Fuaran.Core.Tests

// ============================================================================
//  The structural families over the skeleton witness - Phase 549.
//
//  WHAT WAS MISSING. The shared corpus's `chain/`, `dag/`, `merge-conformance/` and `diff/`
//  families exercise Core's mechanisms, but every vector in them embeds bytes only a UI-tier
//  codec writes (DECISIONS.md D191), so a spine with no UI dependency had nothing Core-owned to
//  certify its chain walk, its DAG ids and fold, its reconcile or its tree diff against.
//
//  WHAT THESE FAMILIES ARE. Four fixed vector sets, one directory each under `conformance/`, each
//  a row of the exporter table (`Exporters.fs`) with its own self-enumerated manifest:
//
//    chain/skeleton-chain.json   the chain over OPAQUE payloads: the op is a string the chain
//                                splices into its `{seq, actor, op}` pre-image and never reads, so
//                                a host certifies the chain with no op codec at all;
//    dag/skeleton-dag.json       the DAG's content ids, its write refusals, heads, whole-DAG order,
//                                Lamport ranks, the fold (replay to each head and of the whole
//                                DAG) and the integrity walk, over skeleton ops;
//    merge/skeleton-merge.json   the common base and the N-lane reconcile (`Dag.reconcileMany`
//                                under the pure footprint `Ops.footprint`): the merge script and the
//                                state it lands on, or the fault, every one of the three;
//    diff/skeleton-diff.json     the content-aware tree diff (`Diff.toOpsWith` and `Diff.changes`
//                                over the witness envelope): the script and the classification, or
//                                the refusal.
//
//  ── decisions worth reading before changing anything here (DECISIONS.md, the Phase 549 entry) ──
//
//  1. ONE WITNESS. The ops are the skeleton ops, encoded exactly as the `apply/` family encodes
//     them (`ApplyVectorExport.encodeOp`), over the same tree envelope (`{"children","id","kind"}`),
//     applied by `Ops.apply` with no policy. A host that has adopted `apply/` holds every codec these
//     families need; the chain family needs none.
//
//  2. EVERY ANSWER IS A FUNCTION OF ITS VECTOR'S INPUT. `answer family case input` decodes the
//     input as a host would and calls Core; the exporter renders exactly that, and the suite
//     re-answers every committed vector from the file's own input (`StructuralVectorTests`). So an
//     input that does not carry what its answer needs fails the suite, not a host.
//
//  3. BYTES A HOST HASHES ARE STRINGS. An op, an actor and a tree appear as JSON strings holding
//     their canonical text, never as nested JSON, because the hash is over those exact bytes.
//
//  4. NO `kitVersion` STAMP, as for `apply/`: these are the mechanisms' semantics, recomputed on
//     every run, not one kit's sampled answers; a version move cannot redden them.
//
//  5. SHA-256 (`OpStream.sha256Hash`), the canonical profile (`StreamConfig.canonical`). The
//     FNV-1a default is a Fable-clean convenience whose 32-bit space collides in practice; the
//     cross-host profile is the digest.
// ============================================================================

module StructuralVectorExport =

    open System.IO
    open Fuaran.Core
    open Fuaran.Core.Tests.Reference

    // -----------------------------------------------------------------------
    //  reading an input as a host would
    // -----------------------------------------------------------------------

    type private ResultBuilder() =
        member _.Bind(r: Result<'a, string>, f: 'a -> Result<'b, string>) = Result.bind f r
        member _.Return(x: 'a) : Result<'a, string> = Ok x
        member _.ReturnFrom(r: Result<'a, string>) = r

    let private result = ResultBuilder()

    /// Every item mapped, or the first error, in order.
    let private traverse (f: 'a -> Result<'b, string>) (xs: 'a list) : Result<'b list, string> =
        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | x :: rest ->
                match f x with
                | Ok y -> go (y :: acc) rest
                | Error e -> Error e

        go [] xs

    let private field (name: string) (v: JVal) : JVal option =
        match v with
        | JObj ms -> ms |> List.tryPick (fun (k, x) -> if k = name then Some x else None)
        | _ -> None

    let private req (name: string) (v: JVal) : Result<JVal, string> =
        match field name v with
        | Some x -> Ok x
        | None -> Error(sprintf "the member `%s` is missing" name)

    let private reqStr (name: string) (v: JVal) : Result<string, string> =
        match field name v with
        | Some(JStr s) -> Ok s
        | _ -> Error(sprintf "the member `%s` is not a string" name)

    let private reqInt (name: string) (v: JVal) : Result<int, string> =
        match field name v with
        | Some(JInt n) -> Ok n
        | _ -> Error(sprintf "the member `%s` is not an integer" name)

    let private reqArr (name: string) (v: JVal) : Result<JVal list, string> =
        match field name v with
        | Some(JArr xs) -> Ok xs
        | _ -> Error(sprintf "the member `%s` is not an array" name)

    let private strs (xs: JVal list) : Result<string list, string> =
        xs
        |> traverse (function
            | JStr s -> Ok s
            | other -> Error(sprintf "expected a string, read %A" other))

    let private jstrs (xs: string list) : JVal = JArr(xs |> List.map JStr)

    /// An actor from its canonical text (`Actor.encode`).
    let decodeActor (text: string) : Result<Actor, string> =
        result {
            let! v = Json.parse text

            match! reqStr "kind" v with
            | "human" ->
                let! id = reqStr "id" v
                return Human id
            | "agent" ->
                let! model = reqStr "model" v
                let! version = reqStr "version" v
                let! id = reqStr "id" v
                return Agent(model, version, id)
            | other -> return! Error(sprintf "the actor kind `%s` is not human or agent" other)
        }

    let private canonical (v: JVal) : string = Canonical.write Rendering.CrossHost v

    // -----------------------------------------------------------------------
    //  the two witnesses
    // -----------------------------------------------------------------------

    /// The digest every structural family is computed under.
    let hashFn: HashFn = OpStream.sha256Hash

    /// The opaque witness: the op IS its text, which the chain splices into its pre-image unread.
    let private opaque: Stream<string, unit, string> =
        OpStream.canonical
            hashFn
            { Apply = fun _ s -> Ok s
              Encode = id
              Decode = Ok }

    let private scope = OpScope.plain nodew idw

    /// The skeleton witness: skeleton ops over the witness envelope, applied with no policy, encoded
    /// as the `apply/` family encodes them.
    let skeleton: Stream<SkeletonOp<RNode, string>, RNode, Rejection<string>> =
        OpStream.canonical
            hashFn
            { Apply = fun op t -> Ops.apply Policy.none scope op t
              Encode = ApplyVectorExport.encodeOp
              Decode = ApplyVectorExport.decodeOp }

    /// The pure footprint of one op, the projection `Dag.reconcileMany` is asked under.
    let footprintOf (op: SkeletonOp<RNode, string>) : Footprint = Ops.footprint Policy.none scope [ op ]

    let private treeAnswer (t: RNode) : (string * JVal) list =
        let bytes = ApplyVectorExport.encodeTree t
        [ "tree", JStr bytes; "hash", JStr(ApplyVectorExport.hashOf bytes) ]

    let private rejection (r: Rejection<string>) : JVal =
        JStr(canonical (ApplyVectorExport.rejectionJ r))

    let private node id kind children = RNode.node id kind children

    /// `root(doc)[ a(section)[a1,a2], b(section)[b1] ]` - the `apply/` family's base tree.
    let baseTree () : RNode =
        node
            "root"
            "doc"
            [ node "a" "section" [ node "a1" "para" []; node "a2" "para" [] ]
              node "b" "section" [ node "b1" "para" [] ] ]

    let private alice = Human "alice"
    let private bob = Human "bob"
    let private agent = Agent("model-x", "2026-10", "agent-7")

    /// One authored vector: its id, its case tag, what it pins, and its input.
    type Vector =
        { Id: string
          Case: string
          Description: string
          Input: JVal }

    // =======================================================================
    //  chain/ - the chain walk over opaque payloads
    // =======================================================================

    module Chain =

        let familyDirName = "chain"
        let vectorsFileName = "skeleton-chain.json"
        let familyId = "skeletonChain"

        let private shape: ChainShape<OpRecord<string>> =
            ChainShape.records StreamConfig.canonical id

        let private anchorJ (a: Anchor) : JVal =
            JObj [ "seq", JInt a.Seq; "prevHash", JStr a.PrevHash ]

        let internal readAnchor (input: JVal) : Result<Anchor, string> =
            result {
                let! a = req "anchor" input
                let! seq = reqInt "seq" a
                let! prev = reqStr "prevHash" a
                return { Seq = seq; PrevHash = prev }
            }

        /// The records `entries` chain to, hung from `anchor` - `ChainShape.append`, one entry at a time.
        let appended (anchor: Anchor) (entries: (Actor * string) list) : OpRecord<string> list =
            entries
            |> List.fold
                (fun records (actor, op) ->
                    ChainShape.append
                        shape
                        hashFn
                        anchor
                        (fun n ->
                            { Seq = n
                              Actor = actor
                              Op = op
                              PrevHash = ""
                              Hash = "" })
                        records)
                []

        let private recordJ (r: OpRecord<string>) : JVal =
            JObj
                [ "seq", JInt r.Seq
                  "actor", JStr(Actor.encode r.Actor)
                  "op", JStr r.Op
                  "prevHash", JStr r.PrevHash
                  "hash", JStr r.Hash ]

        let internal readRecord (v: JVal) : Result<OpRecord<string>, string> =
            result {
                let! seq = reqInt "seq" v
                let! actorText = reqStr "actor" v
                let! actor = decodeActor actorText
                let! op = reqStr "op" v
                let! prev = reqStr "prevHash" v
                let! hash = reqStr "hash" v

                return
                    { Seq = seq
                      Actor = actor
                      Op = op
                      PrevHash = prev
                      Hash = hash }
            }

        /// The answer to an `append` or a `walk` vector, computed by calling Core.
        let answer (case: string) (input: JVal) : Result<JVal, string> =
            result {
                let! anchor = readAnchor input

                match case with
                | "append" ->
                    let! entries = reqArr "entries" input

                    let! pairs =
                        entries
                        |> traverse (fun e ->
                            result {
                                let! actorText = reqStr "actor" e
                                let! actor = decodeActor actorText
                                let! op = reqStr "op" e
                                return actor, op
                            })

                    let records = appended anchor pairs

                    let rows =
                        records
                        |> List.map (fun r ->
                            JObj
                                [ "seq", JInt r.Seq
                                  "prevHash", JStr r.PrevHash
                                  "payload", JStr(StreamConfig.canonical.Payload r.Seq r.Actor r.Op)
                                  "hash", JStr r.Hash ])

                    return JObj [ "records", JArr rows; "tip", anchorJ (ChainShape.tip shape anchor records) ]
                | "walk" ->
                    let! items = reqArr "records" input
                    let! records = items |> traverse readRecord

                    match ChainShape.walk shape hashFn anchor records with
                    | None ->
                        return
                            JObj
                                [ "verdict", JStr "intact"
                                  "tip", anchorJ (ChainShape.tip shape anchor records) ]
                    | Some b ->
                        return
                            JObj
                                [ "verdict", JStr "break"
                                  "index", JInt b.Index
                                  "reason", JStr(ChainBreakReason.toString b.Reason)
                                  "expected", JStr b.Expected
                                  "got", JStr b.Got ]
                | other -> return! Error(sprintf "the chain family has no case `%s`" other)
            }

        /// The chain model's answer to a vector (Phase 563), read from the same input: an actor is
        /// its canonical text, which the model splices unread, as it does an op.
        let modelAnswer (v: Vector) : VectorOracle.Answer option =
            let answered =
                result {
                    let! anchor = readAnchor v.Input

                    match v.Case with
                    | "append" ->
                        let! entries = reqArr "entries" v.Input

                        let! pairs =
                            entries
                            |> traverse (fun e ->
                                result {
                                    let! actor = reqStr "actor" e
                                    let! op = reqStr "op" e
                                    return actor, op
                                })

                        return ChainOracle.append hashFn anchor.Seq anchor.PrevHash pairs
                    | "walk" ->
                        let! items = reqArr "records" v.Input

                        let! records =
                            items
                            |> traverse (fun r ->
                                result {
                                    let! seq = reqInt "seq" r
                                    let! actor = reqStr "actor" r
                                    let! op = reqStr "op" r
                                    let! prev = reqStr "prevHash" r
                                    let! hash = reqStr "hash" r
                                    return seq, actor, op, prev, hash
                                })

                        return ChainOracle.walk hashFn anchor.Seq anchor.PrevHash records
                    | other -> return! Error(sprintf "the chain family has no case `%s`" other)
                }

            match answered with
            | Ok answer ->
                Some
                    { Model = ChainOracle.Model
                      LadderRow = ChainOracle.LadderRow
                      Cell = CorpusCell.Value answer }
            | Error why -> failwithf "chain vector %s: the model's input does not read: %s" v.Id why

        let private appendInput (anchor: Anchor) (entries: (Actor * string) list) : JVal =
            JObj
                [ "anchor", anchorJ anchor
                  "entries",
                  JArr(
                      entries
                      |> List.map (fun (a, op) -> JObj [ "actor", JStr(Actor.encode a); "op", JStr op ])
                  ) ]

        let private walkInput (anchor: Anchor) (records: OpRecord<string> list) : JVal =
            JObj [ "anchor", anchorJ anchor; "records", JArr(records |> List.map recordJ) ]

        let private genesis = Anchor.genesis StreamConfig.canonical

        let private three =
            [ alice, ApplyVectorExport.encodeOp (RemoveNode "a1")
              agent, "[1,2,3]"
              bob, "\"plain text\"" ]

        /// A tail kept above a snapshot: five records before it, the last hashing to this.
        let private snapshotAnchor =
            { Seq = 5
              PrevHash = hashFn "" "a snapshot boundary" }

        let private tail = [ alice, "{\"n\":5}"; alice, "{\"n\":6}" ]

        let vectors () : Vector list =
            let chained = appended genesis three
            let anchored = appended snapshotAnchor tail

            let at i (f: OpRecord<string> -> OpRecord<string>) =
                chained |> List.mapi (fun j r -> if i = j then f r else r)

            let append id description anchor entries =
                { Id = id
                  Case = "append"
                  Description = description
                  Input = appendInput anchor entries }

            let walk id description anchor records =
                { Id = id
                  Case = "walk"
                  Description = description
                  Input = walkInput anchor records }

            [ append "append-nothing" "No entry chains no record; the tip is the genesis anchor." genesis []
              append "append-one" "One record at sequence 0, linked to the empty genesis hash." genesis [ alice, "{}" ]
              append
                  "append-three-opaque"
                  "Three records by three actors, a human and an agent among them. The ops are opaque text: a skeleton op, an array and a string are all spliced into the pre-image unread."
                  genesis
                  three
              append
                  "append-utf8"
                  "An op carrying non-ASCII text: the pre-image is hashed over its UTF-8 bytes."
                  genesis
                  [ alice, "{\"name\":\"Zoë été \U0001F332\"}" ]
              append
                  "append-anchored"
                  "A tail hung from a snapshot boundary: the first record carries the anchor's sequence and links to its hash."
                  snapshotAnchor
                  tail
              walk "walk-intact" "The appended chain walks intact; the tip is its last record." genesis chained
              walk "walk-empty" "An empty run is intact; its tip is its anchor." genesis []
              walk
                  "walk-truncated"
                  "A prefix of a chain is a chain: a walk cannot see a truncated tail, only a head can."
                  genesis
                  (List.take 2 chained)
              walk
                  "walk-tampered-op"
                  "An op rewritten after chaining breaks its record's hash."
                  genesis
                  (at 1 (fun r -> { r with Op = "[1,2,4]" }))
              walk
                  "walk-tampered-actor"
                  "A record re-attributed after chaining breaks its hash: the actor is folded into the pre-image."
                  genesis
                  (at 2 (fun r -> { r with Actor = alice }))
              walk
                  "walk-broken-link"
                  "A record whose prevHash names no predecessor breaks the link before the hash is checked."
                  genesis
                  (at 1 (fun r ->
                      { r with
                          PrevHash = hashFn "" "elsewhere" }))
              walk
                  "walk-forged-hash"
                  "A record whose hash is not its own digest is a hash break."
                  genesis
                  (at 2 (fun r ->
                      { r with
                          Hash = hashFn r.PrevHash "forged" }))
              walk
                  "walk-gap"
                  "A dropped record is a sequence break at the record after the gap."
                  genesis
                  (chained |> List.removeAt 1)
              walk
                  "walk-reordered"
                  "Two records swapped is a sequence break at the first of them."
                  genesis
                  [ chained[0]; chained[2]; chained[1] ]
              walk "walk-anchored-intact" "A tail walks intact from the anchor it hangs from." snapshotAnchor anchored
              walk
                  "walk-anchored-from-genesis"
                  "The same tail walked from the genesis anchor breaks at its first record's sequence."
                  genesis
                  anchored ]

        let description =
            "The op-stream chain over OPAQUE payloads (Phase 549), under the canonical profile (the {seq, actor, op} pre-image, the empty genesis) and SHA-256: hash = sha256(prevHash + \"|\" + payload), lower-case hex. "
            + "Every op is a string the chain splices into its pre-image unread, so a host certifies the chain with no op codec; an actor is its canonical text. "
            + "An `append` vector chains `input.entries` from `input.anchor` and expects each record's seq, prevHash, payload (the pre-image, exactly) and hash, and the tip the next record hangs from. "
            + "A `walk` vector walks `input.records` from `input.anchor` (sequence, then prev-link, then hash, first fault reported) and expects `intact` with the tip, or `break` with the index in the run, the reason's canonical spelling, and the expected and got values. "
            + "Every expectation is computed by calling Core (ChainShape.append, ChainShape.walk, ChainShape.tip); there is no kitVersion stamp."

    // =======================================================================
    //  the DAG scenarios the dag/ and merge/ families share
    // =======================================================================

    /// One authored write of a scenario: its label, its verb, its parents (`@label` names an earlier
    /// write's node; anything else is a literal id), its actor and its op.
    type Write =
        { Label: string
          Verb: string
          Parents: string list
          Actor: Actor
          Op: SkeletonOp<RNode, string> }

    let private writeJ (w: Write) : JVal =
        JObj
            [ "label", JStr w.Label
              "verb", JStr w.Verb
              "parents", jstrs w.Parents
              "actor", JStr(Actor.encode w.Actor)
              "op", JStr(ApplyVectorExport.encodeOp w.Op) ]

    let private readWrite (v: JVal) : Result<Write, string> =
        result {
            let! label = reqStr "label" v
            let! verb = reqStr "verb" v
            let! parentsJ = reqArr "parents" v
            let! parents = strs parentsJ
            let! actorText = reqStr "actor" v
            let! actor = decodeActor actorText
            let! opText = reqStr "op" v
            let! op = ApplyVectorExport.decodeOp opText

            return
                { Label = label
                  Verb = verb
                  Parents = parents
                  Actor = actor
                  Op = op }
        }

    let private faultJ (f: DagAppendFault) : JVal =
        match f with
        | DagAppendFault.EmptyParentId -> Canonical.typed "emptyParentId" []
        | DagAppendFault.CommaInParentId p -> Canonical.typed "commaInParentId" [ "parentId", JStr p ]
        | DagAppendFault.UnknownParent p -> Canonical.typed "unknownParent" [ "parentId", JStr p ]
        | DagAppendFault.ContentIdCollision id -> Canonical.typed "contentIdCollision" [ "nodeId", JStr id ]

    /// A scenario built: the DAG, each write's outcome in order, and every label's node id.
    type Built =
        { Dag: Dag.T<SkeletonOp<RNode, string>>
          Outcomes: (string * Result<string, DagAppendFault>) list
          Ids: Map<string, string> }

        /// The label a node id was FIRST written under (a deduplicated write names an earlier node).
        member b.LabelOf(id: string) : string =
            b.Outcomes
            |> List.tryPick (fun (label, r) ->
                match r with
                | Ok i when i = id -> Some label
                | _ -> None)
            |> Option.defaultValue id

    /// Build a scenario by calling the DAG's writes, in order. A refused write changes nothing.
    let build (writes: Write list) : Result<Built, string> =
        writes
        |> List.fold
            (fun (acc: Result<Built, string>) w ->
                result {
                    let! b = acc

                    let! parents =
                        w.Parents
                        |> traverse (fun p ->
                            if p.StartsWith "@" then
                                match Map.tryFind (p.Substring 1) b.Ids with
                                | Some id -> Ok id
                                | None ->
                                    Error(
                                        sprintf "the write `%s` names `%s`, which no earlier write recorded" w.Label p
                                    )
                            else
                                Ok p)

                    let written =
                        match w.Verb with
                        | "appendOn" -> Ok(Dag.appendOn skeleton w.Actor w.Op parents b.Dag)
                        | "mergeAll" -> Ok(Dag.mergeAll skeleton w.Actor w.Op parents b.Dag)
                        | other -> Error(sprintf "the write `%s` has no verb `%s`" w.Label other)

                    match! written with
                    | Ok wr ->
                        return
                            { Dag = wr.Dag
                              Outcomes = b.Outcomes @ [ w.Label, Ok wr.Id ]
                              Ids = Map.add w.Label wr.Id b.Ids }
                    | Error f ->
                        return
                            { b with
                                Outcomes = b.Outcomes @ [ w.Label, Error f ] }
                })
            (Ok
                { Dag = Dag.empty
                  Outcomes = []
                  Ids = Map.empty })

    let internal readScenario (input: JVal) : Result<RNode * Write list, string> =
        result {
            let! stateText = reqStr "state0" input
            let! state0 = ApplyVectorExport.decodeTree stateText
            let! ws = reqArr "writes" input
            let! writes = ws |> traverse readWrite
            return state0, writes
        }

    let private scenarioInput (state0: RNode) (writes: Write list) (extra: (string * JVal) list) : JVal =
        JObj(
            [ "state0", JStr(ApplyVectorExport.encodeTree state0)
              "writes", JArr(writes |> List.map writeJ) ]
            @ extra
        )

    let private replayAnswer (b: Built) (r: Result<RNode, string * Rejection<string>>) : (string * JVal) list =
        match r with
        | Ok t -> ("verdict", JStr "accept") :: treeAnswer t
        | Error(nodeId, rej) ->
            [ "verdict", JStr "reject"
              "node", JStr(b.LabelOf nodeId)
              "rejection", rejection rej ]

    let private w label verb parents actor op =
        { Label = label
          Verb = verb
          Parents = parents
          Actor = actor
          Op = op }

    // =======================================================================
    //  dag/ - content ids, write refusals, order, ranks, the fold and the integrity walk
    // =======================================================================

    module DagFamily =

        let familyDirName = "dag"
        let vectorsFileName = "skeleton-dag.json"
        let familyId = "skeletonDag"

        let private nodeJ (n: DagNode<SkeletonOp<RNode, string>>) : JVal =
            JObj
                [ "id", JStr n.Id
                  "parents", jstrs n.Parents
                  "actor", JStr(Actor.encode n.Actor)
                  "op", JStr(ApplyVectorExport.encodeOp n.Op) ]

        let private readNode (v: JVal) : Result<DagNode<SkeletonOp<RNode, string>>, string> =
            result {
                let! id = reqStr "id" v
                let! parentsJ = reqArr "parents" v
                let! parents = strs parentsJ
                let! actorText = reqStr "actor" v
                let! actor = decodeActor actorText
                let! opText = reqStr "op" v
                let! op = ApplyVectorExport.decodeOp opText

                return
                    { Id = id
                      Parents = parents
                      Actor = actor
                      Op = op }
            }

        let private writesAnswer (b: Built) : JVal =
            JArr(
                b.Outcomes
                |> List.map (fun (label, r) ->
                    match r with
                    | Ok id -> JObj [ "label", JStr label; "id", JStr id ]
                    | Error f -> JObj [ "label", JStr label; "refused", JStr(canonical (faultJ f)) ])
            )

        /// The answer to a `build` or a `verify` vector, computed by calling Core.
        let answer (case: string) (input: JVal) : Result<JVal, string> =
            result {
                match case with
                | "build" ->
                    let! state0, writes = readScenario input
                    let! b = build writes
                    let labels (ids: string list) = jstrs (ids |> List.map b.LabelOf)

                    let! order =
                        Dag.totalOrderBy (fun _ -> 0) b.Dag
                        |> Result.mapError (fun c -> sprintf "the scenario is cyclic: %A" c.Unplaced)

                    let! ranks = Dag.ranks b.Dag |> Result.mapError (fun c -> sprintf "cyclic: %A" c.Unplaced)
                    let heads = Dag.heads b.Dag

                    let replays =
                        heads
                        |> List.map (fun h ->
                            let r =
                                Dag.tryReplayTo skeleton.Witness state0 b.Dag h
                                |> Result.mapError (function
                                    | Dag.ReplayFault.Rejected(n, rej) -> n, rej
                                    | other -> failwithf "a built scenario replays only to a rejection, not %A" other)

                            JObj(("head", JStr(b.LabelOf h)) :: replayAnswer b r))

                    let all =
                        Dag.replayAllBy (fun _ -> 0) skeleton.Witness state0 b.Dag
                        |> Result.mapError (function
                            | Dag.ReplayAllFault.Rejected(n, rej) -> n, rej
                            | other -> failwithf "a built scenario replays only to a rejection, not %A" other)

                    return
                        JObj
                            [ "writes", writesAnswer b
                              "heads", labels heads
                              "order", labels order
                              "ranks", JObj(order |> List.map (fun id -> b.LabelOf id, JInt ranks[id]))
                              "replays", JArr replays
                              "replayAll", JObj(replayAnswer b all) ]
                | "verify" ->
                    let! items = reqArr "nodes" input
                    let! nodes = items |> traverse readNode

                    let! dag =
                        nodes
                        |> List.map (fun n -> n.Id, n)
                        |> Map.ofList
                        |> Dag.ofNodes
                        |> Result.mapError (fun k -> sprintf "the node map is keyed wrongly at %s" k.Key)

                    match Dag.firstBreak skeleton dag with
                    | None -> return JObj [ "verdict", JStr "intact" ]
                    | Some b ->
                        return
                            JObj
                                [ "verdict", JStr "break"
                                  "node", JStr b.NodeId
                                  "reason", JStr(DagBreakReason.toString b.Reason)
                                  "expected", JStr b.Expected
                                  "got", JStr b.Got ]
                | other -> return! Error(sprintf "the dag family has no case `%s`" other)
            }

        /// The models' answer to a vector (Phase 563). A `verify` vector is the DAG walker's model's
        /// (`Chain.first_break`). A `build` vector is the reference's alone: its answer carries the heads
        /// and the ranks, which no model states (`Dag.heads`, `Dag.ranks`), beside the content ids, the
        /// order and the replays, so no model answers the whole of it.
        let modelAnswer (v: Vector) : VectorOracle.Answer option =
            match v.Case with
            | "verify" ->
                let answered =
                    result {
                        let! items = reqArr "nodes" v.Input
                        let! nodes = items |> traverse readNode

                        let! dag =
                            nodes
                            |> List.map (fun n -> n.Id, n)
                            |> Map.ofList
                            |> Dag.ofNodes
                            |> Result.mapError (fun k -> sprintf "the node map is keyed wrongly at %s" k.Key)

                        return ChainOracle.verify hashFn ApplyVectorExport.encodeOp dag
                    }

                match answered with
                | Ok answer ->
                    Some
                        { Model = ChainOracle.Model
                          LadderRow = ChainOracle.LadderRow
                          Cell = CorpusCell.Value answer }
                | Error why -> failwithf "dag vector %s: the model's input does not read: %s" v.Id why
            | _ -> None

        /// Fork, cross-actor writes and a converging merge, on the base tree.
        let forkAndMerge =
            [ w "g" "appendOn" [] alice (InsertChild("b", node "n1" "para" []))
              w "x" "appendOn" [ "@g" ] alice (InsertChild("a", node "x1" "para" []))
              w "y" "appendOn" [ "@g" ] bob (RemoveNode "b1")
              w "m" "mergeAll" [ "@y"; "@x" ] agent (Batch []) ]

        let vectors () : Vector list =
            let t = baseTree ()

            let buildCase id description writes =
                { Id = id
                  Case = "build"
                  Description = description
                  Input = scenarioInput t writes [] }

            let built =
                match build forkAndMerge with
                | Ok b -> b
                | Error e -> failwith e

            let nodes =
                Dag.totalOrderBy (fun _ -> 0) built.Dag
                |> Result.defaultValue []
                |> List.map (fun id -> built.Dag.Nodes[id])

            let verifyCase id description (ns: DagNode<SkeletonOp<RNode, string>> list) =
                { Id = id
                  Case = "verify"
                  Description = description
                  Input = JObj [ "nodes", JArr(ns |> List.map nodeJ) ] }

            let tamper i f =
                nodes |> List.mapi (fun j n -> if i = j then f n else n)

            [ buildCase
                  "build-fork-and-merge"
                  "A genesis write, two writers forking off it, and an agent's mergeAll of the two heads (named in reverse; the parents are sorted). Each node's id is sha256(sorted parents joined by \",\" + \"|\" + actor + \"|\" + op)."
                  forkAndMerge
              buildCase
                  "build-two-heads"
                  "A fork left unmerged: two heads, each replayed alone, and the whole DAG replayed in its total order."
                  (List.take 3 forkAndMerge)
              buildCase
                  "build-parent-order-free"
                  "appendOn over the same two parents in either order records ONE node: the id is a function of the parent set; the stored parents keep the first write's order."
                  [ yield! List.take 3 forkAndMerge
                    w "p" "appendOn" [ "@x"; "@y" ] alice (Batch [])
                    w "q" "appendOn" [ "@y"; "@x" ] alice (Batch []) ]
              buildCase
                  "build-dedup-and-actor"
                  "The same op by the same actor on the same parent is the same node; by another actor it is another node. Two roots: each head replays alone, and the whole DAG replayed refuses the second removal of the same id."
                  [ w "g" "appendOn" [] alice (RemoveNode "a2")
                    w "g2" "appendOn" [] alice (RemoveNode "a2")
                    w "h" "appendOn" [] bob (RemoveNode "a2") ]
              buildCase
                  "build-refusals"
                  "The write refusals, each judged before anything is added: a parent the DAG does not hold, a parent carrying the pre-image's separator, and the genesis marker named as a parent."
                  [ w "g" "appendOn" [] alice (RemoveNode "a2")
                    w "unknown" "appendOn" [ "no-such-node" ] alice (RemoveNode "a1")
                    w "comma" "appendOn" [ "@g"; "x,y" ] alice (RemoveNode "a1")
                    w "empty" "mergeAll" [ "@g"; "" ] alice (RemoveNode "a1") ]
              buildCase
                  "build-replay-rejected"
                  "The DAG records an op it never applies; the replay of the head refuses at the first node the domain rejects, naming it."
                  [ w "g" "appendOn" [] alice (RemoveNode "a2")
                    w "bad" "appendOn" [ "@g" ] alice (RemoveNode "a2")
                    w "after" "appendOn" [ "@bad" ] alice (RemoveNode "b1") ]
              buildCase
                  "build-deep-ranks"
                  "Ranks are Lamport depths: a merge of a long and a short branch ranks one past the long one."
                  [ w "g" "appendOn" [] alice (InsertChild("a", node "c1" "para" []))
                    w "l1" "appendOn" [ "@g" ] alice (InsertChild("a", node "c2" "para" []))
                    w "l2" "appendOn" [ "@l1" ] alice (InsertChild("a", node "c3" "para" []))
                    w "s1" "appendOn" [ "@g" ] bob (InsertChild("b", node "c4" "para" []))
                    w "m" "mergeAll" [ "@l2"; "@s1" ] bob (Batch []) ]
              verifyCase "verify-intact" "The nodes of build-fork-and-merge verify." nodes
              verifyCase
                  "verify-tampered-op"
                  "A node whose op was rewritten no longer hashes to its id."
                  (tamper 1 (fun n -> { n with Op = RemoveNode "a1" }))
              verifyCase
                  "verify-tampered-actor"
                  "A node re-attributed no longer hashes to its id."
                  (tamper 2 (fun n -> { n with Actor = agent }))
              verifyCase
                  "verify-missing-parent"
                  "A node whose parent is absent from the DAG is a missing-parent break."
                  (nodes |> List.removeAt 0) ]

        let description =
            "The op-DAG over the skeleton witness (Phase 549), under the canonical profile and SHA-256: a node's id is sha256(the parent ids sorted ordinally and joined by \",\" + \"|\" + the actor's canonical text + \"|\" + the op's canonical text). "
            + "The ops are the skeleton ops in the apply/ family's encoding, over the apply/ family's tree envelope; a state is a tree. "
            + "A `build` vector runs `input.writes` in order from an empty DAG (verb appendOn: the parents as given, [] for genesis; verb mergeAll: the parents deduplicated and sorted; a parent `@label` is the node an earlier write recorded under that label, anything else a literal id) and expects each write's id or its refusal, the heads, the whole-DAG order (Kahn's drain, smallest id first among the ready nodes), each node's rank (0 for a root, else one past its deepest parent), each head replayed from `input.state0` and the whole DAG replayed in that order (accept with the tree's canonical bytes and sha256, or reject naming the first rejecting node). "
            + "Nodes are named by the label of the write that first recorded them. "
            + "A `verify` vector walks `input.nodes` in the same drain order and expects `intact` or the first `break` (content-id mismatch, then missing parent). "
            + "Every expectation is computed by calling Core (Dag.appendOn, mergeAll, heads, totalOrderBy, ranks, tryReplayTo, replayAllBy, firstBreak); there is no kitVersion stamp."

    // =======================================================================
    //  merge/ - the common base and the N-lane reconcile
    // =======================================================================

    module Merge =

        let familyDirName = "merge"
        let vectorsFileName = "skeleton-merge.json"
        let familyId = "skeletonMerge"

        let private shapeJ (s: MergeConflictShape) : JVal =
            match s with
            | MergeConflictShape.ConcurrentUpdate -> JStr "concurrentUpdate"
            | MergeConflictShape.InsertPositionClash -> JStr "insertPositionClash"
            | MergeConflictShape.MoveVsRemove -> JStr "moveVsRemove"
            | MergeConflictShape.SlotClash slot -> JObj [ "slotClash", JStr slot ]

        let conflictJ (c: MergeConflict<SkeletonOp<RNode, string>>) : JVal =
            JObj
                [ "left", JStr(ApplyVectorExport.encodeOp c.Left)
                  "right", JStr(ApplyVectorExport.encodeOp c.Right)
                  "address", JStr c.Address
                  "shape", shapeJ c.Shape ]

        /// The script folded from `state` by the witness's own apply.
        let landOn (state: RNode) (script: SkeletonOp<RNode, string> list) : Result<RNode, Rejection<string>> =
            script
            |> List.fold (fun acc op -> Result.bind (skeleton.Witness.Apply op) acc) (Ok state)

        /// The answer to a `reconcile` vector, computed by calling Core.
        let answer (case: string) (input: JVal) : Result<JVal, string> =
            result {
                match case with
                | "reconcile" ->
                    let! state0, writes = readScenario input
                    let! b = build writes
                    let! headsJ = reqArr "heads" input
                    let! headLabels = strs headsJ

                    let! heads =
                        headLabels
                        |> traverse (fun l ->
                            match Map.tryFind l b.Ids with
                            | Some id -> Ok id
                            | None -> Error(sprintf "the head `%s` names no recorded write" l))

                    let common = Dag.commonBase b.Dag heads

                    let! named =
                        match field "base" input with
                        | Some(JStr l) ->
                            match Map.tryFind l b.Ids with
                            | Some id -> Ok(Some id)
                            | None -> Error(sprintf "the base `%s` names no recorded write" l)
                        | Some other -> Error(sprintf "the member `base` is not a string: %A" other)
                        | None -> Ok None

                    let ids =
                        "ids",
                        JObj(
                            b.Outcomes
                            |> List.choose (fun (l, r) -> r |> Result.toOption |> Option.map (fun id -> l, JStr id))
                        )

                    let commonJ =
                        "commonBase",
                        (match common with
                         | Some c -> JStr(b.LabelOf c)
                         | None -> JStr "")

                    match
                        (match named with
                         | Some n -> Some n
                         | None -> common)
                    with
                    | None -> return JObj [ ids; commonJ; "verdict", JStr "noBase" ]
                    | Some baseId ->
                        let! baseState =
                            match Dag.tryReplayTo skeleton.Witness state0 b.Dag baseId with
                            | Ok s -> Ok s
                            | Error f -> Error(sprintf "the base does not replay: %A" f)

                        let label = b.LabelOf

                        let outcome =
                            match Dag.reconcileMany skeleton.Witness footprintOf b.Dag baseId baseState heads with
                            | Ok script ->
                                match landOn baseState script with
                                | Ok t ->
                                    [ "verdict", JStr "merged"
                                      "script", jstrs (script |> List.map ApplyVectorExport.encodeOp) ]
                                    @ treeAnswer t
                                | Error r -> [ "verdict", JStr "unappliable"; "rejection", rejection r ]
                            | Error(ReconcileFault.LanesInterfere cs) ->
                                [ "verdict", JStr "interfere"; "conflicts", JArr(cs |> List.map conflictJ) ]
                            | Error(ReconcileFault.SharedHistoryRejected(n, rej)) ->
                                [ "verdict", JStr "sharedRejected"
                                  "node", JStr(label n)
                                  "rejection", rejection rej ]
                            | Error(ReconcileFault.LanesRejected lanes) ->
                                [ "verdict", JStr "lanesRejected"
                                  "lanes",
                                  JArr(
                                      lanes
                                      |> List.map (fun l ->
                                          JObj
                                              [ "head", JStr(label l.Head)
                                                "node", JStr(label l.NodeId)
                                                "rejection", rejection l.Reject ])
                                  ) ]

                        return JObj([ ids; commonJ; "base", JStr(label baseId) ] @ outcome)
                | other -> return! Error(sprintf "the merge family has no case `%s`" other)
            }

        let vectors () : Vector list =
            let t = baseTree ()

            let case id description writes heads baseLabel =
                { Id = id
                  Case = "reconcile"
                  Description = description
                  Input =
                    scenarioInput
                        t
                        writes
                        ([ "heads", jstrs heads ]
                         @ (match baseLabel with
                            | Some l -> [ "base", JStr l ]
                            | None -> [])) }

            let g = w "g" "appendOn" [] alice (InsertChild("root", node "c" "section" []))

            let on parent label actor op =
                w label "appendOn" [ "@" + parent ] actor op

            [ case
                  "clean-two-lanes"
                  "Two lanes off one base insert under different parents: their footprints are independent, so the script is lane A's delta then lane B's, and it lands."
                  [ g
                    on "g" "x" alice (InsertChild("a", node "x1" "para" []))
                    on "g" "y" bob (InsertChild("b", node "y1" "para" [])) ]
                  [ "x"; "y" ]
                  None
              case
                  "clean-heads-reversed"
                  "The same lanes named in the other order: the script follows the order the heads are named in, and lands on the same tree."
                  [ g
                    on "g" "x" alice (InsertChild("a", node "x1" "para" []))
                    on "g" "y" bob (InsertChild("b", node "y1" "para" [])) ]
                  [ "y"; "x" ]
                  None
              case
                  "clean-three-lanes"
                  "Three lanes, every pair independent: one script over all three, lane by lane."
                  [ g
                    on "g" "x" alice (InsertChild("a", node "x1" "para" []))
                    on "g" "y" bob (InsertChild("b", node "y1" "para" []))
                    on "g" "z" agent (InsertChild("c", node "z1" "para" [])) ]
                  [ "x"; "y"; "z" ]
                  None
              case
                  "insert-position-clash"
                  "Both lanes insert under the same parent: an insert-position clash at that parent, nothing applied."
                  [ g
                    on "g" "x" alice (InsertChild("a", node "x1" "para" []))
                    on "g" "y" bob (InsertChild("a", node "y1" "para" [])) ]
                  [ "x"; "y" ]
                  None
              case
                  "concurrent-update"
                  "Both lanes rewrite the same node: a concurrent update at it."
                  [ g
                    on "g" "x" alice (UpdateNode(node "a1" "quote" []))
                    on "g" "y" bob (UpdateNode(node "a1" "code" [])) ]
                  [ "x"; "y" ]
                  None
              case
                  "move-vs-remove"
                  "A removal races a structural write on the other lane: reported against the removed id, conservatively, though these two would commute."
                  [ g
                    on "g" "x" alice (RemoveNode "b1")
                    on "g" "y" bob (InsertChild("a", node "y1" "para" [])) ]
                  [ "x"; "y" ]
                  None
              case
                  "fast-forward"
                  "One head descends from the other: the shared history is applied once and nothing conflicts with itself."
                  [ g
                    on "g" "x" alice (InsertChild("a", node "x1" "para" []))
                    on "x" "x2" alice (InsertChild("a", node "x2" "para" [])) ]
                  [ "x"; "x2" ]
                  None
              case
                  "same-head-twice"
                  "A head named twice is one lane."
                  [ g; on "g" "x" alice (InsertChild("a", node "x1" "para" [])) ]
                  [ "x"; "x" ]
                  None
              case
                  "named-base"
                  "The base is named rather than computed: the lanes' shared node above it is history both hold, applied once, first."
                  [ g
                    on "g" "s" alice (InsertChild("c", node "s1" "para" []))
                    on "s" "x" alice (InsertChild("a", node "x1" "para" []))
                    on "s" "y" bob (InsertChild("b", node "y1" "para" [])) ]
                  [ "x"; "y" ]
                  (Some "g")
              case
                  "shared-history-rejected"
                  "The shared region above the named base does not replay: no lane is to blame, so the node is named and no lane is."
                  [ g
                    on "g" "s" alice (RemoveNode "no-such-node")
                    on "s" "x" alice (InsertChild("a", node "x1" "para" []))
                    on "s" "y" bob (InsertChild("b", node "y1" "para" [])) ]
                  [ "x"; "y" ]
                  (Some "g")
              case
                  "lanes-rejected"
                  "Each lane is replayed on its own from the shared state; every lane that does not apply is named, sorted by head id, whatever order the heads came in."
                  [ g
                    on "g" "x" alice (InsertChild("no-such-parent", node "x1" "para" []))
                    on "g" "y" bob (InsertChild("b", node "y1" "para" []))
                    on "g" "z" agent (InsertChild("another-missing-parent", node "z1" "para" [])) ]
                  [ "z"; "y"; "x" ]
                  None
              case
                  "no-common-base"
                  "Two roots share no ancestor: there is no base to reconcile over."
                  [ g; w "h" "appendOn" [] bob (InsertChild("root", node "d" "section" [])) ]
                  [ "g"; "h" ]
                  None ]

        let description =
            "Branch reconciliation over the skeleton witness (Phase 549): the dag/ family's scenario (same writes, same ids, same SHA-256 profile), then `input.heads` reconciled. "
            + "The base is `input.base` when named, else the heads' common base (the deepest common ancestor: the most ancestors, the larger id on a tie); `commonBase` is always reported, \"\" when there is none, and with no base the verdict is `noBase`. "
            + "The lanes are Dag.reconcileMany's: the region above the base partitioned into SHARED nodes (held by two or more heads, applied once, first) and each distinct head's EXCLUSIVE delta, in the whole drain's order; every unordered pair of exclusive deltas is checked under the pure footprint of each op (Ops.footprint, no policy); the shared region is replayed from the base's state; each lane is replayed alone from there. "
            + "The verdict is `merged` with the script (shared, then each lane in the order the heads are named) and the tree it lands on with its sha256, or `interfere` with every conflict (left op, right op, address, shape), or `sharedRejected` with the node, or `lanesRejected` with every lane that does not apply, sorted by head id. "
            + "Every expectation is computed by calling Core; there is no kitVersion stamp. Slot-granular conflicts (`slotClash`) cannot arise here: a skeleton op has no slot footprint."

    // =======================================================================
    //  diff/ - the content-aware skeleton diff
    // =======================================================================

    module DiffFamily =

        let familyDirName = "diff"
        let vectorsFileName = "skeleton-diff.json"
        let familyId = "skeletonDiff"

        let private kindJ (k: Diff.ChangeKind<string>) : JVal =
            match k with
            | Diff.ChangeKind.Added -> Canonical.typed "added" []
            | Diff.ChangeKind.Removed -> Canonical.typed "removed" []
            | Diff.ChangeKind.Moved(f, t) -> Canonical.typed "moved" [ "from", JStr f; "to", JStr t ]
            | Diff.ChangeKind.KindChanged(f, t) -> Canonical.typed "kindChanged" [ "from", JStr f; "to", JStr t ]
            | Diff.ChangeKind.Changed -> Canonical.typed "changed" []
            | Diff.ChangeKind.Reordered -> Canonical.typed "reordered" []

        /// The answer to a `diff` vector, computed by calling Core.
        let answer (case: string) (input: JVal) : Result<JVal, string> =
            result {
                match case with
                | "diff" ->
                    let! beforeText = reqStr "before" input
                    let! afterText = reqStr "after" input
                    let! before = ApplyVectorExport.decodeTree beforeText
                    let! after = ApplyVectorExport.decodeTree afterText
                    let encode = ApplyVectorExport.encodeTree

                    match
                        Diff.toOpsWith encode Policy.none scope before after,
                        Diff.changes encode Policy.none scope before after
                    with
                    | Ok script, Ok changes ->
                        return
                            JObj
                                [ "verdict", JStr "diff"
                                  "script", jstrs (script |> List.map ApplyVectorExport.encodeOp)
                                  "changes",
                                  JArr(changes |> List.map (fun c -> JObj [ "id", JStr c.Id; "kind", kindJ c.Kind ])) ]
                    | Error r, _ -> return JObj [ "verdict", JStr "refuse"; "rejection", rejection r ]
                    | Ok _, Error r ->
                        return! Error(sprintf "the classification refused where the script did not: %A" r)
                | other -> return! Error(sprintf "the diff family has no case `%s`" other)
            }

        let vectors () : Vector list =
            let t = baseTree ()
            let enc = ApplyVectorExport.encodeTree

            let case id description (before: RNode) (after: RNode) =
                { Id = id
                  Case = "diff"
                  Description = description
                  Input = JObj [ "before", JStr(enc before); "after", JStr(enc after) ] }

            let tree a b = node "root" "doc" [ a; b ]
            let a kids = node "a" "section" kids
            let b kids = node "b" "section" kids
            let p id = node id "para" []

            [ case "identity" "A tree diffed against itself is the empty script and no change." t t
              case
                  "add-leaf"
                  "A new leaf is one InsertChild under its parent."
                  t
                  (tree (a [ p "a1"; p "a2"; p "a3" ]) (b [ p "b1" ]))
              case
                  "add-subtree"
                  "A new subtree grafts whole, in one InsertChild; every id in it is added."
                  t
                  (node "root" "doc" [ a [ p "a1"; p "a2" ]; b [ p "b1" ]; node "c" "section" [ p "c1"; p "c2" ] ])
              case "remove-leaf" "A vanished leaf is one RemoveNode." t (tree (a [ p "a1" ]) (b [ p "b1" ]))
              case
                  "remove-subtree"
                  "A vanished subtree is removed at its top; every id below it is classified removed."
                  t
                  (node "root" "doc" [ a [ p "a1"; p "a2" ] ])
              case "move" "A survivor under a new parent is a MoveNode." t (tree (a [ p "a1" ]) (b [ p "b1"; p "a2" ]))
              case
                  "reorder"
                  "Kept children in a new order are one ReorderChildren of their parent."
                  t
                  (tree (a [ p "a2"; p "a1" ]) (b [ p "b1" ]))
              case
                  "kind-change"
                  "A survivor whose kind changed is an UpdateNode carrying its new shell, classified kindChanged."
                  t
                  (tree (a [ node "a1" "quote" []; p "a2" ]) (b [ p "b1" ]))
              case
                  "move-and-kind-change"
                  "A survivor that moved and changed kind carries both: the move and the update."
                  t
                  (tree (a [ p "a1" ]) (b [ p "b1"; node "a2" "quote" [] ]))
              case
                  "move-into-new-node"
                  "A survivor moved under a node the after tree adds: the graft comes first, then the move."
                  t
                  (node "root" "doc" [ a [ p "a2" ]; b [ p "b1" ]; node "c" "section" [ p "a1" ] ])
              case
                  "insert-before-kept"
                  "A child added before a kept one: the graft appends, so a reorder restates the order."
                  t
                  (tree (a [ p "n"; p "a1"; p "a2" ]) (b [ p "b1" ]))
              case
                  "combined"
                  "An add, a removal, a move and a kind change in one diff."
                  t
                  (tree (node "a" "group" [ p "a2"; p "b1" ]) (b [ p "x" ]))
              case
                  "refuse-root-id"
                  "Two trees whose roots differ share no id space at the root: refused."
                  t
                  (node "other" "doc" [])
              case
                  "refuse-duplicate-id"
                  "An after tree holding an id twice is not a tree: refused."
                  t
                  (tree (a [ p "a1"; p "a2" ]) (b [ p "a1" ])) ]

        let description =
            "The content-aware structural diff over the skeleton witness (Phase 549): for `input.before` and `input.after` (the apply/ family's tree envelope, one id space) the skeleton-op script that turns one into the other, and the per-id classification read off the same run. "
            + "A node's content is its shell, the envelope with no children, so a kind change is an UpdateNode. "
            + "The verdict is `diff` with `script` (each op in the apply/ family's encoding, in emission order) and `changes` (ascending id, then added, removed, moved, kindChanged, changed, reordered), or `refuse` with the rejection in the apply/ family's projection. "
            + "The script, applied to `before` by the apply engine, lands on `after`. "
            + "Every expectation is computed by calling Core (Diff.toOpsWith and Diff.changes, no policy, the plain scope); there is no kitVersion stamp. A keyed scope is not exercised: the witness envelope declares no keyed position."

    // -----------------------------------------------------------------------
    //  the families as one table of their own, and the writing
    // -----------------------------------------------------------------------

    /// One structural family: where it lives, what it pins, its vectors and its answer.
    type Family =
        {
            Name: string
            DirName: string
            FileName: string
            FamilyId: string
            Kind: string
            Description: string
            Summary: string
            Vectors: unit -> Vector list
            Answer: string -> JVal -> Result<JVal, string>
            /// The extracted model's answer to a vector, read from the same input (Phase 563): the
            /// render holds it to `Answer`'s and stamps the vector, or fails on a disagreement.
            Model: Vector -> VectorOracle.Answer option
        }

    let families: Family list =
        [ { Name = "chain"
            DirName = Chain.familyDirName
            FileName = Chain.vectorsFileName
            FamilyId = Chain.familyId
            Kind = "chain-vectors"
            Description = Chain.description
            Summary =
              "The op-stream chain over opaque payloads: append (records, pre-images, hashes, tip) and walk (intact, or the first break)."
            Vectors = Chain.vectors
            Answer = Chain.answer
            Model = Chain.modelAnswer }
          { Name = "dag"
            DirName = DagFamily.familyDirName
            FileName = DagFamily.vectorsFileName
            FamilyId = DagFamily.familyId
            Kind = "dag-vectors"
            Description = DagFamily.description
            Summary =
              "The op-DAG over skeleton ops: content ids and write refusals, heads, order, ranks, the fold, and the integrity walk."
            Vectors = DagFamily.vectors
            Answer = DagFamily.answer
            Model = DagFamily.modelAnswer }
          { Name = "merge"
            DirName = Merge.familyDirName
            FileName = Merge.vectorsFileName
            FamilyId = Merge.familyId
            Kind = "merge-vectors"
            Description = Merge.description
            Summary =
              "The common base and the N-lane reconcile over skeleton ops: the merge script and the tree it lands on, or the fault."
            Vectors = Merge.vectors
            Answer = Merge.answer
            // Reference-only, NOT yet asked of its models (D232): the content ids (`Chain`'s DAG build),
            // the common base (`DagPrimitives.common_base`), the script (`DagClosure.reconcile_many_drained`)
            // and the landing (`TreeOps`) each have one, and the export does not compose them yet.
            Model = fun _ -> None }
          { Name = "diff"
            DirName = DiffFamily.familyDirName
            FileName = DiffFamily.vectorsFileName
            FamilyId = DiffFamily.familyId
            Kind = "diff-vectors"
            Description = DiffFamily.description
            Summary = "The content-aware skeleton diff: the script and the per-id classification, or the refusal."
            Vectors = DiffFamily.vectors
            Answer = DiffFamily.answer
            // Reference-only by reason (D232): a vector's answer is the script AND the per-id
            // classification, and no model states `Diff.changes` — `TreeDiff.fst`'s `d_*` state
            // `Tree.Digests.diff`, a different function. The script alone is `TreeDiff.to_ops`'s.
            Model = fun _ -> None } ]

    let tryFamily (name: string) : Family option =
        families |> List.tryFind (fun f -> f.Name = name)

    /// A vector's expectation, or the reason its input does not answer - a defect in the authored
    /// case, so the render stops rather than writing a file that is not true of the code.
    let expectationOf (f: Family) (v: Vector) : JVal =
        match f.Answer v.Case v.Input with
        | Ok e -> e
        | Error why -> failwithf "%s vector %s: %s" f.Name v.Id why

    let renderVectors (f: Family) : string =
        CorpusFile.render
            { Head =
                [ "family", CorpusFile.str f.FamilyId
                  "description", CorpusFile.str f.Description ]
              Items = "vectors"
              Rows =
                f.Vectors()
                |> List.map (fun v ->
                    let expected = expectationOf f v

                    CorpusCell.Row
                        [ "id", CorpusFile.str v.Id
                          "case", CorpusFile.str v.Case
                          "description", CorpusFile.str v.Description
                          "input", CorpusCell.Value v.Input
                          "expected", CorpusCell.Value expected
                          VectorOracle.stamp v.Id (CorpusCell.Value expected) (f.Model v) ]) }

    /// The family's self-enumerated index, in the one manifest schema.
    let internal manifest (f: Family) : CorpusManifest =
        { Version = 1
          Description =
            "A Core-owned structural family over the skeleton witness (Phase 549). SELF-ENUMERATED, as apply/ is: not indexed by the corpus root manifest.json. `adoption` is per host and is DATA: a host whose entry reads `proposed` reports the family BY NAME rather than skipping it, and flips its own entry to `adopted` in the change-set that lands its leg. This manifest is authoritative for the vector count."
          Families =
            [ { Id = f.FamilyId
                Kind = f.Kind
                File = f.FileName
                Vectors = List.length (f.Vectors())
                Adoption =
                  // Measured by this repository's own suite (`StructuralVectorTests`), every vector.
                  ("fuaran", HostAdoption.Measured(List.length (f.Vectors()), AdoptionSource.Here))
                  :: (ApplyVectorExport.hostRoster |> List.map (fun h -> h, HostAdoption.Proposed))
                  @ SpineAdoption.adoptions f.FamilyId
                Description = f.Summary } ] }

    let renderManifest (f: Family) : string =
        CorpusFile.render (CorpusFile.manifest (manifest f))

    let vectorsPath (f: Family) (dir: string) : string =
        Path.Combine(dir, f.DirName, f.FileName)

    let manifestPath (f: Family) (dir: string) : string =
        Path.Combine(dir, f.DirName, "manifest.json")

    /// A committed vector file read back: each vector's id, case, input and recorded expectation.
    let parseVectors (json: string) : Result<(string * string * JVal * JVal) list, string> =
        result {
            let! doc = Json.parse json
            let! items = reqArr "vectors" doc

            return!
                items
                |> traverse (fun v ->
                    result {
                        let! id = reqStr "id" v
                        let! case = reqStr "case" v
                        let! input = req "input" v
                        let! expected = req "expected" v
                        return id, case, input, expected
                    })
        }

    /// Re-answer every committed vector from its own input, as a host would, and name each whose
    /// recorded expectation is not what Core answers now.
    let checkFile (f: Family) (json: string) : string list =
        match parseVectors json with
        | Error why -> [ sprintf "%s: the file does not read: %s" f.Name why ]
        | Ok vectors ->
            [ for id, case, input, expected in vectors do
                  match f.Answer case input with
                  | Error why -> yield sprintf "%s/%s: the input does not answer: %s" f.Name id why
                  | Ok actual when canonical actual = canonical expected -> ()
                  | Ok actual ->
                      yield
                          sprintf
                              "%s/%s: Core answers\n  %s\nbut the vector records\n  %s"
                              f.Name
                              id
                              (canonical actual)
                              (canonical expected) ]
