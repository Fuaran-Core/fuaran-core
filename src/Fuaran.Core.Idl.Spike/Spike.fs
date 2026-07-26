module Fuaran.Core.Idl.Spike.Fixtures

open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Phase 316 spike + Phase 317 increment 1 (generics). The mini UI IDL now covers
// 8 kinds and a *parameterised* value-union `Binding<'T>` — one definition that,
// instantiated at `float` (Metric.source) and `bool` (Button.disabled), produces
// the right wire for both. Kinds: a layout (Card / Stack, children + nesting), a
// display family (Heading / Badge / Metric / Markdown / Divider), an input with
// an action (Button → Chain). Value-unions: TextSource / Binding<'T> / Format /
// Action.
// ---------------------------------------------------------------------------

/// The mini UI IDL — the canonical source the codec is driven from.
let miniIdl: Idl =
    { Enums =
        [ { Name = "HeadingVariant"
            Cases = [ "Standard"; "Subtle"; "Display" ] }
          { Name = "BadgeVariant"
            Cases = [ "Info"; "Success"; "Warning"; "Critical"; "Neutral" ] }
          { Name = "ButtonVariant"
            Cases = [ "Primary"; "Secondary"; "Ghost"; "Danger" ] }
          { Name = "Emphasis"
            Cases = [ "Normal"; "Strong"; "Subtle" ] }
          { Name = "ToneVariant"
            Cases = [ "Default"; "Brand"; "Positive"; "Caution"; "Critical" ] }
          { Name = "StyleWeight"
            Cases = [ "Standard"; "Light"; "Heavy" ] }
          { Name = "Orientation"
            Cases = [ "Horizontal"; "Vertical" ] }
          { Name = "BoxRole"
            Cases = [ "Dashboard"; "Card"; "Group" ] } ]
      Unions =
        [ { Name = "TextSource"
            Params = []
            Cases =
              [ { Tag = "Literal"
                  Fields =
                    [ { Name = "text"
                        Type = TStr
                        Opt = Required } ] } ] }
          // The parameterised union — `Binding<'T>`; `'T` flows into the case fields.
          { Name = "Binding"
            Params = [ "T" ]
            Cases =
              [ { Tag = "Static"
                  Fields =
                    [ { Name = "value"
                        Type = TVar "T"
                        Opt = Required } ] }
                { Tag = "State"
                  Fields =
                    [ { Name = "defaultValue"
                        Type = TVar "T"
                        Opt = Required }
                      { Name = "key"
                        Type = TStr
                        Opt = Required } ] } ] }
          { Name = "Format"
            Params = []
            Cases =
              [ { Tag = "Currency"
                  Fields =
                    [ { Name = "code"
                        Type = TStr
                        Opt = Required } ] }
                { Tag = "Percent"
                  Fields =
                    [ { Name = "decimals"
                        Type = TInt
                        Opt = Required } ] } ] }
          { Name = "Action"
            Params = []
            Cases =
              [ { Tag = "Chain"
                  Fields =
                    [ { Name = "ops"
                        Type = TList(TUnion("Action", []))
                        Opt = Required } ] }
                // Phase 676 — a `TJson` case, present so the GENERATIVE cross-host
                // test actually exercises the JSON passthrough. Without it the spike
                // vocabulary has no `TJson` anywhere and both new legs (F# `id` /
                // `dJson`, TS `encJson`) would ship unverified across hosts. No fixed
                // fixture uses it, so the vendored snapshots are unchanged.
                { Tag = "Notify"
                  Fields =
                    [ { Name = "channel"
                        Type = TStr
                        Opt = Required }
                      { Name = "payload"
                        Type = TJson
                        Opt = Required } ] } ] }
          // Fuaran-UI 0.2.0 Box unification: Dashboard/Card/Stack/GridLayout → one `Box`
          // kind with `role` + a `layout` mode (Auto / Flex{direction,wrap} / Grid{cols}).
          { Name = "LayoutMode"
            Params = []
            Cases =
              [ { Tag = "Auto"; Fields = [] }
                { Tag = "Flex"
                  Fields =
                    [ { Name = "direction"
                        Type = TEnum "Orientation"
                        Opt = Required }
                      { Name = "wrap"
                        Type = TBool
                        Opt = Required } ] }
                { Tag = "Grid"
                  Fields =
                    [ { Name = "cols"
                        Type = TInt
                        Opt = Required }
                      { Name = "templateColumns"
                        Type = TStr
                        Opt = Optional } ] } ] } ]
      Kinds =
        [ { Tag = "Heading"
            Category = "Display"
            Fields =
              [ { Name = "level"
                  Type = TInt
                  Opt = Required }
                { Name = "text"
                  Type = TUnion("TextSource", [])
                  Opt = Required }
                { Name = "variant"
                  Type = TEnum "HeadingVariant"
                  Opt = Required } ] }
          { Tag = "Badge"
            Category = "Display"
            Fields =
              [ { Name = "label"
                  Type = TUnion("TextSource", [])
                  Opt = Required }
                { Name = "variant"
                  Type = TEnum "BadgeVariant"
                  Opt = Required } ] }
          { Tag = "Button"
            Category = "Input"
            Fields =
              [ { Name = "disabled"
                  Type = TUnion("Binding", [ TBool ])
                  Opt = Optional }
                { Name = "icon"
                  Type = TStr
                  Opt = Optional }
                { Name = "label"
                  Type = TUnion("TextSource", [])
                  Opt = Required }
                { Name = "onClick"
                  Type = TUnion("Action", [])
                  Opt = Required }
                { Name = "variant"
                  Type = TEnum "ButtonVariant"
                  Opt = Required } ] }
          { Tag = "Metric"
            Category = "Display"
            Fields =
              [ { Name = "emphasis"
                  Type = TEnum "Emphasis"
                  Opt = OmitDefault(VEnum "Normal") }
                { Name = "format"
                  Type = TUnion("Format", [])
                  Opt = Required }
                { Name = "icon"
                  Type = TStr
                  Opt = Optional }
                { Name = "label"
                  Type = TUnion("TextSource", [])
                  Opt = Required }
                { Name = "subtext"
                  Type = TUnion("TextSource", [])
                  Opt = Optional }
                { Name = "tone"
                  Type = TEnum "ToneVariant"
                  Opt = OmitDefault(VEnum "Default") }
                { Name = "trend"
                  Type = TUnion("Binding", [ TFloat ])
                  Opt = Optional }
                { Name = "trendFormat"
                  Type = TUnion("Format", [])
                  Opt = Optional }
                // Fuaran-UI 0.2.x renamed Metric's binding slot `source` → `value`. Declared in
                // Ordinal key order (the TS backend emits in author order — no key sort).
                { Name = "value"
                  Type = TUnion("Binding", [ TFloat ])
                  Opt = Required }
                { Name = "weight"
                  Type = TEnum "StyleWeight"
                  Opt = OmitDefault(VEnum "Standard") } ] }
          { Tag = "Markdown"
            Category = "Display"
            Fields =
              [ { Name = "text"
                  Type = TUnion("TextSource", [])
                  Opt = Required } ] }
          // The unified `Box` container (was `Card` / `Stack` / `GridLayout` / `Dashboard`).
          { Tag = "Box"
            Category = "Layout"
            Fields =
              [ { Name = "children"
                  Type = TList TNode
                  Opt = Required }
                { Name = "heading"
                  Type = TUnion("TextSource", [])
                  Opt = Optional }
                { Name = "layout"
                  Type = TUnion("LayoutMode", [])
                  Opt = Required }
                { Name = "role"
                  Type = TEnum "BoxRole"
                  Opt = Required } ] } ]
      Records = []
      Defaults =
        [ { Kind = "Heading"
            Field = "variant"
            Value = VEnum "Standard" }
          { Kind = "Button"
            Field = "variant"
            Value = VEnum "Primary" } ] }

/// `TextSource.Literal` — the most-used value-union, sugared.
let lit (s: string) : IdlValue = VUnion("Literal", [ "text", VStr s ])

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

let private btn1 =
    VNode(
        "btn-1",
        "Button",
        [ "disabled", VUnion("State", [ "defaultValue", VBool false; "key", VStr "loading" ]) // Binding<bool>
          "icon", VStr "refresh"
          "label", lit "Refresh"
          "onClick", VUnion("Chain", [ "ops", VList [] ])
          "variant", VEnum "Primary" ]
    )

/// Metric — also reused as a child of Card and Stack (so the nested nodes are the same value).
let private metric1 =
    VNode(
        "metric-1",
        "Metric",
        [ "emphasis", VEnum "Normal"
          "format", VUnion("Currency", [ "code", VStr "GBP" ])
          "icon", VStr "trending-up"
          "label", lit "Revenue"
          "value", VUnion("Static", [ "value", VFloat 1234.5 ]) // Binding<float>
          "subtext", lit "vs last month"
          "tone", VEnum "Brand"
          "trend", VUnion("Static", [ "value", VFloat 0.07 ]) // Binding<float>
          "trendFormat", VUnion("Percent", [ "decimals", VInt 1 ])
          "weight", VEnum "Standard" ]
    )

let private markdown1 =
    VNode("markdown-1", "Markdown", [ "text", lit "Updated hourly." ])

let private flexV =
    VUnion("Flex", [ "direction", VEnum "Vertical"; "wrap", VBool false ])

let private card1 =
    VNode(
        "card-1",
        "Box",
        [ "children", VList [ metric1 ]
          "heading", lit "Insights"
          "layout", flexV
          "role", VEnum "Card" ]
    )

let private stack1 =
    VNode(
        "stack-1",
        "Box",
        [ "children", VList [ metric1; markdown1 ]
          "layout", flexV
          "role", VEnum "Group" ]
    )

/// Fixture name → authored value. Each must encode byte-identical to `nodes/<name>.json`.
let cases: (string * IdlValue) list =
    [ "heading-1", heading1
      "badge-1", badge1
      "btn-1", btn1
      "metric-1", metric1
      "card-1", card1
      "markdown-1", markdown1
      "stack-1", stack1 ]

/// A negative control: one mutated field, asserted *not* to match its fixture — so a
/// green run is meaningful rather than vacuous.
let negativeControl: string * IdlValue =
    "heading-1",
    VNode(
        "heading-1",
        "Heading",
        [ "level", VInt 2
          "text", lit "Channel performance — TYPO"
          "variant", VEnum "Standard" ]
    )

/// A Button whose `disabled : Binding<bool>` is given a `float` value — must be rejected,
/// proving the generic instantiation type-checks (`'T` bound to `bool`, a `VFloat` does not
/// match). The rest of the node is well-formed so the failure is the element type alone.
let wrongElementType: IdlValue =
    VNode(
        "bad-binding",
        "Button",
        [ "disabled", VUnion("Static", [ "value", VFloat 1.0 ]) // Binding<bool> given a float
          "label", lit "x"
          "onClick", VUnion("Chain", [ "ops", VList [] ])
          "variant", VEnum "Primary" ]
    )
