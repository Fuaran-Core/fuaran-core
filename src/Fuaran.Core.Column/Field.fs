namespace Fuaran.Core

/// A schema entry (Phase 427): a column's name and type, and the metadata that says what the column
/// MEANS — an optional `UnitOfMeasure` (Phase 426's runtime unit), an optional label, an optional
/// description, and an extension map of namespaced keys to strings that every host preserves
/// verbatim and Core never interprets. A `Schema` is a `Field list`.
///
/// **Opaque, built through functions.** A field is built with `Field.create name ty` and refined with
/// the `Field.with…` builders (`Field.create "m" FloatType |> Field.withUnit kg |> Field.withLabel
/// "Payload mass"`), and read through the properties. Widening a tuple is a breaking change, and so is
/// adding a field to a public F# record (every full-literal construction stops compiling, FS0764), so
/// the representation is internal: the next metadata member is a new builder and a new property,
/// never a break.
///
/// **Metadata is part of a field's identity.** Equality is structural over every member, so two
/// fields differing only in a unit are different fields, and two schemas differing only in a unit are
/// different schemas (`Schema.diff` reports the change as `Amended`, and `Schema.fingerprint` moves
/// with it).
///
/// **Metadata is meaning, not storage.** A unit is not a `ColumnType` parameter: a `Float` in
/// kilograms and a `Float` in metres hold the same bytes, and the typed column (`Column`, Phase 417)
/// carries only its name and its storage — a column's field is its schema entry, read by name
/// (`Table.tryField`). The timestamp unit of Phase 422 is different in kind: it decides how an
/// instant is stored and so lives inside `ColumnType`.
type Field =
    internal
        {
            /// The column name — the key a table matches a column against; unique within a schema.
            FieldName: string
            /// The column type.
            FieldType: ColumnType
            /// The unit the column's values are measured in, when stated.
            FieldUnit: UnitOfMeasure option
            /// A short display name for the column, when stated.
            FieldLabel: string option
            /// A sentence saying what the column holds, when stated.
            FieldDescription: string option
            /// Namespaced extension members (`vendor.key` by convention) a host preserves verbatim.
            FieldExt: Map<string, string>
        }

    /// The column name — the key a table matches a column against; unique within a schema.
    member f.Name: string = f.FieldName

    /// The column type.
    member f.Type: ColumnType = f.FieldType

    /// The unit the column's values are measured in — `None` when the field states none.
    member f.Unit: UnitOfMeasure option = f.FieldUnit

    /// A short display name for the column — `None` when the field states none.
    member f.Label: string option = f.FieldLabel

    /// A sentence saying what the column holds — `None` when the field states none.
    member f.Description: string option = f.FieldDescription

    /// The extension members, keyed by their namespaced key; empty when the field carries none. Core
    /// preserves them verbatim and interprets none.
    member f.Ext: Map<string, string> = f.FieldExt

/// The builders and readers of a `Field` (Phase 427). Every builder is total and answers a new field;
/// the one it was given is unchanged.
module Field =

    /// A field of `name` and `ty` with no metadata — the entry `(name, ty)` spelled before Phase 427.
    let create (name: string) (ty: ColumnType) : Field =
        { FieldName = name
          FieldType = ty
          FieldUnit = None
          FieldLabel = None
          FieldDescription = None
          FieldExt = Map.empty }

    /// The field with its type replaced and every other member kept — what `Schema.patch` applies a
    /// `Retyped` entry with.
    let withType (ty: ColumnType) (f: Field) : Field = { f with FieldType = ty }

    /// The field measured in `unit`.
    let withUnit (unit: UnitOfMeasure) (f: Field) : Field = { f with FieldUnit = Some unit }

    /// The field with no unit stated.
    let withoutUnit (f: Field) : Field = { f with FieldUnit = None }

    /// The field labelled `label`.
    let withLabel (label: string) (f: Field) : Field = { f with FieldLabel = Some label }

    /// The field with no label stated.
    let withoutLabel (f: Field) : Field = { f with FieldLabel = None }

    /// The field described by `description`.
    let withDescription (description: string) (f: Field) : Field =
        { f with
            FieldDescription = Some description }

    /// The field with no description stated.
    let withoutDescription (f: Field) : Field = { f with FieldDescription = None }

    /// The field with the extension member `key` set to `value` (replacing one of that key). Keys are
    /// namespaced by convention (`vendor.key`); Core checks no shape and preserves the pair verbatim.
    let withExt (key: string) (value: string) (f: Field) : Field =
        { f with
            FieldExt = Map.add key value f.FieldExt }

    /// The field without the extension member `key` (unchanged when it carries none).
    let withoutExt (key: string) (f: Field) : Field =
        { f with
            FieldExt = Map.remove key f.FieldExt }

    /// Whether the field states any metadata — a unit, a label, a description or an extension member.
    /// A field that states none is exactly the entry `(name, ty)` was, and encodes to the bytes it did.
    let hasMetadata (f: Field) : bool =
        f.FieldUnit.IsSome
        || f.FieldLabel.IsSome
        || f.FieldDescription.IsSome
        || not f.FieldExt.IsEmpty
