module Fuaran.Core.Tests.StateAwareWitnessTests

// Phase 643 — the guarded witness gains a state-aware form. The reference domain is the note list
// of `AiSurfaceTests` (string ids, the same state), driven by operations whose effects depend on the
// state they are applied to: `Drop id` removes the note when it is there and does nothing when it is
// not, so it invokes the deleting capability in some states and nothing in others.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.AiSurfaceTests

// ---- the reference domain: the note list, with operations whose effects depend on the state ----

/// An operation on the note list that applies in every state: `Put` writes a note, replacing one of
/// the same id; `Drop` removes a note and is a no-op for an id the list does not hold.
type NoteStep =
    | Put of id: string * text: string
    | Drop of id: string

let private holds (id: string) (s: NoteState) =
    s.Notes |> List.exists (fun (k, _) -> k = id)

let private applyStep (op: NoteStep) (s: NoteState) : Result<NoteState, NoteRej> =
    match op with
    | Put(id, text) ->
        Ok
            { Notes =
                if holds id s then
                    s.Notes |> List.map (fun (k, v) -> if k = id then k, text else k, v)
                else
                    s.Notes @ [ id, text ] }
    | Drop id -> Ok { Notes = s.Notes |> List.filter (fun (k, _) -> k <> id) }

/// The capabilities the steps invoke, in the states they invoke them: a `Put` stores a new note and
/// overwrites a held one; a `Drop` deletes a held note and invokes nothing for a missing one.
let private effectsAt (op: NoteStep) (s: NoteState) : (string * (string * string) list) list =
    match op with
    | Put(id, _) when holds id s -> [ "overwrite-note", [ "id", id ] ]
    | Put(id, _) -> [ "store-note", [ "id", id ] ]
    | Drop id when holds id s -> [ "delete-note", [ "id", id ] ]
    | Drop _ -> []

let private noteCap (id: string) : Capability =
    Capability.create
        id
        { Name = id
          Holes =
            [ { Addr = "id"
                Name = "id"
                Kind = ValueHole AnyString
                Required = true } ]
          Effect = Effect.pureDeterministic }
        Server
        (Returns.Value AnyString)

/// The registry: storing is allowed; overwriting and deleting wait on an approval.
let private registry: CapabilityRegistry<unit> =
    [ noteCap "store-note"; noteCap "overwrite-note"; noteCap "delete-note" ]
    |> List.fold (fun r c -> CapabilityRegistry.register c r |> Result.defaultValue r) CapabilityRegistry.empty
    |> CapabilityRegistry.withGate
        { Policy = "notes"
          Decide =
            fun () c _ ->
                match c.Id with
                | "store-note" -> PolicyDecision.Allow
                | _ -> PolicyDecision.NeedsApproval }

let private surface: AiSurfaceWitness<NoteState, NoteStep, NoteRej> =
    { ReadTools = witness.ReadTools
      OpKinds = []
      KindOfOp =
        function
        | Put _ -> "put"
        | Drop _ -> "drop"
      Patterns = []
      Decide = fun _ _ -> PolicyDecision.Allow
      Apply = applyStep
      Explain = witness.Explain }

/// The present witness can name an operation's invocations from the operation alone. These are the
/// only two honest choices it has for `Drop`: report the deletion always, or never.
let private alwaysReports: GuardedSurfaceWitness<NoteState, NoteStep, NoteRej> =
    { Surface = surface
      DryRun = applyStep
      EffectsOf =
        function
        | Put(id, _) -> [ "store-note", [ "id", id ] ]
        | Drop id -> [ "delete-note", [ "id", id ] ] }

let private neverReports: GuardedSurfaceWitness<NoteState, NoteStep, NoteRej> =
    { alwaysReports with
        EffectsOf =
            function
            | Put(id, _) -> [ "store-note", [ "id", id ] ]
            | Drop _ -> [] }


/// The state-aware form: what `Drop` invokes is read off the state it would be applied to.
let private stateAware: StateAwareGuardedWitness<NoteState, NoteStep, NoteRej> =
    { Surface = surface
      DryRun = applyStep
      EffectsOf = effectsAt }

let private submitPresent gw (ops: NoteStep list) =
    Proposals.submitGuarded gw registry () "agent" "t0" None ops Proposals.Queue.empty state0

let private submitIn (ops: NoteStep list) (state: NoteState) =
    Proposals.submitGuardedIn stateAware registry () "agent" "t0" None ops Proposals.Queue.empty state

let private applied outcome =
    match outcome with
    | Proposals.SubmitApplied _ -> true
    | _ -> false

let private parked outcome =
    match outcome with
    | Proposals.SubmitProposed _ -> true
    | _ -> false

/// A sequence parked over the state-aware form at `state0`, and its id.
let private parkedIn (ops: NoteStep list) =
    match submitIn ops state0 with
    | Proposals.SubmitProposed(q, id) -> q, id
    | other -> failtestf "expected the sequence parked, got %A" other

[<Tests>]
let tests =
    testList
        "Phase 643 — the state-aware guarded witness"
        [ // ---- t4: the laws, in the conformance package, over its own note-list reference domain ----
          testCase "the six Phase 643 laws of policyLaws are green, each on evidence"
          <| fun () ->
              let results = Conformance.policyLaws 4242 200

              let ours =
                  results
                  |> List.filter (fun r ->
                      [ "StateAwareGuardedWitness.ofGuarded"
                        "submitGuardedIn"
                        "nothing is applied"
                        "approveGuardedIn by the proposal's author"
                        "StaleEffects"
                        "approveGuardedIn answers the grant" ]
                      |> List.exists r.Law.Contains)

              Expect.equal ours.Length 6 (sprintf "the six laws, found %A" (ours |> List.map _.Law))

              for r in ours do
                  Expect.isTrue r.Passed (sprintf "%s — %A" r.Law r.Counterexample)
                  Expect.isGreaterThan r.Evidence 0 (sprintf "%s stood on no evidence" r.Law)

          // ---- t1: the two ways the present witness gets a state-dependent op wrong, fixed ----
          testCase "a drop of a missing note invokes nothing and is applied, not parked"
          <| fun () ->
              // `state0` holds `n1` only, so dropping `n9` deletes nothing.
              let outcome = submitIn [ Drop "n9" ] state0
              Expect.isTrue (applied outcome) (sprintf "a harmless drop was parked: %A" outcome)

              Expect.isTrue
                  (parked (submitPresent alwaysReports [ Drop "n9" ]))
                  "the present witness that always reports the deletion parks it, which is why the form exists"

          testCase "a drop of a held note deletes it and is parked, not applied"
          <| fun () ->
              let outcome = submitIn [ Drop "n1" ] state0
              Expect.isTrue (parked outcome) (sprintf "a deletion was applied without approval: %A" outcome)

              Expect.isTrue
                  (applied (submitPresent neverReports [ Drop "n1" ]))
                  "the present witness that never reports the deletion applies it, which is why the form exists"

          // ---- t3: decided whole, along the dry run ----
          testCase "each op is decided at the state the earlier ones leave"
          <| fun () ->
              // `n2` is not held at `state0`: the put stores it, and the drop then deletes it.
              match submitIn [ Put("n2", "new"); Drop "n2" ] state0 with
              | Proposals.SubmitProposed(q, id) ->
                  let p = q.Proposals |> List.find (fun p -> p.Id = id)

                  Expect.equal
                      (p.Ops |> List.map _.Invocations)
                      [ [ "store-note", [ "id", "n2" ] ]; [ "delete-note", [ "id", "n2" ] ] ]
                      "the drop is staged with the deletion it makes after the put"
              | other -> failtestf "expected the sequence parked whole, got %A" other

              Expect.isTrue
                  (applied (submitIn [ Drop "n2"; Put("n2", "new") ] state0))
                  "in the other order the drop invokes nothing and the put only stores"

          testCase "nothing is applied when any invocation is denied"
          <| fun () ->
              let applies = ResizeArray<NoteStep>()

              let counting =
                  { stateAware with
                      Surface =
                          { surface with
                              Apply =
                                  fun op s ->
                                      applies.Add op
                                      applyStep op s } }

              // `wipe` is registered nowhere, so an op invoking it is denied naming what is.
              let denying =
                  { counting with
                      EffectsOf =
                          fun op s ->
                              match op with
                              | Drop "n1" -> [ "wipe", [ "id", "n1" ] ]
                              | _ -> effectsAt op s }

              match
                  Proposals.submitGuardedIn
                      denying
                      registry
                      ()
                      "agent"
                      "t0"
                      None
                      [ Put("n2", "x"); Drop "n1" ]
                      Proposals.Queue.empty
                      state0
              with
              | Proposals.SubmitDenied g ->
                  Expect.stringContains g.Message "wipe" "the refusal names the unregistered capability"
              | other -> failtestf "expected the sequence denied whole, got %A" other

              Expect.isEmpty applies "no op was applied"

          testCase "an op that would not apply refuses the sequence before it is parked"
          <| fun () ->
              let refusing =
                  { stateAware with
                      DryRun =
                          fun op s ->
                              match op with
                              | Drop "n1" -> Error(NoSuchNote("n1", []))
                              | _ -> applyStep op s }

              match
                  Proposals.submitGuardedIn
                      refusing
                      registry
                      ()
                      "agent"
                      "t0"
                      None
                      [ Put("n1", "x"); Drop "n1" ]
                      Proposals.Queue.empty
                      state0
              with
              | Proposals.SubmitOpRejected(NoSuchNote("n1", _)) -> ()
              | other -> failtestf "expected the dry run's rejection, got %A" other

          // ---- t3: approval ----
          testCase "an approval presents a grant for each invocation it covers, through the gate"
          <| fun () ->
              let q, id = parkedIn [ Drop "n1" ]

              match Proposals.approveGuardedIn stateAware registry () "approver" "t1" id q state0 with
              | Ok(q', next, grants) ->
                  Expect.isFalse (holds "n1" next) "approved: the note is gone"

                  Expect.equal
                      grants
                      [ Capability.grant (noteCap "delete-note") [ "id", "n1" ] ]
                      "one grant, for exactly the deletion the approval covered"

                  Expect.equal
                      (q'.Proposals |> List.map _.Status)
                      [ Proposals.ProposalStatus.Approved("approver", "t1") ]
                      "the proposal is approved"

                  // The host dispatches the deletion with its grant, and the gate runs it; without
                  // the grant the gate still asks for an approval.
                  let ran = ResizeArray<string>()

                  let dispatch grant =
                      CapabilityRegistry.dispatch registry () grant "delete-note" [ "id", "n1" ] (fun c () ->
                          ran.Add c.Id
                          Ready "n1")

                  Expect.equal (dispatch (List.tryHead grants)) (Ok(Ready "n1")) "dispatched with its grant"
                  Expect.equal (dispatch None) (Error(ApprovalRequired "notes")) "without it, parked"
                  Expect.equal (List.ofSeq ran) [ "delete-note" ] "the body ran once, with the grant"
              | Error e -> failtestf "the approval was refused: %A" e

          testCase "an approval by the author is refused"
          <| fun () ->
              let q, id = parkedIn [ Drop "n1" ]

              match Proposals.approveGuardedIn stateAware registry () "agent" "t1" id q state0 with
              | Error(Proposals.StagedApprovalFailure.Refused(Proposals.SelfApproval(pid, "agent"))) ->
                  Expect.equal pid id "the proposal named"
              | other -> failtestf "expected SelfApproval, got %A" other

          testCase "a sequence whose effects changed since submission is refused as stale"
          <| fun () ->
              // Parked at `state0` (the drop needs an approval), where `n2` is not held: the put would
              // store it. By approval `n2` is held, and the same put would overwrite it — an
              // invocation nobody was shown.
              let q, id = parkedIn [ Put("n2", "x"); Drop "n1" ]
              let moved = { Notes = state0.Notes @ [ "n2", "other" ] }

              match Proposals.approveGuardedIn stateAware registry () "approver" "t1" id q moved with
              | Error(Proposals.StagedApprovalFailure.StaleEffects(pid, 0, submitted, now)) ->
                  Expect.equal pid id "the proposal named"
                  Expect.equal submitted [ "store-note", [ "id", "n2" ] ] "what the approver was shown"
                  Expect.equal now [ "overwrite-note", [ "id", "n2" ] ] "what it would invoke now"
              | other -> failtestf "expected StaleEffects, got %A" other

              Expect.equal
                  (Proposals.Queue.pending q |> List.map _.Id)
                  [ id ]
                  "the refused proposal is still pending in the queue the host holds"

              Expect.isOk
                  (Proposals.approveGuardedIn stateAware registry () "approver" "t1" id q state0)
                  "at a state where the effects are the ones submitted, the same approval goes through"

          testCase "a deny for the approver is lifted by no grant"
          <| fun () ->
              let q, id = parkedIn [ Drop "n1" ]

              let strict =
                  registry
                  |> CapabilityRegistry.withGate
                      { Policy = "strict"
                        Decide =
                          fun () c _ ->
                              if c.Id = "delete-note" then
                                  PolicyDecision.deny "never"
                              else
                                  PolicyDecision.Allow }

              match Proposals.approveGuardedIn stateAware strict () "approver" "t1" id q state0 with
              | Error(Proposals.StagedApprovalFailure.Refused(Proposals.ApprovalDenied(_, g))) ->
                  Expect.equal g.Message "never" "the gate's denial"
              | other -> failtestf "expected ApprovalDenied, got %A" other

          // ---- the lift ----
          testCase "over the lift a domain on the present witness decides exactly as before"
          <| fun () ->
              let lifted = StateAwareGuardedWitness.ofGuarded alwaysReports
              let held = { Notes = state0.Notes @ [ "n2", "b" ] }

              for op in [ Put("n1", "x"); Put("n9", "y"); Drop "n1"; Drop "n9" ] do
                  for s in [ state0; held ] do
                      Expect.equal
                          (Proposals.decideGuardedIn lifted registry () "agent" op s)
                          (Proposals.decideGuarded alwaysReports registry () "agent" op)
                          (sprintf "%A at %A" op s)

              let ops = [ Put("n9", "y"); Drop "n1" ]

              match
                  Proposals.submitGuardedIn lifted registry () "agent" "t0" None ops Proposals.Queue.empty state0,
                  submitPresent alwaysReports ops
              with
              | Proposals.SubmitProposed(q, id), Proposals.SubmitProposed(q0, id0) ->
                  Expect.equal id id0 "the same id"

                  Expect.equal
                      (q.Proposals |> List.collect (fun p -> p.Ops |> List.map _.Op))
                      (q0.Proposals |> List.collect _.Ops)
                      "the same ops parked"
              | a, b -> failtestf "the two forms disagree: %A / %A" a b

          // ---- t6: the frozen witnesses stay at the unit context; `under` is the route ----
          testCase "a registry that reads a context is certified by the guarded family once per context, through under"
          <| fun () ->
              // The note surface's capabilities, in ONE registry whose gate reads a context: the
              // trusted side may do anything, the other needs an approval to delete.
              let byContext: CapabilityRegistry<bool> =
                  CapabilityRegistry.enumerate PolicyGateTests.noteRegistry
                  |> List.fold
                      (fun r c -> CapabilityRegistry.register c r |> Result.defaultValue r)
                      CapabilityRegistry.empty
                  |> CapabilityRegistry.withGate
                      { Policy = "side"
                        Decide =
                          fun trusted c _ ->
                              if trusted || c.Id <> "delete-note" then
                                  PolicyDecision.Allow
                              else
                                  PolicyDecision.NeedsApproval }

              for ctx in [ true; false ] do
                  let results =
                      Conformance.policyLawsAt
                          PolicyGateTests.guarded
                          (CapabilityRegistry.under ctx byContext)
                          state0
                          genNoteOp
                          PolicyGateTests.actors
                          PolicyGateTests.privileged
                          4242
                          300

                  for r in results do
                      Expect.isTrue r.Passed (sprintf "under %b: %s — %A" ctx r.Law r.Counterexample)

          testCase "the capability seam family certifies a context only where its gate admits every drawn call"
          <| fun () ->
              // The finding t6 recorded (DECISIONS.md D254.6): `capabilityLawsAt` takes a call as
              // admitted when its id is registered and its arguments validate, and reads no gate. So
              // `under` certifies a context whose gate admits every drawn call, and a context whose
              // gate parks one fails the agreement law although host and registry agree. This pins
              // that, so the change that makes the family read the gate turns it red here.
              let byContext: CapabilityRegistry<bool> =
                  CapabilityRegistry.enumerate WitnessTakingFamiliesTests.capabilityRegistry
                  |> List.fold
                      (fun r c -> CapabilityRegistry.register c r |> Result.defaultValue r)
                      CapabilityRegistry.empty
                  |> CapabilityRegistry.withGate
                      { Policy = "side"
                        Decide =
                          fun trusted c _ ->
                              if trusted || c.Id <> "flaky" then
                                  PolicyDecision.Allow
                              else
                                  PolicyDecision.NeedsApproval }

              let run (ctx: bool) =
                  Conformance.capabilityLawsAt
                      { Registry = CapabilityRegistry.under ctx byContext
                        Dispatch = CapabilityRegistry.dispatch byContext ctx None
                        GenCall = WitnessTakingFamiliesTests.genCapabilityCall }
                      WitnessTakingFamiliesTests.capabilityBody
                      2460
                      300

              for r in run true do
                  Expect.isTrue r.Passed (sprintf "under the admitting context: %s — %A" r.Law r.Counterexample)

              Expect.exists
                  (run false)
                  (fun r -> not r.Passed && r.Law.Contains "agrees with its registry")
                  "under the parking context the family reads no gate, and its agreement law fails" ]
