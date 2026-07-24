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
    /// byte-parity surface (two conformant hosts must produce the same set). The codes are joined
    /// with the `U+0001` control byte (Phase 25), which no defect code contains, so the projection
    /// cannot be aliased by a code that happens to hold the separator (the prior `,` join could).
    /// Parity-string-breaking vs the old `,`-joined form — defect codes are stable identifiers, so
    /// in practice unchanged, but a host that persisted the old projection should re-derive it.
    let canonicalCodes (defects: Defect<'Id> list) : string =
        defects |> List.map (fun d -> d.Code) |> List.sort |> String.concat Hash.foldSep

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

    /// A canonical, host-deterministic token for a cell — the uniqueness key element. Type-tagged so
    /// distinct cell types never collide.
    let private cellToken (c: Cell) : string =
        match c with
        | Int i -> "i:" + string i
        | Float f ->
            "f:"
            + (if System.Double.IsNaN f then
                   "NaN"
               elif System.Double.IsPositiveInfinity f then
                   "Inf"
               elif System.Double.IsNegativeInfinity f then
                   "-Inf"
               // Phase 55/54: the finite token uses the single cross-host canonical float layout
               // (`Wire.Canon.canonicalFloat` — `ToString("R")` on .NET, JS re-lay under Fable). The
               // prior `string f` was locale-dependent + lossy; a raw `ToString("R")` is not
               // Fable-supported (the Phase-54 gate caught it), so route through Canon.
               else
                   Canon.canonicalFloat f)
        | Bool b -> "b:" + (if b then "1" else "0")
        | Str s -> "s:" + s
        | Date s -> "d:" + s
        | Timestamp s -> "t:" + s
        | Null -> "n:"

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
    let unique (columns: string list) : ColumnRule =
        rule ("unique:" + String.concat "," columns) (fun t ->
            let missing = columns |> List.filter (fun c -> (Table.tryColumn c t).IsNone)

            if not (List.isEmpty missing) then
                missing |> List.map noColDefect
            else
                let rc = Table.rowCount t
                let cols = columns |> List.map (fun c -> Table.tryColumn c t |> Option.get)

                let keyAt i =
                    cols |> List.map (fun c -> cellToken (Column.cell i c))

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
