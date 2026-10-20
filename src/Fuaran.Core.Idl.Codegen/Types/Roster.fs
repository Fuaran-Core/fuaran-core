namespace Fuaran.Core.Idl.Codegen.Types

open Fuaran.Core

// Phase 524 (DECISIONS.md D125, amended) — the stability classifier's host roster and the obligations a classified change places on it, lifted out of the
// `Diff` module so the classifier splits one concern per file (`Diff/*.fs`).

/// What a roster host does with the wire, which decides the obligations a change
/// raises for it.
type HostRole =
    /// Encodes and decodes documents itself: every wire-touching change obliges its
    /// encoder, decoder and schema in the same change-set.
    | CodecHost
    /// Renders a tree it does not decode itself: obliged to add or drop a render arm on a
    /// NodeKind-set change, and only checked otherwise.
    | RenderProjection

/// One entry of a vocabulary's host roster (`manifest.json` `hosts`).
type Host =
    {
        /// The host's identifier, named in its obligation rows. The id `fuaran` is
        /// special: its presence switches on the UI tier's full checklist.
        Id: string
        /// Display only; `?` when the manifest omits it.
        Language: string
        /// Read from the manifest's `role`: `render-projection` gives `RenderProjection`,
        /// anything else (or nothing) gives `CodecHost`.
        Role: HostRole
    }

/// How firmly an obligation binds. `Check` exists because the honest answer
/// to several of these is conditional, and a report that stated them as
/// `Required` would train its reader to skim.
[<RequireQualifiedAccess>]
type Strength =
    /// The surface must change in the same change-set (`MUST` in the report).
    | Required
    /// Whether the surface is bound depends on something the artifact does not record;
    /// the author decides (`CHECK`).
    | Check
    /// The surface is not bound by this change (`n/a`); the weakest, so it loses every
    /// consolidation.
    | NotBound

/// One surface a classified change obliges someone to touch.
type Obligation =
    {
        /// The surface's label, e.g. `codec: <id> (<language>)`. The consolidated set
        /// de-duplicates on it, keeping the strongest `Strength`.
        Surface: string
        /// Printed as `MUST` / `CHECK` / `n/a`; the consolidated set sorts by it, strongest
        /// first.
        Strength: Strength
        /// What to do on that surface, or why it is only conditionally bound.
        Note: string
    }
