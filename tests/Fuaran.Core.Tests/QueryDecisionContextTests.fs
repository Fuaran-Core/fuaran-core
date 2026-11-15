module Fuaran.Core.Tests.QueryDecisionContextTests

// Phase 651 — the query registry takes the decision context and a grant, as the capability
// registry did in Phase 638. These are the phase's laws on cases a reader can check by eye. None
// of them could be written before the phase: the query registry decided at the unit context only,
// a dispatch had no context or grant to present, and there was no way to mint a grant for a read.
// The sampled forms are `Conformance.policyLaws`' context, grant and named-decision laws, which
// run the query registry beside the capability registry; the proved forms are in
// `proofs/Query.fst`.

open Expecto
open Fuaran.Core

// ---- fixtures ----------------------------------------------------------------------------------

/// The decision context of these fixtures: one fact a host would put there. Core names neither
/// case; the gate below is the only thing that reads it.
type private Side =
    | Local
    | Remote

let private query (id: string) : Query =
    { Id = id
      Params =
        [ { Name = "n"
            Type = IntType
            Required = true } ]
      ResultSchema = [ Field.create "n" IntType ]
      Effect =
        { Host = ReadsHost
          Determinism = Effect.network }
      Source = Ref id
      TimeoutMs = None
      PageSize = None
      Where = []
      OrderBy = [] }

/// One gate: the local side may read anything; the remote side may read the summary, needs an
/// approval to read the ledger, and may never read the secrets.
let private disclosure: PolicyGate<Side, Query, (string * Cell) list> =
    { Policy = "disclosure"
      Decide =
        fun side q _ ->
            match side, q.Id with
            | Local, _ -> PolicyDecision.Allow
            | Remote, "ledger" -> PolicyDecision.NeedsApproval
            | Remote, "secrets" -> PolicyDecision.deny "the remote side never reads the secrets"
            | Remote, _ -> PolicyDecision.Allow }

/// ONE registry. Every test below asks it; none builds a second.
let private registry: QueryRegistry<Side> =
    [ query "summary"; query "ledger"; query "secrets" ]
    |> List.fold (fun r q -> QueryRegistry.register q r |> Result.defaultValue r) QueryRegistry.empty
    |> QueryRegistry.withGate disclosure

let private args = [ "n", Int 5 ]

let private rows: QueryResult =
    { Rows = { Schema = []; Columns = [] }
      PageNum = 0
      TotalRowCount = None
      NextPageToken = None }

/// A resolver that records that it ran.
let private resolver (ran: ResizeArray<string>) (q: Query) : Deferred<QueryResult> =
    ran.Add q.Id
    Ready rows

let private dispatch (side: Side) (grant: Grant option) (id: string) (a: (string * Cell) list) =
    let ran = ResizeArray<string>()
    QueryRegistry.dispatch registry side grant id a (resolver ran), List.ofSeq ran

[<Tests>]
let tests =
    testList
        "Phase 651 — the query registry's decision context and grant"
        [ testCase "one query registry decides differently under two contexts"
          <| fun () ->
              Expect.equal
                  (QueryRegistry.decide registry Local None "ledger" args)
                  PolicyDecision.Allow
                  "the local side reads the ledger"

              Expect.equal
                  (QueryRegistry.decide registry Remote None "ledger" args)
                  PolicyDecision.NeedsApproval
                  "the remote side needs an approval, through the same registry"

              Expect.equal (dispatch Local None "ledger" args) (Ok(Ready rows), [ "ledger" ]) "and the dispatch follows"

              Expect.equal
                  (dispatch Remote None "ledger" args)
                  (Error(QueryApprovalRequired "disclosure"), [])
                  "refused, naming the gate, and the resolver did not run"

          testCase "a needs-approval query runs with the grant for exactly its invocation"
          <| fun () ->
              let grant = Query.grant (query "ledger") args

              Expect.equal
                  (dispatch Remote (Some grant) "ledger" args)
                  (Ok(Ready rows), [ "ledger" ])
                  "the grant lifts it and the resolver runs"

              Expect.equal
                  (QueryRegistry.decide registry Remote (Some grant) "ledger" args)
                  PolicyDecision.Allow
                  "and decide says so without dispatching"

              Expect.equal
                  (QueryRegistry.decideNamed registry Remote (Some grant) "ledger" args)
                  (Ok("disclosure", PolicyDecision.Allow))
                  "the named read keeps the name of the gate whose approval was answered"

              Expect.equal
                  grant.Invocation
                  (Query.invocationPreimage (query "ledger") args)
                  "a grant carries the invocation's identity"

              Expect.equal
                  (Query.grant (query "ledger") [ "n", Int 5 ])
                  grant
                  "one argument set has one grant, minted again"

          testCase "with no grant, or a grant for another invocation, it does not"
          <| fun () ->
              let refused = Error(QueryApprovalRequired "disclosure"), []

              let others =
                  [ "other arguments", Query.grant (query "ledger") [ "n", Int 6 ]
                    "another query", Query.grant (query "summary") args
                    "the hashed capture key", Grant.ofInvocation (Query.invocationKey (query "ledger") args)
                    "a capability's identity", Grant.ofInvocation "ledger\u0001n\u00015\u0001"
                    "an empty identity", Grant.ofInvocation "" ]

              Expect.equal (dispatch Remote None "ledger" args) refused "no grant"

              for label, grant in others do
                  Expect.equal (dispatch Remote (Some grant) "ledger" args) refused label

                  Expect.equal
                      (QueryRegistry.decide registry Remote (Some grant) "ledger" args)
                      PolicyDecision.NeedsApproval
                      label

          testCase "a denied query reaches no resolver, whatever grant is presented"
          <| fun () ->
              let own = Query.grant (query "secrets") args

              let denied =
                  Error(QueryPolicyRefused("disclosure", "the remote side never reads the secrets", [])), []

              Expect.equal (dispatch Remote None "secrets" args) denied "denied with nothing presented"
              Expect.equal (dispatch Remote (Some own) "secrets" args) denied "and with the grant for exactly it"

              Expect.equal
                  (QueryRegistry.decide registry Remote (Some own) "secrets" args)
                  (PolicyDecision.deny "the remote side never reads the secrets")
                  "the decision is the gate's denial, unchanged"

          testCase "an allow is unchanged by any grant"
          <| fun () ->
              let stray = Query.grant (query "ledger") args

              Expect.equal
                  (dispatch Remote (Some stray) "summary" args)
                  (dispatch Remote None "summary" args)
                  "a grant for another invocation changes nothing"

              Expect.equal
                  (dispatch Remote (Some(Query.grant (query "summary") args)) "summary" args)
                  (Ok(Ready rows), [ "summary" ])
                  "and neither does its own"

          testCase "an unregistered id is refused whatever is presented"
          <| fun () ->
              let refused = Error(NoSuchQuery("audit", [ "ledger"; "secrets"; "summary" ])), []

              for side in [ Local; Remote ] do
                  for grant in
                      [ None
                        Some(Query.grant (query "audit") args)
                        Some(Query.grant (query "ledger") args) ] do
                      Expect.equal (dispatch side grant "audit" args) refused "default-deny by shape"

          testCase "parameters that do not validate are refused before any gate, whatever is presented"
          <| fun () ->
              let consulted = ResizeArray<string>()

              let watched =
                  registry
                  |> QueryRegistry.withGate
                      { Policy = "watch"
                        Decide =
                          fun _ q _ ->
                              consulted.Add q.Id
                              PolicyDecision.Allow }

              let bad = [ "n", Str "five" ]

              for side in [ Local; Remote ] do
                  for grant in [ None; Some(Query.grant (query "ledger") bad) ] do
                      let ran = ResizeArray<string>()

                      Expect.equal
                          (QueryRegistry.dispatch watched side grant "ledger" bad (resolver ran))
                          (Error(ParamTypeMismatch("n", IntType, StringType)))
                          "the validation refusal"

                      Expect.isEmpty ran "and no resolver ran"

              Expect.isEmpty consulted "no gate was consulted"

          testCase "the grant for a read covers every page of it"
          <| fun () ->
              let grant = Query.grant (query "ledger") args

              for token in [ None; Some "page-2"; Some "page-3" ] do
                  let ran = ResizeArray<string>()

                  Expect.equal
                      (QueryRegistry.dispatchPage registry Remote (Some grant) "ledger" args token (fun q _ ->
                          resolver ran q))
                      (Ok(Ready rows))
                      (sprintf "the page %A is read with the invocation's grant" token)

                  Expect.equal (List.ofSeq ran) [ "ledger" ] "and its resolver ran once"

                  Expect.equal
                      (QueryRegistry.dispatchPage registry Remote None "ledger" args token (fun q _ -> resolver ran q))
                      (Error(QueryApprovalRequired "disclosure"))
                      "and refused with none"

              Expect.notEqual
                  (Grant.ofInvocation (Query.invocationKeyPage (query "ledger") args (Some "page-2")))
                  grant
                  "a page's capture key is not the identity a grant is bound to"

          testCase "the decision is a function of the declaration, the arguments, the context and the grant"
          <| fun () ->
              let grant = Some(Query.grant (query "ledger") args)
              let seen = ResizeArray<Side * string * (string * Cell) list>()

              let recording =
                  registry
                  |> QueryRegistry.withGate
                      { Policy = "recording"
                        Decide =
                          fun side q shown ->
                              seen.Add(side, q.Id, shown)
                              PolicyDecision.Allow }

              let first = QueryRegistry.decide recording Remote grant "ledger" args
              let second = QueryRegistry.decide recording Remote grant "ledger" args
              Expect.equal first PolicyDecision.Allow "the grant lifts the one approval the first gate asks for"
              Expect.equal first second "the same asking, the same answer"

              Expect.equal
                  (List.ofSeq seen)
                  [ Remote, "ledger", args; Remote, "ledger", args ]
                  "and the gate was shown the context, the declaration and the arguments, and nothing else"

          testCase "a registry at the unit context decides as it does today"
          <| fun () ->
              // A gate written before this phase reads the declaration and the arguments. Carried
              // across by `ignoringContext`, or written with `fun () ...`, the registry it is added to
              // takes `()` and no grant at each dispatch — one changed line — and decides as before.
              let old (q: Query) (_: (string * Cell) list) =
                  if q.Id = "secrets" then PolicyDecision.deny "no secrets"
                  elif q.Id = "ledger" then PolicyDecision.NeedsApproval
                  else PolicyDecision.Allow

              let unitRegistry: QueryRegistry<unit> =
                  [ query "summary"; query "ledger"; query "secrets" ]
                  |> List.fold (fun r q -> QueryRegistry.register q r |> Result.defaultValue r) QueryRegistry.empty
                  |> QueryRegistry.withGate (PolicyGate.ignoringContext "old" old)

              for id in [ "summary"; "ledger"; "secrets"; "audit" ] do
                  let ran = ResizeArray<string>()
                  let out = QueryRegistry.dispatch unitRegistry () None id args (resolver ran)

                  let expected =
                      match id with
                      | "summary" -> Ok(Ready rows)
                      | "ledger" -> Error(QueryApprovalRequired "old")
                      | "secrets" -> Error(QueryPolicyRefused("old", "no secrets", []))
                      | _ -> Error(NoSuchQuery("audit", [ "ledger"; "secrets"; "summary" ]))

                  Expect.equal out expected id
                  Expect.equal (List.ofSeq ran) (if id = "summary" then [ "summary" ] else []) id

                  Expect.equal
                      (QueryRegistry.decide unitRegistry () None id args)
                      (match QueryRegistry.tryFind id unitRegistry with
                       | Some q -> old q args
                       | None -> QueryRegistry.decide unitRegistry () None id args)
                      "the gate decides as the old one did"

          testCase "a refusal reaches the observer with its context, and a lifted approval reaches none"
          <| fun () ->
              let told = ResizeArray<PolicyDenial<Side, (string * Cell) list>>()
              let observed = registry |> QueryRegistry.onDenied told.Add

              let run grant =
                  QueryRegistry.dispatch observed Remote grant "ledger" args (fun _ -> Ready rows)

              run (Some(Query.grant (query "ledger") args)) |> ignore
              Expect.isEmpty told "a granted dispatch is not a refusal"
              run None |> ignore

              Expect.equal
                  (List.ofSeq told)
                  [ { Policy = "disclosure"
                      Context = Remote
                      Id = "ledger"
                      Args = args
                      Decision = PolicyDecision.NeedsApproval } ]
                  "the refusal names the gate and the context it decided under"

          testCase "under fixes a context: the registry one side sees"
          <| fun () ->
              let remote: QueryRegistry<unit> = QueryRegistry.under Remote registry

              for id in [ "summary"; "ledger"; "secrets"; "audit" ] do
                  Expect.equal
                      (QueryRegistry.decide remote () None id args)
                      (QueryRegistry.decide registry Remote None id args)
                      id

                  Expect.equal
                      (QueryRegistry.decideNamed remote () None id args)
                      (QueryRegistry.decideNamed registry Remote None id args)
                      id

          testCase "the named decision is the refusal every dispatch verb makes, under each context and grant"
          <| fun () ->
              for side in [ Local; Remote ] do
                  for grant in [ None; Some(Query.grant (query "ledger") args) ] do
                      for id in [ "summary"; "ledger"; "secrets"; "audit" ] do
                          let named = QueryRegistry.decideNamed registry side grant id args

                          let expected =
                              match named with
                              | Error e -> Some(Error e)
                              | Ok(_, PolicyDecision.Allow) -> None
                              | Ok(name, PolicyDecision.Deny g) ->
                                  Some(Error(QueryPolicyRefused(name, g.Message, g.Alternatives)))
                              | Ok(name, PolicyDecision.NeedsApproval) -> Some(Error(QueryApprovalRequired name))

                          let typed _ _ = Ok(Ready rows)

                          let outcomes =
                              [ "dispatch", QueryRegistry.dispatch registry side grant id args (fun _ -> Ready rows)
                                "dispatchWithArgs", QueryRegistry.dispatchWithArgs registry side grant id args typed
                                "dispatchPage",
                                QueryRegistry.dispatchPage registry side grant id args (Some "t") (fun _ _ ->
                                    Ready rows)
                                "dispatchPageWith",
                                QueryRegistry.dispatchPageWith registry side grant id args (Some "t") (fun _ _ _ ->
                                    Ok(Ready rows))
                                "dispatchCaptured",
                                (QueryRegistry.dispatchCaptured
                                    OpStream.defaultHash
                                    (fun _ -> "rows")
                                    registry
                                    side
                                    grant
                                    id
                                    args
                                    (fun _ -> Ready rows)
                                    [])
                                    .Outcome ]

                          for verb, out in outcomes do
                              match expected with
                              | Some refusal -> Expect.equal out refusal (sprintf "%s %A %A %s" verb side grant id)
                              | None -> Expect.equal out (Ok(Ready rows)) (sprintf "%s %A %A %s" verb side grant id) ]
