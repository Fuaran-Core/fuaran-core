// ============================================================================
//  The acceptance criterion, written out by hand: a C# consumer constructs and
//  reads the column layer, the artifact-function hole family and `JVal` without
//  touching an F# option, list, tuple or union case. The dataframe half (`ColExpr`,
//  `Transform`) is proved the same way by tests/Fuaran.Core.DataFrame.CSharp.Proof
//  since Phase 257.
//
//  Deliberately hand-written rather than generated, and deliberately reading like
//  an authoring veneer's own code, because the generated law next door proves
//  FIDELITY and this proves USABILITY — that the surface a veneer would actually
//  write against exists and composes.
//
//  There is no `using Microsoft.FSharp.*` in this file and no cast to a Core type.
// ============================================================================

namespace Fuaran.Core.CSharp.Proof;

internal static class Authoring
{
    internal static void Run(Check check)
    {
        // ---- an embedded source, built column by column, read back through its schema ----

        var lookup = SourceValue.Embedded(
            TableValue.Of(
                ColumnValue.Of("region", ColumnKind.String, new[] { CellValue.Str("north"), CellValue.Str("south") }),
                ColumnValue.Of("quota", ColumnKind.Int, new[] { CellValue.Int(10), CellValue.Null })
            )
        );

        var schema = lookup.Match(
            onEmbedded: table => table.Schema.Select(e => $"{e.Name}:{e.Kind}").ToArray(),
            onReference: _ => System.Array.Empty<string>()
        );

        check.That(
            schema.Length == 2 && schema[0] == "region:String" && schema[1] == "quota:Int",
            "the embedded source should read back its derived schema, got [" + string.Join(", ", schema) + "]"
        );

        // ---- the column-layer vocabularies through their declared bridge (Phase 257) ----

        check.That(
            Vocabulary.FromCore(Vocabulary.ToCore(ColumnKind.Timestamp)) == ColumnKind.Timestamp
                && Vocabulary.FromCore(Vocabulary.ToCore(AggregateFunction.CountDistinct)) == AggregateFunction.CountDistinct,
            "a column kind and an aggregate function should survive the Vocabulary bridge"
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
