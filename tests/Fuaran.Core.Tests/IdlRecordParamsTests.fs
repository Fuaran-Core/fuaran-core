module Fuaran.Core.Tests.IdlRecordParamsTests

// ---------------------------------------------------------------------------
// Phase 732 — a record takes type parameters, and a record reference applies arguments as a
// union reference does: `TRecord of recordName * args`, `IdlRecord.Params`.
//
// This file holds the DECLARATION: what `Declare.errors` admits and refuses, each refusal in the
// sentence the same fault of a union gets; what the artifact writes and reads; that a vocabulary
// declaring no parameterised record writes neither new member; and that a backend which does not
// emit a parameterised record yet refuses it by the record's name.
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
let private fixtures: (string * Idl) list =
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

/// One description per backend entry that emits a declaration or prints a value and does not emit a
/// parameterised record yet, over `idl`. The JSON-schema entries emit one, and are held to the
/// interpreter in `IdlSchemaValidatorTests`.
let private backends (idl: Idl) : (string * Result<unit, string>) list =
    let tags = idl.Kinds |> List.map _.Tag

    let describe (r: Result<'a, CodegenError>) =
        r |> Result.map ignore |> Result.mapError CodegenError.describe

    [ "fsharpTypes", describe (Gen.fsharpTypes idl)
      "fsharpModule", describe (Gen.fsharpModule "Boxed.Generated" idl tags)
      "fsharpModuleDerived", describe (Gen.fsharpModuleDerived GenSupport.Empty [] "Boxed.Generated" idl tags)
      "fsharpCodecModule",
      describe (
          Gen.fsharpCodecModule
              GenSupport.Empty
              { Roots = [ "Box" ]
                HostTypes = Map.empty }
              "Boxed.Codec"
              idl
      )
      "fsharpValue", describe (Gen.fsharpValue idl TStr (VStr "x"))
      "typescriptModule", describe (Gen.typescriptModule idl tags)
      "typescriptModuleDerived", describe (Gen.typescriptModuleDerived [] idl tags)
      "typescriptDeclarations", describe (Gen.typescriptDeclarations idl tags)
      "typescriptDeclarationsDerived", describe (Gen.typescriptDeclarationsDerived [] idl tags)
      "typescriptValue", describe (Gen.typescriptValue idl TStr (VStr "x"))
      "FStarTarget.vocabularyModule", describe (FStarTarget.vocabularyModule "Boxed" idl tags)
      "FStarTarget.proofsModule", describe (FStarTarget.proofsModule "Boxed" "BoxedProofs" idl tags) ]

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

          testCase
              "the schema emits a parameterised record, and refuses a reference no binding can be made for"
              (fun _ ->
                  Expect.isOk (Gen.jsonSchema boxed) "every instantiation is stated"
                  Expect.isOk (Gen.contractArgumentSchemas boxed) "and a method's arguments read the same definitions"

                  // Arguments applied to a record that declares none: the declaration check refuses the
                  // vocabulary, and a backend handed it unchecked names the reference.
                  let applied =
                      { boxed with
                          Kinds = [ kind "Holder" [ f "shape" (TRecord("Shape", [ TStr ])) Required ] ]
                          Unions = []
                          Records = [ record "Shape" [] [ f "w" TInt Required ] ] }

                  match Gen.jsonSchema applied with
                  | Error e ->
                      Expect.stringContains
                          (CodegenError.describe e)
                          "a reference applying record 'Shape' to 1 type argument(s) where it declares 0"
                          "by the record's name"
                  | Ok schema -> failtestf "a schema naming an instantiation that does not exist: %s" schema)

          testCase "a backend that does not emit a parameterised record yet refuses it by name" (fun _ ->
              // A declared parameter, and separately an applied argument no declaration backs: a
              // backend is handed an `Idl` value, not a declaration that was checked.
              let applied =
                  { boxed with
                      Kinds = [ kind "Holder" [ f "shape" (TRecord("Shape", [ TStr ])) Required ] ]
                      Unions = []
                      Records = [ record "Shape" [] [ f "w" TInt Required ] ] }

              for what, idl, named in [ "declared", boxed, "Box"; "applied", applied, "Shape" ] do
                  for entry, answer in backends idl do
                      match answer with
                      | Ok() -> failtestf "%s (%s): emitted for a parameterised record" entry what
                      | Error description ->
                          Expect.stringContains
                              description
                              (sprintf "a type-parameterised record ('%s')" named)
                              (sprintf "%s (%s) names the record" entry what)) ]
