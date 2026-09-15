module Fuaran.Core.Tests.IdlFStarTargetTests

open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Phase 150 — the F* PROOF-MODEL target, and the GENERATION DIFF that holds each committed
// `proofs/<M>.fst` model to a fresh generation from the vocabulary it is generated from.
//
// The diff is the same discipline the proof leg already applies to the extracted oracle —
// `proofs/oracle/<M>.fs` must be byte-identical to a fresh extraction of `<M>.fst`, or the
// oracle the suite runs is not the model the theorem is about. Here the pair is one step
// further up: a committed model must be byte-identical to a fresh generation from its
// vocabulary, or the vocabulary the theorem is about is not the one this repository
// certifies the backend on. A vocabulary that moves without a regeneration is VOCABULARY
// DRIFT and this is where it is caught.
//
// Phase 173 — WHICH vocabulary, and why it is no longer the pinned corpus's `idl.json`.
// Until 173 the one generated model was the UI vocabulary's, read from the shared corpus:
// a domain's proof running in the substrate's CI, which D14 (and Phase 114, completing it
// for the F# backend) had already ruled out for `tests/`. The generation source is now the
// CERTIFICATION SET — the engine's own reference vocabulary (`ReferenceIdl.refIdl`) and the
// two vendored non-UI samples — the same set `IdlCertificationTests` certifies the F# and
// TypeScript backends over. What the leg proves is therefore the F* BACKEND, and the corpus
// read has left this file entirely: nothing here resolves `SiblingCorpus`, so the leg is
// not gated on a sibling clone and `--emit-fstar` takes its vocabulary from this project.
// A domain proves its OWN vocabulary in its own repository with the same generator; the
// UI vocabulary's model, proofs and cost are `fuaran#1754`'s, the proof kit's first adopter.
//
// It lives in the Expecto suite rather than in `check.ps1`'s PowerShell because the
// generator is F#: the leg invokes it under the `Proofs.Vocabulary` filter, beside the two
// families it already runs, and the repository gate runs it with everything else.
// ---------------------------------------------------------------------------

let private repoRoot = Path.GetDirectoryName(Snapshots.repoFile "Fuaran.Core.slnx")

let private lf (s: string) = s.Replace("\r\n", "\n")

/// One generated model: the F* module name (which is also the file name under `proofs/`),
/// the vocabulary it is generated from, and the provenance its header names.
type private Generated =
    { Module: string
      Idl: Idl
      Provenance: FStarTarget.Provenance }

/// What every header says about the remedy, once — the command and the family that holds
/// the committed file to it. The origin paragraph of each provenance ends with this.
let private remedyLines =
    [ "DO NOT EDIT: `dotnet run --project tests/Fuaran.Core.Tests -- --emit-fstar` regenerates"
      "it, and the `Proofs.Vocabulary` family holds this file to a fresh generation from that"
      "source — a vocabulary that moves without a regeneration is VOCABULARY DRIFT, and"
      "`proofs/check.ps1` names it." ]

/// What every header says about the claim, once. The model is a property of the BACKEND
/// over the certification set, which is the whole of the Phase 173 change: which
/// vocabulary the theorem is ABOUT.
let private provesLines =
    [ "F* BACKEND (`Fuaran.Core.Idl.Codegen`'s `FStarTarget`) over the vocabulary the engine"
      "is certified on — the same set the F# and TypeScript backends are certified over — and"
      "not of any domain's vocabulary. A domain proves its own vocabulary in its own"
      "repository with the same generator; the UI vocabulary's model is `fuaran#1754`'s." ]

/// The certification set the F* backend is proved over, in the order the F# and TypeScript
/// backends meet it in `IdlCertificationTests`: the reference vocabulary carries the part of
/// the type model neither vendored sample uses, and the two samples carry the declared
/// NON-DEFAULT wire shape (bare-string discriminator, flat node envelope, declaration key
/// order) that the reference vocabulary, on `WireShape.Default`, does not reach.
let private generated: Generated list =
    [ { Module = "Vocabulary"
        Idl = ReferenceIdl.refIdl
        Provenance =
          { Origin =
              [ "the engine's REFERENCE vocabulary — `tests/Fuaran.Core.Tests/ReferenceIdl.fs`, the"
                "domain-neutral `Idl` Phase 114 authored to reach the part of the type model no"
                "vendored sample uses (fuaran-core Phase 173, applying D14 to `proofs/`)." ]
              @ remedyLines
            Proves = provesLines } }
      { Module = "DocVocabulary"
        Idl = SecondDomainSpike.docIdl
        Provenance =
          { Origin =
              [ "the vendored SECOND-DOMAIN sample — `tests/Fuaran.Core.Tests/SecondDomainSpike.fs`,"
                "a document vocabulary with a corpus written outside this repository, on the"
                "declared non-default wire shape the reference vocabulary does not use"
                "(fuaran-core Phase 173, applying D14 to `proofs/`)." ]
              @ remedyLines
            Proves = provesLines } }
      { Module = "ScoreVocabulary"
        Idl = ScoreDomainSpike.scoreIdl
        Provenance =
          { Origin =
              [ "the vendored THIRD-DOMAIN sample — `tests/Fuaran.Core.Tests/ScoreDomainSpike.fs`,"
                "a notation vocabulary with a corpus written outside this repository: records and"
                "omit-at-default at scale, on the same declared non-default wire shape"
                "(fuaran-core Phase 173, applying D14 to `proofs/`)." ]
              @ remedyLines
            Proves = provesLines } } ]

let private modelPath (g: Generated) =
    Path.Combine(repoRoot, "proofs", g.Module + ".fst")

/// The proof script beside a model: `<M>Proofs.fst`, opening `<M>`.
let private proofsName (g: Generated) = g.Module + "Proofs"

let private proofsPath (g: Generated) =
    Path.Combine(repoRoot, "proofs", proofsName g + ".fst")

/// The kinds a committed model covers: EVERY kind the backend can express, not the
/// `FStarTarget.proofKinds` slice. That rule is a cost control sized against a vocabulary whose
/// node envelope already carries dozens of declared types, where a kind reaching only those
/// costs one constructor and a kind bringing its own object is a measured decision (Phase 150,
/// over the UI vocabulary). Applied to the certification set it is the wrong instrument: the
/// reference vocabulary's envelope is two scalars, so the rule kept ONE of its five kinds and
/// dropped the four that carry the type-model remainder the vocabulary exists to reach —
/// checked before this was written, and the reason `selection` is not `proofKinds` here. The
/// cost of the whole set is measured in `proofs/modules.json`, where it is small enough that
/// there is nothing for a cost control to control. The rule itself is unchanged, because it is
/// the adopter's (`fuaran#1754`) instrument at the scale it was measured at.
let private selection (idl: Idl) : string list =
    FStarTarget.partition idl
    |> List.filter (fun v -> v.Refusal.IsNone)
    |> List.map _.Tag

/// A minimal vocabulary with one kind, used by the refusal cases. Written by hand rather
/// than cut down from a certification vocabulary, so a change there cannot quietly make a
/// refusal case stop exercising the construct it is about.
let private tinyIdl (fieldType: IdlType) (opt: Optionality) : Idl =
    { Kinds =
        [ { Tag = "Only"
            Category = "Display"
            Fields =
              [ { Name = "member"
                  Type = fieldType
                  Opt = opt
                  Annotations = Annotations.Empty } ]
            Annotations = Annotations.Empty } ]
      Unions = []
      Enums = []
      Records = []
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      Harden =
        { HardenPolicy.Default with
            TransparentUnions = [] } }

/// Both generated files for one vocabulary, from one walk: the model and the theorems over it.
/// Phase 150 committed only the model, because the theorems did not discharge at the UI
/// vocabulary's scale; over the certification set they do (measured, `proofs/modules.json`),
/// so both are committed and both are held here.
let private pair (g: Generated) : (string * Result<string, CodegenError>) list =
    let kinds = selection g.Idl

    [ modelPath g, FStarTarget.vocabularyModuleFrom g.Provenance g.Module g.Idl kinds
      proofsPath g, FStarTarget.proofsModuleFrom g.Provenance (proofsName g) g.Module g.Idl kinds ]

/// `--emit-fstar` — rewrite every committed model and proof script from the certification set.
/// The command the generation diff names when it fails, and the only sanctioned way those files
/// change.
let emit () : int =
    let write (path: string) (result: Result<string, CodegenError>) =
        match result with
        | Error e ->
            eprintfn "--emit-fstar: %s" (CodegenError.describe e)
            2
        | Ok text ->
            File.WriteAllText(path, lf text, System.Text.UTF8Encoding false)
            printfn "regenerated %s" path
            0

    generated
    |> List.fold
        (fun worst g ->
            let written =
                pair g |> List.map (fun (path, result) -> write path result) |> List.max

            printfn
                "%s: %d of %d kinds modelled; the rest are named in the emitted header"
                g.Module
                (selection g.Idl |> List.length)
                (List.length g.Idl.Kinds)

            max worst written)
        0

let private refusalOf (t: IdlType) =
    match FStarTarget.vocabularyModule "M" (tinyIdl t Required) [ "Only" ] with
    | Ok _ -> None
    | Error e -> Some(CodegenError.describe e)

[<Tests>]
let idlFStarTargetTests =
    testList
        "Proofs.Vocabulary"
        [

          // ---- the refusals ------------------------------------------------

          testCase "a tree-op slot is a NAMED refusal, not a dropped member"
          <| fun _ ->
              match refusalOf TOp with
              | None -> failtest "the F* target emitted a model over a slot it cannot express"
              | Some why ->
                  Expect.stringContains why "tree-op" "the refusal names the construct"
                  Expect.stringContains why "kind Only.member" "the refusal names where it was reached"

          testCase "a bare-kind slot is a named refusal"
          <| fun _ ->
              match refusalOf TKind with
              | None -> failtest "the F* target emitted a model over a bare-kind slot"
              | Some why -> Expect.stringContains why "bare kind" "the refusal names the construct"

          testCase "a wire-visible closure is MODELLED as the sentinel it carries, not refused"
          <| fun _ ->
              // The distinction the refusal set turns on: a closure is not *absent* from the
              // wire, it is a constant on it. Modelling it as `unit` is the statement that it
              // carries no information; refusing it would exclude nearly every kind.
              match FStarTarget.vocabularyModule "M" (tinyIdl TClosure Required) [ "Only" ] with
              | Error e -> failtestf "a wire-visible closure was refused: %s" (CodegenError.describe e)
              | Ok text ->
                  Expect.stringContains text "JStr \"<closure>\"" "the sentinel is what the model encodes"
                  Expect.stringContains text "member:(unit)" "and `unit` is what it carries"

          testCase "a HOST-ONLY member is absent from the model entirely"
          <| fun _ ->
              match
                  FStarTarget.vocabularyModule
                      "M"
                      (tinyIdl
                          (TFn
                              { FSharp = "int -> 'Msg"
                                TypeScript = "never"
                                Placeholder = "(fun _ -> ())" })
                          HostOnly)
                      [ "Only" ]
              with
              | Error e -> failtestf "a host-only member was refused: %s" (CodegenError.describe e)
              | Ok text ->
                  Expect.isFalse
                      (text.Contains "\"member\"")
                      "a member that is never on the wire has no place in a model OF the wire"

          testCase "the refusal is typed, and its case is the shared CodegenError channel"
          <| fun _ ->
              match FStarTarget.vocabularyModule "M" (tinyIdl TOp Required) [ "Only" ] with
              | Ok _ -> failtest "expected a refusal"
              | Error(CodegenError.UnmodellableInFStar _) -> ()
              | Error other -> failtestf "expected UnmodellableInFStar, got %A" other

          // ---- every refusal class is reached, and by whom -------------------

          testCase
              "every refusal class of the backend is reached at least once — by the reference vocabulary or by a hand-written case"
          <| fun _ ->
              // Phase 173's acceptance says the reference vocabulary reaches every refusal
              // class "by construction (it was authored to reach every IdlType)". Checked
              // against the backend, that premise is FALSE, and the correction is worth
              // pinning: reaching every type case is not reaching every refusal class,
              // because most of the backend's refusals are about an ILL-FORMED IDL — an
              // undeclared record, a union arity mismatch, an unresolved type parameter — that
              // a well-formed certification vocabulary cannot carry, and the one refusal a
              // well-formed vocabulary CAN reach at kind level is the numeric default. The
              // reference vocabulary reaches that one (`Measure.value` declares
              // `Fixed { value = 0.0 }` and the model's numeric carriers are opaque). Its
              // `TKind` / `TOp` slots live in its OP vocabulary, which the model — a model of
              // nodes — does not walk, so those two classes are reached by the hand-written
              // cases above. Each row below names the class and what reaches it; a class the
              // backend gains without a row here is a refusal nothing exercises.
              let reached (idl: Idl) (tags: string list) =
                  match FStarTarget.vocabularyModule "M" idl tags with
                  | Ok _ -> None
                  | Error e -> Some(CodegenError.describe e)

              let onlyTag = [ "Only" ]

              let withUnion (u: IdlUnion) (idl: Idl) = { idl with Unions = [ u ] }

              let classes: (string * string * (unit -> string option)) list =
                  [ "a numeric default the opaque model cannot spell",
                    "declared default the opaque numeric model cannot spell",
                    fun () ->
                        FStarTarget.partition ReferenceIdl.refIdl
                        |> List.tryFind (fun v -> v.Tag = "Measure")
                        |> Option.bind _.Refusal
                    "a tree-op slot", "tree-op slot", (fun () -> refusalOf TOp)
                    "a bare-kind slot", "bare kind slot", (fun () -> refusalOf TKind)
                    "a host codec with no declared host type",
                    "host codec with no declared host type",
                    fun () ->
                        refusalOf (
                            THosted
                                { FSharp = ""
                                  Encode = "enc"
                                  Decode = "dec" }
                        )
                    "an unresolved type parameter",
                    "unresolved type parameter",
                    fun () -> reached (tinyIdl (TVar "T") Required) onlyTag
                    "an undeclared record",
                    "undeclared record",
                    (fun () -> reached (tinyIdl (TRecord "Missing") Required) onlyTag)
                    "an undeclared union",
                    "undeclared union",
                    fun () -> reached (tinyIdl (TUnion("Missing", [])) Required) onlyTag
                    "a union arity mismatch",
                    "union arity mismatch",
                    fun () ->
                        tinyIdl (TUnion("Slot", [ TStr; TStr ])) Required
                        |> withUnion
                            { Name = "Slot"
                              Params = [ "T" ]
                              Cases =
                                [ { Tag = "Fixed"
                                    Fields =
                                      [ { Name = "value"
                                          Type = TVar "T"
                                          Opt = Required
                                          Annotations = Annotations.Empty } ]
                                    Annotations = Annotations.Empty } ] }
                        |> fun idl -> reached idl onlyTag
                    "an undeclared kind", "undeclared kind", (fun () -> reached (tinyIdl TStr Required) [ "Absent" ])
                    "a default omitting a required member",
                    "default omitting the required member",
                    fun () ->
                        let idl =
                            tinyIdl (TUnion("Text", [])) (OmitDefault(VUnion("Inline", [])))
                            |> withUnion
                                { Name = "Text"
                                  Params = []
                                  Cases =
                                    [ { Tag = "Inline"
                                        Fields =
                                          [ { Name = "text"
                                              Type = TStr
                                              Opt = Required
                                              Annotations = Annotations.Empty } ]
                                        Annotations = Annotations.Empty } ] }

                        reached idl onlyTag
                    "a transparent union case that is not a single scalar member",
                    "transparent union case that is not a single scalar member",
                    fun () ->
                        let idl =
                            tinyIdl (TUnion("Text", [])) Required
                            |> withUnion
                                { Name = "Text"
                                  Params = []
                                  Cases =
                                    [ { Tag = "Inline"
                                        Fields =
                                          [ { Name = "text"
                                              Type = TStr
                                              Opt = Required
                                              Annotations = Annotations.Empty }
                                            { Name = "lang"
                                              Type = TStr
                                              Opt = Required
                                              Annotations = Annotations.Empty } ]
                                        Annotations = Annotations.Empty } ] }

                        reached
                            { idl with
                                Harden =
                                    { idl.Harden with
                                        TransparentUnions = [ "Text", "Inline" ] } }
                            onlyTag ]

              for name, fragment, reach in classes do
                  match reach () with
                  | None -> failtestf "%s was NOT refused — the class is unreached, or the backend now admits it" name
                  | Some why ->
                      Expect.stringContains why fragment (sprintf "the refusal of %s names the construct" name)

          // ---- the partition, per certification vocabulary -----------------

          testCase "every kind of every certification vocabulary is either modelled or refused BY NAME"
          <| fun _ ->
              for g in generated do
                  let verdicts = FStarTarget.partition g.Idl

                  Expect.equal
                      (List.length verdicts)
                      (List.length g.Idl.Kinds)
                      (sprintf "%s: the partition is total over the vocabulary's kinds" g.Module)

                  for v in verdicts do
                      match v.Refusal with
                      | Some why ->
                          Expect.isTrue
                              (why.Length > 20)
                              (sprintf "%s: the refusal of '%s' says what could not be expressed" g.Module v.Tag)
                      | None -> ()

                  // The rule is a SUBSET of what the target can express, never a second opinion
                  // about expressibility — a kind it selects that the target then refuses would
                  // be a generator that contradicts its own partition.
                  let expressible = verdicts |> List.filter (fun v -> v.Refusal.IsNone)

                  for tag in FStarTarget.proofKinds g.Idl do
                      Expect.isTrue
                          (expressible |> List.exists (fun v -> v.Tag = tag))
                          (sprintf
                              "%s: the proof vocabulary selected '%s', which the target cannot express"
                              g.Module
                              tag)

          testCase
              "the certification set is a real slice of the backend: the two wire shapes, the whole of each sample, and one named boundary"
          <| fun _ ->
              // A partition that could express nothing would satisfy the clause above. What is
              // asserted here is what the set was CHOSEN for. The reference vocabulary carries
              // the type-model remainder and one BOUNDARY — `Measure`, refused for its numeric
              // default and nothing else — so a change that widened the refusal, or narrowed it
              // to nothing, reads as a change of coverage rather than as ordinary drift. The
              // two samples are on the declared non-default wire shape, and every kind of each
              // is modelled: a sample whose kinds started falling out of the proof vocabulary
              // would be a backend that had stopped covering the shape the reference one lacks.
              let refused (g: Generated) =
                  FStarTarget.partition g.Idl
                  |> List.filter (fun v -> v.Refusal.IsSome)
                  |> List.map _.Tag

              let byModule = generated |> List.map (fun g -> g.Module, g) |> Map.ofList

              Expect.equal
                  (refused byModule["Vocabulary"])
                  [ "Measure" ]
                  "the reference vocabulary's one boundary is the numeric-default kind, and only that"

              Expect.equal
                  (selection ReferenceIdl.refIdl |> List.length)
                  (List.length ReferenceIdl.refIdl.Kinds - 1)
                  "and every other reference kind is modelled"

              // The cost rule is NOT what selects here, and the difference is asserted so that a
              // reader who reaches for `proofKinds` meets the measurement rather than re-taking
              // it: over the reference vocabulary the rule keeps one kind of five.
              Expect.equal
                  (FStarTarget.proofKinds ReferenceIdl.refIdl)
                  [ "Group" ]
                  "`proofKinds`, the UI-scale cost control, would keep one reference kind — which is why the committed model does not use it"

              // The same boundary, and only that boundary, is what the third-domain sample
              // meets: `Note.voice` and `Chord.voice` declare the integer default `1`. Pinned as
              // a set rather than as "non-empty" so that a sample kind falling out for any OTHER
              // reason reads as the coverage change it is.
              for m, boundary in
                  [ "DocVocabulary", Set.empty
                    "ScoreVocabulary", Set.ofList [ "Chord"; "Note" ] ] do
                  let g = byModule[m]

                  Expect.equal
                      g.Idl.Wire.NodeEnvelope
                      NodeEnvelopeShape.FlatKind
                      (sprintf "%s is on the flat envelope" m)

                  Expect.equal
                      (refused g |> Set.ofList)
                      boundary
                      (sprintf "%s: the sample's only boundary is the numeric-default kind set" m)

                  Expect.equal
                      (selection g.Idl |> List.length)
                      (List.length g.Idl.Kinds - Set.count boundary)
                      (sprintf "%s: and every other kind of the sample is modelled" m)

              Expect.equal
                  ReferenceIdl.refIdl.Wire.NodeEnvelope
                  NodeEnvelopeShape.NestedKind
                  "the reference vocabulary is on the nested envelope, so the set reaches both"

          // ---- the generation diff ------------------------------------------

          testCase
              "every committed proofs/<M>.fst and <M>Proofs.fst IS a fresh generation from its certification vocabulary"
          <| fun _ ->
              for g in generated do
                  let remedy =
                      sprintf
                          "VOCABULARY DRIFT — %s has moved and the committed F* files have not.\n"
                          (match g.Module with
                           | "Vocabulary" -> "the reference vocabulary"
                           | m -> "the vendored sample behind " + m)
                      + "Regenerate them with `dotnet run --project tests/Fuaran.Core.Tests -- --emit-fstar`,\n"
                      + "commit them, and re-run the proof leg: the model must still check, and the round trip\n"
                      + "must still discharge, over the new vocabulary."

                  for path, result in pair g do
                      let name = Path.GetFileName path

                      match result with
                      | Error e ->
                          failtestf "%s: the F* target refused the vocabulary: %s" name (CodegenError.describe e)
                      | Ok text ->
                          Expect.isTrue (File.Exists path) (sprintf "proofs/%s is committed" name)

                          Expect.equal
                              (lf text)
                              (lf (File.ReadAllText path))
                              (sprintf "proofs/%s has drifted from a fresh generation.\n%s" name remedy)

          testCase "no committed model was generated from anything but this project — the header names its source"
          <| fun _ ->
              // The generation diff above already implies this; it is stated on its own because
              // it is the sentence Phase 173 exists for. A model whose header named the shared
              // corpus would be a domain's proof back in the substrate's leg.
              for g in generated do
                  for path in [ modelPath g; proofsPath g ] do
                      let name = Path.GetFileName path
                      let text = File.ReadAllText path

                      Expect.stringContains
                          text
                          "tests/Fuaran.Core.Tests/"
                          (sprintf "proofs/%s names a source inside this project" name)

                      Expect.isFalse
                          (text.Contains "pinned wire-format IDL" || text.Contains "idl.json")
                          (sprintf "proofs/%s does not name the shared corpus as its source" name)

          // ---- the theorems emitter -------------------------------------------

          testCase "the theorems emitter produces a proof script over the same walk"
          <| fun _ ->
              // The committed `<M>Proofs.fst` are held above; this pins the property the pair
              // needs at a vocabulary nothing else owns: the two emitters agree about WHICH
              // vocabulary they are talking about. (Phase 150 measured the emitted round trip
              // NOT discharging at the UI vocabulary's twenty kinds, for a structural reason
              // `proofs/README.md`'s theorem 1 records; at the certification set it discharges,
              // which is why the scripts are committed since Phase 173. The per-kind lemma shape
              // that reaches UI scale is Phase 168's.)
              match
                  FStarTarget.vocabularyModule "M" (tinyIdl TStr Required) [ "Only" ],
                  FStarTarget.proofsModule "MP" "M" (tinyIdl TStr Required) [ "Only" ]
              with
              | Error e, _
              | _, Error e -> failtestf "the target refused a one-scalar vocabulary: %s" (CodegenError.describe e)
              | Ok model, Ok proofs ->
                  Expect.stringContains model "let rec dec_node" "the model declares the node decoder"
                  Expect.stringContains proofs "open M" "the proof script opens the model it is about"

                  Expect.stringContains
                      proofs
                      "dec_node (enc_node #num #flt x) == Ok x"
                      "and states the round trip over the generated definitions"

          testCase "the un-suffixed entry points emit the generator's own provenance, and a named one replaces it"
          <| fun _ ->
              // `vocabularyModule` / `proofsModule` are the shipped entry points a caller with no
              // named source uses; `…From` is what a caller with one passes. The default must not
              // claim a source it does not have — until Phase 173 it named this repository's
              // corpus and its check script in a package a domain generates from.
              let idl = tinyIdl TStr Required

              match
                  FStarTarget.vocabularyModule "M" idl [ "Only" ],
                  FStarTarget.vocabularyModuleFrom generated.Head.Provenance "M" idl [ "Only" ]
              with
              | Error e, _
              | _, Error e -> failtestf "the target refused a one-scalar vocabulary: %s" (CodegenError.describe e)
              | Ok plain, Ok named ->
                  Expect.stringContains plain "the IDL its caller supplied" "the default names no source it cannot know"
                  Expect.isFalse (plain.Contains "proofs/check.ps1") "and no script of this repository"
                  Expect.stringContains named "ReferenceIdl.fs" "a named provenance is what the header carries"
                  Expect.stringContains named "F* BACKEND" "and what the model is a property of" ]
