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

// Phase 337 — a refusal is Core's `DecodeError`: a code from the closed `DecodeCode` set, the
// path from the value the outermost decoder was handed, what the position expected, and a
// sentence. The code and the path are the ones the IDL interpreter reports for the same
// document; the sentence is this layer's own.
let private dFail (code: DecodeCode) (expected: string) (message: string) : Result<'T, DecodeError> =
    Error(DecodeError.make code expected message)

// One step further from the root — what a refusal gains as it leaves a member or an item.
let private dUnder (step: PathSegment) (r: Result<'T, DecodeError>) : Result<'T, DecodeError> =
    match r with
    | Ok v -> Ok v
    | Error e -> Error(DecodeError.under step e)

let private dObj (j: JVal) : Result<(string * JVal) list, DecodeError> =
    match j with
    | JObj fs -> Ok fs
    | _ -> dFail DecodeCode.WrongKind "object" "expected an object"

// The discriminator: absent is `MissingField` naming it, a non-string `WrongKind` at it.
let private dTag (fs: (string * JVal) list) : Result<string, DecodeError> =
    match fs |> List.tryFind (fun (k, _) -> k = "$type") with
    | Some(_, JStr t) -> Ok t
    | Some _ -> dFail DecodeCode.WrongKind "string" "missing or non-string $type" |> dUnder (PathSegment.Key "$type")
    | None ->
        Error
            { Decoder.missing "$type" with
                Message = "missing or non-string $type" }

// A tag naming no case this decoder knows: `UnknownTag` at the discriminator.
let private dUnknown (expected: string) (message: string) : Result<'T, DecodeError> =
    dFail DecodeCode.UnknownTag expected message |> dUnder (PathSegment.Key "$type")

let private dStr (j: JVal) : Result<string, DecodeError> =
    match j with
    | JStr s -> Ok s
    | _ -> dFail DecodeCode.WrongKind "string" "expected a string"

let private dInt (j: JVal) : Result<int, DecodeError> =
    match j with
    | JInt i -> Ok i
    | _ -> dFail DecodeCode.WrongKind "int" "expected an int"

let private dBool (j: JVal) : Result<bool, DecodeError> =
    match j with
    | JBool b -> Ok b
    | _ -> dFail DecodeCode.WrongKind "bool" "expected a bool"

// A whole-valued float renders without a decimal point, so it parses back as JInt.
// WIRE_FORMAT §7 — a float slot also accepts the three quoted non-finite sentinels, which
// is how §5 spells a number JSON has no literal for. The value decodes to the FLOAT, never
// to the string: a host that answered the string would hand a consumer a different tree on
// the second decode while the bytes stayed identical. `dInt` is NOT widened — §7 stops at
// the float slot.
let private dFloat (j: JVal) : Result<float, DecodeError> =
    match j with
    | JFloat f -> Ok f
    | JInt i -> Ok(float i)
    | JStr "NaN" -> Ok System.Double.NaN
    | JStr "Infinity" -> Ok System.Double.PositiveInfinity
    | JStr "-Infinity" -> Ok System.Double.NegativeInfinity
    | _ -> dFail DecodeCode.WrongKind "number" "expected a number"

let private dUnit (_: JVal) : Result<unit, DecodeError> = Ok()

// Phase 676 — arbitrary JSON, kept verbatim. No shape check: the field's
// contract is that its content is not the schema's business.
let private dJson (j: JVal) : Result<JVal, DecodeError> = Ok j

let private dList (dec: JVal -> Result<'T, DecodeError>) (j: JVal) : Result<'T list, DecodeError> =
    match j with
    | JArr xs ->
        let rec go (i: int) (acc: 'T list) (rest: JVal list) =
            match rest with
            | [] -> Ok(List.rev acc)
            | x :: tail ->
                match dec x with
                | Ok v -> go (i + 1) (v :: acc) tail
                | Error e -> Error(DecodeError.under (PathSegment.Index i) e)

        go 0 [] xs
    | _ -> dFail DecodeCode.WrongKind "array" "expected an array"

let private dMap (dec: JVal -> Result<'T, DecodeError>) (j: JVal) : Result<Map<string, 'T>, DecodeError> =
    match j with
    | JObj fs ->
        (Ok [], fs)
        ||> List.fold (fun acc (k, v) ->
            match acc with
            | Error e -> Error e
            | Ok items -> dec v |> dUnder (PathSegment.Key k) |> Result.map (fun d -> (k, d) :: items))
        |> Result.map (List.rev >> Map.ofList)
    | _ -> dFail DecodeCode.WrongKind "object" "expected an object"

let private dReq (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, DecodeError>) : Result<'T, DecodeError> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v |> dUnder (PathSegment.Key name)
    | None ->
        Error
            { Decoder.missing name with
                Message = "missing required field '" + name + "'" }

let private dOpt (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, DecodeError>) : Result<'T option, DecodeError> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v |> dUnder (PathSegment.Key name) |> Result.map Some
    | None -> Ok None

let private dDef (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, DecodeError>) (dflt: 'T) : Result<'T, DecodeError> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v |> dUnder (PathSegment.Key name)
    | None -> Ok dflt

// An optional closure / opaque field: the value is a sentinel carrying nothing,
// but its PRESENCE distinguishes `Some ()` from `None` and must be read back.
let private dPresent (name: string) (fs: (string * JVal) list) : Result<unit option, DecodeError> =
    Ok(fs |> List.tryFind (fun (k, _) -> k = name) |> Option.map (fun _ -> ()))

// A hosted slot's codec answers a SENTENCE (`JVal -> Result<'host, string>`): its refusal is
// `OutOfRange` at the slot — any declared wire form has already been checked, so the value is
// of the kind the slot takes and the codec does not admit it.
let private dHosted (r: Result<'T, string>) : Result<'T, DecodeError> =
    match r with
    | Ok v -> Ok v
    | Error m -> dFail DecodeCode.OutOfRange "a value the slot's host codec admits" m

// A declared case refine answers a SENTENCE over the members it reads: `dRefine` binds the
// case's last member and lifts the refine's refusal to `OutOfRange` at the case's object —
// every member decoded, and together they are a value the case does not admit.
let private dRefine (f: 'T -> Result<'U, string>) (r: Result<'T, DecodeError>) : Result<'U, DecodeError> =
    match r with
    | Error e -> Error e
    | Ok v ->
        match f v with
        | Ok u -> Ok u
        | Error m -> dFail DecodeCode.OutOfRange "a case its refinement admits" m

let private decLayoutKind (j: JVal) : Result<LayoutKind, DecodeError> =
    match j with
    | JStr "Stack" -> Ok LayoutKind.Stack
    | JStr "Row" -> Ok LayoutKind.Row
    | JStr "Grid" -> Ok LayoutKind.Grid
    | JStr _ -> dFail DecodeCode.UnknownTag "one of 'Stack', 'Row', 'Grid'" "not a LayoutKind"
    | _ -> dFail DecodeCode.WrongKind "string" "not a LayoutKind"

let private decLevel (j: JVal) : Result<Level, DecodeError> =
    match j with
    | JStr "low" -> Ok Level.Low
    | JStr "medium" -> Ok Level.Medium
    | JStr "high" -> Ok Level.High
    | JStr _ -> dFail DecodeCode.UnknownTag "one of 'low', 'medium', 'high'" "not a Level"
    | _ -> dFail DecodeCode.WrongKind "string" "not a Level"

let private decStrictness (j: JVal) : Result<Strictness, DecodeError> =
    match j with
    | JStr "StrictReplay" -> Ok Strictness.StrictReplay
    | JStr "AdvisoryWarning" -> Ok Strictness.AdvisoryWarning
    | JStr _ -> dFail DecodeCode.UnknownTag "one of 'StrictReplay', 'AdvisoryWarning'" "not a Strictness"
    | _ -> dFail DecodeCode.WrongKind "string" "not a Strictness"

let rec private decNodeKind (j: JVal) : Result<NodeKind, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dTag __fs |> Result.bind (fun __t ->
    match __t with
    | "Embed" -> decEmbedSpec j |> Result.map NodeKind.Embed
    | "Group" -> decGroupSpec j |> Result.map NodeKind.Group
    | "Link" -> decLinkSpec j |> Result.map NodeKind.Link
    | "Measure" -> decMeasureSpec j |> Result.map NodeKind.Measure
    | "Note" -> decNoteSpec j |> Result.map NodeKind.Note
    | __other -> dUnknown "one of 'Embed', 'Group', 'Link', 'Measure', 'Note'" ("unknown node kind: " + __other)))

and private decNode (j: JVal) : Result<Node, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "id" __fs dStr |> Result.bind (fun id ->
    dReq "kind" __fs decNodeKind |> Result.bind (fun kind ->
    dOpt "hidden" __fs dBool |> Result.bind (fun hidden ->
    dOpt "label" __fs dStr |> Result.bind (fun label ->
    Ok { Id = id; Kind = kind; Hidden = hidden; Label = label })))))

and private decSlot<'T> (decT: JVal -> Result<'T, DecodeError>) (j: JVal) : Result<Slot<'T>, DecodeError> =
    match j with
    | JObj __fs ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Fixed" ->
            dReq "value" __fs decT |> Result.bind (fun value ->
            Ok(Slot.Fixed(value)))
        | "Ref" ->
            dReq "name" __fs dStr |> Result.bind (fun name ->
            Ok(Slot.Ref(name)))
        | __other -> dUnknown "one of 'Fixed', 'Ref'" ("unknown Slot case: " + __other))
    | _ -> dFail DecodeCode.WrongKind "object" "expected a Slot object"

and private decText (j: JVal) : Result<Text, DecodeError> =
    match j with
    | JObj __fs ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Inline" ->
            dReq "text" __fs dStr |> Result.bind (fun text ->
            Ok(Text.Inline(text)))
        | "Lookup" ->
            dOpt "args" __fs (dMap dStr) |> Result.bind (fun args ->
            dReq "key" __fs dStr |> dRefine (fun key ->
            Ok(Text.Lookup(args, key))))
        | __other -> dUnknown "one of 'Inline', 'Lookup'" ("unknown Text case: " + __other))
    | _ -> dFail DecodeCode.WrongKind "object" "expected a Text object"

and private decContentHash (j: JVal) : Result<ContentHash, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "hash" __fs dStr |> Result.bind (fun hash ->
    dReq "strictness" __fs decStrictness |> Result.bind (fun strictness ->
    Ok { Hash = hash; Strictness = strictness })))

and private decPoint (j: JVal) : Result<Point, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "x" __fs dFloat |> Result.bind (fun x ->
    dReq "y" __fs dFloat |> Result.bind (fun y ->
    Ok { X = x; Y = y })))

and private decEmbedSpec (j: JVal) : Result<EmbedSpec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "componentId" __fs dStr |> Result.bind (fun componentId ->
    dOpt "contentHash" __fs decContentHash |> Result.bind (fun contentHash ->
    dReq "moduleId" __fs dStr |> Result.bind (fun moduleId ->
    Ok (ignore) |> Result.bind (fun onMount ->
    dOpt "props" __fs (dMap dJson) |> Result.bind (fun props ->
    Ok { ComponentId = componentId; ContentHash = contentHash; ModuleId = moduleId; OnMount = onMount; Props = props }))))))

and private decGroupSpec (j: JVal) : Result<GroupSpec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dDef "layout" __fs decLayoutKind (LayoutKind.Stack) |> Result.bind (fun layout ->
    Ok (ignore) |> Result.bind (fun onSelect ->
    Ok { Children = children; Layout = layout; OnSelect = onSelect }))))

and private decLinkSpec (j: JVal) : Result<LinkSpec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "href" __fs (decSlot dStr) |> Result.bind (fun href ->
    dReq "label" __fs decText |> Result.bind (fun label ->
    Ok() |> Result.bind (fun onClick ->
    Ok { Href = href; Label = label; OnClick = onClick }))))

and private decMeasureSpec (j: JVal) : Result<MeasureSpec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs decText |> Result.bind (fun label ->
    dDef "level" __fs decLevel (Level.Low) |> Result.bind (fun level ->
    dOpt "origin" __fs decPoint |> Result.bind (fun origin ->
    Ok() |> Result.bind (fun raw ->
    dReq "series" __fs (fun (__j: JVal) -> dHosted ((decSeries) __j)) |> Result.bind (fun series ->
    dDef "value" __fs (decSlot dFloat) (Slot.Fixed(0.0)) |> Result.bind (fun value ->
    Ok { Label = label; Level = level; Origin = origin; Raw = raw; Series = series; Value = value })))))))

and private decNoteSpec (j: JVal) : Result<NoteSpec, DecodeError> =
        dObj j |> Result.bind (fun fs -> dReq "body" fs decText) |> Result.map (fun t -> { Body = t })

and private decReferenceMarker (j: JVal) : Result<ReferenceMarker, string> =
        jprop "note" j |> Result.bind jstr |> Result.map (fun n -> { Note = n })

/// Structural decode. The policy layer (diagnostics, §16 lenient-accept,
/// the reject set) composes ABOVE this — see the Phase 672 note in the generator.
/// A refusal is Core's `DecodeError` (Phase 337): the code and path the IDL interpreter
/// reports for the same document, and this layer's sentence (`DecodeError.describe`).
let decodeNode (s: string) : Result<Node, DecodeError> =
    Decoder.parse s |> Result.bind decNode

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