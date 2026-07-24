namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.Lease (Phase 84) — a generic lease / claim strand: claims over an
//  abstract resource axis, with grant / release / expiry as a closed `LeaseOp`
//  algebra. Generalises the coordination a downstream work-dispatcher
//  hand-rolls (holder + resource set + host-supplied expiry, one live lease
//  per work item). Total `apply` / `canApply` (GP4) where a conflicting
//  claim is a TYPED rejection enumerating the current holder + the overlapping
//  resources (GP5); a canonical wire codec (GP3 — Fable-clean encode AND decode);
//  and its own `Fuaran.Core.OpStream` `StreamWitness`, so lease history is
//  append-only, hash-chained, and replayable exactly like the rest of the family.
//
//  The resource axis reuses the existing `IdWitness<'Res>` (GP2 — no new witness
//  shape): a resource is whatever the host's ids name — file paths (the dispatcher),
//  Phase 78 footprint tree-addresses. Expiry takes TIME AS DATA (the Phase 27
//  determinism discipline): the host supplies a monotonic logical timestamp per op;
//  Core never reads a clock, so replay is deterministic. Analysis + state, NOT
//  scheduling (GP6): there is no process loop — `Expire` is an op the host emits with
//  "now" as data, never a timer Core runs.
//
//  It introduces NO base type and NO new permanent witness field: the op DU is
//  self-contained over `Lease<'Res>`, and `OpStream` stays generic over its
//  `(apply, encode, decode)` witness — the lease stream is just an instance of it.
//  The resource-axis `IdWitness<'Res>` is a per-call parameter (`captureEffect`'s GP2
//  pattern), so one module serves file-path and tree-address resources alike.
// ============================================================================

/// A single held lease. `Holder` is the claimant key — at most one active lease per holder (the
/// dispatcher's `"lease-" + phaseId`); a re-`Claim` by the same holder renews it in place.
/// `Resources` are the claimed resource ids (the dispatcher's contested `KeyFiles`). `GrantedAt` /
/// `Ttl` are host-supplied logical time carried as DATA (Phase 27): the lease is live until a
/// host-supplied "now" reaches `GrantedAt + Ttl`. Core does no calendar arithmetic beyond that
/// integer add + compare, so it stays clock-free and Fable-clean; the host maps its wall-clock /
/// ISO-8601 instants onto this monotonic axis.
type Lease<'Res> =
    { Holder: string
      Resources: 'Res list
      GrantedAt: int64
      Ttl: int64 }

/// A lease op over a resource axis. The closed algebra the strand certifies:
type LeaseOp<'Res> =
    /// Claim `resources` for `holder` from logical time `grantedAt` for a `ttl` duration (both as
    /// data). Rejected if any resource is held by a different holder; a same-holder claim renews.
    | Claim of holder: string * resources: 'Res list * grantedAt: int64 * ttl: int64
    /// Release the lease held by `holder` (`NoSuchLease` if it holds none).
    | Release of holder: string
    /// Expire every lease whose `grantedAt + ttl <= now` — the host supplies "now" as data (GP6: an
    /// op, not a clock Core reads).
    | Expire of now: int64

/// The lease projection: the currently-active leases (at most one per holder). Equality is
/// structural (conformance compares folded states), so `apply` keeps `Active` in a canonical order —
/// insertion order, with a same-holder re-`Claim` replacing in place rather than appending.
type LeaseState<'Res> = { Active: Lease<'Res> list }

/// Why a lease op was rejected — recoverable + enumerated (GP5), never a throw (GP4).
type LeaseRejection<'Res> =
    /// A `Claim` overlaps an active lease held by a DIFFERENT holder. Names the current holder and the
    /// exact overlapping resources (GP5) — the enumerated-conflict contract the strand exists to give.
    | Conflict of holder: string * overlap: 'Res list
    /// A `Release` named a holder that holds no active lease.
    | NoSuchLease of holder: string

/// The lease op-algebra: `apply` / `canApply` over a `LeaseState`, the wire codec, and the
/// `Fuaran.Core.OpStream` `StreamWitness` that makes a lease stream chainable + replayable. Every
/// entry point threads an `IdWitness<'Res>` — the host's resource-id vocabulary (GP2, no new witness
/// shape).
[<RequireQualifiedAccess>]
module Lease =

    /// The empty lease projection (no active leases). A function (not a value) to sidestep the
    /// value-restriction on the generic record.
    let emptyState<'Res> () : LeaseState<'Res> = { Active = [] }

    // ---- apply ----

    /// Apply a lease op to the projection — total (a typed `LeaseRejection`, never a throw, GP4).
    /// `Claim` grants unless a resource collides with a *different* holder's live lease (a typed
    /// `Conflict` naming that holder + the overlap, GP5); a same-holder claim renews in place.
    /// `Release` drops the holder's lease (`NoSuchLease` if absent). `Expire now` drops every lease
    /// whose `GrantedAt + Ttl <= now` — time as data, so it is deterministic on replay (GP6: an op the
    /// host emits, not a clock Core reads).
    let apply
        (idw: IdWitness<'Res>)
        (op: LeaseOp<'Res>)
        (state: LeaseState<'Res>)
        : Result<LeaseState<'Res>, LeaseRejection<'Res>> =
        match op with
        | Claim(holder, resources, grantedAt, ttl) ->
            let claimKeys = resources |> List.map idw.ToString |> Set.ofList

            let conflict =
                state.Active
                |> List.tryPick (fun l ->
                    if l.Holder = holder then
                        None
                    else
                        match l.Resources |> List.filter (fun r -> Set.contains (idw.ToString r) claimKeys) with
                        | [] -> None
                        | shared -> Some(l.Holder, shared))

            match conflict with
            | Some(otherHolder, shared) -> Error(Conflict(otherHolder, shared))
            | None ->
                let fresh =
                    { Holder = holder
                      Resources = resources
                      GrantedAt = grantedAt
                      Ttl = ttl }

                if state.Active |> List.exists (fun l -> l.Holder = holder) then
                    Ok { Active = state.Active |> List.map (fun l -> if l.Holder = holder then fresh else l) }
                else
                    Ok { Active = state.Active @ [ fresh ] }
        | Release holder ->
            if state.Active |> List.exists (fun l -> l.Holder = holder) then
                Ok { Active = state.Active |> List.filter (fun l -> l.Holder <> holder) }
            else
                Error(NoSuchLease holder)
        | Expire now -> Ok { Active = state.Active |> List.filter (fun l -> l.GrantedAt + l.Ttl > now) }

    /// Dry-run accept/reject, consistent with `apply` by construction (the lease `apply` is a cheap
    /// pure fold, so `canApply` is `apply` with the result discarded — they can never disagree).
    let canApply
        (idw: IdWitness<'Res>)
        (op: LeaseOp<'Res>)
        (state: LeaseState<'Res>)
        : Result<unit, LeaseRejection<'Res>> =
        apply idw op state |> Result.map ignore

    /// Is `holder` currently holding a live lease (in `state`, without an intervening `Expire`)?
    let isHeld (holder: string) (state: LeaseState<'Res>) : bool =
        state.Active |> List.exists (fun l -> l.Holder = holder)

    /// The holder currently claiming `resource`, if any (by the witness's id equality).
    let holderOf (idw: IdWitness<'Res>) (resource: 'Res) (state: LeaseState<'Res>) : string option =
        let key = idw.ToString resource

        state.Active
        |> List.tryPick (fun l ->
            if l.Resources |> List.exists (fun r -> idw.ToString r = key) then
                Some l.Holder
            else
                None)

    // ---- canonical wire codec ----

    // A logical timestamp is carried on the wire as a JSON *string*: the `Fuaran.Core.Wire` `JVal`
    // number model caps `JInt` at int32 (its int53 parse guard), so an int64 would not round-trip as a
    // number. Encoding the decimal text and parsing it back with `int64` keeps full precision and is
    // Fable-clean on both sides.
    let private i64Json (n: int64) : JVal = JStr(string n)

    let private field (k: string) (el: JVal) : Result<JVal, string> =
        match el with
        | JObj fields ->
            match fields |> List.tryFind (fun (n, _) -> n = k) with
            | Some(_, v) -> Ok v
            | None -> Error("missing field: " + k)
        | _ -> Error("expected object for field " + k)

    let private strOf =
        function
        | JStr s -> Ok s
        | _ -> Error "expected string"

    let private arrOf =
        function
        | JArr xs -> Ok xs
        | _ -> Error "expected array"

    let private kindOf el = field "$type" el |> Result.bind strOf

    let private i64Of (el: JVal) : Result<int64, string> =
        match el with
        | JStr s ->
            try
                Ok(int64 s)
            with _ ->
                Error("not an int64: " + s)
        | _ -> Error "expected int64 (encoded as a JSON string)"

    let private mapM (f: 'a -> Result<'b, string>) (xs: 'a list) : Result<'b list, string> =
        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | x :: rest -> f x |> Result.bind (fun v -> go (v :: acc) rest)

        go [] xs

    /// Encode a `LeaseOp` to a `JVal` — a camelCase kind-tag envelope (`$type`), the `Fuaran.Core`
    /// codec discipline. Resource ids serialise through `idw.ToString`; int64 time via `i64Json`.
    let encodeJson (idw: IdWitness<'Res>) (op: LeaseOp<'Res>) : JVal =
        match op with
        | Claim(holder, resources, grantedAt, ttl) ->
            Canon.typed
                "claim"
                [ "holder", JStr holder
                  "resources", JArr(resources |> List.map (fun r -> JStr(idw.ToString r)))
                  "grantedAt", i64Json grantedAt
                  "ttl", i64Json ttl ]
        | Release holder -> Canon.typed "release" [ "holder", JStr holder ]
        | Expire now -> Canon.typed "expire" [ "now", i64Json now ]

    /// The canonical wire string for a `LeaseOp` (Ordinal-sorted keys → byte-identical across hosts).
    let encode (idw: IdWitness<'Res>) (op: LeaseOp<'Res>) : string = Canon.render (encodeJson idw op)

    let decodeJson (idw: IdWitness<'Res>) (el: JVal) : Result<LeaseOp<'Res>, string> =
        kindOf el
        |> Result.bind (fun k ->
            match k with
            | "claim" ->
                field "holder" el
                |> Result.bind strOf
                |> Result.bind (fun holder ->
                    field "resources" el
                    |> Result.bind arrOf
                    |> Result.bind (mapM (strOf >> Result.map idw.OfString))
                    |> Result.bind (fun resources ->
                        field "grantedAt" el
                        |> Result.bind i64Of
                        |> Result.bind (fun g ->
                            field "ttl" el
                            |> Result.bind i64Of
                            |> Result.map (fun t -> Claim(holder, resources, g, t)))))
            | "release" -> field "holder" el |> Result.bind strOf |> Result.map Release
            | "expire" -> field "now" el |> Result.bind i64Of |> Result.map Expire
            | other -> Error("unknown lease op: " + other))

    /// Decode a `LeaseOp` from a wire string (a JSON-syntax error becomes a `not json` message).
    let decode (idw: IdWitness<'Res>) (s: string) : Result<LeaseOp<'Res>, string> =
        match Json.parse s with
        | Error m -> Error("not json: " + m)
        | Ok el -> decodeJson idw el

    /// Render a `LeaseRejection` as a stable human string (resource ids via the witness).
    let rejectionString (idw: IdWitness<'Res>) (r: LeaseRejection<'Res>) : string =
        match r with
        | Conflict(holder, overlap) ->
            "lease conflict: resources { "
            + (overlap |> List.map idw.ToString |> String.concat ", ")
            + " } already held by '"
            + holder
            + "'"
        | NoSuchLease holder -> "no active lease held by '" + holder + "'"

    /// The `Fuaran.Core.OpStream` `StreamWitness` for the lease algebra, closed over the host's
    /// resource-id vocabulary — `apply` + the wire `encode`/`decode`. With it, `OpStream.append` /
    /// `verifyChain` / `replay` / `toJsonl` chain, verify, replay, and persist a lease stream with NO
    /// core change (the witness pattern, GP2 — no new witness field).
    let streamWitnessFor (idw: IdWitness<'Res>) : StreamWitness<LeaseOp<'Res>, LeaseState<'Res>, LeaseRejection<'Res>> =
        { Apply = apply idw
          Encode = encode idw
          Decode = decode idw }
