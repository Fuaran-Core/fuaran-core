namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.Projection (Phase 58) — the generic projection seam: a compact,
//  id-keyed, round-trippable textual projection an AI reads INSTEAD of the wire
//  JSON / rendered artifact, plus scoped / windowed reads (whole / by-id /
//  subtree / changed-since). A scoped read encodes and digests only the nodes
//  in its scope; locating them is one structural walk of the tree (Phase 298
//  corrects the older claim that read cost was independent of artifact size —
//  the walk is linear, the per-node content work is what the scope bounds).
//
//  The core owns the projection DISCIPLINE (the line shape, the scoping, the
//  digest, the laws); the domain supplies a `ProjectionWitness` — per-call, no
//  new base type (GP1/GP2), no domain content in core (GP6). FSharp.Core only,
//  Fable-clean (string folds over the existing portable FNV-1a and SHA-256).
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
/// `IdKey` and `Kind` are the ESCAPED cells (`Projection.escapeCell`), so `IdKey`
/// is the one key every scope compares (`Projection.idKey`).
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

/// The changed-since baseline: each node's SNAPSHOT digest keyed by its id key
/// (`Projection.idKey`), captured from a prior read. A projection can only carry
/// nodes that exist NOW — a node present in the snapshot but deleted since does
/// not appear (deletion detection is a consumer diff over the two id sets, not a
/// scope).
///
/// **The snapshot digest is not the line digest (Phase 298).** It is a SHA-256
/// over the node's content pre-image AND its ordered child id keys
/// (`Projection.snapshotDigestOf`), so a reorder reports the parent whose order
/// moved and a move reports the parent it left and the parent it joined — the
/// 32-bit content digest saw neither, and over enough random edits let a real
/// content edit read as unchanged. A snapshot taken before Phase 298 holds the
/// old digests, and reads every node as changed once.
type ProjectionSnapshot = { Digests: Map<string, string> }

/// The read window. `Whole` is the full artifact; `ById` a single node's line;
/// `Subtree` a node and everything below it (the contiguous preorder slice);
/// `ChangedSince` every node whose snapshot digest differs from (or is absent
/// in) the snapshot — the incremental re-read. `RequireQualifiedAccess` (Phase
/// 298): `Scope.Whole`, `Scope.ById`, … — the cases no longer shadow a
/// consumer's own `Whole` or `ById`.
[<RequireQualifiedAccess>]
type Scope<'Id> =
    | Whole
    | ById of 'Id
    | Subtree of 'Id
    | ChangedSince of ProjectionSnapshot

/// The generic projection functions. No domain content is ever in scope here —
/// every content-bearing cell (id, kind, encode, snippet) comes through the
/// witness; the core contributes only structural glue (indent, separators, the
/// digest fold).
///
/// **The line grammar (Phase 298, stated).** A rendered line is `2·depth` spaces,
/// then `ID KIND #DIGEST`, then ` SNIPPET` when the snippet is non-empty — four
/// fields separated by single spaces. `ID` and `KIND` are ESCAPED (`escapeCell`):
/// `\` is written `\\`, a space `\s`, a tab `\t`, a line feed `\n` and a carriage
/// return `\r`, so neither cell can hold a separator or a line break and a line
/// splits into its fields unambiguously; `unescapeCell` reads a cell back.
/// `DIGEST` is lowercase hex. `SNIPPET` is the rest of the line, newlines
/// flattened to spaces — it is a short human/AI-readable cell, never the full
/// content, and is not read back. A line is terminated by `\n`; `parseBack`
/// also accepts `\r\n`.
module Projection =

    /// One node is always exactly one line: any newline in the snippet is
    /// flattened to a space before it can break the line grammar.
    let private oneLine (s: string) =
        s.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ")

    /// An id or kind cell as the line grammar writes it: `\` → `\\`, space → `\s`,
    /// tab → `\t`, LF → `\n`, CR → `\r`. Injective; `unescapeCell` inverts it.
    let escapeCell (s: string) : string =
        s.Replace("\\", "\\\\").Replace(" ", "\\s").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r")

    /// Read an escaped cell back: the inverse of `escapeCell` on its image. A `\`
    /// followed by anything else, or ending the cell, is kept as written.
    let unescapeCell (s: string) : string =
        let sb = System.Text.StringBuilder()
        let mutable i = 0

        while i < s.Length do
            let c = s[i]

            if c = '\\' && i + 1 < s.Length then
                match s[i + 1] with
                | '\\' -> sb.Append('\\') |> ignore
                | 's' -> sb.Append(' ') |> ignore
                | 't' -> sb.Append('\t') |> ignore
                | 'n' -> sb.Append('\n') |> ignore
                | 'r' -> sb.Append('\r') |> ignore
                | other -> sb.Append(c).Append(other) |> ignore

                i <- i + 2
            else
                sb.Append(c) |> ignore
                i <- i + 1

        sb.ToString()

    /// THE key of a node id (Phase 298): its `IdW.ToString` form, escaped. Every
    /// scope compares it and every snapshot is keyed by it — `ById`, `Subtree`,
    /// `snapshot` and `ChangedSince` used to disagree (the line flattened a newline
    /// that the scopes compared raw), so an id carrying a newline never matched
    /// `ById` and always read as changed.
    let idKey (pw: ProjectionWitness<'Node, 'Id, 'Op>) (id: 'Id) : string = escapeCell (pw.IdW.ToString id)

    /// The per-node content digest: FNV-1a over the three fields id, kind and the
    /// witness `Encode`, through `Hash.canonicalFields` — each field escaped and
    /// terminated, so a cell that spells the separator cannot run into the next
    /// (until Phase 290 the three were joined on the bare `Hash.foldSep`, which an
    /// `Encode` can spell). Everything on the line derives from this pre-image
    /// (snippet included, via `Encode`'s injectivity), which is exactly what makes
    /// "line changes iff digest changes" a law. That two distinct pre-images hash
    /// apart under FNV-1a is not claimed — which is why the changed-since baseline
    /// does not use it (`snapshotDigestOf`).
    let digestOf (pw: ProjectionWitness<'Node, 'Id, 'Op>) (node: 'Node) : string =
        Hash.canonicalFields [ pw.IdW.ToString(pw.Tree.Id node); pw.Tree.KindTag node; pw.Encode node ]
        |> Hash.fnv1a

    /// The changed-since digest of a node (Phase 298): SHA-256 over its id, kind,
    /// `Encode`, child count and each child's id, through `Hash.canonicalFields`.
    /// A content edit, a reorder of its children and a child arriving or leaving
    /// each move it; a 64-hex digest makes an unseen change a SHA-256 collision,
    /// not a 1-in-2^32 chance.
    let snapshotDigestOf (pw: ProjectionWitness<'Node, 'Id, 'Op>) (node: 'Node) : string =
        let kids = pw.Tree.Children node

        pw.IdW.ToString(pw.Tree.Id node)
        :: pw.Tree.KindTag node
        :: pw.Encode node
        :: string (List.length kids)
        :: (kids |> List.map (fun c -> pw.IdW.ToString(pw.Tree.Id c)))
        |> Hash.canonicalFields
        |> Hash.sha256Hex

    /// Project one node at `depth` into its terse line.
    let lineOf (pw: ProjectionWitness<'Node, 'Id, 'Op>) (depth: int) (node: 'Node) : ProjectionLine =
        { IdKey = idKey pw (pw.Tree.Id node)
          Kind = escapeCell (pw.Tree.KindTag node)
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
    /// Structural only: no witness content is read.
    let private preorderDepth (w: NodeWitness<'Node, 'Id>) (root: 'Node) : ('Node * int) list =
        let rec loop acc stack =
            match stack with
            | [] -> List.rev acc
            | (node, d) :: rest -> loop ((node, d) :: acc) ((w.Children node |> List.map (fun c -> c, d + 1)) @ rest)

        loop [] [ root, 0 ]

    /// Project `root` under `scope`. Every scoped read selects nodes from the
    /// SAME preorder at absolute depth and renders each through `lineOf`, so a
    /// scoped projection is a sub-list of the whole one by construction
    /// (`Conformance.projectionLaws` checks it over a domain generator). Only the
    /// selected nodes are encoded and digested — `ChangedSince` digests every
    /// node, since every node is a candidate. An absent `ById`/`Subtree` id yields
    /// the empty projection — a total read, not an error channel.
    let project (pw: ProjectionWitness<'Node, 'Id, 'Op>) (scope: Scope<'Id>) (root: 'Node) : Projection =
        let placed = preorderDepth pw.Tree root
        let keyOf (n: 'Node) = idKey pw (pw.Tree.Id n)

        let render (nodes: ('Node * int) list) =
            nodes |> List.map (fun (n, d) -> lineOf pw d n)

        match scope with
        | Scope.Whole -> { Lines = render placed }
        | Scope.ById target ->
            let key = idKey pw target
            { Lines = placed |> List.filter (fun (n, _) -> keyOf n = key) |> render }
        | Scope.Subtree target ->
            // the contiguous preorder slice: the target + every following node
            // strictly deeper than it (its descendants, and nothing else)
            let key = idKey pw target

            let rec take acc rootDepth rest =
                match rest with
                | (n, d) :: tl when d > rootDepth -> take ((n, d) :: acc) rootDepth tl
                | _ -> List.rev acc

            let rec find rest =
                match rest with
                | [] -> []
                | (n, d) :: tl when keyOf n = key -> (n, d) :: take [] d tl
                | _ :: tl -> find tl

            { Lines = find placed |> render }
        | Scope.ChangedSince snap ->
            { Lines =
                placed
                |> List.filter (fun (n, _) -> Map.tryFind (keyOf n) snap.Digests <> Some(snapshotDigestOf pw n))
                |> render }

    /// Capture the changed-since baseline for `root`: every node's snapshot
    /// digest (`snapshotDigestOf`) keyed by its id key (`idKey`).
    let snapshot (pw: ProjectionWitness<'Node, 'Id, 'Op>) (root: 'Node) : ProjectionSnapshot =
        { Digests =
            preorderDepth pw.Tree root
            |> List.map (fun (n, _) -> idKey pw (pw.Tree.Id n), snapshotDigestOf pw n)
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
    /// split into lines (`\n`, with a `\r` before it dropped, so CRLF text reads
    /// as LF text), hand each non-blank line to the witness `ParseBack`
    /// (indentation included — the domain reads depth from it), and concatenate.
    /// First failing line wins, and the refusal NAMES it (Phase 298): `line N: `
    /// (1-based, counting every line of the text, blank ones included) before the
    /// witness's own message — a projection edit either round-trips whole or says
    /// which line does not.
    let parseBack (pw: ProjectionWitness<'Node, 'Id, 'Op>) (text: string) : Result<'Op list, string> =
        let lines =
            text.Split('\n')
            |> Array.toList
            |> List.mapi (fun i s -> i + 1, (if s.EndsWith "\r" then s.Substring(0, s.Length - 1) else s))
            |> List.filter (fun (_, s) -> s.Trim() <> "")

        let rec loop acc rest =
            match rest with
            | [] -> Ok(List.rev acc |> List.collect id)
            | (n, line) :: tl ->
                match pw.ParseBack line with
                | Ok ops -> loop (ops :: acc) tl
                | Error e -> Error("line " + string n + ": " + e)

        loop [] lines
