module Fuaran.Core.Tests.IdlThreeHostTests

// ---------------------------------------------------------------------------
// Phase 303 — the IDL emitter compiles what it emits, and the three hosts agree.
//
// Before this phase the suite proved the three hosts of a vocabulary — the reference
// interpreter (`Encode` / `Decode`), the generated F# module and the generated TypeScript
// module — agree on the shapes the sampler happens to draw, by BYTES, and compiled the
// generated F# of one certification vocabulary out of three. Both halves had a hole the
// second review pass walked through:
//
//   * the score vocabulary's generated F# did not compile (a field-less kind was emitted as
//     `{ }`), and nothing noticed, because the suite only regenerated it;
//   * byte identity is not value identity — a transparent case over an object-capable type
//     re-encodes to the same bytes as a DIFFERENT value on every host, and the interpreter
//     refused the WIRE_FORMAT §7 sentinels its own encoder writes, which no byte comparison
//     over finite floats could see.
//
// So this file holds: the drift guards for the two newly compiled fixtures; a standing
// THREE-WAY differential over every certification vocabulary, with an adversarial pass over
// the sampler's draws, asserting VALUE equality beside byte identity on every host; the
// declaration refusals that keep the transparent-case class out; the §7 decode direction;
// and the harden-policy entry check.
//
// VALUE equality is stated in one normal form (`valueKey` below): a value's fields in
// declared order with the presence rules applied (an absent optional is absent, an absent
// omit-at-default member is its default, a host-only member is not data), floats under the
// canonical float layout (so `-0.0` is `0`, and `NaN` is `NaN`), a JSON or hosted slot by its
// canonical rendering, and a MAP as its key set — a map's entry order is not part of its value
// (the "P1" map-order mismatch: the interpreter keeps a decoded map in wire order, the F#
// host in `Map` order, the TypeScript host in object order; the normal form is where that is
// settled, and DECISIONS records it). What the normal form keeps is the CASE of every union
// and the kind of every node, which is exactly what bytes lose.
// ---------------------------------------------------------------------------

open System
open System.IO
open Expecto
open Microsoft.FSharp.Reflection
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

// ---------------------------------------------------------------------------
// The value normal form.
// ---------------------------------------------------------------------------

let private floatKey (f: float) =
    Canonical.write Rendering.CrossHost (JFloat f)

let private fieldValue (name: string) (fields: (string * IdlValue) list) =
    fields
    |> List.tryPick (fun (n, v) ->
        match v with
        | VAbsent -> None
        | _ when n = name -> Some v
        | _ -> None)

/// One string per value: two values of a type are the same value exactly when their keys are
/// equal. See the header for what the form keeps and what it settles.
let rec valueKey (idl: Idl) (t: IdlType) (v: IdlValue) : string =
    match t, v with
    | TStr, VStr s -> "s" + Canonical.write Rendering.CrossHost (JStr s)
    | TInt, VInt i -> "i" + string i
    | TBool, VBool b -> if b then "true" else "false"
    | TFloat, VFloat f -> "f" + floatKey f
    | TFloat, VInt i -> "f" + floatKey (float i)
    | TEnum _, VEnum c -> "e" + Canonical.write Rendering.CrossHost (JStr c)
    | (TClosure | TFn _), _ -> "closure"
    | TOpaque, _ -> "opaque"
    | (TJson | THosted _), VJson j -> "j" + Canonical.write Rendering.CrossHost j
    | TList inner, VList xs -> "[" + (xs |> List.map (valueKey idl inner) |> String.concat ",") + "]"
    | TMap vt, VMap entries ->
        "{"
        + (entries
           |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))
           |> List.map (fun (k, ev) -> Canonical.write Rendering.CrossHost (JStr k) + ":" + valueKey idl vt ev)
           |> String.concat ",")
        + "}"
    | TRecord n, VRecord fs ->
        match idl.Records |> List.tryFind (fun r -> r.Name = n) with
        | Some r -> "R" + n + "(" + fieldsKey idl Map.empty r.Fields fs + ")"
        | None -> sprintf "?record %s" n
    | TUnion(n, args), VUnion(tag, fs) ->
        match idl.Unions |> List.tryFind (fun u -> u.Name = n) with
        | Some u ->
            match TypeParams.bind u args, u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
            | Some subst, Some c -> "U" + n + "." + tag + "(" + fieldsKey idl subst c.Fields fs + ")"
            | _ -> sprintf "?case %s.%s" n tag
        | None -> sprintf "?union %s" n
    | (TKind | TOp), VUnion(tag, fs) ->
        let owners = if t = TKind then idl.Kinds else idl.Ops

        match owners |> List.tryFind (fun k -> k.Tag = tag) with
        | Some k -> "K" + tag + "(" + fieldsKey idl Map.empty k.Fields fs + ")"
        | None -> sprintf "?tag %s" tag
    | TNode, VNode(id, tag, fs) -> nodeKey idl id [] tag fs
    | TNode, VNodeEnv(id, env, tag, fs) -> nodeKey idl id env tag fs
    | _ -> sprintf "?%A=%A" t v

and private fieldsKey
    (idl: Idl)
    (subst: Map<string, IdlType>)
    (decl: IdlField list)
    (fs: (string * IdlValue) list)
    : string =
    decl
    |> List.choose (fun f ->
        let ty = TypeParams.substitute subst f.Type

        match f.Opt, fieldValue f.Name fs with
        | HostOnly, _ -> None
        | Optional, None -> None
        | OmitDefault d, None -> Some(f.Name + "=" + valueKey idl ty d)
        | Required, None -> Some(f.Name + "=<missing>")
        | _, Some v -> Some(f.Name + "=" + valueKey idl ty v))
    |> String.concat ";"

and private nodeKey (idl: Idl) (id: string) env (tag: string) fs : string =
    match idl.Kinds |> List.tryFind (fun k -> k.Tag = tag) with
    | Some k ->
        "N"
        + Canonical.write Rendering.CrossHost (JStr id)
        + ":"
        + tag
        + "("
        + fieldsKey idl Map.empty k.Fields fs
        + ")env("
        + fieldsKey idl Map.empty idl.NodeFields env
        + ")"
    | None -> sprintf "?kind %s" tag

// ---------------------------------------------------------------------------
// The compiled host's value, read back into the interpreter's value model by reflection,
// guided by the vocabulary — the generated types are the vocabulary's shape, so the walk is
// the inverse of the type emitter: a record field is the field's name with its first letter
// upper-cased, an optional field is an `option`, a field-less declaration is a marker union.
// ---------------------------------------------------------------------------

let private pascal (s: string) =
    if s.Length = 0 then
        s
    else
        string (Char.ToUpperInvariant s[0]) + s.Substring 1

let private prop (o: obj) (name: string) : obj =
    match o.GetType().GetProperty name with
    | null -> failwithf "the compiled value of type %s has no member %s" (o.GetType().Name) name
    | p -> p.GetValue o

let private someOf (o: obj) : obj option =
    // `None` is null at runtime; `Some x` is a union value with one field.
    if isNull o then
        None
    else
        let _, fields = FSharpValue.GetUnionFields(o, o.GetType())
        Some fields[0]

let rec private toIdl (hosted: Map<string, obj -> JVal>) (idl: Idl) (t: IdlType) (o: obj) : IdlValue =
    let recur = toIdl hosted idl

    match t with
    | TStr -> VStr(unbox<string> o)
    | TInt -> VInt(unbox<int> o)
    | TBool -> VBool(unbox<bool> o)
    | TFloat -> VFloat(unbox<float> o)
    | TClosure
    | TFn _ -> VClosure
    | TOpaque -> VOpaque
    | TJson -> VJson(unbox<JVal> o)
    | THosted h ->
        match hosted.TryFind h.Encode with
        | Some enc -> VJson(enc o)
        | None -> failwithf "no host codec registered for %s" h.Encode
    | TEnum n ->
        let e = idl.Enums |> List.find (fun e -> e.Name = n)
        let case, _ = FSharpValue.GetUnionFields(o, o.GetType())
        VEnum(e.WireOf case.Name)
    | TList inner -> VList [ for x in (o :?> Collections.IEnumerable) -> recur inner x ]
    | TMap vt ->
        VMap [ for kv in (o :?> Collections.IEnumerable) -> unbox<string> (prop kv "Key"), recur vt (prop kv "Value") ]
    | TRecord n ->
        let r = idl.Records |> List.find (fun r -> r.Name = n)

        if List.isEmpty r.Fields then
            VRecord []
        else
            VRecord(fieldsOf hosted idl Map.empty r.Fields (fun f -> prop o (pascal f.Name)))
    | TUnion(n, args) ->
        let u = idl.Unions |> List.find (fun u -> u.Name = n)
        let subst = TypeParams.bind u args |> Option.defaultValue Map.empty
        let case, values = FSharpValue.GetUnionFields(o, o.GetType())
        let c = u.Cases |> List.find (fun c -> c.Tag = case.Name)

        let byName =
            List.zip (c.Fields |> List.map _.Name) (List.ofArray values) |> Map.ofList

        VUnion(case.Name, fieldsOf hosted idl subst c.Fields (fun f -> byName[f.Name]))
    | TNode ->
        let kindVal = prop o "Kind"
        let case, specs = FSharpValue.GetUnionFields(kindVal, kindVal.GetType())
        let k = idl.Kinds |> List.find (fun k -> k.Tag = case.Name)

        let fields =
            if List.isEmpty k.Fields then
                []
            else
                fieldsOf hosted idl Map.empty k.Fields (fun f -> prop specs[0] (pascal f.Name))

        let env =
            fieldsOf hosted idl Map.empty idl.NodeFields (fun f -> prop o (pascal f.Name))

        let id = unbox<string> (prop o "Id")

        match env with
        | [] -> VNode(id, k.Tag, fields)
        | _ -> VNodeEnv(id, env, k.Tag, fields)
    | TKind
    | TOp
    | TVar _ -> failwithf "no compiled value of %A" t

and private fieldsOf hosted idl subst (decl: IdlField list) (read: IdlField -> obj) =
    decl
    |> List.choose (fun f ->
        let ty = TypeParams.substitute subst f.Type

        match f.Opt with
        | HostOnly -> None
        | Optional -> someOf (read f) |> Option.map (fun x -> f.Name, toIdl hosted idl ty x)
        | Required
        | OmitDefault _ -> Some(f.Name, toIdl hosted idl ty (read f)))

// ---------------------------------------------------------------------------
// The adversarial pass: the sampler's draws with the shapes it does not reach planted in —
// non-finite and whole-valued floats at float slots (the §7 sentinels, and the layouts where
// a host's shortest round trip and the canonical one part), and map keys that collide with
// the envelope's and the discriminator's names, need escaping, sort differently under
// Ordinal and under JavaScript's integer-key rule, or are empty.
// ---------------------------------------------------------------------------

let private adversarialFloats =
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

let private adversarialKeys =
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
let rec private rewrite (idl: Idl) (f: IdlType -> IdlValue -> IdlValue option) (t: IdlType) (v: IdlValue) : IdlValue =
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

let private adversarial (idl: Idl) (rng: Random) : IdlValue -> IdlValue =
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
let private vectorsFor (idl: Idl) (hosted: Map<string, Random -> JVal>) (seed: int) (count: int) : IdlValue list =
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

// ---------------------------------------------------------------------------
// The three hosts.
// ---------------------------------------------------------------------------

/// A compiled generated module, behind the two entry points every one of them exports.
type private Compiled =
    { Decode: string -> Result<obj, DecodeError>
      Encode: obj -> string
      Hosted: Map<string, obj -> JVal> }

/// A certification vocabulary as the differential runs it: the vocabulary the interpreter and
/// the TypeScript host are generated from, and the compiled F# module beside it.
type private Certified =
    { Name: string
      Idl: Idl
      Compiled: Compiled
      HostedDraw: Map<string, Random -> JVal> }

/// The reference vocabulary's hosted `series` slot, drawn from the host type its prelude
/// codec reads (`float list`): finite floats only, since a non-finite float inside a verbatim
/// hosted value has no canonical rendering of its own and the interpreter refuses it (Phase 292).
let private seriesDraw (rng: Random) : JVal =
    let pool = [ 0.0; 1.0; -2.5; 2.0; 1e21; 5e-324; 0.1; -0.0 ]
    JArr [ for _ in 1 .. rng.Next 4 -> JFloat pool[rng.Next pool.Length] ]

let private certified: Certified list =
    [ { Name = "document"
        Idl = SecondDomainSpike.docIdl
        Compiled =
          { Decode = fun s -> DocGenerated.decodeNode s |> Result.map box
            Encode = fun o -> DocGenerated.encodeNode (unbox o)
            Hosted = Map.empty }
        HostedDraw = Map.empty }
      { Name = "score"
        Idl = ScoreDomainSpike.scoreIdl
        Compiled =
          { Decode = fun s -> ScoreGenerated.decodeNode s |> Result.map box
            Encode = fun o -> ScoreGenerated.encodeNode (unbox o)
            Hosted = Map.empty }
        HostedDraw = Map.empty }
      { Name = "reference"
        Idl = ReferenceIdl.refIdl
        Compiled =
          { Decode = fun s -> ReferenceGenerated.decodeNode s |> Result.map box
            Encode = fun o -> ReferenceGenerated.encodeNode (unbox o)
            Hosted = Map.ofList [ "encSeries", (fun o -> ReferencePrelude.encSeries (unbox o)) ] }
        HostedDraw = Map.ofList [ "encSeries", seriesDraw ] } ]

/// Run a harness under node; `None` when node is not on PATH.
let private runNode (script: string) : string option =
    let tmp =
        Path.Combine(Path.GetTempPath(), sprintf "fuaran-three-host-%s.mjs" (Guid.NewGuid().ToString "N"))

    File.WriteAllText(tmp, script)

    try
        match ChildProcess.run (ChildProcess.command "node" ("\"" + tmp + "\"")) with
        | Error _ -> None
        | Ok p ->
            let stdout = p.Stdout
            let stderr = p.Stderr

            if p.Exit <> 0 then
                failtestf "node failed running the three-host harness: %s" stderr

            Some stdout
    finally
        try
            File.Delete tmp
        with _ ->
            ()

/// The deep equality the TypeScript leg judges VALUE by: numbers by value with `NaN` equal to
/// itself, and an object's members order-free (a map's value is its key set). A member that
/// carries no data is the same as an absent one — `undefined`, and the stand-ins a sentinel or
/// host-only slot takes (the value emitter writes a function, the decoder `null`), which is the
/// `valueKey` rule that a closure slot is not data.
let private tsDeepEqual =
    """
function __eq(x, y) {
  if (typeof x === 'number' && typeof y === 'number') return x === y || (Number.isNaN(x) && Number.isNaN(y));
  if (typeof x === 'function' && typeof y === 'function') return true;
  if (x === y) return true;
  if (x === null || y === null || typeof x !== 'object' || typeof y !== 'object') return false;
  if (Array.isArray(x) !== Array.isArray(y)) return false;
  if (Array.isArray(x)) return x.length === y.length && x.every((v, i) => __eq(v, y[i]));
  const data = (o) => Object.keys(o).filter(k => o[k] !== undefined && o[k] !== null && typeof o[k] !== 'function');
  const kx = data(x);
  const ky = data(y);
  if (kx.length !== ky.length) return false;
  return kx.every(k => Object.prototype.hasOwnProperty.call(y, k) && __eq(x[k], y[k]));
}
"""

/// The TypeScript leg's answer for one vector: the bytes it writes for the AUTHORED value, and
/// either its refusal of the interpreter's bytes or the bytes it re-encodes them to beside
/// whether the value it decoded equals the interpreter's decoded value.
type private TsResult =
    { Authored: string
      Decoded: Result<string * bool, string>
      Shown: string }

let private tsLeg (idl: Idl) (rows: (IdlValue * IdlValue * string) list) : Map<int, TsResult> option =
    let lit (v: IdlValue) =
        match Gen.typescriptValue idl TNode v with
        | Ok src -> src
        | Error e -> failtestf "the TypeScript value emitter refused %A: %s" v (CodegenError.describe e)

    let tsModule =
        match Gen.typescriptModule idl (idl.Kinds |> List.map _.Tag) with
        | Ok src -> src
        | Error e -> failtestf "TypeScript codegen refused: %s" (CodegenError.describe e)

    let vectors =
        rows
        |> List.mapi (fun i (authored, decoded, bytes) ->
            sprintf
                "  [%d, %s, %s, %s],"
                i
                (lit authored)
                (lit decoded)
                (Canonical.write Rendering.CrossHost (JStr bytes)))
        |> String.concat "\n"

    let harness =
        tsModule
        + "\n"
        + tsDeepEqual
        + "\nconst __vectors = [\n"
        + vectors
        + "\n];\n"
        + "const __msg = (e) => (e && e.message ? e.message : String(e));\n"
        + "const __show = (v) => JSON.stringify(v, (k, x) => typeof x === 'function' ? '<fn>' : (typeof x === 'number' && !Number.isFinite(x)) ? String(x) : x);\n"
        + "for (const [i, a, d, b] of __vectors) {\n"
        + "  const out = { i };\n"
        + "  try { out.enc = encodeNode(a); } catch (e) { out.enc = 'THREW: ' + __msg(e); }\n"
        + "  const r = decodeNode(b);\n"
        + "  if (!r.ok) { out.refused = JSON.stringify(r.error); } else {\n"
        + "    try { out.re = encodeNode(r.value); } catch (e) { out.re = 'THREW: ' + __msg(e); }\n"
        + "    out.eq = __eq(r.value, d);\n"
        + "    if (!out.eq) { out.got = __show(r.value); out.want = __show(d); }\n"
        + "  }\n"
        + "  console.log(JSON.stringify(out));\n"
        + "}\n"

    runNode harness
    |> Option.map (fun stdout ->
        stdout.Replace("\r\n", "\n").Split('\n')
        |> Array.filter (fun l -> l <> "")
        |> Array.map (fun line ->
            use doc = Text.Json.JsonDocument.Parse line
            let root = doc.RootElement
            let str (name: string) = root.GetProperty(name).GetString()

            let decoded =
                match root.TryGetProperty "refused" with
                | true, r -> Error(r.GetString())
                | _ -> Ok(str "re", root.GetProperty("eq").GetBoolean())

            root.GetProperty("i").GetInt32(),
            let shown =
                match root.TryGetProperty "got", root.TryGetProperty "want" with
                | (true, g), (true, w) ->
                    sprintf " (TS read %s, the interpreter's value is %s)" (g.GetString()) (w.GetString())
                | _ -> ""

            { Authored = str "enc"
              Decoded = decoded
              Shown = shown })
        |> Map.ofArray)

let private noNode = "node is not on PATH: the TypeScript leg did not run"

/// One vocabulary through all three hosts. Returns every divergence, each naming the vector's
/// index, the host and what differed — empty is the pass — and how many vectors reached the
/// compiled and TypeScript legs.
let private differential (c: Certified) (vectors: IdlValue list) : string list * int =
    let idl = c.Idl
    let key = valueKey idl TNode

    let interp =
        vectors
        |> List.mapi (fun i v ->
            match Encode.encode idl v with
            | Error m -> i, v, Error("interpreter encode refused: " + m)
            | Ok bytes ->
                match Decode.decode idl bytes with
                | Error m -> i, v, Error(sprintf "interpreter refused its own bytes %s: %s" bytes m)
                | Ok d -> i, v, Ok(bytes, d))

    let interpFindings =
        interp
        |> List.collect (fun (i, v, r) ->
            match r with
            | Error m -> [ sprintf "#%d %s" i m ]
            | Ok(bytes, d) ->
                [ if key d <> key v then
                      sprintf "#%d interpreter VALUE: authored %s, decoded %s" i (key v) (key d)
                  match Encode.encode idl d with
                  | Ok again when again = bytes -> ()
                  | Ok again -> sprintf "#%d interpreter re-encode: %s then %s" i bytes again
                  | Error m -> sprintf "#%d interpreter re-encode refused: %s" i m ])

    let ok =
        interp
        |> List.choose (fun (i, v, r) ->
            match r with
            | Ok(bytes, d) -> Some(i, v, bytes, d)
            | Error _ -> None)

    let compiledFindings =
        ok
        |> List.collect (fun (i, _, bytes, d) ->
            match c.Compiled.Decode bytes with
            | Error e -> [ sprintf "#%d compiled F# refused %s: %s" i bytes (DecodeError.render e) ]
            | Ok typed ->
                [ let again = c.Compiled.Encode typed

                  if again <> bytes then
                      sprintf "#%d compiled F# BYTES: interpreter %s, F# %s" i bytes again

                  let projected = key (toIdl c.Compiled.Hosted idl TNode typed)

                  if projected <> key d then
                      sprintf "#%d compiled F# VALUE: interpreter %s, F# %s" i (key d) projected ])

    let tsFindings =
        match tsLeg idl (ok |> List.map (fun (_, v, bytes, d) -> v, d, bytes)) with
        | None -> [ noNode ]
        | Some results ->
            ok
            |> List.mapi (fun row (i, _, bytes, _) ->
                match results.TryFind row with
                | None -> [ sprintf "#%d TypeScript produced no result" i ]
                | Some r ->
                    [ if r.Authored <> bytes then
                          sprintf "#%d TypeScript authoring BYTES: interpreter %s, TS %s" i bytes r.Authored
                      match r.Decoded with
                      | Error m -> sprintf "#%d TypeScript refused %s: %s" i bytes m
                      | Ok(again, eq) ->
                          if again <> bytes then
                              sprintf "#%d TypeScript BYTES: interpreter %s, TS %s" i bytes again

                          if not eq then
                              sprintf "#%d TypeScript VALUE differs from the interpreter's for %s%s" i bytes r.Shown ])
            |> List.concat

    interpFindings @ compiledFindings @ tsFindings, List.length ok

let private report (name: string) (findings: string list) =
    if not (List.isEmpty findings) then
        failtestf
            "%s: %d divergence(s) across the three hosts; the first ten:\n  %s"
            name
            findings.Length
            (findings |> List.truncate 10 |> String.concat "\n  ")

// ---------------------------------------------------------------------------
// The drift guards for the two compiled fixtures this phase adds.
// ---------------------------------------------------------------------------

let private driftGuard (rel: string) (modName: string) (sup: GenSupport) (idl: Idl) =
    let path = Snapshots.repoFile rel

    if not (File.Exists path) then
        failtestf
            "%s not found — regenerate with: dotnet run --project tests/Fuaran.Core.Tests -- --regen-snapshots"
            rel

    match Gen.fsharpModuleWith sup modName idl (idl.Kinds |> List.map _.Tag) with
    | Error e -> failtestf "codegen refused %s: %s" rel (CodegenError.describe e)
    | Ok src ->
        Expect.equal
            (File.ReadAllText(path).Replace("\r\n", "\n"))
            src
            (sprintf
                "the generator no longer reproduces %s byte-for-byte — regenerate it with: dotnet run --project tests/Fuaran.Core.Tests -- --regen-snapshots"
                rel)

[<Tests>]
let compiledFixtures =
    testList
        "Phase 303 — every certification vocabulary's generated F# compiles"
        [ testCase "drift guard: the generator still reproduces the committed ScoreGenerated.fs" (fun _ ->
              driftGuard
                  "tests/Fuaran.Core.Tests/ScoreGenerated.fs"
                  "Fuaran.Core.Tests.ScoreGenerated"
                  GenSupport.Empty
                  ScoreDomainSpike.scoreIdl)

          testCase "drift guard: the generator still reproduces the committed ReferenceGenerated.fs" (fun _ ->
              driftGuard
                  "tests/Fuaran.Core.Tests/ReferenceGenerated.fs"
                  "Fuaran.Core.Tests.ReferenceGenerated"
                  ReferenceIdl.support.Support
                  ReferenceIdl.refIdl)

          testCase "a field-less kind is a marker type: it constructs, encodes and decodes" (fun _ ->
              let markers =
                  ScoreDomainSpike.scoreIdl.Kinds |> List.filter (fun k -> List.isEmpty k.Fields)

              Expect.isGreaterThanOrEqual markers.Length 5 "the score vocabulary carries its five marker kinds"

              let node = ScoreGenerated.mkFermata "f1"
              let bytes = ScoreGenerated.encodeNode node

              Expect.equal
                  (Encode.encode ScoreDomainSpike.scoreIdl (VNode("f1", "Fermata", [])))
                  (Ok bytes)
                  "the compiled host and the interpreter write one marker node"

              match ScoreGenerated.decodeNode bytes with
              | Ok back -> Expect.equal (ScoreGenerated.encodeNode back) bytes "and it reads back as itself"
              | Error e -> failtestf "the compiled host refused its own marker node: %s" (DecodeError.render e))

          testCase "a field-less RECORD is a marker type too, in the one type emitter" (fun _ ->
              let idl =
                  { ScoreDomainSpike.scoreIdl with
                      Records =
                          { Name = "Empty"
                            Fields = []
                            Annotations = Annotations.Empty }
                          :: ScoreDomainSpike.scoreIdl.Records }

              match Gen.fsharpTypes idl with
              | Ok src ->
                  Expect.stringContains src "Empty =\n    | Empty" "a marker type, not `{ }`"
                  Expect.isFalse (src.Contains "{\n\n    }") "no empty record body anywhere"
              | Error e -> failtestf "the type emitter refused: %s" (CodegenError.describe e)) ]

// ---------------------------------------------------------------------------
// The standing differential.
// ---------------------------------------------------------------------------

/// Vectors per vocabulary before the adversarial pass doubles them. Seeded per vocabulary.
let private perVocabulary = 600

let private seedOf (c: Certified) = 20261002 + c.Name.Length

/// An op holder: the vocabulary plus one kind whose only field is an op, so the sampler —
/// which draws nodes — draws ops through it. Nothing is added to the vocabulary's op set;
/// the holder vocabulary is returned beside the ops, since a drawn bare kind may be it.
let private opsOf (idl: Idl) (seed: int) (count: int) : Idl * IdlValue list =
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

let private agreeOn (c: Certified) =
    testCase
        (sprintf "%s: interpreter, compiled F# and TypeScript agree on every drawn and adversarial vector" c.Name)
        (fun _ ->
            let vectors = vectorsFor c.Idl c.HostedDraw (seedOf c) perVocabulary
            let findings, compared = differential c vectors

            if findings = [ noNode ] then
                skiptest "node not on PATH — the three-way differential's TypeScript leg cannot run"

            report c.Name findings

            Expect.equal compared (2 * perVocabulary) "every vector reached all three hosts (none refused at encode)")

[<Tests>]
let threeWay =
    testList
        "Phase 303 — the three hosts agree by VALUE and by BYTES"
        [ for c in certified do
              agreeOn c

          testCase "the adversarial pass reaches what it claims: sentinels, whole floats and hostile map keys" (fun _ ->
              // Non-degeneracy: a pass that planted nothing would certify nothing beyond the sampler.
              let wires =
                  certified
                  |> List.collect (fun c ->
                      vectorsFor c.Idl c.HostedDraw (seedOf c) perVocabulary
                      |> List.choose (fun v -> Encode.encode c.Idl v |> Result.toOption))
                  |> String.concat "\n"

              for token in [ "\"NaN\""; "\"Infinity\""; "\"-Infinity\""; "1E+21"; "\"$type\":"; "\"\":" ] do
                  Expect.stringContains wires token (sprintf "some vector carries %s" token))

          testCase "negative control: a value change under identical bytes is caught by the value key" (fun _ ->
              // The class the differential exists for: two values, one byte string. The key keeps
              // the CASE, which bytes lose; and it lets a map's order go, which bytes cannot.
              let idl = ReferenceIdl.refIdl
              let t = TUnion("Text", [])
              let a = VUnion("Inline", [ "text", VStr "x" ])
              let b = VUnion("Lookup", [ "key", VStr "x" ])
              Expect.notEqual (valueKey idl t a) (valueKey idl t b) "distinct cases are distinct values"

              Expect.equal
                  (valueKey idl (TMap TStr) (VMap [ "b", VStr "1"; "a", VStr "2" ]))
                  (valueKey idl (TMap TStr) (VMap [ "a", VStr "2"; "b", VStr "1" ]))
                  "and a map's value is its key set, whatever order it was written or read in")

          testCase "root ops: the interpreter round-trips every drawn op by value and by bytes" (fun _ ->
              // The generated F# and TypeScript layers carry no op root — an op slot is refused by
              // their emitters (Phase 195) — so an op is certified on the interpreter here and,
              // in IdlSchemaValidatorTests, against the emitted schema.
              // Encoded under the holder vocabulary: an `Insert`'s bare kind may be the holder.
              let idl, ops = opsOf ReferenceIdl.refIdl 303 200
              Expect.equal ops.Length 200 "the holder drew an op per node"

              ops
              |> List.iteri (fun i op ->
                  match Encode.encodeOp idl op with
                  | Error m -> failtestf "op #%d did not encode: %s" i m
                  | Ok bytes ->
                      match Decode.decodeOp idl bytes with
                      | Error m -> failtestf "op #%d: the interpreter refused its own bytes %s: %s" i bytes m
                      | Ok back ->
                          Expect.equal (valueKey idl TOp back) (valueKey idl TOp op) (sprintf "op #%d by value" i)
                          Expect.equal (Encode.encodeOp idl back) (Ok bytes) (sprintf "op #%d by bytes" i)))

          testCase
              "a required-field migration pair: every host REFUSES a document missing a member it requires"
              (fun _ ->
                  // The older vocabulary did not have `Note.body`; the current one requires it. A
                  // document written under the older one must be refused by all three current hosts —
                  // never filled, never read as a different value.
                  let current = ReferenceIdl.refIdl

                  let withNote (fields: IdlField list -> IdlField list) =
                      { current with
                          Kinds =
                              current.Kinds
                              |> List.map (fun k ->
                                  if k.Tag = "Note" then
                                      { k with Fields = fields k.Fields }
                                  else
                                      k) }

                  let older = withNote (fun _ -> [])

                  let bytes =
                      match Encode.encode older (VNode("n1", "Note", [])) with
                      | Ok b -> b
                      | Error m -> failtestf "the older vocabulary did not encode: %s" m

                  // Phase 337 — refused with ONE code and ONE path: the absent member, named.
                  let missingBody =
                      Error(DecodeCode.MissingField, [ PathSegment.Key "kind"; PathSegment.Key "body" ])

                  let codeAndPath (r: Result<'T, DecodeError>) =
                      r |> Result.map ignore |> Result.mapError (fun e -> e.Code, e.Path)

                  Expect.equal
                      (codeAndPath (Decode.decodeDetailed current bytes))
                      missingBody
                      "the interpreter refuses it"

                  Expect.equal
                      (codeAndPath (ReferenceGenerated.decodeNode bytes))
                      missingBody
                      "the compiled F# host refuses it, at the same member"

                  let tsModule =
                      match Gen.typescriptModule current (current.Kinds |> List.map _.Tag) with
                      | Ok src -> src
                      | Error e -> failtestf "TypeScript codegen refused: %s" (CodegenError.describe e)

                  match
                      runNode (
                          tsModule
                          + "\nconst __r = decodeNode("
                          + Canonical.write Rendering.CrossHost (JStr bytes)
                          + ");\nconsole.log(JSON.stringify(__r.ok ? 'accepted' : [__r.error.code, __r.error.path]));\n"
                      )
                  with
                  | None -> skiptest "node not on PATH"
                  | Some out ->
                      Expect.equal
                          (out.Trim())
                          """["MissingField",["kind","body"]]"""
                          "the TypeScript host refuses it, at the same member"

                  // And the other direction: a member the current vocabulary does not know is
                  // tolerated, and the value read is the one the current vocabulary declares.
                  let newer =
                      withNote (fun fs ->
                          fs
                          @ [ { Name = "zz"
                                Type = TStr
                                Opt = Required
                                Aliases = []
                                Presence = None
                                Annotations = Annotations.Empty } ])

                  let body = "body", VUnion("Inline", [ "text", VStr "t" ])
                  let note = VNode("n1", "Note", [ body ])

                  let newerBytes =
                      match Encode.encode newer (VNode("n1", "Note", [ body; "zz", VStr "extra" ])) with
                      | Ok b -> b
                      | Error m -> failtestf "the newer vocabulary did not encode: %s" m

                  match Decode.decode current newerBytes, ReferenceGenerated.decodeNode newerBytes with
                  | Ok d, Ok typed ->
                      Expect.equal
                          (valueKey current TNode d)
                          (valueKey current TNode note)
                          "the interpreter reads the declared value"

                      Expect.equal
                          (valueKey current TNode (toIdl Map.empty current TNode typed))
                          (valueKey current TNode note)
                          "and so does the compiled F# host"
                  | other -> failtestf "a member the vocabulary does not declare was refused: %A" other) ]

// ---------------------------------------------------------------------------
// The §7 direction: the interpreter reads back what its encoder writes at a float slot.
// ---------------------------------------------------------------------------

[<Tests>]
let section7 =
    testList
        "Phase 303 — the interpreter accepts the §7 sentinels its hosts accept"
        [ testCase "a float slot decodes NaN, Infinity and -Infinity, and re-encodes them" (fun _ ->
              let idl = ReferenceIdl.refIdl

              for token, expected in [ "NaN", nan; "Infinity", infinity; "-Infinity", -infinity ] do
                  let point = VRecord [ "x", VFloat expected; "y", VFloat 1.0 ]

                  match
                      Encode.encode
                          idl
                          (VNode(
                              "m",
                              "Measure",
                              [ "label", VUnion("Inline", [ "text", VStr "t" ])
                                "origin", point
                                "raw", VOpaque
                                "series", VJson(JArr []) ]
                          ))
                  with
                  | Error m -> failtestf "the interpreter would not encode %s: %s" token m
                  | Ok wire ->
                      Expect.stringContains wire ("\"x\":\"" + token + "\"") "the encoder writes the §7 token"

                      match Decode.decode idl wire with
                      | Ok v ->
                          match v with
                          | VNode(_, _, fields) ->
                              Expect.equal
                                  (valueKey
                                      idl
                                      (TRecord "Point")
                                      (fields |> List.find (fun (n, _) -> n = "origin") |> snd))
                                  (valueKey idl (TRecord "Point") point)
                                  (sprintf "%s reads as the non-finite float" token)
                          | other -> failtestf "decoded %A" other

                          Expect.equal (Encode.encode idl v) (Ok wire) (sprintf "%s re-encodes byte-identically" token)
                      | Error m -> failtestf "the interpreter refused the §7 token %s it wrote: %s" token m)

          testCase "§7 stops at the float slot: an int slot and a string slot do not widen" (fun _ ->
              let idl = ReferenceIdl.refIdl
              Expect.isError (Decode.value idl TInt (JStr "NaN")) "an int slot refuses the token"
              Expect.equal (Decode.value idl TStr (JStr "NaN")) (Ok(VStr "NaN")) "a string slot reads the string"
              Expect.isError (Decode.value idl TFloat (JStr "nan")) "and only the exact three tokens are sentinels")

          testCase "an artifact with a non-finite declared default reads back what it wrote" (fun _ ->
              let idl =
                  { ReferenceIdl.refIdl with
                      Defaults =
                          ReferenceIdl.refIdl.Defaults
                          @ [ { Kind = "Measure"
                                Field = "ratio"
                                Value = VFloat nan } ] }

              match Artifact.parse (Artifact.render idl) with
              | Ok back ->
                  let d = back.Defaults |> List.find (fun d -> d.Field = "ratio")

                  match d.Value with
                  | VFloat f -> Expect.isTrue (Double.IsNaN f) "the NaN default survives its own bytes"
                  | other -> failtestf "the default came back as %A" other

                  Expect.equal (Artifact.render back) (Artifact.render idl) "and renders the same bytes again"
              | Error m -> failtestf "Artifact.parse refused the artifact Artifact.render wrote: %s" m) ]

// ---------------------------------------------------------------------------
// The declaration refusals the transparent-case class rests on (Phase 292 shipped them;
// pinned here at the loading boundary this phase's acceptance names).
// ---------------------------------------------------------------------------

let private reqField (name: string) (t: IdlType) : IdlField =
    { Name = name
      Type = t
      Opt = Required
      Aliases = []
      Presence = None
      Annotations = Annotations.Empty }

[<Tests>]
let declarationRefusals =
    testList
        "Phase 303 — an object-capable transparent case and an envelope-named field are refused at ofJson"
        [ testCase "a transparent case over json is refused when the artifact is loaded" (fun _ ->
              let lit: IdlUnion =
                  { TagRule = Fuaran.Core.Idl.UnionTagRule.CaseName
                    Name = "Lit"
                    Params = []
                    Cases =
                      [ { Hosted = None
                          Tag = "Lit"
                          Fields = [ reqField "value" TJson ]
                          Annotations = Annotations.Empty }
                        { Hosted = None
                          Tag = "Ref"
                          Fields = [ reqField "name" TStr ]
                          Annotations = Annotations.Empty } ]
                    Annotations = Annotations.Empty }

              let idl =
                  { ReferenceIdl.refIdl with
                      Unions = lit :: ReferenceIdl.refIdl.Unions
                      Harden =
                          { ReferenceIdl.refIdl.Harden with
                              TransparentUnions = [ "Lit", "Lit" ] } }

              match Artifact.parse (Artifact.render idl) with
              | Ok _ -> failtest "a transparent case whose payload can be an object was loaded"
              | Error m -> Expect.stringContains m "Lit" "the refusal names the case")

          testCase
              "an envelope field named id or kind is refused when the artifact is loaded, under every shape"
              (fun _ ->
                  for shape in [ NodeEnvelopeShape.NestedKind; NodeEnvelopeShape.FlatKind ] do
                      for name in [ "id"; "kind" ] do
                          let idl =
                              { ReferenceIdl.refIdl with
                                  Wire =
                                      { ReferenceIdl.refIdl.Wire with
                                          NodeEnvelope = shape
                                          Discriminator =
                                              (if shape = NodeEnvelopeShape.FlatKind then
                                                   "kind"
                                               else
                                                   "$type") }
                                  NodeFields =
                                      ReferenceIdl.refIdl.NodeFields
                                      @ [ { reqField name TStr with
                                              Opt = Optional
                                              Aliases = []
                                              Presence = None } ] }

                          Expect.isError
                              (Artifact.parse (Artifact.render idl))
                              (sprintf "an envelope field '%s' under %A" name shape)) ]

// ---------------------------------------------------------------------------
// Harden-policy entries: a declared field is never silently unsanitised.
// ---------------------------------------------------------------------------

let private policy (urls: (string * string) list) (markdown: (string * string) list) : Trust.Policy =
    { Allowlist = []
      UrlFields = Set.ofList urls
      MarkdownFields = Set.ofList markdown }

/// `refIdl` plus a kind whose `href` sits inside a RECORD — the nested-`javascript:` probe — and
/// a kind carrying a plain-string URL and a plain-string markdown body.
let private hardenIdl: Idl =
    { ReferenceIdl.refIdl with
        Records =
            { Name = "Target"
              Fields = [ reqField "href" TStr ]
              Annotations = Annotations.Empty }
            :: ReferenceIdl.refIdl.Records
        Kinds =
            ReferenceIdl.refIdl.Kinds
            @ [ { Tag = "Anchor"
                  Category = "content"
                  Fields = [ reqField "target" (TRecord "Target") ]
                  Annotations = Annotations.Empty }
                { Tag = "Plain"
                  Category = "content"
                  Fields = [ reqField "body" TStr; reqField "url" TStr ]
                  Annotations = Annotations.Empty } ] }

let private refusedEntry (r: Result<unit, CodegenError>) (entry: string) =
    match r with
    | Error(CodegenError.UnsupportedConstruct(construct, _, _)) ->
        Expect.stringContains construct entry "the refusal names the entry"
    | other -> failtestf "expected the entry %s to be refused, got %A" entry other

[<Tests>]
let hardenEntries =
    testList
        "Phase 303 — a harden-policy entry the floor cannot reach is refused"
        [ testCase "the reference policy's own entries are admitted" (fun _ ->
              Expect.isOk
                  (Trust.checkHardenPolicy ReferenceIdl.refIdl (policy [ "Link", "href" ] [ "Note", "body" ]))
                  "a Slot<str> URL and a Text markdown body are the shapes the floor rewrites")

          testCase "an entry naming no kind, or no field of it, is refused" (fun _ ->
              refusedEntry (Trust.checkHardenPolicy hardenIdl (policy [ "Nope", "href" ] [])) "'Nope', 'href'"
              refusedEntry (Trust.checkHardenPolicy hardenIdl (policy [ "Link", "nope" ] [])) "'Link', 'nope'"
              refusedEntry (Trust.checkHardenPolicy hardenIdl (policy [] [ "Note", "nope" ])) "'Note', 'nope'")

          testCase
              "an entry naming a field the floor cannot sanitise is refused (record, wrong union, host-only)"
              (fun _ ->
                  refusedEntry
                      (Trust.checkHardenPolicy hardenIdl (policy [ "Anchor", "target" ] []))
                      "'Anchor', 'target'"
                  // `Text` has no `Fixed` case: a URL entry on it would never be sanitised.
                  refusedEntry (Trust.checkHardenPolicy hardenIdl (policy [ "Link", "label" ] [])) "'Link', 'label'"
                  // `Slot` has no `Inline` case: a markdown entry on it would never be scrubbed.
                  refusedEntry (Trust.checkHardenPolicy hardenIdl (policy [] [ "Link", "href" ])) "'Link', 'href'"

                  refusedEntry
                      (Trust.checkHardenPolicy hardenIdl (policy [ "Embed", "onMount" ] []))
                      "'Embed', 'onMount'")

          testCase "the nested javascript: probe is refused rather than passed verbatim" (fun _ ->
              let node =
                  VNode("a1", "Anchor", [ "target", VRecord [ "href", VStr "javascript:alert(1)" ] ])

              match Trust.harden hardenIdl (policy [ "Anchor", "target" ] []) node with
              | Ok v -> failtestf "the policy was admitted and the probe came back %A" v
              | Error _ -> ())

          testCase "a str-typed URL or markdown field is sanitised directly" (fun _ ->
              let node =
                  VNode(
                      "p1",
                      "Plain",
                      [ "body", VStr "<script>alert(1)</script>hi"
                        "url", VStr "javascript:alert(1)" ]
                  )

              match Trust.harden hardenIdl (policy [ "Plain", "url" ] [ "Plain", "body" ]) node with
              | Ok(VNode(_, _, fields)) ->
                  let str name =
                      match fields |> List.tryFind (fun (n, _) -> n = name) with
                      | Some(_, VStr s) -> s
                      | other -> failtestf "field %s came back %A" name other

                  Expect.equal (str "url") (Sanitize.sanitizeUrlOrBlank "javascript:alert(1)") "the URL is blanked"
                  Expect.isFalse ((str "body").Contains "<script") "the markdown is scrubbed"
              | other -> failtestf "harden refused or reshaped the node: %A" other) ]

// ---------------------------------------------------------------------------
// The differential's vectors, for the schema leg (IdlSchemaValidatorTests): the schema is
// certified over exactly the documents the three hosts are.
// ---------------------------------------------------------------------------

/// Every certification vocabulary with its drawn and adversarial wires.
let certificationWires () : (string * Idl * string list) list =
    certified
    |> List.map (fun c ->
        c.Name,
        c.Idl,
        vectorsFor c.Idl c.HostedDraw (seedOf c) perVocabulary
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

let private vectorsPerModel = 12

/// The committed module's name and path.
let vectorsModuleName = "VocabularyVectors"

/// Whether a wire carries one of the adversarial pass's map keys that no vocabulary member is
/// spelled as — the vectors that put a model's map members under the hostile keys. Sorted first
/// (stably), so the facts carry them rather than whichever draws happen to come first.
let private hostileKey (wire: string) =
    [ ""; "a\"b"; "\\"; "\u0000"; "10"; "9"; "0" ]
    |> List.exists (fun k -> wire.Contains(Canonical.write Rendering.CrossHost (JStr k) + ":"))

let private vectorModels () : VectorModel list =
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

let private vectorsFile () =
    Snapshots.repoFile ("proofs/" + vectorsModuleName + ".fst")

/// `--emit-fstar`'s share: rewrite `proofs/VocabularyVectors.fst`.
let emitVectors () : int =
    match vectorsText () with
    | Error e ->
        eprintfn "--emit-fstar: %s" (CodegenError.describe e)
        2
    | Ok text ->
        File.WriteAllText(vectorsFile (), text.Replace("\r\n", "\n"), Text.UTF8Encoding false)
        printfn "regenerated %s" (vectorsFile ())
        0

[<Tests>]
let fstarFacts =
    testList
        "Phase 303 — the F* target writes the interpreter's vectors as normaliser facts"
        [ testCase "generation diff: proofs/VocabularyVectors.fst is the generator's output" (fun _ ->
              match vectorsText () with
              | Error e -> failtestf "the F* target refused the vectors: %s" (CodegenError.describe e)
              | Ok text ->
                  let path = vectorsFile ()

                  if not (File.Exists path) then
                      failtestf
                          "%s not found — regenerate with: dotnet run --project tests/Fuaran.Core.Tests -- --emit-fstar"
                          path

                  Expect.equal
                      (File.ReadAllText(path).Replace("\r\n", "\n"))
                      (text.Replace("\r\n", "\n"))
                      "VECTOR DRIFT: the committed facts are not what the generator writes — regenerate with: dotnet run --project tests/Fuaran.Core.Tests -- --emit-fstar")

          testCase "every certification model carries its full set of facts, adversarial ones among them" (fun _ ->
              let models = vectorModels ()

              for m in models do
                  Expect.equal
                      m.Wires.Length
                      vectorsPerModel
                      (sprintf "%s: the target admitted enough vectors" m.Model)

              Expect.isTrue
                  (models |> List.exists (fun m -> m.Wires |> List.exists hostileKey))
                  "some fact puts a model's map member under an adversarial key"

              for m in models do
                  Expect.equal (List.distinct m.Wires) m.Wires (sprintf "%s: no fact is repeated" m.Model))

          testCase "the target REFUSES a vector its model cannot express, naming it, rather than dropping it" (fun _ ->
              // `Measure` is the reference model's one refused kind (its numeric default has no F*
              // literal): a vector holding one is refused by name, never emitted or skipped.
              let idl = ReferenceIdl.refIdl

              let wire =
                  match Encode.encode idl ReferenceIdl.measureAtDefault with
                  | Ok w -> w
                  | Error m -> failtestf "the probe did not encode: %s" m

              match
                  FStarTarget.vectorsModule
                      "Probe"
                      [ { Model = "Vocabulary"
                          Prefix = "R"
                          Idl = idl
                          Wires = [ wire ] } ]
              with
              | Ok _ -> failtest "a node of a kind the model does not declare was emitted into a fact"
              | Error e ->
                  let said = CodegenError.describe e
                  Expect.stringContains said "vector 0" "the refusal names the vector"
                  Expect.stringContains said "Measure" "and the kind") ]

// ---------------------------------------------------------------------------
// Phase 304 — an int slot's RANGE, at every int slot the certification vocabularies carry.
//
// The vectors above are AUTHORED values, and an authored `VInt` is a 32-bit int, so no vector
// ever put an integral number outside that range on the wire — and the generated TypeScript
// decoder accepted one (2^31, say) that the interpreter and the compiled F# host refuse. The
// evolution differential found it by narrowing a float slot to `int`; this block reaches the
// same class at any int slot. Each vector is a drawn node whose FIRST int slot is set to a
// marker, encoded, and the marker's digits replaced by an out-of-range literal: all three hosts
// must refuse. The control is the same bytes with the marker left in, which all three accept,
// so a refusal here is about the range and nothing else.
// ---------------------------------------------------------------------------

/// A value no sampler draws, so its digits locate the planted slot in the bytes.
let private intMarker = 1987654321

let private outOfRangeInts = [ "2147483648"; "-2147483649"; "9007199254740992" ]

/// The node with its first int slot (in the walk's order) set to the marker; `None` when the
/// node carries no int slot on the wire.
let private plantMarker (idl: Idl) (v: IdlValue) : IdlValue option =
    let mutable planted = false

    let out =
        rewrite
            idl
            (fun t x ->
                match t, x with
                | TInt, VInt _ when not planted ->
                    planted <- true
                    Some(VInt intMarker)
                | _ -> None)
            TNode
            v

    if planted then Some out else None

/// The TypeScript decoder of `idl` over raw documents: `true` per document it accepted.
let private tsAccepts (idl: Idl) (docs: string list) : bool list option =
    let tsModule =
        match Gen.typescriptModule idl (idl.Kinds |> List.map _.Tag) with
        | Ok src -> src
        | Error e -> failtestf "TypeScript codegen refused: %s" (CodegenError.describe e)

    let harness =
        tsModule
        + "\nconst __docs = [\n"
        + (docs
           |> List.map (fun d -> "  " + Canonical.write Rendering.CrossHost (JStr d) + ",")
           |> String.concat "\n")
        + "\n];\n"
        + "for (const d of __docs) console.log(decodeNode(d).ok ? 'ok' : 'refused');\n"

    runNode harness
    |> Option.map (fun out ->
        out.Replace("\r\n", "\n").Split('\n')
        |> Array.filter (fun l -> l <> "")
        |> Array.map (fun l -> l = "ok")
        |> List.ofArray)

[<Tests>]
let intRange =
    testList
        "Phase 304 — every host refuses an integral number outside the int range at an int slot"
        [ testCase "an out-of-range integer at an int slot is refused by all three hosts; the in-range control by none"
          <| fun _ ->
              let rows =
                  [ for c in certified do
                        for v in vectorsFor c.Idl c.HostedDraw (seedOf c) 60 do
                            match plantMarker c.Idl v with
                            | None -> ()
                            | Some planted ->
                                match Encode.encode c.Idl planted with
                                | Error _ -> ()
                                | Ok bytes ->
                                    let marker = string intMarker
                                    // Exactly one occurrence, so the replacement lands at the slot.
                                    if bytes.Split(marker).Length = 2 then
                                        yield c, bytes, [ for lit in outOfRangeInts -> bytes.Replace(marker, lit) ] ]

              let vocabularies = rows |> List.map (fun (c, _, _) -> c.Name) |> List.distinct

              Expect.isGreaterThan
                  rows.Length
                  20
                  (sprintf "vectors reached an int slot (in %s)" (String.concat ", " vocabularies))

              let findings =
                  [ for (c, control, bad) in rows do
                        if Result.isError (Decode.decode c.Idl control) then
                            yield sprintf "%s: the interpreter refused the in-range control %s" c.Name control

                        if Result.isError (c.Compiled.Decode control) then
                            yield sprintf "%s: the compiled F# host refused the in-range control %s" c.Name control

                        for b in bad do
                            if Result.isOk (Decode.decode c.Idl b) then
                                yield sprintf "%s: the interpreter ACCEPTED %s" c.Name b

                            if Result.isOk (c.Compiled.Decode b) then
                                yield sprintf "%s: the compiled F# host ACCEPTED %s" c.Name b ]

              report "int range (interpreter and compiled F#)" findings

              let tsFindings =
                  [ for c in certified do
                        let mine = rows |> List.filter (fun (r, _, _) -> r.Name = c.Name)

                        if not mine.IsEmpty then
                            let docs = mine |> List.collect (fun (_, control, bad) -> control :: bad)

                            match tsAccepts c.Idl docs with
                            | None -> yield noNode
                            | Some answers ->
                                let expected =
                                    mine
                                    |> List.collect (fun (_, _, bad) -> true :: (bad |> List.map (fun _ -> false)))

                                for (d, want, got) in List.zip3 docs expected answers do
                                    if want <> got then
                                        yield
                                            sprintf
                                                "%s: TypeScript %s %s"
                                                c.Name
                                                (if got then "ACCEPTED" else "refused the control")
                                                d ]

              if tsFindings |> List.contains noNode then
                  skiptest "node not on PATH — the TypeScript leg of the int-range block cannot run"

              report "int range (TypeScript)" tsFindings ]

// ---------------------------------------------------------------------------
// Phase 513 — the interpreter's readers switch on one lowering.
// ---------------------------------------------------------------------------

/// The lines of `src/Fuaran.Core.Idl/<file>` that are code, not comment.
let private codeLines (file: string) : (int * string) list =
    File.ReadAllLines(Snapshots.repoFile ("src/Fuaran.Core.Idl/" + file))
    |> Array.toList
    |> List.mapi (fun i l -> i + 1, l)
    |> List.filter (fun (_, l) -> not (l.TrimStart().StartsWith "//"))

/// A match arm on a declared `IdlType` case: what the lowering exists to decide once.
let private idlTypeArm =
    Text.RegularExpressions.Regex
        @"^\s*\|\s*\(?T(Str|Int|Bool|Float|Enum|Union|Var|Node|List|Closure|Fn|Opaque|Json|Hosted|Record|Map|Kind|Op)\b"

/// The findings the guard reports over `files`: an `IdlType` arm in a reader, and a sentinel
/// spelled outside the lowering.
let private loweringFindings (files: string list) (sentinelOwners: string list) : string list =
    [ for file in files do
          for n, l in codeLines file do
              if idlTypeArm.IsMatch l then
                  sprintf "%s:%d matches on a declared IdlType case: %s" file n (l.Trim())

      for file in sentinelOwners do
          for n, l in codeLines file do
              if l.Contains "\"<closure>\"" || l.Contains "\"<opaque>\"" then
                  sprintf "%s:%d spells a sentinel the lowering owns: %s" file n (l.Trim()) ]

[<Tests>]
let oneLowering =
    testList
        "Phase 513 — the encoder, the decoder and the sampler switch on the one lowered wire form"
        [ testCase
              "no reader re-derives a wire form from the declared type, and the sentinels are spelled once"
              (fun _ ->
                  let readers = [ "Encode.fs"; "Decode.fs"; "Sample.fs" ]

                  let sentinelOwners =
                      [ "Encode.fs"; "Decode.fs"; "Sample.fs"; "Declare.fs"; "Artifact.fs" ]

                  let findings = loweringFindings readers sentinelOwners

                  Expect.isEmpty
                      findings
                      "a reader switches on WireForm.ofType (WireForm.fs), never on the declared IdlType: \
                   a second derivation is how the hosts came to disagree about one slot"

                  // The guard sees what it guards: the lowering itself is full of declared-type arms and
                  // spells both sentinels, so the same sweep over it must report both kinds of finding.
                  let control = loweringFindings [ "WireForm.fs" ] [ "WireForm.fs" ]

                  Expect.isTrue
                      (control |> List.exists (fun f -> f.Contains "IdlType case"))
                      "control: the sweep finds the declared-type arms in WireForm.fs"

                  Expect.isTrue
                      (control |> List.exists (fun f -> f.Contains "sentinel"))
                      "control: the sweep finds the sentinel spellings in WireForm.fs") ]

// ---------------------------------------------------------------------------
// Phase 550 — what a derived seam codec needs, shown on one test vocabulary across the three
// hosts: the tag-naming rule, the codec-only module over host-declared types, the typed hosted
// decoder (at a slot and at a CASE, the query predicate's three-member literal over a two-field
// host case), the generic payload, and the strict-members reading.
// ---------------------------------------------------------------------------

module private Seam550 =
    let private ann = Annotations.Empty

    let private fld name t opt : IdlField =
        { Name = name
          Type = t
          Opt = opt
          Aliases = []
          Presence = None
          Annotations = ann }

    let private req name t = fld name t Required

    let private case tag fields : IdlUnionCase =
        { Tag = tag
          Fields = fields
          Annotations = ann
          Hosted = None }

    let private union name ps cases : IdlUnion =
        { Name = name
          Params = ps
          Cases = cases
          Annotations = ann
          TagRule = UnionTagRule.LowerFirst }

    let typed (fs: string) (enc: string) (dec: string) : HostedCodec =
        { FSharp = fs
          Encode = enc
          Decode = dec
          Wire = None
          Format = None
          Refusal = HostedRefusal.Typed }

    let placement =
        union
            "Placement"
            []
            [ case "BuildTime" []
              case "Server" []
              case "ClientIsland" [ req "island" (TEnum "IslandKind") ] ]

    let deferred =
        union
            "Deferred"
            [ "T" ]
            [ case "Pending" []
              case "Ready" [ req "value" (TVar "T") ]
              case "Failed" [ req "message" TStr ] ]

    let fault =
        union
            "Fault"
            []
            [ case
                  "EmptySpace"
                  [ req "addr" TStr
                    req "space" (THosted(typed "Host.Space" "Host.encSpace" "Host.decSpace")) ]
              case "NonFiniteBound" [ req "addr" TStr ] ]

    /// Query's `ColumnPredicate` shape: `equalTo` writes `column`, `type` and `value`, three members
    /// over a host case of two fields, the value read at the type its sibling states.
    let predicate =
        union
            "Predicate"
            []
            [ { case "EqualTo" [ req "column" TStr; req "type" (TEnum "ColType"); req "value" TJson ] with
                  Hosted = Some(typed "string * Host.Cell" "Host.encCompared" "Host.decEqualTo") }
              case "IsNull" [ req "column" TStr ] ]

    let window: IdlRecord =
        { Name = "Window"
          Fields = [ req "start" TInt; fld "label" TStr Optional ]
          Annotations = ann }

    let idl: Idl =
        { Kinds =
            [ { Tag = "Holder"
                Category = ""
                Fields =
                  [ req "placement" (TUnion("Placement", []))
                    req "deferred" (TUnion("Deferred", [ TInt ]))
                    req "window" (TRecord "Window") ]
                Annotations = ann }
              { Tag = "Seams"
                Category = ""
                Fields = [ req "fault" (TUnion("Fault", [])); req "predicate" (TUnion("Predicate", [])) ]
                Annotations = ann } ]
          Unions = [ placement; deferred; fault; predicate ]
          Enums =
            [ Declare.enumWith "IslandKind" [ "Pyodide", "pyodide"; "Fable", "fable"; "Js", "js" ]
              Declare.enumWith "ColType" [ "Int", "int"; "Str", "string" ] ]
          Records = [ window ]
          Defaults = []
          NodeFields = []
          Ops = []
          Wire = WireShape.Default
          Harden = HardenPolicy.Undeclared }

    /// The host declarations the codec-only module is generated against: one with qualified access,
    /// one without, a generic, a record, and the two host codecs.
    let hostSource =
        """module Host =
    type IslandKind =
        | Pyodide
        | Fable
        | Js

    type ColType =
        | Int
        | Str

    [<RequireQualifiedAccess>]
    type Placement =
        | BuildTime
        | Server
        | ClientIsland of island: IslandKind

    type Deferred<'T> =
        | Pending
        | Ready of value: 'T
        | Failed of message: string

    type Window = { Start: int; Label: string option }
    type Space = { Lo: int }

    [<RequireQualifiedAccess>]
    type Fault =
        | EmptySpace of addr: string * space: Space
        | NonFiniteBound of addr: string

    type Cell =
        | I of int
        | S of string

    [<RequireQualifiedAccess>]
    type Predicate =
        | EqualTo of column: string * literal: Cell
        | IsNull of column: string

    let encSpace (s: Space) : JVal = JObj [ "lo", JInt s.Lo ]

    let decSpace (j: JVal) : Result<Space, DecodeError> =
        Decoder.field "lo" Decoder.int j |> Result.map (fun lo -> { Lo = lo })

    let encCompared (column: string, cell: Cell) : (string * JVal) list =
        [ "column", JStr column
          "type", JStr(match cell with I _ -> "int" | S _ -> "string")
          "value", (match cell with I i -> JInt i | S s -> JStr s) ]

    let decEqualTo (j: JVal) : Result<string * Cell, DecodeError> =
        Decoder.field "column" Decoder.str j
        |> Result.bind (fun column ->
            Decoder.field "type" Decoder.str j
            |> Result.bind (fun ty ->
                Decoder.field
                    "value"
                    (fun v ->
                        match ty, v with
                        | "int", JInt i -> Ok(I i)
                        | "string", JStr s -> Ok(S s)
                        | _ -> Error(DecodeError.make DecodeCode.OutOfRange ("a " + ty + " value") "a literal of another type"))
                    j
                |> Result.map (fun cell -> column, cell)))
"""

    let request: CodecOnly =
        { Roots = [ "Placement"; "Deferred"; "Window"; "Fault"; "Predicate" ]
          HostTypes =
            Map.ofList
                [ "IslandKind", "Host.IslandKind"
                  "ColType", "Host.ColType"
                  "Placement", "Host.Placement"
                  "Deferred", "Host.Deferred"
                  "Window", "Host.Window"
                  "Fault", "Host.Fault"
                  "Predicate", "Host.Predicate" ] }

    let rootType (root: string) : IdlType =
        match root with
        | "Deferred" -> TUnion("Deferred", [ TInt ])
        | "Window" -> TRecord "Window"
        | r -> TUnion(r, [])

    /// The generated module's reader and writer of `root`, as script source.
    let private fsReader (root: string) (strict: bool) =
        let suffix = if strict then "Strict" else ""

        match root with
        | "Deferred" -> sprintf "decodeDeferred%s (Decoder.int)" suffix
        | r -> sprintf "decode%s%s" r suffix

    let private fsWriter (root: string) =
        match root with
        | "Deferred" -> "encodeDeferred JInt"
        | r -> "encode" + r

    /// Run the codec-only module under `dotnet fsi` over `(root, document)` rows: per row, the bytes
    /// the module re-encodes the lenient read to (or its refusal, rendered), then the strict read's
    /// verdict. `None` without `dotnet`.
    let fsharpLeg (rows: (string * string) list) : (string * string) list option =
        let moduleText =
            match Gen.fsharpCodecModule GenSupport.Empty request "Seam550Codec" idl with
            | Ok m -> m
            | Error e -> failtestf "the codec-only module was refused: %s" (CodegenError.describe e)

        let probe =
            rows
            |> List.map (fun (root, doc) ->
                sprintf
                    "probe (fun j -> %s j |> Result.map (%s)) (%s) %s"
                    (fsReader root false)
                    (fsWriter root)
                    (fsReader root true)
                    (Canonical.write Rendering.CrossHost (JStr doc)))
            |> String.concat "\n"

        let dll (name: string) =
            "#r @\"" + Path.Combine(AppContext.BaseDirectory, name) + "\"\n"

        let script =
            dll "Fuaran.Core.Text.dll"
            + dll "Fuaran.Core.Wire.dll"
            + "open Fuaran.Core\n"
            + hostSource
            + "\n"
            + moduleText.Replace("module Seam550Codec\n", "")
            + "\n\nlet probe (lenient: JVal -> Result<JVal, DecodeError>) (strict: JVal -> Result<'a, DecodeError>) (doc: string) =\n"
            + "    let j = match Json.parse doc with Ok j -> j | Error m -> failwith m\n"
            + "    let show r = match r with Ok _ -> \"ok\" | Error e -> DecodeError.render e\n"
            + "    let first = match lenient j with Ok v -> Canonical.write Rendering.CrossHost v | Error e -> DecodeError.render e\n"
            + "    printfn \"%s\\t%s\" first (show (strict j))\n\n"
            + probe
            + "\n"

        let path =
            Path.Combine(Path.GetTempPath(), sprintf "fuaran-550-%s.fsx" (Guid.NewGuid().ToString "N"))

        File.WriteAllText(path, script)

        try
            match ChildProcess.run (ChildProcess.command "dotnet" ("fsi \"" + path + "\"")) with
            | Error _ -> None
            | Ok p when p.Exit <> 0 -> failtestf "the codec-only module did not run: %s\n%s" p.Stderr p.Stdout
            | Ok p ->
                p.Stdout.Replace("\r\n", "\n").Split('\n')
                |> Array.filter (fun l -> l <> "")
                |> Array.map (fun l ->
                    match l.Split('\t') with
                    | [| a; b |] -> a, b
                    | _ -> failtestf "unexpected probe line: %s" l)
                |> List.ofArray
                |> Some
        finally
            try
                File.Delete path
            with _ ->
                ()

    /// The interpreter's readings of one row: the lenient read (as the bytes `written` — the document
    /// with any member the reading drops left out — or its refusal) and the strict verdict, in the F#
    /// leg's spelling.
    let interpreterLeg (root: string, doc: string) (written: string) : string * string =
        let t = rootType root

        let j =
            match Json.parse doc with
            | Ok j -> j
            | Error m -> failtestf "%s" m

        let first =
            match Decode.valueDetailed idl t j with
            | Error e -> DecodeError.render e
            | Ok _ -> Canonical.write Rendering.CrossHost (Result.defaultWith failwith (Json.parse written))

        let strict =
            match Decode.valueStrictDetailed idl t j with
            | Ok _ -> "ok"
            | Error e -> DecodeError.render e

        first, strict

    /// Documents every host must read and write back byte-for-byte: each case of each root, with the
    /// predicate literal in query's pinned shape.
    let accepted: (string * string) list =
        [ "Placement", "{\"$type\":\"buildTime\"}"
          "Placement", "{\"$type\":\"server\"}"
          "Placement", "{\"$type\":\"clientIsland\",\"island\":\"pyodide\"}"
          "Deferred", "{\"$type\":\"pending\"}"
          "Deferred", "{\"$type\":\"ready\",\"value\":7}"
          "Deferred", "{\"$type\":\"failed\",\"message\":\"boom\"}"
          "Window", "{\"label\":\"w\",\"start\":3}"
          "Window", "{\"start\":-1}"
          "Fault", "{\"$type\":\"emptySpace\",\"addr\":\"a\",\"space\":{\"lo\":2}}"
          "Fault", "{\"$type\":\"nonFiniteBound\",\"addr\":\"b\"}"
          "Predicate", "{\"$type\":\"equalTo\",\"column\":\"c\",\"type\":\"int\",\"value\":1}"
          "Predicate", "{\"$type\":\"equalTo\",\"column\":\"c\",\"type\":\"string\",\"value\":\"x\"}"
          "Predicate", "{\"$type\":\"isNull\",\"column\":\"c\"}" ]

    /// One undeclared member per object shape, nested ones included: both readings agree that the
    /// lenient one reads past it and the strict one refuses it at that member.
    let undeclared: (string * string * string) list =
        [ "Placement",
          "{\"$type\":\"clientIsland\",\"island\":\"js\",\"actor\":\"m\"}",
          "{\"$type\":\"clientIsland\",\"island\":\"js\"}"
          "Deferred", "{\"$type\":\"failed\",\"message\":\"m\",\"code\":1}", "{\"$type\":\"failed\",\"message\":\"m\"}"
          "Window", "{\"start\":1,\"extra\":true}", "{\"start\":1}"
          "Fault",
          "{\"$type\":\"nonFiniteBound\",\"addr\":\"b\",\"space\":{}}",
          "{\"$type\":\"nonFiniteBound\",\"addr\":\"b\"}"
          "Predicate",
          "{\"$type\":\"isNull\",\"column\":\"c\",\"text\":\"x\"}",
          "{\"$type\":\"isNull\",\"column\":\"c\"}" ]

[<Tests>]
let seamCodecs550 =
    testList
        "Phase 550 - a derived seam codec, across the three hosts"
        [ testCase "the test vocabulary is well-formed and its artifact keeps every new declaration" (fun _ ->
              Expect.isEmpty (Declare.errors Seam550.idl) "the vocabulary is admitted"

              match Artifact.parse (Artifact.render Seam550.idl) with
              | Error m -> failtestf "the artifact did not read back: %s" m
              | Ok back ->
                  Expect.equal
                      back
                      (Artifact.canonicalise Seam550.idl)
                      "the tag rule, the typed refusals and the case codec round-trip")

          testCase "the interpreter and the TypeScript host agree on every drawn node, under the tag rule" (fun _ ->
              let tags = [ "Holder" ]

              let vectors =
                  match Sample.trySampleNodes Seam550.idl tags 550 120 with
                  | Ok vs -> vs
                  | Error f -> failtestf "the sampler refused: %A" f

              let rows =
                  vectors
                  |> List.map (fun v ->
                      match Encode.encode Seam550.idl v with
                      | Error m -> failtestf "the interpreter refused %A: %s" v m
                      | Ok bytes ->
                          match Decode.decode Seam550.idl bytes with
                          | Error m -> failtestf "the interpreter refused its own %s: %s" bytes m
                          | Ok d ->
                              Expect.equal (Encode.encode Seam550.idl d) (Ok bytes) "the interpreter round-trips"

                              Expect.equal
                                  (Decode.decodeStrictDetailed Seam550.idl bytes)
                                  (Decode.decodeDetailed Seam550.idl bytes)
                                  "a declared document reads alike under both readings"

                              v, d, bytes)

              // Every derived tag is on the wire, and no case name is.
              let all = rows |> List.map (fun (_, _, b) -> b) |> String.concat "\n"

              for tag in [ "buildTime"; "server"; "clientIsland"; "pending"; "ready"; "failed" ] do
                  Expect.stringContains all ("\"" + tag + "\"") (sprintf "the draws reach the wire tag %s" tag)

              Expect.isFalse (all.Contains "\"ClientIsland\"") "a case name never reaches the wire"

              match tsLeg Seam550.idl rows with
              | None -> ()
              | Some results ->
                  rows
                  |> List.iteri (fun i (_, _, bytes) ->
                      match results.TryFind i with
                      | None -> failtestf "#%d: the TypeScript host answered nothing" i
                      | Some r ->
                          Expect.equal r.Authored bytes (sprintf "#%d TypeScript writes the interpreter's bytes" i)

                          match r.Decoded with
                          | Error m -> failtestf "#%d TypeScript refused %s: %s" i bytes m
                          | Ok(again, eq) ->
                              Expect.equal again bytes (sprintf "#%d TypeScript re-encodes the bytes" i)
                              Expect.isTrue eq (sprintf "#%d TypeScript decodes the interpreter's value%s" i r.Shown)))

          testCase
              "the codec-only module reads and writes every case as the interpreter does, over host types"
              (fun _ ->
                  let rows =
                      (Seam550.accepted |> List.map (fun (r, d) -> r, d, d)) @ Seam550.undeclared

                  match Seam550.fsharpLeg (rows |> List.map (fun (r, d, _) -> r, d)) with
                  | None -> ()
                  | Some answers ->
                      List.zip rows answers
                      |> List.iter (fun ((root, doc, written), (fsFirst, fsStrict)) ->
                          let interpFirst, interpStrict = Seam550.interpreterLeg (root, doc) written
                          Expect.equal fsFirst interpFirst (sprintf "%s %s: the F# host's lenient read" root doc)
                          Expect.equal fsStrict interpStrict (sprintf "%s %s: the F# host's strict read" root doc))

                      for (root, doc), (first, strict) in
                          List.zip Seam550.accepted (List.truncate Seam550.accepted.Length answers) do
                          Expect.equal
                              first
                              (Canonical.write Rendering.CrossHost (Result.defaultWith failwith (Json.parse doc)))
                              (sprintf "%s: byte-identical" doc)

                          Expect.equal
                              strict
                              "ok"
                              (sprintf "%s %s: the strict reading accepts a declared document" root doc)

                      for (_, doc, _), (first, strict) in
                          List.zip Seam550.undeclared (List.skip Seam550.accepted.Length answers) do
                          Expect.isFalse
                              (first.StartsWith "UndeclaredMember")
                              (sprintf "%s: the lenient reading reads past it" doc)

                          Expect.stringStarts
                              strict
                              "UndeclaredMember at $["
                              (sprintf "%s: the strict reading refuses it at the member" doc))

          testCase "a typed host codec keeps its code and its path, under the slot and under the case" (fun _ ->
              let refused =
                  [ "Fault", "{\"$type\":\"emptySpace\",\"addr\":\"a\",\"space\":{\"lo\":\"x\"}}"
                    "Predicate", "{\"$type\":\"equalTo\",\"column\":\"c\",\"type\":\"int\",\"value\":\"x\"}" ]

              match Seam550.fsharpLeg refused with
              | None -> ()
              | Some answers ->
                  let first = answers |> List.map fst
                  // At the slot: the host decoder's WrongKind at `lo`, under `space` — not OutOfRange at the slot.
                  Expect.stringStarts
                      first[0]
                      "WrongKind at $[\"space\"][\"lo\"]"
                      "the slot codec's refusal, under the slot"
                  // At the case: the dependent read's OutOfRange at `value`, where query pins it.
                  Expect.stringStarts first[1] "OutOfRange at $[\"value\"]" "the case codec's refusal, under the case")

          testCase "each new declaration refusal is red on a planted violation" (fun _ ->
              let refusedWith (fragment: string) (idl: Idl) =
                  let faults = Declare.errors idl |> List.map VocabularyFault.describe

                  Expect.exists faults (fun f -> f.Contains fragment) (sprintf "refused: %s (got %A)" fragment faults)

              let collide =
                  { Seam550.placement with
                      Cases =
                          Seam550.placement.Cases
                          @ [ { Seam550.placement.Cases[0] with
                                  Tag = "buildTime" } ] }

              refusedWith
                  "derives the wire tag 'buildTime'"
                  { Seam550.idl with
                      Unions = collide :: Seam550.idl.Unions.Tail }

              // The control: the same two names under the case-name rule carry two tags.
              Expect.isEmpty
                  (Declare.errors
                      { Seam550.idl with
                          Unions =
                              { collide with
                                  TagRule = UnionTagRule.CaseName }
                              :: Seam550.idl.Unions.Tail })
                  "under the case-name rule the two cases are told apart"

              let withCase (h: HostedCodec) =
                  let p = Seam550.predicate

                  { Seam550.idl with
                      Unions =
                          Seam550.idl.Unions
                          |> List.map (fun u ->
                              if u.Name = p.Name then
                                  { p with
                                      Cases = { p.Cases[0] with Hosted = Some h } :: p.Cases.Tail }
                              else
                                  u) }

              let h = Seam550.typed "string * Host.Cell" "Host.encCompared" "Host.decEqualTo"
              refusedWith "declares no wire form" (withCase { h with Wire = Some TStr })

              let transparent =
                  { Seam550.idl with
                      Harden =
                          { HardenPolicy.Undeclared with
                              TransparentUnions = [ "Predicate", "EqualTo" ] } }

              refusedWith "has no members for a case-level host codec" transparent)

          testCase
              "the structural module declares the host payload of a case codec and agrees with the interpreter"
              (fun _ ->
                  let moduleText =
                      match Gen.fsharpModule "Seam550Structural" Seam550.idl [ "Holder"; "Seams" ] with
                      | Ok m -> m
                      | Error e -> failtestf "the structural module was refused: %s" (CodegenError.describe e)

                  Expect.stringContains
                      moduleText
                      "| EqualTo of string * Host.Cell"
                      "the case declares the codec's payload"

                  let docs =
                      [ for f in Seam550.accepted |> List.filter (fun (r, _) -> r = "Fault") |> List.map snd do
                            for p in Seam550.accepted |> List.filter (fun (r, _) -> r = "Predicate") |> List.map snd do
                                sprintf
                                    "{\"id\":\"n\",\"kind\":{\"$type\":\"Seams\",\"fault\":%s,\"predicate\":%s}}"
                                    f
                                    p ]

                  let dll (name: string) =
                      "#r @\"" + Path.Combine(AppContext.BaseDirectory, name) + "\"\n"

                  let script =
                      dll "Fuaran.Core.Text.dll"
                      + dll "Fuaran.Core.Wire.dll"
                      + dll "Fuaran.Core.Tree.dll"
                      + dll "Fuaran.Core.Validator.dll"
                      + "open Fuaran.Core\n"
                      + Seam550.hostSource
                      + "\n"
                      + moduleText.Replace("module Seam550Structural\n", "")
                      + "\n\n"
                      + (docs
                         |> List.map (fun d ->
                             sprintf
                                 "printfn \"%%s\" (match decodeNode %s with Ok n -> encodeNode n | Error e -> DecodeError.render e)"
                                 (Canonical.write Rendering.CrossHost (JStr d)))
                         |> String.concat "\n")
                      + "\n"

                  let path =
                      Path.Combine(Path.GetTempPath(), sprintf "fuaran-550s-%s.fsx" (Guid.NewGuid().ToString "N"))

                  File.WriteAllText(path, script)

                  try
                      match ChildProcess.run (ChildProcess.command "dotnet" ("fsi \"" + path + "\"")) with
                      | Error _ -> ()
                      | Ok p ->
                          Expect.equal p.Exit 0 (sprintf "the structural module runs: %s" p.Stderr)

                          let lines =
                              p.Stdout.Replace("\r\n", "\n").Split('\n') |> Array.filter (fun l -> l <> "")

                          for d, line in List.zip docs (List.ofArray lines) do
                              let interpreter =
                                  Decode.decode Seam550.idl d |> Result.bind (Encode.encode Seam550.idl)

                              Expect.equal
                                  (Ok line)
                                  interpreter
                                  (sprintf "%s: the F# host's bytes are the interpreter's" d)
                  finally
                      try
                          File.Delete path
                      with _ ->
                          ())

          testCase "a tag-rule change is a wire break, a case-codec change undecided" (fun _ ->
              let before = Seam550.idl

              let after =
                  { before with
                      Unions =
                          before.Unions
                          |> List.map (fun u ->
                              if u.Name = "Placement" then
                                  { u with
                                      TagRule = UnionTagRule.CaseName }
                              elif u.Name = "Predicate" then
                                  { u with
                                      Cases = u.Cases |> List.map (fun c -> { c with Hosted = None }) }
                              else
                                  u) }

              match Diff.ofValues (before, None) (after, None) with
              | Error m -> failtestf "%s" m
              | Ok v ->
                  let rows = v.Changes |> List.map (fun c -> c.Change, c.Severity)

                  Expect.contains
                      rows
                      (UnionTagRuleChanged("Placement", "lowerFirst", "caseName"), Severity.BreakingWire)
                      "a rule change re-tags every case"

                  Expect.exists
                      rows
                      (fun (c, s) ->
                          match c with
                          | UnionCaseHostedChanged("Predicate", "EqualTo", _, "") -> s = Severity.Unclassifiable
                          | _ -> false)
                      "a case codec retired is undecided") ]
