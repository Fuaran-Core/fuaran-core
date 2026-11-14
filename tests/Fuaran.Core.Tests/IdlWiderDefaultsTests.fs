module Fuaran.Core.Tests.IdlWiderDefaultsTests

// ============================================================================
//  Phase 656 (DECISIONS.md D258) — a declared default at the wider data
//  universe's eight types (Phase 639, D251). Before this phase the interpreter
//  honoured one and every generator that prints a default refused it as
//  `UnsupportedDefault` (D251.9). Now the one admissibility, `Decl.literal`,
//  admits each type's canonical spelling, a tuple whose every item is admitted,
//  and the empty set and keyed map; the F# and TypeScript generators print the
//  literal in the host type each binds; and the F* model prints what carries no
//  obligation and refuses the string-borne scalars by name. Held here: the
//  admissibility and its refusals, every generator's verdict, and the laws (the
//  generated F# codec compiled and run, the generated TypeScript codec run under
//  node, each filling the value the interpreter fills).
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit
open Fuaran.Core.Idl.Codegen.Types

let private idl = WiderDefaultsIdl.idl
let private kinds = WiderDefaultsIdl.kinds
let private at = WiderDefaultsIdl.at

/// Each generator that prints a declared default, over a one-member vocabulary (`Holder.x`): its
/// verdict, `Ok` or the refusal.
let private printers: (string * (Idl -> Result<string, CodegenError>)) list =
    let holder = [ "Holder" ]
    let node = VNode("n", "Holder", [ "name", VStr "a" ])

    /// The same member on a record, for the codec-only module, which exposes records.
    let boxed (v: Idl) =
        { v with
            Records =
                [ { Name = "Box"
                    Fields = v.Kinds.Head.Fields
                    Annotations = Annotations.Empty
                    Inline = false } ] }

    /// The kind with `x` its only member, so a default record is a value.
    let only (v: Idl) =
        { v with
            Kinds =
                v.Kinds
                |> List.map (fun k ->
                    { k with
                        Fields = k.Fields |> List.filter (fun f -> f.Name = "x") }) }

    let codecOnly: CodecOnly =
        { Roots = [ "Box" ]
          HostTypes = Map [ "Box", "Host.Box"; "Mark", "Host.Mark" ] }

    [ "F# module", (fun v -> Gen.fsharpModule "M" v holder)
      "F# collecting decoders",
      (fun v -> Gen.fsharpModuleDerived GenSupport.Empty [ Derivation.SpecDecoders ] "M" v holder)
      "F# default records",
      (fun v -> Gen.fsharpModuleDerived GenSupport.Empty [ Derivation.DefaultRecords ] "M" (only v) holder)
      "F# codec-only module", (fun v -> Gen.fsharpCodecModule GenSupport.Empty codecOnly "M" (boxed v))
      "F# scaffold", (fun v -> Gen.fsharpValue v TNode node)
      "TypeScript module", (fun v -> Gen.typescriptModule v holder)
      "TypeScript default records", (fun v -> Gen.typescriptModuleDerived [ Derivation.DefaultRecords ] (only v) holder)
      "TypeScript scaffold", (fun v -> Gen.typescriptValue v TNode node) ]

let private unsupportedDefault (what: string) (r: Result<string, CodegenError>) =
    match r with
    | Error(CodegenError.UnsupportedDefault _) -> ()
    | Error e -> failtestf "%s: refused, but not as UnsupportedDefault: %s" what (CodegenError.describe e)
    | Ok _ -> failtestf "%s: admitted a default it must refuse" what

/// A default at each scalar type in a spelling its reader refuses: a leading zero, a sign, `-0`, a
/// trailing fraction zero, a set unused bit, missing padding, an hour without its zero, a fraction
/// longer than it need be, a whole second with a fraction, and `-0s`.
let private nonCanonical: (IdlType * IdlValue) list =
    [ TInt64, VStr "05"
      TInt64, VStr "+5"
      TInt64, VStr "-0"
      TInt64, VStr "9223372036854775808"
      TDecimal, VStr "1.50"
      TDecimal, VStr "01.5"
      TDecimal, VStr "-0"
      TDecimal, VStr "1e3"
      TBytes, VStr "AB=="
      TBytes, VStr "AQI"
      TTimeOfDay, VStr "8:30:00"
      TTimeOfDay, VStr "08:30:00.50"
      TTimeOfDay, VStr "24:00:00"
      TDuration, VStr "1.0s"
      TDuration, VStr "-0s"
      TDuration, VStr "90"
      TTuple [ TInt64; TStr ], VList [ VStr "05"; VStr "a" ]
      TTuple [ TInt64; TStr ], VList [ VStr "5" ] ]

/// Defaults no generator prints, though the interpreter honours them: a set or keyed map with items
/// (as a list or map with items), and an empty collection or a JSON value inside a tuple, where the
/// literal stands in pattern position.
let private beyondTheRule: (string * IdlType * IdlValue) list =
    [ "a set with items", TSet TStr, VList [ VStr "a" ]
      "a keyed map with items", TKeyedMap(TInt64, TStr), VList [ VList [ VStr "1"; VStr "x" ] ]
      "an empty set inside a tuple", TTuple [ TSet TStr; TStr ], VList [ VList []; VStr "a" ]
      "an empty keyed map inside a tuple", TTuple [ TKeyedMap(TInt64, TStr); TStr ], VList [ VList []; VStr "a" ] ]

let private fsModule () =
    match
        Gen.fsharpModuleDerived
            GenSupport.Empty
            [ Derivation.SpecDecoders; Derivation.DefaultRecords ]
            "Wider"
            idl
            kinds
    with
    | Ok s -> s
    | Error e -> failtestf "the F# backend refused: %s" (CodegenError.describe e)

/// The interpreter's bytes for a `Tally` decoded from `doc` and written through `Plain`, which declares
/// no default, so every member it was filled with is written.
let private interpreterThroughPlain (doc: string) : string =
    match Decode.decode idl doc with
    | Ok(VNode(id, "Tally", fields)) -> Encode.encode idl (VNode(id, "Plain", fields)) |> Result.defaultWith failtest
    | other -> failtestf "the interpreter did not read a Tally: %A" other

/// `Tally` with every member absent but the one with no omit-at-default (`seed`).
let private absentDoc = """{"id":"n","kind":{"$type":"Tally","seed":"7"}}"""

/// `Tally` with every member present AWAY from its default, in canonical order.
let private fullDoc =
    """{"id":"n","kind":{"$type":"Tally","amount":"3","at":"23:59:59.9999999","count":"-9223372036854775808","digest":"","levels":[["1","x"]],"mark":{"$type":"Unset"},"pair":["0","b"],"seed":"7","span":"0s","tags":["a","b"]}}"""

[<Tests>]
let tests =
    testList
        "Idl.WiderDefaults (Phase 656)"
        [ testCase "the fixtures are well-formed: the interpreter admits a default at every type"
          <| fun _ ->
              Expect.isEmpty (Declare.errors idl |> List.map VocabularyFault.describe) "the full fixture"

              for name, t, d in WiderDefaultsIdl.defaults do
                  Expect.isEmpty (Declare.errors (at t d) |> List.map VocabularyFault.describe) name

          testCase "the interpreter fills each default on read and omits it on write"
          <| fun _ ->
              for name, t, d in WiderDefaultsIdl.defaults do
                  let v = at t d
                  let doc = """{"id":"n","kind":{"$type":"Holder","name":"a"}}"""

                  match Decode.decode v doc with
                  | Ok(VNode(_, _, fields)) ->
                      Expect.equal (fields |> List.tryFind (fst >> (=) "x") |> Option.map snd) (Some d) name
                      Expect.equal (Encode.encode v (VNode("n", "Holder", fields))) (Ok doc) name
                  | other -> failtestf "%s: %A" name other

          testCase "Decl.literal admits each type's canonical default, as its typed literal"
          <| fun _ ->
              let lit t v = Decl.literal idl t v

              Expect.equal (lit TInt64 (VStr "9007199254740993")) (Ok(Literal.Int64 9007199254740993L)) "int64"
              Expect.equal (lit TInt64 (VStr "-9223372036854775808")) (Ok(Literal.Int64 Int64.MinValue)) "int64 floor"
              Expect.equal (lit TDecimal (VStr "-12.5")) (Ok(Literal.Text(TDecimal, "-12.5"))) "decimal"
              Expect.equal (lit TTimeOfDay (VStr "08:30:00.5")) (Ok(Literal.Text(TTimeOfDay, "08:30:00.5"))) "time"
              Expect.equal (lit TDuration (VStr "-1.5s")) (Ok(Literal.Text(TDuration, "-1.5s"))) "duration"

              match lit TBytes (VStr "AQI=") with
              | Ok(Literal.Bytes(text, bytes)) ->
                  Expect.equal text "AQI=" "the text"
                  Expect.equal bytes [| 1uy; 2uy |] "the bytes"
              | other -> failtestf "bytes: %A" other

              match lit TBytes (VStr "") with
              | Ok(Literal.Bytes("", bytes)) -> Expect.isEmpty bytes "no bytes"
              | other -> failtestf "empty bytes: %A" other

              Expect.equal
                  (lit (TTuple [ TInt64; TStr ]) (VList [ VStr "-5"; VStr "a" ]))
                  (Ok(Literal.Tuple([ TInt64; TStr ], [ VStr "-5"; VStr "a" ])))
                  "tuple"

              Expect.equal (lit (TSet TStr) (VList [])) (Ok Literal.EmptySet) "empty set"
              Expect.equal (lit (TKeyedMap(TInt64, TStr)) (VList [])) (Ok Literal.EmptyKeyedMap) "empty keyed map"

          testCase "a spelling that is not canonical is refused by name everywhere, and never repaired"
          <| fun _ ->
              for t, d in nonCanonical do
                  let what = sprintf "%A at %A" d t
                  let v = at t d

                  Expect.isNonEmpty (Declare.errors v) (what + ": the interpreter refuses it at declaration too")

                  for name, gen in printers do
                      unsupportedDefault (name + ", " + what) (gen v)

                  match FStarTarget.vocabularyModule "M" v [ "Holder" ] with
                  | Error(CodegenError.UnsupportedDefault _) -> ()
                  | other -> failtestf "F*, %s: %A" what (Result.map ignore other)

          testCase "every generator that prints a default prints one at each type"
          <| fun _ ->
              for name, t, d in WiderDefaultsIdl.defaults do
                  for gen, run in printers do
                      match run (at t d) with
                      | Ok _ -> ()
                      | Error e -> failtestf "%s refused %s: %s" gen name (CodegenError.describe e)

          testCase "a set or keyed map with items, and a collection inside a tuple, stay refused by every generator"
          <| fun _ ->
              for what, t, d in beyondTheRule do
                  let v = at t d
                  Expect.isEmpty (Declare.errors v) (what + ": the interpreter honours it")

                  for name, gen in printers do
                      unsupportedDefault (name + ", " + what) (gen v)

          testCase "the F# literals are the host values: int64, text, a byte array, a tuple, Set.empty and Map.empty"
          <| fun _ ->
              let src = fsModule ()

              for expected in
                  [ "(if s.Count = 9007199254740993L then None else"
                    "(if s.Amount = \"-12.5\" then None else"
                    "(if s.Digest = [| 1uy; 2uy |] then None else"
                    "(match s.Pair with | (-5L, \"a\") -> None | _ ->"
                    "(if Map.isEmpty s.Levels then None else"
                    "(if Set.isEmpty s.Tags then None else"
                    "(match s.Mark with | Mark.At([| 1uy; 2uy |], (-5L, \"a\")) -> None | _ ->"
                    "dDef \"span\" __fs (dText \"duration\" isDurationText) (\"-1.5s\")"
                    "Seed = 7L" ] do
                  Expect.stringContains src expected expected

          testCase "the TypeScript literals are the host values: a bigint, text, a Uint8Array, a tuple and empty arrays"
          <| fun _ ->
              match Gen.typescriptModuleDerived [ Derivation.DefaultRecords ] idl kinds with
              | Error e -> failtestf "refused: %s" (CodegenError.describe e)
              | Ok src ->
                  for expected in
                      [ "s.count === 9007199254740993n ? null"
                        "encBytes(s.digest) === \"\\\"AQI=\\\"\" ? null"
                        "s.pair[0] === -5n && s.pair[1] === \"a\" ? null"
                        "s.levels.length === 0 ? null"
                        "s.tags.length === 0 ? null"
                        "dDef(\"count\", fs, dInt64, 9007199254740993n)"
                        "dDef(\"digest\", fs, dBytes, new Uint8Array([1, 2]))"
                        "dDef(\"pair\", fs, dTuple([dInt64, dStr]), [-5n, \"a\"])"
                        "\"seed\": 7n"
                        "\"mark\": { $type: \"At\", stamp: new Uint8Array([1, 2]), pair: [-5n, \"a\"] }" ] do
                      Expect.stringContains src expected expected

          testCase "the F* model prints the empty set and keyed map, and refuses the string-borne scalars by name"
          <| fun _ ->
              for name, t, d in WiderDefaultsIdl.defaults do
                  match t, FStarTarget.vocabularyModule "M" (at t d) [ "Holder" ] with
                  | (TSet _ | TKeyedMap _), Ok src -> Expect.stringContains src "[]" name
                  | (TInt64 | TDecimal | TBytes | TTimeOfDay | TDuration | TTuple _),
                    Error(CodegenError.UnmodellableInFStar(construct, _)) ->
                      Expect.stringContains
                          construct
                          "string-borne scalar"
                          (name + ": named as the refinement boundary")
                  | _, other -> failtestf "F*, %s: %A" name (Result.map ignore other)

              // A tuple is its items' F* tuple where every item carries no obligation.
              match
                  FStarTarget.vocabularyModule
                      "M"
                      (at (TTuple [ TStr; TBool ]) (VList [ VStr "a"; VBool true ]))
                      [ "Holder" ]
              with
              | Ok src -> Expect.stringContains src "(\"a\", true)" "the F* tuple literal"
              | Error e -> failtestf "F* refused a tuple of a string and a bool: %s" (CodegenError.describe e)

          testCase
              "law: the generated F# codec, strict and collecting, fills each default the interpreter fills (compiled and run)"
          <| fun _ ->
              let names = WiderDefaultsIdl.memberNames
              let lit (s: string) = SourceLit.fsString s

              let pascal (n: string) =
                  string (Char.ToUpperInvariant n[0]) + n.Substring 1

              let copy =
                  names
                  |> List.map (fun n -> sprintf "%s = t.%s" (pascal n) (pascal n))
                  |> String.concat "; "

              let through (decoder: string) (doc: string) =
                  sprintf
                      "printfn \"%%s\" (match %s %s with | Ok { Kind = NodeKind.Tally t } -> encodeNode { Id = \"n\"; Kind = NodeKind.Plain ({ %s } : PlainSpec) } | other -> sprintf \"ERR %%A\" other)"
                      decoder
                      (lit doc)
                      copy

              let script =
                  IdlDeriveTests.Projected.scriptOf
                      "Wider"
                      (fsModule ())
                      (String.concat
                          "\n"
                          [ through "decodeNode" absentDoc
                            through "decodeNodeAll" absentDoc
                            sprintf
                                "printfn \"%%s\" (match decodeNode %s with Ok n -> encodeNode n | Error e -> sprintf \"ERR %%A\" e)"
                                (lit absentDoc)
                            sprintf
                                "printfn \"%%s\" (match decodeNodeAll %s with Ok n -> encodeNode n | Error e -> sprintf \"ERR %%A\" e)"
                                (lit fullDoc)
                            "printfn \"%s\" (encodeNode (mkTally \"n\"))"
                            "printfn \"%s\" (encodeNode { Id = \"n\"; Kind = NodeKind.Tally defaultTallySpec })" ])

              let expected = interpreterThroughPlain absentDoc

              // The probe is not vacuous: the interpreter's reading carries every declared default.
              for _, _, d in WiderDefaultsIdl.defaults do
                  match d with
                  | VStr s -> Expect.stringContains expected ("\"" + s + "\"") "a declared default is filled"
                  | _ -> ()

              Expect.stringContains
                  expected
                  "\"mark\":{\"$type\":\"At\",\"pair\":[\"-5\",\"a\"],\"stamp\":\"AQI=\"}"
                  "the union default"

              Expect.equal
                  (Decode.decode idl fullDoc |> Result.bind (Encode.encode idl))
                  (Ok fullDoc)
                  "the full document is canonical"

              match IdlDeriveTests.Projected.runFsiWith "--checknulls+ --warnaserror+ " script with
              | None -> skiptest "dotnet not on PATH - the generated-codec run is skipped"
              | Some(code, out, err) ->
                  Expect.equal code 0 (sprintf "the generated module compiles and runs: %s" err)

                  let lines =
                      out.Replace("\r\n", "\n").Split('\n')
                      |> Array.filter (fun l -> l <> "")
                      |> List.ofArray

                  Expect.equal
                      lines
                      [ expected; expected; absentDoc; fullDoc; absentDoc; absentDoc ]
                      "each decoder fills what the interpreter fills; the encoder omits it; the constructor and the default record hold it"

          testCase "law: the generated TypeScript codec fills each default the interpreter fills (under node)"
          <| fun _ ->
              let ts =
                  match Gen.typescriptModuleDerived [ Derivation.DefaultRecords ] idl kinds with
                  | Ok s -> s
                  | Error e -> failtestf "the TypeScript codec refused: %s" (CodegenError.describe e)

              let js (s: string) = Text.Json.JsonSerializer.Serialize s

              let harness =
                  ts
                  + "\n\nconst __plain = (r) => r.ok ? encodeNode({ ...r.value, kind: { ...r.value.kind, $type: \"Plain\" } }) : 'ERR ' + JSON.stringify(r.error);\n"
                  + "const __enc = (r) => r.ok ? encodeNode(r.value) : 'ERR ' + JSON.stringify(r.error);\n"
                  + sprintf "console.log(__plain(decodeNode(%s)));\n" (js absentDoc)
                  + sprintf "console.log(__enc(decodeNode(%s)));\n" (js absentDoc)
                  + sprintf "console.log(__enc(decodeNode(%s)));\n" (js fullDoc)
                  + "console.log(encodeNode({ id: \"n\", kind: defaultTallySpec }));\n"

              let tmp =
                  Path.Combine(Path.GetTempPath(), sprintf "fuaran-656-ts-%s.mjs" (Guid.NewGuid().ToString("N")))

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
                          [ interpreterThroughPlain absentDoc; absentDoc; fullDoc; absentDoc ]
                          "the TypeScript host fills what the interpreter fills, and omits it again"
              finally
                  try
                      File.Delete tmp
                  with _ ->
                      () ]
