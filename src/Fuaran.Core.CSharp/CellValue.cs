// ============================================================================
//  Fuaran.Core.CSharp — the scalar cell (Phase 128), a facade over `Fuaran.Core.Cell`.
//
//  Null/NA is a first-class case on the F# side, never a sentinel buried in the
//  data — so it is a first-class factory here (`CellValue.Null`) rather than a
//  nullable payload. `Date` / `Timestamp` carry their canonical ISO-8601 string,
//  exactly as the wrapped model does; the facade adds no host `DateTime`.
// ============================================================================

namespace Fuaran.Core.CSharp;

/// <summary>A single realized scalar cell.</summary>
public sealed class CellValue : IEquatable<CellValue>
{
    private readonly Cell _core;

    private CellValue(Cell core) => _core = core;

    /// <summary>An integer cell.</summary>
    public static CellValue Int(int value) => new(Cell.NewInt(value));

    /// <summary>A float cell.</summary>
    public static CellValue Float(double value) => new(Cell.NewFloat(value));

    /// <summary>A boolean cell.</summary>
    public static CellValue Bool(bool value) => new(Cell.NewBool(value));

    /// <summary>A string cell.</summary>
    public static CellValue Str(string value) => new(Cell.NewStr(Interop.NotNull(value, nameof(value))));

    /// <summary>A date cell, carrying its canonical <c>YYYY-MM-DD</c> string.</summary>
    public static CellValue Date(string iso8601) => new(Cell.NewDate(Interop.NotNull(iso8601, nameof(iso8601))));

    /// <summary>A timestamp cell, carrying its canonical <c>YYYY-MM-DDThh:mm:ssZ</c> string.</summary>
    public static CellValue Timestamp(string iso8601) =>
        new(Cell.NewTimestamp(Interop.NotNull(iso8601, nameof(iso8601))));

    /// <summary>The null / NA cell — the in-memory form of the wire's validity mask.</summary>
    public static CellValue Null { get; } = new(Cell.Null);

    /// <summary>Read this cell by case. Total: exactly one branch runs.</summary>
    public T Match<T>(
        Func<int, T> onInt,
        Func<double, T> onFloat,
        Func<bool, T> onBool,
        Func<string, T> onStr,
        Func<string, T> onDate,
        Func<string, T> onTimestamp,
        Func<T> onNull
    ) =>
        _core.Tag switch
        {
            Cell.Tags.Int => onInt(((Cell.Int)_core).Item),
            Cell.Tags.Float => onFloat(((Cell.Float)_core).Item),
            Cell.Tags.Bool => onBool(((Cell.Bool)_core).Item),
            Cell.Tags.Str => onStr(((Cell.Str)_core).Item),
            Cell.Tags.Date => onDate(((Cell.Date)_core).Item),
            Cell.Tags.Timestamp => onTimestamp(((Cell.Timestamp)_core).Item),
            Cell.Tags.Null => onNull(),
            _ => throw Interop.UnknownCase(nameof(Cell), _core.Tag),
        };

    /// <summary>Read this cell by case for effect. Total: exactly one branch runs.</summary>
    public void Switch(
        Action<int> onInt,
        Action<double> onFloat,
        Action<bool> onBool,
        Action<string> onStr,
        Action<string> onDate,
        Action<string> onTimestamp,
        Action onNull
    ) =>
        Match(
            v =>
            {
                onInt(v);
                return true;
            },
            v =>
            {
                onFloat(v);
                return true;
            },
            v =>
            {
                onBool(v);
                return true;
            },
            v =>
            {
                onStr(v);
                return true;
            },
            v =>
            {
                onDate(v);
                return true;
            },
            v =>
            {
                onTimestamp(v);
                return true;
            },
            () =>
            {
                onNull();
                return true;
            }
        );

    /// <summary>The bridge OUT: the wrapped F# value.</summary>
    public Cell ToCore() => _core;

    /// <summary>The bridge IN: wrap an F# value.</summary>
    public static CellValue FromCore(Cell core) => new(Interop.NotNull(core, nameof(core)));

    /// <inheritdoc />
    public bool Equals(CellValue? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as CellValue);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _core.ToString();
}
