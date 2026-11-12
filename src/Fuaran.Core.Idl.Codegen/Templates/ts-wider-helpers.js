// Phase 639 — the wider data universe, checked and written as the F# host and the interpreter do.
// The canonical order of a set's elements and a keyed map's keys: their canonical texts compared by
// code point (the order of their UTF-8 bytes); UTF-16 code-unit order agrees except between a
// surrogate and a unit at or above U+E000, which the remap puts right.
const canonicalCompare = (a, b) => {
  const point = (c) => (c >= 0xd800 && c <= 0xdfff) ? c + 0x2000 : (c >= 0xe000 ? c - 0x800 : c);
  const n = Math.min(a.length, b.length);
  for (let i = 0; i < n; i++) {
    const x = a.charCodeAt(i);
    const y = b.charCodeAt(i);
    if (x !== y) return point(x) - point(y);
  }
  return a.length - b.length;
};
// Items `[keyText, written]` in the canonical order of their keys.
const encOrdered = (items) => '[' + items.slice().sort((p, q) => canonicalCompare(p[0], q[0])).map((p) => p[1]).join(',') + ']';
const encInt64 = (n) => '"' + n.toString() + '"';
const b64 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
const encBytes = (u) => {
  let s = '';
  for (let i = 0; i < u.length; i += 3) {
    const n = (u[i] << 16) | ((i + 1 < u.length ? u[i + 1] : 0) << 8) | (i + 2 < u.length ? u[i + 2] : 0);
    s += b64[(n >> 18) & 63] + b64[(n >> 12) & 63]
      + (i + 1 < u.length ? b64[(n >> 6) & 63] : '=')
      + (i + 2 < u.length ? b64[n & 63] : '=');
  }
  return '"' + s + '"';
};
const encSet = (enc) => (xs) => encOrdered(xs.map((x) => { const t = enc(x); return [t, t]; }));
const encKeyedMap = (encK, encV) => (es) => encOrdered(es.map((e) => { const t = encK(e[0]); return [t, '[' + t + ',' + encV(e[1]) + ']']; }));
// The canonical spellings, read strictly: a reader admits the one spelling of a value.
const atMostText = (bound, d) => d.length < bound.length || (d.length === bound.length && d <= bound);
const isInt64Text = (s) => {
  const m = /^(-?)(0|[1-9][0-9]*)$/.exec(s);
  return m !== null && !(m[1] === '-' && m[2] === '0')
    && atMostText(m[1] === '-' ? '9223372036854775808' : '9223372036854775807', m[2]);
};
const isDecimalText = (s) => s !== '-0' && /^-?(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$/.test(s);
const isBytesText = (s) => /^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/][AQgw]==|[A-Za-z0-9+/]{2}[AEIMQUYcgkosw048]=)?$/.test(s);
const isTimeOfDayText = (s) => /^([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9](\.[0-9]{0,6}[1-9])?$/.test(s);
const isDurationText = (s) => {
  const m = /^(-?)(0|[1-9][0-9]*)(?:\.([0-9]{0,6}[1-9]))?s$/.exec(s);
  if (m === null || (m[1] === '-' && m[2] === '0' && m[3] === undefined)) return false;
  const ticks = (m[2] + (m[3] === undefined ? '' : m[3]).padEnd(7, '0')).replace(/^0+/, '');
  return atMostText(m[1] === '-' ? '9223372036854775808' : '9223372036854775807', ticks);
};
const dText = (name, admits) => (j) => {
  if (typeof j !== 'string') return dFail('WrongKind', 'string', 'expected a string');
  return admits(j) ? j : dFail('OutOfRange', "a canonical '" + name + "' string", "expected a canonical '" + name + "' string");
};
const dInt64 = (j) => BigInt(dText('int64', isInt64Text)(j));
const dBytes = (j) => {
  const s = dText('bytes', isBytesText)(j);
  const pad = s.endsWith('==') ? 2 : (s.endsWith('=') ? 1 : 0);
  const out = new Uint8Array(s.length / 4 * 3 - pad);
  const at = (c) => (c === '=' ? 0 : b64.indexOf(c));
  let o = 0;
  for (let i = 0; i < s.length; i += 4) {
    const n = (at(s[i]) << 18) | (at(s[i + 1]) << 12) | (at(s[i + 2]) << 6) | at(s[i + 3]);
    if (o < out.length) out[o++] = (n >> 16) & 255;
    if (o < out.length) out[o++] = (n >> 8) & 255;
    if (o < out.length) out[o++] = n & 255;
  }
  return out;
};
// Decoded items `[path, canonicalText]`, strictly ascending in canonical order: an equal text is the
// duplicate refusal and a lower one the order refusal, at the later item.
const dOrdered = (items) => {
  for (let i = 1; i < items.length; i++) {
    const a = items[i - 1][1];
    const b = items[i][1];
    const c = canonicalCompare(a, b);
    if (c >= 0) {
      const f = new DecodeFault('OutOfRange', c === 0 ? 'a distinct element' : 'an element ordered after ' + a, c === 0 ? 'a repeat of ' + b : b);
      f.path.push(...items[i][0]);
      throw f;
    }
  }
};
const dTuple = (decs) => (j) => {
  if (!Array.isArray(j)) return dFail('WrongKind', 'array', 'expected an array');
  if (j.length !== decs.length) return dFail('OutOfRange', 'an array of ' + decs.length + ' items', 'an array of ' + j.length + ' items');
  return decs.map((d, i) => dAt(i, () => d(dRead(j, i))));
};
const dSet = (enc, dec) => (j) => {
  const xs = dList(dec)(j);
  dOrdered(xs.map((x, i) => [[i], enc(x)]));
  return xs;
};
const dKeyedMap = (encK, decK, decV) => (j) => {
  if (!Array.isArray(j)) return dFail('WrongKind', 'array', 'expected an array');
  const es = j.map((_, i) => dAt(i, () => {
    const e = dRead(j, i);
    if (!Array.isArray(e)) return dFail('WrongKind', 'array', 'expected an array');
    if (e.length !== 2) return dFail('OutOfRange', 'a [key, value] array of 2 items', 'an array of ' + e.length + ' items');
    return [dAt(0, () => decK(dRead(e, 0))), dAt(1, () => decV(dRead(e, 1)))];
  }));
  dOrdered(es.map((e, i) => [[i, 0], encK(e[0])]));
  return es;
};
