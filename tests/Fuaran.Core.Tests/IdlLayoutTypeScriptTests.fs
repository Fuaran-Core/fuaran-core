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
//   * a slot enumerator, a fold and a projection over an inline declaration answer what the F#
//     derivation answers, value for value, and are refused only where it refuses (D285).
//
// The executed tests need `node` on PATH and are skipped without it, as the other TypeScript
// agreement tests are.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

module Gen' = Fuaran.Core.Tests.LayoutGenerated
module D = Fuaran.Core.Tests.LayoutDerivedGenerated

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

/// A document with several defects, for the collecting order: two inline members' and a case's
/// own, then the member the case synthesises.
let private manyDefects: string =
    """{"id":"g","kind":{"$type":"Grid","columns":3,"pageSize":"x","source":{"$type":"Static","value":[1]},"staticRows":{"headers":4,"rows":[]}}}"""

let private field (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Aliases = []
      Keyed = false
      Presence = None
      Annotations = Annotations.Empty }

/// A host-only member's type: a function the host holds, which is what carries its placeholder.
let private hostFn: IdlType =
    TFn
        { FSharp = "unit -> unit"
          TypeScript = "() => void"
          Placeholder = "ignore" }

/// `layout` with a HOST-ONLY member in each position a kind's keys are read from: the kind's own
/// (`memo`), an inline record's (`hint`) and a presence-keyed case's (`note`).
let private hostOnlyInCase: Idl =
    { LayoutIdl.layout with
        Kinds =
            LayoutIdl.layout.Kinds
            |> List.map (fun k ->
                if k.Tag = "Grid" then
                    { k with
                        Fields = k.Fields @ [ field "memo" hostFn HostOnly ] }
                else
                    k)
        Records =
            LayoutIdl.layout.Records
            |> List.map (fun r ->
                if r.Name = "Paging" then
                    { r with
                        Fields = r.Fields @ [ field "hint" hostFn HostOnly ] }
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
                                        Fields = c.Fields @ [ field "note" hostFn HostOnly ] }
                                else
                                    c) }
                else
                    u) }

/// `layout` with a node in each kind of inline declaration: two in the inline record, one in a
/// presence-keyed case.
let private withNodes: Idl =
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

/// `layoutDeclared` with a placeholder whose keys are NOT in sorted order as the vocabulary declares
/// them: `Feed.Static` carries `value` and then `kind`, so the member `StaticRows` synthesises is
/// written `{"$type":"Static","value":[],"kind":"none"}`. The layout's own placeholder (`$type`,
/// `value`) is in sorted order already, where a writer that sorts and one that does not agree.
let private unsortedPlaceholder: Idl =
    let placeholder = VUnion("Static", [ "value", VJson(JArr []); "kind", VStr "none" ])

    { LayoutIdl.layoutDeclared with
        Unions =
            LayoutIdl.layoutDeclared.Unions
            |> List.map (fun u ->
                match u.Name, u.Discrimination with
                | "Feed", _ ->
                    { u with
                        Cases =
                            u.Cases
                            |> List.map (fun c ->
                                if c.Tag = "Static" then
                                    { c with
                                        Fields = c.Fields @ [ field "kind" TStr Required ] }
                                else
                                    c) }
                | "GridRows", UnionDiscrimination.ByPresence keyed ->
                    { u with
                        Discrimination =
                            keyed
                            |> List.map (fun k ->
                                if k.Case = "StaticRows" then
                                    { k with
                                        Synthesised = [ "source", placeholder ] }
                                else
                                    k)
                            |> UnionDiscrimination.ByPresence }
                | _ -> u) }

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
              for doc, first, every in answers LayoutIdl.layout LayoutIdl.refused do
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

              // Under declared key order the module's own JSON writer keeps a value's authored order,
              // so a comparison of TEXT refuses this document there and accepts it under sorted order.
              for idl, name in [ LayoutIdl.layout, "sorted"; LayoutIdl.layoutDeclared, "declared" ] do
                  match answers idl [ doc ] with
                  | [ (_, first, every) ] ->
                      Expect.equal first ("ok " + interpreted idl doc) (name + ": accepted")
                      Expect.equal every first (name + ": and by the collecting decoder")
                  | other -> failtestf "one document, %d answers" (List.length other))

          testCase
              "under declared key order a synthesised member is written in the vocabulary's order and read by value"
              (fun _ ->
                  let idl = unsortedPlaceholder
                  Expect.isEmpty (Declare.errors idl) "the vocabulary is admitted"

                  let written =
                      VNode(
                          "g",
                          "Grid",
                          [ "columns", VList []
                            "paging", VRecord []
                            "rows",
                            VUnion("StaticRows", [ "staticRows", VRecord [ "headers", VList []; "rows", VList [] ] ]) ]
                      )
                      |> Encode.encode idl
                      |> function
                          | Ok s -> s
                          | Error m -> failtestf "the interpreter refused the value: %s" m

                  // The fixture bites: the interpreter's placeholder is NOT in sorted key order.
                  let placeholder = "\"source\":{\"$type\":\"Static\",\"value\":[],\"kind\":\"none\"}"
                  Expect.stringContains written placeholder "the interpreter writes the placeholder as declared"

                  let reordered =
                      written.Replace(placeholder, "\"source\":{\"kind\":\"none\",\"value\":[],\"$type\":\"Static\"}")

                  for doc, first, every in answers idl [ written; reordered ] do
                      Expect.equal (interpreted idl doc) written "the interpreter reads it and writes its own bytes"

                      Expect.equal
                          first
                          ("ok " + written)
                          (sprintf "read, and written as the interpreter writes it: %s" doc)

                      Expect.equal every first "and by the collecting decoder"

                  // Another VALUE at the member is refused, as the interpreter refuses it.
                  let other = written.Replace("\"kind\":\"none\"", "\"kind\":\"some\"")

                  match Decode.decodeDetailed idl other, answers idl [ other ] with
                  | Error e, [ (_, first, every) ] ->
                      Expect.equal first ("refused " + shape e) "the interpreter's refusal, by code and path"
                      Expect.equal every first "and the collecting decoder's one defect"
                  | r, _ -> failtestf "the interpreter accepted another value at the member: %A" r)

          testCase
              "a required presence-keyed union holding no case is refused on encode, as the interpreter refuses it"
              (fun _ ->
                  let refusal =
                      match
                          Encode.encode
                              LayoutIdl.layout
                              (VNode("g", "Grid", [ "columns", VList []; "paging", VRecord [] ]))
                      with
                      | Error m -> m
                      | Ok s -> failtestf "the interpreter wrote %s" s

                  Expect.equal refusal "required field 'rows' is absent" "the interpreter names the member"

                  let probe =
                      [ "const __try = (v) => { try { return 'wrote ' + encodeNode(v); } catch (e) { return 'threw ' + e.message; } };"
                        // No case of the REQUIRED union: refused. The same holder with a case: written,
                        // the OPTIONAL union (`identity`) holding no case and writing nothing.
                        "console.log(__try({ id: 'g', kind: { $type: 'Grid', columns: [] } }));"
                        "console.log(__try({ id: 'g', kind: { $type: 'Grid', columns: [], source: { $type: 'State', key: 'k' } } }));" ]
                      |> String.concat "\n"

                  match IdlDeriveTests.runTsModule (moduleOf LayoutIdl.layout) probe with
                  | None -> skiptest "node not on PATH — the executed TypeScript layout agreement is skipped"
                  | Some lines ->
                      Expect.equal
                          lines
                          [ "threw " + refusal
                            "wrote " + grid ""","source":{"$type":"State","key":"k"}""" ]
                          "no document the module's own decoder refuses is written")

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

          // The compiler leg (Phase 348): it runs where `FUARAN_CORE_TSC` names a TypeScript compiler,
          // as the other declaration legs do, and is skipped by name without one.
          testCase "with a TypeScript compiler, a typed consumer of the layout's declarations checks" (fun _ ->
              match IdlDeriveTests.tscJs () with
              | None -> skiptest "FUARAN_CORE_TSC names no TypeScript compiler — the type-check leg cannot run"
              | Some tsc ->
                  let idl = LayoutIdl.layout

                  let consumer =
                      [ "import { encodeNode, decodeNode, type Node, type GridSpec, type FormSpec, type StaticTable, type Feed } from './generated.mjs';"
                        // Each case of the required union, the optional union held and not held, and
                        // the inline record's members: all on the holder.
                        "const a: GridSpec = { $type: 'Grid', columns: [], source: { $type: 'State', key: 'k' } };"
                        "const b: GridSpec = { $type: 'Grid', columns: ['c'], pageSize: 3, rowKeyField: 'id', staticRows: { headers: [], rows: [] }, caption: 'c' };"
                        "const c: GridSpec = { $type: 'Grid', columns: [], pageStateKey: 'p', rowKey: () => undefined, source: { $type: 'Static', value: [] } };"
                        "const f: FormSpec = { $type: 'Form', field: { $type: 'Text', value: 'v', placeholder: 'p' } };"
                        "const size: number | undefined = b.pageSize;"
                        "if ('staticRows' in b) { const t: StaticTable = b.staticRows; console.log(t.headers.length); }"
                        "if ('source' in a) { const s: Feed = a.source; console.log(s.$type); }"
                        "const nodes: Node[] = [{ id: 'a', kind: a }, { id: 'b', kind: b }, { id: 'c', kind: c }, { id: 'f', kind: f }];"
                        "for (const n of nodes) { const r = decodeNode(encodeNode(n)); if (r.ok) { const back: Node = r.value; console.log(back.id, size); } }"
                        // A REQUIRED presence-keyed union with no case is not a value of the holder.
                        "// @ts-expect-error"
                        "const none: GridSpec = { $type: 'Grid', columns: [] };"
                        // An inline declaration has no key of its own on this host.
                        "// @ts-expect-error"
                        "const keyed: GridSpec = { $type: 'Grid', columns: [], paging: { pageSize: 3 }, source: { $type: 'State', key: 'k' } };"
                        "console.log(none, keyed);" ]
                      |> String.concat "\n"

                  let files =
                      [ "generated.mjs", moduleOf idl
                        "generated.d.mts",
                        emitted (Gen.typescriptDeclarationsDerived [ Derivation.SpecDecoders ] idl (tagsOf idl))
                        "consumer.mts", consumer ]

                  match IdlDeriveTests.tscCheck tsc files with
                  | None -> skiptest "node not on PATH"
                  | Some(code, out) -> Expect.equal code 0 (sprintf "tsc accepts the typed consumer: %s" out))

          testCase "with a TypeScript compiler, every layout vocabulary's derived declarations compile" (fun _ ->
              match IdlDeriveTests.tscJs () with
              | None -> skiptest "FUARAN_CORE_TSC names no TypeScript compiler — the type-check leg cannot run"
              | Some tsc ->
                  for name, idl in
                      [ "layout", LayoutIdl.layout
                        "layout, declared order", LayoutIdl.layoutDeclared
                        "flat", LayoutIdl.flat
                        "derived", LayoutIdl.derived
                        "nodes in inline declarations", withNodes
                        "a keyed case holding a host-only member", hostOnlyInCase ] do
                      IdlDeriveTests.derivedDeclarationsCompile tsc name idl)

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
                  Expect.isFalse (text.Contains helper) (sprintf "the flat twin's module carries no %s" helper)

              // Every byte, not seven names: each emission is held to the SHA-256 of what the commit
              // before Phase 722 emitted for the flat twin (`69faa9ac`, measured on a build of it). A
              // later change that moves this host's emission for every vocabulary moves these three
              // pins with it, deliberately; the laying-out of inline declarations must not.
              let sha256 (s: string) : string =
                  System.Text.Encoding.UTF8.GetBytes s
                  |> System.Security.Cryptography.SHA256.HashData
                  |> System.Convert.ToHexString
                  |> fun h -> h.ToLowerInvariant()

              let tags = tagsOf LayoutIdl.flat

              for name, emission, pin in
                  [ "the module",
                    Gen.typescriptModule LayoutIdl.flat tags,
                    "5feb26f1b16736e13ed66a7c6d2cde60214c3598d10c0a6a32061a44020eb821"
                    "the declarations",
                    Gen.typescriptDeclarations LayoutIdl.flat tags,
                    "c5362a7ef7eb0f490cf367bd23bcb15ca1b0631b9a8540199d952e980ad2c82c"
                    "the module with the collecting decoders",
                    Ok text,
                    "ee444ad114a080b6fa4e67850478be42d2647324103453eb3682e4fd21127fe6" ] do
                  Expect.equal (sha256 (emitted emission)) pin (name + " of the flat twin is the bytes it was"))

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

          // The rule for a host-only member is the F# derivation's: it names every declared field,
          // host-only included. So this host names one wherever it is declared: the kind's own, an
          // inline record's and a presence-keyed case's.
          testCase
              "the vocabulary constants name a host-only member wherever it is declared, as the F# derivation does"
              (fun _ ->
                  let idl = hostOnlyInCase
                  Expect.isEmpty (Declare.errors idl) "the vocabulary is admitted"

                  let fsharp =
                      emitted (
                          Gen.fsharpModuleDerived
                              GenSupport.Empty
                              [ Derivation.VocabularyConstants ]
                              "M"
                              idl
                              (tagsOf idl)
                      )

                  // What the F# derivation names for the kind: every declared field, the host-only one too.
                  let declared = [ "columns"; "paging"; "identity"; "rows"; "caption"; "memo" ]

                  Expect.stringContains
                      fsharp
                      (sprintf "\"Grid\", set [ %s ]" (declared |> List.map (sprintf "\"%s\"") |> String.concat "; "))
                      "the F# host names every declared field of the kind, host-only included"

                  // The same rule one level down: a field holding an inline declaration is that
                  // declaration's every declared member, host-only included.
                  let membersOf (name: string) : string list =
                      let grid = idl.Kinds |> List.find (fun k -> k.Tag = "Grid")
                      let f = grid.Fields |> List.find (fun f -> f.Name = name)

                      match f.Type with
                      | TRecord r ->
                          match idl.Records |> List.tryFind (fun d -> d.Name = r && d.Inline) with
                          | Some d -> d.Fields |> List.map _.Name
                          | None -> [ name ]
                      | TUnion(u, _) ->
                          match idl.Unions |> List.tryFind (fun d -> d.Name = u) with
                          | Some d when d.Discrimination <> UnionDiscrimination.Tagged ->
                              d.Cases |> List.collect (fun c -> c.Fields |> List.map _.Name) |> List.distinct
                          | _ -> [ name ]
                      | _ -> [ name ]

                  let expected =
                      declared
                      |> List.collect membersOf
                      |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b))

                  for hostOnly in [ "memo"; "hint"; "note" ] do
                      Expect.contains expected hostOnly "the fixture declares a host-only member in each position"

                  Expect.stringContains
                      (emitted (Gen.typescriptModuleDerived [ Derivation.VocabularyConstants ] idl (tagsOf idl)))
                      (sprintf
                          "[\"Grid\", new Set([%s])]"
                          (expected |> List.map (sprintf "\"%s\"") |> String.concat ", "))
                      "this host names the same members, each inline field as its declaration's") ]

// ---------------------------------------------------------------------------
// Phase 722 (DECISIONS.md D285) — the derived members over an inline declaration. Each host is run
// over `LayoutIdl.derived`: the committed F# module `LayoutDerivedGenerated` and the TypeScript
// module the same requests emit, under node. A value is compared as the BYTES its own host's
// encoder writes for a node holding it, so the comparison is each host's codec's, not a rendering
// of this file's: the F# value is set into a blank node, the TypeScript value is spread into one.
// ---------------------------------------------------------------------------

let private blankGrid: D.GridSpec =
    { Columns = []
      Paging = ({ PageSize = None; PageStateKey = None }: D.Paging)
      Identity = None
      Rows = D.GridRows.Source(D.Feed.State "k")
      Caption = None }

let private enc (k: D.NodeKind) : string =
    D.encodeNode ({ Id = "x"; Kind = k }: D.Node)

let private showPaging (p: D.Paging) =
    enc (D.NodeKind.Grid { blankGrid with Paging = p })

let private showIdentity (r: D.RowIdentity) =
    enc (D.NodeKind.Grid { blankGrid with Identity = Some r })

let private showRows (r: D.GridRows) =
    enc (D.NodeKind.Grid { blankGrid with Rows = r })

let private showField (f: D.Field) =
    enc (D.NodeKind.Form({ Field = f }: D.FormSpec))

let private showChain (c: D.Chain) =
    enc (
        D.NodeKind.Outline(
            { Chain = c
              Tree = D.Tree.Leaf 0
              Box = None }
            : D.OutlineSpec
        )
    )

let private showTree (t: D.Tree) =
    enc (
        D.NodeKind.Outline(
            { Chain = D.Chain.Stop ""
              Tree = t
              Box = None }
            : D.OutlineSpec
        )
    )

let private opt (show: 'a -> string) (o: 'a option) : string =
    match o with
    | Some v -> show v
    | None -> "none"

let private slots (show: 'a -> string) (xs: (string * 'a) list) : string =
    xs |> List.map (fun (name, v) -> name + "=" + show v) |> String.concat ";"

/// Every derived member's answer over one node, by the F# host.
let private fsharpAnswers (n: D.Node) : string list =
    [ yield "paging " + slots showPaging (D.slotsOfPaging n)
      yield "identity " + slots showIdentity (D.slotsOfRowIdentity n)
      yield "rows " + slots showRows (D.slotsOfGridRows n)
      yield "field " + slots showField (D.slotsOfField n)
      yield "chain " + slots showChain (D.slotsOfChain n)
      yield "tree " + slots showTree (D.slotsOfTree n)
      for _, v in D.slotsOfRowIdentity n do
          yield "rowKey " + opt (fun () -> "closure") (D.RowIdentity.rowKey v)
          yield "rowKeyField " + opt id (D.RowIdentity.rowKeyField v)
      for _, v in D.slotsOfGridRows n do
          yield "source " + opt (D.GridRows.Source >> showRows) (D.GridRows.source v)

          yield
              "staticRows "
              + opt (D.GridRows.StaticRows >> showRows) (D.GridRows.staticRows v)
      for _, v in D.slotsOfField n do
          yield "payload " + opt (D.Field.Text >> showField) (D.Field.payload v)
          yield "on " + opt (fun (b: bool) -> if b then "true" else "false") (D.Field.on v)
      for _, v in D.slotsOfChain n do
          yield "link " + opt (D.Chain.Link >> showChain) (D.Chain.link v)
          yield "stop " + opt id (D.Chain.stop v)

          yield
              "foldChain "
              + (D.Chain.fold (fun acc c -> acc @ [ showChain c ]) [] v |> String.concat ";")
      for _, v in D.slotsOfTree n do
          yield "branch " + opt (D.Tree.Branch >> showTree) (D.Tree.payload v)
          yield "n " + opt (fun (i: int) -> string i) (D.Tree.n v)

          yield
              "foldTree "
              + (D.Tree.fold (fun acc t -> acc @ [ showTree t ]) [] v |> String.concat ";") ]

/// The same answers by the TypeScript host, one line a document; then one line saying whether a
/// member handed the HOLDER answers what it answers handed the slot's value, and whether each
/// inline value is its declaration's members alone.
let private derivedHarness (docs: string list) : string =
    [ "const RowIdentity = RowIdentity$, GridRows = GridRows$, Field = Field$, Chain = Chain$, Tree = Tree$;"
      "const __enc = (kind) => encodeNode({ id: 'x', kind: kind });"
      "const __grid = { $type: 'Grid', columns: [] };"
      "const __rows = { source: { $type: 'State', key: 'k' } };"
      "const showPaging = (v) => __enc({ ...__grid, ...__rows, ...v });"
      "const showIdentity = (v) => __enc({ ...__grid, ...__rows, ...v });"
      "const showRows = (v) => __enc({ ...__grid, ...v });"
      "const showField = (v) => __enc({ $type: 'Form', field: v });"
      "const showChain = (v) => __enc({ $type: 'Outline', tree: { $type: 'Leaf', n: 0 }, ...v });"
      "const showTree = (v) => __enc({ $type: 'Outline', stop: '', tree: v });"
      "const opt = (show, v) => (v === undefined ? 'none' : show(v));"
      "const slots = (show, xs) => xs.map(([name, v]) => name + '=' + show(v)).join(';');"
      "const collect = (show) => (acc, v) => acc.concat([show(v)]);"
      "const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);"
      "for (const d of [" + (docs |> List.map js |> String.concat ", ") + "]) {"
      "  const n = decodeNode(d).value;"
      "  const out = [];"
      "  out.push('paging ' + slots(showPaging, slotsOfPaging(n)));"
      "  out.push('identity ' + slots(showIdentity, slotsOfRowIdentity(n)));"
      "  out.push('rows ' + slots(showRows, slotsOfGridRows(n)));"
      "  out.push('field ' + slots(showField, slotsOfField(n)));"
      "  out.push('chain ' + slots(showChain, slotsOfChain(n)));"
      "  out.push('tree ' + slots(showTree, slotsOfTree(n)));"
      "  let held = true;"
      "  const alone = (v, keys) => Object.keys(v).every((k) => keys.indexOf(k) >= 0);"
      "  for (const [, v] of slotsOfPaging(n)) held = held && alone(v, ['pageSize', 'pageStateKey']);"
      "  for (const [, v] of slotsOfRowIdentity(n)) {"
      "    out.push('rowKey ' + opt(() => 'closure', RowIdentity.rowKey(v)));"
      "    out.push('rowKeyField ' + opt((s) => s, RowIdentity.rowKeyField(v)));"
      "    held = held && alone(v, ['rowKey', 'rowKeyField']) && same(RowIdentity.rowKey(n.kind), RowIdentity.rowKey(v)) && same(RowIdentity.rowKeyField(n.kind), RowIdentity.rowKeyField(v));"
      "  }"
      "  for (const [, v] of slotsOfGridRows(n)) {"
      "    out.push('source ' + opt((f) => showRows({ source: f }), GridRows.source(v)));"
      "    out.push('staticRows ' + opt((t) => showRows({ staticRows: t }), GridRows.staticRows(v)));"
      "    held = held && alone(v, ['source', 'staticRows']) && same(GridRows.source(n.kind), GridRows.source(v)) && same(GridRows.staticRows(n.kind), GridRows.staticRows(v));"
      "  }"
      "  for (const [, v] of slotsOfField(n)) {"
      "    out.push('payload ' + opt((p) => showField({ $type: 'Text', ...p }), Field.payload(v)));"
      "    out.push('on ' + opt((b) => (b ? 'true' : 'false'), Field.on(v)));"
      "    held = held && (Field.payload(v) === undefined || alone(Field.payload(v), ['value', 'placeholder']));"
      "  }"
      "  for (const [, v] of slotsOfChain(n)) {"
      "    out.push('link ' + opt((b) => showChain({ link: b }), Chain.link(v)));"
      "    out.push('stop ' + opt((s) => s, Chain.stop(v)));"
      "    out.push('foldChain ' + Chain.fold(collect(showChain), [], v).join(';'));"
      "    held = held && alone(v, ['link', 'stop']) && same(Chain.fold(collect(showChain), [], n.kind), Chain.fold(collect(showChain), [], v)) && same(Chain.link(n.kind), Chain.link(v));"
      "  }"
      "  for (const [, v] of slotsOfTree(n)) {"
      "    out.push('branch ' + opt((p) => showTree({ $type: 'Branch', ...p }), Tree.payload(v)));"
      "    out.push('n ' + opt((i) => String(i), Tree.n(v)));"
      "    out.push('foldTree ' + Tree.fold(collect(showTree), [], v).join(';'));"
      "    held = held && (Tree.payload(v) === undefined || alone(Tree.payload(v), ['kids', 'note']));"
      "  }"
      "  console.log(out.join(' | '));"
      "  console.log('held ' + held);"
      "}" ]
    |> String.concat "\n"

[<Tests>]
let derivedTests =
    let idl = LayoutIdl.derived
    let tags = tagsOf idl
    let docs = LayoutIdl.corpus @ LayoutIdl.derivedCorpus

    testList
        "Phase 722 the TypeScript derived members over an inline declaration"
        [ testCase "the committed LayoutDerivedGenerated.fs is what the generator emits (byte for byte)" (fun _ ->
              let generated = emitted (LayoutIdl.generateDerived ())
              let path = Snapshots.repoFile LayoutIdl.derivedGeneratedFile

              Approval.write Approval.Regen Approval.Regenerated.LayoutDerived path generated
              |> ignore

              Expect.equal
                  generated
                  (System.IO.File.ReadAllText path)
                  "regenerate with: FUARAN_REGEN=LayoutDerivedGenerated, running IdlLayoutTypeScriptTests")

          testCase "every slot, fold and projection answers what the F# derivation answers" (fun _ ->
              let text = emitted (Gen.typescriptModuleDerived LayoutIdl.derivations idl tags)

              let expected =
                  docs
                  |> List.map (fun d ->
                      match D.decodeNode d with
                      | Ok n -> fsharpAnswers n |> String.concat " | "
                      | Error e -> failtestf "the generated F# module refused %s: %s" d (DecodeError.render e))

              // The corpus is not vacuous: each inline shape is held by some document, both cases of a
              // presence-keyed union are answered, and each fold visits a nested value.
              let all = String.concat "\n" expected

              for needle in
                  [ "paging paging="
                    "identity identity="
                    "rowKey closure"
                    "rowKeyField id"
                    "staticRows {"
                    "source {"
                    "payload {"
                    "link {"
                    "stop now"
                    "branch {"
                    "n 7" ] do
                  Expect.stringContains all needle "the F# host answers this member over the corpus"

              let visits =
                  match D.decodeNode LayoutIdl.derivedCorpus[0] with
                  | Ok n ->
                      D.slotsOfChain n
                      |> List.sumBy (fun (_, v) -> D.Chain.fold (fun c _ -> c + 1) 0 v),
                      D.slotsOfTree n |> List.sumBy (fun (_, v) -> D.Tree.fold (fun c _ -> c + 1) 0 v)
                  | Error _ -> 0, 0

              Expect.equal visits (3, 5) "the folds visit every nested value: three of the chain, five of the tree"

              match IdlDeriveTests.runTsModule text (derivedHarness docs) with
              | None -> skiptest "node not on PATH — the executed TypeScript derived agreement is skipped"
              | Some lines ->
                  Expect.equal (List.length lines) (2 * List.length docs) "two lines a document"

                  docs
                  |> List.iteri (fun i d ->
                      Expect.equal lines[2 * i] expected[i] (sprintf "the two hosts answer alike over %s" d)

                      Expect.equal
                          lines[2 * i + 1]
                          "held true"
                          (sprintf
                              "a holder answers as the value it holds, and an inline value is its members alone, over %s"
                              d)))

          testCase "a holder's default record lays an inline member's defaults on the holder, at wire keys" (fun _ ->
              let text =
                  emitted (Gen.typescriptModuleDerived [ Derivation.DefaultRecords ] idl tags)

              let expected = enc (D.NodeKind.Grid D.defaultGridSpec)

              // The fixture bites: the F# default holds a member of the inline record and the case
              // of the required union, whose encoding carries the member that case synthesises.
              for needle in [ "\"pageSize\":10"; "\"staticRows\":{"; "\"source\":{" ] do
                  Expect.stringContains expected needle "the F# default record encodes this member"

              let probe =
                  [ "console.log(Object.keys(defaultGridSpec).sort().join(','));"
                    "const __doc = encodeNode({ id: 'x', kind: defaultGridSpec });"
                    "console.log(__doc);"
                    "const __r = decodeNode(__doc);"
                    "console.log(__r.ok ? 'ok ' + encodeNode(__r.value) : 'refused ' + __r.error.code + ' ' + __r.error.path.join('/'));" ]
                  |> String.concat "\n"

              match IdlDeriveTests.runTsModule text probe with
              | None -> skiptest "node not on PATH — the executed TypeScript derived agreement is skipped"
              | Some lines ->
                  Expect.equal
                      lines
                      [ "$type,columns,pageSize,staticRows"; expected; "ok " + expected ]
                      "the holder's own keys; the F# default's bytes; and the module's own decoder reads them back")

          testCase "the declaration file declares every derived member over the declaration's own type" (fun _ ->
              let text =
                  emitted (Gen.typescriptDeclarationsDerived LayoutIdl.derivations idl tags)

              for line in
                  [ "export declare function slotsOfPaging(n: Node): Array<[string, Paging]>;"
                    "export declare function slotsOfRowIdentity(n: Node): Array<[string, RowIdentity]>;"
                    "  fold<S>(folder: (state: S, v: Chain) => S, state: S, v: Chain): S;"
                    "  rowKeyField(v: RowIdentity): string | undefined;"
                    "  payload(v: Field): TextPayload | undefined;" ] do
                  Expect.stringContains text line "declared")

          // D285.4 — no refusal of this host's own is left: a request is refused exactly where the F#
          // derivation refuses it, in its words.
          testCase "a request the F# derivation refuses is refused here in the same words, and no other" (fun _ ->
              let fsharp (request: Derivation) =
                  Gen.fsharpModuleDerived GenSupport.Empty [ request ] "M" idl tags
                  |> Result.map ignore

              let typescript (request: Derivation) =
                  [ Gen.typescriptModuleDerived [ request ] idl tags |> Result.map ignore
                    Gen.typescriptDeclarationsDerived [ request ] idl tags |> Result.map ignore ]

              for request, reason in
                  [ Derivation.Fold "Paging", "a union derivation is generated over a union"
                    Derivation.Projections("Paging", [ "pageSize" ]), "a union derivation is generated over a union"
                    Derivation.Fold "RowIdentity", "a fold is derived for a union whose cases contain the union itself"
                    Derivation.Fold "GridRows", "a fold is derived for a union whose cases contain the union itself"
                    Derivation.SlotsOf "TextPayload", "no selected kind or envelope field is declared at this one"
                    Derivation.SlotsOf "BranchPayload", "no selected kind or envelope field is declared at this one"
                    Derivation.Projections("Chain", [ "label" ]),
                    "a projection reads a field the union's cases declare" ] do
                  match fsharp request with
                  | Error(CodegenError.UnsupportedConstruct(_, why, _)) as refusal ->
                      Expect.stringContains why reason "the F# derivation's reason"

                      for answer in typescript request do
                          Expect.equal (sprintf "%A" answer) (sprintf "%A" refusal) (sprintf "%A" request)
                  | other -> failtestf "the F# derivation did not refuse %A: %A" request other

              for request in LayoutIdl.derivations do
                  Expect.isOk (fsharp request) (sprintf "the F# derivation emits %A" request)

                  for answer in typescript request do
                      Expect.isOk answer (sprintf "and so does this host: %A" request))

          testCase "a vocabulary with no inline declaration reads no inline value" (fun _ ->
              let requests =
                  [ Derivation.SlotsOf "Field"
                    Derivation.Projections("Field", [ "on"; "value" ]) ]

              let text =
                  emitted (Gen.typescriptModuleDerived requests LayoutIdl.flat (tagsOf LayoutIdl.flat))

              for helper in [ "heldMembers"; "valMembers"; "ePick" ] do
                  Expect.isFalse (text.Contains helper) (sprintf "the flat twin's derived module carries no %s" helper)) ]
