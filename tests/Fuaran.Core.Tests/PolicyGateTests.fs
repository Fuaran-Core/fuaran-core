module Fuaran.Core.Tests.PolicyGateTests

// Phase 318 — the policy gate on the invocable registries, the write gate over an op footprint,
// the guarded AI surface, and the keyed capture journal: the four law families, and the fixtures
// that pin what each promise means on cases a reader can check by eye.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.AiSurfaceTests

let private allPass (label: string) (results: LawResult list) =
    for r in results do
        Expect.isTrue r.Passed (sprintf "%s: %s — %A" label r.Law r.Counterexample)

// ---- fixtures ----------------------------------------------------------------------------------

let private noteCap (id: string) (host: HostEffect) : Capability =
    Capability.create
        id
        { Name = id
          Holes =
            [ { Addr = "id"
                Name = "id"
                Kind = ValueHole AnyString
                Required = true } ]
          Effect =
            { Effect.pureDeterministic with
                Host = host } }
        Server

/// The capability registry the note surface's ops invoke: storing and deleting a note both write
/// to the host; `count-notes` only reads.
let noteRegistry: CapabilityRegistry =
    [ noteCap "store-note" WritesHost
      noteCap "delete-note" WritesHost
      noteCap "count-notes" ReadsHost ]
    |> List.fold (fun r c -> CapabilityRegistry.register c r |> Result.defaultValue r) CapabilityRegistry.empty

/// The ops' effects: an add stores, a remove deletes.
let private effectsOf (op: NoteOp) : (string * (string * string) list) list =
    match op with
    | AddNote(id, _) -> [ "store-note", [ "id", id ] ]
    | RemoveNote id -> [ "delete-note", [ "id", id ] ]

/// A domain policy that keeps the no-unapproved-write law: only `admin` may write without an
/// approval.
let private writePolicy (actor: string) (_: NoteOp) : PolicyDecision =
    if actor = "admin" then
        PolicyDecision.Allow
    else
        PolicyDecision.NeedsApproval

/// The guarded note surface: the note witness under `writePolicy`, its reducer as the dry run.
let guarded: GuardedSurfaceWitness<NoteState, NoteOp, NoteRej> =
    { Surface = { witness with Decide = writePolicy }
      DryRun = witness.Apply
      EffectsOf = effectsOf }

let actors = [ Human "admin"; Agent("model", "1", "agent"); Human "intern" ]

let privileged (actor: Actor) = Actor.id actor = "admin"

[<Tests>]
let tests =
    testList
        "Policy gate (Phase 318)"
        [ testCase "the four law families are green at the kit's fixtures and the reference domain"
          <| fun _ ->
              allPass "policyLaws" (Conformance.policyLaws 4242 200)
              allPass "keyedCaptureLaws" (Conformance.keyedCaptureLaws 4242 200)
              allPass "writeGateLaws" (Conformance.writeGateLaws nodew idw ConformanceTests.opGen 4242 300)

              allPass
                  "policyLawsAt"
                  (Conformance.policyLawsAt guarded noteRegistry state0 genNoteOp actors privileged 4242 300)

          testCase "a policy that allows an unprivileged write fails the no-unapproved-write law, by name"
          <| fun _ ->
              let permissive =
                  { guarded with
                      Surface =
                          { guarded.Surface with
                              Decide = fun _ _ -> PolicyDecision.Allow } }

              let results =
                  Conformance.policyLawsAt permissive noteRegistry state0 genNoteOp actors privileged 4242 300

              let failed = results |> List.filter (fun r -> not r.Passed) |> List.map _.Law

              Expect.equal failed.Length 1 "exactly one law reddens"
              Expect.stringContains failed.Head "host-writing capability" "the no-unapproved-write law"

          testCase "the join is the most restrictive decision, and the first denial's guidance is the one kept"
          <| fun _ ->
              let first = PolicyDecision.deny "first"
              let second = PolicyDecision.denyWith "second" [ "x" ]
              Expect.equal (PolicyDecision.join first second) first "a tie keeps the left denial"
              Expect.equal (PolicyDecision.join PolicyDecision.NeedsApproval second) second "Deny absorbs"

              Expect.equal
                  (PolicyDecision.all [ PolicyDecision.Allow; PolicyDecision.NeedsApproval; first; second ])
                  first
                  "all: the first denial"

              Expect.equal
                  (PolicyDecision.any [ first; PolicyDecision.NeedsApproval; PolicyDecision.Allow ])
                  PolicyDecision.Allow
                  "any: one permitting policy suffices"

              Expect.equal (PolicyDecision.rank (PolicyDecision.any [])) 2 "the empty disjunction denies"

          testCase "a denied dispatch runs no body, names its gate, and reaches the observer once"
          <| fun _ ->
              let seen = ResizeArray<PolicyDenial<(string * string) list>>()
              let mutable ran = false

              let reg =
                  noteRegistry
                  |> CapabilityRegistry.withGate
                      { Policy = "read-only-session"
                        Decide =
                          fun c _ ->
                              if c.Signature.Effect.Host = WritesHost then
                                  PolicyDecision.denyWith "this session may only read" [ "count-notes" ]
                              else
                                  PolicyDecision.Allow }
                  |> CapabilityRegistry.onDenied seen.Add

              let out =
                  CapabilityRegistry.dispatch reg "store-note" [ "id", "n9" ] (fun _ () ->
                      ran <- true
                      Ready())

              Expect.equal
                  out
                  (Error(PolicyRefused("read-only-session", "this session may only read", [ "count-notes" ])))
                  "PolicyRefused naming the gate and what it allows"

              Expect.isFalse ran "no body ran"
              Expect.equal seen.Count 1 "the observer was told once"
              Expect.equal seen[0].Id "store-note" "of the invocation it refused"

              Expect.equal
                  (InvokeError.describe (
                      match out with
                      | Error e -> e
                      | Ok _ -> BodyFailed "unreachable"
                  ))
                  "Refused: the policy 'read-only-session' does not allow this: this session may only read. What it allows instead: 'count-notes'."
                  "the sentence a model reads"

              Expect.equal
                  (CapabilityRegistry.dispatch reg "count-notes" [ "id", "n1" ] (fun _ () -> Ready 1))
                  (Ok(Ready 1))
                  "a read passes the same gate"

          testCase "a gated registry still equals itself after register then unregister"
          <| fun _ ->
              let reg =
                  noteRegistry
                  |> CapabilityRegistry.withGate
                      { Policy = "p"
                        Decide = fun _ _ -> PolicyDecision.Allow }

              Expect.equal
                  (CapabilityRegistry.register (noteCap "extra" Pure) reg
                   |> Result.bind (CapabilityRegistry.unregister "extra"))
                  (Ok reg)
                  "the policy is carried through"

              Expect.notEqual reg noteRegistry "a gate is part of the registry's identity"

          testCase "submitGuarded dry-runs the whole sequence before the first Apply"
          <| fun _ ->
              let applied = ResizeArray<NoteOp>()

              let counting =
                  { guarded with
                      Surface =
                          { guarded.Surface with
                              Apply =
                                  fun op s ->
                                      applied.Add op
                                      witness.Apply op s } }

              match
                  Proposals.submitGuarded
                      counting
                      noteRegistry
                      "admin"
                      "t0"
                      None
                      [ AddNote("n2", "b"); RemoveNote "missing" ]
                      Proposals.Queue.empty
                      state0
              with
              | Proposals.SubmitOpRejected(NoSuchNote("missing", _)) ->
                  Expect.isEmpty applied "no Apply ran for a sequence a later op refuses"
              | other -> failtestf "expected the dry run to refuse, got %A" other

              // the plain submit, by contrast, folds Apply op by op
              match
                  Proposals.submit
                      counting.Surface
                      "admin"
                      "t0"
                      None
                      [ AddNote("n2", "b"); RemoveNote "missing" ]
                      Proposals.Queue.empty
                      state0
              with
              | Proposals.SubmitOpRejected _ ->
                  Expect.equal applied.Count 2 "submit reached the refusing op by applying"
              | other -> failtestf "expected a rejection, got %A" other

          testCase "submitGuarded refuses an op invoking an unregistered capability, naming the registered ones"
          <| fun _ ->
              let stray =
                  { guarded with
                      EffectsOf = fun _ -> [ "publish-note", [ "id", "n1" ] ] }

              match
                  Proposals.submitGuarded
                      stray
                      noteRegistry
                      "admin"
                      "t0"
                      None
                      [ AddNote("n2", "b") ]
                      Proposals.Queue.empty
                      state0
              with
              | Proposals.SubmitDenied g ->
                  Expect.stringContains g.Message "publish-note" "names the stray capability"
                  Expect.equal g.Alternatives [ "count-notes"; "delete-note"; "store-note" ] "and the registered ones"
              | other -> failtestf "expected a denial, got %A" other

          testCase "an unprivileged author's write parks; approveGuarded applies it for a permitted approver"
          <| fun _ ->
              match
                  Proposals.submitGuarded
                      guarded
                      noteRegistry
                      "agent"
                      "t0"
                      None
                      [ AddNote("n2", "b") ]
                      Proposals.Queue.empty
                      state0
              with
              | Proposals.SubmitProposed(q, id) ->
                  let readOnly =
                      noteRegistry
                      |> CapabilityRegistry.withGate
                          { Policy = "read-only"
                            Decide = fun _ _ -> PolicyDecision.deny "read only" }

                  match Proposals.approveGuarded guarded readOnly "admin" "t1" id q state0 with
                  | Error(Proposals.ApprovalDenied(_, g)) -> Expect.equal g.Message "read only" "the approver's gate"
                  | other -> failtestf "expected the approver's registry to deny, got %A" other

                  match Proposals.approveGuarded guarded noteRegistry "admin" "t1" id q state0 with
                  | Ok(_, s) -> Expect.equal s.Notes [ "n1", "hello"; "n2", "b" ] "applied"
                  | Error e -> failtestf "expected approval, got %A" e
              | other -> failtestf "expected a parked proposal, got %A" other

          testCase "the write gate decides from the footprint, with subtree locking"
          <| fun _ ->
              // doc
              //  ├─ boiler (para)
              //  └─ body (section)
              //      └─ p1 (para)
              let tree =
                  RNode.node
                      "doc"
                      "doc"
                      [ RNode.leaf "boiler" "para" "v"
                        RNode.node "body" "section" [ RNode.leaf "p1" "para" "v" ] ]

              let decide gate op = WriteGate.decide gate nodew idw op tree

              let edit id = UpdateNode(RNode.leaf id "para" "w")

              // deny-list: the locked block, and everything under a locked section
              Expect.equal
                  (decide (WriteGate.lockOnly [ "boiler" ]) (edit "boiler"))
                  (Error(WriteDenial.Locked("boiler", "boiler")))
                  "a locked block"

              Expect.equal
                  (decide (WriteGate.lockOnly [ "body" ]) (edit "p1"))
                  (Error(WriteDenial.Locked("p1", "body")))
                  "a block inside a locked section"

              Expect.equal (decide (WriteGate.lockOnly [ "boiler" ]) (edit "p1")) (Ok()) "an unlocked block"

              // allow-list: a section's body is open, and so is a block inserted into it
              let open' = WriteGate.allowOnly [ "body" ]
              Expect.equal (decide open' (edit "p1")) (Ok()) "inside the allowed section"

              Expect.equal
                  (decide open' (InsertChild("body", RNode.leaf "p2" "para" "v")))
                  (Ok())
                  "a block inserted into the allowed section"

              Expect.equal
                  (decide open' (edit "boiler"))
                  (Error(WriteDenial.NotWritable("boiler", [ "body" ])))
                  "outside it, naming the allow-list"

              Expect.equal
                  (decide open' (MoveNode("p1", "doc")))
                  (Error(WriteDenial.NotWritable("doc", [ "body" ])))
                  "a move out of it writes the destination"

              // a removal destroys the subtree: removing the section of a locked block is refused
              Expect.equal
                  (decide (WriteGate.lockOnly [ "p1" ]) (RemoveNode "body"))
                  (Error(WriteDenial.Locked("p1", "p1")))
                  "a lock protects a block from its ancestor's removal"

              Expect.equal
                  (WriteGate.applyGated (WriteGate.lockOnly [ "boiler" ]) nodew idw (edit "boiler") tree)
                  (Error(GatedApplyFailure.Denied(WriteDenial.Locked("boiler", "boiler"))))
                  "applyGated refuses before the reducer"

              Expect.equal
                  (WriteGate.targetsOf nodew idw (RemoveNode "body") tree)
                  (Set.ofList [ "body"; "p1" ])
                  "a removal's targets are its destroyed subtree"

          testCase "a paged network query captures each page under its own key and replays it exactly"
          <| fun _ ->
              let q: Query =
                  { Id = "feed"
                    Params = []
                    ResultSchema = [ Field.create "n" IntType ]
                    Effect =
                      { Effect.pureDeterministic with
                          Determinism = Effect.network }
                    Source = Ref "feed"
                    TimeoutMs = None
                    PageSize = Some 1
                    Where = []
                    OrderBy = [] }

              let reg =
                  QueryRegistry.register q QueryRegistry.empty
                  |> Result.defaultValue QueryRegistry.empty

              let page (n: int) : QueryResult =
                  { Rows = { Schema = []; Columns = [] }
                    PageNum = n
                    TotalRowCount = None
                    NextPageToken = Some(string (n + 1)) }

              let hashFn = OpStream.defaultHash
              let enc = QueryCodec.encodeResult

              let c1 =
                  QueryRegistry.dispatchPageCaptured hashFn enc reg "feed" [] None (fun _ _ -> Ready(page 1)) []

              let c2 =
                  QueryRegistry.dispatchPageCaptured
                      hashFn
                      enc
                      reg
                      "feed"
                      []
                      (Some "2")
                      (fun _ _ -> Ready(page 2))
                      c1.Journal

              let j2 = c2.Journal

              Expect.equal (c1.Outcome, c2.Outcome) (Ok(Ready(page 1)), Ok(Ready(page 2))) "live pages"
              Expect.isTrue (OpStream.verifyKeyedCaptures hashFn j2) "the journal verifies"

              // replay page two FIRST: keyed, not positional
              let live _ _ = Ready(page 99)

              let second =
                  QueryRegistry.dispatchReplayed QueryCodec.decodeResult reg "feed" [] (Some "2") live Map.empty j2

              Expect.equal second.Outcome (Ok(Ready(page 2))) "page two replays from its own capture"

              let first =
                  QueryRegistry.dispatchReplayed QueryCodec.decodeResult reg "feed" [] None live second.Cursor j2

              Expect.equal first.Outcome (Ok(Ready(page 1))) "and page one from its own"

              let never =
                  QueryRegistry.dispatchReplayed QueryCodec.decodeResult reg "feed" [] (Some "3") live Map.empty j2

              Expect.equal
                  never.Outcome
                  (Error(ReplayFailure.Unanswered(KeyedCaptureFault.NoCapture(Query.invocationKeyPage q [] (Some "3")))))
                  "a page never captured refuses rather than fetching"

              Expect.equal never.Cursor Map.empty "and consumes nothing"

          testCase "a pending invocation leaves its attempt open, and settles later"
          <| fun _ ->
              let hashFn = OpStream.defaultHash
              let enc (v: int) = string v

              let captured =
                  OpStream.captureEffectKeyed hashFn enc "network" "k" (fun () -> None) []

              let occ, j = captured.Occurrence, captured.Journal

              Expect.equal (captured.Answer, occ, List.length j) (None, 0, 1) "only the attempt is journalled"
              Expect.isTrue (OpStream.verifyKeyedCaptures hashFn j) "an open attempt verifies"

              match OpStream.settleEffectKeyed hashFn enc "k" occ (Error "timed out") j with
              | Ok settled ->
                  Expect.isTrue (OpStream.verifyKeyedCaptures hashFn settled) "the settled journal verifies"

                  Expect.equal
                      (OpStream.replayEffectKeyed
                          (fun s -> Ok(int s))
                          "network"
                          "k"
                          (fun () -> Some(Ok 0))
                          Map.empty
                          settled)
                      (Ok(Some(Error "timed out"), Map.ofList [ "k", 1 ]))
                      "the failure replays as the same failure"
              | Error f -> failtestf "settle refused: %A" f ]
