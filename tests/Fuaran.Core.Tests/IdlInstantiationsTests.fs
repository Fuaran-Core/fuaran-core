module Fuaran.Core.Tests.IdlInstantiationsTests

// ---------------------------------------------------------------------------
// Phase 732 — whether a vocabulary's generic declarations have finitely many instantiations is
// decided from the declarations (the expanding-cycle test of ECMA-335, Partition II, 9.2), and
// every walk over instantiations asks that instead of counting how deep it has gone.
//
// This file holds: the analysis against a walk that asks nothing first, over a family of
// declarations; the sentence a growing declaration is refused in, and which walk follows what;
// that every entry ANSWERS over a declaration that grows along two members (each is run under a
// time limit: a walk bounded by depth does not return on one, it has 2^depth instantiations to
// enter); that a long finite chain is read to its end, by every entry that once cut it; the count
// an enumeration stops at; and that the default rule is exact over a family that holds growing
// declarations and an instantiation no value reaches.
// ---------------------------------------------------------------------------

open System
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Tests.IdlRecordParamsTests

/// `work`'s answer, or a failed test where it has not answered in `seconds`. The work is left
/// running on its own thread, which ends with the process.
let private within (seconds: float) (what: string) (work: unit -> 'a) : 'a =
    let task = Threading.Tasks.Task.Run(fun () -> work ())

    if task.Wait(TimeSpan.FromSeconds seconds) then
        task.Result
    else
        failtestf "%s did not answer in %.0f s" what seconds

let private refer (asUnion: bool) (name: string) (args: IdlType list) : IdlType =
    if asUnion then TUnion(name, args) else TRecord(name, args)

/// One generic declaration, as a union of one case or as a record.
let private declared
    (asUnion: bool)
    (name: string)
    (pars: string list)
    (members: IdlField list)
    : IdlUnion list * IdlRecord list =
    if asUnion then
        [ union name pars [ case "Mk" members ] ], []
    else
        [], [ record name pars members ]

let private sentence (what: string) (name: string) (from: string) (reaches: string) : string =
    sprintf "the generic %s '%s', which has no finite set of instantiations: %s reaches %s" what name from reaches

let private tooMany =
    "more than 4096 instantiations of the vocabulary's generic declarations"

/// What an entry that prints every instantiation says past the size they may hold between them.
let private oversized =
    "more than 65536 nodes across the instantiations of the vocabulary's generic declarations"

/// `G<T>` growing along TWO members, a list and a map, beside the member `value`; held by `K` at
/// `at`, beside a kind that reaches nothing generic.
let private twoWay (asUnion: bool) (value: IdlField) (at: IdlType) : Idl =
    let self (t: IdlType) = refer asUnion "G" [ t ]

    let unions, records =
        declared
            asUnion
            "G"
            [ "T" ]
            [ f "l" (self (TList(TVar "T"))) Optional
              f "m" (self (TMap(TVar "T"))) Optional
              value ]

    vocabulary [ kind "K" [ f "g" (self at) Optional ]; kind "Plain" [ f "n" TInt Required ] ] unions records

/// What a value on the wire holds from `roots`, read the slow way: every instantiation entered, its
/// wire members substituted and descended, nothing asked of the declarations first. It stops once it
/// has entered more than `budget` instantiations.
let private reached (idl: Idl) (budget: int) (roots: IdlType list) : Collections.Generic.HashSet<IdlType> =
    let seen = Collections.Generic.HashSet<IdlType>(HashIdentity.Structural)

    let rec visit (t: IdlType) =
        if seen.Count <= budget then
            let enter (pars: string list) (args: IdlType list) (members: IdlField list) =
                if pars.Length = args.Length && seen.Add t then
                    let binding = Map.ofList (List.zip pars args)

                    for m in members do
                        if m.Opt <> HostOnly then
                            visit (TypeParams.substitute binding m.Type)

            match t with
            | TList inner
            | TMap inner
            | TSet inner -> visit inner
            | TTuple components -> components |> List.iter visit
            | TKeyedMap(k, v) ->
                visit k
                visit v
            | TRecord(n, args) ->
                idl.Records
                |> List.tryFind (fun r -> r.Name = n)
                |> Option.iter (fun r -> enter r.Params args r.Fields)
            | TUnion(n, args) ->
                idl.Unions
                |> List.tryFind (fun u -> u.Name = n)
                |> Option.iter (fun u -> enter u.Params args (u.Cases |> List.collect _.Fields))
            | _ -> ()

    roots |> List.iter visit
    seen

/// [[reached]], spelled; `None` once it has entered more than `budget` instantiations.
let private walked (idl: Idl) (budget: int) (roots: IdlType list) : Set<string> option =
    let seen = reached idl budget roots

    if seen.Count > budget then
        None
    else
        Some(seen |> Seq.map (sprintf "%A") |> Set.ofSeq)

/// How many instantiations of the declaration `name` reached from the kinds' members ([[reached]])
/// the omit-at-default literal of its member `field` does not fit: the brute force a fault's count
/// is held to. Each is asked of the encoder at the member's type there, through a kind holding it.
let private failing (idl: Idl) (name: string) (field: string) : int =
    let declared =
        (idl.Records |> List.map (fun r -> r.Name, (r.Params, r.Fields)))
        @ (idl.Unions
           |> List.map (fun u -> u.Name, (u.Params, u.Cases |> List.collect _.Fields)))
        |> Map.ofList

    let pars, members = declared[name]
    let m = members |> List.find (fun x -> x.Name = field)

    let literal =
        match m.Opt with
        | OmitDefault v -> v
        | other -> failtestf "%s.%s holds no default: %A" name field other

    let fits (t: IdlType) =
        let probe =
            { idl with
                Kinds = idl.Kinds @ [ kind "Probe" [ f "m" t Required ] ] }

        Result.isOk (Encode.encode probe (VNode("n", "Probe", [ "m", literal ])))

    reached idl 1_000_000 (idl.Kinds |> List.collect (fun k -> k.Fields |> List.map _.Type))
    |> Seq.filter (fun t ->
        match t with
        | TRecord(n, args)
        | TUnion(n, args) when n = name -> not (fits (TypeParams.substitute (Map.ofList (List.zip pars args)) m.Type))
        | _ -> false)
    |> Seq.length

/// The `N` of a fault's `and at least N more`; a failed test where it says none.
let private atLeast (detail: string) : int =
    let m = Text.RegularExpressions.Regex.Match(detail, " and at least (\d+) more")

    if m.Success then
        int m.Groups[1].Value
    else
        failtestf "no count: %s" detail

let private described (r: Result<'a, CodegenError>) : Result<unit, string> =
    r |> Result.map ignore |> Result.mapError CodegenError.describe

/// The document `K` writes holding `inner` at `g`.
let private node (inner: IdlValue) : IdlValue = VNode("n", "K", [ "g", inner ])

[<Tests>]
let tests =
    testList
        "Phase 732 — the instantiations a vocabulary reaches: finitely many or not, decided from the declarations"
        [ testList
              "the analysis"
              [ testCase "agrees with a walk that asks nothing first, over a family of declarations" (fun _ ->
                    let options (p: string) : IdlType list =
                        let v = TVar p

                        [ v
                          TList v
                          TRecord("G", [ v ])
                          TRecord("G", [ TList v ])
                          TRecord("H", [ v ])
                          TRecord("H", [ TList v ])
                          TRecord("H", [ TRecord("G", [ v ]) ])
                          // `Phantom` holds nothing at its parameter; `Box` holds it.
                          TRecord("Phantom", [ TRecord("G", [ TList v ]) ])
                          TRecord("Box", [ TRecord("G", [ TList v ]) ])
                          TTuple [ TRecord("G", [ v ]); TInt ]
                          TRecord("G", [ TInt ])
                          TRecord("Box", [ v ])
                          TRecord("Phantom", [ v ])
                          TRecord("Box", [ TRecord("Box", [ v ]) ])
                          TKeyedMap(TInt, TRecord("H", [ TSet v ])) ]

                    let held (p: string) : IdlType list =
                        let v = TVar p

                        [ v
                          TInt
                          TRecord("G", [ v ])
                          TRecord("G", [ TList v ])
                          TRecord("H", [ v ])
                          TRecord("H", [ TMap v ])
                          TRecord("Box", [ TRecord("H", [ v ]) ]) ]

                    let mutable finite = 0
                    let mutable infinite = 0
                    let roots = [ TRecord("G", [ TStr ]) ]

                    for i, a in List.indexed (options "T") do
                        for b in options "T" |> List.skip i do
                            for h in held "U" do
                                let idl =
                                    vocabulary
                                        [ kind "K" [ f "g" roots.Head Optional ] ]
                                        []
                                        [ record "G" [ "T" ] [ f "a" a Optional; f "b" b Optional ]
                                          record "H" [ "U" ] [ f "h" h Optional ]
                                          record "Phantom" [ "V" ] [ f "n" TInt Optional ]
                                          record "Box" [ "W" ] [ f "item" (TVar "W") Optional ] ]

                                let label = sprintf "G { a: %A; b: %A }, H { h: %A }" a b h

                                match Decl.wireInstantiations idl roots, walked idl 300 roots with
                                | Ok all, Some slow ->
                                    finite <- finite + 1

                                    Expect.equal
                                        (all |> List.map (sprintf "%A") |> Set.ofList)
                                        slow
                                        (label + ": every instantiation the walk enters, and no other")

                                    Expect.equal (List.length all) (Set.count slow) (label + ": each named once")
                                | Error why, None ->
                                    infinite <- infinite + 1

                                    Expect.stringContains
                                        why
                                        "which has no finite set of instantiations: "
                                        (label + ": refused as growing, in the one sentence")
                                | Ok all, None ->
                                    failtestf
                                        "%s: read as finite (%d instantiations), and the walk passed 300"
                                        label
                                        all.Length
                                | Error why, Some slow ->
                                    failtestf "%s: refused (%s), and the walk ends at %d" label why slow.Count

                    // Neither side is vacuous.
                    Expect.isGreaterThan finite 100 "vocabularies that close"
                    Expect.isGreaterThan infinite 100 "and vocabularies that grow")

                testCase "names the declaration and what it reaches itself at" (fun _ ->
                    let refusal (records: IdlRecord list) (root: IdlType) : string =
                        match Decl.wireInstantiations (vocabulary [] [] records) [ root ] with
                        | Error why -> why
                        | Ok all -> failtestf "read as finite: %A" all

                    Expect.equal
                        (refusal
                            [ record "G" [ "T" ] [ f "next" (TRecord("G", [ TList(TVar "T") ])) Optional ] ]
                            (TRecord("G", [ TInt ])))
                        (sentence "record" "G" "G<'T>" "G<list<'T>>")
                        "a declaration that names itself at a larger argument"

                    // The cycle passes two declarations, and only one of its edges expands: followed
                    // once from the declaration first reached, which is where it comes back larger.
                    Expect.equal
                        (refusal
                            [ record "A" [ "T" ] [ f "b" (TRecord("B", [ TVar "T" ])) Optional ]
                              record "B" [ "U" ] [ f "a" (TRecord("A", [ TMap(TVar "U") ])) Optional ] ]
                            (TRecord("A", [ TInt ])))
                        (sentence "record" "A" "A<'T>" "A<map<str, 'T>>")
                        "a cycle through two declarations"

                    Expect.equal
                        (refusal
                            [ record
                                  "G"
                                  [ "A"; "B" ]
                                  [ f "next" (TRecord("G", [ TVar "A"; TTuple [ TVar "B"; TInt ] ])) Optional ] ]
                            (TRecord("G", [ TInt; TStr ])))
                        (sentence "record" "G" "G<'A, 'B>" "G<'A, tuple<'B, int>>")
                        "two parameters, one of them growing inside a tuple"

                    Expect.equal
                        (refusal
                            [ record "Plain" [] [ f "g" (TRecord("G", [ TBool ])) Required ]
                              record
                                  "G"
                                  [ "T" ]
                                  [ f "next" (TKeyedMap(TInt, TRecord("G", [ TSet(TVar "T") ]))) Optional ] ]
                            (TRecord("Plain", [])))
                        (sentence "record" "G" "G<'T>" "G<set<'T>>")
                        "reached through a declaration that declares no parameter, growing inside a set")

                testCase "a cycle that only passes its arguments on is finite, however they are exchanged" (fun _ ->
                    let swap =
                        record "Swap" [ "A"; "B" ] [ f "s" (TRecord("Swap", [ TVar "B"; TVar "A" ])) Optional ]

                    Expect.equal
                        (Decl.wireInstantiations (vocabulary [] [] [ swap ]) [ TRecord("Swap", [ TInt; TStr ]) ])
                        (Ok [ TRecord("Swap", [ TInt; TStr ]); TRecord("Swap", [ TStr; TInt ]) ])
                        "two instantiations, each reaching the other"

                    // A growing declaration nothing reaches refuses nothing.
                    let unreached =
                        record "G" [ "T" ] [ f "next" (TRecord("G", [ TList(TVar "T") ])) Optional ]

                    Expect.equal
                        (Decl.wireInstantiations
                            (vocabulary [] [] [ swap; unreached ])
                            [ TRecord("Swap", [ TInt; TInt ]) ])
                        (Ok [ TRecord("Swap", [ TInt; TInt ]) ])
                        "the roots decide, not the vocabulary")

                testCase "a walk enters what it follows: a value, or every reference written" (fun _ ->
                    // `G<T>` names itself at a larger argument only as the argument of `Phantom`,
                    // which holds nothing at its parameter. No VALUE of `G<list<T>>` is ever held:
                    // the wire reach is two instantiations. The JSON schema states a definition for
                    // every instantiation WRITTEN, and those have no end.
                    let phantom (members: IdlField list) =
                        vocabulary
                            [ kind "K" [ f "g" (TRecord("G", [ TInt ])) Optional ] ]
                            []
                            [ record
                                  "G"
                                  [ "T" ]
                                  [ f "p" (TRecord("Phantom", [ TRecord("G", [ TList(TVar "T") ]) ])) Optional ]
                              record "Phantom" [ "U" ] members ]

                    let unheld = phantom [ f "n" TInt Optional ]
                    let root = [ TRecord("G", [ TInt ]) ]

                    Expect.equal
                        (Decl.wireInstantiations unheld root)
                        (Ok [ TRecord("G", [ TInt ]); TRecord("Phantom", [ TRecord("G", [ TList TInt ]) ]) ])
                        "no value holds the larger instantiation"

                    Expect.stringContains
                        (match within 20.0 "Gen.jsonSchema" (fun () -> described (Gen.jsonSchema unheld)) with
                         | Error why -> why
                         | Ok() -> "emitted")
                        (sentence "record" "G" "G<'T>" "G<list<'T>>")
                        "every instantiation written is named, and they are infinitely many"

                    // Once `Phantom` holds its parameter, a value does hold it.
                    Expect.equal
                        (Decl.wireInstantiations (phantom [ f "held" (TList(TVar "U")) Optional ]) root)
                        (Error(sentence "record" "G" "G<'T>" "G<list<'T>>"))
                        "held through the member that names the parameter"

                    // Held only through an argument position that is itself held: `Outer<V>` passes
                    // its parameter to `Box`, which holds it.
                    let through (inner: string) =
                        vocabulary
                            []
                            []
                            [ record
                                  "G"
                                  [ "T" ]
                                  [ f "p" (TRecord("Outer", [ TRecord("G", [ TList(TVar "T") ]) ])) Optional ]
                              record "Outer" [ "V" ] [ f "o" (TRecord(inner, [ TVar "V" ])) Optional ]
                              record "Box" [ "W" ] [ f "item" (TVar "W") Optional ]
                              record "Phantom" [ "U" ] [ f "n" TInt Optional ] ]

                    Expect.isError
                        (Decl.wireInstantiations (through "Box") root)
                        "an argument position is held where the position it is passed to is"

                    Expect.isOk
                        (Decl.wireInstantiations (through "Phantom") root)
                        "and is not where that position holds nothing")

                testCase "the walk of every reference written reads a hosted slot's declared wire form" (fun _ ->
                    // The JSON schema states a hosted slot as its declared wire form, so an
                    // instantiation written only there is one it names. `G<int>` is written nowhere
                    // else, and `G` grows: there is no end to what the schema would have to name.
                    let slot (wire: IdlType) =
                        THosted
                            { FSharp = "Held"
                              Encode = "encodeHeld"
                              Decode = "decodeHeld"
                              Wire = Some wire
                              Format = None
                              Refusal = HostedRefusal.Sentence }

                    let held (members: IdlField list) =
                        vocabulary
                            [ kind "K" [ f "h" (slot (TRecord("G", [ TInt ]))) Optional ] ]
                            []
                            [ record "G" [ "T" ] members ]

                    let grows =
                        held
                            [ f "next" (TRecord("G", [ TList(TVar "T") ])) Optional
                              f "v" (TVar "T") Optional ]

                    Expect.isEmpty (Declare.errors grows) "the declaration check admits it"

                    Expect.stringContains
                        (match within 20.0 "Gen.jsonSchema" (fun () -> described (Gen.jsonSchema grows)) with
                         | Error why -> why
                         | Ok() -> "emitted")
                        (sentence "record" "G" "G<'T>" "G<list<'T>>")
                        "the growth is met through the wire form, and said"

                    // The same holder over a declaration that does not grow is emitted, and names it.
                    match Gen.jsonSchema (held [ f "v" (TVar "T") Optional ]) with
                    | Ok text ->
                        Expect.stringContains text "\"G_int\":" "the instantiation in the wire form is defined"
                    | Error e -> failtestf "refused: %s" (CodegenError.describe e))

                testCase "an enumeration stops at its count, and says the count" (fun _ ->
                    // FINITE, and enormous: each of twelve declarations names the next at two
                    // arguments, so the last is reached at 2^12 instantiations.
                    let chain (n: int) (last: IdlField) : Idl =
                        vocabulary
                            [ kind "K" [ f "d" (TRecord("D1", [ TInt ])) Optional ] ]
                            []
                            [ for i in 1..n do
                                  record
                                      (sprintf "D%d" i)
                                      [ "T" ]
                                      (if i = n then
                                           [ last ]
                                       else
                                           [ f "a" (TRecord(sprintf "D%d" (i + 1), [ TList(TVar "T") ])) Optional
                                             f "b" (TRecord(sprintf "D%d" (i + 1), [ TMap(TVar "T") ])) Optional ]) ]

                    let plain = f "v" (TVar "T") Optional
                    let root = [ TRecord("D1", [ TInt ]) ]

                    // Under the count: 2^11 - 1 instantiations, every one named.
                    match within 30.0 "the wire reach" (fun () -> Decl.wireInstantiations (chain 11 plain) root) with
                    | Ok all -> Expect.equal all.Length 2047 "a chain of eleven is read to its end"
                    | Error why -> failtestf "a chain of eleven: %s" why

                    // The count is of GENERIC instantiations: 2^12 - 1 of them is under it, and two
                    // declarations applied to no argument beside them are not counted.
                    let twelve = chain 12 plain

                    let besidePlain =
                        { twelve with
                            Records =
                                twelve.Records
                                @ [ record "P1" [] [ f "w" TInt Required ]
                                    record "P2" [] [ f "w" TInt Required ] ] }

                    match
                        within 30.0 "the wire reach" (fun () ->
                            Decl.wireInstantiations besidePlain (root @ [ TRecord("P1", []); TRecord("P2", []) ]))
                    with
                    | Ok all -> Expect.equal all.Length 4097 "4095 generic instantiations and two plain declarations"
                    | Error why -> failtestf "a chain of twelve beside two plain declarations: %s" why

                    // The count's edge, exactly: 4096 generic instantiations are read, 4097 are not.
                    let besideOnce (args: IdlType list) =
                        { twelve with
                            Kinds =
                                twelve.Kinds
                                @ [ kind
                                        "L"
                                        [ for i, a in List.indexed args ->
                                              f (sprintf "e%d" i) (TRecord("E", [ a ])) Optional ] ]
                            Records = twelve.Records @ [ record "E" [ "T" ] [ f "v" (TVar "T") Optional ] ] }

                    let edge (args: IdlType list) =
                        within 30.0 "the wire reach" (fun () ->
                            Decl.wireInstantiations
                                (besideOnce args)
                                (root @ (args |> List.map (fun a -> TRecord("E", [ a ])))))

                    match edge [ TInt ] with
                    | Ok all -> Expect.equal all.Length 4096 "exactly the count is read to its end"
                    | Error why -> failtestf "exactly 4096 instantiations: %s" why

                    Expect.equal (edge [ TInt; TStr ]) (Error tooMany) "one past the count is said"

                    let over = chain 13 plain

                    Expect.equal
                        (within 30.0 "the wire reach" (fun () -> Decl.wireInstantiations over root))
                        (Error tooMany)
                        "over the count: said, in a sentence that is true of it"

                    Expect.isEmpty (Declare.errors over) "the declaration check admits it: nothing asks for them all"

                    Expect.equal
                        (within 30.0 "Artifact.parse" (fun () -> Artifact.parse (Artifact.render over)))
                        (Ok(Artifact.canonicalise over))
                        "and its artifact reads back, at once"

                    let answers =
                        within 60.0 "the backends" (fun () -> backends over @ schemas over)
                        |> List.map (fun (entry, answer) ->
                            entry,
                            (match answer with
                             | Ok() -> "emits"
                             | Error description when description.Contains tooMany -> "refuses: " + tooMany
                             | Error description -> "refuses otherwise: " + description))

                    Expect.equal
                        answers
                        [ "fsharpTypes", "emits"
                          "fsharpModule", "emits"
                          "fsharpModuleDerived", "emits"
                          "fsharpCodecModule", "emits"
                          "fsharpCodecModuleWith", "emits"
                          "fsharpValue", "emits"
                          "typescriptModule", "emits"
                          "typescriptModuleDerived", "emits"
                          "typescriptDeclarations", "emits"
                          "typescriptDeclarationsDerived", "emits"
                          "typescriptValue", "emits"
                          "FStarTarget.vocabularyModule", "refuses: " + tooMany
                          "FStarTarget.proofsModule", "refuses: " + tooMany
                          "jsonSchema", "refuses: " + tooMany
                          "contractArgumentSchemas", "emits" ]
                        "a backend that states a generic natively emits; one that names each instantiation refuses"

                    // A literal that fits with the parameter unbound is asked nothing more, so there
                    // is nothing to enumerate and nothing to stop at.
                    Expect.isEmpty
                        (Declare.errors (chain 13 (f "v" (TList(TVar "T")) (OmitDefault(VList [])))))
                        "an empty list at list<T> is admitted without reading an instantiation"

                    // Any other literal has every type its parameter takes to be judged at, and the
                    // declaration check counts only those: `D13`'s 4096 are each judged, however many
                    // the twelve before it have between them, and the member is refused in one fault
                    // that names the first it does not fit and counts the others.
                    let judged =
                        within 60.0 "the declaration check" (fun () ->
                            Declare.errors (chain 13 (f "v" (TVar "T") (OmitDefault(VStr "")))))

                    Expect.equal judged.Length 1 "one refusal for the member"

                    Expect.stringStarts
                        judged.Head.Detail
                        "record 'D13', field 'v': the omit-at-default value does not fit the field's type at the instantiation D13<"
                        "it names the member and an instantiation of it"

                    Expect.stringContains
                        judged.Head.Detail
                        " and at least 4095 more ("
                        "and counts the other instantiations of D13 it found, here every one"

                    Expect.equal
                        (failing (chain 13 (f "v" (TVar "T") (OmitDefault(VStr "")))) "D13" "v")
                        4096
                        "the brute force: every one of D13's 4096 fails")

                testCase
                    "the declaration check counts the types ONE parameter takes, and judges in classes past them"
                    (fun _ ->
                        let limit = 16384

                        let mk (name: string) (pars: string list) (members: IdlField list) =
                            union name pars [ case "Mk" members ]

                        let refusal (name: string) =
                            sprintf
                                "more than %d instantiations of union '%s' reach union '%s' case 'Mk', field 'v'; its default cannot be checked at each"
                                limit
                                name
                                name

                        // Past the count a member is judged in classes: refused at the instantiation
                        // that carried the first class, and the other class at the outermost form
                        // (`list<…>` beside `map<…>`) counted as found.
                        let inClasses (name: string) (x: string) =
                            x.StartsWith(
                                sprintf
                                    "union '%s' case 'Mk', field 'v': the omit-at-default value does not fit the field's type at the instantiation %s<"
                                    name
                                    name
                            )
                            && x.Contains " and at least 1 more ("

                        // `Uk<T> { a: U(k+1)<list<T>>; b: U(k+1)<map<T>> }`, the last holding `v: T = ""`
                        // and `more`: the last has 2^(n-1) instantiations, each its own.
                        let doubling (n: int) (more: IdlField list) (beside: IdlUnion list) (roots: IdlField list) =
                            vocabulary
                                [ kind "K" (f "g" (TUnion("U1", [ TStr ])) Optional :: roots) ]
                                ([ for k in 1..n ->
                                       mk
                                           (sprintf "U%d" k)
                                           [ "T" ]
                                           (if k < n then
                                                [ f "a" (TUnion(sprintf "U%d" (k + 1), [ TList(TVar "T") ])) Optional
                                                  f "b" (TUnion(sprintf "U%d" (k + 1), [ TMap(TVar "T") ])) Optional ]
                                            else
                                                f "v" (TVar "T") (OmitDefault(VStr "")) :: more) ]
                                 @ beside)
                                []

                        // 2^39 types at U40's parameter: past the count of the types it takes, which a
                        // position the parameter is placed at is given at least as many of (past it at
                        // U16, so at every position after it), and judged in classes at once.
                        Expect.equal
                            (within 10.0 "Declare.errors" (fun () -> Declare.errors (doubling 40 [] [] []))
                             |> List.map (fun x -> inClasses "U40" x.Detail))
                            [ true ]
                            "past the count: judged in classes, and refused at the two classes found"

                        // A neighbour past the count decides nothing for a declaration it does not
                        // reach: `W<T> { v: T = "" }`, reached once at str beside U40, is judged and
                        // admitted; U40 alone is refused, in classes.
                        Expect.equal
                            (within 10.0 "Declare.errors" (fun () ->
                                Declare.errors (
                                    doubling
                                        40
                                        []
                                        [ mk "W" [ "T" ] [ f "v" (TVar "T") (OmitDefault(VStr "")) ] ]
                                        [ f "w" (TUnion("W", [ TStr ])) Optional ]
                                ))
                             |> List.map (fun x -> inClasses "U40" x.Detail))
                            [ true ]
                            "the neighbour admitted, U40 refused"

                        // A declaration reached only through a member refused for the count, at a
                        // parameter that member does not read, is still judged at each of its
                        // instantiations, the walk reading on for it: `Wk<T, S> { a: W(k+1)<list<T>, S>; b: W(k+1)<map<T>, list<S>> }`,
                        // the last holding `v: T = ""` and `e: E<S>`, with `E<T> { v: T = "" }`. W16's `T`
                        // takes 2^15 types and its `v` is refused, in classes; E has sixteen, `E<str>` and `E<list^j<str>>`
                        // for j from 1 to 15, the last of them reached only at the last of W16's.
                        let downstream =
                            vocabulary
                                [ kind "K" [ f "g" (TUnion("W1", [ TStr; TStr ])) Optional ] ]
                                ([ for k in 1..16 ->
                                       mk
                                           (sprintf "W%d" k)
                                           [ "T"; "S" ]
                                           (if k < 16 then
                                                [ f
                                                      "a"
                                                      (TUnion(sprintf "W%d" (k + 1), [ TList(TVar "T"); TVar "S" ]))
                                                      Optional
                                                  f
                                                      "b"
                                                      (TUnion(
                                                          sprintf "W%d" (k + 1),
                                                          [ TMap(TVar "T"); TList(TVar "S") ]
                                                      ))
                                                      Optional ]
                                            else
                                                [ f "v" (TVar "T") (OmitDefault(VStr ""))
                                                  f "e" (TUnion("E", [ TVar "S" ])) Optional ]) ]
                                 @ [ mk "E" [ "T" ] [ f "v" (TVar "T") (OmitDefault(VStr "")) ] ])
                                []

                        let answer = within 20.0 "Declare.errors" (fun () -> Declare.errors downstream)

                        Expect.equal
                            (answer
                             |> List.filter (fun x -> x.Detail.StartsWith "union 'E'")
                             |> List.map (fun x -> x.Detail.Contains " and at least 14 more ("))
                            [ true ]
                            "E judged at each of its sixteen, and refused in one fault at each but E<str>"

                        Expect.equal
                            (answer
                             |> List.filter (fun x -> not (x.Detail.StartsWith "union 'E'"))
                             |> List.map (fun x -> inClasses "W16" x.Detail))
                            [ true ]
                            "W16 past the count, refused in classes"

                        // Instantiations that differ only where nothing judged reads them are one step:
                        // U40 holding no default but `d: E<str>` is walked at once, and E admitted.
                        let upstream =
                            vocabulary
                                [ kind "K" [ f "g" (TUnion("U1", [ TStr ])) Optional ] ]
                                ([ for k in 1..40 ->
                                       mk
                                           (sprintf "U%d" k)
                                           [ "T" ]
                                           (if k < 40 then
                                                [ f "a" (TUnion(sprintf "U%d" (k + 1), [ TList(TVar "T") ])) Optional
                                                  f "b" (TUnion(sprintf "U%d" (k + 1), [ TMap(TVar "T") ])) Optional ]
                                            else
                                                [ f "d" (TUnion("E", [ TStr ])) Optional ]) ]
                                 @ [ mk "E" [ "T" ] [ f "v" (TVar "T") (OmitDefault(VStr "")) ] ])
                                []

                        Expect.isEmpty
                            (within 10.0 "Declare.errors" (fun () -> Declare.errors upstream))
                            "E admitted at its one instantiation, at once"

                        // Two declarations judged, each fed one parameter of a chain that doubles the
                        // other: `Xk<T, S> { a: X(k+1)<list<T>, S>; b: X(k+1)<map<T>, S> }`, the
                        // eightieth holding `d: D<T>` and `e: E<S>`, each of D and E holding
                        // `v: T = ""`. No reference writes both parameters, and D, past the count, is
                        // refused in classes at once; E, at its one instantiation `E<str>`, is admitted, and the
                        // walk reads no more of the doubled parameter once D is refused, which feeds only D.
                        let lossy =
                            vocabulary
                                [ kind "K" [ f "g" (TUnion("X1", [ TStr; TStr ])) Optional ] ]
                                ([ for k in 1..80 ->
                                       mk
                                           (sprintf "X%d" k)
                                           [ "T"; "S" ]
                                           (if k < 80 then
                                                [ f
                                                      "a"
                                                      (TUnion(sprintf "X%d" (k + 1), [ TList(TVar "T"); TVar "S" ]))
                                                      Optional
                                                  f
                                                      "b"
                                                      (TUnion(sprintf "X%d" (k + 1), [ TMap(TVar "T"); TVar "S" ]))
                                                      Optional ]
                                            else
                                                [ f "d" (TUnion("D", [ TVar "T" ])) Optional
                                                  f "e" (TUnion("E", [ TVar "S" ])) Optional ]) ]
                                 @ [ mk "D" [ "T" ] [ f "v" (TVar "T") (OmitDefault(VStr "")) ]
                                     mk "E" [ "T" ] [ f "v" (TVar "T") (OmitDefault(VStr "")) ] ])
                                []

                        Expect.equal
                            (within 10.0 "Declare.errors" (fun () -> Declare.errors lossy)
                             |> List.map (fun x -> inClasses "D" x.Detail))
                            [ true ]
                            "D refused in classes at once, E admitted"

                        // In classes the count is of the classes one parameter takes, at one depth, and
                        // only a parameter a member reads is read: `D<T, S> { a: D<T, list<S>>; b: D<T,
                        // map<S>>; v: T = [[…[]…]] }` (sixty lists deep) held at `D<list^60<str>, str>` is
                        // decided only from depth sixty-one, and `S` has 2^k classes at depth k; but `v`
                        // reads `T`, which takes one type, so each depth is a few steps, and it fits.
                        let rec nest (k: int) =
                            if k = 0 then VList [] else VList [ nest (k - 1) ]

                        let rec lists (k: int) (t: IdlType) =
                            if k = 0 then t else TList(lists (k - 1) t)

                        let deep =
                            vocabulary
                                [ kind "K" [ f "g" (TUnion("D", [ lists 60 TStr; TStr ])) Optional ] ]
                                [ mk
                                      "D"
                                      [ "T"; "S" ]
                                      [ f "a" (TUnion("D", [ TVar "T"; TList(TVar "S") ])) Optional
                                        f "b" (TUnion("D", [ TVar "T"; TMap(TVar "S") ])) Optional
                                        f "v" (TVar "T") (OmitDefault(nest 59)) ] ]
                                []

                        Expect.isEmpty
                            (within 60.0 "Declare.errors" (fun () -> Declare.errors deep))
                            "the parameter read takes one type: judged, and admitted"

                        // `D<T> { a: D<Box<T>>; b: D<Bag<T>>; v: T = { item: { item: … "" } } }` (fourteen
                        // records, a string inside), `Box` and `Bag` each holding `item: W`, held at
                        // `D<Box^14<str>>`: the two records agree on the literal at every level, so no
                        // class is answered above depth fifteen, and at depth fourteen `T` has 2^15 - 1
                        // classes: refused for the count, the one thing the classes walk cannot decide.
                        let rec boxes (k: int) (t: IdlType) =
                            if k = 0 then t else TRecord("Box", [ boxes (k - 1) t ])

                        let rec items (k: int) =
                            if k = 0 then VStr "" else VRecord [ "item", items (k - 1) ]

                        let wide =
                            vocabulary
                                [ kind "K" [ f "g" (TUnion("D", [ boxes 14 TStr ])) Optional ] ]
                                [ mk
                                      "D"
                                      [ "T" ]
                                      [ f "a" (TUnion("D", [ TRecord("Box", [ TVar "T" ]) ])) Optional
                                        f "b" (TUnion("D", [ TRecord("Bag", [ TVar "T" ]) ])) Optional
                                        f "v" (TVar "T") (OmitDefault(items 14)) ] ]
                                [ record "Box" [ "W" ] [ f "item" (TVar "W") Required ]
                                  record "Bag" [ "W" ] [ f "item" (TVar "W") Required ] ]

                        Expect.equal
                            (within 60.0 "Declare.errors" (fun () -> Declare.errors wide)
                             |> List.map _.Detail)
                            [ refusal "D" ]
                            "past the count of its classes: refused")

                testCase
                    "the count is four times the most instantiations of one declaration a fixture reaches"
                    (fun _ ->
                        // Every instantiation a document can hold, from each owner declaring no parameter,
                        // counted by its declaration, over every fixture vocabulary of the suite's library.
                        let roots (idl: Idl) : IdlType list =
                            [ for k in idl.Kinds @ idl.Ops do
                                  for x in k.Fields -> x.Type
                              for x in idl.NodeFields -> x.Type
                              for u in idl.Unions do
                                  if List.isEmpty u.Params then
                                      for c in u.Cases do
                                          for x in c.Fields -> x.Type
                              for r in idl.Records do
                                  if List.isEmpty r.Params then
                                      for x in r.Fields -> x.Type
                              for c in idl.Contracts do
                                  for m in c.Methods do
                                      yield! (m.Arguments |> List.map _.Type) @ Option.toList m.Result ]

                        let most =
                            [ for name, idl in ("parameterised records", ParameterisedRecordsIdl.idl) :: fixtures do
                                  match Decl.wireInstantiations idl (roots idl) with
                                  | Ok all ->
                                      all
                                      |> List.countBy (fun t ->
                                          match t with
                                          | TRecord(n, _)
                                          | TUnion(n, _) -> n
                                          | _ -> "")
                                      |> List.map snd
                                      |> List.fold max 0
                                  | Error why -> failtestf "%s: %s" name why ]
                            |> List.fold max 0

                        // The vocabularies built here to reach many reach 4096 of one declaration (`D13`
                        // above); a fixture reaches far fewer.
                        Expect.isLessThanOrEqual most 4096 (sprintf "a fixture reaches %d of one declaration" most)

                        Expect.isGreaterThan most 0 "the measure is not vacuous")

                testCase
                    "a list around a parameter expands, and a declaration naming itself closes a cycle at once"
                    (fun _ ->
                        // Each answered in its own sentence, not by an enumeration that stops at the
                        // count: the growth is found from the declarations.
                        let through =
                            vocabulary
                                []
                                []
                                [ record "A" [ "T" ] [ f "b" (TRecord("B", [ TList(TVar "T") ])) Optional ]
                                  record "B" [ "T" ] [ f "a" (TRecord("A", [ TVar "T" ])) Optional ] ]

                        Expect.equal
                            (within 20.0 "the wire reach" (fun () ->
                                Decl.wireInstantiations through [ TRecord("A", [ TInt ]) ]))
                            (Error(sentence "record" "A" "A<'T>" "A<list<'T>>"))
                            "a list around the parameter, followed round two declarations, is growth"

                        let itself =
                            vocabulary
                                []
                                []
                                [ record
                                      "G"
                                      [ "T" ]
                                      [ f "g" (TRecord("G", [ TRecord("Box", [ TVar "T" ]) ])) Optional ]
                                  record "Box" [ "T" ] [ f "v" (TVar "T") Optional ] ]

                        Expect.equal
                            (within 20.0 "the wire reach" (fun () ->
                                Decl.wireInstantiations itself [ TRecord("G", [ TInt ]) ]))
                            (Error(sentence "record" "G" "G<'T>" "G<Box<'T>>"))
                            "an edge from a declaration to itself is a cycle on its own") ]

          testList
              "a closure few in number and enormous in size: read at once, and refused where it is printed"
              [ for asUnion in [ false; true ] do
                    let what = if asUnion then "union" else "record"

                    // `D1<T> { x: D2<tuple<T, T>> } … Dn<T> { last }` held at `D1<str>`: no recursion and
                    // n instantiations, the k-th 2^k nodes, 2^(n+1) - 2 between them.
                    let doubling
                        (n: int)
                        (last: IdlField)
                        (beside: IdlField list)
                        (extra: IdlUnion list * IdlRecord list)
                        =
                        let at (i: int) (t: IdlType) = refer asUnion (sprintf "D%d" i) [ t ]

                        let chain =
                            [ for i in 1..n ->
                                  declared
                                      asUnion
                                      (sprintf "D%d" i)
                                      [ "T" ]
                                      [ if i = n then
                                            last
                                        else
                                            f "x" (at (i + 1) (TTuple [ TVar "T"; TVar "T" ])) Optional ] ]

                        vocabulary
                            [ kind "K" (f "g" (at 1 TStr) Optional :: beside) ]
                            (List.collect fst chain @ fst extra)
                            (List.collect snd chain @ snd extra)

                    let plain = f "v" (TVar "T") Optional

                    testCase (what + ": the declaration check, the artifact reader and every generator entry") (fun _ ->
                        let idl = doubling 40 plain [] ([], [])

                        Expect.isEmpty
                            (within 20.0 "Declare.errors" (fun () -> Declare.errors idl))
                            "admitted: nothing judges an instantiation of it"

                        Expect.equal
                            (within 20.0 "Artifact.parse" (fun () -> Artifact.parse (Artifact.render idl)))
                            (Ok(Artifact.canonicalise idl))
                            "the artifact reads back"

                        let answers =
                            within 60.0 "the generator entries" (fun () -> backends idl @ schemas idl)
                            |> List.map (fun (entry, answer) ->
                                entry,
                                (match answer with
                                 | Ok() -> "emits"
                                 | Error description when description.Contains oversized -> "refuses: " + oversized
                                 | Error description -> "refuses otherwise: " + description))

                        let tooLarge = "refuses: " + oversized

                        Expect.equal
                            answers
                            [ "fsharpTypes", "emits"
                              "fsharpModule", "emits"
                              "fsharpModuleDerived", "emits"
                              "fsharpCodecModule", "emits"
                              "fsharpCodecModuleWith", "emits"
                              "fsharpValue", "emits"
                              "typescriptModule", "emits"
                              "typescriptModuleDerived", "emits"
                              "typescriptDeclarations", "emits"
                              "typescriptDeclarationsDerived", "emits"
                              "typescriptValue", "emits"
                              "FStarTarget.vocabularyModule", tooLarge
                              "FStarTarget.proofsModule", tooLarge
                              "jsonSchema", tooLarge
                              "contractArgumentSchemas", "emits" ]
                            "a generic stated natively is emitted; one printed per instantiation is refused, saying what was measured"

                        // The classifier prints each instantiation pair it enters, so beside it a
                        // restated member of another generic declaration reads moved, at once.
                        let restated (v: IdlType) =
                            doubling
                                40
                                plain
                                [ f "b" (refer asUnion "Box" [ TInt ]) Optional ]
                                (declared asUnion "Box" [ "T" ] [ f "v" v Required ])

                        match
                            within 20.0 "Diff.ofValues" (fun () ->
                                Diff.ofValues (restated (TVar "T"), None) (restated TInt, None))
                        with
                        | Ok verdict ->
                            Expect.equal
                                (verdict.Changes |> List.map _.Severity)
                                [ BreakingWire ]
                                "answered, on the side of the break"
                        | Error e -> failtestf "the pair did not classify: %s" e)

                    testCase (what + ": a default is judged at its own declaration's instantiations") (fun _ ->
                        // `Opt<T> { v: T = "" }` held at `Opt<str>` beside the chain: the chain cannot
                        // reach `Opt`, so the walk for `Opt` does not read it.
                        let opt = declared asUnion "Opt" [ "T" ] [ f "v" (TVar "T") (OmitDefault(VStr "")) ]

                        let beside (at: IdlType list) =
                            at
                            |> List.mapi (fun i t -> f (sprintf "o%d" i) (refer asUnion "Opt" [ t ]) Optional)

                        let fits = doubling 40 plain (beside [ TStr ]) opt
                        Expect.isEmpty (within 20.0 "Declare.errors" (fun () -> Declare.errors fits)) "admitted"

                        Expect.equal
                            (within 20.0 "Artifact.parse" (fun () -> Artifact.parse (Artifact.render fits)))
                            (Ok(Artifact.canonicalise fits))
                            "the artifact reads back"

                        match
                            within 20.0 "Declare.errors" (fun () ->
                                Declare.errors (doubling 40 plain (beside [ TStr; TInt ]) opt))
                            |> List.map _.Detail
                        with
                        | [ only ] ->
                            Expect.stringContains
                                only
                                "does not fit the field's type at the instantiation Opt<int> and at least 0 more ("
                                "and refused where it does not fit"
                        | other -> failtestf "one fault expected: %A" other

                        // The same beside a chain of thirteen naming the next at two arguments
                        // (8191 instantiations, more than the count): it cannot reach `Opt` either.
                        let branching =
                            let at (i: int) (t: IdlType) = refer asUnion (sprintf "B%d" i) [ t ]

                            let chain =
                                [ for i in 1..13 ->
                                      declared
                                          asUnion
                                          (sprintf "B%d" i)
                                          [ "T" ]
                                          (if i = 13 then
                                               [ plain ]
                                           else
                                               [ f "a" (at (i + 1) (TList(TVar "T"))) Optional
                                                 f "b" (at (i + 1) (TMap(TVar "T"))) Optional ]) ]

                            let unions, records = opt

                            vocabulary
                                [ kind "K" (f "g" (at 1 TStr) Optional :: beside [ TStr ]) ]
                                (List.collect fst chain @ unions)
                                (List.collect snd chain @ records)

                        Expect.isEmpty
                            (within 20.0 "Declare.errors" (fun () -> Declare.errors branching))
                            "admitted beside the count"

                        // And beside a declaration that grows along two members: its instantiations
                        // are infinitely many, and none is read for `Opt`.
                        let growing =
                            let idl = twoWay asUnion plain TStr
                            let unions, records = opt

                            { idl with
                                Kinds = idl.Kinds @ [ kind "O" (beside [ TStr ]) ]
                                Unions = idl.Unions @ unions
                                Records = idl.Records @ records }

                        Expect.isEmpty
                            (within 20.0 "Declare.errors" (fun () -> Declare.errors growing))
                            "admitted beside growth"

                        // And where the judged declaration grows itself, so it is judged in classes: a
                        // literal twelve lists deep is decided only at a cut twelve deep, where the
                        // growth beside it has thousands of classes; none of them is read for `H`.
                        let deep = (VList [], [ 1..11 ]) ||> List.fold (fun v _ -> VList [ v ])
                        let self (t: IdlType) = refer asUnion "H" [ t ]

                        let classed =
                            let idl = twoWay asUnion plain TStr

                            let unions, records =
                                declared
                                    asUnion
                                    "H"
                                    [ "T" ]
                                    [ f "l" (self (TList(TVar "T"))) Optional; f "v" (TVar "T") (OmitDefault deep) ]

                            { idl with
                                Kinds =
                                    idl.Kinds
                                    @ [ kind
                                            "O"
                                            [ f
                                                  "h"
                                                  (self ((TInt, [ 1..12 ]) ||> List.fold (fun t _ -> TList t)))
                                                  Optional ] ]
                                Unions = idl.Unions @ unions
                                Records = idl.Records @ records }

                        Expect.isEmpty
                            (within 20.0 "Declare.errors" (fun () -> Declare.errors classed))
                            "admitted, judged in classes beside growth it cannot reach"

                        // Two judged declarations, one reaching the other: each member is refused once,
                        // at its own declaration's instantiation, though one walk enters both.
                        let pair =
                            let ua, ra =
                                declared
                                    asUnion
                                    "A"
                                    [ "T" ]
                                    [ f "v" (TVar "T") (OmitDefault(VStr ""))
                                      f "b" (refer asUnion "B" [ TVar "T" ]) Optional ]

                            let ub, rb =
                                declared asUnion "B" [ "T" ] [ f "w" (TVar "T") (OmitDefault(VStr "")) ]

                            vocabulary [ kind "K" [ f "a" (refer asUnion "A" [ TInt ]) Optional ] ] (ua @ ub) (ra @ rb)

                        Expect.equal
                            (within 20.0 "Declare.errors" (fun () -> Declare.errors pair)
                             |> List.map (fun x -> x.Detail.Substring(0, x.Detail.IndexOf ':')))
                            [ (if asUnion then
                                   "union 'A' case 'Mk', field 'v'"
                               else
                                   "record 'A', field 'v'")
                              (if asUnion then
                                   "union 'B' case 'Mk', field 'w'"
                               else
                                   "record 'B', field 'w'") ]
                            "one fault per member, each at its own declaration"

                        Expect.equal
                            (within 20.0 "Artifact.parse" (fun () -> Artifact.parse (Artifact.render branching)))
                            (Ok(Artifact.canonicalise branching))
                            "and its artifact reads back")

                    testCase
                        (what + ": a default at the chain's end is refused at any size, its text bounded")
                        (fun _ ->
                            let zero = f "v" (TVar "T") (OmitDefault(VInt 0))

                            // `D26<T>` is reached at one instantiation of 2^26 nodes: judged without being
                            // printed, and refused in a sentence that names the declaration, the member and
                            // the instantiation to its first nodes.
                            for n in [ 12; 26 ] do
                                match
                                    within 20.0 "Declare.errors" (fun () ->
                                        Declare.errors (doubling n zero [] ([], [])))
                                    |> List.map _.Detail
                                with
                                | [ only ] ->
                                    Expect.stringStarts
                                        only
                                        (sprintf
                                            "%s 'D%d'%s, field 'v': the omit-at-default value does not fit the field's type at the instantiation D%d<tuple<tuple<"
                                            what
                                            n
                                            (if asUnion then " case 'Mk'" else "")
                                            n)
                                        "refused at the instantiation, by declaration and member"

                                    Expect.stringContains only "..." "the instantiation abbreviated"
                                    Expect.isLessThan only.Length 8192 "the text bounded"
                                | other -> failtestf "one fault expected: %A" other)

                    testCase (what + ": a default that fits every instantiation is admitted at any size") (fun _ ->
                        // `Dk<T> { x: D(k+1)<tuple<T, T>>; o: Opt<list<T>> }`, k = 1 … 30, held at
                        // `D1<int>`, and `Opt<T> { v: T = [] }`: `Opt` is reached only at lists, the last
                        // holding 2^30 nodes, and the literal fits each.
                        let n = 30
                        let at (i: int) (t: IdlType) = refer asUnion (sprintf "D%d" i) [ t ]

                        let chain =
                            [ for i in 1..n ->
                                  declared
                                      asUnion
                                      (sprintf "D%d" i)
                                      [ "T" ]
                                      [ if i < n then
                                            f "x" (at (i + 1) (TTuple [ TVar "T"; TVar "T" ])) Optional
                                        f "o" (refer asUnion "Opt" [ TList(TVar "T") ]) Optional ] ]

                        let opt =
                            declared asUnion "Opt" [ "T" ] [ f "v" (TVar "T") (OmitDefault(VList [])) ]

                        let idl =
                            vocabulary
                                [ kind "K" [ f "g" (at 1 TInt) Optional ] ]
                                (List.collect fst chain @ fst opt)
                                (List.collect snd chain @ snd opt)

                        Expect.isEmpty (within 20.0 "Declare.errors" (fun () -> Declare.errors idl)) "admitted"

                        Expect.equal
                            (within 20.0 "Artifact.parse" (fun () -> Artifact.parse (Artifact.render idl)))
                            (Ok(Artifact.canonicalise idl))
                            "the artifact reads back")

                    testCase
                        (what
                         + ": a parameter on a cycle that only copies it is judged at each instantiation")
                        (fun _ ->
                            // `Opt<T> { next: Opt<T>; v: T = "" }` held at `Opt<int>` and `Opt<bool>`: the
                            // cycle holds no expanding edge, so the instantiations are finitely many, each
                            // is judged, and the member is refused once, naming the first reached and
                            // counting the other.
                            let opt =
                                declared
                                    asUnion
                                    "Opt"
                                    [ "T" ]
                                    [ f "next" (refer asUnion "Opt" [ TVar "T" ]) Optional
                                      f "v" (TVar "T") (OmitDefault(VStr "")) ]

                            let idl =
                                vocabulary
                                    [ kind
                                          "K"
                                          [ f "a" (refer asUnion "Opt" [ TInt ]) Optional
                                            f "b" (refer asUnion "Opt" [ TBool ]) Optional ] ]
                                    (fst opt)
                                    (snd opt)

                            Expect.equal
                                (within 20.0 "Declare.errors" (fun () -> Declare.errors idl)
                                 |> List.map (fun x ->
                                     x.Detail.Contains "the instantiation Opt<int> and at least 1 more (",
                                     x.Detail.Contains "Opt<bool>"))
                                [ true, false ]
                                "both instantiations refused, in one fault")

                    testCase (what + ": a thousand declarations, each holding a default, are checked at once") (fun _ ->
                        // `D1<T> { x: D2<T>; v: T = "" } … D1000<T> { v: T = "" }`, held at `D1<str>`:
                        // what the walks share is found once, and the declarations' instantiations are
                        // read in one enumeration for them all. A walk per declaration took a minute.
                        let n = 1000

                        let chain (grows: bool) =
                            [ for i in 1..n ->
                                  declared
                                      asUnion
                                      (sprintf "D%d" i)
                                      [ "T" ]
                                      [ if i < n then
                                            f "x" (refer asUnion (sprintf "D%d" (i + 1)) [ TVar "T" ]) Optional
                                        if grows && i = 1 then
                                            f "l" (refer asUnion "D1" [ TList(TVar "T") ]) Optional
                                        f "v" (TVar "T") (OmitDefault(if grows then VList [] else VStr "")) ] ]

                        let held (grows: bool) (at: IdlType) =
                            let declarations = chain grows

                            vocabulary
                                [ kind "K" [ f "g" (refer asUnion "D1" [ at ]) Optional ] ]
                                (List.collect fst declarations)
                                (List.collect snd declarations)

                        let idl = held false TStr
                        Expect.isEmpty (within 10.0 "Declare.errors" (fun () -> Declare.errors idl)) "admitted"

                        Expect.equal
                            (within 10.0 "Artifact.parse" (fun () -> Artifact.parse (Artifact.render idl)))
                            (Ok(Artifact.canonicalise idl))
                            "the artifact reads back"

                        // Held at `D1<int>`, each declaration's own member is refused, once.
                        let refused =
                            within 10.0 "Declare.errors" (fun () -> Declare.errors (held false TInt))
                            |> List.map (fun x -> x.Detail.Substring(0, x.Detail.IndexOf ':'))

                        Expect.equal
                            (List.sort refused)
                            (List.sort
                                [ for i in 1..n ->
                                      if asUnion then
                                          sprintf "union 'D%d' case 'Mk', field 'v'" i
                                      else
                                          sprintf "record 'D%d', field 'v'" i ])
                            "each declaration judged, once"

                        // Where the first grows (`D1<T>` also holds `D1<list<T>>`) and each default is
                        // `[]`, every one is judged in classes, in one walk.
                        Expect.isEmpty
                            (within 10.0 "Declare.errors" (fun () -> Declare.errors (held true (TList TStr))))
                            "admitted in classes"

                        Expect.equal
                            (within 10.0 "Declare.errors" (fun () -> Declare.errors (held true TInt))
                             |> List.length)
                            n
                            "and each refused in classes")

                    testCase
                        (what
                         + ": five thousand declarations, each holding a default, are each judged, and the artifact loads")
                        (fun _ ->
                            // `D1<T> { x: D2<T>; v: T = "" } … D5000<T> { v: T = "" }` held at `D1<str>`: each
                            // declaration has ONE instantiation, so the count, which is of one declaration's
                            // own, is reached by none, however many declarations there are.
                            let n = 5000

                            let chain (m: int) =
                                let d =
                                    [ for i in 1..m ->
                                          declared
                                              asUnion
                                              (sprintf "D%d" i)
                                              [ "T" ]
                                              [ if i < m then
                                                    f "x" (refer asUnion (sprintf "D%d" (i + 1)) [ TVar "T" ]) Optional
                                                f "v" (TVar "T") (OmitDefault(VStr "")) ] ]

                                vocabulary
                                    [ kind "K" [ f "g" (refer asUnion "D1" [ TStr ]) Optional ] ]
                                    (List.collect fst d)
                                    (List.collect snd d)

                            // One run after the code has run once: its answer, the bytes it allocated on
                            // its own thread (the work runs on one, so the count is the work's, whatever
                            // the machine) and the time it took.
                            let run (work: unit -> 'a) : 'a * int64 * float =
                                work () |> ignore
                                let before = GC.GetAllocatedBytesForCurrentThread()
                                let watch = Diagnostics.Stopwatch.StartNew()
                                let answer = work ()
                                let seconds = watch.Elapsed.TotalSeconds
                                answer, GC.GetAllocatedBytesForCurrentThread() - before, seconds

                            // Judges and loads `m` declarations: the faults, the artifact read back, and
                            // what each cost.
                            let measure (m: int) =
                                let idl = chain m
                                let text = Artifact.render idl

                                let faults, checkingBytes, checking =
                                    within 120.0 "Declare.errors" (fun () -> run (fun () -> Declare.errors idl))

                                let read, loadingBytes, loading =
                                    within 120.0 "Artifact.parse" (fun () -> run (fun () -> Artifact.parse text))

                                idl, faults, read, (checkingBytes, checking), (loadingBytes, loading)

                            let small = 1000
                            let _, _, _, smallChecking, smallLoading = measure small
                            let idl, faults, read, checking, loading = measure n

                            Expect.isEmpty faults "no fault: each default fits the one instantiation it has"
                            Expect.equal read (Ok(Artifact.canonicalise idl)) "the artifact reads back"

                            // A clock measures the machine, not the code: what is held is the growth of
                            // the work, as the bytes it allocates. Five times the declarations allocate
                            // about five times as much (each duplicate-name check reading every earlier
                            // declaration allocated about eleven times as much, and took twenty-five
                            // times as long where memory was slow). A ceiling on the time is only a
                            // backstop against an exponential.
                            let ceiling =
#if DEBUG
                                60.0
#else
                                10.0
#endif

                            let grown
                                (what: string)
                                (smallBytes: int64, smallTime: float)
                                (bytes: int64, time: float)
                                =
                                let ratio = float bytes / float (max smallBytes 1L)

                                let message =
                                    sprintf
                                        "%s allocated %d bytes in %.3f s at %d declarations and %d bytes in %.3f s at %d: a ratio of %.2f"
                                        what
                                        smallBytes
                                        smallTime
                                        small
                                        bytes
                                        time
                                        n
                                        ratio

                                Expect.isLessThan ratio 7.0 message
                                Expect.isLessThan time ceiling message

                            grown "Declare.errors" smallChecking checking
                            grown "Artifact.parse" smallLoading loading)

                testCase
                    "a generic union applied at a tuple of its parameter is stated at the tuple's components"
                    (fun _ ->
                        // `U1<T> { x: U2<tuple<T, T>> } … U6<T> { v: T }` held at `U1<str>`, no record
                        // declared: the schema names and types each tuple's components, and the F* target
                        // models the chain (the parameter was left in the tuple, unbound, before).
                        let idl =
                            vocabulary
                                [ kind "K" [ f "g" (TUnion("U1", [ TStr ])) Optional ] ]
                                [ for i in 1..6 ->
                                      union
                                          (sprintf "U%d" i)
                                          [ "T" ]
                                          [ case
                                                "Mk"
                                                [ if i = 6 then
                                                      f "v" (TVar "T") Optional
                                                  else
                                                      f
                                                          "x"
                                                          (TUnion(
                                                              sprintf "U%d" (i + 1),
                                                              [ TTuple [ TVar "T"; TVar "T" ] ]
                                                          ))
                                                          Optional ] ] ]
                                []

                        match Gen.jsonSchema idl with
                        | Ok text ->
                            for name in [ "U2_tuple2_str_str"; "U3_tuple2_tuple2_str_str_tuple2_str_str" ] do
                                Expect.stringContains text ("\"" + name + "\":") (name + " is defined")

                            Expect.isFalse (text.Contains "_T_T") "no definition is named for the parameter"
                        | Error e -> failtestf "refused: %s" (CodegenError.describe e)

                        match FStarTarget.vocabularyModule "Probe" idl [ "K" ] with
                        | Ok text -> Expect.stringContains text "and u_u3__tp2_tp2_str_str_tp2_str_str " "modelled"
                        | Error e -> failtestf "refused: %s" (CodegenError.describe e))

                testCase
                    "a chain wrapping its argument in a list is stated at 360 declarations and refused at 361"
                    (fun _ ->
                        // `U1<T> { x: U2<list<T>> } … Un<T> { v: T }` held at `U1<str>`: the k-th
                        // instantiation holds k + 1 nodes, n (n + 3) / 2 between them, which passes 65536
                        // at n = 361.
                        let chain (n: int) =
                            vocabulary
                                [ kind "K" [ f "g" (TUnion("U1", [ TStr ])) Optional ] ]
                                [ for i in 1..n ->
                                      union
                                          (sprintf "U%d" i)
                                          [ "T" ]
                                          [ case
                                                "Mk"
                                                [ if i = n then
                                                      f "v" (TVar "T") Optional
                                                  else
                                                      f
                                                          "x"
                                                          (TUnion(sprintf "U%d" (i + 1), [ TList(TVar "T") ]))
                                                          Optional ] ] ]
                                []

                        Expect.isOk
                            (within 60.0 "Gen.jsonSchema" (fun () -> described (Gen.jsonSchema (chain 360))))
                            "stated"

                        Expect.equal
                            (within 60.0 "Gen.jsonSchema" (fun () -> described (Gen.jsonSchema (chain 361)))
                             |> Result.mapError (fun e -> e.Contains oversized))
                            (Error true)
                            "refused one declaration on")

                testCase "the size is counted exactly: 65536 nodes are read and 65537 are not" (fun _ ->
                    // `Box<T> { v: T = [] }` held at `Box<list<tuple<int, …, int>>>` of m components:
                    // m + 3 nodes, the one instantiation the vocabulary has.
                    let held (m: int) : Idl =
                        vocabulary
                            [ kind "K" [ f "b" (TRecord("Box", [ TList(TTuple(List.replicate m TInt)) ])) Optional ] ]
                            []
                            [ record "Box" [ "T" ] [ f "v" (TVar "T") (OmitDefault(VList [])) ] ]

                    let root (m: int) =
                        [ TRecord("Box", [ TList(TTuple(List.replicate m TInt)) ]) ]

                    let at, over = held 65533, held 65534

                    Expect.isOk
                        (within 30.0 "the wire reach" (fun () -> Decl.wireInstantiations at (root 65533)))
                        "exactly the size: read"

                    Expect.equal
                        (within 30.0 "the wire reach" (fun () -> Decl.wireInstantiations over (root 65534)))
                        (Error oversized)
                        "one node past it: said"

                    Expect.isOk
                        (within 60.0 "Gen.jsonSchema" (fun () -> described (Gen.jsonSchema at)))
                        "the schema states it"

                    Expect.equal
                        (within 60.0 "Gen.jsonSchema" (fun () -> described (Gen.jsonSchema over))
                         |> Result.mapError (fun e -> e.Contains oversized))
                        (Error true)
                        "and refuses one node past it"

                    // The declaration check prints nothing to judge, so it judges either.
                    Expect.isEmpty
                        (within 30.0 "Declare.errors" (fun () -> Declare.errors at))
                        "the declaration check judges it"

                    Expect.isEmpty
                        (within 30.0 "Declare.errors" (fun () -> Declare.errors over))
                        "and judges one node past it"

                    // The classifier counts the same nodes: `Box.r` restated as the type it is held at
                    // is the same type at 65536 nodes, and past them reads moved (the side of the
                    // break).
                    let grade (m: int) =
                        let big = TList(TTuple(List.replicate m TInt))

                        let revision (written: bool) =
                            vocabulary
                                [ kind "K" [ f "b" (TRecord("Box", [ big ])) Optional ] ]
                                []
                                [ record
                                      "Box"
                                      [ "W" ]
                                      [ f "item" (TVar "W") (OmitDefault(VList []))
                                        f "r" (if written then big else TVar "W") Optional ] ]

                        match
                            within 60.0 "Diff.ofValues" (fun () ->
                                Diff.ofValues (revision false, None) (revision true, None))
                        with
                        | Ok verdict -> verdict.Changes |> List.map _.Severity
                        | Error e -> failtestf "the pair did not classify: %s" e

                    Expect.equal (grade 65533) [ HostSurfaceOnly ] "the classifier reads 65536 nodes"
                    Expect.equal (grade 65534) [ BreakingWire ] "and not 65537")

                testCase
                    "a fold through a chain whose instantiations double is refused, saying what was measured"
                    (fun _ ->
                        // `Tree` holds itself through `R1<Tree>`, and `Rk<T>` holds `R(k+1)<tuple<T, T>>`.
                        let idl =
                            vocabulary
                                [ kind "K" [ f "tree" (TUnion("Tree", [])) Required ] ]
                                [ union
                                      "Tree"
                                      []
                                      [ case "Leaf" [ f "n" TInt Required ]
                                        case "Branch" [ f "r" (TRecord("R1", [ TUnion("Tree", []) ])) Required ] ] ]
                                [ for i in 1..40 ->
                                      record
                                          (sprintf "R%d" i)
                                          [ "T" ]
                                          [ if i = 40 then
                                                f "v" (TVar "T") Optional
                                            else
                                                f
                                                    "x"
                                                    (TRecord(sprintf "R%d" (i + 1), [ TTuple [ TVar "T"; TVar "T" ] ]))
                                                    Optional ] ]

                        let answers =
                            within 20.0 "the fold" (fun () ->
                                [ "F#",
                                  described (
                                      Gen.fsharpModuleDerived
                                          GenSupport.Empty
                                          [ Derivation.Fold "Tree" ]
                                          "Probe"
                                          idl
                                          [ "K" ]
                                  )
                                  "TypeScript",
                                  described (Gen.typescriptModuleDerived [ Derivation.Fold "Tree" ] idl [ "K" ]) ])

                        for host, answer in answers do
                            match answer with
                            | Error why ->
                                Expect.stringContains
                                    why
                                    ("a fold over 'Tree' through " + oversized)
                                    (host + ": refused, naming what was measured")

                                Expect.stringContains
                                    why
                                    "so they must be few enough to read"
                                    (host + ": and why a fold reads them all")
                            | Ok() -> failtestf "%s: the fold was emitted" host) ]

          testList
              "every entry answers over a declaration that grows along two members"
              [ for asUnion in [ false; true ] do
                    let what = if asUnion then "union" else "record"
                    let grows = sentence what "G" "G<'T>" "G<list<'T>>"

                    testCase (what + ": the declaration check and the artifact reader") (fun _ ->
                        let check (label: string) (idl: Idl) : string list =
                            within 20.0 (label + ": Declare.errors") (fun () -> Declare.errors idl)
                            |> List.map _.Detail

                        // No defaulted member: nothing to judge.
                        let plain = twoWay asUnion (f "v" (TVar "T") Optional) TStr
                        Expect.isEmpty (check "no default" plain) "admitted"

                        // A literal that fits whatever the parameter is: every instantiation, unread.
                        let fitsAll = twoWay asUnion (f "v" (TList(TVar "T")) (OmitDefault(VList []))) TInt

                        Expect.isEmpty (check "an empty list at list<T>" fitsAll) "admitted"

                        // A literal that fits `G<str>` and not what it holds.
                        let fitsFirst = twoWay asUnion (f "v" (TVar "T") (OmitDefault(VStr ""))) TStr

                        match check "an empty string at T" fitsFirst with
                        | [ only ] ->
                            Expect.stringContains
                                only
                                "does not fit the field's type at the instantiation G<list<str>> and at least 3 more ("
                                "refused at the first instantiation reached that the literal does not fit, counting one of each other class at depth one it does not fit, a lower bound"
                        | other -> failtestf "one fault expected: %A" other

                        // A literal that fits every instantiation the vocabulary reaches, though not
                        // every type: `[]` at `T`, from `G<list<int>>` through lists alone.
                        let self (t: IdlType) = refer asUnion "G" [ t ]

                        let unions, records =
                            declared
                                asUnion
                                "G"
                                [ "T" ]
                                [ f "l" (self (TList(TVar "T"))) Optional
                                  f "v" (TVar "T") (OmitDefault(VList [])) ]

                        let lists (at: IdlType) =
                            vocabulary [ kind "K" [ f "g" (self at) Optional ] ] unions records

                        Expect.isEmpty (check "an empty list at T, from a list" (lists (TList TInt))) "admitted"

                        Expect.equal
                            (check "an empty list at T, from an integer" (lists TInt) |> List.length)
                            1
                            "and refused from an argument it does not fit"

                        for label, idl in [ "no default", plain; "an empty list at list<T>", fitsAll ] do
                            let text = Artifact.render idl

                            Expect.equal
                                (within 20.0 (label + ": Artifact.parse") (fun () -> Artifact.parse text))
                                (Ok(Artifact.canonicalise idl))
                                (label + ": the artifact reads back")

                        Expect.isError
                            (within 20.0 "Artifact.parse" (fun () -> Artifact.parse (Artifact.render fitsFirst)))
                            "and a refused vocabulary's artifact is refused")

                    testCase (what + ": every generator entry") (fun _ ->
                        let idl = twoWay asUnion (f "v" (TVar "T") Optional) TStr

                        let answers =
                            within 60.0 "the generator entries" (fun () -> backends idl @ schemas idl)
                            |> List.map (fun (entry, answer) ->
                                entry,
                                (match answer with
                                 | Ok() -> "emits"
                                 | Error description when description.Contains grows -> "refuses: " + grows
                                 | Error description -> "refuses otherwise: " + description))

                        Expect.equal
                            answers
                            [ "fsharpTypes", "emits"
                              "fsharpModule", "emits"
                              "fsharpModuleDerived", "emits"
                              "fsharpCodecModule", "emits"
                              "fsharpCodecModuleWith", "emits"
                              "fsharpValue", "emits"
                              "typescriptModule", "emits"
                              "typescriptModuleDerived", "emits"
                              "typescriptDeclarations", "emits"
                              "typescriptDeclarationsDerived", "emits"
                              "typescriptValue", "emits"
                              "FStarTarget.vocabularyModule", "refuses: " + grows
                              "FStarTarget.proofsModule", "refuses: " + grows
                              "jsonSchema", "refuses: " + grows
                              "contractArgumentSchemas", "emits" ]
                            "a generic stated natively is emitted; each instantiation named is refused, in one sentence"

                        let partition =
                            within 20.0 "FStarTarget.partition" (fun () -> FStarTarget.partition idl)

                        Expect.equal
                            (partition |> List.map (fun v -> v.Tag, v.Refusal.IsSome))
                            [ "K", true; "Plain", false ]
                            "only the kind that reaches the declaration is refused"

                        Expect.equal
                            (within 20.0 "FStarTarget.proofKinds" (fun () -> FStarTarget.proofKinds idl))
                            [ "Plain" ]
                            "and the other is modelled")

                    testCase (what + ": the classifier") (fun _ ->
                        // A member restated beside the growing declaration: `Box<T>.v` from `T` to
                        // the `int` it is held at.
                        let beside (v: IdlType) : Idl =
                            let idl = twoWay asUnion (f "v" (TVar "T") Optional) TStr
                            let unions, records = declared asUnion "Box" [ "T" ] [ f "v" v Required ]

                            { idl with
                                Kinds = idl.Kinds @ [ kind "L" [ f "b" (refer asUnion "Box" [ TInt ]) Optional ] ]
                                Unions = idl.Unions @ unions
                                Records = idl.Records @ records }

                        let classify (before: Idl) (after: Idl) =
                            within 20.0 "Diff.ofValues" (fun () -> Diff.ofValues (before, None) (after, None))

                        match classify (beside (TVar "T")) (beside TInt) with
                        | Ok verdict ->
                            Expect.equal
                                (verdict.Changes
                                 |> List.map (fun c ->
                                     c.Severity,
                                     (match c.Change with
                                      | FieldTypeChanged(_, n, _, _) -> "changed " + n
                                      | other -> sprintf "%A" other)))
                                [ BreakingWire, "changed v" ]
                                "answered, and on the side of the break: the reach cannot be read to excuse it"
                        | Error e -> failtestf "the pair did not classify: %s" e

                        // The growing declaration held at another argument.
                        let plain = twoWay asUnion (f "v" (TVar "T") Optional)

                        match classify (plain TStr) (plain TInt) with
                        | Ok verdict ->
                            Expect.equal
                                (verdict.Changes |> List.map _.Severity)
                                [ BreakingWire ]
                                "a moved argument is a break"
                        | Error e -> failtestf "the pair did not classify: %s" e) ]

          testList
              "a long finite chain is read to its end"
              [ testCase "sixty-five, seventy and seven hundred generic declarations: the default rule" (fun _ ->
                    // `A1<T> { x: A2<T> } … An<T> { v: T = "" }` held at `A1<int>`: no recursion, and
                    // one instantiation of each.
                    let chain (n: int) : Idl =
                        vocabulary
                            [ kind "K" [ f "m" (TRecord("A1", [ TInt ])) Optional ] ]
                            []
                            [ for i in 1..n do
                                  record
                                      (sprintf "A%d" i)
                                      [ "T" ]
                                      [ if i = n then
                                            f "v" (TVar "T") (OmitDefault(VStr ""))
                                        else
                                            f "x" (TRecord(sprintf "A%d" (i + 1), [ TVar "T" ])) Optional ] ]

                    for n in [ 10; 64; 65; 70; 700 ] do
                        match
                            within 60.0 (sprintf "a chain of %d" n) (fun () -> Declare.errors (chain n))
                            |> List.map _.Detail
                        with
                        | [ only ] ->
                            Expect.stringContains
                                only
                                (sprintf "at the instantiation A%d<int> and at least 0 more (" n)
                                (sprintf "a chain of %d: refused at its last instantiation" n)
                        | other -> failtestf "a chain of %d: one fault expected, got %A" n other

                    // The document that leaves the member out is the one the rule is for.
                    let idl = chain 70

                    let inner = (VRecord [], [ 1..69 ]) ||> List.fold (fun v _ -> VRecord [ "x", v ])

                    match Encode.encode idl (VNode("n", "K", [ "m", inner ])) with
                    | Ok bytes ->
                        Expect.notEqual
                            (Decode.decode idl bytes |> Result.bind (Encode.encode idl))
                            (Ok bytes)
                            "the refused vocabulary's document does not read back"
                    | Error e -> failtestf "the document was not written: %s" e)

                testCase
                    "sixty-five and seventy generic declarations, and a nest of seventy: the model and the schema"
                    (fun _ ->
                        let chain (n: int) : Idl =
                            vocabulary
                                [ kind "K" [ f "m" (TRecord("A1", [ TInt ])) Required ] ]
                                []
                                [ for i in 1..n do
                                      record
                                          (sprintf "A%d" i)
                                          [ "T" ]
                                          [ if i = n then
                                                f "v" (TVar "T") Required
                                            else
                                                f "x" (TRecord(sprintf "A%d" (i + 1), [ TVar "T" ])) Required ] ]

                        // `Lab<Lab<…<int>…>>`, n deep: n instantiations of one declaration, and finite.
                        let nest (n: int) : Idl =
                            vocabulary
                                [ kind
                                      "K"
                                      [ f
                                            "r"
                                            ((TInt, [ 1..n ]) ||> List.fold (fun t _ -> TRecord("Lab", [ t ])))
                                            Required ] ]
                                []
                                [ record "Lab" [ "T" ] [ f "value" (TVar "T") Required ] ]

                        for label, idl in
                            [ "a chain of 65", chain 65
                              "a chain of 70", chain 70
                              "a nest of 60", nest 60
                              "a nest of 70", nest 70 ] do
                            Expect.isEmpty (Declare.errors idl) (label + ": admitted")

                            Expect.isOk
                                (within 120.0 (label + ": the F* model") (fun () ->
                                    described (FStarTarget.vocabularyModule "Probe" idl [ "K" ])))
                                (label + ": modelled, one type per instantiation")

                            Expect.isOk
                                (within 60.0 (label + ": the JSON schema") (fun () -> described (Gen.jsonSchema idl)))
                                (label + ": one definition per instantiation"))

                testCase "a fold through seventy records that declare no parameter" (fun _ ->
                    // `Tree` holds itself at the end of a chain of ordinary records: nothing here
                    // is instantiated, and the fold descends every one.
                    let chain (n: int) : Idl =
                        let tree = TUnion("Tree", [])

                        vocabulary
                            [ kind "K" [ f "tree" tree Required ] ]
                            [ union
                                  "Tree"
                                  []
                                  [ case "Leaf" [ f "n" TInt Required ]
                                    case "Branch" [ f "r" (TRecord("R0", [])) Required ] ] ]
                            [ for i in 0 .. n - 1 do
                                  record
                                      (sprintf "R%d" i)
                                      []
                                      [ f
                                            "next"
                                            (if i = n - 1 then
                                                 tree
                                             else
                                                 TRecord(sprintf "R%d" (i + 1), []))
                                            Optional ] ]

                    for n in [ 10; 64; 70; 200 ] do
                        let idl = chain n

                        Expect.isOk
                            (described (
                                Gen.fsharpModuleDerived GenSupport.Empty [ Derivation.Fold "Tree" ] "Probe" idl [ "K" ]
                            ))
                            (sprintf "F#: a fold through %d records" n)

                        Expect.isOk
                            (described (Gen.typescriptModuleDerived [ Derivation.Fold "Tree" ] idl [ "K" ]))
                            (sprintf "TypeScript: a fold through %d records" n))

                testCase "a fold is refused for the growth it would follow, and for no other" (fun _ ->
                    // `Tree` holds itself through `R<T>`. Where `R` names itself at a larger argument
                    // in a member the fold descends (a list), the fold has no last record to read.
                    // Where it does so only inside a keyed map, which a fold does not descend, the
                    // fold reads `R<int>` and ends.
                    let through (larger: IdlType -> IdlType) : Idl =
                        vocabulary
                            [ kind "K" [ f "tree" (TUnion("Tree", [])) Required ] ]
                            [ union
                                  "Tree"
                                  []
                                  [ case "Leaf" [ f "n" TInt Required ]
                                    case "Branch" [ f "r" (TRecord("R", [ TInt ])) Required ] ] ]
                            [ record
                                  "R"
                                  [ "T" ]
                                  [ f "tree" (TUnion("Tree", [])) Optional
                                    f "more" (larger (TRecord("R", [ TList(TVar "T") ]))) Optional ] ]

                    let fold (idl: Idl) =
                        [ "F#",
                          described (
                              Gen.fsharpModuleDerived GenSupport.Empty [ Derivation.Fold "Tree" ] "Probe" idl [ "K" ]
                          )
                          "TypeScript", described (Gen.typescriptModuleDerived [ Derivation.Fold "Tree" ] idl [ "K" ]) ]

                    for host, answer in fold (through TList) do
                        match answer with
                        | Error why ->
                            Expect.stringContains
                                why
                                ("a fold over 'Tree' through " + sentence "record" "R" "R<'T>" "R<list<'T>>")
                                (host + ": refused, naming the record and what it reaches itself at")
                        | Ok() -> failtestf "%s: the fold was emitted" host

                    for host, answer in fold (through (fun t -> TKeyedMap(TInt, t))) do
                        Expect.isOk answer (host + ": the growth is in a member the fold does not descend")

                    // The larger argument is a union's instantiation. A fold does not look inside a
                    // union, and the record is still read at one new instantiation after another.
                    let wrapped =
                        let idl = through TList

                        { idl with
                            Unions =
                                idl.Unions
                                @ [ union
                                        "Opt"
                                        [ "X" ]
                                        [ case "None" []; case "Some" [ f "value" (TVar "X") Required ] ] ]
                            Records =
                                [ record
                                      "R"
                                      [ "T" ]
                                      [ f "tree" (TUnion("Tree", [])) Optional
                                        f "more" (TList(TRecord("R", [ TUnion("Opt", [ TVar "T" ]) ]))) Optional ] ] }

                    for host, answer in within 20.0 "the fold" (fun () -> fold wrapped) do
                        match answer with
                        | Error why ->
                            Expect.stringContains
                                why
                                ("a fold over 'Tree' through " + sentence "record" "R" "R<'T>" "R<Opt<'T>>")
                                (host + ": refused, the argument growing inside a union's")
                        | Ok() -> failtestf "%s: the fold was emitted" host)

                testCase
                    "a fold is refused for a recursion through another union met only as a record's argument"
                    (fun _ ->
                        // `Tree` holds itself in a list, and holds `Other`, whose case holds `Lab<Tree>`.
                        // The fold does not look inside `Other`, so every `Tree` behind it would go
                        // unvisited: the recursion through `Other` is found through `Lab`'s argument.
                        let idl (via: IdlType) =
                            vocabulary
                                [ kind "K" [ f "tree" (TUnion("Tree", [])) Required ] ]
                                [ union
                                      "Tree"
                                      []
                                      [ case "TLeaf" [ f "n" TInt Required ]
                                        case
                                            "TBranch"
                                            [ f "o" (TUnion("Other", [])) Optional
                                              f "kids" (TList(TUnion("Tree", []))) Required ] ]
                                  union "Other" [] [ case "Stop" []; case "Go" [ f "via" via Required ] ] ]
                                [ record "Lab" [ "T" ] [ f "label" TStr Required; f "value" (TVar "T") Required ] ]

                        let fold (idl: Idl) =
                            [ "F#",
                              described (
                                  Gen.fsharpModuleDerived
                                      GenSupport.Empty
                                      [ Derivation.Fold "Tree" ]
                                      "Probe"
                                      idl
                                      [ "K" ]
                              )
                              "TypeScript",
                              described (Gen.typescriptModuleDerived [ Derivation.Fold "Tree" ] idl [ "K" ]) ]

                        for host, answer in fold (idl (TRecord("Lab", [ TUnion("Tree", []) ]))) do
                            match answer with
                            | Error why ->
                                Expect.stringContains
                                    why
                                    "a fold over 'Tree', which recurses through the union 'Other'"
                                    (host + ": refused, naming the union")
                            | Ok() -> failtestf "%s: the fold was emitted" host

                        // The control: `Lab` applied to what reaches no `Tree`.
                        for host, answer in fold (idl (TRecord("Lab", [ TInt ]))) do
                            Expect.isOk answer (host + ": Other reaches no Tree, and the fold is emitted")) ]

          testList
              "the default rule"
              [ // Exact over this family: admitted exactly where every document the encoder writes
                // reads back to its bytes. The family holds declarations that grow along one member
                // and along two, an instantiation no value reaches (the argument of a declaration
                // that holds nothing at its parameter), and literals that fit some instantiations of
                // an infinite set and all of another.
                testCase
                    "is exact where the instantiations are infinitely many, and at an instantiation no value reaches"
                    (fun _ ->
                        let mutable admitted = 0
                        let mutable refused = 0
                        let mutable growing = 0

                        for asUnion in [ false; true ] do
                            for declaredAt, literal in
                                [ TVar "T", VStr ""
                                  TVar "T", VList []
                                  TVar "T", VList [ VList [] ]
                                  TList(TVar "T"), VList []
                                  TList(TVar "T"), VList [ VStr "" ] ] do
                                for growth in [ []; [ "l" ]; [ "m" ]; [ "l"; "m" ] ] do
                                    for arg in [ TStr; TInt; TList TStr; TList(TList TInt); TMap TStr ] do
                                        for holder in [ "direct"; "box"; "phantom" ] do
                                            let self (t: IdlType) = refer asUnion "G" [ t ]

                                            let grown (member': string) =
                                                if member' = "l" then TList(TVar "T") else TMap(TVar "T")

                                            let unions, records =
                                                declared
                                                    asUnion
                                                    "G"
                                                    [ "T" ]
                                                    ([ f "v" declaredAt (OmitDefault literal) ]
                                                     @ (growth |> List.map (fun m -> f m (self (grown m)) Optional)))

                                            let held =
                                                match holder with
                                                | "direct" -> self arg
                                                | "box" -> TRecord("Box", [ self arg ])
                                                | _ -> TRecord("Phantom", [ self arg ])

                                            let idl =
                                                vocabulary
                                                    [ kind "K" [ f "g" held Optional ] ]
                                                    unions
                                                    (records
                                                     @ [ record "Box" [ "W" ] [ f "item" (TVar "W") Optional ]
                                                         record "Phantom" [ "U" ] [ f "n" TInt Optional ] ])

                                            let value (members: (string * IdlValue) list) =
                                                if asUnion then VUnion("Mk", members) else VRecord members

                                            // Every value of `G` three instantiations deep: each path
                                            // of growing members, ending in the member left out or
                                            // authored at the literal.
                                            let rec values (depth: int) : IdlValue list =
                                                [ yield value []
                                                  yield value [ "v", literal ]
                                                  if depth > 0 then
                                                      for m in growth do
                                                          for inner in values (depth - 1) do
                                                              yield value [ m, inner ] ]

                                            let documents =
                                                match holder with
                                                | "direct" -> values 3 |> List.map node
                                                | "box" -> values 3 |> List.map (fun v -> node (VRecord [ "item", v ]))
                                                | _ -> [ node (VRecord []); node (VRecord [ "n", VInt 1 ]) ]

                                            let written =
                                                documents
                                                |> List.choose (fun document ->
                                                    match Encode.encode idl document with
                                                    | Ok bytes -> Some bytes
                                                    | Error _ -> None)

                                            let every =
                                                written
                                                |> List.forall (fun bytes ->
                                                    (Decode.decode idl bytes |> Result.bind (Encode.encode idl)) = Ok
                                                        bytes)

                                            let label =
                                                sprintf
                                                    "%s G<T> { v: %A = %A; %A }, at %A, held %s"
                                                    (if asUnion then "union" else "record")
                                                    declaredAt
                                                    literal
                                                    growth
                                                    arg
                                                    holder

                                            Expect.isNonEmpty written (label + ": the encoder writes some value")

                                            let faults =
                                                within 20.0 (label + ": Declare.errors") (fun () -> Declare.errors idl)

                                            if not (List.isEmpty growth) && holder <> "phantom" then
                                                growing <- growing + 1

                                            if List.isEmpty faults then
                                                admitted <- admitted + 1
                                                Expect.isTrue every (label + ": admitted, so every value reads back")
                                            else
                                                refused <- refused + 1

                                                Expect.isFalse
                                                    every
                                                    (label + ": refused, so some value does not read back")

                        // No side is vacuous.
                        Expect.isGreaterThan admitted 100 "declarations are admitted"
                        Expect.isGreaterThan refused 100 "declarations are refused"
                        Expect.isGreaterThan growing 200 "and most of them reach infinitely many instantiations")

                testCase "a class is judged at its first member only where the cut decides it" (fun _ ->
                    // `[[]]` fits `list<list<int>>` and does not fit `list<map<int>>`: two
                    // instantiations that agree one level down (a list of something). The one reached
                    // first is admitted, the cut one level down does not decide that, and so the
                    // other is still judged.
                    let idl =
                        vocabulary
                            [ kind "K" [ f "g" (TRecord("G", [ TList(TList TInt) ])) Optional ]
                              kind "L" [ f "g" (TRecord("G", [ TList(TMap TInt) ])) Optional ] ]
                            []
                            [ record
                                  "G"
                                  [ "T" ]
                                  [ f "l" (TRecord("G", [ TList(TVar "T") ])) Optional
                                    f "v" (TVar "T") (OmitDefault(VList [ VList [] ])) ] ]

                    match within 20.0 "Declare.errors" (fun () -> Declare.errors idl) |> List.map _.Detail with
                    | [ only ] ->
                        Expect.stringContains
                            only
                            "at the instantiation G<list<map<str, int>>> and at least 0 more ("
                            "the instantiation it does not fit"
                    | other -> failtestf "one fault expected: %A" other

                    // And from the deeper one alone every instantiation fits, without end.
                    Expect.isEmpty
                        (within 20.0 "Declare.errors" (fun () ->
                            Declare.errors { idl with Kinds = [ idl.Kinds.Head ] }))
                        "admitted")

                testCase "an instantiation is spelled one way, and a keyed map of strings is not a map" (fun _ ->
                    let at (t: IdlType) =
                        vocabulary
                            [ kind "K" [ f "m" (TRecord("Opt", [ t ])) Optional ] ]
                            []
                            [ record "Opt" [ "T" ] [ f "v" (TVar "T") (OmitDefault(VStr "")) ] ]

                    for t, spelled in
                        [ TMap TInt, "Opt<map<str, int>>"
                          TKeyedMap(TStr, TInt), "Opt<keyedMap<str, int>>" ] do
                        Expect.equal
                            (Declare.errors (at t)
                             |> List.filter (fun d ->
                                 d.Detail.Contains("at the instantiation " + spelled + " and at least 0 more ("))
                             |> List.length)
                            1
                            (spelled + ": one fault names it")) ] ]

// ---------------------------------------------------------------------------
// The declaration check's walk is kept per PARAMETER: what a member's answer reads is the types each
// parameter it names takes, never which go together, so two parameters carried along one chain are
// not multiplied; a member is refused in ONE fault, judged once per class of the instantiations
// found; and two counts bound the walk, each failing closed in its own sentence.
// ---------------------------------------------------------------------------

/// `G1..Gn<A, B>` each name the next at `list<A>` and at `list<tuple<A, int>>`, carrying `B`;
/// `Gn+1..G2n` do the same to `B`, carrying `A`; `G2n+1<A, B> { h: H<A>; i: H<B> }`, held at
/// `G1<list<int>, list<int>>`. `H` has 2^(n+1) instantiations and `G2n+1` has 4^n.
let private carried (n: int) (judged: IdlUnion list) : Idl =
    let g (k: int) (a: IdlType) (b: IdlType) = TUnion(sprintf "G%d" k, [ a; b ])
    let a, b = TVar "A", TVar "B"
    let w1 (t: IdlType) = TList t
    let w2 (t: IdlType) = TList(TTuple [ t; TInt ])

    vocabulary
        [ kind "K" [ f "g" (g 1 (TList TInt) (TList TInt)) Optional ] ]
        ([ for k in 1 .. 2 * n + 1 ->
               union
                   (sprintf "G%d" k)
                   [ "A"; "B" ]
                   [ case
                         "Mk"
                         (if k <= n then
                              [ f "p" (g (k + 1) (w1 a) b) Optional; f "q" (g (k + 1) (w2 a) b) Optional ]
                          elif k <= 2 * n then
                              [ f "r" (g (k + 1) a (w1 b)) Optional; f "s" (g (k + 1) a (w2 b)) Optional ]
                          else
                              [ f "h" (TUnion("H", [ a ])) Optional; f "i" (TUnion("H", [ b ])) Optional ]) ] ]
         @ judged)
        []

/// `U1..Un<T>` each name the next at `list<T>` and at `list<tuple<T, int>>`, the last holding
/// `v: T = literal`, held at `U1<str>`: the last has 2^(n-1) instantiations, every one a list.
let private branching (n: int) (literal: IdlValue) : Idl =
    vocabulary
        [ kind "K" [ f "g" (TUnion("U1", [ TStr ])) Optional ] ]
        [ for k in 1..n ->
              union
                  (sprintf "U%d" k)
                  [ "T" ]
                  [ case
                        "Mk"
                        (if k < n then
                             [ f "a" (TUnion(sprintf "U%d" (k + 1), [ TList(TVar "T") ])) Optional
                               f "b" (TUnion(sprintf "U%d" (k + 1), [ TList(TTuple [ TVar "T"; TInt ]) ])) Optional ]
                         else
                             [ f "v" (TVar "T") (OmitDefault literal) ]) ] ]
        []

[<Tests>]
let perParameterTests =
    testList
        "Phase 732 — the declaration check walks each parameter, not each combination"
        [ testCase "two parameters carried together are not multiplied" (fun _ ->
              let defaulted =
                  [ union "H" [ "T" ] [ case "Mk" [ f "v" (TVar "T") (OmitDefault(VList [])) ] ] ]

              // Every instantiation of `H` is a list, so `[]` fits each: admitted, at once, where a
              // walk over the instantiations of `G2n+1` reads 4^n of them.
              for n in [ 10; 12 ] do
                  Expect.isEmpty
                      (within 10.0 (sprintf "Declare.errors at %d" n) (fun () -> Declare.errors (carried n defaulted)))
                      (sprintf "n = %d: admitted" n)

              let twelve = carried 12 defaulted

              Expect.equal
                  (within 10.0 "Artifact.parse" (fun () -> Artifact.parse (Artifact.render twelve)))
                  (Ok(Artifact.canonicalise twelve))
                  "its artifact reads back"

              // Past the count at `H`'s parameter (2^17, 2^21 and 2^41 types): judged in classes, where
              // `[]` is decided at the outermost form of each, and admitted.
              for n in [ 16; 20; 40 ] do
                  Expect.isEmpty
                      (within 10.0 (sprintf "Declare.errors at %d" n) (fun () -> Declare.errors (carried n defaulted)))
                      (sprintf "n = %d: past the count, judged in classes and admitted" n)

              // The transparent rule reads the same walk.
              let transparentAt (n: int) =
                  { carried
                        n
                        [ union "H" [ "T" ] [ case "Mk" [ f "u" (TUnion("T1", [ TVar "T" ])) Optional ] ]
                          union
                              "T1"
                              [ "X" ]
                              [ case "V" [ f "v" (TVar "X") Required ]; case "Tag" [ f "n" TInt Required ] ] ] with
                      Harden =
                          { HardenPolicy.Undeclared with
                              TransparentUnions = [ "T1", "V" ] } }

              Expect.isEmpty
                  (within 10.0 "Declare.errors, the transparent rule" (fun () -> Declare.errors (transparentAt 10)))
                  "a list is never an object: admitted"

              Expect.isEmpty
                  (within 10.0 "Declare.errors, the transparent rule" (fun () -> Declare.errors (transparentAt 20)))
                  "past the count: judged in classes, and admitted")

          testCase "a literal is refused in one fault, judged once per class of instantiations" (fun _ ->
              // `""` fits no list: refused at each of `U13`'s 4096 and `U15`'s 16384 instantiations,
              // in one fault that names the first and counts the rest. The encoder renders the literal
              // once for all of them: they are one class at their outermost form.
              for n, more in [ 13, 4095; 15, 16383 ] do
                  let idl = branching n (VStr "")

                  let faults =
                      within 10.0 (sprintf "Declare.errors at %d" n) (fun () -> Declare.errors idl)

                  Expect.equal faults.Length 1 (sprintf "n = %d: one fault" n)

                  Expect.stringStarts
                      faults.Head.Detail
                      (sprintf
                          "union 'U%d' case 'Mk', field 'v': the omit-at-default value does not fit the field's type at the instantiation U%d<list<"
                          n
                          n)
                      (sprintf "n = %d: it names the member and the first instantiation" n)

                  Expect.stringContains
                      faults.Head.Detail
                      (sprintf " and at least %d more (authored value does not match IDL type " more)
                      (sprintf "n = %d: it counts the others, and gives the encoder's sentence" n)

                  // Every one of U<n>'s 2^(n-1) instantiations fails, so the count is a true bound, and
                  // here every other: the brute force agrees where it is small enough to run.
                  if n = 13 then
                      Expect.equal (failing idl "U13" "v") (more + 1) "n = 13: the brute force"

              // Eight such members: eight faults, the literal of each rendered once for its 16384
              // instantiations, not once for each (which took seconds a member).
              let eight =
                  let idl = branching 15 (VStr "")

                  { idl with
                      Unions =
                          idl.Unions
                          |> List.map (fun u ->
                              if u.Name = "U15" then
                                  { u with
                                      Cases =
                                          [ case
                                                "Mk"
                                                [ for j in 1..8 ->
                                                      f (sprintf "v%d" j) (TVar "T") (OmitDefault(VStr "")) ] ] }
                              else
                                  u) }

              Expect.equal
                  (within 10.0 "Declare.errors, eight members" (fun () -> Declare.errors eight)
                   |> List.map (fun x -> x.Detail.Contains " and at least 16383 more ("))
                  (List.replicate 8 true)
                  "one fault a member, each counting the rest"

              // A literal that fits some instantiations: one fault, counting only those it does not
              // fit. `U3` is reached at `list<X>` for X each of `list<str>`, `tuple<list<str>, int>`,
              // `list<tuple<str, int>>` and `tuple<list<tuple<str, int>>, int>`; a list of one pair of
              // an empty list and an integer fits the two tuples, and is judged a level deeper at each
              // class until it is answered.
              let pair = VList [ VList [ VList []; VInt 0 ] ]

              let faults =
                  within 10.0 "Declare.errors" (fun () -> Declare.errors (branching 3 pair))
                  |> List.map _.Detail

              Expect.equal (failing (branching 3 pair) "U3" "v") 2 "the brute force: two of the four fail"

              Expect.equal faults.Length 1 "one fault"

              Expect.stringContains
                  faults.Head
                  "at the instantiation U3<list<list<str>>> and at least 1 more ("
                  "the first it does not fit, and the one other"

              // Past the count (`U16` takes 32768 types): judged in classes. Every one is a list, one
              // class at the outermost form, so `[]` is admitted and `""` refused in one fault naming
              // the instantiation that carried the class and counting no other, since it found none:
              // `and at least 0 more`, a true lower bound of the 32767 others it does not fit.
              Expect.isEmpty
                  (within 10.0 "Declare.errors at 16, []" (fun () -> Declare.errors (branching 16 (VList []))))
                  "n = 16, []: judged in classes and admitted"

              let sixteen =
                  within 10.0 "Declare.errors at 16" (fun () -> Declare.errors (branching 16 (VStr "")))
                  |> List.map _.Detail

              Expect.equal sixteen.Length 1 "n = 16: one fault"

              Expect.stringStarts
                  sixteen.Head
                  "union 'U16' case 'Mk', field 'v': the omit-at-default value does not fit the field's type at the instantiation U16<list<"
                  "n = 16: refused, naming the member and an instantiation reached"

              Expect.stringContains
                  sixteen.Head
                  " and at least 0 more ("
                  "n = 16: one class found, so no other counted, and the count a lower bound")

          testCase "the count is of the types one parameter takes, and of no other parameter's" (fun _ ->
              // `Vk<T, S>` doubles `S` (at `list<S>` and `list<tuple<S, int>>`) and passes `T` on;
              // `V15` holds `v: S = ""` and `w: T = ""`. `S` takes 2^14 types at `V15`, each a list,
              // and `T` takes one: every one is judged, `v` fits none and `w` fits each.
              let edge (extra: bool) =
                  vocabulary
                      [ kind
                            "K"
                            [ f "g" (TUnion("V1", [ TStr; TStr ])) Optional
                              if extra then
                                  f "h" (TUnion("V15", [ TStr; TList TBool ])) Optional ] ]
                      [ for k in 1..15 ->
                            union
                                (sprintf "V%d" k)
                                [ "T"; "S" ]
                                [ case
                                      "Mk"
                                      (if k < 15 then
                                           [ f
                                                 "a"
                                                 (TUnion(sprintf "V%d" (k + 1), [ TVar "T"; TList(TVar "S") ]))
                                                 Optional
                                             f
                                                 "b"
                                                 (TUnion(
                                                     sprintf "V%d" (k + 1),
                                                     [ TVar "T"; TList(TTuple [ TVar "S"; TInt ]) ]
                                                 ))
                                                 Optional ]
                                       else
                                           [ f "v" (TVar "S") (OmitDefault(VStr ""))
                                             f "w" (TVar "T") (OmitDefault(VStr "")) ]) ] ]
                      []

              // Exactly the count at `S`: each of its 16384 types judged, `v` refused at each, counted
              // exactly, and `w` admitted.
              let at =
                  within 20.0 "Declare.errors" (fun () -> Declare.errors (edge false))
                  |> List.map _.Detail

              Expect.equal
                  (at
                   |> List.map (fun x ->
                       x.StartsWith "union 'V15' case 'Mk', field 'v': ", x.Contains " and at least 16383 more ("))
                  [ true, true ]
                  "exactly the count at S: v refused at every one of the 16384, w admitted"

              // One more type at `S` is one past the count: `v`, which reads `S`, is judged in
              // classes (two at the outermost form, `list<…>` and `list<bool>`), refused at each and
              // counted only as found; `w`, which reads only `T`, is judged at each and admitted.
              let past =
                  within 20.0 "Declare.errors" (fun () -> Declare.errors (edge true))
                  |> List.map _.Detail

              Expect.equal
                  (past
                   |> List.map (fun x ->
                       x.StartsWith "union 'V15' case 'Mk', field 'v': ", x.Contains " and at least 1 more ("))
                  [ true, true ]
                  "one past it: v judged in classes and refused, counted as found; w admitted")


          testCase "a position an argument past the count is placed at is past it too" (fun _ ->
              // `U1..U15` double `T` (2^14 types at `U15`), `U15` names `C1` at the two wrappings
              // (2^15 at `C1`, past the count) and `C1<T>` copies `T` to `C2<T> { v: T = "" }`. The
              // walk stops reading types at `C1` at the count, so it finds no more than the count at
              // `C2`; `C2` is given every one of `C1`'s, so it is past the count too, and judged in
              // classes: refused at the one class found, never counted as if every type were found.
              let idl =
                  vocabulary
                      [ kind "K" [ f "g" (TUnion("U1", [ TStr ])) Optional ] ]
                      ([ for k in 1..15 ->
                             union
                                 (sprintf "U%d" k)
                                 [ "T" ]
                                 [ case
                                       "Mk"
                                       [ let next = if k < 15 then sprintf "U%d" (k + 1) else "C1"

                                         f "a" (TUnion(next, [ TList(TVar "T") ])) Optional
                                         f "b" (TUnion(next, [ TList(TTuple [ TVar "T"; TInt ]) ])) Optional ] ] ]
                       @ [ union "C1" [ "T" ] [ case "Mk" [ f "c" (TUnion("C2", [ TVar "T" ])) Optional ] ]
                           union "C2" [ "T" ] [ case "Mk" [ f "v" (TVar "T") (OmitDefault(VStr "")) ] ] ])
                      []

              Expect.equal
                  (within 20.0 "Declare.errors" (fun () -> Declare.errors idl)
                   |> List.map (fun x ->
                       x.Detail.StartsWith
                           "union 'C2' case 'Mk', field 'v': the omit-at-default value does not fit the field's type at the instantiation C2<list<",
                       x.Detail.Contains " and at least 0 more ("))
                  [ true, true ]
                  "judged in classes, as the declaration it is copied from is, the one class found counting no other")

          testCase "in classes a refusal decides beside a class the cut leaves open" (fun _ ->
              // `G<T> { l: G<list<T>>; m: G<map<T>>; v: T = [[]] }` held at `G<str>`: at depth one
              // the classes are `str`, `list<str>`, `map<str>`, `list<…>` and `map<…>`, and the literal
              // fails four, the cut leaving `list<…>` open: refused there, counting the three as found,
              // a lower bound of the infinitely many.
              let idl =
                  vocabulary
                      [ kind "K" [ f "g" (TUnion("G", [ TStr ])) Optional ] ]
                      [ union
                            "G"
                            [ "T" ]
                            [ case
                                  "Mk"
                                  [ f "l" (TUnion("G", [ TList(TVar "T") ])) Optional
                                    f "m" (TUnion("G", [ TMap(TVar "T") ])) Optional
                                    f "v" (TVar "T") (OmitDefault(VList [ VList [] ])) ] ] ]
                      []

              Expect.equal
                  (within 10.0 "Declare.errors" (fun () -> Declare.errors idl)
                   |> List.map (fun x -> x.Detail.Contains "at the instantiation G<str> and at least 3 more ("))
                  [ true ]
                  "one fault, at depth one")

          testCase "a member naming a variable no parameter binds is refused, and the check returns" (fun _ ->
              // `Opt<T> { v: '' = "" }`: no binding replaces the variable, so the literal is refused at
              // each instantiation whatever the cut, and no deeper cut is asked.
              let idl =
                  vocabulary
                      [ kind "K" [ f "g" (TRecord("Opt", [ TInt ])) Optional ] ]
                      []
                      [ record "Opt" [ "T" ] [ f "v" (TVar "") (OmitDefault(VStr "")) ] ]

              let faults = within 10.0 "Declare.errors" (fun () -> Declare.errors idl)

              Expect.exists
                  faults
                  (fun x ->
                      x.Detail.Contains
                          "at the instantiation Opt<int> and at least 0 more (unsubstituted type variable '')")
                  "refused at its instantiation, in the encoder's words")

          testCase "a count is a lower bound on every path, 0 included, and never claims to be every one" (fun _ ->
              // Each fault names the first instantiation found that its literal does not fit and says
              // `and at least N more`, N the others found that it does not fit. Each case holds N to
              // the brute force over every instantiation reached (`failing`), and the sentence whole.
              let refusal (idl: Idl) =
                  within 10.0 "Declare.errors" (fun () -> Declare.errors idl) |> List.map _.Detail

              let bounded (label: string) (idl: Idl) (name: string) (field: string) (truth: int) (said: string) =
                  let faults = refusal idl
                  Expect.equal faults [ said ] (label + ": one fault, in the one form")
                  Expect.equal (failing idl name field) truth (label + ": the brute force")
                  Expect.isLessThanOrEqual (atLeast faults.Head) (truth - 1) (label + ": a true lower bound")

              // `G<A, B> { y: Y<tuple<A, B>> }` and `Y<P> { v: P = L }` held at `G<str, int>`,
              // `G<int, str>` and `G<str, str>`: the last brings no type new at `A` or at `B`, so the
              // walk does not enter it, and `Y<tuple<str, str>>`, which it reaches, is not found.
              let gy (literal: IdlValue) =
                  vocabulary
                      [ kind
                            "K"
                            [ for i, (a, b) in List.indexed [ TStr, TInt; TInt, TStr; TStr, TStr ] ->
                                  f (sprintf "r%d" i) (TRecord("G", [ a; b ])) Optional ] ]
                      []
                      [ record "G" [ "A"; "B" ] [ f "y" (TRecord("Y", [ TTuple [ TVar "A"; TVar "B" ] ])) Optional ]
                        record "Y" [ "P" ] [ f "v" (TVar "P") (OmitDefault literal) ] ]

              let atY (first: string) (more: int) (why: string) =
                  sprintf
                      "record 'Y', field 'v': the omit-at-default value does not fit the field's type at the instantiation Y<%s> and at least %d more (authored value does not match IDL type %s)"
                      first
                      more
                      why

              bounded
                  "all three fail, two found"
                  (gy (VStr ""))
                  "Y"
                  "v"
                  3
                  (atY "tuple<str, int>" 1 "TTuple [TStr; TInt]")

              bounded
                  "a pair of a string and an integer fails two, one found"
                  (gy (VList [ VStr ""; VInt 0 ]))
                  "Y"
                  "v"
                  2
                  (atY "tuple<int, str>" 0 "TInt")

              // `H<A, B>` held at each of the nine pairs of `int`, `bool` and `str`. `v: A = ""` reads
              // only `A`: the walk keeps one instantiation for each type `A` takes, so of the six it
              // does not fit it finds two.
              let pairs =
                  [ for a in [ TInt; TBool; TStr ] do
                        for b in [ TInt; TBool; TStr ] -> a, b ]

              let held (at: (IdlType * IdlType) list) (members: IdlField list) =
                  vocabulary
                      [ kind
                            "K"
                            [ for i, (a, b) in List.indexed at -> f (sprintf "h%d" i) (TRecord("H", [ a; b ])) Optional ] ]
                      []
                      [ record "H" [ "A"; "B" ] members ]

              let atH (more: int) =
                  sprintf
                      "record 'H', field 'v': the omit-at-default value does not fit the field's type at the instantiation H<int, int> and at least %d more (authored value does not match IDL type TInt)"
                      more

              bounded
                  "six of nine"
                  (held pairs [ f "v" (TVar "A") (OmitDefault(VStr "")); f "w" (TVar "B") Optional ])
                  "H"
                  "v"
                  6
                  (atH 1)

              // `v: tuple<A, B> = ["", ""]` reads both and fits only `H<str, str>`: the walk keeps
              // the five that bring a type new at `A` or at `B`, each of which it fails.
              let both =
                  [ f "v" (TTuple [ TVar "A"; TVar "B" ]) (OmitDefault(VList [ VStr ""; VStr "" ])) ]

              bounded "eight of nine" (held pairs both) "H" "v" 8 (atH 4)

              // Where every instantiation happens to be found the count is every other, and the
              // sentence is the same: one type at `A`, two at `B`.
              bounded "two of two" (held [ TInt, TInt; TInt, TBool ] both) "H" "v" 2 (atH 1)

              // A member failing once: `Opt<T> { v: T = "" }` at `Opt<int>` and `Opt<str>`.
              bounded
                  "one of two"
                  (vocabulary
                      [ kind
                            "K"
                            [ f "a" (TRecord("Opt", [ TInt ])) Optional
                              f "b" (TRecord("Opt", [ TStr ])) Optional ] ]
                      []
                      [ record "Opt" [ "T" ] [ f "v" (TVar "T") (OmitDefault(VStr "")) ] ])
                  "Opt"
                  "v"
                  1
                  "record 'Opt', field 'v': the omit-at-default value does not fit the field's type at the instantiation Opt<int> and at least 0 more (authored value does not match IDL type TInt)")

          testCase
              "the walk that reads each instantiation stops at the step budget, exactly; what it read whole is judged, the rest in classes"
              (fun _ ->
                  // Chain `j` is `Cj_1..Cj_14<T>`, each naming the next at `list<T>` and at
                  // `list<tuple<T, int>>`, the last holding `v: T = ""`, held at `Cj_1<list<int>>`: a
                  // walk reads 2^14 - 1 instantiations a chain, and `Cj_14`'s 8192 types are all lists.
                  // Sixty-four chains read 1048512, so `pad` more references to an instantiation read
                  // already bring the walk to the budget: at sixty-four exactly, and at sixty-five one
                  // past it, the last reference unread.
                  let chains = 64

                  let parallel (pad: int) (beside: IdlField list) (records: IdlRecord list) (unions: IdlUnion list) =
                      let u j k t = TUnion(sprintf "C%d_%d" j k, [ t ])

                      { vocabulary
                            [ kind
                                  "K"
                                  [ for j in 1..chains -> f (sprintf "g%d" j) (u j 1 (TList TInt)) Optional
                                    for p in 1..pad -> f (sprintf "p%d" p) (u chains 1 (TList TInt)) Optional
                                    yield! beside ] ]
                            ([ for j in 1..chains do
                                   for k in 1..14 ->
                                       union
                                           (sprintf "C%d_%d" j k)
                                           [ "T" ]
                                           [ case
                                                 "Mk"
                                                 (if k < 14 then
                                                      [ f "a" (u j (k + 1) (TList(TVar "T"))) Optional
                                                        f "b" (u j (k + 1) (TList(TTuple [ TVar "T"; TInt ]))) Optional ]
                                                  else
                                                      [ f "v" (TVar "T") (OmitDefault(VStr "")) ]) ] ]
                             @ unions)
                            records with
                          Harden =
                              { HardenPolicy.Undeclared with
                                  TransparentUnions = [ "T1", "V" ] } }

                  let chain (j: int) (x: string) =
                      x.StartsWith(sprintf "union 'C%d_14' case 'Mk', field 'v': " j)

                  let counted (x: string) = x.Contains " and at least 8191 more ("

                  // At the budget exactly, every chain was read whole: each is judged at its 8192
                  // instantiations and counts the 8191 others, every one. Beside them, `Hold<T> { u: T1<T> }` held at
                  // `Hold<Box<int>>` and `Hold<Box<str>>`, `T1`'s case `V` transparent at its
                  // parameter: the other rule, which shares the budget, has none left, so it judges
                  // `Hold.u` in classes, where it is refused at both and counts the other as found.
                  let atBudget =
                      within 60.0 "Declare.errors at the budget" (fun () ->
                          Declare.errors (
                              parallel
                                  chains
                                  [ f "h1" (TRecord("Hold", [ TRecord("Box", [ TInt ]) ])) Optional
                                    f "h2" (TRecord("Hold", [ TRecord("Box", [ TStr ]) ])) Optional ]
                                  [ record "Box" [ "W" ] [ f "item" (TVar "W") Required ]
                                    record "Hold" [ "T" ] [ f "u" (TUnion("T1", [ TVar "T" ])) Required ] ]
                                  [ union
                                        "T1"
                                        [ "X" ]
                                        [ case "V" [ f "v" (TVar "X") Required ]; case "Tag" [ f "n" TInt Required ] ] ]
                          ))
                      |> List.map _.Detail

                  Expect.equal
                      [ for j in 1..chains -> atBudget |> List.filter (chain j) |> List.map counted ]
                      (List.replicate chains [ true ])
                      "at the budget exactly: each chain read whole, judged and counting every other"

                  Expect.equal
                      (atBudget |> List.filter (fun x -> x.StartsWith "record 'Hold'"))
                      [ "record 'Hold', field 'u': at the instantiation Hold<Box<int>> and at least 1 more, instantiates union 'T1' so that its transparent case's field 'v' can encode to an object — a bare object is indistinguishable from a tagged case" ]
                      "the other rule finds the budget spent, and judges in classes"

                  // One past it: the walk stops with the last reference unread, which leads to the
                  // last chain only. Each other chain was read whole and is judged as before; the
                  // last is judged in classes, one class at the outermost form, so it counts no other.
                  let past =
                      within 60.0 "Declare.errors one past the budget" (fun () ->
                          Declare.errors (parallel (chains + 1) [] [] []))
                      |> List.map _.Detail

                  Expect.equal
                      [ for j in 1..chains ->
                            past
                            |> List.filter (chain j)
                            |> List.map (fun x -> counted x, x.Contains " and at least 0 more (") ]
                      (List.replicate (chains - 1) [ true, false ] @ [ [ false, true ] ])
                      "one past the budget: every chain read whole judged as before, the last in classes"

                  Expect.isFalse
                      (past |> List.exists (fun x -> x.Contains "budget"))
                      "and none is refused for the budget: the walk in classes decides each")

          testCase "the walks in classes read at most the step budget, and say so past it" (fun _ ->
              // `U1..U16<T>` each name the next at `tuple<T, json>` and at `tuple<T, list<json>>`, held
              // at `U1<str>`, so `U16`'s parameter takes 2^15 types. `U16` holds `v: T = L`, `L` the
              // pair `[L', []]` nested fifteen deep around `""`, which fits every one of them and is
              // decided only at a cut that reaches its innermost string, and two hundred references
              // to `N { u: U1<str> }`, which declares no parameter: each instantiation of `U16` the
              // walks enter leads to two hundred readings of `N`. The walk that finds each
              // instantiation reads its budget first, and `v` is judged in classes; there `U16` has
              // 2^k classes at depth `k`, so the walks in classes read their budget before they reach
              // the depth `L` is decided at: refused, naming the declaration they did not finish.
              let literal =
                  (VStr "", [ 1..15 ]) ||> List.fold (fun inner _ -> VList [ inner; VList [] ])

              let idl =
                  vocabulary
                      [ kind "K" [ f "g" (TUnion("U1", [ TStr ])) Optional ] ]
                      ([ for k in 1..16 ->
                             union
                                 (sprintf "U%d" k)
                                 [ "T" ]
                                 [ case
                                       "Mk"
                                       (if k < 16 then
                                            [ f
                                                  "a"
                                                  (TUnion(sprintf "U%d" (k + 1), [ TTuple [ TVar "T"; TJson ] ]))
                                                  Optional
                                              f
                                                  "b"
                                                  (TUnion(sprintf "U%d" (k + 1), [ TTuple [ TVar "T"; TList TJson ] ]))
                                                  Optional ]
                                        else
                                            f "v" (TVar "T") (OmitDefault literal)
                                            :: [ for j in 1..200 -> f (sprintf "n%d" j) (TUnion("N", [])) Optional ]) ] ]
                       @ [ union "N" [] [ case "Mk" [ f "u" (TUnion("U1", [ TStr ])) Optional ] ] ])
                      []

              Expect.equal
                  (within 60.0 "Declare.errors" (fun () -> Declare.errors idl) |> List.map _.Detail)
                  [ "union 'U16' case 'Mk', field 'v': the declaration check reads its budget of 1048576 instantiations in classes and stops before it has read every one that leads to union 'U16'; its default cannot be checked at each" ]
                  "past the budget: refused in its own sentence") ]

// ---------------------------------------------------------------------------
// A transparent case typed at a type parameter, instantiated through a generic HOLDER. The case is
// written bare, so its field may not encode to an object; a reference that applies the union to
// its holder's own parameter (`Hold<T> { u: T1<T> }`) is judged at every instantiation of the
// holder a document can hold a value of, and refused at the holder's member.
// ---------------------------------------------------------------------------

/// `vocabulary` with `T1<T>`, whose case `Bare` is transparent and holds `held`.
let private transparent (held: IdlType) (kinds: IdlKind list) (unions: IdlUnion list) (records: IdlRecord list) : Idl =
    { vocabulary
          kinds
          (union "T1" [ "T" ] [ case "Bare" [ f "v" held Required ]; case "Tagged" [ f "n" TInt Required ] ]
           :: unions)
          records with
        Harden =
            { HardenPolicy.Undeclared with
                TransparentUnions = [ "T1", "Bare" ] } }

let private t1 (t: IdlType) : IdlType = TUnion("T1", [ t ])

[<Tests>]
let transparentTests =
    testList
        "Phase 732 — a transparent case instantiated to an object through a generic holder"
        [ testCase "the refusal names the holder, the member and the instantiation" (fun _ ->
              let box = record "Box" [ "W" ] [ f "item" (TVar "W") Required ]

              let through (holders: IdlUnion list * IdlRecord list) (at: IdlType) =
                  transparent (TVar "T") [ kind "K" [ f "g" at Optional ] ] (fst holders) (box :: snd holders)

              let faults (idl: Idl) =
                  Declare.errors idl |> List.map (fun e -> e.At, e.Detail)

              let ending =
                  ", instantiates union 'T1' so that its transparent case's field 'v' can encode to an object — a bare object is indistinguishable from a tagged case"

              Expect.equal
                  (faults (
                      through
                          ([], [ record "Hold" [ "T" ] [ f "u" (t1 (TVar "T")) Required ] ])
                          (TRecord("Hold", [ TRecord("Box", [ TInt ]) ]))
                  ))
                  [ [ PathSegment.Key "records"
                      PathSegment.Index 1
                      PathSegment.Key "fields"
                      PathSegment.Index 0
                      PathSegment.Key "type" ],
                    "record 'Hold', field 'u': at the instantiation Hold<Box<int>> and at least 0 more"
                    + ending ]
                  "a generic record as the holder"

              Expect.equal
                  (faults (
                      through
                          ([ union "HoldU" [ "T" ] [ case "C" [ f "u" (t1 (TVar "T")) Required ] ] ], [])
                          (TUnion("HoldU", [ TMap TInt ]))
                  ))
                  [ [ PathSegment.Key "unions"
                      PathSegment.Index 1
                      PathSegment.Key "cases"
                      PathSegment.Index 0
                      PathSegment.Key "fields"
                      PathSegment.Index 0
                      PathSegment.Key "type" ],
                    "union 'HoldU' case 'C', field 'u': at the instantiation HoldU<map<str, int>> and at least 0 more"
                    + ending ]
                  "a generic union as the holder, at a map"

              // The same holders where the argument is no object: admitted.
              Expect.isEmpty
                  (faults (
                      through
                          ([], [ record "Hold" [ "T" ] [ f "u" (t1 (TVar "T")) Required ] ])
                          (TRecord("Hold", [ TList(TRecord("Box", [ TInt ])) ]))
                  ))
                  "a list of objects is an array")

          // A reference written where the member holds no value of it writes no bytes of it, so it is
          // judged nowhere, whatever another parameter's argument is: `Pair<X, Y> { y: Y }` holds
          // nothing at `X`, so `Hold<T, Q> { u: Pair<T1<T>, Q> }` writes no `T1<T>`, even where `Q`
          // is the same `T1<Box<int>>` that `T` would make. Each vocabulary is refused exactly where a
          // value of the union is written, as it was; no fault repeats another at a second member.
          testCase "a reference is judged only where a value of the member holds it" (fun _ ->
              let box (t: IdlType) = TRecord("Box", [ t ])

              let at (fields: IdlField list) (extra: IdlRecord list) =
                  transparent
                      (TVar "T")
                      [ kind "K" fields ]
                      []
                      ([ record "Box" [ "W" ] [ f "item" (TVar "W") Required ]
                         record "Pair" [ "X"; "Y" ] [ f "y" (TVar "Y") Required ]
                         record "Hold" [ "T"; "Q" ] [ f "u" (TRecord("Pair", [ t1 (TVar "T"); TVar "Q" ])) Required ] ]
                       @ extra)

              let faults (idl: Idl) =
                  Declare.errors idl |> List.map (fun e -> e.At, e.Detail)

              let ending =
                  "instantiates union 'T1' so that its transparent case's field 'v' can encode to an object — a bare object is indistinguishable from a tagged case"

              let atKind (i: int) (name: string) =
                  [ PathSegment.Key "kinds"
                    PathSegment.Index 0
                    PathSegment.Key "fields"
                    PathSegment.Index i
                    PathSegment.Key "type" ],
                  sprintf "kind 'K', field '%s': %s" name ending

              // `T` and `Q` both `Box<int>`-made: the one fault where `T1<Box<int>>` is written held.
              Expect.equal
                  (faults (at [ f "g" (TRecord("Hold", [ box TInt; t1 (box TInt) ])) Optional ] []))
                  [ atKind 0 "g" ]
                  "a closed origin whose two arguments meet"

              Expect.equal
                  (faults (at [ f "g" (TRecord("Hold", [ TInt; t1 (box TInt) ])) Optional ] []))
                  [ atKind 0 "g" ]
                  "a closed origin whose arguments do not meet"

              // `F<P> { h: Hold<P, T1<P>> }` at `F<Box<int>>`: `Q` holds `T1<P>`, so `F.h` writes it.
              Expect.equal
                  (faults (
                      at
                          [ f "g" (TRecord("F", [ box TInt ])) Optional ]
                          [ record "F" [ "P" ] [ f "h" (TRecord("Hold", [ TVar "P"; t1 (TVar "P") ])) Required ] ]
                  ))
                  [ [ PathSegment.Key "records"
                      PathSegment.Index 3
                      PathSegment.Key "fields"
                      PathSegment.Index 0
                      PathSegment.Key "type" ],
                    "record 'F', field 'h': at the instantiation F<Box<int>> and at least 0 more, "
                    + ending ]
                  "a generic origin"

              // `Hold<Box<int>, int>` and `Hold<int, T1<Box<int>>>` are reached and
              // `Hold<Box<int>, T1<Box<int>>>` is not: nothing is asked of a combination.
              Expect.equal
                  (faults (
                      at
                          [ f "g" (TRecord("Hold", [ box TInt; TInt ])) Optional
                            f "g2" (TRecord("Hold", [ TInt; t1 (box TInt) ])) Optional ]
                          []
                  ))
                  [ atKind 1 "g2" ]
                  "two instantiations, neither meeting")

          // Exact over this family: admitted exactly where every document the encoder writes reads
          // back to its bytes. It holds each way a holder passes its parameter to the union (bare,
          // in a list, through a second holder, as the argument of a record that holds it and of
          // one that does not), holders whose instantiations are infinitely many, and a union whose
          // transparent field is a list of its parameter (never an object).
          testCase "is exact over a family of holders, finitely and infinitely instantiated" (fun _ ->
              let mutable admitted = 0
              let mutable refused = 0
              let mutable growing = 0

              let args: (IdlType * IdlValue) list =
                  [ TInt, VInt 1
                    TStr, VStr "s"
                    TList TInt, VList [ VInt 1 ]
                    TList(TRecord("Box", [ TInt ])), VList [ VRecord [ "item", VInt 1 ] ]
                    TRecord("Box", [ TInt ]), VRecord [ "item", VInt 1 ]
                    TRecord("Box", [ TList TInt ]), VRecord [ "item", VList [ VInt 1 ] ]
                    TMap TInt, VMap [ "k", VInt 1 ]
                    TRecord("Plain", []), VRecord [ "n", VInt 1 ] ]

              for bare in [ true; false ] do
                  // The transparent field: the parameter itself, or a list of it.
                  let held = if bare then TVar "T" else TList(TVar "T")
                  let inBare (v: IdlValue) = if bare then v else VList [ v ]

                  let values (v: IdlValue) : IdlValue list =
                      [ VUnion("Bare", [ "v", inBare v ]); VUnion("Tagged", [ "n", VInt 1 ]) ]

                  for arg, argValue in args do
                      for holder in
                          [ "direct"
                            "record"
                            "union"
                            "list"
                            "nested"
                            "passed"
                            "phantom"
                            "grows by a list"
                            "grows into a record" ] do
                          let hold (members: IdlField list) = record "Hold" [ "T" ] members
                          let self (t: IdlType) = TRecord("Hold", [ t ])
                          let u = f "u" (t1 (TVar "T")) Optional

                          let unions, records, at, documents =
                              match holder with
                              | "direct" -> [], [], t1 arg, values argValue
                              | "record" ->
                                  [],
                                  [ hold [ u ] ],
                                  self arg,
                                  values argValue |> List.map (fun v -> VRecord [ "u", v ])
                              | "union" ->
                                  [ union "HoldU" [ "T" ] [ case "C" [ u ] ] ],
                                  [],
                                  TUnion("HoldU", [ arg ]),
                                  values argValue |> List.map (fun v -> VUnion("C", [ "u", v ]))
                              | "list" ->
                                  [],
                                  [ hold [ f "u" (TList(t1 (TVar "T"))) Optional ] ],
                                  self arg,
                                  [ VRecord [ "u", VList(values argValue) ] ]
                              | "nested" ->
                                  [],
                                  [ hold [ u ]; record "Outer" [ "S" ] [ f "h" (self (TVar "S")) Optional ] ],
                                  TRecord("Outer", [ arg ]),
                                  values argValue |> List.map (fun v -> VRecord [ "h", VRecord [ "u", v ] ])
                              | "passed" ->
                                  [],
                                  [ hold [ f "u" (TRecord("Pair", [ t1 (TVar "T"); TInt ])) Optional ]
                                    record "Pair" [ "A"; "B" ] [ f "x" (TVar "A") Required; f "y" (TVar "B") Required ] ],
                                  self arg,
                                  values argValue
                                  |> List.map (fun v -> VRecord [ "u", VRecord [ "x", v; "y", VInt 2 ] ])
                              | "phantom" ->
                                  // No value of `T1<arg>` is ever held: `Phantom` holds nothing at
                                  // its parameter.
                                  [],
                                  [ hold [ f "u" (TRecord("Phantom", [ t1 (TVar "T") ])) Optional ]
                                    record "Phantom" [ "P" ] [ f "n" TInt Optional ] ],
                                  self arg,
                                  [ VRecord []; VRecord [ "u", VRecord [ "n", VInt 1 ] ] ]
                              | "grows by a list" ->
                                  // `Hold<arg>`, `Hold<list<arg>>`, …: only the first can be an object.
                                  [],
                                  [ hold [ u; f "next" (self (TList(TVar "T"))) Optional ] ],
                                  self arg,
                                  [ for v in values argValue do
                                        yield VRecord [ "u", v ]

                                        for deeper in values (VList [ argValue ]) do
                                            yield VRecord [ "u", v; "next", VRecord [ "u", deeper ] ] ]
                              | _ ->
                                  // `Hold<arg>`, `Hold<Box<arg>>`, …: every one after the first is.
                                  [],
                                  [ hold [ u; f "next" (self (TRecord("Box", [ TVar "T" ]))) Optional ] ],
                                  self arg,
                                  [ for v in values argValue do
                                        yield VRecord [ "u", v ]

                                        for deeper in values (VRecord [ "item", argValue ]) do
                                            yield VRecord [ "u", v; "next", VRecord [ "u", deeper ] ] ]

                          let idl =
                              transparent
                                  held
                                  [ kind "K" [ f "g" at Optional ] ]
                                  unions
                                  (records
                                   @ [ record "Box" [ "W" ] [ f "item" (TVar "W") Required ]
                                       record "Plain" [] [ f "n" TInt Required ] ])

                          let label = sprintf "T1<T> { Bare: %A }, held %s at %A" held holder arg

                          let written =
                              documents
                              |> List.choose (fun document ->
                                  match Encode.encode idl (node document) with
                                  | Ok bytes -> Some bytes
                                  | Error _ -> None)

                          Expect.equal
                              (List.length written)
                              (List.length documents)
                              (label + ": the encoder writes every value")

                          let every =
                              written
                              |> List.forall (fun bytes ->
                                  (Decode.decode idl bytes |> Result.bind (Encode.encode idl)) = Ok bytes)

                          let faults = within 20.0 (label + ": Declare.errors") (fun () -> Declare.errors idl)

                          if holder.StartsWith "grows" then
                              growing <- growing + 1

                          if List.isEmpty faults then
                              admitted <- admitted + 1
                              Expect.isTrue every (label + ": admitted, so every value reads back")
                          else
                              refused <- refused + 1
                              Expect.isFalse every (label + ": refused, so some value does not read back")

                              for fault in faults do
                                  Expect.stringContains
                                      fault.Detail
                                      "instantiates union 'T1' so that its transparent case's field 'v' can encode to an object"
                                      (label + ": refused by this rule and no other")

              // No side is vacuous.
              Expect.isGreaterThan admitted 80 "declarations are admitted"
              Expect.isGreaterThan refused 25 "declarations are refused"
              Expect.equal growing 32 "and thirty-two of them reach infinitely many instantiations") ]
