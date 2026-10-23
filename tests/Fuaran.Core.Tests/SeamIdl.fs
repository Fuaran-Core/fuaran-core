module Fuaran.Core.Tests.SeamIdl

open System.IO
open Expecto
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

// ---------------------------------------------------------------------------
// Phase 551 — the seams' refusal and declaration unions, declared in the IDL. Each union stays a
// HAND-DECLARED public type in its own package (D195); what is declared here is its WIRE FORM, and
// the codec-only generator (D198) derives the encoder, the decoder and the strict-members reader from
// it. The five generated modules are committed beside the types they serve, each checked for drift
// by `SeamCodecTests` below.
//
//   - the capability seam (`Fuaran.Core.Function`): `InvokeError`, `DeclarationFault`, `Placement`
//     with `IslandKind`, and the async envelope `Deferred<'T>`;
//   - the capability pipeline (`Fuaran.Core.Function`): `ArgSource`, `PipelineNode` and the
//     pipeline record;
//   - the query seam (`Fuaran.Core.Query`): `QueryError`, `ColumnPredicate` and `SortKey` with
//     `SortDirection`;
//   - the schema delta's metadata change (`Fuaran.Core.Column`): `FieldChange`, discriminated by
//     `member`;
//   - the ops rejection envelope (`Fuaran.Core.AiSurface`): `Rejection<'Id>` with `NestRelation`.
//
// Every seam union takes its tags by `LowerFirst` (D163, by construction since D198). A value the
// IDL does not model — a value space, a column type, a cell, a decode refusal, a unit — is a TYPED
// hosted slot: its own codec reads it, and its refusal keeps its code and path. A union's cases are
// declared in the order its reader has always named them in, so an unknown tag's sentence lists the
// tags as it did.
// ---------------------------------------------------------------------------

let private field name ty opt : IdlField =
    { Name = name
      Type = ty
      Opt = opt
      Aliases = []
      Keyed = false
      Presence = None
      Annotations = Annotations.Empty }

let private req name ty = field name ty Required
let private opt name ty = field name ty Optional

let private case tag fields : IdlUnionCase =
    { Tag = tag
      Fields = fields
      Annotations = Annotations.Empty
      Hosted = None }

let private union name ps cases : IdlUnion =
    { Name = name
      Params = ps
      Cases = cases
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.LowerFirst
      Discrimination = UnionDiscrimination.Tagged }

/// A slot (or a case) whose host codec answers a typed `DecodeError`.
let private typed (fs: string) (enc: string) (dec: string) : HostedCodec =
    { FSharp = fs
      Encode = enc
      Decode = dec
      Wire = None
      Format = None
      Refusal = HostedRefusal.Typed }

let private strs = TList TStr

let private vocabulary unions enums records discriminator : Idl =
    { Kinds = []
      Unions = unions
      Enums = enums
      Records = records
      Defaults = []
      NodeFields = []
      Ops = []
      Wire =
        { WireShape.Default with
            Discriminator = discriminator }
      Harden = HardenPolicy.Undeclared }

/// One generated module: its vocabulary, the request, the module name and the committed file.
type SeamModule =
    { Stem: string
      Idl: Idl
      Request: CodecOnly
      ModuleName: string
      File: string }

// ---- the capability seam ---------------------------------------------------------------------

/// A value space: `SpaceCodec`'s, the one writer and reader every seam shares.
let private space =
    THosted(typed "Fuaran.Core.ValueSpace" "SpaceCodec.toJson" "SpaceCodec.decoder")

let private capabilityIdl =
    vocabulary
        [ union
              "Placement"
              []
              [ case "BuildTime" []
                case "Server" []
                case "ClientDeclarative" []
                case "Precomputed" []
                case "ClientIsland" [ req "island" (TEnum "IslandKind") ] ]
          union
              "DeclarationFault"
              []
              [ case "EmptySpace" [ req "addr" TStr; req "space" space ]
                case "NonFiniteBound" [ req "addr" TStr ]
                case "DuplicateHoleAddr" [ req "addr" TStr ]
                case "HoleUnderSlot" [ req "node" TStr ] ]
          union
              "InvokeError"
              []
              [ case "NoSuchCapability" [ req "id" TStr; req "known" strs ]
                case "DuplicateCapability" [ req "id" TStr ]
                case "UnknownArg" [ req "addr" TStr; req "declared" strs ]
                case "ArgOutOfSpace" [ req "addr" TStr; req "space" space; req "got" TStr ]
                case "RequiredArgsUnbound" [ req "addrs" strs ]
                case "UninvocableArg" [ req "addr" TStr ]
                case "BodyFailed" [ req "reason" TStr ]
                case "NonTotalCapability" [ req "id" TStr; req "addrs" strs ]
                case "IllFormedCapability" [ req "id" TStr; req "fault" (TUnion("DeclarationFault", [])) ]
                case "DuplicateArg" [ req "addr" TStr ]
                case "PolicyRefused" [ req "policy" TStr; req "reason" TStr; req "allowed" strs ]
                case "ApprovalRequired" [ req "policy" TStr ] ]
          union
              "Deferred"
              [ "T" ]
              [ case "Pending" []
                case "Ready" [ req "value" (TVar "T") ]
                case "Failed" [ req "message" TStr ] ] ]
        [ Declare.enumWith "IslandKind" [ "Pyodide", "pyodide"; "Fable", "fable"; "Js", "js" ] ]
        []
        "$type"

let capability: SeamModule =
    { Stem = "CapabilitySeamCodec"
      Idl = capabilityIdl
      Request =
        { Roots = [ "InvokeError"; "DeclarationFault"; "Placement"; "Deferred" ]
          HostTypes =
            Map.ofList
                [ "IslandKind", "Fuaran.Core.IslandKind"
                  "Placement", "Fuaran.Core.Placement"
                  "DeclarationFault", "Fuaran.Core.DeclarationFault"
                  "InvokeError", "Fuaran.Core.InvokeError"
                  "Deferred", "Fuaran.Core.Deferred" ] }
      ModuleName = "Fuaran.Core.CapabilitySeamCodec"
      File = "src/Fuaran.Core.Function/CapabilitySeamCodec.fs" }

// ---- the capability pipeline -----------------------------------------------------------------

/// A node's declared output space: `SpaceCodec`'s reader, then the admission check (Phase 307).
let private outputSpace =
    THosted(typed "Fuaran.Core.ValueSpace" "SpaceCodec.toJson" "PipelineWire.outputSpace")

/// One argument of an `Invoke` node: the host's `(addr, source)` pair, written as one object.
let private pipelineArg =
    THosted(
        typed
            "string * Fuaran.Core.ArgSource"
            "(fun (addr: string, source: Fuaran.Core.ArgSource) -> JObj [ \"addr\", JStr addr; \"source\", encArgSource source ])"
            "(fun (j: JVal) -> Decoder.field \"addr\" Decoder.str j |> Result.bind (fun addr -> Decoder.field \"source\" decArgSource j |> Result.map (fun source -> addr, source)))"
    )

let private pipelineIdl =
    vocabulary
        [ union "ArgSource" [] [ case "Literal" [ req "value" TStr ]; case "FromNode" [ req "node" TStr ] ]
          union
              "PipelineNode"
              []
              [ case "Source" [ req "id" TStr; req "dataRef" TStr; req "outputType" outputSpace ]
                case
                    "Invoke"
                    [ req "id" TStr
                      req "capabilityId" TStr
                      req "outputType" outputSpace
                      req "args" (TList pipelineArg) ] ] ]
        []
        [ { Name = "CapabilityPipeline"
            Fields = [ req "nodes" (TList(TUnion("PipelineNode", []))) ]
            Annotations = Annotations.Empty
            Inline = false } ]
        "$type"

let pipeline: SeamModule =
    { Stem = "PipelineSeamCodec"
      Idl = pipelineIdl
      Request =
        { Roots = [ "ArgSource"; "PipelineNode"; "CapabilityPipeline" ]
          HostTypes =
            Map.ofList
                [ "ArgSource", "Fuaran.Core.ArgSource"
                  "PipelineNode", "Fuaran.Core.PipelineNode"
                  "CapabilityPipeline", "Fuaran.Core.CapabilityPipeline" ] }
      ModuleName = "Fuaran.Core.PipelineSeamCodec"
      File = "src/Fuaran.Core.Function/PipelineSeamCodec.fs" }

// ---- the query seam --------------------------------------------------------------------------

/// A column type, spelled by `ColumnType.tag`.
let private columnType =
    THosted(typed "Fuaran.Core.ColumnType" "QueryWire.columnTypeJson" "QueryWire.columnTypeOf")

/// A predicate kind named by its wire tag (`PredicateNotApplicable`'s `predicate`).
let private predicateTag = THosted(typed "string" "JStr" "QueryWire.predicateTagOf")

/// A comparison's `type` and `value` are one host cell, read at the type its document states: a
/// CASE-level host codec over the column codec's one-cell reader and writer (D198).
let private compared (tag: string) : IdlUnionCase =
    { case tag [ req "column" TStr; req "type" TStr; req "value" TJson ] with
        Hosted = Some(typed "string * Fuaran.Core.Cell" "QueryWire.comparedMembers" "QueryWire.comparedOf") }

let private queryIdl =
    vocabulary
        [ union
              "ColumnPredicate"
              []
              [ compared "EqualTo"
                compared "GreaterThan"
                compared "AtLeast"
                compared "LessThan"
                compared "AtMost"
                case "Contains" [ req "column" TStr; req "text" TStr ]
                case "IsNull" [ req "column" TStr ]
                case "IsNotNull" [ req "column" TStr ] ]
          union
              "QueryError"
              []
              [ case "NoSuchQuery" [ req "id" TStr; req "known" strs ]
                case "DuplicateQuery" [ req "id" TStr ]
                case "UnknownParam" [ req "name" TStr; req "declared" strs ]
                case "ParamTypeMismatch" [ req "name" TStr; req "expected" columnType; req "got" columnType ]
                case "RequiredParamsUnbound" [ req "names" strs ]
                case "SourceNotResolved" [ req "ref" TStr ]
                case "ExecutionFailed" [ req "detail" TStr; req "recoverable" strs ]
                case "Timeout" []
                case "RequiredParamsNull" [ req "names" strs ]
                case "DuplicateParam" [ req "name" TStr ]
                case "QueryPolicyRefused" [ req "policy" TStr; req "reason" TStr; req "allowed" strs ]
                case "QueryApprovalRequired" [ req "policy" TStr ]
                case
                    "UnreadableArgs"
                    [ req "error" (THosted(typed "Fuaran.Core.DecodeError" "DecodeError.toJson" "DecodeError.decoder")) ]
                case "UnknownColumn" [ req "name" TStr; req "declared" strs ]
                case "PredicateTypeMismatch" [ req "column" TStr; req "expected" columnType; req "got" columnType ]
                case
                    "PredicateNotApplicable"
                    [ req "predicate" predicateTag; req "column" TStr; req "columnType" columnType ]
                case "IllFormedLiteral" [ req "column" TStr; req "reason" TStr ]
                case "DuplicateSortColumn" [ req "column" TStr ]
                case "PredicateNotHonoured" [ req "predicate" (TUnion("ColumnPredicate", [])) ]
                case "OrderNotHonoured" [ req "column" TStr ] ] ]
        [ Declare.enumWith "SortDirection" [ "Ascending", "ascending"; "Descending", "descending" ] ]
        [ { Name = "SortKey"
            Fields = [ req "column" TStr; req "direction" (TEnum "SortDirection") ]
            Annotations = Annotations.Empty
            Inline = false } ]
        "$type"

let query: SeamModule =
    { Stem = "QuerySeamCodec"
      Idl = queryIdl
      Request =
        { Roots = [ "QueryError"; "ColumnPredicate"; "SortKey" ]
          HostTypes =
            Map.ofList
                [ "QueryError", "Fuaran.Core.QueryError"
                  "ColumnPredicate", "Fuaran.Core.ColumnPredicate"
                  "SortKey", "Fuaran.Core.SortKey"
                  "SortDirection", "Fuaran.Core.SortDirection" ] }
      ModuleName = "Fuaran.Core.QuerySeamCodec"
      File = "src/Fuaran.Core.Query/QuerySeamCodec.fs" }

// ---- the schema delta's metadata change ------------------------------------------------------

/// A unit, as its canonical text (`Unit.render` / `Unit.parse`).
let private unitText =
    THosted(
        typed
            "Fuaran.Core.UnitOfMeasure"
            "(fun (u: Fuaran.Core.UnitOfMeasure) -> JStr(Unit.render u))"
            "(fun (j: JVal) -> Decoder.str j |> Result.bind (fun s -> match Unit.parse s with Ok u -> Ok u | Error _ -> Error(DecodeError.make DecodeCode.OutOfRange \"a unit the kit parses\" (\"'\" + s + \"' is not a unit the kit parses\"))))"
    )

let private fieldChangeIdl =
    vocabulary
        [ union
              "FieldChange"
              []
              [ case "Unit" [ opt "from" unitText; opt "to" unitText ]
                case "Label" [ opt "from" TStr; opt "to" TStr ]
                case "Description" [ opt "from" TStr; opt "to" TStr ]
                case "Ext" [ req "key" TStr; opt "from" TStr; opt "to" TStr ] ] ]
        []
        []
        "member"

let fieldChange: SeamModule =
    { Stem = "FieldChangeSeamCodec"
      Idl = fieldChangeIdl
      Request =
        { Roots = [ "FieldChange" ]
          HostTypes = Map.ofList [ ("FieldChange", "Fuaran.Core.FieldChange") ] }
      ModuleName = "Fuaran.Core.FieldChangeSeamCodec"
      File = "src/Fuaran.Core.Column/FieldChangeSeamCodec.fs" }

// ---- the ops rejection envelope --------------------------------------------------------------

let private rejectionIdl =
    let ids = TList(TVar "Id")

    vocabulary
        [ union
              "Rejection"
              [ "Id" ]
              [ case "UnknownNode" [ req "target" (TVar "Id"); req "addressable" ids ]
                case "DuplicateId" [ req "id" (TVar "Id") ]
                case "CannotRemoveRoot" []
                case "WouldNestUnderSelf" [ req "target" (TVar "Id"); req "relation" (TEnum "NestRelation") ]
                case "NotAContainer" [ req "target" (TVar "Id"); req "kindTag" TStr ]
                case "ReorderMismatch" [ req "parent" (TVar "Id"); req "expected" ids; req "got" ids ]
                case "Rejected" [ req "code" TStr; req "message" TStr ]
                case "KeyedPosition" [ req "target" (TVar "Id"); req "holder" (TVar "Id"); req "slot" TInt ]
                case
                    "IllegalChild"
                    [ req "child" (TVar "Id")
                      req "childKind" TStr
                      req "parent" (TVar "Id")
                      req "parentKind" TStr
                      req "legal" strs ]
                case "StillReferenced" [ req "target" (TVar "Id"); req "referrers" ids ]
                case "RootIdMismatch" [ req "before" (TVar "Id"); req "after" (TVar "Id") ] ] ]
        [ Declare.enumWith "NestRelation" [ "Self", "self"; "Descendant", "descendant" ] ]
        []
        "$type"

let rejection: SeamModule =
    { Stem = "RejectionSeamCodec"
      Idl = rejectionIdl
      Request =
        { Roots = [ "Rejection" ]
          HostTypes =
            Map.ofList
                [ "Rejection", "Fuaran.Core.Rejection"
                  "NestRelation", "Fuaran.Core.NestRelation" ] }
      ModuleName = "Fuaran.Core.RejectionSeamCodec"
      File = "src/Fuaran.Core.AiSurface/RejectionSeamCodec.fs" }

/// The five generated modules, in package order.
let modules: SeamModule list =
    [ capability; pipeline; query; fieldChange; rejection ]

/// The `FUARAN_REGEN` stem handle of each module (`Approval.Regenerated`), so a typo is a compile error.
let private stemHandle (m: SeamModule) : string =
    match m.Stem with
    | "CapabilitySeamCodec" -> Approval.Regenerated.CapabilitySeam
    | "PipelineSeamCodec" -> Approval.Regenerated.PipelineSeam
    | "QuerySeamCodec" -> Approval.Regenerated.QuerySeam
    | "FieldChangeSeamCodec" -> Approval.Regenerated.FieldChangeSeam
    | _ -> Approval.Regenerated.RejectionSeam

/// The module text the generator emits for one seam.
let generate (m: SeamModule) : Result<string, CodegenError> =
    Gen.fsharpCodecModule GenSupport.Empty m.Request m.ModuleName m.Idl

[<Tests>]
let seamCodecTests =
    testList
        "Phase 551 - the seams' codecs are derived from their IDL declaration"
        [ for m in modules do
              testCase (sprintf "the committed %s is what the generator emits (byte for byte)" m.Stem)
              <| fun _ ->
                  let generated =
                      match generate m with
                      | Ok s -> s
                      | Error e -> failtestf "codegen refused the %s vocabulary: %s" m.Stem (CodegenError.describe e)

                  let path = Snapshots.repoFile m.File
                  Approval.write Approval.Regen (stemHandle m) path generated |> ignore

                  Expect.equal
                      (File.ReadAllText path)
                      generated
                      (sprintf "regenerate with: FUARAN_REGEN=%s dotnet <the test dll>" m.Stem)

          testCase "every seam vocabulary declares itself without a fault"
          <| fun _ ->
              for m in modules do
                  Expect.isEmpty (Declare.errors m.Idl) (m.Stem + ": the vocabulary is well-formed")

          testCase "the seam-refusal family is green over its vectors, and red on a planted wrong path"
          <| fun _ ->
              let results = Fuaran.Core.Conformance.seamRefusalLaws ()
              Expect.isNonEmpty results "the family ran"

              for r in results do
                  Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

              let missingIsland =
                  Fuaran.Core.SeamRefusalLaws.vectors
                  |> List.find (fun v -> v.Reader = "placement" && v.Name = "missing-island")

              let planted =
                  { missingIsland with
                      Expected =
                          Fuaran.Core.SeamRefusalLaws.Refused(
                              Fuaran.Core.DecodeCode.MissingField,
                              [ Fuaran.Core.PathSegment.Key "isle" ]
                          ) }

              Expect.exists
                  (Fuaran.Core.SeamRefusalLaws.lawsOver [ planted ])
                  (fun r -> not r.Passed)
                  "a vector naming the wrong path reds the family" ]
