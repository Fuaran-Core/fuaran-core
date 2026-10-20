const dMaxDepth = __CAP__;
const dParse = (s) => {
  const n = s.length;
  let i = 0;
  // The path to the value being read, root first: a member's key or an item's index per open
  // container (Phase 519) — where a container opened past the cap is refused.
  const at = [];
  // Whether the value just read was a WHOLE number written as a float token.
  let floatTok = false;
  const bad = (m) => { throw new DecodeFault('InvalidJson', 'JSON text', 'not valid JSON: ' + m); };
  const ws = () => {
    while (i < n) {
      const c = s.charCodeAt(i);
      if (c === 32 || c === 9 || c === 10 || c === 13) i++;
      else break;
    }
  };
  const expect = (c) => { if (i < n && s[i] === c) i++; else bad("expected '" + c + "'"); };
  const markFloat = (c, k) => {
    let at = dFloatAt.get(c);
    if (at === undefined) {
      at = new Set();
      dFloatAt.set(c, at);
    }
    at.add(k);
  };
  // A string is well-formed UTF-16 or refused, whichever spelling a unit arrived in; a raw control
  // character (U+0000-U+001F) is refused at the character (Phase 462); any other is read as itself.
  const str = () => {
    expect('"');
    let out = '';
    let run = i;
    let high = false;
    const unit = (u) => {
      const low = u >= 0xDC00 && u <= 0xDFFF;
      if (high && !low) bad('ill-formed string: a high surrogate not followed by a low surrogate');
      if (!high && low) bad('ill-formed string: a low surrogate with no high surrogate before it');
      high = u >= 0xD800 && u <= 0xDBFF;
      out += String.fromCharCode(u);
    };
    for (;;) {
      if (i >= n) bad('unterminated string');
      const c = s.charCodeAt(i);
      if (c < 0x20) bad('unescaped control character U+00' + (c < 16 ? '0' : '') + c.toString(16).toUpperCase() + ' in a string');
      if (!high && c !== 34 && c !== 92 && (c < 0xD800 || c > 0xDFFF)) {
        i++;
        continue;
      }
      out += s.slice(run, i);
      i++;
      if (c === 34) {
        if (high) bad('ill-formed string: a high surrogate not followed by a low surrogate');
        return out;
      }
      if (c !== 92) unit(c);
      else {
        if (i >= n) bad('unterminated escape');
        const e = s[i++];
        if (e === '"') unit(34);
        else if (e === '\\') unit(92);
        else if (e === '/') unit(47);
        else if (e === 'n') unit(10);
        else if (e === 'r') unit(13);
        else if (e === 't') unit(9);
        else if (e === 'b') unit(8);
        else if (e === 'f') unit(12);
        else if (e === 'u') {
          if (i + 4 > n) bad('truncated \\u escape');
          const h = s.slice(i, i + 4);
          if (!/^[0-9a-fA-F]{4}$/.test(h)) bad('bad hex digit in \\u escape');
          i += 4;
          unit(parseInt(h, 16));
        } else bad("bad escape '\\" + e + "'");
      }
      run = i;
    }
  };
  // The F# reader's number: the JSON grammar exactly; a float token finite; an integer token an
  // int within Int32, else a float while |n| <= 2^53, else only the canonical layout of its double
  // (which writes a 16- or 17-digit whole double in full, and every longer one with an exponent).
  const num = () => {
    const start = i;
    let isFloat = false;
    const digits = () => { while (i < n && s[i] >= '0' && s[i] <= '9') i++; };
    if (s[i] === '-') i++;
    digits();
    if (s[i] === '.') {
      isFloat = true;
      i++;
      digits();
    }
    if (s[i] === 'e' || s[i] === 'E') {
      isFloat = true;
      i++;
      if (s[i] === '+' || s[i] === '-') i++;
      digits();
    }
    const tok = s.slice(start, i);
    if (!/^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$/.test(tok)) bad('malformed number: ' + tok);
    const v = Number(tok);
    if (isFloat) {
      if (!Number.isFinite(v)) bad('number outside the finite double range; it cannot round-trip on the wire: ' + tok);
      floatTok = Number.isInteger(v) && v >= -2147483648 && v <= 2147483647;
      return v;
    }
    if (v >= -2147483648 && v <= 2147483647) return v;
    const mag = tok[0] === '-' ? tok.slice(1) : tok;
    if (mag.length < 16 || (mag.length === 16 && mag <= '9007199254740992')) return v;
    if (mag.length <= 17 && String(v) === tok) return v;
    return bad('integer literal outside the int53 safe range (|n| > 2^53); it cannot round-trip without precision loss: ' + tok);
  };
  const lit = (word, v) => {
    if (s.startsWith(word, i)) {
      i += word.length;
      return v;
    }
    return bad("expected '" + word + "'");
  };
  const deep = (depth) => {
    if (depth >= dMaxDepth) {
      const f = new DecodeFault('LimitExceeded', 'within the depth bound of ' + dMaxDepth, 'limit depth of ' + dMaxDepth + ' exceeded');
      f.path = at.slice();
      throw f;
    }
  };
  const obj = (depth) => {
    deep(depth);
    expect('{');
    ws();
    const o = {};
    const members = [];
    let reordered = false;
    if (s[i] === '}') {
      i++;
      return o;
    }
    for (;;) {
      ws();
      const k = str();
      ws();
      expect(':');
      ws();
      at.push(k);
      const v = val(depth + 1);
      at.pop();
      const f = floatTok;
      // Phase 462: a repeated member name is refused, as the F# reader refuses it, once its value
      // is read — a refusal inside the value wins, and a repeated null is the null's refusal.
      if (hasOwn(o, k)) bad('repeated member name in one object');
      members.push([k, v, f]);
      if (k === '__proto__') Object.defineProperty(o, k, { value: v, writable: true, enumerable: true, configurable: true });
      else o[k] = v;
      if (f) markFloat(o, k);
      if (/^(0|[1-9][0-9]*)$/.test(k)) reordered = true;
      ws();
      if (s[i] === ',') i++;
      else if (s[i] === '}') {
        i++;
        break;
      } else bad("expected ',' or '}'");
    }
    if (reordered) dMembersOf.set(o, members);
    return o;
  };
  const arr = (depth) => {
    deep(depth);
    expect('[');
    ws();
    const a = [];
    if (s[i] === ']') {
      i++;
      return a;
    }
    for (;;) {
      at.push(a.length);
      const v = val(depth + 1);
      at.pop();
      if (floatTok) markFloat(a, a.length);
      a.push(v);
      ws();
      if (s[i] === ',') i++;
      else if (s[i] === ']') {
        i++;
        break;
      } else bad("expected ',' or ']'");
    }
    return a;
  };
  const val = (depth) => {
    ws();
    floatTok = false;
    if (i >= n) return bad('unexpected end of input');
    const c = s[i];
    let v;
    if (c === '"') v = str();
    else if (c === '{') v = obj(depth);
    else if (c === '[') v = arr(depth);
    else if (c === 't') v = lit('true', true);
    else if (c === 'f') v = lit('false', false);
    else if (c === 'n') return bad('null is not representable in the Fuaran wire JVal model');
    else if (c === '-' || (c >= '0' && c <= '9')) return num();
    else return bad("unexpected character '" + c + "'");
    floatTok = false;
    return v;
  };
  const root = val(0);
  ws();
  if (i !== n) bad('trailing characters');
  return root;
};