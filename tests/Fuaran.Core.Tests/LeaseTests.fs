module Fuaran.Core.Tests.LeaseTests

open Expecto
open Fuaran.Core

// ---- fixtures ----

/// The string-resource axis (file paths / opaque ids) — the dispatcher's instance.
let private idw: IdWitness<string> =
    { ToString = id
      OfString = id
      Equals = (=) }

let private empty: LeaseState<string> = Lease.emptyState<string> ()

let private ok =
    function
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got Error %A" e

[<Tests>]
let tests =
    testList
        "Lease"
        [ // ---- claim / grant ----
          testCase "Claim grants a lease; holderOf + isHeld report it"
          <| fun _ ->
              let s = ok (Lease.apply idw (Claim("h0", [ "a"; "b" ], 0L, 10L)) empty)
              Expect.isTrue (Lease.isHeld "h0" s) "h0 holds a lease"
              Expect.equal (Lease.holderOf idw "a" s) (Some "h0") "resource a is held by h0"
              Expect.equal (Lease.holderOf idw "z" s) None "unclaimed resource has no holder"

          // ---- conflict names the holder + overlap (GP5) ----
          testCase "an overlapping claim by a different holder is a Conflict naming holder + overlap"
          <| fun _ ->
              let granted = ok (Lease.apply idw (Claim("h0", [ "a"; "b" ], 0L, 10L)) empty)

              match Lease.apply idw (Claim("h1", [ "b"; "c" ], 1L, 10L)) granted with
              | Error(Conflict("h0", overlap)) -> Expect.equal overlap [ "b" ] "overlap is exactly the shared resource"
              | other -> failtestf "expected Conflict(\"h0\", [\"b\"]), got %A" other

          testCase "a same-holder re-Claim renews in place (no conflict)"
          <| fun _ ->
              let s0 = ok (Lease.apply idw (Claim("h0", [ "a" ], 0L, 10L)) empty)
              let s1 = ok (Lease.apply idw (Claim("h0", [ "a"; "b" ], 5L, 20L)) s0)
              Expect.equal (List.length s1.Active) 1 "still one lease (renewed, not appended)"
              Expect.equal (Lease.holderOf idw "b" s1) (Some "h0") "the renewed lease covers the new resource"

          testCase "a disjoint claim by another holder coexists"
          <| fun _ ->
              let s0 = ok (Lease.apply idw (Claim("h0", [ "a" ], 0L, 10L)) empty)
              let s1 = ok (Lease.apply idw (Claim("h1", [ "b" ], 0L, 10L)) s0)
              Expect.equal (List.length s1.Active) 2 "two disjoint leases"

          // ---- release ----
          testCase "Release drops the holder's lease; releasing an absent holder is NoSuchLease"
          <| fun _ ->
              let s0 = ok (Lease.apply idw (Claim("h0", [ "a" ], 0L, 10L)) empty)
              let s1 = ok (Lease.apply idw (Release "h0") s0)
              Expect.isFalse (Lease.isHeld "h0" s1) "h0 released"

              match Lease.apply idw (Release "nope") s1 with
              | Error(NoSuchLease "nope") -> ()
              | other -> failtestf "expected NoSuchLease, got %A" other

          // ---- expiry as data (no clock in Core) ----
          testCase "Expire drops leases past grantedAt+ttl, keeps live ones — a pure function of now"
          <| fun _ ->
              let s = ok (Lease.apply idw (Claim("h0", [ "a" ], 5L, 10L)) empty) // expires at 15
              Expect.isTrue (Lease.isHeld "h0" (ok (Lease.apply idw (Expire 14L) s))) "live just before expiry"
              Expect.isFalse (Lease.isHeld "h0" (ok (Lease.apply idw (Expire 15L) s))) "gone at expiry instant"
              Expect.isFalse (Lease.isHeld "h0" (ok (Lease.apply idw (Expire 99L) s))) "gone well after"
              // a released resource is claimable again by another holder after expiry
              let expired = ok (Lease.apply idw (Expire 15L) s)

              Expect.isTrue
                  (Lease.isHeld "h1" (ok (Lease.apply idw (Claim("h1", [ "a" ], 15L, 10L)) expired)))
                  "reclaimable"

          // ---- canApply ≡ apply ----
          testCase "canApply agrees with apply (accept and reject)"
          <| fun _ ->
              Expect.equal (Lease.canApply idw (Claim("h0", [ "a" ], 0L, 5L)) empty) (Ok()) "valid claim accepted"

              let granted = ok (Lease.apply idw (Claim("h0", [ "a" ], 0L, 10L)) empty)

              match Lease.canApply idw (Claim("h1", [ "a" ], 0L, 10L)) granted with
              | Error(Conflict("h0", _)) -> ()
              | other -> failtestf "expected Conflict from canApply, got %A" other

          // ---- codec ----
          testCase "every lease op round-trips through the wire codec"
          <| fun _ ->
              let ops =
                  [ Claim("lease-42", [ "src/A.fs"; "src/B.fs" ], 1720000000000L, 300000L)
                    Release "lease-42"
                    Expire 1720000300000L ]

              for op in ops do
                  match Lease.decode idw (Lease.encode idw op) with
                  | Ok op2 -> Expect.equal op2 op (sprintf "round-trip %A" op)
                  | Error m -> failtestf "decode failed for %A: %s" op m

          testCase "the wire envelope is a camelCase kind-tag; int64 time survives"
          <| fun _ ->
              let wire = Lease.encode idw (Claim("h0", [ "a" ], 9007199254740993L, 1L))
              Expect.stringContains wire "\"$type\":\"claim\"" "kind-tagged claim envelope"
              // an int64 beyond int32 round-trips (carried as a JSON string)
              match Lease.decode idw wire with
              | Ok(Claim(_, _, g, _)) -> Expect.equal g 9007199254740993L "int64 grantedAt preserved"
              | other -> failtestf "expected Claim, got %A" other

          // ---- op-stream: chain + replay ----
          testCase "a lease stream is hash-chained + replays to the live state"
          <| fun _ ->
              let sw = Lease.streamWitnessFor idw
              let hashFn = OpStream.defaultHash

              let ops =
                  [ Claim("h0", [ "a" ], 0L, 100L)
                    Claim("h1", [ "b" ], 0L, 100L)
                    Release "h0"
                    Claim("h2", [ "a" ], 10L, 100L)
                    Expire 200L ]

              let mutable state = empty
              let mutable recs = OpStream.empty

              for op in ops do
                  let s', recs' = ok (OpStream.append hashFn sw (Human "test") op state recs)
                  state <- s'
                  recs <- recs'

              Expect.isTrue (OpStream.verifyChain hashFn sw recs) "chain verifies"
              Expect.equal (OpStream.replay sw empty recs) (Ok state) "replay from empty = live state"

          // ---- conformance law ----
          testCase "leaseLaws certify totality + canApply + conflict + chain + replay + expiry (Phase 84)"
          <| fun _ ->
              let results = Conformance.leaseLaws 8484 200
              Expect.equal (List.length results) 6 "totality + equiv + conflict + verify + replay + expiry"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "leaseLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.leaseLaws 8484 200) results "same seed ⇒ identical report" ]
