module Fuaran.Core.Tests.PropagationContractTests

// Phase 209 — the propagation evaluator contract, ENFORCED. `Propagation.walk` hands `evalNode` a
// resolver that answers for `deps[id]` and for nothing else; a read outside that set ends the
// evaluation with `EvalUndeclaredRead` naming the node and the read. These are the unit cases the
// law family does not reach: what the refusal SAYS, the two neighbouring shapes it must NOT be
// confused with, and the one place it is not observable.
//
// ---- the go-red, and its honest boundary -------------------------------------------------------
//
// The phase's charter asked for each case to be "shown red against the open resolver", so
// `openWalk` below is the PRE-209 driver transcribed — the resolver over everything computed so
// far, no refusal — and the refusal cases are asserted to come out differently under it. Two of the
// four cases CANNOT be shown red that way, and saying so is more useful than manufacturing a
// go-red that proves nothing:
//
//   * a declared-but-absent read yields `None` under BOTH resolvers, by construction — that is
//     exactly what the restriction preserved, so a difference would be the defect;
//   * a cycle is reported as a cycle under BOTH, because `walk` never evaluates a cyclic node and
//     the resolver never sees one.
//
// So those two are NON-REGRESSION assertions — the two ways the new refusal could have been made
// to fire where it must not — and they are labelled as such rather than dressed up as teeth.

open Expecto
open Fuaran.Core

// ---------------------------------------------------------------------------------------------
//  The open resolver — `Propagation.walk` as it stood before Phase 209, transcribed for the
//  go-red. It resolves ANY id already computed and has no refusal, so an evaluator that reads an
//  undeclared node runs to completion here. Kept deliberately small: it exists to make the
//  difference visible, not to be a second driver.
// ---------------------------------------------------------------------------------------------
let private openEval
    (evalNode: (string -> 'v option) -> string -> Result<'v, string>)
    (deps: Map<string, Set<string>>)
    : Result<Propagation.EvalOutcome<'v>, Propagation.PropagationError> =
    let topo = Propagation.sort deps

    let rec go (results: Map<string, 'v>) =
        function
        | [] ->
            Ok
                { Propagation.EvalOutcome.Values = results
                  Propagation.EvalOutcome.Cyclic = topo.Cycles }
        | id :: rest ->
            match evalNode (fun k -> Map.tryFind k results) id with
            | Ok v -> go (Map.add id v results) rest
            | Error m -> Error(Propagation.EvalNodeFailed(id, m))

    go Map.empty topo.Order

[<Tests>]
let tests =
    testList
        "PropagationContract"
        [

          // ---- 1. the refusal names the node and the read ----

          testCase "a read outside the declared set is refused, naming the node and the read (Phase 209)"
          <| fun _ ->
              // `b` declares nothing and reads `a` anyway.
              let deps = Map.ofList [ "a", Set.empty; "b", Set.empty; "c", Set.singleton "b" ]

              let evalNode (resolve: string -> int option) id =
                  match id with
                  | "a" -> Ok 1
                  | "b" -> Ok(10 + (resolve "a" |> Option.defaultValue 0))
                  | _ -> Ok(100 + (resolve "b" |> Option.defaultValue 0))

              Expect.equal
                  (Propagation.eval evalNode deps)
                  (Error(Propagation.EvalUndeclaredRead("b", "a")))
                  "eval refuses, naming the reading node and the id it read"

              Expect.equal
                  (Propagation.evalFrom evalNode Map.empty (Set.singleton "a") deps)
                  (Error(Propagation.EvalUndeclaredRead("b", "a")))
                  "and evalFrom refuses identically where it recomputes the node"

              // THE GO-RED: under the open resolver the same evaluator runs to completion and `b`
              // silently carries a value computed from a read the graph does not know about.
              match openEval evalNode deps with
              | Ok outcome ->
                  Expect.equal
                      (Map.tryFind "b" outcome.Values)
                      (Some 11)
                      "the open resolver answered the undeclared read"
              | Error e -> failtestf "the open resolver was expected to succeed, not to refuse: %A" e

          testCase "the FIRST undeclared read is the one named, and the walk stops there (Phase 209)"
          <| fun _ ->
              // `b` reads two ids it never declared. The refusal names the first one asked for, and
              // nothing downstream of `b` is evaluated.
              let deps =
                  Map.ofList [ "a", Set.empty; "z", Set.empty; "b", Set.empty; "c", Set.singleton "b" ]

              let invoked = ResizeArray<string>()

              let evalNode (resolve: string -> int option) id =
                  invoked.Add id

                  match id with
                  | "b" ->
                      let first = resolve "z" |> Option.defaultValue 0
                      let second = resolve "a" |> Option.defaultValue 0
                      Ok(first + second)
                  | _ -> Ok 1

              Expect.equal
                  (Propagation.eval evalNode deps)
                  (Error(Propagation.EvalUndeclaredRead("b", "z")))
                  "the first undeclared read is named, not the last"

              Expect.isFalse (invoked.Contains "c") "and `c`, which reads `b`, was never evaluated"

          testCase "an evaluator's own failure at the violating node does not hide the violation (Phase 209)"
          <| fun _ ->
              // The evaluator reads undeclared AND then returns its own `Error`. The refusal names
              // the violation, which is the cause a domain can act on; the failure it computed from
              // a read that answered nothing is downstream of it.
              let deps = Map.ofList [ "a", Set.empty; "b", Set.empty ]

              let evalNode (resolve: string -> int option) id =
                  match id with
                  | "a" -> Ok 1
                  | _ ->
                      match resolve "a" with
                      | Some v -> Ok v
                      | None -> Error "b could not compute"

              Expect.equal
                  (Propagation.eval evalNode deps)
                  (Error(Propagation.EvalUndeclaredRead("b", "a")))
                  "the undeclared read is reported, not the failure it caused"

          // ---- 2. the two shapes the refusal must NOT be confused with ----
          //        (non-regression: identical under both resolvers by construction)

          testCase "a DECLARED read that resolves to nothing still yields None, not a refusal (Phase 209)"
          <| fun _ ->
              // `b` declares a read of `ghost`, which is not a node of the graph — a dangling
              // reference, the validator's concern and a `None` here. `None` means "declared, and
              // absent or failed upstream", which is what a domain propagates as data; the refusal
              // exists so that a contract violation is never reported in those terms.
              let deps = Map.ofList [ "a", Set.empty; "b", Set.singleton "ghost" ]

              // A `ref` and not a `let mutable`: the evaluator is a closure, and F# will not let one
              // capture a mutable local.
              let sawNone = ref false

              let evalNode (resolve: string -> int option) id =
                  match id with
                  | "a" -> Ok 1
                  | _ ->
                      match resolve "ghost" with
                      | None ->
                          sawNone.Value <- true
                          Ok 0
                      | Some v -> Ok v

              match Propagation.eval evalNode deps with
              | Ok outcome ->
                  Expect.isTrue sawNone.Value "the declared-but-absent read resolved to None"
                  Expect.equal (Map.tryFind "b" outcome.Values) (Some 0) "and the node evaluated"
              | Error e -> failtestf "a declared read must not be refused: %A" e

              // A declared read that is CYCLIC or not yet reached is the same shape: declared, so
              // answered, and `None` until there is a value.
              let ordered = Map.ofList [ "s", Set.empty; "t", Set.singleton "s" ]

              let counted (resolve: string -> int option) id =
                  Ok(
                      if id = "s" then
                          5
                      else
                          Option.defaultValue 0 (resolve "s") + 1
                  )

              match Propagation.eval counted ordered with
              | Ok outcome -> Expect.equal (Map.tryFind "t" outcome.Values) (Some 6) "a declared read in order resolves"
              | Error e -> failtestf "unexpected refusal: %A" e

          testCase "a cycle is still reported as a cycle, never as an undeclared read (Phase 209)"
          <| fun _ ->
              // `p` and `q` read each other — each read IS declared, so nothing here is a contract
              // violation; the group is data in `Cyclic` and neither node is evaluated. `d` reads
              // `p`, declared, and resolves it to `None` because a cyclic node has no value.
              let deps =
                  Map.ofList
                      [ "p", Set.singleton "q"
                        "q", Set.singleton "p"
                        "d", Set.singleton "p"
                        "t", Set.empty ]

              let evalNode (resolve: string -> int option) id =
                  match id with
                  | "d" -> Ok(Option.defaultValue -1 (resolve "p"))
                  | _ -> Ok 3

              match Propagation.eval evalNode deps with
              | Ok outcome ->
                  Expect.isTrue
                      (outcome.Cyclic |> List.exists (fun g -> Set.ofList g = Set.ofList [ "p"; "q" ]))
                      "the cyclic group is data"

                  Expect.equal (Map.tryFind "d" outcome.Values) (Some -1) "a declared read of a cyclic node is None"
                  Expect.isFalse (Map.containsKey "p" outcome.Values) "and no cyclic node was evaluated"
              | Error e -> failtestf "a cycle must not be reported as an undeclared read: %A" e

          // ---- 3. where the refusal is NOT observable, stated rather than assumed ----

          testCase
              "evalFrom does not see a violation at a node it reuses — and eval is why that prior cannot exist (Phase 209)"
          <| fun _ ->
              // `b` reads undeclared `a`. With `a` changed, the dirty set is `{a}` — `b` is not
              // downstream of a read it never declared — so `evalFrom` reuses `b` from `prior`
              // without invoking the evaluator there, and sees nothing. That is `evalfrom_minimal`
              // and not a hole in the enforcement: `eval` over the same map REFUSES, so a `prior`
              // of this shape cannot have come from the `eval` that `evalFrom`'s contract requires.
              let deps = Map.ofList [ "a", Set.empty; "b", Set.empty; "c", Set.empty ]

              let evalNode (resolve: string -> int option) id =
                  match id with
                  | "a" -> Ok 2
                  | "b" -> Ok(10 + (resolve "a" |> Option.defaultValue 0))
                  | _ -> Ok 7

              let handBuiltPrior = Map.ofList [ "a", 1; "b", 11; "c", 7 ]

              Expect.equal
                  (Propagation.evalFrom evalNode handBuiltPrior (Set.singleton "a") deps)
                  (Ok
                      { Propagation.EvalOutcome.Values = Map.ofList [ "a", 2; "b", 11; "c", 7 ]
                        Propagation.EvalOutcome.Cyclic = [] })
                  "a violating node that is clean AND present in prior is reused, so nothing invokes it"

              Expect.equal
                  (Propagation.eval evalNode deps)
                  (Error(Propagation.EvalUndeclaredRead("b", "a")))
                  "and the full driver refuses the same evaluator, so priming with eval finds it first"

              // The same node absent from `prior` IS recomputed, so the refusal is seen.
              Expect.equal
                  (Propagation.evalFrom evalNode (Map.remove "b" handBuiltPrior) (Set.singleton "a") deps)
                  (Error(Propagation.EvalUndeclaredRead("b", "a")))
                  "an id absent from prior is always recomputed, so the violation surfaces there too" ]
