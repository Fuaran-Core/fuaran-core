# Score-vocabulary spike corpus

Eleven sample scores, vendored here so the certification legs of
`../../ScoreDomainSpike.fs` run in any clone of this repository rather than
reporting themselves skipped.

## What these are

Each file holds one `root` node tree written in a **third, score-shaped
(music-notation) node vocabulary** — deliberately not this repository's own.
Three things about its wire shape are the point of the spike, and all three are
visible across the set:

- the union discriminator is a bare-string **`kind`** member, not `$type` — the
  same divergence the second (document-shaped) vocabulary showed;
- the node envelope is **flat** — the tag, the node's `id` and the kind's own
  fields all sit in one object — again matching the second vocabulary;
- the wire practises **omit-at-default economy**: a voice of 1, a dot count of
  0 and false boolean flags are omitted on emit and reconstituted on decode.

The set spans the whole structural variety of the slice declared in the spike —
every node kind the slice declares appears in at least one sample. Unlike the
second-vocabulary corpus (whose findings record that its fixtures never
exercised its optionality finding), these samples **deliberately populate the
omittable fields in one place and omit them in another** — `single-dotted-note`
carries an explicit voice, dot count and tie flag; the ensemble samples rely on
the defaults — so the omit-at-default measurement is exercised in both
directions by the corpus itself.

The document envelope of the sampled vocabulary (a version stamp beside the
root) is reduced to the bare `root` member here, exactly as the second-domain
corpus does: the spike reads `root` and ignores everything else, and the
envelope has no IDL position by design. Node ids are shortened to readable
tokens — the vocabulary's id is a plain wire string either way, and nothing in
the spike depends on its format.

The certification claim is that the declared slice round-trips **these**
documents byte-identically; it is not a claim about any other corpus, and the
bytes below are the ones it is measured against.

## Layout

`manifest.json` identifies the directory as a corpus and enumerates the
round-trip documents; `model-roundtrips/*.json` are the documents themselves.

## Pointing the spike at a different corpus

Set `FUARAN_SCORE_SPIKE_CORPUS` to a directory of this shape. It must carry a
`manifest.json` naming `modelRoundTrips` and a `model-roundtrips/` directory, or
the spike refuses it by name rather than falling back silently.
