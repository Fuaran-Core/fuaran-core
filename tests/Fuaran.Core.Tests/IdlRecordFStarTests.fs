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
                    "  | C__r_entry__str_e_direction__Mk : key:(string) -> value:(option (e_direction)) -> r_entry__str_e_direction num flt"
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

          testCase "an inline parameterised record's members are its holder's, at the holder's argument" (fun _ ->
              let text = modelOf ParameterisedRecordsIdl.idl

              for line in
                  [ // `Grid.paging: Paging<string>`: the kind's own members, the cursor a string.
                    "  | C__vkind__Grid : cursor:(option (string)) -> page_size:(num) -> series:(option (r_series__flt num flt)) -> sort:(option (r_entry__str_e_direction num flt)) -> title:(option (r_labelled__str num flt)) -> vkind num flt"
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
