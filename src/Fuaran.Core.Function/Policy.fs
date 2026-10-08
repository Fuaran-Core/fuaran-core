namespace Fuaran.Core

// ============================================================================
//  The policy gate (Phase 318) — the SHAPE of a permission decision, which Core
//  owns, with every policy's CONTENT left to the domain that writes it.
//
//  Before this phase being registered was the same as being permitted: a
//  registry refused only an id it did not hold, and the one place a decision
//  had three outcomes (`PolicyDecision`, inside the AI surface's witness) had no
//  way to combine two of them. Every consumer that needed a gate wrote one, and
//  the combinations they wrote were the same one: the most restrictive of the
//  decisions wins (`Allow < NeedsApproval < Deny`). That is `PolicyDecision.join`.
//
//  A registry carries a `RegistryPolicy`: the gates it runs and the observers it
//  tells about a refusal. `withGate` only ever ADDS a gate, and the decision is
//  the join over every gate, so a gate can tighten a registry and can never
//  loosen it. FSharp.Core + Fuaran.Core.Ops (whose `RejectionGuidance` a denial
//  carries) only, Fable-clean.
// ============================================================================

/// The policy decision for one action by one actor — the shape of a domain's permission policy (the
/// rules stay domain-side). Declared here since Phase 318, where the invocable registries' gate can
/// reach it; it was declared in `Fuaran.Core.AiSurface` before, in the same namespace, so a source that
/// names it compiles unchanged.
///
/// **A denial is GUIDANCE (Phase 298).** `Deny` carries a `RejectionGuidance` — the message and the
/// alternatives the actor may take instead — so a refused agent repairs from the same envelope a
/// reducer rejection gives it. `RequireQualifiedAccess`: `PolicyDecision.Allow`, …
[<RequireQualifiedAccess>]
type PolicyDecision =
    /// The action may proceed now. The least restrictive decision: the identity of `join`.
    | Allow
    /// The action must be approved first. Between the two: it outranks `Allow` and `Deny` outranks it.
    | NeedsApproval
    /// The action is refused, with the guidance a refused actor repairs from. The most restrictive
    /// decision: it absorbs every other under `join`.
    | Deny of guidance: RejectionGuidance

/// Constructors and the lattice over `PolicyDecision` (Phase 298 constructors; Phase 318 lattice).
///
/// The decisions are ordered by how much they refuse, `Allow < NeedsApproval < Deny`, and `join` is the
/// maximum in that order — the decision of two policies that must BOTH permit. `meet` is the minimum
/// — the decision of two policies of which EITHER suffices. Two denials are equal in rank; the join
/// and the meet keep the LEFT one's guidance, so a fold reports the first denial it met.
module PolicyDecision =

    /// A denial with `reason` and no alternatives.
    let deny (reason: string) : PolicyDecision =
        PolicyDecision.Deny { Message = reason; Alternatives = [] }

    /// A denial with `reason` and the `alternatives` the actor may take instead.
    let denyWith (reason: string) (alternatives: string list) : PolicyDecision =
        PolicyDecision.Deny
            { Message = reason
              Alternatives = alternatives }

    /// How much the decision refuses: `Allow` 0, `NeedsApproval` 1, `Deny` 2. The order `join` and
    /// `meet` are the maximum and the minimum in.
    let rank (d: PolicyDecision) : int =
        match d with
        | PolicyDecision.Allow -> 0
        | PolicyDecision.NeedsApproval -> 1
        | PolicyDecision.Deny _ -> 2

    /// The more restrictive of two decisions — both policies must permit. Commutative and associative
    /// up to which denial's guidance is kept (the left one on a tie), idempotent, with `Allow` its
    /// identity and any `Deny` absorbing. Monotone: raising either argument never lowers the result
    /// (`proofs/Capability.fst`, `policy_join_monotone`).
    let join (a: PolicyDecision) (b: PolicyDecision) : PolicyDecision = if rank b > rank a then b else a

    /// The less restrictive of two decisions — either policy suffices. The left one on a tie.
    let meet (a: PolicyDecision) (b: PolicyDecision) : PolicyDecision = if rank b < rank a then b else a

    /// The join of every decision — what a conjunction of policies decides. `Allow` for none (the
    /// join's identity); otherwise the most restrictive, the first of its rank where several tie, so
    /// the guidance is the first denial's.
    let all (decisions: PolicyDecision list) : PolicyDecision =
        decisions |> List.fold join PolicyDecision.Allow

    /// The meet of every decision — what a disjunction of policies decides. With none to consult
    /// nothing permits the action, so the empty disjunction is a denial (default-deny by shape);
    /// otherwise the least restrictive, the first of its rank where several tie.
    let any (decisions: PolicyDecision list) : PolicyDecision =
        match decisions with
        | [] -> deny "no policy permits this action"
        | d :: rest -> rest |> List.fold meet d

    /// The guidance a refusal carries: a denial's own, and for `NeedsApproval` a sentence saying the
    /// action waits on an approval. `None` for `Allow`.
    let guidance (d: PolicyDecision) : RejectionGuidance option =
        match d with
        | PolicyDecision.Allow -> None
        | PolicyDecision.NeedsApproval ->
            Some
                { Message = "this action needs approval before it runs"
                  Alternatives = [] }
        | PolicyDecision.Deny g -> Some g

/// One named policy over the invocations of a registry (Phase 318): `Decide` sees the declaration
/// the invocation resolved to and its validated arguments. `Policy` names it in every refusal it
/// makes, so a refused caller and a denial observer can tell which of several gates said no. The
/// content of `Decide` is the domain's; Core only runs it, after validation and before the body.
type PolicyGate<'Decl, 'Args> =
    {
        /// The gate's name, carried by `PolicyRefused` / `ApprovalRequired` and by `PolicyDenial`.
        Policy: string
        /// The decision for one resolved declaration and its validated arguments. Must be total and
        /// side-effect free: a registry may consult it to answer `decide` without dispatching.
        Decide: 'Decl -> 'Args -> PolicyDecision
    }

/// A refusal a registry's policy made, as a denial observer sees it (Phase 318): which gate refused,
/// the id invoked, the arguments, and the decision — a `Deny` with its guidance, or `NeedsApproval`.
type PolicyDenial<'Args> =
    {
        /// The refusing gate's `Policy` name.
        Policy: string
        /// The id the invocation named.
        Id: string
        /// The arguments the invocation carried, as validated.
        Args: 'Args
        /// `Deny` or `NeedsApproval` — never `Allow`, which is not a refusal.
        Decision: PolicyDecision
    }

/// The gates a registry runs and the observers it notifies of a refusal (Phase 318). Built only
/// through `RegistryPolicy`; a registry starts with `RegistryPolicy.none`, which runs no gate and so
/// admits every validated invocation, exactly as a registry did before this phase.
///
/// **Equality is identity of the functions.** A function has no extensional equality, so two
/// policies are equal when they hold the same gates — the same `Policy` name AND the same `Decide`
/// function value — and the same observer values, in the same order. A registry is still equal to
/// itself after `register` then `unregister`, because both carry its policy through untouched.
/// There is no ordering: a registry carrying a policy is not `comparison`.
[<CustomEquality; NoComparison>]
type RegistryPolicy<'Decl, 'Args> =
    private
        { Gates: PolicyGate<'Decl, 'Args> list
          Observers: (PolicyDenial<'Args> -> unit) list }

    /// Equal when both hold the same gates — the same `Policy` name AND the same `Decide` function
    /// value, in order — and the same observer values, in order. Identity of the functions, because a
    /// function has no extensional equality.
    override p.Equals(o: obj) =
        match o with
        | :? RegistryPolicy<'Decl, 'Args> as q ->
            p.Gates.Length = q.Gates.Length
            && p.Observers.Length = q.Observers.Length
            && List.forall2
                (fun (a: PolicyGate<'Decl, 'Args>) (b: PolicyGate<'Decl, 'Args>) ->
                    a.Policy = b.Policy && obj.ReferenceEquals(a.Decide, b.Decide))
                p.Gates
                q.Gates
            && List.forall2
                (fun (a: PolicyDenial<'Args> -> unit) b -> obj.ReferenceEquals(a, b))
                p.Observers
                q.Observers
        | _ -> false

    /// Consistent with `Equals`: hashes the gate names and the observer count, so equal policies hash
    /// equal.
    override p.GetHashCode() =
        p.Gates |> List.fold (fun h g -> h * 31 + hash g.Policy) p.Observers.Length

/// Building and consulting a `RegistryPolicy` (Phase 318). The three registries forward to these;
/// a host reaches them through `CapabilityRegistry.withGate`, `FunctionRegistry.withGate` and
/// `QueryRegistry.withGate`.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module RegistryPolicy =

    /// No gate and no observer: every validated invocation is admitted.
    let none<'Decl, 'Args> : RegistryPolicy<'Decl, 'Args> =
        { Gates = []; Observers = [] }

    /// `p` with `gate` added after its gates. Adding is the only verb: the decision is the join over
    /// every gate, so the result refuses everything `p` refused, and possibly more.
    let withGate (gate: PolicyGate<'Decl, 'Args>) (p: RegistryPolicy<'Decl, 'Args>) : RegistryPolicy<'Decl, 'Args> =
        { p with Gates = p.Gates @ [ gate ] }

    /// `p` with `observe` added after its observers. Every observer is told of every refusal, in the
    /// order they were added.
    let onDenied
        (observe: PolicyDenial<'Args> -> unit)
        (p: RegistryPolicy<'Decl, 'Args>)
        : RegistryPolicy<'Decl, 'Args> =
        { p with
            Observers = p.Observers @ [ observe ] }

    /// The policy of the union of two registries: both policies' gates and observers, the left's
    /// first. A union never loosens either side — an invocation either registry's policy refused,
    /// the union's refuses. Associative.
    let combine (a: RegistryPolicy<'Decl, 'Args>) (b: RegistryPolicy<'Decl, 'Args>) : RegistryPolicy<'Decl, 'Args> =
        { Gates = a.Gates @ b.Gates
          Observers = a.Observers @ b.Observers }

    /// The gate names, in the order they run.
    let gates (p: RegistryPolicy<'Decl, 'Args>) : string list = p.Gates |> List.map _.Policy

    /// The decision for one declaration and its arguments, with the name of the gate that made it:
    /// the join over every gate, in order, so the deciding gate is the FIRST of the most restrictive
    /// rank. `("", Allow)` when no gate runs. Consults the gates only — no observer is told.
    let decideNamed (p: RegistryPolicy<'Decl, 'Args>) (decl: 'Decl) (args: 'Args) : string * PolicyDecision =
        p.Gates
        |> List.fold
            (fun (name, acc) g ->
                let d = g.Decide decl args

                if PolicyDecision.rank d > PolicyDecision.rank acc then
                    g.Policy, d
                else
                    name, acc)
            ("", PolicyDecision.Allow)

    /// The decision alone: `PolicyDecision.all` over the gates' decisions.
    let decide (p: RegistryPolicy<'Decl, 'Args>) (decl: 'Decl) (args: 'Args) : PolicyDecision =
        decideNamed p decl args |> snd

    /// Run the gates for one invocation: `Ok ()` on `Allow`; otherwise every observer is told, in
    /// order, and the refusal is the seam's own — `refused policy guidance` for a `Deny`,
    /// `approval policy` for `NeedsApproval`. Internal: the registries' dispatch is the surface.
    let internal admit
        (refused: string -> RejectionGuidance -> 'E)
        (approval: string -> 'E)
        (p: RegistryPolicy<'Decl, 'Args>)
        (id: string)
        (decl: 'Decl)
        (args: 'Args)
        : Result<unit, 'E> =
        match decideNamed p decl args with
        | _, PolicyDecision.Allow -> Ok()
        | name, d ->
            let denial =
                { Policy = name
                  Id = id
                  Args = args
                  Decision = d }

            for observe in p.Observers do
                observe denial

            match d with
            | PolicyDecision.Deny g -> Error(refused name g)
            | _ -> Error(approval name)
