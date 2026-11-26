- **The canonical-form law set is declared, indexed and pinned** (Phase 554, `Fuaran.Core` conformance). `conformance/laws/canonical-form-laws.json`
  is now a declared derived file of the version ledger with the shared corpus's copy of it, so `version check` names it on a cut, and
  `LawVectorExport.manifestRows` lists `canonicalFormLaws`, so the laws-manifest check reads the corpus's row for it. The corpus pin moves to
  the corpus commit that carries the file. `render laws <corpus dir>` writes the copy beside the other four. No emitted byte changes.