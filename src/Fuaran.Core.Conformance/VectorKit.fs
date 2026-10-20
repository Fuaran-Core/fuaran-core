namespace Fuaran.Core

/// The vector families' shared verdicts and spellings (Phase 388) — `WireNullTolerance`,
/// `StringEscapeVectors` and `EncodingProfileVectors` each wrote these out, body for body. A vector
/// family is a runner over an enumerated table rather than a drawn `LawResult` family, and these are
/// the pieces every such runner builds its `Corpus.Outcome`s from. Internal: the outcomes are the
/// surface, and their bytes are what the families' tests pin. The public shape every vector family
/// instantiates, and the generic verbs over it, follow below (`VectorFamily`, Phase 508).
module internal VectorKit =

    /// A passing outcome.
    let pass (name: string) : Corpus.Outcome =
        { Name = name
          Passed = true
          Detail = "ok" }

    /// A failing outcome, carrying what was wrong.
    let fail (name: string) (detail: string) : Corpus.Outcome =
        { Name = name
          Passed = false
          Detail = detail }

    /// `got` held to `expected`; a miss names what emitted it (`what`) and both spellings.
    let expect (name: string) (what: string) (expected: string) (got: string) : Corpus.Outcome =
        if got = expected then
            pass name
        else
            fail name (what + " emitted " + got + ", expected " + expected)

    /// The 32 control characters `U+0000`–`U+001F` as the vector tables state them (Phase 508): the
    /// code, the canonical spelling (`\u00xx`, lower-case hex) and the short form the `V1` profile and
    /// the pre-Phase-287 chain wrote (`\n`, `\r`, `\t`; every other character as the canonical one).
    /// `StringEscapeVectors` and `EncodingProfileVectors` both read it, so the rule is stated once. It
    /// is stated HERE, from the rule, and not read from `Wire.Json`'s own table: these rows are the
    /// oracle the escapers are held to, and an oracle read from the code it judges agrees with any
    /// change to that code (DECISIONS.md D171).
    let controlCharacters: (int * string * string) list =
        [ for c in 0..0x1F do
              let canonical = sprintf "\\u%04x" c

              let short =
                  match c with
                  | 0x0A -> "\\n"
                  | 0x0D -> "\\r"
                  | 0x09 -> "\\t"
                  | _ -> canonical

              c, canonical, short ]

    /// An already-escaped body inside the quotes of a JSON string literal.
    let quoted (body: string) : string = "\"" + body + "\""

    /// `{"seq":0,"actor":<actor>,"op":{}}` — the linear chain pre-image for one actor.
    let payload (actor: string) : string =
        "{\"seq\":0,\"actor\":" + actor + ",\"op\":{}}"

    /// The one law every vector family's `lawsWith` carries beside its per-vector laws (Phase 390):
    /// its evidence is one assertion per vector the run EVALUATED, counted where the family builds
    /// each vector's verdict, so a run handed no vectors reads red by name (`never reached`) rather
    /// than green over an empty list. It is what lets the census tell a fixed corpus that was walked
    /// from one that was not.
    let corpusCell (prefix: string) : LawKit.LawCell =
        LawKit.LawCell(prefix + ": the corpus evaluated at least one vector")

    /// The corpus law's verdict, as what it is (Phase 515): a `Corpus` result over vectors, its
    /// evidence the vectors evaluated, so a census reads it as the non-emptiness law by its kind and
    /// never by its name.
    let corpusResult (corpus: LawKit.LawCell) : LawResult =
        { corpus.Result with
            Kind = LawKind.Corpus "vector" }

    /// One vector's law: green exactly when every outcome it produced passed, its first failing
    /// outcome the counterexample. The vector is counted on `corpus` as it is evaluated.
    let verdict (corpus: LawKit.LawCell) (law: string) (outcomes: Corpus.Outcome list) : LawResult =
        corpus.Saw()
        let cell = LawKit.LawCell law

        match outcomes |> List.tryFind (fun o -> not o.Passed) with
        | Some o -> cell.Check(false, (fun () -> o.Name + ": " + o.Detail))
        | None -> cell.Saw()

        cell.Result

    /// The stored families' twin of `corpusCell` (Phase 390): one assertion per stored item the store
    /// holds, so an empty store reads red by name rather than green over nothing. `what` names the
    /// item — `record`, `node`, `capture`, `text`.
    let storeCell (prefix: string) (what: string) (count: int) : LawResult =
        let cell = LawKit.LawCell(prefix + "the store holds at least one " + what)

        for _ in 1..count do
            cell.Saw()

        { cell.Result with
            Kind = LawKind.Corpus what }

/// A vector family over one shape (Phase 508): a fixed corpus, what one vector checks, and the line a
/// host in another language diffs. The verbs over it are generic (`VectorFamily.run`, `check`,
/// `lines`, `laws`, `lawsWith`), so a new vector family is a value of this type and no new runner.
type internal VectorFamily<'V> =
    {
        /// The family's label — the prefix of every per-vector law (`<Label>: <Name v>`) and the
        /// subject of its corpus law (`<Label>: the corpus evaluated at least one vector`).
        Label: string
        /// The vectors the committed family runs; `VectorFamily.over` hands it another set.
        Corpus: 'V list
        /// One vector's law name, after the label.
        Name: 'V -> string
        /// Every check one vector makes, named.
        Check: 'V -> Corpus.Outcome list
        /// One vector as the line a host in another language diffs.
        Line: 'V -> string
    }

/// The generic verbs over a `VectorFamily` (Phase 508). One definition of each, so every vector
/// family is red over an empty corpus by the same law and renders its lines by the same walk.
[<RequireQualifiedAccess>]
module internal VectorFamily =

    /// The family over another corpus — a host's own additions, a subset, or none.
    let over (corpus: 'V list) (family: VectorFamily<'V>) : VectorFamily<'V> = { family with Corpus = corpus }

    /// Every check of every vector, in corpus order.
    let run (family: VectorFamily<'V>) : Corpus.Outcome list =
        family.Corpus |> List.collect family.Check

    /// `Ok`, or the first failing outcome of `outcomes`, named.
    let firstFailure (outcomes: Corpus.Outcome list) : Result<unit, string> =
        match outcomes |> List.tryFind (fun o -> not o.Passed) with
        | None -> Ok()
        | Some first -> Error(first.Name + ": " + first.Detail)

    /// The whole family as a single verdict — `Ok` or the first failing check, named.
    let check (family: VectorFamily<'V>) : Result<unit, string> = firstFailure (run family)

    /// The corpus as the lines a host diffs, in corpus order. Deterministic; no seed.
    let lines (family: VectorFamily<'V>) : string list = family.Corpus |> List.map family.Line

    /// The family as `LawResult`s: one law per vector (`<Label>: <Name v>`), green exactly when every
    /// check it makes passes, its first failing check the counterexample; then `extra`, the laws a
    /// family states over its table as a whole (a format law, the checks no vector owns); then the
    /// corpus law, whose evidence is one assertion per vector evaluated, so a family handed no vectors
    /// is red by name rather than green over nothing.
    let lawsWith (extra: LawResult list) (family: VectorFamily<'V>) : LawResult list =
        let corpus = VectorKit.corpusCell family.Label

        let perVector =
            family.Corpus
            |> List.map (fun v -> VectorKit.verdict corpus (family.Label + ": " + family.Name v) (family.Check v))

        perVector @ extra @ [ VectorKit.corpusResult corpus ]

    /// `lawsWith []` — the per-vector laws and the corpus law.
    let laws (family: VectorFamily<'V>) : LawResult list = lawsWith [] family

/// How one value is laid out in a committed conformance file (Phase 508).
[<RequireQualifiedAccess>]
type internal CorpusCell =
    /// The value as `Canon.render` writes it: a scalar, or a compact canonical object or array.
    | Value of JVal
    /// An object in AUTHOR order, spaced — `{ "k": v, "k2": v2 }` — and `{}` with no member.
    | Row of (string * CorpusCell) list
    /// An array in AUTHOR order, spaced — `[a, b]` — and `[]` with no item.
    | List of CorpusCell list

/// One family's row in a self-enumerated family manifest (Phase 508) — the ONE manifest schema the
/// emitted families share, so a validator over a manifest reads one shape.
type internal CorpusManifestRow =
    {
        /// The family's id.
        Id: string
        /// What kind of vectors the family holds.
        Kind: string
        /// The vector file beside the manifest.
        File: string
        /// How many vectors that file holds — the manifest is authoritative for the count.
        Vectors: int
        /// Each host's adoption of the family, in roster order.
        Adoption: (string * string) list
        /// What the family pins.
        Description: string
    }

/// A self-enumerated family manifest (Phase 508): a version, what the index is, and one row per family.
type internal CorpusManifest =
    {
        /// The manifest's version.
        Version: int
        /// What the index is and how a host reads it.
        Description: string
        /// The families, one row each.
        Families: CorpusManifestRow list
    }

/// A committed conformance file (Phase 508): header members one per line, then ONE array member with
/// one item per line.
type internal CorpusDocument =
    {
        /// The header members, in order.
        Head: (string * CorpusCell) list
        /// The array member's name — `vectors`, `lines`, `families`.
        Items: string
        /// The array's items, in order.
        Rows: CorpusCell list
    }

/// Where a committed file first differs from the text its exporter renders (Phase 508).
type internal CorpusDifference =
    {
        /// The 1-based number of the first line that differs.
        Line: int
        /// That line as committed, `<absent>` past the committed text's end.
        Committed: string
        /// That line as rendered, `<absent>` past the rendered text's end.
        Rendered: string
    }

/// The one writer of the conformance files a repository emits (Phase 508). Every key, string, number
/// and compact value goes through `Canon.render`, so a file escapes by the spine's one rule; the layout
/// is the one every host reads line by line — a header member per line, an item per line, LF endings,
/// a trailing newline. An exporter builds a `CorpusDocument` and calls `render`; it writes no JSON of
/// its own.
[<RequireQualifiedAccess>]
module internal CorpusFile =

    let rec private cell (c: CorpusCell) : string =
        match c with
        | CorpusCell.Value v -> Canon.render v
        | CorpusCell.Row [] -> "{}"
        | CorpusCell.Row members ->
            "{ "
            + (members
               |> List.map (fun (k, v) -> Canon.render (JStr k) + ": " + cell v)
               |> String.concat ", ")
            + " }"
        | CorpusCell.List items -> "[" + (items |> List.map cell |> String.concat ", ") + "]"

    /// A string's cell.
    let str (s: string) : CorpusCell = CorpusCell.Value(JStr s)

    /// An integer's cell.
    let num (n: int) : CorpusCell = CorpusCell.Value(JInt n)

    /// One cell as `render` writes it, indentation excluded — the text of one item line.
    let renderCell (c: CorpusCell) : string = cell c

    /// The document's text.
    let render (doc: CorpusDocument) : string =
        let head =
            doc.Head
            |> List.map (fun (k, v) -> "  " + Canon.render (JStr k) + ": " + cell v + ",\n")
            |> String.concat ""

        let rows =
            match doc.Rows with
            | [] -> ""
            | rows -> (rows |> List.map (fun r -> "    " + cell r) |> String.concat ",\n") + "\n"

        "{\n"
        + head
        + "  "
        + Canon.render (JStr doc.Items)
        + ": [\n"
        + rows
        + "  ]\n}\n"

    /// A manifest row as one item: its members in schema order, the adoption a spaced object in
    /// roster order.
    let manifestRow (r: CorpusManifestRow) : CorpusCell =
        CorpusCell.Row
            [ "id", str r.Id
              "kind", str r.Kind
              "file", str r.File
              "vectors", num r.Vectors
              "adoption", CorpusCell.Row(r.Adoption |> List.map (fun (host, status) -> host, str status))
              "description", str r.Description ]

    /// A manifest as the document `render` writes.
    let manifest (m: CorpusManifest) : CorpusDocument =
        { Head = [ "version", num m.Version; "description", str m.Description ]
          Items = "families"
          Rows = m.Families |> List.map manifestRow }

    /// The ONE freshness reading of a committed file against the text its exporter renders: `None`
    /// when they agree once CRLF line endings are read as LF (the one difference a checkout's
    /// end-of-line policy may introduce), else the first line that differs (`<absent>` past either end).
    let firstDifference (committed: string) (rendered: string) : CorpusDifference option =
        let a = committed.Replace("\r\n", "\n").Split('\n')
        let b = rendered.Split('\n')

        let at (xs: string[]) i =
            if i < xs.Length then xs[i] else "<absent>"

        if a = b then
            None
        else
            [ 0 .. max a.Length b.Length - 1 ]
            |> List.tryFind (fun i -> at a i <> at b i)
            |> Option.map (fun i ->
                { Line = i + 1
                  Committed = at a i
                  Rendered = at b i })
