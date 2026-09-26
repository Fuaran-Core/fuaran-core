module Fuaran.Core.Tests.PropagationCompositionTests

// Phase 250 — propagation composes with incremental. A downstream spreadsheet-shaped consumer's
// measurement (Phase 250) built a sheet over `Column.Ops`, `DataFrame.Incremental` and
// `Propagation` and found five places where the three strands met only through glue it kept by
// hand. Each section below is one of those places, closed inside the contract, and the sheet at the
// bottom composes all of them and is certified by `Conformance.propagationEvaluatorLawsWith`:
//
//   1. `UpdateNode` — a redefinition is one in-place op, so it dirties one node.
//   2. `Propagation.changedForOp` — the post-edit change set, with a removed node's dependents in it
//      and the removed id out of it.
//   3. Column-granular reads — `partDependencyMap` + `dirtyFromChangedParts`, with the columnar
//      adapter `ColumnOps.changedColumns` supplied as the changed-parts function.
//   4. `ColumnOps.deltaOf` — a one-cell edit reaches `Incremental` as one row.
//   5. `Propagation.evalFromWith` — a node's prior value is an argument, so a table node's
//      incremental state is carried by the driver rather than beside it.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

// =============================================================================================
//  1. UpdateNode
// =============================================================================================

let private tree () : RNode =
    RNode.node
        "root"
        "doc"
        [ RNode.node "a" "section" [ RNode.node "a1" "para" []; RNode.node "a2" "para" [] ]
          RNode.node "b" "section" [ RNode.node "b1" "para" [] ] ]

let private kindOf (id: string) (t: RNode) =
    Tree.tryFind nodew idw id t |> Option.map (fun n -> n.Kind)

let private childIds (id: string) (t: RNode) =
    Tree.tryFind nodew idw id t
    |> Option.map (fun n -> n.Children |> List.map (fun c -> c.Id))

let private updateNodeTests =
    testList
        "UpdateNode"
        [ testCase "rewrites the node's own content in place: same id, same position, same children"
          <| fun () ->
              let t = tree ()

              match Ops.apply nodew idw (UpdateNode(RNode.node "a" "aside" [])) t with
              | Ok t' ->
                  Expect.equal (kindOf "a" t') (Some "aside") "the content is the payload's"
                  Expect.equal (childIds "a" t') (Some [ "a1"; "a2" ]) "the children are the ones it had"
                  Expect.equal (childIds "root" t') (childIds "root" t) "its position among its siblings is unchanged"
                  Expect.equal (Tree.ids nodew t') (Tree.ids nodew t) "the id set, in preorder, is unchanged"
              | Error e -> failtestf "refused: %A" e

          testCase "the payload's children are not read"
          <| fun () ->
              let t = tree ()
              let payload = RNode.node "a" "aside" [ RNode.node "zz" "para" [] ]

              match Ops.apply nodew idw (UpdateNode payload) t with
              | Ok t' ->
                  Expect.equal (childIds "a" t') (Some [ "a1"; "a2" ]) "a payload child is not grafted"
                  Expect.isNone (Tree.tryFind nodew idw "zz" t') "nor does its id enter the tree"
              | Error e -> failtestf "refused: %A" e

          testCase "the root can be rewritten; an absent id is UnknownNode enumerating the tree"
          <| fun () ->
              let t = tree ()

              match Ops.apply nodew idw (UpdateNode(RNode.node "root" "article" [])) t with
              | Ok t' -> Expect.equal (kindOf "root" t') (Some "article") "the root's content moved"
              | Error e -> failtestf "refused: %A" e

              match Ops.apply nodew idw (UpdateNode(RNode.node "nope" "para" [])) t with
              | Error(UnknownNode("nope", addressable)) ->
                  Expect.equal addressable (Tree.ids nodew t) "the refusal enumerates the addressable ids"
              | other -> failtestf "expected UnknownNode, got %A" other

          testCase "applyContained refuses a rewrite that leaves children under a non-container, naming the NEW kind"
          <| fun () ->
              let t = tree ()
              let canHold (n: RNode) = n.Kind <> "para"

              match Ops.applyContained canHold nodew idw (UpdateNode(RNode.node "a" "para" [])) t with
              | Error(NotAContainer("a", "para")) -> ()
              | other -> failtestf "expected NotAContainer(a, para), got %A" other

              // A leaf may become a non-container: it holds no children to strand.
              match Ops.applyContained canHold nodew idw (UpdateNode(RNode.node "a1" "para" [])) t with
              | Ok _ -> ()
              | Error e -> failtestf "a childless rewrite was refused: %A" e

              // canApply agrees with apply, clause for clause.
              Expect.equal
                  (Ops.canApplyContained canHold nodew idw (UpdateNode(RNode.node "a" "para" [])) t)
                  (Error(NotAContainer("a", "para")))
                  "the dry run returns the refusal apply would"

          testCase "invert restores the prior content and keeps the children the tree holds when the undo runs"
          <| fun () ->
              let t = tree ()
              let op = UpdateNode(RNode.node "a" "aside" [])

              match Ops.invert nodew idw op t, Ops.apply nodew idw op t with
              | Ok inv, Ok t' ->
                  Expect.equal (Ops.apply nodew idw inv t') (Ok t) "apply (invert op pre) (apply op pre) = pre"

                  match inv with
                  | UpdateNode n -> Expect.equal n.Kind "section" "the inverse carries the pre-state content"
                  | other -> failtestf "the inverse of an update is an update, got %A" other
              | a, b -> failtestf "invert %A, apply %A" a b

          testCase "footprint: a read and a content-write of the target, and an unknown-parent write"
          <| fun () ->
              let fp = Ops.footprint nodew idw [ UpdateNode(RNode.node "a1" "heading" []) ]
              Expect.equal fp.Reads (Set.singleton "a1") "reads the target"
              Expect.equal fp.ContentWrites (Set.singleton "a1") "writes the target's content"
              Expect.equal fp.UnknownParentWrites (Set.singleton "a1") "rewrites an entry of a parent it cannot name"
              Expect.isEmpty fp.StructureWrites "names no parent's child-list"

          testCase
              "the unknown-parent write is REQUIRED: an update and a concurrent remove of an ancestor do not commute"
          <| fun () ->
              let t = tree ()
              let upd = UpdateNode(RNode.node "a1" "heading" [])
              let rem = RemoveNode "a"
              let fpU = Ops.footprint nodew idw [ upd ]
              let fpR = Ops.footprint nodew idw [ rem ]

              // Both apply at t, and in one order the second is refused.
              let updThenRem = Ops.apply nodew idw upd t |> Result.bind (Ops.apply nodew idw rem)
              let remThenUpd = Ops.apply nodew idw rem t |> Result.bind (Ops.apply nodew idw upd)
              Expect.isOk updThenRem "update then remove lands"
              Expect.isError remThenUpd "remove then update is refused — the pair does not commute"
              Expect.isFalse (Ops.independent fpU fpR) "so the footprints must not call them independent"

              // The falsifier: erase the unknown-parent write and the pair reads as independent.
              let erased =
                  { fpU with
                      UnknownParentWrites = Set.empty }

              Expect.isTrue (Ops.independent erased fpR) "without it, the fatal pair would be freed"

          testCase "touchedBy is the node alone, so a redefinition with no readers dirties one node"
          <| fun () ->
              let t = tree ()
              let readsOf (_: RNode) : string seq = Seq.empty
              let redefined = RNode.node "b1" "heading" []
              let viaUpdate = Propagation.dirtyFromOp nodew idw t readsOf (UpdateNode redefined)

              let viaRemoveInsert =
                  Propagation.dirtyFromOp nodew idw t readsOf (Batch [ RemoveNode "b1"; InsertChild("b", redefined) ])

              Expect.equal viaUpdate (Set.singleton "b1") "one node"
              Expect.equal viaRemoveInsert (Set.ofList [ "b"; "b1" ]) "the pre-250 spelling also dirtied the parent" ]

// =============================================================================================
//  2. changedForOp
// =============================================================================================

/// A flat formula tree: `root[x, y, z]`, where `y` reads `x` and `z` reads `y`. The reads live in
/// the node's `Value` as a comma list, so an `UpdateNode` can move them.
let private formulas () =
    RNode.node
        "root"
        "sheet"
        [ RNode.leaf "x" "input" ""
          RNode.leaf "y" "formula" "x"
          RNode.leaf "z" "formula" "y" ]

let private formulaReads (n: RNode) : string seq =
    n.Value.Split([| ',' |], System.StringSplitOptions.RemoveEmptyEntries)
    |> Seq.ofArray

let private changedForOpTests =
    testList
        "changedForOp"
        [ testCase "a removed node's dependents are in the change set and the removed id is not"
          <| fun () ->
              let pre = formulas ()
              let op = RemoveNode "y"

              let post =
                  Ops.apply nodew idw op pre |> Result.defaultWith (fun e -> failwithf "%A" e)

              let changed = Propagation.changedForOp nodew idw formulaReads pre post op
              Expect.isTrue (Set.contains "z" changed) "z read y, so it moved"
              Expect.isFalse (Set.contains "y" changed) "y is gone from the graph evalFrom walks"

              // evalFrom over the post-edit graph accepts it: no EvalUnknownChange.
              let deps = Propagation.dependencyMap nodew idw formulaReads post

              let ev (resolve: string -> int option) (id: string) =
                  match id with
                  | "z" -> Ok(resolve "y" |> Option.defaultValue -1)
                  | _ -> Ok 1

              let preDeps = Propagation.dependencyMap nodew idw formulaReads pre

              let prior =
                  Propagation.eval ev preDeps
                  |> Result.map (fun o -> o.Values)
                  |> Result.defaultValue Map.empty

              let prior = Map.remove "y" prior

              match Propagation.evalFrom ev prior changed deps, Propagation.eval ev deps with
              | Ok inc, Ok full -> Expect.equal inc.Values full.Values "the refresh agrees with a full evaluation"
              | a, b -> failtestf "evalFrom %A, eval %A" a b

          testCase "the two obvious spellings each fail, which is why the function exists"
          <| fun () ->
              let pre = formulas ()
              let op = RemoveNode "y"

              let post =
                  Ops.apply nodew idw op pre |> Result.defaultWith (fun e -> failwithf "%A" e)

              let deps = Propagation.dependencyMap nodew idw formulaReads post

              let ev (resolve: string -> int option) (id: string) =
                  match id with
                  | "z" -> Ok(resolve "y" |> Option.defaultValue -1)
                  | _ -> Ok 1

              let preDeps = Propagation.dependencyMap nodew idw formulaReads pre

              let prior =
                  Propagation.eval ev preDeps
                  |> Result.map (fun o -> o.Values)
                  |> Result.defaultValue Map.empty

              // (a) the pre-edit dirty set, handed over whole, names the removed id and is refused.
              let whole = Propagation.dirtyFromOp nodew idw pre formulaReads op

              match Propagation.evalFrom ev prior whole deps with
              | Error(Propagation.EvalUnknownChange [ "y" ]) -> ()
              | other -> failtestf "expected EvalUnknownChange [y], got %A" other

              // (b) touchedBy narrowed to the survivors is accepted and leaves z stale.
              let survivors = Tree.ids nodew post |> Set.ofList
              let narrowed = Set.intersect (Propagation.touchedBy nodew idw pre op) survivors

              match Propagation.evalFrom ev prior narrowed deps, Propagation.eval ev deps with
              | Ok inc, Ok full -> Expect.notEqual inc.Values full.Values "z kept its stale value"
              | a, b -> failtestf "evalFrom %A, eval %A" a b

          testCase "an UpdateNode that moves a node's reads changes that node and its readers only"
          <| fun () ->
              let pre = formulas ()
              let op = UpdateNode(RNode.leaf "z" "formula" "x")

              let post =
                  Ops.apply nodew idw op pre |> Result.defaultWith (fun e -> failwithf "%A" e)

              let changed = Propagation.changedForOp nodew idw formulaReads pre post op
              Expect.equal changed (Set.singleton "z") "one node" ]

// =============================================================================================
//  3. column-granular reads
// =============================================================================================

let private partTests =
    let parts (xs: string list) = Some(Set.ofList xs)

    // orders is read by `lines` for {qty, price}, by `ids` for {id}, and whole by `raw`; `total`
    // reads `lines`.
    let partDeps: Map<string, Map<string, Set<string> option>> =
        Map.ofList
            [ "orders", Map.empty
              "lines", Map.ofList [ "orders", parts [ "qty"; "price" ] ]
              "ids", Map.ofList [ "orders", parts [ "id" ] ]
              "raw", Map.ofList [ "orders", None ]
              "total", Map.ofList [ "lines", None ] ]

    testList
        "column-granular reads"
        [ testCase
              "a price edit dirties the readers of price and everything downstream of them, and not the reader of id"
          <| fun () ->
              let op = SetCell("price", 0, Float 2.0)

              let dirty =
                  Propagation.dirtyFromChangedParts
                      partDeps
                      (fun _ -> ColumnOps.changedColumns op)
                      (Set.singleton "orders")

              Expect.equal dirty (Set.ofList [ "orders"; "lines"; "total"; "raw" ]) "ids reads only the id column"

          testCase "only the first hop narrows: a reader of a recomputed node is always dirty"
          <| fun () ->
              let dirty =
                  Propagation.dirtyFromChangedParts
                      partDeps
                      (fun _ -> Some(Set.singleton "qty"))
                      (Set.singleton "orders")

              Expect.isTrue
                  (Set.contains "total" dirty)
                  "which parts of `lines` moved is not known until it is recomputed"

          testCase "an op whose moved columns are unknown dirties exactly what dirtyFromChangedIds does"
          <| fun () ->
              let op = AppendRows [ [ "id", Int 9 ] ]
              Expect.isNone (ColumnOps.changedColumns op) "an append moves every column"

              let byParts =
                  Propagation.dirtyFromChangedParts
                      partDeps
                      (fun _ -> ColumnOps.changedColumns op)
                      (Set.singleton "orders")

              let byNodes =
                  Propagation.dirtyFromChangedIds (Propagation.nodeDependencies partDeps) (Set.singleton "orders")

              Expect.equal byParts byNodes "the part map degrades to the node map"

          testCase "partDependencyMap merges two reads of one node, and a whole read absorbs a narrowed one"
          <| fun () ->
              let t =
                  RNode.node "root" "sheet" [ RNode.leaf "s" "src" ""; RNode.leaf "f" "f" ""; RNode.leaf "g" "f" "" ]

              let read (id: string) (ps: Set<string> option) : Propagation.PartRead<string> =
                  { Propagation.Read = id
                    Propagation.Parts = ps }

              let readsOf (n: RNode) : Propagation.PartRead<string> seq =
                  match n.Id with
                  | "f" -> Seq.ofList [ read "s" (parts [ "a" ]); read "s" (parts [ "b" ]) ]
                  | "g" -> Seq.ofList [ read "s" (parts [ "a" ]); read "s" None ]
                  | _ -> Seq.empty

              let m = Propagation.partDependencyMap nodew idw readsOf t
              Expect.equal (Map.find "s" (Map.find "f" m)) (parts [ "a"; "b" ]) "parts unite"
              Expect.equal (Map.find "s" (Map.find "g" m)) None "a whole-value read absorbs"
              Expect.equal (Map.find "root" (Propagation.nodeDependencies m)) Set.empty "every node appears" ]

// =============================================================================================
//  4. ColumnOps.deltaOf
// =============================================================================================

let private orders (n: int) : Table =
    let rows = [ 0 .. n - 1 ]

    { Schema = [ "id", IntType; "qty", IntType; "price", FloatType ]
      Columns =
        [ Column.create "id" IntType (rows |> List.map (fun i -> Int(i + 1)))
          Column.create "qty" IntType (rows |> List.map (fun i -> Int(1 + i % 7)))
          Column.create "price" FloatType (rows |> List.map (fun i -> Float(0.25 * float (1 + i % 40)))) ] }

let private rid = RowIdentity.byColumn "id"

let private linesPipeline =
    [ Derive("amount", Binary(Mul, Cast(FloatType, Col "qty"), Col "price")) ]

let private deltaTests =
    let rowsOf (d: TableDelta) =
        match d with
        | FullRefresh -> None
        | RowSet r -> Some r.Rows

    testList
        "ColumnOps.deltaOf"
        [ testCase "a cell edit off the key is its one row, RowChanged"
          <| fun () ->
              let d = ColumnOps.deltaOf rid (orders 5) (SetCell("price", 2, Float 9.0))
              Expect.equal (rowsOf d) (Some [ ByKey "i:3", RowChanged ]) "row 2 carries key 3"

          testCase "a cell edit that writes the value already there is the empty delta"
          <| fun () ->
              let t = orders 5
              let d = ColumnOps.deltaOf rid t (SetCell("qty", 0, Int 1))
              Expect.equal d (Delta.empty rid.Scheme) "nothing moved"

          testCase "a key edit is the old key removed and the new key added"
          <| fun () ->
              let d = ColumnOps.deltaOf rid (orders 5) (SetCell("id", 0, Int 99))
              Expect.equal (rowsOf d) (Some [ ByKey "i:1", RowRemoved; ByKey "i:99", RowAdded ]) "identity moved"

          testCase "a column edit names every row whose cell moved; an append names every new row"
          <| fun () ->
              let t = orders 4
              let col = Column.create "qty" IntType [ Int 1; Int 5; Int 3; Int 4 ]
              let d = ColumnOps.deltaOf rid t (SetColumn col)
              Expect.equal (rowsOf d) (Some [ ByKey "i:2", RowChanged ]) "only row 1's qty moved"

              let d2 = ColumnOps.deltaOf rid t (AppendRows [ [ "id", Int 10 ]; [ "id", Int 11 ] ])
              Expect.equal (rowsOf d2) (Some [ ByKey "i:10", RowAdded; ByKey "i:11", RowAdded ]) "two new keys"

          testCase "FullRefresh wherever identity or the op cannot say more"
          <| fun () ->
              let t = orders 4
              Expect.equal (ColumnOps.deltaOf rid t (RemoveColumn "qty")) FullRefresh "a schema change"

              Expect.equal
                  (ColumnOps.deltaOf rid t (ApplyTransform linesPipeline))
                  FullRefresh
                  "a whole-table transform"

              Expect.equal
                  (ColumnOps.deltaOf rid t (SetCell("price", 99, Float 1.0)))
                  FullRefresh
                  "an op that does not apply"

              Expect.equal
                  (ColumnOps.deltaOf rid t (AppendRows [ [ "id", Int 1 ] ]))
                  FullRefresh
                  "an append reusing a key"

              Expect.equal
                  (ColumnOps.deltaOf rid t (AppendRows [ [ "qty", Int 1 ] ]))
                  FullRefresh
                  "an append with no key"

          testCase "the delta is true: refreshing with it equals evaluating the edited table"
          <| fun () ->
              let t = orders 50

              let ops =
                  [ SetCell("price", 7, Float 3.5)
                    SetCell("id", 3, Int 1000)
                    SetColumn(Column.create "qty" IntType [ for i in 0..49 -> Int(i % 3) ])
                    AppendRows [ [ "id", Int 500; "qty", Int 2; "price", Float 1.0 ] ] ]

              for op in ops do
                  let t' = ColumnOps.apply op t |> Result.defaultWith (fun e -> failwithf "%A" e)

                  let s0 =
                      Incremental.prime DataFrame.noResolve Map.empty rid linesPipeline t
                      |> Result.defaultWith (fun e -> failwithf "%A" e)

                  let d = ColumnOps.deltaOf rid t op

                  let s1 =
                      Incremental.refresh DataFrame.noResolve Map.empty rid linesPipeline s0 d t'
                      |> Result.defaultWith (fun e -> failwithf "%A" e)

                  Expect.equal
                      (Incremental.result s1)
                      (DataFrame.evalPipeline linesPipeline t'
                       |> Result.defaultWith (fun e -> failwithf "%A" e))
                      (sprintf "%A" op)

          testCase "a one-cell edit reaches Incremental as ONE row, where the column invalidation reaches every row"
          <| fun () ->
              let n = 1000
              let t = orders n
              let op = SetCell("price", 500, Float 7.25)
              let t' = ColumnOps.apply op t |> Result.defaultWith (fun e -> failwithf "%A" e)

              let s0 =
                  Incremental.prime DataFrame.noResolve Map.empty rid linesPipeline t
                  |> Result.defaultWith (fun e -> failwithf "%A" e)

              let refreshWith d =
                  Incremental.refresh DataFrame.noResolve Map.empty rid linesPipeline s0 d t'
                  |> Result.defaultWith (fun e -> failwithf "%A" e)
                  |> Incremental.footprint
                  |> Incremental.rowsEvaluated

              Expect.equal (refreshWith (ColumnOps.deltaOf rid t op)) 1 "one row evaluated"

              let coarse = Delta.ofChange rid.Scheme (ColumnOps.changeOf op)
              Expect.isGreaterThanOrEqual (refreshWith coarse) n "the column invalidation re-evaluates every row" ]

// =============================================================================================
//  5. the sheet: evalFromWith over a tree edited by UpdateNode, sources edited by ColumnOps
// =============================================================================================

/// A table node's value: the result a node MEANS, plus the incremental state that let it be
/// computed cheaply and the source version that state is in step with. Equality is over the result
/// alone — the state is a cache, and the theorem's equality is the value type's.
[<CustomEquality; NoComparison>]
type TableSnap =
    { Result: Table
      State: IncrementalEval option
      BuiltAt: int }

    override this.Equals(o) =
        match o with
        | :? TableSnap as t -> this.Result = t.Result
        | _ -> false

    override this.GetHashCode() = hash this.Result

/// A source node's value: its table, the version it is at, and the delta from the version before.
type SourceSnap =
    { Table: Table
      Version: int
      Delta: TableDelta }

type SheetValue =
    | CellV of Cell
    | SourceV of SourceSnap
    | TableV of TableSnap

type SheetDef =
    | Root
    | Source
    | TableFormula of source: string * pipeline: Transform list * columns: string list
    | CellSum of table: string * column: string

type SheetNode =
    { Id: string
      Def: SheetDef
      Children: SheetNode list }

type Sheet =
    { Tree: SheetNode
      Sources: Map<string, Table>
      Versions: Map<string, int>
      Deltas: Map<string, TableDelta> }

let private sheetw: NodeWitness<SheetNode, string> =
    { Id = fun n -> n.Id
      KindTag =
        fun n ->
            match n.Def with
            | Root -> "root"
            | Source -> "source"
            | TableFormula _ -> "table"
            | CellSum _ -> "cell"
      Children = fun n -> n.Children
      ReplaceChildren = fun n cs -> { n with Children = cs } }

let private sheetReads (n: SheetNode) : Propagation.PartRead<string> seq =
    match n.Def with
    | Root
    | Source -> Seq.empty
    | TableFormula(src, _, cols) ->
        Seq.singleton
            { Propagation.Read = src
              Parts = Some(Set.ofList cols) }
    | CellSum(t, col) ->
        Seq.singleton
            { Propagation.Read = t
              Parts = Some(Set.singleton col) }

let private nodeReads (n: SheetNode) : string seq =
    sheetReads n |> Seq.map (fun r -> r.Read)

let private depsOf (s: Sheet) =
    Propagation.nodeDependencies (Propagation.partDependencyMap sheetw idw sheetReads s.Tree)

let private defOf (s: Sheet) (id: string) =
    Tree.tryFind sheetw idw id s.Tree |> Option.map (fun n -> n.Def)

let private sumColumn (t: Table) (col: string) : Cell =
    match Table.tryColumn col t with
    | None -> Null
    | Some c ->
        c.Cells
        |> List.sumBy (function
            | Int i -> float i
            | Float f -> f
            | _ -> 0.0)
        |> Float

let private tableOf (v: SheetValue option) =
    match v with
    | Some(SourceV s) -> Some s.Table
    | Some(TableV t) -> Some t.Result
    | _ -> None

/// The REFERENCE evaluator: what each node means, computed from scratch.
let private reference (s: Sheet) (resolve: string -> SheetValue option) (id: string) : Result<SheetValue, string> =
    match defOf s id with
    | None -> Error("no such node " + id)
    | Some Root -> Ok(CellV Null)
    | Some Source ->
        Ok(
            SourceV
                { Table = s.Sources[id]
                  Version = s.Versions[id]
                  Delta = s.Deltas[id] }
        )
    | Some(TableFormula(src, pipeline, _)) ->
        match resolve src with
        | Some(SourceV snap) ->
            DataFrame.evalPipeline pipeline snap.Table
            |> Result.map (fun t ->
                TableV
                    { Result = t
                      State = None
                      BuiltAt = snap.Version })
            |> Result.mapError DataFrame.errorString
        | _ -> Ok(CellV Null)
    | Some(CellSum(t, col)) ->
        match tableOf (resolve t) with
        | Some tbl -> Ok(CellV(sumColumn tbl col))
        | None -> Ok(CellV Null)

/// The PRIOR-AWARE evaluator: a table node refreshes its prior state against its source's delta
/// when that state is one version behind (or at) the source, and primes otherwise. Everything it
/// needs is an argument — the prior from the driver, the delta from the source's value.
let withPrior
    (s: Sheet)
    (resolve: string -> SheetValue option)
    (prior: SheetValue option)
    (id: string)
    : Result<SheetValue, string> =
    match defOf s id with
    | Some(TableFormula(src, pipeline, _)) ->
        match resolve src with
        | Some(SourceV snap) ->
            let result =
                match prior with
                | Some(TableV { State = Some st; BuiltAt = at }) when at = snap.Version - 1 ->
                    Incremental.refresh DataFrame.noResolve Map.empty rid pipeline st snap.Delta snap.Table
                | Some(TableV { State = Some st; BuiltAt = at }) when at = snap.Version ->
                    Incremental.refresh
                        DataFrame.noResolve
                        Map.empty
                        rid
                        pipeline
                        st
                        (Delta.empty rid.Scheme)
                        snap.Table
                | _ -> Incremental.prime DataFrame.noResolve Map.empty rid pipeline snap.Table

            result
            |> Result.map (fun st ->
                TableV
                    { Result = Incremental.result st
                      State = Some st
                      BuiltAt = snap.Version })
            |> Result.mapError DataFrame.errorString
        | _ -> Ok(CellV Null)
    | _ -> reference s resolve id

let private initialSheet (rows: int) : Sheet =
    let node id def = { Id = id; Def = def; Children = [] }

    { Tree =
        { Id = "root"
          Def = Root
          Children =
            [ node "orders" Source
              node "lines" (TableFormula("orders", linesPipeline, [ "qty"; "price" ]))
              node "total" (CellSum("lines", "amount"))
              node "idSum" (CellSum("orders", "id")) ] }
      Sources = Map.ofList [ "orders", orders rows ]
      Versions = Map.ofList [ "orders", 0 ]
      Deltas = Map.ofList [ "orders", Delta.empty rid.Scheme ] }

/// A data edit through `ColumnOps`: the source moves one version, and its delta is `deltaOf`.
let private dataEdit (op: ColumnOp) (s: Sheet) : Sheet * Set<string> =
    let t = s.Sources["orders"]

    match ColumnOps.apply op t with
    | Error _ -> s, Set.empty
    | Ok t' ->
        { s with
            Sources = Map.add "orders" t' s.Sources
            Versions = Map.add "orders" (s.Versions["orders"] + 1) s.Versions
            Deltas = Map.add "orders" (ColumnOps.deltaOf rid t op) s.Deltas },
        Set.singleton "orders"

/// A structural edit through `Ops`, its change set from `changedForOp`.
let private sheetEdit (op: SkeletonOp<SheetNode, string>) (s: Sheet) : Sheet * Set<string> =
    match Ops.apply sheetw idw op s.Tree with
    | Error _ -> s, Set.empty
    | Ok tree' -> { s with Tree = tree' }, Propagation.changedForOp sheetw idw nodeReads s.Tree tree' op

let private redefineLines (factor: float) =
    UpdateNode
        { Id = "lines"
          Def =
            TableFormula(
                "orders",
                [ Derive("amount", Binary(Mul, Binary(Mul, Cast(FloatType, Col "qty"), Col "price"), Lit(Float factor))) ],
                [ "qty"; "price" ]
            )
          Children = [] }

let evaluatorWitness: EvaluatorWitness<Sheet, SheetValue> =
    { Surface = "the Phase 250 composition sheet"
      Model =
        fun r ->
            let n, r1 = ConfRng.intBelow 8 r
            initialSheet (3 + n), r1
      Deps = depsOf
      EvalNode = reference
      Change =
        fun s r ->
            let rows = Table.rowCount s.Sources["orders"]
            let kind, r1 = ConfRng.intBelow 8 r
            let row, r2 = ConfRng.intBelow rows r1
            let v, r3 = ConfRng.intBelow 40 r2

            let edited =
                match kind with
                | 0 -> dataEdit (SetCell("price", row, Float(0.25 * float (v + 1)))) s
                | 1 -> dataEdit (SetCell("qty", row, Int(v + 1))) s
                | 2 -> dataEdit (SetCell("id", row, Int(1000 + v))) s
                | 3 -> dataEdit (AppendRows [ [ "id", Int(2000 + v); "qty", Int 1; "price", Float 1.0 ] ]) s
                | 4 ->
                    dataEdit (SetColumn(Column.create "qty" IntType [ for i in 0 .. rows - 1 -> Int((i + v) % 5) ])) s
                | 5 -> sheetEdit (redefineLines (float (v % 3 + 1))) s
                // A redefinition the pipeline evaluator refuses (a column the source does not have),
                // so the failing-evaluator arm of the agreement law is reached.
                | 6 ->
                    sheetEdit
                        (UpdateNode
                            { Id = "lines"
                              Def = TableFormula("orders", [ Derive("amount", Col "missing") ], [ "missing" ])
                              Children = [] })
                        s
                | _ -> sheetEdit (Batch [ RemoveNode "idSum" ]) s

            edited, r3 }

let private sheetTests =
    testList
        "the composed sheet"
        [ testCase "a one-cell edit refreshes the table node with ONE row, carried by evalFromWith's prior"
          <| fun () ->
              let s0 = initialSheet 1000

              let full0 =
                  Propagation.evalWith (withPrior s0) (depsOf s0)
                  |> Result.defaultWith (fun e -> failwithf "%A" e)

              let s1, changed = dataEdit (SetCell("price", 500, Float 7.25)) s0

              match Propagation.evalFromWith (withPrior s1) full0.Values changed (depsOf s1) with
              | Ok o ->
                  match o.Values["lines"] with
                  | TableV { State = Some st } ->
                      Expect.equal (Incremental.footprint st).Recompute (RowsRecomputed 1) "one row"
                  | other -> failtestf "lines is not a refreshed table: %A" other

                  Expect.equal
                      (Ok o)
                      (Propagation.eval (reference s1) (depsOf s1))
                      "and the refresh equals a full reference evaluation"
              | Error e -> failtestf "%A" e

          testCase "a price edit reaches, at column granularity, every node but the one reading ids"
          <| fun () ->
              let s0 = initialSheet 10
              let op = SetCell("price", 3, Float 1.5)
              let pdeps = Propagation.partDependencyMap sheetw idw sheetReads s0.Tree

              let dirty =
                  Propagation.dirtyFromChangedParts
                      pdeps
                      (fun _ -> ColumnOps.changedColumns op)
                      (Set.singleton "orders")

              Expect.equal dirty (Set.ofList [ "orders"; "lines"; "total" ]) "idSum reads only id"

          testCase "a formula redefinition is one UpdateNode, and dirties one node plus its readers"
          <| fun () ->
              let s0 = initialSheet 10
              let s1, changed = sheetEdit (redefineLines 2.0) s0
              Expect.equal changed (Set.ofList [ "lines"; "total" ]) "lines and the node that reads it"

              let dirtyByOp = Propagation.touchedBy sheetw idw s0.Tree (redefineLines 2.0)

              Expect.equal dirtyByOp (Set.singleton "lines") "touched: the node alone, not the root"

              let prior =
                  Propagation.evalWith (withPrior s0) (depsOf s0)
                  |> Result.defaultWith (fun e -> failwithf "%A" e)

              Expect.equal
                  (Propagation.evalFromWith (withPrior s1) prior.Values changed (depsOf s1))
                  (Propagation.evalWith (withPrior s1) (depsOf s1))
                  "and the refresh agrees"

          testCase "the sheet's prior-aware evaluator is certified by propagationEvaluatorLawsWith"
          <| fun () ->
              let results =
                  Conformance.propagationEvaluatorLawsWith evaluatorWitness withPrior 2500 120

              match results |> List.filter (fun r -> not r.Passed) with
              | [] -> ()
              | failed ->
                  failed
                  |> List.map (fun r -> r.Law + ": " + defaultArg r.Counterexample "")
                  |> String.concat "\n"
                  |> failtestf "the sheet failed propagationEvaluatorLawsWith:\n%s"

          testCase "the family has teeth: an evaluator that trusts a stale prior fails the prior discipline"
          <| fun () ->
              // Returns the prior's table unconditionally when handed one: fast, and wrong after an edit.
              let trusting (s: Sheet) resolve (prior: SheetValue option) id =
                  match prior, defOf s id with
                  | Some(TableV t), Some(TableFormula _) -> Ok(TableV t)
                  | _ -> withPrior s resolve prior id

              let results =
                  Conformance.propagationEvaluatorLawsWith evaluatorWitness trusting 2500 120

              let law = results |> List.find (fun r -> r.Law.Contains "prior discipline")

              Expect.isFalse law.Passed "the stale prior is caught"

          testCase "the family has teeth: an evaluator whose prior-blind reading is not the reference fails"
          <| fun () ->
              let drifting (s: Sheet) resolve (prior: SheetValue option) id =
                  match prior, defOf s id with
                  | None, Some(CellSum _) -> Ok(CellV(Float -1.0))
                  | _ -> withPrior s resolve prior id

              let results =
                  Conformance.propagationEvaluatorLawsWith evaluatorWitness drifting 2500 40

              let law = results |> List.find (fun r -> r.Law.Contains "prior-blind reading")
              Expect.isFalse law.Passed "the drift is caught" ]

[<Tests>]
let tests =
    testList "PropagationComposition" [ updateNodeTests; changedForOpTests; partTests; deltaTests; sheetTests ]
