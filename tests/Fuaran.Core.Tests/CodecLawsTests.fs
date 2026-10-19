module Fuaran.Core.Tests.CodecLawsTests

// Phase 442 — `Conformance.codecLawsAt` (`codecLaws` until Phase 507), the family that samples at a domain's own codec the premises
// Phase 436 proved the `Codec` blocks under.
//
// Held here: the family green at the reference witness (a declaration that uses every block a domain
// hands a function to — `map`, `refine`, `record`, `union`, `enum`, `optField`, `list`) and at Core's
// own leaf codecs (`Codec.int`, `Codec.float`, `Codec.string`, `Codec.bool`, each an `ofDecoder` codec,
// so the reader-reads-its-writer premise is one Core itself owes); one go-red per premise, each red in
// the cell that premise's failure produces and in no other subject cell; and the vacuity a drawn
// family can show — a codec that refuses no mutation, a generator that draws one value, a run of no
// iterations.

open Expecto
open Fuaran.Core

type RefKind =
    | Small
    | Large

type RefShape =
    | Circle of radius: int
    | Rect of w: int * h: int

/// A count the declaration refines out of an `int`: never negative.
type RefCount = RefCount of int

/// A label the declaration maps out of a `string`.
type RefLabel = RefLabel of string

type RefItem =
    { Label: RefLabel
      Count: RefCount
      Kind: RefKind
      Shape: RefShape
      Tags: string list
      Note: string option }

/// A fixture declaration, built: these are well-formed, so a refusal is a defect of the suite.
let private declared (r: Result<Codec<'T>, CodecDeclarationFault list>) : Codec<'T> =
    match r with
    | Ok c -> c
    | Error faults -> failwithf "the fixture declaration was refused: %A" faults

let private count: Codec<RefCount> =
    Codec.int
    |> Codec.refine
        "a count, never negative"
        (fun n ->
            if n >= 0 then
                Ok(RefCount n)
            else
                Error "a count is never negative")
        (fun (RefCount n) -> n)

let private label: Codec<RefLabel> =
    Codec.string |> Codec.map RefLabel (fun (RefLabel s) -> s)

let private kind: Codec<RefKind> =
    declared (Codec.enum [ "small", Small; "large", Large ])

/// The shape union, its `inject` per case given — so a go-red can hand a wrong one.
let private shapeWith (injectCircle: int -> RefShape) : Codec<RefShape> =
    declared (
        Codec.union
            "type"
            [ Codec.case
                  "circle"
                  (function
                  | Circle r -> Some r
                  | _ -> None)
                  injectCircle
                  (Codec.record id |> Codec.field "r" id Codec.int)
              Codec.case
                  "rect"
                  (function
                  | Rect(w, h) -> Some(w, h)
                  | _ -> None)
                  Rect
                  (Codec.record (fun w h -> w, h)
                   |> Codec.field "w" fst Codec.int
                   |> Codec.field "h" snd Codec.int) ]
    )

/// The reference declaration, its constructor given — so a go-red can hand a wrong one.
let private itemWith (ctor: RefLabel -> RefCount -> RefKind -> RefShape -> string list -> string option -> RefItem) =
    declared (
        Codec.record ctor
        |> Codec.field "label" _.Label label
        |> Codec.field "count" _.Count count
        |> Codec.field "kind" _.Kind kind
        |> Codec.field "shape" _.Shape (shapeWith Circle)
        |> Codec.field "tags" _.Tags (Codec.list Codec.string)
        |> Codec.optField "note" _.Note Codec.string
        |> Codec.build
    )

/// The reference witness: a declaration using every block a domain hands a function to.
let referenceCodec: Codec<RefItem> =
    itemWith (fun l c k s t n ->
        { Label = l
          Count = c
          Kind = k
          Shape = s
          Tags = t
          Note = n })

let private texts = [ ""; "a"; "b c"; "line\nfeed"; "tab\there"; "é"; "\"quoted\"" ]

let private drawInt (lo: int) (hi: int) (r: ConfRng.T) : int * ConfRng.T =
    let n, r = ConfRng.intBelow (hi - lo) r
    lo + n, r

/// The reference generator: every case of every block, drawn.
let referenceGen (r: ConfRng.T) : RefItem * ConfRng.T =
    let l, r = ConfRng.choose texts r
    let c, r = ConfRng.intBelow 1000 r
    let k, r = ConfRng.choose [ Small; Large ] r
    let which, r = ConfRng.intBelow 2 r
    let a, r = drawInt -500 500 r
    let b, r = drawInt -500 500 r
    let n, r = ConfRng.intBelow 4 r

    let tags, r =
        List.fold (fun (acc, r) _ -> let t, r = ConfRng.choose texts r in t :: acc, r) ([], r) [ 1..n ]

    let hasNote, r = ConfRng.intBelow 2 r
    let note, r = ConfRng.choose texts r

    { Label = RefLabel l
      Count = RefCount c
      Kind = k
      Shape = (if which = 0 then Circle a else Rect(a, b))
      Tags = tags
      Note = (if hasNote = 0 then None else Some note) },
    r

/// An `int` drawn over both signs.
let private intGen (r: ConfRng.T) = drawInt -1000 1000 r

let private seed = 442
let private iterations = 200

let private reds (results: LawResult list) : string list =
    results |> List.filter (fun r -> not r.Passed) |> List.map _.Law

let private green (what: string) (results: LawResult list) =
    Expect.isEmpty (reds results) (sprintf "%s: codecLaws is green" what)

/// The family's subject cells, by the fragment that names each.
let private written = "every sampled value is written"
let private reads = "every written value reads with no defect"
let private inverse = "reads back as the value it was written from"
let private text = "canonical text under every encoding profile"
let private refusals = "every refusal of a mutated written document"

/// Red in exactly the cells named, among the five subject cells — a premise's failure is reported
/// where that premise is stated, and does not spill into a cell about another.
let private redIn (what: string) (cells: string list) (results: LawResult list) =
    let subject = [ written; reads; inverse; text; refusals ]

    let redSubject =
        subject
        |> List.filter (fun c -> results |> List.exists (fun r -> not r.Passed && r.Law.Contains c))

    Expect.equal redSubject cells (sprintf "%s: red in exactly these cells" what)

/// An `ofDecoder` int codec whose reader refuses every non-integer with `refusal`.
let private refusingWith (refusal: JVal -> DecodeError) : Codec<int> =
    Codec.ofDecoder
        JInt
        (fun j ->
            match j with
            | JInt n -> Ok n
            | other -> Error(refusal other))
        (JObj [ "type", JStr "integer" ])

[<Tests>]
let tests =
    testList
        "Conformance.codecLawsAt (Phase 442; Phase 507)"
        [ testCase "green at the reference witness"
          <| fun _ -> green "reference" (Conformance.codecLawsAt referenceCodec referenceGen seed iterations)

          testCase "green at Core's own leaf codecs — each an ofDecoder codec whose reader reads its writer"
          <| fun _ ->
              green "Codec.int" (Conformance.codecLawsAt Codec.int intGen seed iterations)

              green
                  "Codec.float"
                  (Conformance.codecLawsAt
                      Codec.float
                      (fun r ->
                          let n, r = drawInt -100000 100000 r
                          float n / 64.0, r)
                      seed
                      iterations)

              green "Codec.string" (Conformance.codecLawsAt Codec.string (ConfRng.choose texts) seed iterations)
              green "Codec.bool" (Conformance.codecLawsAt Codec.bool (ConfRng.choose [ true; false ]) seed iterations)

              green
                  "Codec.list Codec.int"
                  (Conformance.codecLawsAt
                      (Codec.list Codec.int)
                      (fun r ->
                          let a, r = intGen r
                          let b, r = intGen r
                          [ a; b ], r)
                      seed
                      iterations)

          testCase "go-red: a map whose there does not invert its back"
          <| fun _ ->
              let skewed = Codec.int |> Codec.map (fun n -> n + 1) id

              redIn "map" [ inverse; text ] (Conformance.codecLawsAt skewed intGen seed iterations)

          testCase "go-red: a refine whose check refuses a value its back writes"
          <| fun _ ->
              let positive =
                  Codec.int
                  |> Codec.refine "positive" (fun n -> if n > 0 then Ok n else Error "not positive") id

              let results = Conformance.codecLawsAt positive intGen seed iterations
              redIn "refine" [ reads; text ] results

              let cx =
                  results
                  |> List.find (fun r -> r.Law.Contains reads)
                  |> _.Counterexample
                  |> Option.defaultValue ""

              Expect.stringContains cx "OutOfRange" "the counterexample names the refine's refusal"

          testCase "go-red: a record constructor that does not rebuild the value from its getters"
          <| fun _ ->
              let dropsNote =
                  itemWith (fun l c k s t _ ->
                      { Label = l
                        Count = c
                        Kind = k
                        Shape = s
                        Tags = t
                        Note = None })

              redIn "record" [ inverse; text ] (Conformance.codecLawsAt dropsNote referenceGen seed iterations)

          testCase "go-red: a union case whose inject does not invert its project"
          <| fun _ ->
              let wrongCase = shapeWith (fun r -> Rect(r, r))

              let shapes (r: ConfRng.T) =
                  let item, r = referenceGen r
                  item.Shape, r

              redIn "union" [ inverse; text ] (Conformance.codecLawsAt wrongCase shapes seed iterations)

          testCase "go-red: a union no case of which recognises a drawn value"
          <| fun _ ->
              let circlesOnly =
                  declared (
                      Codec.union
                          "type"
                          [ Codec.case
                                "circle"
                                (function
                                | Circle r -> Some r
                                | _ -> None)
                                Circle
                                (Codec.record id |> Codec.field "r" id Codec.int) ]
                  )

              let results =
                  Conformance.codecLawsAt circlesOnly (fun r -> Rect(1, 2), r) seed iterations

              Expect.contains
                  (reds results)
                  (results |> List.find (fun r -> r.Law.Contains written)).Law
                  "written is red"

          testCase "go-red: a make reader that does not read back its writer"
          <| fun _ ->
              let forgetful = Codec.make JInt (fun _ -> Ok 0) (JObj [])
              redIn "make" [ inverse; text ] (Conformance.codecLawsAt forgetful intGen seed iterations)

          testCase "go-red: a reader that reads at the value level and not through the canonical text"
          <| fun _ ->
              // Written as `JFloat`, read only from `JFloat`: a whole value's canonical text is an
              // integer token, which reads back as `JInt` and is refused. The value-level cells hold.
              let floatOnly =
                  Codec.make
                      (fun (n: int) -> JFloat(float n))
                      (fun j ->
                          match j with
                          | JFloat f -> Ok(int f)
                          | other -> Error [ Decoder.wrongKind "a float token" other ])
                      (JObj [])

              redIn "text" [ text ] (Conformance.codecLawsAt floatOnly intGen seed iterations)

          testCase "go-red: a refusal that points nowhere — a path the document does not hold"
          <| fun _ ->
              let nowhere =
                  refusingWith (fun other ->
                      DecodeError.under (PathSegment.Key "nowhere") (Decoder.wrongKind "integer" other))

              redIn "path" [ refusals ] (Conformance.codecLawsAt nowhere intGen seed iterations)

          testCase "go-red: a refusal with an unsound code — MissingField at the root, InvalidJson over a parsed value"
          <| fun _ ->
              let missingAtRoot =
                  refusingWith (fun _ -> DecodeError.make DecodeCode.MissingField "an integer" "missing")

              redIn "MissingField" [ refusals ] (Conformance.codecLawsAt missingAtRoot intGen seed iterations)

              let invalidJson =
                  refusingWith (fun _ -> DecodeError.make DecodeCode.InvalidJson "an integer" "not JSON")

              redIn "InvalidJson" [ refusals ] (Conformance.codecLawsAt invalidJson intGen seed iterations)

          testCase "go-red: a collecting reader that refuses with no defect"
          <| fun _ ->
              let silent =
                  Codec.make
                      JInt
                      (fun j ->
                          match j with
                          | JInt n -> Ok n
                          | _ -> Error [])
                      (JObj [])

              redIn "empty refusal" [ refusals ] (Conformance.codecLawsAt silent intGen seed iterations)

          testCase "vacuity: a codec that refuses no mutation starves the refusal arm, by name"
          <| fun _ ->
              let gen (r: ConfRng.T) =
                  let n, r = intGen r
                  JInt n, r

              let results = Conformance.codecLawsAt Codec.json gen seed iterations
              redIn "json" [] results
              let starved = reds results
              Expect.equal (List.length starved) 1 "only the guard is red"
              Expect.stringContains starved.Head "mutation outcome" "and it names the dimension"

          testCase "vacuity: a generator that draws one value starves the span, by name"
          <| fun _ ->
              let results = Conformance.codecLawsAt Codec.int (fun r -> 7, r) seed iterations
              let starved = reds results
              Expect.equal (List.length starved) 1 "only the span guard is red"
              Expect.stringContains starved.Head "distinct written values" "and it names the measure"

          testCase "vacuity: no iterations is no evidence, and every subject cell says so"
          <| fun _ ->
              let results = Conformance.codecLawsAt referenceCodec referenceGen seed 0

              redIn "zero" [ written; reads; inverse; text ] results ]
