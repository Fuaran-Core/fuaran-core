module Fuaran.Core.Tests.IdlRecordParamsTests

// ---------------------------------------------------------------------------
// Phase 732 — a record takes type parameters, and a record reference applies arguments as a
// union reference does: `TRecord of recordName * args`, `IdlRecord.Params`.
//
// This file holds the DECLARATION: what `Declare.errors` admits and refuses, each refusal in the
// sentence the same fault of a union gets; what the artifact writes and reads; that a vocabulary
// declaring no parameterised record writes neither new member; that every backend entry emits a
// parameterised record and refuses by name a reference no binding can be made for, a union's as a
// record's; what each entry answers over a recursion that grows its type argument; and that an
// omit-at-default member at a type parameter is admitted exactly where every value reads back.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

let f (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Aliases = []
      Keyed = false
      Presence = None
      Annotations = Annotations.Empty }

let kind (tag: string) (fields: IdlField list) : IdlKind =
    { Tag = tag
      Category = "test"
      Fields = fields
      Annotations = Annotations.Empty }

let case (tag: string) (fields: IdlField list) : IdlUnionCase =
    { Hosted = None
      Tag = tag
      Fields = fields
      Annotations = Annotations.Empty }

let union (name: string) (pars: string list) (cases: IdlUnionCase list) : IdlUnion =
    { Name = name
      Params = pars
      Cases = cases
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

let record (name: string) (pars: string list) (fields: IdlField list) : IdlRecord =
    { Name = name
      Params = pars
      Fields = fields
      Annotations = Annotations.Empty
      Inline = false }

let vocabulary (kinds: IdlKind list) (unions: IdlUnion list) (records: IdlRecord list) : Idl =
    { Kinds = kinds
      Unions = unions
      Enums = []
      Records = records
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      Harden = HardenPolicy.Undeclared
      Contracts = [] }

/// `Box<T>` holding its parameter bare, in a list and passed on to a union; `Pair<A, B>`; applied
/// at a kind, at a union case that passes its own parameter down, and as an INLINE record a
/// parameterised case holds (the shape fuaran-core#722 deferred here).
let boxed: Idl =
    vocabulary
        [ kind
              "Holder"
              [ f "box" (TRecord("Box", [ TStr ])) Required
                f "pair" (TRecord("Pair", [ TInt; TRecord("Box", [ TBool ]) ])) Optional
                f "binding" (TUnion("Binding", [ TFloat ])) Required ] ]
        [ union
              "Binding"
              [ "T" ]
              [ case "Static" [ f "value" (TVar "T") Required ]
                case "Local" [ f "payload" (TRecord("Local", [ TVar "T" ])) Required ]
                case "Boxed" [ f "box" (TRecord("Box", [ TVar "T" ])) Required ] ]
          union "Maybe" [ "T" ] [ case "Just" [ f "it" (TVar "T") Required ]; case "Nothing" [] ] ]
        [ record
              "Box"
              [ "T" ]
              [ f "value" (TVar "T") Required
                f "more" (TList(TVar "T")) Optional
                f "maybe" (TUnion("Maybe", [ TVar "T" ])) Optional ]
          record "Pair" [ "A"; "B" ] [ f "left" (TVar "A") Required; f "right" (TVar "B") Required ]
          { record "Local" [ "T" ] [ f "initial" (TVar "T") Required; f "key" TStr Required ] with
              Inline = true } ]

let private details (idl: Idl) : string list = Declare.errors idl |> List.map _.Detail

/// `Opt<T>` as a union of one case `Mk` or as a record, at `args`.
let private direct (asUnion: bool) (arg: IdlType) : IdlType =
    if asUnion then
        TUnion("Opt", [ arg ])
    else
        TRecord("Opt", [ arg ])

/// `Opt<T>` declaring one member `v: declared = literal` (omitted at the literal), `Outer<U>` passing
/// its own parameter down to it, and a kind `K` holding one optional member per type of `held`.
let private defaulted (asUnion: bool) (declared: IdlType) (literal: IdlValue) (held: IdlType list) : Idl =
    let members = [ f "v" declared (OmitDefault literal) ]

    vocabulary
        [ kind "K" (held |> List.mapi (fun i t -> f (sprintf "m%d" i) t Optional)) ]
        (if asUnion then
             [ union "Opt" [ "T" ] [ case "Mk" members ] ]
         else
             [])
        ((if asUnion then [] else [ record "Opt" [ "T" ] members ])
         @ [ record "Outer" [ "U" ] [ f "inner" (direct asUnion (TVar "U")) Required ] ])

/// The one fault of `idl`, as its path and its sentence.
let private theFault (idl: Idl) : PathSegment list * string =
    match Declare.errors idl with
    | [ fault ] -> fault.At, fault.Detail
    | other -> failtestf "one fault expected: %A" other

/// Every member name the artifact writes on a record declaration's own object, and on a record
/// reference's.
let private recordMembers (artifact: JVal) : string list * string list =
    let rec objects (j: JVal) : (string * JVal) list list =
        match j with
        | JObj ms -> ms :: (ms |> List.collect (snd >> objects))
        | JArr xs -> xs |> List.collect objects
        | _ -> []

    let declarations =
        match artifact with
        | JObj ms ->
            ms
            |> List.collect (function
                | "records", JArr rs ->
                    rs
                    |> List.collect (function
                        | JObj r -> List.map fst r
                        | _ -> [])
                | _ -> [])
        | _ -> []

    let references =
        objects artifact
        |> List.filter (List.contains ("$type", JStr "record"))
        |> List.collect (List.map fst)

    declarations, references

/// Whether a type applies a record to an argument, at any depth.
let rec private appliesRecord (t: IdlType) : bool =
    match t with
    | TRecord(_, args) -> not (List.isEmpty args)
    | TList i
    | TMap i
    | TSet i -> appliesRecord i
    | TUnion(_, args)
    | TTuple args -> args |> List.exists appliesRecord
    | TKeyedMap(k, v) -> appliesRecord k || appliesRecord v
    | THosted { Wire = Some w } -> appliesRecord w
    | _ -> false

/// Every type a vocabulary declares a member, an argument or a result at.
let private declaredTypes (idl: Idl) : IdlType list =
    [ idl.NodeFields
      yield! idl.Kinds |> List.map _.Fields
      yield! idl.Ops |> List.map _.Fields
      yield! idl.Records |> List.map _.Fields
      yield! idl.Unions |> List.collect (fun u -> u.Cases |> List.map _.Fields) ]
    |> List.collect (List.map _.Type)
    |> List.append (
        idl.Contracts
        |> List.collect _.Methods
        |> List.collect (fun m -> (m.Arguments |> List.map _.Type) @ Option.toList m.Result)
    )

/// The fixture vocabularies of the suite's fixture library that declare no parameterised record:
/// all of them but [[ParameterisedRecordsIdl]], which is the one built to declare them. A fixture
/// added there is added here.
let fixtures: (string * Idl) list =
    [ "mini", MiniIdl.miniIdl
      "document", SecondDomainIdl.docIdl
      "score", ScoreDomainIdl.scoreIdl
      "reference", ReferenceIdl.refIdl
      "reference, value coverage", ReferenceIdl.valueCoverageIdl
      "reference, unsorted coverage", ReferenceIdl.unsortedCoverageIdl
      "contracts", ContractsIdl.idl
      "derive", DeriveIdl.deriveIdl
      "identity", IdentityIdl.idl
      "layout", LayoutIdl.layout
      "layout, flat twin", LayoutIdl.flat
      "layout, declared order", LayoutIdl.layoutDeclared
      "layout, flat twin in declared order", LayoutIdl.flatDeclared
      "layout holders", LayoutIdl.holders
      "layout derived", LayoutIdl.derived
      "omit when empty", OmitWhenEmptyIdl.idl
      "omit when empty, plain", OmitWhenEmptyIdl.plain
      "refusal corners", RefusalCornersIdl.idl
      "spec decoders", SpecDecodersIdl.idl
      "wider defaults", WiderDefaultsIdl.idl
      "classify base", IdlClassify.Fixtures.baseIdl ]

/// One description per backend entry that emits a declaration or prints a value, over `idl`: the F#
/// and TypeScript entries (held to the interpreter in `IdlRecordFSharpTests`,
/// `IdlRecordTypeScriptTests` and `IdlThreeHostTests`) and the F* entries (whose model is held to it
/// by the decode vectors, `IdlRecordFStarTests`). The JSON-schema entries are held to the interpreter
/// in `IdlSchemaValidatorTests`.
let backends (idl: Idl) : (string * Result<unit, string>) list =
    let tags = idl.Kinds |> List.map _.Tag

    let describe (r: Result<'a, CodegenError>) =
        r |> Result.map ignore |> Result.mapError CodegenError.describe

    // Every reached declaration at a host type, so the codec-only entry has nothing else to refuse.
    let hostTypes =
        (idl.Unions |> List.map _.Name)
        @ (idl.Records |> List.map _.Name)
        @ (idl.Enums |> List.map _.Name)
        |> List.map (fun n -> n, "Boxed.Host." + n)
        |> Map.ofList

    let codecOnly: CodecOnly =
        { Roots = idl.Records |> List.filter (fun r -> not r.Inline) |> List.map _.Name
          HostTypes = hostTypes }

    [ "fsharpTypes", describe (Gen.fsharpTypes idl)
      "fsharpModule", describe (Gen.fsharpModule "Boxed.Generated" idl tags)
      "fsharpModuleDerived", describe (Gen.fsharpModuleDerived GenSupport.Empty [] "Boxed.Generated" idl tags)
      "fsharpCodecModule", describe (Gen.fsharpCodecModule GenSupport.Empty codecOnly "Boxed.Codec" idl)
      "fsharpCodecModuleWith", describe (Gen.fsharpCodecModuleWith GenSupport.Empty codecOnly [] "Boxed.Codec" idl)
      "fsharpValue", describe (Gen.fsharpValue idl TStr (VStr "x"))
      "typescriptModule", describe (Gen.typescriptModule idl tags)
      "typescriptModuleDerived", describe (Gen.typescriptModuleDerived [] idl tags)
      "typescriptDeclarations", describe (Gen.typescriptDeclarations idl tags)
      "typescriptDeclarationsDerived", describe (Gen.typescriptDeclarationsDerived [] idl tags)
      "typescriptValue", describe (Gen.typescriptValue idl TStr (VStr "x"))
      "FStarTarget.vocabularyModule", describe (FStarTarget.vocabularyModule "Boxed" idl tags)
      "FStarTarget.proofsModule", describe (FStarTarget.proofsModule "Boxed" "BoxedProofs" idl tags) ]

/// The two JSON-schema entries, asked the same way.
let schemas (idl: Idl) : (string * Result<unit, string>) list =
    let describe (r: Result<'a, CodegenError>) =
        r |> Result.map ignore |> Result.mapError CodegenError.describe

    [ "jsonSchema", describe (Gen.jsonSchema idl)
      "contractArgumentSchemas", describe (Gen.contractArgumentSchemas idl) ]

[<Tests>]
let tests =
    testList
        "Phase 732 — a record declares type parameters and a reference applies arguments"
        [ testCase "a parameterised record, applied as a union is, is a well-formed declaration" (fun _ ->
              Expect.isEmpty
                  (details boxed)
                  "a parameter bare, in a list and passed to a union; two parameters; an argument that is itself an applied record; a case passing its own parameter down; an inline record held by a parameterised case")

          testCase "a wrong argument count is refused in the sentence a union's is" (fun _ ->
              let at (t: IdlType) =
                  { boxed with
                      Kinds = boxed.Kinds @ [ kind "Bad" [ f "x" t Required ] ] }

              let path =
                  [ PathSegment.Key "kinds"
                    PathSegment.Index 1
                    PathSegment.Key "fields"
                    PathSegment.Index 0
                    PathSegment.Key "type" ]

              Expect.equal
                  (theFault (at (TRecord("Pair", [ TStr ]))))
                  (path, "kind 'Bad', field 'x': applies record 'Pair' to 1 type argument(s); it declares 2")
                  "too few"

              Expect.equal
                  (theFault (at (TRecord("Box", []))))
                  (path, "kind 'Bad', field 'x': applies record 'Box' to 0 type argument(s); it declares 1")
                  "none, to a record that declares one"

              // The union's sentence for the same fault at the same place, word for word.
              let unionPath, unionDetail = theFault (at (TUnion("Maybe", [ TStr; TInt ])))
              let recordPath, recordDetail = theFault (at (TRecord("Box", [ TStr; TInt ])))
              Expect.equal recordPath unionPath "at the same path"

              Expect.equal
                  recordDetail
                  (unionDetail.Replace("union 'Maybe'", "record 'Box'"))
                  "and in the same words, the declaration's kind and name apart"

              // An argument is checked whether or not the count is right, as a union's is.
              Expect.equal
                  (details (at (TRecord("Box", [ TEnum "Nope"; TInt ]))) |> List.sort)
                  [ "kind 'Bad', field 'x': applies record 'Box' to 2 type argument(s); it declares 1"
                    "kind 'Bad', field 'x': names enum 'Nope', which the vocabulary does not declare" ]
                  "both faults")

          testCase "an unknown parameter is refused in the sentence a union's is" (fun _ ->
              let recordFault =
                  theFault
                      { boxed with
                          Records = boxed.Records @ [ record "Loose" [ "T" ] [ f "v" (TVar "U") Required ] ] }

              Expect.equal
                  recordFault
                  ([ PathSegment.Key "records"
                     PathSegment.Index 3
                     PathSegment.Key "fields"
                     PathSegment.Index 0
                     PathSegment.Key "type" ],
                   "record 'Loose', field 'v': type variable 'U' is not a parameter of the record declaring it")
                  "a field naming a variable its record does not declare"

              let _, unionDetail =
                  theFault
                      { boxed with
                          Unions =
                              boxed.Unions
                              @ [ union "Loose" [ "T" ] [ case "Of" [ f "v" (TVar "U") Required ] ] ] }

              Expect.equal
                  (snd recordFault)
                  (unionDetail
                      .Replace("union 'Loose' case 'Of'", "record 'Loose'")
                      .Replace("of the union declaring it", "of the record declaring it"))
                  "the union's sentence, the declaration's kind and name apart"

              // A variable inside an argument is held to the same parameters.
              Expect.equal
                  (details
                      { boxed with
                          Records =
                              boxed.Records
                              @ [ record "Nested" [ "T" ] [ f "v" (TRecord("Box", [ TVar "U" ])) Required ] ] })
                  [ "record 'Nested', field 'v': type variable 'U' is not a parameter of the record declaring it" ]
                  "in a record reference's argument")

          testCase "a parameter that would reach the wire unsubstituted is refused where a union's is" (fun _ ->
              // A kind, an op, the envelope and a contract's method declare no parameter, so a
              // variable there has no instantiation to substitute it. The sentence is the one a
              // union reference's argument gets at the same place.
              let at (t: IdlType) =
                  theFault
                      { boxed with
                          Kinds = boxed.Kinds @ [ kind "Bad" [ f "x" t Required ] ] }

              Expect.equal
                  (at (TRecord("Box", [ TVar "T" ])))
                  (at (TUnion("Maybe", [ TVar "T" ])))
                  "the same path and the same sentence"

              Expect.equal
                  (snd (at (TRecord("Box", [ TVar "T" ]))))
                  "kind 'Bad', field 'x': type variable 'T' names a type parameter, and a kind declares none"
                  "naming the variable, and that a kind declares no parameter to bind it"

              // A record's own parameter is not in scope in another declaration.
              Expect.isNonEmpty
                  (details
                      { boxed with
                          Records =
                              boxed.Records
                              @ [ record "Plain" [] [ f "v" (TRecord("Box", [ TVar "T" ])) Required ] ] })
                  "a record that declares no parameter binds none")

          testCase "a variable where no declaration can bind it names the owner that declares none" (fun _ ->
              // A union's case and a record's field name the declaration that lacks the parameter.
              // A kind, an op, the node envelope and a contract's method declare none at all, and
              // until this phase each was refused in the union's sentence, naming a union that is
              // not there.
              let sentence (idl: Idl) = theFault idl |> snd

              let variable = f "x" (TVar "T") Required

              Expect.equal
                  (sentence
                      { boxed with
                          Kinds = boxed.Kinds @ [ kind "Bad" [ variable ] ] })
                  "kind 'Bad', field 'x': type variable 'T' names a type parameter, and a kind declares none"
                  "a kind"

              Expect.equal
                  (sentence
                      { boxed with
                          Ops = [ kind "Edit" [ variable ] ] })
                  "op 'Edit', field 'x': type variable 'T' names a type parameter, and an op declares none"
                  "an op"

              Expect.equal
                  (sentence { boxed with NodeFields = [ variable ] })
                  "the node envelope, field 'x': type variable 'T' names a type parameter, and the node envelope declares none"
                  "the node envelope"

              let contract = ContractsIdl.idl.Contracts |> List.head
              let method = contract.Methods |> List.head

              let resulting =
                  { ContractsIdl.idl with
                      Contracts =
                          [ { contract with
                                Methods = [ { method with Result = Some(TVar "T") } ] } ] }

              Expect.contains
                  (details resulting)
                  "method 'orders.get': its result: type variable 'T' names a type parameter, and a contract's method declares none"
                  "a contract's method"

              Expect.equal
                  (sentence
                      { boxed with
                          Unions = boxed.Unions @ [ union "Loose" [] [ case "Of" [ variable ] ] ] })
                  "union 'Loose' case 'Of', field 'x': type variable 'T' is not a parameter of the union declaring it"
                  "a union's case keeps its sentence")

          testCase "a record's parameter names are held to the rules a union's are" (fun _ ->
              let recordDetails =
                  details
                      { boxed with
                          Records = boxed.Records @ [ record "Twice" [ "T"; "T"; "a b" ] [] ] }

              let unionDetails =
                  details
                      { boxed with
                          Unions = boxed.Unions @ [ union "Twice" [ "T"; "T"; "a b" ] [ case "Of" [] ] ] }

              Expect.isNonEmpty recordDetails "a repeated parameter and one that is no identifier"

              Expect.equal
                  recordDetails
                  (unionDetails |> List.map _.Replace("union 'Twice'", "record 'Twice'"))
                  "each in the union's sentence")

          testCase "an inline declaration as a record's type argument is held inside another type" (fun _ ->
              let faults =
                  details
                      { boxed with
                          Kinds =
                              boxed.Kinds
                              @ [ kind "Bad" [ f "x" (TRecord("Box", [ TRecord("Local", [ TStr ]) ])) Required ] ] }

              Expect.exists
                  faults
                  (fun d -> d.Contains "inline record 'Local' is held inside another type")
                  "refused as it is as a union's argument")

          testCase "substitution descends a record reference's arguments" (fun _ ->
              let subst = Map.ofList [ "T", TStr; "U", TInt ]

              Expect.equal
                  (TypeParams.substitute subst (TRecord("Pair", [ TVar "T"; TList(TRecord("Box", [ TVar "U" ])) ])))
                  (TRecord("Pair", [ TStr; TList(TRecord("Box", [ TInt ])) ]))
                  "at any depth"

              let pair = boxed.Records |> List.find (fun r -> r.Name = "Pair")

              Expect.equal
                  (TypeParams.bindRecord pair [ TStr; TInt ])
                  (Some(Map.ofList [ "A", TStr; "B", TInt ]))
                  "each parameter bound by name to its argument"

              Expect.isNone (TypeParams.bindRecord pair [ TStr ]) "and no binding at another count")

          testCase "the artifact writes the parameters and the arguments, and reads them back" (fun _ ->
              let text = Artifact.render boxed

              Expect.equal (Artifact.parse text) (Ok(Artifact.canonicalise boxed)) "the vocabulary reads back"

              match Artifact.json boxed with
              | JObj ms ->
                  let records =
                      match ms |> List.find (fun (k, _) -> k = "records") |> snd with
                      | JArr rs -> rs
                      | other -> failtestf "records: %A" other

                  let paramsOf (name: string) =
                      records
                      |> List.pick (function
                          | JObj r when List.contains ("name", JStr name) r ->
                              Some(r |> List.tryFind (fun (k, _) -> k = "params") |> Option.map snd)
                          | _ -> None)

                  Expect.equal (paramsOf "Pair") (Some(JArr [ JStr "A"; JStr "B" ])) "in declared order, never sorted"
              | other -> failtestf "the artifact is not an object: %A" other

              Expect.stringContains
                  (Canonical.write Rendering.CrossHost (Artifact.json boxed))
                  """{"$type":"record","args":[{"$type":"int"},{"$type":"record","args":[{"$type":"bool"}],"name":"Box"}],"name":"Pair"}"""
                  "a reference's arguments in position, nested references included"

              // A hand-edited artifact is refused by the declaration check, as any vocabulary is.
              match Artifact.parse (text.Replace("\"A\",", "\"B\",")) with
              | Error e -> Expect.stringContains e "type parameter 'B' is declared more than once" "by name"
              | Ok _ -> failtest "an artifact declaring one parameter twice read")

          testCase "a vocabulary declaring no parameterised record writes neither member" (fun _ ->
              for name, idl in fixtures do
                  Expect.isTrue
                      (idl.Records |> List.forall (fun r -> List.isEmpty r.Params)
                       && not (declaredTypes idl |> List.exists appliesRecord))
                      (sprintf "%s declares no parameterised record and applies none" name)

                  let declarations, references = recordMembers (Artifact.json idl)

                  Expect.isFalse
                      (List.contains "params" declarations)
                      (sprintf "%s: no record declaration writes `params`" name)

                  Expect.isFalse
                      (List.contains "args" references)
                      (sprintf "%s: no record reference writes `args`" name)

                  Expect.equal
                      (Artifact.parse (Artifact.render idl))
                      (Ok(Artifact.canonicalise idl))
                      (sprintf "%s reads back" name)

              // And the two members are the only ones a record's two objects can gain: a reference
              // is its tag and its name, as it was before the phase.
              let _, references = recordMembers (Artifact.json ReferenceIdl.refIdl)
              Expect.isNonEmpty references "the reference vocabulary names a record"
              Expect.equal (references |> List.distinct |> List.sort) [ "$type"; "name" ] "tag and name alone")

          testCase "every backend entry and both schema entries emit a parameterised record" (fun _ ->
              let emitting = backends boxed

              Expect.equal (List.length emitting) 13 "the six F# entries, the five TypeScript ones and the two F* ones"

              for entry, answer in emitting @ schemas boxed do
                  Expect.equal answer (Ok()) (sprintf "%s emits the parameterised records" entry))

          testCase "a misapplied reference is refused by every entry in one sentence, a union's as a record's" (fun _ ->
              // Each way a reference can apply another number of arguments than its declaration
              // declares, for a union and for a record, at a kind's member.
              let misapplied (asUnion: bool) (pars: string list) (args: IdlType list) : Idl =
                  let held =
                      if asUnion then
                          TUnion("Shape", args)
                      else
                          TRecord("Shape", args)

                  let members = [ f "w" TInt Required ]

                  vocabulary
                      [ kind "Holder" [ f "shape" held Required ] ]
                      (if asUnion then
                           [ union "Shape" pars [ case "Mk" members ] ]
                       else
                           [])
                      (if asUnion then [] else [ record "Shape" pars members ])

              let ways: (string * string list * IdlType list) list =
                  [ "arguments applied to a declaration that declares none", [], [ TStr ]
                    "no argument applied to a declaration that declares one", [ "T" ], []
                    "two arguments applied to a declaration that declares one", [ "T" ], [ TStr; TInt ] ]

              // Every entry is asked of every way before the test answers, so a failure names
              // all of the entries at fault, not the first.
              let atFault = ResizeArray<string>()

              let asked (where: string) (sentence: string) (idl: Idl) =
                  for entry, answer in backends idl @ schemas idl do
                      match answer with
                      | Ok() -> atFault.Add(sprintf "%s: %s emitted" where entry)
                      | Error description ->
                          if not (description.Contains sentence) then
                              atFault.Add(sprintf "%s: %s refused in other words: %s" where entry description)

              for what, asUnion in [ "union", true; "record", false ] do
                  for way, pars, args in ways do
                      let idl = misapplied asUnion pars args
                      Expect.isNonEmpty (Declare.errors idl) (sprintf "%s, %s: the declaration is refused" what way)

                      let sentence =
                          sprintf
                              "a reference applying %s 'Shape' to %d type argument(s) where it declares %d"
                              what
                              (List.length args)
                              (List.length pars)

                      asked (what + ", " + way) sentence idl

              // And at a contract method's argument, which no kind reaches.
              for what, name, held in [ "union", "Maybe", TUnion("Maybe", []); "record", "Box", TRecord("Box", []) ] do
                  let idl =
                      { boxed with
                          Contracts =
                              [ { ContractsIdl.orders with
                                    Methods =
                                        ContractsIdl.orders.Methods
                                        |> List.truncate 1
                                        |> List.map (fun m ->
                                            { m with
                                                Result = None
                                                Arguments =
                                                    m.Arguments
                                                    |> List.truncate 1
                                                    |> List.map (fun x -> { x with Type = held }) }) } ] }

                  asked
                      (what + " at a method's argument")
                      (sprintf "a reference applying %s '%s' to 0 type argument(s) where it declares 1" what name)
                      idl

              Expect.equal (List.ofSeq atFault) [] "every entry refuses every misapplied reference, by name")

          testCase
              "the value entries print a parameterised record's value at its instantiation, and refuse one misapplied in the type handed them"
              (fun _ ->
                  // `backends` asks the value entries of a string, so that they answer for the
                  // vocabulary's own references; here they are asked of a record.
                  let held = TRecord("Box", [ TInt ])
                  let value = VRecord [ "value", VInt 7 ]

                  match Gen.fsharpValue boxed held value, Gen.typescriptValue boxed held value with
                  | Ok fs, Ok ts ->
                      Expect.stringContains fs "Value = 7" "F#: the member at the argument"
                      Expect.stringContains ts "value: 7" "TypeScript: the member at the argument"
                  | other -> failtestf "a value of Box<int>: %A" other

                  // The member is read at the argument: a string there is not a value of Box<int>.
                  for what, answer in
                      [ "F#", Gen.fsharpValue boxed held (VRecord [ "value", VStr "x" ])
                        "TypeScript", Gen.typescriptValue boxed held (VRecord [ "value", VStr "x" ]) ] do
                      match answer with
                      | Error e ->
                          Expect.stringContains
                              (CodegenError.describe e)
                              "a value that does not match IDL type TInt"
                              (what + ": refused at the argument")
                      | Ok src -> failtestf "%s printed %s" what src

                  for args in [ []; [ TInt; TStr ] ] do
                      let misapplied = TRecord("Box", args)

                      for what, answer in
                          [ "F#", Gen.fsharpValue boxed misapplied value
                            "TypeScript", Gen.typescriptValue boxed misapplied value ] do
                          match answer with
                          | Error e ->
                              Expect.stringContains
                                  (CodegenError.describe e)
                                  (sprintf "record 'Box' applied to %d type arguments" args.Length)
                                  (what + ": the handed type is refused, naming the record and the count")
                          | Ok src -> failtestf "%s printed %s at %A" what src misapplied)

          testCase
              "a declaration whose recursion grows its type argument: every entry answers, and each as pinned"
              (fun _ ->
                  // `Grow<T>` holds `Grow<T list>`: no finite set of instantiations. The declaration
                  // check admits it. Every entry returns; the entries that need the finite set (the
                  // F* model, the JSON schema) refuse it by name, and the F# and TypeScript entries,
                  // whose hosts recurse polymorphically, emit it.
                  let growing (asUnion: bool) : Idl =
                      let self (t: IdlType) =
                          if asUnion then
                              TUnion("Grow", [ t ])
                          else
                              TRecord("Grow", [ t ])

                      let members =
                          [ f "value" (TVar "T") Required; f "next" (self (TList(TVar "T"))) Optional ]

                      vocabulary
                          [ kind "K" [ f "g" (self TStr) Required ] ]
                          (if asUnion then
                               [ union "Grow" [ "T" ] [ case "More" members ] ]
                           else
                               [])
                          (if asUnion then [] else [ record "Grow" [ "T" ] members ])

                  for what, asUnion in [ "union", true; "record", false ] do
                      let idl = growing asUnion
                      Expect.isEmpty (Declare.errors idl) (what + ": the declaration check admits it")

                      // The one sentence of every entry that must name each instantiation.
                      let model =
                          sprintf
                              "the generic %s 'Grow', which has no finite set of instantiations: Grow<'T> reaches Grow<list<'T>>"
                              what

                      let answers =
                          backends idl @ schemas idl
                          |> List.map (fun (entry, answer) ->
                              entry,
                              (match answer with
                               | Ok() -> "emits"
                               | Error description when description.Contains model -> "refuses: " + model
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
                            "FStarTarget.vocabularyModule", "refuses: " + model
                            "FStarTarget.proofsModule", "refuses: " + model
                            "jsonSchema", "refuses: " + model
                            // No contract is declared, so no definition is asked for.
                            "contractArgumentSchemas", "emits" ]
                          (what + ": what each entry answers"))

          testCase "a long chain of declarations applied to no argument is not read as a growing recursion" (fun _ ->
              // Seventy declarations each holding the next: deep, and nothing in it grows. How deep a
              // walk has gone says nothing of whether the instantiations it reaches have an end.
              let chain (asUnion: bool) : Idl =
                  let name (i: int) = sprintf "Link%d" i

                  let held (i: int) =
                      if asUnion then TUnion(name i, []) else TRecord(name i, [])

                  let members (i: int) =
                      if i < 69 then
                          [ f "next" (held (i + 1)) Required ]
                      else
                          [ f "last" TStr Required ]

                  vocabulary
                      [ kind "K" [ f "first" (held 0) Required ] ]
                      (if asUnion then
                           [ for i in 0..69 -> union (name i) [] [ case "Mk" (members i) ] ]
                       else
                           [])
                      (if asUnion then
                           []
                       else
                           [ for i in 0..69 -> record (name i) [] (members i) ])

              for what, asUnion in [ "unions", true; "records", false ] do
                  let idl = chain asUnion
                  Expect.isEmpty (Declare.errors idl) (what + ": well-formed")

                  for entry, answer in backends idl @ schemas idl do
                      Expect.equal answer (Ok()) (sprintf "%s: %s emits the chain" what entry))

          testCase
              "an omit-at-default member at a type parameter is refused at the declaration, naming the instantiation its literal does not fit"
              (fun _ ->
                  for asUnion in [ false; true ] do
                      let idl =
                          defaulted asUnion (TVar "T") (VStr "") [ direct asUnion TInt; direct asUnion TStr ]

                      let owner, path =
                          if asUnion then
                              "union 'Opt' case 'Mk', field 'v'",
                              [ PathSegment.Key "unions"
                                PathSegment.Index 0
                                PathSegment.Key "cases"
                                PathSegment.Index 0 ]
                          else
                              "record 'Opt', field 'v'", [ PathSegment.Key "records"; PathSegment.Index 0 ]

                      let at, detail = theFault idl

                      Expect.equal
                          at
                          (path
                           @ [ PathSegment.Key "fields"
                               PathSegment.Index 0
                               PathSegment.Key "optionality"
                               PathSegment.Key "default" ])
                          "at the declaration's literal"

                      Expect.stringStarts
                          detail
                          (owner
                           + ": the omit-at-default value does not fit the field's type at the instantiation Opt<int> and at least 0 more (")
                          "the declaration, the member and the instantiation; `Opt<str>` is not named"

                      // Reached through a generic holder's own parameter, and at a contract method's
                      // argument which no kind reaches: the same fault.
                      let through =
                          defaulted asUnion (TVar "T") (VStr "") [ TRecord("Outer", [ TList TBool ]) ]

                      Expect.stringContains
                          (theFault through |> snd)
                          "at the instantiation Opt<list<bool>> and at least 0 more ("
                          "the argument the holder passes down"

                      let atMethod =
                          { defaulted asUnion (TVar "T") (VStr "") [] with
                              Contracts =
                                  [ { ContractsIdl.orders with
                                        Methods =
                                            ContractsIdl.orders.Methods
                                            |> List.truncate 1
                                            |> List.map (fun m ->
                                                { m with
                                                    Result = Some(direct asUnion TInt)
                                                    Arguments = [] }) } ] }

                      Expect.isTrue
                          (details atMethod
                           |> List.exists (fun d -> d.Contains "at the instantiation Opt<int> and at least 0 more ("))
                          "a contract method's result is an instantiation reached"

                      // No instantiation reached: nothing for the literal to fail to fit.
                      Expect.isEmpty
                          (Declare.errors (defaulted asUnion (TVar "T") (VStr "") []))
                          "an unreached declaration"

                      Expect.isEmpty
                          (Declare.errors (defaulted asUnion (TVar "T") (VStr "") [ direct asUnion TStr ]))
                          "and one reached only where the literal fits"

                      // An argument that is itself an unbound variable is the variable's own fault, and
                      // no instantiation: the literal is not also held against it.
                      match details (defaulted asUnion (TVar "T") (VStr "") [ direct asUnion (TVar "T") ]) with
                      | [ only ] ->
                          Expect.stringContains
                              only
                              "type variable 'T' names a type parameter, and a kind declares none"
                              "the one fault"
                      | other -> failtestf "one fault expected: %A" other

                      // A declaration that holds itself at its own parameter is entered once, so the
                      // fault is stated once; one whose recursion grows its argument has no last
                      // instantiation, and is refused at the first one reached that the literal does
                      // not fit.
                      let recursive (name: string) (next: IdlType) (at: IdlType) : Idl =
                          let self (arg: IdlType) =
                              if asUnion then
                                  TUnion(name, [ arg ])
                              else
                                  TRecord(name, [ arg ])

                          let members =
                              [ f "v" (TVar "T") (OmitDefault(VStr ""))
                                f "next" (TList(self next)) Optional ]

                          vocabulary
                              [ kind "K" [ f "m" (self at) Optional ] ]
                              (if asUnion then
                                   [ union name [ "T" ] [ case "Mk" members ] ]
                               else
                                   [])
                              (if asUnion then [] else [ record name [ "T" ] members ])

                      Expect.stringContains
                          (theFault (recursive "Tree" (TVar "T") TInt) |> snd)
                          "at the instantiation Tree<int> and at least 0 more ("
                          "a recursive declaration, once"

                      let grown = details (recursive "Grow" (TList(TVar "T")) TStr)

                      Expect.stringContains
                          (List.head grown)
                          "at the instantiation Grow<list<str>> and at least 1 more ("
                          "the literal fits `Grow<str>`, and not the instantiation it holds, nor the one class beneath that at depth one: of infinitely many, a lower bound"

                      Expect.equal (List.length grown) 1 "and the member is refused once, there")

          // The rule is exact over this family: a declaration is admitted exactly where every value
          // the encoder writes at each instantiation reads back to the bytes it had.
          testCase
              "an omit-at-default member at a type parameter is admitted exactly where every value reads back"
              (fun _ ->
                  let sample (t: IdlType) : IdlValue =
                      match t with
                      | TStr -> VStr "a"
                      | TInt -> VInt 3
                      | TBool -> VBool true
                      | TFloat -> VFloat 2.5
                      | _ -> VList [ VStr "a" ]

                  let mutable admitted = 0
                  let mutable refused = 0

                  for asUnion in [ false; true ] do
                      for listed in [ false; true ] do
                          for literal in
                              [ VStr ""
                                VStr "x"
                                VInt 0
                                VBool false
                                VFloat 1.5
                                VList []
                                VList [ VInt 1 ] ] do
                              for arg in [ TStr; TInt; TBool; TFloat; TList TStr ] do
                                  for viaHolder in [ false; true ] do
                                      let declared = if listed then TList(TVar "T") else TVar "T"

                                      let held =
                                          if viaHolder then
                                              TRecord("Outer", [ arg ])
                                          else
                                              direct asUnion arg

                                      let idl = defaulted asUnion declared literal [ held ]

                                      let opt (members: (string * IdlValue) list) =
                                          if asUnion then VUnion("Mk", members) else VRecord members

                                      let document (members: (string * IdlValue) list) =
                                          let inner = opt members

                                          VNode(
                                              "n",
                                              "K",
                                              [ "m0", (if viaHolder then VRecord [ "inner", inner ] else inner) ]
                                          )

                                      let ordinary = if listed then VList [ sample arg ] else sample arg

                                      // What the encoder writes of the three candidates: the member left
                                      // out, the member authored at the literal, and an ordinary value.
                                      let written =
                                          [ []; [ "v", literal ]; [ "v", ordinary ] ]
                                          |> List.choose (fun members ->
                                              match Encode.encode idl (document members) with
                                              | Ok bytes -> Some bytes
                                              | Error _ -> None)

                                      let every =
                                          written
                                          |> List.forall (fun bytes ->
                                              (Decode.decode idl bytes |> Result.bind (Encode.encode idl)) = Ok bytes)

                                      let label =
                                          sprintf
                                              "%s, member %A = %A, at %A%s"
                                              (if asUnion then "union" else "record")
                                              declared
                                              literal
                                              arg
                                              (if viaHolder then " through a holder" else "")

                                      Expect.isNonEmpty written (label + ": the encoder writes some value")

                                      if List.isEmpty (Declare.errors idl) then
                                          admitted <- admitted + 1
                                          Expect.isTrue every (label + ": admitted, so every value reads back")
                                      else
                                          refused <- refused + 1
                                          Expect.isFalse every (label + ": refused, so some value does not read back")

                  // Neither side is vacuous.
                  Expect.isGreaterThan admitted 20 "declarations are admitted"
                  Expect.isGreaterThan refused 100 "and declarations are refused") ]
