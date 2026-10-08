namespace Fuaran.Core

/// The canonical wire codec for a `SchemaDelta` (Phase 317) — what a host that records deltas beside
/// `Schema.fingerprint` as provenance writes, so that `Schema.patch` can replay them later. One
/// object with five members, all required: `added` and `removed` as `{name, type}` entries (the
/// columnar codec's schema entry), `retyped` as `{name, from, to}`, `reordered` a bool, and `order`
/// the column names `Order` carries. Rendered under `Canon`, so the bytes are canonical across
/// hosts; decode surfaces the columnar codec envelope (`ColumnError`). It decodes what it is handed
/// and judges nothing about the schema the delta will meet — that is `Schema.patch`'s question.
module SchemaDeltaCodec =

    let private entryJson (name: string, ty: ColumnType) : JVal =
        JObj [ "name", JStr name; "type", JStr(ColumnType.tag ty) ]

    /// Encode a delta to a `JVal`; `encode` renders it under `Canon`, which sorts the keys.
    let encodeJson (d: SchemaDelta) : JVal =
        JObj
            [ "added", JArr(d.Added |> List.map entryJson)
              "removed", JArr(d.Removed |> List.map entryJson)
              "retyped",
              JArr(
                  d.Retyped
                  |> List.map (fun (name, fromTy, toTy) ->
                      JObj
                          [ "name", JStr name
                            "from", JStr(ColumnType.tag fromTy)
                            "to", JStr(ColumnType.tag toTy) ])
              )
              "reordered", JBool d.Reordered
              "order", JArr(d.Order |> List.map JStr) ]

    /// The canonical wire string for a delta. Total: every delta encodes.
    let encode (d: SchemaDelta) : string = Canon.render (encodeJson d)

    let private fault (ctx: string) (f: Decode.Fault) : ColumnError =
        match f with
        | Decode.MissingProperty name -> MissingField name
        | Decode.WrongKind(expected, got) -> MalformedShape(ctx + ": expected " + expected + ", got " + got)

    let private field (name: string) (el: JVal) : Result<JVal, ColumnError> = Decode.propWith (fault name) name el

    let private text (ctx: string) (el: JVal) : Result<string, ColumnError> = Decode.stringWith (fault ctx) el

    let private columnType (ctx: string) (el: JVal) : Result<ColumnType, ColumnError> =
        text ctx el
        |> Result.bind (fun tag ->
            match ColumnType.ofTag tag with
            | Some ty -> Ok ty
            | None -> Error(UnknownType(tag, ColumnType.all)))

    let private items (name: string) (read: JVal -> Result<'a, ColumnError>) (el: JVal) : Result<'a list, ColumnError> =
        field name el
        |> Result.bind (Decode.arrayWith (fault name))
        |> Result.bind (fun xs ->
            let rec go acc =
                function
                | [] -> Ok(List.rev acc)
                | x :: rest ->
                    match read x with
                    | Ok v -> go (v :: acc) rest
                    | Error e -> Error e

            go [] xs)

    let private entry (ctx: string) (el: JVal) : Result<string * ColumnType, ColumnError> =
        field "name" el
        |> Result.bind (text (ctx + ".name"))
        |> Result.bind (fun name ->
            field "type" el
            |> Result.bind (columnType (ctx + ".type"))
            |> Result.map (fun ty -> name, ty))

    let private retypedEntry (el: JVal) : Result<string * ColumnType * ColumnType, ColumnError> =
        field "name" el
        |> Result.bind (text "retyped.name")
        |> Result.bind (fun name ->
            field "from" el
            |> Result.bind (columnType "retyped.from")
            |> Result.bind (fun fromTy ->
                field "to" el
                |> Result.bind (columnType "retyped.to")
                |> Result.map (fun toTy -> name, fromTy, toTy)))

    /// Decode a delta from a `JVal`. `decodeJson (encodeJson d) = Ok d` for every delta.
    let decodeJson (el: JVal) : Result<SchemaDelta, ColumnError> =
        items "added" (entry "added") el
        |> Result.bind (fun added ->
            items "removed" (entry "removed") el
            |> Result.bind (fun removed ->
                items "retyped" retypedEntry el
                |> Result.bind (fun retyped ->
                    field "reordered" el
                    |> Result.bind (fun r ->
                        match r with
                        | JBool b -> Ok b
                        | other -> Error(MalformedShape("reordered: expected bool, got " + JVal.kindName other)))
                    |> Result.bind (fun reordered ->
                        items "order" (text "order") el
                        |> Result.map (fun order ->
                            { Added = added
                              Removed = removed
                              Retyped = retyped
                              Reordered = reordered
                              Order = order })))))

    /// Decode a wire string into a delta (a JSON-syntax failure is `NotJson`).
    let decode (s: string) : Result<SchemaDelta, ColumnError> =
        match Json.parseDetailed s with
        | Error e -> Error(NotJson e)
        | Ok el -> decodeJson el

    /// The `Corpus.Codec` over `SchemaDelta`, for the conformance corpus tooling.
    let codec: Corpus.Codec<SchemaDelta> =
        { Encode = encode
          Decode = fun s -> decode s |> Result.mapError ColumnCodec.errorString }
