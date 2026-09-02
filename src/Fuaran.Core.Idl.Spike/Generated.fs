// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen (Phase 317 increment 3). Do not edit by hand.
module Fuaran.Core.Idl.Spike.Generated
#nowarn "44" // this layer implements every declared member, including deprecated ones

open Fuaran.Core

[<RequireQualifiedAccess>]
type HeadingVariant =
    | Standard
    | Subtle
    | Display

[<RequireQualifiedAccess>]
type BadgeVariant =
    | Info
    | Success
    | Warning
    | Critical
    | Neutral

[<RequireQualifiedAccess>]
type ButtonVariant =
    | Primary
    | Secondary
    | Ghost
    | Danger

[<RequireQualifiedAccess>]
type Emphasis =
    | Normal
    | Strong
    | Subtle

[<RequireQualifiedAccess>]
type ToneVariant =
    | Default
    | Brand
    | Positive
    | Caution
    | Critical

[<RequireQualifiedAccess>]
type StyleWeight =
    | Standard
    | Light
    | Heavy

[<RequireQualifiedAccess>]
type Orientation =
    | Horizontal
    | Vertical

[<RequireQualifiedAccess>]
type BoxRole =
    | Dashboard
    | Card
    | Group

[<RequireQualifiedAccess>]
type TextSource =
    | Literal of text: string

and [<RequireQualifiedAccess>] Binding<'T> =
    | Static of value: 'T
    | State of defaultValue: 'T * key: string
    /// **In-process only** — this member has no wire projection: a value here
    /// is carried inside one host process and is LOST across any wire boundary.
    | [<System.Obsolete("in-process only — no wire projection; a value here is lost across a wire boundary", false)>] Computed of fn: (obj -> 'T)

and [<RequireQualifiedAccess>] Format =
    | Currency of code: string
    | Percent of decimals: int

and [<RequireQualifiedAccess>] Action =
    | Chain of ops: Action list
    | Notify of channel: string * payload: JVal

and [<RequireQualifiedAccess>] LayoutMode =
    | Auto
    | Flex of direction: Orientation * wrap: bool
    | Grid of cols: int * templateColumns: string option

// Display
and HeadingSpec =
    {
      Level: int
      Text: TextSource
      Variant: HeadingVariant
    }

// Display
and BadgeSpec =
    {
      Label: TextSource
      Variant: BadgeVariant
    }

// Input
and ButtonSpec =
    {
      Disabled: Binding<bool> option
      Icon: string option
      Label: TextSource
      OnClick: Action
      Variant: ButtonVariant
    }

// Display
and MetricSpec =
    {
      Emphasis: Emphasis
      Format: Format
      Icon: string option
      Label: TextSource
      Subtext: TextSource option
      Tone: ToneVariant
      Trend: Binding<float> option
      TrendFormat: Format option
      Value: Binding<float>
      Weight: StyleWeight
    }

// Layout
/// Since `0.2.0`.
and BoxSpec<'Msg> =
    {
      Children: Node<'Msg> list
      Heading: TextSource option
      Layout: LayoutMode
      Role: BoxRole
    }

// Display
and MarkdownSpec =
    {
      Text: TextSource
    }

// Layout
and TabsSpec<'Msg> =
    {
      Children: Node<'Msg> list
      /// **In-process only** — this member has no wire projection: a value here
      /// is carried inside one host process and is LOST across any wire boundary.
      [<System.Obsolete("in-process only — no wire projection; a value here is lost across a wire boundary", false)>]
      OnCommit: (string -> 'Msg)
      OnSelect: (int -> 'Msg) option
    }

and [<RequireQualifiedAccess>] NodeKind<'Msg> =
    | Heading of HeadingSpec
    | Badge of BadgeSpec
    | Button of ButtonSpec
    | Metric of MetricSpec
    | Box of BoxSpec<'Msg>
    | Markdown of MarkdownSpec
    | Tabs of TabsSpec<'Msg>

and Node<'Msg> = { Id: string; Kind: NodeKind<'Msg> }

let private encHeadingVariant (v: HeadingVariant) : JVal =
    match v with
    | HeadingVariant.Standard -> JStr "Standard"
    | HeadingVariant.Subtle -> JStr "Subtle"
    | HeadingVariant.Display -> JStr "Display"

let private encBadgeVariant (v: BadgeVariant) : JVal =
    match v with
    | BadgeVariant.Info -> JStr "Info"
    | BadgeVariant.Success -> JStr "Success"
    | BadgeVariant.Warning -> JStr "Warning"
    | BadgeVariant.Critical -> JStr "Critical"
    | BadgeVariant.Neutral -> JStr "Neutral"

let private encButtonVariant (v: ButtonVariant) : JVal =
    match v with
    | ButtonVariant.Primary -> JStr "Primary"
    | ButtonVariant.Secondary -> JStr "Secondary"
    | ButtonVariant.Ghost -> JStr "Ghost"
    | ButtonVariant.Danger -> JStr "Danger"

let private encEmphasis (v: Emphasis) : JVal =
    match v with
    | Emphasis.Normal -> JStr "Normal"
    | Emphasis.Strong -> JStr "Strong"
    | Emphasis.Subtle -> JStr "Subtle"

let private encToneVariant (v: ToneVariant) : JVal =
    match v with
    | ToneVariant.Default -> JStr "Default"
    | ToneVariant.Brand -> JStr "Brand"
    | ToneVariant.Positive -> JStr "Positive"
    | ToneVariant.Caution -> JStr "Caution"
    | ToneVariant.Critical -> JStr "Critical"

let private encStyleWeight (v: StyleWeight) : JVal =
    match v with
    | StyleWeight.Standard -> JStr "Standard"
    | StyleWeight.Light -> JStr "Light"
    | StyleWeight.Heavy -> JStr "Heavy"

let private encOrientation (v: Orientation) : JVal =
    match v with
    | Orientation.Horizontal -> JStr "Horizontal"
    | Orientation.Vertical -> JStr "Vertical"

let private encBoxRole (v: BoxRole) : JVal =
    match v with
    | BoxRole.Dashboard -> JStr "Dashboard"
    | BoxRole.Card -> JStr "Card"
    | BoxRole.Group -> JStr "Group"

// WIRE_FORMAT §5 — a non-finite double has no JSON *number* spelling, so it rides as
// one of the three quoted sentinel strings, which §7 requires a decoder to read back
// AT A FLOAT SLOT (`dFloat` below; `dInt` is deliberately not widened — §7 stops at
// the float slot, and an integer slot has no sentinel).
//
// Building the `JStr` HERE rather than leaving `Canon.render` to spell a non-finite
// `JFloat` is what keeps the emitted `JVal` renderable by the GUARDED
// `Fuaran.Core.Wire.tryRender`, which refuses a non-finite `JFloat` outright. The core
// wire model still has no non-finite float — the sentinel is a string, which it carries
// perfectly — so this widens the generated float slot's spelling, not the model.
let private encFloat (f: float) : JVal =
    if System.Double.IsNaN f then JStr "NaN"
    elif System.Double.IsPositiveInfinity f then JStr "Infinity"
    elif System.Double.IsNegativeInfinity f then JStr "-Infinity"
    else JFloat f

let rec private encNodeKind (k: NodeKind<'Msg>) : JVal =
    match k with
    | NodeKind.Heading s -> encHeadingSpec s
    | NodeKind.Badge s -> encBadgeSpec s
    | NodeKind.Button s -> encButtonSpec s
    | NodeKind.Metric s -> encMetricSpec s
    | NodeKind.Box s -> encBoxSpec s
    | NodeKind.Markdown s -> encMarkdownSpec s
    | NodeKind.Tabs s -> encTabsSpec s

and private encNode (n: Node<'Msg>) : JVal =
    let kind = encNodeKind n.Kind

    JObj [ "id", JStr n.Id; "kind", kind ]

and private encTextSource (v: TextSource) : JVal =
    match v with
    | TextSource.Literal text -> JStr text

and private encBinding<'T> (encT: 'T -> JVal) (v: Binding<'T>) : JVal =
    match v with
    | Binding.Static value -> Canon.typed "Static" [ "value", encT value ]
    | Binding.State (defaultValue, key) -> Canon.typed "State" [ "defaultValue", encT defaultValue; "key", JStr key ]
    | Binding.Computed fn -> Canon.typed "Computed" [ "fn", JStr "<closure>" ]

and private encFormat (v: Format) : JVal =
    match v with
    | Format.Currency code -> Canon.typed "Currency" [ "code", JStr code ]
    | Format.Percent decimals -> Canon.typed "Percent" [ "decimals", JInt decimals ]

and private encAction (v: Action) : JVal =
    match v with
    | Action.Chain ops -> Canon.typed "Chain" [ "ops", JArr(List.map encAction ops) ]
    | Action.Notify (channel, payload) -> Canon.typed "Notify" [ "channel", JStr channel; "payload", id payload ]

and private encLayoutMode (v: LayoutMode) : JVal =
    match v with
    | LayoutMode.Auto -> Canon.typed "Auto" [  ]
    | LayoutMode.Flex (direction, wrap) -> Canon.typed "Flex" [ "direction", encOrientation direction; "wrap", JBool wrap ]
    | LayoutMode.Grid (cols, templateColumns) -> Canon.typed "Grid" ([ Some("cols", JInt cols); (templateColumns |> Option.map (fun v -> "templateColumns", JStr v)) ] |> List.choose id)

and private encHeadingSpec (s: HeadingSpec) : JVal =
    Canon.typed "Heading" ([ Some("level", JInt s.Level); Some("text", encTextSource s.Text); Some("variant", encHeadingVariant s.Variant) ] |> List.choose id)

and private encBadgeSpec (s: BadgeSpec) : JVal =
    Canon.typed "Badge" ([ Some("label", encTextSource s.Label); Some("variant", encBadgeVariant s.Variant) ] |> List.choose id)

and private encButtonSpec (s: ButtonSpec) : JVal =
    Canon.typed "Button" ([ (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)); (s.Icon |> Option.map (fun v -> "icon", JStr v)); Some("label", encTextSource s.Label); Some("onClick", encAction s.OnClick); Some("variant", encButtonVariant s.Variant) ] |> List.choose id)

and private encMetricSpec (s: MetricSpec) : JVal =
    Canon.typed "Metric" ([ (if s.Emphasis = Emphasis.Normal then None else Some("emphasis", encEmphasis s.Emphasis)); Some("format", encFormat s.Format); (s.Icon |> Option.map (fun v -> "icon", JStr v)); Some("label", encTextSource s.Label); (s.Subtext |> Option.map (fun v -> "subtext", encTextSource v)); (if s.Tone = ToneVariant.Default then None else Some("tone", encToneVariant s.Tone)); (s.Trend |> Option.map (fun v -> "trend", (encBinding encFloat) v)); (s.TrendFormat |> Option.map (fun v -> "trendFormat", encFormat v)); Some("value", (encBinding encFloat) s.Value); (if s.Weight = StyleWeight.Standard then None else Some("weight", encStyleWeight s.Weight)) ] |> List.choose id)

and private encBoxSpec<'Msg> (s: BoxSpec<'Msg>) : JVal =
    Canon.typed "Box" ([ Some("children", JArr(List.map encNode s.Children)); (s.Heading |> Option.map (fun v -> "heading", encTextSource v)); Some("layout", encLayoutMode s.Layout); Some("role", encBoxRole s.Role) ] |> List.choose id)

and private encMarkdownSpec (s: MarkdownSpec) : JVal =
    Canon.typed "Markdown" ([ Some("text", encTextSource s.Text) ] |> List.choose id)

and private encTabsSpec<'Msg> (s: TabsSpec<'Msg>) : JVal =
    Canon.typed "Tabs" ([ Some("children", JArr(List.map encNode s.Children)); Some("onCommit", JStr "<closure>"); (s.OnSelect |> Option.map (fun v -> "onSelect", JStr "<closure>")) ] |> List.choose id)

let encodeNode (n: Node<'Msg>) : string = Canon.render (encNode n)

/// JVal-level accessors (Phase 694) — for host codecs that splice generated
/// encodings into a larger canonical document (e.g. a TreeOp codec).
let encodeNodeJson (n: Node<'Msg>) : JVal = encNode n

let encodeNodeKindJson (k: NodeKind<'Msg>) : JVal = encNodeKind k

let private dObj (j: JVal) : Result<(string * JVal) list, string> =
    match j with
    | JObj fs -> Ok fs
    | _ -> Error "expected an object"

let private dTag (fs: (string * JVal) list) : Result<string, string> =
    match fs |> List.tryFind (fun (k, _) -> k = "$type") with
    | Some(_, JStr t) -> Ok t
    | _ -> Error "missing or non-string $type"

let private dStr (j: JVal) : Result<string, string> =
    match j with
    | JStr s -> Ok s
    | _ -> Error "expected a string"

let private dInt (j: JVal) : Result<int, string> =
    match j with
    | JInt i -> Ok i
    | _ -> Error "expected an int"

let private dBool (j: JVal) : Result<bool, string> =
    match j with
    | JBool b -> Ok b
    | _ -> Error "expected a bool"

// A whole-valued float renders without a decimal point, so it parses back as JInt.
// WIRE_FORMAT §7 — a float slot also accepts the three quoted non-finite sentinels, which
// is how §5 spells a number JSON has no literal for. The value decodes to the FLOAT, never
// to the string: a host that answered the string would hand a consumer a different tree on
// the second decode while the bytes stayed identical. `dInt` is NOT widened — §7 stops at
// the float slot.
let private dFloat (j: JVal) : Result<float, string> =
    match j with
    | JFloat f -> Ok f
    | JInt i -> Ok(float i)
    | JStr "NaN" -> Ok System.Double.NaN
    | JStr "Infinity" -> Ok System.Double.PositiveInfinity
    | JStr "-Infinity" -> Ok System.Double.NegativeInfinity
    | _ -> Error "expected a number"

let private dUnit (_: JVal) : Result<unit, string> = Ok()

// Phase 676 — arbitrary JSON, kept verbatim. No shape check: the field's
// contract is that its content is not the schema's business.
let private dJson (j: JVal) : Result<JVal, string> = Ok j

let private dList (dec: JVal -> Result<'T, string>) (j: JVal) : Result<'T list, string> =
    match j with
    | JArr xs ->
        (Ok [], xs)
        ||> List.fold (fun acc x ->
            match acc with
            | Error e -> Error e
            | Ok items -> dec x |> Result.map (fun v -> v :: items))
        |> Result.map List.rev
    | _ -> Error "expected an array"

let private dMap (dec: JVal -> Result<'T, string>) (j: JVal) : Result<Map<string, 'T>, string> =
    match j with
    | JObj fs ->
        (Ok [], fs)
        ||> List.fold (fun acc (k, v) ->
            match acc with
            | Error e -> Error e
            | Ok items -> dec v |> Result.map (fun d -> (k, d) :: items))
        |> Result.map (List.rev >> Map.ofList)
    | _ -> Error "expected an object"

let private dReq (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, string>) : Result<'T, string> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v
    | None -> Error("missing required field '" + name + "'")

let private dOpt (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, string>) : Result<'T option, string> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v |> Result.map Some
    | None -> Ok None

let private dDef (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, string>) (dflt: 'T) : Result<'T, string> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v
    | None -> Ok dflt

// An optional closure / opaque field: the value is a sentinel carrying nothing,
// but its PRESENCE distinguishes `Some ()` from `None` and must be read back.
let private dPresent (name: string) (fs: (string * JVal) list) : Result<unit option, string> =
    Ok(fs |> List.tryFind (fun (k, _) -> k = name) |> Option.map (fun _ -> ()))

let private decHeadingVariant (j: JVal) : Result<HeadingVariant, string> =
    match j with
    | JStr "Standard" -> Ok HeadingVariant.Standard
    | JStr "Subtle" -> Ok HeadingVariant.Subtle
    | JStr "Display" -> Ok HeadingVariant.Display
    | _ -> Error "not a HeadingVariant"

let private decBadgeVariant (j: JVal) : Result<BadgeVariant, string> =
    match j with
    | JStr "Info" -> Ok BadgeVariant.Info
    | JStr "Success" -> Ok BadgeVariant.Success
    | JStr "Warning" -> Ok BadgeVariant.Warning
    | JStr "Critical" -> Ok BadgeVariant.Critical
    | JStr "Neutral" -> Ok BadgeVariant.Neutral
    | _ -> Error "not a BadgeVariant"

let private decButtonVariant (j: JVal) : Result<ButtonVariant, string> =
    match j with
    | JStr "Primary" -> Ok ButtonVariant.Primary
    | JStr "Secondary" -> Ok ButtonVariant.Secondary
    | JStr "Ghost" -> Ok ButtonVariant.Ghost
    | JStr "Danger" -> Ok ButtonVariant.Danger
    | _ -> Error "not a ButtonVariant"

let private decEmphasis (j: JVal) : Result<Emphasis, string> =
    match j with
    | JStr "Normal" -> Ok Emphasis.Normal
    | JStr "Strong" -> Ok Emphasis.Strong
    | JStr "Subtle" -> Ok Emphasis.Subtle
    | _ -> Error "not a Emphasis"

let private decToneVariant (j: JVal) : Result<ToneVariant, string> =
    match j with
    | JStr "Default" -> Ok ToneVariant.Default
    | JStr "Brand" -> Ok ToneVariant.Brand
    | JStr "Positive" -> Ok ToneVariant.Positive
    | JStr "Caution" -> Ok ToneVariant.Caution
    | JStr "Critical" -> Ok ToneVariant.Critical
    | _ -> Error "not a ToneVariant"

let private decStyleWeight (j: JVal) : Result<StyleWeight, string> =
    match j with
    | JStr "Standard" -> Ok StyleWeight.Standard
    | JStr "Light" -> Ok StyleWeight.Light
    | JStr "Heavy" -> Ok StyleWeight.Heavy
    | _ -> Error "not a StyleWeight"

let private decOrientation (j: JVal) : Result<Orientation, string> =
    match j with
    | JStr "Horizontal" -> Ok Orientation.Horizontal
    | JStr "Vertical" -> Ok Orientation.Vertical
    | _ -> Error "not a Orientation"

let private decBoxRole (j: JVal) : Result<BoxRole, string> =
    match j with
    | JStr "Dashboard" -> Ok BoxRole.Dashboard
    | JStr "Card" -> Ok BoxRole.Card
    | JStr "Group" -> Ok BoxRole.Group
    | _ -> Error "not a BoxRole"

let rec private decNodeKind (j: JVal) : Result<NodeKind<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dTag __fs |> Result.bind (fun __t ->
    match __t with
    | "Heading" -> decHeadingSpec j |> Result.map NodeKind.Heading
    | "Badge" -> decBadgeSpec j |> Result.map NodeKind.Badge
    | "Button" -> decButtonSpec j |> Result.map NodeKind.Button
    | "Metric" -> decMetricSpec j |> Result.map NodeKind.Metric
    | "Box" -> decBoxSpec j |> Result.map NodeKind.Box
    | "Markdown" -> decMarkdownSpec j |> Result.map NodeKind.Markdown
    | "Tabs" -> decTabsSpec j |> Result.map NodeKind.Tabs
    | __other -> Error ("unknown node kind: " + __other)))

and private decNode (j: JVal) : Result<Node<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "id" __fs dStr |> Result.bind (fun id ->
    dReq "kind" __fs decNodeKind |> Result.bind (fun kind ->
    Ok { Id = id; Kind = kind })))

and private decTextSource (j: JVal) : Result<TextSource, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Literal" ->
            dReq "text" __fs dStr |> Result.bind (fun text ->
            Ok(TextSource.Literal(text)))
        | __other -> Error ("unknown TextSource case: " + __other))
    | __bare ->
        dStr __bare |> Result.bind (fun text -> Ok(TextSource.Literal(text)))

and private decBinding<'T> (decT: JVal -> Result<'T, string>) (j: JVal) : Result<Binding<'T>, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Static" ->
            dReq "value" __fs decT |> Result.bind (fun value ->
            Ok(Binding.Static(value)))
        | "State" ->
            dReq "defaultValue" __fs decT |> Result.bind (fun defaultValue ->
            dReq "key" __fs dStr |> Result.bind (fun key ->
            Ok(Binding.State(defaultValue, key))))
        | "Computed" ->
            Ok ((fun _ -> Unchecked.defaultof<'T>)) |> Result.bind (fun fn ->
            Ok(Binding.Computed(fn)))
        | __other -> Error ("unknown Binding case: " + __other))
    | _ -> Error "expected a Binding object"

and private decFormat (j: JVal) : Result<Format, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Currency" ->
            dReq "code" __fs dStr |> Result.bind (fun code ->
            Ok(Format.Currency(code)))
        | "Percent" ->
            dReq "decimals" __fs dInt |> Result.bind (fun decimals ->
            Ok(Format.Percent(decimals)))
        | __other -> Error ("unknown Format case: " + __other))
    | _ -> Error "expected a Format object"

and private decAction (j: JVal) : Result<Action, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Chain" ->
            dReq "ops" __fs (dList decAction) |> Result.bind (fun ops ->
            Ok(Action.Chain(ops)))
        | "Notify" ->
            dReq "channel" __fs dStr |> Result.bind (fun channel ->
            dReq "payload" __fs dJson |> Result.bind (fun payload ->
            Ok(Action.Notify(channel, payload))))
        | __other -> Error ("unknown Action case: " + __other))
    | _ -> Error "expected a Action object"

and private decLayoutMode (j: JVal) : Result<LayoutMode, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Auto" -> Ok LayoutMode.Auto
        | "Flex" ->
            dReq "direction" __fs decOrientation |> Result.bind (fun direction ->
            dReq "wrap" __fs dBool |> Result.bind (fun wrap ->
            Ok(LayoutMode.Flex(direction, wrap))))
        | "Grid" ->
            dReq "cols" __fs dInt |> Result.bind (fun cols ->
            dOpt "templateColumns" __fs dStr |> Result.bind (fun templateColumns ->
            Ok(LayoutMode.Grid(cols, templateColumns))))
        | __other -> Error ("unknown LayoutMode case: " + __other))
    | _ -> Error "expected a LayoutMode object"

and private decHeadingSpec (j: JVal) : Result<HeadingSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "level" __fs dInt |> Result.bind (fun level ->
    dReq "text" __fs decTextSource |> Result.bind (fun text ->
    dReq "variant" __fs decHeadingVariant |> Result.bind (fun variant ->
    Ok { Level = level; Text = text; Variant = variant }))))

and private decBadgeSpec (j: JVal) : Result<BadgeSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "variant" __fs decBadgeVariant |> Result.bind (fun variant ->
    Ok { Label = label; Variant = variant })))

and private decButtonSpec (j: JVal) : Result<ButtonSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "disabled" __fs (decBinding dBool) |> Result.bind (fun disabled ->
    dOpt "icon" __fs dStr |> Result.bind (fun icon ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "onClick" __fs decAction |> Result.bind (fun onClick ->
    dReq "variant" __fs decButtonVariant |> Result.bind (fun variant ->
    Ok { Disabled = disabled; Icon = icon; Label = label; OnClick = onClick; Variant = variant }))))))

and private decMetricSpec (j: JVal) : Result<MetricSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dDef "emphasis" __fs decEmphasis (Emphasis.Normal) |> Result.bind (fun emphasis ->
    dReq "format" __fs decFormat |> Result.bind (fun format ->
    dOpt "icon" __fs dStr |> Result.bind (fun icon ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dOpt "subtext" __fs decTextSource |> Result.bind (fun subtext ->
    dDef "tone" __fs decToneVariant (ToneVariant.Default) |> Result.bind (fun tone ->
    dOpt "trend" __fs (decBinding dFloat) |> Result.bind (fun trend ->
    dOpt "trendFormat" __fs decFormat |> Result.bind (fun trendFormat ->
    dReq "value" __fs (decBinding dFloat) |> Result.bind (fun value ->
    dDef "weight" __fs decStyleWeight (StyleWeight.Standard) |> Result.bind (fun weight ->
    Ok { Emphasis = emphasis; Format = format; Icon = icon; Label = label; Subtext = subtext; Tone = tone; Trend = trend; TrendFormat = trendFormat; Value = value; Weight = weight })))))))))))

and private decBoxSpec (j: JVal) : Result<BoxSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dOpt "heading" __fs decTextSource |> Result.bind (fun heading ->
    dReq "layout" __fs decLayoutMode |> Result.bind (fun layout ->
    dReq "role" __fs decBoxRole |> Result.bind (fun role ->
    Ok { Children = children; Heading = heading; Layout = layout; Role = role })))))

and private decMarkdownSpec (j: JVal) : Result<MarkdownSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "text" __fs decTextSource |> Result.bind (fun text ->
    Ok { Text = text }))

and private decTabsSpec (j: JVal) : Result<TabsSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    Ok ((fun (_: string) -> box "<closure>")) |> Result.bind (fun onCommit ->
    (dPresent "onSelect" __fs |> Result.map (Option.map (fun () -> (fun (_: int) -> box "<closure>")))) |> Result.bind (fun onSelect ->
    Ok { Children = children; OnCommit = onCommit; OnSelect = onSelect }))))

/// Structural decode. The policy layer (diagnostics, §16 lenient-accept,
/// the reject set) composes ABOVE this — see the Phase 672 note in the generator.
let decodeNode (s: string) : Result<Node<obj>, string> =
    Json.parse s |> Result.bind decNode

let private witnessKindTag (n: Node<'Msg>) : string =
    match n.Kind with
    | NodeKind.Heading _ -> "Heading"
    | NodeKind.Badge _ -> "Badge"
    | NodeKind.Button _ -> "Button"
    | NodeKind.Metric _ -> "Metric"
    | NodeKind.Box _ -> "Box"
    | NodeKind.Markdown _ -> "Markdown"
    | NodeKind.Tabs _ -> "Tabs"

let private witnessChildren (n: Node<'Msg>) : Node<'Msg> list =
    match n.Kind with
    | NodeKind.Box s -> s.Children
    | NodeKind.Tabs s -> s.Children
    | _ -> []

let private witnessReplaceChildren (n: Node<'Msg>) (kids: Node<'Msg> list) : Node<'Msg> =
    match n.Kind with
    | NodeKind.Box s -> { n with Kind = NodeKind.Box { s with Children = kids } }
    | NodeKind.Tabs s -> { n with Kind = NodeKind.Tabs { s with Children = kids } }
    | _ -> n

let nodeWitness: NodeWitness<Node<'Msg>, string> =
    { Id = fun n -> n.Id
      KindTag = witnessKindTag
      Children = witnessChildren
      ReplaceChildren = witnessReplaceChildren }

// Validator scaffold — register domain RuleFamilies into `reg`; rule content stays domain-side.
let runValidator (reg: Validator.Registry<Node<'Msg>, string>) (root: Node<'Msg>) : Defect<string> list =
    Validator.runAll nodeWitness reg root

// Smart constructors — required-without-default fields are parameters; IDL-declared
// defaults are filled, other optionals default to None.

let mkHeading (id: string) (level: int) (text: TextSource) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Heading { Level = level; Text = text; Variant = HeadingVariant.Standard } }

let mkBadge (id: string) (label: TextSource) (variant: BadgeVariant) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Badge { Label = label; Variant = variant } }

let mkButton (id: string) (label: TextSource) (onClick: Action) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Button { Disabled = None; Icon = None; Label = label; OnClick = onClick; Variant = ButtonVariant.Primary } }

let mkMetric (id: string) (format: Format) (label: TextSource) (value: Binding<float>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Metric { Emphasis = Emphasis.Normal; Format = format; Icon = None; Label = label; Subtext = None; Tone = ToneVariant.Default; Trend = None; TrendFormat = None; Value = value; Weight = StyleWeight.Standard } }

let mkBox (id: string) (children: Node<'Msg> list) (layout: LayoutMode) (role: BoxRole) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Box { Children = children; Heading = None; Layout = layout; Role = role } }

let mkMarkdown (id: string) (text: TextSource) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Markdown { Text = text } }

let mkTabs (id: string) (children: Node<'Msg> list) (onCommit: (string -> 'Msg)) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Tabs { Children = children; OnCommit = onCommit; OnSelect = None } }