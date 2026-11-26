module Fuaran.Core.Tests.IdlSchemaValidatorTests

// ---------------------------------------------------------------------------
// Phase 303 — the schema leg is CERTIFIED, not described.
//
// `Directory.Packages.props` has pinned JsonSchema.Net since Phase 697 "because the schema leg
// is certified with a real Draft 2020-12 validator", and until this phase no test used it: the
// emitted schema was checked by substring asserts (it names `$defs`, it mentions every kind),
// which say what the schema CONTAINS and nothing about what it ACCEPTS. Here every certification
// vocabulary's schema is built by the pinned validator and every wire the three-way differential
// runs — the sampler's draws and their adversarial variants, §7 sentinels and hostile map keys
// included — is evaluated against it; the reference vocabulary's drawn ops are evaluated against
// its two-rooted schema; and the schema's own graph is checked closed: every `$ref` resolves to a
// definition, and every definition is reachable from the root.
//
// Each claim has a negative control beside it, because a validator that accepts everything
// certifies nothing: a schema with a dangling reference or an orphan definition is named, and a
// document that breaks the vocabulary is rejected.
// ---------------------------------------------------------------------------

open System.Text.Json
open Expecto
open Json.Schema
open Fuaran.Core
open Fuaran.Core.Idl

let private schemaOf (idl: Idl) : string =
    match Gen.jsonSchema idl with
    | Ok s -> s
    | Error e -> failtestf "the schema leg refused the vocabulary: %s" (CodegenError.describe e)

/// Build with a FRESH registry, so one schema's definitions can never satisfy another's reference.
let private build (schema: string) : JsonSchema =
    JsonSchema.FromText(schema, BuildOptions(SchemaRegistry = SchemaRegistry()))

let private evaluate (schema: JsonSchema) (wire: string) : bool =
    use doc = JsonDocument.Parse wire
    let options = EvaluationOptions(OutputFormat = OutputFormat.Flag)
    (schema.Evaluate(doc.RootElement, options)).IsValid

let private defsOf (root: JVal) : Map<string, JVal> =
    match root with
    | JObj fields ->
        match fields |> List.tryFind (fun (k, _) -> k = "$defs") with
        | Some(_, JObj ds) -> Map.ofList ds
        | _ -> Map.empty
    | _ -> Map.empty

/// The `$ref`s under a value, not descending into a `$defs` block.
let rec private refsIn (j: JVal) : string list =
    match j with
    | JObj fields ->
        fields
        |> List.collect (fun (k, v) ->
            match k, v with
            | "$defs", _ -> []
            | "$ref", JStr r -> [ r ]
            | _ -> refsIn v)
    | JArr xs -> List.collect refsIn xs
    | _ -> []

/// The schema's definitions, the ones reachable from its root through `$ref`, and the
/// references that name no definition.
let private closure (schema: string) : Set<string> * Set<string> * string list =
    let prefix = "#/$defs/"

    match Json.parse schema with
    | Error m -> failtestf "the emitted schema is not JSON: %s" m
    | Ok root ->
        let defs = defsOf root

        let rec walk (seen: Set<string>) (dangling: string list) (todo: string list) =
            match todo with
            | [] -> seen, List.rev dangling
            | r :: rest when not (r.StartsWith prefix) -> walk seen (r :: dangling) rest
            | r :: rest ->
                let name = r.Substring prefix.Length

                if seen.Contains name then
                    walk seen dangling rest
                else
                    match defs.TryFind name with
                    | None -> walk seen (r :: dangling) rest
                    | Some body -> walk (seen.Add name) dangling (refsIn body @ rest)

        let reachable, dangling = walk Set.empty [] (refsIn root)
        defs |> Map.keys |> Set.ofSeq, reachable, dangling

let private assertClosed (name: string) (schema: string) =
    let defs, reachable, dangling = closure schema
    Expect.isEmpty dangling (sprintf "%s: every $ref resolves to a definition" name)

    Expect.isEmpty
        (Set.difference defs reachable |> Set.toList)
        (sprintf "%s: every definition is reachable from the root (no orphan defs)" name)

let private vocabularyCase (name: string, idl: Idl, wires: string list) =
    testCase (sprintf "%s: every three-way vector validates, and the schema's references close" name) (fun _ ->
        let text = schemaOf idl
        assertClosed name text
        let schema = build text

        Expect.isGreaterThan wires.Length 0 "the differential drew wires to validate"

        let rejected = wires |> List.filter (evaluate schema >> not)

        Expect.isEmpty
            (rejected |> List.truncate 5)
            (sprintf "%s: %d of %d wires rejected by the schema" name rejected.Length wires.Length))

[<Tests>]
let schemaValidator =
    testList
        "Phase 303 — the schema leg is certified by the pinned validator"
        [ for c in CertificationVectors.certificationWires () do
              vocabularyCase c

          testCase "reference: every drawn op validates against the two-rooted schema" (fun _ ->
              let idl, ops = CertificationVectors.certificationOps ()
              let text = schemaOf idl
              assertClosed "reference ops" text
              let schema = build text
              Expect.isGreaterThan ops.Length 0 "ops were drawn"
              let rejected = ops |> List.filter (evaluate schema >> not)
              Expect.isEmpty (rejected |> List.truncate 5) "every op validates")

          testCase "negative control: a document that breaks the vocabulary is rejected" (fun _ ->
              let idl = ReferenceIdl.refIdl
              let schema = build (schemaOf idl)

              let good =
                  match Encode.encode idl ReferenceIdl.note1 with
                  | Ok w -> w
                  | Error m -> failtestf "the fixture did not encode: %s" m

              Expect.isTrue (evaluate schema good) "the fixture validates"

              // An unknown kind, a missing required member, and a §7 token at an INT slot (§7
              // stops at the float slot) — each must be refused.
              for bad in
                  [ good.Replace("\"Note\"", "\"Nope\"")
                    """{"id":"n","kind":{"$type":"Note"}}"""
                    """{"$type":"Insert","index":"NaN","newKind":{"$type":"Note","body":{"$type":"Inline","text":"t"}},"parentId":"p"}""" ] do
                  Expect.isFalse (evaluate schema bad) (sprintf "the schema rejects %s" bad))

          testCase "negative control: a dangling reference or an orphan definition fails the closure check" (fun _ ->
              let text = schemaOf ReferenceIdl.refIdl

              let _, _, dangling =
                  closure (text.Replace("\"#/$defs/Point\"", "\"#/$defs/NoSuchDef\""))

              Expect.contains dangling "#/$defs/NoSuchDef" "the dangling reference is named"

              let defs, reachable, _ =
                  closure (text.Replace("\"$defs\":{", "\"$defs\":{\"Orphan303\":{\"type\":\"string\"},"))

              Expect.contains (Set.difference defs reachable) "Orphan303" "the orphan is named") ]

// ---------------------------------------------------------------------------
// Phase 704 — the schema leg states the vocabulary's docs. A declaration's `doc` is its
// definition's `description`, a case's is its branch's and a field's is its property's; a
// vocabulary that documents nothing emits the bytes it emitted before, pinned by digest because
// the repository commits no generated vocabulary schema to compare against. A generic union is
// defined once per instantiation under the resolution's instance names, which a host's
// hand-written schema already uses for the same definitions.
// ---------------------------------------------------------------------------

let private sha256 (s: string) : string =
    System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes s)
    |> Array.map (fun b -> b.ToString "x2")
    |> String.concat ""

/// Each undocumented vocabulary's schema, as the leg emitted it before Phase 704 (bytes, digest).
let private undocumentedPins: (string * Idl * int * string) list =
    let certification name =
        CertificationVectors.vocabularies |> List.find (fun v -> v.Name = name) |> _.Idl

    [ "reference", ReferenceIdl.refIdl, 9432, "89a86662a588fcf5c40370c483bc044116cae2c49517e2bb1abea48e91ad9bd6"
      "document", certification "document", 3878, "add582492c169fe1cc0ab66524800992a9c0c127fe840d552deeb22c19890dbb"
      "score", certification "score", 7217, "67c8b04837817d3c944c1032910893ed38441e8b1cd6d84681ee3466ce7e5ccd" ]

let private doc (d: string) = { Annotations.Empty with Doc = Some d }

/// A doc carrying a quote and a line break, so the description is shown to be escaped and kept.
let private textDoc =
    "Text a reader sees, \"as written\".\nA lookup resolves it by key."

let private documentField (name: string) (d: string) (fields: IdlField list) =
    fields
    |> List.map (fun f -> if f.Name = name then { f with Annotations = doc d } else f)

/// The reference vocabulary with a doc on one declaration of each sort: an enum, a union and one
/// of its cases, a generic union and one of its cases, a record and a field, a kind and a field, an
/// op and a field, and an envelope member.
let private documentedIdl: Idl =
    let idl = ReferenceIdl.refIdl

    let documentCase (tag: string) (d: string) (u: IdlUnion) =
        { u with
            Cases =
                u.Cases
                |> List.map (fun c -> if c.Tag = tag then { c with Annotations = doc d } else c) }

    { idl with
        Enums =
            idl.Enums
            |> List.map (fun e ->
                if e.Name = "LayoutKind" then
                    { e with
                        Annotations = doc "How a group lays out its children." }
                else
                    e)
        Unions =
            idl.Unions
            |> List.map (fun u ->
                match u.Name with
                | "Text" -> { u with Annotations = doc textDoc } |> documentCase "Inline" "The text itself."
                | "Slot" ->
                    { u with
                        Annotations = doc "A value, fixed or named." }
                    |> documentCase "Fixed" "The value, stated."
                | _ -> u)
        Records =
            idl.Records
            |> List.map (fun r ->
                if r.Name = "Point" then
                    { r with
                        Annotations = doc "A point on the plane."
                        Fields = documentField "x" "The horizontal coordinate." r.Fields }
                else
                    r)
        Kinds =
            idl.Kinds
            |> List.map (fun k ->
                if k.Tag = "Measure" then
                    { k with
                        Annotations = doc "One measured quantity."
                        Fields = documentField "label" "What the quantity is called." k.Fields }
                else
                    k)
        Ops =
            idl.Ops
            |> List.map (fun o ->
                if o.Tag = "Insert" then
                    { o with
                        Annotations = doc "Insert a node under a parent."
                        Fields = documentField "index" "Where among the parent's children." o.Fields }
                else
                    o)
        NodeFields = documentField "hidden" "Whether the node is drawn." idl.NodeFields }

let private member' (key: string) (v: JVal) : JVal =
    match v with
    | JObj fs ->
        match fs |> List.tryFind (fun (k, _) -> k = key) with
        | Some(_, x) -> x
        | None -> failtestf "no member `%s` in %s" key (Json.render v)
    | other -> failtestf "`%s` asked of a non-object %s" key (Json.render other)

let private at (path: string list) (v: JVal) : JVal =
    (v, path) ||> List.fold (fun x k -> member' k x)

let private parsed (text: string) : JVal =
    match Json.parse text with
    | Ok v -> v
    | Error m -> failtestf "the emitted schema is not JSON: %s" m

/// The branch of a union definition whose discriminator const is `tag`.
let private branch (tag: string) (def: JVal) : JVal =
    match member' "oneOf" def with
    | JArr bs ->
        bs
        |> List.find (fun b ->
            match b with
            | JObj fs when fs |> List.exists (fun (k, _) -> k = "properties") ->
                at [ "properties"; "$type"; "const" ] b = JStr tag
            | _ -> false)
    | other -> failtestf "`oneOf` is not an array: %s" (Json.render other)

/// The schema with every string-valued `description` removed.
let rec private undescribed (v: JVal) : JVal =
    match v with
    | JObj fs ->
        JObj(
            fs
            |> List.choose (fun (k, x) ->
                match k, x with
                | "description", JStr _ -> None
                | _ -> Some(k, undescribed x))
        )
    | JArr xs -> JArr(List.map undescribed xs)
    | other -> other

[<Tests>]
let schemaDocs =
    testList
        "Phase 704 — the schema leg states the vocabulary's docs"
        [ for name, idl, length, digest in undocumentedPins do
              testCase (sprintf "%s: a vocabulary that documents nothing emits the bytes it did before" name) (fun _ ->
                  let text = schemaOf idl
                  Expect.equal text.Length length (sprintf "%s: the schema's length is unchanged" name)
                  Expect.equal (sha256 text) digest (sprintf "%s: the schema's bytes are unchanged" name))

          testCase "a declaration's doc is its definition's description, leading" (fun _ ->
              let schema = parsed (schemaOf documentedIdl)
              let defs = member' "$defs" schema

              for def, expected in
                  [ "LayoutKind", "How a group lays out its children."
                    "Text", textDoc
                    "Point", "A point on the plane."
                    "Measure", "One measured quantity."
                    "Insert", "Insert a node under a parent." ] do
                  match member' def defs with
                  | JObj(("description", d) :: _) -> Expect.equal d (JStr expected) (sprintf "%s's description" def)
                  | other -> failtestf "%s does not lead with its description: %s" def (Json.render other))

          testCase "a generic union's doc is on every instantiation, and its case's on each branch" (fun _ ->
              let defs = member' "$defs" (parsed (schemaOf documentedIdl))

              for inst in [ "Slot_float"; "Slot_str" ] do
                  let def = member' inst defs
                  Expect.equal (member' "description" def) (JStr "A value, fixed or named.") (inst + "'s description")

                  Expect.equal
                      (member' "description" (branch "Fixed" def))
                      (JStr "The value, stated.")
                      (inst + ": the Fixed branch's description")

                  Expect.isFalse
                      (match branch "Ref" def with
                       | JObj fs -> fs |> List.exists (fun (k, _) -> k = "description")
                       | _ -> true)
                      (inst + ": an undocumented case's branch carries none"))

          testCase "a case's doc is its branch's description" (fun _ ->
              let defs = member' "$defs" (parsed (schemaOf documentedIdl))

              Expect.equal
                  (member' "description" (branch "Inline" (member' "Text" defs)))
                  (JStr "The text itself.")
                  "the Inline branch's description")

          testCase "a field's doc is its property's description, after the type's keywords" (fun _ ->
              let defs = member' "$defs" (parsed (schemaOf documentedIdl))

              for path, keyword, expected in
                  [ [ "Point"; "properties"; "x" ], "anyOf", "The horizontal coordinate."
                    [ "Measure"; "properties"; "label" ], "$ref", "What the quantity is called."
                    [ "Insert"; "properties"; "index" ], "type", "Where among the parent's children."
                    [ "Node"; "properties"; "hidden" ], "type", "Whether the node is drawn." ] do
                  match at path defs with
                  | JObj [ (k, _); ("description", d) ] ->
                      Expect.equal k keyword (sprintf "%A: the type's keyword comes first" path)
                      Expect.equal d (JStr expected) (sprintf "%A's description" path)
                  | other -> failtestf "%A is not the type then its description: %s" path (Json.render other))

          testCase "the docs add descriptions and nothing else" (fun _ ->
              let documented = parsed (schemaOf documentedIdl)
              let plain = schemaOf ReferenceIdl.refIdl
              Expect.notEqual (Json.render documented) plain "the documented schema differs"

              Expect.equal
                  (Json.render (undescribed documented))
                  plain
                  "without its descriptions it is the plain schema")

          testCase "the documented schema is well-formed, closed and accepts the vocabulary's documents" (fun _ ->
              let text = schemaOf documentedIdl
              assertClosed "documented reference" text
              let schema = build text

              for wire in
                  [ for _, value, _ in ReferenceIdl.nodeCases do
                        match Encode.encode documentedIdl value with
                        | Ok w -> w
                        | Error m -> failtestf "a fixture did not encode: %s" m ] do
                  Expect.isTrue (evaluate schema wire) (sprintf "the schema accepts %s" wire))

          testCase "a generic union is defined once per instantiation, under its instance names" (fun _ ->
              // A guard rather than a red-first test: the behaviour predates Phase 704 (fuaran#1068),
              // and this pins the names a host's published schema reproduces.
              let defs = member' "$defs" (parsed (schemaOf ReferenceIdl.refIdl))

              let names =
                  match defs with
                  | JObj fs -> fs |> List.map fst
                  | _ -> []

              Expect.containsAll names [ "Slot_float"; "Slot_str" ] "one definition per instantiation"
              Expect.isFalse (List.contains "Slot" names) "never the bare generic name"

              Expect.equal
                  (at [ "properties"; "value"; "type" ] (branch "Fixed" (member' "Slot_str" defs)))
                  (JStr "string")
                  "each instantiation is the union at its argument") ]

// ---------------------------------------------------------------------------
// Phase 732 — a parameterised record in the schema: one definition per instantiation, named as a
// union's instantiations are named, and held to the interpreter's reading in both directions.
// ---------------------------------------------------------------------------

[<Tests>]
let parameterisedRecords =
    let idl = ParameterisedRecordsIdl.idl
    let defs () = schemaOf idl |> parsed |> defsOf

    testList
        "Phase 732 — the schema states a parameterised record once per instantiation"
        [ testCase "each instantiation named has its own definition, and no generic record stands bare" (fun _ ->
              let names = defs () |> Map.keys |> Set.ofSeq

              for instance in
                  [ "Ranged_float"
                    "Ranged_int"
                    "Labelled_float"
                    "Labelled_str"
                    "Labelled_Ranged_int"
                    "Series_float"
                    "Entry_str_Direction"
                    "Binding_float"
                    "Binding_int"
                    "Direction" ] do
                  Expect.isTrue (names.Contains instance) (sprintf "the schema defines %s" instance)

              for generic in idl.Records |> List.filter (fun r -> not (List.isEmpty r.Params)) do
                  Expect.isFalse
                      (names.Contains generic.Name)
                      (sprintf "%s is never defined uninstantiated" generic.Name)

              // An inline record has no object of its own at any instantiation: its members are
              // stated on the object that holds it.
              for name in names do
                  Expect.isFalse
                      (name.StartsWith "Paging" || name.StartsWith "LocalState")
                      (sprintf "%s: an inline record has no definition" name)

              assertClosed "parameterised records" (schemaOf idl))

          testCase "a member naming a parameter is stated at the instantiation's argument" (fun _ ->
              let memberOf (instance: string) (name: string) =
                  defs () |> Map.find instance |> at [ "properties"; name ]

              Expect.equal (memberOf "Ranged_int" "low" |> member' "type") (JStr "integer") "Ranged<int>.low"
              Expect.equal (memberOf "Labelled_str" "value" |> member' "type") (JStr "string") "Labelled<string>.value"

              Expect.notEqual
                  (memberOf "Ranged_float" "low")
                  (memberOf "Ranged_int" "low")
                  "one declaration, two instantiations, two schemas"

              // A record of a record, and a record passing its parameter on, name the
              // instantiation they make.
              Expect.equal
                  (memberOf "Labelled_Ranged_int" "value")
                  (JObj [ "$ref", JStr "#/$defs/Ranged_int" ])
                  "Labelled<Ranged<int>>.value"

              Expect.equal
                  (memberOf "Series_float" "points" |> member' "items")
                  (JObj [ "$ref", JStr "#/$defs/Labelled_float" ])
                  "Series<float>.points"

              // An inline record held by a parameterised case: the case's object states the
              // record's members, at the case's argument.
              let local (instance: string) =
                  branch "Local" (defs () |> Map.find instance) |> at [ "properties"; "initial" ]

              Expect.equal (local "Binding_int" |> member' "type") (JStr "integer") "Binding<int>.Local.initial"
              Expect.equal (local "Binding_float") (memberOf "Ranged_float" "low") "Binding<float>.Local.initial")

          testCase "the schema admits what the interpreter writes, and refuses what it refuses" (fun _ ->
              let schema = build (schemaOf idl)

              for what, _, bytes in ParameterisedRecordsIdl.documents do
                  Expect.isTrue (evaluate schema bytes) (what + ": validates")

              match Sample.trySampleNodes idl ParameterisedRecordsIdl.kindTags 732 600 with
              | Error fault -> failtestf "%s" fault.Describe
              | Ok drawn ->
                  let wires = drawn |> List.choose (Encode.encode idl >> Result.toOption)
                  Expect.equal wires.Length drawn.Length "every drawn document encodes"
                  Expect.isEmpty (wires |> List.filter (evaluate schema >> not) |> List.truncate 5) "and validates"

              // One instantiation's value at another's slot: the interpreter refuses each, and so
              // does the schema. A schema that erased the parameter would admit every one.
              let document (i: int) =
                  let _, _, bytes = ParameterisedRecordsIdl.documents[i]
                  bytes

              let slider, stepper, grid = document 0, document 1, document 2

              for what, original, (find: string), (replace: string) in
                  [ "a string at Ranged<float>.low", slider, "\"low\":0.5,\"steps\"", "\"low\":\"0.5\",\"steps\""
                    "a fraction at Ranged<int>.high", stepper, "\"high\":9", "\"high\":9.5"
                    "a number at an inline LocalState<float>'s key", slider, "\"stateKey\":\"k\"", "\"stateKey\":1"
                    "a boolean at an inline Paging<string>.cursor", grid, "\"cursor\":\"c\"", "\"cursor\":true"
                    "no Direction at Entry<string, Direction>.value",
                    grid,
                    "\"value\":\"desc\"",
                    "\"value\":\"sideways\""
                    "a record of a record missing its inner member", stepper, "{\"high\":4,\"low\":2}", "{\"high\":4}" ] do
                  let wrong = original.Replace(find, replace)
                  Expect.notEqual wrong original (what + ": the document was changed")
                  Expect.isError (Decode.decode idl wrong) (what + ": the interpreter refuses")
                  Expect.isFalse (evaluate schema wrong) (what + ": the schema refuses")) ]

// ---------------------------------------------------------------------------
// Phase 732 — the NAME a definition stated once per instantiation is filed under
// (`Decl.instName`). Three properties are held here, over a family built to break each:
//
//   1. it is injective over what a type states on the wire: two instantiations share a name
//      exactly where they state one definition (they differ only in a hosted slot's host type or
//      codec, or in a function's signature);
//   2. no instantiation is named as a declaration is, nor as another entry of the schema;
//   3. it is a function of the type alone (true by construction: the function takes nothing else).
//
// The family holds declared names that read as something else (`a_b` beside `a` and `b`, a record
// named `list`, `tuple2` or `Box_int`), tuples that differ only in where they nest, and hosted
// slots that differ only in their format or their wire form. Every instantiation of it is then
// held in ONE vocabulary, whose schema must parse, close, and admit what the interpreter writes.
// ---------------------------------------------------------------------------

module private InstanceNames =
    open Fuaran.Core.Idl.Emit

    let field (name: string) (t: IdlType) (opt: Optionality) : IdlField =
        { Name = name
          Type = t
          Opt = opt
          Aliases = []
          Keyed = false
          Presence = None
          Annotations = Annotations.Empty }

    let record (name: string) (pars: string list) (fields: IdlField list) : IdlRecord =
        { Name = name
          Params = pars
          Fields = fields
          Annotations = Annotations.Empty
          Inline = false }

    let hosted (host: string) (wire: IdlType option) (format: HostedFormat option) : IdlType =
        THosted
            { FSharp = host
              Encode = "enc" + host
              Decode = "dec" + host
              Wire = wire
              Format = format
              Refusal = HostedRefusal.Sentence }

    let box (t: IdlType) : IdlType = TRecord("Box", [ t ])
    let pair (a: IdlType) (b: IdlType) : IdlType = TRecord("Pair", [ a; b ])
    let opt (t: IdlType) : IdlType = TUnion("Opt", [ t ])

    /// Plain records whose names read as a separator, an escape, a fixed word or an instantiation.
    let plainNames: string list =
        [ "a"
          "b"
          "c"
          "a_b"
          "b_c"
          "a_"
          "_b"
          "a__b"
          "a_0b"
          "a0b"
          "list"
          "int"
          "hosted"
          "hostedAs"
          "tuple2"
          "closure"
          "Node"
          "_1list"
          "_2T"
          "Box_int"
          "Box_a_b"
          "Box_list_int"
          "Pair_a_b"
          "Pair_int_str"
          "Opt_int" ]

    let enumNames: string list = [ "tone"; "to_ne"; "str" ]

    /// What a type states on the wire: a hosted slot is its wire form and format, and a function
    /// is the one sentinel. Written apart from `Decl.instName`, which is held to it.
    let rec wire (t: IdlType) : IdlType =
        match t with
        | THosted h -> hosted "" (h.Wire |> Option.map wire) h.Format
        | TFn _ -> TClosure
        | TList i -> TList(wire i)
        | TMap i -> TMap(wire i)
        | TSet i -> TSet(wire i)
        | TTuple cs -> TTuple(List.map wire cs)
        | TKeyedMap(k, v) -> TKeyedMap(wire k, wire v)
        | TRecord(n, args) -> TRecord(n, List.map wire args)
        | TUnion(n, args) -> TUnion(n, List.map wire args)
        | other -> other

    /// The arguments an instantiation is built at: every scalar, every declared name, hosted slots
    /// that differ in one part each, and tuples and collections that differ in their nesting.
    let leaves: IdlType list =
        [ TStr
          TInt
          TBool
          TFloat
          TJson
          TInt64
          TDecimal
          TBytes
          TTimeOfDay
          TDuration ]
        @ (plainNames |> List.map (fun n -> TRecord(n, [])))
        @ (enumNames |> List.map TEnum)
        @ [ hosted "A" None None
            hosted "B" None None
            hosted "A" (Some TStr) None
            hosted "B" (Some TStr) None
            hosted "A" (Some TStr) (Some HostedFormat.Date)
            hosted "A" (Some TStr) (Some HostedFormat.DateTime)
            hosted "A" (Some TStr) (Some HostedFormat.Uuid)
            hosted "A" (Some TInt) None
            hosted "A" (Some(box TInt)) None
            hosted "A" (Some(TList TStr)) None ]
        @ [ TTuple [ TInt; TStr ]
            TTuple [ TInt; TStr; TBool ]
            TTuple [ TTuple [ TInt; TStr ]; TBool ]
            TTuple [ TInt; TTuple [ TStr; TBool ] ]
            TTuple [ TInt; TTuple [ TStr; TBool ]; TFloat ]
            TTuple [ TInt; TTuple [ TStr; TBool; TFloat ] ]
            TList TInt
            TList(TList TInt)
            TMap TInt
            TMap(TList TInt)
            TSet TInt
            TKeyedMap(TInt, TStr)
            TKeyedMap(TInt64, TList TInt) ]

    /// A second level: a generic declaration applied to a leaf, as an argument.
    let nested: IdlType list =
        [ box TInt
          box (TRecord("int", []))
          box (TRecord("a", []))
          box (TList TInt)
          opt TInt
          pair TInt TStr
          pair (TRecord("a", [])) (TRecord("b", []))
          TList(box TInt) ]

    /// Every instantiation of the family: each generic declaration at every argument, and the
    /// two-parameter one at every pair of a selection that holds each ambiguity.
    let instantiations: IdlType list =
        let arguments = leaves @ nested

        let paired =
            [ TInt; TStr; TList TInt ]
            @ ([ "a"
                 "b"
                 "c"
                 "a_b"
                 "b_c"
                 "a_"
                 "_b"
                 "list"
                 "int"
                 "Box_int"
                 "Pair_a_b" ]
               |> List.map (fun n -> TRecord(n, [])))
            @ [ box TInt
                pair TInt TStr
                TTuple [ TInt; TStr ]
                hosted "A" (Some TStr) (Some HostedFormat.Date) ]

        (arguments |> List.collect (fun a -> [ box a; opt a ]))
        @ [ for x in paired do
                for y in paired do
                    pair x y ]
        |> List.distinct

    let private kindOf (members: (string * IdlType) list) : IdlKind =
        { Tag = "K"
          Category = "probe"
          Fields = members |> List.map (fun (n, t) -> field n t Optional)
          Annotations = Annotations.Empty }

    let private generics: IdlRecord list =
        [ record "Box" [ "T" ] [ field "item" (TVar "T") Required ]
          record "Pair" [ "A"; "B" ] [ field "x" (TVar "A") Required; field "y" (TVar "B") Required ] ]

    /// A vocabulary of the three generic holders over `records` and `enums`, one kind of `members`.
    let over (records: IdlRecord list) (enums: IdlEnum list) (members: (string * IdlType) list) : Idl =
        { Kinds = [ kindOf members ]
          Unions =
            [ { Name = "Opt"
                Params = [ "T" ]
                Cases =
                  [ { Tag = "Held"
                      Fields = [ field "v" (TVar "T") Required ]
                      Annotations = Annotations.Empty
                      Hosted = None }
                    { Tag = "Empty"
                      Fields = []
                      Annotations = Annotations.Empty
                      Hosted = None } ]
                Annotations = Annotations.Empty
                TagRule = UnionTagRule.CaseName
                Discrimination = UnionDiscrimination.Tagged } ]
          Enums = enums
          Records = generics @ records
          Defaults = []
          NodeFields = []
          Ops = []
          Wire = WireShape.Default
          Harden = HardenPolicy.Undeclared
          Contracts = [] }

    /// The family in one vocabulary: each instantiation an optional member of the one kind.
    let vocabulary: Idl =
        over
            (plainNames |> List.map (fun n -> record n [] [ field "n" TInt Required ]))
            (enumNames |> List.map (fun n -> Declare.enumOf n [ "p"; "q" ]))
            (instantiations |> List.mapi (fun i t -> sprintf "m%d" i, t))

    let name (t: IdlType) : string = Decl.instName t

    /// The declaration check's faults, as the sentences a failed expectation should show.
    let faults (idl: Idl) : string list =
        Declare.errors idl |> List.truncate 3 |> List.map _.Detail

    /// [[evaluate]] with a `format` asserted: the validator reads it as an annotation otherwise,
    /// and two hosted formats are then one schema.
    let admits (schema: JsonSchema) (wire: string) : bool =
        use doc = JsonDocument.Parse wire

        let options =
            EvaluationOptions(OutputFormat = OutputFormat.Flag, RequireFormatValidation = true)

        (schema.Evaluate(doc.RootElement, options)).IsValid

    let isWord (n: string) : bool = Decl.isNameWord n

[<Tests>]
let instanceNames =
    let names (ts: IdlType list) =
        ts |> List.map InstanceNames.name |> List.distinct

    let r (n: string) = TRecord(n, [])
    let formatted = InstanceNames.hosted "A" (Some TStr)

    let doc (m: string) (value: string) =
        sprintf """{"id":"n","kind":{"$type":"K","%s":%s}}""" m value

    testList
        "Phase 732 — a definition's name is injective, and never a declaration's"
        [ testCase "two instantiations share a name exactly where they state one definition" (fun _ ->
              let family =
                  InstanceNames.instantiations @ InstanceNames.leaves @ InstanceNames.nested

              Expect.isGreaterThan (List.length InstanceNames.instantiations) 300 "the family is not a handful"

              let byName = family |> List.groupBy InstanceNames.name
              let byWire = family |> List.groupBy InstanceNames.wire

              for name, group in byName do
                  Expect.equal
                      (group |> List.map InstanceNames.wire |> List.distinct |> List.length)
                      1
                      (sprintf "'%s' names one type on the wire" name)

              for _, group in byWire do
                  Expect.equal
                      (names group)
                      [ InstanceNames.name (List.head group) ]
                      "one type on the wire has one name"

              // The merge is met: the family holds types that differ only off the wire.
              Expect.isLessThan
                  (List.length byWire)
                  (List.length (List.distinct family))
                  "some of the family differ only in the host"

              Expect.equal
                  (InstanceNames.name (InstanceNames.box (InstanceNames.hosted "A" (Some TStr) None)))
                  (InstanceNames.name (InstanceNames.box (InstanceNames.hosted "B" (Some TStr) None)))
                  "a hosted slot is named by its wire form, whatever its host type")

          testCase "each pair of types the old spelling named alike has two names" (fun _ ->
              for what, one, other, expected in
                  [ "two hosted formats",
                    InstanceNames.box (formatted (Some HostedFormat.Date)),
                    InstanceNames.box (formatted (Some HostedFormat.Uuid)),
                    ("Box_hostedDate_str", "Box_hostedUuid_str")
                    "a hosted slot with and without a wire form",
                    InstanceNames.box (InstanceNames.hosted "A" None None),
                    InstanceNames.box (formatted None),
                    ("Box_hosted", "Box_hostedAs_str")
                    "a tuple's arity",
                    InstanceNames.box (TTuple [ TInt; TTuple [ TStr; TBool ]; TFloat ]),
                    InstanceNames.box (TTuple [ TInt; TTuple [ TStr; TBool; TFloat ] ]),
                    ("Box_tuple3_int_tuple2_str_bool_float", "Box_tuple2_int_tuple3_str_bool_float")
                    "a `_` in a declared name",
                    InstanceNames.pair (r "a_b") (r "c"),
                    InstanceNames.pair (r "a") (r "b_c"),
                    ("Pair_a_0b_c", "Pair_a_b_0c")
                    "an instantiation beside a declaration of its old name",
                    InstanceNames.box TInt,
                    r "Box_int",
                    ("Box_int", "Box_0int")
                    "a declaration named as a fixed word",
                    InstanceNames.pair (TList(r "list")) TInt,
                    InstanceNames.pair (r "list") (TList TInt),
                    ("Pair_list__1list_int", "Pair__1list_list_int")
                    "a leading and a trailing `_`",
                    InstanceNames.pair (r "a_") (r "b"),
                    InstanceNames.pair (r "a") (r "_b"),
                    ("Pair_a_0_b", "Pair_a__0b") ] do
                  Expect.equal (InstanceNames.name one, InstanceNames.name other) expected what

              // A type parameter is never a declaration either.
              Expect.equal (InstanceNames.name (InstanceNames.box (TVar "T"))) "Box__2T" "a parameter"
              Expect.equal (InstanceNames.name (InstanceNames.box (TEnum "T"))) "Box_T" "an enum of its name")

          testCase "a name that holds no `_` and is no fixed word is the declaration's own, as it always was" (fun _ ->
              for n in [ "Binding"; "SelectOption"; "K"; "a0b"; "Tuple2"; "tuple"; "tupleX"; "Hosted" ] do
                  Expect.isFalse (InstanceNames.isWord n) (n + " is no fixed word")
                  Expect.equal (InstanceNames.name (r n)) n n
                  Expect.equal (InstanceNames.name (TEnum n)) n (n + ", an enum")

              // Every word the name of a type that is not a declaration opens with is a fixed word.
              for t in InstanceNames.leaves @ [ TNode; TKind; TOp; TOpaque; TClosure; TTuple [] ] do
                  match t with
                  | TRecord _
                  | TEnum _ -> ()
                  | _ ->
                      let word = (InstanceNames.name t).Split('_')[0]
                      Expect.isTrue (InstanceNames.isWord word) (sprintf "'%s' is a fixed word" word))

          testCase "no instantiation is named as a declaration or as another entry of the schema is" (fun _ ->
              let declared =
                  (InstanceNames.plainNames |> List.map r)
                  @ (InstanceNames.enumNames |> List.map TEnum)
                  @ [ r "K"; TNode; TKind; TOp ]
                  |> List.map InstanceNames.name

              Expect.equal (List.distinct declared) declared "the declarations' own names are distinct"

              let instantiated = names InstanceNames.instantiations

              Expect.isEmpty
                  (Set.intersect (Set.ofList declared) (Set.ofList instantiated) |> Set.toList)
                  "no instantiation's name is a declaration's"

              for n in declared @ instantiated do
                  Expect.isTrue
                      (n
                       |> Seq.forall (fun c ->
                           (c >= 'a' && c <= 'z')
                           || (c >= 'A' && c <= 'Z')
                           || (c >= '0' && c <= '9')
                           || c = '_'))
                      (sprintf "'%s' needs no escape in a `$ref`" n))

          testCase "the family in one vocabulary: the schema parses, closes, and has one entry a name" (fun _ ->
              let idl = InstanceNames.vocabulary
              Expect.equal (InstanceNames.faults idl) [] "the vocabulary is well-formed"

              let text = schemaOf idl
              // `Json.parse` refuses a member name repeated in one object.
              let defs = parsed text |> defsOf |> Map.keys |> Set.ofSeq
              assertClosed "the family" text

              for t in InstanceNames.instantiations do
                  Expect.isTrue (defs.Contains(InstanceNames.name t)) (InstanceNames.name t + " is defined")

              for n in InstanceNames.plainNames do
                  Expect.isTrue (defs.Contains(InstanceNames.name (r n))) (sprintf "the record '%s' is defined" n)

              let schema = build text

              let written =
                  match Sample.trySampleNodes idl [ "K" ] 732 12 with
                  | Error fault -> failtestf "the sampler faulted: %s" fault.Describe
                  | Ok nodes ->
                      nodes
                      |> List.map (fun v ->
                          match Encode.encode idl v with
                          | Ok bytes -> bytes
                          | Error e -> failtestf "the interpreter refused its own sample: %s" e)

              Expect.isGreaterThan (written |> List.distinct |> List.length) 6 "documents were drawn"

              for d in written do
                  Expect.isTrue
                      (InstanceNames.admits schema d)
                      (sprintf "the schema admits %s" (d.Substring(0, min 120 d.Length))))

          testCase
              "each such pair in one vocabulary: a document is admitted at its own member and refused at the other"
              (fun _ ->
                  let plain (n: string) =
                      InstanceNames.record n [] [ InstanceNames.field "other" TStr Required ]

                  for what, idl, one, other in
                      [ "two hosted formats",
                        InstanceNames.over
                            []
                            []
                            [ "p", InstanceNames.box (formatted (Some HostedFormat.Date))
                              "q", InstanceNames.box (formatted (Some HostedFormat.Uuid)) ],
                        """{"item":"2024-01-02"}""",
                        """{"item":"123e4567-e89b-12d3-a456-426614174000"}"""
                        "a tuple's arity",
                        InstanceNames.over
                            []
                            []
                            [ "p", InstanceNames.box (TTuple [ TInt; TTuple [ TStr; TBool ]; TFloat ])
                              "q", InstanceNames.box (TTuple [ TInt; TTuple [ TStr; TBool; TFloat ] ]) ],
                        """{"item":[1,["a",true],0.5]}""",
                        """{"item":[1,["a",true,0.5]]}"""
                        "a `_` in a declared name",
                        InstanceNames.over
                            []
                            [ Declare.enumOf "a_b" [ "ab" ]
                              Declare.enumOf "c" [ "c" ]
                              Declare.enumOf "a" [ "a" ]
                              Declare.enumOf "b_c" [ "bc" ] ]
                            [ "p", InstanceNames.pair (TEnum "a_b") (TEnum "c")
                              "q", InstanceNames.pair (TEnum "a") (TEnum "b_c") ],
                        """{"x":"ab","y":"c"}""",
                        """{"x":"a","y":"bc"}"""
                        "an instantiation beside a declaration of its old name",
                        InstanceNames.over
                            [ plain "Box_float" ]
                            []
                            [ "p", InstanceNames.box TFloat; "q", r "Box_float" ],
                        """{"item":1.5}""",
                        """{"other":"o"}"""
                        "an instantiation beside one over a declaration of its argument's old name",
                        InstanceNames.over
                            [ plain "Box_int" ]
                            []
                            [ "p", InstanceNames.pair (InstanceNames.box TInt) TInt
                              "q", InstanceNames.pair (r "Box_int") TInt ],
                        """{"x":{"item":1},"y":2}""",
                        """{"x":{"other":"o"},"y":2}""" ] do
                      Expect.equal (InstanceNames.faults idl) [] (what + ": well-formed")
                      let text = schemaOf idl
                      assertClosed what text
                      let schema = build text

                      for m, own, foreign in [ "p", one, other; "q", other, one ] do
                          Expect.equal
                              (Decode.decode idl (doc m own) |> Result.bind (Encode.encode idl))
                              (Ok(doc m own))
                              (sprintf "%s: the interpreter reads and re-writes `%s` at %s" what own m)

                          Expect.isTrue
                              (InstanceNames.admits schema (doc m own))
                              (sprintf "%s: `%s` is admitted at %s" what own m)

                          Expect.isError
                              (Decode.decode idl (doc m foreign))
                              (sprintf "%s: the interpreter refuses `%s` at %s" what foreign m)

                          Expect.isFalse
                              (InstanceNames.admits schema (doc m foreign))
                              (sprintf "%s: `%s` is refused at %s" what foreign m))

          testCase
              "a kind, an op and a union whose names hold `_` or are fixed words are filed and referenced under one spelling"
              (fun _ ->
                  let plainUnion (name: string) : IdlUnion =
                      { Name = name
                        Params = []
                        Cases =
                          [ { Tag = "One"
                              Fields = [ InstanceNames.field "n" TInt Required ]
                              Annotations = Annotations.Empty
                              Hosted = None } ]
                        Annotations = Annotations.Empty
                        TagRule = UnionTagRule.CaseName
                        Discrimination = UnionDiscrimination.Tagged }

                  let kindNamed (tag: string) : IdlKind =
                      { Tag = tag
                        Category = "probe"
                        Fields = [ InstanceNames.field "n" TInt Required ]
                        Annotations = Annotations.Empty }

                  let base' =
                      InstanceNames.over [] [] [ "u", TUnion("my_union", []); "w", TUnion("list", []) ]

                  let idl =
                      { base' with
                          Kinds = base'.Kinds @ [ kindNamed "my_kind"; kindNamed "map" ]
                          Ops = [ kindNamed "my_op"; kindNamed "set" ]
                          Unions = base'.Unions @ [ plainUnion "my_union"; plainUnion "list" ] }

                  Expect.equal (InstanceNames.faults idl) [] "well-formed"
                  let text = schemaOf idl
                  // A definition filed under one spelling and referenced under another is a dangling
                  // `$ref`, and the definition an orphan.
                  assertClosed "declared names" text
                  let defs = parsed text |> defsOf |> Map.keys |> Set.ofSeq

                  for n in [ "my_0kind"; "_1map"; "my_0op"; "_1set"; "my_0union"; "_1list" ] do
                      Expect.isTrue (defs.Contains n) (n + " is defined"))

          testCase "an instantiation named only in a hosted slot's wire form has its definition" (fun _ ->
              let slot (wire: IdlType) =
                  InstanceNames.hosted "A" (Some wire) None

              let idl =
                  InstanceNames.over
                      []
                      []
                      [ "h", slot (InstanceNames.box TInt)
                        "g", InstanceNames.box (slot (InstanceNames.box TStr)) ]

              Expect.equal (InstanceNames.faults idl) [] "well-formed"
              let text = schemaOf idl
              assertClosed "a hosted wire form" text
              let defs = parsed text |> defsOf |> Map.keys |> Set.ofSeq

              Expect.equal
                  (Set.intersect defs (set [ "Box_int"; "Box_str"; "Box_hostedAs_Box_str" ]))
                  (set [ "Box_int"; "Box_str"; "Box_hostedAs_Box_str" ])
                  "the instantiation in each wire form, and the holder named by it"

              let schema = build text

              for m, good, bad in
                  [ "h", """{"item":1}""", """{"item":"x"}"""
                    "g", """{"item":{"item":"x"}}""", """{"item":{"item":1}}""" ] do
                  Expect.equal
                      (Decode.decode idl (doc m good) |> Result.bind (Encode.encode idl))
                      (Ok(doc m good))
                      "the interpreter reads the wire form"

                  Expect.isTrue (InstanceNames.admits schema (doc m good)) (good + " is admitted")
                  Expect.isError (Decode.decode idl (doc m bad)) "the interpreter refuses another type"
                  Expect.isFalse (InstanceNames.admits schema (doc m bad)) (bad + " is refused")) ]
