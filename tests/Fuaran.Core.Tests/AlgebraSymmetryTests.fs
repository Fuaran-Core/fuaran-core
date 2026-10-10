/// Phase 317 — the remaining algebra symmetries: `Schema.patch` (the transform `Schema.diff`
/// reports), propagation pull (`Propagation.neededFor` / `evalFor` / `evalForWith`, the dual of
/// the dirty set) and an index maintained through an edit (`Ops.Index.afterOp`, the dual of
/// `Tree.Index.build`). Each law is held over a generator, with the arms it must reach counted.
module Fuaran.Core.Tests.AlgebraSymmetryTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

// ---- shared ---------------------------------------------------------------------------------

/// A cursor over `ConfRng`, so a body reads `draw 6` rather than threading the state.
type private Rng(seed: int) =
    let mutable state = ConfRng.ofSeed seed

    member _.Below(n: int) : int =
        let v, s = ConfRng.intBelow n state
        state <- s
        v

    member r.Pick(xs: 'a list) : 'a = List.item (r.Below(List.length xs)) xs

    member _.Shuffle(xs: 'a list) : 'a list =
        let v, s = ConfRng.shuffle xs state
        state <- s
        v

// ---- Schema.patch -----------------------------------------------------------------------------

let private allTypes =
    [ IntType
      FloatType
      BoolType
      StringType
      DateType
      TimestampType TimeUnit.Seconds
      DecimalType ]

let private namePool = [ "a"; "b"; "c"; "d"; "e"; "f"; "g"; "h"; "x|y"; "\u0001" ]

/// A schema naming no column twice: up to six names from the pool, each with a drawn type.
let private genSchema (rng: Rng) : Schema =
    rng.Shuffle namePool
    |> List.truncate (rng.Below 7)
    |> List.map (fun n -> n, rng.Pick allTypes)

/// A target related to `old` the way edits relate them — some columns dropped, some retyped, some
/// added, and the result reordered or not — or, one time in four, an unrelated schema.
let private genTarget (rng: Rng) (old: Schema) : Schema =
    if rng.Below 4 = 0 then
        genSchema rng
    else
        let kept = old |> List.filter (fun _ -> rng.Below 4 <> 0)

        let retyped =
            kept
            |> List.map (fun (n, t) -> if rng.Below 4 = 0 then n, rng.Pick allTypes else n, t)

        let used = old |> List.map fst |> Set.ofList

        let fresh =
            namePool
            |> List.filter (fun n -> not (used.Contains n))
            |> rng.Shuffle
            |> List.truncate (rng.Below 3)
            |> List.map (fun n -> n, rng.Pick allTypes)

        let combined = retyped @ fresh

        match rng.Below 3 with
        | 0 -> rng.Shuffle combined
        | 1 ->
            // an added column placed somewhere other than last, the common columns in order
            match fresh with
            | f :: _ when not retyped.IsEmpty ->
                let at = rng.Below(List.length retyped)
                (List.take at retyped) @ [ f ] @ (List.skip at retyped) @ (List.tail fresh)
            | _ -> combined
        | _ -> combined

[<Tests>]
let schemaPatchTests =
    testList
        "Phase 317 — Schema.patch"
        [ testCase
              "patch old (diff old target) = Ok target over the schema generator, reorders and placed additions reached"
          <| fun () ->
              let rng = Rng 317
              let mutable reordered = 0
              let mutable placedAddition = 0
              let mutable retypedSeen = 0
              let mutable removedSeen = 0

              for i in 0..1999 do
                  let old = genSchema rng
                  let target = genTarget rng old
                  let delta = Schema.diff old target

                  if delta.Reordered then
                      reordered <- reordered + 1

                  if not delta.Reordered && not delta.Order.IsEmpty then
                      placedAddition <- placedAddition + 1

                  if not delta.Retyped.IsEmpty then
                      retypedSeen <- retypedSeen + 1

                  if not delta.Removed.IsEmpty then
                      removedSeen <- removedSeen + 1

                  Expect.equal
                      (Schema.patch old delta)
                      (Ok target)
                      (sprintf "iter %d: old=%A target=%A delta=%A" i old target delta)

                  // `Reordered` is implied by `Order`
                  if delta.Reordered then
                      Expect.isFalse delta.Order.IsEmpty (sprintf "iter %d: a reorder with no Order" i)

              Expect.isGreaterThan reordered 100 "reorders of the common columns were drawn"
              Expect.isGreaterThan placedAddition 20 "additions placed before the last column were drawn"
              Expect.isGreaterThan retypedSeen 100 "retypes were drawn"
              Expect.isGreaterThan removedSeen 100 "removals were drawn"

          testCase "diff a a is the identity delta, and the identity delta patches every schema to itself"
          <| fun () ->
              let rng = Rng 3170

              for i in 0..499 do
                  let s = genSchema rng
                  Expect.equal (Schema.diff s s) Schema.identityDelta (sprintf "iter %d: diff a a" i)
                  Expect.equal (Schema.patch s Schema.identityDelta) (Ok s) (sprintf "iter %d: patch identity" i)

          testCase "an order that only moves an added column is recorded, and one that matches the derived order is not"
          <| fun () ->
              let old = [ "a", IntType; "b", IntType ]
              let appended = Schema.diff old [ "a", IntType; "b", IntType; "c", StringType ]
              Expect.isEmpty appended.Order "the derived order needs no record"
              Expect.isFalse appended.Reordered "nothing common moved"
              let placed = Schema.diff old [ "c", StringType; "a", IntType; "b", IntType ]
              Expect.equal placed.Order [ "c"; "a"; "b" ] "an addition placed first is recorded"
              Expect.isFalse placed.Reordered "the common columns kept their order"

              Expect.equal (Schema.patch old placed) (Ok [ "c", StringType; "a", IntType; "b", IntType ]) "and replays"

          testCase "patch refuses a delta computed against another schema, by name"
          <| fun () ->
              let s = [ "a", IntType; "b", StringType ]

              let delta =
                  { Schema.identityDelta with
                      Removed = [ "z", IntType ] }

              Expect.equal (Schema.patch s delta) (Error(AbsentColumn "z")) "removing an absent column"

              Expect.equal
                  (Schema.patch
                      s
                      { Schema.identityDelta with
                          Removed = [ "a", FloatType ] })
                  (Error(TypeDisagrees("a", FloatType, IntType)))
                  "removing a column recorded with another type"

              Expect.equal
                  (Schema.patch
                      s
                      { Schema.identityDelta with
                          Retyped = [ "q", IntType, FloatType ] })
                  (Error(AbsentColumn "q"))
                  "retyping an absent column"

              Expect.equal
                  (Schema.patch
                      s
                      { Schema.identityDelta with
                          Retyped = [ "b", IntType, FloatType ] })
                  (Error(TypeDisagrees("b", IntType, StringType)))
                  "retyping from a type the column does not have"

              Expect.equal
                  (Schema.patch
                      s
                      { Schema.identityDelta with
                          Added = [ "a", IntType ] })
                  (Error(AlreadyPresent "a"))
                  "adding a column already present"

              Expect.equal
                  (Schema.patch
                      s
                      { Schema.identityDelta with
                          Order = [ "a" ] })
                  (Error(OrderMismatch([ "a"; "b" ], [ "a" ])))
                  "an order that is not a permutation"

              Expect.equal
                  (Schema.patch [ "a", IntType; "a", IntType ] Schema.identityDelta)
                  (Error(DuplicateColumn "a"))
                  "a schema naming a column twice"

          testCase "the delta codec round-trips every generated delta, and its bytes are canonical"
          <| fun () ->
              let rng = Rng 31700

              for i in 0..999 do
                  let old = genSchema rng
                  let delta = Schema.diff old (genTarget rng old)
                  let wire = SchemaDeltaCodec.encode delta
                  Expect.equal (SchemaDeltaCodec.decode wire) (Ok delta) (sprintf "iter %d: %s" i wire)
                  Expect.equal (SchemaDeltaCodec.codec.Decode wire) (Ok delta) (sprintf "iter %d: codec" i)

              let d =
                  { Added = [ "c", StringType ]
                    Removed = [ "z", BoolType ]
                    Retyped = [ "a", IntType, FloatType ]
                    Reordered = true
                    Order = [ "b"; "a"; "c" ] }

              Expect.equal
                  (SchemaDeltaCodec.encode d)
                  """{"added":[{"name":"c","type":"string"}],"order":["b","a","c"],"removed":[{"name":"z","type":"bool"}],"reordered":true,"retyped":[{"from":"int","name":"a","to":"float"}]}"""
                  "the canonical bytes: Ordinal-sorted keys, the column-type tags"

          testCase "the delta codec refuses what it cannot carry, in the columnar envelope"
          <| fun () ->
              Expect.isTrue
                  (match SchemaDeltaCodec.decode "{" with
                   | Error(NotJson _) -> true
                   | _ -> false)
                  "not JSON"

              Expect.equal
                  (SchemaDeltaCodec.decode """{"added":[],"removed":[],"retyped":[],"reordered":false}""")
                  (Error(MissingField "order"))
                  "a missing member"

              Expect.equal
                  (SchemaDeltaCodec.decode
                      """{"added":[{"name":"a","type":"uuid"}],"removed":[],"retyped":[],"reordered":false,"order":[]}""")
                  (Error(UnknownType("uuid", ColumnType.all)))
                  "an unknown type tag"

              Expect.isTrue
                  (match
                      SchemaDeltaCodec.decode """{"added":[],"removed":[],"retyped":[],"reordered":"no","order":[]}"""
                   with
                   | Error(MalformedShape _) -> true
                   | _ -> false)
                  "a reordered flag that is not a bool" ]

// ---- propagation pull -------------------------------------------------------------------------

/// The independent oracle: every id reachable from the targets by following reads.
let private upstreamOracle (deps: Map<string, Set<string>>) (targets: Set<string>) : Set<string> =
    let rec go (acc: Set<string>) (stack: string list) =
        match stack with
        | [] -> acc
        | x :: rest ->
            if acc.Contains x then
                go acc rest
            else
                let reads = Map.tryFind x deps |> Option.map Set.toList |> Option.defaultValue []

                go (Set.add x acc) (reads @ rest)

    go Set.empty (Set.toList targets)

/// A dependency map: lower reads, one graph in four with back edges (cycles), one in four with a
/// dangling read.
let private genDeps (rng: Rng) : Map<string, Set<string>> * string list =
    let n = rng.Below 7 + 2
    let ids = [ for k in 0 .. n - 1 -> string k ]
    let shape = rng.Below 4

    let deps =
        [ for k in 0 .. n - 1 ->
              let lower =
                  [ for j in 0 .. k - 1 do
                        if rng.Below 3 = 0 then
                            yield string j ]

              let back =
                  if shape = 1 && k < n - 1 && rng.Below 2 = 0 then
                      [ string (k + 1 + rng.Below(n - 1 - k)) ]
                  else
                      []

              let dangling = if shape = 2 && rng.Below 4 = 0 then [ "zz" ] else []
              string k, Set.ofList (lower @ back @ dangling) ]
        |> Map.ofList

    deps, ids

/// The toy evaluator: a node's base plus its reads, a designated base refusing by name; it records
/// every id it is handed.
let private toyEval
    (baseOf: Map<string, int>)
    (deps: Map<string, Set<string>>)
    (invoked: ResizeArray<string>)
    (resolve: string -> int option)
    (id: string)
    : Result<int, string> =
    invoked.Add id
    let b = Map.find id baseOf

    if b < 0 then
        Error("refused:" + id)
    else
        Ok(
            b
            + (Map.find id deps
               |> Set.fold (fun s r -> s + (resolve r |> Option.defaultValue -1)) 0)
        )

let private meets (s: Set<string>) (g: string list) = g |> List.exists s.Contains

[<Tests>]
let propagationPullTests =
    testList
        "Phase 317 — propagation pull"
        [ testCase
              "neededFor is the upstream closure: it holds the targets, is closed under reads, and is the least such set"
          <| fun () ->
              let rng = Rng 3171

              for i in 0..999 do
                  let deps, ids = genDeps rng

                  let targets =
                      [ for _ in 0 .. rng.Below 3 -> rng.Pick(ids @ [ "no-such-id" ]) ] |> Set.ofList

                  let needed = Propagation.neededFor deps targets
                  Expect.equal needed (upstreamOracle deps targets) (sprintf "iter %d: %A %A" i deps targets)
                  Expect.isTrue (Set.isSubset targets needed) "holds the targets"

                  for n in needed do
                      match Map.tryFind n deps with
                      | Some reads -> Expect.isTrue (Set.isSubset reads needed) (sprintf "iter %d: closed at %s" i n)
                      | None -> ()

                  // the dual of the dirty set: x is needed for {t} exactly when t is dirty for {x}
                  for t in ids do
                      for x in ids do
                          Expect.equal
                              ((Propagation.neededFor deps (Set.singleton t)).Contains x)
                              ((Propagation.dirtyFromChangedIds deps (Set.singleton x)).Contains t)
                              (sprintf "iter %d: duality at %s, %s" i t x)

          testCase "evalFor agrees with eval on the needed set, evaluates each needed node once and nothing else"
          <| fun () ->
              let rng = Rng 3172
              let mutable agreed = 0
              let mutable cyclicMet = 0
              let mutable pulledPastFailure = 0
              let mutable strictSubset = 0

              for i in 0..1999 do
                  let deps, ids = genDeps rng
                  // one graph in five carries a node that refuses
                  let failing = if rng.Below 5 = 0 then Some(rng.Pick ids) else None

                  let baseOf =
                      ids
                      |> List.map (fun id -> id, (if Some id = failing then -1 else rng.Below 100))
                      |> Map.ofList

                  let targets = [ for _ in 0 .. rng.Below 2 -> rng.Pick ids ] |> Set.ofList
                  let needed = Propagation.neededFor deps targets
                  let fullRan = ResizeArray()
                  let pullRan = ResizeArray()
                  let full = Propagation.eval (toyEval baseOf deps fullRan) deps
                  let pull = Propagation.evalFor (toyEval baseOf deps pullRan) targets deps

                  for id in pullRan do
                      Expect.isTrue (needed.Contains id) (sprintf "iter %d: evaluated %s outside the needed set" i id)

                  Expect.equal
                      (List.ofSeq pullRan |> List.distinct)
                      (List.ofSeq pullRan)
                      (sprintf "iter %d: a node evaluated twice" i)

                  if Set.count needed < List.length ids then
                      strictSubset <- strictSubset + 1

                  match full with
                  | Ok o ->
                      let expected: Propagation.EvalOutcome<int> =
                          { Values = o.Values |> Map.filter (fun k _ -> needed.Contains k)
                            Cyclic = o.Cyclic |> List.filter (meets needed) }

                      Expect.equal pull (Ok expected) (sprintf "iter %d: deps=%A targets=%A" i deps targets)
                      agreed <- agreed + 1

                      if not expected.Cyclic.IsEmpty then
                          cyclicMet <- cyclicMet + 1
                  | Error _ ->
                      match pull, failing with
                      | Ok _, Some f ->
                          Expect.isFalse (needed.Contains f) "a pull succeeds only past an unneeded failure"
                      | _ -> ()

                      if Result.isOk pull then
                          pulledPastFailure <- pulledPastFailure + 1

              Expect.isGreaterThan agreed 1000 "agreement asserted"
              Expect.isGreaterThan cyclicMet 20 "cyclic groups inside the needed set reached"
              Expect.isGreaterThan pulledPastFailure 10 "a pull that succeeds where the full evaluation fails reached"
              Expect.isGreaterThan strictSubset 500 "targets needing less than the whole map reached"

          testCase "evalForWith agrees with evalWith on the needed set and hands every node no prior"
          <| fun () ->
              let rng = Rng 3173

              for i in 0..499 do
                  let deps, ids = genDeps rng
                  let baseOf = ids |> List.map (fun id -> id, rng.Below 100) |> Map.ofList
                  let targets = Set.singleton (rng.Pick ids)
                  let needed = Propagation.neededFor deps targets
                  let priors = ResizeArray()

                  let ev (resolve: string -> int option) (prior: int option) (id: string) =
                      priors.Add prior
                      toyEval baseOf deps (ResizeArray()) resolve id

                  match Propagation.evalWith ev deps with
                  | Ok o ->
                      Expect.equal
                          (Propagation.evalForWith ev targets deps)
                          (Ok
                              { Values = o.Values |> Map.filter (fun k _ -> needed.Contains k)
                                Cyclic = o.Cyclic |> List.filter (meets needed) })
                          (sprintf "iter %d" i)
                  | Error e -> failtestf "iter %d: the toy evaluator never refuses here: %A" i e

                  Expect.isTrue (priors |> Seq.forall Option.isNone) "no prior is ever handed"

          testCase "a target the map does not hold is needed, gets no value and is not an error"
          <| fun () ->
              let deps = Map.ofList [ "a", Set.empty; "b", Set.ofList [ "a" ] ]
              let ran = ResizeArray()
              let baseOf = Map.ofList [ "a", 1; "b", 2 ]

              Expect.equal
                  (Propagation.neededFor deps (Set.ofList [ "b"; "q" ]))
                  (Set.ofList [ "a"; "b"; "q" ])
                  "needed"

              Expect.equal
                  (Propagation.evalFor (toyEval baseOf deps ran) (Set.ofList [ "b"; "q" ]) deps)
                  (Ok
                      { Values = Map.ofList [ "a", 1; "b", 3 ]
                        Cyclic = [] })
                  "the absent target has no value"

          testCase "evalFor refuses an undeclared read inside the needed set, as eval does"
          <| fun () ->
              let deps = Map.ofList [ "a", Set.empty; "b", Set.empty ]

              let leaky (resolve: string -> int option) (id: string) : Result<int, string> =
                  if id = "b" then
                      resolve "a" |> ignore

                  Ok 1

              Expect.equal
                  (Propagation.evalFor leaky (Set.singleton "b") deps)
                  (Error(Propagation.PropagationError.EvalUndeclaredRead("b", "a")))
                  "the undeclared read is named" ]

// ---- Ops.Index.afterOp ------------------------------------------------------------------------

/// A tree from the reference witness: up to `size` nodes, each hung under a drawn earlier node.
let private genTree (rng: Rng) (size: int) : RNode =
    let n = rng.Below size + 1

    let parents =
        [ for k in 1 .. n - 1 -> k, rng.Below k ] |> List.groupBy snd |> Map.ofList

    let rec build (k: int) : RNode =
        let kids =
            Map.tryFind k parents |> Option.map (List.map fst) |> Option.defaultValue []

        RNode.node (sprintf "n%d" k) (if List.isEmpty kids then "leaf" else "box") (kids |> List.map build)

    build 0

let mutable private freshCounter = 0

let private freshSubtree (rng: Rng) : RNode =
    freshCounter <- freshCounter + 1
    let stem = sprintf "f%d" freshCounter

    if rng.Below 2 = 0 then
        RNode.leaf stem "leaf" "v"
    else
        RNode.node stem "box" [ RNode.leaf (stem + "a") "leaf" "x"; RNode.leaf (stem + "b") "leaf" "y" ]

/// One structural op against `tree`, possibly one `apply` refuses.
let private genStructural (rng: Rng) (tree: RNode) : SkeletonOp<RNode, string> =
    let nodes = Tree.preorder nodew tree
    let ids = nodes |> List.map nodew.Id

    match rng.Below 4 with
    | 0 -> InsertChild(rng.Pick ids, freshSubtree rng)
    | 1 -> RemoveNode(rng.Pick ids)
    | 2 -> MoveNode(rng.Pick ids, rng.Pick ids)
    | _ ->
        let p = rng.Pick nodes
        ReorderChildren(p.Id, rng.Shuffle(p.Children |> List.map nodew.Id))

/// Every op kind: the four structural ones, a batch of one to three, and a content update.
let private genAnyOp (rng: Rng) (tree: RNode) : SkeletonOp<RNode, string> =
    match rng.Below 6 with
    | 4 -> Batch [ for _ in 0 .. rng.Below 3 -> genStructural rng tree ]
    | 5 ->
        let n = rng.Pick(Tree.preorder nodew tree)

        UpdateNode
            { n with
                Kind = (if rng.Below 2 = 0 then n.Kind else n.Kind + "'")
                Value = n.Value + "!" }
    | _ -> genStructural rng tree

let private kindOf (op: SkeletonOp<'N, 'I>) =
    match op with
    | InsertChild _ -> "insert"
    | RemoveNode _ -> "remove"
    | MoveNode _ -> "move"
    | ReorderChildren _ -> "reorder"
    | Batch _ -> "batch"
    | UpdateNode _ -> "update"

/// `≡`: the same root, parent links and stamp, the same keys, and equal nodes at every key.
let private sameIndex (a: Tree.NodeIndex<RNode, string>) (b: Tree.NodeIndex<RNode, string>) : bool =
    a.Root = b.Root
    && a.ParentOf = b.ParentOf
    && a.Fingerprint = b.Fingerprint
    && a.ById = b.ById

/// A witness that counts how often the index machinery reads a node's children — the measure of
/// work the cost leg reads, deterministic where a clock is not.
let private counting () =
    let calls = ref 0

    let w: NodeWitness<RNode, string> =
        { nodew with
            Children =
                fun n ->
                    calls.Value <- calls.Value + 1
                    n.Children }

    w, calls

[<Tests>]
let indexAfterOpTests =
    testList
        "Phase 317 — Ops.Index.afterOp"
        [ testCase "afterOp op post (build pre) ≡ build post over every op kind, and the stamp stays fresh"
          <| fun () ->
              let rng = Rng 3174
              let mutable kinds: Map<string, int> = Map.empty

              for i in 0..2999 do
                  let pre = genTree rng 12
                  let op = genAnyOp rng pre

                  match Ops.apply nodew idw op pre with
                  | Ok post ->
                      kinds <-
                          kinds
                          |> Map.add (kindOf op) ((Map.tryFind (kindOf op) kinds |> Option.defaultValue 0) + 1)

                      let carried = Ops.Index.afterOp nodew idw op post (Tree.Index.build nodew idw pre)
                      let rebuilt = Tree.Index.build nodew idw post

                      Expect.isTrue
                          (sameIndex carried rebuilt)
                          (sprintf "iter %d: op=%A\npre=%A\ncarried=%A\nrebuilt=%A" i op pre carried rebuilt)

                      Expect.isTrue (Tree.Index.isFreshFor nodew idw post carried) (sprintf "iter %d: fresh" i)
                  | Error _ -> ()

              for k in [ "insert"; "remove"; "move"; "reorder"; "batch"; "update" ] do
                  Expect.isGreaterThan
                      (Map.tryFind k kinds |> Option.defaultValue 0)
                      50
                      (sprintf "accepted %s ops were drawn: %A" k kinds)

          testCase "an index carried through a whole edit session equals the rebuild at every step"
          <| fun () ->
              let rng = Rng 3175

              for s in 0..99 do
                  let mutable tree = genTree rng 20
                  let mutable ix = Tree.Index.build nodew idw tree

                  for k in 0..29 do
                      let op = genAnyOp rng tree

                      match Ops.apply nodew idw op tree with
                      | Ok post ->
                          ix <- Ops.Index.afterOp nodew idw op post ix
                          tree <- post

                          Expect.isTrue
                              (sameIndex ix (Tree.Index.build nodew idw tree))
                              (sprintf "session %d step %d: op=%A" s k op)
                      | Error _ -> ()

          testCase "a post the op does not describe falls back to the rebuild, so the law holds whatever is handed"
          <| fun () ->
              let pre = sample ()
              let ix = Tree.Index.build nodew idw pre
              let ids = Tree.preorder nodew pre |> List.map nodew.Id
              let target = ids |> List.last
              // an op `apply` would accept, handed the UNEDITED tree
              let carried = Ops.Index.afterOp nodew idw (RemoveNode target) pre ix
              Expect.isTrue (sameIndex carried ix) "the unedited tree's index"
              // an op naming an id the tree does not hold
              let carried2 = Ops.Index.afterOp nodew idw (RemoveNode "no-such-id") pre ix
              Expect.isTrue (sameIndex carried2 ix) "an unknown id"

          testCase "the stamp is order-free over nodes yet still sees a reorder, a kind change and a removal"
          <| fun () ->
              let t =
                  RNode.node "r" "box" [ RNode.leaf "a" "leaf" "1"; RNode.leaf "b" "leaf" "2" ]

              let ix = Tree.Index.build nodew idw t

              let swapped =
                  RNode.node "r" "box" [ RNode.leaf "b" "leaf" "2"; RNode.leaf "a" "leaf" "1" ]

              let rekinded =
                  RNode.node "r" "box" [ RNode.leaf "a" "other" "1"; RNode.leaf "b" "leaf" "2" ]

              let removed = RNode.node "r" "box" [ RNode.leaf "a" "leaf" "1" ]
              Expect.isTrue (Tree.Index.isFreshFor nodew idw t ix) "fresh for itself"
              Expect.isFalse (Tree.Index.isFreshFor nodew idw swapped ix) "a reorder"
              Expect.isFalse (Tree.Index.isFreshFor nodew idw rekinded ix) "a kind change"
              Expect.isFalse (Tree.Index.isFreshFor nodew idw removed ix) "a removal"

          testCase "an edit session's index cost is linear in the ops and does not grow with the tree"
          <| fun () ->
              // The edits happen under `work`; `ballast` is the rest of the tree and only grows.
              let tree (ballastSize: int) =
                  RNode.node
                      "root"
                      "box"
                      [ RNode.node "work" "box" [ RNode.leaf "w0" "leaf" "0"; RNode.leaf "w1" "leaf" "1" ]
                        RNode.node
                            "ballast"
                            "box"
                            [ for k in 0 .. ballastSize - 1 ->
                                  RNode.node (sprintf "b%d" k) "box" [ RNode.leaf (sprintf "b%d-x" k) "leaf" "" ] ] ]

              // a deterministic session confined to `work`: every op kind, a batch included
              let session (rounds: int) : SkeletonOp<RNode, string> list =
                  [ for r in 0 .. rounds - 1 do
                        let x = sprintf "s%d" r
                        yield InsertChild("work", RNode.leaf x "leaf" "v")
                        yield ReorderChildren("work", [ x; "w0"; "w1" ])
                        yield UpdateNode(RNode.leaf x "leaf" "v'")
                        yield MoveNode(x, "w0")
                        yield Batch [ MoveNode(x, "work"); ReorderChildren("work", [ "w0"; "w1"; x ]) ]
                        yield RemoveNode x ]

              let carriedCost (ballastSize: int) (rounds: int) =
                  let w, calls = counting ()
                  let mutable t = tree ballastSize
                  let mutable ix = Tree.Index.build nodew idw t

                  for op in session rounds do
                      match Ops.apply nodew idw op t with
                      | Ok post ->
                          ix <- Ops.Index.afterOp w idw op post ix
                          t <- post
                      | Error e -> failtestf "the session is valid: %A refused %A" op e

                  Expect.isTrue (sameIndex ix (Tree.Index.build nodew idw t)) "the carried index is the rebuild"
                  calls.Value

              let rebuildCost (ballastSize: int) (rounds: int) =
                  let w, calls = counting ()
                  let mutable t = tree ballastSize

                  for op in session rounds do
                      match Ops.apply nodew idw op t with
                      | Ok post ->
                          Tree.Index.build w idw post |> ignore
                          t <- post
                      | Error e -> failtestf "the session is valid: %A refused %A" op e

                  calls.Value

              let small, large = 10, 400
              // not in the tree: the same session costs the same over a tree forty times the size
              Expect.equal (carriedCost large 4) (carriedCost small 4) "the carried cost does not see the ballast"
              // linear in the ops: twice the session, twice the cost
              Expect.equal (carriedCost large 8) (2 * carriedCost large 4) "twice the ops, twice the cost"
              // and the rebuild it replaces does grow with the tree
              Expect.isGreaterThan (rebuildCost large 4) (20 * carriedCost large 4) "the rebuild pays for the tree" ]
