module Fuaran.Core.Tests.RefusalVectorTests

open Expecto
open Fuaran.Core

// ============================================================================
//  Phase 299 — the codec REFUSAL corpus, `conformance/refusals/`.
//
//  The parser now holds to the JSON number grammar and refuses a lone or
//  ill-ordered surrogate; the columnar codec refuses a cell outside its column's
//  type, a repeated name, non-canonical date and timestamp text and a ragged
//  table, and reads a whole-valued number token into a decimal column. Each is a
//  statement the host codec twins must make too, so each is a VECTOR here: an
//  authored input, and the outcome computed by CALLING the codec — a refusal's
//  class, or an acceptance's canonical bytes — never by restating what it ought
//  to answer. The authored table also names the outcome each vector is FOR, and
//  a test holds the computed outcome to it, so a codec change that moves a
//  vector is a red test naming it before it is a re-emitted file.
//
//  The family is SELF-ENUMERATED (its own `manifest.json`, with per-host
//  adoption), the shape `apply/` uses and for its reason: the corpus root
//  manifest indexes the UI wire-format codec families only. It is emitted, never
//  hand-edited:  dotnet run --project tools/Fuaran.Core.Tools -- render refusals [<dir>]
//  and the suite holds the committed files to a fresh render.
// ============================================================================


[<Tests>]
let tests =
    testList
        "Refusal vectors (Phase 299)"
        [ testCase "every authored refusal vector gets the outcome it is for"
          <| fun _ ->
              for v in RefusalCorpus.vectors do
                  match v.Intent, RefusalCorpus.outcome v with
                  | RefusalCorpus.Refuse cls, Error got -> Expect.equal got cls (sprintf "%s refuses as %s" v.Name cls)
                  | RefusalCorpus.Accept, Ok _ -> ()
                  | intent, got -> failtestf "%s: authored for %A, the codec answered %A" v.Name intent got

              let names = RefusalCorpus.vectors |> List.map _.Name
              Expect.equal (List.distinct names) names "vector names are unique"

          // Phase 462 — a row binds every ENTRY POINT that turns JSON text into a value, not one
          // parser. Since Phase 519 there are four readers — `Json.read`, `Json.parse`,
          // `Decoder.parse` and `Reader.read` — and each `json` vector is put to every one, under
          // both named read options, and each must give the answer `Json.read ReadOptions.strict`
          // gives: the same refusal in each reader's own error type, or an acceptance of the same
          // value.
          testCase "every json vector gets one answer at every decode entry point"
          <| fun _ ->
              let rec walk (r: Reader) (input: string) : unit =
                  // A structural reading through the Reader's own object and array loops.
                  match input.TrimStart() with
                  | t when t.StartsWith "{" -> Reader.members (fun _ r -> Reader.value r |> ignore) r
                  | t when t.StartsWith "[" -> Reader.items (fun r -> Reader.value r |> ignore) r
                  | _ -> Reader.value r |> ignore

              for v in RefusalCorpus.vectors |> List.filter (fun v -> v.Codec = "json") do
                  let reference = Json.read ReadOptions.strict v.Input
                  let typed = reference |> Result.mapError DecodeError.ofJsonRefusal

                  // no vector carries a null, so the tolerant policy has nothing to erase
                  Expect.equal
                      (Json.read ReadOptions.tolerantOfNull v.Input)
                      reference
                      (sprintf "%s: Json.read tolerantOfNull" v.Name)

                  match reference, Json.parse v.Input with
                  | Ok a, Ok b -> Expect.equal b a (sprintf "%s: Json.parse" v.Name)
                  | Error(JsonRefusal.Malformed e), Error m ->
                      Expect.equal
                          m
                          ("not valid JSON: " + e.Message + " at position " + string e.Position)
                          (sprintf "%s: Json.parse" v.Name)
                  | a, b -> failtestf "%s: Json.read answered %A, Json.parse %A" v.Name a b

                  for name, options in [ "strict", ReadOptions.strict; "tolerantOfNull", ReadOptions.tolerantOfNull ] do
                      Expect.equal (Decoder.parse options v.Input) typed (sprintf "%s: Decoder.parse %s" v.Name name)

                      let expected = typed |> Result.map ignore

                      Expect.equal
                          (Reader.read options (Reader.value >> ignore) v.Input)
                          expected
                          (sprintf "%s: Reader.value %s" v.Name name)

                      Expect.equal
                          (Reader.read options (fun r -> walk r v.Input) v.Input)
                          expected
                          (sprintf "%s: Reader structural %s" v.Name name)

          // Phase 462 — an erased member null is NOT an occurrence of its name (D157): the tolerant
          // read is the strict read of the document with its member nulls deleted (Phase 190), and
          // a repeated name the deletion removes was never there.
          testCase "an erased member null does not count as an occurrence of its name"
          <| fun _ ->
              let tolerant = Json.read ReadOptions.tolerantOfNull
              let strict = Json.read ReadOptions.strict

              Expect.equal (tolerant """{"a":null,"a":1}""") (strict """{"a":1}""") "erased first"

              Expect.equal (tolerant """{"a":1,"a":null}""") (strict """{"a":1}""") "erased second"

              match tolerant """{"a":1,"a":2}""" with
              | Error(JsonRefusal.Malformed e) ->
                  Expect.equal e.Kind DuplicateMember "two surviving members still repeat"
              | other -> failtestf "expected DuplicateMember, got %A" other

          testCase "a repeated member is refused at its name's opening quote"
          <| fun _ ->
              match Json.read ReadOptions.strict """{"a":1,"b":2,"a":3}""" with
              | Error(JsonRefusal.Malformed e) ->
                  Expect.equal e.Kind DuplicateMember "kind"
                  Expect.equal e.Position 13 "at the second \"a\""
              | other -> failtestf "expected DuplicateMember, got %A" other

          // The amendment's culture leg: the corpus answers the same under a culture whose negative
          // sign is not U+002D, because the parser's integer reader is the invariant one.
          testCase "every vector gets the same outcome under fa-IR and he-IL"
          <| fun _ ->
              let invariant = RefusalCorpus.vectors |> List.map RefusalCorpus.outcome

              for culture in [ "fa-IR"; "he-IL" ] do
                  let saved = System.Globalization.CultureInfo.CurrentCulture
                  System.Globalization.CultureInfo.CurrentCulture <- System.Globalization.CultureInfo culture

                  try
                      Expect.equal
                          (RefusalCorpus.vectors |> List.map RefusalCorpus.outcome)
                          invariant
                          (sprintf "the corpus under %s" culture)
                  finally
                      System.Globalization.CultureInfo.CurrentCulture <- saved ]
