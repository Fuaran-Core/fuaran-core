module Fuaran.Core.Tests.IdlRecordTypeScriptTests

// ---------------------------------------------------------------------------
// Phase 732 — the TypeScript backend emits a parameterised record.
//
// This host holds a value in its WIRE shape, so a parameterised record is that host's object at each
// instantiation and an inline one's members are its holder's own. The codec is held to the
// interpreter and the compiled F# module in `IdlThreeHostTests`; this file holds the declaration
// file to a TypeScript compiler, the derived members to the generated F# module's answers, the
// value printer to the argument, and pins the refusals the backend keeps.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

let private emitted (r: Result<string, CodegenError>) : string =
    match r with
    | Ok src -> src
    | Error e -> failtestf "the generator refused: %s" (CodegenError.describe e)

let private tagsOf (idl: Idl) : string list = idl.Kinds |> List.map _.Tag

/// The lines of an emitted file that declare a type, each from `export type` to its `;`.
let private typeLines (declarations: string) : string list =
    declarations.Split('\n')
    |> Array.filter (fun l -> l.StartsWith("export type ", System.StringComparison.Ordinal))
    |> List.ofArray

// ---------------------------------------------------------------------------
// The declarations: a generic TypeScript type for a generic record, applied at each reference; an
// inline one intersected into its holder at the holder's argument.
// ---------------------------------------------------------------------------

/// A consumer of the vocabulary's declarations that uses a generic record at two instantiations,
/// passes one through a function generic in the record's parameter, and round-trips a node of
/// every kind. Each `@ts-expect-error` line is one the compiler must refuse: an unused directive is
/// itself an error, so the consumer checks only while every one of them is still refused.
let private typedConsumer: string =
    [ "import { encodeNode, decodeNode, type Node, type Ranged, type Labelled, type Series, type Entry, type Paging, type Binding, type Direction, type SliderSpec, type StepperSpec, type GridSpec } from './generated.mjs';"
      // One record, two instantiations, each holding its members at its own argument.
      "const wide: Ranged<number> = { low: 0.5, high: 9.5, steps: [1.5, 2.5] };"
      "const named: Ranged<string> = { low: 'a', high: 'z' };"
      // A function generic in the record's parameter reads either.
      "function span<T>(r: Ranged<T>, minus: (a: T, b: T) => number): number { return minus(r.high, r.low); }"
      "const spans: number[] = [span(wide, (a, b) => a - b), span(named, (a, b) => a.length - b.length)];"
      // A record applied to a record, a record passing its parameter on, two parameters.
      "const window: Labelled<Ranged<number>> = { label: 'w', value: { low: 2, high: 4 } };"
      "const series: Series<number> = { points: [{ label: 'p', value: 0.5 }] };"
      "const sort: Entry<string, Direction> = { key: 'name', value: 'desc' };"
      // A parameterised case holding a record at its own parameter, and an inline one beside its tag.
      "const bounded: Binding<number> = { $type: 'Bounded', range: wide };"
      "const local: Binding<number> = { $type: 'Local', initial: 2.5, stateKey: 'k' };"
      "const slider: SliderSpec = { $type: 'Slider', range: wide, marks: [{ label: 'a', value: 1.5 }], binding: local };"
      "const stepper: StepperSpec = { $type: 'Stepper', range: { low: 1, high: 9 }, window, binding: bounded };"
      // An inline record's members are the holder's own, at the holder's argument.
      "const grid: GridSpec = { $type: 'Grid', pageSize: 20, cursor: 'c', title: { label: 't', value: 'v' }, series, sort };"
      "const paging: Paging<string> = grid;"
      "const cursor: string | undefined = grid.cursor;"
      "const low: number = slider.range.low;"
      "const nodes: Node[] = [{ id: 's', kind: slider }, { id: 'p', kind: stepper }, { id: 'g', kind: grid }];"
      "for (const n of nodes) { const r = decodeNode(encodeNode(n)); if (r.ok) { const back: Node = r.value; console.log(back.id); } }"
      "console.log(spans, paging.pageSize, cursor, low);"
      // A member at the parameter is held to the argument.
      "// @ts-expect-error"
      "const b1: Ranged<number> = { low: 'a', high: 9.5 };"
      // Two instantiations are two types.
      "// @ts-expect-error"
      "const b2: Ranged<string> = wide;"
      // An inline record's member is held to the holder's argument.
      "// @ts-expect-error"
      "const b3: GridSpec = { $type: 'Grid', pageSize: 20, cursor: 3 };"
      // A generic record is not a type until it is applied.
      "// @ts-expect-error"
      "const b4: Ranged = wide;"
      // An argument that is itself an applied record is held through both.
      "// @ts-expect-error"
      "const b5: Labelled<Ranged<number>> = { label: 'w', value: named };"
      "console.log(b1, b2, b3, b4, b5);" ]
    |> String.concat "\n"

/// A consumer the compiler must refuse: a generic record's member held at the wrong argument, with
/// no directive expecting it.
let private mistypedConsumer: string =
    [ "import { type Ranged } from './generated.mjs';"
      "const wide: Ranged<number> = { low: 'a', high: 9.5 };"
      "console.log(wide);" ]
    |> String.concat "\n"

[<Tests>]
let declarationTests =
    testList
        "Phase 732 — the TypeScript declarations over a parameterised record"
        [ testCase "a generic record is a generic type, applied at each reference" (fun _ ->
              let lines =
                  typeLines (
                      emitted (Gen.typescriptDeclarations ParameterisedRecordsIdl.idl ParameterisedRecordsIdl.kindTags)
                  )

              for line in
                  [ "export type Ranged<T> = { low: T; high: T; steps?: Array<T> };"
                    "export type Labelled<T> = { label: string; value: T };"
                    // A record passing its parameter on, and two parameters.
                    "export type Series<T> = { points: Array<Labelled<T>> };"
                    "export type Entry<K, V> = { key: K; value?: V };"
                    // An inline record is a type too: its holder intersects it at the holder's argument.
                    "export type Paging<T> = { pageSize: number; cursor?: T };"
                    "export type Binding<T> = { $type: \"Static\"; value: T } | ({ $type: \"Local\" } & LocalState<T>) | { $type: \"Bounded\"; range: Ranged<T> };"
                    "export type Shape = { $type: \"Span\"; extent: Ranged<number> } | { $type: \"Point\" };"
                    // Two instantiations of one record, and a record applied to a record.
                    "export type StepperSpec = { $type: \"Stepper\"; range: Ranged<number>; window?: Labelled<Ranged<number>>; binding?: Binding<number> };"
                    "export type GridSpec = { $type: \"Grid\"; title?: Labelled<string>; series?: Series<number>; sort?: Entry<string, Direction> } & Paging<string>;" ] do
                  Expect.contains lines line "declared")

          // The compiler leg (Phase 348): it runs where `FUARAN_CORE_TSC` names a TypeScript compiler,
          // as the other declaration legs do, and is skipped by name without one.
          testCase
              "with a TypeScript compiler, a typed consumer uses a generic record at two instantiations, and a mistyped one is refused"
              (fun _ ->
                  match IdlDeriveTests.tscJs () with
                  | None -> skiptest "FUARAN_CORE_TSC names no TypeScript compiler — the type-check leg cannot run"
                  | Some tsc ->
                      for name, idl in ParameterisedRecordsIdl.configurations |> List.map (fun (n, idl, _) -> n, idl) do
                          // The flat configuration discriminates by `kind` and merges the kind into the node.
                          let consumer (text: string) =
                              if idl.Wire.Discriminator = "$type" then
                                  text
                              else
                                  text
                                      .Replace("$type:", idl.Wire.Discriminator + ":")
                                      .Replace("{ id: 's', kind: slider }", "{ id: 's', ...slider }")
                                      .Replace("{ id: 'p', kind: stepper }", "{ id: 'p', ...stepper }")
                                      .Replace("{ id: 'g', kind: grid }", "{ id: 'g', ...grid }")

                          let run (text: string) =
                              IdlDeriveTests.tscCheck
                                  tsc
                                  [ "generated.mjs", emitted (Gen.typescriptModule idl (tagsOf idl))
                                    "generated.d.mts", emitted (Gen.typescriptDeclarations idl (tagsOf idl))
                                    "consumer.mts", consumer text ]

                          match run typedConsumer with
                          | None -> skiptest "node not on PATH"
                          | Some(code, out) ->
                              Expect.equal code 0 (sprintf "%s: tsc accepts the typed consumer: %s" name out)

                          match run mistypedConsumer with
                          | None -> skiptest "node not on PATH"
                          | Some(code, out) ->
                              Expect.notEqual code 0 (name + ": tsc refuses the mistyped consumer")

                              Expect.stringContains
                                  out
                                  "consumer.mts(2,"
                                  (name + ": at the member held at the wrong argument"))

          testCase "with a TypeScript compiler, the derived variant's declarations compile beside its module" (fun _ ->
              match IdlDeriveTests.tscJs () with
              | None -> skiptest "FUARAN_CORE_TSC names no TypeScript compiler — the type-check leg cannot run"
              | Some tsc ->
                  let idl = ParameterisedRecordsIdl.derived

                  match
                      IdlDeriveTests.tscCheck
                          tsc
                          [ "generated.mjs", emitted (Gen.typescriptModule idl (tagsOf idl))
                            "generated.d.mts", emitted (Gen.typescriptDeclarations idl (tagsOf idl))
                            "consumer.mts",
                            [ "import { type Tagged, type Kid, type Panel, type Tree, type Node } from './generated.mjs';"
                              // A union held at a record's argument, and a generic record holding a node.
                              "const leaf: Tree = { $type: 'Leaf', n: 1 };"
                              "const kid: Kid<Tree> = { name: 'k', child: leaf };"
                              "const spare: Tagged<Tree> = { tag: '', items: [leaf] };"
                              "const tree: Tree = { $type: 'Branch', kids: [kid], spare };"
                              "const outline: Node = { id: 'o', kind: { $type: 'Outline', tree, trail: { tag: 't', items: ['a'] } } };"
                              "const panel: Panel<string> = { body: outline, meta: 'm' };"
                              "console.log(panel.body.id, kid.child.$type);"
                              "// @ts-expect-error"
                              "const bad: Panel<string> = { body: outline, meta: 3 };"
                              "console.log(bad);" ]
                            |> String.concat "\n" ]
                  with
                  | None -> skiptest "node not on PATH"
                  | Some(code, out) -> Expect.equal code 0 (sprintf "tsc accepts the consumer: %s" out)) ]

// ---------------------------------------------------------------------------
// The derived members ([[ParameterisedRecordsIdl.derived]]): each is run under node over the
// vocabulary's documents and held to the answer the generated F# module's member gives.
// ---------------------------------------------------------------------------

module D = Fuaran.Core.Tests.ParameterisedRecordsDerivedGenerated
module C = Fuaran.Core.Tests.ParameterisedRecordsCodecGenerated

let private written (j: Fuaran.Core.JVal) : string =
    Fuaran.Core.Canonical.write Fuaran.Core.Rendering.CrossHost j

let private js (s: string) : string = written (Fuaran.Core.JStr s)

let private opt (show: 'a -> string) (o: 'a option) : string =
    match o with
    | Some v -> show v
    | None -> "none"

let private slots (show: 'a -> string) (xs: (string * 'a) list) : string =
    xs |> List.map (fun (name, v) -> name + "=" + show v) |> String.concat ";"

/// A slot of the generic record `Ranged`, at whichever argument the node applies it to.
let private showRanged (o: obj) : string =
    match o with
    | :? D.Ranged<float> as r -> written (C.encodeRanged Fuaran.Core.JFloat r)
    | :? D.Ranged<int> as r -> written (C.encodeRanged Fuaran.Core.JInt r)
    | other -> failtestf "a Ranged at an argument the vocabulary does not apply: %A" other

let private showPanel (o: obj) : string =
    match o with
    | :? D.Panel<string, obj> as p -> p.Meta + "@" + D.encodeNode p.Body
    | other -> failtestf "a Panel at an argument the vocabulary does not apply: %A" other

let private showTree (t: D.Tree) : string = written (C.encodeTree t)

/// A parameterised union's projections, at the argument the kind applies it to: a generic record
/// a case holds, and an inline generic record laid beside the case's tag.
let private bindingAnswers (n: D.Node<obj>) : string list =
    let answers (enc: 'T -> Fuaran.Core.JVal) (b: D.Binding<'T>) =
        [ "range " + opt (C.encodeRanged enc >> written) (D.Binding.range b)
          "payload "
          + opt (fun (l: D.LocalState<'T>) -> written (enc l.Initial) + "/" + l.StateKey) (D.Binding.payload b) ]

    match n.Kind with
    | D.NodeKind.Slider s -> answers Fuaran.Core.JFloat s.Binding
    | D.NodeKind.Stepper s -> s.Binding |> Option.map (answers Fuaran.Core.JInt) |> Option.defaultValue []
    | _ -> []

/// Every derived member's answer over one node, by the F# host.
let private fsharpAnswers (n: D.Node<obj>) : string list =
    [ yield "ranged " + slots showRanged (D.slotsOfRanged n)
      yield "panel " + slots showPanel (D.slotsOfPanel n)
      yield "tree " + slots showTree (D.slotsOfTree n)
      for _, t in D.slotsOfTree n do
          yield "n " + opt (fun (i: int) -> string i) (D.Tree.n t)

          yield
              "kids "
              + opt (List.map (C.encodeKid C.encodeTree >> written) >> String.concat ",") (D.Tree.kids t)

          yield
              "fold "
              + (D.Tree.fold (fun acc x -> acc @ [ showTree x ]) [] t |> String.concat ";")
      yield! bindingAnswers n
      yield "keyed " + (D.keyedChildren n |> List.map _.Id |> String.concat ",")
      yield "children " + (D.children n |> List.map _.Id |> String.concat ",")

      yield
          "rekeyed "
          + D.encodeNode (D.withKeyedChildren (D.keyedChildren n |> List.map (fun c -> { c with Id = c.Id + "z" })) n) ]

/// The same answers by the TypeScript host, one line a document; then whether an inline generic
/// record's value is its own members alone.
let private derivedHarness (docs: string list) : string =
    [ "const Tree = Tree$, Binding = Binding$;"
      "const opt = (show, v) => (v === undefined ? 'none' : show(v));"
      "const slots = (show, xs) => xs.map(([name, v]) => name + '=' + show(v)).join(';');"
      "const collect = (show) => (acc, v) => acc.concat([show(v)]);"
      "const showPanel = (v) => v.meta + '@' + encodeNode(v.body);"
      // A slot's record is written through the generic encoder, each member at the value's own JSON.
      "const showRanged = (v) => encRanged(encJson, v);"
      "for (const d of [" + (docs |> List.map js |> String.concat ", ") + "]) {"
      "  const n = decodeNode(d).value;"
      "  const out = [];"
      "  let held = true;"
      "  out.push('ranged ' + slots(showRanged, slotsOfRanged(n)));"
      "  out.push('panel ' + slots(showPanel, slotsOfPanel(n)));"
      "  out.push('tree ' + slots(encTree, slotsOfTree(n)));"
      "  for (const [, t] of slotsOfTree(n)) {"
      "    out.push('n ' + opt(String, Tree.n(t)));"
      "    out.push('kids ' + opt((ks) => ks.map((k) => encKid(encTree, k)).join(','), Tree.kids(t)));"
      "    out.push('fold ' + Tree.fold(collect(encTree), [], t).join(';'));"
      "  }"
      "  const b = (n.kind.$type === 'Slider' || n.kind.$type === 'Stepper') ? n.kind.binding : undefined;"
      "  if (b !== undefined) {"
      "    out.push('range ' + opt(showRanged, Binding.range(b)));"
      "    out.push('payload ' + opt((l) => encJson(l.initial) + '/' + l.stateKey, Binding.payload(b)));"
      "    const p = Binding.payload(b);"
      "    held = held && (p === undefined || Object.keys(p).every((k) => k === 'initial' || k === 'stateKey'));"
      "  }"
      "  out.push('keyed ' + keyedChildren(n).map((c) => c.id).join(','));"
      "  out.push('children ' + children(n).map((c) => c.id).join(','));"
      "  out.push('rekeyed ' + encodeNode(withKeyedChildren(keyedChildren(n).map((c) => ({ ...c, id: c.id + 'z' })), n)));"
      "  console.log(out.join(' | '));"
      "  console.log('held ' + held);"
      "}" ]
    |> String.concat "\n"

/// A `Slider` with a defect behind each generic record it holds: two of `Ranged<float>`'s members,
/// both members of a `Labelled<float>` in a list, and both members of the inline `LocalState<float>`
/// a case of `Binding<float>` lays beside its tag.
let private manyDefects: string =
    """{"id":"s","kind":{"$type":"Slider","binding":{"$type":"Local","initial":"x"},"marks":[{"label":3,"value":"y"}],"range":{"high":"h","low":0.5,"steps":[1.5,"z"]}}}"""

let private shape (e: Fuaran.Core.DecodeError) : string =
    Fuaran.Core.DecodeError.codeName e.Code
    + " "
    + Fuaran.Core.DecodePath.render e.Path

[<Tests>]
let derivedTests =
    let idl = ParameterisedRecordsIdl.derived
    let tags = tagsOf idl

    let docs =
        (ParameterisedRecordsIdl.documents |> List.map (fun (_, _, bytes) -> bytes))
        @ ParameterisedRecordsIdl.derivedCorpus

    let node (d: string) : D.Node<obj> =
        match D.decodeNode d with
        | Ok n -> n
        | Error e -> failtestf "the generated F# module refused %s: %s" d (Fuaran.Core.DecodeError.render e)

    testList
        "Phase 732 — the TypeScript derived members over a parameterised record"
        [ testCase "every slot, fold, projection and keyed walk answers what the F# derivation answers" (fun _ ->
              let text =
                  emitted (Gen.typescriptModuleDerived ParameterisedRecordsIdl.derivations idl tags)

              let expected = docs |> List.map (node >> fsharpAnswers >> String.concat " | ")
              Expect.equal (List.length docs) 6 "the three pinned documents and the three of the further kinds"

              // The corpus is not vacuous: each member is answered with a value by some document, at
              // two instantiations of the slot's record, and the fold visits values held at a record's
              // argument.
              let all = String.concat "\n" expected

              for needle in
                  [ "ranged range={\"high\":9.5,"
                    "ranged range={\"high\":9,"
                    "panel panel=m@{"
                    "tree tree={"
                    "n 7"
                    "kids {\"child\":"
                    "range {\"high\":3,\"low\":1}"
                    "payload 2.5/k"
                    "keyed b"
                    "children r"
                    "\"id\":\"bz\"" ] do
                  Expect.stringContains all needle "the F# host answers this member over the corpus"

              Expect.equal
                  (D.slotsOfTree (node ParameterisedRecordsIdl.derivedCorpus[0])
                   |> List.sumBy (fun (_, t) -> D.Tree.fold (fun c _ -> c + 1) 0 t))
                  5
                  "the fold visits the value and the four nested behind `Kid<Tree>` and `Tagged<Tree>`"

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
                          (sprintf "an inline generic record's value is its members alone, over %s" d)))

          testCase "a generic record's default record is its members' defaults, restored and omitted" (fun _ ->
              let text =
                  emitted (Gen.typescriptModuleDerived [ Derivation.DefaultRecords ] idl tags)

              let atDefault = ParameterisedRecordsIdl.derivedCorpus[1]

              let probe =
                  [ "console.log(JSON.stringify(defaultTagged));"
                    "const __n = decodeNode(" + js atDefault + ").value;"
                    "console.log(JSON.stringify(__n.kind.trail));"
                    "console.log(encodeNode({ id: 'o2', kind: { $type: 'Outline', tree: __n.kind.tree, trail: defaultTagged } }));" ]
                  |> String.concat "\n"

              // The F# default, at any argument, and the member the F# decoder restores.
              let empty: D.Tagged<string> = D.defaultTagged
              Expect.equal empty { Tag = ""; Items = []; Note = None } "the F# default record"

              match IdlDeriveTests.runTsModule text probe with
              | None -> skiptest "node not on PATH — the executed TypeScript derived agreement is skipped"
              | Some lines ->
                  Expect.equal
                      lines
                      [ """{"tag":"","items":[]}"""; """{"tag":"","items":[]}"""; atDefault ]
                      "the default record; the member the decoder restores; and the encoder omits it again")

          testCase "the vocabulary constants name an inline generic record's members as the holder's keys" (fun _ ->
              let text =
                  emitted (Gen.typescriptModuleDerived [ Derivation.VocabularyConstants ] idl tags)

              let probe =
                  "for (const k of ['Slider', 'Grid', 'Frame']) console.log(k + ' ' + [...kindFieldNames.get(k)].join(','));"

              match IdlDeriveTests.runTsModule text probe with
              | None -> skiptest "node not on PATH — the executed TypeScript derived agreement is skipped"
              | Some lines ->
                  Expect.equal
                      lines
                      [ "Slider binding,marks,range,shape"
                        "Grid cursor,pageSize,series,sort,title"
                        "Frame onOpen,panel,rest" ]
                      "`Paging<string>`'s members are the grid's own keys; the F# constant names `paging`"

                  Expect.isTrue
                      (D.kindFieldNames["Grid"].Contains "paging")
                      "the F# host's value keeps the grouping field (D284)")

          testCase
              "the collecting decoders read a generic record, and answer the F# module's defects in its order"
              (fun _ ->
                  let text =
                      emitted (Gen.typescriptModuleDerived [ Derivation.SpecDecoders ] idl tags)

                  let expected =
                      match D.decodeNodeAll manyDefects with
                      | Error es -> es |> List.map shape |> String.concat " | "
                      | Ok _ -> failtest "the generated F# module accepted the document"

                  Expect.equal (expected.Split('|').Length) 6 "a defect behind every generic record the kind holds"

                  let harness =
                      [ "const __path = (p) => '$' + p.map((s) => (typeof s === 'number') ? '[' + s + ']' : '[\"' + s + '\"]').join('');"
                        "const __shape = (e) => e.code + ' ' + __path(e.path);"
                        "for (const d of [" + (docs |> List.map js |> String.concat ", ") + "]) {"
                        "  const a = decodeNodeAll(d);"
                        "  console.log(a.ok ? encodeNode(a.value) : 'refused');"
                        "}"
                        "const __bad = decodeNodeAll(" + js manyDefects + ");"
                        "console.log(__bad.ok ? 'accepted' : __bad.errors.map(__shape).join(' | '));"
                        "const __spec = decodeSliderSpecAll(JSON.parse(" + js manyDefects + ").kind);"
                        "console.log(__spec.ok ? 'accepted' : String(__spec.errors.length));"
                        "const __first = decodeNode(" + js manyDefects + ");"
                        "console.log(__first.ok ? 'accepted' : __shape(__first.error));" ]
                      |> String.concat "\n"

                  match IdlDeriveTests.runTsModule text harness with
                  | None -> skiptest "node not on PATH — the executed TypeScript derived agreement is skipped"
                  | Some lines ->
                      Expect.equal (List.take 6 lines) docs "the collecting decoder reads and re-writes every document"
                      Expect.equal lines[6] expected "every defect, in the F# module's order"
                      Expect.equal lines[7] "6" "the per-kind collecting decoder finds the same six"
                      Expect.isTrue (expected.StartsWith lines[8]) "the first defect is the short-circuiting decoder's")

          testCase "the declaration file declares every derived member over the generic type" (fun _ ->
              let text =
                  emitted (Gen.typescriptDeclarationsDerived ParameterisedRecordsIdl.derivations idl tags)

              for line in
                  [ // A generic record's instantiations differ field by field: the F# host's `obj`.
                    "export declare function slotsOfRanged(n: Node): Array<[string, unknown]>;"
                    "export declare function slotsOfTree(n: Node): Array<[string, Tree]>;"
                    // A value of the record at every argument: the F# host's `defaultTagged<'T>`.
                    "export declare const defaultTagged: Tagged<never>;"
                    "  kids(v: Tree): Array<Kid<Tree>> | undefined;"
                    "  range<T>(v: Binding<T>): Ranged<T> | undefined;"
                    "  payload<T>(v: Binding<T>): LocalState<T> | undefined;" ] do
                  Expect.stringContains text line "declared")

          testCase
              "with a TypeScript compiler, the derived declarations compile and a typed consumer of the derived members checks"
              (fun _ ->
                  match IdlDeriveTests.tscJs () with
                  | None -> skiptest "FUARAN_CORE_TSC names no TypeScript compiler — the type-check leg cannot run"
                  | Some tsc ->
                      for name, vocabulary in
                          [ "parameterised records", ParameterisedRecordsIdl.idl
                            "parameterised records, flat", ParameterisedRecordsIdl.flat
                            "parameterised records, derived", idl ] do
                          IdlDeriveTests.derivedDeclarationsCompile tsc name vocabulary

                      let consumer =
                          [ "import { Binding, Tree, defaultTagged, slotsOfRanged, type Node, type Ranged, type Tagged, type LocalState, type Kid } from './generated.mjs';"
                            "const wide: Ranged<number> = { low: 1, high: 2 };"
                            "const bounded: Binding<number> = { $type: 'Bounded', range: wide };"
                            "const local: Binding<string> = { $type: 'Local', initial: 'i', stateKey: 'k' };"
                            // A projection answers at the union's argument, at each instantiation.
                            "const range: Ranged<number> | undefined = Binding.range(bounded);"
                            "const none: LocalState<number> | undefined = Binding.payload(bounded);"
                            "const payload: LocalState<string> | undefined = Binding.payload(local);"
                            // The default of a generic record is a value of it at every argument.
                            "const strings: Tagged<string> = defaultTagged;"
                            "const trees: Tagged<Tree> = defaultTagged;"
                            "const leaf: Tree = { $type: 'Leaf', n: 1 };"
                            "const kids: Array<Kid<Tree>> | undefined = Tree.kids(leaf);"
                            "const count: number = Tree.fold((acc: number, _t: Tree) => acc + 1, 0, leaf);"
                            "declare const node: Node;"
                            "const held: Array<[string, unknown]> = slotsOfRanged(node);"
                            "console.log(range, none, payload, strings, trees, kids, count, held);"
                            "// @ts-expect-error"
                            "const b1: Ranged<string> | undefined = Binding.range(bounded);"
                            "// @ts-expect-error"
                            "const b2: Array<[string, Ranged<number>]> = slotsOfRanged(node);"
                            "// @ts-expect-error"
                            "const b3: Tagged<string> = { ...defaultTagged, items: [1] };"
                            "console.log(b1, b2, b3);" ]
                          |> String.concat "\n"

                      match
                          IdlDeriveTests.tscCheck
                              tsc
                              [ "generated.mjs",
                                emitted (Gen.typescriptModuleDerived ParameterisedRecordsIdl.derivations idl tags)
                                "generated.d.mts",
                                emitted (
                                    Gen.typescriptDeclarationsDerived ParameterisedRecordsIdl.derivations idl tags
                                )
                                "consumer.mts", consumer ]
                      with
                      | None -> skiptest "node not on PATH"
                      | Some(code, out) -> Expect.equal code 0 (sprintf "tsc accepts the typed consumer: %s" out)) ]

// ---------------------------------------------------------------------------
// The value printers, and the refusals this backend keeps over a parameterised record. Each kept
// refusal is the one the F# backend keeps, in its sentence, for the reason it gives there; none is
// the refusal of an entry that does not emit a parameterised record.
// ---------------------------------------------------------------------------

let private f (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Aliases = []
      Keyed = false
      Presence = None
      Annotations = Annotations.Empty }

let private kind (tag: string) (fields: IdlField list) : IdlKind =
    { Tag = tag
      Category = "probe"
      Fields = fields
      Annotations = Annotations.Empty }

let private record (name: string) (pars: string list) (fields: IdlField list) : IdlRecord =
    { Name = name
      Params = pars
      Fields = fields
      Annotations = Annotations.Empty
      Inline = false }

let private union (name: string) (pars: string list) (cases: (string * IdlField list) list) : IdlUnion =
    { Name = name
      Params = pars
      Cases =
        cases
        |> List.map (fun (tag, fields) ->
            { Tag = tag
              Fields = fields
              Annotations = Annotations.Empty
              Hosted = None })
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

let private vocabulary (kinds: IdlKind list) : Idl =
    { Kinds = kinds
      Unions = []
      Enums = []
      Records = []
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      Harden = HardenPolicy.Undeclared
      Contracts = [] }

/// The four TypeScript entries that emit a declaration, over `idl` with `requests`: its name and
/// its answer. The module and the declaration file, each plain and with the requests.
let private entries (requests: Derivation list) (idl: Idl) : (string * Result<string, CodegenError>) list =
    let tags = tagsOf idl

    [ "typescriptModule", Gen.typescriptModule idl tags
      "typescriptDeclarations", Gen.typescriptDeclarations idl tags
      "typescriptModuleDerived", Gen.typescriptModuleDerived requests idl tags
      "typescriptDeclarationsDerived", Gen.typescriptDeclarationsDerived requests idl tags ]

/// What a request is refused with by the two derived entries, which must be the F# derivation's
/// refusal of the same request, word for word; the two plain entries emit.
let private refusedAsFSharp (requests: Derivation list) (idl: Idl) (construct: string) : unit =
    let fsharp =
        Gen.fsharpModuleDerived GenSupport.Empty requests "Probe" idl (tagsOf idl)

    match fsharp with
    | Error(CodegenError.UnsupportedConstruct(what, _, _)) -> Expect.equal what construct "the F# derivation's refusal"
    | other -> failtestf "the F# derivation did not refuse %A: %A" requests (Result.isOk other)

    for entry, answer in entries requests idl do
        if entry.EndsWith("Derived", System.StringComparison.Ordinal) then
            Expect.equal
                (answer |> Result.map ignore)
                (fsharp |> Result.map ignore)
                (entry + " refuses the request as the F# derivation does")
        else
            Expect.isOk answer (entry + " emits the vocabulary: only the request is refused")

[<Tests>]
let printerAndRefusalTests =
    testList
        "Phase 732 — the TypeScript value printers and the kept refusals over a parameterised record"
        [ testCase "an authored value of a parameterised record prints each member at its argument" (fun _ ->
              let idl = ParameterisedRecordsIdl.idl

              Expect.equal
                  (Gen.typescriptValue
                      idl
                      (TRecord("Entry", [ TStr; TEnum "Direction" ]))
                      (VRecord [ "key", VStr "name"; "value", VEnum "desc" ]))
                  (Ok "{ key: \"name\", value: \"desc\" }")
                  "an enum's wire string at the parameter"

              // The member at the parameter takes the argument's HOST shape: a 64-bit integer is a
              // `bigint` on this host, which no reading of the declared variable could say.
              Expect.equal
                  (Gen.typescriptValue
                      idl
                      (TRecord("Labelled", [ TInt64 ]))
                      (VRecord [ "label", VStr "w"; "value", VStr "9007199254740993" ]))
                  (Ok "{ label: \"w\", value: 9007199254740993n }")
                  "a bigint where the argument is a 64-bit integer"

              Expect.equal
                  (Gen.typescriptValue
                      idl
                      (TRecord("Labelled", [ TRecord("Ranged", [ TInt ]) ]))
                      (VRecord [ "label", VStr "w"; "value", VRecord [ "low", VInt 2; "high", VInt 4 ] ]))
                  (Ok "{ label: \"w\", value: { low: 2, high: 4 } }")
                  "a record applied to a record"

              // An inline generic record's members are the holder's own, at the holder's argument.
              Expect.equal
                  (Gen.typescriptValue
                      idl
                      (TUnion("Binding", [ TInt64 ]))
                      (VUnion("Local", [ "payload", VRecord [ "initial", VStr "7"; "stateKey", VStr "k" ] ])))
                  (Ok "{ $type: \"Local\", ...{ initial: 7n, stateKey: \"k\" } }")
                  "an inline record's members spread beside the tag, at the union's argument"

              // The value does not fit the argument: refused, not printed at the declared variable.
              Expect.isError
                  (Gen.typescriptValue
                      idl
                      (TRecord("Labelled", [ TInt ]))
                      (VRecord [ "label", VStr "w"; "value", VStr "x" ]))
                  "a string where the argument is an integer")

          testCase
              "a declared default that is a parameterised record's literal is tested and restored at its argument"
              (fun _ ->
                  // The member held AT the parameter is a 64-bit integer at this reference: the encoder's
                  // test and the literal the decoder restores are both the host's `bigint`.
                  let idl =
                      { vocabulary
                            [ kind
                                  "K"
                                  [ f
                                        "pin"
                                        (TRecord("Pinned", [ TInt64 ]))
                                        (OmitDefault(VRecord [ "label", VStr "a"; "value", VStr "5" ])) ] ] with
                          Records =
                              [ record "Pinned" [ "T" ] [ f "label" TStr Required; f "value" (TVar "T") Required ] ] }

                  Expect.isEmpty (Declare.errors idl) "the declaration is admitted"
                  let text = emitted (Gen.typescriptModule idl [ "K" ])

                  Expect.stringContains
                      text
                      "(s.pin.label === \"a\" && s.pin.value === 5n ? null : [\"pin\", encPinned(encInt64, s.pin)])"
                      "the encoder omits the member at the literal"

                  Expect.stringContains
                      text
                      "dDef(\"pin\", fs, ((x) => decPinned(dInt64, x)), { label: \"a\", value: 5n })"
                      "and the decoder restores it"

                  let doc = """{"id":"k","kind":{"$type":"K"}}"""

                  Expect.equal
                      (Decode.decode idl doc |> Result.bind (Encode.encode idl))
                      (Ok doc)
                      "the interpreter's bytes"

                  let probe =
                      [ "const __r = decodeNode(" + js doc + ");"
                        "console.log(typeof __r.value.kind.pin.value, String(__r.value.kind.pin.value));"
                        "console.log(encodeNode(__r.value));"
                        "console.log(encodeNode({ id: 'k', kind: { $type: 'K', pin: { label: 'a', value: 6n } } }));" ]
                      |> String.concat "\n"

                  match IdlDeriveTests.runTsModule text probe with
                  | None -> skiptest "node not on PATH — the executed TypeScript agreement is skipped"
                  | Some lines ->
                      Expect.equal
                          lines
                          [ "bigint 5"
                            doc
                            """{"id":"k","kind":{"$type":"K","pin":{"label":"a","value":"6"}}}""" ]
                          "restored as a bigint, omitted again at the default, and written away from it")

          testCase "a reference no binding can be made for is refused by every entry, by the record's name" (fun _ ->
              // The declaration check refuses the vocabulary; a backend handed it unchecked has no
              // generic function or type to apply, and names the reference.
              let misapplied =
                  { vocabulary [ kind "Holder" [ f "shape" (TRecord("Box", [])) Required ] ] with
                      Records = [ record "Box" [ "T" ] [ f "held" (TVar "T") Required ] ] }

              Expect.isNonEmpty (Declare.errors misapplied) "the declaration is refused"

              let sentence =
                  "a reference applying record 'Box' to 0 type argument(s) where it declares 1"

              for entry, answer in entries [ Derivation.VocabularyConstants ] misapplied do
                  match answer with
                  | Ok _ -> failtestf "%s emitted for a reference no binding can be made for" entry
                  | Error e ->
                      Expect.stringContains (CodegenError.describe e) sentence (entry + " names the reference")

              match Gen.typescriptValue misapplied TStr (VStr "x") with
              | Ok _ -> failtest "typescriptValue printed over a reference no binding can be made for"
              | Error e -> Expect.stringContains (CodegenError.describe e) sentence "typescriptValue names it too"

              // The sentence is the F# entries' own.
              match Gen.fsharpModule "Probe" misapplied [ "Holder" ] with
              | Ok _ -> failtest "the F# module emitted for the same reference"
              | Error e -> Expect.stringContains (CodegenError.describe e) sentence "the F# backend's sentence")

          testCase "an omit-at-default member at a type parameter is refused, for a union and a record alike" (fun _ ->
              // The default's host value is the argument's (a number, a `bigint`, bytes), known only at
              // an instantiation, and the generic encoder is one function for every instantiation:
              // there is no one literal to test against, on this host as on the F# one.
              let holder =
                  kind
                      "K"
                      [ f "u" (TUnion("U", [ TInt ])) Required
                        f "r" (TRecord("R", [ TInt ])) Required ]

              let member' = f "at" (TVar "T") (OmitDefault(VInt 0))

              let asUnion =
                  { vocabulary [ holder ] with
                      Unions = [ union "U" [ "T" ] [ "Only", [ member' ] ] ]
                      Records = [ record "R" [ "T" ] [ f "plain" (TVar "T") Required ] ] }

              let asRecord =
                  { vocabulary [ holder ] with
                      Unions = [ union "U" [ "T" ] [ "Only", [ f "plain" (TVar "T") Required ] ] ]
                      Records = [ record "R" [ "T" ] [ member' ] ] }

              for what, idl in [ "a union case's member", asUnion; "a record's member", asRecord ] do
                  Expect.isEmpty (Declare.errors idl) (what + ": the declaration is admitted")

                  let refusal: Result<string, CodegenError> =
                      Error(CodegenError.UnsupportedDefault(TVar "T", VInt 0))

                  Expect.equal (Gen.typescriptModule idl [ "K" ]) refusal (what + ": the codec has no literal to test")
                  Expect.equal (Gen.fsharpModule "Probe" idl [ "K" ]) refusal (what + ": as the F# module has none")

                  // A default moves no type: the declaration file states the member as present.
                  Expect.isOk (Gen.typescriptDeclarations idl [ "K" ]) (what + ": the declarations emit"))

          testCase
              "a node held through a generic record's argument is refused by the keyed walk, as a union's is"
              (fun _ ->
                  let idl =
                      { vocabulary
                            [ kind "K" [ f "boxed" (TRecord("Box", [ TNode ])) Required ]
                              kind "Leaf" [ f "n" TInt Required ] ] with
                          Records = [ record "Box" [ "T" ] [ f "held" (TVar "T") Required ] ] }

                  refusedAsFSharp
                      [ Derivation.KeyedPositions ]
                      idl
                      "a node held through a type argument of the generic record 'Box'"

                  let asUnion =
                      { idl with
                          Records = []
                          Unions = [ union "Box" [ "T" ] [ "Only", [ f "held" (TVar "T") Required ] ] ]
                          Kinds =
                              [ kind "K" [ f "boxed" (TUnion("Box", [ TNode ])) Required ]
                                kind "Leaf" [ f "n" TInt Required ] ] }

                  refusedAsFSharp
                      [ Derivation.KeyedPositions ]
                      asUnion
                      "a node held through a type argument of the generic union 'Box'"

                  // The codec reads and writes the node through the argument, under node.
                  let doc =
                      """{"id":"k","kind":{"$type":"K","boxed":{"held":{"id":"l","kind":{"$type":"Leaf","n":3}}}}}"""

                  Expect.equal
                      (Decode.decode idl doc |> Result.bind (Encode.encode idl))
                      (Ok doc)
                      "the interpreter's bytes"

                  match
                      IdlDeriveTests.runTsModule
                          (emitted (Gen.typescriptModule idl [ "K"; "Leaf" ]))
                          ("console.log(encodeNode(decodeNode(" + js doc + ").value));")
                  with
                  | None -> skiptest "node not on PATH — the executed TypeScript agreement is skipped"
                  | Some lines -> Expect.equal lines [ doc ] "the generated codec re-writes the interpreter's bytes")

          testCase
              "the message parameter carried through a generic record's argument refuses the message map request"
              (fun _ ->
                  // This host emits nothing for the message map; a request is admitted by the one
                  // derivation admission both hosts share, so the module and its declaration file answer a
                  // request list exactly as the F# derivation does, for a record as for a union.
                  let handler: IdlType =
                      TFn
                          { FSharp = "string -> 'Msg"
                            TypeScript = "(x: string) => Msg"
                            Placeholder = "(fun (_: string) -> box \"<closure>\")" }

                  let idl =
                      { vocabulary [ kind "K" [ f "boxed" (TRecord("Box", [ handler ])) Required ] ] with
                          Records = [ record "Box" [ "T" ] [ f "held" (TVar "T") Required ] ] }

                  refusedAsFSharp
                      [ Derivation.MapMsg ]
                      idl
                      "the message map through a type argument of 'Box' at K.boxed"

                  let asUnion =
                      { vocabulary [ kind "K" [ f "boxed" (TUnion("Box", [ handler ])) Required ] ] with
                          Unions = [ union "Box" [ "T" ] [ "Only", [ f "held" (TVar "T") Required ] ] ] }

                  refusedAsFSharp
                      [ Derivation.MapMsg ]
                      asUnion
                      "the message map through a type argument of 'Box' at K.boxed")

          testCase "a fold through a record whose recursion grows its type argument is refused by name" (fun _ ->
              // `Grow<T>` names `Grow<T list>` at every step: the fold has no last instantiation to read.
              let idl =
                  { vocabulary [ kind "K" [ f "tree" (TUnion("Tree", [])) Required ] ] with
                      Unions =
                          [ union
                                "Tree"
                                []
                                [ "Leaf", []
                                  "Branch", [ f "grown" (TRecord("Grow", [ TUnion("Tree", []) ])) Required ] ] ]
                      Records =
                          [ record
                                "Grow"
                                [ "T" ]
                                [ f "here" (TVar "T") Required
                                  f "next" (TRecord("Grow", [ TList(TVar "T") ])) Optional ] ] }

              refusedAsFSharp
                  [ Derivation.Fold "Tree" ]
                  idl
                  "a fold over 'Tree' through the generic record 'Grow', which has no finite set of instantiations: Grow<'T> reaches Grow<list<'T>>"

              // The codec is one function per record whatever it is applied to: it reads and writes the
              // growing record, two instantiations deep, under node.
              let doc =
                  """{"id":"k","kind":{"$type":"K","tree":{"$type":"Branch","grown":{"here":{"$type":"Leaf"},"next":{"here":[{"$type":"Leaf"}],"next":{"here":[[{"$type":"Leaf"}]]}}}}}}"""

              Expect.equal
                  (Decode.decode idl doc |> Result.bind (Encode.encode idl))
                  (Ok doc)
                  "the interpreter's bytes"

              match
                  IdlDeriveTests.runTsModule
                      (emitted (Gen.typescriptModule idl [ "K" ]))
                      ("console.log(encodeNode(decodeNode(" + js doc + ").value));")
              with
              | None -> skiptest "node not on PATH — the executed TypeScript agreement is skipped"
              | Some lines -> Expect.equal lines [ doc ] "the generated codec re-writes the interpreter's bytes")

          testCase "no entry refuses a vocabulary for declaring a parameterised record" (fun _ ->
              // Every entry over both fixture vocabularies, with every request the derived one admits.
              for name, idl, requests in
                  [ "parameterised records", ParameterisedRecordsIdl.idl, [ Derivation.SpecDecoders ]
                    "parameterised records, flat", ParameterisedRecordsIdl.flat, [ Derivation.SpecDecoders ]
                    "parameterised records, derived",
                    ParameterisedRecordsIdl.derived,
                    ParameterisedRecordsIdl.derivations ] do
                  for entry, answer in entries requests idl do
                      Expect.isOk answer (sprintf "%s: %s emits" name entry)

                  Expect.isOk (Gen.typescriptValue idl TStr (VStr "x")) (name + ": typescriptValue prints")) ]
