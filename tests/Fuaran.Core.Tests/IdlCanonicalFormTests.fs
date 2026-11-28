module Fuaran.Core.Tests.IdlCanonicalFormTests

// ============================================================================
//  Phase 539 (DECISIONS.md D190) — the three canonical-form declarations an IDL
//  carries as data: a member's aliases (and an enum case's), a member's presence
//  condition, and an empty-map or JSON default. Held here over the fixture domain
//  `CanonicalFormVectorExport.idl`: the interpreter's reading (which the law file
//  exports), the generated F# codec's (compiled and run, which must equal it), the
//  sampler's, the validator's refusals, the artifact's round trip, the stability
//  classifier's verdicts, the generated TypeScript codec's (run under node, which
//  must equal it too, Phase 723), and the F* model, which carries them (Phase
//  723, D290) and refuses by name each shape it does not.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

let private idl = CanonicalFormVectorExport.idl
let private kinds = [ "Grid"; "Select"; "Notify"; "Place" ]

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

/// Phase 723 (DECISIONS.md D288) — each refusal vector's code and path below the node's `kind`, stated
/// here rather than read from the reference, so a reference that moved a path is caught: every segment
/// names a member as the document writes it, or an index.
let private asWritten: (string * (DecodeCode * PathSegment list)) list =
    let k = PathSegment.Key

    [ "refusal-alias-member", (DecodeCode.WrongKind, [ k "source" ])
      "refusal-alias-list-element", (DecodeCode.WrongKind, [ k "suggestions"; PathSegment.Index 1 ])
      "refusal-alias-both-spellings", (DecodeCode.WrongKind, [ k "options" ])
      "refusal-alias-nested-record", (DecodeCode.WrongKind, [ k "position"; k "left" ])
      "refusal-alias-union-case-field", (DecodeCode.WrongKind, [ k "mark"; k "text" ])
      "refusal-alias-union-case-record", (DecodeCode.WrongKind, [ k "mark"; k "where"; k "left" ])
      "refusal-alias-missing-at-canonical", (DecodeCode.MissingField, [ k "position"; k "x" ]) ]

/// The strict reading's undeclared member beneath a member read under an alias, and its path below
/// `kind`: at the alias as written, then the undeclared key.
let private strictAsWritten: (string * PathSegment list) list =
    let k = PathSegment.Key

    [ """{"$type":"Place","position":{"left":1,"y":2,"z":3}}""", [ k "position"; k "z" ]
      """{"$type":"Place","mark":{"$type":"Pin","text":"a","where":{"x":1,"y":2,"q":0}}}""",
      [ k "mark"; k "where"; k "q" ] ]

/// Run a script under node: its non-empty stdout lines, or `None` when node is not on PATH.
let private nodeLines (script: string) : string list option =
    let tmp =
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            sprintf "fuaran-723-ts-%s.mjs" (System.Guid.NewGuid().ToString "N")
        )

    System.IO.File.WriteAllText(tmp, script)

    try
        match ChildProcess.run (ChildProcess.command "node" ("\"" + tmp + "\"")) with
        | Error _ -> None
        | Ok p ->
            Expect.equal p.Exit 0 (sprintf "node ran the generated module: %s" p.Stderr)

            p.Stdout.Replace("\r\n", "\n").Split('\n')
            |> Array.filter (fun l -> l <> "")
            |> List.ofArray
            |> Some
    finally
        try
            System.IO.File.Delete tmp
        with _ ->
            ()

/// Phase 723 — a refusal as the TypeScript host prints it below: `[code, path]`, the path as
/// `DecodePath.toJson` writes it.
let private refusalLine (code: string) (path: PathSegment list) : string =
    Canonical.write Rendering.CrossHost (JArr [ JStr code; DecodePath.toJson path ])

/// Phase 723 — authored values whose canonical form omits a member its condition makes meaningless, or
/// writes a member read under an alias at its canonical spelling.
let private authored: IdlValue list =
    [ node "Grid" [ "cols", VInt 3; "template", VStr "t" ]
      node "Grid" [ "cols", VInt 3 ]
      node
          "Select"
          [ "multiple", VBool true
            "value", VStr "x"
            "values", VList [ VStr "y" ]
            "options", VList [ VStr "o" ] ]
      node "Select" [ "value", VStr "x"; "values", VList [ VStr "y" ] ]
      node "Notify" [ "args", VMap []; "payload", VJson(JObj []); "tone", VEnum "Default" ]
      node "Notify" [ "heading", VStr "h"; "tone", VEnum "Critical" ]
      node
          "Place"
          [ "at", VRecord [ "x", VInt 1; "y", VInt 2 ]
            "mark", VUnion("Pin", [ "label", VStr "a" ]) ] ]

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
                          // A refusal vector (Phase 723) pins where a fault is located, not what a
                          // decoder drops; the planted defect is read against the bytes the others expect.
                          vs
                          |> List.choose (function
                              | JObj m when List.exists (fun (k, _) -> k = "refusal") m -> None
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

          // Phase 723 (DECISIONS.md D288, amending D190.1) — a decode error's path names the document AS
          // WRITTEN: a refusal inside a member read under an alias is at the alias, in both readings.
          testCase "a refusal inside a member read under an alias is located at the alias as written"
          <| fun _ ->
              let refusalOf (doc: string) =
                  CanonicalFormVectorExport.refusalInputs
                  |> List.find (fun (id, _, _) -> id = doc)
                  |> fun (_, _, body) -> CanonicalFormVectorExport.document body

              for id, (code, path) in asWritten do
                  for reading, read in
                      [ "lenient", Decode.decodeDetailed idl
                        "strict", Decode.decodeStrictDetailed idl ] do
                      match read (refusalOf id) with
                      | Ok v -> failtestf "%s (%s) decoded: %A" id reading v
                      | Error e ->
                          Expect.equal (e.Code, e.Path) (code, PathSegment.Key "kind" :: path) (id + ", " + reading)

              // The strict reading's own refusal under an alias, at any depth beneath it.
              for body, path in strictAsWritten do
                  match Decode.decodeStrictDetailed idl (CanonicalFormVectorExport.document body) with
                  | Ok v -> failtestf "%s decoded strictly: %A" body v
                  | Error e ->
                      Expect.equal
                          (e.Code, e.Path)
                          (DecodeCode.UndeclaredMember, PathSegment.Key "kind" :: path)
                          ("strict " + body)

          testCase "every refusal vector is authored with the path this suite pins"
          <| fun _ ->
              Expect.equal
                  (CanonicalFormVectorExport.refusalInputs |> List.map (fun (id, _, _) -> id))
                  (asWritten |> List.map fst)
                  "one pinned path per refusal vector, in the file's order"

          testCase "the generated F# codec reads every vector to the reference's bytes, and omits as the encoder does"
          <| fun _ ->
              // Phase 723 — with the collecting decoder beside the short-circuiting one, so both readers
              // locate a refusal under an alias as the interpreter does.
              let moduleText =
                  match
                      Gen.fsharpModuleDerived GenSupport.Empty [ Derivation.SpecDecoders ] "CanonicalForm" idl kinds
                  with
                  | Ok s -> s
                  | Error e -> failtestf "the F# backend refused the fixture: %s" (CodegenError.describe e)

              let lit (s: string) = SourceLit.fsString s

              let reads =
                  CanonicalFormVectorExport.inputs
                  |> List.map (fun (_, _, body) ->
                      sprintf
                          "printfn \"%%s\" (match decodeNode %s with | Ok n -> encodeNode n | Error e -> \"ERR \" + DecodeError.describe e)"
                          (lit (CanonicalFormVectorExport.document body)))

              let at =
                  "(fun (e: DecodeError) -> DecodeError.codeName e.Code + \" \" + DecodePath.render e.Path)"

              let refusals =
                  CanonicalFormVectorExport.refusalInputs
                  |> List.collect (fun (_, _, body) ->
                      let doc = lit (CanonicalFormVectorExport.document body)

                      [ sprintf
                            "printfn \"%%s\" (match decodeNode %s with | Ok n -> encodeNode n | Error e -> %s e)"
                            doc
                            at
                        sprintf
                            "printfn \"%%s\" (match decodeNodeAll %s with | Ok n -> encodeNode n | Error es -> es |> List.map %s |> String.concat \"; \")"
                            doc
                            at ])

              let refused =
                  CanonicalFormVectorExport.refusalInputs
                  |> List.collect (fun (id, _, body) ->
                      match CanonicalFormVectorExport.refusal (CanonicalFormVectorExport.document body) with
                      | Some(code, path) ->
                          let line = code + " " + DecodePath.render path
                          [ line; line ]
                      | None -> failtestf "%s decoded" id)

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
                  @ refused

              let script =
                  IdlDeriveTests.Projected.scriptOf
                      "CanonicalForm"
                      moduleText
                      (String.concat "\n" (reads @ encodes @ refusals))

              match IdlDeriveTests.Projected.runFsiWith "--checknulls+ --warnaserror+ " script with
              | None -> skiptest "dotnet not on PATH - the generated-codec run is skipped"
              | Some(code, out, err) ->
                  Expect.equal code 0 (sprintf "the generated module compiles and runs: %s" err)

                  let lines =
                      out.Replace("\r\n", "\n").Split('\n')
                      |> Array.filter (fun l -> l <> "")
                      |> List.ofArray

                  Expect.equal lines expected "the generated codec agrees with the interpreter, line for line"

          // Phase 723 (DECISIONS.md D288) — the codec-only module's lenient and STRICT readers locate a
          // refusal under an alias as the interpreter's two readings do, at the alias as written.
          testCase "the codec-only readers, lenient and strict, locate a refusal under an alias as the interpreter does"
          <| fun _ ->
              let request: CodecOnly =
                  { Roots = [ "Point"; "Mark" ]
                    HostTypes = Map.ofList [ "Point", "Host.Point"; "Mark", "Host.Mark" ] }

              let codecText =
                  match Gen.fsharpCodecModule GenSupport.Empty request "AliasCodec" idl with
                  | Ok s -> s
                  | Error e -> failtestf "the codec-only module refused the fixture: %s" (CodegenError.describe e)

              let host =
                  "module Host =\n    type Point = { X: int; Y: int }\n\n    type Mark =\n        | Pin of label: string * at: Point option\n        | Dot\n\n"

              let rows =
                  [ "Point", """{"left":"one","y":2}"""
                    "Point", """{"y":2}"""
                    "Point", """{"x":1,"left":"bad","y":2}"""
                    "Point", """{"left":1,"y":2,"z":0}"""
                    "Mark", """{"$type":"Pin","text":5}"""
                    "Mark", """{"$type":"Pin","label":"a","where":{"left":true,"y":2}}"""
                    "Mark", """{"$type":"Pin","label":"a","where":{"x":1,"y":2,"q":0}}""" ]

              let shown (r: Result<'a, DecodeError>) =
                  match r with
                  | Ok _ -> "ok"
                  | Error e -> DecodeError.codeName e.Code + " " + DecodePath.render e.Path

              let reference =
                  rows
                  |> List.map (fun (root, doc) ->
                      let t =
                          if root = "Mark" then
                              TUnion(root, [])
                          else
                              TRecord(root, [])

                      let j = Json.parse doc |> Result.defaultWith failtest

                      shown (Decode.valueDetailed idl t j)
                      + "\t"
                      + shown (Decode.valueStrictDetailed idl t j))

              Expect.equal
                  (reference |> List.map (fun l -> l.Split('\t')[1]))
                  [ "WrongKind $[\"left\"]"
                    "MissingField $[\"x\"]"
                    "ok"
                    "UndeclaredMember $[\"z\"]"
                    "WrongKind $[\"text\"]"
                    "WrongKind $[\"where\"][\"left\"]"
                    "UndeclaredMember $[\"where\"][\"q\"]" ]
                  "the interpreter's strict reading, at the alias as written"

              let probe =
                  rows
                  |> List.map (fun (root, doc) ->
                      sprintf "probe decode%s decode%sStrict %s" root root (SourceLit.fsString doc))

              let body =
                  "let probe (lenient: JVal -> Result<'a, DecodeError>) (strict: JVal -> Result<'b, DecodeError>) (doc: string) =\n"
                  + "    let j = match Json.parse doc with Ok j -> j | Error m -> failwith m\n"
                  + "    let shown r = match r with Ok _ -> \"ok\" | Error (e: DecodeError) -> DecodeError.codeName e.Code + \" \" + DecodePath.render e.Path\n"
                  + "    printfn \"%s\\t%s\" (shown (lenient j)) (shown (strict j))\n\n"
                  + String.concat "\n" probe

              let script = IdlDeriveTests.Projected.scriptOf "AliasCodec" (host + codecText) body

              match IdlDeriveTests.Projected.runFsiWith "--checknulls+ --warnaserror+ " script with
              | None -> skiptest "dotnet not on PATH - the generated-codec run is skipped"
              | Some(code, out, err) ->
                  Expect.equal code 0 (sprintf "the codec-only module compiles and runs: %s" err)

                  let lines =
                      out.Replace("\r\n", "\n").Split('\n')
                      |> Array.filter (fun l -> l <> "")
                      |> List.ofArray

                  Expect.equal lines reference "the generated readers agree with the interpreter, line for line"

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
              let bare (fs: IdlField list) =
                  fs |> List.map (fun f -> { f with Aliases = []; Presence = None })

              let plain =
                  { idl with
                      Kinds = idl.Kinds |> List.map (fun k -> { k with Fields = bare k.Fields })
                      Records = idl.Records |> List.map (fun r -> { r with Fields = bare r.Fields })
                      Unions =
                          idl.Unions
                          |> List.map (fun u ->
                              { u with
                                  Cases = u.Cases |> List.map (fun c -> { c with Fields = bare c.Fields }) })
                      Enums = idl.Enums |> List.map (fun e -> { e with Aliases = [] }) }

              Expect.isFalse ((Artifact.render plain).Contains "\"aliases\"") "no aliases member"
              Expect.isFalse ((Artifact.render plain).Contains "\"presence\"") "no presence member"

              match Gen.fsharpModule "Plain" plain kinds with
              | Ok s -> Expect.isFalse (s.Contains "dMember") "no member-reading helper"
              | Error e -> failtestf "refused: %s" (CodegenError.describe e)

              // Phase 723 — nor in the TypeScript module (every byte of it is pinned in
              // `IdlCanonicalFormEmissionTests`, over the fixture library).
              match Gen.typescriptModule plain kinds with
              | Ok s -> Expect.isFalse (s.Contains "dSpelling") "no TypeScript member-reading helper"
              | Error e -> failtestf "refused: %s" (CodegenError.describe e)

          // Phase 723 (DECISIONS.md D190, D288) — the TypeScript host applies every declaration, defect for
          // defect with the interpreter (and so with the F# host above): each vector decodes to the
          // reference's bytes, each refusal vector is refused with the reference's code at the path as
          // written, by the short-circuiting and the collecting decoder alike, and the encoder omits a
          // member whose condition fails and writes an aliased one at its canonical spelling.
          testCase
              "the generated TypeScript codec reads every vector as the reference does, and omits as the encoder does (under node)"
          <| fun _ ->
              let moduleText =
                  match Gen.typescriptModuleDerived [ Derivation.SpecDecoders ] idl kinds with
                  | Ok s -> s
                  | Error e -> failtestf "the TypeScript backend refused the fixture: %s" (CodegenError.describe e)

              let js (s: string) =
                  System.Text.Json.JsonSerializer.Serialize s

              let value (v: IdlValue) =
                  match Gen.typescriptValue idl TNode v with
                  | Ok src -> src
                  | Error e -> failtestf "the TypeScript value emitter refused %A: %s" v (CodegenError.describe e)

              let documents =
                  (CanonicalFormVectorExport.inputs @ CanonicalFormVectorExport.refusalInputs)
                  |> List.map (fun (_, _, body) -> js (CanonicalFormVectorExport.document body))

              let harness =
                  moduleText
                  + "\n\nconst __refusal = (e) => JSON.stringify([e.code, e.path]);\n"
                  + "for (const s of ["
                  + String.concat ", " documents
                  + "]) {\n"
                  + "  const r = decodeNode(s);\n"
                  + "  console.log(r.ok ? encodeNode(r.value) : __refusal(r.error));\n"
                  + "  const all = decodeNodeAll(s);\n"
                  + "  console.log(all.ok ? encodeNode(all.value) : all.errors.map(__refusal).join('; '));\n"
                  + "}\n"
                  + "for (const v of ["
                  + String.concat ", " (authored |> List.map value)
                  + "]) console.log(encodeNode(v));\n"

              let expected =
                  (CanonicalFormVectorExport.inputs
                   |> List.collect (fun (id, _, body) ->
                       match CanonicalFormVectorExport.canonical (CanonicalFormVectorExport.document body) with
                       | Ok b -> [ b; b ]
                       | Error e -> failtestf "%s: %s" id e))
                  @ (CanonicalFormVectorExport.refusalInputs
                     |> List.collect (fun (id, _, body) ->
                         match CanonicalFormVectorExport.refusal (CanonicalFormVectorExport.document body) with
                         | Some(code, path) -> [ refusalLine code path; refusalLine code path ]
                         | None -> failtestf "%s decoded" id))
                  @ (authored |> List.map (Encode.encode idl >> Result.defaultWith failtest))

              match nodeLines harness with
              | None -> skiptest "node not on PATH - the executed TypeScript leg is skipped"
              | Some lines ->
                  Expect.equal lines expected "the TypeScript host agrees with the interpreter, line for line"

          testCase
              "with a TypeScript compiler, the canonical-form vocabularies' derived modules and declarations compile"
          <| fun _ ->
              match IdlDeriveTests.tscJs () with
              | None -> skiptest "FUARAN_CORE_TSC names no TypeScript compiler - the type-check leg cannot run"
              | Some tsc ->
                  IdlDeriveTests.derivedDeclarationsCompile tsc "canonical-form laws" idl
                  IdlDeriveTests.derivedDeclarationsCompile tsc "canonical-form hosts" CanonicalFormHostsIdl.idl

          // Phase 723 — the four presence conditions and the aliases they read, one document per rule,
          // through the interpreter, the compiled F# host and the TypeScript host: each answers the
          // interpreter's bytes or its refusal at the path as written. And the TypeScript encoder omits a
          // member exactly where the interpreter's does, under each condition. The same holds for an
          // aliased and a conditional member beside a presence-keyed union, whose generated F# module
          // takes both sets of decode helpers (it once declared `dPick` twice and did not compile).
          testCase "every presence condition reads and writes alike on the three hosts, over aliased siblings"
          <| fun _ ->
              let hosts = CanonicalFormHostsIdl.idl

              let gate (body: string) =
                  """{"id":"n","kind":{"$type":"Gate",""" + body + "}}"

              let link (body: string) =
                  """{"id":"n","kind":{"$type":"Link",""" + body + "}}"

              let documents =
                  [ // `unit` only while `count` is the int 1: a float token is not, so `unit` is dropped
                    // unread and the refusal is at `count`.
                    gate "\"measure\":5,\"count\":1.0"
                    gate "\"measure\":5,\"count\":1"
                    gate "\"measure\":\"m\",\"count\":1"
                    // `note` while `open` (spelt `isOpen`) is present, `fallback` while it is absent.
                    gate "\"isOpen\":true,\"note\":\"n\",\"backup\":7"
                    gate "\"note\":7,\"backup\":\"b\""
                    gate "\"backup\":7"
                    gate "\"open\":true,\"isOpen\":5"
                    gate "\"isOpen\":\"yes\""
                    // `tags` (spelt `labels`) only when it holds an item; one of the wrong kind is read.
                    gate "\"labels\":[]"
                    gate "\"labels\":[1]"
                    gate "\"labels\":\"x\""
                    // `alert` while `level` (spelt `severity`) is `Critical`, which `Danger` spells too.
                    gate "\"severity\":\"Danger\",\"alert\":\"a\""
                    gate "\"severity\":\"Danger\",\"alert\":3"
                    gate "\"severity\":\"Positive\",\"alert\":3"
                    gate "\"alert\":3"
                    gate "\"severity\":\"Grave\""
                    // Beside a presence-keyed union, whose case the keys present pick: `title` (spelt
                    // `heading`) and `hint` (only while `title` is present) are read as on any object.
                    link "\"heading\":\"h\",\"hint\":\"x\",\"url\":\"u\""
                    link "\"heading\":5,\"url\":\"u\""
                    link "\"hint\":\"x\",\"ref\":\"r\""
                    link "\"title\":\"t\",\"heading\":\"h\",\"ref\":5"
                    link "\"heading\":\"h\",\"url\":\"u\",\"ref\":\"r\""
                    link "\"heading\":\"h\"" ]

              let answer (r: Result<string, DecodeError>) =
                  match r with
                  | Ok bytes -> bytes
                  | Error e -> refusalLine (DecodeError.codeName e.Code) e.Path

              let interpreter =
                  documents
                  |> List.map (fun d ->
                      answer (
                          Decode.decodeDetailed hosts d
                          |> Result.map (Encode.encode hosts >> Result.defaultWith failtest)
                      ))

              let compiled =
                  documents
                  |> List.map (fun d ->
                      answer (
                          CanonicalFormGenerated.decodeNode d
                          |> Result.map CanonicalFormGenerated.encodeNode
                      ))

              Expect.equal compiled interpreter "the compiled F# host answers as the interpreter does"

              // Non-vacuity: some documents are refused under an alias, some read.
              Expect.isTrue (interpreter |> List.exists (fun l -> l.Contains "\"measure\"]")) "a refusal at an alias"
              Expect.isTrue (interpreter |> List.exists (fun l -> l.StartsWith "{")) "a document read"

              Expect.isTrue
                  (interpreter |> List.exists (fun l -> l.Contains "\"heading\"]"))
                  "a refusal at an alias beside a presence-keyed union"

              Expect.isTrue
                  (interpreter
                   |> List.exists (fun l -> l.Contains "\"url\":\"u\"" && l.Contains "\"title\":\"h\""))
                  "a document read under an alias beside a presence-keyed union's case"

              let gateValue (members: (string * IdlValue) list) = node "Gate" members

              let values =
                  [ gateValue [ "unit", VStr "u"; "count", VInt 2 ]
                    gateValue [ "unit", VStr "u"; "count", VInt 1 ]
                    gateValue [ "note", VStr "n"; "fallback", VStr "f" ]
                    gateValue [ "open", VBool false; "note", VStr "n"; "fallback", VStr "f" ]
                    gateValue [ "tags", VList [] ]
                    gateValue [ "tags", VList [ VStr "t" ] ]
                    gateValue [ "level", VEnum "Critical"; "alert", VStr "a" ]
                    gateValue [ "level", VEnum "Success"; "alert", VStr "a" ]
                    gateValue [ "alert", VStr "a" ]
                    node
                        "Link"
                        [ "title", VStr "t"
                          "hint", VStr "h"
                          "source", VUnion("ByUrl", [ "url", VStr "u" ]) ]
                    node "Link" [ "hint", VStr "h"; "source", VUnion("ByRef", [ "ref", VStr "r" ]) ] ]

              let ts =
                  match Gen.typescriptModule hosts (hosts.Kinds |> List.map _.Tag) with
                  | Ok s -> s
                  | Error e -> failtestf "the TypeScript backend refused: %s" (CodegenError.describe e)

              let literal (v: IdlValue) =
                  match Gen.typescriptValue hosts TNode v with
                  | Ok src -> src
                  | Error e -> failtestf "the TypeScript value emitter refused %A: %s" v (CodegenError.describe e)

              let js (s: string) =
                  System.Text.Json.JsonSerializer.Serialize s

              // Bytes alone cannot tell a member decoded as `[]` from one decoded as absent, since the
              // encoder writes neither: each document read is also held to the interpreter's VALUE, by
              // a key-sorted rendering that drops an absent (`undefined`) member.
              let wanted =
                  documents
                  |> List.map (fun d ->
                      match Decode.decode hosts d with
                      | Ok v -> literal v
                      | Error _ -> "null")

              let harness =
                  ts
                  + "\n\nconst __canon = (x) => JSON.stringify(x, (_, v) => v !== null && typeof v === 'object' && !Array.isArray(v) ? Object.fromEntries(Object.entries(v).filter(([, y]) => y !== undefined).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))) : v);\n"
                  + "for (const [s, want] of ["
                  + String.concat ", " (List.map2 (fun d w -> "[" + js d + ", " + w + "]") documents wanted)
                  + "]) { const r = decodeNode(s); console.log(r.ok ? encodeNode(r.value) + (__canon(r.value) === __canon(want) ? '' : ' VALUE ' + __canon(r.value)) : JSON.stringify([r.error.code, r.error.path])); }\n"
                  + "for (const v of ["
                  + String.concat ", " (List.map literal values)
                  + "]) console.log(encodeNode(v));\n"

              let written =
                  values |> List.map (Encode.encode hosts >> Result.defaultWith failtest)

              match nodeLines harness with
              | None -> skiptest "node not on PATH - the executed TypeScript leg is skipped"
              | Some lines ->
                  Expect.equal
                      lines
                      (interpreter @ written)
                      "the TypeScript host answers and writes as the interpreter does"

          // Phase 723 (DECISIONS.md D290) — the F* model carries the declarations. What it proves is the
          // proof leg's (`proofs/CanonicalFormVocabularyProofs.fst`); what is held here is that the model
          // is emitted for every kind, reads each spelling and condition, and refuses BY NAME each shape
          // no certified vocabulary holds.
          testCase "the F* model carries every declaration of the law set's vocabulary"
          <| fun _ ->
              let verdicts = FStarTarget.partition idl
              Expect.equal (verdicts |> List.map _.Tag) kinds "every kind is partitioned"

              for v in verdicts do
                  Expect.isNone v.Refusal (v.Tag + " is modelled")

              let model =
                  FStarTarget.vocabularyModule "M" idl kinds
                  |> Result.defaultWith (CodegenError.describe >> failtest)

              for wanted, what in
                  [ "(match get_prop \"options\" el with | Ok v -> Ok v | Error e -> (match get_prop \"source\" el with | Ok v -> Ok v | Error _ -> (match get_prop \"suggestions\" el with | Ok v -> Ok v | Error _ -> Error e)))",
                    "a member is read under its canonical spelling, then each alias in declared order"
                    "if s = \"Danger\" then Ok C__e_tone__Critical else", "an enum case's alias decodes to the case"
                    "template:(option (string)){(None? template || None? cols)}",
                    "a member whose condition fails is absent from the model's type (refining the sibling after it)"
                    "value:(option (string)){(multiple = false || None? value)}",
                    "an equality over a sibling omitted at its default refines the member after it"
                    "(match o0 with | Error e -> Error e | Ok sv -> if sv = false then",
                    "a member is read only when its sibling's decoded value satisfies its condition" ] do
                  Expect.stringContains model wanted what

          // Phase 723 (DECISIONS.md D291) — a refusal is the exception: each shape the model once
          // refused and now carries is pinned as modelled, and each refusal that remains by its sentence.
          testCase "the F* model carries each shape it can state and refuses the rest by name"
          <| fun _ ->
              let refusalOf (v: Idl) (tag: string) =
                  FStarTarget.partition v
                  |> List.tryFind (fun k -> k.Tag = tag)
                  |> Option.bind _.Refusal
                  |> Option.defaultValue "(modelled)"

              let plain (name: string) (t: IdlType) (opt: Optionality) : IdlField =
                  { Name = name
                    Type = t
                    Opt = opt
                    Aliases = []
                    Keyed = false
                    Presence = None
                    Annotations = Annotations.Empty }

              let aliased (names: string list) (f: IdlField) = { f with Aliases = names }

              let kindField (tag: string) (name: string) (change: IdlField -> IdlField) (v: Idl) =
                  { v with
                      Kinds =
                          v.Kinds
                          |> List.map (fun k ->
                              if k.Tag = tag then
                                  { k with
                                      Fields = k.Fields |> List.map (fun f -> if f.Name = name then change f else f) }
                              else
                                  k) }

              let recordField (record: string) (name: string) (change: IdlField -> IdlField) (v: Idl) =
                  { v with
                      Records =
                          v.Records
                          |> List.map (fun r ->
                              if r.Name = record then
                                  { r with
                                      Fields = r.Fields |> List.map (fun f -> if f.Name = name then change f else f) }
                              else
                                  r) }

              // A record whose member `t` is conditional on `s`, held at the declared default `d`.
              let spotOf (fields: IdlField list) (d: IdlValue) =
                  { idl with
                      Records =
                          idl.Records
                          @ [ { Name = "Spot"
                                Params = []
                                Fields = fields
                                Annotations = Annotations.Empty
                                Inline = false } ]
                      Kinds =
                          idl.Kinds
                          |> List.map (fun k ->
                              if k.Tag = "Place" then
                                  { k with
                                      Fields = k.Fields @ [ plain "spot" (TRecord("Spot", [])) (OmitDefault d) ] }
                              else
                                  k) }

              let spot =
                  spotOf
                      [ plain "s" TStr Optional
                        { plain "t" TStr Optional with
                            Presence = Some(WhenPresent "s") } ]

              // `t` only while `s`, omitted at its default `z`, is `a`: the literal writes `s` as the
              // member's term and the condition's term bare, so the two are compared unwrapped.
              let spotEquals =
                  spotOf
                      [ plain "s" TStr (OmitDefault(VStr "z"))
                        { plain "t" TStr Optional with
                            Presence = Some(WhenEquals("s", VStr "a")) } ]

              let spotAbsent =
                  spotOf
                      [ plain "s" TStr Optional
                        { plain "t" TStr Optional with
                            Presence = Some(WhenAbsent "s") } ]

              let spotNonEmpty =
                  spotOf
                      [ { plain "t" (TList TStr) Optional with
                            Presence = Some WhenNonEmpty } ]

              let envelope =
                  { idl with
                      NodeFields =
                          [ aliased [ "isHidden" ] (plain "hidden" TBool Optional)
                            { plain "tip" TStr Optional with
                                Presence = Some(WhenPresent "hidden") } ] }

              // A condition over a sibling that is no member of the model's, which the declaration
              // check refuses; the target is handed a vocabulary that was not checked.
              let hostOnlySibling =
                  kindField
                      "Grid"
                      "template"
                      (fun f ->
                          { f with
                              Presence = Some(WhenPresent "host") })
                      { idl with
                          Kinds =
                              idl.Kinds
                              |> List.map (fun k ->
                                  if k.Tag = "Grid" then
                                      { k with
                                          Fields = k.Fields @ [ plain "host" TStr HostOnly ] }
                                  else
                                      k) }

              // ACCEPTED (D291): the shapes the model once refused and now carries.
              for v, tag, what in
                  [ kindField "Grid" "caption" (aliased [ "title" ]) LayoutIdl.layout,
                    "Grid",
                    "an alias beside a presence-keyed union"
                    CanonicalFormHostsIdl.idl, "Link", "an alias and a condition beside a presence-keyed union"
                    envelope, "Grid", "an alias and a condition on the node envelope's members"
                    spot (VRecord []), "Place", "a default of a record declaring a condition, its member absent"
                    spot (VRecord [ "s", VStr "a"; "t", VStr "b" ]),
                    "Place",
                    "a default of a record declaring a condition, writing its member where the condition holds"
                    spotEquals (VRecord [ "s", VStr "a"; "t", VStr "b" ]),
                    "Place",
                    "a default writing a member under an equality its sibling, omitted at another default, satisfies"
                    spotAbsent (VRecord [ "t", VStr "b" ]),
                    "Place",
                    "a default writing a member while its sibling is absent"
                    spotNonEmpty (VRecord []),
                    "Place",
                    "a default of a record declaring WhenNonEmpty, its member absent (a list literal is empty or absent)" ] do
                  Expect.equal (refusalOf v tag) "(modelled)" (sprintf "%s: %s is modelled" tag what)

              // REFUSED, each by the sentence naming what the model cannot state.

              for v, tag, phrase in
                  [ CanonicalFormHostsIdl.idl,
                    "Gate",
                    "which compares the number its sibling 'count' holds with VInt 1: the opaque numeric model cannot spell it"
                    recordField "Paging" "pageSize" (aliased [ "size" ]) LayoutIdl.layout,
                    "Grid",
                    "a member of an inline declaration takes no alias and no presence condition"
                    kindField "Gate" "count" (aliased [ "id" ]) CanonicalFormHostsIdl.proved,
                    "Gate",
                    "a member 'count' whose alias 'id' the node object already carries"
                    spot (VRecord [ "t", VStr "b" ]),
                    "Place",
                    "a declared default holding member 't' where its presence condition fails, a value the model's type does not hold"
                    spotEquals (VRecord [ "t", VStr "b" ]),
                    "Place",
                    "a declared default holding member 't' where its presence condition fails, a value the model's type does not hold"
                    spotAbsent (VRecord [ "s", VStr "a"; "t", VStr "b" ]),
                    "Place",
                    "a declared default holding member 't' where its presence condition fails, a value the model's type does not hold"
                    spotNonEmpty (VRecord [ "t", VList [] ]),
                    "Place",
                    "a declared default holding member 't' where its presence condition fails, a value the model's type does not hold"
                    hostOnlySibling,
                    "Grid",
                    "the presence condition of member 'template' over 'host', which the model does not hold as a member of its own" ] do
                  Expect.stringContains (refusalOf v tag) phrase (tag + " is refused by name")

              Expect.equal
                  (refusalOf CanonicalFormHostsIdl.idl "Place")
                  "(modelled)"
                  "a kind meeting no such shape is modelled"

              for k in FStarTarget.partition CanonicalFormHostsIdl.proved do
                  Expect.isNone k.Refusal (k.Tag + " of the certified vocabulary is modelled")

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
