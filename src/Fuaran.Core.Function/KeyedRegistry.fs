namespace Fuaran.Core

// ============================================================================
//  The keyed-registry lattice (Phase 316) — ONE implementation of the lifecycle
//  the invocable seams' registries share: `CapabilityRegistry`, `FunctionRegistry`
//  and `QueryRegistry` each hold their entries as a map keyed by id, and each
//  used to write `register` (refusing a duplicate) / `tryFind` / `enumerate`
//  over it by hand, with nothing to remove, replace, narrow or combine.
//
//  A registry is a LATTICE here, not an append log: `restrict` narrows it to a
//  set of ids (the meet with that set), `union` joins two registries whose ids
//  are disjoint and refuses a collision (no silent overwrite, as `register`),
//  `unregister` removes one id it holds and refuses one it does not, and
//  `replace` swaps the entry under an id it holds, through the seam's own
//  admission gate. Every refusal is the SEAM's own typed error, built by the
//  function the caller passes — so the duplicate and unknown refusals stay
//  `DuplicateCapability`/`NoSuchCapability` on the capability seams and
//  `DuplicateQuery`/`NoSuchQuery` on the query seam.
//
//  Internal: the three registries are the surface. FSharp.Core only,
//  Fable-clean, total.
// ============================================================================

/// The lifecycle algebra over a registry's id-keyed map (Phase 316). Each function is total and
/// leaves the map unchanged on a refusal.
module internal KeyedRegistry =

    /// The ids the map holds, in id order — what an unknown-id refusal enumerates.
    let ids (m: Map<string, 'V>) : string list = m |> Map.toList |> List.map fst

    /// Add `v` under `id`: refused `duplicate id` when the map holds `id`, refused `admit v`'s
    /// fault when the seam's admission gate refuses `v`, and the map extended by exactly one entry
    /// otherwise. The duplicate check runs first, as every registry's `register` always has.
    let register
        (duplicate: string -> 'E)
        (admit: 'V -> 'E option)
        (id: string)
        (v: 'V)
        (m: Map<string, 'V>)
        : Result<Map<string, 'V>, 'E> =
        if Map.containsKey id m then
            Error(duplicate id)
        else
            match admit v with
            | Some fault -> Error fault
            | None -> Ok(Map.add id v m)

    /// Remove `id`: refused `unknown id held` (the ids the map holds, in id order) when the map
    /// does not hold it. `unregister id (register id v m) = Ok m` for an id `m` does not hold.
    let unregister
        (unknown: string -> string list -> 'E)
        (id: string)
        (m: Map<string, 'V>)
        : Result<Map<string, 'V>, 'E> =
        if Map.containsKey id m then
            Ok(Map.remove id m)
        else
            Error(unknown id (ids m))

    /// Swap the entry under `id` for `v`: refused `unknown` when the map does not hold `id`, and
    /// refused the admission gate's fault when it refuses `v` — so a replacement is held to exactly
    /// what a registration is. The id is kept; only its entry moves.
    let replace
        (unknown: string -> string list -> 'E)
        (admit: 'V -> 'E option)
        (id: string)
        (v: 'V)
        (m: Map<string, 'V>)
        : Result<Map<string, 'V>, 'E> =
        if not (Map.containsKey id m) then
            Error(unknown id (ids m))
        else
            match admit v with
            | Some fault -> Error fault
            | None -> Ok(Map.add id v m)

    /// The entries whose id is in `keep` — the meet with that set. An id in `keep` the map does not
    /// hold is ignored, so `restrict` never widens: what it returns is a sub-map of what it is given.
    let restrict (keep: Set<string>) (m: Map<string, 'V>) : Map<string, 'V> =
        m |> Map.filter (fun id _ -> keep.Contains id)

    /// The join of two maps whose ids are disjoint; refused `duplicate id` naming the FIRST id, in id
    /// order, that both hold — no silent overwrite, as `register`. Associative: either association
    /// of three maps is refused exactly when two of them share an id, and agrees otherwise.
    let union (duplicate: string -> 'E) (a: Map<string, 'V>) (b: Map<string, 'V>) : Result<Map<string, 'V>, 'E> =
        match b |> Map.toList |> List.tryFind (fun (id, _) -> Map.containsKey id a) with
        | Some(id, _) -> Error(duplicate id)
        | None -> Ok(Map.fold (fun acc id v -> Map.add id v acc) a b)
