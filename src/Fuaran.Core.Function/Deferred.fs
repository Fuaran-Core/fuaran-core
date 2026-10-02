namespace Fuaran.Core

/// A domain-general async-result envelope for a capability invocation (Phase 32) — the Compute Layer
/// spec's `Deferred<'T> = Pending | Ready of 'T | Error of e` (§4), put in the SUBSTRATE so every host
/// (Mail network-send, Legal LLM-extraction, CAD server mesh-ops — none of which may depend on
/// `Fuaran.UI`) gets the async-invocation envelope from Core; the UI surface *renders* it
/// (`onLoading`/`onError`) rather than defining it. The failure rides as a rendered `string` (the
/// `BodyFailed` / `InvokeError` text) so the envelope is one-type-parameter + serialisable, and the case
/// is named `Failed` (not `Error`) so it never shadows `Result.Error` in a consumer that opens
/// `Fuaran.Core`.
type Deferred<'T> =
    /// Not yet settled; `map` and `bind` pass it through untouched.
    | Pending
    /// Settled with a value; the only case the capture seam journals.
    | Ready of 'T
    /// Settled with a failure, carried as rendered text so the envelope stays serialisable.
    | Failed of message: string

/// Total combinators over `Deferred` (Phase 32). `map`/`bind` operate on a `Ready`; `Pending`/`Failed`
/// propagate unchanged. `toResult` projects to a `Result` (`Pending` → `Error "pending"`).
module Deferred =

    /// Transforms a `Ready` value; `f` is not called on `Pending` or `Failed`.
    let map (f: 'a -> 'b) (d: Deferred<'a>) : Deferred<'b> =
        match d with
        | Ready v -> Ready(f v)
        | Pending -> Pending
        | Failed m -> Failed m

    /// Chains a further deferred step on a `Ready` value, whose outcome (including `Pending`) is the
    /// result; a `Pending` or `Failed` input short-circuits without calling `f`.
    let bind (f: 'a -> Deferred<'b>) (d: Deferred<'a>) : Deferred<'b> =
        match d with
        | Ready v -> f v
        | Pending -> Pending
        | Failed m -> Failed m

    /// `Ready v` → `Ok v`; `Failed m` → `Error m`; `Pending` → `Error "pending"` — which a
    /// `Failed "pending"` also projects to; `settled` keeps the two apart.
    let toResult (d: Deferred<'T>) : Result<'T, string> =
        match d with
        | Ready v -> Ok v
        | Failed m -> Error m
        | Pending -> Error "pending"

    /// The realized value, if `Ready` (the value the Phase 27 capture seam journals; `Pending`/`Failed`
    /// are not captured — replay re-issues the invocation).
    let tryValue (d: Deferred<'T>) : 'T option =
        match d with
        | Ready v -> Some v
        | _ -> None

    /// The settled outcome, or `None` while `Pending` (Phase 295): `Ready v` → `Some(Ok v)`,
    /// `Failed m` → `Some(Error m)`. `toResult` folds `Pending` into `Error "pending"`, which a body
    /// failing with the message `"pending"` also produces; this keeps the two apart.
    let settled (d: Deferred<'T>) : Result<'T, string> option =
        match d with
        | Ready v -> Some(Ok v)
        | Failed m -> Some(Error m)
        | Pending -> None
