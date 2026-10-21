/// `proofs/README.md` as a RENDER (Phase 533). What is proved is stated once, in `proofs.json`'s rows
/// (`statement` + `boundary`, Phase 522), and what each model describes is stated once, in its `.fst`
/// header's opening paragraph and its WHAT IS MODELLED paragraph. This module renders the README's model
/// sections from those two sources and nothing else, so the README cannot say a different thing from the
/// ladder. `render proofs-readme` (the tool) is the file's one writer and the `Proofs.Ladder` family holds
/// the committed file to the render.
module Fuaran.Core.Tests.ProofReadme

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Fuaran.Core.Tests.ProofLadder

// ---------------------------------------------------------------------------
//  The rows, read once
// ---------------------------------------------------------------------------

/// One ladder row, as the render reads it.
type Row =
    { Id: string
      Level: string
      Class: string option
      Statement: string
      Boundary: string
      Phase: string
      Theorem: string option
      Supporting: string list
      Model: string option
      Family: string option
      Cases: int
      Assumption: string option
      DischargedBy: string option
      Closes: string option }

/// The ladder's rows, in ladder order.
let rows (ladderText: string) : Row list =
    use doc = JsonDocument.Parse ladderText

    match doc.RootElement.TryGetProperty "claims" with
    | true, c when c.ValueKind = JsonValueKind.Array ->
        c.EnumerateArray()
        |> Seq.map (fun r ->
            let ev = objMember r "evidence"

            let evStr name =
                ev |> Option.bind (fun e -> strMember e name)

            let evList name =
                ev |> Option.bind (fun e -> strArrayMember e name) |> Option.defaultValue []

            { Id = strMember r "id" |> Option.defaultValue "<no id>"
              Level = strMember r "level" |> Option.defaultValue "<no level>"
              Class = strMember r "class"
              Statement = strMember r "statement" |> Option.defaultValue ""
              Boundary = strMember r "boundary" |> Option.defaultValue ""
              Phase = strMember r "phase" |> Option.defaultValue ""
              Theorem = evStr "theorem"
              Supporting = evList "supporting"
              Model = evStr "model"
              Family = evStr "family"
              Cases = List.length (evList "cases")
              Assumption = evStr "assumption"
              DischargedBy = strMember r "dischargedBy"
              Closes = strMember r "closes" })
        |> List.ofSeq
    | _ -> []

// ---------------------------------------------------------------------------
//  A model's header — the opening paragraph and the description block the render takes from it
// ---------------------------------------------------------------------------

/// The paragraphs of the leading `(* … *)` comment, each a list of its lines with their indentation
/// trimmed. A paragraph is a run of non-blank lines.
let headerParagraphs (fstText: string) : string list list =
    let text = fstText.Replace("\r\n", "\n")
    let start = text.IndexOf "(*"
    let stop = if start < 0 then -1 else text.IndexOf("*)", start)

    if start < 0 || stop < 0 then
        []
    else
        text.Substring(start + 2, stop - start - 2).Split('\n')
        |> Array.map _.Trim()
        |> Array.fold
            (fun (acc: string list list) line ->
                match line, acc with
                | "", [] :: _ -> acc
                | "", _ -> [] :: acc
                | l, cur :: rest -> (l :: cur) :: rest
                | l, [] -> [ [ l ] ])
            [ [] ]
        |> List.map List.rev
        |> List.filter (List.isEmpty >> not)
        |> List.rev

/// A paragraph that opens a header BLOCK: it starts with a label of two or more upper-case words
/// (`WHAT IS MODELLED.`, `HOW TO READ IT.`), or is the licence line. Any other paragraph — a list
/// item, a numbered step, prose — continues the block before it.
let private blockStart =
    Regex(@"^(?:Apache-2\.0|(?:[A-Z][A-Z0-9'\-]*(?![a-z])[ ,]+)+[A-Z][A-Z0-9'\-]*(?![a-z]))", RegexOptions.Compiled)

/// The header's blocks after its opening paragraph, each a label paragraph and its continuations.
let headerBlocks (fstText: string) : string list list list =
    match headerParagraphs fstText with
    | [] -> []
    | _ :: rest ->
        rest
        |> List.fold
            (fun (acc: string list list list) para ->
                match acc with
                | cur :: done' when not (blockStart.IsMatch(List.head para)) -> (para :: cur) :: done'
                | _ -> [ para ] :: acc)
            []
        |> List.map List.rev
        |> List.rev

/// The labels a header's description block opens with, in preference order.
let modelledLabels = [ "WHAT IS MODELLED"; "WHAT THIS IS." ]

/// The header's opening paragraph, and its description block (the `WHAT IS MODELLED` block, or a
/// `WHAT THIS IS.` block where the module is not a model of one function) with its label's own
/// sentence opener dropped. Lines, as authored.
let headerSummary (fstText: string) : string list option * string list list option =
    let opening = headerParagraphs fstText |> List.tryHead

    let described =
        modelledLabels
        |> List.tryPick (fun label ->
            headerBlocks fstText
            |> List.tryFind (fun block -> (List.head (List.head block)).StartsWith(label, StringComparison.Ordinal))
            |> Option.map (fun block ->
                match block with
                | (first :: lines) :: paras ->
                    let dot = label.TrimEnd('.') + "."

                    let first' =
                        if first.StartsWith(dot, StringComparison.Ordinal) then
                            first.Substring(dot.Length).TrimStart()
                        else
                            first

                    (first' :: lines) :: paras
                | _ -> block))

    opening, described

// ---------------------------------------------------------------------------
//  Rendering
// ---------------------------------------------------------------------------

/// `F*` outside a code span is escaped, because a bare `*` opens emphasis in Markdown.
let private escapeProse (s: string) : string =
    s.Split('`')
    |> Array.mapi (fun i part -> if i % 2 = 0 then part.Replace("F*", "F\\*") else part)
    |> String.concat "`"

/// The anchor a row renders under, so a section can link a row it rests on.
let anchor (id: string) = "row-" + id

let private kindNote (kind: ModelKind) =
    match kind with
    | Extracted -> "checked, extracted to the oracle and run beside production by `Proofs.Oracle`"
    | ProofOnly -> "checked only; its production side cannot be run beside an extraction (`check.ps1` says why)"
    | Generated -> "generated by `Fuaran.Core.Idl.Codegen`'s F\\* target and held to a fresh generation"
    | Shared -> "a shared module every model opens; it carries no row"

/// One row, as a list item: id, evidence, phase, statement, then its boundary as a second paragraph.
let renderRow (r: Row) : string list =
    let evidence =
        [ match r.Theorem with
          | Some t -> yield sprintf "`%s`" t
          | None -> ()
          match r.Family with
          | Some f when r.Cases > 0 -> yield sprintf "`%s`, %d case%s" f r.Cases (if r.Cases = 1 then "" else "s")
          | Some f -> yield sprintf "`%s`" f
          | None -> ()
          match r.Class with
          | Some c -> yield sprintf "`%s`" c
          | None -> ()
          match r.DischargedBy, r.Closes with
          | Some d, _ -> yield sprintf "discharged by `%s`" d
          | None, Some c -> yield sprintf "closes: `%s`" c
          | None, None -> ()
          if r.Phase <> "" then
              yield r.Phase ]
        |> String.concat " · "

    [ yield sprintf "- <a id=\"%s\"></a>**`%s`** — %s" (anchor r.Id) r.Id evidence
      yield ""
      yield "  " + escapeProse r.Statement
      if not (List.isEmpty r.Supporting) then
          yield ""

          yield
              "  _With:_ "
              + (r.Supporting |> List.map (sprintf "`%s`") |> String.concat ", ")
              + "."
      match r.Assumption with
      | Some a ->
          yield ""
          yield "  _Assumes:_ " + escapeProse a + "."
      | None -> ()
      if r.Boundary <> "" then
          yield ""
          yield "  _Boundary:_ " + escapeProse r.Boundary
      yield "" ]

/// Whether `text` names `id` as a code span — how one row cites another.
let private cites (text: string) (id: string) = text.Contains("`" + id + "`")

let private rowText (r: Row) = r.Statement + " " + r.Boundary

/// The rows a model's rows rest on or are bounded by: every `assumed` or `policy` row that one of them
/// cites, or that cites one of them.
let restsOn (all: Row list) (own: Row list) : Row list =
    all
    |> List.filter (fun r -> r.Level = "assumed" || r.Level = "policy")
    |> List.filter (fun a -> own |> List.exists (fun r -> cites (rowText r) a.Id || cites (rowText a) r.Id))

/// The rows whose evidence names `modul`'s `.fst`.
let rowsOf (all: Row list) (modul: string) : Row list =
    let path = sprintf "proofs/%s.fst" modul
    all |> List.filter (fun r -> r.Model = Some path)

/// Every model `entry` opens, directly or through another model, in roster order; shared modules
/// (which carry no row) are left out.
let modelsOpened (roster: RosterEntry list) (entry: RosterEntry) : RosterEntry list =
    let byName = roster |> List.map (fun e -> e.Module, e) |> Map.ofList

    let rec close (seen: Set<string>) (todo: string list) =
        match todo with
        | [] -> seen
        | m :: rest when seen.Contains m -> close seen rest
        | m :: rest ->
            let opens = Map.tryFind m byName |> Option.map _.Opens |> Option.defaultValue []

            close (seen.Add m) (opens @ rest)

    let reached = close Set.empty entry.Opens
    roster |> List.filter (fun e -> reached.Contains e.Module && isModel e)

/// One model's section: heading, the header's two paragraphs, its rows by level, the assumed and
/// policy rows its own rows touch, and those it inherits through the models it opens.
let renderModel (all: Row list) (roster: RosterEntry list) (entry: RosterEntry) (fstText: string) : string list =
    let own = rowsOf all entry.Module
    let opened = modelsOpened roster entry
    let opening, described = headerSummary fstText

    let level name label =
        match own |> List.filter (fun r -> r.Level = name) with
        | [] -> []
        | rs ->
            [ yield sprintf "**%s** (%d)" label (List.length rs)
              yield ""
              yield! rs |> List.collect renderRow ]

    [ yield sprintf "### `%s`" entry.Module
      yield ""
      yield
          sprintf
              "[`%s.fst`](%s.fst) — %s.%s"
              entry.Module
              entry.Module
              (kindNote entry.Kind)
              (match opened with
               | [] -> ""
               | os ->
                   " Opens "
                   + (os |> List.map (fun e -> sprintf "`%s`" e.Module) |> String.concat ", ")
                   + ".")
      yield ""
      match opening with
      | Some lines ->
          yield! lines |> List.map (fun l -> "> " + escapeProse l)
          yield ""
      | None -> ()
      match described with
      | Some(first :: paras) ->
          yield!
              first
              |> List.mapi (fun i l ->
                  if i = 0 then
                      "**What is modelled.** " + escapeProse l
                  else
                      escapeProse l)

          yield ""

          for para in paras do
              yield! para |> List.map escapeProse
              yield ""
      | _ -> ()
      yield! level "proved" "Proved"
      yield! level "tested" "Tested"
      let link (r: Row) =
          sprintf "[`%s`](#%s) (%s)" r.Id (anchor r.Id) r.Level

      let direct = restsOn all own

      let inherited =
          restsOn all (opened |> List.collect (fun e -> rowsOf all e.Module))
          |> List.filter (fun r -> not (List.contains r direct))

      match direct with
      | [] -> ()
      | rs ->
          yield
              "**Rests on, or is bounded by:** "
              + (rs |> List.map link |> String.concat ", ")
              + "."

          yield ""

      match inherited with
      | [] -> ()
      | rs ->
          yield
              "**Inherited through the models it opens:** "
              + (rs |> List.map link |> String.concat ", ")
              + "."

          yield "" ]

// ---------------------------------------------------------------------------
//  The whole document
// ---------------------------------------------------------------------------

/// The README, repository-relative.
[<Literal>]
let ReadmePath = "proofs/README.md"

/// The family the tool renders the README under (`render proofs-readme`).
[<Literal>]
let Family = "proofs-readme"

let modelsBegin =
    "<!-- models:begin — rendered from ../proofs.json, modules.json and each model's header by `render proofs-readme`; edit those, never this block -->"

let modelsEnd = "<!-- models:end -->"

/// The rows no model section carries — every `assumed` and `policy` row, and every `tested` row whose
/// evidence names no model — once each, where the sections' links land.
let renderUnmodelled (all: Row list) : string list =
    let group level label =
        match all |> List.filter (fun r -> r.Level = level && r.Model.IsNone) with
        | [] -> []
        | rs ->
            [ yield sprintf "**%s** (%d)" label (List.length rs)
              yield ""
              yield! rs |> List.collect renderRow ]

    [ yield "### Assumed, policy, and tested beyond a model"
      yield ""
      yield
          "The rows no model section carries. An `assumed` row is classed in the contract table above; a model section links each one its rows rest on."
      yield ""
      yield! group "assumed" "Assumed"
      yield! group "policy" "Policy"
      yield! group "tested" "Tested beyond a model" ]

/// Every model's section in roster (check) order, then the rows no section carries.
let renderModels (ladderText: string) (roster: RosterEntry list) (header: string -> string) : string =
    let all = rows ladderText

    [ for e in roster do
          if isModel e then
              yield! renderModel all roster e (header e.Module)
      yield! renderUnmodelled all ]
    |> String.concat "\n"
    |> fun s -> s.TrimEnd('\n')

/// `text` with the lines strictly between `beginLine` and `endLine` replaced by `body`.
let replaceBlock (beginLine: string) (endLine: string) (body: string) (text: string) : Result<string, string> =
    let lines = text.Replace("\r\n", "\n").Split('\n') |> List.ofArray

    match List.tryFindIndex ((=) beginLine) lines, List.tryFindIndex ((=) endLine) lines with
    | Some b, Some e when e > b -> Ok(String.Join("\n", lines[..b] @ [ body ] @ lines[e..]))
    | Some _, Some _ -> Error(sprintf "`%s` precedes its begin marker" endLine)
    | _ -> Error(sprintf "the README carries no `%s` … `%s` block" beginLine endLine)

/// What the render reads, beside the README itself.
type Inputs =
    {
        Ladder: string
        Roster: RosterEntry list
        /// A model's `.fst` text, by module name.
        Header: string -> string
        /// The operation clause's per-package table (`ProofCoverage.renderOperationTable`).
        OperationTable: string
    }

/// The README as the inputs say it must read: the ladder summary and the contract table and counts
/// (`regenerateReadme`), the operation table, and the model sections. Prose outside the blocks is
/// carried byte for byte.
let render (inputs: Inputs) (readmeText: string) : Result<string, string> =
    regenerateReadme inputs.Ladder readmeText
    |> Result.bind (ProofCoverage.regenerateOperationTable inputs.OperationTable)
    |> Result.bind (replaceBlock modelsBegin modelsEnd (renderModels inputs.Ladder inputs.Roster inputs.Header))

/// The inputs, read from the tree.
let liveInputs () : Result<Inputs, string> =
    match parseRoster (File.ReadAllText(Snapshots.repoFile "proofs/modules.json")) with
    | Error es -> Error(sprintf "proofs/modules.json: %s" (String.concat "; " es))
    | Ok roster ->
        let _, tally = ProofCoverage.checkOperations (ProofCoverage.liveOperationInputs ())

        Ok
            { Ladder = File.ReadAllText(Snapshots.repoFile "proofs.json")
              Roster = roster
              Header = fun m -> File.ReadAllText(Snapshots.repoFile (sprintf "proofs/%s.fst" m))
              OperationTable = ProofCoverage.renderOperationTable tally }

/// The committed README's render, from the tree.
let renderLive () : Result<string, string> =
    liveInputs ()
    |> Result.bind (fun inputs -> render inputs (File.ReadAllText(Snapshots.repoFile ReadmePath)))

/// The tool's `render proofs-readme`: the ONE writer of the README.
let write () : int =
    match renderLive () with
    | Error why ->
        eprintfn "render %s: %s" Family why
        2
    | Ok text ->
        let path = Snapshots.repoFile ReadmePath

        if File.ReadAllText path <> text then
            File.WriteAllText(path, text)
            printfn "regenerated %s" ReadmePath
        else
            printfn "%s is current" ReadmePath

        0

// ---------------------------------------------------------------------------
//  The inventory line a document outside this repository quotes
// ---------------------------------------------------------------------------

/// One sentence stating the proof inventory, for a document that restates it (the tool prints it as
/// `proofs-inventory`): the models, their source lines, and the ladder's rows by level.
let inventoryLine (ladderText: string) (roster: RosterEntry list) (lineCount: string -> int) : string =
    let all = rows ladderText
    let models = roster |> List.filter isModel
    let lines = models |> List.sumBy (fun e -> lineCount e.Module)

    let at level =
        all |> List.filter (fun r -> r.Level = level) |> List.length

    sprintf
        "%d F* models, %s lines, %d claims-ladder rows (%d proved, %d tested, %d assumed, %d policy)"
        (List.length models)
        (lines.ToString("N0", Globalization.CultureInfo.InvariantCulture))
        (List.length all)
        (at "proved")
        (at "tested")
        (at "assumed")
        (at "policy")

/// The inventory line, from the tree.
let inventoryLive () : Result<string, string> =
    match parseRoster (File.ReadAllText(Snapshots.repoFile "proofs/modules.json")) with
    | Error es -> Error(String.concat "; " es)
    | Ok roster ->
        Ok(
            inventoryLine (File.ReadAllText(Snapshots.repoFile "proofs.json")) roster (fun m ->
                File.ReadAllLines(Snapshots.repoFile (sprintf "proofs/%s.fst" m)).Length)
        )
