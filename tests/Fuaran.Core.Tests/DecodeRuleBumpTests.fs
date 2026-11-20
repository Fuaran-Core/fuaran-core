module Fuaran.Core.Tests.DecodeRuleBumpTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Tests.IdlClassify

// ---------------------------------------------------------------------------
// Phase 587 — the profile bump takes the profile's decode rule.
//
// `Diff.bumpProfile` answers for a profile whose decoders REFUSE an undeclared member, so an added
// optional member moves the minor. A profile whose decoders IGNORE one (the UI wire format's §2 rule 2)
// absorbs that member without negotiating, and `WIRE_FORMAT.md` §15.4 exempts it. `Diff.bumpProfileUnder`
// takes the rule as data.
//
// Three families:
//
//   1. the LAW, over a perturbation family of vocabulary pairs: under each rule, the step the classifier
//      names is the step a consumer under that rule actually needs, read off documents rather than off
//      the classifier. A MAJOR is needed when some OLD document is refused, or re-encodes to other bytes,
//      under the new vocabulary read by that rule's decoder; otherwise a MINOR when some NEW document is
//      refused by the old vocabulary under that rule (the consumer behind it); otherwise none. The
//      interpreter has both readings: `Decode.decode` ignores an undeclared member and
//      `Decode.decodeStrictDetailed` refuses one.
//   2. the default rule is `bumpProfile`, answer for answer.
//   3. named vectors for the cases the reference host's §15.4 rule decides, with the go-red that drops
//      the exemption (the decode-rule-blind `bumpProfile`), and the cases where that rule and this
//      classifier differ, each with the reason.
// ---------------------------------------------------------------------------

module private Law =

    open Fixtures

    /// The old vocabulary: the shared `Note`, plus an `int` slot to widen, a `float` slot to narrow and an
    /// optional `Source` slot (so a union case added is reachable), and a second kind to remove.
    let oldIdl: Idl =
        let n = baseIdl.Kinds.Head

        { baseIdl with
            Kinds =
                [ { n with
                      Fields =
                          n.Fields
                          @ [ fld "count" TInt Required
                              fld "ratio" TFloat Required
                              fld "source" (TUnion("Source", [])) Optional ] }
                  kindOf "Rule" [ fld "weight" TInt Required ] ] }

    let private note = oldIdl.Kinds.Head

    let private noteWith (f: IdlField list -> IdlField list) : Idl =
        { oldIdl with
            Kinds = { note with Fields = f note.Fields } :: oldIdl.Kinds.Tail }

    let private retype name (g: IdlField -> IdlField) =
        noteWith (List.map (fun (x: IdlField) -> if x.Name = name then g x else x))

    let private defaultOn (idl: Idl) =
        { idl with
            Defaults =
                [ { Kind = "Note"
                    Field = "loudness"
                    Value = VEnum "Plain" } ] }

    /// What a consumer needs. Ordered: a `Major` is never softened by a minor beside it.
    type Step =
        | NoStep
        | Minor
        | Major

    /// A perturbation: its name, the before and after vocabularies.
    let perturbations: (string * Idl * Idl) list =
        [ "unchanged", oldIdl, oldIdl
          "added optional field", oldIdl, noteWith (fun fs -> fs @ [ fld "caption" TStr Optional ])
          "added omit-at-default field", oldIdl, noteWith (fun fs -> fs @ [ fld "caption" TStr (OmitDefault(VStr "")) ])
          "added optional field of a new record type",
          oldIdl,
          { noteWith (fun fs -> fs @ [ fld "meta" (TRecord "Meta") Optional ]) with
              Records =
                  [ { Name = "Meta"
                      Fields = [ fld "tag" TStr Required ]
                      Annotations = ann
                      Inline = false } ] }
          "added required field", oldIdl, noteWith (fun fs -> fs @ [ fld "caption" TStr Required ])
          "tightened optional to required", oldIdl, retype "loudness" (fun x -> { x with Opt = Required })
          "loosened required to optional", oldIdl, retype "body" (fun x -> { x with Opt = Optional })
          "widened int to float", oldIdl, retype "count" (fun x -> { x with Type = TFloat })
          "narrowed float to int", oldIdl, retype "ratio" (fun x -> { x with Type = TInt })
          "removed optional field", oldIdl, noteWith (List.filter (fun x -> x.Name <> "source"))
          "added kind",
          oldIdl,
          { oldIdl with
              Kinds = oldIdl.Kinds @ [ kindOf "Mark" [ fld "at" TInt Required ] ] }
          "removed kind", oldIdl, { oldIdl with Kinds = [ note ] }
          "added union case",
          oldIdl,
          { oldIdl with
              Unions =
                  [ { oldIdl.Unions.Head with
                        Cases = oldIdl.Unions.Head.Cases @ [ case "Computed" [ fld "expr" TStr Required ] ] } ] }
          "added enum case",
          oldIdl,
          { oldIdl with
              Enums =
                  [ { oldIdl.Enums.Head with
                        Cases = oldIdl.Enums.Head.Cases @ [ "Soft" ] } ] }
          "authoring default added", oldIdl, defaultOn oldIdl
          "authoring default withdrawn (an emitter break)", defaultOn oldIdl, oldIdl ]

    let private tags (idl: Idl) = idl.Kinds |> List.map _.Tag

    /// Documents a vocabulary emits: a sample over every kind, encoded canonically.
    let documents (idl: Idl) : string list =
        match Sample.trySampleNodes idl (tags idl) 587 240 with
        | Error r -> failtestf "the sampler refused the vocabulary: %s" r.Describe
        | Ok vs ->
            vs
            |> List.map (fun v ->
                match Encode.encode idl v with
                | Ok b -> b
                | Error m -> failtestf "the vocabulary refused its own document %A: %s" v m)
            |> List.distinct

    /// Read one document under a vocabulary by a rule's decoder: `None` when refused, else its re-encoding.
    let read (rule: Versioning.DecodeRule) (idl: Idl) (doc: string) : string option =
        let decoded =
            match rule with
            | Versioning.DecodeRule.IgnoresUnknownMembers -> Decode.decode idl doc |> Result.mapError ignore
            | Versioning.DecodeRule.RefusesUnknownMembers ->
                Decode.decodeStrictDetailed idl doc |> Result.mapError ignore

        match decoded with
        | Error() -> None
        | Ok v ->
            match Encode.encode idl v with
            | Ok again -> Some again
            | Error m -> failtestf "decoded %s and could not re-encode it: %s" doc m

    /// The step a consumer under `rule` needs, measured on documents.
    let needed (rule: Versioning.DecodeRule) (before: Idl) (after: Idl) : Step =
        let oldDocs = documents before
        let newDocs = documents after

        if oldDocs |> List.exists (fun d -> read rule after d <> Some d) then
            Major
        elif newDocs |> List.exists (fun d -> read rule before d |> Option.isNone) then
            Minor
        else
            NoStep

    let private baseProfile: Versioning.Capability =
        { Name = "core"; Major = 1; Minor = 4 }

    let verdictOf (before: Idl) (after: Idl) : Verdict =
        match Diff.ofValues (before, None) (after, None) with
        | Ok v -> v
        | Error e -> failtestf "ofValues: %s" e

    let stepOf (b: Bump) : Step =
        match b with
        | Bump.Undecided rows -> failtestf "the pair left %d rows undecided" rows.Length
        | Bump.Bumped p when p.Major <> baseProfile.Major -> Major
        | Bump.Bumped p when p.Minor <> baseProfile.Minor -> Minor
        | Bump.Bumped _ -> NoStep

    let classified (rule: Versioning.DecodeRule) (before: Idl) (after: Idl) : Step =
        stepOf (Diff.bumpProfileUnder rule baseProfile (verdictOf before after))

    let legacy (before: Idl) (after: Idl) : Step =
        stepOf (Diff.bumpProfile baseProfile (verdictOf before after))

    /// The pairs on which the DEFAULT rule's answer is 127's, held unchanged, and over-steps: rows no
    /// document carries, which `bumpProfile` reads as an introduction. A behind consumer under either rule
    /// needs nothing there.
    let defaultRuleOverSteps =
        set [ "authoring default added"; "authoring default withdrawn (an emitter break)" ]

/// The reference host's §15.4 rule (`WIRE_FORMAT.md` §15.4, as its regeneration check applies it), transcribed
/// as the oracle for the named vectors: a wire break is a major; then any undecided row is undecided; then a
/// new TAG in a closed discriminated vocabulary is a minor; everything else moves nothing.
module private ReferenceRule =

    type Answer =
        | NoStep
        | Minor
        | Major
        | Undecided

    let private introducesTag (c: Change) =
        match c with
        | KindAdded _
        | OpAdded _
        | UnionCaseAdded _
        | EnumCaseAdded _ -> true
        | _ -> false

    let step (cs: Classification list) : Answer =
        let has s =
            cs |> List.exists (fun c -> c.Severity = s)

        if has BreakingWire then
            Major
        elif has Unclassifiable then
            Undecided
        elif cs |> List.exists (fun c -> introducesTag c.Change) then
            Minor
        else
            NoStep

    let ofBump (b: Bump) : Answer =
        match b with
        | Bump.Undecided _ -> Undecided
        | Bump.Bumped p when p.Major <> 1 -> Major
        | Bump.Bumped p when p.Minor <> 0 -> Minor
        | Bump.Bumped _ -> NoStep

/// A verdict over hand-chosen rows, for a vector no single vocabulary pair produces.
let private verdictOfRows (rows: Classification list) : Verdict =
    { Encoding = 1, 1
      Changes = rows
      Evolution = Diff.evolution rows
      Undecided = rows |> List.filter (fun c -> c.Severity = Unclassifiable)
      BreaksEmitters = rows |> List.exists (fun c -> c.Severity = BreakingForEmitters)
      FSharpConsequences = []
      StabilityImpact = ""
      ProfileAdvice = "" }

let private ignored = Versioning.DecodeRule.IgnoresUnknownMembers
let private refused = Versioning.DecodeRule.RefusesUnknownMembers

let private rowsOf (before: Idl) (after: Idl) = (Law.verdictOf before after).Changes

/// An erased-slot re-model laid over a vocabulary pair: a hosted field re-declared as an enum, which the
/// artifact cannot decide. The pair's own rows are kept, and one undecided row is added beside them.
let private withUndecided (before: Idl, after: Idl) : Idl * Idl =
    let withRegion (idl: Idl) ty =
        { idl with
            Records =
                idl.Records
                @ [ { Name = "Access"
                      Fields = [ Fixtures.fld "region" ty Optional ]
                      Annotations = Fixtures.ann
                      Inline = false } ]
            Enums =
                idl.Enums
                @ [ { Law.oldIdl.Enums.Head with
                        Name = "Region"
                        Cases = [ "Polite" ] } ] }

    let hosted =
        THosted
            { Refusal = HostedRefusal.Sentence
              FSharp = "Host.Region"
              Encode = "encRegion"
              Decode = "decRegion"
              Wire = None
              Format = None }

    withRegion before hosted, withRegion after (TEnum "Region")

/// The undecided row alone, over the unchanged vocabulary.
let private undecidedRows () =
    withUndecided (Law.oldIdl, Law.oldIdl) ||> rowsOf

/// A named perturbation's vocabulary pair.
let private pairOf (name: string) : Idl * Idl =
    let _, before, after = Law.perturbations |> List.find (fun (n, _, _) -> n = name)
    before, after

/// The step `ProfileAdvice` names, read off its opening words.
let private adviceStep (advice: string) : ReferenceRule.Answer =
    if advice.StartsWith "`/v2/` MAJOR" then
        ReferenceRule.Major
    elif advice.StartsWith "UNDECIDED" then
        ReferenceRule.Undecided
    elif advice.StartsWith "`core@1.(x+1)`" then
        ReferenceRule.Minor
    elif advice.StartsWith "no wire-profile movement" then
        ReferenceRule.NoStep
    else
        failtestf "the advice names no step: %s" advice

[<Tests>]
let tests =
    testList
        "Phase 587 — the profile bump takes the profile's decode rule"
        [ testCase "the law: under each rule, the step named is the step a consumer under that rule needs" (fun _ ->
              for name, before, after in Law.perturbations do
                  let needIgnored = Law.needed ignored before after
                  let needRefused = Law.needed refused before after

                  Expect.equal
                      (Law.classified ignored before after)
                      needIgnored
                      (sprintf "%s: under IgnoresUnknownMembers the classifier names the step the documents need" name)

                  if Law.defaultRuleOverSteps.Contains name then
                      Expect.equal needRefused Law.NoStep (sprintf "%s: no document moves under either rule" name)

                      Expect.equal
                          (Law.classified refused before after)
                          Law.Minor
                          (sprintf "%s: the default rule keeps 127's minor, which over-steps — named, not silent" name)
                  else
                      Expect.equal
                          (Law.classified refused before after)
                          needRefused
                          (sprintf
                              "%s: under RefusesUnknownMembers the classifier names the step the documents need"
                              name))

          testCase
              "adequacy: the family reaches every step, and the two rules part on the members a consumer ignores"
              (fun _ ->
                  let need rule =
                      Law.perturbations
                      |> List.map (fun (n, b, a) -> n, Law.needed rule b a)
                      |> Map.ofList

                  let i, r = need ignored, need refused

                  for s in [ Law.NoStep; Law.Minor; Law.Major ] do
                      Expect.exists (Map.toList i) (fun (_, x) -> x = s) (sprintf "the family reaches %A" s)

                  let parted =
                      Law.perturbations
                      |> List.map (fun (n, _, _) -> n)
                      |> List.filter (fun n -> i[n] <> r[n])
                      |> Set.ofList

                  Expect.equal
                      parted
                      (set
                          [ "added optional field"
                            "added omit-at-default field"
                            "added optional field of a new record type" ])
                      "the decode rule decides exactly the added optional members — the documents say so, not the classifier")

          testCase "the default rule is bumpProfile, answer for answer" (fun _ ->
              for name, before, after in Law.perturbations do
                  let v = Law.verdictOf before after

                  Expect.equal
                      (Diff.bumpProfileUnder refused Versioning.Capability.coreV1 v)
                      (Diff.bumpProfile Versioning.Capability.coreV1 v)
                      name

              let undecided = verdictOfRows (undecidedRows ())

              Expect.equal
                  (Diff.bumpProfileUnder refused Versioning.Capability.coreV1 undecided)
                  (Diff.bumpProfile Versioning.Capability.coreV1 undecided)
                  "and on an undecided verdict")

          testCase "go-red: the decode-rule-blind bump fails the law under IgnoresUnknownMembers" (fun _ ->
              let _, before, after =
                  Law.perturbations |> List.find (fun (n, _, _) -> n = "added optional field")

              Expect.equal
                  (Law.needed ignored before after)
                  Law.NoStep
                  "a consumer that ignores the member needs nothing"

              Expect.equal
                  (Law.legacy before after)
                  Law.Minor
                  "the bump that drops the exemption names a minor — the comparison can fail")

          testCase "the reference rule's named vectors: where it decides, IgnoresUnknownMembers agrees" (fun _ ->
              let agree =
                  [ "unchanged"
                    "added optional field"
                    "added omit-at-default field"
                    "added optional field of a new record type"
                    "added required field"
                    "tightened optional to required"
                    "narrowed float to int"
                    "removed optional field"
                    "added kind"
                    "removed kind"
                    "added union case"
                    "added enum case"
                    "authoring default added"
                    "authoring default withdrawn (an emitter break)" ]

              for name in agree do
                  let _, before, after = Law.perturbations |> List.find (fun (n, _, _) -> n = name)

                  let v = Law.verdictOf before after

                  Expect.equal
                      (ReferenceRule.ofBump (Diff.bumpProfileUnder ignored Versioning.Capability.coreV1 v))
                      (ReferenceRule.step v.Changes)
                      (sprintf "%s: Core under IgnoresUnknownMembers answers as the reference rule" name))

          testCase
              "the reference rule's named vectors: the two cases it answers otherwise, each with its reason"
              (fun _ ->
                  // 1-2. The reference rule exempts every non-tag addition. Two of them are not absorbed by a
                  // consumer that ignores unknown members: a loosened required member (a new document may omit
                  // what the old vocabulary requires) and a widened int slot (a new document may carry 2.5). The
                  // documents refuse, so the minor is the need, and Core names it.
                  for name in [ "loosened required to optional"; "widened int to float" ] do
                      let _, before, after = Law.perturbations |> List.find (fun (n, _, _) -> n = name)

                      let v = Law.verdictOf before after

                      Expect.equal
                          (ReferenceRule.step v.Changes)
                          ReferenceRule.NoStep
                          (name + ": the reference rule exempts it")

                      Expect.equal
                          (Law.needed ignored before after)
                          Law.Minor
                          (name + ": a behind consumer refuses a new document")

                      Expect.equal
                          (ReferenceRule.ofBump (Diff.bumpProfileUnder ignored Versioning.Capability.coreV1 v))
                          ReferenceRule.Minor
                          (name + ": Core names the minor the documents need"))

          testCase
              "the reference rule's named vectors: a wire break beside an undecided row is a major, and Core agrees (Phase 710)"
              (fun _ ->
                  // Phase 587 recorded this as a third disagreement and held Core's undecided; Phase 710 rules it:
                  // nothing outranks a major, so no check of the undecided row can lower the answer.
                  let mixed = verdictOfRows ((pairOf "removed kind" ||> rowsOf) @ undecidedRows ())

                  Expect.equal (ReferenceRule.step mixed.Changes) ReferenceRule.Major "the reference rule: major"

                  for rule in [ ignored; refused ] do
                      Expect.equal
                          (ReferenceRule.ofBump (Diff.bumpProfileUnder rule Versioning.Capability.coreV1 mixed))
                          ReferenceRule.Major
                          (sprintf "Core under %A: major" rule))

          testCase "absorbedWhenUnknownMembersIgnored reads the row, never a wire break" (fun _ ->
              let rowsOfPair name =
                  let _, b, a = Law.perturbations |> List.find (fun (n, _, _) -> n = name)
                  rowsOf b a

              Expect.isTrue
                  (rowsOfPair "added optional field"
                   |> List.forall Diff.absorbedWhenUnknownMembersIgnored)
                  "an added optional member is absorbed"

              for name in
                  [ "added required field"
                    "added kind"
                    "added enum case"
                    "loosened required to optional"
                    "widened int to float" ] do
                  Expect.isFalse
                      (rowsOfPair name |> List.exists Diff.absorbedWhenUnknownMembersIgnored)
                      (name + ": not absorbed")) ]

// ---------------------------------------------------------------------------
// Phase 710 — the profile bump answers major on a decided wire break, whatever else is undecided.
//
// Nothing outranks a major, so no check of an undecided row can lower the answer; `ProfileAdvice` already said
// so, and the typed answer now reads the same decision. With no decided wire break, an undecided row still stops
// the bump: reporting the decidable remainder is how a `/v2/` event gets published as a minor. The gate class
// keeps its own precedence (D242, as amended).
// ---------------------------------------------------------------------------

/// The perturbation family, each pair also with an undecided row laid beside its own rows.
let private withAndWithoutUndecided: (string * Idl * Idl) list =
    Law.perturbations
    @ (Law.perturbations
       |> List.map (fun (n, b, a) ->
           let b', a' = withUndecided (b, a)
           n + ", beside an undecided row", b', a'))

let private breaksWire (v: Verdict) =
    v.Changes |> List.exists (fun c -> c.Severity = BreakingWire)

let private answerUnder (rule: Versioning.DecodeRule) (v: Verdict) : ReferenceRule.Answer =
    ReferenceRule.ofBump (Diff.bumpProfileUnder rule Versioning.Capability.coreV1 v)

let private rules = [ ignored; refused ]

[<Tests>]
let wireBreakBesideUndecided =
    testList
        "Phase 710 — the profile bump answers major on a wire break, whatever else is undecided"
        [ testCase "a wire break beside an undecided row: every surface's answer, under every decode rule" (fun _ ->
              let v = Law.verdictOf <|| withUndecided (pairOf "removed kind")

              Expect.isTrue (breaksWire v) "the pair breaks the wire"
              Expect.isNonEmpty v.Undecided "and leaves a row undecided"

              for rule in rules do
                  Expect.equal (answerUnder rule v) ReferenceRule.Major (sprintf "the typed bump under %A" rule)

              Expect.equal
                  (ReferenceRule.ofBump (Diff.bumpProfile Versioning.Capability.coreV1 v))
                  ReferenceRule.Major
                  "bumpProfile"

              Expect.equal (adviceStep v.ProfileAdvice) ReferenceRule.Major "the advice"
              Expect.equal v.StabilityImpact "breaking" "the front-matter draft"

              Expect.equal
                  (Diff.verdictClass v)
                  VerdictClass.Undecided
                  "the gate class keeps its precedence: a row is still unchecked")

          testCase "a decided wire break is a major under every decode rule, with or without an undecided row" (fun _ ->
              let breaking =
                  withAndWithoutUndecided
                  |> List.filter (fun (_, b, a) -> breaksWire (Law.verdictOf b a))
                  |> List.map (fun (n, _, _) -> n)

              Expect.containsAll
                  breaking
                  [ "removed kind, beside an undecided row"
                    "added required field, beside an undecided row"
                    "removed kind"
                    "added required field" ]
                  "the family carries wire breaks on both sides of the undecided row"

              for name, before, after in withAndWithoutUndecided do
                  let v = Law.verdictOf before after

                  if breaksWire v then
                      for rule in rules do
                          Expect.equal (answerUnder rule v) ReferenceRule.Major (sprintf "%s, under %A" name rule))

          testCase "an undecided row with no decided wire break beside it still stops the bump" (fun _ ->
              let stopped =
                  withAndWithoutUndecided
                  |> List.filter (fun (_, b, a) ->
                      let v = Law.verdictOf b a
                      not v.Undecided.IsEmpty && not (breaksWire v))

              Expect.containsAll
                  (stopped |> List.map (fun (n, _, _) -> n))
                  [ "unchanged, beside an undecided row"
                    "authoring default withdrawn (an emitter break), beside an undecided row"
                    "added optional field, beside an undecided row" ]
                  "the undecided row alone, beside an emitter break and beside an addition"

              for name, before, after in stopped do
                  let v = Law.verdictOf before after

                  for rule in rules do
                      Expect.equal (answerUnder rule v) ReferenceRule.Undecided (sprintf "%s, under %A" name rule)

                      match Diff.bumpProfileUnder rule Versioning.Capability.coreV1 v with
                      | Bump.Undecided rows -> Expect.equal rows v.Undecided (name + ": it names the rows to check")
                      | Bump.Bumped p -> failtestf "%s: bumped to %A" name p)

          testCase "an emitter break beside an undecided row is undecided, as before" (fun _ ->
              let v =
                  Law.verdictOf
                  <|| withUndecided (pairOf "authoring default withdrawn (an emitter break)")

              Expect.isTrue v.BreaksEmitters "the pair breaks emitters"
              Expect.isFalse (breaksWire v) "and not the wire"

              for rule in rules do
                  Expect.equal (answerUnder rule v) ReferenceRule.Undecided (sprintf "under %A" rule)

              Expect.equal (adviceStep v.ProfileAdvice) ReferenceRule.Undecided "the advice")

          testCase "a wire break alone is a major under every decode rule" (fun _ ->
              let v = Law.verdictOf <|| pairOf "removed kind"

              Expect.isEmpty v.Undecided "nothing undecided"

              for rule in rules do
                  Expect.equal (answerUnder rule v) ReferenceRule.Major (sprintf "under %A" rule))

          testCase "the advice and the typed answer agree on every case enumerated" (fun _ ->
              for name, before, after in withAndWithoutUndecided do
                  let v = Law.verdictOf before after

                  Expect.equal
                      (ReferenceRule.ofBump (Diff.bumpProfile Versioning.Capability.coreV1 v))
                      (adviceStep v.ProfileAdvice)
                      (sprintf "%s: bumpProfile names the step its own advice names" name)) ]
