namespace Fuaran.Core.Tests

open Fuaran.Core
open Fuaran.Core.Idl

/// The IDL's wider data universe as conformance vectors (Phase 639, DECISIONS.md D251): documents of the
/// reference vocabulary's `Tally` kind, which holds every type of it, each with the canonical bytes a
/// reader re-encodes it to or the refusal a reader answers. The interpreter answers each here; the
/// generated F# host and the generated TypeScript host are held to the same answers by
/// `IdlWiderUniverseTests`. Written by `render idl`, held to a fresh render.
module WiderUniverseCorpus =

    let familyDirName = "idl"
    let fileName = "wider-universe.json"

    /// A `Tally` whose every member is a canonical value: `name` and its JSON text, in key order.
    let private baseMembers: (string * string) list =
        [ "amount", "\"12.25\""
          "at", "\"23:59:59.9999999\""
          "count", "\"9007199254740993\""
          "digest", "\"AAE=\""
          "grid", "[[[10,false],\"ten\"],[[9,true],\"nine\"]]"
          "levels", "[[\"high\",\"1.5s\"],[\"low\",\"90s\"]]"
          "pair", "[\"-9223372036854775808\",[\"x\",2.5]]"
          "span", "\"0.0000001s\""
          "tags", "[\"Z\",\"a\",\"é\",\"￿\",\"\U0001F600\"]" ]

    /// The document with `changes` applied: a member replaced by its new text, or removed (`None`).
    let private document (changes: (string * string option) list) : string =
        let members =
            baseMembers
            |> List.choose (fun (n, v) ->
                match changes |> List.tryFind (fun (c, _) -> c = n) with
                | Some(_, Some v') -> Some(n, v')
                | Some(_, None) -> None
                | None -> Some(n, v))

        "{\"id\":\"t\",\"kind\":{\"$type\":\"Tally\","
        + (members |> List.map (fun (n, v) -> "\"" + n + "\":" + v) |> String.concat ",")
        + "}}"

    /// One vector: its name and its document.
    let vectors: (string * string) list =
        let one name changes = name, document changes

        [ // Round trips — each type at its boundaries.
          one "every-type" []
          one "optional-members-absent" [ "at", None; "digest", None ]
          one "int64-extremes" [ "count", Some "\"9223372036854775807\""; "pair", Some "[\"-1\",[\"\",-0.5]]" ]
          one "decimal-precision" [ "amount", Some "\"-0.0000000000000000000000000001\"" ]
          one "decimal-beyond-a-host-decimal" [ "amount", Some "\"79228162514264337593543950336\"" ]
          one "bytes-empty" [ "digest", Some "\"\"" ]
          one "bytes-one-pad" [ "digest", Some "\"/+8=\"" ]
          one "time-of-day-midnight" [ "at", Some "\"00:00:00\"" ]
          one
              "duration-extremes"
              [ "span", Some "\"-922337203685.4775808s\""
                "levels", Some "[[\"low\",\"922337203685.4775807s\"]]" ]
          one "duration-whole-second" [ "span", Some "\"0s\"" ]
          one "set-empty" [ "tags", Some "[]" ]
          one "set-code-point-order" [ "tags", Some "[\"￿\",\"\U0001F600\"]" ]
          one "keyed-map-empty" [ "grid", Some "[]"; "levels", Some "[]" ]
          one "keyed-map-key-text-order" [ "grid", Some "[[[-1,true],\"a\"],[[1,false],\"b\"],[[1,true],\"c\"]]" ]
          // Rejects — every spelling but the canonical one, and every order but the canonical one.
          one "int64-above-range" [ "count", Some "\"9223372036854775808\"" ]
          one "int64-below-range" [ "pair", Some "[\"-9223372036854775809\",[\"x\",2.5]]" ]
          one "int64-as-number" [ "count", Some "5" ]
          one "int64-leading-zero" [ "count", Some "\"01\"" ]
          one "int64-negative-zero" [ "count", Some "\"-0\"" ]
          one "int64-plus" [ "count", Some "\"+1\"" ]
          one "decimal-trailing-zero" [ "amount", Some "\"1.50\"" ]
          one "decimal-exponent" [ "amount", Some "\"1e3\"" ]
          one "decimal-negative-zero" [ "amount", Some "\"-0\"" ]
          one "decimal-bare-point" [ "amount", Some "\".5\"" ]
          one "bytes-unused-bits" [ "digest", Some "\"AB==\"" ]
          one "bytes-unpadded" [ "digest", Some "\"AA\"" ]
          one "bytes-url-alphabet" [ "digest", Some "\"-_8=\"" ]
          one "time-of-day-hour-24" [ "at", Some "\"24:00:00\"" ]
          one "time-of-day-trailing-zero" [ "at", Some "\"12:00:00.10\"" ]
          one "time-of-day-eighth-digit" [ "at", Some "\"12:00:00.00000001\"" ]
          one "time-of-day-no-seconds" [ "at", Some "\"12:00\"" ]
          one "time-of-day-offset" [ "at", Some "\"12:00:00Z\"" ]
          one "duration-trailing-zero" [ "span", Some "\"1.0s\"" ]
          one "duration-negative-zero" [ "span", Some "\"-0s\"" ]
          one "duration-eighth-digit" [ "span", Some "\"0.00000001s\"" ]
          one "duration-above-range" [ "span", Some "\"922337203685.4775808s\"" ]
          one "duration-calendar-spelling" [ "span", Some "\"PT1S\"" ]
          one "duration-no-unit" [ "span", Some "\"1\"" ]
          one "tuple-short" [ "pair", Some "[\"1\"]" ]
          one "tuple-long" [ "pair", Some "[\"1\",[\"x\",2.5],3]" ]
          one "tuple-as-object" [ "pair", Some "{\"0\":\"1\"}" ]
          one "set-duplicate" [ "tags", Some "[\"a\",\"a\"]" ]
          one "set-out-of-order" [ "tags", Some "[\"b\",\"a\"]" ]
          one "set-utf16-order" [ "tags", Some "[\"\U0001F600\",\"￿\"]" ]
          one "keyed-map-duplicate-key" [ "levels", Some "[[\"low\",\"1s\"],[\"low\",\"2s\"]]" ]
          one "keyed-map-numeric-order" [ "grid", Some "[[[9,true],\"nine\"],[[10,false],\"ten\"]]" ]
          one "keyed-map-entry-not-a-pair" [ "levels", Some "[[\"low\"]]" ]
          one "keyed-map-entry-not-an-array" [ "levels", Some "[\"low\"]" ]
          one "keyed-map-as-object" [ "levels", Some "{\"low\":\"1s\"}" ] ]

    /// The interpreter's answer for one document: `Ok` its canonical re-encoding, or `Error` the refusal's
    /// code and path.
    let answer (doc: string) : Result<string, string * JVal list> =
        match Decode.decodeDetailed ReferenceIdl.refIdl doc with
        | Error e ->
            Error(
                DecodeError.codeName e.Code,
                e.Path
                |> List.map (function
                    | PathSegment.Key k -> JStr k
                    | PathSegment.Index i -> JInt i)
            )
        | Ok v ->
            match Encode.encode ReferenceIdl.refIdl v with
            | Ok bytes -> Ok bytes
            | Error m -> failwithf "the interpreter decoded vector %s and refused to re-encode it: %s" doc m

    let private description =
        "The IDL's wider data universe (Phase 639, DECISIONS.md D251): documents of the reference vocabulary's `Tally` kind, which holds a 64-bit integer, a decimal, bytes, a time of day, a duration, tuples, a set and two keyed maps. Each vector is a document and either `output`, the canonical bytes a reader re-encodes it to (equal to the input exactly when the input was canonical), or `refusal`, the code and the path from the document root a reader refuses it with. Every type has one canonical spelling; a set's elements and a keyed map's keys stand in ascending order of their canonical texts compared by code point, distinct."

    let render () : string =
        CorpusFile.render
            { Head =
                [ "family", CorpusFile.str "idlWiderUniverse"
                  "description", CorpusFile.str description
                  "vocabulary", CorpusFile.str "tests/Fuaran.Core.Tests.Fixtures/ReferenceIdl.fs" ]
              Items = "vectors"
              Rows =
                vectors
                |> List.map (fun (name, doc) ->
                    CorpusCell.Row(
                        [ "name", CorpusFile.str name
                          "answeredBy", CorpusFile.answeredBy AnsweredBy.Reference
                          "input", CorpusFile.str doc ]
                        @ (match answer doc with
                           | Ok bytes -> [ "output", CorpusFile.str bytes ]
                           | Error(code, path) ->
                               [ "refusal",
                                 CorpusCell.Row [ "code", CorpusFile.str code; "path", CorpusCell.Value(JArr path) ] ])
                    )) }

    let path (dir: string) =
        System.IO.Path.Combine(dir, familyDirName, fileName)
