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
