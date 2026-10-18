module Fuaran.Core.Tests.ProofOracle.TwinTests

// Phase 309 - twin evaluation across every extracted model; see the roster below.

open Expecto

// Phase 309 — TWIN EVALUATION. Every extracted module ends with a `twins` list: fixtures that apply
// the model's own functions to a concrete input and compare the result with the value the model
// means there, asserted `true` by F*'s NORMALISER (`assert_norm (twins_hold twins == true)` in the
// model). The list is extracted with the model, so what runs here is the extracted F# evaluating
// the very closures the normaliser evaluated: a `false` is the F# backend computing something other
// than the model on that input, which no byte diff of the extraction can see.
//
// Phase 504 — the roster is FOUND, not written: every type of the oracle assembly that declares a
// `twins` value of the shared `Twin.twin` record is a module whose twins run here. There is no list
// to forget a model in; `Proofs.Ladder` holds what is found to `proofs/modules.json`'s extracted set
// in both directions, and the kit's TWIN step holds each module's source to declaring the list.
let twinRoster: (string * (string * (unit -> bool)) list) list =
    let oracle = typeof<Twin.twin>.Assembly

    [ for t in oracle.GetTypes() do
          match
              t.GetProperty("twins", System.Reflection.BindingFlags.Static ||| System.Reflection.BindingFlags.Public)
          with
          | null -> ()
          | p when p.PropertyType = typeof<Twin.twin list> ->
              let ts = p.GetValue null :?> Twin.twin list
              yield t.Name, ts |> List.map (fun tw -> tw.tname, tw.tholds)
          | _ -> () ]
    |> List.sortBy fst

/// Every twin that does NOT hold on the extracted F#, as "<model>/<twin>" — empty is clean.
let twinFailures (roster: (string * (string * (unit -> bool)) list) list) : string list =
    [ for model, ts in roster do
          for name, holds in ts do
              if not (holds ()) then
                  yield model + "/" + name ]

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- Phase 309: twin evaluation — the extracted F# against the normaliser ----

          testCase "twin evaluation: every extracted model's normalised fixtures hold on the extracted F#"
          <| fun _ ->
              // The extractor premise, discharged on the sampled inputs. Each closure here is one
              // the prover evaluated to `true` under the model's semantics; the extracted F# must
              // agree on every one of them.
              Expect.isNonEmpty twinRoster "the roster names the extracted models"

              for model, ts in twinRoster do
                  Expect.isNonEmpty ts (sprintf "%s declares at least one twin" model)

              match twinFailures twinRoster with
              | [] -> ()
              | fs ->
                  failtestf
                      "the extracted F# disagrees with F*'s normaliser on %d fixture(s): %s — the extraction COMPILES and computes something else"
                      (List.length fs)
                      (String.concat ", " fs)

          testCase "twin evaluation can lose: a drain at the HEAD of the frontier misses the DagFold fixture"
          <| fun _ ->
              // The teeth, on the fixture itself. `pick_head` is as legitimate a selector as
              // `pick_min` by everything the model asks of one (`picks_from_frontier`), and it is the
              // natural mis-extraction of a fold the backend got backwards — so the twin's input,
              // drained that way, must give a different order from the one the normaliser certified.
              let ns = DagFold.twin_dag.nodes

              let certified = DagFold.drain_order DagFold.twin_lt DagFold.twin_fuel ns

              let perturbed =
                  DagFold.kahn DagFold.pick_head DagFold.twin_fuel ns (DagFold.ids_of ns) []

              Expect.equal certified [ "a"; "b"; "c"; "m" ] "the certified drain is the twin's expected order"
              Expect.notEqual perturbed certified "a selector taking the frontier's head must miss the fixture" ]
