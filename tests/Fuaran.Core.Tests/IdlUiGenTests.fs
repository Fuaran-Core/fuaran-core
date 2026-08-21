module Fuaran.Core.Tests.IdlUiGenTests

open System
open System.IO
open Expecto
open Fuaran.Core.Idl
open Fuaran.Core.Tests.UiIdl

module G = Fuaran.Core.Tests.UiGenerated

// ---------------------------------------------------------------------------
// Phase 317 — real-tier migration: the COMPILED codegen leg.
//
// `IdlUiTests` proves the schema-driven *interpreter* (`Encode.encode`) reproduces
// the whole 84-fixture corpus byte-for-byte. This file closes the other half of
// the phase's acceptance: `Gen.fsharpModule` emits a *compiling* F# module
// (`UiGenerated.fs`, committed + in the build) for the full ~40-kind real
// `Fuaran.UI` vocabulary, and its **generated** `encodeNode` (not the interpreter)
// is byte-identical to the corpus.
//
// Merely compiling `UiGenerated.fs` is most of the proof — it forces every emission
// leg the 8-kind spike never exercised to be valid F#: non-discriminated *records*
// (`TRecord` → a record type + `enc<Record>`), string-keyed *maps* (`TMap` →
// `Map<string,_>`), closure / opaque sentinels (`TClosure` / `TOpaque`, erased to
// `unit` in the encoder-only structural layer — see docs/migrations/317-*),
// optional *union-case* fields (omit-on-`None`), keyword field names (`default` on
// `HoleDecl.Value`, back-tick-escaped), and **polymorphic recursion** (`encBinding<'T>`
// recurses at a fixed `float` inside `Binding<'T>` for `Binding.Format`).
//
// The byte-identity cases below construct values of the GENERATED types and encode
// them via the GENERATED encoder, one per new emission class (the spike's 4-of-8
// precedent, scaled to cover records / maps / sentinels / optional-union-fields /
// multi-node kinds / the meta family). A drift guard checks the generator still
// reproduces the committed `UiGenerated.fs`.
// ---------------------------------------------------------------------------

/// Every vendored wire snapshot, across all five families.
let private allExpected =
    displayExpected @ layoutExpected @ inputExpected @ visExpected @ metaExpected

let private expectedMap = Map.ofList allExpected

let private wire (name: string) =
    match Map.tryFind name expectedMap with
    | Some s -> s
    | None -> failwithf "no vendored wire snapshot for '%s'" name

/// All 40 kind tags, in `uiIdl.Kinds` order — the generator's input.
let private allKindTags = uiIdl.Kinds |> List.map (fun k -> k.Tag)

// ─── Shared child nodes (reused across the composite fixtures) ──────────────

let private lit (s: string) = G.TextSource.Literal s

/// Phase 691 — a stand-in handler. Closure slots carry their real signature
/// now, so a fixture supplies a function rather than `()`. The encoder emits
/// `"<closure>"` regardless of which function it is — that is the whole reason
/// the slot's host type was free to declare.
let private noop _ : G.Action<unit> = G.Action.Chain []

let private markdown1: G.Node<unit> =
    { Id = "markdown-1"
      Accessibility = None
      ExtraAttributes = None
      Motion = None
      State = None
      Style = None
      Kind = G.NodeKind.Markdown { Text = lit "Updated hourly." } }

/// `metric-1` — optional spec fields (all present) + a generic `Binding<float>` +
/// two `CellFormat`s (one with an optional `decimals`).
let private metric1: G.Node<unit> =
    { Id = "metric-1"
      Accessibility = None
      ExtraAttributes = None
      Motion = None
      State = None
      Style = None
      Kind =
        G.NodeKind.Metric
            { Label = lit "Revenue"
              Value = G.Binding.Static(Some 1234.5)
              Format = G.CellFormat.Currency "GBP"
              Tone = G.ToneVariant.Brand
              Weight = G.StyleWeight.Standard
              Emphasis = G.Emphasis.Normal
              Trend = Some(G.Binding.Static(Some 0.07))
              TrendFormat = Some(G.CellFormat.Percent(Some 1))
              Icon = Some "trending-up"
              Subtext = Some(lit "vs last month") } }

// ─── The generated fixtures — name → generated Node (one per emission class) ──

/// `stack-1` — a `Box` (role Group / Flex layout) nesting across families (Metric + Markdown).
let private stack1: G.Node<unit> =
    { Id = "stack-1"
      Accessibility = None
      ExtraAttributes = None
      Motion = None
      State = None
      Style = None
      Kind =
        G.NodeKind.Box
            { Children = [ metric1; markdown1 ]
              Heading = None
              Layout = G.LayoutMode.Flex(G.Orientation.Vertical, false, None)
              Role = G.BoxRole.Group } }

/// `btn-invoke` — the `Action` union + a `TRecord` (`InvokeArg`) list + optionals-as-None.
let private btnInvoke: G.Node<unit> =
    { Id = "btn-invoke"
      Accessibility = None
      ExtraAttributes = None
      Motion = None
      State = None
      Style = None
      Kind =
        G.NodeKind.Button
            { Label = lit "Run model"
              OnClick = G.Action.Invoke("model.score", [ ({ Addr = "rows"; Value = "all" }: G.InvokeArg) ])
              Variant = G.ButtonVariant.Primary
              Icon = None
              Tooltip = None
              Disabled = None } }

/// `form-1` — a `FormField` record list, the closure-heavy `FormFieldKind` union,
/// `TClosure`/`TOpaque` sentinels, an optional record field (`help`), a `State` binding.
let private form1: G.Node<unit> =
    // NOT a node — `FormField` is a spec record that happens to share the `Id` /
    // `Kind` field names, so it takes no envelope.
    let field id (kind: G.FormFieldKind<unit>) label required help : G.FormField<unit> =
        { Id = id
          Kind = kind
          Label = lit label
          Required = required
          Help = help |> Option.map lit
          // Fuaran-UI Phase 864 — the declared-constraint slot. `None` here on
          // purpose: `form-1` is the byte-parity fixture for the PRE-864 wire,
          // and an absent `rule` must keep encoding to nothing at all.
          Rule = None }

    { Id = "form-1"
      Accessibility = None
      ExtraAttributes = None
      Motion = None
      State = None
      Style = None
      Kind =
        G.NodeKind.Form
            { Fields =
                // Phase 692 gap-closure: every control `value` slot is optional
                // (Phase 596 auto-bind — absence is legal wire), so an explicit
                // value wraps in `Some`. Case-field ORDER is the hand-written
                // tier's positional order (the swap-prep alignment).
                [ field
                      "name"
                      (G.FormFieldKind.Text(Some(G.Binding.Static(Some "")), Some noop))
                      "Name"
                      true
                      (Some "Full legal name")
                  field "age" (G.FormFieldKind.Number(Some(G.Binding.Static(Some 0.0)), Some noop)) "Age" false None
                  field
                      "agree"
                      (G.FormFieldKind.Checkbox(Some(G.Binding.Static(Some false)), Some noop))
                      "I agree"
                      true
                      None
                  field
                      "tier"
                      (G.FormFieldKind.Choice(
                          G.Binding.Static(
                              Some [ { Label = "Basic"; Value = "basic" }; { Label = "Pro"; Value = "pro" } ]
                          ),
                          Some(G.Binding.Static(Some "basic")),
                          Some noop
                      ))
                      "Tier"
                      false
                      None
                  field
                      "notes"
                      (G.FormFieldKind.TextArea(Some(G.Binding.Static(Some "")), Some noop, 5))
                      "Notes"
                      false
                      None ]
              OnSubmit = G.Action.Chain []
              SubmitLabel = lit "Save"
              Disabled = Some(G.Binding.State("formBusy", Some false)) } }

/// `grid-1` — the `ColumnErased` record holding a `CellKindErased` union + a
/// `ColumnWidth` union + a `CellFormat` + closure/opaque sentinels.
let private grid1: G.Node<unit> =
    { Id = "grid-1"
      Accessibility = None
      ExtraAttributes = None
      Motion = None
      State = None
      Style = None
      Kind =
        G.NodeKind.DataGrid
            { Columns =
                [ ({ Field = None
                     Sortable = None
                     Editable = None
                     Format = G.CellFormat.None
                     Kind = G.CellKindErased.Text
                     Label = "Channel"
                     Value = Some(fun _ -> Fuaran.UI.HostPrelude.CellValue.Empty)
                     Width = G.ColumnWidth.Auto }
                  : G.ColumnErased<unit>) ]
              Editable = false
              RowKey = Some(fun _ -> "")
              RowKeyField = None
              SortStateKey = None
              PageSize = None
              PageStateKey = None
              DefaultSort = None
              EditStateKey = None
              Reorderable = false
              // fuaran#665 — typed rows: mirrors the authored `gridNode` sample
              // byte-for-byte (int cells encode via `JInt`; a decoded row would
              // carry `float`, which renders the same bytes under rule 5).
              Source =
                G.Binding.Static(
                    Some(
                        Seq.ofList
                            [ (Map.ofList [ "channel", box "Direct"; "revenue", box 1200 ]: Fuaran.Core.Row)
                              Map.ofList [ "channel", box "Referral"; "revenue", box 830 ] ]
                    )
                )
              StaticRows = None
              OnRowClick = None } }

/// `table-1` — the retired `Table` decode-upgraded to a static `DataGrid` (`staticRows`
/// carrying its header/row grid; cells are `TextSource`, a `Literal` renders bare).
let private table1: G.Node<unit> =
    { Id = "table-1"
      Accessibility = None
      ExtraAttributes = None
      Motion = None
      State = None
      Style = None
      Kind =
        G.NodeKind.DataGrid
            { Columns = []
              Editable = false
              RowKey = None
              RowKeyField = None
              SortStateKey = None
              PageSize = None
              PageStateKey = None
              DefaultSort = None
              EditStateKey = None
              Reorderable = false
              Source = G.Binding.Static(Some(Seq.empty: Fuaran.Core.Row seq))
              StaticRows =
                Some
                    // Phase 801 — the sort-intent slots stay absent here on purpose: this
                    // fixture is the byte-identity anchor for the pre-801 wire.
                    { DefaultSort = None
                      Headers = [ G.TextSource.Literal "Term"; G.TextSource.Literal "Definition" ]
                      Rows =
                        [ [ G.TextSource.Literal "MVU"; G.TextSource.Literal "Model-View-Update" ]
                          [ G.TextSource.Literal "DSL"; G.TextSource.Literal "Domain-specific language" ] ]
                      Sortable = None }
              OnRowClick = None } }

/// `custom-bounded-1` — an (empty) `TMap` + a `ContentHash` record + an optional string list.
let private customBounded1: G.Node<unit> =
    { Id = "custom-bounded-1"
      Accessibility = None
      ExtraAttributes = None
      Motion = None
      State = None
      Style = None
      Kind =
        G.NodeKind.Custom
            { ModuleId = "deal-flow"
              ComponentId = "QualityRing"
              Props = Map.empty
              ContentHash =
                Some(
                    { Algorithm = "SHA256"
                      Hash = "abc123def456"
                      Strictness = G.HashStrictness.StrictReplay }
                    : G.ContentHash
                )
              ExposedNodeIds = Some [ "quality-ring-segment-1"; "quality-ring-segment-2" ] } }

/// `frag-ref-args` — a non-empty `TMap` of the `FragmentArg` union (incl. a `Node`-bearing `SlotArg`).
let private fragRefArgs: G.Node<unit> =
    { Id = "frag-ref-args"
      Accessibility = None
      ExtraAttributes = None
      Motion = None
      State = None
      Style = None
      Kind =
        G.NodeKind.FragmentRef
            { Name = "stat-card"
              Args =
                Some(
                    Map.ofList
                        [ "content",
                          G.FragmentArg.SlotArg
                              { Id = "slot-tree"
                                Accessibility = None
                                ExtraAttributes = None
                                Motion = None
                                State = None
                                Style = None
                                Kind = G.NodeKind.Markdown { Text = lit "Bound slot" } }
                          "count", G.FragmentArg.Int 7 ]
                ) } }

/// `boundary-1` — a multi-single-`Node` kind (`child` + `fallback`).
let private boundary1: G.Node<unit> =
    { Id = "boundary-1"
      Accessibility = None
      ExtraAttributes = None
      Motion = None
      State = None
      Style = None
      Kind =
        G.NodeKind.ErrorBoundary
            { Child =
                { Id = "boundary-child"
                  Accessibility = None
                  ExtraAttributes = None
                  Motion = None
                  State = None
                  Style = None
                  Kind = G.NodeKind.Markdown { Text = lit "Child body" } }
              Fallback =
                { Id = "boundary-fallback"
                  Accessibility = None
                  ExtraAttributes = None
                  Motion = None
                  State = None
                  Style = None
                  Kind =
                    G.NodeKind.Callout
                        { Body = lit "Fallback rendered"
                          Dismissable = false
                          Tone = G.ToneVariant.Warning
                          Heading = Some(lit "Couldn't render")
                          Icon = None } } } }

/// `frag-decl-param` — the `HoleDecl` union (incl. the back-tick `default` field +
/// an omitted optional `default`), `Scalar` / `HoleValueSpace`, an `EffectClass` record.
let private fragDeclParam: G.Node<unit> =
    { Id = "frag-decl-param"
      Accessibility = None
      ExtraAttributes = None
      Motion = None
      State = None
      Style = None
      Kind =
        G.NodeKind.FragmentDecl
            { Body =
                { Id = "param-body"
                  Accessibility = None
                  ExtraAttributes = None
                  Motion = None
                  State = None
                  Style = None
                  Kind = G.NodeKind.Markdown { Text = lit "Parameterised body" } }
              Name = "stat-card"
              Holes =
                Some
                    [ G.HoleDecl.Value("title", G.HoleValueSpace.StringLen(1, 40), Some(G.Scalar.Str "Untitled"))
                      G.HoleDecl.Value("count", G.HoleValueSpace.IntRange(0, 100), None)
                      G.HoleDecl.Slot("content", Some "Display")
                      G.HoleDecl.Repeat("rows", G.HoleValueSpace.IntRange(1, 12)) ]
              Effect =
                Some(
                    { Determinism = G.DeterminismSource.Clock
                      HostEffect = G.HostEffect.ReadsHost }
                    : G.EffectClass
                ) } }

/// `format-bindings` — the polymorphic-recursion path: `Binding.Format`'s `source`
/// is a fixed `Binding<float>` inside `Binding<'T>`, exercised at runtime here.
let private formatBindings: G.Node<unit> =
    let fmt id (format: G.Format) (locale: G.LocaleSource) (source: G.Binding<float>) : G.Node<unit> =
        { Id = id
          Accessibility = None
          ExtraAttributes = None
          Motion = None
          State = None
          Style = None
          Kind = G.NodeKind.Markdown { Text = G.TextSource.Bound(G.Binding.Format(source, format, locale)) } }

    { Id = "format-bindings"
      Accessibility = None
      ExtraAttributes = None
      Motion = None
      State = None
      Style = None
      Kind =
        G.NodeKind.Box
            { Heading = None
              Layout = G.LayoutMode.Flex(G.Orientation.Vertical, false, None)
              Role = G.BoxRole.Group
              Children =
                [ fmt
                      "fmt-number"
                      (G.Format.Number(Some 2))
                      (G.LocaleSource.Explicit "en-US")
                      (G.Binding.Static(Some 1234.5))
                  fmt
                      "fmt-currency"
                      (G.Format.Currency "GBP")
                      (G.LocaleSource.Explicit "en-GB")
                      (G.Binding.Static(Some 1234.5))
                  fmt "fmt-percent" (G.Format.Percent Option.None) G.LocaleSource.Ambient (G.Binding.Static(Some 0.42))
                  fmt
                      "fmt-date"
                      (G.Format.Date G.DateStyle.Medium)
                      (G.LocaleSource.Explicit "fr-FR")
                      (G.Binding.Static(Some 1700000000.0))
                  fmt
                      "fmt-relative"
                      (G.Format.RelativeTime G.RelativeTimeUnit.Day)
                      (G.LocaleSource.Explicit "en-US")
                      (G.Binding.Static(Some -3.0)) ] } }

/// The representative generated fixtures — one (or more) per new emission class.
let private generatedCases: (string * G.Node<unit>) list =
    [ "metric-1", metric1
      "stack-1", stack1
      "btn-invoke", btnInvoke
      "form-1", form1
      "grid-1", grid1
      "table-1", table1
      "custom-bounded-1", customBounded1
      "frag-ref-args", fragRefArgs
      "boundary-1", boundary1
      "frag-decl-param", fragDeclParam
      "format-bindings", formatBindings ]

let private tryFindGenerated () : string option =
    let rel = Path.Combine("tests", "Fuaran.Core.Tests", "UiGenerated.fs")

    let rec climb (dir: string) (budget: int) =
        if budget < 0 || isNull dir then
            None
        else
            let candidate = Path.Combine(dir, rel)

            if File.Exists candidate then
                Some candidate
            else
                match Directory.GetParent dir with
                | null -> None
                | parent -> climb parent.FullName (budget - 1)

    [ Directory.GetCurrentDirectory(); AppContext.BaseDirectory ]
    |> List.tryPick (fun start -> climb start 12)

[<Tests>]
let tests =
    testList
        "Phase 317 — real-tier compiled codegen"
        [ testCase "the GENERATED encoder is byte-identical to the corpus across every new field class" (fun _ ->
              for name, node in generatedCases do
                  Expect.equal
                      (G.encodeNode node)
                      (wire name)
                      (sprintf "generated encoder byte mismatch for '%s'" name))

          // ---- Phase 672 task 2: the round-trip gate ----
          testCase "the GENERATED decoder round-trips the whole corpus byte-for-byte" (fun _ ->
              // The same corpus that gates the generated encoder, run the other way:
              // decode each fixture into the generated structural types and re-encode.
              // Byte-identity proves the decoder reads every field the encoder writes
              // — a structural inverse, established over the real 40-kind vocabulary
              // rather than asserted.
              let failures =
                  allExpected
                  |> List.choose (fun (name, expected) ->
                      match G.decodeNode expected with
                      | Error e -> Some(name, "decode failed: " + e)
                      | Ok node ->
                          let reEncoded = G.encodeNode node

                          if reEncoded = expected then
                              None
                          else
                              Some(
                                  name,
                                  "re-encode differs:
  in : "
                                  + expected
                                  + "
  out: "
                                  + reEncoded
                              ))

              Expect.isEmpty failures (sprintf "decodeNode >> encodeNode is not the identity for: %A" failures))

          testCase
              "generated witness: Children / ReplaceChildren over a multi-single-Node kind (ErrorBoundary)"
              (fun _ ->
                  // ErrorBoundary has two single-Node fields — the generated witness returns them in
                  // field order and re-assigns `kids` positionally (the generalised replaceArm leg).
                  let w = G.nodeWitness
                  Expect.equal (w.KindTag boundary1) "ErrorBoundary" "witness reads the kind tag"

                  Expect.equal
                      (Fuaran.Core.Tree.ids w boundary1)
                      [ "boundary-1"; "boundary-child"; "boundary-fallback" ]
                      "preorder ids: child then fallback"

                  let swapped = w.ReplaceChildren boundary1 [ markdown1; markdown1 ]

                  Expect.equal
                      (Fuaran.Core.Tree.ids w swapped)
                      [ "boundary-1"; "markdown-1"; "markdown-1" ]
                      "ReplaceChildren re-seats both node fields positionally")

          testCase "drift guard: the generator still reproduces the committed UiGenerated.fs" (fun _ ->
              let generated =
                  match
                      Gen.fsharpModuleWith UiIdlSupport.support "Fuaran.Core.Tests.UiGenerated" uiIdl allKindTags
                  with
                  | Ok s -> s
                  | Error e -> failtestf "codegen rejected the UI vocabulary: %A" e

              match tryFindGenerated () with
              | None -> skiptest "UiGenerated.fs not found on disk — drift guard skipped"
              | Some path ->
                  // Regeneration escape hatch: FUARAN_REGEN=1 rewrites the committed
                  // file instead of asserting, so a deliberate generator change is a
                  // one-command update rather than a hand-edit of generated code.
                  if System.Environment.GetEnvironmentVariable "FUARAN_REGEN" = "1" then
                      File.WriteAllText(path, generated)

                  // Whitespace-insensitive: a real generator change is caught, formatting is not.
                  let strip (s: string) =
                      s |> Seq.filter (Char.IsWhiteSpace >> not) |> Seq.toArray |> String

                  Expect.equal
                      (strip generated)
                      (strip (File.ReadAllText path))
                      "the generator no longer reproduces UiGenerated.fs — regenerate it") ]
