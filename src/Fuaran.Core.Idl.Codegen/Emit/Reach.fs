namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit.Core

/// Phase 293 — the ONE reachability walk: the enums, unions and records a kind selection
/// transitively references, in first-reach order. Shared by the F# module emitter and the
/// type emitter; the JSON schema's generic-instantiation worklist and the F* target's
/// monomorphic closure each walk the same declarations by the same finders.
module internal Reach =

    /// Transitive closure of the enum / union / record types referenced from a set of kinds —
    /// through union case fields, record fields, list elements, map value-types, and union
    /// type-args. Records ↔ unions are mutually recursive (`CellKindErased.ButtonGroup` holds a
    /// `ButtonGroupItem` record; `FormField` holds a `FormFieldKind` union), so the walk visits
    /// both. Returns each set filtered to IDL declaration order.
    let referenced (idl: Idl) (kinds: IdlKind list) : IdlEnum list * IdlUnion list * IdlRecord list =
        let enums = System.Collections.Generic.HashSet<string>()
        let unions = System.Collections.Generic.HashSet<string>()
        let records = System.Collections.Generic.HashSet<string>()

        let rec visit (t: IdlType) =
            match t with
            | TEnum n -> enums.Add n |> ignore
            | TUnion(n, args) ->
                args |> List.iter visit

                if unions.Add n then
                    match IdlLookup.tryUnion idl n with
                    | Some u -> u.Cases |> List.iter (fun c -> c.Fields |> List.iter (fun f -> visit f.Type))
                    | None -> ()
            | TRecord n ->
                if records.Add n then
                    match IdlLookup.tryRecord idl n with
                    | Some r -> r.Fields |> List.iter (fun f -> visit f.Type)
                    | None -> ()
            | TList inner -> visit inner
            | TMap vt -> visit vt
            // Phase 691 — a declared type NAMED IN A SIGNATURE is reachable. `TFn.FSharp`
            // is free text the walker cannot parse, so this searches it for the names the
            // IDL already declares. Crude, but self-limiting (it can only ever mark
            // something the IDL defines), and without it a type reached ONLY through a
            // signature — `Motion`, on the host-only node fields — is declared in the IDL
            // and then never emitted, so the generated module names a type it lacks.
            | TFn sg ->
                for e in idl.Enums do
                    if sg.FSharp.Contains e.Name then
                        enums.Add e.Name |> ignore

                for r in idl.Records do
                    if sg.FSharp.Contains r.Name then
                        visit (TRecord r.Name)

                for u in idl.Unions do
                    if sg.FSharp.Contains u.Name then
                        visit (TUnion(u.Name, []))
            // Same name-scan for a hosted slot: its type and codec expressions may
            // reference declared types AND generated codecs (`encRangePair`,
            // `decBinding`) — a type reached only that way must still be emitted.
            | THosted h ->
                // Phase 252 — a declared wire form is a reachability root like any field type.
                h.Wire |> Option.iter visit
                let text = h.FSharp + " " + h.Encode + " " + h.Decode

                for e in idl.Enums do
                    if text.Contains e.Name then
                        enums.Add e.Name |> ignore

                for r in idl.Records do
                    if text.Contains r.Name then
                        visit (TRecord r.Name)

                for u in idl.Unions do
                    if text.Contains u.Name then
                        visit (TUnion(u.Name, []))
            | _ -> ()

        kinds |> List.iter (fun k -> k.Fields |> List.iter (fun f -> visit f.Type))
        // Phase 690 — the node envelope is a reachability ROOT too. Its records are
        // reachable from no kind (nothing nests a `SemanticStyle`), so walking only
        // the kinds emits a `Node` whose field types were never declared.
        idl.NodeFields |> List.iter (fun f -> visit f.Type)

        idl.Enums |> List.filter (fun e -> enums.Contains e.Name),
        idl.Unions |> List.filter (fun u -> unions.Contains u.Name),
        idl.Records |> List.filter (fun r -> records.Contains r.Name)
