// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen 0.35.2. Do not edit by hand.
module Fuaran.Core.Tests.DecodeVectorsGenerated

open Fuaran.Core

[<RequireQualifiedAccess>]
type U11_1 =
    | circle of r: float
    | square of side: float

and [<RequireQualifiedAccess>] U12_1 =
    | circle of r: float
    | square of side: float

and [<RequireQualifiedAccess>] U13_1 =
    | circle of r: float
    | square of side: float

and [<RequireQualifiedAccess>] U14_1 =
    | circle of r: float
    | square of side: float

and [<RequireQualifiedAccess>] U15_1 =
    | circle of r: float
    | square of side: float

and R03_1 =
    {
      A: string
    }

and R04_2 =
    {
      B: int
    }

and R04_1 =
    {
      A: R04_2
    }

and R05_1 =
    {
      A: int
    }

and R06_1 =
    {
      Xs: int list
    }

and R07_1 =
    {
      A: int
    }

and R09_1 =
    {
      A: int option
    }

and R10_1 =
    {
      A: int option
    }

and R20_1 =
    {
      A: int
      B: string option
    }

and R23_1 =
    {
      X: int
      Y: int
    }

// decode
and V00Spec =
    {
      V: string
    }

// decode
and V01Spec =
    {
      V: int list
    }

// decode
and V03Spec =
    {
      V: R03_1
    }

// decode
and V04Spec =
    {
      V: R04_1
    }

// decode
and V05Spec =
    {
      V: R05_1
    }

// decode
and V06Spec =
    {
      V: R06_1
    }

// decode
and V07Spec =
    {
      V: R07_1
    }

// decode
and V09Spec =
    {
      V: R09_1
    }

// decode
and V10Spec =
    {
      V: R10_1
    }

// decode
and V11Spec =
    {
      V: U11_1
    }

// decode
and V12Spec =
    {
      V: U12_1
    }

// decode
and V13Spec =
    {
      V: U13_1
    }

// decode
and V14Spec =
    {
      V: U14_1
    }

// decode
and V15Spec =
    {
      V: U15_1
    }

// decode
and V20Spec =
    {
      V: R20_1
    }

// decode
and V23Spec =
    {
      V: R23_1
    }

and [<RequireQualifiedAccess>] NodeKind =
    | V00 of V00Spec
    | V01 of V01Spec
    | V03 of V03Spec
    | V04 of V04Spec
    | V05 of V05Spec
    | V06 of V06Spec
    | V07 of V07Spec
    | V09 of V09Spec
    | V10 of V10Spec
    | V11 of V11Spec
    | V12 of V12Spec
    | V13 of V13Spec
    | V14 of V14Spec
    | V15 of V15Spec
    | V20 of V20Spec
    | V23 of V23Spec

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
    | NodeKind.V00 s -> encV00Spec s
    | NodeKind.V01 s -> encV01Spec s
    | NodeKind.V03 s -> encV03Spec s
    | NodeKind.V04 s -> encV04Spec s
    | NodeKind.V05 s -> encV05Spec s
    | NodeKind.V06 s -> encV06Spec s
    | NodeKind.V07 s -> encV07Spec s
    | NodeKind.V09 s -> encV09Spec s
    | NodeKind.V10 s -> encV10Spec s
    | NodeKind.V11 s -> encV11Spec s
    | NodeKind.V12 s -> encV12Spec s
    | NodeKind.V13 s -> encV13Spec s
    | NodeKind.V14 s -> encV14Spec s
    | NodeKind.V15 s -> encV15Spec s
    | NodeKind.V20 s -> encV20Spec s
    | NodeKind.V23 s -> encV23Spec s

and private encNode (n: Node) : JVal =
    let kind = encNodeKind n.Kind

    match kind with
    | JObj(__d :: __kf) -> JObj(__d :: ("id", JStr n.Id) :: __kf)
    | __other -> __other

and private encU11_1 (v: U11_1) : JVal =
    match v with
    | U11_1.circle r -> typedTag "circle" [ "r", encFloat r ]
    | U11_1.square side -> typedTag "square" [ "side", encFloat side ]

and private encU12_1 (v: U12_1) : JVal =
    match v with
    | U12_1.circle r -> typedTag "circle" [ "r", encFloat r ]
    | U12_1.square side -> typedTag "square" [ "side", encFloat side ]

and private encU13_1 (v: U13_1) : JVal =
    match v with
    | U13_1.circle r -> typedTag "circle" [ "r", encFloat r ]
    | U13_1.square side -> typedTag "square" [ "side", encFloat side ]

and private encU14_1 (v: U14_1) : JVal =
    match v with
    | U14_1.circle r -> typedTag "circle" [ "r", encFloat r ]
    | U14_1.square side -> typedTag "square" [ "side", encFloat side ]

and private encU15_1 (v: U15_1) : JVal =
    match v with
    | U15_1.circle r -> typedTag "circle" [ "r", encFloat r ]
    | U15_1.square side -> typedTag "square" [ "side", encFloat side ]

and private encR03_1 (s: R03_1) : JVal =
    JObj([ Some("a", JStr s.A) ] |> List.choose id)

and private encR04_2 (s: R04_2) : JVal =
    JObj([ Some("b", JInt s.B) ] |> List.choose id)

and private encR04_1 (s: R04_1) : JVal =
    JObj([ Some("a", encR04_2 s.A) ] |> List.choose id)

and private encR05_1 (s: R05_1) : JVal =
    JObj([ Some("a", JInt s.A) ] |> List.choose id)

and private encR06_1 (s: R06_1) : JVal =
    JObj([ Some("xs", JArr(List.map JInt s.Xs)) ] |> List.choose id)

and private encR07_1 (s: R07_1) : JVal =
    JObj([ Some("a", JInt s.A) ] |> List.choose id)

and private encR09_1 (s: R09_1) : JVal =
    JObj([ (s.A |> Option.map (fun v -> "a", JInt v)) ] |> List.choose id)

and private encR10_1 (s: R10_1) : JVal =
    JObj([ (s.A |> Option.map (fun v -> "a", JInt v)) ] |> List.choose id)

and private encR20_1 (s: R20_1) : JVal =
    JObj([ Some("a", JInt s.A); (s.B |> Option.map (fun v -> "b", JStr v)) ] |> List.choose id)

and private encR23_1 (s: R23_1) : JVal =
    JObj([ Some("x", JInt s.X); Some("y", JInt s.Y) ] |> List.choose id)

and private encV00Spec (s: V00Spec) : JVal =
    typedTag "V00" ([ Some("v", JStr s.V) ] |> List.choose id)

and private encV01Spec (s: V01Spec) : JVal =
    typedTag "V01" ([ Some("v", JArr(List.map JInt s.V)) ] |> List.choose id)

and private encV03Spec (s: V03Spec) : JVal =
    typedTag "V03" ([ Some("v", encR03_1 s.V) ] |> List.choose id)

and private encV04Spec (s: V04Spec) : JVal =
    typedTag "V04" ([ Some("v", encR04_1 s.V) ] |> List.choose id)

and private encV05Spec (s: V05Spec) : JVal =
    typedTag "V05" ([ Some("v", encR05_1 s.V) ] |> List.choose id)

and private encV06Spec (s: V06Spec) : JVal =
    typedTag "V06" ([ Some("v", encR06_1 s.V) ] |> List.choose id)

and private encV07Spec (s: V07Spec) : JVal =
    typedTag "V07" ([ Some("v", encR07_1 s.V) ] |> List.choose id)

and private encV09Spec (s: V09Spec) : JVal =
    typedTag "V09" ([ Some("v", encR09_1 s.V) ] |> List.choose id)

and private encV10Spec (s: V10Spec) : JVal =
    typedTag "V10" ([ Some("v", encR10_1 s.V) ] |> List.choose id)

and private encV11Spec (s: V11Spec) : JVal =
    typedTag "V11" ([ Some("v", encU11_1 s.V) ] |> List.choose id)

and private encV12Spec (s: V12Spec) : JVal =
    typedTag "V12" ([ Some("v", encU12_1 s.V) ] |> List.choose id)

and private encV13Spec (s: V13Spec) : JVal =
    typedTag "V13" ([ Some("v", encU13_1 s.V) ] |> List.choose id)

and private encV14Spec (s: V14Spec) : JVal =
    typedTag "V14" ([ Some("v", encU14_1 s.V) ] |> List.choose id)

and private encV15Spec (s: V15Spec) : JVal =
    typedTag "V15" ([ Some("v", encU15_1 s.V) ] |> List.choose id)

and private encV20Spec (s: V20Spec) : JVal =
    typedTag "V20" ([ Some("v", encR20_1 s.V) ] |> List.choose id)

and private encV23Spec (s: V23Spec) : JVal =
    typedTag "V23" ([ Some("v", encR23_1 s.V) ] |> List.choose id)

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
    | "V00" -> decV00Spec j |> Result.map NodeKind.V00
    | "V01" -> decV01Spec j |> Result.map NodeKind.V01
    | "V03" -> decV03Spec j |> Result.map NodeKind.V03
    | "V04" -> decV04Spec j |> Result.map NodeKind.V04
    | "V05" -> decV05Spec j |> Result.map NodeKind.V05
    | "V06" -> decV06Spec j |> Result.map NodeKind.V06
    | "V07" -> decV07Spec j |> Result.map NodeKind.V07
    | "V09" -> decV09Spec j |> Result.map NodeKind.V09
    | "V10" -> decV10Spec j |> Result.map NodeKind.V10
    | "V11" -> decV11Spec j |> Result.map NodeKind.V11
    | "V12" -> decV12Spec j |> Result.map NodeKind.V12
    | "V13" -> decV13Spec j |> Result.map NodeKind.V13
    | "V14" -> decV14Spec j |> Result.map NodeKind.V14
    | "V15" -> decV15Spec j |> Result.map NodeKind.V15
    | "V20" -> decV20Spec j |> Result.map NodeKind.V20
    | "V23" -> decV23Spec j |> Result.map NodeKind.V23
    | __other -> dUnknown "one of 'V00', 'V01', 'V03', 'V04', 'V05', 'V06', 'V07', 'V09', 'V10', 'V11', 'V12', 'V13', 'V14', 'V15', 'V20', 'V23'" ("unknown node kind: " + __other)))

and private decNode (j: JVal) : Result<Node, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "id" __fs dStr |> Result.bind (fun id ->
    decNodeKind j |> Result.bind (fun kind ->
    Ok { Id = id; Kind = kind })))

and private decU11_1 (j: JVal) : Result<U11_1, DecodeError> =
    match j with
    | JObj __fs ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "circle" ->
            dReq "r" __fs dFloat |> Result.bind (fun r ->
            Ok(U11_1.circle(r)))
        | "square" ->
            dReq "side" __fs dFloat |> Result.bind (fun side ->
            Ok(U11_1.square(side)))
        | __other -> dUnknown "one of 'circle', 'square'" ("unknown U11_1 case: " + __other))
    | _ -> dFail DecodeCode.WrongKind "object" "expected a U11_1 object"

and private decU12_1 (j: JVal) : Result<U12_1, DecodeError> =
    match j with
    | JObj __fs ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "circle" ->
            dReq "r" __fs dFloat |> Result.bind (fun r ->
            Ok(U12_1.circle(r)))
        | "square" ->
            dReq "side" __fs dFloat |> Result.bind (fun side ->
            Ok(U12_1.square(side)))
        | __other -> dUnknown "one of 'circle', 'square'" ("unknown U12_1 case: " + __other))
    | _ -> dFail DecodeCode.WrongKind "object" "expected a U12_1 object"

and private decU13_1 (j: JVal) : Result<U13_1, DecodeError> =
    match j with
    | JObj __fs ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "circle" ->
            dReq "r" __fs dFloat |> Result.bind (fun r ->
            Ok(U13_1.circle(r)))
        | "square" ->
            dReq "side" __fs dFloat |> Result.bind (fun side ->
            Ok(U13_1.square(side)))
        | __other -> dUnknown "one of 'circle', 'square'" ("unknown U13_1 case: " + __other))
    | _ -> dFail DecodeCode.WrongKind "object" "expected a U13_1 object"

and private decU14_1 (j: JVal) : Result<U14_1, DecodeError> =
    match j with
    | JObj __fs ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "circle" ->
            dReq "r" __fs dFloat |> Result.bind (fun r ->
            Ok(U14_1.circle(r)))
        | "square" ->
            dReq "side" __fs dFloat |> Result.bind (fun side ->
            Ok(U14_1.square(side)))
        | __other -> dUnknown "one of 'circle', 'square'" ("unknown U14_1 case: " + __other))
    | _ -> dFail DecodeCode.WrongKind "object" "expected a U14_1 object"

and private decU15_1 (j: JVal) : Result<U15_1, DecodeError> =
    match j with
    | JObj __fs ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "circle" ->
            dReq "r" __fs dFloat |> Result.bind (fun r ->
            Ok(U15_1.circle(r)))
        | "square" ->
            dReq "side" __fs dFloat |> Result.bind (fun side ->
            Ok(U15_1.square(side)))
        | __other -> dUnknown "one of 'circle', 'square'" ("unknown U15_1 case: " + __other))
    | _ -> dFail DecodeCode.WrongKind "object" "expected a U15_1 object"

and private decR03_1 (j: JVal) : Result<R03_1, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "a" __fs dStr |> Result.bind (fun a ->
    Ok { A = a }))

and private decR04_2 (j: JVal) : Result<R04_2, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "b" __fs dInt |> Result.bind (fun b ->
    Ok { B = b }))

and private decR04_1 (j: JVal) : Result<R04_1, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "a" __fs decR04_2 |> Result.bind (fun a ->
    Ok { A = a }))

and private decR05_1 (j: JVal) : Result<R05_1, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "a" __fs dInt |> Result.bind (fun a ->
    Ok { A = a }))

and private decR06_1 (j: JVal) : Result<R06_1, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "xs" __fs (dList dInt) |> Result.bind (fun xs ->
    Ok { Xs = xs }))

and private decR07_1 (j: JVal) : Result<R07_1, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "a" __fs dInt |> Result.bind (fun a ->
    Ok { A = a }))

and private decR09_1 (j: JVal) : Result<R09_1, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "a" __fs dInt |> Result.bind (fun a ->
    Ok { A = a }))

and private decR10_1 (j: JVal) : Result<R10_1, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "a" __fs dInt |> Result.bind (fun a ->
    Ok { A = a }))

and private decR20_1 (j: JVal) : Result<R20_1, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "a" __fs dInt |> Result.bind (fun a ->
    dOpt "b" __fs dStr |> Result.bind (fun b ->
    Ok { A = a; B = b })))

and private decR23_1 (j: JVal) : Result<R23_1, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "x" __fs dInt |> Result.bind (fun x ->
    dReq "y" __fs dInt |> Result.bind (fun y ->
    Ok { X = x; Y = y })))

and private decV00Spec (j: JVal) : Result<V00Spec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "v" __fs dStr |> Result.bind (fun v ->
    Ok { V = v }))

and private decV01Spec (j: JVal) : Result<V01Spec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "v" __fs (dList dInt) |> Result.bind (fun v ->
    Ok { V = v }))

and private decV03Spec (j: JVal) : Result<V03Spec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "v" __fs decR03_1 |> Result.bind (fun v ->
    Ok { V = v }))

and private decV04Spec (j: JVal) : Result<V04Spec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "v" __fs decR04_1 |> Result.bind (fun v ->
    Ok { V = v }))

and private decV05Spec (j: JVal) : Result<V05Spec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "v" __fs decR05_1 |> Result.bind (fun v ->
    Ok { V = v }))

and private decV06Spec (j: JVal) : Result<V06Spec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "v" __fs decR06_1 |> Result.bind (fun v ->
    Ok { V = v }))

and private decV07Spec (j: JVal) : Result<V07Spec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "v" __fs decR07_1 |> Result.bind (fun v ->
    Ok { V = v }))

and private decV09Spec (j: JVal) : Result<V09Spec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "v" __fs decR09_1 |> Result.bind (fun v ->
    Ok { V = v }))

and private decV10Spec (j: JVal) : Result<V10Spec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "v" __fs decR10_1 |> Result.bind (fun v ->
    Ok { V = v }))

and private decV11Spec (j: JVal) : Result<V11Spec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "v" __fs decU11_1 |> Result.bind (fun v ->
    Ok { V = v }))

and private decV12Spec (j: JVal) : Result<V12Spec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "v" __fs decU12_1 |> Result.bind (fun v ->
    Ok { V = v }))

and private decV13Spec (j: JVal) : Result<V13Spec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "v" __fs decU13_1 |> Result.bind (fun v ->
    Ok { V = v }))

and private decV14Spec (j: JVal) : Result<V14Spec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "v" __fs decU14_1 |> Result.bind (fun v ->
    Ok { V = v }))

and private decV15Spec (j: JVal) : Result<V15Spec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "v" __fs decU15_1 |> Result.bind (fun v ->
    Ok { V = v }))

and private decV20Spec (j: JVal) : Result<V20Spec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "v" __fs decR20_1 |> Result.bind (fun v ->
    Ok { V = v }))

and private decV23Spec (j: JVal) : Result<V23Spec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "v" __fs decR23_1 |> Result.bind (fun v ->
    Ok { V = v }))

/// Structural decode. The policy layer (diagnostics, §16 lenient-accept,
/// the reject set) composes ABOVE this — see the Phase 672 note in the generator.
/// A refusal is Core's `DecodeError` (Phase 337): the code and path the IDL interpreter
/// reports for the same document, and this layer's sentence (`DecodeError.describe`).
let decodeNode (s: string) : Result<Node, DecodeError> =
    Decoder.parse s |> Result.bind decNode

let private witnessKindTag (n: Node) : string =
    match n.Kind with
    | NodeKind.V00 _ -> "V00"
    | NodeKind.V01 _ -> "V01"
    | NodeKind.V03 _ -> "V03"
    | NodeKind.V04 _ -> "V04"
    | NodeKind.V05 _ -> "V05"
    | NodeKind.V06 _ -> "V06"
    | NodeKind.V07 _ -> "V07"
    | NodeKind.V09 _ -> "V09"
    | NodeKind.V10 _ -> "V10"
    | NodeKind.V11 _ -> "V11"
    | NodeKind.V12 _ -> "V12"
    | NodeKind.V13 _ -> "V13"
    | NodeKind.V14 _ -> "V14"
    | NodeKind.V15 _ -> "V15"
    | NodeKind.V20 _ -> "V20"
    | NodeKind.V23 _ -> "V23"

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

let mkV00 (id: string) (v: string) : Node =
    { Id = id; Kind = NodeKind.V00 { V = v } }

let mkV01 (id: string) (v: int list) : Node =
    { Id = id; Kind = NodeKind.V01 { V = v } }

let mkV03 (id: string) (v: R03_1) : Node =
    { Id = id; Kind = NodeKind.V03 { V = v } }

let mkV04 (id: string) (v: R04_1) : Node =
    { Id = id; Kind = NodeKind.V04 { V = v } }

let mkV05 (id: string) (v: R05_1) : Node =
    { Id = id; Kind = NodeKind.V05 { V = v } }

let mkV06 (id: string) (v: R06_1) : Node =
    { Id = id; Kind = NodeKind.V06 { V = v } }

let mkV07 (id: string) (v: R07_1) : Node =
    { Id = id; Kind = NodeKind.V07 { V = v } }

let mkV09 (id: string) (v: R09_1) : Node =
    { Id = id; Kind = NodeKind.V09 { V = v } }

let mkV10 (id: string) (v: R10_1) : Node =
    { Id = id; Kind = NodeKind.V10 { V = v } }

let mkV11 (id: string) (v: U11_1) : Node =
    { Id = id; Kind = NodeKind.V11 { V = v } }

let mkV12 (id: string) (v: U12_1) : Node =
    { Id = id; Kind = NodeKind.V12 { V = v } }

let mkV13 (id: string) (v: U13_1) : Node =
    { Id = id; Kind = NodeKind.V13 { V = v } }

let mkV14 (id: string) (v: U14_1) : Node =
    { Id = id; Kind = NodeKind.V14 { V = v } }

let mkV15 (id: string) (v: U15_1) : Node =
    { Id = id; Kind = NodeKind.V15 { V = v } }

let mkV20 (id: string) (v: R20_1) : Node =
    { Id = id; Kind = NodeKind.V20 { V = v } }

let mkV23 (id: string) (v: R23_1) : Node =
    { Id = id; Kind = NodeKind.V23 { V = v } }