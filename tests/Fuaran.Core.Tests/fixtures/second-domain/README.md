# Second-vocabulary spike corpus

Eight sample structured documents, vendored here so the certification legs of
`../../SecondDomainSpike.fs` run in any clone of this repository rather than
reporting themselves skipped.

## What these are

Each file holds one `root` node tree written in a **second, document-shaped node
vocabulary** — deliberately not this repository's own. Two things about its wire
shape are the point of the spike, and both are visible in every file:

- the union discriminator is a bare-string **`kind`** member, not `$type`;
- the node envelope is **flat** — the tag, the node's `id` and the kind's own
  fields all sit in one object, rather than the kind body being nested under a
  `kind` member beside `id`.

The set spans the structural variety of the slice declared in the spike — a root
carrying an optional string and two closed sets, containers over child nodes, a
run-bearing leaf, a boolean-bearing row, an optional list, and a string-bearing
leaf — rather than being complete. Two declared run cases (`Emphasis`,
`InlineRef`) are unexercised here, which is a fact about the corpus that the
spike's findings already record.

The prose is generic sample text. The certification claim is that the declared
slice round-trips **these** documents byte-identically; it is not a claim about
any other corpus, and the bytes below are the ones it is measured against.

## Layout

`manifest.json` identifies the directory as a corpus and enumerates the
round-trip documents; `model-roundtrips/*.json` are the documents themselves.
The spike reads the `root` member of each file and ignores everything else, so a
richer corpus in the same layout — carrying its own document envelope and
sidecars alongside `root` — is read by the same code path.

## Pointing the spike at a different corpus

Set `FUARAN_SPIKE_CORPUS` to a directory of this shape. It must carry a
`manifest.json` naming `modelRoundTrips` and a `model-roundtrips/` directory, or
the spike refuses it by name rather than falling back silently.
