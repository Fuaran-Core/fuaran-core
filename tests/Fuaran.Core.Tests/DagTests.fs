module Fuaran.Core.Tests.DagTests

// Phase 250 — content-addressed branching/merging op-DAG over the StreamWitness.

open Expecto
open Fuaran.Core

// The counter stream domain — the one reference copy, `Reference.Counter` (Phase 296). The copy
// this suite carried decoded an op kind it did not know as `Inc`; the shared one refuses it.
open Fuaran.Core.Tests.Reference.Counter

let private sw = witness

let private h = OpStream.defaultHash

[<Tests>]
let tests =
    testList
        "Dag"
        [ testCase "a linear chain verifies and replays like the linear spine"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1 |> Reference.built
              Expect.isTrue (Dag.verifyDag h sw d2) "intact DAG verifies"
              Expect.equal (Dag.tryReplayTo sw 0 d2 b) (Ok 8) "replay to b = 5 + 3"
              Expect.equal (Dag.heads d2) [ b ] "single head"

          testCase "appending onto a non-head node forks a branch"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1 |> Reference.built
              let c, d3 = Dag.append h sw (Human "x") (Inc 4) a d2 |> Reference.built // fork off a
              Expect.equal (Dag.tryReplayTo sw 0 d3 b) (Ok 8) "branch b = 5 + 3"
              Expect.equal (Dag.tryReplayTo sw 0 d3 c) (Ok 9) "branch c = 5 + 4"
              Expect.equal (Dag.heads d3 |> List.length) 2 "two heads after the fork"

          testCase "merge converges both branches; replay is deterministic and order-symmetric"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1 |> Reference.built
              let c, d3 = Dag.append h sw (Human "x") (Inc 4) a d2 |> Reference.built
              let m, d4 = Dag.merge h sw (Human "x") (Inc 0) b c d3 |> Reference.built
              // genesis(5) + branch b(3) + branch c(4) + merge(0) = 12
              Expect.equal (Dag.tryReplayTo sw 0 d4 m) (Ok 12) "merge replays both branches once"
              Expect.equal (Dag.tryReplayTo sw 0 d4 m) (Dag.tryReplayTo sw 0 d4 m) "replay is deterministic"
              Expect.equal (Dag.heads d4) [ m ] "the merge is the sole head"

              // the symmetric merge (c,b) converges to the SAME content id (Phase 64.1 —
              // parents are sorted before hashing, so merge identity is order-independent;
              // two hosts reconciling the same pair mint the same node).
              let m2, d5 = Dag.merge h sw (Human "x") (Inc 0) c b d4 |> Reference.built
              Expect.equal m2 m "left/right order ⇒ the SAME content id (order-independent merge)"
              Expect.equal (Dag.tryReplayTo sw 0 d5 m2) (Dag.tryReplayTo sw 0 d4 m) "and the same converged state"

          testCase "identical histories converge to identical content ids"
          <| fun _ ->
              let a1, _ = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              let a2, _ = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              Expect.equal a1 a2 "content addressing is deterministic"

              // a different op or actor ⇒ a different id
              let a3, _ = Dag.append h sw (Human "x") (Inc 6) "" Dag.empty |> Reference.built
              Expect.notEqual a1 a3 "different op ⇒ different id"

          testCase "verifyDag detects a tampered node"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              let _, d2 = Dag.append h sw (Human "x") (Inc 3) a d1 |> Reference.built

              let tampered = DagOf.nodes (d2.Nodes |> Map.map (fun _ n -> { n with Op = Inc 99 }))

              Expect.isFalse (Dag.verifyDag h sw tampered) "a tampered op breaks the content hash"

          // ---- JSONL persistence (Phase 01) ----

          testCase "a branch+merge DAG round-trips through JSONL"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1 |> Reference.built
              let c, d3 = Dag.append h sw (Human "x") (Inc 4) a d2 |> Reference.built
              let m, d4 = Dag.merge h sw (Human "x") (Inc 0) b c d3 |> Reference.built

              match
                  Dag.fromJsonl sw (Dag.toJsonl sw.Encode d4)
                  |> Result.mapError Dag.loadFaultToString
              with
              | Ok d4' ->
                  Expect.equal d4'.Nodes d4.Nodes "round-trip preserves every node"
                  Expect.isTrue (Dag.verifyDag h sw d4') "the decoded DAG re-verifies"
                  Expect.equal (Dag.tryReplayTo sw 0 d4' m) (Dag.tryReplayTo sw 0 d4 m) "decoded replays identically"
              | Error e -> failtestf "fromJsonl failed: %s" e

          testCase "an empty DAG round-trips"
          <| fun _ ->
              Expect.equal (Dag.toJsonl sw.Encode Dag.empty) "" "empty DAG ⇒ empty text"
              Expect.equal (Dag.fromJsonl sw "") (Ok Dag.empty) "empty text ⇒ empty DAG"

          testCase "toJsonl is stable (id-sorted) for a fixed DAG"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              let _, d2 = Dag.append h sw (Human "x") (Inc 3) a d1 |> Reference.built
              Expect.equal (Dag.toJsonl sw.Encode d2) (Dag.toJsonl sw.Encode d2) "stable output"

          testCase "a malformed line is a named Error, not an exception"
          <| fun _ ->
              match Dag.fromJsonl sw "{ not json" |> Result.mapError Dag.loadFaultToString with
              | Error e -> Expect.stringContains e "line 1" "the Error names the offending line"
              | Ok _ -> failtest "expected a named Error"

          testCase "a dangling parent decodes structurally but fails verifyDag"
          <| fun _ ->
              let line =
                  "{\"node\":true,\"id\":\"orphan\",\"parents\":[\"missing\"],\"actor\":{\"kind\":\"human\",\"id\":\"x\"},\"op\":"
                  + sw.Encode(Inc 1)
                  + "}"

              match Dag.fromJsonl sw line |> Result.mapError Dag.loadFaultToString with
              | Ok dag -> Expect.isFalse (Dag.verifyDag h sw dag) "a missing parent breaks verifyDag"
              | Error e -> failtestf "structural parse should succeed: %s" e

          // Phase 260 — an unknown actor kind refuses instead of reading as Human.
          testCase "fromJsonl decodes both known actor kinds (Phase 260)"
          <| fun _ ->
              let node actor =
                  "{\"node\":true,\"id\":\"n\",\"parents\":[],\"actor\":"
                  + actor
                  + ",\"op\":"
                  + sw.Encode(Inc 1)
                  + "}"

              match
                  Dag.fromJsonl sw (node (Actor.encode (Human "ann")))
                  |> Result.mapError Dag.loadFaultToString
              with
              | Ok dag -> Expect.equal (Map.find "n" dag.Nodes).Actor (Human "ann") "a human actor decodes as Human"
              | Error e -> failtestf "a known kind must decode: %s" e

              match
                  Dag.fromJsonl sw (node (Actor.encode (Agent("m", "v", "bot"))))
                  |> Result.mapError Dag.loadFaultToString
              with
              | Ok dag ->
                  Expect.equal (Map.find "n" dag.Nodes).Actor (Agent("m", "v", "bot")) "an agent actor decodes as Agent"
              | Error e -> failtestf "a known kind must decode: %s" e

          testCase "fromJsonl refuses an unknown actor kind, naming the kind and the line (Phase 260)"
          <| fun _ ->
              let node actor =
                  "{\"node\":true,\"id\":\"n\",\"parents\":[],\"actor\":"
                  + actor
                  + ",\"op\":"
                  + sw.Encode(Inc 1)
                  + "}"

              match
                  Dag.fromJsonl sw (node "{\"kind\":\"service\",\"id\":\"svc-1\"}")
                  |> Result.mapError Dag.loadFaultToString
              with
              | Error e ->
                  Expect.stringContains e "line 1" "names the failing line"
                  Expect.stringContains e "unknown actor kind \"service\"" "names the kind it does not know"
              | Ok dag -> failtestf "an unknown kind must not decode, got %A" dag

              match
                  Dag.fromJsonl sw (node "{\"id\":\"ann\"}")
                  |> Result.mapError Dag.loadFaultToString
              with
              | Error e -> Expect.stringContains e "no kind" "a kind-less actor is refused too, not read as Human"
              | Ok dag -> failtestf "a kind-less actor must not decode, got %A" dag

              Expect.isError
                  (Dag.fromJsonlVerified h sw (node "{\"kind\":\"service\",\"id\":\"svc-1\"}"))
                  "the verified reader refuses"

          // Phase 13 — verified load gates the structural decode on verifyDag.
          testCase "fromJsonlVerified accepts an intact DAG and rejects a dangling parent"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1 |> Reference.built
              Expect.equal (Dag.fromJsonlVerified h sw (Dag.toJsonl sw.Encode d2)) (Ok d2) "intact DAG verifies on load"

              // a genuinely dangling parent: serialize only b's line (b's id IS its real content
              // hash, so the missing-parent check trips — not content-id mismatch)
              let bLine =
                  (Dag.toJsonl sw.Encode d2).Split('\n') |> Array.find (fun l -> l.Contains b)

              match Dag.fromJsonlVerified h sw bLine |> Result.mapError Dag.loadFaultToString with
              | Error e -> Expect.stringContains e "parent" "the load is gated on verifyDag (missing parent)"
              | Ok _ -> failtest "expected the dangling-parent DAG to be refused on load"

          testCase "fromJsonlVerified rejects a tampered node id"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              // rewrite the stored id so it no longer matches the content hash
              let tampered =
                  DagOf.nodes (
                      d1.Nodes
                      |> Map.toList
                      |> List.map (fun (_, n) -> "forged", { n with Id = "forged" })
                      |> Map.ofList
                  )

              Expect.isError
                  (Dag.fromJsonlVerified h sw (Dag.toJsonl sw.Encode tampered))
                  "a content-id mismatch is refused on load"

          // ---- merge-base / branch-delta (Phase 08) ----

          testCase "mergeBase finds the fork point of two branches"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1 |> Reference.built
              let c, d3 = Dag.append h sw (Human "x") (Inc 4) a d2 |> Reference.built // fork off a
              Expect.equal (Dag.mergeBase d3 b c) (Some a) "the fork point a is the merge base"
              Expect.equal (Dag.ancestorsOf d3 a) (Set.ofList [ a ]) "a genesis closure is itself"

          testCase "mergeBase is None for disjoint histories"
          <| fun _ ->
              let g1, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              let g2, d2 = Dag.append h sw (Human "x") (Inc 7) "" d1 |> Reference.built // a second, unrelated genesis
              Expect.equal (Dag.mergeBase d2 g1 g2) None "no common ancestor"

          testCase "between returns the branch delta in topological order"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1 |> Reference.built
              let c, d3 = Dag.append h sw (Human "x") (Inc 4) b d2 |> Reference.built // linear a -> b -> c

              Expect.equal (Dag.between d3 a c |> List.map _.Id) [ b; c ] "nodes after a, up to c"
              Expect.equal (Dag.between d3 c c) [] "base == head ⇒ empty delta"
              Expect.equal (Dag.between d3 b c |> List.map _.Id) [ c ] "single-step delta"

          // Phase 26 — branch delta as an applyable op list.
          testCase "betweenOps projects the branch delta's ops in topological order"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1 |> Reference.built
              let c, d3 = Dag.append h sw (Human "x") (Inc 4) b d2 |> Reference.built

              Expect.equal (Dag.betweenOps d3 a c) [ Inc 3; Inc 4 ] "ops of the nodes between a and c"
              Expect.equal (Dag.betweenOps d3 c c) [] "base == head ⇒ no ops"
              // consistent with `between`
              Expect.equal (Dag.betweenOps d3 a c) (Dag.between d3 a c |> List.map _.Op) "matches between"

          // Phase 21 — DAG break localisation.
          testCase "firstBreak is None for an intact DAG"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              let _, d2 = Dag.append h sw (Human "x") (Inc 3) a d1 |> Reference.built
              Expect.isNone (Dag.firstBreak h sw d2) "intact ⇒ no break"

          testCase "firstBreak localises a tampered node and a dangling parent"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              let _, d2 = Dag.append h sw (Human "x") (Inc 3) a d1 |> Reference.built

              // tamper a node's op without rewriting its id ⇒ content-id mismatch
              let tampered =
                  DagOf.nodes (d2.Nodes |> Map.map (fun _ n -> { n with Op = Inc 999 }))

              match Dag.firstBreak h sw tampered with
              | Some b -> Expect.equal b.Reason DagBreakReason.ContentIdMismatch "names a content-id mismatch"
              | None -> failtest "expected a break"

              // a real node whose parent is dropped from the map: its id still matches its content
              // hash, so the *missing-parent* check (not content-id) is what trips
              let ra, dra = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              let _, drab = Dag.append h sw (Human "x") (Inc 3) ra dra |> Reference.built
              let orphaned = DagOf.nodes (drab.Nodes |> Map.remove ra)

              match Dag.firstBreak h sw orphaned with
              | Some b ->
                  Expect.equal b.Reason DagBreakReason.MissingParent "names a missing parent"
                  Expect.equal b.Got ra "reports the missing parent id"
              | None -> failtest "expected a break"

          // ---- Phase 147: DagBreak.Reason is a closed DU ----

          testCase "fromJsonlVerified's error renders the PRE-0.24.0 spellings byte for byte"
          <| fun _ ->
              // The compatibility half of Phase 147, pinned rather than asserted in prose. `Reason`
              // is typed now, but this error text is what a consumer outside this library matches,
              // and `DagBreakReason.toString` is the only thing keeping it what it was. An exact
              // comparison, not a substring: this case exists to go red if the wording ever moves.
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1 |> Reference.built

              let bLine =
                  (Dag.toJsonl sw.Encode d2).Split('\n') |> Array.find (fun l -> l.Contains b)

              match Dag.fromJsonlVerified h sw bLine |> Result.mapError Dag.loadFaultToString with
              | Error e ->
                  Expect.equal
                      e
                      (sprintf "Dag.fromJsonlVerified: missing parent at node %s" b)
                      "the missing-parent load error is unchanged"
              | Ok _ -> failtest "expected the dangling-parent DAG to be refused on load"

              let tampered =
                  DagOf.nodes (d1.Nodes |> Map.map (fun _ n -> { n with Op = Inc 999 }))

              match Dag.firstBreak h sw tampered with
              | Some br ->
                  Expect.equal
                      (DagBreakReason.toString br.Reason)
                      "content-id mismatch (tampered node)"
                      "the content-id spelling is unchanged"
              | None -> failtest "expected a break"

          testCase "dagBreakReasonLaws certify the named-case discipline on the DAG walker (Phase 147)"
          <| fun _ ->
              let results = Conformance.dagBreakReasonLaws 5147 120

              let fails =
                  results
                  |> List.filter (fun r -> not r.Passed)
                  |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

              if not (List.isEmpty fails) then
                  failtestf "dagBreakReasonLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.dagBreakReasonLaws 5147 120) results "same seed ⇒ identical report"

          // ---- conformance: dagLaws (Phase 07) ----

          testCase "dagLaws certify the reference stream witness green"
          <| fun _ ->
              let genOp (rng: ConfRng.T) =
                  let kind, r1 = ConfRng.intBelow 2 rng
                  let n, r2 = ConfRng.intBelow 5 r1
                  (if kind = 0 then Inc n else Dec n), r2

              let streamGen: StreamGen<CounterOp, int> = { State0 = 0; Op = genOp }
              let results = Conformance.dagLaws sw streamGen h 99 100

              Expect.equal
                  (List.length results)
                  9
                  "verifyDag + determinism + tamper + JSONL round-trip + the three Phase 296 refusals + the two Phase 329 verified-append laws"

              Expect.isTrue (results |> List.forall _.Passed) "dag laws pass"

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

              let cyclic: Dag.T<CounterOp> = DagOf.nodes (Map.ofList [ "a", nodeA; "b", nodeB ])

              Expect.isFalse (Dag.isAcyclic cyclic "a") "the closure is cyclic"

              match Dag.tryTopoOrder cyclic "a" with
              | Error msg -> Expect.stringContains msg "cyclic" "names the cyclic history"
              | Ok _ -> failtest "expected a cyclic-history error"

              match Dag.tryReplayTo sw 0 cyclic "a" with
              | Error(Dag.ReplayFault.CyclicHistory "a") -> ()
              | other -> failtestf "expected CyclicHistory, got %A" other

          testCase "tryReplayTo replays an acyclic DAG like replayTo"
          <| fun _ ->
              let a, d1 = Dag.append h sw (Human "x") (Inc 5) "" Dag.empty |> Reference.built
              let b, d2 = Dag.append h sw (Human "x") (Inc 3) a d1 |> Reference.built
              Expect.isTrue (Dag.isAcyclic d2 b) "a genuine DAG is acyclic"

              match Dag.tryReplayTo sw 0 d2 b with
              | Ok 8 -> ()
              | other -> failtestf "expected Ok 8, got %A" other ]

// ---- Phase 296 — the DAG's typed refusals: an unknown head, an unknown parent, a colliding id ----

[<Tests>]
let refusalTests =
    let x = Human "x"

    testList
        "Dag refusals (Phase 296)"
        [ testCase "ofNodes admits a map filed by node id and refuses a key that is not its node's id (Phase 386)"
          <| fun _ ->
              let a, d1 = Dag.append h sw x (Inc 5) "" Dag.empty |> Reference.built
              let node = d1.Nodes[a]

              match Dag.ofNodes d1.Nodes with
              | Ok d -> Expect.equal d d1 "a map filed by node id rebuilds the same DAG"
              | Error m -> failtestf "a well-filed map was refused: %A" m

              // go-red: the same node filed under another key — the map a public record let a
              // consumer build until 1.0.0, out of step with the id every walk looks parents up by.
              Expect.equal
                  (Dag.ofNodes (Map.ofList [ "elsewhere", node ]))
                  (Error { Key = "elsewhere"; NodeId = a })
                  "a key that is not its node's id is refused, naming both"

              // a node whose id does not match its CONTENT is admitted: that is firstBreak's question
              let tampered = Map.ofList [ a, { node with Op = Inc 6 } ]

              match Dag.ofNodes tampered with
              | Ok d -> Expect.isFalse (Dag.verifyDag h sw d) "admitted, and verifyDag catches the tamper"
              | Error m -> failtestf "a content tamper is firstBreak's to report, not ofNodes': %A" m

          testCase "tryReplayTo refuses a head the DAG does not hold"
          <| fun _ ->
              let a, d1 = Dag.append h sw x (Inc 5) "" Dag.empty |> Reference.built

              Expect.equal (Dag.tryReplayTo sw 0 d1 a) (Ok 5) "a held head replays"

              Expect.equal
                  (Dag.tryReplayTo sw 0 d1 (a + "typo"))
                  (Error(Dag.ReplayFault.UnknownHead(a + "typo")))
                  "a typo'd head is named, never the initial state"

          testCase "append and merge refuse a parent the DAG does not hold"
          <| fun _ ->
              let a, d1 = Dag.append h sw x (Inc 5) "" Dag.empty |> Reference.built

              Expect.equal
                  (Dag.append h sw x (Inc 1) "nope" d1)
                  (Error(DagAppendFault.UnknownParent "nope"))
                  "an append onto an absent parent"

              Expect.equal
                  (Dag.merge h sw x (Inc 0) a "nope" d1)
                  (Error(DagAppendFault.UnknownParent "nope"))
                  "a merge naming an absent parent"

              Expect.equal
                  (Dag.append h sw x (Inc 1) "p,q" d1)
                  (Error(DagAppendFault.CommaInParentId "p,q"))
                  "a comma-bearing id is refused for what it is, before it is looked up"

          testCase
              "an id the DAG holds for a different node is refused; the held node stays (a HashFn that collides on demand)"
          <| fun _ ->
              let collide: HashFn = fun _ _ -> "c"
              let c, d1 = Dag.append collide sw x (Inc 5) "" Dag.empty |> Reference.built

              Expect.equal
                  (Dag.append collide sw x (Inc 6) "" d1)
                  (Error(DagAppendFault.ContentIdCollision c))
                  "a different op under the same id"

              Expect.equal
                  (Dag.append collide sw (Human "y") (Inc 5) "" d1)
                  (Error(DagAppendFault.ContentIdCollision c))
                  "a different actor under the same id"

              Expect.equal (Dag.append collide sw x (Inc 5) "" d1) (Ok(c, d1)) "the SAME node deduplicates, by design"

          testCase "a merge and an append naming the comma-spliced parent are told apart by their parents"
          <| fun _ ->
              let a, d1 = Dag.append h sw x (Inc 5) "" Dag.empty |> Reference.built
              let b, d2 = Dag.append h sw x (Inc 3) a d1 |> Reference.built
              let c, d3 = Dag.append h sw x (Inc 4) a d2 |> Reference.built
              let m, d4 = Dag.merge h sw x (Inc 0) b c d3 |> Reference.built

              let lo, hi =
                  (if System.String.CompareOrdinal(b, c) < 0 then
                       b, c
                   else
                       c, b)

              // A file carrying, beside the merge, a node with the merge's id and the ONE spliced
              // parent — buildable by nothing here, refused as a collision when read.
              let spliced =
                  "{\"node\":true,\"id\":\""
                  + m
                  + "\",\"parents\":[\""
                  + lo
                  + ","
                  + hi
                  + "\"],\"actor\":{\"kind\":\"human\",\"id\":\"x\"},\"op\":"
                  + sw.Encode(Inc 0)
                  + "}"

              match
                  Dag.fromJsonl sw (Dag.toJsonl sw.Encode d4 + "\n" + spliced)
                  |> Result.mapError Dag.loadFaultToString
              with
              | Error e -> Expect.stringContains e "content-id collision" "the spliced node is refused"
              | Ok _ -> failtest "a node colliding with the merge must be refused"

              Expect.equal
                  (Dag.fromJsonl sw (Dag.toJsonl sw.Encode d4 + "\n" + Dag.toJsonl sw.Encode d4))
                  (Ok d4)
                  "a repeated identical node deduplicates on load"

          testCase "appendChecked applies the op at the caller's state: a rejected op never enters the DAG"
          <| fun _ ->
              let a, d1 = Dag.append h sw x (Inc 5) "" Dag.empty |> Reference.built

              match Dag.appendChecked h sw x (Dec 9) 5 a d1 with
              | Error(DagAppendRejection.Domain "would go negative") -> ()
              | other -> failtestf "expected the domain's rejection, got %A" other

              match Dag.appendChecked h sw x (Dec 2) 5 a d1 with
              | Ok r when r.State = 3 ->
                  Expect.equal (Dag.tryReplayTo sw 0 r.Dag r.Id) (Ok 3) "the state it returns is the replay"
              | other -> failtestf "expected the applied node, got %A" other

              match Dag.mergeChecked h sw x (Inc 0) 5 a "nope" d1 with
              | Error(DagAppendRejection.Fault(DagAppendFault.UnknownParent "nope")) -> ()
              | other -> failtestf "the graph refusal is judged first, got %A" other

          testCase "the DAG reader refuses what the shared scanner refuses, with a 1-based line"
          <| fun _ ->
              let a, d1 = Dag.append h sw x (Inc 5) "" Dag.empty |> Reference.built
              let good = Dag.toJsonl sw.Encode d1

              let refused (text: string) (expect: string) =
                  match Dag.fromJsonl sw text |> Result.mapError Dag.loadFaultToString with
                  | Error e ->
                      Expect.stringContains e "line 3:" "the line counts the blank one"
                      Expect.stringContains e expect "and names the reason"
                  | Ok _ -> failtestf "expected a refusal: %s" expect

              refused (good + "\n\n" + good.Replace("\"id\":\"" + a + "\"", "\"id\":12")) "field id is not a string"

              refused
                  (good + "\n\n" + good.Replace("\"parents\":[]", "\"parents\":[1]"))
                  "field parents is not an array of strings"

              // Since Phase 416 an op the witness refuses does not stop the read, so a line repeating a
              // held id with an op it cannot decode is what it is: a content-id collision.
              refused (good + "\n\n" + good.Replace("\"kind\":\"inc\"", "\"kind\":\"bogus\"")) "content-id collision"

              // ...and the same op under an id of its own is the one undecodable site, at its line.
              let fresh =
                  good
                      .Replace("\"id\":\"" + a + "\"", "\"id\":\"n2\"")
                      .Replace("\"kind\":\"inc\"", "\"kind\":\"bogus\"")

              Expect.equal
                  (Dag.fromJsonl sw (good + "\n\n" + fresh))
                  (Error(
                      StreamLoadFault.Undecodable
                          [ { Lane = None
                              Line = 3
                              NodeId = "n2"
                              Reason = "unknown op kind: bogus" } ]
                  ))
                  "the undecodable op is named by its line and id" ]

// ---- Phase 329 — the verified append: the parent is replayed and a handed-in state that is not its
// state is refused ----

[<Tests>]
let verifiedTests =
    let x = Human "x"

    // genesis g (5); fork heads a = g+3 (8) and b = g+4 (9); the merge m of (a, b) with Inc 0 (12).
    let g, d1 = Dag.append h sw x (Inc 5) "" Dag.empty |> Reference.built
    let a, d2 = Dag.append h sw x (Inc 3) g d1 |> Reference.built
    let b, d3 = Dag.append h sw x (Inc 4) g d2 |> Reference.built
    let m, d4 = Dag.merge h sw x (Inc 0) a b d3 |> Reference.built

    let checkedAs r =
        r |> Result.mapError Dag.VerifiedAppendRejection.Checked

    testList
        "Dag verified append (Phase 329)"
        [ testCase "handed the parent's own state, the verified forms answer exactly as the checked forms"
          <| fun _ ->
              Expect.equal
                  (Dag.appendVerified h sw 0 x (Inc 1) 8 a d4)
                  (Dag.appendChecked h sw x (Inc 1) 8 a d4 |> checkedAs)
                  "an append onto a fork head"

              Expect.equal
                  (Dag.appendVerified h sw 0 x (Dec 9) 8 a d4)
                  (Error(Dag.VerifiedAppendRejection.Checked(DagAppendRejection.Domain "would go negative")))
                  "the domain's rejection at a verified state is the checked form's, verbatim"

              // the merge's parents replay to 5 + 3 + 4, without the merge op
              match Dag.mergeVerified h sw 0 x (Inc 0) 12 a b d3 with
              | Ok r as verified when r.State = 12 ->
                  Expect.equal verified (Dag.mergeChecked h sw x (Inc 0) 12 a b d3 |> checkedAs) "the checked merge"
                  Expect.equal r.Id m "the same merge node"
                  Expect.equal (Dag.tryReplayTo sw 0 r.Dag r.Id) (Ok 12) "whose own replay is the state returned"
              | other -> failtestf "expected the verified merge, got %A" other

          testCase "the genesis parent replays to the initial state itself"
          <| fun _ ->
              Expect.equal
                  (Dag.appendVerified h sw 0 x (Inc 5) 0 "" Dag.empty)
                  (Dag.appendChecked h sw x (Inc 5) 0 "" Dag.empty |> checkedAs)
                  "a genesis append handed the initial state"

              Expect.equal
                  (Dag.appendVerified h sw 0 x (Inc 5) 3 "" d4)
                  (Error(Dag.VerifiedAppendRejection.StateMismatch(3, 0)))
                  "a genesis append handed any other state"

          testCase "forking from an older node while holding the latest head's state is refused, both states named"
          <| fun _ ->
              // At the merge's 12, Dec 7 applies; at g's own 5 it does not — the checked form admits
              // an op that never applied at that point in the graph.
              Expect.isOk (Dag.appendChecked h sw x (Dec 7) 12 g d4) "the checked form admits it"

              Expect.equal
                  (Dag.appendVerified h sw 0 x (Dec 7) 12 g d4)
                  (Error(Dag.VerifiedAppendRejection.StateMismatch(12, 5)))
                  "the verified form refuses the pairing"

          testCase "appending to one head while holding another's is refused, both states named"
          <| fun _ ->
              // At b's own 9, Dec 9 applies; at a's 8 it does not — the checked form refuses a valid op.
              Expect.equal
                  (Dag.appendChecked h sw x (Dec 9) 8 b d3)
                  (Error(DagAppendRejection.Domain "would go negative"))
                  "the checked form refuses a valid op"

              Expect.equal
                  (Dag.appendVerified h sw 0 x (Dec 9) 8 b d3)
                  (Error(Dag.VerifiedAppendRejection.StateMismatch(8, 9)))
                  "the verified form names the mis-pairing instead"

              Expect.equal
                  (Dag.appendVerified h sw 0 x (Inc 1) 9 a d3)
                  (Error(Dag.VerifiedAppendRejection.StateMismatch(9, 8)))
                  "and the other way round"

          testCase "passing one side's state after a merge is refused, both states named"
          <| fun _ ->
              Expect.equal
                  (Dag.appendVerified h sw 0 x (Inc 1) 8 m d4)
                  (Error(Dag.VerifiedAppendRejection.StateMismatch(8, 12)))
                  "an append onto the merge holding a's state"

              Expect.equal
                  (Dag.mergeVerified h sw 0 x (Inc 0) 9 a b d3)
                  (Error(Dag.VerifiedAppendRejection.StateMismatch(9, 12)))
                  "a merge handed b's state alone"

          testCase "a parent whose replay is itself refused surfaces the replay fault"
          <| fun _ ->
              // The plain append does not apply the op (D83), so a rejecting node can be built — one
              // that rejects wherever it drains, since no state in this DAG reaches 20.
              let bad, e1 = Dag.append h sw x (Dec 20) g d4 |> Reference.built

              let fault = Dag.ReplayFault.Rejected(bad, "would go negative")

              Expect.equal (Dag.tryReplayTo sw 0 e1 bad) (Error fault) "the parent does not replay"

              Expect.equal
                  (Dag.appendVerified h sw 0 x (Inc 1) 0 bad e1)
                  (Error(Dag.VerifiedAppendRejection.ParentReplay fault))
                  "an append onto it"

              Expect.equal
                  (Dag.mergeVerified h sw 0 x (Inc 0) 0 m bad e1)
                  (Error(Dag.VerifiedAppendRejection.ParentReplay fault))
                  "a merge with it"

              let node id parent : DagNode<CounterOp> =
                  { Id = id
                    Parents = [ parent ]
                    Actor = x
                    Op = Inc 1 }

              let cyclic: Dag.T<CounterOp> =
                  DagOf.nodes (Map.ofList [ "p", node "p" "q"; "q", node "q" "p" ])

              Expect.equal
                  (Dag.appendVerified h sw 0 x (Inc 1) 0 "p" cyclic)
                  (Error(Dag.VerifiedAppendRejection.ParentReplay(Dag.ReplayFault.CyclicHistory "p")))
                  "a cyclic history"

          testCase "the graph refusals are judged before anything is replayed"
          <| fun _ ->
              Expect.equal
                  (Dag.appendVerified h sw 0 x (Inc 1) 0 "nope" d4)
                  (Error(
                      Dag.VerifiedAppendRejection.Checked(DagAppendRejection.Fault(DagAppendFault.UnknownParent "nope"))
                  ))
                  "an unknown parent"

              Expect.equal
                  (Dag.mergeVerified h sw 0 x (Inc 0) 0 "" a d4)
                  (Error(Dag.VerifiedAppendRejection.Checked(DagAppendRejection.Fault DagAppendFault.EmptyParentId)))
                  "the genesis marker as a merge parent"

          testCase "the …With forms take the caller's equality; one that skips the comparison verifies nothing"
          <| fun _ ->
              let skips = fun (_: int) (_: int) -> true

              Expect.equal
                  (Dag.appendVerifiedWith skips h sw 0 x (Dec 7) 12 g d4)
                  (Dag.appendChecked h sw x (Dec 7) 12 g d4 |> checkedAs)
                  "a comparison that always agrees is the checked form, mis-pairing and all"

              Expect.equal
                  (Dag.mergeVerifiedWith skips h sw 0 x (Inc 0) 9 a b d3)
                  (Dag.mergeChecked h sw x (Inc 0) 9 a b d3 |> checkedAs)
                  "for a merge too"

              // a coarser domain equality: states agreeing on parity are one state to this caller
              let parity = fun (p: int) (q: int) -> p % 2 = q % 2

              Expect.isOk (Dag.appendVerifiedWith parity h sw 0 x (Inc 1) 10 a d4) "10 and 8 agree on parity"

              Expect.equal
                  (Dag.appendVerifiedWith parity h sw 0 x (Inc 1) 9 a d4)
                  (Error(Dag.VerifiedAppendRejection.StateMismatch(9, 8)))
                  "9 and 8 do not" ]

// ---- Phase 383 — the parents reader refuses a bad string with the scanner's reason ----

[<Tests>]
let parentsScanTests =
    let x = Human "x"

    /// One DAG line with `parents` LAST, so the scanner's refusal is the parents array's own.
    let lineWith (id: string) (parents: string) =
        "{\"node\":true,\"id\":\""
        + id
        + "\",\"actor\":{\"kind\":\"human\",\"id\":\"x\"},\"op\":"
        + sw.Encode(Inc 1)
        + ",\"parents\":"
        + parents
        + "}"

    testList
        "Dag.fromJsonl refuses a bad parents string (Phase 383)"
        [ testCase "an unterminated string, a bad escape and a bad \\u are each refused with the scanner's reason"
          <| fun _ ->
              let a, d1 = Dag.append h sw x (Inc 5) "" Dag.empty |> Reference.built
              let good = Dag.toJsonl sw.Encode d1

              for parents, reason in
                  [ "[\"" + a + "]", "unterminated string"
                    "[\"a\\qb\"]", "invalid escape \\q"
                    "[\"\\uZZZZ\"]", "invalid escape \\uZZZZ" ] do
                  match
                      Dag.fromJsonl sw (good + "\n" + lineWith "n2" parents)
                      |> Result.mapError Dag.loadFaultToString
                  with
                  | Error e ->
                      Expect.stringContains e "line 2:" "the second line is named"
                      Expect.stringContains e reason (sprintf "and the scanner's reason, for %s" parents)
                  | Ok dag -> failtestf "a parents array %s must be refused, read %A" parents dag

              // the well-formed spelling of the same line reads
              match
                  Dag.fromJsonl sw (good + "\n" + lineWith "n2" ("[\"" + a + "\"]"))
                  |> Result.mapError Dag.loadFaultToString
              with
              | Ok dag -> Expect.equal (dag.Nodes |> Map.find "n2").Parents [ a ] "the parent is read"
              | Error e -> failtestf "the well-formed line must read: %s" e ]

// ---- Phase 416: one DAG text written by a newer host ----

[<Tests>]
let newerDagTests =
    let nw = OpStreamTests.Newer.witness
    let x = Human "x"

    let built r =
        match r with
        | Ok(c: Dag.CheckedAppend<int, OpStreamTests.Newer.NewerOp>) -> c.Id, c.Dag
        | Error e -> failwithf "the newer host could not write: %A" e

    // g; r1 = reset on g; a on r1; r2 = reset on a
    let newerDag () =
        let g, d1 =
            Dag.appendChecked h nw x (OpStreamTests.Newer.Known(Inc 1)) 0 "" Dag.empty
            |> built

        let r1, d2 = Dag.appendChecked h nw x OpStreamTests.Newer.Reset 1 g d1 |> built

        let a, d3 =
            Dag.appendChecked h nw x (OpStreamTests.Newer.Known(Inc 2)) 0 r1 d2 |> built

        let r2, d4 = Dag.appendChecked h nw x OpStreamTests.Newer.Reset 2 a d3 |> built
        d4, [ r1; r2 ]

    testList
        "Dag loads type their faults (Phase 416)"
        [ testCase "an intact DAG holding ops this witness does not know is Undecodable, naming every one in line order"
          <| fun _ ->
              let dag, resets = newerDag ()
              let text = Dag.toJsonl nw.Encode dag

              let lineOf (id: string) =
                  1
                  + (text.Split('\n')
                     |> Array.findIndex (fun l -> l.Contains("\"id\":\"" + id + "\"")))

              let expected =
                  resets
                  |> List.map (fun id ->
                      { Lane = None
                        Line = lineOf id
                        NodeId = id
                        Reason = OpStreamTests.Newer.refusal })
                  |> List.sortBy (fun s -> s.Line)

              Expect.equal (Dag.fromJsonlVerified h sw text) (Error(StreamLoadFault.Undecodable expected)) "verified"
              Expect.equal (Dag.fromJsonl sw text) (Error(StreamLoadFault.Undecodable expected)) "structural"
              Expect.equal (Dag.fromJsonlVerified h nw text) (Ok dag) "the newer witness reads it"

          testCase "a tampered DAG answers the break whatever else it holds"
          <| fun _ ->
              let dag, resets = newerDag ()
              let text = Dag.toJsonl nw.Encode dag
              let r1 = resets.Head
              let tampered = text.Replace("\"id\":\"" + r1 + "\"", "\"id\":\"" + r1 + "0\"")
              Expect.notEqual tampered text "the probe flipped the id"

              match Dag.fromJsonlVerified h sw tampered with
              | Error(StreamLoadFault.Broken _) -> ()
              | other -> failtestf "expected the break, got %A" other ]
