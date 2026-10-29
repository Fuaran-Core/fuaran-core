module Fuaran.Core.Tests.IdentityIdl

// The identity rule's reference vocabulary (Phase 578, `docs/identity-rule.md`): a small record declared
// through the IDL as any domain would declare one, used ONLY to emit the `identity/` vectors. It is shaped
// like a provenance record — a method digest, a list of source fingerprints, a value, and one member that
// always has a value and is omitted at its default — and it is a FIXTURE: Core ships no such type and no
// such vocabulary, and a domain that wants one declares its own and follows the rule.

open Fuaran.Core
open Fuaran.Core.Idl

let private f (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Aliases = []
      Keyed = false
      Presence = None
      Annotations = Annotations.Empty }

/// The record's tag. The IDL interpreter's public encoder has two roots, a node and a `$type`-tagged value
/// (`Encode.encode`, `Encode.encodeOp`); a node carries an id, which an identity cannot be computed over,
/// so the record is declared as the tagged root, and its tag is one of its members.
[<Literal>]
let RecordTag = "Provenance"

/// The member `unit` is omitted when it holds this value.
[<Literal>]
let DefaultUnit = "1"

/// The vocabulary: one tagged record, the default wire shape (Ordinal key order, `$type`).
let idl: Idl =
    { Kinds = []
      Unions = []
      Enums = []
      Records = []
      Defaults = []
      NodeFields = []
      Ops =
        [ { Tag = RecordTag
            Category = "record"
            Annotations = Annotations.Empty
            Fields =
              [ f "method" TStr Required
                f "sources" (TList TStr) Required
                f "value" TStr Required
                f "unit" TStr (OmitDefault(VStr DefaultUnit)) ] } ]
      Wire = WireShape.Default
      Harden = HardenPolicy.Undeclared }

/// The declared values of one record. `Value` is a decimal number's text, and the vocabulary admits one
/// text per number (`DecimalText.tryCanonical`); `Unit` is `None` when the author left it out.
type Declared =
    { Method: string
      Sources: string list
      Value: string
      Unit: string option }

/// The record as an authored value. A `Value` that is not its own canonical decimal text is refused: two
/// texts for one number would be two addresses for one record.
let authored (d: Declared) : Result<IdlValue, string> =
    match DecimalText.tryCanonical d.Value with
    | Some c when c = d.Value ->
        Ok(
            VUnion(
                RecordTag,
                [ "method", VStr d.Method
                  "sources", VList(d.Sources |> List.map VStr)
                  "value", VStr d.Value ]
                @ (d.Unit |> Option.map (fun u -> [ "unit", VStr u ]) |> Option.defaultValue [])
            )
        )
    | Some c -> Error(sprintf "the value '%s' is not its canonical decimal text ('%s')" d.Value c)
    | None -> Error(sprintf "the value '%s' is not a decimal number" d.Value)

/// The record's canonical form under its vocabulary, as the value that canonical text reads as: members in
/// the vocabulary's canonical order, absent optionals omitted, a default not materialised.
let canonicalValue (v: IdlValue) : Result<JVal, string> =
    Encode.encodeOp idl v
    |> Result.bind (fun text -> Canonical.read text |> Result.mapError DecodeError.describe)

/// The record a JSON text declares, read through the vocabulary — the route a host that received the
/// record as text takes before it mints.
let read (text: string) : Result<IdlValue, string> = Decode.decodeOp idl text

/// The rule, minted the way a host without `ContentAddress` mints it: `sha256:` and the lower-case hex
/// SHA-256 of the UTF-8 bytes of the guarded rendering of the canonical value under `profile`. The suite
/// holds `ContentAddress.ofValue` to it, vector by vector.
let address (profile: EncodingProfile) (canonical: JVal) : Result<string, string> =
    Canonical.tryWrite (Rendering.ofProfile profile) canonical
    |> Result.map (fun text -> "sha256:" + Hash.sha256Hex text)
