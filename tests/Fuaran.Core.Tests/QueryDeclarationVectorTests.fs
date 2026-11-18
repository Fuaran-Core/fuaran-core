module Fuaran.Core.Tests.QueryDeclarationVectorTests

open System.IO
open Expecto
open Microsoft.FSharp.Reflection
open Fuaran.Core

// ---------------------------------------------------------------------------
//  Phase 680 — the query declaration family (`laws/query-declaration-laws.json`, DECISIONS.md D263),
//  read back the way a port reads it: the registry is decoded from the FILE's head, the gate from its
//  table, and each vector's declaration, arguments and grant from the vector, never from the exporter's
//  values, and every vector is answered again by production. That the committed bytes are the emitter's
//  is the laws row of `ExportersTests`.
//
//  The census: every case of the closed `QueryError` union is either raised by a vector of the family
//  or named in its `unreached` list with the reason no declaration and no typed call raises it
//  (D263.6). The cases are read off the union by reflection, so a case added to `QueryError` is red
//  here until it is given a vector or a reason.
// ---------------------------------------------------------------------------

let private fileName = "query-declaration-laws.json"

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
    | None -> []
    | other -> failwithf "`%s` is not an array: %A" name other

/// Every case of `QueryError` as its wire tag, by D163's naming rule (`QueryTests.tagOf528`): the
/// case's name with its first letter lowered, a prefix such as `query…` kept.
let unionTags: Set<string> =
    FSharpType.GetUnionCases(typeof<QueryError>)
    |> Array.map (fun c -> QueryTests.tagOf528 c.Name)
    |> Set.ofArray

/// The committed family, or `None` when the file does not exist.
let private committed () : JVal option =
    let path = Path.Combine(OwnedConformance.root (), "laws", fileName)

    if File.Exists path then
        match Json.parse (File.ReadAllText path) with
        | Ok j -> Some j
        | Error e -> failwithf "%s does not parse: %A" path e
    else
        None

/// The census's findings over a family file (`None`: no file): each case of the union with neither a
/// vector raising it nor a recorded reason, or with both; each `unreached` entry without a reason or
/// naming no case; and each refusal a vector raises that names no case.
let censusFindings (file: JVal option) : string list =
    let vectors = file |> Option.map (items "vectors") |> Option.defaultValue []

    let raised =
        vectors
        |> List.choose (fun v -> field "expected" v |> Option.bind (field "error") |> Option.map (text "$type"))
        |> Set.ofList

    let unreached =
        file
        |> Option.map (items "unreached")
        |> Option.defaultValue []
        |> List.map (fun u -> text "error" u, (field "reason" u |> Option.map (fun _ -> text "reason" u)))

    [ if Option.isNone file then
          yield sprintf "laws/%s does not exist" fileName
      for t in unionTags do
          let inVectors = Set.contains t raised
          let named = unreached |> List.exists (fun (e, _) -> e = t)

          if not inVectors && not named then
              yield sprintf "`%s`: no vector raises it and no reason is recorded" t
          elif inVectors && named then
              yield sprintf "`%s`: a vector raises it and `unreached` names it" t
      for e, reason in unreached do
          if not (Set.contains e unionTags) then
              yield sprintf "`unreached` names `%s`, which is no case of QueryError" e

          match reason with
          | Some r when r.Trim() <> "" -> ()
          | _ -> yield sprintf "`unreached` names `%s` without a reason" e
      for t in raised do
          if not (Set.contains t unionTags) then
              yield sprintf "a vector raises `%s`, which is no case of QueryError" t ]

// ---- reading the file as a port reads it ----

let private file () : JVal =
    match committed () with
    | Some j -> j
    | None -> failwithf "laws/%s does not exist" fileName

/// A cell as the file spells it: its kind and its text, as it stands (a non-finite float's text is its
/// token in quotes, as the canonical float layout writes it).
let private cellOf (el: JVal) : Cell =
    let t () = text "text" el

    match text "kind" el with
    | "null" -> Null
    | "int" -> Int(int (t ()))
    | "float" -> Float(System.Double.Parse((t ()).Trim('"'), System.Globalization.CultureInfo.InvariantCulture))
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

/// The registry's declarations, read from the head by the declaration reader.
let private registryOf (j: JVal) : Query list =
    items "registry" j
    |> List.map (fun d ->
        match d with
        | JStr t ->
            match QueryCodec.decode t with
            | Ok q -> q
            | Error e -> failwithf "a declaration of the head is refused: %s" e
        | other -> failwithf "a registry entry is not a string: %A" other)

/// The head's gate table, as the registry the vectors dispatch through.
let private registryWithGates (j: JVal) : QueryRegistry<unit> =
    let registered =
        registryOf j
        |> List.fold (fun r q -> r |> Result.bind (QueryRegistry.register q)) (Ok QueryRegistry.empty)
        |> Result.defaultWith (fun e -> failwithf "the head's registry did not build: %A" e)

    items "gates" j
    |> List.fold
        (fun r g ->
            let rules =
                items "rules" g
                |> List.map (fun rule ->
                    let decision =
                        match text "decision" rule with
                        | "allow" -> PolicyDecision.Allow
                        | "needsApproval" -> PolicyDecision.NeedsApproval
                        | "deny" ->
                            PolicyDecision.denyWith
                                (text "message" rule)
                                (items "alternatives" rule
                                 |> List.map (function
                                     | JStr s -> s
                                     | other -> failwithf "an alternative is not a string: %A" other))
                        | other -> failwithf "unknown decision %s" other

                    text "query" rule, decision)

            r
            |> QueryRegistry.withGate
                { Policy = text "policy" g
                  Decide =
                    fun () q _ ->
                        rules
                        |> List.tryPick (fun (id, d) -> if id = q.Id then Some d else None)
                        |> Option.defaultValue PolicyDecision.Allow })
        registered

let private columnType (tag: string) : ColumnType =
    match ColumnType.ofTag tag with
    | Some t -> t
    | None -> failwithf "unknown column type %s" tag

let private bool (name: string) (el: JVal) : bool =
    match field name el with
    | Some(JBool b) -> b
    | other -> failwithf "`%s` is not a boolean: %A" name other

let private predicateOf (el: JVal) : ColumnPredicate =
    let c = text "column" el

    let cell () =
        match field "cell" el with
        | Some v -> cellOf v
        | None -> failwith "a comparison without a cell"

    match text "predicate" el with
    | "equalTo" -> ColumnPredicate.EqualTo(c, cell ())
    | "greaterThan" -> ColumnPredicate.GreaterThan(c, cell ())
    | "atLeast" -> ColumnPredicate.AtLeast(c, cell ())
    | "lessThan" -> ColumnPredicate.LessThan(c, cell ())
    | "atMost" -> ColumnPredicate.AtMost(c, cell ())
    | "contains" -> ColumnPredicate.Contains(c, text "text" el)
    | "isNull" -> ColumnPredicate.IsNull c
    | "isNotNull" -> ColumnPredicate.IsNotNull c
    | other -> failwithf "unknown predicate %s" other

/// The declaration a vector states: a declaration of the head, with the members the input gives
/// replacing its own.
let private statedOf (registry: Query list) (input: JVal) : Query =
    let b = registry |> List.find (fun q -> q.Id = text "query" input)

    let replaced name read fallback =
        match field name input with
        | Some _ -> read ()
        | None -> fallback

    { b with
        Id = replaced "id" (fun () -> text "id" input) b.Id
        Params =
            replaced
                "params"
                (fun () ->
                    items "params" input
                    |> List.map (fun p ->
                        { Name = text "name" p
                          Type = columnType (text "type" p)
                          Required = bool "required" p }))
                b.Params
        Where = replaced "where" (fun () -> items "where" input |> List.map predicateOf) b.Where
        OrderBy =
            replaced
                "orderBy"
                (fun () ->
                    items "orderBy" input
                    |> List.map (fun k ->
                        { Column = text "column" k
                          Direction =
                            match text "direction" k with
                            | "ascending" -> SortDirection.Ascending
                            | "descending" -> SortDirection.Descending
                            | other -> failwithf "unknown direction %s" other }))
                b.OrderBy }

let private canonical (v: JVal) = Canonical.write Rendering.CrossHost v

let private errorText (e: QueryError) =
    canonical (QuerySeamCodec.encodeQueryError e)

let private segText (s: PathSegment) =
    match s with
    | PathSegment.Key k -> k
    | PathSegment.Index i -> string i

let private strings (name: string) (el: JVal) : string list =
    items name el
    |> List.map (function
        | JStr s -> s
        | other -> failwithf "`%s` holds a non-string: %A" name other)

/// Every disagreement between the file and production, as (vector id, what differs).
let private check (j: JVal) : (string * string) list =
    let registry = registryOf j
    let gated = registryWithGates j

    [ for v in items "vectors" j do
          let id = text "id" v
          let input = field "input" v |> Option.get
          let expected = field "expected" v |> Option.get
          let verdict = text "verdict" expected

          let expectedError () =
              field "error" expected |> Option.map canonical

          match text "case" v with
          | "declarationRefused" ->
              match QueryCodec.decodeDetailedWith ReadPolicy.Lenient (text "declaration" input) with
              | Ok _ when verdict = "accept" -> ()
              | Ok _ -> yield id, "production reads the declaration; the file says it is refused"
              | Error e ->
                  let got = string e.Code, e.Path |> List.map segText

                  if verdict <> "reject" || got <> (text "code" expected, strings "path" expected) then
                      yield id, sprintf "production refuses %A; the file says %s" got (canonical expected)
          | "admissionFault" ->
              let q = statedOf registry input

              let got =
                  LawVectorExport.QueryDeclarations.admissionFaultAt q
                  |> Option.map (fun (at, e) -> at |> List.map segText, errorText e)

              let want =
                  match verdict with
                  | "admitted" -> None
                  | _ -> Some(strings "at" expected, Option.get (expectedError ()))

              if got <> want then
                  yield id, sprintf "the gate answers %A; the file says %A" got want

              // `register` runs the same gate: into an empty registry it refuses with the same error.
              match QueryRegistry.register q QueryRegistry.empty<unit>, want with
              | Ok _, None -> ()
              | Error e, Some(_, error) when errorText e = error -> ()
              | other, _ -> yield id, sprintf "register into an empty registry answers %A" (Result.map ignore other)
          | "register" ->
              let got =
                  match QueryRegistry.register (statedOf registry input) gated with
                  | Ok _ -> "registered", None
                  | Error e -> "refused", Some(errorText e)

              if got <> (verdict, expectedError ()) then
                  yield id, sprintf "register answers %A; the file says %s" got (canonical expected)
          | "validateParams" ->
              let q = registry |> List.find (fun q -> q.Id = text "query" input)

              let got =
                  match Query.validateParams q (argsOf input) with
                  | Ok() -> "accept", None
                  | Error e -> "reject", Some(errorText e)

              if got <> (verdict, expectedError ()) then
                  yield id, sprintf "validateParams answers %A; the file says %s" got (canonical expected)
          | "dispatch" ->
              let grant =
                  field "grant" input
                  |> Option.map (function
                      | JStr g -> Grant.ofInvocation g
                      | other -> failwithf "`grant` is not a string: %A" other)

              let mutable ran = 0

              let outcome =
                  QueryRegistry.dispatch gated () grant (text "query" input) (argsOf input) (fun _ ->
                      ran <- ran + 1
                      Deferred.Pending)

              let got =
                  match outcome with
                  | Ok _ -> sprintf "performed %d" ran, None
                  | Error e -> sprintf "refused %d" ran, Some(errorText e)

              let want =
                  match verdict with
                  | "performed" -> "performed 1", None
                  | _ -> "refused 0", expectedError ()

              if got <> want then
                  yield id, sprintf "dispatch answers %A; the file says %s" got (canonical expected)
          | other -> yield id, sprintf "unknown case %s" other ]

/// Every admission vector whose declaration's TEXT reaches the gate (no `Null` literal, and not one of
/// the vectors `referenceOnly` names for a literal its column cannot carry), read again as text: the
/// reader refuses it `OutOfRange` at the gate's path with the gate's sentence, or reads it when the gate
/// admits it. The two readers of one declaration disagree nowhere (D263.1).
let private readerAgainstGate (j: JVal) : (string * string) list =
    let registry = registryOf j

    let uncarried =
        items "referenceOnly" j
        |> List.filter (fun r -> text "case" r = "admissionFault")
        |> List.collect (strings "vectors")
        |> Set.ofList

    [ for v in items "vectors" j do
          let id = text "id" v

          if text "case" v = "admissionFault" && not (Set.contains id uncarried) then
              let q = statedOf registry (field "input" v |> Option.get)

              let carriesNull =
                  q.Where
                  |> List.exists (function
                      | ColumnPredicate.EqualTo(_, Null)
                      | ColumnPredicate.GreaterThan(_, Null)
                      | ColumnPredicate.AtLeast(_, Null)
                      | ColumnPredicate.LessThan(_, Null)
                      | ColumnPredicate.AtMost(_, Null) -> true
                      | _ -> false)

              if not carriesNull then
                  let expected = field "expected" v |> Option.get

                  match
                      QueryCodec.decodeDetailedWith ReadPolicy.Lenient (QueryCodec.encode q), text "verdict" expected
                  with
                  | Ok _, "admitted" -> ()
                  | Error e, "refused" ->
                      let sentence =
                          match QuerySeamCodec.decodeQueryError (field "error" expected |> Option.get) with
                          | Ok err -> QueryError.describe err
                          | Error d -> failwithf "%s: the file's error does not read: %A" id d

                      if
                          e.Code <> DecodeCode.OutOfRange
                          || (e.Path |> List.map segText) <> strings "at" expected
                          || not (e.Message.Contains sentence)
                      then
                          yield id, sprintf "the reader refuses %A at %A (%s)" e.Code e.Path e.Message
                  | r, want -> yield id, sprintf "the reader answers %A; the gate %s" (Result.map ignore r) want ]

[<Tests>]
let tests =
    testList
        "Phase 680 — the query declaration family"
        [ testCase "the census: every case of QueryError has a vector or a recorded reason, never both"
          <| fun _ ->
              match censusFindings (committed ()) with
              | [] -> ()
              | findings -> failtestf "the census finds %d gaps:\n%s" findings.Length (String.concat "\n" findings)

          testCase "the reference reads its own query declaration family back and answers every vector as the file says"
          <| fun _ -> Expect.isEmpty (check (file ())) "every vector agrees with production"

          testCase "the reader and the gate agree on every declaration whose text reaches the gate"
          <| fun _ ->
              Expect.isEmpty (readerAgainstGate (file ())) "the reader refuses where the gate refuses, at its path"

          testCase "the family reaches every case, both verdicts of each, and every refusal the gate checks, in order"
          <| fun _ ->
              let vectors = items "vectors" (file ())

              let verdicts =
                  vectors
                  |> List.map (fun v -> text "case" v, text "verdict" (field "expected" v |> Option.get))
                  |> Set.ofList

              Expect.equal
                  verdicts
                  (set
                      [ "declarationRefused", "accept"
                        "declarationRefused", "reject"
                        "admissionFault", "admitted"
                        "admissionFault", "refused"
                        "register", "registered"
                        "register", "refused"
                        "validateParams", "accept"
                        "validateParams", "reject"
                        "dispatch", "performed"
                        "dispatch", "refused" ])
                  "every case, both ways"

              let gateErrors =
                  vectors
                  |> List.filter (fun v -> text "case" v = "admissionFault")
                  |> List.choose (fun v ->
                      field "expected" v |> Option.bind (field "error") |> Option.map (text "$type"))
                  |> Set.ofList

              Expect.equal
                  gateErrors
                  (set
                      [ "duplicateParam"
                        "unknownColumn"
                        "predicateNotApplicable"
                        "illFormedLiteral"
                        "predicateTypeMismatch"
                        "duplicateSortColumn" ])
                  "every refusal of the admission gate"

          testCase
              "a vector names the query model beside the reference exactly where the file does not name it reference-only"
          <| fun _ ->
              let j = file ()

              let referenceOnly =
                  items "referenceOnly" j
                  |> List.map (fun r -> text "case" r, strings "vectors" r)

              for v in items "vectors" j do
                  let id = text "id" v
                  let case = text "case" v

                  let alone =
                      referenceOnly
                      |> List.exists (fun (c, ids) -> c = case && (List.isEmpty ids || List.contains id ids))

                  let stamp = strings "answeredBy" v

                  Expect.equal
                      stamp
                      (if alone then
                           [ "reference" ]
                       else
                           [ "reference"; "model:Query" ])
                      (id + "'s answeredBy") ]

// ---------------------------------------------------------------------------
//  Phase 685 (DECISIONS.md D264.4) — the declaration reader's `UnknownTag` sentences name what was
//  found once: `unknown host effect 'everywhere'; expected …`, never `unknown unknown …`.
// ---------------------------------------------------------------------------

/// A declaration the reader admits, as text: no parameter, no column, a `ref` source.
let private plainDeclaration685 =
    """{"$type":"query","effect":{"determinism":"clock","host":"pure"},"id":"q","params":[],"resultSchema":[],"source":{"ref":"r","schema":[]}}"""

/// Each `UnknownTag` the declaration reader reaches from one edit of `plainDeclaration685`, with the
/// spelling the edit plants.
let private unknownTags685 =
    [ "host effect", "everywhere", plainDeclaration685.Replace("\"host\":\"pure\"", "\"host\":\"everywhere\"")
      "determinism",
      "sometimes",
      plainDeclaration685.Replace("\"determinism\":\"clock\"", "\"determinism\":\"sometimes\"")
      "parameter type",
      "complex",
      plainDeclaration685.Replace(
          "\"params\":[]",
          "\"params\":[{\"name\":\"p\",\"required\":true,\"type\":\"complex\"}]"
      ) ]

[<Tests>]
let sentenceTests685 =
    testList
        "Phase 685 - the declaration reader's unknown-tag sentences"
        [ testCase "the plain declaration reads"
          <| fun _ ->
              Expect.isOk (QueryCodec.decodeDetailedWith ReadPolicy.Lenient plainDeclaration685) "the base reads"

          for what, found, text in unknownTags685 do
              testCase (sprintf "an unknown %s names what was found, once" what)
              <| fun _ ->
                  match QueryCodec.decodeDetailedWith ReadPolicy.Lenient text with
                  | Ok _ -> failtestf "the reader admitted an unknown %s" what
                  | Error e ->
                      Expect.equal e.Code DecodeCode.UnknownTag (what + ": the code")
                      Expect.isFalse (e.Message.Contains "unknown unknown") (what + " doubles its lead: " + e.Message)
                      Expect.stringContains e.Message ("'" + found + "'") (what + " names what was found: " + e.Message) ]
