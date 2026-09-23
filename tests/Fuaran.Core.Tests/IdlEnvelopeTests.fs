module Fuaran.Core.Tests.IdlEnvelopeTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Phase 201 — defaults-fill on decode, and the full node envelope declared.
//
// `docs/idl-inversion-spike-findings.md` left two items open from the Phase 316
// inversion spike: defaults-fill ("feasible, not yet built") and the full node
// envelope ("the spike emits `id` + `kind` only"). Both were written ABOUT
// `Fuaran.Core.Idl.Spike`, and both have since been built in the production
// engine — `Optionality.OmitDefault` (Phase 124) and `Idl.NodeFields` +
// `Optionality.HostOnly` (Phases 690/691/698). What was missing was not the
// machinery but the CLAIM: nothing held the engine to the law the two items
// describe, and nothing declared an envelope carrying all five `WIRE_FORMAT.md`
// §3.1 slots, so "the declaration can express the whole envelope" was an
// assertion about code rather than a property of a vocabulary that exists.
//
// This file is that claim, stated so it can fail. Four things it holds:
//
//   1. **Defaults-fill on decode is a LAW, not a code path.** A member absent
//      from a document decodes to its declared default and re-encodes ABSENT, so
//      the bytes are stable across the round trip. Both halves matter and each
//      alone is trivially satisfiable — a decoder that never filled passes the
//      byte-stability half, and an encoder that always omitted passes the fill
//      half — so the non-default direction is asserted beside them.
//   2. **The law holds in EVERY decoder leg the generator emits**, which is what
//      the spike item's "same mechanism" claim actually costs: the kind spec, a
//      record, a union case and the node envelope, in one vocabulary, so a leg
//      that was missed is named rather than merely uncovered.
//   3. **The full node envelope is declarable**, all five §3.1 slots: three
//      wire-visible (one omit-on-absence, two omit-at-default — including the
//      accessibility slot, which is where an ARIA default becomes IDL-DECLARED
//      rather than generator-resident) and two host-only, which is what `motion`
//      and `extraAttributes` are. `HostOnly` is declared over a `TFn` slot, and
//      `TFn` is named for its commonest use rather than its meaning: it is a slot
//      whose HOST type is declared and whose WIRE form is fixed — for a host-only
//      member, fixed at absence. So a map-valued or closure-valued host-only
//      member needs no widening of the type model.
//   4. **An `IdlDefault` is an AUTHORING default and deliberately does NOT fill on
//      decode.** See `requiredEnvelopeIdl` below for the argument; the case is
//      here so the "obvious improvement" cannot be made without meeting it.
//
// The vocabulary is neutral in the sense `ReferenceIdl` is: the five slot names
// are `WIRE_FORMAT.md` §3.1's, because closing a findings item by inventing other
// names would make the closure unverifiable — but every VALUE is the engine's
// own, and no domain's vocabulary is named.
// ---------------------------------------------------------------------------

let private f (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Annotations = Annotations.Empty }

let private req name t = f name t Required
let private opt name t = f name t Optional
let private omit name t d = f name t (OmitDefault d)
let private hostOnly name t = f name t HostOnly

/// The host-only slots. A closure-valued one and a MAP-valued one, because the two
/// members the findings item names (`motion`, `extraAttributes`) are of those two
/// shapes and the point is that neither needs a new type constructor.
let private motionSlot: ClosureSig =
    { FSharp = "(unit -> unit) option"
      TypeScript = "(() => void) | undefined"
      Placeholder = "None" }

let private attributesSlot: ClosureSig =
    { FSharp = "Map<string, string>"
      TypeScript = "Record<string, string>"
      Placeholder = "Map.empty" }

/// One kind, one record, one union — so the same declared default can be planted in
/// each decoder leg the generator emits and read back out of one module.
let private envelopeIdl: Idl =
    { Kinds =
        [ { Tag = "Element"
            Category = "content"
            Annotations = Annotations.Empty
            Fields =
              [ req "label" TStr
                // the kind-spec leg
                omit "weight" (TEnum "Weight") (VEnum "Normal")
                req "trim" (TRecord "Trim")
                req "source" (TUnion("Source", [])) ] } ]
      Unions =
        [ { Name = "Source"
            Params = []
            Cases =
              [ { Tag = "Literal"
                  // the union-case leg
                  Fields = [ req "text" TStr; omit "scale" TInt (VInt 1) ]
                  Annotations = Annotations.Empty } ] } ]
      Enums =
        [ Declare.enumOf "Weight" [ "Normal"; "Heavy" ]
          Declare.enumOf "Presence" [ "Live"; "Muted" ]
          Declare.enumOf "Exposure" [ "Auto"; "Hidden" ] ]
      Records =
        [ { Name = "Trim"
            // the record leg
            Fields = [ omit "inset" TInt (VInt 0) ] }
          { Name = "Style"
            Fields = [ req "name" TStr ] } ]
      Defaults = []
      // The full `WIRE_FORMAT.md` §3.1 envelope: three wire-visible slots and two
      // host-only ones. `accessibility` is omit-at-default rather than optional
      // deliberately — an accessibility default that is RESTORED on decode is the
      // shape the findings item's "ARIA defaults become IDL-declared" asks for, and
      // it is a different claim from "the member may be absent".
      NodeFields =
        [ omit "state" (TEnum "Presence") (VEnum "Live")
          omit "accessibility" (TEnum "Exposure") (VEnum "Auto")
          opt "style" (TRecord "Style")
          hostOnly "motion" (TFn motionSlot)
          hostOnly "extraAttributes" (TFn attributesSlot) ]
      Ops = []
      Wire = WireShape.Default
      Harden = HardenPolicy.Undeclared }

/// The same envelope with a REQUIRED member carrying a declared `IdlDefault` — the
/// Phase 195 shape, addressed by the empty `IdlDefault.Kind`.
///
/// **Why the declared default fills the CONSTRUCTOR and not the DECODER, which is
/// the question this phase had to answer.** The two readings were:
///
///   (A) a declared `IdlDefault` also fills an absent member on decode, so a
///       document omitting it is accepted; or
///   (B) it is an AUTHORING default only — what a caller need not pass — and the
///       wire-level default is `Optionality.OmitDefault`, which already fills on
///       decode in every leg.
///
/// (B), because (A) is not byte-stable and the whole engine rests on byte
/// stability. A `Required` member is ALWAYS emitted, so a decoder that filled one
/// from a declared default would re-encode it PRESENT: two distinct byte-streams
/// would decode to one tree, and `decode >> encode` would stop being the identity
/// on the wire. That identity is what the conformance corpus compares, what a
/// content digest over a tree means, and what a cross-host attestation rests on.
/// (A) would buy leniency toward documents this vocabulary's own encoder cannot
/// produce, at the cost of the property everything else is built on.
///
/// So the two defaults are two different declarations of two different facts, and
/// the asymmetry below is the contract rather than a gap in it.
let private requiredEnvelopeIdl: Idl =
    { envelopeIdl with
        NodeFields = envelopeIdl.NodeFields @ [ req "version" TStr ]
        Defaults =
            [ { Kind = ""
                Field = "version"
                Value = VStr "1" } ] }

let private tags (idl: Idl) = idl.Kinds |> List.map _.Tag

let private emitFs (what: string) (idl: Idl) : string =
    match Gen.fsharpModule "Phase201.Generated" idl (tags idl) with
    | Ok src -> src
    | Error e -> failtestf "%s: F# codegen refused the vocabulary: %s" what (CodegenError.describe e)

let private emitTs (what: string) (idl: Idl) : string =
    match Gen.typescriptModule idl (tags idl) with
    | Ok src -> src
    | Error e -> failtestf "%s: TypeScript codegen refused the vocabulary: %s" what (CodegenError.describe e)

let private decoded (what: string) (idl: Idl) (wire: string) : IdlValue =
    match Decode.decode idl wire with
    | Ok v -> v
    | Error m -> failtestf "%s: decode refused the wire: %s" what m

let private encoded (what: string) (idl: Idl) (v: IdlValue) : string =
    match Encode.encode idl v with
    | Ok s -> s
    | Error m -> failtestf "%s: encode refused the value: %s" what m

/// The envelope of a decoded node. A node whose declared envelope restores anything
/// comes back as `VNodeEnv`; `VNode` would mean the envelope leg did not run.
let private envelopeOf (what: string) (v: IdlValue) : (string * IdlValue) list =
    match v with
    | VNodeEnv(_, envelope, _, _) -> envelope
    | VNode _ -> failtestf "%s: decoded to a BARE node — the declared envelope restored nothing" what
    | other -> failtestf "%s: decoded to %A rather than a node" what other

/// The wire a minimal element makes: no envelope member present at all.
let private bareWire =
    """{"id":"e1","kind":{"$type":"Element","label":"hello","source":{"$type":"Literal","text":"t"},"trim":{}}}"""

[<Tests>]
let tests =
    testList
        "Phase 201 — defaults-fill on decode and the full node envelope"
        [

          // ── 1. the law: fill on absence, and re-encode ABSENT ─────────────

          testCase "an absent omit-at-default envelope member decodes to its declared default" (fun _ ->
              let envelope = bareWire |> decoded "bare wire" envelopeIdl |> envelopeOf "bare wire"

              Expect.equal
                  envelope
                  [ "state", VEnum "Live"; "accessibility", VEnum "Auto" ]
                  "both omit-at-default envelope members restore, in declaration order, and the optional one does not")

          testCase "a filled default re-encodes ABSENT — the round trip is byte-stable" (fun _ ->
              let round =
                  bareWire |> decoded "bare wire" envelopeIdl |> encoded "bare wire" envelopeIdl

              // The stated law. If the fill leaked into the encoding this is where it shows.
              Expect.equal round bareWire "decode >> encode is not the identity on a document omitting its defaults"

              Expect.isFalse (round.Contains "\"state\"") "the restored default is not re-emitted"
              Expect.isFalse (round.Contains "\"accessibility\"") "nor the restored accessibility default")

          // The regression direction, and it is the one a fill test loses silently: a
          // decoder that filled unconditionally, or an encoder that omitted
          // unconditionally, passes both cases above and fails this one.
          testCase "a PRESENT non-default value is carried, not overwritten by the default" (fun _ ->
              let wire =
                  """{"accessibility":"Hidden","id":"e1","kind":{"$type":"Element","label":"hello","source":{"$type":"Literal","text":"t"},"trim":{}},"state":"Muted"}"""

              let value = decoded "non-default wire" envelopeIdl wire

              Expect.equal
                  (envelopeOf "non-default wire" value)
                  [ "state", VEnum "Muted"; "accessibility", VEnum "Hidden" ]
                  "a present envelope value decodes to what the wire said"

              Expect.equal
                  (encoded "non-default wire" envelopeIdl value)
                  wire
                  "and re-encodes present — omit-at-default must omit only AT the default")

          // ── 2. every decoder leg the generator emits ──────────────────────

          testCase "the default is restored in EVERY decoder leg — envelope, kind spec, record, union case" (fun _ ->
              let src = emitFs "the envelope vocabulary" envelopeIdl

              for name, where in
                  [ "state", "the node envelope"
                    "accessibility", "the node envelope"
                    "weight", "the kind spec"
                    "inset", "a record"
                    "scale", "a union case" ] do
                  Expect.stringContains
                      src
                      (sprintf "dDef \"%s\" __fs" name)
                      (sprintf "'%s' (%s) does not restore its declared default on absence" name where)

                  Expect.isFalse
                      (src.Contains(sprintf "dReq \"%s\" __fs" name))
                      (sprintf "'%s' (%s) is read as required as well — the two legs disagree" name where))

          testCase "the TypeScript decoder restores the same defaults — the second backend agrees" (fun _ ->
              let src = emitTs "the envelope vocabulary" envelopeIdl

              // The TS backend renders a restored enum default as its wire string. Both
              // envelope members are asserted, so a backend that reached the kind fields
              // and not the envelope is named rather than merely uncovered.
              for wire, name in [ "\"Live\"", "state"; "\"Auto\"", "accessibility"; "\"Normal\"", "weight" ] do
                  Expect.stringContains src wire (sprintf "the TypeScript decoder does not restore '%s'" name))

          // ── 3. the full §3.1 envelope, host-only members included ─────────

          testCase "the five-slot node envelope generates on both backends and in the schema" (fun _ ->
              // The findings item's "more fields, same mechanism", made falsifiable: if any
              // of the five slots were inexpressible the emitters would refuse here.
              let fsSrc = emitFs "the five-slot envelope" envelopeIdl
              let tsSrc = emitTs "the five-slot envelope" envelopeIdl

              Expect.stringContains
                  fsSrc
                  "Motion: ((unit -> unit) option)"
                  "the host-only closure slot declares its host type"

              Expect.stringContains
                  fsSrc
                  "ExtraAttributes: (Map<string, string>)"
                  "and the host-only MAP slot declares its own — no new type constructor was needed"

              Expect.stringContains fsSrc "State: Presence" "the wire-visible omit-at-default slot is a plain field"
              Expect.stringContains fsSrc "Style: Style option" "and the optional slot is an option"

              Expect.stringContains tsSrc "state" "the TypeScript node carries the envelope too"

              match Gen.jsonSchema envelopeIdl with
              | Error e -> failtestf "the schema leg refused the five-slot envelope: %s" (CodegenError.describe e)
              | Ok schema ->
                  Expect.stringContains schema "\"state\"" "the schema describes the wire-visible envelope members"
                  Expect.stringContains schema "\"accessibility\"" "including the accessibility slot"

                  Expect.isFalse
                      (schema.Contains "\"motion\"")
                      "the schema must not describe a host-only member — it is never on the wire"

                  Expect.isFalse (schema.Contains "\"extraAttributes\"") "nor the host-only attribute bag")

          testCase "a host-only envelope member is on NEITHER side of the wire" (fun _ ->
              // Authored with values, which is the case that matters: a host-only member is
              // not merely unread, it is unwritten.
              let authored =
                  VNodeEnv(
                      "e1",
                      [ "motion", VClosure; "extraAttributes", VClosure ],
                      "Element",
                      [ "label", VStr "hello"
                        "source", VUnion("Literal", [ "text", VStr "t" ])
                        "trim", VRecord [] ]
                  )

              let wire = encoded "host-only authoring" envelopeIdl authored
              Expect.equal wire bareWire "an authored host-only value reached the wire"

              // And the decode side: the member is restored from its declared placeholder in
              // the generated host, and simply absent from the interpreter's envelope.
              Expect.equal
                  (envelopeOf "host-only decode" (decoded "host-only decode" envelopeIdl wire))
                  [ "state", VEnum "Live"; "accessibility", VEnum "Auto" ]
                  "a host-only member must not appear in the decoded envelope"

              let src = emitFs "the five-slot envelope" envelopeIdl

              Expect.isFalse (src.Contains "\"motion\"") "the generated module reads or writes a 'motion' wire key"

              Expect.isFalse
                  (src.Contains "\"extraAttributes\"")
                  "the generated module reads or writes an 'extraAttributes' wire key"

              Expect.stringContains src "Motion = None" "the decoder restores the declared placeholder"
              Expect.stringContains src "ExtraAttributes = Map.empty" "and the map slot's own placeholder")

          // ── 4. the boundary: an IdlDefault is an AUTHORING default ────────

          testCase "a Required envelope member's declared default fills the CONSTRUCTOR, not the decoder" (fun _ ->
              let src = emitFs "the required-member envelope" requiredEnvelopeIdl

              Expect.stringContains src "; Version = \"1\"" "the smart constructor fills the declared default"

              Expect.stringContains
                  src
                  "dReq \"version\" __fs"
                  "the decoder reads the member as REQUIRED — see `requiredEnvelopeIdl` for why filling it is not byte-stable"

              Expect.isFalse
                  (src.Contains "dDef \"version\" __fs")
                  "an IdlDefault must not become a wire default: a filled Required member re-encodes PRESENT, and decode >> encode stops being the identity")

          testCase "and a document omitting that member is refused rather than filled" (fun _ ->
              match Decode.decode requiredEnvelopeIdl bareWire with
              | Ok v ->
                  failtestf
                      "a document omitting a Required envelope member decoded to %A — the authoring default leaked into the wire contract"
                      v
              | Error m -> Expect.stringContains m "version" "the refusal names the absent member")

          // ── 5. the regeneration proof ─────────────────────────────────────

          // The acceptance criterion's "the three vocabularies regenerate with no emitted
          // diff". The existing triple proof (Phase 114) makes this claim for the reference
          // vocabulary on the F# backend alone; the two VENDORED vocabularies are the ones
          // whose corpora were written outside this repository, so they are the ones the
          // claim is worth the most on, and the TypeScript backend is the one it was never
          // made on at all.
          //
          // **The comparison is against the CANONICAL form, and that is not a weakening.**
          // An artifact is canonical by construction (`Artifact.parse (Artifact.render idl)
          // = Artifact.canonicalise idl`, Phase 114), and declaration order is emission
          // order, so a vocabulary authored out of canonical order regenerates a module
          // whose members are ordered differently — a reshuffle, not a loss. `refIdl` is
          // authored canonically on purpose and so compares equal either way; the two
          // vendored vocabularies are not, which is why stating the law against the
          // authored form would have measured their authoring order rather than the
          // engine. What the law says is what matters here: nothing a vocabulary declares
          // is lost on the way through its own bytes, on either backend.
          testCase "each neutral vocabulary regenerates byte-identically from its own artifact bytes" (fun _ ->
              for name, idl in
                  [ "reference", ReferenceIdl.refIdl
                    "document", SecondDomainSpike.docIdl
                    "score", ScoreDomainSpike.scoreIdl ] do
                  let canonical = Artifact.canonicalise idl

                  let reloaded =
                      match Artifact.parse (Artifact.render idl) with
                      | Ok v -> v
                      | Error m -> failtestf "%s: the vocabulary did not load from its own bytes: %s" name m

                  let kindTags = tags canonical

                  match
                      Gen.fsharpModule "Phase201.Regen" canonical kindTags,
                      Gen.fsharpModule "Phase201.Regen" reloaded kindTags
                  with
                  | Ok a, Ok b ->
                      Expect.equal b a (sprintf "%s: the reloaded vocabulary emits a different F# module" name)
                  | Error e, _
                  | _, Error e -> failtestf "%s: F# codegen refused: %s" name (CodegenError.describe e)

                  match Gen.typescriptModule canonical kindTags, Gen.typescriptModule reloaded kindTags with
                  | Ok a, Ok b ->
                      Expect.equal b a (sprintf "%s: the reloaded vocabulary emits a different TypeScript module" name)
                  | Error e, _
                  | _, Error e -> failtestf "%s: TypeScript codegen refused: %s" name (CodegenError.describe e)) ]
