// ============================================================================
//  The round-trip law: facade -> F# value -> facade is the identity.
//
//  Stated precisely, because the trivial reading of that sentence is vacuous. The
//  facade WRAPS the F# value, so `FromCore(x.ToCore()).ToCore()` is identity by
//  construction and proves nothing. What is asserted here is the useful form:
//
//      ToCore( Rebuild( FromCore( x.ToCore() ) ) )  =  x.ToCore()
//
//  where `Rebuild` reads every case through `Match` and re-constructs it through
//  the public factories, touching no F# type. So the law says the READERS and the
//  FACTORIES are mutually inverse over the whole algebra — a case whose reader
//  dropped a payload, or whose factory reassembled it in the wrong order, fails
//  here rather than in a downstream veneer months later.
//
//  Its falsifier is concrete and was exercised: swapping the two operands in the
//  `Binary` rebuild, or dropping the else-branch of `Case`, reddens it. Since
//  Phase 257 those two legs are the dataframe facade's, in
//  tests/Fuaran.Core.DataFrame.CSharp.Proof; this file keeps the column layer, the
//  wire JSON model and the hole family.
// ============================================================================

namespace Fuaran.Core.CSharp.Proof;

internal static class RoundTrip
{
    internal const int Iterations = 600;

    internal static void Run(Check check, Coverage coverage)
    {
        var gen = new Gen(seed: 20260912);

        for (var i = 0; i < Iterations; i++)
        {
            // Phase 257 — the column layer, which the dataframe legs reached on this facade's behalf
            // until they moved to their own proof: a cell, a source (an embedded table reaches every
            // column kind), and an aggregate function through the declared `Vocabulary` bridge.
            var cell = gen.Cell();
            var cellCore = cell.ToCore();
            coverage.Visit(cellCore);
            check.That(
                Rebuild.Cell(CellValue.FromCore(cellCore)).ToCore().Equals(cellCore),
                $"cell round trip failed at iteration {i}: `{cellCore}`"
            );

            var source = gen.Source();
            var sourceCore = source.ToCore();
            coverage.Visit(sourceCore);
            check.That(
                Rebuild.Source(SourceValue.FromCore(sourceCore)).ToCore().Equals(sourceCore),
                $"source round trip failed at iteration {i}: `{sourceCore}`"
            );

            var aggregate = gen.Aggregate();
            var aggregateCore = Vocabulary.ToCore(aggregate);
            coverage.Visit(aggregateCore);
            check.That(
                Vocabulary.ToCore(Vocabulary.FromCore(aggregateCore)).Equals(aggregateCore),
                $"aggregate function round trip failed at iteration {i}: `{aggregateCore}`"
            );

            var json = gen.Json(i, depth: 3);
            var jsonCore = json.ToCore();
            coverage.Visit(jsonCore);
            check.That(
                Rebuild.Json(JsonValue.FromCore(jsonCore)).ToCore().Equals(jsonCore),
                $"json round trip failed at iteration {i}: `{jsonCore}`"
            );

            var hole = gen.Hole(i);
            var holeCore = hole.ToCore();
            coverage.Visit(holeCore);
            check.That(
                Rebuild.Hole(HoleSpec.FromCore(holeCore)).ToCore().Equals(holeCore),
                $"hole declaration round trip failed at iteration {i}: `{holeCore}`"
            );

            var signature = gen.Signature(i);
            var signatureCore = signature.ToCore();
            coverage.Visit(signatureCore);
            check.That(
                Rebuild.Signature(SignatureView.FromCore(signatureCore)).ToCore().Equals(signatureCore),
                $"signature round trip failed at iteration {i}: `{signatureCore}`"
            );

            var space = gen.Space(i);
            var spaceCore = space.ToCore();
            coverage.Visit(spaceCore);
            check.That(
                Rebuild.Space(HoleSpace.FromCore(spaceCore)).ToCore().Equals(spaceCore),
                $"value space round trip failed at iteration {i}: `{spaceCore}`"
            );

            var shape = gen.Shape(i);
            var shapeCore = shape.ToCore();
            coverage.Visit(shapeCore);
            check.That(
                Rebuild.Shape(HoleShape.FromCore(shapeCore)).ToCore().Equals(shapeCore),
                $"hole kind round trip failed at iteration {i}: `{shapeCore}`"
            );
        }
    }

    /// <summary>The unions the sample must reach EVERY case of for the law above to mean anything.</summary>
    internal static IReadOnlyList<Type> CoveredUnions { get; } =
        new[]
        {
            typeof(Cell),
            typeof(ColumnType),
            typeof(DataSource),
            typeof(JVal),
            typeof(ValueSpace),
            typeof(HoleKind),
            typeof(HostEffect),
            typeof(DeterminismSource),
            typeof(AggFn),
        };
}
