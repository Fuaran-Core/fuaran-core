module Fuaran.Core.Tests.RegistryLifecycleTests

// ---------------------------------------------------------------------------
// Phase 316 — a registry is a lattice, not an append log. The four registries (capability,
// function, query, validator) each gain `unregister` / `replace` / `restrict` / `union`; the
// function registry is opaque, so its result index cannot drift from its entries; a content pack
// unloads; and a paged query reaches its resolver through the seam with a key per page. The
// sampled laws are `Conformance.registryLaws`, `packLoadingLaws` and `queryLaws`; these are the
// named cases a reader can run one at a time.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core

let private hole (hi: int) : SigEntry =
    { Addr = "h"
      Name = "h"
      Kind = ValueHole(IntRange(0, hi))
      Required = true }

let private cap (id: string) (hi: int) : Capability =
    Capability.create
        id
        { Name = id
          Holes = [ hole hi ]
          Effect = Effect.pureDeterministic }
        BuildTime

let private query (id: string) : Query =
    { Id = id
      Params =
        [ { Name = "region"
            Type = StringType
            Required = true } ]
      ResultSchema = [ "n", IntType ]
      Effect =
        { Host = ReadsHost
          Determinism = Effect.network }
      Source = Ref id
      TimeoutMs = None
      PageSize = Some 2
      Where = []
      OrderBy = [] }

let private orFail (r: Result<'a, 'e>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> failtestf "unexpected refusal %A" e

let private caps (ids: string list) : CapabilityRegistry =
    ids
    |> List.fold (fun r id -> CapabilityRegistry.register (cap id 9) r |> orFail) CapabilityRegistry.empty

let private fns (entries: (string * string) list) : FunctionRegistry =
    entries
    |> List.fold
        (fun r (id, kind) -> FunctionRegistry.register (FunctionRegistry.entry kind (cap id 9)) r |> orFail)
        FunctionRegistry.empty

let private queries (ids: string list) : QueryRegistry =
    ids
    |> List.fold (fun r id -> QueryRegistry.register (query id) r |> orFail) QueryRegistry.empty

let private family (id: string) : RuleFamily<unit, string> = { Id = id; Run = fun _ _ -> [] }

let private rules (ids: string list) : Validator.RuleRegistry<unit, string> =
    Validator.ofFamilies (ids |> List.map family) |> orFail

let private capIds r =
    CapabilityRegistry.enumerate r |> List.map _.Id

let private ofKind (kind: string) (r: FunctionRegistry) : string list =
    FunctionRegistry.findBySignature
        Subsumes
        { ResultType = Some kind
          Available = [ hole 0 ] }
        r
    |> List.map _.Capability.Id

[<Tests>]
let tests =
    testList
        "Registry lifecycle (Phase 316)"
        [ testCase "unregister undoes register on a fresh id, on all four registries"
          <| fun _ ->
              let cr = caps [ "a"; "b" ]

              Expect.equal
                  (CapabilityRegistry.register (cap "c" 9) cr
                   |> Result.bind (CapabilityRegistry.unregister "c"))
                  (Ok cr)
                  "capability"

              let fr = fns [ "a", "doc"; "b", "sheet" ]

              Expect.equal
                  (FunctionRegistry.register (FunctionRegistry.entry "doc" (cap "c" 9)) fr
                   |> Result.bind (FunctionRegistry.unregister "c"))
                  (Ok fr)
                  "function — the index too, by structural equality"

              let qr = queries [ "a"; "b" ]

              Expect.equal
                  (QueryRegistry.register (query "c") qr
                   |> Result.bind (QueryRegistry.unregister "c"))
                  (Ok qr)
                  "query"

              let vr = rules [ "a"; "b" ]

              Expect.equal
                  (Validator.register (family "c") vr
                   |> Result.bind (Validator.unregister "c")
                   |> Result.map Validator.enumerate)
                  (Ok [ "a"; "b" ])
                  "validator"

          testCase "an unheld id is refused by each seam's own unknown-id error, naming the held ids"
          <| fun _ ->
              Expect.equal
                  (CapabilityRegistry.unregister "z" (caps [ "b"; "a" ]))
                  (Error(NoSuchCapability("z", [ "a"; "b" ])))
                  "capability"

              Expect.equal
                  (FunctionRegistry.unregister "z" (fns [ "a", "doc" ]))
                  (Error(NoSuchCapability("z", [ "a" ])))
                  "function"

              Expect.equal (QueryRegistry.unregister "z" (queries [ "a" ])) (Error(NoSuchQuery("z", [ "a" ]))) "query"

              Expect.equal
                  (QueryRegistry.replace (query "z") (queries [ "a" ]))
                  (Error(NoSuchQuery("z", [ "a" ])))
                  "query replace"

              match Validator.unregister "z" (rules [ "b"; "a" ]) with
              | Error(RegistrationError.UnknownRule("z", [ "b"; "a" ])) -> ()
              | other -> failtestf "expected UnknownRule naming the registration order, got %A" other

          testCase "replace swaps the entry under its id through the admission gate, and keeps the validator's order"
          <| fun _ ->
              let cr = caps [ "a"; "b" ]
              let r2 = CapabilityRegistry.replace (cap "a" 3) cr |> orFail
              Expect.equal (CapabilityRegistry.tryFind "a" r2) (Some(cap "a" 3)) "swapped"
              Expect.equal (CapabilityRegistry.tryFind "b" r2) (CapabilityRegistry.tryFind "b" cr) "the other kept"

              match CapabilityRegistry.replace (cap "a" (-1)) cr with
              | Error(IllFormedCapability("a", _)) -> ()
              | other -> failtestf "an ill-formed replacement must be refused as registration refuses it, got %A" other

              let twice =
                  { query "a" with
                      Params = (query "a").Params @ (query "a").Params }

              Expect.equal (QueryRegistry.replace twice (queries [ "a" ])) (Error(DuplicateParam "region")) "query gate"

              let vr = rules [ "a"; "b"; "c" ] |> Validator.replace (family "b") |> orFail
              Expect.equal (Validator.enumerate vr) [ "a"; "b"; "c" ] "position kept"

          testCase "restrict narrows to the ids kept, never widening"
          <| fun _ ->
              let keep = Set.ofList [ "b"; "z" ]
              Expect.equal (capIds (CapabilityRegistry.restrict keep (caps [ "a"; "b"; "c" ]))) [ "b" ] "capability"

              Expect.equal
                  (FunctionRegistry.ids (FunctionRegistry.restrict keep (fns [ "a", "doc"; "b", "doc" ])))
                  [ "b" ]
                  "function"

              Expect.equal
                  (QueryRegistry.restrict keep (queries [ "a"; "b" ])
                   |> QueryRegistry.enumerate
                   |> List.map _.Id)
                  [ "b" ]
                  "query"

              Expect.equal
                  (Validator.restrict keep (rules [ "c"; "b"; "a" ]) |> Validator.enumerate)
                  [ "b" ]
                  "validator"

              let restricted = CapabilityRegistry.restrict keep (caps [ "a"; "b" ])

              match CapabilityRegistry.dispatch restricted "a" [ "h", "1" ] (fun _ () -> Ready 1) with
              | Error(NoSuchCapability("a", [ "b" ])) -> ()
              | other -> failtestf "a restricted-away id must be default-denied, got %A" other

          testCase "union joins disjoint registries and refuses a shared id by the duplicate error"
          <| fun _ ->
              Expect.equal
                  (CapabilityRegistry.union (caps [ "a" ]) (caps [ "b" ]) |> Result.map capIds)
                  (Ok [ "a"; "b" ])
                  "joined"

              Expect.equal
                  (CapabilityRegistry.union (caps [ "a"; "c" ]) (caps [ "b"; "c" ]))
                  (Error(DuplicateCapability "c"))
                  "capability collision"

              Expect.equal
                  (QueryRegistry.union (queries [ "a" ]) (queries [ "a" ]))
                  (Error(DuplicateQuery "a"))
                  "query collision"

              Expect.equal
                  (FunctionRegistry.union (fns [ "a", "doc" ]) (fns [ "a", "doc" ]))
                  (Error(DuplicateCapability "a"))
                  "function collision"

              Expect.equal
                  (Validator.union (rules [ "b" ]) (rules [ "a" ])
                   |> Result.map Validator.enumerate)
                  (Ok [ "b"; "a" ])
                  "validator keeps each side's order"

              match Validator.union (rules [ "a" ]) (rules [ "a" ]) with
              | Error(RegistrationError.DuplicateRule("a", [ "a" ])) -> ()
              | other -> failtestf "expected DuplicateRule, got %A" other

          testCase "the function registry's index follows every edit — no phantom, no miss"
          <| fun _ ->
              let fr = fns [ "a", "doc"; "b", "doc"; "c", "sheet" ]
              Expect.equal (ofKind "doc" fr) [ "a"; "b" ] "built"

              let unregistered = FunctionRegistry.unregister "a" fr |> orFail
              Expect.equal (ofKind "doc" unregistered) [ "b" ] "no phantom after unregister"

              let moved =
                  FunctionRegistry.replace (FunctionRegistry.entry "sheet" (cap "b" 9)) fr
                  |> orFail

              Expect.equal (ofKind "doc" moved) [ "a" ] "replace moves the entry out of its old kind"
              Expect.equal (ofKind "sheet" moved) [ "b"; "c" ] "and into its new one — no miss"

              let joined =
                  FunctionRegistry.union (fns [ "a", "doc" ]) (fns [ "d", "doc" ]) |> orFail

              Expect.equal (ofKind "doc" joined) [ "a"; "d" ] "union indexes both"
              Expect.equal (ofKind "doc" (FunctionRegistry.restrict (Set.ofList [ "d" ]) joined)) [ "d" ] "restrict too"

          testCase "a content pack unloads, all or nothing"
          <| fun _ ->
              let baseEntry = FunctionRegistry.entry "doc" (cap "base" 9)
              let reg = FunctionRegistry.register baseEntry FunctionRegistry.empty |> orFail

              let manifest =
                  { PackId = "pk"
                    Domain = "d"
                    PackVersion = 1
                    Functions = [ ContentPack.pack "curried" (Set.ofList [ "h" ]) baseEntry ] }

              let loaded = ContentPack.load manifest reg |> orFail
              Expect.equal (FunctionRegistry.ids loaded) [ "base"; "curried" ] "loaded"

              Expect.equal
                  (ContentPack.unload manifest loaded)
                  (Ok reg)
                  "unload gives back the registry the pack loaded into"

              Expect.equal
                  (ContentPack.unload manifest reg)
                  (Error(PackNotLoaded("pk", "curried", [ "base" ])))
                  "a pack not loaded is refused by name" ]

[<Tests>]
let pagingTests =
    testList
        "Query paging (Phase 316)"
        [ testCase "a paged query receives its token through the seam and keys each page apart"
          <| fun _ ->
              let q = query "sales"
              let args = [ "region", Str "UK" ]
              let reg = queries [ "sales" ]
              let mutable seen = []

              let resolve (_: Query) (token: string option) : Deferred<QueryResult> =
                  seen <- seen @ [ token ]

                  Ready
                      { Rows =
                          { Schema = [ "n", IntType ]
                            Columns = [] }
                        PageNum = (if token.IsNone then 0 else 1)
                        TotalRowCount = None
                        NextPageToken = None }

              match QueryRegistry.dispatchPage reg "sales" args (Some "cursor-2") resolve with
              | Ok(Ready r) -> Expect.equal r.PageNum 1 "the resolver answered the page it was asked for"
              | other -> failtestf "expected a settled page, got %A" other

              Expect.equal seen [ Some "cursor-2" ] "the token reached the resolver"

              Expect.equal
                  (Query.invocationKeyPage q args None)
                  (Query.invocationKey q args)
                  "the first page keys exactly as before paging, so existing journals replay"

              let keys =
                  [ None; Some ""; Some "p"; Some "cursor-2" ]
                  |> List.map (Query.invocationKeyPage q args)

              Expect.equal (List.distinct keys) keys "distinct tokens give distinct keys"

          testCase "a refused page never reaches the resolver"
          <| fun _ ->
              let mutable ran = false

              let resolve (_: Query) (_: string option) : Deferred<QueryResult> =
                  ran <- true
                  Pending

              Expect.equal
                  (QueryRegistry.dispatchPage (queries [ "sales" ]) "nope" [] (Some "t") resolve)
                  (Error(NoSuchQuery("nope", [ "sales" ])))
                  "default-deny"

              Expect.equal
                  (Query.invokePage (query "sales") [] (Some "t") resolve)
                  (Error(RequiredParamsUnbound [ "region" ]))
                  "validation first"

              Expect.isFalse ran "no resolver ran" ]
