module Fuaran.Core.Tests.DecodeOracle

// ============================================================================
//  Phase 563 — the decode layer's model (`proofs/DecodeLayer.fst`, ladder row
//  `decode-layer-differential`) as the decode-reject export asks it, and the bridges the decode
//  differentials share with it: the value as `WireDecode.jval` and the refusal's code and path.
//  Moved from the test project (`ProofOracle.Bridges.fs`, `ProofOracle.DecodeLayerTests.fs`), so
//  each is defined once.
// ============================================================================

module M = DecodeLayer

open Fuaran.Core
open Fuaran.Core.Tests.VectorOracle

/// The model is parametric in the two numeric carriers; the bridge instantiates it at `<int, float>`.
type MJVal = WireDecode.jval<int, float>

let rec toModel (v: JVal) : MJVal =
    match v with
    | JStr s -> WireDecode.JStr s
    | JInt i -> WireDecode.JInt i
    | JBool b -> WireDecode.JBool b
    | JFloat f -> WireDecode.JFloat f
    | JArr xs -> WireDecode.JArr(xs |> List.map toModel)
    | JObj fields -> WireDecode.JObj(fields |> List.map (fun (k, v) -> k, toModel v))

/// A path step as the model spells it.
let segOf (s: PathSegment) : M.seg =
    match s with
    | PathSegment.Key k -> M.Key k
    | PathSegment.Index i -> M.Index(bigint i)

let mCodeOf (c: DecodeCode) : M.code =
    match c with
    | DecodeCode.InvalidJson -> M.InvalidJson
    | DecodeCode.MissingField -> M.MissingField
    | DecodeCode.WrongKind -> M.WrongKind
    | DecodeCode.UnknownTag -> M.UnknownTag
    | DecodeCode.OutOfRange -> M.OutOfRange
    | DecodeCode.UndeclaredMember -> M.UndeclaredMember
    | DecodeCode.LimitExceeded -> M.LimitExceeded
    | DecodeCode.NotAdmitted -> M.NotAdmitted
    | DecodeCode.SchemaFault -> M.SchemaFault


/// The model's code back as production's.
let codeOfModel (c: M.code) : DecodeCode =
    match c with
    | M.InvalidJson -> DecodeCode.InvalidJson
    | M.MissingField -> DecodeCode.MissingField
    | M.WrongKind -> DecodeCode.WrongKind
    | M.UnknownTag -> DecodeCode.UnknownTag
    | M.OutOfRange -> DecodeCode.OutOfRange
    | M.UndeclaredMember -> DecodeCode.UndeclaredMember
    | M.LimitExceeded -> DecodeCode.LimitExceeded
    | M.NotAdmitted -> DecodeCode.NotAdmitted
    | M.SchemaFault -> DecodeCode.SchemaFault

/// The model's path step back as production's.
let segOfModel (s: M.seg) : PathSegment =
    match s with
    | M.Key k -> PathSegment.Key k
    | M.Index i -> PathSegment.Index(int i)

// ---------------------------------------------------------------------------
//  The decode-reject vectors, as the export asks the model (Phase 563)
// ---------------------------------------------------------------------------

[<Literal>]
let Model = "DecodeLayer"

[<Literal>]
let LadderRow = "decode-layer-differential"

/// A vector's outcome: accepted, or the refusal's code and path.
type Outcome = Result<unit, DecodeCode * PathSegment list>

/// A shape (`DecodeRejectCorpus`'s vocabulary) compiled to the MODEL's combinators, one per shape
/// kind, exactly as the reference compiles it to production's: the shape is read once, by the export,
/// and each side answers it with its own decoder.
let rec shapeDecoder (defs: (string * JVal) list) (shape: JVal) : MJVal -> M.result<unit, M.derr> =
    let mem (name: string) = Decoder.tryMember name shape
    let unitOf (d: MJVal -> M.result<'a, M.derr>) = M.map ignore d
    let any = JObj [ "$type", JStr "any" ]

    match mem "$type" with
    | Some(JStr "string") -> unitOf M.str
    | Some(JStr "int") -> unitOf M.int_d
    | Some(JStr "float") -> unitOf (M.float_d float)
    | Some(JStr "bool") -> unitOf M.bool_d
    | Some(JStr "any") -> unitOf M.json
    | Some(JStr "list") ->
        let item = shapeDecoder defs (defaultArg (mem "of") any)

        match mem "maxItems" with
        | Some(JInt n) -> unitOf (M.bounded_list (bigint n) item)
        | _ -> unitOf (M.list_d item)
    | Some(JStr "object") ->
        let fields =
            match mem "fields" with
            | Some(JArr fs) -> fs
            | _ -> []

        let nameOf f =
            match Decoder.tryMember "name" f with
            | Some(JStr n) -> n
            | _ -> ""

        let reads =
            fields
            |> List.map (fun f ->
                let d = shapeDecoder defs (defaultArg (Decoder.tryMember "shape" f) any)

                match Decoder.tryMember "optional" f with
                | Some(JBool true) -> unitOf (M.opt_field (nameOf f) d)
                | _ -> M.field (nameOf f) d)

        let body (v: MJVal) =
            match v with
            | WireDecode.JObj _ -> unitOf (M.sequence reads) v
            | other -> M.Error(M.wrong_kind other)

        match mem "closed" with
        | Some(JBool true) -> M.closed (fields |> List.map nameOf) body
        | _ -> body
    | Some(JStr "tagged") ->
        let tagKey =
            match mem "key" with
            | Some(JStr k) -> k
            | _ -> "kind"

        let admit =
            match mem "admit" with
            | Some(JArr xs) ->
                Some(
                    xs
                    |> List.choose (function
                        | JStr s -> Some s
                        | _ -> None)
                )
            | _ -> None

        let cases =
            match mem "cases" with
            | Some(JArr cs) ->
                cs
                |> List.choose (fun c ->
                    match Decoder.tryMember "tag" c, Decoder.tryMember "shape" c with
                    | Some(JStr t), Some s ->
                        match admit with
                        | Some admitted when not (List.contains t admitted) ->
                            Some(t, (fun (_: MJVal) -> M.Error(M.under (M.Key tagKey) (M.make M.NotAdmitted))))
                        | _ -> Some(t, shapeDecoder defs s)
                    | _ -> None)
            | _ -> []

        M.tag_dispatch tagKey cases
    | Some(JStr "intRange") ->
        match mem "min", mem "max" with
        | Some(JInt lo), Some(JInt hi) -> unitOf (M.int_range (fun n -> lo <= n && n <= hi))
        | _ -> M.fail M.RSchemaFault
    | Some(JStr "ref") ->
        let name =
            match mem "name" with
            | Some(JStr n) -> n
            | _ -> ""

        match defs |> List.tryFind (fun (n, _) -> n = name) with
        | Some(_, s) -> fun v -> shapeDecoder defs s v
        | None -> M.fail M.RSchemaFault
    | _ -> M.fail M.RSchemaFault

/// A vector, as the models answer it: `input` read by the parser model (under `limits` when the
/// vector carries them) — its refusal is the decode layer's `InvalidJson` at the root
/// (`DecodeLayer.fst`, `resolves_in`) and the parser's answer — then decoded by the shape's model
/// decoder. A limit BREACH is declined: the parser model names a breach by its limit and position, and
/// the decode path the reference names it at is not in the model. `render` is the export's renderer.
let read
    (limits: JsonLimits option)
    (defs: (string * JVal) list)
    (shape: JVal)
    (render: Outcome -> CorpusCell)
    (input: string)
    : Asked =
    let parsed =
        match limits with
        | Some l -> JsonModel.readLimited l input
        | None -> JsonModel.readStrict input

    match parsed with
    | Error breach when breach.StartsWith "LimitExceeded" ->
        Declined(
            JsonModel.Model,
            JsonModel.LadderRow,
            "a limit breach: the parser model names it by its limit and position, not by the decode path"
        )
    | Error _ ->
        Answered
            { Model = JsonModel.Model
              LadderRow = JsonModel.LadderRow
              Cell = render (Error(DecodeCode.InvalidJson, [])) }
    | Ok jv ->
        let outcome =
            match shapeDecoder defs shape (toModel jv) with
            | M.Ok() -> Ok()
            | M.Error e -> Error(codeOfModel e.code, e.path |> List.map segOfModel)

        Answered
            { Model = Model
              LadderRow = LadderRow
              Cell = render outcome }
