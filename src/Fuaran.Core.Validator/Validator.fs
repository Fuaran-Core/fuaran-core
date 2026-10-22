namespace Fuaran.Core

/// Defect severity. Shared across domains; the *codes* are domain-side.
/// `RequireQualifiedAccess` so the `Error` case never shadows `Result.Error` in a
/// consumer that `open`s `Fuaran.Core` — write `Severity.Error`.
[<RequireQualifiedAccess>]
type Severity =
    /// A fault: the one severity `Validator.hasErrors` counts. A throwing rule's `RULE-FAULT`
    /// finding is always one.
    | Error
    /// Worth a reader's attention but not a fault — `hasErrors` ignores it (the stock `REF-UNUSED`
    /// finding is one).
    | Warning
    /// Informational only; `hasErrors` ignores it.
    | Info

/// A single validation finding. `Code` is the domain's stable defect code (e.g.
/// FUARAN058, the Calc six, a Doc house-style pack rule). `Node` locates it.
///
/// **`Family` and `Related` (Phase 298).** `Family` is the id of the rule family (or column rule,
/// or `pack/rule`) that produced the finding — the provenance the `PackRule` convention below
/// promised and the record could not carry. A rule body writes `""` (or builds through
/// `Defect.create`); the walkers that run it (`Validator.runAll`, `ColumnValidator.validate`,
/// `Validator.runPack`) STAMP it, so a rule cannot mis-cite itself. `Related` is the supporting
/// enumeration — the other nodes a finding is about (the members of a cycle, the declaration a
/// reference resolves to, the legal alternatives) — so the envelope agrees with
/// `Rejection.UnknownNode(target, addressable)` and `RejectionGuidance.Alternatives` instead of each
/// domain adding the field to its own copy of this record. `[]` when there is nothing to enumerate.
type Defect<'Id> =
    {
        /// The stable code hosts compare — `Validator.canonicalCodes` projects these, and only these,
        /// into the cross-host parity string.
        Code: string
        /// How serious the finding is; only `Severity.Error` makes `Validator.hasErrors` true.
        Severity: Severity
        /// Human-readable explanation. Outside the cross-host parity projection, which reads only
        /// `Code`, so rewording a message never breaks parity.
        Message: string
        /// Where the finding is located, or `None` for a finding about the whole subject (a thrown
        /// rule's `RULE-FAULT`).
        Node: 'Id option
        /// The producing family, rule or `pack/rule` id. A rule body leaves it `""`; the walker
        /// that runs the rule overwrites it, so whatever the body writes is discarded.
        Family: string
        /// The other locations the finding is about (a cycle's members, the declaration referred
        /// to), `[]` when there are none.
        Related: 'Id list
    }

/// Constructors for `Defect` (Phase 298): the two provenance fields defaulted, for a rule body
/// whose walker stamps them.
module Defect =

    /// A finding with no family yet (`""`, stamped by the walker) and no related nodes.
    let create (code: string) (severity: Severity) (message: string) (node: 'Id option) : Defect<'Id> =
        { Code = code
          Severity = severity
          Message = message
          Node = node
          Family = ""
          Related = [] }

    /// `d` attributed to `family` — what every walker does to a rule's output.
    let inFamily (family: string) (d: Defect<'Id>) : Defect<'Id> = { d with Family = family }

/// Why a rule registry refused an edit (Phase 298): the id is already registered, or (Phase 316)
/// it is not. `registered` enumerates the ids the registry holds, in registration order, so the
/// refusal names the closed set (GP5).
[<RequireQualifiedAccess>]
type RegistrationError =
    /// `id` is already registered; `registered` lists every id the registry holds, in
    /// registration order.
    | DuplicateRule of id: string * registered: string list
    /// `id` is not registered (Phase 316: `unregister` / `replace` name a family the registry does
    /// not hold); `registered` lists every id the registry holds, in registration order.
    | UnknownRule of id: string * registered: string list

/// A registered rule family — a named bundle of checks the walker runs over a tree.
/// The framework owns registration + walking; the rule *body* is domain-supplied.
type RuleFamily<'Node, 'Id> =
    {
        /// The family's registry key and the provenance stamped on every finding it produces;
        /// unique within a registry (`Validator.register` refuses a repeat).
        Id: string
        /// The checks over a whole tree, given its root. May throw: the walker turns a throw into
        /// one `RULE-FAULT` error and keeps running the other families.
        Run: NodeWitness<'Node, 'Id> -> 'Node -> Defect<'Id> list
    }

/// The rule-pack extension point: a rule contributed by a pack layered atop a base
/// domain's rule families (the Documents → Legal pack-atop-pack composition). This is the
/// extensibility seam for layered domain rule families.
///
/// **Provenance convention (the public contract for pack-contributed rules).** A pack rule
/// is legible and collision-free by construction when its `RuleFamily.Id` is minted as
/// `pack + "/" + ruleId` (e.g. `"legal-house-style/no-passive-voice"`). The `/` separator
/// makes the contributing pack recoverable from any `Defect`'s family id without a side
/// channel, so a host can attribute, filter, or disable a whole pack's findings. Core owns
/// only this shape — the pack *content* stays domain-side, and the concrete defect-**code**
/// numbering is a domain choice (a domain that uses a `FUARAN###`-style scheme reserves a
/// band for pack/host-assigned codes so they never collide with its own spec rules — see the
/// consuming domain's error-code reference). This keeps pack-layering certifiable through the
/// public framework without the framework ever shipping pack rules.
type PackRule =
    {
        /// The contributing pack's name — the part of a family id before the `/`.
        Pack: string
        /// The rule's id within its pack — the part after the `/`.
        RuleId: string
    }

/// The rule-family framework: registration, the per-node walker scaffolding, defect
/// aggregation, and the cross-host defect-code byte-parity helper. All rule content
/// stays domain-side; the core owns only the scaffolding.
module Validator =

    /// An ordered registry of rule families, one family per id (Phase 298: renamed from
    /// `Registry`, which sat beside `Function`'s registry under the same name).
    type RuleRegistry<'Node, 'Id> =
        {
            /// The families in registration order — the order `runAll` runs them and reports their
            /// findings in; no two share an id when built through `register` / `ofFamilies`.
            Families: RuleFamily<'Node, 'Id> list
        }

    // ---- the registration-ordered lattice (Phase 316; written once in Phase 521) ----
    //
    // The four lifecycle verbs the invocable seams' registries carry over an id-keyed map
    // (`KeyedRegistry`, in `Fuaran.Core.Function`), over a LIST: a validator registry keeps
    // REGISTRATION order, which is the order its walker runs the rules and reports their findings in,
    // so it cannot share the id-ordered body (D117.2). Until Phase 521 this registry wrote the six
    // verbs out by hand and `ColumnValidator`'s registry wrote three of them again; both are now this
    // one body, read through the id each entry registers under, refusing in `RegistrationError`.

    /// The registration-ordered lattice: each function is total, keeps the order of what it is given,
    /// and leaves the list unchanged on a refusal.
    module internal Ordered =

        /// The ids `xs` holds, in registration order — what every refusal enumerates.
        let ids (idOf: 'R -> string) (xs: 'R list) : string list = xs |> List.map idOf

        let private holds (idOf: 'R -> string) (id: string) (xs: 'R list) : bool =
            xs |> List.exists (fun x -> idOf x = id)

        /// `x` after the entries held — refused `DuplicateRule` when its id is held.
        let register (idOf: 'R -> string) (x: 'R) (xs: 'R list) : Result<'R list, RegistrationError> =
            if holds idOf (idOf x) xs then
                Error(RegistrationError.DuplicateRule(idOf x, ids idOf xs))
            else
                Ok(xs @ [ x ])

        /// The entries without the one under `id` — refused `UnknownRule` when it is not held.
        let unregister (idOf: 'R -> string) (id: string) (xs: 'R list) : Result<'R list, RegistrationError> =
            if holds idOf id xs then
                Ok(xs |> List.filter (fun x -> idOf x <> id))
            else
                Error(RegistrationError.UnknownRule(id, ids idOf xs))

        /// `x` in the position of the entry under its id — refused `UnknownRule` when it is not held.
        let replace (idOf: 'R -> string) (x: 'R) (xs: 'R list) : Result<'R list, RegistrationError> =
            let id = idOf x

            if holds idOf id xs then
                Ok(xs |> List.map (fun y -> if idOf y = id then x else y))
            else
                Error(RegistrationError.UnknownRule(id, ids idOf xs))

        /// The entries whose id is in `keep`, in their order — a subsequence, never a widening.
        let restrict (idOf: 'R -> string) (keep: Set<string>) (xs: 'R list) : 'R list =
            xs |> List.filter (fun x -> keep.Contains(idOf x))

        /// `a` then `b` — refused `DuplicateRule` naming the first of `b`'s ids, in `b`'s order, that
        /// `a` holds (and `a`'s ids). Associative.
        let union (idOf: 'R -> string) (a: 'R list) (b: 'R list) : Result<'R list, RegistrationError> =
            let held = ids idOf a |> Set.ofList

            match b |> List.tryFind (fun x -> held.Contains(idOf x)) with
            | Some x -> Error(RegistrationError.DuplicateRule(idOf x, ids idOf a))
            | None -> Ok(a @ b)

        /// `register` folded from the empty list, refusing the first repeated id.
        let ofList (idOf: 'R -> string) (xs: 'R list) : Result<'R list, RegistrationError> =
            (Ok [], xs) ||> List.fold (fun acc x -> acc |> Result.bind (register idOf x))

    let private familyId (f: RuleFamily<'Node, 'Id>) : string = f.Id

    let private holding (families: RuleFamily<'Node, 'Id> list) : RuleRegistry<'Node, 'Id> = { Families = families }

    /// A registry holding no family, so `runAll` over it finds nothing; the seed `ofFamilies` folds from.
    let empty<'Node, 'Id> : RuleRegistry<'Node, 'Id> = { Families = [] }

    /// The registered family ids, in registration order.
    let enumerate (reg: RuleRegistry<'Node, 'Id>) : string list = Ordered.ids familyId reg.Families

    /// The family registered under `id`, if any.
    let tryFind (id: string) (reg: RuleRegistry<'Node, 'Id>) : RuleFamily<'Node, 'Id> option =
        reg.Families |> List.tryFind (fun f -> f.Id = id)

    /// Register `family` after the ones already held — REFUSED as `DuplicateRule` when its id is
    /// already registered (Phase 298; it used to append, so a second registration doubled every
    /// finding of the family and two families could share one provenance id).
    let register
        (family: RuleFamily<'Node, 'Id>)
        (reg: RuleRegistry<'Node, 'Id>)
        : Result<RuleRegistry<'Node, 'Id>, RegistrationError> =
        Ordered.register familyId family reg.Families |> Result.map holding

    /// Remove the family registered under `id`, keeping the others in order — refused `UnknownRule`
    /// when the registry does not hold it, naming every id it does hold. Removing a family just
    /// registered gives back the registry it was registered into.
    let unregister (id: string) (reg: RuleRegistry<'Node, 'Id>) : Result<RuleRegistry<'Node, 'Id>, RegistrationError> =
        Ordered.unregister familyId id reg.Families |> Result.map holding

    /// Swap the family registered under `family.Id` for `family`, in the same position — refused
    /// `UnknownRule` when the id is not registered.
    let replace
        (family: RuleFamily<'Node, 'Id>)
        (reg: RuleRegistry<'Node, 'Id>)
        : Result<RuleRegistry<'Node, 'Id>, RegistrationError> =
        Ordered.replace familyId family reg.Families |> Result.map holding

    /// The registry narrowed to the families whose id is in `keep`, in their registration order. An
    /// id in `keep` the registry does not hold is ignored, so the result enumerates a subsequence of
    /// what `reg` enumerates.
    let restrict (keep: Set<string>) (reg: RuleRegistry<'Node, 'Id>) : RuleRegistry<'Node, 'Id> =
        holding (Ordered.restrict familyId keep reg.Families)

    /// `a`'s families then `b`'s, each in its own order — refused `DuplicateRule` naming the first of
    /// `b`'s ids, in `b`'s order, that `a` also holds (and `a`'s ids), so no family runs twice under
    /// one provenance id. Associative.
    let union
        (a: RuleRegistry<'Node, 'Id>)
        (b: RuleRegistry<'Node, 'Id>)
        : Result<RuleRegistry<'Node, 'Id>, RegistrationError> =
        Ordered.union familyId a.Families b.Families |> Result.map holding

    /// A registry holding `families` in order — `register` folded from `empty`, refusing the first
    /// repeated id.
    let ofFamilies (families: RuleFamily<'Node, 'Id> list) : Result<RuleRegistry<'Node, 'Id>, RegistrationError> =
        Ordered.ofList familyId families |> Result.map holding

    /// Build a rule family from a per-node predicate — the common shape. The walker
    /// visits every node in preorder and collects whatever defects the rule emits.
    let perNode (id: string) (rule: NodeWitness<'Node, 'Id> -> 'Node -> Defect<'Id> list) : RuleFamily<'Node, 'Id> =
        { Id = id
          Run = fun w root -> Tree.preorder w root |> List.collect (rule w) }

    // ---- Phase 489: a finding and its recovery guidance (DECISIONS.md D184.8) ----
    // What a refused emitter needs beside the defect is what to do and what it could have said
    // instead: the envelope's `RejectionGuidance`, the shape `Rejection.explain` and a policy's
    // `Deny` already answer in. It rides BESIDE the defect, as `PackFinding` wraps one, never as a
    // field of `Defect`: the defect is the parity surface (`canonicalCodes`, the gate's `(code,
    // node)`), and guidance is advice that can be reworded without moving either.

    /// A defect with the recovery guidance its rule stated (Phase 489): what a guided walk
    /// (`Rules.runFindings`) answers, so a validator hands an agent the remedy and the alternatives
    /// without a finding type of its own and without a lowering that loses them.
    type Finding<'Id> =
        {
            /// The finding itself, stamped by the walk exactly as the defect walk stamps it.
            Defect: Defect<'Id>
            /// The repair, as the rule stated it: `Message` is the remedy — what the emitter should
            /// do, `""` where the rule names none — and `Alternatives` what it could have written
            /// instead (the enum's cases, the fields a kind admits), `[]` where it enumerates none.
            /// `None` when the rule states no guidance; a thrown rule's `RULE-FAULT` is always `None`.
            Guidance: RejectionGuidance option
        }

    /// Constructors for `Finding` (Phase 489).
    [<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
    module Finding =

        /// `d` with no guidance — how every defect a `PackCheck` returns enters a guided walk.
        let ofDefect (d: Defect<'Id>) : Finding<'Id> = { Defect = d; Guidance = None }

        /// `d` with `guidance` — the remedy in its `Message` and the alternatives in its
        /// `Alternatives`.
        let guided (guidance: RejectionGuidance) (d: Defect<'Id>) : Finding<'Id> =
            { Defect = d; Guidance = Some guidance }

    /// The stock code of a family that THREW instead of returning its findings (Phase 298).
    [<Literal>]
    let FamilyFaultCode = "RULE-FAULT"

    /// Run one rule body, total: its findings stamped with `family`, or — when it throws — one
    /// `Severity.Error` finding coded `RULE-FAULT` naming the family and the exception's message, so
    /// one broken rule never costs the findings of the others.
    let internal runGuarded (family: string) (run: unit -> Finding<'Id> list) : Finding<'Id> list =
        try
            run ()
            |> List.map (fun f ->
                { f with
                    Defect = Defect.inFamily family f.Defect })
        with ex ->
            [ Finding.ofDefect
                  { Code = FamilyFaultCode
                    Severity = Severity.Error
                    Message = "rule '" + family + "' failed: " + ex.Message
                    Node = None
                    Family = family
                    Related = [] } ]

    /// THE WALKER (Phase 521; over findings since Phase 489): every rule of `rules` over its subject,
    /// in rule order then each rule's own order, each finding paired with the rule that produced it,
    /// its defect stamped `stamp rule` in `Family` and its guidance carried as the rule stated it; a
    /// rule that throws is one `RULE-FAULT` error, unguided, and the rules after it still run.
    /// `runAllTagged`, `Rules.run`, `Rules.runFindings` and `runPack` are this one walk — over a
    /// family, a check and a pack's check; until Phase 521 each, and `ColumnValidator.validateTagged`,
    /// was a walk of its own.
    let internal walkFindings
        (stamp: 'R -> string)
        (run: 'R -> Finding<'Id> list)
        (rules: 'R list)
        : ('R * Finding<'Id>) list =
        rules
        |> List.collect (fun r -> runGuarded (stamp r) (fun () -> run r) |> List.map (fun f -> r, f))

    /// The walker over rules that state no guidance: `walkFindings` with each defect unguided, read
    /// back as its defect — so a defect walk and a finding walk cannot disagree over a defect.
    let internal walk (stamp: 'R -> string) (run: 'R -> Defect<'Id> list) (rules: 'R list) : ('R * Defect<'Id>) list =
        walkFindings stamp (run >> List.map Finding.ofDefect) rules
        |> List.map (fun (r, f) -> r, f.Defect)

    /// Run every registered family over the tree and pair each finding with the id of the family
    /// that produced it (Phase 298), in family order then each family's own order. Each finding's
    /// `Family` is stamped with that id; a family that throws contributes one `RULE-FAULT` error and
    /// the families after it still run.
    let runAllTagged
        (w: NodeWitness<'Node, 'Id>)
        (reg: RuleRegistry<'Node, 'Id>)
        (root: 'Node)
        : (string * Defect<'Id>) list =
        walk familyId (fun f -> f.Run w root) reg.Families
        |> List.map (fun (f, d) -> f.Id, d)

    /// Run every registered family over the tree, concatenating defects in family order — the
    /// findings of `runAllTagged`, each carrying its family in `Family`. Total (Phase 298).
    let runAll (w: NodeWitness<'Node, 'Id>) (reg: RuleRegistry<'Node, 'Id>) (root: 'Node) : Defect<'Id> list =
        runAllTagged w reg root |> List.map snd

    /// True when any finding is a `Severity.Error`; warnings and infos alone never make it true.
    let hasErrors (defects: Defect<'Id> list) : bool =
        defects |> List.exists (fun d -> d.Severity = Severity.Error)

    /// Per-severity counts over a defect list (Phase 25) — the everyday summary domains otherwise
    /// re-derive by hand. Pure and total.
    type Summary =
        {
            /// The count of `Severity.Error` findings; non-zero exactly when `hasErrors` is true.
            Errors: int
            /// The count of `Severity.Warning` findings — never a fault on its own.
            Warnings: int
            /// The count of `Severity.Info` findings — never a fault on its own.
            Infos: int
        }

    /// The three counts in one value. They always add up to the length of `defects`, since every
    /// finding has exactly one severity.
    let summary (defects: Defect<'Id> list) : Summary =
        { Errors = defects |> List.filter (fun d -> d.Severity = Severity.Error) |> List.length
          Warnings = defects |> List.filter (fun d -> d.Severity = Severity.Warning) |> List.length
          Infos = defects |> List.filter (fun d -> d.Severity = Severity.Info) |> List.length }

    /// Canonical, order-independent projection of the emitted defect codes — the cross-host
    /// byte-parity surface (two conformant hosts must produce the same set). The sorted codes go
    /// through `Hash.canonicalFields` (Phase 290): each code escaped and terminated by `U+0001`, so
    /// the projection is INJECTIVE over sorted code lists whatever a code contains — the Phase 25
    /// bare `U+0001` join promised that only for codes that never spell the byte, and the `,` join
    /// before it not even that. Parity-string-breaking vs both earlier forms: a host that persisted
    /// the old projection re-derives it, and a host twin re-certifies against the new bytes.
    let canonicalCodes (defects: Defect<'Id> list) : string =
        defects |> List.map _.Code |> List.sort |> Hash.canonicalFields

    // ---- Phase 314: the defect-set diff and its gate verdict ----
    // What a merge gate asks is not "is the candidate valid" but "did THIS step make it less valid":
    // a defect every baseline already carried was not caused by the fold, so it is carried through
    // and never flagged, and the defects the candidate has that no baseline has are the ones the
    // step INTRODUCED. The diff is a set difference keyed on `(code, node)` — the identity two hosts
    // agree on, as `canonicalCodes` keys on codes — and the verdict is one of three policies over it.

    /// How introduced defects gate a candidate (Phase 314). `RequireQualifiedAccess`:
    /// `GatePolicy.Gated`.
    [<RequireQualifiedAccess>]
    type GatePolicy =
        /// The validator is not consulted: nothing is reported and nothing blocks.
        | Lenient
        /// The introduced defects are reported; nothing blocks.
        | Diagnostic
        /// The introduced defects are reported, and an introduced `Severity.Error` blocks.
        | Gated

    /// The verdict of a gate over a candidate (Phase 314): the policy it was read under, the defects
    /// the candidate introduced against every baseline (`[]` under `Lenient`), and whether the policy
    /// blocks it — `Gated` and an introduced error, and nothing else.
    type GateVerdict<'Id> =
        {
            /// The policy the verdict was read under.
            Policy: GatePolicy
            /// The introduced defects in canonical `(code, node)` order; `[]` under `Lenient`.
            Introduced: Defect<'Id> list
            /// True exactly when `Policy` is `Gated` and `Introduced` carries a `Severity.Error`.
            Blocked: bool
        }

    /// The identity the introduced-set diff keys on: the code and the node key (`None` for a finding
    /// about the whole subject). Message, family and the related nodes are outside it, as they are
    /// outside `canonicalCodes`: two hosts agree on codes and locations, not on prose.
    let private identityOf (idw: IdWitness<'Id>) (d: Defect<'Id>) : string * string option =
        d.Code, d.Node |> Option.map idw.ToString

    /// The defects of `candidate` whose `(code, node)` identity no baseline's findings carry, in
    /// canonical order — ascending code, then node key, a whole-subject finding before any node —
    /// each kept as the candidate reported it (a `(code, node)` the candidate reports twice is
    /// reported twice). Pure over the defect lists, so a host that has already run its validators
    /// diffs their output here; `introduced` is the form that runs a registry. No baseline at all
    /// introduces everything.
    let introducedDefects
        (idw: IdWitness<'Id>)
        (baselines: Defect<'Id> list list)
        (candidate: Defect<'Id> list)
        : Defect<'Id> list =
        let known = baselines |> List.collect (List.map (identityOf idw)) |> Set.ofList

        candidate
        |> List.filter (fun d -> not (Set.contains (identityOf idw d) known))
        |> List.sortBy (identityOf idw)

    /// Run `reg` over `candidate` and every baseline, and report the candidate's findings absent from
    /// every baseline's (`introducedDefects`). One baseline is a before/after gate; two are a merge's
    /// parents over a lane fold; none makes every finding introduced.
    let introduced
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (reg: RuleRegistry<'Node, 'Id>)
        (baselines: 'Node list)
        (candidate: 'Node)
        : Defect<'Id> list =
        introducedDefects idw (baselines |> List.map (runAll w reg)) (runAll w reg candidate)

    /// Read introduced defects under a policy: `Lenient` reports none and never blocks, `Diagnostic`
    /// reports them and never blocks, `Gated` reports them and blocks on an introduced error
    /// (`hasErrors`). Pure, so a host composes it over `introducedDefects` or `introduced`.
    let verdict (policy: GatePolicy) (introduced: Defect<'Id> list) : GateVerdict<'Id> =
        match policy with
        | GatePolicy.Lenient ->
            { Policy = policy
              Introduced = []
              Blocked = false }
        | GatePolicy.Diagnostic ->
            { Policy = policy
              Introduced = introduced
              Blocked = false }
        | GatePolicy.Gated ->
            { Policy = policy
              Introduced = introduced
              Blocked = hasErrors introduced }

    /// The whole gate in one call: `verdict policy (introduced w idw reg baselines candidate)`, except
    /// that `Lenient` runs no validator at all.
    let gate
        (policy: GatePolicy)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (reg: RuleRegistry<'Node, 'Id>)
        (baselines: 'Node list)
        (candidate: 'Node)
        : GateVerdict<'Id> =
        match policy with
        | GatePolicy.Lenient -> verdict policy []
        | _ -> verdict policy (introduced w idw reg baselines candidate)

    /// The policy's tag in the verdict encoding.
    let private policyTag (p: GatePolicy) : string =
        match p with
        | GatePolicy.Lenient -> "lenient"
        | GatePolicy.Diagnostic -> "diagnostic"
        | GatePolicy.Gated -> "gated"

    /// The severity's tag in the verdict encoding.
    let private severityTag (s: Severity) : string =
        match s with
        | Severity.Error -> "error"
        | Severity.Warning -> "warning"
        | Severity.Info -> "info"

    /// The canonical, byte-stable encoding of a verdict — the cross-host surface a refused fold's
    /// verdict hash is taken over, as `canonicalCodes` is for a defect list. Through
    /// `Hash.canonicalFields`: the policy tag, `blocked` or `passed`, then for each introduced defect
    /// in canonical order its code, its location as two fields (`node` and the id key, or `subject`
    /// and the empty string) and its severity tag. INJECTIVE over verdicts up to the fields it reads
    /// (message, family and related nodes are outside it, as they are outside the parity projection),
    /// so two hosts that introduce one defect set under one policy encode one string, and a
    /// `Hash.sha256Hex` over it is the verdict hash.
    let encodeVerdict (idw: IdWitness<'Id>) (v: GateVerdict<'Id>) : string =
        policyTag v.Policy
        :: (if v.Blocked then "blocked" else "passed")
        :: (v.Introduced
            |> List.sortBy (identityOf idw)
            |> List.collect (fun d ->
                [ d.Code
                  (match d.Node with
                   | Some _ -> "node"
                   | None -> "subject")
                  (match d.Node with
                   | Some n -> idw.ToString n
                   | None -> "")
                  severityTag d.Severity ]))
        |> Hash.canonicalFields

    // ---- Phase 315: the versioned rule pack ----
    // The container the `PackRule` convention above was always about, which every domain that
    // ships packs wrote for itself: a named, VERSIONED set of rules whose findings each carry the
    // rule that produced them and a `pack@version` citation, so a report can cite the rule and a
    // consumer can pin the pack version it audits against. Generic over the SUBJECT a rule reads (a
    // tree root, a document, a model, a voicing sequence) — a tree family is the instance
    // `fun root -> family.Run w root` — and over the defect's location `'Id`.

    /// One rule of a pack: its id within the pack (the citation key) and its check.
    type PackCheck<'Subject, 'Id> =
        {
            /// The key the rule is cited by (`<pack>@<version>/<ruleId>`) and stamped into each
            /// finding's `Family` as `<pack>/<ruleId>`; `runPack` does not check it is unique.
            RuleId: string
            /// The check. May throw: `runPack` turns a throw into one `RULE-FAULT` finding and runs
            /// the rules after it.
            Run: 'Subject -> Defect<'Id> list
        }

    /// A versioned rule pack. `Version` is the pack's own, cited on every finding.
    type Pack<'Subject, 'Id> =
        {
            /// The pack's name — the `Pack` of every `PackRule` it stamps and the citation's prefix.
            Name: string
            /// Opaque text cited after the `@`; never parsed or compared as a version number.
            Version: string
            /// The rules in the order `runPack` runs them and reports their findings in.
            Rules: PackCheck<'Subject, 'Id> list
        }

    /// One finding of a pack run: the defect, the `PackRule` that produced it (stamped by `runPack`,
    /// so a rule cannot mis-cite itself), and the citation `<pack>@<version>/<ruleId>`.
    type PackFinding<'Id> =
        {
            /// The pack and rule that produced the finding, stamped by `runPack` rather than taken
            /// from the rule body.
            Rule: PackRule
            /// `<pack>@<version>/<ruleId>` — the same rule under another pack version cites differently.
            Citation: string
            /// The finding itself, its `Family` stamped `<pack>/<ruleId>`.
            Defect: Defect<'Id>
        }

    /// The citation of a pack's rule: `<pack>@<version>/<ruleId>` — the `PackRule` family id
    /// convention (`pack + "/" + ruleId`) with the version the finding was produced under.
    let citation (pack: Pack<'Subject, 'Id>) (ruleId: string) : string =
        pack.Name + "@" + pack.Version + "/" + ruleId

    /// Run every rule of `pack` over `subject`, in rule order then each rule's own defect order,
    /// stamping each defect with its `PackRule` and citation.
    let runPack (pack: Pack<'Subject, 'Id>) (subject: 'Subject) : PackFinding<'Id> list =
        // the `PackRule` family-id convention, stamped on the defect itself (Phase 298); a rule that
        // throws is one `RULE-FAULT` finding, as under every walk (it IS the one walk, Phase 521)
        walk
            (fun (rule: PackCheck<'Subject, 'Id>) -> pack.Name + "/" + rule.RuleId)
            (fun rule -> rule.Run subject)
            pack.Rules
        |> List.map (fun (rule, d) ->
            { Rule =
                { Pack = pack.Name
                  RuleId = rule.RuleId }
              Citation = citation pack rule.RuleId
              Defect = d })

    /// A tree rule family as a `PackCheck` over the tree's root (Phase 298) — the subject-generic
    /// rule `PackCheck` already is, with the tree family the instance `fun root -> family.Run w
    /// root`. So a tree family and a rule over any other subject (a column table, a voicing
    /// sequence) meet in one pack without `RuleFamily` being retyped.
    let asCheck (w: NodeWitness<'Node, 'Id>) (family: RuleFamily<'Node, 'Id>) : PackCheck<'Node, 'Id> =
        { RuleId = family.Id
          Run = family.Run w }

    /// A rule that states the recovery of its findings (Phase 489): `PackCheck`'s shape with a body
    /// answering `Finding`s, so the remedy and the alternatives are computed where the rule has the
    /// context to compute them. A `Rules` registry holds it beside plain checks (`Rules.registerGuided`)
    /// and `Rules.runFindings` hands its guidance back intact.
    type GuidedCheck<'Subject, 'Id> =
        {
            /// The key the rule is registered under and stamped into each finding's `Family` as.
            RuleId: string
            /// The check. May throw: the walk turns a throw into one unguided `RULE-FAULT` finding and
            /// runs the rules after it.
            Run: 'Subject -> Finding<'Id> list
        }

    /// A plain check as a guided one stating no guidance (Phase 489): the same id, the same defects,
    /// each `Finding.ofDefect`.
    let asGuided (check: PackCheck<'Subject, 'Id>) : GuidedCheck<'Subject, 'Id> =
        { RuleId = check.RuleId
          Run = check.Run >> List.map Finding.ofDefect }

    // ---- Phase 521: `Rules` — the one rule registry, over any subject ----
    // `PackCheck` is the rule. `Rules` is a registry of them with the lifecycle every other registry
    // carries (the registration-ordered lattice above) and the one walker, so a rule over a column
    // table, a tree root or any other subject is registered, removed, swapped, narrowed and joined
    // under one gate. `ColumnValidator.Registry` is `Rules<Table, string>`, the fifth registry on the
    // lattice (D117 counted four); a tree family enters as `asCheck w family`.

    /// A registration-ordered registry of rules over one subject type, one rule per `RuleId` —
    /// opaque, so the one-rule-per-id invariant holds by construction: build it with `Rules.empty`,
    /// `Rules.register` or `Rules.ofChecks`, and read it with `Rules.enumerate` / `Rules.tryFind`.
    ///
    /// Since Phase 489 it holds guided rules too (`registerGuided`): a plain check is held as
    /// `asGuided` of it, so one registry and one walk carry both, and `run` is `runFindings` read
    /// back as defects.
    type Rules<'Subject, 'Id> =
        private
            { Checks: GuidedCheck<'Subject, 'Id> list }

    /// The verbs over `Rules` (Phase 521): the registration-ordered lattice — the refusals of
    /// `Validator.register` and its lifecycle, in `RegistrationError` — and the one walker, `run`.
    [<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
    module Rules =

        let private checkId (c: GuidedCheck<'Subject, 'Id>) : string = c.RuleId

        let private holding (checks: GuidedCheck<'Subject, 'Id> list) : Rules<'Subject, 'Id> = { Checks = checks }

        /// A registry holding no rule, so `run` over it finds nothing; the seed `ofChecks` folds from.
        let empty<'Subject, 'Id> : Rules<'Subject, 'Id> = { Checks = [] }

        /// The registered rule ids, in registration order — the order `run` runs them in.
        let enumerate (rules: Rules<'Subject, 'Id>) : string list = Ordered.ids checkId rules.Checks

        /// The rule registered under `id`, if any, with its guidance (Phase 489): a plain check comes
        /// back as `asGuided` of it.
        let tryFindGuided (id: string) (rules: Rules<'Subject, 'Id>) : GuidedCheck<'Subject, 'Id> option =
            rules.Checks |> List.tryFind (fun c -> c.RuleId = id)

        /// The rule registered under `id`, if any, as a plain check: its defects. A guided rule's
        /// guidance is read through `tryFindGuided` or `runFindings`.
        let tryFind (id: string) (rules: Rules<'Subject, 'Id>) : PackCheck<'Subject, 'Id> option =
            tryFindGuided id rules
            |> Option.map (fun g ->
                { RuleId = g.RuleId
                  Run = g.Run >> List.map _.Defect })

        /// Register the guided rule `check` after the rules held (Phase 489) — refused `DuplicateRule`
        /// when its id is registered, naming every id held.
        let registerGuided
            (check: GuidedCheck<'Subject, 'Id>)
            (rules: Rules<'Subject, 'Id>)
            : Result<Rules<'Subject, 'Id>, RegistrationError> =
            Ordered.register checkId check rules.Checks |> Result.map holding

        /// Register `check` after the rules held — refused `DuplicateRule` when its id is registered,
        /// naming every id held.
        let register
            (check: PackCheck<'Subject, 'Id>)
            (rules: Rules<'Subject, 'Id>)
            : Result<Rules<'Subject, 'Id>, RegistrationError> =
            registerGuided (asGuided check) rules

        /// Remove the rule registered under `id`, keeping the others in order — refused `UnknownRule`
        /// when it is not registered. Removing a rule just registered gives back the registry it was
        /// registered into.
        let unregister (id: string) (rules: Rules<'Subject, 'Id>) : Result<Rules<'Subject, 'Id>, RegistrationError> =
            Ordered.unregister checkId id rules.Checks |> Result.map holding

        /// Swap the rule registered under `check.RuleId` for `check`, in the same position — refused
        /// `UnknownRule` when the id is not registered.
        let replace
            (check: PackCheck<'Subject, 'Id>)
            (rules: Rules<'Subject, 'Id>)
            : Result<Rules<'Subject, 'Id>, RegistrationError> =
            Ordered.replace checkId (asGuided check) rules.Checks |> Result.map holding

        /// Swap the rule registered under `check.RuleId` for the guided rule `check`, in the same
        /// position (Phase 489) — refused `UnknownRule` when the id is not registered.
        let replaceGuided
            (check: GuidedCheck<'Subject, 'Id>)
            (rules: Rules<'Subject, 'Id>)
            : Result<Rules<'Subject, 'Id>, RegistrationError> =
            Ordered.replace checkId check rules.Checks |> Result.map holding

        /// The registry narrowed to the rules whose id is in `keep`, in registration order; an id it
        /// does not hold is ignored, so it never widens.
        let restrict (keep: Set<string>) (rules: Rules<'Subject, 'Id>) : Rules<'Subject, 'Id> =
            holding (Ordered.restrict checkId keep rules.Checks)

        /// `a`'s rules then `b`'s — refused `DuplicateRule` naming the first of `b`'s ids, in `b`'s
        /// order, that `a` holds (and `a`'s ids). Associative.
        let union
            (a: Rules<'Subject, 'Id>)
            (b: Rules<'Subject, 'Id>)
            : Result<Rules<'Subject, 'Id>, RegistrationError> =
            Ordered.union checkId a.Checks b.Checks |> Result.map holding

        /// A registry holding `checks` in order, refusing the first repeated id.
        let ofChecks (checks: PackCheck<'Subject, 'Id> list) : Result<Rules<'Subject, 'Id>, RegistrationError> =
            Ordered.ofList checkId (checks |> List.map asGuided) |> Result.map holding

        /// A registry holding the guided rules `checks` in order, refusing the first repeated id
        /// (Phase 489). A plain check joins the list as `asGuided` of it.
        let ofGuided (checks: GuidedCheck<'Subject, 'Id> list) : Result<Rules<'Subject, 'Id>, RegistrationError> =
            Ordered.ofList checkId checks |> Result.map holding

        /// THE WALK, with the guidance (Phase 489): every rule over `subject`, in registration order then
        /// each rule's own order, each finding paired with its rule's id, its defect stamped with it in
        /// `Family` and its guidance as the rule stated it — `None` for a plain check's defect and for a
        /// thrown rule's `RULE-FAULT`, and the rules after a throw still run. `run` is this walk read
        /// back as defects, so the two never disagree over a code, a node, a family or an order.
        let runFindings (rules: Rules<'Subject, 'Id>) (subject: 'Subject) : (string * Finding<'Id>) list =
            walkFindings checkId (fun c -> c.Run subject) rules.Checks
            |> List.map (fun (c, f) -> c.RuleId, f)

        /// THE WALK over a registry: every rule over `subject`, in registration order then each rule's
        /// own order, each finding paired with its rule's id and stamped with it in `Family`; a rule
        /// that throws is one `RULE-FAULT` error and the rules after it still run. Deterministic: one
        /// `(rules, subject)` gives one finding list, so `canonicalCodes` over it is the parity string.
        let run (rules: Rules<'Subject, 'Id>) (subject: 'Subject) : (string * Defect<'Id>) list =
            runFindings rules subject |> List.map (fun (id, f) -> id, f.Defect)

    // ---- Phase 313: the stock structural-integrity families ----
    // Two checks nearly every domain with a grammar or with cross-node references wrote for itself,
    // each with its own refusal shape and, for cycles, its own depth-first search. The containment
    // family reads the grammar through `Ops.isLegalChild` — the definition the grammar engine
    // refuses by — and the reference family finds cycles through `Propagation.sort`, so a defect
    // here and a refusal there are about the same pairs and the same cycles.

    /// The stock code of a `containment` finding.
    [<Literal>]
    let IllegalChildCode = "TREE-ILLEGALCHILD"

    /// A child the containment grammar does not let its parent hold (Phase 313): one
    /// `Severity.Error` defect per pair `Ops.illegalChildren` reports over the whole tree, in its
    /// order, located at the CHILD, coded `TREE-ILLEGALCHILD`, its message naming both kinds and the
    /// children the parent's kind may hold. The family the grammar engine (`Ops.apply` under `Policy.grammar`)
    /// refuses to create — it reports the same pairs over a tree that arrived whole, decoded, merged
    /// or authored outside the engine. `allowedChildren` maps a parent's kind tag to the kind tags it
    /// may hold, `None` meaning any.
    let containment (allowedChildren: string -> string list option) : RuleFamily<'Node, 'Id> =
        { Id = "containment"
          Run =
            fun w root ->
                Ops.illegalChildren allowedChildren w root
                |> List.map (fun (p, c) ->
                    let pk = w.KindTag p
                    let legal = allowedChildren pk |> Option.defaultValue []

                    { Code = IllegalChildCode
                      Severity = Severity.Error
                      Message =
                        "a "
                        + w.KindTag c
                        + " cannot sit under a "
                        + pk
                        + (if List.isEmpty legal then
                               "; a " + pk + " holds no children"
                           else
                               "; a " + pk + " holds: " + String.concat ", " legal)
                      Node = Some(w.Id c)
                      Family = "containment"
                      Related = [ w.Id p ] }) }

    /// One reference defect (Phase 313), over a `RefWitness`. `from` is the referring node,
    /// `declarer` a declaring node, `target` / `declared` the referenced or declared id.
    [<RequireQualifiedAccess>]
    type ReferenceDefect<'Id> =
        /// `from` refers to `target`, which no node of the tree declares.
        | DanglingReference of from: 'Id * target: 'Id
        /// `declarer` declares `declared`, which no node of the tree refers to.
        | UnusedDeclaration of declarer: 'Id * declared: 'Id
        /// `from` refers to `target`, declared by `declarer`, which comes AFTER `from` in sibling
        /// order: below their lowest common ancestor, `declarer`'s branch is a later child than
        /// `from`'s. A reference to an ancestor's or a descendant's declaration is never forward.
        | ForwardReference of from: 'Id * target: 'Id * declarer: 'Id
        /// The nodes of one circular group of the "refers to a declaration of" relation — more than
        /// one node, or a node referring to its own declaration — as `Propagation.sort` reports it.
        | ReferenceCycle of cycle: 'Id list

    /// The stock codes of the reference families.
    [<Literal>]
    let DanglingReferenceCode = "REF-DANGLING"

    /// The code of an `UnusedDeclaration` finding — a `Severity.Warning`, the one reference finding
    /// that is not an error.
    [<Literal>]
    let UnusedDeclarationCode = "REF-UNUSED"

    /// The code of a `ForwardReference` finding — an error, emitted only by the opt-in
    /// `referenceOrder` family, never by `referenceIntegrity`.
    [<Literal>]
    let ForwardReferenceCode = "REF-FORWARD"

    /// The code of a `ReferenceCycle` finding — an error located at the cycle's first node, with
    /// every member of the cycle in `Related`.
    [<Literal>]
    let ReferenceCycleCode = "REF-CYCLE"

    /// The reference graph of a tree: every node in preorder with its key, the declarers of each
    /// declared key, and the set of referenced keys.
    let private referenceIndex (refw: RefWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (w: NodeWitness<'Node, 'Id>) root =
        let nodes = Tree.preorder w root

        let declarers =
            nodes
            |> List.collect (fun n -> refw.DeclsOf n |> List.map (fun d -> idw.ToString d, n))
            |> List.groupBy fst
            |> List.map (fun (k, ns) -> k, ns |> List.map snd)
            |> Map.ofList

        nodes, declarers

    /// The dangling references, unused declarations and reference cycles of a tree (Phase 313), in
    /// that order: dangling references by referring node in preorder and each node's references in
    /// its order; unused declarations by declaring node in preorder; then each cycle
    /// `Propagation.sort` finds over the relation "node `a` refers to an id node `b` declares" (a
    /// dangling reference adds no edge). Linear in nodes plus references; total, cycles as data.
    let referenceDefects
        (refw: RefWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (w: NodeWitness<'Node, 'Id>)
        (root: 'Node)
        : ReferenceDefect<'Id> list =
        let key (i: 'Id) = idw.ToString i
        let nodes, declarers = referenceIndex refw idw w root

        let referenced = nodes |> List.collect refw.RefsOf |> List.map key |> Set.ofList

        let dangling =
            nodes
            |> List.collect (fun n ->
                refw.RefsOf n
                |> List.filter (fun r -> not (declarers.ContainsKey(key r)))
                |> List.map (fun r -> ReferenceDefect.DanglingReference(w.Id n, r)))

        let unused =
            nodes
            |> List.collect (fun n ->
                refw.DeclsOf n
                |> List.filter (fun d -> not (referenced.Contains(key d)))
                |> List.map (fun d -> ReferenceDefect.UnusedDeclaration(w.Id n, d)))

        let idOf = nodes |> List.map (fun n -> key (w.Id n), w.Id n) |> Map.ofList

        let deps =
            nodes
            |> List.map (fun n ->
                key (w.Id n),
                refw.RefsOf n
                |> List.collect (fun r ->
                    match declarers.TryFind(key r) with
                    | Some ds -> ds |> List.map (fun d -> key (w.Id d))
                    | None -> [])
                |> Set.ofList)
            |> Map.ofList

        let cycles =
            (Propagation.sort deps).Cycles
            |> List.map (fun cycle -> ReferenceDefect.ReferenceCycle(cycle |> List.choose idOf.TryFind))

        dangling @ unused @ cycles

    /// The forward references of a tree (Phase 313) — opt-in, because order matters only to a domain
    /// that reads its tree in order (a feature history, a sequential calculation, a document that
    /// defines before it uses). Every resolved reference whose declaring node comes after the
    /// referring node in sibling order, by referring node in preorder; a reference to an ancestor's
    /// or a descendant's declaration is not forward, and a dangling one is `referenceDefects`'.
    let forwardReferences
        (refw: RefWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (w: NodeWitness<'Node, 'Id>)
        (root: 'Node)
        : ReferenceDefect<'Id> list =
        let key (i: 'Id) = idw.ToString i

        // every node with its child-index path from the root, in preorder
        let rec walk acc stack =
            match stack with
            | [] -> List.rev acc
            | (n, path) :: rest ->
                let below = w.Children n |> List.mapi (fun i c -> c, path @ [ i ])
                walk ((n, path) :: acc) (below @ rest)

        let placed = walk [] [ root, [] ]

        let pathOf = placed |> List.map (fun (n, path) -> key (w.Id n), path) |> Map.ofList

        let declarers =
            placed
            |> List.collect (fun (n, _) -> refw.DeclsOf n |> List.map (fun d -> key d, n))
            |> List.groupBy fst
            |> List.map (fun (k, ns) -> k, ns |> List.map snd)
            |> Map.ofList

        // `later a b`: b's branch is a later child than a's below their lowest common ancestor
        let rec later (a: int list) (b: int list) =
            match a, b with
            | x :: xs, y :: ys when x = y -> later xs ys
            | x :: _, y :: _ -> y > x
            | _ -> false // one is a prefix of the other: ancestor or descendant

        placed
        |> List.collect (fun (n, path) ->
            refw.RefsOf n
            |> List.collect (fun r ->
                match declarers.TryFind(key r) with
                | None -> []
                | Some ds ->
                    ds
                    |> List.filter (fun d ->
                        match pathOf.TryFind(key (w.Id d)) with
                        | Some dp -> later path dp
                        | None -> false)
                    |> List.map (fun d -> ReferenceDefect.ForwardReference(w.Id n, r, w.Id d))))

    /// A reference defect as a `Defect`, its ids rendered by `idw`.
    let private referenceDefect (idw: IdWitness<'Id>) (d: ReferenceDefect<'Id>) : Defect<'Id> =
        let q (i: 'Id) = "'" + idw.ToString i + "'"

        match d with
        | ReferenceDefect.DanglingReference(from, target) ->
            Defect.create
                DanglingReferenceCode
                Severity.Error
                ("the reference to " + q target + " resolves to no declaration")
                (Some from)
        | ReferenceDefect.UnusedDeclaration(declarer, declared) ->
            Defect.create
                UnusedDeclarationCode
                Severity.Warning
                (q declared + " is declared and never referenced")
                (Some declarer)
        | ReferenceDefect.ForwardReference(from, target, declarer) ->
            { Defect.create
                  ForwardReferenceCode
                  Severity.Error
                  ("the reference to "
                   + q target
                   + " comes before its declaration at "
                   + q declarer)
                  (Some from) with
                Related = [ declarer ] }
        | ReferenceDefect.ReferenceCycle cycle ->
            { Defect.create
                  ReferenceCycleCode
                  Severity.Error
                  ("reference cycle through " + (cycle |> List.map q |> String.concat ", "))
                  (List.tryHead cycle) with
                Related = cycle }

    /// The reference-integrity family (Phase 313): `referenceDefects` as defects — dangling
    /// references (`REF-DANGLING`, error), unused declarations (`REF-UNUSED`, warning) and reference
    /// cycles (`REF-CYCLE`, error), each located at the referring, declaring or first cycle node.
    let referenceIntegrity (refw: RefWitness<'Node, 'Id>) (idw: IdWitness<'Id>) : RuleFamily<'Node, 'Id> =
        { Id = "referenceIntegrity"
          Run = fun w root -> referenceDefects refw idw w root |> List.map (referenceDefect idw) }

    /// The opt-in ordering family (Phase 313): `forwardReferences` as `REF-FORWARD` errors, located
    /// at the referring node. Register it beside `referenceIntegrity` in a domain that defines
    /// before it uses.
    let referenceOrder (refw: RefWitness<'Node, 'Id>) (idw: IdWitness<'Id>) : RuleFamily<'Node, 'Id> =
        { Id = "referenceOrder"
          Run = fun w root -> forwardReferences refw idw w root |> List.map (referenceDefect idw) }

/// A columnar validation rule over a `Table` (Phase 37) — since Phase 521 the `Validator.PackCheck`
/// it always had the shape of, so a column rule IS a rule of the one framework: it registers in a
/// `Validator.Rules<Table, string>` (`ColumnValidator.Registry`) and sits in a versioned pack as it is.
/// The SAME `Defect` / `Severity` model as every family (one defect vocabulary); the location `'Id`
/// is a `string`: a column name, or `column#row` for a cell-level fault. `RuleId` is the registry key
/// and the provenance stamped on every finding — the stock rules mint it with `ColumnValidator.ruleId`,
/// so two rules share an id exactly when they are one rule; `Run` may throw, and the walker turns a
/// throw into one `RULE-FAULT` error and runs the rules after it. Build one with `ColumnValidator.rule`.
type ColumnRule = Validator.PackCheck<Table, string>

/// The columnar validator surface (Phase 37): a rule family over a `Table` (registration + walker +
/// stock rules), reusing the `Validator` defect/severity model + the `canonicalCodes` byte-parity
/// projection — data-quality as a Core concern, not re-implemented per domain.
module ColumnValidator =

    let private locOf (column: string) (row: int) = column + "#" + string row

    // ---- the stock codes (Phase 521: every code a literal, none spelled inline) ----

    /// The code of a rule naming a column the table does not hold.
    [<Literal>]
    let NoColumnCode = "COL-NOCOL"

    /// The code of an absent row in a `notNull` column.
    [<Literal>]
    let NotNullCode = "COL-NOTNULL"

    /// The code of a column whose type is not the one `ofType` names.
    [<Literal>]
    let OfTypeCode = "COL-OFTYPE"

    /// The code of a value outside an `inRange` rule's bounds.
    [<Literal>]
    let InRangeCode = "COL-INRANGE"

    /// The code of a repeated `unique` key.
    [<Literal>]
    let UniqueCode = "COL-UNIQUE"

    let private noColDefect (column: string) : Defect<string> =
        Defect.create NoColumnCode Severity.Error ("no such column: " + column) (Some column)

    /// The id of a stock column rule (Phase 298): the rule's kind and its parameters through
    /// `Hash.canonicalFields`, so two rules share an id exactly when they are one rule — `unique`
    /// over `["a,b"]` and over `["a"; "b"]` are two ids, and two `inRange` rules over one column with
    /// different bounds are two rules (each used to be `kind:column`, joined on `,`).
    let ruleId (kind: string) (parameters: string list) : string =
        Hash.canonicalFields (kind :: parameters)

    /// Build a rule from an id + body.
    let rule (id: string) (run: Table -> Defect<string> list) : ColumnRule = { RuleId = id; Run = run }

    /// Each cell in `column` must be present (non-null) — a missing column is itself a fault.
    let notNull (column: string) : ColumnRule =
        rule (ruleId "notNull" [ column ]) (fun t ->
            match Table.tryColumn column t with
            | None -> [ noColDefect column ]
            | Some c ->
                // The validity mask IS the answer (Phase 417): an absent row is a `Null` cell.
                let mask = Column.validity c

                [ for i in 0 .. Column.length c - 1 do
                      if not (Validity.isPresent i mask) then
                          Defect.create
                              NotNullCode
                              Severity.Error
                              ("null in non-null column '" + column + "'")
                              (Some(locOf column i)) ])

    /// `column` must be of type `ty`: one `COL-OFTYPE` error, located at the column, when its type is
    /// another; nothing otherwise.
    ///
    /// **A fact about the column, not its cells (Phase 497).** Since Phase 417 a present cell cannot be
    /// of another type than its column, so the rule has one thing to say. It used to walk the cells and
    /// read each one's type back from its text, which is not the column's type: a millisecond column
    /// renders a whole-second instant with no fraction, `Cell.typeOf` reads that text as seconds, and
    /// every whole-second row of a column OF the declared type was reported.
    let ofType (column: string) (ty: ColumnType) : ColumnRule =
        rule (ruleId "ofType" [ column; ColumnType.tag ty ]) (fun t ->
            match Table.tryColumn column t with
            | None -> [ noColDefect column ]
            | Some c when c.Type <> ty ->
                [ Defect.create
                      OfTypeCode
                      Severity.Error
                      ("column '"
                       + column
                       + "' expected "
                       + ColumnType.tag ty
                       + ", got "
                       + ColumnType.tag c.Type)
                      (Some column) ]
            | Some _ -> [])

    /// The stock code of a range rule whose BOUNDS are not numbers (Phase 298).
    [<Literal>]
    let BadRangeCode = "COL-BADRANGE"

    /// The stock code of a cell a range rule cannot read as a number (Phase 298).
    [<Literal>]
    let NotANumberCode = "COL-NAN"

    /// Every present numeric cell in `column` must lie within `[lo, hi]` (inclusive).
    ///
    /// **Total over what it cannot compare (Phase 298).** A NaN bound compares false with every
    /// value, so the rule used to pass every cell: a NaN `lo` or `hi` is now one `COL-BADRANGE` error
    /// located at the column, and no cell is read. A NaN cell, and a `Decimal` cell whose text is not
    /// decimal, are `COL-NAN` errors at the cell — a value the rule cannot place inside or outside
    /// the range is a finding, never a pass. An infinite bound is a number and is honoured.
    let inRange (column: string) (lo: float) (hi: float) : ColumnRule =
        rule (ruleId "inRange" [ column; Cell.token (Float lo); Cell.token (Float hi) ]) (fun t ->
            match Table.tryColumn column t with
            | None -> [ noColDefect column ]
            | Some _ when System.Double.IsNaN lo || System.Double.IsNaN hi ->
                [ Defect.create
                      BadRangeCode
                      Severity.Error
                      ("column '" + column + "' range bounds are not numbers")
                      (Some column) ]
            | Some c ->
                // On the typed storage (Phase 521; `Column.toCells` until then): one monomorphic loop
                // per numeric type, as the aggregate loops are written (D145.5), and nothing for a
                // column whose values are not numbers. `ValueNone` is a present value the rule cannot
                // place as a number.
                let validity = Column.validity c

                let over (read: int -> float voption) : Defect<string> list =
                    [ for i in 0 .. Column.length c - 1 do
                          if Validity.isPresent i validity then
                              match read i with
                              | ValueNone ->
                                  Defect.create
                                      NotANumberCode
                                      Severity.Error
                                      ("column '" + column + "' value at row " + string i + " is not a number")
                                      (Some(locOf column i))
                              | ValueSome x when x < lo || x > hi ->
                                  Defect.create
                                      InRangeCode
                                      Severity.Error
                                      ("column '" + column + "' value out of range at row " + string i)
                                      (Some(locOf column i))
                              | ValueSome _ -> () ]

                match c.Data with
                | Ints(xs, _) -> over (fun i -> ValueSome(float xs[i]))
                | Floats(xs, _) ->
                    over (fun i ->
                        let f = xs[i]
                        if System.Double.IsNaN f then ValueNone else ValueSome f)
                // The bounds are floats, so a decimal is read at the nearest float: a range check is a
                // statement about magnitude, and leaving the case out would pass every decimal column
                // unchecked. Past the float range `tryToFloat` refuses (Phase 299) rather than answering
                // ∞; such a decimal text is out of every finite range, so it is read as the infinity of
                // its sign here, where "out of range" is the whole question. Text that is not decimal is
                // unreadable (Phase 298: a finding, where it used to pass unread).
                | Decimals(xs, _) ->
                    over (fun i ->
                        let s = xs[i]

                        match DecimalText.tryToFloat s, DecimalText.compare s DecimalText.zero with
                        | Some f, _ -> ValueSome f
                        | None, Some c when c < 0 -> ValueSome -infinity
                        | None, Some _ -> ValueSome infinity
                        | None, None -> ValueNone)
                | Bools _
                | Strs _
                | Dates _
                | Timestamps _ -> [])

    /// One composite key as one string (Phase 298): the cells' tokens through `Hash.canonicalFields`,
    /// injective over token lists, so a hash set decides key equality.
    let private keyText (tokens: string list) : string = Hash.canonicalFields tokens

    /// The stock code of a key column whose length is not the table's row count (Phase 298).
    [<Literal>]
    let RaggedCode = "COL-RAGGED"

    /// The composite key formed by `columns` must be unique across rows — each repeat is located.
    /// A key element is `Cell.token` (Phase 315; a private copy until then), so two cells are one
    /// key value exactly when every consumer keying on the token says so: NaN is one value, `-0.0`
    /// is `0`, and two `Decimal` cells holding `1.5` and `1.50` are one value.
    ///
    /// **Linear, and only over a rectangular key (Phase 298).** Each key column is read ONCE into an
    /// array, so a row's key is O(key width) and the rule is linear in rows (it indexed every row
    /// through the column's list, quadratically). A key column whose length is not the table's row
    /// count is one `COL-RAGGED` error naming it, and no key is compared — reading past a short
    /// column used to yield `Null` cells and report rows as duplicates that had no key at all.
    /// **`Null` participates as a value:** two rows whose key cells are `Null` in the same positions
    /// are one key, so a missing key cell repeated is a duplicate (pair the rule with `notNull` to
    /// forbid missing key cells outright).
    let unique (columns: string list) : ColumnRule =
        rule (ruleId "unique" columns) (fun t ->
            let missing = columns |> List.filter (fun c -> (Table.tryColumn c t).IsNone)

            if not (List.isEmpty missing) then
                missing |> List.map noColDefect
            else
                let rc = Table.rowCount t

                let cols = columns |> List.choose (fun c -> Table.tryColumn c t)

                match cols |> List.filter (fun col -> Column.length col <> rc) with
                | _ :: _ as ragged ->
                    ragged
                    |> List.map (fun col ->
                        Defect.create
                            RaggedCode
                            Severity.Error
                            ("key column '"
                             + col.Name
                             + "' has "
                             + string (Column.length col)
                             + " rows where the table has "
                             + string rc)
                            (Some col.Name))
                | [] ->
                    // each key cell read off the typed storage at its row (`Column.cell`, O(1), `Null`
                    // at an absent row), so no column is materialised as a cell list (Phase 521)
                    let keyAt i =
                        cols |> List.map (fun col -> Cell.token (Column.cell i col))

                    let keyName = String.concat "," columns
                    let seen = System.Collections.Generic.HashSet<string>()
                    let found = ResizeArray<Defect<string>>()

                    for i in 0 .. rc - 1 do
                        // one string per key: the tokens through `canonicalFields`, injective over
                        // token lists, so a HashSet decides membership in O(key width)
                        if not (seen.Add(keyText (keyAt i))) then
                            found.Add(
                                Defect.create
                                    UniqueCode
                                    Severity.Error
                                    ("duplicate key (" + keyName + ") at row " + string i)
                                    (Some(locOf keyName i))
                            )

                    List.ofSeq found)

    // ---- the registry (Phase 521): `Rules<Table, string>`, the fifth registry on the lattice ----
    // The column vocabulary over the one framework: each verb IS the `Validator.Rules` verb at the
    // column table, so this registry carries the lifecycle (`unregister` / `replace` / `restrict` /
    // `union`) the other four do — it had `empty` / `enumerate` / `tryFind` / `register` / `ofRules`
    // only, with a walker of its own — and its walk is the one walk.

    /// A registration-ordered registry of column rules, one rule per id: the order `validate` runs
    /// them and reports their findings in.
    type Registry = Validator.Rules<Table, string>

    /// A registry holding no rule, so `validate` over it finds nothing; the seed `ofRules` folds from.
    let empty: Registry = Validator.Rules.empty

    /// The registered rule ids, in registration order.
    let enumerate (reg: Registry) : string list = Validator.Rules.enumerate reg

    /// The rule registered under `id`, if any.
    let tryFind (id: string) (reg: Registry) : ColumnRule option = Validator.Rules.tryFind id reg

    /// Register `r` after the rules already held — REFUSED as `DuplicateRule` when its id is already
    /// registered (Phase 298), as `Validator.register` refuses.
    let register (r: ColumnRule) (reg: Registry) : Result<Registry, RegistrationError> = Validator.Rules.register r reg

    /// Remove the rule registered under `id`, keeping the others in order — refused `UnknownRule`
    /// naming every id held when it is not registered (Phase 521).
    let unregister (id: string) (reg: Registry) : Result<Registry, RegistrationError> =
        Validator.Rules.unregister id reg

    /// Swap the rule registered under `r.RuleId` for `r`, in the same position — refused `UnknownRule`
    /// when the id is not registered (Phase 521).
    let replace (r: ColumnRule) (reg: Registry) : Result<Registry, RegistrationError> = Validator.Rules.replace r reg

    /// The registry narrowed to the rules whose id is in `keep`, in registration order; never wider
    /// than `reg` (Phase 521).
    let restrict (keep: Set<string>) (reg: Registry) : Registry = Validator.Rules.restrict keep reg

    /// `a`'s rules then `b`'s — refused `DuplicateRule` naming the first of `b`'s ids that `a` holds
    /// (Phase 521). Associative.
    let union (a: Registry) (b: Registry) : Result<Registry, RegistrationError> = Validator.Rules.union a b

    /// A registry holding `rules` in order, refusing the first repeated id.
    let ofRules (rules: ColumnRule list) : Result<Registry, RegistrationError> = Validator.Rules.ofChecks rules

    /// Run every registered rule and pair each finding with the id of the rule that produced it
    /// (Phase 298), in rule order then row order; each finding's `Family` is stamped with that id,
    /// and a rule that throws is one `RULE-FAULT` error while the rules after it still run.
    let validateTagged (reg: Registry) (t: Table) : (string * Defect<string>) list = Validator.Rules.run reg t

    /// Run every registered rule over the table, concatenating defects in rule order then row order — a
    /// deterministic, byte-canonical defect list for a given `(registry, table)` (the same table yields
    /// the same defect bytes; `Validator.canonicalCodes` projects the cross-host parity string). The
    /// findings of `validateTagged`, each carrying its rule in `Family`.
    let validate (reg: Registry) (t: Table) : Defect<string> list = validateTagged reg t |> List.map snd
