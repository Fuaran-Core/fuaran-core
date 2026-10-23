module Fuaran.Core.Tests.IdlLayoutTests

// ---------------------------------------------------------------------------
// Phase 474 — the INLINE declarations: a flattened sub-record, a record-payload union case and a
// presence-keyed union (DECISIONS.md D220). The evidence, in five groups:
//   1. DECLARATION — `layout` and its flat twin are well-formed, the misplacements and malformed
//      rules are refused by name, and `idl.json` carries the two attributes and reads them back.
//   2. THE WIRE DOES NOT MOVE — every corpus document is read and re-written byte-identically by
//      the interpreter and by the generated module over `layout`, and by the interpreter over the
//      flat twin; under DECLARED key order the two vocabularies write one key sequence.
//   3. THE REFUSALS — the both-present refusal, a synthesised member absent or at another value,
//      and a required union with no key, each with its code and path, the generated decoders (the
//      short-circuiting and the collecting form) and the interpreter answering alike.
//   4. THE OTHER BACKENDS — the JSON schema states the inline members on the holding object; the
//      TypeScript backend, the F* target and the codec-only module refuse by name.
//   5. THE CLASSIFIER — an inline layout that moves is a wire break.
// ---------------------------------------------------------------------------

open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

module Gen' = Fuaran.Core.Tests.LayoutGenerated

let private parse (s: string) : JVal =
    match Json.parse s with
    | Ok j -> j
    | Error e -> failtestf "test input is not JSON: %s" e

/// A path as `DecodePath.render` writes it, from its keys.
let private at (keys: string list) : string =
    "$" + (keys |> List.map (fun k -> "[\"" + k + "\"]") |> String.concat "")

let private shape (e: DecodeError) : string * string =
    DecodeError.codeName e.Code, DecodePath.render e.Path

/// The interpreter's re-encoding of a document under `idl`.
let private interpreted (idl: Idl) (doc: string) : string =
    match Decode.decodeDetailed idl doc with
    | Error e -> failtestf "the interpreter refused %s: %s" doc (DecodeError.render e)
    | Ok v ->
        match Encode.encode idl v with
        | Ok s -> s
        | Error m -> failtestf "the interpreter could not re-encode %s: %s" doc m

/// The interpreter's refusal of a document under `idl`, as code and path.
let private interpreterRefusal (idl: Idl) (doc: string) : string * string =
    match Decode.decodeDetailed idl doc with
    | Error e -> shape e
    | Ok _ -> failtestf "the interpreter accepted %s" doc

let private emitted (r: Result<string, CodegenError>) : string =
    match r with
    | Ok s -> s
    | Error e -> failtestf "codegen refused: %A" e

let private refusalOf (r: Result<string, CodegenError>) : string =
    match r with
    | Ok _ -> failtest "expected the backend to refuse"
    | Error(CodegenError.UnsupportedConstruct(what, _, _)) -> what
    | Error e -> sprintf "%A" e

/// `layout` with one declaration replaced.
let private withUnion (name: string) (f: IdlUnion -> IdlUnion) : Idl =
    { LayoutIdl.layout with
        Unions = LayoutIdl.layout.Unions |> List.map (fun u -> if u.Name = name then f u else u) }

let private faults (idl: Idl) : string list =
    Declare.layoutErrors idl |> List.map VocabularyFault.describe

let private expectFault (idl: Idl) (fragment: string) (because: string) =
    let fs = faults idl

    Expect.isTrue (fs |> List.exists (fun f -> f.Contains fragment)) (sprintf "%s — faults: %A" because fs)

[<Tests>]
let declarationTests =
    testList
        "Phase 474 inline declarations"
        [ testCase "the layout vocabulary and its flat twin are well-formed" (fun _ ->
              Expect.isEmpty (Declare.errors LayoutIdl.layout) "layout"
              Expect.isEmpty (Declare.errors LayoutIdl.flat) "flat")

          testCase "idl.json writes the two attributes, omits them at their defaults, and reads them back" (fun _ ->
              let text = Artifact.render LayoutIdl.layout
              Expect.stringContains text "\"inline\": true" "an inline record says so"
              Expect.stringContains text "\"byPresence\"" "a presence-keyed union carries its rules"
              Expect.stringContains text "\"synthesised\"" "a synthesised member is declared"

              match Artifact.parse text with
              | Error m -> failtestf "the artifact did not read back: %s" m
              | Ok back -> Expect.equal (Artifact.render back) text "the artifact round-trips"

              let flatText = Artifact.render LayoutIdl.flat
              Expect.isFalse (flatText.Contains "\"inline\"") "no inline record, no key"
              Expect.isFalse (flatText.Contains "\"byPresence\"") "no presence-keyed union, no key")

          testCase "an inline declaration nested in another type is refused" (fun _ ->
              let idl =
                  { LayoutIdl.layout with
                      Kinds =
                          LayoutIdl.layout.Kinds
                          @ [ { Tag = "Pages"
                                Category = "layout"
                                Annotations = Annotations.Empty
                                Fields =
                                  [ { Name = "pages"
                                      Type = TList(TRecord "Paging")
                                      Opt = Required
                                      Aliases = []
                                      Presence = None
                                      Annotations = Annotations.Empty } ] } ] }

              expectFault idl "inline record 'Paging' is held inside another type" "a list of an inline record")

          testCase "an optional inline record and an inline record in an inline record are refused" (fun _ ->
              let optionalPaging =
                  { LayoutIdl.layout with
                      Kinds =
                          LayoutIdl.layout.Kinds
                          |> List.map (fun k ->
                              { k with
                                  Fields =
                                      k.Fields
                                      |> List.map (fun f ->
                                          if f.Name = "paging" then { f with Opt = Optional } else f) }) }

              expectFault optionalPaging "so it is Required" "an inline record is always there"

              let nested =
                  { LayoutIdl.layout with
                      Records =
                          LayoutIdl.layout.Records
                          |> List.map (fun r ->
                              if r.Name = "TextPayload" then
                                  { r with
                                      Fields =
                                          r.Fields
                                          @ [ { Name = "paging"
                                                Type = TRecord "Paging"
                                                Opt = Required
                                                Aliases = []
                                                Presence = None
                                                Annotations = Annotations.Empty } ] }
                              else
                                  r) }

              expectFault nested "cannot stand here" "an inline record holds no inline declaration")

          testCase "a member spelled twice once the layout is expanded is refused" (fun _ ->
              let clash =
                  { LayoutIdl.layout with
                      Kinds =
                          LayoutIdl.layout.Kinds
                          |> List.map (fun k ->
                              if k.Tag = "Grid" then
                                  { k with
                                      Fields =
                                          k.Fields
                                          @ [ { Name = "pageSize"
                                                Type = TInt
                                                Opt = Optional
                                                Aliases = []
                                                Presence = None
                                                Annotations = Annotations.Empty } ] }
                              else
                                  k) }

              expectFault clash "the member 'pageSize' is on its object twice" "an inline member collides")

          testCase "a presence-keyed union's malformed rules are refused" (fun _ ->
              let badKey =
                  withUnion "RowIdentity" (fun u ->
                      { u with
                          Discrimination =
                              UnionDiscrimination.ByPresence
                                  [ { Case = "RowKey"
                                      Key = "nope"
                                      Synthesised = [] }
                                    { Case = "RowKeyField"
                                      Key = "rowKeyField"
                                      Synthesised = [] } ] })

              expectFault badKey "the key 'nope' is not a Required member" "a key names the case's member"

              let missingRule =
                  withUnion "RowIdentity" (fun u ->
                      { u with
                          Discrimination =
                              UnionDiscrimination.ByPresence
                                  [ { Case = "RowKey"
                                      Key = "rowKey"
                                      Synthesised = [] } ] })

              expectFault missingRule "one rule per case" "every case has a rule"

              let badSynth =
                  withUnion "GridRows" (fun u ->
                      { u with
                          Discrimination =
                              UnionDiscrimination.ByPresence
                                  [ { Case = "Source"
                                      Key = "source"
                                      Synthesised = [] }
                                    { Case = "StaticRows"
                                      Key = "staticRows"
                                      Synthesised = [ ("source", VStr "x") ] } ] })

              expectFault badSynth "the synthesised value does not fit member 'source'" "a synthesised value is typed"

              let tagRule =
                  withUnion "RowIdentity" (fun u ->
                      { u with
                          TagRule = UnionTagRule.LowerFirst })

              expectFault tagRule "has no tag, so no tag rule" "a presence-keyed union has no tag") ]

/// Each corpus document, read and written by `read`, must come back byte for byte.
let private roundTrips (name: string) (read: string -> string) =
    for doc in LayoutIdl.corpus do
        Expect.equal (read doc) doc (sprintf "%s re-writes %s unchanged" name doc)

[<Tests>]
let wireTests =
    testList
        "Phase 474 inline declarations keep the wire"
        [ testCase "the interpreter over the layout and over its flat twin re-write every document unchanged" (fun _ ->
              roundTrips "the layout interpreter" (interpreted LayoutIdl.layout)
              roundTrips "the flat interpreter" (interpreted LayoutIdl.flat))

          testCase "the generated module re-writes every document unchanged" (fun _ ->
              roundTrips "the generated module" (fun doc ->
                  match Gen'.decodeNode doc with
                  | Ok n -> Gen'.encodeNode n
                  | Error e -> failtestf "the generated decoder refused %s: %s" doc (DecodeError.render e)))

          testCase "the collecting decoders agree with the short-circuiting ones on every document" (fun _ ->
              for doc in LayoutIdl.corpus do
                  match Gen'.decodeNodeAll doc, Gen'.decodeNode doc with
                  | Ok a, Ok b -> Expect.equal (Gen'.encodeNode a) (Gen'.encodeNode b) doc
                  | a, b -> failtestf "the two forms disagree on %s: %A / %A" doc a b)

          testCase "the generated host shape is the reshaped one" (fun _ ->
              match Gen'.decodeNode (List.item 3 LayoutIdl.corpus) with
              | Ok { Kind = Gen'.NodeKind.Grid g } ->
                  Expect.equal g.Paging.PageSize (Some 3) "the sub-record holds the flat member"

                  match g.Rows with
                  | Gen'.GridRows.StaticRows t ->
                      Expect.isEmpty t.Headers "the static case, not the placeholder source"
                  | other -> failtestf "expected the static rows, got %A" other
              | other -> failtestf "expected a grid, got %A" other

              match Gen'.decodeNode (List.item 4 LayoutIdl.corpus) with
              | Ok { Kind = Gen'.NodeKind.Form { Field = Gen'.Field.Text p } } ->
                  Expect.equal p.Placeholder (Some "type here") "the record payload holds the positional member"
              | other -> failtestf "expected a text field, got %A" other)

          testCase
              "every sampled layout value is read back alike by the interpreter, the generated module and the flat twin"
              (fun _ ->
                  match Sample.trySampleNodes LayoutIdl.layout [ "Grid"; "Form" ] 474 60 with
                  | Error f -> failtestf "the sampler refused the layout vocabulary: %A" f
                  | Ok values ->
                      for v in values do
                          match Encode.encode LayoutIdl.layout v with
                          | Error m -> failtestf "a sampled value did not encode: %s" m
                          | Ok doc ->
                              Expect.equal (interpreted LayoutIdl.layout doc) doc "the interpreter re-writes it"
                              Expect.equal (interpreted LayoutIdl.flat doc) doc "the flat twin reads the same wire"

                              match Gen'.decodeNode doc with
                              | Ok n -> Expect.equal (Gen'.encodeNode n) doc "the generated module re-writes it"
                              | Error e -> failtestf "the generated decoder refused %s: %s" doc (DecodeError.render e))

          testCase "under declared key order the layout writes the flat twin's key sequence" (fun _ ->
              for doc in LayoutIdl.corpus do
                  Expect.equal
                      (interpreted LayoutIdl.layoutDeclared doc)
                      (interpreted LayoutIdl.flatDeclared doc)
                      (sprintf "one key sequence for %s" doc)) ]

/// A grid document with the members `extra` beside the required columns, in sorted form.
let private grid (members: string) : string =
    sprintf """{"id":"g","kind":{"$type":"Grid","columns":[]%s}}""" members

/// The refusal `doc` meets, the same from the interpreter and both generated forms.
let private refusedAlike (doc: string) (expected: string * string) =
    Expect.equal (interpreterRefusal LayoutIdl.layout doc) expected (sprintf "the interpreter on %s" doc)

    match Gen'.decodeNode doc with
    | Error e -> Expect.equal (shape e) expected (sprintf "the generated decoder on %s" doc)
    | Ok _ -> failtestf "the generated decoder accepted %s" doc

    match Gen'.decodeNodeAll doc with
    | Error(e :: _) -> Expect.equal (shape e) expected (sprintf "the collecting decoder's first defect on %s" doc)
    | other -> failtestf "the collecting decoder did not refuse %s: %A" doc other

[<Tests>]
let refusalTests =
    testList
        "Phase 474 inline declarations refuse alike"
        [ testCase "two cases' keys present: the both-present refusal at the second key" (fun _ ->
              refusedAlike
                  (grid ""","rowKey":"<closure>","rowKeyField":"id","source":{"$type":"State","key":"k"}""")
                  ("OutOfRange", at [ "kind"; "rowKeyField" ]))

          testCase "a static grid whose source is not the placeholder is refused at the source" (fun _ ->
              refusedAlike
                  (grid ""","source":{"$type":"State","key":"k"},"staticRows":{"headers":[],"rows":[]}""")
                  ("OutOfRange", at [ "kind"; "source" ]))

          testCase "a static grid without the synthesised source is refused naming it" (fun _ ->
              refusedAlike
                  (grid ""","staticRows":{"headers":[],"rows":[]}""")
                  ("MissingField", at [ "kind"; "source" ]))

          testCase "a required presence-keyed union with no key present is refused at its first key" (fun _ ->
              refusedAlike (grid "") ("MissingField", at [ "kind"; "source" ]))

          testCase "a defect inside an inline member is at that member of the holding object" (fun _ ->
              refusedAlike
                  (grid ""","pageSize":"ten","source":{"$type":"State","key":"k"}""")
                  ("WrongKind", at [ "kind"; "pageSize" ])

              refusedAlike
                  """{"id":"f","kind":{"$type":"Form","field":{"$type":"Text"}}}"""
                  ("MissingField", at [ "kind"; "field"; "value" ]))

          testCase "the synthesised member is compared canonically, so its key order is free" (fun _ ->
              let doc =
                  grid ""","source":{"value":[],"$type":"Static"},"staticRows":{"headers":[],"rows":[]}"""

              Expect.isOk (Gen'.decodeNode doc) "the generated decoder"
              Expect.isOk (Decode.decodeDetailed LayoutIdl.layout doc) "the interpreter")

          testCase "the collecting decoder reports every defect, the synthesised one after the case's own" (fun _ ->
              let doc =
                  grid ""","pageSize":"x","source":{"$type":"State","key":"k"},"staticRows":{"headers":1,"rows":[]}"""

              match Gen'.decodeNodeAll doc with
              | Error es ->
                  Expect.equal
                      (es |> List.map shape)
                      [ "WrongKind", at [ "kind"; "pageSize" ]
                        "WrongKind", at [ "kind"; "staticRows"; "headers" ]
                        "OutOfRange", at [ "kind"; "source" ] ]
                      "in field declaration order"
              | Ok _ -> failtest "expected the collecting decoder to refuse") ]

[<Tests>]
let backendTests =
    testList
        "Phase 474 inline declarations in the other backends"
        [ testCase "the committed LayoutGenerated.fs is what the generator emits (byte for byte)" (fun _ ->
              let generated = emitted (LayoutIdl.generate ())
              let path = Snapshots.repoFile LayoutIdl.generatedFile

              Approval.write Approval.Regen Approval.Regenerated.Layout path generated
              |> ignore

              Expect.equal
                  generated
                  (File.ReadAllText path)
                  "regenerate with: FUARAN_REGEN=LayoutGenerated, running IdlLayoutTests")

          testCase "the JSON schema states the inline members on the holding object" (fun _ ->
              let schema = emitted (Gen.jsonSchema LayoutIdl.layout)

              match parse schema with
              | JObj root ->
                  let defs =
                      match root |> List.tryFind (fun (k, _) -> k = "$defs") with
                      | Some(_, JObj ds) -> ds
                      | _ -> failtest "no $defs"

                  let names = defs |> List.map fst

                  for inlined in [ "Paging"; "TextPayload"; "RowIdentity"; "GridRows" ] do
                      Expect.isFalse (List.contains inlined names) (inlined + " has no definition of its own")

                  let memberOf (def: string) (key: string) =
                      match defs |> List.tryFind (fun (k, _) -> k = def) with
                      | Some(_, JObj d) ->
                          match d |> List.tryFind (fun (k, _) -> k = key) with
                          | Some(_, v) -> v
                          | None -> failtestf "%s has no %s" def key
                      | _ -> failtestf "no definition %s" def

                  let keys =
                      function
                      | JObj ps -> ps |> List.map fst
                      | JArr xs ->
                          xs
                          |> List.choose (function
                              | JStr s -> Some s
                              | _ -> None)
                      | _ -> []

                  Expect.equal
                      (keys (memberOf "Grid" "properties"))
                      [ "$type"
                        "columns"
                        "pageSize"
                        "pageStateKey"
                        "rowKey"
                        "rowKeyField"
                        "source"
                        "staticRows"
                        "caption" ]
                      "every member on the grid, in place"

                  Expect.equal
                      (keys (memberOf "Grid" "required"))
                      [ "$type"; "columns"; "source" ]
                      "a member every case writes is required, as the flat grid requires it"
              | _ -> failtest "the schema is not an object")

          testCase "the TypeScript backend refuses a vocabulary that lays a declaration out inline" (fun _ ->
              let tags = LayoutIdl.layout.Kinds |> List.map _.Tag

              for name, r in
                  [ "module", Gen.typescriptModule LayoutIdl.layout tags
                    "derived module", Gen.typescriptModuleDerived [] LayoutIdl.layout tags
                    "declarations", Gen.typescriptDeclarations LayoutIdl.layout tags
                    "derived declarations", Gen.typescriptDeclarationsDerived [] LayoutIdl.layout tags
                    "value", Gen.typescriptValue LayoutIdl.layout TStr (VStr "x") ] do
                  Expect.stringContains (refusalOf r) "the " (name + " names the declaration")

              Expect.isOk
                  (Gen.typescriptModule LayoutIdl.flat (LayoutIdl.flat.Kinds |> List.map _.Tag))
                  "the flat twin")

          testCase "the F* target refuses a kind reaching an inline declaration, by name" (fun _ ->
              let verdicts = FStarTarget.partition LayoutIdl.layout

              for v in verdicts do
                  match v.Refusal with
                  | Some why ->
                      Expect.isTrue
                          (why.Contains "inline record" || why.Contains "presence-keyed union")
                          (sprintf "%s: %s" v.Tag why)
                  | None -> failtestf "the F* target modelled %s" v.Tag)

          testCase "the codec-only module refuses an inline declaration, by name" (fun _ ->
              let request: CodecOnly =
                  { Roots = [ "Field" ]
                    HostTypes = Map.ofList [ "Field", "Fuaran.Core.Tests.LayoutGenerated.Field"; "TextPayload", "T" ] }

              Expect.stringContains
                  (refusalOf (Gen.fsharpCodecModule GenSupport.Empty request "M" LayoutIdl.layout))
                  "the inline record 'TextPayload'"
                  "named") ]

[<Tests>]
let classifierTests =
    testList
        "Phase 474 inline declarations in the stability classifier"
        [ testCase "a record turned inline and a union turned presence-keyed are wire breaks" (fun _ ->
              let before =
                  { LayoutIdl.layout with
                      Records =
                          LayoutIdl.layout.Records
                          |> List.map (fun r -> if r.Name = "Paging" then { r with Inline = false } else r)
                      Unions =
                          LayoutIdl.layout.Unions
                          |> List.map (fun u ->
                              if u.Name = "RowIdentity" then
                                  { u with
                                      Discrimination = UnionDiscrimination.Tagged }
                              else
                                  u) }

              match Diff.ofValues (before, None) (LayoutIdl.layout, None) with
              | Error m -> failtestf "%s" m
              | Ok v ->
                  let rows = v.Changes |> List.map (fun c -> c.Change, c.Severity)

                  Expect.contains
                      rows
                      (TypeLayoutChanged("record", "Paging", "", "inline"), Severity.BreakingWire)
                      "a record turned inline"

                  Expect.exists
                      rows
                      (fun (c, s) ->
                          match c with
                          | TypeLayoutChanged("union", "RowIdentity", "", after) ->
                              after <> "" && s = Severity.BreakingWire
                          | _ -> false)
                      "a union turned presence-keyed") ]
