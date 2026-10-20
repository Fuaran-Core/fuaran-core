namespace Fuaran.Core.Idl.Emit

open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Idl.Emit.Core

// ---------------------------------------------------------------------------
// Phase 524 (B §8 S10) — the STRUCTURAL DERIVATIONS (Phase 374) on one plan, a printer per backend.
// The F# and TypeScript hosts derive the same members — the structural witness, the keyed walk,
// the slot enumerators, a union's fold and projections, the default records, the vocabulary
// constants — and each used to decide for itself which kinds bear children, which positions hold
// a node, which cases recurse, which fields a projection reads and which records are fully
// defaulted, then walk a slot's type to reach them. Those decisions and walks are stated once here;
// each backend prints the plan in its own syntax.
// ---------------------------------------------------------------------------

/// How a kind's structural children are re-assigned from a child list.
[<RequireQualifiedAccess>]
type internal ChildLayout =
    /// The kind's one structural position is a node LIST: the child list is it, whole.
    | Whole of IdlField
    /// The kind's structural positions are single nodes, re-assigned by position.
    | Positional of IdlField list

/// What a backend prints for one step of a walk over the node POSITIONS a value of a type holds (a
/// lister and a rebuilder are two printers of it).
type internal PositionPrinter =
    {
        /// A node at `e`.
        Node: string -> string
        /// A list at `e`, its element bound to `x`, from the element's body.
        List: string -> string -> string -> string
        /// A map entry's value, the map at `e` and its key or value bound to `x`.
        MapItem: string -> string -> string
        /// A map at `e`, its entry bound to `x`, from the entry value's body, at depth `d`.
        Map: string -> string -> string -> int -> string
        /// A declared record or union holding nodes, by name, at `e`.
        Named: string -> string -> string
        /// A value holding no node, at `e`.
        Other: string -> string
        /// The variable an optional member's present value is read from.
        OptionalVar: string -> string
        /// An optional member at `e`, from its present value's body.
        Optional: string -> string -> string
    }

/// What a backend prints for a self-recursive union's FOLD over a value of a type.
type internal FoldPrinter =
    {
        /// The union itself at `e`, folded from state `st`: `Self st e`.
        Self: string -> string -> string
        /// A list at `e`, element `x`, step state `s`, from the element's body, from state `st`.
        List: string -> string -> string -> string -> string -> string
        /// A map entry's value, the map at `e` and its key or value bound to `x`.
        MapItem: string -> string -> string
        /// A map at `e`, entry `x`, step state `s`, from the entry value's body, from state `st`.
        Map: string -> string -> string -> string -> string -> string
        /// A record member read off `e`.
        Access: string -> IdlField -> string
        /// The variable an optional member's present value is read from, at depth `d`.
        OptionalVar: string -> int -> string
        /// An optional member at `e` (depth `d`), from its present value's body, from state `st`.
        Optional: string -> int -> string -> string -> string
        /// The state the `i`-th of several members at depth `d` folds from, the first from `st`.
        StepState: int -> int -> string -> string
        /// Several members at depth `d` threaded from `st`, from each member's body in order.
        Thread: int -> string -> string list -> string
    }

/// One field's place in a slot enumerator over a declared type.
type internal SlotPosition =
    {
        Field: IdlField
        /// The field holds a LIST of the type.
        List: bool
        /// The field is optional.
        Optional: bool
    }

/// The keyed walk's plan: every node position that is not a structural child.
type internal KeyedPlan =
    {
        /// The node-holding records, with their node-holding fields.
        Records: (IdlRecord * IdlField list) list
        /// The node-holding unions, with the cases holding a node (their node-holding fields by
        /// position) and whether some case holds none.
        Unions: (IdlUnion * (IdlUnionCase * (int * IdlField) list) list * bool) list
        /// The kinds with a keyed position, with those positions.
        Kinds: (IdlKind * IdlField list) list
        /// Some kind has no keyed position.
        KindWild: bool
        /// The envelope's node-holding fields.
        Envelope: IdlField list
    }

/// A default record: a declaration every member of which has a value without the caller.
type internal DefaultRecord =
    {
        /// The declaration's type name (`<Tag>Spec` for a kind).
        Name: string
        /// The kind, for a kind's spec record.
        Kind: IdlKind option
        /// Every member with its value under the one presence rule's CONSTRUCTOR site.
        Members: (IdlField * MemberValue) list
    }

/// The vocabulary constants, each a list of names in declaration order.
type internal Constants =
    { Categories: (string * string list) list
      KindFields: (string * string list) list
      Envelope: string list
      OpFields: (string * string list) list }

/// The derivations' plans and walks.
module internal Derive =

    let private refuse (construct: string) (principle: string) (alternative: string) : Result<'a, CodegenError> =
        Error(CodegenError.UnsupportedConstruct(construct, principle, alternative))

    // ---- the structural witness (emission 1) ----

    /// A STRUCTURAL child position: a node or node list a kind always carries (`Some isList`). An
    /// optional node is a keyed position — it is not an ordered list a structural edit may rebuild —
    /// and so is a node held anywhere deeper (inside a record, a union case, a list of either, a map).
    let structural (f: IdlField) : bool option =
        match f.Opt, f.Type with
        | (Required | OmitDefault _), TNode -> Some false
        | (Required | OmitDefault _), TList TNode -> Some true
        | _ -> None

    /// The structural positions among `fields`, in field order, each with whether it is a list.
    let structuralFields (fields: IdlField list) : (IdlField * bool) list =
        fields
        |> List.choose (fun f -> structural f |> Option.map (fun isList -> f, isList))

    /// How the kind `tag`'s structural positions are re-assigned from a child list. A kind mixing a
    /// node list with other structural positions has no unambiguous positional split, and is refused
    /// at generation time (GP4) rather than answered by a guard in the generated code.
    let childLayout (tag: string) (positions: (IdlField * bool) list) : Result<ChildLayout, CodegenError> =
        match positions with
        | [ (f, true) ] -> Ok(ChildLayout.Whole f)
        | fs when fs |> List.forall (fun (_, isList) -> not isList) -> Ok(ChildLayout.Positional(List.map fst fs))
        | _ -> Error(CodegenError.MultiChildFieldKind tag)

    // ---- the keyed walk (emission 2) ----

    let private throughArgument (u: string) : Result<'a, CodegenError> =
        refuse
            (sprintf "a node held through a type argument of the generic union '%s'" u)
            "the keyed walk is derived per declaration, and a generic union's argument has no one declaration to derive it from"
            "hold the node in a non-generic record or union, or do not request KeyedPositions"

    /// The walk over the nodes a value of type `t` at `e` holds, in declaration order (`d` names the
    /// binders of nested steps apart).
    let rec positions
        (p: PositionPrinter)
        (hs: Set<string>)
        (t: IdlType)
        (e: string)
        (d: int)
        : Result<string, CodegenError> =
        let x = sprintf "__x%d" d

        match t with
        | TNode -> Ok(p.Node e)
        | TList i -> positions p hs i x (d + 1) |> Result.map (p.List e x)
        | TMap i -> positions p hs i (p.MapItem e x) (d + 1) |> Result.map (fun b -> p.Map e x b d)
        | TRecord n -> Ok(p.Named n e)
        | TUnion(n, args) when args |> List.exists (Decl.holdsIn hs) -> throughArgument n
        | TUnion(n, _) -> Ok(p.Named n e)
        | _ -> Ok(p.Other e)

    /// [[positions]] over a member read from `e`, under its optionality.
    let positionsOf (p: PositionPrinter) (hs: Set<string>) (f: IdlField) (e: string) : Result<string, CodegenError> =
        match f.Opt with
        | Optional -> positions p hs f.Type (p.OptionalVar e) 0 |> Result.map (p.Optional e)
        | _ -> positions p hs f.Type e 0

    /// The keyed walk's plan; `kindFields` is the fields a backend reads for a kind (a projected
    /// kind's declared record on the F# host, the wire fields on a host holding the wire shape).
    let keyedPlan
        (ctx: Decl)
        (kindFields: IdlKind -> Result<IdlField list, CodegenError>)
        : Result<KeyedPlan, CodegenError> =
        let hs = ctx.NodeHolders
        let holds (f: IdlField) = Decl.holdsIn hs f.Type

        ctx.Kinds
        |> List.map (fun k ->
            kindFields k
            |> Result.map (fun fs -> k, fs |> List.filter (fun f -> holds f && (structural f).IsNone)))
        |> sequenceR
        |> Result.map (fun kinds ->
            let keyed = kinds |> List.filter (fun (_, fs) -> not (List.isEmpty fs))

            { Records =
                ctx.Records
                |> List.filter (fun r -> hs.Contains r.Name)
                |> List.map (fun r -> r, r.Fields |> List.filter holds)
              Unions =
                ctx.Unions
                |> List.filter (fun u -> hs.Contains u.Name)
                |> List.map (fun u ->
                    let holding =
                        u.Cases
                        |> List.filter (fun c -> c.Fields |> List.exists holds)
                        |> List.map (fun c -> c, c.Fields |> List.indexed |> List.filter (snd >> holds))

                    u, holding, List.length holding < List.length u.Cases)
              Kinds = keyed
              KindWild = List.length keyed < List.length ctx.Kinds
              Envelope = ctx.Idl.NodeFields |> List.filter holds })

    // ---- the slot enumerator (emission 3) ----

    /// Where `f` holds a value of the declared type `typeName` (directly, optional or in a list).
    let slotPosition (typeName: string) (f: IdlField) : SlotPosition option =
        let isT (t: IdlType) =
            match t with
            | TEnum n
            | TRecord n
            | TUnion(n, _) -> n = typeName
            | _ -> false

        let at isList =
            Some
                { Field = f
                  List = isList
                  Optional = (f.Opt = Optional) }

        match f.Type with
        | t when isT t -> at false
        | TList t when isT t -> at true
        | _ -> None

    // ---- a union's fold and projections (emissions 5 and 6) ----

    /// The cases of `u` that hold a value of `u`, with those fields by position; refused for a union
    /// that does not recurse, which has nothing to fold.
    let foldPlan (ctx: Decl) (u: IdlUnion) : Result<(IdlUnionCase * (int * IdlField) list) list, CodegenError> =
        u.Cases
        |> List.map (fun c ->
            c.Fields
            |> List.indexed
            |> List.map (fun (i, f) -> Decl.foldSelfIn ctx u Set.empty f.Type |> Result.map (fun b -> i, f, b))
            |> sequenceR
            |> Result.map (fun fs -> c, fs |> List.filter (fun (_, _, b) -> b) |> List.map (fun (i, f, _) -> i, f)))
        |> sequenceR
        |> Result.bind (fun cases ->
            match cases |> List.filter (fun (_, fs) -> not (List.isEmpty fs)) with
            | [] ->
                refuse
                    (sprintf "a fold over '%s', which holds no value of its own type" u.Name)
                    "a fold is derived for a union whose cases contain the union itself"
                    "request the fold over a self-recursive union"
            | recursive -> Ok recursive)

    /// The walk folding `u`'s fold over a value of type `t` at `e`, from state `st`, in preorder.
    let rec fold (p: FoldPrinter) (ctx: Decl) (u: IdlUnion) (t: IdlType) (e: string) (st: string) (d: int) =
        let x = sprintf "__x%d" d
        let s = sprintf "__s%d" d

        match t with
        | TUnion(n, _) when n = u.Name -> Ok(p.Self st e)
        | TList i -> fold p ctx u i x s (d + 1) |> Result.map (fun b -> p.List e x s b st)
        | TMap i ->
            fold p ctx u i (p.MapItem e x) s (d + 1)
            |> Result.map (fun b -> p.Map e x s b st)
        | TRecord n ->
            let fs =
                Decl.tryRecord ctx n
                |> Option.map _.Fields
                |> Option.defaultValue []
                |> List.filter (fun f -> Decl.foldSelfIn ctx u (Set.singleton n) f.Type = Ok true)

            foldMembers p ctx u fs (p.Access e) st (d + 1)
        | _ -> Ok st

    and private foldMember (p: FoldPrinter) ctx u (f: IdlField) (e: string) (st: string) (d: int) =
        match f.Opt with
        | Optional ->
            fold p ctx u f.Type (p.OptionalVar e d) st (d + 1)
            |> Result.map (fun b -> p.Optional e d b st)
        | _ -> fold p ctx u f.Type e st d

    /// The state threaded through several members, left to right.
    and foldMembers (p: FoldPrinter) ctx u (fs: IdlField list) (access: IdlField -> string) (st: string) (d: int) =
        match fs with
        | [] -> Ok st
        | [ f ] -> foldMember p ctx u f (access f) st d
        | _ ->
            fs
            |> List.mapi (fun i f -> foldMember p ctx u f (access f) (p.StepState d i st) (d + 1))
            |> sequenceR
            |> Result.map (p.Thread d st)

    /// The cases of `u` carrying the field `field`, each with its position and declaration, and
    /// whether every case does; refused when none does.
    let projectionPlan
        (u: IdlUnion)
        (field: string)
        : Result<(IdlUnionCase * int * IdlField) list * bool, CodegenError> =
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
        | _ -> Ok(carriers, List.length carriers = List.length u.Cases)

    /// The unions named in a Fold or Projections request, each with whether it folds and the
    /// fields it projects, in request order.
    let unionModules
        (ctx: Decl)
        (requests: Derivation list)
        : Result<(IdlUnion * bool * string list) list, CodegenError> =
        requests
        |> List.choose (fun r ->
            match r with
            | Derivation.Fold n -> Some n
            | Derivation.Projections(n, _) -> Some n
            | _ -> None)
        |> List.distinct
        |> List.map (fun name ->
            match Decl.tryUnion ctx name with
            | None ->
                refuse
                    (sprintf "a fold or projection over '%s'" name)
                    "a union derivation is generated over a union this module emits"
                    "name a union the selected kinds reach"
            | Some u ->
                let folds = requests |> List.exists (fun r -> r = Derivation.Fold name)

                let fields =
                    requests
                    |> List.collect (fun r ->
                        match r with
                        | Derivation.Projections(n, fs) when n = name -> fs
                        | _ -> [])
                    |> List.distinct

                if folds && List.contains "fold" fields then
                    refuse
                        (sprintf "a projection of 'fold' over '%s' beside its fold" name)
                        "the fold and the projections share the union's module, so a field named `fold` would shadow it"
                        "drop one of the two requests"
                else
                    Ok(u, folds, fields))
        |> sequenceR

    // ---- default records (emission 7) ----

    /// Every admitted kind (`admits`) and record whose every member has a value without the caller,
    /// kinds first, in declaration order; refused when there is none.
    let defaultRecords (ctx: Decl) (admits: IdlKind -> bool) : Result<DefaultRecord list, CodegenError> =
        let defaultFor (tag: string) (field: string) =
            ctx.Idl.Defaults
            |> List.tryPick (fun d ->
                if d.Kind = tag && d.Field = field then
                    Some d.Value
                else
                    None)

        let record (name: string) (kind: IdlKind option) (declared: IdlField -> IdlValue option) (fs: IdlField list) =
            let members =
                fs
                |> List.map (fun f -> f, Decl.memberValue ValueSite.Constructor f.Opt (declared f))

            if members |> List.exists (fun (_, mv) -> mv = MemberValue.Parameter) then
                None
            else
                Some
                    { Name = name
                      Kind = kind
                      Members = members }

        let kinds =
            ctx.Kinds
            |> List.filter admits
            |> List.choose (fun k -> record (k.Tag + "Spec") (Some k) (fun f -> defaultFor k.Tag f.Name) k.Fields)

        let records =
            ctx.Records
            |> List.choose (fun r -> record r.Name None (fun _ -> None) r.Fields)

        match kinds @ records with
        | [] ->
            refuse
                "default records over a vocabulary none of whose kinds or records is fully defaulted"
                "a default record is a value only where every field has one without the caller"
                "declare defaults for the required fields, or drop the request"
        | xs -> Ok xs

    // ---- the vocabulary constants (emission 8) ----

    /// The kind tags of each declared category, the wire field names of each kind, of the node
    /// envelope and of each tree op.
    let constants (ctx: Decl) : Constants =
        let fieldsOf (ks: IdlKind list) =
            ks |> List.map (fun k -> k.Tag, k.Fields |> List.map _.Name)

        { Categories =
            ctx.Kinds
            |> List.map _.Category
            |> List.distinct
            |> List.map (fun c -> c, ctx.Kinds |> List.filter (fun k -> k.Category = c) |> List.map _.Tag)
          KindFields = fieldsOf ctx.Kinds
          Envelope = ctx.Idl.NodeFields |> List.map _.Name
          OpFields = fieldsOf ctx.Idl.Ops }
