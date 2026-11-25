module Fuaran.Core.Tests.IdlRecordFSharpTests

// ---------------------------------------------------------------------------
// Phase 732 — the F# backend emits a parameterised record.
//
// This file holds the F# backend to the interpreter over a parameterised record: what each entry
// emits for one (a generic record, its encoder and decoders taking one codec per parameter as a
// generic union's do, an inline one's members laid on its holder at the holder's argument), the
// refusals that stay, and the vocabulary-wide predicates the emitted module depends on.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

let private f (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Aliases = []
      Keyed = false
      Presence = None
      Annotations = Annotations.Empty }

let private kind (tag: string) (fields: IdlField list) : IdlKind =
    { Tag = tag
      Category = "probe"
      Fields = fields
      Annotations = Annotations.Empty }

let private vocabulary (kinds: IdlKind list) : Idl =
    { Kinds = kinds
      Unions = []
      Enums = []
      Records = []
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      Harden = HardenPolicy.Undeclared
      Contracts = [] }

let private emitted (r: Result<string, CodegenError>) : string =
    match r with
    | Ok src -> src
    | Error e -> failtestf "the generator refused: %s" (CodegenError.describe e)

// ---------------------------------------------------------------------------
// The vocabulary-wide predicates: whether the vocabulary declares a hosted slot (its helpers are
// emitted only then) and which declarations carry the message parameter. Each descended a list and
// a map and no tuple, set or keyed map, so a hosted slot or a message-carrying member held only
// inside one of those emitted a module naming a helper it did not define, or a type parameter its
// declaration did not bind.
// ---------------------------------------------------------------------------

let private hosted: IdlType =
    THosted
        { FSharp = "System.Guid"
          Encode = "(fun (g: System.Guid) -> JStr(g.ToString()))"
          Decode = "(fun (j: JVal) -> match j with JStr s -> Ok(System.Guid.Parse s) | _ -> Error \"guid\")"
          Wire = Some TStr
          Format = Some HostedFormat.Uuid
          Refusal = HostedRefusal.Sentence }

let private handler: IdlType =
    TFn
        { FSharp = "unit -> 'Msg"
          TypeScript = "() => unknown"
          Placeholder = "(fun () -> Unchecked.defaultof<_>)" }

/// Every position a type can hold another in, beside the list and the map the predicates always
/// descended: what it is, the type holding `inner` there, and whether the declaration check admits
/// a hosted slot or a function there (a set's element and a keyed map's key must be orderable).
let private holdings (inner: IdlType) : (string * IdlType * bool) list =
    [ "a tuple's component", TTuple [ inner; TInt ], true
      "a keyed map's value", TKeyedMap(TInt, inner), true
      "a set's element", TSet inner, false
      "a keyed map's key", TKeyedMap(inner, TInt), false ]

[<Tests>]
let predicateTests =
    testList
        "Phase 732 — the vocabulary-wide predicates descend every type"
        [ testCase "a hosted slot held in a tuple, a set or a keyed map has its helpers emitted" (fun _ ->
              for where, t, admitted in holdings hosted do
                  let idl = vocabulary [ kind "K" [ f "held" t Required ] ]

                  Expect.equal
                      (List.isEmpty (Declare.errors idl))
                      admitted
                      (sprintf "%s: whether the declaration check admits it" where)

                  let src = emitted (Gen.fsharpModule "Probe" idl [ "K" ])

                  Expect.stringContains src "dHosted " (sprintf "%s: the decoder reads the slot through dHosted" where)

                  Expect.stringContains src "let private dHosted " (sprintf "%s: and the module defines it" where)

                  Expect.stringContains
                      src
                      "let private dFormat "
                      (sprintf "%s: and the format check the slot declares" where))

          testCase
              "a function over the message held in a tuple, a set or a keyed map makes its holder generic"
              (fun _ ->
                  for where, t, admitted in holdings handler do
                      // `Plain` holds a node (a keyed map's value may be one), so it is generic when `K` is.
                      let idl =
                          vocabulary
                              [ kind "K" [ f "held" t Required ]
                                kind "Plain" [ f "children" (TKeyedMap(TInt, TNode)) Required ] ]

                      Expect.equal
                          (List.isEmpty (Declare.errors idl))
                          admitted
                          (sprintf "%s: whether the declaration check admits it" where)

                      let src = emitted (Gen.fsharpModule "Probe" idl [ "K"; "Plain" ])

                      Expect.stringContains src "KSpec<'Msg> =" (sprintf "%s: the holder binds the parameter" where)
                      Expect.stringContains src "Node<'Msg> =" (sprintf "%s: and so does the node" where)

                      Expect.stringContains
                          src
                          "PlainSpec<'Msg> ="
                          (sprintf "%s: and a kind holding a node as a keyed map's value" where)) ]

// ---------------------------------------------------------------------------
// The committed generated module over the fixture vocabulary ([[ParameterisedRecordsIdl]]): compiled
// by this project, held to the generator, and held to the interpreter's bytes in both directions.
// ---------------------------------------------------------------------------

module G = Fuaran.Core.Tests.ParameterisedRecordsGenerated

[<Tests>]
let generatedTests =
    testList
        "Phase 732 — the generated F# module over the parameterised records"
        [ testCase
              "the committed ParameterisedRecordsGenerated.fs is what the generator emits (byte for byte)"
              (fun _ ->
                  let generated = emitted (ParameterisedRecordsIdl.generate ())
                  let path = Snapshots.repoFile ParameterisedRecordsIdl.generatedFile

                  Approval.write Approval.Regen Approval.Regenerated.ParameterisedRecords path generated
                  |> ignore

                  Expect.equal
                      generated
                      (System.IO.File.ReadAllText path)
                      "regenerate with: FUARAN_REGEN=ParameterisedRecordsGenerated, running IdlRecordFSharpTests")

          testCase "the generated module reads each pinned document and re-writes its bytes" (fun _ ->
              for what, _, bytes in ParameterisedRecordsIdl.documents do
                  match G.decodeNode bytes with
                  | Ok node -> Expect.equal (G.encodeNode node) bytes (what + ": re-written unchanged")
                  | Error e ->
                      failtestf "%s: the generated decoder refused: %s" what (Fuaran.Core.DecodeError.render e)) ]

[<Tests>]
let flatGeneratedTests =
    testList
        "Phase 732 — the generated F# module in the second wire configuration"
        [ testCase
              "the committed ParameterisedRecordsFlatGenerated.fs is what the generator emits (byte for byte)"
              (fun _ ->
                  let generated = emitted (ParameterisedRecordsIdl.generateFlat ())
                  let path = Snapshots.repoFile ParameterisedRecordsIdl.flatGeneratedFile

                  Approval.write Approval.Regen Approval.Regenerated.ParameterisedRecordsFlat path generated
                  |> ignore

                  Expect.equal
                      generated
                      (System.IO.File.ReadAllText path)
                      "regenerate with: FUARAN_REGEN=ParameterisedRecordsFlatGenerated, running IdlRecordFSharpTests") ]
