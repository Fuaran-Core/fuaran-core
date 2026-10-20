// Phase 252 — a hosted slot's declared string format, checked as the F# host checks it.
const dFormatDay = (y, m, d) => {
  if (y < 1 || m < 1 || m > 12 || d < 1) return false;
  const last = new Date(0);
  last.setUTCFullYear(y, m, 0);
  return d <= last.getUTCDate();
};
const dFormat = (format, v) => {
  let ok = false;
  if (typeof v === 'string') {
    if (format === 'date') {
      const m = /^([0-9]{4})-([0-9]{2})-([0-9]{2})$/.exec(v);
      ok = m !== null && dFormatDay(+m[1], +m[2], +m[3]);
    } else if (format === 'date-time') {
      const m = /^([0-9]{4})-([0-9]{2})-([0-9]{2})[Tt]([0-9]{2}):([0-9]{2}):([0-9]{2})([.][0-9]+)?([Zz]|[+-]([0-9]{2}):([0-9]{2}))$/.exec(v);
      ok = m !== null && dFormatDay(+m[1], +m[2], +m[3]) && +m[4] <= 23 && +m[5] <= 59 && +m[6] <= 59
        && (m[9] === undefined || (+m[9] <= 23 && +m[10] <= 59));
    } else if (format === 'uuid') {
      ok = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/.test(v);
    }
  }
  return ok ? v : dFail('OutOfRange', "a '" + format + "' string", "expected a '" + format + "' string");
};