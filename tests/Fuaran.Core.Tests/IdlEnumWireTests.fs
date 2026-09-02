module Fuaran.Core.Tests.IdlEnumWireTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Phase 707 — enum case-name ↔ wire-string mapping.
//
// An `IdlEnum`'s case name USED to be its wire string, which made any closed set
// whose wire values are lower-case or hyphenated unmodellable as a `TEnum`: the
// UI vocabulary left `liveRegion` out of the enum model on exactly those grounds,
// and no other domain's wire values are obliged to respect F# case-name syntax
// either. `Wires` splits the two, positionally parallel to `Cases`, empty when
// they coincide.
//
// The invariant that matters is that ONE representation crosses each boundary:
// `IdlValue`'s `VEnum` carries the WIRE string — as `VUnion` carries the wire
// `$type` tag and `VNode` the wire kind tag — so the interpreter, the schema, the
// sampler and the TypeScript backend all read it unchanged, and the F# emitter is
// the single leg that maps back to the declared identifier. These tests pin all
// five legs against one enum that declares a mapping and one that does not, so a
// leg that silently reverts to "case name IS the wire string" fails here rather
// than in a host's corpus run.
// ---------------------------------------------------------------------------

/// Lower-case wire strings that are not legal F# case names — the `liveRegion`
/// shape that motivated the split.
let private liveRegion =
    Declare.enumWith "LiveRegion" [ "Off", "off"; "Polite", "polite"; "Assertive", "assertive" ]

/// The identity shape every declaration had before this phase.
let private tone = Declare.enumOf "Tone" [ "Default"; "Brand"; "Critical" ]

let private idl: Idl =
    { Kinds =
        [ { Tag = "Note"
            Category = "display"
            Fields =
              [ { Name = "live"
                  Type = TEnum "LiveRegion"
                  Opt = Required
                  Annotations = Annotations.Empty }
                { Name = "tone"
                  Type = TEnum "Tone"
                  Opt = OmitDefault(VEnum "Default")
                  Annotations = Annotations.Empty } ] } ]
      Unions = []
      Enums = [ liveRegion; tone ]
      Records = []
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      Harden = HardenPolicy.Default }

let private note (live: string) (tone: string) =
    VNode("n1", "Note", [ "live", VEnum live; "tone", VEnum tone ])

let private emitted =
    match Gen.fsharpModule "T" idl [ "Note" ] with
    | Ok src -> src
    | Error e -> failwithf "codegen failed: %A" e

let private ts = Gen.typescriptModule idl [ "Note" ]

[<Tests>]
let tests =
    testList
        "Phase 707 — enum case↔wire mapping"
        [

          // ── the declaration surface ──────────────────────────────────────

          testCase "enumWith splits case names from wire strings, positionally" (fun _ ->
              Expect.equal liveRegion.Cases [ "Off"; "Polite"; "Assertive" ] "host case names"
              Expect.equal liveRegion.Wires [ "off"; "polite"; "assertive" ] "wire strings")

          testCase "enumOf leaves Wires empty — case name IS the wire string" (fun _ ->
              Expect.equal tone.Wires [] "an unmapped enum declares no wire strings"
              Expect.equal tone.WireCases tone.Cases "so its wire cases are its case names"
              Expect.equal (tone.WireOf "Brand") "Brand" "identity in one direction"
              Expect.equal (tone.CaseOf "Brand") (Some "Brand") "and the other")

          testCase "the mapping is invertible in both directions" (fun _ ->
              for case, wire in List.zip liveRegion.Cases liveRegion.Wires do
                  Expect.equal (liveRegion.WireOf case) wire (sprintf "%s → wire" case)
                  Expect.equal (liveRegion.CaseOf wire) (Some case) (sprintf "%s → case" wire))

          testCase "CaseOf rejects a string outside the closed set" (fun _ ->
              // The host case name is NOT a wire string once a mapping is declared —
              // this is the confusion the whole split exists to make impossible.
              Expect.equal (liveRegion.CaseOf "Polite") None "a case name is not a wire string"
              Expect.equal (liveRegion.CaseOf "shouty") None "nor is an unrelated string")

          // ── the well-formedness backstop ─────────────────────────────────

          testCase "a well-formed IDL reports no enum-wire errors" (fun _ ->
              Expect.isEmpty (Declare.enumWireErrors idl) "both enums are well-formed")

          testCase "enumWireErrors catches a hand-built record with mismatched arity" (fun _ ->
              // `enumWith` cannot express this — it takes pairs. A record literal can.
              let broken =
                  { idl with
                      Enums =
                          [ { Name = "Bad"
                              Cases = [ "A"; "B" ]
                              Wires = [ "a" ] } ] }

              let errs = Declare.enumWireErrors broken
              Expect.hasLength errs 1 "one finding"
              Expect.stringContains errs[0] "parallel" "names the invariant")

          testCase "enumWireErrors catches a non-invertible mapping" (fun _ ->
              let broken =
                  { idl with
                      Enums =
                          [ { Name = "Bad"
                              Cases = [ "A"; "B" ]
                              Wires = [ "x"; "x" ] } ] }

              let errs = Declare.enumWireErrors broken
              Expect.hasLength errs 1 "one finding"
              Expect.stringContains errs[0] "invertible" "names the consequence")

          testCase "enumWireErrors catches a duplicate case name" (fun _ ->
              let broken =
                  { idl with
                      Enums = [ Declare.enumOf "Bad" [ "A"; "A" ] ] }

              Expect.hasLength (Declare.enumWireErrors broken) 1 "one finding")

          // ── leg 1: the schema-driven interpreter ─────────────────────────

          testCase "encode: a VEnum carrying the wire string encodes verbatim" (fun _ ->
              match Encode.encode idl (note "polite" "Brand") with
              | Ok wire ->
                  Expect.stringContains wire "\"live\":\"polite\"" "the mapped enum emits its wire string"
                  Expect.stringContains wire "\"tone\":\"Brand\"" "the unmapped enum is unaffected"
              | Error e -> failtestf "encode failed: %s" e)

          testCase "encode: a VEnum carrying the CASE name is rejected, not silently emitted" (fun _ ->
              // The failure this guards is the quiet one: emitting `"Polite"` onto a
              // wire whose closed set is lower-case produces bytes no conformant host
              // accepts, and nothing local would have complained.
              match Encode.encode idl (note "Polite" "Brand") with
              | Ok wire -> failtestf "expected rejection, got %s" wire
              | Error e -> Expect.stringContains e "Polite" "the error names the offending value")

          testCase "decode: a wire string decodes back to itself" (fun _ ->
              match Encode.encode idl (note "assertive" "Critical") with
              | Error e -> failtestf "encode failed: %s" e
              | Ok wire ->
                  match Decode.decode idl wire with
                  | Error e -> failtestf "decode failed: %s" e
                  | Ok decoded -> Expect.equal decoded (note "assertive" "Critical") "round-trips as the wire form")

          testCase "decode: a host case name on the wire is rejected" (fun _ ->
              let hostile =
                  """{"id":"n1","kind":{"$type":"Note","live":"Polite","tone":"Brand"}}"""

              match Decode.decode idl hostile with
              | Ok v -> failtestf "expected rejection, decoded %A" v
              | Error e -> Expect.stringContains e "Polite" "the error names the offending value")

          // ── leg 2: the F# emitter — the one leg that maps BACK ───────────

          testCase "F# emitter: the DU declares case NAMES" (fun _ ->
              Expect.stringContains emitted "| Off" "the F# identifier, not the wire string"
              Expect.stringContains emitted "| Polite" "the F# identifier, not the wire string")

          testCase "F# emitter: the encoder maps each case to its wire string" (fun _ ->
              Expect.stringContains emitted "| LiveRegion.Polite -> JStr \"polite\"" "mapped enum"
              Expect.stringContains emitted "| Tone.Brand -> JStr \"Brand\"" "unmapped enum unchanged")

          testCase "F# emitter: the decoder matches the wire string" (fun _ ->
              Expect.stringContains emitted "| JStr \"polite\" -> Ok LiveRegion.Polite" "mapped enum"
              Expect.stringContains emitted "| JStr \"Brand\" -> Ok Tone.Brand" "unmapped enum unchanged")

          testCase "F# emitter: an omit-at-default literal is the CASE name" (fun _ ->
              // `OmitDefault (VEnum "Default")` is authored in wire form like every
              // other `IdlValue`; the emitted F# comparison must be `Tone.Default`,
              // the identifier — this is where the map-back actually bites.
              Expect.stringContains emitted "Tone.Default" "the default renders as an F# case")

          // ── leg 3: the JSON-schema leg ───────────────────────────────────

          testCase "schema: the enum array lists WIRE strings" (fun _ ->
              let schema = Gen.jsonSchema idl
              Expect.stringContains schema "\"off\"" "the wire string is the validation contract"
              Expect.stringContains schema "\"assertive\"" "the wire string is the validation contract"

              Expect.isFalse (schema.Contains "\"Assertive\"") "the host case name is NOT a schema value")

          // ── leg 4: the TypeScript backend ────────────────────────────────

          testCase "TS: the decoder's case list is the wire strings" (fun _ ->
              Expect.stringContains ts "dEnum(\"LiveRegion\", [\"off\", \"polite\", \"assertive\"])" "mapped enum"
              Expect.stringContains ts "dEnum(\"Tone\", [\"Default\", \"Brand\", \"Critical\"])" "unmapped enum")

          testCase "TS: the omit-at-default test compares the wire string" (fun _ ->
              // TS holds an enum AS its wire string — there is no second
              // representation on that side, which is why it needs no map-back.
              Expect.stringContains ts "\"Default\"" "the default is compared in wire form")

          // ── leg 5: the sampler ───────────────────────────────────────────

          testCase "sampler: every sampled enum value is a wire string" (fun _ ->
              let wires = set liveRegion.Wires

              let sampled =
                  [ for seed in 1..40 do
                        for v in Sample.sampleNodes idl [ "Note" ] seed 1 do
                            match v with
                            | VNode(_, _, fields) ->
                                match fields |> List.tryFind (fun (n, _) -> n = "live") with
                                | Some(_, VEnum s) -> s
                                | _ -> failtest "sampled node lost its enum field"
                            | _ -> failtest "sampler produced a non-node" ]

              Expect.isNonEmpty sampled "the sampler produced values"

              for s in sampled do
                  Expect.isTrue (wires.Contains s) (sprintf "sampled '%s' is not a declared wire string" s)

              // …and the sample actually encodes, which a case-name leak would not.
              for seed in 1..40 do
                  for v in Sample.sampleNodes idl [ "Note" ] seed 1 do
                      match Encode.encode idl v with
                      | Ok _ -> ()
                      | Error e -> failtestf "a sampled node failed to encode: %s" e)

          // ── the artifact ─────────────────────────────────────────────────

          testCase "artifact: a mapped enum records both faces, an unmapped one only the wire" (fun _ ->
              let text = Artifact.render idl
              Expect.stringContains text "\"hostCases\"" "the mapped enum carries its host identifiers"

              // The identity case must add NOTHING, or every pre-707 artefact's
              // bytes would move — which is the acceptance criterion this pins.
              let toneOnly = Artifact.render { idl with Enums = [ tone ] }

              Expect.isFalse (toneOnly.Contains "hostCases") "an unmapped enum adds no host-surface key") ]
