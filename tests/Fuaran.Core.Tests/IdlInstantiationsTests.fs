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
/// wire members substituted and descended, nothing asked of the declarations first. `None` once it
/// has entered more than `budget` instantiations.
let private walked (idl: Idl) (budget: int) (roots: IdlType list) : Set<string> option =
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

    if seen.Count > budget then
        None
    else
        Some(seen |> Seq.map (sprintf "%A") |> Set.ofSeq)

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

                    // Any other literal has every instantiation to be judged at, and the rule says
                    // it did not judge them.
                    let unjudged =
                        within 30.0 "the declaration check" (fun () ->
                            Declare.errors (chain 13 (f "v" (TVar "T") (OmitDefault(VStr "")))))

                    match unjudged with
                    | [ only ] ->
                        Expect.equal
                            only.Detail
                            "record 'D13', field 'v': the omit-at-default value was not checked at every instantiation, because the vocabulary reaches more than 4096 instantiations of record 'D13' and the generic declarations that reach it"
                            "the member, the declaration it was walked for, and why it was not judged"
                    | other -> failtestf "one fault expected: %A" other)

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
                                "does not fit the field's type at the instantiation Opt<int> ("
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
                        "and judges one node past it")

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
                                "does not fit the field's type at the instantiation G<list<str>> ("
                                "refused at the first instantiation reached that the literal does not fit"
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
                                (sprintf "at the instantiation A%d<int> (" n)
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
                            "at the instantiation G<list<map<str, int>>> ("
                            "the instantiation it does not fit"
                    | other -> failtestf "one fault expected: %A" other

                    // And from the deeper one alone every instantiation fits, without end.
                    Expect.isEmpty
                        (within 20.0 "Declare.errors" (fun () ->
                            Declare.errors { idl with Kinds = [ idl.Kinds.Head ] }))
                        "admitted")

                testCase "an instantiation is spelled one way, and a keyed map of strings is not a map" (fun _ ->
                    let idl =
                        vocabulary
                            [ kind
                                  "K"
                                  [ f "m" (TRecord("Opt", [ TMap TInt ])) Optional
                                    f "k" (TRecord("Opt", [ TKeyedMap(TStr, TInt) ])) Optional ] ]
                            []
                            [ record "Opt" [ "T" ] [ f "v" (TVar "T") (OmitDefault(VStr "")) ] ]

                    let details = Declare.errors idl |> List.map _.Detail

                    for spelled in [ "Opt<map<str, int>>"; "Opt<keyedMap<str, int>>" ] do
                        Expect.equal
                            (details
                             |> List.filter (fun d -> d.Contains("at the instantiation " + spelled + " "))
                             |> List.length)
                            1
                            (spelled + ": one fault names it")) ] ]

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
                    "record 'Hold', field 'u': at the instantiation Hold<Box<int>>" + ending ]
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
                    "union 'HoldU' case 'C', field 'u': at the instantiation HoldU<map<str, int>>"
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
