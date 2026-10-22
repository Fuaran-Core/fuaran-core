module Fuaran.Core.Tests.SigEntryTests

// Phase 487 — `SigEntry.ofHoleDecl` is the ONE reading of a declared hole as a signature entry, and
// `Function.signature` is a map over it. The law is stated here, in Core's own suite, rather than as a
// conformance family at a domain's witness: the mapping is Core's code, so no domain witness could
// make the two disagree, while a change to Core's `signature` can.

open System
open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

let private hole addr kind : HoleDecl =
    { Addr = addr
      Name = "n-" + addr
      Kind = kind }

/// A witness over the reference tree whose holes are exactly `holes`, so `signature` reads them as given.
let private witnessOf (holes: HoleDecl list) : ArtifactWitness<RNode, string> = { artw with Holes = fun _ -> holes }

/// Every scalar space shape, count spaces and non-count spaces alike, drawn from `rng`.
let private drawSpace (rng: Random) : ValueSpace =
    match rng.Next 9 with
    | 0 -> IntRange(0, rng.Next(0, 50)) // a count space
    | 1 -> IntRange(rng.Next(-5, 5), rng.Next(-5, 5)) // possibly negative or inverted
    | 2 -> IntRange(0, Space.maxRepeatCount + rng.Next(0, 2)) // the cap, or one past it
    | 3 -> FloatRange(0.0, float (rng.Next(1, 10)))
    | 4 -> StringLen(0, rng.Next(0, 20))
    | 5 -> Enum [ "a"; "b" ]
    | 6 -> Enum []
    | 7 -> AnyString
    | _ -> SlotTree(if rng.Next 2 = 0 then None else Some "Text")

let private drawKind (rng: Random) : HoleKind =
    match rng.Next 4 with
    | 0 -> ValueHole(drawSpace rng)
    | 1 -> SlotHole(if rng.Next 2 = 0 then None else Some "Card")
    | 2 -> RepeatHole(drawSpace rng)
    | _ -> ActionHole Effect.pureDeterministic

let private drawHoles (rng: Random) : HoleDecl list =
    List.init (rng.Next(0, 8)) (fun i -> hole (sprintf "a/h%d" i) (drawKind rng))

[<Tests>]
let tests =
    testList
        "SigEntry.ofHoleDecl"
        [ testCase "a value hole: address and name as declared, its space, required"
          <| fun _ ->
              let e = SigEntry.ofHoleDecl (hole "a/v" (ValueHole(StringLen(0, 5))))
              Expect.equal (e.Addr, e.Name) ("a/v", "n-a/v") "address and label"
              Expect.equal e.Space (Some(StringLen(0, 5))) "the hole's space"
              Expect.isTrue e.Required "a value hole is required"

          testCase "a slot hole: the SlotTree of its constraint, required"
          <| fun _ ->
              let e = SigEntry.ofHoleDecl (hole "a/s" (SlotHole(Some "Card")))
              Expect.equal e.Space (Some(SlotTree(Some "Card"))) "slot space"
              Expect.isTrue e.Required "a slot hole is required"

          testCase "a repeat hole is required exactly when its count space is a count space"
          <| fun _ ->
              let req s =
                  (SigEntry.ofHoleDecl (hole "a/r" (RepeatHole s))).Required

              Expect.isTrue (req (IntRange(0, 10))) "a bounded count repeat is required"
              Expect.isTrue (req (IntRange(0, Space.maxRepeatCount))) "the cap itself is a count space"
              Expect.isFalse (req (IntRange(0, Space.maxRepeatCount + 1))) "one past the cap is not"
              Expect.isFalse (req (IntRange(-1, 3))) "a negative count is not"
              Expect.isFalse (req AnyString) "an unbounded repeat is not required"
              Expect.isFalse (req (FloatRange(0.0, 3.0))) "a float range is no count space"

          testCase "an action hole: no space, not required on the data axis"
          <| fun _ ->
              let e = SigEntry.ofHoleDecl (hole "a/x" (ActionHole Effect.pureDeterministic))
              Expect.equal e.Space None "no data space"
              Expect.isFalse e.Required "bound by bindHandlers, never by a data argument"

          testCase "the entry's Required agrees with Space.isCount over every drawn repeat"
          <| fun _ ->
              let rng = Random 487

              for _ in 1..500 do
                  let s = drawSpace rng
                  let e = SigEntry.ofHoleDecl (hole "a/r" (RepeatHole s))
                  Expect.equal e.Required (Space.isCount s) (sprintf "repeat over %A" s)

          testCase "signature is a map of ofHoleDecl over the witness's holes, for every drawn hole list"
          <| fun _ ->
              let rng = Random 4870
              let node = template ()

              for _ in 1..500 do
                  let holes = drawHoles rng
                  let sg = Function.signature (witnessOf holes) "drawn" node

                  Expect.equal
                      sg.Holes
                      (holes |> List.map SigEntry.ofHoleDecl)
                      (sprintf "signature disagrees with ofHoleDecl over %A" holes)

          testCase "signature is a map of ofHoleDecl over the reference fixtures' own holes"
          <| fun _ ->
              for node in [ template (); sample () ] do
                  Expect.equal
                      (Function.signature artw "ref" node).Holes
                      (artw.Holes node |> List.map SigEntry.ofHoleDecl)
                      "the fixture's signature is the per-hole mapping" ]
