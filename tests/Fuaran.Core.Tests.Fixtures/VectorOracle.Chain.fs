module Fuaran.Core.Tests.ChainOracle

// ============================================================================
//  Phase 563 — the hash chain's model (`proofs/Chain.fst`, ladder row `chain-walker-differential`) and
//  the canonical encoder's escaper (`proofs/WireCanon.fst`, ladder row `canon-differential`) as the
//  chain and escape exports ask them, and the position bridge the chain differentials share with them
//  (moved from the test project's `ProofOracle.ChainTests.fs`, so it is defined once).
// ============================================================================

open Fuaran.Core
open Fuaran.Core.Tests.VectorOracle
open Fuaran.Core.Tests.CanonOracle

let rec posOfInt (i: int) : Chain.pos =
    if i <= 0 then
        Chain.PZero
    else
        Chain.PSucc(posOfInt (i - 1))

let rec intOfPos (p: Chain.pos) : int =
    match p with
    | Chain.PZero -> 0
    | Chain.PSucc m -> 1 + intOfPos m

/// F#'s `string (seq: int)`, which the model takes as a parameter for the same reason it takes the
/// hash: rendering an integer is not something a model of a hash chain owns.
let showPos (p: Chain.pos) : string = string (intOfPos p)

/// The model's `le`: F#'s `List.sortWith (fun a b -> String.CompareOrdinal(a, b))` as a predicate.
let ordinalLe (a: string) (b: string) : bool = System.String.CompareOrdinal(a, b) <= 0

/// The order `Dag.firstBreak` walks a DAG since Phase 311: the whole DAG's drain (parents first,
/// the smallest key among the ready nodes), then every node on or below a cycle in key order. Read
/// off production rather than re-derived: `totalOrderBy` at a constant key is that drain, and where
/// a cycle leaves nodes unplaced the drain of the rest — which no unplaced node is a parent of — is
/// the placed prefix.
let firstBreakWalk (dag: Dag.T<'Op>) : string list =
    match Dag.totalOrderBy (fun _ -> 0) dag with
    | Ok order -> order
    | Error { Unplaced = unplaced } ->
        let rest: Dag.T<'Op> =
            DagOf.nodes (dag.Nodes |> Map.filter (fun k _ -> not (List.contains k unplaced)))

        (match Dag.totalOrderBy (fun _ -> 0) rest with
         | Ok order -> order
         | Error _ -> [])
        @ unplaced

/// A production `Dag.T` as the model reads it — in the order `Dag.firstBreak` walks
/// (`firstBreakWalk`; `Map.toList`'s id order until Phase 311); the map KEY beside the node,
/// because production compares the key and never the node's own `Id` field; and the actor as the
/// string `Actor.encode` produces, which is the only thing the hash pre-image ever sees of it.
let toChainEntries (dag: Dag.T<'Op>) : Chain.entry<'Op> list =
    firstBreakWalk dag
    |> List.map (fun k -> k, dag.Nodes[k])
    |> List.map (fun (k, n) ->
        { Chain.ekey = k
          Chain.enode =
            { Chain.dparents = n.Parents
              Chain.dactor = Actor.encode n.Actor
              Chain.dop = n.Op } })

/// A `verify` vector as the DAG walker's model answers it: `first_break` over production's walk order
/// (the order is the model's premise, read off production as the differential reads it), intact or the
/// first break.
let verify (hash: HashFn) (encodeOp: 'Op -> string) (dag: Dag.T<'Op>) : JVal =
    match Chain.first_break (fun p s -> hash p s) encodeOp ordinalLe (toChainEntries dag) with
    | Chain.Missing -> JObj [ "verdict", JStr "intact" ]
    | Chain.Found b ->
        JObj
            [ "verdict", JStr "break"
              "node", JStr b.bnode
              "reason", JStr b.breason
              "expected", JStr b.bexpected
              "got", JStr b.bgot ]

[<Literal>]
let Model = "Chain"

[<Literal>]
let LadderRow = "chain-walker-differential"

/// The tip the next record hangs from: the anchor's sequence advanced by the run, and the run's last
/// hash (`last_hash`).
let private tip (anchorSeq: int) (anchorPrev: string) (rs: Chain.record<string> list) : JVal =
    JObj
        [ "seq", JInt(anchorSeq + List.length rs)
          "prevHash", JStr(Chain.last_hash anchorPrev rs) ]

/// An `append` vector as the model answers it: each entry (its actor's canonical text, its opaque op)
/// chained by `append_rec` from the anchor, the pre-image `rec_payload`, then the tip.
let append (hash: HashFn) (anchorSeq: int) (anchorPrev: string) (entries: (string * string) list) : JVal =
    let h = fun prev payload -> hash prev payload

    let records =
        entries
        |> List.fold
            (fun (acc: Chain.record<string> list) (actor, op) ->
                let prev = Chain.last_hash anchorPrev acc

                acc
                @ [ Chain.append_rec h showPos id prev (posOfInt (anchorSeq + List.length acc)) actor op ])
            []

    let rows =
        records
        |> List.map (fun r ->
            JObj
                [ "seq", JInt(intOfPos r.rseq)
                  "prevHash", JStr r.rprev
                  "payload", JStr(Chain.rec_payload showPos id r.rseq r.ractor r.rop)
                  "hash", JStr r.rhash ])

    JObj [ "records", JArr rows; "tip", tip anchorSeq anchorPrev records ]

/// A `walk` vector as the model answers it: `first_chain_break_from` over the run from the anchor —
/// intact with the tip, or the first break.
let walk
    (hash: HashFn)
    (anchorSeq: int)
    (anchorPrev: string)
    (records: (int * string * string * string * string) list)
    : JVal =
    let rs =
        records
        |> List.map (fun (seq, actor, op, prev, h) ->
            { Chain.rseq = posOfInt seq
              Chain.ractor = actor
              Chain.rop = op
              Chain.rprev = prev
              Chain.rhash = h })

    match Chain.first_chain_break_from (fun p s -> hash p s) showPos id anchorPrev (posOfInt anchorSeq) rs with
    | Chain.Missing -> JObj [ "verdict", JStr "intact"; "tip", tip anchorSeq anchorPrev rs ]
    | Chain.Found b ->
        JObj
            [ "verdict", JStr "break"
              "index", JInt(intOfPos b.cindex)
              "reason", JStr b.creason
              "expected", JStr b.cexpected
              "got", JStr b.cgot ]

// ---------------------------------------------------------------------------
//  The escape table (Phase 563)
// ---------------------------------------------------------------------------

/// The escaper's model and row: every spelling and every actor encoding is `WireCanon.escape` /
/// `quoted`; the pre-image's envelope around an encoding is `Chain.rec_payload`, evaluated too.
[<Literal>]
let EscapeModel = "WireCanon"

[<Literal>]
let EscapeRow = "canon-differential"

/// A string as the model quotes it.
let private quoted (s: string) : string =
    canonFromChs (WireCanon.quoted (canonToChs s))

/// An actor's canonical text, its fields quoted by the model — the object frame is the exporter's.
let actorText (a: Actor) : string =
    match a with
    | Human id -> "{\"kind\":\"human\",\"id\":" + quoted id + "}"
    | Agent(model, version, id) ->
        "{\"kind\":\"agent\",\"model\":"
        + quoted model
        + ",\"version\":"
        + quoted version
        + ",\"id\":"
        + quoted id
        + "}"

/// A character vector's line as the model answers it: its name, the model's spelling of its input,
/// and the encoding of a human whose id is that input.
let characterLine (name: string) (input: string) : string =
    name
    + "\t"
    + canonFromChs (WireCanon.escape (canonToChs input))
    + "\t"
    + actorText (Human input)

/// A named actor's line as the model answers it: its name, the model's encoding of it, and the
/// pre-image at sequence 0 with an empty op.
let actorLine (name: string) (actor: Actor) : string =
    let encoded = actorText actor

    name
    + "\t"
    + encoded
    + "\t"
    + Chain.rec_payload showPos id (posOfInt 0) encoded "{}"
