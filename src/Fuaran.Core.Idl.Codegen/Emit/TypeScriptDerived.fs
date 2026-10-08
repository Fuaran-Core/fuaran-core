namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.Reach
open Fuaran.Core.Idl.Emit.Annotations
open Fuaran.Core.Idl.Emit.FSharpDefaults

/// The TypeScript backend's structural derivations, collecting decoders and their declarations
/// (Phases 380, 381; `TypeScript` until the Phase 388 split along its banners).
module internal TypeScriptDerived =
    open TypeScriptCodec
    open TypeScriptDeclarations

    // -----------------------------------------------------------------------
    // Phase 380 — the STRUCTURAL DERIVATIONS (Phase 374) and the COLLECTING DECODERS (Phase 377)
    // in the TypeScript host. Every derivation is opt-in exactly as on the F# side, and is stated
    // over the SAME analysis rather than a second one: which positions are structural children
    // (`FSharpDerive.structuralField`), which declarations hold a node (`FSharpDerive.nodeHolders`),
    // which fields a fold descends (`FSharpDerive.foldSelfIn`), when a defect is a leaf's
    // (`FSharpCodec.collectsOver`). ADMISSIBILITY IS THE F# PATH'S: the requests are run through
    // the F# derivation first and its refusal is this path's refusal, so the two hosts accept and
    // refuse the same requests with the same typed error.
    //
    // This host holds a value in its WIRE shape — plain objects keyed by wire field name, a union
    // case tagged by the discriminator, an enum as its wire string, an absent optional as
    // `undefined`, a map as an object — so every derived member reads and rebuilds that shape. A
    // map is walked in Ordinal key order, the order the F# host's `Map` iterates in, so the two
    // hosts list the nodes a map holds, and fold over its values, in the same order.
    //
    // `MapMsg` has no TypeScript counterpart: this host holds a handler slot (`TFn`) as its
    // sentinel's `null` and declares no message type, so a message map has nothing to rewrite. The
    // request is still checked — a vocabulary the F# path refuses is refused here — and emits
    // nothing.
    // -----------------------------------------------------------------------

    /// `obj.name`, or `obj["odd name"]` for a key JavaScript cannot spell bare.
    let private tsProp (obj: string) (name: string) = tsDiscProp name obj

    /// `[...a, ...b]`, a single operand as itself, or `[]`.
    let private tsAppend (xs: string list) =
        match xs with
        | [] -> "[]"
        | [ x ] -> x
        | _ -> "[" + (xs |> List.map (fun x -> "..." + x) |> String.concat ", ") + "]"

    /// The kind object of the node bound to `n` — the node itself under the flat envelope.
    let private tsKindOf (flat: bool) (n: string) = if flat then n else n + ".kind"

    /// The structural children of a kind, by wire field name: `(name, isList)`.
    let private tsStructuralFields (k: IdlKind) : (string * bool) list =
        k.Fields
        |> List.choose (fun f -> FSharpDerive.structuralField f |> Option.map (fun (_, isList) -> f.Name, isList))

    /// The `switch` over the kind tag of `k` — one arm per entry, then `fallback`.
    let private tsKindSwitch (disc: string) (indent: string) (arms: (string * string) list) (fallback: string) =
        [ yield indent + "switch (" + tsDiscProp disc "k" + ") {"
          for tag, body in arms do
              yield indent + "  case " + SourceLit.tsString tag + ": " + body
          yield indent + "  default: " + fallback
          yield indent + "}" ]
        |> String.concat "\n"

    /// Emission 1 — public `wireTag`, `allWireTags`, `children`, `withChildren` (kids first, so
    /// `withChildren(children(n), n)` is `n` rebuilt), and `nodeWitness` over them: the four members
    /// of Core's `NodeWitness`, as a plain object, since this host has no Core runtime to type it.
    let private tsStructuralDecl (disc: string) (flat: bool) (kinds: IdlKind list) : string * string list =
        let bearing =
            kinds |> List.filter (fun k -> not (List.isEmpty (tsStructuralFields k)))

        let rebuild (assigns: string) =
            if flat then
                sprintf "{ ...n, %s }" assigns
            else
                sprintf "{ ...n, kind: { ...k, %s } }" assigns

        let childArms =
            bearing
            |> List.map (fun k ->
                let items =
                    tsStructuralFields k
                    |> List.map (fun (name, isList) -> (if isList then "..." else "") + tsProp "k" name)
                    |> String.concat ", "

                k.Tag, sprintf "return [%s];" items)

        let replaceArms =
            bearing
            |> List.map (fun k ->
                let assigns =
                    match tsStructuralFields k with
                    | [ (name, true) ] -> SourceLit.tsKey name + ": kids"
                    | fs ->
                        fs
                        |> List.mapi (fun i (name, _) -> sprintf "%s: kids[%d]" (SourceLit.tsKey name) i)
                        |> String.concat ", "

                k.Tag, sprintf "return %s;" (rebuild assigns))

        let allTags =
            "const allWireTags = ["
            + (kinds |> List.map (fun k -> SourceLit.tsString k.Tag) |> String.concat ", ")
            + "];"

        [ "// Phase 380 — STRUCTURAL ACCESS. The kind's wire tag — the discriminator it is encoded under."
          "function wireTag(n) {"
          "  return " + tsDiscProp disc (tsKindOf flat "n") + ";"
          "}"
          ""
          "// Every wire tag this module's kinds are encoded under, in declaration order."
          allTags
          ""
          "// The node's ordered structural children, in field order: a node or node list a kind always"
          "// carries. An optional node is a keyed position, not a child."
          "function children(n) {"
          "  const k = " + tsKindOf flat "n" + ";"
          tsKindSwitch disc "  " childArms "return [];"
          "}"
          ""
          "// The node with exactly this structural child list, its id and kind kept."
          "function withChildren(kids, n) {"
          "  const k = " + tsKindOf flat "n" + ";"
          tsKindSwitch disc "  " replaceArms "return n;"
          "}"
          ""
          "// The structural witness — Core's `NodeWitness` members over this module's node."
          "const nodeWitness = {"
          "  id: (n) => n.id,"
          "  kindTag: wireTag,"
          "  children: children,"
          "  replaceChildren: (n, kids) => withChildren(kids, n),"
          "};" ]
        |> String.concat "\n",
        [ "wireTag"; "allWireTags"; "children"; "withChildren"; "nodeWitness" ]

    /// Emission 2 — the keyed walk: per-declaration node listers and rebuilders, the node-level
    /// `keyedChildren` / `withKeyedChildren` (arity-preserving), the full walk, and `keyedWitness`
    /// (Core's `KeyedWitness` members). Every node position that is not a structural child: an
    /// optional node, a node in a record, a union case, a list or a map, and the node envelope.
    let private tsKeyedDecl (disc: string) (flat: bool) (ctx: FSharpDerive.Ctx) : string * string list =
        let hs = FSharpDerive.nodeHolders ctx
        let holdsF (f: IdlField) = FSharpDerive.holdsIn hs f.Type

        // A node held through a generic union's type argument never reaches here: the F# analysis
        // refused it first.
        let rec collect (t: IdlType) (e: string) (d: int) : string =
            let x = sprintf "__x%d" d

            match t with
            | TNode -> sprintf "[%s]" e
            | TList i -> sprintf "%s.flatMap((%s) => %s)" e x (collect i x (d + 1))
            | TMap i ->
                sprintf "Object.keys(%s).sort().flatMap((%s) => %s)" e x (collect i (sprintf "%s[%s]" e x) (d + 1))
            | TRecord n
            | TUnion(n, _) -> sprintf "nodesIn%s(%s)" n e
            | _ -> "[]"

        let rec mapNodes (t: IdlType) (e: string) (d: int) : string =
            let x = sprintf "__x%d" d

            match t with
            | TNode -> sprintf "f(%s)" e
            | TList i -> sprintf "%s.map((%s) => %s)" e x (mapNodes i x (d + 1))
            | TMap i -> sprintf "keyedMapEntries(%s, (%s) => %s)" e x (mapNodes i x (d + 1))
            | TRecord n
            | TUnion(n, _) -> sprintf "mapNodesIn%s(f, %s)" n e
            | _ -> e

        let collectField (f: IdlField) (e: string) =
            match f.Opt with
            | Optional -> sprintf "(%s === undefined ? [] : %s)" e (collect f.Type e 0)
            | _ -> collect f.Type e 0

        let mapField (f: IdlField) (e: string) =
            match f.Opt with
            | Optional -> sprintf "(%s === undefined ? undefined : %s)" e (mapNodes f.Type e 0)
            | _ -> mapNodes f.Type e 0

        /// `const __m<i> = …;` per field, then the object of the rebuilt members.
        let rebuilt (indent: string) (fs: IdlField list) (access: IdlField -> string) =
            let lets =
                fs
                |> List.mapi (fun i f -> sprintf "%sconst __m%d = %s;\n" indent i (mapField f (access f)))
                |> String.concat ""

            let assigns =
                fs
                |> List.mapi (fun i f -> sprintf "%s: __m%d" (SourceLit.tsKey f.Name) i)
                |> String.concat ", "

            lets, assigns

        let recordHelpers (r: IdlRecord) =
            let fs = r.Fields |> List.filter holdsF
            let lets, assigns = rebuilt "  " fs (fun f -> tsProp "v" f.Name)

            [ sprintf
                  "function nodesIn%s(v) {\n  return %s;\n}"
                  r.Name
                  (tsAppend (fs |> List.map (fun f -> collectField f (tsProp "v" f.Name))))
              sprintf "function mapNodesIn%s(f, v) {\n%s  return { ...v, %s };\n}" r.Name lets assigns ]

        let unionHelpers (u: IdlUnion) =
            let holding = u.Cases |> List.filter (fun c -> c.Fields |> List.exists holdsF)

            let tag = tsDiscProp ctx.Idl.Wire.Discriminator "v"

            let listerArms =
                holding
                |> List.map (fun c ->
                    let fs = c.Fields |> List.filter holdsF

                    sprintf
                        "    case %s: return %s;"
                        (SourceLit.tsString c.Tag)
                        (tsAppend (fs |> List.map (fun f -> collectField f (tsProp "v" f.Name)))))

            let mapperArms =
                holding
                |> List.map (fun c ->
                    let fs = c.Fields |> List.filter holdsF
                    let lets, assigns = rebuilt "      " fs (fun f -> tsProp "v" f.Name)

                    sprintf
                        "    case %s: {\n%s      return { ...v, %s };\n    }"
                        (SourceLit.tsString c.Tag)
                        lets
                        assigns)

            let lister =
                sprintf "function nodesIn%s(v) {\n  switch (%s) {\n" u.Name tag
                + String.concat "\n" (listerArms @ [ "    default: return [];" ])
                + "\n  }\n}"

            let mapper =
                sprintf "function mapNodesIn%s(f, v) {\n  switch (%s) {\n" u.Name tag
                + String.concat "\n" (mapperArms @ [ "    default: return v;" ])
                + "\n  }\n}"

            [ lister; mapper ]

        let helpers =
            (ctx.Records
             |> List.filter (fun r -> hs.Contains r.Name)
             |> List.collect recordHelpers)
            @ (ctx.Unions
               |> List.filter (fun u -> hs.Contains u.Name)
               |> List.collect unionHelpers)

        let keyedFields (k: IdlKind) =
            k.Fields
            |> List.filter (fun f -> holdsF f && (FSharpDerive.structuralField f).IsNone)

        let keyedKinds =
            ctx.Kinds |> List.filter (fun k -> not (List.isEmpty (keyedFields k)))

        let envFields = ctx.Idl.NodeFields |> List.filter holdsF
        let disc = ctx.Idl.Wire.Discriminator

        let listerArms =
            keyedKinds
            |> List.map (fun k ->
                k.Tag,
                sprintf
                    "ofKind = %s; break;"
                    (tsAppend (keyedFields k |> List.map (fun f -> collectField f (tsProp "k" f.Name)))))

        let envListers = envFields |> List.map (fun f -> collectField f (tsProp "n" f.Name))

        let mapperArms =
            keyedKinds
            |> List.map (fun k ->
                let lets, assigns = rebuilt "      " (keyedFields k) (fun f -> tsProp "k" f.Name)
                k.Tag, sprintf "{\n%s      kind = { %s };\n      break;\n    }" lets assigns)

        let envLets, envAssigns =
            let lets =
                envFields
                |> List.mapi (fun i f -> sprintf "  const __e%d = %s;\n" i (mapField f (tsProp "n" f.Name)))
                |> String.concat ""

            let assigns =
                envFields
                |> List.mapi (fun i f -> sprintf ", %s: __e%d" (SourceLit.tsKey f.Name) i)
                |> String.concat ""

            lets, assigns

        let mapKeyedReturn =
            if flat then
                sprintf "  return { ...n, ...kind%s };" envAssigns
            else
                sprintf "  return { ...n, kind: { ...k, ...kind }%s };" envAssigns

        let text =
            [ yield
                  "// Phase 380 — KEYED POSITIONS. A map is rebuilt entry by entry in Ordinal key order — the\n// order `keyedChildren` lists its nodes in.\nconst keyedMapEntries = (m, f) => {\n  const out = Object.create(null);\n  for (const key of Object.keys(m).sort()) out[key] = f(m[key]);\n  return out;\n};"
              yield! helpers
              yield
                  [ "// The nodes this node holds in keyed, non-structural positions, in declaration order."
                    "function keyedChildren(n) {"
                    "  const k = " + tsKindOf flat "n" + ";"
                    "  let ofKind;"
                    tsKindSwitch disc "  " listerArms "ofKind = [];"
                    "  return " + tsAppend ("ofKind" :: envListers) + ";"
                    "}" ]
                  |> String.concat "\n"
              yield
                  [ "function mapKeyed(f, n) {"
                    "  const k = " + tsKindOf flat "n" + ";"
                    "  let kind = {};"
                    tsKindSwitch disc "  " mapperArms "break;"
                    envLets + mapKeyedReturn
                    "}" ]
                  |> String.concat "\n"
              yield
                  [ "// The node with these nodes in its keyed positions, position for position (arity-preserving)."
                    "function withKeyedChildren(kids, n) {"
                    "  let i = 0;"
                    "  return mapKeyed((old) => (i < kids.length ? kids[i++] : old), n);"
                    "}"
                    ""
                    "// The generated full walk: every node position the vocabulary declares, structural and keyed."
                    "function idsUniqueFullWalk(root) {"
                    "  const seen = new Set();"
                    "  const stack = [root];"
                    "  while (stack.length > 0) {"
                    "    const x = stack.pop();"
                    "    if (seen.has(x.id)) return false;"
                    "    seen.add(x.id);"
                    "    stack.push(...children(x), ...keyedChildren(x));"
                    "  }"
                    "  return true;"
                    "}"
                    ""
                    "// The keyed witness — Core's `KeyedWitness` members over this module's node."
                    "const keyedWitness = {"
                    "  surface: \"the generated full walk over every node position the vocabulary declares\","
                    "  keyedChildren: keyedChildren,"
                    "  replaceKeyedChildren: (n, kids) => withKeyedChildren(kids, n),"
                    "  placeKeyedChild: (n, id) => {"
                    "    const ks = keyedChildren(n);"
                    "    return ks.length === 0 ? undefined : withKeyedChildren([{ ...ks[0], id: id }, ...ks.slice(1)], n);"
                    "  },"
                    "  idsUnique: idsUniqueFullWalk,"
                    "};" ]
                  |> String.concat "\n" ]
            |> String.concat "\n\n"

        text, [ "keyedChildren"; "withKeyedChildren"; "keyedWitness" ]

    /// Emission 3 — `slotsOf<T>(n)`: every `[field name, value]` pair the node holds at the declared
    /// type `T` (directly, optional or in a list), kind fields first, then the envelope's.
    let private tsSlotsDecl
        (disc: string)
        (flat: bool)
        (ctx: FSharpDerive.Ctx)
        (typeName: string)
        : string * string list =
        let isT (t: IdlType) =
            match t with
            | TEnum n
            | TRecord n
            | TUnion(n, _) -> n = typeName
            | _ -> false

        let ofField (owner: string) (f: IdlField) : string option =
            let e = tsProp owner f.Name
            let key = SourceLit.tsString f.Name

            let direct =
                match f.Type with
                | t when isT t -> Some(sprintf "[[%s, %s]]" key e)
                | TList t when isT t -> Some(sprintf "%s.map((__v) => [%s, __v])" e key)
                | _ -> None

            match f.Opt with
            | Optional -> direct |> Option.map (sprintf "(%s === undefined ? [] : %s)" e)
            | _ -> direct

        let arms =
            ctx.Kinds
            |> List.choose (fun k ->
                match k.Fields |> List.choose (ofField "k") with
                | [] -> None
                | xs -> Some(k.Tag, sprintf "ofKind = %s; break;" (tsAppend xs)))

        let env = ctx.Idl.NodeFields |> List.choose (ofField "n")
        let name = "slotsOf" + typeName

        [ sprintf
              "// Every [field name, value] pair this node holds at the declared type `%s`, kind fields first."
              typeName
          sprintf "function %s(n) {" name
          "  const k = " + tsKindOf flat "n" + ";"
          "  let ofKind;"
          tsKindSwitch disc "  " arms "ofKind = [];"
          "  return " + tsAppend ("ofKind" :: env) + ";"
          "}" ]
        |> String.concat "\n",
        [ name ]

    /// Emissions 5 and 6 — a union's fold and its field projections, the members of one object
    /// exported under the union's name (`Rule.fold`, `Trigger.owner`), as the F# host's `module`
    /// is. The object is declared as `<Union>$` and exported `as <Union>`, so a union named like a
    /// global the module reads (`Map`, `Object`, `Error`) shadows nothing inside the module.
    let private tsUnionModulesDecl
        (ctx: FSharpDerive.Ctx)
        (requests: FSharpDerive.Request list)
        : (string * string list) list =
        let disc = ctx.Idl.Wire.Discriminator

        let named =
            requests
            |> List.choose (fun r ->
                match r with
                | FSharpDerive.Request.Fold n -> Some n
                | FSharpDerive.Request.Projections(n, _) -> Some n
                | _ -> None)
            |> List.distinct

        let recursesIn (u: IdlUnion) (seen: Set<string>) (t: IdlType) =
            FSharpDerive.foldSelfIn ctx u seen t = Ok true

        let foldMember (u: IdlUnion) =
            let self = u.Name + "$"

            let rec foldE (t: IdlType) (e: string) (st: string) (d: int) : string =
                let x = sprintf "__x%d" d
                let s = sprintf "__s%d" d

                match t with
                | TUnion(n, _) when n = u.Name -> sprintf "%s.fold(folder, %s, %s)" self st e
                | TList i -> sprintf "%s.reduce((%s, %s) => %s, %s)" e s x (foldE i x s (d + 1)) st
                | TMap i ->
                    sprintf
                        "Object.keys(%s).sort().reduce((%s, %s) => %s, %s)"
                        e
                        s
                        x
                        (foldE i (sprintf "%s[%s]" e x) s (d + 1))
                        st
                | TRecord n ->
                    let fs =
                        ctx.Records
                        |> List.tryFind (fun r -> r.Name = n)
                        |> Option.map _.Fields
                        |> Option.defaultValue []
                        |> List.filter (fun f -> recursesIn u (Set.singleton n) f.Type)

                    foldFields fs (fun f -> tsProp e f.Name) st (d + 1)
                | _ -> st

            and foldField (f: IdlField) (e: string) (st: string) (d: int) =
                match f.Opt with
                | Optional -> sprintf "(%s === undefined ? %s : %s)" e st (foldE f.Type e st (d + 1))
                | _ -> foldE f.Type e st d

            /// Thread the state through several fields, left to right.
            and foldFields (fs: IdlField list) (access: IdlField -> string) (st: string) (d: int) =
                match fs with
                | [] -> st
                | [ f ] -> foldField f (access f) st d
                | _ ->
                    let c = sprintf "__c%d" d

                    let steps =
                        fs
                        |> List.map (fun f -> sprintf "%s = %s; " c (foldField f (access f) c (d + 1)))
                        |> String.concat ""

                    sprintf "(() => { let %s = %s; %sreturn %s; })()" c st steps c

            let arms =
                u.Cases
                |> List.choose (fun c ->
                    match c.Fields |> List.filter (fun f -> recursesIn u Set.empty f.Type) with
                    | [] -> None
                    | fs ->
                        Some(
                            sprintf
                                "      case %s: return %s;"
                                (SourceLit.tsString c.Tag)
                                (foldFields fs (fun f -> tsProp "v" f.Name) "state" 0)
                        ))

            [ sprintf "  // Fold `folder` over this value and every nested `%s` it holds, in preorder." u.Name
              "  fold(folder, state, v) {"
              "    state = folder(state, v);"
              "    switch (" + tsDiscProp disc "v" + ") {" ]
            @ arms
            @ [ "      default: return state;"; "    }"; "  }," ]
            |> String.concat "\n"

        let projectionMember (u: IdlUnion) (field: string) =
            let carriers =
                u.Cases
                |> List.filter (fun c -> c.Fields |> List.exists (fun f -> f.Name = field))

            let total = List.length carriers = List.length u.Cases

            [ yield
                  sprintf
                      "  // The `%s` field, %s."
                      field
                      (if total then
                           "which every case carries"
                       else
                           "where the case carries one (`undefined` where it does not)")
              yield "  " + SourceLit.tsKey field + "(v) {"
              yield "    switch (" + tsDiscProp disc "v" + ") {"
              for c in carriers do
                  yield sprintf "      case %s: return %s;" (SourceLit.tsString c.Tag) (tsProp "v" field)
              yield "      default: return undefined;"
              yield "    }"
              yield "  }," ]
            |> String.concat "\n"

        named
        |> List.choose (fun name -> ctx.Unions |> List.tryFind (fun u -> u.Name = name))
        |> List.map (fun u ->
            let folds = requests |> List.contains (FSharpDerive.Request.Fold u.Name)

            let fields =
                requests
                |> List.collect (fun r ->
                    match r with
                    | FSharpDerive.Request.Projections(n, fs) when n = u.Name -> fs
                    | _ -> [])
                |> List.distinct

            let members =
                (if folds then [ foldMember u ] else [])
                @ (fields |> List.map (projectionMember u))

            sprintf "// Phase 380 — derived members of `%s`, exported as `%s`.\n" u.Name u.Name
            + sprintf "const %s$ = {\n" u.Name
            + String.concat "\n\n" members
            + "\n};",
            [ sprintf "%s$ as %s" u.Name u.Name ])

    /// Emission 7 — `default<Tag>Spec` / `default<Record>` for every kind and record whose every
    /// field has a value without the caller, in the shape the generated decoder produces: an
    /// omitted-at-default member filled as the decoder refills it, a declared default rendered as
    /// the scaffold renders it, an absent optional and a host-only member absent.
    let private tsDefaultRecordsDecl
        (idl: Idl)
        (disc: string)
        (ctx: FSharpDerive.Ctx)
        : Result<string * string list, CodegenError> =
        let defaultFor (tag: string) (field: string) =
            idl.Defaults
            |> List.tryPick (fun d ->
                if d.Kind = tag && d.Field = field then
                    Some d.Value
                else
                    None)

        let sentinel (t: IdlType) =
            match t with
            | TClosure
            | TFn _
            | TOpaque -> true
            | _ -> false

        /// `None`: no value without the caller. `Some(Ok None)`: the member is absent.
        let fieldValue (declared: IdlValue option) (f: IdlField) : Result<string option, CodegenError> option =
            match declared, f.Opt with
            | _, OmitDefault d ->
                if sentinel f.Type then
                    Some(Ok(Some "null"))
                else
                    Some(tsDefaultLit idl disc f.Type d |> Result.map Some)
            | Some v, Required
            | Some v, Optional -> Some(typescriptValue idl f.Type v |> Result.map Some)
            | None, Required -> None
            | None, Optional
            | _, HostOnly -> Some(Ok None)

        let value
            (name: string)
            (extra: (string * string) list)
            (declared: IdlField -> IdlValue option)
            (fs: IdlField list)
            =
            let parts = fs |> List.map (fun f -> f, fieldValue (declared f) f)

            if parts |> List.exists (fun (_, v) -> v.IsNone) then
                None
            else
                parts
                |> List.map (fun (f, v) ->
                    v.Value
                    |> Result.map (Option.map (fun lit -> SourceLit.tsString f.Name + ": " + lit)))
                |> sequenceR
                |> Result.map (fun pieces ->
                    let pairs = (extra |> List.map (fun (k, v) -> k + ": " + v)) @ List.choose id pieces

                    sprintf
                        "// `%s` with every field at the value a caller need not pass.\nconst default%s = { %s };"
                        name
                        name
                        (String.concat ", " pairs),
                    "default" + name)
                |> Some

        let kinds =
            ctx.Kinds
            |> List.choose (fun k ->
                value
                    (k.Tag + "Spec")
                    [ tsDiscKey disc, SourceLit.tsString k.Tag ]
                    (fun f -> defaultFor k.Tag f.Name)
                    k.Fields)

        let records =
            ctx.Records |> List.choose (fun r -> value r.Name [] (fun _ -> None) r.Fields)

        (kinds @ records)
        |> sequenceR
        |> Result.map (fun xs -> xs |> List.map fst |> String.concat "\n\n", xs |> List.map snd)

    /// Emission 8 — `kindCategories`, `kindFieldNames`, `opFieldNames` as `Map`s of `Set`s and
    /// `envelopeFieldNames` as a `Set`, each in Ordinal order (the order the F# host's `Map` and
    /// `Set` iterate in).
    let private tsConstantsDecl (ctx: FSharpDerive.Ctx) : string * string list =
        let ordinal (xs: string list) =
            xs
            |> List.distinct
            |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b))

        let setLit (xs: string list) =
            match ordinal xs with
            | [] -> "new Set()"
            | ys -> "new Set([" + (ys |> List.map SourceLit.tsString |> String.concat ", ") + "])"

        let mapLit (entries: (string * string list) list) =
            match
                entries
                |> List.sortWith (fun (a, _) (b, _) -> System.String.CompareOrdinal(a, b))
            with
            | [] -> "new Map()"
            | es ->
                "new Map(["
                + (es
                   |> List.map (fun (k, vs) -> sprintf "[%s, %s]" (SourceLit.tsString k) (setLit vs))
                   |> String.concat ", ")
                + "])"

        let categories =
            ctx.Kinds
            |> List.map (fun k -> k.Category)
            |> List.distinct
            |> List.map (fun c -> c, ctx.Kinds |> List.filter (fun k -> k.Category = c) |> List.map _.Tag)

        let fieldsOf (ks: IdlKind list) =
            ks |> List.map (fun k -> k.Tag, k.Fields |> List.map _.Name)

        [ "// Phase 380 — VOCABULARY CONSTANTS. The kind tags of each declared category."
          "const kindCategories = " + mapLit categories + ";"
          ""
          "// The wire field names of each kind."
          "const kindFieldNames = " + mapLit (fieldsOf ctx.Kinds) + ";"
          ""
          "// The wire field names of the node envelope."
          "const envelopeFieldNames = "
          + setLit (ctx.Idl.NodeFields |> List.map _.Name)
          + ";"
          ""
          "// The wire field names of each tree op."
          "const opFieldNames = " + mapLit (fieldsOf ctx.Idl.Ops) + ";" ]
        |> String.concat "\n",
        [ "kindCategories"; "kindFieldNames"; "envelopeFieldNames"; "opFieldNames" ]

    // ---- the collecting decoders (Phase 377's `SpecDecoders`) ----

    /// The collecting decoder reference for a type — [[tsDecFn]]'s, arm for arm, where the type has
    /// independent positions to collect over; a leaf is [[tsDecFn]]'s decoder, which refuses once.
    let rec private tsColFn (t: IdlType) : Result<string, CodegenError> =
        match t with
        | TVar v -> Ok("col" + v)
        | TUnion(n, []) -> Ok("col" + n)
        | TUnion(n, args) ->
            args
            |> List.map tsColFn
            |> concatR ", "
            |> Result.map (fun a -> "((x) => col" + n + "(" + a + ", x))")
        | TNode -> Ok "colNode"
        | TList inner -> tsColFn inner |> Result.map (fun c -> "cList(" + c + ")")
        | TMap vt -> tsColFn vt |> Result.map (fun c -> "cMap(" + c + ")")
        | TRecord n -> Ok("col" + n)
        | _ -> tsDecFn t

    /// One member read back under [[tsDecField]]'s presence rules, collecting. A leaf member (and a
    /// host-only one) IS [[tsDecField]]'s expression, so its presence rules are the short-circuiting
    /// decoder's by construction.
    let private tsColField (idl: Idl) (disc: string) (f: IdlField) : Result<string, CodegenError> =
        let key = SourceLit.tsString f.Name

        match f.Opt with
        | HostOnly -> tsDecField idl disc f
        | _ when not (FSharpCodec.collectsOver f.Type) -> tsDecField idl disc f
        | Required -> tsColFn f.Type |> Result.map (fun c -> "cReq(" + key + ", fs, " + c + ")")
        | Optional -> tsColFn f.Type |> Result.map (fun c -> "cOpt(" + key + ", fs, " + c + ")")
        | OmitDefault d ->
            tsDefaultLit idl disc f.Type d
            |> Result.bind (fun lit ->
                tsColFn f.Type
                |> Result.map (fun c -> "cDef(" + key + ", fs, " + c + ", " + lit + ")"))

    /// Read every member in declaration order, then build the object [[tsFieldObject]] builds — the
    /// same keys in the same order — or throw every member's refusals, in that order.
    let private tsColObject
        (idl: Idl)
        (disc: string)
        (indent: string)
        (extra: (string * string) list)
        (fields: IdlField list)
        : Result<string, CodegenError> =
        fields
        |> List.map (tsColField idl disc)
        |> sequenceR
        |> Result.map (fun reads ->
            let pairs =
                (extra |> List.map (fun (k, v) -> k + ": " + v))
                @ (fields
                   |> List.mapi (fun i f -> SourceLit.tsString f.Name + ": v[" + string i + "]"))

            let literal = "{ " + String.concat ", " pairs + " }"

            match reads with
            | [] -> indent + "return " + literal + ";"
            | _ ->
                indent
                + "const v = cAll(["
                + (reads |> List.map (fun r -> "() => " + r) |> String.concat ", ")
                + "]);\n"
                + indent
                + "return "
                + literal
                + ";")

    let private tsColUnion (idl: Idl) (disc: string) (tokens: HardenPolicy) (u: IdlUnion) =
        let argList =
            match u.Params with
            | [] -> "j"
            | ps -> (ps |> List.map (fun p -> "col" + p) |> String.concat ", ") + ", j"

        let arm (c: IdlUnionCase) =
            tsColObject idl disc "        " [ tsDiscKey disc, SourceLit.tsString c.Tag ] c.Fields
            |> Result.map (fun body -> "      case " + SourceLit.tsString c.Tag + ": {\n" + body + "\n      }")

        let taggedR =
            u.Cases
            |> List.map arm
            |> concatR "\n"
            |> Result.map (fun arms ->
                "  if (isObj(j)) {\n    const fs = j;\n    switch (dTag(j)) {\n"
                + arms
                + "\n      default: return dUnknown("
                + SourceLit.tsString (oneOf (u.Cases |> List.map (fun c -> c.Tag)))
                + ", "
                + SourceLit.tsString ("unknown " + u.Name + " case: ")
                + " + "
                + tsDiscProp disc "j"
                + ");\n    }\n  }")

        // A transparent union reads its single-field case bare, collecting through it.
        let untagged =
            match TransparentUnion.tag tokens u with
            | Some ttag ->
                match u.Cases |> List.tryFind (fun c -> c.Tag = ttag) with
                | Some({ Fields = [ f ] }) ->
                    tsColFn f.Type
                    |> Result.map (fun cfn ->
                        "  return { "
                        + tsDiscKey disc
                        + ": "
                        + SourceLit.tsString ttag
                        + ", "
                        + SourceLit.tsString f.Name
                        + ": "
                        + cfn
                        + "(j) };")
                | _ -> Error(transparentArity u.Name ttag)
            | None ->
                Ok(
                    "  return dFail('WrongKind', 'object', "
                    + SourceLit.tsString ("expected a " + u.Name + " object")
                    + ");"
                )

        taggedR
        |> Result.bind (fun tagged ->
            untagged
            |> Result.map (fun untagged ->
                "function col"
                + u.Name
                + "("
                + argList
                + ") {\n"
                + tagged
                + "\n"
                + untagged
                + "\n}"))

    /// The collecting prelude: the refusal LIST a collecting decoder throws, and the readers that
    /// gather it. It states the defect order, which a test pins.
    let private tsCollectingPrelude =
        """// ---------------------------------------------------------------------------
// Phase 380 — COLLECTING DECODERS. Beside every short-circuiting `dec*` decoder above, a `col*`
// decoder answers EVERY defect it finds, in one deterministic order — the F# host's:
//   - an object's members in FIELD DECLARATION ORDER (a node: `id`, its kind, then its envelope
//     fields), each member's defects at that member's position, its nested defects included
//     (depth-first);
//   - a list's items in index order, and a map's entries in document order;
//   - a leaf (a scalar, an enum, a sentinel, a hosted slot, verbatim JSON) reports at most one
//     defect, and so does a value of the wrong kind or an absent or unknown discriminator, whose
//     members are never read.
// The first defect of a collecting decoder is the defect its short-circuiting twin reports, and on
// a clean input both answer the same value. Codes and paths are Core's `DecodeError`'s.
// ---------------------------------------------------------------------------
// Several refusals at once — what a collecting decoder throws when a position it read refused.
class DecodeFaults extends Error {
  constructor(faults) {
    super(faults.length + ' decode faults');
    this.faults = faults;
  }
}
// One read: its value, or its refusals (one for a short-circuiting refusal, every one for a
// collecting one).
const cRun = (read) => {
  try {
    return { ok: true, value: read() };
  } catch (e) {
    if (e instanceof DecodeFault) return { ok: false, faults: [e] };
    if (e instanceof DecodeFaults) return { ok: false, faults: e.faults };
    throw e;
  }
};
// Every read, in order: their values when all decoded, else every read's refusals, in that order.
const cAll = (reads) => {
  const results = reads.map(cRun);
  const faults = results.flatMap((r) => (r.ok ? [] : r.faults));
  if (faults.length > 0) throw new DecodeFaults(faults);
  return results.map((r) => r.value);
};
// One step further from the root, for every refusal a member or an item raised.
const cAt = (step, f) => {
  try {
    return f();
  } catch (e) {
    if (e instanceof DecodeFault) e.path.unshift(step);
    else if (e instanceof DecodeFaults) for (const x of e.faults) x.path.unshift(step);
    throw e;
  }
};
const cList = (dec) => (j) => {
  if (!Array.isArray(j)) return dFail('WrongKind', 'array', 'expected an array');
  return cAll(j.map((_, i) => () => cAt(i, () => dec(dRead(j, i)))));
};
// Every entry is checked, in document order; a repeated key keeps its FIRST value, as `dMap` does.
const cMap = (dec) => (j) => {
  const o = dObj(j);
  const members = dMembersOf.get(o);
  const entries = (members === undefined)
    ? Object.keys(o).map((k) => [k, () => cAt(k, () => dec(dRead(o, k)))])
    : members.map(([k, v, floatTok]) => [k, () => cAt(k, () => { dFloatTok = floatTok; return dec(v); })]);
  const values = cAll(entries.map(([, read]) => read));
  const out = Object.create(null);
  entries.forEach(([k], i) => { if (!hasOwn(out, k)) out[k] = values[i]; });
  return out;
};
const cReq = (name, fs, dec) => hasOwn(fs, name) ? cAt(name, () => dec(dRead(fs, name))) : dMissing(name, "missing required field '" + name + "'");
const cOpt = (name, fs, dec) => hasOwn(fs, name) ? cAt(name, () => dec(dRead(fs, name))) : undefined;
const cDef = (name, fs, dec, dflt) => hasOwn(fs, name) ? cAt(name, () => dec(dRead(fs, name))) : dflt;"""

    /// The collecting prelude, the collecting decoders (node kind, node, unions, records, specs)
    /// and the public entries: per kind `decode<Tag>Spec` / `decode<Tag>SpecAll` over the kind's
    /// object, per node `decodeNodeJson` / `decodeNodeJsonAll` over a parsed value and
    /// `decodeNodeAll` over text — each answering `decodeNode`'s `{ ok, value }` / `{ ok, error }`,
    /// the collecting ones `{ ok: false, errors }`.
    let private tsCollectingDecl
        (idl: Idl)
        (disc: string)
        (flat: bool)
        (kinds: IdlKind list)
        (unions: IdlUnion list)
        (records: IdlRecord list)
        : Result<string * string list, CodegenError> =
        let colKind =
            "function colKind(j) {\n  if (!isObj(j)) return dFail('WrongKind', 'object', 'expected a kind object');\n  switch (dTag(j)) {\n"
            + (kinds
               |> List.map (fun k -> "    case " + SourceLit.tsString k.Tag + ": return col" + k.Tag + "Spec(j);")
               |> String.concat "\n")
            + "\n    default: return dUnknown("
            + SourceLit.tsString (oneOf (kinds |> List.map (fun k -> k.Tag)))
            + ", 'unknown node kind: ' + "
            + tsDiscProp disc "j"
            + ");\n  }\n}"

        let colNode =
            idl.NodeFields
            |> List.map (tsColField idl disc)
            |> sequenceR
            |> Result.map (fun envelope ->
                let kindRead = if flat then "colKind(j)" else "cReq('kind', fs, colKind)"

                let reads =
                    [ "dReq('id', fs, dStr)"; kindRead ] @ envelope
                    |> List.map (fun r -> "() => " + r)
                    |> String.concat ", "

                let envelopeAssigns =
                    idl.NodeFields
                    |> List.mapi (fun i f -> sprintf ", %s: v[%d]" (SourceLit.tsKey f.Name) (i + 2))
                    |> String.concat ""

                let build =
                    if flat then
                        "Object.assign({}, v[1], { id: v[0]" + envelopeAssigns + " })"
                    else
                        "{ id: v[0], kind: v[1]" + envelopeAssigns + " }"

                "function colNode(j) {\n  const fs = dObj(j);\n  const v = cAll(["
                + reads
                + "]);\n  return "
                + build
                + ";\n}")

        let objectDecoder (name: string) (extra: (string * string) list) (fields: IdlField list) =
            tsColObject idl disc "  " extra fields
            |> Result.map (fun body -> "function col" + name + "(j) {\n  const fs = dObj(j);\n" + body + "\n}")

        let group =
            [ Ok colKind; colNode ]
            @ (unions |> List.map (tsColUnion idl disc idl.Harden))
            @ (records |> List.map (fun r -> objectDecoder r.Name [] r.Fields))
            @ (kinds
               |> List.map (fun k ->
                   objectDecoder (k.Tag + "Spec") [ tsDiscKey disc, SourceLit.tsString k.Tag ] k.Fields))
            |> concatR "\n\n"

        let entries =
            [ yield
                  """// The public per-spec decoders. `decode<Tag>Spec` reads one kind's object (a parsed value) and
// answers its FIRST defect, as `{ ok: false, error }`; `decode<Tag>SpecAll` reads the same object and
// answers EVERY defect, as `{ ok: false, errors }`, in the order stated above. Each defect carries
// `decodeNode`'s members: `{ code, path, expected, message }`.
const cDefect = (e) => ({ code: e.code, path: e.path, expected: e.expected, message: e.message });
const cFirstOf = (read) => {
  try {
    dFloatTok = false;
    return { ok: true, value: read() };
  } catch (e) {
    if (!(e instanceof DecodeFault)) throw e;
    return { ok: false, error: cDefect(e) };
  }
};
const cEveryOf = (read) => {
  try {
    dFloatTok = false;
    return { ok: true, value: read() };
  } catch (e) {
    if (e instanceof DecodeFault) return { ok: false, errors: [cDefect(e)] };
    if (e instanceof DecodeFaults) return { ok: false, errors: e.faults.map(cDefect) };
    throw e;
  }
};"""
              for k in kinds do
                  yield sprintf "function decode%sSpec(j) {\n  return cFirstOf(() => dec%sSpec(j));\n}" k.Tag k.Tag
                  yield sprintf "function decode%sSpecAll(j) {\n  return cEveryOf(() => col%sSpec(j));\n}" k.Tag k.Tag
              yield
                  "// The whole node over a parsed value: the first defect, or every defect.\nfunction decodeNodeJson(j) {\n  return cFirstOf(() => decNode(j));\n}"
              yield "function decodeNodeJsonAll(j) {\n  return cEveryOf(() => colNode(j));\n}"
              yield
                  "// `decodeNode`'s collecting twin: a parser refusal is the one defect, else every defect.\nfunction decodeNodeAll(s) {\n  return cEveryOf(() => {\n    const root = dParse(s);\n    dFloatTok = false;\n    return colNode(root);\n  });\n}" ]
            |> String.concat "\n\n"

        let names =
            (kinds
             |> List.collect (fun k -> [ "decode" + k.Tag + "Spec"; "decode" + k.Tag + "SpecAll" ]))
            @ [ "decodeNodeJson"; "decodeNodeJsonAll"; "decodeNodeAll" ]

        group
        |> Result.map (fun g -> [ tsCollectingPrelude; g; entries ] |> String.concat "\n\n", names)

    // ---- Phase 381 — the derived members' DECLARATIONS ----

    /// One derived emission: the module text, the names its `export` names, and one declaration
    /// per exported name, for the declaration file. Built in ONE place for both files, so a member
    /// the module emits and the declaration the file writes for it are chosen by the same request
    /// and typed from the same analysis.
    type private DerivedPart =
        { Text: string
          Names: string list
          Decls: string list }

    /// The union of a kind list's wire tags as TypeScript string-literal types, `never` for none.
    let private tsTagUnion (kinds: IdlKind list) =
        match kinds with
        | [] -> "never"
        | _ -> kinds |> List.map (fun k -> SourceLit.tsString k.Tag) |> String.concat " | "

    /// `<A, B>` for a declaration's type parameters, or nothing.
    let private tsGeneric (ps: string list) =
        match ps with
        | [] -> ""
        | _ -> "<" + String.concat ", " ps + ">"

    /// `StructuralAccess` — the four access members and the witness object over them.
    let private structuralDecls (kinds: IdlKind list) : string list =
        let tag = tsTagUnion kinds

        [ "export declare function wireTag(n: Node): " + tag + ";"
          "export declare const allWireTags: ReadonlyArray<" + tag + ">;"
          "export declare function children(n: Node): Array<Node>;"
          "export declare function withChildren(kids: Array<Node>, n: Node): Node;"
          "export declare const nodeWitness: { id(n: Node): string; kindTag(n: Node): "
          + tag
          + "; children(n: Node): Array<Node>; replaceChildren(n: Node, kids: Array<Node>): Node };" ]

    /// `KeyedPositions` — the keyed walk and its witness object.
    let private keyedDecls: string list =
        [ "export declare function keyedChildren(n: Node): Array<Node>;"
          "export declare function withKeyedChildren(kids: Array<Node>, n: Node): Node;"
          "export declare const keyedWitness: { surface: string; keyedChildren(n: Node): Array<Node>; replaceKeyedChildren(n: Node, kids: Array<Node>): Node; placeKeyedChild(n: Node, id: string): Node | undefined; idsUnique(root: Node): boolean };" ]

    /// `SlotsOf T` — the pairs hold a `T`; a generic union's instantiations differ field by field,
    /// so its pairs hold `unknown`, the F# host's `obj`, for the same reason.
    let private slotsDecls (ctx: FSharpDerive.Ctx) (typeName: string) : string list =
        let generic =
            ctx.Unions
            |> List.exists (fun u -> u.Name = typeName && not (List.isEmpty u.Params))

        let elem = if generic then "unknown" else typeName

        [ sprintf "export declare function slotsOf%s(n: Node): Array<[string, %s]>;" typeName elem ]

    /// `Fold U` / `Projections (U, fields)` — the object they are exported as: `fold<S>(folder,
    /// state, v)` and one method per projected field, `T` where every case carries the field and
    /// none optionally, `T | undefined` otherwise. A generic union's parameters are each method's.
    let private unionModuleDecls (u: IdlUnion) (folds: bool) (fields: string list) : Result<string list, CodegenError> =
        let self = u.Name + tsGeneric u.Params

        let rec stateName (s: string) =
            if List.contains s u.Params then stateName (s + "_") else s

        let st = stateName "S"

        let foldDecl =
            sprintf
                "  fold%s(folder: (state: %s, v: %s) => %s, state: %s, v: %s): %s;"
                (tsGeneric (st :: u.Params))
                st
                self
                st
                st
                self
                st

        let projectionDecl (field: string) =
            let carried =
                u.Cases
                |> List.choose (fun c -> c.Fields |> List.tryFind (fun f -> f.Name = field))

            let alwaysPresent =
                List.length carried = List.length u.Cases
                && carried
                   |> List.forall (fun f ->
                       match f.Opt with
                       | Optional
                       | HostOnly -> false
                       | Required
                       | OmitDefault _ -> true)

            // The F# path refused a projection no case carries before this is reached.
            let ty =
                match carried with
                | f :: _ -> tsDeclType f.Type
                | [] -> Ok "never"

            ty
            |> Result.map (fun ty ->
                sprintf
                    "  %s%s(v: %s): %s;"
                    (SourceLit.tsKey field)
                    (tsGeneric u.Params)
                    self
                    (if alwaysPresent then ty else ty + " | undefined"))

        (if folds then [ Ok foldDecl ] else []) @ (fields |> List.map projectionDecl)
        |> sequenceR
        |> Result.map (fun members ->
            [ sprintf "export declare const %s: {\n%s\n};" u.Name (String.concat "\n" members) ])

    /// `VocabularyConstants` — `Map`s of `Set`s and a `Set`, declared read-only.
    let private constantsDecls (kinds: IdlKind list) : string list =
        let tag = tsTagUnion kinds

        [ "export declare const kindCategories: ReadonlyMap<string, ReadonlySet<"
          + tag
          + ">>;"
          "export declare const kindFieldNames: ReadonlyMap<"
          + tag
          + ", ReadonlySet<string>>;"
          "export declare const envelopeFieldNames: ReadonlySet<string>;"
          "export declare const opFieldNames: ReadonlyMap<string, ReadonlySet<string>>;" ]

    /// `SpecDecoders` — the public decoders' answers: the first defect as `decodeNode`'s `error`,
    /// or every defect, in order, as `errors`.
    let private collectingDecls (kinds: IdlKind list) : string list =
        let first v =
            "{ ok: true; value: " + v + " } | { ok: false; error: " + refusalTypeName + " }"

        let every v =
            "{ ok: true; value: "
            + v
            + " } | { ok: false; errors: Array<"
            + refusalTypeName
            + "> }"

        [ for k in kinds do
              yield sprintf "export declare function decode%sSpec(j: unknown): %s;" k.Tag (first (k.Tag + "Spec"))
              yield sprintf "export declare function decode%sSpecAll(j: unknown): %s;" k.Tag (every (k.Tag + "Spec"))
          yield "export declare function decodeNodeJson(j: unknown): " + first "Node" + ";"
          yield "export declare function decodeNodeJsonAll(j: unknown): " + every "Node" + ";"
          yield "export declare function decodeNodeAll(s: string): " + every "Node" + ";" ]

    /// Phase 380/381 — the requested derivations as parts, admitted exactly when the F# path
    /// admits them. `None` when nothing is requested: the plain module and the plain declarations.
    let private tsDerivedParts
        (requests: FSharpDerive.Request list)
        (decoders: bool)
        (idl: Idl)
        (kindTags: string list)
        : Result<DerivedPart list option, CodegenError> =
        if List.isEmpty requests && not decoders then
            Ok None
        else
            let kinds = kindTags |> List.choose (fun t -> CodegenLookup.tryKind idl t)

            let _, unions, records = referenced idl kinds
            let msg = msgCarrying idl
            let disc = idl.Wire.Discriminator
            let flat = idl.Wire.NodeEnvelope = NodeEnvelopeShape.FlatKind

            let ctx: FSharpDerive.Ctx =
                { Idl = idl
                  Msg = msg
                  Kinds = kinds
                  Unions = unions
                  Records = records
                  Projections = Map.empty }

            let has r = List.contains r requests

            let publicAccess =
                has FSharpDerive.Request.StructuralAccess
                || has FSharpDerive.Request.KeyedPositions

            // The F# path decides admissibility: its structural witness (which refuses a kind
            // mixing a node list with other children) and its derivations, texts discarded.
            let admitted =
                (if publicAccess then
                     FSharpCodec.witnessDecl true Map.empty msg kinds |> Result.map ignore
                 else
                     Ok())
                |> Result.bind (fun () -> FSharpDerive.derivedDecl ctx requests |> Result.map ignore)

            let slots =
                requests
                |> List.choose (fun r ->
                    match r with
                    | FSharpDerive.Request.SlotsOf t -> Some t
                    | _ -> None)
                |> List.distinct

            let part (text: string, names: string list) (decls: string list) =
                { Text = text
                  Names = names
                  Decls = decls }

            // `tsUnionModulesDecl` answers one entry per requested union, in this same order.
            let unionModules () =
                let named =
                    requests
                    |> List.choose (fun r ->
                        match r with
                        | FSharpDerive.Request.Fold n -> Some n
                        | FSharpDerive.Request.Projections(n, _) -> Some n
                        | _ -> None)
                    |> List.distinct
                    |> List.choose (fun name -> ctx.Unions |> List.tryFind (fun u -> u.Name = name))

                let fieldsOf (u: IdlUnion) =
                    requests
                    |> List.collect (fun r ->
                        match r with
                        | FSharpDerive.Request.Projections(n, fs) when n = u.Name -> fs
                        | _ -> [])
                    |> List.distinct

                List.zip named (tsUnionModulesDecl ctx requests)
                |> List.map (fun (u, emitted) ->
                    unionModuleDecls u (has (FSharpDerive.Request.Fold u.Name)) (fieldsOf u)
                    |> Result.map (part emitted))

            // `default<N>` is a value of the declared type `N`: a `<Tag>Spec` or a record.
            let defaultDecls (names: string list) =
                names
                |> List.map (fun n -> sprintf "export declare const %s: %s;" n (n.Substring "default".Length))

            admitted
            |> Result.bind (fun () ->
                [ (if publicAccess then
                       [ Ok(part (tsStructuralDecl disc flat kinds) (structuralDecls kinds)) ]
                   else
                       [])
                  (if has FSharpDerive.Request.KeyedPositions then
                       [ Ok(part (tsKeyedDecl disc flat ctx) keyedDecls) ]
                   else
                       [])
                  slots
                  |> List.map (fun t -> Ok(part (tsSlotsDecl disc flat ctx t) (slotsDecls ctx t)))
                  (if has FSharpDerive.Request.DefaultRecords then
                       [ tsDefaultRecordsDecl idl disc ctx
                         |> Result.map (fun (text, names) -> part (text, names) (defaultDecls names)) ]
                   else
                       [])
                  (if has FSharpDerive.Request.VocabularyConstants then
                       [ Ok(part (tsConstantsDecl ctx) (constantsDecls kinds)) ]
                   else
                       [])
                  unionModules ()
                  (if decoders then
                       [ tsCollectingDecl idl disc flat kinds unions records
                         |> Result.map (fun emitted -> part emitted (collectingDecls kinds)) ]
                   else
                       []) ]
                |> List.concat
                |> sequenceR
                |> Result.map Some)

    /// Phase 380 — `typescriptModule` plus the requested derivations, appended after its members
    /// with one further `export` naming them; no request and no decoders IS `typescriptModule`,
    /// byte for byte. A request the F# path refuses is refused here with the F# path's error.
    let typescriptModuleDerived
        (requests: FSharpDerive.Request list)
        (decoders: bool)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        typescriptModule idl kindTags
        |> Result.bind (fun baseText ->
            tsDerivedParts requests decoders idl kindTags
            |> Result.map (fun parts ->
                match parts with
                | None -> baseText
                | Some parts ->
                    let texts = parts |> List.map _.Text

                    let exported =
                        match parts |> List.collect _.Names with
                        | [] -> []
                        | names -> [ "export { " + String.concat ", " names + " };" ]

                    String.concat "\n\n" ((baseText :: texts) @ exported) |> normalizeEol))

    /// Phase 381 — `typescriptDeclarations` plus one declaration per member the derived module
    /// exports, appended after the file's own; no request and no decoders IS
    /// `typescriptDeclarations`, byte for byte, and a request is refused exactly as
    /// [[typescriptModuleDerived]] refuses it. The parts are the module's own, so the members the
    /// module exports and the members this file declares are chosen by one request list.
    ///
    /// A requested `MapMsg` is not declared, because the module exports nothing for it; the file's
    /// header comment says why.
    let typescriptDeclarationsDerived
        (requests: FSharpDerive.Request list)
        (decoders: bool)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        typescriptDeclarations idl kindTags
        |> Result.bind (fun baseText ->
            tsDerivedParts requests decoders idl kindTags
            |> Result.map (fun parts ->
                match parts with
                | None -> baseText
                | Some parts ->
                    let header =
                        [ yield
                              "// Phase 381 — the derived members the module was generated with are declared after its own, one declaration per exported name."
                          if List.contains FSharpDerive.Request.MapMsg requests then
                              yield
                                  "// `mapMsg` is not declared: the module emits no `mapMsg`, because this host holds a handler slot as its sentinel's `null` and carries no message type, so a message map has nothing to rewrite." ]
                        |> String.concat "\n"

                    // The base file's first line is its header comment; the derived lines join it.
                    let firstBreak = baseText.IndexOf '\n'

                    let headed =
                        baseText.Substring(0, firstBreak)
                        + "\n"
                        + header
                        + baseText.Substring firstBreak

                    let decls =
                        parts
                        |> List.map (fun p -> String.concat "\n" p.Decls)
                        |> List.filter (fun s -> s <> "")

                    String.concat "\n\n" (headed.TrimEnd '\n' :: decls) + "\n" |> normalizeEol))
