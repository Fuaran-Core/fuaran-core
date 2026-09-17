module Fuaran.Core.Tests.IdlCodegenRefusalTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Phase 195 — the codegen's refusals are DATA.
//
// Guiding principle 3 says every rejection is a typed envelope naming the failure
// and the valid alternatives, never an exception. The IDL generator broke it in
// thirteen places: a required node-envelope member, an op-vocabulary slot in each
// of the five recursive emitters, a HostOnly field that declared no placeholder, a
// transparent union case of the wrong arity in three backends, an unbounded
// generic-instantiation walk, and one "unreachable" arm in the proposal spike.
// Each crashed the generator with a sentence; every other refusal in the same file
// was already a `CodegenError`.
//
// These are the cases that used to throw. Each one asserts the SHAPE of the
// refusal (which typed case, naming what), not merely that something went wrong —
// a test that only asserted `Error` would pass against a single catch-all case and
// so would not hold the generator to GP5's "the refusal names the construct, and
// thereby the set that IS supported".
//
// The regression direction is covered too, and it is the half a refusal test can
// silently lose: a generator that refuses EVERYTHING passes every case below. So
// the required-envelope pair is stated both ways — WITH a declared default it must
// EMIT, and the emitted constructor must carry the default it was given.
// ---------------------------------------------------------------------------

let private f (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Annotations = Annotations.Empty }

/// A minimal one-kind vocabulary. Each test below perturbs exactly one thing about
/// it, so a refusal is attributable to that perturbation and to nothing else.
let private baseIdl: Idl =
    { Kinds =
        [ { Tag = "Note"
            Category = "leaf"
            Annotations = Annotations.Empty
            Fields = [ f "label" TStr Required ] } ]
      Unions = []
      Enums = []
      Records = []
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      Harden = HardenPolicy.Default }

let private kinds = [ "Note" ]

let private emitFs (idl: Idl) =
    Gen.fsharpModule "Refusal.Probe" idl kinds

/// The vocabulary is fine and the generator must say so — the control every
/// refusal case below is measured against.
let private expectEmits (what: string) (r: Result<string, CodegenError>) : string =
    match r with
    | Ok src -> src
    | Error e -> failtestf "%s was refused, and should not have been: %s" what (CodegenError.describe e)

let private expectRefusal (what: string) (r: Result<string, CodegenError>) : CodegenError =
    match r with
    | Error e -> e
    | Ok _ -> failtestf "%s emitted a module where a typed refusal was required" what

[<Tests>]
let tests =
    testList
        "Phase 195 — codegen refusals are data"
        [
          // ── the node envelope ────────────────────────────────────────────

          testCase "a Required envelope member with NO declared default is refused as data" (fun _ ->
              let idl =
                  { baseIdl with
                      NodeFields = [ f "state" TStr Required ] }

              match expectRefusal "a Required envelope member with no default" (emitFs idl) with
              | CodegenError.RequiredEnvelopeField(field, ty, alternative) ->
                  Expect.equal field "state" "the refusal names the member"
                  Expect.equal ty TStr "and its declared type"

                  Expect.isTrue
                      (alternative.Contains "Optional" && alternative.Contains "default")
                      "and the alternatives an author can take"
              | other -> failtestf "expected RequiredEnvelopeField, got %A" other)

          testCase "a Required envelope member WITH a declared default is emitted, not refused" (fun _ ->
              // The shape the full node envelope needs. The default is addressed by the
              // EMPTY `IdlDefault.Kind`: the envelope has no kind tag, and a kind's tag is
              // its wire discriminator, so the empty address can name nothing else.
              let idl =
                  { baseIdl with
                      NodeFields = [ f "state" TStr Required ]
                      Defaults =
                          [ { Kind = ""
                              Field = "state"
                              Value = VStr "idle" } ] }

              let src =
                  expectEmits "a Required envelope member with a declared default" (emitFs idl)

              Expect.stringContains
                  src
                  "; State = \"idle\""
                  "the smart constructor fills the envelope member with its declared default"

              Expect.stringContains src "State: string" "and the member is a plain (non-option) field, like Required")

          testCase "the envelope default is addressed by kind, so a KIND's default does not fill it" (fun _ ->
              // The discriminating case: a default declared against the KIND rather than the
              // envelope must NOT satisfy the envelope member, or the address means nothing.
              let idl =
                  { baseIdl with
                      NodeFields = [ f "state" TStr Required ]
                      Defaults =
                          [ { Kind = "Note"
                              Field = "state"
                              Value = VStr "idle" } ] }

              match expectRefusal "a kind-addressed default standing in for an envelope one" (emitFs idl) with
              | CodegenError.RequiredEnvelopeField(field, _, _) ->
                  Expect.equal field "state" "the refusal still stands"
              | other -> failtestf "expected RequiredEnvelopeField, got %A" other)

          // ── the op vocabulary, in every backend that meets it ────────────

          testCase "an op-vocabulary slot is refused as data by the F# module emitter" (fun _ ->
              let idl =
                  { baseIdl with
                      Kinds =
                          [ { Tag = "Note"
                              Category = "leaf"
                              Annotations = Annotations.Empty
                              Fields = [ f "label" TStr Required; f "op" TOp Required ] } ] }

              match expectRefusal "a TOp slot" (emitFs idl) with
              | CodegenError.UnsupportedConstruct(construct, principle, alternative) ->
                  Expect.stringContains construct "op-vocabulary slot" "the refusal names the construct"
                  Expect.stringContains principle "GP4" "and the guiding principle behind it"
                  Expect.isFalse (alternative = "") "and states an alternative"
              | other -> failtestf "expected UnsupportedConstruct, got %A" other)

          testCase "an op-vocabulary slot is refused as data by the TYPE emitter" (fun _ ->
              // `Gen.fsharpTypes` published a bare `string` before this phase, which is
              // exactly why its refusal was a throw. The channel carries it now.
              let idl =
                  { baseIdl with
                      Kinds =
                          [ { Tag = "Note"
                              Category = "leaf"
                              Annotations = Annotations.Empty
                              Fields = [ f "kindSlot" TKind Required ] } ] }

              match expectRefusal "a TKind slot in the type emitter" (Gen.fsharpTypes idl) with
              | CodegenError.UnsupportedConstruct(construct, _, _) ->
                  Expect.stringContains construct "type emitter" "the refusal names the backend that met it"
              | other -> failtestf "expected UnsupportedConstruct, got %A" other)

          testCase "an op-vocabulary slot is refused as data by the TypeScript backend" (fun _ ->
              let idl =
                  { baseIdl with
                      Kinds =
                          [ { Tag = "Note"
                              Category = "leaf"
                              Annotations = Annotations.Empty
                              Fields = [ f "op" (TList TOp) Required ] } ] }

              match expectRefusal "a TOp slot under a list" (Gen.typescriptModule idl kinds) with
              | CodegenError.UnsupportedConstruct(construct, _, _) ->
                  Expect.stringContains construct "TypeScript" "the refusal names the backend that met it"
              | other -> failtestf "expected UnsupportedConstruct, got %A" other)

          // ── the two IDL defects that used to crash the generator ─────────

          testCase "a HostOnly field that is not a TFn is refused as data" (fun _ ->
              // A HostOnly slot is wire-absent by declaration, so its placeholder is the only
              // thing that can put a value back; one that declares none is under-determined.
              let idl =
                  { baseIdl with
                      Kinds =
                          [ { Tag = "Note"
                              Category = "leaf"
                              Annotations = Annotations.Empty
                              Fields = [ f "label" TStr Required; f "motion" TStr HostOnly ] } ] }

              match expectRefusal "a HostOnly non-TFn field" (emitFs idl) with
              | CodegenError.UnsupportedConstruct(construct, _, alternative) ->
                  Expect.stringContains construct "motion" "the refusal names the offending field"
                  Expect.stringContains construct "HostOnly" "and what is wrong with it"
                  Expect.stringContains alternative "TFn" "and the declaration that would fix it"
              | other -> failtestf "expected UnsupportedConstruct, got %A" other)

          testCase "a declared transparent union case of the wrong arity is refused as data" (fun _ ->
              // A transparent case is on the wire BARE, so its single field IS the encoding;
              // a two-field case has no unambiguous bare form.
              let idl =
                  { baseIdl with
                      Kinds =
                          [ { Tag = "Note"
                              Category = "leaf"
                              Annotations = Annotations.Empty
                              Fields = [ f "src" (TUnion("Src", [])) Required ] } ]
                      Unions =
                          [ { Name = "Src"
                              Params = []
                              Cases =
                                [ { Tag = "Pair"
                                    Fields = [ f "a" TStr Required; f "b" TStr Required ]
                                    Annotations = Annotations.Empty } ] } ]
                      Harden =
                          { HardenPolicy.Default with
                              TransparentUnions = [ "Src", "Pair" ] } }

              match expectRefusal "a two-field transparent case" (emitFs idl) with
              | CodegenError.UnsupportedConstruct(construct, _, alternative) ->
                  Expect.stringContains construct "Src.Pair" "the refusal names the case"
                  Expect.stringContains alternative "exactly one field" "and what a transparent case must carry"
              | other -> failtestf "expected UnsupportedConstruct, got %A" other

              // The same declaration, met by the other two backends that used to throw on it.
              match expectRefusal "a two-field transparent case (TypeScript)" (Gen.typescriptModule idl kinds) with
              | CodegenError.UnsupportedConstruct(construct, _, _) ->
                  Expect.stringContains construct "Src.Pair" "the TS backend refuses it by name too"
              | other -> failtestf "expected UnsupportedConstruct, got %A" other)

          // ── the rendering, which is where a refusal becomes readable ─────

          testCase "each new case renders a one-line refusal naming its subject" (fun _ ->
              let envelope =
                  CodegenError.describe (CodegenError.RequiredEnvelopeField("state", TStr, "declare it Optional"))

              let construct =
                  CodegenError.describe (
                      CodegenError.UnsupportedConstruct("an op slot", "GP4", "declare it otherwise")
                  )

              Expect.stringContains envelope "state" "the envelope refusal names the member"
              Expect.stringContains envelope "declare it Optional" "and carries the alternative through"
              Expect.stringContains construct "an op slot" "the construct refusal names the construct"
              Expect.stringContains construct "GP4" "and the principle"

              for line in [ envelope; construct ] do
                  Expect.isFalse (line.Contains "\n") "a described refusal is ONE line"
                  Expect.isFalse (line = "") "and is never empty") ]
