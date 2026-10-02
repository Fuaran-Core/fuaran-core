// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen 0.34.0. Do not edit by hand.
module Fuaran.Core.Tests.ReferenceGenerated
#nowarn "44" // this layer implements every declared member, including deprecated ones

open Fuaran.Core

[<RequireQualifiedAccess>]
type LayoutKind =
    | Stack
    | Row
    | Grid

[<RequireQualifiedAccess>]
type Level =
    | Low
    | Medium
    | High

[<RequireQualifiedAccess>]
type Strictness =
    | StrictReplay
    | AdvisoryWarning

[<RequireQualifiedAccess>]
type Slot<'T> =
    /// A value supplied inline rather than resolved by name.
    | Fixed of value: 'T
    /// **Deprecated.** Use `Fixed` instead.
    /// A by-name slot cannot be resolved without a host registry.
    /// Since `0.1.0`.
    | [<System.Obsolete("deprecated — use `Fixed` instead: A by-name slot cannot be resolved without a host registry.", false)>] Ref of name: string

and [<RequireQualifiedAccess>] Text =
    | Inline of text: string
    | Lookup of args: Map<string, string> option * key: string

and ContentHash =
    {
      Hash: string
      Strictness: Strictness
    }

/// A point in the reference vocabulary's own coordinate space.
and Point =
    {
      X: float
      Y: float
    }

// meta
and [<CustomEquality; NoComparison>] EmbedSpec =
    {
      ComponentId: string
      ContentHash: ContentHash option
      ModuleId: string
      OnMount: (unit -> unit)
      Props: Map<string, JVal> option
    }
    // Phase 252 — wire equality: a host-only field is not on the wire, so it takes no part.
    override this.Equals(other: obj) =
        match other with
        | :? EmbedSpec as o -> this.ComponentId = o.ComponentId && this.ContentHash = o.ContentHash && this.ModuleId = o.ModuleId && this.Props = o.Props
        | _ -> false

    override this.GetHashCode() = hash (this.ComponentId, this.ContentHash, this.ModuleId, this.Props)

// layout
and GroupSpec =
    {
      Children: Node list
      Layout: LayoutKind
      OnSelect: (int -> unit)
    }

// content
and LinkSpec =
    {
      Href: Slot<string>
      Label: Text
      OnClick: unit
    }

// data
and MeasureSpec =
    {
      Label: Text
      Level: Level
      Origin: Point option
      Raw: unit
      Series: float list
      Value: Slot<float>
    }

// content
and NoteSpec =
    { Body: Text }

and [<RequireQualifiedAccess>] NodeKind =
    | Embed of EmbedSpec
    | Group of GroupSpec
    | Link of LinkSpec
    | Measure of MeasureSpec
    | Note of NoteSpec

and Node =
    {
      Id: string
      Kind: NodeKind
      Hidden: bool option
      Label: string option
    }

and ReferenceMarker = { Note: string }

let private encLayoutKind (v: LayoutKind) : JVal =
    match v with
    | LayoutKind.Stack -> JStr "Stack"
    | LayoutKind.Row -> JStr "Row"
    | LayoutKind.Grid -> JStr "Grid"

let private encLevel (v: Level) : JVal =
    match v with
    | Level.Low -> JStr "low"
    | Level.Medium -> JStr "medium"
    | Level.High -> JStr "high"

let private encStrictness (v: Strictness) : JVal =
    match v with
    | Strictness.StrictReplay -> JStr "StrictReplay"
    | Strictness.AdvisoryWarning -> JStr "AdvisoryWarning"

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

let rec private encNodeKind (k: NodeKind) : JVal =
    match k with
    | NodeKind.Embed s -> encEmbedSpec s
    | NodeKind.Group s -> encGroupSpec s
    | NodeKind.Link s -> encLinkSpec s
    | NodeKind.Measure s -> encMeasureSpec s
    | NodeKind.Note s -> encNoteSpec s

and private encNode (n: Node) : JVal =
    let kind = encNodeKind n.Kind

    JObj([ Some("id", JStr n.Id); Some("kind", kind); (n.Hidden |> Option.map (fun v -> "hidden", JBool v)); (n.Label |> Option.map (fun v -> "label", JStr v)) ] |> List.choose id)

and private encSlot<'T> (encT: 'T -> JVal) (v: Slot<'T>) : JVal =
    match v with
    | Slot.Fixed value -> Canon.typed "Fixed" [ "value", encT value ]
    | Slot.Ref name -> Canon.typed "Ref" [ "name", JStr name ]

and private encText (v: Text) : JVal =
    match v with
    | Text.Inline text -> Canon.typed "Inline" [ "text", JStr text ]
    | Text.Lookup (args, key) -> Canon.typed "Lookup" ([ (args |> Option.map (fun v -> "args", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, JStr v))) v)); Some("key", JStr key) ] |> List.choose id)

and private encContentHash (s: ContentHash) : JVal =
    JObj([ Some("hash", JStr s.Hash); Some("strictness", encStrictness s.Strictness) ] |> List.choose id)

and private encPoint (s: Point) : JVal =
    JObj([ Some("x", encFloat s.X); Some("y", encFloat s.Y) ] |> List.choose id)

and private encEmbedSpec (s: EmbedSpec) : JVal =
    Canon.typed "Embed" ([ Some("componentId", JStr s.ComponentId); (s.ContentHash |> Option.map (fun v -> "contentHash", encContentHash v)); Some("moduleId", JStr s.ModuleId); None; (s.Props |> Option.map (fun v -> "props", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, id v))) v)) ] |> List.choose id)

and private encGroupSpec (s: GroupSpec) : JVal =
    Canon.typed "Group" ([ Some("children", JArr(List.map encNode s.Children)); (if s.Layout = LayoutKind.Stack then None else Some("layout", encLayoutKind s.Layout)); Some("onSelect", JStr "<closure>") ] |> List.choose id)

and private encLinkSpec (s: LinkSpec) : JVal =
    Canon.typed "Link" ([ Some("href", (encSlot JStr) s.Href); Some("label", encText s.Label); Some("onClick", JStr "<closure>") ] |> List.choose id)

and private encMeasureSpec (s: MeasureSpec) : JVal =
    Canon.typed "Measure" ([ Some("label", encText s.Label); (if s.Level = Level.Low then None else Some("level", encLevel s.Level)); (s.Origin |> Option.map (fun v -> "origin", encPoint v)); Some("raw", JStr "<opaque>"); Some("series", encSeries s.Series); (match s.Value with | Slot.Fixed(0.0) -> None | _ -> Some("value", (encSlot encFloat) s.Value)) ] |> List.choose id)

and private encNoteSpec (s: NoteSpec) : JVal = Canon.typed "Note" [ "body", encText s.Body ]

and private encReferenceMarker (m: ReferenceMarker) : JVal = JObj [ "note", JStr m.Note ]

let encodeNode (n: Node) : string = Canon.render (encNode n)

/// JVal-level accessors (Phase 694) — for host codecs that splice generated
/// encodings into a larger canonical document (e.g. a TreeOp codec).
let encodeNodeJson (n: Node) : JVal = encNode n

let encodeNodeKindJson (k: NodeKind) : JVal = encNodeKind k

let referenceMarkerName = "reference"

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

let private decLayoutKind (j: JVal) : Result<LayoutKind, string> =
    match j with
    | JStr "Stack" -> Ok LayoutKind.Stack
    | JStr "Row" -> Ok LayoutKind.Row
    | JStr "Grid" -> Ok LayoutKind.Grid
    | _ -> Error "not a LayoutKind"

let private decLevel (j: JVal) : Result<Level, string> =
    match j with
    | JStr "low" -> Ok Level.Low
    | JStr "medium" -> Ok Level.Medium
    | JStr "high" -> Ok Level.High
    | _ -> Error "not a Level"

let private decStrictness (j: JVal) : Result<Strictness, string> =
    match j with
    | JStr "StrictReplay" -> Ok Strictness.StrictReplay
    | JStr "AdvisoryWarning" -> Ok Strictness.AdvisoryWarning
    | _ -> Error "not a Strictness"

let rec private decNodeKind (j: JVal) : Result<NodeKind, string> =
    dObj j |> Result.bind (fun __fs ->
    dTag __fs |> Result.bind (fun __t ->
    match __t with
    | "Embed" -> decEmbedSpec j |> Result.map NodeKind.Embed
    | "Group" -> decGroupSpec j |> Result.map NodeKind.Group
    | "Link" -> decLinkSpec j |> Result.map NodeKind.Link
    | "Measure" -> decMeasureSpec j |> Result.map NodeKind.Measure
    | "Note" -> decNoteSpec j |> Result.map NodeKind.Note
    | __other -> Error ("unknown node kind: " + __other)))

and private decNode (j: JVal) : Result<Node, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "id" __fs dStr |> Result.bind (fun id ->
    dReq "kind" __fs decNodeKind |> Result.bind (fun kind ->
    dOpt "hidden" __fs dBool |> Result.bind (fun hidden ->
    dOpt "label" __fs dStr |> Result.bind (fun label ->
    Ok { Id = id; Kind = kind; Hidden = hidden; Label = label })))))

and private decSlot<'T> (decT: JVal -> Result<'T, string>) (j: JVal) : Result<Slot<'T>, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Fixed" ->
            dReq "value" __fs decT |> Result.bind (fun value ->
            Ok(Slot.Fixed(value)))
        | "Ref" ->
            dReq "name" __fs dStr |> Result.bind (fun name ->
            Ok(Slot.Ref(name)))
        | __other -> Error ("unknown Slot case: " + __other))
    | _ -> Error "expected a Slot object"

and private decText (j: JVal) : Result<Text, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Inline" ->
            dReq "text" __fs dStr |> Result.bind (fun text ->
            Ok(Text.Inline(text)))
        | "Lookup" ->
            dOpt "args" __fs (dMap dStr) |> Result.bind (fun args ->
            dReq "key" __fs dStr |> Result.bind (fun key ->
            Ok(Text.Lookup(args, key))))
        | __other -> Error ("unknown Text case: " + __other))
    | _ -> Error "expected a Text object"

and private decContentHash (j: JVal) : Result<ContentHash, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "hash" __fs dStr |> Result.bind (fun hash ->
    dReq "strictness" __fs decStrictness |> Result.bind (fun strictness ->
    Ok { Hash = hash; Strictness = strictness })))

and private decPoint (j: JVal) : Result<Point, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "x" __fs dFloat |> Result.bind (fun x ->
    dReq "y" __fs dFloat |> Result.bind (fun y ->
    Ok { X = x; Y = y })))

and private decEmbedSpec (j: JVal) : Result<EmbedSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "componentId" __fs dStr |> Result.bind (fun componentId ->
    dOpt "contentHash" __fs decContentHash |> Result.bind (fun contentHash ->
    dReq "moduleId" __fs dStr |> Result.bind (fun moduleId ->
    Ok (ignore) |> Result.bind (fun onMount ->
    dOpt "props" __fs (dMap dJson) |> Result.bind (fun props ->
    Ok { ComponentId = componentId; ContentHash = contentHash; ModuleId = moduleId; OnMount = onMount; Props = props }))))))

and private decGroupSpec (j: JVal) : Result<GroupSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dDef "layout" __fs decLayoutKind (LayoutKind.Stack) |> Result.bind (fun layout ->
    Ok (ignore) |> Result.bind (fun onSelect ->
    Ok { Children = children; Layout = layout; OnSelect = onSelect }))))

and private decLinkSpec (j: JVal) : Result<LinkSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "href" __fs (decSlot dStr) |> Result.bind (fun href ->
    dReq "label" __fs decText |> Result.bind (fun label ->
    Ok() |> Result.bind (fun onClick ->
    Ok { Href = href; Label = label; OnClick = onClick }))))

and private decMeasureSpec (j: JVal) : Result<MeasureSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs decText |> Result.bind (fun label ->
    dDef "level" __fs decLevel (Level.Low) |> Result.bind (fun level ->
    dOpt "origin" __fs decPoint |> Result.bind (fun origin ->
    Ok() |> Result.bind (fun raw ->
    dReq "series" __fs decSeries |> Result.bind (fun series ->
    dDef "value" __fs (decSlot dFloat) (Slot.Fixed(0.0)) |> Result.bind (fun value ->
    Ok { Label = label; Level = level; Origin = origin; Raw = raw; Series = series; Value = value })))))))

and private decNoteSpec (j: JVal) : Result<NoteSpec, string> =
        jprop "body" j |> Result.bind decText |> Result.map (fun t -> { Body = t })

and private decReferenceMarker (j: JVal) : Result<ReferenceMarker, string> =
        jprop "note" j |> Result.bind jstr |> Result.map (fun n -> { Note = n })

/// Structural decode. The policy layer (diagnostics, §16 lenient-accept,
/// the reject set) composes ABOVE this — see the Phase 672 note in the generator.
let decodeNode (s: string) : Result<Node, string> =
    Json.parse s |> Result.bind decNode

let private witnessKindTag (n: Node) : string =
    match n.Kind with
    | NodeKind.Embed _ -> "Embed"
    | NodeKind.Group _ -> "Group"
    | NodeKind.Link _ -> "Link"
    | NodeKind.Measure _ -> "Measure"
    | NodeKind.Note _ -> "Note"

let private witnessChildren (n: Node) : Node list =
    match n.Kind with
    | NodeKind.Group s -> s.Children
    | _ -> []

let private witnessReplaceChildren (n: Node) (kids: Node list) : Node =
    match n.Kind with
    | NodeKind.Group s -> { n with Kind = NodeKind.Group { s with Children = kids } }
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

let mkEmbed (id: string) (componentId: string) (moduleId: string) : Node =
    { Id = id; Kind = NodeKind.Embed { ComponentId = componentId; ContentHash = None; ModuleId = moduleId; OnMount = ignore; Props = None }; Hidden = None; Label = None }

let mkGroup (id: string) (children: Node list) (onSelect: (int -> unit)) : Node =
    { Id = id; Kind = NodeKind.Group { Children = children; Layout = LayoutKind.Stack; OnSelect = onSelect }; Hidden = None; Label = None }

let mkLink (id: string) (href: Slot<string>) (label: Text) (onClick: unit) : Node =
    { Id = id; Kind = NodeKind.Link { Href = href; Label = label; OnClick = onClick }; Hidden = None; Label = None }

let mkMeasure (id: string) (label: Text) (raw: unit) (series: float list) : Node =
    { Id = id; Kind = NodeKind.Measure { Label = label; Level = Level.Low; Origin = None; Raw = raw; Series = series; Value = Slot.Fixed(0.0) }; Hidden = None; Label = None }

let mkNote (body: Text) : NoteSpec = { Body = body }