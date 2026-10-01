module Fuaran.Core.Tests.SurfaceLawsTests

// Phase 331 — the tests of the kit's surface laws (`SurfaceLaws.fs`): the witness-record field freeze,
// moved verbatim from `ConformanceTests.fs`.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.Reference2
open Fuaran.Core.Tests.Reference.Counter
open Fuaran.Core.Tests.ConformanceTests

// ---------------------------------------------------------------------------
//  Phase 232 — the witness-record field freeze, held by `witnessSurfaceLaws`
// ---------------------------------------------------------------------------

/// The go-red decoy: `NodeWitness`'s four fields and ONE more. Only the field NAMES and their
/// ORDER are read, so the field types are placeholders.
type DecoyWidenedWitness =
    { Id: int
      KindTag: int
      Children: int
      ReplaceChildren: int
      Label: int }

/// `NodeWitness`'s four fields, reordered — a move the compiler accepts at every construction
/// site (records are built by name) and the freeze still refuses.
type DecoyReorderedWitness =
    { KindTag: int
      Id: int
      Children: int
      ReplaceChildren: int }

/// A public record named `…Witness` that nobody classified.
type DecoyUnclassifiedWitness = { Anything: int }

/// `KeyedWitness`'s five fields with two of them swapped — the go-red decoy for a record Phase 330
/// brought into the freeze. Qualified access keeps its labels from capturing record inference for
/// the real witness records built later in this file.
[<RequireQualifiedAccess>]
type DecoySwappedKeyedWitness =
    { Surface: int
      ReplaceKeyedChildren: int
      KeyedChildren: int
      PlaceKeyedChild: int
      IdsUnique: int }

/// Every public type in the Fuaran.Core assemblies this test project was built against — wider
/// than the kit's own reference closure (it also holds the packages the kit does not reference),
/// and excluding the test assemblies, whose decoys above are not shipped witnesses.
let private shippedPublicTypes: Lazy<System.Type list> =
    lazy
        (System.IO.Directory.GetFiles(System.AppContext.BaseDirectory, "Fuaran.Core*.dll")
         |> Array.map System.IO.Path.GetFileNameWithoutExtension
         |> Array.filter (fun n -> not (n.StartsWith("Fuaran.Core.Tests", System.StringComparison.Ordinal)))
         |> Array.sort
         |> Array.toList
         |> List.collect (fun n ->
             System.Reflection.Assembly.Load(System.Reflection.AssemblyName n).GetExportedTypes()
             |> Array.toList))

let private pinnedOf (record: string) : string list =
    Conformance.frozenWitnessFields
    |> List.tryFind (fun (n, _) -> n = record)
    |> Option.map snd
    |> Option.defaultWith (fun () -> failtestf "%s is not a frozen witness" record)


[<Tests>]
let witnessSurfaceLawTests =
    testList
        "Conformance.witnessSurfaceLaws (Phase 232)"
        [ testCase "green at this Core: one law per frozen record, plus the coverage law"
          <| fun _ ->
              let results = Conformance.witnessSurfaceLaws ()

              let fails =
                  results
                  |> List.filter (fun r -> not r.Passed)
                  |> List.map (fun r -> r.Law + " — " + defaultArg r.Counterexample "")

              if not (List.isEmpty fails) then
                  failtestf "witnessSurfaceLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal
                  (List.length results)
                  (List.length Conformance.frozenWitnessFields + 1)
                  "one field law per frozen record and one coverage law"

              for record, _ in Conformance.frozenWitnessFields do
                  Expect.exists results (fun r -> r.Law.Contains("(" + record + ")")) (sprintf "no law names %s" record)

          testCase "the pinned records are exactly the ones STABILITY.md freezes, and STABILITY.md names the family"
          <| fun _ ->
              // `Snapshots` compiles after this file, so the repository root is found the same way
              // it finds it: the nearest ancestor holding the solution.
              let rec root (dir: System.IO.DirectoryInfo) =
                  if isNull dir then
                      failtest "no ancestor of the test output holds Fuaran.Core.slnx"
                  elif System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Fuaran.Core.slnx")) then
                      dir.FullName
                  else
                      root dir.Parent

              let stability =
                  System.IO.File.ReadAllText(
                      System.IO.Path.Combine(
                          root (System.IO.DirectoryInfo System.AppContext.BaseDirectory),
                          "STABILITY.md"
                      )
                  )

              let heading = "## Witness-record field freeze"
              let at = stability.IndexOf(heading, System.StringComparison.Ordinal)
              Expect.isGreaterThanOrEqual at 0 "STABILITY.md no longer carries the freeze section"

              let section =
                  let body = stability.Substring(at + heading.Length)
                  let next = body.IndexOf("\n## ", System.StringComparison.Ordinal)
                  if next < 0 then body else body.Substring(0, next)

              // The freeze's opening sentence enumerates the records in backticks, up to "are".
              let opening =
                  section.Substring(0, section.IndexOf(") are", System.StringComparison.Ordinal))

              let named =
                  System.Text.RegularExpressions.Regex.Matches(opening, @"`([A-Za-z]+Witness)`")
                  |> Seq.map (fun m -> m.Groups[1].Value)
                  |> Seq.sort
                  |> Seq.toList

              Expect.equal
                  named
                  (Conformance.frozenWitnessFields |> List.map fst |> List.sort)
                  "the records STABILITY.md freezes and the records Conformance.frozenWitnessFields pins disagree"

              Expect.stringContains
                  section
                  "Conformance.witnessSurfaceLaws"
                  "the freeze section names the family that holds it"

          testCase "go-red: a record with one field more than its pin fails, naming the record and the field"
          <| fun _ ->
              let r =
                  Conformance.witnessFieldsLaw "NodeWitness" (pinnedOf "NodeWitness") typeof<DecoyWidenedWitness>

              Expect.isFalse r.Passed "a widened NodeWitness passed the freeze"
              Expect.stringContains r.Law "(NodeWitness)" "the law names the record"
              Expect.stringContains (defaultArg r.Counterexample "") "added: Label" "the counterexample names the field"

              // ...and the control: the real record under the same law is green, so the red above is
              // the extra field and not the law.
              let control =
                  Conformance.witnessFieldsLaw "NodeWitness" (pinnedOf "NodeWitness") typeof<NodeWitness<obj, obj>>

              Expect.isTrue control.Passed "the real NodeWitness fails its own pin"

          testCase "go-red: a reordered record fails too — the compiler accepts it, the freeze does not"
          <| fun _ ->
              let r =
                  Conformance.witnessFieldsLaw "NodeWitness" (pinnedOf "NodeWitness") typeof<DecoyReorderedWitness>

              Expect.isFalse r.Passed "a reordered NodeWitness passed the freeze"
              Expect.stringContains (defaultArg r.Counterexample "") "reordered" "the counterexample says what moved"

          testCase "go-red: a record Phase 330 froze fails with two of its fields swapped"
          <| fun _ ->
              let r =
                  Conformance.witnessFieldsLaw "KeyedWitness" (pinnedOf "KeyedWitness") typeof<DecoySwappedKeyedWitness>

              Expect.isFalse r.Passed "a KeyedWitness with two fields swapped passed the freeze"
              Expect.stringContains r.Law "(KeyedWitness)" "the law names the record"
              Expect.stringContains (defaultArg r.Counterexample "") "reordered" "the counterexample says what moved"

              let control =
                  Conformance.witnessFieldsLaw "KeyedWitness" (pinnedOf "KeyedWitness") typeof<KeyedWitness<obj, obj>>

              Expect.isTrue control.Passed "the real KeyedWitness fails its own pin"

          testCase "Phase 330, 313 and 298: fourteen records are frozen and none is declared outside the freeze"
          <| fun _ ->
              Expect.equal
                  (Conformance.frozenWitnessFields |> List.map fst)
                  [ "IdWitness"
                    "NodeWitness"
                    "StreamWitness"
                    "ArtifactWitness"
                    "AiSurfaceWitness"
                    "ProjectionWitness"
                    "ObserverWitness"
                    "CapabilitySeamWitness"
                    "QuerySeamWitness"
                    "CapabilityPipelineWitness"
                    "ConstructWitness"
                    "KeyedWitness"
                    "EvaluatorWitness"
                    "RefWitness" ]
                  "the frozen records, in pin order"

              Expect.isEmpty Conformance.unfrozenWitnesses "a witness is declared outside the freeze again"

          testCase "go-red: a type that is not a record fails rather than reading as an empty field set"
          <| fun _ ->
              let r = Conformance.witnessFieldsLaw "IdWitness" (pinnedOf "IdWitness") typeof<int>
              Expect.isFalse r.Passed "a non-record passed the freeze"

          testCase "every public witness record the repository ships is classified — wider than the kit's own closure"
          <| fun _ ->
              let r = Conformance.witnessCoverageLaw shippedPublicTypes.Value
              Expect.isTrue r.Passed (defaultArg r.Counterexample r.Law)

          testCase "go-red: the coverage law refuses an unclassified witness, and a pin with no record behind it"
          <| fun _ ->
              let unclassified =
                  Conformance.witnessCoverageLaw (typeof<DecoyUnclassifiedWitness> :: shippedPublicTypes.Value)

              Expect.isFalse unclassified.Passed "a new, unclassified witness record passed"

              Expect.stringContains
                  (defaultArg unclassified.Counterexample "")
                  "DecoyUnclassifiedWitness"
                  "the counterexample names the unclassified record"

              let withoutId =
                  shippedPublicTypes.Value
                  |> List.filter (fun t -> t <> typedefof<IdWitness<_>>)
                  |> Conformance.witnessCoverageLaw

              Expect.isFalse withoutId.Passed "a frozen pin over a missing record passed"
              Expect.stringContains (defaultArg withoutId.Counterexample "") "IdWitness" "the stale pin is named"

          testCase "the frozen and unfrozen declarations are disjoint and each gives its reason"
          <| fun _ ->
              let frozen = Conformance.frozenWitnessFields |> List.map fst |> Set.ofList
              let unfrozen = Conformance.unfrozenWitnesses |> List.map fst |> Set.ofList
              Expect.isEmpty (Set.intersect frozen unfrozen) "a witness is frozen or it is not"

              for name, why in Conformance.unfrozenWitnesses do
                  Expect.isNotEmpty (why.Trim()) (sprintf "%s is declared outside the freeze with no reason" name) ]
