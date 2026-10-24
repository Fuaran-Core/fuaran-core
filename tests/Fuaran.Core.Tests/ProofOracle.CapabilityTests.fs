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
open Fuaran.Core.Tests.CapabilityOracle

let private modelSpaceRender (s: ModelCap.value_space) : string =
    match s with
    | ModelCap.IntRange(lo, hi) -> sprintf "int[%s..%s]" (string lo) (string hi)
    | ModelCap.FloatRange(lo, hi) -> sprintf "float[%s..%s]" lo hi
    | ModelCap.StringLen(lo, hi) -> sprintf "len[%s..%s]" (string lo) (string hi)
    | ModelCap.Enum xs -> sprintf "enum{%s}" (String.concat "," xs)
    | ModelCap.AnyString -> "any"
    | ModelCap.SlotTree c -> sprintf "tree{%s}" (defaultArg (ofMOpt c) "*")

/// The go-red: an int reader that reads nothing, so every int-ranged value is out of space to
/// the model and in space to production.
let blindReaders: ModelCap.readers =
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

/// `NoSuchCapability`'s `known` list is production's sorted map keys and the model's list in
/// registration order — the ONE ordering the model does not carry; both render sorted.
let prodInvokeErrRender (e: InvokeError) : string =
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

let modelInvokeErrRender (e: ModelCap.invoke_error) : string =
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

/// The render pairs of the seam's answer DUs: a declaration fault, an application's refusal, an
/// invocation's refusal and the body's envelope.
let private declFault: Differential<DeclarationFault, ModelCap.decl_fault> =
    Differential.pair prodFaultRender modelFaultRender

let private applyError: Differential<ApplyError, ModelCap.apply_error> =
    Differential.pair prodApplyErrRender modelApplyErrRender

let private invokeError: Differential<InvokeError, ModelCap.invoke_error> =
    Differential.pair prodInvokeErrRender modelInvokeErrRender

let private deferredInt: Differential<Deferred<int>, ModelCap.deferred<int>> =
    Differential.pair prodDeferredRender modelDeferredRender

/// A refusal's class: its render up to the payload's opening parenthesis.
let errClass (render: 'e -> string) (e: 'e) : string =
    let s = render e
    s.Substring(0, s.IndexOf '(')

/// A value for a space — in space more often than not, and out of it (or unparseable) the rest.
let genValueFor (s: ValueSpace) (r: ConfRng.T) : string * ConfRng.T =
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

/// The function seam's tally: the artifacts compared, each verdict of apply, curry and compose,
/// the non-total artifacts and under-declared roots, and the refusal classes the pool reached.
let private fnTally =
    Tally.empty
        [ "Artifacts"
          "Applied"
          "ApplyRefused"
          "Curried"
          "CurryRefused"
          "Composed"
          "ComposeRefused"
          "NonTotal"
          "AuditErrors" ]
    |> Tally.sets [ "Classes" ]

let private fnProbe
    (rd: ModelCap.readers)
    (t: RNode)
    (args: Map<string, Arg<RNode>>)
    (inner: RNode)
    (slot: string)
    (acc: Tally)
    : Tally =
    let mw = modelWitness
    let margs = args |> Map.toList |> List.map (fun (k, v) -> k, argToModel v)

    let label =
        sprintf "artifact %A args %A" (artw.Holes t |> List.map (fun h -> h.Addr, h.Kind)) (Map.keys args |> List.ofSeq)

    // signature, isTotal, signatureExcluding
    let psg = Function.signature artw "f" t
    let msg = ModelCap.signature_of mw "f" t
    let bound = args |> Map.keys |> List.ofSeq

    // apply / curry — verdict, result tree, rejection class and payload
    let verdict
        (what: string)
        (p: Result<RNode, ApplyError>)
        (m: ModelCap.outcome<RNode, ModelCap.apply_error>)
        (tally: Tally)
        =
        match p, m with
        | Ok pn, ModelCap.Ok mn ->
            Differential.check
                (pn = mn)
                (fun () -> sprintf "%s: %s accepted on both sides with DIFFERENT trees" label what)
                tally
        | Error pe, ModelCap.Error me ->
            Differential.compareWith
                applyError
                (fun ps ms -> sprintf "%s: %s refused differently\n  prod %s\n  model %s" label what ps ms)
                pe
                me
                tally
        | Ok _, ModelCap.Error me ->
            Tally.disagree
                (sprintf "%s: %s accepted by production, refused by the model (%s)" label what (applyError.Model me))
                tally
        | Error pe, ModelCap.Ok _ ->
            Tally.disagree
                (sprintf
                    "%s: %s refused by production (%s), accepted by the model"
                    label
                    what
                    (applyError.Production pe))
                tally

    let pApply = Function.apply artw args t
    let pCurry = Function.curry artw args t
    let pComp = Function.compose artw slot inner t

    // Phase 307 — the admission check over the artifact.
    let validates (tally: Tally) =
        match Function.validate artw t, ModelCap.validate_decl rd mw t with
        | Ok(), FStar_Pervasives_Native.None -> tally
        | Error pf, FStar_Pervasives_Native.Some mf when declFault.Production pf = declFault.Model mf -> tally
        | p, m -> Tally.disagree (sprintf "%s: validate differs\n  prod %A\n  model %A" label p m) tally

    let pAudit = Function.auditEffect artw t

    let audits (tally: Tally) =
        match pAudit, ModelCap.audit_effect mw t with
        | Ok(), ModelCap.Ok() -> tally
        | Error(d, a), ModelCap.Error(md, ma) ->
            Differential.check
                (effToModel d = md && effToModel a = ma)
                (fun () -> sprintf "%s: auditEffect error payload differs" label)
                tally
        | _ -> Tally.disagree (sprintf "%s: auditEffect verdict differs" label) tally

    let reachClass (r: Result<RNode, ApplyError>) (tally: Tally) =
        match r with
        | Ok _ -> tally
        | Error e -> Tally.reach "Classes" (errClass applyError.Production e) tally

    acc
    |> Differential.check (sigToModel psg = msg) (fun () ->
        sprintf "%s: signature differs\n  prod %A\n  model %A" label (sigToModel psg) msg)
    |> Differential.check (Function.isTotal psg = ModelCap.is_total msg) (fun () ->
        sprintf "%s: isTotal differs (prod %b, model %b)" label (Function.isTotal psg) (ModelCap.is_total msg))
    |> Differential.check
        (sigToModel (Function.signatureExcluding (Set.ofList bound) psg) = ModelCap.signature_excluding bound msg)
        (fun () -> sprintf "%s: signatureExcluding differs" label)
    |> verdict "apply" pApply (ModelCap.apply rd mw margs t)
    |> verdict "curry" pCurry (ModelCap.curry rd mw margs t)
    |> verdict "compose" pComp (ModelCap.compose rd mw slot inner t)
    |> validates
    // the effect surfaces
    |> Differential.check
        (effToModel (Function.composedEffect artw inner t) = ModelCap.composed_effect mw inner t)
        (fun () -> sprintf "%s: composedEffect differs" label)
    |> Differential.check (effToModel (Function.observedEffect artw t) = ModelCap.observed_effect mw t) (fun () ->
        sprintf "%s: observedEffect differs" label)
    |> audits
    |> Tally.bump "Artifacts"
    |> Tally.bumpIf (Result.isOk pApply) "Applied"
    |> Tally.bumpIf (Result.isError pApply) "ApplyRefused"
    |> Tally.bumpIf (Result.isOk pCurry) "Curried"
    |> Tally.bumpIf (Result.isError pCurry) "CurryRefused"
    |> Tally.bumpIf (Result.isOk pComp) "Composed"
    |> Tally.bumpIf (Result.isError pComp) "ComposeRefused"
    |> Tally.bumpIf (not (Function.isTotal psg)) "NonTotal"
    |> Tally.bumpIf (Result.isError pAudit) "AuditErrors"
    |> reachClass pApply
    |> reachClass pCurry
    |> reachClass pComp

/// Run `f` under the named culture on this thread, restoring the culture after (Phase 307): the
/// readers premise is a claim about functions of the STRING alone, and he-IL is a culture whose
/// minus sign the pre-295 reader got wrong — so the differential runs there, not under whatever
/// culture the host machine happens to have.
let underCulture (name: string) (f: unit -> 'T) : 'T =
    let saved = System.Globalization.CultureInfo.CurrentCulture

    try
        System.Globalization.CultureInfo.CurrentCulture <- System.Globalization.CultureInfo name
        f ()
    finally
        System.Globalization.CultureInfo.CurrentCulture <- saved

let private fnDifferential (rd: ModelCap.readers) (seed: int) (trials: int) : Tally =
    let step (_: int) (rng: ConfRng.T) (tally: Tally) =
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

        r4, fnProbe rd t args (RNode.leaf "in" innerKind "z") slot tally

    Differential.seeded trials (ConfRng.ofSeed seed) step fnTally

/// The registry's tally: registrations and refused duplicates, each dispatch verdict, the bodies
/// that ran, failed or were never reached, and the refusal classes the pool reached.
let private capTally =
    Tally.empty
        [ "Registered"
          "DupRefused"
          "Dispatched"
          "NoSuch"
          "Validated"
          "Refused"
          "BodyRan"
          "BodyFailed"
          "RefusedWithoutBody" ]
    |> Tally.sets [ "Classes" ]

let capIdPool = [ "cap-a"; "cap-b"; "cap-c" ]

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

let private capProbe (rd: ModelCap.readers) (seedTag: int) (r: ConfRng.T) (acc: Tally) : ConfRng.T * Tally =
    // build a registry on both sides, in the same order, with duplicates possible
    let n, r1 = ConfRng.intBelow 4 r

    let register (i: int) (rng: ConfRng.T, preg: CapabilityRegistry, mreg: ModelCap.registry) (tally: Tally) =
        let id, r2 = ConfRng.choose capIdPool rng
        let sg, r3 = genCapSignature (sprintf "f%d" i) r2
        let placement, r4 = ConfRng.choose [ BuildTime; Server; ClientIsland Pyodide ] r3
        let cap = Capability.create id sg placement

        match CapabilityRegistry.register cap preg, ModelCap.register rd (capToModel cap) mreg with
        | Ok p', ModelCap.Ok m' -> (r4, p', m'), Tally.bump "Registered" tally
        | Error pe, ModelCap.Error me ->
            (r4, preg, mreg),
            tally
            |> Tally.bump "DupRefused"
            |> Differential.compareWith
                invokeError
                (fun ps ms -> sprintf "seed %d: register refused differently (%s vs %s)" seedTag ps ms)
                pe
                me
        | _ -> (r4, preg, mreg), Tally.disagree (sprintf "seed %d: register verdict differs on %s" seedTag id) tally

    let (rng, preg, mreg), acc =
        Differential.seededFrom n (r1, CapabilityRegistry.empty, ModelCap.empty) register acc

    // enumerate + tryFind agree (membership, and the entries themselves, sorted by id)
    let penum = CapabilityRegistry.enumerate preg |> List.map capToModel
    let menum = ModelCap.enumerate mreg |> List.sortBy _.c_id

    let acc =
        acc
        |> Differential.check (penum = menum) (fun () -> sprintf "seed %d: enumerate differs" seedTag)
        |> Differential.over ("cap-x" :: capIdPool) (fun id tally ->
            let p = CapabilityRegistry.tryFind id preg |> Option.map capToModel
            let m = ofMOpt (ModelCap.try_find_cap id mreg)
            Differential.check (p = m) (fun () -> sprintf "seed %d: tryFind %s differs" seedTag id) tally)

    // invocations: an id from the pool or an unregistered one, args drawn against the
    // resolved capability's signature when there is one
    let k, r5 = ConfRng.intBelow 4 rng

    let invoke (_: int) (rng: ConfRng.T) (tally: Tally) =
        let id, r6 = ConfRng.choose ("cap-x" :: capIdPool) rng

        let args, r7 =
            match CapabilityRegistry.tryFind id preg with
            | Some c -> genInvocation c.Signature r6
            | None -> [ "h1", "1" ], r6

        // Phase 210 — the body answers in the envelope, so the draw reaches all three of its cases:
        // 0 fails (the typed `BodyFailed`), 1 is still pending, 2 and 3 settle.
        let failBody, r8 = ConfRng.intBelow 4 r7
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

        // ... and neither side ever hands an `Ok(Failed _)` out of the seam
        // (`invoke_never_ok_failed` / `dispatch_never_ok_failed`, sampled here on the shipped one).
        let neverOkFailed (pv: Deferred<int>) (mv: ModelCap.deferred<int>) (tally: Tally) =
            match pv, mv with
            | Failed _, _
            | _, ModelCap.Failed _ ->
                Tally.disagree (sprintf "seed %d: dispatch %s accepted with an Ok(Failed _) envelope" seedTag id) tally
            | _ -> tally

        let dispatches (tally: Tally) =
            match p, m with
            | Ok pv, ModelCap.Ok mv ->
                tally
                |> Tally.bump "Dispatched"
                |> Differential.compareWith
                    deferredInt
                    (fun ps ms ->
                        sprintf
                            "seed %d: dispatch %s accepted with different envelopes\n  prod %s\n  model %s"
                            seedTag
                            id
                            ps
                            ms)
                    pv
                    mv
                |> neverOkFailed pv mv
            | Error pe, ModelCap.Error me ->
                tally
                |> Tally.reach "Classes" (errClass invokeError.Production pe)
                |> Differential.compareWith
                    invokeError
                    (fun ps ms ->
                        sprintf "seed %d: dispatch %s refused differently\n  prod %s\n  model %s" seedTag id ps ms)
                    pe
                    me
                |> Tally.bump (
                    match pe with
                    | NoSuchCapability _ -> "NoSuch"
                    | BodyFailed _ -> "BodyFailed"
                    | _ -> "Refused"
                )
            | Ok _, ModelCap.Error me ->
                Tally.disagree
                    (sprintf
                        "seed %d: dispatch %s accepted by production, refused by the model (%s)"
                        seedTag
                        id
                        (invokeError.Model me))
                    tally
            | Error pe, ModelCap.Ok _ ->
                Tally.disagree
                    (sprintf
                        "seed %d: dispatch %s refused by production (%s), accepted by the model"
                        seedTag
                        id
                        (invokeError.Production pe))
                    tally

        // the body ran on both sides or on neither — and never past a refusal that is not the body's
        let refusedWithoutBody (tally: Tally) =
            match p with
            | Error(BodyFailed _)
            | Ok _ -> tally
            | Error _ ->
                tally
                |> Tally.bump "RefusedWithoutBody"
                |> Differential.check (not pRan.Value) (fun () ->
                    sprintf "seed %d: dispatch %s was REFUSED and the body still ran" seedTag id)

        // validateArgs on its own, on the resolved capability
        let validates (tally: Tally) =
            match CapabilityRegistry.tryFind id preg with
            | Some c ->
                match Capability.validateArgs c args, ModelCap.validate_args rd (capToModel c) args with
                | Ok(), ModelCap.Ok() -> Tally.bump "Validated" tally
                | Error pe, ModelCap.Error me ->
                    Differential.compareWith
                        invokeError
                        (fun ps ms ->
                            sprintf "seed %d: validateArgs refused differently\n  prod %s\n  model %s" seedTag ps ms)
                        pe
                        me
                        tally
                | _ -> Tally.disagree (sprintf "seed %d: validateArgs verdict differs on %s" seedTag id) tally
            | None -> tally

        r8,
        tally
        |> dispatches
        |> Differential.check (pRan.Value = mRan.Value) (fun () ->
            sprintf
                "seed %d: dispatch %s ran the body on one side only (prod %b, model %b)"
                seedTag
                id
                pRan.Value
                mRan.Value)
        |> Tally.bumpIf pRan.Value "BodyRan"
        |> refusedWithoutBody
        |> validates

    Differential.seededFrom (k + 1) r5 invoke acc

let private capDifferential (rd: ModelCap.readers) (seed: int) (trials: int) : Tally =
    Differential.seeded trials (ConfRng.ofSeed seed) (capProbe rd) capTally

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

let private bindError: Differential<BindHandlerError, ModelCap.bind_handler_error> =
    Differential.pair prodBindErrRender modelBindErrRender

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

/// The handler table's tally: the verdicts, the accepted tables that bound a handler, the sets
/// bound again in a second arrival order or carrying a repeated key, and the refusal classes.
let private bindTally =
    Tally.empty [ "Accepted"; "AcceptedBound"; "Refused"; "Reordered"; "Replaced" ]
    |> Tally.sets [ "Classes" ]

let private bindCompare
    (le: string -> string -> bool)
    (seedTag: int)
    (t: RNode)
    (arriving: (string * HandlerBinding<int>) list)
    (tally: Tally)
    : Result<HandlerTable<int>, BindHandlerError> * Tally =
    let p = Function.bindHandlers artw (Map.ofList arriving) t

    let m =
        ModelCap.bind_handlers_of le modelWitness (arriving |> List.map (fun (a, hb) -> a, bindingToModel hb)) t

    p,
    match p, m with
    | Ok pt, ModelCap.Ok mt ->
        let pl = Map.toList pt.Handlers |> List.map (fun (a, hb) -> a, bindingToModel hb)

        Differential.check
            (pl = mt.ht_handlers)
            (fun () -> sprintf "seed %d: bindHandlers accepted with different tables" seedTag)
            tally
    | Error pe, ModelCap.Error me ->
        Differential.compareWith
            bindError
            (fun ps ms -> sprintf "seed %d: bindHandlers refused differently\n  prod %s\n  model %s" seedTag ps ms)
            pe
            me
            tally
    | Ok _, ModelCap.Error me ->
        Tally.disagree
            (sprintf
                "seed %d: bindHandlers accepted by production, refused by the model (%s)"
                seedTag
                (bindError.Model me))
            tally
    | Error pe, ModelCap.Ok _ ->
        Tally.disagree
            (sprintf
                "seed %d: bindHandlers refused by production (%s), accepted by the model"
                seedTag
                (bindError.Production pe))
            tally

let private bindDifferential (le: string -> string -> bool) (seed: int) (trials: int) : Tally =
    let step (i: int) (rng: ConfRng.T) (tally: Tally) =
        let t, r1 = genArtifact rng
        let arriving, r2 = genHandlers t r1
        let p, tally = bindCompare le i t arriving tally

        let tally =
            match p with
            | Ok pt ->
                tally
                |> Tally.bump "Accepted"
                |> Tally.bumpIf (not (Map.isEmpty pt.Handlers)) "AcceptedBound"
            | Error pe ->
                tally
                |> Tally.bump "Refused"
                |> Tally.reach "Classes" (errClass bindError.Production pe)

        let keys = arriving |> List.map fst

        let tally =
            if List.distinct keys = keys then
                // `bind_handlers_order_independent`, on the shipped function: the same distinct
                // bindings in another arrival order bind identically — the table or the refusal.
                if List.length arriving > 1 then
                    let again, tally =
                        bindCompare le i t (List.rev arriving) (Tally.bump "Reordered" tally)

                    Differential.check
                        (again = p)
                        (fun () -> sprintf "seed %d: bindHandlers depends on the order the handlers arrive in" i)
                        tally
                else
                    tally
            else
                Tally.bump "Replaced" tally

        r2, tally

    Differential.seeded trials (ConfRng.ofSeed seed) step bindTally

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
              Differential.expectAgrees "the function oracle" t
              let n name = Tally.count name t

              // Adequacy, per shape. Measured at 200 artifacts: applied 73, applyRefused 127,
              // curried 96, curryRefused 104, composed 17, composeRefused 183, nonTotal 11,
              // auditErrors 92. Each threshold sits below its measurement with room; they
              // catch a generator that stops reaching a shape, not pin the numbers.
              Expect.equal (n "Artifacts") 200 "every artifact was compared"

              Expect.isGreaterThan
                  (n "Applied")
                  40
                  (sprintf "full applications were accepted (applied=%d)" (n "Applied"))

              Expect.isGreaterThan
                  (n "ApplyRefused")
                  80
                  (sprintf "full applications were refused (applyRefused=%d)" (n "ApplyRefused"))

              Expect.isGreaterThan
                  (n "Curried")
                  60
                  (sprintf "partial applications were accepted (curried=%d)" (n "Curried"))

              Expect.isGreaterThan
                  (n "CurryRefused")
                  60
                  (sprintf "partial applications were refused (curryRefused=%d)" (n "CurryRefused"))

              Expect.isGreaterThan (n "Composed") 8 (sprintf "compositions were accepted (composed=%d)" (n "Composed"))

              Expect.isGreaterThan
                  (n "ComposeRefused")
                  100
                  (sprintf "compositions were refused (composeRefused=%d)" (n "ComposeRefused"))

              Expect.isGreaterThan
                  (n "NonTotal")
                  5
                  (sprintf "non-total artifacts were reached (nonTotal=%d)" (n "NonTotal"))

              Expect.isGreaterThan
                  (n "AuditErrors")
                  50
                  (sprintf "under-declared roots were reached (auditErrors=%d)" (n "AuditErrors"))

              let reached = Tally.reached "Classes" t

              for cls in
                  [ "UnknownHoleAddr"
                    "ValueOutOfSpace"
                    "RequiredHolesUnbound"
                    "NotASlot"
                    "SlotKindMismatch"
                    "NonTotal" ] do
                  Expect.isTrue
                      (Set.contains cls reached)
                      (sprintf "the sample reached a %s refusal (reached: %A)" cls reached)

              // seeded, replayable
              Expect.equal (fnDifferential readers 1770 200) t "same seed => identical tally"

          testCase
              "the capability oracle's determinism label and effect lattice are production's over every determinism set and a pool of labels"
          <| fun _ ->
              let lattice (a: DeterminismSource) (b: DeterminismSource) (t: Tally) =
                  let ea = { Host = Pure; Determinism = a }
                  let eb = { Host = ReadsHost; Determinism = b }

                  t
                  |> Differential.check
                      (effToModel (Effect.join ea eb) = ModelCap.join (effToModel ea) (effToModel eb))
                      (fun () -> sprintf "join differs on %A and %A" a b)
                  |> Differential.check
                      (Effect.covers ea eb = ModelCap.covers (effToModel ea) (effToModel eb))
                      (fun () -> sprintf "covers differs on %A over %A" a b)
                  |> Differential.check
                      (Effect.covers eb ea = ModelCap.covers (effToModel eb) (effToModel ea))
                      (fun () -> sprintf "covers differs on %A over %A" b a)

              let label (a: DeterminismSource) (t: Tally) =
                  let tag = Effect.determinismTag a

                  t
                  |> Differential.check (tag = ModelCap.determinism_tag (detToModel a)) (fun () ->
                      sprintf "determinismTag differs on %A: production %s" a tag)
                  |> Differential.check (Effect.tryDeterminismOfTag tag = Some a) (fun () ->
                      sprintf "production's tryDeterminismOfTag does not invert its own label %s" tag)
                  |> Differential.check (ModelCap.det_of_tag tag = toMOpt (Some(detToModel a))) (fun () ->
                      sprintf "the model's det_of_tag does not invert the label %s" tag)
                  |> Differential.over allDeterminismSets (lattice a)

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

              let reading (s: string) (t: Tally) =
                  let prod = Effect.tryDeterminismOfTag s |> Option.map detToModel |> toMOpt

                  Differential.check
                      (prod = ModelCap.det_of_tag s)
                      (fun () ->
                          sprintf "the readings of %A differ: production %A, model %A" s prod (ModelCap.det_of_tag s))
                      t

              let t =
                  Tally.empty []
                  |> Differential.over allDeterminismSets label
                  |> Differential.over pool reading

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

              Differential.expectAgrees "the capability oracle on the determinism set" t

          testCase
              "the capability oracle agrees with CapabilityRegistry.register, enumerate, tryFind and dispatch, and Capability.validateArgs, over generated registries and invocations"
          <| fun _ ->
              let t = underCulture "he-IL" (fun () -> capDifferential readers 1771 300)

              Differential.expectAgrees "the capability oracle" t
              let n name = Tally.count name t

              // Measured at 150 registries: registered 172, dupRefused 51, dispatched 33,
              // noSuch 270, validated 45, refused 71, bodyFailed 12, refusedWithoutBody 341.
              // Phase 307 doubled the draw to 300: the gate now refuses a repeat over anything
              // but a capped integer range, and the draw reaches ill-formed declarations and
              // repeated arguments, so fewer registrations reach a body per registry built.
              Expect.isGreaterThan
                  (n "Registered")
                  100
                  (sprintf "capabilities were registered (registered=%d)" (n "Registered"))

              Expect.isGreaterThan
                  (n "DupRefused")
                  20
                  (sprintf "duplicate registrations were refused (dupRefused=%d)" (n "DupRefused"))

              Expect.isGreaterThan
                  (n "Dispatched")
                  15
                  (sprintf "invocations were dispatched and ran (dispatched=%d)" (n "Dispatched"))

              Expect.isGreaterThan (n "NoSuch") 150 (sprintf "unregistered ids were refused (noSuch=%d)" (n "NoSuch"))

              Expect.isGreaterThan
                  (n "Validated")
                  20
                  (sprintf "argument sets were accepted (validated=%d)" (n "Validated"))

              Expect.isGreaterThan (n "Refused") 40 (sprintf "argument sets were refused (refused=%d)" (n "Refused"))

              Expect.isGreaterThan
                  (n "BodyFailed")
                  5
                  (sprintf "bodies failed and were named (bodyFailed=%d)" (n "BodyFailed"))

              Expect.equal
                  (n "BodyRan")
                  (n "Dispatched" + n "BodyFailed")
                  "the body ran exactly on the invocations validation passed"

              Expect.isGreaterThan
                  (n "RefusedWithoutBody")
                  200
                  (sprintf
                      "refusals were checked for a body that did not run (refusedWithoutBody=%d)"
                      (n "RefusedWithoutBody"))

              let reached = Tally.reached "Classes" t

              for cls in
                  [ "NoSuchCapability"
                    "UnknownArg"
                    "ArgOutOfSpace"
                    "RequiredArgsUnbound"
                    "UninvocableArg"
                    "BodyFailed" ] do
                  Expect.isTrue
                      (Set.contains cls reached)
                      (sprintf "the sample reached a %s refusal (reached: %A)" cls reached)

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
              Differential.expectLoses "a blind int reader on the seam" c

              Expect.isTrue
                  (Tally.disagreements c |> List.exists (fun d -> d.Contains "ArgOutOfSpace"))
                  "and the disagreement is about the SPACE check, which is what the reader blinded"

              let f = fnDifferential blindReaders 1770 60
              Differential.expectLoses "a blind int reader on the algebra" f

              Expect.isTrue
                  (Tally.disagreements f |> List.exists (fun d -> d.Contains "ValueOutOfSpace"))
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

              Differential.expectAgrees "the handler-table oracle" t
              let n name = Tally.count name t

              // Adequacy. Measured at 600 handler sets: accepted 421 (95 of them binding a
              // handler), refused 179, bound again in a second arrival order 80, a repeated key
              // 23. Each threshold sits below its measurement with room.
              Expect.isGreaterThan
                  (n "Accepted")
                  250
                  (sprintf "handler sets were accepted (accepted=%d)" (n "Accepted"))

              Expect.isGreaterThan
                  (n "AcceptedBound")
                  40
                  (sprintf "accepted tables bound a handler (acceptedBound=%d)" (n "AcceptedBound"))

              Expect.isGreaterThan (n "Refused") 100 (sprintf "handler sets were refused (refused=%d)" (n "Refused"))

              Expect.isGreaterThan
                  (n "Reordered")
                  40
                  (sprintf "handler sets were bound in a second arrival order (reordered=%d)" (n "Reordered"))

              Expect.isGreaterThan
                  (n "Replaced")
                  8
                  (sprintf
                      "a repeated key was reached, the later binding replacing the earlier (replaced=%d)"
                      (n "Replaced"))

              let reached = Tally.reached "Classes" t

              for cls in
                  [ "UnknownActionAddr"
                    "NotAnActionHole"
                    "RequiredActionsUnbound"
                    "HandlerEffectExceedsCeiling" ] do
                  Expect.isTrue
                      (Set.contains cls reached)
                      (sprintf "the sample reached a %s refusal (reached: %A)" cls reached)

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
              Differential.expectLoses "a reversed key order" t

              Expect.isTrue
                  (Tally.disagreements t |> List.exists (fun d -> d.Contains "UnknownActionAddr"))
                  "and one disagreement is about WHICH undeclared key is named first" ]
