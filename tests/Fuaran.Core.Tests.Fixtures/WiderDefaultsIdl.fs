namespace Fuaran.Core.Tests

// ============================================================================
//  Phase 656 — the fixture vocabularies for a DECLARED DEFAULT at each of the
//  wider data universe's eight types (Phase 639, DECISIONS.md D251). `at` is a
//  one-member vocabulary per type, which is what the go-red and the refusal
//  pins generate; `idl` declares a default at every type at once: an
//  omit-at-default member of each on the kind `Tally`, a smart-constructor
//  default at a required 64-bit integer, and a union default whose case
//  carries bytes and a tuple (so a literal also stands in PATTERN position, in
//  the F# encoder's omit test). Its twin kind `Plain` carries the same members
//  required and declares nothing, so a value decoded at `Tally` can be written
//  through `Plain` to show every member it was filled with.
// ============================================================================

module WiderDefaultsIdl =

    open Fuaran.Core.Idl

    let private field (name: string) (t: IdlType) (opt: Optionality) : IdlField =
        { Name = name
          Type = t
          Opt = opt
          Aliases = []
          Keyed = false
          Presence = None
          Annotations = Annotations.Empty }

    let private case (tag: string) (fields: IdlField list) : IdlUnionCase =
        { Tag = tag
          Fields = fields
          Annotations = Annotations.Empty
          Hosted = None }

    /// Each of the eight types, the member that carries it and a canonical default at it: a 64-bit
    /// integer above 2^53 (where a double would round), a negative decimal, bytes ending in padding,
    /// a tuple of a negative 64-bit integer and a string, the empty keyed map, the empty set, a time
    /// of day with a fraction and a negative duration.
    let defaults: (string * IdlType * IdlValue) list =
        [ "count", TInt64, VStr "9007199254740993"
          "amount", TDecimal, VStr "-12.5"
          "digest", TBytes, VStr "AQI="
          "pair", TTuple [ TInt64; TStr ], VList [ VStr "-5"; VStr "a" ]
          "levels", TKeyedMap(TInt64, TStr), VList []
          "tags", TSet TStr, VList []
          "at", TTimeOfDay, VStr "08:30:00.5"
          "span", TDuration, VStr "-1.5s" ]

    /// The value `Mark.At` takes as the `mark` member's default.
    let markDefault =
        VUnion("At", [ "stamp", VStr "AQI="; "pair", VList [ VStr "-5"; VStr "a" ] ])

    let private mark: IdlUnion =
        { Name = "Mark"
          Params = []
          Cases =
            [ case
                  "At"
                  [ field "stamp" TBytes Required
                    field "pair" (TTuple [ TInt64; TStr ]) Required ]
              case "Unset" [] ]
          Annotations = Annotations.Empty
          TagRule = UnionTagRule.CaseName
          Discrimination = UnionDiscrimination.Tagged }

    let private vocabulary (kinds: IdlKind list) (defaults: IdlDefault list) : Idl =
        { Kinds = kinds
          Unions = [ mark ]
          Enums = []
          Records = []
          Defaults = defaults
          NodeFields = []
          Ops = []
          Wire = WireShape.Default
          Harden = HardenPolicy.Undeclared }

    /// The one-member vocabulary: the kind `Holder` with a required `name` and the member `x` of type
    /// `t`, omitted at the default `d`.
    let at (t: IdlType) (d: IdlValue) : Idl =
        vocabulary
            [ { Tag = "Holder"
                Category = "Display"
                Fields = [ field "name" TStr Required; field "x" t (OmitDefault d) ]
                Annotations = Annotations.Empty } ]
            []

    /// The members `Tally` and `Plain` share, in declaration order, with the optionality each takes.
    let private members (declared: bool) : IdlField list =
        [ for name, t, d in defaults do
              field name t (if declared then OmitDefault d else Required)
          field "seed" TInt64 Required
          field "mark" (TUnion("Mark", [])) (if declared then OmitDefault markDefault else Required) ]

    /// The full fixture: `Tally` declares a default at every type (and `seed` a smart-constructor
    /// default), `Plain` the same members, required, with none.
    let idl: Idl =
        vocabulary
            [ { Tag = "Tally"
                Category = "Display"
                Fields = members true
                Annotations = Annotations.Empty }
              { Tag = "Plain"
                Category = "Display"
                Fields = members false
                Annotations = Annotations.Empty } ]
            [ { Kind = "Tally"
                Field = "seed"
                Value = VStr "7" } ]

    /// The kinds the tests generate.
    let kinds = [ "Tally"; "Plain" ]

    /// Every member name of `Tally`, in declaration order.
    let memberNames = members true |> List.map _.Name
