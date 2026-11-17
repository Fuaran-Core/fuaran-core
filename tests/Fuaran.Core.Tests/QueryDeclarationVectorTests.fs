module Fuaran.Core.Tests.QueryDeclarationVectorTests

open System.IO
open Expecto
open Microsoft.FSharp.Reflection
open Fuaran.Core

// ---------------------------------------------------------------------------
//  Phase 680 — the query declaration family (`laws/query-declaration-laws.json`, DECISIONS.md D263).
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

/// Every case of `QueryError` as its wire tag: the case's name with its first letter lowered, which is
/// how the seam codec spells each of the twenty (`queryPolicyRefused` keeps its prefix, Phase 497).
let unionTags: Set<string> =
    FSharpType.GetUnionCases(typeof<QueryError>)
    |> Array.map (fun c -> string (System.Char.ToLowerInvariant c.Name[0]) + c.Name.Substring 1)
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

[<Tests>]
let tests =
    testList
        "Phase 680 — the query declaration family"
        [ testCase "the census: every case of QueryError has a vector or a recorded reason, never both"
          <| fun _ ->
              match censusFindings (committed ()) with
              | [] -> ()
              | findings -> failtestf "the census finds %d gaps:\n%s" findings.Length (String.concat "\n" findings) ]
