- **The claims ladder gains a fifth assumed class, `witness-obligation`** (Phase 601, `proofs/kit/`, `proofs/`) —
  class additive; no package, wire or conformance surface moves, and every existing ladder reads as before. A
  `witness-obligation` row is a property every implementation of a domain's own witness must answer, which a theorem
  takes as a hypothesis and the witness's own tests discharge: no law of the shipped kit reaches it, so it is neither
  a `domain-obligation` nor a `host-premise`. It names the member in `dischargedBy`, `<Witness>.<member>`, and its
  `evidence.assumption` says what the member answers. `Proofs.Ladder` refuses such a row with no `dischargedBy`, with
  one outside that form or naming a `Conformance.<law>` (that row is a `domain-obligation`), and with a `closes` or a
  `probe`; `Proofs.Coverage` counts it as `witness-discharged`. The ladder summary and the contract section count the
  class (this repository's ladder carries none), the kit template carries one annotated example row, and the kit
  asset's sha moves with `LADDER.md` and the template. **Migration:** none for a package consumer. A ladder that
  already carries rows of this class decodes unchanged once a registry reads the kit at this version, whether or not
  its rows name a `dischargedBy`: a registry refuses only an unrecognised `class`, and the member rule is held by the
  host family of the repository that states the rows.
