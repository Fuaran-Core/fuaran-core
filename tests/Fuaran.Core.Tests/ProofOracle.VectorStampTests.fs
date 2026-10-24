module Fuaran.Core.Tests.ProofOracle.VectorStampTests

// ---------------------------------------------------------------------------
//  Phase 478 — THE MODEL STAMP on the emitted vectors.
//
//  Each exporter evaluates the extracted model that asks a vector's question, through the bridges in
//  the fixtures library (`VectorOracle`), and stamps the vector `["reference", "model:<Model>"]` — or
//  fails the export on a disagreement. The committed files are held to a fresh render by the
//  `Exporters` family, so a stamp that is in a file was earned by the render that wrote it. These
//  cases hold the rest: every vector carries a well-formed stamp; the model stamps are where the
//  models answer (a census, so an exporter that silently stops asking its model is red); and a
//  disagreement refuses, naming the vector and the ladder row.
// ---------------------------------------------------------------------------


open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges

/// Every `answeredBy` stamp in one committed file: one per vector of a `vectors` file, and the
/// file's own for a `lines` file, whose lines carry no member.
let internal stampsOf (text: string) : Result<AnsweredBy list, string> =
    let memberOf k fields =
        fields |> List.tryFind (fun (n, _) -> n = k) |> Option.map snd

    let readAll entries =
        entries
        |> List.fold
            (fun acc (at, entry) ->
                acc
                |> Result.bind (fun read ->
                    match entry with
                    | Some e ->
                        AnsweredBy.read e
                        |> Result.map (fun a -> read @ [ a ])
                        |> Result.mapError (fun m -> at + ": " + m)
                    | None -> Error(at + " carries no `answeredBy`")))
            (Ok [])

    match Json.parse text with
    | Ok(JObj fields) ->
        match memberOf "vectors" fields, memberOf "lines" fields with
        | Some(JArr vectors), _ ->
            vectors
            |> List.mapi (fun i v ->
                match v with
                | JObj vf -> sprintf "vector %d" i, memberOf "answeredBy" vf
                | _ -> sprintf "vector %d" i, None)
            |> readAll
        | None, Some(JArr _) -> readAll [ "the file", memberOf "answeredBy" fields ]
        | _ -> Error "neither a `vectors` nor a `lines` file"
    | Ok _ -> Error "not an object"
    | Error e -> Error("does not parse: " + e)

/// The committed vector files: every emitted file under `conformance/` but the manifests and the
/// IDL schema, which index and declare rather than answer.
let vectorFiles () : string list =
    System.IO.Directory.GetFiles(OwnedConformance.root (), "*.json", System.IO.SearchOption.AllDirectories)
    |> Array.filter (fun p ->
        let name = System.IO.Path.GetFileName p
        name <> "manifest.json" && not (name.EndsWith ".schema.json"))
    |> Array.sort
    |> List.ofArray

let relative (p: string) =
    System.IO.Path.GetRelativePath(OwnedConformance.root (), p).Replace(System.IO.Path.DirectorySeparatorChar, '/')

/// The census: per file, how many vectors each extracted model answers. Every file not named here
/// is answered by the reference alone. A model wired into an exporter is one row here.
let modelStamps: (string * (string * int)) list =
    [ "apply/skeleton-apply.json", ("TreeOps", 25)
      "chain/skeleton-chain.json", ("Chain", 16)
      "dag/skeleton-dag.json", ("Chain", 4)
      "decode/decode-rejects.json", ("JsonParse", 8)
      "decode/decode-rejects.json", ("DecodeLayer", 28)
      "escape/string-escape.json", ("WireCanon", 1)
      "laws/capability-laws.json", ("Capability", 72)
      "laws/decimal-laws.json", ("DecimalText", 49)
      "laws/decimal-laws.json", ("WireColumn", 7)
      "laws/field-laws.json", ("WireColumn", 24)
      "laws/propagation-laws.json", ("Propagation", 73)
      "premises/int53.json", ("JsonParse", 29)
      "premises/recursion-depth.json", ("JsonParse", 13)
      "premises/string-model.json", ("JsonParse", 10)
      "refusals/codec-refusals.json", ("JsonParse", 59)
      "refusals/codec-refusals.json", ("WireColumn", 13)
      "refusals/seam-refusals.json", ("Capability", 55)
      "refusals/seam-refusals.json", ("Query", 45) ]

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [ testCase "every committed vector carries an answeredBy stamp a host can read"
          <| fun _ ->
              let files = vectorFiles ()
              Expect.isGreaterThanOrEqual (List.length files) 15 "the vector files were found"

              for p in files do
                  match stampsOf (System.IO.File.ReadAllText p) with
                  | Ok stamps -> Expect.isNonEmpty stamps (relative p + " carries stamps")
                  | Error e -> failtestf "%s: %s" (relative p) e

          testCase "the model stamps are where the extracted models answer, and nowhere else"
          <| fun _ ->
              for p in vectorFiles () do
                  let stamps =
                      match stampsOf (System.IO.File.ReadAllText p) with
                      | Ok s -> s
                      | Error e -> failtestf "%s: %s" (relative p) e

                  let models =
                      stamps
                      |> List.choose (function
                          | AnsweredBy.Model m -> Some m
                          | AnsweredBy.Reference -> None)
                      |> List.countBy id

                  let expected =
                      modelStamps |> List.filter (fun (f, _) -> f = relative p) |> List.map snd

                  Expect.equal models expected (relative p + "'s model stamps")

          testCase "a disagreement refuses the stamp, naming the vector, the model and the ladder row"
          <| fun _ ->
              let answer text : ModelAnswer =
                  { Model = "Propagation"
                    LadderRow = "propagation-differential"
                    Answer = text }

              Expect.equal
                  (AnsweredBy.agree "v-1" "{\"dirty\":[\"1\"]}" (Some(answer "{\"dirty\":[\"1\"]}")))
                  (Ok(AnsweredBy.Model "Propagation"))
                  "an agreeing model answer earns the model stamp"

              Expect.equal
                  (AnsweredBy.agree "v-1" "x" None)
                  (Ok AnsweredBy.Reference)
                  "no model answer is the reference's"

              match AnsweredBy.agree "v-1" "{\"dirty\":[\"1\"]}" (Some(answer "{\"dirty\":[]}")) with
              | Ok a -> failtestf "a perturbed model answer was stamped %A" a
              | Error e ->
                  for part in [ "v-1"; "Propagation"; "propagation-differential" ] do
                      Expect.stringContains e part "the refusal names the vector, the model and the row"

          testCase "go-red: an exporter handed a disagreeing model answer refuses to render the vector"
          <| fun _ ->
              let reference = CorpusCell.Row [ "dirty", CorpusFile.str "1" ]

              let perturbed: VectorOracle.Answer =
                  { Model = VectorOracle.PropagationModel.Model
                    LadderRow = VectorOracle.PropagationModel.LadderRow
                    Cell = CorpusCell.Row [ "dirty", CorpusFile.str "2" ] }

              Expect.throwsC
                  (fun () ->
                      VectorOracle.stamp "propagation-0-dirty-set" reference (Some perturbed)
                      |> ignore)
                  (fun e -> Expect.stringContains e.Message "propagation-0-dirty-set" "names the vector")

          testCase "the not-answered rule: a declined vector is reference-only only where its family declares it"
          <| fun _ ->
              let answer: VectorOracle.Answer =
                  { Model = "WireColumn"
                    LadderRow = "column-codec-differential"
                    Cell = CorpusCell.Value(JStr "x") }

              let declined =
                  VectorOracle.Declined("WireColumn", "column-codec-differential", "lenient ingest")

              let declared = Map.ofList [ "v-declared", "a JSON float in a decimal column" ]

              Expect.equal
                  (VectorOracle.admit declared "v-open" (VectorOracle.Answered answer))
                  (Some answer)
                  "an undeclared vector the model answers is the model's"

              Expect.equal
                  (VectorOracle.admit declared "v-declared" declined)
                  None
                  "a declared vector the model declines is the reference's"

              Expect.throwsC (fun () -> VectorOracle.admit declared "v-open" declined |> ignore) (fun e ->
                  for part in [ "v-open"; "WireColumn"; "column-codec-differential"; "does not declare" ] do
                      Expect.stringContains e.Message part "an undeclared decline is refused, by name")

              Expect.throwsC
                  (fun () ->
                      VectorOracle.admit declared "v-declared" (VectorOracle.Answered answer)
                      |> ignore)
                  (fun e ->
                      for part in [ "v-declared"; "WireColumn"; "stale" ] do
                          Expect.stringContains e.Message part "a declaration the model no longer needs is refused")

          testCase "a stamp outside the two shapes is refused on read"
          <| fun _ ->
              for bad in
                  [ JArr []
                    JArr [ JStr "model:Propagation" ]
                    JArr [ JStr "reference"; JStr "model:" ]
                    JArr [ JStr "reference"; JStr "oracle:Propagation" ]
                    JStr "reference" ] do
                  Expect.isError (AnsweredBy.read bad) (sprintf "%A is refused" bad) ]
