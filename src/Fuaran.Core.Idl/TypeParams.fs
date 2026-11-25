namespace Fuaran.Core.Idl

open Fuaran.Core

/// Type-parameter substitution (Phase 292) — the ONE definition the encoder, the decoder,
/// the sampler and the generator share; the first three read it through the lowering
/// ([[WireForm]]'s union binding) since Phase 513.
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
    ///
    /// Every case is named, so a type added to [[IdlType]] is placed here on purpose. Until
    /// Phase 732 the cases that hold no type were one wildcard, and a tuple, a set and a keyed
    /// map (Phase 639) fell into it: a member holding its parameter in one of them was admitted
    /// by [[Declare.errors]] and refused by the encoder at every instantiation. A hosted slot's
    /// declared wire form and a host function's signature hold types and are left as declared:
    /// the first cannot name a variable ([[Declare.hostedWireErrors]]), and the second is one
    /// sentinel on the wire whatever it names.
    let rec substitute (subst: Map<string, IdlType>) (t: IdlType) : IdlType =
        match t with
        | TVar v ->
            match Map.tryFind v subst with
            | Some r -> r
            | None -> t
        | TList inner -> TList(substitute subst inner)
        | TMap inner -> TMap(substitute subst inner)
        | TSet element -> TSet(substitute subst element)
        | TTuple components -> TTuple(List.map (substitute subst) components)
        | TKeyedMap(key, value) -> TKeyedMap(substitute subst key, substitute subst value)
        | TUnion(n, args) -> TUnion(n, List.map (substitute subst) args)
        | TRecord(n, args) -> TRecord(n, List.map (substitute subst) args)
        | TStr
        | TInt
        | TBool
        | TFloat
        | TEnum _
        | TNode
        | TClosure
        | TFn _
        | TOpaque
        | TJson
        | THosted _
        | TKind
        | TOp
        | TInt64
        | TDecimal
        | TBytes
        | TTimeOfDay
        | TDuration -> t

    /// Each parameter keyed by its name to the argument at its position, or `None` when the
    /// counts differ, so no caller ever zips two lists of different length.
    let private bindNames (pars: string list) (args: IdlType list) : Map<string, IdlType> option =
        if List.length pars = List.length args then
            Some(Map.ofList (List.zip pars args))
        else
            None

    /// The substitution an instantiation `TUnion(u.Name, args)` binds — each parameter keyed
    /// by its name to the argument at its position — or `None` when the counts differ.
    let bind (u: IdlUnion) (args: IdlType list) : Map<string, IdlType> option = bindNames u.Params args

    /// The substitution an instantiation `TRecord(r.Name, args)` binds (Phase 732), as [[bind]]
    /// answers a union's.
    let bindRecord (r: IdlRecord) (args: IdlType list) : Map<string, IdlType> option = bindNames r.Params args
