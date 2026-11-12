module Fuaran.Core.Tests.ProofOracle.JsonDocumentTests

// The `Proofs.Oracle` differential for `proofs/JsonDocument.fst` (Phase 640): the extracted model
// (`proofs/oracle/JsonDocument.fs`) run beside production's `JDoc.read` and `JDoc.toJVal`. The model
// reads with `JsonParse`'s alphabet, so the character bridge, the float reader and the limits bridge
// are the parser differential's (`JsonParseDiff`, `VectorOracle.JsonModel`).

// The model's top-level module, aliased before `open Fuaran.Core` so that its `DNull`, `DNum`, ...
// never meet production's `JDoc` cases.
module M = JsonDocument

open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges

let private cap = Json.defaultMaxDepth

let private atCap = JsonLimits.depthOnly cap

/// A bounded record that every limit of the pool's documents reaches somewhere.
let private bounded =
    { MaxDepth = 4
      MaxStringLength = 3
      MaxArrayLength = 2
      MaxObjectMembers = 2
      MaxDocumentBytes = 40 }

let private bytesOk (r: JsonLimits) (input: string) =
    System.Text.Encoding.UTF8.GetByteCount input <= r.MaxDocumentBytes

let rec private renderProd (d: JDoc) : string =
    match d with
    | DNull -> "null"
    | DBool b -> "b:" + string b
    | DNum t -> "n:" + t
    | DStr s -> "s:" + s
    | DArr xs -> "[" + (xs |> List.map renderProd |> String.concat ",") + "]"
    | DObj fs ->
        "{"
        + (fs |> List.map (fun (k, v) -> k + "=" + renderProd v) |> String.concat ",")
        + "}"

let rec private renderModel (d: M.jdoc<JsonParse.och list, JsonParse.ch list>) : string =
    match d with
    | M.DNull -> "null"
    | M.DBool b -> "b:" + string b
    | M.DNum t -> "n:" + JsonParseDiff.tokStr t
    | M.DStr s -> "s:" + VectorOracle.JsonModel.modelStr s
    | M.DArr xs -> "[" + (xs |> List.map renderModel |> String.concat ",") + "]"
    | M.DObj fs ->
        "{"
        + (fs
           |> List.map (fun (k, v) -> VectorOracle.JsonModel.modelStr k + "=" + renderModel v)
           |> String.concat ",")
        + "}"

/// A refusal as both sides render it: a malformed text as its sentence (which carries the message
/// and the position), a breach as the limit and its declared value (the model carries no path).
let private prodAnswer (limits: JsonLimits) (input: string) : string =
    match JDoc.read limits input with
    | Ok d -> "ok " + renderProd d
    | Error e when e.Code = DecodeCode.LimitExceeded -> "limit " + e.Expected
    | Error e -> "err " + DecodeError.render e

let private modelLimit (l: JsonParse.limit) : JsonLimit =
    match l with
    | JsonParse.LDepth -> JsonLimit.Depth
    | JsonParse.LString -> JsonLimit.StringLength
    | JsonParse.LItems -> JsonLimit.ArrayLength
    | JsonParse.LMembers -> JsonLimit.ObjectMembers
    | JsonParse.LBytes -> JsonLimit.DocumentBytes

let private modelAnswer (bridge: string -> JsonParse.ch list) (limits: JsonLimits) (input: string) : string =
    let r =
        M.read_lossless
            JsonParseDiff.floatRead
            (string limits.MaxDepth)
            (JsonParseDiff.budget limits.MaxDepth)
            (VectorOracle.JsonModel.modelLims limits)
            (bytesOk limits input)
            (bridge input)

    match r with
    | M.DOk d -> "ok " + renderModel d
    | M.DErr(_, m, at) ->
        let refusal =
            JsonRefusal.Malformed
                { Position = input.Length - List.length at
                  Message = m
                  Kind = UnexpectedChar }

        "err " + DecodeError.render (DecodeError.ofJsonRefusal refusal)
    | M.DLimit(l, at) ->
        let limit = modelLimit l

        let refusal =
            JsonRefusal.LimitExceeded
                { Limit = limit
                  Declared = JsonLimit.declared limits limit
                  Path = []
                  Position = input.Length - List.length at }

        "limit " + (DecodeError.ofJsonRefusal refusal).Expected

let private sweep (bridge: string -> JsonParse.ch list) (limits: JsonLimits) (inputs: string list) =
    Tally.empty [ "Accepted"; "Refused" ]
    |> Differential.over inputs (fun input t ->
        let p = prodAnswer limits input
        let m = modelAnswer bridge limits input

        t
        |> Tally.bump (if p.StartsWith "ok " then "Accepted" else "Refused")
        |> Differential.compareWith
            Differential.text
            (fun p m -> sprintf "input %s\n    production %s\n    model      %s" input p m)
            p
            m)

// ---- the lowering (section 5): the model's `lower` beside `JDoc.toJVal` ----

/// A production document as the model's `ddoc string`.
let rec private toModelDoc (d: JDoc) : M.jdoc<string, string> =
    match d with
    | DNull -> M.DNull
    | DBool b -> M.DBool b
    | DNum t -> M.DNum t
    | DStr s -> M.DStr s
    | DArr xs -> M.DArr(xs |> List.map toModelDoc)
    | DObj fs -> M.DObj(fs |> List.map (fun (k, v) -> k, toModelDoc v))

/// The host's reading of a number text, at `WireDecode`'s `<int, float>`: production's own
/// (`JDoc.tryInt32` / `tryFloat`), so what is compared is the lowering's structure — the codes, the
/// paths and the first fault in document order.
let private numberOf (t: string) : FStar_Pervasives_Native.option<M.numval<int, float>> =
    match JDoc.tryInt32 (DNum t), JDoc.tryFloat (DNum t) with
    | Some i, _ -> FStar_Pervasives_Native.Some(WireDecode.JInt i)
    | None, Some f -> FStar_Pervasives_Native.Some(WireDecode.JFloat f)
    | None, None -> FStar_Pervasives_Native.None

let private showModelErr (e: DecodeLayer.derr) : string =
    let code =
        match e.code with
        | DecodeLayer.InvalidJson -> "InvalidJson"
        | DecodeLayer.MissingField -> "MissingField"
        | DecodeLayer.WrongKind -> "WrongKind"
        | DecodeLayer.UnknownTag -> "UnknownTag"
        | DecodeLayer.OutOfRange -> "OutOfRange"
        | DecodeLayer.UndeclaredMember -> "UndeclaredMember"
        | DecodeLayer.LimitExceeded -> "LimitExceeded"
        | DecodeLayer.NotAdmitted -> "NotAdmitted"
        | DecodeLayer.SchemaFault -> "SchemaFault"

    let seg (s: DecodeLayer.seg) =
        match s with
        | DecodeLayer.Key k -> "[\"" + k + "\"]"
        | DecodeLayer.Index i -> "[" + string i + "]"

    code + "@$" + (e.path |> List.map seg |> String.concat "")

let private showProdErr (e: DecodeError) : string =
    let seg (s: PathSegment) =
        match s with
        | PathSegment.Key k -> "[\"" + k + "\"]"
        | PathSegment.Index i -> "[" + string i + "]"

    DecodeError.codeName e.Code
    + "@$"
    + (e.Path |> List.map seg |> String.concat "")

/// Run `f` on a thread with a deep stack. The model's conversion and lowering recurse once per item
/// of an array (`to_jvals`, `lower_items` build the list after the recursive call, as a model of a
/// fold does), and the benchmark corpora hold arrays of many thousands of items, which the test
/// host's default stack does not reach. Production is iterative and needs nothing of the kind.
let private onDeepStack (f: unit -> unit) =
    let mutable failure: exn option = None

    let worker =
        System.Threading.Thread(
            (fun () ->
                try
                    f ()
                with e ->
                    failure <- Some e),
            512 * 1024 * 1024
        )

    worker.Start()
    worker.Join()

    match failure with
    | Some e -> raise e
    | None -> ()

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [ testCase "the lossless-document oracle reads what JDoc.read reads over the pool, refusal for refusal"
          <| fun _ ->
              let pool = Fuaran.Core.Tests.JsonDocumentTests.pool
              let t = sweep JsonParseDiff.toChs atCap pool
              Differential.expectAgrees "lossless reader, at the cap" t
              Expect.isGreaterThan (Tally.count "Accepted" t) 1000 "accepted documents compared"
              Expect.isGreaterThan (Tally.count "Refused" t) 1000 "refusals compared"

              let b = sweep JsonParseDiff.toChs bounded (List.truncate 20000 pool)
              Differential.expectAgrees "lossless reader, under a bounded record" b
              Expect.isGreaterThan (Tally.count "Refused" b) 1000 "breaches compared"

          testCase
              "the extracted lossless reading converts to the extracted strict parse over the pool - the theorem, evaluated"
          <| fun _ ->
              onDeepStack (fun () ->
                  let mutable compared = 0

                  for limits in [ atCap; bounded ] do
                      for input in Fuaran.Core.Tests.JsonDocumentTests.pool do
                          let chs = JsonParseDiff.toChs input
                          let fr = JsonParseDiff.floatRead
                          let b = JsonParseDiff.budget limits.MaxDepth
                          let lims = VectorOracle.JsonModel.modelLims limits
                          let ok = bytesOk limits input

                          let lossless =
                              M.converted fr (M.read_lossless fr (string limits.MaxDepth) b lims ok chs)

                          let strict =
                              match
                                  JsonParse.parse_limited
                                      fr
                                      (string limits.MaxDepth)
                                      JsonParse.RejectNull
                                      b
                                      lims
                                      ok
                                      chs
                              with
                              | JsonParse.ROk v -> FStar_Pervasives_Native.Some v
                              | _ -> FStar_Pervasives_Native.None

                          compared <- compared + 1

                          if lossless <> strict then
                              failtestf
                                  "input %s: the lossless reading converts to %s, the strict parse answers %s"
                                  input
                                  (match lossless with
                                   | FStar_Pervasives_Native.Some _ -> "a value"
                                   | FStar_Pervasives_Native.None -> "nothing")
                                  (match strict with
                                   | FStar_Pervasives_Native.Some _ -> "a value"
                                   | FStar_Pervasives_Native.None -> "nothing")

                  Expect.isGreaterThan compared 10000 "inputs evaluated")

          testCase "the extracted lowering refuses at toJVal's codes and paths"
          <| fun _ ->
              onDeepStack (fun () ->
                  let constructed =
                      [ DObj [ "a", DNull ]
                        DArr [ DNum "1"; DArr [ DNull; DNum "1e400" ] ]
                        DObj [ "a", DObj []; "a", DObj [ "b", DNull ] ]
                        DObj [ "x", DNum "12345678901234567890"; "y", DNull ]
                        DArr [ DNum "01" ] ]

                  let docs =
                      constructed
                      @ (Fuaran.Core.Tests.JsonDocumentTests.pool
                         |> List.choose (fun input -> JDoc.read atCap input |> Result.toOption))

                  let mutable refused = 0

                  for d in docs do
                      let p =
                          match JDoc.toJVal d with
                          | Ok _ -> "ok"
                          | Error e ->
                              refused <- refused + 1
                              showProdErr e

                      let m =
                          match M.lower numberOf (toModelDoc d) with
                          | DecodeLayer.Ok _ -> "ok"
                          | DecodeLayer.Error e -> showModelErr e

                      if m <> p then
                          failtestf
                              "%s: production %s, model %s"
                              (JDoc.tryWrite d |> Result.defaultValue "(unwritable)")
                              p
                              m

                  Expect.isGreaterThan refused 1000 "refusals compared")

          testCase "a lossless model shown no containers disagrees on every structured input - the blind bridge"
          <| fun _ ->
              let structured =
                  [ "{}"; "[]"; "{\"a\":null}"; "[1.50,null]"; "{\"a\":[1,{\"b\":2}]}" ]

              for input in structured do
                  Expect.notEqual (modelAnswer JsonParseDiff.toChsBlind atCap input) (prodAnswer atCap input) input

              for input in [ "null"; "1.50"; "\"x\""; "true" ] do
                  Expect.equal (modelAnswer JsonParseDiff.toChsBlind atCap input) (prodAnswer atCap input) input ]
