module Fuaran.Core.Tests.IdlLayoutTypeScriptTests

// ---------------------------------------------------------------------------
// Phase 722 (DECISIONS.md D283) — the TypeScript backend lays out the INLINE declarations (D220).
//
// D220.5 left the TypeScript backend refusing any vocabulary that reaches an inline record, a
// presence-keyed union or a record-payload case. It lays all three out now, and these tests hold
// it to the two hosts that already did, over Phase 474's own vocabulary and documents
// (`LayoutIdl`):
//
//   * every document of the corpus is re-written unchanged by the generated module, by its
//     short-circuiting decoder and by its collecting one, in sorted and in declared key order,
//     and the bytes are the interpreter's;
//   * every refusal is the interpreter's by code and path, and the collecting decoder's defects
//     are the generated F# module's, defect for defect;
//   * the declaration file states the members where the wire holds them, an authored value is
//     printed in that shape, and the keyed walk reads and rebuilds a node an inline declaration
//     holds;
//   * what this host still refuses is refused by name: a derived member over a declaration of
//     which it holds no value.
//
// The executed tests need `node` on PATH and are skipped without it, as the other TypeScript
// agreement tests are.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

module Gen' = Fuaran.Core.Tests.LayoutGenerated

let private emitted (r: Result<string, CodegenError>) : string =
    match r with
    | Ok s -> s
    | Error e -> failtestf "codegen refused: %A" e

let private tagsOf (idl: Idl) : string list = idl.Kinds |> List.map _.Tag

/// A string as a JavaScript string literal.
let private js (s: string) : string =
    Canonical.write Rendering.CrossHost (JStr s)

/// The module with the collecting decoders, which the harness below calls.
let private moduleOf (idl: Idl) : string =
    emitted (Gen.typescriptModuleDerived [ Derivation.SpecDecoders ] idl (tagsOf idl))

/// Two lines per document: the short-circuiting decoder's answer, then the collecting one's. An
/// accepted document is its re-encoding; a refused one is each defect's code and path, the path
/// rendered as `DecodePath.render` writes it.
let private harness (docs: string list) : string =
    [ "const __path = (p) => '$' + p.map((s) => (typeof s === 'number') ? '[' + s + ']' : '[\"' + s + '\"]').join('');"
      "const __shape = (e) => e.code + ' ' + __path(e.path);"
      "for (const d of [" + (docs |> List.map js |> String.concat ", ") + "]) {"
      "  const r = decodeNode(d);"
      "  console.log(r.ok ? 'ok ' + encodeNode(r.value) : 'refused ' + __shape(r.error));"
      "  const a = decodeNodeAll(d);"
      "  console.log(a.ok ? 'ok ' + encodeNode(a.value) : 'refused ' + a.errors.map(__shape).join(' | '));"
      "}" ]
    |> String.concat "\n"

/// The module's two answers per document, or a skip where node is not installed.
let private answers (idl: Idl) (docs: string list) : (string * string * string) list =
    match IdlDeriveTests.runTsModule (moduleOf idl) (harness docs) with
    | None -> skiptest "node not on PATH — the executed TypeScript layout agreement is skipped"
    | Some lines ->
        Expect.equal (List.length lines) (2 * List.length docs) "two answers per document"

        docs |> List.mapi (fun i d -> d, lines[2 * i], lines[2 * i + 1])

/// The interpreter's re-encoding of a document under `idl`.
let private interpreted (idl: Idl) (doc: string) : string =
    match Decode.decodeDetailed idl doc with
    | Error e -> failtestf "the interpreter refused %s: %s" doc (DecodeError.render e)
    | Ok v ->
        match Encode.encode idl v with
        | Ok s -> s
        | Error m -> failtestf "the interpreter could not re-encode %s: %s" doc m

let private shape (e: DecodeError) : string =
    DecodeError.codeName e.Code + " " + DecodePath.render e.Path

let private grid (members: string) : string =
    sprintf """{"id":"g","kind":{"$type":"Grid","columns":[]%s}}""" members

/// Documents the layout refuses, one per rule of the D220.3 reading and one inside an inline member.
let private refused: string list =
    [ grid ""","rowKey":"<closure>","rowKeyField":"id","source":{"$type":"State","key":"k"}"""
      grid ""","source":{"$type":"State","key":"k"},"staticRows":{"headers":[],"rows":[]}"""
      grid ""","staticRows":{"headers":[],"rows":[]}"""
      grid ""
      grid ""","pageSize":"ten","source":{"$type":"State","key":"k"}"""
      """{"id":"f","kind":{"$type":"Form","field":{"$type":"Text","placeholder":3,"value":"v"}}}""" ]

/// A document with several defects, for the collecting order: two inline members' and a case's
/// own, then the member the case synthesises.
let private manyDefects: string =
    """{"id":"g","kind":{"$type":"Grid","columns":3,"pageSize":"x","source":{"$type":"Static","value":[1]},"staticRows":{"headers":4,"rows":[]}}}"""

/// `layout` with a node in each kind of inline declaration: two in the inline record, one in a
/// presence-keyed case.
let private withNodes: Idl =
    let field (name: string) (t: IdlType) (opt: Optionality) : IdlField =
        { Name = name
          Type = t
          Opt = opt
          Aliases = []
          Keyed = false
          Presence = None
          Annotations = Annotations.Empty }

    { LayoutIdl.layout with
        Records =
            LayoutIdl.layout.Records
            |> List.map (fun r ->
                if r.Name = "Paging" then
                    { r with
                        Fields =
                            r.Fields
                            @ [ field "footer" TNode Optional; field "pages" (TList TNode) Required ] }
                else
                    r)
        Unions =
            LayoutIdl.layout.Unions
            |> List.map (fun u ->
                if u.Name = "RowIdentity" then
                    { u with
                        Cases =
                            u.Cases
                            |> List.map (fun c ->
                                if c.Tag = "RowKeyField" then
                                    { c with
                                        Fields = c.Fields @ [ field "cell" TNode Optional ] }
                                else
                                    c) }
                else
                    u) }

[<Tests>]
let agreementTests =
    testList
        "Phase 722 the TypeScript backend lays out inline declarations"
        [ testCase "every document is re-written unchanged, and the bytes are the interpreter's" (fun _ ->
              for idl, name in
                  [ LayoutIdl.layout, "layout"
                    LayoutIdl.flat, "flat"
                    LayoutIdl.layoutDeclared, "layout, declared order"
                    LayoutIdl.flatDeclared, "flat, declared order" ] do
                  for doc, first, every in answers idl LayoutIdl.corpus do
                      let expected = "ok " + interpreted idl doc
                      Expect.equal first expected (sprintf "%s: the short-circuiting decoder on %s" name doc)
                      Expect.equal every expected (sprintf "%s: the collecting decoder on %s" name doc))

          testCase "under declared key order the layout writes the flat twin's bytes" (fun _ ->
              let layout = answers LayoutIdl.layoutDeclared LayoutIdl.corpus
              let flat = answers LayoutIdl.flatDeclared LayoutIdl.corpus

              Expect.equal
                  (layout |> List.map (fun (_, first, _) -> first))
                  (flat |> List.map (fun (_, first, _) -> first))
                  "the inline members stand where the flat members stood")

          testCase "every refusal is the interpreter's, by code and path" (fun _ ->
              for doc, first, every in answers LayoutIdl.layout refused do
                  let expected =
                      match Decode.decodeDetailed LayoutIdl.layout doc with
                      | Error e -> "refused " + shape e
                      | Ok _ -> failtestf "the interpreter accepted %s" doc

                  Expect.equal first expected (sprintf "the short-circuiting decoder on %s" doc)
                  Expect.equal every expected (sprintf "the collecting decoder's one defect on %s" doc))

          testCase "the collecting decoder's defects are the generated F# module's, in its order" (fun _ ->
              let expected =
                  match Gen'.decodeNodeAll manyDefects with
                  | Error es -> "refused " + (es |> List.map shape |> String.concat " | ")
                  | Ok _ -> failtest "the generated F# module accepted the document"

              Expect.isGreaterThan (expected.Split('|').Length) 3 "the document carries several defects"

              match answers LayoutIdl.layout [ manyDefects ] with
              | [ (_, first, every) ] ->
                  Expect.equal every expected "every defect, the synthesised member's after the case's own"
                  Expect.isTrue (expected.StartsWith first) "the first defect is the short-circuiting decoder's"
              | other -> failtestf "one document, %d answers" (List.length other))

          testCase "the synthesised member is compared canonically, so its key order is free" (fun _ ->
              let doc =
                  grid ""","source":{"value":[],"$type":"Static"},"staticRows":{"headers":[],"rows":[]}"""

              match answers LayoutIdl.layout [ doc ] with
              | [ (_, first, _) ] -> Expect.equal first ("ok " + interpreted LayoutIdl.layout doc) "accepted"
              | other -> failtestf "one document, %d answers" (List.length other))

          testCase "an authored value is printed in the wire shape the encoder reads" (fun _ ->
              let literals =
                  LayoutIdl.corpus
                  |> List.map (fun doc ->
                      match Decode.decodeDetailed LayoutIdl.layout doc with
                      | Ok v ->
                          match v with
                          | VNode _
                          | VNodeEnv _ -> emitted (Gen.typescriptValue LayoutIdl.layout TNode v)
                          | other -> failtestf "not a node: %A" other
                      | Error e -> failtestf "the interpreter refused %s: %s" doc (DecodeError.render e))

              let prints =
                  literals
                  |> List.map (fun l -> "console.log(encodeNode(" + l + "));")
                  |> String.concat "\n"

              match IdlDeriveTests.runTsModule (moduleOf LayoutIdl.layout) prints with
              | None -> skiptest "node not on PATH — the executed TypeScript layout agreement is skipped"
              | Some lines -> Expect.equal lines LayoutIdl.corpus "each literal encodes to its document")

          testCase "the keyed walk reads and rebuilds the nodes an inline declaration holds" (fun _ ->
              let leaf (id: string) =
                  sprintf """{"id":"%s","kind":{"$type":"Form","field":{"$type":"Check","on":true}}}""" id

              let doc =
                  sprintf
                      """{"id":"g","kind":{"$type":"Grid","cell":%s,"columns":[],"footer":%s,"pages":[%s,%s],"rowKeyField":"id","source":{"$type":"State","key":"k"}}}"""
                      (leaf "c")
                      (leaf "f")
                      (leaf "p1")
                      (leaf "p2")

              let text =
                  emitted (Gen.typescriptModuleDerived [ Derivation.KeyedPositions ] withNodes (tagsOf withNodes))

              let probe =
                  [ "const __n = decodeNode(" + js doc + ").value;"
                    "console.log(keyedChildren(__n).map((c) => c.id).join(','));"
                    "const __kids = keyedChildren(__n).map((c) => ({ ...c, id: c.id.toUpperCase() }));"
                    "const __m = withKeyedChildren(__kids, __n);"
                    "console.log(keyedChildren(__m).map((c) => c.id).join(','));"
                    "console.log(encodeNode(withKeyedChildren(keyedChildren(__n), __n)));" ]
                  |> String.concat "\n"

              match IdlDeriveTests.runTsModule text probe with
              | None -> skiptest "node not on PATH — the executed TypeScript layout agreement is skipped"
              | Some lines ->
                  Expect.equal
                      lines
                      [ "f,p1,p2,c"; "F,P1,P2,C"; interpreted withNodes doc ]
                      "the record's nodes then the picked case's, rebuilt in place, and the same bytes") ]

[<Tests>]
let shapeTests =
    testList
        "Phase 722 the TypeScript host's shape for an inline declaration"
        [ testCase "the declaration file states the members where the wire holds them" (fun _ ->
              let text =
                  emitted (Gen.typescriptDeclarations LayoutIdl.layout (tagsOf LayoutIdl.layout))

              for line in
                  [ "export type Paging = { pageSize?: number; pageStateKey?: string };"
                    "export type RowIdentity = { rowKey: unknown } | { rowKeyField: string };"
                    "export type GridRows = { source: Feed } | { staticRows: StaticTable };"
                    """export type Field = ({ $type: "Text" } & TextPayload) | { $type: "Check"; on: boolean; label?: string };"""
                    """export type GridSpec = { $type: "Grid"; columns: Array<string>; caption?: string } & Paging & (RowIdentity | { rowKey?: undefined; rowKeyField?: undefined }) & GridRows;""" ] do
                  Expect.stringContains text line "declared")

          testCase "a vocabulary with no inline declaration emits what it emitted" (fun _ ->
              let text = moduleOf LayoutIdl.flat

              for helper in
                  [ "ePick"
                    "dPick"
                    "dSynth"
                    "dKeyed"
                    "encMembers"
                    "decMembers"
                    "colMembers" ] do
                  Expect.isFalse (text.Contains helper) (sprintf "the flat twin's module carries no %s" helper))

          // Phase 722 (DECISIONS.md D284.3) — the constants name the keys of this host's object.
          testCase "the vocabulary constants name the host object's keys: an inline declaration's members" (fun _ ->
              let keys =
                  """["Grid", new Set(["caption", "columns", "pageSize", "pageStateKey", "rowKey", "rowKeyField", "source", "staticRows"])]"""

              let constants (idl: Idl) =
                  emitted (Gen.typescriptModuleDerived [ Derivation.VocabularyConstants ] idl (tagsOf idl))

              Expect.stringContains
                  (constants LayoutIdl.layout)
                  keys
                  "the members the grid's object carries, and no field that holds a declaration"

              // The reshape is the host's, never the wire's: the flat twin answers the same keys.
              Expect.stringContains (constants LayoutIdl.flat) keys "the flat twin"

              // The F# host's value keeps the grouping field, and its constant names that field.
              Expect.stringContains
                  (emitted (
                      Gen.fsharpModuleDerived
                          GenSupport.Empty
                          [ Derivation.VocabularyConstants ]
                          "M"
                          LayoutIdl.layout
                          (tagsOf LayoutIdl.layout)
                  ))
                  """"Grid", set [ "columns"; "paging"; "identity"; "rows"; "caption" ]"""
                  "the F# host names its own value's fields")

          testCase "a derived member over an inline declaration is refused by name" (fun _ ->
              let tags = tagsOf LayoutIdl.layout

              for request, named in
                  [ Derivation.SlotsOf "Paging", "'Paging'"
                    Derivation.SlotsOf "GridRows", "'GridRows'"
                    Derivation.Projections("RowIdentity", [ "rowKeyField" ]), "'RowIdentity'"
                    Derivation.Fold "RowIdentity", "'RowIdentity'" ] do
                  for r in
                      [ Gen.typescriptModuleDerived [ request ] LayoutIdl.layout tags
                        Gen.typescriptDeclarationsDerived [ request ] LayoutIdl.layout tags ] do
                      match r with
                      | Error(CodegenError.UnsupportedConstruct(what, why, _)) ->
                          Expect.stringContains what "a derived member over the inline declaration" "what"
                          Expect.stringContains what named "the declaration is named"
                          Expect.stringContains why "no value of the declaration's type" "the reason"
                      | other -> failtestf "%A was not refused by name: %A" request other

              Expect.isOk
                  (Gen.typescriptModuleDerived [ Derivation.SlotsOf "Field" ] LayoutIdl.layout tags)
                  "a tagged union holding a record payload still has slots") ]
