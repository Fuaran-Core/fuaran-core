namespace Fuaran.Core

/// Defect severity. Shared across domains; the *codes* are domain-side.
/// `RequireQualifiedAccess` so the `Error` case never shadows `Result.Error` in a
/// consumer that `open`s `Fuaran.Core` — write `Severity.Error`.
[<RequireQualifiedAccess>]
type Severity =
    | Error
    | Warning
    | Info

/// A single validation finding. `Code` is the domain's stable defect code (e.g.
/// FUARAN058, the Calc six, a Doc house-style pack rule). `Node` locates it.
type Defect<'Id> =
    { Code: string
      Severity: Severity
      Message: string
      Node: 'Id option }

/// A registered rule family — a named bundle of checks the walker runs over a tree.
/// The framework owns registration + walking; the rule *body* is domain-supplied.
type RuleFamily<'Node, 'Id> =
    { Id: string
      Run: NodeWitness<'Node, 'Id> -> 'Node -> Defect<'Id> list }

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
type PackRule = { Pack: string; RuleId: string }

/// The rule-family framework: registration, the per-node walker scaffolding, defect
/// aggregation, and the cross-host defect-code byte-parity helper. All rule content
/// stays domain-side; the core owns only the scaffolding.
module Validator =

    /// A simple ordered registry of rule families.
    type Registry<'Node, 'Id> =
        { Families: RuleFamily<'Node, 'Id> list }

    let empty<'Node, 'Id> : Registry<'Node, 'Id> = { Families = [] }

    let register (family: RuleFamily<'Node, 'Id>) (reg: Registry<'Node, 'Id>) : Registry<'Node, 'Id> =
        { reg with
            Families = reg.Families @ [ family ] }

    /// Build a rule family from a per-node predicate — the common shape. The walker
    /// visits every node in preorder and collects whatever defects the rule emits.
    let perNode (id: string) (rule: NodeWitness<'Node, 'Id> -> 'Node -> Defect<'Id> list) : RuleFamily<'Node, 'Id> =
        { Id = id
          Run = fun w root -> Tree.preorder w root |> List.collect (rule w) }

    /// Run every registered family over the tree, concatenating defects in family order.
    let runAll (w: NodeWitness<'Node, 'Id>) (reg: Registry<'Node, 'Id>) (root: 'Node) : Defect<'Id> list =
        reg.Families |> List.collect (fun f -> f.Run w root)

    let hasErrors (defects: Defect<'Id> list) : bool =
        defects |> List.exists (fun d -> d.Severity = Severity.Error)

    /// Per-severity counts over a defect list (Phase 25) — the everyday summary domains otherwise
    /// re-derive by hand. Pure and total.
    type Summary =
        { Errors: int
          Warnings: int
          Infos: int }

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
        defects |> List.map (fun d -> d.Code) |> List.sort |> Hash.canonicalFields

    // ---- Phase 315: the versioned rule pack ----
    // The container the `PackRule` convention above was always about, which every domain that
    // ships packs wrote for itself: a named, VERSIONED set of rules whose findings each carry the
    // rule that produced them and a `pack@version` citation, so a report can cite the rule and a
    // consumer can pin the pack version it audits against. Generic over the SUBJECT a rule reads (a
    // tree root, a document, a model, a voicing sequence) — a tree family is the instance
    // `fun root -> family.Run w root` — and over the defect's location `'Id`.

    /// One rule of a pack: its id within the pack (the citation key) and its check.
    type PackCheck<'Subject, 'Id> =
        { RuleId: string
          Run: 'Subject -> Defect<'Id> list }

    /// A versioned rule pack. `Version` is the pack's own, cited on every finding.
    type Pack<'Subject, 'Id> =
        { Name: string
          Version: string
          Rules: PackCheck<'Subject, 'Id> list }

    /// One finding of a pack run: the defect, the `PackRule` that produced it (stamped by `runPack`,
    /// so a rule cannot mis-cite itself), and the citation `<pack>@<version>/<ruleId>`.
    type PackFinding<'Id> =
        { Rule: PackRule
          Citation: string
          Defect: Defect<'Id> }

    /// The citation of a pack's rule: `<pack>@<version>/<ruleId>` — the `PackRule` family id
    /// convention (`pack + "/" + ruleId`) with the version the finding was produced under.
    let citation (pack: Pack<'Subject, 'Id>) (ruleId: string) : string =
        pack.Name + "@" + pack.Version + "/" + ruleId

    /// Run every rule of `pack` over `subject`, in rule order then each rule's own defect order,
    /// stamping each defect with its `PackRule` and citation.
    let runPack (pack: Pack<'Subject, 'Id>) (subject: 'Subject) : PackFinding<'Id> list =
        pack.Rules
        |> List.collect (fun rule ->
            let stamp: PackRule =
                { Pack = pack.Name
                  RuleId = rule.RuleId }

            let cited = citation pack rule.RuleId

            rule.Run subject
            |> List.map (fun d ->
                { Rule = stamp
                  Citation = cited
                  Defect = d }))

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
    /// children the parent's kind may hold. The family the grammar engine (`Ops.applyGrammar`)
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
                      Node = Some(w.Id c) }) }

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

    [<Literal>]
    let UnusedDeclarationCode = "REF-UNUSED"

    [<Literal>]
    let ForwardReferenceCode = "REF-FORWARD"

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
            { Code = DanglingReferenceCode
              Severity = Severity.Error
              Message = "the reference to " + q target + " resolves to no declaration"
              Node = Some from }
        | ReferenceDefect.UnusedDeclaration(declarer, declared) ->
            { Code = UnusedDeclarationCode
              Severity = Severity.Warning
              Message = q declared + " is declared and never referenced"
              Node = Some declarer }
        | ReferenceDefect.ForwardReference(from, target, declarer) ->
            { Code = ForwardReferenceCode
              Severity = Severity.Error
              Message =
                "the reference to "
                + q target
                + " comes before its declaration at "
                + q declarer
              Node = Some from }
        | ReferenceDefect.ReferenceCycle cycle ->
            { Code = ReferenceCycleCode
              Severity = Severity.Error
              Message = "reference cycle through " + (cycle |> List.map q |> String.concat ", ")
              Node = List.tryHead cycle }

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

/// A columnar validation rule over a `Table` (Phase 37) — the columnar analogue of `RuleFamily`,
/// reusing the SAME `Defect` / `Severity` model (one defect vocabulary, GP-consistent). The location
/// `'Id` is a `string`: a column name, or `column#row` for a cell-level fault. Rules are functions over
/// the data strand — no base type (GP1), mirroring the witness discipline.
type ColumnRule =
    { Id: string
      Run: Table -> Defect<string> list }

/// The columnar validator surface (Phase 37): a rule family over a `Table` (registration + walker +
/// stock rules), reusing the `Validator` defect/severity model + the `canonicalCodes` byte-parity
/// projection — data-quality as a Core concern, not re-implemented per domain.
module ColumnValidator =

    let private locOf (column: string) (row: int) = column + "#" + string row

    let private noColDefect (column: string) : Defect<string> =
        { Code = "COL-NOCOL"
          Severity = Severity.Error
          Message = "no such column: " + column
          Node = Some column }

    /// Build a rule from an id + body.
    let rule (id: string) (run: Table -> Defect<string> list) : ColumnRule = { Id = id; Run = run }

    /// Each cell in `column` must be present (non-null) — a missing column is itself a fault.
    let notNull (column: string) : ColumnRule =
        rule ("notNull:" + column) (fun t ->
            match Table.tryColumn column t with
            | None -> [ noColDefect column ]
            | Some c ->
                c.Cells
                |> List.mapi (fun i cell -> i, cell)
                |> List.filter (fun (_, cell) -> Cell.isNull cell)
                |> List.map (fun (i, _) ->
                    { Code = "COL-NOTNULL"
                      Severity = Severity.Error
                      Message = "null in non-null column '" + column + "'"
                      Node = Some(locOf column i) }))

    /// Every PRESENT cell in `column` must carry type `ty` (a `Null` is type-agnostic — use `notNull`).
    let ofType (column: string) (ty: ColumnType) : ColumnRule =
        rule ("ofType:" + column) (fun t ->
            match Table.tryColumn column t with
            | None -> [ noColDefect column ]
            | Some c ->
                c.Cells
                |> List.mapi (fun i cell -> i, cell)
                |> List.choose (fun (i, cell) ->
                    match Cell.typeOf cell with
                    | Some t' when t' <> ty ->
                        Some
                            { Code = "COL-OFTYPE"
                              Severity = Severity.Error
                              Message =
                                "column '"
                                + column
                                + "' expected "
                                + ColumnType.tag ty
                                + ", got "
                                + ColumnType.tag t'
                              Node = Some(locOf column i) }
                    | _ -> None))

    /// Every present numeric cell in `column` must lie within `[lo, hi]` (inclusive).
    let inRange (column: string) (lo: float) (hi: float) : ColumnRule =
        rule ("inRange:" + column) (fun t ->
            match Table.tryColumn column t with
            | None -> [ noColDefect column ]
            | Some c ->
                c.Cells
                |> List.mapi (fun i cell -> i, cell)
                |> List.choose (fun (i, cell) ->
                    let v =
                        match cell with
                        | Int n -> Some(float n)
                        | Float f -> Some f
                        // The bounds are floats, so a decimal is read at the nearest float: a range
                        // check is a statement about magnitude, and leaving the case out would pass
                        // every decimal column unchecked. Past the float range `tryToFloat` refuses
                        // (Phase 299) rather than answering ∞; such a decimal text is out of every
                        // finite range, so it is read as the infinity of its sign here, where "out
                        // of range" is the whole question. Text that is not decimal stays unread.
                        | Decimal s ->
                            match DecimalText.tryToFloat s, DecimalText.compare s DecimalText.zero with
                            | Some f, _ -> Some f
                            | None, Some c when c < 0 -> Some -infinity
                            | None, Some _ -> Some infinity
                            | None, None -> None
                        | _ -> None

                    match v with
                    | Some x when x < lo || x > hi ->
                        Some
                            { Code = "COL-INRANGE"
                              Severity = Severity.Error
                              Message = "column '" + column + "' value out of range at row " + string i
                              Node = Some(locOf column i) }
                    | _ -> None))

    /// The composite key formed by `columns` must be unique across rows — each repeat is located.
    /// A key element is `Cell.token` (Phase 315; a private copy until then), so two cells are one
    /// key value exactly when every consumer keying on the token says so: NaN is one value, `-0.0`
    /// is `0`, and two `Decimal` cells holding `1.5` and `1.50` are one value.
    let unique (columns: string list) : ColumnRule =
        rule ("unique:" + String.concat "," columns) (fun t ->
            let missing = columns |> List.filter (fun c -> (Table.tryColumn c t).IsNone)

            if not (List.isEmpty missing) then
                missing |> List.map noColDefect
            else
                let rc = Table.rowCount t
                let cols = columns |> List.map (fun c -> Table.tryColumn c t |> Option.get)

                let keyAt i =
                    cols |> List.map (fun c -> Cell.token (Column.cell i c))

                let keyName = String.concat "," columns

                let rec go i (seen: Set<string list>) acc =
                    if i >= rc then
                        List.rev acc
                    else
                        let k = keyAt i

                        if Set.contains k seen then
                            go
                                (i + 1)
                                seen
                                ({ Code = "COL-UNIQUE"
                                   Severity = Severity.Error
                                   Message = "duplicate key (" + keyName + ") at row " + string i
                                   Node = Some(locOf keyName i) }
                                 :: acc)
                        else
                            go (i + 1) (Set.add k seen) acc

                go 0 Set.empty [])

    /// An ordered registry of columnar rules.
    type Registry = { Rules: ColumnRule list }

    let empty: Registry = { Rules = [] }

    let register (r: ColumnRule) (reg: Registry) : Registry = { reg with Rules = reg.Rules @ [ r ] }

    /// Run every registered rule over the table, concatenating defects in rule order then row order — a
    /// deterministic, byte-canonical defect list for a given `(registry, table)` (the same table yields
    /// the same defect bytes; `Validator.canonicalCodes` projects the cross-host parity string).
    let validate (reg: Registry) (t: Table) : Defect<string> list =
        reg.Rules |> List.collect (fun r -> r.Run t)
