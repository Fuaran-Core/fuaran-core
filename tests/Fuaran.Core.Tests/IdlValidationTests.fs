module Fuaran.Core.Tests.IdlValidationTests

// ---------------------------------------------------------------------------
// Phase 292 — a vocabulary is validated data, and the generator escapes everything it
// splices.
//
// Four claims, each pinned here with a case that goes red when the mechanism behind it is
// removed: `Declare.errors` names every rule a vocabulary breaks and every loading path
// refuses what it names; every splice of IDL-authored text into generated source goes
// through `SourceLit` (a hostile enum wire string, a hostile annotation, a hostile category
// and a hostile discriminator never appear in an artefact as source); `Decode`, `Encode`
// and `Sample` are total over an adversarial vocabulary corpus; and the sampler draws only
// values the encoder accepts.
// ---------------------------------------------------------------------------

open System.IO
open System.Text.RegularExpressions
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl

let private f (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Annotations = Annotations.Empty }

let private kind (tag: string) (fields: IdlField list) : IdlKind =
    { Tag = tag
      Category = "test"
      Fields = fields
      Annotations = Annotations.Empty }

let private case (tag: string) (fields: IdlField list) : IdlUnionCase =
    { Tag = tag
      Fields = fields
      Annotations = Annotations.Empty }

/// A small well-formed vocabulary touching every rule's subject: a two-parameter union, an
/// enum with a wire mapping, a float slot with an INT default, a marker kind and a record.
let private small: Idl =
    { Kinds =
        [ kind
              "Note"
              [ f "text" TStr Required
                f "level" (TEnum "Level") (OmitDefault(VEnum "low"))
                f "ratio" TFloat (OmitDefault(VInt 2))
                f "tag" TStr Optional ]
          kind "Paired" [ f "p" (TUnion("Pair", [ TStr; TInt ])) Required ]
          kind "Marker" []
          kind "Shaped" [ f "shape" (TRecord "Shape") Required; f "extra" TJson Optional ] ]
      Unions =
        [ { Name = "Pair"
            Params = [ "A"; "B" ]
            Cases = [ case "Of" [ f "a" (TVar "A") Required; f "b" (TVar "B") Required ] ] } ]
      Enums = [ Declare.enumWith "Level" [ "Low", "low"; "High", "high" ] ]
      Records =
        [ { Name = "Shape"
            Fields = [ f "w" TInt Required; f "label" TStr Optional ] } ]
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      Harden = HardenPolicy.Undeclared }

/// An unpaired surrogate. Built from its code unit because an F# string literal cannot carry
/// one: the compiler reads `"\uD800"` as U+FFFD (measured), which is why `SourceLit.fsString`
/// writes U+FFFD for one too.
let private lone (unit: int) : string = string (char unit)

let private withKinds (ks: IdlKind list) (idl: Idl) = { idl with Kinds = idl.Kinds @ ks }

let private deprecated (message: string) : Annotations =
    { Annotations.Empty with
        Deprecated =
            Some
                { Replacement = None
                  Message = Some message } }

/// The adversarial vocabulary corpus: each entry breaks ONE rule, and names the fragment
/// its error must carry. Every one of them used to load through `Artifact.ofJson`.
let private adversarial: (string * string * Idl) list =
    [ "a quote in the discriminator",
      "discriminator",
      { small with
          Wire =
              { WireShape.Default with
                  Discriminator = "ty\"pe" } }
      "a dangling enum name", "enum 'Nope'", small |> withKinds [ kind "Bad" [ f "x" (TEnum "Nope") Required ] ]
      "a dangling record name", "record 'Nope'", small |> withKinds [ kind "Bad" [ f "x" (TRecord "Nope") Required ] ]
      "a dangling union name", "union 'Nope'", small |> withKinds [ kind "Bad" [ f "x" (TUnion("Nope", [])) Required ] ]
      "a union applied to the wrong arity",
      "applies union 'Pair' to 1",
      small |> withKinds [ kind "Bad" [ f "x" (TUnion("Pair", [ TStr ])) Required ] ]
      "a duplicate kind tag", "kind 'Note' is declared more than once", small |> withKinds [ kind "Note" [] ]
      "a duplicate case tag",
      "case 'Of' is declared more than once",
      { small with
          Unions =
              [ { Name = "Pair"
                  Params = [ "A"; "B" ]
                  Cases = [ case "Of" []; case "Of" [] ] } ] }
      "a type name declared twice",
      "type name 'Level'",
      { small with
          Records = small.Records @ [ { Name = "Level"; Fields = [] } ] }
      "a wrong-typed omit-at-default",
      "omit-at-default value does not fit",
      small |> withKinds [ kind "Bad" [ f "n" TInt (OmitDefault(VStr "zero")) ] ]
      "a wrong-typed declared default",
      "the value does not fit",
      { small with
          Defaults =
              [ { Kind = "Note"
                  Field = "text"
                  Value = VInt 3 } ] }
      "a HostOnly field that is not a TFn",
      "HostOnly field must be a TFn",
      small |> withKinds [ kind "Bad" [ f "h" TStr HostOnly ] ]
      "a field name that is not an identifier",
      "field 'a b' is not an identifier",
      small |> withKinds [ kind "Bad" [ f "a b" TStr Required ] ]
      "a kind tag that is source text",
      "kind 'X\"); evil(); (\"' is not an identifier",
      small |> withKinds [ kind "X\"); evil(); (\"" [] ]
      "a newline in a deprecation message",
      "line break or control character",
      small
      |> withKinds
          [ { kind "Bad" [] with
                Annotations = deprecated "retired\n__INJECTED__();" } ]
      "a newline in a category",
      "the category carries a line break",
      small
      |> withKinds
          [ { kind "Bad" [] with
                Category = "layout\n__INJECTED__();" } ]
      "an unbound type variable",
      "type variable 'Z'",
      { small with
          Unions =
              small.Unions
              @ [ { Name = "Loose"
                    Params = [ "A" ]
                    Cases = [ case "One" [ f "v" (TVar "Z") Required ] ] } ] }
      "an object-capable transparent case",
      "can encode to an object",
      { small with
          Unions =
              small.Unions
              @ [ { Name = "Lit"
                    Params = []
                    Cases = [ case "Raw" [ f "v" TJson Required ] ] } ]
          Harden =
              { HardenPolicy.Undeclared with
                  TransparentUnions = [ "Lit", "Raw" ] } }
      "an instantiation making a transparent case object-capable",
      "instantiates union 'Wrap'",
      { small with
          Unions =
              small.Unions
              @ [ { Name = "Wrap"
                    Params = [ "T" ]
                    Cases = [ case "Bare" [ f "v" (TVar "T") Required ] ] } ]
          Harden =
              { HardenPolicy.Undeclared with
                  TransparentUnions = [ "Wrap", "Bare" ] } }
      |> withKinds [ kind "Bad" [ f "w" (TUnion("Wrap", [ TRecord "Shape" ])) Required ] ]
      "an envelope field named kind under the nested shape",
      "field 'kind' is reserved",
      { small with
          NodeFields = [ f "kind" TStr Optional ] }
      "an ill-formed wire string",
      "ill-formed UTF-16",
      { small with
          Enums = [ Declare.enumWith "Level" [ "Low", "low"; "High", "hi" + lone 0xD800 + "gh" ] ] } ]

let private noThrow (what: string) (thunk: unit -> 'a) : 'a option =
    try
        Some(thunk ())
    with e ->
        failtestf "%s raised %s: %s" what (e.GetType().Name) e.Message

[<Tests>]
let declareErrorsTests =
    testList
        "Phase 292 — Declare.errors"
        [ test "every vocabulary the suite certifies against is well-formed" {
              // The falsifier for the rules below: each one fires on the corpus, so an empty
              // list here is the rules NOT over-reaching onto real vocabularies.
              for name, idl in
                  [ "small", small
                    "mini", MiniIdl.miniIdl
                    "reference", ReferenceIdl.refIdl
                    "value-coverage", ReferenceIdl.valueCoverageIdl
                    "unsorted-coverage", ReferenceIdl.unsortedCoverageIdl
                    "second-domain", SecondDomainSpike.docIdl
                    "score", ScoreDomainSpike.scoreIdl
                    "doc-annotated", IdlDocAnnotationTests.docIdl ] do
                  Expect.isEmpty (Declare.errors idl) (sprintf "%s: Declare.errors" name)
          }

          test "each adversarial vocabulary is refused, naming the rule it breaks" {
              for name, fragment, idl in adversarial do
                  let errs = Declare.errors idl
                  Expect.isNonEmpty errs (sprintf "%s: refused" name)

                  Expect.isTrue
                      (errs |> List.exists (fun e -> e.Contains fragment))
                      (sprintf "%s: an error names '%s' — got %A" name fragment errs)
          }

          test "a zero-field kind and a zero-field record are well-formed, and the artifact carries them" {
              let idl =
                  { small with
                      Records = small.Records @ [ { Name = "Empty"; Fields = [] } ] }

              Expect.isEmpty (Declare.errors idl) "marker kind and empty record"

              match Artifact.parse (Artifact.render idl) with
              | Ok back -> Expect.equal (Artifact.render back) (Artifact.render idl) "the artifact round-trips"
              | Error e -> failtestf "the artifact did not read back: %s" e
          }

          test "Artifact.ofJson refuses a hand-edited idl.json, naming EVERY error" {
              // A quote in the discriminator, a newline in an annotation, a dangling type
              // name and a wrong-typed default, in one artifact — the acceptance's four.
              let bad =
                  { small with
                      Wire =
                          { WireShape.Default with
                              Discriminator = "ty\"pe" }
                      Defaults =
                          [ { Kind = "Note"
                              Field = "text"
                              Value = VInt 3 } ] }
                  |> withKinds
                      [ { kind "Bad" [ f "x" (TEnum "Nope") Required ] with
                            Annotations = deprecated "retired\n__INJECTED__();" } ]

              match Artifact.parse (Artifact.render bad) with
              | Ok _ -> failtest "a vocabulary with four errors loaded"
              | Error e ->
                  for fragment in [ "discriminator"; "line break"; "enum 'Nope'"; "does not fit" ] do
                      Expect.stringContains e fragment (sprintf "the refusal names '%s'" fragment)
          }

          test "Proposal.applyDelta refuses a delta whose vocabulary is not well-formed" {
              match Proposal.applyDelta small [ AddKind(kind "Bad" [ f "x" (TEnum "Nope") Required ]) ] with
              | Ok _ -> failtest "a delta adding a dangling enum name applied"
              | Error e -> Expect.stringContains e "enum 'Nope'" "the refusal names the dangling enum"

              match Proposal.applyDelta small [ AddKind(kind "Fine" [ f "x" TStr Required ]) ] with
              | Ok _ -> ()
              | Error e -> failtestf "a well-formed delta was refused: %s" e
          } ]

/// A payload a correct escaper can never emit verbatim: a quote, a line break, and a line
/// that would be live code if the break reached the output raw.
let private payload = "q\"\n__INJECTED__();"

/// No artefact line may carry the payload's second line as source — only inside a comment
/// line, or escaped inside a literal on a line that also carries the escaped break.
let private expectNoInjection (what: string) (emitted: string) =
    Expect.isFalse (emitted.Contains payload) (what + ": the payload never appears verbatim")

    for line in emitted.Split('\n') do
        let t = line.TrimStart()

        if t.Contains "__INJECTED__" then
            Expect.isTrue
                (t.StartsWith "//" || t.StartsWith "(*" || line.Contains "\\n__INJECTED__")
                (sprintf "%s: the payload reached source as code: %s" what line)

/// A vocabulary built IN CODE — no loader sees it, so only the escaper stands between its
/// text and the generated source.
let private hostile: Idl =
    { small with
        Kinds =
            [ { kind "Note" [ f "level" (TEnum "Level") (OmitDefault(VEnum payload)) ] with
                  Category = "layout" + payload
                  Annotations =
                      { Annotations.Empty with
                          Deprecated =
                              Some
                                  { Replacement = Some("r" + payload)
                                    Message = Some payload }
                          Since = Some("1" + payload) } } ]
        Enums = [ Declare.enumWith "Level" [ "Low", payload; "High", "high" ] ] }

[<Tests>]
let sourceLitTests =
    testList
        "Phase 292 — SourceLit"
        [ test "a string literal's value is the authored string, for F#, TypeScript and F*" {
              let samples =
                  [ ""
                    "plain"
                    "q\"b\\s"
                    "a\nb\rc\td"
                    "nul\u0000bel\u0007"
                    "ls\u2028ps\u2029nel\u0085"
                    "astral-\U0001F600" ]

              for s in samples do
                  for name, lit in
                      [ "fsString", SourceLit.fsString s
                        "fsAttribute", SourceLit.fsAttribute s
                        "tsString", SourceLit.tsString s
                        "fstarString", SourceLit.fstarString s ] do
                      Expect.isFalse (lit.Contains "\n" || lit.Contains "\r") (sprintf "%s %A: no raw break" name s)

                      // The four escapes the policies write are JSON's own, so a JSON reader
                      // recovers the value exactly.
                      Expect.equal
                          (System.Text.Json.JsonSerializer.Deserialize<string>(lit))
                          s
                          (sprintf "%s %A: the literal's value" name s)
          }

          test "an unpaired surrogate is escaped in F# and TypeScript and replaced in F*" {
              Expect.equal (SourceLit.tsString ("a" + lone 0xDC00 + "b")) "\"a\\udc00b\"" "TypeScript spells it"
              Expect.equal (SourceLit.fsString ("a" + lone 0xD800 + "b")) "\"a\\ufffdb\"" "F#: no spelling, U+FFFD"
              Expect.equal (SourceLit.fstarString ("a" + lone 0xD800 + "b")) "\"a\\ufffdb\"" "F*: no spelling, U+FFFD"
              Expect.isFalse (SourceLit.isWellFormed ("x" + lone 0xDBFF)) "a trailing high surrogate"
              Expect.isTrue (SourceLit.isWellFormed "astral-\U0001F600") "a pair"
          }

          test "comment text is one line per authored line under every break, with nothing a comment cannot carry" {
              Expect.equal
                  (SourceLit.tsCommentLines "a\nb\r\nc\rd\u2028e\u2029f\u0085g")
                  [ "a"; "b"; "c"; "d"; "e"; "f"; "g" ]
                  "every break splits"

              Expect.equal
                  (SourceLit.fsDocLines ("x\u0001y" + lone 0xD800))
                  [ "x\uFFFDy\uFFFD" ]
                  "C0 and a lone surrogate"

              Expect.equal (SourceLit.fsDocLines "  \n ") [] "whitespace says nothing"
          }

          test "a TypeScript key is bare when JavaScript can spell it and a literal otherwise" {
              Expect.equal (SourceLit.tsKey "kind") "kind" "identifier"
              Expect.equal (SourceLit.tsKey "x: 1, y") "\"x: 1, y\"" "not an identifier"
              Expect.equal (SourceLit.tsStringSingle "it's \"q\"\n") "'it\\'s \"q\"\\n'" "the single-quoted spelling"
              Expect.equal (SourceLit.tsStringSingle "$type") "'$type'" "a plain key keeps its bytes"
          }

          test "go-red: hostile vocabulary text never reaches a generated artefact as source" {
              // Without SourceLit each of these emitted the payload's second line as code:
              // the enum wire string in an F# pattern and encoder literal, the deprecation
              // prose in `///` and `//` lines, the category in a `//` line.
              let tags = [ "Note" ]

              let artefacts =
                  [ "F# module", Gen.fsharpModule "Phase292.Hostile" hostile tags
                    "F# types", Gen.fsharpTypes hostile
                    "TypeScript module", Gen.typescriptModule hostile tags
                    "TypeScript declarations", Gen.typescriptDeclarations hostile tags
                    "F* vocabulary", FStarTarget.vocabularyModule "Phase292Hostile" hostile tags ]

              for name, result in artefacts do
                  match result with
                  | Ok emitted -> expectNoInjection name emitted
                  | Error e -> failtestf "%s refused the hostile vocabulary: %A" name e
          }

          test "go-red: a hostile discriminator is spliced as a literal in every backend" {
              let disc = "ty\"pe\n__INJECTED__();"

              let idl =
                  { small with
                      Wire =
                          { WireShape.Default with
                              Discriminator = disc } }

              for name, result in
                  [ "F# module", Gen.fsharpModule "Phase292.Disc" idl [ "Note" ]
                    "TypeScript module", Gen.typescriptModule idl [ "Note" ] ] do
                  match result with
                  | Ok emitted ->
                      Expect.isFalse (emitted.Contains disc) (name + ": the discriminator never appears raw")
                      expectNoInjection name emitted
                  | Error e -> failtestf "%s refused: %A" name e
          }

          test "go-red: a hostile VEnum is refused by the F# scaffold and escaped by the TypeScript one" {
              match Gen.fsharpValue small (TEnum "Level") (VEnum payload) with
              | Ok src -> failtestf "the F# scaffold emitted a wire string the enum does not admit: %s" src
              | Error _ -> ()

              // Admitted by the enum: F# writes the HOST case, TypeScript an escaped literal.
              match Gen.fsharpValue hostile (TEnum "Level") (VEnum payload) with
              | Ok src -> Expect.equal src "Level.Low" "the host case, never the wire text"
              | Error e -> failtestf "the F# scaffold refused an admitted value: %A" e

              match Gen.typescriptValue hostile (TEnum "Level") (VEnum payload) with
              | Ok src ->
                  Expect.equal src (SourceLit.tsString payload) "the escaped literal"
                  expectNoInjection "TypeScript value" src
              | Error e -> failtestf "the TypeScript scaffold refused an admitted value: %A" e
          }

          test "the scaffold writes a whole float at a float slot as a float literal" {
              match Gen.fsharpValue small TFloat (VInt 2), Gen.fsharpValue small TFloat (VFloat 2.0) with
              | Ok a, Ok b ->
                  Expect.equal a "2.0" "an int-authored float"
                  Expect.equal b "2.0" "a whole float"
              | r -> failtestf "refused: %A" r

              match Gen.fsharpValue small TFloat (VFloat nan) with
              | Ok src -> failtestf "a non-finite float has no F# literal, yet emitted %s" src
              | Error _ -> ()
          }

          test "go-red: the emitters hold no hand-rolled splice of text into a literal" {
              // The lexical half of the acceptance: a quote pasted around an interpolated
              // value, or a quoted `%s`/`%A` inside an escaped literal, is exactly the shape
              // every raw splice this phase removed had. The retired escapers are gone too,
              // so a second escaper cannot quietly grow back beside `SourceLit`.
              let shapes =
                  [ "a quote pasted before a splice", Regex("\"\\\\\"\"\\s*\\+")
                    "a quote pasted after a splice", Regex("\\+\\s*\"\\\\\"\"")
                    "a %s inside an escaped literal", Regex("\\\\\"[^\"\\\\]*%[sA][^\"\\\\]*\\\\\"")
                    "a retired escaper", Regex("\\b(fsAttrStr|fsDefaultStr|tsSourceStr|fsStringLit)\\b") ]

              let offences =
                  [ for rel in
                        [ "src/Fuaran.Core.Idl.Codegen/Codegen.fs"
                          "src/Fuaran.Core.Idl.Codegen/FStar.fs" ] do
                        let lines = File.ReadAllLines(Snapshots.repoFile rel)

                        for i in 0 .. lines.Length - 1 do
                            for what, rx in shapes do
                                if rx.IsMatch lines[i] then
                                    sprintf "%s:%d — %s: %s" rel (i + 1) what (lines[i].Trim()) ]

              Expect.isEmpty offences "no raw splice in an emitter"
          } ]

[<Tests>]
let codecTotalityTests =
    testList
        "Phase 292 — a total codec"
        [ test "the transparent bare-value arm refuses an arity mismatch rather than throwing" {
              match
                  noThrow "Decode.value" (fun () ->
                      Decode.value MiniIdl.miniIdl (TUnion("TextSource", [ TStr ])) (JStr "x"))
              with
              | Some(Error e) -> Expect.stringContains e "type args" "named as an arity refusal"
              | other -> failtestf "expected a refusal, got %A" other
          }

          test "a non-finite float inside a verbatim json value is refused by name; at a float slot it is §7's token" {
              match
                  Encode.encode
                      small
                      (VNode("n", "Shaped", [ "shape", VRecord [ "w", VInt 1 ]; "extra", VJson(JArr [ JFloat nan ]) ]))
              with
              | Ok wire -> failtestf "a NaN inside json encoded as %s" wire
              | Error e ->
                  Expect.stringContains e "NaN" "the token"
                  Expect.stringContains e "$[0]" "the path"

              match Encode.encode small (VNode("n", "Note", [ "text", VStr "t"; "ratio", VFloat nan ])) with
              | Ok wire -> Expect.stringContains wire "\"ratio\":\"NaN\"" "the §7 sentinel at a float slot"
              | Error e -> failtestf "a NaN at a float slot was refused: %s" e
          }

          test "an ill-formed string is refused at encode, naming its path" {
              match Encode.encode small (VNode("n", "Note", [ "text", VStr("a" + lone 0xD800) ])) with
              | Ok wire -> failtestf "a lone surrogate encoded as %s" wire
              | Error e -> Expect.stringContains e "ill-formed" "named"
          }

          test "omit-at-default compares in the slot's value space" {
              let wire (ratio: IdlValue) =
                  match Encode.encode small (VNode("n", "Note", [ "text", VStr "t"; "ratio", ratio ])) with
                  | Ok w -> w
                  | Error e -> failtestf "refused: %s" e

              Expect.equal (wire (VFloat 2.0)) (wire (VInt 2)) "one value, one encoding"
              Expect.isFalse ((wire (VFloat 2.0)).Contains "ratio") "a float equal to the int default is AT the default"
              Expect.stringContains (wire (VFloat 2.5)) "\"ratio\":2.5" "a different value is emitted"
          }

          test "Decode, Encode and Sample raise no exception over the adversarial vocabulary corpus" {
              for name, _, idl in adversarial do
                  let tags = idl.Kinds |> List.map (fun k -> k.Tag)

                  match noThrow (name + ": trySampleNodes") (fun () -> Sample.trySampleNodes idl tags 7 24) with
                  | Some(Ok vs) ->
                      for v in vs do
                          match noThrow (name + ": encode") (fun () -> Encode.encode idl v) with
                          | Some(Ok w) -> noThrow (name + ": decode") (fun () -> Decode.decode idl w) |> ignore
                          | _ -> ()
                  | _ -> ()

                  for text in
                      [ "{}"
                        "{\"id\":\"n\",\"kind\":{\"$type\":\"Bad\",\"x\":\"q\"}}"
                        "\"bare\""
                        "[1,2]" ] do
                      noThrow (name + ": decode " + text) (fun () -> Decode.decode idl text) |> ignore

                      noThrow (name + ": decodeOp " + text) (fun () -> Decode.decodeOp idl text)
                      |> ignore
          } ]

[<Tests>]
let samplerTotalityTests =
    testList
        "Phase 292 — a total sampler"
        [ test "nothing to cycle over, or a tag naming no kind, is a typed refusal" {
              match Sample.trySampleNodes small [] 1 3 with
              | Error r -> Expect.stringContains r.Describe "kind tags" "named"
              | Ok vs -> failtestf "sampled %A from no tags" vs

              match Sample.trySampleNodes small [ "Nope" ] 1 3 with
              | Error r -> Expect.stringContains r.At "Nope" "named"
              | Ok vs -> failtestf "sampled %A for an undeclared kind" vs

              Expect.throws (fun () -> Sample.sampleNodes small [] 1 3 |> ignore) "the throwing face raises the refusal"
          }

          test "an empty enum or union, or a type with no finite value, is refused rather than divided by" {
              let emptyEnum =
                  { small with
                      Enums = [ Declare.enumOf "Level" [] ] }
                  |> withKinds [ kind "E" [ f "l" (TEnum "Level") Required ] ]

              let selfRecord =
                  { small with
                      Records =
                          [ { Name = "Loop"
                              Fields = [ f "next" (TRecord "Loop") Required ] } ] }
                  |> withKinds [ kind "L" [ f "l" (TRecord "Loop") Required ] ]

              for name, idl, tag in [ "empty enum", emptyEnum, "E"; "self-referential record", selfRecord, "L" ] do
                  match noThrow name (fun () -> Sample.trySampleNodes idl [ tag ] 3 4) with
                  | Some(Error _) -> ()
                  | other -> failtestf "%s: expected a refusal, got %A" name other
          }

          test "every drawn value encodes: two type parameters bind by name, presence rules hold everywhere" {
              // The sampler's own substitution mapped EVERY variable to the first argument, so
              // `Pair<string, int>` drew `b` as a string and the encoder refused the vector.
              let ops =
                  [ kind "Edit" [ f "newKind" TKind Required; f "note" TStr Optional ]
                    kind "Batch" [ f "ops" (TList TOp) Required ] ]

              let idl =
                  { small with Ops = ops }
                  |> withKinds [ kind "Holder" [ f "k" TKind Required; f "op" TOp Required ] ]

              Expect.isEmpty (Declare.errors idl) "the vocabulary is well-formed"
              let tags = idl.Kinds |> List.map (fun k -> k.Tag)

              match Sample.trySampleNodes idl tags 292 400 with
              | Error r -> failtestf "refused: %s" r.Describe
              | Ok vs ->
                  for v in vs do
                      match Encode.encode idl v with
                      | Ok w ->
                          match Decode.decode idl w |> Result.bind (Encode.encode idl) with
                          | Ok w2 -> Expect.equal w2 w "decode >> encode is the identity"
                          | Error e -> failtestf "the drawn vector did not round-trip: %s (%s)" e w
                      | Error e -> failtestf "the sampler drew a value the encoder refuses: %s — %A" e v

                  // An Optional field of a bare kind is sometimes absent and sometimes present.
                  let editNotes =
                      vs
                      |> List.choose (function
                          | VNode(_, "Holder", fs) ->
                              match List.tryFind (fun (n, _) -> n = "op") fs with
                              | Some(_, VUnion("Edit", efs)) ->
                                  Some(List.exists (fun (n, v) -> n = "note" && v <> VAbsent) efs)
                              | _ -> None
                          | _ -> None)

                  Expect.contains editNotes true "an optional op field is drawn present"
                  Expect.contains editNotes false "an optional op field is drawn absent"
          } ]
