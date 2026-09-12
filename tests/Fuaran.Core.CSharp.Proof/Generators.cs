// ============================================================================
//  The sample. Values are drawn off `ConfRng` — the seeded, Fable-identical draw
//  stream every Core conformance family already generates from — rather than off a
//  generator invented here, so a recorded seed reproduces a counterexample the same
//  way it does everywhere else in this repo.
//
//  Two deliberate choices about the DISTRIBUTION, both of which exist to keep the
//  round-trip law from certifying a sample that never reached most of the algebra:
//
//   * the TOP-LEVEL case is forced, round-robin, rather than drawn — so a run of N
//     iterations reaches every case of the union it generates by construction; and
//   * the nullary vocabularies (`BinOp`, `ScalarFn`, `AggFn`, `JoinKind`,
//     `SortDir`, `ColumnType`, `WindowFn`) rotate rather than draw, for the same
//     reason.
//
//  Neither makes the law weaker: the law is an identity over whatever is drawn.
//  What they buy is that `Coverage` — the vacuity guard beside it — reports a
//  missing case as a FACADE gap rather than as an unlucky sample.
// ============================================================================

namespace Fuaran.Core.CSharp.Proof;

internal sealed class Gen
{
    private static readonly BinaryOperator[] BinOps = Enum.GetValues<BinaryOperator>();
    private static readonly ScalarFunction[] ScalarFns = Enum.GetValues<ScalarFunction>();
    private static readonly AggregateFunction[] AggFns = Enum.GetValues<AggregateFunction>();
    private static readonly JoinMode[] JoinModes = Enum.GetValues<JoinMode>();
    private static readonly SortOrder[] SortOrders = Enum.GetValues<SortOrder>();
    private static readonly ColumnKind[] ColumnKinds = Enum.GetValues<ColumnKind>();
    private static readonly WindowFunctionKind[] WindowKinds = Enum.GetValues<WindowFunctionKind>();

    internal const int ExprCases = 12;
    internal const int StepCases = 14;
    internal const int JsonCases = 6;
    internal const int SpaceCases = 5;
    internal const int ShapeCases = 4;

    private ConfRng.T _rng;
    private int _binOp;
    private int _scalarFn;
    private int _aggFn;
    private int _joinMode;
    private int _sortOrder;
    private int _columnKind;
    private int _windowKind;
    private int _cellCase;
    private int _names;

    internal Gen(int seed) => _rng = ConfRng.ofSeed(seed);

    private int Below(int n)
    {
        var drawn = ConfRng.intBelow(n, _rng);
        _rng = drawn.Item2;
        return drawn.Item1;
    }

    private string Name() => "c" + (_names++ % 7);

    // ---- leaves ----

    internal CellValue Cell()
    {
        switch (_cellCase++ % 7)
        {
            case 0:
                return CellValue.Int(Below(100) - 50);
            case 1:
                return CellValue.Float(Below(1000) / 8.0);
            case 2:
                return CellValue.Bool(Below(2) == 0);
            case 3:
                return CellValue.Str("s" + Below(20));
            case 4:
                return CellValue.Date($"2026-09-{(Below(27) + 1):00}");
            case 5:
                return CellValue.Timestamp($"2026-09-{(Below(27) + 1):00}T00:00:00Z");
            default:
                return CellValue.Null;
        }
    }

    internal ColumnValue Column()
    {
        var kind = ColumnKinds[_columnKind++ % ColumnKinds.Length];
        var cells = Enumerable.Range(0, 1 + Below(3)).Select(_ => Cell()).ToArray();
        return ColumnValue.Of(Name(), kind, cells);
    }

    internal TableValue Table() =>
        TableValue.Of(Enumerable.Range(0, 1 + Below(2)).Select(_ => Column()).ToArray());

    internal SourceValue Source() => Below(2) == 0 ? SourceValue.Embedded(Table()) : SourceValue.Reference(Name());

    internal WindowFunction WindowFn()
    {
        var kind = WindowKinds[_windowKind++ % WindowKinds.Length];

        return kind switch
        {
            WindowFunctionKind.RowNumber => WindowFunction.RowNumber,
            WindowFunctionKind.Rank => WindowFunction.Rank,
            WindowFunctionKind.Lag => WindowFunction.Lag,
            WindowFunctionKind.Lead => WindowFunction.Lead,
            WindowFunctionKind.CumulSum => WindowFunction.CumulSum,
            WindowFunctionKind.RollingMean => WindowFunction.RollingMean,
            WindowFunctionKind.DenseRank => WindowFunction.DenseRank,
            WindowFunctionKind.CompetitionRank => WindowFunction.CompetitionRank,
            WindowFunctionKind.NTile => WindowFunction.NTile(1 + Below(8)),
            WindowFunctionKind.CumulMax => WindowFunction.CumulMax,
            WindowFunctionKind.CumulMin => WindowFunction.CumulMin,
            _ => WindowFunction.RollingSum,
        };
    }

    // ---- the expression algebra ----

    internal Expr Expression(int forcedCase, int depth)
    {
        var c = depth <= 0 ? forcedCase % 3 : forcedCase % ExprCases;

        switch (c)
        {
            case 0:
                return Expr.Col(Name());
            case 1:
                return Expr.Literal(Cell());
            case 2:
                return Expr.Param("p" + Below(4));
            case 3:
                return Expr.Binary(
                    BinOps[_binOp++ % BinOps.Length],
                    Expression(Below(ExprCases), depth - 1),
                    Expression(Below(ExprCases), depth - 1)
                );
            case 4:
                return Expr.Not(Expression(Below(ExprCases), depth - 1));
            case 5:
                return Expr.Coalesce(
                    Enumerable.Range(0, 1 + Below(3)).Select(_ => Expression(Below(ExprCases), depth - 1)).ToArray()
                );
            case 6:
                return Expr.Case(
                    Enumerable
                        .Range(0, 1 + Below(2))
                        .Select(_ => new CaseArm(
                            Expression(Below(ExprCases), depth - 1),
                            Expression(Below(ExprCases), depth - 1)
                        ))
                        .ToArray(),
                    Expression(Below(ExprCases), depth - 1)
                );
            case 7:
                return Expr.Cast(ColumnKinds[_columnKind++ % ColumnKinds.Length], Expression(Below(ExprCases), depth - 1));
            case 8:
                return Expr.Apply(
                    ScalarFns[_scalarFn++ % ScalarFns.Length],
                    Enumerable.Range(0, 1 + Below(2)).Select(_ => Expression(Below(ExprCases), depth - 1)).ToArray()
                );
            case 9:
                return Expr.InList(
                    Expression(Below(ExprCases), depth - 1),
                    Enumerable.Range(0, 1 + Below(2)).Select(_ => Expression(Below(ExprCases), depth - 1)).ToArray()
                );
            case 10:
                return Expr.IsNull(Expression(Below(ExprCases), depth - 1));
            default:
                return Expr.InParam(Expression(Below(ExprCases), depth - 1), "lp" + Below(3));
        }
    }

    // ---- the verb set ----

    internal Step TransformStep(int forcedCase)
    {
        switch (forcedCase % StepCases)
        {
            case 0:
                return Step.Filter(Expression(Below(ExprCases), 2));
            case 1:
                return Step.Project(
                    Enumerable.Range(0, 1 + Below(2)).Select(_ => new ColumnRename(Name(), Name())).ToArray()
                );
            case 2:
                return Step.Derive(Name(), Expression(Below(ExprCases), 2));
            case 3:
                return Step.GroupBy(
                    Enumerable.Range(0, 1 + Below(2)).Select(_ => Name()).ToArray(),
                    Enumerable
                        .Range(0, 1 + Below(2))
                        .Select(_ => AggregateSpec.Of(Name(), AggFns[_aggFn++ % AggFns.Length], Name()))
                        .ToArray()
                );
            case 4:
                return Step.Join(
                    Source(),
                    Enumerable.Range(0, 1 + Below(2)).Select(_ => new JoinKey(Name(), Name())).ToArray(),
                    JoinModes[_joinMode++ % JoinModes.Length]
                );
            case 5:
                return Step.Window(
                    WindowStepSpec.Of(
                        Enumerable.Range(0, Below(2)).Select(_ => Name()).ToArray(),
                        Enumerable
                            .Range(0, 1 + Below(2))
                            .Select(_ => new SortKey(Name(), SortOrders[_sortOrder++ % SortOrders.Length]))
                            .ToArray(),
                        WindowFn(),
                        Name(),
                        Name()
                    )
                );
            case 6:
                return Step.Pivot(
                    PivotStepSpec.Of(
                        Enumerable.Range(0, 1 + Below(2)).Select(_ => Name()).ToArray(),
                        Name(),
                        Name(),
                        AggFns[_aggFn++ % AggFns.Length]
                    )
                );
            case 7:
                return Step.Unpivot(
                    Enumerable.Range(0, 1 + Below(2)).Select(_ => Name()).ToArray(),
                    Enumerable.Range(0, 1 + Below(2)).Select(_ => Name()).ToArray()
                );
            case 8:
                return Step.Sort(
                    Enumerable
                        .Range(0, 1 + Below(2))
                        .Select(_ => new SortKey(Name(), SortOrders[_sortOrder++ % SortOrders.Length]))
                        .ToArray()
                );
            case 9:
                return Step.Distinct;
            case 10:
                return Step.Limit(Below(50), Below(10));
            case 11:
                return Step.Union(Source());
            case 12:
                return Step.Intersect(Source());
            default:
                return Step.Except(Source());
        }
    }

    internal Pipeline PipelineOf(int forcedCase) =>
        Pipeline.Of(
            Enumerable.Range(0, 1 + Below(2)).Select(i => TransformStep(forcedCase + i)).ToArray()
        );

    // ---- the wire JSON model ----

    internal JsonValue Json(int forcedCase, int depth)
    {
        var c = depth <= 0 ? forcedCase % 4 : forcedCase % JsonCases;

        return c switch
        {
            0 => JsonValue.Str("j" + Below(20)),
            1 => JsonValue.Int(Below(200) - 100),
            2 => JsonValue.Bool(Below(2) == 0),
            3 => JsonValue.Float(Below(1000) / 16.0),
            4 => JsonValue.Array(
                Enumerable.Range(0, 1 + Below(3)).Select(_ => Json(Below(JsonCases), depth - 1)).ToArray()
            ),
            _ => JsonValue.Object(
                Enumerable
                    .Range(0, 1 + Below(3))
                    .Select(_ => new JsonMember(Name(), Json(Below(JsonCases), depth - 1)))
                    .ToArray()
            ),
        };
    }

    // ---- the artifact-function declaration family ----

    internal EffectSignature Effect() =>
        EffectSignature.Of(
            (HostEffectKind)Below(Enum.GetValues<HostEffectKind>().Length),
            (DeterminismKind)Below(Enum.GetValues<DeterminismKind>().Length)
        );

    internal HoleSpace Space(int forcedCase) =>
        (forcedCase % SpaceCases) switch
        {
            0 => HoleSpace.IntRange(Below(5), 5 + Below(20)),
            1 => HoleSpace.FloatRange(Below(5) / 2.0, 5.0 + Below(20)),
            2 => HoleSpace.StringLen(Below(3), 3 + Below(30)),
            3 => HoleSpace.Enumeration(Enumerable.Range(0, 1 + Below(3)).Select(_ => Name()).ToArray()),
            _ => HoleSpace.AnyString,
        };

    internal HoleShape Shape(int forcedCase) =>
        (forcedCase % ShapeCases) switch
        {
            0 => HoleShape.Value(Space(Below(SpaceCases))),
            1 => HoleShape.Slot(Below(2) == 0 ? null : "k" + Below(4)),
            2 => HoleShape.Repeat(Space(Below(SpaceCases - 1))),
            _ => HoleShape.Action(Effect()),
        };

    internal HoleSpec Hole(int forcedCase) => HoleSpec.Of("/a/" + Below(9), Name(), Shape(forcedCase));

    internal SignatureHoleView SignatureHole(int forcedCase) =>
        SignatureHoleView.Of(
            "/a/" + Below(9),
            Name(),
            "kind" + Below(4),
            forcedCase % 2 == 0 ? Space(Below(SpaceCases)) : null,
            forcedCase % 3 == 0 ? "slot" + Below(3) : null,
            forcedCase % 5 == 0 ? Effect() : null,
            Below(2) == 0
        );

    internal SignatureView Signature(int forcedCase) =>
        SignatureView.Of(
            Name(),
            Enumerable.Range(0, 1 + Below(3)).Select(i => SignatureHole(forcedCase + i)).ToArray(),
            Effect()
        );
}
