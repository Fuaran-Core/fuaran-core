namespace Fuaran.Core.Idl.Emit

// ---------------------------------------------------------------------------
// The F# collecting decoders' runtime prelude (Phase 377), as the text the generator emits. It is
// data with no dependency on the emitters, so it sits before `FSharpCodec`, which splices it into
// every module that requests `Gen.Derivation.SpecDecoders` (Phase 541 moved it out of that file,
// which the 2,000-line rule of D141 holds).
// ---------------------------------------------------------------------------

module internal FSharpCollecting =

    /// The collecting prelude, emitted once per module that requests the collecting decoders. It
    /// states the defect order, which a test pins.
    let helpers: string =
        """// ---------------------------------------------------------------------------
// Phase 377 — COLLECTING DECODERS. Beside every short-circuiting `dec*` decoder above, a `col*`
// decoder answers EVERY defect it finds, as a `DecodeError list`, in one deterministic order:
//   - an object's members in FIELD DECLARATION ORDER (a node: `id`, its kind, then its envelope
//     fields), each member's defects at that member's position, its nested defects included
//     (depth-first);
//   - a list's items in index order, and a map's entries in document order;
//   - a leaf (a scalar, an enum, a sentinel, a hosted slot, verbatim JSON) reports at most one
//     defect, and so does a value of the wrong kind or an absent or unknown discriminator, whose
//     members are never read.
// The first defect of a collecting decoder is the defect its short-circuiting twin reports, and on
// a clean input both answer the same value. A case refine runs once its members all decoded, and a
// host projection supplies only a short-circuiting decoder, so each adds at most one defect of its
// own. Phase 541 — every node, non-generic union, record and kind spec is read through one record
// of decoders, `CollectingDecoders`, so a host's decoder at a type applies at every depth. Codes
// and paths are Core's `DecodeError`; a consumer whose specification orders or names defects
// differently maps them at its own seam.
// ---------------------------------------------------------------------------
let private cOne (r: Result<'T, DecodeError>) : Result<'T, DecodeError list> =
    match r with
    | Ok v -> Ok v
    | Error e -> Error [ e ]

let private cLift (dec: JVal -> Result<'T, DecodeError>) (j: JVal) : Result<'T, DecodeError list> = cOne (dec j)

let private cErrs (r: Result<'T, DecodeError list>) : DecodeError list =
    match r with
    | Ok _ -> []
    | Error es -> es

// One step further from the root, for every defect a member or an item answered.
let private cUnder (step: PathSegment) (r: Result<'T, DecodeError list>) : Result<'T, DecodeError list> =
    match r with
    | Ok v -> Ok v
    | Error es -> Error(es |> List.map (DecodeError.under step))

// The first defect — how a short-circuiting decoder is handed a collecting codec.
let private cFirst (r: Result<'T, DecodeError list>) : Result<'T, DecodeError> =
    match r with
    | Ok v -> Ok v
    | Error(e :: _) -> Error e
    | Error [] -> dFail DecodeCode.SchemaFault "a refusal" "a collecting decoder answered no defect and no value"

let private cObj (j: JVal) : Result<(string * JVal) list, DecodeError list> = cOne (dObj j)

let private cList (dec: JVal -> Result<'T, DecodeError list>) (j: JVal) : Result<'T list, DecodeError list> =
    match j with
    | JArr xs ->
        let results = xs |> List.mapi (fun i x -> dec x |> cUnder (PathSegment.Index i))

        match results |> List.collect cErrs with
        | [] -> Ok(results |> List.choose (function Ok v -> Some v | Error _ -> None))
        | errors -> Error errors
    | _ -> cOne (dFail DecodeCode.WrongKind "array" "expected an array")

// Every entry is checked, in document order; a repeated key keeps its FIRST value, as `dMap` does.
let private cMap (dec: JVal -> Result<'T, DecodeError list>) (j: JVal) : Result<Map<string, 'T>, DecodeError list> =
    match j with
    | JObj fs ->
        let results = fs |> List.map (fun (k, v) -> k, (dec v |> cUnder (PathSegment.Key k)))

        match results |> List.collect (snd >> cErrs) with
        | [] ->
            (Map.empty, results)
            ||> List.fold (fun items (k, r) ->
                match r with
                | Ok d when not (Map.containsKey k items) -> Map.add k d items
                | _ -> items)
            |> Ok
        | errors -> Error errors
    | _ -> cOne (dFail DecodeCode.WrongKind "object" "expected an object")

// An absent required member is `dReq`'s refusal, sentence and path included.
let private cReq (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, DecodeError list>) : Result<'T, DecodeError list> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v |> cUnder (PathSegment.Key name)
    | None -> cOne (dReq name fs (fun _ -> dFail DecodeCode.SchemaFault "never read" "never read"))

let private cOpt (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, DecodeError list>) : Result<'T option, DecodeError list> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v |> cUnder (PathSegment.Key name) |> Result.map Some
    | None -> Ok None

let private cDef (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, DecodeError list>) (dflt: 'T) : Result<'T, DecodeError list> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v |> cUnder (PathSegment.Key name)
    | None -> Ok dflt"""

    /// Phase 541 — the refine helper of the collecting decoders, emitted when the support declares
    /// a case refine: `FSharpCodec.collectRefined` hands it the collected members, and a refusal is
    /// `dRefine`'s, code, expectation and position included.
    let refineHelper: string =
        """// A declared case refine over the COLLECTED members: every member decoded, and the refine's
// refusal is `dRefine`'s — `OutOfRange` at the case's object, its sentence unchanged.
let private cRefine (f: 'T -> Result<'U, string>) (r: Result<'T, DecodeError list>) : Result<'U, DecodeError list> =
    match r with
    | Error es -> Error es
    | Ok v ->
        match f v with
        | Ok u -> Ok u
        | Error m -> cOne (dFail DecodeCode.OutOfRange "a case its refinement admits" m)"""
