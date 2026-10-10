namespace Fuaran.Core

/// The fixed, Arrow-compatible scalar type set a column ranges over (spec §1). Closed by
/// intent — a new scalar type is an additive case, never an open extension point.
type ColumnType =
    /// 32-bit signed integers, tagged `int`. An int `Sum` outside the int32 range is refused, never
    /// wrapped.
    | IntType
    /// IEEE doubles, tagged `float`; `Int` cells widen into it. Present values must be finite to
    /// encode — the wire has no NaN or infinity.
    | FloatType
    /// Booleans, tagged `bool`; ordered `false` before `true`.
    | BoolType
    /// Free text, tagged `string`; ordered ordinally, never by culture.
    | StringType
    /// Calendar days, held as `int32` days since 1970-01-01 (Arrow's Date32, Phase 422) and written
    /// as canonical `YYYY-MM-DD` text.
    | DateType
    /// UTC instants at a `TimeUnit` (Phase 422, Arrow's `Timestamp(unit)`), held as integer-valued
    /// epoch seconds plus, for a sub-second unit, the fraction scaled to the unit; written as the
    /// canonical instant text (`TemporalText`). Tagged `timestamp` (seconds), `timestamp_ms`,
    /// `timestamp_us` or `timestamp_ns`; a coarser unit widens into a finer one. The decoder also
    /// reads an epoch number: in a `timestamp` column in seconds or milliseconds by magnitude, in a
    /// sub-second column in its own unit (DECISIONS.md D143.5).
    | TimestampType of unit: TimeUnit
    /// An EXACT decimal (`0.33.0`): a value a `float` cannot hold without rounding, such as a sum of
    /// money. Unparameterised — it carries no precision and no scale, because the text a cell
    /// carries has exactly the digits it has. A host that maps it to a fixed-scale store type reads
    /// the scale off the data or declares it in its own model.
    | DecimalType

/// The wire tags of the scalar types and the widening lattice — the one place both the codec and
/// schema compatibility read "is this retype safe" from.
module ColumnType =

    /// The canonical wire tag for a column type (the fixed scalar-set vocabulary).
    let tag (t: ColumnType) : string =
        match t with
        | IntType -> "int"
        | FloatType -> "float"
        | BoolType -> "bool"
        | StringType -> "string"
        | DateType -> "date"
        | TimestampType TimeUnit.Seconds -> "timestamp"
        | TimestampType TimeUnit.Milliseconds -> "timestamp_ms"
        | TimestampType TimeUnit.Microseconds -> "timestamp_us"
        | TimestampType TimeUnit.Nanoseconds -> "timestamp_ns"
        | DecimalType -> "decimal"

    /// The type's position in `all` — the identity `widens` compares by, as an integer (Phase 353).
    let private ordinal (t: ColumnType) : int =
        match t with
        | IntType -> 0
        | FloatType -> 1
        | BoolType -> 2
        | StringType -> 3
        | DateType -> 4
        | TimestampType TimeUnit.Seconds -> 5
        | DecimalType -> 6
        | TimestampType TimeUnit.Milliseconds -> 7
        | TimestampType TimeUnit.Microseconds -> 8
        | TimestampType TimeUnit.Nanoseconds -> 9

    /// The full closed set of valid type tags — the `UnknownType` enumeration (and the encode order).
    /// The three sub-second timestamp units (Phase 422) are APPENDED after `DecimalType`, so the
    /// enumeration keeps its earlier prefix in its earlier order.
    let all =
        [ IntType
          FloatType
          BoolType
          StringType
          DateType
          TimestampType TimeUnit.Seconds
          DecimalType
          TimestampType TimeUnit.Milliseconds
          TimestampType TimeUnit.Microseconds
          TimestampType TimeUnit.Nanoseconds ]

    /// The wire tags in `all`'s order — the tags of the `expected` list an `UnknownType` refusal
    /// carries.
    let allTags = all |> List.map tag

    /// Resolve a wire tag to its type, or `None` for an unknown tag.
    let ofTag (s: string) : ColumnType option =
        all |> List.tryFind (fun t -> tag t = s)

    /// The pinned type-widening lattice (Phase 33). A `from`→`target` change is a *safe widening* iff it
    /// is the identity or the one lossless promotion the rest of the strand already pins: `Int → Float`
    /// (`ColumnCodec.decodeCell` decodes a JSON int into a `FloatType` column; the compute
    /// repository's `Fuaran.Compute.DataFrame` arithmetic promotes int operands to float). This is the
    /// single source of truth for "is a retype safe" — the schema-compatibility check and the
    /// codec/evaluator coercion agree by construction, not by a second rule-set.
    ///
    /// `Int → Decimal` is the second lossless promotion (`0.33.0`), and the codec agrees with it the
    /// same way: `ColumnCodec.decodeCell` decodes a JSON int into a `DecimalType` column. `Float →
    /// Decimal` is NOT a widening, in either direction: a float is an approximation and a decimal is
    /// a statement of digits, so a retype between them changes what the column claims.
    ///
    /// A coarser timestamp unit widens into a finer one (Phase 422: `timestamp` into
    /// `timestamp_ms`, and so on, `TimeUnit.widens`) — the scaling is exact, so the retype is
    /// lossless; a finer unit never widens into a coarser one.
    ///
    /// Read by pattern, not by `=` (Phase 353): a union's `=` is a structural-equality call under
    /// Fable, and `Column.aggregate` asks this once per cell. The identity is `ordinal`'s, whose
    /// match is exhaustive, so a new type cannot silently fail to widen into itself.
    let widens (from: ColumnType) (target: ColumnType) : bool =
        match from, target with
        | IntType, (FloatType | DecimalType) -> true
        | TimestampType a, TimestampType b -> TimeUnit.widens a b
        | _ -> ordinal from = ordinal target
