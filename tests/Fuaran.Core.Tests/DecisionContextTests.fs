module Fuaran.Core.Tests.DecisionContextTests

// Phase 638 — the registry's gate takes a decision context, and a dispatch may present a grant.
// These are the phase's laws on cases a reader can check by eye. None of them could be written
// before the phase: a gate was a function of the declaration and the arguments, a registry had no
// context to decide under, and a dispatch had nothing to present. The sampled forms are
// `Conformance.policyLaws`' two Phase 638 laws; the proved forms are in `proofs/Capability.fst`.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.AiSurfaceTests

// ---- fixtures ----------------------------------------------------------------------------------

/// The decision context of these fixtures: one fact a host would put there. Core names neither
/// case; the gate below is the only thing that reads it.
type private Side =
    | Local
    | Remote

let private cap (id: string) : Capability =
    Capability.create
        id
        { Name = id
          Holes =
            [ { Addr = "n"
                Name = "n"
                Kind = ValueHole(IntRange(0, 99))
                Required = true } ]
          Effect = Effect.pureDeterministic }
        Server
        (Returns.Value(IntRange(0, 99)))

/// One gate: the local side may do anything; the remote side may read, needs an approval to reset,
/// and may never purge.
let private hazard: PolicyGate<Side, Capability, (string * string) list> =
    { Policy = "hazard"
      Decide =
        fun side c _ ->
            match side, c.Id with
            | Local, _ -> PolicyDecision.Allow
            | Remote, "reset" -> PolicyDecision.NeedsApproval
            | Remote, "purge" -> PolicyDecision.deny "the remote side never purges"
            | Remote, _ -> PolicyDecision.Allow }

/// ONE registry. Every test below asks it; none builds a second.
let private registry: CapabilityRegistry<Side> =
    [ cap "read"; cap "reset"; cap "purge" ]
    |> List.fold (fun r c -> CapabilityRegistry.register c r |> Result.defaultValue r) CapabilityRegistry.empty
    |> CapabilityRegistry.withGate hazard

let private args = [ "n", "5" ]

/// A body that records that it ran.
let private performer (ran: ResizeArray<string>) (c: Capability) () : Deferred<int> =
    ran.Add c.Id
    Ready 1

let private dispatch (side: Side) (grant: Grant option) (id: string) (a: (string * string) list) =
    let ran = ResizeArray<string>()
    CapabilityRegistry.dispatch registry side grant id a (performer ran), List.ofSeq ran

[<Tests>]
let tests =
    testList
        "Phase 638 — the decision context and the grant"
        [ testCase "one registry decides differently under two contexts"
          <| fun () ->
              Expect.equal
                  (CapabilityRegistry.decide registry Local None "reset" args)
                  PolicyDecision.Allow
                  "the local side resets"

              Expect.equal
                  (CapabilityRegistry.decide registry Remote None "reset" args)
                  PolicyDecision.NeedsApproval
                  "the remote side needs an approval, through the same registry"

              Expect.equal (dispatch Local None "reset" args) (Ok(Ready 1), [ "reset" ]) "and the dispatch follows"

              Expect.equal
                  (dispatch Remote None "reset" args)
                  (Error(ApprovalRequired "hazard"), [])
                  "refused, naming the gate, and the body did not run"

          testCase "a needs-approval dispatches with the grant for exactly its invocation"
          <| fun () ->
              let grant = Capability.grant (cap "reset") args

              Expect.equal
                  (dispatch Remote (Some grant) "reset" args)
                  (Ok(Ready 1), [ "reset" ])
                  "the grant lifts it and the body runs"

              Expect.equal
                  (CapabilityRegistry.decide registry Remote (Some grant) "reset" args)
                  PolicyDecision.Allow
                  "and decide says so without dispatching"

              Expect.equal
                  (Capability.grant (cap "reset") [ "n", "05" ])
                  grant
                  "one value has one grant: 05 and 5 are one integer"

          testCase "with no grant, or a grant for anything else, it does not"
          <| fun () ->
              let refused = Error(ApprovalRequired "hazard"), []

              let others =
                  [ "other arguments", Capability.grant (cap "reset") [ "n", "6" ]
                    "another capability", Capability.grant (cap "read") args
                    "the hashed capture key", Grant.ofInvocation (Capability.invocationKey (cap "reset") args)
                    "an empty identity", Grant.ofInvocation "" ]

              Expect.equal (dispatch Remote None "reset" args) refused "no grant"

              for label, grant in others do
                  Expect.equal (dispatch Remote (Some grant) "reset" args) refused label

                  Expect.equal
                      (CapabilityRegistry.decide registry Remote (Some grant) "reset" args)
                      PolicyDecision.NeedsApproval
                      label

          testCase "a deny is lifted by no grant"
          <| fun () ->
              let own = Capability.grant (cap "purge") args
              let denied = Error(PolicyRefused("hazard", "the remote side never purges", [])), []

              Expect.equal (dispatch Remote None "purge" args) denied "denied with nothing presented"
              Expect.equal (dispatch Remote (Some own) "purge" args) denied "and with the grant for exactly it"

              Expect.equal
                  (CapabilityRegistry.decide registry Remote (Some own) "purge" args)
                  (PolicyDecision.deny "the remote side never purges")
                  "the decision is the gate's denial, unchanged"

          testCase "an allow is unchanged by any grant"
          <| fun () ->
              let stray = Capability.grant (cap "reset") args

              Expect.equal
                  (dispatch Remote (Some stray) "read" args)
                  (dispatch Remote None "read" args)
                  "a grant for another invocation changes nothing"

              Expect.equal
                  (dispatch Remote (Some(Capability.grant (cap "read") args)) "read" args)
                  (Ok(Ready 1), [ "read" ])
                  "and neither does its own"

          testCase "an unregistered id is refused whatever is presented"
          <| fun () ->
              let refused = Error(NoSuchCapability("erase", [ "purge"; "read"; "reset" ])), []

              for side in [ Local; Remote ] do
                  for grant in
                      [ None
                        Some(Capability.grant (cap "erase") args)
                        Some(Capability.grant (cap "reset") args) ] do
                      Expect.equal (dispatch side grant "erase" args) refused "default-deny by shape"

          testCase "the decision is a function of the declaration, the arguments, the context and the grant"
          <| fun () ->
              let grant = Some(Capability.grant (cap "reset") args)
              let seen = ResizeArray<Side * string * (string * string) list>()

              let recording =
                  registry
                  |> CapabilityRegistry.withGate
                      { Policy = "recording"
                        Decide =
                          fun side c shown ->
                              seen.Add(side, c.Id, shown)
                              PolicyDecision.Allow }

              let first = CapabilityRegistry.decide recording Remote grant "reset" args
              let second = CapabilityRegistry.decide recording Remote grant "reset" args
              Expect.equal first PolicyDecision.Allow "the grant lifts the one approval the first gate asks for"
              Expect.equal first second "the same asking, the same answer"

              Expect.equal
                  (List.ofSeq seen)
                  [ Remote, "reset", args; Remote, "reset", args ]
                  "and the gate was shown the context, the declaration and the arguments, and nothing else"

          testCase "a gate written without a context is lifted by one helper and decides as it did"
          <| fun () ->
              let old (c: Capability) (_: (string * string) list) =
                  if c.Id = "purge" then
                      PolicyDecision.deny "no purging"
                  else
                      PolicyDecision.Allow

              let lifted: CapabilityRegistry<Side> =
                  [ cap "read"; cap "purge" ]
                  |> List.fold
                      (fun r c -> CapabilityRegistry.register c r |> Result.defaultValue r)
                      CapabilityRegistry.empty
                  |> CapabilityRegistry.withGate (PolicyGate.ignoringContext "old" old)

              for side in [ Local; Remote ] do
                  for id in [ "read"; "purge" ] do
                      Expect.equal
                          (CapabilityRegistry.decide lifted side None id args)
                          (old (cap id) args)
                          "the lifted gate decides as the old one, under every context"

          testCase "a refusal reaches the observer with its context, and a lifted approval reaches none"
          <| fun () ->
              let told = ResizeArray<PolicyDenial<Side, (string * string) list>>()
              let observed = registry |> CapabilityRegistry.onDenied told.Add

              let run grant =
                  CapabilityRegistry.dispatch observed Remote grant "reset" args (fun _ () -> Ready 1)

              run (Some(Capability.grant (cap "reset") args)) |> ignore
              Expect.isEmpty told "a granted dispatch is not a refusal"
              run None |> ignore

              Expect.equal
                  (List.ofSeq told)
                  [ { Policy = "hazard"
                      Context = Remote
                      Id = "reset"
                      Args = args
                      Decision = PolicyDecision.NeedsApproval } ]
                  "the refusal names the gate and the context it decided under"

          testCase "under fixes a context: the registry one side sees"
          <| fun () ->
              let remote: CapabilityRegistry<unit> = CapabilityRegistry.under Remote registry

              for id in [ "read"; "reset"; "purge"; "erase" ] do
                  Expect.equal
                      (CapabilityRegistry.decide remote () None id args)
                      (CapabilityRegistry.decide registry Remote None id args)
                      id

          testCase "the function registry and a pipeline decide under the context and take the grant"
          <| fun () ->
              let functions: FunctionRegistry<Side> =
                  [ cap "read"; cap "reset" ]
                  |> List.fold
                      (fun r c -> FunctionRegistry.register (FunctionRegistry.entry c) r |> Result.defaultValue r)
                      FunctionRegistry.empty
                  |> FunctionRegistry.withGate hazard

              let grant = Capability.grant (cap "reset") args
              let body (_: FunctionEntry) () : Deferred<int> = Ready 1

              Expect.equal
                  (FunctionRegistry.dispatch functions Remote None "reset" args body)
                  (Error(ApprovalRequired "hazard"))
                  "the function registry parks it"

              Expect.equal
                  (FunctionRegistry.dispatch functions Remote (Some grant) "reset" args body)
                  (Ok(Ready 1))
                  "and the grant lifts it"

              let pipeline: CapabilityPipeline =
                  { Nodes = [ Invoke("r", "reset", IntRange(0, 99), [ "n", Literal "5" ]) ] }

              let eval side grants =
                  CapabilityPipeline.eval
                      (CapabilityLookup.ofFunctionRegistry functions)
                      side
                      grants
                      string
                      (fun _ _ -> Ok 1)
                      pipeline

              Expect.equal (eval Local []) (Ok(Map.ofList [ "r", 1 ])) "the local side evaluates it"

              Expect.equal
                  (eval Remote [])
                  (Error(PipelineEvalError.EvalPolicyRefused("r", ApprovalRequired "hazard")))
                  "the remote side is parked at the node"

              Expect.equal
                  (eval Remote [ Capability.grant (cap "read") args; grant ])
                  (Ok(Map.ofList [ "r", 1 ]))
                  "and runs it when one of the presented grants is for the node's invocation"

          testCase "an approval at the guarded surface presents a grant through the gate"
          <| fun () ->
              // The ops' effects: an add stores, a remove purges. Under the remote context the gate
              // asks for an approval to store and never lets a purge through.
              let noteCap (id: string) : Capability =
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

              let notes: CapabilityRegistry<Side> =
                  [ noteCap "store"; noteCap "purge" ]
                  |> List.fold
                      (fun r c -> CapabilityRegistry.register c r |> Result.defaultValue r)
                      CapabilityRegistry.empty
                  |> CapabilityRegistry.withGate
                      { Policy = "notes"
                        Decide =
                          fun side c _ ->
                              match side, c.Id with
                              | Local, _ -> PolicyDecision.Allow
                              | Remote, "store" -> PolicyDecision.NeedsApproval
                              | Remote, _ -> PolicyDecision.deny "the remote side never purges" }

              let guarded: GuardedSurfaceWitness<NoteState, NoteOp, NoteRej> =
                  { Surface =
                      { witness with
                          Decide = fun _ _ -> PolicyDecision.Allow }
                    DryRun = witness.Apply
                    EffectsOf =
                      function
                      | AddNote(id, _) -> [ "store", [ "id", id ] ]
                      | RemoveNote id -> [ "purge", [ "id", id ] ] }

              let add = AddNote("n2", "more")

              Expect.equal
                  (Proposals.decideGuarded guarded notes Remote "agent" add)
                  PolicyDecision.NeedsApproval
                  "the registry parks the op under the remote context; the domain's own policy allowed it"

              let parked, id =
                  match
                      Proposals.submitGuarded
                          guarded
                          notes
                          Remote
                          "agent"
                          "t0"
                          None
                          [ add ]
                          Proposals.Queue.empty
                          state0
                  with
                  | Proposals.SubmitProposed(q, id) -> q, id
                  | other -> failtestf "expected the sequence parked, got %A" other

              match Proposals.approveGuarded guarded notes Remote "approver" "t1" id parked state0 with
              | Ok(_, next) ->
                  Expect.isTrue
                      (next.Notes |> List.exists (fun (k, _) -> k = "n2"))
                      "approved: the grant for exactly the stored invocation lifted the registry's NeedsApproval"
              | Error e -> failtestf "the approval was refused: %A" e

              let purge, pid =
                  Proposals.propose "agent" "t0" None [ RemoveNote "n1" ] Proposals.Queue.empty

              match Proposals.approveGuarded guarded notes Remote "approver" "t1" pid purge state0 with
              | Error(Proposals.ApprovalDenied(_, g)) ->
                  Expect.equal
                      g.Message
                      "the remote side never purges"
                      "a deny under the approver's context is lifted by no approval"
              | other -> failtestf "expected the approval denied by the gate, got %A" other ]
