module Fuaran.Core.Tests.PropagationVectorExportTests

// The propagation law set's vectors held to the laws they export (Phase 538). The exporter is in the
// fixtures library (`PropagationVectorExport`); this is its family.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.PropagationVectorExport

[<Tests>]
let tests =
    testList
        "PropagationVectorExport"
        [ testCase "every exported case agrees with the laws it exports"
          <| fun _ ->
              let found = draws () |> List.mapi findings |> List.concat
              Expect.isEmpty found "the propagation vectors are the law's"

          testCase "the sample is the one the families draw, and both families are green over it"
          <| fun _ ->
              for r in
                  Conformance.propagationEvalLaws seed iterations
                  @ Conformance.dirtyPropagationLaws seed iterations do
                  Expect.isTrue r.Passed (sprintf "%s — %A" r.Law r.Counterexample)

          testCase "the sample reaches dirty and clean nodes, and every refusal class"
          <| fun _ ->
              let ds = draws ()

              let dirty, clean =
                  ds
                  |> List.collect (fun d ->
                      let dirty = Propagation.dirtyFromChangedIds d.Deps (Set.singleton d.ChangedId)
                      d.Ids |> List.map (fun n -> Set.contains n dirty))
                  |> List.partition id

              Expect.isNonEmpty dirty "a dirty node is reached"
              Expect.isNonEmpty clean "a clean node is reused"

              Expect.equal
                  ((Propagation.sort cyclic).Cycles |> List.map Set.ofList)
                  [ set [ "a"; "b"; "c" ] ]
                  "the fixed graph carries its cycle as data"

              let rendered = renderAt "0.0.0"

              for needle in
                  [ "\"evalUndeclaredRead\""
                    "\"evalUnknownChange\""
                    "\"" + absentRead + "\""
                    "\"driversDisagree\"" ] do
                  Expect.equal
                      (rendered.Contains needle)
                      (needle <> "\"driversDisagree\"")
                      (sprintf
                          "%s is %s the file"
                          needle
                          (if needle = "\"driversDisagree\"" then
                               "absent from"
                           else
                               "in"))

          testCase "the rendered file is LF-only and byte-stable across renders"
          <| fun _ ->
              let once = renderAt "0.0.0"
              Expect.equal once (renderAt "0.0.0") "two renders produce the same bytes"
              Expect.isFalse (once.Contains "\r") "no CR may reach a corpus byte-compared across three OSes" ]
