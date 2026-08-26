namespace Fuaran.Core.Idl

open Fuaran.Core

// ---------------------------------------------------------------------------
// The spike: what a proposed vocabulary change costs, measured rather than
// argued.
//
// **Branchless, and that is the design.** The obvious shape for a spike is "cut
// a branch, apply the delta to the declaration, regenerate, run the suite" — and
// it is the wrong shape here for three reasons. It mutates a checkout, so an
// abandoned spike leaves residue exactly where residue is most expensive; it
// costs minutes rather than seconds, which is the difference between spiking
// every candidate and spiking none; and it puts a WRITE to the vocabulary inside
// a pipeline whose foundational rule is that it never writes one. Applying the
// delta to an in-memory `Idl` value gives the same answers with none of that:
// the post-delta vocabulary exists for the duration of one function call and is
// then garbage.
//
// **What the legs prove, and what they cannot.**
//
//   * `generate` proves the change is EXPRESSIBLE by the generator — that the
//     F#, TypeScript and JSON-schema legs all emit. A codegen failure here is a
//     real finding: it means the shape proposed is not one the engine can carry.
//   * `corpus` proves the change is ADDITIVE against real recorded wire — every
//     document the base vocabulary accepted, the post vocabulary accepts, and
//     encodes to the same bytes. This is the claim "additive by construction"
//     checked rather than asserted.
//   * `fuzz` proves the change round-trips over the delta's own cross-section,
//     on generated vectors nobody chose. The corpus leg cannot do this: a corpus
//     contains only documents someone thought to write.
//   * `candidates` proves the change does what the proposal SAYS — the fixtures
//     must be inexpressible before and expressible after. The first half is the
//     one that catches a proposal answering a demand the vocabulary already met,
//     which is the commonest way a proposal is wrong.
//   * `cost` is not a check at all. It is the stability classification and the
//     obligation set, reported so the reader sees the bill.
//
// None of them says the change is a good idea. A green spike removes the
// question "does this work"; every remaining question is a judgement.
// ---------------------------------------------------------------------------

/// One generated leg run outside this process — a TypeScript module under a JS
/// runtime, say. Optional everywhere: the spike degrades to its in-process legs
/// and SAYS SO, rather than reporting a pass it did not obtain.
type ExternalLeg =
    {
        Name: string
        /// `moduleSource` → `vectorsLiteral` → one wire string per vector, in order.
        Run: string -> string -> Result<string list, string>
    }

/// The verdict of one leg.
type SpikeLeg =
    {
        Name: string
        Passed: bool
        /// One line a reader can act on. Populated on pass as well as failure —
        /// "12 fixtures, all byte-identical" is what stops a vacuous leg (0
        /// fixtures, trivially green) from reading like a real one.
        Detail: string
    }

type SpikeInput =
    {
        /// The vocabulary as it stands.
        Base: Idl
        Proposal: Proposal
        /// Recorded wire documents, `name` → text. Typically a conformance corpus.
        Corpus: (string * string) list
        /// Seed for the generative leg. Pin it: the spike must be the same vectors
        /// on every machine, or a divergence is unreproducible.
        FuzzSeed: int
        FuzzVectors: int
        External: ExternalLeg list
    }

type SpikeReport =
    {
        ProposalId: string
        /// Document-level defects from `Proposal.validate`. A non-empty list does
        /// NOT stop the spike — a reader is better served by "the argument is
        /// incomplete AND the delta does not round-trip" than by the first of the
        /// two.
        Defects: string list
        Legs: SpikeLeg list
        /// The stability + obligation report over the two artifact revisions.
        CostReport: string
        /// Every leg passed. Deliberately not called `Verdict`.
        Green: bool
    }

[<RequireQualifiedAccess>]
module ProposalSpike =

    let private leg name passed detail =
        { Name = name
          Passed = passed
          Detail = detail }

    /// Decode then re-encode, reporting byte-identity. `None` ⇒ the document is
    /// outside this vocabulary's domain (it did not decode).
    let private roundTrip (idl: Idl) (wire: string) : Result<string, string> =
        Decode.decode idl wire |> Result.bind (Encode.encode idl)

    let private corpusLeg (baseIdl: Idl) (post: Idl) (corpus: (string * string) list) : SpikeLeg =
        if List.isEmpty corpus then
            // A leg with no inputs is not a pass. Say so, loudly, rather than
            // letting an empty corpus read as a clean bill.
            leg "corpus" false "no corpus documents supplied — the additive claim was not checked"
        else
            let mutable accepted = 0
            let mutable outOfDomain = 0

            let regressions =
                [ for name, wire in corpus do
                      match roundTrip baseIdl wire with
                      | Error _ ->
                          // The BASE vocabulary cannot read it, so the post
                          // vocabulary is under no obligation to. Counted, never
                          // silently dropped.
                          outOfDomain <- outOfDomain + 1
                      | Ok before ->
                          accepted <- accepted + 1

                          match roundTrip post wire with
                          | Error e -> yield name, "post-delta decode failed: " + e
                          | Ok after when after <> before -> yield name, "post-delta re-encode differs from base"
                          | Ok _ -> () ]

            if List.isEmpty regressions then
                leg
                    "corpus"
                    true
                    (sprintf
                        "%d document(s) accepted by the base vocabulary re-encode identically after the delta (%d \
                         outside the interpreter's domain, unchanged by it)"
                        accepted
                        outOfDomain)
            else
                leg
                    "corpus"
                    false
                    (sprintf
                        "%d regression(s) — the delta is NOT additive: %s"
                        (List.length regressions)
                        (regressions
                         |> List.truncate 5
                         |> List.map (fun (n, m) -> n + " (" + m + ")")
                         |> String.concat "; "))

    let private generateLeg (post: Idl) (tags: string list) : SpikeLeg * string option =
        match Gen.fsharpModuleWith Gen.GenSupport.Empty "Spike.Generated" post tags with
        | Error e -> leg "generate" false (sprintf "the F# structural layer does not generate: %A" e), None
        | Ok fsharp ->
            let schema = Gen.jsonSchema post
            let ts = Gen.typescriptModule post tags

            match Json.parse schema with
            | Error e -> leg "generate" false ("the generated JSON schema is not parseable JSON: " + e), None
            | Ok _ ->
                leg
                    "generate"
                    true
                    (sprintf
                        "F# %d chars, TypeScript %d chars, JSON schema %d chars — all three legs emit"
                        (String.length fsharp)
                        (String.length ts)
                        (String.length schema)),
                Some ts

    let private fuzzLeg
        (post: Idl)
        (tags: string list)
        (seed: int)
        (count: int)
        (tsModule: string option)
        (externals: ExternalLeg list)
        : SpikeLeg list =
        if count <= 0 || List.isEmpty tags then
            [ leg "fuzz" false "no vectors requested, or the delta touches no kind — the generative leg is vacuous" ]
        else
            // `Sample`, not `Gen`: the sampler travelled with the model at the 0.8.0
            // split (Phase 97) precisely because its output is a VALUE rather than a
            // language, and a consumer wanting vectors should not have to take a
            // source generator to get them.
            let vectors = Sample.sampleNodes post tags seed count

            let interpreter =
                vectors
                |> List.mapi (fun i v ->
                    match Encode.encode post v with
                    | Ok w -> Ok(i, v, w)
                    | Error m -> Error(sprintf "vector %d did not encode: %s" i m))

            match
                interpreter
                |> List.tryPick (function
                    | Error e -> Some e
                    | Ok _ -> None)
            with
            | Some e -> [ leg "fuzz" false e ]
            | None ->
                let encoded =
                    interpreter
                    |> List.map (function
                        | Ok x -> x
                        | Error _ -> failwith "unreachable")

                let selfDivergences =
                    [ for i, _, w in encoded do
                          match roundTrip post w with
                          | Error m -> yield sprintf "vector %d: decode failed (%s)" i m
                          | Ok again when again <> w -> yield sprintf "vector %d: re-encode differs" i
                          | Ok _ -> () ]

                let selfLeg =
                    if List.isEmpty selfDivergences then
                        leg
                            "fuzz"
                            true
                            (sprintf
                                "%d generated vector(s) over %d kind(s) at seed %d round-trip byte-identically"
                                (List.length encoded)
                                (List.length tags)
                                seed)
                    else
                        leg
                            "fuzz"
                            false
                            (sprintf
                                "%d divergence(s): %s"
                                (List.length selfDivergences)
                                (selfDivergences |> List.truncate 5 |> String.concat "; "))

                let externalLegs =
                    match tsModule with
                    | None -> []
                    | Some source ->
                        [ for ext in externals do
                              let literal =
                                  encoded
                                  |> List.map (fun (i, v, _) -> sprintf "  [%d, %s]," i (Gen.typescriptValue v))
                                  |> String.concat "\n"

                              match ext.Run source literal with
                              | Error m -> yield leg ("fuzz:" + ext.Name) false ("the external leg did not run: " + m)
                              | Ok produced ->
                                  let expected = encoded |> List.map (fun (_, _, w) -> w)

                                  if List.length produced <> List.length expected then
                                      yield
                                          leg
                                              ("fuzz:" + ext.Name)
                                              false
                                              (sprintf
                                                  "the external leg returned %d wire(s) for %d vector(s)"
                                                  (List.length produced)
                                                  (List.length expected))
                                  else
                                      let diffs =
                                          List.zip expected produced
                                          |> List.mapi (fun i (e, p) -> i, e, p)
                                          |> List.filter (fun (_, e, p) -> e <> p)

                                      if List.isEmpty diffs then
                                          yield
                                              leg
                                                  ("fuzz:" + ext.Name)
                                                  true
                                                  (sprintf
                                                      "%d vector(s) agree with the interpreter"
                                                      (List.length expected))
                                      else
                                          yield
                                              leg
                                                  ("fuzz:" + ext.Name)
                                                  false
                                                  (sprintf
                                                      "%d vector(s) diverge, first at index %d"
                                                      (List.length diffs)
                                                      (diffs |> List.head |> (fun (i, _, _) -> i))) ]

                selfLeg :: externalLegs

    let private candidateLeg (baseIdl: Idl) (post: Idl) (fixtures: ProposalFixture list) : SpikeLeg =
        if List.isEmpty fixtures then
            leg "candidates" false "no candidate fixtures — the change's own claim was not checked"
        else
            // **"Expressible" means SURVIVES the round trip, not "decodes".** A
            // decoder that tolerates unknown keys — which this family's does, by
            // an explicit strictness decision — accepts a document carrying a
            // field the vocabulary has never heard of and then drops it on
            // re-encode. Testing decode alone would therefore read every
            // new-field candidate as "already expressible" and refuse every such
            // proposal, which is the precise opposite of the truth: the field
            // was accepted and silently lost.
            let expressible (idl: Idl) (f: ProposalFixture) =
                match roundTrip idl f.Wire with
                | Ok again -> again = f.Wire
                | Error _ -> false

            let findings =
                [ for f in fixtures do
                      if expressible baseIdl f then
                          yield
                              f.Name,
                              "already expressible BEFORE the delta — this candidate is evidence against the proposal, \
                               not for it"
                      else
                          match roundTrip post f.Wire with
                          | Error e -> yield f.Name, "still not expressible after the delta: " + e
                          | Ok again when again <> f.Wire ->
                              // `f.Wire` is already canonical (the reader renders
                              // the authored JSON through `Canon`), so a mismatch
                              // is a real round-trip loss and never a formatting
                              // difference.
                              yield f.Name, "expressible after the delta but does not re-encode to the authored bytes"
                          | Ok _ -> () ]

            if List.isEmpty findings then
                leg
                    "candidates"
                    true
                    (sprintf
                        "%d candidate(s): each is inexpressible before the delta and round-trips after it"
                        (List.length fixtures))
            else
                leg "candidates" false (findings |> List.map (fun (n, m) -> n + " — " + m) |> String.concat "; ")

    /// Run the spike. The vocabulary is never written; the report is the whole
    /// output.
    let run (input: SpikeInput) : Result<SpikeReport, string> =
        let p = input.Proposal
        let defects = Proposal.validate p

        match Proposal.applyDelta input.Base p.Delta with
        | Error e ->
            Ok
                { ProposalId = p.Id
                  Defects = defects
                  Legs = [ leg "apply" false e ]
                  CostReport = "(not computed — the delta did not apply)"
                  Green = false }
        | Ok post ->
            let tags = Proposal.touchedKinds post p.Delta
            let generated, tsModule = generateLeg post tags

            let legs =
                [ leg "apply" true (sprintf "%d delta op(s) applied to an in-memory copy" (List.length p.Delta))
                  generated
                  corpusLeg input.Base post input.Corpus
                  yield! fuzzLeg post tags input.FuzzSeed input.FuzzVectors tsModule input.External
                  candidateLeg input.Base post p.Fixtures ]

            let cost =
                match Diff.run None (Artifact.render input.Base) (Artifact.render post) with
                | Ok text -> text
                | Error e -> "(cost report unavailable: " + e + ")"

            Ok
                { ProposalId = p.Id
                  Defects = defects
                  Legs = legs
                  CostReport = cost
                  Green = legs |> List.forall (fun l -> l.Passed) }

    /// Render a report for a human reader.
    ///
    /// Defects are printed FIRST and green is printed last, because the ordering
    /// is the message: an incomplete argument is not redeemed by a clean spike,
    /// and a reader who sees "GREEN" at the top reads the rest as confirmation.
    let render (r: SpikeReport) : string =
        let sb = System.Text.StringBuilder()
        let line (s: string) = sb.AppendLine s |> ignore

        line ("# Spike report — " + r.ProposalId)
        line ""

        if not (List.isEmpty r.Defects) then
            line "## Document defects"
            line ""

            line
                "The proposal is incomplete as an argument. The legs below still ran; a clean run does not close these."

            line ""

            for d in r.Defects do
                line ("- " + d)

            line ""

        line "## Legs"
        line ""

        for l in r.Legs do
            line (sprintf "- **%s** — %s. %s" l.Name (if l.Passed then "pass" else "FAIL") l.Detail)

        line ""
        line "## Cost"
        line ""
        line r.CostReport
        line ""

        line (
            if r.Green then
                "**Every leg passed.** This is the removal of one objection, not a recommendation: \
                 the semantic, accessibility and confusion costs are unmeasured here and are the ones \
                 that decide."
            else
                "**At least one leg failed.** See above."
        )

        sb.ToString()
