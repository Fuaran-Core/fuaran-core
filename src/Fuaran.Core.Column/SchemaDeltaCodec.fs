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

    let private stated (name: string) (v: string option) : (string * JVal) list =
        match v with
        | Some s -> [ name, JStr s ]
        | None -> []

    let private amendedJson (name: string, change: FieldChange) : JVal =
        let entry (memberName: string) (key: (string * JVal) list) (before: string option) (after: string option) =
            JObj(
                [ "name", JStr name; "member", JStr memberName ]
                @ key
                @ stated "from" before
                @ stated "to" after
            )

        match change with
        | FieldChange.Unit(b, a) -> entry "unit" [] (Option.map Unit.render b) (Option.map Unit.render a)
        | FieldChange.Label(b, a) -> entry "label" [] b a
        | FieldChange.Description(b, a) -> entry "description" [] b a
        | FieldChange.Ext(key, b, a) -> entry "ext" [ "key", JStr key ] b a

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

    /// An optional text member of an `amended` entry: absent is `None`, present must be a string.
    let private optionalText (ctx: string) (name: string) (el: JVal) : Result<string option, ColumnError> =
        match Decode.tryProp name el with
        | None -> Ok None
        | Some v -> text (ctx + "." + name) v |> Result.map Some

    let private unitOf (ctx: string) (name: string) (v: string option) : Result<UnitOfMeasure option, ColumnError> =
        match v with
        | None -> Ok None
        | Some s ->
            match Unit.parse s with
            | Ok u -> Ok(Some u)
            | Error _ -> Error(MalformedShape(ctx + "." + name + ": '" + s + "' is not a unit the kit parses"))

    let private amendedEntry (el: JVal) : Result<string * FieldChange, ColumnError> =
        field "name" el
        |> Result.bind (text "amended.name")
        |> Result.bind (fun name ->
            field "member" el
            |> Result.bind (text "amended.member")
            |> Result.bind (fun memberName ->
                optionalText "amended" "from" el
                |> Result.bind (fun before ->
                    optionalText "amended" "to" el
                    |> Result.bind (fun after ->
                        match memberName with
                        | "unit" ->
                            unitOf "amended" "from" before
                            |> Result.bind (fun b ->
                                unitOf "amended" "to" after
                                |> Result.map (fun a -> name, FieldChange.Unit(b, a)))
                        | "label" -> Ok(name, FieldChange.Label(before, after))
                        | "description" -> Ok(name, FieldChange.Description(before, after))
                        | "ext" ->
                            field "key" el
                            |> Result.bind (text "amended.key")
                            |> Result.map (fun key -> name, FieldChange.Ext(key, before, after))
                        | other ->
                            Error(
                                MalformedShape(
                                    "amended.member: expected one of unit, label, description, ext; got '"
                                    + other
                                    + "'"
                                )
                            )))))

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
                    (match Decode.tryProp "amended" el with
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
        match Json.parseDetailed s with
        | Error e -> Error(NotJson e)
        | Ok el -> decodeJson el

    /// The `Corpus.Codec` over `SchemaDelta`, for the conformance corpus tooling.
    let codec: Corpus.Codec<SchemaDelta> =
        { Encode = encode
          Decode = fun s -> decode s |> Result.mapError ColumnCodec.errorString }
