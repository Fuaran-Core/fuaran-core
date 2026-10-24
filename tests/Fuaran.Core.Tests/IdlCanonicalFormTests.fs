module Fuaran.Core.Tests.IdlCanonicalFormTests

// ============================================================================
//  Phase 539 (DECISIONS.md D190) — the three canonical-form declarations an IDL
//  carries as data: a member's aliases (and an enum case's), a member's presence
//  condition, and an empty-map or JSON default. Held here over the fixture domain
//  `CanonicalFormVectorExport.idl`: the interpreter's reading (which the law file
//  exports), the generated F# codec's (compiled and run, which must equal it), the
//  sampler's, the validator's refusals, the artifact's round trip, the stability
//  classifier's verdicts, and the backends that refuse a declaration by name.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

let private idl = CanonicalFormVectorExport.idl
let private kinds = [ "Grid"; "Select"; "Notify" ]

let private kindOf (tag: string) =
    idl.Kinds |> List.find (fun k -> k.Tag = tag)

/// The fixture with one field of one kind replaced.
let private withField (tag: string) (name: string) (edit: IdlField -> IdlField) : Idl =
    { idl with
        Kinds =
            idl.Kinds
            |> List.map (fun k ->
                if k.Tag = tag then
                    { k with
                        Fields = k.Fields |> List.map (fun f -> if f.Name = name then edit f else f) }
                else
                    k) }

let private faults (v: Idl) : string list =
    Declare.errors v |> List.map VocabularyFault.describe

let private refusedWith (phrase: string) (v: Idl) =
    let fs = faults v

    Expect.isTrue
        (fs |> List.exists (fun f -> f.Contains phrase))
        (sprintf "expected a fault containing %A, got %A" phrase fs)

let private severities (before: Idl) (after: Idl) : Severity list =
    match Diff.parse (Artifact.render before) None, Diff.parse (Artifact.render after) None with
    | Ok b, Ok a -> Diff.changes b a |> List.map (fun c -> (Diff.classify c).Severity)
    | Error e, _
    | _, Error e -> failtestf "the artifact did not read back: %s" e

let private node (tag: string) (fields: (string * IdlValue) list) = VNode("n", tag, fields)

[<Tests>]
let tests =
    testList
        "Idl.CanonicalForm (Phase 539)"
        [ testCase "the fixture vocabulary is well-formed"
          <| fun _ -> Expect.isEmpty (faults idl) "no fault"

          testCase "an enum reads a canonical wire string or an alias to its case, and nothing else"
          <| fun _ ->
              let tone = idl.Enums.Head
              Expect.equal (tone.CaseAccepting "Danger") (Some "Critical") "an alias"
              Expect.equal (tone.CaseAccepting "Critical") (Some "Critical") "the canonical string"
              Expect.equal (tone.CaseAccepting "Amber") None "an unknown spelling"
              Expect.equal (tone.AliasesOf "Critical") [ "Danger"; "Negative" ] "the aliases, in priority order"
              Expect.equal (tone.AliasesOf "Default") [] "a case declaring none"

          testCase "every expected document is canonical: it reads back to itself"
          <| fun _ ->
              for id, _, body in CanonicalFormVectorExport.inputs do
                  match CanonicalFormVectorExport.canonical (CanonicalFormVectorExport.document body) with
                  | Error e -> failtestf "%s did not read: %s" id e
                  | Ok bytes ->
                      Expect.equal (CanonicalFormVectorExport.canonical bytes) (Ok bytes) (id + " is a fixed point")

          testCase "the encoder omits a member whose presence condition fails, whatever it holds"
          <| fun _ ->
              let encoded fields =
                  Encode.encode idl (node "Grid" fields) |> Result.defaultWith failtest

              Expect.equal
                  (encoded [ "cols", VInt 3; "template", VStr "t" ])
                  """{"id":"n","kind":{"$type":"Grid","template":"t"}}"""
                  "cols is meaningless under a template"

              Expect.equal
                  (encoded [ "cols", VInt 3 ])
                  """{"id":"n","kind":{"$type":"Grid","cols":3}}"""
                  "and kept without one"

              Expect.equal
                  (Encode.encode
                      idl
                      (node "Select" [ "multiple", VBool true; "value", VStr "x"; "values", VList [ VStr "y" ] ]))
                  (Ok """{"id":"n","kind":{"$type":"Select","multiple":true,"values":["y"]}}""")
                  "an equality test reads the sibling's value"

          // Phase 568 — the planted defect: a decoder that reads a member whose presence condition fails
          // instead of dropping it, leaving the drop to the encoder. It is the reference's decoder over the
          // fixture with every presence condition removed, then the reference's encoder over the fixture,
          // and it must fail each vector carrying an ill-typed value under a failed condition.
          testCase
              "go-red: a decoder that reads a member whose presence condition fails fails the canonical-form vectors"
          <| fun _ ->
              let unconditioned =
                  { idl with
                      Kinds =
                          idl.Kinds
                          |> List.map (fun k ->
                              { k with
                                  Fields = k.Fields |> List.map (fun f -> { f with Presence = None }) }) }

              let host (doc: string) =
                  Decode.decode unconditioned doc |> Result.bind (Encode.encode idl)

              let committed =
                  System.IO.File.ReadAllText(
                      System.IO.Path.Combine(OwnedConformance.root (), "laws", CanonicalFormVectorExport.fileName)
                  )

              let member' (m: (string * JVal) list) (k: string) =
                  match m |> List.tryFind (fun (n, _) -> n = k) with
                  | Some(_, JStr x) -> x
                  | _ -> failtestf "a vector carries no string `%s`" k

              let caught =
                  match Json.parse committed with
                  | Ok(JObj ms) ->
                      match ms |> List.tryFind (fun (k, _) -> k = "vectors") with
                      | Some(_, JArr vs) ->
                          vs
                          |> List.choose (function
                              | JObj m when host (member' m "document") <> Ok(member' m "expected") ->
                                  Some(member' m "id")
                              | JObj _ -> None
                              | _ -> failtest "a vector is an object")
                      | _ -> failtest "the file carries no vectors"
                  | _ -> failtest "the committed file does not read"

              printfn "a decoder that reads a failed-condition member is caught by: %A" caught

              for pinned in
                  [ "presence-fails-unread"
                    "presence-equals-fails-unread"
                    "presence-equals-default-fails-unread" ] do
                  Expect.contains caught pinned (sprintf "%s pins the decoder's drop" pinned)

          testCase "an alias decodes to the canonical member, and a refusal inside it is at the canonical spelling"
          <| fun _ ->
              match
                  Decode.decodeDetailed
                      idl
                      (CanonicalFormVectorExport.document """{"$type":"Select","source":"not a list"}""")
              with
              | Ok v -> failtestf "a malformed aliased member decoded: %A" v
              | Error e ->
                  Expect.equal
                      e.Path
                      [ PathSegment.Key "kind"; PathSegment.Key "options" ]
                      "the path names the member, not the spelling"

          testCase "the generated F# codec reads every vector to the reference's bytes, and omits as the encoder does"
          <| fun _ ->
              let moduleText =
                  match Gen.fsharpModule "CanonicalForm" idl kinds with
                  | Ok s -> s
                  | Error e -> failtestf "the F# backend refused the fixture: %s" (CodegenError.describe e)

              let lit (s: string) = SourceLit.fsString s

              let reads =
                  CanonicalFormVectorExport.inputs
                  |> List.map (fun (_, _, body) ->
                      sprintf
                          "printfn \"%%s\" (match decodeNode %s with | Ok n -> encodeNode n | Error e -> \"ERR \" + DecodeError.describe e)"
                          (lit (CanonicalFormVectorExport.document body)))

              let encodes =
                  [ "printfn \"%s\" (encodeNode { Id = \"n\"; Kind = NodeKind.Grid { Cols = Some 3; Template = Some \"t\" } })"
                    "printfn \"%s\" (encodeNode { Id = \"n\"; Kind = NodeKind.Select { Multiple = true; Value = Some \"x\"; Values = Some [ \"y\" ]; Options = [ \"o\" ] } })"
                    "printfn \"%s\" (encodeNode { Id = \"n\"; Kind = NodeKind.Notify { Args = Map.empty; Payload = JObj []; Heading = None; Tone = Tone.Default } })" ]

              let expected =
                  (CanonicalFormVectorExport.inputs
                   |> List.map (fun (id, _, body) ->
                       match CanonicalFormVectorExport.canonical (CanonicalFormVectorExport.document body) with
                       | Ok b -> b
                       | Error e -> failtestf "%s: %s" id e))
                  @ [ """{"id":"n","kind":{"$type":"Grid","template":"t"}}"""
                      """{"id":"n","kind":{"$type":"Select","multiple":true,"options":["o"],"values":["y"]}}"""
                      """{"id":"n","kind":{"$type":"Notify"}}""" ]

              let script =
                  IdlDeriveTests.Projected.scriptOf "CanonicalForm" moduleText (String.concat "\n" (reads @ encodes))

              match IdlDeriveTests.Projected.runFsiWith "--checknulls+ --warnaserror+ " script with
              | None -> skiptest "dotnet not on PATH - the generated-codec run is skipped"
              | Some(code, out, err) ->
                  Expect.equal code 0 (sprintf "the generated module compiles and runs: %s" err)

                  let lines =
                      out.Replace("\r\n", "\n").Split('\n')
                      |> Array.filter (fun l -> l <> "")
                      |> List.ofArray

                  Expect.equal lines expected "the generated codec agrees with the interpreter, line for line"

          testCase "the sampler draws canonical values: a meaningless member is never drawn present"
          <| fun _ ->
              match Sample.trySampleNodes idl kinds 539 120 with
              | Error f -> failtestf "the fixture did not sample: %A" f
              | Ok samples ->
                  for s in samples do
                      let bytes = Encode.encode idl s |> Result.defaultWith failtest
                      Expect.equal (CanonicalFormVectorExport.canonical bytes) (Ok bytes) "a sample encodes canonically"

                      Expect.equal
                          (Decode.decode idl bytes |> Result.bind (Encode.encode idl))
                          (Ok bytes)
                          "and round-trips"

                      match s with
                      | VNode(_, "Grid", fields) ->
                          let present n =
                              fields |> List.exists (fun (k, v) -> k = n && v <> VAbsent)

                          Expect.isFalse (present "cols" && present "template") "cols is drawn absent under a template"
                      | _ -> ()

          testCase "the artifact carries the declarations and reads them back"
          <| fun _ ->
              let text = Artifact.render idl
              Expect.stringContains text "\"aliases\"" "the aliases member"
              Expect.stringContains text "\"whenEquals\"" "the presence member"
              Expect.equal (Artifact.parse text) (Ok(Artifact.canonicalise idl)) "the round trip is the canonical model"

          testCase "a vocabulary that declares none of them renders and generates exactly as before"
          <| fun _ ->
              let plain =
                  { idl with
                      Kinds =
                          idl.Kinds
                          |> List.map (fun k ->
                              { k with
                                  Fields = k.Fields |> List.map (fun f -> { f with Aliases = []; Presence = None }) })
                      Enums = idl.Enums |> List.map (fun e -> { e with Aliases = [] }) }

              Expect.isFalse ((Artifact.render plain).Contains "\"aliases\"") "no aliases member"
              Expect.isFalse ((Artifact.render plain).Contains "\"presence\"") "no presence member"

              match Gen.fsharpModule "Plain" plain kinds with
              | Ok s -> Expect.isFalse (s.Contains "dMember") "no member-reading helper"
              | Error e -> failtestf "refused: %s" (CodegenError.describe e)

          testCase "the TypeScript codec and the F* model refuse the declarations by name"
          <| fun _ ->
              match Gen.typescriptModule idl kinds with
              | Error(CodegenError.UnsupportedConstruct(construct, _, _)) ->
                  Expect.stringContains construct "field '" "the refusal names the declaration"
              | other -> failtestf "the TypeScript codec did not refuse: %A" (Result.map ignore other)

              match FStarTarget.vocabularyModule "M" idl kinds with
              | Error e -> Expect.stringContains (CodegenError.describe e) "canonical-form" "named as one"
              | Ok _ -> failtest "the F* model admitted a canonical-form declaration"

          testCase "the validator refuses an ill-formed declaration at its path, by name"
          <| fun _ ->
              refusedWith
                  "needs an Optional or OmitDefault member"
                  (withField "Grid" "template" (fun f ->
                      { f with
                          Opt = Required
                          Presence = Some(WhenPresent "cols") }))

              refusedWith
                  "which is not a sibling"
                  (withField "Grid" "cols" (fun f ->
                      { f with
                          Presence = Some(WhenAbsent "nope") }))

              refusedWith
                  "cannot name its own member"
                  (withField "Grid" "cols" (fun f ->
                      { f with
                          Presence = Some(WhenAbsent "cols") }))

              refusedWith
                  "which is itself conditional"
                  (withField "Select" "values" (fun f ->
                      { f with
                          Presence = Some(WhenPresent "value") }))

              refusedWith
                  "needs an Optional sibling"
                  (withField "Select" "value" (fun f ->
                      { f with
                          Presence = Some(WhenPresent "multiple") }))

              refusedWith
                  "not a canonical scalar"
                  (withField "Select" "value" (fun f ->
                      { f with
                          Presence = Some(WhenEquals("multiple", VList [])) }))

              refusedWith
                  "another spelling of a member"
                  (withField "Select" "options" (fun f -> { f with Aliases = [ "value" ] }))

              refusedWith
                  "is not an identifier"
                  (withField "Select" "options" (fun f -> { f with Aliases = [ "a-b" ] }))

              let enumWith aliases =
                  { idl with
                      Enums = idl.Enums |> List.map (fun e -> { e with Aliases = aliases }) }

              refusedWith "is one of the enum's wire strings" (enumWith [ "Success", [ "Critical" ] ])
              refusedWith "which the enum does not declare" (enumWith [ "Warning", [ "Amber" ] ])

          testCase "the stability classifier grades each change by what an old document does"
          <| fun _ ->
              let aliases a =
                  withField "Select" "options" (fun f -> { f with Aliases = a })

              Expect.equal
                  (severities (aliases [ "source" ]) (aliases [ "source"; "data" ]))
                  [ Additive ]
                  "an alias added"

              Expect.equal (severities (aliases [ "source" ]) (aliases [])) [ BreakingWire ] "an alias withdrawn"

              Expect.equal
                  (severities idl (withField "Grid" "cols" (fun f -> { f with Presence = None })))
                  [ BreakingWire ]
                  "a presence condition withdrawn"

              Expect.equal
                  (severities
                      { idl with
                          Enums = idl.Enums |> List.map (fun e -> { e with Aliases = [] }) }
                      idl)
                  [ Additive; Additive ]
                  "enum case aliases declared" ]
