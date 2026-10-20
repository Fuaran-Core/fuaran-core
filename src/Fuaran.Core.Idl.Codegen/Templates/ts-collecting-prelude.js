// ---------------------------------------------------------------------------
// Phase 380 — COLLECTING DECODERS. Beside every short-circuiting `dec*` decoder above, a `col*`
// decoder answers EVERY defect it finds, in one deterministic order — the F# host's:
//   - an object's members in FIELD DECLARATION ORDER (a node: `id`, its kind, then its envelope
//     fields), each member's defects at that member's position, its nested defects included
//     (depth-first);
//   - a list's items in index order, and a map's entries in document order;
//   - a leaf (a scalar, an enum, a sentinel, a hosted slot, verbatim JSON) reports at most one
//     defect, and so does a value of the wrong kind or an absent or unknown discriminator, whose
//     members are never read.
// The first defect of a collecting decoder is the defect its short-circuiting twin reports, and on
// a clean input both answer the same value. Codes and paths are Core's `DecodeError`'s.
// ---------------------------------------------------------------------------
// Several refusals at once — what a collecting decoder throws when a position it read refused.
class DecodeFaults extends Error {
  constructor(faults) {
    super(faults.length + ' decode faults');
    this.faults = faults;
  }
}
// One read: its value, or its refusals (one for a short-circuiting refusal, every one for a
// collecting one).
const cRun = (read) => {
  try {
    return { ok: true, value: read() };
  } catch (e) {
    if (e instanceof DecodeFault) return { ok: false, faults: [e] };
    if (e instanceof DecodeFaults) return { ok: false, faults: e.faults };
    throw e;
  }
};
// Every read, in order: their values when all decoded, else every read's refusals, in that order.
const cAll = (reads) => {
  const results = reads.map(cRun);
  const faults = results.flatMap((r) => (r.ok ? [] : r.faults));
  if (faults.length > 0) throw new DecodeFaults(faults);
  return results.map((r) => r.value);
};
// One step further from the root, for every refusal a member or an item raised.
const cAt = (step, f) => {
  try {
    return f();
  } catch (e) {
    if (e instanceof DecodeFault) e.path.unshift(step);
    else if (e instanceof DecodeFaults) for (const x of e.faults) x.path.unshift(step);
    throw e;
  }
};
const cList = (dec) => (j) => {
  if (!Array.isArray(j)) return dFail('WrongKind', 'array', 'expected an array');
  return cAll(j.map((_, i) => () => cAt(i, () => dec(dRead(j, i)))));
};
// Every entry is checked, in document order; a repeated key keeps its FIRST value, as `dMap` does.
const cMap = (dec) => (j) => {
  const o = dObj(j);
  const members = dMembersOf.get(o);
  const entries = (members === undefined)
    ? Object.keys(o).map((k) => [k, () => cAt(k, () => dec(dRead(o, k)))])
    : members.map(([k, v, floatTok]) => [k, () => cAt(k, () => { dFloatTok = floatTok; return dec(v); })]);
  const values = cAll(entries.map(([, read]) => read));
  const out = Object.create(null);
  entries.forEach(([k], i) => { if (!hasOwn(out, k)) out[k] = values[i]; });
  return out;
};
const cReq = (name, fs, dec) => hasOwn(fs, name) ? cAt(name, () => dec(dRead(fs, name))) : dMissing(name, "missing required field '" + name + "'");
const cOpt = (name, fs, dec) => hasOwn(fs, name) ? cAt(name, () => dec(dRead(fs, name))) : undefined;
const cDef = (name, fs, dec, dflt) => hasOwn(fs, name) ? cAt(name, () => dec(dRead(fs, name))) : dflt;