// ============================================================================
//  Fuaran.Core.CSharp — the closed nullary vocabularies, as C# enums (Phase 128).
//
//  Every union in this file is nullary in every case on the F# side (`ColumnType`,
//  `AggFn`, `HostEffect`, `DeterminismSource`), so a C# enum carries it exactly — a
//  caller writes `ColumnKind.Int` and switches over it with the compiler's own
//  exhaustiveness advice, instead of reaching for an F# union's static property.
//
//  The dataframe layer's vocabularies (`BinOp`, `ScalarFn`, `JoinKind`, `SortDir`,
//  `NowGrain`) ship from Fuaran.Core.DataFrame.CSharp since Phase 257, in this same
//  namespace, with the rest of the dataframe half of the facade (DECISIONS.md D68).
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

/// <summary>
/// The declared bridge for the column-layer vocabularies (Phase 257). An enum cannot carry a
/// <c>ToCore</c> / <c>FromCore</c> pair of its own, so the pair lives here: the one place a caller —
/// the dataframe half of the facade among them — converts a <see cref="ColumnKind"/> or an
/// <see cref="AggregateFunction"/> to the Core union and back.
/// </summary>
public static class Vocabulary
{
    /// <summary>The Core <c>ColumnType</c> for a column kind.</summary>
    public static ColumnType ToCore(ColumnKind kind) => Vocab.ToCore(kind);

    /// <summary>The column kind for a Core <c>ColumnType</c>.</summary>
    public static ColumnKind FromCore(ColumnType type) => Vocab.ColumnKindOf(Interop.NotNull(type, nameof(type)));

    /// <summary>The Core <c>AggFn</c> for an aggregate function.</summary>
    public static AggFn ToCore(AggregateFunction function) => Vocab.ToCore(function);

    /// <summary>The aggregate function for a Core <c>AggFn</c>.</summary>
    public static AggregateFunction FromCore(AggFn function) =>
        Vocab.AggregateFunctionOf(Interop.NotNull(function, nameof(function)));
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
