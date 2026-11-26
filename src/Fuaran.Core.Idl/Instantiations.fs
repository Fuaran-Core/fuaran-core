namespace Fuaran.Core.Idl

open Fuaran.Core

/// Phase 732 — whether a set of generic declarations has FINITELY many instantiations, decided
/// from the declarations alone, and the instantiations themselves where it has.
///
/// A generic declaration is instantiated wherever a reference applies arguments to it, and an
/// instantiation's members name further instantiations at its own arguments. Most vocabularies
/// close: `Ranged<float>` names nothing new. One whose recursion GROWS an argument does not:
/// `G<T> { next: G<list<T>> }` at `G<int>` names `G<list<int>>`, which names `G<list<list<int>>>`,
/// without end. Counting how deep a walk has gone cannot tell the two apart: a declaration that
/// grows along two members names 2^n instantiations n levels down, and a long chain of ordinary
/// declarations is deep without growing at all.
///
/// THE TEST is the expanding-cycle test of ECMA-335 (6th edition, Partition II, section 9.2,
/// "Generics and recursive inheritance graphs"), which the CLI uses to refuse a type whose
/// instantiation closure is infinite. Its nodes are the type parameters of the generic
/// declarations. For each reference `D<a1..an>` a walk enters inside a declaration `E`, and each
/// parameter `p` of `E` that occurs in `ai`, there is an edge from `p` to `D`'s i-th parameter:
/// NON-EXPANDING where `ai` is exactly `p`, EXPANDING where `p` occurs properly inside `ai`
/// (`list<p>`, `Box<p>`, a tuple). The theorem (stated in the standard; proved as "finitary iff
/// not expansive" by M. Viroli, "On the recursive generation of parametric types", 2000, and
/// restated by A. Kennedy and B. Pierce, "On decidability of nominal subtyping with variance",
/// 2007, section 5): the instantiations reached are infinitely many exactly when a declaration
/// that is reached has a parameter on a cycle holding an expanding edge.
///
/// Why it holds here, in both directions. Reached instantiations are the least set holding the
/// references the roots enter and, with `E<s>`, the reference `D<a[s]>` for each reference `D<a>`
/// a walk enters in `E`. (=>) Follow the cycle once from a reached `E<s>`: each edge is a
/// reference its declaration enters, a non-expanding edge copies the argument and an expanding one
/// wraps it, so `E` is reached again at an argument strictly larger; repeat. (<=) Take the
/// parameters in the order of the graph's strongly connected components. With no expanding edge on
/// a cycle, every edge inside a component copies, and every other edge into it wraps arguments of
/// earlier components, which are finitely many by induction; the roots' own arguments are finitely
/// many. So each parameter takes finitely many values.
///
/// WHICH REFERENCES A WALK ENTERS is the walk's own ([[Reading]]), and the theorem is exact for
/// whichever it states. Two kinds are in use. A walk that names every instantiation WRITTEN enters a
/// reference wherever it stands, a reference inside another's argument included (the JSON
/// schema's `$defs`). A walk that follows what a VALUE holds enters a reference written as an
/// argument only where the member holding that parameter is itself followed: `Phantom<Opt<int>>`,
/// where `Phantom<T>` holds nothing at `T`, holds no value of `Opt<int>` (the wire reach, the
/// declaration check's default rule, the stability classifier). The second is the first over the
/// argument positions a value reaches, found as a least fixed point ([[Analysis]]'s `held`).
///
/// The cases this type language has that the cited rule does not name: a tuple, a set, a keyed
/// map and a list are type constructors like any other (an argument inside one is properly inside
/// it, so the edge expands); an INLINE record is a record; a hosted slot's declared wire form and
/// a host function's signature cannot name a parameter that a substitution replaces
/// ([[TypeParams.substitute]] leaves both as declared), so neither carries an edge, and a hosted
/// slot's wire form is followed as a closed type.
///
/// Generic over the type's representation, so the declarations of a vocabulary ([[ofIdl]]) and
/// the type shapes a stored snapshot carries are read by the one analysis.
[<RequireQualifiedAccess>]
module internal Instantiations =

    /// One type, as the analysis reads it.
    [<RequireQualifiedAccess>]
    type Shape<'T> =
        /// A type parameter of the declaration the type is written in.
        | Parameter of name: string
        /// A reference to a declaration, by its key in [[Reading.Declarations]], at its arguments.
        | Reference of declaration: string * args: 'T list
        /// Any other type the walk looks inside, with the types it holds (a list's item, a tuple's
        /// components).
        | Holds of parts: 'T list
        /// A type the walk does NOT look inside: it enters no reference written there, and a
        /// substitution still replaces the parameters its parts name.
        | Names of parts: 'T list

    /// One declaration: its parameters and the types of the members a walk follows.
    type Declaration<'T> =
        { Params: string list
          Members: 'T list }

    /// What one walk reads: the declarations, how a type is shaped, and which references it enters.
    type Reading<'T> =
        {
            Declarations: Map<string, Declaration<'T>>
            Shape: 'T -> Shape<'T>
            /// `t` with each parameter the map names replaced by its binding.
            Substitute: Map<string, 'T> -> 'T -> 'T
            /// `true` where a reference written as an ARGUMENT is entered wherever it stands; `false`
            /// where it is entered only if a value of the outer declaration holds one.
            Arguments: bool
            /// `t` as its HEAD (`t` with each type it holds replaced by one placeholder) and the types
            /// it holds, in order: two types with one head and the same parts are one type. What
            /// [[identity]] interns a type by.
            Split: 'T -> 'T * 'T list
        }

    /// The witness that a closure is infinite: the declaration at its own parameters reaches itself
    /// at [[Reaches]], where one argument holds a parameter properly inside it.
    type Growth<'T> =
        { Declaration: string
          Params: string list
          Reaches: 'T list }

    /// How many instantiations an enumeration reads before it stops and says so. No vocabulary
    /// written by hand comes near it; it is the answer to a FINITE closure built to be enormous (a
    /// chain of n declarations, each naming the next at two arguments, has 2^n instantiations).
    [<Literal>]
    let limit = 4096

    /// How many NODES the instantiations an entry prints may hold between them, each counted at
    /// its full printed size (`Box<tuple<int, int>>` is four). A closure of few instantiations can
    /// still be enormous to print: a chain of n declarations, each naming the next at
    /// `tuple<T, T>`, has n instantiations and the last holds 2^n leaves. An entry that prints or
    /// models every instantiation refuses past it, beside [[limit]]; a chain of twelve naming the
    /// next at two arguments (4095 instantiations, 49152 nodes) is under both.
    [<Literal>]
    let sizeLimit = 65536

    /// A sum of printed sizes, saturating: a size past the range is still more than every cap.
    let private add (a: int64) (b: int64) : int64 =
        if a > System.Int64.MaxValue - b then
            System.Int64.MaxValue
        else
            a + b

    /// Types as numbers: each type an analysis meets is interned once, by its head and the numbers
    /// of the types it holds, and an object met again is answered from a table of objects. A type
    /// written once and SHARED in memory (a substitution places one argument at each position that
    /// names its parameter) is therefore read once however often it is held, so equality, hashing
    /// and the printed size cost a constant per node of the shared graph, never per node of the
    /// type printed out.
    type private Interner<'T when 'T: equality and 'T: not struct>(split: 'T -> 'T * 'T list) =
        let objects = System.Collections.Generic.Dictionary<'T, int>(HashIdentity.Reference)

        let nodes =
            System.Collections.Generic.Dictionary<'T * int list, int>(HashIdentity.Structural)

        let sizes = ResizeArray<int64>()

        member this.Id(t: 'T) : int =
            match objects.TryGetValue t with
            | true, id -> id
            | false, _ ->
                let head, parts = split t
                let ids = parts |> List.map this.Id
                let key = head, ids

                let id =
                    match nodes.TryGetValue key with
                    | true, id -> id
                    | false, _ ->
                        let id = sizes.Count
                        nodes[key] <- id
                        sizes.Add((1L, ids) ||> List.fold (fun acc i -> add acc sizes[i]))
                        id

                objects[t] <- id
                id

        member _.Size(id: int) : int64 = sizes[id]

        member this.SizeOf(args: 'T list) : int64 =
            (1L, args) ||> List.fold (fun acc a -> add acc (this.Size(this.Id a)))

    type private Edge<'T> =
        {
            From: string * int
            To: string * int
            Expanding: bool
            /// The reference the edge was read from, as written in `From`'s declaration.
            Reference: string * 'T list
        }

    /// One reading, analysed once: the argument positions a value reaches, the references each
    /// declaration enters, and the parameter graph.
    type Analysis<'T when 'T: equality and 'T: not struct> =
        private
            { Reading: Reading<'T>
              Held: Set<string * int>
              Entered: Map<string, (string * 'T list) list>
              Edges: Map<string * int, Edge<'T> list>
              Interner: Interner<'T> }

    let private applied (reading: Reading<'T>) (declaration: string) (args: 'T list) : Declaration<'T> option =
        reading.Declarations
        |> Map.tryFind declaration
        |> Option.filter (fun d -> d.Params.Length = args.Length)

    /// Whether the parameter `p` occurs in `t` at a position a substitution replaces.
    let rec private mentions (reading: Reading<'T>) (p: string) (t: 'T) : bool =
        match reading.Shape t with
        | Shape.Parameter q -> q = p
        | Shape.Reference(_, parts)
        | Shape.Holds parts
        | Shape.Names parts -> parts |> List.exists (mentions reading p)

    /// The references a walk enters in `t`, outermost first: each one that applies as many
    /// arguments as its declaration has parameters. A reference no binding can be made for is
    /// entered nowhere, and what its arguments name is still read.
    let private references (reading: Reading<'T>) (held: Set<string * int>) (t: 'T) : (string * 'T list) list =
        let rec go (t: 'T) =
            match reading.Shape t with
            | Shape.Parameter _
            | Shape.Names _ -> []
            | Shape.Holds parts -> parts |> List.collect go
            | Shape.Reference(declaration, args) ->
                let inner =
                    args
                    |> List.indexed
                    |> List.collect (fun (i, a) ->
                        if reading.Arguments || held.Contains(declaration, i) then
                            go a
                        else
                            [])

                match applied reading declaration args with
                | Some _ -> (declaration, args) :: inner
                | None -> inner

        go t

    /// A reading from its parts: each declaration as its key, its parameters and the types of the
    /// members the walk follows. Where two share a key the first is read.
    let reading
        (declarations: (string * string list * 'T list) list)
        (shape: 'T -> Shape<'T>)
        (substitute: Map<string, 'T> -> 'T -> 'T)
        (split: 'T -> 'T * 'T list)
        (arguments: bool)
        : Reading<'T> =
        { Declarations =
            (declarations, Map.empty)
            ||> List.foldBack (fun (key, pars, members) acc -> acc.Add(key, { Params = pars; Members = members }))
          Shape = shape
          Substitute = substitute
          Arguments = arguments
          Split = split }

    /// The analysis of one reading.
    let analyse (reading: Reading<'T>) : Analysis<'T> =
        // The argument positions a value reaches: a parameter a member holds outside every
        // reference, or inside an argument position that is itself held. A least fixed point; with
        // [[Reading.Arguments]] every position is entered and the set is not asked.
        let held =
            if reading.Arguments then
                Set.empty
            else
                let rec holds (held: Set<string * int>) (p: string) (t: 'T) : bool =
                    match reading.Shape t with
                    | Shape.Parameter q -> q = p
                    | Shape.Names _ -> false
                    | Shape.Holds parts -> parts |> List.exists (holds held p)
                    | Shape.Reference(declaration, args) ->
                        args
                        |> List.indexed
                        |> List.exists (fun (i, a) -> held.Contains(declaration, i) && holds held p a)

                let step (held: Set<string * int>) =
                    (held, reading.Declarations)
                    ||> Map.fold (fun acc name d ->
                        (acc, List.indexed d.Params)
                        ||> List.fold (fun acc (i, p) ->
                            if d.Members |> List.exists (holds held p) then
                                acc.Add(name, i)
                            else
                                acc))

                let rec fix (held: Set<string * int>) =
                    let next = step held
                    if next = held then held else fix next

                fix Set.empty

        let entered =
            reading.Declarations
            |> Map.map (fun _ d -> d.Members |> List.collect (references reading held))

        let edges =
            [ for KeyValue(name, d) in reading.Declarations do
                  for declaration, args in entered[name] do
                      for i, a in List.indexed args do
                          for j, p in List.indexed d.Params do
                              if mentions reading p a then
                                  { From = name, j
                                    To = declaration, i
                                    Expanding =
                                      (match reading.Shape a with
                                       | Shape.Parameter _ -> false
                                       | Shape.Reference _
                                       | Shape.Holds _
                                       | Shape.Names _ -> true)
                                    Reference = declaration, args } ]
            |> List.groupBy _.From
            |> Map.ofList

        { Reading = reading
          Held = held
          Entered = entered
          Edges = edges
          Interner = Interner reading.Split }

    /// The references a walk enters in `t` ([[references]]).
    let entered (analysis: Analysis<'T>) (t: 'T) : (string * 'T list) list =
        references analysis.Reading analysis.Held t

    /// The type `t` as a number: two types have one number exactly where they are one type
    /// ([[Interner]]), at a cost per node of `t` as it is shared in memory.
    let identity (analysis: Analysis<'T>) (t: 'T) : int = analysis.Interner.Id t

    /// The printed size of the instantiation `declaration<args>`: its nodes, each type it holds
    /// counted at its full printed size, read off the shared graph.
    let size (analysis: Analysis<'T>) (_: string, args: 'T list) : int64 = analysis.Interner.SizeOf args

    /// The printed size of these instantiations together ([[size]]), saturating.
    let printedSize (analysis: Analysis<'T>) (instantiations: (string * 'T list) list) : int64 =
        (0L, instantiations) ||> List.fold (fun acc i -> add acc (size analysis i))

    /// The declarations whose instantiations can reach one of `declaration` through the members a
    /// walk follows, `declaration` itself included: the only ones a walk for its instantiations
    /// needs to enter, since an instantiation is reached only from a root or from an instantiation
    /// of a declaration that enters its declaration.
    let reaching (analysis: Analysis<'T>) (declaration: string) : Set<string> =
        let into =
            [ for KeyValue(name, found) in analysis.Entered do
                  for inner, _ in found do
                      inner, name ]
            |> List.groupBy fst
            |> List.map (fun (inner, pairs) -> inner, pairs |> List.map snd)
            |> Map.ofList

        let rec close (acc: Set<string>) (frontier: string list) =
            match frontier with
            | [] -> acc
            | d :: rest ->
                let fresh =
                    into
                    |> Map.tryFind d
                    |> Option.defaultValue []
                    |> List.filter (fun e -> not (acc.Contains e))
                    |> List.distinct

                close (Set.union acc (Set.ofList fresh)) (fresh @ rest)

        close (Set.singleton declaration) [ declaration ]

    /// The binding an instantiation makes: each parameter of its declaration to the argument at its
    /// position. None where the reference applies another number of arguments, or names nothing.
    let binding (analysis: Analysis<'T>) (declaration: string, args: 'T list) : Map<string, 'T> option =
        applied analysis.Reading declaration args
        |> Option.map (fun d -> Map.ofList (List.zip d.Params args))

    /// The references the instantiation `declaration<args>` enters: each one its declaration
    /// enters, at the instantiation's own arguments. None where no binding can be made.
    let successors (analysis: Analysis<'T>) (declaration: string, args: 'T list) : (string * 'T list) list =
        match binding analysis (declaration, args) with
        | None -> []
        | Some binding ->
            analysis.Entered[declaration]
            |> List.map (fun (inner, innerArgs) -> inner, innerArgs |> List.map (analysis.Reading.Substitute binding))

    /// The declarations the roots reach, each once, in first-reach order.
    let private declarationsReached (analysis: Analysis<'T>) (roots: 'T list) : string list =
        let seen = System.Collections.Generic.HashSet<string>()
        let order = ResizeArray<string>()
        let queue = System.Collections.Generic.Queue<string>()

        let offer (found: (string * 'T list) list) =
            for declaration, _ in found do
                if seen.Add declaration then
                    order.Add declaration
                    queue.Enqueue declaration

        roots |> List.iter (entered analysis >> offer)

        while queue.Count > 0 do
            offer analysis.Entered[queue.Dequeue()]

        List.ofSeq order

    /// The edges of a shortest path from `start` to another node `target` in the parameter graph, if
    /// there is one.
    let private path (analysis: Analysis<'T>) (start: string * int) (target: string * int) : Edge<'T> list option =
        let from =
            System.Collections.Generic.Dictionary<string * int, Edge<'T>>(HashIdentity.Structural)

        let seen = System.Collections.Generic.HashSet<string * int>(HashIdentity.Structural)
        let queue = System.Collections.Generic.Queue<string * int>()
        seen.Add start |> ignore
        queue.Enqueue start

        while queue.Count > 0 && not (from.ContainsKey target) do
            let node = queue.Dequeue()

            for edge in analysis.Edges |> Map.tryFind node |> Option.defaultValue [] do
                if seen.Add edge.To then
                    from[edge.To] <- edge
                    queue.Enqueue edge.To

        if from.ContainsKey target then
            let rec back (node: string * int) (acc: Edge<'T> list) =
                let edge = from[node]
                let acc = edge :: acc
                if edge.From = start then acc else back edge.From acc

            Some(back target [])
        else
            None

    /// Whether the instantiations the roots reach are infinitely many, and the witness where they
    /// are: a reached declaration with a parameter on a cycle that holds an expanding edge (the
    /// first one found, taking the declarations in first-reach order), and what following that
    /// cycle once reaches it at. Every expanding edge of a reached declaration is tried as the
    /// cycle's first edge, so no such cycle is missed. Only the declarations `keep` answers are
    /// asked; a cycle through one of [[reaching]]'s declarations passes only through others.
    let growth (analysis: Analysis<'T>) (keep: string -> bool) (roots: 'T list) : Growth<'T> option =
        let cycle (declaration: string) : Edge<'T> list option =
            let own =
                analysis.Reading.Declarations[declaration].Params
                |> List.mapi (fun j _ -> analysis.Edges |> Map.tryFind (declaration, j) |> Option.defaultValue [])
                |> List.concat

            own
            |> List.tryPick (fun first ->
                // A cycle through `first` that holds an expanding edge: `first` itself where it
                // expands, or any expanding edge the cycle through it passes.
                let closing =
                    if first.To = first.From then
                        Some []
                    else
                        path analysis first.To first.From

                closing
                |> Option.map (fun rest -> first :: rest)
                |> Option.filter (List.exists _.Expanding))

        declarationsReached analysis roots
        |> List.filter keep
        |> List.tryPick (fun declaration ->
            cycle declaration
            |> Option.map (fun edges ->
                // Follow the cycle once from the declaration at its own parameters.
                let reaches, _ =
                    (([], None), edges)
                    ||> List.fold (fun (_, binding: Map<string, 'T> option) edge ->
                        let inner, args = edge.Reference

                        let at =
                            match binding with
                            | Some b -> args |> List.map (analysis.Reading.Substitute b)
                            | None -> args

                        at, Some(Map.ofList (List.zip analysis.Reading.Declarations[inner].Params at)))

                { Declaration = declaration
                  Params = analysis.Reading.Declarations[declaration].Params
                  Reaches = reaches }))

    /// The instantiations a set of roots reaches.
    [<RequireQualifiedAccess>]
    type Closure<'T> =
        /// Every one, each once, in breadth-first order of first reach, and the printed size of
        /// the GENERIC ones together ([[size]]).
        | Finite of instantiations: (string * 'T list) list * size: int64
        /// Infinitely many ([[growth]]).
        | Infinite of Growth<'T>
        /// Finitely many, and more than [[limit]].
        | Exceeds

    /// Every instantiation the roots reach of the declarations `keep` answers: a worklist and a
    /// visited set, with no bound on depth. An instantiation is keyed by its declaration and the
    /// [[identity]] of each argument, so a step costs what the declarations' own types cost
    /// however large the arguments it substitutes have grown.
    let closureWithin (analysis: Analysis<'T>) (keep: string -> bool) (roots: 'T list) : Closure<'T> =
        match growth analysis keep roots with
        | Some g -> Closure.Infinite g
        | None ->
            let seen =
                System.Collections.Generic.HashSet<string * int list>(HashIdentity.Structural)

            let order = ResizeArray<string * 'T list>()
            let queue = System.Collections.Generic.Queue<string * 'T list>()
            // Counted: the instantiations of GENERIC declarations. A declaration applied to no
            // argument is one entry however it is reached.
            let mutable generic = 0
            let mutable printed = 0L

            let offer (found: (string * 'T list) list) =
                for (declaration, args) as instantiation in found do
                    if
                        generic <= limit
                        && keep declaration
                        && seen.Add((declaration, args |> List.map (identity analysis)))
                    then
                        if not (List.isEmpty args) then
                            generic <- generic + 1
                            printed <- add printed (size analysis instantiation)

                        order.Add instantiation
                        queue.Enqueue instantiation

            roots |> List.iter (entered analysis >> offer)

            while queue.Count > 0 && generic <= limit do
                offer (successors analysis (queue.Dequeue()))

            if generic > limit then
                Closure.Exceeds
            else
                Closure.Finite(List.ofSeq order, printed)

    /// Every instantiation the roots reach ([[closureWithin]], every declaration kept).
    let closure (analysis: Analysis<'T>) (roots: 'T list) : Closure<'T> =
        closureWithin analysis (fun _ -> true) roots

    // -----------------------------------------------------------------------
    // The reading of a vocabulary's own declarations.
    // -----------------------------------------------------------------------

    /// A type as a fault spells an instantiation: the artifact's tags, a declaration by its name
    /// and its arguments (`Opt<int>`, `Pair<str, list<bool>>`). Every case is named.
    let rec spell (t: IdlType) : string =
        let applied (name: string) (args: IdlType list) =
            match args with
            | [] -> name
            | _ -> sprintf "%s<%s>" name (args |> List.map spell |> String.concat ", ")

        match t with
        | TStr -> "str"
        | TInt -> "int"
        | TBool -> "bool"
        | TFloat -> "float"
        | TInt64 -> "int64"
        | TDecimal -> "decimal"
        | TBytes -> "bytes"
        | TTimeOfDay -> "timeOfDay"
        | TDuration -> "duration"
        | TNode -> "node"
        | TKind -> "kind"
        | TOp -> "op"
        | TJson -> "json"
        | TClosure -> "closure"
        | TOpaque -> "opaque"
        | TFn _ -> "fn"
        | THosted _ -> "hosted"
        | TEnum n -> n
        | TVar v -> "'" + v
        | TRecord(n, args)
        | TUnion(n, args) -> applied n args
        | TList inner -> applied "list" [ inner ]
        | TMap inner -> applied "map" [ TStr; inner ]
        | TSet inner -> applied "set" [ inner ]
        // A string-keyed map is `map<str, v>` and a keyed map `keyedMap<k, v>`: two types, two
        // spellings, whatever the key.
        | TKeyedMap(k, v) -> applied "keyedMap" [ k; v ]
        | TTuple components -> applied "tuple" components

    /// A record's key in a vocabulary's reading ([[ofIdl]]).
    let recordKey (name: string) = "record " + name

    /// A union's key in a vocabulary's reading ([[ofIdl]]).
    let unionKey (name: string) = "union " + name

    /// What one walk over a vocabulary follows.
    type Walk =
        {
            /// The members of a declaration the walk follows (all of them, or those on the wire).
            Members: IdlField list -> IdlField list
            /// [[Reading.Arguments]].
            Arguments: bool
            /// Whether the walk looks inside this type. One it does not is read for the parameters
            /// it names and no further ([[Shape.Names]]).
            Follows: IdlType -> bool
        }

    /// What a VALUE on the wire holds: the wire members, a hosted slot's declared wire form
    /// included, and a reference written as an argument only where a member holds it. The wire
    /// reach's walk and the declaration check's.
    let wire: Walk =
        { Members = List.filter (fun f -> f.Opt <> HostOnly)
          Arguments = false
          Follows = fun _ -> true }

    /// Every instantiation WRITTEN in a wire member, a reference inside an argument and one inside
    /// a hosted slot's declared wire form included. The walk that names one definition per
    /// instantiation.
    let written: Walk =
        { Members = List.filter (fun f -> f.Opt <> HostOnly)
          Arguments = true
          Follows = fun _ -> true }

    /// What a fold descends: a list, a map and a record, through every member of the record. A
    /// union is where a fold stops or is refused, so it is not looked inside.
    let folded: Walk =
        { Members = id
          Arguments = false
          Follows =
            fun t ->
                match t with
                | TList _
                | TMap _
                | TRecord _ -> true
                | _ -> false }

    /// How a walk reads an [[IdlType]]. Every case is named. A host function's signature names no
    /// parameter a substitution replaces, and neither does a hosted slot's wire form.
    let shape (walk: Walk) (t: IdlType) : Shape<IdlType> =
        let holds (parts: IdlType list) =
            if walk.Follows t then
                Shape.Holds parts
            else
                Shape.Names parts

        match t with
        | TVar v -> Shape.Parameter v
        | TRecord(n, args) ->
            if walk.Follows t then
                Shape.Reference(recordKey n, args)
            else
                Shape.Names args
        | TUnion(n, args) ->
            if walk.Follows t then
                Shape.Reference(unionKey n, args)
            else
                Shape.Names args
        | TList inner
        | TMap inner
        | TSet inner -> holds [ inner ]
        | TTuple components -> holds components
        | TKeyedMap(k, v) -> holds [ k; v ]
        | THosted h ->
            if walk.Follows t then
                Shape.Holds(Option.toList h.Wire)
            else
                Shape.Holds []
        | TStr
        | TInt
        | TBool
        | TFloat
        | TEnum _
        | TNode
        | TClosure
        | TFn _
        | TOpaque
        | TJson
        | TKind
        | TOp
        | TInt64
        | TDecimal
        | TBytes
        | TTimeOfDay
        | TDuration -> Shape.Holds []

    /// An [[IdlType]] as its head and the types it holds ([[Reading.Split]]): a declaration's
    /// arguments, a collection's items, a tuple's components, a hosted slot's declared wire form.
    /// A host function's signature is held by nothing a substitution reaches, so it is all head.
    /// Every case is named.
    let split (t: IdlType) : IdlType * IdlType list =
        let hole = TVar ""
        let holes (parts: IdlType list) = parts |> List.map (fun _ -> hole)

        match t with
        | TRecord(n, args) -> TRecord(n, holes args), args
        | TUnion(n, args) -> TUnion(n, holes args), args
        | TList inner -> TList hole, [ inner ]
        | TMap inner -> TMap hole, [ inner ]
        | TSet inner -> TSet hole, [ inner ]
        | TTuple components -> TTuple(holes components), components
        | TKeyedMap(k, v) -> TKeyedMap(hole, hole), [ k; v ]
        | THosted h ->
            THosted
                { h with
                    Wire = h.Wire |> Option.map (fun _ -> hole) },
            Option.toList h.Wire
        | TStr
        | TInt
        | TBool
        | TFloat
        | TEnum _
        | TVar _
        | TNode
        | TClosure
        | TFn _
        | TOpaque
        | TJson
        | TKind
        | TOp
        | TInt64
        | TDecimal
        | TBytes
        | TTimeOfDay
        | TDuration -> t, []

    /// A vocabulary's records and unions as one walk reads them. Where two declarations share a
    /// name the first is read, as [[IdlLookup]] answers it.
    let ofIdl (walk: Walk) (idl: Idl) : Reading<IdlType> =
        let types (fields: IdlField list) = walk.Members fields |> List.map _.Type

        reading
            [ for u in idl.Unions do
                  unionKey u.Name, u.Params, u.Cases |> List.collect (fun c -> types c.Fields)
              for r in idl.Records do
                  recordKey r.Name, r.Params, types r.Fields ]
            (shape walk)
            TypeParams.substitute
            split
            walk.Arguments

    /// A declaration's key as a reference names it: its family and its name.
    let named (declaration: string) : string * string =
        match declaration.IndexOf " " with
        | -1 -> "", declaration
        | i -> declaration.Substring(0, i), declaration.Substring(i + 1)

    /// The type an instantiation is: the reference to its declaration at its arguments.
    let applies (declaration: string, args: IdlType list) : IdlType =
        match named declaration with
        | "union", name -> TUnion(name, args)
        | _, name -> TRecord(name, args)

    /// THE ONE SENTENCE every entry that must name each instantiation refuses a growing declaration
    /// in: the declaration, and what it reaches itself at (``G<'T> reaches G<list<'T>>``).
    let describe (g: Growth<IdlType>) : string =
        let family, name = named g.Declaration

        sprintf
            "the generic %s '%s', which has no finite set of instantiations: %s reaches %s"
            family
            name
            (spell (applies (g.Declaration, g.Params |> List.map TVar)))
            (spell (applies (g.Declaration, g.Reaches)))

    /// What an enumeration that stopped at [[limit]] says of the vocabulary.
    let exceeds: string =
        sprintf "more than %d instantiations of the vocabulary's generic declarations" limit

    /// A declaration's key as a sentence names it: `record 'Opt'`, `union 'Pick'`.
    let spelledKey (declaration: string) : string =
        let family, name = named declaration
        sprintf "%s '%s'" family name

    /// What a walk for the instantiations of ONE declaration says when it stopped at [[limit]]: it
    /// counts that declaration's and those of the declarations that reach it ([[reaching]]).
    let exceedsReaching (declaration: string) : string =
        sprintf
            "more than %d instantiations of %s and the generic declarations that reach it"
            limit
            (spelledKey declaration)

    /// What a walk for the instantiations of ONE declaration says when they hold more than
    /// [[sizeLimit]] nodes between them.
    let oversizedAt (declaration: string) : string =
        sprintf "more than %d nodes across the instantiations of %s" sizeLimit (spelledKey declaration)

    /// What an entry that prints every instantiation says of a vocabulary whose instantiations
    /// hold more than [[sizeLimit]] nodes between them.
    let oversized: string =
        sprintf "more than %d nodes across the instantiations of the vocabulary's generic declarations" sizeLimit

    /// Every instantiation a walk over `idl` reaches from `roots`, each as the reference it is,
    /// or why an entry that prints each one cannot: they are infinitely many ([[describe]]), more
    /// than [[limit]] ([[exceeds]]), or more than [[sizeLimit]] nodes to print ([[oversized]]).
    let reached (walk: Walk) (idl: Idl) (roots: IdlType list) : Result<IdlType list, string> =
        match closure (analyse (ofIdl walk idl)) roots with
        | Closure.Finite(_, printed) when printed > int64 sizeLimit -> Error oversized
        | Closure.Finite(all, _) -> Ok(all |> List.map applies)
        | Closure.Infinite g -> Error(describe g)
        | Closure.Exceeds -> Error exceeds

    /// Why an entry that prints every instantiation a walk over `idl` reaches from `roots` cannot
    /// ([[reached]]); `None` where it can.
    let unread (walk: Walk) (idl: Idl) (roots: IdlType list) : string option =
        match reached walk idl roots with
        | Ok _ -> None
        | Error why -> Some why
