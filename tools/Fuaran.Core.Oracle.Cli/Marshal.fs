/// The bridges between the protocol's JSON and the extracted models' own types — the CLI's whole
/// interpretation of the models, in one file. Every function-typed parameter a model takes (a float
/// reader, a key order, a string-encodability test, a hash) is instantiated HERE, over the base
/// library alone, and each is the instantiation the suite's own differential uses or an independent
/// implementation of the algorithm it names (docs/oracle-protocol.md, "What the CLI supplies").
/// Nothing here decides an answer: the model does, and this file only spells its input and output.
module Fuaran.Core.Oracle.Cli.Marshal

open System
open System.Globalization
open Fuaran.Core.Oracle.Cli.Json

/// The input does not fit the function's declared shape. Raised before any model runs, and
/// answered as the `bad-input` error, never as a model answer.
exception BadInput of string

let bad (message: string) = raise (BadInput message)

let private inv = CultureInfo.InvariantCulture

// ---- reading the input ---------------------------------------------------------------------------

/// An object whose members are all declared. A member outside `allowed` is refused by name, so a
/// misspelt optional member is an error rather than a silently applied default.
let members (where: string) (allowed: string list) (v: J) : Map<string, J> =
    match v with
    | JObj ms ->
        for k, _ in ms do
            if not (List.contains k allowed) then
                bad $"{where}: undeclared member \"{k}\""

        Map.ofList ms
    | _ -> bad $"{where}: expected an object"

let required (where: string) (name: string) (m: Map<string, J>) : J =
    match m.TryFind name with
    | Some v -> v
    | None -> bad $"{where}: missing member \"{name}\""

let asString (where: string) (v: J) : string =
    match v with
    | JStr s -> s
    | _ -> bad $"{where}: expected a string"

let asBool (where: string) (v: J) : bool =
    match v with
    | JBool b -> b
    | _ -> bad $"{where}: expected true or false"

let asList (where: string) (v: J) : J list =
    match v with
    | JArr xs -> xs
    | _ -> bad $"{where}: expected an array"

/// A non-negative integer written as an integer token (no fraction, no exponent, no sign) and at
/// most `cap`.
let asCount (where: string) (cap: int) (v: J) : int =
    match v with
    | JNum token when token |> Seq.forall Char.IsAsciiDigit ->
        match Int32.TryParse(token, NumberStyles.None, inv) with
        | true, n when n <= cap -> n
        | _ -> bad $"{where}: {token} is above the protocol limit {cap}"
    | _ -> bad $"{where}: expected a non-negative integer"

let stringList (where: string) (v: J) : string list =
    asList where v |> List.mapi (fun k x -> asString $"{where}[{k}]" x)

/// The largest count the protocol turns into a model's unary bound (a depth, a string, array or
/// member limit, a sequence number): those are lists of that length, so the cap is a memory bound.
[<Literal>]
let MaxUnary = 1048576

let unary (n: int) : unit list = List.replicate n ()

// ---- the JSON parser model (JsonParse) ------------------------------------------------------------

/// A UTF-16 unit as the parser model's character. The suite's bridge, unit for unit
/// (ProofOracle.Bridges.fs, `JsonParseDiff.toCh`): the model distinguishes these classes, and which
/// class a unit falls in is the bridge's to say.
let toParseCh (c: char) : JsonParse.ch =
    match c with
    | ' ' -> JsonParse.CSpace
    | '\t' -> JsonParse.CTab
    | '\n' -> JsonParse.CNewline
    | '\r' -> JsonParse.CReturn
    | '"' -> JsonParse.CQuote
    | '\\' -> JsonParse.CBackslash
    | '/' -> JsonParse.CSlash
    | '{' -> JsonParse.CLBrace
    | '}' -> JsonParse.CRBrace
    | '[' -> JsonParse.CLBrack
    | ']' -> JsonParse.CRBrack
    | ':' -> JsonParse.CColon
    | ',' -> JsonParse.CComma
    | '-' -> JsonParse.CMinus
    | '+' -> JsonParse.CPlus
    | '.' -> JsonParse.CDot
    | '0' -> JsonParse.CD0
    | '1' -> JsonParse.CD1
    | '2' -> JsonParse.CD2
    | '3' -> JsonParse.CD3
    | '4' -> JsonParse.CD4
    | '5' -> JsonParse.CD5
    | '6' -> JsonParse.CD6
    | '7' -> JsonParse.CD7
    | '8' -> JsonParse.CD8
    | '9' -> JsonParse.CD9
    | 'a' -> JsonParse.CLa
    | 'b' -> JsonParse.CLb
    | 'c' -> JsonParse.CLc
    | 'd' -> JsonParse.CLd
    | 'e' -> JsonParse.CLe
    | 'f' -> JsonParse.CLf
    | 'l' -> JsonParse.CLl
    | 'n' -> JsonParse.CLn
    | 'r' -> JsonParse.CLr
    | 's' -> JsonParse.CLs
    | 't' -> JsonParse.CLt
    | 'u' -> JsonParse.CLu
    | 'A' -> JsonParse.CUa
    | 'B' -> JsonParse.CUb
    | 'C' -> JsonParse.CUc
    | 'D' -> JsonParse.CUd
    | 'E' -> JsonParse.CUe
    | 'F' -> JsonParse.CUf
    | other when int other >= 0xD800 && int other <= 0xDBFF -> JsonParse.CHiSur(string other)
    | other when int other >= 0xDC00 && int other <= 0xDFFF -> JsonParse.CLoSur(string other)
    | other when int other < 0x20 -> JsonParse.CCtrl(string other, "U+" + (int other).ToString("X4"))
    | other -> JsonParse.COther(string other)

let toParseChs (s: string) : JsonParse.ch list = s |> Seq.map toParseCh |> List.ofSeq

let tokenText (t: JsonParse.ch list) : string =
    t |> List.map JsonParse.ch_str |> String.concat ""

/// The parser model's float reader — the one parameter the model leaves to its host, a numeral's
/// class. The suite's (`JsonParseDiff.floatRead`) with its one production call replaced by what
/// that call is on .NET: the canonical layout of a finite double is its round-trip `R` text.
let floatRead (tok: JsonParse.ch list) : JsonParse.freadv =
    let s = tokenText tok

    match Double.TryParse(s, NumberStyles.Float, inv) with
    | true, v when Double.IsNaN v || Double.IsInfinity v -> JsonParse.FNonFinite
    | true, v when v.ToString("R", inv) = s -> JsonParse.FCanonical
    | true, _ -> JsonParse.FFinite
    | _ -> JsonParse.FUnparsable

let private hexVal (c: JsonParse.ch) : int = Convert.ToInt32(JsonParse.ch_str c, 16)

/// The text one of the model's string characters denotes.
let ochText (o: JsonParse.och) : string =
    match o with
    | JsonParse.OLit c -> JsonParse.ch_str c
    | JsonParse.OEsc e ->
        match JsonParse.ch_str e with
        | "n" -> "\n"
        | "r" -> "\r"
        | "t" -> "\t"
        | "b" -> "\b"
        | "f" -> "\f"
        | other -> other
    | JsonParse.OUni(a, b, c, d) -> string (char ((hexVal a <<< 12) + (hexVal b <<< 8) + (hexVal c <<< 4) + hexVal d))

let ochsText (os: JsonParse.och list) : string =
    os |> List.map ochText |> String.concat ""

/// The parse settings a `json.parse`, `wire.decode` or `wire.render` input carries.
type ParseSettings =
    { Nulls: JsonParse.policy
      Depth: int
      Limits: JsonParse.lims
      DocumentBytes: int option }

/// Reads `nulls` and `limits` from a parse-taking input. Absent `nulls` is `reject`; absent
/// `limits.depth` is 512 (the strict reading's depth); every other absent limit is unbounded.
let parseSettings (where: string) (m: Map<string, J>) : ParseSettings =
    let nulls =
        match m.TryFind "nulls" with
        | None -> JsonParse.RejectNull
        | Some(JStr "reject") -> JsonParse.RejectNull
        | Some(JStr "eraseMember") -> JsonParse.EraseMemberNull
        | Some _ -> bad $"{where}.nulls: expected \"reject\" or \"eraseMember\""

    let lim =
        match m.TryFind "limits" with
        | None -> Map.empty
        | Some v ->
            members $"{where}.limits" [ "depth"; "stringLength"; "arrayLength"; "objectMembers"; "documentBytes" ] v

    let bound name =
        match lim.TryFind name with
        | None -> JsonParse.Unbounded
        | Some v -> JsonParse.Bounded(unary (asCount $"{where}.limits.{name}" MaxUnary v))

    { Nulls = nulls
      Depth =
        match lim.TryFind "depth" with
        | None -> 512
        | Some v -> asCount $"{where}.limits.depth" MaxUnary v
      Limits =
        { JsonParse.lstr = bound "stringLength"
          JsonParse.litems = bound "arrayLength"
          JsonParse.lmembers = bound "objectMembers" }
      DocumentBytes =
        lim.TryFind "documentBytes"
        |> Option.map (asCount $"{where}.limits.documentBytes" Int32.MaxValue) }

/// The parser model, asked about `text` under `settings`. The document-size verdict is the
/// caller's in the model (`bytes_ok`); it is the UTF-8 length, an unpaired surrogate counting as
/// the three bytes of its replacement, as the suite's differential computes it.
let parseText (settings: ParseSettings) (text: string) : JsonParse.jresult =
    let bytesOk =
        match settings.DocumentBytes with
        | None -> true
        | Some cap -> Text.Encoding.UTF8.GetByteCount text <= cap

    JsonParse.parse_limited
        floatRead
        (string settings.Depth)
        settings.Nulls
        (unary settings.Depth)
        settings.Limits
        bytesOk
        (toParseChs text)

let ekindName (k: JsonParse.ekind) : string =
    match k with
    | JsonParse.UnexpectedChar -> "UnexpectedChar"
    | JsonParse.UnexpectedEndOfInput -> "UnexpectedEndOfInput"
    | JsonParse.ExpectedToken -> "ExpectedToken"
    | JsonParse.UnterminatedString -> "UnterminatedString"
    | JsonParse.UnterminatedEscape -> "UnterminatedEscape"
    | JsonParse.TruncatedUnicodeEscape -> "TruncatedUnicodeEscape"
    | JsonParse.BadEscape -> "BadEscape"
    | JsonParse.BadHexDigit -> "BadHexDigit"
    | JsonParse.MalformedNumber -> "MalformedNumber"
    | JsonParse.NullNotRepresentable -> "NullNotRepresentable"
    | JsonParse.MaxDepthExceeded -> "MaxDepthExceeded"
    | JsonParse.TrailingCharacters -> "TrailingCharacters"
    | JsonParse.DuplicateMember -> "DuplicateMember"
    | JsonParse.LimitExceeded _ -> "LimitExceeded"

let limitName (l: JsonParse.limit) : string =
    match l with
    | JsonParse.LDepth -> "depth"
    | JsonParse.LString -> "stringLength"
    | JsonParse.LItems -> "arrayLength"
    | JsonParse.LMembers -> "objectMembers"
    | JsonParse.LBytes -> "documentBytes"

/// A parsed value as the protocol writes it: a one-member object naming the model's case, a number
/// kept as the token the model kept, and an object's members in the order the model holds them.
let rec parsedValue (v: JsonParse.jval) : J =
    match v with
    | JsonParse.JStr s -> JObj [ "string", JStr(ochsText s) ]
    | JsonParse.JInt tok -> JObj [ "int", JStr(tokenText tok) ]
    | JsonParse.JBool b -> JObj [ "bool", JBool b ]
    | JsonParse.JFloat tok -> JObj [ "float", JStr(tokenText tok) ]
    | JsonParse.JArr xs -> JObj [ "array", JArr(xs |> List.map parsedValue) ]
    | JsonParse.JObj fs ->
        JObj
            [ "object",
              JArr(
                  fs
                  |> List.map (fun (k, x) -> JObj [ "key", JStr(ochsText k); "value", parsedValue x ])
              ) ]

/// A refused parse as the protocol writes it, `at` being the UTF-16 index the model stopped at.
/// `None` when the model accepted the document.
let parseRefusal (text: string) (r: JsonParse.jresult) : (string * J) option =
    let at (rest: JsonParse.ch list) = ofInt (text.Length - List.length rest)

    match r with
    | JsonParse.ROk _ -> None
    | JsonParse.RErr(k, message, rest) ->
        Some("parseRefused", JObj [ "kind", JStr(ekindName k); "message", JStr message; "at", at rest ])
    | JsonParse.RLimit(l, rest) -> Some("parseLimit", JObj [ "limit", JStr(limitName l); "at", at rest ])

/// The parser model's number tokens, read as the other models' numbers: an `int` token (one the
/// parser kept as an integer, which fits 32 bits by its own rule) as an `int`, every other as the
/// double it denotes, which the parser has already held finite.
let tokenInt (tok: JsonParse.ch list) : int =
    Int32.Parse(tokenText tok, NumberStyles.AllowLeadingSign, inv)

let tokenFloat (tok: JsonParse.ch list) : float =
    Double.Parse(tokenText tok, NumberStyles.Float, inv)

// ---- the node decoder model (WireDecode) ---------------------------------------------------------

let rec toDecodeValue (v: JsonParse.jval) : WireDecode.jval<int, float> =
    match v with
    | JsonParse.JStr s -> WireDecode.JStr(ochsText s)
    | JsonParse.JInt tok -> WireDecode.JInt(tokenInt tok)
    | JsonParse.JBool b -> WireDecode.JBool b
    | JsonParse.JFloat tok -> WireDecode.JFloat(tokenFloat tok)
    | JsonParse.JArr xs -> WireDecode.JArr(xs |> List.map toDecodeValue)
    | JsonParse.JObj fs -> WireDecode.JObj(fs |> List.map (fun (k, x) -> ochsText k, toDecodeValue x))

/// A decoder value as protocol JSON. Used for the model's own encoding of a decoded node, which
/// writes strings, booleans, arrays and objects only.
let rec ofDecodeValue (v: WireDecode.jval<int, float>) : J =
    match v with
    | WireDecode.JStr s -> JStr s
    | WireDecode.JInt i -> ofInt i
    | WireDecode.JBool b -> JBool b
    | WireDecode.JFloat f -> JNum(f.ToString("R", inv))
    | WireDecode.JArr xs -> JArr(xs |> List.map ofDecodeValue)
    | WireDecode.JObj fs -> JObj(fs |> List.map (fun (k, x) -> k, ofDecodeValue x))

// ---- the canonical renderer model (WireCanon) ----------------------------------------------------

let private hexd (n: int) : WireCanon.hexd =
    match n with
    | 0 -> WireCanon.HD0
    | 1 -> WireCanon.HD1
    | 2 -> WireCanon.HD2
    | 3 -> WireCanon.HD3
    | 4 -> WireCanon.HD4
    | 5 -> WireCanon.HD5
    | 6 -> WireCanon.HD6
    | 7 -> WireCanon.HD7
    | 8 -> WireCanon.HD8
    | 9 -> WireCanon.HD9
    | 10 -> WireCanon.HDa
    | 11 -> WireCanon.HDb
    | 12 -> WireCanon.HDc
    | 13 -> WireCanon.HDd
    | 14 -> WireCanon.HDe
    | _ -> WireCanon.HDf

let private hexdValue (d: WireCanon.hexd) : int =
    match d with
    | WireCanon.HD0 -> 0
    | WireCanon.HD1 -> 1
    | WireCanon.HD2 -> 2
    | WireCanon.HD3 -> 3
    | WireCanon.HD4 -> 4
    | WireCanon.HD5 -> 5
    | WireCanon.HD6 -> 6
    | WireCanon.HD7 -> 7
    | WireCanon.HD8 -> 8
    | WireCanon.HD9 -> 9
    | WireCanon.HDa -> 10
    | WireCanon.HDb -> 11
    | WireCanon.HDc -> 12
    | WireCanon.HDd -> 13
    | WireCanon.HDe -> 14
    | WireCanon.HDf -> 15

/// A UTF-16 unit as the renderer model's character: total, and a `CPlain` never carries a spelling
/// another constructor denotes (the model's `bridged`), as the suite's `canonToCh` is.
let toCanonCh (c: char) : WireCanon.ch =
    match c with
    | '"' -> WireCanon.CQuote
    | '\\' -> WireCanon.CBackslash
    | '{' -> WireCanon.CLBrace
    | '}' -> WireCanon.CRBrace
    | '[' -> WireCanon.CLBrack
    | ']' -> WireCanon.CRBrack
    | ':' -> WireCanon.CColon
    | ',' -> WireCanon.CComma
    | '-' -> WireCanon.CMinus
    | '+' -> WireCanon.CPlus
    | '.' -> WireCanon.CDot
    | 'E' -> WireCanon.CUpE
    | 'u' -> WireCanon.CLu
    | c when c < ' ' -> WireCanon.CCtrl(int c >= 16, hexd (int c % 16))
    | c when c >= '0' && c <= '9' -> WireCanon.CHexCh(hexd (int c - int '0'))
    | c when c >= 'a' && c <= 'f' -> WireCanon.CHexCh(hexd (int c - int 'a' + 10))
    | c -> WireCanon.CPlain(string c)

let toCanonChs (s: string) : WireCanon.ch list = s |> Seq.map toCanonCh |> List.ofSeq

let ofCanonCh (c: WireCanon.ch) : string =
    match c with
    | WireCanon.CQuote -> "\""
    | WireCanon.CBackslash -> "\\"
    | WireCanon.CLBrace -> "{"
    | WireCanon.CRBrace -> "}"
    | WireCanon.CLBrack -> "["
    | WireCanon.CRBrack -> "]"
    | WireCanon.CColon -> ":"
    | WireCanon.CComma -> ","
    | WireCanon.CMinus -> "-"
    | WireCanon.CPlus -> "+"
    | WireCanon.CDot -> "."
    | WireCanon.CUpE -> "E"
    | WireCanon.CLu -> "u"
    | WireCanon.CHexCh d -> (hexdValue d).ToString("x")
    | WireCanon.CCtrl(hi, lo) -> string (char ((if hi then 16 else 0) + hexdValue lo))
    | WireCanon.CPlain s -> s

let ofCanonChs (l: WireCanon.ch list) : string =
    l |> List.map ofCanonCh |> String.concat ""

let private strictUtf8 = Text.UTF8Encoding(false, true)

let private strictlyEncodable (s: string) : bool =
    try
        strictUtf8.GetBytes s |> ignore
        true
    with :? Text.EncoderFallbackException ->
        false

/// The renderer model's host record, as the suite's `canonWire` instantiates it, except
/// `tok_read`: only the model's READER calls it, the protocol exposes no reader, and so it answers
/// an error rather than reaching for a parser.
let canonWire: WireCanon.wire<int, float> =
    { WireCanon.int_str = fun i -> toCanonChs (string i)
      WireCanon.float_str = fun f -> toCanonChs (f.ToString("R", inv))
      WireCanon.fclass =
        fun f ->
            if Double.IsNaN f then WireCanon.FNaN
            elif Double.IsPositiveInfinity f then WireCanon.FPosInf
            elif Double.IsNegativeInfinity f then WireCanon.FNegInf
            else WireCanon.FFinite
      WireCanon.is_zero = fun f -> f = 0.0
      WireCanon.pos_zero = 0.0
      WireCanon.key_le = fun a b -> String.CompareOrdinal(ofCanonChs a, ofCanonChs b) <= 0
      WireCanon.tok_read = fun _ -> WireCanon.Error "the protocol exposes no reader"
      WireCanon.str_ok = fun s -> strictlyEncodable (ofCanonChs s) }

/// A value in the tagged form `json.parse` answers with, as the renderer model's value. The form
/// reaches what document text cannot: a string or key holding an unpaired surrogate, an object
/// repeating a key, and a non-finite float (`NaN`, `Infinity`, `-Infinity`).
let rec toCanonValue (where: string) (v: J) : WireCanon.jval<int, float> =
    match v with
    | JObj [ "string", JStr s ] -> WireCanon.JStr(toCanonChs s)
    | JObj [ "int", JStr token ] ->
        match Int32.TryParse(token, NumberStyles.AllowLeadingSign, inv) with
        | true, i -> WireCanon.JInt i
        | _ -> bad $"{where}.int: \"{token}\" is not a 32-bit integer"
    | JObj [ "float", JStr token ] ->
        match token with
        | "NaN" -> WireCanon.JFloat Double.NaN
        | "Infinity" -> WireCanon.JFloat Double.PositiveInfinity
        | "-Infinity" -> WireCanon.JFloat Double.NegativeInfinity
        | _ ->
            match Double.TryParse(token, NumberStyles.Float, inv) with
            | true, f when Double.IsFinite f -> WireCanon.JFloat f
            | _ -> bad $"{where}.float: \"{token}\" is not a finite decimal, NaN, Infinity or -Infinity"
    | JObj [ "bool", JBool b ] -> WireCanon.JBool b
    | JObj [ "array", JArr xs ] -> WireCanon.JArr(xs |> List.mapi (fun k x -> toCanonValue $"{where}.array[{k}]" x))
    | JObj [ "object", JArr fs ] ->
        fs
        |> List.mapi (fun k f ->
            let at = $"{where}.object[{k}]"
            let m = members at [ "key"; "value" ] f
            toCanonChs (asString $"{at}.key" (required at "key" m)), toCanonValue $"{at}.value" (required at "value" m))
        |> WireCanon.JObj
    | _ ->
        bad
            $"{where}: expected one of {{\"string\"}}, {{\"int\"}}, {{\"float\"}}, {{\"bool\"}}, {{\"array\"}}, {{\"object\"}}"

let canonPath (p: WireCanon.pstep list) : J =
    p
    |> List.map (fun step ->
        match step with
        | WireCanon.PItem i -> JNum(i.ToString(inv))
        | WireCanon.PMember k -> JStr(ofCanonChs k))
    |> JArr

// ---- the tree model (TreeOps) --------------------------------------------------------------------

let rec toTree (where: string) (v: J) : TreeOps.tree =
    let m = members where [ "id"; "kind"; "children" ] v

    let children =
        match m.TryFind "children" with
        | None -> []
        | Some c ->
            asList $"{where}.children" c
            |> List.mapi (fun k x -> toTree $"{where}.children[{k}]" x)

    TreeOps.TNode(
        asString $"{where}.id" (required where "id" m),
        asString $"{where}.kind" (required where "kind" m),
        children
    )

let rec ofTree (t: TreeOps.tree) : J =
    match t with
    | TreeOps.TNode(id, kind, kids) ->
        JObj [ "id", JStr id; "kind", JStr kind; "children", JArr(kids |> List.map ofTree) ]

let rec toOp (where: string) (v: J) : TreeOps.op =
    let tag =
        match v with
        | JObj ms ->
            match List.tryFind (fun (k, _) -> k = "op") ms with
            | Some(_, JStr t) -> t
            | _ -> bad $"{where}: expected a string member \"op\""
        | _ -> bad $"{where}: expected an object"

    let str name m =
        asString $"{where}.{name}" (required where name m)

    match tag with
    | "insertChild" ->
        let m = members where [ "op"; "parent"; "node" ] v
        TreeOps.InsertChild(str "parent" m, toTree $"{where}.node" (required where "node" m))
    | "removeNode" ->
        let m = members where [ "op"; "node" ] v
        TreeOps.RemoveNode(str "node" m)
    | "moveNode" ->
        let m = members where [ "op"; "node"; "newParent" ] v
        TreeOps.MoveNode(str "node" m, str "newParent" m)
    | "reorderChildren" ->
        let m = members where [ "op"; "parent"; "order" ] v
        TreeOps.ReorderChildren(str "parent" m, stringList $"{where}.order" (required where "order" m))
    | "batch" ->
        let m = members where [ "op"; "ops" ] v

        TreeOps.Batch(
            asList $"{where}.ops" (required where "ops" m)
            |> List.mapi (fun k x -> toOp $"{where}.ops[{k}]" x)
        )
    | "updateNode" ->
        let m = members where [ "op"; "node" ] v
        TreeOps.UpdateNode(toTree $"{where}.node" (required where "node" m))
    | other -> bad $"{where}.op: unknown op \"{other}\""

let rec ofOp (o: TreeOps.op) : J =
    match o with
    | TreeOps.InsertChild(p, n) -> JObj [ "op", JStr "insertChild"; "parent", JStr p; "node", ofTree n ]
    | TreeOps.RemoveNode x -> JObj [ "op", JStr "removeNode"; "node", JStr x ]
    | TreeOps.MoveNode(x, np) -> JObj [ "op", JStr "moveNode"; "node", JStr x; "newParent", JStr np ]
    | TreeOps.ReorderChildren(p, order) ->
        JObj
            [ "op", JStr "reorderChildren"
              "parent", JStr p
              "order", JArr(order |> List.map JStr) ]
    | TreeOps.Batch os -> JObj [ "op", JStr "batch"; "ops", JArr(os |> List.map ofOp) ]
    | TreeOps.UpdateNode n -> JObj [ "op", JStr "updateNode"; "node", ofTree n ]

let ofRejection (r: TreeOps.rejection) : J =
    let strs (xs: string list) = JArr(xs |> List.map JStr)

    match r with
    | TreeOps.UnknownNode(target, addressable) ->
        JObj
            [ "rejection", JStr "unknownNode"
              "target", JStr target
              "addressable", strs addressable ]
    | TreeOps.DuplicateId id -> JObj [ "rejection", JStr "duplicateId"; "id", JStr id ]
    | TreeOps.CannotRemoveRoot -> JObj [ "rejection", JStr "cannotRemoveRoot" ]
    | TreeOps.WouldNestUnderSelf x -> JObj [ "rejection", JStr "wouldNestUnderSelf"; "node", JStr x ]
    | TreeOps.NotAContainer(x, kind) -> JObj [ "rejection", JStr "notAContainer"; "node", JStr x; "kind", JStr kind ]
    | TreeOps.ReorderMismatch(p, current, requested) ->
        JObj
            [ "rejection", JStr "reorderMismatch"
              "parent", JStr p
              "current", strs current
              "requested", strs requested ]
    | TreeOps.Rejected(code, message) ->
        JObj [ "rejection", JStr "rejected"; "code", JStr code; "message", JStr message ]

// ---- the hash chain model (Chain) ----------------------------------------------------------------

/// The chain model's sequence numbers are Peano naturals.
let rec toPos (n: int) : Chain.pos =
    let mutable p = Chain.PZero

    for _ in 1..n do
        p <- Chain.PSucc p

    p

let ofPos (p: Chain.pos) : int =
    let rec go acc p =
        match p with
        | Chain.PZero -> acc
        | Chain.PSucc q -> go (acc + 1) q

    go 0 p

/// FNV-1a 32 over the UTF-16 units of `prev + "|" + payload`, eight lower-case hex digits —
/// written here from the algorithm's definition, sharing no line with any other implementation.
let fnv1a (prev: string) (payload: string) : string =
    let mutable h = 2166136261u

    for c in prev + "|" + payload do
        h <- h ^^^ uint32 c
        h <- h * 16777619u

    h.ToString("x8", inv)

/// SHA-256 over the UTF-8 bytes of `prev + "|" + payload` (an unpaired surrogate encoded as
/// U+FFFD), as lower-case hex.
let sha256 (prev: string) (payload: string) : string =
    let bytes = Text.Encoding.UTF8.GetBytes(prev + "|" + payload)

    Security.Cryptography.SHA256.HashData(bytes)
    |> Convert.ToHexString
    |> fun s -> s.ToLowerInvariant()

/// The decimal text of a sequence number, the model's `show`.
let showPos (p: Chain.pos) : string = string (ofPos p)

// ---- the DAG fold model (DagFold) ----------------------------------------------------------------

let shapeTag (s: DagFold.shape) : string =
    match s with
    | DagFold.ConcurrentUpdate -> "concurrent-update"
    | DagFold.InsertPositionClash -> "insert-position-clash"
    | DagFold.MoveVsRemove -> "move-vs-remove"
    | DagFold.SlotClash slot -> "slot-clash:" + slot

let ofConflict (c: DagFold.conflict<TreeOps.op>) : J =
    JObj
        [ "left", ofOp c.left
          "right", ofOp c.right
          "address", JStr c.address
          "shape", JStr(shapeTag c.shape) ]
