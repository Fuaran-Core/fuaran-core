namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Idl.Emit.Core
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
    // (`Derive.structural`), which declarations hold a node (`Decl.NodeHolders`),
    // which fields a fold descends (`Decl.foldSelfIn`), when a defect is a leaf's
    // (`SlotTraversal.collectsOver`). Phase 524 — the plans and walks are `Derive`'s, one per
    // derivation, and this file prints them. ADMISSIBILITY IS THE F# PATH'S: the requests are run through
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
    let private tsStructuralDecl
        (disc: string)
        (flat: bool)
        (kinds: IdlKind list)
        : Result<string * string list, CodegenError> =
        let bearing =
            kinds
            |> List.choose (fun k ->
                match Derive.structuralFields k.Fields with
                | [] -> None
                | fs -> Some(k, fs))

        let rebuild (assigns: string) =
            if flat then
                sprintf "{ ...n, %s }" assigns
            else
                sprintf "{ ...n, kind: { ...k, %s } }" assigns

        let childArms =
            bearing
            |> List.map (fun (k, fs) ->
                let items =
                    fs
                    |> List.map (fun (f, isList) -> (if isList then "..." else "") + tsProp "k" f.Name)
                    |> String.concat ", "

                k.Tag, sprintf "return [%s];" items)

        let replaceArms =
            bearing
            |> List.map (fun (k, fs) ->
                Derive.childLayout k.Tag fs
                |> Result.map (fun layout ->
                    let assigns =
                        match layout with
                        | ChildLayout.Whole f -> SourceLit.tsKey f.Name + ": kids"
                        | ChildLayout.Positional fs ->
                            fs
                            |> List.mapi (fun i f -> sprintf "%s: kids[%d]" (SourceLit.tsKey f.Name) i)
                            |> String.concat ", "

                    k.Tag, sprintf "return %s;" (rebuild assigns)))
            |> sequenceR

        let allTags =
            "const allWireTags = ["
            + (kinds |> List.map (fun k -> SourceLit.tsString k.Tag) |> String.concat ", ")
            + "];"

        replaceArms
        |> Result.map (fun replaceArms ->
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
            [ "wireTag"; "allWireTags"; "children"; "withChildren"; "nodeWitness" ])

    /// The TypeScript lister of the nodes a value holds, in declaration order; a map in Ordinal key
    /// order, the order the F# host's `Map` iterates in.
    let private tsLister: PositionPrinter =
        { Node = sprintf "[%s]"
          List = fun e x b -> sprintf "%s.flatMap((%s) => %s)" e x b
          MapItem = fun e x -> sprintf "%s[%s]" e x
          Map = fun e x b _ -> sprintf "Object.keys(%s).sort().flatMap((%s) => %s)" e x b
          Named = sprintf "nodesIn%s(%s)"
          Other = fun _ -> "[]"
          OptionalVar = id
          Optional = fun e b -> sprintf "(%s === undefined ? [] : %s)" e b }

    /// The TypeScript rebuilder of a value with `f` applied to every node it holds.
    let private tsMapper: PositionPrinter =
        { Node = sprintf "f(%s)"
          List = fun e x b -> sprintf "%s.map((%s) => %s)" e x b
          MapItem = fun _ x -> x
          Map = fun e x b _ -> sprintf "keyedMapEntries(%s, (%s) => %s)" e x b
          Named = sprintf "mapNodesIn%s(f, %s)"
          Other = id
          OptionalVar = id
          Optional = fun e b -> sprintf "(%s === undefined ? undefined : %s)" e b }

    /// Emission 2 — the keyed walk: per-declaration node listers and rebuilders, the node-level
    /// `keyedChildren` / `withKeyedChildren` (arity-preserving), the full walk, and `keyedWitness`
    /// (Core's `KeyedWitness` members). Every node position that is not a structural child: an
    /// optional node, a node in a record, a union case, a list or a map, and the node envelope.
    let private tsKeyedDecl (disc: string) (flat: bool) (ctx: Decl) : Result<string * string list, CodegenError> =
        let hs = ctx.NodeHolders
        let collectField (f: IdlField) e = Derive.positionsOf tsLister hs f e
        let mapField (f: IdlField) e = Derive.positionsOf tsMapper hs f e

        /// `const __m<i> = …;` per field, then the object of the rebuilt members.
        let rebuilt (indent: string) (fs: IdlField list) (access: IdlField -> string) =
            fs
            |> List.mapi (fun i f -> mapField f (access f) |> Result.map (fun b -> i, f, b))
            |> sequenceR
            |> Result.map (fun xs ->
                let lets =
                    xs
                    |> List.map (fun (i, _, b) -> sprintf "%sconst __m%d = %s;\n" indent i b)
                    |> String.concat ""

                let assigns =
                    xs
                    |> List.map (fun (i, f, _) -> sprintf "%s: __m%d" (SourceLit.tsKey f.Name) i)
                    |> String.concat ", "

                lets, assigns)

        let listed (access: IdlField -> string) (fs: IdlField list) =
            fs
            |> List.map (fun f -> collectField f (access f))
            |> sequenceR
            |> Result.map tsAppend

        let recordHelpers (r: IdlRecord, fs: IdlField list) =
            match listed (tsProp "v" << _.Name) fs, rebuilt "  " fs (tsProp "v" << _.Name) with
            | Error e, _
            | _, Error e -> Error e
            | Ok items, Ok(lets, assigns) ->
                Ok
                    [ sprintf "function nodesIn%s(v) {\n  return %s;\n}" r.Name items
                      sprintf "function mapNodesIn%s(f, v) {\n%s  return { ...v, %s };\n}" r.Name lets assigns ]

        let unionHelpers (u: IdlUnion, holding: (IdlUnionCase * (int * IdlField) list) list, _: bool) =
            let tag = tsDiscProp ctx.Idl.Wire.Discriminator "v"

            let listerArms =
                holding
                |> List.map (fun (c, fs) ->
                    listed (tsProp "v" << _.Name) (List.map snd fs)
                    |> Result.map (sprintf "    case %s: return %s;" (SourceLit.tsString c.Tag)))
                |> sequenceR

            let mapperArms =
                holding
                |> List.map (fun (c, fs) ->
                    rebuilt "      " (List.map snd fs) (tsProp "v" << _.Name)
                    |> Result.map (fun (lets, assigns) ->
                        sprintf
                            "    case %s: {\n%s      return { ...v, %s };\n    }"
                            (SourceLit.tsString c.Tag)
                            lets
                            assigns))
                |> sequenceR

            match listerArms, mapperArms with
            | Error e, _
            | _, Error e -> Error e
            | Ok listerArms, Ok mapperArms ->
                Ok
                    [ sprintf "function nodesIn%s(v) {\n  switch (%s) {\n" u.Name tag
                      + String.concat "\n" (listerArms @ [ "    default: return [];" ])
                      + "\n  }\n}"
                      sprintf "function mapNodesIn%s(f, v) {\n  switch (%s) {\n" u.Name tag
                      + String.concat "\n" (mapperArms @ [ "    default: return v;" ])
                      + "\n  }\n}" ]

        // This host holds the WIRE shape, so a kind's keyed positions are its wire fields.
        Derive.keyedPlan ctx (fun k -> Ok k.Fields)
        |> Result.bind (fun plan ->
            let helpers =
                (plan.Records |> List.map recordHelpers)
                @ (plan.Unions |> List.map unionHelpers)
                |> sequenceR
                |> Result.map List.concat

            let listerArms =
                plan.Kinds
                |> List.map (fun (k, fs) ->
                    listed (tsProp "k" << _.Name) fs
                    |> Result.map (fun items -> k.Tag, sprintf "ofKind = %s; break;" items))
                |> sequenceR

            let envListers =
                plan.Envelope
                |> List.map (fun f -> collectField f (tsProp "n" f.Name))
                |> sequenceR

            let mapperArms =
                plan.Kinds
                |> List.map (fun (k, fs) ->
                    rebuilt "      " fs (tsProp "k" << _.Name)
                    |> Result.map (fun (lets, assigns) ->
                        k.Tag, sprintf "{\n%s      kind = { %s };\n      break;\n    }" lets assigns))
                |> sequenceR

            let envMappers =
                plan.Envelope
                |> List.mapi (fun i f -> mapField f (tsProp "n" f.Name) |> Result.map (fun b -> i, f, b))
                |> sequenceR

            match helpers, listerArms, envListers, mapperArms, envMappers with
            | Error e, _, _, _, _
            | _, Error e, _, _, _
            | _, _, Error e, _, _
            | _, _, _, Error e, _
            | _, _, _, _, Error e -> Error e
            | Ok helpers, Ok listerArms, Ok envListers, Ok mapperArms, Ok envMappers ->
                let envLets =
                    envMappers
                    |> List.map (fun (i, _, b) -> sprintf "  const __e%d = %s;\n" i b)
                    |> String.concat ""

                let envAssigns =
                    envMappers
                    |> List.map (fun (i, f, _) -> sprintf ", %s: __e%d" (SourceLit.tsKey f.Name) i)
                    |> String.concat ""

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

                Ok(text, [ "keyedChildren"; "withKeyedChildren"; "keyedWitness" ]))

    /// Emission 3 — `slotsOf<T>(n)`: every `[field name, value]` pair the node holds at the declared
    /// type `T` (directly, optional or in a list), kind fields first, then the envelope's.
    let private tsSlotsDecl (disc: string) (flat: bool) (ctx: Decl) (typeName: string) : string * string list =
        let ofField (owner: string) (f: IdlField) : string option =
            let e = tsProp owner f.Name
            let key = SourceLit.tsString f.Name

            Derive.slotPosition typeName f
            |> Option.map (fun at ->
                let direct =
                    if at.List then
                        sprintf "%s.map((__v) => [%s, __v])" e key
                    else
                        sprintf "[[%s, %s]]" key e

                if at.Optional then
                    sprintf "(%s === undefined ? [] : %s)" e direct
                else
                    direct)

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
        (ctx: Decl)
        (requests: Derivation list)
        : Result<(IdlUnion * bool * string list * (string * string list)) list, CodegenError> =
        let disc = ctx.Idl.Wire.Discriminator

        let foldMember (u: IdlUnion) =
            let self = u.Name + "$"

            let folder: FoldPrinter =
                { Self = fun st e -> sprintf "%s.fold(folder, %s, %s)" self st e
                  List = fun e x s b st -> sprintf "%s.reduce((%s, %s) => %s, %s)" e s x b st
                  MapItem = fun e x -> sprintf "%s[%s]" e x
                  Map = fun e x s b st -> sprintf "Object.keys(%s).sort().reduce((%s, %s) => %s, %s)" e s x b st
                  Access = fun e f -> tsProp e f.Name
                  OptionalVar = fun e _ -> e
                  Optional = fun e _ b st -> sprintf "(%s === undefined ? %s : %s)" e st b
                  StepState = fun d _ _ -> sprintf "__c%d" d
                  Thread =
                    fun d st bodies ->
                        let c = sprintf "__c%d" d

                        sprintf
                            "(() => { let %s = %s; %sreturn %s; })()"
                            c
                            st
                            (bodies |> List.map (fun b -> sprintf "%s = %s; " c b) |> String.concat "")
                            c }

            Derive.foldPlan ctx u
            |> Result.bind (fun recursive ->
                recursive
                |> List.map (fun (c, fs) ->
                    Derive.foldMembers folder ctx u (List.map snd fs) (tsProp "v" << _.Name) "state" 0
                    |> Result.map (sprintf "      case %s: return %s;" (SourceLit.tsString c.Tag)))
                |> sequenceR)
            |> Result.map (fun arms ->
                [ sprintf "  // Fold `folder` over this value and every nested `%s` it holds, in preorder." u.Name
                  "  fold(folder, state, v) {"
                  "    state = folder(state, v);"
                  "    switch (" + tsDiscProp disc "v" + ") {" ]
                @ arms
                @ [ "      default: return state;"; "    }"; "  }," ]
                |> String.concat "\n")

        let projectionMember (u: IdlUnion) (field: string) =
            Derive.projectionPlan u field
            |> Result.map (fun (carriers, total) ->
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
                  for c, _, _ in carriers do
                      yield sprintf "      case %s: return %s;" (SourceLit.tsString c.Tag) (tsProp "v" field)
                  yield "      default: return undefined;"
                  yield "    }"
                  yield "  }," ]
                |> String.concat "\n")

        Derive.unionModules ctx requests
        |> Result.bind (fun unions ->
            unions
            |> List.map (fun (u, folds, fields) ->
                ((if folds then [ foldMember u ] else [])
                 @ (fields |> List.map (projectionMember u)))
                |> sequenceR
                |> Result.map (fun members ->
                    u,
                    folds,
                    fields,
                    (sprintf "// Phase 380 — derived members of `%s`, exported as `%s`.\n" u.Name u.Name
                     + sprintf "const %s$ = {\n" u.Name
                     + String.concat "\n\n" members
                     + "\n};",
                     [ sprintf "%s$ as %s" u.Name u.Name ])))
            |> sequenceR)

    /// Emission 7 — `default<Tag>Spec` / `default<Record>` for every kind and record whose every
    /// field has a value without the caller, in the shape the generated decoder produces: an
    /// omitted-at-default member filled as the decoder refills it, a declared default rendered as
    /// the scaffold renders it, an absent optional and a host-only member absent.
    let private tsDefaultRecordsDecl
        (idl: Idl)
        (disc: string)
        (ctx: Decl)
        : Result<string * string list, CodegenError> =
        let sentinel (t: IdlType) =
            match t with
            | TClosure
            | TFn _
            | TOpaque -> true
            | _ -> false

        /// A member's value, or `None` where the member is absent. Phase 524 (511.t4) — the
        /// TypeScript printer of the one presence rule's CONSTRUCTOR site.
        let fieldValue (f: IdlField) (mv: MemberValue) : Result<string option, CodegenError> =
            match mv with
            | MemberValue.IdentityDefault d ->
                if sentinel f.Type then
                    Ok(Some "null")
                else
                    tsDefaultLit idl disc f.Type d |> Result.map Some
            | MemberValue.Carries v
            | MemberValue.Optional v -> typescriptValue idl f.Type v |> Result.map Some
            | MemberValue.Absent
            | MemberValue.Placeholder
            | MemberValue.Parameter -> Ok None
            | MemberValue.Refused v -> Error(CodegenError.UnsupportedDefault(f.Type, v))

        Derive.defaultRecords ctx (fun _ -> true)
        |> Result.bind (fun records ->
            records
            |> List.map (fun r ->
                let extra =
                    match r.Kind with
                    | Some k -> [ tsDiscKey disc + ": " + SourceLit.tsString k.Tag ]
                    | None -> []

                r.Members
                |> List.map (fun (f, mv) ->
                    fieldValue f mv
                    |> Result.map (Option.map (fun lit -> SourceLit.tsString f.Name + ": " + lit)))
                |> sequenceR
                |> Result.map (fun pieces ->
                    sprintf
                        "// `%s` with every field at the value a caller need not pass.\nconst default%s = { %s };"
                        r.Name
                        r.Name
                        (String.concat ", " (extra @ List.choose id pieces)),
                    "default" + r.Name))
            |> sequenceR)
        |> Result.map (fun xs -> xs |> List.map fst |> String.concat "\n\n", xs |> List.map snd)

    /// Emission 8 — `kindCategories`, `kindFieldNames`, `opFieldNames` as `Map`s of `Set`s and
    /// `envelopeFieldNames` as a `Set`, each in Ordinal order (the order the F# host's `Map` and
    /// `Set` iterate in).
    let private tsConstantsDecl (ctx: Decl) : string * string list =
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

        let c = Derive.constants ctx

        [ "// Phase 380 — VOCABULARY CONSTANTS. The kind tags of each declared category."
          "const kindCategories = " + mapLit c.Categories + ";"
          ""
          "// The wire field names of each kind."
          "const kindFieldNames = " + mapLit c.KindFields + ";"
          ""
          "// The wire field names of the node envelope."
          "const envelopeFieldNames = " + setLit c.Envelope + ";"
          ""
          "// The wire field names of each tree op."
          "const opFieldNames = " + mapLit c.OpFields + ";" ]
        |> String.concat "\n",
        [ "kindCategories"; "kindFieldNames"; "envelopeFieldNames"; "opFieldNames" ]

    // ---- the collecting decoders (Phase 377's `SpecDecoders`) ----

    /// Phase 524 — the TypeScript COLLECTING decoder printer, derived from [[tsDecPrinter]]
    /// ([[SlotTraversal.collecting]]): a leaf is the short-circuiting decoder, which refuses once.
    let private tsColPrinter: SlotPrinter =
        SlotTraversal.collecting
            tsDecPrinter
            id
            { tsDecPrinter with
                Var = fun v -> "col" + v
                Named = fun n -> "col" + n
                Union = fun n args -> "((x) => col" + n + "(" + String.concat ", " args + ", x))"
                Node = "colNode"
                List = fun c -> "cList(" + c + ")"
                Map = fun c -> "cMap(" + c + ")" }

    /// The collecting member reader: [[tsMemberDecPrinter]]'s presence, read through
    /// [[tsColPrinter]].
    let private tsColMemberPrinter (idl: Idl) (disc: string) : MemberDecPrinter =
        { tsMemberDecPrinter idl disc with
            Slot = tsColPrinter
            Req = fun key o c -> "cReq(" + key + ", " + o + ", " + c + ")"
            Opt = fun key o c -> "cOpt(" + key + ", " + o + ", " + c + ")"
            Def = fun key o c lit -> "cDef(" + key + ", " + o + ", " + c + ", " + lit + ")" }

    /// Read every member in declaration order, then build the object [[tsFieldObject]] builds — the
    /// same keys in the same order — or throw every member's refusals, in that order.
    let private tsColObject (indent: string) (extra: (string * string) list) (members: (IdlField * string) list) =
        let pairs =
            (extra |> List.map (fun (k, v) -> k + ": " + v))
            @ (members
               |> List.mapi (fun i (f, _) -> SourceLit.tsString f.Name + ": v[" + string i + "]"))

        let literal = "{ " + String.concat ", " pairs + " }"

        match members with
        | [] -> indent + "return " + literal + ";"
        | _ ->
            indent
            + "const v = cAll(["
            + (members |> List.map (fun (_, r) -> "() => " + r) |> String.concat ", ")
            + "]);\n"
            + indent
            + "return "
            + literal
            + ";"

    let private tsColUnion
        (decl: Decl)
        (disc: string)
        (read: IdlField list -> Result<(IdlField * string) list, CodegenError>)
        (u: IdlUnion)
        =
        let argList =
            match u.Params with
            | [] -> "j"
            | ps -> (ps |> List.map (fun p -> "col" + p) |> String.concat ", ") + ", j"

        UnionTraversal.decoder
            { Read = read
              Slot = tsColPrinter
              Arm =
                fun _ c members ->
                    "      case "
                    + SourceLit.tsString c.Tag
                    + ": {\n"
                    + tsColObject "        " [ tsDiscKey disc, SourceLit.tsString c.Tag ] members
                    + "\n      }"
              // A transparent union reads its single-field case bare, collecting through it.
              Bare =
                fun _ c f cfn ->
                    "  return { "
                    + tsDiscKey disc
                    + ": "
                    + SourceLit.tsString c.Tag
                    + ", "
                    + SourceLit.tsString f.Name
                    + ": "
                    + cfn
                    + "(j) };"
              NoBare =
                fun _ ->
                    "  return dFail('WrongKind', 'object', "
                    + SourceLit.tsString ("expected a " + u.Name + " object")
                    + ");"
              Union =
                fun _ arms untagged ->
                    "function col"
                    + u.Name
                    + "("
                    + argList
                    + ") {\n"
                    + "  if (isObj(j)) {\n    const fs = j;\n    switch (dTag(j)) {\n"
                    + String.concat "\n" arms
                    + "\n      default: return dUnknown("
                    + SourceLit.tsString (oneOf (u.Cases |> List.map _.Tag))
                    + ", "
                    + SourceLit.tsString ("unknown " + u.Name + " case: ")
                    + " + "
                    + tsDiscProp disc "j"
                    + ");\n    }\n  }"
                    + "\n"
                    + untagged
                    + "\n}" }
            decl
            u

    /// The collecting prelude: the refusal LIST a collecting decoder throws, and the readers that
    /// gather it. It states the defect order, which a test pins.
    let private tsCollectingPrelude = (Template.text "ts-collecting-prelude.js")

    /// The collecting prelude, the collecting decoders (node kind, node, unions, records, specs)
    /// and the public entries: per kind `decode<Tag>Spec` / `decode<Tag>SpecAll` over the kind's
    /// object, per node `decodeNodeJson` / `decodeNodeJsonAll` over a parsed value and
    /// `decodeNodeAll` over text — each answering `decodeNode`'s `{ ok, value }` / `{ ok, error }`,
    /// the collecting ones `{ ok: false, errors }`.
    let private tsCollectingDecl (decl: Decl) (disc: string) : Result<string * string list, CodegenError> =
        let idl, kinds = decl.Idl, decl.Kinds

        let colKind =
            "function colKind(j) {\n  if (!isObj(j)) return dFail('WrongKind', 'object', 'expected a kind object');\n  switch (dTag(j)) {\n"
            + (kinds
               |> List.map (fun k -> "    case " + SourceLit.tsString k.Tag + ": return col" + k.Tag + "Spec(j);")
               |> String.concat "\n")
            + "\n    default: return dUnknown("
            + SourceLit.tsString (oneOf (kinds |> List.map _.Tag))
            + ", 'unknown node kind: ' + "
            + tsDiscProp disc "j"
            + ");\n  }\n}"

        let colNode (envelope: (IdlField * string) list) =
            let kindRead =
                match decl.Envelope.Kind with
                | KindPlacement.NestedUnder key -> "cReq(" + SourceLit.tsStringSingle key + ", fs, colKind)"
                | KindPlacement.Merged -> "colKind(j)"

            let reads =
                [ "dReq('id', fs, dStr)"; kindRead ] @ (envelope |> List.map snd)
                |> List.map (fun r -> "() => " + r)
                |> String.concat ", "

            let envelopeAssigns =
                decl.Envelope.Members
                |> List.mapi (fun i f -> sprintf ", %s: v[%d]" (SourceLit.tsKey f.Name) (i + 2))
                |> String.concat ""

            let build =
                match decl.Envelope.Kind with
                | KindPlacement.NestedUnder key ->
                    "{ id: v[0], " + SourceLit.tsKey key + ": v[1]" + envelopeAssigns + " }"
                | KindPlacement.Merged -> "Object.assign({}, v[1], { id: v[0]" + envelopeAssigns + " })"

            "function colNode(j) {\n  const fs = dObj(j);\n  const v = cAll(["
            + reads
            + "]);\n  return "
            + build
            + ";\n}"

        let objectDecoder (name: string) (extra: (string * string) list) (members: (IdlField * string) list) =
            "function col"
            + name
            + "(j) {\n  const fs = dObj(j);\n"
            + tsColObject "  " extra members
            + "\n}"

        let group =
            CollectingTraversal.group
                { Dec = tsMemberDecPrinter idl disc
                  Col = tsColMemberPrinter idl disc
                  Lift = id
                  Union = tsColUnion decl disc
                  NodeKind = colKind
                  Node = colNode
                  Record = fun r members -> objectDecoder r.Name [] members
                  ReadsSpec = fun _ -> true
                  Spec =
                    fun k members ->
                        objectDecoder
                            (k.Tag + "Spec")
                            [ tsDiscKey disc, SourceLit.tsString k.Tag ]
                            (defaultArg members []) }
                decl

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
    let private slotsDecls (ctx: Decl) (typeName: string) : string list =
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
    let private tsDerivedParts (requests: Derivation list) (ctx: Decl) : Result<DerivedPart list option, CodegenError> =
        let decoders = List.contains Derivation.SpecDecoders requests

        if List.isEmpty requests then
            Ok None
        else
            let idl, kinds = ctx.Idl, ctx.Kinds
            let disc = idl.Wire.Discriminator
            let flat = ctx.Envelope.Kind = KindPlacement.Merged

            let has r = List.contains r requests

            let publicAccess = has Derivation.StructuralAccess || has Derivation.KeyedPositions

            // Phase 511 (S12) — admission is the one derivation admission, over this host's
            // resolution: no host projections, because a projection is F# source.
            let admitted = FSharpCodec.admitDerivations ctx requests

            let slots =
                requests
                |> List.choose (fun r ->
                    match r with
                    | Derivation.SlotsOf t -> Some t
                    | _ -> None)
                |> List.distinct

            let part (text: string, names: string list) (decls: string list) =
                { Text = text
                  Names = names
                  Decls = decls }

            let unionModules () =
                match tsUnionModulesDecl ctx requests with
                | Error e -> [ Error e ]
                | Ok modules ->
                    modules
                    |> List.map (fun (u, folds, fields, emitted) ->
                        unionModuleDecls u folds fields |> Result.map (part emitted))

            // `default<N>` is a value of the declared type `N`: a `<Tag>Spec` or a record.
            let defaultDecls (names: string list) =
                names
                |> List.map (fun n -> sprintf "export declare const %s: %s;" n (n.Substring "default".Length))

            admitted
            |> Result.bind (fun () ->
                [ (if publicAccess then
                       [ tsStructuralDecl disc flat kinds
                         |> Result.map (fun e -> part e (structuralDecls kinds)) ]
                   else
                       [])
                  (if has Derivation.KeyedPositions then
                       [ tsKeyedDecl disc flat ctx |> Result.map (fun e -> part e keyedDecls) ]
                   else
                       [])
                  slots
                  |> List.map (fun t -> Ok(part (tsSlotsDecl disc flat ctx t) (slotsDecls ctx t)))
                  (if has Derivation.DefaultRecords then
                       [ tsDefaultRecordsDecl idl disc ctx
                         |> Result.map (fun (text, names) -> part (text, names) (defaultDecls names)) ]
                   else
                       [])
                  (if has Derivation.VocabularyConstants then
                       [ Ok(part (tsConstantsDecl ctx) (constantsDecls kinds)) ]
                   else
                       [])
                  unionModules ()
                  (if decoders then
                       [ tsCollectingDecl ctx disc
                         |> Result.map (fun emitted -> part emitted (collectingDecls kinds)) ]
                   else
                       []) ]
                |> List.concat
                |> sequenceR
                |> Result.map Some)

    /// Phase 380 — `typescriptModule` plus the requested derivations, appended after its members
    /// with one further `export` naming them; no request IS `typescriptModule`,
    /// byte for byte. A request the F# path refuses is refused here with the F# path's error.
    let typescriptModuleDerived (requests: Derivation list) (decl: Decl) : Result<string, CodegenError> =
        let idl = decl.Idl

        typescriptModuleBody decl
        |> Result.bind (fun baseText ->
            tsDerivedParts requests decl
            |> Result.map (fun parts ->
                match parts with
                | None -> tsModuleStamp idl baseText
                | Some parts ->
                    let texts = parts |> List.map _.Text

                    let exported =
                        match parts |> List.collect _.Names with
                        | [] -> []
                        | names -> [ "export { " + String.concat ", " names + " };" ]

                    String.concat "\n\n" ((baseText :: texts) @ exported)
                    |> normalizeEol
                    |> tsModuleStamp idl))

    /// Phase 381 — `typescriptDeclarations` plus one declaration per member the derived module
    /// exports, appended after the file's own; no request IS
    /// `typescriptDeclarations`, byte for byte, and a request is refused exactly as
    /// [[typescriptModuleDerived]] refuses it. The parts are the module's own, so the members the
    /// module exports and the members this file declares are chosen by one request list.
    ///
    /// A requested `MapMsg` is not declared, because the module exports nothing for it; the file's
    /// header comment says why.
    let typescriptDeclarationsDerived (requests: Derivation list) (decl: Decl) : Result<string, CodegenError> =
        let idl = decl.Idl

        typescriptDeclarationsBody decl
        |> Result.bind (fun baseText ->
            tsDerivedParts requests decl
            |> Result.map (fun parts ->
                match parts with
                | None -> tsDeclarationsStamp idl baseText
                | Some parts ->
                    let header =
                        [ yield
                              "// Phase 381 — the derived members the module was generated with are declared after its own, one declaration per exported name."
                          if List.contains Derivation.MapMsg requests then
                              yield
                                  "// `mapMsg` is not declared: the module emits no `mapMsg`, because this host holds a handler slot as its sentinel's `null` and carries no message type, so a message map has nothing to rewrite." ]
                        |> String.concat "\n"

                    // The derived lines head the unstamped file, directly under the stamp (Phase 505).
                    let headed = header + "\n" + baseText

                    let decls =
                        parts
                        |> List.map (fun p -> String.concat "\n" p.Decls)
                        |> List.filter (fun s -> s <> "")

                    String.concat "\n\n" (headed.TrimEnd '\n' :: decls) + "\n"
                    |> normalizeEol
                    |> tsDeclarationsStamp idl))
