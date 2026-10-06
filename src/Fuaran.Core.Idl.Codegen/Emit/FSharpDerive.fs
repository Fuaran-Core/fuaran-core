namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.FSharpDefaults

/// Phase 374 — the STRUCTURAL DERIVATIONS: members of the generated F# module that follow
/// mechanically from facts the generator already holds (which fields hold nodes, which hold a
/// given declared type, which declarations carry the message parameter, what each field defaults
/// to, what each kind is tagged with) and that a consuming domain would otherwise write by hand,
/// once per consumer, with a silent omission each time the vocabulary grows a case.
///
/// **Every derivation is OPT-IN.** A module that requests none is the module the generator always
/// emitted, byte for byte — the request list is the only channel, and an empty one changes
/// nothing. Every derivation is stated over IDL constructs (a node-bearing position, a field of a
/// named declared type, a self-recursive union, a declaration generic in the message parameter),
/// never over any one domain's vocabulary.
///
/// **A request the vocabulary cannot honour is REFUSED, never approximated** — a fold over a union
/// that does not recurse, a projection no case carries, a message map through a signature that
/// takes the message as an argument. Each refusal is a typed `UnsupportedConstruct` naming the
/// construct and the alternative.
module internal FSharpDerive =

    /// The emitters' view of `Gen.Derivation`, case for case.
    [<RequireQualifiedAccess>]
    type Request =
        | StructuralAccess
        | KeyedPositions
        | SlotsOf of typeName: string
        | MapMsg
        | Fold of unionName: string
        | Projections of unionName: string * fieldNames: string list
        | DefaultRecords
        | VocabularyConstants

    /// What a derivation reads: the vocabulary, the msg-carrying set, and the declarations the
    /// module emits (the selected kinds and what they reach).
    type Ctx =
        { Idl: Idl
          Msg: Set<string>
          Kinds: IdlKind list
          Unions: IdlUnion list
          Records: IdlRecord list
          Projected: Set<string> }

    let private refuse (construct: string) (principle: string) (alternative: string) : Result<'a, CodegenError> =
        Error(CodegenError.UnsupportedConstruct(construct, principle, alternative))

    /// A STRUCTURAL child position: a node or node list a kind always carries. An optional node is
    /// a keyed position — it is not an ordered list a structural edit may rebuild — and so is a
    /// node held anywhere deeper (inside a record, a union case, a list of either, a map).
    let structuralField (f: IdlField) : (string * bool) option =
        match f.Opt, f.Type with
        | (Required | OmitDefault _), TNode -> Some(pascal f.Name, false)
        | (Required | OmitDefault _), TList TNode -> Some(pascal f.Name, true)
        | _ -> None

    let private nodeT (ctx: Ctx) = "Node" + declParams ctx.Msg "Node" []

    /// The host type text of a declared record / union name in this module.
    let private declT (ctx: Ctx) (name: string) (ps: string list) = name + declParams ctx.Msg name ps

    let private unionParams (ctx: Ctx) (name: string) =
        ctx.Unions
        |> List.tryFind (fun u -> u.Name = name)
        |> Option.map _.Params
        |> Option.defaultValue []

    /// The union-case pattern binding every field positionally: `U.Tag(__f0, __f1)`, or `U.Tag`.
    let private casePattern (u: IdlUnion) (c: IdlUnionCase) =
        match c.Fields with
        | [] -> u.Name + "." + c.Tag
        | fs -> sprintf "%s.%s(%s)" u.Name c.Tag (fs |> List.mapi (fun i _ -> sprintf "__f%d" i) |> String.concat ", ")

    // -----------------------------------------------------------------------
    // Node positions — the keyed walk (emission 2).
    // -----------------------------------------------------------------------

    /// The record / union names that hold a node at any depth (not through a type argument —
    /// a node held through one is refused where it is met). Host-neutral: the TypeScript
    /// emitter reads it too (Phase 380).
    let nodeHolders (ctx: Ctx) : Set<string> =
        let rec holds (hs: Set<string>) (t: IdlType) =
            match t with
            | TNode -> true
            | TList i
            | TMap i -> holds hs i
            | TRecord n -> hs.Contains n
            | TUnion(n, args) -> hs.Contains n || List.exists (holds hs) args
            | _ -> false

        let step (hs: Set<string>) =
            let us =
                ctx.Unions
                |> List.filter (fun u ->
                    u.Cases
                    |> List.exists (fun c -> c.Fields |> List.exists (fun f -> holds hs f.Type)))
                |> List.map _.Name

            let rs =
                ctx.Records
                |> List.filter (fun r -> r.Fields |> List.exists (fun f -> holds hs f.Type))
                |> List.map _.Name

            Set.unionMany [ hs; Set.ofList us; Set.ofList rs ]

        let rec fix hs =
            let next = step hs
            if next = hs then hs else fix next

        fix Set.empty

    /// Whether a value of type `t` holds a node, given the [[nodeHolders]] set.
    let rec holdsIn (hs: Set<string>) (t: IdlType) =
        match t with
        | TNode -> true
        | TList i
        | TMap i -> holdsIn hs i
        | TRecord n -> hs.Contains n
        | TUnion(n, args) -> hs.Contains n || List.exists (holdsIn hs) args
        | _ -> false

    let private throughArgument (u: string) : Result<'a, CodegenError> =
        refuse
            (sprintf "a node held through a type argument of the generic union '%s'" u)
            "the keyed walk is derived per declaration, and a generic union's argument has no one declaration to derive it from"
            "hold the node in a non-generic record or union, or do not request KeyedPositions"

    /// An expression listing the nodes a value of type `t` holds, in declaration order.
    let rec private collect (hs: Set<string>) (t: IdlType) (e: string) (d: int) : Result<string, CodegenError> =
        let x = sprintf "__x%d" d

        match t with
        | TNode -> Ok(sprintf "[ %s ]" e)
        | TList i ->
            collect hs i x (d + 1)
            |> Result.map (fun b -> sprintf "List.collect (fun %s -> %s) %s" x b e)
        | TMap i ->
            collect hs i x (d + 1)
            |> Result.map (fun b -> sprintf "(%s |> Map.toList |> List.collect (fun (_, %s) -> %s))" e x b)
        | TRecord n -> Ok(sprintf "nodesIn%s %s" n e)
        | TUnion(n, args) when args |> List.exists (holdsIn hs) -> throughArgument n
        | TUnion(n, _) -> Ok(sprintf "nodesIn%s %s" n e)
        | _ -> Ok "[]"

    /// An expression rebuilding a value of type `t` with `f` applied to every node it holds, in
    /// the order [[collect]] lists them.
    let rec private mapNodes (hs: Set<string>) (t: IdlType) (e: string) (d: int) : Result<string, CodegenError> =
        let x = sprintf "__x%d" d

        match t with
        | TNode -> Ok(sprintf "f %s" e)
        | TList i ->
            mapNodes hs i x (d + 1)
            |> Result.map (fun b -> sprintf "List.map (fun %s -> %s) %s" x b e)
        | TMap i ->
            mapNodes hs i x (d + 1)
            |> Result.map (fun b ->
                sprintf "(%s |> Map.toList |> List.map (fun (__k%d, %s) -> __k%d, %s) |> Map.ofList)" e d x d b)
        | TRecord n -> Ok(sprintf "mapNodesIn%s f %s" n e)
        | TUnion(n, args) when args |> List.exists (holdsIn hs) -> throughArgument n
        | TUnion(n, _) -> Ok(sprintf "mapNodesIn%s f %s" n e)
        | _ -> Ok e

    let private collectField (hs: Set<string>) (f: IdlField) (e: string) : Result<string, CodegenError> =
        match f.Opt with
        | Optional ->
            collect hs f.Type "__o" 0
            |> Result.map (sprintf "(match %s with Some __o -> %s | None -> [])" e)
        | _ -> collect hs f.Type e 0

    let private mapField (hs: Set<string>) (f: IdlField) (e: string) : Result<string, CodegenError> =
        match f.Opt with
        | Optional ->
            mapNodes hs f.Type "__o" 0
            |> Result.map (fun b -> sprintf "Option.map (fun __o -> %s) %s" b e)
        | _ -> mapNodes hs f.Type e 0

    /// `a @ b @ c`, or `[]` for none.
    let private appendAll (xs: string list) =
        match xs with
        | [] -> "[]"
        | _ -> String.concat " @ " xs

    /// The keyed walk: per-declaration node listers and rebuilders, the node-level
    /// `keyedChildren` / `withKeyedChildren`, the generated full walk, and the `KeyedWitness`.
    let keyedDecl (ctx: Ctx) : Result<string, CodegenError> =
        let hs = nodeHolders ctx
        let node = nodeT ctx

        let holdsF (f: IdlField) = holdsIn hs f.Type

        let recordHelpers (r: IdlRecord) =
            let t = declT ctx r.Name []
            let fs = r.Fields |> List.filter holdsF

            let lister =
                fs
                |> List.map (fun f -> collectField hs f ("v." + pascal f.Name))
                |> sequenceR
                |> Result.map (fun xs ->
                    sprintf "and private nodesIn%s (v: %s) : %s list =\n    %s" r.Name t node (appendAll xs))

            let mapper =
                fs
                |> List.mapi (fun i f -> mapField hs f ("v." + pascal f.Name) |> Result.map (fun b -> i, f, b))
                |> sequenceR
                |> Result.map (fun xs ->
                    let lets =
                        xs
                        |> List.map (fun (i, _, b) -> sprintf "    let __m%d = %s\n" i b)
                        |> String.concat ""

                    let assigns =
                        xs
                        |> List.map (fun (i, f, _) -> sprintf "%s = __m%d" (pascal f.Name) i)
                        |> String.concat "; "

                    sprintf
                        "and private mapNodesIn%s (f: %s -> %s) (v: %s) : %s =\n%s    { v with %s }"
                        r.Name
                        node
                        node
                        t
                        t
                        lets
                        assigns)

            [ lister; mapper ]

        let unionHelpers (u: IdlUnion) =
            let t = declT ctx u.Name u.Params
            let gen = declParams ctx.Msg u.Name u.Params

            let holding = u.Cases |> List.filter (fun c -> c.Fields |> List.exists holdsF)

            let wild = List.length holding < List.length u.Cases

            let lister =
                holding
                |> List.map (fun c ->
                    c.Fields
                    |> List.mapi (fun i f -> i, f)
                    |> List.filter (fun (_, f) -> holdsF f)
                    |> List.map (fun (i, f) -> collectField hs f (sprintf "__f%d" i))
                    |> sequenceR
                    |> Result.map (fun xs -> sprintf "    | %s -> %s" (casePattern u c) (appendAll xs)))
                |> sequenceR
                |> Result.map (fun arms ->
                    sprintf "and private nodesIn%s%s (v: %s) : %s list =\n    match v with\n" u.Name gen t node
                    + String.concat "\n" (arms @ (if wild then [ "    | _ -> []" ] else [])))

            let mapper =
                holding
                |> List.map (fun c ->
                    c.Fields
                    |> List.mapi (fun i f ->
                        if holdsF f then
                            mapField hs f (sprintf "__f%d" i) |> Result.map Some
                        else
                            Ok None)
                    |> sequenceR
                    |> Result.map (fun ms ->
                        let lets =
                            ms
                            |> List.mapi (fun i m ->
                                m |> Option.map (fun b -> sprintf "        let __m%d = %s\n" i b))
                            |> List.choose id
                            |> String.concat ""

                        let args =
                            ms
                            |> List.mapi (fun i m ->
                                match m with
                                | Some _ -> sprintf "__m%d" i
                                | None -> sprintf "__f%d" i)
                            |> String.concat ", "

                        sprintf "    | %s ->\n%s        %s.%s(%s)" (casePattern u c) lets u.Name c.Tag args))
                |> sequenceR
                |> Result.map (fun arms ->
                    sprintf
                        "and private mapNodesIn%s%s (f: %s -> %s) (v: %s) : %s =\n    match v with\n"
                        u.Name
                        gen
                        node
                        node
                        t
                        t
                    + String.concat "\n" (arms @ (if wild then [ "    | other -> other" ] else [])))

            [ lister; mapper ]

        let helpers =
            (ctx.Records
             |> List.filter (fun r -> hs.Contains r.Name)
             |> List.collect recordHelpers)
            @ (ctx.Unions
               |> List.filter (fun u -> hs.Contains u.Name)
               |> List.collect unionHelpers)
            |> sequenceR

        let keyedFields (k: IdlKind) =
            k.Fields |> List.filter (fun f -> holdsF f && (structuralField f).IsNone)

        let keyedKinds =
            ctx.Kinds |> List.filter (fun k -> not (List.isEmpty (keyedFields k)))

        let kindWild = List.length keyedKinds < List.length ctx.Kinds
        let envFields = ctx.Idl.NodeFields |> List.filter holdsF

        let listerArms =
            keyedKinds
            |> List.map (fun k ->
                keyedFields k
                |> List.map (fun f -> collectField hs f ("s." + pascal f.Name))
                |> sequenceR
                |> Result.map (fun xs -> sprintf "        | NodeKind.%s s -> %s" k.Tag (appendAll xs)))
            |> sequenceR

        let envListers =
            envFields
            |> List.map (fun f -> collectField hs f ("n." + pascal f.Name))
            |> sequenceR

        let mapperArms =
            keyedKinds
            |> List.map (fun k ->
                keyedFields k
                |> List.mapi (fun i f -> mapField hs f ("s." + pascal f.Name) |> Result.map (fun b -> i, f, b))
                |> sequenceR
                |> Result.map (fun xs ->
                    let lets =
                        xs
                        |> List.map (fun (i, _, b) -> sprintf "            let __m%d = %s\n" i b)
                        |> String.concat ""

                    let assigns =
                        xs
                        |> List.map (fun (i, f, _) -> sprintf "%s = __m%d" (pascal f.Name) i)
                        |> String.concat "; "

                    sprintf
                        "        | NodeKind.%s s ->\n%s            NodeKind.%s { s with %s }"
                        k.Tag
                        lets
                        k.Tag
                        assigns))
            |> sequenceR

        let envMappers =
            envFields
            |> List.mapi (fun i f -> mapField hs f ("n." + pascal f.Name) |> Result.map (fun b -> i, f, b))
            |> sequenceR

        match helpers, listerArms, envListers, mapperArms, envMappers with
        | Ok helpers, Ok listerArms, Ok envListers, Ok mapperArms, Ok envMappers ->
            let helperGroup =
                match helpers with
                | [] -> []
                | first :: rest ->
                    // The group opens with `let rec`; every member after it is an `and`.
                    [ "let rec " + first.Substring 4 :: rest |> String.concat "\n\n" ]

            let ofKind =
                if List.isEmpty keyedKinds then
                    " []"
                else
                    "\n        match n.Kind with\n"
                    + String.concat "\n" (listerArms @ (if kindWild then [ "        | _ -> []" ] else []))

            let lister =
                sprintf "/// The nodes this node holds in keyed, non-structural positions, in declaration order.\n"
                + sprintf "let keyedChildren (n: %s) : %s list =\n" node node
                + sprintf "    let ofKind =%s\n\n" ofKind
                + sprintf "    %s" (appendAll ("ofKind" :: envListers))

            let kindMap =
                if List.isEmpty keyedKinds then
                    " n.Kind"
                else
                    "\n        match n.Kind with\n"
                    + String.concat "\n" (mapperArms @ (if kindWild then [ "        | k -> k" ] else []))

            let envLets =
                envMappers
                |> List.map (fun (i, _, b) -> sprintf "    let __e%d = %s\n" i b)
                |> String.concat ""

            let envAssigns =
                envMappers
                |> List.map (fun (i, f, _) -> sprintf "; %s = __e%d" (pascal f.Name) i)
                |> String.concat ""

            let mapper =
                sprintf "let private mapKeyed (f: %s -> %s) (n: %s) : %s =\n" node node node node
                + sprintf "    let kind =%s\n\n" kindMap
                + envLets
                + sprintf "    { n with Kind = kind%s }" envAssigns

            let rest =
                [ "/// The node with these nodes in its keyed positions, position for position (arity-preserving)."
                  sprintf "let withKeyedChildren (kids: %s list) (n: %s) : %s =" node node node
                  "    let rest = ref kids"
                  ""
                  sprintf "    let take (old: %s) =" node
                  "        match rest.Value with"
                  "        | h :: t ->"
                  "            rest.Value <- t"
                  "            h"
                  "        | [] -> old"
                  ""
                  "    mapKeyed take n"
                  ""
                  "/// The generated full walk: every node position the vocabulary declares, structural and keyed."
                  sprintf "let private idsUniqueFullWalk (root: %s) : bool =" node
                  sprintf "    let rec go (seen: Set<string>) (stack: %s list) =" node
                  "        match stack with"
                  "        | [] -> true"
                  "        | x :: rest ->"
                  "            if Set.contains x.Id seen then"
                  "                false"
                  "            else"
                  "                go (Set.add x.Id seen) (children x @ keyedChildren x @ rest)"
                  ""
                  "    go Set.empty [ root ]"
                  ""
                  sprintf "let keyedWitness: KeyedWitness<%s, string> =" node
                  "    { Surface = \"the generated full walk over every node position the vocabulary declares\""
                  "      KeyedChildren = keyedChildren"
                  "      ReplaceKeyedChildren = fun n kids -> withKeyedChildren kids n"
                  "      PlaceKeyedChild ="
                  "        fun n id ->"
                  "            match keyedChildren n with"
                  "            | first :: others -> Some(withKeyedChildren ({ first with Id = id } :: others) n)"
                  "            | [] -> None"
                  "      IdsUnique = idsUniqueFullWalk }" ]
                |> String.concat "\n"

            Ok(String.concat "\n\n" (helperGroup @ [ lister; mapper; rest ]))
        | Error e, _, _, _, _
        | _, Error e, _, _, _
        | _, _, Error e, _, _
        | _, _, _, Error e, _
        | _, _, _, _, Error e -> Error e

    // -----------------------------------------------------------------------
    // Emission 3 — the typed slot enumerator.
    // -----------------------------------------------------------------------

    let slotsDecl (ctx: Ctx) (typeName: string) : Result<string, CodegenError> =
        let isT (t: IdlType) =
            match t with
            | TEnum n
            | TRecord n
            | TUnion(n, _) -> n = typeName
            | _ -> false

        let generic = unionParams ctx typeName |> List.isEmpty |> not

        let declared =
            ctx.Unions |> List.exists (fun u -> u.Name = typeName)
            || ctx.Records |> List.exists (fun r -> r.Name = typeName)
            || ctx.Idl.Enums |> List.exists (fun e -> e.Name = typeName)

        let slotT = if generic then "obj" else declT ctx typeName []

        let wrap (v: string) =
            if generic then sprintf "box %s" v else v

        /// The pairs a field contributes, or `None` when it holds no slot of the type.
        let ofField (owner: string) (f: IdlField) : string option =
            let e = owner + "." + pascal f.Name
            let key = SourceLit.fsString f.Name

            let direct (e: string) =
                match f.Type with
                | t when isT t -> Some(sprintf "[ %s, %s ]" key (wrap e))
                | TList t when isT t -> Some(sprintf "(%s |> List.map (fun __v -> %s, %s))" e key (wrap "__v"))
                | _ -> None

            match f.Opt with
            | Optional ->
                direct "__v"
                |> Option.map (sprintf "(match %s with Some __v -> %s | None -> [])" e)
            | _ -> direct e

        let arms =
            ctx.Kinds
            |> List.choose (fun k ->
                match k.Fields |> List.choose (ofField "s") with
                | [] -> None
                | xs -> Some(sprintf "        | NodeKind.%s s -> %s" k.Tag (appendAll xs)))

        let env = ctx.Idl.NodeFields |> List.choose (ofField "n")

        if not declared then
            refuse
                (sprintf "a slot enumerator over '%s'" typeName)
                "a slot enumerator is derived over a declared enum, record or union this module emits"
                "name a declared type the selected kinds reach"
        elif List.isEmpty arms && List.isEmpty env then
            refuse
                (sprintf "a slot enumerator over '%s'" typeName)
                "a slot enumerator lists the kind and envelope fields declared at a type, and no selected kind or envelope field is declared at this one"
                "name a type a kind or envelope field is declared at, or drop the request"
        else
            let node = nodeT ctx

            let ofKind =
                if List.isEmpty arms then
                    " []"
                else
                    "\n        match n.Kind with\n"
                    + String.concat
                        "\n"
                        (arms
                         @ (if List.length arms < List.length ctx.Kinds then
                                [ "        | _ -> []" ]
                            else
                                []))

            Ok(
                sprintf
                    "/// Every (field name, value) pair this node holds at the declared type `%s`, kind fields first.\n"
                    typeName
                + sprintf "let slotsOf%s (n: %s) : (string * %s) list =\n" typeName node slotT
                + sprintf "    let ofKind =%s\n\n" ofKind
                + sprintf "    %s" (appendAll ("ofKind" :: env))
            )

    // -----------------------------------------------------------------------
    // Emission 4 — the structural map over the message parameter.
    // -----------------------------------------------------------------------

    let private mapperName (decl: string) =
        if decl = "Node" then "mapMsg" else "mapMsg" + decl

    /// Strip parentheses that wrap the WHOLE text.
    let rec private stripParens (t: string) =
        let t = t.Trim()

        if t.StartsWith "(" && t.EndsWith ")" then
            // The opening parenthesis must close at the very end.
            let mutable depth = 0
            let mutable closesAtEnd = true

            for i in 0 .. t.Length - 1 do
                match t[i] with
                | '(' -> depth <- depth + 1
                | ')' ->
                    depth <- depth - 1

                    if depth = 0 && i < t.Length - 1 then
                        closesAtEnd <- false
                | _ -> ()

            if closesAtEnd then
                stripParens (t.Substring(1, t.Length - 2))
            else
                t
        else
            t

    /// The first top-level `->`: `(argument, result)`.
    let private splitArrow (t: string) : (string * string) option =
        let mutable depth = 0
        let mutable found = -1
        let mutable i = 0

        while found < 0 && i < t.Length - 1 do
            match t[i] with
            | '('
            | '<' -> depth <- depth + 1
            | ')'
            | '>' when not (i > 0 && t[i - 1] = '-') -> depth <- depth - 1
            | '-' when depth = 0 && t[i + 1] = '>' -> found <- i
            | _ -> ()

            i <- i + 1

        if found < 0 then
            None
        else
            Some(t.Substring(0, found).Trim(), t.Substring(found + 2).Trim())

    let private mentionsMsgText (t: string) =
        System.Text.RegularExpressions.Regex.IsMatch(t, "'Msg\\b")

    /// Map a value of a declared host SIGNATURE (a `TFn` slot) over the message parameter.
    let rec private mapSig
        (ctx: Ctx)
        (where: string)
        (text: string)
        (e: string)
        (d: int)
        : Result<string, CodegenError> =
        let t = stripParens text

        let refuseSig () =
            refuse
                (sprintf "the message map through the signature '%s' of %s" text where)
                "the message map rewrites a signature whose message parameter appears only covariantly: the message itself, the result of an arrow, an option or list of those, or a generated type applied to the message parameter alone"
                "declare the slot with a covariant signature, or do not request MapMsg"

        if not (mentionsMsgText t) then
            Ok e
        elif t = "'Msg" then
            Ok(sprintf "f (%s)" e)
        else
            match splitArrow t with
            | Some(a, b) ->
                if mentionsMsgText a then
                    refuseSig ()
                else
                    let x = sprintf "__a%d" d

                    mapSig ctx where b (sprintf "(%s) %s" e x) (d + 1)
                    |> Result.map (fun body -> sprintf "(fun %s -> %s)" x body)
            | None ->
                let x = sprintf "__y%d" d

                if t.EndsWith " option" then
                    mapSig ctx where (t.Substring(0, t.Length - 7)) x (d + 1)
                    |> Result.map (fun body -> sprintf "Option.map (fun %s -> %s) (%s)" x body e)
                elif t.EndsWith " list" then
                    mapSig ctx where (t.Substring(0, t.Length - 5)) x (d + 1)
                    |> Result.map (fun body -> sprintf "List.map (fun %s -> %s) (%s)" x body e)
                else
                    let m =
                        System.Text.RegularExpressions.Regex.Match(t, "^([A-Za-z_][A-Za-z0-9_]*)<'Msg>$")

                    if
                        m.Success
                        && ctx.Msg.Contains m.Groups[1].Value
                        && List.isEmpty (unionParams ctx m.Groups[1].Value)
                    then
                        Ok(sprintf "%s f (%s)" (mapperName m.Groups[1].Value) e)
                    else
                        refuseSig ()

    let rec private mentionsMsg (ctx: Ctx) (t: IdlType) =
        match t with
        | TFn s -> mentionsMsgText s.FSharp
        | TList i
        | TMap i -> mentionsMsg ctx i
        | TNode -> ctx.Msg.Contains "Node"
        | TUnion(n, args) -> ctx.Msg.Contains n || List.exists (mentionsMsg ctx) args
        | TRecord n -> ctx.Msg.Contains n
        | _ -> false

    let rec private mapMsgE
        (ctx: Ctx)
        (where: string)
        (t: IdlType)
        (e: string)
        (d: int)
        : Result<string, CodegenError> =
        let x = sprintf "__x%d" d

        if not (mentionsMsg ctx t) then
            Ok e
        else
            match t with
            | TNode -> Ok(sprintf "mapMsg f %s" e)
            | TList i ->
                mapMsgE ctx where i x (d + 1)
                |> Result.map (fun b -> sprintf "List.map (fun %s -> %s) %s" x b e)
            | TMap i ->
                mapMsgE ctx where i x (d + 1)
                |> Result.map (fun b -> sprintf "Map.map (fun _ %s -> %s) %s" x b e)
            | TRecord n -> Ok(sprintf "%s f %s" (mapperName n) e)
            | TUnion(n, args) when args |> List.exists (mentionsMsg ctx) ->
                refuse
                    (sprintf "the message map through a type argument of '%s' at %s" n where)
                    "the message map is derived per declaration, and a type argument carrying the message parameter has no one declaration to derive it from"
                    "carry the message parameter in a non-generic declaration, or do not request MapMsg"
            | TUnion(n, _) -> Ok(sprintf "%s f %s" (mapperName n) e)
            | TFn s -> mapSig ctx where s.FSharp e d
            | _ -> Ok e

    let private mapMsgField (ctx: Ctx) (where: string) (f: IdlField) (e: string) =
        match f.Opt with
        | Optional when mentionsMsg ctx f.Type ->
            mapMsgE ctx where f.Type "__o" 0
            |> Result.map (fun b -> sprintf "Option.map (fun __o -> %s) %s" b e)
        | _ -> mapMsgE ctx where f.Type e 0

    let mapMsgDecl (ctx: Ctx) : Result<string, CodegenError> =
        if not (ctx.Msg.Contains "Node") then
            refuse
                "a message map over a vocabulary whose node carries no message parameter"
                "the message map rewrites the message type of the declarations generic in it, and this vocabulary's node is not"
                "declare a TFn slot whose signature mentions 'Msg, or do not request MapMsg"
        else
            let to2 (name: string) (ps: string list) =
                name
                + "<"
                + String.concat ", " ((ps |> List.map (fun p -> "'" + p)) @ [ "'Msg2" ])
                + ">"

            let gen (ps: string list) =
                "<"
                + String.concat ", " ((ps |> List.map (fun p -> "'" + p)) @ [ "'Msg"; "'Msg2" ])
                + ">"

            let header (name: string) (ps: string list) (binder: string) =
                sprintf
                    "and private %s%s (f: 'Msg -> 'Msg2) (%s: %s) : %s ="
                    (mapperName name)
                    (gen ps)
                    binder
                    (declT ctx name ps)
                    (to2 name ps)

            let recordMapper (typeName: string) (where: string) (fs: IdlField list) =
                fs
                |> List.map (fun f ->
                    mapMsgField ctx (where + "." + f.Name) f ("v." + pascal f.Name)
                    |> Result.map (fun b -> sprintf "%s = %s" (pascal f.Name) b))
                |> sequenceR
                |> Result.map (fun assigns ->
                    header typeName [] "v"
                    + sprintf "\n    (%s: %s)" (recordLit (to2 typeName []) assigns) (to2 typeName []))

            let records =
                ctx.Records
                |> List.filter (fun r -> ctx.Msg.Contains r.Name)
                |> List.map (fun r -> recordMapper r.Name r.Name r.Fields)

            let specs =
                ctx.Kinds
                |> List.filter (fun k -> ctx.Msg.Contains(k.Tag + "Spec"))
                |> List.map (fun k ->
                    if ctx.Projected.Contains k.Tag then
                        refuse
                            (sprintf "the message map over the projected kind '%s'" k.Tag)
                            "a projected kind's record is verbatim host source, so the generator cannot construct it"
                            "supply the projected kind's map through the declared support, or do not request MapMsg"
                    else
                        recordMapper (k.Tag + "Spec") k.Tag k.Fields)

            let unions =
                ctx.Unions
                |> List.filter (fun u -> ctx.Msg.Contains u.Name)
                |> List.map (fun u ->
                    u.Cases
                    |> List.map (fun c ->
                        c.Fields
                        |> List.mapi (fun i f ->
                            mapMsgField ctx (u.Name + "." + c.Tag + "." + f.Name) f (sprintf "__f%d" i))
                        |> sequenceR
                        |> Result.map (fun args ->
                            match args with
                            | [] -> sprintf "    | %s -> %s.%s" (casePattern u c) u.Name c.Tag
                            | xs ->
                                sprintf
                                    "    | %s -> %s.%s(%s)"
                                    (casePattern u c)
                                    u.Name
                                    c.Tag
                                    (String.concat ", " xs)))
                    |> sequenceR
                    |> Result.map (fun arms ->
                        header u.Name u.Params "v" + "\n    match v with\n" + String.concat "\n" arms))

            let kindArms =
                ctx.Kinds
                |> List.map (fun k ->
                    if ctx.Msg.Contains(k.Tag + "Spec") then
                        sprintf "    | NodeKind.%s s -> NodeKind.%s(%s f s)" k.Tag k.Tag (mapperName (k.Tag + "Spec"))
                    else
                        sprintf "    | NodeKind.%s s -> NodeKind.%s s" k.Tag k.Tag)

            let envelope =
                ctx.Idl.NodeFields
                |> List.map (fun f ->
                    mapMsgField ctx ("node." + f.Name) f ("n." + pascal f.Name)
                    |> Result.map (fun b -> sprintf "%s = %s" (pascal f.Name) b))
                |> sequenceR

            let all = sequenceR (records @ specs @ unions)

            match all, envelope with
            | Ok members, Ok envelope ->
                let nodeMapper =
                    "/// The tree with its message type rewritten through `f`, every structure rebuilt.\n"
                    + "let rec mapMsg<'Msg, 'Msg2> (f: 'Msg -> 'Msg2) (n: Node<'Msg>) : Node<'Msg2> =\n"
                    + sprintf
                        "    (%s: Node<'Msg2>)"
                        (recordLit "Node<'Msg2>" ([ "Id = n.Id"; "Kind = mapMsgNodeKind f n.Kind" ] @ envelope))

                let kindMapper =
                    "and private mapMsgNodeKind<'Msg, 'Msg2> (f: 'Msg -> 'Msg2) (k: NodeKind<'Msg>) : NodeKind<'Msg2> =\n    match k with\n"
                    + String.concat "\n" kindArms

                Ok(String.concat "\n\n" (nodeMapper :: kindMapper :: members))
            | Error e, _
            | _, Error e -> Error e

    // -----------------------------------------------------------------------
    // Emissions 5 and 6 — a self-recursive union's fold and its common-field projections, in a
    // module named after the union (`Rule.fold`, `Rule.owner`).
    // -----------------------------------------------------------------------

    /// Whether `t` reaches the union `target` at any depth, through any declaration.
    let reaches (ctx: Ctx) (target: string) (t: IdlType) : bool =
        let rec go (seen: Set<string>) (t: IdlType) =
            match t with
            | TUnion(n, args) ->
                n = target
                || List.exists (go seen) args
                || (not (seen.Contains n)
                    && (ctx.Unions
                        |> List.tryFind (fun u -> u.Name = n)
                        |> Option.exists (fun u ->
                            u.Cases
                            |> List.exists (fun c -> c.Fields |> List.exists (fun f -> go (seen.Add n) f.Type)))))
            | TRecord n ->
                not (seen.Contains n)
                && (ctx.Records
                    |> List.tryFind (fun r -> r.Name = n)
                    |> Option.exists (fun r -> r.Fields |> List.exists (fun f -> go (seen.Add n) f.Type)))
            | TList i
            | TMap i -> go seen i
            | _ -> false

        go Set.empty t

    /// Whether a value of type `t` holds a value of the union `u`'s own instantiation — the
    /// positions a fold over `u` descends — refusing a recursion the fold cannot follow (at other
    /// type arguments, or through another union). `seen` is the records already entered.
    /// Host-neutral: the TypeScript fold reads the same analysis (Phase 380).
    let rec foldSelfIn (ctx: Ctx) (u: IdlUnion) (seen: Set<string>) (t: IdlType) : Result<bool, CodegenError> =
        match t with
        | TUnion(n, args) when n = u.Name ->
            if args = (u.Params |> List.map TVar) then
                Ok true
            else
                refuse
                    (sprintf "a fold over '%s', which recurses at other type arguments" u.Name)
                    "the fold visits nested values of the union's own instantiation"
                    "recurse at the declared type parameters, or do not request the fold"
        | TUnion(n, _) when reaches ctx u.Name t ->
            refuse
                (sprintf "a fold over '%s', which recurses through the union '%s'" u.Name n)
                "the fold descends lists, options, maps and records, and a recursion through another union would leave the nested values it holds unvisited"
                "recurse through a list, an option, a map or a record, or do not request the fold"
        | TList i
        | TMap i -> foldSelfIn ctx u seen i
        | TRecord n when not (seen.Contains n) ->
            ctx.Records
            |> List.tryFind (fun r -> r.Name = n)
            |> Option.map (fun r ->
                r.Fields
                |> List.map (fun f -> foldSelfIn ctx u (seen.Add n) f.Type)
                |> sequenceR
                |> Result.map (List.exists id))
            |> Option.defaultValue (Ok false)
        | _ -> Ok false

    let private foldDecl (ctx: Ctx) (u: IdlUnion) : Result<string, CodegenError> =
        let selfIn = foldSelfIn ctx u

        let ut = declT ctx u.Name u.Params

        let rec foldE (t: IdlType) (e: string) (st: string) (d: int) : Result<string, CodegenError> =
            let x = sprintf "__x%d" d
            let s = sprintf "__s%d" d

            match t with
            | TUnion(n, _) when n = u.Name -> Ok(sprintf "fold folder %s %s" st e)
            | TList i ->
                foldE i x s (d + 1)
                |> Result.map (fun b -> sprintf "List.fold (fun %s %s -> %s) %s %s" s x b st e)
            | TMap i ->
                foldE i x s (d + 1)
                |> Result.map (fun b -> sprintf "(%s |> Map.toList |> List.fold (fun %s (_, %s) -> %s) %s)" e s x b st)
            | TRecord n ->
                let fs =
                    ctx.Records
                    |> List.tryFind (fun r -> r.Name = n)
                    |> Option.map _.Fields
                    |> Option.defaultValue []
                    |> List.filter (fun f ->
                        (match selfIn (Set.singleton n) f.Type with
                         | Ok b -> b
                         | Error _ -> false))

                foldFields fs (fun f -> e + "." + pascal f.Name) st (d + 1)
            | _ -> Ok st

        and foldField (f: IdlField) (e: string) (st: string) (d: int) =
            match f.Opt with
            | Optional ->
                foldE f.Type (sprintf "__o%d" d) st (d + 1)
                |> Result.map (fun b -> sprintf "(match %s with Some __o%d -> %s | None -> %s)" e d b st)
            | _ -> foldE f.Type e st d

        /// Thread the state through several fields, left to right.
        and foldFields (fs: IdlField list) (access: IdlField -> string) (st: string) (d: int) =
            match fs with
            | [] -> Ok st
            | [ f ] -> foldField f (access f) st d
            | _ ->
                fs
                |> List.mapi (fun i f -> i, f)
                |> List.fold
                    (fun acc (i, f) ->
                        acc
                        |> Result.bind (fun (prev, lets) ->
                            let next = sprintf "__c%d_%d" d i

                            foldField f (access f) prev (d + 1)
                            |> Result.map (fun b -> next, lets @ [ sprintf "let %s = %s in" next b ])))
                    (Ok(st, []))
                |> Result.map (fun (last, lets) -> sprintf "(%s %s)" (String.concat " " lets) last)

        let cases =
            u.Cases
            |> List.map (fun c ->
                c.Fields
                |> List.mapi (fun i f -> selfIn Set.empty f.Type |> Result.map (fun b -> i, f, b))
                |> sequenceR
                |> Result.map (fun fs -> c, fs |> List.filter (fun (_, _, b) -> b)))
            |> sequenceR

        cases
        |> Result.bind (fun cases ->
            let recursive = cases |> List.filter (fun (_, fs) -> not (List.isEmpty fs))

            if List.isEmpty recursive then
                refuse
                    (sprintf "a fold over '%s', which holds no value of its own type" u.Name)
                    "a fold is derived for a union whose cases contain the union itself"
                    "request the fold over a self-recursive union"
            else
                recursive
                |> List.map (fun (c, fs) ->
                    let byName = fs |> List.map (fun (i, f, _) -> f.Name, i) |> Map.ofList

                    foldFields
                        (fs |> List.map (fun (_, f, _) -> f))
                        (fun f -> sprintf "__f%d" byName[f.Name])
                        "state"
                        0
                    |> Result.map (fun b -> sprintf "        | %s -> %s" (casePattern u c) b))
                |> sequenceR
                |> Result.map (fun arms ->
                    let wild =
                        if List.length recursive < List.length u.Cases then
                            [ "        | _ -> state" ]
                        else
                            []

                    sprintf
                        "    /// Fold `folder` over this value and every nested `%s` it holds, in preorder.\n"
                        u.Name
                    + sprintf "    let rec fold (folder: 'S -> %s -> 'S) (state: 'S) (v: %s) : 'S =\n" ut ut
                    + "        let state = folder state v\n\n"
                    + "        match v with\n"
                    + String.concat "\n" (arms @ wild)))

    let private projectionDecl (ctx: Ctx) (u: IdlUnion) (field: string) : Result<string, CodegenError> =
        let fsType = fsTypeIn ctx.Msg

        let hostType (f: IdlField) =
            match f.Opt with
            | Optional -> fsType f.Type |> Result.map (fun s -> s + " option")
            | _ -> fsType f.Type

        let carriers =
            u.Cases
            |> List.choose (fun c ->
                c.Fields
                |> List.tryFindIndex (fun f -> f.Name = field)
                |> Option.map (fun i -> c, i, c.Fields[i]))

        match carriers with
        | [] ->
            refuse
                (sprintf "a projection of '%s' over '%s', which no case carries" field u.Name)
                "a projection reads a field the union's cases declare"
                "name a field a case declares"
        | (_, _, f0) :: _ ->
            carriers
            |> List.map (fun (_, _, f) -> hostType f)
            |> sequenceR
            |> Result.bind (fun types ->
                if types |> List.distinct |> List.length > 1 then
                    refuse
                        (sprintf
                            "a projection of '%s' over '%s', whose cases declare it at different types"
                            field
                            u.Name)
                        "a projection returns one type, so every case carrying the field must declare it at the same host type"
                        "declare the field at one type in every case, or drop it from the projection"
                else
                    let total = List.length carriers = List.length u.Cases
                    let ty = List.head types
                    let ut = declT ctx u.Name u.Params

                    let arms =
                        carriers
                        |> List.map (fun (c, i, _) ->
                            let pat =
                                c.Fields
                                |> List.mapi (fun j _ -> if j = i then "__v" else "_")
                                |> String.concat ", "

                            sprintf "        | %s.%s(%s) -> %s" u.Name c.Tag pat (if total then "__v" else "Some __v"))

                    let arms = arms @ (if total then [] else [ "        | _ -> None" ])

                    Ok(
                        sprintf
                            "    /// The `%s` field, %s.\n"
                            f0.Name
                            (if total then
                                 "which every case carries"
                             else
                                 "where the case carries one")
                        + sprintf "    let %s (v: %s) : %s =\n" (ident field) ut (if total then ty else ty + " option")
                        + "        match v with\n"
                        + String.concat "\n" arms
                    ))

    /// One module per union named in a Fold or Projections request, holding all of them.
    let unionModulesDecl (ctx: Ctx) (requests: Request list) : Result<string list, CodegenError> =
        let named =
            requests
            |> List.choose (fun r ->
                match r with
                | Request.Fold n -> Some n
                | Request.Projections(n, _) -> Some n
                | _ -> None)
            |> List.distinct

        named
        |> List.map (fun name ->
            match ctx.Unions |> List.tryFind (fun u -> u.Name = name) with
            | None ->
                refuse
                    (sprintf "a fold or projection over '%s'" name)
                    "a union derivation is generated over a union this module emits"
                    "name a union the selected kinds reach"
            | Some u ->
                let folds = requests |> List.exists (fun r -> r = Request.Fold name)

                let fields =
                    requests
                    |> List.collect (fun r ->
                        match r with
                        | Request.Projections(n, fs) when n = name -> fs
                        | _ -> [])
                    |> List.distinct

                let clash = folds && List.contains "fold" fields

                if clash then
                    refuse
                        (sprintf "a projection of 'fold' over '%s' beside its fold" name)
                        "the fold and the projections share the union's module, so a field named `fold` would shadow it"
                        "drop one of the two requests"
                else
                    ((if folds then [ foldDecl ctx u ] else [])
                     @ (fields |> List.map (projectionDecl ctx u)))
                    |> sequenceR
                    |> Result.map (fun members ->
                        sprintf "/// Derived members of `%s`.\nmodule %s =\n" name name
                        + String.concat "\n\n" members))
        |> sequenceR

    // -----------------------------------------------------------------------
    // Emission 7 — default records as values.
    // -----------------------------------------------------------------------

    let defaultRecordsDecl (ctx: Ctx) : Result<string, CodegenError> =
        let defaultFor (tag: string) (field: string) =
            ctx.Idl.Defaults
            |> List.tryPick (fun d ->
                if d.Kind = tag && d.Field = field then
                    Some d.Value
                else
                    None)

        let value (typeName: string) (declared: IdlField -> IdlValue option) (fs: IdlField list) =
            let parts = fs |> List.map (fun f -> f, fieldValue ctx.Idl (declared f) f)

            if parts |> List.exists (fun (_, v) -> v.IsNone) then
                None
            else
                parts
                |> List.map (fun (f, v) -> v.Value |> Result.map (fun e -> sprintf "%s = %s" (pascal f.Name) e))
                |> sequenceR
                |> Result.map (fun assigns ->
                    let gen = declParams ctx.Msg typeName []

                    sprintf
                        "/// `%s` with every field at the value a caller need not pass.\nlet default%s%s: %s%s =\n    %s"
                        typeName
                        typeName
                        gen
                        typeName
                        gen
                        (recordLit typeName assigns))
                |> Some

        let kinds =
            ctx.Kinds
            |> List.filter (fun k -> not (ctx.Projected.Contains k.Tag))
            |> List.choose (fun k -> value (k.Tag + "Spec") (fun f -> defaultFor k.Tag f.Name) k.Fields)

        let records =
            ctx.Records |> List.choose (fun r -> value r.Name (fun _ -> None) r.Fields)

        match kinds @ records with
        | [] ->
            refuse
                "default records over a vocabulary none of whose kinds or records is fully defaulted"
                "a default record is a value only where every field has one without the caller"
                "declare defaults for the required fields, or drop the request"
        | xs -> xs |> sequenceR |> Result.map (String.concat "\n\n")

    // -----------------------------------------------------------------------
    // Emission 8 — the vocabulary constants.
    // -----------------------------------------------------------------------

    let constantsDecl (ctx: Ctx) : string =
        let setLit (xs: string list) =
            match xs with
            | [] -> "Set.empty"
            | _ -> "set [ " + (xs |> List.map SourceLit.fsString |> String.concat "; ") + " ]"

        let mapLit (entries: (string * string list) list) =
            match entries with
            | [] -> "Map.empty"
            | _ ->
                "Map.ofList [ "
                + (entries
                   |> List.map (fun (k, vs) -> sprintf "%s, %s" (SourceLit.fsString k) (setLit vs))
                   |> String.concat "; ")
                + " ]"

        let categories =
            ctx.Kinds
            |> List.map (fun k -> k.Category)
            |> List.distinct
            |> List.map (fun c -> c, ctx.Kinds |> List.filter (fun k -> k.Category = c) |> List.map _.Tag)

        let fieldsOf (ks: IdlKind list) =
            ks |> List.map (fun k -> k.Tag, k.Fields |> List.map _.Name)

        [ "/// The kind tags of each declared category."
          "let kindCategories: Map<string, Set<string>> ="
          "    " + mapLit categories
          ""
          "/// The wire field names of each kind."
          "let kindFieldNames: Map<string, Set<string>> ="
          "    " + mapLit (fieldsOf ctx.Kinds)
          ""
          "/// The wire field names of the node envelope."
          "let envelopeFieldNames: Set<string> ="
          "    " + setLit (ctx.Idl.NodeFields |> List.map _.Name)
          ""
          "/// The wire field names of each tree op."
          "let opFieldNames: Map<string, Set<string>> ="
          "    " + mapLit (fieldsOf ctx.Idl.Ops) ]
        |> String.concat "\n"

    /// Every requested derivation after the module's existing members, in a fixed order: keyed
    /// walk, slot enumerators, message map, default records, constants, then the union modules.
    let derivedDecl (ctx: Ctx) (requests: Request list) : Result<string list, CodegenError> =
        let has r = List.contains r requests

        let slots =
            requests
            |> List.choose (fun r ->
                match r with
                | Request.SlotsOf t -> Some t
                | _ -> None)
            |> List.distinct

        let parts =
            [ (if has Request.KeyedPositions then [ keyedDecl ctx ] else [])
              slots |> List.map (slotsDecl ctx)
              (if has Request.MapMsg then [ mapMsgDecl ctx ] else [])
              (if has Request.DefaultRecords then
                   [ defaultRecordsDecl ctx ]
               else
                   [])
              (if has Request.VocabularyConstants then
                   [ Ok(constantsDecl ctx) ]
               else
                   []) ]
            |> List.concat
            |> sequenceR

        match parts, unionModulesDecl ctx requests with
        | Ok ps, Ok ms -> Ok(ps @ ms)
        | Error e, _
        | _, Error e -> Error e
