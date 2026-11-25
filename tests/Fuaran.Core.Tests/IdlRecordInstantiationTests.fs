module Fuaran.Core.Tests.IdlRecordInstantiationTests

// ---------------------------------------------------------------------------
// Phase 732 — a parameterised record at its instantiations: what the interpreter writes, reads and
// samples, what the F# backend and the JSON schema emit for one, and the substitution all of them
// share. [[IdlRecordParamsTests]] holds the declaration.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Tests.IdlRecordParamsTests

/// The three positions substitution did not descend before this phase — a tuple's component, a set's
/// element and a keyed map's value — each as a member's declared type naming the parameter `T`, the
/// member's value authored at `T` = string, and the bytes it is written as.
let private positions: (string * IdlType * IdlValue * string) list =
    [ "tuple", TTuple [ TVar "T"; TInt ], VList [ VStr "a"; VInt 1 ], "[\"a\",1]"
      "keyed",
      TKeyedMap(TInt, TVar "T"),
      VList [ VList [ VInt 1; VStr "a" ]; VList [ VInt 2; VStr "b" ] ],
      "[[1,\"a\"],[2,\"b\"]]"
      "set", TSet(TVar "T"), VList [ VStr "a"; VStr "b" ], "[\"a\",\"b\"]" ]

/// A union and a record that each hold their parameter in the named positions: the union one case
/// per position, the record one optional member per position.
let private wider (names: string list) : Idl =
    let held = positions |> List.filter (fun (n, _, _, _) -> List.contains n names)

    vocabulary
        [ kind
              "Holder"
              [ f "union" (TUnion("Held", [ TStr ])) Optional
                f "record" (TRecord("HeldRecord", [ TStr ])) Optional ] ]
        [ union "Held" [ "T" ] [ for n, t, _, _ in held -> case n [ f "it" t Required ] ] ]
        [ record "HeldRecord" [ "T" ] [ for n, t, _, _ in held -> f n t Optional ] ]

let private everyPosition = wider [ "tuple"; "keyed"; "set" ]

/// `text` reads back and writes the same bytes.
let private roundTrips (idl: Idl) (text: string) : unit =
    Expect.equal (Decode.decode idl text |> Result.bind (Encode.encode idl)) (Ok text) "reads back to the same bytes"

[<Tests>]
let substitution =
    testList
        "Phase 732 — substitution descends a tuple, a set and a keyed map"
        [ testCase "the one shared body replaces a variable in each position, at any depth" (fun _ ->
              let subst = Map.ofList [ "T", TStr; "U", TInt ]

              for declared, expected in
                  [ TTuple [ TVar "T"; TInt; TVar "U" ], TTuple [ TStr; TInt; TInt ]
                    TSet(TVar "U"), TSet TInt
                    TKeyedMap(TVar "U", TVar "T"), TKeyedMap(TInt, TStr)
                    TList(TTuple [ TVar "T"; TSet(TTuple [ TVar "U"; TBool ]) ]),
                    TList(TTuple [ TStr; TSet(TTuple [ TInt; TBool ]) ])
                    TRecord("Box", [ TKeyedMap(TInt, TTuple [ TVar "T"; TVar "U" ]) ]),
                    TRecord("Box", [ TKeyedMap(TInt, TTuple [ TStr; TInt ]) ]) ] do
                  Expect.equal (TypeParams.substitute subst declared) expected (sprintf "%A" declared))

          testCase
              "a union case holding its parameter in each position encodes and decodes at its instantiation"
              (fun _ ->
                  for name, _, value, wire in positions do
                      let authored = VNode("n", "Holder", [ "union", VUnion(name, [ "it", value ]) ])

                      match Encode.encode everyPosition authored with
                      | Ok text ->
                          Expect.stringContains text (sprintf "\"it\":%s" wire) (sprintf "%s: the member's bytes" name)
                          roundTrips everyPosition text
                      | Error e -> failtestf "%s: %s" name e)

          testCase "a record holding its parameter in each position encodes and decodes at its instantiation" (fun _ ->
              for name, _, value, wire in positions do
                  let authored = VNode("n", "Holder", [ "record", VRecord [ name, value ] ])

                  match Encode.encode everyPosition authored with
                  | Ok text ->
                      Expect.stringContains
                          text
                          (sprintf "\"%s\":%s" name wire)
                          (sprintf "%s: the member's bytes" name)

                      roundTrips everyPosition text
                  | Error e -> failtestf "%s: %s" name e)

          testCase "the declaration check admits a variable where an instantiation can order nothing by it" (fun _ ->
              // A tuple's component and a keyed map's value take any type. A set's element and a
              // keyed map's key must be ORDERABLE, which a variable is not (its order would depend on
              // the instantiation), so the check refuses those two by name for a union and a record
              // alike; the interpreter, handed the declaration unchecked, still reads them above.
              Expect.isEmpty
                  (Declare.errors (wider [ "tuple"; "keyed" ]))
                  "a tuple's component and a keyed map's value"

              let refused = Declare.errors (wider [ "set" ]) |> List.map _.Detail

              Expect.equal (List.length refused) 2 "the union's case and the record's member"

              for detail in refused do
                  Expect.stringContains detail "a set's element type must be orderable" "by name") ]

// ---------------------------------------------------------------------------
// The interpreter: [[Encode]], [[Decode]] and [[Sample]] read a record reference's binding as they
// read a union reference's, over [[IdlRecordParamsTests.boxed]].
// ---------------------------------------------------------------------------

/// One `Holder` with every instantiation the vocabulary names in it: `Box<string>` with each of its
/// members, `Pair<int, Box<bool>>` (a record applied to a record), and the given `Binding<float>`.
let private holder (binding: IdlValue) : IdlValue =
    VNode(
        "n",
        "Holder",
        [ "box",
          VRecord
              [ "value", VStr "x"
                "more", VList [ VStr "y" ]
                "maybe", VUnion("Just", [ "it", VStr "z" ]) ]
          "pair", VRecord [ "left", VInt 1; "right", VRecord [ "value", VBool true ] ]
          "binding", binding ]
    )

let private boxAndPair =
    "\"box\":{\"maybe\":{\"$type\":\"Just\",\"it\":\"z\"},\"more\":[\"y\"],\"value\":\"x\"},\"pair\":{\"left\":1,\"right\":{\"value\":true}}}}"

/// Each case of `Binding<float>` and the bytes its `Holder` is written as.
let private holders: (string * IdlValue * string) list =
    [ "a parameter held bare",
      VUnion("Static", [ "value", VFloat 2.5 ]),
      "{\"id\":\"n\",\"kind\":{\"$type\":\"Holder\",\"binding\":{\"$type\":\"Static\",\"value\":2.5},"
      + boxAndPair
      "an inline record held by a parameterised case, its members on the case's object",
      VUnion("Local", [ "payload", VRecord [ "initial", VFloat 2.5; "key", VStr "k" ] ]),
      "{\"id\":\"n\",\"kind\":{\"$type\":\"Holder\",\"binding\":{\"$type\":\"Local\",\"initial\":2.5,\"key\":\"k\"},"
      + boxAndPair
      "a record held by a parameterised case, applied to the case's parameter",
      VUnion("Boxed", [ "box", VRecord [ "value", VFloat 2.5; "more", VList [ VFloat 1.0; VInt 3 ] ] ]),
      "{\"id\":\"n\",\"kind\":{\"$type\":\"Holder\",\"binding\":{\"$type\":\"Boxed\",\"box\":{\"more\":[1,3],\"value\":2.5}},"
      + boxAndPair ]

let private box (args: IdlType list) : IdlType = TRecord("Box", args)

/// A vocabulary whose one kind beside the leaf requires a node behind `Box<node>`.
let private nested: Idl =
    { boxed with
        Kinds = [ kind "Wrap" [ f "inner" (box [ TNode ]) Required ]; kind "Leaf" [] ] }

/// How many `Wrap`s deep a sampled value goes.
let rec private wraps (v: IdlValue) : int =
    let deepest (fields: (string * IdlValue) list) =
        fields |> List.map (snd >> wraps) |> List.fold max 0

    match v with
    | VNode(_, "Wrap", fields) -> 1 + deepest fields
    | VNode(_, _, fields)
    | VRecord fields
    | VUnion(_, fields) -> deepest fields
    | VList items -> items |> List.map wraps |> List.fold max 0
    | _ -> 0

[<Tests>]
let interpreter =
    testList
        "Phase 732 — the interpreter reads a record reference's binding"
        [ testCase "a parameterised record is written at each instantiation, and reads back" (fun _ ->
              Expect.isEmpty (Declare.errors boxed) "the vocabulary is well-formed"

              for what, binding, bytes in holders do
                  Expect.equal (Encode.encode boxed (holder binding)) (Ok bytes) what
                  roundTrips boxed bytes
                  Expect.isOk (Decode.decodeStrictDetailed boxed bytes) (what + ": under the strict reading"))

          testCase "a member is held to the argument its instantiation binds, not to another's" (fun _ ->
              // `Box<string>` and `Box<bool>` are one declaration: what `value` admits is the
              // reference's to say.
              for args, good, bad in [ [ TStr ], JStr "x", JBool true; [ TBool ], JBool true, JStr "x" ] do
                  Expect.isOk
                      (Decode.value boxed (box args) (JObj [ "value", good ]))
                      (sprintf "%A admits its own" args)

                  match Decode.valueDetailed boxed (box args) (JObj [ "value", bad ]) with
                  | Error e ->
                      Expect.equal e.Code DecodeCode.WrongKind (sprintf "%A refuses the other's" args)
                      Expect.equal e.Path [ PathSegment.Key "value" ] "at the member"
                  | Ok v -> failtestf "%A read %A" args v

              match Encode.encode boxed (holder (VUnion("Boxed", [ "box", VRecord [ "value", VStr "x" ] ]))) with
              | Error e ->
                  Expect.stringContains e "does not match IDL type" "the encoder refuses a string at Box<float>"
              | Ok text -> failtestf "encoded %s" text

              // A parameter passed down two levels: `Box<T>.maybe` is `Maybe<T>`.
              let just (it: JVal) =
                  JObj [ "value", JInt 1; "maybe", JObj [ "$type", JStr "Just"; "it", it ] ]

              Expect.isOk
                  (Decode.value boxed (box [ TInt ]) (just (JInt 2)))
                  "the union a member applies takes the record's argument"

              Expect.isError (Decode.value boxed (box [ TInt ]) (just (JStr "2"))) "and refuses another type there")

          testCase "the strict reading names an inline parameterised record's members on its holder" (fun _ ->
              let _, _, bytes = holders[1]

              match
                  Decode.decodeStrictDetailed boxed (bytes.Replace("\"key\":\"k\"", "\"key\":\"k\",\"stray\":1"))
              with
              | Error e -> Expect.equal e.Code DecodeCode.UndeclaredMember "a member the record does not declare"
              | Ok v -> failtestf "read %A" v)

          testCase "a wrong argument count is refused in the words a union's is" (fun _ ->
              // The declaration check refuses both; the interpreter is handed an `Idl` value, not a
              // declaration that was checked, and refuses the reference where it reads the value.
              let asUnion (s: string) =
                  s.Replace("record 'Box'", "union 'Maybe'")

              let recordAt, unionAt = box [], TUnion("Maybe", [])

              let one (t: IdlType) =
                  { boxed with
                      Kinds = [ kind "One" [ f "it" t Required ] ] }

              match
                  Encode.encode (one recordAt) (VNode("n", "One", [ "it", VRecord [ "value", VStr "x" ] ])),
                  Encode.encode (one unionAt) (VNode("n", "One", [ "it", VUnion("Nothing", []) ]))
              with
              | Error r, Error u ->
                  Expect.equal r "record 'Box' given 0 type args, expects 1" "the encoder's sentence"
                  Expect.equal (asUnion r) u "is the union's"
              | other -> failtestf "encoded: %A" other

              match
                  Decode.valueDetailed boxed recordAt (JObj [ "value", JStr "x" ]),
                  Decode.valueDetailed boxed unionAt (JObj [ "$type", JStr "Nothing" ])
              with
              | Error r, Error u ->
                  Expect.equal r.Code DecodeCode.SchemaFault "the decoder's code"

                  Expect.equal
                      (asUnion r.Expected, asUnion r.Message, r.Path)
                      (u.Expected, u.Message, u.Path)
                      "is the union's"
              | other -> failtestf "decoded: %A" other

              match
                  Sample.trySampleNodes (one recordAt) [ "One" ] 1 1, Sample.trySampleNodes (one unionAt) [ "One" ] 1 1
              with
              | Error r, Error u ->
                  Expect.equal (asUnion r.At, r.Reason) (u.At, u.Reason) "the sampler's refusal is the union's"
              | other -> failtestf "sampled: %A" other

              // Arguments applied to a record that declares none.
              let plain =
                  { boxed with
                      Records = boxed.Records @ [ record "Plain" [] [ f "w" TInt Required ] ] }

              match Decode.value plain (TRecord("Plain", [ TStr ])) (JObj [ "w", JInt 1 ]) with
              | Error e -> Expect.stringContains e "1 type args for record 'Plain'" "refused, naming the record"
              | Ok v -> failtestf "read %A" v)

          testCase "the sampler draws each instantiation at its arguments" (fun _ ->
              match Sample.trySampleNodes boxed [ "Holder" ] 732 200 with
              | Error fault -> failtestf "%s" fault.Describe
              | Ok drawn ->
                  for v in drawn do
                      match Encode.encode boxed v with
                      | Ok text -> roundTrips boxed text
                      | Error e -> failtestf "a drawn value does not encode: %s (%A)" e v

                  let cases =
                      drawn
                      |> List.choose (function
                          | VNode(_, _, fields) ->
                              fields
                              |> List.tryPick (function
                                  | "binding", VUnion(tag, _) -> Some tag
                                  | _ -> None)
                          | _ -> None)
                      |> List.distinct
                      |> List.sort

                  Expect.equal cases [ "Boxed"; "Local"; "Static" ] "every case that holds a record is drawn")

          testCase "the depth floor reads a record's arguments, as it reads a union's" (fun _ ->
              // `Wrap` requires a node behind `Box<node>`, so it is no leaf: at the floor the sampler
              // draws `Leaf`. Read as declared alone, `Box`'s member is a variable that reaches
              // nothing, `Wrap` passes for a leaf, and the nesting is bounded only by chance.
              match Sample.trySampleNodes nested [ "Wrap" ] 732 400 with
              | Error fault -> failtestf "%s" fault.Describe
              | Ok drawn ->
                  Expect.equal
                      (drawn |> List.map wraps |> List.distinct |> List.sort)
                      [ 1; 2 ]
                      "a drawn Wrap holds a Leaf, or one more Wrap above the floor, and never a third") ]

// ---------------------------------------------------------------------------
// The fixture vocabulary ([[ParameterisedRecordsIdl]]): a parameterised record in every position,
// read and written by the interpreter.
// ---------------------------------------------------------------------------

[<Tests>]
let fixture =
    testList
        "Phase 732 — the parameterised-records vocabulary, on the interpreter"
        [ testCase "the vocabulary is well-formed, and each of its documents is written and read back" (fun _ ->
              let idl = ParameterisedRecordsIdl.idl

              Expect.isEmpty (Declare.errors idl) "well-formed"

              for what, authored, bytes in ParameterisedRecordsIdl.documents do
                  Expect.equal (Encode.encode idl authored) (Ok bytes) what
                  roundTrips idl bytes
                  Expect.isOk (Decode.decodeStrictDetailed idl bytes) (what + ": under the strict reading"))

          testCase "every drawn document is written, and reads back to the same bytes" (fun _ ->
              let idl = ParameterisedRecordsIdl.idl

              match Sample.trySampleNodes idl ParameterisedRecordsIdl.kindTags 732 600 with
              | Error fault -> failtestf "%s" fault.Describe
              | Ok drawn ->
                  for v in drawn do
                      match Encode.encode idl v with
                      | Ok text ->
                          roundTrips idl text
                          Expect.isOk (Decode.decodeStrictDetailed idl text) "under the strict reading"
                      | Error e -> failtestf "a drawn value does not encode: %s (%A)" e v) ]
