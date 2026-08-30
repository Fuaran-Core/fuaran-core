// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen (Phase 317 increment 3). Do not edit by hand.
module Fuaran.Core.Tests.DocGenerated

open Fuaran.Core

[<RequireQualifiedAccess>]
type Locale =
    | EnGB
    | EnUS

[<RequireQualifiedAccess>]
type Numbering =
    | NoNumbering
    | DecimalNumbering
    | LegalNumbering

[<RequireQualifiedAccess>]
type HeadingDepth =
    | H1
    | H2
    | H3
    | H4
    | H5
    | H6

[<RequireQualifiedAccess>]
type ListStyle =
    | Bulleted
    | Numbered
    | Lettered
    | Roman

[<RequireQualifiedAccess>]
type Run =
    | Text of value: string
    | Emphasis of runs: Run list
    | Strong of runs: Run list
    | InlineRef of target: string
    | InlineVariable of field: string
    | Link of text: string * url: string
    | Code of value: string

// structure
and DocumentSpec =
    {
      Title: string option
      Locale: Locale
      Numbering: Numbering
      Children: Node list
    }

// structure
and SectionSpec =
    {
      Heading: Run list
      Depth: HeadingDepth
      Children: Node list
    }

// leaf
and ParagraphSpec =
    {
      Runs: Run list
    }

// structure
and ListBlockSpec =
    {
      Style: ListStyle
      Children: Node list
    }

// structure
and ListItemSpec =
    {
      Children: Node list
    }

// structure
and TableSpec =
    {
      Caption: Run list option
      Children: Node list
    }

// structure
and RowSpec =
    {
      IsHeader: bool
      Children: Node list
    }

// leaf
and CellSpec =
    {
      Runs: Run list
    }

// structure
and FigureSpec =
    {
      Source: string
      Children: Node list
    }

// leaf
and CaptionSpec =
    {
      Runs: Run list
    }

// structure
and FootnoteSpec =
    {
      Children: Node list
    }

and [<RequireQualifiedAccess>] NodeKind =
    | Document of DocumentSpec
    | Section of SectionSpec
    | Paragraph of ParagraphSpec
    | ListBlock of ListBlockSpec
    | ListItem of ListItemSpec
    | Table of TableSpec
    | Row of RowSpec
    | Cell of CellSpec
    | Figure of FigureSpec
    | Caption of CaptionSpec
    | Footnote of FootnoteSpec

and Node = { Id: string; Kind: NodeKind }

let private encLocale (v: Locale) : JVal =
    match v with
    | Locale.EnGB -> JStr "EnGB"
    | Locale.EnUS -> JStr "EnUS"

let private encNumbering (v: Numbering) : JVal =
    match v with
    | Numbering.NoNumbering -> JStr "NoNumbering"
    | Numbering.DecimalNumbering -> JStr "DecimalNumbering"
    | Numbering.LegalNumbering -> JStr "LegalNumbering"

let private encHeadingDepth (v: HeadingDepth) : JVal =
    match v with
    | HeadingDepth.H1 -> JStr "H1"
    | HeadingDepth.H2 -> JStr "H2"
    | HeadingDepth.H3 -> JStr "H3"
    | HeadingDepth.H4 -> JStr "H4"
    | HeadingDepth.H5 -> JStr "H5"
    | HeadingDepth.H6 -> JStr "H6"

let private encListStyle (v: ListStyle) : JVal =
    match v with
    | ListStyle.Bulleted -> JStr "Bulleted"
    | ListStyle.Numbered -> JStr "Numbered"
    | ListStyle.Lettered -> JStr "Lettered"
    | ListStyle.Roman -> JStr "Roman"

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

// Phase 108 — `Canon.typed` under this vocabulary's DECLARED discriminator key.
let private typedTag (tag: string) (fields: (string * JVal) list) : JVal =
    JObj(("kind", JStr tag) :: fields)

let rec private encNodeKind (k: NodeKind) : JVal =
    match k with
    | NodeKind.Document s -> encDocumentSpec s
    | NodeKind.Section s -> encSectionSpec s
    | NodeKind.Paragraph s -> encParagraphSpec s
    | NodeKind.ListBlock s -> encListBlockSpec s
    | NodeKind.ListItem s -> encListItemSpec s
    | NodeKind.Table s -> encTableSpec s
    | NodeKind.Row s -> encRowSpec s
    | NodeKind.Cell s -> encCellSpec s
    | NodeKind.Figure s -> encFigureSpec s
    | NodeKind.Caption s -> encCaptionSpec s
    | NodeKind.Footnote s -> encFootnoteSpec s

and private encNode (n: Node) : JVal =
    let kind = encNodeKind n.Kind

    match kind with
    | JObj __kf -> JObj(("id", JStr n.Id) :: __kf)
    | __other -> __other

and private encRun (v: Run) : JVal =
    match v with
    | Run.Text value -> typedTag "Text" [ "value", JStr value ]
    | Run.Emphasis runs -> typedTag "Emphasis" [ "runs", JArr(List.map encRun runs) ]
    | Run.Strong runs -> typedTag "Strong" [ "runs", JArr(List.map encRun runs) ]
    | Run.InlineRef target -> typedTag "InlineRef" [ "target", JStr target ]
    | Run.InlineVariable field -> typedTag "InlineVariable" [ "field", JStr field ]
    | Run.Link (text, url) -> typedTag "Link" [ "text", JStr text; "url", JStr url ]
    | Run.Code value -> typedTag "Code" [ "value", JStr value ]

and private encDocumentSpec (s: DocumentSpec) : JVal =
    typedTag "Document" ([ (s.Title |> Option.map (fun v -> "title", JStr v)); Some("locale", encLocale s.Locale); Some("numbering", encNumbering s.Numbering); Some("children", JArr(List.map encNode s.Children)) ] |> List.choose id)

and private encSectionSpec (s: SectionSpec) : JVal =
    typedTag "Section" ([ Some("heading", JArr(List.map encRun s.Heading)); Some("depth", encHeadingDepth s.Depth); Some("children", JArr(List.map encNode s.Children)) ] |> List.choose id)

and private encParagraphSpec (s: ParagraphSpec) : JVal =
    typedTag "Paragraph" ([ Some("runs", JArr(List.map encRun s.Runs)) ] |> List.choose id)

and private encListBlockSpec (s: ListBlockSpec) : JVal =
    typedTag "ListBlock" ([ Some("style", encListStyle s.Style); Some("children", JArr(List.map encNode s.Children)) ] |> List.choose id)

and private encListItemSpec (s: ListItemSpec) : JVal =
    typedTag "ListItem" ([ Some("children", JArr(List.map encNode s.Children)) ] |> List.choose id)

and private encTableSpec (s: TableSpec) : JVal =
    typedTag "Table" ([ (s.Caption |> Option.map (fun v -> "caption", JArr(List.map encRun v))); Some("children", JArr(List.map encNode s.Children)) ] |> List.choose id)

and private encRowSpec (s: RowSpec) : JVal =
    typedTag "Row" ([ Some("isHeader", JBool s.IsHeader); Some("children", JArr(List.map encNode s.Children)) ] |> List.choose id)

and private encCellSpec (s: CellSpec) : JVal =
    typedTag "Cell" ([ Some("runs", JArr(List.map encRun s.Runs)) ] |> List.choose id)

and private encFigureSpec (s: FigureSpec) : JVal =
    typedTag "Figure" ([ Some("source", JStr s.Source); Some("children", JArr(List.map encNode s.Children)) ] |> List.choose id)

and private encCaptionSpec (s: CaptionSpec) : JVal =
    typedTag "Caption" ([ Some("runs", JArr(List.map encRun s.Runs)) ] |> List.choose id)

and private encFootnoteSpec (s: FootnoteSpec) : JVal =
    typedTag "Footnote" ([ Some("children", JArr(List.map encNode s.Children)) ] |> List.choose id)

let encodeNode (n: Node) : string = Canon.render (encNode n)

/// JVal-level accessors (Phase 694) — for host codecs that splice generated
/// encodings into a larger canonical document (e.g. a TreeOp codec).
let encodeNodeJson (n: Node) : JVal = encNode n

let encodeNodeKindJson (k: NodeKind) : JVal = encNodeKind k

let private dObj (j: JVal) : Result<(string * JVal) list, string> =
    match j with
    | JObj fs -> Ok fs
    | _ -> Error "expected an object"

let private dTag (fs: (string * JVal) list) : Result<string, string> =
    match fs |> List.tryFind (fun (k, _) -> k = "kind") with
    | Some(_, JStr t) -> Ok t
    | _ -> Error "missing or non-string kind"

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

let private decLocale (j: JVal) : Result<Locale, string> =
    match j with
    | JStr "EnGB" -> Ok Locale.EnGB
    | JStr "EnUS" -> Ok Locale.EnUS
    | _ -> Error "not a Locale"

let private decNumbering (j: JVal) : Result<Numbering, string> =
    match j with
    | JStr "NoNumbering" -> Ok Numbering.NoNumbering
    | JStr "DecimalNumbering" -> Ok Numbering.DecimalNumbering
    | JStr "LegalNumbering" -> Ok Numbering.LegalNumbering
    | _ -> Error "not a Numbering"

let private decHeadingDepth (j: JVal) : Result<HeadingDepth, string> =
    match j with
    | JStr "H1" -> Ok HeadingDepth.H1
    | JStr "H2" -> Ok HeadingDepth.H2
    | JStr "H3" -> Ok HeadingDepth.H3
    | JStr "H4" -> Ok HeadingDepth.H4
    | JStr "H5" -> Ok HeadingDepth.H5
    | JStr "H6" -> Ok HeadingDepth.H6
    | _ -> Error "not a HeadingDepth"

let private decListStyle (j: JVal) : Result<ListStyle, string> =
    match j with
    | JStr "Bulleted" -> Ok ListStyle.Bulleted
    | JStr "Numbered" -> Ok ListStyle.Numbered
    | JStr "Lettered" -> Ok ListStyle.Lettered
    | JStr "Roman" -> Ok ListStyle.Roman
    | _ -> Error "not a ListStyle"

let rec private decNodeKind (j: JVal) : Result<NodeKind, string> =
    dObj j |> Result.bind (fun __fs ->
    dTag __fs |> Result.bind (fun __t ->
    match __t with
    | "Document" -> decDocumentSpec j |> Result.map NodeKind.Document
    | "Section" -> decSectionSpec j |> Result.map NodeKind.Section
    | "Paragraph" -> decParagraphSpec j |> Result.map NodeKind.Paragraph
    | "ListBlock" -> decListBlockSpec j |> Result.map NodeKind.ListBlock
    | "ListItem" -> decListItemSpec j |> Result.map NodeKind.ListItem
    | "Table" -> decTableSpec j |> Result.map NodeKind.Table
    | "Row" -> decRowSpec j |> Result.map NodeKind.Row
    | "Cell" -> decCellSpec j |> Result.map NodeKind.Cell
    | "Figure" -> decFigureSpec j |> Result.map NodeKind.Figure
    | "Caption" -> decCaptionSpec j |> Result.map NodeKind.Caption
    | "Footnote" -> decFootnoteSpec j |> Result.map NodeKind.Footnote
    | __other -> Error ("unknown node kind: " + __other)))

and private decNode (j: JVal) : Result<Node, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "id" __fs dStr |> Result.bind (fun id ->
    decNodeKind j |> Result.bind (fun kind ->
    Ok { Id = id; Kind = kind })))

and private decRun (j: JVal) : Result<Run, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "kind")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Text" ->
            dReq "value" __fs dStr |> Result.bind (fun value ->
            Ok(Run.Text(value)))
        | "Emphasis" ->
            dReq "runs" __fs (dList decRun) |> Result.bind (fun runs ->
            Ok(Run.Emphasis(runs)))
        | "Strong" ->
            dReq "runs" __fs (dList decRun) |> Result.bind (fun runs ->
            Ok(Run.Strong(runs)))
        | "InlineRef" ->
            dReq "target" __fs dStr |> Result.bind (fun target ->
            Ok(Run.InlineRef(target)))
        | "InlineVariable" ->
            dReq "field" __fs dStr |> Result.bind (fun field ->
            Ok(Run.InlineVariable(field)))
        | "Link" ->
            dReq "text" __fs dStr |> Result.bind (fun text ->
            dReq "url" __fs dStr |> Result.bind (fun url ->
            Ok(Run.Link(text, url))))
        | "Code" ->
            dReq "value" __fs dStr |> Result.bind (fun value ->
            Ok(Run.Code(value)))
        | __other -> Error ("unknown Run case: " + __other))
    | _ -> Error "expected a Run object"

and private decDocumentSpec (j: JVal) : Result<DocumentSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "title" __fs dStr |> Result.bind (fun title ->
    dReq "locale" __fs decLocale |> Result.bind (fun locale ->
    dReq "numbering" __fs decNumbering |> Result.bind (fun numbering ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    Ok { Title = title; Locale = locale; Numbering = numbering; Children = children })))))

and private decSectionSpec (j: JVal) : Result<SectionSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "heading" __fs (dList decRun) |> Result.bind (fun heading ->
    dReq "depth" __fs decHeadingDepth |> Result.bind (fun depth ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    Ok { Heading = heading; Depth = depth; Children = children }))))

and private decParagraphSpec (j: JVal) : Result<ParagraphSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "runs" __fs (dList decRun) |> Result.bind (fun runs ->
    Ok { Runs = runs }))

and private decListBlockSpec (j: JVal) : Result<ListBlockSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "style" __fs decListStyle |> Result.bind (fun style ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    Ok { Style = style; Children = children })))

and private decListItemSpec (j: JVal) : Result<ListItemSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    Ok { Children = children }))

and private decTableSpec (j: JVal) : Result<TableSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "caption" __fs (dList decRun) |> Result.bind (fun caption ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    Ok { Caption = caption; Children = children })))

and private decRowSpec (j: JVal) : Result<RowSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "isHeader" __fs dBool |> Result.bind (fun isHeader ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    Ok { IsHeader = isHeader; Children = children })))

and private decCellSpec (j: JVal) : Result<CellSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "runs" __fs (dList decRun) |> Result.bind (fun runs ->
    Ok { Runs = runs }))

and private decFigureSpec (j: JVal) : Result<FigureSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "source" __fs dStr |> Result.bind (fun source ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    Ok { Source = source; Children = children })))

and private decCaptionSpec (j: JVal) : Result<CaptionSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "runs" __fs (dList decRun) |> Result.bind (fun runs ->
    Ok { Runs = runs }))

and private decFootnoteSpec (j: JVal) : Result<FootnoteSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    Ok { Children = children }))

/// Structural decode. The policy layer (diagnostics, §16 lenient-accept,
/// the reject set) composes ABOVE this — see the Phase 672 note in the generator.
let decodeNode (s: string) : Result<Node, string> =
    Json.parse s |> Result.bind decNode

let private witnessKindTag (n: Node) : string =
    match n.Kind with
    | NodeKind.Document _ -> "Document"
    | NodeKind.Section _ -> "Section"
    | NodeKind.Paragraph _ -> "Paragraph"
    | NodeKind.ListBlock _ -> "ListBlock"
    | NodeKind.ListItem _ -> "ListItem"
    | NodeKind.Table _ -> "Table"
    | NodeKind.Row _ -> "Row"
    | NodeKind.Cell _ -> "Cell"
    | NodeKind.Figure _ -> "Figure"
    | NodeKind.Caption _ -> "Caption"
    | NodeKind.Footnote _ -> "Footnote"

let private witnessChildren (n: Node) : Node list =
    match n.Kind with
    | NodeKind.Document s -> s.Children
    | NodeKind.Section s -> s.Children
    | NodeKind.ListBlock s -> s.Children
    | NodeKind.ListItem s -> s.Children
    | NodeKind.Table s -> s.Children
    | NodeKind.Row s -> s.Children
    | NodeKind.Figure s -> s.Children
    | NodeKind.Footnote s -> s.Children
    | _ -> []

let private witnessReplaceChildren (n: Node) (kids: Node list) : Node =
    match n.Kind with
    | NodeKind.Document s -> { n with Kind = NodeKind.Document { s with Children = kids } }
    | NodeKind.Section s -> { n with Kind = NodeKind.Section { s with Children = kids } }
    | NodeKind.ListBlock s -> { n with Kind = NodeKind.ListBlock { s with Children = kids } }
    | NodeKind.ListItem s -> { n with Kind = NodeKind.ListItem { s with Children = kids } }
    | NodeKind.Table s -> { n with Kind = NodeKind.Table { s with Children = kids } }
    | NodeKind.Row s -> { n with Kind = NodeKind.Row { s with Children = kids } }
    | NodeKind.Figure s -> { n with Kind = NodeKind.Figure { s with Children = kids } }
    | NodeKind.Footnote s -> { n with Kind = NodeKind.Footnote { s with Children = kids } }
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

let mkDocument (id: string) (locale: Locale) (numbering: Numbering) (children: Node list) : Node =
    { Id = id; Kind = NodeKind.Document { Title = None; Locale = locale; Numbering = numbering; Children = children } }

let mkSection (id: string) (heading: Run list) (depth: HeadingDepth) (children: Node list) : Node =
    { Id = id; Kind = NodeKind.Section { Heading = heading; Depth = depth; Children = children } }

let mkParagraph (id: string) (runs: Run list) : Node =
    { Id = id; Kind = NodeKind.Paragraph { Runs = runs } }

let mkListBlock (id: string) (style: ListStyle) (children: Node list) : Node =
    { Id = id; Kind = NodeKind.ListBlock { Style = style; Children = children } }

let mkListItem (id: string) (children: Node list) : Node =
    { Id = id; Kind = NodeKind.ListItem { Children = children } }

let mkTable (id: string) (children: Node list) : Node =
    { Id = id; Kind = NodeKind.Table { Caption = None; Children = children } }

let mkRow (id: string) (isHeader: bool) (children: Node list) : Node =
    { Id = id; Kind = NodeKind.Row { IsHeader = isHeader; Children = children } }

let mkCell (id: string) (runs: Run list) : Node =
    { Id = id; Kind = NodeKind.Cell { Runs = runs } }

let mkFigure (id: string) (source: string) (children: Node list) : Node =
    { Id = id; Kind = NodeKind.Figure { Source = source; Children = children } }

let mkCaption (id: string) (runs: Run list) : Node =
    { Id = id; Kind = NodeKind.Caption { Runs = runs } }

let mkFootnote (id: string) (children: Node list) : Node =
    { Id = id; Kind = NodeKind.Footnote { Children = children } }