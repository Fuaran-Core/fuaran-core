// ============================================================================
//  Fuaran.Core.CSharp — the closed nullary vocabularies, as C# enums (Phase 128).
//
//  Every union in this file is nullary in every case on the F# side (`ColumnType`,
//  `BinOp`, `ScalarFn`, `AggFn`, `JoinKind`, `SortDir`, `HostEffect`,
//  `DeterminismSource`), so a C# enum carries it exactly — a caller writes
//  `BinaryOperator.Add` and switches over it with the compiler's own
//  exhaustiveness advice, instead of reaching for an F# union's static property.
//
//  `WindowFn` is deliberately NOT here: `NTile` carries a bucket count, so it is a
//  class with a `Kind` + `Buckets` reader (see Steps.cs).
//
//  The mapping is written out case by case rather than derived from the tag
//  ordinal. An ordinal mapping would silently re-point every value if a case were
//  ever inserted mid-union; an explicit map makes that a compile error on this
//  side, which is where it is cheap.
// ============================================================================

namespace Fuaran.Core.CSharp;

/// <summary>The fixed, Arrow-compatible scalar type set a column ranges over.</summary>
public enum ColumnKind
{
    Int,
    Float,
    Bool,
    String,
    Date,
    Timestamp,
}

/// <summary>A binary operator: arithmetic, comparison, logical, or an ordinal substring predicate.</summary>
public enum BinaryOperator
{
    Add,
    Sub,
    Mul,
    Div,
    Mod,
    Eq,
    Ne,
    Lt,
    Le,
    Gt,
    Ge,
    And,
    Or,
    Contains,
    StartsWith,
    EndsWith,
}

/// <summary>The fixed scalar-function set an expression may apply.</summary>
public enum ScalarFunction
{
    Abs,
    Round,
    Floor,
    Ceil,
    Length,
    Lower,
    Upper,
    Substr,
    DatePart,
    Concat,
    Trim,
    Replace,
    DateDiffDays,
    Sqrt,
    Least,
    Greatest,
    IndexOf,
}

/// <summary>The group / window aggregate function set.</summary>
public enum AggregateFunction
{
    Sum,
    Mean,
    Min,
    Max,
    Count,
    Median,
    StdDev,
    First,
    Last,
    CountDistinct,
}

/// <summary>How a join pairs its two sides.</summary>
public enum JoinMode
{
    Inner,
    Left,
    Right,
    Outer,
    Semi,
    Anti,
}

/// <summary>A sort direction.</summary>
public enum SortOrder
{
    Asc,
    Desc,
}

/// <summary>Effect axis 1 — does the artifact touch the world.</summary>
public enum HostEffectKind
{
    Pure,
    ReadsHost,
    WritesHost,
}

/// <summary>Effect axis 2 — does the artifact read a non-deterministic source.</summary>
public enum DeterminismKind
{
    Deterministic,
    Clock,
    Random,
    Network,
}

internal static class Vocab
{
    internal static ColumnType ToCore(ColumnKind k) =>
        k switch
        {
            ColumnKind.Int => ColumnType.IntType,
            ColumnKind.Float => ColumnType.FloatType,
            ColumnKind.Bool => ColumnType.BoolType,
            ColumnKind.String => ColumnType.StringType,
            ColumnKind.Date => ColumnType.DateType,
            ColumnKind.Timestamp => ColumnType.TimestampType,
            _ => throw new ArgumentOutOfRangeException(nameof(k), k, "not a ColumnKind"),
        };

    internal static ColumnKind ColumnKindOf(ColumnType t) =>
        t.Tag switch
        {
            ColumnType.Tags.IntType => ColumnKind.Int,
            ColumnType.Tags.FloatType => ColumnKind.Float,
            ColumnType.Tags.BoolType => ColumnKind.Bool,
            ColumnType.Tags.StringType => ColumnKind.String,
            ColumnType.Tags.DateType => ColumnKind.Date,
            ColumnType.Tags.TimestampType => ColumnKind.Timestamp,
            _ => throw Interop.UnknownCase(nameof(ColumnType), t.Tag),
        };

    internal static BinOp ToCore(BinaryOperator op) =>
        op switch
        {
            BinaryOperator.Add => BinOp.Add,
            BinaryOperator.Sub => BinOp.Sub,
            BinaryOperator.Mul => BinOp.Mul,
            BinaryOperator.Div => BinOp.Div,
            BinaryOperator.Mod => BinOp.Mod,
            BinaryOperator.Eq => BinOp.Eq,
            BinaryOperator.Ne => BinOp.Ne,
            BinaryOperator.Lt => BinOp.Lt,
            BinaryOperator.Le => BinOp.Le,
            BinaryOperator.Gt => BinOp.Gt,
            BinaryOperator.Ge => BinOp.Ge,
            BinaryOperator.And => BinOp.And,
            BinaryOperator.Or => BinOp.Or,
            BinaryOperator.Contains => BinOp.Contains,
            BinaryOperator.StartsWith => BinOp.StartsWith,
            BinaryOperator.EndsWith => BinOp.EndsWith,
            _ => throw new ArgumentOutOfRangeException(nameof(op), op, "not a BinaryOperator"),
        };

    internal static BinaryOperator BinaryOperatorOf(BinOp op) =>
        op.Tag switch
        {
            BinOp.Tags.Add => BinaryOperator.Add,
            BinOp.Tags.Sub => BinaryOperator.Sub,
            BinOp.Tags.Mul => BinaryOperator.Mul,
            BinOp.Tags.Div => BinaryOperator.Div,
            BinOp.Tags.Mod => BinaryOperator.Mod,
            BinOp.Tags.Eq => BinaryOperator.Eq,
            BinOp.Tags.Ne => BinaryOperator.Ne,
            BinOp.Tags.Lt => BinaryOperator.Lt,
            BinOp.Tags.Le => BinaryOperator.Le,
            BinOp.Tags.Gt => BinaryOperator.Gt,
            BinOp.Tags.Ge => BinaryOperator.Ge,
            BinOp.Tags.And => BinaryOperator.And,
            BinOp.Tags.Or => BinaryOperator.Or,
            BinOp.Tags.Contains => BinaryOperator.Contains,
            BinOp.Tags.StartsWith => BinaryOperator.StartsWith,
            BinOp.Tags.EndsWith => BinaryOperator.EndsWith,
            _ => throw Interop.UnknownCase(nameof(BinOp), op.Tag),
        };

    internal static ScalarFn ToCore(ScalarFunction fn) =>
        fn switch
        {
            ScalarFunction.Abs => ScalarFn.Abs,
            ScalarFunction.Round => ScalarFn.Round,
            ScalarFunction.Floor => ScalarFn.Floor,
            ScalarFunction.Ceil => ScalarFn.Ceil,
            ScalarFunction.Length => ScalarFn.Length,
            ScalarFunction.Lower => ScalarFn.Lower,
            ScalarFunction.Upper => ScalarFn.Upper,
            ScalarFunction.Substr => ScalarFn.Substr,
            ScalarFunction.DatePart => ScalarFn.DatePart,
            ScalarFunction.Concat => ScalarFn.Concat,
            ScalarFunction.Trim => ScalarFn.Trim,
            ScalarFunction.Replace => ScalarFn.Replace,
            ScalarFunction.DateDiffDays => ScalarFn.DateDiffDays,
            ScalarFunction.Sqrt => ScalarFn.Sqrt,
            ScalarFunction.Least => ScalarFn.Least,
            ScalarFunction.Greatest => ScalarFn.Greatest,
            ScalarFunction.IndexOf => ScalarFn.IndexOf,
            _ => throw new ArgumentOutOfRangeException(nameof(fn), fn, "not a ScalarFunction"),
        };

    internal static ScalarFunction ScalarFunctionOf(ScalarFn fn) =>
        fn.Tag switch
        {
            ScalarFn.Tags.Abs => ScalarFunction.Abs,
            ScalarFn.Tags.Round => ScalarFunction.Round,
            ScalarFn.Tags.Floor => ScalarFunction.Floor,
            ScalarFn.Tags.Ceil => ScalarFunction.Ceil,
            ScalarFn.Tags.Length => ScalarFunction.Length,
            ScalarFn.Tags.Lower => ScalarFunction.Lower,
            ScalarFn.Tags.Upper => ScalarFunction.Upper,
            ScalarFn.Tags.Substr => ScalarFunction.Substr,
            ScalarFn.Tags.DatePart => ScalarFunction.DatePart,
            ScalarFn.Tags.Concat => ScalarFunction.Concat,
            ScalarFn.Tags.Trim => ScalarFunction.Trim,
            ScalarFn.Tags.Replace => ScalarFunction.Replace,
            ScalarFn.Tags.DateDiffDays => ScalarFunction.DateDiffDays,
            ScalarFn.Tags.Sqrt => ScalarFunction.Sqrt,
            ScalarFn.Tags.Least => ScalarFunction.Least,
            ScalarFn.Tags.Greatest => ScalarFunction.Greatest,
            ScalarFn.Tags.IndexOf => ScalarFunction.IndexOf,
            _ => throw Interop.UnknownCase(nameof(ScalarFn), fn.Tag),
        };

    internal static AggFn ToCore(AggregateFunction fn) =>
        fn switch
        {
            AggregateFunction.Sum => AggFn.Sum,
            AggregateFunction.Mean => AggFn.Mean,
            AggregateFunction.Min => AggFn.Min,
            AggregateFunction.Max => AggFn.Max,
            AggregateFunction.Count => AggFn.Count,
            AggregateFunction.Median => AggFn.Median,
            AggregateFunction.StdDev => AggFn.StdDev,
            AggregateFunction.First => AggFn.First,
            AggregateFunction.Last => AggFn.Last,
            AggregateFunction.CountDistinct => AggFn.CountDistinct,
            _ => throw new ArgumentOutOfRangeException(nameof(fn), fn, "not an AggregateFunction"),
        };

    internal static AggregateFunction AggregateFunctionOf(AggFn fn) =>
        fn.Tag switch
        {
            AggFn.Tags.Sum => AggregateFunction.Sum,
            AggFn.Tags.Mean => AggregateFunction.Mean,
            AggFn.Tags.Min => AggregateFunction.Min,
            AggFn.Tags.Max => AggregateFunction.Max,
            AggFn.Tags.Count => AggregateFunction.Count,
            AggFn.Tags.Median => AggregateFunction.Median,
            AggFn.Tags.StdDev => AggregateFunction.StdDev,
            AggFn.Tags.First => AggregateFunction.First,
            AggFn.Tags.Last => AggregateFunction.Last,
            AggFn.Tags.CountDistinct => AggregateFunction.CountDistinct,
            _ => throw Interop.UnknownCase(nameof(AggFn), fn.Tag),
        };

    internal static JoinKind ToCore(JoinMode m) =>
        m switch
        {
            JoinMode.Inner => JoinKind.Inner,
            JoinMode.Left => JoinKind.Left,
            JoinMode.Right => JoinKind.Right,
            JoinMode.Outer => JoinKind.Outer,
            JoinMode.Semi => JoinKind.Semi,
            JoinMode.Anti => JoinKind.Anti,
            _ => throw new ArgumentOutOfRangeException(nameof(m), m, "not a JoinMode"),
        };

    internal static JoinMode JoinModeOf(JoinKind k) =>
        k.Tag switch
        {
            JoinKind.Tags.Inner => JoinMode.Inner,
            JoinKind.Tags.Left => JoinMode.Left,
            JoinKind.Tags.Right => JoinMode.Right,
            JoinKind.Tags.Outer => JoinMode.Outer,
            JoinKind.Tags.Semi => JoinMode.Semi,
            JoinKind.Tags.Anti => JoinMode.Anti,
            _ => throw Interop.UnknownCase(nameof(JoinKind), k.Tag),
        };

    internal static SortDir ToCore(SortOrder o) =>
        o switch
        {
            SortOrder.Asc => SortDir.Asc,
            SortOrder.Desc => SortDir.Desc,
            _ => throw new ArgumentOutOfRangeException(nameof(o), o, "not a SortOrder"),
        };

    internal static SortOrder SortOrderOf(SortDir d) =>
        d.Tag switch
        {
            SortDir.Tags.Asc => SortOrder.Asc,
            SortDir.Tags.Desc => SortOrder.Desc,
            _ => throw Interop.UnknownCase(nameof(SortDir), d.Tag),
        };

    internal static HostEffect ToCore(HostEffectKind h) =>
        h switch
        {
            HostEffectKind.Pure => HostEffect.Pure,
            HostEffectKind.ReadsHost => HostEffect.ReadsHost,
            HostEffectKind.WritesHost => HostEffect.WritesHost,
            _ => throw new ArgumentOutOfRangeException(nameof(h), h, "not a HostEffectKind"),
        };

    internal static HostEffectKind HostEffectKindOf(HostEffect h) =>
        h.Tag switch
        {
            HostEffect.Tags.Pure => HostEffectKind.Pure,
            HostEffect.Tags.ReadsHost => HostEffectKind.ReadsHost,
            HostEffect.Tags.WritesHost => HostEffectKind.WritesHost,
            _ => throw Interop.UnknownCase(nameof(HostEffect), h.Tag),
        };

    internal static DeterminismSource ToCore(DeterminismKind d) =>
        d switch
        {
            DeterminismKind.Deterministic => DeterminismSource.Deterministic,
            DeterminismKind.Clock => DeterminismSource.Clock,
            DeterminismKind.Random => DeterminismSource.Random,
            DeterminismKind.Network => DeterminismSource.Network,
            _ => throw new ArgumentOutOfRangeException(nameof(d), d, "not a DeterminismKind"),
        };

    internal static DeterminismKind DeterminismKindOf(DeterminismSource d) =>
        d.Tag switch
        {
            DeterminismSource.Tags.Deterministic => DeterminismKind.Deterministic,
            DeterminismSource.Tags.Clock => DeterminismKind.Clock,
            DeterminismSource.Tags.Random => DeterminismKind.Random,
            DeterminismSource.Tags.Network => DeterminismKind.Network,
            _ => throw Interop.UnknownCase(nameof(DeterminismSource), d.Tag),
        };
}
