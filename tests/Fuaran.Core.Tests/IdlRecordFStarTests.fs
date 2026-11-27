module Fuaran.Core.Tests.IdlRecordFStarTests

// ---------------------------------------------------------------------------
// Phase 732 — the F* proof model over a parameterised record.
//
// The model is monomorphic: a record at two instantiations is two model types, each named for its
// arguments as a union's instance is, and an inline one has no type at all — its members are its
// holder's, at the holder's argument. The generated round trip over the two certified vocabularies
// is checked by the proof leg (`proofs/ParameterisedVocabularyProofs.fst`,
// `proofs/ParameterisedFlatVocabularyProofs.fst`, held to the generator by the `Proofs.Vocabulary`
// family); this file holds what the model STATES for each position of the fixture, the refusals the
// target keeps, and the decode vectors that tie the model's reading to the interpreter's.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Idl.Emit
open Fuaran.Core.Tests.IdlRecordParamsTests

let private emitted (r: Result<string, CodegenError>) : string =
    match r with
    | Ok src -> src.Replace("\r\n", "\n")
    | Error e -> failtestf "the generator refused: %s" (CodegenError.describe e)

let private tagsOf (idl: Idl) : string list = idl.Kinds |> List.map _.Tag

let private modelOf (idl: Idl) : string =
    emitted (FStarTarget.vocabularyModule "M" idl (tagsOf idl))

/// The refusal the target gives the kind `tag` of `idl`.
let private refusalOf (idl: Idl) (tag: string) : string =
    match (FStarTarget.partition idl |> List.find (fun v -> v.Tag = tag)).Refusal with
    | Some why -> why
    | None -> failtestf "the F* target models %s" tag

/// The members of a `Grow<T>` that holds `Grow<T list>`, `self` naming the declaration applied to an
/// argument: every instantiation names a new one.
let private growing (self: IdlType -> IdlType) : IdlField list =
    [ f "here" (TVar "T") Required; f "next" (self (TList(TVar "T"))) Optional ]

[<Tests>]
let model =
    testList
        "Phase 732 — the F* model over a parameterised record"
        [ testCase "every kind of the fixture is modelled, in each of its vocabularies" (fun _ ->
              for name, idl, kinds in
                  [ "default", ParameterisedRecordsIdl.idl, 3
                    "flat", ParameterisedRecordsIdl.flat, 3
                    "derived", ParameterisedRecordsIdl.derived, 5
                    "derived, flat", ParameterisedRecordsIdl.derivedFlat, 5 ] do
                  let verdicts = FStarTarget.partition idl
                  Expect.equal (List.length verdicts) kinds (sprintf "%s: its kinds" name)

                  for v in verdicts do
                      Expect.isNone v.Refusal (sprintf "%s: %s is modelled" name v.Tag))

          testCase "a record is one model type per instantiation, named for its arguments" (fun _ ->
              let text = modelOf ParameterisedRecordsIdl.idl

              // Two instantiations of one record, an argument that is itself an applied record, a
              // record passing its parameter on, two parameters (one an enum reached only there).
              for line in
                  [ "  | C__r_ranged__flt__Mk : high:(flt) -> low:(flt) -> steps:(option (list (flt))) -> r_ranged__flt num flt"
                    "  | C__r_ranged__int__Mk : high:(num) -> low:(num) -> steps:(option (list (num))) -> r_ranged__int num flt"
                    "  | C__r_labelled__flt__Mk : label:(string) -> value:(flt) -> r_labelled__flt num flt"
                    "  | C__r_labelled__str__Mk : label:(string) -> value:(string) -> r_labelled__str num flt"
                    "  | C__r_labelled__r_ranged__int__Mk : label:(string) -> value:(r_ranged__int num flt) -> r_labelled__r_ranged__int num flt"
                    "  | C__r_series__flt__Mk : points:(list (r_labelled__flt num flt)) -> r_series__flt num flt"
                    "  | C__r_entry__str__e_direction__Mk : key:(string) -> value:(option (e_direction)) -> r_entry__str__e_direction num flt"
                    // A case of a parameterised union holding a record at the union's own argument,
                    // and a union declaring no parameter holding one at a closed argument.
                    "  | C__u_binding__flt__Bounded : range:(r_ranged__flt num flt) -> u_binding__flt num flt"
                    "  | C__u_binding__int__Bounded : range:(r_ranged__int num flt) -> u_binding__int num flt"
                    "  | C__u_shape__Span : extent:(r_ranged__flt num flt) -> u_shape num flt" ] do
                  Expect.stringContains text (line + "\n") line

              // No type is declared for the generic declaration itself: nothing is named for a bare
              // record that is only ever applied.
              for bare in [ "r_ranged "; "r_labelled "; "r_series "; "r_entry " ] do
                  Expect.isFalse (text.Contains("and " + bare)) (sprintf "no type `%s`" (bare.Trim())))

          testCase "the model's type names are injective: one name exactly where one slot" (fun _ ->
              // Declared names that the old spelling ran together with an instantiation's or with each
              // other's: an underscore before a capital, a doubled, leading or trailing underscore,
              // a capital after another, and names one prefix of another.
              let names =
                  [ "Ranged"
                    "Ranged_Int"
                    "Ranged__int"
                    "Ranged_int"
                    "RangedInt"
                    "A"
                    "AL"
                    "AStr"
                    "Box"
                    "Box_"
                    "_Box"
                    "Foo"
                    "FooEBar"
                    "BarInt"
                    "Foo_bar"
                    "FooBar"
                    "Pair" ]

              let leaves =
                  [ FStarModel.SStr
                    FStarModel.SInt
                    FStarModel.SFloat
                    FStarModel.SList FStarModel.SStr
                    FStarModel.SMap FStarModel.SInt ]
                  @ [ for n in names -> FStarModel.SEnum n ]
                  @ [ for n in names -> FStarModel.SRecord(n, []) ]

              let tuples =
                  [ for a in leaves.[0..2] @ [ FStarModel.SEnum "Foo"; FStarModel.SEnum "Box" ] do
                        for b in leaves.[0..2] @ [ FStarModel.SEnum "BarInt"; FStarModel.SEnum "Foo_bar" ] do
                            FStarModel.STuple [ a; b ] ]

              let args = leaves @ tuples

              let slots =
                  leaves
                  @ tuples
                  @ [ for n in names do
                          for a in args do
                              FStarModel.SRecord(n, [ a ])
                              FStarModel.SUnion(n, [ a ])
                              FStarModel.SList(FStarModel.SRecord(n, [ a ]))

                          for a in leaves do
                              for b in leaves do
                                  FStarModel.SRecord(n, [ a; b ])
                                  FStarModel.SRecord(n, [ FStarModel.SRecord("Box", [ a ]); b ]) ]
                  |> List.distinct

              Expect.isGreaterThan slots.Length 5000 "the family is not vacuous"

              let byName = slots |> List.groupBy FStarModel.slotName

              for name, same in byName do
                  Expect.equal same.Length 1 (sprintf "one slot named %s: %A" name same)

              // Every name is an F* identifier that starts lower case.
              for name, _ in byName do
                  Expect.isTrue
                      (System.Char.IsLower name[0]
                       && name |> Seq.forall (fun c -> System.Char.IsLetterOrDigit c || c = '_'))
                      (sprintf "%s is an F* type name" name)

              // The pairs the old spelling named alike, each now two names.
              for a, b in
                  [ FStarModel.SRecord("Ranged", [ FStarModel.SInt ]), FStarModel.SRecord("Ranged_Int", [])
                    FStarModel.SRecord("Ranged", [ FStarModel.SInt ]), FStarModel.SRecord("Ranged__int", [])
                    FStarModel.SUnion("BoxU", [ FStarModel.SFloat ]), FStarModel.SUnion("BoxU__flt", [])
                    FStarModel.SRecord("Pair", [ FStarModel.SRecord("A", []); FStarModel.SList FStarModel.SStr ]),
                    FStarModel.SRecord("Pair", [ FStarModel.SRecord("AL", []); FStarModel.SStr ])
                    FStarModel.SRecord("Foo_bar", []), FStarModel.SRecord("FooBar", []) ] do
                  Expect.notEqual (FStarModel.slotName a) (FStarModel.slotName b) (sprintf "%A and %A" a b)

              // The names a committed module states keep their spelling.
              Expect.equal
                  ([ FStarModel.SUnion("Slot", [ FStarModel.SStr ])
                     FStarModel.SRecord("Labelled", [ FStarModel.SRecord("Ranged", [ FStarModel.SInt ]) ])
                     FStarModel.SRecord("RangedFloat", []) ]
                   |> List.map FStarModel.slotName)
                  [ "u_slot__str"; "r_labelled__r_ranged__int"; "r_ranged_float" ]
                  "a name with no underscore of its own, and an instantiation of one argument, are unchanged")

          testCase "the model states one type per instantiation beside a declaration its old name met" (fun _ ->
              let typesOf (text: string) =
                  text.Split('\n')
                  |> Array.choose (fun l ->
                      if l.StartsWith "and r_" || l.StartsWith "and u_" then
                          Some(l.Substring(4, l.IndexOf(' ', 4) - 4))
                      else
                          None)
                  |> List.ofArray

              for label, idl, expected in
                  [ "Ranged<int> beside a record Ranged_Int",
                    vocabulary
                        [ kind
                              "K"
                              [ f "a" (TRecord("Ranged", [ TInt ])) Required
                                f "b" (TRecord("Ranged_Int", [])) Required ] ]
                        []
                        [ record "Ranged" [ "T" ] [ f "low" (TVar "T") Required ]
                          record "Ranged_Int" [] [ f "other" TStr Required ] ],
                    [ "r_ranged__int"; "r_ranged_0_int" ]
                    "BoxU<float> beside a union BoxU__flt",
                    vocabulary
                        [ kind
                              "K"
                              [ f "a" (TUnion("BoxU", [ TFloat ])) Required
                                f "b" (TUnion("BoxU__flt", [])) Required ] ]
                        [ union "BoxU" [ "T" ] [ case "Mk" [ f "v" (TVar "T") Required ] ]
                          union "BoxU__flt" [] [ case "Mk" [ f "w" TStr Required ] ] ]
                        [],
                    [ "u_box_u__flt"; "u_box_u_0_0flt" ]
                    "Pair<A, list<str>> beside Pair<AL, str>",
                    vocabulary
                        [ kind
                              "K"
                              [ f "a" (TRecord("Pair", [ TRecord("A", []); TList TStr ])) Required
                                f "b" (TRecord("Pair", [ TRecord("AL", []); TStr ])) Required ] ]
                        []
                        [ record "Pair" [ "X"; "Y" ] [ f "x" (TVar "X") Required; f "y" (TVar "Y") Required ]
                          record "A" [] [ f "n" TInt Required ]
                          record "AL" [] [ f "s" TStr Required ] ],
                    [ "r_pair__r_a__l_str"; "r_a"; "r_pair__r_a_l__str"; "r_a_l" ] ] do
                  Expect.equal
                      (typesOf (modelOf idl) |> List.sort)
                      (List.sort expected)
                      (label
                       + ": one model type per instantiation and per declaration, each its own name")

              // The reach the model reads keys a hosted slot by its format as well as its wire form, as
              // the schema's names do: two instantiations that differ only there are two entries.
              let hosted (format: HostedFormat) =
                  THosted
                      { FSharp = "string"
                        Encode = "JStr"
                        Decode = "Ok"
                        Wire = Some TStr
                        Format = Some format
                        Refusal = HostedRefusal.Sentence }

              let formats =
                  vocabulary
                      [ kind
                            "K"
                            [ f "a" (TRecord("Box", [ hosted HostedFormat.Date ])) Required
                              f "b" (TRecord("Box", [ hosted HostedFormat.Uuid ])) Required ] ]
                      []
                      [ record "Box" [ "T" ] [ f "v" (TVar "T") Required ] ]

              Expect.equal
                  ((Decl.resolve formats Map.empty [ "K" ]).Wire.FromKinds
                   |> List.filter (fun t ->
                       match t with
                       | TRecord("Box", _) -> true
                       | _ -> false)
                   |> List.length)
                  2
                  "a date slot and a uuid slot: two instantiations of Box"

              // Two declared names that differ only in the case of their first letter meet in one
              // word; the vocabulary is refused by name.
              let cased =
                  vocabulary
                      [ kind
                            "K"
                            [ f "a" (TRecord("FooBar", [])) Required
                              f "b" (TRecord("fooBar", [])) Required ] ]
                      []
                      [ record "FooBar" [] [ f "n" TInt Required ]
                        record "fooBar" [] [ f "s" TStr Required ] ]

              Expect.stringContains
                  (refusalOf cased "K")
                  "two types the model would give one name, 'r_foo_bar': FooBar and fooBar"
                  "refused, naming both"

              // A tuple keeps its older spelling, under which two tuples of enums meet; an
              // instantiation at each is refused the same way.
              let enumsOf (names: string list) : IdlEnum list =
                  names
                  |> List.map (fun n ->
                      { Name = n
                        Cases = [ "a" ]
                        Wires = []
                        CaseAnnotations = []
                        Aliases = []
                        Annotations = Annotations.Empty })

              let tupled =
                  { vocabulary
                        [ kind
                              "K"
                              [ f "a" (TRecord("Box", [ TTuple [ TEnum "FooEBar"; TInt ] ])) Required
                                f "b" (TRecord("Box", [ TTuple [ TEnum "Foo"; TEnum "BarInt" ] ])) Required ] ]
                        []
                        [ record "Box" [ "T" ] [ f "v" (TVar "T") Required ] ] with
                      Enums = enumsOf [ "FooEBar"; "Foo"; "BarInt" ] }

              Expect.stringContains
                  (refusalOf tupled "K")
                  "two types the model would give one name, 'r_box__tp2_e_foo_e_bar_int': Box<tuple<FooEBar, int>> and Box<tuple<Foo, BarInt>>"
                  "refused, naming both")

          testCase
              "every top-level name the target emits passes through one table: a pair of each kind that meets is refused by name, the rest are named apart"
              (fun _ ->
                  let enumsOf (names: (string * string list) list) : IdlEnum list =
                      names
                      |> List.map (fun (n, cases) ->
                          { Name = n
                            Cases = cases
                            Wires = []
                            CaseAnnotations = []
                            Aliases = []
                            Annotations = Annotations.Empty })

                  let refusedAs (label: string) (idl: Idl) (parts: string list) =
                      let why =
                          match (FStarTarget.partition idl |> List.find (fun v -> v.Tag = "K")).Refusal with
                          | Some why -> why
                          | None -> failtestf "%s: the F* target models K" label

                      for part in parts do
                          Expect.stringContains why part (label + ": refused by name")

                  // Types. An enum, a union and a generic record whose names differ only in the case of
                  // their first letter; a tuple of enums at a kind's own member; a presence-keyed union's
                  // pick.
                  refusedAs
                      "two enums"
                      { vocabulary [ kind "K" [ f "a" (TEnum "Dir") Required; f "b" (TEnum "dir") Required ] ] [] [] with
                          Enums = enumsOf [ "Dir", [ "n" ]; "dir", [ "s" ] ] }
                      [ "two types the model would give one name, 'e_dir': enum 'Dir' and enum 'dir'" ]

                  refusedAs
                      "two unions"
                      (vocabulary
                          [ kind "K" [ f "a" (TUnion("Uu", [])) Required; f "b" (TUnion("uu", [])) Required ] ]
                          [ union "Uu" [] [ case "A" [ f "n" TInt Required ] ]
                            union "uu" [] [ case "B" [ f "s" TStr Required ] ] ]
                          [])
                      [ "two types the model would give one name, 'u_uu': Uu and uu" ]

                  refusedAs
                      "two generic records"
                      (vocabulary
                          [ kind
                                "K"
                                [ f "a" (TRecord("Box", [ TInt ])) Required
                                  f "b" (TRecord("box", [ TInt ])) Required ] ]
                          []
                          [ record "Box" [ "T" ] [ f "v" (TVar "T") Required ]
                            record "box" [ "T" ] [ f "w" (TVar "T") Required ] ])
                      [ "two types the model would give one name, 'r_box__int': Box<int> and box<int>" ]

                  refusedAs
                      "two tuples of enums"
                      { vocabulary
                            [ kind
                                  "K"
                                  [ f "a" (TTuple [ TEnum "FooEBar"; TInt ]) Required
                                    f "b" (TTuple [ TEnum "Foo"; TEnum "BarInt" ]) Required ] ]
                            []
                            [] with
                          Enums = enumsOf [ "FooEBar", [ "a" ]; "Foo", [ "a" ]; "BarInt", [ "a" ] ] }
                      [ "two types the model would give one name, 'tp2_e_foo_e_bar_int': tuple<FooEBar, int> and tuple<Foo, BarInt>" ]

                  let picks =
                      let row = LayoutIdl.layout.Unions |> List.find (fun u -> u.Name = "RowIdentity")

                      // Each laid out on its own object: the kind's, and a record's.
                      { LayoutIdl.layout with
                          Kinds =
                              [ kind
                                    "K"
                                    [ f "a" (TUnion("RowIdentity", [])) Required
                                      f "h" (TRecord("H", [])) Required ] ]
                          Unions = LayoutIdl.layout.Unions @ [ { row with Name = "rowIdentity" } ]
                          Records =
                              LayoutIdl.layout.Records
                              @ [ record "H" [] [ f "b" (TUnion("rowIdentity", [])) Required ] ] }

                  refusedAs
                      "two presence-keyed unions"
                      picks
                      [ "two types the model would give one name, 'pk_u_row_identity': union 'RowIdentity''s pick and union 'rowIdentity''s pick" ]

                  // Constructors. `Gu<int>`'s case `X` beside a union `gu` whose case is tagged `int__X`.
                  refusedAs
                      "two constructors"
                      (vocabulary
                          [ kind "K" [ f "a" (TUnion("Gu", [ TInt ])) Required; f "b" (TUnion("gu", [])) Required ] ]
                          [ union "Gu" [ "T" ] [ case "X" [ f "v" (TVar "T") Required ] ]
                            union "gu" [] [ case "int__X" [ f "w" TStr Required ] ] ]
                          [])
                      [ "two constructors the model would give one name, 'C__u_gu__int__X'"
                        "Gu<int>, constructor 'X'"
                        "gu, constructor 'int__X'" ]

                  // Definitions. Each member's lookup lemma: a case `L` holding `a__b` beside a case `L__a`
                  // holding `b`.
                  refusedAs
                      "two members' definitions"
                      (vocabulary
                          [ kind "K" [ f "t" (TUnion("T", [])) Required ] ]
                          [ union "T" [] [ case "L" [ f "a__b" TInt Required ]; case "L__a" [ f "b" TInt Required ] ] ]
                          [])
                      [ "two definitions the model would give one name, 'lk_u_t__L__a__b'"
                        "T, constructor 'L', member 'a__b'"
                        "T, constructor 'L__a', member 'b'" ]

                  // Named apart: an enum's constructors (its word holds no `__`), the string-borne scalars,
                  // and every definition the two modules state over a vocabulary of each kind.
                  let apart =
                      { vocabulary
                            [ kind
                                  "K"
                                  [ f "a" (TEnum "A") Required
                                    f "b" (TEnum "A_b") Required
                                    f "c" TTimeOfDay Required
                                    f "d" TDuration Required
                                    f "e" (TRecord("Items", [ TInt ])) Required
                                    f "g" (TList(TRecord("Items", [ TInt ]))) Required ] ]
                            []
                            [ record "Items" [ "T" ] [ f "v" (TVar "T") Required ] ] with
                          Enums = enumsOf [ "A", [ "b__c" ]; "A_b", [ "c" ] ] }

                  let topLevel (text: string) =
                      text.Split('\n')
                      |> Array.choose (fun l ->
                          let word (s: string) =
                              s.Split([| ' '; ':'; '(' |], System.StringSplitOptions.RemoveEmptyEntries)
                              |> Array.tryHead

                          if l.StartsWith "let rec " then
                              word (l.Substring 8)
                          elif l.StartsWith "let " then
                              word (l.Substring 4)
                          elif l.StartsWith "type " then
                              word (l.Substring 5)
                          elif l.StartsWith "and " then
                              word (l.Substring 4)
                          elif l.StartsWith "val " then
                              word (l.Substring 4)
                          elif l.StartsWith "  | " && not (l.Contains "->") then
                              word (l.Substring 4)
                          else
                              None)
                      |> List.ofArray

                  for label, idl in
                      [ "the named-apart vocabulary", apart
                        "the parameterised-record vocabulary", ParameterisedRecordsIdl.idl
                        "the layout vocabulary", LayoutIdl.layout ] do
                      let tags = tagsOf idl
                      let model = emitted (FStarTarget.vocabularyModule "M" idl tags)
                      let proofs = emitted (FStarTarget.proofsModule "P" "M" idl tags)

                      for text in [ model; proofs ] do
                          let names = topLevel text
                          Expect.isNonEmpty names (label + ": definitions read")

                          Expect.equal
                              (names |> List.countBy id |> List.filter (fun (_, n) -> n > 1))
                              []
                              (label + ": no top-level name twice")

                  let text = modelOf apart

                  for name in
                      [ "C__e_a__b__c"
                        "C__e_a_0b__c"
                        "t_time_of_day"
                        "t_duration"
                        "C__r_items__int__Mk" ] do
                      Expect.stringContains text name (name + " is stated"))

          testCase "an inline parameterised record's members are its holder's, at the holder's argument" (fun _ ->
              let text = modelOf ParameterisedRecordsIdl.idl

              for line in
                  [ // `Grid.paging: Paging<string>`: the kind's own members, the cursor a string.
                    "  | C__vkind__Grid : cursor:(option (string)) -> page_size:(num) -> series:(option (r_series__flt num flt)) -> sort:(option (r_entry__str__e_direction num flt)) -> title:(option (r_labelled__str num flt)) -> vkind num flt"
                    // `Binding<T>.Local.payload: LocalState<T>`: beside the case's tag, at each
                    // instantiation of the union.
                    "  | C__u_binding__flt__Local : initial:(flt) -> state_key:(string) -> u_binding__flt num flt"
                    "  | C__u_binding__int__Local : initial:(num) -> state_key:(string) -> u_binding__int num flt" ] do
                  Expect.stringContains text (line + "\n") line

              for inlined in [ "r_paging"; "r_local_state" ] do
                  Expect.isFalse (text.Contains inlined) (sprintf "an inline record declares no type (%s)" inlined))

          testCase "a declared default that is a generic record's literal is the instance's constructor" (fun _ ->
              // `Outline.trail: Tagged<string>`, omitted at `{ tag = ""; items = [] }`.
              let text = modelOf ParameterisedRecordsIdl.derived

              Expect.stringContains
                  text
                  "(if f0 = (C__r_tagged__str__Mk ([]) None (\"\")) then"
                  "the encoder compares against the instance's own value"

              Expect.stringContains
                  text
                  "(match get_prop \"trail\" el with | Error _ -> Ok (C__r_tagged__str__Mk ([]) None (\"\"))"
                  "and the decoder restores it")

          testCase
              "an omit-at-default member at a type parameter is read at each instantiation's argument, as a union case's is"
              (fun _ ->
                  // The model is monomorphic, so the member's type is closed by the time its default is
                  // asked for: admitted where the literal is a value of the argument, refused in the
                  // admissibility rule's own words where it is not. (The F# and TypeScript backends
                  // refuse the member at every instantiation: their record is generic.) The declaration
                  // check refuses the second vocabulary itself, at the declaration; the model's refusal
                  // is what a caller handing it an unchecked value meets.
                  let members = [ f "v" (TVar "T") (OmitDefault(VStr "")); f "w" TStr Required ]

                  let asRecord (arg: IdlType) =
                      vocabulary
                          [ kind "K" [ f "o" (TRecord("Opt", [ arg ])) Required ] ]
                          []
                          [ record "Opt" [ "T" ] members ]

                  let asUnion (arg: IdlType) =
                      vocabulary
                          [ kind "K" [ f "o" (TUnion("Opt", [ arg ])) Required ] ]
                          [ union "Opt" [ "T" ] [ case "Some" members ] ]
                          []

                  for what, declare in [ "record", asRecord; "union", asUnion ] do
                      Expect.isEmpty (Declare.errors (declare TStr)) (what + ": well-formed at a string")

                      Expect.isNonEmpty
                          (Declare.errors (declare TInt))
                          (what + ": refused at its declaration at an integer")

                      Expect.isNone
                          (FStarTarget.partition (declare TStr) |> List.find (fun v -> v.Tag = "K")).Refusal
                          (sprintf "%s: modelled at a string, where the default is a value" what)

                      Expect.stringContains
                          (refusalOf (declare TInt) "K")
                          "no default literal for an IDL value of type TInt"
                          (sprintf "%s: refused at an integer, where it is not" what)

                  Expect.stringContains
                      (modelOf (asRecord TStr))
                      "  | C__r_opt__str__Mk : v:(string) -> w:(string) -> r_opt__str num flt\n"
                      "the instance holds the member at the argument")

          testCase "a union that holds itself at a record's argument is one recursive family" (fun _ ->
              // `Tree.Branch.kids: Kid<Tree> list`, `spare: Tagged<Tree>`: the record's instance at
              // the union is a type of the union's own mutual group.
              let text = modelOf ParameterisedRecordsIdl.derived

              for line in
                  [ "  | C__u_tree__Branch : kids:(list (r_kid__u_tree num flt)) -> spare:(option (r_tagged__u_tree num flt)) -> u_tree num flt"
                    "  | C__r_kid__u_tree__Mk : child:(u_tree num flt) -> name:(string) -> r_kid__u_tree num flt" ] do
                  Expect.stringContains text (line + "\n") line)

          testCase "a declaration whose recursion grows its type argument is refused by name" (fun _ ->
              // No finite set of instances, so no model. The reach stops at a fixed depth and names
              // the declaration; a kind that does not reach it is modelled as it always was.
              let record' =
                  vocabulary
                      [ kind "K" [ f "grown" (TRecord("Grow", [ TStr ])) Required ]
                        kind "Plain" [ f "label" TStr Required ] ]
                      []
                      [ record "Grow" [ "T" ] (growing (fun t -> TRecord("Grow", [ t ]))) ]

              Expect.isEmpty (Declare.errors record') "the declaration check admits it"

              Expect.stringContains
                  (refusalOf record' "K")
                  "the generic record 'Grow', which has no finite set of instantiations: Grow<'T> reaches Grow<list<'T>>"
                  "by the record's name"

              Expect.isNone
                  (FStarTarget.partition record' |> List.find (fun v -> v.Tag = "Plain")).Refusal
                  "a kind that does not reach it is modelled"

              // The union's same recursion: one analysis answers both.
              let union' =
                  vocabulary
                      [ kind "K" [ f "grown" (TUnion("Grow", [ TStr ])) Required ] ]
                      [ union "Grow" [ "T" ] [ case "More" (growing (fun t -> TUnion("Grow", [ t ]))) ] ]
                      []

              Expect.isEmpty (Declare.errors union') "the declaration check admits it"

              Expect.stringContains
                  (refusalOf union' "K")
                  "the generic union 'Grow', which has no finite set of instantiations: Grow<'T> reaches Grow<list<'T>>"
                  "by the union's name"

              // Every backend resolves a vocabulary through the same reach, and the reach is finite:
              // the two codecs emit the growing declaration (each host recurses polymorphically).
              for what, idl in [ "record", record'; "union", union' ] do
                  Expect.isOk (Gen.fsharpModule "Probe" idl [ "K" ]) (sprintf "the F# codec emits the growing %s" what)
                  Expect.isOk (Gen.typescriptModule idl [ "K" ]) (sprintf "and the TypeScript codec (%s)" what)) ]

// ---------------------------------------------------------------------------
// The decode vectors. The generated round trip speaks only of what the model's encoder writes; what
// holds the model's READING of a parameterised record to the interpreter's is the committed facts
// module (`proofs/VocabularyVectors.fst`, held to this text by `IdlThreeHostTests` and checked by the
// proof leg), where the normaliser evaluates each model's `dec_node` on documents the interpreter
// wrote and on documents it refuses.
// ---------------------------------------------------------------------------

/// One certified model's vectors: the vocabulary, the model's abbreviation, the constructor a node
/// of a kind is (the default envelope nests the kind, the flat one merges it), the documents read
/// and the documents refused.
let private certified: (string * Idl * string * (string -> string) * string list * (string * string) list) list =
    [ "default",
      ParameterisedRecordsIdl.derived,
      "P",
      (fun tag -> "(P.C__vkind__" + tag + " "),
      ParameterisedRecordsIdl.modelDocuments,
      ParameterisedRecordsIdl.refused
      "flat",
      ParameterisedRecordsIdl.derivedFlat,
      "PF",
      (fun tag -> "(PF.C__node__" + tag + " "),
      ParameterisedRecordsIdl.flatModelDocuments (),
      ParameterisedRecordsIdl.flatRefused ]

[<Tests>]
let vectors =
    testList
        "Phase 732 — the F* decode vectors over a parameterised record"
        [ testCase "each document the interpreter wrote is read at the interpreter's value" (fun _ ->
              let facts = emitted (CertificationVectors.vectorsText ())

              let definition (name: string) : string =
                  let head = sprintf "let %s : " name

                  match facts.IndexOf(head, System.StringComparison.Ordinal) with
                  | -1 -> failtestf "the vectors module defines no %s" name
                  | at -> facts.Substring(at, facts.IndexOf("\n\n", at, System.StringComparison.Ordinal) - at)

              Expect.equal
                  (List.length ParameterisedRecordsIdl.modelDocuments)
                  6
                  "one document per kind of the fixture, and the derived corpus"

              // Under the flat configuration the same six, as the interpreter writes them there:
              // the first three are the bytes the fixture pins.
              Expect.equal
                  (ParameterisedRecordsIdl.flatModelDocuments () |> List.truncate 3)
                  ParameterisedRecordsIdl.flatBytes
                  "the fixture's pinned flat bytes"

              Expect.equal (List.length (ParameterisedRecordsIdl.flatModelDocuments ())) 6 "and six in all"

              for name, idl, prefix, nodeOf, documents, _ in certified do
                  let lower = prefix.ToLowerInvariant()

                  // The kind is read off the interpreter's own value, never off the generated text.
                  let tags =
                      documents
                      |> List.mapi (fun i doc ->
                          match Decode.decodeDetailed idl doc with
                          | Ok(VNode(_, tag, _))
                          | Ok(VNodeEnv(_, _, tag, _)) -> tag
                          | other -> failtestf "%s: the interpreter does not read document %d: %A" name i other)

                  Expect.equal (set tags) (set (tagsOf idl)) (sprintf "%s: every kind is read by some document" name)

                  tags
                  |> List.iteri (fun i tag ->
                      Expect.stringContains
                          (definition (sprintf "%s_dv%d" lower i))
                          (nodeOf tag)
                          (sprintf "%s: document %d is a value of its kind's constructor" name i)

                      Expect.stringContains
                          facts
                          (sprintf "  assert_norm (%s.dec_node %s_de%d == Ok %s_dv%d)" prefix lower i lower i)
                          (sprintf "%s: and the decoder is evaluated on document %d" name i))

              // The documents are not vacuous for the phase: between them the values read hold a
              // record at every instantiation the two models declare.
              let read (prefix: string) (count: int) =
                  [ 0 .. count - 1 ]
                  |> List.map (fun i -> definition (sprintf "%s_dv%d" prefix i))
                  |> String.concat "\n"

              let declared (idl: Idl) =
                  (modelOf idl).Split('\n')
                  |> Array.choose (fun l ->
                      let l = l.Trim()

                      if l.StartsWith("| C__r_", System.StringComparison.Ordinal) then
                          Some(l.Substring(2, l.IndexOf(' ', 2) - 2))
                      else
                          None)
                  |> List.ofArray

              for prefix, lower, idl, count in
                  [ "P", "p", ParameterisedRecordsIdl.derived, 6
                    "PF", "pf", ParameterisedRecordsIdl.derivedFlat, 6 ] do
                  let values = read lower count
                  let instances = declared idl
                  Expect.isNonEmpty instances "the model declares record instantiations"

                  for ctor in instances do
                      Expect.stringContains
                          values
                          ("(" + prefix + "." + ctor + " ")
                          (ctor + " is read by some document"))

          testCase "each document the interpreter refuses is refused by the model" (fun _ ->
              let facts = emitted (CertificationVectors.vectorsText ())

              Expect.equal
                  (List.length ParameterisedRecordsIdl.refused)
                  13
                  "the default configuration's refused documents"

              Expect.equal (List.length ParameterisedRecordsIdl.flatRefused) 8 "the flat configuration's"

              for name, idl, prefix, _, _, refused in certified do
                  refused
                  |> List.iteri (fun i (what, doc) ->
                      Expect.isError
                          (Decode.decodeDetailed idl doc)
                          (sprintf "%s: the interpreter refuses %s" name what)

                      Expect.stringContains
                          facts
                          (sprintf "  assert_norm (Error? (%s.dec_node %s_dr%d))" prefix (prefix.ToLowerInvariant()) i)
                          (sprintf "%s: the decoder is evaluated on the document (%s)" name what))) ]
