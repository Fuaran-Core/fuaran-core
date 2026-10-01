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
