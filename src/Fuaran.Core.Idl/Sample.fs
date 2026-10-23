namespace Fuaran.Core.Idl

open Fuaran.Core
open System.Runtime.CompilerServices

/// Deterministic, adversarial SAMPLING over a vocabulary: draw `count` nodes from an
/// [[Idl]] and a seed, reproducibly, on any host and any runtime.
///
/// **Why this is its own module rather than part of `Gen` (Phase 97).** It was written
/// inside the generator because the generator is what first wanted vectors, and it stayed
/// there by where it was written rather than by what it depends on — it references no
/// emitter helper, builds no source string, and its output is a VALUE rather than a
/// language. Left beside the emitters it was unreachable to any consumer that wants
/// sampled vectors without also taking on a source generator, so it travels here with the
/// model, [[Encode]] and [[Decode]] in the domain-neutral, Fable-clean half.
module Sample =
    // -----------------------------------------------------------------------
    // Phase 317 — GENERATIVE conformance vectors.
    //
    // The fixed corpus proves the hosts agree on the shapes someone thought to
    // write down. It cannot prove they agree on the shapes nobody did — and
    // independent hosts diverge in exactly two places the corpus under-samples:
    // **string escaping** and **float formatting**. So the pools below are
    // adversarial by construction rather than uniform: quotes, backslashes, a
    // control character, an astral-plane codepoint, and floats that render with
    // no decimal point (so a re-parse sees an integer).
    //
    // Determinism is the whole point of a failing vector, so this uses an
    // explicit generator rather than `System.Random`, whose sequence is not
    // contractually stable across runtimes — a vector that fails elsewhere has
    // to reproduce here from its seed alone.
    //
    // Phase 387 (DECISIONS.md D124) — "on any runtime" is MEASURED, not asserted.
    // The generator is the conformance kit's `ConfRng` shape, draw for draw: a
    // uint32 xorshift32 (shifts and XOR only) seeded by the same warm-up, and every
    // bounded choice taken from the HIGH bits by rejection. Until 0.36.0 it was a
    // uint64 LCG with a 64-bit multiply — the arithmetic shape `ConfRng` documents
    // as the one Fable cannot carry — and it chose by `% n`, the low-bit modulo
    // draw `ConfRng.intBelow` was fixed for at 0.12.0. Nothing measured either:
    // `ParityVectors` carried no sampler row. Its `sample/*` rows now pin the
    // drawn choices and the sampled node sets, so the receiving gate's node leg
    // compares this module's output across the two pipelines, and
    // `ParityVectorTests` holds the stream equal to `ConfRng`'s from the same seed.
    //
    // Phase 388 (DECISIONS.md D125) — one body, not a copy held equal by a test.
    // `Fuaran.Core.Conformance` depends on this package, so the kernel lives HERE
    // (`Xorshift32`) and `ConfRng` reads it through this package's
    // `InternalsVisibleTo`; the sampler and the kit draw the same stream because
    // they run the same code. `ParityVectorTests` still pins the two streams equal,
    // which now holds the threading rather than a duplicated arithmetic.
    // -----------------------------------------------------------------------

    /// Why a vocabulary could not be sampled (Phase 292; `SampleRefusal` before `0.37.0`, renamed
    /// by Phase 391 under STABILITY.md's "Vocabulary": the sampler ran over the vocabulary it was
    /// given and could not complete, which is a fault) — the typed fault
    /// [[trySampleNodes]] returns where the sampler used to throw (a division by zero
    /// choosing from an empty list) or to draw a placeholder the encoder then refused
    /// (`VStr "?"` at an enum, `VUnion("?", [])` at a union).
    ///
    /// `At` names the slot — the type, or the kind tag — and `Reason` says why nothing in
    /// it can be drawn: an empty enum, union, kind or op set, a name the vocabulary does not
    /// declare, an unbound type variable, or a type with no finite value.
    type SampleFault =
        {
            /// A noun phrase locating the slot, written to follow "cannot sample" in
            /// [[Describe]] — `enum 'Tone'`, `type variable 'T'`, or a type's `%A` rendering.
            At: string
            /// A lower-case clause, written to follow the colon in [[Describe]].
            Reason: string
        }

        /// The fault as one sentence.
        member this.Describe = sprintf "cannot sample %s: %s" this.At this.Reason

    /// Raised inside the sampler and caught at [[trySampleNodes]], so the refusal unwinds
    /// the recursion without threading a `Result` through every draw (which would have to
    /// thread the RNG too). Never escapes this module.
    exception private Unsampleable of at: string * reason: string

    let private refuse (at: string) (reason: string) : 'a = raise (Unsampleable(at, reason))

    /// How far below the depth floor a draw may go before the type is declared to have no
    /// finite value. The floor arms terminate every vocabulary with a leaf kind well above
    /// this; what reaches it is a cycle no floor arm can break (a record whose required
    /// field is itself, a domain with no leaf kind), which used to overflow the stack.
    let private bottom = -24

    /// The sampler's generator position — `ConfRng.T`'s xorshift32 state, held mutably because
    /// the sampler threads one stream through a recursion that returns values, not states.
    type private Rng = { mutable State: uint32 }

    /// `ConfRng.ofSeed`: the kernel's seed (`Xorshift32`, Phase 388 — the one body both read).
    ///
    /// The three functions here that name `Xorshift32` are `NoInlining` (DECISIONS.md D129): since
    /// Phase 531 the kernel is internal to `Fuaran.Core.Text`, read through its `InternalsVisibleTo`,
    /// and a body naming it that the F# optimiser copied into a Release-built caller of this package
    /// would fail there with `MethodAccessException`.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let private ofSeed (seed: int) : Rng = { State = Xorshift32.seeded seed }

    /// `ConfRng.next`: advance, and answer the top 31 bits of the new state.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let private next (r: Rng) : int =
        r.State <- Xorshift32.step r.State
        Xorshift32.value r.State

    /// `ConfRng.intBelow`: the kernel's high-bit rejection draw over this mutable position — the
    /// position is threaded as itself, so each draw advances it in place. `n <= 0` draws nothing
    /// (no caller passes one — [[pickAt]] refuses an empty list first).
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let private intBelow (r: Rng) (n: int) : int =
        Xorshift32.below (fun (r: Rng) -> next r, r) n r |> fst

    /// A draw from a non-empty list, its index by [[intBelow]]; an empty one is refused as
    /// nothing to choose from at `at`, never divided by.
    let private pickAt (r: Rng) (at: string) (xs: 'a list) : 'a =
        match xs with
        | [] -> refuse at "there is nothing to choose from (it declares no case)"
        | _ -> List.item (intBelow r (List.length xs)) xs

    /// A draw from one of the sampler's own pools, which are never empty.
    let private pick (r: Rng) (xs: 'a list) : 'a = pickAt r "a pool" xs

    /// Strings chosen to break a hand-rolled escaper: the two characters JSON
    /// must escape, a control character (the \u00xx path), a surrogate pair, and
    /// a payload that would terminate an unescaped script context.
    let private stringPool =
        [ ""
          "plain"
          "quote\" inside"
          "back\\slash"
          "ctrlhere"
          "new\nline"
          "tab\there"
          "accent-é"
          "astral-\U0001F600"
          "</script>" ]

    /// Both Int32 extremes (digit-count boundaries) plus zero.
    let private intPool = [ 0; 1; -1; 42; -7; 2147483647; -2147483648 ]

    /// Whole-valued floats are the hazard; mixed with values that exercise
    /// round-trip ("R") formatting.
    let private floatPool = [ 0.0; 1.0; -1.0; 3.0; 2.5; -0.125; 1234.5; 1e10; 1e-7 ]

    /// Values for a hosted slot's declared FORMAT (Phase 252, [[HostedFormat]]): the
    /// boundaries a codec most often gets wrong — a leap day, the calendar's ends, the
    /// epoch, an offset other than UTC — each admitted by its format, so a document
    /// drawn here is valid in every leg. An unknown format draws nothing it could admit;
    /// the empty string stands in, and [[Declare.hostedWireErrors]] is what reports it.
    let private formatPool (format: HostedFormat) : string list =
        match format with
        | HostedFormat.Date -> [ "2026-10-03"; "2000-02-29"; "1970-01-01"; "0001-01-01"; "9999-12-31" ]
        | HostedFormat.DateTime ->
            [ "2026-10-03T12:34:56Z"
              "2000-02-29T23:59:59+01:00"
              "1970-01-01T00:00:00Z"
              "1999-12-31T23:59:59-05:30" ]
        | HostedFormat.Uuid ->
            [ "00000000-0000-0000-0000-000000000000"
              "123e4567-e89b-12d3-a456-426614174000"
              "f81d4fae-7dec-11d0-a765-00a0c91e6bf6" ]

    /// The kinds (or ops) a node (or op) may take AT THE DEPTH FLOOR: those whose required fields
    /// reach no further node ([[WireForm.requiredReachesNode]]), so the recursion stops there. Falls
    /// back to all of them when none qualifies — the sampler must still produce a node for a
    /// required slot, and a domain with no leaf kind has no finite node at all, which is its own
    /// defect rather than one to hide here.
    let private atFloor (idl: Idl) (owners: IdlKind list) : IdlKind list =
        match
            owners
            |> List.filter (fun o -> not (WireForm.requiredReachesNode idl Set.empty o.Fields))
        with
        | [] -> owners
        | leaves -> leaves

    let rec private sampleType (idl: Idl) (r: Rng) (depth: int) (t: IdlType) : IdlValue =
        if depth < bottom then
            refuse (sprintf "%A" t) "it has no finite value — every draw at the depth floor recurses"

        // Phase 513 — the draw switches on the type's wire form ([[WireForm.ofType]]), the same
        // lowering the encoder writes and the decoder reads, so what is drawn is what they agree on.
        match WireForm.ofType idl t with
        | WireForm.Str -> VStr(pick r stringPool)
        | WireForm.Int -> VInt(pick r intPool)
        | WireForm.Bool -> VBool(intBelow r 2 = 0)
        | WireForm.Float -> VFloat(pick r floatPool)
        | WireForm.Sentinel(value, _) -> value
        // Phase 252 — a hosted slot that DECLARES its wire form is drawn from it: from the
        // format's pool when it names one, else from the wire type itself, rendered through
        // the interpreter's own encoder.
        | WireForm.Hosted(_, Some fmt) -> VJson(JStr(pick r (formatPool fmt)))
        | WireForm.Hosted(Some w, None) ->
            match Encode.valueJson idl w (sampleType idl r depth w) with
            | Ok j -> VJson j
            | Error m -> refuse (sprintf "the hosted wire form %A" w) m
        // Phase 676 — sample real JSON, built from the SAME adversarial pools, so the
        // passthrough is stressed on escaping and float layout like every other leg. A hosted slot
        // that declares no wire form samples the same way: both the interpreter and the TS backend
        // carry it verbatim, so arbitrary JSON stresses exactly what they share, and a vocabulary
        // that declares nothing keeps its seeded stream.
        | WireForm.Verbatim
        | WireForm.Hosted(None, None) ->
            VJson(
                match intBelow r 4 with
                | 0 -> JStr(pick r stringPool)
                | 1 -> JFloat(pick r floatPool)
                | 2 -> JArr [ JInt(pick r intPool); JStr(pick r stringPool) ]
                | _ -> JObj [ "z", JInt(pick r intPool); "a", JStr(pick r stringPool) ]
            )
        // An unbound variable has no type to draw from; the encoder refuses it by name.
        | WireForm.Unbound v ->
            refuse ("type variable '" + v + "'") "it is not bound by an enclosing union instantiation"
        | WireForm.Enum e -> VEnum(pickAt r ("enum '" + e.Name + "'") e.WireCases)
        | WireForm.Undeclared(Undeclared.Enum name) ->
            refuse ("enum '" + name + "'") "the vocabulary does not declare it"
        | WireForm.List inner ->
            // Bounded, and empty is a legitimate sample — an empty collection is
            // NOT absence, and the two must stay distinguishable on the wire.
            let n = if depth <= 0 then 0 else intBelow r 3
            VList [ for _ in 1..n -> sampleType idl r (depth - 1) inner ]
        | WireForm.Map vt ->
            let n = if depth <= 0 then 0 else intBelow r 3
            VMap [ for i in 1..n -> (sprintf "k%d" i), sampleType idl r (depth - 1) vt ]
        | WireForm.Record rc -> VRecord(sampleFields idl r (depth - 1) rc.Fields)
        | WireForm.Undeclared(Undeclared.Record name) ->
            refuse ("record '" + name + "'") "the vocabulary does not declare it"
        | WireForm.Union u ->
            // At the depth floor prefer a nullary case when one exists, so a
            // recursive union terminates rather than being truncated.
            let candidates =
                if depth <= 0 then
                    match u.Decl.Cases |> List.filter (fun c -> List.isEmpty c.Fields) with
                    | [] -> u.Decl.Cases
                    | nullary -> nullary
                else
                    u.Decl.Cases

            // The parameters are substituted RECURSIVELY and BY NAME (Phase 292 — [[TypeParams]],
            // through the lowering's binding, shared with the codec and the generator). A shallow
            // swap left `TList (TVar "T")` alone, so the sampler generated a string where the slot's
            // codec expects a float; the copy that replaced mapped every variable to the FIRST
            // argument, which drew a two-parameter union's second parameter at the first's type.
            match u.Binding with
            | None ->
                refuse
                    ("union '" + u.Name + "'")
                    (sprintf
                        "it is applied to %d type argument(s) and declares %d"
                        (List.length u.Args)
                        (List.length u.Decl.Params))
            | Some binding ->
                let c = pickAt r ("union '" + u.Name + "'") candidates
                VUnion(u.Decl.WireTagOf c.Tag, sampleFields idl r (depth - 1) (WireForm.caseFields binding c))
        | WireForm.Undeclared(Undeclared.Union(name, _)) ->
            refuse ("union '" + name + "'") "the vocabulary does not declare it"
        | WireForm.Node ->
            // At the floor a REQUIRED node cannot be omitted, so the shallowest legal
            // one is produced instead: a kind whose required fields reach no further
            // node. See [[WireForm.reachesNodeAtFloor]] for why the guard exists.
            let tags =
                if depth <= 0 then
                    atFloor idl idl.Kinds |> List.map _.Tag
                else
                    idl.Kinds |> List.map _.Tag

            sampleNode idl r (depth - 1) (pickAt r "a node slot (the vocabulary's kinds)" tags)
        // A bare kind and an op draw their fields through [[sampleFields]] (Phase 292), so
        // an `Optional` field is sometimes absent, an `OmitDefault` one sometimes at its
        // default and a `HostOnly` one never drawn — the presence rules every other owner's
        // fields are drawn under, which these two arms used to ignore.
        | WireForm.Kind ->
            let k =
                if depth <= 0 then
                    pickAt r "a bare-kind slot" (atFloor idl idl.Kinds)
                else
                    pickAt r "a bare-kind slot (the vocabulary's kinds)" idl.Kinds

            VUnion(k.Tag, sampleFields idl r (depth - 1) k.Fields)
        // At the floor an op is still drawn — the old `VStr "?"` there was a value the
        // encoder refused — from the ops whose required fields recurse no further.
        | WireForm.Op ->
            let o =
                pickAt r "an op slot (the vocabulary's ops)" (if depth <= 0 then atFloor idl idl.Ops else idl.Ops)

            VUnion(o.Tag, sampleFields idl r (depth - 1) o.Fields)

    and private sampleFields (idl: Idl) (r: Rng) (depth: int) (fields: IdlField list) : (string * IdlValue) list =
        fields
        |> List.map (fun f ->
            let v =
                match f.Opt with
                // Host-only fields have no wire projection, so there is nothing to sample.
                | HostOnly -> VAbsent
                | Required -> sampleType idl r depth f.Type
                // At the depth floor a node-reaching OPTIONAL is forced to its absent
                // form — the other half of the termination guard, and the half that
                // stops the node ENVELOPE recursing (`state` → `StateBehaviour` →
                // `onEmpty`/`onLoading`). No RNG is drawn, which is what keeps a
                // floor-safe vocabulary's seeded stream byte-identical.
                | Optional when depth <= 0 && WireForm.reachesNodeAtFloor idl Set.empty f.Type -> VAbsent
                | OmitDefault d when depth <= 0 && WireForm.reachesNodeAtFloor idl Set.empty f.Type -> d
                // Sample BOTH sides of every presence rule: an optional that is
                // sometimes absent, and an omit-when-default that sits at its
                // default often enough to exercise the omission path.
                | Optional ->
                    if intBelow r 3 = 0 then
                        VAbsent
                    else
                        sampleType idl r depth f.Type
                | OmitDefault d ->
                    if intBelow r 2 = 0 then
                        d
                    else
                        sampleType idl r depth f.Type

            f.Name, v)
        // Phase 539 — a member whose presence condition fails over the draw is drawn ABSENT, as the
        // decoder reads it (its optionality's absence), so a sample is a canonical value. No RNG is
        // drawn here, which keeps every vocabulary without a condition's seeded stream byte-identical.
        |> fun drawn ->
            drawn
            |> List.map (fun (name, v) ->
                match fields |> List.tryFind (fun f -> f.Name = name) with
                | Some({ Keyed = false; Presence = Some c } as f) when not (WireForm.holdsAuthored fields c drawn) ->
                    match f.Opt with
                    | OmitDefault d -> name, d
                    | _ -> name, VAbsent
                | _ -> name, v)

    /// Phase 698 — the node ENVELOPE, sampled from [[Idl.NodeFields]] through the
    /// same [[sampleFields]] the kind fields use, so both presence polarities are
    /// drawn on a node field exactly as on a kind field (`Optional` absent 1-in-3,
    /// `OmitDefault` at its default 1-in-2, `HostOnly` never present).
    ///
    /// `VAbsent` entries are dropped so "the envelope is empty" is a shape, not a
    /// list of absences — which is what lets an envelope-free draw stay a plain
    /// [[VNode]] and keeps every seeded stream that predates this byte-identical.
    /// An IDL declaring no envelope draws nothing at all from the RNG.
    ///
    /// **This closes the Phase 690 limitation that stood here.** That note recorded
    /// that a sampled node carried no envelope, so `state` / `style` /
    /// `accessibility` were reachable only by the GENERATED codecs and cross-host
    /// envelope parity was unproven — covered by one corpus fixture rather than by
    /// the generative sweep. It is proven now: the generative sweep draws the envelope
    /// and the cross-host legs compare it across the interpreter, the generated F#
    /// module and the generated TypeScript module on every vector, so removing it
    /// from any one leg fails from vector 0. The instance that ran at a whole
    /// domain's scale left with that domain's vocabulary (DECISIONS.md D14); what
    /// certifies the claim here is `IdlSpikeTests`' generative sweep and the vendored
    /// second-vocabulary spike's three-way comparison.
    and private sampleEnvelope (idl: Idl) (r: Rng) (depth: int) : (string * IdlValue) list =
        sampleFields idl r depth idl.NodeFields
        |> List.filter (fun (_, v) -> v <> VAbsent)

    and private sampleNode (idl: Idl) (r: Rng) (depth: int) (kindTag: string) : IdlValue =
        let id = pick r [ "n"; "node-1"; "a\"b"; "" ]
        let envelope = sampleEnvelope idl r depth

        let fields =
            match idl.Kinds |> List.tryFind (fun k -> k.Tag = kindTag) with
            | Some k -> sampleFields idl r depth k.Fields
            | None -> refuse ("kind '" + kindTag + "'") "the vocabulary does not declare it"

        match envelope with
        | [] -> VNode(id, kindTag, fields)
        | env -> VNodeEnv(id, env, kindTag, fields)

    /// `count` deterministic sample nodes over `kindTags`, cycling the tags so the
    /// vocabulary is covered evenly rather than by chance. Same seed gives the
    /// same vectors on any host and any runtime.
    ///
    /// **Total (Phase 292).** A vocabulary the sampler cannot draw from — no tag given (the
    /// cycle used to divide by zero), a tag naming no kind, an empty enum, union or op set
    /// reached by a slot, a name the vocabulary does not declare, an unbound type variable,
    /// a type with no finite value — is a typed [[SampleFault]], and every value it does
    /// draw is one the encoder accepts: there is no placeholder left that it refuses.
    let trySampleNodes
        (idl: Idl)
        (kindTags: string list)
        (seed: int)
        (count: int)
        : Result<IdlValue list, SampleFault> =
        let r = ofSeed seed

        match kindTags with
        | [] when count > 0 ->
            Error
                { At = "the kind tags"
                  Reason = "none was given to cycle over" }
        | _ ->
            try
                // The tags in order, repeated — a deterministic cycle, not a draw, so it takes no
                // RNG. `kindTags` is non-empty whenever `count > 0` (refused above), so the
                // truncation always terminates.
                let tags =
                    Seq.initInfinite (fun _ -> kindTags)
                    |> Seq.concat
                    |> Seq.truncate (max 0 count)
                    |> Seq.toList

                Ok [ for tag in tags -> sampleNode idl r 3 tag ]
            with Unsampleable(at, reason) ->
                Error { At = at; Reason = reason }
