module Fuaran.Core.Tests.IdlFStarTargetTests

open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Phase 150 — the F* PROOF-MODEL target, and the GENERATION DIFF that holds the committed
// `proofs/Vocabulary.fst` to a fresh generation from the pinned corpus.
//
// The diff is the same discipline the proof leg already applies to the extracted oracle —
// `proofs/oracle/<M>.fs` must be byte-identical to a fresh extraction of `<M>.fst`, or the
// oracle the suite runs is not the model the theorem is about. Here the pair is one step
// further up: `proofs/Vocabulary.fst` must be byte-identical to a fresh generation from
// `idl.json`, or the vocabulary the theorem is about is not the vocabulary the
// specification declares. An IDL that moves without a regeneration is VOCABULARY DRIFT and
// this is where it is caught.
//
// It lives in the Expecto suite rather than in `check.ps1`'s PowerShell because the
// generator is F#: the leg invokes it under the `Proofs.Vocabulary` filter, beside the two
// families it already runs, and the repository gate runs it with everything else.
//
// The shard for this phase named `tests/Fuaran.Core.Tests/IdlCodegenTests.fs` as the home
// for the emitter's cases. No such file exists — the IDL codegen suites in this tree are
// split by subject (`IdlCodegenEolTests`, `IdlWireShapeTests`, `IdlAnnotationTests`, …) —
// so this is one more of those, named for its own subject.
// ---------------------------------------------------------------------------

let private repoRoot = Path.GetDirectoryName(Snapshots.repoFile "Fuaran.Core.slnx")
let private modelPath = Path.Combine(repoRoot, "proofs", "Vocabulary.fst")

/// The corpus family the resolver anchors on; `idl.json` sits at the corpus ROOT beside it.
let private corpusFamily = "nodes"

let private lf (s: string) = s.Replace("\r\n", "\n")

/// A minimal vocabulary with one kind, used by the refusal cases. Written by hand rather
/// than cut down from the corpus, so a corpus change cannot quietly make a refusal case
/// stop exercising the construct it is about.
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

/// The pinned corpus's IDL, or the reason there is none — read for the regeneration COMMAND,
/// whose invocation is its own ask (Phase 172: the test legs below go through `resolve`, which
/// is gated on `FUARAN_CORE_CORPUS_FRESHNESS`; a command the operator ran is not).
let private pinnedIdl () : Result<Idl, string> =
    SiblingCorpus.locate corpusFamily
    |> Result.bind (fun root -> Artifact.parse (File.ReadAllText(Path.Combine(root, "idl.json"))))

/// `--emit-fstar` — rewrite the committed model from the pinned corpus. The command the generation
/// diff names when it fails, and the only sanctioned way that file changes.
let emit () : int =
    match pinnedIdl () with
    | Error why ->
        eprintfn "--emit-fstar: %s" why
        2
    | Ok idl ->
        let selection = FStarTarget.proofKinds idl

        let write (path: string) (result: Result<string, CodegenError>) =
            match result with
            | Error e ->
                eprintfn "--emit-fstar: %s" (CodegenError.describe e)
                2
            | Ok text ->
                File.WriteAllText(path, lf text, System.Text.UTF8Encoding false)
                printfn "regenerated %s" path
                0

        let written =
            write modelPath (FStarTarget.vocabularyModule "Vocabulary" idl selection)

        printfn
            "%d of %d kinds modelled; the rest are named in the emitted header"
            (List.length selection)
            (List.length idl.Kinds)

        written

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

          // ---- the partition -----------------------------------------------

          testCase "every kind of the pinned corpus is either modelled or refused BY NAME"
          <| fun _ ->
              match SiblingCorpus.resolve corpusFamily with
              | SiblingCorpus.NotAsked why -> skiptest why
              | SiblingCorpus.Absent why -> failtest why
              | SiblingCorpus.Found root ->
                  let idl =
                      match Artifact.parse (File.ReadAllText(Path.Combine(root, "idl.json"))) with
                      | Ok i -> i
                      | Error e -> failtestf "the pinned corpus idl.json did not parse: %s" e

                  let verdicts = FStarTarget.partition idl

                  Expect.equal
                      (List.length verdicts)
                      (List.length idl.Kinds)
                      "the partition is total over the vocabulary's kinds"

                  for v in verdicts do
                      match v.Refusal with
                      | Some why ->
                          Expect.isTrue
                              (why.Length > 20)
                              (sprintf "the refusal of '%s' says what could not be expressed" v.Tag)
                      | None -> ()

                  // A partition that could express nothing, or a proof vocabulary that covered
                  // nothing, would satisfy every clause above. The two counts are asserted
                  // separately because they answer different questions and can move apart: what
                  // the TARGET can express is a property of the model's boundary, and what the
                  // PROOF VOCABULARY covers is the declared cost rule applied to it.
                  let expressible =
                      FStarTarget.partition idl |> List.filter (fun v -> v.Refusal.IsNone)

                  Expect.isGreaterThan
                      (List.length expressible)
                      30
                      "the target expresses the bulk of the vocabulary, not a token corner of it"

                  Expect.isGreaterThan
                      (FStarTarget.proofKinds idl |> List.length)
                      15
                      "and the proof vocabulary is a real slice of it"

                  // The rule is a SUBSET of what the target can express, never a second opinion
                  // about expressibility — a kind it selects that the target then refuses would
                  // be a generator that contradicts its own partition.
                  for tag in FStarTarget.proofKinds idl do
                      Expect.isTrue
                          (expressible |> List.exists (fun v -> v.Tag = tag))
                          (sprintf "the proof vocabulary selected '%s', which the target cannot express" tag)

          // ---- the generation diff ------------------------------------------

          testCase "the committed proofs/Vocabulary.fst IS a fresh generation from the pinned IDL"
          <| fun _ ->
              match SiblingCorpus.resolve corpusFamily with
              | SiblingCorpus.NotAsked why -> skiptest why
              | SiblingCorpus.Absent why -> failtest why
              | SiblingCorpus.Found root ->
                  let idl =
                      match Artifact.parse (File.ReadAllText(Path.Combine(root, "idl.json"))) with
                      | Ok i -> i
                      | Error e -> failtestf "the pinned corpus idl.json did not parse: %s" e

                  let selection = FStarTarget.proofKinds idl

                  let remedy =
                      "VOCABULARY DRIFT — the IDL has moved and the committed F* model has not.\n"
                      + "Regenerate it with `dotnet run --project tests/Fuaran.Core.Tests -- --emit-fstar`,\n"
                      + "commit it, and re-run the proof leg: the model must still check over the new vocabulary."

                  match FStarTarget.vocabularyModule "Vocabulary" idl selection with
                  | Error e -> failtestf "the F* target refused the pinned corpus: %s" (CodegenError.describe e)
                  | Ok text ->
                      Expect.isTrue (File.Exists modelPath) "proofs/Vocabulary.fst is committed"

                      Expect.equal
                          (lf text)
                          (lf (File.ReadAllText modelPath))
                          (sprintf "proofs/Vocabulary.fst has drifted from a fresh generation.\n%s" remedy)

          // ---- the theorems emitter, which emits and is NOT committed ---------

          testCase "the theorems emitter produces a proof script over the same walk"
          <| fun _ ->
              // `proofsModule` is a shipped part of the target and is exercised here, but its output
              // is not committed beside the model. Measured on the pinned prover, the emitted round
              // trip discharges for a SMALL vocabulary — green at one kind and at eight — and does
              // NOT at the twenty this corpus's proof vocabulary selects: the widest kind's arm
              // (`FileUpload`, eleven members, five of them conditional) is not proved even at
              // `--z3rlimit 200`, because a decoder reading eleven members off an object with five
              // conditional cells puts thirty-two object shapes into one query. Committing a `.fst`
              // that does not verify would be worse than committing none, so there is none, and
              // `proofs/README.md`'s theorem 1 section carries the measurement and the remedies
              // already tried. What this case pins is the property a committed pair would have
              // needed, and the one a later phase builds on: the two emitters agree about WHICH
              // vocabulary they are talking about.
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
                      "and states the round trip over the generated definitions" ]
