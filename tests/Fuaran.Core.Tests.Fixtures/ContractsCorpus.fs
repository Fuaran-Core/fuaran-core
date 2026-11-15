namespace Fuaran.Core.Tests

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

/// The IDL's contracts as conformance vectors (Phase 644, DECISIONS.md D260, `docs/idl-contracts.md`):
/// the contract fixture's vocabulary, with the reference's answer to four questions. Is this vocabulary
/// valid, and if not which refusals does it earn; what capability does each method project to; and what
/// class is the change between two revisions. Written by `render idl`, held to a fresh render, and
/// replayed from the committed bytes by `IdlContractVectorTests`.
module ContractsCorpus =

    let familyDirName = "idl"
    let fileName = "contracts.json"

    let private text (v: JVal) : string = Canonical.write Rendering.CrossHost v

    /// The canonical `idl.json` of a vocabulary, on one line.
    let artifact (idl: Idl) : string = text (Artifact.json idl)

    /// The canonical text of a vocabulary's `contracts` member: `[]` when it declares none.
    let contractsOf (idl: Idl) : string =
        match Artifact.json idl with
        | JObj members ->
            members
            |> List.tryPick (fun (k, v) -> if k = "contracts" then Some(text v) else None)
            |> Option.defaultValue "[]"
        | _ -> "[]"

    /// The revision a vector names: the vocabulary `idl` (its `idl.json`, declaring no contract) with
    /// `contracts` as its `contracts` member, the member left out when the array is empty.
    let revision (idl: string) (contracts: string) : string =
        match Json.parse idl, Json.parse contracts with
        | Ok(JObj members), Ok(JArr []) -> text (JObj members)
        | Ok(JObj members), Ok cs -> text (JObj(members @ [ "contracts", cs ]))
        | _ -> failwith "ContractsCorpus.revision: the vocabulary is not an object, or the contracts are not JSON"

    /// The fixture's vocabulary with no contract: what every projection and differ vector is read over.
    let baseIdl: string = artifact (ContractsIdl.withContracts [])

    /// The stable label of a row's class, as `docs/idl-stability-classes.md` writes it.
    let severityLabel (s: Severity) : string =
        match s with
        | Additive -> "additive"
        | BreakingForEmitters -> "breaking-for-emitters"
        | BreakingWire -> "breaking-wire"
        | HostSurfaceOnly -> "host-surface-only"
        | Unclassifiable -> "undecided"

    let private segments (path: PathSegment list) : JVal =
        path
        |> List.map (function
            | PathSegment.Key k -> JStr k
            | PathSegment.Index i -> JInt i)
        |> JArr

    /// The refusal cases the artifact can spell: an ill-formed description has no JSON text, so that one
    /// refusal is the in-memory model's alone.
    let refusals: (string * string * Idl) list =
        ContractsIdl.refusalCases
        |> List.filter (fun (_, name, _) -> name <> "illFormedDescription")

    /// The refusals a vocabulary earns, each with its name and its path from the document root.
    let refusalsOf (idl: Idl) : (string * JVal) list =
        Contract.faults idl
        |> List.map (fun (path, fault) -> ContractFault.name fault, segments path)

    /// Every method of the fixture, as `<contract>.<method>`, with the canonical declaration of the
    /// capability it projects to on the server.
    let projections: (string * string) list =
        match ContractProjection.capabilities Server ContractsIdl.idl with
        | Ok capabilities -> capabilities |> List.map (fun c -> c.Id, CapabilityCodec.encode c)
        | Error faults -> failwithf "the contract fixture did not project: %A" faults

    /// The differ's answer for one pair of revisions: the verdict's class, and each row's case and class
    /// in report order.
    let classify (before: string) (after: string) : string * (string * string) list =
        match Diff.ofArtifacts (before, None) (after, None) with
        | Error e -> failwithf "ContractsCorpus.classify: %s" e
        | Ok verdict ->
            Diff.classLabel (Diff.verdictClass verdict),
            verdict.Changes
            |> List.map (fun row ->
                let case =
                    match Reflection.FSharpValue.GetUnionFields(row.Change, typeof<Change>) with
                    | info, _ -> info.Name

                case, severityLabel row.Severity)

    let private slug (label: string) : string =
        label
        |> String.map (fun c -> if System.Char.IsLetterOrDigit c then c else '-')
        |> fun s -> s.Trim('-')

    let private description =
        "The IDL's contracts (Phase 644, DECISIONS.md D260, docs/idl-contracts.md). `idl` is the canonical idl.json of the fixture vocabulary declaring no contract, and `contracts` the canonical text of the fixture's `contracts` member; a revision is `idl` with a `contracts` member added, the member left out when the array is empty. A `valid` vector names a revision by its `contracts` (and by its own `idl` where the vocabulary beside the contracts is not the file's), which every reader admits. A `refusal` vector names a revision the same way, which a reader refuses, and `expected.refusals` names each refusal it earns with the path from the document root, in order. A `projection` vector names a method of the fixture revision (`idl` with `contracts`) and expects `capability`, the canonical declaration of the capability the method projects to on the server, as a JSON string a host decodes with its capability codec. A `classify` vector gives the `contracts` member of the revision `before` and of the revision `after`, and expects the verdict's `class` (unchanged, host-surface, additive, breaking, undecided) and each row in report order: the change's case and its class (additive, breaking-wire, host-surface-only, undecided). An ill-formed description is refused by the model and has no JSON text, so it has no vector."

    /// What names a vector's revision: its `contracts`, and its own `idl` only when the vocabulary
    /// beside the contracts is not the file's.
    let private revisionOf (idl: Idl) : (string * CorpusCell) list =
        let own = artifact { idl with Contracts = [] }

        (if own = baseIdl then [] else [ "idl", CorpusFile.str own ])
        @ [ "contracts", CorpusFile.str (contractsOf idl) ]

    let private row (name: string) (case: string) (members: (string * CorpusCell) list) : CorpusCell =
        CorpusCell.Row(
            [ "name", CorpusFile.str name
              "case", CorpusFile.str case
              "answeredBy", CorpusFile.answeredBy AnsweredBy.Reference ]
            @ members
        )

    let render () : string =
        let valid =
            [ "the-fixture", ContractsIdl.idl
              "no-contract", ContractsIdl.withContracts []
              "one-contract", ContractsIdl.withContracts [ ContractsIdl.orders ] ]
            |> List.map (fun (name, idl) ->
                match refusalsOf idl with
                | [] ->
                    row
                        ("valid-" + name)
                        "valid"
                        (revisionOf idl
                         @ [ "expected", CorpusCell.Row [ "verdict", CorpusFile.str "accept" ] ])
                | faults -> failwithf "ContractsCorpus: the valid vector %s is refused: %A" name faults)

        let refused =
            refusals
            |> List.map (fun (label, _, idl) ->
                row
                    ("refused-" + slug label)
                    "refusal"
                    (revisionOf idl
                     @ [ "expected",
                         CorpusCell.Row
                             [ "refusals",
                               CorpusCell.Value(
                                   refusalsOf idl
                                   |> List.map (fun (name, path) -> JObj [ "name", JStr name; "path", path ])
                                   |> JArr
                               ) ] ]))

        let projected =
            projections
            |> List.map (fun (methodName, declaration) ->
                row
                    ("projects-" + methodName)
                    "projection"
                    [ "method", CorpusFile.str methodName
                      "expected", CorpusCell.Row [ "capability", CorpusFile.str declaration ] ])

        let classified =
            ContractsIdl.diffCases
            |> List.map (fun (name, before, after, _) ->
                let cb, ca = contractsOf before, contractsOf after
                let verdictClass, rows = classify (revision baseIdl cb) (revision baseIdl ca)

                row
                    ("classify-" + name)
                    "classify"
                    [ "before", CorpusFile.str cb
                      "after", CorpusFile.str ca
                      "expected",
                      CorpusCell.Row
                          [ "class", CorpusFile.str verdictClass
                            "changes",
                            CorpusCell.Value(
                                rows
                                |> List.map (fun (case, severity) ->
                                    JObj [ "change", JStr case; "class", JStr severity ])
                                |> JArr
                            ) ] ])

        CorpusFile.render
            { Head =
                [ "family", CorpusFile.str "idlContracts"
                  "description", CorpusFile.str description
                  "vocabulary", CorpusFile.str "tests/Fuaran.Core.Tests.Fixtures/ContractsIdl.fs"
                  "idl", CorpusFile.str baseIdl
                  "contracts", CorpusFile.str (contractsOf ContractsIdl.idl) ]
              Items = "vectors"
              Rows = valid @ refused @ projected @ classified }

    let path (dir: string) =
        System.IO.Path.Combine(dir, familyDirName, fileName)
