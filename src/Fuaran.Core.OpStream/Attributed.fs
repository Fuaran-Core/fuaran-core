namespace Fuaran.Core

/// An attribution envelope wrapping a domain op with "who did what" provenance (Phase 81): the actor
/// and session ids, an optional turn/sequence within the session, and a host-supplied timestamp — all
/// carried INSIDE the chained op via `OpStream.Attributed.liftWitness`, not via a new witness field
/// (GP2 — the per-op witness-metadata seam F8 was rejected and stays rejected; this *wraps*, it does
/// not seam). Because the envelope rides inside the op's wire encoding, the existing hash chain covers
/// it: re-attributing a chained op breaks `verifyChain` exactly as op-tampering does — provenance is
/// tamper-evident for free, with no change to the `OpStream` surface.
///
/// Identity is **host-side vocabulary**: `Actor` / `Session` are opaque strings — Core owns no identity
/// model (a distinct axis from the chain-level typed `Actor` DU folded into `OpRecord`). `Turn` is an
/// optional ordinal within a session. `At` is a timestamp carried **as data** — Core never reads a
/// clock (the Phase 27 effect discipline); the host supplies it, `""` meaning unstamped.
type Attributed<'Op> =
    { Actor: string
      Session: string
      Turn: int option
      At: string
      Op: 'Op }

/// The bodies of the `OpStream.Attributed` members (Phase 332). Internal: a consumer reaches each one
/// through its forward in `OpStream.Attributed` (OpStream.fs), which carries the member's contract
/// and documentation.
module internal OpStreamAttributed =
    open Fuaran.Core.OpStreamJsonl

    // ---- attributed-stream lift (Phase 81) ----

    module Attributed =

        let encodeEnvelope (encodeInner: 'Op -> string) (a: Attributed<'Op>) : string =
            "{\"actor\":"
            + jstr a.Actor
            + ",\"session\":"
            + jstr a.Session
            + ",\"turn\":"
            + (match a.Turn with
               | Some t -> string t
               | None -> "null")
            + ",\"at\":"
            + jstr a.At
            + ",\"op\":"
            + encodeInner a.Op
            + "}"

        let decodeEnvelope
            (decodeInner: string -> Result<'Op, string>)
            (line: string)
            : Result<Attributed<'Op>, string> =
            Jsonl.parseLine 1 line
            |> bindR (fun l ->
                let turn =
                    match Jsonl.tryRawField "turn" l with
                    | None
                    | Some "null" -> Ok None
                    | Some _ -> Jsonl.intField "turn" l |> Result.map Some

                Jsonl.stringField "actor" l
                |> bindR (fun actor ->
                    Jsonl.stringField "session" l
                    |> bindR (fun session ->
                        turn
                        |> bindR (fun turn ->
                            Jsonl.stringField "at" l
                            |> bindR (fun at ->
                                Jsonl.rawField "op" l
                                |> bindR (fun raw -> decodeInner raw |> Result.mapError (Jsonl.refuse l))
                                |> Result.map (fun op ->
                                    { Actor = actor
                                      Session = session
                                      Turn = turn
                                      At = at
                                      Op = op }))))))
            |> Result.mapError spanFault

        let liftWitness (w: StreamWitness<'Op, 'State, 'Rej>) : StreamWitness<Attributed<'Op>, 'State, 'Rej> =
            { Apply = fun a state -> w.Apply a.Op state
              Encode = encodeEnvelope w.Encode
              Decode = decodeEnvelope w.Decode }

        /// Group an attributed stream's records by a projected key, preserving per-key append order
        /// (records for one key stay in stream order). The shared engine for `byActor` / `bySession`.
        let private groupBy
            (keyOf: Attributed<'Op> -> string)
            (records: OpRecord<Attributed<'Op>> list)
            : Map<string, OpRecord<Attributed<'Op>> list> =
            // Linear (Phase 296): each group is built reversed with a cons, then reversed once — the
            // old `group @ [ r ]` copied the group on every record, quadratic in a busy actor's share.
            (Map.empty, records)
            ||> List.fold (fun acc r ->
                let k = keyOf r.Op
                Map.add k (r :: (Map.tryFind k acc |> Option.defaultValue [])) acc)
            |> Map.map (fun _ group -> List.rev group)

        let byActor (records: OpRecord<Attributed<'Op>> list) : Map<string, OpRecord<Attributed<'Op>> list> =
            groupBy _.Actor records

        let bySession (records: OpRecord<Attributed<'Op>> list) : Map<string, OpRecord<Attributed<'Op>> list> =
            groupBy _.Session records
