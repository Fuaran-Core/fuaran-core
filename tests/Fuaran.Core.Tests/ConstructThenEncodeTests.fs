/// Phase 126 — the construct-then-encode family: the AUTHORING surface certified, not only the
/// codec. The suite runs the law over the reference string-id witness (D7), and proves it goes red
/// on the shape that motivated it — an authoring constructor that widens a field the encoder does
/// not expect, while the plain decoder-encoder round trip over the very same corpus stays green.
module Fuaran.Core.Tests.ConstructThenEncodeTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

// A reference codec over `RNode`, built from the Core.Wire encode DSL + decode combinators. Local
// to this file by the suite's own idiom — the reference DOMAIN is shared (Reference.fs), each
// file's codec is its own fixture, so a change here cannot silently move another file's subject.
let rec private encodeNode (n: RNode) : JVal =
    Json.kindObj
        n.Kind
        [ "id", JStr n.Id
          "value", JStr n.Value
          "children", JArr(n.Children |> List.map encodeNode) ]

let private encode (n: RNode) = Json.render (encodeNode n)

let rec private decodeNode el : Result<RNode, string> =
    Decode.kindOf el
    |> Result.bind (fun kind ->
        Decode.strField "id" el
        |> Result.bind (fun id ->
            Decode.strField "value" el
            |> Result.bind (fun value ->
                Decode.getProp "children" el
                |> Result.bind (Decode.mapList decodeNode)
                |> Result.map (fun kids ->
                    { Id = id
                      Kind = kind
                      Value = value
                      Hole = None
                      HoleName = ""
                      Eff = Effect.pureDeterministic
                      Children = kids }))))

let private decode (s: string) =
    Decode.parse s |> Result.bind decodeNode

let private codec: Corpus.Codec<RNode> = { Encode = encode; Decode = decode }

// ---------------------------------------------------------------------------
//  the authoring surfaces
// ---------------------------------------------------------------------------

/// Fold a per-child `Result` over a child list, stopping at the first refusal — the authoring
/// surface is allowed to refuse, and a refusal has to reach the law rather than be swallowed.
let rec private mapChildren (f: RNode -> Result<RNode, string>) (acc: RNode list) (rest: RNode list) =
    match rest with
    | [] -> Ok(List.rev acc)
    | k :: tail ->
        match f k with
        | Ok k' -> mapChildren f (k' :: acc) tail
        | Error e -> Error e

/// The reference domain's HONEST authoring surface: rebuild every node through `RNode.node` /
/// `RNode.leaf` — the two smart constructors an author calls — and never through the decoded record.
/// Going through the record (`{ n with Children = … }`) would certify nothing: the whole point is
/// that the author-facing constructors are on the path.
let rec private constructThrough (n: RNode) : Result<RNode, string> =
    if List.isEmpty n.Children then
        Ok(RNode.leaf n.Id n.Kind n.Value)
    else
        mapChildren constructThrough [] n.Children
        |> Result.map (RNode.node n.Id n.Kind)

/// The WIDENED authoring surface — the shape that motivated the family (`@fuaran-ui/ui` 0.26.0,
/// fuaran#1661). A leaf's text is no longer a plain value: the constructor widens it into a child
/// source node, exactly as a field widens in memory from a literal to a richer carrier. Every
/// resulting tree is perfectly encodable and perfectly decodable — the codec never sees a problem —
/// but what an author now builds from a corpus document is not what that document says.
let rec private constructWidened (n: RNode) : Result<RNode, string> =
    if List.isEmpty n.Children then
        Ok(RNode.node n.Id n.Kind [ RNode.leaf (n.Id + ".src") "textSource" n.Value ])
    else
        mapChildren constructWidened [] n.Children
        |> Result.map (RNode.node n.Id n.Kind)

/// A surface that REFUSES — a validating constructor that rejects a value the domain's own codec
/// decoded. A different finding from a divergent one, and the family separates them.
let private constructRefusing (_: RNode) : Result<RNode, string> =
    Error "a section may not be constructed without a title"

let private honest: ConstructWitness<RNode> =
    { Surface = "the RNode smart constructors"
      Construct = constructThrough }

let private widened: ConstructWitness<RNode> =
    { Surface = "the widened RNode constructors"
      Construct = constructWidened }

let private refusing: ConstructWitness<RNode> =
    { Surface = "the validating RNode constructors"
      Construct = constructRefusing }

// ---------------------------------------------------------------------------
//  the corpus — documents written by the codec itself, so they are canonical
// ---------------------------------------------------------------------------

let private document (name: string) (n: RNode) : Corpus.Case =
    { Name = name
      Kind = Corpus.RoundTrip
      Json = encode n
      Tag = n.Kind }

let private rejectCase: Corpus.Case =
    { Name = "no-kind"
      Kind = Corpus.Reject
      Json = "{\"id\":\"x\"}"
      Tag = "reject" }

let private corpus =
    [ document "sample-tree" (sample ())
      document "single-leaf" (RNode.leaf "a1" "para" "x")
      document "empty-section" (RNode.node "s" "section" [ RNode.leaf "s1" "para" "" ])
      rejectCase ]

let private law (n: int) (results: LawResult list) = List.item n results

[<Tests>]
let tests =
    testList
        "ConstructThenEncode (Phase 126)"
        [

          testCase "the reference authoring surface certifies green over the reference corpus"
          <| fun _ ->
              let results =
                  Conformance.constructThenEncodeLaws "reference" codec (Some honest) corpus

              Expect.equal (List.length results) 3 "three laws: non-vacuity, acceptance, the law itself"

              Expect.isTrue
                  (results |> List.forall (fun r -> r.Passed))
                  (sprintf
                      "rebuilding through RNode.node / RNode.leaf re-encodes every corpus document: %A"
                      (results |> List.filter (fun r -> not r.Passed)))

          // The go-red, and the reason the family exists: the codec suite is green over the SAME
          // corpus throughout. A round-trip law starts at bytes and ends at bytes, so the authoring
          // surface is not on its path and no amount of it can see this.
          testCase "a widened authoring constructor is caught, while the codec round-trip stays green"
          <| fun _ ->
              let codecOutcomes = Corpus.runCorpus codec corpus

              Expect.isTrue
                  (codecOutcomes |> List.forall (fun o -> o.Passed))
                  (sprintf
                      "the codec certifies over this corpus: %A"
                      (codecOutcomes |> List.filter (fun o -> not o.Passed)))

              let results =
                  Conformance.constructThenEncodeLaws "reference" codec (Some widened) corpus

              Expect.isTrue (law 0 results).Passed "the corpus is not vacuous"
              Expect.isTrue (law 1 results).Passed "the widened surface accepts every document — it does not refuse"
              Expect.isFalse (law 2 results).Passed "and the widening is caught by the construct-then-encode law"

              match (law 2 results).Counterexample with
              | None -> failtest "a failing law must carry a counterexample"
              | Some cx ->
                  Expect.stringContains cx "sample-tree" "the counterexample names the corpus document"
                  Expect.stringContains cx "textSource" "and shows what the authoring surface built"

                  Expect.stringContains
                      cx
                      "the plain codec round-trip PASSES"
                      "and states that the codec is green over the same document — the finding itself"

          testCase "a refusing authoring surface is a distinct finding from a divergent one"
          <| fun _ ->
              let results =
                  Conformance.constructThenEncodeLaws "reference" codec (Some refusing) corpus

              Expect.isFalse (law 1 results).Passed "the acceptance law carries the refusal"

              Expect.isTrue
                  (law 2 results).Passed
                  "and the construct-then-encode law is not also reddened — nothing was built to compare"

              match (law 1 results).Counterexample with
              | None -> failtest "a failing law must carry a counterexample"
              | Some cx ->
                  Expect.stringContains cx "the validating RNode constructors" "the surface is named"
                  Expect.stringContains cx "a section may not be constructed" "and its own refusal is quoted"

          // The acceptance criterion in as many words: never as passed, and never silently absent.
          testCase "a domain supplying no construct witness is reported by NAME as not adopted"
          <| fun _ ->
              let results = Conformance.constructThenEncodeLaws "the UI tier" codec None corpus

              Expect.equal (List.length results) 1 "one result — the family reports itself, it does not vanish"
              Expect.isFalse (law 0 results).Passed "not adopted is never counted as passed"
              Expect.stringContains (law 0 results).Law "the UI tier" "the report names the domain"
              Expect.stringContains (law 0 results).Law "NOT ADOPTED" "in the vocabulary a census row reads"

              match (law 0 results).Counterexample with
              | None -> failtest "not-adopted must say what is uncertified and what to do"
              | Some cx -> Expect.stringContains cx "ConstructWitness" "and name the witness to supply"

          testCase "a corpus with no round-trip document fails the family rather than certifying it"
          <| fun _ ->
              let empty = Conformance.constructThenEncodeLaws "reference" codec (Some honest) []

              Expect.isFalse (law 0 empty).Passed "an empty corpus is vacuous, not green"

              // A `Reject` case is not a document: it is a JSON the decoder must refuse, so reading
              // it as one would report a decode failure as an authoring defect.
              let rejectsOnly =
                  Conformance.constructThenEncodeLaws "reference" codec (Some honest) [ rejectCase ]

              Expect.isFalse (law 0 rejectsOnly).Passed "a corpus of reject cases offers no document to rebuild"
              Expect.isTrue (law 1 rejectsOnly).Passed "and the reject case is not run through the decoder here" ]
