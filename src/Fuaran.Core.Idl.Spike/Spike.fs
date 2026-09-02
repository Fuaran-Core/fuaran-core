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
        [ Declare.enumOf "HeadingVariant" [ "Standard"; "Subtle"; "Display" ]
          Declare.enumOf "BadgeVariant" [ "Info"; "Success"; "Warning"; "Critical"; "Neutral" ]
          Declare.enumOf "ButtonVariant" [ "Primary"; "Secondary"; "Ghost"; "Danger" ]
          Declare.enumOf "Emphasis" [ "Normal"; "Strong"; "Subtle" ]
          Declare.enumOf "ToneVariant" [ "Default"; "Brand"; "Positive"; "Caution"; "Critical" ]
          Declare.enumOf "StyleWeight" [ "Standard"; "Light"; "Heavy" ]
          Declare.enumOf "Orientation" [ "Horizontal"; "Vertical" ]
          Declare.enumOf "BoxRole" [ "Dashboard"; "Card"; "Group" ] ]
      Unions =
        [ { Name = "TextSource"
            Params = []
            Cases =
              [ { Tag = "Literal"
                  Fields =
                    [ { Name = "text"
                        Type = TStr
                        Opt = Required
                        Annotations = Annotations.Empty } ]
                  Annotations = Annotations.Empty } ] }
          // The parameterised union — `Binding<'T>`; `'T` flows into the case fields.
          { Name = "Binding"
            Params = [ "T" ]
            Cases =
              [ { Tag = "Static"
                  Fields =
                    [ { Name = "value"
                        Type = TVar "T"
                        Opt = Required
                        Annotations = Annotations.Empty } ]
                  Annotations = Annotations.Empty }
                { Tag = "State"
                  Fields =
                    [ { Name = "defaultValue"
                        Type = TVar "T"
                        Opt = Required
                        Annotations = Annotations.Empty }
                      { Name = "key"
                        Type = TStr
                        Opt = Required
                        Annotations = Annotations.Empty } ]
                  Annotations = Annotations.Empty }
                // Phase 689 spike, leg 3 — a closure whose signature mentions the
                // union's OWN type parameter rather than `'Msg`. It is here to prove
                // the `'Msg` fixpoint does not over-reach: `Binding<'T>` must stay
                // msg-free, exactly as the hand-written tier keeps it (which obj-erases
                // at `LocalBinding.OnCommit` for the same reason).
                { Tag = "Computed"
                  Fields =
                    [ { Name = "fn"
                        Type =
                          TFn
                              { FSharp = "obj -> 'T"
                                TypeScript = "(ctx: unknown) => T"
                                // A `'T` cannot be conjured at decode. The hand-written
                                // decoder threads a real placeholder value per type
                                // parameter (`bindingGeneric<'T> … placeholder …`); the
                                // generated one has no such channel yet, so the spike
                                // records the gap rather than papering over it.
                                Placeholder = "(fun _ -> Unchecked.defaultof<'T>)" }
                        Opt = Required
                        Annotations = Annotations.Empty } ]
                  // Phase 113 — the spike's declared-annotation fixture, on the same
                  // footing as Phase 676's `Action.Notify` (added so the generative
                  // sweep exercised `TJson` at all): compiling `Generated.fs` is what
                  // proves the emitted `[<Obsolete>]` is valid F# where the emitter
                  // puts it — INLINE after the bar, inside a `[<RequireQualifiedAccess>]`
                  // `and`-group member that is generic in `'T`.
                  //
                  // And it is TRUE of this case rather than decorative. `Computed`'s
                  // entire content is a closure: the encoder writes the fixed
                  // `"<closure>"` sentinel without reading it and the decoder restores
                  // the declared placeholder, so what survives a wire boundary is the
                  // shape and never the function. That is the same fact `Action.Dispatch`
                  // carries in the real vocabulary, which is the case this annotation set
                  // was asked for.
                  Annotations =
                    { Annotations.Empty with
                        InProcessOnly = true } } ] }
          { Name = "Format"
            Params = []
            Cases =
              [ { Tag = "Currency"
                  Fields =
                    [ { Name = "code"
                        Type = TStr
                        Opt = Required
                        Annotations = Annotations.Empty } ]
                  Annotations = Annotations.Empty }
                { Tag = "Percent"
                  Fields =
                    [ { Name = "decimals"
                        Type = TInt
                        Opt = Required
                        Annotations = Annotations.Empty } ]
                  Annotations = Annotations.Empty } ] }
          { Name = "Action"
            Params = []
            Cases =
              [ { Tag = "Chain"
                  Fields =
                    [ { Name = "ops"
                        Type = TList(TUnion("Action", []))
                        Opt = Required
                        Annotations = Annotations.Empty } ]
                  Annotations = Annotations.Empty }
                // Phase 676 — a `TJson` case, present so the GENERATIVE cross-host
                // test actually exercises the JSON passthrough. Without it the spike
                // vocabulary has no `TJson` anywhere and both new legs (F# `id` /
                // `dJson`, TS `encJson`) would ship unverified across hosts. No fixed
                // fixture uses it, so the vendored snapshots are unchanged.
                { Tag = "Notify"
                  Fields =
                    [ { Name = "channel"
                        Type = TStr
                        Opt = Required
                        Annotations = Annotations.Empty }
                      { Name = "payload"
                        Type = TJson
                        Opt = Required
                        Annotations = Annotations.Empty } ]
                  Annotations = Annotations.Empty } ] }
          // Fuaran-UI 0.2.0 Box unification: Dashboard/Card/Stack/GridLayout → one `Box`
          // kind with `role` + a `layout` mode (Auto / Flex{direction,wrap} / Grid{cols}).
          { Name = "LayoutMode"
            Params = []
            Cases =
              [ { Tag = "Auto"
                  Fields = []
                  Annotations = Annotations.Empty }
                { Tag = "Flex"
                  Fields =
                    [ { Name = "direction"
                        Type = TEnum "Orientation"
                        Opt = Required
                        Annotations = Annotations.Empty }
                      { Name = "wrap"
                        Type = TBool
                        Opt = Required
                        Annotations = Annotations.Empty } ]
                  Annotations = Annotations.Empty }
                { Tag = "Grid"
                  Fields =
                    [ { Name = "cols"
                        Type = TInt
                        Opt = Required
                        Annotations = Annotations.Empty }
                      { Name = "templateColumns"
                        Type = TStr
                        Opt = Optional
                        Annotations = Annotations.Empty } ]
                  Annotations = Annotations.Empty } ] } ]
      Kinds =
        [ { Tag = "Heading"
            Category = "Display"
            Annotations = Annotations.Empty
            Fields =
              [ { Name = "level"
                  Type = TInt
                  Opt = Required
                  Annotations = Annotations.Empty }
                { Name = "text"
                  Type = TUnion("TextSource", [])
                  Opt = Required
                  Annotations = Annotations.Empty }
                { Name = "variant"
                  Type = TEnum "HeadingVariant"
                  Opt = Required
                  Annotations = Annotations.Empty } ] }
          { Tag = "Badge"
            Category = "Display"
            Annotations = Annotations.Empty
            Fields =
              [ { Name = "label"
                  Type = TUnion("TextSource", [])
                  Opt = Required
                  Annotations = Annotations.Empty }
                { Name = "variant"
                  Type = TEnum "BadgeVariant"
                  Opt = Required
                  Annotations = Annotations.Empty } ] }
          { Tag = "Button"
            Category = "Input"
            Annotations = Annotations.Empty
            Fields =
              [ { Name = "disabled"
                  Type = TUnion("Binding", [ TBool ])
                  Opt = Optional
                  Annotations = Annotations.Empty }
                { Name = "icon"
                  Type = TStr
                  Opt = Optional
                  Annotations = Annotations.Empty }
                { Name = "label"
                  Type = TUnion("TextSource", [])
                  Opt = Required
                  Annotations = Annotations.Empty }
                { Name = "onClick"
                  Type = TUnion("Action", [])
                  Opt = Required
                  Annotations = Annotations.Empty }
                { Name = "variant"
                  Type = TEnum "ButtonVariant"
                  Opt = Required
                  Annotations = Annotations.Empty } ] }
          { Tag = "Metric"
            Category = "Display"
            Annotations = Annotations.Empty
            Fields =
              [ { Name = "emphasis"
                  Type = TEnum "Emphasis"
                  Opt = OmitDefault(VEnum "Normal")
                  Annotations = Annotations.Empty }
                { Name = "format"
                  Type = TUnion("Format", [])
                  Opt = Required
                  Annotations = Annotations.Empty }
                { Name = "icon"
                  Type = TStr
                  Opt = Optional
                  Annotations = Annotations.Empty }
                { Name = "label"
                  Type = TUnion("TextSource", [])
                  Opt = Required
                  Annotations = Annotations.Empty }
                { Name = "subtext"
                  Type = TUnion("TextSource", [])
                  Opt = Optional
                  Annotations = Annotations.Empty }
                { Name = "tone"
                  Type = TEnum "ToneVariant"
                  Opt = OmitDefault(VEnum "Default")
                  Annotations = Annotations.Empty }
                { Name = "trend"
                  Type = TUnion("Binding", [ TFloat ])
                  Opt = Optional
                  Annotations = Annotations.Empty }
                { Name = "trendFormat"
                  Type = TUnion("Format", [])
                  Opt = Optional
                  Annotations = Annotations.Empty }
                // Fuaran-UI 0.2.x renamed Metric's binding slot `source` → `value`. Declared in
                // Ordinal key order (the TS backend emits in author order — no key sort).
                { Name = "value"
                  Type = TUnion("Binding", [ TFloat ])
                  Opt = Required
                  Annotations = Annotations.Empty }
                { Name = "weight"
                  Type = TEnum "StyleWeight"
                  Opt = OmitDefault(VEnum "Standard")
                  Annotations = Annotations.Empty } ] }
          { Tag = "Markdown"
            Category = "Display"
            Annotations = Annotations.Empty
            Fields =
              [ { Name = "text"
                  Type = TUnion("TextSource", [])
                  Opt = Required
                  Annotations = Annotations.Empty } ] }
          // The unified `Box` container (was `Card` / `Stack` / `GridLayout` / `Dashboard`).
          //
          // Phase 119 — annotated at KIND level, for the reason `Binding.Computed` and
          // `Tabs.onCommit` are annotated at case and field level: it is TRUE of this
          // vocabulary — the line above already records that `Box` is the 0.2.0
          // unification, so `0.2.0` is the version the kind first appeared in — and
          // compiling `Generated.fs` is what proves the emitted doc block is valid F# in
          // the placement this phase adds, above an `and`-joined member of the
          // type-recursion group. `Since` earns no attribute, by design: there is nothing
          // for a compiler to say about a fact concerning the past.
          { Tag = "Box"
            Category = "Layout"
            Annotations =
              { Annotations.Empty with
                  Since = Some "0.2.0" }
            Fields =
              [ { Name = "children"
                  Type = TList TNode
                  Opt = Required
                  Annotations = Annotations.Empty }
                { Name = "heading"
                  Type = TUnion("TextSource", [])
                  Opt = Optional
                  Annotations = Annotations.Empty }
                { Name = "layout"
                  Type = TUnion("LayoutMode", [])
                  Opt = Required
                  Annotations = Annotations.Empty }
                { Name = "role"
                  Type = TEnum "BoxRole"
                  Opt = Required
                  Annotations = Annotations.Empty } ] }
          // ── Phase 689 spike, legs 1 + 2 ───────────────────────────────────
          //
          // A kind carrying `'Msg`-producing handlers. `Tabs` stands in for the
          // phase's named targets because the real `Button.onClick` turns out NOT
          // to be a closure at all — it is an `Action` union, and the `'Msg` in
          // `Action.Dispatch of 'Msg` is a wire-OMITTED value rather than a
          // `"<closure>"` sentinel. That is a distinct case (see D2 open question
          // 2); `Tabs.onSelect` / `onCommit` are genuine closures, and two of them
          // with different argument types is what the spike needs to prove.
          //
          // Both slots are `TList TNode`-adjacent on purpose: `children` forces the
          // `'Msg` to travel Node → NodeKind → TabsSpec → Node again, which is the
          // recursion that would break a naive threading.
          { Tag = "Tabs"
            Category = "Layout"
            Annotations = Annotations.Empty
            Fields =
              [ { Name = "children"
                  Type = TList TNode
                  Opt = Required
                  Annotations = Annotations.Empty }
                // Declared in Ordinal key order — the TS backend emits in author
                // order and does not sort, so `onCommit` precedes `onSelect` here.
                //
                // Required: always on the wire as the sentinel. A second argument
                // type, so the emission cannot be accidentally monomorphic.
                //
                // Phase 113 — annotated for the same two reasons the `Binding.Computed`
                // case is: it is true (the handler is written as a sentinel and read
                // back as a placeholder, so the function itself never crosses the wire),
                // and compiling `Generated.fs` is what proves the emitted attribute is
                // valid F# in the OTHER placement the emitter uses — its own line above
                // a record field, inside the generated spec record.
                { Name = "onCommit"
                  Type =
                    TFn
                        { FSharp = "string -> 'Msg"
                          TypeScript = "(tag: string) => Msg"
                          Placeholder = "(fun (_: string) -> box \"<closure>\")" }
                  Opt = Required
                  Annotations =
                    { Annotations.Empty with
                        InProcessOnly = true } }
                // Optional: PRESENCE is wire-visible, so decode must restore
                // `Some placeholder` — not `None`, and not `Some ()`.
                { Name = "onSelect"
                  Type =
                    TFn
                        { FSharp = "int -> 'Msg"
                          TypeScript = "(index: number) => Msg"
                          Placeholder = "(fun (_: int) -> box \"<closure>\")" }
                  Opt = Optional
                  Annotations = Annotations.Empty } ] } ]
      Records = []
      Defaults =
        [ { Kind = "Heading"
            Field = "variant"
            Value = VEnum "Standard" }
          { Kind = "Button"
            Field = "variant"
            Value = VEnum "Primary" } ]
      // The mini IDL models no node envelope — `Node` stays `{ id, kind }`, which
      // is also what keeps the Phase 690 generator change provably additive here.
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      Harden = HardenPolicy.Default }

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
