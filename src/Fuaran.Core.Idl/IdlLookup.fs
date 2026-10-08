namespace Fuaran.Core.Idl

open Fuaran.Core

/// The declaration lookups (Phase 388): a vocabulary's enum, union or record by its name, and a
/// kind by its tag — the first declaration that matches, in declaration order. The one body of
/// each: `Encode` and `Decode` here, and the code generator's emitters (through this package's
/// `InternalsVisibleTo`), each used to declare or inline its own.
module internal IdlLookup =

    /// The enum declared as `name`.
    let tryEnum (idl: Idl) (name: string) : IdlEnum option =
        idl.Enums |> List.tryFind (fun e -> e.Name = name)

    /// The union declared as `name`.
    let tryUnion (idl: Idl) (name: string) : IdlUnion option =
        idl.Unions |> List.tryFind (fun u -> u.Name = name)

    /// The kind declared under the tag `tag`.
    let tryKind (idl: Idl) (tag: string) : IdlKind option =
        idl.Kinds |> List.tryFind (fun k -> k.Tag = tag)

    /// The record declared as `name`.
    let tryRecord (idl: Idl) (name: string) : IdlRecord option =
        idl.Records |> List.tryFind (fun r -> r.Name = name)
