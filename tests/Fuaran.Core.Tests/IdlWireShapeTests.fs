module Fuaran.Core.Tests.IdlWireShapeTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Phases 108/109 — the declared wire shape, exercised on the two axes the
// readiness spikes do NOT cover: the spikes both declare `kind` + flat (the
// shape both foreign vocabularies chose), so this file pins the axes
// INDEPENDENTLY — a custom discriminator under the NESTED envelope, and the
// FLAT envelope under the default `$type` key — plus the declaration-level
// validation, the artifact key, and the diff classification.
// ---------------------------------------------------------------------------

let private f (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Annotations = Annotations.Empty }

let private baseIdl: Idl =
    { Kinds =
        [ { Tag = "Note"
            Category = "leaf"
            Fields = [ f "label" TStr Required; f "src" (TUnion("Src", [])) Optional ] } ]
      Unions =
        [ { Name = "Src"
            Params = []
            Cases =
              [ { Tag = "Lit"
                  Fields = [ f "value" TStr Required ]
                  Annotations = Annotations.Empty }
                { Tag = "Ref"
                  Fields = [ f "target" TStr Required ]
                  Annotations = Annotations.Empty } ] } ]
      Enums = []
      Records = []
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      Harden = HardenPolicy.Default }

let private authored =
    VNode("a", "Note", [ "label", VStr "x"; "src", VUnion("Lit", [ "value", VStr "y" ]) ])

let private roundTrip (idl: Idl) =
    match Encode.encode idl authored with
    | Error e -> failtestf "encode: %s" e
    | Ok bytes ->
        match Decode.decode idl bytes with
        | Error e -> failtestf "decode: %s" e
        | Ok value ->
            match Encode.encode idl value with
            | Error e -> failtestf "re-encode: %s" e
            | Ok again ->
                Expect.equal again bytes "round-trip is byte-stable"
                bytes

[<Tests>]
let tests =
    testList
        "IDL wire shape (Phases 108/109)"
        [

          test "a custom discriminator under the NESTED envelope round-trips — the key axis alone" {
              let idl =
                  { baseIdl with
                      Wire =
                          { WireShape.Default with
                              Discriminator = "tag"
                              NodeEnvelope = NodeEnvelopeShape.NestedKind } }

              let bytes = roundTrip idl
              Expect.isTrue (bytes.Contains "\"tag\":\"Note\"") "the declared key tags the kind body"
              Expect.isTrue (bytes.Contains "\"tag\":\"Lit\"") "the declared key tags the union case too"
              Expect.isFalse (bytes.Contains "$type") "nothing is $type-tagged"
              Expect.isTrue (bytes.Contains "\"kind\":{") "the envelope stays nested — the axes are independent"

              // Go-red partner: the same bytes under the default key are refused.
              Expect.isTrue
                  (Result.isError (Decode.decode baseIdl bytes))
                  "the default-shape declaration refuses the re-keyed wire"
          }

          test "the FLAT envelope under the DEFAULT key round-trips — the envelope axis alone" {
              let idl =
                  { baseIdl with
                      Wire =
                          { WireShape.Default with
                              Discriminator = "$type"
                              NodeEnvelope = NodeEnvelopeShape.FlatKind } }

              let bytes = roundTrip idl
              Expect.isTrue (bytes.Contains "\"$type\":\"Note\"") "the default key tags the flat node"
              Expect.isFalse (bytes.Contains "\"kind\"") "no nested kind member exists"

              Expect.isTrue
                  (Result.isError (Decode.decode baseIdl bytes))
                  "the default-shape declaration refuses the flat wire"
          }

          test "the default shape is the default — an unshaped declaration is byte-identical to Canon.typed" {
              let bytes = roundTrip baseIdl
              Expect.isTrue (bytes.Contains "\"kind\":{\"$type\":\"Note\"") "nested $type, exactly as before"
          }

          test "wireShapeErrors refuses the reserved and unspellable keys" {
              let shaped disc env =
                  { baseIdl with
                      Wire =
                          { WireShape.Default with
                              Discriminator = disc
                              NodeEnvelope = env } }

              Expect.isEmpty (Declare.wireShapeErrors baseIdl) "the default shape is well-formed"

              Expect.isNonEmpty
                  (Declare.wireShapeErrors (shaped "" NodeEnvelopeShape.NestedKind))
                  "an empty discriminator is refused"

              Expect.isNonEmpty
                  (Declare.wireShapeErrors (shaped "id" NodeEnvelopeShape.NestedKind))
                  "the key 'id' collides with the node id"

              Expect.isNonEmpty
                  (Declare.wireShapeErrors (shaped "a\"b" NodeEnvelopeShape.NestedKind))
                  "a quote-bearing key cannot be spliced into generated source"

              Expect.isNonEmpty
                  (Declare.wireShapeErrors (shaped "label" NodeEnvelopeShape.NestedKind))
                  "a key colliding with a declared field name is refused"

              // Flat only: a kind field named `id` shares the node's own object.
              let withIdField =
                  { baseIdl with
                      Kinds =
                          [ { Tag = "Note"
                              Category = "leaf"
                              Fields = [ f "id" TStr Required ] } ]
                      Wire =
                          { WireShape.Default with
                              Discriminator = "tag"
                              NodeEnvelope = NodeEnvelopeShape.FlatKind } }

              Expect.isNonEmpty (Declare.wireShapeErrors withIdField) "a flat kind field named 'id' is refused"

              Expect.isEmpty
                  (Declare.wireShapeErrors
                      { withIdField with
                          Wire =
                              { WireShape.Default with
                                  Discriminator = "tag"
                                  NodeEnvelope = NodeEnvelopeShape.NestedKind } })
                  "the same field is legal under the nested envelope — the reservation is flat-only"
          }

          test "the artifact carries the shape only when it is not the default" {
              let plain = Artifact.render baseIdl
              Expect.isFalse (plain.Contains "\"wire\"") "a default-shape artifact is byte-for-byte what it was"

              let shaped =
                  Artifact.render
                      { baseIdl with
                          Wire =
                              { WireShape.Default with
                                  Discriminator = "tag"
                                  NodeEnvelope = NodeEnvelopeShape.FlatKind } }

              Expect.isTrue (shaped.Contains "\"wire\"") "a shaped artifact declares its wire key"
              Expect.isTrue (shaped.Contains "\"discriminator\": \"tag\"") "…the discriminator"
              Expect.isTrue (shaped.Contains "\"nodeEnvelope\": \"flatKind\"") "…and the envelope"
              Expect.isTrue (shaped.Contains "\"keyOrder\": \"sorted\"") "…and the key order (Phase 111)"
          }

          test "DECLARED key order preserves declaration order — and re-encode normalises to it" {
              // Fields deliberately declared in REVERSE alphabetical order, so the
              // two orderings are distinguishable on the wire.
              let idl =
                  { baseIdl with
                      Kinds =
                          [ { Tag = "Note"
                              Category = "leaf"
                              Fields = [ f "zeta" TStr Required; f "alpha" TStr Required ] } ]
                      Unions = []
                      Wire =
                          { WireShape.Default with
                              KeyOrder = KeyOrder.Declared } }

              let node = VNode("a", "Note", [ "zeta", VStr "z"; "alpha", VStr "y" ])

              let bytes =
                  match Encode.encode idl node with
                  | Ok s -> s
                  | Error e -> failtestf "encode: %s" e

              Expect.isTrue
                  (bytes.Contains "\"$type\":\"Note\",\"zeta\":\"z\",\"alpha\":\"y\"")
                  "the kind body lays its keys in DECLARATION order — discriminator first, zeta before alpha"

              // The sorted variant produces DIFFERENT bytes — the axis is load-bearing.
              match Encode.encode { idl with Wire = WireShape.Default } node with
              | Ok sortedBytes -> Expect.notEqual sortedBytes bytes "Sorted and Declared orders differ on this kind"
              | Error e -> failtestf "sorted encode: %s" e

              // Re-encode NORMALISES: a document authored in any key order decodes
              // and re-encodes to the declared order, so canonical form is unique.
              let shuffled = """{"kind":{"alpha":"y","zeta":"z","$type":"Note"},"id":"a"}"""

              match Decode.decode idl shuffled with
              | Error e -> failtestf "decode of shuffled input: %s" e
              | Ok v ->
                  match Encode.encode idl v with
                  | Ok again -> Expect.equal again bytes "re-encode of a shuffled document normalises to declared order"
                  | Error e -> failtestf "re-encode: %s" e
          }

          test "a wire-shape change diffs as BREAKING (wire)" {
              let before =
                  match Diff.parse (Artifact.render baseIdl) with
                  | Ok s -> s
                  | Error e -> failtestf "before: %s" e

              let after =
                  match
                      Diff.parse (
                          Artifact.render
                              { baseIdl with
                                  Wire =
                                      { WireShape.Default with
                                          Discriminator = "tag"
                                          NodeEnvelope = NodeEnvelopeShape.FlatKind } }
                      )
                  with
                  | Ok s -> s
                  | Error e -> failtestf "after: %s" e

              Expect.equal before.Wire "$type/nestedKind/sorted" "a wire-less artifact reads as the default shape"
              Expect.equal after.Wire "tag/flatKind/sorted" "the shaped artifact reads back"

              let shapeChanges =
                  Diff.changes before after
                  |> List.map Diff.classify
                  |> List.filter (fun c ->
                      match c.Change with
                      | Diff.WireShapeChanged _ -> true
                      | _ -> false)

              match shapeChanges with
              | [ c ] -> Expect.equal c.Severity Diff.BreakingWire "a shape move is a breaking wire event"
              | other -> failtestf "expected exactly one WireShapeChanged, got %A" other
          }

          test "the generated legs carry the declared shape through — schema, F# and TS surfaces" {
              let idl =
                  { baseIdl with
                      Wire =
                          { WireShape.Default with
                              Discriminator = "tag"
                              NodeEnvelope = NodeEnvelopeShape.FlatKind } }

              let schema = Gen.jsonSchema idl
              Expect.isTrue (schema.Contains "\"tag\"") "the schema's const key is the declared discriminator"
              Expect.isFalse (schema.Contains "$type") "no $type appears in a shaped schema"

              match Gen.fsharpModule "Shaped.Test" idl [ "Note" ] with
              | Error e -> failtestf "fsharp emitter: %A" e
              | Ok src ->
                  Expect.isTrue (src.Contains "typedTag") "the F# module carries the declared-key helper"
                  Expect.isFalse (src.Contains "Canon.typed \"") "no default-keyed emission remains"

              let ts = Gen.typescriptModule idl [ "Note" ]
              Expect.isTrue (ts.Contains "'tag' in j") "the TS module's isTagged tests the declared key"
              Expect.isFalse (ts.Contains "$type") "no $type appears in a shaped TS module"
          } ]
