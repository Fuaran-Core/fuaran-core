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
                      Eff = { Host = Pure; Determinism = Clock } }

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
              Expect.equal joined.Determinism Clock "determinism joined to clock"
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
              let clock = { Host = Pure; Determinism = Clock }

              let writes =
                  { Host = WritesHost
                    Determinism = Deterministic }

              let j = Effect.join clock writes
              Expect.equal j.Host WritesHost "host widened"
              Expect.equal j.Determinism Clock "determinism widened"
              Expect.isTrue (Effect.covers j clock) "join covers each input"
              Expect.isFalse (Effect.covers Effect.pureDeterministic clock) "pure does not cover clock"

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
                        Determinism = Network } }

              let cap = Capability.create "score" sg Server
              Expect.equal cap.Determinism Network "determinism mirrors the signature effect"
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

              match Registry.dispatch reg "ghost" [] (fun _ () -> Ok 1) with
              | Error(NoSuchCapability("ghost", _)) -> ()
              | other -> failtestf "expected NoSuchCapability, got %A" other

              Expect.equal (Registry.dispatch reg "apple" [] (fun _ () -> Ok 42)) (Ok 42) "registered id dispatches"

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
                        Determinism = Random } }

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
                        Determinism = Random } }

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
      Determinism = Deterministic }

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
                              Determinism = Network } } ]

              match Function.bindHandlers artw handlers (buttonTpl ()) with
              | Error(HandlerEffectExceedsCeiling("btn/click", ceiling, handler)) ->
                  Expect.equal ceiling writesHost "names the declared ceiling"
                  Expect.equal handler.Determinism Network "names the over-wide handler effect"
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
                            Determinism = Clock } }

              let joined = Function.composedEffectAcross artw artw inner (template ())
              Expect.equal joined.Host ReadsHost "host widened to the inner's"
              Expect.equal joined.Determinism Clock "determinism widened to the inner's"
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
                      Eff = { Host = Pure; Determinism = Clock } }

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
                  Registry.empty
                  |> Registry.register (Capability.create "prod" prodSig BuildTime)
                  |> Result.bind (Registry.register (Capability.create "cons" consSig BuildTime))
                  |> function
                      | Ok r -> r
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
              Expect.equal (List.length results) 3 "type-check + round-trip + replay reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "capabilityPipelineLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.capabilityPipelineLaws 4242 200) results "same seed ⇒ identical report"

          testCase "eval walks the DAG resolving FromNode edges; evalFrom reuses clean branches (Phase 62)"
          <| fun _ ->
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

              match CapabilityPipeline.eval (bodyWith (Map.ofList [ "s1", 10; "s2", 20 ]) (ResizeArray())) p with
              | Error e -> failtestf "eval errored: %A" e
              | Ok prior ->
                  Expect.equal prior (Map.ofList [ "s1", 10; "s2", 20; "a", 11; "b", 21 ]) "full eval resolves edges"

                  // change s1 only → dirty {s1, a}; s2/b reused
                  let incrInvoked = ResizeArray()

                  match
                      CapabilityPipeline.evalFrom
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
              Expect.equal (List.length results) 3 "byte-identical + minimal + effect-honesty reported"

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
                      FunctionRegistry.dispatch loaded "house-style" [ "h1", "5" ] (fun _ () -> Ok "rendered")

                  Expect.equal
                      dispatched
                      (Ok "rendered")
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
