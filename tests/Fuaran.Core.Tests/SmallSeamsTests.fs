module Fuaran.Core.Tests.SmallSeamsTests

// Phase 298 — the five smaller seams made total: an iterative, prepared Propagation; tagged and
// total validator runs; one projection id key; ordinal anchors and a gated approval; a terminating
// tree index and a path-copying update; and the observer as a witness.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

// ---- Propagation ----

/// The recursive Tarjan this phase replaced, verbatim, as the reference the iterative one must
/// agree with item for item on every graph shallow enough for it to run.
let private recursiveTarjan (nodes: string list) (succ: string -> string list) : string list list =
    let mutable index = 0
    let idx = System.Collections.Generic.Dictionary<string, int>()
    let low = System.Collections.Generic.Dictionary<string, int>()
    let onStack = System.Collections.Generic.HashSet<string>()
    let stack = System.Collections.Generic.Stack<string>()
    let sccs = ResizeArray<string list>()

    let rec strongConnect v =
        idx[v] <- index
        low[v] <- index
        index <- index + 1
        stack.Push v
        onStack.Add v |> ignore

        for w in succ v do
            if not (idx.ContainsKey w) then
                strongConnect w
                low[v] <- min low[v] low[w]
            elif onStack.Contains w then
                low[v] <- min low[v] idx[w]

        if low[v] = idx[v] then
            let comp = ResizeArray<string>()
            let mutable popped = false

            while not popped do
                let w = stack.Pop()
                onStack.Remove w |> ignore
                comp.Add w

                if w = v then
                    popped <- true

            sccs.Add(List.ofSeq comp)

    for n in nodes do
        if not (idx.ContainsKey n) then
            strongConnect n

    List.ofSeq sccs

/// `sort` as it was defined over the recursive Tarjan.
let private recursiveSort (deps: Map<string, Set<string>>) : Propagation.TopoResult =
    let nodes = deps |> Map.toList |> List.map fst

    let succ id =
        match Map.tryFind id deps with
        | Some ds -> ds |> Set.toList |> List.filter (fun d -> Map.containsKey d deps)
        | None -> []

    let sccs = recursiveTarjan nodes succ

    { Order =
        sccs
        |> List.choose (fun comp ->
            match comp with
            | [ single ] when not (List.contains single (succ single)) -> Some single
            | _ -> None)
      Cycles =
        sccs
        |> List.filter (fun comp ->
            match comp with
            | [ single ] -> List.contains single (succ single)
            | _ -> true) }

/// A chain `c0 <- c1 <- … <- c(n-1)`: each node reads the one before it — a running-total column.
let private chain (n: int) : Map<string, Set<string>> =
    [ for i in 0 .. n - 1 ->
          "c" + string i,
          (if i = 0 then
               Set.empty
           else
               Set.singleton ("c" + string (i - 1))) ]
    |> Map.ofList

let private sumEval (resolve: string -> int option) (id: string) : Result<int, string> =
    let i = int (id.Substring 1)

    if i = 0 then
        Ok 1
    else
        match resolve ("c" + string (i - 1)) with
        | Some v -> Ok(v + 1)
        | None -> Error "missing upstream"

let private propagationTests =
    testList
        "Propagation (Phase 298)"
        [ testCase "the iterative sort agrees with the recursive one on 300 random graphs, cycles included"
          <| fun _ ->
              let rnd = System.Random 298

              for _ in 1..300 do
                  let n = 1 + rnd.Next 12

                  let deps =
                      [ for i in 0 .. n - 1 ->
                            "v" + string i,
                            ([ for _ in 1 .. rnd.Next 4 -> "v" + string (rnd.Next(n + 2)) ] |> Set.ofList) ]
                      |> Map.ofList

                  Expect.equal (Propagation.sort deps) (recursiveSort deps) (sprintf "sort over %A" deps)

          testCase "a 50,000-long dependency chain sorts, evaluates and re-evaluates without a stack overflow"
          <| fun _ ->
              // the recursive sort died near 3,500 deep in a Debug test host
              let n = 50_000
              let deps = chain n
              let topo = Propagation.sort deps
              Expect.equal (List.length topo.Order) n "every node ordered"
              Expect.isEmpty topo.Cycles "a chain has no cycle"
              Expect.equal (List.head topo.Order) "c0" "dependencies first"

              match Propagation.eval (fun r id -> sumEval r id) deps with
              | Ok full ->
                  Expect.equal (Map.find ("c" + string (n - 1)) full.Values) n "the running total"

                  match Propagation.evalFrom (fun r id -> sumEval r id) full.Values (Set.singleton "c0") deps with
                  | Ok again -> Expect.equal again.Values full.Values "evalFrom over the chain agrees with eval"
                  | Error e -> failtestf "evalFrom refused: %A" e
              | Error e -> failtestf "eval refused: %A" e

          testCase "a Plan reused across ticks gives evalFrom's answer, and refuses an unknown change alike"
          <| fun _ ->
              let deps =
                  Map.ofList
                      [ "a", Set.empty
                        "b", Set.ofList [ "a" ]
                        "c", Set.ofList [ "b"; "a" ]
                        "d", Set.empty ]

              let ev (resolve: string -> int option) (id: string) =
                  match id with
                  | "a" -> Ok 1
                  | "d" -> Ok 7
                  | "b" -> Ok((resolve "a" |> Option.defaultValue 0) + 1)
                  | _ -> Ok((resolve "b" |> Option.defaultValue 0) + (resolve "a" |> Option.defaultValue 0))

              let plan = Propagation.plan deps
              let prior = (Propagation.eval ev deps |> Result.toOption).Value.Values

              for changed in [ Set.empty; Set.ofList [ "a" ]; Set.ofList [ "b" ]; Set.ofList [ "d"; "a" ] ] do
                  Expect.equal
                      (Propagation.evalFromPlan ev prior changed plan)
                      (Propagation.evalFrom ev prior changed deps)
                      (sprintf "tick over %A" changed)

              match Propagation.evalFromPlan ev prior (Set.ofList [ "zz" ]) plan with
              | Error(Propagation.PropagationError.EvalUnknownChange [ "zz" ]) -> ()
              | other -> failtestf "expected EvalUnknownChange, got %A" other ]

// ---- Validator ----

let private flagAll (code: string) : RuleFamily<RNode, string> =
    Validator.perNode code (fun w n -> [ Defect.create code Severity.Warning code (Some(w.Id n)) ])

let private throwing: RuleFamily<RNode, string> =
    { Id = "boom"
      Run = fun _ _ -> failwith "rule exploded" }

let private table (cols: (string * ColumnType * Cell list) list) : Table =
    { Schema = cols |> List.map (fun (n, ty, _) -> n, ty)
      Columns = cols |> List.map (fun (n, ty, cells) -> Column.create n ty cells) }

let private orFail (r: Result<'a, RegistrationError>) : 'a =
    r |> Result.defaultWith (fun e -> failwithf "registry: %A" e)

let private validatorTests =
    testList
        "Validator (Phase 298)"
        [ testCase "runAllTagged pairs each finding with its family, and Family is stamped on the defect"
          <| fun _ ->
              let reg = Validator.ofFamilies [ flagAll "A"; flagAll "B" ] |> orFail
              let tree = RNode.node "root" "doc" [ RNode.leaf "x" "para" "" ]
              let tagged = Validator.runAllTagged nodew reg tree

              Expect.equal (tagged |> List.map fst) [ "A"; "A"; "B"; "B" ] "family order, then node order"
              Expect.isTrue (tagged |> List.forall (fun (f, d) -> d.Family = f)) "the walker stamped Family"
              Expect.equal (Validator.runAll nodew reg tree) (List.map snd tagged) "runAll is the tagged run's defects"

          testCase "a family that throws is one RULE-FAULT error, and the families after it still run"
          <| fun _ ->
              let reg = Validator.ofFamilies [ throwing; flagAll "A" ] |> orFail
              let defects = Validator.runAll nodew reg (RNode.leaf "x" "para" "")

              match defects with
              | [ fault; a ] ->
                  Expect.equal fault.Code Validator.FamilyFaultCode "the fault"
                  Expect.equal fault.Family "boom" "names the family"
                  Expect.stringContains fault.Message "rule exploded" "carries the exception's message"
                  Expect.equal a.Code "A" "the good family's finding survives"
              | other -> failtestf "expected a fault and a finding, got %A" other

          testCase "register refuses a duplicate id naming the registered ones; enumerate and tryFind read the registry"
          <| fun _ ->
              let reg = Validator.ofFamilies [ flagAll "A"; flagAll "B" ] |> orFail
              Expect.equal (Validator.enumerate reg) [ "A"; "B" ] "registration order"
              Expect.isSome (Validator.tryFind "B" reg) "found"
              Expect.isNone (Validator.tryFind "Z" reg) "absent"

              match Validator.register (flagAll "A") reg with
              | Error(RegistrationError.DuplicateRule("A", [ "A"; "B" ])) -> ()
              | other -> failtestf "expected DuplicateRule, got %A" other

              match ColumnValidator.ofRules [ ColumnValidator.notNull "a"; ColumnValidator.notNull "a" ] with
              | Error(RegistrationError.DuplicateRule _) -> ()
              | other -> failtestf "expected the column registry to refuse too, got %A" other

          testCase "the Registry alias still names the registry"
          <| fun _ ->
              let reg: Validator.RuleRegistry<RNode, string> = Validator.empty
              Expect.isEmpty (Validator.enumerate reg) "one type under two names"

          testCase "column rule ids are injective over their parameters"
          <| fun _ ->
              Expect.notEqual
                  (ColumnValidator.unique [ "a,b" ]).Id
                  (ColumnValidator.unique [ "a"; "b" ]).Id
                  "a comma in a column name is not a column boundary"

              Expect.notEqual
                  (ColumnValidator.inRange "a" 0.0 1.0).Id
                  (ColumnValidator.inRange "a" 0.0 2.0).Id
                  "two ranges over one column are two rules"

          testCase "ColumnValidator.validateTagged stamps each finding with its rule"
          <| fun _ ->
              let rule = ColumnValidator.notNull "a"
              let reg = ColumnValidator.ofRules [ rule ] |> orFail

              match ColumnValidator.validateTagged reg (table [ "a", IntType, [ Null; Int 1 ] ]) with
              | [ id, d ] ->
                  Expect.equal id rule.Id "tagged with the rule id"
                  Expect.equal d.Family rule.Id "stamped"
              | other -> failtestf "expected one finding, got %A" other

          testCase "inRange refuses a NaN bound and reports NaN and unreadable cells"
          <| fun _ ->
              let t =
                  table
                      [ "f", FloatType, [ Float nan; Float 1.0 ]
                        "d", DecimalType, [ Decimal "abc"; Decimal "2" ] ]

              let codes (r: ColumnRule) =
                  ColumnValidator.validate (ColumnValidator.ofRules [ r ] |> orFail) t
                  |> List.map (fun d -> d.Code, d.Node)

              Expect.equal
                  (codes (ColumnValidator.inRange "f" nan 10.0))
                  [ ColumnValidator.BadRangeCode, Some "f" ]
                  "NaN lo"

              Expect.equal
                  (codes (ColumnValidator.inRange "f" 0.0 nan))
                  [ ColumnValidator.BadRangeCode, Some "f" ]
                  "NaN hi"

              Expect.equal
                  (codes (ColumnValidator.inRange "f" 0.0 10.0))
                  [ ColumnValidator.NotANumberCode, Some "f#0" ]
                  "a NaN cell"

              Expect.equal
                  (codes (ColumnValidator.inRange "d" 0.0 10.0))
                  [ ColumnValidator.NotANumberCode, Some "d#0" ]
                  "decimal text that is not decimal"

          testCase "unique refuses a ragged key column instead of reporting phantom duplicates; Null is a key value"
          <| fun _ ->
              let ragged =
                  { Schema = [ "a", IntType; "b", IntType ]
                    Columns =
                      [ Column.create "a" IntType [ Int 1; Int 2; Int 3 ]
                        Column.create "b" IntType [ Int 1 ] ] }

              let reg = ColumnValidator.ofRules [ ColumnValidator.unique [ "a"; "b" ] ] |> orFail

              Expect.equal
                  (ColumnValidator.validate reg ragged |> List.map (fun d -> d.Code, d.Node))
                  [ ColumnValidator.RaggedCode, Some "b" ]
                  "the short column is named; no row is called a duplicate"

              let nulls = table [ "a", IntType, [ Null; Int 1; Null ] ]
              let reg1 = ColumnValidator.ofRules [ ColumnValidator.unique [ "a" ] ] |> orFail

              Expect.equal
                  (ColumnValidator.validate reg1 nulls |> List.map _.Node)
                  [ Some "a#2" ]
                  "a repeated Null key is a duplicate"

          testCase "unique over 100,000 rows is linear (a measurement leg: 25k vs 100k)"
          <| fun _ ->
              let run n =
                  let t = table [ "k", IntType, [ for i in 0 .. n - 1 -> Int(i % (n - 1)) ] ]
                  let reg = ColumnValidator.ofRules [ ColumnValidator.unique [ "k" ] ] |> orFail
                  let sw = System.Diagnostics.Stopwatch.StartNew()
                  let defects = ColumnValidator.validate reg t
                  sw.Stop()
                  Expect.equal (List.length defects) 1 "exactly one repeat"
                  sw.Elapsed.TotalMilliseconds

              // the fastest of three runs at each size, after a warm-up, so a collection that lands
              // in one run does not read as a growth rate
              run 25_000 |> ignore
              let small = List.min [ run 25_000; run 25_000; run 25_000 ]
              let large = List.min [ run 100_000; run 100_000; run 100_000 ]

              printfn
                  "Phase 298 measurement: ColumnValidator.unique 25,000 rows %.1f ms; 100,000 rows %.1f ms (ratio %.2f)"
                  small
                  large
                  (large / max small 0.1)
              // quadratic indexing made 4x rows cost ~16x; linear costs ~4x
              Expect.isLessThan large (max 2000.0 (10.0 * small)) "100,000 rows within a linear budget"

          testCase "asCheck puts a tree family and a column rule in versioned packs"
          <| fun _ ->
              let pack: Validator.Pack<RNode, string> =
                  { Name = "p"
                    Version = "1"
                    Rules = [ Validator.asCheck nodew (flagAll "A") ] }

              let findings = Validator.runPack pack (RNode.leaf "x" "para" "")
              Expect.equal (findings |> List.map _.Citation) [ "p@1/A" ] "cited"
              Expect.equal (findings |> List.map _.Defect.Family) [ "p/A" ] "the PackRule family id, stamped"

              let colPack: Validator.Pack<Table, string> =
                  { Name = "q"
                    Version = "2"
                    Rules = [ ColumnValidator.asCheck (ColumnValidator.notNull "a") ] }

              Expect.equal
                  (Validator.runPack colPack (table [ "a", IntType, [ Null ] ]) |> List.length)
                  1
                  "a column rule runs as a pack check" ]

// ---- Projection ----

let private ppw: ProjectionWitness<RNode, string, unit> =
    { Tree = nodew
      IdW = idw
      Encode = fun n -> n.Kind + "|" + n.Value
      Snippet = fun n -> n.Value
      ParseBack =
        fun line ->
            if line.Contains "BAD" then
                Error "unreadable"
            else
                Ok [ () ] }

let private projectionTests =
    testList
        "Projection (Phase 298)"
        [ testCase "an id carrying a newline or a space matches ById and Subtree, keyed by idKey"
          <| fun _ ->
              let tree =
                  RNode.node "root" "doc" [ RNode.node "a\nb" "sec" [ RNode.leaf "c d" "para" "x" ] ]

              let byId = Projection.project ppw (Scope.ById "a\nb") tree
              Expect.equal (byId.Lines |> List.map _.IdKey) [ "a\\nb" ] "found, escaped"
              let sub = Projection.project ppw (Scope.Subtree "a\nb") tree
              Expect.equal (sub.Lines |> List.map _.IdKey) [ "a\\nb"; "c\\sd" ] "the slice"

              Expect.isEmpty
                  (Projection.project ppw (Scope.ChangedSince(Projection.snapshot ppw tree)) tree).Lines
                  "unchanged"

              Expect.equal
                  (Projection.unescapeCell (Projection.escapeCell "a\\ b\n\r\tc"))
                  "a\\ b\n\r\tc"
                  "the cell round-trips"

          testCase "ChangedSince sees a reorder and a move, not only content"
          <| fun _ ->
              let tree =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node "p" "sec" [ RNode.leaf "a" "x" "1"; RNode.leaf "b" "x" "2" ]
                        RNode.node "q" "sec" [] ]

              let snap = Projection.snapshot ppw tree

              let reordered =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node "p" "sec" [ RNode.leaf "b" "x" "2"; RNode.leaf "a" "x" "1" ]
                        RNode.node "q" "sec" [] ]

              Expect.equal
                  ((Projection.project ppw (Scope.ChangedSince snap) reordered).Lines
                   |> List.map _.IdKey)
                  [ "p" ]
                  "the parent whose order moved"

              let moved =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node "p" "sec" [ RNode.leaf "a" "x" "1" ]
                        RNode.node "q" "sec" [ RNode.leaf "b" "x" "2" ] ]

              Expect.equal
                  ((Projection.project ppw (Scope.ChangedSince snap) moved).Lines
                   |> List.map _.IdKey)
                  [ "p"; "q" ]
                  "the parent it left and the parent it joined"

          testCase "parseBack reads CRLF text and names the failing line"
          <| fun _ ->
              Expect.equal (Projection.parseBack ppw "a\r\nb\r\n") (Ok [ (); () ]) "CRLF reads as LF"

              match Projection.parseBack ppw "ok\n\nBAD line\n" with
              | Error e -> Expect.stringStarts e "line 3: " "1-based, blank lines counted"
              | Ok _ -> failtest "a bad line was accepted" ]

// ---- AiSurface ----

type private Op = Put of string

let private surface
    (decide: string -> Op -> PolicyDecision)
    (anchors: string list)
    : AiSurfaceWitness<string list, Op, string> =
    { ReadTools = []
      OpKinds = []
      KindOfOp = fun _ -> "put"
      Patterns =
        [ { Name = "p"
            Title = "p"
            PromptAnchors = anchors
            Emit = fun _ -> Ok [ Put "x" ] } ]
      Decide = decide
      Apply = fun (Put s) st -> Ok(s :: st)
      Explain = fun m -> { Message = m; Alternatives = [] } }

let private aiSurfaceTests =
    testList
        "AiSurface (Phase 298)"
        [ testCase "anchor matching is ordinal: no throw on a shorter culture match, no zero-width catch-all"
          <| fun _ ->
              Expect.isFalse (PatternBank.matchesAnchor "é{x}b" "é") "the cursor never overruns the text"
              Expect.isTrue (PatternBank.matchesAnchor "É{x}B" "é and b") "case-insensitive"

              Expect.isFalse
                  (PatternBank.matchesAnchor "\u200B" "hello")
                  "a zero-width segment is a char like any other"

              Expect.isFalse (PatternBank.matchesAnchor "\u00AD{x}" "hello") "so is a soft hyphen"

          testCase "the kit refuses a catch-all anchor"
          <| fun _ ->
              let w = surface (fun _ _ -> PolicyDecision.Allow) [ "{anything}" ]

              let red =
                  Conformance.aiSurfaceKitPolicyLawsAt w (fun r -> Put "x", r) [] 1 4
                  |> List.filter (fun r -> not r.Passed)
                  |> List.choose _.Counterexample

              Expect.isTrue (red |> List.exists (fun c -> c.Contains "has no literal segment")) (sprintf "%A" red)

          testCase "a denial carries the policy's guidance through submit"
          <| fun _ ->
              let w =
                  surface (fun _ _ -> PolicyDecision.denyWith "locked" [ "edit a draft instead" ]) [ "put {x}" ]

              match Proposals.submit w "agent" "t0" None [ Put "a" ] Proposals.Queue.empty [] with
              | Proposals.SubmitDenied g ->
                  Expect.equal g.Alternatives [ "edit a draft instead" ] "alternatives survive"

                  Expect.stringContains
                      (Proposals.renderGuidance g)
                      "- edit a draft instead"
                      "rendered like a rejection"
              | other -> failtestf "expected SubmitDenied, got %A" other

          testCase "approve refuses the author, and refuses an approver the policy denies"
          <| fun _ ->
              let denyAll = surface (fun _ _ -> PolicyDecision.deny "no writes") [ "put {x}" ]
              let q, id = Proposals.propose "agent" "t0" None [ Put "a" ] Proposals.Queue.empty

              match Proposals.approve denyAll "agent" "t1" id q [] with
              | Error(Proposals.SelfApproval(i, "agent")) when i = id -> ()
              | other -> failtestf "expected SelfApproval, got %A" other

              // propose is public: without the re-consult this applied what submit denies
              match Proposals.approve denyAll "reviewer" "t1" id q [] with
              | Error(Proposals.ApprovalDenied(i, g)) when i = id ->
                  Expect.equal g.Message "no writes" "the policy's guidance"
              | other -> failtestf "expected ApprovalDenied, got %A" other

              let parks = surface (fun _ _ -> PolicyDecision.NeedsApproval) [ "put {x}" ]

              match Proposals.approve parks "reviewer" "t1" id q [] with
              | Ok(_, st) -> Expect.equal st [ "a" ] "approving IS the approval a parked op needs"
              | Error e -> failtestf "approval refused: %A" e

          testCase "proposal ids are never re-issued, and a caller-chosen id is refused when held"
          <| fun _ ->
              let q1, a = Proposals.propose "x" "t" None [ Put "1" ] Proposals.Queue.empty
              let q2, b = Proposals.propose "x" "t" None [ Put "2" ] q1
              Expect.equal (a, b) (1, 2) "positional ids for a queue that only grew"
              // a host prunes the first proposal; Length + 1 would re-issue 2
              let pruned =
                  { q2 with
                      Proposals = q2.Proposals |> List.filter (fun p -> p.Id <> a) }

              let _, c = Proposals.propose "x" "t" None [ Put "3" ] pruned
              Expect.equal c 3 "one past the largest held"

              match Proposals.proposeWithId "x" "t" b None [ Put "4" ] q2 with
              | Error(Proposals.ProposeFailure.DuplicateProposal(i, [ 1; 2 ])) when i = b -> ()
              | other -> failtestf "expected DuplicateProposal, got %A" other

              match Proposals.proposeWithId "x" "t" 40 None [ Put "4" ] q2 with
              | Ok q -> Expect.equal (q.Proposals |> List.map _.Id) [ 1; 2; 40 ] "appended under the chosen id"
              | Error e -> failtestf "refused: %A" e ]

// ---- Tree / Ops ----

let private treeTests =
    testList
        "Tree and Ops (Phase 298)"
        [ testCase "Index.tryBuild refuses a repeated id; Index.path terminates on the cyclic index build makes"
          <| fun _ ->
              // `a` holds `b`, which holds a second `a`: ParentOf ends a → b and b → a
              let bad =
                  RNode.node "r" "doc" [ RNode.node "a" "s" [ RNode.node "b" "s" [ RNode.leaf "a" "s" "" ] ] ]

              match Tree.Index.tryBuild nodew idw bad with
              | Error(Tree.RepeatedId "a") -> ()
              | other -> failtestf "expected RepeatedId a, got %A" other

              let ix = Tree.Index.build nodew idw bad
              Expect.isNone (Tree.Index.path idw "b" ix) "a parent cycle has no root path — and the walk ends"
              Expect.isOk (Tree.Index.tryBuild nodew idw (sample ())) "a well-formed tree indexes"

          testCase "updateNode copies only the root-to-target path and shares every other subtree"
          <| fun _ ->
              let left = RNode.node "l" "s" [ RNode.leaf "l1" "p" "x" ]
              let right = RNode.node "r" "s" [ RNode.leaf "r1" "p" "y" ]
              let root = RNode.node "root" "doc" [ left; right ]

              match Tree.updateNode nodew idw "r1" (fun n -> { n with Value = "z" }) root with
              | Some updated ->
                  Expect.isTrue (obj.ReferenceEquals(updated.Children[0], left)) "the untouched sibling is shared"
                  Expect.equal updated.Children[1].Children[0].Value "z" "the target rewritten"
              | None -> failtest "target not found"

          testCase "canApply (MoveNode) answers exactly what apply refuses, on every move over a sample tree"
          <| fun _ ->
              let root = sample ()
              let ids = Tree.ids nodew root

              for t in ids @ [ "ghost" ] do
                  for p in ids @ [ "ghost" ] do
                      let op = MoveNode(t, p)

                      Expect.equal
                          (Ops.canApply nodew idw op root)
                          (Ops.apply nodew idw op root |> Result.map ignore)
                          (sprintf "MoveNode(%s, %s)" t p) ]

// ---- Observer ----

let private observerTests =
    testList
        "Observer (Phase 298)"
        [ testCase "observerLawsAt are green at a reference witness"
          <| fun _ ->
              let w =
                  ObserverWitness.create (fun (x: int) ->
                      [ if x > 5 then
                            "big"

                            if x % 2 = 0 then
                                "even" ])

              for r in Conformance.observerLawsAt w (ConfRng.intBelow 10) 298 100 do
                  Expect.isTrue r.Passed (sprintf "%s — %A" r.Law r.Counterexample)

          testCase "a derivation that throws leaves the state unregistered"
          <| fun _ ->
              let w =
                  ObserverWitness.create (fun (x: int) -> if x < 0 then failwith "bad metric" else [ x ])

              let st, _ = ObserverWitness.register w "ok" 1 None ObserverWitness.empty

              let after =
                  try
                      Some(ObserverWitness.register w "bad" -1 (Some "ok") st)
                  with _ ->
                      None

              Expect.isNone after "the throw surfaces"

              Expect.equal (ObserverWitness.observeTree st "ok" |> List.map _.NodeId) [ "ok" ] "nothing half-registered"

          testCase "observeTree is registration-ordered after an unregister and re-register"
          <| fun _ ->
              let w = ObserverWitness.create (fun (x: int) -> [ x ])

              let st =
                  ObserverWitness.empty
                  |> fun s -> fst (ObserverWitness.register w "root" 0 None s)
                  |> fun s -> fst (ObserverWitness.register w "a" 0 (Some "root") s)
                  |> fun s -> fst (ObserverWitness.register w "b" 0 (Some "root") s)
                  |> ObserverWitness.unregister "a"
                  |> fun s -> fst (ObserverWitness.register w "c" 0 (Some "root") s)
                  |> fun s -> fst (ObserverWitness.register w "a" 0 (Some "root") s)

              Expect.equal
                  (ObserverWitness.observeTree st "root" |> List.map _.NodeId)
                  [ "root"; "b"; "c"; "a" ]
                  "a re-registered node joins the end" ]

[<Tests>]
let tests =
    testList
        "Phase 298 — the smaller seams"
        [ propagationTests
          validatorTests
          projectionTests
          aiSurfaceTests
          treeTests
          observerTests ]
