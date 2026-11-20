- **The generated `NodeKind` cases and `Node` fields carry their docs** (Phase 705, `Fuaran.Core.Idl.Codegen`) —
  class **breaking**: no public signature moves, and the generated F# of a vocabulary whose kinds carry a `doc`
  annotation moves, so a consumer holding its generated module to a fresh generation regenerates it. Each
  `NodeKind` case carries its kind's authored doc, where only the kind's `<Kind>Spec` record did; the case takes the
  doc alone, and the deprecation, in-process and since notes and the `Obsolete` attribute stay on the spec record.
  The support record's `Docs` reaches three new paths: `case:NodeKind.<Tag>` above a case, `field:Node.Id` and
  `field:Node.Kind` above the node's identity and kind fields, which the IDL does not declare as fields, and
  `field:Node.<Field>` above an envelope field, as every record field takes one. A support block and an authored doc
  on one case are printed as Phase 713 prints them on any member. A node with no envelope and no node doc keeps its
  one-line record; one with a doc on `Id` or `Kind` is printed as a record of lines. **What a consumer does:**
  regenerate a committed generated module whose kinds carry a `doc`; a vocabulary that declares no doc emits the
  bytes it did, and a support document that names none of the new paths is unchanged (each was refused as a path the
  emission never consulted).
