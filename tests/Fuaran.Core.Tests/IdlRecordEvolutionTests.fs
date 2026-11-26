module Fuaran.Core.Tests.IdlRecordEvolutionTests

// ---------------------------------------------------------------------------
// Phase 732 — how a vocabulary that moves a TYPE PARAMETER or a TYPE ARGUMENT is graded and
// proposed, for a record and a union through the one rule.
//
// The classifier grades each move by what an OLD document does under the new vocabulary
// (Phase 304), so every grade here stands beside the document that shows it: one the old
// vocabulary wrote, which the new one reads back to the same bytes, or refuses.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Tests.IdlRecordParamsTests

/// A declaration as a union of one case `Mk`, or as a record: the two forms every case below
/// is run in.
let private declare (asUnion: bool) (name: string) (pars: string list) (fields: IdlField list) : Idl -> Idl =
    fun idl ->
        if asUnion then
            { idl with
                Unions = idl.Unions @ [ union name pars [ case "Mk" fields ] ] }
        else
            { idl with
                Records = idl.Records @ [ record name pars fields ] }

/// A reference to such a declaration at `args`.
let private refer (asUnion: bool) (name: string) (args: IdlType list) : IdlType =
    if asUnion then TUnion(name, args) else TRecord(name, args)

/// A value of such a declaration.
let private value (asUnion: bool) (fields: (string * IdlValue) list) : IdlValue =
    if asUnion then VUnion("Mk", fields) else VRecord fields

/// The kind `K`, holding one required member `b` of type `held`.
let private holder (held: IdlType) : Idl =
    vocabulary [ kind "K" [ f "b" held Required ] ] [] []

/// `K.b` holding `Box` at `args`, and `Box` declaring `pars` and one required member `v`.
let private box (asUnion: bool) (pars: string list) (v: IdlType) (args: IdlType list) : Idl =
    holder (refer asUnion "Box" args)
    |> declare asUnion "Box" pars [ f "v" v Required ]

/// One row of a verdict, as the test reads it: the severity, and which rule spoke of what.
let private rows (before: Idl) (after: Idl) : (Severity * string) list =
    match Diff.ofValues (before, None) (after, None) with
    | Error e -> failtestf "the pair did not classify: %s" e
    | Ok verdict ->
        verdict.Changes
        |> List.map (fun c ->
            c.Severity,
            (match c.Change with
             | UnionParamsChanged(n, b, a)
             | RecordParamsChanged(n, b, a) -> sprintf "params %s %A -> %A" n b a
             | FieldTypeRestated(o, n, _, _) -> sprintf "restated %s.%s" o.Describe n
             | FieldTypeChanged(o, n, _, _) -> sprintf "changed %s.%s" o.Describe n
             | other -> sprintf "%A" other))

/// What the new vocabulary does with a document the old one wrote: `Ok` of the bytes it writes
/// back, or the refusal.
let private under (before: Idl) (after: Idl) (document: IdlValue) : string * Result<string, string> =
    Expect.isEmpty (Declare.errors before) "the old vocabulary is well-formed"
    Expect.isEmpty (Declare.errors after) "the new vocabulary is well-formed"

    match Encode.encode before document with
    | Error e -> failtestf "the old vocabulary did not write the document: %A" e
    | Ok bytes -> bytes, (Decode.decode after bytes |> Result.bind (Encode.encode after))

/// The old document reads back to the bytes it had.
let private stillReads (before: Idl) (after: Idl) (document: IdlValue) : unit =
    let bytes, read = under before after document
    Expect.equal read (Ok bytes) "the old document reads to the same value and writes the same bytes"

/// The old document is refused.
let private stopsReading (before: Idl) (after: Idl) (document: IdlValue) : unit =
    let _, read = under before after document
    Expect.isError read "the old document no longer reads"

/// `kind K`'s row subject, and the declaration's own, in the words `Owner.Describe` uses.
let private owned (asUnion: bool) (name: string) : string =
    if asUnion then
        sprintf "union %s.Mk" name
    else
        sprintf "record %s" name

let private node (b: IdlValue) : IdlValue = VNode("n", "K", [ "b", b ])

let private forms: (string * bool) list = [ "a union", true; "a record", false ]

[<Tests>]
let tests =
    testList
        "Phase 732 — a type parameter and a type argument, graded and proposed"
        [ testList
              "the classifier grades by what an old document does"
              [ for what, asUnion in forms do
                    let one = node (value asUnion [ "v", VInt 1 ])

                    testCase (what + ": a parameter no member names, added, is host surface") (fun _ ->
                        let before = box asUnion [] TInt []
                        let after = box asUnion [ "T" ] TInt [ TStr ]

                        Expect.equal
                            (rows before after)
                            [ HostSurfaceOnly, """params Box [] -> ["T"]"""
                              HostSurfaceOnly, "restated kind K.b" ]
                            "the parameter list and the reference moved; no member's type did"

                        stillReads before after one)

                    testCase (what + ": the same parameter removed is host surface") (fun _ ->
                        let before = box asUnion [ "T" ] TInt [ TStr ]
                        let after = box asUnion [] TInt []

                        Expect.equal
                            (rows before after)
                            [ HostSurfaceOnly, """params Box ["T"] -> []"""
                              HostSurfaceOnly, "restated kind K.b" ]
                            "the mirror of the addition"

                        stillReads before after one)

                    testCase (what + ": a parameter renamed is the parameter list's row and no other") (fun _ ->
                        let before = box asUnion [ "T" ] (TVar "T") [ TInt ]
                        let after = box asUnion [ "U" ] (TVar "U") [ TInt ]

                        Expect.equal
                            (rows before after)
                            [ HostSurfaceOnly, """params Box ["T"] -> ["U"]""" ]
                            "a member naming the parameter by its new name did not move"

                        stillReads before after one)

                    testCase (what + ": two parameters exchanged under references as written is a wire break") (fun _ ->
                        let pair (pars: string list) =
                            holder (refer asUnion "Pair" [ TInt; TStr ])
                            |> declare asUnion "Pair" pars [ f "x" (TVar "A") Required ]

                        let before = pair [ "A"; "B" ]
                        let after = pair [ "B"; "A" ]

                        Expect.equal
                            (rows before after)
                            [ HostSurfaceOnly, """params Pair ["A"; "B"] -> ["B"; "A"]"""
                              BreakingWire, sprintf "changed %s.x" (owned asUnion "Pair") ]
                            "the member is written as it was and is another type at the reference"

                        stopsReading before after (node (value asUnion [ "x", VInt 1 ])))

                    testCase
                        (what
                         + ": an argument changed where a member holds the parameter is a wire break")
                        (fun _ ->
                            let before = box asUnion [ "T" ] (TVar "T") [ TInt ]
                            let after = box asUnion [ "T" ] (TVar "T") [ TStr ]

                            Expect.equal
                                (rows before after)
                                [ BreakingWire, "changed kind K.b" ]
                                "the member's type moved"

                            stopsReading before after one)

                    testCase (what + ": an argument widened from int to float is additive") (fun _ ->
                        let before = box asUnion [ "T" ] (TVar "T") [ TInt ]
                        let after = box asUnion [ "T" ] (TVar "T") [ TFloat ]

                        Expect.equal
                            (rows before after)
                            [ Additive, "changed kind K.b" ]
                            "the widening rule, at an argument"

                        stillReads before after one)

                    testCase
                        (what
                         + ": an argument changed where no member holds the parameter is host surface")
                        (fun _ ->
                            let before = box asUnion [ "T" ] TInt [ TInt ]
                            let after = box asUnion [ "T" ] TInt [ TStr ]

                            Expect.equal
                                (rows before after)
                                [ HostSurfaceOnly, "restated kind K.b" ]
                                "no member's type moved"

                            stillReads before after one)

                    testCase
                        (what
                         + ": made generic, every reference applying the type it held, is host surface")
                        (fun _ ->
                            let before = box asUnion [] TInt []
                            let after = box asUnion [ "T" ] (TVar "T") [ TInt ]

                            Expect.equal
                                (rows before after)
                                [ HostSurfaceOnly, """params Box [] -> ["T"]"""
                                  HostSurfaceOnly, "restated kind K.b"
                                  HostSurfaceOnly, sprintf "restated %s.v" (owned asUnion "Box") ]
                                "the member names the parameter, and is the type it was at the one reference"

                            stillReads before after one)

                    testCase (what + ": made generic and applied at another type is a wire break") (fun _ ->
                        let before = box asUnion [] TInt []
                        let after = box asUnion [ "T" ] (TVar "T") [ TStr ]

                        Expect.equal
                            (rows before after)
                            [ HostSurfaceOnly, """params Box [] -> ["T"]"""
                              BreakingWire, "changed kind K.b"
                              BreakingWire, sprintf "changed %s.v" (owned asUnion "Box") ]
                            "the member is another type at the reference, and both rows say so"

                        stopsReading before after one)

                    testCase
                        (what
                         + ": a member's declared type moved under a reference as written is a wire break")
                        (fun _ ->
                            let before = box asUnion [ "T" ] (TVar "T") [ TInt ]
                            let after = box asUnion [ "T" ] (TList(TVar "T")) [ TInt ]

                            Expect.equal
                                (rows before after)
                                [ BreakingWire, sprintf "changed %s.v" (owned asUnion "Box") ]
                                "the declaration's row carries it; the reference did not move"

                            stopsReading before after one)

                    testCase
                        (what
                         + ": an instantiation reached through a generic holder is graded at its arguments")
                        (fun _ ->
                            // `K.b: Outer<int>`, `Outer<T> { inner: Box<T> }`: `Box` is reached at `int` only
                            // through `Outer`'s own parameter.
                            let through (boxPars: string list) (v: IdlType) (inner: IdlType list) =
                                holder (refer asUnion "Outer" [ TInt ])
                                |> declare asUnion "Outer" [ "T" ] [ f "inner" (refer asUnion "Box" inner) Required ]
                                |> declare asUnion "Box" boxPars [ f "v" v Required ]

                            let nested = node (value asUnion [ "inner", value asUnion [ "v", VInt 1 ] ])

                            let before = through [ "T" ] (TVar "T") [ TVar "T" ]
                            let moved = through [ "T" ] (TList(TVar "T")) [ TVar "T" ]

                            Expect.equal
                                (rows before moved)
                                [ BreakingWire, sprintf "changed %s.v" (owned asUnion "Box") ]
                                "the member moved at the instantiation the holder passes down"

                            stopsReading before moved nested

                            // The parameter dropped from `Box`, which held `int` at every instantiation reached.
                            let fixedAtInt = through [ "T" ] TInt [ TVar "T" ]
                            let dropped = through [] TInt []

                            Expect.equal
                                (rows fixedAtInt dropped)
                                [ HostSurfaceOnly, """params Box ["T"] -> []"""
                                  HostSurfaceOnly, sprintf "restated %s.inner" (owned asUnion "Outer") ]
                                "a generic declaration's own member, restated at every instantiation reached"

                            stillReads fixedAtInt dropped nested)

                    testCase
                        (what
                         + ": an instantiation a contract's argument reaches is one a declaration is graded at")
                        (fun _ ->
                            // The kind's reference moves with the declaration and reads as it did; the
                            // contract's argument is written as it was, and is another type.
                            let vocab (v: IdlType) (atKind: IdlType) =
                                ContractsIdl.withContracts
                                    [ { ContractsIdl.orders with
                                          Methods =
                                              ContractsIdl.orders.Methods
                                              |> List.map (fun m ->
                                                  { m with
                                                      Arguments =
                                                          m.Arguments
                                                          |> List.mapi (fun i x ->
                                                              if i = 0 then
                                                                  { x with
                                                                      Type = refer asUnion "Box" [ TInt ] }
                                                              else
                                                                  x) }) } ]
                                |> fun idl ->
                                    { idl with
                                        Kinds =
                                            idl.Kinds
                                            @ [ kind "K" [ f "b" (refer asUnion "Box" [ atKind ]) Required ] ] }
                                |> declare asUnion "Box" [ "T" ] [ f "v" v Required ]

                            Expect.equal
                                (rows (vocab (TList(TVar "T")) TInt) (vocab (TVar "T") (TList TInt)))
                                [ BreakingWire, sprintf "changed %s.v" (owned asUnion "Box")
                                  HostSurfaceOnly, "restated kind K.b" ]
                                "the declaration's member moved at the contract's instantiation")

                    testCase
                        (what
                         + ": every owner that declares no parameter is where an instantiation is reached from")
                        (fun _ ->
                            // `Box` made generic, applied at the type it held, and reached from one position
                            // only: an op, the node envelope, a declaration that declares no parameter.
                            let reachedFrom
                                (place: IdlType -> Idl -> Idl)
                                (pars: string list)
                                (v: IdlType)
                                (args: IdlType list)
                                =
                                vocabulary [ kind "K" [ f "b" TInt Required ] ] [] []
                                |> place (refer asUnion "Box" args)
                                |> declare asUnion "Box" pars [ f "v" v Required ]

                            let places: (string * (IdlType -> Idl -> Idl)) list =
                                [ "an op",
                                  (fun t idl ->
                                      { idl with
                                          Ops = [ kind "Put" [ f "b" t Required ] ] })
                                  "the node envelope",
                                  (fun t idl ->
                                      { idl with
                                          NodeFields = [ f "b" t Optional ] })
                                  "a declaration", (fun t idl -> declare asUnion "Wrap" [] [ f "b" t Required ] idl) ]

                            for where, place in places do
                                Expect.contains
                                    (rows
                                        (reachedFrom place [] TInt [])
                                        (reachedFrom place [ "T" ] (TVar "T") [ TInt ]))
                                    (HostSurfaceOnly, sprintf "restated %s.v" (owned asUnion "Box"))
                                    (where + ": the member is graded at the instantiation reached from there"))

                    testCase (what + ": a declaration that holds itself, made generic, is host surface") (fun _ ->
                        let tree (pars: string list) (v: IdlType) (self: IdlType list) (at: IdlType list) =
                            holder (refer asUnion "Tree" at)
                            |> declare
                                asUnion
                                "Tree"
                                pars
                                [ f "v" v Required; f "kids" (TList(refer asUnion "Tree" self)) Required ]

                        let before = tree [] TInt [] []
                        let after = tree [ "T" ] (TVar "T") [ TVar "T" ] [ TInt ]

                        Expect.equal
                            (rows before after |> List.map fst |> List.distinct)
                            [ HostSurfaceOnly ]
                            "every row is host surface: the comparison meets the instantiation again and stops"

                        stillReads
                            before
                            after
                            (node (
                                value
                                    asUnion
                                    [ "v", VInt 1
                                      "kids", VList [ value asUnion [ "v", VInt 2; "kids", VList [] ] ] ]
                            )))

                    testCase
                        (what
                         + ": a member moved in a generic declaration no reference reaches is a wire break")
                        (fun _ ->
                            let alone (v: IdlType) =
                                vocabulary [ kind "K" [ f "b" TInt Required ] ] [] []
                                |> declare asUnion "Box" [ "T" ] [ f "v" v Required ]

                            Expect.equal
                                (rows (alone (TVar "T")) (alone (TList(TVar "T"))))
                                [ BreakingWire, sprintf "changed %s.v" (owned asUnion "Box") ]
                                "no reference shows what an old document held, so the move is not excused")

                    testCase (what + ": a recursion that grows its argument is graded, and read as moved") (fun _ ->
                        // No member holds the parameter, so no document moves; the instantiations are
                        // infinitely many, so the comparison has no end to reach and does not excuse it.
                        let grow (at: IdlType) =
                            holder (refer asUnion "Grow" [ at ])
                            |> declare
                                asUnion
                                "Grow"
                                [ "T" ]
                                [ f "next" (TList(refer asUnion "Grow" [ TList(TVar "T") ])) Required ]

                        Expect.equal
                            (rows (grow TInt) (grow TStr))
                            [ BreakingWire, "changed kind K.b" ]
                            "the classifier answers, on the side of the break"

                        // And the instantiations the revision reaches cannot all be read either, so a
                        // member restated beside it is not excused: `Box`, made generic at the type it
                        // held, reads as moved, the grade that claims nothing of an old document.
                        let beside (idl: Idl) (pars: string list) (v: IdlType) (args: IdlType list) =
                            { idl with
                                Kinds = idl.Kinds @ [ kind "L" [ f "c" (refer asUnion "Box" args) Required ] ] }
                            |> declare asUnion "Box" pars [ f "v" v Required ]

                        Expect.contains
                            (rows (beside (grow TInt) [] TInt []) (beside (grow TInt) [ "T" ] (TVar "T") [ TInt ]))
                            (BreakingWire, sprintf "changed %s.v" (owned asUnion "Box"))
                            "the classifier answers, and on the side of the break") ]

          testList
              "the snapshot"
              [ testCase "a record's parameters and a reference's arguments are read" (fun _ ->
                    match Diff.parse (Artifact.render boxed) None with
                    | Error e -> failtest e
                    | Ok snapshot ->
                        Expect.equal
                            snapshot.RecordParams
                            (Map.ofList [ "Box", [ "T" ]; "Local", [ "T" ]; "Pair", [ "A"; "B" ] ])
                            "each record that declares a parameter, by name"

                        Expect.equal
                            (snapshot.Kinds["Holder"] |> List.map (fun fs -> fs.Name, fs.Label))
                            [ "box", "record Box<str>"
                              "pair", "record Pair<int, record Box<bool>>"
                              "binding", "union Binding<float>" ]
                            "a record reference is labelled with its arguments, as a union's is")

                testCase
                    "a vocabulary declaring no parameterised record reads as it did, and does not differ from itself"
                    (fun _ ->
                        for name, idl in fixtures do
                            match Diff.parse (Artifact.render idl) None with
                            | Error e -> failtestf "%s: %s" name e
                            | Ok snapshot ->
                                Expect.isEmpty snapshot.RecordParams (name + ": no record declares a parameter")

                                Expect.isEmpty
                                    (Diff.changes snapshot snapshot)
                                    (name + ": a revision compared with itself has no row")) ]

          testList
              "the proposal surface"
              [ testCase "a read delta applies arguments at a record reference" (fun _ ->
                    let delta =
                        """{ "op": "addField",
                             "owner": { "$type": "kind", "name": "Holder" },
                             "field": { "name": "extra",
                                        "type": { "$type": "record", "name": "Box", "args": [ { "$type": "int" } ] },
                                        "optionality": { "$type": "optional" } } }"""

                    match Proposal.parseDetailed (IdlProposalTests.completeJson delta "{}") with
                    | Error e -> failtestf "the delta did not read: %A" e
                    | Ok proposal ->
                        Expect.equal
                            proposal.Delta
                            [ AddField(OwnerKind "Holder", f "extra" (TRecord("Box", [ TInt ])) Optional) ]
                            "the reference's arguments are read"

                        match Proposal.applyDelta boxed proposal.Delta with
                        | Error e -> failtestf "the delta was refused: %s" e
                        | Ok proposed ->
                            let extra (v: IdlValue) =
                                VNode(
                                    "n",
                                    "Holder",
                                    [ "box", VRecord [ "value", VStr "x" ]
                                      "binding", VUnion("Static", [ "value", VFloat 1.5 ])
                                      "extra", VRecord [ "value", v ] ]
                                )

                            Expect.isOk
                                (Encode.encode proposed (extra (VInt 7)))
                                "a document holds the member at the argument"

                            Expect.isError (Encode.encode proposed (extra (VStr "seven"))) "and no other type there")

                testCase "a read delta naming a host-surface type as an argument is refused at the argument" (fun _ ->
                    let delta =
                        """{ "op": "addField",
                             "owner": { "$type": "kind", "name": "Holder" },
                             "field": { "name": "extra",
                                        "type": { "$type": "record", "name": "Box", "args": [ { "$type": "opaque" } ] },
                                        "optionality": { "$type": "optional" } } }"""

                    match Proposal.parseDetailed (IdlProposalTests.completeJson delta "{}") with
                    | Ok _ -> failtest "an opaque argument was accepted from a proposal document"
                    | Error e ->
                        Expect.equal
                            (e.Code, e.Path)
                            (DecodeCode.NotAdmitted,
                             [ PathSegment.Key "delta"
                               PathSegment.Index 0
                               PathSegment.Key "field"
                               PathSegment.Key "type"
                               PathSegment.Key "args"
                               PathSegment.Index 0
                               PathSegment.Key "$type" ])
                            "NotAdmitted at the argument's tag, as a union reference's argument is")

                testCase "a delta may widen a generic record at its own parameter" (fun _ ->
                    match Proposal.applyDelta boxed [ AddField(OwnerRecord "Box", f "spare" (TVar "T") Optional) ] with
                    | Error e -> failtestf "refused: %s" e
                    | Ok proposed ->
                        Expect.equal
                            ((proposed.Records |> List.find (fun r -> r.Name = "Box")).Fields
                             |> List.map _.Name)
                            [ "value"; "more"; "maybe"; "spare" ]
                            "the member is appended to the record")

                testCase "a delta that breaks one of the declaration's three rules is refused by applyDelta" (fun _ ->
                    let refusal (delta: ProposalDelta list) : string =
                        match Proposal.applyDelta boxed delta with
                        | Ok _ -> failtestf "the delta was applied: %A" delta
                        | Error e -> e

                    Expect.stringContains
                        (refusal [ AddField(OwnerKind "Holder", f "extra" (TRecord("Box", [])) Optional) ])
                        "applies record 'Box' to 0 type argument(s); it declares 1"
                        "a wrong argument count"

                    Expect.stringContains
                        (refusal [ AddField(OwnerRecord "Box", f "spare" (TVar "U") Optional) ])
                        "type variable 'U' is not a parameter of the record declaring it"
                        "an unknown parameter"

                    Expect.stringContains
                        (refusal [ AddField(OwnerKind "Holder", f "extra" (TVar "T") Optional) ])
                        "type variable 'T' names a type parameter, and a kind declares none"
                        "a parameter that would reach the wire unsubstituted") ] ]
