// Phase 337 — a refusal: the code (the closed DecodeCode set), the path from the document root
// (member keys and item indices), what the position expected, and a sentence.
class DecodeFault extends Error {
  constructor(code, expected, message) {
    super(message);
    this.code = code;
    this.path = [];
    this.expected = expected;
  }
}
const dFail = (code, expected, m) => { throw new DecodeFault(code, expected, m); };
// One step further from the root — what a refusal gains as it leaves a member or an item.
const dAt = (step, f) => {
  try {
    return f();
  } catch (e) {
    if (e instanceof DecodeFault) e.path.unshift(step);
    throw e;
  }
};
const dMissing = (name, m) => {
  const f = new DecodeFault('MissingField', "a member '" + name + "'", m);
  f.path.push(name);
  throw f;
};
const hasOwn = (o, k) => Object.prototype.hasOwnProperty.call(o, k);
const isObj = (j) => j !== null && typeof j === 'object' && !Array.isArray(j);
// Phase 347 — what the reader (`dParse`) knows and a JS value cannot carry, kept beside the value
// for the decode that reads it: per object or array, the members and items whose WHOLE number was
// written as a float token (an int slot refuses it); and per object whose JS form loses its document
// order, its members as written — an index-like key (the object lists those first). A repeated key
// no longer reaches here: the reader refuses it (Phase 462).
const dFloatAt = new WeakMap();
const dMembersOf = new WeakMap();
// Whether the value being decoded was read as a float token: set by `dRead` as each member or item
// is handed to its decoder, and read by `dInt` before any other value is.
let dFloatTok = false;
const dRead = (c, k) => {
  const at = dFloatAt.get(c);
  dFloatTok = at !== undefined && at.has(k);
  return c[k];
};
const dDisc = __DISC__;
// The discriminator: absent is MissingField naming it, a non-string WrongKind at it.
const dTag = (j) => {
  if (!hasOwn(j, dDisc)) return dMissing(dDisc, 'missing or non-string ' + dDisc);
  const t = j[dDisc];
  return (typeof t === 'string') ? t : dAt(dDisc, () => dFail('WrongKind', 'string', 'missing or non-string ' + dDisc));
};
// A tag naming no case this decoder knows: UnknownTag at the discriminator.
const dUnknown = (expected, m) => dAt(dDisc, () => dFail('UnknownTag', expected, m));
const dObj = (j) => isObj(j) ? j : dFail('WrongKind', 'object', 'expected an object');
const dStr = (j) => (typeof j === 'string') ? j : dFail('WrongKind', 'string', 'expected a string');
// An int slot is the interpreter's 32-bit int: an integral number outside it is refused here as
// the interpreter and the compiled F# host refuse it (Phase 304), never read as a wider value.
// Phase 347 — and only a number the reader met as an INTEGER token: `1.0` and `1e0` are float
// tokens, which the F# reader reads as a float and an int slot refuses, though their value is whole.
const dInt = (j) => (typeof j === 'number' && !dFloatTok && Number.isInteger(j) && j >= -2147483648 && j <= 2147483647) ? j : dFail('WrongKind', 'int', 'expected an int');
// §7 — a float slot also accepts the three quoted non-finite sentinels `encFloat` emits
// (§5), and decodes them to the NUMBER, never the string. `dInt` above is not widened:
// §7 stops at the float slot.
const dFloat = (j) => {
  if (typeof j === 'number') return j;
  if (j === 'NaN') return NaN;
  if (j === 'Infinity') return Infinity;
  if (j === '-Infinity') return -Infinity;
  return dFail('WrongKind', 'number', 'expected a number');
};
const dBool = (j) => (typeof j === 'boolean') ? j : dFail('WrongKind', 'bool', 'expected a bool');
const dList = (dec) => (j) => Array.isArray(j) ? j.map((_, i) => dAt(i, () => dec(dRead(j, i)))) : dFail('WrongKind', 'array', 'expected an array');
// Phase 347 — a map decodes into a NULL-PROTOTYPE object: a key is data whatever it spells, so
// `__proto__` is an entry like any other (on a plain object it replaced the prototype, and every
// later read of the map went through the document's object), and no key reads one the prototype
// has (`constructor`, `toString`). Its entries are read in DOCUMENT order, as the interpreter and
// the F# host read them — an object lists an index-like key first. A repeated key in TEXT is the
// reader's refusal (Phase 462); one handed in as a value keeps its first entry, as the F# host's
// `dMap` does.
const dMap = (dec) => (j) => {
  const o = dObj(j);
  const out = Object.create(null);
  const members = dMembersOf.get(o);
  if (members === undefined) {
    for (const k of Object.keys(o)) out[k] = dAt(k, () => dec(dRead(o, k)));
  } else {
    for (const [k, v, floatTok] of members) {
      const d = dAt(k, () => { dFloatTok = floatTok; return dec(v); });
      if (!hasOwn(out, k)) out[k] = d;
    }
  }
  return out;
};
const dEnum = (name, cases) => (j) => {
  if (typeof j !== 'string') return dFail('WrongKind', 'string', 'not a ' + name);
  return (cases.indexOf(j) >= 0) ? j : dFail('UnknownTag', 'one of ' + cases.map((c) => "'" + c + "'").join(', '), 'not a ' + name);
};
const dReq = (name, fs, dec) => hasOwn(fs, name) ? dAt(name, () => dec(dRead(fs, name))) : dMissing(name, "missing required field '" + name + "'");
const dOpt = (name, fs, dec) => hasOwn(fs, name) ? dAt(name, () => dec(dRead(fs, name))) : undefined;
const dDef = (name, fs, dec, dflt) => hasOwn(fs, name) ? dAt(name, () => dec(dRead(fs, name))) : dflt;
// Phase 347 — a closure / opaque slot holds one fixed sentinel string and is read BY VALUE, as the
// interpreter reads it: another string is `OutOfRange` (a string, but not the one value the slot
// takes), any other kind `WrongKind`. It decodes to `null` — the sentinel carries nothing — and an
// optional slot's presence is still what tells `null` from `undefined`.
const dSentinel = (sentinel) => (j) => {
  if (j === sentinel) return null;
  return (typeof j === 'string')
    ? dFail('OutOfRange', 'string', 'expected the sentinel ' + sentinel)
    : dFail('WrongKind', 'string', 'expected a string');
};