/// The oracle protocol: one request line in, one response line out (docs/oracle-protocol.md). A
/// response carries either the model's ANSWER — which may be the model refusing the input, an
/// ordinary answer — or an ERROR, which says the oracle could not answer: the request was not a
/// request, named no function, did not fit the function's input, or the oracle itself failed.
module Fuaran.Core.Oracle.Cli.Protocol

open System
open Fuaran.Core.Oracle.Cli.Json
open Fuaran.Core.Oracle.Cli.Marshal

/// The protocol's version, `major.minor`. The minor moves when a function is added; the major
/// moves when any existing function's input, answer or meaning changes (docs/oracle-protocol.md,
/// "Versioning").
[<Literal>]
let Version = "1.0"

/// The error codes, every one of them. A caller's harness treats each as "no answer", never as a
/// model verdict.
module ErrorCode =
    [<Literal>]
    let MalformedRequest = "malformed-request"

    [<Literal>]
    let UnknownFunction = "unknown-function"

    [<Literal>]
    let BadInput = "bad-input"

    [<Literal>]
    let Internal = "internal"

/// An answer: a one-member object whose member names the model's case.
let private answer (case: string) (payload: J) : J = JObj [ case, payload ]

let private parseInputMembers = [ "text"; "nulls"; "limits" ]

/// `json.parse` — the parser model under limits (JsonParse.parse_limited).
let jsonParse (input: J) : J =
    let m = members "input" parseInputMembers input
    let text = asString "input.text" (required "input" "text" m)
    let r = parseText (parseSettings "input" m) text

    match r with
    | JsonParse.ROk v -> answer "value" (parsedValue v)
    | _ ->
        match parseRefusal text r with
        | Some(case, payload) -> answer case payload
        | None -> failwith "unreachable: an accepted parse has no refusal"

/// `wire.decode` — the parser model, then the node decoder model over what it accepted
/// (JsonParse.parse_limited, then WireDecode.decode_node).
let wireDecode (input: J) : J =
    let m = members "input" parseInputMembers input
    let text = asString "input.text" (required "input" "text" m)
    let r = parseText (parseSettings "input" m) text

    match r with
    | JsonParse.ROk v ->
        match WireDecode.decode_node (toDecodeValue v) with
        | WireDecode.Ok node -> answer "node" (ofDecodeValue (WireDecode.encode node))
        | WireDecode.Error message -> answer "decodeRefused" (JObj [ "message", JStr message ])
    | _ ->
        match parseRefusal text r with
        | Some(case, payload) -> answer case payload
        | None -> failwith "unreachable: an accepted parse has no refusal"

/// `wire.render` — the guarded canonical renderer model (WireCanon.try_render) over a value in the
/// tagged form `json.parse` answers with.
let wireRender (input: J) : J =
    let m = members "input" [ "value" ] input
    let v = toCanonValue "input.value" (required "input" "value" m)

    match WireCanon.try_render canonWire v with
    | WireCanon.Rendered chs -> answer "rendered" (JStr(ofCanonChs chs))
    | WireCanon.Refused(path, f) ->
        answer
            "refusedNonFinite"
            (JObj
                [ "path", canonPath path
                  "value",
                  JStr(
                      if Double.IsNaN f then "NaN"
                      elif f > 0.0 then "Infinity"
                      else "-Infinity"
                  ) ])
    | WireCanon.RefusedString(path, s, isKey) ->
        answer "refusedString" (JObj [ "path", canonPath path; "string", JStr(ofCanonChs s); "isKey", JBool isKey ])
    | WireCanon.RefusedRepeated(path, k) ->
        answer "refusedRepeated" (JObj [ "path", canonPath path; "key", JStr(ofCanonChs k) ])

/// `tree.apply` — the tree model's apply (TreeOps.apply).
let treeApply (input: J) : J =
    let m = members "input" [ "tree"; "op" ] input
    let tree = toTree "input.tree" (required "input" "tree" m)
    let op = toOp "input.op" (required "input" "op" m)

    match TreeOps.apply op tree with
    | DagFold.Ok t -> answer "applied" (ofTree t)
    | DagFold.Error r -> answer "rejected" (ofRejection r)

/// `chain.verify` — the chain model's first break (Chain.first_chain_break), over records whose op
/// is its own encoding.
let chainVerify (input: J) : J =
    let m = members "input" [ "hash"; "genesis"; "records" ] input

    let h =
        match required "input" "hash" m with
        | JStr "fnv1a" -> fnv1a
        | JStr "sha256" -> sha256
        | _ -> bad "input.hash: expected \"fnv1a\" or \"sha256\""

    let genesis =
        match m.TryFind "genesis" with
        | None -> ""
        | Some g -> asString "input.genesis" g

    let records =
        asList "input.records" (required "input" "records" m)
        |> List.mapi (fun k v ->
            let where = $"input.records[{k}]"
            let r = members where [ "seq"; "actor"; "op"; "prev"; "hash" ] v

            let str name =
                asString $"{where}.{name}" (required where name r)

            { Chain.rseq = toPos (asCount $"{where}.seq" MaxUnary (required where "seq" r))
              Chain.ractor = str "actor"
              Chain.rop = str "op"
              Chain.rprev = str "prev"
              Chain.rhash = str "hash" })

    match Chain.first_chain_break h showPos id genesis records with
    | Chain.Missing -> answer "intact" (JObj [])
    | Chain.Found b ->
        answer
            "broken"
            (JObj
                [ "index", ofInt (ofPos b.cindex)
                  "reason", JStr b.creason
                  "expected", JStr b.cexpected
                  "got", JStr b.cgot ])

/// `dag.fold` — the fold model (DagFold.fold_once) instantiated at the tree model: the checked
/// apply (TreeOps.wapply) and the op footprint (TreeOps.op_fp).
let dagFold (input: J) : J =
    let m = members "input" [ "base"; "lanes" ] input
    let baseTree = toTree "input.base" (required "input" "base" m)

    let lanes =
        asList "input.lanes" (required "input" "lanes" m)
        |> List.mapi (fun i lane ->
            asList $"input.lanes[{i}]" lane
            |> List.mapi (fun k op -> toOp $"input.lanes[{i}][{k}]" op))

    match DagFold.fold_once TreeOps.wapply TreeOps.op_fp baseTree lanes with
    | DagFold.LaneFolded t -> answer "folded" (ofTree t)
    | DagFold.LaneHalted cs -> answer "halted" (JArr(cs |> List.map ofConflict))
    | DagFold.LaneRejected r -> answer "rejected" (ofRejection r)
    | DagFold.LanesRejected ls -> answer "lanesRejected" (JArr(ls |> List.map (fun l -> JArr(l |> List.map ofOp))))

/// The function table: name, the model module(s) it asks, and the handler. Adding a row is a
/// minor protocol version; changing one is a major.
let functions: (string * string list * (J -> J)) list =
    [ "json.parse", [ "JsonParse" ], jsonParse
      "wire.decode", [ "JsonParse"; "WireDecode" ], wireDecode
      "wire.render", [ "WireCanon" ], wireRender
      "tree.apply", [ "TreeOps" ], treeApply
      "chain.verify", [ "Chain" ], chainVerify
      "dag.fold", [ "DagFold"; "TreeOps" ], dagFold ]

/// The oracle's own version: the assembly's informational version, which the build stamps from the
/// repository's `<Version>`.
let oracleVersion: string =
    let attr =
        Reflection.Assembly
            .GetExecutingAssembly()
            .GetCustomAttributes(typeof<Reflection.AssemblyInformationalVersionAttribute>, false)

    match attr with
    | [| :? Reflection.AssemblyInformationalVersionAttribute as a |] ->
        // SourceLink appends `+<commit>`; the release names the version alone.
        a.InformationalVersion.Split('+')[0]
    | _ -> "unknown"

/// `oracle.describe` — the protocol version, the oracle's version and every function with the
/// models it asks.
let describe (input: J) : J =
    members "input" [] input |> ignore

    answer
        "description"
        (JObj
            [ "protocol", JStr Version
              "oracle", JStr oracleVersion
              "functions",
              JArr(
                  ("oracle.describe", [])
                  :: (functions |> List.map (fun (name, models, _) -> name, models))
                  |> List.map (fun (name, models) ->
                      JObj [ "name", JStr name; "models", JArr(models |> List.map JStr) ])
              ) ])

let private table: Map<string, J -> J> =
    functions
    |> List.map (fun (name, _, f) -> name, f)
    |> List.append [ "oracle.describe", describe ]
    |> Map.ofList

let private response (id: J) (outcome: Result<J, string * string>) : string =
    let body =
        match outcome with
        | Ok a -> "answer", a
        | Error(code, message) -> "error", JObj [ "code", JStr code; "message", JStr message ]

    Json.write (JObj [ "protocol", JStr Version; "id", id; body ])

/// One request line to one response line. Never throws: every failure is an `error` response.
let handle (line: string) : string =
    match
        (try
            Ok(Json.read line)
         with ReadError(message, at) ->
             Error $"not one JSON value: {message} at unit {at}")
    with
    | Error message -> response JNull (Error(ErrorCode.MalformedRequest, message))
    | Ok request ->
        let id =
            match request with
            | JObj ms ->
                match List.tryFind (fun (k, _) -> k = "id") ms with
                | Some(_, v) -> v
                | None -> JNull
            | _ -> JNull

        let envelope =
            match request with
            | JObj ms ->
                match ms |> List.tryFind (fun (k, _) -> k <> "id" && k <> "fn" && k <> "input") with
                | Some(k, _) -> Error $"undeclared request member \"{k}\""
                | None ->
                    match List.tryFind (fun (k, _) -> k = "fn") ms, List.tryFind (fun (k, _) -> k = "input") ms with
                    | Some(_, JStr fn), Some(_, input) -> Ok(fn, input)
                    | Some(_, JStr _), None -> Error "the request has no \"input\""
                    | _ -> Error "the request has no string \"fn\""
            | _ -> Error "a request is a JSON object"

        match envelope with
        | Error message -> response id (Error(ErrorCode.MalformedRequest, message))
        | Ok(fn, input) ->
            match table.TryFind fn with
            | None -> response id (Error(ErrorCode.UnknownFunction, $"no function \"{fn}\" in protocol {Version}"))
            | Some f ->
                try
                    response id (Ok(f input))
                with
                | BadInput message -> response id (Error(ErrorCode.BadInput, message))
                | e -> response id (Error(ErrorCode.Internal, $"{e.GetType().Name}: {e.Message}"))
