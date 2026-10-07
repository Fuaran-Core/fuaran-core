module Fuaran.Core.Tests.FunctionTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

let private valueOf id (n: RNode) =
    Tree.tryFind nodew idw id n |> Option.map (fun x -> x.Value)

let private holeAt id (n: RNode) =
    Tree.tryFind nodew idw id n |> Option.bind (fun x -> x.Hole)

// ---- Phase 57 content-pack fixtures: a 2-hole base function in a registry ----

let private packIntHole addr : SigEntry =
    { Addr = addr
      Name = addr
      Kind = "value"
      Space = Some(IntRange(0, 100))
      Slot = None
      Action = None
      Required = true }

let private packBaseSig: Signature =
    { Name = "doc-fn"
      Holes = [ packIntHole "h0"; packIntHole "h1" ]
      Effect = Effect.pureDeterministic }

let private packBaseEntry () : FunctionEntry =
    FunctionRegistry.entry "doc" (Capability.create "doc-fn" packBaseSig BuildTime)

let private packBaseReg () : FunctionRegistry =
    FunctionRegistry.empty
    |> FunctionRegistry.register (packBaseEntry ())
    |> function
        | Ok r -> r
        | Error e -> failwithf "base register failed: %A" e

[<Tests>]
let tests =
    testList
        "Function"
        [ testCase "signature enumerates the declared holes"
          <| fun _ ->
              let sg = Function.signature artw "tpl" (template ())
              Expect.equal (sg.Holes |> List.map (fun h -> h.Name)) [ "title"; "count"; "body" ] "names"
              Expect.equal (sg.Holes |> List.map (fun h -> h.Kind)) [ "value"; "value"; "slot" ] "kinds"
              Expect.equal (sg.Holes |> List.map (fun h -> h.Addr)) [ "tpl/t"; "tpl/c"; "tpl/s" ] "absolute addresses"

          testCase "apply binds every hole by absolute address"
          <| fun _ ->
              let args =
                  Map.ofList
                      [ "tpl/t", ValueArg "Hello"
                        "tpl/c", ValueArg "5"
                        "tpl/s", SlotArg(RNode.leaf "p" "para" "hi") ]

              match Function.apply artw args (template ()) with
              | Ok r ->
                  Expect.equal (valueOf "t" r) (Some "Hello") "title bound"
                  Expect.equal (valueOf "c" r) (Some "5") "count bound"
                  Expect.isNone (holeAt "t" r) "title hole cleared"
                  Expect.equal (holeAt "s" r) None "slot hole cleared"
              | Error e -> failtestf "unexpected %A" e

          testCase "apply rejects a value outside its space"
          <| fun _ ->
              let args =
                  Map.ofList
                      [ "tpl/t", ValueArg "Hi"
                        "tpl/c", ValueArg "99"
                        "tpl/s", SlotArg(RNode.leaf "p" "para" "hi") ]

              match Function.apply artw args (template ()) with
              | Error(ValueOutOfSpace("tpl/c", IntRange(0, 10), "99")) -> ()
              | other -> failtestf "expected ValueOutOfSpace, got %A" other

          testCase "apply (strict) requires every hole to be bound"
          <| fun _ ->
              let args = Map.ofList [ "tpl/t", ValueArg "Hi" ]

              match Function.apply artw args (template ()) with
              | Error(RequiredHolesUnbound addrs) ->
                  Expect.containsAll addrs [ "tpl/c"; "tpl/s" ] "names the unbound holes"
              | other -> failtestf "expected RequiredHolesUnbound, got %A" other

          testCase "apply rejects an arg that addresses no declared hole"
          <| fun _ ->
              let args = Map.ofList [ "tpl/nope", ValueArg "x" ]

              match Function.apply artw args (template ()) with
              | Error(UnknownHoleAddr("tpl/nope", declared)) -> Expect.contains declared "tpl/t" "enumerates declared"
              | other -> failtestf "expected UnknownHoleAddr, got %A" other

          testCase "curry partially applies, leaving the rest open"
          <| fun _ ->
              let args = Map.ofList [ "tpl/t", ValueArg "Hi" ]

              match Function.curry artw args (template ()) with
              | Ok r ->
                  Expect.equal (valueOf "t" r) (Some "Hi") "title bound"
                  Expect.isNone (holeAt "t" r) "title hole cleared"
                  Expect.isSome (holeAt "c" r) "count still open"
                  Expect.isSome (holeAt "s" r) "body still open"
              | Error e -> failtestf "unexpected %A" e

          // Phase 24 — a curried artifact introspects only its still-open holes.
          testCase "signature over a curried tree omits the bound hole (Bind clears it)"
          <| fun _ ->
              match Function.curry artw (Map.ofList [ "tpl/t", ValueArg "Hi" ]) (template ()) with
              | Ok curried ->
                  let sg = Function.signature artw "tpl" curried
                  Expect.equal (sg.Holes |> List.map (fun h -> h.Addr)) [ "tpl/c"; "tpl/s" ] "tpl/t dropped"
              | Error e -> failtestf "unexpected %A" e

          testCase "signatureExcluding narrows the projection explicitly"
          <| fun _ ->
              let full = Function.signature artw "tpl" (template ())
              let narrowed = Function.signatureExcluding (Set.ofList [ "tpl/t" ]) full
              Expect.equal (narrowed.Holes |> List.map (fun h -> h.Addr)) [ "tpl/c"; "tpl/s" ] "tpl/t excluded"

              // the JSON Schema lists only the still-open holes in both properties and required
              let json = Json.render (Function.toJsonSchema narrowed)
              Expect.isFalse (json.Contains "tpl/t") "bound addr absent from the schema"
              Expect.stringContains json "tpl/c" "open value hole present"
              Expect.stringContains json "tpl/s" "open slot present"

          testCase "curry-then-apply equals full apply (narrowing is execution-consistent)"
          <| fun _ ->
              let full =
                  Map.ofList
                      [ "tpl/t", ValueArg "Hello"
                        "tpl/c", ValueArg "5"
                        "tpl/s", SlotArg(RNode.leaf "p" "para" "hi") ]

              let viaApply = Function.apply artw full (template ())

              let viaCurry =
                  Function.curry artw (Map.ofList [ "tpl/t", ValueArg "Hello" ]) (template ())
                  |> Result.bind (fun curried ->
                      Function.apply
                          artw
                          (Map.ofList [ "tpl/c", ValueArg "5"; "tpl/s", SlotArg(RNode.leaf "p" "para" "hi") ])
                          curried)

              Expect.equal viaCurry viaApply "curry then apply the rest = apply all at once"

          testCase "compose wires an inner tree into a slot and joins effects"
          <| fun _ ->
              let inner =
                  { RNode.leaf "p" "para" "composed" with
                      Eff =
                          { Host = Pure
                            Determinism = Effect.clock } }

              let outer = template ()

              match Function.compose artw "tpl/s" inner outer with
              | Ok r ->
                  let bodyChildren =
                      Tree.tryFind nodew idw "s" r
                      |> Option.map (fun s -> s.Children |> List.map (fun c -> c.Id))

                  Expect.equal bodyChildren (Some [ "p" ]) "inner wired into the slot"
              | Error e -> failtestf "unexpected %A" e

              // effect join law: pure ∘ clock = clock (componentwise widest)
              let joined = Function.composedEffect artw inner outer
              Expect.equal joined.Determinism Effect.clock "determinism joined to clock"
              Expect.equal joined.Host Pure "host stays pure"

          testCase "compose rejects a slot kind mismatch"
          <| fun _ ->
              match Function.compose artw "tpl/s" (RNode.leaf "tb" "table" "") (template ()) with
              | Error(SlotKindMismatch("tpl/s", "para", "table")) -> ()
              | other -> failtestf "expected SlotKindMismatch, got %A" other

          testCase "hygiene — two same-named holes bind independently by address"
          <| fun _ ->
              let args =
                  Map.ofList [ "root/g1/gx1", ValueArg "first"; "root/g2/gx2", ValueArg "second" ]

              match Function.apply artw args (twoSameName ()) with
              | Ok r ->
                  Expect.equal (valueOf "gx1" r) (Some "first") "first hole"
                  Expect.equal (valueOf "gx2" r) (Some "second") "second hole — no capture"
              | Error e -> failtestf "unexpected %A" e

          testCase "totality — a bounded repeat is total, an unbounded one is not"
          <| fun _ ->
              let bounded =
                  RNode.node "root" "doc" [ RNode.hole "r" "region" "rep" (RepeatHole(IntRange(0, 5))) ]

              let unbounded =
                  RNode.node "root" "doc" [ RNode.hole "r" "region" "rep" (RepeatHole AnyString) ]

              Expect.isTrue (Function.isTotal (Function.signature artw "b" bounded)) "bounded repeat is total"
              Expect.isFalse (Function.isTotal (Function.signature artw "u" unbounded)) "unbounded repeat is not total"

              match Function.apply artw Map.empty unbounded with
              | Error(NonTotal "root/r") -> ()
              | other -> failtestf "expected NonTotal, got %A" other

          testCase "effect join is the componentwise widest"
          <| fun _ ->
              let clock =
                  { Host = Pure
                    Determinism = Effect.clock }

              let writes =
                  { Host = WritesHost
                    Determinism = Effect.deterministic }

              let j = Effect.join clock writes
              Expect.equal j.Host WritesHost "host widened"
              Expect.equal j.Determinism Effect.clock "determinism widened"
              Expect.isTrue (Effect.covers j clock) "join covers each input"
              Expect.isFalse (Effect.covers Effect.pureDeterministic clock) "pure does not cover clock"

          // ---- Phase 319: the determinism axis is a SET of factors, joined by union ----

          testCase "determinism is a set: join keeps every factor, and a declaration covers only the factors it names"
          <| fun _ ->
              let only d = { Host = Pure; Determinism = d }

              let clockRandom = Effect.join (only Effect.clock) (only Effect.random)

              Expect.equal
                  clockRandom.Determinism
                  (Set.ofList [ ClockFactor; RandomFactor ])
                  "join of clock and random names BOTH — the chain this replaced kept only the maximum"

              Expect.isTrue (Effect.covers clockRandom (only Effect.clock)) "the union covers the clock read"
              Expect.isTrue (Effect.covers clockRandom (only Effect.random)) "and the random read"

              Expect.isFalse
                  (Effect.covers (only Effect.network) (only Effect.clock))
                  "a network-only declaration does NOT cover a clock read — it once did, as the maximum"

              Expect.isFalse
                  (Effect.covers (only Effect.random) (only clockRandom.Determinism))
                  "a random-only declaration does not cover a body that also reads the clock"

              Expect.equal
                  (Effect.join Effect.pureDeterministic (only Effect.network))
                  (only Effect.network)
                  "deterministic (the empty set) is the identity of the join"

          testCase
              "the canonical determinism label names the members in fixed order and is inverted only on canonical labels"
          <| fun _ ->
              let labels =
                  [ Effect.deterministic, "deterministic"
                    Effect.clock, "clock"
                    Effect.random, "random"
                    Effect.network, "network"
                    Set.ofList [ ClockFactor; RandomFactor ], "clock+random"
                    Set.ofList [ ClockFactor; NetworkFactor ], "clock+network"
                    Set.ofList [ RandomFactor; NetworkFactor ], "random+network"
                    Set.ofList [ ClockFactor; RandomFactor; NetworkFactor ], "clock+random+network" ]

              for set, label in labels do
                  Expect.equal (Effect.determinismTag set) label (sprintf "%A renders canonically" set)
                  Expect.equal (Effect.tryDeterminismOfTag label) (Some set) (sprintf "%s names its set" label)

              // a set has ONE spelling: every reordering, repetition and guess is refused
              for bad in
                  [ "random+clock"
                    "network+random+clock"
                    "clock+clock"
                    "deterministic+clock"
                    "clock+deterministic"
                    ""
                    "+"
                    "clock+"
                    "Clock"
                    "clock,random"
                    "wall" ] do
                  Expect.isNone (Effect.tryDeterminismOfTag bad) (sprintf "%A is not a canonical label" bad)

          testCase "the capability codec round-trips a multi-factor determinism and refuses a non-canonical label"
          <| fun _ ->
              let sg: Signature =
                  { Name = "f"
                    Holes = []
                    Effect =
                      { Host = ReadsHost
                        Determinism = Set.ofList [ ClockFactor; RandomFactor ] } }

              let cap = Capability.create "f" sg Server
              let wire = CapabilityCodec.encode cap
              Expect.stringContains wire "\"determinism\":\"clock+random\"" "the set is written as its canonical label"

              match CapabilityCodec.decode wire with
              | Ok c2 -> Expect.equal c2 cap "a multi-factor capability round-trips"
              | Error m -> failtestf "decode failed: %s" m

              let reordered = wire.Replace("clock+random", "random+clock")
              Expect.notEqual reordered wire "the label was reordered"

              match CapabilityCodec.decode reordered with
              | Error _ -> ()
              | Ok _ -> failtest "a reordered label is not canonical and must be refused"

          // ---- Phase 30: invocable Capability + registry ----

          testCase "Capability.create derives Determinism from the signature effect"
          <| fun _ ->
              let sg: Signature =
                  { Name = "infer"
                    Holes =
                      [ { Addr = "p"
                          Name = "p"
                          Kind = "value"
                          Space = Some(IntRange(0, 10))
                          Slot = None
                          Action = None
                          Required = true } ]
                    Effect =
                      { Host = ReadsHost
                        Determinism = Effect.network } }

              let cap = Capability.create "score" sg Server
              Expect.equal cap.Determinism Effect.network "determinism mirrors the signature effect"
              Expect.equal (Capability.determinismTag cap) "network" "tag matches the Phase 27 label"

          testCase "validateArgs accepts in-space and names every refusal"
          <| fun _ ->
              let sg: Signature =
                  { Name = "f"
                    Holes =
                      [ { Addr = "n"
                          Name = "n"
                          Kind = "value"
                          Space = Some(IntRange(1, 5))
                          Slot = None
                          Action = None
                          Required = true }
                        { Addr = "slot"
                          Name = "s"
                          Kind = "slot"
                          Space = None
                          Slot = Some "para"
                          Action = None
                          Required = false } ]
                    Effect = Effect.pureDeterministic }

              let cap = Capability.create "f" sg ClientDeclarative
              Expect.equal (Capability.validateArgs cap [ "n", "3" ]) (Ok()) "in-space accepted"

              match Capability.validateArgs cap [ "n", "9" ] with
              | Error(ArgOutOfSpace("n", _, "9")) -> ()
              | other -> failtestf "expected ArgOutOfSpace, got %A" other

              match Capability.validateArgs cap [ "zzz", "3" ] with
              | Error(UnknownArg("zzz", _)) -> ()
              | other -> failtestf "expected UnknownArg, got %A" other

              match Capability.validateArgs cap [ "slot", "x" ] with
              | Error(UninvocableArg "slot") -> ()
              | other -> failtestf "expected UninvocableArg, got %A" other

              match Capability.validateArgs cap [] with
              | Error(RequiredArgsUnbound [ "n" ]) -> ()
              | other -> failtestf "expected RequiredArgsUnbound, got %A" other

          // Phase 229 — a tree-typed slot has a value space (`SlotTree`), so a capability over a
          // slotted artifact is invocable at the scalar seam. The signature is DERIVED from the
          // reference template (title, count, and a slot constrained to "para"), never hand-built.
          testCase "a capability over a slotted artifact registers, enumerates and dispatches (Phase 229)"
          <| fun _ ->
              let sg = Function.signature artw "tpl" (template ())

              let slot = sg.Holes |> List.find (fun h -> h.Addr = "tpl/s")
              Expect.equal slot.Space (Some(SlotTree(Some "para"))) "the slot is entered with its tree space"
              Expect.isTrue slot.Required "and stays required"

              let cap = Capability.create "tpl-cap" sg Server

              let reg =
                  match Registry.register cap Registry.empty with
                  | Ok r -> r
                  | Error e -> failtestf "a slotted capability did not register: %A" e

              Expect.equal (Registry.enumerate reg |> List.map (fun c -> c.Id)) [ "tpl-cap" ] "enumerated"

              let para = """{"kind":"para","text":"hi"}"""

              let args s =
                  [ "tpl/t", "Hello"; "tpl/c", "5"; "tpl/s", s ]

              Expect.equal
                  (Registry.dispatch reg "tpl-cap" (args para) (fun _ () -> Ready "ran"))
                  (Ok(Ready "ran"))
                  "a conforming slot argument dispatches"

              let ran = ref false

              match
                  Registry.dispatch reg "tpl-cap" (args """{"kind":"heading"}""") (fun _ () ->
                      ran.Value <- true
                      Ready "ran")
              with
              | Error(ArgOutOfSpace("tpl/s", SlotTree(Some "para"), """{"kind":"heading"}""")) ->
                  Expect.isFalse ran.Value "refused before the body"
              | other -> failtestf "expected ArgOutOfSpace naming the slot's constraint, got %A" other

              for notATree in [ "hi"; "42"; "[1,2]"; """{"text":"no kind"}"""; """{"kind":7}"""; "{" ] do
                  match Capability.validateArgs cap (args notATree) with
                  | Error(UninvocableArg "tpl/s") -> ()
                  | other -> failtestf "a slot bound to %s: expected UninvocableArg, got %A" notATree other

              // an unconstrained slot takes a tree of any kind, and still no scalar
              Expect.isTrue (Space.validate (SlotTree None) """{"kind":"anything"}""") "any kind"
              Expect.isFalse (Space.validate (SlotTree None) "anything") "no scalar"
              Expect.equal (Space.slotKindOf """{"kind":"para"}""") (Some "para") "the reader"

          // Phase 229's wire promise: the slot's space is DERIVED from its constraint, so the tool
          // schema, the capability codec and the pack fingerprint of a slotted signature are the
          // bytes they were before 229 — pinned against the literal pre-229 schema — and a pre-229
          // document decodes to the post-229 signature.
          testCase "a slotted signature's wire bytes and fingerprint are unchanged by Phase 229"
          <| fun _ ->
              let derived = Function.signature artw "tpl" (template ())

              // the pre-229 shape of the same signature: the slot entered spaceless
              let pre229 =
                  { derived with
                      Holes =
                          derived.Holes
                          |> List.map (fun h -> if h.Kind = "slot" then { h with Space = None } else h) }

              let schema = Json.render (Function.toSchema derived)

              Expect.equal
                  schema
                  """{"kind":"signature","name":"tpl","effect":{"host":"pure","determinism":"deterministic"},"holes":[{"addr":"tpl/t","name":"title","kind":"value","required":true,"space":{"kind":"stringLen","minLength":1,"maxLength":20}},{"addr":"tpl/c","name":"count","kind":"value","required":true,"space":{"kind":"intRange","min":0,"max":10}},{"addr":"tpl/s","name":"body","kind":"slot","required":true,"slotKind":"para"}],"required":["tpl/t","tpl/c","tpl/s"]}"""
                  "the literal pre-229 tool schema"

              Expect.equal schema (Json.render (Function.toSchema pre229)) "toSchema bytes"

              Expect.equal
                  (ContentPack.signatureFingerprint derived)
                  (ContentPack.signatureFingerprint pre229)
                  "the pack fingerprint"

              let enc sg =
                  CapabilityCodec.encode (Capability.create "tpl-cap" sg Server)

              Expect.equal (enc derived) (enc pre229) "capability codec bytes"
              Expect.isFalse ((enc derived).Contains "slotTree") "the derived space is not written"

              match CapabilityCodec.decode (enc pre229) with
              | Ok c -> Expect.equal c.Signature derived "a pre-229 document decodes to the post-229 signature"
              | Error e -> failtestf "decode failed: %s" e

              // a space that says something the entry does not IS written, and round-trips
              let odd =
                  { derived with
                      Holes =
                          derived.Holes
                          |> List.map (fun h ->
                              if h.Kind = "slot" then
                                  { h with
                                      Space = Some(SlotTree(Some "other")) }
                              else
                                  h) }

              match CapabilityCodec.decode (enc odd) with
              | Ok c -> Expect.equal c.Signature odd "an explicit slot space round-trips"
              | Error e -> failtestf "decode failed: %s" e

          testCase "invocationKey is arg-order-independent but arg-value-sensitive"
          <| fun _ ->
              let sg: Signature =
                  { Name = "k"
                    Holes = []
                    Effect = Effect.pureDeterministic }

              let cap = Capability.create "k" sg Server
              let k1 = Capability.invocationKey cap [ "a", "1"; "b", "2" ]
              let k2 = Capability.invocationKey cap [ "b", "2"; "a", "1" ]
              let k3 = Capability.invocationKey cap [ "a", "1"; "b", "3" ]
              Expect.equal k1 k2 "arg order does not change the key"
              Expect.notEqual k1 k3 "a different arg value changes the key"

          testCase "registry: register is additive, dispatch is default-deny, enumerate is stable"
          <| fun _ ->
              let mk id =
                  Capability.create
                      id
                      { Name = id
                        Holes = []
                        Effect = Effect.pureDeterministic }
                      Server

              let reg =
                  Registry.empty
                  |> Registry.register (mk "zebra")
                  |> Result.bind (Registry.register (mk "apple"))
                  |> function
                      | Ok r -> r
                      | Error e -> failtestf "register failed: %A" e

              Expect.equal
                  (Registry.enumerate reg |> List.map (fun c -> c.Id))
                  [ "apple"; "zebra" ]
                  "enumerate id-sorted"

              match Registry.register (mk "apple") reg with
              | Error(DuplicateCapability "apple") -> ()
              | other -> failtestf "expected DuplicateCapability, got %A" other

              match Registry.dispatch reg "ghost" [] (fun _ () -> Ready 1) with
              | Error(NoSuchCapability("ghost", _)) -> ()
              | other -> failtestf "expected NoSuchCapability, got %A" other

              Expect.equal
                  (Registry.dispatch reg "apple" [] (fun _ () -> Ready 42))
                  (Ok(Ready 42))
                  "registered id dispatches, settled"

              // Phase 210 — the envelope's other two cases on the seam: a pending body stays
              // pending inside an `Ok`, and a failing one is the typed `BodyFailed`, never
              // `Ok(Failed _)`.
              Expect.equal
                  (Registry.dispatch reg "apple" [] (fun _ () -> Pending))
                  (Ok Pending: Result<Deferred<int>, InvokeError>)
                  "a pending body stays pending"

              Expect.equal
                  (Registry.dispatch reg "apple" [] (fun _ () -> Failed "boom"))
                  (Error(BodyFailed "boom"): Result<Deferred<int>, InvokeError>)
                  "a failing body is the typed BodyFailed"

          testCase "a capability declaration + invocation round-trips through the codec"
          <| fun _ ->
              let sg: Signature =
                  { Name = "predict"
                    Holes =
                      [ { Addr = "x"
                          Name = "x"
                          Kind = "value"
                          Space = Some(FloatRange(0.0, 1.0))
                          Slot = None
                          Action = None
                          Required = true } ]
                    Effect =
                      { Host = ReadsHost
                        Determinism = Effect.random } }

              let cap = Capability.create "predict" sg (ClientIsland Pyodide)

              match CapabilityCodec.decode (CapabilityCodec.encode cap) with
              | Ok c2 -> Expect.equal c2 cap "capability declaration round-trips"
              | Error m -> failtestf "decode failed: %s" m

              match CapabilityCodec.decodeInvocation (CapabilityCodec.encodeInvocation "predict" [ "x", "0.5" ]) with
              | Ok("predict", [ "x", "0.5" ]) -> ()
              | other -> failtestf "invocation round-trip: %A" other

          // ---- Phase 44: capability determinism field cross-check ----

          testCase "decode rejects a capability whose determinism tag disagrees with its signature effect"
          <| fun _ ->
              let sg: Signature =
                  { Name = "f"
                    Holes = []
                    Effect =
                      { Host = ReadsHost
                        Determinism = Effect.random } }

              let cap = Capability.create "f" sg Server
              let wire = CapabilityCodec.encode cap
              // the honest wire carries a top-level "determinism":"random" (the capability object leads
              // with $type:capability); tamper only that one, leaving the nested signature effect intact
              Expect.stringContains wire "\"determinism\":\"random\"" "encode writes the signature-derived tag"

              let tampered =
                  wire.Replace(
                      "\"$type\":\"capability\",\"determinism\":\"random\"",
                      "\"$type\":\"capability\",\"determinism\":\"deterministic\""
                  )

              Expect.notEqual tampered wire "the top-level determinism tag was tampered"

              match CapabilityCodec.decode tampered with
              | Error msg -> Expect.stringContains msg "determinism disagrees" "named cross-check error"
              | Ok _ -> failtest "expected the disagreeing determinism tag to be rejected"

              // the honest payload still round-trips
              match CapabilityCodec.decode wire with
              | Ok c2 -> Expect.equal c2 cap "an agreeing payload decodes unchanged"
              | Error m -> failtestf "honest decode failed: %s" m ]

// ---- Phase 318: action holes — typed dispatch as host-side hole-binding ----

/// The handler-effect ceiling the button's `onClick` declares: it writes the host, deterministically.
let private writesHost =
    { Host = WritesHost
      Determinism = Effect.deterministic }

/// A button artifact: a data `label` hole (filled by the AI) + an `onClick` action hole (a dispatch
/// slot a human binds a handler to). The tree stays pure — the action hole carries no handler and no
/// `'Msg`, only the declared effect ceiling of the handler that will fill it.
let private buttonTpl () =
    RNode.node
        "btn"
        "button"
        [ RNode.hole "lbl" "field" "label" (ValueHole(StringLen(1, 20)))
          RNode.hole "click" "event" "onClick" (ActionHole writesHost) ]

[<Tests>]
let actionHoleTests =
    testList
        "Function.actionHoles"
        [ testCase "signature surfaces an action hole with its effect ceiling, non-required on the data axis"
          <| fun _ ->
              let sg = Function.signature artw "btn" (buttonTpl ())
              let action = sg.Holes |> List.find (fun h -> h.Kind = "action")
              Expect.equal action.Addr "btn/click" "absolute address"
              Expect.equal action.Name "onClick" "name"
              Expect.equal action.Action (Some writesHost) "effect ceiling surfaced"
              Expect.isFalse action.Required "an action hole is non-required on the data-binding axis"

          testCase "apply binds the data hole and ignores the action hole — the artifact stays apply-able"
          <| fun _ ->
              // strict apply must NOT demand a value/slot arg for the dispatch slot.
              match Function.apply artw (Map.ofList [ "btn/lbl", ValueArg "Go" ]) (buttonTpl ()) with
              | Ok r ->
                  Expect.equal (valueOf "lbl" r) (Some "Go") "label bound"
                  Expect.isSome (holeAt "click" r) "action hole untouched by data binding"
              | Error e -> failtestf "unexpected %A" e

          testCase "toSchema includes the action hole with its actionEffect"
          <| fun _ ->
              let json =
                  Json.render (Function.toSchema (Function.signature artw "btn" (buttonTpl ())))

              Expect.stringContains json "\"kind\":\"action\"" "action kind projected"

              Expect.stringContains
                  json
                  "\"actionEffect\":{\"host\":\"writesHost\",\"determinism\":\"deterministic\"}"
                  "effect ceiling projected"

          testCase "toJsonSchema lists actions under x-actions, NOT in properties/required"
          <| fun _ ->
              let json =
                  Json.render (Function.toJsonSchema (Function.signature artw "btn" (buttonTpl ())))
              // the data hole is the only argument the AI fills
              Expect.stringContains
                  json
                  "\"properties\":{\"btn/lbl\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":20}}"
                  "only the data hole is a property"

              Expect.stringContains json "\"required\":[\"btn/lbl\"]" "action excluded from required"
              // the dispatch slot travels as the host's hole-binding surface
              Expect.stringContains
                  json
                  "\"x-actions\":[{\"addr\":\"btn/click\",\"name\":\"onClick\",\"effect\":{\"host\":\"writesHost\",\"determinism\":\"deterministic\"}}]"
                  "action surfaced under x-actions"

          testCase "a data-only signature emits no x-actions (byte-identical to before)"
          <| fun _ ->
              let json =
                  Json.render (Function.toJsonSchema (Function.signature artw "tpl" (template ())))

              Expect.isFalse (json.Contains "x-actions") "no x-actions key when no action holes"

          testCase "bindHandlers binds a typed handler table validated against the signature"
          <| fun _ ->
              let handlers =
                  Map.ofList
                      [ "btn/click",
                        { Handler = (fun () -> "clicked")
                          Effect = writesHost } ]

              match Function.bindHandlers artw handlers (buttonTpl ()) with
              | Ok table ->
                  Expect.equal
                      (table.Handlers |> Map.toList |> List.map fst)
                      [ "btn/click" ]
                      "bound by absolute address"
              | Error e -> failtestf "unexpected %A" e

          testCase "bindHandlers rejects an unbound action hole (default-deny — a dead dispatch slot)"
          <| fun _ ->
              match
                  Function.bindHandlers artw (Map.empty: Map<string, HandlerBinding<unit -> string>>) (buttonTpl ())
              with
              | Error(RequiredActionsUnbound [ "btn/click" ]) -> ()
              | other -> failtestf "expected RequiredActionsUnbound, got %A" other

          testCase "bindHandlers rejects a handler whose effect exceeds the declared ceiling"
          <| fun _ ->
              let handlers =
                  Map.ofList
                      [ "btn/click",
                        { Handler = (fun () -> "x")
                          Effect =
                            { Host = WritesHost
                              Determinism = Effect.network } } ]

              match Function.bindHandlers artw handlers (buttonTpl ()) with
              | Error(HandlerEffectExceedsCeiling("btn/click", ceiling, handler)) ->
                  Expect.equal ceiling writesHost "names the declared ceiling"
                  Expect.equal handler.Determinism Effect.network "names the over-wide handler effect"
              | other -> failtestf "expected HandlerEffectExceedsCeiling, got %A" other

          testCase "bindHandlers rejects a handler on a non-action hole, and on an unknown address"
          <| fun _ ->
              let onDataHole =
                  Map.ofList
                      [ "btn/click",
                        { Handler = (fun () -> "x")
                          Effect = writesHost }
                        "btn/lbl",
                        { Handler = (fun () -> "y")
                          Effect = Effect.pureDeterministic } ]

              match Function.bindHandlers artw onDataHole (buttonTpl ()) with
              | Error(NotAnActionHole "btn/lbl") -> ()
              | other -> failtestf "expected NotAnActionHole, got %A" other

              let unknown =
                  Map.ofList
                      [ "btn/click",
                        { Handler = (fun () -> "x")
                          Effect = writesHost }
                        "btn/zzz",
                        { Handler = (fun () -> "y")
                          Effect = writesHost } ]

              match Function.bindHandlers artw unknown (buttonTpl ()) with
              | Error(UnknownActionAddr("btn/zzz", declaredActions)) ->
                  Expect.equal declaredActions [ "btn/click" ] "enumerates the declared action holes"
              | other -> failtestf "expected UnknownActionAddr, got %A" other

          testCase "an action-bearing signature round-trips through the capability codec"
          <| fun _ ->
              let sg = Function.signature artw "btn" (buttonTpl ())
              let cap = Capability.create "btn" sg Server

              match CapabilityCodec.decode (CapabilityCodec.encode cap) with
              | Ok c2 -> Expect.equal c2 cap "action-bearing capability declaration round-trips"
              | Error m -> failtestf "decode failed: %s" m ]

// ---- Phase 47: higher-order cross-domain composition (composeAcross) ----

[<Tests>]
let composeAcrossTests =
    // The reference is a single domain, so cross-witness composition is exercised at 'A = 'B = RNode
    // with `embed = id`; this still drives the full generic `composeAcross` path (two-witness threading,
    // the embed parameter, the cross-boundary totality guard, and the effect-join surface).
    testList
        "Function.composeAcross"
        [ testCase "wires a 'B-function into an 'A-function's slot across witnesses"
          <| fun _ ->
              let inner = RNode.leaf "p" "para" "composed"

              match Function.composeAcross artw artw id "tpl/s" inner (template ()) with
              | Ok r ->
                  let bodyChildren =
                      Tree.tryFind nodew idw "s" r
                      |> Option.map (fun s -> s.Children |> List.map (fun c -> c.Id))

                  Expect.equal bodyChildren (Some [ "p" ]) "inner wired into the slot across the boundary"
              | Error e -> failtestf "unexpected %A" e

          testCase "carries the effect-signature join across the boundary (Fork 3)"
          <| fun _ ->
              let inner =
                  { RNode.leaf "p" "para" "x" with
                      Eff =
                          { Host = ReadsHost
                            Determinism = Effect.clock } }

              let joined = Function.composedEffectAcross artw artw inner (template ())
              Expect.equal joined.Host ReadsHost "host widened to the inner's"
              Expect.equal joined.Determinism Effect.clock "determinism widened to the inner's"
              Expect.isTrue (Effect.covers joined (artw.Effect(template ()))) "join covers the outer"
              Expect.isTrue (Effect.covers joined (artw.Effect inner)) "join covers the inner"

          testCase "rejects a slot kind mismatch (the embedded inner's kind is checked)"
          <| fun _ ->
              match Function.composeAcross artw artw id "tpl/s" (RNode.leaf "tb" "table" "") (template ()) with
              | Error(SlotKindMismatch("tpl/s", "para", "table")) -> ()
              | other -> failtestf "expected SlotKindMismatch, got %A" other

          testCase "rejects an unknown slot address, and a non-slot address"
          <| fun _ ->
              match Function.composeAcross artw artw id "tpl/nope" (RNode.leaf "p" "para" "x") (template ()) with
              | Error(UnknownHoleAddr("tpl/nope", declared)) ->
                  Expect.contains declared "tpl/s" "enumerates declared holes"
              | other -> failtestf "expected UnknownHoleAddr, got %A" other

              // tpl/t is a value hole, not a slot
              match Function.composeAcross artw artw id "tpl/t" (RNode.leaf "p" "para" "x") (template ()) with
              | Error(NotASlot "tpl/t") -> ()
              | other -> failtestf "expected NotASlot, got %A" other

          testCase "totality — rejected (never run) when the OUTER carries an unbounded repeat (Fork 1)"
          <| fun _ ->
              let outer =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.hole "r" "region" "rep" (RepeatHole AnyString)
                        RNode.hole "s" "region" "body" (SlotHole(Some "para")) ]

              match Function.composeAcross artw artw id "root/s" (RNode.leaf "p" "para" "x") outer with
              | Error(NonTotal "root/r") -> ()
              | other -> failtestf "expected NonTotal, got %A" other

          testCase "totality — rejected (never run) when the INNER carries an unbounded repeat (Fork 1)"
          <| fun _ ->
              let inner =
                  RNode.node "ir" "para" [ RNode.hole "irr" "region" "rep" (RepeatHole AnyString) ]

              match Function.composeAcross artw artw id "tpl/s" inner (template ()) with
              | Error(NonTotal "ir/irr") -> ()
              | other -> failtestf "expected NonTotal, got %A" other ]

// ---- Phase 49: memoised application (applyMemo / applyMemoComposed) ----

/// A full valid param-set for the reference `template ()`.
let private fullArgs count =
    Map.ofList
        [ "tpl/t", ValueArg "Hello"
          "tpl/c", ValueArg count
          "tpl/s", SlotArg(RNode.leaf "p" "para" "hi") ]

[<Tests>]
let memoTests =
    testList
        "Function.memo"
        [ testCase "applyMemo: a miss computes + stores the direct apply, a re-apply is a hit"
          <| fun _ ->
              let args = fullArgs "5"
              let direct = Function.apply artw args (template ())

              match Function.applyMemo artw encNode args (template ()) Memo.empty with
              | Ok(r1, c1) ->
                  Expect.equal (Ok r1) direct "miss returns exactly what apply produces"
                  Expect.equal c1.Misses 1 "one miss"
                  Expect.equal c1.Hits 0 "no hit on the miss"
                  Expect.equal (Memo.count c1) 1 "one entry stored"

                  match Function.applyMemo artw encNode args (template ()) c1 with
                  | Ok(r2, c2) ->
                      Expect.equal r2 r1 "the hit serves the same tree"
                      Expect.equal c2.Hits 1 "the re-apply is a cache hit"
                      Expect.equal (Memo.count c2) 1 "no new entry on a hit"
                  | Error e -> failtestf "unexpected %A" e
              | Error e -> failtestf "unexpected %A" e

          testCase "applyMemo: a changed param-set misses (the original still re-hits)"
          <| fun _ ->
              match Function.applyMemo artw encNode (fullArgs "5") (template ()) Memo.empty with
              | Ok(_, c1) ->
                  match Function.applyMemo artw encNode (fullArgs "6") (template ()) c1 with
                  | Ok(_, c2) ->
                      Expect.equal c2.Misses 2 "the changed param-set is a second miss"
                      Expect.equal c2.Hits 0 "no hit on the changed param-set"

                      match Function.applyMemo artw encNode (fullArgs "5") (template ()) c2 with
                      | Ok(_, c3) -> Expect.equal c3.Hits 1 "the original param-set still re-hits"
                      | Error e -> failtestf "unexpected %A" e
                  | Error e -> failtestf "unexpected %A" e
              | Error e -> failtestf "unexpected %A" e

          testCase "applyMemo: an effecting function is bypassed — computed directly, never cached (Fork 3)"
          <| fun _ ->
              let effFn =
                  { template () with
                      Eff =
                          { Host = Pure
                            Determinism = Effect.clock } }

              let args = fullArgs "5"
              let direct = Function.apply artw args effFn

              match Function.applyMemo artw encNode args effFn Memo.empty with
              | Ok(r1, c1) ->
                  Expect.equal (Ok r1) direct "bypass still returns the correct result"
                  Expect.isTrue (Map.isEmpty c1.Entries) "nothing stored for an effecting function"
                  Expect.equal c1.Bypasses 1 "the bypass is counted"
                  Expect.equal c1.Hits 0 "never a hit"

                  // even a re-apply never serves it from cache
                  match Function.applyMemo artw encNode args effFn c1 with
                  | Ok(_, c2) ->
                      Expect.equal c2.Hits 0 "still never served from cache"
                      Expect.isTrue (Map.isEmpty c2.Entries) "still nothing stored"
                  | Error e -> failtestf "unexpected %A" e
              | Error e -> failtestf "unexpected %A" e

          // Phase 290 — the key is INJECTIVE: the two aliases the bare-join key admitted must miss.
          testCase
              "applyMemo: a value spelling the separator and the next binding is not served the two-binding result (Phase 290)"
          <| fun _ ->
              // `{tpl/c = "5\u0001tpl/s=s…"; tpl/t = …}` spelt, under the bare join the key used
              // until Phase 290, the same pre-image as `{tpl/c = "5"; tpl/s = …; tpl/t = …}` — so a
              // cache holding the latter served its tree for the former, whose own `apply` is a
              // REFUSAL (`tpl/s` is a declared hole left unbound).
              let full = fullArgs "5"

              let forged =
                  full
                  |> Map.remove "tpl/s"
                  |> Map.add
                      "tpl/c"
                      (ValueArg(
                          "5"
                          + Hash.foldSep
                          + "tpl/s=s"
                          + Tree.encodePreimage nodew encNode (RNode.leaf "p" "para" "hi")
                      ))

              match Function.applyMemo artw encNode full (template ()) Memo.empty with
              | Ok(_, c1) ->
                  let direct = Function.apply artw forged (template ())
                  Expect.isError direct "the forged set is refused by apply — a hole is unbound"

                  match Function.applyMemo artw encNode forged (template ()) c1 with
                  | Ok(_, _) -> failtest "the forged set was SERVED from the cache"
                  | Error e -> Expect.equal direct (Error e) "applyMemo answers exactly what apply answers"
              | Error e -> failtestf "unexpected %A" e

          testCase "applyMemo: two functions one MoveNode apart — one preorder — key distinctly (Phase 290)"
          <| fun _ ->
              // `MoveNode(s, c)` re-nests the slot hole under the count hole: the preorder is
              // unchanged (tpl, t, c, s), so under the arity-free fold both functions had ONE
              // `encodeHash` and one memo key — and the moved hole's address changed, so the cached
              // result of the first is the wrong answer for the second (whose own `apply` refuses
              // `tpl/s` as an unknown address).
              let flat = template ()

              let nested =
                  match Ops.apply nodew idw (MoveNode("s", "c")) flat with
                  | Ok t -> t
                  | Error e -> failtestf "the move was refused: %A" e

              Expect.equal
                  (Tree.preorder nodew nested |> List.map (fun n -> n.Id))
                  (Tree.preorder nodew flat |> List.map (fun n -> n.Id))
                  "the premise: one preorder"

              Expect.notEqual
                  (Tree.encodeHash nodew encNode flat)
                  (Tree.encodeHash nodew encNode nested)
                  "distinct digests"

              let args = fullArgs "5"

              match Function.applyMemo artw encNode args flat Memo.empty with
              | Ok(_, c1) ->
                  let direct = Function.apply artw args nested
                  Expect.isError direct "the moved hole's address is unknown to the nested function"

                  match Function.applyMemo artw encNode args nested c1 with
                  | Ok(_, _) -> failtest "the nested function was SERVED the flat function's tree"
                  | Error e -> Expect.equal direct (Error e) "applyMemo answers exactly what apply answers"
              | Error e -> failtestf "unexpected %A" e

          // subtree-level memo: a single-hole edit re-derives only the affected path.
          testCase "applyMemoComposed: editing only the OUTER hole reuses the unchanged inner (a hit)"
          <| fun _ ->
              let innerFn () =
                  RNode.node "in" "para" [ RNode.hole "iv" "field" "x" (ValueHole AnyString) ]

              let innerArgs = Map.ofList [ "in/iv", ValueArg "deep" ]
              let inners () = [ "tpl/s", innerFn (), innerArgs ]

              let outerArgs c =
                  Map.ofList [ "tpl/t", ValueArg "Hi"; "tpl/c", ValueArg c ]

              match Function.applyMemoComposed artw encNode (inners ()) (outerArgs "3") (template ()) Memo.empty with
              | Ok(r1, c1) ->
                  Expect.equal c1.Misses 2 "first run: inner + outer both miss"
                  Expect.equal c1.Hits 0 "no hits on the first run"
                  // the inner subtree was wired into the body slot, and bound
                  let bodyKid =
                      Tree.tryFind nodew idw "s" r1
                      |> Option.bind (fun s -> s.Children |> List.tryHead)
                      |> Option.bind (fun inn -> inn.Children |> List.tryHead)
                      |> Option.map (fun v -> v.Value)

                  Expect.equal bodyKid (Some "deep") "the inner hole was bound under the slot"

                  // edit ONLY the outer count hole; the inner is unchanged.
                  match Function.applyMemoComposed artw encNode (inners ()) (outerArgs "4") (template ()) c1 with
                  | Ok(_, c2) ->
                      Expect.equal c2.Hits 1 "the unchanged inner sub-function is served from cache"
                      Expect.equal c2.Misses 3 "only the outer (affected path) re-derives"
                  | Error e -> failtestf "unexpected %A" e
              | Error e -> failtestf "unexpected %A" e

          testCase "applyMemoComposed: editing the INNER hole re-derives the affected path (no reuse)"
          <| fun _ ->
              let innerFn () =
                  RNode.node "in" "para" [ RNode.hole "iv" "field" "x" (ValueHole AnyString) ]

              let outerArgs = Map.ofList [ "tpl/t", ValueArg "Hi"; "tpl/c", ValueArg "3" ]

              let innersWith v =
                  [ "tpl/s", innerFn (), Map.ofList [ "in/iv", ValueArg v ] ]

              match Function.applyMemoComposed artw encNode (innersWith "deep") outerArgs (template ()) Memo.empty with
              | Ok(_, c1) ->
                  match Function.applyMemoComposed artw encNode (innersWith "other") outerArgs (template ()) c1 with
                  | Ok(_, c2) ->
                      Expect.equal c2.Hits c1.Hits "an inner edit yields no cache reuse"
                      Expect.equal c2.Misses 4 "both the inner and the (content-changed) outer re-derive"
                  | Error e -> failtestf "unexpected %A" e
              | Error e -> failtestf "unexpected %A" e

          // ---- Phase 32: Deferred async-result envelope ----

          testCase "Deferred map / bind / toResult behave (Ready lifts; Pending/Failed propagate)"
          <| fun _ ->
              Expect.equal (Deferred.map ((+) 1) (Ready 41)) (Ready 42) "map over Ready"
              Expect.equal (Deferred.map ((+) 1) Pending) Pending "map propagates Pending"
              Expect.equal (Deferred.map ((+) 1) (Failed "x")) (Failed "x") "map propagates Failed"
              Expect.equal (Deferred.bind (fun v -> Ready(v * 2)) (Ready 21)) (Ready 42) "bind over Ready"
              Expect.equal (Deferred.bind (fun v -> Ready(v * 2)) (Failed "e")) (Failed "e") "bind propagates Failed"
              Expect.equal (Deferred.toResult (Ready 7)) (Ok 7) "toResult Ready → Ok"
              Expect.equal (Deferred.toResult (Failed "boom")) (Error "boom") "toResult Failed → Error"
              Expect.equal (Deferred.toResult (Pending: Deferred<int>)) (Error "pending") "toResult Pending → Error"

          testCase "Deferred round-trips the wire for all three cases"
          <| fun _ ->
              let encInt (n: int) : JVal = JInt n

              let decInt =
                  function
                  | JInt i -> Ok i
                  | _ -> Error "not int"

              for d in [ Pending; Ready 99; Failed "nope" ] do
                  match CapabilityCodec.decodeDeferred decInt (CapabilityCodec.encodeDeferred encInt d) with
                  | Ok d2 -> Expect.equal d2 d (sprintf "round-trip %A" d)
                  | Error m -> failtestf "decode failed for %A: %s" d m

          testCase "deferredLaws certify round-trip + combinators + Ready replay (Phase 32)"
          <| fun _ ->
              let results = Conformance.deferredLaws 4242 200
              Expect.equal (List.length results) 3 "round-trip + combinators + replay reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "deferredLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.deferredLaws 4242 200) results "same seed ⇒ identical report"

          // ---- Phase 35: serializable capability pipeline (capability-DAG) ----

          testCase "CapabilityPipeline type-checks a well-typed DAG and names an ill-typed edge"
          <| fun _ ->
              let prodSig: Signature =
                  { Name = "prod"
                    Holes = []
                    Effect = Effect.pureDeterministic }

              let consSig: Signature =
                  { Name = "cons"
                    Holes =
                      [ { Addr = "x"
                          Name = "x"
                          Kind = "value"
                          Space = Some(IntRange(0, 100))
                          Slot = None
                          Action = None
                          Required = true } ]
                    Effect = Effect.pureDeterministic }

              let reg =
                  CapabilityRegistry.empty
                  |> CapabilityRegistry.register (Capability.create "prod" prodSig BuildTime)
                  |> Result.bind (CapabilityRegistry.register (Capability.create "cons" consSig BuildTime))
                  |> function
                      | Ok r -> CapabilityLookup.ofRegistry r
                      | Error e -> failtestf "registry build failed: %A" e

              let good =
                  { Nodes =
                      [ Invoke("n1", "prod", IntRange(0, 100), [])
                        Invoke("n2", "cons", IntRange(0, 100), [ "x", FromNode "n1" ]) ] }

              Expect.equal (CapabilityPipeline.typeCheck reg good) (Ok()) "well-typed DAG passes"

              // an int arg fed by a string producer
              let bad =
                  { good with
                      Nodes =
                          [ Invoke("n1", "prod", AnyString, [])
                            Invoke("n2", "cons", IntRange(0, 100), [ "x", FromNode "n1" ]) ] }

              match CapabilityPipeline.typeCheck reg bad with
              | Error(EdgeTypeMismatch("n2", "x", "anyString", "int")) -> ()
              | other -> failtestf "expected EdgeTypeMismatch, got %A" other

              // an unregistered capability is default-deny
              let unreg = { Nodes = [ Invoke("n1", "ghost", IntRange(0, 100), []) ] }

              match CapabilityPipeline.typeCheck reg unreg with
              | Error(PipelineNoSuchCapability("ghost", _)) -> ()
              | other -> failtestf "expected PipelineNoSuchCapability, got %A" other

          testCase "CapabilityPipeline round-trips the wire"
          <| fun _ ->
              let p =
                  { Nodes =
                      [ Source("src", "sales-2026", AnyString)
                        Invoke("n1", "load", IntRange(0, 10), [ "p", Literal "3"; "q", FromNode "src" ]) ] }

              match CapabilityPipeline.decode (CapabilityPipeline.encode p) with
              | Ok p2 -> Expect.equal p2 p "pipeline round-trips"
              | Error m -> failtestf "decode failed: %s" m

          testCase "capabilityPipelineLaws certify type-check + round-trip + node replay (Phase 35)"
          <| fun _ ->
              let results = Conformance.capabilityPipelineLaws 4242 200

              Expect.equal
                  (List.length results)
                  7
                  "type-check + round-trip + replay + space relation + soundness + order + checked-first reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "capabilityPipelineLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.capabilityPipelineLaws 4242 200) results "same seed ⇒ identical report"

          testCase "eval walks the DAG resolving FromNode edges; evalFrom reuses clean branches (Phase 62)"
          <| fun _ ->
              let inc =
                  Capability.create
                      "inc"
                      { Name = "inc"
                        Holes =
                          [ { Addr = "x"
                              Name = "x"
                              Kind = "value"
                              Space = Some(IntRange(0, 1000))
                              Slot = None
                              Action = None
                              Required = true } ]
                        Effect = Effect.pureDeterministic }
                      Server

              let lookup =
                  CapabilityRegistry.empty
                  |> CapabilityRegistry.register inc
                  |> function
                      | Ok r -> CapabilityLookup.ofRegistry r
                      | Error e -> failtestf "registry build failed: %A" e

              // s1 → a, s2 → b : a change to s1 leaves the s2/b branch clean.
              let p: CapabilityPipeline =
                  { Nodes =
                      [ Source("s1", "r1", IntRange(0, 1000))
                        Source("s2", "r2", IntRange(0, 1000))
                        Invoke("a", "inc", IntRange(0, 1000), [ "x", FromNode "s1" ])
                        Invoke("b", "inc", IntRange(0, 1000), [ "x", FromNode "s2" ]) ] }

              let bodyWith (sv: Map<string, int>) (invoked: ResizeArray<string>) =
                  fun (node: PipelineNode) (args: (string * PipelineArg<int>) list) ->
                      invoked.Add(CapabilityPipeline.nodeId node)

                      match node with
                      | Source(id, _, _) -> Ok(Map.find id sv)
                      | Invoke _ ->
                          Ok(
                              1
                              + (args
                                 |> List.sumBy (fun (_, a) ->
                                     match a with
                                     | FromUpstream v -> v
                                     | LiteralArg s -> int s))
                          )

              match
                  CapabilityPipeline.eval lookup string (bodyWith (Map.ofList [ "s1", 10; "s2", 20 ]) (ResizeArray())) p
              with
              | Error e -> failtestf "eval errored: %A" e
              | Ok prior ->
                  Expect.equal prior (Map.ofList [ "s1", 10; "s2", 20; "a", 11; "b", 21 ]) "full eval resolves edges"

                  // change s1 only → dirty {s1, a}; s2/b reused
                  let incrInvoked = ResizeArray()

                  match
                      CapabilityPipeline.evalFrom
                          lookup
                          string
                          (bodyWith (Map.ofList [ "s1", 100; "s2", 20 ]) incrInvoked)
                          prior
                          (Set.ofList [ "s1" ])
                          p
                  with
                  | Error e -> failtestf "evalFrom errored: %A" e
                  | Ok result ->
                      Expect.equal
                          result
                          (Map.ofList [ "s1", 100; "s2", 20; "a", 101; "b", 21 ])
                          "evalFrom byte-identical to a full eval over the changed inputs"

                      Expect.equal (Set.ofSeq incrInvoked) (Set.ofList [ "s1"; "a" ]) "only the dirty branch re-invoked"

                      Expect.equal
                          (CapabilityPipeline.dirtySet (Set.ofList [ "s1" ]) p)
                          (Set.ofList [ "s1"; "a" ])
                          "dirtySet = changed ∪ dependents"

          testCase "capabilityPipelineIncrementalLaws certify evalFrom ≡ eval + minimal reuse (Phase 62)"
          <| fun _ ->
              let results = Conformance.capabilityPipelineIncrementalLaws 4242 200

              Expect.equal
                  (List.length results)
                  4
                  "byte-identical + minimal + effect-honesty + the Phase 121 node-reuse adequacy guard reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "capabilityPipelineIncrementalLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal
                  (Conformance.capabilityPipelineIncrementalLaws 4242 200)
                  results
                  "same seed ⇒ identical report"

          // ---- Phase 57: content-pack packaging contract ----

          testCase "ContentPack.load curries + registers each packed function under its narrowed signature"
          <| fun _ ->
              let baseEntry = packBaseEntry ()
              let reg = packBaseReg ()
              // a pack that binds h0 — distributing a partially-applied (curried) artifact-function
              let pf = ContentPack.pack "house-style" (Set.ofList [ "h0" ]) baseEntry

              let manifest =
                  { PackId = "legal-pack"
                    Domain = "legal"
                    PackVersion = 1
                    Functions = [ pf ] }

              match ContentPack.load manifest reg with
              | Ok loaded ->
                  // the narrowed signature is now in the index — findable from the smaller context {h1}
                  let ids =
                      FunctionRegistry.findBySignature
                          Subsumes
                          { ResultType = Some "doc"
                            Available = [ packIntHole "h1" ] }
                          loaded
                      |> List.map (fun e -> e.Capability.Id)

                  Expect.contains ids "house-style" "narrowed pack entry findable from the smaller context"
                  Expect.isFalse (List.contains "doc-fn" ids) "un-narrowed base still needs h0 — not findable from {h1}"
                  // FGP 6 — the pack carried no body; the HOST supplies it at dispatch (content stays domain-side)
                  let dispatched =
                      FunctionRegistry.dispatch loaded "house-style" [ "h1", "5" ] (fun _ () -> Ready "rendered")

                  Expect.equal
                      dispatched
                      (Ok(Ready "rendered"))
                      "host-supplied body runs; the pack carried only the typed declaration"
              | Error e -> failtestf "load failed: %A" e

          testCase "ContentPack.load refuses a pack pinned to a stale base-signature version (never binds stale)"
          <| fun _ ->
              let baseEntry = packBaseEntry ()
              let reg = packBaseReg ()
              let pf = ContentPack.pack "p" (Set.ofList [ "h0" ]) baseEntry

              let stale =
                  { pf with
                      BaseSignatureVersion = "v-old" }

              let manifest =
                  { PackId = "pk"
                    Domain = "d"
                    PackVersion = 1
                    Functions = [ stale ] }

              match ContentPack.load manifest reg with
              | Error(SignatureVersionMismatch("pk", "doc-fn", "v-old", actual)) ->
                  Expect.equal
                      actual
                      (ContentPack.signatureFingerprint packBaseSig)
                      "actual = the registry's live fingerprint"
              | other -> failtestf "expected SignatureVersionMismatch, got %A" other

          testCase "ContentPack.load default-denies an unknown base function (enumerating the known ids)"
          <| fun _ ->
              let reg = packBaseReg ()

              let ghost =
                  { NewId = "g"
                    BaseId = "ghost"
                    BaseSignatureVersion = "x"
                    BoundAddrs = Set.empty }

              let manifest =
                  { PackId = "pk"
                    Domain = "d"
                    PackVersion = 1
                    Functions = [ ghost ] }

              match ContentPack.load manifest reg with
              | Error(UnknownBaseFunction("pk", "ghost", known)) ->
                  Expect.contains known "doc-fn" "enumerates the known base ids"
              | other -> failtestf "expected UnknownBaseFunction, got %A" other

          testCase "ContentPack.load surfaces a duplicate registration as PackRegisterFailed"
          <| fun _ ->
              let baseEntry = packBaseEntry ()
              let reg = packBaseReg ()
              // a packed function whose NewId collides with the already-registered base id
              let dup = ContentPack.pack "doc-fn" (Set.ofList [ "h0" ]) baseEntry

              let manifest =
                  { PackId = "pk"
                    Domain = "d"
                    PackVersion = 1
                    Functions = [ dup ] }

              match ContentPack.load manifest reg with
              | Error(PackRegisterFailed("pk", "doc-fn", DuplicateCapability "doc-fn")) -> ()
              | other -> failtestf "expected PackRegisterFailed (DuplicateCapability), got %A" other

          testCase "the packaging contract carries no pack content (FGP 6 — content-free manifest)"
          <| fun _ ->
              // A manifest is fully expressible from ids / addresses / version tags ALONE — no domain node,
              // tree, or payload is constructible into it (PackManifest is non-generic: it has no 'Node type
              // parameter). Authoring the distribution surface never touches a domain type, so the Apache-2.0
              // abstractions package depends on no domain payload.
              let manifest =
                  { PackId = "artist-pack-vol1"
                    Domain = "music"
                    PackVersion = 3
                    Functions =
                      [ { NewId = "swing-feel"
                          BaseId = "groove"
                          BaseSignatureVersion = "abc123"
                          BoundAddrs = Set.ofList [ "groove/style" ] } ] }

              Expect.equal manifest.Functions.Length 1 "a content pack is just typed partial-application declarations"
              Expect.equal manifest.Domain "music" "carries only metadata — domain tag, ids, addresses, version" ]

// ---- Phase 295: the invocable seams converge ----

let private intEntry (addr: string) (sp: ValueSpace) : SigEntry =
    { Addr = addr
      Name = addr
      Kind = "value"
      Space = Some sp
      Slot = None
      Action = None
      Required = true }

let private capOver (id: string) (holes: SigEntry list) : Capability =
    Capability.create
        id
        { Name = id
          Holes = holes
          Effect = Effect.pureDeterministic }
        Server

[<Tests>]
let convergenceTests =
    testList
        "Function.convergence (Phase 295)"
        [ testCase "Space.subsumes compares bounds, widens int into float, and keeps trees apart"
          <| fun _ ->
              Expect.isFalse (Space.subsumes (IntRange(0, 10)) (IntRange(0, 1000))) "a wider int range does not fit"
              Expect.isTrue (Space.subsumes (IntRange(0, 1000)) (IntRange(0, 10))) "a narrower one does"
              Expect.isTrue (Space.subsumes (FloatRange(0.0, 10.0)) (IntRange(0, 5))) "int widens into float"
              Expect.isFalse (Space.subsumes (IntRange(0, 10)) (FloatRange(0.0, 5.0))) "float never narrows to int"
              Expect.isTrue (Space.subsumes (Enum [ "a"; "b" ]) (Enum [ "a" ])) "an enum subset fits"
              Expect.isTrue (Space.subsumes (StringLen(1, 3)) (Enum [ "ab" ])) "a bounded enum fits a length"
              Expect.isTrue (Space.subsumes AnyString (IntRange(0, 5))) "AnyString tops the scalars"
              Expect.isFalse (Space.subsumes AnyString (SlotTree None)) "no scalar space admits a tree"
              Expect.isTrue (Space.subsumes (SlotTree None) (SlotTree(Some "para"))) "an open slot admits any kind"
              Expect.isFalse (Space.subsumes (SlotTree(Some "table")) (SlotTree(Some "para"))) "a kind is a kind"

          testCase "a numeric argument is read strictly and has one canonical spelling"
          <| fun _ ->
              Expect.isFalse (Space.validate (IntRange(0, 10)) " 5") "leading white space refused"
              Expect.isFalse (Space.validate (IntRange(0, 10)) "+5") "a + sign refused"
              Expect.equal (Space.canonical (IntRange(0, 10)) "05") (Some "5") "leading zeros read, written short"
              Expect.equal (Space.canonical (FloatRange(0.0, 10.0)) "1.50") (Some "1.5") "the canonical float layout"
              Expect.isFalse (Space.validate (FloatRange(0.0, 10.0)) ".5") "a bare fraction refused"
              Expect.isFalse (Space.validate (FloatRange(-1e300, 1e300)) "Infinity") "Infinity refused"

              let c = capOver "k" [ intEntry "n" (IntRange(0, 10)) ]

              Expect.equal
                  (Capability.invocationKey c [ "n", "05" ])
                  (Capability.invocationKey c [ "n", "5" ])
                  "one value, one capture key"

          testCase "a bounded repeat is Required, and strict apply and validateArgs both demand it"
          <| fun _ ->
              let tree =
                  RNode.node "root" "doc" [ RNode.hole "r" "region" "rep" (RepeatHole(IntRange(0, 5))) ]

              let sg = Function.signature artw "r" tree
              Expect.isTrue (sg.Holes |> List.forall (fun h -> h.Required)) "the repeat is required"

              match Function.apply artw Map.empty tree with
              | Error(RequiredHolesUnbound [ _ ]) -> ()
              | other -> failtestf "strict apply should demand the repeat, got %A" other

              match Capability.validateArgs (Capability.create "rep" sg Server) [] with
              | Error(RequiredArgsUnbound [ _ ]) -> ()
              | other -> failtestf "validateArgs should demand the repeat, got %A" other

          testCase "registration refuses a non-total capability, naming the hole"
          <| fun _ ->
              let unbounded =
                  RNode.node "root" "doc" [ RNode.hole "r" "region" "rep" (RepeatHole AnyString) ]

              let cap = Capability.create "u" (Function.signature artw "u" unbounded) Server

              match CapabilityRegistry.register cap CapabilityRegistry.empty with
              | Error(NonTotalCapability("u", [ _ ])) -> ()
              | other -> failtestf "expected NonTotalCapability, got %A" other

              match FunctionRegistry.register (FunctionRegistry.entry "doc" cap) FunctionRegistry.empty with
              | Error(NonTotalCapability("u", _)) -> ()
              | other -> failtestf "the function registry should refuse it too, got %A" other

          testCase "compose checks totality on both parts, as composeAcross does"
          <| fun _ ->
              let outer = RNode.node "root" "doc" [ RNode.hole "s" "slot" "body" (SlotHole None) ]

              let inner =
                  RNode.node "ir" "para" [ RNode.hole "irr" "region" "rep" (RepeatHole AnyString) ]

              let slotAddr =
                  (Function.signature artw "o" outer).Holes |> List.head |> (fun h -> h.Addr)

              match Function.compose artw slotAddr inner outer with
              | Error(NonTotal _) -> ()
              | other -> failtestf "expected NonTotal, got %A" other

          testCase "a hand-built capability's determinism is its signature's"
          <| fun _ ->
              let sg =
                  { Name = "clocked"
                    Holes = []
                    Effect =
                      { Host = ReadsHost
                        Determinism = Effect.clock } }

              let c =
                  { Id = "clocked"
                    Signature = sg
                    Placement = Server }

              Expect.equal c.Determinism Effect.clock "derived from the signature"
              Expect.equal (Capability.determinismTag c) "clock" "and keyed by it"

          testCase "the capability codec refuses an unknown hole kind; the space reader takes the descriptor spelling"
          <| fun _ ->
              let c =
                  capOver
                      "odd"
                      [ { intEntry "n" (IntRange(0, 3)) with
                            Kind = "int" } ]

              match CapabilityCodec.decode (CapabilityCodec.encode c) with
              | Error m -> Expect.stringContains m "unknown hole kind: int" "names the kind"
              | Ok _ -> failtest "an unknown kind decoded"

              Expect.equal
                  (SpaceCodec.decoder (SpaceCodec.descriptorJson (StringLen(1, 4))))
                  (Ok(StringLen(1, 4)))
                  "the descriptor spelling reads back"

              Expect.equal
                  (SpaceCodec.decoder (SpaceCodec.toJson (StringLen(1, 4))))
                  (Ok(StringLen(1, 4)))
                  "the document spelling reads back"

          testCase "a pack binding an address that is not a bindable hole is refused UnknownBoundAddr"
          <| fun _ ->
              let pf = ContentPack.pack "doc-fn.typo" (Set.ofList [ "h9" ]) (packBaseEntry ())

              let manifest =
                  { PackId = "p"
                    Domain = "d"
                    PackVersion = 1
                    Functions = [ pf ] }

              match ContentPack.load manifest (packBaseReg ()) with
              | Error(UnknownBoundAddr("p", "doc-fn.typo", "h9", [ "h0"; "h1" ])) -> ()
              | other -> failtestf "expected UnknownBoundAddr, got %A" other

              match FunctionRegistry.partiallyApply "x" (Set.ofList [ "h0" ]) (packBaseEntry ()) with
              | Ok e -> Expect.equal (e.Capability.Signature.Holes |> List.map (fun h -> h.Addr)) [ "h1" ] "narrowed"
              | Error e -> failtestf "a bindable hole was refused: %A" e

          testCase "a pipeline refuses a self-edge, a cycle and a forward edge by name, and eval type-checks first"
          <| fun _ ->
              let cons = capOver "cons" [ intEntry "x" (IntRange(0, 100)) ]

              let lookup =
                  FunctionRegistry.empty
                  |> FunctionRegistry.register (FunctionRegistry.entry "n" cons)
                  |> function
                      | Ok r -> CapabilityLookup.ofFunctionRegistry r
                      | Error e -> failtestf "register failed: %A" e

              let check nodes =
                  CapabilityPipeline.typeCheck lookup { Nodes = nodes }

              Expect.equal
                  (check [ Invoke("a", "cons", IntRange(0, 100), [ "x", FromNode "a" ]) ])
                  (Error(PipelineCycle("a", [ "a" ])))
                  "self-edge"

              Expect.equal
                  (check
                      [ Invoke("a", "cons", IntRange(0, 100), [ "x", FromNode "b" ])
                        Invoke("b", "cons", IntRange(0, 100), [ "x", FromNode "a" ]) ])
                  (Error(PipelineCycle("a", [ "a"; "b" ])))
                  "cycle"

              Expect.equal
                  (check
                      [ Invoke("a", "cons", IntRange(0, 100), [ "x", FromNode "s" ])
                        Source("s", "ref", IntRange(0, 100)) ])
                  (Error(PipelineForwardEdge("a", "x", "s")))
                  "forward edge"

              Expect.equal
                  (check [ Invoke("a", "cons", IntRange(0, 100), [ "x", Literal "500" ]) ])
                  (Error(PipelineArgRefused("a", ArgOutOfSpace("x", IntRange(0, 100), "500"))))
                  "an out-of-space literal carries the space and the value"

              Expect.equal
                  (check
                      [ Source("s", "ref", IntRange(0, 1000))
                        Invoke("a", "cons", IntRange(0, 100), [ "x", FromNode "s" ]) ])
                  (Error(EdgeTypeMismatch("a", "x", "int", "int")))
                  "a wider producer range does not feed a narrower argument"

              let mutable ran = false

              let body (_: PipelineNode) (_: (string * PipelineArg<int>) list) : Result<int, string> =
                  ran <- true
                  Ok 1

              match
                  CapabilityPipeline.eval
                      lookup
                      string
                      body
                      { Nodes = [ Invoke("a", "cons", IntRange(0, 100), [ "x", FromNode "a" ]) ] }
              with
              | Error(EvalIllTyped(PipelineCycle _)) when not ran -> ()
              | other -> failtestf "expected EvalIllTyped before any body, got %A (ran %b)" other ran

              let narrow =
                  { Nodes =
                      [ Source("s", "ref", IntRange(0, 100))
                        Invoke("a", "cons", IntRange(0, 100), [ "x", FromNode "s" ]) ] }

              let overflow (n: PipelineNode) (_: (string * PipelineArg<int>) list) : Result<int, string> =
                  Ok(if CapabilityPipeline.nodeId n = "s" then 500 else 0)

              match CapabilityPipeline.eval lookup string overflow narrow with
              | Error(EvalArgRefused("a", ArgOutOfSpace("x", IntRange(0, 100), "500"))) -> ()
              | other -> failtestf "expected EvalArgRefused, got %A" other

          testCase "Deferred.settled keeps Pending apart from a failure that says pending"
          <| fun _ ->
              Expect.equal (Deferred.settled (Pending: Deferred<int>)) None "pending"
              Expect.equal (Deferred.settled (Failed "pending": Deferred<int>)) (Some(Error "pending")) "failed"
              Expect.equal (Deferred.settled (Ready 3)) (Some(Ok 3)) "ready" ]

// ---- Phase 307: validated declarations on the invocable seams ----

let private entry307 addr kind space : SigEntry =
    { Addr = addr
      Name = addr
      Kind = kind
      Space = space
      Slot = None
      Action = None
      Required = true }

let private sig307 holes : Signature =
    { Name = "f"
      Holes = holes
      Effect = Effect.pureDeterministic }

let private register307 (holes: SigEntry list) : Result<unit, InvokeError> =
    CapabilityRegistry.register (Capability.create "c" (sig307 holes) Server) CapabilityRegistry.empty
    |> Result.map ignore

let private registerFn307 (holes: SigEntry list) : Result<unit, InvokeError> =
    FunctionRegistry.register
        (FunctionRegistry.entry "t" (Capability.create "c" (sig307 holes) Server))
        FunctionRegistry.empty
    |> Result.map ignore

/// The value-hole and repeat-hole declarations the probes built, each with the refusal both
/// registries must give it.
let private probes307: (string * SigEntry list * InvokeError) list =
    [ "an infinite count", [ entry307 "r" "repeat" (Some(FloatRange(0.0, infinity))) ], NonTotalCapability("c", [ "r" ])
      "a count past the cap",
      [ entry307 "r" "repeat" (Some(IntRange(0, Space.maxRepeatCount + 1))) ],
      NonTotalCapability("c", [ "r" ])
      "a negative count", [ entry307 "r" "repeat" (Some(IntRange(-1, 3))) ], NonTotalCapability("c", [ "r" ])
      "an empty int range",
      [ entry307 "n" "value" (Some(IntRange(5, 1))) ],
      IllFormedCapability("c", EmptySpace("n", IntRange(5, 1)))
      "an empty enum", [ entry307 "e" "value" (Some(Enum [])) ], IllFormedCapability("c", EmptySpace("e", Enum []))
      "an empty string length",
      [ entry307 "s" "value" (Some(StringLen(0, -1))) ],
      IllFormedCapability("c", EmptySpace("s", StringLen(0, -1)))
      "a NaN bound", [ entry307 "x" "value" (Some(FloatRange(nan, 1.0))) ], IllFormedCapability("c", NonFiniteBound "x")
      "an infinite bound",
      [ entry307 "x" "value" (Some(FloatRange(-infinity, 1.0))) ],
      IllFormedCapability("c", NonFiniteBound "x")
      "two holes at one address",
      [ entry307 "a" "value" (Some AnyString); entry307 "a" "value" (Some AnyString) ],
      IllFormedCapability("c", DuplicateHoleAddr "a") ]

/// A capability over one integer hole, for the reader and duplicate checks.
let private intCap307 =
    Capability.create "n" (sig307 [ entry307 "n" "value" (Some(IntRange(-10, 10))) ]) Server

[<Tests>]
let validatedDeclarationTests =
    testList
        "Validated declarations (Phase 307)"
        [ testCase "every probe declaration is refused at registration, by both registries, with a typed reason"
          <| fun _ ->
              for name, holes, expected in probes307 do
                  Expect.equal (register307 holes) (Error expected) (name + ": CapabilityRegistry")
                  Expect.equal (registerFn307 holes) (Error expected) (name + ": FunctionRegistry")

          testCase "a declaration fault reads as a sentence naming what is wrong"
          <| fun _ ->
              Expect.stringContains
                  (DeclarationFault.describe (EmptySpace("n", IntRange(5, 1))))
                  "an integer from 5 to 1"
                  "an empty space names its bounds"

              Expect.stringContains (DeclarationFault.describe (DuplicateHoleAddr "a")) "'a'" "the address"
              Expect.stringContains (DeclarationFault.describe (HoleUnderSlot "s")) "'s'" "the slot's node"

              Expect.stringStarts
                  (InvokeError.describe (IllFormedCapability("c", NonFiniteBound "x")))
                  "Refused: the tool 'c' cannot be registered"
                  "inside the registration refusal"

          testCase "Space.wellFormed and Space.isCount say what a space and a count are"
          <| fun _ ->
              Expect.equal (Space.wellFormed (IntRange(1, 1))) (Ok()) "a one-value range"
              Expect.equal (Space.wellFormed (IntRange(2, 1))) (Error SpaceFault.Empty) "an empty range"
              Expect.equal (Space.wellFormed (FloatRange(1.0, nan))) (Error SpaceFault.NonFinite) "NaN"
              Expect.equal (Space.wellFormed (FloatRange(2.0, 1.0))) (Error SpaceFault.Empty) "an empty float range"
              Expect.equal (Space.wellFormed (FloatRange(-1e300, 9.0e15 * 4.0))) (Ok()) "bounds past 2^53"
              Expect.equal (Space.wellFormed AnyString) (Ok()) "any string"
              Expect.isTrue (Space.isCount (IntRange(0, Space.maxRepeatCount))) "the cap is a count"
              Expect.isFalse (Space.isCount (IntRange(0, Space.maxRepeatCount + 1))) "past the cap is not"
              Expect.isFalse (Space.isCount (FloatRange(0.0, 3.0))) "a float range is no count"
              Expect.isFalse (Space.isCount (IntRange(3, 2))) "an empty range counts nothing"

          testCase "a repeat over no count space is non-total, not required, and refused by apply"
          <| fun _ ->
              let art =
                  RNode.node "root" "doc" [ RNode.hole "r" "field" "rows" (RepeatHole(FloatRange(0.0, infinity))) ]

              let sg = Function.signature artw "f" art
              Expect.isFalse (Function.isTotal sg) "not total"
              Expect.isFalse (sg.Holes |> List.forall (fun h -> h.Required)) "and not required"

              Expect.equal
                  (Function.apply artw (Map.ofList [ "root/r", ValueArg "1e300" ]) art)
                  (Error(NonTotal "root/r"))
                  "apply refuses it before reading the count"

          testCase "a hole beneath a slot is refused, naming the slot's node"
          <| fun _ ->
              let nested =
                  RNode.node
                      "root"
                      "doc"
                      [ { RNode.hole "s" "region" "body" (SlotHole None) with
                            Children = [ RNode.hole "u" "field" "under" (ValueHole AnyString) ] } ]

              Expect.equal (Function.validate artw nested) (Error(HoleUnderSlot "s")) "the nested hole"
              Expect.equal (Function.validate artw (template ())) (Ok()) "the template is well-formed"

          testCase "compose refuses a result whose inner hole captures an outer address"
          <| fun _ ->
              // The inner tree's hole has the id of an outer hole beside the slot, so after
              // composition two holes share one address — one argument would fill both.
              let inner =
                  RNode.node "body" "para" [ RNode.hole "t" "field" "title" (ValueHole AnyString) ]

              match Function.compose artw "tpl/s" inner (template ()) with
              | Error(IllFormedResult(DuplicateHoleAddr _)) -> ()
              | other -> failtestf "expected IllFormedResult(DuplicateHoleAddr _), got %A" other

          testCase "every registrable capability's JSON Schema renders as valid canonical JSON"
          <| fun _ ->
              let finite = FloatRange(-1e300, 1e300)

              let sg =
                  sig307
                      [ entry307 "x" "value" (Some finite)
                        entry307 "n" "value" (Some(IntRange(-5, 5))) ]

              Expect.isOk (register307 sg.Holes) "it registers"
              Expect.isOk (Canon.tryRender (Function.toJsonSchema sg)) "and its schema renders"

              let cap = Capability.create "c" sg Server
              Expect.equal (CapabilityCodec.tryEncode cap) (Ok(CapabilityCodec.encode cap)) "tryEncode is encode"

              let bad =
                  Capability.create "c" (sig307 [ entry307 "x" "value" (Some(FloatRange(0.0, infinity))) ]) Server

              Expect.isError (CapabilityCodec.tryEncode bad) "a non-finite bound is refused by the guarded encode"

          testCase "the integer reader is the same under every culture; U+2212 is refused everywhere"
          <| fun _ ->
              let saved = System.Globalization.CultureInfo.CurrentCulture

              try
                  for culture in [ "en-US"; "he-IL"; "fa-IR"; "ar-SA"; "sv-SE"; "nb-NO"; "fi-FI" ] do
                      System.Globalization.CultureInfo.CurrentCulture <- System.Globalization.CultureInfo culture
                      Expect.equal (Capability.validateArgs intCap307 [ "n", "-5" ]) (Ok()) (culture + ": -5")

                      Expect.isError
                          (Capability.validateArgs intCap307 [ "n", "\u22125" ])
                          (culture + ": U+2212 is refused")

                      Expect.equal
                          (Capability.typeArgs intCap307 [ "n", "-05" ])
                          (Ok [ "n", IntValue -5 ])
                          (culture + ": the value handed on is the integer")
              finally
                  System.Globalization.CultureInfo.CurrentCulture <- saved

          testCase "an address bound twice is DuplicateArg at every seam that reads an argument list"
          <| fun _ ->
              let args = [ "n", "1"; "n", "2" ]
              Expect.equal (Capability.validateArgs intCap307 args) (Error(DuplicateArg "n")) "validateArgs"
              Expect.equal (Capability.validateArgsAll intCap307 args) (Error [ DuplicateArg "n" ]) "validateArgsAll"

              Expect.isError
                  (CapabilityCodec.decodeInvocation (CapabilityCodec.encodeInvocation "n" args))
                  "decodeInvocation"

              let lookup =
                  CapabilityLookup.ofRegistry (
                      match CapabilityRegistry.register intCap307 CapabilityRegistry.empty with
                      | Ok r -> r
                      | Error e -> failtestf "register: %A" e
                  )

              let p =
                  { Nodes = [ Invoke("i", "n", IntRange(-10, 10), [ "n", Literal "1"; "n", Literal "2" ]) ] }

              Expect.equal
                  (CapabilityPipeline.typeCheck lookup p)
                  (Error(PipelineArgRefused("i", DuplicateArg "n")))
                  "typeCheck"

          testCase "a pre-229 spaceless slot entry is invocable like a derived one"
          <| fun _ ->
              let slot =
                  { entry307 "s" "slot" None with
                      Slot = Some "para" }

              let c = Capability.create "c" (sig307 [ slot ]) Server
              Expect.equal (Capability.validateArgs c [ "s", """{"kind":"para"}""" ]) (Ok()) "a tree of its kind"

              Expect.equal
                  (Capability.validateArgs c [ "s", """{"kind":"heading"}""" ])
                  (Error(ArgOutOfSpace("s", SlotTree(Some "para"), """{"kind":"heading"}""")))
                  "a tree of another kind"

          testCase "the capability decoder checks its $type and refuses an ill-formed signature"
          <| fun _ ->
              let good =
                  Capability.create "c" (sig307 [ entry307 "n" "value" (Some(IntRange(0, 3))) ]) Server

              let text = CapabilityCodec.encode good
              Expect.equal (CapabilityCodec.decode text) (Ok good) "round trip"

              Expect.isError
                  (CapabilityCodec.decode (text.Replace("\"$type\":\"capability\"", "\"$type\":\"invocation\"")))
                  "another document type"

              Expect.isError (CapabilityCodec.decode (text.Replace("\"$type\":\"capability\",", ""))) "no document type"

              let empty = text.Replace("\"max\":3,\"min\":0", "\"max\":0,\"min\":3")
              Expect.notEqual empty text "the probe edited the bounds"
              Expect.isError (CapabilityCodec.decode empty) "an empty range is refused on read"

          testCase "strict apply refuses an open slot argument; curry accepts it"
          <| fun _ ->
              let openTree =
                  RNode.node "in" "para" [ RNode.hole "o" "field" "o" (ValueHole AnyString) ]

              let args =
                  Map.ofList [ "tpl/t", ValueArg "x"; "tpl/c", ValueArg "3"; "tpl/s", SlotArg openTree ]

              Expect.equal (Function.apply artw args (template ())) (Error(SlotArgOpen("tpl/s", [ "in/o" ]))) "apply"

              Expect.isOk (Function.curry artw args (template ())) "curry"
              Expect.isTrue (Function.isClosed artw (RNode.leaf "in" "para" "z")) "a leaf is closed"
              Expect.isFalse (Function.isClosed artw openTree) "a tree with a hole is open"

          testCase "applyMemo and applyMemoComposed agree on the Clock-slot probe: both bypass"
          <| fun _ ->
              let clockLeaf =
                  { RNode.leaf "in" "para" "now" with
                      Eff =
                          { Host = Pure
                            Determinism = Effect.clock } }

              let outerArgs = Map.ofList [ "tpl/t", ValueArg "x"; "tpl/c", ValueArg "3" ]
              let direct = Map.add "tpl/s" (SlotArg clockLeaf) outerArgs

              match Function.applyMemo artw encNode direct (template ()) Memo.empty with
              | Ok(_, c) ->
                  Expect.equal (c.Bypasses, c.Misses, c.Hits) (1, 0, 0) "applyMemo bypasses an effecting slot argument"
              | Error e -> failtestf "applyMemo: %A" e

              match
                  Function.applyMemoComposed
                      artw
                      encNode
                      [ "tpl/s", clockLeaf, Map.empty ]
                      outerArgs
                      (template ())
                      Memo.empty
              with
              | Ok(_, c) -> Expect.equal c.Hits 0 "applyMemoComposed serves nothing from the cache either"
              | Error e -> failtestf "applyMemoComposed: %A" e

          testCase "the node kind leads the pipeline key: a Source and an Invoke never share one"
          <| fun _ ->
              let s = Source("source", "source", AnyString)
              let i = Invoke("source", "source", AnyString, [])
              Expect.notEqual (CapabilityPipeline.nodeInvocationKey s) (CapabilityPipeline.nodeInvocationKey i) "apart"

          testCase "evalFrom refuses a reordered pipeline exactly as eval does"
          <| fun _ ->
              let up = Capability.create "up" (sig307 []) Server

              let down =
                  Capability.create "down" (sig307 [ entry307 "x" "value" (Some AnyString) ]) Server

              let lookup =
                  [ up; down ]
                  |> List.fold
                      (fun r c ->
                          match CapabilityRegistry.register c r with
                          | Ok r' -> r'
                          | Error e -> failtestf "register: %A" e)
                      CapabilityRegistry.empty
                  |> CapabilityLookup.ofRegistry

              let reordered =
                  { Nodes =
                      [ Invoke("b", "down", AnyString, [ "x", FromNode "a" ])
                        Invoke("a", "up", AnyString, []) ] }

              let body _ _ = Ok "v"
              let e1 = CapabilityPipeline.eval lookup id body reordered

              let e2 =
                  CapabilityPipeline.evalFrom lookup id body Map.empty (Set.ofList [ "a" ]) reordered

              Expect.isError e1 "eval refuses"
              Expect.equal e2 e1 "evalFrom refuses with the same reason" ]

// ---- Phase 383 — composeAcross checks its result as compose does ----

[<Tests>]
let composeAcrossResultTests =
    testList
        "Function.composeAcross validates its result (Phase 383)"
        [ testCase "a cross-witness composition whose embedded hole captures an outer address is IllFormedResult"
          <| fun _ ->
              // The embedded inner's hole has the id of an outer hole beside the slot, so after the
              // binding two holes share one address — the tree `compose` refuses (Phase 307).
              let inner =
                  RNode.node "body" "para" [ RNode.hole "t" "field" "title" (ValueHole AnyString) ]

              match Function.composeAcross artw artw id "tpl/s" inner (template ()) with
              | Error(IllFormedResult(DuplicateHoleAddr _)) -> ()
              | other -> failtestf "expected IllFormedResult(DuplicateHoleAddr _), got %A" other

              Expect.equal
                  (Function.composeAcross artw artw id "tpl/s" inner (template ()))
                  (Function.compose artw "tpl/s" inner (template ()))
                  "at one witness and the identity embedding, the two compositions agree on the refusal" ]
