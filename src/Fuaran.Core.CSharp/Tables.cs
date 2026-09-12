// ============================================================================
//  Fuaran.Core.CSharp — the columnar carrier (Phase 128): schema entries, columns,
//  embedded tables and the data source a `Join` / `Union` / `Intersect` / `Except`
//  step names.
//
//  `TableValue.Of` DERIVES the schema from the columns it is given, because the
//  wrapped model's invariant is that column order follows the schema — a caller
//  that could state the two independently could state them inconsistently, and the
//  codec would refuse the result somewhere else entirely. `FromCore` preserves
//  whatever schema the F# value carries, so wrapping never rewrites a value.
// ============================================================================

namespace Fuaran.Core.CSharp;

/// <summary>One <c>(name, type)</c> entry of a table's ordered schema.</summary>
public sealed class SchemaEntry : IEquatable<SchemaEntry>
{
    /// <summary>Create a schema entry.</summary>
    public SchemaEntry(string name, ColumnKind kind)
    {
        Name = Interop.NotNull(name, nameof(name));
        Kind = kind;
    }

    /// <summary>The column name.</summary>
    public string Name { get; }

    /// <summary>The column's declared scalar type.</summary>
    public ColumnKind Kind { get; }

    /// <inheritdoc />
    public bool Equals(SchemaEntry? other) => other is not null && Name == other.Name && Kind == other.Kind;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SchemaEntry);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name, Kind);

    /// <inheritdoc />
    public override string ToString() => $"{Name}: {Kind}";
}

/// <summary>A typed, null-aware column.</summary>
public sealed class ColumnValue : IEquatable<ColumnValue>
{
    private readonly Column _core;

    private ColumnValue(Column core) => _core = core;

    /// <summary>Create a column from its name, declared type and cells.</summary>
    public static ColumnValue Of(string name, ColumnKind kind, IEnumerable<CellValue> cells) =>
        new(
            new Column(
                Interop.NotNull(name, nameof(name)),
                Vocab.ToCore(kind),
                Interop.List(Interop.Items(cells, nameof(cells)).Select(c => c.ToCore()))
            )
        );

    /// <summary>The column name.</summary>
    public string Name => _core.Name;

    /// <summary>The column's declared scalar type.</summary>
    public ColumnKind Kind => Vocab.ColumnKindOf(_core.Type);

    /// <summary>The column's cells, co-indexed with the table's rows.</summary>
    public IReadOnlyList<CellValue> Cells => Interop.Read(_core.Cells).Select(CellValue.FromCore).ToArray();

    /// <summary>The bridge OUT: the wrapped F# value.</summary>
    public Column ToCore() => _core;

    /// <summary>The bridge IN: wrap an F# value.</summary>
    public static ColumnValue FromCore(Column core) => new(Interop.NotNull(core, nameof(core)));

    /// <inheritdoc />
    public bool Equals(ColumnValue? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ColumnValue);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _core.ToString();
}

/// <summary>An embedded columnar table: its ordered schema plus the columns.</summary>
public sealed class TableValue : IEquatable<TableValue>
{
    private readonly Table _core;

    private TableValue(Table core) => _core = core;

    /// <summary>Create a table from its columns; the schema is derived from them, in their order.</summary>
    public static TableValue Of(IEnumerable<ColumnValue> columns)
    {
        var cols = Interop.Items(columns, nameof(columns));

        return new TableValue(
            new Table(
                Interop.List(cols.Select(c => Tuple.Create(c.Name, Vocab.ToCore(c.Kind)))),
                Interop.List(cols.Select(c => c.ToCore()))
            )
        );
    }

    /// <summary>Create a table from its columns; the schema is derived from them, in their order.</summary>
    public static TableValue Of(params ColumnValue[] columns) => Of((IEnumerable<ColumnValue>)columns);

    /// <summary>The table's ordered schema.</summary>
    public IReadOnlyList<SchemaEntry> Schema =>
        Interop.Read(_core.Schema).Select(e => new SchemaEntry(e.Item1, Vocab.ColumnKindOf(e.Item2))).ToArray();

    /// <summary>The table's columns, in schema order.</summary>
    public IReadOnlyList<ColumnValue> Columns => Interop.Read(_core.Columns).Select(ColumnValue.FromCore).ToArray();

    /// <summary>The bridge OUT: the wrapped F# value.</summary>
    public Table ToCore() => _core;

    /// <summary>The bridge IN: wrap an F# value.</summary>
    public static TableValue FromCore(Table core) => new(Interop.NotNull(core, nameof(core)));

    /// <inheritdoc />
    public bool Equals(TableValue? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as TableValue);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _core.ToString();
}

/// <summary>A data source: embedded columns, or a host-resolved named reference.</summary>
public sealed class SourceValue : IEquatable<SourceValue>
{
    private readonly DataSource _core;

    private SourceValue(DataSource core) => _core = core;

    /// <summary>An embedded table — the rows travel with the pipeline.</summary>
    public static SourceValue Embedded(TableValue table) =>
        new(DataSource.NewEmbedded(Interop.NotNull(table, nameof(table)).ToCore()));

    /// <summary>A named source the evaluator resolves host-side — the wire carries the name, never the rows.</summary>
    public static SourceValue Reference(string name) =>
        new(DataSource.NewRef(Interop.NotNull(name, nameof(name))));

    /// <summary>Read this source by case. Total: exactly one branch runs.</summary>
    public T Match<T>(Func<TableValue, T> onEmbedded, Func<string, T> onReference) =>
        _core.Tag switch
        {
            DataSource.Tags.Embedded => onEmbedded(TableValue.FromCore(((DataSource.Embedded)_core).Item)),
            DataSource.Tags.Ref => onReference(((DataSource.Ref)_core).Item),
            _ => throw Interop.UnknownCase(nameof(DataSource), _core.Tag),
        };

    /// <summary>Read this source by case for effect. Total: exactly one branch runs.</summary>
    public void Switch(Action<TableValue> onEmbedded, Action<string> onReference) =>
        Match(
            t =>
            {
                onEmbedded(t);
                return true;
            },
            n =>
            {
                onReference(n);
                return true;
            }
        );

    /// <summary>The bridge OUT: the wrapped F# value.</summary>
    public DataSource ToCore() => _core;

    /// <summary>The bridge IN: wrap an F# value.</summary>
    public static SourceValue FromCore(DataSource core) => new(Interop.NotNull(core, nameof(core)));

    /// <inheritdoc />
    public bool Equals(SourceValue? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SourceValue);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _core.ToString();
}
