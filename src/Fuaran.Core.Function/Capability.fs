namespace Fuaran.Core

// ============================================================================
//  Invocable capability (Phase 30) — `Fuaran.Core.Function` generalised from "a
//  signature you can project to a tool schema" into a *registrable, invocable
//  runtime capability* with a typed registry an agent can enumerate. This is how
//  arbitrary compute (model inference, scipy, custom numerics, any host body)
//  enters a Fuaran app without code on the wire: the wire carries a typed
//  declaration + invocation; the host provides the body, placed where it must run.
//  A non-`Deterministic` invocation's realized value is journaled through the
//  Phase 27 determinism-capture seam for exact replay. (Compute Layer spec §3–§5.)
// ============================================================================

/// Where a capability's body runs (spec §5) — a typed, portable placement contract, not a
/// deploy-time accident. A `ClientIsland` carries its sub-kind.
type IslandKind =
    /// Python in the browser; wire tag `pyodide`.
    | Pyodide
    /// F# compiled to JavaScript; wire tag `fable`.
    | Fable
    /// Plain JavaScript; wire tag `js`.
    | Js

/// Where the host runs a capability's body. The core only carries and round-trips it — no
/// operation here reads it — so routing by it is the host's.
type Placement =
    /// Run when the app is built, not on request; wire tag `buildTime`.
    | BuildTime
    /// Run on a server, so the body may answer `Pending`; wire tag `server`.
    | Server
    /// Run in the client without a code island; wire tag `clientDeclarative`.
    | ClientDeclarative
    /// Run in the client inside a code island of the given kind, so the body may answer `Pending`;
    /// wire tag `clientIsland` with the kind as `island`.
    | ClientIsland of IslandKind
    /// Served from a result computed ahead of time; wire tag `precomputed`.
    | Precomputed

/// A registrable, invocable runtime capability. `Signature` (incl. its `Effect`) is reused
/// verbatim from the artifact-function surface; `Placement` routes the host body. The wire carries
/// this declaration + a typed invocation, never the body.
type Capability =
    {
        /// The registry key; a second registration under the same id is refused `DuplicateCapability`.
        Id: string
        /// The arguments it takes and the effect it declares; registration refuses a non-total one.
        Signature: Signature
        /// Where its body runs; carried, never acted on, by the core.
        Placement: Placement
    }

    /// The capture-keying axis: the signature's effect determinism, DERIVED (Phase 295). It was a
    /// field the smart constructor filled, so a hand-built record could disagree with its own
    /// signature — journalled under one label, re-decoded under another, and refused by its own
    /// codec. A member cannot disagree.
    member c.Determinism: DeterminismSource = c.Signature.Effect.Determinism

/// Why a typed invocation (or a registration) was refused — total, names the failure and, where a
/// closed set is expected, enumerates the alternatives (GP5). Default-deny by shape: only a
/// registered id with in-space args dispatches.
type InvokeError =
    /// Dispatch named an id the registry does not hold; `known` lists every registered id, by id.
    | NoSuchCapability of id: string * known: string list
    /// Registration under an id the registry already holds; the registry is left unchanged.
    | DuplicateCapability of id: string
    /// An argument addresses no hole of the signature; `declared` lists every hole address.
    | UnknownArg of addr: string * declared: string list
    /// An argument's value `got` is not in its hole's `space`; for a slot, a tree of the wrong kind.
    | ArgOutOfSpace of addr: string * space: ValueSpace * got: string
    /// These required holes received no argument, in declaration order.
    | RequiredArgsUnbound of addrs: string list
    /// The hole cannot take a caller's value at all: an action hole, or a slot sent something that
    /// is not a `"kind"`-tagged object.
    | UninvocableArg of addr: string
    /// The arguments validated and the body ran, answering `Failed reason`.
    | BodyFailed of reason: string
    /// A registration refused because the capability's signature is not total (Phase 295): the
    /// repeat holes it names range over an unbounded count space, or an entry projects to no hole
    /// kind. Declared last, so every earlier case keeps its tag.
    | NonTotalCapability of id: string * addrs: string list

/// What a model reads when an invocation is refused (Phase 251). The union names the failure and
/// carries the alternatives in its fields; `describe` turns a case into one plain sentence that
/// keeps both — every case that refuses against a closed set (`NoSuchCapability`, `UnknownArg`,
/// `ArgOutOfSpace`, `RequiredArgsUnbound`) names the members of that set, so the next attempt has
/// something to aim at. The wire form of the same value is `CapabilityCodec.invokeErrorJson`.
module InvokeError =

    /// One sentence a model can act on, naming the failure and what would be accepted.
    let describe (e: InvokeError) : string =
        match e with
        | NoSuchCapability(id, []) ->
            "Refused: there is no tool '"
            + id
            + "' you may call. There are no tools you may call."
        | NoSuchCapability(id, known) ->
            "Refused: there is no tool '"
            + id
            + "' you may call. The tools you may call are "
            + Space.quoteAll known
            + "."
        | DuplicateCapability id -> "Refused: the tool '" + id + "' is registered twice."
        | UnknownArg(addr, []) ->
            "Refused: '"
            + addr
            + "' is not an argument of this tool. It takes no arguments."
        | UnknownArg(addr, declared) ->
            "Refused: '"
            + addr
            + "' is not an argument of this tool. Its arguments are "
            + Space.quoteAll declared
            + "."
        | ArgOutOfSpace(addr, space, got) ->
            "Refused: argument '"
            + addr
            + "' must be "
            + Space.describe space
            + "; you sent '"
            + got
            + "'."
        | RequiredArgsUnbound addrs -> "Refused: required arguments missing: " + Space.quoteAll addrs + "."
        | UninvocableArg addr ->
            "Refused: argument '"
            + addr
            + "' cannot take the value sent. A tree argument takes a JSON object with a \"kind\"; an action is bound by the host and never by a caller."
        | BodyFailed reason -> "Refused: the tool ran and failed: " + reason + "."
        | NonTotalCapability(id, addrs) ->
            "Refused: the tool '"
            + id
            + "' cannot be registered, because it is not total: "
            + Space.quoteAll addrs
            + " must each be a repeat over a bounded count, or a declared hole."

    /// Every refusal, one sentence per line, in the order given — the reading of
    /// `Capability.validateArgsAll`'s answer, so a call with two bad arguments is answered once.
    let describeAll (es: InvokeError list) : string =
        es |> List.map describe |> String.concat "\n"

/// A validated argument, TYPED by the value space it was checked against (Phase 251) — what
/// `Capability.invokeWithArgs` hands a body, so the body reads the value `validateArgs` already
/// accepted instead of parsing the string again. `IntRange` arguments arrive as `IntValue`,
/// `FloatRange` as `FloatValue`, `StringLen` / `Enum` / `AnyString` as `TextValue`, and a
/// `SlotTree` argument as `TreeValue`, the parsed wire document (decoding it into the domain's node
/// stays the host's, per the witness pattern).
type ArgValue =
    /// An `IntRange` argument, within its bounds.
    | IntValue of int
    /// A `FloatRange` argument, finite and within its bounds.
    | FloatValue of float
    /// A `StringLen`, `Enum` or `AnyString` argument, exactly as sent.
    | TextValue of string
    /// A `SlotTree` argument, parsed but not decoded into any domain node.
    | TreeValue of JVal

/// The invocable-capability surface: the typed registry (populate + enumerate + dispatch), the
/// arg-validation contract, the Phase 27 capture keying, and the wire codec. Additive over the
/// `Function` surface; FSharp.Core-only, Fable-clean.
module Capability =

    /// Build a capability. Its `Determinism` is the signature's effect determinism, by derivation
    /// (Phase 295), so the two cannot disagree.
    let create (id: string) (sg: Signature) (placement: Placement) : Capability =
        { Id = id
          Signature = sg
          Placement = placement }

    /// The entries that make the signature non-total (Phase 295): a repeat over an unbounded count
    /// space, or an entry that projects to no hole kind. Empty exactly when `Function.isTotal`.
    let internal nonTotalAddrs (c: Capability) : string list =
        c.Signature.Holes
        |> List.filter (fun e -> not (Function.isTotal { c.Signature with Holes = [ e ] }))
        |> List.map (fun e -> e.Addr)

    /// The registration refusal a non-total capability earns, or `None` (Phase 295).
    let internal totalityFault (c: Capability) : InvokeError option =
        match nonTotalAddrs c with
        | [] -> None
        | addrs -> Some(NonTotalCapability(c.Id, addrs))

    /// The Phase 27 determinism label this capability keys its captures on (`"deterministic"` /
    /// `"clock"` / `"random"` / `"network"`).
    let determinismTag (c: Capability) : string = Effect.determinismTag c.Determinism

    /// The effect-identity key the Phase 27 capture seam journals a non-deterministic invocation
    /// under: the capability id + a hash of the canonical (addr-sorted) pre-image, built through
    /// `Hash.canonicalFields` — two fields per binding (addr, value). So two invocations with the
    /// same args replay the same captured value, and the pre-image is INJECTIVE (Phase 225,
    /// `invocation_key_injective` in `proofs/Capability.fst`): distinct argument sets never share
    /// one, whatever their values contain. That two distinct pre-images hash apart is a property
    /// of `Hash.fnv1a` and is not claimed. A consumer threads this as `OpStream.captureEffect`'s
    /// `eff` argument.
    ///
    /// Since Phase 295 each argument is keyed by its CANONICAL spelling in its hole's space
    /// (`Space.canonical`) where it has one — `05` and `5` for an integer hole, `1.50` and `1.5`
    /// for a number hole, are one value and one key — and by its own spelling otherwise, so
    /// injectivity holds over the canonical argument lists.
    let invocationKey (c: Capability) (args: (string * string) list) : string =
        let spelled (a: string, v: string) =
            match
                c.Signature.Holes
                |> List.tryFind (fun h -> h.Addr = a)
                |> Option.bind (fun h -> h.Space)
            with
            | Some space -> a, (Space.canonical space v |> Option.defaultValue v)
            | None -> a, v

        let canonical =
            args
            |> List.map spelled
            |> List.sortBy fst
            |> List.collect (fun (a, v) -> [ a; v ])
            |> Hash.canonicalFields

        c.Id + "#" + Hash.fnv1a canonical

    /// Validate typed `args` (addr → string value) against the capability's signature *before*
    /// dispatch: every arg must address a declared data hole and lie in its space; every required
    /// hole must be bound. A slot hole is invocable since Phase 229: its space is `SlotTree`, so its
    /// argument is a wire document (a `"kind"`-tagged object) whose kind satisfies the constraint —
    /// a tree of the wrong kind is `ArgOutOfSpace` naming the addr and the `SlotTree` constraint,
    /// and an argument that is no tree at all (a scalar, a malformed document) is `UninvocableArg`.
    /// A spaceless entry (an action hole) stays `UninvocableArg` for every argument. The host validates this
    /// before running any body (default-deny by shape, FGP 3). `Required` is checked as the hole's
    /// PRESENCE among the args, which here is presence of a value: an arg is a string checked
    /// against its space, and a value space has no absent marker, so a required hole cannot be
    /// bound to "no value" the way a `Query` param bound to `Null` could before Phase 226.
    let validateArgs (c: Capability) (args: (string * string) list) : Result<unit, InvokeError> =
        let holes = c.Signature.Holes
        let declared = holes |> List.map (fun h -> h.Addr)
        let argMap = Map.ofList args

        // 1. every arg addresses a declared hole, and that hole takes a scalar value in-space.
        let rec checkArgs =
            function
            | [] -> Ok()
            | (addr, value) :: rest ->
                match holes |> List.tryFind (fun h -> h.Addr = addr) with
                | None -> Error(UnknownArg(addr, declared))
                | Some h ->
                    match h.Space with
                    | None -> Error(UninvocableArg addr) // a spaceless (action) hole
                    | Some(SlotTree _) when (Space.slotKindOf value).IsNone -> Error(UninvocableArg addr) // no tree
                    | Some space ->
                        if Space.validate space value then
                            checkArgs rest
                        else
                            Error(ArgOutOfSpace(addr, space, value))

        checkArgs args
        |> Result.bind (fun () ->
            // 2. every required hole is bound.
            let unbound =
                holes
                |> List.filter (fun h -> h.Required && not (Map.containsKey h.Addr argMap))
                |> List.map (fun h -> h.Addr)

            if List.isEmpty unbound then
                Ok()
            else
                Error(RequiredArgsUnbound unbound))

    /// Invoke a capability: validate the args, then run the host `body`. Deterministic capabilities
    /// re-evaluate freely; for a non-`Deterministic` capability the caller journals the realized
    /// value via `OpStream.captureEffect` keyed by `invocationKey` + `determinismTag` (Phase 27),
    /// so the invocation replays exactly. A body failure is a named `BodyFailed`, never a throw.
    ///
    /// Since Phase 210 the body answers in the `Deferred` envelope declared above — a body placed on
    /// a `Server` or in a `ClientIsland` cannot settle synchronously, and `Deferred` was put in this
    /// package for exactly that. A synchronous body wraps its value in `Ready`. The ASYNC axis rides
    /// the envelope; the ERROR axis stays typed on the outer `Result`, with a body's `Failed m`
    /// projected into the enumerated `BodyFailed m`. So an invocation has exactly three outcomes —
    /// SETTLED (`Ok(Ready v)`), PENDING (`Ok Pending`) and REFUSED (`Error e`, typed) — and
    /// `Ok(Failed _)` is unreachable by construction, which `capabilityLaws` certifies and
    /// `proofs/Capability.fst`'s `invoke_never_ok_failed` proves rather than this comment asserting.
    /// The same shape `Query.invoke` carries (Phase 198): one async shape across both seams.
    let invoke
        (c: Capability)
        (args: (string * string) list)
        (body: unit -> Deferred<'v>)
        : Result<Deferred<'v>, InvokeError> =
        validateArgs c args
        |> Result.bind (fun () ->
            match body () with
            | Ready v -> Ok(Ready v)
            | Pending -> Ok Pending
            | Failed m -> Error(BodyFailed m))

    // ---- every refusal at once, and the validated arguments handed on (Phase 251) ----

    /// The refusal one argument earns on its own, by the rules `validateArgs` applies to it.
    let internal argFault (c: Capability) (declared: string list) (addr: string, value: string) : InvokeError option =
        match c.Signature.Holes |> List.tryFind (fun h -> h.Addr = addr) with
        | None -> Some(UnknownArg(addr, declared))
        | Some h ->
            match h.Space with
            | None -> Some(UninvocableArg addr)
            | Some(SlotTree _) when (Space.slotKindOf value).IsNone -> Some(UninvocableArg addr)
            | Some space ->
                if Space.validate space value then
                    None
                else
                    Some(ArgOutOfSpace(addr, space, value))

    /// Validate as `validateArgs` does, but answer with EVERY refusal rather than the first: one
    /// per refused argument, in argument order, then `RequiredArgsUnbound` naming every required
    /// hole left out. So a call with two bad arguments is refused naming both, and a model needs one
    /// round trip, not two. The first-failure form is kept, and the two agree by construction of
    /// their order: the head of this list is exactly `validateArgs`'s refusal, and `Ok ()` here is
    /// `Ok ()` there.
    let validateArgsAll (c: Capability) (args: (string * string) list) : Result<unit, InvokeError list> =
        let holes = c.Signature.Holes
        let declared = holes |> List.map (fun h -> h.Addr)
        let argMap = Map.ofList args
        let faults = args |> List.choose (argFault c declared)

        let unbound =
            holes
            |> List.filter (fun h -> h.Required && not (Map.containsKey h.Addr argMap))
            |> List.map (fun h -> h.Addr)

        let all =
            if List.isEmpty unbound then
                faults
            else
                faults @ [ RequiredArgsUnbound unbound ]

        if List.isEmpty all then Ok() else Error all

    /// One argument as the value its space admits, or `None` where the string is not in it.
    let private typedValue (space: ValueSpace option) (value: string) : ArgValue option =
        match space with
        | Some(IntRange _) -> Space.readInt value |> Option.map IntValue
        | Some(FloatRange _) -> Space.readFloat value |> Option.map FloatValue
        | Some(StringLen _)
        | Some(Enum _)
        | Some AnyString -> Some(TextValue value)
        | Some(SlotTree _) ->
            match Json.parse value with
            | Ok el -> Some(TreeValue el)
            | Error _ -> None
        | None -> None

    /// Validate `args` (`validateArgs`), then type each one by the space it was checked against —
    /// the list `invokeWithArgs` hands a body, in argument order. The parses are the ones
    /// `Space.validate` made, so an argument that validated always types; were one ever not to, it
    /// is refused as `ArgOutOfSpace` rather than handed on untyped.
    let typeArgs (c: Capability) (args: (string * string) list) : Result<(string * ArgValue) list, InvokeError> =
        validateArgs c args
        |> Result.bind (fun () ->
            let rec go (acc: (string * ArgValue) list) =
                function
                | [] -> Ok(List.rev acc)
                | (addr, value) :: rest ->
                    let space =
                        c.Signature.Holes
                        |> List.tryPick (fun h -> if h.Addr = addr then h.Space else None)

                    match typedValue space value with
                    | Some v -> go ((addr, v) :: acc) rest
                    | None ->
                        match space with
                        | Some sp -> Error(ArgOutOfSpace(addr, sp, value))
                        | None -> Error(UninvocableArg addr)

            go [] args)

    /// `invoke`, with the body handed the validated arguments, typed (`typeArgs`) — so the body
    /// reads the values validation accepted instead of closing over the caller's list and parsing it
    /// again, and the list it reads is the list that was checked. Additive beside `invoke`; the same
    /// three outcomes, and a body's `Failed m` is projected into `BodyFailed m` exactly as there.
    let invokeWithArgs
        (c: Capability)
        (args: (string * string) list)
        (body: (string * ArgValue) list -> Deferred<'v>)
        : Result<Deferred<'v>, InvokeError> =
        typeArgs c args
        |> Result.bind (fun typed ->
            match body typed with
            | Ready v -> Ok(Ready v)
            | Pending -> Ok Pending
            | Failed m -> Error(BodyFailed m))

/// A typed capability registry — the discovery surface an agent enumerates (the compute analogue of
/// node-introspection): "what compute may I invoke, with what typed args". Default-deny by shape on
/// dispatch — only a registered id resolves.
type CapabilityRegistry =
    {
        /// Keyed by `Capability.Id`; every member was admitted by `register`, so each is total.
        Capabilities: Map<string, Capability>
    }

/// Populate / enumerate / dispatch the capability registry (named `Registry` until Phase 295,
/// which kept that name as an obsolete alias for the 0.34.0 draft). `ModuleSuffix`, so the module
/// and the type share a name, as `FunctionRegistry` does.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module CapabilityRegistry =

    /// The registry with no capabilities: every dispatch through it is `NoSuchCapability` with an
    /// empty `known` list.
    let empty: CapabilityRegistry = { Capabilities = Map.empty }

    /// Register a capability — additive, no silent overwrite (a duplicate id is a named error), and
    /// only a TOTAL one (Phase 295): a capability whose signature carries a repeat over an unbounded
    /// count is refused `NonTotalCapability`, naming the holes, rather than dispatched.
    let register (c: Capability) (r: CapabilityRegistry) : Result<CapabilityRegistry, InvokeError> =
        if Map.containsKey c.Id r.Capabilities then
            Error(DuplicateCapability c.Id)
        else
            match Capability.totalityFault c with
            | Some e -> Error e
            | None ->
                Ok
                    { r with
                        Capabilities = Map.add c.Id c r.Capabilities }

    /// The capability registered under exactly `id` (ordinal, case-sensitive), or `None`.
    let tryFind (id: string) (r: CapabilityRegistry) : Capability option = Map.tryFind id r.Capabilities

    /// Enumerate the registry in a stable order (by id) — the discovery surface; stability is part
    /// of the contract (`capabilityLaws` certifies it).
    let enumerate (r: CapabilityRegistry) : Capability list =
        r.Capabilities |> Map.toList |> List.map snd

    /// Dispatch an invocation through the registry: resolve the id (default-deny — an unregistered
    /// id is `NoSuchCapability`), then `Capability.invoke`. The host body is supplied by the caller
    /// per the resolved capability's placement, and answers in the `Deferred` envelope (Phase 210 —
    /// the same three outcomes `Capability.invoke` documents).
    let dispatch
        (r: CapabilityRegistry)
        (id: string)
        (args: (string * string) list)
        (body: Capability -> unit -> Deferred<'v>)
        : Result<Deferred<'v>, InvokeError> =
        match Map.tryFind id r.Capabilities with
        | None -> Error(NoSuchCapability(id, r.Capabilities |> Map.toList |> List.map fst))
        | Some c -> Capability.invoke c args (body c)

    /// `dispatch`, with the body handed the resolved capability and the validated arguments, typed
    /// (`Capability.invokeWithArgs`, Phase 251). Additive beside `dispatch`; default-deny the same.
    let dispatchWithArgs
        (r: CapabilityRegistry)
        (id: string)
        (args: (string * string) list)
        (body: Capability -> (string * ArgValue) list -> Deferred<'v>)
        : Result<Deferred<'v>, InvokeError> =
        match Map.tryFind id r.Capabilities with
        | None -> Error(NoSuchCapability(id, r.Capabilities |> Map.toList |> List.map fst))
        | Some c -> Capability.invokeWithArgs c args (body c)

/// The capability registry's former module name, kept for the 0.34.0 draft only (Phase 295): each
/// member forwards to `CapabilityRegistry`.
[<System.Obsolete("Registry is CapabilityRegistry since Phase 295; this alias is removed at the next draft.")>]
module Registry =

    /// Forwards to `CapabilityRegistry.empty`; removed at the next draft.
    let empty: CapabilityRegistry = CapabilityRegistry.empty

    /// Forwards to `CapabilityRegistry.register`; removed at the next draft.
    let register (c: Capability) (r: CapabilityRegistry) : Result<CapabilityRegistry, InvokeError> =
        CapabilityRegistry.register c r

    /// Forwards to `CapabilityRegistry.tryFind`; removed at the next draft.
    let tryFind (id: string) (r: CapabilityRegistry) : Capability option = CapabilityRegistry.tryFind id r

    /// Forwards to `CapabilityRegistry.enumerate`; removed at the next draft.
    let enumerate (r: CapabilityRegistry) : Capability list = CapabilityRegistry.enumerate r

    /// Forwards to `CapabilityRegistry.dispatch`; removed at the next draft.
    let dispatch
        (r: CapabilityRegistry)
        (id: string)
        (args: (string * string) list)
        (body: Capability -> unit -> Deferred<'v>)
        : Result<Deferred<'v>, InvokeError> =
        CapabilityRegistry.dispatch r id args body

    /// Forwards to `CapabilityRegistry.dispatchWithArgs`; removed at the next draft.
    let dispatchWithArgs
        (r: CapabilityRegistry)
        (id: string)
        (args: (string * string) list)
        (body: Capability -> (string * ArgValue) list -> Deferred<'v>)
        : Result<Deferred<'v>, InvokeError> =
        CapabilityRegistry.dispatchWithArgs r id args body

/// How a seam codec treats a member it does not know (Phase 251). `Lenient` — the default, and
/// what every decoder without a policy argument does — ignores it, so a reader tolerates a writer
/// one version ahead. `Strict` refuses it, naming the member and the members that WOULD be read:
/// for input from a model, an ignored member is a claim that silently went unread (an `"actor"`
/// beside an invocation, say), and the refusal is what tells the model so.
[<RequireQualifiedAccess>]
type ReadPolicy =
    /// Read past members the decoder does not know; the policy of every reader without a policy argument.
    | Lenient
    /// Refuse the first unknown member, naming it and the members that would have been read.
    | Strict

/// The canonical wire codec for a `Capability` declaration + a typed invocation record. Round-trips
/// the full `Signature` (so an enumerated capability re-serialises identically), the determinism
/// tag, and the placement. Fable-clean (`Json` / `Decode`).
module CapabilityCodec =

    // ---- value-space ----

    // The value space is `SpaceCodec`'s and the effect `EffectCodec`'s (Phase 295): one writer and
    // one reader each, shared with every other codec that carries one.

    // Phase 310 — every member is read through the typed decode layer (`Decoder`), so a refusal
    // carries a code and the path to the value at fault (`decodeJsonDetailedWith` and its
    // siblings); the string forms answer the sentence this codec has always answered. One reading
    // changed: an optional `slotKind` that is present and not a string is refused, where it was
    // read as absent.

    /// Two members of one object, combined.
    let private both (a: Decoder<'A>) (b: Decoder<'B>) (f: 'A -> 'B -> 'C) : Decoder<'C> =
        a |> Decoder.bind (fun x -> b |> Decoder.map (f x))

    /// Dispatch on `$type`; a miss keeps this codec's sentence `<what><tag>`.
    let private dispatch (what: string) (cases: (string * Decoder<'T>) list) : Decoder<'T> =
        fun el ->
            Decoder.tagDispatch "$type" cases el
            |> Result.mapError (fun e ->
                match e.Code, e.Path, Decoder.tryMember "$type" el with
                | DecodeCode.UnknownTag, [ PathSegment.Key "$type" ], Some(JStr other) ->
                    { e with Message = what + other }
                | _ -> e)

    /// A string from a closed set; a miss is `UnknownTag`, in this codec's sentence `<what><value>`.
    let private tagged (what: string) (cases: (string * 'T) list) : Decoder<'T> =
        Decoder.str
        |> Decoder.andThen (fun s ->
            match cases |> List.tryFind (fun (k, _) -> k = s) with
            | Some(_, v) -> Ok v
            | None ->
                Error(
                    DecodeError.make
                        DecodeCode.UnknownTag
                        ("one of "
                         + (cases |> List.map (fun (k, _) -> "'" + k + "'") |> String.concat ", "))
                        (what + s)
                ))

    // ---- signature entry + signature ----

    let private entryJson (e: SigEntry) : JVal =
        [ "addr", JStr e.Addr
          "name", JStr e.Name
          "kind", JStr e.Kind
          "required", JBool e.Required ]
        @ (match e.Space with
           | Some _ when Function.derivedSlotSpace e -> []
           | Some s -> [ "space", SpaceCodec.toJson s ]
           | None -> [])
        @ (match e.Slot with
           | Some k -> [ "slotKind", JStr k ]
           | None -> [])
        @ (match e.Action with
           | Some eff -> [ "actionEffect", EffectCodec.toJson eff ]
           | None -> [])
        |> JObj

    let private entryOf (el: JVal) : Result<SigEntry, DecodeError> =
        let str name = Decoder.field name Decoder.str el

        str "addr"
        |> Result.bind (fun addr ->
            str "name"
            |> Result.bind (fun name ->
                // Phase 295: the tag is one of `HoleKind.tags`; any other is refused.
                Decoder.field "kind" (tagged "unknown hole kind: " (HoleKind.tags |> List.map (fun t -> t, t))) el
                |> Result.bind (fun kind ->
                    Decoder.field "required" Decoder.bool el
                    |> Result.bind (fun required ->
                        Decoder.optField "space" SpaceCodec.decoder el
                        |> Result.bind (fun sp ->
                            Decoder.optField "actionEffect" EffectCodec.decoder el
                            |> Result.bind (fun ac ->
                                Decoder.optField "slotKind" Decoder.str el
                                |> Result.map (fun slot ->
                                    // A slot entry travels without its derived space (Phase 229), so
                                    // decoding restores it from the constraint.
                                    let sp =
                                        match sp with
                                        | None when kind = HoleKind.tag (SlotHole slot) -> Some(SlotTree slot)
                                        | other -> other

                                    { Addr = addr
                                      Name = name
                                      Kind = kind
                                      Space = sp
                                      Slot = slot
                                      Action = ac
                                      Required = required })))))))

    let internal signatureJson (sg: Signature) : JVal =
        JObj
            [ "name", JStr sg.Name
              "effect", EffectCodec.toJson sg.Effect
              "holes", JArr(sg.Holes |> List.map entryJson) ]

    let private signatureOfDetailed (el: JVal) : Result<Signature, DecodeError> =
        Decoder.field "name" Decoder.str el
        |> Result.bind (fun name ->
            Decoder.field "effect" EffectCodec.decoder el
            |> Result.bind (fun eff ->
                Decoder.field "holes" (Decoder.list entryOf) el
                |> Result.map (fun holes ->
                    { Name = name
                      Holes = holes
                      Effect = eff })))

    /// Read a signature object (`name`, `effect`, `holes`) leniently, refusing a hole-kind tag
    /// outside `HoleKind.tags`; a slot entry written without its space gets `SlotTree` of its
    /// constraint back.
    let signatureOf (el: JVal) : Result<Signature, string> =
        Decoder.describing signatureOfDetailed el

    // ---- placement ----

    let private islandTag =
        function
        | Pyodide -> "pyodide"
        | Fable -> "fable"
        | Js -> "js"

    let private islandOf: Decoder<IslandKind> =
        tagged "unknown island kind: " [ "pyodide", Pyodide; "fable", Fable; "js", Js ]

    let private placementJson (p: Placement) : JVal =
        match p with
        | BuildTime -> Canon.typed "buildTime" []
        | Server -> Canon.typed "server" []
        | ClientDeclarative -> Canon.typed "clientDeclarative" []
        | Precomputed -> Canon.typed "precomputed" []
        | ClientIsland k -> Canon.typed "clientIsland" [ "island", JStr(islandTag k) ]

    let private placementOf: Decoder<Placement> =
        dispatch
            "unknown placement: "
            [ "buildTime", Decoder.succeed BuildTime
              "server", Decoder.succeed Server
              "clientDeclarative", Decoder.succeed ClientDeclarative
              "precomputed", Decoder.succeed Precomputed
              "clientIsland", Decoder.field "island" islandOf |> Decoder.map ClientIsland ]

    // ---- capability declaration ----

    /// Encode a `Capability` declaration to a `JVal` (`"$type":"capability"`).
    let encodeJson (c: Capability) : JVal =
        Canon.typed
            "capability"
            [ "id", JStr c.Id
              "signature", signatureJson c.Signature
              "determinism", JStr(Effect.determinismTag c.Determinism)
              "placement", placementJson c.Placement ]

    /// `encodeJson` rendered canonically: one capability always renders to the same string.
    let encode (c: Capability) : string = Canon.render (encodeJson c)

    let private decodeJsonDetailed (el: JVal) : Result<Capability, DecodeError> =
        Decoder.field "id" Decoder.str el
        |> Result.bind (fun id ->
            Decoder.field "signature" signatureOfDetailed el
            |> Result.bind (fun sg ->
                // Cross-check the wire `determinism` tag against the signature's effect determinism
                // (Phase 44). The tag is written on encode but was previously ignored on decode, so a
                // payload whose tag disagrees with its signature — tampered, or from a divergent host —
                // decoded silently to the signature-derived value, mis-keying the Phase 27 replay seam.
                let expectedTag = Effect.determinismTag sg.Effect.Determinism

                Decoder.field
                    "determinism"
                    (Decoder.str
                     |> Decoder.andThen (fun wireTag ->
                         if wireTag <> expectedTag then
                             Error(
                                 DecodeError.make
                                     DecodeCode.OutOfRange
                                     ("'" + expectedTag + "', the signature's determinism")
                                     ("capability determinism disagrees with signature effect: wire '"
                                      + wireTag
                                      + "' vs signature '"
                                      + expectedTag
                                      + "'")
                             )
                         else
                             Ok()))
                    el
                |> Result.bind (fun () ->
                    Decoder.field "placement" placementOf el
                    |> Result.map (fun placement ->
                        { Id = id
                          Signature = sg
                          Placement = placement }))))

    /// Read a capability declaration leniently. The wire `determinism` must equal the label the
    /// decoded signature's effect derives; a disagreeing tag is refused, never silently corrected.
    let decodeJson (el: JVal) : Result<Capability, string> =
        Decoder.describing decodeJsonDetailed el

    /// Parse, then `decodeJson`; a malformed document is refused with the parser's sentence.
    let decode (s: string) : Result<Capability, string> =
        Decode.parse s |> Result.bind decodeJson

    // ---- typed invocation record ----

    /// Encode a typed invocation `(capabilityId, args)` to a `JVal` (`"$type":"invocation"`).
    let internal encodeInvocationJson (capabilityId: string) (args: (string * string) list) : JVal =
        Canon.typed
            "invocation"
            [ "capabilityId", JStr capabilityId
              "args", JArr(args |> List.map (fun (a, v) -> JObj [ "addr", JStr a; "value", JStr v ])) ]

    /// A typed invocation as a canonical `"$type":"invocation"` document. The arguments keep the
    /// order given, each an `{addr, value}` object; nothing is validated against a signature here.
    let encodeInvocation (capabilityId: string) (args: (string * string) list) : string =
        Canon.render (encodeInvocationJson capabilityId args)

    let private invocationOf: Decoder<string * (string * string) list> =
        let arg =
            both (Decoder.field "addr" Decoder.str) (Decoder.field "value" Decoder.str) (fun addr v -> addr, v)

        both (Decoder.field "capabilityId" Decoder.str) (Decoder.field "args" (Decoder.list arg)) (fun cid args ->
            cid, args)

    /// Read an invocation back as `(capabilityId, args)` in wire order, leniently; argument values are
    /// read as strings and checked against nothing until `validateArgs`.
    let decodeInvocation (s: string) : Result<string * (string * string) list, string> =
        Decode.parse s |> Result.bind (Decoder.describing invocationOf)

    // ---- Deferred<'T> async-result envelope (Phase 32) ----

    /// Encode a `Deferred<'T>` to a `JVal` (`"$type"`-tagged `pending` / `ready` / `failed`), using
    /// `encodeT` for a `Ready` payload. The async-invocation result wire shape every host shares.
    let deferredJson (encodeT: 'T -> JVal) (d: Deferred<'T>) : JVal =
        match d with
        | Pending -> Canon.typed "pending" []
        | Ready v -> Canon.typed "ready" [ "value", encodeT v ]
        | Failed m -> Canon.typed "failed" [ "message", JStr m ]

    /// `deferredJson` rendered canonically.
    let encodeDeferred (encodeT: 'T -> JVal) (d: Deferred<'T>) : string = Canon.render (deferredJson encodeT d)

    /// Decode a `Deferred<'T>` from a `JVal`, using `decodeT` for a `ready` payload — `Result`-typed with
    /// a named error (the codec envelope discipline).
    let deferredOf (decodeT: JVal -> Result<'T, string>) (el: JVal) : Result<Deferred<'T>, string> =
        // The caller's payload reader answers a sentence and no code, so its refusal is carried
        // through this envelope's string form unchanged.
        let payload: Decoder<'T> =
            fun v ->
                decodeT v
                |> Result.mapError (fun m -> DecodeError.make DecodeCode.OutOfRange "a payload its reader accepts" m)

        Decoder.describing
            (dispatch
                "unknown deferred kind: "
                [ "pending", Decoder.succeed Pending
                  "ready", Decoder.field "value" payload |> Decoder.map Ready
                  "failed", Decoder.field "message" Decoder.str |> Decoder.map Failed ])
            el

    /// Parse, then `deferredOf`; a refusal from `decodeT` is passed through as its own sentence.
    let decodeDeferred (decodeT: JVal -> Result<'T, string>) (s: string) : Result<Deferred<'T>, string> =
        Decode.parse s |> Result.bind (deferredOf decodeT)

    // ---- typed refusal (Phase 251) ----
    // The refusal crosses the wire like every other seam type, under the same `$type` envelope
    // (one case, one tag; the case name in camelCase). A value space travels in its codec form, so
    // the document decodes back to the same `InvokeError`. The sentence a model reads is
    // `InvokeError.describe`; this is the structured form beside it.

    let private strs (xs: string list) : JVal = JArr(xs |> List.map JStr)

    /// Encode an `InvokeError` to a `JVal` (`"$type"` is the case: `noSuchCapability`, `argOutOfSpace`, …).
    let invokeErrorJson (e: InvokeError) : JVal =
        match e with
        | NoSuchCapability(id, known) -> Canon.typed "noSuchCapability" [ "id", JStr id; "known", strs known ]
        | DuplicateCapability id -> Canon.typed "duplicateCapability" [ "id", JStr id ]
        | UnknownArg(addr, declared) -> Canon.typed "unknownArg" [ "addr", JStr addr; "declared", strs declared ]
        | ArgOutOfSpace(addr, space, got) ->
            Canon.typed "argOutOfSpace" [ "addr", JStr addr; "space", SpaceCodec.toJson space; "got", JStr got ]
        | RequiredArgsUnbound addrs -> Canon.typed "requiredArgsUnbound" [ "addrs", strs addrs ]
        | UninvocableArg addr -> Canon.typed "uninvocableArg" [ "addr", JStr addr ]
        | BodyFailed reason -> Canon.typed "bodyFailed" [ "reason", JStr reason ]
        | NonTotalCapability(id, addrs) -> Canon.typed "nonTotalCapability" [ "id", JStr id; "addrs", strs addrs ]

    /// `invokeErrorJson` rendered canonically; `decodeInvokeError` reads it back to the same value.
    let encodeInvokeError (e: InvokeError) : string = Canon.render (invokeErrorJson e)

    /// Decode an `InvokeError` from a `JVal` — `Result`-typed with a named error.
    let invokeErrorOf (el: JVal) : Result<InvokeError, string> =
        let str name = Decoder.field name Decoder.str

        let strList name =
            Decoder.field name (Decoder.list Decoder.str)

        let invokeError =
            dispatch
                "unknown invoke error: "
                [ "noSuchCapability", both (str "id") (strList "known") (fun id known -> NoSuchCapability(id, known))
                  "duplicateCapability", str "id" |> Decoder.map DuplicateCapability
                  "unknownArg", both (str "addr") (strList "declared") (fun addr d -> UnknownArg(addr, d))
                  "argOutOfSpace",
                  str "addr"
                  |> Decoder.bind (fun addr ->
                      both (Decoder.field "space" SpaceCodec.decoder) (str "got") (fun sp got ->
                          ArgOutOfSpace(addr, sp, got)))
                  "requiredArgsUnbound", strList "addrs" |> Decoder.map RequiredArgsUnbound
                  "uninvocableArg", str "addr" |> Decoder.map UninvocableArg
                  "bodyFailed", str "reason" |> Decoder.map BodyFailed
                  "nonTotalCapability",
                  both (str "id") (strList "addrs") (fun id addrs -> NonTotalCapability(id, addrs)) ]

        Decoder.describing invokeError el

    /// Parse, then `invokeErrorOf`; an unknown `$type` is refused as `unknown invoke error: <tag>`.
    let decodeInvokeError (s: string) : Result<InvokeError, string> =
        Decode.parse s |> Result.bind invokeErrorOf

    // ---- strict read policy (Phase 251) ----
    // `Strict` is a members check over the document BEFORE the ordinary decoder runs: every object
    // this codec reads may carry only the members this codec reads from it. The decoders above are
    // untouched, so `Lenient` is byte-for-byte the old behaviour. A shape fault (a string where an
    // object belongs) is left to the decoder to name; the check only ever adds the unknown-member
    // refusal. A `ready` envelope's payload is the caller's `decodeT`'s to read, so its members are
    // the caller's to police.

    // Phase 310 — the members check IS the decode layer's strict policy (`Decoder.members`, its
    // generalisation); a refusal keeps this codec's sentence, naming the object it is in, and its
    // path names the member.

    let private tagOf (el: JVal) : string option =
        match Decoder.tryMember "$type" el with
        | Some(JStr t) -> Some t
        | _ -> None

    /// The first member of `el` outside `known`, as a refusal naming it and the members read.
    let private members (where: string) (known: string list) : Decoder<unit> =
        fun el ->
            Decoder.members known el
            |> Result.mapError (fun e ->
                match List.tryLast e.Path with
                | Some(PathSegment.Key k) ->
                    { e with
                        Message =
                            "unknown member '"
                            + k
                            + "' in "
                            + where
                            + "; its members are "
                            + Space.quoteAll (List.sort known) }
                | _ -> e)

    /// Check the member `name` of `el`, where it is present.
    let private within (name: string) (check: Decoder<unit>) : Decoder<unit> =
        fun el ->
            match el with
            | JObj _ -> Decoder.optField name check el |> Result.map ignore
            | _ -> Ok()

    /// Check every element of an array.
    let private each (check: Decoder<unit>) : Decoder<unit> =
        fun el ->
            match el with
            | JArr _ -> Decoder.list check el |> Result.map ignore
            | _ -> Ok()

    let private strictSpace (el: JVal) =
        let extra =
            match tagOf el with
            | Some "intRange"
            | Some "floatRange"
            | Some "stringLen" -> [ "min"; "max" ]
            | Some "enum" -> [ "values" ]
            | Some "slotTree" -> [ "slotKind" ]
            | _ -> []

        members "value space" ("$type" :: extra) el

    let private strictEffect (el: JVal) = members "effect" EffectCodec.members el

    let private strictEntry (el: JVal) =
        members "signature hole" [ "addr"; "name"; "kind"; "required"; "space"; "slotKind"; "actionEffect" ] el
        |> Result.bind (fun () -> within "space" strictSpace el)
        |> Result.bind (fun () -> within "actionEffect" strictEffect el)

    let private strictSignature (el: JVal) =
        members "signature" [ "name"; "effect"; "holes" ] el
        |> Result.bind (fun () -> within "effect" strictEffect el)
        |> Result.bind (fun () -> within "holes" (each strictEntry) el)

    let private strictPlacement (el: JVal) =
        let extra =
            match tagOf el with
            | Some "clientIsland" -> [ "island" ]
            | _ -> []

        members "placement" ("$type" :: extra) el

    let private strictCapability (el: JVal) =
        members "capability" [ "$type"; "id"; "signature"; "determinism"; "placement" ] el
        |> Result.bind (fun () -> within "signature" strictSignature el)
        |> Result.bind (fun () -> within "placement" strictPlacement el)

    let private strictInvocation (el: JVal) =
        members "invocation" [ "$type"; "capabilityId"; "args" ] el
        |> Result.bind (fun () -> within "args" (each (members "invocation argument" [ "addr"; "value" ])) el)

    let private strictDeferred (el: JVal) =
        let extra =
            match tagOf el with
            | Some "ready" -> [ "value" ]
            | Some "failed" -> [ "message" ]
            | _ -> []

        members "deferred" ("$type" :: extra) el

    let private under (policy: ReadPolicy) (check: Decoder<unit>) (d: Decoder<'T>) : Decoder<'T> =
        match policy with
        | ReadPolicy.Lenient -> d
        | ReadPolicy.Strict -> check |> Decoder.bind (fun () -> d)

    /// `decodeJsonWith` answering a typed refusal (Phase 310): its code, the path to the value at
    /// fault (an undeclared member under `Strict` is `UndeclaredMember` at that member), what the
    /// position expected, and `decodeJsonWith`'s sentence.
    let decodeJsonDetailedWith (policy: ReadPolicy) (el: JVal) : Result<Capability, DecodeError> =
        under policy strictCapability decodeJsonDetailed el

    /// `decodeWith` answering a typed refusal (Phase 310); a parse failure is refused at the root.
    let decodeDetailedWith (policy: ReadPolicy) (s: string) : Result<Capability, DecodeError> =
        Decoder.parse s |> Result.bind (decodeJsonDetailedWith policy)

    /// `decodeJson` under a read policy: `Strict` refuses an unknown member anywhere in the
    /// declaration (its signature, holes, value spaces, effects and placement included).
    let decodeJsonWith (policy: ReadPolicy) (el: JVal) : Result<Capability, string> =
        decodeJsonDetailedWith policy el |> Result.mapError DecodeError.describe

    /// `decode` under a read policy.
    let decodeWith (policy: ReadPolicy) (s: string) : Result<Capability, string> =
        Decode.parse s |> Result.bind (decodeJsonWith policy)

    /// `decodeInvocationWith` answering a typed refusal (Phase 310); a parse failure is refused at
    /// the root.
    let decodeInvocationDetailedWith
        (policy: ReadPolicy)
        (s: string)
        : Result<string * (string * string) list, DecodeError> =
        Decoder.parse s |> Result.bind (under policy strictInvocation invocationOf)

    /// `decodeInvocation` under a read policy: `Strict` refuses an unknown member of the
    /// invocation or of any of its arguments — an `"actor"` beside the `capabilityId`, say, which
    /// `Lenient` reads past.
    let decodeInvocationWith (policy: ReadPolicy) (s: string) : Result<string * (string * string) list, string> =
        Decode.parse s
        |> Result.bind (fun el ->
            under policy strictInvocation invocationOf el
            |> Result.mapError DecodeError.describe)

    /// `deferredOf` under a read policy: `Strict` refuses an unknown member of the envelope; the
    /// `ready` payload is `decodeT`'s to read.
    let deferredOfWith
        (policy: ReadPolicy)
        (decodeT: JVal -> Result<'T, string>)
        (el: JVal)
        : Result<Deferred<'T>, string> =
        match policy with
        | ReadPolicy.Lenient -> Ok()
        | ReadPolicy.Strict -> strictDeferred el |> Result.mapError DecodeError.describe
        |> Result.bind (fun () -> deferredOf decodeT el)

    /// `decodeDeferred` under a read policy.
    let decodeDeferredWith
        (policy: ReadPolicy)
        (decodeT: JVal -> Result<'T, string>)
        (s: string)
        : Result<Deferred<'T>, string> =
        Decode.parse s |> Result.bind (deferredOfWith policy decodeT)
