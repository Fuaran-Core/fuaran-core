/// A minimal reference domain that instantiates every Fuaran.Core witness. It stands
/// in for a real domain (UI / Calc / Doc / …) so the generic functions can be exercised
/// in conformance tests WITHOUT depending on any domain workspace — exactly the
/// "build core end-to-end, don't substitute into the domains yet" posture.
module Fuaran.Core.Tests.Reference

open Fuaran.Core

/// A tiny reference node: string ids (the string-id side of the identity axis), a kind
/// tag, an optional leaf value, an optional declared hole, an effect class, and children.
type RNode =
    { Id: string
      Kind: string
      Value: string
      Hole: HoleKind option
      HoleName: string
      Eff: EffectClass
      Children: RNode list }

module RNode =

    let node id kind children =
        { Id = id
          Kind = kind
          Value = ""
          Hole = None
          HoleName = ""
          Eff = Effect.pureDeterministic
          Children = children }

    let leaf id kind value =
        { Id = id
          Kind = kind
          Value = value
          Hole = None
          HoleName = ""
          Eff = Effect.pureDeterministic
          Children = [] }

    let hole id kind holeName hk =
        { Id = id
          Kind = kind
          Value = ""
          Hole = Some hk
          HoleName = holeName
          Eff = Effect.pureDeterministic
          Children = [] }

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
/// surface. Two same-named holes get distinct addresses by construction.
let holesOf (root: RNode) : HoleDecl list =
    Tree.preorder nodew root
    |> List.filter (fun n -> n.Hole.IsSome)
    |> List.map (fun n ->
        let addr =
            match Tree.path nodew idw n.Id root with
            | Some ids -> String.concat "/" ids
            | None -> n.Id

        { Addr = addr
          Name = n.HoleName
          Kind = n.Hole.Value })

/// Lower a hole binding to the reference domain's "op": a value sets the node's leaf
/// value; a slot wires the inner tree under the slot node. Clears the hole (bound).
let bind (addr: string) (arg: Arg<RNode>) (root: RNode) : Result<RNode, string> =
    let targetId = addr.Split('/') |> Array.last

    let upd (n: RNode) =
        match arg with
        | ValueArg s -> { n with Value = s; Hole = None }
        | SlotArg sub ->
            { n with
                Children = [ sub ]
                Hole = None }

    match Tree.updateNode nodew idw targetId upd root with
    | Some r -> Ok r
    | None -> Error("no node at " + targetId)

let artw: ArtifactWitness<RNode, string> =
    { Tree = nodew
      IdW = idw
      Holes = holesOf
      Effect = (fun n -> n.Eff)
      Bind = bind }

/// A canonical per-node content encoder for `Tree.encodeHash` — the node's LOCAL content (id, kind,
/// value, hole, effect), NOT its children (encodeHash walks preorder, applying this per node). The
/// `'Node -> string` parameter `Function.applyMemo` (Phase 49) takes for its content-addressed key.
let encNode (n: RNode) : string =
    String.concat "|" [ n.Id; n.Kind; n.Value; n.HoleName; sprintf "%A" n.Hole; sprintf "%A" n.Eff ]

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
