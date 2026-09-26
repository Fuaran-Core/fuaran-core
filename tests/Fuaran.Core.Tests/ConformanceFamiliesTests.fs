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
//   * OPT-IN — read off `certify` / `certifyStream`'s own source: a family either appears in an
//     aggregate's body or it does not.
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

/// Phase 257 — the dataframe families' own home. The roster keys them by the spellings a consumer
/// calls today, `Conformance.<family>`, which the forwarding module in the same assembly carries;
/// the home module is held to that forwarding module member for member by its own test below,
/// so it is excluded here rather than rostered twice. Phase 258 removes the forwards and re-keys
/// the roster to this module.
[<Literal>]
let private forwardedHome = "Fuaran.Core.DataFrameConformance"

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

/// Every public law entry point the kit ships, found by REFLECTION OVER RETURN TYPE across both
/// of its assemblies (Phase 257). A law family is an entry point that answers with `LawResult
/// list`; that is what the type says, and unlike a naming convention it cannot be spelled around.
/// Two assemblies can each declare a module of the same name — the forwards do exactly that — so
/// a key found twice is REFUSED rather than silently overwritten: two families under one roster
/// key is precisely the ambiguity a consumer's call would resolve by reference order.
let private shipped () : Map<string, MethodInfo> =
    let found =
        [ for asm in KitRoster.assemblies do
              for t in asm.GetTypes() do
                  if t.IsPublic && t.FullName <> guardModule && t.FullName <> forwardedHome then
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
/// whose type name ends in `Witness`, `Gen` or `Sink`. A function parameter, a seed and a corpus
/// are not witnesses, and the three suffixes are the kit's own vocabulary for the ones that are.
let private witnessOf (m: MethodInfo) : string list =
    let bare (t: Type) =
        let n = t.Name
        if n.Contains "`" then n.Substring(0, n.IndexOf '`') else n

    m.GetParameters()
    |> Array.map (fun p -> bare p.ParameterType)
    |> Array.filter (fun n ->
        n.EndsWith("Witness", StringComparison.Ordinal)
        || n.EndsWith("Gen", StringComparison.Ordinal)
        || n.EndsWith("Sink", StringComparison.Ordinal))
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
//  opt-in, read off the aggregates' own source
// ---------------------------------------------------------------------------

let private conformanceSource =
    lazy (File.ReadAllText(Snapshots.repoFile "src/Fuaran.Core.Conformance/Conformance.fs"))

/// The body of a top-level `let <name>` binding in the kit's own source: from the binding line to
/// the next line that opens a sibling binding or its doc comment. Comment lines are stripped, so
/// a family NAMED in a prose aside is never read as a family CALLED.
let private bodyOf (name: string) (source: string) : string =
    let lines = source.Replace("\r\n", "\n").Split('\n')

    let opens =
        Regex(@"^    let " + Regex.Escape name + @"$|^    let " + Regex.Escape name + @"\s")

    match lines |> Array.tryFindIndex opens.IsMatch with
    | None -> failtestf "the kit no longer declares a top-level `%s`" name
    | Some start ->
        let after = lines[start + 1 ..]

        let stop =
            after
            |> Array.tryFindIndex (fun l ->
                l.StartsWith("    let ", StringComparison.Ordinal)
                || l.StartsWith("    ///", StringComparison.Ordinal))
            |> Option.defaultValue after.Length

        after[.. stop - 1]
        |> Array.filter (fun l -> not ((l.TrimStart()).StartsWith("//", StringComparison.Ordinal)))
        |> String.concat "\n"

/// The families an aggregate's body invokes by name.
let private invokedBy (aggregate: string) (entries: string list) : Set<string> =
    let body = bodyOf aggregate conformanceSource.Value

    entries
    |> List.filter (fun e -> Regex.IsMatch(body, @"(?<![A-Za-z0-9_'])" + Regex.Escape e + @"(?![A-Za-z0-9_'])"))
    |> Set.ofList

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

    let write (dir: string) =
        Directory.CreateDirectory dir |> ignore
        let measured = cases ()
        File.WriteAllText(markdownPath dir, KitRoster.toMarkdownWith measured)
        File.WriteAllText(jsonPath dir, KitRoster.toJsonWith measured)

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
              let missing, phantom =
                  compareRoster (Set.ofList KitRoster.ids) (shipped () |> Map.toList |> List.map fst |> Set.ofList)

              Expect.isEmpty
                  missing
                  (sprintf
                      "these law families are not in Fuaran.Core.Families — add a record for each (id, witness, opt-in, discharges) in the same commit that ships the family: %A"
                      missing)

              Expect.isEmpty phantom (sprintf "these Families records name no shipped law entry point: %A" phantom)

          testCase
              "every dataframe family is forwarded under its pre-split spelling, and every forward reaches its home"
          <| fun _ ->
              // Phase 257 — the forwards are what keep `Conformance.<family>` compiling for a
              // consumer that references both packages. Held member for member, both directions,
              // by name and parameter types, so a family added to its home without a forward (or a
              // forward left behind by a removed family) fails here; then each seed-and-iterations
              // forward is RUN beside its home, so a forward that called the wrong family does too.
              let dfAsm = KitRoster.assemblies |> List.last

              let membersOf (fullName: string) =
                  match dfAsm.GetType fullName with
                  | null -> failtestf "%s is not in %s" fullName (dfAsm.GetName().Name)
                  | t ->
                      t.GetMethods(BindingFlags.Public ||| BindingFlags.Static ||| BindingFlags.DeclaredOnly)
                      |> Array.map (fun m ->
                          m.Name
                          + "("
                          + (m.GetParameters()
                             |> Array.map (fun p -> p.ParameterType.Name)
                             |> String.concat ",")
                          + ")")
                      |> Set.ofArray

              let home = membersOf forwardedHome
              let forwards = membersOf "Fuaran.Core.Conformance"
              let unforwarded, orphaned = compareRoster forwards home

              Expect.isEmpty
                  unforwarded
                  "these DataFrameConformance members have no forward in the Conformance module beside them"

              Expect.isEmpty orphaned "these forwards name no DataFrameConformance member"

              let homeType = dfAsm.GetType forwardedHome
              let fwdType = dfAsm.GetType "Fuaran.Core.Conformance"

              for m in lawMethods homeType do
                  let ps = m.GetParameters() |> Array.map _.ParameterType

                  if ps = [| typeof<int>; typeof<int> |] then
                      let run (t: Type) =
                          t.GetMethod(m.Name, ps).Invoke(null, [| box 20260926; box 3 |]) :?> LawResult list

                      Expect.equal
                          (run fwdType)
                          (run homeType)
                          (sprintf
                              "Conformance.%s must answer exactly what DataFrameConformance.%s answers"
                              m.Name
                              m.Name)

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
              let dupes =
                  KitRoster.ids
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
                  match Map.tryFind f.Id ship with
                  | None -> failtestf "%s is not a shipped entry point" f.Id
                  | Some m ->
                      Expect.equal
                          f.Witness
                          (witnessOf m)
                          (sprintf "%s declares a witness list its signature does not take" f.Id)

          testCase "a family is opt-in exactly when it says WHY, and the reason is checkable where it can be"
          <| fun _ ->
              // Phase 194. `OptIn` is DERIVED from `Reason` at every construction site in
              // `Families`, so the first half of this cannot fail while that derivation stands —
              // it is here to fail LOUDLY if someone ever re-introduces the two as independent
              // fields, which is the shape that let a family be opt-in with no stated reason.
              for f in KitRoster.families do
                  Expect.equal
                      f.OptIn
                      (Option.isSome f.Reason)
                      (sprintf "%s: OptIn and Reason disagree — an opt-in family must say why" f.Id)

              // The go-red, on the predicate rather than on the roster: the check must LOSE on a
              // family that claims opt-in and gives no reason. Constructed here because the roster
              // can no longer express one.
              let unexplained: Families.LawFamily =
                  { Id = "Conformance.unexplainedLaws"
                    Module = "Conformance"
                    Entry = "unexplainedLaws"
                    Witness = []
                    OptIn = true
                    Reason = None
                    Discharges = [] }

              Expect.notEqual
                  unexplained.OptIn
                  (Option.isSome unexplained.Reason)
                  "the check cannot fail: an opt-in family with no reason passed it"

              // `NeedsWitnessCapability` is the one case the ROSTER's own data decides, so it is
              // read back rather than trusted: a family demanding a witness outside the base run's
              // sets is exactly that case, and a family inside them is not.
              let baseWitnesses =
                  KitRoster.families
                  |> List.filter (fun f -> not f.OptIn)
                  |> List.collect (fun f -> f.Witness)
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

          testCase "opt-in is exactly `not folded into certify or certifyStream`"
          <| fun _ ->
              // Read off the aggregates' own bodies: a family folded into one of them without its
              // record moving would leave the roster telling a domain to call something it already
              // gets, or worse, that it need not call something it does not.
              let conformance =
                  KitRoster.families |> List.filter (fun f -> f.Module = "Conformance")

              let entries = conformance |> List.map (fun f -> f.Entry)

              let folded =
                  Set.union (invokedBy "certify" entries) (invokedBy "certifyStream" entries)

              for f in conformance do
                  Expect.equal
                      f.OptIn
                      (not (Set.contains f.Entry folded))
                      (sprintf
                          "%s is declared %s but %s in an aggregate's body"
                          f.Id
                          (if f.OptIn then "opt-in" else "base run")
                          (if Set.contains f.Entry folded then
                               "IS called"
                           else
                               "is NOT called"))

              // No aggregate exists outside `Conformance`, so every family in another module is
              // opt-in by construction — stated rather than assumed, since a second aggregate
              // would silently invalidate the check above.
              for f in KitRoster.families |> List.filter (fun f -> f.Module <> "Conformance") do
                  Expect.isTrue f.OptIn (sprintf "%s is outside Conformance, so no aggregate can run it" f.Id)

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
                  (sprintf
                      "these roster families have no census row (SampleAdequacy.census or DataFrameFamilies.census): %A"
                      missing)

              Expect.isEmpty phantom (sprintf "these census rows name no roster family: %A" phantom)

          testCase "the generated docs/conformance-families.md is what the roster renders"
          <| fun _ ->
              let path = Export.markdownPath (Export.root ())

              Expect.isTrue
                  (File.Exists path)
                  (sprintf
                      "this repository carries no %s — run `dotnet run --project tests/Fuaran.Core.Tests -- --emit-families` and commit the result"
                      path)

              Expect.equal
                  (OwnedConformance.fingerprint (File.ReadAllText path))
                  (OwnedConformance.fingerprint (KitRoster.toMarkdownWith (Export.cases ())))
                  "the committed docs/conformance-families.md is not what the roster renders — re-run `--emit-families` and commit it"

          testCase "the generated docs/conformance-families.json is what the roster renders"
          <| fun _ ->
              let path = Export.jsonPath (Export.root ())

              Expect.isTrue
                  (File.Exists path)
                  (sprintf
                      "this repository carries no %s — run `dotnet run --project tests/Fuaran.Core.Tests -- --emit-families` and commit the result"
                      path)

              Expect.equal
                  (OwnedConformance.fingerprint (File.ReadAllText path))
                  (OwnedConformance.fingerprint (KitRoster.toJsonWith (Export.cases ())))
                  "the committed docs/conformance-families.json is not what the roster renders — re-run `--emit-families` and commit it"

          testCase "the JSON export carries the documented shape, for every family"
          <| fun _ ->
              // The shape is a contract `STABILITY.md` documents and an offline projection reads,
              // so it is asserted rather than left to the renderer. Not a JSON parse: the point is
              // that the exact member spellings a reader keys off are present.
              let json = KitRoster.toJsonWith (Export.cases ())

              Expect.stringContains json "\"kind\": \"fuaran.core.conformance.families\"" "the export names its kind"
              Expect.stringContains json "\"schema\": 5" "the export carries a schema version"

              Expect.stringContains
                  json
                  "\"packages\": [\"Fuaran.Core.Conformance\", \"Fuaran.Core.DataFrame.Conformance\"]"
                  "the export names the packages it composes (Phase 257)"

              for f in KitRoster.families do
                  Expect.stringContains json ("\"id\": \"" + f.Id + "\"") (sprintf "%s appears in the export" f.Id)

                  Expect.stringContains
                      json
                      ("\"id\": \""
                       + f.Id
                       + "\",
      \"module\": \""
                       + f.Module
                       + "\",
      \"entry\": \""
                       + f.Entry
                       + "\",
      \"package\": \""
                       + (KitRoster.packageOf f.Id |> Option.defaultValue "?")
                       + "\"")
                      (sprintf "%s names the package it ships from" f.Id)

              for member_ in
                  [ "\"module\":"
                    "\"entry\":"
                    "\"witness\":"
                    "\"optIn\":"
                    "\"discharges\":"
                    "\"cases\":"
                    "\"adequacy\":"
                    "\"refusal\":" ] do
                  Expect.stringContains json member_ (sprintf "the export carries %s" member_)

              Expect.isTrue (json.EndsWith "\n") "the export ends with a newline"

          testCase "the export ROUND-TRIPS: parsed back, it is the roster"
          <| fun _ ->
              // The export is what an offline reader consumes, so "documented shape" has to mean a
              // reader can actually recover the roster from it — not merely that the right
              // substrings appear. Parsed with the kit's own portable JSON parser and rebuilt into
              // `LawFamily` records, it must equal `KitRoster.families` exactly. This is the leg
              // that would catch an escaping bug, a member dropped by a renderer edit, or a
              // `witness` list silently flattened to a string.
              let mk id m entry witness optIn reason discharges : Families.LawFamily =
                  { Id = id
                    Module = m
                    Entry = entry
                    Witness = witness
                    OptIn = optIn
                    Reason = reason
                    Discharges = discharges }

              let strList v =
                  match v with
                  | JArr xs ->
                      xs
                      |> List.map (function
                          | JStr s -> s
                          | other -> failtestf "expected a string in an array, got %A" other)
                  | other -> failtestf "expected an array, got %A" other

              match Json.parse (KitRoster.toJsonWith (Export.cases ())) with
              | Error e -> failtestf "the export does not parse as JSON: %s" e
              | Ok(JObj top) ->
                  let member_ name =
                      match top |> List.tryFind (fun (k, _) -> k = name) with
                      | Some(_, v) -> v
                      | None -> failtestf "the export carries no `%s`" name

                  Expect.equal (member_ "kind") (JStr "fuaran.core.conformance.families") "kind"
                  Expect.equal (member_ "schema") (JInt 5) "schema"

                  Expect.equal
                      (member_ "packages")
                      (JArr(KitRoster.rosters |> List.map (fun r -> JStr r.Package)))
                      "packages"

                  let rebuilt =
                      match member_ "families" with
                      | JArr xs ->
                          xs
                          |> List.map (function
                              | JObj fields ->
                                  let get name =
                                      match fields |> List.tryFind (fun (k, _) -> k = name) with
                                      | Some(_, v) -> v
                                      | None -> failtestf "a family object carries no `%s`" name

                                  // Phase 194: `reason` is absent for a base-run family, so it is
                                  // read with a lookup that tolerates absence — `get` fails on it.
                                  let tryGet name =
                                      fields |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd

                                  let str name =
                                      match get name with
                                      | JStr s -> s
                                      | other -> failtestf "`%s` is not a string: %A" name other

                                  mk
                                      (str "id")
                                      (str "module")
                                      (str "entry")
                                      (strList (get "witness"))
                                      (match get "optIn" with
                                       | JBool b -> b
                                       | other -> failtestf "`optIn` is not a boolean: %A" other)
                                      // Phase 194: `reason` round-trips through its wire token, so a
                                      // renderer that dropped or misspelled one is caught here too.
                                      (match tryGet "reason" with
                                       | None -> None
                                       | Some(JStr "needs-witness-capability") -> Some Families.NeedsWitnessCapability
                                       | Some(JStr "seam-not-every-domain-has") -> Some Families.SeamNotEveryDomainHas
                                       | Some(JStr "stronger-promise") -> Some Families.StrongerPromise
                                       | other -> failtestf "`reason` is not a known opt-in token: %A" other)
                                      (strList (get "discharges"))
                              | other -> failtestf "a families entry is not an object: %A" other)
                      | other -> failtestf "`families` is not an array: %A" other

                  Expect.equal
                      rebuilt
                      (KitRoster.families |> List.sortBy (fun f -> f.Id))
                      "the roster recovered from the export is the roster it was rendered from"
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
                          .IndexOf("\"id\": \"" + id + "\"", StringComparison.Ordinal))

              Expect.equal jsonPositions (List.sort jsonPositions) "the JSON families array is in id order" ]
