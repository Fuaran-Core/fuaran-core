namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.Projection (Phase 58) — the generic projection seam: a compact,
//  id-keyed, round-trippable textual projection an AI reads INSTEAD of the wire
//  JSON / rendered artifact, plus scoped / windowed reads (whole / by-id /
//  subtree / changed-since) so read cost is independent of artifact size.
//
//  The core owns the projection DISCIPLINE (the line shape, the scoping, the
//  digest, the laws); the domain supplies a `ProjectionWitness` — per-call, no
//  new base type (GP1/GP2), no domain content in core (GP6). FSharp.Core only,
//  Fable-clean (string folds over the existing portable FNV-1a).
// ============================================================================

/// The per-domain projection witness. The core composes the terse per-node line
/// itself (id + kind + content digest + snippet) from these parts — rather than
/// taking an opaque `terse : 'Node -> string` — so the projection laws
/// (digest-stability, scoped ⊆ whole) hold by construction and are provable
/// generically, instead of being re-trusted per domain.
///
///   - `Tree` / `IdW`     — the existing witnesses: children/identity selectors.
///   - `Encode`           — the per-node content encoder (the Phase 06/11
///                          `encodeHash` encoder posture): the node's OWN content
///                          (no children), injective over the node space
///                          (certify with `Conformance.encoderInjectivityLaws`).
///                          The per-line digest derives from it, so a projection
///                          line changes iff the node's content changes.
///   - `Snippet`          — a short single-line human/AI-readable content cell
///                          (never the full content; the core strips newlines so
///                          one node is always exactly one line).
///   - `ParseBack`        — one rendered projection line (indentation included)
///                          back to domain ops: the write path that keeps an edit
///                          expressed against the projection trackable as ops.
type ProjectionWitness<'Node, 'Id, 'Op> =
    { Tree: NodeWitness<'Node, 'Id>
      IdW: IdWitness<'Id>
      Encode: 'Node -> string
      Snippet: 'Node -> string
      ParseBack: string -> Result<'Op list, string> }

/// One projected node. `Depth` is PRESENTATION (the render indent encoding the
/// tree shape); the node's identity-bearing content cell is `lineText` (id +
/// kind + digest + snippet), so a structural move re-indents a line without
/// changing it — consistent with "a line changes iff its content digest changes".
type ProjectionLine =
    { IdKey: string
      Kind: string
      Depth: int
      Digest: string
      Snippet: string }

/// A projection: one terse line per node, in preorder, at absolute depth. A
/// scoped projection is therefore a sub-list of the whole projection's lines
/// (the ⊆ law holds by construction).
type Projection = { Lines: ProjectionLine list }

/// The changed-since baseline: each node's content digest keyed by its id
/// string, captured from a prior read. A projection can only carry nodes that
/// exist NOW — a node present in the snapshot but deleted since does not appear
/// (deletion detection is a consumer diff over the two id sets, not a scope).
type ProjectionSnapshot = { Digests: Map<string, string> }

/// The read window. `Whole` is the full artifact; `ById` a single node's line;
/// `Subtree` a node and everything below it (the contiguous preorder slice);
/// `ChangedSince` every node whose content digest differs from (or is absent
/// in) the snapshot — the incremental re-read.
type Scope<'Id> =
    | Whole
    | ById of 'Id
    | Subtree of 'Id
    | ChangedSince of ProjectionSnapshot

/// The generic projection functions. No domain content is ever in scope here —
/// every content-bearing cell (id, kind, encode, snippet) comes through the
/// witness; the core contributes only structural glue (indent, separators, the
/// digest fold).
module Projection =

    /// One node is always exactly one line: any newline in a witness-supplied
    /// cell is flattened to a space before it can break the line grammar.
    let private oneLine (s: string) =
        s.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ")

    /// The per-node content digest: FNV-1a over id + kind + the witness `Encode`
    /// (fields separated by the shared `Hash.foldSep`, so adjacent cells cannot
    /// run together into a colliding pre-image). Everything on the line derives
    /// from this pre-image (snippet included, via `Encode`'s injectivity), which
    /// is exactly what makes "line changes iff digest changes" a law.
    let digestOf (pw: ProjectionWitness<'Node, 'Id, 'Op>) (node: 'Node) : string =
        pw.IdW.ToString(pw.Tree.Id node)
        + Hash.foldSep
        + pw.Tree.KindTag node
        + Hash.foldSep
        + pw.Encode node
        |> Hash.fnv1a

    /// Project one node at `depth` into its terse line.
    let lineOf (pw: ProjectionWitness<'Node, 'Id, 'Op>) (depth: int) (node: 'Node) : ProjectionLine =
        { IdKey = oneLine (pw.IdW.ToString(pw.Tree.Id node))
          Kind = oneLine (pw.Tree.KindTag node)
          Depth = depth
          Digest = digestOf pw node
          Snippet = oneLine (pw.Snippet node) }

    /// The identity-bearing content cell: `id kind #digest snippet` (the snippet
    /// segment is omitted when empty). Depth is deliberately NOT part of it.
    let lineText (l: ProjectionLine) : string =
        let head = l.IdKey + " " + l.Kind + " #" + l.Digest
        if l.Snippet = "" then head else head + " " + l.Snippet

    /// One rendered line: two spaces of indent per depth level + the content cell.
    let renderLine (l: ProjectionLine) : string =
        String.replicate (l.Depth * 2) " " + lineText l

    /// Preorder walk carrying each node's depth. Iterative with an explicit
    /// work-list (the `Tree.preorder` posture) — a deep tree cannot overflow.
    let private preorderDepth (w: NodeWitness<'Node, 'Id>) (root: 'Node) : ('Node * int) list =
        let rec loop acc stack =
            match stack with
            | [] -> List.rev acc
            | (node, d) :: rest -> loop ((node, d) :: acc) ((w.Children node |> List.map (fun c -> c, d + 1)) @ rest)

        loop [] [ root, 0 ]

    /// Project `root` under `scope`. Every scoped read filters the SAME whole
    /// preorder line list at absolute depth, so a scoped projection is a
    /// sub-list of the whole one by construction (`Conformance.projectionLaws`
    /// proves it over a domain generator). An absent `ById`/`Subtree` id yields
    /// the empty projection — a total read, not an error channel.
    let project (pw: ProjectionWitness<'Node, 'Id, 'Op>) (scope: Scope<'Id>) (root: 'Node) : Projection =
        let lines = preorderDepth pw.Tree root |> List.map (fun (n, d) -> lineOf pw d n)

        match scope with
        | Whole -> { Lines = lines }
        | ById target ->
            let key = pw.IdW.ToString target
            { Lines = lines |> List.filter (fun l -> l.IdKey = key) }
        | Subtree target ->
            // the contiguous preorder slice: the target's line + every following
            // line strictly deeper than it (its descendants, and nothing else)
            let key = pw.IdW.ToString target

            let rec take acc rootDepth rest =
                match rest with
                | l :: tl when l.Depth > rootDepth -> take (l :: acc) rootDepth tl
                | _ -> List.rev acc

            let rec find rest =
                match rest with
                | [] -> []
                | (l: ProjectionLine) :: tl when l.IdKey = key -> l :: take [] l.Depth tl
                | _ :: tl -> find tl

            { Lines = find lines }
        | ChangedSince snap ->
            { Lines =
                lines
                |> List.filter (fun l -> Map.tryFind l.IdKey snap.Digests <> Some l.Digest) }

    /// Capture the changed-since baseline for `root`: every node's content
    /// digest keyed by its id string.
    let snapshot (pw: ProjectionWitness<'Node, 'Id, 'Op>) (root: 'Node) : ProjectionSnapshot =
        { Digests =
            preorderDepth pw.Tree root
            |> List.map (fun (n, _) -> pw.IdW.ToString(pw.Tree.Id n), digestOf pw n)
            |> Map.ofList }

    /// Render a projection to its textual form — the thing an AI reads instead
    /// of the wire JSON. One line per node, newline-joined.
    let render (p: Projection) : string =
        p.Lines |> List.map renderLine |> String.concat "\n"

    /// The size accessor (the compactness measure): the rendered character
    /// count. A consumer (the office eval) compares this against its wire-form
    /// size; `Conformance.projectionLaws` asserts projection < wire on the
    /// corpus — a floor, not a fixed ratio.
    let sizeOf (p: Projection) : int = render p |> String.length

    /// Map a rendered (possibly edited) projection text back to domain ops:
    /// split into lines, hand each non-blank line to the witness `ParseBack`
    /// (indentation included — the domain reads depth from it), and concatenate.
    /// First failing line wins — a projection edit either round-trips whole or
    /// names the line that does not.
    let parseBack (pw: ProjectionWitness<'Node, 'Id, 'Op>) (text: string) : Result<'Op list, string> =
        let lines =
            text.Split('\n') |> Array.toList |> List.filter (fun s -> s.Trim() <> "")

        let rec loop acc rest =
            match rest with
            | [] -> Ok(List.rev acc |> List.collect id)
            | line :: tl ->
                match pw.ParseBack line with
                | Ok ops -> loop (ops :: acc) tl
                | Error e -> Error e

        loop [] lines
