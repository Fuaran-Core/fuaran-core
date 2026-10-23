module Fuaran.Core.Tests.ProofCoverage

// The coverage predicate (Phases 294, 400) and the operation census (Phase 339's successor records): what
// "exhaustively proved" means, computed from inputs, and the README regions it renders. `ProofCoverageTests`
// (the Proofs.Coverage family) gathers the live inputs from the tree and holds the verdict.

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Fuaran.Core.Tests.ProofLadder

// ---------------------------------------------------------------------------
//  The inputs the predicate is computed from
// ---------------------------------------------------------------------------

/// One entry of `proofs/coverage-exclusions.json`.
type Exclusion =
    {
        Package: string
        Reason: string
        /// The `Conformance.<law>` a `law-tested-by-design` entry names, and that only such an
        /// entry may name.
        Family: string option
        Note: string
        Phase: string
    }

/// One entry of `proofs/modules.json`, as this family reads it: the model, and the packable
/// packages whose production code it is about.
type ModelEntry =
    { Module: string
      Packages: string list }

type CoverageInputs =
    {
        /// Every packable package. For the real run this is Phase 199's own derivation, called
        /// rather than restated — two spellings of "what ships" would drift exactly the way the
        /// documents that phase gates had drifted.
        Packable: string list
        /// `proofs/modules.json`'s roster, with its Phase 203 `packages` attribution.
        Models: ModelEntry list
        Exclusions: Exclusion list
        /// The reason vocabulary, read from the exclusions file's own `reasons` block rather than
        /// restated here: the vocabulary is closed BY THAT FILE, and a copy of it in this one
        /// would be a second closed set to keep in step.
        Reasons: Set<string>
        /// The law names a `law-tested-by-design` entry may cite — the shipped kit's declared
        /// roster, for the same reason `Proofs.Ladder` reads it there.
        Laws: Set<string>
        /// The contract section of `proofs/README.md`, whose prose counts are held to the ladder.
        ContractText: string
    }

/// What the predicate says, once computed — the numbers the emitted line renders.
type CoverageTally =
    {
        Modelled: int
        Excluded: int
        Assumed: int
        Premise: int
        DomainDischarged: int
        /// A `domain-obligation` whose law checks the domain's DECLARATION and cannot check past it
        /// (`"discharge": "domain-declared"`, Phase 309) — counted apart, because a green run of its
        /// law is not a discharge.
        DomainDeclared: int
        Permanent: int
        Unscheduled: int
        /// A `host-premise` (Phase 483): discharged per host, by that host's run of the row's probe,
        /// so it is counted apart from what a domain discharges and from what nothing does.
        HostProbed: int
    }

let internal finding (subject: string) (clause: string) (detail: string) =
    sprintf "'%s' [%s]: %s" subject clause detail

/// `fuaran-core#NNN`, the one phase form this repository's ladder uses.
let internal phaseForm =
    Regex(@"^fuaran-core#([1-9][0-9]*)$", RegexOptions.Compiled)

/// A model path as a `proved` row states it: `proofs/<Module>.fst`.
let internal moduleOfPath (path: string) = Path.GetFileNameWithoutExtension path

// ---------------------------------------------------------------------------
//  The predicate
// ---------------------------------------------------------------------------

/// Every finding the three clauses yield against these inputs and this ladder, with the tally the
/// emitted line renders. Empty findings is exhaustive.
let checkCoverage (inputs: CoverageInputs) (ladderText: string) : string list * CoverageTally =
    use doc = JsonDocument.Parse ladderText

    let rows =
        match doc.RootElement.TryGetProperty "claims" with
        | true, c when c.ValueKind = JsonValueKind.Array -> c.EnumerateArray() |> List.ofSeq
        | _ -> []

    let atLevel lvl =
        rows |> List.filter (fun r -> strMember r "level" = Some lvl)

    let packable = Set.ofList inputs.Packable

    // ---- clause 1: coverage is total or declared -------------------------------------------

    let modelFindings =
        inputs.Models
        |> List.collect (fun m ->
            let empty =
                if List.isEmpty m.Packages then
                    [ finding
                          m.Module
                          "model-package"
                          "names no `packages` — a model attributed to nothing cannot cover anything, and reads as coverage in the roster" ]
                else
                    []

            let unknown =
                m.Packages
                |> List.filter (fun p -> not (Set.contains p packable))
                |> List.map (fun p ->
                    finding
                        m.Module
                        "model-package"
                        (sprintf
                            "names package '%s', which is not packable — the attribution has outlived the package"
                            p))

            empty @ unknown)

    let modelled =
        inputs.Models
        |> List.collect _.Packages
        |> List.filter (fun p -> Set.contains p packable)
        |> Set.ofList

    let excluded = inputs.Exclusions |> List.map _.Package |> Set.ofList

    let uncovered =
        inputs.Packable
        |> List.filter (fun p -> not (Set.contains p modelled) && not (Set.contains p excluded))
        |> List.map (fun p ->
            finding
                p
                "coverage-declared"
                "is packable and is named by no model in `modules.json` and no entry in `coverage-exclusions.json` — there is no third state, so state which one it is")

    let exclusionFindings =
        inputs.Exclusions
        |> List.collect (fun e ->
            let stale =
                if not (Set.contains e.Package packable) then
                    [ finding
                          e.Package
                          "exclusion-stale"
                          "is excluded and is not packable — the reason has outlived the package it describes" ]
                elif Set.contains e.Package modelled then
                    [ finding
                          e.Package
                          "exclusion-stale"
                          "is excluded and HAS a model — delete the entry; an exclusion beside a model is a decision that has already been reversed" ]
                else
                    []

            let reason =
                if Set.contains e.Reason inputs.Reasons then
                    []
                else
                    [ finding
                          e.Package
                          "exclusion-reason"
                          (sprintf
                              "reason '%s' is outside the closed vocabulary %s"
                              e.Reason
                              (inputs.Reasons |> Set.toList |> String.concat " | ")) ]

            let family =
                match e.Reason, e.Family with
                | "law-tested-by-design", None ->
                    [ finding
                          e.Package
                          "exclusion-family"
                          "is `law-tested-by-design` and names no `family` — a declined theorem must say what stands in its place, or the entry is an excuse" ]
                | "law-tested-by-design", Some f when not (f.StartsWith "Conformance.") ->
                    [ finding
                          e.Package
                          "exclusion-family"
                          (sprintf "`family` '%s' is not the `Conformance.<law>` form" f) ]
                | "law-tested-by-design", Some f when
                    not (Set.contains (f.Substring "Conformance.".Length) inputs.Laws)
                    ->
                    [ finding
                          e.Package
                          "exclusion-family"
                          (sprintf
                              "`family` names '%s', which the shipped law-family roster does not declare — the entry cites a run no census enumerates"
                              f) ]
                | "law-tested-by-design", Some _ -> []
                | _, Some f ->
                    [ finding
                          e.Package
                          "exclusion-family"
                          (sprintf "carries `family` '%s' — only a `law-tested-by-design` entry names a law" f) ]
                | _, None -> []

            let recorded =
                let blank (v: string) = String.IsNullOrWhiteSpace v

                if blank e.Note then
                    [ finding
                          e.Package
                          "exclusion-reason"
                          "carries no `note` — the reason token says which KIND of decision it is, never the decision" ]
                elif not (phaseForm.IsMatch e.Phase) then
                    [ finding
                          e.Package
                          "exclusion-reason"
                          (sprintf "phase '%s' is not the `fuaran-core#NNN` form" e.Phase) ]
                else
                    []

            stale @ reason @ family @ recorded)

    // ---- clause 2: every assumed row is accounted for ---------------------------------------

    let assumedRows = atLevel "assumed"

    let accountOf (row: JsonElement) =
        let id = strMember row "id" |> Option.defaultValue "<no id>"

        match strMember row "class" with
        | None ->
            [ finding
                  id
                  "assumed-accounted"
                  "carries no `class` — an assumed row with no class is one no reader can act on, and it drops silently out of the predicate's own count" ],
            None
        | Some "premise" -> [], Some "premise"
        | Some "domain-obligation" ->
            match strMember row "dischargedBy" with
            | Some _ when strMember row "discharge" = Some "domain-declared" -> [], Some "domain-declared"
            | Some _ -> [], Some "domain-discharged"
            | None ->
                [ finding
                      id
                      "assumed-accounted"
                      "is a `domain-obligation` and names no `dischargedBy` — an obligation with no law is one a domain has no way to discharge" ],
                None
        | Some "model-bridge" ->
            match strMember row "closes" with
            | Some "permanent" -> [], Some "permanent"
            | Some "unscheduled" -> [], Some "unscheduled"
            | Some c ->
                [ finding
                      id
                      "assumed-accounted"
                      (sprintf
                          "`closes` '%s' is neither `permanent` nor `unscheduled` — no schedule is kept here, so a phase is not a value (proofs/README.md)"
                          c) ],
                None
            | None ->
                [ finding
                      id
                      "assumed-accounted"
                      "is a `model-bridge` and names no `closes` — the bridge is unaccounted: nobody can tell a gap nothing could close from one nobody has taken" ],
                None
        | Some "host-premise" ->
            match strMember row "probe" with
            | Some _ -> [], Some "host-probed"
            | None ->
                [ finding
                      id
                      "assumed-accounted"
                      "is a `host-premise` and names no `probe` — a premise no host can show it answered is unaccounted" ],
                None
        | Some c ->
            [ finding
                  id
                  "assumed-accounted"
                  (sprintf "class '%s' is outside the closed set %s" c (String.concat " | " classOrder)) ],
            None

    let accounts = assumedRows |> List.map accountOf
    let assumedFindings = accounts |> List.collect fst

    let countAccount name =
        accounts |> List.filter (fun (_, a) -> a = Some name) |> List.length

    // ---- clause 3: every differential is paired to a theorem ---------------------------------

    let provedModels =
        atLevel "proved"
        |> List.choose (fun r -> objMember r "evidence" |> Option.bind (fun e -> strMember e "model"))
        |> List.map moduleOfPath
        |> Set.ofList

    let knownModules = inputs.Models |> List.map _.Module |> Set.ofList

    let differentialFindings =
        atLevel "tested"
        |> List.collect (fun row ->
            let id = strMember row "id" |> Option.defaultValue "<no id>"
            let ev = objMember row "evidence"
            let family = ev |> Option.bind (fun e -> strMember e "family")
            let model = ev |> Option.bind (fun e -> strMember e "model")

            match family, model with
            | Some f, Some m when f.StartsWith "Suite:" ->
                let mn = moduleOfPath m

                if not (Set.contains mn knownModules) then
                    [ finding
                          id
                          "differential-paired"
                          (sprintf "`evidence.model` names '%s', which is not a model the leg checks" m) ]
                elif not (Set.contains mn provedModels) then
                    [ finding id "differential-paired" (sprintf "is about '%s', which carries NO `proved` row" m) ]
                else
                    []
            | Some "Proofs.Oracle", None ->
                [ finding
                      id
                      "differential-paired"
                      "is a `Proofs.Oracle` differential and names no `evidence.model` — a differential that does not say which model it ran beside cannot be paired to a theorem, only assumed to have one" ]
            | Some "Proofs.Oracle", Some m ->
                let mn = moduleOfPath m

                if not (Set.contains mn knownModules) then
                    [ finding
                          id
                          "differential-paired"
                          (sprintf "`evidence.model` names '%s', which is not a model the leg checks" m) ]
                elif not (Set.contains mn provedModels) then
                    [ finding
                          id
                          "differential-paired"
                          (sprintf
                              "runs beside '%s', which carries NO `proved` row — a differential with no theorem beside it is a test pretending to be a rung"
                              m) ]
                else
                    []
            | Some f, Some m when f.StartsWith "Conformance." ->
                [ finding
                      id
                      "differential-paired"
                      (sprintf
                          "is a law row on '%s' and carries `evidence.model` '%s' — a law is sampled at a witness and stands beside no model of ours"
                          f
                          m) ]
            | _ -> [])

    // ---- the count clause Phase 187 asked for -------------------------------------------------

    let countIn (label: string) (pattern: string) (actual: int) =
        let m = Regex.Match(inputs.ContractText, pattern, RegexOptions.Singleline)

        if not m.Success then
            [ finding
                  label
                  "contract-counts"
                  "the contract section states no count this clause can find — a count that cannot be located is a count that cannot be checked, and the prose has been reworded out of the gate" ]
        elif m.Groups[1].Value <> string actual then
            [ finding
                  label
                  "contract-counts"
                  (sprintf "the contract section says %s and `proofs.json` carries %d" m.Groups[1].Value actual) ]
        else
            []

    let classCount cls =
        assumedRows
        |> List.filter (fun r -> strMember r "class" = Some cls)
        |> List.length

    let countFindings =
        countIn "assumed rows" @"the (\d+) assumed rows" (List.length assumedRows)
        @ (classOrder
           |> List.collect (fun cls ->
               countIn cls (sprintf @"\*\*`%s`.*?(\d+) rows\." (Regex.Escape cls)) (classCount cls)))

    let tally =
        { Modelled = Set.count modelled
          Excluded = List.length inputs.Exclusions
          Assumed = List.length assumedRows
          Premise = countAccount "premise"
          DomainDischarged = countAccount "domain-discharged"
          DomainDeclared = countAccount "domain-declared"
          Permanent = countAccount "permanent"
          Unscheduled = countAccount "unscheduled"
          HostProbed = countAccount "host-probed" }

    modelFindings
    @ uncovered
    @ exclusionFindings
    @ assumedFindings
    @ differentialFindings
    @ countFindings,
    tally

/// THE LINE. Emitted whatever the verdict and saying which verdict it is: a line that only
/// appeared when green would make its absence the signal, and an absence is the one thing a reader
/// scanning a log does not see. `permanent` counts the rows nothing on any side closes — the
/// premises, and the bridges whose `closes` says so; `domain-discharged` is what a domain closes
/// at its own witness; `unscheduled` is kept separate from `permanent` deliberately, because
/// rounding the one into the other asserts an impossibility the contract section argues against at
/// length.
let renderPredicate (exhaustive: bool) (t: CoverageTally) : string =
    sprintf
        "proofs: %s (%d packages modelled, %d excluded; %d assumed: %d permanent, %d domain-discharged, %d domain-declared, %d unscheduled, %d host-probed)"
        (if exhaustive then "exhaustive" else "NOT exhaustive")
        t.Modelled
        t.Excluded
        t.Assumed
        (t.Premise + t.Permanent)
        t.DomainDischarged
        t.DomainDeclared
        t.Unscheduled
        t.HostProbed

// ---------------------------------------------------------------------------
//  The real inputs
// ---------------------------------------------------------------------------

/// One public operation of the census: the package whose baseline publishes it, its spelling in the
/// coverage records, and the baseline's CLR name.
type Operation =
    { Package: string
      Name: string
      Clr: string }

/// One entry of `coverage-exclusions.json`'s `operations` block.
type OperationExclusion =
    { Members: string list
      Class: string
      Reason: string
      ForwardsTo: string option
      StandIn: string option
      Through: string option }

type OperationInputs =
    {
        Census: Operation list
        /// `proofs.json` rows that carry `evidence.operations`: (row id, level, operations).
        Ladder: (string * string * string list) list
        /// The kit's operation roster: (family id, operations).
        Rosters: (string * string list) list
        /// Every declared law-family id — an entry point named here is mapped by its own row.
        Families: Set<string>
        Exclusions: OperationExclusion list
        /// The class vocabulary, read from the exclusions file's own `operationClasses` block.
        Classes: Set<string>
        /// The text of each stand-in a `measured-elsewhere` entry names, by its repo-relative path. A
        /// path absent from the map does not exist (or is not under `tests/`).
        StandIns: Map<string, string>
        /// The census operations that carry `System.Obsolete`, on itself or its declaring type.
        Obsolete: Set<string>
    }

/// How the census divides, each operation counted once, by the first route that maps it.
type OperationTally =
    { Operations: int
      Ladder: int
      Rostered: int
      ByClass: (string * int) list
      ByPackage: (string * int * int * int * (string * int) list) list }

/// The class order the records and the README table use.
let operationClassOrder =
    [ "trivial"; "forward"; "obsolete"; "host-seam"; "measured-elsewhere" ]

/// A baseline's CLR member name as the coverage records spell it: `Fuaran.Core.` dropped, generic
/// arity dropped, nested types joined by `.`, and the compiler's `Module` suffix dropped from every
/// type segment (`Fuaran.Core.Dag+ReachModule.ancestors` is `Dag.Reach.ancestors`).
let operationName (clr: string) : string =
    let rest =
        if clr.StartsWith "Fuaran.Core." then
            clr.Substring "Fuaran.Core.".Length
        else
            clr

    let segs = Regex.Replace(rest, "`[0-9]+", "").Replace('+', '.').Split('.')

    segs
    |> Array.mapi (fun i s ->
        if i < segs.Length - 1 && s.Length > 6 && s.EndsWith "Module" then
            s.Substring(0, s.Length - 6)
        else
            s)
    |> String.concat "."

/// The `method` lines of one package's baseline, as operations.
let censusOf (package: string) (baselineText: string) : Operation list =
    baselineText.Split('\n')
    |> Array.choose (fun raw ->
        let line = raw.TrimEnd '\r'

        if line.StartsWith "method " && line.IndexOf '(' > 7 then
            let clr = line.Substring(7, line.IndexOf '(' - 7)

            Some
                { Package = package
                  Name = operationName clr
                  Clr = clr }
        else
            None)
    |> List.ofArray

/// `<Owner>.<member>` — the last two segments, which is how a test file names an operation.
let internal twoSegments (op: string) =
    let s = op.Split '.'

    if s.Length >= 2 then
        s[s.Length - 2] + "." + s[s.Length - 1]
    else
        op

/// Whether `text` mentions `token`, bounded as a word on each side where the token has a word
/// character there (a `.Member` token may follow an identifier).
let internal mentions (text: string) (token: string) =
    let isWord (c: char) = Char.IsLetterOrDigit c || c = '_'

    token.Length > 0
    && Regex.IsMatch(
        text,
        (if isWord token[0] then @"(?<![\w])" else "")
        + Regex.Escape token
        + (if isWord token[token.Length - 1] then @"(?![\w])" else "")
    )

/// Every finding the operation clause yields, with the tally the README table renders.
let checkOperations (inputs: OperationInputs) : string list * OperationTally =
    let census = inputs.Census |> List.map _.Name |> Set.ofList

    let ladderOps =
        inputs.Ladder
        |> List.filter (fun (_, level, _) -> level = "proved" || level = "tested")
        |> List.collect (fun (_, _, ops) -> ops)
        |> Set.ofList

    let rosterOps = inputs.Rosters |> List.collect snd |> Set.ofList

    let entryOps = Set.intersect census inputs.Families

    let excluded =
        inputs.Exclusions
        |> List.collect (fun e -> e.Members |> List.map (fun m -> m, e))
        |> List.groupBy fst
        |> List.map (fun (m, es) -> m, es |> List.map snd)
        |> Map.ofList

    let directlyMapped (op: string) =
        ladderOps.Contains op
        || rosterOps.Contains op
        || entryOps.Contains op
        || (match Map.tryFind op excluded with
            | Some(e :: _) -> e.Class <> "forward" && e.Class <> "obsolete"
            | _ -> false)

    // A forward's coverage is its target's: follow `forwardsTo` until a directly mapped operation,
    // refusing a cycle and a chain that ends nowhere.
    let rec resolves (seen: Set<string>) (op: string) =
        if directlyMapped op then
            true
        elif seen.Contains op then
            false
        else
            match Map.tryFind op excluded with
            | Some(e :: _) when (e.Class = "forward" || e.Class = "obsolete") ->
                match e.ForwardsTo with
                | Some t -> resolves (seen.Add op) t
                | None -> false
            | _ -> false

    let ladderFindings =
        inputs.Ladder
        |> List.collect (fun (id, level, ops) ->
            [ if level <> "proved" && level <> "tested" then
                  yield
                      finding
                          id
                          "operation-ladder"
                          (sprintf
                              "a %s row names operations; only a proved or tested row is evidence an operation is covered"
                              level)
              for op in ops do
                  if not (census.Contains op) then
                      yield
                          finding
                              id
                              "operation-ladder"
                              (sprintf "`evidence.operations` names %s, which no API baseline publishes" op) ])

    let rosterFindings =
        [ for fam, ops in inputs.Rosters do
              if not (inputs.Families.Contains fam) then
                  yield
                      finding fam "operation-roster" "an operation-roster row for a family the roster does not declare"

              for op in ops do
                  if not (census.Contains op) then
                      yield finding fam "operation-roster" (sprintf "lists %s, which no API baseline publishes" op)
          for fam, n in inputs.Rosters |> List.countBy fst do
              if n > 1 then
                  yield finding fam "operation-roster" (sprintf "%d operation-roster rows; one per family" n) ]

    let exclusionFindings =
        [ for KeyValue(m, es) in excluded do
              if List.length es > 1 then
                  yield
                      finding
                          m
                          "operation-exclusion-stale"
                          (sprintf "listed by %d entries; one decision per operation" es.Length)

              if not (census.Contains m) then
                  yield finding m "operation-exclusion-stale" "an exclusion for an operation no API baseline publishes"
              elif ladderOps.Contains m || rosterOps.Contains m || entryOps.Contains m then
                  yield
                      finding
                          m
                          "operation-exclusion-stale"
                          "an exclusion for an operation that has since gained a ladder row or a roster line — delete the entry"
          for e in inputs.Exclusions do
              let subject = String.concat ", " e.Members

              if List.isEmpty e.Members then
                  yield finding "(empty entry)" "operation-exclusion-class" "an entry naming no operation"

              if not (inputs.Classes.Contains e.Class) then
                  yield
                      finding
                          subject
                          "operation-exclusion-class"
                          (sprintf "class '%s' is not in the closed vocabulary %A" e.Class (Set.toList inputs.Classes))

              if String.IsNullOrWhiteSpace e.Reason then
                  yield finding subject "operation-exclusion-reason" "an entry with no reason"

              let forwarding = e.Class = "forward" || e.Class = "obsolete"

              match forwarding, e.ForwardsTo with
              | true, None ->
                  yield finding subject "operation-forward" (sprintf "a %s entry names no `forwardsTo`" e.Class)
              | false, Some _ ->
                  yield finding subject "operation-forward" "`forwardsTo` belongs to a forward or obsolete entry"
              | true, Some t when not (census.Contains t) ->
                  yield
                      finding subject "operation-forward" (sprintf "forwards to %s, which no API baseline publishes" t)
              | true, Some t ->
                  for m in e.Members do
                      if census.Contains m && not (resolves Set.empty m) then
                          yield
                              finding
                                  m
                                  "operation-forward"
                                  (sprintf "forwards to %s, which is not itself mapped (or the chain is a cycle)" t)
              | false, None -> ()

              if e.Class = "obsolete" then
                  for m in e.Members do
                      if census.Contains m && not (inputs.Obsolete.Contains m) then
                          yield
                              finding
                                  m
                                  "operation-obsolete"
                                  "an obsolete entry for an operation that carries no System.Obsolete"

              match e.Class = "measured-elsewhere", e.StandIn with
              | true, None -> yield finding subject "operation-stand-in" "a measured-elsewhere entry names no `standIn`"
              | false, Some _ ->
                  yield finding subject "operation-stand-in" "`standIn` belongs to a measured-elsewhere entry"
              | true, Some path ->
                  match Map.tryFind path inputs.StandIns with
                  | None ->
                      yield
                          finding
                              subject
                              "operation-stand-in"
                              (sprintf "stand-in %s is not a test file under tests/ in this repository" path)
                  | Some text ->
                      for m in e.Members do
                          let token = e.Through |> Option.defaultValue (twoSegments m)

                          if not (mentions text token) then
                              yield
                                  finding m "operation-stand-in" (sprintf "stand-in %s never mentions `%s`" path token)
              | false, None -> ()

              if e.Class <> "measured-elsewhere" && e.Through.IsSome then
                  yield finding subject "operation-stand-in" "`through` belongs to a measured-elsewhere entry" ]

    let unmapped =
        inputs.Census
        |> List.filter (fun o -> not (resolves Set.empty o.Name) && not (Map.containsKey o.Name excluded))
        |> List.map (fun o ->
            finding
                o.Name
                "operation-mapped"
                (sprintf
                    "published by %s and mapped to no ladder row, law-family roster line or recorded exclusion"
                    o.Package))

    let routeOf (op: string) =
        if ladderOps.Contains op then
            "ladder"
        elif rosterOps.Contains op || entryOps.Contains op then
            "family"
        else
            match Map.tryFind op excluded with
            | Some(e :: _) -> e.Class
            | _ -> "unmapped"

    let countIn (ops: Operation list) (route: string) =
        ops |> List.filter (fun o -> routeOf o.Name = route) |> List.length

    let tally =
        { Operations = List.length inputs.Census
          Ladder = countIn inputs.Census "ladder"
          Rostered = countIn inputs.Census "family"
          ByClass = operationClassOrder |> List.map (fun c -> c, countIn inputs.Census c)
          ByPackage =
            inputs.Census
            |> List.groupBy _.Package
            |> List.sortBy fst
            |> List.map (fun (p, ops) ->
                p,
                List.length ops,
                countIn ops "ladder",
                countIn ops "family",
                operationClassOrder |> List.map (fun c -> c, countIn ops c)) }

    ladderFindings @ rosterFindings @ exclusionFindings @ unmapped, tally

/// The operation clause's line, emitted whatever the verdict.
let renderOperationPredicate (mapped: bool) (t: OperationTally) : string =
    sprintf
        "proofs: operations %s (%d public operations: %d on the ladder, %d by a law family, %d excluded — %s)"
        (if mapped then "mapped" else "NOT mapped")
        t.Operations
        t.Ladder
        t.Rostered
        (t.ByClass |> List.sumBy snd)
        (t.ByClass |> List.map (fun (c, n) -> sprintf "%d %s" n c) |> String.concat ", ")

let operationTableBegin =
    "<!-- operation-coverage:begin — generated from ../api/*.txt, ../proofs.json, the kit's operation roster and coverage-exclusions.json by `render proofs-readme`; held by the Proofs.Ladder family -->"

let operationTableEnd = "<!-- operation-coverage:end -->"

/// The README's per-package table: each package's operations by the route that maps them.
let renderOperationTable (t: OperationTally) : string =
    let header =
        [ "| Package | Operations | Ladder | Law family | "
          + (operationClassOrder |> String.concat " | ")
          + " |"
          "|---|---:|---:|---:|"
          + (operationClassOrder |> List.map (fun _ -> "---:|") |> String.concat "") ]

    let row (name: string) (ops: int) (lad: int) (fam: int) (classes: (string * int) list) =
        sprintf
            "| %s | %d | %d | %d | %s |"
            name
            ops
            lad
            fam
            (classes |> List.map (snd >> string) |> String.concat " | ")

    let rows =
        t.ByPackage
        |> List.map (fun (p, ops, lad, fam, cls) -> row ("`" + p + "`") ops lad fam cls)

    header @ rows @ [ row "**Total**" t.Operations t.Ladder t.Rostered t.ByClass ]
    |> String.concat "\n"

/// The README with its operation table replaced by `table`, or why the markers were not found.
let regenerateOperationTable (table: string) (readmeText: string) : Result<string, string> =
    let text = readmeText.Replace("\r\n", "\n")

    match text.IndexOf operationTableBegin, text.IndexOf operationTableEnd with
    | -1, _
    | _, -1 -> Error "the operation-coverage markers are missing from proofs/README.md"
    | b, e when e < b -> Error "the operation-coverage end marker precedes its begin marker"
    | b, e ->
        Ok(
            text.Substring(0, b + operationTableBegin.Length)
            + "\n"
            + table
            + "\n"
            + text.Substring e
        )

// ---------------------------------------------------------------------------
//  The operation clause's live inputs, read from the tree (Phase 533 moved them here from the suite, so
//  the README's render, which the tool writes, reads the same census the clause checks)
// ---------------------------------------------------------------------------


let liveCensus () : Operation list =
    Directory.GetFiles(Snapshots.repoFile "api", "*.txt")
    |> Array.sort
    |> Array.toList
    |> List.collect (fun path -> censusOf (Path.GetFileNameWithoutExtension path) (File.ReadAllText path))

let liveLadderOperations () =
    use doc = JsonDocument.Parse(File.ReadAllText(Snapshots.repoFile "proofs.json"))

    doc.RootElement.GetProperty("claims").EnumerateArray()
    |> Seq.choose (fun row ->
        match objMember row "evidence" with
        | Some ev ->
            match ev.TryGetProperty "operations" with
            | true, ops when ops.ValueKind = JsonValueKind.Array ->
                Some(
                    strMember row "id" |> Option.defaultValue "",
                    strMember row "level" |> Option.defaultValue "",
                    ops.EnumerateArray() |> Seq.map _.GetString() |> List.ofSeq
                )
            | _ -> None
        | None -> None)
    |> List.ofSeq

let operationExclusionsOf (root: JsonElement) : OperationExclusion list =
    match root.TryGetProperty "operations" with
    | true, ops when ops.ValueKind = JsonValueKind.Array ->
        ops.EnumerateArray()
        |> Seq.map (fun el ->
            { Members =
                match el.TryGetProperty "members" with
                | true, v when v.ValueKind = JsonValueKind.Array ->
                    v.EnumerateArray() |> Seq.map _.GetString() |> List.ofSeq
                | _ -> []
              Class = strMember el "class" |> Option.defaultValue ""
              Reason = strMember el "reason" |> Option.defaultValue ""
              ForwardsTo = strMember el "forwardsTo"
              StandIn = strMember el "standIn"
              Through = strMember el "through" })
        |> List.ofSeq
    | _ -> []

let operationClassesOf (root: JsonElement) : Set<string> =
    match root.TryGetProperty "operationClasses" with
    | true, v when v.ValueKind = JsonValueKind.Object -> v.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
    | _ -> Set.empty

/// Whether the baseline member carries `System.Obsolete`, on itself or on a declaring type —
/// read from the loaded assembly, so an `obsolete` entry cannot claim an attribute the code lacks.
let carriesObsolete (o: Operation) : bool =
    try
        let asm = Reflection.Assembly.Load(Reflection.AssemblyName o.Package)
        let cut = o.Clr.LastIndexOf '.'
        let ty = asm.GetType(o.Clr.Substring(0, cut))
        let name = Regex.Replace(o.Clr.Substring(cut + 1), "`[0-9]+$", "")

        let rec typeObsolete (t: Type) =
            not (isNull t)
            && (t.IsDefined(typeof<ObsoleteAttribute>, false) || typeObsolete t.DeclaringType)

        not (isNull ty)
        && (typeObsolete ty
            || ty.GetMethods(
                Reflection.BindingFlags.Public
                ||| Reflection.BindingFlags.Static
                ||| Reflection.BindingFlags.Instance
               )
               |> Array.exists (fun m -> m.Name = name && m.IsDefined(typeof<ObsoleteAttribute>, false)))
    with _ ->
        false

let liveOperationInputs () : OperationInputs =
    use exclusionsDoc =
        JsonDocument.Parse(File.ReadAllText(Snapshots.repoFile "proofs/coverage-exclusions.json"))

    let exclusions = operationExclusionsOf exclusionsDoc.RootElement
    let census = liveCensus ()

    let claimedObsolete =
        exclusions
        |> List.filter (fun e -> e.Class = "obsolete")
        |> List.collect _.Members
        |> Set.ofList

    let standIns =
        exclusions
        |> List.choose _.StandIn
        |> List.distinct
        |> List.choose (fun p ->
            let full = Snapshots.repoFile p

            if p.StartsWith "tests/" && not (p.Contains "..") && File.Exists full then
                Some(p, File.ReadAllText full)
            else
                None)
        |> Map.ofList

    { Census = census
      Ladder = liveLadderOperations ()
      Rosters = Fuaran.Core.Families.families |> List.map (fun f -> f.Id, f.Operations)
      // A family's pinned-default spellings are its entry points too (Phase 507).
      Families = KitRoster.families |> List.collect (fun f -> f.Id :: f.Spellings) |> Set.ofList
      Exclusions = exclusions
      Classes = operationClassesOf exclusionsDoc.RootElement
      StandIns = standIns
      Obsolete =
        census
        |> List.filter (fun o -> claimedObsolete.Contains o.Name && carriesObsolete o)
        |> List.map _.Name
        |> Set.ofList }

// ---------------------------------------------------------------------------
//  The host evidence map (Phase 480, DECISIONS.md D224)
// ---------------------------------------------------------------------------

/// For every ladder row, the evidence a host can offer for it: the vector families whose cases an
/// extracted model answered (`answeredBy` naming the row's model, Phase 478) and the oracle functions
/// (docs/oracle-protocol.md) that ask the row's model. Derived from three inputs and nothing authored:
/// `proofs.json`, the committed vector files under `conformance/`, and the oracle's function table. A row
/// with neither is a gap in what Core offers, never in a host. `render evidence-map` writes
/// `docs/host-evidence-map.json` and its rendering beside it; `Proofs.Coverage` holds both to the render.
/// The grain is the MODEL (D224): a stamp and the oracle table both name a model, never a function in it.
module EvidenceMap =

    open Fuaran.Core

    /// The two files the render writes, repository-relative.
    let JsonPath = "docs/host-evidence-map.json"

    let MarkdownPath = "docs/host-evidence-map.md"

    /// The tool family that writes both (`Renders`).
    [<Literal>]
    let Family = "evidence-map"

    /// The corpus root the vector files are read from, repository-relative.
    let CorpusRoot = "conformance"

    /// One family's cases that one model answered.
    type FamilyOffer =
        { Family: string
          File: string
          Cases: int }

    /// One ladder row and what a host can offer for it.
    type RowOffer =
        {
            Id: string
            Level: string
            /// The F* module the row's evidence names (`proofs/<Model>.fst`), or none: an assumed or
            /// policy row, or a law row.
            Model: string option
            Families: FamilyOffer list
            /// The oracle's function names, in its table's order.
            Oracle: string list
            /// The probe family a `host-premise` row names (Phase 483), or none.
            Probe: string option
        }

        member r.Offered = not (List.isEmpty r.Families && List.isEmpty r.Oracle)

    /// One committed vector file: its family, and how many of its cases each answerer answered
    /// (`None`, the reference alone; `Some m`, the reference and the extracted model `m`).
    type FamilyStamps =
        { Family: string
          File: string
          ByModel: (string option * int) list }

    type Inputs =
        {
            /// `proofs.json`'s text.
            Ladder: string
            /// Every JSON file under the corpus root, repository-relative with `/`, and its text.
            VectorFiles: (string * string) list
            /// The oracle's functions: each name and the models it asks.
            Oracle: (string * string list) list
        }

    type EvidenceMap =
        { Rows: RowOffer list
          Families: FamilyStamps list }

    let private find (fields: (string * JVal) list) (name: string) =
        fields |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd

    let private stampOf (where: string) (v: JVal option) : Result<string option, string> =
        match v with
        | None -> Error(sprintf "%s carries no `answeredBy`" where)
        | Some s ->
            match AnsweredBy.read s with
            | Ok AnsweredBy.Reference -> Ok None
            | Ok(AnsweredBy.Model m) -> Ok(Some m)
            | Error e -> Error(sprintf "%s: %s" where e)

    /// A vector file's stamps, or `None` for a JSON file that is no family's vector file (a manifest,
    /// a schema): one with no string `family` at its root. A family file carries its cases as a
    /// `vectors` array, each stamped, or as `lines` under one header stamp (Phase 478).
    let stampsOf (path: string) (text: string) : Result<FamilyStamps option, string> =
        match Json.parse text with
        | Error e -> Error(sprintf "%s is not JSON (%s)" path e)
        | Ok(JObj fields) ->
            match find fields "family" with
            | Some(JStr family) ->
                let answers =
                    match find fields "vectors", find fields "lines" with
                    | Some(JArr vs), _ ->
                        vs
                        |> List.mapi (fun i v ->
                            match v with
                            | JObj vf -> stampOf (sprintf "%s vector %d" path i) (find vf "answeredBy")
                            | _ -> Error(sprintf "%s vector %d is not an object" path i))
                    | None, Some(JArr ls) ->
                        let header = stampOf (sprintf "%s's header" path) (find fields "answeredBy")
                        ls |> List.map (fun _ -> header)
                    | _ ->
                        [ Error(sprintf "%s (family `%s`) carries neither a `vectors` nor a `lines` array" path family) ]

                match
                    answers
                    |> List.choose (function
                        | Error e -> Some e
                        | Ok _ -> None)
                with
                | e :: _ -> Error e
                | [] ->
                    let byModel =
                        answers
                        |> List.choose (function
                            | Ok m -> Some m
                            | Error _ -> None)
                        |> List.countBy id

                    Ok(
                        Some
                            { Family = family
                              File = path
                              ByModel = byModel }
                    )
            | _ -> Ok None
        | Ok _ -> Ok None

    let private ladderRows (ladderText: string) : (string * string * string option * string option) list =
        use doc = JsonDocument.Parse ladderText

        match doc.RootElement.TryGetProperty "claims" with
        | true, c when c.ValueKind = JsonValueKind.Array ->
            c.EnumerateArray()
            |> Seq.map (fun r ->
                let model =
                    objMember r "evidence"
                    |> Option.bind (fun e -> strMember e "model")
                    |> Option.map moduleOfPath

                let probe =
                    if strMember r "class" = Some "host-premise" then
                        strMember r "probe"
                    else
                        None

                strMember r "id" |> Option.defaultValue "<no id>",
                strMember r "level" |> Option.defaultValue "<no level>",
                model,
                probe)
            |> List.ofSeq
        | _ -> []

    /// The map, or every reason it cannot be derived: an unreadable vector file, two files claiming
    /// one family, and a stamp or an oracle function naming a model no ladder row states (a stale
    /// name would offer evidence for nothing, silently).
    let derive (inputs: Inputs) : Result<EvidenceMap, string list> =
        let rows = ladderRows inputs.Ladder

        let stamped =
            inputs.VectorFiles
            |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))
            |> List.map (fun (p, t) -> stampsOf p t)

        let families =
            stamped
            |> List.choose (function
                | Ok(Some f) -> Some f
                | _ -> None)

        let ladderModels = rows |> List.choose (fun (_, _, m, _) -> m) |> Set.ofList

        let findings =
            [ if List.isEmpty rows then
                  yield "the ladder carries no `claims` rows"

              for s in stamped do
                  match s with
                  | Error e -> yield e
                  | Ok _ -> ()

              for family, files in families |> List.groupBy _.Family do
                  if List.length files > 1 then
                      yield
                          sprintf
                              "family `%s` is claimed by %s"
                              family
                              (files |> List.map (fun f -> "`" + f.File + "`") |> String.concat " and ")

              for f in families do
                  for m, _ in f.ByModel do
                      match m with
                      | Some m when not (ladderModels.Contains m) ->
                          yield sprintf "`%s` stamps `model:%s`, a model no ladder row states" f.File m
                      | _ -> ()

              for name, models in inputs.Oracle do
                  for m in models do
                      if not (ladderModels.Contains m) then
                          yield sprintf "the oracle's `%s` asks `%s`, a model no ladder row states" name m

              let familyIds = families |> List.map _.Family |> Set.ofList

              for id, _, _, probe in rows do
                  match probe with
                  | Some p when not (familyIds.Contains p) ->
                      yield sprintf "`%s` names the probe `%s`, which no vector file under conformance/ is" id p
                  | _ -> () ]

        if not (List.isEmpty findings) then
            Error findings
        else
            let offer (id, level, model, probe) =
                let families, oracle =
                    match model, probe with
                    | None, Some p ->
                        // A host premise is offered its probe, every case of it: a host reproducing
                        // the family is the discharge (Phase 483).
                        families
                        |> List.filter (fun f -> f.Family = p)
                        |> List.map (fun f ->
                            { FamilyOffer.Family = f.Family
                              File = f.File
                              Cases = f.ByModel |> List.sumBy snd }),
                        []
                    | None, None -> [], []
                    | Some m, _ ->
                        families
                        |> List.choose (fun f ->
                            f.ByModel
                            |> List.tryPick (fun (a, n) -> if a = Some m then Some n else None)
                            |> Option.map (fun n ->
                                { FamilyOffer.Family = f.Family
                                  File = f.File
                                  Cases = n })),
                        inputs.Oracle
                        |> List.filter (fun (_, models) -> List.contains m models)
                        |> List.map fst

                { Id = id
                  Level = level
                  Model = model
                  Families = families
                  Oracle = oracle
                  Probe = probe }

            Ok
                { Rows = rows |> List.map offer
                  Families = families }

    let private ordinal (a: string, _) (b: string, _) = String.CompareOrdinal(a, b)

    /// The rows of `level` no host can offer evidence for, grouped by model in ordinal order (a row
    /// with no model groups under ""). Over `proved` this is the worklist for new families.
    let uncovered (level: string) (m: EvidenceMap) : (string * string list) list =
        m.Rows
        |> List.filter (fun r -> r.Level = level && not r.Offered)
        |> List.groupBy (fun r -> r.Model |> Option.defaultValue "")
        |> List.sortWith ordinal
        |> List.map (fun (model, rs) -> model, rs |> List.map _.Id)

    /// The families no model answered a case of: certified against the reference implementation
    /// alone, so they offer nothing for a proved row.
    let referenceOnly (m: EvidenceMap) : FamilyStamps list =
        m.Families
        |> List.filter (fun f -> f.ByModel |> List.forall (fun (a, _) -> a.IsNone))

    let casesOf (f: FamilyStamps) = f.ByModel |> List.sumBy snd

    let private generatedBy =
        sprintf "GENERATED by `dotnet run --project tools/Fuaran.Core.Tools -- render %s`; never edit it." Family

    let private note =
        generatedBy
        + " For every row of the claims ladder (proofs.json), in ladder order, the evidence a host can offer for it:"
        + " `families`, the vector families under conformance/ with cases the row's model answered (`answeredBy`"
        + " names `model:<model>`; `cases` counts those cases), and `oracle`, the oracle functions"
        + " (docs/oracle-protocol.md) that ask the row's model. `model` is the F* module the row's evidence names,"
        + " absent when it names none. The grain is the model (DECISIONS.md D224). A row whose two lists are empty is"
        + " uncovered: a gap in what Core offers, not in a host. `worklist` groups the uncovered proved rows by"
        + " model; `referenceOnly` names the families no model answered. docs/host-evidence-map.md renders this file."

    let private famObj (family: string) (file: string) (cases: int) =
        JObj [ "family", JStr family; "file", JStr file; "cases", JInt cases ]

    /// The JSON file: header members, then one line per row, per worklist group and per family.
    let renderJson (m: EvidenceMap) : string =
        let str (s: string) = Json.render (JStr s)

        let rowLine (r: RowOffer) =
            JObj
                [ yield "id", JStr r.Id
                  yield "level", JStr r.Level
                  match r.Model with
                  | Some model -> yield "model", JStr model
                  | None -> ()
                  yield "families", JArr(r.Families |> List.map (fun f -> famObj f.Family f.File f.Cases))
                  yield "oracle", JArr(r.Oracle |> List.map JStr) ]
            |> Json.render

        let block (name: string) (lines: string list) =
            match lines with
            | [] -> sprintf "  %s: []" (str name)
            | _ ->
                sprintf "  %s: [\n%s\n  ]" (str name) (lines |> List.map (fun l -> "    " + l) |> String.concat ",\n")

        let groups =
            uncovered "proved" m
            |> List.map (fun (model, ids) ->
                JObj [ "model", JStr model; "rows", JArr(ids |> List.map JStr) ] |> Json.render)

        let refOnly =
            referenceOnly m
            |> List.map (fun f -> famObj f.Family f.File (casesOf f) |> Json.render)

        [ "{"
          sprintf "  %s: %s," (str "kind") (str "hostEvidenceMap")
          sprintf "  %s: %s," (str "$note") (str note)
          sprintf "  %s: %s," (str "grain") (str "model")
          block "rows" (m.Rows |> List.map rowLine) + ","
          block "worklist" groups + ","
          block "referenceOnly" refOnly
          "}" ]
        |> String.concat "\n"
        |> fun s -> s + "\n"

    let private code (s: string) = "`" + s + "`"

    /// The Markdown rendering: the schema, the offers by model, the worklist, every other uncovered row
    /// and the reference-only families.
    let renderMarkdown (m: EvidenceMap) : string =
        let offered = m.Rows |> List.filter _.Offered
        let levels = m.Rows |> List.map _.Level |> List.distinct

        let uncoveredCount level =
            m.Rows |> List.filter (fun r -> r.Level = level && not r.Offered) |> List.length

        let byModel =
            offered
            |> List.filter _.Model.IsSome
            |> List.groupBy (fun r -> r.Model |> Option.defaultValue "")
            |> List.sortWith ordinal

        let byPremise = offered |> List.filter _.Probe.IsSome

        let groupLines (groups: (string * string list) list) =
            [ for model, ids in groups do
                  yield ""

                  yield
                      sprintf
                          "- %s (%d): %s"
                          (if model = "" then "no model" else code model)
                          ids.Length
                          (ids |> List.map code |> String.concat ", ") ]

        [ yield "# Host evidence map"
          yield ""
          yield sprintf "<!-- %s -->" generatedBy
          yield ""
          yield
              "For every row of the claims ladder ([`proofs.json`](../proofs.json)), the evidence a host can offer for it: the vector families whose cases the row's model answered, and the oracle functions that ask the row's model. A row with neither is a gap in what Core offers, not in a host. Nothing here is authored: [`host-evidence-map.json`](host-evidence-map.json) is derived from the ladder, the vector files under [`conformance/`](../conformance/) and the oracle's function table ([oracle-protocol.md](oracle-protocol.md)), and this page renders it. The `Proofs.Coverage` family holds both files to the render."
          yield ""
          yield "## The schema"
          yield ""
          yield
              "- `rows[]`, one per ladder row in ladder order: `id` and `level` (the row's), `model` (the F* module its evidence names, `proofs/<model>.fst`; absent when it names none), `families[]` (`family`, `file`, and `cases`, the count of that family's cases whose `answeredBy` names `model:<model>`), and `oracle[]` (the protocol functions whose table entry asks the model). A row with both lists empty is uncovered."
          yield "- `worklist[]`: the uncovered `proved` rows, grouped by `model` in ordinal order."
          yield "- `referenceOnly[]`: the families no model answered a case of, with their case counts."
          yield ""
          yield
              "**The grain is the model** ([DECISIONS.md](../DECISIONS.md) D224). A vector's stamp and the oracle's table both name a model, never a function in it, so a family or a function is offered for every row about that model. Which of a theorem's inputs a host's run exercised is the host's own declaration."
          yield ""
          yield "## Summary"
          yield ""

          yield
              sprintf
                  "%d rows: %d offered, %d uncovered (%s)."
                  m.Rows.Length
                  offered.Length
                  (m.Rows.Length - offered.Length)
                  (levels
                   |> List.map (fun l -> sprintf "%s %d" l (uncoveredCount l))
                   |> String.concat ", ")

          yield ""
          yield "## Offers, by model"
          yield ""
          yield "| Model | Rows | Families (cases) | Oracle functions |"
          yield "|---|---|---|---|"

          for model, rs in byModel do
              let r = List.head rs

              yield
                  sprintf
                      "| %s | %d | %s | %s |"
                      (code model)
                      rs.Length
                      (match r.Families with
                       | [] -> "—"
                       | fs ->
                           fs
                           |> List.map (fun f -> sprintf "%s (%d)" (code f.Family) f.Cases)
                           |> String.concat ", ")
                      (match r.Oracle with
                       | [] -> "—"
                       | os -> os |> List.map code |> String.concat ", ")

          yield ""
          yield "## Offers, by host premise"
          yield ""

          yield
              "A `host-premise` row names no model: it is offered the probe family it names, every case of it, and a host reproducing that family is the discharge ([host-premises.md](host-premises.md))."

          yield ""
          yield "| Row | Probe family (cases) |"
          yield "|---|---|"

          for r in byPremise do
              yield
                  sprintf
                      "| %s | %s |"
                      (code r.Id)
                      (r.Families
                       |> List.map (fun f -> sprintf "%s (%d)" (code f.Family) f.Cases)
                       |> String.concat ", ")

          yield ""
          yield "## Worklist: proved rows no host can offer evidence for"
          yield ""
          yield "Grouped by model. Each group is a candidate vector family or oracle function."
          yield! groupLines (uncovered "proved" m)
          yield ""
          yield "## Every other uncovered row"
          yield ""

          yield
              "A row naming no model (an `assumed` or `policy` row, or a law row) is answered by a premise, a policy or a law run, never by an offer here. The one exception is a `host-premise` row, which is offered the probe family it names (docs/host-premises.md)."

          for level in levels |> List.filter (fun l -> l <> "proved") do
              let groups = uncovered level m

              if not (List.isEmpty groups) then
                  yield ""
                  yield sprintf "### %s" level
                  yield! groupLines groups

          yield ""
          yield "## Families no model answers"
          yield ""
          yield
              "Certified against the reference implementation alone, so a host passing one offers nothing for a proved row."
          yield ""
          yield "| Family | File | Cases |"
          yield "|---|---|---|"

          for f in referenceOnly m do
              yield sprintf "| %s | [%s](../%s) | %d |" (code f.Family) (code f.File) f.File (casesOf f) ]
        |> String.concat "\n"
        |> fun s -> s + "\n"

    /// Every JSON file under the corpus root of the repository at `root`, repository-relative with `/`.
    let vectorFilesUnder (root: string) : (string * string) list =
        let dir = Path.Combine(root, CorpusRoot)

        Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories)
        |> Array.map (fun p -> Path.GetRelativePath(root, p).Replace('\\', '/'), File.ReadAllText p)
        |> List.ofArray

    /// The oracle process's own function table (`Protocol.functions`), names and models.
    let oracleTable () : (string * string list) list =
        Fuaran.Core.Oracle.Cli.Protocol.functions
        |> List.map (fun (name, models, _) -> name, models)

    /// The inputs this repository's tree holds.
    let liveInputs (root: string) : Inputs =
        { Ladder = File.ReadAllText(Path.Combine(root, "proofs.json"))
          VectorFiles = vectorFilesUnder root
          Oracle = oracleTable () }

    /// The two files, rendered from the inputs.
    let render (inputs: Inputs) : Result<(string * string) list, string list> =
        derive inputs
        |> Result.map (fun m -> [ JsonPath, renderJson m; MarkdownPath, renderMarkdown m ])
