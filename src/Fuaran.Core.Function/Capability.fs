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
    /// A registration refused because the capability's signature is not a well-formed declaration
    /// (Phase 307): `Signature.validate`'s first fault — an empty or non-finite space, or two holes
    /// at one address.
    | IllFormedCapability of id: string * fault: DeclarationFault
    /// An invocation binds this address twice (Phase 307). Refused before anything else is read of
    /// the second binding: one address takes one value, so which one was meant is not a question
    /// the seam answers, and a capture key over the list would depend on its order.
    | DuplicateArg of addr: string
    /// The registry's policy DENIED the invocation (Phase 318): the gate named `policy` refused it,
    /// with `reason` and the `allowed` alternatives its guidance enumerates. Raised after the
    /// arguments validated and before the body: no body ran, and nothing was captured.
    | PolicyRefused of policy: string * reason: string * allowed: string list
    /// The registry's policy answered `NeedsApproval` for the invocation (Phase 318): the gate named
    /// `policy` will not let it run until an approval is given. Raised where `PolicyRefused` is, and
    /// with the same guarantee — no body ran.
    | ApprovalRequired of policy: string

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
            + SeamCodec.quoteAll known
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
            + SeamCodec.quoteAll declared
            + "."
        | ArgOutOfSpace(addr, space, got) ->
            "Refused: argument '"
            + addr
            + "' must be "
            + Space.describe space
            + "; you sent '"
            + got
            + "'."
        | RequiredArgsUnbound addrs -> "Refused: required arguments missing: " + SeamCodec.quoteAll addrs + "."
        | UninvocableArg addr ->
            "Refused: argument '"
            + addr
            + "' cannot take the value sent. A tree argument takes a JSON object with a \"kind\"; an action is bound by the host and never by a caller."
        | BodyFailed reason -> "Refused: the tool ran and failed: " + reason + "."
        | NonTotalCapability(id, addrs) ->
            "Refused: the tool '"
            + id
            + "' cannot be registered, because it is not total: "
            + SeamCodec.quoteAll addrs
            + " must each be a repeat over a bounded count, or a declared hole."
        | IllFormedCapability(id, fault) ->
            "Refused: the tool '"
            + id
            + "' cannot be registered, because its declaration is ill-formed: "
            + DeclarationFault.describe fault
            + "."
        | DuplicateArg addr ->
            "Refused: argument '"
            + addr
            + "' is given more than once. Give each argument once."
        | PolicyRefused(policy, reason, []) ->
            "Refused: the policy '" + policy + "' does not allow this: " + reason + "."
        | PolicyRefused(policy, reason, allowed) ->
            "Refused: the policy '"
            + policy
            + "' does not allow this: "
            + reason
            + ". What it allows instead: "
            + SeamCodec.quoteAll allowed
            + "."
        | ApprovalRequired policy ->
            "Refused: the policy '"
            + policy
            + "' requires an approval before this tool runs."

    /// The refusal a registry's policy makes, in this seam's error (Phase 318): a `Deny`'s guidance
    /// becomes `PolicyRefused` naming the gate, its message and its alternatives.
    /// A body's answer in the seam's outcome (Phase 388: the one projection every invoking path —
    /// `Capability.invoke`, `invokeTyped`, the registry's dispatch and `FunctionRegistry.dispatch` —
    /// settles through): `Ready` and `Pending` ride out unchanged, and `Failed m` is the typed
    /// `BodyFailed m`, so `Ok(Failed _)` is unreachable.
    let internal settle (d: Deferred<'v>) : Result<Deferred<'v>, InvokeError> =
        match d with
        | Ready v -> Ok(Ready v)
        | Pending -> Ok Pending
        | Failed m -> Error(BodyFailed m)

    let internal policyRefused (policy: string) (g: RejectionGuidance) : InvokeError =
        PolicyRefused(policy, g.Message, g.Alternatives)

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

    /// The entries that make a signature non-total (Phase 295): a repeat over an unbounded count
    /// space, or an entry that projects to no hole kind. Empty exactly when `Function.isTotal`.
    let internal nonTotalAddrs (sg: Signature) : string list =
        sg.Holes
        |> List.filter (fun e -> not (Function.isTotalEntry e))
        |> List.map _.Addr

    /// THE admission gate (Phase 307; one function since Phase 385, D111): totality first
    /// (`NonTotalCapability`, naming the non-total entries), then well-formedness
    /// (`Signature.validate`, refused `IllFormedCapability`), for a declaration under `id`. `None`
    /// admits. Both registries run it through `admissionFault`, and `CapabilityCodec`'s capability
    /// reader runs it over the signature it has just read, so a reader never admits what a registry
    /// refuses and refuses it with the registry's own error.
    let internal admissionOf (id: string) (sg: Signature) : InvokeError option =
        match nonTotalAddrs sg with
        | _ :: _ as addrs -> Some(NonTotalCapability(id, addrs))
        | [] ->
            match Signature.validate sg with
            | Ok() -> None
            | Error fault -> Some(IllFormedCapability(id, fault))

    /// `admissionOf` over a built capability — what both registries run at `register` / `replace`.
    let internal admissionFault (c: Capability) : InvokeError option = admissionOf c.Id c.Signature

    /// The space an argument for this entry is checked against: its own, or — for a slot entry built
    /// by hand before Phase 229, which carries none — the `SlotTree` of its constraint, so the
    /// spaceless slot is invocable exactly as a derived one is (Phase 307). An action entry has none.
    let private argSpace (h: SigEntry) : ValueSpace option = h.Space

    /// Every address an argument list binds more than once, at each repeat, in list order (Phase
    /// 307) — empty exactly when the addresses are distinct. The one duplicate check both seams and
    /// the pipeline run (`DuplicateArg`, `DuplicateParam`).
    let repeatedAddrs (args: (string * 'v) list) : string list =
        args
        |> List.fold
            (fun (seen: Set<string>, acc) (addr, _) ->
                if seen.Contains addr then
                    seen, addr :: acc
                else
                    seen.Add addr, acc)
            (Set.empty, [])
        |> snd
        |> List.rev

    /// The Phase 27 determinism label this capability keys its captures on: `Effect.determinismTag` of its
    /// determinism set — `"deterministic"` for the empty set, else its factors joined by `+` in canonical
    /// order (`"clock"`, `"clock+random"`, …; Phase 319).
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
            match c.Signature.Holes |> List.tryFind (fun h -> h.Addr = a) |> Option.bind _.Space with
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
    /// dispatch: every arg must address a declared data hole, once (`DuplicateArg` otherwise, Phase
    /// 307 — so the argument list `invocationKey` keys is one with distinct addresses, the hypothesis
    /// its determinism rests on), and lie in its space; every required hole must be bound. A slot hole is invocable since Phase 229: its space is `SlotTree`, so its
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
        let declared = holes |> List.map _.Addr
        let argMap = Map.ofList args

        // 0. every address is bound once (Phase 307): the first repeated address, at its second
        //    occurrence, is `DuplicateArg`, before any value is read.
        // 1. every arg addresses a declared hole, and that hole takes a scalar value in-space.
        let rec checkArgs =
            function
            | [] -> Ok()
            | (addr, value) :: rest ->
                match holes |> List.tryFind (fun h -> h.Addr = addr) with
                | None -> Error(UnknownArg(addr, declared))
                | Some h ->
                    match argSpace h with
                    | None -> Error(UninvocableArg addr) // a spaceless (action) hole
                    | Some(SlotTree _) when (Space.slotKindOf value).IsNone -> Error(UninvocableArg addr) // no tree
                    | Some space ->
                        if Space.validate space value then
                            checkArgs rest
                        else
                            Error(ArgOutOfSpace(addr, space, value))

        match repeatedAddrs args with
        | dup :: _ -> Error(DuplicateArg dup)
        | [] -> checkArgs args
        |> Result.bind (fun () ->
            // 2. every required hole is bound.
            let unbound =
                holes
                |> List.filter (fun h -> h.Required && not (Map.containsKey h.Addr argMap))
                |> List.map _.Addr

            if List.isEmpty unbound then
                Ok()
            else
                Error(RequiredArgsUnbound unbound))

    /// Invoke a capability: validate the args, then run the host `body`. Deterministic capabilities
    /// re-evaluate freely; for a non-`Deterministic` capability the caller journals the realized
    /// value via `OpStream.captureEffect` keyed by `invocationKey` + `determinismTag` (Phase 27),
    /// so the invocation replays exactly. A body failure is a named `BodyFailed`, never a throw.
    ///
    /// **The body must be TOTAL (Phase 307).** The seam does not wrap it: a body that throws
    /// propagates the exception to the caller, unconverted, because catching every exception would
    /// also catch the ones a host means to escape (cancellation, its own fatal faults). A body
    /// reports failure by answering `Failed reason`, which is `BodyFailed reason` here. The proved
    /// model's body is `Tot`, and this obligation is the assumption that ties the two
    /// (`proofs.json`, `capability-body-total`).
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
        validateArgs c args |> Result.bind (fun () -> InvokeError.settle (body ()))

    // ---- every refusal at once, and the validated arguments handed on (Phase 251) ----

    /// The refusal one argument earns on its own, by the rules `validateArgs` applies to it.
    let internal argFault (c: Capability) (declared: string list) (addr: string, value: string) : InvokeError option =
        match c.Signature.Holes |> List.tryFind (fun h -> h.Addr = addr) with
        | None -> Some(UnknownArg(addr, declared))
        | Some h ->
            match argSpace h with
            | None -> Some(UninvocableArg addr)
            | Some(SlotTree _) when (Space.slotKindOf value).IsNone -> Some(UninvocableArg addr)
            | Some space ->
                if Space.validate space value then
                    None
                else
                    Some(ArgOutOfSpace(addr, space, value))

    /// Validate as `validateArgs` does, but answer with EVERY refusal rather than the first: a
    /// `DuplicateArg` per repeated address (Phase 307), then one per refused argument, in argument
    /// order, then `RequiredArgsUnbound` naming every required
    /// hole left out. So a call with two bad arguments is refused naming both, and a model needs one
    /// round trip, not two. The first-failure form is kept, and the two agree by construction of
    /// their order: the head of this list is exactly `validateArgs`'s refusal, and `Ok ()` here is
    /// `Ok ()` there.
    let validateArgsAll (c: Capability) (args: (string * string) list) : Result<unit, InvokeError list> =
        let holes = c.Signature.Holes
        let declared = holes |> List.map _.Addr
        let argMap = Map.ofList args

        // Every repeated address first (Phase 307), as `validateArgs` checks it first; then one
        // refusal per argument, in order.
        let faults =
            (repeatedAddrs args |> List.map DuplicateArg)
            @ (args |> List.choose (argFault c declared))

        let unbound =
            holes
            |> List.filter (fun h -> h.Required && not (Map.containsKey h.Addr argMap))
            |> List.map _.Addr

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
                        |> List.tryFind (fun h -> h.Addr = addr)
                        |> Option.bind argSpace

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
        typeArgs c args |> Result.bind (fun typed -> InvokeError.settle (body typed))

/// A dispatch journalled in the keyed capture journal (Phase 391; a positional triple before
/// `1.0.0`): what `CapabilityRegistry.dispatchCaptured` and the `QueryRegistry.dispatch…Captured`
/// verbs answer. `'e` is the seam's refusal — `InvokeError` for a capability, `QueryError` for a
/// query.
type CapturedDispatch<'v, 'e> =
    {
        /// Exactly what the un-journalled dispatch answers.
        Outcome: Result<Deferred<'v>, 'e>
        /// The invocation key and occurrence the attempt was journalled under — the ticket
        /// `OpStream.settleEffectKeyed` settles a `Pending` answer with later. `None` when nothing
        /// was journalled: a refusal before the body, or a deterministic declaration.
        Ticket: (string * int) option
        /// The journal, with the attempt and its settlement appended when one was journalled.
        Journal: KeyedCapture list
    }

/// Why a replayed dispatch did not answer a value (Phase 391; a nested `Result` before `1.0.0`): the
/// seam's own refusal, which a live dispatch would have answered too, or the journal's inability
/// to answer the invocation.
[<RequireQualifiedAccess>]
type ReplayFailure<'e> =
    /// The seam refused, exactly as the live dispatch refuses — an id it does not hold, arguments
    /// that do not validate, a policy refusal — or the journal recorded the invocation's refusal,
    /// which replays as the refusal it was answered live.
    | Refused of 'e
    /// The journal cannot answer the invocation — `NoCapture`, `Exhausted`, a label mismatch, an
    /// undecodable value. Never answered by a live call.
    | Unanswered of KeyedCaptureFault

/// A dispatch replayed from the keyed capture journal (Phase 391; a nested `Result` and a pair
/// before `1.0.0`): what `CapabilityRegistry.dispatchReplayed` and the
/// `QueryRegistry.dispatchReplayed…` verbs answer.
type ReplayedDispatch<'v, 'e> =
    {
        /// The replayed answer: a value as `Ready`, an attempt that never settled as `Pending`, or
        /// the failure.
        Outcome: Result<Deferred<'v>, ReplayFailure<'e>>
        /// The replay cursor after this invocation — advanced past every journal record the replay
        /// consumed (a recorded refusal included), unchanged when the seam refused before the
        /// journal or the journal could not answer. Thread it into the next replay.
        Cursor: Map<string, int>
    }

/// A typed capability registry — the discovery surface an agent enumerates (the compute analogue of
/// node-introspection): "what compute may I invoke, with what typed args". Default-deny by shape on
/// dispatch — only a registered id resolves.
///
/// OPAQUE since `1.0.0` (Phase 386), with `FunctionRegistry`'s shape (Phase 316): the map and the
/// policy are reachable only through this module's verbs, so `register` — and its admission gate —
/// is the only way a capability gets in. While the record was public, `{ r with Capabilities =
/// Map.add id c r.Capabilities }` admitted a capability no gate had seen, which is the default-deny
/// promise broken by construction.
type CapabilityRegistry =
    private
        {
            /// Keyed by `Capability.Id`; every member was admitted by `register`, so each is total.
            Capabilities: Map<string, Capability>
            /// The gates every dispatch runs after validation and before the body, and the observers a
            /// refusal reaches (Phase 318). `RegistryPolicy.none` — no gate — in `empty`; only
            /// `withGate` and `onDenied` add to it, and every lifecycle verb carries it through.
            Policy: RegistryPolicy<Capability, (string * string) list>
        }

/// Populate / enumerate / dispatch the capability registry (named `Registry` until Phase 295; the
/// obsolete alias left at `1.0.0`). `ModuleSuffix`, so the module and the type share a name, as
/// `FunctionRegistry` does.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module CapabilityRegistry =

    /// The registry with no capabilities: every dispatch through it is `NoSuchCapability` with an
    /// empty `known` list.
    let empty: CapabilityRegistry =
        { Capabilities = Map.empty
          Policy = RegistryPolicy.none }

    /// Register a capability — additive, no silent overwrite (a duplicate id is a named error), and
    /// only a TOTAL one (Phase 295): a capability whose signature carries a repeat over no count
    /// space is refused `NonTotalCapability`, naming the holes, rather than dispatched. And only a
    /// WELL-FORMED one (Phase 307): a signature `Signature.validate` refuses — an empty or
    /// non-finite space, two holes at one address — is `IllFormedCapability`.
    let register (c: Capability) (r: CapabilityRegistry) : Result<CapabilityRegistry, InvokeError> =
        KeyedRegistry.register DuplicateCapability Capability.admissionFault c.Id c r.Capabilities
        |> Result.map (fun m -> { r with Capabilities = m })

    // ---- the policy gate (Phase 318) ----

    /// `r` with `gate` added to the gates every dispatch runs (`RegistryPolicy.withGate`). A gate runs
    /// after the arguments validate and before the body, and the decision is the join over every
    /// gate — so adding one can only refuse more: an invocation `r` refused, the result refuses.
    let withGate (gate: PolicyGate<Capability, (string * string) list>) (r: CapabilityRegistry) : CapabilityRegistry =
        { r with
            Policy = RegistryPolicy.withGate gate r.Policy }

    /// `r` with `observe` told of every invocation its policy refuses — a `Deny` or a
    /// `NeedsApproval` — before the refusal is returned. A refusal for any other reason (an unknown
    /// id, an argument out of space) is not a policy refusal and reaches no observer.
    let onDenied (observe: PolicyDenial<(string * string) list> -> unit) (r: CapabilityRegistry) : CapabilityRegistry =
        { r with
            Policy = RegistryPolicy.onDenied observe r.Policy }

    /// What the registry would decide for an invocation, without dispatching it (Phase 318) — the
    /// meeting point of default-deny by shape and the policy gate: an id the
    /// registry does not hold is a `Deny` whose guidance is `NoSuchCapability`'s sentence with the
    /// registered ids as its alternatives; an invocation whose arguments do not validate is a
    /// `Deny` with the validation refusal's sentence; otherwise the gates' join. No observer is told.
    let decide (r: CapabilityRegistry) (id: string) (args: (string * string) list) : PolicyDecision =
        match Map.tryFind id r.Capabilities with
        | None ->
            let known = r.Capabilities |> Map.toList |> List.map fst
            PolicyDecision.denyWith (InvokeError.describe (NoSuchCapability(id, known))) known
        | Some c ->
            match Capability.validateArgs c args with
            | Error e -> PolicyDecision.deny (InvokeError.describe e)
            | Ok() -> RegistryPolicy.decide r.Policy c args

    /// The policy's admission of one validated invocation: `Ok ()`, or the typed refusal after the
    /// observers were told.
    let private admit
        (r: CapabilityRegistry)
        (c: Capability)
        (args: (string * string) list)
        : Result<unit, InvokeError> =
        RegistryPolicy.admit InvokeError.policyRefused ApprovalRequired r.Policy c.Id c args

    /// The capability registered under exactly `id` (ordinal, case-sensitive), or `None`.
    let tryFind (id: string) (r: CapabilityRegistry) : Capability option = Map.tryFind id r.Capabilities

    /// Enumerate the registry in a stable order (by id) — the discovery surface; stability is part
    /// of the contract (`capabilityLaws` certifies it).
    let enumerate (r: CapabilityRegistry) : Capability list =
        r.Capabilities |> Map.toList |> List.map snd

    /// Dispatch an invocation through the registry: resolve the id (default-deny — an unregistered
    /// id is `NoSuchCapability`), validate the arguments, run the policy's gates (Phase 318 — a
    /// `Deny` is `PolicyRefused`, a `NeedsApproval` is `ApprovalRequired`, and either reaches every
    /// `onDenied` observer), and only then the body. With no gate this is `Capability.invoke`
    /// exactly. The host body is supplied by the caller per the resolved capability's placement, and
    /// answers in the `Deferred` envelope (Phase 210 — the same three outcomes `Capability.invoke`
    /// documents).
    let dispatch
        (r: CapabilityRegistry)
        (id: string)
        (args: (string * string) list)
        (body: Capability -> unit -> Deferred<'v>)
        : Result<Deferred<'v>, InvokeError> =
        match Map.tryFind id r.Capabilities with
        | None -> Error(NoSuchCapability(id, r.Capabilities |> Map.toList |> List.map fst))
        | Some c ->
            Capability.validateArgs c args
            |> Result.bind (fun () -> admit r c args)
            |> Result.bind (fun () -> InvokeError.settle (body c ()))

    /// `dispatch`, with the body handed the resolved capability and the validated arguments, typed
    /// (`Capability.invokeWithArgs`, Phase 251). Additive beside `dispatch`; default-deny and gated
    /// the same (Phase 318: the gate runs after the arguments type, before the body).
    let dispatchWithArgs
        (r: CapabilityRegistry)
        (id: string)
        (args: (string * string) list)
        (body: Capability -> (string * ArgValue) list -> Deferred<'v>)
        : Result<Deferred<'v>, InvokeError> =
        match Map.tryFind id r.Capabilities with
        | None -> Error(NoSuchCapability(id, r.Capabilities |> Map.toList |> List.map fst))
        | Some c ->
            Capability.typeArgs c args
            |> Result.bind (fun typed -> admit r c args |> Result.map (fun () -> typed))
            |> Result.bind (fun typed -> InvokeError.settle (body c typed))

    // ---- invocation-keyed capture (Phase 318) ----

    /// `dispatch`, journalling the invocation in the KEYED capture journal (Phase 318) under
    /// `Capability.invocationKey` and the capability's `determinismTag`: the attempt is journalled
    /// after the id resolved, the arguments validated and the policy admitted the invocation, and
    /// before the body runs; the body's answer settles it — `Ready v` as `Completed` (`encode v`),
    /// `Failed m` as `Refused m` — and a `Pending` answer leaves the attempt open, with the returned
    /// `Ticket` (`key`, occurrence) the one `OpStream.settleEffectKeyed` settles it with later. A
    /// refusal before the body journals nothing, so a policy refusal leaves no capture; a
    /// deterministic capability journals nothing either (`Ticket = None`). The `Outcome` is exactly
    /// `dispatch`'s; a `CapturedDispatch` since Phase 391.
    let dispatchCaptured
        (hashFn: HashFn)
        (encode: 'v -> string)
        (r: CapabilityRegistry)
        (id: string)
        (args: (string * string) list)
        (body: Capability -> unit -> Deferred<'v>)
        (journal: KeyedCapture list)
        : CapturedDispatch<'v, InvokeError> =
        let unjournalled outcome =
            { Outcome = outcome
              Ticket = None
              Journal = journal }

        match Map.tryFind id r.Capabilities with
        | None -> unjournalled (Error(NoSuchCapability(id, r.Capabilities |> Map.toList |> List.map fst)))
        | Some c ->
            match Capability.validateArgs c args |> Result.bind (fun () -> admit r c args) with
            | Error e -> unjournalled (Error e)
            | Ok() ->
                let det = Capability.determinismTag c

                if det = OpStream.deterministicTag then
                    unjournalled (InvokeError.settle (body c ()))
                else
                    let key = Capability.invocationKey c args
                    let mutable answered = Pending

                    let captured =
                        OpStream.captureEffectKeyed
                            hashFn
                            encode
                            det
                            key
                            (fun () ->
                                answered <- body c ()
                                Deferred.settled answered)
                            journal

                    { Outcome = InvokeError.settle answered
                      Ticket = Some(key, captured.Occurrence)
                      Journal = captured.Journal }

    /// REPLAY an invocation from the keyed capture journal instead of running its body (Phase 318) —
    /// so a `Network` capability's replay is exact. The id resolves, the arguments validate and the
    /// policy runs exactly as `dispatch` (a refusal there is answered as `dispatch` answers it, and
    /// consults no journal); then a non-deterministic capability is answered from the journal by its
    /// invocation key: a completion as `Ready`, a recorded refusal as the same `BodyFailed`, an attempt
    /// that never settled as `Pending`. A deterministic capability runs `body` live, as a reproducible
    /// effect may. A journal that cannot answer is `ReplayFailure.Unanswered` with the
    /// `KeyedCaptureFault` — `NoCapture` for an invocation never captured, `Exhausted` past the
    /// recorded ones — never a live call; every refusal of the seam, live or recorded, is
    /// `ReplayFailure.Refused`. A `ReplayedDispatch` since Phase 391: one `Result`, and the cursor
    /// beside it whatever the outcome.
    let dispatchReplayed
        (decode: string -> Result<'v, string>)
        (r: CapabilityRegistry)
        (id: string)
        (args: (string * string) list)
        (body: Capability -> unit -> Deferred<'v>)
        (cursor: Map<string, int>)
        (journal: KeyedCapture list)
        : ReplayedDispatch<'v, InvokeError> =
        let refused e =
            { Outcome = Error(ReplayFailure.Refused e)
              Cursor = cursor }

        match Map.tryFind id r.Capabilities with
        | None -> refused (NoSuchCapability(id, r.Capabilities |> Map.toList |> List.map fst))
        | Some c ->
            match Capability.validateArgs c args |> Result.bind (fun () -> admit r c args) with
            | Error e -> refused e
            | Ok() ->
                let det = Capability.determinismTag c

                if det = OpStream.deterministicTag then
                    { Outcome = InvokeError.settle (body c ()) |> Result.mapError ReplayFailure.Refused
                      Cursor = cursor }
                else
                    match
                        OpStream.replayEffectKeyed
                            decode
                            det
                            (Capability.invocationKey c args)
                            (fun () -> None)
                            cursor
                            journal
                    with
                    | Error fault ->
                        { Outcome = Error(ReplayFailure.Unanswered fault)
                          Cursor = cursor }
                    | Ok(answer, cursor') ->
                        { Outcome =
                            match answer with
                            | Some(Ok v) -> Ok(Ready v)
                            | Some(Error m) -> Error(ReplayFailure.Refused(BodyFailed m))
                            | None -> Ok Pending
                          Cursor = cursor' }

    // ---- the lifecycle (Phase 316): a registry is a lattice, not an append log ----

    /// Remove the capability registered under `id` — refused `NoSuchCapability(id, known)` when the
    /// registry does not hold it, naming every id it does hold. Removing a capability just
    /// registered gives back the registry it was registered into.
    let unregister (id: string) (r: CapabilityRegistry) : Result<CapabilityRegistry, InvokeError> =
        KeyedRegistry.unregister (fun id known -> NoSuchCapability(id, known)) id r.Capabilities
        |> Result.map (fun m -> { r with Capabilities = m })

    /// Swap the capability registered under `c.Id` for `c` — the hot-reload verb. Refused
    /// `NoSuchCapability` when the id is not registered, and held to the admission gate `register`
    /// runs (`NonTotalCapability`, `IllFormedCapability`), so a replacement can admit nothing a
    /// registration would refuse. On a refusal the registry is unchanged.
    let replace (c: Capability) (r: CapabilityRegistry) : Result<CapabilityRegistry, InvokeError> =
        KeyedRegistry.replace
            (fun id known -> NoSuchCapability(id, known))
            Capability.admissionFault
            c.Id
            c
            r.Capabilities
        |> Result.map (fun m -> { r with Capabilities = m })

    /// The registry narrowed to the ids in `keep` — a session- or actor-scoped default-deny is a
    /// `restrict` of the host's registry. An id in `keep` the registry does not hold is ignored, so
    /// the result enumerates a subset of what `r` enumerates and dispatches nothing `r` would refuse.
    let restrict (keep: Set<string>) (r: CapabilityRegistry) : CapabilityRegistry =
        { r with
            Capabilities = KeyedRegistry.restrict keep r.Capabilities }

    /// The join of two registries whose ids are disjoint — refused `DuplicateCapability` naming the
    /// first id, in id order, that both hold (no silent overwrite, as `register`). Associative. The
    /// union runs BOTH registries' gates and tells both registries' observers (Phase 318,
    /// `RegistryPolicy.combine`), so it refuses every invocation either registry's policy refused.
    let union (a: CapabilityRegistry) (b: CapabilityRegistry) : Result<CapabilityRegistry, InvokeError> =
        KeyedRegistry.union DuplicateCapability a.Capabilities b.Capabilities
        |> Result.map (fun m ->
            { Capabilities = m
              Policy = RegistryPolicy.combine a.Policy b.Policy })

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
        Decoder.tagDispatchWith what "$type" cases

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
          "kind", JStr(HoleKind.tag e.Kind)
          "required", JBool e.Required ]
        @ Function.kindMembers SpaceCodec.toJson e.Kind
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
                                |> Result.bind (fun slot ->
                                    // Phase 409: the entry is read AS its hole kind, so a tag without
                                    // the member its kind needs is refused `MissingField` at that
                                    // member. A slot travels without its derived space (Phase 229);
                                    // a member the kind does not read is not part of the entry.
                                    let holeKind =
                                        match kind, sp, ac with
                                        | "value", Some s, _ -> Ok(ValueHole s)
                                        | "repeat", Some s, _ -> Ok(RepeatHole s)
                                        | "slot", _, _ -> Ok(SlotHole slot)
                                        | "action", _, Some e -> Ok(ActionHole e)
                                        | "action", _, None -> Error(Decoder.missing "actionEffect")
                                        | _ -> Error(Decoder.missing "space")

                                    holeKind
                                    |> Result.map (fun k ->
                                        { Addr = addr
                                          Name = name
                                          Kind = k
                                          Required = required }))))))))

    let internal signatureJson (sg: Signature) : JVal =
        JObj
            [ "name", JStr sg.Name
              "effect", EffectCodec.toJson sg.Effect
              "holes", JArr(sg.Holes |> List.map entryJson) ]

    /// A signature's members, read with no admission check (Phase 385): the capability reader runs
    /// the registries' whole gate (`Capability.admissionOf`) over the signature it reads, and
    /// the bare signature reader runs well-formedness over this.
    let private signatureFieldsOf (el: JVal) : Result<Signature, DecodeError> =
        Decoder.field "name" Decoder.str el
        |> Result.bind (fun name ->
            Decoder.field "effect" EffectCodec.decoder el
            |> Result.bind (fun eff ->
                Decoder.field "holes" (Decoder.list entryOf) el
                |> Result.map (fun holes ->
                    { Name = name
                      Holes = holes
                      Effect = eff })))

    let private signatureOfDetailed (el: JVal) : Result<Signature, DecodeError> =
        signatureFieldsOf el
        |> Result.bind (fun sg ->
            // Phase 307: the reader runs the well-formedness check the registries run, so a
            // signature that decodes is one a registry could admit on well-formedness.
            match Signature.validate sg with
            | Ok() -> Ok sg
            | Error fault ->
                Error(
                    DecodeError.under
                        (PathSegment.Key "holes")
                        (DecodeError.make
                            DecodeCode.OutOfRange
                            "a well-formed declaration"
                            ("ill-formed signature: " + DeclarationFault.describe fault))
                ))

    /// Read a signature object (`name`, `effect`, `holes`) leniently, refusing a hole-kind tag
    /// outside `HoleKind.tags`, an entry without the member its kind needs (`space` for a value or
    /// repeat hole, `actionEffect` for an action hole; Phase 409) and a signature `Signature.validate`
    /// refuses (Phase 307). A slot's space is its constraint's tree, and is never read.
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

    /// `encodeJson` rendered canonically: one capability always renders to the same string. Total;
    /// over a declaration the admission gate accepts it is exactly `tryEncode`'s `Ok`, and over one
    /// with a non-finite bound it writes bytes no reader takes back — use `tryEncode` for a
    /// declaration nothing has validated.
    let encode (c: Capability) : string = Canon.render (encodeJson c)

    /// The GUARDED encode (Phase 307): `Canon.tryRender` of `encodeJson`, so a declaration with a
    /// non-finite bound or an ill-formed string is refused, naming it, rather than written as bytes
    /// the decoder would refuse. `Ok` is exactly `encode`'s string.
    let tryEncode (c: Capability) : Result<string, string> = Canon.tryRender (encodeJson c)

    let private decodeJsonDetailed (el: JVal) : Result<Capability, DecodeError> =
        // Phase 307: a capability document says so — its `"$type"` is `capability`.
        Decoder.field "$type" (tagged "not a capability declaration: " [ "capability", () ]) el
        |> Result.bind (fun () -> Decoder.field "id" Decoder.str el)
        |> Result.bind (fun id ->
            Decoder.field "signature" signatureFieldsOf el
            |> Result.bind (fun sg ->
                // Phase 385: THE admission gate the registries run (D111) — totality, then
                // well-formedness — over the signature just read, where Phase 307's reader ran the
                // well-formedness half alone. A declaration that decodes is one `register` admits,
                // and a refused one is refused at `signature.holes` with the registry's own error.
                match Capability.admissionOf id sg with
                | Some e ->
                    Error(
                        DecodeError.under
                            (PathSegment.Key "signature")
                            (DecodeError.under
                                (PathSegment.Key "holes")
                                (DecodeError.make
                                    DecodeCode.OutOfRange
                                    "a declaration the registries admit"
                                    (InvokeError.describe e)))
                    )
                | None -> Ok sg)
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
    /// A declaration the registries' admission gate refuses (`NonTotalCapability`,
    /// `IllFormedCapability`) is refused with that error's sentence (Phase 385).
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

        // Phase 307: one address, one argument — a repeated address is refused here as it is by
        // `validateArgs` (`DuplicateArg`), at the second occurrence.
        let distinctArgs (args: (string * string) list) : Result<(string * string) list, DecodeError> =
            let rec go (seen: Set<string>) (i: int) =
                function
                | [] -> Ok args
                | (addr: string, _) :: rest ->
                    if seen.Contains addr then
                        Error(
                            DecodeError.under
                                (PathSegment.Key "args")
                                (DecodeError.under
                                    (PathSegment.Index i)
                                    (DecodeError.make
                                        DecodeCode.OutOfRange
                                        "each address once"
                                        ("argument '" + addr + "' is given more than once")))
                        )
                    else
                        go (seen.Add addr) (i + 1) rest

            go Set.empty 0 args

        both (Decoder.field "capabilityId" Decoder.str) (Decoder.field "args" (Decoder.list arg)) (fun cid args ->
            cid, args)
        |> Decoder.andThen (fun (cid, args) -> distinctArgs args |> Result.map (fun a -> cid, a))

    /// Read an invocation back as `(capabilityId, args)` in wire order, leniently; argument values are
    /// read as strings and checked against nothing until `validateArgs`, but an address given twice
    /// is refused (Phase 307).
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

    /// A declaration fault as a wire document (Phase 307), `"$type"`-tagged.
    let internal faultJson (f: DeclarationFault) : JVal =
        match f with
        | EmptySpace(addr, space) -> Canon.typed "emptySpace" [ "addr", JStr addr; "space", SpaceCodec.toJson space ]
        | NonFiniteBound addr -> Canon.typed "nonFiniteBound" [ "addr", JStr addr ]
        | DuplicateHoleAddr addr -> Canon.typed "duplicateHoleAddr" [ "addr", JStr addr ]
        | HoleUnderSlot node -> Canon.typed "holeUnderSlot" [ "node", JStr node ]

    let private faultOf: Decoder<DeclarationFault> =
        let str name = Decoder.field name Decoder.str

        dispatch
            "unknown declaration fault: "
            [ "emptySpace", both (str "addr") (Decoder.field "space" SpaceCodec.decoder) (fun a sp -> EmptySpace(a, sp))
              "nonFiniteBound", str "addr" |> Decoder.map NonFiniteBound
              "duplicateHoleAddr", str "addr" |> Decoder.map DuplicateHoleAddr
              "holeUnderSlot", str "node" |> Decoder.map HoleUnderSlot ]

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
        | IllFormedCapability(id, fault) ->
            Canon.typed "illFormedCapability" [ "id", JStr id; "fault", faultJson fault ]
        | DuplicateArg addr -> Canon.typed "duplicateArg" [ "addr", JStr addr ]
        | PolicyRefused(policy, reason, allowed) ->
            Canon.typed "policyRefused" [ "policy", JStr policy; "reason", JStr reason; "allowed", strs allowed ]
        | ApprovalRequired policy -> Canon.typed "approvalRequired" [ "policy", JStr policy ]

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
                  both (str "id") (strList "addrs") (fun id addrs -> NonTotalCapability(id, addrs))
                  "illFormedCapability",
                  both (str "id") (Decoder.field "fault" faultOf) (fun id f -> IllFormedCapability(id, f))
                  "duplicateArg", str "addr" |> Decoder.map DuplicateArg
                  "policyRefused",
                  str "policy"
                  |> Decoder.bind (fun policy ->
                      both (str "reason") (strList "allowed") (fun reason allowed ->
                          PolicyRefused(policy, reason, allowed)))
                  "approvalRequired", str "policy" |> Decoder.map ApprovalRequired ]

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

    open SeamCodec

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
