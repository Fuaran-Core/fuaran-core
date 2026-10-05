// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen 0.35.1. Do not edit by hand.
module Fuaran.Core.Tests.DocAnnotatedGenerated
#nowarn "44" // this layer implements every declared member, including deprecated ones

open Fuaran.Core

[<RequireQualifiedAccess>]
type Tone =
    | Quiet
    /// <summary>
    /// &lt;'T> is not a tag here: Option&lt;'T> &amp; friends.
    /// </summary>
    | Loud

[<RequireQualifiedAccess>]
type Src =
    /// The literal case: the value is the text.
    | Lit of value: string
    /// A by-name reference to another note.
    /// **Deprecated.** Use `Lit` instead.
    /// resolve the reference before encoding.
    /// **In-process only** — this member has no wire projection: a value here
    /// is carried inside one host process and is LOST across any wire boundary.
    /// Since `0.2.0`.
    | [<System.Obsolete("deprecated — use `Lit` instead: resolve the reference before encoding.; in-process only — no wire projection; a value here is lost across a wire boundary", false)>] Ref of target: string

and Pair =
    {
      /// The left half, < the right.
      Left: string
      Right: string
    }

// leaf
/// A short note.
/// Renders as one paragraph & never wraps <'T>.
and NoteSpec =
    {
      /// Holds a List<'T> & a <b>bold</b> claim.
      /// See javascript:alert(1) <script>x</script>.
      ///
      /// Tab	here, bell �, lone � end.
      Label: string
      Src: Src option
      Tone: Tone
      Pair: Pair option
    }

and [<RequireQualifiedAccess>] NodeKind =
    | Note of NoteSpec

and Node = { Id: string; Kind: NodeKind }

let private encTone (v: Tone) : JVal =
    match v with
    | Tone.Quiet -> JStr "Quiet"
    | Tone.Loud -> JStr "Loud"

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
    | NodeKind.Note s -> encNoteSpec s

and private encNode (n: Node) : JVal =
    let kind = encNodeKind n.Kind

    JObj [ "id", JStr n.Id; "kind", kind ]

and private encSrc (v: Src) : JVal =
    match v with
    | Src.Lit value -> Canon.typed "Lit" [ "value", JStr value ]
    | Src.Ref target -> Canon.typed "Ref" [ "target", JStr target ]

and private encPair (s: Pair) : JVal =
    JObj([ Some("left", JStr s.Left); Some("right", JStr s.Right) ] |> List.choose id)

and private encNoteSpec (s: NoteSpec) : JVal =
    Canon.typed "Note" ([ Some("label", JStr s.Label); (s.Src |> Option.map (fun v -> "src", encSrc v)); Some("tone", encTone s.Tone); (s.Pair |> Option.map (fun v -> "pair", encPair v)) ] |> List.choose id)

let encodeNode (n: Node) : string = Canon.render (encNode n)

/// JVal-level accessors (Phase 694) — for host codecs that splice generated
/// encodings into a larger canonical document (e.g. a TreeOp codec).
let encodeNodeJson (n: Node) : JVal = encNode n

let encodeNodeKindJson (k: NodeKind) : JVal = encNodeKind k

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

// Phase 347 — a closure / opaque slot holds one fixed sentinel string, read BY VALUE as the
// interpreter reads it: another string is `OutOfRange` (a string, but not the one value the slot
// takes), any other kind `WrongKind`. The sentinel carries nothing, so it decodes to `()`.
let private dSentinel (sentinel: string) (j: JVal) : Result<unit, DecodeError> =
    match j with
    | JStr s when s = sentinel -> Ok()
    | JStr _ -> dFail DecodeCode.OutOfRange "string" ("expected the sentinel " + sentinel)
    | _ -> dFail DecodeCode.WrongKind "string" "expected a string"

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

// Every entry is checked, in document order; a repeated key keeps its FIRST value, as every
// member read does (Phase 347 — `Map.ofList` kept the last, as `JSON.parse` does).
let private dMap (dec: JVal -> Result<'T, DecodeError>) (j: JVal) : Result<Map<string, 'T>, DecodeError> =
    match j with
    | JObj fs ->
        (Ok Map.empty, fs)
        ||> List.fold (fun acc (k, v) ->
            match acc with
            | Error e -> Error e
            | Ok items ->
                dec v
                |> dUnder (PathSegment.Key k)
                |> Result.map (fun d -> if Map.containsKey k items then items else Map.add k d items))
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

let private decTone (j: JVal) : Result<Tone, DecodeError> =
    match j with
    | JStr "Quiet" -> Ok Tone.Quiet
    | JStr "Loud" -> Ok Tone.Loud
    | JStr _ -> dFail DecodeCode.UnknownTag "one of 'Quiet', 'Loud'" "not a Tone"
    | _ -> dFail DecodeCode.WrongKind "string" "not a Tone"

let rec private decNodeKind (j: JVal) : Result<NodeKind, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dTag __fs |> Result.bind (fun __t ->
    match __t with
    | "Note" -> decNoteSpec j |> Result.map NodeKind.Note
    | __other -> dUnknown "one of 'Note'" ("unknown node kind: " + __other)))

and private decNode (j: JVal) : Result<Node, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "id" __fs dStr |> Result.bind (fun id ->
    dReq "kind" __fs decNodeKind |> Result.bind (fun kind ->
    Ok { Id = id; Kind = kind })))

and private decSrc (j: JVal) : Result<Src, DecodeError> =
    match j with
    | JObj __fs ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Lit" ->
            dReq "value" __fs dStr |> Result.bind (fun value ->
            Ok(Src.Lit(value)))
        | "Ref" ->
            dReq "target" __fs dStr |> Result.bind (fun target ->
            Ok(Src.Ref(target)))
        | __other -> dUnknown "one of 'Lit', 'Ref'" ("unknown Src case: " + __other))
    | _ -> dFail DecodeCode.WrongKind "object" "expected a Src object"

and private decPair (j: JVal) : Result<Pair, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "left" __fs dStr |> Result.bind (fun left ->
    dReq "right" __fs dStr |> Result.bind (fun right ->
    Ok { Left = left; Right = right })))

and private decNoteSpec (j: JVal) : Result<NoteSpec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs dStr |> Result.bind (fun label ->
    dOpt "src" __fs decSrc |> Result.bind (fun src ->
    dReq "tone" __fs decTone |> Result.bind (fun tone ->
    dOpt "pair" __fs decPair |> Result.bind (fun pair ->
    Ok { Label = label; Src = src; Tone = tone; Pair = pair })))))

/// Structural decode. The policy layer (diagnostics, §16 lenient-accept,
/// the reject set) composes ABOVE this — see the Phase 672 note in the generator.
/// A refusal is Core's `DecodeError` (Phase 337): the code and path the IDL interpreter
/// reports for the same document, and this layer's sentence (`DecodeError.describe`).
let decodeNode (s: string) : Result<Node, DecodeError> =
    Decoder.parse s |> Result.bind decNode

let private witnessKindTag (n: Node) : string =
    match n.Kind with
    | NodeKind.Note _ -> "Note"

let private witnessChildren (n: Node) : Node list =
    match n.Kind with
    | _ -> []

let private witnessReplaceChildren (n: Node) (kids: Node list) : Node =
    match n.Kind with
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

let mkNote (id: string) (label: string) (tone: Tone) : Node =
    { Id = id; Kind = NodeKind.Note { Label = label; Src = None; Tone = tone; Pair = None } }