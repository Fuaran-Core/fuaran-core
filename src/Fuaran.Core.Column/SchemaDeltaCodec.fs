namespace Fuaran.Core

/// The canonical wire codec for a `SchemaDelta` (Phase 317) — what a host that records deltas beside
/// `Schema.fingerprint` as provenance writes, so that `Schema.patch` can replay them later. One
/// object with five members always present — `added` and `removed` as schema entries (the columnar
/// codec's, `ColumnCodec.fieldJson`: `{name, type}` and the metadata members a field states),
/// `retyped` as `{name, from, to}`, `reordered` a bool, and `order` the column names `Order` carries
/// — and, since Phase 427, `amended` ONLY WHEN NON-EMPTY: one `{name, member, from?, to?}` per
/// metadata change (`member` one of `unit`, `label`, `description`, `ext`; an `ext` entry carries its
/// `key`; `from` and `to` are the member's text before and after, each absent where the field stated
/// none, a unit as its canonical text), so every delta between schemas without metadata keeps the
/// bytes it had. Rendered under `Canon`, so the bytes are canonical across hosts; decode surfaces the
/// columnar codec envelope (`ColumnError`) and reads an absent `amended` as empty. It decodes what it
/// is handed and judges nothing about the schema the delta will meet — that is `Schema.patch`'s
/// question.
module SchemaDeltaCodec =

    // Phase 551: an `amended` entry is the field's `name` beside a `FieldChange`, whose members (its
    // `member` discriminator, `from`, `to` and an `ext` entry's `key`) are written and read by the codec
    // DERIVED from the union's IDL declaration (`FieldChangeSeamCodec`, DECISIONS.md D200).

    let private amendedJson (name: string, change: FieldChange) : JVal =
        match FieldChangeSeamCodec.encodeFieldChange change with
        | JObj members -> JObj(("name", JStr name) :: members)
        | other -> other

    /// Encode a delta to a `JVal`; `encode` renders it under `Canon`, which sorts the keys.
    let encodeJson (d: SchemaDelta) : JVal =
        JObj(
            [ "added", JArr(d.Added |> List.map ColumnCodec.fieldJson)
              "removed", JArr(d.Removed |> List.map ColumnCodec.fieldJson)
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
            @ (if d.Amended.IsEmpty then
                   []
               else
                   [ "amended", JArr(d.Amended |> List.map amendedJson) ])
        )

    /// The canonical wire string for a delta. Total: every delta encodes.
    let encode (d: SchemaDelta) : string =
        Canonical.write Rendering.CrossHost (encodeJson d)

    let private field (name: string) (el: JVal) : Result<JVal, ColumnError> =
        Decoder.field name Decoder.json el
        |> Result.mapError (ColumnCodec.ofDecodeError name)

    let private text (ctx: string) (el: JVal) : Result<string, ColumnError> =
        Decoder.str el |> Result.mapError (ColumnCodec.ofDecodeError ctx)

    let private columnType (ctx: string) (el: JVal) : Result<ColumnType, ColumnError> =
        text ctx el
        |> Result.bind (fun tag ->
            match ColumnType.ofTag tag with
            | Some ty -> Ok ty
            | None -> Error(UnknownType(tag, ColumnType.all)))

    let private items (name: string) (read: JVal -> Result<'a, ColumnError>) (el: JVal) : Result<'a list, ColumnError> =
        field name el
        |> Result.bind (Decoder.items >> Result.mapError (ColumnCodec.ofDecodeError name))
        |> Result.bind (fun xs ->
            let rec go acc =
                function
                | [] -> Ok(List.rev acc)
                | x :: rest ->
                    match read x with
                    | Ok v -> go (v :: acc) rest
                    | Error e -> Error e

            go [] xs)

    /// A derived reader's refusal of an `amended` entry, in this codec's envelope: placed by the member
    /// it names (`amended.from: expected string, got int`).
    let private amendedRefused (e: DecodeError) : ColumnError =
        match List.tryLast e.Path with
        | Some(PathSegment.Key name) -> ColumnCodec.ofDecodeError ("amended." + name) e
        | _ -> ColumnCodec.ofDecodeError "amended" e

    let private amendedEntry (el: JVal) : Result<string * FieldChange, ColumnError> =
        field "name" el
        |> Result.bind (text "amended.name")
        |> Result.bind (fun name ->
            FieldChangeSeamCodec.decodeFieldChange el
            |> Result.mapError amendedRefused
            |> Result.map (fun change -> name, change))

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
        items "added" ColumnCodec.decodeField el
        |> Result.bind (fun added ->
            items "removed" ColumnCodec.decodeField el
            |> Result.bind (fun removed ->
                items "retyped" retypedEntry el
                |> Result.bind (fun retyped ->
                    (match Decoder.tryMember "amended" el with
                     | None -> Ok []
                     | Some _ -> items "amended" amendedEntry el)
                    |> Result.bind (fun amended ->
                        field "reordered" el
                        |> Result.bind (fun r ->
                            match r with
                            | JBool b -> Ok b
                            | other ->
                                Error(MalformedShape("reordered: expected bool, got " + JVal.kindName other)))
                        |> Result.bind (fun reordered ->
                            items "order" (text "order") el
                            |> Result.map (fun order ->
                                { Added = added
                                  Removed = removed
                                  Retyped = retyped
                                  Amended = amended
                                  Reordered = reordered
                                  Order = order }))))))

    /// Decode a wire string into a delta (a JSON-syntax failure is `NotJson`).
    let decode (s: string) : Result<SchemaDelta, ColumnError> =
        Json.read ReadOptions.strict s
        |> Result.mapError ColumnCodec.ofJsonRefusal
        |> Result.bind decodeJson

    /// The `Corpus.Codec` over `SchemaDelta`, for the conformance corpus tooling.
    let codec: Corpus.Codec<SchemaDelta> =
        { Encode = encode
          Decode = fun s -> decode s |> Result.mapError ColumnCodec.errorString }
