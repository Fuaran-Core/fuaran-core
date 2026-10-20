module Fuaran.Core.Tests.ProofOracle.CapabilityTests

// The `Proofs.Oracle` differential for `proofs/Capability.fst`: the extracted model
// (`proofs/oracle/Capability.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

// Phase 177 — the extracted FUNCTION-SEAM model, bound the same way and for the same reason:
// production's `Fuaran.Core.Capability` module shares the model's name once `Fuaran.Core` is open.
module ModelCap = Capability
module ModelProp = Propagation

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges

let private modelSpaceRender (s: ModelCap.value_space) : string =
    match s with
    | ModelCap.IntRange(lo, hi) -> sprintf "int[%s..%s]" (string lo) (string hi)
    | ModelCap.FloatRange(lo, hi) -> sprintf "float[%s..%s]" lo hi
    | ModelCap.StringLen(lo, hi) -> sprintf "len[%s..%s]" (string lo) (string hi)
    | ModelCap.Enum xs -> sprintf "enum{%s}" (String.concat "," xs)
    | ModelCap.AnyString -> "any"
    | ModelCap.SlotTree c -> sprintf "tree{%s}" (defaultArg (ofMOpt c) "*")

/// The readers premise made concrete: the five host functions `Space.validate` and
/// `Space.wellFormed` reach for, exactly as production calls them (Phase 229 added `kind_of`,
/// production's `Space.slotKindOf`; Phase 307 `float_fault`, and restated `int_of` as production's
/// own invariant reader — `Space.canonical` over the whole `int` range — where it was
/// `Int32.TryParse` under the CURRENT culture, a reader production stopped using at Phase 295).
let private readers: ModelCap.readers =
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

/// The go-red: an int reader that reads nothing, so every int-ranged value is out of space to
/// the model and in space to production.
let private blindReaders: ModelCap.readers =
    { readers with
        ModelCap.readers.int_of = fun _ -> FStar_Pervasives_Native.None }

let private holeDeclToModel (h: HoleDecl) : ModelCap.hole_decl =
    { ModelCap.hole_decl.h_addr = h.Addr
      ModelCap.hole_decl.h_name = h.Name
      ModelCap.hole_decl.h_kind = holeKindToModel h.Kind }

let private argToModel (a: Arg<RNode>) : ModelCap.arg<RNode> =
    match a with
    | ValueArg s -> ModelCap.ValueArg s
    | SlotArg n -> ModelCap.SlotArg n

let private argOfModel (a: ModelCap.arg<RNode>) : Arg<RNode> =
    match a with
    | ModelCap.ValueArg s -> ValueArg s
    | ModelCap.SlotArg n -> SlotArg n

/// `Reference.artw`, as the model reads it — the five functions the algebra consults, and no
/// others. The tree is the same `RNode` on both sides.
let private modelWitness: ModelCap.witness<RNode> =
    { ModelCap.witness.holes = fun n -> artw.Holes n |> List.map holeDeclToModel
      ModelCap.witness.eff = fun n -> effToModel (artw.Effect n)
      ModelCap.witness.bind_hole =
        fun addr a n ->
            match artw.Bind addr (argOfModel a) n with
            | Ok r -> ModelCap.Ok r
            | Error m -> ModelCap.Error m
      ModelCap.witness.kind_tag = artw.Tree.KindTag
      ModelCap.witness.preorder = fun n -> Tree.preorder artw.Tree n
      ModelCap.witness.children = artw.Tree.Children
      ModelCap.witness.node_id = fun n -> artw.IdW.ToString(artw.Tree.Id n) }

let private prodFaultRender (f: DeclarationFault) : string =
    match f with
    | EmptySpace(a, s) -> sprintf "EmptySpace(%s;%s)" a (modelSpaceRender (spaceToModel s))
    | NonFiniteBound a -> sprintf "NonFiniteBound(%s)" a
    | DuplicateHoleAddr a -> sprintf "DuplicateHoleAddr(%s)" a
    | HoleUnderSlot n -> sprintf "HoleUnderSlot(%s)" n

let private modelFaultRender (f: ModelCap.decl_fault) : string =
    match f with
    | ModelCap.EmptySpace(a, s) -> sprintf "EmptySpace(%s;%s)" a (modelSpaceRender s)
    | ModelCap.NonFiniteBound a -> sprintf "NonFiniteBound(%s)" a
    | ModelCap.DuplicateHoleAddr a -> sprintf "DuplicateHoleAddr(%s)" a
    | ModelCap.HoleUnderSlot n -> sprintf "HoleUnderSlot(%s)" n

let private prodApplyErrRender (e: ApplyError) : string =
    match e with
    | UnknownHoleAddr(a, d) -> sprintf "UnknownHoleAddr(%s;%s)" a (String.concat "," d)
    | ValueOutOfSpace(a, s, g) -> sprintf "ValueOutOfSpace(%s;%s;%s)" a (modelSpaceRender (spaceToModel s)) g
    | RequiredHolesUnbound xs -> sprintf "RequiredHolesUnbound(%s)" (String.concat "," xs)
    | NotASlot a -> sprintf "NotASlot(%s)" a
    | SlotKindMismatch(a, e, g) -> sprintf "SlotKindMismatch(%s;%s;%s)" a e g
    | NonTotal a -> sprintf "NonTotal(%s)" a
    | BindFailed(a, m) -> sprintf "BindFailed(%s;%s)" a m
    | SlotArgOpen(a, hs) -> sprintf "SlotArgOpen(%s;%s)" a (String.concat "," hs)
    | IllFormedResult f -> sprintf "IllFormedResult(%s)" (prodFaultRender f)

let private modelApplyErrRender (e: ModelCap.apply_error) : string =
    match e with
    | ModelCap.UnknownHoleAddr(a, d) -> sprintf "UnknownHoleAddr(%s;%s)" a (String.concat "," d)
    | ModelCap.ValueOutOfSpace(a, s, g) -> sprintf "ValueOutOfSpace(%s;%s;%s)" a (modelSpaceRender s) g
    | ModelCap.RequiredHolesUnbound xs -> sprintf "RequiredHolesUnbound(%s)" (String.concat "," xs)
    | ModelCap.NotASlot a -> sprintf "NotASlot(%s)" a
    | ModelCap.SlotKindMismatch(a, e, g) -> sprintf "SlotKindMismatch(%s;%s;%s)" a e g
    | ModelCap.NonTotal a -> sprintf "NonTotal(%s)" a
    | ModelCap.BindFailed(a, m) -> sprintf "BindFailed(%s;%s)" a m
    | ModelCap.SlotArgOpen(a, hs) -> sprintf "SlotArgOpen(%s;%s)" a (String.concat "," hs)
    | ModelCap.IllFormedResult f -> sprintf "IllFormedResult(%s)" (modelFaultRender f)

let private applyErrClass (e: ApplyError) : string =
    (prodApplyErrRender e).Substring(0, (prodApplyErrRender e).IndexOf '(')

/// `NoSuchCapability`'s `known` list is production's sorted map keys and the model's list in
/// registration order — the ONE ordering the model does not carry; both render sorted.
let private prodInvokeErrRender (e: InvokeError) : string =
    match e with
    | NoSuchCapability(id, known) -> sprintf "NoSuchCapability(%s;%s)" id (String.concat "," (List.sort known))
    | DuplicateCapability id -> sprintf "DuplicateCapability(%s)" id
    | UnknownArg(a, d) -> sprintf "UnknownArg(%s;%s)" a (String.concat "," d)
    | ArgOutOfSpace(a, s, g) -> sprintf "ArgOutOfSpace(%s;%s;%s)" a (modelSpaceRender (spaceToModel s)) g
    | RequiredArgsUnbound xs -> sprintf "RequiredArgsUnbound(%s)" (String.concat "," xs)
    | UninvocableArg a -> sprintf "UninvocableArg(%s)" a
    | BodyFailed m -> sprintf "BodyFailed(%s)" m
    | NonTotalCapability(id, xs) -> sprintf "NonTotalCapability(%s;%s)" id (String.concat "," xs)
    | IllFormedCapability(id, f) -> sprintf "IllFormedCapability(%s;%s)" id (prodFaultRender f)
    | DuplicateArg a -> sprintf "DuplicateArg(%s)" a
    // Phase 318 — the policy gate's two refusals; the model's dispatch has no gate, so they never
    // arise in the differential, which renders them only to stay total.
    | PolicyRefused(p, r, xs) -> sprintf "PolicyRefused(%s;%s;%s)" p r (String.concat "," xs)
    | ApprovalRequired p -> sprintf "ApprovalRequired(%s)" p

let private modelInvokeErrRender (e: ModelCap.invoke_error) : string =
    match e with
    | ModelCap.NoSuchCapability(id, known) -> sprintf "NoSuchCapability(%s;%s)" id (String.concat "," (List.sort known))
    | ModelCap.DuplicateCapability id -> sprintf "DuplicateCapability(%s)" id
    | ModelCap.UnknownArg(a, d) -> sprintf "UnknownArg(%s;%s)" a (String.concat "," d)
    | ModelCap.ArgOutOfSpace(a, s, g) -> sprintf "ArgOutOfSpace(%s;%s;%s)" a (modelSpaceRender s) g
    | ModelCap.RequiredArgsUnbound xs -> sprintf "RequiredArgsUnbound(%s)" (String.concat "," xs)
    | ModelCap.UninvocableArg a -> sprintf "UninvocableArg(%s)" a
    | ModelCap.NonTotalCapability(id, xs) -> sprintf "NonTotalCapability(%s;%s)" id (String.concat "," xs)
    | ModelCap.BodyFailed m -> sprintf "BodyFailed(%s)" m
    | ModelCap.IllFormedCapability(id, f) -> sprintf "IllFormedCapability(%s;%s)" id (modelFaultRender f)
    | ModelCap.DuplicateArg a -> sprintf "DuplicateArg(%s)" a

let private invokeErrClass (e: InvokeError) : string =
    (prodInvokeErrRender e).Substring(0, (prodInvokeErrRender e).IndexOf '(')

/// Phase 210 — the body answers in the `Deferred` envelope on both sides, and the two `deferred`
/// types are distinct (production's `Fuaran.Core.Deferred`, the model's extracted `deferred`), so
/// the accepted VALUE is compared by rendering exactly as the refusal already is.
let private prodDeferredRender (d: Deferred<int>) : string =
    match d with
    | Pending -> "Pending"
    | Ready v -> sprintf "Ready(%d)" v
    | Failed m -> sprintf "Failed(%s)" m

let private modelDeferredRender (d: ModelCap.deferred<int>) : string =
    match d with
    | ModelCap.Pending -> "Pending"
    | ModelCap.Ready v -> sprintf "Ready(%d)" v
    | ModelCap.Failed m -> sprintf "Failed(%s)" m

/// A value for a space — in space more often than not, and out of it (or unparseable) the rest.
let private genValueFor (s: ValueSpace) (r: ConfRng.T) : string * ConfRng.T =
    let roll, r1 = ConfRng.intBelow 10 r
    let k, r2 = ConfRng.intBelow 8 r1

    match s with
    | IntRange(lo, hi) ->
        (if roll < 6 && lo <= hi then
             string (lo + k % (hi - lo + 1))
         elif roll < 8 then
             string (hi + 1 + k)
         // Phase 307 — the spellings a culture-dependent reader got wrong (he-IL, fa-IR, sv-SE …).
         else
             List.item (k % 6) [ "x"; "-5"; "\u22125"; " 5"; "+5"; "1e999" ]),
        r2
    | FloatRange(lo, hi) ->
        (if roll < 6 then
             (lo + (hi - lo) * float (k % 4) / 4.0).ToString("R", inv)
         elif roll < 8 then
             (hi + 1.0).ToString("R", inv)
         else
             "nan?"),
        r2
    | StringLen(lo, hi) ->
        String.replicate
            (if roll < 6 && lo <= hi then
                 lo + k % (hi - lo + 1)
             else
                 max 0 (hi + 1 + k))
            "s",
        r2
    | Enum xs ->
        (if roll < 7 && not (List.isEmpty xs) then
             List.item (k % List.length xs) xs
         else
             "zz"),
        r2
    | AnyString -> sprintf "v%d" k, r2
    // Phase 229 — a tree space: a document of the constrained kind, one of another kind, or
    // something that is no tree at all (a scalar, then a kind-less object).
    | SlotTree c ->
        let kind = defaultArg c ("k" + string (k % 3))

        (if roll < 6 then sprintf """{"kind":"%s","n":%d}""" kind k
         elif roll < 8 then sprintf """{"kind":"%s-x"}""" kind
         elif roll < 9 then "s"
         else """{"n":1}"""),
        r2

/// An argument set for an artifact's holes: three draws in four bind a hole (with a value for
/// its space, or — one draw in eight — an arg of the wrong axis), and one draw in six adds an
/// arg at an address no hole declares.
let private genArgs (t: RNode) (r: ConfRng.T) : Map<string, Arg<RNode>> * ConfRng.T =
    let mutable rng = r
    let mutable args = Map.empty

    for h in artw.Holes t do
        let roll, r1 = ConfRng.intBelow 8 rng
        rng <- r1

        if roll < 6 then
            let a, r2 =
                match h.Kind with
                | ValueHole s
                | RepeatHole s ->
                    if roll = 5 then
                        SlotArg(RNode.leaf "in" "para" "z"), rng
                    else
                        let v, r' = genValueFor s rng
                        ValueArg v, r'
                | SlotHole _ ->
                    if roll = 5 then
                        ValueArg "s", rng
                    elif roll = 4 then
                        SlotArg(RNode.leaf "in" "field" "z"), rng
                    // Phase 307 — a slot argument that still has an open hole of its own.
                    elif roll = 3 then
                        SlotArg(RNode.node "in" "para" [ RNode.hole "inh" "field" "o" (ValueHole AnyString) ]), rng
                    else
                        SlotArg(RNode.leaf "in" "para" "z"), rng
                | ActionHole _ -> ValueArg "act", rng

            rng <- r2
            args <- Map.add h.Addr a args

    let extra, r3 = ConfRng.intBelow 6 rng
    rng <- r3

    if extra = 0 then
        args <- Map.add "root/zz" (ValueArg "1") args

    args, rng

type private FnTally =
    { FDiffs: string list
      Artifacts: int
      Applied: int
      ApplyRefused: int
      Curried: int
      CurryRefused: int
      Composed: int
      ComposeRefused: int
      NonTotal: int
      AuditErrors: int
      FClasses: Set<string> }

let private fnProbe
    (rd: ModelCap.readers)
    (t: RNode)
    (args: Map<string, Arg<RNode>>)
    (inner: RNode)
    (slot: string)
    (acc: FnTally)
    : FnTally =
    let mw = modelWitness
    let margs = args |> Map.toList |> List.map (fun (k, v) -> k, argToModel v)
    let diffs = System.Collections.Generic.List<string>()

    let label =
        sprintf "artifact %A args %A" (artw.Holes t |> List.map (fun h -> h.Addr, h.Kind)) (Map.keys args |> List.ofSeq)

    // signature, isTotal, signatureExcluding
    let psg = Function.signature artw "f" t
    let msg = ModelCap.signature_of mw "f" t

    if sigToModel psg <> msg then
        diffs.Add(sprintf "%s: signature differs\n  prod %A\n  model %A" label (sigToModel psg) msg)

    if Function.isTotal psg <> ModelCap.is_total msg then
        diffs.Add(
            sprintf "%s: isTotal differs (prod %b, model %b)" label (Function.isTotal psg) (ModelCap.is_total msg)
        )

    let bound = args |> Map.keys |> List.ofSeq

    if
        sigToModel (Function.signatureExcluding (Set.ofList bound) psg)
        <> ModelCap.signature_excluding bound msg
    then
        diffs.Add(sprintf "%s: signatureExcluding differs" label)

    // apply / curry — verdict, result tree, rejection class and payload
    let compare (what: string) (p: Result<RNode, ApplyError>) (m: ModelCap.outcome<RNode, ModelCap.apply_error>) =
        match p, m with
        | Ok pn, ModelCap.Ok mn ->
            if pn <> mn then
                diffs.Add(sprintf "%s: %s accepted on both sides with DIFFERENT trees" label what)
        | Error pe, ModelCap.Error me ->
            if prodApplyErrRender pe <> modelApplyErrRender me then
                diffs.Add(
                    sprintf
                        "%s: %s refused differently\n  prod %s\n  model %s"
                        label
                        what
                        (prodApplyErrRender pe)
                        (modelApplyErrRender me)
                )
        | Ok _, ModelCap.Error me ->
            diffs.Add(
                sprintf "%s: %s accepted by production, refused by the model (%s)" label what (modelApplyErrRender me)
            )
        | Error pe, ModelCap.Ok _ ->
            diffs.Add(
                sprintf "%s: %s refused by production (%s), accepted by the model" label what (prodApplyErrRender pe)
            )

    let pApply = Function.apply artw args t
    compare "apply" pApply (ModelCap.apply rd mw margs t)
    let pCurry = Function.curry artw args t
    compare "curry" pCurry (ModelCap.curry rd mw margs t)
    let pComp = Function.compose artw slot inner t
    compare "compose" pComp (ModelCap.compose rd mw slot inner t)

    // Phase 307 — the admission check over the artifact.
    match Function.validate artw t, ModelCap.validate_decl rd mw t with
    | Ok(), FStar_Pervasives_Native.None -> ()
    | Error pf, FStar_Pervasives_Native.Some mf when prodFaultRender pf = modelFaultRender mf -> ()
    | p, m -> diffs.Add(sprintf "%s: validate differs\n  prod %A\n  model %A" label p m)

    // the effect surfaces
    if
        effToModel (Function.composedEffect artw inner t)
        <> ModelCap.composed_effect mw inner t
    then
        diffs.Add(sprintf "%s: composedEffect differs" label)

    if effToModel (Function.observedEffect artw t) <> ModelCap.observed_effect mw t then
        diffs.Add(sprintf "%s: observedEffect differs" label)

    let pAudit = Function.auditEffect artw t
    let mAudit = ModelCap.audit_effect mw t

    (match pAudit, mAudit with
     | Ok(), ModelCap.Ok() -> ()
     | Error(d, a), ModelCap.Error(md, ma) ->
         if effToModel d <> md || effToModel a <> ma then
             diffs.Add(sprintf "%s: auditEffect error payload differs" label)
     | _ -> diffs.Add(sprintf "%s: auditEffect verdict differs" label))

    let cls (r: Result<RNode, ApplyError>) =
        match r with
        | Ok _ -> None
        | Error e -> Some(applyErrClass e)

    { FDiffs = acc.FDiffs @ List.ofSeq diffs
      Artifacts = acc.Artifacts + 1
      Applied = acc.Applied + (if Result.isOk pApply then 1 else 0)
      ApplyRefused = acc.ApplyRefused + (if Result.isError pApply then 1 else 0)
      Curried = acc.Curried + (if Result.isOk pCurry then 1 else 0)
      CurryRefused = acc.CurryRefused + (if Result.isError pCurry then 1 else 0)
      Composed = acc.Composed + (if Result.isOk pComp then 1 else 0)
      ComposeRefused = acc.ComposeRefused + (if Result.isError pComp then 1 else 0)
      NonTotal = acc.NonTotal + (if Function.isTotal psg then 0 else 1)
      AuditErrors = acc.AuditErrors + (if Result.isError pAudit then 1 else 0)
      FClasses =
        [ cls pApply; cls pCurry; cls pComp ]
        |> List.choose id
        |> List.fold (fun s c -> Set.add c s) acc.FClasses }

/// Run `f` under the named culture on this thread, restoring the culture after (Phase 307): the
/// readers premise is a claim about functions of the STRING alone, and he-IL is a culture whose
/// minus sign the pre-295 reader got wrong — so the differential runs there, not under whatever
/// culture the host machine happens to have.
let private underCulture (name: string) (f: unit -> 'T) : 'T =
    let saved = System.Globalization.CultureInfo.CurrentCulture

    try
        System.Globalization.CultureInfo.CurrentCulture <- System.Globalization.CultureInfo name
        f ()
    finally
        System.Globalization.CultureInfo.CurrentCulture <- saved

let private fnDifferential (rd: ModelCap.readers) (seed: int) (trials: int) : FnTally =
    let mutable rng = ConfRng.ofSeed seed

    let mutable tally =
        { FDiffs = []
          Artifacts = 0
          Applied = 0
          ApplyRefused = 0
          Curried = 0
          CurryRefused = 0
          Composed = 0
          ComposeRefused = 0
          NonTotal = 0
          AuditErrors = 0
          FClasses = Set.empty }

    for _ in 1..trials do
        let t, r1 = genArtifact rng
        let args, r2 = genArgs t r1
        let innerKind, r3 = ConfRng.choose [ "para"; "field" ] r2
        let holes = artw.Holes t
        let slotRoll, r4 = ConfRng.intBelow 4 r3

        let slots =
            holes
            |> List.filter (fun h ->
                match h.Kind with
                | SlotHole _ -> true
                | _ -> false)

        let slot =
            if slotRoll = 0 || List.isEmpty holes then
                "root/nope"
            elif slotRoll < 3 && not (List.isEmpty slots) then
                (List.item (slotRoll % List.length slots) slots).Addr
            else
                (List.item (slotRoll % List.length holes) holes).Addr

        rng <- r4
        tally <- fnProbe rd t args (RNode.leaf "in" innerKind "z") slot tally

    tally

type private CapTally =
    { CDiffs: string list
      Registered: int
      DupRefused: int
      Dispatched: int
      NoSuch: int
      Validated: int
      Refused: int
      BodyRan: int
      BodyFailed: int
      RefusedWithoutBody: int
      CClasses: Set<string> }

let private capIdPool = [ "cap-a"; "cap-b"; "cap-c" ]

/// A typed invocation: for each entry, three draws in four an arg (a value for its space — for a
/// slot entry, since Phase 229, a tree of its kind, of another kind, or no tree — or, for a
/// spaceless entry, a value it cannot take), plus one draw in five an unknown address.
let private genInvocation (sg: Signature) (r: ConfRng.T) : (string * string) list * ConfRng.T =
    let mutable rng = r
    let args = System.Collections.Generic.List<string * string>()

    for e in sg.Holes do
        let roll, r1 = ConfRng.intBelow 4 rng
        rng <- r1

        if roll < 3 then
            match e.Space with
            | Some s ->
                let v, r2 = genValueFor s rng
                rng <- r2
                args.Add(e.Addr, v)
            | None -> args.Add(e.Addr, "s")

    let extra, r3 = ConfRng.intBelow 5 rng
    rng <- r3

    if extra = 0 then
        args.Add("root/zz", "1")

    // Phase 307 — one draw in ten repeats an address the list already binds.
    let dup, r4 = ConfRng.intBelow 10 rng
    rng <- r4

    if dup = 0 && args.Count > 0 then
        args.Add(fst args[0], "again")

    List.ofSeq args, rng

let private capProbe (rd: ModelCap.readers) (seedTag: int) (acc: CapTally) (r: ConfRng.T) : CapTally * ConfRng.T =
    let diffs = System.Collections.Generic.List<string>()
    let mutable rng = r

    // build a registry on both sides, in the same order, with duplicates possible
    let n, r1 = ConfRng.intBelow 4 rng
    rng <- r1
    let mutable preg = CapabilityRegistry.empty
    let mutable mreg = ModelCap.empty
    let mutable registered = 0
    let mutable dupRefused = 0

    for i in 1..n do
        let id, r2 = ConfRng.choose capIdPool rng
        let sg, r3 = genCapSignature (sprintf "f%d" i) r2
        let placement, r4 = ConfRng.choose [ BuildTime; Server; ClientIsland Pyodide ] r3
        rng <- r4
        let cap = Capability.create id sg placement

        match CapabilityRegistry.register cap preg, ModelCap.register rd (capToModel cap) mreg with
        | Ok p', ModelCap.Ok m' ->
            preg <- p'
            mreg <- m'
            registered <- registered + 1
        | Error pe, ModelCap.Error me ->
            dupRefused <- dupRefused + 1

            if prodInvokeErrRender pe <> modelInvokeErrRender me then
                diffs.Add(
                    sprintf
                        "seed %d: register refused differently (%s vs %s)"
                        seedTag
                        (prodInvokeErrRender pe)
                        (modelInvokeErrRender me)
                )
        | _ -> diffs.Add(sprintf "seed %d: register verdict differs on %s" seedTag id)

    // enumerate + tryFind agree (membership, and the entries themselves, sorted by id)
    let penum = CapabilityRegistry.enumerate preg |> List.map capToModel
    let menum = ModelCap.enumerate mreg |> List.sortBy _.c_id

    if penum <> menum then
        diffs.Add(sprintf "seed %d: enumerate differs" seedTag)

    for id in "cap-x" :: capIdPool do
        let p = CapabilityRegistry.tryFind id preg |> Option.map capToModel
        let m = ofMOpt (ModelCap.try_find_cap id mreg)

        if p <> m then
            diffs.Add(sprintf "seed %d: tryFind %s differs" seedTag id)

    // invocations: an id from the pool or an unregistered one, args drawn against the
    // resolved capability's signature when there is one
    let mutable dispatched = 0
    let mutable noSuch = 0
    let mutable validated = 0
    let mutable refused = 0
    let mutable bodyRan = 0
    let mutable bodyFailed = 0
    let mutable refusedWithoutBody = 0
    let mutable classes = acc.CClasses
    let k, r5 = ConfRng.intBelow 4 rng
    rng <- r5

    for _ in 0..k do
        let id, r6 = ConfRng.choose ("cap-x" :: capIdPool) rng
        rng <- r6

        let args, r7 =
            match CapabilityRegistry.tryFind id preg with
            | Some c -> genInvocation c.Signature rng
            | None -> [ "h1", "1" ], rng

        // Phase 210 — the body answers in the envelope, so the draw reaches all three of its cases:
        // 0 fails (the typed `BodyFailed`), 1 is still pending, 2 and 3 settle.
        let failBody, r8 = ConfRng.intBelow 4 r7
        rng <- r8
        let pRan = ref false
        let mRan = ref false

        let pBody (_: Capability) () =
            pRan.Value <- true

            match failBody with
            | 0 -> Failed "boom"
            | 1 -> Pending
            | _ -> Ready 42

        let mBody (_: ModelCap.capability) () =
            mRan.Value <- true

            match failBody with
            | 0 -> ModelCap.Failed "boom"
            | 1 -> ModelCap.Pending
            | _ -> ModelCap.Ready 42

        let p = CapabilityRegistry.dispatch preg id args pBody
        let m = ModelCap.dispatch rd mreg id args mBody

        (match p, m with
         | Ok pv, ModelCap.Ok mv ->
             dispatched <- dispatched + 1

             if prodDeferredRender pv <> modelDeferredRender mv then
                 diffs.Add(
                     sprintf
                         "seed %d: dispatch %s accepted with different envelopes\n  prod %s\n  model %s"
                         seedTag
                         id
                         (prodDeferredRender pv)
                         (modelDeferredRender mv)
                 )

             // and neither side ever hands an `Ok(Failed _)` out of the seam
             // (`invoke_never_ok_failed` / `dispatch_never_ok_failed`, sampled here on the shipped one).
             match pv, mv with
             | Failed _, _
             | _, ModelCap.Failed _ ->
                 diffs.Add(sprintf "seed %d: dispatch %s accepted with an Ok(Failed _) envelope" seedTag id)
             | _ -> ()
         | Error pe, ModelCap.Error me ->
             classes <- Set.add (invokeErrClass pe) classes

             if prodInvokeErrRender pe <> modelInvokeErrRender me then
                 diffs.Add(
                     sprintf
                         "seed %d: dispatch %s refused differently\n  prod %s\n  model %s"
                         seedTag
                         id
                         (prodInvokeErrRender pe)
                         (modelInvokeErrRender me)
                 )

             match pe with
             | NoSuchCapability _ -> noSuch <- noSuch + 1
             | BodyFailed _ -> bodyFailed <- bodyFailed + 1
             | _ -> refused <- refused + 1
         | Ok _, ModelCap.Error me ->
             diffs.Add(
                 sprintf
                     "seed %d: dispatch %s accepted by production, refused by the model (%s)"
                     seedTag
                     id
                     (modelInvokeErrRender me)
             )
         | Error pe, ModelCap.Ok _ ->
             diffs.Add(
                 sprintf
                     "seed %d: dispatch %s refused by production (%s), accepted by the model"
                     seedTag
                     id
                     (prodInvokeErrRender pe)
             ))

        // the body ran on both sides or on neither — and never past a refusal that is not the body's
        if pRan.Value <> mRan.Value then
            diffs.Add(
                sprintf
                    "seed %d: dispatch %s ran the body on one side only (prod %b, model %b)"
                    seedTag
                    id
                    pRan.Value
                    mRan.Value
            )

        if pRan.Value then
            bodyRan <- bodyRan + 1

        (match p with
         | Error(BodyFailed _)
         | Ok _ -> ()
         | Error _ ->
             refusedWithoutBody <- refusedWithoutBody + 1

             if pRan.Value then
                 diffs.Add(sprintf "seed %d: dispatch %s was REFUSED and the body still ran" seedTag id))

        // validateArgs on its own, on the resolved capability
        match CapabilityRegistry.tryFind id preg with
        | Some c ->
            match Capability.validateArgs c args, ModelCap.validate_args rd (capToModel c) args with
            | Ok(), ModelCap.Ok() -> validated <- validated + 1
            | Error pe, ModelCap.Error me ->
                if prodInvokeErrRender pe <> modelInvokeErrRender me then
                    diffs.Add(
                        sprintf
                            "seed %d: validateArgs refused differently\n  prod %s\n  model %s"
                            seedTag
                            (prodInvokeErrRender pe)
                            (modelInvokeErrRender me)
                    )
            | _ -> diffs.Add(sprintf "seed %d: validateArgs verdict differs on %s" seedTag id)
        | None -> ()

    { CDiffs = acc.CDiffs @ List.ofSeq diffs
      Registered = acc.Registered + registered
      DupRefused = acc.DupRefused + dupRefused
      Dispatched = acc.Dispatched + dispatched
      NoSuch = acc.NoSuch + noSuch
      Validated = acc.Validated + validated
      Refused = acc.Refused + refused
      BodyRan = acc.BodyRan + bodyRan
      BodyFailed = acc.BodyFailed + bodyFailed
      RefusedWithoutBody = acc.RefusedWithoutBody + refusedWithoutBody
      CClasses = classes },
    rng

let private capDifferential (rd: ModelCap.readers) (seed: int) (trials: int) : CapTally =
    let mutable rng = ConfRng.ofSeed seed

    let mutable tally =
        { CDiffs = []
          Registered = 0
          DupRefused = 0
          Dispatched = 0
          NoSuch = 0
          Validated = 0
          Refused = 0
          BodyRan = 0
          BodyFailed = 0
          RefusedWithoutBody = 0
          CClasses = Set.empty }

    for i in 1..trials do
        let t, r' = capProbe rd i tally rng
        tally <- t
        rng <- r'

    tally


// ------------------------------------------------------------------------------------------
// Phase 354 — the three sections of `proofs/Capability.fst` Phase 307 left unmodelled, each beside
// the production function it names: the HANDLER TABLE (`Function.bindHandlers`), the CAPABILITY
// PIPELINE (`CapabilityPipeline.typeCheck` / `eval` / `dependencyMap` / `evalFrom`) and the CODECS
// (`CapabilityCodec` for a signature and a capability, the pipeline's for its nodes).
//
// The draws reach what the sections' theorems are about and the Phase 307 generator did not:
// handler sets in more than one arrival order, with a key repeated and with two undeclared keys at
// once (so the refusal ORDER is compared, not only the refusal); pipelines with their nodes
// reordered, an edge pointing forward, a cycle, an ill-typed edge and an argument bound twice; and
// documents the decoders must refuse — a member dropped, a value of the wrong kind, a tag outside
// its set, a label that disagrees with the signature it sits beside.
// ------------------------------------------------------------------------------------------

/// The ordinal order an F# `Map<string, _>` keeps its keys in — the comparator premise
/// `bind_handlers_order_independent` takes (`total_order`).
let private mapKeyLe (a: string) (b: string) : bool = System.String.CompareOrdinal(a, b) <= 0

let private prodBindErrRender (e: BindHandlerError) : string =
    match e with
    | UnknownActionAddr(a, d) -> sprintf "UnknownActionAddr(%s;%s)" a (String.concat "," d)
    | NotAnActionHole a -> sprintf "NotAnActionHole(%s)" a
    | RequiredActionsUnbound xs -> sprintf "RequiredActionsUnbound(%s)" (String.concat "," xs)
    | HandlerEffectExceedsCeiling(a, c, h) ->
        sprintf "HandlerEffectExceedsCeiling(%s;%A;%A)" a (effToModel c) (effToModel h)

let private modelBindErrRender (e: ModelCap.bind_handler_error) : string =
    match e with
    | ModelCap.UnknownActionAddr(a, d) -> sprintf "UnknownActionAddr(%s;%s)" a (String.concat "," d)
    | ModelCap.NotAnActionHole a -> sprintf "NotAnActionHole(%s)" a
    | ModelCap.RequiredActionsUnbound xs -> sprintf "RequiredActionsUnbound(%s)" (String.concat "," xs)
    | ModelCap.HandlerEffectExceedsCeiling(a, c, h) -> sprintf "HandlerEffectExceedsCeiling(%s;%A;%A)" a c h

let private bindErrClass (e: BindHandlerError) : string =
    (prodBindErrRender e).Substring(0, (prodBindErrRender e).IndexOf '(')

let private bindingToModel (hb: HandlerBinding<int>) : ModelCap.handler_binding<int> =
    { ModelCap.handler_binding.hb_handler = hb.Handler
      ModelCap.handler_binding.hb_effect = effToModel hb.Effect }

/// A handler set for an artifact, in ARRIVAL order. An action hole gets a handler inside its
/// ceiling seven draws in ten, one drawn from the pool (which may exceed it) two in ten, and none
/// otherwise; a data hole gets one a draw in ten; one set in six carries undeclared keys — two of
/// them half the time, so the first-in-key-order rule is exercised — and one in ten binds a key
/// twice, the later binding replacing the earlier.
let private genHandlers (t: RNode) (r: ConfRng.T) : (string * HandlerBinding<int>) list * ConfRng.T =
    let mutable rng = r
    let hs = System.Collections.Generic.List<string * HandlerBinding<int>>()
    let mutable n = 0

    for h in artw.Holes t do
        let roll, r1 = ConfRng.intBelow 10 rng
        let e, r2 = ConfRng.choose effPool r1
        rng <- r2
        n <- n + 1

        match h.Kind with
        | ActionHole ceiling ->
            if roll < 7 then
                let inside = if roll < 4 then Effect.pureDeterministic else ceiling
                hs.Add(h.Addr, ({ Handler = n; Effect = inside }: HandlerBinding<int>))
            elif roll < 9 then
                hs.Add(h.Addr, ({ Handler = n; Effect = e }: HandlerBinding<int>))
        | _ ->
            if roll = 0 then
                hs.Add(h.Addr, ({ Handler = n; Effect = e }: HandlerBinding<int>))

    let extra, r3 = ConfRng.intBelow 12 rng
    rng <- r3

    if extra < 2 then
        hs.Add(
            "root/zz",
            ({ Handler = 90
               Effect = Effect.pureDeterministic }
            : HandlerBinding<int>)
        )

    if extra = 0 then
        hs.Add(
            "root/aa",
            ({ Handler = 91
               Effect = Effect.pureDeterministic }
            : HandlerBinding<int>)
        )

    let dup, r4 = ConfRng.intBelow 10 rng
    rng <- r4

    if dup = 0 && hs.Count > 0 then
        hs.Add(
            fst hs[0],
            ({ Handler = 99
               Effect = Effect.pureDeterministic }
            : HandlerBinding<int>)
        )

    List.ofSeq hs, rng

type private BindTally =
    { BDiffs: string list
      BAccepted: int
      BAcceptedBound: int
      BRefused: int
      BReordered: int
      BReplaced: int
      BClasses: Set<string> }

let private bindCompare
    (le: string -> string -> bool)
    (seedTag: int)
    (t: RNode)
    (arriving: (string * HandlerBinding<int>) list)
    (diffs: System.Collections.Generic.List<string>)
    : Result<HandlerTable<int>, BindHandlerError> =
    let p = Function.bindHandlers artw (Map.ofList arriving) t

    let m =
        ModelCap.bind_handlers_of le modelWitness (arriving |> List.map (fun (a, hb) -> a, bindingToModel hb)) t

    (match p, m with
     | Ok pt, ModelCap.Ok mt ->
         let pl = Map.toList pt.Handlers |> List.map (fun (a, hb) -> a, bindingToModel hb)

         if pl <> mt.ht_handlers then
             diffs.Add(sprintf "seed %d: bindHandlers accepted with different tables" seedTag)
     | Error pe, ModelCap.Error me ->
         if prodBindErrRender pe <> modelBindErrRender me then
             diffs.Add(
                 sprintf
                     "seed %d: bindHandlers refused differently\n  prod %s\n  model %s"
                     seedTag
                     (prodBindErrRender pe)
                     (modelBindErrRender me)
             )
     | Ok _, ModelCap.Error me ->
         diffs.Add(
             sprintf
                 "seed %d: bindHandlers accepted by production, refused by the model (%s)"
                 seedTag
                 (modelBindErrRender me)
         )
     | Error pe, ModelCap.Ok _ ->
         diffs.Add(
             sprintf
                 "seed %d: bindHandlers refused by production (%s), accepted by the model"
                 seedTag
                 (prodBindErrRender pe)
         ))

    p

let private bindDifferential (le: string -> string -> bool) (seed: int) (trials: int) : BindTally =
    let mutable rng = ConfRng.ofSeed seed
    let diffs = System.Collections.Generic.List<string>()
    let mutable accepted = 0
    let mutable acceptedBound = 0
    let mutable refused = 0
    let mutable reordered = 0
    let mutable replaced = 0
    let mutable classes = Set.empty

    for i in 1..trials do
        let t, r1 = genArtifact rng
        let arriving, r2 = genHandlers t r1
        rng <- r2
        let p = bindCompare le i t arriving diffs

        (match p with
         | Ok pt ->
             accepted <- accepted + 1

             if not (Map.isEmpty pt.Handlers) then
                 acceptedBound <- acceptedBound + 1
         | Error pe ->
             refused <- refused + 1
             classes <- Set.add (bindErrClass pe) classes)

        let keys = arriving |> List.map fst

        if List.distinct keys = keys then
            // `bind_handlers_order_independent`, on the shipped function: the same distinct
            // bindings in another arrival order bind identically — the table or the refusal.
            if List.length arriving > 1 then
                reordered <- reordered + 1
                let again = bindCompare le i t (List.rev arriving) diffs

                if again <> p then
                    diffs.Add(sprintf "seed %d: bindHandlers depends on the order the handlers arrive in" i)
        else
            replaced <- replaced + 1

    { BDiffs = List.ofSeq diffs
      BAccepted = accepted
      BAcceptedBound = acceptedBound
      BRefused = refused
      BReordered = reordered
      BReplaced = replaced
      BClasses = classes }

// ---- the pipeline ----

/// The two float comparisons `Space.subsumes` makes, exactly as production writes them.
let private feedReaders: ModelCap.feed_readers =
    let f (s: string) = System.Double.Parse(s, inv)

    { ModelCap.feed_readers.float_within = fun rl rh al ah -> f rl <= f al && f ah <= f rh
      ModelCap.feed_readers.int_within = fun rl rh al ah -> f rl <= float (int al) && float (int ah) <= f rh }

let private argSrcToModel (s: ArgSource) : ModelCap.arg_source =
    match s with
    | ArgSource.Literal v -> ModelCap.Literal v
    | ArgSource.FromNode n -> ModelCap.FromNode n

let private nodeToModel (n: PipelineNode) : ModelCap.pipeline_node =
    match n with
    | PipelineNode.Source(id, dref, ty) -> ModelCap.Source(id, dref, spaceToModel ty)
    | PipelineNode.Invoke(id, capId, ty, args) ->
        ModelCap.Invoke(id, capId, spaceToModel ty, args |> List.map (fun (a, s) -> a, argSrcToModel s))

let private pipeToModel (p: CapabilityPipeline) : ModelCap.pipeline =
    { ModelCap.pipeline.p_nodes = p.Nodes |> List.map nodeToModel }

let private outputTypeOf (n: PipelineNode) : ValueSpace =
    match n with
    | PipelineNode.Source(_, _, ty) -> ty
    | PipelineNode.Invoke(_, _, ty, _) -> ty

let private upstreamsOf (n: PipelineNode) : string list =
    match n with
    | PipelineNode.Source _ -> []
    | PipelineNode.Invoke(_, _, _, args) ->
        args
        |> List.choose (fun (_, s) ->
            match s with
            | ArgSource.FromNode up -> Some up
            | ArgSource.Literal _ -> None)

/// Declaration order is a topological order of the edges — `ordered [] p.p_nodes`, computed here
/// from production's nodes and from nothing the model wrote.
let private isTopological (nodes: PipelineNode list) : bool =
    let rec go (earlier: Set<string>) =
        function
        | [] -> true
        | n :: rest ->
            let nid = CapabilityPipeline.nodeId n

            not (earlier.Contains nid)
            && List.forall earlier.Contains (upstreamsOf n)
            && go (earlier.Add nid) rest

    go Set.empty nodes

let private prodPipeErrRender (e: PipelineError) : string =
    match e with
    | PipelineError.DuplicateNode id -> sprintf "DuplicateNode(%s)" id
    | PipelineError.UnknownNode id -> sprintf "UnknownNode(%s)" id
    | PipelineError.PipelineNoSuchCapability(id, known) ->
        sprintf "PipelineNoSuchCapability(%s;%s)" id (String.concat "," (List.sort known))
    | PipelineError.PipelineArgRefused(n, r) -> sprintf "PipelineArgRefused(%s;%s)" n (prodInvokeErrRender r)
    | PipelineError.PipelineCycle(n, c) -> sprintf "PipelineCycle(%s;%s)" n (String.concat ">" c)
    | PipelineError.EdgeTypeMismatch(n, a, p, c) -> sprintf "EdgeTypeMismatch(%s;%s;%s;%s)" n a p c
    | PipelineError.PipelineForwardEdge(n, a, u) -> sprintf "PipelineForwardEdge(%s;%s;%s)" n a u

let private modelPipeErrRender (e: ModelCap.pipeline_error) : string =
    match e with
    | ModelCap.DuplicateNode id -> sprintf "DuplicateNode(%s)" id
    | ModelCap.UnknownNode id -> sprintf "UnknownNode(%s)" id
    | ModelCap.PipelineNoSuchCapability(id, known) ->
        sprintf "PipelineNoSuchCapability(%s;%s)" id (String.concat "," (List.sort known))
    | ModelCap.PipelineArgRefused(n, r) -> sprintf "PipelineArgRefused(%s;%s)" n (modelInvokeErrRender r)
    | ModelCap.PipelineCycle(n, c) -> sprintf "PipelineCycle(%s;%s)" n (String.concat ">" c)
    | ModelCap.EdgeTypeMismatch(n, a, p, c) -> sprintf "EdgeTypeMismatch(%s;%s;%s;%s)" n a p c
    | ModelCap.PipelineForwardEdge(n, a, u) -> sprintf "PipelineForwardEdge(%s;%s;%s)" n a u

let private pipeErrClass (e: PipelineError) : string =
    (prodPipeErrRender e).Substring(0, (prodPipeErrRender e).IndexOf '(')

let private prodEvalErrRender (e: PipelineEvalError) : string =
    match e with
    | PipelineEvalError.EvalIllTyped r -> sprintf "EvalIllTyped(%s)" (prodPipeErrRender r)
    | PipelineEvalError.EvalNodeFailed(n, m) -> sprintf "EvalNodeFailed(%s;%s)" n m
    | PipelineEvalError.EvalArgRefused(n, r) -> sprintf "EvalArgRefused(%s;%s)" n (prodInvokeErrRender r)
    // Phase 383 — the policy admission the model does not carry; the oracle's lookups run no gate,
    // so it is never answered here, and a render that reached it would differ from every model render.
    | PipelineEvalError.EvalPolicyRefused(n, r) -> sprintf "EvalPolicyRefused(%s;%s)" n (prodInvokeErrRender r)

let private modelEvalErrRender (e: ModelCap.pipeline_eval_error) : string =
    match e with
    | ModelCap.EvalIllTyped r -> sprintf "EvalIllTyped(%s)" (modelPipeErrRender r)
    | ModelCap.EvalNodeFailed(n, m) -> sprintf "EvalNodeFailed(%s;%s)" n m
    | ModelCap.EvalArgRefused(n, r) -> sprintf "EvalArgRefused(%s;%s)" n (modelInvokeErrRender r)

/// An evaluation's answer, rendered: the result map by id, or the refusal.
let private prodEvalRender (r: Result<Map<string, string>, PipelineEvalError>) : string =
    match r with
    | Ok m ->
        "Ok "
        + (Map.toList m |> List.map (fun (k, v) -> k + "=" + v) |> String.concat ";")
    | Error e -> "Error " + prodEvalErrRender e

/// The model's result map is a list read latest-first (`Map.add` is the cons), so it is folded
/// oldest-first into a map before it is rendered.
let private modelEvalRender (r: ModelCap.outcome<(string * string) list, ModelCap.pipeline_eval_error>) : string =
    match r with
    | ModelCap.Ok l ->
        "Ok "
        + (Map.ofList (List.rev l)
           |> Map.toList
           |> List.map (fun (k, v) -> k + "=" + v)
           |> String.concat ";")
    | ModelCap.Error e -> "Error " + modelEvalErrRender e

/// A value that lies in a well-formed space.
let private validValueFor (s: ValueSpace) : string =
    match s with
    | IntRange(lo, _) -> lo.ToString(inv)
    | FloatRange(lo, _) -> lo.ToString("R", inv)
    | StringLen(lo, _) -> String.replicate (max lo 0) "s"
    | Enum(x :: _) -> x
    | Enum [] -> "zz"
    | AnyString -> "v"
    | SlotTree c -> sprintf """{"kind":"%s"}""" (defaultArg c "k0")

/// A registry on both sides holding up to the three pool ids, each tried up to three times so
/// most registries hold all of them (the admission gate refuses a good share of the draws).
let private genRegistryPair (rd: ModelCap.readers) (r: ConfRng.T) : CapabilityRegistry * ModelCap.registry * ConfRng.T =
    let mutable rng = r
    let mutable preg = CapabilityRegistry.empty
    let mutable mreg = ModelCap.empty

    for id in capIdPool do
        let mutable placed = false

        for k in 1..3 do
            if not placed then
                let sg, r1 = genCapSignature (id + string k) rng
                rng <- r1
                let cap = Capability.create id sg Server

                match CapabilityRegistry.register cap preg, ModelCap.register rd (capToModel cap) mreg with
                | Ok p', ModelCap.Ok m' ->
                    preg <- p'
                    mreg <- m'
                    placed <- true
                | _ -> ()

    preg, mreg, rng

let private withArgs (n: PipelineNode) (f: (string * ArgSource) list -> (string * ArgSource) list) : PipelineNode =
    match n with
    | PipelineNode.Invoke(id, capId, ty, args) -> PipelineNode.Invoke(id, capId, ty, f args)
    | other -> other

let private isInvokeWithArgs (n: PipelineNode) : bool =
    match n with
    | PipelineNode.Invoke(_, _, _, _ :: _) -> true
    | _ -> false

/// Rewrite the first node `pick` holds of.
let private rewriteFirst (pick: PipelineNode -> bool) (f: PipelineNode -> PipelineNode) (ns: PipelineNode list) =
    match List.tryFindIndex pick ns with
    | Some i -> ns |> List.mapi (fun k n -> if k = i then f n else n)
    | None -> ns

/// A pipeline over a registry. Each `Invoke` binds every required hole of a registered
/// capability: a literal in its space, a fresh `Source` whose output IS the hole's space (declared
/// first), an earlier node whatever it outputs (so an edge may be ill-typed), or a literal drawn
/// against the space (so it may lie outside it). Then, two pipelines in five, ONE fault: a repeated
/// node id, the first node moved last (a forward edge), a self-edge, a cycle through two nodes, an
/// edge to no node, an argument at no hole, an argument bound twice, an argument dropped, or the
/// whole declaration reversed.
let private genPipeline (preg: CapabilityRegistry) (r: ConfRng.T) : CapabilityPipeline * ConfRng.T =
    let mutable rng = r
    let nodes = System.Collections.Generic.List<PipelineNode>()
    let registered = CapabilityRegistry.enumerate preg
    let count, r0 = ConfRng.intBelow 4 rng
    rng <- r0
    let mutable fresh = 0

    for i in 1..count do
        let pick, r1 = ConfRng.intBelow 12 rng
        rng <- r1
        let nid = sprintf "n%d" i

        if List.isEmpty registered || pick = 0 then
            nodes.Add(PipelineNode.Invoke(nid, "cap-x", AnyString, []))
        else
            let cap, r2 = ConfRng.choose registered rng
            let outTy, r3 = genSpace r2
            rng <- r3
            let args = System.Collections.Generic.List<string * ArgSource>()

            for e in cap.Signature.Holes do
                match e.Space with
                | Some sp when e.Required ->
                    let how, r4 = ConfRng.intBelow 10 rng
                    rng <- r4

                    if how < 4 then
                        args.Add(e.Addr, ArgSource.Literal(validValueFor sp))
                    elif how < 7 then
                        fresh <- fresh + 1
                        let sid = sprintf "s%d" fresh
                        nodes.Add(PipelineNode.Source(sid, "ref-" + sid, sp))
                        args.Add(e.Addr, ArgSource.FromNode sid)
                    elif how < 9 && nodes.Count > 0 then
                        let k, r5 = ConfRng.intBelow nodes.Count rng
                        rng <- r5
                        args.Add(e.Addr, ArgSource.FromNode(CapabilityPipeline.nodeId nodes[k]))
                    else
                        let v, r5 = genValueFor sp rng
                        rng <- r5
                        args.Add(e.Addr, ArgSource.Literal v)
                | _ -> ()

            nodes.Add(PipelineNode.Invoke(nid, cap.Id, outTy, List.ofSeq args))

    let fault, rf = ConfRng.intBelow 22 rng
    rng <- rf
    let ns = List.ofSeq nodes

    let faulted =
        match fault, ns with
        | 0, first :: _ -> ns @ [ PipelineNode.Source(CapabilityPipeline.nodeId first, "dup", AnyString) ]
        | 1, first :: rest -> rest @ [ first ]
        | 2, _ ->
            ns
            |> rewriteFirst isInvokeWithArgs (fun n ->
                withArgs n (fun args ->
                    match args with
                    | (a, _) :: tl -> (a, ArgSource.FromNode(CapabilityPipeline.nodeId n)) :: tl
                    | [] -> []))
        | 3, _ ->
            // a cycle through two nodes: the first `Invoke` with arguments gains an edge to a
            // LATER node that already reaches it.
            (match List.tryFindIndex isInvokeWithArgs ns with
             | Some i ->
                 let aId = CapabilityPipeline.nodeId ns[i]

                 (match
                     ns
                     |> List.indexed
                     |> List.tryFind (fun (k, n) -> k > i && List.contains aId (upstreamsOf n))
                  with
                  | Some(_, b) ->
                      ns
                      |> List.mapi (fun k n ->
                          if k = i then
                              withArgs n (fun args ->
                                  match args with
                                  | (a, _) :: tl -> (a, ArgSource.FromNode(CapabilityPipeline.nodeId b)) :: tl
                                  | [] -> [])
                          else
                              n)
                  | None -> ns)
             | None -> ns)
        | 4, _ ->
            ns
            |> rewriteFirst isInvokeWithArgs (fun n ->
                withArgs n (fun args ->
                    match args with
                    | (a, _) :: tl -> (a, ArgSource.FromNode "n9") :: tl
                    | [] -> []))
        | 5, _ ->
            ns
            |> rewriteFirst isInvokeWithArgs (fun n ->
                withArgs n (fun args -> args @ [ "root/zz", ArgSource.Literal "1" ]))
        | 6, _ ->
            ns
            |> rewriteFirst isInvokeWithArgs (fun n ->
                withArgs n (fun args ->
                    match args with
                    | first :: _ -> args @ [ first ]
                    | [] -> []))
        | 7, _ -> ns |> rewriteFirst isInvokeWithArgs (fun n -> withArgs n List.tail)
        | 8, _ -> List.rev ns
        | _ -> ns

    ({ Nodes = faulted }: CapabilityPipeline), rng

type private PipeTally =
    { PpDiffs: string list
      PpAccepted: int
      PpRefused: int
      PpEvaluated: int
      PpEvalRefused: int
      PpIncremental: int
      PpReused: int
      PpReordered: int
      PpReorderedTopological: int
      PpStaleLost: int
      PpSubsumes: int
      PpClasses: Set<string> }

let private pipeDifferential (rd: ModelCap.readers) (seed: int) (trials: int) : PipeTally =
    let mutable rng = ConfRng.ofSeed seed
    let diffs = System.Collections.Generic.List<string>()
    let mutable accepted = 0
    let mutable refused = 0
    let mutable evaluated = 0
    let mutable evalRefused = 0
    let mutable incremental = 0
    let mutable reused = 0
    let mutable reordered = 0
    let mutable reorderedTopological = 0
    let mutable staleLost = 0
    let mutable subsumesCompared = 0
    let mutable classes = Set.empty

    for i in 1..trials do
        // THE SPACE RELATION, on its own: production's `Space.subsumes` against the model's over a
        // drawn pair, so an edge's verdict below is not the only place the relation is compared.
        let sa, ra = genSpace rng
        let sb, rb = genSpace ra
        rng <- rb
        subsumesCompared <- subsumesCompared + 1

        if
            Space.subsumes sa sb
            <> ModelCap.subsumes feedReaders rd (spaceToModel sa) (spaceToModel sb)
        then
            diffs.Add(sprintf "seed %d: subsumes differs on %A against %A" i sa sb)

        let preg, mreg, r1 = genRegistryPair rd rng
        let p, r2 = genPipeline preg r1
        rng <- r2
        let lookup = CapabilityLookup.ofRegistry preg
        let mlookup = ModelCap.lookup_of_registry mreg
        let mp = pipeToModel p

        // ---- typeCheck ----
        let pt = CapabilityPipeline.typeCheck lookup p
        let mt = ModelCap.type_check feedReaders rd mlookup mp

        (match pt, mt with
         | Ok(), ModelCap.Ok() -> accepted <- accepted + 1
         | Error pe, ModelCap.Error me ->
             refused <- refused + 1
             classes <- Set.add (pipeErrClass pe) classes

             if prodPipeErrRender pe <> modelPipeErrRender me then
                 diffs.Add(
                     sprintf
                         "seed %d: typeCheck refused differently\n  prod %s\n  model %s"
                         i
                         (prodPipeErrRender pe)
                         (modelPipeErrRender me)
                 )
         | Ok(), ModelCap.Error me ->
             diffs.Add(
                 sprintf
                     "seed %d: typeCheck accepted by production, refused by the model (%s)"
                     i
                     (modelPipeErrRender me)
             )
         | Error pe, ModelCap.Ok() ->
             diffs.Add(
                 sprintf "seed %d: typeCheck refused by production (%s), accepted by the model" i (prodPipeErrRender pe)
             ))

        // `typecheck_topological`, on the shipped function: an accepted pipeline is in topological
        // order, and another declaration of the same nodes is accepted exactly when it is too.
        if pt = Ok() then
            if not (isTopological p.Nodes) then
                diffs.Add(sprintf "seed %d: typeCheck accepted a declaration that is not in topological order" i)

            if List.length p.Nodes > 1 then
                reordered <- reordered + 1
                let k, rk = ConfRng.intBelow (List.length p.Nodes) rng
                rng <- rk
                let rotated = List.skip k p.Nodes @ List.take k p.Nodes

                let p': CapabilityPipeline =
                    { Nodes = (if k = 0 then List.rev rotated else rotated) }

                let pt' = CapabilityPipeline.typeCheck lookup p'
                let mt' = ModelCap.type_check feedReaders rd mlookup (pipeToModel p')

                if isTopological p'.Nodes then
                    reorderedTopological <- reorderedTopological + 1

                    if pt' <> Ok() then
                        diffs.Add(
                            sprintf
                                "seed %d: typeCheck's verdict moved under another topological order of the same nodes"
                                i
                        )
                else
                    match pt' with
                    | Error(PipelineError.PipelineForwardEdge _) -> ()
                    | other ->
                        diffs.Add(
                            sprintf
                                "seed %d: a reordering out of topological order was not refused as a forward edge (%A)"
                                i
                                other
                        )

                match pt', mt' with
                | Ok(), ModelCap.Ok() -> ()
                | Error pe, ModelCap.Error me when prodPipeErrRender pe = modelPipeErrRender me -> ()
                | _ -> diffs.Add(sprintf "seed %d: typeCheck disagrees on the reordered declaration" i)

        // ---- eval ----
        // The host body is a table: each node's answer drawn once — a value for its output space
        // (in it four draws in five), or a failure one draw in twelve — and a log of what it was
        // handed, so the RESOLVED ARGUMENTS are compared and not only the answers.
        let table = System.Collections.Generic.Dictionary<string, Result<string, string>>()

        for n in p.Nodes do
            let roll, ra1 = ConfRng.intBelow 12 rng
            let v, ra2 = genValueFor (outputTypeOf n) ra1
            rng <- ra2

            table[CapabilityPipeline.nodeId n] <-
                (if roll = 0 then Error "boom"
                 elif roll < 10 then Ok(validValueFor (outputTypeOf n))
                 else Ok v)

        let renderArgs (render: 'a -> string) (args: (string * 'a) list) =
            args |> List.map (fun (a, x) -> a + "=" + render x) |> String.concat ","

        let pArg (a: PipelineArg<string>) =
            match a with
            | FromUpstream v -> "U:" + v
            | LiteralArg s -> "L:" + s

        let mArg (a: ModelCap.pipeline_arg<string>) =
            match a with
            | ModelCap.FromUpstream v -> "U:" + v
            | ModelCap.LiteralArg s -> "L:" + s

        let pBodyOf
            (tbl: System.Collections.Generic.Dictionary<string, Result<string, string>>)
            (log: ResizeArray<string>)
            =
            fun (n: PipelineNode) (args: (string * PipelineArg<string>) list) ->
                log.Add(CapabilityPipeline.nodeId n + "(" + renderArgs pArg args + ")")
                tbl[CapabilityPipeline.nodeId n]

        let mBodyOf
            (tbl: System.Collections.Generic.Dictionary<string, Result<string, string>>)
            (log: ResizeArray<string>)
            =
            fun (n: ModelCap.pipeline_node) (args: (string * ModelCap.pipeline_arg<string>) list) ->
                log.Add(ModelCap.node_id n + "(" + renderArgs mArg args + ")")

                match tbl[ModelCap.node_id n] with
                | Ok v -> ModelCap.Ok v
                | Error m -> ModelCap.Error m

        let pLog = ResizeArray<string>()
        let mLog = ResizeArray<string>()
        let pe0 = CapabilityPipeline.eval lookup id (pBodyOf table pLog) p
        let me0 = ModelCap.eval feedReaders rd mlookup id (mBodyOf table mLog) mp

        if prodEvalRender pe0 <> modelEvalRender me0 then
            diffs.Add(
                sprintf "seed %d: eval differs\n  prod %s\n  model %s" i (prodEvalRender pe0) (modelEvalRender me0)
            )

        if List.ofSeq pLog <> List.ofSeq mLog then
            diffs.Add(sprintf "seed %d: eval ran the body on different nodes or arguments" i)

        (match pt, pe0 with
         | Error e, Error(PipelineEvalError.EvalIllTyped e') when e = e' ->
             // `pipeline_refused_never_evaluated`: the refusal is the type check's, and no body ran.
             if pLog.Count > 0 then
                 diffs.Add(sprintf "seed %d: eval ran a body on a pipeline typeCheck refused" i)
         | Error _, _ -> diffs.Add(sprintf "seed %d: eval did not answer the type check's refusal" i)
         | Ok(), Error(PipelineEvalError.EvalIllTyped _) ->
             // `pipeline_illtyped_iff_refused`: never, over an accepted pipeline.
             diffs.Add(
                 sprintf "seed %d: eval answered PipelineEvalError.EvalIllTyped over a pipeline typeCheck ACCEPTED" i
             )
         | Ok(), Ok _ -> evaluated <- evaluated + 1
         | Ok(), Error _ -> evalRefused <- evalRefused + 1)

        // ---- dependencyMap, the dirty set and evalFrom ----
        // Phase 544: the model's dirty set is Propagation's closure instantiated at `deps_of`, and
        // production's is Propagation's closure over `dependencyMap`, so the differential compares
        // the INSTANTIATION (the edge relation each map names, which is what `pipeline_edge_iff`
        // and the two closure theorems are stated over) and then the closure over it — model
        // against production on both, never production against itself.
        let pDeps = CapabilityPipeline.dependencyMap p
        let mDeps = ModelCap.deps_of mp.p_nodes

        let pEdges =
            pDeps
            |> Map.toSeq
            |> Seq.collect (fun (n, rs) -> rs |> Seq.map (fun r -> n, r))
            |> Set.ofSeq

        let mEdges =
            mDeps |> Seq.collect (fun (n, rs) -> rs |> Seq.map (fun r -> n, r)) |> Set.ofSeq

        if pEdges <> mEdges then
            diffs.Add(sprintf "seed %d: dependencyMap's edges differ (%A vs %A)" i pEdges mEdges)

        let nodeIds = p.Nodes |> List.map CapabilityPipeline.nodeId
        let mutable changed = []

        for nid in nodeIds do
            let c, rc = ConfRng.intBelow 4 rng
            rng <- rc

            if c = 0 then
                changed <- nid :: changed

        let stray, rs = ConfRng.intBelow 8 rng
        rng <- rs

        if stray = 0 then
            changed <- "n9" :: changed

        let changedSet = Set.ofList changed
        let changedList = Set.toList changedSet
        let pDirty = Propagation.dirtyFromChangedIds pDeps changedSet
        let mDirty = Set.ofList (ModelProp.dirty_from_changed_ids mDeps changedList)

        if pDirty <> mDirty then
            diffs.Add(sprintf "seed %d: the dirty set differs (%A vs %A)" i pDirty mDirty)

        // The new body: the old one, re-drawn at exactly the changed nodes — `agree_off`.
        let table1 =
            System.Collections.Generic.Dictionary<string, Result<string, string>>(table)

        for n in p.Nodes do
            let nid = CapabilityPipeline.nodeId n

            if changedSet.Contains nid then
                let roll, rb1 = ConfRng.intBelow 12 rng
                rng <- rb1

                table1[nid] <-
                    (if roll = 0 then
                         Error "boom2"
                     else
                         Ok(validValueFor (outputTypeOf n)))

        // A prior: the first evaluation's result where it finished, a partial map otherwise (the
        // model agrees on any prior; the theorem is about the first kind).
        let prior =
            match pe0 with
            | Ok m -> m
            | Error _ -> nodeIds |> List.truncate 1 |> List.map (fun nid -> nid, "stale") |> Map.ofList

        let pLog1 = ResizeArray<string>()
        let mLog1 = ResizeArray<string>()

        let pf =
            CapabilityPipeline.evalFrom lookup id (pBodyOf table1 pLog1) prior changedSet p

        let mf =
            ModelCap.eval_from feedReaders rd mlookup id (mBodyOf table1 mLog1) (Map.toList prior) changedList mp

        if prodEvalRender pf <> modelEvalRender mf then
            diffs.Add(
                sprintf "seed %d: evalFrom differs\n  prod %s\n  model %s" i (prodEvalRender pf) (modelEvalRender mf)
            )

        if List.ofSeq pLog1 <> List.ofSeq mLog1 then
            diffs.Add(sprintf "seed %d: evalFrom ran the body on different nodes or arguments" i)

        match pe0 with
        | Ok _ ->
            // `pipeline_evalfrom_agrees`, on the shipped functions: over a finished prior
            // evaluation and a body that moved only at the changed nodes, the incremental answer
            // IS the full one.
            incremental <- incremental + 1

            let full =
                CapabilityPipeline.eval lookup id (pBodyOf table1 (ResizeArray<string>())) p

            if pf <> full then
                diffs.Add(
                    sprintf
                        "seed %d: evalFrom is not eval over the same inputs\n  evalFrom %s\n  eval     %s"
                        i
                        (prodEvalRender pf)
                        (prodEvalRender full)
                )

            reused <- reused + (List.length nodeIds - pLog1.Count |> max 0)

            // THE HYPOTHESIS IS NEEDED: a body that also moves at a node the caller did NOT name
            // is a stale reuse. Counted, not asserted per probe — the unnamed node must be one
            // `evalFrom` reuses for the two to part.
            (match nodeIds |> List.tryFind (fun nid -> not (pDirty.Contains nid)) with
             | Some unnamed ->
                 let table2 =
                     System.Collections.Generic.Dictionary<string, Result<string, string>>(table1)

                 table2[unnamed] <- Ok "moved-unnamed"

                 let stale =
                     CapabilityPipeline.evalFrom lookup id (pBodyOf table2 (ResizeArray<string>())) prior changedSet p

                 let fresh =
                     CapabilityPipeline.eval lookup id (pBodyOf table2 (ResizeArray<string>())) p

                 if stale <> fresh then
                     staleLost <- staleLost + 1
             | None -> ())
        | Error _ -> ()

    { PpDiffs = List.ofSeq diffs
      PpAccepted = accepted
      PpRefused = refused
      PpEvaluated = evaluated
      PpEvalRefused = evalRefused
      PpIncremental = incremental
      PpReused = reused
      PpReordered = reordered
      PpReorderedTopological = reorderedTopological
      PpStaleLost = staleLost
      PpSubsumes = subsumesCompared
      PpClasses = classes }

// ---- the codecs ----

/// The one float read the codecs make — `float i`, as `Decoder.float` reads a `JInt`.
let private codecReaders: ModelCap.codec_readers =
    { ModelCap.codec_readers.float_of_int = fun i -> (float (int i)).ToString("R", inv) }

/// The go-red: a `float_fault` that finds nothing, so a float range with a NaN, an infinite or an
/// inverted bound is well-formed to the model and refused by production's readers.
let private blindFaultReaders: ModelCap.readers =
    { readers with
        ModelCap.readers.float_fault = fun _ _ -> FStar_Pervasives_Native.None }

let private prodDecodeErrRender (e: DecodeError) : string =
    sprintf
        "%A@%s"
        e.Code
        (e.Path
         |> List.map (function
             | PathSegment.Key k -> "k:" + k
             | PathSegment.Index n -> "i:" + string n)
         |> String.concat "/")

let private modelDecodeErrRender (e: ModelCap.decode_error) : string =
    sprintf
        "%A@%s"
        e.d_code
        (e.d_path
         |> List.map (function
             | ModelCap.Key k -> "k:" + k
             | ModelCap.Index n -> "i:" + string n)
         |> String.concat "/")

type private CodecTally =
    { CoDiffs: string list
      CoEncoded: int
      CoDocuments: int
      CoAccepted: int
      CoRefused: int
      CoWellFormed: int
      CoPayloadRefused: int
      CoBytes: int
      CoSignatures: int
      CoNodes: int
      CoNodeDocuments: int
      CoCodes: Set<string> }

let private codecDifferential (rd: ModelCap.readers) (seed: int) (trials: int) : CodecTally =
    let mutable rng = ConfRng.ofSeed seed
    let diffs = System.Collections.Generic.List<string>()
    let mutable encoded = 0
    let mutable documents = 0
    let mutable accepted = 0
    let mutable refused = 0
    let mutable wellFormed = 0
    let mutable payloadRefused = 0
    let mutable bytes = 0
    let mutable signatures = 0
    let mutable nodesCompared = 0
    let mutable nodeDocuments = 0
    let mutable codes = Set.empty

    let placements =
        [ BuildTime
          Server
          ClientDeclarative
          Precomputed
          ClientIsland Pyodide
          ClientIsland Fable
          ClientIsland Js ]

    for i in 1..trials do
        // ---- a capability: a derived signature. Since Phase 409 an entry IS its hole kind, so the
        //      two shapes only a hand-built entry had (a spaceless slot, a kind that is no hole kind's)
        //      cannot be built; the reader meets their documents below, as targeted mutations ----
        let sg, r1 = genCapSignature (sprintf "c%d" i) rng
        let placement, r2 = ConfRng.choose placements r1
        rng <- r2

        let cap = Capability.create (sprintf "cap-%d" i) sg placement
        let mcap = capToModel cap
        let j0 = CapabilityCodec.encodeJson cap
        encoded <- encoded + 1

        if jvalToModel j0 <> ModelCap.capability_json mcap then
            diffs.Add(sprintf "seed %d: the capability encodes differently" i)

        // `capability_roundtrip_identity` and its converse, on the shipped codec: decode after
        // encode is the identity EXACTLY on the well-formed capabilities.
        let wf = ModelCap.wf_capability rd mcap
        let back = CapabilityCodec.decodeJsonDetailedWith ReadPolicy.Lenient j0

        if wf then
            wellFormed <- wellFormed + 1

        (match back with
         | Ok c when c = cap ->
             if not wf then
                 diffs.Add(sprintf "seed %d: a capability that is not well-formed read back as itself" i)
         | Ok _ -> diffs.Add(sprintf "seed %d: a capability read back as another (Phase 409: no normal form)" i)
         | Error e ->
             if wf then
                 diffs.Add(sprintf "seed %d: a well-formed capability was refused (%s)" i (prodDecodeErrRender e)))

        // ... and through the BYTES, where the document can be written: `Canon.render` then the
        // parser, which is where a whole float comes back as an integer token.
        (match CapabilityCodec.tryEncode cap with
         | Ok s ->
             bytes <- bytes + 1

             match CapabilityCodec.decode s with
             | Ok c when c = cap ->
                 if not wf then
                     diffs.Add(sprintf "seed %d: through the bytes, an ill-formed capability read back as itself" i)
             | Ok _
             | Error _ ->
                 if wf then
                     diffs.Add(sprintf "seed %d: through the bytes, a well-formed capability did not read back" i)
         | Error _ -> ())

        // ---- the reader, over the encoding, three mutations of it, and the targeted ones: each
        //      entry's kind payload removed (a value or repeat entry without its `space`, an action
        //      entry without its `actionEffect`, a slot without its `slotKind`), and its tag replaced
        //      by one outside `HoleKind.tags` ----
        let mutable docs = [ j0 ]

        for _ in 1..3 do
            let m, rm = jmutate j0 rng
            rng <- rm
            docs <- m :: docs

        let withEntry (k: int) (f: (string * JVal) list -> (string * JVal) list) (j: JVal) : JVal =
            let onHoles =
                List.map (fun (name, v) ->
                    match name, v with
                    | "holes", JArr es ->
                        name,
                        JArr(
                            es
                            |> List.mapi (fun n e ->
                                match e with
                                | JObj fs when n = k -> JObj(f fs)
                                | other -> other)
                        )
                    | _ -> name, v)

            match j with
            | JObj fs ->
                JObj(
                    fs
                    |> List.map (fun (name, v) ->
                        match name, v with
                        | "signature", JObj sfs -> name, JObj(onHoles sfs)
                        | _ -> name, v)
                )
            | other -> other

        let payload = set [ "space"; "actionEffect"; "slotKind" ]

        for k in 0 .. List.length sg.Holes - 1 do
            docs <-
                withEntry k (List.filter (fun (name, _) -> not (payload.Contains name))) j0
                :: docs

            docs <-
                withEntry k (List.map (fun (name, v) -> if name = "kind" then name, JStr "int" else name, v)) j0
                :: docs

        for j in docs do
            documents <- documents + 1
            let p = CapabilityCodec.decodeJsonDetailedWith ReadPolicy.Lenient j
            let m = ModelCap.capability_of_j rd codecReaders (jvalToModel j)

            (match p, m with
             | Ok pc, ModelCap.Ok mc ->
                 accepted <- accepted + 1

                 if capToModel pc <> mc then
                     diffs.Add(sprintf "seed %d: a capability document decoded to different values" i)

                 // `capability_decoded_wf`: whatever was accepted is well-formed.
                 if not (ModelCap.wf_capability rd (capToModel pc)) then
                     diffs.Add(sprintf "seed %d: the reader accepted a capability that is not well-formed" i)
             | Error pe, ModelCap.Error me ->
                 refused <- refused + 1
                 codes <- Set.add (sprintf "%A" pe.Code) codes

                 match pe.Code, List.tryLast pe.Path with
                 | DecodeCode.MissingField, Some(PathSegment.Key("space" | "actionEffect")) ->
                     payloadRefused <- payloadRefused + 1
                 | _ -> ()

                 if prodDecodeErrRender pe <> modelDecodeErrRender me then
                     diffs.Add(
                         sprintf
                             "seed %d: a capability document was refused differently\n  prod %s\n  model %s"
                             i
                             (prodDecodeErrRender pe)
                             (modelDecodeErrRender me)
                     )
             | Ok _, ModelCap.Error me ->
                 diffs.Add(
                     sprintf
                         "seed %d: a capability document accepted by production, refused by the model (%s)"
                         i
                         (modelDecodeErrRender me)
                 )
             | Error pe, ModelCap.Ok _ ->
                 diffs.Add(
                     sprintf
                         "seed %d: a capability document refused by production (%s), accepted by the model"
                         i
                         (prodDecodeErrRender pe)
                 ))

            // the signature reader on its own, where the document still carries one
            match Decoder.tryMember "signature" j with
            | Some sj ->
                signatures <- signatures + 1

                match CapabilityCodec.signatureOf sj, ModelCap.signature_of_j rd codecReaders (jvalToModel sj) with
                | Ok ps, ModelCap.Ok ms ->
                    if sigToModel ps <> ms then
                        diffs.Add(sprintf "seed %d: a signature document decoded to different values" i)
                | Error _, ModelCap.Error _ -> ()
                | _ -> diffs.Add(sprintf "seed %d: the signature reader's verdict differs" i)
            | None -> ()

        // ---- a pipeline: its encoding byte for byte, and its reader over the parsed document ----
        let preg, _, r4 = genRegistryPair rd rng
        let pl, r5 = genPipeline preg r4
        rng <- r5
        let mpl = pipeToModel pl
        nodesCompared <- nodesCompared + List.length pl.Nodes

        if
            CapabilityPipeline.encode pl
            <> Canon.render (jvalOfModel (ModelCap.pipeline_json mpl))
        then
            diffs.Add(sprintf "seed %d: the pipeline encodes differently" i)

        match CapabilityPipeline.tryEncode pl with
        | Error _ -> ()
        | Ok s ->
            // `node_roundtrip`, on the shipped codec: the identity exactly where every node's
            // output space is well-formed.
            let allWf =
                pl.Nodes |> List.forall (fun n -> Space.wellFormed (outputTypeOf n) = Ok())

            (match CapabilityPipeline.decodeDetailed s with
             | Ok back when back = pl ->
                 if not allWf then
                     diffs.Add(sprintf "seed %d: a pipeline with an ill-formed output space read back as itself" i)
             | Ok _ -> diffs.Add(sprintf "seed %d: a pipeline read back as another" i)
             | Error e ->
                 if allWf then
                     diffs.Add(sprintf "seed %d: a well-formed pipeline was refused (%s)" i (prodDecodeErrRender e)))

            match Decoder.parse ReadOptions.strict s with
            | Error _ -> diffs.Add(sprintf "seed %d: the pipeline's own encoding did not parse" i)
            | Ok parsed ->
                let mutable pdocs = [ parsed ]

                for _ in 1..3 do
                    let m, rm = jmutate parsed rng
                    rng <- rm
                    pdocs <- m :: pdocs

                for j in pdocs do
                    nodeDocuments <- nodeDocuments + 1
                    let text = Canon.render j

                    match Decoder.parse ReadOptions.strict text with
                    | Error _ -> diffs.Add(sprintf "seed %d: a mutated pipeline document did not parse" i)
                    | Ok reparsed ->
                        match
                            CapabilityPipeline.decodeDetailed text,
                            ModelCap.pipeline_of_j rd codecReaders (jvalToModel reparsed)
                        with
                        | Ok pp, ModelCap.Ok mpp ->
                            if pipeToModel pp <> mpp then
                                diffs.Add(sprintf "seed %d: a pipeline document decoded to different values" i)
                        | Error pe, ModelCap.Error me ->
                            codes <- Set.add (sprintf "%A" pe.Code) codes

                            if prodDecodeErrRender pe <> modelDecodeErrRender me then
                                diffs.Add(
                                    sprintf
                                        "seed %d: a pipeline document was refused differently\n  prod %s\n  model %s"
                                        i
                                        (prodDecodeErrRender pe)
                                        (modelDecodeErrRender me)
                                )
                        | _ -> diffs.Add(sprintf "seed %d: the pipeline reader's verdict differs" i)

    { CoDiffs = List.ofSeq diffs
      CoEncoded = encoded
      CoDocuments = documents
      CoAccepted = accepted
      CoRefused = refused
      CoWellFormed = wellFormed
      CoPayloadRefused = payloadRefused
      CoBytes = bytes
      CoSignatures = signatures
      CoNodes = nodesCompared
      CoNodeDocuments = nodeDocuments
      CoCodes = codes }

// ---- Phase 455 — the seam codec, whole: the invocation, the async envelope and the typed refusal ----

let private modelCodeOf (c: ModelCap.decode_code) : DecodeCode =
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

let private faultToModel (f: DeclarationFault) : ModelCap.decl_fault =
    match f with
    | EmptySpace(a, s) -> ModelCap.EmptySpace(a, spaceToModel s)
    | NonFiniteBound a -> ModelCap.NonFiniteBound a
    | DuplicateHoleAddr a -> ModelCap.DuplicateHoleAddr a
    | HoleUnderSlot n -> ModelCap.HoleUnderSlot n

/// Production's twelve-case `InvokeError` as the model's `gated_error`.
let private invokeErrorToModel (e: InvokeError) : ModelCap.gated_error =
    match e with
    | NoSuchCapability(id, known) -> ModelCap.GRefused(ModelCap.NoSuchCapability(id, known))
    | DuplicateCapability id -> ModelCap.GRefused(ModelCap.DuplicateCapability id)
    | UnknownArg(a, d) -> ModelCap.GRefused(ModelCap.UnknownArg(a, d))
    | ArgOutOfSpace(a, s, g) -> ModelCap.GRefused(ModelCap.ArgOutOfSpace(a, spaceToModel s, g))
    | RequiredArgsUnbound xs -> ModelCap.GRefused(ModelCap.RequiredArgsUnbound xs)
    | UninvocableArg a -> ModelCap.GRefused(ModelCap.UninvocableArg a)
    | BodyFailed r -> ModelCap.GRefused(ModelCap.BodyFailed r)
    | NonTotalCapability(id, xs) -> ModelCap.GRefused(ModelCap.NonTotalCapability(id, xs))
    | IllFormedCapability(id, f) -> ModelCap.GRefused(ModelCap.IllFormedCapability(id, faultToModel f))
    | DuplicateArg a -> ModelCap.GRefused(ModelCap.DuplicateArg a)
    | PolicyRefused(p, r, a) -> ModelCap.GPolicyRefused(p, r, a)
    | ApprovalRequired p -> ModelCap.GApprovalRequired p

let private seamNames = [ "a"; "b"; "c"; "p" ]

let private genNames (r: ConfRng.T) : string list * ConfRng.T =
    let n, r1 = ConfRng.intBelow 3 r
    let mutable rr = r1

    let xs =
        [ for _ in 1..n do
              let x, r2 = ConfRng.choose seamNames rr
              rr <- r2
              x ]

    xs, rr

let private genInvokeError (r: ConfRng.T) : InvokeError * ConfRng.T =
    let k, r1 = ConfRng.intBelow 16 r
    let s, r2 = ConfRng.choose seamNames r1
    let xs, r3 = genNames r2
    let sp, r4 = genSpace r3

    let e =
        match k with
        | 0 -> NoSuchCapability(s, xs)
        | 1 -> DuplicateCapability s
        | 2 -> UnknownArg(s, xs)
        | 3 -> ArgOutOfSpace(s, sp, "got")
        | 4 -> RequiredArgsUnbound xs
        | 5 -> UninvocableArg s
        | 6 -> BodyFailed "boom"
        | 7 -> NonTotalCapability(s, xs)
        | 8 -> IllFormedCapability(s, EmptySpace(s, sp))
        | 9 -> IllFormedCapability(s, NonFiniteBound s)
        | 10 -> IllFormedCapability(s, DuplicateHoleAddr s)
        | 11 -> IllFormedCapability(s, HoleUnderSlot s)
        | 12 -> DuplicateArg s
        | 13 -> PolicyRefused(s, "no", xs)
        | _ -> ApprovalRequired s

    e, r4

type private SeamCodecTally =
    { ScDiffs: string list
      ScDocuments: int
      ScInvocationRefused: int
      ScRepeatRefused: int
      ScErrorsEncoded: int
      ScErrorsRefused: int
      ScDeferred: int
      ScDeferredRefused: int
      ScCodes: Set<string> }

/// Runs the model's invocation, envelope and refusal codecs beside production's: the encoding, the
/// reading of each encoding and of mutations of it (acceptance, the value, and — where production
/// answers a typed refusal — its code and path), and, on production, that every typed refusal
/// RESOLVES in the document it read (`DecodeError.resolvesIn`, the theorem's statement). The model's
/// invocation writer is a parameter so a go-red can hand it a writer that drops an argument.
let private seamCodecDifferential
    (invocationJson: string -> (string * string) list -> ModelCap.jval)
    (seed: int)
    (trials: int)
    : SeamCodecTally =
    let rng = ref (ConfRng.ofSeed seed)
    let diffs = System.Collections.Generic.List<string>()
    let mutable documents = 0
    let mutable invRefused = 0
    let mutable repeatRefused = 0
    let mutable errorsEncoded = 0
    let mutable errorsRefused = 0
    let mutable deferred = 0
    let mutable deferredRefused = 0
    let mutable codes = Set.empty

    let mutations (j: JVal) =
        let mutable out = [ j ]

        for _ in 1..4 do
            let m, r = jmutate j rng.Value
            rng.Value <- r
            out <- m :: out

        out

    for i in 1..trials do
        // ---- the invocation: addresses drawn WITH replacement, so a repeat arises ----
        let cid, r1 = ConfRng.choose [ "cap-a"; "cap-b" ] rng.Value
        let n, r2 = ConfRng.intBelow 4 r1
        rng.Value <- r2

        let args =
            [ for k in 1..n do
                  let a, r = ConfRng.choose seamNames rng.Value
                  rng.Value <- r
                  a, sprintf "v%d" k ]

        let text = CapabilityCodec.encodeInvocation cid args

        match Json.parse text with
        | Error e -> diffs.Add(sprintf "seed %d: the invocation did not parse: %A" i e)
        | Ok j0 ->
            // The encoding is compared as the canonical TEXT both write: `encodeInvocation` renders it.
            if Canon.render (jvalOfModel (invocationJson cid args)) <> text then
                diffs.Add(sprintf "seed %d: the invocation encodes differently: %s" i text)

            for jm in mutations j0 do
                documents <- documents + 1
                // The document production reads is the text the mutation renders to, so the model
                // reads exactly that document too.
                let j =
                    match Json.parse (Canon.render jm) with
                    | Ok v -> v
                    | Error _ -> jm

                let p =
                    CapabilityCodec.decodeInvocationDetailedWith ReadPolicy.Lenient (Canon.render j)

                let m = ModelCap.invocation_of_j (jvalToModel j)

                match p, m with
                | Ok(pc, pa), ModelCap.Ok(mc, ma) when pc = mc && pa = ma -> ()
                | Error pe, ModelCap.Error me when prodDecodeErrRender pe = modelDecodeErrRender me ->
                    invRefused <- invRefused + 1
                    codes <- codes.Add(string pe.Code)

                    if jm = j0 then
                        repeatRefused <- repeatRefused + 1

                    if not (DecodeError.resolvesIn j pe) then
                        diffs.Add(
                            sprintf
                                "seed %d: an invocation refusal does not resolve: %s in %s"
                                i
                                (prodDecodeErrRender pe)
                                (Canon.render j)
                        )
                | _ ->
                    diffs.Add(sprintf "seed %d: the invocation readers disagree on %s: %A vs %A" i (Canon.render j) p m)

        // ---- the typed refusal: every case, a value space and a declaration fault inside ----
        let e, r3 = genInvokeError rng.Value
        rng.Value <- r3
        let ej = CapabilityCodec.invokeErrorJson e
        errorsEncoded <- errorsEncoded + 1

        if jvalToModel ej <> ModelCap.invoke_error_json (invokeErrorToModel e) then
            diffs.Add(sprintf "seed %d: the invoke error encodes differently: %A" i e)

        for j in mutations ej do
            documents <- documents + 1

            match CapabilityCodec.invokeErrorOf j, ModelCap.invoke_error_of_j codecReaders (jvalToModel j) with
            | Ok pe, ModelCap.Ok me when invokeErrorToModel pe = me -> ()
            | Error _, ModelCap.Error me ->
                errorsRefused <- errorsRefused + 1
                codes <- codes.Add(string (modelCodeOf me.d_code))
            | p, m ->
                diffs.Add(sprintf "seed %d: the invoke-error readers disagree on %s: %A vs %A" i (Canon.render j) p m)

        // ---- the async envelope, over a string payload ----
        let k, r4 = ConfRng.intBelow 3 rng.Value
        rng.Value <- r4

        let d =
            match k with
            | 0 -> Pending
            | 1 -> Ready(sprintf "x%d" i)
            | _ -> Failed "late"

        let md =
            match d with
            | Pending -> ModelCap.Pending
            | Ready v -> ModelCap.Ready v
            | Failed m -> ModelCap.Failed m

        let dj = CapabilityCodec.deferredJson JStr d
        deferred <- deferred + 1

        let mEnc (s: string) = ModelCap.JStr s

        if jvalToModel dj <> ModelCap.deferred_json mEnc md then
            diffs.Add(sprintf "seed %d: the envelope encodes differently: %A" i d)

        let pDec (v: JVal) : Result<string, string> =
            match v with
            | JStr s -> Ok s
            | _ -> Error "not a string"

        let mDec (v: ModelCap.jval) : ModelCap.outcome<string, string> =
            match v with
            | ModelCap.JStr s -> ModelCap.Ok s
            | _ -> ModelCap.Error "not a string"

        for j in mutations dj do
            documents <- documents + 1

            match CapabilityCodec.deferredOf pDec j, ModelCap.deferred_of_j mDec (jvalToModel j) with
            | Ok Pending, ModelCap.Ok ModelCap.Pending -> ()
            | Ok(Ready a), ModelCap.Ok(ModelCap.Ready b) when a = b -> ()
            | Ok(Failed a), ModelCap.Ok(ModelCap.Failed b) when a = b -> ()
            | Error _, ModelCap.Error me ->
                deferredRefused <- deferredRefused + 1
                codes <- codes.Add(string (modelCodeOf me.d_code))
            | p, m -> diffs.Add(sprintf "seed %d: the envelope readers disagree on %s: %A vs %A" i (Canon.render j) p m)

    { ScDiffs = List.ofSeq diffs
      ScDocuments = documents
      ScInvocationRefused = invRefused
      ScRepeatRefused = repeatRefused
      ScErrorsEncoded = errorsEncoded
      ScErrorsRefused = errorsRefused
      ScDeferred = deferred
      ScDeferredRefused = deferredRefused
      ScCodes = codes }

/// The go-red's writer: the model's invocation writer handed an argument list with its last
/// argument DROPPED.
let private droppingInvocationJson (cid: string) (args: (string * string) list) : ModelCap.jval =
    let kept =
        match List.rev args with
        | _ :: rest -> List.rev rest
        | [] -> []

    ModelCap.invocation_json cid kept


[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- Phase 177 — the FUNCTION SEAM: the effect lattice, the function algebra and the
          //      capability registry against the model ----

          testCase
              "the function oracle agrees with Function.signature, apply, curry, compose and auditEffect over generated artifacts and argument sets"
          <| fun _ ->
              let t = underCulture "he-IL" (fun () -> fnDifferential readers 1770 200)

              match t.FDiffs with
              | d :: _ -> failtestf "the function oracle and production DISAGREE\n%s" d
              | [] ->
                  // Adequacy, per shape. Measured at 200 artifacts: applied 73, applyRefused 127,
                  // curried 96, curryRefused 104, composed 17, composeRefused 183, nonTotal 11,
                  // auditErrors 92. Each threshold sits below its measurement with room; they
                  // catch a generator that stops reaching a shape, not pin the numbers.
                  Expect.equal t.Artifacts 200 "every artifact was compared"
                  Expect.isGreaterThan t.Applied 40 (sprintf "full applications were accepted (applied=%d)" t.Applied)

                  Expect.isGreaterThan
                      t.ApplyRefused
                      80
                      (sprintf "full applications were refused (applyRefused=%d)" t.ApplyRefused)

                  Expect.isGreaterThan
                      t.Curried
                      60
                      (sprintf "partial applications were accepted (curried=%d)" t.Curried)

                  Expect.isGreaterThan
                      t.CurryRefused
                      60
                      (sprintf "partial applications were refused (curryRefused=%d)" t.CurryRefused)

                  Expect.isGreaterThan t.Composed 8 (sprintf "compositions were accepted (composed=%d)" t.Composed)

                  Expect.isGreaterThan
                      t.ComposeRefused
                      100
                      (sprintf "compositions were refused (composeRefused=%d)" t.ComposeRefused)

                  Expect.isGreaterThan
                      t.NonTotal
                      5
                      (sprintf "non-total artifacts were reached (nonTotal=%d)" t.NonTotal)

                  Expect.isGreaterThan
                      t.AuditErrors
                      50
                      (sprintf "under-declared roots were reached (auditErrors=%d)" t.AuditErrors)

                  for cls in
                      [ "UnknownHoleAddr"
                        "ValueOutOfSpace"
                        "RequiredHolesUnbound"
                        "NotASlot"
                        "SlotKindMismatch"
                        "NonTotal" ] do
                      Expect.isTrue
                          (Set.contains cls t.FClasses)
                          (sprintf "the sample reached a %s refusal (reached: %A)" cls t.FClasses)

                  // seeded, replayable
                  Expect.equal (fnDifferential readers 1770 200) t "same seed => identical tally"

          testCase
              "the capability oracle's determinism label and effect lattice are production's over every determinism set and a pool of labels"
          <| fun _ ->
              let diffs = ResizeArray<string>()

              for a in allDeterminismSets do
                  let tag = Effect.determinismTag a

                  if tag <> ModelCap.determinism_tag (detToModel a) then
                      diffs.Add(sprintf "determinismTag differs on %A: production %s" a tag)

                  if Effect.tryDeterminismOfTag tag <> Some a then
                      diffs.Add(sprintf "production's tryDeterminismOfTag does not invert its own label %s" tag)

                  if ModelCap.det_of_tag tag <> toMOpt (Some(detToModel a)) then
                      diffs.Add(sprintf "the model's det_of_tag does not invert the label %s" tag)

                  for b in allDeterminismSets do
                      let ea = { Host = Pure; Determinism = a }
                      let eb = { Host = ReadsHost; Determinism = b }

                      if effToModel (Effect.join ea eb) <> ModelCap.join (effToModel ea) (effToModel eb) then
                          diffs.Add(sprintf "join differs on %A and %A" a b)

                      if Effect.covers ea eb <> ModelCap.covers (effToModel ea) (effToModel eb) then
                          diffs.Add(sprintf "covers differs on %A over %A" a b)

                      if Effect.covers eb ea <> ModelCap.covers (effToModel eb) (effToModel ea) then
                          diffs.Add(sprintf "covers differs on %A over %A" b a)

              // The pool: every canonical label, every reordering and repetition of factors, the
              // empty member, the spellings a reader might guess — production and model refuse the
              // same strings and accept the same ones.
              let pool =
                  (allDeterminismSets |> List.map Effect.determinismTag)
                  @ [ "random+clock"
                      "network+random"
                      "network+clock+random"
                      "clock+clock"
                      "deterministic+clock"
                      "clock+deterministic"
                      ""
                      "+"
                      "clock+"
                      "+clock"
                      "Clock"
                      "clock random"
                      "clock,random"
                      "wall" ]

              for s in pool do
                  let prod = Effect.tryDeterminismOfTag s |> Option.map detToModel |> toMOpt

                  if prod <> ModelCap.det_of_tag s then
                      diffs.Add(
                          sprintf "the readings of %A differ: production %A, model %A" s prod (ModelCap.det_of_tag s)
                      )

              // Teeth: a model that read the label through the chain this replaced (the highest
              // factor names the class) would collapse every multi-factor set onto one label, and
              // the comparison above would name it.
              let collapsed (d: DeterminismSource) =
                  if Set.contains NetworkFactor d then "network"
                  elif Set.contains RandomFactor d then "random"
                  elif Set.contains ClockFactor d then "clock"
                  else "deterministic"

              let collapseWitnesses =
                  allDeterminismSets
                  |> List.filter (fun d -> collapsed d <> Effect.determinismTag d)

              Expect.equal
                  (List.length collapseWitnesses)
                  4
                  "a chain-shaped label disagrees with the set label on exactly the four multi-factor sets"

              match List.ofSeq diffs with
              | d :: _ -> failtestf "the capability oracle and production DISAGREE on the determinism set\n%s" d
              | [] -> ()

          testCase
              "the capability oracle agrees with CapabilityRegistry.register, enumerate, tryFind and dispatch, and Capability.validateArgs, over generated registries and invocations"
          <| fun _ ->
              let t = underCulture "he-IL" (fun () -> capDifferential readers 1771 300)

              match t.CDiffs with
              | d :: _ -> failtestf "the capability oracle and production DISAGREE\n%s" d
              | [] ->
                  // Measured at 150 registries: registered 172, dupRefused 51, dispatched 33,
                  // noSuch 270, validated 45, refused 71, bodyFailed 12, refusedWithoutBody 341.
                  // Phase 307 doubled the draw to 300: the gate now refuses a repeat over anything
                  // but a capped integer range, and the draw reaches ill-formed declarations and
                  // repeated arguments, so fewer registrations reach a body per registry built.
                  Expect.isGreaterThan
                      t.Registered
                      100
                      (sprintf "capabilities were registered (registered=%d)" t.Registered)

                  Expect.isGreaterThan
                      t.DupRefused
                      20
                      (sprintf "duplicate registrations were refused (dupRefused=%d)" t.DupRefused)

                  Expect.isGreaterThan
                      t.Dispatched
                      15
                      (sprintf "invocations were dispatched and ran (dispatched=%d)" t.Dispatched)

                  Expect.isGreaterThan t.NoSuch 150 (sprintf "unregistered ids were refused (noSuch=%d)" t.NoSuch)
                  Expect.isGreaterThan t.Validated 20 (sprintf "argument sets were accepted (validated=%d)" t.Validated)
                  Expect.isGreaterThan t.Refused 40 (sprintf "argument sets were refused (refused=%d)" t.Refused)

                  Expect.isGreaterThan
                      t.BodyFailed
                      5
                      (sprintf "bodies failed and were named (bodyFailed=%d)" t.BodyFailed)

                  Expect.equal
                      t.BodyRan
                      (t.Dispatched + t.BodyFailed)
                      "the body ran exactly on the invocations validation passed"

                  Expect.isGreaterThan
                      t.RefusedWithoutBody
                      200
                      (sprintf
                          "refusals were checked for a body that did not run (refusedWithoutBody=%d)"
                          t.RefusedWithoutBody)

                  for cls in
                      [ "NoSuchCapability"
                        "UnknownArg"
                        "ArgOutOfSpace"
                        "RequiredArgsUnbound"
                        "UninvocableArg"
                        "BodyFailed" ] do
                      Expect.isTrue
                          (Set.contains cls t.CClasses)
                          (sprintf "the sample reached a %s refusal (reached: %A)" cls t.CClasses)

                  Expect.equal
                      (underCulture "he-IL" (fun () -> capDifferential readers 1771 300))
                      t
                      "same seed => identical tally"

          testCase
              "a capability oracle handed a BLIND int reader DISAGREES with Capability.validateArgs and Function.apply — the measurement can fail"
          <| fun _ ->
              // The teeth. Under the blind reader every int-ranged value is out of space to the
              // model, so it refuses what production accepts. If this ever passes, the space
              // check has stopped reaching the comparison and the green runs above certify
              // nothing about it.
              let c = capDifferential blindReaders 1771 60
              Expect.isNonEmpty c.CDiffs "a blind int reader MUST disagree with production on the seam"

              Expect.isTrue
                  (c.CDiffs |> List.exists (fun d -> d.Contains "ArgOutOfSpace"))
                  "and the disagreement is about the SPACE check, which is what the reader blinded"

              let f = fnDifferential blindReaders 1770 60
              Expect.isNonEmpty f.FDiffs "a blind int reader MUST disagree with production on the algebra"

              Expect.isTrue
                  (f.FDiffs |> List.exists (fun d -> d.Contains "ValueOutOfSpace"))
                  "and the disagreement is about the value-space check"

          testCase
              "no handler runs on a refused dispatch — `unregistered_refused` and `validate_before_invoke`, on the shipped seam"
          <| fun _ ->
              // The two theorems' statements instantiated on production: an unregistered id and
              // a rejected argument set each return the typed refusal with the body untouched,
              // and the result is the same under a body that would have failed.
              // The template's slot hole is dropped from this signature so the refusal sets stay
              // scalar; the slot-bearing capability is exercised below, where Phase 229 CLOSED the
              // Phase 177 finding (`slot_hole_invocable_in_space`).
              let full =
                  { Function.signature artw "f" (template ()) with
                      Effect = Effect.pureDeterministic }

              let sg =
                  { full with
                      Holes =
                          full.Holes
                          |> List.filter (fun e ->
                              match e.Kind with
                              | SlotHole _ -> false
                              | _ -> true) }

              let cap = Capability.create "cap-t" sg Server

              let reg =
                  match CapabilityRegistry.register cap CapabilityRegistry.empty with
                  | Ok r -> r
                  | Error e -> failtestf "register refused: %A" e

              let ran = ref 0

              let body (_: Capability) () =
                  ran.Value <- ran.Value + 1
                  Ready "ran"

              let failing (_: Capability) () =
                  ran.Value <- ran.Value + 1
                  Failed "boom"

              let pending (_: Capability) () =
                  ran.Value <- ran.Value + 1
                  Pending

              // unregistered_refused
              Expect.equal
                  (CapabilityRegistry.dispatch reg "cap-u" [ "tpl/t", "x" ] body)
                  (Error(NoSuchCapability("cap-u", [ "cap-t" ])))
                  "an unregistered id is the typed refusal, naming what IS registered"

              Expect.equal
                  (CapabilityRegistry.dispatch reg "cap-u" [ "tpl/t", "x" ] failing)
                  (Error(NoSuchCapability("cap-u", [ "cap-t" ])))
                  "and the same refusal under a failing body"

              // validate_before_invoke — out of space, unknown, slot-targeted, required-unbound
              for args in
                  [ [ "tpl/t", "x"; "tpl/c", "99" ]
                    [ "tpl/t", "x"; "tpl/c", "1"; "tpl/zz", "1" ]
                    [ "tpl/t", "x"; "tpl/c", "1"; "tpl/s", "para" ]
                    [ "tpl/t", "x" ] ] do
                  let a = CapabilityRegistry.dispatch reg "cap-t" args body
                  let b = CapabilityRegistry.dispatch reg "cap-t" args failing
                  Expect.isTrue (Result.isError a) (sprintf "the set %A is refused" args)
                  Expect.equal a b "and the refusal is the same under a failing body"

                  Expect.equal
                      a
                      (Capability.validateArgs cap args |> Result.map (fun () -> Ready "unreachable"))
                      "and it IS the validation's refusal"

              Expect.equal ran.Value 0 "no body ran on any refusal"

              // CLOSED (Phase 229; was Phase 177's `slot_hole_uninvocable`): the capability declared
              // over the WHOLE template — its slot hole required, and spaced `SlotTree (Some "para")`
              // — is registered and enumerated, refuses a non-conforming slot argument by name with
              // the body never running, and DISPATCHES a conforming one (asserted after the counts
              // below): `slot_hole_invocable_in_space`, `slot_wrong_kind_refused` and
              // `slot_scalar_uninvocable`, on the shipped seam.
              let slotCap = Capability.create "cap-slot" full Server

              let reg2 =
                  match CapabilityRegistry.register slotCap reg with
                  | Ok r -> r
                  | Error e -> failtestf "register refused: %A" e

              Expect.equal
                  (CapabilityRegistry.enumerate reg2 |> List.map _.Id)
                  [ "cap-slot"; "cap-t" ]
                  "the slot-bearing capability enumerates like any other"

              for args, expected in
                  [ [], RequiredArgsUnbound [ "tpl/t"; "tpl/c"; "tpl/s" ]
                    [ "tpl/t", "x"; "tpl/c", "3" ], RequiredArgsUnbound [ "tpl/s" ]
                    [ "tpl/t", "x"; "tpl/c", "3"; "tpl/s", "para" ], UninvocableArg "tpl/s"
                    [ "tpl/t", "x"; "tpl/c", "3"; "tpl/s", """{"kind":"field"}""" ],
                    ArgOutOfSpace("tpl/s", SlotTree(Some "para"), """{"kind":"field"}""")
                    [ "tpl/s", "" ], UninvocableArg "tpl/s" ] do
                  Expect.equal
                      (CapabilityRegistry.dispatch reg2 "cap-slot" args body)
                      (Error expected)
                      (sprintf "the slot-bearing capability refuses %A by name" args)

              Expect.equal ran.Value 0 "and no body ran on any of those either"

              // and the accepted set runs it, once
              let accepted = [ "tpl/t", "x"; "tpl/c", "3" ]

              Expect.equal
                  (CapabilityRegistry.dispatch reg "cap-t" accepted body)
                  (Ok(Ready "ran"))
                  "an accepted set runs the body"

              Expect.equal ran.Value 1 "exactly once"

              // Phase 210 — invoke_never_ok_failed / dispatch_never_ok_failed on the shipped seam:
              // past an accepted validation the envelope's three cases go to exactly the three
              // outcomes, and `Ok(Failed _)` is not among them.
              Expect.equal
                  (CapabilityRegistry.dispatch reg "cap-t" accepted pending)
                  (Ok Pending: Result<Deferred<string>, InvokeError>)
                  "a pending body stays pending inside the Ok"

              Expect.equal
                  (CapabilityRegistry.dispatch reg "cap-t" accepted failing)
                  (Error(BodyFailed "boom"): Result<Deferred<string>, InvokeError>)
                  "a failing body is the typed BodyFailed, never Ok(Failed _)"

              Expect.equal ran.Value 3 "and each of those ran its body once"

              // ... and the slot-bearing capability DISPATCHES a conforming slot argument.
              Expect.equal
                  (CapabilityRegistry.dispatch reg2 "cap-slot" (accepted @ [ "tpl/s", """{"kind":"para"}""" ]) body)
                  (Ok(Ready "ran"))
                  "a capability over a slotted artifact is invocable"

              Expect.equal ran.Value 4 "and it ran its body once"

              // and the model says the same through the same theorems' clauses
              let mreg =
                  match ModelCap.register readers (capToModel cap) ModelCap.empty with
                  | ModelCap.Ok r -> r
                  | ModelCap.Error _ -> failtest "the model refused the registration"

              Expect.equal
                  (ModelCap.dispatch readers mreg "cap-u" [ "tpl/t", "x" ] (fun _ () -> ModelCap.Ready "ran"))
                  (ModelCap.Error(ModelCap.NoSuchCapability("cap-u", [ "cap-t" ])))
                  "the model refuses the unregistered id the same way"

              Expect.equal (ModelCap.ids (ModelCap.enumerate mreg)) [ "cap-t" ] "and enumerates the registered one"

          // ---- Phase 354 — the handler table, the capability pipeline and the codecs against
          //      sections 14-16 of proofs/Capability.fst ----

          testCase
              "the handler-table oracle agrees with Function.bindHandlers over generated artifacts and handler sets, in more than one arrival order"
          <| fun _ ->
              let t = bindDifferential mapKeyLe 3541 600

              match t.BDiffs with
              | d :: _ -> failtestf "the handler-table oracle and production DISAGREE\n%s" d
              | [] ->
                  // Adequacy. Measured at 600 handler sets: accepted 421 (95 of them binding a
                  // handler), refused 179, bound again in a second arrival order 80, a repeated key
                  // 23. Each threshold sits below its measurement with room.
                  Expect.isGreaterThan t.BAccepted 250 (sprintf "handler sets were accepted (accepted=%d)" t.BAccepted)

                  Expect.isGreaterThan
                      t.BAcceptedBound
                      40
                      (sprintf "accepted tables bound a handler (acceptedBound=%d)" t.BAcceptedBound)

                  Expect.isGreaterThan t.BRefused 100 (sprintf "handler sets were refused (refused=%d)" t.BRefused)

                  Expect.isGreaterThan
                      t.BReordered
                      40
                      (sprintf "handler sets were bound in a second arrival order (reordered=%d)" t.BReordered)

                  Expect.isGreaterThan
                      t.BReplaced
                      8
                      (sprintf
                          "a repeated key was reached, the later binding replacing the earlier (replaced=%d)"
                          t.BReplaced)

                  for cls in
                      [ "UnknownActionAddr"
                        "NotAnActionHole"
                        "RequiredActionsUnbound"
                        "HandlerEffectExceedsCeiling" ] do
                      Expect.isTrue
                          (Set.contains cls t.BClasses)
                          (sprintf "the sample reached a %s refusal (reached: %A)" cls t.BClasses)

                  Expect.equal (bindDifferential mapKeyLe 3541 600) t "same seed => identical tally"

          testCase
              "a handler-table oracle handed a comparator that is NOT the map's key order DISAGREES with Function.bindHandlers — the measurement can fail"
          <| fun _ ->
              // The teeth, and the comparator premise made visible. `bind_handlers_exact` names the
              // FIRST offending key in key order, and the table is the map's key-ordered list; under
              // the reversed order the model names another key and lists another table. If this ever
              // passes, the refusal order has stopped reaching the comparison.
              let reversed (a: string) (b: string) = System.String.CompareOrdinal(a, b) >= 0
              let t = bindDifferential reversed 3541 600
              Expect.isNonEmpty t.BDiffs "a reversed key order MUST disagree with production"

              Expect.isTrue
                  (t.BDiffs |> List.exists (fun d -> d.Contains "UnknownActionAddr"))
                  "and one disagreement is about WHICH undeclared key is named first"

          testCase
              "the pipeline oracle agrees with CapabilityPipeline.typeCheck, eval, dependencyMap and evalFrom, and its dirty set with Propagation.dirtyFromChangedIds over that map, over generated registries, pipelines, reorderings and change sets"
          <| fun _ ->
              let t = underCulture "he-IL" (fun () -> pipeDifferential readers 3542 500)

              match t.PpDiffs with
              | d :: _ -> failtestf "the pipeline oracle and production DISAGREE\n%s" d
              | [] ->
                  // Adequacy. Measured at 500 pipelines: accepted 313, refused 187, evaluated to a
                  // result 290, refused in evaluation 23, held to eval incrementally 290 (254
                  // values reused), re-declared in another order 133 (79 of them topological), and
                  // 137 probes where a body moved at an unnamed node and the agreement broke.
                  Expect.isGreaterThan t.PpAccepted 200 (sprintf "pipelines were accepted (accepted=%d)" t.PpAccepted)
                  Expect.isGreaterThan t.PpRefused 100 (sprintf "pipelines were refused (refused=%d)" t.PpRefused)

                  Expect.isGreaterThan
                      t.PpEvaluated
                      180
                      (sprintf "accepted pipelines evaluated to a result (evaluated=%d)" t.PpEvaluated)

                  Expect.isGreaterThan
                      t.PpEvalRefused
                      8
                      (sprintf "accepted pipelines were refused in evaluation (evalRefused=%d)" t.PpEvalRefused)

                  Expect.isGreaterThan
                      t.PpIncremental
                      180
                      (sprintf "evalFrom was held to eval over a finished prior (incremental=%d)" t.PpIncremental)

                  Expect.isGreaterThan t.PpReused 120 (sprintf "evalFrom reused prior values (reused=%d)" t.PpReused)

                  Expect.isGreaterThan
                      t.PpReordered
                      70
                      (sprintf "accepted pipelines were re-declared in another order (reordered=%d)" t.PpReordered)

                  Expect.isGreaterThan
                      t.PpReorderedTopological
                      30
                      (sprintf
                          "some reorderings were topological orders too (reorderedTopological=%d)"
                          t.PpReorderedTopological)

                  Expect.isGreaterThan
                      t.PpStaleLost
                      60
                      (sprintf
                          "a body that moved at an UNNAMED node broke the agreement — the hypothesis is needed (staleLost=%d)"
                          t.PpStaleLost)

                  for cls in
                      [ "DuplicateNode"
                        "UnknownNode"
                        "PipelineNoSuchCapability"
                        "PipelineArgRefused"
                        "PipelineCycle"
                        "EdgeTypeMismatch"
                        "PipelineForwardEdge" ] do
                      Expect.isTrue
                          (Set.contains cls t.PpClasses)
                          (sprintf "the sample reached a %s refusal (reached: %A)" cls t.PpClasses)

                  Expect.equal
                      (underCulture "he-IL" (fun () -> pipeDifferential readers 3542 500))
                      t
                      "same seed => identical tally"

          testCase
              "a pipeline oracle handed a BLIND int reader DISAGREES with CapabilityPipeline.typeCheck — the measurement can fail"
          <| fun _ ->
              // Under the blind reader a literal in an integer hole is out of space to the model
              // and in space to production, so the model refuses a pipeline production accepts.
              let t = pipeDifferential blindReaders 3542 150
              Expect.isNonEmpty t.PpDiffs "a blind int reader MUST disagree with production on the pipeline"

              Expect.isTrue
                  (t.PpDiffs |> List.exists (fun d -> d.Contains "ArgOutOfSpace"))
                  "and the disagreement is about a literal's SPACE check, which is what the reader blinded"

          testCase
              "the codec oracle agrees with CapabilityCodec and the pipeline codec over generated capabilities, pipelines and mutated documents"
          <| fun _ ->
              let t = underCulture "he-IL" (fun () -> codecDifferential readers 3543 300)

              match t.CoDiffs with
              | d :: _ -> failtestf "the codec oracle and production DISAGREE\n%s" d
              | [] ->
                  // Adequacy. Measured at 300 capabilities: 1,200 documents (588 accepted, 612
                  // refused), 237 well-formed, 22 read back in a normal form, 294 through the
                  // bytes, 1,199 signature documents, 573 pipeline nodes and 1,148 pipeline
                  // documents.
                  Expect.equal t.CoEncoded 300 "every capability's encoding was compared"

                  Expect.isGreaterThan
                      t.CoAccepted
                      350
                      (sprintf "capability documents were accepted (accepted=%d)" t.CoAccepted)

                  Expect.isGreaterThan
                      t.CoRefused
                      350
                      (sprintf "capability documents were refused (refused=%d)" t.CoRefused)

                  Expect.isGreaterThan
                      t.CoWellFormed
                      150
                      (sprintf "well-formed capabilities read back as themselves (wellFormed=%d)" t.CoWellFormed)

                  Expect.isGreaterThan
                      t.CoPayloadRefused
                      100
                      (sprintf
                          "an entry without the member its kind needs was refused at that member (payloadRefused=%d)"
                          t.CoPayloadRefused)

                  Expect.isGreaterThan
                      t.CoBytes
                      200
                      (sprintf "the round trip ran through the bytes (bytes=%d)" t.CoBytes)

                  Expect.isGreaterThan
                      t.CoSignatures
                      800
                      (sprintf "the signature reader was compared on its own (signatures=%d)" t.CoSignatures)

                  Expect.isGreaterThan t.CoNodes 300 (sprintf "pipeline nodes were encoded (nodes=%d)" t.CoNodes)

                  Expect.isGreaterThan
                      t.CoNodeDocuments
                      700
                      (sprintf "pipeline documents were decoded (nodeDocuments=%d)" t.CoNodeDocuments)

                  for code in [ "MissingField"; "WrongKind"; "UnknownTag"; "OutOfRange" ] do
                      Expect.isTrue
                          (Set.contains code t.CoCodes)
                          (sprintf "the sample reached a %s refusal (reached: %A)" code t.CoCodes)

                  Expect.equal
                      (underCulture "he-IL" (fun () -> codecDifferential readers 3543 300))
                      t
                      "same seed => identical tally"

          testCase
              "a codec oracle handed a BLIND float-fault reader DISAGREES with the shipped readers — the measurement can fail"
          <| fun _ ->
              // Under the blind reader a float range with a NaN, an infinite or an inverted bound
              // is well-formed to the model, which then accepts a declaration the shipped reader
              // refuses `OutOfRange`.
              let t = codecDifferential blindFaultReaders 3543 300
              Expect.isNonEmpty t.CoDiffs "a blind float-fault reader MUST disagree with the shipped readers"

              Expect.isTrue
                  (t.CoDiffs |> List.exists (fun d -> d.Contains "OutOfRange"))
                  "and the disagreement is about the admission check on read, which is what the reader blinded"

          // ---- Phase 455 — the seam codec, whole ----

          testCase
              "the seam-codec oracle agrees with CapabilityCodec's invocation, envelope and invoke-error codecs over generated values and mutated documents, and every typed refusal resolves"
          <| fun _ ->
              let t =
                  underCulture "he-IL" (fun () -> seamCodecDifferential ModelCap.invocation_json 4550 300)

              match t.ScDiffs with
              | d :: _ -> failtestf "the seam-codec oracle and production DISAGREE\n%s" d
              | [] ->
                  Expect.isGreaterThan t.ScDocuments 3000 (sprintf "documents were read (%d)" t.ScDocuments)

                  Expect.isGreaterThan
                      t.ScRepeatRefused
                      20
                      (sprintf "a repeated address was refused on an UNMUTATED encoding (%d)" t.ScRepeatRefused)

                  Expect.isGreaterThan
                      t.ScInvocationRefused
                      100
                      (sprintf "invocations were refused (%d)" t.ScInvocationRefused)

                  Expect.isGreaterThan
                      t.ScErrorsRefused
                      100
                      (sprintf "invoke errors were refused (%d)" t.ScErrorsRefused)

                  Expect.isGreaterThan
                      t.ScDeferredRefused
                      100
                      (sprintf "envelopes were refused (%d)" t.ScDeferredRefused)

                  for c in [ "MissingField"; "WrongKind"; "UnknownTag"; "OutOfRange" ] do
                      Expect.isTrue (t.ScCodes.Contains c) (sprintf "a %s refusal was compared (%A)" c t.ScCodes)

          testCase
              "a seam-codec oracle whose invocation writer DROPS an argument DISAGREES with CapabilityCodec — the measurement can fail"
          <| fun _ ->
              let t = seamCodecDifferential droppingInvocationJson 4551 100
              Expect.isNonEmpty t.ScDiffs "a writer that drops an argument MUST disagree with production's"

              Expect.isTrue
                  (t.ScDiffs |> List.exists (fun d -> d.Contains "encodes differently"))
                  "and the disagreement is about the encoding, which is what the writer dropped" ]
