module Fuaran.Core.Tests.EffectAuditTests

// Phase 248 — declared-vs-actual effect audit. The teeth on the mandatory effect
// signature: a descendant that leaks an effect the artifact under-declares is caught.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

let private clock = { Host = Pure; Determinism = Clock }

[<Tests>]
let tests =
    testList
        "Function.auditEffect"
        [ testCase "a sound artifact (all pure) passes"
          <| fun _ -> Expect.equal (Function.auditEffect artw (template ())) (Ok()) "pure template is sound"

          testCase "a declaration that covers the actual passes"
          <| fun _ ->
              let clockRoot =
                  { RNode.node
                        "r"
                        "doc"
                        [ { RNode.leaf "c" "para" "x" with
                              Eff = clock } ] with
                      Eff = clock }

              Expect.equal (Function.auditEffect artw clockRoot) (Ok()) "declared clock covers a clock child"

          testCase "an under-declared effect is caught, naming declared + actual"
          <| fun _ ->
              // a clock-reading inner wired under a pure-declared outer
              let inner =
                  { RNode.leaf "p" "para" "now" with
                      Eff = clock }

              match Function.compose artw "tpl/s" inner (template ()) with
              | Ok composed ->
                  match Function.auditEffect artw composed with
                  | Error(declared, actual) ->
                      Expect.equal declared.Determinism Deterministic "declared was deterministic"
                      Expect.equal actual.Determinism Clock "actual leaked clock"
                  | Ok() -> failtest "expected the clock leak to be caught"
              | Error e -> failtestf "compose failed: %A" e

          testCase "the audit's actual effect agrees with composedEffect (the join law)"
          <| fun _ ->
              let inner =
                  { RNode.leaf "p" "para" "now" with
                      Eff = clock }

              match Function.compose artw "tpl/s" inner (template ()) with
              | Ok composed ->
                  let ce = Function.composedEffect artw inner (template ())

                  match Function.auditEffect artw composed with
                  | Error(_, actual) ->
                      Expect.equal actual.Determinism ce.Determinism "audit determinism = composedEffect"
                      Expect.equal actual.Host ce.Host "audit host = composedEffect"
                  | Ok() -> failtest "expected a leak to be caught"
              | Error e -> failtestf "compose failed: %A" e

          testCase "a host-write leak under a pure declaration is caught"
          <| fun _ ->
              let writer =
                  { RNode.node
                        "r"
                        "doc"
                        [ { RNode.leaf "w" "para" "" with
                              Eff =
                                  { Host = WritesHost
                                    Determinism = Deterministic } } ] with
                      Eff = Effect.pureDeterministic }

              match Function.auditEffect artw writer with
              | Error(declared, actual) ->
                  Expect.equal declared.Host Pure "declared pure"
                  Expect.equal actual.Host WritesHost "actual writes the host"
              | Ok() -> failtest "expected the host-write leak to be caught" ]
