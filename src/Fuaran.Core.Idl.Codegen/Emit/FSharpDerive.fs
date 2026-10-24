namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
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

    let private refuse (construct: string) (principle: string) (alternative: string) : Result<'a, CodegenError> =
        Error(CodegenError.UnsupportedConstruct(construct, principle, alternative))

    // -----------------------------------------------------------------------
    // Phase 403 — a PROJECTED kind's fields. Its record is verbatim host source, so the fields a
    // derivation reads are the projection's declared `RecordFields` (the record's host shapes),
    // never the kind's wire fields: the two differ exactly where a projection earns its keep (a
    // wire key optional where the record's member is required, two wire keys merged into one).
    // -----------------------------------------------------------------------

    /// The principle every refusal over an undeclared projected record states.
    let private undeclaredRecord =
        "a projected kind's record is verbatim host source, so the generator cannot read its field shapes"

    /// The alternative every refusal over an undeclared projected record names.
    let private declareRecord (orElse: string) =
        sprintf "declare the projected record's fields through the projection's RecordFields, or %s" orElse

    /// The fields a derivation reads for a kind: a kind's own wire fields, a projected kind's
    /// declared record fields, or — for a projected kind that declares none — `undeclared ()`,
    /// which says whether that derivation can proceed without them.
    let recordFields
        (projections: Map<string, KindProjection>)
        (k: IdlKind)
        (undeclared: unit -> Result<IdlField list, CodegenError>)
        : Result<IdlField list, CodegenError> =
        match projections.TryFind k.Tag with
        | None -> Ok k.Fields
        | Some { RecordFields = Some fs } -> Ok fs
        | Some { RecordFields = None } -> undeclared ()

    /// The node positions of a projected kind with no declared record, read from its WIRE: a
    /// projection's encoder writes the wire, and a node on no wire key is a node no host can read
    /// back, so a wire that holds no node (`holds` false for every field) says the record holds
    /// none and the kind has no position to walk. A wire that does hold one says nothing about
    /// which member of the record holds it, and is refused by name.
    let nodeFieldsOf
        (projections: Map<string, KindProjection>)
        (holds: IdlField -> bool)
        (construct: string)
        (orElse: string)
        (k: IdlKind)
        : Result<IdlField list, CodegenError> =
        recordFields projections k (fun () ->
            if k.Fields |> List.exists holds then
                refuse
                    (sprintf "%s of the projected kind '%s'" construct k.Tag)
                    undeclaredRecord
                    (declareRecord orElse)
            else
                Ok [])

    let private nodeT (ctx: Decl) = "Node" + declParams ctx.Msg "Node" []

    /// The host type text of a declared record / union name in this module.
    let private declT (ctx: Decl) (name: string) (ps: string list) = name + declParams ctx.Msg name ps

    let private unionParams (ctx: Decl) (name: string) =
        Decl.tryUnion ctx name |> Option.map _.Params |> Option.defaultValue []

    /// The union-case pattern binding every field positionally: `U.Tag(__f0, __f1)`, or `U.Tag`.
    let private casePattern (u: IdlUnion) (c: IdlUnionCase) =
        match c.Fields with
        | [] -> u.Name + "." + c.Tag
        | fs -> sprintf "%s.%s(%s)" u.Name c.Tag (fs |> List.mapi (fun i _ -> sprintf "__f%d" i) |> String.concat ", ")

    // -----------------------------------------------------------------------
    // Node positions — the keyed walk (emission 2), printed from [[Derive.keyedPlan]].
    // -----------------------------------------------------------------------

    /// The F# lister of the nodes a value holds, in declaration order.
    let private lister: PositionPrinter =
        { Node = sprintf "[ %s ]"
          List = fun e x b -> sprintf "List.collect (fun %s -> %s) %s" x b e
          MapItem = fun _ x -> x
          Map = fun e x b _ -> sprintf "(%s |> Map.toList |> List.collect (fun (_, %s) -> %s))" e x b
          Named = sprintf "nodesIn%s %s"
          Other = fun _ -> "[]"
          OptionalVar = fun _ -> "__o"
          Optional = sprintf "(match %s with Some __o -> %s | None -> [])" }

    /// The F# rebuilder of a value with `f` applied to every node it holds, in the order [[lister]]
    /// lists them.
    let private mapper: PositionPrinter =
        { Node = sprintf "f %s"
          List = fun e x b -> sprintf "List.map (fun %s -> %s) %s" x b e
          MapItem = fun _ x -> x
          Map =
            fun e x b d ->
                sprintf "(%s |> Map.toList |> List.map (fun (__k%d, %s) -> __k%d, %s) |> Map.ofList)" e d x d b
          Named = sprintf "mapNodesIn%s f %s"
          Other = id
          OptionalVar = fun _ -> "__o"
          Optional = fun e b -> sprintf "Option.map (fun __o -> %s) %s" b e }

    /// `a @ b @ c`, or `[]` for none.
    let private appendAll (xs: string list) =
        match xs with
        | [] -> "[]"
        | _ -> String.concat " @ " xs

    /// The keyed walk: per-declaration node listers and rebuilders, the node-level
    /// `keyedChildren` / `withKeyedChildren`, the generated full walk, and the `KeyedWitness`.
    let keyedDecl (ctx: Decl) : Result<string, CodegenError> =
        let hs = ctx.NodeHolders
        let node = nodeT ctx
        let collectField (f: IdlField) e = Derive.positionsOf lister hs f e
        let mapField (f: IdlField) e = Derive.positionsOf mapper hs f e

        let recordHelpers (r: IdlRecord, fs: IdlField list) =
            let t = declT ctx r.Name []

            let lister =
                fs
                |> List.map (fun f -> collectField f ("v." + pascal f.Name))
                |> sequenceR
                |> Result.map (fun xs ->
                    sprintf "and private nodesIn%s (v: %s) : %s list =\n    %s" r.Name t node (appendAll xs))

            let mapper =
                fs
                |> List.mapi (fun i f -> mapField f ("v." + pascal f.Name) |> Result.map (fun b -> i, f, b))
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

        let unionHelpers (u: IdlUnion, holding: (IdlUnionCase * (int * IdlField) list) list, wild: bool) =
            let t = declT ctx u.Name u.Params
            let gen = declParams ctx.Msg u.Name u.Params

            let lister =
                holding
                |> List.map (fun (c, fs) ->
                    fs
                    |> List.map (fun (i, f) -> collectField f (sprintf "__f%d" i))
                    |> sequenceR
                    |> Result.map (fun xs -> sprintf "    | %s -> %s" (casePattern u c) (appendAll xs)))
                |> sequenceR
                |> Result.map (fun arms ->
                    sprintf "and private nodesIn%s%s (v: %s) : %s list =\n    match v with\n" u.Name gen t node
                    + String.concat "\n" (arms @ (if wild then [ "    | _ -> []" ] else [])))

            let mapper =
                holding
                |> List.map (fun (c, fs) ->
                    let held = Map.ofList fs

                    c.Fields
                    |> List.mapi (fun i _ ->
                        match held.TryFind i with
                        | Some f -> mapField f (sprintf "__f%d" i) |> Result.map Some
                        | None -> Ok None)
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

        // Phase 403 — a projected kind's keyed positions are its declared record's.
        Derive.keyedPlan ctx (fun k ->
            nodeFieldsOf
                ctx.Projections
                (fun f -> Decl.holdsIn hs f.Type)
                "the keyed node positions"
                "do not request KeyedPositions"
                k)
        |> Result.bind (fun plan ->
            let helpers =
                (plan.Records |> List.collect recordHelpers)
                @ (plan.Unions |> List.collect unionHelpers)
                |> sequenceR

            let listerArms =
                plan.Kinds
                |> List.map (fun (k, fs) ->
                    fs
                    |> List.map (fun f -> collectField f ("s." + pascal f.Name))
                    |> sequenceR
                    |> Result.map (fun xs -> sprintf "        | NodeKind.%s s -> %s" k.Tag (appendAll xs)))
                |> sequenceR

            let envListers =
                plan.Envelope
                |> List.map (fun f -> collectField f ("n." + pascal f.Name))
                |> sequenceR

            let mapperArms =
                plan.Kinds
                |> List.map (fun (k, fs) ->
                    fs
                    |> List.mapi (fun i f -> mapField f ("s." + pascal f.Name) |> Result.map (fun b -> i, f, b))
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
                plan.Envelope
                |> List.mapi (fun i f -> mapField f ("n." + pascal f.Name) |> Result.map (fun b -> i, f, b))
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
                    if List.isEmpty plan.Kinds then
                        " []"
                    else
                        "\n        match n.Kind with\n"
                        + String.concat "\n" (listerArms @ (if plan.KindWild then [ "        | _ -> []" ] else []))

                let lister =
                    sprintf "/// The nodes this node holds in keyed, non-structural positions, in declaration order.\n"
                    + sprintf "let keyedChildren (n: %s) : %s list =\n" node node
                    + sprintf "    let ofKind =%s\n\n" ofKind
                    + sprintf "    %s" (appendAll ("ofKind" :: envListers))

                let kindMap =
                    if List.isEmpty plan.Kinds then
                        " n.Kind"
                    else
                        "\n        match n.Kind with\n"
                        + String.concat "\n" (mapperArms @ (if plan.KindWild then [ "        | k -> k" ] else []))

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
            | _, _, _, _, Error e -> Error e)

    // -----------------------------------------------------------------------
    // Emission 3 — the typed slot enumerator.
    // -----------------------------------------------------------------------

    let slotsDecl (ctx: Decl) (typeName: string) : Result<string, CodegenError> =
        let generic = unionParams ctx typeName |> List.isEmpty |> not

        let declared =
            ctx.Unions |> List.exists (fun u -> u.Name = typeName)
            || ctx.Records |> List.exists (fun r -> r.Name = typeName)
            || ctx.Idl.Enums |> List.exists (fun e -> e.Name = typeName)

        let slotT = if generic then "obj" else declT ctx typeName []

        // Phase 540 — an upcast, not `box`: under nullness checking `box` answers `obj | null`, which
        // the declared `obj` result refuses (FS3261), while a union value upcast is a non-null `obj`.
        let wrap (v: string) =
            if generic then sprintf "(%s :> obj)" v else v

        /// The pairs a field contributes ([[Derive.slotPosition]]), or `None` when it holds no slot
        /// of the type.
        let ofField (owner: string) (f: IdlField) : string option =
            let e = owner + "." + pascal f.Name
            let key = SourceLit.fsString f.Name

            Derive.slotPosition typeName f
            |> Option.map (fun at ->
                let direct (e: string) =
                    if at.List then
                        sprintf "(%s |> List.map (fun __v -> %s, %s))" e key (wrap "__v")
                    else
                        sprintf "[ %s, %s ]" key (wrap e)

                if at.Optional then
                    sprintf "(match %s with Some __v -> %s | None -> [])" e (direct "__v")
                else
                    direct e)

        // Phase 403 — a projected kind's slots are its declared record's. Undeclared, it is refused
        // whatever its wire says: a host record may hold a value of the type that its wire spells
        // otherwise (two wire keys merged into one member), so an absent wire field proves nothing.
        let armsR =
            ctx.Kinds
            |> List.map (fun k ->
                recordFields ctx.Projections k (fun () ->
                    refuse
                        (sprintf "a slot enumerator over '%s' through the projected kind '%s'" typeName k.Tag)
                        undeclaredRecord
                        (declareRecord (sprintf "do not request SlotsOf %s" (SourceLit.fsString typeName))))
                |> Result.map (fun fs ->
                    match fs |> List.choose (ofField "s") with
                    | [] -> None
                    | xs -> Some(sprintf "        | NodeKind.%s s -> %s" k.Tag (appendAll xs))))
            |> sequenceR
            |> Result.map (List.choose id)

        let arms = armsR |> Result.defaultValue []

        let env = ctx.Idl.NodeFields |> List.choose (ofField "n")

        if not declared then
            refuse
                (sprintf "a slot enumerator over '%s'" typeName)
                "a slot enumerator is derived over a declared enum, record or union this module emits"
                "name a declared type the selected kinds reach"
        elif Result.isError armsR then
            armsR |> Result.map (fun _ -> "")
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
        (ctx: Decl)
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

    let rec private mentionsMsg (ctx: Decl) (t: IdlType) =
        match t with
        | TFn s -> mentionsMsgText s.FSharp
        | TList i
        | TMap i -> mentionsMsg ctx i
        | TNode -> ctx.Msg.Contains "Node"
        | TUnion(n, args) -> ctx.Msg.Contains n || List.exists (mentionsMsg ctx) args
        | TRecord n -> ctx.Msg.Contains n
        | _ -> false

    let rec private mapMsgE
        (ctx: Decl)
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

    let private mapMsgField (ctx: Decl) (where: string) (f: IdlField) (e: string) =
        match f.Opt with
        | Optional when mentionsMsg ctx f.Type ->
            mapMsgE ctx where f.Type "__o" 0
            |> Result.map (fun b -> sprintf "Option.map (fun __o -> %s) %s" b e)
        | _ -> mapMsgE ctx where f.Type e 0

    let mapMsgDecl (ctx: Decl) : Result<string, CodegenError> =
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
                    match ctx.Projections.TryFind k.Tag with
                    // Phase 403 — the projection's own map, composed verbatim: the projected record
                    // is host source, so its map is written once beside it, as its `Mk` is.
                    | Some { MapMsg = Some map } -> Ok map
                    | Some { MapMsg = None } ->
                        refuse
                            (sprintf "the message map over the projected kind '%s'" k.Tag)
                            "a projected kind's record is verbatim host source, so the generator cannot construct it"
                            "supply the projected kind's map through the declared support, or do not request MapMsg"
                    | None -> recordMapper (k.Tag + "Spec") k.Tag k.Fields)

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

    /// The F# fold over a value: `List.fold` / `Map` folds, the union's own `fold` recursing.
    let private folder: FoldPrinter =
        { Self = sprintf "fold folder %s %s"
          List = fun e x s b st -> sprintf "List.fold (fun %s %s -> %s) %s %s" s x b st e
          MapItem = fun _ x -> x
          Map = fun e x s b st -> sprintf "(%s |> Map.toList |> List.fold (fun %s (_, %s) -> %s) %s)" e s x b st
          Access = fun e f -> e + "." + pascal f.Name
          OptionalVar = fun _ d -> sprintf "__o%d" d
          Optional = fun e d b st -> sprintf "(match %s with Some __o%d -> %s | None -> %s)" e d b st
          StepState = fun d i st -> if i = 0 then st else sprintf "__c%d_%d" d (i - 1)
          Thread =
            fun d _ bodies ->
                let lets = bodies |> List.mapi (fun i b -> sprintf "let __c%d_%d = %s in" d i b)
                sprintf "(%s __c%d_%d)" (String.concat " " lets) d (List.length bodies - 1) }

    let private foldDecl (ctx: Decl) (u: IdlUnion) : Result<string, CodegenError> =
        let ut = declT ctx u.Name u.Params

        Derive.foldPlan ctx u
        |> Result.bind (fun recursive ->
            recursive
            |> List.map (fun (c, fs) ->
                let byName = fs |> List.map (fun (i, f) -> f.Name, i) |> Map.ofList

                Derive.foldMembers folder ctx u (List.map snd fs) (fun f -> sprintf "__f%d" byName[f.Name]) "state" 0
                |> Result.map (fun b -> sprintf "        | %s -> %s" (casePattern u c) b))
            |> sequenceR
            |> Result.map (fun arms ->
                let wild =
                    if List.length recursive < List.length u.Cases then
                        [ "        | _ -> state" ]
                    else
                        []

                sprintf "    /// Fold `folder` over this value and every nested `%s` it holds, in preorder.\n" u.Name
                + sprintf "    let rec fold (folder: 'S -> %s -> 'S) (state: 'S) (v: %s) : 'S =\n" ut ut
                + "        let state = folder state v\n\n"
                + "        match v with\n"
                + String.concat "\n" (arms @ wild)))

    let private projectionDecl (ctx: Decl) (u: IdlUnion) (field: string) : Result<string, CodegenError> =
        let fsType = fsTypeIn ctx.Msg

        let hostType (f: IdlField) =
            match f.Opt with
            | Optional -> fsType f.Type |> Result.map (fun s -> s + " option")
            | _ -> fsType f.Type

        Derive.projectionPlan u field
        |> Result.bind (fun (carriers, total) ->
            let _, _, f0 = List.head carriers

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
                    let ty = List.head types
                    let ut = declT ctx u.Name u.Params

                    let arms =
                        carriers
                        |> List.map (fun (c, i, _) ->
                            let pat =
                                c.Fields
                                |> List.mapi (fun j _ -> if j = i then "__v" else "_")
                                |> String.concat ", "

                            sprintf
                                "        | %s.%s(%s) -> %s"
                                u.Name
                                c.Tag
                                pat
                                (if total then "__v" else "Some __v"))

                    let arms = arms @ (if total then [] else [ "        | _ -> None" ])

                    Ok(
                        sprintf
                            "    /// The `%s` field, %s.\n"
                            f0.Name
                            (if total then
                                 "which every case carries"
                             else
                                 "where the case carries one")
                        + sprintf
                            "    let %s (v: %s) : %s =\n"
                            (ident field)
                            ut
                            (if total then ty else ty + " option")
                        + "        match v with\n"
                        + String.concat "\n" arms
                    )))

    /// One module per union named in a Fold or Projections request, holding all of them.
    let unionModulesDecl (ctx: Decl) (requests: Derivation list) : Result<string list, CodegenError> =
        Derive.unionModules ctx requests
        |> Result.bind (fun unions ->
            unions
            |> List.map (fun (u, folds, fields) ->
                ((if folds then [ foldDecl ctx u ] else [])
                 @ (fields |> List.map (projectionDecl ctx u)))
                |> sequenceR
                |> Result.map (fun members ->
                    sprintf "/// Derived members of `%s`.\nmodule %s =\n" u.Name u.Name
                    + String.concat "\n\n" members))
            |> sequenceR)

    // -----------------------------------------------------------------------
    // Emission 7 — default records as values.
    // -----------------------------------------------------------------------

    let defaultRecordsDecl (ctx: Decl) : Result<string, CodegenError> =
        // A projected kind's record is the projection's, which supplies its own values.
        Derive.defaultRecords ctx (fun k -> not (ctx.Projections.ContainsKey k.Tag))
        |> Result.bind (fun records ->
            records
            |> List.map (fun r ->
                r.Members
                |> List.map (fun (f, mv) ->
                    let lit = fsDefaultLit ctx.Idl f.Type

                    fsMember lit lit (fun v -> CodegenError.UnsupportedDefault(f.Type, v)) f mv
                    |> Option.defaultValue (Ok(ident f.Name))
                    |> Result.map (fun e -> sprintf "%s = %s" (pascal f.Name) e))
                |> sequenceR
                |> Result.map (fun assigns ->
                    let gen = declParams ctx.Msg r.Name []

                    sprintf
                        "/// `%s` with every field at the value a caller need not pass.\nlet default%s%s: %s%s =\n    %s"
                        r.Name
                        r.Name
                        gen
                        r.Name
                        gen
                        (recordLit r.Name assigns)))
            |> sequenceR
            |> Result.map (String.concat "\n\n"))

    // -----------------------------------------------------------------------
    // Emission 8 — the vocabulary constants.
    // -----------------------------------------------------------------------

    let constantsDecl (ctx: Decl) : string =
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

        let c = Derive.constants ctx

        [ "/// The kind tags of each declared category."
          "let kindCategories: Map<string, Set<string>> ="
          "    " + mapLit c.Categories
          ""
          "/// The wire field names of each kind."
          "let kindFieldNames: Map<string, Set<string>> ="
          "    " + mapLit c.KindFields
          ""
          "/// The wire field names of the node envelope."
          "let envelopeFieldNames: Set<string> ="
          "    " + setLit c.Envelope
          ""
          "/// The wire field names of each tree op."
          "let opFieldNames: Map<string, Set<string>> ="
          "    " + mapLit c.OpFields ]
        |> String.concat "\n"

    /// Every requested derivation after the module's existing members, in a fixed order: keyed
    /// walk, slot enumerators, message map, default records, constants, then the union modules.
    let derivedDecl (ctx: Decl) (requests: Derivation list) : Result<string list, CodegenError> =
        let has r = List.contains r requests

        let slots =
            requests
            |> List.choose (fun r ->
                match r with
                | Derivation.SlotsOf t -> Some t
                | _ -> None)
            |> List.distinct

        let parts =
            [ (if has Derivation.KeyedPositions then
                   [ keyedDecl ctx ]
               else
                   [])
              slots |> List.map (slotsDecl ctx)
              (if has Derivation.MapMsg then [ mapMsgDecl ctx ] else [])
              (if has Derivation.DefaultRecords then
                   [ defaultRecordsDecl ctx ]
               else
                   [])
              (if has Derivation.VocabularyConstants then
                   [ Ok(constantsDecl ctx) ]
               else
                   []) ]
            |> List.concat
            |> sequenceR

        match parts, unionModulesDecl ctx requests with
        | Ok ps, Ok ms -> Ok(ps @ ms)
        | Error e, _
        | _, Error e -> Error e

/// Phase 541 / 562 (DECISIONS.md D175, D230) — the RECORD of overridable collecting decoders, from
/// the declarations a selection reaches: its type (one field per node, non-generic union, record,
/// unprojected kind spec, enumeration and JSON scalar; a `Collecting<Union>` seam per generic union;
/// and `Absent`), the generated enumeration decoders, and `collectingDecoders`, the generated value.
/// The decoders it names are [[FSharpCodec.collectingDecl]]'s.
module internal FSharpCollectingRecord =

    /// The record's three pieces of module text.
    type Parts =
        {
            /// `type CollectingDecoders`, and each generic union's seam beside it.
            TypeDecl: string
            /// Each enumeration's generated collecting decoder.
            Defaults: string
            /// `collectingDecoders`, the record of the generated decoders.
            Generated: string
        }

    /// The record for `decl`, or the refusal of a vocabulary whose types the record would name alike.
    let emit (sup: GenSupport) (decl: Decl) : Result<Parts, CodegenError> =
        let msg, kinds, unions, records = decl.Msg, decl.Kinds, decl.Unions, decl.Records
        let nodeTy = objParams msg "Node" []
        let projected (k: IdlKind) = sup.KindProjections.ContainsKey k.Tag

        let specTy (k: IdlKind) =
            k.Tag + "Spec" + objParams msg (k.Tag + "Spec") []

        // A projected kind's decoder is the host projection's, a leaf with no position beneath it.
        // Phase 474 — an inline declaration is read from its holder's members, never from a value,
        // so it has no slot: its readers take the record and the holder's members (`colMembers*`).
        // Phase 562 — every enumeration and the four JSON scalars have a slot (D230.1).
        let slots =
            [ yield "Node", "Node" + nodeTy
              for u in unions do
                  if List.isEmpty u.Params && not (Decl.isKeyed u) then
                      yield u.Name, u.Name + objParams msg u.Name []
              for r in records do
                  if not r.Inline then
                      yield r.Name, r.Name + objParams msg r.Name []
              for k in kinds do
                  if not (projected k) then
                      yield k.Tag + "Spec", specTy k
              for e in decl.Enums do
                  yield e.Name, e.Name
              yield "String", "string"
              yield "Int", "int"
              yield "Float", "float"
              yield "Bool", "bool" ]

        // Phase 562 (D230.2) — a GENERIC union's slot is an interface whose one member, `Decode`, is
        // generic in the union's parameters: an F# record field cannot be generic, and one field per
        // instantiation would make a host restate its policy once per instantiation the vocabulary
        // uses (and could not name an instantiation over a type parameter).
        let generic =
            unions
            |> List.filter (fun u -> not (List.isEmpty u.Params) && not (Decl.isKeyed u))

        let seamName (u: IdlUnion) = "Collecting" + u.Name

        // A slot's field is named by its type, so two types the record would name alike (a vocabulary
        // type named `String`, `Int`, `Float`, `Bool` or `Absent`, or two that print alike) are refused
        // at generation rather than emitted as a module that does not compile; so is a vocabulary type
        // named as a generic union's seam.
        let fieldNames =
            (slots |> List.map fst) @ (generic |> List.map _.Name) @ [ "Absent" ]

        let typeNames =
            Set.ofList (
                [ "Node"; "NodeKind"; "CollectingDecoders" ]
                @ (decl.Enums |> List.map _.Name)
                @ (unions |> List.map _.Name)
                @ (records |> List.map _.Name)
                @ (kinds |> List.map (fun k -> k.Tag + "Spec"))
            )

        let collision =
            match fieldNames |> List.countBy id |> List.tryFind (fun (_, n) -> n > 1) with
            | Some(name, _) -> Some(sprintf "two collecting decoders named '%s'" name)
            | None ->
                generic
                |> List.tryFind (fun u -> typeNames.Contains(seamName u))
                |> Option.map (fun u ->
                    sprintf "the collecting seam '%s' beside a vocabulary type of that name" (seamName u))

        let genericSig (u: IdlUnion) =
            let decoders =
                u.Params
                |> List.map (fun p -> sprintf "(JVal -> Result<'%s, DecodeError list>)" p)
                |> String.concat " * "

            sprintf
                "Decode<%s> : CollectingDecoders * %s * JVal -> Result<%s%s, DecodeError list>"
                (u.Params |> List.map (fun p -> "'" + p) |> String.concat ", ")
                decoders
                u.Name
                (objParams msg u.Name u.Params)

        let recordTypeDecl =
            "/// Phase 541 — the collecting decoders a host may override, one per type the vocabulary reaches\n"
            + "/// through a nested position. Each takes the record it is read from, so a decoder that delegates\n"
            + "/// to `collectingDecoders.<Type> self` keeps every position beneath it reading `self`.\n"
            + "/// Phase 562 — every enumeration and JSON scalar has a field too; a generic union's field is its\n"
            + "/// `Collecting<Union>` seam, generic in the union's parameters; and `Absent` is asked at every\n"
            + "/// declared member an object does not carry, with the holder's name, the key and the object.\n"
            + "type CollectingDecoders =\n"
            + (slots
               |> List.mapi (fun i (field, ty) ->
                   sprintf
                       "%s%s: CollectingDecoders -> JVal -> Result<%s, DecodeError list>"
                       (if i = 0 then "    { " else "      ")
                       field
                       ty)
               |> String.concat "\n")
            + (generic
               |> List.map (fun u -> sprintf "\n      %s: %s" u.Name (seamName u))
               |> String.concat "")
            + "\n      Absent: CollectingDecoders -> string -> string -> JVal -> Result<JVal option, DecodeError list> }"
            + (generic
               |> List.map (fun u ->
                   sprintf
                       "\n\n/// Phase 562 — the collecting decoder of `%s` at every instantiation, from its argument decoders.\nand %s =\n    abstract %s"
                       u.Name
                       (seamName u)
                       (genericSig u))
               |> String.concat "")

        // Phase 562 — each enumeration's generated collecting decoder, its short-circuiting one lifted.
        let enumDefaults =
            decl.Enums
            |> List.map (fun e ->
                sprintf
                    "let private col%s (_: CollectingDecoders) (j: JVal) : Result<%s, DecodeError list> = cOne (dec%s j)"
                    e.Name
                    e.Name
                    e.Name)
            |> String.concat "\n\n"

        let generatedDecl =
            "/// Phase 541 — the generated collecting decoders: what every `decode*All` entry reads through, and\n"
            + "/// the record a host copies (`{ collectingDecoders with <Type> = … }`) to override one type.\n"
            + "let collectingDecoders: CollectingDecoders =\n"
            + (slots
               |> List.mapi (fun i (field, _) ->
                   sprintf "%s%s = col%s" (if i = 0 then "    { " else "      ") field field)
               |> String.concat "\n")
            + (generic
               |> List.map (fun u ->
                   let ps = u.Params |> List.map (fun p -> "col" + p) |> String.concat ", "

                   sprintf
                       "\n      %s =\n        { new %s with\n            member _.Decode(__ov, %s, j) = col%s __ov %s j }"
                       u.Name
                       (seamName u)
                       ps
                       u.Name
                       (u.Params |> List.map (fun p -> "col" + p) |> String.concat " "))
               |> String.concat "")
            + "\n      Absent = colAbsent }"

        match collision with
        | Some what ->
            Error(
                CodegenError.UnsupportedConstruct(
                    what,
                    "a collecting decoder is named by its type",
                    "rename the vocabulary type"
                )
            )
        | None ->
            Ok
                { TypeDecl = recordTypeDecl
                  Defaults = enumDefaults
                  Generated = generatedDecl }
