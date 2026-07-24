// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen (Phase 317 increment 3). Do not edit by hand.
module Fuaran.Core.Idl.Spike.Generated

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

and [<RequireQualifiedAccess>] Format =
    | Currency of code: string
    | Percent of decimals: int

and [<RequireQualifiedAccess>] Action =
    | Chain of ops: Action list

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
and BoxSpec =
    {
      Children: Node list
      Heading: TextSource option
      Layout: LayoutMode
      Role: BoxRole
    }

// Display
and MarkdownSpec =
    {
      Text: TextSource
    }

and [<RequireQualifiedAccess>] NodeKind =
    | Heading of HeadingSpec
    | Badge of BadgeSpec
    | Button of ButtonSpec
    | Metric of MetricSpec
    | Box of BoxSpec
    | Markdown of MarkdownSpec

and Node = { Id: string; Kind: NodeKind }

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

let rec private encNode (n: Node) : JVal =
    let kind =
        match n.Kind with
        | NodeKind.Heading s -> encHeadingSpec s
        | NodeKind.Badge s -> encBadgeSpec s
        | NodeKind.Button s -> encButtonSpec s
        | NodeKind.Metric s -> encMetricSpec s
        | NodeKind.Box s -> encBoxSpec s
        | NodeKind.Markdown s -> encMarkdownSpec s

    JObj [ "id", JStr n.Id; "kind", kind ]

and private encTextSource (v: TextSource) : JVal =
    match v with
    | TextSource.Literal text -> JStr text

and private encBinding<'T> (encT: 'T -> JVal) (v: Binding<'T>) : JVal =
    match v with
    | Binding.Static value -> Canon.typed "Static" [ "value", encT value ]
    | Binding.State (defaultValue, key) -> Canon.typed "State" [ "defaultValue", encT defaultValue; "key", JStr key ]

and private encFormat (v: Format) : JVal =
    match v with
    | Format.Currency code -> Canon.typed "Currency" [ "code", JStr code ]
    | Format.Percent decimals -> Canon.typed "Percent" [ "decimals", JInt decimals ]

and private encAction (v: Action) : JVal =
    match v with
    | Action.Chain ops -> Canon.typed "Chain" [ "ops", JArr(List.map encAction ops) ]

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
    Canon.typed "Metric" ([ (if s.Emphasis = Emphasis.Normal then None else Some("emphasis", encEmphasis s.Emphasis)); Some("format", encFormat s.Format); (s.Icon |> Option.map (fun v -> "icon", JStr v)); Some("label", encTextSource s.Label); (s.Subtext |> Option.map (fun v -> "subtext", encTextSource v)); (if s.Tone = ToneVariant.Default then None else Some("tone", encToneVariant s.Tone)); (s.Trend |> Option.map (fun v -> "trend", (encBinding JFloat) v)); (s.TrendFormat |> Option.map (fun v -> "trendFormat", encFormat v)); Some("value", (encBinding JFloat) s.Value); (if s.Weight = StyleWeight.Standard then None else Some("weight", encStyleWeight s.Weight)) ] |> List.choose id)

and private encBoxSpec (s: BoxSpec) : JVal =
    Canon.typed "Box" ([ Some("children", JArr(List.map encNode s.Children)); (s.Heading |> Option.map (fun v -> "heading", encTextSource v)); Some("layout", encLayoutMode s.Layout); Some("role", encBoxRole s.Role) ] |> List.choose id)

and private encMarkdownSpec (s: MarkdownSpec) : JVal =
    Canon.typed "Markdown" ([ Some("text", encTextSource s.Text) ] |> List.choose id)

let encodeNode (n: Node) : string = Canon.render (encNode n)

let private witnessKindTag (n: Node) : string =
    match n.Kind with
    | NodeKind.Heading _ -> "Heading"
    | NodeKind.Badge _ -> "Badge"
    | NodeKind.Button _ -> "Button"
    | NodeKind.Metric _ -> "Metric"
    | NodeKind.Box _ -> "Box"
    | NodeKind.Markdown _ -> "Markdown"

let private witnessChildren (n: Node) : Node list =
    match n.Kind with
    | NodeKind.Box s -> s.Children
    | _ -> []

let private witnessReplaceChildren (n: Node) (kids: Node list) : Node =
    match n.Kind with
    | NodeKind.Box s -> { n with Kind = NodeKind.Box { s with Children = kids } }
    | _ -> n

let nodeWitness: NodeWitness<Node, string> =
    { Id = fun n -> n.Id
      KindTag = witnessKindTag
      Children = witnessChildren
      ReplaceChildren = witnessReplaceChildren }

// Validator scaffold — register domain RuleFamilies into `reg`; rule content stays domain-side.
let runValidator (reg: Validator.Registry<Node, string>) (root: Node) : Defect<string> list =
    Validator.runAll nodeWitness reg root

// Smart constructors — required-without-default fields are parameters; IDL-declared
// defaults are filled, other optionals default to None.

let mkHeading (id: string) (level: int) (text: TextSource) : Node =
    { Id = id; Kind = NodeKind.Heading { Level = level; Text = text; Variant = HeadingVariant.Standard } }

let mkBadge (id: string) (label: TextSource) (variant: BadgeVariant) : Node =
    { Id = id; Kind = NodeKind.Badge { Label = label; Variant = variant } }

let mkButton (id: string) (label: TextSource) (onClick: Action) : Node =
    { Id = id; Kind = NodeKind.Button { Disabled = None; Icon = None; Label = label; OnClick = onClick; Variant = ButtonVariant.Primary } }

let mkMetric (id: string) (format: Format) (label: TextSource) (value: Binding<float>) : Node =
    { Id = id; Kind = NodeKind.Metric { Emphasis = Emphasis.Normal; Format = format; Icon = None; Label = label; Subtext = None; Tone = ToneVariant.Default; Trend = None; TrendFormat = None; Value = value; Weight = StyleWeight.Standard } }

let mkBox (id: string) (children: Node list) (layout: LayoutMode) (role: BoxRole) : Node =
    { Id = id; Kind = NodeKind.Box { Children = children; Heading = None; Layout = layout; Role = role } }

let mkMarkdown (id: string) (text: TextSource) : Node =
    { Id = id; Kind = NodeKind.Markdown { Text = text } }