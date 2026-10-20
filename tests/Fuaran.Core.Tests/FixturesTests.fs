module Fuaran.Core.Tests.FixturesTests

// Phase 532 — the fixtures library (`tests/Fuaran.Core.Tests.Fixtures`) is read by this suite and by the
// tool, and holds no test. Expecto discovers the `[<Tests>]` values of the assembly it runs, so a test
// declared in the library would compile, read as coverage, and never run. This family holds that.

open System.Reflection
open Expecto

/// The `[<Tests>]` values an assembly declares, as `Type.member`.
let private declaredTests (asm: Assembly) : string list =
    [ for t in asm.GetTypes() do
          for p in t.GetProperties(BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic) do
              if p.IsDefined(typeof<TestsAttribute>, false) then
                  yield t.FullName + "." + p.Name ]

[<Tests>]
let tests =
    testList
        "Fixtures"
        [ testCase "the fixtures library declares no [<Tests>] value, which the runner would never discover"
          <| fun _ ->
              Expect.isEmpty
                  (declaredTests typeof<Renders.Render>.Assembly)
                  "a test in the fixtures library never runs; declare it in the test project"

          testCase "go-red: the reading finds the tests this assembly declares"
          <| fun _ ->
              Expect.contains
                  (declaredTests (Assembly.GetExecutingAssembly()))
                  "Fuaran.Core.Tests.FixturesTests.tests"
                  "the reflection that finds no test in the library finds this one" ]
