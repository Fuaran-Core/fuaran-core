// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen 0.34.0. Do not edit by hand.
module Fuaran.Core.Tests.ScoreGenerated

open Fuaran.Core

[<RequireQualifiedAccess>]
type NoteLetter =
    | C
    | D
    | E
    | F
    | G
    | A
    | B

[<RequireQualifiedAccess>]
type Accidental =
    | DoubleFlat
    | Flat
    | Natural
    | Sharp
    | DoubleSharp

[<RequireQualifiedAccess>]
type BaseDuration =
    | Whole
    | Half
    | Quarter
    | Eighth
    | Sixteenth
    | ThirtySecond
    | SixtyFourth

[<RequireQualifiedAccess>]
type Mode =
    | Ionian
    | Dorian
    | Phrygian
    | Lydian
    | Mixolydian
    | Aeolian
    | Locrian
    | MelodicMinor
    | Dorianb2
    | LydianAugmented
    | LydianDominant
    | Mixolydianb6
    | LocrianNatural2
    | SuperLocrian
    | HarmonicMinor
    | LocrianNatural6
    | IonianAugmented
    | DorianSharp4
    | PhrygianDominant
    | LydianSharp2
    | UltraLocrian

[<RequireQualifiedAccess>]
type ClefKind =
    | Treble
    | Bass
    | Alto
    | Tenor
    | Percussion

[<RequireQualifiedAccess>]
type BracketKind =
    | Brace
    | Square
    | Line
    | None

[<RequireQualifiedAccess>]
type HairpinKind =
    | Crescendo
    | Decrescendo

[<RequireQualifiedAccess>]
type OctaveShiftKind =
    | Ottava
    | OttavaBassa
    | Quindicesima
    | QuindicesimaBassa

[<RequireQualifiedAccess>]
type GraceKind =
    | Acciaccatura
    | Appoggiatura

[<RequireQualifiedAccess>]
type OrnamentName =
    | Trill
    | Turn
    | InvertedTurn
    | Mordent
    | InvertedMordent
    | Tremolo

[<RequireQualifiedAccess>]
type NavigationKind =
    | Segno
    | Coda
    | DaCapo
    | DaCapoAlFine
    | DaCapoAlCoda
    | DalSegno
    | DalSegnoAlFine
    | DalSegnoAlCoda
    | Fine
    | ToCoda

[<RequireQualifiedAccess>]
type DynamicLevel =
    | Pianississimo
    | Pianissimo
    | Piano
    | MezzoPiano
    | MezzoForte
    | Forte
    | Fortissimo
    | Fortississimo
    | Sforzando
    | Forzato
    | Rinforzando
    | FortePiano
    | SforzandoPiano

type Pitch =
    {
      Letter: NoteLetter
      Accidental: Accidental
      Octave: int
      Midi: int
    }

and Duration =
    {
      Base: BaseDuration
      Dots: int
    }

and KeySignature =
    {
      Tonic: NoteLetter
      TonicAccidental: Accidental
      Mode: Mode
    }

and TimeSignature =
    {
      Numerator: int
      Denominator: int
    }

and StaffDefinition =
    {
      Number: int
      Clef: ClefKind
      InitialKey: KeySignature
      InitialTime: TimeSignature
    }

and FormSection =
    {
      Label: string
      Children: Node list
    }

// structure
and ScoreSpec =
    {
      Title: string option
      Composer: string option
      Children: Node list
    }

// structure
and PartSpec =
    {
      Name: string
      Staves: StaffDefinition list
      Children: Node list
    }

// structure
and PartGroupSpec =
    {
      Name: string option
      Bracket: BracketKind
      Children: Node list
    }

// structure
and MeasureSpec =
    {
      Number: int
      RepeatStart: bool
      RepeatEnd: bool
      Volta: int list option
      IsAnacrusis: bool
      Children: Node list
    }

// structure
and StaffSpec =
    {
      StaffNumber: int
      Children: Node list
    }

// event
and NoteSpec =
    {
      Pitch: Pitch
      Duration: Duration
      Voice: int
      TiedToNext: bool
    }

// event
and ChordSpec =
    {
      Pitches: Pitch list
      Duration: Duration
      Voice: int
      TiedToNext: bool
    }

// event
and GraceNoteSpec =
    {
      Pitch: Pitch
      Grace: GraceKind
    }

// mark
and DynamicSpec =
    {
      Level: DynamicLevel
    }

// mark
and FermataSpec =
    | FermataSpec

// spanner
and HairpinStartSpec =
    {
      Hairpin: HairpinKind
    }

// spanner
and HairpinEndSpec =
    | HairpinEndSpec

// spanner
and SlurStartSpec =
    | SlurStartSpec

// spanner
and SlurEndSpec =
    | SlurEndSpec

// spanner
and OctaveShiftStartSpec =
    {
      OctaveShift: OctaveShiftKind
    }

// spanner
and OctaveShiftEndSpec =
    | OctaveShiftEndSpec

// event
and MultiRestSpec =
    {
      MeasureCount: int
    }

// mark
and OrnamentSpec =
    {
      Ornament: OrnamentName
      SlashCount: int option
    }

// mark
and RehearsalMarkSpec =
    {
      Label: string
    }

// mark
and NavigationMarkSpec =
    {
      Navigation: NavigationKind
    }

// structure
and FormSpec =
    {
      Name: string option
      Sections: FormSection list
      Arrangement: string list
    }

and [<RequireQualifiedAccess>] NodeKind =
    | Score of ScoreSpec
    | Part of PartSpec
    | PartGroup of PartGroupSpec
    | Measure of MeasureSpec
    | Staff of StaffSpec
    | Note of NoteSpec
    | Chord of ChordSpec
    | GraceNote of GraceNoteSpec
    | Dynamic of DynamicSpec
    | Fermata of FermataSpec
    | HairpinStart of HairpinStartSpec
    | HairpinEnd of HairpinEndSpec
    | SlurStart of SlurStartSpec
    | SlurEnd of SlurEndSpec
    | OctaveShiftStart of OctaveShiftStartSpec
    | OctaveShiftEnd of OctaveShiftEndSpec
    | MultiRest of MultiRestSpec
    | Ornament of OrnamentSpec
    | RehearsalMark of RehearsalMarkSpec
    | NavigationMark of NavigationMarkSpec
    | Form of FormSpec

and Node = { Id: string; Kind: NodeKind }

let private encNoteLetter (v: NoteLetter) : JVal =
    match v with
    | NoteLetter.C -> JStr "C"
    | NoteLetter.D -> JStr "D"
    | NoteLetter.E -> JStr "E"
    | NoteLetter.F -> JStr "F"
    | NoteLetter.G -> JStr "G"
    | NoteLetter.A -> JStr "A"
    | NoteLetter.B -> JStr "B"

let private encAccidental (v: Accidental) : JVal =
    match v with
    | Accidental.DoubleFlat -> JStr "DoubleFlat"
    | Accidental.Flat -> JStr "Flat"
    | Accidental.Natural -> JStr "Natural"
    | Accidental.Sharp -> JStr "Sharp"
    | Accidental.DoubleSharp -> JStr "DoubleSharp"

let private encBaseDuration (v: BaseDuration) : JVal =
    match v with
    | BaseDuration.Whole -> JStr "Whole"
    | BaseDuration.Half -> JStr "Half"
    | BaseDuration.Quarter -> JStr "Quarter"
    | BaseDuration.Eighth -> JStr "Eighth"
    | BaseDuration.Sixteenth -> JStr "Sixteenth"
    | BaseDuration.ThirtySecond -> JStr "ThirtySecond"
    | BaseDuration.SixtyFourth -> JStr "SixtyFourth"

let private encMode (v: Mode) : JVal =
    match v with
    | Mode.Ionian -> JStr "Ionian"
    | Mode.Dorian -> JStr "Dorian"
    | Mode.Phrygian -> JStr "Phrygian"
    | Mode.Lydian -> JStr "Lydian"
    | Mode.Mixolydian -> JStr "Mixolydian"
    | Mode.Aeolian -> JStr "Aeolian"
    | Mode.Locrian -> JStr "Locrian"
    | Mode.MelodicMinor -> JStr "MelodicMinor"
    | Mode.Dorianb2 -> JStr "Dorianb2"
    | Mode.LydianAugmented -> JStr "LydianAugmented"
    | Mode.LydianDominant -> JStr "LydianDominant"
    | Mode.Mixolydianb6 -> JStr "Mixolydianb6"
    | Mode.LocrianNatural2 -> JStr "LocrianNatural2"
    | Mode.SuperLocrian -> JStr "SuperLocrian"
    | Mode.HarmonicMinor -> JStr "HarmonicMinor"
    | Mode.LocrianNatural6 -> JStr "LocrianNatural6"
    | Mode.IonianAugmented -> JStr "IonianAugmented"
    | Mode.DorianSharp4 -> JStr "DorianSharp4"
    | Mode.PhrygianDominant -> JStr "PhrygianDominant"
    | Mode.LydianSharp2 -> JStr "LydianSharp2"
    | Mode.UltraLocrian -> JStr "UltraLocrian"

let private encClefKind (v: ClefKind) : JVal =
    match v with
    | ClefKind.Treble -> JStr "Treble"
    | ClefKind.Bass -> JStr "Bass"
    | ClefKind.Alto -> JStr "Alto"
    | ClefKind.Tenor -> JStr "Tenor"
    | ClefKind.Percussion -> JStr "Percussion"

let private encBracketKind (v: BracketKind) : JVal =
    match v with
    | BracketKind.Brace -> JStr "Brace"
    | BracketKind.Square -> JStr "Square"
    | BracketKind.Line -> JStr "Line"
    | BracketKind.None -> JStr "None"

let private encHairpinKind (v: HairpinKind) : JVal =
    match v with
    | HairpinKind.Crescendo -> JStr "Crescendo"
    | HairpinKind.Decrescendo -> JStr "Decrescendo"

let private encOctaveShiftKind (v: OctaveShiftKind) : JVal =
    match v with
    | OctaveShiftKind.Ottava -> JStr "Ottava"
    | OctaveShiftKind.OttavaBassa -> JStr "OttavaBassa"
    | OctaveShiftKind.Quindicesima -> JStr "Quindicesima"
    | OctaveShiftKind.QuindicesimaBassa -> JStr "QuindicesimaBassa"

let private encGraceKind (v: GraceKind) : JVal =
    match v with
    | GraceKind.Acciaccatura -> JStr "Acciaccatura"
    | GraceKind.Appoggiatura -> JStr "Appoggiatura"

let private encOrnamentName (v: OrnamentName) : JVal =
    match v with
    | OrnamentName.Trill -> JStr "Trill"
    | OrnamentName.Turn -> JStr "Turn"
    | OrnamentName.InvertedTurn -> JStr "InvertedTurn"
    | OrnamentName.Mordent -> JStr "Mordent"
    | OrnamentName.InvertedMordent -> JStr "InvertedMordent"
    | OrnamentName.Tremolo -> JStr "Tremolo"

let private encNavigationKind (v: NavigationKind) : JVal =
    match v with
    | NavigationKind.Segno -> JStr "Segno"
    | NavigationKind.Coda -> JStr "Coda"
    | NavigationKind.DaCapo -> JStr "DaCapo"
    | NavigationKind.DaCapoAlFine -> JStr "DaCapoAlFine"
    | NavigationKind.DaCapoAlCoda -> JStr "DaCapoAlCoda"
    | NavigationKind.DalSegno -> JStr "DalSegno"
    | NavigationKind.DalSegnoAlFine -> JStr "DalSegnoAlFine"
    | NavigationKind.DalSegnoAlCoda -> JStr "DalSegnoAlCoda"
    | NavigationKind.Fine -> JStr "Fine"
    | NavigationKind.ToCoda -> JStr "ToCoda"

let private encDynamicLevel (v: DynamicLevel) : JVal =
    match v with
    | DynamicLevel.Pianississimo -> JStr "Pianississimo"
    | DynamicLevel.Pianissimo -> JStr "Pianissimo"
    | DynamicLevel.Piano -> JStr "Piano"
    | DynamicLevel.MezzoPiano -> JStr "MezzoPiano"
    | DynamicLevel.MezzoForte -> JStr "MezzoForte"
    | DynamicLevel.Forte -> JStr "Forte"
    | DynamicLevel.Fortissimo -> JStr "Fortissimo"
    | DynamicLevel.Fortississimo -> JStr "Fortississimo"
    | DynamicLevel.Sforzando -> JStr "Sforzando"
    | DynamicLevel.Forzato -> JStr "Forzato"
    | DynamicLevel.Rinforzando -> JStr "Rinforzando"
    | DynamicLevel.FortePiano -> JStr "FortePiano"
    | DynamicLevel.SforzandoPiano -> JStr "SforzandoPiano"

// WIRE_FORMAT §5 — a non-finite double has no JSON *number* spelling, so it rides as
// one of the three quoted sentinel strings, which §7 requires a decoder to read back
// AT A FLOAT SLOT (`dFloat` below; `dInt` is deliberately not widened — §7 stops at
// the float slot, and an integer slot has no sentinel).
//
// Building the `JStr` HERE rather than leaving `Canon.render` to spell a non-finite
// `JFloat` is what keeps the emitted `JVal` renderable by the GUARDED
// `Fuaran.Core.Wire.tryRender`, which refuses a non-finite `JFloat` outright. The core
// wire model still has no non-finite float — the sentinel is a string, which it carries
// perfectly — so this widens the generated float slot's spelling, not the model.
let private encFloat (f: float) : JVal =
    if System.Double.IsNaN f then JStr "NaN"
    elif System.Double.IsPositiveInfinity f then JStr "Infinity"
    elif System.Double.IsNegativeInfinity f then JStr "-Infinity"
    else JFloat f

// Phase 108 — `Canon.typed` under this vocabulary's DECLARED discriminator key.
let private typedTag (tag: string) (fields: (string * JVal) list) : JVal =
    JObj(("kind", JStr tag) :: fields)

let rec private encNodeKind (k: NodeKind) : JVal =
    match k with
    | NodeKind.Score s -> encScoreSpec s
    | NodeKind.Part s -> encPartSpec s
    | NodeKind.PartGroup s -> encPartGroupSpec s
    | NodeKind.Measure s -> encMeasureSpec s
    | NodeKind.Staff s -> encStaffSpec s
    | NodeKind.Note s -> encNoteSpec s
    | NodeKind.Chord s -> encChordSpec s
    | NodeKind.GraceNote s -> encGraceNoteSpec s
    | NodeKind.Dynamic s -> encDynamicSpec s
    | NodeKind.Fermata s -> encFermataSpec s
    | NodeKind.HairpinStart s -> encHairpinStartSpec s
    | NodeKind.HairpinEnd s -> encHairpinEndSpec s
    | NodeKind.SlurStart s -> encSlurStartSpec s
    | NodeKind.SlurEnd s -> encSlurEndSpec s
    | NodeKind.OctaveShiftStart s -> encOctaveShiftStartSpec s
    | NodeKind.OctaveShiftEnd s -> encOctaveShiftEndSpec s
    | NodeKind.MultiRest s -> encMultiRestSpec s
    | NodeKind.Ornament s -> encOrnamentSpec s
    | NodeKind.RehearsalMark s -> encRehearsalMarkSpec s
    | NodeKind.NavigationMark s -> encNavigationMarkSpec s
    | NodeKind.Form s -> encFormSpec s

and private encNode (n: Node) : JVal =
    let kind = encNodeKind n.Kind

    match kind with
    | JObj(__d :: __kf) -> JObj(__d :: ("id", JStr n.Id) :: __kf)
    | __other -> __other

and private encPitch (s: Pitch) : JVal =
    JObj([ Some("letter", encNoteLetter s.Letter); Some("accidental", encAccidental s.Accidental); Some("octave", JInt s.Octave); Some("midi", JInt s.Midi) ] |> List.choose id)

and private encDuration (s: Duration) : JVal =
    JObj([ Some("base", encBaseDuration s.Base); (if s.Dots = 0 then None else Some("dots", JInt s.Dots)) ] |> List.choose id)

and private encKeySignature (s: KeySignature) : JVal =
    JObj([ Some("tonic", encNoteLetter s.Tonic); Some("tonicAccidental", encAccidental s.TonicAccidental); Some("mode", encMode s.Mode) ] |> List.choose id)

and private encTimeSignature (s: TimeSignature) : JVal =
    JObj([ Some("numerator", JInt s.Numerator); Some("denominator", JInt s.Denominator) ] |> List.choose id)

and private encStaffDefinition (s: StaffDefinition) : JVal =
    JObj([ Some("number", JInt s.Number); Some("clef", encClefKind s.Clef); Some("initialKey", encKeySignature s.InitialKey); Some("initialTime", encTimeSignature s.InitialTime) ] |> List.choose id)

and private encFormSection (s: FormSection) : JVal =
    JObj([ Some("label", JStr s.Label); Some("children", JArr(List.map encNode s.Children)) ] |> List.choose id)

and private encScoreSpec (s: ScoreSpec) : JVal =
    typedTag "Score" ([ (s.Title |> Option.map (fun v -> "title", JStr v)); (s.Composer |> Option.map (fun v -> "composer", JStr v)); Some("children", JArr(List.map encNode s.Children)) ] |> List.choose id)

and private encPartSpec (s: PartSpec) : JVal =
    typedTag "Part" ([ Some("name", JStr s.Name); Some("staves", JArr(List.map encStaffDefinition s.Staves)); Some("children", JArr(List.map encNode s.Children)) ] |> List.choose id)

and private encPartGroupSpec (s: PartGroupSpec) : JVal =
    typedTag "PartGroup" ([ (s.Name |> Option.map (fun v -> "name", JStr v)); Some("bracket", encBracketKind s.Bracket); Some("children", JArr(List.map encNode s.Children)) ] |> List.choose id)

and private encMeasureSpec (s: MeasureSpec) : JVal =
    typedTag "Measure" ([ Some("number", JInt s.Number); (if s.RepeatStart = false then None else Some("repeatStart", JBool s.RepeatStart)); (if s.RepeatEnd = false then None else Some("repeatEnd", JBool s.RepeatEnd)); (s.Volta |> Option.map (fun v -> "volta", JArr(List.map JInt v))); (if s.IsAnacrusis = false then None else Some("isAnacrusis", JBool s.IsAnacrusis)); Some("children", JArr(List.map encNode s.Children)) ] |> List.choose id)

and private encStaffSpec (s: StaffSpec) : JVal =
    typedTag "Staff" ([ Some("staffNumber", JInt s.StaffNumber); Some("children", JArr(List.map encNode s.Children)) ] |> List.choose id)

and private encNoteSpec (s: NoteSpec) : JVal =
    typedTag "Note" ([ Some("pitch", encPitch s.Pitch); Some("duration", encDuration s.Duration); (if s.Voice = 1 then None else Some("voice", JInt s.Voice)); (if s.TiedToNext = false then None else Some("tiedToNext", JBool s.TiedToNext)) ] |> List.choose id)

and private encChordSpec (s: ChordSpec) : JVal =
    typedTag "Chord" ([ Some("pitches", JArr(List.map encPitch s.Pitches)); Some("duration", encDuration s.Duration); (if s.Voice = 1 then None else Some("voice", JInt s.Voice)); (if s.TiedToNext = false then None else Some("tiedToNext", JBool s.TiedToNext)) ] |> List.choose id)

and private encGraceNoteSpec (s: GraceNoteSpec) : JVal =
    typedTag "GraceNote" ([ Some("pitch", encPitch s.Pitch); Some("grace", encGraceKind s.Grace) ] |> List.choose id)

and private encDynamicSpec (s: DynamicSpec) : JVal =
    typedTag "Dynamic" ([ Some("level", encDynamicLevel s.Level) ] |> List.choose id)

and private encFermataSpec (s: FermataSpec) : JVal =
    typedTag "Fermata" ([  ] |> List.choose id)

and private encHairpinStartSpec (s: HairpinStartSpec) : JVal =
    typedTag "HairpinStart" ([ Some("hairpin", encHairpinKind s.Hairpin) ] |> List.choose id)

and private encHairpinEndSpec (s: HairpinEndSpec) : JVal =
    typedTag "HairpinEnd" ([  ] |> List.choose id)

and private encSlurStartSpec (s: SlurStartSpec) : JVal =
    typedTag "SlurStart" ([  ] |> List.choose id)

and private encSlurEndSpec (s: SlurEndSpec) : JVal =
    typedTag "SlurEnd" ([  ] |> List.choose id)

and private encOctaveShiftStartSpec (s: OctaveShiftStartSpec) : JVal =
    typedTag "OctaveShiftStart" ([ Some("octaveShift", encOctaveShiftKind s.OctaveShift) ] |> List.choose id)

and private encOctaveShiftEndSpec (s: OctaveShiftEndSpec) : JVal =
    typedTag "OctaveShiftEnd" ([  ] |> List.choose id)

and private encMultiRestSpec (s: MultiRestSpec) : JVal =
    typedTag "MultiRest" ([ Some("measureCount", JInt s.MeasureCount) ] |> List.choose id)

and private encOrnamentSpec (s: OrnamentSpec) : JVal =
    typedTag "Ornament" ([ Some("ornament", encOrnamentName s.Ornament); (s.SlashCount |> Option.map (fun v -> "slashCount", JInt v)) ] |> List.choose id)

and private encRehearsalMarkSpec (s: RehearsalMarkSpec) : JVal =
    typedTag "RehearsalMark" ([ Some("label", JStr s.Label) ] |> List.choose id)

and private encNavigationMarkSpec (s: NavigationMarkSpec) : JVal =
    typedTag "NavigationMark" ([ Some("navigation", encNavigationKind s.Navigation) ] |> List.choose id)

and private encFormSpec (s: FormSpec) : JVal =
    typedTag "Form" ([ (s.Name |> Option.map (fun v -> "name", JStr v)); Some("sections", JArr(List.map encFormSection s.Sections)); Some("arrangement", JArr(List.map JStr s.Arrangement)) ] |> List.choose id)

let encodeNode (n: Node) : string = Canon.renderOrdered (encNode n)

/// JVal-level accessors (Phase 694) — for host codecs that splice generated
/// encodings into a larger canonical document (e.g. a TreeOp codec).
let encodeNodeJson (n: Node) : JVal = encNode n

let encodeNodeKindJson (k: NodeKind) : JVal = encNodeKind k

let private dObj (j: JVal) : Result<(string * JVal) list, string> =
    match j with
    | JObj fs -> Ok fs
    | _ -> Error "expected an object"

let private dTag (fs: (string * JVal) list) : Result<string, string> =
    match fs |> List.tryFind (fun (k, _) -> k = "kind") with
    | Some(_, JStr t) -> Ok t
    | _ -> Error "missing or non-string kind"

let private dStr (j: JVal) : Result<string, string> =
    match j with
    | JStr s -> Ok s
    | _ -> Error "expected a string"

let private dInt (j: JVal) : Result<int, string> =
    match j with
    | JInt i -> Ok i
    | _ -> Error "expected an int"

let private dBool (j: JVal) : Result<bool, string> =
    match j with
    | JBool b -> Ok b
    | _ -> Error "expected a bool"

// A whole-valued float renders without a decimal point, so it parses back as JInt.
// WIRE_FORMAT §7 — a float slot also accepts the three quoted non-finite sentinels, which
// is how §5 spells a number JSON has no literal for. The value decodes to the FLOAT, never
// to the string: a host that answered the string would hand a consumer a different tree on
// the second decode while the bytes stayed identical. `dInt` is NOT widened — §7 stops at
// the float slot.
let private dFloat (j: JVal) : Result<float, string> =
    match j with
    | JFloat f -> Ok f
    | JInt i -> Ok(float i)
    | JStr "NaN" -> Ok System.Double.NaN
    | JStr "Infinity" -> Ok System.Double.PositiveInfinity
    | JStr "-Infinity" -> Ok System.Double.NegativeInfinity
    | _ -> Error "expected a number"

let private dUnit (_: JVal) : Result<unit, string> = Ok()

// Phase 676 — arbitrary JSON, kept verbatim. No shape check: the field's
// contract is that its content is not the schema's business.
let private dJson (j: JVal) : Result<JVal, string> = Ok j

let private dList (dec: JVal -> Result<'T, string>) (j: JVal) : Result<'T list, string> =
    match j with
    | JArr xs ->
        (Ok [], xs)
        ||> List.fold (fun acc x ->
            match acc with
            | Error e -> Error e
            | Ok items -> dec x |> Result.map (fun v -> v :: items))
        |> Result.map List.rev
    | _ -> Error "expected an array"

let private dMap (dec: JVal -> Result<'T, string>) (j: JVal) : Result<Map<string, 'T>, string> =
    match j with
    | JObj fs ->
        (Ok [], fs)
        ||> List.fold (fun acc (k, v) ->
            match acc with
            | Error e -> Error e
            | Ok items -> dec v |> Result.map (fun d -> (k, d) :: items))
        |> Result.map (List.rev >> Map.ofList)
    | _ -> Error "expected an object"

let private dReq (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, string>) : Result<'T, string> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v
    | None -> Error("missing required field '" + name + "'")

let private dOpt (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, string>) : Result<'T option, string> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v |> Result.map Some
    | None -> Ok None

let private dDef (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, string>) (dflt: 'T) : Result<'T, string> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v
    | None -> Ok dflt

// An optional closure / opaque field: the value is a sentinel carrying nothing,
// but its PRESENCE distinguishes `Some ()` from `None` and must be read back.
let private dPresent (name: string) (fs: (string * JVal) list) : Result<unit option, string> =
    Ok(fs |> List.tryFind (fun (k, _) -> k = name) |> Option.map (fun _ -> ()))

let private decNoteLetter (j: JVal) : Result<NoteLetter, string> =
    match j with
    | JStr "C" -> Ok NoteLetter.C
    | JStr "D" -> Ok NoteLetter.D
    | JStr "E" -> Ok NoteLetter.E
    | JStr "F" -> Ok NoteLetter.F
    | JStr "G" -> Ok NoteLetter.G
    | JStr "A" -> Ok NoteLetter.A
    | JStr "B" -> Ok NoteLetter.B
    | _ -> Error "not a NoteLetter"

let private decAccidental (j: JVal) : Result<Accidental, string> =
    match j with
    | JStr "DoubleFlat" -> Ok Accidental.DoubleFlat
    | JStr "Flat" -> Ok Accidental.Flat
    | JStr "Natural" -> Ok Accidental.Natural
    | JStr "Sharp" -> Ok Accidental.Sharp
    | JStr "DoubleSharp" -> Ok Accidental.DoubleSharp
    | _ -> Error "not a Accidental"

let private decBaseDuration (j: JVal) : Result<BaseDuration, string> =
    match j with
    | JStr "Whole" -> Ok BaseDuration.Whole
    | JStr "Half" -> Ok BaseDuration.Half
    | JStr "Quarter" -> Ok BaseDuration.Quarter
    | JStr "Eighth" -> Ok BaseDuration.Eighth
    | JStr "Sixteenth" -> Ok BaseDuration.Sixteenth
    | JStr "ThirtySecond" -> Ok BaseDuration.ThirtySecond
    | JStr "SixtyFourth" -> Ok BaseDuration.SixtyFourth
    | _ -> Error "not a BaseDuration"

let private decMode (j: JVal) : Result<Mode, string> =
    match j with
    | JStr "Ionian" -> Ok Mode.Ionian
    | JStr "Dorian" -> Ok Mode.Dorian
    | JStr "Phrygian" -> Ok Mode.Phrygian
    | JStr "Lydian" -> Ok Mode.Lydian
    | JStr "Mixolydian" -> Ok Mode.Mixolydian
    | JStr "Aeolian" -> Ok Mode.Aeolian
    | JStr "Locrian" -> Ok Mode.Locrian
    | JStr "MelodicMinor" -> Ok Mode.MelodicMinor
    | JStr "Dorianb2" -> Ok Mode.Dorianb2
    | JStr "LydianAugmented" -> Ok Mode.LydianAugmented
    | JStr "LydianDominant" -> Ok Mode.LydianDominant
    | JStr "Mixolydianb6" -> Ok Mode.Mixolydianb6
    | JStr "LocrianNatural2" -> Ok Mode.LocrianNatural2
    | JStr "SuperLocrian" -> Ok Mode.SuperLocrian
    | JStr "HarmonicMinor" -> Ok Mode.HarmonicMinor
    | JStr "LocrianNatural6" -> Ok Mode.LocrianNatural6
    | JStr "IonianAugmented" -> Ok Mode.IonianAugmented
    | JStr "DorianSharp4" -> Ok Mode.DorianSharp4
    | JStr "PhrygianDominant" -> Ok Mode.PhrygianDominant
    | JStr "LydianSharp2" -> Ok Mode.LydianSharp2
    | JStr "UltraLocrian" -> Ok Mode.UltraLocrian
    | _ -> Error "not a Mode"

let private decClefKind (j: JVal) : Result<ClefKind, string> =
    match j with
    | JStr "Treble" -> Ok ClefKind.Treble
    | JStr "Bass" -> Ok ClefKind.Bass
    | JStr "Alto" -> Ok ClefKind.Alto
    | JStr "Tenor" -> Ok ClefKind.Tenor
    | JStr "Percussion" -> Ok ClefKind.Percussion
    | _ -> Error "not a ClefKind"

let private decBracketKind (j: JVal) : Result<BracketKind, string> =
    match j with
    | JStr "Brace" -> Ok BracketKind.Brace
    | JStr "Square" -> Ok BracketKind.Square
    | JStr "Line" -> Ok BracketKind.Line
    | JStr "None" -> Ok BracketKind.None
    | _ -> Error "not a BracketKind"

let private decHairpinKind (j: JVal) : Result<HairpinKind, string> =
    match j with
    | JStr "Crescendo" -> Ok HairpinKind.Crescendo
    | JStr "Decrescendo" -> Ok HairpinKind.Decrescendo
    | _ -> Error "not a HairpinKind"

let private decOctaveShiftKind (j: JVal) : Result<OctaveShiftKind, string> =
    match j with
    | JStr "Ottava" -> Ok OctaveShiftKind.Ottava
    | JStr "OttavaBassa" -> Ok OctaveShiftKind.OttavaBassa
    | JStr "Quindicesima" -> Ok OctaveShiftKind.Quindicesima
    | JStr "QuindicesimaBassa" -> Ok OctaveShiftKind.QuindicesimaBassa
    | _ -> Error "not a OctaveShiftKind"

let private decGraceKind (j: JVal) : Result<GraceKind, string> =
    match j with
    | JStr "Acciaccatura" -> Ok GraceKind.Acciaccatura
    | JStr "Appoggiatura" -> Ok GraceKind.Appoggiatura
    | _ -> Error "not a GraceKind"

let private decOrnamentName (j: JVal) : Result<OrnamentName, string> =
    match j with
    | JStr "Trill" -> Ok OrnamentName.Trill
    | JStr "Turn" -> Ok OrnamentName.Turn
    | JStr "InvertedTurn" -> Ok OrnamentName.InvertedTurn
    | JStr "Mordent" -> Ok OrnamentName.Mordent
    | JStr "InvertedMordent" -> Ok OrnamentName.InvertedMordent
    | JStr "Tremolo" -> Ok OrnamentName.Tremolo
    | _ -> Error "not a OrnamentName"

let private decNavigationKind (j: JVal) : Result<NavigationKind, string> =
    match j with
    | JStr "Segno" -> Ok NavigationKind.Segno
    | JStr "Coda" -> Ok NavigationKind.Coda
    | JStr "DaCapo" -> Ok NavigationKind.DaCapo
    | JStr "DaCapoAlFine" -> Ok NavigationKind.DaCapoAlFine
    | JStr "DaCapoAlCoda" -> Ok NavigationKind.DaCapoAlCoda
    | JStr "DalSegno" -> Ok NavigationKind.DalSegno
    | JStr "DalSegnoAlFine" -> Ok NavigationKind.DalSegnoAlFine
    | JStr "DalSegnoAlCoda" -> Ok NavigationKind.DalSegnoAlCoda
    | JStr "Fine" -> Ok NavigationKind.Fine
    | JStr "ToCoda" -> Ok NavigationKind.ToCoda
    | _ -> Error "not a NavigationKind"

let private decDynamicLevel (j: JVal) : Result<DynamicLevel, string> =
    match j with
    | JStr "Pianississimo" -> Ok DynamicLevel.Pianississimo
    | JStr "Pianissimo" -> Ok DynamicLevel.Pianissimo
    | JStr "Piano" -> Ok DynamicLevel.Piano
    | JStr "MezzoPiano" -> Ok DynamicLevel.MezzoPiano
    | JStr "MezzoForte" -> Ok DynamicLevel.MezzoForte
    | JStr "Forte" -> Ok DynamicLevel.Forte
    | JStr "Fortissimo" -> Ok DynamicLevel.Fortissimo
    | JStr "Fortississimo" -> Ok DynamicLevel.Fortississimo
    | JStr "Sforzando" -> Ok DynamicLevel.Sforzando
    | JStr "Forzato" -> Ok DynamicLevel.Forzato
    | JStr "Rinforzando" -> Ok DynamicLevel.Rinforzando
    | JStr "FortePiano" -> Ok DynamicLevel.FortePiano
    | JStr "SforzandoPiano" -> Ok DynamicLevel.SforzandoPiano
    | _ -> Error "not a DynamicLevel"

let rec private decNodeKind (j: JVal) : Result<NodeKind, string> =
    dObj j |> Result.bind (fun __fs ->
    dTag __fs |> Result.bind (fun __t ->
    match __t with
    | "Score" -> decScoreSpec j |> Result.map NodeKind.Score
    | "Part" -> decPartSpec j |> Result.map NodeKind.Part
    | "PartGroup" -> decPartGroupSpec j |> Result.map NodeKind.PartGroup
    | "Measure" -> decMeasureSpec j |> Result.map NodeKind.Measure
    | "Staff" -> decStaffSpec j |> Result.map NodeKind.Staff
    | "Note" -> decNoteSpec j |> Result.map NodeKind.Note
    | "Chord" -> decChordSpec j |> Result.map NodeKind.Chord
    | "GraceNote" -> decGraceNoteSpec j |> Result.map NodeKind.GraceNote
    | "Dynamic" -> decDynamicSpec j |> Result.map NodeKind.Dynamic
    | "Fermata" -> decFermataSpec j |> Result.map NodeKind.Fermata
    | "HairpinStart" -> decHairpinStartSpec j |> Result.map NodeKind.HairpinStart
    | "HairpinEnd" -> decHairpinEndSpec j |> Result.map NodeKind.HairpinEnd
    | "SlurStart" -> decSlurStartSpec j |> Result.map NodeKind.SlurStart
    | "SlurEnd" -> decSlurEndSpec j |> Result.map NodeKind.SlurEnd
    | "OctaveShiftStart" -> decOctaveShiftStartSpec j |> Result.map NodeKind.OctaveShiftStart
    | "OctaveShiftEnd" -> decOctaveShiftEndSpec j |> Result.map NodeKind.OctaveShiftEnd
    | "MultiRest" -> decMultiRestSpec j |> Result.map NodeKind.MultiRest
    | "Ornament" -> decOrnamentSpec j |> Result.map NodeKind.Ornament
    | "RehearsalMark" -> decRehearsalMarkSpec j |> Result.map NodeKind.RehearsalMark
    | "NavigationMark" -> decNavigationMarkSpec j |> Result.map NodeKind.NavigationMark
    | "Form" -> decFormSpec j |> Result.map NodeKind.Form
    | __other -> Error ("unknown node kind: " + __other)))

and private decNode (j: JVal) : Result<Node, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "id" __fs dStr |> Result.bind (fun id ->
    decNodeKind j |> Result.bind (fun kind ->
    Ok { Id = id; Kind = kind })))

and private decPitch (j: JVal) : Result<Pitch, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "letter" __fs decNoteLetter |> Result.bind (fun letter ->
    dReq "accidental" __fs decAccidental |> Result.bind (fun accidental ->
    dReq "octave" __fs dInt |> Result.bind (fun octave ->
    dReq "midi" __fs dInt |> Result.bind (fun midi ->
    Ok { Letter = letter; Accidental = accidental; Octave = octave; Midi = midi })))))

and private decDuration (j: JVal) : Result<Duration, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "base" __fs decBaseDuration |> Result.bind (fun ``base`` ->
    dDef "dots" __fs dInt (0) |> Result.bind (fun dots ->
    Ok { Base = ``base``; Dots = dots })))

and private decKeySignature (j: JVal) : Result<KeySignature, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "tonic" __fs decNoteLetter |> Result.bind (fun tonic ->
    dReq "tonicAccidental" __fs decAccidental |> Result.bind (fun tonicAccidental ->
    dReq "mode" __fs decMode |> Result.bind (fun mode ->
    Ok { Tonic = tonic; TonicAccidental = tonicAccidental; Mode = mode }))))

and private decTimeSignature (j: JVal) : Result<TimeSignature, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "numerator" __fs dInt |> Result.bind (fun numerator ->
    dReq "denominator" __fs dInt |> Result.bind (fun denominator ->
    Ok { Numerator = numerator; Denominator = denominator })))

and private decStaffDefinition (j: JVal) : Result<StaffDefinition, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "number" __fs dInt |> Result.bind (fun number ->
    dReq "clef" __fs decClefKind |> Result.bind (fun clef ->
    dReq "initialKey" __fs decKeySignature |> Result.bind (fun initialKey ->
    dReq "initialTime" __fs decTimeSignature |> Result.bind (fun initialTime ->
    Ok { Number = number; Clef = clef; InitialKey = initialKey; InitialTime = initialTime })))))

and private decFormSection (j: JVal) : Result<FormSection, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs dStr |> Result.bind (fun label ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    Ok { Label = label; Children = children })))

and private decScoreSpec (j: JVal) : Result<ScoreSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "title" __fs dStr |> Result.bind (fun title ->
    dOpt "composer" __fs dStr |> Result.bind (fun composer ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    Ok { Title = title; Composer = composer; Children = children }))))

and private decPartSpec (j: JVal) : Result<PartSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "name" __fs dStr |> Result.bind (fun name ->
    dReq "staves" __fs (dList decStaffDefinition) |> Result.bind (fun staves ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    Ok { Name = name; Staves = staves; Children = children }))))

and private decPartGroupSpec (j: JVal) : Result<PartGroupSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "name" __fs dStr |> Result.bind (fun name ->
    dReq "bracket" __fs decBracketKind |> Result.bind (fun bracket ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    Ok { Name = name; Bracket = bracket; Children = children }))))

and private decMeasureSpec (j: JVal) : Result<MeasureSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "number" __fs dInt |> Result.bind (fun number ->
    dDef "repeatStart" __fs dBool (false) |> Result.bind (fun repeatStart ->
    dDef "repeatEnd" __fs dBool (false) |> Result.bind (fun repeatEnd ->
    dOpt "volta" __fs (dList dInt) |> Result.bind (fun volta ->
    dDef "isAnacrusis" __fs dBool (false) |> Result.bind (fun isAnacrusis ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    Ok { Number = number; RepeatStart = repeatStart; RepeatEnd = repeatEnd; Volta = volta; IsAnacrusis = isAnacrusis; Children = children })))))))

and private decStaffSpec (j: JVal) : Result<StaffSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "staffNumber" __fs dInt |> Result.bind (fun staffNumber ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    Ok { StaffNumber = staffNumber; Children = children })))

and private decNoteSpec (j: JVal) : Result<NoteSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "pitch" __fs decPitch |> Result.bind (fun pitch ->
    dReq "duration" __fs decDuration |> Result.bind (fun duration ->
    dDef "voice" __fs dInt (1) |> Result.bind (fun voice ->
    dDef "tiedToNext" __fs dBool (false) |> Result.bind (fun tiedToNext ->
    Ok { Pitch = pitch; Duration = duration; Voice = voice; TiedToNext = tiedToNext })))))

and private decChordSpec (j: JVal) : Result<ChordSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "pitches" __fs (dList decPitch) |> Result.bind (fun pitches ->
    dReq "duration" __fs decDuration |> Result.bind (fun duration ->
    dDef "voice" __fs dInt (1) |> Result.bind (fun voice ->
    dDef "tiedToNext" __fs dBool (false) |> Result.bind (fun tiedToNext ->
    Ok { Pitches = pitches; Duration = duration; Voice = voice; TiedToNext = tiedToNext })))))

and private decGraceNoteSpec (j: JVal) : Result<GraceNoteSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "pitch" __fs decPitch |> Result.bind (fun pitch ->
    dReq "grace" __fs decGraceKind |> Result.bind (fun grace ->
    Ok { Pitch = pitch; Grace = grace })))

and private decDynamicSpec (j: JVal) : Result<DynamicSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "level" __fs decDynamicLevel |> Result.bind (fun level ->
    Ok { Level = level }))

and private decFermataSpec (j: JVal) : Result<FermataSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    Ok FermataSpec.FermataSpec)

and private decHairpinStartSpec (j: JVal) : Result<HairpinStartSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "hairpin" __fs decHairpinKind |> Result.bind (fun hairpin ->
    Ok { Hairpin = hairpin }))

and private decHairpinEndSpec (j: JVal) : Result<HairpinEndSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    Ok HairpinEndSpec.HairpinEndSpec)

and private decSlurStartSpec (j: JVal) : Result<SlurStartSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    Ok SlurStartSpec.SlurStartSpec)

and private decSlurEndSpec (j: JVal) : Result<SlurEndSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    Ok SlurEndSpec.SlurEndSpec)

and private decOctaveShiftStartSpec (j: JVal) : Result<OctaveShiftStartSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "octaveShift" __fs decOctaveShiftKind |> Result.bind (fun octaveShift ->
    Ok { OctaveShift = octaveShift }))

and private decOctaveShiftEndSpec (j: JVal) : Result<OctaveShiftEndSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    Ok OctaveShiftEndSpec.OctaveShiftEndSpec)

and private decMultiRestSpec (j: JVal) : Result<MultiRestSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "measureCount" __fs dInt |> Result.bind (fun measureCount ->
    Ok { MeasureCount = measureCount }))

and private decOrnamentSpec (j: JVal) : Result<OrnamentSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "ornament" __fs decOrnamentName |> Result.bind (fun ornament ->
    dOpt "slashCount" __fs dInt |> Result.bind (fun slashCount ->
    Ok { Ornament = ornament; SlashCount = slashCount })))

and private decRehearsalMarkSpec (j: JVal) : Result<RehearsalMarkSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs dStr |> Result.bind (fun label ->
    Ok { Label = label }))

and private decNavigationMarkSpec (j: JVal) : Result<NavigationMarkSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "navigation" __fs decNavigationKind |> Result.bind (fun navigation ->
    Ok { Navigation = navigation }))

and private decFormSpec (j: JVal) : Result<FormSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "name" __fs dStr |> Result.bind (fun name ->
    dReq "sections" __fs (dList decFormSection) |> Result.bind (fun sections ->
    dReq "arrangement" __fs (dList dStr) |> Result.bind (fun arrangement ->
    Ok { Name = name; Sections = sections; Arrangement = arrangement }))))

/// Structural decode. The policy layer (diagnostics, §16 lenient-accept,
/// the reject set) composes ABOVE this — see the Phase 672 note in the generator.
let decodeNode (s: string) : Result<Node, string> =
    Json.parse s |> Result.bind decNode

let private witnessKindTag (n: Node) : string =
    match n.Kind with
    | NodeKind.Score _ -> "Score"
    | NodeKind.Part _ -> "Part"
    | NodeKind.PartGroup _ -> "PartGroup"
    | NodeKind.Measure _ -> "Measure"
    | NodeKind.Staff _ -> "Staff"
    | NodeKind.Note _ -> "Note"
    | NodeKind.Chord _ -> "Chord"
    | NodeKind.GraceNote _ -> "GraceNote"
    | NodeKind.Dynamic _ -> "Dynamic"
    | NodeKind.Fermata _ -> "Fermata"
    | NodeKind.HairpinStart _ -> "HairpinStart"
    | NodeKind.HairpinEnd _ -> "HairpinEnd"
    | NodeKind.SlurStart _ -> "SlurStart"
    | NodeKind.SlurEnd _ -> "SlurEnd"
    | NodeKind.OctaveShiftStart _ -> "OctaveShiftStart"
    | NodeKind.OctaveShiftEnd _ -> "OctaveShiftEnd"
    | NodeKind.MultiRest _ -> "MultiRest"
    | NodeKind.Ornament _ -> "Ornament"
    | NodeKind.RehearsalMark _ -> "RehearsalMark"
    | NodeKind.NavigationMark _ -> "NavigationMark"
    | NodeKind.Form _ -> "Form"

let private witnessChildren (n: Node) : Node list =
    match n.Kind with
    | NodeKind.Score s -> s.Children
    | NodeKind.Part s -> s.Children
    | NodeKind.PartGroup s -> s.Children
    | NodeKind.Measure s -> s.Children
    | NodeKind.Staff s -> s.Children
    | _ -> []

let private witnessReplaceChildren (n: Node) (kids: Node list) : Node =
    match n.Kind with
    | NodeKind.Score s -> { n with Kind = NodeKind.Score { s with Children = kids } }
    | NodeKind.Part s -> { n with Kind = NodeKind.Part { s with Children = kids } }
    | NodeKind.PartGroup s -> { n with Kind = NodeKind.PartGroup { s with Children = kids } }
    | NodeKind.Measure s -> { n with Kind = NodeKind.Measure { s with Children = kids } }
    | NodeKind.Staff s -> { n with Kind = NodeKind.Staff { s with Children = kids } }
    | _ -> n

let nodeWitness: NodeWitness<Node, string> =
    { Id = fun n -> n.Id
      KindTag = witnessKindTag
      Children = witnessChildren
      ReplaceChildren = witnessReplaceChildren }

// Validator scaffold — register domain RuleFamilies into `reg`; rule content stays domain-side.
let runValidator (reg: Validator.Registry<Node, string>) (root: Node) : Defect<string> list =
    Validator.runAll nodeWitness reg root

// Smart constructors — required-without-default fields are parameters; IDL-declared
// defaults are filled, other optionals default to None.

let mkScore (id: string) (children: Node list) : Node =
    { Id = id; Kind = NodeKind.Score { Title = None; Composer = None; Children = children } }

let mkPart (id: string) (name: string) (staves: StaffDefinition list) (children: Node list) : Node =
    { Id = id; Kind = NodeKind.Part { Name = name; Staves = staves; Children = children } }

let mkPartGroup (id: string) (bracket: BracketKind) (children: Node list) : Node =
    { Id = id; Kind = NodeKind.PartGroup { Name = None; Bracket = bracket; Children = children } }

let mkMeasure (id: string) (number: int) (children: Node list) : Node =
    { Id = id; Kind = NodeKind.Measure { Number = number; RepeatStart = false; RepeatEnd = false; Volta = None; IsAnacrusis = false; Children = children } }

let mkStaff (id: string) (staffNumber: int) (children: Node list) : Node =
    { Id = id; Kind = NodeKind.Staff { StaffNumber = staffNumber; Children = children } }

let mkNote (id: string) (pitch: Pitch) (duration: Duration) : Node =
    { Id = id; Kind = NodeKind.Note { Pitch = pitch; Duration = duration; Voice = 1; TiedToNext = false } }

let mkChord (id: string) (pitches: Pitch list) (duration: Duration) : Node =
    { Id = id; Kind = NodeKind.Chord { Pitches = pitches; Duration = duration; Voice = 1; TiedToNext = false } }

let mkGraceNote (id: string) (pitch: Pitch) (grace: GraceKind) : Node =
    { Id = id; Kind = NodeKind.GraceNote { Pitch = pitch; Grace = grace } }

let mkDynamic (id: string) (level: DynamicLevel) : Node =
    { Id = id; Kind = NodeKind.Dynamic { Level = level } }

let mkFermata (id: string) : Node =
    { Id = id; Kind = NodeKind.Fermata FermataSpec.FermataSpec }

let mkHairpinStart (id: string) (hairpin: HairpinKind) : Node =
    { Id = id; Kind = NodeKind.HairpinStart { Hairpin = hairpin } }

let mkHairpinEnd (id: string) : Node =
    { Id = id; Kind = NodeKind.HairpinEnd HairpinEndSpec.HairpinEndSpec }

let mkSlurStart (id: string) : Node =
    { Id = id; Kind = NodeKind.SlurStart SlurStartSpec.SlurStartSpec }

let mkSlurEnd (id: string) : Node =
    { Id = id; Kind = NodeKind.SlurEnd SlurEndSpec.SlurEndSpec }

let mkOctaveShiftStart (id: string) (octaveShift: OctaveShiftKind) : Node =
    { Id = id; Kind = NodeKind.OctaveShiftStart { OctaveShift = octaveShift } }

let mkOctaveShiftEnd (id: string) : Node =
    { Id = id; Kind = NodeKind.OctaveShiftEnd OctaveShiftEndSpec.OctaveShiftEndSpec }

let mkMultiRest (id: string) (measureCount: int) : Node =
    { Id = id; Kind = NodeKind.MultiRest { MeasureCount = measureCount } }

let mkOrnament (id: string) (ornament: OrnamentName) : Node =
    { Id = id; Kind = NodeKind.Ornament { Ornament = ornament; SlashCount = None } }

let mkRehearsalMark (id: string) (label: string) : Node =
    { Id = id; Kind = NodeKind.RehearsalMark { Label = label } }

let mkNavigationMark (id: string) (navigation: NavigationKind) : Node =
    { Id = id; Kind = NodeKind.NavigationMark { Navigation = navigation } }

let mkForm (id: string) (sections: FormSection list) (arrangement: string list) : Node =
    { Id = id; Kind = NodeKind.Form { Name = None; Sections = sections; Arrangement = arrangement } }