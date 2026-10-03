namespace Fuaran.Core

/// The domain-supplied op-algebra generator: how to build a random tree, and how to mint
/// a fresh node whose id avoids a given set (for `InsertChild`). `CanHold` (Phase 251) is
/// the container capability — `Some p` exercises the laws through `Ops.applyContained` so a
/// witness whose `ReplaceChildren` is partial on leaves certifies green without restricting
/// the generator to containers; `None` uses the plain `apply` (every node can hold children).
type OpGen<'Node, 'Id> =
    {
        /// Draws a random base tree for one iteration; the ops the laws apply are drawn against
        /// it, so the generator's breadth bounds what the laws can reach.
        Tree: ConfRng.T -> 'Node * ConfRng.T
        /// Mints a node whose id, in its `IdWitness.ToString` form, is NOT in the given set — the
        /// kit passes the ids already taken and relies on the fresh id colliding with none of them.
        FreshNode: Set<string> -> ConfRng.T -> 'Node * ConfRng.T
        /// The container predicate the laws apply through, or `None` when every node can hold
        /// children.
        CanHold: ('Node -> bool) option
    }

/// The domain-supplied op-stream generator: the base state and a random op source.
type StreamGen<'Op, 'State> =
    {
        /// The state every drawn stream is reduced and replayed from.
        State0: 'State
        /// Draws one op, independently of any state — a drawn op may be one the reducer rejects,
        /// and the stream laws must hold over those too.
        Op: ConfRng.T -> 'Op * ConfRng.T
    }

// ---- Phase 246: the seam witnesses ----
//
// The seam families (`capabilityLaws`, `queryLaws`, `capabilityPipelineLaws`) take a seed and
// certify Core's own fixtures, so a domain's registry, body and host dispatch are out of their
// reach: downstream consumers' measurements (Phase 246) planted a host that ran the body before the
// registry refused, and every seam family stayed green. These three records are what a domain hands
// the witness-taking forms instead. They COMPOSE the seam's own types rather than growing any
// frozen witness (STABILITY's "compose, never grow").

/// A domain's capability seam, as `Conformance.capabilityLawsWith` certifies it (Phase 246).
///
/// - `Registry` — the registry the domain dispatches against. The laws read it as the ORACLE: a
///   call whose id is registered and whose arguments `Capability.validateArgs` accepts must reach
///   the body, and every other call must be refused with the registry's own error.
/// - `Body` — the domain's body, handed the call's arguments first, in the shape
///   `CapabilityRegistry.dispatch` wants after them. The kit wraps it to count how often it runs.
/// - `Dispatch` — the domain's HOST path: the function its surface actually calls to invoke a
///   capability, handed the id, the arguments and the body to run. A host that delegates to Core
///   passes `CapabilityRegistry.dispatch registry`; a host with its own wiring passes that wiring, which is the
///   point — a defect in it (a body run before the registry refuses) is what the family can see.
/// - `GenCall` — the calls a model could make: registered and invented ids, arguments in space, out
///   of space, missing and stray. The family is starved unless it reaches a settled, a pending and a
///   refused dispatch.
type CapabilitySeamWitness<'v> =
    {
        /// The oracle: whether a call should reach the body, and which error it must be refused
        /// with otherwise, is read from this registry alone.
        Registry: CapabilityRegistry
        /// The domain's body, arguments first. The kit counts its runs: a refused call must run it
        /// zero times, any other call exactly once.
        Body: (string * string) list -> Capability -> unit -> Deferred<'v>
        /// The host's invocation path — id, arguments, body. It must refuse with exactly the
        /// registry's error and must never return `Ok(Failed _)`; a throw fails the family.
        Dispatch:
            string
                -> (string * string) list
                -> (Capability -> unit -> Deferred<'v>)
                -> Result<Deferred<'v>, InvokeError>
        /// Draws one `(id, arguments)` call. It must reach a settled, a pending and a refused
        /// dispatch, or the family reports itself starved.
        GenCall: ConfRng.T -> (string * (string * string) list) * ConfRng.T
    }

/// A domain's query seam, as `Conformance.queryLawsWith` certifies it (Phase 246) — the query
/// mirror of `CapabilitySeamWitness`: `Queries` is the oracle, `Resolver` the domain's resolver
/// (handed the call's arguments first), `Dispatch` the host path (`QueryRegistry.dispatch queries`
/// for a host that delegates to Core), and `GenQuery` the calls a model could make.
type QuerySeamWitness =
    {
        /// The oracle: whether a call should reach the resolver, and which error refuses it
        /// otherwise, is read from this registry alone.
        Queries: QueryRegistry
        /// The domain's resolver, arguments first. The kit counts its runs: a refused call must
        /// run it zero times, any other call exactly once.
        Resolver: (string * Cell) list -> Query -> Deferred<QueryResult>
        /// The host's query path — id, arguments, resolver. It must refuse with exactly the
        /// registry's error; a throw fails the family.
        Dispatch:
            string
                -> (string * Cell) list
                -> (Query -> Deferred<QueryResult>)
                -> Result<Deferred<QueryResult>, QueryError>
        /// Draws one `(id, arguments)` call. It must reach a settled, a pending and a refused
        /// dispatch, or the family reports itself starved.
        GenQuery: ConfRng.T -> (string * (string * Cell) list) * ConfRng.T
    }

/// A domain's capability pipelines, as `Conformance.capabilityPipelineLawsWith` certifies them
/// (Phase 246): the registry they compose against, and the pipelines the domain builds.
type CapabilityPipelineWitness =
    {
        /// The registry every drawn pipeline must type-check against; the kit also derives an id
        /// absent from it to build the unregistered-capability refusal.
        PipelineRegistry: CapabilityRegistry
        /// Draws one pipeline the domain would build. The default-deny laws are built per `Invoke`
        /// node, so pipelines with none leave the family starved.
        GenPipeline: ConfRng.T -> CapabilityPipeline * ConfRng.T
    }

// `LawResult` — one law's verdict — is defined in `SampleAdequacy.fs`, which is compiled ahead of
// this file. It moved there in Phase 121 for one reason: the adequacy guard produces `LawResult`s
// like every family here does, and every family here declares its demands through the guard, so the
// guard has to precede them — and it deliberately depends on no family, which makes it the right
// place for the type they all share.

/// The domain-supplied AUTHORING surface (Phase 126): how to rebuild a decoded value through the
/// smart constructors / builders a program actually writes against, rather than through the decoded
/// record itself. `Surface` names that surface in the report — it is read by a human reading a
/// counterexample, so name the thing an author calls ("the smart constructors", "the `ui` builders"),
/// not the module it lives in.
///
/// `Construct` returns a `Result` because an authoring surface is allowed to REFUSE: a constructor
/// that validates is the common case, and a refusal of a value the domain's own codec just decoded is
/// itself a finding (`constructThenEncodeLaws`' second law), not an exception.
///
/// It is deliberately NOT part of any existing witness. A domain opts in by supplying one, and a
/// domain that supplies none is reported by name rather than skipped — see `constructThenEncodeLaws`.
type ConstructWitness<'T> =
    {
        /// The authoring surface as an author would name it; it appears verbatim in every
        /// counterexample.
        Surface: string
        /// Rebuilds a decoded value through that surface. `Error` is a refusal the laws report as a
        /// finding, never an exception.
        Construct: 'T -> Result<'T, string>
    }

// `KeyedWitness` (Phase 189) was declared here until Phase 286, which moved it to
// `Fuaran.Core.Tree` beside `NodeWitness`: the keyed walks and `Ops.applyContainedKeyed` read it,
// and neither can reference this assembly. The namespace is unchanged.

/// The domain-supplied INCREMENTAL EVALUATOR (Phase 211): what a domain hands `Propagation.eval`
/// and `Propagation.evalFrom`, together with the edits it re-evaluates under and the change set it
/// names for each. `Conformance.propagationEvaluatorLaws` runs it.
///
/// **Why this is a witness of its own.** The agreement theorem (`evalfrom_agrees`,
/// `proofs/Propagation.fst`) is generic over the evaluator, so the evaluator is the model's
/// PARAMETER and stays outside every theorem. What the theorem assumes of it — that it is a
/// function of what it reads, and that a change set names every node on which it moved — can only
/// be checked at the evaluator a domain actually runs. The kit's own `propagationEvalLaws`
/// certifies the DRIVER over a toy evaluator; this certifies an ADOPTER, over its own.
///
/// `'Model` is whatever the domain evaluates — a sheet of formulas, a pipeline, a document — and
/// every other field reads it, so one generated model yields the map, the evaluator and the edits
/// the domain would hand the driver for it.
type EvaluatorWitness<'Model, 'V> =
    {
        /// The domain's evaluator, named as a reader of a counterexample would look for it — name
        /// the thing an author calls ("the cell evaluator in `Sheet.recalc`"), not the module.
        Surface: string
        /// A generated domain model, drawn from the domain's own generator.
        Model: ConfRng.T -> 'Model * ConfRng.T
        /// The dependency map the domain hands `Propagation.eval` / `evalFrom` for this model.
        Deps: 'Model -> Map<string, Set<string>>
        /// The domain's per-node evaluator for this model — exactly what it hands the driver.
        EvalNode: 'Model -> (string -> 'V option) -> string -> Result<'V, string>
        /// An edit to the model, and the change set the domain would hand `evalFrom` for it. The
        /// change set is the CLAIM being certified, so it is the domain's and never derived by the
        /// kit — a kit that computed it from the edit would agree with the domain by construction.
        Change: 'Model -> ConfRng.T -> ('Model * Set<string>) * ConfRng.T
    }

/// The aggregate certification report.
type ConformanceReport =
    {
        /// Every law's verdict, in the order the aggregate ran its families.
        Results: LawResult list
        /// `true` when every result passed — vacuously `true` for an empty `Results`.
        AllPassed: bool
    }

/// The composition sample (Phase 47) a domain supplies per draw to certify cross-witness
/// `composeAcross`. `Outer` is an `'A`-function carrying TWO independent typed slots (`SlotA`,
/// `SlotB`, by absolute address) plus its value-hole bindings (`OuterArgs`, addr → in-space
/// value). `ClosedInner` is a fully-bound `'B`-function whose `embed`-lift fits either slot.
/// `OpenInnerA` / `OpenInnerB` each carry exactly one open value hole sharing the name
/// `OpenHoleName` but at DISTINCT ids (so the two re-rooted copies get distinct absolute
/// addresses — the hygiene case), fillable with `OpenHoleArg`.
type CompositionSample<'A, 'B> =
    {
        /// The outer function, holding both slots and the value holes `OuterArgs` binds.
        Outer: 'A
        /// The first slot's absolute address in `Outer`; it must be disjoint from `SlotB`, since
        /// the laws compose into both in either order.
        SlotA: string
        /// The second slot's absolute address in `Outer`.
        SlotB: string
        /// `(address, value)` for every value hole of `Outer`, each value in its hole's space —
        /// strict application must succeed with exactly these.
        OuterArgs: (string * string) list
        /// A fully-bound inner function; composed into both slots, it leaves no hole open.
        ClosedInner: 'B
        /// The inner function composed into `SlotA` for the hygiene law, with one open value hole
        /// named `OpenHoleName`.
        OpenInnerA: 'B
        /// The inner function composed into `SlotB`; its open hole shares `OpenHoleName` but sits
        /// at a different id from `OpenInnerA`'s.
        OpenInnerB: 'B
        /// The name both open holes carry; after composition exactly two holes must bear it, at
        /// distinct addresses.
        OpenHoleName: string
        /// An in-space value for the open hole, bound to one copy to show the other stays open.
        OpenHoleArg: string
    }

// ---- artifact-function property-verification (Phase 48) ----
// Lift verification from "is this *tree* valid?" to "does this *function* produce a valid tree
// for ALL (sampled / symbolic) valid param sets?" — property-test an artifact-function against
// a domain `Validator.Registry` (the validity oracle the verifier *drives*, read-only). The
// correct-by-construction property no freeform code-gen can offer: a saved typed-tree function
// is certified valid across its whole binding space, not just one instance. The function-under-
// test, the validator registry, and the param-set source all ride as per-call parameters (GP2);
// no new witness field (additive over the frozen `ArtifactWitness`).

/// Why one param-set failed verification (Phase 48) — a typed defect, never a throw (GP4).
type VerifyDefect<'Id> =
    /// the "valid" param-set was itself rejected by `apply` (a generator producing an
    /// out-of-space / unbound set, or a non-total function).
    | DidNotApply of ApplyError
    /// `apply` produced a tree the domain validator faulted (≥1 `Severity.Error` defect).
    | ValidatorRejected of Defect<'Id> list
    /// the applied tree observes an effect its declared class does not cover (Fork-3 cross-check).
    | EffectObserved of declared: EffectClass * observed: EffectClass

/// A reproducible counterexample: the offending param-set, the defect, and the seed/iteration
/// (a failure is reproduced by re-running the same seed — deterministic seed-replay).
type VerifyCounterexample<'Node, 'Id> =
    {
        /// The failing bindings as `(address, argument)`, sorted by address.
        ParamSet: (string * Arg<'Node>) list
        /// Why the bindings failed: refused by `apply`, faulted by the validator, or an effect the
        /// declared class does not cover.
        Defect: VerifyDefect<'Id>
        /// The seed the run was given — the same for every iteration, so re-running with it
        /// reproduces the whole run.
        Seed: int
        /// The 0-based index of the failing param-set: its draw within the run, or its position
        /// in the enumeration when the space was enumerated. The run stops here, so the report's
        /// coverage counts `Iteration + 1` cases evaluated.
        Iteration: int
    }

/// How the param space was covered — coverage honesty (never silently sample-and-claim-verified).
/// A report states what was EVALUATED, never what was planned (Phase 348): `Exhaustive` when every
/// case of a finite space was evaluated, `Sampled` otherwise — a run that stopped early included.
type VerifyCoverage =
    /// Every combination of the finite hole domains was evaluated; `cases` is the size of that
    /// product, which is the count evaluated. Only `verifyFunctionSymbolic` reports it, and only
    /// when the enumeration reached its last case — a counterexample AT the last case included.
    | Exhaustive of cases: int
    /// Fewer cases than the whole space were evaluated, or the space is not known to be finite:
    /// `drawn` is the count of cases actually evaluated, under both verifiers — a counterexample
    /// stops the run, so it is the counterexample's `Iteration + 1` when there is one. The cases are
    /// draws, or, when `verifyFunctionSymbolic` stopped an enumeration early, the enumeration's
    /// first `drawn` cases in order. `spaceSize` is the size of the space they came from: `None`
    /// when the space is unbounded (a hole ranges over `FloatRange` / `StringLen` / `AnyString`),
    /// too large for an `int`, or (under `verifyFunction`, whose generator is opaque) not known.
    | Sampled of drawn: int * spaceSize: int option

/// The verification verdict: certified across the covered param space, or a counterexample.
type FunctionVerifyReport<'Node, 'Id> =
    {
        /// `true` exactly when `Counterexample` is `None`: no covered param-set failed. Structural
        /// validity only — never a claim about output quality or determinism.
        Verified: bool
        /// How much of the param space the verdict stands on; read it before trusting `Verified`.
        Coverage: VerifyCoverage
        /// The first failing param-set, if any — the run stops there.
        Counterexample: VerifyCounterexample<'Node, 'Id> option
    }

// ---- memoised application (Phase 49) ----
// The teeth on `Function.applyMemo` (content-addressed application caching) + its collapse of
// op-stream replay into re-application over the memo.

/// The memo sample (Phase 49) a domain supplies per draw to certify `Function.applyMemo`. `PureFn`
/// is a memoisable (fully pure & deterministic) function with a valid full param-set `Args`;
/// `ArgsAlt` is a DIFFERENT valid full param-set (it must key distinctly — the "a param change
/// misses" case). `EffectingFn` is a non-memoisable (non-deterministic / host-effecting) function
/// with a valid full param-set `EffectingArgs` (the soundness-guard case — never served from cache).
type MemoSample<'Node> =
    {
        /// A pure, deterministic function — memoisable, so a repeat application must hit the cache.
        PureFn: 'Node
        /// A valid full param-set for `PureFn`, applied twice: the first a miss, the second a hit.
        /// When it has two or more bindings and the first by address is a value, it also drives
        /// the key-collision arm.
        Args: Map<string, Arg<'Node>>
        /// A second valid full param-set for `PureFn` that must key differently from `Args` — a
        /// lookup with it must miss.
        ArgsAlt: Map<string, Arg<'Node>>
        /// A function whose effect class is not memoisable; `applyMemo` must never serve it from
        /// cache.
        EffectingFn: 'Node
        /// A valid full param-set for `EffectingFn`.
        EffectingArgs: Map<string, Arg<'Node>>
    }

/// The domain-supplied CONTENT-EDIT generator (Phase 297): given a node the tree holds, an edited
/// node carrying the SAME id — what `UpdateNode` rewrites a node with. The kit cannot draw a content
/// edit on its own (it does not know what a node's content is), so without one it draws the
/// identity update — `UpdateNode n` for a node `n` already in the tree — which exercises every
/// `UpdateNode` path (validation, rebuild, invert, footprint, conflict detection) over an edit
/// that changes nothing. A domain that supplies one certifies the same laws over real edits.
///
/// It composes rather than growing `OpGen` ("compose, never grow", STABILITY.md): a domain that
/// has no content edits constructs nothing new.
type UpdateGen<'Node> =
    {
        /// An edited copy of `node`, with the same id. The kit never reads the copy's children —
        /// `UpdateNode` keeps the tree's — so a generator may return any children it likes.
        Update: 'Node -> ConfRng.T -> 'Node * ConfRng.T
    }

/// The sanitisation floor a host ships, as the six functions `Conformance.sanitizeLaws` holds to
/// the floor each one claims (Phase 349). `Fuaran.Core.Idl.Sanitize` is the reference
/// (`SanitizeWitness.core`); a host carrying its own copy of the floor — a renderer that predates
/// the lift, a port in another pipeline — certifies that copy by building this record from it, so
/// parity between the copies is held by the laws rather than by a comment.
type SanitizeWitness =
    {
        /// The sanitised URL, or `None` for a refused one (`Sanitize.sanitizeUrl`).
        SanitizeUrl: string -> string option
        /// The sanitised URL or the deny sentinel `about:blank` (`Sanitize.sanitizeUrlOrBlank`).
        SanitizeUrlOrBlank: string -> string
        /// The attribute-key allowlist (`Sanitize.isAllowedAttributeKey`).
        IsAllowedAttributeKey: string -> bool
        /// The attribute-value floor (`Sanitize.isSafeAttributeValue`).
        IsSafeAttributeValue: string -> bool
        /// The attribute-map filter (`Sanitize.sanitizeAttributes`).
        SanitizeAttributes: Map<string, string> -> Map<string, string>
        /// The markdown scrub (`Sanitize.scrubMarkdown`).
        ScrubMarkdown: string -> string
    }

/// The reference sanitisation witness (Phase 349).
module SanitizeWitness =

    /// `Fuaran.Core.Idl.Sanitize`, the floor every host's copy is held to.
    let core: SanitizeWitness =
        { SanitizeUrl = Fuaran.Core.Idl.Sanitize.sanitizeUrl
          SanitizeUrlOrBlank = Fuaran.Core.Idl.Sanitize.sanitizeUrlOrBlank
          IsAllowedAttributeKey = Fuaran.Core.Idl.Sanitize.isAllowedAttributeKey
          IsSafeAttributeValue = Fuaran.Core.Idl.Sanitize.isSafeAttributeValue
          SanitizeAttributes = Fuaran.Core.Idl.Sanitize.sanitizeAttributes
          ScrubMarkdown = Fuaran.Core.Idl.Sanitize.scrubMarkdown }
