// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen 0.35.2. Do not edit by hand.
module Fuaran.Core.Tests.RefusalCornersGenerated

open Fuaran.Core

// corner
type CornerSpec =
    {
      Count: int
      Counts: Map<string, int>
      Items: int list
      Meta: JVal
      OnClick: unit
      OnHover: unit option
      OnPick: (int -> unit)
      Ratio: float
      Raw: unit
    }

and [<RequireQualifiedAccess>] NodeKind =
    | Corner of CornerSpec

and Node = { Id: string; Kind: NodeKind }

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
    | NodeKind.Corner s -> encCornerSpec s

and private encNode (n: Node) : JVal =
    let kind = encNodeKind n.Kind

    match kind with
    | JObj(__d :: __kf) -> JObj(__d :: ("id", JStr n.Id) :: __kf)
    | __other -> __other

and private encCornerSpec (s: CornerSpec) : JVal =
    typedTag "Corner" ([ Some("count", JInt s.Count); Some("counts", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, JInt v))) s.Counts); Some("items", JArr(List.map JInt s.Items)); Some("meta", id s.Meta); Some("onClick", JStr "<closure>"); (s.OnHover |> Option.map (fun v -> "onHover", JStr "<closure>")); Some("onPick", JStr "<closure>"); Some("ratio", encFloat s.Ratio); Some("raw", JStr "<opaque>") ] |> List.choose id)

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
    match fs |> List.tryFind (fun (k, _) -> k = "kind") with
    | Some(_, JStr t) -> Ok t
    | Some _ -> dFail DecodeCode.WrongKind "string" "missing or non-string kind" |> dUnder (PathSegment.Key "kind")
    | None ->
        Error
            { Decoder.missing "kind" with
                Message = "missing or non-string kind" }

// A tag naming no case this decoder knows: `UnknownTag` at the discriminator.
let private dUnknown (expected: string) (message: string) : Result<'T, DecodeError> =
    dFail DecodeCode.UnknownTag expected message |> dUnder (PathSegment.Key "kind")

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

let rec private decNodeKind (j: JVal) : Result<NodeKind, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dTag __fs |> Result.bind (fun __t ->
    match __t with
    | "Corner" -> decCornerSpec j |> Result.map NodeKind.Corner
    | __other -> dUnknown "one of 'Corner'" ("unknown node kind: " + __other)))

and private decNode (j: JVal) : Result<Node, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "id" __fs dStr |> Result.bind (fun id ->
    decNodeKind j |> Result.bind (fun kind ->
    Ok { Id = id; Kind = kind })))

and private decCornerSpec (j: JVal) : Result<CornerSpec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "count" __fs dInt |> Result.bind (fun count ->
    dReq "counts" __fs (dMap dInt) |> Result.bind (fun counts ->
    dReq "items" __fs (dList dInt) |> Result.bind (fun items ->
    dReq "meta" __fs dJson |> Result.bind (fun meta ->
    dReq "onClick" __fs (dSentinel "<closure>") |> Result.bind (fun onClick ->
    dOpt "onHover" __fs (dSentinel "<closure>") |> Result.bind (fun onHover ->
    dReq "onPick" __fs (fun (__j: JVal) -> dSentinel "<closure>" __j |> Result.map (fun () -> ignore)) |> Result.bind (fun onPick ->
    dReq "ratio" __fs dFloat |> Result.bind (fun ratio ->
    dReq "raw" __fs (dSentinel "<opaque>") |> Result.bind (fun raw ->
    Ok { Count = count; Counts = counts; Items = items; Meta = meta; OnClick = onClick; OnHover = onHover; OnPick = onPick; Ratio = ratio; Raw = raw }))))))))))

/// Structural decode. The policy layer (diagnostics, §16 lenient-accept,
/// the reject set) composes ABOVE this — see the Phase 672 note in the generator.
/// A refusal is Core's `DecodeError` (Phase 337): the code and path the IDL interpreter
/// reports for the same document, and this layer's sentence (`DecodeError.describe`).
let decodeNode (s: string) : Result<Node, DecodeError> =
    Decoder.parse s |> Result.bind decNode

let private witnessKindTag (n: Node) : string =
    match n.Kind with
    | NodeKind.Corner _ -> "Corner"

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

let mkCorner (id: string) (count: int) (counts: Map<string, int>) (items: int list) (meta: JVal) (onClick: unit) (onPick: (int -> unit)) (ratio: float) (raw: unit) : Node =
    { Id = id; Kind = NodeKind.Corner { Count = count; Counts = counts; Items = items; Meta = meta; OnClick = onClick; OnHover = None; OnPick = onPick; Ratio = ratio; Raw = raw } }