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

// ---------------------------------------------------------------------------
// The value normal form.
// ---------------------------------------------------------------------------

let private floatKey (f: float) = Canon.render (JFloat f)

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
    | TStr, VStr s -> "s" + Canon.render (JStr s)
    | TInt, VInt i -> "i" + string i
    | TBool, VBool b -> if b then "true" else "false"
    | TFloat, VFloat f -> "f" + floatKey f
    | TFloat, VInt i -> "f" + floatKey (float i)
    | TEnum _, VEnum c -> "e" + Canon.render (JStr c)
    | (TClosure | TFn _), _ -> "closure"
    | TOpaque, _ -> "opaque"
    | (TJson | THosted _), VJson j -> "j" + Canon.render j
    | TList inner, VList xs -> "[" + (xs |> List.map (valueKey idl inner) |> String.concat ",") + "]"
    | TMap vt, VMap entries ->
        "{"
        + (entries
           |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))
           |> List.map (fun (k, ev) -> Canon.render (JStr k) + ":" + valueKey idl vt ev)
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
        + Canon.render (JStr id)
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
        string (Char.ToUpperInvariant s.[0]) + s.Substring 1

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
        Some fields.[0]

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

        VUnion(case.Name, fieldsOf hosted idl subst c.Fields (fun f -> byName.[f.Name]))
    | TNode ->
        let kindVal = prop o "Kind"
        let case, specs = FSharpValue.GetUnionFields(kindVal, kindVal.GetType())
        let k = idl.Kinds |> List.find (fun k -> k.Tag = case.Name)

        let fields =
            if List.isEmpty k.Fields then
                []
            else
                fieldsOf hosted idl Map.empty k.Fields (fun f -> prop specs.[0] (pascal f.Name))

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
                Some(VFloat adversarialFloats.[rng.Next adversarialFloats.Length])
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

    Sample.sampleNodes idl tags seed count
    |> List.map (rehost TNode)
    |> List.collect (fun v -> [ v; adversarial idl rng v ])

// ---------------------------------------------------------------------------
// The three hosts.
// ---------------------------------------------------------------------------

/// A compiled generated module, behind the two entry points every one of them exports.
type private Compiled =
    { Decode: string -> Result<obj, string>
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
    JArr [ for _ in 1 .. rng.Next 4 -> JFloat pool.[rng.Next pool.Length] ]

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
        let proc =
            try
                Some(Diagnostics.Process.Start(ChildProcess.redirected "node" ("\"" + tmp + "\"")))
            with _ ->
                None

        match proc with
        | None -> None
        | Some p ->
            let stdout = p.StandardOutput.ReadToEnd()
            let stderr = p.StandardError.ReadToEnd()
            p.WaitForExit()

            if p.ExitCode <> 0 then
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
            sprintf "  [%d, %s, %s, %s]," i (lit authored) (lit decoded) (Canon.render (JStr bytes)))
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
        + "  if (!r.ok) { out.refused = r.error; } else {\n"
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
            | Error m -> [ sprintf "#%d compiled F# refused %s: %s" i bytes m ]
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

let private driftGuard (rel: string) (modName: string) (sup: Gen.GenSupport) (idl: Idl) =
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
                  Gen.GenSupport.Empty
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
              | Error m -> failtestf "the compiled host refused its own marker node: %s" m)

          testCase "a field-less RECORD is a marker type too, in the one type emitter" (fun _ ->
              let idl =
                  { ScoreDomainSpike.scoreIdl with
                      Records = { Name = "Empty"; Fields = [] } :: ScoreDomainSpike.scoreIdl.Records }

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
                            Annotations = Annotations.Empty } ]
                      Annotations = Annotations.Empty } ] }

    holder,
    Sample.sampleNodes holder [ "OpHolder303" ] seed count
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

                  Expect.isError (Decode.decode current bytes) "the interpreter refuses it"
                  Expect.isError (ReferenceGenerated.decodeNode bytes) "the compiled F# host refuses it"

                  let tsModule =
                      match Gen.typescriptModule current (current.Kinds |> List.map _.Tag) with
                      | Ok src -> src
                      | Error e -> failtestf "TypeScript codegen refused: %s" (CodegenError.describe e)

                  match
                      runNode (
                          tsModule
                          + "\nconsole.log(JSON.stringify(decodeNode("
                          + Canon.render (JStr bytes)
                          + ").ok));\n"
                      )
                  with
                  | None -> skiptest "node not on PATH"
                  | Some out -> Expect.equal (out.Trim()) "false" "the TypeScript host refuses it"

                  // And the other direction: a member the current vocabulary does not know is
                  // tolerated, and the value read is the one the current vocabulary declares.
                  let newer =
                      withNote (fun fs ->
                          fs
                          @ [ { Name = "zz"
                                Type = TStr
                                Opt = Required
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
      Annotations = Annotations.Empty }

[<Tests>]
let declarationRefusals =
    testList
        "Phase 303 — an object-capable transparent case and an envelope-named field are refused at ofJson"
        [ testCase "a transparent case over json is refused when the artifact is loaded" (fun _ ->
              let lit: IdlUnion =
                  { Name = "Lit"
                    Params = []
                    Cases =
                      [ { Tag = "Lit"
                          Fields = [ reqField "value" TJson ]
                          Annotations = Annotations.Empty }
                        { Tag = "Ref"
                          Fields = [ reqField "name" TStr ]
                          Annotations = Annotations.Empty } ] }

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
                                              Opt = Optional } ] }

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
              Fields = [ reqField "href" TStr ] }
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
    |> List.exists (fun k -> wire.Contains(Canon.render (JStr k) + ":"))

let private vectorModels () : FStarTarget.VectorModel list =
    let wires = certificationWires ()

    [ "DocVocabulary", "D", "document"
      "ScoreVocabulary", "S", "score"
      "Vocabulary", "R", "reference" ]
    |> List.map (fun (model, prefix, name) ->
        let _, idl, ws = wires |> List.find (fun (n, _, _) -> n = name)

        let one (w: string) : FStarTarget.VectorModel =
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
