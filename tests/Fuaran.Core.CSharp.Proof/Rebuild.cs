// ============================================================================
//  The reconstruction walk — READ every case through `Match` / `Switch`, and
//  re-CONSTRUCT the same value through the factories, using nothing but
//  `Fuaran.Core.CSharp`.
//
//  This file is the phase's acceptance criterion written as code: it constructs
//  and reads `ColExpr`, `Transform`, the hole-declaration family and `JVal`
//  without naming an F# option, list, tuple or union case anywhere. There is
//  deliberately no `using Microsoft.FSharp.*` in it, and no cast to a Core type —
//  if the facade were incomplete in any case, this file could not be written, and
//  if a reader and its factory disagreed, the round-trip law would say so.
// ============================================================================

namespace Fuaran.Core.CSharp.Proof;

internal static class Rebuild
{
    internal static CellValue Cell(CellValue v) =>
        v.Match(
            onInt: x => CellValue.Int(x),
            onFloat: x => CellValue.Float(x),
            onBool: x => CellValue.Bool(x),
            onStr: x => CellValue.Str(x),
            onDate: x => CellValue.Date(x),
            onTimestamp: x => CellValue.Timestamp(x),
            onNull: () => CellValue.Null
        );

    internal static Expr Expression(Expr e) =>
        e.Match(
            onCol: n => Expr.Col(n),
            onLiteral: c => Expr.Literal(Cell(c)),
            onParam: n => Expr.Param(n),
            onBinary: (op, l, r) => Expr.Binary(op, Expression(l), Expression(r)),
            onNot: x => Expr.Not(Expression(x)),
            onCoalesce: xs => Expr.Coalesce(xs.Select(x => Expression(x))),
            onCase: (arms, els) =>
                Expr.Case(arms.Select(a => new CaseArm(Expression(a.When), Expression(a.Then))), Expression(els)),
            onCast: (k, x) => Expr.Cast(k, Expression(x)),
            onApply: (fn, xs) => Expr.Apply(fn, xs.Select(x => Expression(x))),
            onInList: (s, xs) => Expr.InList(Expression(s), xs.Select(x => Expression(x))),
            onIsNull: x => Expr.IsNull(Expression(x)),
            onInParam: (s, n) => Expr.InParam(Expression(s), n),
            onNow: g => Expr.Now(g)
        );

    internal static ColumnValue ColumnOf(ColumnValue c) =>
        ColumnValue.Of(c.Name, c.Kind, c.Cells.Select(x => Cell(x)));

    internal static TableValue Table(TableValue t) => TableValue.Of(t.Columns.Select(c => ColumnOf(c)));

    internal static SourceValue Source(SourceValue s) =>
        s.Match(onEmbedded: t => SourceValue.Embedded(Table(t)), onReference: n => SourceValue.Reference(n));

    internal static WindowFunction Window(WindowFunction f) =>
        f.Kind switch
        {
            WindowFunctionKind.RowNumber => WindowFunction.RowNumber,
            WindowFunctionKind.Rank => WindowFunction.Rank,
            WindowFunctionKind.Lag => WindowFunction.Lag,
            WindowFunctionKind.Lead => WindowFunction.Lead,
            WindowFunctionKind.CumulSum => WindowFunction.CumulSum,
            WindowFunctionKind.RollingMean => WindowFunction.RollingMean,
            WindowFunctionKind.DenseRank => WindowFunction.DenseRank,
            WindowFunctionKind.CompetitionRank => WindowFunction.CompetitionRank,
            WindowFunctionKind.NTile => WindowFunction.NTile(
                f.Buckets ?? throw new InvalidOperationException("NTile with no bucket count")
            ),
            WindowFunctionKind.CumulMax => WindowFunction.CumulMax,
            WindowFunctionKind.CumulMin => WindowFunction.CumulMin,
            WindowFunctionKind.RollingSum => WindowFunction.RollingSum,
            _ => throw new InvalidOperationException($"unmodelled window function {f.Kind}"),
        };

    internal static AggregateSpec Aggregate(AggregateSpec a) => AggregateSpec.Of(a.Name, a.Function, a.OfColumn);

    internal static WindowStepSpec WindowStep(WindowStepSpec w) =>
        WindowStepSpec.Of(
            w.PartitionBy,
            w.OrderBy.Select(k => new SortKey(k.Column, k.Order)),
            Window(w.Function),
            w.OfColumn,
            w.AsColumn
        );

    internal static PivotStepSpec PivotStep(PivotStepSpec p) =>
        PivotStepSpec.Of(p.Index, p.OnColumn, p.ValuesColumn, p.Aggregate);

    internal static Step TransformStep(Step s) =>
        s.Match(
            onFilter: e => Step.Filter(Expression(e)),
            onProject: rs => Step.Project(rs.Select(r => new ColumnRename(r.Source, r.Output))),
            onDerive: (n, e) => Step.Derive(n, Expression(e)),
            onGroupBy: (keys, aggs) => Step.GroupBy(keys, aggs.Select(a => Aggregate(a))),
            onJoin: (src, keys, mode) =>
                Step.Join(Source(src), keys.Select(k => new JoinKey(k.LeftColumn, k.RightColumn)), mode),
            onWindow: w => Step.Window(WindowStep(w)),
            onPivot: p => Step.Pivot(PivotStep(p)),
            onUnpivot: (ids, vals) => Step.Unpivot(ids, vals),
            onSort: keys => Step.Sort(keys.Select(k => new SortSlot(k.Column, k.Order))),
            onDistinct: () => Step.Distinct,
            onLimit: (n, o) => Step.Limit(n, o),
            onUnion: src => Step.Union(Source(src)),
            onIntersect: src => Step.Intersect(Source(src)),
            onExcept: src => Step.Except(Source(src))
        );

    internal static Pipeline PipelineOf(Pipeline p) => Pipeline.Of(p.Steps.Select(s => TransformStep(s)));

    internal static JsonValue Json(JsonValue v) =>
        v.Match(
            onStr: x => JsonValue.Str(x),
            onInt: x => JsonValue.Int(x),
            onBool: x => JsonValue.Bool(x),
            onFloat: x => JsonValue.Float(x),
            onArray: xs => JsonValue.Array(xs.Select(x => Json(x))),
            onObject: ms => JsonValue.Object(ms.Select(m => new JsonMember(m.Name, Json(m.Value))))
        );

    internal static EffectSignature Effect(EffectSignature e) => EffectSignature.Of(e.Host, e.Determinism);

    internal static HoleSpace Space(HoleSpace s) =>
        s.Match(
            onIntRange: (lo, hi) => HoleSpace.IntRange(lo, hi),
            onFloatRange: (lo, hi) => HoleSpace.FloatRange(lo, hi),
            onStringLen: (lo, hi) => HoleSpace.StringLen(lo, hi),
            onEnumeration: ms => HoleSpace.Enumeration(ms),
            onAnyString: () => HoleSpace.AnyString
        );

    internal static HoleShape Shape(HoleShape k) =>
        k.Match(
            onValue: s => HoleShape.Value(Space(s)),
            onSlot: c => HoleShape.Slot(c),
            onRepeat: s => HoleShape.Repeat(Space(s)),
            onAction: e => HoleShape.Action(Effect(e))
        );

    internal static HoleSpec Hole(HoleSpec h) => HoleSpec.Of(h.Address, h.Name, Shape(h.Shape));

    internal static SignatureHoleView SignatureHole(SignatureHoleView h) =>
        SignatureHoleView.Of(
            h.Address,
            h.Name,
            h.Kind,
            h.Space is null ? null : Space(h.Space),
            h.Slot,
            h.Action is null ? null : Effect(h.Action),
            h.Required
        );

    internal static SignatureView Signature(SignatureView s) =>
        SignatureView.Of(s.Name, s.Holes.Select(h => SignatureHole(h)), Effect(s.Effect));
}
