module Fuaran.Core.Tests.UiIdl

open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Phase 317 — the real-tier migration. ALL FIVE families, ~40 kinds.
//
// The Phase 316 spike (`Fuaran.Core.Idl.Spike`) proved the IDL drives a codec
// byte-identical to the wire over an 8-kind *mini* vocabulary. This file grew the
// IDL to the FULL real `Fuaran.UI` vocabulary — the kinds + value-unions + enums
// + records + maps whose canonical encoder is
// `Fuaran.UI.OpStream.Abstractions.CanonicalJson` — and proves the schema-driven
// encoder reproduces the live `Fuaran-UI/wire-format-fixtures` corpus
// byte-for-byte, one kind-family at a time (the staged migration the phase
// mandates). Now covers Display (16 kinds) + Layout (11) + Input (5) +
// Visualisation (4) + Meta (4) = 40 kinds.
//
// Why byte-identity is *already* guaranteed for modelled shapes: the spike's
// encoder renders through `Fuaran.Core.Canon.render`, which Ordinal-sorts object
// keys recursively, escapes control chars as `\u00xx` (no `\n`/`\r`/`\t`
// shortcuts), and pins the `ToString("R")` float layout — byte-for-byte the rules
// `CanonicalJson.appendObject` / `appendRawString` / `appendFloat` apply. So the
// only work to reach the real tier was *modelling the field classes* the mini IDL
// lacked, all landed in `Idl.fs`:
//   * closure-typed fields (`TClosure` → `"<closure>"`) — `Binding.Query`'s
//     accessor, `Action.Dispatch`'s msg, every `onChange` / `onSelect` / `onRead`;
//   * obj-erased fields (`TOpaque` → `"<opaque>"`) — `Sparkline.source`,
//     `Select.source` / `.value`, choice `options`;
//   * non-discriminated *records* (`TRecord`, no `$type`) — `FormField`,
//     `FilterSpec`, `TabHeader`, `ColumnErased`, `ContentHash`, `EffectClass`;
//   * string-keyed *maps* (`TMap`) — `Custom.props`, `FragmentRef.args`;
//   * the real recursive `TextSource` / `Binding<'T>` (now at FULL case parity
//     with the hand-written tier — Static/Query/Filter/Selection/State/Computed/
//     I18n/Local/Format/Transform/Invoke, the Phase 692 gap-closure) / `Action`
//     / `CellFormat` / `HoleDecl` unions;
//   * HOSTED slots (`THosted`) — `Binding.Transform`'s `source` / `pipeline`
//     delegate to `Fuaran.Core.ColumnCodec` / `DataFrameCodec`, and the `Range`
//     control's transparent-Static value carries a slot-specific codec.
//
// The earlier exclusions have all since landed: the node envelope (Phase 690),
// `'Msg` threading via `TFn` (Phase 691), `grid-transform` + the whole Transform
// family and the Phase 596 auto-bind omissions (the Phase 692 gap-closure — the
// full 85-fixture node corpus now round-trips through the generated layer, the
// tier-side `GeneratedLayerTests` pin). Still out of scope: multi-param generic
// specs (`GridSpecOf<'row,'Msg>` — a typed *author* facade, not wire-visible)
// and the `Types.fs` switch-over itself (Phase 692's remaining work).
// ---------------------------------------------------------------------------

// ─── Enums (bare-string DUs on the wire) ───────────────────────────────────

let private headingVariant =
    { Name = "HeadingVariant"
      Cases = [ "Standard"; "Eyebrow"; "Caption"; "Lead" ] }

let private badgeVariant =
    { Name = "BadgeVariant"
      Cases = [ "Neutral"; "Brand"; "Success"; "Warning"; "Critical"; "Info" ] }

let private orientation =
    { Name = "Orientation"
      Cases = [ "Vertical"; "Horizontal" ] }

/// `Box.role` — the container-role enum (Fuaran-UI 0.2.0 Box unification: Dashboard /
/// Card / Stack / GridLayout collapsed into one `Box` kind with a `role` + `layout`).
let private boxRole =
    { Name = "BoxRole"
      Cases = [ "Dashboard"; "Card"; "Group" ] }

let private mathDisplay =
    { Name = "MathDisplay"
      Cases = [ "Inline"; "Block" ] }

let private imageVariant =
    { Name = "ImageVariant"
      Cases = [ "Default"; "Avatar"; "Rounded" ] }

let private toneVariant =
    { Name = "ToneVariant"
      Cases = [ "Default"; "Subdued"; "Brand"; "Success"; "Warning"; "Critical"; "Info" ] }

let private styleWeight =
    { Name = "StyleWeight"
      Cases = [ "Compact"; "Standard"; "Spacious" ] }

let private emphasis =
    { Name = "Emphasis"
      Cases = [ "Quiet"; "Normal"; "Loud" ] }

// ─── Phase 690: the node envelope (WIRE_FORMAT.md §3.1) ────────────────────
//
// `style` / `state` / `accessibility` sit on the NODE, beside `id` and `kind`,
// and each is omitted when empty. Excluded from the IDL since Phase 671 on the
// stated grounds that no corpus fixture carried one — which Phase 674 found to be
// false (`style-role-voice-1` does, and the generated layer was corrupting it).

let private styleRole =
    { Name = "StyleRole"
      Cases = [ "None"; "Eyebrow"; "Data"; "Lede"; "Caption" ] }

let private fontVoice =
    { Name = "FontVoice"
      Cases = [ "Default"; "Display"; "Structural" ] }

/// Phase 691 — the per-node animation token. NEVER on the wire (`WIRE_FORMAT.md`
/// §9: motion is consumer-authored, not AI-authored), and declared only so the
/// host-only `Node.motion` field has a type to name.
let private motion =
    { Name = "Motion"
      Cases =
        [ "None"
          "PulseDuringLoad"
          "FadeInOnMount"
          "SlideInFromBelow"
          "ShakeOnError"
          "RotateOnRefresh"
          "SlideInFromRight"
          "ExpandCollapse" ] }

/// `LayoutKind.ScrollArea`'s scroll-axis enum (distinct from `Orientation` — it
/// adds `Both`).
let private scrollOrientation =
    { Name = "ScrollOrientation"
      Cases = [ "Vertical"; "Horizontal"; "Both" ] }

let private buttonVariant =
    { Name = "ButtonVariant"
      Cases = [ "Primary"; "Secondary"; "Tertiary"; "Destructive" ] }

let private fileReadEncoding =
    { Name = "FileReadEncoding"
      Cases = [ "Text"; "Base64"; "DataUrl" ] }

let private dateVariant =
    { Name = "DateVariant"
      Cases = [ "Date"; "Time"; "DateTime" ] }

let private dateStyle =
    { Name = "DateStyle"
      Cases = [ "Short"; "Medium"; "Long"; "Full" ] }

let private relativeTimeUnit =
    { Name = "RelativeTimeUnit"
      Cases = [ "Second"; "Minute"; "Hour"; "Day"; "Week"; "Month"; "Year" ] }

let private chartKind =
    { Name = "ChartKind"
      Cases = [ "Line"; "Bar"; "Area"; "Pie"; "Scatter"; "Heatmap" ] }

let private hashStrictness =
    { Name = "HashStrictness"
      Cases = [ "StrictReplay"; "AdvisoryWarning"; "Enforced" ] }

let private hostEffect =
    { Name = "HostEffect"
      Cases = [ "Pure"; "ReadsHost"; "WritesHost" ] }

let private determinismSource =
    { Name = "DeterminismSource"
      Cases = [ "Deterministic"; "Clock"; "Random"; "Network" ] }

// ─── Value-unions ──────────────────────────────────────────────────────────

let private req (name: string) (t: IdlType) : IdlField =
    { Name = name
      Type = t
      Opt = Required }

let private opt (name: string) (t: IdlType) : IdlField =
    { Name = name
      Type = t
      Opt = Optional }

// ─── Phase 691: function-typed slots carry their HOST signature ────────────
//
// Wire behaviour is identical to `TClosure` — the fixed `"<closure>"` sentinel,
// presence-only decode. What `TFn` adds is the declaration, which is what lets
// the generated layer be the authoring type rather than a projection of it (D2).
//
// Signatures are read from `Fuaran.UI/Types.fs`, NOT inferred from the field
// name. Two shapes dominate: a handler returning `Action<'Msg>` (every `on*` on a
// spec) and a pure projection returning a value (the `DataGrid` column functions).
//
// Where an argument's host type is not IDL-declared — `BindingContext`,
// `ErrorPayload`, `CellValue`, `FileSelection` — the slot takes `obj` in that
// position and says so at the site. That is a real fidelity loss against the
// hand-written type, and it is the one thing standing between this phase and a
// generated layer that could be authored against directly; Phase 692 resolves it
// when it reconciles the two authoring surfaces.

/// A function-typed slot: the F# declaration, the TypeScript one, and the
/// expression the decoder puts in the slot (written at `'Msg = obj`).
let private hostOnly (name: string) (fs: string) (ph: string) : IdlField =
    { Name = name
      Type =
        TFn
            { FSharp = fs
              TypeScript = "never"
              Placeholder = ph }
      Opt = HostOnly }

let private fn (fs: string) (ts: string) (ph: string) : IdlType =
    TFn
        { FSharp = fs
          TypeScript = ts
          Placeholder = ph }

/// The common shape — an event handler `arg -> Action<'Msg>`. The placeholder is
/// `Action.Chain []`: a decoded tree has no behaviour, and "do nothing" is the
/// honest stand-in for a handler the wire could not carry.
let private handlerOf (arg: string) (tsArg: string) : IdlType =
    fn (arg + " -> Action<'Msg>") ("(v: " + tsArg + ") => Action") ("(fun (_: " + arg + ") -> Action.Chain [])")

/// A pure projection `arg -> result` (no `'Msg`) — the `DataGrid` column
/// functions and `Binding`'s accessors.
let private projOf (arg: string) (result: string) (tsSig: string) (ph: string) : IdlType =
    fn (arg + " -> " + result) tsSig ph

/// A field omitted on the wire when it equals its identity default `dflt`, restored
/// on absence (Fuaran-UI Phase 460 omit-when-default: tone/weight/emphasis/format/width).
let private omit (name: string) (t: IdlType) (dflt: IdlValue) : IdlField =
    { Name = name
      Type = t
      Opt = OmitDefault dflt }

/// `TextSource` — `Literal` (the corpus's only Display case) + `Bound`
/// (`Binding<string>`). The `I18n` case (a `Map<string, JsonValue>` arg bag)
/// rides a later slice — the IDL has no map type yet and no Display fixture uses it.
let private textSource =
    { Name = "TextSource"
      Params = []
      Cases =
        [ { Tag = "Literal"
            Fields = [ req "text" TStr ] }
          { Tag = "Bound"
            Fields = [ req "binding" (TUnion("Binding", [ TStr ])) ] } ] }

/// `Binding<'T>` — the real recursive binding union, now at full case parity with
/// the hand-written tier (the Phase 692 gap-closure): every case the hand-written
/// encoder can emit is modelled — `Static` / `Query` / `Filter` / `Selection` /
/// `State` / `Computed` / `I18n` / `Local` / `Format` / `Transform` / `Invoke`.
/// The `Deferred<'T>` trio (`Pending` / `Ready` / `Error`) is deliberately NOT
/// here: it is not a `Binding` case at all but a separate runtime-only envelope
/// ("a runtime value (the resolver produces it); not wire-serialised" — the
/// resolver's async view of an `Invoke`), and the corpus carries no occurrence.
let private binding =
    { Name = "Binding"
      Params = [ "T" ]
      Cases =
        // Phase 677 — absence is STRUCTURAL: a binding carrying no value omits the
        // key rather than emitting JSON null, for which the wire model has no case.
        [ { Tag = "Static"
            Fields = [ opt "value" (TVar "T") ] }
          // Phase 671 step 2 — the direct byte-diff caught this: the wire has NOT
          // carried `accessor` since 0.2.0 (the encoder renders `dependsOn` +
          // `name` only). The closure survives as a HOST-ONLY slot (never encoded,
          // restored to the identity projection on decode) so the generated case
          // can hold everything the hand-written one holds. `dependsOn` rides as a
          // string array, omitted when empty.
          { Tag = "Query"
            Fields =
              [ hostOnly "accessor" "obj -> 'T" "(fun (raw: obj) -> unbox raw)"
                opt "dependsOn" (TList TStr)
                req "name" TStr ] }
          // `defaultValue` (Fuaran-UI 0.2.0) rides the wire when present, omitted
          // when None — the value the resolver yields before the filter is first
          // written.
          { Tag = "Filter"
            Fields = [ opt "defaultValue" (TVar "T"); req "name" TStr ] }
          // Row selection on `nodeId` (Fuaran-UI 0.2.9/0.2.10). `defaultValue` and
          // `field` (the declarative row-field projection) ride when present; the
          // accessor closure is host-only — the hand-written POLICY decoder
          // synthesises `projectSelectionField field` when `field` is present, a
          // context-dependent restoration the structural placeholder (identity)
          // deliberately does not attempt.
          { Tag = "Selection"
            Fields =
              [ hostOnly "accessor" "obj -> 'T" "(fun (raw: obj) -> unbox raw)"
                opt "defaultValue" (TVar "T")
                opt "field" TStr
                req "nodeId" TStr ] }
          { Tag = "State"
            Fields = [ opt "defaultValue" (TVar "T"); req "key" TStr ] }
          { Tag = "Computed"
            // `BindingContext -> 'T`. `BindingContext` is a HOST type (it carries a
            // `TryGetState<'T>` member), so the argument erases to `obj` here.
            Fields = [ req "fn" (projOf "obj" "'T" "(ctx: unknown) => T" "(fun _ -> Unchecked.defaultof<'T>)") ] }
          // A controlled-input local buffer. `initialFrom` recurses at the same
          // `'T`; `format` / `onCommit` / `parse` are closures; `flushOn` is a DU.
          { Tag = "Local"
            Fields =
              [ req "flushOn" (TUnion("LocalFlushTrigger", []))
                req "format" (projOf "'T" "string" "(v: T) => string" "(fun _ -> \"\")")
                req "initialFrom" (TUnion("Binding", [ TVar "T" ]))
                opt "onCommit" (projOf "'T" "obj" "(v: T) => unknown" "(fun _ -> (\"<closure>\" :> obj))")
                req "parse" (projOf "string" "Result<'T, string>" "(s: string) => T" "(fun _ -> Error \"<closure>\")") ] }
          // Locale-aware formatted string. `source` is ALWAYS `Binding<float>`
          // (independent of `'T`); `format` / `locale` are bounded DUs.
          { Tag = "Format"
            Fields =
              [ req "format" (TUnion("Format", []))
                req "locale" (TUnion("LocaleSource", []))
                req "source" (TUnion("Binding", [ TFloat ])) ] }
          // i18n catalog lookup. `args` is a name-keyed bag of `Binding<obj>`
          // placeholder sources, omitted when None. The obj-erased positions
          // (here and `Transform.params`) instantiate at `JVal` — the typed
          // verbatim carrier — because `TOpaque` would erase real defaultValues
          // to a sentinel and lose bytes.
          { Tag = "I18n"
            Fields = [ opt "args" (TMap(TUnion("Binding", [ TJson ]))); req "key" TStr ] }
          // Declarative dataframe transform (Fuaran-UI Phase 282/424 — the Compute
          // layer). `source` / `pipeline` are HOSTED slots: real `Fuaran.Core`
          // types rendered by Core's own codecs under the same `Canon` discipline,
          // so the composite splices in canonical and byte-stable ($type < params
          // < pipeline < source after the Ordinal sort). `params` binds pipeline
          // `ColExpr.Param` names to scalar binding sources, omitted when empty.
          { Tag = "Transform"
            Fields =
              [ opt "params" (TList(TRecord "TransformParam"))
                req
                    "pipeline"
                    (TList(
                        THosted
                            { FSharp = "Fuaran.Core.Transform"
                              Encode = "Fuaran.Core.DataFrameCodec.encodeTransform"
                              Decode =
                                "(fun __j -> Fuaran.Core.DataFrameCodec.decodeTransform __j |> Result.mapError string)" }
                    ))
                req
                    "source"
                    (THosted
                        { FSharp = "Fuaran.Core.DataSource"
                          Encode = "Fuaran.Core.ColumnCodec.encodeJson"
                          Decode = "(fun __j -> Fuaran.Core.ColumnCodec.decodeJson __j |> Result.mapError string)" }) ] }
          // Host-registered capability value. Same wire shape as `Action.Invoke`.
          { Tag = "Invoke"
            Fields = [ req "args" (TList(TRecord "InvokeArg")); req "capabilityId" TStr ] } ] }

/// `CellFormat` — the column / `Metric` display-format vocabulary. `Number` /
/// `Percent` carry an *optional* `decimals` (omitted on `None`, rule 4); `Custom`
/// carries a closure fn.
let private cellFormat =
    { Name = "CellFormat"
      Params = []
      Cases =
        [ { Tag = "None"; Fields = [] }
          { Tag = "Number"
            Fields = [ opt "decimals" TInt ] }
          { Tag = "Currency"
            Fields = [ req "code" TStr ] }
          { Tag = "Percent"
            Fields = [ opt "decimals" TInt ] }
          { Tag = "SignificantDigits"
            Fields = [ req "digits" TInt ] }
          { Tag = "Date"
            Fields = [ req "format" TStr ] }
          { Tag = "Custom"
            // `CellValue -> string`; `CellValue` is a host type, so the argument erases.
            Fields = [ req "fn" (projOf "obj" "string" "(v: unknown) => string" "(fun _ -> \"\")") ] } ] }

/// `Action<'Msg>` — the effect-typed action union. `Chain` recurses; `Dispatch`
/// / `onRead` payloads are closures; `Invoke` / `ReadFileBody` carry data. The
/// data cases not in the corpus (`Navigate` / `Notify` / `SetState` / `Call` /
/// `CommitLocal`) are omitted until a fixture exercises them.
let private action =
    { Name = "Action"
      Params = []
      Cases =
        [ { Tag = "Chain"
            Fields = [ req "ops" (TList(TUnion("Action", []))) ] }
          { Tag = "WriteToClipboard"
            Fields = [ req "text" TStr ] }
          // Fuaran-UI 0.2.x: the dispatch msg closure is omitted entirely (no wire key).
          // `Dispatch of 'Msg`. The payload is a host value with NO wire projection —
          // `{"$type":"Dispatch"}` is the whole encoding, before and after. Declaring it
          // host-only is what lets the generated `Action` be the authoring `Action`.
          { Tag = "Dispatch"
            Fields = [ hostOnly "msg" "'Msg" "((\"<dispatch>\" :> obj))" ] }
          { Tag = "Invoke"
            Fields = [ req "args" (TList(TRecord "InvokeArg")); req "capabilityId" TStr ] }
          { Tag = "ReadFileBody"
            Fields =
              [ req "encoding" (TEnum "FileReadEncoding")
                req "fileRef" TStr
                opt "onRead" (fn "string -> 'Msg" "(body: string) => Msg" "(fun (_: string) -> (\"<closure>\" :> obj))") ] }
          // `ApiEndpoint` is a bare string on the wire; `into` is the declarative
          // result target, omitted when None; `onResult` rides only when present.
          { Tag = "Call"
            Fields =
              [ req "endpoint" TStr
                opt "into" (TUnion("CallResultTarget", []))
                opt "onResult" (fn "obj -> 'Msg" "(r: unknown) => Msg" "(fun (_: obj) -> (\"<closure>\" :> obj))") ] }
          { Tag = "Navigate"
            Fields = [ req "route" TStr ] }
          { Tag = "CommitLocal"
            Fields = [ req "nodeId" TStr ] }
          // Phase 676 — the three JSON-payload actions. `TJson`, never `TOpaque`:
          // these carry real data in both directions (`Notify` is the estate's
          // cross-host data primitive), so erasing them to a sentinel would be
          // silent data loss.
          { Tag = "Notify"
            Fields = [ req "channel" TStr; req "payload" TJson ] }
          { Tag = "SetState"
            Fields = [ req "key" TStr; req "value" TJson ] }
          { Tag = "AiTool"
            Fields = [ req "args" TJson; req "toolName" TStr ] } ] }

/// Where a `Call`'s result lands, declaratively. NOTE the wire tags are `State` /
/// `Query`, not the F# case names `IntoState` / `IntoQuery`.
let private callResultTarget =
    { Name = "CallResultTarget"
      Params = []
      Cases =
        [ { Tag = "State"
            Fields = [ req "key" TStr ] }
          { Tag = "Query"
            Fields = [ req "name" TStr ] } ] }

/// The locale-aware `Binding.Format` intent union (distinct from [[cellFormat]] —
/// this one carries `isoCode` / `dateStyle` / `unit`, not `code`).
let private formatUnion =
    { Name = "Format"
      Params = []
      Cases =
        [ { Tag = "Number"
            Fields = [ opt "decimals" TInt ] }
          { Tag = "Currency"
            Fields = [ req "isoCode" TStr ] }
          { Tag = "Percent"
            Fields = [ opt "decimals" TInt ] }
          { Tag = "Date"
            Fields = [ req "dateStyle" (TEnum "DateStyle") ] }
          { Tag = "RelativeTime"
            Fields = [ req "unit" (TEnum "RelativeTimeUnit") ] } ] }

let private localeSource =
    { Name = "LocaleSource"
      Params = []
      Cases =
        [ { Tag = "Ambient"; Fields = [] }
          { Tag = "Explicit"
            Fields = [ req "tag" TStr ] } ] }

let private localFlushTrigger =
    { Name = "LocalFlushTrigger"
      Params = []
      Cases =
        [ { Tag = "OnBlur"; Fields = [] }
          { Tag = "OnSubmit"; Fields = [] }
          { Tag = "OnDebounce"
            Fields = [ req "milliseconds" TInt ] }
          { Tag = "OnCommitAction"; Fields = [] } ] }

/// `Box.layout` — the container-layout mode (Fuaran-UI 0.2.0 Box unification). `Auto`
/// (was `Dashboard`), `Flex` (was `Stack`, carries `direction` + `wrap`), `Grid` (was
/// `GridLayout`, carries `cols` + an optional `templateColumns`).
let private layoutMode =
    { Name = "LayoutMode"
      Params = []
      Cases =
        [ { Tag = "Auto"; Fields = [] }
          { Tag = "Flex"
            Fields = [ req "direction" (TEnum "Orientation"); req "wrap" TBool ] }
          { Tag = "Grid"
            Fields = [ req "cols" TInt; opt "templateColumns" TStr ] } ] }

/// `FormFieldKind<'Msg>` — the per-field input-shape union, shared by `Form`
/// fields AND `Filters` chips (the 0.2.0 filters-unification — the separate
/// `FilterKind` union this file carried until the Phase 692 gap-closure was
/// pre-unification drift).
///
/// **Every `value` slot is Optional (Phase 596 auto-bind).** The wire contract is
/// that a control may omit `value` entirely: a filter chip auto-binds
/// `Filter(name)`, a form field `State(field id, typed placeholder)`. That
/// synthesis is CONTEXT-dependent — it turns on the enclosing record's `name` /
/// `id` — so it is policy, owned by the hand-written decoder above this layer;
/// the structural layer carries absence as absence (`None` ⇔ no key), which is
/// what makes the round-trip byte-exact without expressing the context rule.
let private formFieldKind =
    { Name = "FormFieldKind"
      Params = []
      Cases =
        [ { Tag = "Text"
            Fields =
              [ opt "onChange" (handlerOf "string" "string")
                opt "value" (TUnion("Binding", [ TStr ])) ] }
          { Tag = "Number"
            Fields =
              [ opt "onChange" (handlerOf "float" "number")
                opt "value" (TUnion("Binding", [ TFloat ])) ] }
          { Tag = "Checkbox"
            Fields =
              [ opt "onToggle" (handlerOf "bool" "boolean")
                opt "value" (TUnion("Binding", [ TBool ])) ] }
          { Tag = "Choice"
            Fields =
              [ opt "onChange" (handlerOf "string option" "string | null")
                req "options" (TUnion("Binding", [ TList(TRecord "SelectOption") ]))
                opt "value" (TUnion("Binding", [ TStr ])) ] }
          { Tag = "TextArea"
            Fields =
              [ opt "onChange" (handlerOf "string" "string")
                req "rows" TInt
                opt "value" (TUnion("Binding", [ TStr ])) ] }
          { Tag = "RangedNumber"
            Fields =
              [ opt "onChange" (handlerOf "float" "number")
                opt "value" (TUnion("Binding", [ TFloat ]))
                opt "min" TFloat
                opt "max" TFloat
                opt "step" TFloat ] }
          // Dual-thumb numeric range (0.2.0 — absorbed FilterKind.RangeFilter).
          // The value slot is HOSTED because its Static case is TRANSPARENT on
          // the wire: `Binding.Static (Some pair)` encodes as the bare
          // `{"max":…,"min":…}` object (no `$type`), while every other binding
          // case keeps its tagged form with a RangePair static payload. That is
          // a property of this SLOT, not of the Binding union, so the slot
          // carries its own codec over the generated `encBinding` / `decBinding`
          // + `RangePair` record codecs.
          { Tag = "Range"
            Fields =
              [ opt "max" TFloat
                opt "min" TFloat
                opt "onChange" (handlerOf "float * float" "[number, number]")
                opt "step" TFloat
                opt
                    "value"
                    (THosted
                        { FSharp = "Binding<RangePair>"
                          Encode =
                            "(fun (v: Binding<RangePair>) -> match v with | Binding.Static(Some p) -> encRangePair p | __other -> encBinding encRangePair __other)"
                          Decode =
                            "(fun (j: JVal) -> match j with | JObj __rf when not (__rf |> List.exists (fun (k, _) -> k = \"$type\")) -> decRangePair j |> Result.map (fun p -> Binding.Static(Some p)) | __other -> decBinding decRangePair __other)" }) ] }
          { Tag = "SegmentedChoice"
            Fields =
              [ opt "onChange" (handlerOf "string option" "string | null")
                req "options" (TUnion("Binding", [ TList(TRecord "SelectOption") ]))
                req "orientation" (TEnum "Orientation")
                opt "value" (TUnion("Binding", [ TStr ])) ] }
          { Tag = "Date"
            Fields =
              [ opt "onChange" (handlerOf "string option" "string | null")
                opt "value" (TUnion("Binding", [ TStr ]))
                req "variant" (TEnum "DateVariant")
                opt "min" TStr
                opt "max" TStr
                opt "step" TFloat ] } ] }

// _(The separate `FilterKind` union this file carried until the Phase 692
// gap-closure was pre-unification drift: the hand-written tier's `FilterSpec`
// holds a `FormFieldKind` — one control vocabulary for forms and filter strips
// since the 0.2.0 filters-unification. `filters-declarative`'s Range chip is
// what surfaced it.)_

/// `ColumnWidth` — a `DataGrid` column's sizing intent.
let private columnWidth =
    { Name = "ColumnWidth"
      Params = []
      Cases =
        [ { Tag = "Auto"; Fields = [] }
          { Tag = "Fixed"
            Fields = [ req "pixels" TInt ] }
          { Tag = "Flex"
            Fields = [ req "weight" TFloat ] } ] }

/// `CellKindErased<'Msg>` — the row-erased grid-cell shape union. Non-interactive
/// cases (`Text` / `Numeric` / `Date`) are field-less; the interactive ones carry
/// closure accessors (`get` / `onEdit` / `onClick` / `fractionFn` …). `ButtonGroup`
/// carries a list of `ButtonGroupItem` records. Only `Text` is corpus-exercised
/// (grid-1); the rest are modelled for a faithful union surface.
let private cellKindErased =
    { Name = "CellKindErased"
      Params = []
      Cases =
        [ { Tag = "Text"; Fields = [] }
          { Tag = "Numeric"; Fields = [] }
          { Tag = "Date"; Fields = [] }
          { Tag = "Editable"
            // `(obj * CellValue) -> Action<'Msg>`; `CellValue` is a host type.
            Fields = [ opt "onEdit" (handlerOf "obj * obj" "[unknown, unknown]") ] }
          { Tag = "Checkbox"
            Fields =
              [ req "get" (projOf "obj" "bool" "(row: unknown) => boolean" "(fun _ -> false)")
                opt "onToggle" (handlerOf "obj * bool" "[unknown, boolean]") ] }
          { Tag = "Button"
            Fields =
              [ req "label" (TUnion("TextSource", []))
                opt "onClick" (handlerOf "obj" "unknown") ] }
          { Tag = "ButtonGroup"
            Fields = [ req "buttons" (TList(TRecord "ButtonGroupItem")) ] }
          { Tag = "Link"
            Fields =
              [ req "hrefFn" (projOf "obj" "string" "(row: unknown) => string" "(fun _ -> \"\")")
                req
                    "labelFn"
                    (projOf "obj" "TextSource" "(row: unknown) => TextSource" "(fun _ -> TextSource.Literal \"\")") ] }
          { Tag = "Pill"
            Fields =
              [ req
                    "labelFn"
                    (projOf "obj" "TextSource" "(row: unknown) => TextSource" "(fun _ -> TextSource.Literal \"\")")
                req
                    "toneFn"
                    (projOf "obj" "ToneVariant" "(row: unknown) => ToneVariant" "(fun _ -> ToneVariant.Default)") ] }
          { Tag = "Progress"
            Fields =
              [ req "fractionFn" (projOf "obj" "float" "(row: unknown) => number" "(fun _ -> 0.0)")
                req
                    "labelFn"
                    (projOf "obj" "TextSource" "(row: unknown) => TextSource" "(fun _ -> TextSource.Literal \"\")") ] }
          { Tag = "Custom"
            // `(obj -> JVal) -> Node<'Msg>` — a cell renderer over the row projector.
            Fields =
              [ req
                    "fn"
                    (fn
                        "(obj -> JVal) -> Node<'Msg>"
                        "(proj: (row: unknown) => unknown) => Node"
                        "(fun _ -> Unchecked.defaultof<Node<obj>>)") ] } ] }

// ─── Meta-family unions (parameterised fragments) ───────────────────────────

/// A hole's value-space (bind-time validation domain) on a `FragmentDecl`.
let private holeValueSpace =
    { Name = "HoleValueSpace"
      Params = []
      Cases =
        [ { Tag = "IntRange"
            Fields = [ req "max" TInt; req "min" TInt ] }
          { Tag = "FloatRange"
            Fields = [ req "max" TFloat; req "min" TFloat ] }
          { Tag = "StringLen"
            Fields = [ req "maxLen" TInt; req "minLen" TInt ] }
          { Tag = "Enum"
            Fields = [ req "choices" (TList TStr) ] }
          { Tag = "AnyString"; Fields = [] } ] }

/// A boxed scalar — a hole default or a `FragmentRef` value arg. Self-describing
/// (`$type` pins the CLR shape).
let private scalar =
    { Name = "Scalar"
      Params = []
      Cases =
        [ { Tag = "Int"
            Fields = [ req "value" TInt ] }
          { Tag = "Float"
            Fields = [ req "value" TFloat ] }
          { Tag = "Bool"
            Fields = [ req "value" TBool ] }
          { Tag = "Str"
            Fields = [ req "value" TStr ] } ] }

/// A declared hole on a parameterised fragment (`FragmentDecl.holes`).
let private holeDecl =
    { Name = "HoleDecl"
      Params = []
      Cases =
        [ { Tag = "Value"
            Fields =
              [ opt "default" (TUnion("Scalar", []))
                req "name" TStr
                req "space" (TUnion("HoleValueSpace", [])) ] }
          { Tag = "Slot"
            Fields = [ opt "kindConstraint" TStr; req "name" TStr ] }
          { Tag = "Repeat"
            Fields = [ req "countSpace" (TUnion("HoleValueSpace", [])); req "name" TStr ] } ] }

/// A bound argument at a `FragmentRef` — a scalar value or a slot subtree. Shares
/// the scalar tags with [[scalar]] plus `SlotArg` (a `Node`-bearing tree).
let private fragmentArg =
    { Name = "FragmentArg"
      Params = []
      Cases =
        [ { Tag = "Int"
            Fields = [ req "value" TInt ] }
          { Tag = "Float"
            Fields = [ req "value" TFloat ] }
          { Tag = "Bool"
            Fields = [ req "value" TBool ] }
          { Tag = "Str"
            Fields = [ req "value" TStr ] }
          { Tag = "SlotArg"
            Fields = [ req "tree" TNode ] } ] }

// ─── Records (non-discriminated objects — no `$type`) ───────────────────────

let private invokeArgRecord =
    { Name = "InvokeArg"
      Fields = [ req "addr" TStr; req "value" TStr ] }

/// An option in a `Select` / `Choice` / `SegmentedChoice` payload (Fuaran-UI 0.2.x
/// typed-Static: the choice `source` / `options` carry a real `SelectOption` list).
let private selectOptionRecord =
    { Name = "SelectOption"
      Fields = [ req "label" TStr; req "value" TStr ] }

/// A `Map.source` marker (Fuaran-UI 0.2.x typed-Static: the map source carries a real
/// marker list instead of the opaque sentinel).
let private mapMarkerRecord =
    { Name = "MapMarker"
      Fields = [ req "label" TStr; req "latitude" TFloat; req "longitude" TFloat ] }

/// A `DataGrid.staticRows` payload — the header/row grid a legacy `Table` decode-upgrades
/// into (Fuaran-UI Phase 393: `Table` retired, becomes a static `DataGrid`). Bare strings.
let private staticRowsRecord =
    { Name = "StaticRows"
      Fields = [ req "headers" (TList TStr); req "rows" (TList(TList TStr)) ] }

let private formFieldRecord =
    { Name = "FormField"
      Fields =
        [ req "id" TStr
          req "kind" (TUnion("FormFieldKind", []))
          req "label" (TUnion("TextSource", []))
          req "required" TBool
          opt "help" (TUnion("TextSource", [])) ] }

let private filterSpecRecord =
    { Name = "FilterSpec"
      Fields =
        [ req "kind" (TUnion("FormFieldKind", []))
          req "label" (TUnion("TextSource", []))
          req "name" TStr ] }

/// One `Binding.Transform` parameter — binds a pipeline `ColExpr.Param` name to a
/// scalar binding source. `from` instantiates `Binding` at `JVal` (the typed
/// verbatim carrier for obj-erased positions).
let private transformParamRecord =
    { Name = "TransformParam"
      Fields = [ req "from" (TUnion("Binding", [ TJson ])); req "name" TStr ] }

/// The `{max, min}` payload of a `Range` control's value — the wire shape of the
/// hand-written tier's `(min, max)` float pair (the IDL has no tuple type; the
/// record IS the wire object, so nothing is lost in the trade).
let private rangePairRecord =
    { Name = "RangePair"
      Fields = [ req "max" TFloat; req "min" TFloat ] }

let private tabHeaderRecord =
    { Name = "TabHeader"
      Fields =
        [ req "label" (TUnion("TextSource", []))
          opt "icon" TStr
          opt "disabled" (TUnion("Binding", [ TBool ])) ] }

/// A `DataGrid` column, row-erased. Fuaran-UI Phase 425: `value` (the projection
/// closure) and `field` (the declarative row-property name) are SIBLING optional
/// slots, each omitted-when-None — a closure-authored column keeps
/// `"value":"<closure>"` byte-stable, a decoded/field-named column carries
/// `"field":"…"` instead. `format` / `width` omitted-when-default (Phase 460).
let private columnErasedRecord =
    { Name = "ColumnErased"
      Fields =
        [ opt "field" TStr
          omit "format" (TUnion("CellFormat", [])) (VUnion("None", []))
          req "kind" (TUnion("CellKindErased", []))
          req "label" TStr
          // `obj -> CellValue`; `CellValue` is a host type, so the result erases.
          opt "value" (projOf "obj" "obj" "(row: unknown) => unknown" "(fun _ -> (\"<closure>\" :> obj))")
          omit "width" (TUnion("ColumnWidth", [])) (VUnion("Auto", [])) ] }

/// One button of a `CellKindErased.ButtonGroup` (`onClick` is a closure).
let private buttonGroupItemRecord =
    { Name = "ButtonGroupItem"
      Fields =
        [ req "label" (TUnion("TextSource", []))
          opt "onClick" (handlerOf "obj" "unknown") ] }

/// A `Custom` node's content-identity envelope (`strictness` is a bare-string DU).
let private contentHashRecord =
    { Name = "ContentHash"
      Fields =
        [ req "algorithm" TStr
          req "hash" TStr
          req "strictness" (TEnum "HashStrictness") ] }

/// A `FragmentDecl`'s two-axis effect class (omitted on the wire when pure +
/// deterministic — modelled here as an optional field on the kind).
let private effectClassRecord =
    { Name = "EffectClass"
      Fields =
        [ req "determinism" (TEnum "DeterminismSource")
          req "hostEffect" (TEnum "HostEffect") ] }

// ─── Type aliases for the field shapes (readability) ───────────────────────

let private TS = TUnion("TextSource", [])
let private bindingOf (t: IdlType) = TUnion("Binding", [ t ])
let private CF = TUnion("CellFormat", [])
/// `IconSource` is a bare string on the wire (`"icon":"trending-up"`).
let private icon = TStr

// ─── The node envelope records (Phase 690, WIRE_FORMAT.md §3.1) ────────────
//
// Field ORDER is Ordinal throughout — the TS backend emits in declared order and
// does not sort, so a declaration out of Ordinal order diverges the two hosts.

/// `{ "emphasis"?, "role"?, "tone"?, "voice"?, "weight"? }` — every field
/// individually omit-when-default (Fuaran-UI Phase 147 role/voice, Phase 460 the
/// other three), and the whole object omitted when all five are default.
let private semanticStyleRecord =
    { Name = "SemanticStyle"
      Fields =
        [ omit "emphasis" (TEnum "Emphasis") (VEnum "Normal")
          omit "role" (TEnum "StyleRole") (VEnum "None")
          omit "tone" (TEnum "ToneVariant") (VEnum "Default")
          omit "voice" (TEnum "FontVoice") (VEnum "Default")
          omit "weight" (TEnum "StyleWeight") (VEnum "Standard") ] }

/// `{ "onEmpty"?: Node, "onError"?: "<closure>", "onLoading"?: Node }`.
/// `onError` is the `ErrorPayload -> Node` callback — unobservable, so the
/// sentinel, and its PRESENCE is the only thing the wire carries.
let private stateBehaviourRecord =
    { Name = "StateBehaviour"
      Fields =
        [ opt "onEmpty" TNode
          opt "onError" (fn "obj -> Node<'Msg>" "(e: unknown) => Node" "(fun _ -> Unchecked.defaultof<Node<obj>>)")
          opt "onLoading" TNode ] }

/// `{ "describedBy"?, "hidden"?, "label"?, "labelledBy"?, "liveRegion"?, "role"? }`.
///
/// `role` and `liveRegion` are `TStr`, deliberately, and this is read from the
/// ENCODER rather than the F# type: `AriaRole` carries a `Custom of string` case
/// that emits its payload verbatim, so the wire position genuinely admits any
/// string and is not a closed set. `liveRegion` IS closed (`polite`/`assertive`/
/// `off`) but its wire strings are lower-case, and an `IdlEnum`'s case name IS its
/// wire string — F# DU cases cannot be lower-case, so modelling it as an enum needs
/// a case-name-to-wire-string mapping the generator does not have. Left as `TStr`
/// with the gap named rather than mis-modelled.
let private accessibilityRecord =
    { Name = "Accessibility"
      Fields =
        [ opt "describedBy" TStr
          opt "hidden" (bindingOf TBool)
          opt "label" (bindingOf TStr)
          opt "labelledBy" TStr
          opt "liveRegion" TStr
          opt "role" TStr ] }

// ─── Display kinds (flat `$type`-discriminated) ────────────────────────────

let displayKinds: IdlKind list =
    [ { Tag = "Heading"
        Category = "Display"
        Fields = [ req "level" TInt; req "text" TS; req "variant" (TEnum "HeadingVariant") ] }
      { Tag = "Badge"
        Category = "Display"
        Fields = [ req "label" TS; req "variant" (TEnum "BadgeVariant") ] }
      { Tag = "Markdown"
        Category = "Display"
        Fields = [ req "text" TS ] }
      { Tag = "Math"
        Category = "Display"
        Fields = [ req "source" TStr; req "display" (TEnum "MathDisplay") ] }
      { Tag = "Skeleton"
        Category = "Display"
        Fields = [ req "rows" TInt ] }
      { Tag = "List"
        Category = "Display"
        Fields = [ req "items" (TList TS); req "ordered" TBool ] }
      { Tag = "Image"
        Category = "Display"
        Fields =
          [ req "alt" TS
            req "src" (bindingOf TStr)
            req "variant" (TEnum "ImageVariant") ] }
      { Tag = "Link"
        Category = "Display"
        Fields =
          [ req "href" (bindingOf TStr)
            req "label" TS
            req "download" TBool
            opt "rel" TStr
            opt "target" TStr ] }
      { Tag = "Callout"
        Category = "Display"
        Fields =
          [ req "body" TS
            omit "dismissable" TBool (VBool false)
            omit "tone" (TEnum "ToneVariant") (VEnum "Default")
            opt "heading" TS
            opt "icon" icon ] }
      { Tag = "Progress"
        Category = "Display"
        Fields =
          [ req "fraction" (bindingOf TFloat)
            omit "indeterminate" TBool (VBool false)
            omit "tone" (TEnum "ToneVariant") (VEnum "Default")
            opt "label" TS
            opt "caveat" TS ] }
      { Tag = "Metric"
        Category = "Display"
        Fields =
          [ req "label" TS
            // Fuaran-UI 0.2.x renamed Metric's binding slot `source` → `value`.
            req "value" (bindingOf TFloat)
            omit "format" CF (VUnion("None", []))
            omit "tone" (TEnum "ToneVariant") (VEnum "Default")
            omit "weight" (TEnum "StyleWeight") (VEnum "Standard")
            omit "emphasis" (TEnum "Emphasis") (VEnum "Normal")
            opt "trend" (bindingOf TFloat)
            opt "trendFormat" CF
            opt "icon" icon
            opt "subtext" TS ] }
      { Tag = "LabelValueRow"
        Category = "Display"
        Fields =
          [ omit "emphasis" TBool (VBool false)
            omit "format" CF (VUnion("None", []))
            req "label" TS
            // Fuaran-UI 0.2.x renamed the binding slot `source` → `value`.
            req "value" (bindingOf TFloat)
            opt "help" TS ] }
      // `Fact` — a labelled TEXT fact (LabelValueRow's sibling: that one's `value`
      // is a numeric binding, this one's is a TextSource). `emphasis` is the
      // behavioural bool, emitted only when true; `tone` omits at Default.
      { Tag = "Fact"
        Category = "Display"
        Fields =
          [ omit "emphasis" TBool (VBool false)
            opt "help" TS
            opt "icon" icon
            req "label" TS
            omit "tone" (TEnum "ToneVariant") (VEnum "Default")
            req "value" TS ] }
      { Tag = "Sparkline"
        Category = "Display"
        // Fuaran-UI 0.2.x typed-Static: the source seq is a real int list on the wire.
        Fields = [ req "source" (bindingOf (TList TInt)) ] }
      { Tag = "CodeBlock"
        Category = "Display"
        Fields =
          [ req "code" TStr
            req "copyable" TBool
            req "highlightLines" (TList TInt)
            req "language" TStr
            req "lineNumbers" TBool ] }
      // Phase 679 — `Toast`. NOTE the polarity: `dismissable` omits when TRUE
      // (a toast defaults dismissable), the opposite of `Callout`'s, which omits
      // when false. Same field name, same type, inverted default.
      { Tag = "Toast"
        Category = "Display"
        Fields =
          [ omit "dismissable" TBool (VBool true)
            req "message" TS
            req "open" (bindingOf TBool)
            omit "tone" (TEnum "ToneVariant") (VEnum "Default") ] }
      // Phase 679 — `Drawing`. Its closure (Shape / DrawStyle / DrawPoint /
      // CurveCommand / ViewBox / TextAnchor) is declared above the meta kinds.
      { Tag = "Drawing"
        Category = "Display"
        Fields =
          [ opt "description" TS
            req "shapes" (TList(TUnion("Shape", [])))
            req "style" (TRecord "DrawStyle")
            opt "title" TS
            req "viewBox" (TRecord "ViewBox") ] } ]

// ─── Layout kinds (child-bearing; `children : Node list` recurses via TNode) ─
//
// The category that proves recursive nesting across families (a `Card` holding a
// `Metric` + a `LabelValueRow`). New shape classes vs Display: node-list children
// (`TList TNode`), `Binding<int>` / `Binding<bool>` controlled-state slots, a
// wire-encoded `Action` (`Modal.OnDismiss`), and closure-sentinel dispatch slots
// (`Tabs`/`Stepper` `onSelect` → `"<closure>"`). Some closures are *omitted*
// entirely (`Disclosure.OnToggle` has no wire key) — modelled by simply not
// declaring the field. `tabs-explicit-1` (TabHeader records + tabTags/activeTag
// overlays) rides the Input-family slice, where the non-discriminated record type
// is the dominant new class.
let layoutKinds: IdlKind list =
    [ // Fuaran-UI 0.2.0 Box unification: Dashboard / Card / Stack / GridLayout collapsed
      // into one `Box` kind carrying `role` (BoxRole) + `layout` (LayoutMode). The other
      // container kinds (SplitPanel / SummaryList / Disclosure / Modal / ScrollArea /
      // Tabs / Stepper) were NOT unified.
      { Tag = "Box"
        Category = "Layout"
        Fields =
          [ req "children" (TList TNode)
            opt "heading" TS
            req "layout" (TUnion("LayoutMode", []))
            req "role" (TEnum "BoxRole") ] }
      { Tag = "SplitPanel"
        Category = "Layout"
        Fields = [ req "children" (TList TNode); req "weight" TFloat ] }
      { Tag = "SummaryList"
        Category = "Layout"
        Fields = [ req "children" (TList TNode); opt "heading" TS ] }
      { Tag = "Disclosure"
        Category = "Layout"
        // Phase 671 step 2 — this comment used to read "OnToggle is a closure that
        // is NOT on the wire — no field declared", which was true before Phase 426
        // and false after: `onToggle` now rides as the `"<closure>"` sentinel when
        // present. The direct byte-diff caught the drift (`controls-closure`).
        Fields =
          [ req "children" (TList TNode)
            req "defaultOpen" TBool
            req "heading" TS
            opt "onToggle" (handlerOf "bool" "boolean")
            req "open" (bindingOf TBool) ] }
      { Tag = "Modal"
        Category = "Layout"
        // `onDismiss` optional since Fuaran-UI Phase 426: `Some action` encodes
        // exactly as before; `None` omits the key and arms the renderer's `Open`
        // write-back default. The IDL carried it Required until the Phase 692
        // gap-closure (`controls-declarative` omits it).
        Fields =
          [ req "children" (TList TNode)
            req "dismissable" TBool
            opt "onDismiss" (TUnion("Action", []))
            req "open" (bindingOf TBool)
            opt "heading" TS ] }
      { Tag = "ScrollArea"
        Category = "Layout"
        Fields =
          [ req "children" (TList TNode)
            req "orientation" (TEnum "ScrollOrientation")
            opt "maxHeight" TInt
            opt "maxWidth" TInt ] }
      { Tag = "Tabs"
        Category = "Layout"
        // onSelect is a closure that IS on the wire as the "<closure>" sentinel.
        // The tabHeaders / tabTags / activeTag overlays are optional (omitted in
        // tabs-1, present in tabs-explicit-1 — the TabHeader record slice).
        // Fuaran-UI 0.2.x dropped Tabs.orientation (no wire key).
        Fields =
          [ req "activeIndex" (bindingOf TInt)
            req "children" (TList TNode)
            opt "onSelect" (handlerOf "int" "number")
            // Phase 671 step 2 — also caught by the direct diff: present in
            // `controls-closure`, absent from the IDL, so it was silently dropped.
            opt "onSelectTag" (handlerOf "string" "string")
            opt "tabHeaders" (TList(TRecord "TabHeader"))
            opt "tabTags" (TList TStr)
            opt "activeTag" (bindingOf TStr) ] }
      { Tag = "Stepper"
        Category = "Layout"
        Fields =
          [ req "activeStep" (bindingOf TInt)
            req "children" (TList TNode)
            opt "onSelect" (handlerOf "int" "number") ] } ]

// ─── Input kinds (interactive; the richest Binding / Action surface) ────────
//
// New shape classes vs Display/Layout: the full `Action` union (`Button.onClick`
// — Chain / WriteToClipboard / Dispatch / Invoke / ReadFileBody), the recursive
// `Binding.Local` / `Binding.Format` / `Binding.Invoke` cases, the closure-heavy
// `FormFieldKind` / `FilterKind` unions, and — the headline — **non-discriminated
// record** fields (`FormField` / `FilterSpec` / `InvokeArg` / `TabHeader`, all
// `TRecord`). _(The old multiselect-1 / form-segmented deferral is closed: Phase
// 677 removed null from the wire — absence omits the key — so both round-trip.)_
let inputKinds: IdlKind list =
    [ { Tag = "Button"
        Category = "Input"
        Fields =
          [ req "label" (TUnion("TextSource", []))
            req "onClick" (TUnion("Action", []))
            req "variant" (TEnum "ButtonVariant")
            opt "icon" TStr
            opt "tooltip" (TUnion("TextSource", []))
            opt "disabled" (bindingOf TBool) ] }
      { Tag = "Select"
        Category = "Input"
        // Fuaran-UI 0.2.x typed-Static: source is a real SelectOption list, value a real
        // string; onChange is a closure sentinel; multiple omitted when false. (`values`
        // rides multiselect-1, deferred — a `Static None` renders JSON null.)
        Fields =
          [ req "label" (TUnion("TextSource", []))
            opt "onChange" (handlerOf "string option" "string | null")
            // Phase 671 step 2 — the multi-select handler, present in
            // `controls-closure` and absent from the IDL until the direct
            // byte-diff found it silently dropped.
            opt "onChangeMulti" (handlerOf "string list" "string[]")
            req "source" (bindingOf (TList(TRecord "SelectOption")))
            req "value" (bindingOf TStr)
            opt "placeholder" (TUnion("TextSource", []))
            opt "disabled" (bindingOf TBool)
            opt "multiple" TBool
            opt "values" (bindingOf (TList TStr)) ] }
      { Tag = "FileUpload"
        Category = "Input"
        Fields =
          [ req "accept" (TList TStr)
            req "label" (TUnion("TextSource", []))
            req "multiple" TBool
            opt "onSelect" (handlerOf "obj list" "unknown[]")
            opt "disabled" (bindingOf TBool) ] }
      { Tag = "Form"
        Category = "Input"
        Fields =
          [ req "fields" (TList(TRecord "FormField"))
            req "onSubmit" (TUnion("Action", []))
            req "submitLabel" (TUnion("TextSource", []))
            opt "disabled" (bindingOf TBool) ] }
      { Tag = "Filters"
        Category = "Input"
        Fields = [ req "items" (TList(TRecord "FilterSpec")) ] } ]

// ─── Visualisation kinds (data-bound; erased-row grid + chart/table/map) ────
//
// New shape classes vs Input: nested list-of-lists (`Table.rows : TList (TList
// TS)`), the erased `ColumnErased` record holding a `CellKindErased` union + a
// `ColumnWidth` union, and closure-projection fields (`rowKey` / column `value`).
// `DataGrid.source` / `Chart.source` / `Map.source` are opaque `Binding`s on the
// wire — except when they carry a `Binding.Transform`, whose `source` / `pipeline`
// are HOSTED slots rendered by Core's own codecs (`DataFrameCodec` / `ColumnCodec`)
// under the same `Canon` discipline (the Phase 692 gap-closure; the old
// grid-transform deferral is closed).
let visKinds: IdlKind list =
    [ { Tag = "DataGrid"
        Category = "Visualisation"
        // Fuaran-UI 0.2.x: `editable` omit-when-false, `rowKey` optional (absent on a
        // static grid), + `staticRows` (the retired `Table` decode-upgrades into a static
        // DataGrid carrying its header/row grid). `source` stays opaque.
        // Phase 425 — `rowKey` (closure) + `rowKeyField` (declarative) are
        // sibling optional slots, mirroring the column-level `value` / `field`.
        Fields =
          [ req "columns" (TList(TRecord "ColumnErased"))
            omit "editable" TBool (VBool false)
            opt "rowKey" (projOf "obj" "string" "(row: unknown) => string" "(fun _ -> \"\")")
            opt "rowKeyField" TStr
            req "source" (bindingOf TOpaque)
            opt "staticRows" (TRecord "StaticRows")
            opt "onRowClick" (handlerOf "obj" "unknown") ] }
      { Tag = "Chart"
        Category = "Visualisation"
        Fields =
          [ req "kind" (TEnum "ChartKind")
            req "source" (bindingOf TOpaque)
            req "stacked" TBool
            req "xField" TStr
            req "yFields" (TList TStr)
            opt "title" TS
            opt "onPointClick" (handlerOf "obj" "unknown") ] }
      { Tag = "Map"
        Category = "Visualisation"
        // Fuaran-UI 0.2.x typed-Static: the map source is a real MapMarker list.
        Fields =
          [ req "centreLatitude" TFloat
            req "centreLongitude" TFloat
            req "source" (bindingOf (TList(TRecord "MapMarker")))
            req "zoom" TInt
            opt "onMarkerClick" (handlerOf "MapMarker" "MapMarker") ] } ]

// ─── Meta kinds (the escape hatches + parameterised fragments) ──────────────
//
// These sit directly on `NodeKind` (no behavioural category). New shape classes:
// string-keyed *maps* (`TMap` — `Custom.props`, `FragmentRef.args`), node-bearing
// fields on non-layout kinds (`ErrorBoundary.child`/`fallback`, `FragmentDecl.body`,
// `SlotArg.tree`), and the `HoleDecl`/`Scalar`/`HoleValueSpace`/`FragmentArg`
// unions. `props` is `Map<string, JsonValue>` — empty in every corpus fixture, so
// its value-type is `TOpaque` (non-empty props with real JsonValue best-effort is
// a later refinement). Completing `Custom` + these kinds is what unblocks
// Phase 321 tasks 2 + 3 (the Custom allowlist + codegen-time sanitisation).
// ─── Phase 679: the `Drawing` sub-vocabulary ───────────────────────────────
//
// One kind, but the largest closure in the IDL: a 9-case RECURSIVE shape union
// (`Group` nests `Shape list`), an all-optional style record, a point record, a
// 5-case path-command union, a viewBox record and a text-anchor enum. Modelled
// together because a half-modelled `Shape` is worse than none — the drift would
// be silent (a dropped case) rather than a loud missing-kind error.

let private textAnchor =
    { Name = "TextAnchor"
      Cases = [ "Start"; "Middle"; "End" ] }

let private drawPoint =
    { Name = "DrawPoint"
      Fields = [ req "x" TFloat; req "y" TFloat ] }

let private viewBoxRecord =
    { Name = "ViewBox"
      Fields =
        [ req "height" TFloat
          req "minX" TFloat
          req "minY" TFloat
          req "width" TFloat ] }

/// Every field optional — an empty `{}` is a legitimate style (see `drawing-empty`).
let private drawStyle =
    { Name = "DrawStyle"
      Fields =
        [ opt "emphasis" (TEnum "Emphasis")
          opt "fill" (bindingOf TStr)
          opt "fontFamily" TStr
          opt "fontSize" TFloat
          opt "opacity" (bindingOf TFloat)
          opt "stroke" (bindingOf TStr)
          opt "strokeWidth" (bindingOf TFloat)
          opt "textAnchor" (TEnum "TextAnchor") ] }

let private curveCommand =
    { Name = "CurveCommand"
      Params = []
      Cases =
        // The destination point is `to` on every command — NOT the F# case-field
        // names (`point` / `endpoint`), which is what the first cut of this
        // modelled and why `drawing-1` failed to decode. Read the wire.
        [ { Tag = "MoveTo"
            Fields = [ req "to" (TRecord "DrawPoint") ] }
          { Tag = "LineTo"
            Fields = [ req "to" (TRecord "DrawPoint") ] }
          { Tag = "CubicTo"
            Fields =
              [ req "control1" (TRecord "DrawPoint")
                req "control2" (TRecord "DrawPoint")
                req "to" (TRecord "DrawPoint") ] }
          { Tag = "QuadraticTo"
            Fields = [ req "control" (TRecord "DrawPoint"); req "to" (TRecord "DrawPoint") ] }
          { Tag = "Close"; Fields = [] } ] }

/// Recursive: `Group` carries `Shape list`. Every case carries a `style`.
let private shape =
    { Name = "Shape"
      Params = []
      Cases =
        [ { Tag = "Group"
            Fields =
              [ req "children" (TList(TUnion("Shape", [])))
                req "style" (TRecord "DrawStyle") ] }
          { Tag = "Rectangle"
            Fields =
              [ opt "cornerRadius" TFloat
                req "height" TFloat
                req "style" (TRecord "DrawStyle")
                req "width" TFloat
                req "x" TFloat
                req "y" TFloat ] }
          { Tag = "Line"
            Fields =
              [ req "style" (TRecord "DrawStyle")
                req "x1" TFloat
                req "x2" TFloat
                req "y1" TFloat
                req "y2" TFloat ] }
          { Tag = "Polyline"
            Fields = [ req "points" (TList(TRecord "DrawPoint")); req "style" (TRecord "DrawStyle") ] }
          { Tag = "Polygon"
            Fields = [ req "points" (TList(TRecord "DrawPoint")); req "style" (TRecord "DrawStyle") ] }
          { Tag = "Curve"
            Fields =
              [ req "commands" (TList(TUnion("CurveCommand", [])))
                req "style" (TRecord "DrawStyle") ] }
          { Tag = "Circle"
            Fields =
              [ req "cx" TFloat
                req "cy" TFloat
                req "r" TFloat
                req "style" (TRecord "DrawStyle") ] }
          { Tag = "Ellipse"
            Fields =
              [ req "cx" TFloat
                req "cy" TFloat
                req "rx" TFloat
                req "ry" TFloat
                req "style" (TRecord "DrawStyle") ] }
          { Tag = "Label"
            Fields =
              [ req "style" (TRecord "DrawStyle")
                req "text" TS
                req "x" TFloat
                req "y" TFloat ] } ] }

/// Phase 679 — a `Switch` case: the match string plus the node it selects. The
/// tier holds this as a `(string * Node) tuple list`, which the IDL has no type
/// for; on the wire it is a two-field record, so that is what is modelled.
let private switchCase =
    { Name = "SwitchCase"
      Fields = [ req "child" TNode; req "match" TStr ] }

/// Phase 679 — `Mount`'s guest channel. `messageShape` rides only on `TwoWay`
/// in practice but is optional in the shape, not conditional on direction.
let private guestChannel =
    { Name = "GuestChannel"
      Fields = [ req "direction" (TEnum "ChannelDirection"); opt "messageShape" TStr ] }

let private channelDirection =
    { Name = "ChannelDirection"
      Cases = [ "OutOnly"; "TwoWay" ] }

let metaKinds: IdlKind list =
    [ { Tag = "Custom"
        Category = "Meta"
        Fields =
          [ req "moduleId" TStr
            req "componentId" TStr
            req "props" (TMap TOpaque)
            opt "contentHash" (TRecord "ContentHash")
            opt "exposedNodeIds" (TList TStr) ] }
      { Tag = "ErrorBoundary"
        Category = "Meta"
        Fields = [ req "child" TNode; req "fallback" TNode ] }
      { Tag = "FragmentDecl"
        Category = "Meta"
        // holes / effect are omitted for the degenerate fixed-body fragment.
        Fields =
          [ req "body" TNode
            req "name" TStr
            opt "holes" (TList(TUnion("HoleDecl", [])))
            opt "effect" (TRecord "EffectClass") ] }
      { Tag = "FragmentRef"
        Category = "Meta"
        // args omitted for the degenerate name-only ref.
        Fields = [ req "name" TStr; opt "args" (TMap(TUnion("FragmentArg", []))) ] }
      // Phase 679 — `Switch`: declarative branch selection on a state key.
      { Tag = "Switch"
        Category = "Meta"
        Fields =
          [ req "cases" (TList(TRecord "SwitchCase"))
            req "default" TNode
            req "stateKey" TStr ] }
      // Phase 679 — `Mount`: a guest fragment host. `inputs` is omitted when
      // empty; `onBubble` is the closure sentinel.
      { Tag = "Mount"
        Category = "Meta"
        Fields =
          [ req "capabilities" (TList TStr)
            req "channel" (TRecord "GuestChannel")
            opt "inputs" (TMap(TUnion("FragmentArg", [])))
            opt "onBubble" (handlerOf "obj" "unknown")
            req "scopeId" TStr ] } ]

/// The real-tier IDL as grown so far: the Display + Layout + Input + Visualisation
/// + meta families over the shared value-unions + enums + records + maps. Children
/// resolve within this one IDL, so any node can nest any kind.
let uiIdl: Idl =
    { Kinds = displayKinds @ layoutKinds @ inputKinds @ visKinds @ metaKinds
      Unions =
        [ textSource
          binding
          cellFormat
          action
          callResultTarget
          formatUnion
          localeSource
          localFlushTrigger
          layoutMode
          formFieldKind
          columnWidth
          cellKindErased
          holeValueSpace
          scalar
          holeDecl
          fragmentArg
          curveCommand
          shape ]
      Enums =
        [ headingVariant
          badgeVariant
          orientation
          boxRole
          mathDisplay
          imageVariant
          toneVariant
          styleWeight
          emphasis
          scrollOrientation
          buttonVariant
          fileReadEncoding
          dateVariant
          dateStyle
          relativeTimeUnit
          chartKind
          hashStrictness
          hostEffect
          determinismSource
          channelDirection
          textAnchor
          styleRole
          fontVoice
          motion ]
      Records =
        [ semanticStyleRecord
          stateBehaviourRecord
          accessibilityRecord
          switchCase
          guestChannel
          drawPoint
          viewBoxRecord
          drawStyle
          invokeArgRecord
          selectOptionRecord
          mapMarkerRecord
          staticRowsRecord
          formFieldRecord
          filterSpecRecord
          transformParamRecord
          rangePairRecord
          tabHeaderRecord
          columnErasedRecord
          buttonGroupItemRecord
          contentHashRecord
          effectClassRecord ]
      Defaults = []
      // Phase 690 — the node envelope, Ordinal-ordered like every other field list.
      //
      // All three are `Optional` here, where the hand-written tier stores `state` and
      // `style` as NON-option records and omits them when empty / all-default. Both
      // shapes produce identical wire — absent is absent — but they are different
      // AUTHORING types, and reconciling them is Phase 692's job, not a difference to
      // paper over. `Optional` is chosen because it is what the wire actually says
      // (§3.1: "omitted when empty"), and because an all-default `Some` is a shape the
      // encoder should never be handed rather than one it must silently absorb.
      NodeFields =
        [ opt "accessibility" (TRecord "Accessibility")
          // `WIRE_FORMAT.md` §9 — consumer-authored, deliberately NOT AI-visible, and
          // never emitted. They are on the node because the generated type has to be
          // able to hold everything the authoring type holds (Phase 694), not because
          // the wire has anything to say about them.
          hostOnly "extraAttributes" "Map<string, string> option" "None"
          hostOnly "motion" "Motion option" "None"
          opt "state" (TRecord "StateBehaviour")
          opt "style" (TRecord "SemanticStyle") ] }

/// Back-compat alias — the Display tests grew up against this name.
let uiDisplayIdl: Idl = uiIdl

// ─── Authored fixtures (must encode byte-identical to the live corpus) ──────

/// `TextSource.Literal`, sugared.
let lit (s: string) : IdlValue = VUnion("Literal", [ "text", VStr s ])

/// `Binding.Static` over a string / float / opaque value.
let private staticStr (s: string) = VUnion("Static", [ "value", VStr s ])
let private staticFloat (f: float) = VUnion("Static", [ "value", VFloat f ])
let private staticOpaque = VUnion("Static", [ "value", VOpaque ])

/// A `SelectOption` record — a `{label, value}` choice entry (Fuaran-UI 0.2.x typed-Static).
let private selectOption (label: string) (value: string) =
    VRecord [ "label", VStr label; "value", VStr value ]

let private heading1 =
    VNode(
        "heading-1",
        "Heading",
        [ "level", VInt 2
          "text", lit "Channel performance"
          "variant", VEnum "Standard" ]
    )

let private badge1 =
    VNode("badge-1", "Badge", [ "label", lit "Beta"; "variant", VEnum "Info" ])

let private markdown1 =
    VNode("markdown-1", "Markdown", [ "text", lit "Updated hourly." ])

let private math1 =
    VNode("math-1", "Math", [ "source", VStr "x^2 + y^2 = z^2"; "display", VEnum "Block" ])

let private skel1 = VNode("skel-1", "Skeleton", [ "rows", VInt 3 ])

let private list1 =
    VNode("list-1", "List", [ "items", VList [ lit "First"; lit "Second" ]; "ordered", VBool true ])

let private image1 =
    VNode(
        "image-1",
        "Image",
        [ "alt", lit "User avatar"
          "src", staticStr "/avatar.png"
          "variant", VEnum "Avatar" ]
    )

let private link1 =
    VNode(
        "link-1",
        "Link",
        [ "href", staticStr "/about"
          "label", lit "About us"
          "download", VBool false
          "rel", VStr "noopener"
          "target", VStr "_blank" ]
    )

let private callout1 =
    VNode(
        "callout-1",
        "Callout",
        [ "body", lit "Live data is delayed."
          "dismissable", VBool true
          "tone", VEnum "Warning"
          "heading", lit "Heads up"
          "icon", VStr "alert" ]
    )

let private progress1 =
    VNode(
        "progress-1",
        "Progress",
        [ "fraction", staticFloat 0.42
          "indeterminate", VBool false
          "tone", VEnum "Brand"
          "label", lit "Loading..." ]
    )

let private metric1 =
    VNode(
        "metric-1",
        "Metric",
        [ "label", lit "Revenue"
          "value", staticFloat 1234.5
          "format", VUnion("Currency", [ "code", VStr "GBP" ])
          "tone", VEnum "Brand"
          "weight", VEnum "Standard"
          "emphasis", VEnum "Normal"
          "trend", staticFloat 0.07
          "trendFormat", VUnion("Percent", [ "decimals", VInt 1 ])
          "icon", VStr "trending-up"
          "subtext", lit "vs last month" ]
    )

let private lvr1 =
    VNode(
        "lvr-1",
        "LabelValueRow",
        [ "emphasis", VBool true
          "format", VUnion("Number", [ "decimals", VInt 2 ])
          "label", lit "Total"
          "value", staticFloat 42.0
          "help", lit "Last 30 days" ]
    )

/// Fuaran-UI 0.2.x typed-Static: Sparkline's source seq is a real int list on the wire.
let private spark1 =
    VNode(
        "spark-1",
        "Sparkline",
        [ "source", VUnion("Static", [ "value", VList [ VInt 1; VInt 2; VInt 3; VInt 2; VInt 4 ] ]) ]
    )

let private code1 =
    VNode(
        "code-1",
        "CodeBlock",
        [ "code", VStr "let x = 1\nlet y = 2"
          "copyable", VBool true
          "highlightLines", VList [ VInt 1; VInt 2 ]
          "language", VStr "fsharp"
          "lineNumbers", VBool true ]
    )

/// Fixture name → authored value. Each must encode byte-identical to
/// `wire-format-fixtures/nodes/<name>.json`.
/// `fact-1` — the labelled-text sibling of `lvr-1`: `emphasis` true (so it emits),
/// `tone` non-Default (so it emits), plus both optionals present.
let private fact1 =
    VNode(
        "fact-1",
        "Fact",
        [ "emphasis", VBool true
          "help", lit "Primary insured"
          "icon", VStr "user"
          "label", lit "Patient"
          "tone", VEnum "Brand"
          "value", lit "Alice Smith" ]
    )

/// `toast-1` — `open` a Static bool, `tone` non-Default, `dismissable` at its
/// (true) default so the omission path is exercised.
let private toast1 =
    VNode(
        "toast-1",
        "Toast",
        [ "message", lit "Saved"
          "open", VUnion("Static", [ "value", VBool true ])
          "tone", VEnum "Success" ]
    )

/// `drawing-empty` — no shapes and an EMPTY style record. Both matter: an empty
/// `{}` style is a legitimate value (every `DrawStyle` field is optional), not
/// absence, and an empty `shapes` array is not absence either.
let private drawingEmpty =
    VNode(
        "drawing-empty",
        "Drawing",
        [ "shapes", VList []
          "style", VRecord []
          "viewBox",
          VRecord
              [ "height", VFloat 100.0
                "minX", VFloat 0.0
                "minY", VFloat 0.0
                "width", VFloat 100.0 ] ]
    )

let displayCases: (string * IdlValue) list =
    [ "heading-1", heading1
      "badge-1", badge1
      "markdown-1", markdown1
      "math-1", math1
      "skel-1", skel1
      "list-1", list1
      "image-1", image1
      "link-1", link1
      "callout-1", callout1
      "progress-1", progress1
      "metric-1", metric1
      "lvr-1", lvr1
      "spark-1", spark1
      "code-1", code1
      "fact-1", fact1
      "toast-1", toast1
      "drawing-empty", drawingEmpty ]

/// Vendored canonical wire bytes for each Display fixture — a self-contained
/// snapshot (the gate never goes vacuous when the corpus is not checked out),
/// drift-guarded against the live `wire-format-fixtures/nodes/` corpus.
let displayExpected: (string * string) list = Snapshots.loadPaired "ui" displayCases

let private staticBool (b: bool) = VUnion("Static", [ "value", VBool b ])
let private staticInt (i: int) = VUnion("Static", [ "value", VInt i ])
let private chain0 = VUnion("Chain", [ "ops", VList [] ])

// ─── Box layout modes (Fuaran-UI 0.2.0 unification) ─────────────────────────
let private layoutAuto = VUnion("Auto", [])

let private layoutFlexV =
    VUnion("Flex", [ "direction", VEnum "Vertical"; "wrap", VBool false ])

let private layoutGrid (cols: int) (tpl: string option) =
    VUnion(
        "Grid",
        [ "cols", VInt cols ]
        @ (match tpl with
           | Some t -> [ "templateColumns", VStr t ]
           | None -> [])
    )

/// A unified `Box` node — `role` (Dashboard/Card/Group) + `layout` (Auto/Flex/Grid).
/// Fields authored in IDL-declaration order (children / heading / layout / role) so a
/// decode round-trip reconstructs the authored value structurally.
let private box (id: string) (role: string) (layout: IdlValue) (heading: string option) (children: IdlValue list) =
    let headF =
        match heading with
        | Some h -> [ "heading", lit h ]
        | None -> []

    VNode(
        id,
        "Box",
        [ "children", VList children ]
        @ headF
        @ [ "layout", layout; "role", VEnum role ]
    )

let private dashEmpty = box "dash-empty" "Dashboard" layoutAuto None []

let private stackNode =
    box "stack-1" "Group" layoutFlexV None [ metric1; markdown1 ]

let private gridOf (id: string) (cols: int) (tpl: string option) =
    box id "Group" (layoutGrid cols tpl) None [ metric1 ]

let private split1 =
    VNode("split-1", "SplitPanel", [ "children", VList [ metric1; markdown1 ]; "weight", VFloat 0.6 ])

let private card1 = box "card-1" "Card" layoutFlexV (Some "Insights") [ metric1 ]

let private summary1 =
    VNode("summary-1", "SummaryList", [ "children", VList [ lvr1 ]; "heading", lit "Stats" ])

let private discl1 =
    VNode(
        "discl-1",
        "Disclosure",
        [ "children", VList [ markdown1 ]
          "defaultOpen", VBool true
          "heading", lit "Additional entitlements"
          "open", staticBool false ]
    )

let private modal1 =
    VNode(
        "modal-1",
        "Modal",
        [ "children", VList [ markdown1 ]
          "dismissable", VBool true
          "onDismiss", chain0
          "open", staticBool false
          "heading", lit "Confirm" ]
    )

let private scroll1 =
    VNode(
        "scroll-1",
        "ScrollArea",
        [ "children", VList [ markdown1 ]
          "orientation", VEnum "Vertical"
          "maxHeight", VInt 320 ]
    )

let private tabs1 =
    VNode(
        "tabs-1",
        "Tabs",
        [ "activeIndex", staticInt 0
          "children", VList [ metric1 ]
          "onSelect", VClosure ]
    )

let private step1 =
    VNode(
        "step-1",
        "Stepper",
        [ "activeStep", staticInt 1
          "children", VList [ markdown1; markdown1 ]
          "onSelect", VClosure ]
    )

let private compositeCard =
    box "composite-card" "Card" layoutFlexV (Some "Composite") [ metric1; lvr1 ]

let private compositeRoot =
    box "composite-root" "Dashboard" layoutAuto None [ compositeCard; stackNode ]

/// Layout fixture name → authored value (each byte-identical to its corpus file).
let layoutCases: (string * IdlValue) list =
    [ "dash-empty", dashEmpty
      "stack-1", stackNode
      "glayout-1", gridOf "glayout-1" 12 None
      "glayout-tpl-ratio", gridOf "glayout-tpl-ratio" 2 (Some "1fr 2fr")
      "glayout-tpl-autofit", gridOf "glayout-tpl-autofit" 1 (Some "repeat(auto-fit, minmax(150px, 1fr))")
      "glayout-tpl-fixed", gridOf "glayout-tpl-fixed" 4 (Some "100px repeat(3, minmax(30px, 1fr))")
      "split-1", split1
      "card-1", card1
      "summary-1", summary1
      "discl-1", discl1
      "modal-1", modal1
      "scroll-1", scroll1
      "tabs-1", tabs1
      "step-1", step1
      "composite-root", compositeRoot ]

/// Vendored canonical wire bytes for each Layout fixture (drift-guarded vs live).
let layoutExpected: (string * string) list = Snapshots.loadPaired "ui" layoutCases

let private invokeArg (addr: string) (value: string) =
    VRecord [ "addr", VStr addr; "value", VStr value ]

let private stateBoolF (key: string) =
    VUnion("State", [ "defaultValue", VBool false; "key", VStr key ])

let private btn1 =
    VNode(
        "btn-1",
        "Button",
        [ "disabled", stateBoolF "loading"
          "icon", VStr "refresh"
          "label", lit "Refresh"
          "onClick", chain0
          "variant", VEnum "Primary" ]
    )

let private btnCopyLink =
    VNode(
        "btn-copy-link",
        "Button",
        [ "label", lit "Copy share link"
          "onClick",
          VUnion(
              "Chain",
              [ "ops",
                VList
                    [ VUnion("WriteToClipboard", [ "text", VStr "https://example.com/share/abc123" ])
                      VUnion("Dispatch", []) ] ]
          )
          "variant", VEnum "Secondary" ]
    )

let private btnInvoke =
    VNode(
        "btn-invoke",
        "Button",
        [ "label", lit "Run model"
          "onClick", VUnion("Invoke", [ "args", VList [ invokeArg "rows" "all" ]; "capabilityId", VStr "model.score" ])
          "variant", VEnum "Primary" ]
    )

let private btnReadWorkbook =
    VNode(
        "btn-read-workbook",
        "Button",
        [ "label", lit "Load workbook"
          "onClick",
          VUnion(
              "ReadFileBody",
              [ "encoding", VEnum "Base64"
                "fileRef", VStr "workbook-upload:0"
                "onRead", VClosure ]
          )
          "variant", VEnum "Primary" ]
    )

let private selectNode =
    VNode(
        "select-1",
        "Select",
        [ "disabled", stateBoolF "selectBusy"
          "label", lit "Region"
          "onChange", VClosure
          "placeholder", lit "Choose one"
          "source", VUnion("Static", [ "value", VList [ selectOption "UK" "uk" ] ])
          "value", staticStr "uk" ]
    )

let private uploadNode =
    VNode(
        "upload-1",
        "FileUpload",
        [ "accept", VList [ VStr ".csv"; VStr "text/csv" ]
          "disabled", stateBoolF "uploadBusy"
          "label", lit "Upload CSV"
          "multiple", VBool false
          "onSelect", VClosure ]
    )

let private filterSpec (name: string) (label: string) (kind: IdlValue) =
    VRecord [ "kind", kind; "label", lit label; "name", VStr name ]

let private filters1 =
    VNode(
        "filters-1",
        "Filters",
        [ "items",
          VList
              [ filterSpec "q" "Search" (VUnion("Text", [ "onChange", VClosure; "value", staticStr "" ]))
                filterSpec
                    "tier"
                    "Tier"
                    (VUnion(
                        "Choice",
                        [ "onChange", VClosure
                          "options", VUnion("Static", [ "value", VList [ selectOption "All" "all" ] ])
                          "value", staticStr "all" ]
                    )) ] ]
    )

let private filtersSegmented =
    VNode(
        "filters-segmented",
        "Filters",
        [ "items",
          VList
              [ filterSpec
                    "view"
                    "View"
                    (VUnion(
                        "SegmentedChoice",
                        [ "onChange", VClosure
                          "options",
                          VUnion(
                              "Static",
                              [ "value", VList [ selectOption "Table" "table"; selectOption "Chart" "chart" ] ]
                          )
                          "orientation", VEnum "Horizontal"
                          "value", staticStr "table" ]
                    )) ] ]
    )

let private formField (id: string) (label: string) (required: bool) (help: string option) (kind: IdlValue) =
    let baseFields =
        [ "id", VStr id; "kind", kind; "label", lit label; "required", VBool required ]

    let helpField =
        match help with
        | Some h -> [ "help", lit h ]
        | None -> []

    VRecord(baseFields @ helpField)

let private form1 =
    VNode(
        "form-1",
        "Form",
        [ "disabled", stateBoolF "formBusy"
          "fields",
          VList
              [ formField
                    "name"
                    "Name"
                    true
                    (Some "Full legal name")
                    (VUnion("Text", [ "onChange", VClosure; "value", staticStr "" ]))
                formField "age" "Age" false None (VUnion("Number", [ "onChange", VClosure; "value", staticFloat 0.0 ]))
                formField
                    "agree"
                    "I agree"
                    true
                    None
                    (VUnion("Checkbox", [ "onToggle", VClosure; "value", staticBool false ]))
                formField
                    "tier"
                    "Tier"
                    false
                    None
                    (VUnion(
                        "Choice",
                        [ "onChange", VClosure
                          "options",
                          VUnion(
                              "Static",
                              [ "value", VList [ selectOption "Basic" "basic"; selectOption "Pro" "pro" ] ]
                          )
                          "value", staticStr "basic" ]
                    ))
                formField
                    "notes"
                    "Notes"
                    false
                    None
                    (VUnion("TextArea", [ "onChange", VClosure; "rows", VInt 5; "value", staticStr "" ])) ]
          "onSubmit", chain0
          "submitLabel", lit "Save" ]
    )

let private form1Ranged =
    VNode(
        "form-ranged",
        "Form",
        [ "fields",
          VList
              [ formField
                    "year"
                    "Year"
                    true
                    None
                    (VUnion(
                        "RangedNumber",
                        [ "onChange", VClosure
                          "value", staticFloat 2024.0
                          "min", VFloat 1979.0
                          "max", VFloat 2028.0
                          "step", VFloat 1.0 ]
                    ))
                formField
                    "years"
                    "Years contributed"
                    false
                    None
                    (VUnion("RangedNumber", [ "onChange", VClosure; "value", staticFloat 10.0; "min", VFloat 0.0 ]))
                formField
                    "amount"
                    "Amount"
                    false
                    None
                    (VUnion("RangedNumber", [ "onChange", VClosure; "value", staticFloat 100.0 ])) ]
          "onSubmit", chain0
          "submitLabel", lit "Save" ]
    )

let private formDate =
    VNode(
        "form-date",
        "Form",
        [ "fields",
          VList
              [ formField
                    "checkIn"
                    "Check in"
                    true
                    None
                    (VUnion(
                        "Date",
                        [ "onChange", VClosure
                          "value", staticStr "2026-01-15"
                          "variant", VEnum "Date"
                          "min", VStr "2026-01-01"
                          "max", VStr "2026-12-31" ]
                    ))
                formField
                    "alarm"
                    "Alarm"
                    false
                    None
                    (VUnion(
                        "Date",
                        [ "onChange", VClosure
                          "value", staticStr "08:30"
                          "variant", VEnum "Time"
                          "step", VFloat 60.0 ]
                    ))
                formField
                    "meeting"
                    "Meeting"
                    false
                    None
                    (VUnion(
                        "Date",
                        [ "onChange", VClosure
                          "value", staticStr "2026-03-01T14:00"
                          "variant", VEnum "DateTime" ]
                    )) ]
          "onSubmit", chain0
          "submitLabel", lit "Book" ]
    )

let private localBinding (flushOn: IdlValue) (initialFrom: IdlValue) =
    VUnion(
        "Local",
        [ "flushOn", flushOn
          "format", VClosure
          "initialFrom", initialFrom
          "onCommit", VClosure
          "parse", VClosure ]
    )

let private formLocal1 =
    VNode(
        "form-local-1",
        "Form",
        [ "fields",
          VList
              [ formField
                    "salary-input"
                    "Salary"
                    false
                    None
                    (VUnion(
                        "Text",
                        [ "onChange", VClosure
                          "value",
                          localBinding
                              (VUnion("OnBlur", []))
                              (VUnion("State", [ "defaultValue", VStr ""; "key", VStr "salary" ])) ]
                    )) ]
          "onSubmit", chain0
          "submitLabel", lit "Save" ]
    )

let private formLocalDebounce =
    VNode(
        "form-local-debounce",
        "Form",
        [ "fields",
          VList
              [ formField
                    "email-input"
                    "Email"
                    true
                    None
                    (VUnion(
                        "Text",
                        [ "onChange", VClosure
                          "value",
                          localBinding
                              (VUnion("OnDebounce", [ "milliseconds", VInt 250 ]))
                              (staticStr "draft@example.com") ]
                    )) ]
          "onSubmit", chain0
          "submitLabel", lit "Save" ]
    )

let private fmtMarkdown (id: string) (format: IdlValue) (locale: IdlValue) (source: IdlValue) =
    VNode(
        id,
        "Markdown",
        [ "text",
          VUnion("Bound", [ "binding", VUnion("Format", [ "format", format; "locale", locale; "source", source ]) ]) ]
    )

let private explicitLocale (tag: string) = VUnion("Explicit", [ "tag", VStr tag ])

let private formatBindings =
    box
        "format-bindings"
        "Group"
        layoutFlexV
        None
        [ fmtMarkdown
              "fmt-number"
              (VUnion("Number", [ "decimals", VInt 2 ]))
              (explicitLocale "en-US")
              (staticFloat 1234.5)
          fmtMarkdown
              "fmt-currency"
              (VUnion("Currency", [ "isoCode", VStr "GBP" ]))
              (explicitLocale "en-GB")
              (staticFloat 1234.5)
          fmtMarkdown "fmt-percent" (VUnion("Percent", [])) (VUnion("Ambient", [])) (staticFloat 0.42)
          fmtMarkdown
              "fmt-date"
              (VUnion("Date", [ "dateStyle", VEnum "Medium" ]))
              (explicitLocale "fr-FR")
              (staticFloat 1700000000.0)
          fmtMarkdown
              "fmt-relative"
              (VUnion("RelativeTime", [ "unit", VEnum "Day" ]))
              (explicitLocale "en-US")
              (staticFloat -3.0) ]

let private tabsExplicit =
    VNode(
        "tabs-explicit-1",
        "Tabs",
        [ "activeIndex", staticInt 1
          "activeTag", staticStr "overview"
          "children", VList [ markdown1; spark1 ]
          "onSelect", VClosure
          "tabHeaders",
          VList
              [ VRecord [ "label", lit "Overview"; "icon", VStr "overview-glyph" ]
                VRecord [ "label", lit "Detail"; "disabled", staticBool false ] ]
          "tabTags", VList [ VStr "overview"; VStr "detail" ] ]
    )

let private metricInvoke =
    VNode(
        "metric-invoke",
        "Metric",
        [ "label", lit "Revenue"
          "value",
          VUnion(
              "Invoke",
              [ "args", VList [ invokeArg "horizon" "12"; invokeArg "scenario" "base" ]
                "capabilityId", VStr "forecast.revenue" ]
          )
          "format", VUnion("Currency", [ "code", VStr "GBP" ])
          "tone", VEnum "Brand"
          "weight", VEnum "Standard"
          "emphasis", VEnum "Normal"
          "icon", VStr "trending-up"
          "subtext", lit "vs last month" ]
    )

/// Input fixture name -> authored value. (multiselect-1 + form-segmented deferred
/// -- a Binding.Static None renders JSON null, absent from the JVal model.)
let inputCases: (string * IdlValue) list =
    [ "btn-1", btn1
      "btn-copy-link", btnCopyLink
      "btn-invoke", btnInvoke
      "btn-read-workbook", btnReadWorkbook
      "select-1", selectNode
      "upload-1", uploadNode
      "filters-1", filters1
      "filters-segmented", filtersSegmented
      "form-1", form1
      "form-ranged", form1Ranged
      "form-date", formDate
      "form-local-1", formLocal1
      "form-local-debounce", formLocalDebounce
      "format-bindings", formatBindings
      "tabs-explicit-1", tabsExplicit
      "metric-invoke", metricInvoke ]

/// Vendored canonical wire bytes for each Input fixture (drift-guarded vs live).
let inputExpected: (string * string) list = Snapshots.loadPaired "ui" inputCases

let private gridNode =
    VNode(
        "grid-1",
        "DataGrid",
        [ "columns",
          VList
              [ VRecord
                    [ "format", VUnion("None", [])
                      "kind", VUnion("Text", [])
                      "label", VStr "Channel"
                      "value", VClosure
                      "width", VUnion("Auto", []) ] ]
          "editable", VBool false
          "rowKey", VClosure
          "source", staticOpaque ]
    )

let private chartNode =
    VNode(
        "chart-1",
        "Chart",
        [ "kind", VEnum "Line"
          "source", staticOpaque
          "stacked", VBool true
          "title", lit "Channel mix"
          "xField", VStr "month"
          "yFields", VList [ VStr "revenue"; VStr "cost" ] ]
    )

/// Fuaran-UI Phase 393: the legacy `Table` retired upstream and decode-upgrades to a
/// static `DataGrid` carrying its header/row grid in `staticRows` (bare strings).
let private tableNode =
    VNode(
        "table-1",
        "DataGrid",
        [ "columns", VList []
          "source", staticOpaque
          "staticRows",
          VRecord
              [ "headers", VList [ VStr "Term"; VStr "Definition" ]
                "rows",
                VList
                    [ VList [ VStr "MVU"; VStr "Model-View-Update" ]
                      VList [ VStr "DSL"; VStr "Domain-specific language" ] ] ] ]
    )

let private mapNode =
    VNode(
        "map-1",
        "Map",
        [ "centreLatitude", VFloat 51.5
          "centreLongitude", VFloat -0.12
          "source",
          VUnion(
              "Static",
              [ "value",
                VList [ VRecord [ "label", VStr "London"; "latitude", VFloat 51.5; "longitude", VFloat -0.12 ] ] ]
          )
          "zoom", VInt 6 ]
    )

/// Visualisation fixture name -> authored value. (grid-transform deferred -- its
/// Binding.Transform embeds a Fuaran.Core.DataFrame pipeline rendered by Core's
/// own codecs, a separate wire surface from the UI structural layer.)
let visCases: (string * IdlValue) list =
    [ "grid-1", gridNode
      "chart-1", chartNode
      "table-1", tableNode
      "map-1", mapNode ]

/// Vendored canonical wire bytes for each Visualisation fixture (drift-guarded vs live).
let visExpected: (string * string) list = Snapshots.loadPaired "ui" visCases

let private custom1 =
    VNode(
        "custom-1",
        "Custom",
        [ "moduleId", VStr "analytics"
          "componentId", VStr "trend-card"
          "props", VMap [] ]
    )

let private contentHash (hash: string) (strictness: string) =
    VRecord
        [ "algorithm", VStr "SHA256"
          "hash", VStr hash
          "strictness", VEnum strictness ]

let private customBounded1 =
    VNode(
        "custom-bounded-1",
        "Custom",
        [ "moduleId", VStr "deal-flow"
          "componentId", VStr "QualityRing"
          "props", VMap []
          "contentHash", contentHash "abc123def456" "StrictReplay"
          "exposedNodeIds", VList [ VStr "quality-ring-segment-1"; VStr "quality-ring-segment-2" ] ]
    )

let private customBoundedAdvisory =
    VNode(
        "custom-bounded-advisory",
        "Custom",
        [ "moduleId", VStr "deal-flow"
          "componentId", VStr "TrendCard"
          "props", VMap []
          "contentHash", contentHash "fedcba654321" "AdvisoryWarning" ]
    )

let private boundaryChild =
    VNode("boundary-child", "Markdown", [ "text", lit "Child body" ])

let private boundaryFallback =
    VNode(
        "boundary-fallback",
        "Callout",
        [ "body", lit "Fallback rendered"
          "dismissable", VBool false
          "tone", VEnum "Warning"
          "heading", lit "Couldn't render" ]
    )

let private boundary1 =
    VNode("boundary-1", "ErrorBoundary", [ "child", boundaryChild; "fallback", boundaryFallback ])

let private fragDecl1 =
    VNode(
        "frag-decl-1",
        "FragmentDecl",
        [ "body", VNode("frag-body", "Markdown", [ "text", lit "Template body" ])
          "name", VStr "card-template" ]
    )

let private fragDeclParam =
    VNode(
        "frag-decl-param",
        "FragmentDecl",
        [ "body", VNode("param-body", "Markdown", [ "text", lit "Parameterised body" ])
          "effect", VRecord [ "determinism", VEnum "Clock"; "hostEffect", VEnum "ReadsHost" ]
          "holes",
          VList
              [ VUnion(
                    "Value",
                    [ "default", VUnion("Str", [ "value", VStr "Untitled" ])
                      "name", VStr "title"
                      "space", VUnion("StringLen", [ "maxLen", VInt 40; "minLen", VInt 1 ]) ]
                )
                VUnion(
                    "Value",
                    [ "name", VStr "count"
                      "space", VUnion("IntRange", [ "max", VInt 100; "min", VInt 0 ]) ]
                )
                VUnion("Slot", [ "kindConstraint", VStr "Display"; "name", VStr "content" ])
                VUnion(
                    "Repeat",
                    [ "countSpace", VUnion("IntRange", [ "max", VInt 12; "min", VInt 1 ])
                      "name", VStr "rows" ]
                ) ]
          "name", VStr "stat-card" ]
    )

let private fragRef1 =
    VNode("frag-ref-1", "FragmentRef", [ "name", VStr "card-template" ])

let private fragRefArgs =
    VNode(
        "frag-ref-args",
        "FragmentRef",
        [ "args",
          VMap
              [ "content", VUnion("SlotArg", [ "tree", VNode("slot-tree", "Markdown", [ "text", lit "Bound slot" ]) ])
                "count", VUnion("Int", [ "value", VInt 7 ]) ]
          "name", VStr "stat-card" ]
    )

/// Meta fixture name -> authored value.
/// `switch-1` — two cases plus a default, each carrying a real child node.
let private switch1 =
    let md id text =
        VNode(id, "Markdown", [ "text", lit text ])

    VNode(
        "switch-1",
        "Switch",
        [ "cases",
          VList
              [ VRecord [ "child", md "switch-details" "Details view"; "match", VStr "details" ]
                VRecord [ "child", md "switch-summary" "Summary view"; "match", VStr "summary" ] ]
          "default",
          VNode(
              "switch-default",
              "Callout",
              [ "body", lit "No view selected"
                "heading", lit "Pick a view"
                "tone", VEnum "Info" ]
          )
          "stateKey", VStr "view" ]
    )

/// `mount-1` — the minimal guest: no inputs (omitted), OutOnly channel.
let private mount1 =
    VNode(
        "mount-1",
        "Mount",
        [ "capabilities", VList []
          "channel", VRecord [ "direction", VEnum "OutOnly" ]
          "onBubble", VClosure
          "scopeId", VStr "guest-sidebar" ]
    )

let metaCases: (string * IdlValue) list =
    [ "custom-1", custom1
      "custom-bounded-1", customBounded1
      "custom-bounded-advisory", customBoundedAdvisory
      "boundary-1", boundary1
      "frag-decl-1", fragDecl1
      "frag-decl-param", fragDeclParam
      "frag-ref-1", fragRef1
      "frag-ref-args", fragRefArgs
      "switch-1", switch1
      "mount-1", mount1 ]

/// Vendored canonical wire bytes for each meta fixture (drift-guarded vs live).
let metaExpected: (string * string) list = Snapshots.loadPaired "ui" metaCases
