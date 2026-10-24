module Fuaran.Core.Tests.ProofOracle.ChainTests

// The `Proofs.Oracle` differential for `proofs/Chain.fst`: the extracted model
// (`proofs/oracle/Chain.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.FoldFixtures
open Fuaran.Core.Tests.ProofOracle.Bridges
open Fuaran.Core.Tests.ChainOracle

// ---------------------------------------------------------------------------
//  Phase 136 — the two INTEGRITY WALKERS as a fourth oracle.
//
//  `proofs/Chain.fst` models `Dag.firstBreak` over the content-addressed DAG and
//  `OpStream.firstChainBreak` over the linear chain, clause for clause, and proves that a
//  single-node tamper is found — under ONE named premise: the content id determines the content.
//  This section runs the extracted model beside BOTH production walkers, over DAGs and chains
//  production itself built and over generated single-node tampers, comparing the verdicts as
//  strings so the outcome class, the node or index it is reported at, WHICH check failed, and the
//  expected/got values are all compared at once.
//
//  `Dag.nodeHash` is private, so the only way to reach it is through `Dag.append` — which is what
//  makes the intact runs evidence rather than a formality. A model whose `isort` and `join_comma`
//  were not production's `List.sortWith CompareOrdinal` and `String.concat ","` would recompute a
//  different id for every node and report a break on an INTACT DAG. `mintsProductionIds` below
//  asserts the same thing directly, node by node, so a divergence says which half moved.
//
//  ON THE CORPUS. The wire corpus's `dag/` family is the UI host's DAG-RECORD WIRE FORMAT
//  (`kind: "dag-record-round-trip"`), not a pool of Core content ids: its `hash` members are
//  64 characters, minted by that host's own pre-image under SHA-256 over an envelope carrying
//  members Core's `DagNode` does not have, where Core's default `HashFn` is FNV-1a and emits 8.
//  Handing those four records to `Dag.firstBreak` would report four content-id mismatches — a true
//  answer to the wrong question. What the family DOES supply, and what the generated pools cannot,
//  are the SHAPES: a genesis node, a linear step, a two-parent MERGE, and both actor kinds. Those
//  are rebuilt below through production's own `Dag.append` / `Dag.merge`, and the size fact is
//  asserted so the boundary goes red if it ever moves.
// ---------------------------------------------------------------------------

/// What a tamper puts in a work-plan op's place: a DIFFERENT op, on every case, so no tamper is
/// silently a no-op. The tampers below skip any that lands back on the op it replaced.
let tamperedPlanOp (op: PlanOp) : PlanOp =
    match op with
    | AddItem(i, t) -> AddItem(i, t + "!")
    | Retitle(i, t) -> Retitle(i, t + "!")
    | SetShipped i -> Retitle(i, "tampered")
    | AddDep(i, d) -> AddDep(i, d + "!")

/// A DAG walker's verdict as a COMPARABLE VALUE (Phase 147). Production's break carries a typed
/// `DagBreakReason`; the model's carries the string its extraction mints, classified through the
/// same total `ofString`. Comparing these compares the reason as a CLASS rather than as two
/// spellings that happen to match — which is the projection Phase 147 deletes, and it is this
/// differential that asked for it. It also sharpens the failure: a model whose reason string drifted
/// a character used to fail as an unexplained textual mismatch and now fails as an `Unrecognised`
/// carrying that text against a named case, which says what went wrong.
type private DagVerdict =
    | DagIntact
    | DagBroke of node: string * reason: DagBreakReason * expected: string * got: string

let private renderDagVerdict (v: DagVerdict) : string =
    match v with
    | DagIntact -> "intact"
    | DagBroke(node, reason, expected, got) ->
        sprintf "break at %s | %s | expected=%s | got=%s" node (DagBreakReason.toString reason) expected got

/// The reason class a verdict names, for the cases that assert WHICH check fired.
let private dagVerdictReason (v: DagVerdict) : DagBreakReason option =
    match v with
    | DagIntact -> None
    | DagBroke(_, reason, _, _) -> Some reason

let private prodDagVerdict (b: DagBreak option) : DagVerdict =
    match b with
    | None -> DagIntact
    | Some b -> DagBroke(b.NodeId, b.Reason, b.Expected, b.Got)

let private modelDagVerdict (b: Chain.found<Chain.dbreak>) : DagVerdict =
    match b with
    | Chain.Missing -> DagIntact
    | Chain.Found b -> DagBroke(b.bnode, DagBreakReason.ofString b.breason, b.bexpected, b.bgot)

/// Both walkers on the same DAG. The two hash functions are separate arguments only so the go-red
/// case can hand the MODEL one production is not using; every real run passes the same one twice.
let private dagVerdicts
    (prodHash: HashFn)
    (modelHash: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (dag: Dag.T<'Op>)
    : DagVerdict * DagVerdict =
    prodDagVerdict (Dag.firstBreak (OpStream.canonical prodHash w) dag),
    modelDagVerdict (Chain.first_break modelHash w.Encode ordinalLe (toChainEntries dag))

/// The DAG `FoldConfluence.foldOnce` builds, under a CHOSEN `HashFn` — one shared base node and
/// one chain per lane. `productionDag` above pins the default hash; the premise case below needs
/// a deliberately non-injective one, and nothing else about the construction may move with it.
let private dagUnder
    (hashFn: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (baseOp: 'Op)
    (lanes: 'Op list list)
    : Dag.T<'Op> =
    let baseId, d0 =
        Dag.append (OpStream.canonical hashFn w) (Human "base") baseOp "" Dag.empty
        |> Reference.built

    let mutable d = d0

    for (i, ops) in List.indexed lanes do
        let actor = Human("lane-" + string i)
        let mutable h = baseId

        for op in ops do
            let id, d' =
                Dag.append (OpStream.canonical hashFn w) actor op h d |> Reference.built

            h <- id
            d <- d'

    d

/// Every node's id, recomputed by the MODEL, against the one production minted — the pre-image
/// itself, checked directly rather than inferred from the walker's verdict.
let private mintsProductionIds
    (hashFn: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (dag: Dag.T<'Op>)
    : string option =
    dag.Nodes
    |> Map.toList
    |> List.tryPick (fun (k, n) ->
        let minted =
            Chain.node_hash hashFn w.Encode ordinalLe n.Parents (Actor.encode n.Actor) n.Op

        if minted <> k then
            Some(sprintf "the model mints %s where production minted %s (op %s)" minted k (w.Encode n.Op))
        else
            None)

/// Every single-node tamper of one DAG, as (what was done, the tampered DAG). A TAMPER moves the
/// node's content and leaves its ADDRESS — the map key — exactly as it was; that is the threat the
/// content id exists to catch, and it is precisely not a rewrite.
///
/// The fourth class is the other break: a node another node NAMES, deleted. It has to be a
/// deletion rather than a re-pointed parent, because a re-pointed parent changes the pre-image and
/// production reports the content-id mismatch first — so "missing parent" is unreachable by
/// tampering a node's own fields.
let private dagTampers (otherOp: 'Op -> 'Op) (dag: Dag.T<'Op>) : (string * Dag.T<'Op>) list =
    let nodes = dag.Nodes |> Map.toList

    [ for (k, n) in nodes do
          let o' = otherOp n.Op

          if o' <> n.Op then
              yield sprintf "op@%s" k, DagOf.nodes (Map.add k { n with Op = o' } dag.Nodes)

          let a' = Human "tamperer"

          if a' <> n.Actor then
              yield sprintf "actor@%s" k, DagOf.nodes (Map.add k { n with Actor = a' } dag.Nodes)

          match
              nodes
              |> List.tryPick (fun (p, _) -> if p <> k && n.Parents <> [ p ] then Some p else None)
          with
          | Some p -> yield sprintf "reparent@%s" k, DagOf.nodes (Map.add k { n with Parents = [ p ] } dag.Nodes)
          | None -> ()

          for p in n.Parents do
              yield sprintf "drop-parent@%s" p, DagOf.nodes (Map.remove p dag.Nodes) ]

/// The walker tally. `Intact`: intact structures compared — a run that never met one has not tested
/// the agreeing case. `Tampers` compared, and how many production actually `Detected`: a run whose
/// tampers were all invisible has compared two walkers that both said "intact" and measured nothing.
let private walkerTally = Tally.empty [ "Intact"; "Tampers"; "Detected" ]

/// One tamper compared: counted, and counted as detected when production's verdict is not `intact`.
let private tamperCounted (intact: 'V) (production: 'V) (t: Tally) : Tally =
    t |> Tally.bump "Tampers" |> Tally.bumpIf (production <> intact) "Detected"

/// Run the DAG walker differential over `iterations` generated lane sets from one pool.
let private dagDifferential
    (label: string)
    (prodHash: HashFn)
    (modelHash: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (otherOp: 'Op -> 'Op)
    (gen: LaneGen<'Op, 'State>)
    (laneCount: int)
    (seed: int)
    (iterations: int)
    : Tally =
    let step (_: int) (rng: ConfRng.T) (t: Tally) =
        let lanes, r' = gen.Lanes laneCount rng
        let dag = dagUnder prodHash w gen.BaseOp lanes

        // The verdicts are compared as VALUES (Phase 147: the reason as a class, not a spelling);
        // the renders only say what differed.
        let compare (what: string) (d: Dag.T<'Op>) (t: Tally) : DagVerdict * Tally =
            let p, m = dagVerdicts prodHash modelHash w d

            p,
            t
            |> Differential.check (p = m) (fun () ->
                sprintf
                    "%s: seed=%d tamper=%s\n%s\n  production: %s\n  model:      %s"
                    label
                    seed
                    what
                    (renderLanes w.Encode lanes)
                    (renderDagVerdict p)
                    (renderDagVerdict m))

        let tamper (what: string, d: Dag.T<'Op>) (t: Tally) : Tally =
            let p, t = compare what d t
            tamperCounted DagIntact p t

        let t =
            match mintsProductionIds prodHash w dag with
            | Some why -> Tally.disagree (sprintf "%s: seed=%d %s" label seed why) t
            | None -> t

        let p, t = compare "none" dag t
        let t = Tally.bumpIf (p = DagIntact) "Intact" t
        let singles = dagTampers otherOp dag
        let t = Differential.over singles tamper t

        // Phase 311: TWO tampers at once, so the walkers must agree on WHICH break comes first —
        // production names the earliest in its walk, and the model is handed that walk.
        let changedKey (d: Dag.T<'Op>) =
            d.Nodes
            |> Map.toList
            |> List.tryFind (fun (k, n) ->
                match Map.tryFind k dag.Nodes with
                | Some o -> o.Parents <> n.Parents || o.Actor <> n.Actor || w.Encode o.Op <> w.Encode n.Op
                | None -> true)
            |> Option.map fst

        // one in-place tamper per node (a tamper that removes a node changes no node in place)
        let perNode =
            singles
            |> List.choose (fun (what, d) ->
                changedKey d
                |> Option.filter d.Nodes.ContainsKey
                |> Option.map (fun k -> k, what, d.Nodes[k]))
            |> List.distinctBy (fun (k, _, _) -> k)

        let pairs =
            [ for (i, (k1, w1, n1)) in List.indexed perNode do
                  for (j, (k2, w2, n2)) in List.indexed perNode do
                      if i < j then
                          yield w1 + " and " + w2, DagOf.nodes (dag.Nodes |> Map.add k1 n1 |> Map.add k2 n2) ]

        r', Differential.over pairs tamper t

    Differential.seeded iterations (ConfRng.ofSeed seed) step walkerTally

// ---- the linear chain ----

/// Production's records as the model reads them. `Seq` is a Peano numeral here (the extraction
/// carries no integers — `proofs/README.md`, finding 2), so a tampered NEGATIVE sequence is
/// outside what this bridge can carry and the tampers below stay non-negative.
let toChainRecords (rs: OpRecord<'Op> list) : Chain.record<'Op> list =
    rs
    |> List.map (fun r ->
        { Chain.rseq = posOfInt r.Seq
          Chain.ractor = Actor.encode r.Actor
          Chain.rop = r.Op
          Chain.rprev = r.PrevHash
          Chain.rhash = r.Hash })

/// The linear walker's verdict, on the same footing as `DagVerdict` (Phase 147). `ChainBreak.Reason`
/// has been a closed DU since Phase 125; this differential was still rendering it to text and
/// comparing the text, so the same reason-as-a-class argument applies unchanged and the section now
/// holds no string match on a break reason at all.
type private ChainVerdict =
    | ChainIntact
    | ChainBroke of index: int * reason: ChainBreakReason * expected: string * got: string

let private renderChainVerdict (v: ChainVerdict) : string =
    match v with
    | ChainIntact -> "intact"
    | ChainBroke(index, reason, expected, got) ->
        sprintf "break at %d | %s | expected=%s | got=%s" index (ChainBreakReason.toString reason) expected got

let private chainVerdictReason (v: ChainVerdict) : ChainBreakReason option =
    match v with
    | ChainIntact -> None
    | ChainBroke(_, reason, _, _) -> Some reason

let private prodChainVerdict (b: ChainBreak option) : ChainVerdict =
    match b with
    | None -> ChainIntact
    | Some b -> ChainBroke(b.Index, b.Reason, b.Expected, b.Got)

let private modelChainVerdict (b: Chain.found<Chain.cbreak>) : ChainVerdict =
    match b with
    | Chain.Missing -> ChainIntact
    | Chain.Found b -> ChainBroke(intOfPos b.cindex, ChainBreakReason.ofString b.creason, b.cexpected, b.cgot)

let private chainVerdicts
    (prodHash: HashFn)
    (modelHash: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (rs: OpRecord<'Op> list)
    : ChainVerdict * ChainVerdict =
    prodChainVerdict (OpStream.firstChainBreak (OpStream.canonical prodHash w) rs),
    modelChainVerdict (Chain.first_chain_break modelHash showPos w.Encode "" (toChainRecords rs))

/// A chain production built by `OpStream.append`, skipping the ops the domain reducer rejects —
/// `append` chains nothing on a rejection, so the chain is the shape of the accepted run.
let chainUnder
    (hashFn: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (state0: 'State)
    (actor: Actor)
    (ops: 'Op list)
    : OpRecord<'Op> list =
    let mutable st = state0
    let mutable rs: OpRecord<'Op> list = OpStream.empty

    for op in ops do
        match OpStream.append (OpStream.canonical hashFn w) actor op st rs with
        | Ok(st', rs') ->
            st <- st'
            rs <- rs'
        | Error _ -> ()

    rs

/// Every single-record tamper of one chain, in the four classes the walker's three checks cover:
/// the op and the actor (found by the recomputed hash), the sequence, the prev-link, and a
/// DROPPED record — truncation, which the sequence check finds.
let chainTampers (otherOp: 'Op -> 'Op) (rs: OpRecord<'Op> list) : (string * OpRecord<'Op> list) list =
    let n = List.length rs

    [ for i in 0 .. n - 1 do
          let r = List.item i rs

          let put (r': OpRecord<'Op>) =
              rs |> List.mapi (fun j x -> if j = i then r' else x)

          let o' = otherOp r.Op

          if o' <> r.Op then
              yield sprintf "op@%d" i, put { r with Op = o' }

          let a' = Human "tamperer"

          if a' <> r.Actor then
              yield sprintf "actor@%d" i, put { r with Actor = a' }

          yield sprintf "seq@%d" i, put { r with Seq = r.Seq + 1 }
          yield sprintf "prev@%d" i, put { r with PrevHash = "deadbeef" }

          if n > 1 then
              yield sprintf "drop@%d" i, (rs |> List.indexed |> List.filter (fun (j, _) -> j <> i) |> List.map snd) ]

let private chainDifferential
    (label: string)
    (prodHash: HashFn)
    (modelHash: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (otherOp: 'Op -> 'Op)
    (gen: LaneGen<'Op, 'State>)
    (seed: int)
    (iterations: int)
    : Tally =
    let step (_: int) (rng: ConfRng.T) (t: Tally) =
        // One lane's ops, appended as one writer's linear stream.
        let lanes, r' = gen.Lanes 2 rng
        let ops = List.concat lanes
        let rs = chainUnder prodHash w gen.State0 (Human "writer") ops

        let compare (what: string) (records: OpRecord<'Op> list) (t: Tally) : ChainVerdict * Tally =
            let p, m = chainVerdicts prodHash modelHash w records

            p,
            t
            |> Differential.check (p = m) (fun () ->
                sprintf
                    "%s: seed=%d tamper=%s ops=[%s]\n  production: %s\n  model:      %s"
                    label
                    seed
                    what
                    (ops |> List.map w.Encode |> String.concat "; ")
                    (renderChainVerdict p)
                    (renderChainVerdict m))

        let tamper (what: string, tampered: OpRecord<'Op> list) (t: Tally) : Tally =
            let p, t = compare what tampered t
            tamperCounted ChainIntact p t

        if List.isEmpty rs then
            r', t
        else
            let p, t = compare "none" rs t
            let t = Tally.bumpIf (p = ChainIntact) "Intact" t
            r', Differential.over (chainTampers otherOp rs) tamper t

    Differential.seeded iterations (ConfRng.ofSeed seed) step walkerTally

let private expectWalkerAgreement (label: string) (t: Tally) =
    Differential.expectAgrees label t
    Expect.isGreaterThan (Tally.count "Intact" t) 0 (sprintf "%s: no intact structure was compared" label)
    Expect.isGreaterThan (Tally.count "Tampers" t) 0 (sprintf "%s: no tamper was compared" label)

    // The teeth's vacuity guard, and the one that matters most here: two walkers that both say
    // "intact" agree perfectly and certify nothing. Detection has to have HAPPENED.
    Expect.isGreaterThan
        (Tally.count "Detected" t)
        0
        (sprintf "%s: every tamper went undetected, so the agreement is vacuous" label)

/// A deliberately NON-INJECTIVE `HashFn`: `nodeHash` hands it the joined parents and
/// `actorEncoded + "|" + encodedOp`, and this folds only up to the first `|` — so the actor
/// survives and the OP IS DROPPED, and two nodes differing only in their op mint one id.
/// `Actor.encode` emits a JSON object with no `|` in it, so the first `|` is the separator.
let private opBlindHash: HashFn =
    fun parents rest ->
        let cut = rest.IndexOf '|'
        OpStream.defaultHash parents (if cut < 0 then rest else rest.Substring(0, cut))

/// The go-red instrument for the comparison itself: a hash the MODEL uses and production does not
/// — the same function with its two arguments swapped, so every recomputed id is wrong and an
/// intact DAG must be reported as broken.
let swappedHash: HashFn = fun a b -> OpStream.defaultHash b a

// ---- the corpus dag/ family, as a source of SHAPES ----

/// One `dag/` fixture, read for what Core can use: the parent shape, the typed actor, and the op
/// payload. Its own `hash` is kept only so the size assertion below can be made about it.
type private DagShape =
    { Fixture: string
      CorpusHash: string
      CorpusParents: string list
      Actor: Actor
      OpJson: string }

let private readDagShapes (root: string) : Result<DagShape list, string> =
    Directory.GetFiles(Path.Combine(root, "dag"), "*.json")
    |> Array.sort
    |> Array.toList
    |> List.filter (fun p -> Path.GetFileName p <> "manifest.json")
    |> List.map (fun path ->
        let name = Path.GetFileNameWithoutExtension path

        match Json.parse (File.ReadAllText path) with
        | Error e -> Error(sprintf "%s: not JSON (%s)" name e)
        | Ok(JObj fields) ->
            let actor =
                match field fields "actor" with
                | Some(JObj af) ->
                    let s k = defaultArg (strField af k) ""

                    match strField af "kind" with
                    | Some "agent" -> Some(Agent(s "model", s "version", s "id"))
                    | _ -> Some(Human(s "id"))
                | _ -> None

            match strField fields "hash", field fields "parents", actor, field fields "op" with
            | Some h, Some(JArr ps), Some a, Some op ->
                Ok
                    { Fixture = name
                      CorpusHash = h
                      CorpusParents =
                        ps
                        |> List.choose (fun v ->
                            match v with
                            | JStr s -> Some s
                            | _ -> None)
                      Actor = a
                      OpJson = Canonical.write Rendering.CrossHost op }
            | _ -> Error(sprintf "%s: expected hash / parents / actor / op" name)
        | Ok _ -> Error(sprintf "%s: not a JSON object" name))
    |> List.fold
        (fun acc r ->
            match acc, r with
            | Ok xs, Ok x -> Ok(xs @ [ x ])
            | Error e, _ -> Error e
            | _, Error e -> Error e)
        (Ok [])

/// The op payload of a `dag/` fixture is opaque wire JSON, and every walker treats an op that way.
let private rawW: StreamWitness<string, string, string> =
    { Apply = fun op st -> Ok(st + "/" + op)
      Encode = id
      Decode = Ok }

// ---------------------------------------------------------------------------
//  Phase 145 — the pre-image's two splices, measured against production's encodings
// ---------------------------------------------------------------------------
//
// `Chain.fst` proves the two splices unambiguous under conditions on the ALPHABETS: a parent id
// carries no comma and is not empty, and no actor encoding is a proper prefix of another. Those
// are claims about `OpStream.defaultHash`'s output and about `Actor.encode`, and the model cannot
// check either — F*'s `string` is primitive. This is where they are checked, and the population is
// chosen adversarially rather than typically, because a typical actor cannot refute anything.

/// `x` is a PROPER prefix of `y` — the model's `Chain.proper_prefix`, spelled over `string`.
let private properPrefix (x: string) (y: string) : bool =
    x.Length < y.Length && y.StartsWith(x, System.StringComparison.Ordinal)

/// Actors chosen to break a splice if anything can: the separator itself, the JSON metacharacters
/// `Actor.encode`'s escaper does and does not handle, an id that spells another actor's encoding,
/// the empty id, and both cases at every field.
let private adversarialActors: Actor list =
    [ Human ""
      Human "a"
      Human "a|b"
      Human "a|b|c"
      Human "a\"}|{\""
      Human "a\\|b"
      Human "a,b"
      Human "{\"kind\":\"human\",\"id\":\"a\"}"
      Human "}|{"
      Human "\n|\t"
      Agent("", "", "")
      Agent("m", "v", "a")
      Agent("m|1", "v|2", "a|3")
      Agent("m", "v", "a\"}|x")
      Agent("m", "v", "{\"kind\":\"agent\"}")
      Agent("m,1", "v,2", "a,3") ]

/// A `StreamGen` over the work-plan domain, drawn through the lane generator this differential
/// already runs on — so the population the kit's codec law samples is the population the model's
/// `op_codec_injective` parameter is about, rather than a second one invented beside it.
let private planStreamGen: StreamGen<PlanOp, Plan> =
    { State0 = planLaneGen.State0
      Op =
        fun rng ->
            let lanes, r = planLaneGen.Lanes 1 rng

            match lanes |> List.collect id with
            | op :: _ -> op, r
            | [] -> planLaneGen.BaseOp, r } // a lane may legitimately be empty

/// Op encodings chosen the same way — including the work-plan codec's own `"A|" + id + "|" + title`
/// shape, which is why the splice condition is not academic.
let private adversarialOpEncodings: string list =
    [ ""; "|"; "A|x|t"; "}|{"; "{\"kind\":\"human\",\"id\":\"a\"}|A|x|t"; "\"}" ]

// ---------------------------------------------------------------------------
//  Phase 197 — the linear chain's ENVELOPE PARSE, measured against production's payload
//
//  `Chain.fst` section 6b discharges the linear side's premise: `rec_payload`'s four-way envelope
//  `{"seq":<n>,"actor":<a>,"op":<o>}` is parsed back into its three fields, each split forced by
//  the leading comma of the separator that follows it, under three conditions on the alphabets —
//  the numeral carries no comma and `show` is injective (`seq_numeral_code`), the actor code is
//  prefix-free (`actor_code_prefix_free`, Phase 145's premise again), and the op codec is
//  injective (premise 4, nothing about its alphabet). The model cannot check any of those of
//  production, so this is where they are measured: that production's payload IS the model's
//  envelope, that the parse recovers all three fields over the adversarial population, that each
//  alphabet condition is load-bearing (the parse loses under a perturbed code), and that the
//  theorem the discharge buys — `payload_splice_breaks_chain` — holds beside production's walker.
// ---------------------------------------------------------------------------

/// Op encodings for the ENVELOPE, chosen to break a parse if anything can: the op is the last
/// field, so these carry the envelope's own separators and its closing brace.
let private adversarialEnvelopeOps: string list =
    [ ""
      "}"
      "{}"
      ",\"op\":"
      "\"}"
      "{\"seq\":1,\"actor\":{},\"op\":{}}"
      "A|x|t" ]

/// Sequence numbers for the envelope, including the ones a thousands-separated renderer would
/// print differently. Bounded at four digits because the model's `pos` is a Peano numeral built
/// by a non-tail recursion (`posOfInt`), which a six-digit sequence overflows the stack with.
let private adversarialSeqs: int list = [ 0; 1; 7; 10; 999; 1000; 1001; 2026 ]

/// The model's envelope over a STRING op with the identity codec — the shape the parse is stated
/// over, since `rec_payload` reads nothing of the op but its encoding.
let private modelEnvelope (show: Chain.pos -> string) (seq: int) (actor: string) (opJson: string) : string =
    Chain.rec_payload show id (posOfInt seq) actor opJson

/// Every PAYLOAD SPLICE of one chain: record `i` takes record `j`'s content while keeping its own
/// stored hash and prev-link — the whole content (which the sequence check finds first) and the
/// actor-and-op only (which only the recomputed hash can find). Exactly the tamper
/// `payload_splice_breaks_chain` is about, drawn from the chain's own payloads rather than minted.
let private payloadSplices (rs: OpRecord<'Op> list) : (string * OpRecord<'Op> list) list =
    let n = List.length rs

    [ for i in 0 .. n - 1 do
          for j in 0 .. n - 1 do
              if i <> j then
                  let r = List.item i rs
                  let s = List.item j rs

                  let put (r': OpRecord<'Op>) =
                      rs |> List.mapi (fun k x -> if k = i then r' else x)

                  yield
                      sprintf "content@%d<-%d" i j,
                      put
                          { r with
                              Seq = s.Seq
                              Actor = s.Actor
                              Op = s.Op }

                  if s.Actor <> r.Actor || s.Op <> r.Op then
                      yield sprintf "actor+op@%d<-%d" i j, put { r with Actor = s.Actor; Op = s.Op } ]

let private payloadSpliceDifferential
    (label: string)
    (prodHash: HashFn)
    (modelHash: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (gen: LaneGen<'Op, 'State>)
    (seed: int)
    (iterations: int)
    : Tally =
    let step (_: int) (rng: ConfRng.T) (t: Tally) =
        let lanes, r' = gen.Lanes 2 rng
        let ops = List.concat lanes
        // Two writers, so an actor-and-op splice can change the actor as well as the op.
        let rs =
            ops
            |> List.mapi (fun k op ->
                (if k % 2 = 0 then
                     Human "writer"
                 else
                     Agent("m", "v", "writer-2")),
                op)
            |> List.fold
                (fun (st, acc) (actor, op) ->
                    match OpStream.append (OpStream.canonical prodHash w) actor op st acc with
                    | Ok(st', acc') -> st', acc'
                    | Error _ -> st, acc)
                (gen.State0, OpStream.empty)
            |> snd

        let compare (what: string) (records: OpRecord<'Op> list) (t: Tally) : ChainVerdict * Tally =
            let p, m = chainVerdicts prodHash modelHash w records

            p,
            t
            |> Differential.check (p = m) (fun () ->
                sprintf
                    "%s: seed=%d splice=%s ops=[%s]\n  production: %s\n  model:      %s"
                    label
                    seed
                    what
                    (ops |> List.map w.Encode |> String.concat "; ")
                    (renderChainVerdict p)
                    (renderChainVerdict m))

        let splice (what: string, spliced: OpRecord<'Op> list) (t: Tally) : Tally =
            let p, t = compare what spliced t

            t
            |> tamperCounted ChainIntact p
            |> Differential.check (p <> ChainIntact) (fun () ->
                sprintf "%s: seed=%d splice=%s went UNDETECTED by production" label seed what)

        if List.length rs > 1 then
            let p, t = compare "none" rs t
            let t = Tally.bumpIf (p = ChainIntact) "Intact" t
            r', Differential.over (payloadSplices rs) splice t
        else
            r', t

    Differential.seeded iterations (ConfRng.ofSeed seed) step walkerTally

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- Phase 136 — the two integrity walkers, and the tampers they are for ----

          testCase "the chain oracle agrees with production over the reference witness's DAG and every tamper of it"
          <| fun _ ->
              dagDifferential
                  "reference witness"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  treeW
                  (fun _ -> RemoveNode "tampered-node")
                  treeLaneGen
                  3
                  3600
                  60
              |> expectWalkerAgreement "reference witness DAG"

          testCase "the chain oracle agrees with production over the work-plan DAG and every tamper of it"
          <| fun _ ->
              dagDifferential
                  "work-plan domain"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  planW
                  tamperedPlanOp
                  planLaneGen
                  3
                  3610
                  60
              |> expectWalkerAgreement "work-plan DAG"

          testCase "the chain oracle agrees with production over the linear op-stream and every tamper of it"
          <| fun _ ->
              chainDifferential
                  "work-plan chain"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  planW
                  tamperedPlanOp
                  planLaneGen
                  3620
                  80
              |> expectWalkerAgreement "work-plan chain"

          testCase "the chain oracle agrees with production over the reference witness's linear op-stream"
          <| fun _ ->
              chainDifferential
                  "reference chain"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  treeW
                  (fun _ -> RemoveNode "tampered-node")
                  treeLaneGen
                  3630
                  60
              |> expectWalkerAgreement "reference chain"

          // ---- the teeth: both comparisons can lose ----

          testCase "a model handed a DIFFERENT hash reports a break where production sees none"
          <| fun _ ->
              // The go-red for the DAG comparison. Nothing about the DAG moves; only the function
              // the model recomputes ids with. If this came back clean the green cases above would
              // be comparing a walker that cannot notice the pre-image it hashes.
              let lanes, _ = planLaneGen.Lanes 3 (ConfRng.ofSeed 3640)
              let dag = dagUnder OpStream.defaultHash planW planLaneGen.BaseOp lanes
              let p, m = dagVerdicts OpStream.defaultHash swappedHash planW dag
              Expect.equal p DagIntact "production built this DAG and sees it intact"
              Expect.notEqual m p "a model recomputing ids with a different hash must disagree"

              Expect.equal
                  (dagVerdictReason m)
                  (Some DagBreakReason.ContentIdMismatch)
                  "and it disagrees by naming the check that failed"

          testCase "a model handed a DIFFERENT hash reports a break on an intact linear chain"
          <| fun _ ->
              let lanes, _ = planLaneGen.Lanes 2 (ConfRng.ofSeed 3650)

              let rs =
                  chainUnder OpStream.defaultHash planW planLaneGen.State0 (Human "writer") (List.concat lanes)

              Expect.isNonEmpty rs "the generated lane produced a chain"
              let p, m = chainVerdicts OpStream.defaultHash swappedHash planW rs
              Expect.equal p ChainIntact "production built this chain and sees it intact"
              Expect.notEqual m p "a model recomputing hashes with a different function must disagree"

              Expect.equal
                  (chainVerdictReason m)
                  (Some ChainBreakReason.HashMismatch)
                  "and it disagrees by naming the check that failed"

          // ---- what the theorem's one premise buys, measured in BOTH directions ----

          testCase "under a NON-INJECTIVE hash the tamper is invisible — to production and to the model alike"
          <| fun _ ->
              // `tamper_detected` rests on exactly one hypothesis: the content id determines the
              // content. `opBlindHash` breaks it in the smallest way that is still a hash — it
              // folds the parents and the actor and drops the op — so two nodes differing only in
              // their op mint one id. Run the SAME tamper both ways: under the real hash it is
              // found, under the collapsing one it is not, and the model tracks production in both
              // directions. That is the premise shown to be load-bearing rather than decorative.
              let lane = [ AddItem("z1", "one"); AddItem("z2", "two") ]

              let tamper (dag: Dag.T<PlanOp>) : Dag.T<PlanOp> =
                  let k, n =
                      dag.Nodes |> Map.toList |> List.find (fun (_, n) -> n.Op = AddItem("z1", "one"))

                  DagOf.nodes (
                      Map.add
                          k
                          { n with
                              Op = AddItem("z1", "TAMPERED") }
                          dag.Nodes
                  )

              let real = dagUnder OpStream.defaultHash planW planLaneGen.BaseOp [ lane ]
              let weak = dagUnder opBlindHash planW planLaneGen.BaseOp [ lane ]

              // The probe did the thing it claims to be about: two different hashes, two different
              // sets of ids. Without this the case could be comparing one DAG with itself.
              Expect.notEqual (Dag.heads real) (Dag.heads weak) "the two hashes mint different ids"

              let rp, rm =
                  dagVerdicts OpStream.defaultHash OpStream.defaultHash planW (tamper real)

              Expect.equal
                  (dagVerdictReason rp)
                  (Some DagBreakReason.ContentIdMismatch)
                  "under an injective hash the tamper is found"

              Expect.equal rm rp "and the model finds it identically"

              let wp, wm = dagVerdicts opBlindHash opBlindHash planW (tamper weak)
              Expect.equal wp DagIntact "a hash that drops the op cannot see an op tamper — this is the premise"
              Expect.equal wm wp "and neither can the model, under the same hash"

          // ---- the corpus dag/ family: its SHAPES, not its addresses ----

          testCase "the corpus dag/ family's shapes, rebuilt through production's own append and merge"
          <| fun _ ->
              match SiblingCorpus.resolve "dag" with
              | SiblingCorpus.NotAsked why -> skiptest why
              | SiblingCorpus.Absent why -> failtest why
              | SiblingCorpus.Found root ->
                  let shapes =
                      match readDagShapes root with
                      | Ok s -> s
                      | Error why -> failtestf "a dag/ fixture could not be read: %s" why

                  Expect.isGreaterThan (List.length shapes) 3 "the dag/ family carries its four shapes"

                  // THE BOUNDARY, as an assertion rather than a sentence, so it goes red if it ever
                  // moves. This family's addresses belong to the UI host's DAG-record wire format —
                  // 64 characters, minted by a different pre-image under SHA-256 — where Core's
                  // default content id is 8. They are not Core content ids and cannot be made into
                  // one; what is used below is the SHAPES the fixtures carry.
                  Expect.equal (OpStream.defaultHash "" "").Length 8 "Core's default content id is 8 characters"

                  for s in shapes do
                      Expect.equal
                          s.CorpusHash.Length
                          64
                          (sprintf "%s: a dag/ address is 64 characters, so it is not a Core content id" s.Fixture)

                  // Rebuild each shape through production: no parents (genesis), one (a linear
                  // step), two (a MERGE — the shape the generated pools never build, because
                  // `foldOnce` only ever grows chains off one base).
                  let mutable dag: Dag.T<string> = Dag.empty
                  let mutable built: (string * string * string) list = []
                  let mutable merges = 0

                  let coreIdOf (h: string) =
                      built |> List.tryPick (fun (_, ch, id) -> if ch = h then Some id else None)

                  let isBuilt (f: string) =
                      built |> List.exists (fun (bf, _, _) -> bf = f)

                  for want in [ 0; 1; 2 ] do
                      for s in shapes do
                          if List.length s.CorpusParents = want && not (isBuilt s.Fixture) then
                              let id, d =
                                  match s.CorpusParents |> List.map coreIdOf with
                                  | [] ->
                                      Dag.append (OpStream.canonical OpStream.defaultHash rawW) s.Actor s.OpJson "" dag
                                      |> Reference.built
                                  | [ Some p ] ->
                                      Dag.append (OpStream.canonical OpStream.defaultHash rawW) s.Actor s.OpJson p dag
                                      |> Reference.built
                                  | [ Some l; Some r ] ->
                                      merges <- merges + 1

                                      Dag.merge (OpStream.canonical OpStream.defaultHash rawW) s.Actor s.OpJson l r dag
                                      |> Reference.built
                                  | _ -> failtestf "%s: it names a parent no earlier fixture built" s.Fixture

                              dag <- d
                              built <- built @ [ s.Fixture, s.CorpusHash, id ]

                  Expect.equal (List.length built) (List.length shapes) "every shape was rebuilt"

                  Expect.isGreaterThan
                      merges
                      0
                      "the two-parent MERGE shape was exercised — the one the generated pools never build"

                  match mintsProductionIds OpStream.defaultHash rawW dag with
                  | Some why -> failtest why
                  | None -> ()

                  let p, m = dagVerdicts OpStream.defaultHash OpStream.defaultHash rawW dag
                  Expect.equal p DagIntact "the rebuilt DAG is intact"
                  Expect.equal m p "and the model agrees"

                  let tampers =
                      walkerTally
                      |> Differential.over (dagTampers (fun (o: string) -> o + " ") dag) (fun (what, tampered) t ->
                          let tp, tm = dagVerdicts OpStream.defaultHash OpStream.defaultHash rawW tampered

                          t
                          |> Differential.check (tm = tp) (fun () ->
                              sprintf "tamper %s: the two walkers disagree" what)
                          |> tamperCounted DagIntact tp)

                  Differential.expectAgrees "the corpus dag/ shapes' tampers" tampers

                  Expect.isGreaterThan
                      (Tally.count "Detected" tampers)
                      0
                      "the tampers were found, so the agreement is not vacuous"

                  // Phase 64.1 on the shape only this family supplies: a merge node's id does not
                  // depend on which head the reconciler called left. This is the production side of
                  // the model's `merge_id_parent_order_independent`, and the last assertion is the
                  // model's own pre-image measured against the id production minted. The merge is
                  // made INTO the rebuilt DAG, which holds both parents: since Phase 296 a merge whose
                  // parent names no node is refused, and this used to merge into an empty one.
                  match built with
                  | (_, _, a) :: (_, _, b) :: _ ->
                      let lr, _ =
                          Dag.merge (OpStream.canonical OpStream.defaultHash rawW) (Human "m") "{}" a b dag
                          |> Reference.built

                      let rl, _ =
                          Dag.merge (OpStream.canonical OpStream.defaultHash rawW) (Human "m") "{}" b a dag
                          |> Reference.built

                      Expect.equal lr rl "merge(A,B) and merge(B,A) converge to one content id"

                      Expect.equal
                          (Chain.node_hash
                              OpStream.defaultHash
                              rawW.Encode
                              ordinalLe
                              [ b; a ]
                              (Actor.encode (Human "m"))
                              "{}")
                          lr
                          "and the model mints that same id from the reversed parent list"
                  | _ -> failtest "two rebuilt nodes are needed to exercise a merge"

          // ---- Phase 145 — the two splices, and the premise each of them actually needs ----

          testCase "an actor encoding CAN carry the splice separator — Phase 136's stated reason is REFUTED"
          <| fun _ ->
              // Phase 136's README says the `|` splice is unambiguous because "`Actor.encode` emits
              // a JSON object that never contains one". It can. `jstr` escapes `"`, `\` and the C0
              // controls; `|` is 0x7C and is none of those. Recorded as an assertion rather than a
              // sentence so it goes red the day the escaper changes — at which point the model's
              // separator-freedom reading would become available and this case is the notice.
              let enc = Actor.encode (Human "a|b")
              Expect.equal enc "{\"kind\":\"human\",\"id\":\"a|b\"}" "the id is copied through unescaped"

              Expect.isTrue
                  (enc.Contains "|")
                  "an actor encoding containing the separator is REACHABLE — separator freedom is false of production"

              Expect.isTrue
                  ((Actor.encode (Agent("m|1", "v|2", "a|3"))).Contains "|")
                  "and on the Agent case at every field"

              // The pre-image really does then carry several separators, which is the whole
              // question: it is not that the splice is safe because there is only one.
              let preimage = Actor.encode (Human "a|b") + "|" + "A|x|t"

              Expect.isGreaterThan
                  (preimage.Split('|').Length - 1)
                  1
                  "the composite pre-image carries several separators"

          testCase "Actor.encode is a PREFIX-FREE code — the premise the splice lemma actually takes"
          <| fun _ ->
              // `Chain.actor_op_splice_unambiguous` asks for exactly this and nothing stronger: no
              // encoding is a proper prefix of another. It is what a self-delimiting JSON object
              // buys, and it is what survives the refutation above.
              let encs = adversarialActors |> List.map Actor.encode
              Expect.equal (List.length (List.distinct encs)) (List.length encs) "the population encodes injectively"

              let offenders =
                  [ for x in encs do
                        for y in encs do
                            if properPrefix x y then
                                yield sprintf "%s is a proper prefix of %s" x y ]

              Expect.isEmpty offenders (sprintf "no actor encoding may be a proper prefix of another: %A" offenders)

              // The probe can lose: drop the JSON envelope and the code stops being prefix-free on
              // this very population, so the emptiness above is a measurement rather than a shape.
              let bare = adversarialActors |> List.map Actor.id

              Expect.isNonEmpty
                  [ for x in bare do
                        for y in bare do
                            if properPrefix x y then
                                yield x, y ]
                  "a bare-id encoder IS prefix-comparable here — the check above can fail"

          testCase "the actor/op splice recovers both halves over the adversarial population"
          <| fun _ ->
              // The model's conclusion, measured on production's own encodings: over every pair of
              // (actor, op-encoding) the composite `Actor.encode a + \"|\" + enc` determines both.
              let pairs =
                  [ for a in adversarialActors do
                        for o in adversarialOpEncodings -> a, o ]

              let spliced = pairs |> List.map (fun (a, o) -> Actor.encode a + "|" + o, (a, o))
              let byBytes = spliced |> List.map fst

              let collisions =
                  spliced
                  |> List.groupBy fst
                  |> List.filter (fun (_, g) -> (g |> List.map snd |> List.distinct |> List.length) > 1)

              Expect.isEmpty
                  (collisions |> List.map fst)
                  "two distinct (actor, op) pairs splice to one pre-image — the splice is ambiguous"

              Expect.equal (List.length (List.distinct byBytes)) (List.length byBytes) "and no two pairs collide at all"

              // Both directions. A bare-id actor encoder makes the SAME population collide, so the
              // green above is not a property of the population.
              let bareSpliced = pairs |> List.map (fun (a, o) -> Actor.id a + "|" + o, (a, o))

              Expect.isLessThan
                  (List.length (List.distinct (bareSpliced |> List.map fst)))
                  (List.length bareSpliced)
                  "under a bare-id encoder the same population DOES collide — the comparison can lose"

          testCase "a content id carries no comma and is not empty, and a parent id that would names no node"
          <| fun _ ->
              // `Chain.parent_splice_unambiguous` asks for exactly two things of a parent id. Both
              // are properties of what mints one. The default hash is FNV-1a rendered `x8`.
              let lanes, _ = planLaneGen.Lanes 3 (ConfRng.ofSeed 3660)
              let dag = dagUnder OpStream.defaultHash planW planLaneGen.BaseOp lanes
              let keys = dag.Nodes |> Map.toList |> List.map fst
              Expect.isGreaterThan (List.length keys) 3 "the generated DAG has nodes to measure"

              for k in keys do
                  Expect.equal k.Length 8 (sprintf "a content id is 8 characters: %s" k)
                  Expect.isFalse (k.Contains ",") (sprintf "a content id carries no comma: %s" k)
                  Expect.isFalse (k = "") "a content id is not empty"

              // And the ids production stores as PARENTS are exactly those keys, in an intact DAG.
              for KeyValue(_, n) in dag.Nodes do
                  for p in n.Parents do
                      Expect.isTrue (List.contains p keys) (sprintf "a stored parent is a key: %s" p)

              // Since Phase 300 `Dag.append` VALIDATES the parent string it is handed: a comma-bearing
              // parent id — whose pre-image splices into the merge of the two ids it names — is
              // refused with a typed fault, so the premise holds of everything production BUILDS.
              let ambiguous = List.head keys + "," + List.item 1 keys

              match
                  Dag.append
                      (OpStream.canonical OpStream.defaultHash planW)
                      (Human "w")
                      planLaneGen.BaseOp
                      ambiguous
                      dag
              with
              | Error(DagAppendFault.CommaInParentId p) -> Expect.equal p ambiguous "the refusal names the offending id"
              | other ->
                  failtestf
                      "a comma-bearing parent id must be refused, got %A"
                      (Result.map (fun (w: Dag.Written<PlanOp>) -> w.Id) other)

              // What such an append WOULD have minted is exactly the merge of the two ids — the
              // splice, measured: a node carrying the merge's content id and the one comma-bearing
              // parent passes the content check. What stops it sitting in a VERIFIED DAG that was not
              // built here (a hand-made or loaded one) is the walk's second clause: it names no node.
              let mergeId, _ =
                  Dag.merge
                      (OpStream.canonical OpStream.defaultHash planW)
                      (Human "w")
                      planLaneGen.BaseOp
                      (List.head keys)
                      (List.item 1 keys)
                      dag
                  |> Reference.built

              let spoiled =
                  dag.Nodes
                  |> Map.add
                      mergeId
                      { Id = mergeId
                        Parents = [ ambiguous ]
                        Actor = Human "w"
                        Op = planLaneGen.BaseOp }
                  |> DagOf.nodes

              match Dag.firstBreak (OpStream.canonical OpStream.defaultHash planW) spoiled with
              | Some b ->
                  // Typed since Phase 147 — `Reason` was the string this case compared when it was
                  // written, and comparing the case is what that phase made possible.
                  Expect.equal
                      b.Reason
                      DagBreakReason.MissingParent
                      "a comma-bearing parent id is caught as a missing parent"

                  Expect.equal b.Got ambiguous "and the break names the offending id"
              | None -> failtest "a DAG naming a parent that is not a node must not verify"

          testCase "the op codec this differential runs on certifies injective through the kit's own law"
          <| fun _ ->
              // The model's FOURTH premise (`Chain.op_codec_injective`) is the domain's, so it is a
              // parameter there and a sampled law here. This runs that law over the very witness the
              // DAG and chain differentials above use, so the premise is certified for the domain
              // whose ids those cases compare rather than for a domain invented for the purpose.
              let results = Conformance.codecInjectivityLaws planW planStreamGen 3670 200

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "the work-plan op codec is not certified injective:\n%s" (String.concat "\n" fails)

              // The premise shown to be load-bearing and the law shown to be able to lose, in one
              // move: a codec that drops the op's payload aliases two ops onto one encoding, so two
              // DAG nodes differing only in that op mint ONE content id and the tamper between them
              // is invisible to production's own walker. That is what the parameter buys.
              let lossy = { planW with Encode = fun _ -> "op" }

              Expect.isTrue
                  (Conformance.codecInjectivityLaws lossy planStreamGen 3670 200
                   |> List.exists (fun r -> not r.Passed))
                  "a codec that erases its op must fail the law — otherwise the green above cannot lose"

              let lane = [ AddItem("z1", "one"); AddItem("z2", "two") ]
              let weak = dagUnder OpStream.defaultHash lossy planLaneGen.BaseOp [ lane ]

              let tampered =
                  let k, n =
                      weak.Nodes
                      |> Map.toList
                      |> List.find (fun (_, n) -> n.Op = AddItem("z1", "one"))

                  DagOf.nodes (
                      Map.add
                          k
                          { n with
                              Op = AddItem("z1", "TAMPERED") }
                          weak.Nodes
                  )

              Expect.isTrue
                  (Dag.verifyDag (OpStream.canonical OpStream.defaultHash lossy) tampered)
                  "under a non-injective CODEC — the hash untouched — production's own walker cannot see the tamper"

          // ---- Phase 197 — the envelope parse: the linear side's premise, discharged ----

          testCase "production's chain payload IS the model's envelope, over the adversarial actors, ops and sequences"
          <| fun _ ->
              // `rec_payload` is a hand-spelled copy of `OpStream.canonicalConfig.Payload`, and the
              // parse is a theorem about the copy. This is the byte-for-byte check that the copy is
              // the production envelope, over the population every case below runs on.
              for seq in adversarialSeqs do
                  for a in adversarialActors do
                      for o in adversarialEnvelopeOps do
                          Expect.equal
                              (modelEnvelope showPos seq (Actor.encode a) o)
                              (StreamConfig.canonical.Payload seq a o)
                              (sprintf "the model's envelope is production's payload for seq=%d actor=%A op=%s" seq a o)

          testCase "the envelope parse recovers the sequence, the actor and the op over the adversarial population"
          <| fun _ ->
              // `Chain.rec_payload_parsed`, measured: over every (seq, actor, op-encoding) triple the
              // envelope determines all three — no two distinct triples share an envelope.
              let triples =
                  [ for seq in adversarialSeqs do
                        for a in adversarialActors do
                            for o in adversarialEnvelopeOps -> seq, Actor.encode a, o ]

              let envelopes =
                  triples
                  |> List.map (fun (seq, a, o) -> modelEnvelope showPos seq a o, (seq, a, o))

              let collisions =
                  envelopes
                  |> List.groupBy fst
                  |> List.filter (fun (_, g) -> (g |> List.map snd |> List.distinct |> List.length) > 1)
                  |> List.map fst

              Expect.isEmpty
                  collisions
                  "two distinct (seq, actor, op) triples share one envelope — the parse is ambiguous"

              Expect.equal
                  (envelopes |> List.map fst |> List.distinct |> List.length)
                  (List.length envelopes)
                  "and no two triples collide at all"

          testCase
              "under a FREE actor code the same envelope IS ambiguous — the prefix-free premise is what the parse spends"
          <| fun _ ->
              // The go-red for the actor split. The parse cuts the actor at the comma that opens
              // `,"op":` and needs the actor code prefix-free to do it; over a free actor string the
              // cut is not forced, and the ambiguity the model's section 6b names is REAL: the actor
              // `A,"op":B` with op `C` and the actor `A` with op `B,"op":C` mint one envelope.
              let ambiguous = modelEnvelope showPos 3 "A,\"op\":B" "C"
              Expect.equal ambiguous (modelEnvelope showPos 3 "A" "B,\"op\":C") "one envelope, two (actor, op) readings"

              // What saves production: both actors are `Actor.encode` outputs there, and the code is
              // prefix-free (Phase 145's measurement), so neither reading's actor is an encoding.
              let isEncoding (s: string) =
                  adversarialActors |> List.exists (fun a -> Actor.encode a = s)

              Expect.isFalse (isEncoding "A,\"op\":B") "the free actor is not an actor encoding"
              Expect.isFalse (isEncoding "A") "nor is the other reading's"

              // And the SAME ambiguity cannot be built from encodings: no actor encoding is a proper
              // prefix of another, so no encoding extended by `,"op":<x>` is an encoding.
              let encs = adversarialActors |> List.map Actor.encode

              Expect.isEmpty
                  [ for x in encs do
                        for y in encs do
                            if y.StartsWith(x + ",\"op\":", System.StringComparison.Ordinal) then
                                yield x, y ]
                  "no actor encoding is another encoding followed by the op separator"

          testCase
              "the sequence numeral is comma-free and injective, and a numeral WITH a comma refutes the first-comma split rather than the envelope"
          <| fun _ ->
              // `seq_numeral_code`, measured on production's `show` — `string : int -> string`.
              let rendered =
                  adversarialSeqs @ [ for i in 0..2000 -> i ]
                  |> List.distinct
                  |> List.map (fun i -> i, string i)

              for (i, s) in rendered do
                  Expect.isFalse (s.Contains ",") (sprintf "the numeral for %d carries no comma: %s" i s)

              Expect.equal
                  (rendered |> List.map snd |> List.distinct |> List.length)
                  (List.length rendered)
                  "and `string` is injective on the sequence numbers"

              // Where the premise is SPENT, and the finding beside it. The proof cuts the sequence at
              // the FIRST comma (`app_sep_split`), which a thousands-separated renderer defeats: for
              // seq 1000 it prints `1,000`, and the first-comma cut reads the sequence as `1`. That
              // is a refutation of the PROOF STEP, not of the envelope — the bytes after that comma
              // are `000,"actor":`, which no envelope's skeleton begins with, so the envelope for
              // seq 1000 is still not the envelope for seq 1. Comma-freedom is therefore SUFFICIENT
              // and what production has, not NECESSARY: the weakest premise the digraph `,"actor":`
              // would admit is that a numeral never spells the separator, and it was not taken
              // because the one-symbol premise is true of `string` and is a one-line split. Recorded
              // so the next reader does not mistake the numeral's comma for an ambiguity.
              let grouped (p: Chain.pos) : string =
                  (intOfPos p).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)

              Expect.equal (grouped (posOfInt 1000)) "1,000" "the perturbed renderer carries a comma"

              let env = modelEnvelope grouped 1000 "X" "{}"
              let afterSkeleton = env.Substring("{\"seq\":".Length)

              Expect.equal
                  (afterSkeleton.Substring(0, afterSkeleton.IndexOf ','))
                  "1"
                  "the first-comma cut misreads the grouped numeral — `app_sep_split`'s premise is load-bearing"

              Expect.notEqual
                  env
                  (modelEnvelope grouped 1 "X" "{}")
                  "yet the envelopes for 1000 and 1 still differ — the skeleton after the comma is not `000`"

              Expect.isFalse
                  (afterSkeleton.StartsWith("1,\"actor\":", System.StringComparison.Ordinal))
                  "so no second reading of the envelope exists; the comma refutes the split, not the parse"

          testCase "a payload splice breaks the chain — production and the oracle agree, over the work-plan stream"
          <| fun _ ->
              // `payload_splice_breaks_chain`, beside production: one record's content swapped for
              // another record's, its stored hash and prev-link kept, in a chain `verifyChain`
              // accepted — and both walkers refuse it, reporting the same break.
              payloadSpliceDifferential
                  "work-plan payload splices"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  planW
                  planLaneGen
                  3970
                  40
              |> expectWalkerAgreement "work-plan payload splices"

          testCase
              "a payload splice breaks the chain — production and the oracle agree, over the reference witness's stream"
          <| fun _ ->
              payloadSpliceDifferential
                  "reference payload splices"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  treeW
                  treeLaneGen
                  3971
                  30
              |> expectWalkerAgreement "reference payload splices"

          testCase
              "a payload splice is INVISIBLE under a hash that folds the payload — the parse cannot outrun the hash premise"
          <| fun _ ->
              // The differential's own go-red, and the honest boundary of the discharge: the parse
              // makes the ENVELOPE injective, and the record hash is `h prev envelope`, so a hash that
              // is not injective on its second argument hides a splice from production and the model
              // alike. `hash_injective` stays the one premise the linear side assumes, exactly as the
              // DAG's does.
              let payloadBlind: HashFn = fun prev _ -> OpStream.defaultHash prev "payload"
              let lanes, _ = planLaneGen.Lanes 2 (ConfRng.ofSeed 3972)

              let rs =
                  chainUnder payloadBlind planW planLaneGen.State0 (Human "writer") (List.concat lanes)

              Expect.isGreaterThan (List.length rs) 1 "at least two records appended, so a splice exists"

              let splices =
                  payloadSplices rs |> List.filter (fun (what, _) -> what.StartsWith "actor+op@")

              Expect.isNonEmpty splices "there is an actor-and-op splice to try"

              for (what, spliced) in splices do
                  let p, m = chainVerdicts payloadBlind payloadBlind planW spliced

                  Expect.equal
                      p
                      ChainIntact
                      (sprintf "%s: under a payload-blind hash production cannot see the splice" what)

                  Expect.equal m ChainIntact (sprintf "%s: nor can the model" what) ]
