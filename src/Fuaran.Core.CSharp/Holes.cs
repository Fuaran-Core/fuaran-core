// ============================================================================
//  Fuaran.Core.CSharp — the artifact-function declaration family (Phase 128), a
//  facade over `Fuaran.Core.Function`'s `ValueSpace` / `HoleKind` / `HoleDecl` /
//  `EffectClass` / `SigEntry` / `Signature`.
//
//  A note on scope, because the phase's own wording points at something that is
//  not there: `Fuaran.Core.Function` publishes no union named `Function` — it
//  publishes a MODULE of that name over this family of declaration types. This is
//  that family: the part of the strand a non-F# veneer actually has to construct
//  (declare a typed hole) and read (a derived signature). `Arg<'Node>` is
//  deliberately absent — it is generic over the DOMAIN's node type, which no Core
//  package names, so a facade over it belongs to whichever host supplies that type.
//
//  The three `option` fields of a signature entry read as nullable references here;
//  `null` is `None` and nothing else, which is why `Space` / `Slot` / `Action` are
//  the only nullable readers in this package.
// ============================================================================

namespace Fuaran.Core.CSharp;

/// <summary>The mandatory two-axis effect signature — never optional, never partial.</summary>
public sealed class EffectSignature : IEquatable<EffectSignature>
{
    private readonly EffectClass _core;

    private EffectSignature(EffectClass core) => _core = core;

    /// <summary>Create an effect signature from its two axes.</summary>
    public static EffectSignature Of(HostEffectKind host, DeterminismKind determinism) =>
        new(new EffectClass(Vocab.ToCore(host), Vocab.ToCore(determinism)));

    /// <summary>The bottom of the lattice: pure and deterministic.</summary>
    public static EffectSignature PureDeterministic { get; } = new(Effect.pureDeterministic);

    /// <summary>Does the artifact touch the world.</summary>
    public HostEffectKind Host => Vocab.HostEffectKindOf(_core.Host);

    /// <summary>Does the artifact read a non-deterministic source.</summary>
    public DeterminismKind Determinism => Vocab.DeterminismKindOf(_core.Determinism);

    /// <summary>The componentwise-widest join — the composition rule.</summary>
    public EffectSignature Join(EffectSignature other) =>
        new(Effect.join(_core, Interop.NotNull(other, nameof(other))._core));

    /// <summary>Is this declared signature at least as wide as <paramref name="actual" /> on both axes.</summary>
    public bool Covers(EffectSignature actual) => Effect.covers(_core, Interop.NotNull(actual, nameof(actual))._core);

    /// <summary>The canonical wire label for this signature's determinism axis.</summary>
    public string DeterminismTag => Effect.determinismTag(_core.Determinism);

    /// <summary>The bridge OUT: the wrapped F# value.</summary>
    public EffectClass ToCore() => _core;

    /// <summary>The bridge IN: wrap an F# value.</summary>
    public static EffectSignature FromCore(EffectClass core) => new(Interop.NotNull(core, nameof(core)));

    /// <inheritdoc />
    public bool Equals(EffectSignature? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as EffectSignature);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _core.ToString();
}

/// <summary>The value domain a hole ranges over.</summary>
public sealed class HoleSpace : IEquatable<HoleSpace>
{
    private readonly ValueSpace _core;

    private HoleSpace(ValueSpace core) => _core = core;

    /// <summary>An inclusive integer range.</summary>
    public static HoleSpace IntRange(int low, int high) => new(ValueSpace.NewIntRange(low, high));

    /// <summary>An inclusive float range.</summary>
    public static HoleSpace FloatRange(double low, double high) => new(ValueSpace.NewFloatRange(low, high));

    /// <summary>An inclusive string-length range.</summary>
    public static HoleSpace StringLen(int low, int high) => new(ValueSpace.NewStringLen(low, high));

    /// <summary>A fixed set of admissible strings.</summary>
    public static HoleSpace Enumeration(IEnumerable<string> members) =>
        new(ValueSpace.NewEnum(Interop.List(Interop.Items(members, nameof(members)))));

    /// <summary>A fixed set of admissible strings.</summary>
    public static HoleSpace Enumeration(params string[] members) => Enumeration((IEnumerable<string>)members);

    /// <summary>Any string — the only UNBOUNDED space, and therefore the one a repeat count may not use.</summary>
    public static HoleSpace AnyString { get; } = new(ValueSpace.AnyString);

    /// <summary>Read this space by case. Total: exactly one branch runs.</summary>
    public T Match<T>(
        Func<int, int, T> onIntRange,
        Func<double, double, T> onFloatRange,
        Func<int, int, T> onStringLen,
        Func<IReadOnlyList<string>, T> onEnumeration,
        Func<T> onAnyString
    )
    {
        switch (_core.Tag)
        {
            case ValueSpace.Tags.IntRange:
            {
                var c = (ValueSpace.IntRange)_core;
                return onIntRange(c.lo, c.hi);
            }
            case ValueSpace.Tags.FloatRange:
            {
                var c = (ValueSpace.FloatRange)_core;
                return onFloatRange(c.lo, c.hi);
            }
            case ValueSpace.Tags.StringLen:
            {
                var c = (ValueSpace.StringLen)_core;
                return onStringLen(c.lo, c.hi);
            }
            case ValueSpace.Tags.Enum:
                return onEnumeration(Interop.Read(((ValueSpace.Enum)_core).Item));
            case ValueSpace.Tags.AnyString:
                return onAnyString();
            default:
                throw Interop.UnknownCase(nameof(ValueSpace), _core.Tag);
        }
    }

    /// <summary>Read this space by case for effect. Total: exactly one branch runs.</summary>
    public void Switch(
        Action<int, int> onIntRange,
        Action<double, double> onFloatRange,
        Action<int, int> onStringLen,
        Action<IReadOnlyList<string>> onEnumeration,
        Action onAnyString
    ) =>
        Match(
            (lo, hi) =>
            {
                onIntRange(lo, hi);
                return true;
            },
            (lo, hi) =>
            {
                onFloatRange(lo, hi);
                return true;
            },
            (lo, hi) =>
            {
                onStringLen(lo, hi);
                return true;
            },
            ms =>
            {
                onEnumeration(ms);
                return true;
            },
            () =>
            {
                onAnyString();
                return true;
            }
        );

    /// <summary>Is a candidate value within this space.</summary>
    public bool Validate(string candidate) => Space.validate(_core, Interop.NotNull(candidate, nameof(candidate)));

    /// <summary>The bridge OUT: the wrapped F# value.</summary>
    public ValueSpace ToCore() => _core;

    /// <summary>The bridge IN: wrap an F# value.</summary>
    public static HoleSpace FromCore(ValueSpace core) => new(Interop.NotNull(core, nameof(core)));

    /// <inheritdoc />
    public bool Equals(HoleSpace? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as HoleSpace);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _core.ToString();
}

/// <summary>The hole flavours: three on the data axis, one on the behaviour axis.</summary>
public sealed class HoleShape : IEquatable<HoleShape>
{
    private readonly HoleKind _core;

    private HoleShape(HoleKind core) => _core = core;

    /// <summary>A typed value slot over a value space.</summary>
    public static HoleShape Value(HoleSpace space) =>
        new(HoleKind.NewValueHole(Interop.NotNull(space, nameof(space)).ToCore()));

    /// <summary>A tree-typed slot; pass null for an unconstrained one.</summary>
    public static HoleShape Slot(string? kindConstraint) => new(HoleKind.NewSlotHole(Interop.Some(kindConstraint)));

    /// <summary>A bounded repeat — the only iteration the total language permits.</summary>
    public static HoleShape Repeat(HoleSpace countSpace) =>
        new(HoleKind.NewRepeatHole(Interop.NotNull(countSpace, nameof(countSpace)).ToCore()));

    /// <summary>A dispatch slot declaring the effect CEILING of the handler that will fill it.</summary>
    public static HoleShape Action(EffectSignature ceiling) =>
        new(HoleKind.NewActionHole(Interop.NotNull(ceiling, nameof(ceiling)).ToCore()));

    /// <summary>Read this hole kind by case. Total: exactly one branch runs.</summary>
    public T Match<T>(
        Func<HoleSpace, T> onValue,
        Func<string?, T> onSlot,
        Func<HoleSpace, T> onRepeat,
        Func<EffectSignature, T> onAction
    ) =>
        _core.Tag switch
        {
            HoleKind.Tags.ValueHole => onValue(HoleSpace.FromCore(((HoleKind.ValueHole)_core).Item)),
            HoleKind.Tags.SlotHole => onSlot(Interop.Opt(((HoleKind.SlotHole)_core).kindConstraint)),
            HoleKind.Tags.RepeatHole => onRepeat(HoleSpace.FromCore(((HoleKind.RepeatHole)_core).countSpace)),
            HoleKind.Tags.ActionHole => onAction(EffectSignature.FromCore(((HoleKind.ActionHole)_core).effect)),
            _ => throw Interop.UnknownCase(nameof(HoleKind), _core.Tag),
        };

    /// <summary>Read this hole kind by case for effect. Total: exactly one branch runs.</summary>
    public void Switch(
        Action<HoleSpace> onValue,
        Action<string?> onSlot,
        Action<HoleSpace> onRepeat,
        Action<EffectSignature> onAction
    ) =>
        Match(
            s =>
            {
                onValue(s);
                return true;
            },
            c =>
            {
                onSlot(c);
                return true;
            },
            s =>
            {
                onRepeat(s);
                return true;
            },
            e =>
            {
                onAction(e);
                return true;
            }
        );

    /// <summary>The bridge OUT: the wrapped F# value.</summary>
    public HoleKind ToCore() => _core;

    /// <summary>The bridge IN: wrap an F# value.</summary>
    public static HoleShape FromCore(HoleKind core) => new(Interop.NotNull(core, nameof(core)));

    /// <inheritdoc />
    public bool Equals(HoleShape? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as HoleShape);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _core.ToString();
}

/// <summary>A declared hole, bound by its absolute lexical address — the hygiene surface.</summary>
public sealed class HoleSpec : IEquatable<HoleSpec>
{
    private readonly HoleDecl _core;

    private HoleSpec(HoleDecl core) => _core = core;

    /// <summary>Declare a hole.</summary>
    public static HoleSpec Of(string address, string name, HoleShape shape) =>
        new(
            new HoleDecl(
                Interop.NotNull(address, nameof(address)),
                Interop.NotNull(name, nameof(name)),
                Interop.NotNull(shape, nameof(shape)).ToCore()
            )
        );

    /// <summary>The absolute lexical address binding keys on.</summary>
    public string Address => _core.Addr;

    /// <summary>The hole's display name — never what binding keys on.</summary>
    public string Name => _core.Name;

    /// <summary>The hole's flavour.</summary>
    public HoleShape Shape => HoleShape.FromCore(_core.Kind);

    /// <summary>The bridge OUT: the wrapped F# value.</summary>
    public HoleDecl ToCore() => _core;

    /// <summary>The bridge IN: wrap an F# value.</summary>
    public static HoleSpec FromCore(HoleDecl core) => new(Interop.NotNull(core, nameof(core)));

    /// <inheritdoc />
    public bool Equals(HoleSpec? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as HoleSpec);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _core.ToString();
}

/// <summary>One entry of a derived signature — the introspectable projection of a hole.</summary>
public sealed class SignatureHoleView : IEquatable<SignatureHoleView>
{
    private readonly SigEntry _core;

    private SignatureHoleView(SigEntry core) => _core = core;

    /// <summary>Create a signature entry. <paramref name="space" />, <paramref name="slot" /> and
    /// <paramref name="action" /> are null where the wrapped model says <c>None</c>.</summary>
    public static SignatureHoleView Of(
        string address,
        string name,
        string kind,
        HoleSpace? space,
        string? slot,
        EffectSignature? action,
        bool required
    ) =>
        new(
            new SigEntry(
                Interop.NotNull(address, nameof(address)),
                Interop.NotNull(name, nameof(name)),
                Interop.NotNull(kind, nameof(kind)),
                Interop.Some(space?.ToCore()),
                Interop.Some(slot),
                Interop.Some(action?.ToCore()),
                required
            )
        );

    /// <summary>The hole's absolute lexical address.</summary>
    public string Address => _core.Addr;

    /// <summary>The hole's display name.</summary>
    public string Name => _core.Name;

    /// <summary>The hole flavour's tag.</summary>
    public string Kind => _core.Kind;

    /// <summary>A value hole's space; null for other flavours.</summary>
    public HoleSpace? Space => Interop.Opt(_core.Space) is { } s ? HoleSpace.FromCore(s) : null;

    /// <summary>A slot hole's kind constraint; null for other flavours and unconstrained slots.</summary>
    public string? Slot => Interop.Opt(_core.Slot);

    /// <summary>An action hole's declared effect ceiling; null for other flavours.</summary>
    public EffectSignature? Action => Interop.Opt(_core.Action) is { } e ? EffectSignature.FromCore(e) : null;

    /// <summary>Must the hole be bound before application.</summary>
    public bool Required => _core.Required;

    /// <summary>The bridge OUT: the wrapped F# value.</summary>
    public SigEntry ToCore() => _core;

    /// <summary>The bridge IN: wrap an F# value.</summary>
    public static SignatureHoleView FromCore(SigEntry core) => new(Interop.NotNull(core, nameof(core)));

    /// <inheritdoc />
    public bool Equals(SignatureHoleView? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SignatureHoleView);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _core.ToString();
}

/// <summary>An artifact's derived signature: which holes, what spaces, and its effect class.</summary>
public sealed class SignatureView : IEquatable<SignatureView>
{
    private readonly Signature _core;

    private SignatureView(Signature core) => _core = core;

    /// <summary>Create a signature.</summary>
    public static SignatureView Of(string name, IEnumerable<SignatureHoleView> holes, EffectSignature effect) =>
        new(
            new Signature(
                Interop.NotNull(name, nameof(name)),
                Interop.List(Interop.Items(holes, nameof(holes)).Select(h => h.ToCore())),
                Interop.NotNull(effect, nameof(effect)).ToCore()
            )
        );

    /// <summary>The artifact's name.</summary>
    public string Name => _core.Name;

    /// <summary>The declared holes, in order.</summary>
    public IReadOnlyList<SignatureHoleView> Holes =>
        Interop.Read(_core.Holes).Select(SignatureHoleView.FromCore).ToArray();

    /// <summary>The artifact's declared effect class.</summary>
    public EffectSignature Effect => EffectSignature.FromCore(_core.Effect);

    /// <summary>The bridge OUT: the wrapped F# value.</summary>
    public Signature ToCore() => _core;

    /// <summary>The bridge IN: wrap an F# value.</summary>
    public static SignatureView FromCore(Signature core) => new(Interop.NotNull(core, nameof(core)));

    /// <inheritdoc />
    public bool Equals(SignatureView? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SignatureView);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _core.ToString();
}
