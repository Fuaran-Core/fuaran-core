module Fuaran.Core.Tests.HostCompliance

// ============================================================================
//  Host compliance (Phase 484): a host declares, row by row of the claims ladder, the evidence it
//  offers, and this repository validates the declaration.
//
//  DECLARE LOCAL. A host commits `host-compliance.json` at its own root (docs/host-compliance.md;
//  the schema is docs/host-compliance.schema.json). For a ladder row it names one or more pieces of
//  evidence, each of one CLASS from a closed set: `vector-certified`, `oracle-differential`,
//  `host-premise`, or `not-covered` with a reason (DECISIONS.md D229).
//
//  VALIDATE HERE. The validator reads three things this repository already derives, and nothing
//  else: the evidence map (`ProofCoverage.EvidenceMap`, Phase 480: what Core offers for each row),
//  every self-enumerated family manifest under `conformance/` (the one `CorpusManifest` shape, and its
//  per-host adoption RECORDS, Phase 477), and the `host-premise` rows with their probes (Phase 483).
//  It refuses a declaration that names a row, a family or an oracle function that does not exist, or
//  a count the family's adoption record does not carry. Every refusal is a `Finding` with a code from
//  `Finding.codes`; the page names each one.
//
//  COMPUTE, NEVER ASSERT. A valid declaration yields a `Verdict` with two labels the projection
//  reads rather than recomputes: `compliant` (every row Core offers evidence for is declared, so every
//  gap is named) and `parity` (every row Core offers evidence for carries ALL of it). A host never
//  writes either label.
//
//  The tool's `compliance <file>` verb runs `cli` from a Core checkout; a host runs it from the
//  checkout its Core pin names, so a declaration is always judged by the Core it names.
// ============================================================================

open System
open System.IO
open System.Text.RegularExpressions
open Fuaran.Core
open Fuaran.Core.Tests.ProofCoverage

/// The file a host commits at its own root.
[<Literal>]
let FileName = "host-compliance.json"

/// The declaration's `kind`.
[<Literal>]
let Kind = "hostCompliance"

/// The verdict's `kind`.
[<Literal>]
let VerdictKind = "hostComplianceVerdict"

/// The schema and the page, repository-relative.
let SchemaPath = "docs/host-compliance.schema.json"

let DocPath = "docs/host-compliance.md"

/// One piece of evidence a host offers for one ladder row. The set is CLOSED (D229): a class outside
/// it is refused, and a further class joins only by a recorded decision that amends this type, the
/// schema's enumeration and the page together.
[<RequireQualifiedAccess>]
type Evidence =
    /// The host's leg ran a vector family Core offers for the row, and asserted `cases` of its vectors.
    | VectorCertified of family: string * cases: int
    /// The host's own fuzzer drove the oracle process (`fuaran-core-oracle`) on `inputs` inputs from
    /// `seed`, asking `fn` of `model`, and found no disagreement.
    | OracleDifferential of model: string * fn: string * inputs: int64 * seed: string
    /// The host discharged a `host-premise` row on its own runtime: its leg reproduced every vector of
    /// the row's probe family.
    | HostPremise of probe: string * cases: int
    /// The host offers nothing for the row, and says why.
    | NotCovered of reason: string

[<RequireQualifiedAccess>]
module Evidence =

    /// Each class as written, with the members it carries beside `class`, in schema order.
    let classes: (string * string list) list =
        [ "vector-certified", [ "family"; "cases" ]
          "oracle-differential", [ "model"; "function"; "inputs"; "seed" ]
          "host-premise", [ "probe"; "cases" ]
          "not-covered", [ "reason" ] ]

    let className (e: Evidence) : string =
        match e with
        | Evidence.VectorCertified _ -> "vector-certified"
        | Evidence.OracleDifferential _ -> "oracle-differential"
        | Evidence.HostPremise _ -> "host-premise"
        | Evidence.NotCovered _ -> "not-covered"

    /// What makes two pieces of evidence on one row the same claim: the class and the thing it names.
    let identity (e: Evidence) : string * string =
        match e with
        | Evidence.VectorCertified(f, _) -> className e, f
        | Evidence.OracleDifferential(_, fn, _, _) -> className e, fn
        | Evidence.HostPremise(p, _) -> className e, p
        | Evidence.NotCovered _ -> className e, ""

/// One row's declaration.
type RowDeclaration =
    { Row: string; Evidence: Evidence list }

/// A host's declaration, as read from its `host-compliance.json`.
type Declaration =
    {
        Host: string
        /// The Core version the declaration is judged by; it must be the checkout that judges it.
        CoreVersion: string
        Rows: RowDeclaration list
    }

/// The members the declaration carries at its root, in schema order, and each row's.
let RootMembers = [ "kind"; "host"; "coreVersion"; "rows" ]

let RowMembers = [ "row"; "evidence" ]

/// One reason a declaration is refused.
type Finding =
    {
        /// One of `Finding.codes`.
        Code: string
        /// Where in the declaration: `$`, `rows[<row>]`, or `rows[<row>].evidence[<n>]`.
        At: string
        Message: string
    }

[<RequireQualifiedAccess>]
module Finding =

    /// Every refusal, by code, with what it means. The page names each one, and the suite holds a
    /// go-red case for each.
    let codes: (string * string) list =
        [ "shape",
          "the file is not a declaration of this schema: not JSON, or a member missing, mistyped, repeated or unknown"
          "kind", "the root `kind` is not `hostCompliance`"
          "host-name", "the host key is not lower-case letters and digits in `-`-separated words"
          "core-version", "the declaration names a Core version other than the checkout judging it"
          "row-unknown", "a row the claims ladder does not carry"
          "row-repeated", "one row declared twice"
          "evidence-empty", "a row with no evidence"
          "class-unknown", "an evidence class outside the closed set"
          "evidence-repeated", "one claim made twice on one row (same class, same family, function or probe)"
          "not-covered-alone", "`not-covered` beside other evidence on the same row"
          "reason-empty", "`not-covered` with a blank reason"
          "family-unknown", "a family no vector file under conformance/ is"
          "family-not-offered", "a family the evidence map does not offer for the row"
          "premise-row", "`vector-certified` on a `host-premise` row, whose evidence is the discharge"
          "adoption-missing", "no adoption record carries the family for this host"
          "adoption-unmeasured", "the host's adoption record reads `proposed`, `declined` or `vacuous`"
          "cases-mismatch", "a case count the host's adoption record does not carry"
          "function-unknown", "an oracle function the oracle's table does not hold"
          "function-not-offered", "an oracle function the evidence map does not offer for the row"
          "model-mismatch", "an oracle function claimed against a model other than the row's"
          "inputs-vacuous", "an oracle differential over no input"
          "seed-empty", "an oracle differential with a blank seed"
          "not-a-premise", "`host-premise` on a row that is not a `host-premise` row"
          "probe-mismatch", "`host-premise` naming a probe other than the row's"
          "premise-partial", "a premise discharged on fewer than every vector of its probe" ]

    let render (f: Finding) : string =
        sprintf "[%s] %s: %s" f.Code f.At f.Message

/// The findings of a failed read, none of a successful one.
let private errorsOf (r: Result<'a, 'e list>) : 'e list =
    match r with
    | Error e -> e
    | Ok _ -> []

let private finding code at message =
    if not (Finding.codes |> List.exists (fun (c, _) -> c = code)) then
        invalidArg "code" (sprintf "`%s` is not a finding code" code)

    { Code = code
      At = at
      Message = message }

// ---------------------------------------------------------------------------
//  Reading a declaration
// ---------------------------------------------------------------------------

let private hostRe = Regex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled)

/// The largest integer a JSON number carries exactly in every host (2^53).
let private int53 = 9007199254740992.0

let private wholeNumber (v: JVal) : int64 option =
    match v with
    | JInt i -> Some(int64 i)
    | JFloat f when Double.IsFinite f && f = Math.Floor f && abs f <= int53 -> Some(int64 f)
    | _ -> None

/// The members of an object, with a `shape` finding for each one repeated, unknown or missing.
let private membersOf
    (at: string)
    (required: string list)
    (fields: (string * JVal) list)
    : Map<string, JVal> * Finding list =
    let findings =
        [ for name, n in fields |> List.countBy fst do
              if n > 1 then
                  yield finding "shape" at (sprintf "the member `%s` appears %d times" name n)

          for name, _ in fields do
              if not (List.contains name required) then
                  yield
                      finding
                          "shape"
                          at
                          (sprintf
                              "carries the member `%s`: the members are %s"
                              name
                              (required |> List.map (sprintf "`%s`") |> String.concat ", "))

          for name in required do
              if not (fields |> List.exists (fun (k, _) -> k = name)) then
                  yield finding "shape" at (sprintf "carries no `%s`" name) ]

    fields |> List.distinctBy fst |> Map.ofList, findings

let private readEvidence (at: string) (v: JVal) : Result<Evidence, Finding list> =
    match v with
    | JObj fields ->
        match fields |> List.tryFind (fun (k, _) -> k = "class") with
        | Some(_, JStr cls) ->
            match Evidence.classes |> List.tryFind (fun (c, _) -> c = cls) with
            | None ->
                Error
                    [ finding
                          "class-unknown"
                          at
                          (sprintf
                              "the class `%s` is not one of %s"
                              cls
                              (Evidence.classes |> List.map (fst >> sprintf "`%s`") |> String.concat ", ")) ]
            | Some(_, members) ->
                let m, shape = membersOf at ("class" :: members) fields

                let str k =
                    match m.TryFind k with
                    | Some(JStr s) -> Ok s
                    | Some _ -> Error [ finding "shape" at (sprintf "`%s` is a string" k) ]
                    | None -> Error []

                let count k =
                    match m.TryFind k |> Option.bind wholeNumber with
                    | Some n -> Ok n
                    | None when m.ContainsKey k -> Error [ finding "shape" at (sprintf "`%s` is a whole number" k) ]
                    | None -> Error []

                let cases k =
                    count k
                    |> Result.bind (fun n ->
                        if n < 0L || n > int64 Int32.MaxValue then
                            Error [ finding "shape" at (sprintf "`%s` is a count between 0 and 2^31 - 1" k) ]
                        else
                            Ok(int n))

                let both a b f =
                    match a, b with
                    | Ok x, Ok y -> Ok(f x y)
                    | Error e1, Error e2 -> Error(e1 @ e2)
                    | Error e, _
                    | _, Error e -> Error e

                let read =
                    match cls with
                    | "vector-certified" ->
                        both (str "family") (cases "cases") (fun f c -> Evidence.VectorCertified(f, c))
                    | "oracle-differential" ->
                        both
                            (both (str "model") (str "function") (fun a b -> a, b))
                            (both (count "inputs") (str "seed") (fun a b -> a, b))
                            (fun (mo, fn) (n, s) -> Evidence.OracleDifferential(mo, fn, n, s))
                    | "host-premise" -> both (str "probe") (cases "cases") (fun p c -> Evidence.HostPremise(p, c))
                    | _ -> str "reason" |> Result.map Evidence.NotCovered

                match shape, read with
                | [], Ok e -> Ok e
                | _, Ok _ -> Error shape
                | _, Error e -> Error(shape @ e)
        | Some _ -> Error [ finding "shape" at "`class` is a string" ]
        | None -> Error [ finding "shape" at "carries no `class`" ]
    | _ -> Error [ finding "shape" at "a piece of evidence is an object" ]

let private readRow (index: int) (v: JVal) : Result<RowDeclaration, Finding list> =
    match v with
    | JObj fields ->
        let rowName =
            match fields |> List.tryFind (fun (k, _) -> k = "row") with
            | Some(_, JStr r) -> Some r
            | _ -> None

        let at = sprintf "rows[%s]" (rowName |> Option.defaultValue (string index))
        let m, shape = membersOf at RowMembers fields

        let evidence =
            match m.TryFind "evidence" with
            | Some(JArr items) ->
                let read =
                    items |> List.mapi (fun i e -> readEvidence (sprintf "%s.evidence[%d]" at i) e)

                match read |> List.collect errorsOf with
                | [] -> Ok(read |> List.choose Result.toOption)
                | fs -> Error fs
            | Some _ -> Error [ finding "shape" at "`evidence` is an array" ]
            | None -> Error []

        let rowType =
            match m.TryFind "row" with
            | Some(JStr _)
            | None -> []
            | Some _ -> [ finding "shape" at "`row` is a string" ]

        match rowName, shape @ rowType, evidence with
        | Some r, [], Ok ev -> Ok { Row = r; Evidence = ev }
        | _, fs, ev -> Error(fs @ errorsOf ev)
    | _ -> Error [ finding "shape" (sprintf "rows[%d]" index) "a row is an object" ]

/// The declaration in `text`, or every reason it is not one of this schema's. Only `shape`, `kind`,
/// `host-name` and `class-unknown` are found here; everything that needs the evidence map is
/// `validate`'s.
let parse (text: string) : Result<Declaration, Finding list> =
    match Json.parse text with
    | Error e -> Error [ finding "shape" "$" (sprintf "the file is not JSON (%s)" e) ]
    | Ok(JObj fields) ->
        let m, shape = membersOf "$" RootMembers fields

        let kind =
            match m.TryFind "kind" with
            | Some(JStr Kind) -> []
            | Some(JStr k) -> [ finding "kind" "$" (sprintf "`kind` reads `%s`: a declaration's is `%s`" k Kind) ]
            | Some _ -> [ finding "shape" "$" "`kind` is a string" ]
            | None -> []

        let host =
            match m.TryFind "host" with
            | Some(JStr h) when hostRe.IsMatch h -> Ok h
            | Some(JStr h) ->
                Error
                    [ finding
                          "host-name"
                          "$"
                          (sprintf
                              "`host` reads `%s`: a host key is lower-case letters and digits in `-`-separated words, as the adoption records key it"
                              h) ]
            | Some _ -> Error [ finding "shape" "$" "`host` is a string" ]
            | None -> Error []

        let version =
            match m.TryFind "coreVersion" with
            | Some(JStr v) -> Ok v
            | Some _ -> Error [ finding "shape" "$" "`coreVersion` is a string" ]
            | None -> Error []

        let rows =
            match m.TryFind "rows" with
            | Some(JArr rs) ->
                let read = rs |> List.mapi readRow

                match read |> List.collect errorsOf with
                | [] -> Ok(read |> List.choose Result.toOption)
                | fs -> Error fs
            | Some _ -> Error [ finding "shape" "$" "`rows` is an array" ]
            | None -> Error []

        match shape @ kind @ errorsOf host @ errorsOf version @ errorsOf rows, host, version, rows with
        | [], Ok h, Ok v, Ok rs -> Ok { Host = h; CoreVersion = v; Rows = rs }
        | fs, _, _, _ -> Error fs
    | Ok _ -> Error [ finding "shape" "$" "the declaration is a JSON object" ]

// ---------------------------------------------------------------------------
//  What a declaration is judged against
// ---------------------------------------------------------------------------

/// One host's adoption of one family, as its record reads (Phase 477).
[<RequireQualifiedAccess>]
type Adoption =
    /// `adopted` (above zero) or `vacuous` (zero): the cases the host's leg asserted.
    | Measured of cases: int
    /// `proposed` or `declined`.
    | Unmeasured of status: string

/// A family a self-enumerated manifest indexes: its vectors and every host's record.
type FamilyRecord =
    { Family: string
      Manifest: string
      Vectors: int
      Adoption: (string * Adoption) list }

type Inputs =
    {
        /// The `<Version>` of the checkout judging the declaration.
        CoreVersion: string
        /// The evidence map's rows, in ladder order.
        Rows: EvidenceMap.RowOffer list
        /// Every vector family under `conformance/`.
        Families: string list
        /// The oracle's functions and the models each asks.
        Oracle: (string * string list) list
        /// Every family a self-enumerated manifest indexes.
        Records: FamilyRecord list
    }

let private versionRe =
    Regex(@"<Version>(?<v>[^<]*)</Version>", RegexOptions.Compiled)

/// The records of every `manifest.json` among `files` (repository-relative path, text).
let records (files: (string * string) list) : Result<FamilyRecord list, string list> =
    let read (path: string, text: string) =
        let vectors =
            match Json.parse text with
            | Ok(JObj fields) ->
                match fields |> List.tryFind (fun (k, _) -> k = "families") with
                | Some(_, JArr rows) ->
                    rows
                    |> List.choose (function
                        | JObj r ->
                            match
                                r |> List.tryFind (fun (k, _) -> k = "id"),
                                r |> List.tryFind (fun (k, _) -> k = "vectors")
                            with
                            | Some(_, JStr id), Some(_, JInt n) -> Some(id, n)
                            | _ -> None
                        | _ -> None)
                    |> Map.ofList
                | _ -> Map.empty
            | _ -> Map.empty

        CorpusFile.readAdoption text
        |> Result.mapError (fun e -> sprintf "%s: %s" path e)
        |> Result.map (fun families ->
            families
            |> List.map (fun (id, hosts) ->
                { Family = id
                  Manifest = path
                  Vectors = vectors.TryFind id |> Option.defaultValue 0
                  Adoption =
                    hosts
                    |> List.map (fun (h, a) ->
                        h,
                        match a with
                        | HostAdoption.Measured(n, _) -> Adoption.Measured n
                        | other -> Adoption.Unmeasured(HostAdoption.status other)) }))

    let read =
        files
        |> List.filter (fun (p, _) -> p.EndsWith("/manifest.json", StringComparison.Ordinal))
        |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))
        |> List.map read

    match
        read
        |> List.choose (function
            | Error e -> Some e
            | Ok _ -> None)
    with
    | [] -> Ok(read |> List.collect (Result.defaultValue []))
    | errors -> Error errors

/// The inputs this repository's tree holds.
let liveInputs (root: string) : Result<Inputs, string list> =
    let ev = EvidenceMap.liveInputs root

    let version =
        match versionRe.Match(File.ReadAllText(Path.Combine(root, "Directory.Build.props"))) with
        | m when m.Success -> Ok m.Groups["v"].Value
        | _ -> Error [ "Directory.Build.props declares no <Version>" ]

    match EvidenceMap.derive ev, records ev.VectorFiles, version with
    | Ok map, Ok recs, Ok v ->
        Ok
            { CoreVersion = v
              Rows = map.Rows
              Families = map.Families |> List.map _.Family
              Oracle = ev.Oracle
              Records = recs }
    | a, b, c -> Error(errorsOf a @ errorsOf b @ errorsOf c)

// ---------------------------------------------------------------------------
//  Validating a declaration
// ---------------------------------------------------------------------------

/// The record of `family` for `host`, checked against the `cases` declared: the findings, if any.
/// `full` asks for every vector of the family (a premise's discharge).
let private adoptionFindings (i: Inputs) (at: string) (host: string) (family: string) (cases: int) (full: bool) =
    match i.Records |> List.tryFind (fun r -> r.Family = family) with
    | None ->
        [ finding
              "adoption-missing"
              at
              (sprintf
                  "`%s` records no per-host adoption: no manifest under conformance/ indexes it, so no case count can be checked"
                  family) ]
    | Some r ->
        match r.Adoption |> List.tryFind (fun (h, _) -> h = host) with
        | None ->
            [ finding
                  "adoption-missing"
                  at
                  (sprintf
                      "%s carries no adoption entry for `%s` on `%s`: the host records its adoption there first, in the change that lands its leg"
                      r.Manifest
                      host
                      family) ]
        | Some(_, Adoption.Unmeasured status) ->
            [ finding
                  "adoption-unmeasured"
                  at
                  (sprintf
                      "%s records `%s` on `%s` as `%s`: nothing was measured to certify"
                      r.Manifest
                      host
                      family
                      status) ]
        | Some(_, Adoption.Measured 0) ->
            [ finding
                  "adoption-unmeasured"
                  at
                  (sprintf
                      "%s records `%s` on `%s` as `vacuous`: a leg that asserted no case certifies nothing"
                      r.Manifest
                      host
                      family) ]
        | Some(_, Adoption.Measured n) when n <> cases ->
            [ finding
                  "cases-mismatch"
                  at
                  (sprintf "declares %d cases of `%s`; %s records %d for `%s`" cases family r.Manifest n host) ]
        | Some(_, Adoption.Measured n) when full && n < r.Vectors ->
            [ finding
                  "premise-partial"
                  at
                  (sprintf
                      "`%s` holds %d vectors and `%s` reproduced %d: a premise is discharged by every vector of its probe"
                      family
                      r.Vectors
                      host
                      n) ]
        | Some _ -> []

let private evidenceFindings (i: Inputs) (host: string) (r: EvidenceMap.RowOffer) (at: string) (e: Evidence) =
    let families = Set.ofList i.Families
    let oracle = Map.ofList i.Oracle

    match e with
    | Evidence.NotCovered reason ->
        if String.IsNullOrWhiteSpace reason then
            [ finding "reason-empty" at "`not-covered` names why: a gap is named, never left blank" ]
        else
            []
    | Evidence.VectorCertified(f, cases) ->
        if not (families.Contains f) then
            [ finding "family-unknown" at (sprintf "`%s` is no vector family under conformance/" f) ]
        elif r.Probe.IsSome then
            [ finding
                  "premise-row"
                  at
                  (sprintf "`%s` is a host-premise row: its evidence is `host-premise`, the discharge of its probe" r.Id) ]
        elif not (r.Families |> List.exists (fun o -> o.Family = f)) then
            [ finding
                  "family-not-offered"
                  at
                  (sprintf
                      "`%s` is not offered for `%s`: the evidence map offers it %s"
                      f
                      r.Id
                      (match r.Families with
                       | [] -> "no family"
                       | fs -> fs |> List.map (fun o -> "`" + o.Family + "`") |> String.concat ", ")) ]
        else
            adoptionFindings i at host f cases false
    | Evidence.OracleDifferential(model, fn, inputs, seed) ->
        [ if not (oracle.ContainsKey fn) then
              yield
                  finding
                      "function-unknown"
                      at
                      (sprintf
                          "`%s` is not in the oracle's table (%s)"
                          fn
                          (i.Oracle |> List.map (fst >> sprintf "`%s`") |> String.concat ", "))
          elif not (List.contains fn r.Oracle) then
              yield
                  finding
                      "function-not-offered"
                      at
                      (sprintf
                          "`%s` does not ask `%s`'s model%s"
                          fn
                          r.Id
                          (match r.Model with
                           | Some m -> sprintf " (`%s`)" m
                           | None -> ", which names none"))
          elif r.Model <> Some model then
              yield
                  finding
                      "model-mismatch"
                      at
                      (sprintf
                          "claims `%s` of `%s`; `%s`'s model is `%s`"
                          fn
                          model
                          r.Id
                          (r.Model |> Option.defaultValue "none"))

          if inputs < 1L then
              yield
                  finding
                      "inputs-vacuous"
                      at
                      (sprintf "a differential over %d inputs asked nothing: the count is at least 1" inputs)

          if String.IsNullOrWhiteSpace seed then
              yield finding "seed-empty" at "names no seed: a run nobody can repeat is not evidence" ]
    | Evidence.HostPremise(probe, cases) ->
        match r.Probe with
        | None -> [ finding "not-a-premise" at (sprintf "`%s` is not a host-premise row" r.Id) ]
        | Some p when p <> probe ->
            [ finding "probe-mismatch" at (sprintf "`%s`'s probe is `%s`, not `%s`" r.Id p probe) ]
        | Some _ -> adoptionFindings i at host probe cases true

/// Every reason the declaration is false of these inputs; empty when it validates.
let validate (i: Inputs) (d: Declaration) : Finding list =
    let rows = i.Rows |> List.map (fun r -> r.Id, r) |> Map.ofList

    [ if d.CoreVersion <> i.CoreVersion then
          yield
              finding
                  "core-version"
                  "$"
                  (sprintf
                      "names Core %s and this checkout is %s: validate it with the Core checkout it names"
                      d.CoreVersion
                      i.CoreVersion)

      for row, n in d.Rows |> List.countBy _.Row do
          if n > 1 then
              yield
                  finding
                      "row-repeated"
                      (sprintf "rows[%s]" row)
                      (sprintf "declared %d times: a row is declared once" n)

      for rd in d.Rows |> List.distinctBy _.Row do
          let at = sprintf "rows[%s]" rd.Row

          match rows.TryFind rd.Row with
          | None -> yield finding "row-unknown" at (sprintf "`%s` is no row of the claims ladder (proofs.json)" rd.Row)
          | Some r ->
              if List.isEmpty rd.Evidence then
                  yield finding "evidence-empty" at "declares no evidence: a gap is `not-covered` with its reason"

              if
                  rd.Evidence.Length > 1
                  && rd.Evidence
                     |> List.exists (function
                         | Evidence.NotCovered _ -> true
                         | _ -> false)
              then
                  yield
                      finding "not-covered-alone" at "is `not-covered` beside other evidence: a row is one or the other"

              for (cls, name), n in rd.Evidence |> List.countBy Evidence.identity do
                  if n > 1 && cls <> "not-covered" then
                      yield finding "evidence-repeated" at (sprintf "claims `%s` %s %d times" cls name n)

              for k, e in rd.Evidence |> List.indexed do
                  yield! evidenceFindings i d.Host r (sprintf "%s.evidence[%d]" at k) e ]

// ---------------------------------------------------------------------------
//  The verdict: the labels a projection reads
// ---------------------------------------------------------------------------

/// One row Core offers evidence for, and the offered evidence the declaration does not carry.
type ParityGap = { Row: string; Missing: string list }

type Verdict =
    {
        Host: string
        CoreVersion: string
        /// Rows the evidence map offers anything for (a family, an oracle function or a probe).
        Offered: int
        /// Rows the declaration names.
        Declared: int
        /// The evidence declared, by class, in `Evidence.classes` order.
        ByClass: (string * int) list
        /// Offered rows the declaration does not name, in ladder order.
        Undeclared: string list
        /// Every offered row is declared: every gap is named.
        Compliant: bool
        /// Offered rows short of all their offered evidence, in ladder order.
        Gaps: ParityGap list
        /// Compliant, and no gap: every offered family certified at every vector, every offered oracle
        /// function differentially tested, and every host premise discharged.
        Parity: bool
    }

/// The evidence the declaration does not carry for one offered row, in the map's order.
let private missing (i: Inputs) (r: EvidenceMap.RowOffer) (declared: Evidence list) : string list =
    match r.Probe with
    | Some p ->
        if
            declared
            |> List.exists (function
                | Evidence.HostPremise(q, _) -> q = p
                | _ -> false)
        then
            []
        else
            [ sprintf "the discharge of `%s`" p ]
    | None ->
        [ for f in r.Families do
              match i.Records |> List.tryFind (fun x -> x.Family = f.Family) with
              | None -> yield sprintf "`%s` (no per-host adoption record in Core)" f.Family
              | Some record ->
                  let full =
                      declared
                      |> List.exists (function
                          | Evidence.VectorCertified(g, c) -> g = f.Family && c = record.Vectors
                          | _ -> false)

                  if not full then
                      yield sprintf "`%s` at all %d vectors" f.Family record.Vectors

          for fn in r.Oracle do
              if
                  not (
                      declared
                      |> List.exists (function
                          | Evidence.OracleDifferential(_, g, _, _) -> g = fn
                          | _ -> false)
                  )
              then
                  yield sprintf "the `%s` differential" fn ]

/// The verdict on a declaration `validate` found nothing in.
let verdict (i: Inputs) (d: Declaration) : Verdict =
    let byRow = d.Rows |> List.map (fun r -> r.Row, r.Evidence) |> Map.ofList
    let offered = i.Rows |> List.filter _.Offered

    let undeclared =
        offered |> List.filter (fun r -> not (byRow.ContainsKey r.Id)) |> List.map _.Id

    let gaps =
        offered
        |> List.choose (fun r ->
            match missing i r (byRow.TryFind r.Id |> Option.defaultValue []) with
            | [] -> None
            | m -> Some { Row = r.Id; Missing = m })

    let all = d.Rows |> List.collect _.Evidence

    { Host = d.Host
      CoreVersion = d.CoreVersion
      Offered = offered.Length
      Declared = d.Rows.Length
      ByClass =
        Evidence.classes
        |> List.map (fun (c, _) -> c, all |> List.filter (fun e -> Evidence.className e = c) |> List.length)
      Undeclared = undeclared
      Compliant = List.isEmpty undeclared
      Gaps = gaps
      Parity = List.isEmpty undeclared && List.isEmpty gaps }

/// The declaration in `text` judged against `i`: its verdict, or every finding.
let check (i: Inputs) (text: string) : Result<Verdict, Finding list> =
    parse text
    |> Result.bind (fun d ->
        match validate i d with
        | [] -> Ok(verdict i d)
        | fs -> Error fs)

/// The verdict as the tool prints it: one canonical JSON object (keys in ordinal order), the shape
/// docs/host-compliance.md states.
let renderVerdict (v: Verdict) : string =
    let strs (xs: string list) = JArr(xs |> List.map JStr)

    JObj
        [ "kind", JStr VerdictKind
          "host", JStr v.Host
          "coreVersion", JStr v.CoreVersion
          "offered", JInt v.Offered
          "declared", JInt v.Declared
          "byClass", JObj(v.ByClass |> List.map (fun (c, n) -> c, JInt n))
          "undeclared", strs v.Undeclared
          "compliant", JBool v.Compliant
          "gaps",
          JArr(
              v.Gaps
              |> List.map (fun g -> JObj [ "row", JStr g.Row; "missing", strs g.Missing ])
          )
          "parity", JBool v.Parity ]
    |> Canonical.write Rendering.CrossHost

/// The tool's `compliance <file>` verb over the checkout at `root`: 0 and the verdict on stdout when
/// the declaration validates, 1 and every finding on stderr when it does not, 2 when it cannot run.
let cli (out: TextWriter) (err: TextWriter) (root: string) (args: string list) : int =
    match args with
    | [ path ] when File.Exists path ->
        match liveInputs root with
        | Error why ->
            err.WriteLine(
                sprintf "compliance: this checkout's evidence cannot be read:\n  %s" (String.concat "\n  " why)
            )

            2
        | Ok i ->
            match check i (File.ReadAllText path) with
            | Ok v ->
                out.WriteLine(renderVerdict v)
                0
            | Error findings ->
                err.WriteLine(sprintf "compliance %s: refused, %d finding(s):" path findings.Length)

                for f in findings do
                    err.WriteLine("  " + Finding.render f)

                1
    | [ path ] ->
        err.WriteLine(sprintf "compliance: no file `%s`" path)
        2
    | _ ->
        err.WriteLine(sprintf "usage: compliance <%s>" FileName)
        2
