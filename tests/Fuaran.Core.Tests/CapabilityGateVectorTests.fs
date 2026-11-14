module Fuaran.Core.Tests.CapabilityGateVectorTests

open System.IO
open Expecto
open Fuaran.Core

// ---------------------------------------------------------------------------
//  Phase 650 — the capability gate family (`laws/capability-gate-laws.json`, DECISIONS.md D255) read
//  back the way a port reads it: the registry, the contexts and the gate table are built from the
//  FILE's head, not from the exporter's values, and every vector is decided with the production
//  registry built from them. So a vector the file states and the kit does not decide fails here, and
//  so does a head that no longer declares the registry the vectors were computed against. That the
//  committed bytes are the emitter's is the laws row of `ExportersTests`.
// ---------------------------------------------------------------------------

let private field (name: string) (el: JVal) : JVal option =
    match el with
    | JObj ms -> ms |> List.tryPick (fun (k, v) -> if k = name then Some v else None)
    | _ -> None

let private text (name: string) (el: JVal) : string =
    match field name el with
    | Some(JStr s) -> s
    | other -> failwithf "`%s` is not a string: %A" name other

let private items (name: string) (el: JVal) : JVal list =
    match field name el with
    | Some(JArr xs) -> xs
    | other -> failwithf "`%s` is not an array: %A" name other

let private argsOf (el: JVal) : (string * string) list =
    items "args" el |> List.map (fun a -> text "addr" a, text "value" a)

let private grantOf (el: JVal) : Grant option =
    match field "grant" el with
    | Some(JStr g) -> Some(Grant.ofInvocation g)
    | None -> None
    | other -> failwithf "`grant` is not a string: %A" other

/// A decision as the file writes it, members `decision`, `message`, `alternatives`.
let private decisionOf (el: JVal) : PolicyDecision =
    match text "decision" el with
    | "allow" -> PolicyDecision.Allow
    | "needsApproval" -> PolicyDecision.NeedsApproval
    | "deny" ->
        PolicyDecision.denyWith
            (text "message" el)
            (items "alternatives" el
             |> List.map (function
                 | JStr s -> s
                 | other -> failwithf "an alternative is not a string: %A" other))
    | other -> failwithf "unknown decision %s" other

let private decisionMembers (d: PolicyDecision) : (string * JVal) list =
    match d with
    | PolicyDecision.Allow -> [ "decision", JStr "allow" ]
    | PolicyDecision.NeedsApproval -> [ "decision", JStr "needsApproval" ]
    | PolicyDecision.Deny g ->
        [ "decision", JStr "deny"
          "message", JStr g.Message
          "alternatives", JArr(g.Alternatives |> List.map JStr) ]

let private refusalMembers (e: InvokeError) : (string * JVal) list =
    match e with
    | NoSuchCapability(id, known) ->
        [ "error", JStr "noSuchCapability"
          "id", JStr id
          "known", JArr(known |> List.map JStr) ]
    | ArgOutOfSpace(addr, _, _) -> [ "error", JStr "argOutOfSpace"; "addr", JStr addr ]
    | PolicyRefused(policy, reason, allowed) ->
        [ "error", JStr "policyRefused"
          "policy", JStr policy
          "reason", JStr reason
          "allowed", JArr(allowed |> List.map JStr) ]
    | ApprovalRequired policy -> [ "error", JStr "approvalRequired"; "policy", JStr policy ]
    | other -> [ "error", JStr "unexpected"; "detail", JStr(InvokeError.describe other) ]

/// The production registry the file's head declares: its capabilities, and one gate per table row
/// that answers its rule's decision for the context and the capability, and `Allow` for any other.
let private registryOf (file: JVal) : CapabilityRegistry<string> =
    let registered =
        items "registry" file
        |> List.map (function
            | JStr d ->
                match CapabilityCodec.decode d with
                | Ok c -> c
                | Error e -> failwithf "a registry declaration does not decode: %s" e
            | other -> failwithf "a registry entry is not a string: %A" other)
        |> List.fold (fun r c -> r |> Result.bind (CapabilityRegistry.register c)) (Ok CapabilityRegistry.empty)
        |> Result.defaultWith (fun e -> failwithf "the declared registry does not build: %A" e)

    items "gates" file
    |> List.fold
        (fun r gate ->
            let rules =
                items "rules" gate
                |> List.map (fun rule -> text "context" rule, text "capability" rule, decisionOf rule)

            r
            |> CapabilityRegistry.withGate
                { Policy = text "policy" gate
                  Decide =
                    fun ctx c _ ->
                        rules
                        |> List.tryPick (fun (x, cap, d) -> if x = ctx && cap = c.Id then Some d else None)
                        |> Option.defaultValue PolicyDecision.Allow })
        registered

/// What this kit answers for one vector, in the file's members: `None` for a case it does not know.
let private answer (reg: CapabilityRegistry<string>) (v: JVal) : (string * JVal) list option =
    let input =
        field "input" v
        |> Option.defaultWith (fun () -> failwith "a vector has no input")

    let capability = text "capability" input
    let args = argsOf input
    let grant = grantOf input

    let cap () =
        CapabilityRegistry.tryFind capability reg
        |> Option.defaultWith (fun () -> failwithf "%s is not declared in the head" capability)

    match text "case" v with
    | "invocationIdentity" -> Some [ "identity", JStr(Capability.invocationPreimage (cap ()) args) ]
    | "grantCovers" ->
        Some [ "covers", JBool(Grant.covers (Capability.invocationPreimage (cap ()) args) (Option.toList grant)) ]
    | "decideNamed" ->
        match CapabilityRegistry.decideNamed reg (text "context" input) grant capability args with
        | Ok(policy, d) -> Some([ "verdict", JStr "decided"; "policy", JStr policy ] @ decisionMembers d)
        | Error e -> Some(("verdict", JStr "refused") :: refusalMembers e)
    | "dispatch" ->
        let ran = ref 0

        match
            CapabilityRegistry.dispatch reg (text "context" input) grant capability args (fun _ () ->
                ran.Value <- ran.Value + 1
                Ready())
        with
        | Ok(Ready()) when ran.Value = 1 -> Some [ "outcome", JStr "performed" ]
        | Error e when ran.Value = 0 -> Some(("outcome", JStr "refused") :: refusalMembers e)
        | other ->
            Some
                [ "outcome", JStr "unexpected"
                  "detail", JStr(sprintf "%A after %d run(s)" other ran.Value) ]
    | _ -> None

/// Every vector of `fileText` this kit disagrees with, by id.
let private disagreements (fileText: string) : string list =
    let file =
        match Json.parse fileText with
        | Ok j -> j
        | Error e -> failwithf "the family is not JSON: %s" e

    let reg = registryOf file

    items "vectors" file
    |> List.choose (fun v ->
        let id = text "id" v

        match answer reg v, field "expected" v with
        | None, _ -> Some(sprintf "%s: case %s is unknown to this kit" id (text "case" v))
        | Some mine, Some(JObj expected) when List.sortBy fst mine = List.sortBy fst expected -> None
        | Some mine, expected -> Some(sprintf "%s: the kit answers %A, the file says %A" id mine expected))

let private committed () =
    File.ReadAllText(Path.Combine(OwnedConformance.root (), "laws", LawVectorExport.Gates.fileName))

[<Tests>]
let tests =
    testList
        "Laws.CapabilityGate"
        [ testCase "the reference reads its own gate family back and decides every vector as the file says"
          <| fun _ ->
              let text = committed ()
              Expect.isEmpty (disagreements text) "every vector decided as written"

          testCase "the family reaches every case, every decision under each context, and every refusal it names"
          <| fun _ ->
              let file =
                  match Json.parse (committed ()) with
                  | Ok j -> j
                  | Error e -> failwithf "not JSON: %s" e

              let vectors = items "vectors" file

              let byCase c =
                  vectors |> List.filter (fun v -> text "case" v = c)

              Expect.equal
                  (vectors |> List.map (text "case") |> List.distinct)
                  [ "invocationIdentity"; "grantCovers"; "decideNamed"; "dispatch" ]
                  "the four cases, in order"

              let expectedOf (v: JVal) = field "expected" v |> Option.get

              for ctx in [ "trusted"; "untrusted" ] do
                  let decided =
                      byCase "decideNamed"
                      |> List.filter (fun v -> field "input" v |> Option.map (text "context") = Some ctx)
                      |> List.choose (fun v -> field "decision" (expectedOf v))

                  for d in [ "allow"; "needsApproval"; "deny" ] do
                      Expect.contains decided (JStr d) (sprintf "%s reaches %s" ctx d)

              let errors =
                  byCase "dispatch"
                  |> List.choose (fun v -> field "error" (expectedOf v))
                  |> List.distinct

              for e in [ "policyRefused"; "approvalRequired"; "noSuchCapability" ] do
                  Expect.contains errors (JStr e) (sprintf "a dispatch is refused %s" e)

              Expect.contains
                  (byCase "dispatch" |> List.map (fun v -> field "outcome" (expectedOf v)))
                  (Some(JStr "performed"))
                  "a dispatch is performed"

              // a lifted decision is in the file: needsApproval without the grant, allow with it
              Expect.exists
                  (byCase "decideNamed")
                  (fun v ->
                      text "id" v = "gate-decide-untrusted-write-grant-own"
                      && field "decision" (expectedOf v) = Some(JStr "allow")
                      && field "policy" (expectedOf v) = Some(JStr "ceiling"))
                  "the grant's lift keeps the gate's name"

          testCase "the gate family's checker names a perturbed vector — the read-back can go red"
          <| fun _ ->
              let text = committed ()

              let perturbed =
                  text.Replace(
                      "\"policy\": \"audit\", \"decision\": \"needsApproval\"",
                      "\"policy\": \"audit\", \"decision\": \"allow\""
                  )

              Expect.notEqual perturbed text "the perturbation landed"

              Expect.exists
                  (disagreements perturbed)
                  (fun d -> d.StartsWith "gate-decide-trusted-write-grant-none")
                  "the perturbed vector is named" ]
