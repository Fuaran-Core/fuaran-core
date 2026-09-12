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
The identity is the manifest's **`kind`** member, which reads
`"second-domain-spike-corpus"` — that member, and nothing else in the document,
is what makes this directory this corpus.
The spike reads the `root` member of each file and ignores everything else, so a
richer corpus in the same layout — carrying its own document envelope and
sidecars alongside `root` — is read by the same code path.

## Pointing the spike at a different corpus

Set `FUARAN_SPIKE_CORPUS` to a directory of this shape. Its `manifest.json` must
declare `"kind": "second-domain-spike-corpus"`, carry a `modelRoundTrips` array,
and sit beside a `model-roundtrips/` directory — or the spike refuses it at
resolution, naming the identity it expected and the path it read, rather than
falling back to the vendored set silently.

Acceptance used to be a containment test over the manifest's text: any document
that CONTAINED the string naming the round-trip family was taken to be a corpus.
That cannot tell a document which *is* this corpus from one which merely
*mentions* it — another vocabulary's manifest listing the family among
specifications it does not carry would have been accepted, and the certification
would then have reported a result about whichever documents were on disk.
