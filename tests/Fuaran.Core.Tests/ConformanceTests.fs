module Fuaran.Core.Tests.ConformanceTests

// Phase 243 — the op-algebra conformance kit, self-proven against the in-repo reference
// witness, plus a deliberately-broken witness whose failure is reproduced from a seed.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.Reference2

// ---- a random tree generator over the reference RNode (unique ids) ----
let genTree (rng: ConfRng.T) : RNode * ConfRng.T =
    let mutable counter = 0
    let mutable r = rng

    let freshId () =
        let id = sprintf "n%d" counter
        counter <- counter + 1
        id

    let rec build depth =
        let id = freshId ()

        if depth <= 0 then
            RNode.leaf id "para" "v"
        else
            let nKids, r' = ConfRng.intBelow 3 r
            r <- r'
            let kids = [ for _ in 1..nKids -> build (depth - 1) ]
            RNode.node id "section" kids

    let t = build 2
    t, r

let private genFresh (existing: Set<string>) (rng: ConfRng.T) : RNode * ConfRng.T =
    let mutable r = rng

    let rec pick () =
        let v, r' = ConfRng.next r
        r <- r'
        let id = sprintf "f%d" (v % 100000)
        if existing.Contains id then pick () else id

    let id = pick ()
    RNode.leaf id "para" "x", r

let opGen: OpGen<RNode, string> =
    { Tree = genTree
      FreshNode = genFresh
      CanHold = None }

// ---- the counter stream witness — the one reference copy, `Reference.Counter` (Phase 296) ----
open Fuaran.Core.Tests.Reference.Counter

let sw: StreamWitness<CounterOp, int, string> = witness

let private genStreamOp (rng: ConfRng.T) : CounterOp * ConfRng.T =
    let kind, r1 = ConfRng.intBelow 2 rng
    let n, r2 = ConfRng.intBelow 5 r1
    (if kind = 0 then Inc n else Dec n), r2

let streamGen: StreamGen<CounterOp, int> = { State0 = 0; Op = genStreamOp }

// ---- Phase 223: the stratified reference generators for the drawn-refusal families ----

/// A `Dec` no reachable counter state can absorb. Every chain these laws build is a handful of
/// `Inc` draws below five, so an overdraw is refused in EVERY state the run can reach.
let overdraw = 1_000_000

/// `streamGen` with its refusal made a STRATUM rather than a coincidence: one draw in three is
/// `Inc` (applies in every state), one is `Dec n` (applies or refuses by the state, as before), and
/// one is an overdraw (refuses in every state). The kit reference generator for `casLaws` and
/// `idempotencyLaws`, whose agreement laws compare a domain refusal only when one is drawn — so a
/// run of the default size reaches both outcomes by the generator's shape, not by the counter
/// happening to sit low. A StreamGen is not told the iteration index, so the stratum is drawn at a
/// fixed rate: over the two hundred iterations the reference runs, the chance that no draw in the
/// arm lands on it is (2/3)^200.
let private genStratifiedStreamOp (rng: ConfRng.T) : CounterOp * ConfRng.T =
    let stratum, r1 = ConfRng.intBelow 3 rng
    let n, r2 = ConfRng.intBelow 5 r1

    match stratum with
    | 0 -> Inc n, r2
    | 1 -> Dec n, r2
    | _ -> Dec overdraw, r2

let stratifiedStreamGen: StreamGen<CounterOp, int> =
    { State0 = 0
      Op = genStratifiedStreamOp }

/// The refusal-free generator every drawn-refusal suite's must-fail case uses: `Inc` only, so no
/// op is ever refused and the family's refused-op guard is the only line that can say so.
let refusalFreeStreamGen: StreamGen<CounterOp, int> =
    { State0 = 0
      Op =
        fun rng ->
            let n, r = ConfRng.intBelow 5 rng
            Inc n, r }

/// The stratified reference generator for `diffContainedLaws`: "section" holds children and "para"
/// is a leaf, and every drawn tree carries a para directly under its root, so the minted probe
/// always has a non-container to graft under unless the four derived ops removed it. Before this
/// the demanding direction of the refusal iff was reached on whichever draws happened to carry a
/// para (100 of 200 at seed 4242).
let containedGen: OpGen<RNode, string> =
    { Tree =
        fun rng ->
            let t, r = genTree rng

            { t with
                Children = t.Children @ [ RNode.leaf "strat-para" "para" "v" ] },
            r
      FreshNode = opGen.FreshNode
      CanHold = Some(fun (n: RNode) -> n.Kind <> "para") }

// ---- Phase 60/65: an in-repo keyed signing sink + a wide collision-resistant HashFn stand-in ----
// GP3: no cryptographic hash ships in Core; these live test-side. `keyedSink` is a keyed FNV/HMAC-style
// stand-in (head-bound: Verify recomputes the keyed tag AND checks the covered head), enough to prove
// the attestation seam's falsification guarantee without a host crypto dependency. `wideHash` is a
// 128-bit FNV-family stand-in — not cryptographic, but wide enough that a bounded birthday search finds
// no collision, so it models the "collision-resistant HashFn" a host wires (SHA-256) for the adversarial
// branch (contrast the 32-bit default FNV-1a, which does collide in-budget — the documented posture).
let private fnv1a (s: string) : string =
    let mutable h = 2166136261u

    for ch in s do
        h <- h ^^^ uint32 ch
        h <- h * 16777619u

    h.ToString("x8")

let keyedSink (key: string) : IAttestationSink =
    let sign (head: string) = fnv1a (key + "|" + head)

    { new IAttestationSink with
        member _.Sign head =
            Some
                { Head = head
                  KeyId = "test-key"
                  Signature = sign head }

        member _.Verify att head =
            att.Head = head && att.Signature = sign head }

let wideHash: HashFn =
    fun prev payload ->
        let s = prev + "|" + payload

        let pass (basis: uint32) (prime: uint32) =
            let mutable h = basis

            for ch in s do
                h <- h ^^^ uint32 ch
                h <- h * prime

            h.ToString("x8")

        pass 2166136261u 16777619u
        + pass 2166136353u 16777639u
        + pass 2166136619u 16777669u
        + pass 2166136721u 16777691u

// ---- a cross-witness composition generator over the reference RNode (Phase 47) ----
// An outer with two independent `para` slots + one value hole, a closed inner, and two open inners
// sharing the hole name "x" at distinct ids (so their re-rooted copies get distinct addresses).
let genComposition (rng: ConfRng.T) : Conformance.CompositionSample<RNode, RNode> * ConfRng.T =
    let v, r1 = ConfRng.intBelow 11 rng // an in-space value for the count hole (0..10)
    let det, r2 = ConfRng.intBelow 4 r1 // vary the inner effect so the join is non-trivial

    let determinism =
        [ Effect.deterministic; Effect.clock; Effect.random; Effect.network ]
        |> List.item det

    let outer =
        RNode.node
            "co"
            "template"
            [ RNode.hole "v1" "field" "count" (ValueHole(IntRange(0, 10)))
              RNode.hole "sa" "region" "a" (SlotHole(Some "para"))
              RNode.hole "sb" "region" "b" (SlotHole(Some "para")) ]

    let openInnerA =
        { RNode.node "ga" "para" [ RNode.hole "xa" "field" "x" (ValueHole AnyString) ] with
            Eff =
                { Host = Pure
                  Determinism = determinism } }

    let openInnerB =
        RNode.node "gb" "para" [ RNode.hole "xb" "field" "x" (ValueHole AnyString) ]

    { Outer = outer
      SlotA = "co/sa"
      SlotB = "co/sb"
      OuterArgs = [ "co/v1", string v ]
      ClosedInner = RNode.leaf "p" "para" "x"
      OpenInnerA = openInnerA
      OpenInnerB = openInnerB
      OpenHoleName = "x"
      OpenHoleArg = "z" },
    r2

// ---- a CROSS-WITNESS composition generator (Phase 51): RNode outer, R2Node inner ----
// The same outer shape as genComposition, but the inners are the second (int-id) reference witness —
// so composeAcross + applyMemo are exercised across a genuinely-distinct witness pair. The R2 inners
// are rooted at Tag "para" so embedToR yields RNode "para" nodes the slots accept.
let genComposition2 (rng: ConfRng.T) : Conformance.CompositionSample<RNode, R2Node> * ConfRng.T =
    let v, r1 = ConfRng.intBelow 11 rng
    let det, r2 = ConfRng.intBelow 4 r1

    let determinism =
        [ Effect.deterministic; Effect.clock; Effect.random; Effect.network ]
        |> List.item det

    let outer =
        RNode.node
            "co"
            "template"
            [ RNode.hole "v1" "field" "count" (ValueHole(IntRange(0, 10)))
              RNode.hole "sa" "region" "a" (SlotHole(Some "para"))
              RNode.hole "sb" "region" "b" (SlotHole(Some "para")) ]

    let openInnerA =
        { R2Node.node 10 "para" [ R2Node.hole 11 "field" "x" (ValueHole AnyString) ] with
            Effect =
                { Host = Pure
                  Determinism = determinism } }

    let openInnerB =
        R2Node.node 20 "para" [ R2Node.hole 21 "field" "x" (ValueHole AnyString) ]

    { Outer = outer
      SlotA = "co/sa"
      SlotB = "co/sb"
      OuterArgs = [ "co/v1", string v ]
      ClosedInner = R2Node.leaf 1 "para" "x"
      OpenInnerA = openInnerA
      OpenInnerB = openInnerB
      OpenHoleName = "x"
      OpenHoleArg = "z" },
    r2

// ---- a memo sample generator over the reference RNode (Phase 49) ----
// A pure template + two distinct full param-sets (count differs), and an effecting (non-deterministic)
// variant — so applyMemo hits, misses, and bypasses are all exercised.
let genMemo (rng: ConfRng.T) : Conformance.MemoSample<RNode> * ConfRng.T =
    let v1, r1 = ConfRng.intBelow 11 rng // count in [0,10]
    let v2, r2 = ConfRng.intBelow 11 r1
    let alt = if v2 = v1 then (v1 + 1) % 11 else v2 // guarantee ArgsAlt ≠ Args
    let det, r3 = ConfRng.intBelow 3 r2
    let determinism = [ Effect.clock; Effect.random; Effect.network ] |> List.item det // a non-deterministic source

    let fullArgs c =
        Map.ofList
            [ "tpl/t", ValueArg "T"
              "tpl/c", ValueArg(string c)
              "tpl/s", SlotArg(RNode.leaf "p" "para" "x") ]

    let effFn =
        { template () with
            Eff =
                { Host = Pure
                  Determinism = determinism } }

    { PureFn = template ()
      Args = fullArgs v1
      ArgsAlt = fullArgs alt
      EffectingFn = effFn
      EffectingArgs = fullArgs v1 },
    r3

[<Tests>]
let conformanceFacadeTests =
    testList
        "Conformance"
        [ testCase "the reference witness certifies green across the algebra + stream laws"
          <| fun _ ->
              let report =
                  Conformance.certify nodew idw opGen sw streamGen OpStream.defaultHash 12345 200

              if not report.AllPassed then
                  let fails =
                      report.Results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "reference witness failed conformance:\n%s" (String.concat "\n" fails)

              Expect.equal
                  (report.Results |> List.length)
                  24
                  // Phase 302: the diff laws gained their non-identity guard and certify folds the
                  // reducer's accepted/refused guards (the stream side's refusals). Before that,
                  // algebra gained the insert-uniqueness law in Phase 137, the
                  // WellFormed-preservation law in Phase 139, and — Phase 220 — its two
                  // accepted/refused adequacy guards; the stream laws gained their accepted-op
                  // and tampered-chain guards in Phase 245; the witness laws gained the
                  // identities-agree law in Phase 290; the stream laws gained the JSONL round
                  // trip in Phase 301.
                  "witness (5) + algebra (5 + 2 guards) + diff (3 + 1 guard) + stream (4 + 2 guards) + reducer (2 guards) laws reported" ]

// ---- Phase 48: artifact-function property-verification ----

/// The domain validity oracle the verifier drives: any node whose Value parses as an int > 5 is a
/// `Severity.Error` defect. The "rule" a correct-by-construction function must respect for every
/// binding — registered into a real `Validator.Registry` so `verifyFunction` drives the framework.
let countReg: Validator.Registry<RNode, string> =
    Validator.ofFamilies
        [ Validator.perNode "count≤5" (fun _ n ->
              match System.Int32.TryParse n.Value with
              | true, v when v > 5 ->
                  [ { Code = "CNT001"
                      Severity = Severity.Error
                      Message = sprintf "count %d exceeds 5" v
                      Node = Some n.Id
                      Family = ""
                      Related = [] } ]
              | _ -> []) ]
    |> Result.defaultWith (fun e -> failwithf "registry: %A" e)

/// A full template whose `count` hole ranges over [lo, hi]; title + body are fixed-shape holes.
let tplCount (lo, hi) =
    { RNode.node
          "tpl"
          "template"
          [ RNode.hole "t" "field" "title" (ValueHole(StringLen(1, 20)))
            RNode.hole "c" "field" "count" (ValueHole(IntRange(lo, hi)))
            RNode.hole "s" "region" "body" (SlotHole(Some "para")) ] with
        Eff = Effect.pureDeterministic }

/// Draw an in-space value for a value/repeat hole's space (covers the spaces these templates use).
let private sampleInSpace (space: ValueSpace) (rng: ConfRng.T) : string * ConfRng.T =
    match space with
    | IntRange(lo, hi) ->
        let v, r = ConfRng.intBelow (hi - lo + 1) rng
        string (lo + v), r
    | StringLen(lo, _) -> String.replicate (max 1 lo) "a", rng
    | Enum xs -> ConfRng.choose xs rng
    | _ -> "x", rng

/// A valid param-set generator: fill every data hole with an in-space value / a para slot.
let genParamsFor (fn: RNode) (rng: ConfRng.T) : Map<string, Arg<RNode>> * ConfRng.T =
    let holes =
        artw.Holes fn
        |> List.filter (fun h ->
            match h.Kind with
            | ActionHole _ -> false
            | _ -> true)

    let mutable r = rng

    let args =
        holes
        |> List.map (fun h ->
            match h.Kind with
            | ValueHole space
            | RepeatHole space ->
                let v, r' = sampleInSpace space r
                r <- r'
                h.Addr, ValueArg v
            | SlotHole _ -> h.Addr, SlotArg(RNode.leaf "p" "para" "x")
            | ActionHole _ -> h.Addr, ValueArg "") // unreachable (filtered above)

    Map.ofList args, r

// ---------------------------------------------------------------------------
//  Phase 189 — `Conformance.keyedChildrenLaws`, certified against the reference
//  witness EXTENDED with a keyed slot, and against the plain one that has none.
// ---------------------------------------------------------------------------

/// The reference node with one addition: a name-keyed CASE TABLE, which `Children` does not
/// report. It is the shape `README.md` names when it says "a case table, a fallback slot, a named
/// alternative, an argument position" — a node the domain holds and the engine cannot see. It is a
/// type of its own rather than a field on `RNode` because every other family in this suite
/// certifies the surface witness, and giving that witness an invisible position would change what
/// those runs are about.
type KNode =
    { Id: string
      Kind: string
      Children: KNode list
      Cases: (string * KNode) list }

let knodew: NodeWitness<KNode, string> =
    { Id = fun n -> n.Id
      KindTag = fun n -> n.Kind
      Children = fun n -> n.Children
      ReplaceChildren = fun n cs -> { n with Children = cs } }

/// A drawn tree carries unique ids and SOMETIMES a keyed case, so the family's declaration count
/// is exercised by the draw — but never a collision: a generator's contract is a fresh id, which
/// is exactly why the two collision laws build their subjects instead.
let private genKTree (rng: ConfRng.T) : KNode * ConfRng.T =
    let mutable counter = 0
    let mutable r = rng

    let freshId () =
        let id = sprintf "k%d" counter
        counter <- counter + 1
        id

    let rec build depth =
        let id = freshId ()

        let kids =
            if depth <= 0 then
                []
            else
                let nKids, r1 = ConfRng.intBelow 3 r
                r <- r1
                [ for _ in 1..nKids -> build (depth - 1) ]

        let caseRoll, r2 = ConfRng.intBelow 2 r
        r <- r2

        let cases =
            if caseRoll = 0 then
                []
            else
                let cid = freshId ()

                [ "default",
                  { Id = cid
                    Kind = "case"
                    Children = []
                    Cases = [] } ]

        { Id = id
          Kind = (if List.isEmpty kids then "para" else "section")
          Children = kids
          Cases = cases }

    build 2, r

let private genKFresh (existing: Set<string>) (rng: ConfRng.T) : KNode * ConfRng.T =
    let mutable r = rng

    let rec pick () =
        let v, r' = ConfRng.next r
        r <- r'
        let id = sprintf "f%d" (v % 100000)
        if existing.Contains id then pick () else id

    { Id = pick ()
      Kind = "para"
      Children = []
      Cases = [] },
    r

let kGen: OpGen<KNode, string> =
    { Tree = genKTree
      FreshNode = genKFresh
      CanHold = None }

/// The domain's OWN full walk: `Children` AND the case table. This is the check the claims ladder
/// makes the domain's obligation — the one `witness-surface-scope` says no kit law could reach
/// until this family gave the domain a way to declare the positions.
let rec private fullWalk (n: KNode) : string list =
    (n.Id :: (n.Children |> List.collect fullWalk))
    @ (n.Cases |> List.collect (fun (_, c) -> fullWalk c))

let idsUnique (root: KNode) =
    let ks = fullWalk root
    List.length (List.distinct ks) = List.length ks

let private caseNode (id: string) =
    { Id = id
      Kind = "case"
      Children = []
      Cases = [] }

let keyw: KeyedWitness<KNode, string> =
    { Surface = "the reference domain's full walk (Children + the case table)"
      KeyedChildren = fun n -> n.Cases |> List.map snd
      ReplaceKeyedChildren =
        fun n ks ->
            if List.length ks = List.length n.Cases then
                { n with
                    Cases = List.map2 (fun (label, _) c -> label, c) n.Cases ks }
            else
                n
      PlaceKeyedChild =
        fun n id ->
            Some
                { n with
                    Cases = n.Cases @ [ id, caseNode id ] }
      IdsUnique = idsUnique }

// ---------------------------------------------------------------------------
//  Phase 211 — `Conformance.propagationEvaluatorLaws`, certified against an
//  in-repo FORMULA SHEET. It is the family's adequacy witness because no adopter
//  evaluator exists yet: measured 2026-09-24, nothing outside this repository
//  calls `Propagation.eval` / `evalFrom`, and inside it every caller is a test's
//  toy. The sheet is not one, in the ways the family distinguishes: it FAILS (a
//  division by zero, a read that answers nothing), and `IfPos` reads only the
//  branch it takes, so what a node asks for is a subset of what it declares and
//  moves with its inputs.
// ---------------------------------------------------------------------------

/// A formula, as a spreadsheet cell holds one.
type SheetFormula =
    | Lit of int
    | Ref of string
    | Add of SheetFormula * SheetFormula
    | Mul of SheetFormula * SheetFormula
    | Div of SheetFormula * SheetFormula
    | IfPos of SheetFormula * SheetFormula * SheetFormula

/// Cells `c0` … `cN`, each holding a formula over earlier cells — and, now and then, over a cell
/// nobody holds (`zz`), which reads as `#REF!`.
type RefSheet = Map<string, SheetFormula>

let rec refsOf (f: SheetFormula) : Set<string> =
    match f with
    | Lit _ -> Set.empty
    | Ref r -> Set.singleton r
    | Add(a, b)
    | Mul(a, b)
    | Div(a, b) -> Set.union (refsOf a) (refsOf b)
    | IfPos(c, t, e) -> Set.unionMany [ refsOf c; refsOf t; refsOf e ]

let rec private evalFormula (resolve: string -> int option) (f: SheetFormula) : Result<int, string> =
    let both a b k =
        match evalFormula resolve a with
        | Error e -> Error e
        | Ok x ->
            match evalFormula resolve b with
            | Error e -> Error e
            | Ok y -> k x y

    match f with
    | Lit n -> Ok n
    | Ref r ->
        match resolve r with
        | Some v -> Ok v
        | None -> Error("#REF! " + r)
    | Add(a, b) -> both a b (fun x y -> Ok(x + y))
    | Mul(a, b) -> both a b (fun x y -> Ok(x * y))
    | Div(a, b) -> both a b (fun x y -> if y = 0 then Error "#DIV/0!" else Ok(x / y))
    | IfPos(c, t, e) ->
        match evalFormula resolve c with
        | Error m -> Error m
        | Ok x ->
            if x > 0 then
                evalFormula resolve t
            else
                evalFormula resolve e

/// The sheet's cell evaluator — what it hands `Propagation.eval` / `evalFrom`.
let sheetEvalNode (s: RefSheet) (resolve: string -> int option) (id: string) : Result<int, string> =
    match Map.tryFind id s with
    | Some f -> evalFormula resolve f
    | None -> Error("no cell " + id)

let rec private genFormula (k: int) (depth: int) (r: ConfRng.T) : SheetFormula * ConfRng.T =
    let pick, r = ConfRng.intBelow (if depth = 0 then 3 else 7) r

    let lit r =
        let n, r = ConfRng.intBelow 10 r
        Lit n, r

    let binary mk r =
        let a, r = genFormula k (depth - 1) r
        let b, r = genFormula k (depth - 1) r
        mk (a, b), r

    match pick with
    | 0
    | 1 ->
        if k = 0 then
            lit r
        else
            let d, r = ConfRng.intBelow 20 r

            if d = 0 then
                Ref "zz", r
            else
                let j, r = ConfRng.intBelow k r
                Ref(sprintf "c%d" j), r
    | 2 -> lit r
    | 3 -> binary Add r
    | 4 -> binary Mul r
    | 5 -> binary Div r
    | _ ->
        let c, r = genFormula k (depth - 1) r
        let t, r = genFormula k (depth - 1) r
        let e, r = genFormula k (depth - 1) r
        IfPos(c, t, e), r

let genSheet (r: ConfRng.T) : RefSheet * ConfRng.T =
    let extra, r = ConfRng.intBelow 6 r

    [ 0 .. extra + 1 ]
    |> List.fold
        (fun (s, r) k ->
            let f, r = genFormula k 2 r
            Map.add (sprintf "c%d" k) f s, r)
        (Map.empty, r)

/// An edit that keeps the formula's references: the first literal bumped.
let rec private bumpLit (f: SheetFormula) : SheetFormula option =
    let pair mk a b =
        match bumpLit a with
        | Some a' -> Some(mk (a', b))
        | None -> bumpLit b |> Option.map (fun b' -> mk (a, b'))

    match f with
    | Lit n -> Some(Lit((n + 1) % 10))
    | Ref _ -> None
    | Add(a, b) -> pair Add a b
    | Mul(a, b) -> pair Mul a b
    | Div(a, b) -> pair Div a b
    | IfPos(c, t, e) ->
        match bumpLit c with
        | Some c' -> Some(IfPos(c', t, e))
        | None -> pair (fun (t', e') -> IfPos(c, t', e')) t e

/// An edit that keeps the formula's references: the root operator turned, or an `IfPos`'s branches
/// swapped — which moves the reads the cell ASKS for without moving the ones it declares.
let private swapOp (f: SheetFormula) : SheetFormula =
    match f with
    | Add(a, b) -> Mul(a, b)
    | Mul(a, b) -> Div(a, b)
    | Div(a, b) -> Add(a, b)
    | IfPos(c, t, e) -> IfPos(c, e, t)
    | other -> other

/// One cell edited, and the id of the cell edited. Seven edits in eight keep the cell's references:
/// a literal bumped, an operator turned, or the formula divided by zero. The division is there so
/// that an evaluation which SUCCEEDED before the edit fails after it, which is the whole-`Result`
/// arm the agreement law must meet (a failure already present before the edit leaves no prior to
/// replay from, and measured without it the arm was reached twice in two hundred). One edit in
/// eight rewrites the formula outright and so may MOVE the dependency map, which the family checks
/// for honesty and deliberately does not replay.
let sheetEdit (s: RefSheet) (r: ConfRng.T) : RefSheet * string * ConfRng.T =
    let id, r = ConfRng.choose (s |> Map.toList |> List.map fst) r
    let f = Map.find id s
    let kind, r = ConfRng.intBelow 8 r

    let f', r =
        match kind with
        | 0
        | 1
        | 2 -> (bumpLit f |> Option.defaultWith (fun () -> swapOp f)), r
        | 3
        | 4 ->
            let g = swapOp f
            (if g = f then bumpLit f |> Option.defaultValue f else g), r
        | 5
        | 6 -> Div(f, Lit 0), r
        | _ -> genFormula (int (id.Substring 1)) 2 r

    Map.add id f' s, id, r

/// The formula sheet as an evaluator witness — the family's in-repo adopter. It names, as its
/// change set, exactly the cell it edited: the honest answer.
let sheetw: EvaluatorWitness<RefSheet, int> =
    { Surface = "the reference formula sheet's cell evaluator"
      Model = genSheet
      Deps = fun s -> s |> Map.map (fun _ f -> refsOf f)
      EvalNode = sheetEvalNode
      Change =
        fun s r ->
            let s', id, r = sheetEdit s r
            (s', Set.singleton id), r }

// ---------------------------------------------------------------------------
//  Phase 220 — the refusable base-run families are `Guarded` over accepted / refused
// ---------------------------------------------------------------------------

/// A lone leaf that holds nothing. The ops are the KIT's (`genOp`), not the domain's, and over any
/// tree `genOp` draws a reorder (always accepted) and a remove of the root (always refused), so
/// across a real run opAlgebra reaches both sides whatever the domain generates — measured in
/// Phase 220, and the reason the guard's teeth are shown at ONE iteration: a run too short to have
/// reached both sides is exactly the run the guard exists to refuse. No holder, so no built arm.
let loneLeafGen: OpGen<RNode, string> =
    { Tree = fun rng -> RNode.leaf "only" "para" "v", rng
      FreshNode = genFresh
      CanHold = Some(fun _ -> false) }

let guardNamed (family: string) (side: string) (results: LawResult list) =
    results
    |> List.find (fun r ->
        r.Law = SampleAdequacy.lawPrefix family
                + "the sample reached every "
                + side
                + " the laws distinguish")

let subjectOf (results: LawResult list) =
    results
    |> List.filter (fun r -> not (r.Law.StartsWith SampleAdequacy.guardOpening))

[<Tests>]
let refusableFamilyTests =
    testList
        "Conformance.refusableFamilies"
        [ testCase "the reference witness reaches both sides of opAlgebra and reducer"
          <| fun _ ->
              for family, results in
                  [ "Conformance.opAlgebra", Conformance.opAlgebra nodew idw opGen 999 200
                    "Conformance.reducer", Conformance.reducer sw.Apply streamGen None 314 200 ] do
                  // Phase 297 — opAlgebra's accepted-op guard also demands every op kind.
                  let accepted =
                      if family = "Conformance.opAlgebra" then
                          "accepted op and op kind"
                      else
                          "accepted op"

                  for side in [ accepted; "refused op" ] do
                      let g = guardNamed family side results
                      Expect.isTrue g.Passed (sprintf "%s: %s — %A" family side g.Counterexample)

          testCase "go-red: certify's verdict moves with opAlgebra's guard"
          <| fun _ ->
              // Phase 297 — a witness under which NO node can hold children never asserts the three
              // `ReplaceChildren` laws, and those cells are strict (no guard counts holders), so
              // `certify` would now stop at the witness laws, honestly. The leaf is a holder here so
              // the run gets as far as the algebra guard this test is about.
              let report =
                  Conformance.certify
                      nodew
                      idw
                      { loneLeafGen with CanHold = None }
                      sw
                      streamGen
                      OpStream.defaultHash
                      12345
                      1

              let redGuards =
                  report.Results
                  |> List.filter (fun r ->
                      not r.Passed
                      && r.Law.StartsWith(SampleAdequacy.lawPrefix "Conformance.opAlgebra"))

              Expect.isFalse report.AllPassed "a run that reached one side of apply no longer certifies"
              Expect.isNonEmpty redGuards "and the red line is opAlgebra's guard, naming the starved side" ]

// ---------------------------------------------------------------------------
//  Phase 245 — `streamLaws` guards its sample, and an aggregate's pass carries its counts
// ---------------------------------------------------------------------------

/// Every op this generator draws is an overdraw no reachable counter state absorbs, so every
/// iteration's chain stays EMPTY. The three stream laws hold over an empty chain — an intact empty
/// chain verifies, replaying nothing re-derives `State0`, and there is no op to tamper — which is
/// the vacuous sample a consumer measured green at 0.30.0.
let allRefusedGen: StreamGen<CounterOp, int> =
    { State0 = 0
      Op = fun rng -> Dec overdraw, rng }

[<Tests>]
let streamAdequacyFacadeTests =
    testList
        "Conformance.streamAdequacy"
        [ testCase "go-red: certifyStream and certify go RED over a generator that refuses every op"
          <| fun _ ->
              let redStreamGuards (report: ConformanceReport) =
                  report.Results
                  |> List.filter (fun r ->
                      not r.Passed
                      && r.Law.StartsWith(SampleAdequacy.lawPrefix "Conformance.streamLaws"))

              let streamOnly =
                  Conformance.certifyStream sw allRefusedGen OpStream.defaultHash 271 200

              Expect.isFalse streamOnly.AllPassed "certifyStream no longer certifies an empty-chain run"
              Expect.equal (List.length (redStreamGuards streamOnly)) 2 "both streamLaws sides are named"

              let whole =
                  Conformance.certify nodew idw opGen sw allRefusedGen OpStream.defaultHash 4242 200

              Expect.isFalse whole.AllPassed "certify no longer certifies an empty-chain run"
              Expect.equal (List.length (redStreamGuards whole)) 2 "and the red lines are streamLaws' guards"

          testCase "an aggregate's pass path carries its counts, through SampleAdequacy.cases"
          <| fun _ ->
              // The aggregate returns every constituent family's laws, guards included, so the
              // census derivation reads it as it reads one family: subject assertions times the
              // iterations the aggregate was driven over, and every starved side by name.
              let klass = Guarded [ "accepted"; "refused"; "tampered chain" ]

              let green = Conformance.certifyStream sw streamGen OpStream.defaultHash 271 200
              Expect.isTrue green.AllPassed "the reference domain certifies green"

              let counted =
                  SampleAdequacy.cases "Conformance.certifyStream" klass 200 green.Results

              Expect.equal counted.Cases 1200 "reducer (2) + streamLaws (4) subject laws, over 200 iterations"
              Expect.isEmpty counted.Starved "and nothing was starved"
              Expect.equal (SampleAdequacy.renderCases counted) "1200" "a green run renders its count"

              let whole =
                  Conformance.certify nodew idw opGen sw streamGen OpStream.defaultHash 4242 200

              Expect.isTrue whole.AllPassed "certify is green at the reference witness"

              let wholeCounted =
                  SampleAdequacy.cases "Conformance.certify" klass 200 whole.Results

              Expect.equal
                  wholeCounted.Cases
                  (200 * List.length (subjectOf whole.Results))
                  "certify's count is every subject law it reported, over its iterations"

              Expect.isFalse (SampleAdequacy.isVacuous wholeCounted) "and it is not vacuous"

              let starved =
                  SampleAdequacy.cases
                      "Conformance.certifyStream"
                      klass
                      200
                      (Conformance.certifyStream sw allRefusedGen OpStream.defaultHash 271 200).Results

              Expect.isTrue (SampleAdequacy.isVacuous starved) "a starved aggregate reads as vacuous"

              Expect.isTrue
                  (starved.Starved |> List.exists (fun d -> d.Contains "tampered chain"))
                  (sprintf "and names the stream side it starved: %A" starved.Starved) ]
