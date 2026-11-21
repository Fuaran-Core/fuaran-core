namespace Fuaran.Core

/// A reading of the JSON Schema keywords Core's writers emit (Phase 714, DECISIONS.md D282) — the
/// judge the argument-object and emission laws hold a reader to. It is written from the JSON Schema
/// meaning of each keyword and shares no code with any reader it judges: `type`, `enum`, `const`,
/// `minimum` / `maximum`, `minLength` / `maxLength` (counted in UTF-16 code units, the unit Core's
/// `StringLen` declares, D282.5), `properties`, `required`, `additionalProperties`, `items`, `oneOf`,
/// `anyOf`, `allOf` and a `$ref` into the root's `$defs`. Annotations (`title`, `description`,
/// `$schema`, `$defs`, `default`, `examples`) and `x-` members are read past. Any other keyword is
/// answered `Error`, naming it: a keyword this reading cannot interpret is never passed over.
module internal SchemaReading =

    let private number (v: JVal) : float option =
        match v with
        | JInt n -> Some(float n)
        | JFloat f -> Some f
        | _ -> None

    /// JSON equality, as `enum` and `const` compare: numbers by value, objects by member set.
    let rec jsonEqual (a: JVal) (b: JVal) : bool =
        match a, b with
        | (JInt _ | JFloat _), (JInt _ | JFloat _) -> number a = number b
        | JStr x, JStr y -> x = y
        | JBool x, JBool y -> x = y
        | JArr xs, JArr ys -> List.length xs = List.length ys && List.forall2 jsonEqual xs ys
        | JObj xs, JObj ys ->
            List.length xs = List.length ys
            && xs
               |> List.forall (fun (k, x) ->
                   match ys |> List.tryFind (fun (k', _) -> k' = k) with
                   | Some(_, y) -> jsonEqual x y
                   | None -> false)
        | _ -> false

    let private hasType (t: string) (v: JVal) : bool =
        match t, v with
        | "object", JObj _
        | "array", JArr _
        | "string", JStr _
        | "boolean", JBool _
        | "number", (JInt _ | JFloat _)
        | "integer", JInt _ -> true
        | "integer", JFloat f ->
            not (System.Double.IsNaN f || System.Double.IsInfinity f)
            && System.Math.Floor f = f
        | _ -> false

    let private annotations =
        set [ "title"; "description"; "$schema"; "$defs"; "default"; "examples" ]

    let private all (rs: Result<bool, string> list) : Result<bool, string> =
        rs
        |> List.fold
            (fun acc r ->
                match acc, r with
                | Error e, _ -> Error e
                | Ok _, Error e -> Error e
                | Ok a, Ok b -> Ok(a && b))
            (Ok true)

    let private count (rs: Result<bool, string> list) : Result<int, string> =
        rs
        |> List.fold
            (fun acc r ->
                match acc, r with
                | Error e, _ -> Error e
                | Ok _, Error e -> Error e
                | Ok n, Ok b -> Ok(if b then n + 1 else n))
            (Ok 0)

    /// Does `schema` admit `v`, with `$ref`s resolved against `root`? An object instance holding a
    /// repeated member is no JSON Schema instance and is admitted by nothing.
    let rec admitsUnder (root: JVal) (schema: JVal) (v: JVal) : Result<bool, string> =
        match schema with
        | JBool b -> Ok b
        | JObj keywords ->
            let named (k: string) =
                keywords |> List.tryFind (fun (n, _) -> n = k) |> Option.map snd

            let repeated =
                match v with
                | JObj ms -> (ms |> List.map fst |> List.distinct |> List.length) <> List.length ms
                | _ -> false

            if repeated then
                Ok false
            else
                keywords
                |> List.map (fun (k, arg) ->
                    match k, arg with
                    | _ when annotations.Contains k || k.StartsWith("x-", System.StringComparison.Ordinal) -> Ok true
                    | "type", JStr t -> Ok(hasType t v)
                    | "enum", JArr xs -> Ok(xs |> List.exists (jsonEqual v))
                    | "const", c -> Ok(jsonEqual v c)
                    | "minimum", bound ->
                        Ok(
                            match number v, number bound with
                            | Some x, Some b -> x >= b
                            | _ -> true
                        )
                    | "maximum", bound ->
                        Ok(
                            match number v, number bound with
                            | Some x, Some b -> x <= b
                            | _ -> true
                        )
                    | "minLength", JInt n ->
                        Ok(
                            match v with
                            | JStr s -> s.Length >= n
                            | _ -> true
                        )
                    | "maxLength", JInt n ->
                        Ok(
                            match v with
                            | JStr s -> s.Length <= n
                            | _ -> true
                        )
                    | "properties", JObj props ->
                        (match v with
                         | JObj ms ->
                             all
                                 [ for (name, value) in ms do
                                       match props |> List.tryFind (fun (p, _) -> p = name) with
                                       | Some(_, s) -> admitsUnder root s value
                                       | None -> Ok true ]
                         | _ -> Ok true)
                    | "required", JArr names ->
                        Ok(
                            match v with
                            | JObj ms ->
                                names
                                |> List.forall (fun n ->
                                    match n with
                                    | JStr n -> ms |> List.exists (fun (m, _) -> m = n)
                                    | _ -> false)
                            | _ -> true
                        )
                    | "additionalProperties", extra ->
                        (match v with
                         | JObj ms ->
                             let declared =
                                 match named "properties" with
                                 | Some(JObj props) -> props |> List.map fst
                                 | _ -> []

                             all
                                 [ for (name, value) in ms do
                                       if not (List.contains name declared) then
                                           admitsUnder root extra value ]
                         | _ -> Ok true)
                    | "items", s ->
                        (match v with
                         | JArr xs -> all (xs |> List.map (admitsUnder root s))
                         | _ -> Ok true)
                    | "oneOf", JArr ss -> count (ss |> List.map (fun s -> admitsUnder root s v)) |> Result.map ((=) 1)
                    | "anyOf", JArr ss -> count (ss |> List.map (fun s -> admitsUnder root s v)) |> Result.map ((<) 0)
                    | "allOf", JArr ss -> all (ss |> List.map (fun s -> admitsUnder root s v))
                    | "$ref", JStr reference when reference.StartsWith("#/$defs/", System.StringComparison.Ordinal) ->
                        let name = reference.Substring 8

                        let target =
                            match root with
                            | JObj rootMembers ->
                                rootMembers
                                |> List.tryFind (fun (n, _) -> n = "$defs")
                                |> Option.bind (fun (_, defs) ->
                                    match defs with
                                    | JObj ds -> ds |> List.tryFind (fun (n, _) -> n = name) |> Option.map snd
                                    | _ -> None)
                            | _ -> None

                        (match target with
                         | Some target -> admitsUnder root target v
                         | None -> Error("an unresolved $ref " + reference))
                    | other, _ -> Error("the keyword '" + other + "'"))
                |> all
        | _ -> Error "a schema that is neither an object nor a boolean"

    /// Does `schema`, its own root, admit `v`?
    let admits (schema: JVal) (v: JVal) : Result<bool, string> = admitsUnder schema schema v

/// Drawn signatures and drawn argument objects for the argument-object law (Phase 714): every hole
/// kind and every value space, and for each hole a value its space admits on most draws and, on
/// the rest, a value past a bound or of another JSON kind (a stringified number, a fractional one
/// for an integer, a stringified tree, a tree with no `kind`); an integral number for an integer
/// hole; a string outside the Basic Multilingual Plane against a length bound; and objects with a
/// required hole left out, an undeclared member, an action hole's name, a member repeated, or no
/// object at all.
module internal ArgumentObjects =

    let private drawSpace (rng: LawKit.Draws) : ValueSpace =
        match rng.IntBelow 9 with
        | 0 ->
            let lo = rng.IntBelow 20 - 10
            IntRange(lo, lo + rng.IntBelow 10)
        | 1 ->
            let lo = float (rng.IntBelow 8 - 4) / 4.0
            FloatRange(lo, lo + float (rng.IntBelow 8) / 4.0)
        | 2 ->
            let lo = rng.IntBelow 3
            StringLen(lo, lo + rng.IntBelow 4)
        | 3 -> Enum(List.take (1 + rng.IntBelow 3) [ "red"; "green"; "blue" ])
        | 4 -> AnyString
        | 5 -> SlotTree None
        | 6 -> SlotTree(Some "para")
        | 7 -> Document None
        | _ -> Document(Some "Order")

    /// A signature of one to four holes, named `a`, `b`, `s.c` (a name qualified by a slot) and `d`.
    let drawSignature (rng: LawKit.Draws) (i: int) : Signature =
        let names = [ "a"; "b"; "s.c"; "d" ]
        let n = 1 + rng.IntBelow 4

        { Name = "args" + string i
          Holes =
            [ for k in 0 .. n - 1 ->
                  let kind =
                      match rng.IntBelow 8 with
                      | 0 -> RepeatHole(IntRange(0, 3))
                      | 1 -> SlotHole(if rng.IntBelow 2 = 0 then None else Some "para")
                      | 2 -> ActionHole Effect.pureDeterministic
                      | _ -> ValueHole(drawSpace rng)

                  // An action hole is never required, as `SigEntry.ofHoleDecl` derives it: a host binds
                  // it and no invocation can.
                  let required = rng.IntBelow 3 > 0

                  { Addr = "h" + string k
                    Name = List.item k names
                    Kind = kind
                    Required =
                      match kind with
                      | ActionHole _ -> false
                      | _ -> required } ]
          Effect = Effect.pureDeterministic }

    let private drawValue (rng: LawKit.Draws) (s: ValueSpace) : JVal =
        let k = rng.IntBelow 6

        match s with
        | IntRange(lo, hi) ->
            match k with
            | 0 -> JInt(hi + 1)
            | 1 -> JStr(string lo)
            | 2 -> JFloat(float lo + 0.5)
            | 3 -> JFloat(float (lo + rng.IntBelow(hi - lo + 1)))
            | _ -> JInt(lo + rng.IntBelow(hi - lo + 1))
        | FloatRange(lo, hi) ->
            match k with
            | 0 -> JFloat(hi + 0.25)
            | 1 -> JStr "0.5"
            | 2 -> JInt(int (ceil lo))
            | _ -> JFloat(lo + (hi - lo) * float (rng.IntBelow 5) / 4.0)
        | StringLen(lo, hi) ->
            match k with
            | 0 -> JStr(String.replicate (hi + 1) "x")
            | 1 -> JInt lo
            | 2 -> JStr "\U0001F600"
            | _ -> JStr(String.replicate (lo + rng.IntBelow(hi - lo + 1)) "y")
        | Enum xs ->
            match k with
            | 0 -> JStr "mauve"
            | 1 -> JInt 1
            | _ -> JStr(rng.Choose xs)
        | AnyString ->
            match k with
            | 0 -> JBool true
            | 1 -> JObj []
            | _ -> JStr "any"
        | SlotTree c ->
            let kind = defaultArg c "note"

            match k with
            | 0 -> JStr("{\"kind\":\"" + kind + "\"}")
            | 1 -> JObj [ "kind", JStr "other" ]
            | 2 -> JObj [ "children", JArr [] ]
            | 3 -> JObj [ "kind", JInt 1 ]
            | _ -> JObj [ "kind", JStr kind; "children", JArr [] ]
        | Document _ ->
            match k with
            | 0 -> JArr [ JInt 1; JStr "x" ]
            | 1 -> JStr "text"
            | 2 -> JFloat 2.5
            | _ -> JObj [ "b", JInt 1; "a", JBool false ]

    /// An argument object for `sg`: a required hole left out on one draw in eight, an optional one
    /// on half, an action hole's name sent on a quarter; on one draw in twelve no object, and on
    /// one each an undeclared member or a member repeated.
    let drawObject (rng: LawKit.Draws) (sg: Signature) : JVal =
        match rng.IntBelow 12 with
        | 0 -> JArr []
        | k ->
            let members =
                [ for h in sg.Holes do
                      match h.Space with
                      | Some s ->
                          let omit =
                              if h.Required then
                                  rng.IntBelow 8 = 0
                              else
                                  rng.IntBelow 2 = 0

                          if not omit then
                              h.Name, drawValue rng s
                      | None ->
                          if rng.IntBelow 4 = 0 then
                              h.Name, JStr "handler" ]

            let extra =
                match k, members with
                | 1, _ -> [ "zz", JInt 0 ]
                | 2, first :: _ -> [ first ]
                | _ -> []

            JObj(members @ extra)

/// The invocable-capability seam family (Phase 30; `SeamLaws` until the Phase 388 split along its banners).
module internal CapabilitySeamLaws =
    /// The invocable-capability laws (Phase 30) — the teeth on `Capability` / `Registry` and their
    /// Phase 27 replay wiring. Self-contained (it builds its own capabilities from the seed); over a
    /// seed-replayable sample it certifies:
    ///
    ///  - **arg-validation** — a well-typed invocation is accepted; an out-of-space value and an
    ///    arg addressing no declared hole are each a named `InvokeError`, never a throw or a silent
    ///    pass (default-deny by shape);
    ///  - **byte-identical replay** — a non-`Deterministic` invocation's realized value, journaled
    ///    via `EffectCapture.captureEffect` (keyed by `Capability.invocationKey` + `determinismTag`),
    ///    replays through `replayEffect` **byte-identically** even when the live source would now
    ///    produce a different value, and fully consumes the journal;
    ///  - **a capture records the whole class** (Phase 319) — the declared determinism is a SET of
    ///    factors, and the label a capture journals decodes back to exactly that set, so the effect
    ///    a capture records covers every factor the body exercised (a body reading the clock beside a
    ///    random source is journaled as both, never as its least deterministic member). Its
    ///    companion law is the converse: a body that reads a factor OUTSIDE the recorded class is
    ///    not covered, and the difference names that factor;
    ///  - **stable enumeration** — `CapabilityRegistry.enumerate` is order-stable (by id) regardless of
    ///    insertion order;
    ///  - **declaration round-trip** — `CapabilityCodec.decode (encode c) = Ok c`;
    ///  - **a slotted artifact is invocable** (Phase 229) — a capability whose signature
    ///    `Function.signature` derives from an artifact carrying tree-typed slots registers,
    ///    enumerates and DISPATCHES with a conforming slot argument (a wire document of the slot's
    ///    kind), while a constructed non-conforming one is refused by name before the body runs: a
    ///    tree of the wrong kind is `ArgOutOfSpace` carrying the slot's `SlotTree` constraint, and a
    ///    scalar is `UninvocableArg`. Both refusals are BUILT every iteration.
    ///
    /// Since Phase 210 it also certifies the seam's `Deferred` envelope, in the shape `queryLaws`
    /// carries it (Phase 198): the envelope rides out of `invoke` and `dispatch` UNCHANGED on
    /// `Ready` / `Pending`, a refusal is typed and lands BEFORE the body runs, and a body's untyped
    /// `Failed` never rides out of the seam — it becomes the enumerated `BodyFailed`, so
    /// `Ok(Failed _)` is unreachable. The three shapes MIRROR the query family's rather than being
    /// instantiated from `deferredLaws`, which takes no witness.
    ///
    /// One of `queryLaws`' three is deliberately NOT mirrored here, and the reason is worth stating
    /// so it does not read as an omission: its first envelope law is a `Deferred&lt;QueryResult&gt;`
    /// WIRE round-trip, and this seam's counterpart already exists and is already certified —
    /// `CapabilityCodec.encodeDeferred` / `decodeDeferred` are value-codec-parameterised, and
    /// `deferredLaws` round-trips all three cases at an `int` payload. Restating it here would
    /// duplicate a law rather than mirror one. What is mirrored is the part `queryLaws` could only
    /// state about ITS seam: the three outcomes, and the unreachable fourth.
    let capabilityLaws (runner: LawKit.Runner) (iterations: int) : LawResult list =
        let validation =
            LawKit.LawCell "arg-validation accepts in-space + rejects out-of-space / unknown args"

        let replay =
            LawKit.LawCell "a non-deterministic invocation replays byte-identically via capture"

        let enumeration = LawKit.LawCell "registry enumeration is stable (id-sorted)"

        let roundtrip =
            LawKit.LawCell "capability declaration round-trips through the codec"

        let declaredResult =
            LawKit.LawCell
                "a declaration states its result: one without `result` is refused MissingField at `result` (Phase 642)"

        let envelope =
            LawKit.LawCell "the envelope rides out of invoke / dispatch unchanged for Ready and Pending"

        let asyncAxis =
            LawKit.LawCell "dispatch settles, stays pending, or refuses typed before the body runs"

        let capturedEffect =
            LawKit.LawCell "the effect a capture records covers every determinism factor the body exercised"

        let underDeclared =
            LawKit.LawCell "a body that reads a factor outside the recorded effect is not covered, and is named"

        let typedFailure =
            LawKit.LawCell "a body failure is a typed BodyFailed, never Ok(Failed _)"

        let slotted =
            LawKit.LawCell
                "a capability over a slotted artifact is invocable; a non-conforming slot arg is refused by name"

        let argumentObjects =
            LawKit.LawCell
                "a model's argument object: toJsonSchema admits it exactly when decodeArgs reads it, and what decodeArgs reads validateArgs admits (Phase 714)"

        // value-codec for the captured realized value (an int — the stand-in for a model output).
        let encodeV (v: int) : string = string v

        let decodeV (s: string) : Result<int, string> =
            match System.Int32.TryParse s with
            | true, v -> Ok v
            | _ -> Error("not an int: " + s)

        let hashFn = OpStream.defaultHash

        // Every non-empty determinism set over the three factors, in a fixed order; iteration `i`
        // declares the (i mod 7)th, so a run reaches the single-factor and the multi-factor labels
        // without a further draw from the cursor (Phase 319).
        let allFactors = [ ClockFactor; RandomFactor; NetworkFactor ]

        let nonEmptySets =
            [ for mask in 1..7 ->
                  allFactors
                  |> List.indexed
                  |> List.filter (fun (k, _) -> (mask >>> k) &&& 1 = 1)
                  |> List.map snd
                  |> Set.ofList ]

        LawKit.run runner iterations (fun rng i at ->
            let lo = rng.IntBelow 50
            let span = rng.IntBelow 50
            let hi = lo + span + 1

            let hole: SigEntry =
                { Addr = "h0"
                  Name = "x"
                  Kind = ValueHole(IntRange(lo, hi))
                  Required = true }

            let sg: Signature =
                { Name = "cap" + string i
                  Holes = [ hole ]
                  Effect =
                    { Host = ReadsHost
                      Determinism = List.item (i % 7) nonEmptySets } }

            // Phase 642: the declared result alternates, so a run reads both spellings — the captured
            // value's space on an even iteration, nothing on an odd one.
            let result =
                if i % 2 = 0 then
                    Returns.Value(IntRange(0, 999))
                else
                    Returns.Nothing

            let cap = Capability.create ("cap-" + string i) sg (ClientIsland Pyodide) result

            // arg-validation: in-space accepts; out-of-space + unknown-arg reject.
            let inSpace = string lo

            match Capability.validateArgs cap [ "x", inSpace ] with
            | Ok() -> validation.Saw()
            | Error e -> validation.Check(false, fun () -> at (sprintf "rejected a valid arg: %A" e))

            (match Capability.validateArgs cap [ "x", string (hi + 1) ] with
             | Error(ArgOutOfSpace _) -> validation.Saw()
             | other -> validation.Check(false, fun () -> at (sprintf "out-of-space not rejected: %A" other)))

            (match Capability.validateArgs cap [ "nope", inSpace ] with
             | Error(UnknownArg _) -> validation.Saw()
             | other -> validation.Check(false, fun () -> at (sprintf "unknown arg not rejected: %A" other)))

            // byte-identical replay through the Phase 27 seam.
            let realized = rng.IntBelow 1000
            let args = [ "x", inSpace ]
            let key = Capability.invocationKey cap args
            let det = Capability.determinismTag cap

            let _, caps =
                EffectCapture.captureEffect StreamConfig.canonical hashFn encodeV det key (fun () -> realized) []

            let liveDifferent () = realized + 1 // a divergent live source

            match EffectCapture.replayEffect decodeV key det liveDifferent caps with
            | Ok(v, rest) ->
                replay.Check(
                    (v = realized && List.isEmpty rest),
                    fun () -> at (sprintf "replay ≠ recorded invocation (%d vs %d)" v realized)
                )
            | Error m -> replay.Check(false, fun () -> at (sprintf "replay errored: %s" m))

            // The effect a capture records covers every factor the body exercised (Phase 319). The
            // body reads the factors it is given, noting each; the journal is decoded back through
            // the canonical label, and the set it names must equal the declared class and so cover
            // every factor read — the whole class is journaled, not the least deterministic member.
            let captureReading (readSet: Set<DeterminismFactor>) =
                let seen = ref Set.empty

                let body () =
                    for f in readSet do
                        seen.Value <- Set.add f seen.Value

                    realized

                let _, journal =
                    EffectCapture.captureEffect StreamConfig.canonical hashFn encodeV det key body []

                let recorded =
                    match journal with
                    | [ c ] -> Effect.tryDeterminismOfTag c.Determinism
                    | _ -> None

                recorded, seen.Value

            let host = cap.Signature.Effect.Host

            let covered (recorded: Set<DeterminismFactor>) (exercised: Set<DeterminismFactor>) =
                Effect.covers { Host = host; Determinism = recorded } { Host = host; Determinism = exercised }

            let declared = cap.Determinism

            for readSet in [ declared; declared |> Set.toList |> List.truncate 1 |> Set.ofList ] do
                match captureReading readSet with
                | Some recorded, seen ->
                    capturedEffect.Check(
                        (recorded = declared && covered recorded seen),
                        fun () ->
                            at (
                                sprintf
                                    "the capture recorded %A but the body exercised %A of declared %A"
                                    recorded
                                    seen
                                    declared
                            )
                    )
                | None, _ ->
                    capturedEffect.Check(false, fun () -> at "the capture journaled no decodable determinism label")

            // A body that reads a factor OUTSIDE the class is not covered by the record, and the
            // difference names exactly that factor — the law has teeth on an under-declaration.
            match allFactors |> List.tryFind (fun f -> not (Set.contains f declared)) with
            | Some outside ->
                match captureReading (Set.add outside declared) with
                | Some recorded, seen ->
                    underDeclared.Check(
                        (not (covered recorded seen))
                        && Set.difference seen recorded = Set.singleton outside,
                        fun () -> at (sprintf "a read of %A outside the recorded %A was not named" outside recorded)
                    )
                | None, _ ->
                    underDeclared.Check(false, fun () -> at "the capture journaled no decodable determinism label")
            | None -> ()

            // stable enumeration regardless of insertion order.
            let capB = Capability.create ("cap-a" + string i) sg BuildTime result

            let reg =
                CapabilityRegistry.empty
                |> CapabilityRegistry.register cap
                |> Result.bind (CapabilityRegistry.register capB)

            (match reg with
             | Ok r ->
                 let ids = CapabilityRegistry.enumerate r |> List.map _.Id
                 enumeration.Check((ids = List.sort ids), fun () -> at (sprintf "enumerate not id-sorted: %A" ids))
             | Error e -> enumeration.Check(false, fun () -> at (sprintf "register failed: %A" e)))

            // declaration round-trip.
            match CapabilityCodec.decode (CapabilityCodec.encode cap) with
            | Ok c2 -> roundtrip.Check((c2 = cap), fun () -> at "capability ≠ round-trip")
            | Error m -> roundtrip.Check(false, fun () -> at (sprintf "decode failed: %s" m))

            // the declared result is the declaration's (Phase 642): without it, nothing is read.
            (match CapabilityCodec.encodeJson cap with
             | JObj fs ->
                 let undeclared =
                     Canonical.write Rendering.CrossHost (JObj(fs |> List.filter (fun (k, _) -> k <> "result")))

                 match CapabilityCodec.decodeDetailedWith ReadPolicy.Lenient undeclared with
                 | Error e when e.Code = DecodeCode.MissingField && e.Path = [ PathSegment.Key "result" ] ->
                     declaredResult.Saw()
                 | other ->
                     declaredResult.Check(
                         false,
                         fun () -> at (sprintf "a declaration without its result was not refused at it: %A" other)
                     )
             | other -> declaredResult.Check(false, fun () -> at (sprintf "the declaration is no object: %A" other)))

            // ---- the Deferred envelope on the seam (Phase 210) ----

            // A declaration the registry refuses is a failure of the first law the registration
            // serves, and the iteration's remaining checks are skipped — never a throw.
            match CapabilityRegistry.register cap CapabilityRegistry.empty with
            | Error e -> envelope.Fail(at (sprintf "the built declaration was refused by the registry: %A" e))
            | Ok creg ->
                // SETTLED and PENDING: the body's envelope rides out of the seam unchanged, through the
                // capability-level `invoke` and the registry-level `dispatch` alike.
                for answer in [ Ready realized; Pending ] do
                    let direct = Capability.invoke cap args (fun () -> answer)

                    let dispatched =
                        CapabilityRegistry.dispatch creg () None cap.Id args (fun _ () -> answer)

                    envelope.Check(
                        (direct = Ok answer && dispatched = Ok answer),
                        fun () ->
                            at (
                                sprintf
                                    "%A did not ride out unchanged (invoke %A, dispatch %A)"
                                    answer
                                    direct
                                    dispatched
                            )
                    )

                // REFUSED: typed, and before the body runs — on a rejected arg set and on an
                // unregistered id alike.
                let ran = ref false

                (match
                    CapabilityRegistry.dispatch creg () None cap.Id [ "x", string (hi + 1) ] (fun _ () ->
                        ran.Value <- true
                        Ready realized)
                 with
                 | Error(ArgOutOfSpace _) when not ran.Value -> asyncAxis.Saw()
                 | other ->
                     asyncAxis.Check(
                         false,
                         fun () -> at (sprintf "refusal not typed-before-body (%A; body ran: %b)" other ran.Value)
                     ))

                (match
                    CapabilityRegistry.dispatch creg () None "no-such-capability" args (fun _ () ->
                        ran.Value <- true
                        Ready realized)
                 with
                 | Error(NoSuchCapability _) when not ran.Value -> asyncAxis.Saw()
                 | other ->
                     asyncAxis.Check(
                         false,
                         fun () ->
                             at (
                                 sprintf
                                     "an unregistered id was not refused before the body (%A; body ran: %b)"
                                     other
                                     ran.Value
                             )
                     ))

                // a body's untyped failure never rides out of the seam: it becomes the enumerated
                // `BodyFailed`, so `Ok(Failed _)` is unreachable. Exhausts the body's three answers
                // rather than asserting the fourth away.
                for answer in [ Ready realized; Pending; Failed("boom-" + string i) ] do
                    match CapabilityRegistry.dispatch creg () None cap.Id args (fun _ () -> answer), answer with
                    | Error(BodyFailed m), Failed fm when m = fm -> typedFailure.Saw()
                    | Ok d, (Ready _ | Pending) when d = answer -> typedFailure.Saw()
                    | other, _ ->
                        typedFailure.Check(
                            false,
                            fun () -> at (sprintf "a %A body did not project to a typed outcome: %A" answer other)
                        )

                // Phase 229 — a capability over a slotted artifact is invocable. The artifact is a
                // one-node witness declaring a CONSTRAINED slot (a drawn kind), an UNCONSTRAINED slot
                // and a value hole; its signature is derived by `Function.signature`, never written by
                // hand, so the law is about what the seam is actually given.
                let kind = "k" + string (lo % 7)
                // Phase 712 (D281): the seam binds by the hole's qualified name; these holes are
                // declared directly, so each name is its bare name.
                let slotAddr, anyAddr, valueAddr = "tpl/body", "tpl/any", "tpl/n"
                let slotName, anyName, valueName = "body", "any", "n"

                let slottedWitness: ArtifactWitness<unit, string> =
                    { Tree =
                        { Id = fun () -> "tpl"
                          KindTag = fun () -> "tpl"
                          Children = fun () -> []
                          ReplaceChildren = fun () _ -> () }
                      IdW =
                        { ToString = id
                          OfString = id
                          Equals = (=) }
                      Holes =
                        fun () ->
                            [ { Addr = slotAddr
                                Name = slotName
                                Kind = SlotHole(Some kind)
                                Slots = [] }
                              { Addr = anyAddr
                                Name = anyName
                                Kind = SlotHole None
                                Slots = [] }
                              { Addr = valueAddr
                                Name = valueName
                                Kind = ValueHole(IntRange(lo, hi))
                                Slots = [] } ]
                      Effect = fun () -> sg.Effect
                      Bind = fun _ _ () -> Ok() }

                let slottedCap =
                    Capability.create
                        ("slotted-" + string i)
                        (Function.signature slottedWitness "slotted" ())
                        Server
                        (Returns.Value(SlotTree(Some "slotted")))

                let tree (k: string) =
                    Json.render (Json.kindObj k [ "n", JInt i ])

                let conforming =
                    [ slotName, tree kind; anyName, tree ("free" + string i); valueName, string lo ]

                let wrongKind =
                    [ slotName, tree (kind + "x"); anyName, tree kind; valueName, string lo ]

                let scalar = [ slotName, tree kind; anyName, string lo; valueName, string lo ]

                let failSlotted msg = slotted.Check(false, fun () -> at msg)

                match CapabilityRegistry.register slottedCap CapabilityRegistry.empty with
                | Error e -> failSlotted (sprintf "a slotted capability did not register: %A" e)
                | Ok sreg ->
                    slotted.Check(
                        CapabilityRegistry.enumerate sreg |> List.map _.Id = [ slottedCap.Id ],
                        fun () -> at "a registered slotted capability is not enumerated"
                    )

                    let ran = ref false

                    let run a =
                        ran.Value <- false

                        CapabilityRegistry.dispatch sreg () None slottedCap.Id a (fun _ () ->
                            ran.Value <- true
                            Ready realized)

                    (match run conforming with
                     | Ok(Ready v) when v = realized && ran.Value -> slotted.Saw()
                     | other -> failSlotted (sprintf "a conforming slot argument did not dispatch: %A" other))

                    (match run wrongKind with
                     | Error(ArgOutOfSpace(a, SlotTree(Some c), _)) when a = slotName && c = kind && not ran.Value ->
                         slotted.Saw()
                     | other ->
                         failSlotted (
                             sprintf "a tree of the wrong kind was not refused by name before the body: %A" other
                         ))

                    (match run scalar with
                     | Error(UninvocableArg a) when a = anyName && not ran.Value -> slotted.Saw()
                     | other ->
                         failSlotted (sprintf "a scalar bound to a slot was not refused as uninvocable: %A" other)))

        // Phase 714 (D282): a model's argument object, judged by the schema the model is given. On a
        // stream of its own (`Fork`), so the draws above, which the published vectors reproduce, are
        // unchanged.
        let admittedDraws = ref 0
        let refusedDraws = ref 0

        LawKit.run (runner.Fork 714) iterations (fun rng i at ->
            let sg = ArgumentObjects.drawSignature rng i
            let cap = Capability.create ("args-" + string i) sg Server Returns.Nothing
            let schema = Function.toJsonSchema sg
            let object = ArgumentObjects.drawObject rng sg

            match SchemaReading.admits schema object with
            | Error unread -> argumentObjects.Fail(at ("the schema reading cannot interpret " + unread))
            | Ok admitted ->
                let read = CapabilityCodec.decodeArgsJson cap object

                let readIt =
                    match read with
                    | Ok _ -> true
                    | Error _ -> false

                if admitted then
                    admittedDraws.Value <- admittedDraws.Value + 1
                else
                    refusedDraws.Value <- refusedDraws.Value + 1

                argumentObjects.Check(
                    (admitted = readIt),
                    fun () ->
                        at (
                            sprintf
                                "the schema %s %s, and decodeArgs answers %A; schema %s"
                                (if admitted then "admits" else "refuses")
                                (Json.render object)
                                read
                                (Json.render schema)
                        )
                )

                match read with
                | Ok args ->
                    let validated = Capability.validateArgs cap args

                    argumentObjects.Check(
                        (validated = Ok()),
                        fun () -> at (sprintf "decodeArgs read %A, which validateArgs refuses: %A" args validated)
                    )
                | Error _ -> ())

        argumentObjects.Check(
            (admittedDraws.Value > 0 && refusedDraws.Value > 0),
            fun () ->
                sprintf
                    "the draws reached %d admitted and %d refused argument objects; the law needs both"
                    admittedDraws.Value
                    refusedDraws.Value
        )

        LawKit.results
            [ validation
              replay
              enumeration
              roundtrip
              declaredResult
              envelope
              asyncAxis
              typedFailure
              slotted
              capturedEffect
              underDeclared
              argumentObjects ]

    /// The capability-seam laws at a DOMAIN'S seam (Phase 246). `capabilityLaws` beside it builds
    /// its own capabilities from the seed and certifies Core's `CapabilityRegistry.dispatch`; it cannot see a
    /// domain's registry, body or host path, so a host that runs the body before the registry
    /// refuses leaves it green. This form runs the domain's own `CapabilitySeamWitness` — every call
    /// the witness's generator draws goes through the witness's `Dispatch` with the domain's `body`
    /// (a per-call argument since Phase 391), counted — and certifies:
    ///
    ///  - **three outcomes** — every dispatch settles (`Ok(Ready _)`), stays pending (`Ok Pending`)
    ///    or is refused typed (`Error _`); `Ok(Failed _)` never escapes, and a `BodyFailed` carries the
    ///    body's own failure and nothing else;
    ///  - **a refusal precedes the body** — a typed refusal ran no body, and a settled, pending or
    ///    body-failed dispatch ran it exactly once;
    ///  - **the host is the registry's** — a call reaches the body iff the registry's NAMED decision
    ///    for it (`CapabilityRegistry.decideNamed` under the witness's context, no grant presented)
    ///    is `Allow`, and a refused call carries the error the registry itself gives
    ///    (`NoSuchCapability` naming the registered ids, the validation error, or the policy refusal
    ///    the named decision is: `PolicyRefused` naming the gate for a `Deny`, `ApprovalRequired`
    ///    naming it for a `NeedsApproval`), so a host can neither add a refusal nor drop one. Since
    ///    Phase 650 the family reads the gate, so `CapabilityRegistry.under ctx` certifies a context
    ///    whose gate parks or denies a drawn call; a registry whose gate admits every drawn call is
    ///    certified exactly as before. The read tells no observer, so the family never reports a
    ///    call the host did not make.
    ///
    /// **Vacuity.** The guard counts settled, pending and refused-before-the-body dispatches over the
    /// drawn calls; a generator that never reaches one of the three is starved, and the family says
    /// so rather than reporting green. A thrown `body` or `Dispatch` is a failure of the first law.
    ///
    /// The capability instance of `LawKit.seamLaws` (Phase 297); `queryLawsAt` is the query one.
    let capabilityLawsAt
        (w: CapabilitySeamWitness<'v>)
        (body: (string * string) list -> Capability -> Deferred<'v>)
        (runner: LawKit.Runner)
        (iterations: int)
        : LawResult list =
        LawKit.seamLaws
            { Admitted =
                fun id args ->
                    match CapabilityRegistry.decideNamed w.Registry () None id args with
                    | Error e -> Error e
                    | Ok(_, PolicyDecision.Allow) -> Ok()
                    | Ok(name, PolicyDecision.Deny g) -> Error(PolicyRefused(name, g.Message, g.Alternatives))
                    | Ok(name, PolicyDecision.NeedsApproval) -> Error(ApprovalRequired name)
              Body = body
              Dispatch = fun id args body -> w.Dispatch id args (fun c () -> body c)
              Gen = w.GenCall
              Rendering =
                { Laws =
                    "capability dispatch at the domain has three outcomes (settled, pending, refused typed); Ok(Failed _) never escapes",
                    "a refusal precedes the body at the domain's host (refused: no body; dispatched: exactly one)",
                    "the domain's host agrees with its registry (reaches the body iff admitted; refuses with the registry's error)"
                  Runner = "body"
                  BodyFailedName = "BodyFailed"
                  TryBodyFailed =
                    fun e ->
                        match e with
                        | BodyFailed m -> Some m
                        | _ -> None
                  BodyFailed = BodyFailed } }
            runner
            iterations
