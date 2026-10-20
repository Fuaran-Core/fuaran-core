module Fuaran.Core.Tests.CertificationVectors

// The certification vocabularies' drawn and adversarial vectors (Phases 292, 303): the draw the
// three-way differential (`IdlThreeHostTests`), the schema leg and the F* facts module share. Seeded per
// vocabulary, so a divergence reproduces from the seed and index alone. The F* facts module
// (`proofs/VocabularyVectors.fst`) is written from here by the tool's `render fstar`.

open System
open System.IO
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

let adversarialFloats =
    [ nan
      infinity
      -infinity
      -0.0
      2.0
      -2.0
      1e15
      1e21
      1e300
      5e-324
      0.1
      123456789012345680.0
      Double.MaxValue ]

let adversarialKeys =
    [ ""
      "$type"
      "kind"
      "id"
      "a\"b"
      "\\"
      "\u0000"
      "é"
      "😀"
      "Z"
      "a"
      "_"
      "10"
      "9"
      "0" ]

/// A type-guided rewrite, bottom-up: every child is rewritten first, then `f` may replace the
/// value at its slot (`None` keeps it). One walk for the adversarial pass and the hosted draw,
/// so both see every slot the presence rules leave on the wire and no other.
let rec rewrite (idl: Idl) (f: IdlType -> IdlValue -> IdlValue option) (t: IdlType) (v: IdlValue) : IdlValue =
    let recur = rewrite idl f

    let fields subst (decl: IdlField list) (fs: (string * IdlValue) list) =
        fs
        |> List.map (fun (n, fv) ->
            match decl |> List.tryFind (fun fd -> fd.Name = n) with
            | Some fd when fd.Opt <> HostOnly -> n, recur (TypeParams.substitute subst fd.Type) fv
            | _ -> n, fv)

    let inner =
        match t, v with
        | TList it, VList xs -> VList(xs |> List.map (recur it))
        | TMap vt, VMap entries -> VMap(entries |> List.map (fun (k, ev) -> k, recur vt ev))
        | TRecord n, VRecord fs ->
            match idl.Records |> List.tryFind (fun r -> r.Name = n) with
            | Some r -> VRecord(fields Map.empty r.Fields fs)
            | None -> v
        | TUnion(n, args), VUnion(tag, fs) ->
            match idl.Unions |> List.tryFind (fun u -> u.Name = n) with
            | Some u ->
                match TypeParams.bind u args, u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                | Some subst, Some c -> VUnion(tag, fields subst c.Fields fs)
                | _ -> v
            | None -> v
        | TNode, VNode(id, tag, fs) ->
            match idl.Kinds |> List.tryFind (fun k -> k.Tag = tag) with
            | Some k -> VNode(id, tag, fields Map.empty k.Fields fs)
            | None -> v
        | TNode, VNodeEnv(id, env, tag, fs) ->
            match idl.Kinds |> List.tryFind (fun k -> k.Tag = tag) with
            | Some k -> VNodeEnv(id, fields Map.empty idl.NodeFields env, tag, fields Map.empty k.Fields fs)
            | None -> v
        | _ -> v

    f t inner |> Option.defaultValue inner

let adversarial (idl: Idl) (rng: Random) : IdlValue -> IdlValue =
    rewrite
        idl
        (fun t v ->
            match t, v with
            | TFloat, (VFloat _ | VInt _) when rng.Next 3 = 0 ->
                Some(VFloat adversarialFloats[rng.Next adversarialFloats.Length])
            | TMap _, VMap entries ->
                let keys =
                    adversarialKeys
                    |> List.sortBy (fun _ -> rng.Next())
                    |> List.truncate (List.length entries)

                Some(VMap(List.zip keys (List.map snd entries)))
            | _ -> None)
        TNode

/// The vectors for one vocabulary: `count` sampled nodes, each followed by its adversarial
/// variant (seeded, so a divergence reproduces from the seed and index alone).
///
/// `hosted` names, per hosted slot's encoder, the draw that stands in for the sampler's: a hosted
/// slot that declares no wire form is drawn as arbitrary JSON, which the interpreter and the
/// TypeScript host carry verbatim and a compiled host's own codec need not admit. The compiled
/// host is the one whose value space is the host type, so its slot is drawn from it.
let vectorsFor (idl: Idl) (hosted: Map<string, Random -> JVal>) (seed: int) (count: int) : IdlValue list =
    let rng = Random seed
    let tags = idl.Kinds |> List.map _.Tag

    let rehost =
        rewrite idl (fun t v ->
            match t, v with
            | THosted h, VJson _ -> hosted.TryFind h.Encode |> Option.map (fun draw -> VJson(draw rng))
            | _ -> None)

    (match Sample.trySampleNodes idl tags seed count with
     | Ok vs -> vs
     | Error r -> failwithf "the sampler refused: %s" r.Describe)
    |> List.map (rehost TNode)
    |> List.collect (fun v -> [ v; adversarial idl rng v ])


/// The reference vocabulary's hosted `series` slot, drawn from the host type its prelude
/// codec reads (`float list`): finite floats only, since a non-finite float inside a verbatim
/// hosted value has no canonical rendering of its own and the interpreter refuses it (Phase 292).
let seriesDraw (rng: Random) : JVal =
    let pool = [ 0.0; 1.0; -2.5; 2.0; 1e21; 5e-324; 0.1; -0.0 ]
    JArr [ for _ in 1 .. rng.Next 4 -> JFloat pool[rng.Next pool.Length] ]

/// A certification vocabulary as the vectors are drawn over it: its name (which seeds its draw), the
/// vocabulary, and the draw standing in for the sampler at each hosted slot a compiled host reads.
type Vocabulary =
    { Name: string
      Idl: Idl
      HostedDraw: Map<string, Random -> JVal> }

/// The certification set, in the order every leg meets it.
let vocabularies: Vocabulary list =
    [ { Name = "document"
        Idl = SecondDomainIdl.docIdl
        HostedDraw = Map.empty }
      { Name = "score"
        Idl = ScoreDomainIdl.scoreIdl
        HostedDraw = Map.empty }
      { Name = "reference"
        Idl = ReferenceIdl.refIdl
        HostedDraw = Map.ofList [ "encSeries", seriesDraw ] } ]

/// Vectors per vocabulary before the adversarial pass doubles them. Seeded per vocabulary.
let perVocabulary = 600

let seedOf (name: string) = 20261002 + name.Length

/// An op holder: the vocabulary plus one kind whose only field is an op, so the sampler —
/// which draws nodes — draws ops through it. Nothing is added to the vocabulary's op set;
/// the holder vocabulary is returned beside the ops, since a drawn bare kind may be it.
let opsOf (idl: Idl) (seed: int) (count: int) : Idl * IdlValue list =
    let holder =
        { idl with
            Kinds =
                idl.Kinds
                @ [ { Tag = "OpHolder303"
                      Category = "probe"
                      Fields =
                        [ { Name = "op"
                            Type = TOp
                            Opt = Required
                            Aliases = []
                            Presence = None
                            Annotations = Annotations.Empty } ]
                      Annotations = Annotations.Empty } ] }

    holder,
    (match Sample.trySampleNodes holder [ "OpHolder303" ] seed count with
     | Ok vs -> vs
     | Error r -> failwithf "the sampler refused: %s" r.Describe)
    |> List.choose (function
        | VNode(_, _, [ "op", op ])
        | VNodeEnv(_, _, _, [ "op", op ]) -> Some op
        | _ -> None)

// ---------------------------------------------------------------------------
// The differential's vectors, for the schema leg (IdlSchemaValidatorTests): the schema is
// certified over exactly the documents the three hosts are.
// ---------------------------------------------------------------------------

/// Every certification vocabulary with its drawn and adversarial wires.
let certificationWires () : (string * Idl * string list) list =
    vocabularies
    |> List.map (fun c ->
        c.Name,
        c.Idl,
        vectorsFor c.Idl c.HostedDraw (seedOf c.Name) perVocabulary
        |> List.choose (fun v -> Encode.encode c.Idl v |> Result.toOption))

/// The reference vocabulary's drawn ops (the wire's second root), with the holder vocabulary
/// they encode under.
let certificationOps () : Idl * string list =
    let idl, ops = opsOf ReferenceIdl.refIdl 303 200
    idl, ops |> List.choose (fun op -> Encode.encodeOp idl op |> Result.toOption)

// ---------------------------------------------------------------------------
// The F* leg of the differential: the interpreter's vectors as NORMALISER facts over each
// certification vocabulary's generated model (`proofs/VocabularyVectors.fst`). The F* target
// refuses a vector its model cannot express, naming it; the selection here keeps the first
// `vectorsPerModel` the target admits, in the differential's own seeded order, so the drawn and
// the adversarial vectors both reach it.
// ---------------------------------------------------------------------------

let vectorsPerModel = 12

/// The committed module's name and path.
let vectorsModuleName = "VocabularyVectors"

/// Whether a wire carries one of the adversarial pass's map keys that no vocabulary member is
/// spelled as — the vectors that put a model's map members under the hostile keys. Sorted first
/// (stably), so the facts carry them rather than whichever draws happen to come first.
let hostileKey (wire: string) =
    [ ""; "a\"b"; "\\"; "\u0000"; "10"; "9"; "0" ]
    |> List.exists (fun k -> wire.Contains(Canonical.write Rendering.CrossHost (JStr k) + ":"))

let vectorModels () : VectorModel list =
    let wires = certificationWires ()

    [ "DocVocabulary", "D", "document"
      "ScoreVocabulary", "S", "score"
      "Vocabulary", "R", "reference" ]
    |> List.map (fun (model, prefix, name) ->
        let _, idl, ws = wires |> List.find (fun (n, _, _) -> n = name)

        let one (w: string) : VectorModel =
            { Model = model
              Prefix = prefix
              Idl = idl
              Wires = [ w ] }

        { one "" with
            Wires =
                ws
                |> List.distinct
                |> List.sortBy (fun w -> if hostileKey w then 0 else 1)
                |> List.filter (fun w -> FStarTarget.vectorsModule "Probe" [ one w ] |> Result.isOk)
                |> List.truncate vectorsPerModel })

/// The facts module the generator writes for the certification set.
let vectorsText () : Result<string, CodegenError> =
    FStarTarget.vectorsModule vectorsModuleName (vectorModels ())

let vectorsFile () =
    Snapshots.repoFile ("proofs/" + vectorsModuleName + ".fst")

/// `render fstar`'s share: rewrite `proofs/VocabularyVectors.fst`.
let emitVectors () : int =
    match vectorsText () with
    | Error e ->
        eprintfn "render fstar: %s" (CodegenError.describe e)
        2
    | Ok text ->
        File.WriteAllText(vectorsFile (), text.Replace("\r\n", "\n"), Text.UTF8Encoding false)
        printfn "regenerated %s" (vectorsFile ())
        0
