module Fuaran.Core.Tests.OracleCliTests

// Phase 482 — `fuaran-core-oracle`, the extracted models behind a JSON-lines protocol
// (docs/oracle-protocol.md). Three families of evidence:
//
//   1. INDEPENDENCE. The CLI may reach the oracle assembly and nothing else of this repository, or
//      a differential a host runs against it could compare production with itself. Held on the
//      project as MSBuild evaluates it and on the sources, and shown red on a planted violation.
//   2. THE CONTRACT. Golden request/response lines, one per function and answer case and one per
//      error code: the protocol's bytes, pinned.
//   3. CONFORMANCE. A seeded pool of requests for every function, answered by ONE CLI process and
//      held, line for line, to the oracle called in-process. The in-process side instantiates each
//      model through the SUITE's bridges (ProofOracle.Bridges.fs) and production's hashes, not the
//      CLI's, so the CLI's reading of the request and its instantiation of each model's parameters
//      are what is compared. The comparison is shown able to lose (the blind parser bridge).

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.ProofOracle
open Fuaran.Core.Tests.ProofOracle.Bridges
open Fuaran.Core.Tests.ChainOracle
open Fuaran.Core.Tests.CanonOracle

module CliJson = Fuaran.Core.Oracle.Cli.Json
module CliMarshal = Fuaran.Core.Oracle.Cli.Marshal

let private inv = Globalization.CultureInfo.InvariantCulture

/// The CLI assembly this suite compiled against; its copy in the test output is the one run.
let private cliDll = typeof<CliJson.J>.Assembly.Location

/// One CLI process answering `requests`, one line each, in order.
let private runCli (requests: string list) : string list =
    let input = (requests |> String.concat "\n") + "\n"

    let p =
        ChildProcess.run (ChildProcess.argv "dotnet" [ cliDll ] |> ChildProcess.withStdin input)
        |> Result.defaultWith failwith

    if p.Exit <> 0 then
        failtestf "the oracle CLI exited %d: %s" p.Exit p.Stderr

    p.Stdout.Split('\n') |> Array.filter (fun l -> l <> "") |> List.ofArray

let private line (id: int) (answer: CliJson.J) : string =
    CliJson.write (CliJson.JObj [ "protocol", CliJson.JStr "1.0"; "id", CliJson.ofInt id; "answer", answer ])

let private request (id: int) (fn: string) (input: CliJson.J) : string =
    CliJson.write (CliJson.JObj [ "id", CliJson.ofInt id; "fn", CliJson.JStr fn; "input", input ])

let private one (case: string) (payload: CliJson.J) = CliJson.JObj [ case, payload ]

/// The case an answer line names, for the reach assertions.
let private caseOf (answer: CliJson.J) =
    match answer with
    | CliJson.JObj [ case, _ ] -> case
    | _ -> "?"

// ---- 1. independence -------------------------------------------------------------------------------

/// What the CLI project and its sources may not do. `projectRefs` and `compiles` are full paths.
let internal cliFindings
    (cliDir: string)
    (oracleProject: string)
    (projectRefs: string list)
    (packageRefs: string list)
    (references: string list)
    (compiles: string list)
    (sources: (string * string) list)
    : string list =
    let production = Regex(@"\bFuaran\.Core\.(?!Oracle\.Cli\b)[A-Za-z]")

    [ for p in projectRefs do
          if
              not (
                  String.Equals(Path.GetFullPath p, Path.GetFullPath oracleProject, StringComparison.OrdinalIgnoreCase)
              )
          then
              yield $"a ProjectReference to {p}: the CLI may reference the oracle project alone"
      for p in packageRefs do
          if p <> "FSharp.Core" then
              yield $"a PackageReference to {p}: the CLI may reference FSharp.Core alone"
      for r in references do
          yield $"a Reference to {r}: the CLI may reference no assembly by path"
      for c in compiles do
          if not ((Path.GetFullPath c).StartsWith(Path.GetFullPath cliDir, StringComparison.OrdinalIgnoreCase)) then
              yield $"a Compile item outside the CLI's directory: {c}"
      for name, text in sources do
          for i, l in Array.indexed (text.Split('\n')) do
              let code =
                  match l.IndexOf("//", StringComparison.Ordinal) with
                  | -1 -> l
                  | k -> l.Substring(0, k)

              if production.IsMatch code then
                  yield $"{name}:{i + 1} names production: {l.Trim()}" ]

/// The CLI project as MSBuild evaluates it (so a reference inherited from Directory.Build.props
/// counts), item type by item type, as full paths or identities.
let private evaluated (project: string) : Map<string, string list> =
    let p =
        ChildProcess.run (
            ChildProcess.argv
                "dotnet"
                [ "msbuild"
                  project
                  "-nologo"
                  "-getItem:ProjectReference"
                  "-getItem:PackageReference"
                  "-getItem:Reference"
                  "-getItem:Compile" ]
        )
        |> Result.defaultWith failwith

    if p.Exit <> 0 then
        failtestf "msbuild could not evaluate %s: %s %s" project p.Stdout p.Stderr

    use doc = JsonDocument.Parse(p.Stdout)
    let items = doc.RootElement.GetProperty("Items")

    [ for kind, key in
          [ "ProjectReference", "FullPath"
            "PackageReference", "Identity"
            "Reference", "Identity"
            "Compile", "FullPath" ] ->
          kind,
          match items.TryGetProperty kind with
          | true, arr -> [ for e in arr.EnumerateArray() -> e.GetProperty(key).GetString() ]
          | _ -> [] ]
    |> Map.ofList

// ---- 2. the contract, pinned -----------------------------------------------------------------------

/// Request line and the exact response line. Each was read against the model's own definition when
/// it was pinned (docs/oracle-protocol.md carries the same examples); a change to any is a protocol
/// change and moves the version.
let private golden: (string * string) list =
    [ """{"id":1,"fn":"oracle.describe","input":{}}""",
      """{"answer":{"description":{"functions":[{"models":[],"name":"oracle.describe"},{"models":["JsonParse"],"name":"json.parse"},{"models":["JsonParse","WireDecode"],"name":"wire.decode"},{"models":["WireCanon"],"name":"wire.render"},{"models":["TreeOps"],"name":"tree.apply"},{"models":["Chain"],"name":"chain.verify"},{"models":["DagFold","TreeOps"],"name":"dag.fold"}],"oracle":"ORACLE","protocol":"1.0"}},"id":1,"protocol":"1.0"}"""
      """{"id":2,"fn":"json.parse","input":{"text":"{\"a\":[1,2.5,\"\\u00e9\"],\"b\":true}"}}""",
      """{"answer":{"value":{"object":[{"key":"a","value":{"array":[{"int":"1"},{"float":"2.5"},{"string":"é"}]}},{"key":"b","value":{"bool":true}}]}},"id":2,"protocol":"1.0"}"""
      """{"id":3,"fn":"json.parse","input":{"text":"[1,"}}""",
      """{"answer":{"parseRefused":{"at":3,"kind":"UnexpectedEndOfInput","message":"unexpected end of input"}},"id":3,"protocol":"1.0"}"""
      """{"id":4,"fn":"json.parse","input":{"text":"[[[1]]]","limits":{"depth":2}}}""",
      """{"answer":{"parseLimit":{"at":2,"limit":"depth"}},"id":4,"protocol":"1.0"}"""
      """{"id":5,"fn":"wire.decode","input":{"text":"{\"kind\":\"group\",\"id\":\"g\",\"items\":[{\"kind\":\"flag\",\"on\":true}]}"}}""",
      """{"answer":{"node":{"id":"g","items":[{"kind":"flag","on":true}],"kind":"group"}},"id":5,"protocol":"1.0"}"""
      """{"id":6,"fn":"wire.decode","input":{"text":"{\"kind\":\"zzz\"}"}}""",
      """{"answer":{"decodeRefused":{"message":"unknown kind: zzz"}},"id":6,"protocol":"1.0"}"""
      """{"id":7,"fn":"wire.render","input":{"value":{"object":[{"key":"b","value":{"int":"1"}},{"key":"a","value":{"float":"-0.0"}}]}}}""",
      """{"answer":{"rendered":"{\"a\":0,\"b\":1}"},"id":7,"protocol":"1.0"}"""
      """{"id":8,"fn":"wire.render","input":{"value":{"array":[{"float":"NaN"}]}}}""",
      """{"answer":{"refusedNonFinite":{"path":[0],"value":"NaN"}},"id":8,"protocol":"1.0"}"""
      """{"id":9,"fn":"wire.render","input":{"value":{"object":[{"key":"\ud800","value":{"bool":true}}]}}}""",
      """{"answer":{"refusedString":{"isKey":true,"path":["\ud800"],"string":"\ud800"}},"id":9,"protocol":"1.0"}"""
      """{"id":10,"fn":"wire.render","input":{"value":{"object":[{"key":"k","value":{"bool":true}},{"key":"k","value":{"bool":false}}]}}}""",
      """{"answer":{"refusedRepeated":{"key":"k","path":[]}},"id":10,"protocol":"1.0"}"""
      """{"id":11,"fn":"tree.apply","input":{"tree":{"id":"r","kind":"doc","children":[{"id":"a","kind":"s"}]},"op":{"op":"insertChild","parent":"a","node":{"id":"n","kind":"p"}}}}""",
      """{"answer":{"applied":{"children":[{"children":[{"children":[],"id":"n","kind":"p"}],"id":"a","kind":"s"}],"id":"r","kind":"doc"}},"id":11,"protocol":"1.0"}"""
      """{"id":12,"fn":"tree.apply","input":{"tree":{"id":"r","kind":"doc"},"op":{"op":"moveNode","node":"x","newParent":"r"}}}""",
      """{"answer":{"rejected":{"addressable":["r"],"rejection":"unknownNode","target":"x"}},"id":12,"protocol":"1.0"}"""
      """{"id":13,"fn":"chain.verify","input":{"hash":"fnv1a","records":[{"seq":0,"actor":"A","op":"x","prev":"","hash":"zz"}]}}""",
      """{"answer":{"broken":{"expected":"d32b41db","got":"zz","index":0,"reason":"hash mismatch (tampered op/actor/seq)"}},"id":13,"protocol":"1.0"}"""
      """{"id":14,"fn":"chain.verify","input":{"hash":"sha256","records":[]}}""",
      """{"answer":{"intact":{}},"id":14,"protocol":"1.0"}"""
      """{"id":15,"fn":"dag.fold","input":{"base":{"id":"r","kind":"doc"},"lanes":[[{"op":"insertChild","parent":"r","node":{"id":"a","kind":"s"}}],[]]}}""",
      """{"answer":{"folded":{"children":[{"children":[],"id":"a","kind":"s"}],"id":"r","kind":"doc"}},"id":15,"protocol":"1.0"}"""
      """{"id":16,"fn":"dag.fold","input":{"base":{"id":"r","kind":"doc"},"lanes":[[{"op":"insertChild","parent":"r","node":{"id":"a","kind":"s"}}],[{"op":"insertChild","parent":"r","node":{"id":"b","kind":"s"}}]]}}""",
      """{"answer":{"halted":[{"address":"r","left":{"node":{"children":[],"id":"a","kind":"s"},"op":"insertChild","parent":"r"},"right":{"node":{"children":[],"id":"b","kind":"s"},"op":"insertChild","parent":"r"},"shape":"insert-position-clash"}]},"id":16,"protocol":"1.0"}"""
      """{"id":17,"fn":"dag.fold","input":{"base":{"id":"r","kind":"doc"},"lanes":[[{"op":"removeNode","node":"q"}]]}}""",
      """{"answer":{"lanesRejected":[[{"node":"q","op":"removeNode"}]]},"id":17,"protocol":"1.0"}"""
      "not json",
      """{"error":{"code":"malformed-request","message":"not one JSON value: expected 'null' at unit 0"},"id":null,"protocol":"1.0"}"""
      """{"id":18,"fn":"x.y","input":{}}""",
      """{"error":{"code":"unknown-function","message":"no function \"x.y\" in protocol 1.0"},"id":18,"protocol":"1.0"}"""
      """{"id":19,"fn":"tree.apply","input":{"tree":{"id":"r"},"op":{"op":"removeNode","node":"r"}}}""",
      """{"error":{"code":"bad-input","message":"input.tree: missing member \"kind\""},"id":19,"protocol":"1.0"}"""
      """{"id":20,"fn":"tree.apply","input":{},"extra":1}""",
      """{"error":{"code":"malformed-request","message":"undeclared request member \"extra\""},"id":20,"protocol":"1.0"}""" ]

// ---- 3. the conformance pool ----------------------------------------------------------------------

let private pick (r: Random) (xs: 'a list) = xs[r.Next(List.length xs)]

/// A JSON document, often not quite one: the grammar's cases, the parser's refusal triggers
/// (malformed and non-finite numbers, bad and unpaired escapes, raw controls and surrogates, repeated
/// members, null) and a random one-unit edit.
let private genDoc (r: Random) : string =
    let rec value depth =
        match r.Next(if depth > 3 then 5 else 8) with
        | 0 -> string (r.Next(-100000, 100000))
        | 1 ->
            pick
                r
                [ "0.5"
                  "-1.25E3"
                  "1e400"
                  "3000000000"
                  "9007199254740993"
                  "01"
                  "1."
                  "-0"
                  "1E2"
                  "-0.0" ]
        | 2 ->
            "\""
            + (List.init (r.Next 4) (fun _ ->
                pick
                    r
                    [ "a"
                      "\\n"
                      "\\u0041"
                      "\\ud83d\\ude00"
                      "\\ud800"
                      "\u00e9"
                      "\\x"
                      string (char 1)
                      string (char 0xD800) ])
               |> String.concat "")
            + "\""
        | 3 -> pick r [ "true"; "false"; "null" ]
        | 4 -> "\"k\""
        | 5 ->
            "["
            + (List.init (r.Next 4) (fun _ -> value (depth + 1)) |> String.concat ",")
            + "]"
        | _ ->
            "{"
            + (List.init (r.Next 4) (fun _ -> "\"" + pick r [ "a"; "b"; "a"; "kind" ] + "\":" + value (depth + 1))
               |> String.concat ",")
            + "}"

    let doc = value 0

    if r.Next 4 = 0 && doc.Length > 0 then
        let k = r.Next doc.Length

        match r.Next 2 with
        | 0 -> doc.Remove(k, 1)
        | _ -> doc.Insert(k, pick r [ "{"; "]"; ","; "\""; "\\"; " "; "x" ])
    else
        doc

/// A node document for the decoder, right or wrong in one place.
let private genNodeDoc (r: Random) : string =
    let rec node depth =
        match r.Next(if depth > 2 then 3 else 5) with
        | 0 -> sprintf "{\"kind\":\"text\",\"value\":%s}" (pick r [ "\"hi\""; "1"; "true" ])
        | 1 -> sprintf "{\"kind\":\"flag\",\"on\":%s}" (pick r [ "true"; "\"yes\"" ])
        | 2 -> sprintf "{\"kind\":\"tags\",\"tags\":[%s]}" (pick r [ ""; "\"a\",\"b\""; "\"a\",2" ])
        | 3 ->
            sprintf
                "{\"kind\":\"group\",\"id\":\"g\",\"items\":[%s]}"
                (List.init (r.Next 3) (fun _ -> node (depth + 1)) |> String.concat ",")
        | _ ->
            pick
                r
                [ "{\"kind\":\"zzz\"}"
                  "{\"value\":\"x\"}"
                  "[1]"
                  "{\"kind\":3}"
                  "{\"kind\":\"group\",\"id\":\"g\",\"items\":{}}" ]

    node 0

/// Parse settings, as the request member and as the model's arguments.
let private genSettings (r: Random) : (string * CliJson.J) list * JsonParse.policy * int * JsonParse.lims * int option =
    let nulls, pol =
        pick
            r
            [ None, JsonParse.RejectNull
              Some "reject", JsonParse.RejectNull
              Some "eraseMember", JsonParse.EraseMemberNull ]

    let depth = pick r [ None; None; Some 1; Some 2; Some 3 ]
    let str = pick r [ None; None; Some 1; Some 3 ]
    let items = pick r [ None; None; Some 1; Some 2 ]
    let membersCap = pick r [ None; None; Some 1 ]
    let bytes = pick r [ None; None; Some 5; Some 40 ]

    let limits =
        [ for name, v in
              [ "depth", depth
                "stringLength", str
                "arrayLength", items
                "objectMembers", membersCap
                "documentBytes", bytes ] do
              match v with
              | Some n -> yield name, CliJson.ofInt n
              | None -> () ]

    let bound =
        function
        | None -> JsonParse.Unbounded
        | Some n -> JsonParse.Bounded(JsonParseDiff.budget n)

    [ match nulls with
      | Some n -> yield "nulls", CliJson.JStr n
      | None -> ()
      if not limits.IsEmpty then
          yield "limits", CliJson.JObj limits ],
    pol,
    defaultArg depth 512,
    { JsonParse.lstr = bound str
      JsonParse.litems = bound items
      JsonParse.lmembers = bound membersCap },
    bytes

/// The parser model, through the SUITE's bridge (`toChs`, `floatRead`).
let private suiteParse bridge (text: string) (pol, depth: int, lims, bytes: int option) =
    let bytesOk =
        match bytes with
        | None -> true
        | Some cap -> Text.Encoding.UTF8.GetByteCount text <= cap

    JsonParse.parse_limited
        JsonParseDiff.floatRead
        (string depth)
        pol
        (JsonParseDiff.budget depth)
        lims
        bytesOk
        (bridge text)

let private parseAnswer (text: string) (r: JsonParse.jresult) : CliJson.J =
    match r with
    | JsonParse.ROk v -> one "value" (CliMarshal.parsedValue v)
    | _ ->
        match CliMarshal.parseRefusal text r with
        | Some(case, payload) -> one case payload
        | None -> failtest "an accepted parse has no refusal"

/// The renderer model's host record, as the suite's WireCanon differential builds it.
let private suiteWire: WireCanon.wire<int, float> =
    { WireCanon.int_str = fun i -> canonToChs (string i)
      WireCanon.float_str = fun f -> canonToChs (f.ToString("R", inv))
      WireCanon.fclass =
        fun f ->
            if Double.IsNaN f then WireCanon.FNaN
            elif Double.IsPositiveInfinity f then WireCanon.FPosInf
            elif Double.IsNegativeInfinity f then WireCanon.FNegInf
            else WireCanon.FFinite
      WireCanon.is_zero = fun f -> f = 0.0
      WireCanon.pos_zero = 0.0
      WireCanon.key_le = fun a b -> String.CompareOrdinal(canonFromChs a, canonFromChs b) <= 0
      WireCanon.tok_read = fun _ -> WireCanon.Error "not read here"
      WireCanon.str_ok = fun s -> strictlyEncodable (canonFromChs s) }

/// A renderer value and its tagged request form, drawn together.
let private genCanon (r: Random) : CliJson.J * WireCanon.jval<int, float> =
    let tag case v = CliJson.JObj [ case, v ]

    let rec value depth =
        match r.Next(if depth > 2 then 4 else 6) with
        | 0 ->
            let s =
                pick
                    r
                    [ "a"
                      "\u00e9"
                      string (char 0xD800)
                      "x" + string (char 0xDC00)
                      "\n"
                      "\"q\"" ]

            tag "string" (CliJson.JStr s), WireCanon.JStr(canonToChs s)
        | 1 ->
            let i = r.Next(-1000, 1000)
            tag "int" (CliJson.JStr(string i)), WireCanon.JInt i
        | 2 ->
            let token, f =
                pick
                    r
                    [ "0.5", 0.5
                      "-0.0", -0.0
                      "1E300", 1E300
                      "100", 100.0
                      "NaN", Double.NaN
                      "Infinity", Double.PositiveInfinity
                      "-Infinity", Double.NegativeInfinity ]

            tag "float" (CliJson.JStr token), WireCanon.JFloat f
        | 3 ->
            let b = r.Next 2 = 0
            tag "bool" (CliJson.JBool b), WireCanon.JBool b
        | 4 ->
            let items = List.init (r.Next 4) (fun _ -> value (depth + 1))
            tag "array" (CliJson.JArr(List.map fst items)), WireCanon.JArr(List.map snd items)
        | _ ->
            let fields =
                List.init (r.Next 4) (fun _ -> pick r [ "a"; "b"; "a"; string (char 0xD800) ], value (depth + 1))

            tag
                "object"
                (CliJson.JArr(
                    fields
                    |> List.map (fun (k, (j, _)) -> CliJson.JObj [ "key", CliJson.JStr k; "value", j ])
                )),
            WireCanon.JObj(fields |> List.map (fun (k, (_, m)) -> canonToChs k, m))

    value 0

let private renderAnswer (g: WireCanon.guarded<float>) : CliJson.J =
    let path (p: WireCanon.pstep list) =
        CliJson.JArr(
            p
            |> List.map (fun s ->
                match s with
                | WireCanon.PItem i -> CliJson.JNum(i.ToString(inv))
                | WireCanon.PMember k -> CliJson.JStr(canonFromChs k))
        )

    match g with
    | WireCanon.Rendered chs -> one "rendered" (CliJson.JStr(canonFromChs chs))
    | WireCanon.Refused(p, f) ->
        one
            "refusedNonFinite"
            (CliJson.JObj
                [ "path", path p
                  "value",
                  CliJson.JStr(
                      if Double.IsNaN f then "NaN"
                      elif f > 0.0 then "Infinity"
                      else "-Infinity"
                  ) ])
    | WireCanon.RefusedString(p, s, isKey) ->
        one
            "refusedString"
            (CliJson.JObj
                [ "path", path p
                  "string", CliJson.JStr(canonFromChs s)
                  "isKey", CliJson.JBool isKey ])
    | WireCanon.RefusedRepeated(p, k) ->
        one "refusedRepeated" (CliJson.JObj [ "path", path p; "key", CliJson.JStr(canonFromChs k) ])

let private kinds = [ "doc"; "sec"; "para" ]

/// A tree over the ids `root` and `n1`..; now and then an id repeats, which the checked apply
/// refuses and the plain apply does not look for.
let private genTree (r: Random) : TreeOps.tree =
    let mutable next = 0

    let rec node depth =
        let id =
            if r.Next 15 = 0 then
                "n1"
            else
                next <- next + 1
                sprintf "n%d" next

        let kids =
            if depth >= 2 then
                []
            else
                List.init (r.Next 3) (fun _ -> node (depth + 1))

        TreeOps.TNode(id, pick r kinds, kids)

    TreeOps.TNode("root", "doc", List.init (r.Next 4) (fun _ -> node 1))

let private idPool = [ "root"; "n1"; "n2"; "n3"; "n4"; "n5"; "n6"; "zz" ]

let rec private genOp (r: Random) (tree: TreeOps.tree) (depth: int) : TreeOps.op =
    match r.Next(if depth > 0 then 5 else 6) with
    | 0 ->
        let fresh = pick r [ sprintf "f%d" (r.Next 1000); "n2" ]
        TreeOps.InsertChild(pick r idPool, TreeOps.TNode(fresh, pick r kinds, []))
    | 1 -> TreeOps.RemoveNode(pick r idPool)
    | 2 -> TreeOps.MoveNode(pick r idPool, pick r idPool)
    | 3 ->
        let parent = pick r idPool

        let kids =
            match TreeOps.find_in parent tree with
            | FStar_Pervasives_Native.Some t -> TreeOps.kid_ids (TreeOps.kids_of t)
            | FStar_Pervasives_Native.None -> [ "n1" ]

        let order = kids |> List.sortBy (fun _ -> r.Next())
        TreeOps.ReorderChildren(parent, (if r.Next 4 = 0 then List.truncate 1 order else order))
    | 4 -> TreeOps.UpdateNode(TreeOps.TNode(pick r idPool, pick r kinds, []))
    | _ -> TreeOps.Batch(List.init (1 + r.Next 2) (fun _ -> genOp r tree (depth + 1)))


/// A chain built by the model under PRODUCTION's hash, then perhaps tampered once.
let private genChain (r: Random) : (string * CliJson.J) list * CliJson.J =
    let hashName, h =
        pick r [ "fnv1a", OpStream.defaultHash; "sha256", OpStream.sha256Hash ]

    let steps =
        List.init (r.Next 5) (fun _ ->
            { Chain.cactor = pick r [ "A"; "B"; "{\"kind\":\"human\",\"id\":\"x\"}" ]
              Chain.cop = pick r [ "x"; "y"; "{\"op\":1}"; "\u00e9" ] })

    let records = Chain.build_chain h showPos id "" Chain.PZero steps

    let records =
        if records.IsEmpty || r.Next 3 = 0 then
            records
        else
            let k = r.Next records.Length

            records
            |> List.mapi (fun i (x: Chain.record<string>) ->
                if i <> k then
                    x
                else
                    match r.Next 4 with
                    | 0 -> { x with rseq = Chain.PSucc x.rseq }
                    | 1 -> { x with rprev = "p" }
                    | 2 -> { x with rhash = "h" }
                    | _ -> { x with rop = "z" })

    let genesis = if r.Next 8 = 0 then "g" else ""

    let input =
        [ "hash", CliJson.JStr hashName
          if genesis <> "" then
              "genesis", CliJson.JStr genesis
          "records",
          CliJson.JArr(
              records
              |> List.map (fun x ->
                  CliJson.JObj
                      [ "seq", CliJson.ofInt (intOfPos x.rseq)
                        "actor", CliJson.JStr x.ractor
                        "op", CliJson.JStr x.rop
                        "prev", CliJson.JStr x.rprev
                        "hash", CliJson.JStr x.rhash ])
          ) ]

    let answer =
        match Chain.first_chain_break h showPos id genesis records with
        | Chain.Missing -> one "intact" (CliJson.JObj [])
        | Chain.Found b ->
            one
                "broken"
                (CliJson.JObj
                    [ "index", CliJson.ofInt (intOfPos b.cindex)
                      "reason", CliJson.JStr b.creason
                      "expected", CliJson.JStr b.cexpected
                      "got", CliJson.JStr b.cgot ])

    input, answer

/// The pool: per draw, a function name, its input, the oracle's answer called in-process, and the
/// same parse answered through the BLIND bridge where the function parses (the go-red instrument).
let private pool (seed: int) (draws: int) : (string * CliJson.J * CliJson.J * CliJson.J option) list =
    let r = Random(seed)

    [ for _ in 1..draws do
          // json.parse
          let text = genDoc r
          let members, pol, depth, lims, bytes = genSettings r
          let settings = pol, depth, lims, bytes
          let input = CliJson.JObj(("text", CliJson.JStr text) :: members)

          yield
              "json.parse",
              input,
              parseAnswer text (suiteParse JsonParseDiff.toChs text settings),
              Some(parseAnswer text (suiteParse JsonParseDiff.toChsBlind text settings))

          // wire.decode
          let text = if r.Next 3 = 0 then genDoc r else genNodeDoc r
          let members, pol, depth, lims, bytes = genSettings r
          let parsed = suiteParse JsonParseDiff.toChs text (pol, depth, lims, bytes)

          let answer =
              match parsed with
              | JsonParse.ROk v ->
                  match WireDecode.decode_node (CliMarshal.toDecodeValue v) with
                  | WireDecode.Ok node -> one "node" (CliMarshal.ofDecodeValue (WireDecode.encode node))
                  | WireDecode.Error m -> one "decodeRefused" (CliJson.JObj [ "message", CliJson.JStr m ])
              | _ -> parseAnswer text parsed

          yield "wire.decode", CliJson.JObj(("text", CliJson.JStr text) :: members), answer, None

          // wire.render
          let tagged, value = genCanon r

          yield
              "wire.render", CliJson.JObj [ "value", tagged ], renderAnswer (WireCanon.try_render suiteWire value), None

          // tree.apply
          let tree = genTree r
          let op = genOp r tree 0

          let answer =
              match TreeOps.apply op tree with
              | DagFold.Ok t -> one "applied" (CliMarshal.ofTree t)
              | DagFold.Error e -> one "rejected" (CliMarshal.ofRejection e)

          yield "tree.apply", CliJson.JObj [ "tree", CliMarshal.ofTree tree; "op", CliMarshal.ofOp op ], answer, None

          // chain.verify
          let input, answer = genChain r
          yield "chain.verify", CliJson.JObj input, answer, None

          // dag.fold
          let baseTree = genTree r

          let lanes =
              List.init (1 + r.Next 3) (fun _ -> List.init (r.Next 3) (fun _ -> genOp r baseTree 1))

          let answer =
              match DagFold.fold_once TreeOps.wapply TreeOps.op_fp baseTree lanes with
              | DagFold.LaneFolded t -> one "folded" (CliMarshal.ofTree t)
              | DagFold.LaneHalted cs -> one "halted" (CliJson.JArr(cs |> List.map CliMarshal.ofConflict))
              | DagFold.LaneRejected e -> one "rejected" (CliMarshal.ofRejection e)
              | DagFold.LanesRejected ls ->
                  one
                      "lanesRejected"
                      (CliJson.JArr(ls |> List.map (fun l -> CliJson.JArr(l |> List.map CliMarshal.ofOp))))

          yield
              "dag.fold",
              CliJson.JObj
                  [ "base", CliMarshal.ofTree baseTree
                    "lanes", CliJson.JArr(lanes |> List.map (fun l -> CliJson.JArr(l |> List.map CliMarshal.ofOp))) ],
              answer,
              None ]

/// Every answer case each function's pool must reach, or the comparison over that case is vacuous.
let private mustReach =
    [ "json.parse", [ "value"; "parseRefused"; "parseLimit" ]
      "wire.decode", [ "node"; "decodeRefused"; "parseRefused"; "parseLimit" ]
      "wire.render", [ "rendered"; "refusedNonFinite"; "refusedString"; "refusedRepeated" ]
      "tree.apply", [ "applied"; "rejected" ]
      "chain.verify", [ "intact"; "broken" ]
      // Not `rejected`: at this instantiation the fold model halts every pair of lanes whose merged
      // script would be refused (a 200,000-draw search, including the model's own `cross_a` /
      // `cross_b` witness, found none that folds and then refuses), so the case is the model's and
      // the protocol carries it, and no tree input reaches it.
      "dag.fold", [ "folded"; "halted"; "lanesRejected" ] ]

[<Tests>]
let tests =
    testList
        "Oracle CLI"
        [ testCase
              "the CLI reaches the oracle assembly and nothing else of this repository, and the check is red on a planted reference"
          <| fun _ ->
              let root = Repo.root ()
              let cliDir = Path.Combine(root, "tools", "Fuaran.Core.Oracle.Cli")
              let project = Path.Combine(cliDir, "Fuaran.Core.Oracle.Cli.fsproj")

              let oracle =
                  Path.Combine(root, "proofs", "oracle", "Fuaran.Core.Proofs.Oracle.fsproj")

              let items = evaluated project

              let sources =
                  items["Compile"] |> List.map (fun f -> Path.GetFileName f, File.ReadAllText f)

              let findings =
                  cliFindings
                      cliDir
                      oracle
                      items["ProjectReference"]
                      items["PackageReference"]
                      items["Reference"]
                      items["Compile"]
                      sources

              Expect.isEmpty findings (sprintf "the CLI reaches beyond the oracle:\n%s" (String.concat "\n" findings))
              Expect.equal (items["ProjectReference"].Length) 1 "the CLI references exactly one project, the oracle"
              Expect.isGreaterThanOrEqual sources.Length 4 "the guard read the CLI's sources"

              let wire = Path.Combine(root, "src", "Fuaran.Core.Wire", "Fuaran.Core.Wire.fsproj")

              let planted =
                  cliFindings
                      cliDir
                      oracle
                      (wire :: items["ProjectReference"])
                      ("System.Text.Json" :: items["PackageReference"])
                      [ "lib.dll" ]
                      (Path.Combine(root, "src", "Fuaran.Core.Wire", "Json.fs") :: items["Compile"])
                      (("Planted.fs", "open Fuaran.Core.Wire\nlet x = Fuaran.Core.Json.read")
                       :: sources)

              Expect.equal
                  planted.Length
                  6
                  (sprintf "every planted violation is named:\n%s" (String.concat "\n" planted))

          testCase "the protocol's golden lines: one per function and answer case, and one per error code"
          <| fun _ ->
              let expected =
                  golden
                  |> List.map (fun (_, resp) ->
                      resp.Replace("\"ORACLE\"", "\"" + Fuaran.Core.Oracle.Cli.Protocol.oracleVersion + "\""))

              let actual = runCli (golden |> List.map fst)
              Expect.equal actual.Length expected.Length "one response line per request line"

              for (req, _), want, got in List.zip3 golden expected actual do
                  Expect.equal got want (sprintf "the response to %s" req)

          testCase
              "the CLI process answers a seeded pool exactly as the oracle called in-process, over every answer case"
          <| fun _ ->
              let draws = onBigStack (fun () -> pool 482 150)
              let requests = draws |> List.mapi (fun k (fn, input, _, _) -> request k fn input)
              let actual = runCli requests
              Expect.equal actual.Length draws.Length "one response line per request line"

              let disagreements =
                  [ for k, ((fn, input, answer, _), got) in List.indexed (List.zip draws actual) do
                        let want = line k answer

                        if got <> want then
                            yield sprintf "%s %s\n  oracle: %s\n  CLI:    %s" fn (CliJson.write input) want got ]

              Expect.isEmpty
                  disagreements
                  (sprintf
                      "%d of %d disagree; the first:\n%s"
                      disagreements.Length
                      draws.Length
                      (disagreements |> List.truncate 3 |> String.concat "\n"))

              for fn, cases in mustReach do
                  let reached =
                      draws
                      |> List.filter (fun (f, _, _, _) -> f = fn)
                      |> List.map (fun (_, _, a, _) -> caseOf a)
                      |> Set.ofList

                  for case in cases do
                      Expect.isTrue
                          (reached.Contains case)
                          (sprintf "%s's pool reached the %s answer (reached %A)" fn case reached)

              // The go-red: the same parses answered through the BLIND bridge, which hides the
              // container characters from the model. The comparison must lose on them.
              let lost =
                  [ for k, ((_, _, _, blind), got) in List.indexed (List.zip draws actual) do
                        match blind with
                        | Some b when line k b <> got -> yield k
                        | _ -> () ]

              Expect.isGreaterThan
                  lost.Length
                  30
                  (sprintf "the comparison loses against the blind bridge (%d lines)" lost.Length) ]
