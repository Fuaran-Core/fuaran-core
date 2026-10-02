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
        [ for c in IdlThreeHostTests.certificationWires () do
              vocabularyCase c

          testCase "reference: every drawn op validates against the two-rooted schema" (fun _ ->
              let idl, ops = IdlThreeHostTests.certificationOps ()
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
