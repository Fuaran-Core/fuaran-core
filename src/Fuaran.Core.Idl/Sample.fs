namespace Fuaran.Core.Idl

open Fuaran.Core

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
    // explicit LCG rather than `System.Random`, whose sequence is not
    // contractually stable across runtimes — a vector that fails elsewhere has
    // to reproduce here from its seed alone.
    // -----------------------------------------------------------------------

    type private Rng = { mutable State: uint64 }

    let private nextInt (r: Rng) : int =
        r.State <- r.State * 6364136223846793005UL + 1442695040888963407UL
        int ((r.State >>> 33) &&& 0x7FFFFFFFUL)

    let private pick (r: Rng) (xs: 'a list) : 'a = xs.[nextInt r % List.length xs]

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

    /// Whether sampling a value of `t` **at the depth floor** can still reach a
    /// `TNode` — the sampler's termination predicate (Phase 698).
    ///
    /// It mirrors [[sampleType]]'s own floor behaviour exactly rather than being a
    /// conservative over-approximation: a list/map is EMPTY at the floor and so
    /// reaches nothing, a union prefers its nullary cases, and an optional field is
    /// forced absent there — which leaves bare nodes and required record fields as
    /// the only surviving paths. Mirroring is what keeps the guard from changing a
    /// single draw on a vocabulary that never needed it: `miniIdl`'s only
    /// node-bearing field is `Box.children`, a LIST, so it is floor-safe and its
    /// seeded stream is untouched.
    ///
    /// **Why this is needed at all.** A bare `TNode` was the one recursion site with
    /// no floor arm — `TList` and `TUnion` both have one — so a vocabulary that
    /// reaches a node from a node by any non-list path recursed until the stack went.
    /// The real vocabulary has exactly that: `ErrorBoundary.child`/`.fallback` and
    /// `Switch.default` on the kind side, and `StateBehaviour.onEmpty`/`.onLoading`
    /// reached through the node ENVELOPE. The envelope path is why this surfaced
    /// only when the sampler learned to draw envelopes — with `state` present 2 in 3
    /// and two node slots behind it, the branching factor crosses 1 and the sampled
    /// tree does not terminate.
    let rec private reachesNodeAtFloor (idl: Idl) (seen: Set<string>) (t: IdlType) : bool =
        match t with
        // A kind / op carries whatever its fields carry, and neither has a floor arm
        // of its own; treat both as reaching, which is also true in practice.
        | TNode
        | TKind
        | TOp -> true
        // Empty at the floor, so nothing inside them is ever sampled there.
        | TList _
        | TMap _ -> false
        | TRecord n when not (Set.contains ("r:" + n) seen) ->
            match idl.Records |> List.tryFind (fun rc -> rc.Name = n) with
            | Some rc ->
                rc.Fields
                |> List.exists (fun f -> f.Opt = Required && reachesNodeAtFloor idl (Set.add ("r:" + n) seen) f.Type)
            | None -> false
        | TUnion(n, args) when not (Set.contains ("u:" + n) seen) ->
            let seen' = Set.add ("u:" + n) seen

            args |> List.exists (reachesNodeAtFloor idl seen')
            || (match idl.Unions |> List.tryFind (fun u -> u.Name = n) with
                | Some u ->
                    // The floor prefers a nullary case when the union has one, and a
                    // nullary case has no fields — so only a union WITHOUT one can
                    // still reach a node here.
                    let candidates =
                        match u.Cases |> List.filter (fun c -> List.isEmpty c.Fields) with
                        | [] -> u.Cases
                        | nullary -> nullary

                    candidates
                    |> List.exists (fun c ->
                        c.Fields
                        |> List.exists (fun f -> f.Opt = Required && reachesNodeAtFloor idl seen' f.Type))
                | None -> false)
        | _ -> false

    /// The kind tags a node may take AT THE DEPTH FLOOR: those whose required fields
    /// reach no further node, so the recursion stops there. Falls back to the whole
    /// vocabulary when a domain declares no such kind — the sampler must still
    /// produce a node for a required slot, and a domain with no leaf kind has no
    /// finite node at all, which is its own defect rather than one to hide here.
    let private floorKindTags (idl: Idl) : string list =
        let leaves =
            idl.Kinds
            |> List.filter (fun k ->
                k.Fields
                |> List.forall (fun f -> f.Opt <> Required || not (reachesNodeAtFloor idl Set.empty f.Type)))

        match leaves with
        | [] -> idl.Kinds |> List.map (fun k -> k.Tag)
        | ks -> ks |> List.map (fun k -> k.Tag)

    let rec private sampleType (idl: Idl) (r: Rng) (depth: int) (t: IdlType) : IdlValue =
        match t with
        | TStr -> VStr(pick r stringPool)
        | TInt -> VInt(pick r intPool)
        | TBool -> VBool(nextInt r % 2 = 0)
        | TFloat -> VFloat(pick r floatPool)
        | TClosure
        | TFn _ -> VClosure
        | TOpaque -> VOpaque
        // Phase 676 — sample real JSON, built from the SAME adversarial pools, so the
        // passthrough is stressed on escaping and float layout like every other leg.
        // A hosted slot samples the same way: both the interpreter and the TS backend
        // carry it verbatim, so arbitrary JSON stresses exactly what they share.
        | TJson
        | THosted _ ->
            VJson(
                match nextInt r % 4 with
                | 0 -> JStr(pick r stringPool)
                | 1 -> JFloat(pick r floatPool)
                | 2 -> JArr [ JInt(pick r intPool); JStr(pick r stringPool) ]
                | _ -> JObj [ "z", JInt(pick r intPool); "a", JStr(pick r stringPool) ]
            )
        | TVar _ -> VStr(pick r stringPool)
        | TEnum name ->
            match idl.Enums |> List.tryFind (fun e -> e.Name = name) with
            | Some e -> VEnum(pick r e.WireCases)
            | None -> VStr "?"
        | TList inner ->
            // Bounded, and empty is a legitimate sample — an empty collection is
            // NOT absence, and the two must stay distinguishable on the wire.
            let n = if depth <= 0 then 0 else nextInt r % 3
            VList [ for _ in 1..n -> sampleType idl r (depth - 1) inner ]
        | TMap vt ->
            let n = if depth <= 0 then 0 else nextInt r % 3
            VMap [ for i in 1..n -> (sprintf "k%d" i), sampleType idl r (depth - 1) vt ]
        | TRecord name ->
            match idl.Records |> List.tryFind (fun rc -> rc.Name = name) with
            | Some rc -> VRecord(sampleFields idl r (depth - 1) rc.Fields)
            | None -> VRecord []
        | TUnion(name, args) ->
            match idl.Unions |> List.tryFind (fun u -> u.Name = name) with
            | Some u ->
                // At the depth floor prefer a nullary case when one exists, so a
                // recursive union terminates rather than being truncated.
                let candidates =
                    if depth <= 0 then
                        match u.Cases |> List.filter (fun c -> List.isEmpty c.Fields) with
                        | [] -> u.Cases
                        | nullary -> nullary
                    else
                        u.Cases

                let c = pick r candidates

                // Substitute the type parameter RECURSIVELY. A shallow swap leaves
                // `TList (TVar "T")` alone, so the sampler would generate a string
                // where the slot's codec expects a float — which surfaces as an
                // unrelated "not iterable" inside the TS escaper rather than as a
                // real divergence.
                let rec subst (ft: IdlType) =
                    match ft with
                    | TVar _ ->
                        match args with
                        | a :: _ -> a
                        | [] -> ft
                    | TList inner -> TList(subst inner)
                    | TMap vt -> TMap(subst vt)
                    | TUnion(n, uargs) -> TUnion(n, uargs |> List.map subst)
                    | _ -> ft

                VUnion(
                    c.Tag,
                    sampleFields idl r (depth - 1) (c.Fields |> List.map (fun f -> { f with Type = subst f.Type }))
                )
            | None -> VUnion("?", [])
        | TNode ->
            // At the floor a REQUIRED node cannot be omitted, so the shallowest legal
            // one is produced instead: a kind whose required fields reach no further
            // node. See [[reachesNodeAtFloor]] for why the guard exists.
            let tags =
                if depth <= 0 then
                    floorKindTags idl
                else
                    idl.Kinds |> List.map (fun k -> k.Tag)

            sampleNode idl r (depth - 1) (pick r tags)
        | TKind ->
            let k = pick r idl.Kinds
            VUnion(k.Tag, [ for f in k.Fields -> f.Name, sampleType idl r (depth - 1) f.Type ])
        | TOp when depth <= 0 || List.isEmpty idl.Ops -> VStr "?"
        | TOp ->
            let o = pick r idl.Ops
            VUnion(o.Tag, [ for f in o.Fields -> f.Name, sampleType idl r (depth - 1) f.Type ])

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
                | Optional when depth <= 0 && reachesNodeAtFloor idl Set.empty f.Type -> VAbsent
                | OmitDefault d when depth <= 0 && reachesNodeAtFloor idl Set.empty f.Type -> d
                // Sample BOTH sides of every presence rule: an optional that is
                // sometimes absent, and an omit-when-default that sits at its
                // default often enough to exercise the omission path.
                | Optional ->
                    if nextInt r % 3 = 0 then
                        VAbsent
                    else
                        sampleType idl r depth f.Type
                | OmitDefault d ->
                    if nextInt r % 2 = 0 then
                        d
                    else
                        sampleType idl r depth f.Type

            f.Name, v)

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
    /// the generative sweep. It is proven now: `IdlFullVocabularyFuzzTests` compares
    /// the envelope across the interpreter, the generated F# module and the
    /// generated TypeScript module on every vector, and removing it from any one leg
    /// fails the sweep from vector 0.
    and private sampleEnvelope (idl: Idl) (r: Rng) (depth: int) : (string * IdlValue) list =
        sampleFields idl r depth idl.NodeFields
        |> List.filter (fun (_, v) -> v <> VAbsent)

    and private sampleNode (idl: Idl) (r: Rng) (depth: int) (kindTag: string) : IdlValue =
        let id = pick r [ "n"; "node-1"; "a\"b"; "" ]
        let envelope = sampleEnvelope idl r depth

        let fields =
            match idl.Kinds |> List.tryFind (fun k -> k.Tag = kindTag) with
            | Some k -> sampleFields idl r depth k.Fields
            | None -> []

        match envelope with
        | [] -> VNode(id, kindTag, fields)
        | env -> VNodeEnv(id, env, kindTag, fields)

    /// `count` deterministic sample nodes over `kindTags`, cycling the tags so the
    /// vocabulary is covered evenly rather than by chance. Same seed gives the
    /// same vectors on any host and any runtime.
    let sampleNodes (idl: Idl) (kindTags: string list) (seed: int) (count: int) : IdlValue list =
        let r = { State = uint64 seed * 2862933555777941757UL + 3037000493UL }

        [ for i in 0 .. count - 1 -> sampleNode idl r 3 (kindTags.[i % List.length kindTags]) ]
