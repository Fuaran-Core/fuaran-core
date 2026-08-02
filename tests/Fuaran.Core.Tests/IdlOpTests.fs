module Fuaran.Core.Tests.IdlOpTests

open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Tests.UiIdl

// ---------------------------------------------------------------------------
// Phase 703 — the op vocabulary in the IDL, certified against the op corpus.
//
// The `Idl` record had no op vocabulary at all: `WIRE_FORMAT.md` §3.4, the
// 22-fixture op corpus and `decodeOp` sat entirely outside it. That left the
// schema leg unable to state the wire's second root, and capped every derived
// artefact — `idl.json`, the 700 diff classifier — at the node vocabulary.
//
// The certification discipline is the same oracle the node legs use, and it is
// the only one that means anything for a codec: DECODE each committed fixture
// through the IDL, RE-ENCODE it, and compare BYTES. A decode alone proves the
// shapes parse; the round-trip proves nothing was silently dropped, reordered or
// widened on the way through — a field the IDL forgot to declare decodes fine and
// vanishes on re-encode, and only the byte compare catches it.
//
// SCOPE — shapes only, and the boundary is load-bearing. Apply SEMANTICS stay
// hand-written above the IDL: what `UpdateProp`'s dotted `path` addresses, whether
// a `target` resolves, §3.4's error mapping. This is the same split the node legs
// already run, where decode POLICY (§16 lenient-accept, the reject set) composes
// above a structural decoder rather than inside it.
// ---------------------------------------------------------------------------

let private opFixtures =
    IdlArtifactTests.tryFindCorpusRoot ()
    |> Option.map (fun root -> Path.Combine(root, "ops"))
    |> Option.filter Directory.Exists
    |> Option.map (fun dir ->
        Directory.GetFiles(dir, "*.json")
        |> Array.sortWith (fun a b -> System.String.CompareOrdinal(a, b))
        |> List.ofArray)
    |> Option.defaultValue []

/// The op tags the corpus actually exercises — the coverage denominator.
let private coveredTags =
    opFixtures
    |> List.choose (fun path ->
        match Json.parse (File.ReadAllText path) with
        | Ok(JObj fs) ->
            fs
            |> List.tryPick (function
                | "$type", JStr t -> Some t
                | _ -> None)
        | _ -> None)
    |> List.distinct
    |> Set.ofList

[<Tests>]
let tests =
    testList
        "Phase 703 — the op vocabulary"
        [

          // ── the declaration ──────────────────────────────────────────────

          testCase "the vocabulary declares the wire's op set" (fun _ ->
              let tags = uiIdl.Ops |> List.map _.Tag |> Set.ofList

              Expect.equal
                  tags
                  (set
                      [ "Batch"
                        "EditNode"
                        "InsertChild"
                        "MoveNode"
                        "RemoveNode"
                        "ReorderChildren"
                        "ReplaceBinding"
                        "ReplaceRoot"
                        "UpdateProp"
                        "UpdateState"
                        "UpdateStyle" ])
                  "the 11 §3.4 op cases")

          testCase "InsertChild carries no `position` — read from the corpus, not old prose" (fun _ ->
              // Phase 681 removed it. The phase body that commissioned this work said
              // to read the bytes rather than the prose, and this is why.
              let insert = uiIdl.Ops |> List.find (fun o -> o.Tag = "InsertChild")
              let names = insert.Fields |> List.map _.Name |> Set.ofList
              Expect.equal names (set [ "child"; "parentId" ]) "no positional index survives")

          // ── the certification ────────────────────────────────────────────

          testCase "every op fixture round-trips through the IDL byte-identically" (fun _ ->
              match opFixtures with
              | [] -> skiptest "wire-format-fixtures not checked out alongside — certification skipped"
              | files ->
                  let failures =
                      [ for path in files do
                            let wire = (File.ReadAllText path).Trim()

                            match Decode.decodeOp uiIdl wire with
                            | Error e -> Path.GetFileName path, "decode failed: " + e
                            | Ok value ->
                                match Encode.encodeOp uiIdl value with
                                | Error e -> Path.GetFileName path, "re-encode failed: " + e
                                | Ok actual when actual <> wire ->
                                    Path.GetFileName path,
                                    sprintf "bytes differ\n    expected: %s\n    actual:   %s" wire actual
                                | Ok _ -> () ]

                  Expect.isEmpty
                      failures
                      (sprintf
                          "%d of %d op fixtures failed:\n  %s"
                          failures.Length
                          files.Length
                          (failures |> List.map (fun (f, m) -> f + " — " + m) |> String.concat "\n  ")))

          testCase "the certification is not vacuous — the corpus was actually read" (fun _ ->
              // A skip that read zero fixtures looks identical to a pass.
              match IdlArtifactTests.tryFindCorpusRoot () with
              | None -> skiptest "wire-format-fixtures not checked out alongside"
              | Some _ -> Expect.equal (List.length opFixtures) 22 "the whole op family")

          testCase "every declared op is exercised by at least one fixture" (fun _ ->
              // The other direction: a declared op no fixture covers is a shape
              // nothing has ever validated, which is exactly the state the node
              // vocabulary was in for `Separator` before the stage-3 swap found it.
              match opFixtures with
              | [] -> skiptest "wire-format-fixtures not checked out alongside"
              | _ ->
                  let declared = uiIdl.Ops |> List.map _.Tag |> Set.ofList
                  let unexercised = Set.difference declared coveredTags

                  Expect.isEmpty
                      unexercised
                      (sprintf "declared but never in a fixture: %s" (String.concat ", " unexercised)))

          testCase "a malformed op is rejected, not silently absorbed" (fun _ ->
              let unknownOp = """{"$type":"NoSuchOp","target":"n1"}"""
              let missingField = """{"$type":"MoveNode","target":"n1"}"""

              Expect.isError (Decode.decodeOp uiIdl unknownOp) "an unknown op tag"
              Expect.isError (Decode.decodeOp uiIdl missingField) "a missing required field")

          testCase "Batch recurses — a nested op is decoded as an op, not as opaque JSON" (fun _ ->
              // `TOp` exists for exactly this. If `Batch.ops` were `TJson` the
              // fixture would still round-trip byte-identically while carrying no
              // structure at all, so the byte gate alone cannot prove this.
              match opFixtures |> List.tryFind (fun p -> Path.GetFileName p = "op-batch.json") with
              | None -> skiptest "op-batch fixture not present"
              | Some path ->
                  match Decode.decodeOp uiIdl ((File.ReadAllText path).Trim()) with
                  | Error e -> failtestf "decode failed: %s" e
                  | Ok(VUnion("Batch", fields)) ->
                      match fields |> List.tryFind (fun (n, _) -> n = "ops") |> Option.map snd with
                      | Some(VList inner) ->
                          Expect.isNonEmpty inner "the batch carries nested ops"

                          for op in inner do
                              match op with
                              | VUnion(tag, _) ->
                                  Expect.isTrue
                                      (uiIdl.Ops |> List.exists (fun o -> o.Tag = tag))
                                      (sprintf "nested '%s' resolved against the op vocabulary" tag)
                              | other -> failtestf "nested op decoded as %A, not a tagged op" other
                      | other -> failtestf "Batch.ops decoded as %A" other
                  | Ok other -> failtestf "op-batch decoded as %A" other)

          testCase "EditNode.newKind is a BARE kind, not a node" (fun _ ->
              // The `TKind` / `TNode` distinction, which the wire makes by the
              // presence of `id`. Decoding a bare kind as a node would fail; decoding
              // it as opaque JSON would succeed and lose the vocabulary.
              match opFixtures |> List.tryFind (fun p -> Path.GetFileName p = "op-editnode.json") with
              | None -> skiptest "op-editnode fixture not present"
              | Some path ->
                  match Decode.decodeOp uiIdl ((File.ReadAllText path).Trim()) with
                  | Ok(VUnion("EditNode", fields)) ->
                      match fields |> List.tryFind (fun (n, _) -> n = "newKind") |> Option.map snd with
                      | Some(VUnion(tag, _)) ->
                          Expect.isTrue
                              (uiIdl.Kinds |> List.exists (fun k -> k.Tag = tag))
                              (sprintf "'%s' resolved against the KIND vocabulary" tag)
                      | other -> failtestf "newKind decoded as %A — expected a bare tagged kind" other
                  | other -> failtestf "op-editnode decoded as %A" other)

          // ── the derived artefacts pick the vocabulary up ─────────────────

          testCase "the schema gains the second root and the op definitions" (fun _ ->
              let schema = Gen.jsonSchema uiIdl

              Expect.stringContains schema "#/$defs/TreeOp" "the root alternation names TreeOp"
              Expect.stringContains schema "\"NodeKind\"" "the kind alternation is named, for TKind to reference"

              for o in uiIdl.Ops do
                  Expect.stringContains schema ("\"" + o.Tag + "\"") (sprintf "op '%s' has a definition" o.Tag))

          testCase "an op-free IDL's schema root is unchanged" (fun _ ->
              // The whole additive claim: a domain that declares no ops gets exactly
              // the single-root schema it had before this phase.
              let schema = Gen.jsonSchema { uiIdl with Ops = [] }
              Expect.stringContains schema "\"$ref\":\"#/$defs/Node\"" "single root"
              Expect.isFalse (schema.Contains "TreeOp") "no op vocabulary leaks in")

          testCase "idl.json carries the op vocabulary" (fun _ ->
              let text = Artifact.render uiIdl

              for o in uiIdl.Ops do
                  Expect.stringContains text ("\"" + o.Tag + "\"") (sprintf "the artefact publishes op '%s'" o.Tag)

              // Additive, on the same terms: an op-free vocabulary's artefact gains
              // nothing, so every pre-703 emission is byte-for-byte what it was.
              //
              // Asserted on an op TAG, not on the key `"ops"` — `Action.Chain` already
              // has a field of that name, so the obvious probe answers a different
              // question and passes either way.
              let opFree = Artifact.render { uiIdl with Ops = [] }
              Expect.isFalse (opFree.Contains "\"ReorderChildren\"") "an op-free vocabulary adds no op family") ]
