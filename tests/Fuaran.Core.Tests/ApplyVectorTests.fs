module Fuaran.Core.Tests.ApplyVectorTests

open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

// ---------------------------------------------------------------------------
//  The `apply/` fixture family, checked from both ends.
//
//  `ApplyVectorExport` renders it; nothing here writes into the shared corpus.
//  Emitting is an explicit command (`--emit-apply <corpus dir>`), because the
//  corpus is a separate repository and a suite that wrote into it on every run
//  would dirty a shared clone.
//
//  Four claims, and the order matters:
//
//   1. Every vector is CONSUMABLE and TRUE. Each is decoded back through the
//      envelope's own codec — the surface a host has — applied with
//      `Ops.apply`, and its recorded answer RECOMPUTED rather than trusted.
//   2. The go-red. A deliberately wrong expected hash, a wrong expected tree
//      and a wrong rejection class each REDDEN that check, by vector id. A
//      corpus leg that cannot fail certifies nothing, and this family's whole
//      value is that it can.
//   3. The sample is ADEQUATE to the description: every skeleton op appears,
//      both verdicts appear, and every rejection class the engine can raise
//      from a wire document is reached. A family that silently stopped
//      carrying a clause would leave a host certifying less than the file says.
//   4. The committed artefacts are CURRENT — byte-identical to what this kit
//      renders now, for the vectors AND the family manifest beside them.
// ---------------------------------------------------------------------------

/// The rejection classes this family claims to reach, by their `$type` token. `notAContainer` and
/// `rejected` are deliberately absent: the first needs a container predicate the engine is HANDED
/// rather than reads from a document, and the second is the domain extension point, so neither is
/// expressible as a wire vector at all. The family description says so; this list is the assertion.
let private reachableClasses =
    [ "duplicateId"
      "unknownNode"
      "cannotRemoveRoot"
      "wouldNestUnderSelf"
      "reorderMismatch" ]

let private classOf (rejection: string) : string =
    match Json.parse rejection with
    | Ok(JObj ms) ->
        ms
        |> List.tryPick (fun (k, v) ->
            match k, v with
            | "$type", JStr s -> Some s
            | _ -> None)
        |> Option.defaultValue ""
    | _ -> ""

[<Tests>]
let tests =
    testList
        "ApplyVectors"
        [

          testCase "every rendered vector decodes through the envelope and records what the reference answered"
          <| fun _ ->
              match ApplyVectorExport.parseVectors (ApplyVectorExport.renderVectors ()) with
              | Error m -> failtest m
              | Ok vectors ->
                  Expect.isNonEmpty vectors "the family renders at least one vector"
                  let failures = vectors |> List.choose ApplyVectorExport.checkVector
                  Expect.isEmpty failures (sprintf "%A" failures)

          testCase "a wrong expected HASH reddens the leg"
          <| fun _ ->
              // Perturbed in the RENDERED TEXT rather than in a record, so the leg is proved to fail
              // through the same path a committed file takes: parse, decode, apply, compare.
              let rendered = ApplyVectorExport.renderVectors ()

              let good =
                  ApplyVectorExport.hashOf (ApplyVectorExport.encodeTree (Reference.sample ()))

              Expect.stringStarts good "sha256:" "the digest convention is `sha256:` plus hex"

              let firstHash =
                  match ApplyVectorExport.parseVectors rendered with
                  | Ok vs -> vs |> List.tryPick (fun v -> v.ExpectedHash)
                  | Error m -> failtest m

              match firstHash with
              | None -> failtest "no accept vector carried a hash — the perturbation has nothing to bite on"
              | Some h ->
                  // flip one hex digit of the first recorded digest
                  let flipped =
                      let tail = h.Substring(7)
                      "sha256:" + (if tail.[0] = '0' then "1" else "0") + tail.Substring(1)

                  let perturbed = rendered.Replace(h, flipped)
                  Expect.notEqual perturbed rendered "the perturbation changed the rendered bytes"

                  match ApplyVectorExport.parseVectors perturbed with
                  | Error m -> failtest m
                  | Ok vectors ->
                      let failures = vectors |> List.choose ApplyVectorExport.checkVector

                      Expect.isNonEmpty
                          failures
                          "a corrupted expected hash MUST redden this leg — a check that cannot fail is not a check"

          testCase "a wrong expected TREE and a wrong rejection CLASS each redden the leg"
          <| fun _ ->
              match ApplyVectorExport.parseVectors (ApplyVectorExport.renderVectors ()) with
              | Error m -> failtest m
              | Ok vectors ->
                  let accepted =
                      vectors |> List.find (fun v -> v.Verdict = "accept" && v.ExpectedTree.IsSome)

                  let wrongTree =
                      { accepted with
                          ExpectedTree = Some "{\"children\":[],\"id\":\"not-the-answer\",\"kind\":\"doc\"}" }

                  Expect.isSome (ApplyVectorExport.checkVector wrongTree) "a corrupted expected tree MUST be reported"

                  let rejected = vectors |> List.find (fun v -> v.Verdict = "reject")

                  let wrongClass =
                      { rejected with
                          Rejection = Some "{\"$type\":\"cannotRemoveRoot\"}" }

                  // The `cannotRemoveRoot` vector itself would not be perturbed by that value, so
                  // pick one whose real class differs — asserted rather than assumed.
                  if classOf (Option.defaultValue "" rejected.Rejection) <> "cannotRemoveRoot" then
                      Expect.isSome
                          (ApplyVectorExport.checkVector wrongClass)
                          "a corrupted rejection class MUST be reported"

          testCase "the sample reaches every op, both verdicts, and every reachable rejection class"
          <| fun _ ->
              match ApplyVectorExport.parseVectors (ApplyVectorExport.renderVectors ()) with
              | Error m -> failtest m
              | Ok vectors ->
                  for op in [ "InsertChild"; "RemoveNode"; "MoveNode"; "ReorderChildren"; "Batch" ] do
                      Expect.isTrue
                          (vectors |> List.exists (fun v -> v.Case = op))
                          (sprintf "no vector was rendered for `%s`" op)

                  Expect.isTrue (vectors |> List.exists (fun v -> v.Verdict = "accept")) "an accepted op was rendered"
                  Expect.isTrue (vectors |> List.exists (fun v -> v.Verdict = "reject")) "a refused op was rendered"

                  let reached =
                      vectors
                      |> List.choose (fun v -> v.Rejection |> Option.map classOf)
                      |> Set.ofList

                  for cls in reachableClasses do
                      Expect.isTrue (Set.contains cls reached) (sprintf "no vector reaches the `%s` refusal" cls)

                  // and the atomicity pair: the batch's first op alone is ACCEPTED, so the batch's
                  // refusal is about the batch rather than about that op.
                  let alone = vectors |> List.tryFind (fun v -> v.Id = "batch-first-op-alone-accept")

                  let together =
                      vectors |> List.tryFind (fun v -> v.Id = "batch-reject-all-or-nothing")

                  match alone, together with
                  | Some a, Some t ->
                      Expect.equal a.Verdict "accept" "the batch's first op succeeds on its own"
                      Expect.equal t.Verdict "reject" "the batch as a whole is refused"
                  | _ -> failtest "the Batch atomicity pair is not in the sample"

          testCase "the envelope round-trips, and the rendered artefacts are LF-only and byte-stable"
          <| fun _ ->
              let tree = Reference.sample ()
              let encoded = ApplyVectorExport.encodeTree tree

              match ApplyVectorExport.decodeTree encoded with
              | Error m -> failtest ("the envelope did not decode its own output: " + m)
              | Ok back ->
                  Expect.equal
                      (ApplyVectorExport.encodeTree back)
                      encoded
                      "decode ∘ encode is the identity on the envelope's own bytes"

              let op = Batch [ InsertChild("a", RNode.node "z" "para" []); RemoveNode "b1" ]
              let encodedOp = ApplyVectorExport.encodeOp op

              match ApplyVectorExport.decodeOp encodedOp with
              | Error m -> failtest ("the op envelope did not decode its own output: " + m)
              | Ok backOp ->
                  Expect.equal (ApplyVectorExport.encodeOp backOp) encodedOp "the op envelope round-trips too"

              for render in [ ApplyVectorExport.renderVectors; ApplyVectorExport.renderManifest ] do
                  let once = render ()
                  let twice = render ()
                  Expect.equal once twice "two renders of the same kit produce the same bytes"

                  Expect.isFalse
                      (once.Contains "\r")
                      "the corpus is byte-compared across three operating systems — no CR may reach it"

          testCase
              "the committed conformance/apply artefacts are the ones this kit renders, and each vector is true of this engine"
          <| fun _ ->
              // Phase 172: the family is AUTHORED here — `conformance/apply/` at this repository's
              // root — and the shared corpus carries a declared copy of both files. Oracle and
              // freshness are asked of the committed files in this checkout; no corpus needed.
              let root = OwnedConformance.root ()

              let pairs =
                  [ ApplyVectorExport.vectorsPath root, ApplyVectorExport.renderVectors
                    ApplyVectorExport.manifestPath root, ApplyVectorExport.renderManifest ]

              for path, _ in pairs do
                  if not (File.Exists path) then
                      failtestf
                          "this repository carries no %s at '%s' — re-run `--emit-apply` (no argument writes into conformance/) and commit the result"
                          (Path.GetFileName path)
                          path

              // First the ORACLE question — is what the file records still true of this
              // engine? — because that is the failure a host would suffer.
              let committed = File.ReadAllText(ApplyVectorExport.vectorsPath root)

              match ApplyVectorExport.parseVectors committed with
              | Error m -> failtest ("the committed vectors did not read: " + m)
              | Ok vectors ->
                  let failures = vectors |> List.choose ApplyVectorExport.checkVector

                  Expect.isEmpty failures (sprintf "the committed vectors disagree with this engine: %A" failures)

              // Then the FRESHNESS question. Distinct from the above: a rendering change (a new
              // vector, a reworded description) leaves every vector true and the file stale.
              for path, render in pairs do
                  Expect.equal
                      (File.ReadAllText path)
                      (render ())
                      (sprintf
                          "the committed conformance/apply/%s is not what this kit renders — re-run `--emit-apply` (no argument) and commit conformance/"
                          (Path.GetFileName path))

          testCase "the corpus copy of apply/ is fresh (opt-in: FUARAN_CORE_CORPUS_FRESHNESS)"
          <| fun _ ->
              // The copy-freshness leg, on the same terms as the laws/ one: Phase 130's absent-
              // corpus failure preserved on the leg it was written for, the registry's
              // `fingerprint` equality so this leg and `roadmapctl copies` agree, asked for by
              // name (CI does) and skipped by name otherwise.
              match SiblingCorpus.resolve ApplyVectorExport.familyDirName with
              | SiblingCorpus.NotAsked why -> skiptest why
              | SiblingCorpus.Absent why -> failtest why
              | SiblingCorpus.Found root ->
                  let owned = OwnedConformance.root ()

                  let pairs =
                      [ ApplyVectorExport.vectorsPath root, ApplyVectorExport.vectorsPath owned
                        ApplyVectorExport.manifestPath root, ApplyVectorExport.manifestPath owned ]

                  for copy, source in pairs do
                      if not (File.Exists copy) then
                          failtestf
                              "the corpus at '%s' carries no %s — re-run `--emit-apply <corpus dir>` and commit the corpus"
                              root
                              (Path.GetFileName copy)

                      if not (File.Exists source) then
                          failtestf
                              "this repository carries no %s at '%s' — nothing to compare the copy against"
                              (Path.GetFileName source)
                              source

                      Expect.equal
                          (OwnedConformance.fingerprint (File.ReadAllText copy))
                          (OwnedConformance.fingerprint (File.ReadAllText source))
                          (sprintf
                              "the corpus copy '%s' is STALE against this repository's conformance/%s/%s — re-run `--emit-apply <corpus dir>` and commit the corpus (copies.json names the same command)"
                              copy
                              ApplyVectorExport.familyDirName
                              (Path.GetFileName source)) ]
