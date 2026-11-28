namespace Fuaran.Core.Idl

open System.Collections.Generic
open System.Runtime.CompilerServices
open Fuaran.Core

/// The declaration lookups (Phase 388): a vocabulary's enum, union or record by its name, and a
/// kind by its tag — the first declaration that matches, in declaration order. The one body of
/// each: `Encode` and `Decode` here, and the code generator's emitters (through this package's
/// `InternalsVisibleTo`), each used to declare or inline its own.
///
/// Phase 732 — a lookup reads the first few declarations in order and, past them, an index of the
/// list built once and kept while the list lives, so a check that looks up each of `n` declarations
/// costs O(n), never the O(n²) of reading the list from its head at each.
module internal IdlLookup =

    /// How many declarations a lookup reads in order before it reads the index.
    [<Literal>]
    let private scanned = 8

    /// Each name of `items` and the FIRST item under it, in declaration order.
    let private indexOf (key: 'T -> string) (items: 'T list) : Dictionary<string, 'T> =
        let index = Dictionary<string, 'T>(System.StringComparer.Ordinal)

        for item in items do
            match key item with
            | null -> ()
            | k -> index.TryAdd(k, item) |> ignore

        index

    /// The first of `items` whose key is `wanted`.
    let private first
        (indexes: ConditionalWeakTable<'T list, Dictionary<string, 'T>>)
        (key: 'T -> string)
        (items: 'T list)
        (wanted: string)
        : 'T option =
        let rec read (left: int) (rest: 'T list) =
            match rest with
            | [] -> None
            | item :: _ when key item = wanted -> Some item
            | _ :: rest when left > 1 || isNull wanted -> read (left - 1) rest
            | _ ->
                // None of the first ones is `wanted`, so the first under it in the whole list is
                // the first under it in the index.
                match
                    indexes.GetValue(items, ConditionalWeakTable.CreateValueCallback(indexOf key)).TryGetValue wanted
                with
                | true, item -> Some item
                | false, _ -> None

        read scanned items

    let private enums =
        ConditionalWeakTable<IdlEnum list, Dictionary<string, IdlEnum>>()

    let private unions =
        ConditionalWeakTable<IdlUnion list, Dictionary<string, IdlUnion>>()

    let private kinds =
        ConditionalWeakTable<IdlKind list, Dictionary<string, IdlKind>>()

    let private records =
        ConditionalWeakTable<IdlRecord list, Dictionary<string, IdlRecord>>()

    /// The enum declared as `name`.
    let tryEnum (idl: Idl) (name: string) : IdlEnum option = first enums _.Name idl.Enums name

    /// The union declared as `name`.
    let tryUnion (idl: Idl) (name: string) : IdlUnion option = first unions _.Name idl.Unions name

    /// The kind declared under the tag `tag`.
    let tryKind (idl: Idl) (tag: string) : IdlKind option = first kinds _.Tag idl.Kinds tag

    /// The record declared as `name`.
    let tryRecord (idl: Idl) (name: string) : IdlRecord option = first records _.Name idl.Records name
