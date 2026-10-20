module Fuaran.Core.Tests.ConformanceFamiliesTests

// Phase 184 — the law-family roster held to the tree.
//
// `Fuaran.Core.Families` is a DECLARATION, so the one thing it cannot do for itself is notice a
// family nobody enrolled. Everything below closes that half, and each check is over a different
// axis of the same record, because the record's value is exactly that a reader can trust every
// field of it without re-deriving any of them:
//
//   * COMPLETENESS — reflection over the shipped assembly, by RETURN TYPE. Every public static
//     entry point that answers with `LawResult list` is a law family, wherever it lives; the
//     roster must name exactly those. The check quantifies over the ASSEMBLY rather than over a
//     module list, so a family added in a module nobody thought of is caught too. This is the
//     hole the phase exists to close: the previous completeness check reflected over method NAMES
//     ending in `Laws`, and `opAlgebra`, `reducer` and `compositionPilot` are not spelled that
//     way — two of the three being the very families `certify` and `certifyStream` are built from.
//   * WITNESS — re-derived from the entry point's own parameter types.
//   * OPT-IN — each family's `Election` held to what `certify` / `certifyStream` RUN (Phase 516): every
//     result an aggregate reports carries the family that emitted it, so a base-run family either
//     labels some result of an aggregate run or it does not. Read off the report, never the source.
//   * DISCHARGES — held equal, both directions, to the claims ladder's `dischargedBy` rows.
//   * CENSUS — held equal to `KitRoster.census`, so the adequacy declaration and the roster
//     can no longer name different sets.
//   * ARTEFACTS — the generated `docs/conformance-families.md` and `.json` compared to what the
//     roster renders now, naming the command that regenerates them.
//
// Every comparison runs through a pure helper beside a go-red that perturbs its input, because a
// completeness check that cannot fail is the thing it exists to detect.

open System
open System.IO
open System.Reflection
open System.Text.RegularExpressions
open Expecto
open Fuaran.Core

// ---------------------------------------------------------------------------
//  what the kit actually ships
// ---------------------------------------------------------------------------

/// The guard module. `SampleAdequacy.check` answers with `LawResult list` and is not a law family
/// — it is the adequacy guard the families EMIT THROUGH, so a roster row for it would claim a
/// domain could run it. The single exclusion is named here rather than inferred, because an
/// exclusion nobody can see is how the next family goes missing.
[<Literal>]
let private guardModule = "Fuaran.Core.SampleAdequacy"

/// The law entry points one module declares, by REFLECTION OVER RETURN TYPE.
let lawMethods (t: Type) : MethodInfo list =
    [ for m in t.GetMethods(BindingFlags.Public ||| BindingFlags.Static ||| BindingFlags.DeclaredOnly) do
          let rt = m.ReturnType

          if
              rt.IsGenericType
              && rt.GetGenericTypeDefinition() = typedefof<list<_>>
              && rt.GenericTypeArguments[0] = typeof<LawResult>
          then
              yield m ]

/// Every public law entry point the kit ships, found by REFLECTION OVER RETURN TYPE across its
/// assemblies (`KitRoster.assemblies`: two from Phase 257, one again since Phase 258 moved the
/// dataframe share out). A law family is an entry point that answers with `LawResult list`; that is
/// what the type says, and unlike a naming convention it cannot be spelled around. A key found
/// twice is REFUSED rather than silently overwritten: two families under one roster key is
/// precisely the ambiguity a consumer's call would resolve by reference order.
let private shipped () : Map<string, MethodInfo> =
    let found =
        [ for asm in KitRoster.assemblies do
              for t in asm.GetTypes() do
                  if t.IsPublic && t.FullName <> guardModule then
                      let moduleName =
                          let n = t.FullName

                          if n.StartsWith("Fuaran.Core.", StringComparison.Ordinal) then
                              n.Substring "Fuaran.Core.".Length
                          else
                              n

                      for m in lawMethods t -> moduleName + "." + m.Name, m ]

    let twice =
        found |> List.countBy fst |> List.filter (fun (_, n) -> n > 1) |> List.map fst

    if not (List.isEmpty twice) then
        failtestf "these roster keys are declared by more than one shipped module: %A" twice

    Map.ofList found

/// The witness and generator types an entry point demands, in parameter order: every parameter
/// whose type name ends in `Witness`, `Gen` or `Sink`, and the domain's `Codec` (Phase 507). A
/// function parameter, a seed and a corpus are not witnesses, and the three suffixes and the one
/// name are the kit's own vocabulary for the ones that are.
let private witnessOf (m: MethodInfo) : string list =
    let bare (t: Type) =
        let n = t.Name
        if n.Contains "`" then n.Substring(0, n.IndexOf '`') else n

    // The wire's `Codec<'T>` is a namespace-level type; `Corpus.Codec`, the corpus harness's record of
    // functions, is nested in a module and is no witness.
    let isDomainCodec (t: Type) =
        bare t = "Codec" && isNull t.DeclaringType && t.Namespace = "Fuaran.Core"

    m.GetParameters()
    |> Array.filter (fun p ->
        let n = bare p.ParameterType

        n.EndsWith("Witness", StringComparison.Ordinal)
        || n.EndsWith("Gen", StringComparison.Ordinal)
        || n.EndsWith("Sink", StringComparison.Ordinal)
        || isDomainCodec p.ParameterType)
    |> Array.map (fun p -> bare p.ParameterType)
    |> Array.toList
    |> List.distinct

// ---------------------------------------------------------------------------
//  the pure comparisons, so each one has a go-red
// ---------------------------------------------------------------------------

/// `declared` measured against `shipped`: what the roster is missing, and what it names that does
/// not exist. Pure and set-shaped, so the go-red below perturbs the inputs rather than the tree.
let compareRoster (declared: Set<string>) (shipped: Set<string>) : string list * string list =
    Set.difference shipped declared |> Set.toList, Set.difference declared shipped |> Set.toList

// ---------------------------------------------------------------------------
//  opt-in, the roster's `Election` held to what the aggregates run
// ---------------------------------------------------------------------------

/// The families an aggregate run labelled its results with (Phase 516). Every result a family emits
/// carries its roster id (`LawResult.Family`, filled by the runner since Phase 515), so what an
/// aggregate RAN is read off its report rather than off its source.
let familiesRanBy (results: LawResult list) : Set<string> =
    results |> List.map _.Family |> Set.ofList

/// The roster rows whose `Election` the aggregates' run contradicts: a `BaseRun` family no aggregate
/// ran, or an `OptIn` family one did. Empty when the roster and the run agree. Pure, so a planted
/// mis-election is shown red against the same run.
let electionDisagreements (roster: Families.LawFamily list) (ran: Set<string>) : string list =
    roster
    |> List.choose (fun f ->
        match f.Election, Set.contains f.Id ran with
        | Families.BaseRun, false -> Some(sprintf "%s is declared base run, but no aggregate ran it" f.Id)
        | Families.OptIn _, true -> Some(sprintf "%s is declared opt-in, but an aggregate ran it" f.Id)
        | _ -> None)

/// The families `certify` and `certifyStream` run over the reference witness — the base run.
let private aggregateRun: Lazy<Set<string>> =
    lazy
        (let tree =
            Conformance.certify
                Reference.nodew
                Reference.idw
                ConformanceTests.opGen
                ConformanceTests.sw
                ConformanceTests.streamGen
                OpStream.defaultHash
                516
                40

         let stream =
             Conformance.certifyStream ConformanceTests.sw ConformanceTests.streamGen OpStream.defaultHash 516 40

         Set.union (familiesRanBy tree.Results) (familiesRanBy stream.Results))

// ---------------------------------------------------------------------------
//  the claims ladder's side of the discharge relation
// ---------------------------------------------------------------------------

/// Every `(obligation row id, dischargedBy)` pair the committed ladder carries. Read with the same
/// deliberately-dumb line scan `ProofsLadderTests` uses on the same file rather than a JSON
/// dependency: a shape this misses is an empty answer and a loud failure, never a quiet pass.
let private ladderDischarges () : (string * string) list =
    let text = File.ReadAllText(Snapshots.repoFile "proofs.json")
    let lines = text.Replace("\r\n", "\n").Split('\n')
    let idLine = Regex("^\\s*\"id\"\\s*:\\s*\"([^\"]+)\"")
    let dischargedLine = Regex("^\\s*\"dischargedBy\"\\s*:\\s*\"([^\"]+)\"")

    let mutable lastId = ""
    let out = ResizeArray<string * string>()

    for l in lines do
        let m = idLine.Match l

        if m.Success then
            lastId <- m.Groups[1].Value

        let d = dischargedLine.Match l

        if d.Success then
            out.Add(lastId, d.Groups[1].Value)

    out |> List.ofSeq |> List.sortBy fst

// ---------------------------------------------------------------------------
//  the generated artefacts
// ---------------------------------------------------------------------------

/// Where `--emit-families` writes, and the suite compares.
module Export =

    let markdownPath (root: string) =
        Path.Combine(root, "conformance-families.md")

    let jsonPath (root: string) =
        Path.Combine(root, "conformance-families.json")

    /// The directory the two artefacts live in — this repository's own `docs/`, resolved through
    /// the same root marker every other owned artefact uses, so a linked worktree writes and reads
    /// its own copy.
    let root () = Snapshots.repoFile "docs"

    /// The measured half of the export — Phase 196. A roster cannot run a law, so the `cases`
    /// column is supplied by the reference run beside it rather than derived here. One call site
    /// for the emit and the freshness legs alike, so the committed artefacts and what the suite
    /// compares them against cannot be rendered from different runs.
    let cases () = ConformanceVacuityTests.cases ()

// ---------------------------------------------------------------------------

[<Tests>]
let familiesTests =
    testList
        "Conformance.Families"
        [

          testCase "the roster names exactly the law families the kit ships"
          <| fun _ ->
              // The half a declaration structurally cannot do for itself, and the reason this
              // module exists: quantified over the ASSEMBLY and over the RETURN TYPE, so neither a
              // new module nor a name that does not end in `Laws` can hide a family.
              //
              // Phase 507 — a family's pinned-default SPELLINGS are entry points of that family, so
              // they count as rostered: one family, one census cell, several names, each still a
              // public entry point reflection finds (which is what a consumer's census enumerates).
              let rostered =
                  KitRoster.families |> List.collect (fun f -> f.Id :: f.Spellings) |> Set.ofList

              let missing, phantom =
                  compareRoster rostered (shipped () |> Map.toList |> List.map fst |> Set.ofList)

              Expect.isEmpty
                  missing
                  (sprintf
                      "these law families are not in Fuaran.Core.Families — add a record for each (id, witness, opt-in, discharges) in the same commit that ships the family: %A"
                      missing)

              Expect.isEmpty phantom (sprintf "these Families records name no shipped law entry point: %A" phantom)

          testCase "the roster comparison goes red in both directions"
          <| fun _ ->
              // The guard proved the only way a guard can be. A completeness check that cannot
              // report a gap is indistinguishable from one with nothing to report.
              let missing, phantom =
                  compareRoster (Set.ofList [ "M.a" ]) (Set.ofList [ "M.a"; "M.b" ])

              Expect.equal missing [ "M.b" ] "a shipped family absent from the roster is named"
              Expect.isEmpty phantom "and nothing else is"

              let missing2, phantom2 =
                  compareRoster (Set.ofList [ "M.a"; "M.z" ]) (Set.ofList [ "M.a" ])

              Expect.isEmpty missing2 "a complete roster reports no gap"
              Expect.equal phantom2 [ "M.z" ] "a roster row naming nothing is named"

          testCase "the roster carries no duplicate id, and every field is populated"
          <| fun _ ->
              // An id and every spelling are one name space (Phase 507): a spelling that is also a
              // row, or two families claiming one spelling, would give an entry point two cells.
              let dupes =
                  KitRoster.families
                  |> List.collect (fun f -> f.Id :: f.Spellings)
                  |> List.countBy id
                  |> List.filter (fun (_, n) -> n > 1)
                  |> List.map fst

              Expect.isEmpty dupes (sprintf "duplicate roster ids: %A" dupes)

              for f in KitRoster.families do
                  Expect.equal f.Id (f.Module + "." + f.Entry) (sprintf "%s: the id is the qualified entry point" f.Id)
                  Expect.isNotEmpty f.Module (sprintf "%s: a family names its module" f.Id)
                  Expect.isNotEmpty f.Entry (sprintf "%s: a family names its entry point" f.Id)

          testCase "each family's declared witness is the one its entry point takes"
          <| fun _ ->
              // The field says what a domain must supply to run the family, which is a fact about
              // the signature — so it is read back off the signature rather than trusted.
              let ship = shipped ()

              for f in KitRoster.families do
                  // A spelling pins a parameter that is not a witness, so it takes the family's.
                  for entry in f.Id :: f.Spellings do
                      match Map.tryFind entry ship with
                      | None -> failtestf "%s is not a shipped entry point" entry
                      | Some m ->
                          Expect.equal
                              f.Witness
                              (witnessOf m)
                              (sprintf "%s declares a witness list its signature does not take" entry)

          testCase "a family's opt-in reason is checkable where the roster's own data decides it"
          <| fun _ ->
              // Phase 194 made "opt-in with no reason" unrepresentable; since Phase 506 `OptIn` and
              // `Reason` are both read off the one `Election` field, so there is no pair to disagree.
              // `NeedsWitnessCapability` is the one case the ROSTER's own data decides, so it is
              // read back rather than trusted: a family demanding a witness outside the base run's
              // sets is exactly that case, and a family inside them is not.
              let baseWitnesses =
                  KitRoster.families
                  |> List.filter (fun f -> not f.OptIn)
                  |> List.collect _.Witness
                  |> Set.ofList

              for f in KitRoster.families do
                  let beyondBase =
                      f.Witness |> List.exists (fun w -> not (Set.contains w baseWitnesses))

                  match f.Reason with
                  | Some Families.NeedsWitnessCapability ->
                      Expect.isTrue
                          beyondBase
                          (sprintf
                              "%s claims NeedsWitnessCapability but every witness it takes is one the base run already demands"
                              f.Id)
                  | Some _ ->
                      Expect.isFalse
                          beyondBase
                          (sprintf
                              "%s takes a witness beyond the base run, so its reason is NeedsWitnessCapability"
                              f.Id)
                  | None -> ()

          testCase "opt-in is exactly `not run by certify or certifyStream`, read off the roster's Election"
          <| fun _ ->
              // Phase 516 — the roster's `Election` is held to what the two aggregates RUN over the
              // reference witness, read off the families their results carry. A family folded into an
              // aggregate without its record moving would leave the roster telling a domain to call
              // something it already gets, or worse, that it need not call something it does not.
              let ran = aggregateRun.Value

              Expect.isNonEmpty ran "the aggregates ran some family, so the check reads a real run"
              Expect.isEmpty (electionDisagreements KitRoster.families ran) "every Election agrees with the run"

          testCase "the Election check goes red on a planted mis-election, either way"
          <| fun _ ->
              let ran = aggregateRun.Value

              let flip (id: string) (election: Families.Election) =
                  KitRoster.families
                  |> List.map (fun f -> if f.Id = id then { f with Election = election } else f)

              let baseRun =
                  KitRoster.families |> List.find (fun f -> f.Election = Families.BaseRun)

              let optIn =
                  KitRoster.families |> List.find (fun f -> f.Election <> Families.BaseRun)

              Expect.equal
                  (electionDisagreements (flip baseRun.Id (Families.OptIn Families.StrongerPromise)) ran)
                  [ sprintf "%s is declared opt-in, but an aggregate ran it" baseRun.Id ]
                  "a base-run family declared opt-in is named"

              Expect.equal
                  (electionDisagreements (flip optIn.Id Families.BaseRun) ran)
                  [ sprintf "%s is declared base run, but no aggregate ran it" optIn.Id ]
                  "an opt-in family declared base run is named"

          testCase "the discharge relation is the claims ladder's, seen from the other side"
          <| fun _ ->
              // Both directions. An obligation naming a family the roster does not carry is the
              // defect this phase is named for; a roster row claiming an obligation the ladder does
              // not raise is the same defect mirrored.
              let ladder = ladderDischarges ()

              Expect.isNonEmpty
                  ladder
                  "the ladder carries `dischargedBy` rows — an empty read is a parse failure, not a clean bill"

              Expect.equal
                  (ladder |> List.sort)
                  (KitRoster.obligations |> List.sort)
                  "Fuaran.Core.Families.obligations and proofs.json's `dischargedBy` rows are one relation; they disagree"

          testCase "the adequacy census and the roster name the same families"
          <| fun _ ->
              // Two declarations over one set. Until this phase they were kept by two different
              // rules and the census was the loser: `opAlgebra`, `reducer` and `compositionPilot`
              // were absent from it, and it is the census a projection reads as the roster.
              let censused = KitRoster.census |> List.map fst |> Set.ofList
              let missing, phantom = compareRoster censused (Set.ofList KitRoster.ids)

              Expect.isEmpty
                  missing
                  (sprintf "these roster families have no census row (SampleAdequacy.census): %A" missing)

              Expect.isEmpty phantom (sprintf "these census rows name no roster family: %A" phantom)

          testCase "the JSON export is the canonical rendering, one family per line"
          <| fun _ ->
              // Phase 506 — every value is written by `Canonical.write Rendering.CrossHost`, and the document is that
              // rendering broken at the families array. So the lines, trimmed and joined, are the
              // canonical rendering of what they parse to: member order, escaping and number layout
              // are the wire's own, not this renderer's.
              let json = KitRoster.toJsonWith (Export.cases ())
              Expect.isTrue (json.EndsWith "\n") "the export ends with a newline"

              match Json.parse json with
              | Error e -> failtestf "the export does not parse as JSON: %s" e
              | Ok parsed ->
                  let lines = json.Split('\n')
                  let joined = lines |> Array.map (fun l -> l.Trim()) |> String.concat ""

                  Expect.equal
                      joined
                      (Canonical.write Rendering.CrossHost parsed)
                      "the export is Canonical.write Rendering.CrossHost of itself, broken into lines"

                  for f in KitRoster.families do
                      Expect.equal
                          (lines
                           |> Array.filter (fun l -> l.Contains("\"id\":\"" + f.Id + "\""))
                           |> Array.length)
                          1
                          (sprintf "%s is one line of the export" f.Id)

              // The go-red: a document whose members are out of canonical order is not its own
              // rendering, so the check above can fail.
              let reordered = "{\"kind\":\"k\",\"families\":[]}"

              match Json.parse reordered with
              | Ok v ->
                  Expect.notEqual
                      reordered
                      (Canonical.write Rendering.CrossHost v)
                      "an out-of-order document is not canonical"
              | Error e -> failtestf "the go-red document does not parse: %s" e

          testCase "a schema bump is a recorded contract move: STABILITY.md names the reader and the current schema"
          <| fun _ ->
              // Phase 498 — the export has a reader outside this repository, which keys on `kind`
              // and reads `families[].id` and `families[].cases`. Until this phase the source called
              // every bump free on the claim that no such reader existed. The contract entry carries
              // the schema number it was written against, so a bump that does not revisit the entry
              // is red here rather than discovered by the reader.
              let json = KitRoster.toJsonWith (Export.cases ())
              let schema = string Families.schema

              Expect.stringContains json ("\"schema\":" + schema) "the export carries the schema the module declares"

              let entry =
                  File.ReadAllLines(Snapshots.repoFile "STABILITY.md")
                  |> Array.tryFind (fun l ->
                      l.Contains "`docs/conformance-families.json` has a reader outside this repository")

              match entry with
              | None -> failtest "STABILITY.md carries no entry for the families export's outside reader"
              | Some line ->
                  for read in
                      [ "`kind`"
                        "`families[].id`"
                        "`families[].cases`"
                        sprintf "`schema: %s`" schema ] do
                      Expect.stringContains
                          line
                          read
                          (sprintf
                              "the STABILITY.md entry for the families export does not name %s — a schema bump revisits the entry in the commit that makes it"
                              read)

          testCase "the families document names the compute strand's package by its current id"
          <| fun _ ->
              // Phase 498 — `Fuaran.Core.DataFrame.Conformance` stopped at `0.34.0`; the compute
              // repository ships the dataframe families as `Fuaran.Compute.Conformance` since its
              // `0.36.0`. The generated document said the old id until this phase.
              let md = KitRoster.toMarkdownWith (Export.cases ())
              Expect.stringContains md "`Fuaran.Compute.Conformance`" "the document names the current id"

              Expect.isFalse
                  (md.Contains "Fuaran.Core.DataFrame.Conformance")
                  "the document names an id that stopped at 0.34.0"

          testCase "the export ROUND-TRIPS: parsed back, it is the roster"
          <| fun _ ->
              // The export is what an offline reader consumes, so "documented shape" has to mean a
              // reader can recover the roster from it. Parsed with the kit's own JSON parser, every
              // family object carries exactly the documented members, and each member is the
              // record's own reading of itself (the adequacy and refusal TOKENS are the renderer's,
              // held by the vacuity suite).
              let strList v =
                  match v with
                  | JArr xs ->
                      xs
                      |> List.map (function
                          | JStr s -> s
                          | other -> failtestf "expected a string in an array, got %A" other)
                  | other -> failtestf "expected an array, got %A" other

              let documented =
                  [ "adequacy"
                    "cases"
                    "discharges"
                    "entry"
                    "id"
                    "module"
                    "optIn"
                    "package"
                    "refusal"
                    "spellings"
                    "witness" ]

              match Json.parse (KitRoster.toJsonWith (Export.cases ())) with
              | Error e -> failtestf "the export does not parse as JSON: %s" e
              | Ok(JObj top) ->
                  let member_ name =
                      match top |> List.tryFind (fun (k, _) -> k = name) with
                      | Some(_, v) -> v
                      | None -> failtestf "the export carries no `%s`" name

                  Expect.equal (top |> List.map fst) [ "families"; "kind"; "packages"; "schema" ] "top-level members"
                  Expect.equal (member_ "kind") (JStr "fuaran.core.conformance.families") "kind"
                  Expect.equal (member_ "schema") (JInt Families.schema) "schema"

                  Expect.equal
                      (member_ "packages")
                      (JArr(KitRoster.rosters |> List.map (fun r -> JStr r.Package)))
                      "packages"

                  let objects =
                      match member_ "families" with
                      | JArr xs ->
                          xs
                          |> List.map (function
                              | JObj fields -> fields
                              | other -> failtestf "a families entry is not an object: %A" other)
                      | other -> failtestf "`families` is not an array: %A" other

                  let idOf (fields: (string * JVal) list) =
                      match fields |> List.tryFind (fun (k, _) -> k = "id") with
                      | Some(_, JStr id) -> id
                      | other -> failtestf "a family object carries no string `id`: %A" other

                  Expect.equal
                      (objects |> List.map idOf)
                      KitRoster.ids
                      "the export names exactly the roster's families, in id order"

                  for fields in objects do
                      let id = idOf fields

                      let get name =
                          match fields |> List.tryFind (fun (k, _) -> k = name) with
                          | Some(_, v) -> v
                          | None -> failtestf "%s carries no `%s`" id name

                      let f = KitRoster.tryFind id |> Option.get
                      // `reason` is present exactly for an opt-in family: this wire model has no null.
                      let expected =
                          (if f.OptIn then documented @ [ "reason" ] else documented) |> List.sort

                      Expect.equal (fields |> List.map fst |> List.sort) expected (sprintf "%s: its members" id)
                      Expect.equal (get "module") (JStr f.Module) (sprintf "%s: module" id)
                      Expect.equal (get "entry") (JStr f.Entry) (sprintf "%s: entry" id)

                      Expect.equal
                          (get "package")
                          (JStr(KitRoster.packageOf id |> Option.defaultValue "?"))
                          (sprintf "%s: package" id)

                      Expect.equal (strList (get "witness")) f.Witness (sprintf "%s: witness" id)
                      Expect.equal (get "optIn") (JBool f.OptIn) (sprintf "%s: optIn" id)
                      Expect.equal (strList (get "discharges")) f.Discharges (sprintf "%s: discharges" id)
                      Expect.equal (strList (get "spellings")) f.Spellings (sprintf "%s: spellings" id)

                      // The reason round-trips through its wire token, so a renderer that dropped or
                      // misspelled one is caught here too.
                      let reason =
                          match fields |> List.tryFind (fun (k, _) -> k = "reason") with
                          | None -> None
                          | Some(_, JStr "needs-witness-capability") -> Some Families.NeedsWitnessCapability
                          | Some(_, JStr "seam-not-every-domain-has") -> Some Families.SeamNotEveryDomainHas
                          | Some(_, JStr "stronger-promise") -> Some Families.StrongerPromise
                          | Some(_, JStr "no-witness-to-certify") -> Some Families.NoWitnessToCertify
                          | other -> failtestf "`reason` is not a known opt-in token: %A" other

                      Expect.equal reason f.Reason (sprintf "%s: reason" id)
              | Ok other -> failtestf "the export is not a JSON object: %A" other

          testCase "both renderings are sorted by id, so a diff shows only what moved"
          <| fun _ ->
              let sorted = KitRoster.ids
              let md = KitRoster.toMarkdownWith (Export.cases ())

              let positions =
                  sorted
                  |> List.map (fun id -> md.IndexOf("| `" + id + "` |", StringComparison.Ordinal))

              Expect.isTrue (positions |> List.forall (fun p -> p >= 0)) "every family has a table row"
              Expect.equal positions (List.sort positions) "the markdown table is in id order"

              let jsonPositions =
                  sorted
                  |> List.map (fun id ->
                      (KitRoster.toJsonWith (Export.cases ()))
                          .IndexOf("\"id\":\"" + id + "\"", StringComparison.Ordinal))

              Expect.equal jsonPositions (List.sort jsonPositions) "the JSON families array is in id order" ]
