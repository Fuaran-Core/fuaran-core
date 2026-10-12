module Fuaran.Core.Tests.ProofOracle.TwinTests

// Phase 309 - twin evaluation across every extracted model; see the roster below.

open System.IO
open Expecto

// Phase 428 — the unit algebra's model. Aliased for the same reason: production's `Unit` module
// (`Fuaran.Core.Unit`) is opened below, and the oracle's `Unit` would be shadowed by it.
module ModelUnit = Unit

// Phase 309 — TWIN EVALUATION. Every extracted model ends with a `twins` list: fixtures that apply
// the model's own functions to a concrete input and compare the result with the value the model
// means there, asserted `true` by F*'s NORMALISER (`assert_norm (twins_hold twins == true)` in the
// model). The list is extracted with the model, so what runs here is the extracted F# evaluating
// the very closures the normaliser evaluated: a `false` is the F# backend computing something other
// than the model on that input, which no byte diff of the extraction can see. Declared before
// `open Fuaran.Core` so that each model's top-level module name means the model and not a
// production module of the same name. `Proofs.Ladder` holds this roster to `check.ps1`'s extracted
// set, and the kit's TWIN step holds each model's source to declaring the list.
let twinRoster: (string * (string * (unit -> bool)) list) list =
    [ "DagFold", DagFold.twins |> List.map (fun t -> t.tname, t.tholds)
      "WireDecode", WireDecode.twins |> List.map (fun t -> t.tname, t.tholds)
      "DecodeLayer", DecodeLayer.twins |> List.map (fun t -> t.tname, t.tholds)
      "TreeOps", TreeOps.twins |> List.map (fun t -> t.tname, t.tholds)
      "Skeleton", Skeleton.twins |> List.map (fun t -> t.tname, t.tholds)
      "Chain", Chain.twins |> List.map (fun t -> t.tname, t.tholds)
      "JsonParse", JsonParse.twins |> List.map (fun t -> t.tname, t.tholds)
      "Preservation", Preservation.twins |> List.map (fun t -> t.tname, t.tholds)
      "TreeDiff", TreeDiff.twins |> List.map (fun t -> t.tname, t.tholds)
      "Limits", Limits.twins |> List.map (fun t -> t.tname, t.tholds)
      "WireCanon", WireCanon.twins |> List.map (fun t -> t.tname, t.tholds)
      "WireVersioning", WireVersioning.twins |> List.map (fun t -> t.tname, t.tholds)
      "WireColumn", WireColumn.twins |> List.map (fun t -> t.tname, t.tholds)
      "ColumnRefinement", ColumnRefinement.twins |> List.map (fun t -> t.tname, t.tholds)
      "Unit", ModelUnit.twins |> List.map (fun t -> t.tname, t.tholds)
      "Temporal", Temporal.twins |> List.map (fun t -> t.tname, t.tholds)
      "Capability", Capability.twins |> List.map (fun t -> t.tname, t.tholds)
      "Propagation", Propagation.twins |> List.map (fun t -> t.tname, t.tholds)
      "Query", Query.twins |> List.map (fun t -> t.tname, t.tholds)
      "Arbitrate", Arbitrate.twins |> List.map (fun t -> t.tname, t.tholds)
      "DecimalText", DecimalText.twins |> List.map (fun t -> t.tname, t.tholds)
      "Normalize", Normalize.twins |> List.map (fun t -> t.tname, t.tholds) ]

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
