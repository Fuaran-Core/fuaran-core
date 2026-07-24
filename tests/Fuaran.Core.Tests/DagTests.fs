module Fuaran.Core.Tests.DagTests

// Phase 250 — content-addressed branching/merging op-DAG over the StreamWitness.

open Expecto
open Fuaran.Core

type private CounterOp =
    | Inc of int
    | Dec of int

let private sw: StreamWitness<CounterOp, int, string> =
    { Apply =
        fun op st ->
            match op with
            | Inc n -> Ok(st + n)
            | Dec n -> if st - n < 0 then Error "negative" else Ok(st - n)
      Encode =
        fun op ->
            match op with
            | Inc n -> Json.render (Json.kindObj "inc" [ "n", JInt n ])
            | Dec n -> Json.render (Json.kindObj "dec" [ "n", JInt n ])
      Decode =
        fun s ->
            Decode.parse s
            |> Result.bind (fun el ->
                Decode.kindOf el
                |> Result.bind (fun k ->
                    Decode.intField "n" el
                    |> Result.map (fun n -> if k = "dec" then Dec n else Inc n))) }

let private h = OpStream.defaultHash

[<Tests>]
let tests =
    testList
        "Dag"
        [ testCase "a linear chain verifies and replays like the linear spine"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1
              Expect.isTrue (Dag.verifyDag h sw d2) "intact DAG verifies"
              Expect.equal (Dag.replayTo sw 0 d2 b) (Ok 8) "replay to b = 5 + 3"
              Expect.equal (Dag.heads d2) [ b ] "single head"

          testCase "appending onto a non-head node forks a branch"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1
              let c, d3 = Dag.append h sw (Human "x") (Inc 4) a d2 // fork off a
              Expect.equal (Dag.replayTo sw 0 d3 b) (Ok 8) "branch b = 5 + 3"
              Expect.equal (Dag.replayTo sw 0 d3 c) (Ok 9) "branch c = 5 + 4"
              Expect.equal (Dag.heads d3 |> List.length) 2 "two heads after the fork"

          testCase "merge converges both branches; replay is deterministic and order-symmetric"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1
              let c, d3 = Dag.append h sw (Human "x") (Inc 4) a d2
              let m, d4 = Dag.merge h sw (Human "x") (Inc 0) b c d3
              // genesis(5) + branch b(3) + branch c(4) + merge(0) = 12
              Expect.equal (Dag.replayTo sw 0 d4 m) (Ok 12) "merge replays both branches once"
              Expect.equal (Dag.replayTo sw 0 d4 m) (Dag.replayTo sw 0 d4 m) "replay is deterministic"
              Expect.equal (Dag.heads d4) [ m ] "the merge is the sole head"

              // the symmetric merge (c,b) converges to the SAME content id (Phase 64.1 —
              // parents are sorted before hashing, so merge identity is order-independent;
              // two hosts reconciling the same pair mint the same node).
              let m2, d5 = Dag.merge h sw (Human "x") (Inc 0) c b d4
              Expect.equal m2 m "left/right order ⇒ the SAME content id (order-independent merge)"
              Expect.equal (Dag.replayTo sw 0 d5 m2) (Dag.replayTo sw 0 d4 m) "and the same converged state"

          testCase "identical histories converge to identical content ids"
          <| fun _ ->
              let a1, _ = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty
              let a2, _ = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty
              Expect.equal a1 a2 "content addressing is deterministic"

              // a different op or actor ⇒ a different id
              let a3, _ = Dag.append h sw (Human "x") (Inc 6) "" Dag.empty
              Expect.notEqual a1 a3 "different op ⇒ different id"

          testCase "verifyDag detects a tampered node"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty
              let _, d2 = Dag.append h sw (Human "x") (Inc 3) a d1

              let tampered =
                  { Dag.T.Nodes = d2.Nodes |> Map.map (fun _ n -> { n with Op = Inc 99 }) }

              Expect.isFalse (Dag.verifyDag h sw tampered) "a tampered op breaks the content hash"

          // ---- JSONL persistence (Phase 01) ----

          testCase "a branch+merge DAG round-trips through JSONL"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1
              let c, d3 = Dag.append h sw (Human "x") (Inc 4) a d2
              let m, d4 = Dag.merge h sw (Human "x") (Inc 0) b c d3

              match Dag.fromJsonl sw (Dag.toJsonl sw.Encode d4) with
              | Ok d4' ->
                  Expect.equal d4'.Nodes d4.Nodes "round-trip preserves every node"
                  Expect.isTrue (Dag.verifyDag h sw d4') "the decoded DAG re-verifies"
                  Expect.equal (Dag.replayTo sw 0 d4' m) (Dag.replayTo sw 0 d4 m) "decoded replays identically"
              | Error e -> failtestf "fromJsonl failed: %s" e

          testCase "an empty DAG round-trips"
          <| fun _ ->
              Expect.equal (Dag.toJsonl sw.Encode Dag.empty) "" "empty DAG ⇒ empty text"
              Expect.equal (Dag.fromJsonl sw "") (Ok Dag.empty) "empty text ⇒ empty DAG"

          testCase "toJsonl is stable (id-sorted) for a fixed DAG"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty
              let _, d2 = Dag.append h sw (Human "x") (Inc 3) a d1
              Expect.equal (Dag.toJsonl sw.Encode d2) (Dag.toJsonl sw.Encode d2) "stable output"

          testCase "a malformed line is a named Error, not an exception"
          <| fun _ ->
              match Dag.fromJsonl sw "{ not json" with
              | Error e -> Expect.stringContains e "line 0" "the Error names the offending line"
              | Ok _ -> failtest "expected a named Error"

          testCase "a dangling parent decodes structurally but fails verifyDag"
          <| fun _ ->
              let line =
                  "{\"node\":true,\"id\":\"orphan\",\"parents\":[\"missing\"],\"actor\":{\"kind\":\"human\",\"id\":\"x\"},\"op\":"
                  + sw.Encode(Inc 1)
                  + "}"

              match Dag.fromJsonl sw line with
              | Ok dag -> Expect.isFalse (Dag.verifyDag h sw dag) "a missing parent breaks verifyDag"
              | Error e -> failtestf "structural parse should succeed: %s" e

          // Phase 13 — verified load gates the structural decode on verifyDag.
          testCase "fromJsonlVerified accepts an intact DAG and rejects a dangling parent"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1
              Expect.equal (Dag.fromJsonlVerified h sw (Dag.toJsonl sw.Encode d2)) (Ok d2) "intact DAG verifies on load"

              // a genuinely dangling parent: serialize only b's line (b's id IS its real content
              // hash, so the missing-parent check trips — not content-id mismatch)
              let bLine =
                  (Dag.toJsonl sw.Encode d2).Split('\n') |> Array.find (fun l -> l.Contains b)

              match Dag.fromJsonlVerified h sw bLine with
              | Error e -> Expect.stringContains e "parent" "the load is gated on verifyDag (missing parent)"
              | Ok _ -> failtest "expected the dangling-parent DAG to be refused on load"

          testCase "fromJsonlVerified rejects a tampered node id"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty
              // rewrite the stored id so it no longer matches the content hash
              let tampered =
                  { Dag.T.Nodes =
                      d1.Nodes
                      |> Map.toList
                      |> List.map (fun (_, n) -> "forged", { n with Id = "forged" })
                      |> Map.ofList }

              Expect.isError
                  (Dag.fromJsonlVerified h sw (Dag.toJsonl sw.Encode tampered))
                  "a content-id mismatch is refused on load"

          // ---- merge-base / branch-delta (Phase 08) ----

          testCase "mergeBase finds the fork point of two branches"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1
              let c, d3 = Dag.append h sw (Human "x") (Inc 4) a d2 // fork off a
              Expect.equal (Dag.mergeBase d3 b c) (Some a) "the fork point a is the merge base"
              Expect.equal (Dag.ancestorsOf d3 a) (Set.ofList [ a ]) "a genesis closure is itself"

          testCase "mergeBase is None for disjoint histories"
          <| fun _ ->
              let g1, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty
              let g2, d2 = Dag.append h sw (Human "x") (Inc 7) "" d1 // a second, unrelated genesis
              Expect.equal (Dag.mergeBase d2 g1 g2) None "no common ancestor"

          testCase "between returns the branch delta in topological order"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1
              let c, d3 = Dag.append h sw (Human "x") (Inc 4) b d2 // linear a -> b -> c

              Expect.equal (Dag.between d3 a c |> List.map (fun n -> n.Id)) [ b; c ] "nodes after a, up to c"
              Expect.equal (Dag.between d3 c c) [] "base == head ⇒ empty delta"
              Expect.equal (Dag.between d3 b c |> List.map (fun n -> n.Id)) [ c ] "single-step delta"

          // Phase 26 — branch delta as an applyable op list.
          testCase "betweenOps projects the branch delta's ops in topological order"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1
              let c, d3 = Dag.append h sw (Human "x") (Inc 4) b d2

              Expect.equal (Dag.betweenOps d3 a c) [ Inc 3; Inc 4 ] "ops of the nodes between a and c"
              Expect.equal (Dag.betweenOps d3 c c) [] "base == head ⇒ no ops"
              // consistent with `between`
              Expect.equal (Dag.betweenOps d3 a c) (Dag.between d3 a c |> List.map (fun n -> n.Op)) "matches between"

          // Phase 21 — DAG break localisation.
          testCase "firstBreak is None for an intact DAG"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty
              let _, d2 = Dag.append h sw (Human "x") (Inc 3) a d1
              Expect.isNone (Dag.firstBreak h sw d2) "intact ⇒ no break"

          testCase "firstBreak localises a tampered node and a dangling parent"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty
              let _, d2 = Dag.append h sw (Human "x") (Inc 3) a d1

              // tamper a node's op without rewriting its id ⇒ content-id mismatch
              let tampered =
                  { Dag.T.Nodes = d2.Nodes |> Map.map (fun _ n -> { n with Op = Inc 999 }) }

              match Dag.firstBreak h sw tampered with
              | Some b -> Expect.stringContains b.Reason "content-id" "names a content-id mismatch"
              | None -> failtest "expected a break"

              // a real node whose parent is dropped from the map: its id still matches its content
              // hash, so the *missing-parent* check (not content-id) is what trips
              let ra, dra = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty
              let _, drab = Dag.append h sw (Human "x") (Inc 3) ra dra
              let orphaned = { Dag.T.Nodes = drab.Nodes |> Map.remove ra }

              match Dag.firstBreak h sw orphaned with
              | Some b ->
                  Expect.stringContains b.Reason "parent" "names a missing parent"
                  Expect.equal b.Got ra "reports the missing parent id"
              | None -> failtest "expected a break"

          // ---- conformance: dagLaws (Phase 07) ----

          testCase "dagLaws certify the reference stream witness green"
          <| fun _ ->
              let genOp (rng: ConfRng.T) =
                  let kind, r1 = ConfRng.intBelow 2 rng
                  let n, r2 = ConfRng.intBelow 5 r1
                  (if kind = 0 then Inc n else Dec n), r2

              let streamGen: StreamGen<CounterOp, int> = { State0 = 0; Op = genOp }
              let results = Conformance.dagLaws sw streamGen h 99 100
              Expect.equal (List.length results) 4 "verifyDag + determinism + tamper + JSONL round-trip"
              Expect.isTrue (results |> List.forall (fun r -> r.Passed)) "dag laws pass"

          // ---- Phase 42: DAG acyclicity guard ----

          testCase "tryReplayTo refuses a cyclic history instead of folding a partial prefix"
          <| fun _ ->
              // a hand-crafted (fromJsonl trusts stored ids) cyclic DAG: a→b→a
              let nodeA: DagNode<CounterOp> =
                  { Id = "a"
                    Parents = [ "b" ]
                    Actor = Human "x"
                    Op = Inc 1 }

              let nodeB: DagNode<CounterOp> =
                  { Id = "b"
                    Parents = [ "a" ]
                    Actor = Human "x"
                    Op = Inc 1 }

              let cyclic: Dag.T<CounterOp> = { Nodes = Map.ofList [ "a", nodeA; "b", nodeB ] }

              Expect.isFalse (Dag.isAcyclic cyclic "a") "the closure is cyclic"

              match Dag.tryTopoOrder cyclic "a" with
              | Error msg -> Expect.stringContains msg "cyclic" "names the cyclic history"
              | Ok _ -> failtest "expected a cyclic-history error"

              match Dag.tryReplayTo sw 0 cyclic "a" with
              | Error(Dag.CyclicHistory "a") -> ()
              | other -> failtestf "expected CyclicHistory, got %A" other

          testCase "tryReplayTo replays an acyclic DAG like replayTo"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1
              Expect.isTrue (Dag.isAcyclic d2 b) "a genuine DAG is acyclic"

              match Dag.tryReplayTo sw 0 d2 b with
              | Ok 8 -> ()
              | other -> failtestf "expected Ok 8, got %A" other ]
