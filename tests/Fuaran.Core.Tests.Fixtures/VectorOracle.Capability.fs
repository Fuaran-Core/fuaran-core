module Fuaran.Core.Tests.CapabilityOracle

// ============================================================================
//  Phase 563 — the function seam's model (`proofs/Capability.fst`, ladder row `capability-differential`)
//  as the exporters ask it, and the bridges the `Proofs.Oracle` differentials share with them.
//
//  Moved here from the test project (`ProofOracle.Bridges.fs`, the capability, query and seam
//  differentials), where they were private to the differentials: an export that must FAIL on a
//  disagreement calls the model itself, and the fixtures library is the one both read. Each bridge is
//  defined once, here.
// ============================================================================

// Aliased before `Fuaran.Core` is opened: production's `Capability` module shares the model's name.
module ModelCap = Capability

open Fuaran.Core
open Fuaran.Core.Tests.VectorOracle

[<Literal>]
let Model = "Capability"

[<Literal>]
let LadderRow = "capability-differential"

let private inv = System.Globalization.CultureInfo.InvariantCulture

let hostToModel (h: HostEffect) : ModelCap.host_effect =
    match h with
    | Pure -> ModelCap.Pure
    | ReadsHost -> ModelCap.ReadsHost
    | WritesHost -> ModelCap.WritesHost

/// The set as the model's characteristic vector: a flag per factor, set exactly when the factor
/// is a member.
let detToModel (d: DeterminismSource) : ModelCap.determinism_source =
    { ModelCap.determinism_source.has_clock = Set.contains ClockFactor d
      ModelCap.determinism_source.has_random = Set.contains RandomFactor d
      ModelCap.determinism_source.has_network = Set.contains NetworkFactor d }

let effToModel (e: EffectClass) : ModelCap.effect_class =
    { ModelCap.effect_class.host = hostToModel e.Host
      ModelCap.effect_class.determinism = detToModel e.Determinism }

/// A float range's bounds cross as opaque carriers (the readers premise): the round-trip `R`
/// format, which parses back to the same double.
let spaceToModel (s: ValueSpace) : ModelCap.value_space =
    match s with
    | IntRange(lo, hi) -> ModelCap.IntRange(bigint lo, bigint hi)
    | FloatRange(lo, hi) -> ModelCap.FloatRange(lo.ToString("R", inv), hi.ToString("R", inv))
    | StringLen(lo, hi) -> ModelCap.StringLen(bigint lo, bigint hi)
    | Enum xs -> ModelCap.Enum xs
    | AnyString -> ModelCap.AnyString
    | SlotTree c -> ModelCap.SlotTree(toMOpt c)

/// The model's value space back.
let spaceOfModel (s: ModelCap.value_space) : ValueSpace =
    match s with
    | ModelCap.IntRange(lo, hi) -> IntRange(int lo, int hi)
    | ModelCap.FloatRange(lo, hi) -> FloatRange(System.Double.Parse(lo, inv), System.Double.Parse(hi, inv))
    | ModelCap.StringLen(lo, hi) -> StringLen(int lo, int hi)
    | ModelCap.Enum xs -> Enum xs
    | ModelCap.AnyString -> AnyString
    | ModelCap.SlotTree c -> SlotTree(ofMOpt c)

let holeKindToModel (k: HoleKind) : ModelCap.hole_kind =
    match k with
    | ValueHole s -> ModelCap.ValueHole(spaceToModel s)
    | SlotHole c -> ModelCap.SlotHole(toMOpt c)
    | RepeatHole s -> ModelCap.RepeatHole(spaceToModel s)
    | ActionHole e -> ModelCap.ActionHole(effToModel e)

let sigEntryToModel (e: SigEntry) : ModelCap.sig_entry =
    { ModelCap.sig_entry.s_addr = e.Addr
      ModelCap.sig_entry.s_name = e.Name
      ModelCap.sig_entry.s_kind = holeKindToModel e.Kind
      ModelCap.sig_entry.s_required = e.Required }

let sigToModel (sg: Signature) : ModelCap.signature =
    { ModelCap.signature.sg_name = sg.Name
      ModelCap.signature.sg_holes = sg.Holes |> List.map sigEntryToModel
      ModelCap.signature.sg_effect = effToModel sg.Effect }

let placementToModel (p: Placement) : ModelCap.placement =
    match p with
    | BuildTime -> ModelCap.BuildTime
    | Server -> ModelCap.Server
    | ClientDeclarative -> ModelCap.ClientDeclarative
    | ClientIsland Pyodide -> ModelCap.ClientIsland ModelCap.Pyodide
    | ClientIsland Fable -> ModelCap.ClientIsland ModelCap.Fable
    | ClientIsland Js -> ModelCap.ClientIsland ModelCap.Js
    | Precomputed -> ModelCap.Precomputed

let capToModel (c: Capability) : ModelCap.capability =
    { ModelCap.capability.c_id = c.Id
      ModelCap.capability.c_signature = sigToModel c.Signature
      ModelCap.capability.c_determinism = detToModel c.Determinism
      ModelCap.capability.c_placement = placementToModel c.Placement }

/// The readers premise made concrete: the five host functions `Space.validate` and
/// `Space.wellFormed` reach for, exactly as production calls them (Phase 229 added `kind_of`,
/// production's `Space.slotKindOf`; Phase 307 `float_fault`, and restated `int_of` as production's
/// own invariant reader — `Space.canonical` over the whole `int` range — where it was
/// `Int32.TryParse` under the CURRENT culture, a reader production stopped using at Phase 295).
let readers: ModelCap.readers =
    { ModelCap.readers.int_of =
        fun s ->
            match Space.canonical (IntRange(System.Int32.MinValue, System.Int32.MaxValue)) s with
            | Some c -> FStar_Pervasives_Native.Some(bigint (System.Int32.Parse(c, inv)))
            | None -> FStar_Pervasives_Native.None
      ModelCap.readers.float_in =
        fun lo hi s -> Space.validate (FloatRange(System.Double.Parse(lo, inv), System.Double.Parse(hi, inv))) s
      ModelCap.readers.str_len = fun s -> bigint s.Length
      ModelCap.readers.kind_of = fun s -> toMOpt (Space.slotKindOf s)
      ModelCap.readers.float_fault =
        fun lo hi ->
            match Space.wellFormed (FloatRange(System.Double.Parse(lo, inv), System.Double.Parse(hi, inv))) with
            | Ok() -> FStar_Pervasives_Native.None
            | Error SpaceFault.Empty -> FStar_Pervasives_Native.Some ModelCap.SEmpty
            | Error SpaceFault.NonFinite -> FStar_Pervasives_Native.Some ModelCap.SNonFinite }

/// Production's own three calls, for the model's capture key (Phase 225): the hash, the address
/// order and the field renderer, with a value canonicalised by its declared space.
let keyRenderers: ModelCap.key_renderers =
    { ModelCap.key_renderers.k_hash = Hash.fnv1a
      ModelCap.key_renderers.k_addr_le = fun (a: string) (b: string) -> System.String.CompareOrdinal(a, b) <= 0
      ModelCap.key_renderers.k_field = Hash.canonicalField
      ModelCap.key_renderers.k_canonical =
        fun (sp: ModelCap.value_space) (v: string) ->
            match Space.canonical (spaceOfModel sp) v with
            | Some c -> FStar_Pervasives_Native.Some c
            | None -> FStar_Pervasives_Native.None }

/// A production `JVal` as the capability model's `jval` (Phase 354; shared with the query codec's
/// differential since Phase 455), and back. A float crosses as its round-trip `R` text.
let rec jvalToModel (v: JVal) : ModelCap.jval =
    match v with
    | JStr s -> ModelCap.JStr s
    | JInt i -> ModelCap.JInt(bigint i)
    | JBool b -> ModelCap.JBool b
    | JFloat f -> ModelCap.JFloat(f.ToString("R", inv))
    | JArr xs -> ModelCap.JArr(List.map jvalToModel xs)
    | JObj fs -> ModelCap.JObj(fs |> List.map (fun (k, x) -> k, jvalToModel x))

let rec jvalOfModel (v: ModelCap.jval) : JVal =
    match v with
    | ModelCap.JStr s -> JStr s
    | ModelCap.JInt i -> JInt(int i)
    | ModelCap.JBool b -> JBool b
    | ModelCap.JFloat f -> JFloat(System.Double.Parse(f, inv))
    | ModelCap.JArr xs -> JArr(List.map jvalOfModel xs)
    | ModelCap.JObj fs -> JObj(fs |> List.map (fun (k, x) -> k, jvalOfModel x))

/// The one float read the codecs make — `float i`, as `Decoder.float` reads a `JInt`.
let codecReaders: ModelCap.codec_readers =
    { ModelCap.codec_readers.float_of_int = fun i -> (float (int i)).ToString("R", inv) }

/// The model's decode code as production's.
let modelCodeOf (c: ModelCap.decode_code) : DecodeCode =
    match c with
    | ModelCap.InvalidJson -> DecodeCode.InvalidJson
    | ModelCap.MissingField -> DecodeCode.MissingField
    | ModelCap.WrongKind -> DecodeCode.WrongKind
    | ModelCap.UnknownTag -> DecodeCode.UnknownTag
    | ModelCap.OutOfRange -> DecodeCode.OutOfRange
    | ModelCap.UndeclaredMember -> DecodeCode.UndeclaredMember
    | ModelCap.LimitExceeded -> DecodeCode.LimitExceeded
    | ModelCap.NotAdmitted -> DecodeCode.NotAdmitted
    | ModelCap.SchemaFault -> DecodeCode.SchemaFault

/// An argument verdict, in the classes the `capabilityLaws` vectors distinguish: the exporter renders
/// either side's verdict through this one shape.
[<RequireQualifiedAccess>]
type Verdict =
    | Accept
    | OutOfSpace of addr: string
    | UnknownArg of addr: string
    | Unexpected

/// Production's `validateArgs` result as a verdict.
let verdictOf (r: Result<unit, InvokeError>) : Verdict =
    match r with
    | Ok() -> Verdict.Accept
    | Error(ArgOutOfSpace(addr, _, _)) -> Verdict.OutOfSpace addr
    | Error(InvokeError.UnknownArg(addr, _)) -> Verdict.UnknownArg addr
    | Error _ -> Verdict.Unexpected

/// `validate_args`, over the readers premise, as a verdict.
let validateArgs (c: Capability) (args: (string * string) list) : Verdict =
    match ModelCap.validate_args readers (capToModel c) args with
    | ModelCap.Ok() -> Verdict.Accept
    | ModelCap.Error(ModelCap.ArgOutOfSpace(addr, _, _)) -> Verdict.OutOfSpace addr
    | ModelCap.Error(ModelCap.UnknownArg(addr, _)) -> Verdict.UnknownArg addr
    | ModelCap.Error _ -> Verdict.Unexpected

/// `invocation_key`, over production's key renderers.
let invocationKey (c: Capability) (args: (string * string) list) : string =
    ModelCap.invocation_key keyRenderers (capToModel c) args

/// `determinism_tag_of`.
let determinismTag (c: Capability) : string =
    ModelCap.determinism_tag_of (capToModel c)

/// A capability vector's model answer, its `expected` members as the exporter builds them.
let answer (expected: (string * CorpusCell) list) : Answer option =
    Some
        { Model = Model
          LadderRow = LadderRow
          Cell = CorpusCell.Row expected }
