module Fuaran.Core.Tests.IdlOmitWhenEmptyTests

// ============================================================================
//  Phase 660 (DECISIONS.md D256) — an optional list declared omit-when-empty
//  (`PresenceCondition.WhenNonEmpty`): `Some []` and `None` write the same bytes,
//  an explicit `[]` reads as `None`, and the member's host type does not move.
//  Held over `OmitWhenEmptyIdl`, a fixture shaped like the UI tier's `Binding`
//  arms, on every leg: the interpreter, the generated F# codec (its strict and
//  collecting decoders, compiled and run), the generated TypeScript codec (run
//  under node), the JSON Schema, the artifact, the sampler, the validator's
//  refusal and the stability classifier. Its twin `OmitWhenEmptyIdl.plain`
//  declares nothing and is the go-red: it writes `Some []` as `[]`.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

let private idl = OmitWhenEmptyIdl.idl
let private plain = OmitWhenEmptyIdl.plain
let private kinds = OmitWhenEmptyIdl.kinds

/// A `Panel` holding `tags` and a `Query` binding holding `dependsOn`, each as given.
let private panel (tags: IdlValue) (dependsOn: IdlValue) =
    VNode(
        "n",
        "Panel",
        [ "title", VStr "t"
          "binding", VUnion("Query", [ "name", VStr "q"; "dependsOn", dependsOn ])
          "tags", tags ]
    )

/// A `Panel` whose binding is the `case` arm carrying `params`.
let private withParams (case: string) (lead: string) (ps: IdlValue) =
    VNode("n", "Panel", [ "title", VStr "t"; "binding", VUnion(case, [ lead, VStr "x"; "params", ps ]) ])

let private enc (v: Idl) (n: IdlValue) =
    Encode.encode v n |> Result.defaultWith failtest

/// The canonical bytes: no `tags`, no `dependsOn`.
let private absentBytes =
    """{"id":"n","kind":{"$type":"Panel","binding":{"$type":"Query","name":"q"},"title":"t"}}"""

/// The same document carrying both lists explicitly empty.
let private emptyBytes =
    """{"id":"n","kind":{"$type":"Panel","binding":{"$type":"Query","dependsOn":[],"name":"q"},"tags":[],"title":"t"}}"""

let private fullBytes =
    """{"id":"n","kind":{"$type":"Panel","binding":{"$type":"Query","dependsOn":["b"],"name":"q"},"tags":["a"],"title":"t"}}"""

let private faults (v: Idl) = Declare.errors v

/// The fixture with the `Panel.tags` field replaced.
let private withTags (edit: IdlField -> IdlField) : Idl =
    { idl with
        Kinds =
            idl.Kinds
            |> List.map (fun k ->
                { k with
                    Fields = k.Fields |> List.map (fun f -> if f.Name = "tags" then edit f else f) }) }

let private refusal = "a non-empty condition needs an Optional list member"

let private refusedAtPresence (v: Idl) =
    let fs = faults v

    match fs |> List.tryFind (fun f -> f.Detail.Contains refusal) with
    | Some f -> Expect.equal (List.last f.At) (PathSegment.Key "presence") "the refusal is at the member's presence"
    | None -> failtestf "expected the non-empty refusal, got %A" (fs |> List.map VocabularyFault.describe)

let private fsModule (v: Idl) =
    match Gen.fsharpModuleDerived GenSupport.Empty [ Derivation.SpecDecoders ] "OmitWhenEmpty" v kinds with
    | Ok s -> s
    | Error e -> failtestf "the F# backend refused: %s" (CodegenError.describe e)

/// Run the compiled generated module under `body`, answering its output lines, or skip without dotnet.
let private runFs (v: Idl) (body: string list) : string list =
    let script =
        IdlDeriveTests.Projected.scriptOf "OmitWhenEmpty" (fsModule v) (String.concat "\n" body)

    match IdlDeriveTests.Projected.runFsiWith "--checknulls+ --warnaserror+ " script with
    | None -> skiptest "dotnet not on PATH - the generated-codec run is skipped"
    | Some(code, out, err) ->
        Expect.equal code 0 (sprintf "the generated module compiles and runs: %s" err)

        out.Replace("\r\n", "\n").Split('\n')
        |> Array.filter (fun l -> l <> "")
        |> List.ofArray

/// The F# probe: each arm and the kind's own list written as `Some []` and as `None`, the explicit-empty
/// document read by both decoders, and the strict and collecting readings of a non-empty one.
let private fsProbe =
    let lit (s: string) = SourceLit.fsString s

    [ "let mk (tags: string list option) (b: Binding<string>) = { Id = \"n\"; Kind = NodeKind.Panel { Title = \"t\"; Binding = b; Tags = tags } }"
      "let p = [ { Name = \"k\"; Value = \"v\" } ]"
      "printfn \"%s\" (encodeNode (mk (Some []) (Binding.Query(\"q\", Some []))))"
      "printfn \"%s\" (encodeNode (mk None (Binding.Query(\"q\", None))))"
      "printfn \"%b\" (encodeNode (mk None (Binding.Transform(\"x\", Some []))) = encodeNode (mk None (Binding.Transform(\"x\", None))))"
      "printfn \"%b\" (encodeNode (mk None (Binding.Expr(\"x\", Some []))) = encodeNode (mk None (Binding.Expr(\"x\", None))))"
      "printfn \"%s\" (encodeNode (mk None (Binding.Expr(\"x\", Some p))))"
      sprintf
          "printfn \"%%b\" ((match decodeNode %s with Ok n -> Some n | Error _ -> None) = Some (mk None (Binding.Query(\"q\", None))))"
          (lit emptyBytes)
      sprintf
          "printfn \"%%b\" ((match decodeNodeAll %s with Ok n -> Some n | Error _ -> None) = Some (mk None (Binding.Query(\"q\", None))))"
          (lit emptyBytes)
      sprintf
          "printfn \"%%s\" (match decodeNodeAll %s with Ok n -> encodeNode n | Error e -> sprintf \"ERR %%A\" e)"
          (lit fullBytes) ]

[<Tests>]
let tests =
    testList
        "Idl.OmitWhenEmpty (Phase 660)"
        [ testCase "the fixture and its plain twin are well-formed, and generate the same F# types"
          <| fun _ ->
              Expect.isEmpty (faults idl) "the declaring vocabulary"
              Expect.isEmpty (faults plain) "the plain twin"

              Expect.equal
                  (Gen.fsharpTypes idl)
                  (Gen.fsharpTypes plain)
                  "the declaration moves no host type: each member stays a `'T list option`"

              match Gen.fsharpTypes idl with
              | Ok t -> Expect.stringContains t "dependsOn: string list option" "the arm keeps its option type"
              | Error e -> failtestf "refused: %s" (CodegenError.describe e)

          testCase "go-red: a vocabulary that does not declare it writes Some [] as [] (the bytes before Phase 660)"
          <| fun _ ->
              Expect.equal (enc plain (panel (VList []) (VList []))) emptyBytes "the old bytes"

              Expect.notEqual
                  (enc plain (panel (VList []) (VList [])))
                  (enc plain (panel VAbsent VAbsent))
                  "two spellings"

          testCase "the interpreter writes Some [] and None to the same bytes, and reads an explicit [] as None"
          <| fun _ ->
              Expect.equal (enc idl (panel (VList []) (VList []))) absentBytes "Some [] writes no member"
              Expect.equal (enc idl (panel VAbsent VAbsent)) absentBytes "None writes no member"

              Expect.equal
                  (enc idl (panel (VList [ VStr "a" ]) (VList [ VStr "b" ])))
                  fullBytes
                  "a non-empty list is written"

              for case, lead in [ "Transform", "source"; "Expr", "expr" ] do
                  Expect.equal
                      (enc idl (withParams case lead (VList [])))
                      (enc idl (withParams case lead VAbsent))
                      (case + ".params")

              Expect.equal
                  (Decode.decode idl emptyBytes |> Result.bind (Encode.encode idl))
                  (Ok absentBytes)
                  "decode (encode v) is the canonical value"

              Expect.equal (Decode.decode idl emptyBytes) (Decode.decode idl absentBytes) "[] reads as absent"

              match Decode.decodeDetailed idl (emptyBytes.Replace("\"tags\":[]", "\"tags\":\"x\"")) with
              | Ok v -> failtestf "a member of the wrong kind was dropped rather than refused: %A" v
              | Error e ->
                  Expect.equal
                      e.Path
                      [ PathSegment.Key "kind"; PathSegment.Key "tags" ]
                      "a non-array still reaches its decoder"

          testCase "the generated F# codec, strict and collecting, agrees with the interpreter (compiled and run)"
          <| fun _ ->
              let lines = runFs idl fsProbe

              Expect.equal
                  lines
                  [ absentBytes
                    absentBytes
                    "true"
                    "true"
                    """{"id":"n","kind":{"$type":"Panel","binding":{"$type":"Expr","expr":"x","params":[{"name":"k","value":"v"}]},"title":"t"}}"""
                    "true"
                    "true"
                    fullBytes ]
                  "Some [] and None write the same bytes; [] reads as None on both decoders"

          testCase "go-red: the plain twin's generated F# encoder writes Some [] as []"
          <| fun _ ->
              let lines =
                  runFs
                      plain
                      [ "printfn \"%s\" (encodeNode { Id = \"n\"; Kind = NodeKind.Panel { Title = \"t\"; Binding = Binding.Query(\"q\", Some []); Tags = Some [] } })" ]

              Expect.equal lines [ emptyBytes ] "the bytes before Phase 660"

          testCase "a vocabulary that does not declare it emits no helper for it"
          <| fun _ ->
              Expect.isFalse ((fsModule plain).Contains "dMember") "no F# member-reading helper"

              match Gen.typescriptModule plain kinds with
              | Ok s -> Expect.isFalse (s.Contains "dNonEmpty") "no TypeScript view helper"
              | Error e -> failtestf "refused: %s" (CodegenError.describe e)

          testCase "the generated TypeScript codec writes [] and undefined alike and reads [] as absent (under node)"
          <| fun _ ->
              let ts =
                  match Gen.typescriptModule idl kinds with
                  | Ok s -> s
                  | Error e -> failtestf "the TypeScript codec refused the declaration: %s" (CodegenError.describe e)

              let js (s: string) = Text.Json.JsonSerializer.Serialize s

              let harness =
                  ts
                  + "\n\nconst __docs = ["
                  + String.concat ", " (List.map js [ emptyBytes; absentBytes; fullBytes ])
                  + "];\n"
                  + "for (const s of __docs) { const r = decodeNode(s); console.log(r.ok ? encodeNode(r.value) : 'ERR ' + JSON.stringify(r.error)); }\n"
                  + "const __empty = (o) => { for (const k of Object.keys(o)) { const v = o[k]; if (Array.isArray(v)) o[k] = []; else if (v !== null && typeof v === 'object') __empty(v); } return o; };\n"
                  + "console.log(encodeNode(__empty(decodeNode("
                  + js fullBytes
                  + ").value)));\n"

              let tmp =
                  Path.Combine(Path.GetTempPath(), sprintf "fuaran-660-ts-%s.mjs" (Guid.NewGuid().ToString("N")))

              File.WriteAllText(tmp, harness)

              try
                  match ChildProcess.run (ChildProcess.command "node" ("\"" + tmp + "\"")) with
                  | Error _ -> skiptest "node not on PATH - the executed TS leg is skipped"
                  | Ok p ->
                      Expect.equal p.Exit 0 (sprintf "node ran the generated module: %s" p.Stderr)

                      let lines =
                          p.Stdout.Replace("\r\n", "\n").Split('\n')
                          |> Array.filter (fun l -> l <> "")
                          |> List.ofArray

                      Expect.equal
                          lines
                          [ absentBytes; absentBytes; fullBytes; absentBytes ]
                          "the TypeScript host agrees with the interpreter"
              finally
                  try
                      File.Delete tmp
                  with _ ->
                      ()

          testCase "the TypeScript codec still refuses every presence condition it does not apply"
          <| fun _ ->
              match Gen.typescriptModule CanonicalFormVectorExport.idl [ "Grid" ] with
              | Error(CodegenError.UnsupportedConstruct(construct, _, _)) ->
                  Expect.stringContains construct "presence condition" "named"
              | other -> failtestf "the TypeScript codec admitted a sibling condition: %A" (Result.map ignore other)

          testCase "the JSON Schema states what the encoder writes: the member optional, an empty array readable"
          <| fun _ ->
              Expect.equal (Gen.jsonSchema idl) (Gen.jsonSchema plain) "the declaration moves no schema"

              match Gen.jsonSchema idl with
              | Ok s -> Expect.isFalse (s.Contains "minItems") "an empty array is still accepted on read"
              | Error e -> failtestf "refused: %s" (CodegenError.describe e)

          testCase "the artifact carries the declaration and reads it back"
          <| fun _ ->
              let text = Artifact.render idl
              Expect.stringContains text "\"whenNonEmpty\"" "the presence member"
              Expect.equal (Artifact.parse text) (Ok(Artifact.canonicalise idl)) "the round trip"
              Expect.isFalse ((Artifact.render plain).Contains "\"presence\"") "the plain twin carries none"

          testCase "the sampler never draws Some [] for a declared member, and every draw round-trips"
          <| fun _ ->
              match Sample.trySampleNodes idl kinds 660 200 with
              | Error f -> failtestf "the fixture did not sample: %A" f
              | Ok samples ->
                  for s in samples do
                      let bytes = enc idl s
                      Expect.isFalse (bytes.Contains "[]") "no empty list is ever written"

                      Expect.equal
                          (Decode.decode idl bytes |> Result.bind (Encode.encode idl))
                          (Ok bytes)
                          "and round-trips"

                      match s with
                      | VNode(_, _, fields) ->
                          Expect.isFalse
                              (fields |> List.exists (fun (k, v) -> k = "tags" && v = VList []))
                              "Some [] is drawn as absent"
                      | _ -> ()

          testCase "the validator admits it only on an Optional list, and refuses it elsewhere at its path"
          <| fun _ ->
              refusedAtPresence (withTags (fun f -> { f with Opt = Required }))
              refusedAtPresence (withTags (fun f -> { f with Opt = OmitDefault(VList []) }))
              refusedAtPresence (withTags (fun f -> { f with Type = TStr }))
              refusedAtPresence (withTags (fun f -> { f with Type = TMap TStr }))

          testCase "declaring it on an existing member is breaking on the wire; the F* model refuses it by name"
          <| fun _ ->
              let severities (before: Idl) (after: Idl) =
                  match Diff.parse (Artifact.render before) None, Diff.parse (Artifact.render after) None with
                  | Ok b, Ok a -> Diff.changes b a |> List.map (fun c -> (Diff.classify c).Severity)
                  | Error e, _
                  | _, Error e -> failtestf "the artifact did not read back: %s" e

              Expect.isTrue
                  (severities plain idl |> List.forall ((=) BreakingWire))
                  "a Some [] document re-encodes differently"

              Expect.isNonEmpty (severities plain idl) "the change is reported"

              match FStarTarget.vocabularyModule "M" idl kinds with
              | Error e -> Expect.stringContains (CodegenError.describe e) "canonical-form" "named as one"
              | Ok _ -> failtest "the F* model admitted the declaration" ]
