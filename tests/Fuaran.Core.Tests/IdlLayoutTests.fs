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
//      TypeScript backend lays them out and the F* target models them (Phase 722), each refusing by
//      name what it cannot express; the codec-only module lays them out with the structural
//      module's emitter and refuses one only as a root (Phase 722, D285).
//   5. THE CLASSIFIER — an inline layout that moves is a wire break.
// ---------------------------------------------------------------------------

open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

module Gen' = Fuaran.Core.Tests.LayoutGenerated
module C = Fuaran.Core.Tests.LayoutCodecGenerated

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

let private kindTags (idl: Idl) : string list = idl.Kinds |> List.map _.Tag

/// The F* model of every kind of `idl`.
let private fstarModel (idl: Idl) : string =
    (emitted (FStarTarget.vocabularyModule "LayoutModel" idl (kindTags idl))).Replace("\r\n", "\n")

/// `layout` with one declaration replaced.
let private withUnion (name: string) (f: IdlUnion -> IdlUnion) : Idl =
    { LayoutIdl.layout with
        Unions = LayoutIdl.layout.Unions |> List.map (fun u -> if u.Name = name then f u else u) }

/// `layout` with the case `RowKey` of `RowIdentity` also declaring `rowKeyField`, the key of the
/// case `RowKeyField`, as a member of its own under `opt`.
let private overlapping (opt: Optionality) : Idl =
    withUnion "RowIdentity" (fun u ->
        { u with
            Cases =
                u.Cases
                |> List.map (fun c ->
                    if c.Tag = "RowKey" then
                        { c with
                            Fields =
                                c.Fields
                                @ [ { c.Fields.Head with
                                        Name = "rowKeyField"
                                        Type = TStr
                                        Opt = opt } ] }
                    else
                        c) })

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
                                      Keyed = false
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
                                                Keyed = false
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
                                                Keyed = false
                                                Presence = None
                                                Annotations = Annotations.Empty } ] }
                              else
                                  k) }

              expectFault clash "the member 'pageSize' is on its object twice" "an inline member collides")

          // Phase 722 (DECISIONS.md D285.6) — a value of such a case carrying the member writes both
          // keys, and the D220.3 reading refuses two cases left: the declaration has a value its own
          // reading does not read back, so it is refused where it is declared.
          testCase
              "a case declaring another case's key as a member of its own is refused, both cases and the key named"
              (fun _ ->
                  for opt in [ Optional; Required ] do
                      expectFault
                          (overlapping opt)
                          "union 'RowIdentity', case 'RowKey': the member 'rowKeyField' is the key of case 'RowKeyField'"
                          "both cases and the key are named"

                      Expect.exists
                          (Declare.errors (overlapping opt))
                          (fun f -> (VocabularyFault.describe f).Contains "is the key of case 'RowKeyField'")
                          "and the vocabulary is refused where every declaration fault is"

                  // A host-only member is never on the wire, so it writes no second key.
                  Expect.isFalse
                      (faults (overlapping HostOnly)
                       |> List.exists (fun f -> f.Contains "is the key of case"))
                      "a host-only member of that name is not this fault"

                  Expect.isEmpty (faults LayoutIdl.layout) "the layout vocabulary declares no such case"
                  Expect.isEmpty (faults LayoutIdl.derived) "nor does the vocabulary the derived members are held over")

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

          // Phase 722 — the TypeScript backend lays the three shapes out (`IdlLayoutTypeScriptTests`);
          // every entry point that refused the vocabulary under D220.5 emits it.
          testCase "the TypeScript backend emits a vocabulary that lays a declaration out inline" (fun _ ->
              let tags = LayoutIdl.layout.Kinds |> List.map _.Tag

              for name, r in
                  [ "module", Gen.typescriptModule LayoutIdl.layout tags
                    "derived module", Gen.typescriptModuleDerived [] LayoutIdl.layout tags
                    "declarations", Gen.typescriptDeclarations LayoutIdl.layout tags
                    "derived declarations", Gen.typescriptDeclarationsDerived [] LayoutIdl.layout tags
                    "value", Gen.typescriptValue LayoutIdl.layout TStr (VStr "x") ] do
                  Expect.isOk r name)

          // Phase 722 (DECISIONS.md D283, D284) — the F* target lays an inline record out on its holder
          // and models a presence-keyed union: its holder has one constructor per choice of case, told
          // apart by the D220.3 reading of the holder's own object.
          testCase
              "the F* target models every kind, a constructor per choice of a presence-keyed union's case"
              (fun _ ->
                  for v in FStarTarget.partition LayoutIdl.layout do
                      Expect.isNone v.Refusal (v.Tag + " is modelled")

                  let model = fstarModel LayoutIdl.layout

                  for ctor in
                      [ "C__vkind__Grid__NoRowIdentity__Source"
                        "C__vkind__Grid__NoRowIdentity__StaticRows"
                        "C__vkind__Grid__RowKey__Source"
                        "C__vkind__Grid__RowKey__StaticRows"
                        "C__vkind__Grid__RowKeyField__Source"
                        "C__vkind__Grid__RowKeyField__StaticRows" ] do
                      Expect.stringContains model ("  | " + ctor + " : ") ctor

                  // An optional union with no key present is a constructor; a required one has none.
                  Expect.isFalse (model.Contains "NoGridRows") "a required union always holds a case")

          testCase
              "the F* model's reading is D220.3: a case another present case explains drops out, two left is refused"
              (fun _ ->
                  let model = fstarModel LayoutIdl.layout

                  for line in
                      [ "let pick_u_grid_rows (#num #flt: eqtype) (el: jval num flt) : Tot pk_u_grid_rows ="
                        "  let p0 = Ok? (get_prop \"source\" el) in"
                        "  let p1 = Ok? (get_prop \"staticRows\" el) in"
                        // `StaticRows` synthesises `source`, so `Source` is not left beside it.
                        "  let c0 = p0 && not (p1) in"
                        "  if c0 then (if c1 then PB__u_grid_rows else PK__u_grid_rows__Source) else"
                        "  if c1 then PK__u_grid_rows__StaticRows else"
                        "  PN__u_grid_rows"
                        // Neither row-identity case synthesises the other's key: both present is two left.
                        "  if c0 then (if c1 then PB__u_row_identity else PK__u_row_identity__RowKey) else"
                        // A holder's arm asks every reading beside its tag.
                        "    if tag = \"Grid\" && pick_u_row_identity #num #flt el = PK__u_row_identity__RowKey && pick_u_grid_rows #num #flt el = PK__u_grid_rows__StaticRows then" ] do
                      Expect.stringContains model (line + "\n") line)

          testCase "the F* model writes a synthesised member at its declared value and refuses another" (fun _ ->
              let model = fstarModel LayoutIdl.layout
              let declared = """(JObj [("$type", (JStr "Static")); ("value", (JArr []))])"""

              Expect.stringContains model ("""("source", """ + declared + ")") "the encoder writes the declared value"

              Expect.stringContains
                  model
                  ("(if v = " + declared + " then (let w = () in Ok w) else Error")
                  "the decoder compares what it reads"

              Expect.stringContains model "source:(unit)" "the constructor carries nothing for it"

              // The proofs state both halves: the round trip, and that the reading decides.
              let proofs =
                  emitted (
                      FStarTarget.proofsModule
                          "LayoutProofs"
                          "LayoutModel"
                          LayoutIdl.layout
                          (kindTags LayoutIdl.layout)
                  )

              Expect.stringContains proofs "let dec_vkind_reads (#num #flt: eqtype) (el: jval num flt)" "stated"

              Expect.stringContains
                  proofs
                  ("| Ok (C__vkind__Grid__RowKey__StaticRows _ _ _ _ _ _ _) -> pick_u_row_identity #num #flt el == PK__u_row_identity__RowKey /\\ pick_u_grid_rows #num #flt el == PK__u_grid_rows__StaticRows /\\ get_prop \"source\" el == Ok "
                   + declared)
                  "an accepted document's constructor is the one its keys pick, its synthesised member as declared"

              Expect.stringContains
                  proofs
                  "let pk_vkind__Grid__NoRowIdentity__Source__identity (#num #flt: eqtype) (x: vkind num flt)"
                  "one lemma per constructor and union states the reading off the encoded object")

          // Phase 722 (D285.6) — `Declare.errors` refuses this union now. The target still refuses it
          // when handed a value no declaration check has seen (the vocabulary here is built in memory
          // and never passes through the artifact reader): it asks the check itself before it lays an
          // inline declaration out (D284.7), and answers in the check's words.
          testCase
              "the F* target refuses a case that declares another case's key, as the declaration check does"
              (fun _ ->
                  let overlapping = overlapping Optional

                  Expect.isNonEmpty (Declare.errors overlapping) "the declaration check refuses it"

                  // A value of the case that carries the member writes both keys, and the reading refuses
                  // two cases left: the round trip is false of it, so no model of the union has it.
                  let value =
                      VNode(
                          "g",
                          "Grid",
                          [ "columns", VList []
                            "paging", VRecord []
                            "identity", VUnion("RowKey", [ "rowKey", VClosure; "rowKeyField", VStr "id" ])
                            "rows", VUnion("Source", [ "source", VUnion("State", [ "key", VStr "k" ]) ]) ]
                      )

                  match Encode.encode overlapping value with
                  | Error m -> failtestf "the value did not encode: %s" m
                  | Ok doc ->
                      match Decode.decodeDetailed overlapping doc with
                      | Ok _ -> failtest "the interpreter read back a document carrying both keys"
                      | Error e -> Expect.equal (shape e) ("OutOfRange", at [ "kind"; "rowKeyField" ]) "refused"

                  match (FStarTarget.partition overlapping |> List.find (fun v -> v.Tag = "Grid")).Refusal with
                  | None -> failtest "the F* target modelled a union whose round trip is false"
                  | Some why ->
                      Expect.stringContains
                          why
                          "union 'RowIdentity', case 'RowKey': the member 'rowKeyField' is the key of case 'RowKeyField'"
                          "both cases and the key are named"

                      Expect.stringContains why "the declaration check refuses" "and the refusal is the declaration's"

                  for emit in
                      [ FStarTarget.vocabularyModule "M" overlapping [ "Grid" ]
                        FStarTarget.proofsModule "MProofs" "M" overlapping [ "Grid" ] ] do
                      Expect.stringContains
                          (refusalOf emit)
                          "is the key of case 'RowKeyField'"
                          "no module is emitted for it")

          testCase "the F* target refuses a synthesised value the opaque numeric model cannot spell, by name" (fun _ ->
              let numbered =
                  withUnion "GridRows" (fun u ->
                      { u with
                          Discrimination =
                              match u.Discrimination with
                              | UnionDiscrimination.ByPresence keyed ->
                                  UnionDiscrimination.ByPresence(
                                      keyed
                                      |> List.map (fun k ->
                                          if k.Case = "StaticRows" then
                                              { k with
                                                  Synthesised =
                                                      [ "source",
                                                        VUnion("Static", [ ("value", VJson(JArr [ JInt 1 ])) ]) ] }
                                          else
                                              k)
                                  )
                              | other -> other })

              Expect.isEmpty (Declare.errors numbered) "well-formed"

              match (FStarTarget.partition numbered |> List.find (fun v -> v.Tag = "Grid")).Refusal with
              | None -> failtest "the F* target spelled a number its carriers cannot"
              | Some why ->
                  Expect.stringContains
                      why
                      "the synthesised member 'source' of union 'GridRows' case 'StaticRows'"
                      "named")

          testCase "the F* vectors state each corpus document at the constructor its keys pick" (fun _ ->
              let facts =
                  emitted (
                      FStarTarget.vectorsModule
                          "LayoutVectors"
                          [ { Model = "LayoutModel"
                              Prefix = "L"
                              Idl = LayoutIdl.layout
                              Wires = LayoutIdl.corpus } ]
                  )

              for ctor in
                  [ "L.C__vkind__Grid__NoRowIdentity__Source"
                    "L.C__vkind__Grid__RowKeyField__Source"
                    "L.C__vkind__Grid__RowKey__StaticRows"
                    "L.C__vkind__Grid__NoRowIdentity__StaticRows"
                    "L.C__vkind__Form" ] do
                  Expect.stringContains facts ctor ctor)

          testCase "the F* model of a record-payload kind is its flat twin's, line for line" (fun _ ->
              // The stamp lines name the vocabulary, and the header's line for `Grid` says why each
              // vocabulary leaves it out; every other line is the model.
              let body (text: string) =
                  text.Replace("\r\n", "\n").Split('\n')
                  |> Array.filter (fun l ->
                      not (
                          l.Contains "sha256:"
                          || l.TrimStart().StartsWith("- Grid:", System.StringComparison.Ordinal)
                      ))
                  |> List.ofArray

              for name, emit in
                  [ "model", (fun idl -> FStarTarget.vocabularyModule "LayoutModel" idl [ "Form" ])
                    "proofs", (fun idl -> FStarTarget.proofsModule "LayoutModelProofs" "LayoutModel" idl [ "Form" ]) ] do
                  let layout = body (emitted (emit LayoutIdl.layout))
                  Expect.isGreaterThan (List.length layout) 50 (name + ": a module was emitted")
                  Expect.equal layout (body (emitted (emit LayoutIdl.flat))) name)

          // D285.5 — the one refusal an inline declaration keeps in the codec-only module is the
          // wire's: a root's entries write and read one JSON value, and it has none of its own.
          testCase "the codec-only module refuses an inline declaration as a ROOT, and only there" (fun _ ->
              for root, named in [ "Paging", "'Paging'"; "Chain", "'Chain'" ] do
                  let request =
                      { LayoutIdl.codecRequest with
                          Roots = [ root ] }

                  match Gen.fsharpCodecModule GenSupport.Empty request "M" LayoutIdl.derived with
                  | Error(CodegenError.UnsupportedConstruct(what, why, _)) ->
                      Expect.stringContains what "a codec-only root" "what"
                      Expect.stringContains what named "the root is named"
                      Expect.stringContains why "an inline declaration has none of its own" "the reason is the wire's"
                  | other -> failtestf "the root %s was not refused by name: %A" root other

              Expect.isOk (LayoutIdl.generateCodec ()) "a declaration that HOLDS one is a root") ]

// ---------------------------------------------------------------------------
// Phase 722 (DECISIONS.md D285) — the CODEC-ONLY module over inline declarations: the committed
// `LayoutCodecGenerated`, whose roots are the declarations of `LayoutIdl.derived` that hold one.
// ---------------------------------------------------------------------------

/// A root's three entries over a JSON value, each answer as its re-encoding or its refusal.
let private codecEntries
    : (string * IdlType * (JVal -> Result<JVal, DecodeError>) * (JVal -> Result<JVal, DecodeError>)) list =
    [ "GridBox",
      TRecord "GridBox",
      (C.decodeGridBox >> Result.map C.encodeGridBox),
      (C.decodeGridBoxStrict >> Result.map C.encodeGridBox)
      "LinkBox",
      TRecord "LinkBox",
      (C.decodeLinkBox >> Result.map C.encodeLinkBox),
      (C.decodeLinkBoxStrict >> Result.map C.encodeLinkBox)
      "Tree",
      TUnion("Tree", []),
      (C.decodeTree >> Result.map C.encodeTree),
      (C.decodeTreeStrict >> Result.map C.encodeTree)
      "Field",
      TUnion("Field", []),
      (C.decodeField >> Result.map C.encodeField),
      (C.decodeFieldStrict >> Result.map C.encodeField) ]

/// Values of the four roots, each in canonical form: every inline shape, both cases of each
/// presence-keyed union, the synthesised placeholder, and a nested holder.
let private codecCorpus: (string * string) list =
    [ "GridBox", """{"source":{"$type":"State","key":"rows"}}"""
      "GridBox",
      """{"pageSize":10,"pageStateKey":"p","rowKeyField":"id","source":{"$type":"Static","value":[1]},"title":"t"}"""
      "GridBox",
      """{"rowKey":"<closure>","source":{"$type":"Static","value":[]},"staticRows":{"headers":["h"],"rows":[["1"]]}}"""
      "LinkBox", """{"label":"a","link":{"label":"b","stop":"z"}}"""
      "LinkBox", """{"label":"open"}"""
      "Tree", """{"$type":"Branch","kids":[{"$type":"Leaf","n":1},{"$type":"Branch","kids":[],"note":"inner"}]}"""
      "Field", """{"$type":"Text","placeholder":"type here","value":"v"}"""
      "Field", """{"$type":"Check","on":true}""" ]

/// Values a reading refuses: an undeclared member beside an inline record's, beside a keyed case's,
/// inside the case a holder's reading picks, inside a nested holder and beside a record payload's
/// (the strict reading's own); then the D220.3 reading's refusals, which both readings give.
let private codecRefused: (string * string) list =
    [ "GridBox", """{"pageSize":1,"pageSizes":2,"source":{"$type":"State","key":"k"}}"""
      "GridBox", """{"source":{"$type":"State","key":"k","extra":1}}"""
      "GridBox", """{"source":{"$type":"Static","value":[]},"staticRows":{"headers":[],"rows":[],"extra":1}}"""
      "LinkBox", """{"label":"a","link":{"label":"b","stop":"z","extra":1}}"""
      "Tree", """{"$type":"Branch","kids":[{"$type":"Branch","kids":[],"extra":1}]}"""
      "Field", """{"$type":"Text","value":"v","on":true}"""
      "GridBox", """{"rowKey":"<closure>","rowKeyField":"id","source":{"$type":"State","key":"k"}}"""
      "GridBox", """{"staticRows":{"headers":[],"rows":[]}}"""
      "GridBox", """{"pageSize":1}"""
      "LinkBox", """{"label":"a","link":{"label":"b"},"stop":"z"}""" ]

[<Tests>]
let codecOnlyTests =
    let idl = LayoutIdl.derived

    let entry (root: string) =
        codecEntries |> List.find (fun (n, _, _, _) -> n = root)

    let rendered (r: Result<JVal, DecodeError>) : string =
        match r with
        | Ok j -> "ok " + Canonical.write Rendering.CrossHost j
        | Error e -> "refused " + DecodeError.codeName e.Code + " " + DecodePath.render e.Path

    /// The interpreter's verdict under a reading: accepted, or the refusal by code and path.
    let interpreter (read: Idl -> IdlType -> JVal -> Result<IdlValue, DecodeError>) (t: IdlType) (j: JVal) =
        match read idl t j with
        | Ok _ -> "ok"
        | Error e -> rendered (Error e)

    let verdict (answer: string) =
        if answer.StartsWith "ok " then "ok" else answer

    /// A node of `derived` holding the root's value, in canonical form: the interpreter's public
    /// entries read and write a node, so its bytes for a value are read off the node that holds it.
    let held (root: string) (doc: string) : string =
        match root with
        | "GridBox" ->
            sprintf """{"id":"n","kind":{"$type":"Outline","box":%s,"stop":"s","tree":{"$type":"Leaf","n":0}}}""" doc
        | "LinkBox" -> sprintf """{"id":"n","kind":{"$type":"Outline","link":%s,"tree":{"$type":"Leaf","n":0}}}""" doc
        | "Tree" -> sprintf """{"id":"n","kind":{"$type":"Outline","stop":"s","tree":%s}}""" doc
        | _ -> sprintf """{"id":"n","kind":{"$type":"Form","field":%s}}""" doc

    testList
        "Phase 722 the codec-only module lays out inline declarations"
        [ testCase "the committed LayoutCodecGenerated.fs is what the generator emits (byte for byte)" (fun _ ->
              let generated = emitted (LayoutIdl.generateCodec ())
              let path = Snapshots.repoFile LayoutIdl.codecGeneratedFile

              Approval.write Approval.Regen Approval.Regenerated.LayoutCodec path generated
              |> ignore

              Expect.equal
                  generated
                  (File.ReadAllText path)
                  "regenerate with: FUARAN_REGEN=LayoutCodecGenerated, running IdlLayoutTests")

          testCase "every value is re-written unchanged, and the bytes are the interpreter's" (fun _ ->
              for root, doc in codecCorpus do
                  let _, t, plain, strict = entry root
                  let j = parse doc

                  Expect.equal (rendered (plain j)) ("ok " + doc) (root + " round-trips " + doc)
                  Expect.equal (rendered (strict j)) ("ok " + doc) (root + " round-trips under the strict reading")

                  Expect.equal (interpreter Decode.valueDetailed t j) "ok" "the interpreter reads the value"

                  Expect.equal
                      (interpreted idl (held root doc))
                      (held root doc)
                      "the interpreter writes the same bytes for the value, on the node that holds it")

          testCase "every refusal is the interpreter's, by code and path, under both readings" (fun _ ->
              for root, doc in codecRefused do
                  let _, t, plain, strict = entry root
                  let j = parse doc

                  Expect.equal
                      (rendered (strict j))
                      (interpreter Decode.valueStrictDetailed t j)
                      (root + " under the strict reading: " + doc)

                  Expect.stringStarts (rendered (strict j)) "refused " (root + " is refused: " + doc)

                  Expect.equal
                      (verdict (rendered (plain j)))
                      (interpreter Decode.valueDetailed t j)
                      (root + " under the plain reading: " + doc)

              // The first six are the strict reading's own: the plain reading accepts them.
              let undeclared =
                  codecRefused
                  |> List.filter (fun (root, doc) ->
                      let _, _, _, strict = entry root
                      (rendered (strict (parse doc))).StartsWith "refused UndeclaredMember")

              Expect.equal
                  (List.length undeclared)
                  6
                  "six undeclared members, each met inside or beside an inline declaration")

          // The SAME emitter: each inline declaration's member writer and reader in the codec-only
          // module is, byte for byte, the block the structural module prints for it.
          testCase "an inline declaration's writer and reader are the structural module's, block for block" (fun _ ->
              let structural = emitted (LayoutIdl.generateDerived ())
              let codecOnly = emitted (LayoutIdl.generateCodec ())

              let block (text: string) (name: string) : string =
                  let start = text.IndexOf("private " + name + " ", System.StringComparison.Ordinal)

                  if start < 0 then
                      failtestf "no %s in the module" name

                  let stop = text.IndexOf("\n\n", start, System.StringComparison.Ordinal)
                  text.Substring(start, (if stop < 0 then text.Length else stop) - start)

              for declaration in [ "Paging"; "TextPayload"; "BranchPayload"; "RowIdentity"; "GridRows"; "Chain" ] do
                  for prefix in [ "encMembers"; "decMembers" ] do
                      let name = prefix + declaration
                      Expect.isGreaterThan (block structural name).Length 40 (name + " is a block")
                      Expect.equal (block codecOnly name) (block structural name) name)

          // Byte identity for a vocabulary with no inline declaration is held by the drift guards of
          // the committed codec-only modules (the seam codecs); this pins that none of the inline
          // machinery is printed for one.
          testCase "a vocabulary with no inline declaration prints none of the inline machinery" (fun _ ->
              let request: CodecOnly =
                  { Roots = [ "Field"; "StaticTable"; "Feed" ]
                    HostTypes = Map.ofList [ "Field", "H.Field"; "StaticTable", "H.StaticTable"; "Feed", "H.Feed" ] }

              let text =
                  emitted (Gen.fsharpCodecModule GenSupport.Empty request "M" LayoutIdl.flat)

              for helper in [ "dPick"; "dSynth"; "encMembers"; "decMembers" ] do
                  Expect.isFalse
                      (text.Contains helper)
                      (sprintf "the flat twin's codec-only module carries no %s" helper)) ]

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
