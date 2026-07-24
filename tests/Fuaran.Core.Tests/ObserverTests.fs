module Fuaran.Core.Tests.ObserverTests

open Expecto
open Fuaran.Core.Observer

// ─── Reference verification packs (flag CONTENT is domain-side) ─────
//
// The framework is Core; every flag vocabulary + derivation below is a
// *domain* supplying its own verification pack — exactly as
// ValidatorTests supplies reference RuleFamilies. Two unrelated domains
// (a UI-layout-shaped box pack and a Calc-recompute-drift pack) drive
// the same generic engine, proving the seam owns no domain content.

// Domain pack #1 — a layout-shaped box (parallels Fuaran.UI.LayoutObserver).
type BoxInput =
    { Width: float
      Height: float
      ContentWidth: float }

type BoxFlag =
    | ZeroWidth
    | ZeroHeight
    | OverflowX

let private deriveBox (input: BoxInput) : BoxFlag list =
    [ if input.Width <= 0.5 then
          ZeroWidth
      if input.Height <= 0.5 then
          ZeroHeight
      if input.ContentWidth > input.Width then
          OverflowX ]

// Domain pack #2 — a Calc recompute-drift pack (a wholly different
// `'Input` + `'Flag`), to show the engine is generic over the domain.
type CellInput = { Cached: float; Recomputed: float }

type DriftFlag = RecomputeDrift of delta: float

let private deriveDrift (input: CellInput) : DriftFlag list =
    let delta = input.Recomputed - input.Cached

    if abs delta > 1e-9 then [ RecomputeDrift delta ] else []

[<Tests>]
let tests =
    testList
        "Fuaran.Core.Observer"
        [ test "register + observe derives the domain flags" {
              let obs = InMemoryObserver.create deriveBox

              obs.RegisterNode(
                  "a",
                  { Width = 0.0
                    Height = 10.0
                    ContentWidth = 5.0 }
              )

              match (obs :> IObserver<_, _>).Observe("a") with
              | Some o ->
                  Expect.equal o.NodeId "a" "node id round-trips"
                  Expect.equal o.Flags [ ZeroWidth; OverflowX ] "derives zero-width + overflow"
              | None -> failtest "expected an observation for a registered node"
          }

          test "observe of an unknown node is None" {
              let obs = InMemoryObserver.create deriveBox
              Expect.isNone ((obs :> IObserver<_, _>).Observe("missing")) "unknown node → None"
          }

          test "derivation is pure — identical input yields identical flags" {
              let input =
                  { Width = 0.0
                    Height = 0.0
                    ContentWidth = 1.0 }

              Expect.equal (deriveBox input) (deriveBox input) "pure: repeated calls agree"

              // Two independent engines agree from the same input — the
              // determinism contract behind an automatable gate.
              let a = InMemoryObserver.create deriveBox
              let b = InMemoryObserver.create deriveBox
              a.RegisterNode("n", input)
              b.RegisterNode("n", input)

              let fa = ((a :> IObserver<_, _>).Observe("n")).Value.Flags
              let fb = ((b :> IObserver<_, _>).Observe("n")).Value.Flags
              Expect.equal fa fb "two engines agree from identical input"
          }

          test "EmitOnFlagChangeOnly: initial always emits, updates emit only on flag-set change" {
              let obs = InMemoryObserver.create deriveBox
              let received = ResizeArray<string * BoxFlag list>()
              use _ = (obs :> IObserver<_, _>).Subscribe(fun (id, o) -> received.Add(id, o.Flags))

              // Initial registration always emits (initial-emission rule).
              obs.RegisterNode(
                  "a",
                  { Width = 10.0
                    Height = 10.0
                    ContentWidth = 5.0 }
              )

              Expect.equal received.Count 1 "initial registration emits"

              // Update with the SAME derived flag set → no emit.
              obs.Update(
                  "a",
                  { Width = 20.0
                    Height = 20.0
                    ContentWidth = 5.0 }
              )

              Expect.equal received.Count 1 "no flag-set change → no emit"

              // Update that flips a flag on → emit. ContentWidth ≤ Width
              // so only ZeroWidth fires (no incidental overflow flag).
              obs.Update(
                  "a",
                  { Width = 0.0
                    Height = 20.0
                    ContentWidth = 0.0 }
              )

              Expect.equal received.Count 2 "flag-set change → emit"
              Expect.equal (snd received[1]) [ ZeroWidth ] "emitted the new flag set"
          }

          test "EmitOnFlagChangeOnly = false emits on every update" {
              let opts =
                  { ObserverOptions.defaults with
                      EmitOnFlagChangeOnly = false }

              let obs = InMemoryObserver.createWith deriveBox opts
              let mutable count = 0
              use _ = (obs :> IObserver<_, _>).Subscribe(fun _ -> count <- count + 1)

              obs.RegisterNode(
                  "a",
                  { Width = 10.0
                    Height = 10.0
                    ContentWidth = 5.0 }
              )

              obs.Update(
                  "a",
                  { Width = 10.0
                    Height = 10.0
                    ContentWidth = 5.0 }
              )

              obs.Update(
                  "a",
                  { Width = 10.0
                    Height = 10.0
                    ContentWidth = 5.0 }
              )

              Expect.equal count 3 "every register + update emits when change-gating is off"
          }

          test "update on an unregistered node is a no-op" {
              let obs = InMemoryObserver.create deriveBox
              let mutable count = 0
              use _ = (obs :> IObserver<_, _>).Subscribe(fun _ -> count <- count + 1)

              obs.Update(
                  "ghost",
                  { Width = 0.0
                    Height = 0.0
                    ContentWidth = 0.0 }
              )

              Expect.equal count 0 "no emission for an unknown node"
          }

          test "ObserveTree walks the parent-pointer graph, root-inclusive, deterministically" {
              let obs = InMemoryObserver.create deriveBox

              let i =
                  { Width = 10.0
                    Height = 10.0
                    ContentWidth = 5.0 }

              obs.RegisterNode("root", i)
              obs.RegisterNode("child1", i, parent = "root")
              obs.RegisterNode("child2", i, parent = "root")
              obs.RegisterNode("grandchild", i, parent = "child1")

              let ids =
                  (obs :> IObserver<_, _>).ObserveTree("root") |> List.map (fun o -> o.NodeId)

              Expect.equal ids [ "root"; "child1"; "child2"; "grandchild" ] "BFS, level-then-order, root first"
          }

          test "ObserveTree of an unknown root is empty" {
              let obs = InMemoryObserver.create deriveBox
              Expect.isEmpty ((obs :> IObserver<_, _>).ObserveTree("nope")) "unknown root → empty"
          }

          test "Unregister removes the node and is idempotent" {
              let obs = InMemoryObserver.create deriveBox

              obs.RegisterNode(
                  "a",
                  { Width = 10.0
                    Height = 10.0
                    ContentWidth = 5.0 }
              )

              let io = obs :> IObserver<_, _>
              io.Unregister("a")
              Expect.isNone (io.Observe("a")) "node gone after unregister"
              io.Unregister("a") // idempotent — must not throw
          }

          test "Dispose of a subscription removes the handler" {
              let obs = InMemoryObserver.create deriveBox
              let mutable count = 0
              let sub = (obs :> IObserver<_, _>).Subscribe(fun _ -> count <- count + 1)

              obs.RegisterNode(
                  "a",
                  { Width = 0.0
                    Height = 0.0
                    ContentWidth = 0.0 }
              )

              Expect.equal count 1 "received the initial emission"
              sub.Dispose()

              obs.Update(
                  "a",
                  { Width = 10.0
                    Height = 10.0
                    ContentWidth = 99.0 }
              )

              Expect.equal count 1 "no more emissions after dispose"
          }

          test "a second, unrelated domain pack drives the same engine (verification-pack genericity)" {
              // Different `'Input` + `'Flag` entirely — same Core engine.
              let obs = InMemoryObserver.create deriveDrift
              obs.RegisterNode("cell!A1", { Cached = 1.0; Recomputed = 1.0 })
              obs.RegisterNode("cell!A2", { Cached = 1.0; Recomputed = 4.0 })

              let io = obs :> IObserver<_, _>
              Expect.equal (io.Observe("cell!A1")).Value.Flags [] "no drift when cached == recomputed"
              Expect.equal (io.Observe("cell!A2")).Value.Flags [ RecomputeDrift 3.0 ] "drift flagged with delta"
          } ]
