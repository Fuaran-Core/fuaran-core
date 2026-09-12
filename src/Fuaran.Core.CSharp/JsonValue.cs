// ============================================================================
//  Fuaran.Core.CSharp — the wire JSON model (Phase 128), a facade over
//  `Fuaran.Core.JVal`.
//
//  The model has no `null` case, deliberately, and the facade does not invent one:
//  an absent member is absent from the object, never a null-valued one. Rendering
//  goes through the GUARDED `tryRender`, so a non-finite float is refused by name
//  here exactly as it is on the F# side rather than producing a document no reader
//  accepts.
// ============================================================================

namespace Fuaran.Core.CSharp;

/// <summary>One <c>(name, value)</c> member of a JSON object.</summary>
public sealed class JsonMember : IEquatable<JsonMember>
{
    /// <summary>Create a member.</summary>
    public JsonMember(string name, JsonValue value)
    {
        Name = Interop.NotNull(name, nameof(name));
        Value = Interop.NotNull(value, nameof(value));
    }

    /// <summary>The member name.</summary>
    public string Name { get; }

    /// <summary>The member value.</summary>
    public JsonValue Value { get; }

    /// <inheritdoc />
    public bool Equals(JsonMember? other) => other is not null && Name == other.Name && Value.Equals(other.Value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as JsonMember);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name, Value);

    /// <inheritdoc />
    public override string ToString() => $"{Name}: {Value}";
}

/// <summary>A JSON value in the canonical wire model.</summary>
public sealed class JsonValue : IEquatable<JsonValue>
{
    private readonly JVal _core;

    private JsonValue(JVal core) => _core = core;

    /// <summary>A JSON string.</summary>
    public static JsonValue Str(string value) => new(JVal.NewJStr(Interop.NotNull(value, nameof(value))));

    /// <summary>A JSON integer.</summary>
    public static JsonValue Int(int value) => new(JVal.NewJInt(value));

    /// <summary>A JSON boolean.</summary>
    public static JsonValue Bool(bool value) => new(JVal.NewJBool(value));

    /// <summary>
    /// A JSON float. JSON has one number type, so a whole-valued float renders as an integer and
    /// reads back as one — match integers and floats together when reading parsed wire.
    /// </summary>
    public static JsonValue Float(double value) => new(JVal.NewJFloat(value));

    /// <summary>A JSON array.</summary>
    public static JsonValue Array(IEnumerable<JsonValue> items) =>
        new(JVal.NewJArr(Interop.List(Interop.Items(items, nameof(items)).Select(v => v._core))));

    /// <summary>A JSON array.</summary>
    public static JsonValue Array(params JsonValue[] items) => Array((IEnumerable<JsonValue>)items);

    /// <summary>A JSON object, in the member order given.</summary>
    public static JsonValue Object(IEnumerable<JsonMember> members) =>
        new(
            JVal.NewJObj(
                Interop.List(Interop.Items(members, nameof(members)).Select(m => Tuple.Create(m.Name, m.Value._core)))
            )
        );

    /// <summary>A JSON object, in the member order given.</summary>
    public static JsonValue Object(params JsonMember[] members) => Object((IEnumerable<JsonMember>)members);

    /// <summary>Read this value by case. Total: exactly one branch runs.</summary>
    public T Match<T>(
        Func<string, T> onStr,
        Func<int, T> onInt,
        Func<bool, T> onBool,
        Func<double, T> onFloat,
        Func<IReadOnlyList<JsonValue>, T> onArray,
        Func<IReadOnlyList<JsonMember>, T> onObject
    ) =>
        _core.Tag switch
        {
            JVal.Tags.JStr => onStr(((JVal.JStr)_core).Item),
            JVal.Tags.JInt => onInt(((JVal.JInt)_core).Item),
            JVal.Tags.JBool => onBool(((JVal.JBool)_core).Item),
            JVal.Tags.JFloat => onFloat(((JVal.JFloat)_core).Item),
            JVal.Tags.JArr => onArray(Interop.Read(((JVal.JArr)_core).Item).Select(v => new JsonValue(v)).ToArray()),
            JVal.Tags.JObj => onObject(
                Interop
                    .Read(((JVal.JObj)_core).Item)
                    .Select(t => new JsonMember(t.Item1, new JsonValue(t.Item2)))
                    .ToArray()
            ),
            _ => throw Interop.UnknownCase(nameof(JVal), _core.Tag),
        };

    /// <summary>Read this value by case for effect. Total: exactly one branch runs.</summary>
    public void Switch(
        Action<string> onStr,
        Action<int> onInt,
        Action<bool> onBool,
        Action<double> onFloat,
        Action<IReadOnlyList<JsonValue>> onArray,
        Action<IReadOnlyList<JsonMember>> onObject
    ) =>
        Match(
            v =>
            {
                onStr(v);
                return true;
            },
            v =>
            {
                onInt(v);
                return true;
            },
            v =>
            {
                onBool(v);
                return true;
            },
            v =>
            {
                onFloat(v);
                return true;
            },
            xs =>
            {
                onArray(xs);
                return true;
            },
            ms =>
            {
                onObject(ms);
                return true;
            }
        );

    /// <summary>
    /// Render to canonical wire JSON. Returns false and names the refusal rather than throwing —
    /// a non-finite float has no wire spelling here.
    /// </summary>
    public bool TryRender(out string? json, out string? error)
    {
        var result = Json.tryRender(_core);

        if (result.IsOk)
        {
            json = result.ResultValue;
            error = null;
            return true;
        }

        json = null;
        error = result.ErrorValue;
        return false;
    }

    /// <summary>Parse canonical wire JSON. Returns false and names the failure rather than throwing.</summary>
    public static bool TryParse(string json, out JsonValue? value, out string? error)
    {
        var result = Json.parse(Interop.NotNull(json, nameof(json)));

        if (result.IsOk)
        {
            value = new JsonValue(result.ResultValue);
            error = null;
            return true;
        }

        value = null;
        error = result.ErrorValue;
        return false;
    }

    /// <summary>The bridge OUT: the wrapped F# value.</summary>
    public JVal ToCore() => _core;

    /// <summary>The bridge IN: wrap an F# value.</summary>
    public static JsonValue FromCore(JVal core) => new(Interop.NotNull(core, nameof(core)));

    /// <inheritdoc />
    public bool Equals(JsonValue? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as JsonValue);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _core.ToString();
}
