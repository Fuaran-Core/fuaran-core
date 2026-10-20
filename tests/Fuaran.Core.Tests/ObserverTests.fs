module Fuaran.Core.Tests.ObserverTests

open Expecto
open Fuaran.Core

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

// Until `0.37.0` these drove the `InMemoryObserver` adapter; it left with the
// `Fuaran.Core.Observer` namespace (Phase 386), so each now drives the witness
// functions the adapter wrapped. Subscription is host state: an emission is the
// value a function returns, so "what a subscriber hears" is that value.

let private box w h c =
    { Width = w
      Height = h
      ContentWidth = c }

/// Register `id` with no parent, keeping the state.
let private reg w id input st =
    fst (ObserverWitness.register w id input None st)

[<Tests>]
let tests =
    testList
        "Fuaran.Core.Observer"
        [ test "register + snapshot derives the domain flags" {
              let w = ObserverWitness.create deriveBox
              let st = reg w "a" (box 0.0 10.0 5.0) ObserverWitness.empty

              match ObserverWitness.snapshot st "a" with
              | Some o ->
                  Expect.equal o.NodeId "a" "node id round-trips"
                  Expect.equal o.Flags [ ZeroWidth; OverflowX ] "derives zero-width + overflow"
              | None -> failtest "expected an observation for a registered node"
          }

          test "snapshot of an unknown node is None" {
              Expect.isNone
                  (ObserverWitness.snapshot ObserverWitness.empty<BoxInput, BoxFlag> "missing")
                  "unknown node → None"
          }

          test "derivation is pure — identical input yields identical flags" {
              let input = box 0.0 0.0 1.0
              Expect.equal (deriveBox input) (deriveBox input) "pure: repeated calls agree"

              // Two independent states agree from the same input — the determinism contract
              // behind an automatable gate.
              let w = ObserverWitness.create deriveBox
              let a = reg w "n" input ObserverWitness.empty
              let b = reg w "n" input ObserverWitness.empty

              Expect.equal
                  (ObserverWitness.snapshot a "n").Value.Flags
                  (ObserverWitness.snapshot b "n").Value.Flags
                  "two states agree from identical input"
          }

          test "EmitOnFlagChangeOnly: initial always emits, updates emit only on flag-set change" {
              let w = ObserverWitness.create deriveBox

              // Initial registration always emits (initial-emission rule).
              let st, first =
                  ObserverWitness.register w "a" (box 10.0 10.0 5.0) None ObserverWitness.empty

              Expect.equal first.Flags [] "initial registration emits"

              // Update with the SAME derived flag set → no emit.
              let st, same = ObserverWitness.update w "a" (box 20.0 20.0 5.0) st
              Expect.isNone same "no flag-set change → no emit"

              // Update that flips a flag on → emit. ContentWidth ≤ Width so only ZeroWidth fires
              // (no incidental overflow flag).
              let _, flipped = ObserverWitness.update w "a" (box 0.0 20.0 0.0) st

              match flipped with
              | Some o -> Expect.equal o.Flags [ ZeroWidth ] "emitted the new flag set"
              | None -> failtest "a flag-set change must emit"
          }

          test "EmitOnFlagChangeOnly = false emits on every update" {
              let w =
                  ObserverWitness.createWith
                      deriveBox
                      { ObserverOptions.defaults with
                          EmitOnFlagChangeOnly = false }

              let input = box 10.0 10.0 5.0
              let st, _ = ObserverWitness.register w "a" input None ObserverWitness.empty
              let st, u1 = ObserverWitness.update w "a" input st
              let _, u2 = ObserverWitness.update w "a" input st

              Expect.isSome u1 "the first unchanged update emits when change-gating is off"
              Expect.isSome u2 "and so does the second"
          }

          test "update on an unregistered node is a no-op" {
              let w = ObserverWitness.create deriveBox
              let st, o = ObserverWitness.update w "ghost" (box 0.0 0.0 0.0) ObserverWitness.empty
              Expect.isNone o "no emission for an unknown node"
              Expect.isNone (ObserverWitness.snapshot st "ghost") "and nothing is registered"
          }

          test "observeTree walks the parent-pointer graph, root-inclusive, deterministically" {
              let w = ObserverWitness.create deriveBox
              let i = box 10.0 10.0 5.0

              let st =
                  ObserverWitness.empty
                  |> reg w "root" i
                  |> fun st -> fst (ObserverWitness.register w "child1" i (Some "root") st)
                  |> fun st -> fst (ObserverWitness.register w "child2" i (Some "root") st)
                  |> fun st -> fst (ObserverWitness.register w "grandchild" i (Some "child1") st)

              let ids = ObserverWitness.observeTree st "root" |> List.map _.NodeId
              Expect.equal ids [ "root"; "child1"; "child2"; "grandchild" ] "BFS, level-then-order, root first"
          }

          test "observeTree of an unknown root is empty" {
              Expect.isEmpty
                  (ObserverWitness.observeTree ObserverWitness.empty<BoxInput, BoxFlag> "nope")
                  "unknown root → empty"
          }

          test "unregister removes the node and is idempotent" {
              let w = ObserverWitness.create deriveBox
              let st = reg w "a" (box 10.0 10.0 5.0) ObserverWitness.empty
              let gone = ObserverWitness.unregister "a" st
              Expect.isNone (ObserverWitness.snapshot gone "a") "node gone after unregister"
              Expect.equal (ObserverWitness.unregister "a" gone) gone "idempotent — a second unregister changes nothing"
          }

          test "a second, unrelated domain pack drives the same engine (verification-pack genericity)" {
              // Different `'Input` + `'Flag` entirely — same Core engine.
              let w = ObserverWitness.create deriveDrift

              let st =
                  ObserverWitness.empty
                  |> reg w "cell!A1" { Cached = 1.0; Recomputed = 1.0 }
                  |> reg w "cell!A2" { Cached = 1.0; Recomputed = 4.0 }

              Expect.equal (ObserverWitness.snapshot st "cell!A1").Value.Flags [] "no drift when cached == recomputed"

              Expect.equal
                  (ObserverWitness.snapshot st "cell!A2").Value.Flags
                  [ RecomputeDrift 3.0 ]
                  "drift flagged with delta"
          } ]

// ---- Phase 523 — the state is opaque; its order is read, never set ----
//
// Until `0.39.0` both fields of `ObserverState` were public, so a caller could hand in a state whose
// `Order` disagreed with its `Entries`, and `tryObserveTree` named the breach (Phase 383). The
// representation is private now: the verbs are the only constructors, and these read what they built.

[<Tests>]
let readerTests =
    let w = ObserverWitness.create deriveBox
    let i = box 10.0 10.0 5.0

    let under parent id st =
        fst (ObserverWitness.register w id i parent st)

    testList
        "ObserverWitness readers over the opaque state (Phase 523)"
        [ test "registered is registration order; a re-registration keeps its place" {
              let st =
                  ObserverWitness.empty
                  |> under None "root"
                  |> under (Some "root") "a"
                  |> under (Some "root") "b"
                  |> under (Some "b") "a"

              Expect.equal (ObserverWitness.registered st) [ "root"; "a"; "b" ] "a keeps its first place"
          }

          test "unregister drops the id from the order; registering it again joins the end" {
              let st =
                  ObserverWitness.empty
                  |> under None "root"
                  |> under (Some "root") "a"
                  |> under (Some "root") "b"
                  |> ObserverWitness.unregister "a"

              Expect.equal (ObserverWitness.registered st) [ "root"; "b" ] "a left the order"
              Expect.isFalse (ObserverWitness.isRegistered st "a") "and is not registered"

              let back = under (Some "root") "a" st
              Expect.equal (ObserverWitness.registered back) [ "root"; "b"; "a" ] "a re-registered id joins the end"
              Expect.isTrue (ObserverWitness.isRegistered back "a") "and is registered"

              Expect.equal
                  (ObserverWitness.observeTree back "root" |> List.map _.NodeId)
                  [ "root"; "b"; "a" ]
                  "the walk reads the same order"
          }

          test "isRegistered agrees with snapshot, and update never registers" {
              let st, _ = ObserverWitness.update w "ghost" i ObserverWitness.empty
              Expect.isFalse (ObserverWitness.isRegistered st "ghost") "an update of an unknown id registers nothing"
              Expect.isEmpty (ObserverWitness.registered st) "and orders nothing"

              let st = under None "n" st
              Expect.isTrue (ObserverWitness.isRegistered st "n") "registered"
              Expect.isSome (ObserverWitness.snapshot st "n") "and snapshot agrees"
          }

          test "the empty state holds nothing" {
              Expect.isEmpty (ObserverWitness.registered ObserverWitness.empty<BoxInput, BoxFlag>) "no ids"

              Expect.isFalse
                  (ObserverWitness.isRegistered ObserverWitness.empty<BoxInput, BoxFlag> "x")
                  "nothing registered"
          } ]
