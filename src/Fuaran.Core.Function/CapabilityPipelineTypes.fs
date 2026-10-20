namespace Fuaran.Core

// ============================================================================
//  Serializable capability pipeline (Phase 35) — a typed capability-DAG declared
//  as DATA, so a multi-step compute (load → invoke → derive → invoke) is
//  replayable end-to-end through the Phase-27 capture seam, not host-side glue.
//  A node's typed output feeds a downstream node's typed arg; an ill-typed edge
//  is a named error at composition time, never a runtime throw.
//
//  Nodes are `Source` (a named data ref) + `Invoke` (a registered capability).
//  A compute step — a data-frame transform, say, from the compute layer that is
//  produced outside this repository since 0.33.0 — participates as a `Capability`
//  whose body runs its evaluator, so the pipeline needs NO dedicated transform node
//  and `Function` takes no dependency on any compute package (GP1/GP2: additive over
//  the Capability surface). FSharp.Core only, Fable-clean.
// ============================================================================

/// Where an `Invoke` node's argument comes from: a literal scalar, or the output of an upstream node
/// (the edge that wires the DAG).
type ArgSource =
    /// A fixed argument string, which must lie in its hole's space.
    | Literal of value: string
    /// The output of the node with this id, which must be declared EARLIER and whose output space
    /// must fit the hole's.
    | FromNode of nodeId: string

/// A node in a capability pipeline. `Source` is a named data ref (the `DataSource.Ref` precedent) with a
/// declared output value-space; `Invoke` runs a registered capability, declaring its output value-space
/// and wiring each arg (by hole address) to a `Literal` or an upstream node's output.
type PipelineNode =
    /// An input the host supplies: `dataRef` is opaque to the core, and `outputType` is what
    /// downstream edges are checked against.
    | Source of id: string * dataRef: string * outputType: ValueSpace
    /// A call of `capabilityId`, each argument keyed by hole address; `outputType` is declared, not
    /// derived, and every downstream edge is checked against it.
    | Invoke of id: string * capabilityId: string * outputType: ValueSpace * args: (string * ArgSource) list

/// A serializable, typed capability-DAG (Phase 35) — nodes in topological (declaration) order; edges are
/// the `FromNode` arg references. `OpStream.Dag` can host it; type-checked at composition.
type CapabilityPipeline =
    {
        /// The nodes in declaration order, which must be a topological order: an edge may point only
        /// to an earlier node, and node ids must be unique.
        Nodes: PipelineNode list
    }

/// Why a pipeline was rejected — recoverable + enumerated (GP5), never a throw (GP4). Default-deny by
/// shape: only a registered capability + a type-compatible, fully-bound, ACYCLIC edge set in
/// declaration order composes. The cases keep their tags where they survived (`EdgeTypeMismatch` is
/// still the sixth).
///
/// Since Phase 295 an argument refusal wraps the `InvokeError` the node's capability gives the same
/// argument — `UnknownArg` naming the declared holes, `ArgOutOfSpace` carrying the space and the
/// value, `UninvocableArg`, `RequiredArgsUnbound` — as `PackLoadError.PackRegisterFailed` wraps a
/// registration's, so a pipeline refusal says what the equivalent invocation refusal says. It
/// replaced `PipelineUnknownArg`, `PipelineArgOutOfSpace` and `PipelineRequiredUnbound`, which
/// carried the address alone.
[<RequireQualifiedAccess>]
type PipelineError =
    /// Two nodes share this id; checked before anything else.
    | DuplicateNode of id: string
    /// A `FromNode` edge names an id that is no node of the pipeline.
    | UnknownNode of id: string
    /// An `Invoke` names a capability the lookup does not resolve; `known` is the lookup's ids.
    | PipelineNoSuchCapability of id: string * known: string list
    /// The node's capability refuses this argument (Phase 295): the wrapped `InvokeError` is the
    /// refusal `Capability.validateArgs` gives it.
    | PipelineArgRefused of node: string * reason: InvokeError
    /// An edge closes a cycle (Phase 295): `cycle` is the node ids around it, starting at `node`
    /// and following each `FromNode` edge to its upstream; a self-edge is `[node]`.
    | PipelineCycle of node: string * cycle: string list
    /// The upstream's output space does not fit argument `arg` of `node`. `producer` and `consumer`
    /// name only the space FAMILIES (`int`, `float`, `string`, `enum`, `anyString`, `slotTree`), so a
    /// bounds mismatch inside one family reads as e.g. `int` against `int`.
    | EdgeTypeMismatch of node: string * arg: string * producer: string * consumer: string
    /// An acyclic edge that points FORWARD in declaration order (Phase 295) — the pipeline is not in
    /// topological order, so the upstream would not have run.
    | PipelineForwardEdge of node: string * arg: string * upstream: string

/// A pipeline node's argument, resolved for evaluation (Phase 62): an upstream node's realised output, or
/// a declared literal. The engine resolves each `ArgSource` edge into one of these before handing the arg
/// list to the host `body` — so the body sees values, never the wire-level `FromNode` reference.
type PipelineArg<'v> =
    /// The upstream node's realised value, already checked (through `spell`) to lie in the hole's space.
    | FromUpstream of 'v
    /// The node's `Literal` string, as declared; `typeCheck` put it in the hole's space.
    | LiteralArg of string

/// Why pipeline evaluation failed — recoverable + named (GP5), never a throw (GP4). Since Phase 295
/// evaluation type-checks first: a pipeline `typeCheck` refuses is `EvalIllTyped`, and no body runs —
/// which is also what retired `EvalUnknownNode`, since a forward reference is now a typeCheck refusal.
/// An upstream value outside the space of the hole it feeds is `EvalArgRefused`, wrapping the
/// `ArgOutOfSpace` the capability gives it; a host `body` failure is `EvalNodeFailed`.
[<RequireQualifiedAccess>]
type PipelineEvalError =
    /// `typeCheck` refused the pipeline; no body ran.
    | EvalIllTyped of reason: PipelineError
    /// The host `body` answered `Error message` for this node; evaluation stops there.
    | EvalNodeFailed of node: string * message: string
    /// An upstream value, spelled, lies outside the space of the hole it feeds; `reason` is the
    /// `ArgOutOfSpace` carrying that spelling.
    | EvalArgRefused of node: string * reason: InvokeError
    /// The lookup's policy refused this node's invocation (Phase 383): `reason` is the registry's own
    /// refusal — `PolicyRefused` naming the gate for a `Deny`, `ApprovalRequired` for a park — after
    /// every denial observer was told. The node's body did not run, and evaluation stops there.
    | EvalPolicyRefused of node: string * reason: InvokeError

/// Where a pipeline resolves its `Invoke` nodes' capabilities (Phase 295): a lookup, the ids it
/// holds (for the refusal that names them) and the policy that admits an invocation (Phase 383).
/// Both registries project one — `ofRegistry`, `ofFunctionRegistry` — so a host that loads content
/// packs into a `FunctionRegistry` type-checks and evaluates against it, and keeps no second registry.
type CapabilityLookup =
    {
        /// Resolves a capability id; `None` makes an `Invoke` of it a `PipelineNoSuchCapability`.
        TryFind: string -> Capability option
        /// The ids `TryFind` resolves, reported in `PipelineNoSuchCapability`; it is not consulted to
        /// resolve, so a hand-built lookup should keep the two in step.
        Known: string list
        /// The gates every `Invoke` node passes before its body runs (Phase 383) — the registry's own
        /// `Policy` in both projections, so evaluating a pipeline over a registry tightened with
        /// `withGate` admits exactly what that registry's dispatch admits. A hand-built lookup states
        /// its policy; `RegistryPolicy.none` runs no gate.
        Policy: RegistryPolicy<Capability, (string * string) list>
    }

/// The two projections onto `CapabilityLookup`.
module CapabilityLookup =

    /// A capability registry as a lookup, carrying the registry's policy.
    let ofRegistry (r: CapabilityRegistry) : CapabilityLookup =
        { TryFind = fun id -> CapabilityRegistry.tryFind id r
          Known = Seam.ids r.Seam
          Policy = r.Seam.Policy }

    /// A function registry as a lookup: each entry's capability, by its id, and the registry's policy.
    let ofFunctionRegistry (r: FunctionRegistry) : CapabilityLookup =
        { TryFind = fun id -> FunctionRegistry.tryFind id r |> Option.map _.Capability
          Known = FunctionRegistry.ids r
          Policy = r.Policy }

/// The pipeline wire's one host-side reader (Phase 551): the codec of `ArgSource`, `PipelineNode` and
/// the pipeline record is DERIVED from their IDL declaration (`PipelineSeamCodec`, DECISIONS.md D200),
/// and reads a node's declared output space through this.
module internal PipelineWire =

    /// A node's declared output space: `SpaceCodec`'s reader, then the admission check (Phase 307), so
    /// an output space the admission check refuses is refused on read.
    let outputSpace (el: JVal) : Result<ValueSpace, DecodeError> =
        SpaceCodec.decoder el
        |> Result.bind (fun sp ->
            match Space.wellFormed sp with
            | Ok() -> Ok sp
            | Error fault ->
                Error(
                    DecodeError.make
                        DecodeCode.OutOfRange
                        "a well-formed value space"
                        (match fault with
                         | SpaceFault.Empty -> "empty value-space: " + Space.describe sp
                         | SpaceFault.NonFinite -> "value-space bound is not a finite number")
                ))
