module Fuaran.Core.Tests.IdlFullVocabularyFuzzTests

open System
open System.IO
open System.Diagnostics
open Expecto
open Fuaran.Core.Idl
open Fuaran.Core.Tests.UiIdl

module G = Fuaran.Core.Tests.UiGenerated

// ---------------------------------------------------------------------------
// Phase 698 — generative cross-host conformance over the FULL vocabulary.
//
// The Phase 317 sweep in `IdlSpikeTests` runs 500 vectors over the 8-kind
// `miniIdl` and compares TWO legs (the interpreter and the generated TS module).
// That left 31 real kinds with no mechanical cross-leg check at all, and — because
// the sampler could not express a node ENVELOPE — left `state` / `style` /
// `accessibility` unexercised on every host but the fixed corpus.
//
// This sweep closes both. It samples the real `uiIdl` (every kind, envelope
// included) and compares THREE legs per vector:
//
//   1. `Encode.encode uiIdl v`      — the schema-driven interpreter, from the value.
//   2. `G.encodeNode`               — the generated F# module, over the value the
//                                     generated `G.decodeNode` reads back from leg 1.
//   3. the generated TS module      — independently emitted, run under node, from
//                                     the value (`Gen.typescriptValue`), not from
//                                     leg 1's bytes.
//
// **What leg 2 does and does not prove, stated plainly.** Its input is derived
// from leg 1's wire, because `Gen.fsharpValue` cannot yet emit the real
// vocabulary's value shapes (records, maps, hosted slots, sentinels), so there is
// no independent way to CONSTRUCT a generated-F# value for an arbitrary sampled
// vector. Comparing `decodeNode >> encodeNode` against the interpreter's bytes
// still catches an encoder that writes a field the interpreter does not (and vice
// versa) — the re-encode simply stops matching. What it cannot catch is a
// COMPENSATING pair, where the generated decoder's loss is exactly restored by the
// generated encoder. Leg 3 is value-derived and has no such gap, so the two legs
// are complementary rather than redundant.
//
// Reproducibility: every vector is a pure function of (seed, index) — `sampleNodes`
// draws from a seeded LCG in index order — so a failure report naming an index is
// enough to reproduce it, with no captured payload.
// ---------------------------------------------------------------------------

/// Every kind in the real vocabulary — the sampler's tag cycle and the generated
/// TS module's kind set, which must be the same set, or a kind missing from the TS
/// side surfaces as `"kind":undefined` rather than as a missing-kind error.
let private allKindTags = uiIdl.Kinds |> List.map (fun k -> k.Tag)

/// The seed. Any value works; this one is the date the sweep was brought up, and
/// it is pinned so the gate is the SAME vectors on every run and every machine.
let private seed = 20260818

/// The byte the harness uses to separate a vector index from its wire string.
/// Spelled as an escape rather than embedded literally: a raw control character in
/// source is invisible in review and survives a copy-paste only by luck.
let private sep = '\u0001'

/// **The vector budget, measured rather than inherited** (Phase 698 task 3).
///
/// `FUARAN_FUZZ_VECTORS` can RAISE the count for a soak run; it deliberately
/// cannot lower it, so the gate cannot be weakened from the environment.
let private vectorBudget =
    // ── What bring-up actually measured ──────────────────────────────────────
    //
    // Two questions were asked, and they gave very different answers.
    //
    // 1. WHERE DID THE REAL DIVERGENCES APPEAR? Both classes bring-up found showed
    //    up almost immediately: the generated TypeScript encoder's missing Ordinal
    //    key sort at vector **0**, and the first host-codec rejection at vector
    //    **2**. Sizing on that alone would justify a budget of about fifty, which
    //    is the trap — it measures the defects that happened to be present, not the
    //    depth at which a defect could hide.
    //
    // 2. HOW DEEP MUST THE SWEEP GO TO EXERCISE EVERY PLACE ONE COULD HIDE? The
    //    vocabulary declares 428 wire-visible field positions. The index at which
    //    the LAST of them is first sampled present — the saturation index — was
    //    measured over 20,000 vectors on four seeds:
    //
    //        seed 987654321 → 1317      seed 1         → 1902
    //        seed 20260818  → 2097      seed 20260726  → 3384   (the pinned seed is 20260818)
    //
    //    A single-point defect in the last-covered position (`CellKindErased`'s
    //    `Progress.labelFn`, on the pinned seed) is invisible below its index.
    //
    // The budget answers question 2, because question 1 cannot be asked about a
    // defect that does not exist yet. 4000 clears the worst of the four seeds with
    // headroom, so a future re-seed does not quietly stop covering the tail. It is
    // emphatically NOT the 500 the 8-kind sweep uses: that number was never
    // contradicted rather than ever chosen, and 2097 contradicts it here.
    //
    // Cost at 4000: the whole three-leg sweep runs in a couple of seconds, node
    // included, so there is no argument for trimming it.
    //
    // 13 of the 428 positions are NEVER covered at any budget, each for a
    // structural reason, all four seeds agreeing: six `RangePair`/`DateRangePair`
    // fields exist only inside `THosted` codec expressions and so are invisible to
    // a sampler that treats a hosted slot as opaque JSON; five `CurveCommand` case
    // fields lose every draw to the depth floor's nullary-case preference
    // (`CurveCommand.Close` takes no fields); and two `ButtonGroupItem` fields sit
    // four list/record levels below a node, where the floor empties the list.
    // Raising the sampler's depth would reach the last two groups and is a separate
    // change — recorded here so the gap is known rather than assumed absent.
    let floor' = 4000

    match Int32.TryParse(Environment.GetEnvironmentVariable "FUARAN_FUZZ_VECTORS") with
    | true, n when n > floor' -> n
    | _ -> floor'

/// One leg's disagreement with the interpreter, carrying everything needed to
/// reproduce and read it.
type private Divergence =
    { Index: int
      Leg: string
      Expected: string
      Actual: string }

let private render (d: Divergence) =
    sprintf "vector %d (seed %d) — %s\n    interpreter: %s\n    this leg   : %s" d.Index seed d.Leg d.Expected d.Actual

/// Run a generated TS module + harness under node. `None` when node is not on
/// PATH (the pre-existing skip, preserved).
let private runNode (source: string) : string option =
    let tmp =
        Path.Combine(Path.GetTempPath(), sprintf "fuaran-ui-fuzz-%s.mjs" (Guid.NewGuid().ToString("N")))

    File.WriteAllText(tmp, source)

    try
        let psi = ProcessStartInfo("node", "\"" + tmp + "\"")
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false

        match
            (try
                Some(Process.Start psi)
             with _ ->
                 None)
        with
        | None -> None
        | Some p ->
            let stdout = p.StandardOutput.ReadToEnd()
            let stderr = p.StandardError.ReadToEnd()
            p.WaitForExit()

            if p.ExitCode <> 0 then
                failtestf "node failed running the generated TS harness: %s" stderr

            Some stdout
    finally
        try
            File.Delete tmp
        with _ ->
            ()

[<Tests>]
let tests =
    testList
        "Phase 698 — generative cross-host conformance over the full vocabulary"
        [ testCase "the sampler draws node envelopes on every presence polarity" (fun _ ->
              // The sampler leg on its own, so a regression that stopped sampling
              // envelopes shows up HERE as a precise failure rather than as the
              // three-way sweep quietly going vacuous — that sweep would still pass,
              // green and meaningless, with every vector envelope-free.
              let vectors = Sample.sampleNodes uiIdl allKindTags seed vectorBudget

              // Phase 945 — narrow Switch shapes to the canonical wire (the
              // cross-field rule the IDL cannot state); see UiIdlSupport's note.
              let vectors = vectors |> List.map UiIdlSupport.canonicaliseVector

              let enveloped =
                  vectors
                  |> List.sumBy (function
                      | VNodeEnv _ -> 1
                      | _ -> 0)

              let bare =
                  vectors
                  |> List.sumBy (function
                      | VNode _ -> 1
                      | _ -> 0)

              Expect.isGreaterThan enveloped 0 "some vectors carry an envelope"
              Expect.isGreaterThan bare 0 "some vectors carry none (absence is sampled too)"

              // Every wire-visible envelope field must be drawn PRESENT somewhere: a
              // field never sampled present is a field this sweep does not cover.
              let wireEnvelope =
                  uiIdl.NodeFields
                  |> List.filter (fun f -> f.Opt <> HostOnly)
                  |> List.map (fun f -> f.Name)

              let seenPresent =
                  vectors
                  |> List.collect (function
                      | VNodeEnv(_, env, _, _) -> env |> List.map fst
                      | _ -> [])
                  |> Set.ofList

              for name in wireEnvelope do
                  Expect.isTrue
                      (seenPresent.Contains name)
                      (sprintf "envelope field '%s' was never sampled present" name)

              // A host-only envelope field has no wire projection, so it must never
              // be drawn present — the third polarity, and the one whose failure
              // would leak a host field onto the wire.
              for f in uiIdl.NodeFields |> List.filter (fun f -> f.Opt = HostOnly) do
                  Expect.isFalse
                      (seenPresent.Contains f.Name)
                      (sprintf "host-only envelope field '%s' was sampled onto the wire" f.Name))

          testCase "three-way: interpreter, generated F# and generated TypeScript agree on every vector" (fun _ ->
              let vectors = Sample.sampleNodes uiIdl allKindTags seed vectorBudget

              // Phase 945 — narrow Switch shapes to the canonical wire (the
              // cross-field rule the IDL cannot state); see UiIdlSupport's note.
              let vectors = vectors |> List.map UiIdlSupport.canonicaliseVector

              Expect.equal (List.length vectors) vectorBudget "the sampler produced the requested vectors"

              // ---- leg 1: the interpreter, the reference bytes ----
              let interpreter =
                  vectors
                  |> List.mapi (fun i v ->
                      match Encode.encode uiIdl v with
                      | Ok w -> w
                      | Error m -> failtestf "interpreter encode failed on vector %d (seed %d): %s" i seed m)

              // ---- leg 2: the generated F# module, in-process ----
              //
              // The generated decoder calls the HOST codecs for every `THosted` slot,
              // and a hosted slot's content grammar is the host's, not the IDL's — the
              // IDL states only that the position carries verbatim JSON. The sampler
              // therefore cannot draw content those codecs are obliged to accept, and
              // the real ones are strict (an aria role is a string; a row feed is an
              // array of row objects or the legacy sentinel; a `DataSource` carries
              // `columns`). A vector that POPULATES a hosted slot is consequently
              // outside leg 2's domain, and is recorded as such — a stated boundary,
              // not a swallowed failure. A rejection on a hosted-FREE vector is a real
              // failure and is reported as a divergence.
              //
              // Measured over 20,000 vectors at this seed: 15,695 populate a hosted
              // slot, 14,405 of those are rejected by a host codec, and **zero**
              // hosted-free vectors are rejected — so the boundary is exactly where
              // this says it is, and nothing else is hiding behind it.
              let hostedVectors = vectors |> List.map (Gen.usesHosted uiIdl)

              let mutable compared = 0
              let mutable outOfDomain = 0

              let fsharpDivergences =
                  List.zip interpreter hostedVectors
                  |> List.mapi (fun i (w, isHosted) ->
                      match (G.decodeNode w: Result<G.Node<obj>, string>) with
                      | Error e ->
                          if isHosted then
                              outOfDomain <- outOfDomain + 1
                              None
                          else
                              Some
                                  { Index = i
                                    Leg = "generated-F#"
                                    Expected = w
                                    Actual = "decodeNode failed on a hosted-FREE vector: " + e }
                      | Ok node ->
                          compared <- compared + 1
                          let reEncoded = G.encodeNode node

                          if reEncoded = w then
                              None
                          else
                              Some
                                  { Index = i
                                    Leg = "generated-F#"
                                    Expected = w
                                    Actual = reEncoded })
                  |> List.choose id

              // ---- leg 3: the generated TypeScript module, under node ----
              let tsModule = Gen.typescriptModule uiIdl allKindTags

              let vectorsJs =
                  vectors
                  |> List.mapi (fun i v -> sprintf "  [%d, %s]," i (Gen.typescriptValue v))
                  |> String.concat "\n"

              let harness =
                  tsModule
                  + "\n\nconst __vectors = [\n"
                  + vectorsJs
                  + "\n];\n"
                  // Fault-isolate per vector: one throwing vector must not cost the
                  // report for all the others, and the index has to survive into it.
                  + "for (const [i, node] of __vectors) {\n"
                  + "  try { console.log(i + '\\u0001' + encodeNode(node)); }\n"
                  + "  catch (e) { console.log(i + '\\u0001' + 'TS-THREW: ' + (e && e.message ? e.message : e)); }\n"
                  + "}\n"

              let tsDivergences =
                  match runNode harness with
                  | None -> None // node absent — leg 3 skipped, reported below
                  | Some stdout ->
                      let got =
                          stdout.Replace("\r\n", "\n").Split('\n')
                          |> Array.filter (fun l -> l <> "")
                          |> Array.map (fun l ->
                              let parts = l.Split(sep)
                              int parts[0], parts[1])
                          |> Map.ofArray

                      interpreter
                      |> List.mapi (fun i w ->
                          match Map.tryFind i got with
                          | None ->
                              Some
                                  { Index = i
                                    Leg = "generated-TS"
                                    Expected = w
                                    Actual = "(no TS output)" }
                          | Some actual when actual <> w ->
                              Some
                                  { Index = i
                                    Leg = "generated-TS"
                                    Expected = w
                                    Actual = actual }
                          | Some _ -> None)
                      |> List.choose id
                      |> Some

              let all = fsharpDivergences @ (defaultArg tsDivergences [])

              if not (List.isEmpty all) then
                  // Report the FIRST divergence per leg in full, plus the index
                  // census — so a bring-up (or a soak) run measures the budget in one
                  // pass instead of one failure at a time.
                  let firstPerLeg =
                      all
                      |> List.groupBy (fun d -> d.Leg)
                      |> List.map (fun (_, ds) -> ds |> List.minBy (fun d -> d.Index) |> render)
                      |> String.concat "\n"

                  let census =
                      all
                      |> List.groupBy (fun d -> d.Leg)
                      |> List.map (fun (leg, ds) ->
                          sprintf
                              "%s: %d of %d diverged; first indices %A"
                              leg
                              (List.length ds)
                              vectorBudget
                              (ds |> List.map (fun d -> d.Index) |> List.truncate 12))
                      |> String.concat "\n"

                  failtestf "cross-host divergence over the full vocabulary\n%s\n\n%s" firstPerLeg census

              // Leg 2 must not quietly become vacuous. Observed at this seed: about
              // 28% of vectors decode and are compared, the rest being hosted-slot
              // content no host codec was ever promised. A floor well under that
              // catches a change that collapses the comparable set without failing on
              // ordinary sampling variance.
              Expect.isGreaterThan
                  compared
                  (vectorBudget / 6)
                  (sprintf
                      "the generated-F# leg compared too few vectors to mean anything (compared %d, hosted-slot vectors out of its domain %d, of %d)"
                      compared
                      outOfDomain
                      vectorBudget)

              match tsDivergences with
              | None -> skiptest "node not on PATH — the generated-TypeScript leg was skipped (legs 1+2 ran)"
              | Some _ -> ()) ]
