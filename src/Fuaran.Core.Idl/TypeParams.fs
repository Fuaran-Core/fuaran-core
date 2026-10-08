namespace Fuaran.Core.Idl

open Fuaran.Core

/// Type-parameter substitution (Phase 292) — the ONE definition the encoder, the decoder,
/// the sampler and the generator share.
///
/// It was written four times, and the copies had drifted: the sampler's mapped EVERY type
/// variable to the union's FIRST argument, so a two-parameter union sampled its second
/// parameter's slots at the first's type, and the decoder's bare-value arm zipped the
/// parameter list against the argument list without checking their lengths. Keyed by
/// parameter NAME, and bound only when the counts agree.
[<RequireQualifiedAccess>]
module TypeParams =

    /// `t` with every type variable the map names replaced by its binding, at any depth. A
    /// variable the map does not name is left in place, so an unbound one stays visible to
    /// the caller (the encoder refuses it by name; [[Declare.errors]] refuses it at
    /// declaration).
    let rec substitute (subst: Map<string, IdlType>) (t: IdlType) : IdlType =
        match t with
        | TVar v ->
            match Map.tryFind v subst with
            | Some r -> r
            | None -> t
        | TList inner -> TList(substitute subst inner)
        | TMap inner -> TMap(substitute subst inner)
        | TUnion(n, args) -> TUnion(n, List.map (substitute subst) args)
        | other -> other

    /// The substitution an instantiation `TUnion(u.Name, args)` binds — each parameter keyed
    /// by its name to the argument at its position — or `None` when the counts differ, so no
    /// caller ever zips two lists of different length.
    let bind (u: IdlUnion) (args: IdlType list) : Map<string, IdlType> option =
        if List.length u.Params = List.length args then
            Some(Map.ofList (List.zip u.Params args))
        else
            None
