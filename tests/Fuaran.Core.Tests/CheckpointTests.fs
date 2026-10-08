module Fuaran.Core.Tests.CheckpointTests

// Phase 288 — a checkpoint on the lane DAG: `Dag.Checkpoint`, `checkpointAt` / `sealAt` /
// `checkpointFrom`, `verifyCheckpoint`, the bounded replay `replayFrom` / `replayFromWith`, the DAG
// that begins at a checkpoint (`compactAt` / `compactFrom`, `firstBreakFrom` / `verifyDagFrom`), the
// sidecar codec, and `Conformance.checkpointLaws`.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference.Counter

let private sw = witness
let private h = OpStream.defaultHash
let private enc (s: int) : string = string s

let private dec (s: string) : Result<int, string> =
    match System.Int32.TryParse s with
    | true, v -> Ok v
    | _ -> Error("not an int: " + s)

let private x = Human "x"

/// genesis g; a and b fork off g; m merges a and b; y extends m.
let private forkMergeTail () =
    let g, d1 = Dag.append h sw x (Inc 1) "" Dag.empty |> Reference.built
    let a, d2 = Dag.append h sw x (Inc 2) g d1 |> Reference.built
    let b, d3 = Dag.append h sw x (Inc 3) g d2 |> Reference.built
    let m, d4 = Dag.merge h sw x (Inc 4) a b d3 |> Reference.built
    let y, d5 = Dag.append h sw x (Inc 5) m d4 |> Reference.built
    g, a, b, m, y, d5

let private ok (r: Result<'a, 'e>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> failwithf "expected Ok, got %A" e

// ---- a witness whose ops do not commute, under a hash that names each node by its op ----
// A log: each op appends its letter. The node id is the op's own text, so the test chooses the ids,
// and with them the order the drain folds a fork in (smallest id first).

let private logWitness: StreamWitness<string, string, string> =
    { Apply = fun op st -> Ok(st + op)
      Encode = fun op -> "\"" + op + "\""
      Decode = fun s -> Ok(s.Trim('"')) }

let private named: HashFn =
    fun _ payload -> payload.Substring(payload.LastIndexOf('|') + 1).Trim('"')

let private logEnc (s: string) = "\"" + s + "\""

/// 0g, then 2a -> 3b on one lane and 1c on another off 0g, merged by 4m. The drain folds 1c BEFORE
/// 2a, because its id is smaller, so 1c is folded inside the prefix a checkpoint at 3b stands for.
let private branchBelow () =
    let w = logWitness
    let g, d1 = Dag.append named w x "0g" "" Dag.empty |> Reference.built
    let a, d2 = Dag.append named w x "2a" g d1 |> Reference.built
    let b, d3 = Dag.append named w x "3b" a d2 |> Reference.built
    let c, d4 = Dag.append named w x "1c" g d3 |> Reference.built
    let m, d5 = Dag.merge named w x "4m" b c d4 |> Reference.built
    g, a, b, c, m, d5

/// One sidecar line with the character at `i` replaced.
let private flipAt (i: int) (s: string) =
    let c = s.[i]

    let c' =
        if System.Char.IsDigit c then
            (if c = '9' then '0' else char (int c + 1))
        elif c = 'a' then
            'b'
        else
            'a'

    s.Substring(0, i) + string c' + s.Substring(i + 1)

[<Tests>]
let tests =
    testList
        "Dag checkpoints (Phase 288)"
        [ testCase
              "checkpointAt seals the state the replay reaches, and the seal is the strict snapshot's at sequence zero"
          <| fun _ ->
              let _, _, _, m, _, dag = forkMergeTail ()
              let cp = Dag.checkpointAt h enc sw 0 dag m |> ok
              Expect.equal cp.Node m "the node"
              Expect.equal cp.State 10 "1 + 2 + 3 + 4"
              Expect.equal (Dag.verifyCheckpoint h enc sw cp dag) (Ok()) "it verifies"

              // the one pre-image, reached through the linear family: the snapshot at seq 0 chained from m
              let snap =
                  OpStream.Snapshots.take SnapshotMode.Strict OpStream.canonicalConfig h enc sw 10 [] 0
                  |> ok

              Expect.equal snap.Hash (h "" "{\"snapshot\":true,\"seq\":0,\"state\":10}") "the linear seal at genesis"

              Expect.equal
                  cp.Hash
                  (h m "{\"snapshot\":true,\"seq\":0,\"state\":10}")
                  "the DAG seal chains from the node id"

          testCase "a checkpoint with its state, node id or seal changed fails verifyCheckpoint, by name"
          <| fun _ ->
              let g, _, _, m, y, dag = forkMergeTail ()
              let cp = Dag.checkpointAt h enc sw 0 dag m |> ok

              let isSeal =
                  function
                  | Error(Dag.CheckpointBreak.Seal _) -> true
                  | _ -> false

              Expect.isTrue (isSeal (Dag.verifyCheckpoint h enc sw { cp with State = 11 } dag)) "a changed state"
              Expect.isTrue (isSeal (Dag.verifyCheckpoint h enc sw { cp with Node = y } dag)) "moved to another node"
              Expect.isTrue (isSeal (Dag.verifyCheckpoint h enc sw { cp with Node = g } dag)) "moved behind"

              Expect.equal
                  (Dag.verifyCheckpoint h enc sw { cp with Node = "absent" } dag)
                  (Error(Dag.CheckpointBreak.UnknownNode "absent"))
                  "a node the DAG does not hold is named"

              Expect.isTrue
                  (isSeal (Dag.verifyCheckpoint h enc sw { cp with Hash = cp.Hash + "0" } dag))
                  "a changed seal"

          testCase
              "one byte of the sidecar line changed — in the state, the node id or the seal — fails verifyCheckpoint"
          <| fun _ ->
              let _, _, _, m, _, dag = forkMergeTail ()
              let cp = Dag.checkpointAt h enc sw 0 dag m |> ok
              let lane, side = Dag.toJsonlWithCheckpoints encode enc dag [ cp ]

              Expect.equal
                  side
                  ("{\"snapshot\":true,\"seq\":0,\"state\":10,\"prevHash\":\""
                   + m
                   + "\",\"hash\":\""
                   + cp.Hash
                   + "\"}")
                  "the sidecar line is the linear snapshot line"

              let at (marker: string) = side.IndexOf(marker) + marker.Length

              for i in [ at "\"state\":"; at "\"prevHash\":\""; at "\"hash\":\"" ] do
                  let tampered = flipAt i side

                  match Dag.fromJsonlWithCheckpoints sw dec lane (Some tampered) with
                  | Ok(d, [ cp' ]) ->
                      Expect.isError (Dag.verifyCheckpoint h enc sw cp' d) (sprintf "byte %d changed: %s" i tampered)
                  | other -> failtestf "byte %d changed: the sidecar read as %A" i other

          testCase "replayFrom folds only the history above the checkpoint and answers as the full replay"
          <| fun _ ->
              let g, a, b, m, y, dag = forkMergeTail ()

              // g (before the fork) and m (after the merge) cover every head above them
              for node in [ g; m ] do
                  let cp = Dag.checkpointAt h enc sw 0 dag node |> ok

                  for head in [ y; m ] do
                      Expect.equal
                          (Dag.replayFrom sw cp dag head)
                          (Dag.tryReplayTo sw 0 dag head |> Result.mapError Dag.CheckpointFault.Replay)
                          (sprintf "from %s to %s" node head)

              // a checkpoint on one lane does not cover the merge of both: the other lane is named
              for node, other in [ a, b; b, a ] do
                  let cp = Dag.checkpointAt h enc sw 0 dag node |> ok

                  for head in [ y; m ] do
                      Expect.equal
                          (Dag.replayFrom sw cp dag head)
                          (Error(Dag.CheckpointFault.Uncovered other))
                          (sprintf "from %s to %s" node head)

              // a checkpoint whose state is NOT its node's replay shows the replay starts from it
              let taken = Dag.checkpointAt h enc sw 0 dag m |> ok
              let cp = { taken with State = 100 }
              Expect.equal (Dag.replayFrom sw cp dag y) (Ok 105) "only y's op is folded over the checkpoint"

          testCase "replayFrom refuses an unknown node or head, an unreached head and a cyclic history by name"
          <| fun _ ->
              let _, a, b, m, _, dag = forkMergeTail ()
              let cpA = Dag.checkpointAt h enc sw 0 dag a |> ok

              Expect.equal (Dag.replayFrom sw cpA dag b) (Error(Dag.CheckpointFault.Unreached b)) "b is not above a"

              Expect.equal
                  (Dag.replayFrom sw cpA dag "nope")
                  (Error(Dag.CheckpointFault.Replay(Dag.ReplayFault.UnknownHead "nope")))
                  "an unknown head"

              Expect.equal
                  (Dag.replayFrom sw { cpA with Node = "nope" } dag m)
                  (Error(Dag.CheckpointFault.UnknownNode "nope"))
                  "an unknown checkpoint node"

              Expect.equal
                  (Dag.checkpointAt h enc sw 0 dag "nope")
                  (Error(Dag.CheckpointFault.UnknownNode "nope"))
                  "checkpointAt names the node"

              let rejecting, d = Dag.append h sw x (Dec 99) m dag |> Reference.built

              Expect.equal
                  (Dag.replayFrom sw (Dag.checkpointAt h enc sw 0 d m |> ok) d rejecting)
                  (Error(Dag.CheckpointFault.Replay(Dag.ReplayFault.Rejected(rejecting, "would go negative"))))
                  "the domain's rejection, at the node the full replay names"

              let template = dag.Nodes.[a]

              let cyclic: Dag.T<CounterOp> =
                  DagOf.nodes (
                      dag.Nodes
                      |> Map.add
                          "p"
                          { template with
                              Id = "p"
                              Parents = [ a; "q" ] }
                      |> Map.add
                          "q"
                          { template with
                              Id = "q"
                              Parents = [ "p" ] }
                  )

              Expect.equal
                  (Dag.replayFrom sw cpA cyclic "q")
                  (Error(Dag.CheckpointFault.Replay(Dag.ReplayFault.CyclicHistory "q")))
                  "a cyclic history"

          testCase
              "a branch that leaves below the checkpoint and merges above it is refused — resuming would fold it out of order"
          <| fun _ ->
              let g, _, b, c, m, dag = branchBelow ()
              let full = Dag.tryReplayTo logWitness "" dag m |> ok
              Expect.equal full "0g1c2a3b4m" "the drain folds 1c before 2a"

              let cp = Dag.checkpointAt named logEnc logWitness "" dag b |> ok
              Expect.equal cp.State "0g2a3b" "the checkpoint at 3b"

              // the fold a checkpoint that ignored the branch would compute: its state, then the delta
              let naive = Dag.betweenOps dag b m |> List.fold (fun st op -> st + op) cp.State

              Expect.notEqual
                  naive
                  full
                  "resuming at 3b and folding the rest gives a state the full replay never reaches"

              Expect.equal
                  (Dag.replayFrom logWitness cp dag m)
                  (Error(Dag.CheckpointFault.Uncovered c))
                  "so replayFrom names the branch instead"

              Expect.equal
                  (Dag.compactAt named logEnc logWitness "" dag b)
                  (Error(Dag.CheckpointFault.Uncovered c))
                  "and a compaction at 3b would strand it"

              // a checkpoint at the fork point covers both lanes
              let atFork = Dag.checkpointAt named logEnc logWitness "" dag g |> ok

              Expect.equal
                  (Dag.replayFrom logWitness atFork dag m)
                  (Ok full)
                  "from the fork point the merge replays exactly"

              let reach = Dag.Reach.ofDag dag

              for node in [ g; b ] do
                  let cp = Dag.checkpointAt named logEnc logWitness "" dag node |> ok

                  Expect.equal
                      (Dag.replayFromWith logWitness cp reach m)
                      (Dag.replayFrom logWitness cp dag m)
                      ("the indexed form agrees from " + node)

          testCase
              "compactAt keeps the node and what is after it; the compacted DAG verifies and replays as the full one"
          <| fun _ ->
              let g, a, b, m, y, dag = forkMergeTail ()
              let cp, small = Dag.compactAt h enc sw 0 dag m |> ok
              Expect.equal cp (Dag.checkpointAt h enc sw 0 dag m |> ok) "the checkpoint checkpointAt takes"
              Expect.equal (small.Nodes |> Map.toList |> List.map fst |> Set.ofList) (set [ m; y ]) "m and y kept"
              Expect.isTrue (Dag.verifyDagFrom h enc sw cp small) "the compacted DAG verifies from its checkpoint"
              Expect.isTrue (Dag.verifyDagFrom h enc sw cp dag) "so does the full DAG"

              Expect.equal
                  (Dag.firstBreak h sw small)
                  (Some
                      { NodeId = m
                        Reason = DagBreakReason.MissingParent
                        Expected = ""
                        Got = a })
                  "without its checkpoint the truncated lane is a missing-parent break, as it should be"

              for head in [ m; y ] do
                  Expect.equal
                      (Dag.replayFrom sw cp small head)
                      (Dag.tryReplayTo sw 0 dag head |> Result.mapError Dag.CheckpointFault.Replay)
                      ("replay to " + head)

              Expect.equal
                  (Dag.replayFrom sw cp small g)
                  (Error(Dag.CheckpointFault.Replay(Dag.ReplayFault.UnknownHead g)))
                  "what was truncated is gone"

          testCase "a byte changed in a node the compaction keeps, or in its checkpoint, fails verifyDagFrom"
          <| fun _ ->
              let _, _, _, m, _, dag = forkMergeTail ()
              let cp, small = Dag.compactAt h enc sw 0 dag m |> ok
              let lane, side = Dag.toJsonlWithCheckpoints encode enc small [ cp ]

              let verifies (laneText: string) (sideText: string) =
                  match Dag.fromJsonlWithCheckpoints sw dec laneText (Some sideText) with
                  | Ok(d, [ cp' ]) -> Dag.verifyDagFrom h enc sw cp' d
                  | _ -> false

              Expect.isTrue (verifies lane side) "intact"

              let lines = lane.Split('\n')

              for k in 0 .. lines.Length - 1 do
                  let line = lines.[k]

                  let positions =
                      [ line.IndexOf("\"id\":\"") + 6 // the node id
                        line.LastIndexOfAny("0123456789".ToCharArray()) ] // the op's operand
                      @ (if line.Contains("\"parents\":[\"") then
                             [ line.IndexOf("\"parents\":[\"") + 12 ]
                         else
                             [])

                  for i in positions do
                      let lines' = Array.copy lines
                      lines'.[k] <- flipAt i line
                      let lane' = String.concat "\n" lines'
                      Expect.isFalse (verifies lane' side) (sprintf "line %d byte %d changed: %s" k i lines'.[k])

              for marker in [ "\"state\":"; "\"prevHash\":\""; "\"hash\":\"" ] do
                  let i = side.IndexOf(marker) + marker.Length
                  Expect.isFalse (verifies lane (flipAt i side)) ("the checkpoint changed after " + marker)

          testCase "compactAt keeps the band when a node after the checkpoint names a parent behind it"
          <| fun _ ->
              // g -> a -> b (the checkpoint); z extends b; w merges z with a, which is behind b
              let g, d1 = Dag.append h sw x (Inc 1) "" Dag.empty |> Reference.built
              let a, d2 = Dag.append h sw x (Inc 2) g d1 |> Reference.built
              let b, d3 = Dag.append h sw x (Inc 3) a d2 |> Reference.built
              let z, d4 = Dag.append h sw x (Inc 4) b d3 |> Reference.built
              let w, dag = Dag.merge h sw x (Inc 5) z a d4 |> Reference.built

              let cp, small = Dag.compactAt h enc sw 0 dag b |> ok

              Expect.equal
                  (small.Nodes |> Map.toList |> List.map fst |> Set.ofList)
                  (set [ a; b; z; w ])
                  "a is kept, g is not"

              Expect.isTrue (Dag.verifyDagFrom h enc sw cp small) "verifies"

              Expect.equal
                  (Dag.replayFrom sw cp small w)
                  (Dag.tryReplayTo sw 0 dag w |> Result.mapError Dag.CheckpointFault.Replay)
                  "a is not folded twice"

              // drop the band and w names a parent the DAG does not hold
              let banless = DagOf.nodes (small.Nodes |> Map.remove a)

              Expect.equal
                  (Dag.firstBreakFrom h enc sw cp banless)
                  (Some(
                      Dag.CheckpointBreak.Node
                          { NodeId = w
                            Reason = DagBreakReason.MissingParent
                            Expected = ""
                            Got = a }
                  ))
                  "after the checkpoint nothing may be missing"

          testCase "a compacted DAG lives on: append onto its node, checkpoint and compact again"
          <| fun _ ->
              let _, _, _, m, y, dag = forkMergeTail ()
              let cp, small = Dag.compactAt h enc sw 0 dag y |> ok
              Expect.equal (Dag.heads small) [ y ] "an empty history above the checkpoint has its node as the head"

              let n, small' = Dag.append h sw x (Inc 6) y small |> Reference.built
              let n2, dag' = Dag.append h sw x (Inc 6) y dag |> Reference.built
              Expect.equal n n2 "the same node as on the full DAG"
              Expect.isTrue (Dag.verifyDagFrom h enc sw cp small') "the appended DAG verifies"
              Expect.equal (Dag.replayFrom sw cp small' n) (Ok 21) "1 + … + 6"

              let reach = Dag.Reach.ofDag small'
              Expect.equal (Dag.replayFromWith sw cp reach n) (Ok 21) "the index builds over a compacted DAG"

              let cp2 = Dag.checkpointFrom h enc sw cp small' n |> ok
              Expect.equal cp2 (Dag.checkpointAt h enc sw 0 dag' n |> ok) "the next checkpoint is the full DAG's"

              let again = Dag.compactFrom h enc sw cp small' n |> ok
              let direct = Dag.compactAt h enc sw 0 dag' n |> ok
              Expect.equal (fst again) (fst direct) "re-compacting gives the full DAG's checkpoint"
              Expect.equal (snd again).Nodes (snd direct).Nodes "and the full DAG's retained nodes"

              Expect.equal
                  (Dag.checkpointFrom h enc sw cp small' "nope")
                  (Error(Dag.CheckpointFault.UnknownNode "nope"))
                  "an absent node is named"

              Expect.equal
                  (Dag.checkpointFrom h enc sw cp small' m)
                  (Error(Dag.CheckpointFault.UnknownNode m))
                  "behind the origin is gone"

          testCase "the genesis-import shape: a history that begins at a sealed, converted state"
          <| fun _ ->
              // the import node is an ordinary genesis node; its state was converted, not folded
              let imp, d1 =
                  Dag.append h sw (Human "import") (Inc 0) "" Dag.empty |> Reference.built

              let cp = Dag.sealAt h enc sw imp 42
              let p, d2 = Dag.append h sw x (Inc 1) imp d1 |> Reference.built
              let q, d3 = Dag.append h sw x (Inc 2) imp d2 |> Reference.built
              let r, dag = Dag.merge h sw x (Inc 3) p q d3 |> Reference.built

              Expect.isTrue (Dag.verifyDagFrom h enc sw cp dag) "the history begins at the checkpoint and verifies"
              Expect.equal (Dag.replayFrom sw cp dag r) (Ok 48) "42 + 1 + 2 + 3"
              Expect.equal (Dag.tryReplayTo sw 0 dag r) (Ok 6) "a replay from the origin knows nothing of the import"

              let second, withRoot = Dag.append h sw x (Inc 7) "" dag |> Reference.built

              Expect.equal
                  (Dag.firstBreakFrom h enc sw cp withRoot)
                  (Some(Dag.CheckpointBreak.Uncovered second))
                  "a second root is history the checkpoint does not cover"

          testCase "a lane file reads exactly as before, with or without a sidecar"
          <| fun _ ->
              let _, _, _, m, y, dag = forkMergeTail ()
              let cp = Dag.checkpointAt h enc sw 0 dag m |> ok
              let lane, side = Dag.toJsonlWithCheckpoints encode enc dag [ cp ]
              Expect.equal lane (Dag.toJsonl encode dag) "the lane is toJsonl's bytes"
              Expect.equal (Dag.toJsonlWithCheckpoints encode enc dag [] |> snd) "" "no checkpoints, an empty sidecar"

              let read = Dag.fromJsonlWithCheckpoints sw dec lane None |> ok
              Expect.equal (fst read) (Dag.fromJsonl sw lane |> ok) "no sidecar reads as fromJsonl"
              Expect.equal (snd read) [] "and carries no checkpoint"
              Expect.equal (Dag.fromJsonlWithCheckpoints sw dec lane (Some "") |> ok |> snd) [] "an empty sidecar"

              let cpY = Dag.checkpointAt h enc sw 0 dag y |> ok
              let _, side2 = Dag.toJsonlWithCheckpoints encode enc dag [ cp; cpY ]

              Expect.equal
                  (Dag.fromJsonlWithCheckpoints sw dec lane (Some(side2 + "\n")) |> ok |> snd)
                  [ cp; cpY ]
                  "the sidecar round-trips in order"

              Expect.isTrue (side2.StartsWith side) "one line per checkpoint"

          testCase "the sidecar reader refuses what is not a strict checkpoint at sequence zero"
          <| fun _ ->
              let _, _, _, m, _, dag = forkMergeTail ()
              let cp = Dag.checkpointAt h enc sw 0 dag m |> ok
              let lane, side = Dag.toJsonlWithCheckpoints encode enc dag [ cp ]

              let refused (sidecar: string) (fragment: string) =
                  match Dag.fromJsonlWithCheckpoints sw dec lane (Some sidecar) with
                  | Error e ->
                      Expect.stringStarts e "checkpoint sidecar: line " "named as the sidecar's"
                      Expect.stringContains e fragment "and why"
                  | Ok v -> failtestf "accepted %A" v

              refused (side + "\n" + side.Replace("\"seq\":0", "\"seq\":1")) "seq 1"

              refused
                  (side.Replace("\"seq\":0,\"state\":10", "\"seq\":0,\"state\":10,\"stateHashed\":false"))
                  "chain-only"

              refused (side.Replace("\"snapshot\":true,", "")) "not a checkpoint line"
              refused (side.Replace("\"prevHash\":\"" + m + "\"", "\"prevHash\":\"\"")) "empty"
              refused (side.Replace("\"state\":10", "\"state\":\"ten\"")) "not an int"

              match Dag.fromJsonlWithCheckpoints sw dec lane (Some(side + "\n" + "{")) with
              | Error e -> Expect.stringStarts e "checkpoint sidecar: line 2:" "the line number"
              | Ok v -> failtestf "accepted %A" v ]

/// The `Conformance.checkpointLaws` cases — the suite `proofs.json`'s Phase 288 rows cite.
[<Tests>]
let checkpointLawTests =
    testList
        "Conformance.checkpointLaws"
        [ testCase "checkpointLaws certify the reference stream witness green"
          <| fun _ ->
              let results =
                  Conformance.checkpointLaws ConformanceTests.sw ConformanceTests.streamGen enc h 288 100

              Expect.equal (List.length results) 10 "nine laws and the DAG-shape guard"

              for r in results do
                  Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

          testCase "checkpointLaws under SHA-256 certify green too"
          <| fun _ ->
              let results =
                  Conformance.checkpointLaws
                      ConformanceTests.sw
                      ConformanceTests.streamGen
                      enc
                      OpStream.sha256Hash
                      2880
                      60

              for r in results do
                  Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

          testCase "checkpointLaws certify a witness whose ops do not commute"
          <| fun _ ->
              let letters = [ "a"; "b"; "c"; "d"; "e" ]

              let gen: StreamGen<string, string> =
                  { State0 = ""
                    Op = fun r -> ConfRng.choose letters r }

              let results = Conformance.checkpointLaws logWitness gen logEnc h 2881 100

              for r in results do
                  Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

          testCase "a sample that never branches reads INCONCLUSIVE, not green"
          <| fun _ ->
              // A hash that answers one id for everything refuses every step after the first as a
              // content-id collision, so each sample is one node: no branch point above or below any
              // checkpoint, and nothing after one to compact. The guard reds; no law is refuted.
              let oneId: HashFn = fun _ _ -> "one"

              let results =
                  Conformance.checkpointLaws ConformanceTests.sw ConformanceTests.streamGen enc oneId 4242 50

              let guardPrefix = SampleAdequacy.lawPrefix "Conformance.checkpointLaws"

              for r in results do
                  if not (r.Law.StartsWith guardPrefix) then
                      Expect.isTrue
                          (r.Passed
                           || (defaultArg r.Counterexample "").StartsWith SampleAdequacy.neverReached)
                          (sprintf "no law is refuted, only untested: %s %A" r.Law r.Counterexample)

              let guard = results |> List.find (fun r -> r.Law.StartsWith guardPrefix)
              Expect.isFalse guard.Passed "the DAG-shape guard is red"

              Expect.stringContains
                  (defaultArg guard.Counterexample "")
                  "never reached covered replay over a lane merge"
                  "naming the shape the sample never held"

          testCase "a replay that ignored a branch from below the checkpoint is caught by the laws"
          <| fun _ ->
              // The go-red: hand the laws a log witness under the naming hash, then compare the law's
              // own refusal with what a checkpoint-blind fold would have answered on the fixed shape.
              let _, _, b, c, m, dag = branchBelow ()
              let cp = Dag.checkpointAt named logEnc logWitness "" dag b |> ok

              let blind = Dag.betweenOps dag b m |> List.fold (fun st op -> st + op) cp.State

              Expect.notEqual (Ok blind) (Dag.tryReplayTo logWitness "" dag m) "the blind fold is wrong"
              Expect.equal (Dag.replayFrom logWitness cp dag m) (Error(Dag.CheckpointFault.Uncovered c)) "refused" ]

// Phase 335 — the operation-coverage clause found `Dag.tryToJsonlWithCheckpoints` published with no
// test: its doc promises `toJsonlWithCheckpoints`'s bytes on `Ok` and a refusal by SIDECAR line, and
// nothing held either. These two cases are the stand-in its coverage entry names.
[<Tests>]
let checkedWriterTests =
    testList
        "Dag checkpoints — the checked writer (Phase 335)"
        [ testCase "on Ok both texts are toJsonlWithCheckpoints's, byte for byte"
          <| fun _ ->
              let _, _, _, m, y, dag = forkMergeTail ()
              let cp = Dag.checkpointAt h enc sw 0 dag m |> ok
              let cpY = Dag.checkpointAt h enc sw 0 dag y |> ok

              for cps in [ []; [ cp ]; [ cp; cpY ] ] do
                  Expect.equal
                      (Dag.tryToJsonlWithCheckpoints encode enc dag cps)
                      (Ok(Dag.toJsonlWithCheckpoints encode enc dag cps))
                      (sprintf "%d checkpoint(s)" (List.length cps))

          testCase "a refused state is the Error by its 1-based sidecar line, member state"
          <| fun _ ->
              let _, _, _, m, y, dag = forkMergeTail ()
              let cp = Dag.checkpointAt h enc sw 0 dag m |> ok
              let cpY = Dag.checkpointAt h enc sw 0 dag y |> ok
              Expect.notEqual cp.State cpY.State "the two checkpoints hold different states"
              // Only the SECOND checkpoint's state encodes across a line break.
              let split (s: int) = if s = cpY.State then "1\n2" else enc s

              match Dag.tryToJsonlWithCheckpoints encode split dag [ cp; cpY ] with
              | Error f ->
                  Expect.equal f.Line 2 "the second sidecar line"
                  Expect.equal f.Member "state" "the state member"
              | Ok v -> failtestf "accepted a state with a line break: %A" v ]
