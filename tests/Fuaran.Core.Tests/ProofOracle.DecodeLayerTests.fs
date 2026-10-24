module Fuaran.Core.Tests.ProofOracle.DecodeLayerTests

// The `Proofs.Oracle` differential for `proofs/DecodeLayer.fst`: the extracted model
// (`proofs/oracle/DecodeLayer.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

// Phase 436 — the model's top-level module, aliased before `open Fuaran.Core` so that its
// `Decoder`-shaped names never meet production's.
module M = DecodeLayer

open Expecto

open Fuaran.Core
open Fuaran.Core.Tests.ProofOracle.Bridges
open Fuaran.Core.Tests.DecodeOracle

// ---------------------------------------------------------------------------
//  Phase 436 — the TYPED DECODE LAYER and the CODEC COMBINATORS: `DecodeLayer` beside
//  `DecodePath`, `DecodeError`, `Decoder` and `Codec`.
// ---------------------------------------------------------------------------
//
// `proofs/DecodeLayer.fst` models the four modules clause for clause over `WireDecode`'s `jval`,
// generic in what they decode, and proves three laws each building block preserves: the composed
// round trip, the collecting reader's totality, and refusal paths that resolve. What runs here is
// the extraction beside production, instantiated at `<int, float>` as `WireDecode`'s is:
//   1. every decoder combinator over a generated pool of `JVal`s, both sides rendered the same way;
//   2. generated codec SHAPES — records with required and optional members, unions, enumerations,
//      lists, a refinement and the leaves — built by both sides, their build-time refusals, their
//      writes of generated values, and their collecting and strict reads of those writes and of
//      MUTATIONS of them (a member dropped, added, duplicated or reordered, an item dropped or
//      added, a value of another kind put in a node's place);
//   3. the theorems' statements held to PRODUCTION over every one of those reads.
// The model drops a refusal's prose and the codec's schema, so a refusal is compared by its code
// and its path, never its sentence.

let private mCodeName (c: M.code) : string =
    match c with
    | M.InvalidJson -> "InvalidJson"
    | M.MissingField -> "MissingField"
    | M.WrongKind -> "WrongKind"
    | M.UnknownTag -> "UnknownTag"
    | M.OutOfRange -> "OutOfRange"
    | M.UndeclaredMember -> "UndeclaredMember"
    | M.LimitExceeded -> "LimitExceeded"
    | M.NotAdmitted -> "NotAdmitted"
    | M.SchemaFault -> "SchemaFault"

/// Every `RootDecodeCode` beside the model's `root_code` it is (Phase 443), in declaration order.
let private rootCodes: (RootDecodeCode * M.root_code) list =
    [ RootDecodeCode.WrongKind, M.RWrongKind
      RootDecodeCode.UnknownTag, M.RUnknownTag
      RootDecodeCode.OutOfRange, M.ROutOfRange
      RootDecodeCode.UndeclaredMember, M.RUndeclaredMember
      RootDecodeCode.LimitExceeded, M.RLimitExceeded
      RootDecodeCode.NotAdmitted, M.RNotAdmitted
      RootDecodeCode.SchemaFault, M.RSchemaFault ]

/// A refusal as both sides are compared: its code and its path.
let private pErr (e: DecodeError) : M.derr =
    { code = mCodeOf e.Code
      path = e.Path |> List.map segOf }

let private showSeg (s: M.seg) =
    match s with
    | M.Key k -> "[\"" + k + "\"]"
    | M.Index i -> "[" + string i + "]"

let private showErr (e: M.derr) =
    mCodeName e.code + "@$" + (e.path |> List.map showSeg |> String.concat "")

let rec private ofM (v: MJVal) : JVal =
    match v with
    | WireDecode.JStr s -> JStr s
    | WireDecode.JInt i -> JInt i
    | WireDecode.JBool b -> JBool b
    | WireDecode.JFloat f -> JFloat f
    | WireDecode.JArr xs -> JArr(xs |> List.map ofM)
    | WireDecode.JObj fields -> JObj(fields |> List.map (fun (k, v) -> k, ofM v))

let private pRes (render: 'a -> string) (r: Result<'a, DecodeError>) : string =
    match r with
    | Ok v -> "Ok " + render v
    | Error e -> "Error " + showErr (pErr e)

let private mRes (render: 'a -> string) (r: M.result<'a, M.derr>) : string =
    match r with
    | M.Ok v -> "Ok " + render v
    | M.Error e -> "Error " + showErr e

let private pAll (render: 'a -> string) (r: Result<'a, DecodeError list>) : string =
    match r with
    | Ok v -> "Ok " + render v
    | Error es -> "Error [" + (es |> List.map (pErr >> showErr) |> String.concat "; ") + "]"

let private mAll (render: 'a -> string) (r: M.result<'a, M.derr list>) : string =
    match r with
    | M.Ok v -> "Ok " + render v
    | M.Error es -> "Error [" + (es |> List.map showErr |> String.concat "; ") + "]"

let private mOpt (o: FStar_Pervasives_Native.option<'a>) : 'a option =
    match o with
    | FStar_Pervasives_Native.Some v -> Some v
    | FStar_Pervasives_Native.None -> None

let private toMOpt (o: 'a option) : FStar_Pervasives_Native.option<'a> =
    match o with
    | Some v -> FStar_Pervasives_Native.Some v
    | None -> FStar_Pervasives_Native.None

let private jr (v: JVal) = Json.render v

/// A path without its first step — the go-red bridge's whole edit.
let private dropFirstStep (e: M.derr) : M.derr =
    match e.path with
    | _ :: rest -> { e with path = rest }
    | [] -> e

let private mjr (v: MJVal) = Json.render (ofM v)

// ---- generators, seeded ----

let private keys = [| "a"; "b"; "c"; "kind"; "t" |]

let rec private genJVal (rng: System.Random) (depth: int) : JVal =
    match rng.Next(if depth <= 0 then 4 else 7) with
    | 0 -> JStr([| ""; "a"; "x"; "kind"; "square" |].[rng.Next 5])
    | 1 -> JInt(rng.Next(-3, 12))
    | 2 -> JBool(rng.Next 2 = 0)
    | 3 -> JFloat([| 0.5; -2.25; 3.0; 1e10 |].[rng.Next 4])
    | 4
    | 5 -> JObj [ for _ in 1 .. rng.Next 4 -> keys.[rng.Next keys.Length], genJVal rng (depth - 1) ]
    | _ -> JArr [ for _ in 1 .. rng.Next 4 -> genJVal rng (depth - 1) ]

let private genPath (rng: System.Random) : PathSegment list =
    [ for _ in 1 .. rng.Next 4 ->
          if rng.Next 2 = 0 then
              PathSegment.Key keys.[rng.Next keys.Length]
          else
              PathSegment.Index(rng.Next(-1, 3)) ]

// ---------------------------------------------------------------------------
//  1. The decoder combinators, beside `Decoder`.
// ---------------------------------------------------------------------------

/// The model's `andThen` function for the probe below: a positive integer, or a root `OutOfRange`.
let private positive (i: int) : Result<int, RootDecodeCode * string * string> =
    if i > 0 then
        Ok i
    else
        Error(RootDecodeCode.OutOfRange, "a positive int", "not positive")

/// The production `andThen` function refusing at `rc`, with no phrases (the model drops them).
let private refuseAs (rc: RootDecodeCode) : Result<JVal, RootDecodeCode * string * string> = Error(rc, "", "")

let private mPositive (i: int) : M.result<int, M.root_code> =
    if i > 0 then M.Ok i else M.Error M.ROutOfRange

/// Every combinator the model covers, asked one question of each side, as (name, production,
/// model). `drop` is the go-red instrument: the identity in every real run, and a path-truncating
/// bridge only in the case that must lose.
let private decoderProbes
    (drop: M.derr -> M.derr)
    (el: JVal)
    (path: PathSegment list)
    : (string * string * string) list =
    let m = toModel el
    let mp = path |> List.map segOf

    let mR render r =
        mRes
            render
            (match r with
             | M.Error e -> M.Error(drop e)
             | ok -> ok)

    let mA render r =
        mAll
            render
            (match r with
             | M.Error es -> M.Error(es |> List.map drop)
             | ok -> ok)

    let s (x: string) = x
    let i (x: int) = string x
    let f (x: float) = x.ToString("R", inv)
    let b (x: bool) = string x
    let strs (xs: string list) = String.concat "," xs

    let ints (xs: int list) =
        xs |> List.map string |> String.concat ","

    let tagCases = [ "a", Decoder.field "x" Decoder.str; "b", Decoder.succeed "B" ]
    let mTagCases = [ "a", M.field "x" M.str; "b", M.succeed "B" ]

    [ "json", pRes jr (Decoder.json el), mR mjr (M.json m)
      "str", pRes s (Decoder.str el), mR s (M.str m)
      "int", pRes i (Decoder.int el), mR i (M.int_d m)
      "float", pRes f (Decoder.float el), mR f (M.float_d float m)
      "bool", pRes b (Decoder.bool el), mR b (M.bool_d m)
      "items",
      pRes (List.map jr >> String.concat ",") (Decoder.items el),
      mR (List.map mjr >> String.concat ",") (M.items m)
      "obj",
      pRes (List.map (fun (k, v) -> k + ":" + jr v) >> String.concat ",") (Decoder.obj el),
      mR (List.map (fun (k, v) -> k + ":" + mjr v) >> String.concat ",") (M.obj m)
      "tryMember",
      (Decoder.tryMember "a" el |> Option.map jr |> string),
      (M.try_member "a" m |> mOpt |> Option.map mjr |> string)
      "field str", pRes s (Decoder.field "a" Decoder.str el), mR s (M.field "a" M.str m)
      "field int", pRes i (Decoder.field "b" Decoder.int el), mR i (M.field "b" M.int_d m)
      "field field",
      pRes s (Decoder.field "a" (Decoder.field "b" Decoder.str) el),
      mR s (M.field "a" (M.field "b" M.str) m)
      "optField",
      pRes (Option.map b >> string) (Decoder.optField "c" Decoder.bool el),
      mR (mOpt >> Option.map b >> string) (M.opt_field "c" M.bool_d m)
      "fieldOr", pRes i (Decoder.fieldOr "a" 7 Decoder.int el), mR i (M.field_or "a" 7 M.int_d m)
      "at json", pRes jr (Decoder.at path Decoder.json el), mR mjr (M.at mp M.json m)
      "at int", pRes i (Decoder.at path Decoder.int el), mR i (M.at mp M.int_d m)
      "list int", pRes ints (Decoder.list Decoder.int el), mR ints (M.list_d M.int_d m)
      "list field",
      pRes strs (Decoder.list (Decoder.field "a" Decoder.str) el),
      mR strs (M.list_d (M.field "a" M.str) m)
      "mapListIndexed",
      pRes ints (Decoder.mapListIndexed (fun ix -> Decoder.map (fun v -> v + ix) Decoder.int) el),
      mR ints (M.map_list_indexed (fun ix -> M.map (fun v -> v + int ix) M.int_d) m)
      "boundedList",
      pRes (List.map jr >> String.concat ",") (Decoder.boundedList 2 Decoder.json el),
      mR (List.map mjr >> String.concat ",") (M.bounded_list (bigint 2) M.json m)
      "listAll", pAll ints (Decoder.listAll Decoder.int el), mA ints (M.list_all M.int_d m)
      "sequence",
      pRes strs (Decoder.sequence [ Decoder.field "a" Decoder.str; Decoder.field "b" Decoder.str ] el),
      mR strs (M.sequence [ M.field "a" M.str; M.field "b" M.str ] m)
      "all",
      pAll strs (Decoder.all [ Decoder.field "a" Decoder.str; Decoder.field "b" Decoder.str ] el),
      mA strs (M.all_d [ M.field "a" M.str; M.field "b" M.str ] m)
      "oneOf", pRes i (Decoder.oneOf [ "x", 1; "a", 2 ] el), mR i (M.one_of [ "x", 1; "a", 2 ] m)
      "tagDispatch", pRes s (Decoder.tagDispatch "t" tagCases el), mR s (M.tag_dispatch "t" mTagCases m)
      "kindDispatch", pRes s (Decoder.kindDispatch tagCases el), mR s (M.kind_dispatch mTagCases m)
      "intRange", pRes i (Decoder.intRange 0 9 el), mR i (M.int_range (fun v -> v >= 0 && v <= 9) m)
      "undeclared",
      (Decoder.undeclared [ "a"; "b" ] el
       |> List.map (pErr >> showErr)
       |> String.concat ";"),
      (M.undeclared [ "a"; "b" ] m |> List.map (drop >> showErr) |> String.concat ";")
      "members", pRes string (Decoder.members [ "a"; "b" ] el), mR string (M.members [ "a"; "b" ] m)
      "closed",
      pRes jr (Decoder.closed [ "a" ] (Decoder.field "a" Decoder.json) el),
      mR mjr (M.closed [ "a" ] (M.field "a" M.json) m)
      "map", pRes i (Decoder.map String.length Decoder.str el), mR i (M.map String.length M.str m)
      "bind",
      pRes s (Decoder.bind (fun k -> Decoder.field k Decoder.str) (Decoder.field "t" Decoder.str) el),
      mR s (M.bind (fun k -> M.field k M.str) (M.field "t" M.str) m)
      "andThen", pRes i (Decoder.andThen positive Decoder.int el), mR i (M.and_then mPositive M.int_d m)
      "succeed", pRes i (Decoder.succeed 3 el), mR i (M.succeed 3 m)
      "wrongKind", showErr (pErr (Decoder.wrongKind "x" el)), showErr (drop (M.wrong_kind m))
      "missing", showErr (pErr (Decoder.missing "a")), showErr (drop (M.missing "a"))
      "resolve",
      (DecodePath.resolve path el |> Option.map jr |> string),
      (M.resolve mp m |> mOpt |> Option.map mjr |> string) ]
    @ [ for rc, mrc in rootCodes -> "fail " + string rc, pRes i (Decoder.fail rc "" "" el), mR i (M.fail mrc m) ]
    // Phase 453: `andThen`'s function refusing at every root code, over a decoder that answers, and
    // inside a member, so the refusal is anchored at the value and then carried one step down.
    @ [ for rc, mrc in rootCodes ->
            "andThen " + string rc,
            pRes jr (Decoder.field "a" (Decoder.json |> Decoder.andThen (fun (_: JVal) -> refuseAs rc)) el),
            mR mjr (M.field "a" (M.and_then (fun (_: MJVal) -> (M.Error mrc: M.result<MJVal, M.root_code>)) M.json) m) ]

/// `DecodeError`'s path operations and `resolvesIn`, over a refusal of every code at the path.
let private errorProbes
    (el: JVal)
    (path: PathSegment list)
    (prefix: PathSegment list)
    : (string * string * string) list =
    let m = toModel el

    [ for code in DecodeError.codes do
          let e =
              { DecodeError.make code "" "" with
                  Path = path }

          let me = pErr e
          let name = DecodeError.codeName code
          yield "make " + name, showErr (pErr (DecodeError.make code "" "")), showErr (M.make (mCodeOf code))

          yield
              "under " + name,
              showErr (pErr (DecodeError.under (PathSegment.Key "k") e)),
              showErr (M.under (M.Key "k") me)

          yield
              "within " + name,
              showErr (pErr (DecodeError.within prefix e)),
              showErr (M.within (prefix |> List.map segOf) me)

          yield "resolvesIn " + name, string (DecodeError.resolvesIn el e), string (M.resolves_in m me) ]

/// The decoder tally: the probes asked of both sides, the production refusals held to
/// `resolvesIn`, the codes they reached, and the refusals that do not resolve (`DecodeLayer.fst`'s theorem 3's
/// statement, production only).
let private decoderTally =
    Tally.empty [ "Probes"; "Refusals" ]
    |> Tally.sets [ "Codes" ]
    |> Tally.classes [ "unresolved" ]

/// Both sides over a generated pool; and the theorem-3 statement held to every refusal
/// PRODUCTION's sound combinators answer (the ones the model proves sound without a premise).
let private decoderPool (seed: int) (n: int) (drop: M.derr -> M.derr) : Tally =
    let step (_: int) (rng: System.Random) (t: Tally) =
        let el = genJVal rng 3
        let path = genPath rng

        // each probe is already rendered by the combinator's own pair of renderers
        let t =
            t
            |> Differential.over (decoderProbes drop el path @ errorProbes el path (genPath rng)) (fun (name, p, o) t ->
                t
                |> Tally.bump "Probes"
                |> Differential.compareWith
                    Differential.text
                    (fun p o ->
                        sprintf "%s over %s at %s: production %s, model %s" name (jr el) (DecodePath.render path) p o)
                    p
                    o)

        let sound: Result<obj, DecodeError> list =
            [ Decoder.field "a" (Decoder.field "b" Decoder.str) el |> Result.map box
              Decoder.at path Decoder.int el |> Result.map box
              Decoder.list (Decoder.field "a" Decoder.str) el |> Result.map box
              Decoder.boundedList 2 Decoder.int el |> Result.map box
              Decoder.tagDispatch "t" [ "a", Decoder.field "x" Decoder.str ] el
              |> Result.map box
              Decoder.closed [ "a" ] (Decoder.optField "a" Decoder.int) el |> Result.map box
              Decoder.intRange 0 9 el |> Result.map box ]

        let collected =
            [ match Decoder.listAll (Decoder.field "a" Decoder.int) el with
              | Error es -> yield! es
              | Ok _ -> ()
              yield! Decoder.undeclared [ "a" ] el ]

        let refusals =
            (sound
             |> List.choose (function
                 | Error e -> Some e
                 | Ok _ -> None))
            @ collected

        let t =
            t
            |> Differential.over refusals (fun e t ->
                let t =
                    t |> Tally.bump "Refusals" |> Tally.reach "Codes" (DecodeError.codeName e.Code)

                if not (DecodeError.resolvesIn el e) || e.Code = DecodeCode.InvalidJson then
                    Tally.record "unresolved" (sprintf "%s over %s" (DecodeError.render e) (jr el)) t
                else
                    t)

        rng, t

    Differential.seeded n (System.Random seed) step decoderTally

// ---------------------------------------------------------------------------
//  2. The codec combinators, beside `Codec`, over generated shapes.
// ---------------------------------------------------------------------------

/// The dynamic value every generated codec decodes to — one type, so both sides' codecs are
/// `Codec<DV>` and their answers compare by equality.
type private DV =
    | VStr of string
    | VInt of int
    | VBool of bool
    | VFloat of float
    | VJson of JVal
    | VList of DV list
    | VRec of (string * DV option) list
    | VCase of string * DV
    | VEnum of int

type private FieldShape =
    { Name: string
      Opt: bool
      Shape: Shape }

and private Shape =
    | SStr
    | SInt
    | SBool
    | SFloat
    | SJson
    | SEven
    | SList of Shape
    | SEnum of string list
    | SRec of FieldShape list
    | SUnion of key: string * cases: (string * FieldShape list) list

let private unStr =
    function
    | VStr s -> s
    | _ -> ""

let private unInt =
    function
    | VInt i -> i
    | _ -> 0

let private unBool =
    function
    | VBool b -> b
    | _ -> false

let private unFloat =
    function
    | VFloat f -> f
    | _ -> 0.0

let private unJson =
    function
    | VJson j -> j
    | _ -> JStr ""

let private unList =
    function
    | VList xs -> xs
    | _ -> []

let private lookup (name: string) (v: DV) : DV option =
    match v with
    | VRec fields -> fields |> List.tryFind (fun (k, _) -> k = name) |> Option.bind snd
    | _ -> None

let private project (tag: string) (v: DV) : DV option =
    match v with
    | VCase(t, p) when t = tag -> Some p
    | _ -> None

let private even (v: DV) : Result<DV, string> =
    match v with
    | VInt i when i % 2 = 0 -> Ok v
    | _ -> Error "odd"

let private mEven (v: DV) : M.result<DV, string> =
    match v with
    | VInt i when i % 2 = 0 -> M.Ok v
    | _ -> M.Error "odd"

let private faultP (f: CodecDeclarationFault) : string =
    match f with
    | CodecDeclarationFault.RepeatedSpelling s -> "RepeatedSpelling " + s
    | CodecDeclarationFault.RepeatedMember(n, c) -> sprintf "RepeatedMember %s %A" n c
    | CodecDeclarationFault.RepeatedTag t -> "RepeatedTag " + t
    | CodecDeclarationFault.DiscriminatorAsMember(t, k) -> "DiscriminatorAsMember " + t + " " + k
    | CodecDeclarationFault.Unrecognised p -> "Unrecognised $" + (p |> List.map (segOf >> showSeg) |> String.concat "")

let private faultM (f: M.fault) : string =
    match f with
    | M.RepeatedSpelling s -> "RepeatedSpelling " + s
    | M.RepeatedMember(n, c) -> sprintf "RepeatedMember %s %A" n (mOpt c)
    | M.RepeatedTag t -> "RepeatedTag " + t
    | M.DiscriminatorAsMember(t, k) -> "DiscriminatorAsMember " + t + " " + k
    | M.Unrecognised p -> "Unrecognised $" + (p |> List.map showSeg |> String.concat "")

// The production side. Every constructor argument is a `DV option` — a required member through
// `Codec.map Some Option.get` — so one record builder serves every arity's mix of required and
// optional members.
let private addP (f: FieldShape) (c: Codec<DV>) (fs: CodecFields<DV, DV option -> 'C>) : CodecFields<DV, 'C> =
    if f.Opt then
        Codec.optField f.Name (lookup f.Name) c fs
    else
        Codec.field f.Name (lookup f.Name) (Codec.map Some Option.get c) fs

let private recOf (fields: FieldShape list) (args: DV option list) : DV =
    VRec(List.zip (fields |> List.map _.Name) args)

let rec private prodCodec (s: Shape) : Result<Codec<DV>, CodecDeclarationFault list> =
    match s with
    | SStr -> Ok(Codec.map VStr unStr Codec.string)
    | SInt -> Ok(Codec.map VInt unInt Codec.int)
    | SBool -> Ok(Codec.map VBool unBool Codec.bool)
    | SFloat -> Ok(Codec.map VFloat unFloat Codec.float)
    | SJson -> Ok(Codec.map VJson unJson Codec.json)
    | SEven -> Ok(Codec.refine "an even int" even id (Codec.map VInt unInt Codec.int))
    | SList inner -> prodCodec inner |> Result.map (fun c -> Codec.map VList unList (Codec.list c))
    | SEnum spellings -> Codec.enum (spellings |> List.mapi (fun i sp -> sp, VEnum i))
    | SRec fields -> prodFields fields |> Result.bind Codec.build
    | SUnion(key, cases) ->
        let built =
            cases
            |> List.map (fun (tag, fields) ->
                prodFields fields
                |> Result.map (Codec.case tag (project tag) (fun p -> VCase(tag, p))))

        match
            built
            |> List.tryPick (function
                | Error e -> Some e
                | Ok _ -> None)
        with
        | Some e -> Error e
        | None ->
            Codec.union
                key
                (built
                 |> List.choose (function
                     | Ok c -> Some c
                     | Error _ -> None))

and private prodFields (fields: FieldShape list) : Result<CodecFields<DV, DV>, CodecDeclarationFault list> =
    let codecs = fields |> List.map (fun f -> prodCodec f.Shape)

    match
        codecs
        |> List.tryPick (function
            | Error e -> Some e
            | Ok _ -> None)
    with
    | Some e -> Error e
    | None ->
        let cs =
            codecs
            |> List.choose (function
                | Ok c -> Some c
                | Error _ -> None)

        let mk = recOf fields

        match fields, cs with
        | [], [] -> Ok(Codec.record (mk []))
        | [ f1 ], [ c1 ] -> Ok(Codec.record (fun a -> mk [ a ]) |> addP f1 c1)
        | [ f1; f2 ], [ c1; c2 ] -> Ok(Codec.record (fun a b -> mk [ a; b ]) |> addP f1 c1 |> addP f2 c2)
        | [ f1; f2; f3 ], [ c1; c2; c3 ] ->
            Ok(
                Codec.record (fun a b c -> mk [ a; b; c ])
                |> addP f1 c1
                |> addP f2 c2
                |> addP f3 c3
            )
        | _ -> failwith "the shape generator draws at most three members"

// The model side, the same shape built from the extracted combinators.
type private MCodec = M.codec<int, float, DV>
type private MFields<'C> = M.fields<int, float, DV, 'C>

let private addM (f: FieldShape) (c: MCodec) (fs: MFields<FStar_Pervasives_Native.option<DV> -> 'C>) : MFields<'C> =
    if f.Opt then
        M.c_opt_field f.Name (lookup f.Name >> toMOpt) c fs
    else
        M.c_field f.Name (lookup f.Name >> toMOpt) (M.c_map FStar_Pervasives_Native.Some (fun o -> (mOpt o).Value) c) fs

let private recOfM (fields: FieldShape list) (args: FStar_Pervasives_Native.option<DV> list) : DV =
    recOf fields (args |> List.map mOpt)

let rec private modelCodec (s: Shape) : M.result<MCodec, M.fault list> =
    match s with
    | SStr -> M.Ok(M.c_map VStr unStr (M.c_string ()))
    | SInt -> M.Ok(M.c_map VInt unInt (M.c_int ()))
    | SBool -> M.Ok(M.c_map VBool unBool (M.c_bool ()))
    | SFloat -> M.Ok(M.c_map VFloat unFloat (M.c_float float))
    | SJson -> M.Ok(M.c_map (ofM >> VJson) (unJson >> toModel) (M.c_json ()))
    | SEven -> M.Ok(M.c_refine "an even int" mEven id (M.c_map VInt unInt (M.c_int ())))
    | SList inner ->
        match modelCodec inner with
        | M.Ok c -> M.Ok(M.c_map VList unList (M.c_list c))
        | M.Error e -> M.Error e
    | SEnum spellings -> M.c_enum (spellings |> List.mapi (fun i sp -> sp, VEnum i))
    | SRec fields ->
        match modelFields fields with
        | M.Ok fs -> M.c_build fs
        | M.Error e -> M.Error e
    | SUnion(key, cases) ->
        let built =
            cases
            |> List.map (fun (tag, fields) ->
                match modelFields fields with
                | M.Ok fs -> M.Ok(M.c_case tag (project tag >> toMOpt) (fun p -> VCase(tag, p)) fs)
                | M.Error e -> M.Error e)

        match
            built
            |> List.tryPick (function
                | M.Error e -> Some e
                | M.Ok _ -> None)
        with
        | Some e -> M.Error e
        | None ->
            M.c_union
                key
                (built
                 |> List.choose (function
                     | M.Ok c -> Some c
                     | M.Error _ -> None))

and private modelFields (fields: FieldShape list) : M.result<MFields<DV>, M.fault list> =
    let codecs = fields |> List.map (fun f -> modelCodec f.Shape)

    match
        codecs
        |> List.tryPick (function
            | M.Error e -> Some e
            | M.Ok _ -> None)
    with
    | Some e -> M.Error e
    | None ->
        let cs =
            codecs
            |> List.choose (function
                | M.Ok c -> Some c
                | M.Error _ -> None)

        let mk = recOfM fields

        match fields, cs with
        | [], [] -> M.Ok(M.c_record (mk []))
        | [ f1 ], [ c1 ] -> M.Ok(M.c_record (fun a -> mk [ a ]) |> addM f1 c1)
        | [ f1; f2 ], [ c1; c2 ] -> M.Ok(M.c_record (fun a b -> mk [ a; b ]) |> addM f1 c1 |> addM f2 c2)
        | [ f1; f2; f3 ], [ c1; c2; c3 ] ->
            M.Ok(
                M.c_record (fun a b c -> mk [ a; b; c ])
                |> addM f1 c1
                |> addM f2 c2
                |> addM f3 c3
            )
        | _ -> failwith "the shape generator draws at most three members"

// ---- shapes, values and mutations ----

let private memberNames = [| "a"; "b"; "c"; "kind" |]

let rec private genShape (rng: System.Random) (depth: int) : Shape =
    let leaf () =
        [| SStr; SInt; SBool; SFloat; SJson; SEven |].[rng.Next 6]

    let fields () =
        [ for _ in 1 .. rng.Next 4 ->
              { Name = memberNames.[rng.Next memberNames.Length]
                Opt = rng.Next 3 = 0
                Shape = genShape rng (depth - 1) } ]

    if depth <= 0 then
        leaf ()
    else
        match rng.Next 9 with
        | 0
        | 1 -> leaf ()
        | 2 -> SList(genShape rng (depth - 1))
        | 3 -> SEnum [ for _ in 1 .. 1 + rng.Next 3 -> [| "x"; "y"; "z" |].[rng.Next 3] ]
        | 4
        | 5
        | 6 -> SRec(fields ())
        | _ ->
            SUnion(
                [| "kind"; "t" |].[rng.Next 2],
                [ for _ in 1 .. 1 + rng.Next 3 -> [| "p"; "q"; "r" |].[rng.Next 3], fields () ]
            )

let rec private genValue (rng: System.Random) (s: Shape) : DV =
    match s with
    | SStr -> VStr([| ""; "a"; "x" |].[rng.Next 3])
    | SInt -> VInt(rng.Next(-3, 9))
    | SBool -> VBool(rng.Next 2 = 0)
    | SFloat -> VFloat([| 0.5; -2.25; 3.0 |].[rng.Next 3])
    | SJson -> VJson(genJVal rng 2)
    | SEven -> VInt(2 * rng.Next(-2, 4) + (if rng.Next 6 = 0 then 1 else 0))
    | SList inner -> VList [ for _ in 1 .. rng.Next 4 -> genValue rng inner ]
    | SEnum spellings -> VEnum(rng.Next(List.length spellings + (if rng.Next 6 = 0 then 1 else 0)))
    | SRec fields -> genRec rng fields
    | SUnion(_, cases) ->
        if rng.Next 8 = 0 then
            VCase("none", VRec [])
        else
            let tag, fields = cases.[rng.Next(List.length cases)]
            VCase(tag, genRec rng fields)

and private genRec (rng: System.Random) (fields: FieldShape list) : DV =
    VRec
        [ for f in fields ->
              f.Name,
              (if f.Opt && rng.Next 2 = 0 then
                   None
               else
                   Some(genValue rng f.Shape)) ]

/// A mutation at a random node: another kind in its place, a member dropped, added, duplicated or
/// moved to the front, an item dropped or added.
let rec private mutate (rng: System.Random) (j: JVal) : JVal =
    let here () =
        match j with
        | JObj((_ :: _) as ms) ->
            match rng.Next 4 with
            | 0 -> JObj(List.removeAt (rng.Next ms.Length) ms)
            | 1 -> JObj(ms @ [ "zz", JInt 1 ])
            | 2 -> JObj(ms @ [ List.item (rng.Next ms.Length) ms ])
            | _ -> JObj(List.rev ms)
        | JArr((_ :: _) as xs) ->
            if rng.Next 2 = 0 then
                JArr(List.removeAt (rng.Next xs.Length) xs)
            else
                JArr(xs @ [ JStr "m" ])
        | JStr _ -> JInt 1
        | _ -> JStr "s"

    match j with
    | JObj((_ :: _) as ms) when rng.Next 2 = 0 ->
        let i = rng.Next ms.Length
        JObj(ms |> List.mapi (fun ix (k, v) -> if ix = i then k, mutate rng v else k, v))
    | JArr((_ :: _) as xs) when rng.Next 2 = 0 ->
        let i = rng.Next xs.Length
        JArr(xs |> List.mapi (fun ix v -> if ix = i then mutate rng v else v))
    | _ -> here ()

let private showDV (v: DV) = sprintf "%A" v

/// A declaration's build-time refusals — each fault, in order.
let private declaration: Differential<CodecDeclarationFault list, M.fault list> =
    Differential.pair (List.map faultP >> sprintf "%A") (List.map faultM >> sprintf "%A")

/// One write-time refusal.
let private writeFault: Differential<CodecDeclarationFault, M.fault> =
    Differential.pair faultP faultM

/// A written value, as JSON text.
let private written: Differential<JVal, MJVal> = Differential.pair jr mjr

/// The collecting reader's answer: the value, or every refusal.
let private collecting: Differential<Result<DV, DecodeError list>, M.result<DV, M.derr list>> =
    Differential.pair (pAll showDV) (mAll showDV)

/// The strict reader's answer: the value, or the first refusal.
let private strict: Differential<Result<DV, DecodeError>, M.result<DV, M.derr>> =
    Differential.pair (pRes showDV) (mRes showDV)

/// The codec tally: the shapes drawn, the declarations both sides refused, the values written,
/// the inputs read and the writes that read back as themselves; the codes the refusals reached;
/// the refusal lists below the root and those the BLIND comparison loses on; and the theorems'
/// statements failing on production (a finding class, production only).
let private codecTally =
    Tally.empty
        [ "Shapes"
          "Refused"
          "Writes"
          "Reads"
          "RoundTrips"
          "NonRoot"
          "BlindLosses" ]
    |> Tally.sets [ "Codes" ]
    |> Tally.classes [ "statement" ]

/// Build, write and read every generated shape on both sides; hold production to the theorems'
/// statements over every read; and count where the BLIND comparison — the model's refusals with
/// their first step dropped — loses.
let private codecPool (seed: int) (n: int) : Tally =
    let blind (es: M.derr list) = es |> List.map dropFirstStep

    let read (c: Codec<DV>) (mc: MCodec) (input: JVal) (t: Tally) =
        let p = c.ReadAll input
        let m = mc.read_all (toModel input)
        let pd = Codec.decoder c input
        let md = M.c_decoder mc (toModel input)

        let t =
            t
            |> Tally.bump "Reads"
            |> Differential.compareWith
                collecting
                (fun ps ms -> sprintf "read of %s: production %s, model %s" (jr input) ps ms)
                p
                m
            |> Differential.compareWith
                strict
                (fun ps ms -> sprintf "decoder of %s: production %s, model %s" (jr input) ps ms)
                pd
                md

        match p, m with
        | Error es, M.Error mes ->
            let t =
                if es.IsEmpty then
                    Tally.record "statement" (sprintf "an EMPTY refusal list over %s" (jr input)) t
                else
                    t

            let t =
                match pd with
                | Error first when not es.IsEmpty && first = es.Head -> t
                | _ ->
                    Tally.record "statement" (sprintf "the strict reader is not the first defect over %s" (jr input)) t

            let t =
                t
                |> Differential.over es (fun e t ->
                    let t = Tally.reach "Codes" (DecodeError.codeName e.Code) t

                    if not (DecodeError.resolvesIn input e) || e.Code = DecodeCode.InvalidJson then
                        Tally.record
                            "statement"
                            (sprintf "%s does not resolve in %s" (DecodeError.render e) (jr input))
                            t
                    else
                        t)

            t
            |> Tally.bumpIf (es |> List.exists (fun e -> not e.Path.IsEmpty)) "NonRoot"
            |> Tally.bumpIf (collecting.Model(M.Error(blind mes)) <> collecting.Production p) "BlindLosses"
        | _ -> t

    let write (rng: System.Random) (shape: Shape) (c: Codec<DV>) (mc: MCodec) (t: Tally) =
        let v = genValue rng shape
        let t = Tally.bump "Writes" t

        match c.Write v, mc.write v with
        | Error pf, M.Error mf ->
            Differential.compareWith
                writeFault
                (fun ps ms -> sprintf "write of %s: production %s, model %s" (showDV v) ps ms)
                pf
                mf
                t
        | Ok j, M.Ok mj ->
            let t =
                t
                |> Differential.compareWith
                    written
                    (fun ps ms -> sprintf "write of %s: production %s, model %s" (showDV v) ps ms)
                    j
                    mj

            // The written value reads back as itself unless the refinement refuses it (an odd
            // even-int) — the one premise the generator breaks on purpose.
            let t =
                match c.ReadAll j with
                | Ok back when back = v -> Tally.bump "RoundTrips" t
                | _ -> t

            Differential.over (j :: [ for _ in 1..3 -> mutate rng j ]) (read c mc) t
        | Ok j, M.Error mf ->
            Tally.disagree (sprintf "write of %s: production %s, model %s" (showDV v) (jr j) (faultM mf)) t
        | Error pf, M.Ok mj ->
            Tally.disagree (sprintf "write of %s: production %s, model %s" (showDV v) (faultP pf) (mjr mj)) t

    let step (_: int) (rng: System.Random) (t: Tally) =
        let shape = genShape rng 3
        let t = Tally.bump "Shapes" t

        let t =
            match prodCodec shape, modelCodec shape with
            | Error pf, M.Error mf ->
                t
                |> Tally.bump "Refused"
                |> Differential.compareWith
                    declaration
                    (fun ps ms -> sprintf "build of %A: production %s, model %s" shape ps ms)
                    pf
                    mf
            | Ok c, M.Ok mc -> Differential.over [ 1..4 ] (fun _ t -> write rng shape c mc t) t
            | Ok _, M.Error mf ->
                Tally.disagree (sprintf "build of %A: production Ok, model %A" shape (List.map faultM mf)) t
            | Error pf, M.Ok _ ->
                Tally.disagree (sprintf "build of %A: production %A, model Ok" shape (List.map faultP pf)) t

        rng, t

    Differential.seeded n (System.Random seed) step codecTally

let private show (xs: string list) =
    xs |> List.truncate 12 |> String.concat "\n"

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- Phase 436: the decode layer and the codec combinators beside DecodeLayer.fst ----

          testCase
              "the decode-layer oracle agrees with every Decoder combinator, DecodeError's path operations and resolvesIn"
          <| fun _ ->
              let t = decoderPool 436 1500 id
              Differential.expectAgrees "the decode-layer oracle" t
              let probes = Tally.count "Probes" t
              Expect.isGreaterThan probes 80000 (sprintf "probes asked of both sides (%d)" probes)

              Expect.isEmpty
                  (Tally.findings "unresolved" t)
                  (sprintf
                      "a production refusal does not resolve in its input:\n%s"
                      (show (Tally.findings "unresolved" t)))

              let refusals = Tally.count "Refusals" t
              Expect.isGreaterThan refusals 3000 (sprintf "refusals held to resolvesIn (%d)" refusals)

              for code in
                  [ "MissingField"
                    "WrongKind"
                    "UnknownTag"
                    "OutOfRange"
                    "UndeclaredMember"
                    "LimitExceeded" ] do
                  Expect.isTrue
                      (Tally.reached "Codes" t |> Set.contains code)
                      (sprintf "the pool reached a %s refusal" code)

          testCase
              "the decode-layer oracle agrees with Codec's builds, writes and collecting and strict reads over generated shapes, valid and mutated"
          <| fun _ ->
              let t = codecPool 4360 700
              Differential.expectAgrees "the codec oracle" t
              let refused = Tally.count "Refused" t
              let writes = Tally.count "Writes" t
              let reads = Tally.count "Reads" t
              let roundTrips = Tally.count "RoundTrips" t
              Expect.isGreaterThan refused 20 (sprintf "declarations both sides refused to build (%d)" refused)
              Expect.isGreaterThan writes 1500 (sprintf "values written by both sides (%d)" writes)
              Expect.isGreaterThan reads 5000 (sprintf "inputs read by both sides (%d)" reads)
              Expect.isGreaterThan roundTrips 1000 (sprintf "written values read back as themselves (%d)" roundTrips)

          testCase
              "every refusal production's codecs answer is non-empty, first in the strict reader and resolves in its input"
          <| fun _ ->
              let t = codecPool 4361 700
              let statement = Tally.findings "statement" t
              let nonRoot = Tally.count "NonRoot" t
              Expect.isEmpty statement (sprintf "the theorems' statements fail on production:\n%s" (show statement))
              Expect.isGreaterThan nonRoot 500 (sprintf "refusal lists with a refusal below the root (%d)" nonRoot)

              for code in [ "MissingField"; "WrongKind"; "UnknownTag"; "OutOfRange"; "UndeclaredMember" ] do
                  Expect.isTrue
                      (Tally.reached "Codes" t |> Set.contains code)
                      (sprintf "the codecs reached a %s refusal" code)

          testCase "a model that drops a refusal's first step loses - on exactly the refusals below the root"
          <| fun _ ->
              // The go-red: the BLIND bridge drops the first step of every model refusal's path, which
              // is what a block that forgot its `under` would answer one level up. It must lose on
              // every refusal list holding a refusal below the root and on no other — so a green
              // comparison above is one that CAN fail.
              let t = codecPool 4362 700
              Expect.isGreaterThan (Tally.count "NonRoot" t) 500 "refusal lists with a refusal below the root"

              Expect.equal
                  (Tally.count "BlindLosses" t)
                  (Tally.count "NonRoot" t)
                  "the blind comparison loses on exactly the non-root refusal lists"

              let blindDecoders = decoderPool 437 400 dropFirstStep
              Differential.expectLoses "the blind decoder comparison" blindDecoders ]
