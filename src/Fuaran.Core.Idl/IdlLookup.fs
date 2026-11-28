namespace Fuaran.Core.Idl

open System.Collections.Generic
open Fuaran.Core

/// The declaration lookups (Phase 388): a vocabulary's enum, union or record by its name, and a
/// kind by its tag — the first declaration that matches, in declaration order. The one body of
/// each: `Encode` and `Decode` here, and the code generator's emitters (through this package's
/// `InternalsVisibleTo`), each used to declare or inline its own.
///
/// Phase 732 — a lookup reads the first few declarations in order and, past them, an index of the
/// list, so a check that looks up each of `n` declarations costs O(n), never the O(n²) of reading
/// the list from its head at each. The index is built once for a list and kept until a lookup asks
/// about a different list. Every part of it compiles under Fable as well as on .NET.
module internal IdlLookup =

    /// How many declarations a lookup reads in order before it reads the index.
    [<Literal>]
    let private scanned = 8

    /// A declaration list and its index: each name of the list and the FIRST item under it.
    type private Indexed<'T> =
        { Items: 'T list
          Index: Dictionary<string, 'T> }

    /// Each name of `items` and the FIRST item under it, in declaration order. String keys compare
    /// ordinally on both runtimes.
    let private indexOf (key: 'T -> string) (items: 'T list) : Indexed<'T> =
        let index = Dictionary<string, 'T>()

        for item in items do
            match key item with
            | null -> ()
            | k when index.ContainsKey k -> ()
            | k -> index[k] <- item

        { Items = items; Index = index }

    /// The first of `items` whose key is `wanted`. `last` holds the index of the list asked about
    /// last; it is replaced whole, never changed in place, so a lookup on another thread reads
    /// either index, each one right for its own list.
    let private first (last: Indexed<'T> option ref) (key: 'T -> string) (items: 'T list) (wanted: string) : 'T option =
        let rec read (left: int) (rest: 'T list) =
            match rest with
            | [] -> None
            | item :: _ when key item = wanted -> Some item
            | _ :: rest when left > 1 || isNull wanted -> read (left - 1) rest
            | _ ->
                // None of the first ones is `wanted`, so the first under it in the whole list is
                // the first under it in the index.
                let indexed =
                    match last.Value with
                    | Some indexed when LanguagePrimitives.PhysicalEquality indexed.Items items -> indexed
                    | _ ->
                        let indexed = indexOf key items
                        last.Value <- Some indexed
                        indexed

                match indexed.Index.TryGetValue wanted with
                | true, item -> Some item
                | false, _ -> None

        read scanned items

    let private enums: Indexed<IdlEnum> option ref = ref None
    let private unions: Indexed<IdlUnion> option ref = ref None
    let private kinds: Indexed<IdlKind> option ref = ref None
    let private records: Indexed<IdlRecord> option ref = ref None

    /// The enum declared as `name`.
    let tryEnum (idl: Idl) (name: string) : IdlEnum option = first enums _.Name idl.Enums name

    /// The union declared as `name`.
    let tryUnion (idl: Idl) (name: string) : IdlUnion option = first unions _.Name idl.Unions name

    /// The kind declared under the tag `tag`.
    let tryKind (idl: Idl) (tag: string) : IdlKind option = first kinds _.Tag idl.Kinds tag

    /// The record declared as `name`.
    let tryRecord (idl: Idl) (name: string) : IdlRecord option = first records _.Name idl.Records name
