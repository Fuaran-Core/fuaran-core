- **A presence-keyed union whose case declares another case's key as a member of its own is refused at its
  declaration** (Phase 722, `Fuaran.Core.Idl`, DECISIONS.md D285.6) — class **breaking**: no public signature moves,
  and a vocabulary that loaded is refused. `Declare.errors` names the member,
  `union '<U>', case '<A>': the member '<m>' is the key of case '<B>', so a value carrying it writes both keys and is read as neither case`,
  at `$.unions[i].cases[c].fields[j]`, beside the union's other rule faults, so the artifact reader and
  `Proposal.applyDelta` refuse it there. It used to load, and the interpreter encoded a value of the case carrying
  the member to a document its own reading refused, `OutOfRange` at the second key. A host-only member of that name
  is never on the wire and is not refused. A consumer whose vocabulary is refused renames the member, or moves it to
  the case whose key it is: the two cases cannot both be told from one key.
