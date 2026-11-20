/// A minimal reference domain that instantiates every Fuaran.Core witness. It stands
/// in for a real domain (UI / Calc / Doc / …) so the generic functions can be exercised
/// in conformance tests WITHOUT depending on any domain workspace — exactly the
/// "build core end-to-end, don't substitute into the domains yet" posture.
module Fuaran.Core.Tests.Reference

open Fuaran.Core

/// A tiny reference node: string ids (the string-id side of the identity axis), a kind
/// tag, an optional leaf value, an optional declared hole, an effect class, and children.
/// `BoundSlot` is the name of the slot this node's subtree was bound into (Phase 712, D281):
/// `bind` records it on the slot node when it wires a `SlotArg`, and `holesOf` reads the recorded
/// names of a hole's ancestors into its `Slots`.
type RNode =
    { Id: string
      Kind: string
      Value: string
      Hole: HoleKind option
      HoleName: string
      Eff: EffectClass
      Children: RNode list
      BoundSlot: string option }

module RNode =

    let node id kind children =
        { Id = id
          Kind = kind
          Value = ""
          Hole = None
          HoleName = ""
          Eff = Effect.pureDeterministic
          Children = children
          BoundSlot = None }

    let leaf id kind value =
        { Id = id
          Kind = kind
          Value = value
          Hole = None
          HoleName = ""
          Eff = Effect.pureDeterministic
          Children = []
          BoundSlot = None }

    let hole id kind holeName hk =
        { Id = id
          Kind = kind
          Value = ""
          Hole = Some hk
          HoleName = holeName
          Eff = Effect.pureDeterministic
          Children = []
          BoundSlot = None }

// ---- the witnesses ----

let idw: IdWitness<string> =
    { ToString = id
      OfString = id
      Equals = (fun a b -> a = b) }

let nodew: NodeWitness<RNode, string> =
    { Id = fun n -> n.Id
      KindTag = fun n -> n.Kind
      Children = fun n -> n.Children
      ReplaceChildren = fun n cs -> { n with Children = cs } }

/// Enumerate declared holes with their absolute lexical address (id-path) — the hygiene
/// surface. Two same-named holes get distinct addresses by construction. Each hole's `Slots` are
/// the slot names its ancestors recorded when they were bound (`BoundSlot`), outermost first, so
/// two same-named holes composed through two slots have distinct qualified names (Phase 712).
let holesOf (root: RNode) : HoleDecl list =
    let rec walk (slots: string list) (n: RNode) : HoleDecl list =
        let own =
            match n.Hole with
            | Some hk ->
                let addr =
                    match Tree.path nodew idw n.Id root with
                    | Some ids -> String.concat "/" ids
                    | None -> n.Id

                [ { Addr = addr
                    Name = n.HoleName
                    Kind = hk
                    Slots = slots } ]
            | None -> []

        let below =
            match n.BoundSlot with
            | Some s -> slots @ [ s ]
            | None -> slots

        own @ List.collect (walk below) n.Children

    walk [] root

/// Lower a hole binding to the reference domain's "op": a value sets the node's leaf
/// value; a slot wires the inner tree under the slot node and records the slot's name on it
/// (`BoundSlot`, Phase 712). Clears the hole (bound).
let bind (addr: string) (arg: Arg<RNode>) (root: RNode) : Result<RNode, string> =
    let targetId = addr.Split('/') |> Array.last

    let upd (n: RNode) =
        match arg with
        | ValueArg s -> { n with Value = s; Hole = None }
        | SlotArg sub ->
            { n with
                Children = [ sub ]
                Hole = None
                BoundSlot = Some n.HoleName }

    match Tree.updateNode nodew idw targetId upd root with
    | Some r -> Ok r
    | None -> Error("no node at " + targetId)

let artw: ArtifactWitness<RNode, string> =
    { Tree = nodew
      IdW = idw
      Holes = holesOf
      Effect = _.Eff
      Bind = bind }

/// A canonical per-node content encoder for `Tree.encodeHash` — the node's LOCAL content (id, kind,
/// value, hole, effect), NOT its children (encodeHash walks preorder, applying this per node). The
/// `'Node -> string` parameter `Function.applyMemo` (Phase 49) takes for its content-addressed key.
let encNode (n: RNode) : string =
    // A recorded slot name is local content too (it moves the holes' `Slots`); a node with none
    // encodes as it did before Phase 712.
    String.concat
        "|"
        ([ n.Id; n.Kind; n.Value; n.HoleName; sprintf "%A" n.Hole; sprintf "%A" n.Eff ]
         @ (n.BoundSlot |> Option.map (fun s -> "slot:" + s) |> Option.toList))

// ---- sample trees ----

/// root(doc) [ a(section)[a1,a2], b(section)[b1] ]
let sample () =
    RNode.node
        "root"
        "doc"
        [ RNode.node "a" "section" [ RNode.leaf "a1" "para" "x"; RNode.leaf "a2" "para" "y" ]
          RNode.node "b" "section" [ RNode.leaf "b1" "para" "z" ] ]

/// A parameterised template: a title (string), a count (int), and a body slot (para).
let template () =
    { RNode.node
          "tpl"
          "template"
          [ RNode.hole "t" "field" "title" (ValueHole(StringLen(1, 20)))
            RNode.hole "c" "field" "count" (ValueHole(IntRange(0, 10)))
            RNode.hole "s" "region" "body" (SlotHole(Some "para")) ] with
        Eff = Effect.pureDeterministic }

/// Two holes with the SAME name "x" at DIFFERENT addresses (root/g1/gx1 vs root/g2/gx2)
/// — the hygiene case: binding by absolute address must not let one capture the other.
let twoSameName () =
    RNode.node
        "root"
        "doc"
        [ RNode.node "g1" "group" [ RNode.hole "gx1" "field" "x" (ValueHole AnyString) ]
          RNode.node "g2" "group" [ RNode.hole "gx2" "field" "x" (ValueHole AnyString) ] ]

/// The reference STREAM domain (Phase 296) — a counter with inc/dec ops, where a `Dec` below zero is
/// the domain's rejection. One copy: the linear stream, idempotent-append, DAG and conformance
/// suites each carried their own until Phase 296, and the DAG's decoded an unknown op kind as `Inc`.
/// Encoded through the Core.Wire helpers and decoded through its combinators, so the suites exercise
/// the real wire surface too; the decoder REFUSES an op kind it does not know.
module Counter =

    type CounterOp =
        | Inc of int
        | Dec of int

    let encode (op: CounterOp) : string =
        match op with
        | Inc n -> Json.render (Json.kindObj "inc" [ "n", JInt n ])
        | Dec n -> Json.render (Json.kindObj "dec" [ "n", JInt n ])

    let decode (s: string) : Result<CounterOp, string> =
        let parsed =
            Json.parse s
            |> Result.bind (fun el ->
                Decoder.describing (Decoder.field "kind" Decoder.str) el
                |> Result.bind (fun k ->
                    Decoder.describing (Decoder.field "n" Decoder.int) el
                    |> Result.map (fun n -> k, n)))

        match parsed with
        | Ok("inc", n) -> Ok(Inc n)
        | Ok("dec", n) -> Ok(Dec n)
        | Ok(k, _) -> Error("unknown op kind: " + k)
        | Error e -> Error e

    let apply (op: CounterOp) (st: int) : Result<int, string> =
        match op with
        | Inc n -> Ok(st + n)
        | Dec n -> if st - n < 0 then Error "would go negative" else Ok(st - n)

    let witness: StreamWitness<CounterOp, int, string> =
        { Apply = apply
          Encode = encode
          Decode = decode }

/// A DAG node a test builds from nodes it just appended cannot be refused (Phase 296: `Dag.append` /
/// `Dag.merge` return `Result`); a refusal fails the test with the fault's own text. The write's
/// `Written` (Phase 529) is spelled back as the id and the DAG a fixture binds.
let built (r: Result<Dag.Written<'Op>, DagAppendFault>) : string * Dag.T<'Op> =
    match r with
    | Ok w -> w.Id, w.Dag
    | Error f -> failwith ("the DAG refused a node the test built: " + DagAppendFault.toString f)
