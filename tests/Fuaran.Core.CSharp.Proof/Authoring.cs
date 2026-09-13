// ============================================================================
//  The acceptance criterion, written out by hand: a C# consumer constructs and
//  reads `ColExpr`, `Transform`, the artifact-function hole family and `JVal`
//  without touching an F# option, list, tuple or union case.
//
//  Deliberately hand-written rather than generated, and deliberately reading like
//  an authoring veneer's own code, because the generated law next door proves
//  FIDELITY and this proves USABILITY — that the surface a veneer would actually
//  write against exists and composes.
//
//  There is no `using Microsoft.FSharp.*` in this file and no cast to a Core type.
//  The pipeline built here also goes out through Core's own canonical codec and
//  comes back, so the claim is not merely that the facade builds something, but
//  that what it builds is the wire the rest of the estate reads.
// ============================================================================

namespace Fuaran.Core.CSharp.Proof;

internal static class Authoring
{
    internal static void Run(Check check)
    {
        // ---- a pipeline a reporting veneer would actually author ----

        var pipeline = Pipeline.Of(
            Step.Filter(Expr.Binary(BinaryOperator.Gt, Expr.Col("amount"), Expr.Param("threshold"))),
            Step.Derive(
                "band",
                Expr.Case(
                    new[]
                    {
                        new CaseArm(
                            Expr.Binary(BinaryOperator.Lt, Expr.Col("amount"), Expr.Literal(CellValue.Int(100))),
                            Expr.Literal(CellValue.Str("small"))
                        ),
                    },
                    Expr.Literal(CellValue.Str("large"))
                )
            ),
            Step.GroupBy(
                new[] { "region" },
                new[] { AggregateSpec.Of("total", AggregateFunction.Sum, "amount") }
            ),
            Step.Sort(new SortKey("total", SortOrder.Desc)),
            Step.Limit(10, 0)
        );

        check.That(pipeline.Steps.Count == 5, "the authored pipeline should have five steps");

        // ---- read it back through Match, with no knowledge of the F# shape ----

        var filterColumn = pipeline.Steps[0]
            .Match(
                onFilter: e =>
                    e.Match(
                        onCol: _ => "?",
                        onLiteral: _ => "?",
                        onParam: _ => "?",
                        onBinary: (op, left, _) =>
                            op == BinaryOperator.Gt ? left.Match(
                                onCol: n => n,
                                onLiteral: _ => "?",
                                onParam: _ => "?",
                                onBinary: (_, _, _) => "?",
                                onNot: _ => "?",
                                onCoalesce: _ => "?",
                                onCase: (_, _) => "?",
                                onCast: (_, _) => "?",
                                onApply: (_, _) => "?",
                                onInList: (_, _) => "?",
                                onIsNull: _ => "?",
                                onInParam: (_, _) => "?",
                                onNow: _ => "?"
                            ) : "?",
                        onNot: _ => "?",
                        onCoalesce: _ => "?",
                        onCase: (_, _) => "?",
                        onCast: (_, _) => "?",
                        onApply: (_, _) => "?",
                        onInList: (_, _) => "?",
                        onIsNull: _ => "?",
                        onInParam: (_, _) => "?",
                        onNow: _ => "?"
                    ),
                onProject: _ => "?",
                onDerive: (_, _) => "?",
                onGroupBy: (_, _) => "?",
                onJoin: (_, _, _) => "?",
                onWindow: _ => "?",
                onPivot: _ => "?",
                onUnpivot: (_, _) => "?",
                onSort: _ => "?",
                onDistinct: () => "?",
                onLimit: (_, _) => "?",
                onUnion: _ => "?",
                onIntersect: _ => "?",
                onExcept: _ => "?"
            );

        check.That(filterColumn == "amount", $"the filter's left operand should read back as `amount`, got `{filterColumn}`");

        var limit = pipeline.Steps[4]
            .Match(
                onFilter: _ => -1,
                onProject: _ => -1,
                onDerive: (_, _) => -1,
                onGroupBy: (_, _) => -1,
                onJoin: (_, _, _) => -1,
                onWindow: _ => -1,
                onPivot: _ => -1,
                onUnpivot: (_, _) => -1,
                onSort: _ => -1,
                onDistinct: () => -1,
                onLimit: (n, _) => n,
                onUnion: _ => -1,
                onIntersect: _ => -1,
                onExcept: _ => -1
            );

        check.That(limit == 10, $"the limit step should read back as 10, got {limit}");

        // ---- and out through Core's own codec, and back ----

        var json = pipeline.Encode();
        check.That(json.Length > 0, "encoding the authored pipeline produced nothing");

        if (Pipeline.TryDecode(json, out var decoded, out var decodeError))
        {
            check.That(
                decoded!.Steps.Count == pipeline.Steps.Count
                    && decoded.Steps.Zip(pipeline.Steps).All(p => p.First.Equals(p.Second)),
                "the authored pipeline did not survive its own canonical wire round trip"
            );
        }
        else
        {
            check.Fail($"decoding the authored pipeline failed: {decodeError}");
        }

        check.That(
            !Pipeline.TryDecode("{ not json", out _, out var badError) && badError is not null,
            "a malformed pipeline document should be refused BY NAME, not thrown or accepted"
        );

        // ---- an embedded source, built column by column ----

        var lookup = SourceValue.Embedded(
            TableValue.Of(
                ColumnValue.Of("region", ColumnKind.String, new[] { CellValue.Str("north"), CellValue.Str("south") }),
                ColumnValue.Of("quota", ColumnKind.Int, new[] { CellValue.Int(10), CellValue.Null })
            )
        );

        var join = Step.Join(lookup, new[] { new JoinKey("region", "region") }, JoinMode.Left);

        var joinedSchema = join.Match(
            onFilter: _ => System.Array.Empty<string>(),
            onProject: _ => System.Array.Empty<string>(),
            onDerive: (_, _) => System.Array.Empty<string>(),
            onGroupBy: (_, _) => System.Array.Empty<string>(),
            onJoin: (src, _, mode) =>
                mode != JoinMode.Left
                    ? System.Array.Empty<string>()
                    : src.Match(
                        onEmbedded: t => t.Schema.Select(e => $"{e.Name}:{e.Kind}").ToArray(),
                        onReference: _ => System.Array.Empty<string>()
                    ),
            onWindow: _ => System.Array.Empty<string>(),
            onPivot: _ => System.Array.Empty<string>(),
            onUnpivot: (_, _) => System.Array.Empty<string>(),
            onSort: _ => System.Array.Empty<string>(),
            onDistinct: () => System.Array.Empty<string>(),
            onLimit: (_, _) => System.Array.Empty<string>(),
            onUnion: _ => System.Array.Empty<string>(),
            onIntersect: _ => System.Array.Empty<string>(),
            onExcept: _ => System.Array.Empty<string>()
        );

        check.That(
            joinedSchema.Length == 2 && joinedSchema[0] == "region:String" && joinedSchema[1] == "quota:Int",
            "the embedded join source should read back its derived schema, got [" + string.Join(", ", joinedSchema) + "]"
        );

        // ---- the wire JSON model ----

        var document = JsonValue.Object(
            new JsonMember("name", JsonValue.Str("veneer")),
            new JsonMember("holes", JsonValue.Array(JsonValue.Int(1), JsonValue.Int(2))),
            new JsonMember("strict", JsonValue.Bool(true))
        );

        if (document.TryRender(out var rendered, out var renderError))
        {
            check.That(
                Fuaran.Core.CSharp.JsonValue.TryParse(rendered!, out var reparsed, out _) && reparsed!.Equals(document),
                "the authored JSON document did not survive render + parse"
            );
        }
        else
        {
            check.Fail($"rendering the authored JSON document failed: {renderError}");
        }

        var memberNames = document.Match(
            onStr: _ => System.Array.Empty<string>(),
            onInt: _ => System.Array.Empty<string>(),
            onBool: _ => System.Array.Empty<string>(),
            onFloat: _ => System.Array.Empty<string>(),
            onArray: _ => System.Array.Empty<string>(),
            onObject: ms => ms.Select(m => m.Name).ToArray()
        );

        check.That(
            memberNames.SequenceEqual(new[] { "name", "holes", "strict" }),
            "the authored JSON object should read back its members in order"
        );

        // ---- the artifact-function declaration family ----

        var ceiling = EffectSignature.Of(HostEffectKind.ReadsHost, DeterminismKind.Clock);

        check.That(
            ceiling.Covers(EffectSignature.PureDeterministic),
            "a ReadsHost/Clock ceiling should cover a pure deterministic handler"
        );

        check.That(
            !EffectSignature.PureDeterministic.Covers(ceiling),
            "a pure deterministic ceiling should NOT cover a ReadsHost/Clock handler"
        );

        check.That(
            ceiling.Join(EffectSignature.Of(HostEffectKind.WritesHost, DeterminismKind.Deterministic))
                .Equals(EffectSignature.Of(HostEffectKind.WritesHost, DeterminismKind.Clock)),
            "the effect join should widen componentwise"
        );

        var holes = new[]
        {
            HoleSpec.Of("/root/0", "title", HoleShape.Value(HoleSpace.StringLen(1, 60))),
            HoleSpec.Of("/root/1", "rows", HoleShape.Repeat(HoleSpace.IntRange(0, 50))),
            HoleSpec.Of("/root/2", "body", HoleShape.Slot(null)),
            HoleSpec.Of("/root/3", "submit", HoleShape.Action(ceiling)),
        };

        var shapes = holes.Select(h =>
                h.Shape.Match(
                    onValue: _ => "value",
                    onSlot: c => c is null ? "slot(any)" : $"slot({c})",
                    onRepeat: _ => "repeat",
                    onAction: e => $"action({e.Host})"
                )
            )
            .ToArray();

        check.That(
            shapes.SequenceEqual(new[] { "value", "repeat", "slot(any)", "action(ReadsHost)" }),
            "the declared holes should read back their flavours, got [" + string.Join(", ", shapes) + "]"
        );

        check.That(
            holes[0].Shape.Match(
                onValue: s => s.Validate("a title"),
                onSlot: _ => false,
                onRepeat: _ => false,
                onAction: _ => false
            ),
            "a 1..60 string-length space should admit a short title"
        );

        var signature = SignatureView.Of(
            "report",
            holes.Select(h =>
                SignatureHoleView.Of(
                    h.Address,
                    h.Name,
                    h.Shape.Match(
                        onValue: _ => "value",
                        onSlot: _ => "slot",
                        onRepeat: _ => "repeat",
                        onAction: _ => "action"
                    ),
                    h.Shape.Match<HoleSpace?>(
                        onValue: s => s,
                        onSlot: _ => null,
                        onRepeat: s => s,
                        onAction: _ => null
                    ),
                    h.Shape.Match<string?>(onValue: _ => null, onSlot: c => c, onRepeat: _ => null, onAction: _ => null),
                    h.Shape.Match<EffectSignature?>(
                        onValue: _ => null,
                        onSlot: _ => null,
                        onRepeat: _ => null,
                        onAction: e => e
                    ),
                    required: true
                )
            ),
            EffectSignature.PureDeterministic
        );

        check.That(signature.Holes.Count == 4, "the derived signature should carry four entries");
        check.That(
            signature.Holes[3].Action is not null && signature.Holes[3].Action!.Host == HostEffectKind.ReadsHost,
            "the action entry should carry its declared ceiling"
        );
        check.That(signature.Holes[2].Space is null, "a slot entry carries no value space");
    }
}
