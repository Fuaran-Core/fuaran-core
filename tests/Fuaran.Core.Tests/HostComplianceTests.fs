module Fuaran.Core.Tests.HostComplianceTests

// Phase 484 — a host's compliance declaration, validated (`HostCompliance`, in the fixtures library,
// which the tool's `compliance` verb runs). Every refusal has a go-red here, each ONE edit away from a
// declaration that validates against the live tree, and a census holds that every finding code has
// one. The verdict's two labels are computed over a small synthetic map, where each way to lose them
// can be shown alone. The schema, the page and the validator are held to name the same closed sets.

open System
open System.IO
open System.Text.Json
open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.HostCompliance

module private Case =

    let live () : Inputs =
        match liveInputs (Repo.root ()) with
        | Ok i -> i
        | Error why -> failtestf "the live inputs do not read:\n%s" (String.concat "\n" why)

    /// The host every live case declares for: the one host measured elsewhere (D225).
    let host = "fuaran-core-rs"

    let vc (family: string) (cases: int) =
        JObj [ "class", JStr "vector-certified"; "family", JStr family; "cases", JInt cases ]

    let od (model: string) (fn: string) (inputs: int) (seed: string) =
        JObj
            [ "class", JStr "oracle-differential"
              "model", JStr model
              "function", JStr fn
              "inputs", JInt inputs
              "seed", JStr seed ]

    let hp (probe: string) (cases: int) =
        JObj [ "class", JStr "host-premise"; "probe", JStr probe; "cases", JInt cases ]

    let nc (reason: string) =
        JObj [ "class", JStr "not-covered"; "reason", JStr reason ]

    let row (id: string) (evidence: JVal list) =
        JObj [ "row", JStr id; "evidence", JArr evidence ]

    let declaration (host: string) (version: string) (rows: JVal list) =
        JObj
            [ "kind", JStr Kind
              "host", JStr host
              "coreVersion", JStr version
              "rows", JArr rows ]

    let record (i: Inputs) (family: string) =
        match i.Records |> List.tryFind (fun r -> r.Family = family) with
        | Some r -> r
        | None -> failtestf "no manifest indexes `%s`" family

    let measured (i: Inputs) (family: string) =
        match (record i family).Adoption |> List.tryFind (fun (h, _) -> h = host) with
        | Some(_, Adoption.Measured n) when n > 0 -> n
        | other -> failtestf "`%s` records %A for %s: the live cases need a measured adoption" family other host

    /// `i` with `host`'s record of `family` replaced.
    let withAdoption (family: string) (a: Adoption) (i: Inputs) =
        { i with
            Records =
                i.Records
                |> List.map (fun r ->
                    if r.Family = family then
                        { r with
                            Adoption = r.Adoption |> List.map (fun (h, x) -> if h = host then h, a else h, x) }
                    else
                        r) }

    let rowWhere (i: Inputs) (what: string) (p: ProofCoverage.EvidenceMap.RowOffer -> bool) =
        match i.Rows |> List.tryFind p with
        | Some r -> r
        | None -> failtestf "no live row is %s (every case built on it would be vacuous)" what

    /// The live rows the cases declare: one the spine certified by `skeletonApply` and whose model the
    /// oracle's `tree.apply` asks, one offered `codecRefusals`, the `host-int53` premise, and one Core
    /// offers nothing for.
    let treeRow (i: Inputs) =
        rowWhere i "offered `skeletonApply` and `tree.apply`" (fun r ->
            r.Families |> List.exists (fun f -> f.Family = "skeletonApply")
            && List.contains "tree.apply" r.Oracle)

    let jsonRow (i: Inputs) =
        rowWhere i "offered `codecRefusals`" (fun r -> r.Families |> List.exists (fun f -> f.Family = "codecRefusals"))

    let premiseRow (i: Inputs) =
        rowWhere i "the `host-int53` premise" (fun r -> r.Id = "host-int53")

    let bareRow (i: Inputs) =
        rowWhere i "offered nothing" (fun r -> not r.Offered)

    /// The live inputs with the spine's premise record measured at every vector: no host has
    /// discharged a premise yet, so the `host-premise` class is exercised on an edited record.
    let inputs () =
        let i = live ()
        withAdoption "premiseInt53" (Adoption.Measured (record i "premiseInt53").Vectors) i

    /// A declaration that validates: one of each class, every count the record carries.
    let baseRows (i: Inputs) =
        [ row
              (treeRow i).Id
              [ vc "skeletonApply" (measured i "skeletonApply")
                od "TreeOps" "tree.apply" 1000 "seed-1" ]
          row (jsonRow i).Id [ vc "codecRefusals" (measured i "codecRefusals") ]
          row (premiseRow i).Id [ hp "premiseInt53" (record i "premiseInt53").Vectors ]
          row (bareRow i).Id [ nc "Core offers no evidence for this row" ] ]

    let baseDeclaration (i: Inputs) =
        declaration host i.CoreVersion (baseRows i)

    let codes (i: Inputs) (decl: JVal) : string list =
        match check i (Json.render decl) with
        | Ok _ -> []
        | Error fs -> fs |> List.map _.Code

    let text (i: Inputs) (decl: string) : string list =
        match check i decl with
        | Ok _ -> []
        | Error fs -> fs |> List.map _.Code

    /// `rows` with the row at `index` replaced by `replacement`.
    let replaceRow (index: int) (replacement: JVal) (rows: JVal list) =
        rows |> List.mapi (fun k r -> if k = index then replacement else r)

/// One go-red: the code it must raise, what it does, and the declaration's text under its inputs.
type private GoRed =
    { Code: string
      What: string
      Run: unit -> Inputs * string }

let private goRed code what (build: Inputs -> Inputs * JVal) =
    { Code = code
      What = what
      Run =
        fun () ->
            let i, d = build (Case.inputs ())
            i, Json.render d }

let private goRedText code what (build: Inputs -> string) =
    { Code = code
      What = what
      Run =
        fun () ->
            let i = Case.inputs ()
            i, build i }

let private onRows (edit: Inputs -> JVal list -> JVal list) (i: Inputs) =
    i, Case.declaration Case.host i.CoreVersion (edit i (Case.baseRows i))

let private goReds: GoRed list =
    let row = Case.row
    let render (v: JVal) = Json.render v

    [ goRedText "shape" "a file that is not JSON" (fun _ -> "{ \"kind\": ")
      goRedText "shape" "a root that is not an object" (fun _ -> "[]")
      goRed "shape" "an unknown root member" (fun i ->
          i,
          match Case.baseDeclaration i with
          | JObj fs -> JObj(fs @ [ "parity", JBool true ])
          | other -> other)
      goRed "shape" "a missing `rows`" (fun i ->
          i,
          match Case.baseDeclaration i with
          | JObj fs -> JObj(fs |> List.filter (fun (k, _) -> k <> "rows"))
          | other -> other)
      goRedText "shape" "a repeated root member" (fun i ->
          (render (Case.baseDeclaration i))
              .Replace("\"kind\":\"hostCompliance\"", "\"kind\":\"hostCompliance\",\"kind\":\"hostCompliance\""))
      goRed
          "shape"
          "a piece of evidence missing a member"
          (onRows (fun i rows ->
              rows
              |> Case.replaceRow
                  1
                  (row
                      (Case.jsonRow i).Id
                      [ JObj [ "class", JStr "vector-certified"; "family", JStr "codecRefusals" ] ])))
      goRed
          "shape"
          "a negative count"
          (onRows (fun i rows ->
              rows
              |> Case.replaceRow 1 (row (Case.jsonRow i).Id [ Case.vc "codecRefusals" -1 ])))
      goRed
          "shape"
          "a count written as a string"
          (onRows (fun i rows ->
              rows
              |> Case.replaceRow
                  1
                  (row
                      (Case.jsonRow i).Id
                      [ JObj
                            [ "class", JStr "vector-certified"
                              "family", JStr "codecRefusals"
                              "cases", JStr "56" ] ])))
      goRed "kind" "another kind" (fun i ->
          i,
          match Case.baseDeclaration i with
          | JObj fs ->
              JObj(
                  fs
                  |> List.map (fun (k, v) -> if k = "kind" then k, JStr "hostAdoption" else k, v)
              )
          | other -> other)
      goRed "host-name" "a host key outside the grammar" (fun i ->
          i, Case.declaration "Fuaran_RS" i.CoreVersion (Case.baseRows i))
      goRed "core-version" "another Core version" (fun i -> i, Case.declaration Case.host "0.0.1" (Case.baseRows i))
      goRed
          "row-unknown"
          "a row the ladder does not carry"
          (onRows (fun _ rows -> rows @ [ row "no-such-row" [ Case.nc "absent" ] ]))
      goRed "row-repeated" "one row twice" (onRows (fun _ rows -> rows @ [ List.last rows ]))
      goRed
          "evidence-empty"
          "a row with no evidence"
          (onRows (fun i rows -> rows |> Case.replaceRow 3 (row (Case.bareRow i).Id [])))
      goRed
          "class-unknown"
          "a class not in the closed set (a proof-sourced class is not one until decided)"
          (onRows (fun i rows ->
              rows
              |> Case.replaceRow
                  3
                  (row (Case.bareRow i).Id [ JObj [ "class", JStr "proof-checked"; "reason", JStr "x" ] ])))
      goRed
          "evidence-repeated"
          "one family claimed twice on a row"
          (onRows (fun i rows ->
              let n = Case.measured i "codecRefusals"

              rows
              |> Case.replaceRow 1 (row (Case.jsonRow i).Id [ Case.vc "codecRefusals" n; Case.vc "codecRefusals" n ])))
      goRed
          "not-covered-alone"
          "`not-covered` beside a certification"
          (onRows (fun i rows ->
              rows
              |> Case.replaceRow
                  1
                  (row
                      (Case.jsonRow i).Id
                      [ Case.vc "codecRefusals" (Case.measured i "codecRefusals"); Case.nc "and yet" ])))
      goRed
          "reason-empty"
          "a blank reason"
          (onRows (fun i rows -> rows |> Case.replaceRow 3 (row (Case.bareRow i).Id [ Case.nc "  " ])))
      goRed
          "family-unknown"
          "a family no vector file is"
          (onRows (fun i rows ->
              rows
              |> Case.replaceRow 1 (row (Case.jsonRow i).Id [ Case.vc "codecRefusalz" 56 ])))
      goRed
          "family-not-offered"
          "a family the map does not offer for the row"
          (onRows (fun i rows ->
              rows
              |> Case.replaceRow 0 (row (Case.treeRow i).Id [ Case.vc "skeletonDag" (Case.measured i "skeletonDag") ])))
      goRed
          "premise-row"
          "`vector-certified` on a host-premise row"
          (onRows (fun i rows ->
              rows
              |> Case.replaceRow
                  2
                  (row (Case.premiseRow i).Id [ Case.vc "premiseInt53" (Case.record i "premiseInt53").Vectors ])))
      goRed
          "adoption-missing"
          "a family no manifest indexes (the law families carry no adoption record)"
          (onRows (fun i rows ->
              let decimalRow =
                  Case.rowWhere i "offered `decimal`" (fun r ->
                      r.Families |> List.exists (fun f -> f.Family = "decimal"))

              rows @ [ row decimalRow.Id [ Case.vc "decimal" 49 ] ]))
      goRed "adoption-missing" "a host the family's record has no entry for" (fun i ->
          let i' =
              { i with
                  Records =
                      i.Records
                      |> List.map (fun r ->
                          if r.Family = "codecRefusals" then
                              { r with
                                  Adoption = r.Adoption |> List.filter (fun (h, _) -> h <> Case.host) }
                          else
                              r) }

          i', Case.baseDeclaration i)
      goRed "adoption-unmeasured" "a `proposed` record" (fun i ->
          Case.withAdoption "codecRefusals" (Adoption.Unmeasured "proposed") i, Case.baseDeclaration i)
      goRed "adoption-unmeasured" "a `vacuous` record" (fun i ->
          Case.withAdoption "codecRefusals" (Adoption.Measured 0) i, Case.baseDeclaration i)
      goRed
          "cases-mismatch"
          "a count the record does not carry"
          (onRows (fun i rows ->
              rows
              |> Case.replaceRow
                  1
                  (row (Case.jsonRow i).Id [ Case.vc "codecRefusals" (Case.measured i "codecRefusals" - 1) ])))
      goRed
          "function-unknown"
          "a function the oracle does not hold"
          (onRows (fun i rows ->
              rows
              |> Case.replaceRow 0 (row (Case.treeRow i).Id [ Case.od "TreeOps" "tree.applyy" 10 "s" ])))
      goRed
          "function-not-offered"
          "a function that asks another model"
          (onRows (fun i rows ->
              rows
              |> Case.replaceRow 0 (row (Case.treeRow i).Id [ Case.od "Chain" "chain.verify" 10 "s" ])))
      goRed
          "model-mismatch"
          "an offered function claimed against another of its models"
          (onRows (fun i rows ->
              rows
              |> Case.replaceRow 0 (row (Case.treeRow i).Id [ Case.od "DagFold" "dag.fold" 10 "s" ])))
      goRed
          "inputs-vacuous"
          "a differential over no input"
          (onRows (fun i rows ->
              rows
              |> Case.replaceRow 0 (row (Case.treeRow i).Id [ Case.od "TreeOps" "tree.apply" 0 "s" ])))
      goRed
          "seed-empty"
          "a differential with no seed"
          (onRows (fun i rows ->
              rows
              |> Case.replaceRow 0 (row (Case.treeRow i).Id [ Case.od "TreeOps" "tree.apply" 10 "" ])))
      goRed
          "not-a-premise"
          "`host-premise` on a row that is not one"
          (onRows (fun i rows ->
              rows
              |> Case.replaceRow 0 (row (Case.treeRow i).Id [ Case.hp "premiseInt53" 29 ])))
      goRed
          "probe-mismatch"
          "another premise's probe"
          (onRows (fun i rows ->
              rows
              |> Case.replaceRow 2 (row (Case.premiseRow i).Id [ Case.hp "premiseSha256" 17 ])))
      goRed "premise-partial" "a premise discharged on all but one vector" (fun i ->
          let short = (Case.record i "premiseInt53").Vectors - 1
          let i' = Case.withAdoption "premiseInt53" (Adoption.Measured short) i

          i',
          Case.declaration
              Case.host
              i.CoreVersion
              (Case.baseRows i
               |> Case.replaceRow 2 (row (Case.premiseRow i).Id [ Case.hp "premiseInt53" short ]))) ]

// ---------------------------------------------------------------------------
//  The verdict over a synthetic map: each way to lose a label, alone.
// ---------------------------------------------------------------------------

module private Synthetic =

    open ProofCoverage.EvidenceMap

    let offer family cases : FamilyOffer =
        { Family = family
          File = "conformance/x/" + family + ".json"
          Cases = cases }

    let rowOf id model families oracle probe : RowOffer =
        { Id = id
          Level = (if Option.isSome probe then "assumed" else "proved")
          Model = model
          Families = families
          Oracle = oracle
          Probe = probe }

    /// `certified` (model `M`: family `f1`, function `fn1`), `premise` (probe `p1`), `unrecorded`
    /// (model `N`: family `f2`, which no manifest indexes) and `bare` (nothing offered).
    let inputs (withUnrecorded: bool) : HostCompliance.Inputs =
        { CoreVersion = "9.9.9"
          Rows =
            [ rowOf "certified" (Some "M") [ offer "f1" 10 ] [ "fn1" ] None
              rowOf "premise" None [ offer "p1" 5 ] [] (Some "p1")
              if withUnrecorded then
                  rowOf "unrecorded" (Some "N") [ offer "f2" 3 ] [] None
              rowOf "bare" None [] [] None ]
          Families = [ "f1"; "f2"; "p1" ]
          Oracle = [ "fn1", [ "M" ] ]
          Records =
            [ ({ Family = "f1"
                 Manifest = "conformance/x/manifest.json"
                 Vectors = 10
                 Adoption = [ "h", Adoption.Measured 10; "partial", Adoption.Measured 9 ] }
              : FamilyRecord)
              ({ Family = "p1"
                 Manifest = "conformance/x/manifest.json"
                 Vectors = 5
                 Adoption = [ "h", Adoption.Measured 5; "partial", Adoption.Measured 5 ] }
              : FamilyRecord) ] }

    let full =
        [ Case.row "certified" [ Case.vc "f1" 10; Case.od "M" "fn1" 7 "s" ]
          Case.row "premise" [ Case.hp "p1" 5 ] ]

    let verdictOf (i: HostCompliance.Inputs) (host: string) (rows: JVal list) =
        match check i (Json.render (Case.declaration host i.CoreVersion rows)) with
        | Ok v -> v
        | Error fs ->
            failtestf
                "the synthetic declaration does not validate:\n%s"
                (fs |> List.map Finding.render |> String.concat "\n")

let private schemaText () =
    File.ReadAllText(Snapshots.repoFile SchemaPath)

let private schemaAccepts (decl: string) : bool =
    let schema =
        global.Json.Schema.JsonSchema.FromText(
            schemaText (),
            global.Json.Schema.BuildOptions(SchemaRegistry = global.Json.Schema.SchemaRegistry())
        )

    use doc = JsonDocument.Parse decl

    let options =
        global.Json.Schema.EvaluationOptions(OutputFormat = global.Json.Schema.OutputFormat.Flag)

    (schema.Evaluate(doc.RootElement, options)).IsValid

[<Tests>]
let tests =
    testList
        "HostCompliance"
        [ testCase "a declaration of each class, at the counts the records carry, validates against the live tree"
          <| fun _ ->
              let i = Case.inputs ()

              match check i (Json.render (Case.baseDeclaration i)) with
              | Error fs -> failtestf "refused:\n%s" (fs |> List.map Finding.render |> String.concat "\n")
              | Ok v ->
                  Expect.equal
                      (v.ByClass |> List.map snd)
                      [ 2; 1; 1; 1 ]
                      "two certifications, one differential, one discharge and one named gap"

                  Expect.isFalse v.Compliant "four rows declared of the many Core offers evidence for"
                  Expect.isFalse v.Parity "and so no parity"

          testList
              "go-red"
              [ for g in goReds ->
                    testCase (sprintf "[%s] %s" g.Code g.What)
                    <| fun _ ->
                        let i, decl = g.Run()
                        let found = Case.text i decl

                        Expect.contains found g.Code (sprintf "refused with `%s`; found %A" g.Code found) ]

          testCase "every finding code has a go-red, and every go-red raises a declared code"
          <| fun _ ->
              let declared = Finding.codes |> List.map fst |> Set.ofList
              let covered = goReds |> List.map _.Code |> Set.ofList
              Expect.equal covered declared "the go-reds cover exactly the declared codes"

          testCase "the go-reds' one edit to the live tree is the premise record (the spine has discharged none)"
          <| fun _ ->
              let i = Case.live ()
              let found = Case.codes i (Case.baseDeclaration i)

              Expect.isTrue
                  (found |> List.forall (fun c -> c = "adoption-unmeasured"))
                  (sprintf "the base declaration is refused only on the premise's record, found %A" found)

          testList
              "verdict"
              [ testCase "every offered row carrying all its offered evidence is compliant and at parity"
                <| fun _ ->
                    let v = Synthetic.verdictOf (Synthetic.inputs false) "h" Synthetic.full
                    Expect.isTrue v.Compliant "every offered row declared"
                    Expect.isTrue v.Parity "and every offer taken"
                    Expect.equal v.Offered 2 "the bare row is not offered and need not be declared"
                    Expect.isEmpty v.Gaps "no gap"

                testCase "an offered row left out is undeclared: neither label"
                <| fun _ ->
                    let v =
                        Synthetic.verdictOf (Synthetic.inputs false) "h" [ List.head Synthetic.full ]

                    Expect.equal v.Undeclared [ "premise" ] "the premise row is named"
                    Expect.isFalse v.Compliant "a gap left unnamed"
                    Expect.isFalse v.Parity "and so no parity"

                testCase "a named gap is compliant, not parity"
                <| fun _ ->
                    let v =
                        Synthetic.verdictOf
                            (Synthetic.inputs false)
                            "h"
                            [ List.head Synthetic.full
                              Case.row "premise" [ Case.nc "no stack probe on this target" ] ]

                    Expect.isTrue v.Compliant "every offered row declared"
                    Expect.isFalse v.Parity "an open premise"

                    Expect.equal
                        v.Gaps
                        [ { Row = "premise"
                            Missing = [ "the discharge of `p1`" ] } ]
                        "the gap"

                testCase "a missing differential withholds parity"
                <| fun _ ->
                    let v =
                        Synthetic.verdictOf
                            (Synthetic.inputs false)
                            "h"
                            [ Case.row "certified" [ Case.vc "f1" 10 ]; List.last Synthetic.full ]

                    Expect.equal
                        v.Gaps
                        [ { Row = "certified"
                            Missing = [ "the `fn1` differential" ] } ]
                        "the gap"

                    Expect.isFalse v.Parity "no parity"

                testCase "a family certified on fewer than all its vectors withholds parity"
                <| fun _ ->
                    let v =
                        Synthetic.verdictOf
                            (Synthetic.inputs false)
                            "partial"
                            [ Case.row "certified" [ Case.vc "f1" 9; Case.od "M" "fn1" 7 "s" ]
                              List.last Synthetic.full ]

                    Expect.equal
                        v.Gaps
                        [ { Row = "certified"
                            Missing = [ "`f1` at all 10 vectors" ] } ]
                        "the gap"

                    Expect.isTrue v.Compliant "still compliant"
                    Expect.isFalse v.Parity "no parity"

                testCase "a family that records no adoption withholds parity from every host, and says so"
                <| fun _ ->
                    let i = Synthetic.inputs true

                    let v =
                        Synthetic.verdictOf
                            i
                            "h"
                            (Synthetic.full @ [ Case.row "unrecorded" [ Case.nc "uncertifiable" ] ])

                    Expect.equal
                        v.Gaps
                        [ { Row = "unrecorded"
                            Missing = [ "`f2` (no per-host adoption record in Core)" ] } ]
                        "the gap names Core's"

                    Expect.isFalse v.Parity "no host can be at parity on it"

                testCase "the verdict renders as one canonical object with the members the page states"
                <| fun _ ->
                    let v = Synthetic.verdictOf (Synthetic.inputs false) "h" Synthetic.full

                    match Json.parse (renderVerdict v) with
                    | Ok(JObj fs) ->
                        Expect.equal
                            (fs |> List.map fst)
                            [ "byClass"
                              "compliant"
                              "coreVersion"
                              "declared"
                              "gaps"
                              "host"
                              "kind"
                              "offered"
                              "parity"
                              "undeclared" ]
                            "the members, in ordinal order"

                        Expect.contains fs ("kind", JStr VerdictKind) "the kind"
                    | other -> failtestf "the verdict reads as %A" other ]

          testList
              "schema"
              [ testCase "the schema enumerates the validator's classes, each with the validator's members"
                <| fun _ ->
                    use doc = JsonDocument.Parse(schemaText ())
                    let root = doc.RootElement
                    let defs = root.GetProperty "$defs"

                    let strings (e: JsonElement) =
                        [ for x in e.EnumerateArray() -> x.GetString() ]

                    Expect.equal (strings (root.GetProperty "required")) RootMembers "the root's members"
                    Expect.equal (strings (defs.GetProperty("row").GetProperty "required")) RowMembers "a row's members"

                    Expect.equal
                        (strings (
                            defs.GetProperty("evidence").GetProperty("properties").GetProperty("class").GetProperty
                                "enum"
                        ))
                        (Evidence.classes |> List.map fst)
                        "the closed class set"

                    for cls, members in Evidence.classes do
                        Expect.equal
                            (strings (defs.GetProperty(cls).GetProperty "required"))
                            ("class" :: members)
                            (sprintf "`%s`'s members" cls)

                testCase "the schema accepts the valid declaration and refuses the shapes the validator refuses"
                <| fun _ ->
                    let i = Case.inputs ()
                    Expect.isTrue (schemaAccepts (Json.render (Case.baseDeclaration i))) "the valid declaration"

                    for g in
                        goReds
                        |> List.filter (fun g -> g.Code = "shape" || g.Code = "class-unknown" || g.Code = "kind") do
                        let _, decl = g.Run()

                        let parses =
                            try
                                use _ = JsonDocument.Parse decl
                                true
                            with _ ->
                                false

                        // A repeated member is invisible to a JSON reader that keeps the last one.
                        if parses && not (g.What.Contains "repeated") then
                            Expect.isFalse (schemaAccepts decl) (sprintf "the schema refuses %s" g.What) ]

          testCase "the page names every class, every finding code and every verdict member"
          <| fun _ ->
              let page = File.ReadAllText(Snapshots.repoFile DocPath)

              let unnamed =
                  [ for c, _ in Evidence.classes -> c
                    for c, _ in Finding.codes -> c
                    yield!
                        [ "compliant"
                          "parity"
                          "undeclared"
                          "gaps"
                          "offered"
                          "declared"
                          "byClass" ] ]
                  |> List.filter (fun n -> not (page.Contains("`" + n + "`")))

              Expect.isEmpty unnamed "named on docs/host-compliance.md"

          testList
              "tool"
              [ let run (text: string option) (args: string list -> string list) =
                    let path =
                        Path.Combine(
                            Path.GetTempPath(),
                            sprintf "host-compliance-%s.json" (Guid.NewGuid().ToString "N")
                        )

                    try
                        text |> Option.iter (fun t -> File.WriteAllText(path, t))
                        use out = new StringWriter()
                        use err = new StringWriter()
                        let code = cli out err (Repo.root ()) (args [ path ])
                        code, out.ToString(), err.ToString()
                    finally
                        if File.Exists path then
                            File.Delete path

                let live () =
                    // The live tree's own records: the one discharge the cases need is not in them, so the
                    // tool's run declares the other classes only.
                    let i = Case.live ()

                    Case.declaration
                        Case.host
                        i.CoreVersion
                        (Case.baseRows i
                         |> List.filter (fun r -> not ((Json.render r).Contains "host-premise")))

                testCase "a valid declaration: exit 0 and the verdict on stdout"
                <| fun _ ->
                    let code, out, _ = run (Some(Json.render (live ()))) id
                    Expect.equal code 0 "exit 0"
                    Expect.stringContains out (sprintf "\"kind\":\"%s\"" VerdictKind) "the verdict"

                testCase "a false declaration: exit 1 and every finding on stderr"
                <| fun _ ->
                    let i = Case.live ()

                    let code, _, err =
                        run (Some(Json.render (Case.declaration Case.host "0.0.1" (Case.baseRows i)))) id

                    Expect.equal code 1 "exit 1"
                    Expect.stringContains err "[core-version]" "names the code"

                testCase "no file, or no argument: exit 2"
                <| fun _ ->
                    let code, _, _ = run None id
                    Expect.equal code 2 "no file"
                    let code, _, err = run None (fun _ -> [])
                    Expect.equal code 2 "no argument"
                    Expect.stringContains err "usage" "the usage" ] ]
