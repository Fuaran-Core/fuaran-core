namespace Fuaran.Core

/// A minimal JSON value model shared by the Fable-clean *encode* path (`Json.render`)
/// and the portable *decode* path (`Json.parse`). Domains build their `"kind"`-tagged,
/// camelCase wire objects from these constructors. The Fuaran wire model has no `null`
/// and no non-finite float (`tryRender` rejects NaN/±Infinity at encode; `parse`
/// rejects overflowing tokens at decode).
///
/// **Numeric normalization — `JInt` and `JFloat` are one population on the wire.**
/// JSON has a single number type, so a whole-valued `JFloat` does not survive a
/// round-trip as `JFloat`: `render (JFloat 2.0)` emits `2` (shortest round-trip form),
/// which `parse` reads back as `JInt 2`. Pattern-matching `JFloat` alone on parsed
/// wire therefore silently misses whole values — read numbers through `JVal.asFloat`
/// (the blessed numeric accessor), or match `JInt`/`JFloat` together.
type JVal =
    /// A string. Any value renders, but only a well-formed UTF-16 one survives `parse`; the guarded
    /// renderers refuse a lone or ill-ordered surrogate.
    | JStr of string
    /// A number `parse` read from an integer token within Int32. A larger integer token arrives as
    /// `JFloat`, not here (exact up to 2^53; past it, only a double's canonical layout is admitted).
    | JInt of int
    /// The `true` / `false` literal; no reader coerces a number or a string to it.
    | JBool of bool
    /// A number with a point or an exponent, or an integer past Int32. Must be finite to render as
    /// valid JSON (`tryRender` refuses NaN / ±Infinity). A whole value within Int32 renders without
    /// a point, so it reads back from `parse` as `JInt`.
    | JFloat of float
    /// Items in order. `parse` caps nesting at `Json.defaultMaxDepth`; the renderers cap nothing.
    | JArr of JVal list
    /// Members in AUTHORED order, a repeated key kept as written: `Json.render` keeps the order,
    /// `Canon.render` sorts keys Ordinal, and every reader takes the FIRST member of a repeated key.
    | JObj of (string * JVal) list

/// Accessors over `JVal` that absorb the wire's numeric normalization (see the type doc:
/// a whole-valued `JFloat` round-trips as `JInt`, because JSON has one number type).
[<RequireQualifiedAccess>]
module JVal =

    /// The blessed numeric read path: a wire number as a `float`, whichever constructor
    /// the parser chose. `JInt` is exact in double (the parser's int53 guard bounds it);
    /// any other case is `None`. Prefer this over matching `JFloat` directly on parsed
    /// wire — `JFloat 2.0` comes back as `JInt 2`.
    let asFloat (v: JVal) : float option =
        match v with
        | JInt i -> Some(float i)
        | JFloat f -> Some f
        | _ -> None

    /// The JSON kind of a value, as every decode error on the spine names it (`string`, `int`,
    /// `bool`, `float`, `array`, `object`). Public once (Phase 299): `Decode`, `RowCodec` and the
    /// columnar codec each kept a private copy of this match.
    let kindName (v: JVal) : string =
        match v with
        | JStr _ -> "string"
        | JInt _ -> "int"
        | JBool _ -> "bool"
        | JFloat _ -> "float"
        | JArr _ -> "array"
        | JObj _ -> "object"

    /// THE spelling of a non-finite float on the spine (Phase 299): `NaN`, `Infinity` or
    /// `-Infinity`, and `None` for a finite value. Every place that names one — the guarded
    /// renderers' refusals, the canonical float layout's quoted token, the columnar codec's
    /// `NonFiniteFloat` and the column layer's distinct token — reads it here, so a non-finite
    /// value has one name on every surface.
    let nonFiniteToken (f: float) : string option =
        if System.Double.IsNaN f then
            Some "NaN"
        elif System.Double.IsPositiveInfinity f then
            Some "Infinity"
        elif System.Double.IsNegativeInfinity f then
            Some "-Infinity"
        else
            None
