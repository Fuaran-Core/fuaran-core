module Fuaran.Core.Tests.ScoreDomainIdl

// The THIRD-DOMAIN certification vocabulary: a notation vocabulary with a corpus written outside this
// repository. Declared here, in the fixtures library, because the tool generates from it
// (`render generated`, `render fstar`); `ScoreDomainSpike` is its family.

open Fuaran.Core
open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// The declared slice
// ---------------------------------------------------------------------------

let private f (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Aliases = []
      Presence = None
      Annotations = Annotations.Empty }

let private req name t = f name t Required
let private opt name t = f name t Optional
let private omit name t d = f name t (OmitDefault d)

let private kind tag category fields : IdlKind =
    { Tag = tag
      Category = category
      Annotations = Annotations.Empty
      Fields = fields }

let private record name fields : IdlRecord =
    { Name = name
      Fields = fields
      Annotations = Annotations.Empty }

let private children = req "children" (TList TNode)

/// A slice of a score-shaped vocabulary's node set — every kind its sample
/// corpus reaches: ensemble structure (score / part-group / part / measure /
/// staff), pitched events (note / chord / grace note), spanners entering and
/// leaving (hairpins, slurs, octave shifts), point marks (dynamic, fermata,
/// ornament, rehearsal and navigation marks), a multi-measure rest, and a form
/// summary whose sections are records carrying node lists.
let scoreIdl: Idl =
    { Kinds =
        [ kind "Score" "structure" [ opt "title" TStr; opt "composer" TStr; children ]
          kind "Part" "structure" [ req "name" TStr; req "staves" (TList(TRecord "StaffDefinition")); children ]
          kind "PartGroup" "structure" [ opt "name" TStr; req "bracket" (TEnum "BracketKind"); children ]
          kind
              "Measure"
              "structure"
              [ req "number" TInt
                omit "repeatStart" TBool (VBool false)
                omit "repeatEnd" TBool (VBool false)
                opt "volta" (TList TInt)
                omit "isAnacrusis" TBool (VBool false)
                children ]
          kind "Staff" "structure" [ req "staffNumber" TInt; children ]
          kind
              "Note"
              "event"
              [ req "pitch" (TRecord "Pitch")
                req "duration" (TRecord "Duration")
                omit "voice" TInt (VInt 1)
                omit "tiedToNext" TBool (VBool false) ]
          kind
              "Chord"
              "event"
              [ req "pitches" (TList(TRecord "Pitch"))
                req "duration" (TRecord "Duration")
                omit "voice" TInt (VInt 1)
                omit "tiedToNext" TBool (VBool false) ]
          kind "GraceNote" "event" [ req "pitch" (TRecord "Pitch"); req "grace" (TEnum "GraceKind") ]
          kind "Dynamic" "mark" [ req "level" (TEnum "DynamicLevel") ]
          kind "Fermata" "mark" []
          kind "HairpinStart" "spanner" [ req "hairpin" (TEnum "HairpinKind") ]
          kind "HairpinEnd" "spanner" []
          kind "SlurStart" "spanner" []
          kind "SlurEnd" "spanner" []
          kind "OctaveShiftStart" "spanner" [ req "octaveShift" (TEnum "OctaveShiftKind") ]
          kind "OctaveShiftEnd" "spanner" []
          kind "MultiRest" "event" [ req "measureCount" TInt ]
          // Finding (3) nuance: the ornament set's tremolo variant carries a
          // payload, and the wire flattens it beside the enum-valued field
          // rather than tagging a union — declared here exactly as flattened.
          kind "Ornament" "mark" [ req "ornament" (TEnum "OrnamentName"); opt "slashCount" TInt ]
          kind "RehearsalMark" "mark" [ req "label" TStr ]
          kind "NavigationMark" "mark" [ req "navigation" (TEnum "NavigationKind") ]
          kind
              "Form"
              "structure"
              [ opt "name" TStr
                req "sections" (TList(TRecord "FormSection"))
                req "arrangement" (TList TStr) ] ]
      // Finding (3): NO value union at all — the only discriminated union on
      // this vocabulary's wire is the node discriminator itself.
      Unions = []
      Enums =
        [ Declare.enumOf "NoteLetter" [ "C"; "D"; "E"; "F"; "G"; "A"; "B" ]
          Declare.enumOf "Accidental" [ "DoubleFlat"; "Flat"; "Natural"; "Sharp"; "DoubleSharp" ]
          Declare.enumOf
              "BaseDuration"
              [ "Whole"
                "Half"
                "Quarter"
                "Eighth"
                "Sixteenth"
                "ThirtySecond"
                "SixtyFourth" ]
          Declare.enumOf
              "Mode"
              [ "Ionian"
                "Dorian"
                "Phrygian"
                "Lydian"
                "Mixolydian"
                "Aeolian"
                "Locrian"
                "MelodicMinor"
                "Dorianb2"
                "LydianAugmented"
                "LydianDominant"
                "Mixolydianb6"
                "LocrianNatural2"
                "SuperLocrian"
                "HarmonicMinor"
                "LocrianNatural6"
                "IonianAugmented"
                "DorianSharp4"
                "PhrygianDominant"
                "LydianSharp2"
                "UltraLocrian" ]
          Declare.enumOf "ClefKind" [ "Treble"; "Bass"; "Alto"; "Tenor"; "Percussion" ]
          Declare.enumOf "BracketKind" [ "Brace"; "Square"; "Line"; "None" ]
          Declare.enumOf "HairpinKind" [ "Crescendo"; "Decrescendo" ]
          Declare.enumOf "OctaveShiftKind" [ "Ottava"; "OttavaBassa"; "Quindicesima"; "QuindicesimaBassa" ]
          Declare.enumOf "GraceKind" [ "Acciaccatura"; "Appoggiatura" ]
          Declare.enumOf "OrnamentName" [ "Trill"; "Turn"; "InvertedTurn"; "Mordent"; "InvertedMordent"; "Tremolo" ]
          Declare.enumOf
              "NavigationKind"
              [ "Segno"
                "Coda"
                "DaCapo"
                "DaCapoAlFine"
                "DaCapoAlCoda"
                "DalSegno"
                "DalSegnoAlFine"
                "DalSegnoAlCoda"
                "Fine"
                "ToCoda" ]
          Declare.enumOf
              "DynamicLevel"
              [ "Pianississimo"
                "Pianissimo"
                "Piano"
                "MezzoPiano"
                "MezzoForte"
                "Forte"
                "Fortissimo"
                "Fortississimo"
                "Sforzando"
                "Forzato"
                "Rinforzando"
                "FortePiano"
                "SforzandoPiano" ] ]
      Records =
        [ record
              "Pitch"
              [ req "letter" (TEnum "NoteLetter")
                req "accidental" (TEnum "Accidental")
                req "octave" TInt
                // Finding (8): redundant derived value, declared as plain data.
                req "midi" TInt ]
          record
              "Duration"
              [ req "base" (TEnum "BaseDuration")
                // Finding (4): omit-at-default INSIDE a record position.
                omit "dots" TInt (VInt 0) ]
          record
              "KeySignature"
              [ req "tonic" (TEnum "NoteLetter")
                req "tonicAccidental" (TEnum "Accidental")
                req "mode" (TEnum "Mode") ]
          record "TimeSignature" [ req "numerator" TInt; req "denominator" TInt ]
          record
              "StaffDefinition"
              [ req "number" TInt
                req "clef" (TEnum "ClefKind")
                req "initialKey" (TRecord "KeySignature")
                req "initialTime" (TRecord "TimeSignature") ]
          // Finding (5): a record carrying a NODE LIST — record-over-tree recursion.
          record "FormSection" [ req "label" TStr; children ] ]
      Defaults = []
      NodeFields = []
      Ops = []
      // Findings (1)/(2), closed: the wire shape is DECLARED (Phases 108/109) —
      // a bare-string `kind` discriminator, and the flat node envelope.
      Wire =
        { Discriminator = "kind"
          NodeEnvelope = NodeEnvelopeShape.FlatKind
          KeyOrder = KeyOrder.Declared }
      Harden = HardenPolicy.Undeclared }
