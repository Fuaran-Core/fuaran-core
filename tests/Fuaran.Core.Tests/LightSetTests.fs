/// Phase 315 — the light set: the small generic exports consumers used to copy, each held to the
/// copy it replaces on a shared fixture, and each operation's law beside the function it mirrors.
module Fuaran.Core.Tests.LightSetTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

// ---------------------------------------------------------------------------------------------
//  the consumer copies, as fixtures — what each export replaces, written the way a consumer wrote it
// ---------------------------------------------------------------------------------------------

/// The chain hash a consumer wrote by hand: the platform's SHA-256 over the UTF-8 of `prev|payload`.
let private platformChainHash (prev: string) (payload: string) : string =
    let bytes = System.Text.Encoding.UTF8.GetBytes(prev + "|" + payload)

    System.Security.Cryptography.SHA256.HashData bytes
    |> Array.map (fun b -> b.ToString("x2"))
    |> String.concat ""

/// The raw FNV-1a value a consumer computed with a 64-bit multiply (the .NET-only shape).
let private wideFnv1a32 (s: string) : uint32 =
    let mutable h = 2166136261UL

    for ch in s do
        h <- (h ^^^ uint64 ch) &&& 0xFFFFFFFFUL
        h <- (h * 16777619UL) &&& 0xFFFFFFFFUL

    uint32 h

/// The compute layer's `cellToken`, as it stands there — it has no `Decimal` case.
let private computeCellToken (c: Cell) : string option =
    match c with
    | Int i -> Some("i:" + string i)
    | Float f ->
        Some(
            "f:"
            + (if System.Double.IsNaN f then "NaN"
               elif System.Double.IsPositiveInfinity f then "Inf"
               elif System.Double.IsNegativeInfinity f then "-Inf"
               else Canon.canonicalFloat f)
        )
    | Bool b -> Some("b:" + (if b then "1" else "0"))
    | Str s -> Some("s:" + s)
    | Date s -> Some("d:" + s)
    | Timestamp s -> Some("t:" + s)
    | Null -> Some "n:"
    | Decimal _ -> None

let private units (codes: int list) : string =
    codes |> List.map (fun c -> string (char c)) |> String.concat ""

/// Inputs crossing SHA-256's padding and block boundaries, the UTF-8 length classes, and ill-formed
/// units (where the copied encoder must replace exactly as the platform does).
let private hashCorpus: string list =
    [ ""
      "a"
      "{\"seq\":0,\"actor\":{\"kind\":\"human\",\"id\":\"x\"},\"op\":{}}"
      "café/日本語/\U0001F600"
      units [ 0xD800 ]
      units [ 0xDFFF; 0x41 ]
      units [ 0xD801; 0xD800 ]
      "a" + units [ 0xD83D ] ]
    @ [ for n in [ 1; 46; 47; 54; 55; 56; 63; 64; 110; 111; 119; 120; 500 ] -> String.replicate n "z" ]

// ---------------------------------------------------------------------------------------------
//  a skeleton-op stream, so `appendMany` and `Ops.applyAll` can be held to one refusal index
// ---------------------------------------------------------------------------------------------

let private skeletonStream: StreamWitness<SkeletonOp<RNode, string>, RNode, Rejection<string>> =
    { Apply = fun op t -> Ops.apply nodew idw op t
      Encode = sprintf "%A"
      Decode = fun _ -> Error "not decoded in this suite" }

let private genOp (tree: RNode) (rng: ConfRng.T) : SkeletonOp<RNode, string> * ConfRng.T =
    let ids = (Tree.preorder nodew tree |> List.map nodew.Id) @ [ "ghost" ]
    let kind, r1 = ConfRng.intBelow 4 rng

    match kind with
    | 0 ->
        let parent, r2 = ConfRng.choose ids r1
        let n, r3 = ConfRng.intBelow 6 r2
        InsertChild(parent, RNode.leaf (sprintf "n%d" n) "para" "v"), r3
    | 1 ->
        let target, r2 = ConfRng.choose ids r1
        RemoveNode target, r2
    | 2 ->
        let target, r2 = ConfRng.choose ids r1
        let np, r3 = ConfRng.choose ids r2
        MoveNode(target, np), r3
    | _ ->
        let parent, r2 = ConfRng.choose ids r1
        let extra, r3 = ConfRng.intBelow 2 r2

        match Tree.tryFind nodew idw parent tree with
        | Some p ->
            let kids = nodew.Children p |> List.map nodew.Id
            let shuffled, r4 = ConfRng.shuffle kids r3
            ReorderChildren(parent, (if extra = 0 then shuffled else shuffled @ [ "x" ])), r4
        | None -> ReorderChildren(parent, []), r3

let private genScript (len: int) (rng: ConfRng.T) : SkeletonOp<RNode, string> list * ConfRng.T =
    let mutable r = rng
    let mutable tree = sample ()
    let ops = ResizeArray()

    for _ in 1..len do
        let op, r' = genOp tree r
        r <- r'
        ops.Add op

        match Ops.apply nodew idw op tree with
        | Ok t -> tree <- t
        | Error _ -> ()

    List.ofSeq ops, r

// ---------------------------------------------------------------------------------------------
//  random footprints for the union law
// ---------------------------------------------------------------------------------------------

let private genSet (rng: ConfRng.T) : Set<string> * ConfRng.T =
    let mutable r = rng
    let mutable acc = Set.empty

    for a in [ "a"; "b"; "c"; "d" ] do
        let k, r' = ConfRng.intBelow 5 r
        r <- r'

        if k = 0 then
            acc <- Set.add a acc

    acc, r

let private genFootprint (rng: ConfRng.T) : Footprint * ConfRng.T =
    let reads, r1 = genSet rng
    let sw, r2 = genSet r1
    let cw, r3 = genSet r2
    let up, r4 = genSet r3

    { Reads = reads
      StructureWrites = sw
      ContentWrites = cw
      UnknownParentWrites = up
      SlotReads = Set.empty
      SlotWrites = Set.empty },
    r4

let private sub (a: Footprint) (b: Footprint) =
    Set.isSubset a.Reads b.Reads
    && Set.isSubset a.StructureWrites b.StructureWrites
    && Set.isSubset a.ContentWrites b.ContentWrites
    && Set.isSubset a.UnknownParentWrites b.UnknownParentWrites

[<Tests>]
let tests =
    testList
        "Phase 315 — the light set"
        [ testList
              "the named hashes"
              [ testCase "Hash.fnv1a32 is the value fnv1a renders, and a 64-bit consumer copy's value"
                <| fun _ ->
                    Expect.equal (Hash.fnv1a32 "") 0x811c9dc5u "the FNV-1a offset basis"
                    Expect.equal (Hash.fnv1a32 "a") 0xe40c292cu "fnv1a \"a\" = e40c292c"

                    for s in hashCorpus do
                        Expect.equal ((Hash.fnv1a32 s).ToString("x8")) (Hash.fnv1a s) "fnv1a renders fnv1a32"
                        Expect.equal (Hash.fnv1a32 s) (wideFnv1a32 s) "the 64-bit copy agrees"

                testCase
                    "OpStream.sha256Hash is byte-equal to the hand-written platform chain hash and to Hash.sha256Hex"
                <| fun _ ->
                    for prev in [ ""; "deadbeef"; String.replicate 64 "a" ] do
                        for s in hashCorpus do
                            let ours = OpStream.sha256Hash prev s
                            Expect.equal ours (platformChainHash prev s) "the consumer copy"
                            Expect.equal ours (Hash.sha256Hex (prev + "|" + s)) "Hash.sha256Hex over the join"

                testCase "a chain hashed with sha256Hash verifies, and a tampered op does not"
                <| fun _ ->
                    let w = Counter.witness

                    match
                        OpStream.appendMany OpStream.sha256Hash w (Human "h") [ Counter.Inc 2; Counter.Dec 1 ] 0 []
                    with
                    | Error e -> failtestf "refused %A" e
                    | Ok(_, recs) ->
                        Expect.isTrue (OpStream.verifyChain OpStream.sha256Hash w recs) "intact"
                        Expect.equal recs.Head.Hash.Length 64 "a SHA-256 hex digest"

                        let forged =
                            recs
                            |> List.mapi (fun i r -> if i = 1 then { r with Op = Counter.Dec 2 } else r)

                        Expect.isFalse (OpStream.verifyChain OpStream.sha256Hash w forged) "tamper detected" ]

          testList
              "stateless and batch append"
              [ testCase "chainHashOf is the hash append stores"
                <| fun _ ->
                    let w = Counter.witness

                    match
                        OpStream.appendMany OpStream.defaultHash w (Human "h") [ Counter.Inc 1; Counter.Inc 4 ] 0 []
                    with
                    | Error e -> failtestf "refused %A" e
                    | Ok(_, recs) ->
                        for r in recs do
                            Expect.equal
                                (OpStream.chainHashOf
                                    OpStream.canonicalConfig
                                    OpStream.defaultHash
                                    r.Seq
                                    r.Actor
                                    (w.Encode r.Op)
                                    r.PrevHash)
                                r.Hash
                                "chainHashOf = the stored hash"

                testCase "appendChainOnly writes append's records, and its chain verifies under verifyChain"
                <| fun _ ->
                    let w = Counter.witness
                    let ops = [ Counter.Inc 3; Counter.Dec 1; Counter.Inc 2 ]

                    for hashFn in [ OpStream.defaultHash; OpStream.sha256Hash ] do
                        let stateless =
                            (OpStream.empty, ops)
                            ||> List.fold (fun recs op -> OpStream.appendChainOnly hashFn w.Encode (Human "h") op recs)

                        match OpStream.appendMany hashFn w (Human "h") ops 0 [] with
                        | Error e -> failtestf "refused %A" e
                        | Ok(_, recs) -> Expect.equal stateless recs "the same records, byte for byte"

                        Expect.isTrue (OpStream.verifyChain hashFn w stateless) "a stateless adapter's chain verifies"

                testCase "appendChainOnly chains an op the domain refuses; replay is where it is refused"
                <| fun _ ->
                    let w = Counter.witness

                    let recs =
                        [ Counter.Inc 1; Counter.Dec 5 ]
                        |> List.fold
                            (fun recs op -> OpStream.appendChainOnly OpStream.defaultHash w.Encode (Human "h") op recs)
                            OpStream.empty

                    Expect.isTrue (OpStream.verifyChain OpStream.defaultHash w recs) "the chain itself is intact"

                    match OpStream.replay w 0 recs with
                    | Error(1, _) -> ()
                    | other -> failtestf "expected replay to refuse record 1, got %A" other

                // The shard's `appendAll` is Phase 296's `appendMany` (same signature, same
                // all-or-nothing indexed refusal); this is the acceptance law it was asked for.
                testCase "appendMany refuses at the index Ops.applyAll would, and otherwise reaches its tree"
                <| fun _ ->
                    let mutable rng = ConfRng.ofSeed 315
                    let mutable refused = 0
                    let mutable accepted = 0

                    for _ in 1..300 do
                        let len, r1 = ConfRng.intBelow 7 rng
                        let ops, r2 = genScript (len + 1) r1
                        rng <- r2

                        match
                            OpStream.appendMany OpStream.defaultHash skeletonStream (Human "h") ops (sample ()) [],
                            Ops.applyAll nodew idw ops (sample ())
                        with
                        | Error(i, e), Error { Applied = j; Rejection = e' } ->
                            refused <- refused + 1
                            Expect.equal i j "the same refusal index"
                            Expect.equal e e' "the same envelope"
                        | Ok(t, recs), Ok t' ->
                            accepted <- accepted + 1
                            Expect.equal t t' "the same tree"
                            Expect.equal (List.length recs) (List.length ops) "one record per op"
                        | a, b -> failtestf "appendMany %A but applyAll %A" a b

                    Expect.isGreaterThan refused 20 "the sample reached refusals"
                    Expect.isGreaterThan accepted 20 "the sample reached acceptances" ]

          testList
              "Footprint builders"
              [ testCase "the builders are Ops.footprint's clauses"
                <| fun _ ->
                    let fp ops = Ops.footprint nodew idw ops

                    Expect.equal
                        (fp [ InsertChild("a", RNode.leaf "n" "para" "v") ])
                        (Footprint.insertUnder "a" "n")
                        "a leaf insert"

                    Expect.equal (fp [ RemoveNode "a1" ]) (Footprint.removeNode "a1") "a remove"
                    Expect.equal (fp [ MoveNode("a1", "b") ]) (Footprint.moveTo "a1" "b") "a move"
                    Expect.equal (fp []) Footprint.empty "the empty script"

                    Expect.equal
                        (fp [ InsertChild("a", RNode.node "n" "section" [ RNode.leaf "m" "para" "v" ]) ])
                        (Footprint.union (Footprint.insertUnder "a" "n") (Footprint.insertUnder "a" "m"))
                        "a subtree insert is the union of one insertUnder per id"

                    let edit = Footprint.contentEdit "x"
                    Expect.isFalse (Ops.independent edit edit) "two edits of one node collide"

                    Expect.isTrue
                        (Ops.independent edit (Footprint.contentEdit "y"))
                        "edits of two nodes are independent"

                testCase "union is the least upper bound, and independence distributes over it (monotone)"
                <| fun _ ->
                    let mutable rng = ConfRng.ofSeed 3150
                    let mutable freed = 0

                    for _ in 1..2000 do
                        let a, r1 = genFootprint rng
                        let b, r2 = genFootprint r1
                        let c, r3 = genFootprint r2
                        rng <- r3
                        let ac = Footprint.union a c

                        Expect.isTrue (sub a ac && sub c ac) "union contains both"
                        Expect.equal (Footprint.union a Footprint.empty) a "empty is the unit"
                        Expect.equal ac (Footprint.union c a) "commutative"

                        Expect.equal
                            (Ops.independent ac b)
                            (Ops.independent a b && Ops.independent c b)
                            "independent (a ∪ c) b ⇔ independent a b ∧ independent c b"

                        // monotone: growing a footprint never frees a pair
                        if Ops.independent ac b then
                            Expect.isTrue (Ops.independent a b) "a sub-footprint of an independent one is independent"
                            freed <- freed + 1

                    Expect.isGreaterThan freed 50 "the sample reached independent unions" ]

          testList
              "Rejection — code, explain, encoder"
              [ testCase "a reorder on a leaf has its own code; a wrong permutation keeps reorderMismatch"
                <| fun _ ->
                    match Ops.apply nodew idw (ReorderChildren("a1", [ "x" ])) (sample ()) with
                    | Error r ->
                        Expect.equal (Rejection.code r) "reorderOnLeaf" "nothing to reorder"

                        Expect.equal
                            (Rejection.explain id RejectionNouns.generic r).Message
                            "'a1' has no children to reorder"
                            "the leaf message"
                    | Ok _ -> failtest "a reorder naming a child a leaf does not hold is refused"

                    match Ops.apply nodew idw (ReorderChildren("a", [ "a1" ])) (sample ()) with
                    | Error r ->
                        Expect.equal (Rejection.code r) "reorderMismatch" "a wrong permutation"

                        Expect.equal
                            (Rejection.explain id RejectionNouns.generic r).Alternatives
                            [ "a1"; "a2" ]
                            "the children a reorder must permute"
                    | Ok _ -> failtest "a short permutation is refused"

                    Expect.isOk
                        (Ops.apply nodew idw (ReorderChildren("a1", [])) (sample ()))
                        "an empty reorder of a leaf is the identity"

                testCase "every case's code, guidance and canonical encoding (the pinned fixture)"
                <| fun _ ->
                    let nouns =
                        { Node = "block"
                          Root = "document root" }

                    let cases: (Rejection<string> * string * string * string list * string) list =
                        [ UnknownNode("z", [ "a"; "b" ]),
                          "unknownNode",
                          "no block 'z' exists",
                          [ "a"; "b" ],
                          """{"$type":"unknownNode","addressable":["a","b"],"target":"z"}"""
                          DuplicateId "a",
                          "duplicateId",
                          "a block 'a' already exists; mint a fresh id",
                          [],
                          """{"$type":"duplicateId","id":"a"}"""
                          CannotRemoveRoot,
                          "cannotRemoveRoot",
                          "the document root cannot be removed or moved",
                          [],
                          """{"$type":"cannotRemoveRoot"}"""
                          WouldNestUnderSelf("a", NestRelation.Self),
                          "wouldNestUnderSelf",
                          "a block cannot be moved under itself ('a')",
                          [],
                          """{"$type":"wouldNestUnderSelf","relation":"self","target":"a"}"""
                          WouldNestUnderSelf("a", NestRelation.Descendant),
                          "wouldNestUnderSelf",
                          "moving 'a' there would nest it inside its own subtree",
                          [],
                          """{"$type":"wouldNestUnderSelf","relation":"descendant","target":"a"}"""
                          NotAContainer("p", "para"),
                          "notAContainer",
                          "'p' (para) cannot hold children",
                          [],
                          """{"$type":"notAContainer","kindTag":"para","target":"p"}"""
                          ReorderMismatch("a", [ "a1"; "a2" ], [ "a1" ]),
                          "reorderMismatch",
                          "a reorder of 'a' must be a permutation of its current children",
                          [ "a1"; "a2" ],
                          """{"$type":"reorderMismatch","expected":["a1","a2"],"got":["a1"],"parent":"a"}"""
                          ReorderMismatch("p", [], [ "x" ]),
                          "reorderOnLeaf",
                          "'p' has no children to reorder",
                          [],
                          """{"$type":"reorderMismatch","expected":[],"got":["x"],"parent":"p"}"""
                          Rejected("DOC-07", "a heading may not be empty"),
                          "DOC-07",
                          "a heading may not be empty",
                          [],
                          """{"$type":"rejected","code":"DOC-07","message":"a heading may not be empty"}"""
                          KeyedPosition("k", "h"),
                          "keyedPosition",
                          "'k' sits in a keyed position of 'h'; vacating or relocating it is a domain edit",
                          [],
                          """{"$type":"keyedPosition","holder":"h","target":"k"}""" ]

                    for r, code, message, alternatives, wire in cases do
                        Expect.equal (Rejection.code r) code (sprintf "code of %A" r)
                        let g = Rejection.explain id nouns r
                        Expect.equal g.Message message (sprintf "message of %A" r)
                        Expect.equal g.Alternatives alternatives (sprintf "alternatives of %A" r)
                        Expect.equal (RejectionCodec.render id r) wire (sprintf "encoding of %A" r)

                        Expect.isOk (Json.parse wire) "the encoding is wire JSON"

                testCase "the guidance renders through the AI surface as before"
                <| fun _ ->
                    let g = Rejection.explain id RejectionNouns.generic (UnknownNode("z", [ "a" ]))

                    Expect.equal
                        (Proposals.renderGuidance g)
                        "no node 'z' exists\nAlternatives:\n- a"
                        "RejectionGuidance moved packages; the surface reads it unchanged" ]

          testList
              "Cell.token and Cell.compare"
              [ testCase "Cell.token is the compute layer's cellToken on every non-decimal cell"
                <| fun _ ->
                    let cells =
                        [ Int 0
                          Int -7
                          Int System.Int32.MaxValue
                          Float nan
                          Float infinity
                          Float -infinity
                          Float 0.0
                          Float -0.0
                          Float 0.1
                          Float 1e21
                          Float 1e-7
                          Float -123.456
                          Bool true
                          Bool false
                          Str ""
                          Str "a:b"
                          Date "2026-10-01"
                          Timestamp "2026-10-01T00:00:00Z"
                          Null ]

                    for c in cells do
                        Expect.equal (Some(Cell.token c)) (computeCellToken c) (sprintf "token of %A" c)

                    Expect.equal (Cell.token (Decimal "1.50")) "m:1.5" "a decimal at its canonical text"
                    Expect.equal (Cell.token (Decimal "1.5")) (Cell.token (Decimal "1.50")) "one value, one token"
                    Expect.equal (Cell.token (Decimal "abc")) "m:abc" "text that is not decimal keeps its text"

                testCase "Cell.compare: NaN last, -0 = 0, floats share a token exactly when they compare equal"
                <| fun _ ->
                    let floats = [ nan; infinity; -infinity; 0.0; -0.0; 1.0; -1.0; 0.1; 1e300 ]

                    for a in floats do
                        Expect.equal
                            (Cell.compare (Float nan) (Float a))
                            (Some(if System.Double.IsNaN a then 0 else 1))
                            "NaN sorts last"

                        for b in floats do
                            let c = Cell.compare (Float a) (Float b) |> Option.get
                            Expect.equal (c = 0) (Cell.token (Float a) = Cell.token (Float b)) "order and token agree"

                            Expect.equal
                                (sign c)
                                (-(sign (Cell.compare (Float b) (Float a) |> Option.get)))
                                "antisymmetric"

                    Expect.equal (Cell.compare (Decimal "1.50") (Decimal "1.5")) (Some 0) "decimals compare exactly"
                    Expect.equal (Cell.compare (Int 2) (Decimal "1.5")) (Some 1) "an int beside a decimal, exactly"

                    Expect.equal
                        (Cell.compare (Float 1.0) (Decimal "1"))
                        None
                        "a float beside a decimal is incomparable"

                    Expect.equal (Cell.compare (Str "a") (Int 1)) None "two families are incomparable"
                    Expect.equal (Cell.compare Null Null) None "null is not ordered"

                testCase "ColumnValidator.unique keys on Cell.token: 1.5 and 1.50 are one key value"
                <| fun _ ->
                    let t =
                        { Schema = [ "d", DecimalType ]
                          Columns = [ Column.ofDecimals "d" (Vector.ofList [ "1.5"; "1.50"; "2" ]) AllValid ] }

                    let defects =
                        ColumnValidator.validate
                            (ColumnValidator.ofRules [ ColumnValidator.unique [ "d" ] ]
                             |> Result.defaultWith (fun e -> failwithf "registry: %A" e))
                            t

                    Expect.equal
                        (defects |> List.map (fun d -> d.Code, d.Node))
                        [ "COL-UNIQUE", Some "d#1" ]
                        "row 1 repeats row 0" ]

          testList
              "Validator.Pack"
              [ testCase "runPack stamps each defect with its PackRule and a pack@version citation, in rule order"
                <| fun _ ->
                    let defect code node : Defect<string> =
                        { Code = code
                          Severity = Severity.Warning
                          Message = code
                          Node = Some node
                          Family = ""
                          Related = [] }

                    let pack: Validator.Pack<RNode, string> =
                        { Name = "house-style"
                          Version = "2.1"
                          Rules =
                            [ { RuleId = "no-empty"
                                Run =
                                  fun root ->
                                      Tree.preorder nodew root
                                      |> List.filter (fun n -> n.Kind = "para")
                                      |> List.map (fun n -> defect "HS-1" n.Id) }
                              { RuleId = "clean"; Run = fun _ -> [] }
                              { RuleId = "root"
                                Run = fun root -> [ defect "HS-3" root.Id ] } ] }

                    let findings = Validator.runPack pack (sample ())

                    Expect.equal
                        (findings
                         |> List.map (fun f -> f.Rule.Pack, f.Rule.RuleId, f.Citation, f.Defect.Node))
                        [ "house-style", "no-empty", "house-style@2.1/no-empty", Some "a1"
                          "house-style", "no-empty", "house-style@2.1/no-empty", Some "a2"
                          "house-style", "no-empty", "house-style@2.1/no-empty", Some "b1"
                          "house-style", "root", "house-style@2.1/root", Some "root" ]
                        "every finding names its rule, in rule order then the rule's own order"

                    Expect.equal (Validator.citation pack "x") "house-style@2.1/x" "the citation form" ]

          testList
              "Actor — an actor names somebody"
              [ testCase "the validating constructors refuse an empty id and admit a named actor"
                <| fun _ ->
                    Expect.equal (Actor.human "") (Error ActorInvalid.EmptyId) "an empty human"
                    Expect.equal (Actor.agent "m" "1" "") (Error ActorInvalid.EmptyId) "an empty agent"
                    Expect.equal (Actor.human "ada") (Ok(Human "ada")) "a person"

                    Expect.equal
                        (Actor.agent "" "" "bot-1")
                        (Ok(Agent("", "", "bot-1")))
                        "model and version may be empty"

                    Expect.equal (Actor.validate (Human "")) (Error ActorInvalid.EmptyId) "validate"

                testCase "the JSONL decoder refuses an empty actor id with ActorInvalid, naming the member"
                <| fun _ ->
                    let w = Counter.witness

                    for actor in [ Human ""; Agent("m", "1", "") ] do
                        let recs =
                            OpStream.appendChainOnly OpStream.defaultHash w.Encode actor (Counter.Inc 1) []

                        match OpStream.fromJsonl w (OpStream.toJsonl w recs) with
                        | Error e -> Expect.stringContains e "the actor in actor has an empty id" "the refusal names it"
                        | Ok _ -> failtestf "an empty %A was read back" actor

                    let named =
                        OpStream.appendChainOnly OpStream.defaultHash w.Encode (Human "ada") (Counter.Inc 1) []

                    Expect.equal
                        (OpStream.fromJsonl w (OpStream.toJsonl w named))
                        (Ok named)
                        "a named actor round-trips" ] ]
