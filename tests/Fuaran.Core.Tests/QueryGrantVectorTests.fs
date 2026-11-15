module Fuaran.Core.Tests.QueryGrantVectorTests

open System.IO
open Expecto
open Fuaran.Core

// ---------------------------------------------------------------------------
//  Phase 651 — the query grant family (`laws/query-grant-laws.json`, DECISIONS.md D261.8) read back the
//  way a port reads it: the queries are decoded from the FILE's head, the arguments from each vector's
//  cells, and every vector is answered twice — by production (`Query.invocationPreimage`,
//  `Grant.covers`), and by a reading of the file's own description that calls no Core function for the
//  identity, so a description that no longer says how the identity is written fails here. That the
//  committed bytes are the emitter's is the laws row of `ExportersTests`.
// ---------------------------------------------------------------------------

let private field (name: string) (el: JVal) : JVal option =
    match el with
    | JObj ms -> ms |> List.tryPick (fun (k, v) -> if k = name then Some v else None)
    | _ -> None

let private text (name: string) (el: JVal) : string =
    match field name el with
    | Some(JStr s) -> s
    | other -> failwithf "`%s` is not a string: %A" name other

let private items (name: string) (el: JVal) : JVal list =
    match field name el with
    | Some(JArr xs) -> xs
    | other -> failwithf "`%s` is not an array: %A" name other

/// A cell as the file spells it: its kind and its text.
let private cellOf (el: JVal) : Cell =
    let t () = text "text" el

    match text "kind" el with
    | "null" -> Null
    | "int" -> Int(int (t ()))
    | "float" -> Float(System.Double.Parse(t (), System.Globalization.CultureInfo.InvariantCulture))
    | "bool" -> Bool(t () = "true")
    | "string" -> Str(t ())
    | "date" -> Date(t ())
    | "timestamp" -> Timestamp(t ())
    | "decimal" -> Decimal(t ())
    | other -> failwithf "unknown cell kind %s" other

let private argsOf (el: JVal) : (string * Cell) list =
    items "args" el
    |> List.map (fun a ->
        match field "cell" a with
        | Some c -> text "name" a, cellOf c
        | None -> failwith "a binding without a cell")

let private grantOf (el: JVal) : Grant option =
    match field "grant" el with
    | Some(JStr g) -> Some(Grant.ofInvocation g)
    | None -> None
    | other -> failwithf "`grant` is not a string: %A" other

/// The identity as the description states it, from the declaration's JSON and the file's cells — no
/// Core function computes any of it: the id, the filter and order triples, then each binding's name,
/// tag and payload, name-sorted, each field escaped and terminated.
let private describedIdentity (declaration: JVal) (args: JVal list) : string =
    let escape (f: string) =
        (f
         |> String.collect (fun ch ->
             if ch = '\u0010' || ch = '\u0001' then
                 "\u0010" + string ch
             else
                 string ch))
        + "\u0001"

    let shape (memberName: string) (tag: string) =
        match field memberName declaration with
        | Some(JArr(_ :: _) as v) -> [ ""; tag; Canonical.write Rendering.CrossHost v ]
        | _ -> []

    let binding (a: JVal) =
        let cell =
            match field "cell" a with
            | Some c -> c
            | None -> failwith "a binding without a cell"

        let tag, payload =
            match text "kind" cell with
            | "null" -> "n", ""
            | "int" -> "i", text "text" cell
            | "float" -> "f", text "text" cell
            | "bool" -> "b", (if text "text" cell = "true" then "1" else "0")
            | "string" -> "s", text "text" cell
            | "date" -> "d", text "text" cell
            | "timestamp" -> "t", text "text" cell
            | "decimal" -> "m", text "text" cell
            | other -> failwithf "unknown cell kind %s" other

        text "name" a, [ text "name" a; tag; payload ]

    let bindings =
        args
        |> List.map binding
        |> List.sortWith (fun (x, _) (y, _) -> System.String.CompareOrdinal(x, y))
        |> List.collect snd

    (text "id" declaration :: shape "where" "w" @ shape "orderBy" "o" @ bindings)
    |> List.map escape
    |> String.concat ""

/// Every vector's verdict: `None` when production and the described reading both answer what the file
/// expects, the disagreement otherwise.
let private check (file: JVal) : (string * string) list =
    let declarations =
        items "registry" file
        |> List.map (fun d ->
            match d with
            | JStr s ->
                match QueryCodec.decode s, Json.parse s with
                | Ok q, Ok j -> q.Id, (q, j)
                | e, _ -> failwithf "a registry declaration does not decode: %A" e
            | other -> failwithf "a registry entry is not a string: %A" other)
        |> Map.ofList

    [ for v in items "vectors" file do
          let id = text "id" v
          let input = field "input" v |> Option.get
          let expected = field "expected" v |> Option.get
          let q, j = declarations[text "query" input]
          let args = argsOf input
          let identity = Query.invocationPreimage q args

          match text "case" v with
          | "invocationIdentity" ->
              let want = text "identity" expected

              if identity <> want then
                  yield id, sprintf "production's identity %A is not the file's %A" identity want

              let described = describedIdentity j (items "args" input)

              if described <> want then
                  yield id, sprintf "the description's identity %A is not the file's %A" described want
          | "grantCovers" ->
              let want =
                  match field "covers" expected with
                  | Some(JBool b) -> b
                  | other -> failwithf "`covers` is not a boolean: %A" other

              let got = Grant.covers identity (Option.toList (grantOf input))

              if got <> want then
                  yield id, sprintf "production covers %b, the file says %b" got want

              let byText =
                  match grantOf input with
                  | Some g -> g.Invocation = describedIdentity j (items "args" input)
                  | None -> false

              if byText <> want then
                  yield id, sprintf "the description covers %b, the file says %b" byText want
          | other -> yield id, sprintf "unknown case %s" other ]

let private committed () : JVal =
    let path =
        Path.Combine(OwnedConformance.root (), "laws", LawVectorExport.QueryGrants.fileName)

    match Json.parse (File.ReadAllText path) with
    | Ok j -> j
    | Error e -> failwithf "%s does not parse: %A" path e

[<Tests>]
let tests =
    testList
        "Phase 651 — the query grant family"
        [ testCase "the reference reads its own query grant family back and answers every vector as the file says"
          <| fun _ -> Expect.isEmpty (check (committed ())) "every vector agrees, by production and by the description"

          testCase
              "the family reaches both cases, both answers, every cell kind, a filter and an order, and a later page"
          <| fun _ ->
              let file = committed ()
              let vectors = items "vectors" file
              let cases = vectors |> List.map (text "case") |> Set.ofList
              Expect.equal cases (set [ "grantCovers"; "invocationIdentity" ]) "both cases"

              let answers =
                  vectors
                  |> List.choose (fun v ->
                      match field "expected" v |> Option.bind (field "covers") with
                      | Some(JBool b) -> Some b
                      | _ -> None)
                  |> Set.ofList

              Expect.equal answers (set [ true; false ]) "a grant that covers and one that does not"

              let kinds =
                  vectors
                  |> List.collect (fun v -> items "args" (field "input" v |> Option.get))
                  |> List.choose (field "cell")
                  |> List.map (text "kind")
                  |> Set.ofList

              Expect.equal
                  kinds
                  (set [ "bool"; "date"; "decimal"; "float"; "int"; "null"; "string"; "timestamp" ])
                  "every cell kind"

              Expect.isTrue
                  (items "registry" file
                   |> List.exists (fun d ->
                       match d with
                       | JStr s -> s.Contains "\"where\":[{" && s.Contains "\"orderBy\":[{"
                       | _ -> false))
                  "a declaration with a filter and an order"

              Expect.isTrue
                  (vectors
                   |> List.exists (fun v -> field "input" v |> Option.bind (field "pageToken") |> Option.isSome))
                  "a read dispatched for a later page"

          testCase "the family's checker names a perturbed vector — the read-back can go red"
          <| fun _ ->
              let perturbed =
                  match committed () with
                  | JObj ms ->
                      ms
                      |> List.map (fun (k, v) ->
                          match k, v with
                          | "vectors", JArr(first :: rest) ->
                              let bent =
                                  match first with
                                  | JObj fs ->
                                      fs
                                      |> List.map (fun (fk, fv) ->
                                          if fk = "expected" then
                                              fk, JObj [ "identity", JStr "ledger\u0001n\u0001i\u00016\u0001" ]
                                          else
                                              fk, fv)
                                      |> JObj
                                  | other -> other

                              k, JArr(bent :: rest)
                          | _ -> k, v)
                      |> JObj
                  | other -> other

              let found = check perturbed

              Expect.equal
                  (found |> List.map fst |> List.distinct)
                  [ "query-identity-ledger" ]
                  "the bent vector is named"

              Expect.equal found.Length 2 "by production and by the description" ]
