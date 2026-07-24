/// A SECOND minimal reference domain (Phase 51) — structurally distinct from `Reference.RNode`:
/// **int ids** (vs RNode's string ids — the other side of the string-vs-Guid identity axis) and a
/// different node-record shape (different field names). It exists so the cross-witness frontier operators
/// (`composeAcross`, Phase 47; `applyMemo`, Phase 49) can be certified to work GENERICALLY across two
/// structurally-distinct witnesses — the cross-witness pilot — the way `Reference.RNode` validated the
/// base surface against one. No dependency on any domain workspace (D7).
module Fuaran.Core.Tests.Reference2

open Fuaran.Core

/// A tiny second-domain node: int ids, a tag (kind), an optional payload (leaf value), an optional
/// declared hole + its name, an effect class, and children.
type R2Node =
    { Ref: int
      Tag: string
      Payload: string
      Slot: HoleKind option
      SlotName: string
      Effect: EffectClass
      Kids: R2Node list }

module R2Node =

    let node ref tag kids =
        { Ref = ref
          Tag = tag
          Payload = ""
          Slot = None
          SlotName = ""
          Effect = Effect.pureDeterministic
          Kids = kids }

    let leaf ref tag payload =
        { Ref = ref
          Tag = tag
          Payload = payload
          Slot = None
          SlotName = ""
          Effect = Effect.pureDeterministic
          Kids = [] }

    let hole ref tag slotName hk =
        { Ref = ref
          Tag = tag
          Payload = ""
          Slot = Some hk
          SlotName = slotName
          Effect = Effect.pureDeterministic
          Kids = [] }

// ---- the witnesses (int ids — the distinct identity shape) ----

let idw2: IdWitness<int> =
    { ToString = string
      OfString = int
      Equals = (fun a b -> a = b) }

let nodew2: NodeWitness<R2Node, int> =
    { Id = fun n -> n.Ref
      KindTag = fun n -> n.Tag
      Children = fun n -> n.Kids
      ReplaceChildren = fun n cs -> { n with Kids = cs } }

/// Enumerate declared holes with their absolute lexical address (id-path) — the hygiene surface.
let holesOf2 (root: R2Node) : HoleDecl list =
    Tree.preorder nodew2 root
    |> List.filter (fun n -> n.Slot.IsSome)
    |> List.map (fun n ->
        let addr =
            match Tree.path nodew2 idw2 n.Ref root with
            | Some ids -> ids |> List.map idw2.ToString |> String.concat "/"
            | None -> idw2.ToString n.Ref

        { Addr = addr
          Name = n.SlotName
          Kind = n.Slot.Value })

/// Lower a hole binding to this domain's "op": a value sets the leaf payload; a slot wires the inner
/// tree under the slot node. Clears the hole (bound).
let bind2 (addr: string) (arg: Arg<R2Node>) (root: R2Node) : Result<R2Node, string> =
    let targetId = addr.Split('/') |> Array.last |> int

    let upd (n: R2Node) =
        match arg with
        | ValueArg s -> { n with Payload = s; Slot = None }
        | SlotArg sub -> { n with Kids = [ sub ]; Slot = None }

    match Tree.updateNode nodew2 idw2 targetId upd root with
    | Some r -> Ok r
    | None -> Error("no node at " + addr)

let artw2: ArtifactWitness<R2Node, int> =
    { Tree = nodew2
      IdW = idw2
      Holes = holesOf2
      Effect = (fun n -> n.Effect)
      Bind = bind2 }

/// A canonical per-node content encoder for `Tree.encodeHash` — the node's LOCAL content (id, tag,
/// payload, hole, effect), NOT its children. The `'Node -> string` parameter `Function.applyMemo`
/// (Phase 49) takes for its content-addressed key.
let encNode2 (n: R2Node) : string =
    String.concat
        "|"
        [ string n.Ref
          n.Tag
          n.Payload
          n.SlotName
          sprintf "%A" n.Slot
          sprintf "%A" n.Effect ]

/// The cross-witness lift `composeAcross` takes (`embed : R2Node -> RNode`): a structural map that
/// preserves children + holes, so an embedded sub-function's holes stay visible in the combined
/// `RNode` signature (hygiene — addresses re-root, never names). The Tag becomes the RNode Kind, so an
/// `R2Node` rooted at Tag "para" satisfies an RNode slot constrained to "para".
let rec embedToR (n: R2Node) : Reference.RNode =
    { Id = string n.Ref
      Kind = n.Tag
      Value = n.Payload
      Hole = n.Slot
      HoleName = n.SlotName
      Eff = n.Effect
      Children = n.Kids |> List.map embedToR }
