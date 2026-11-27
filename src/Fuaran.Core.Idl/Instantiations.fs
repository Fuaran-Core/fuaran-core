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

    /// How many types the declaration check finds at ONE type parameter of a generic declaration
    /// before it stops finding each and judges a member that reads that parameter in classes
    /// instead. It counts that parameter's own types and no other's, so no member's verdict depends
    /// on how many types another parameter takes. Where the classes at the depth a member is decided
    /// at are more than this too, the member is refused, never admitted unjudged. More types (or
    /// classes) at a parameter than this is more instantiations of its declaration than this.
    [<Literal>]
    let parameterLimit = 16384

    /// How many instantiations the declaration check reads in the walks that find each one, across
    /// one check, and again how many in the walks in classes. A safety net: the walks read at most
    /// one instantiation per type new at a parameter (and the references it enters), which
    /// [[parameterLimit]] bounds. Where the first walk stops at it, a member whose declaration it
    /// had read whole is judged, and the rest in classes; where the walks in classes stop at it, a
    /// member whose declaration they had not read whole is refused, never admitted unjudged.
    [<Literal>]
    let stepLimit = 1048576

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
    /// declaration enters, the parameter graph, and what every question below asks of the whole
    /// vocabulary, each computed once however many questions are asked.
    type Analysis<'T when 'T: equality and 'T: not struct> =
        private
            {
                Reading: Reading<'T>
                Held: Set<string * int>
                Entered: Map<string, (string * 'T list) list>
                Edges: Map<string * int, Edge<'T> list>
                /// Each node of the parameter graph, and the nodes with an edge to it.
                Back: Lazy<Map<string * int, (string * int) list>>
                /// Each declaration, and the declarations that enter it.
                Into: Map<string, string list>
                /// The declarations with a parameter on a cycle of the parameter graph that holds an
                /// expanding edge ([[expansive]]).
                Expansive: Lazy<Set<string>>
                Interner: Interner<'T>
            }

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

    /// The nodes of a graph that lie on a cycle holding one of the edges `marked` answers, by the
    /// graph's strongly connected components (R. Tarjan, "Depth-first search and linear graph
    /// algorithms", 1972), in time linear in the nodes and edges. A node lies on such a cycle exactly
    /// where its component holds a marked edge between two of its own nodes: in a strongly connected
    /// component every node and every edge inside it lie on one closed walk. Iterative, so a long
    /// chain of declarations does not exhaust the stack.
    let private onMarkedCycle
        (successors: 'N -> 'E list)
        (target: 'E -> 'N)
        (marked: 'E -> bool)
        (nodes: 'N list)
        : Set<'N> when 'N: comparison =
        let index = System.Collections.Generic.Dictionary<'N, int>()
        let low = System.Collections.Generic.Dictionary<'N, int>()
        let componentOf = System.Collections.Generic.Dictionary<'N, int>()
        let onStack = System.Collections.Generic.HashSet<'N>()
        let stack = System.Collections.Generic.Stack<'N>()
        let mutable next = 0
        let mutable components = 0

        for root in nodes do
            if not (index.ContainsKey root) then
                // Each frame: a node and the out-edges it has still to visit.
                let frames = System.Collections.Generic.Stack<'N * 'E list>()

                let enter (n: 'N) =
                    index[n] <- next
                    low[n] <- next
                    next <- next + 1
                    stack.Push n
                    onStack.Add n |> ignore
                    frames.Push(n, successors n)

                enter root

                while frames.Count > 0 do
                    match frames.Pop() with
                    | n, e :: rest ->
                        frames.Push(n, rest)
                        let m = target e

                        if not (index.ContainsKey m) then
                            enter m
                        elif onStack.Contains m then
                            low[n] <- min low[n] index[m]
                    | n, [] ->
                        if low[n] = index[n] then
                            let mutable closing = true

                            while closing do
                                let m = stack.Pop()
                                onStack.Remove m |> ignore
                                componentOf[m] <- components
                                closing <- m <> n

                            components <- components + 1

                        if frames.Count > 0 then
                            let parent, _ = frames.Peek()
                            low[parent] <- min low[parent] low[n]

        let expanding =
            [ for n in nodes do
                  for e in successors n do
                      if marked e && componentOf[n] = componentOf[target e] then
                          componentOf[n] ]
            |> Set.ofList

        nodes |> List.filter (fun n -> expanding.Contains componentOf[n]) |> Set.ofList

    /// The analysis of one reading.
    let analyse (reading: Reading<'T>) : Analysis<'T> =
        // The argument positions a value reaches: a parameter a member holds outside every
        // reference, or inside an argument position that is itself held. The least set closed
        // under that rule, found as Horn clauses are (W. Dowling and J. Gallier, "Linear-time
        // algorithms for testing the satisfiability of propositional Horn formulae", 1984): each
        // place a member names a parameter is one clause, "the parameter's position is held if every
        // argument position on the way to it is", and each position is settled once, so the cost is
        // linear in the size of the declared member types. With [[Reading.Arguments]] every position
        // is entered and the set is not asked.
        let held =
            if reading.Arguments then
                Set.empty
            else
                // Each parameter a type names, with the argument positions on the way to it; none
                // inside a type the walk does not look into.
                let rec occurrences (path: (string * int) list) (t: 'T) : (string * (string * int) list) list =
                    match reading.Shape t with
                    | Shape.Parameter q -> [ q, path ]
                    | Shape.Names _ -> []
                    | Shape.Holds parts -> parts |> List.collect (occurrences path)
                    | Shape.Reference(declaration, args) ->
                        args
                        |> List.indexed
                        |> List.collect (fun (i, a) -> occurrences ((declaration, i) :: path) a)

                let clauses =
                    [| for KeyValue(name, d) in reading.Declarations do
                           let index = d.Params |> List.indexed |> List.map (fun (i, p) -> p, i) |> Map.ofList

                           for m in d.Members do
                               for q, path in occurrences [] m do
                                   match Map.tryFind q index with
                                   | Some i -> (name, i), List.distinct path
                                   | None -> () |]

                let waiting = Array.map (snd >> List.length) clauses

                let watching =
                    clauses
                    |> Array.indexed
                    |> Array.collect (fun (c, (_, path)) -> path |> List.map (fun atom -> atom, c) |> Array.ofList)
                    |> Array.groupBy fst
                    |> Array.map (fun (atom, cs) -> atom, cs |> Array.map snd)
                    |> dict

                let settled = System.Collections.Generic.HashSet<string * int>()
                let queue = System.Collections.Generic.Queue<string * int>()

                let settle (atom: string * int) =
                    if settled.Add atom then
                        queue.Enqueue atom

                clauses
                |> Array.iteri (fun c (head, _) ->
                    if waiting[c] = 0 then
                        settle head)

                while queue.Count > 0 do
                    match watching.TryGetValue(queue.Dequeue()) with
                    | true, cs ->
                        for c in cs do
                            waiting[c] <- waiting[c] - 1

                            if waiting[c] = 0 then
                                settle (fst clauses[c])
                    | false, _ -> ()

                Set.ofSeq settled

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

        let into =
            [ for KeyValue(name, found) in entered do
                  for inner, _ in found do
                      inner, name ]
            |> List.distinct
            |> List.groupBy fst
            |> List.map (fun (inner, pairs) -> inner, pairs |> List.map snd)
            |> Map.ofList

        // A declaration whose parameter lies on a cycle holding an expanding edge: the graph's
        // components, read once.
        let expansive =
            lazy
                (let successors (node: string * int) =
                    edges |> Map.tryFind node |> Option.defaultValue []

                 let nodes =
                     [ for KeyValue(from, out) in edges do
                           from

                           for e in out do
                               e.To ]
                     |> List.distinct

                 onMarkedCycle successors _.To _.Expanding nodes |> Set.map fst)

        { Reading = reading
          Held = held
          Entered = entered
          Edges = edges
          Back =
            lazy
                ([ for KeyValue(_, out) in edges do
                       for e in out do
                           e.To, e.From ]
                 |> List.distinct
                 |> List.groupBy fst
                 |> List.map (fun (target, pairs) -> target, pairs |> List.map snd)
                 |> Map.ofList)
          Into = into
          Expansive = expansive
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

    /// The type parameters `declaration` declares, in order; none where it is not declared.
    let parameters (analysis: Analysis<'T>) (declaration: string) : string list =
        analysis.Reading.Declarations
        |> Map.tryFind declaration
        |> Option.map _.Params
        |> Option.defaultValue []

    /// The positions of `declaration`'s parameters that `t` names where a substitution replaces
    /// them: the only arguments an answer about `t` at an instantiation of `declaration` can read.
    let positionsNamed (analysis: Analysis<'T>) (declaration: string) (t: 'T) : int list =
        parameters analysis declaration
        |> List.indexed
        |> List.filter (fun (_, p) -> mentions analysis.Reading p t)
        |> List.map fst

    /// The declarations whose instantiations can reach one of the `declarations` through the
    /// members a walk follows, those declarations themselves included: the only ones a walk for
    /// their instantiations needs to enter, since an instantiation is reached only from a root or
    /// from an instantiation of a declaration that enters its declaration. Linear in the edges of
    /// the declarations it returns.
    let reachingAny (analysis: Analysis<'T>) (declarations: string list) : Set<string> =
        let found = System.Collections.Generic.HashSet<string>(declarations)
        let frontier = System.Collections.Generic.Queue<string>(declarations)

        while frontier.Count > 0 do
            for e in analysis.Into |> Map.tryFind (frontier.Dequeue()) |> Option.defaultValue [] do
                if found.Add e then
                    frontier.Enqueue e

        Set.ofSeq found

    /// The declarations the `declarations` enter, directly or through others, those declarations
    /// included: every one an instantiation of theirs can reach. Linear in the edges it reads.
    let entering (analysis: Analysis<'T>) (declarations: string seq) : Set<string> =
        let found = System.Collections.Generic.HashSet<string>(declarations)
        let frontier = System.Collections.Generic.Queue<string>(found)

        while frontier.Count > 0 do
            for inner, _ in analysis.Entered |> Map.tryFind (frontier.Dequeue()) |> Option.defaultValue [] do
                if found.Add inner then
                    frontier.Enqueue inner

        Set.ofSeq found

    /// The nodes of the parameter graph `next` reaches from `positions`, those included. Linear in
    /// the edges it reads.
    let private closed
        (next: string * int -> (string * int) list)
        (positions: (string * int) seq)
        : System.Collections.Generic.HashSet<string * int> =
        let found =
            System.Collections.Generic.HashSet<string * int>(positions, HashIdentity.Structural)

        let frontier = System.Collections.Generic.Queue<string * int>(found)

        while frontier.Count > 0 do
            for node in next (frontier.Dequeue()) do
                if found.Add node then
                    frontier.Enqueue node

        found

    /// The argument positions whose argument can become part of an argument at one of
    /// `positions`: each of them, and each position of a declaration whose parameter is written in
    /// an argument at such a position (the parameter graph read backwards). An instantiation's
    /// arguments at the other positions decide nothing about which arguments are reached at
    /// `positions`: a substitution places an argument only where its parameter is written.
    let relevant
        (analysis: Analysis<'T>)
        (positions: (string * int) seq)
        : System.Collections.Generic.HashSet<string * int> =
        let back = analysis.Back.Force()
        closed (fun node -> back |> Map.tryFind node |> Option.defaultValue []) positions

    /// The argument positions an argument at one of `positions` can become part of: each of them,
    /// and each position an edge of the parameter graph leads to from one (the graph read
    /// forwards). Each instantiation of a declaration enters the references the declaration
    /// enters, and a substitution places different arguments where it places its parameter
    /// differently, so a position reached so is given at least as many arguments as the one it is
    /// reached from.
    let downstream
        (analysis: Analysis<'T>)
        (positions: (string * int) seq)
        : System.Collections.Generic.HashSet<string * int> =
        closed (fun node -> analysis.Edges |> Map.tryFind node |> Option.defaultValue [] |> List.map _.To) positions

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
    /// cycle's first edge, so no such cycle is missed.
    ///
    /// Whether there is such a cycle is read off the parameter graph's components, found once per
    /// analysis ([[Analysis.Expansive]]); only the declarations on one are searched for the
    /// witness, and only where there is one.
    let growth (analysis: Analysis<'T>) (roots: 'T list) : Growth<'T> option =
        let expansive = analysis.Expansive.Force()

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
        |> List.filter expansive.Contains
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

    /// The declarations whose walk [[growth]] finds infinite: each one that a reached declaration on
    /// an expanding cycle enters, directly or through others, those declarations included. Every
    /// declaration that reaches another declaration is another such ([[reachingAny]]), so a walk
    /// over the declarations reaching one that is not, keeping only those, is finite. Found once,
    /// in time linear in the declarations and the references they enter.
    let unbounded (analysis: Analysis<'T>) (roots: 'T list) : Set<string> =
        let expansive = analysis.Expansive.Force()

        declarationsReached analysis roots
        |> List.filter expansive.Contains
        |> entering analysis

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

    /// Every instantiation the roots reach: a worklist and a visited set, with no bound on depth.
    /// An instantiation is keyed by its declaration and the [[identity]] of each argument, so a step costs what the declarations' own types cost
    /// however large the arguments it substitutes have grown.
    let closure (analysis: Analysis<'T>) (roots: 'T list) : Closure<'T> =
        match growth analysis roots with
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

    // -----------------------------------------------------------------------
    // The reading of a vocabulary's own declarations.
    // -----------------------------------------------------------------------

    /// A type spelled by its tags, each declaration and collection by `applied` over its arguments.
    let private spelledAs (applied: string -> IdlType list -> string) (t: IdlType) : string =
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

    /// How many nodes of an instantiation a REFUSAL spells ([[spellAbbreviated]]): what is past them
    /// is written `...`. It bounds the text of a fault, never whether one is found.
    [<Literal>]
    let spelledLimit = 256

    /// `t` spelled to its first `nodes` nodes, read depth first, the rest of each list of
    /// arguments it stops in written `...` once.
    let private spellUpTo (nodes: int) (t: IdlType) : string =
        let mutable left = nodes

        let rec go (t: IdlType) : string =
            left <- left - 1

            let applied (name: string) (args: IdlType list) =
                match args with
                | [] -> name
                | _ ->
                    let rec parts (acc: string list) (rest: IdlType list) =
                        match rest with
                        | [] -> List.rev acc
                        | _ when left <= 0 -> List.rev ("..." :: acc)
                        | a :: tail -> parts (go a :: acc) tail

                    sprintf "%s<%s>" name (parts [] args |> String.concat ", ")

            spelledAs applied t

        go t

    /// A type as a fault spells an instantiation: the artifact's tags, a declaration by its name
    /// and its arguments (`Opt<int>`, `Pair<str, list<bool>>`). Every case is named.
    let spell (t: IdlType) : string = spellUpTo System.Int32.MaxValue t

    /// [[spell]] to the first [[spelledLimit]] nodes: what a refusal names an instantiation by,
    /// so that its text is bounded however large the instantiation is. An instantiation of no
    /// more nodes is spelled in full.
    let spellAbbreviated (t: IdlType) : string = spellUpTo spelledLimit t

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

    /// Whether `t` holds at most `nodes` nodes, each type it holds counted at its full printed size
    /// ([[split]]), read only as far as the answer needs.
    let holdsAtMost (nodes: int) (t: IdlType) : bool =
        let mutable left = nodes

        let rec go (t: IdlType) =
            left <- left - 1

            for part in snd (split t) do
                if left >= 0 then
                    go part

        go t
        left >= 0

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

    /// What the declaration check says of a member that reads a parameter of its declaration given
    /// more than [[parameterLimit]] classes at the depth the walk in classes reached: the
    /// declaration, the member, the number. It is true of the instantiations, since each class at
    /// the parameter is carried by another instantiation.
    let tooManyToJudge (declaration: string) (memberName: string) (what: string) : string =
        sprintf
            "more than %d instantiations of %s reach %s; %s cannot be checked at each"
            parameterLimit
            (spelledKey declaration)
            memberName
            what

    /// What the declaration check says of a member whose declaration the walks in classes had not
    /// read whole when they had read [[stepLimit]] instantiations, an instantiation left unread
    /// leading to it: the member, the number, the declaration.
    let tooLongToJudge (declaration: string) (memberName: string) (what: string) : string =
        sprintf
            "%s: the declaration check reads its budget of %d instantiations in classes and stops before it has read every one that leads to %s; %s cannot be checked at each"
            memberName
            stepLimit
            (spelledKey declaration)
            what

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

    // -----------------------------------------------------------------------
    // The walk the declaration check's two instantiation rules share (Phase 732).
    // -----------------------------------------------------------------------

    /// What a rule answers of one member at one binding of its declaration's parameters.
    [<RequireQualifiedAccess>]
    type Verdict =
        | Fits
        /// It does not, and why, in the rule's words.
        | Fails of why: string
        /// A parameter is bound to a type cut short, and the answer reads what was cut.
        | Undecided

    /// One member a rule judges at the instantiations of its declaration.
    type Judged =
        {
            /// The declaration, by its key in the reading.
            Declaration: string
            /// The positions of the declaration's parameters the answer reads.
            Reads: int list
            /// The answer at a binding of the declaration's parameters, the flag saying whether an
            /// argument it reads was cut short ([[Verdict.Undecided]] is answered only then).
            Judge: bool -> Map<string, IdlType> -> Verdict
        }

    /// What became of one member.
    [<RequireQualifiedAccess>]
    type Outcome =
        /// It fits every instantiation the walk reached.
        | Admitted
        /// It does not fit the first instantiation named, for the reason given, nor `more` others:
        /// every other it does not fit where `every` (each instantiation was found), else those
        /// found, a lower bound.
        | Refused of instantiation: IdlType * why: string * more: int * every: bool
        /// A parameter it reads takes more than [[parameterLimit]] types (in classes, classes).
        | TooMany
        /// The walk read [[stepLimit]] instantiations with one unread that leads to its declaration.
        | TooLong
        /// Its classes were cut too short to answer: judged again a level deeper.
        | Deeper

    /// `t` with every type held `k` levels down replaced by an unbound parameter (no declared
    /// parameter is named by the empty string). A type that holds none is kept.
    let rec private cutAt (k: int) (t: IdlType) : IdlType =
        let below = cutAt (k - 1)

        match t with
        | TList _
        | TMap _
        | TSet _
        | TTuple _
        | TKeyedMap _
        | TRecord(_, _ :: _)
        | TUnion(_, _ :: _) when k = 0 -> TVar ""
        | TList inner -> TList(below inner)
        | TMap inner -> TMap(below inner)
        | TSet inner -> TSet(below inner)
        | TTuple components -> TTuple(List.map below components)
        | TKeyedMap(key, value) -> TKeyedMap(below key, below value)
        | TRecord(n, args) -> TRecord(n, List.map below args)
        | TUnion(n, args) -> TUnion(n, List.map below args)
        | TStr
        | TInt
        | TBool
        | TFloat
        | TEnum _
        | TVar _
        | TNode
        | TKind
        | TOp
        | TClosure
        | TOpaque
        | TFn _
        | TJson
        | THosted _
        | TInt64
        | TDecimal
        | TBytes
        | TTimeOfDay
        | TDuration -> t

    /// The member `m` over the instantiations `found` of its declaration, judged in groups whose
    /// arguments at the positions it reads agree to a depth, from `depth`. A group the cut leaves
    /// undecided is split a level deeper where every instantiation was found (`exact`); in classes
    /// only one of each class at the walk's depth was, so the member is walked for again. A
    /// refusal decides: it names an instantiation the vocabulary reaches, and in classes a group
    /// left undecided beside it is not counted among those that fail. `every` says the
    /// instantiations found are every one reached, so the count of those it fails is exact.
    let private judgedOver
        (analysis: Analysis<IdlType>)
        (m: Judged)
        (exact: bool)
        (every: bool)
        (depth: int)
        (found: IdlType array list)
        : Outcome =
        let identity = identity analysis
        let pars = parameters analysis m.Declaration

        let binding (args: IdlType array) =
            Map.ofList (List.zip pars (List.ofArray args))

        let judged = found |> List.indexed
        let failing = ResizeArray<int * string>()

        // Whether every group of `group` was answered; each that fails is recorded.
        let rec sort (k: int) (group: (int * IdlType array) list) : bool =
            group
            |> List.groupBy (fun (_, args) -> m.Reads |> List.map (fun i -> identity (cutAt k args[i])))
            |> List.map (fun (_, members) ->
                let _, args = List.head members

                let cut =
                    args |> Array.mapi (fun i a -> if List.contains i m.Reads then cutAt k a else a)

                let whole = m.Reads |> List.forall (fun i -> identity cut[i] = identity args[i])

                match m.Judge (not whole) (binding cut) with
                | Verdict.Fits -> true
                | Verdict.Fails why ->
                    failing.AddRange(members |> List.map (fun (i, _) -> i, why))
                    true
                | Verdict.Undecided -> exact && sort (k + 1) members)
            |> List.forall id

        let answered = sort depth judged

        if failing.Count = 0 then
            if answered then Outcome.Admitted else Outcome.Deeper
        else
            let first, cutWhy = failing |> Seq.minBy fst
            let args = snd judged[first]

            // The sentence of the instantiation itself; a group's was read with its arguments cut.
            let why =
                match m.Judge false (binding args) with
                | Verdict.Fails why -> why
                | Verdict.Fits
                | Verdict.Undecided -> cutWhy

            Outcome.Refused(applies (m.Declaration, List.ofArray args), why, failing.Count - 1, exact && every)

    /// How many instantiations the walks of one declaration check have read, each kind against
    /// [[stepLimit]]: those that find each instantiation (`Each`), and those in classes.
    type Steps =
        { Each: int ref
          InClasses: int ref }

        /// None read yet.
        static member None() = { Each = ref 0; InClasses = ref 0 }

    /// One walk for the members `live`, every argument read at `cut`; what became of each, in order.
    let private walk
        (analysis: Analysis<IdlType>)
        (roots: IdlType list)
        (holdsVariable: IdlType -> bool)
        (steps: int ref)
        (live: Judged list)
        (exact: bool)
        (depth: int)
        (cut: IdlType -> IdlType)
        : Outcome list =
        let identity = identity analysis

        // The positions read, by declaration, for the members `judged`: those whose arguments can
        // become part of an argument one of them reads.
        let positionsFor (judged: Judged list) =
            relevant
                analysis
                [ for m in judged do
                      for i in m.Reads -> m.Declaration, i ]
            |> Seq.groupBy fst
            |> Seq.map (fun (d, at) -> d, at |> Seq.map snd |> Seq.sort |> Array.ofSeq)
            |> dict

        let mutable positions = positionsFor live

        let declarations = live |> List.map _.Declaration |> List.distinct
        let within = reachingAny analysis declarations

        let types =
            System.Collections.Generic.Dictionary<string * int, System.Collections.Generic.HashSet<int>>(
                HashIdentity.Structural
            )

        let over = System.Collections.Generic.HashSet<string * int>(HashIdentity.Structural)

        // A declaration none of whose positions is read is one step, however it is reached.
        let enteredOnce = System.Collections.Generic.HashSet<string>()

        // The instantiations found of each declaration a member is judged at, in the order found.
        let found =
            declarations |> List.map (fun d -> d, ResizeArray<IdlType array>()) |> dict

        let pastCount (m: Judged) =
            m.Reads |> List.exists (fun i -> over.Contains(m.Declaration, i))

        let mutable judging = live.Length
        let mutable exhausted = false
        let stack = System.Collections.Generic.Stack<string * IdlType list>()

        // Pushed last first, so the first reference an instantiation enters is read first.
        let offer (instantiations: (string * IdlType list) list) =
            for (name, _) as instantiation in List.rev instantiations do
                if within.Contains name then
                    stack.Push instantiation

        offer (roots |> List.collect (entered analysis))

        while stack.Count > 0 && judging > 0 && not exhausted do
            if steps.Value >= stepLimit then
                exhausted <- true
            else
                steps.Value <- steps.Value + 1
                let (name, written) as instantiation = stack.Pop()
                let args = Array.ofList written

                let fresh =
                    match positions.TryGetValue name with
                    | true, at ->
                        let mutable fresh = false

                        for i in at do
                            if not (over.Contains(name, i)) then
                                let seen =
                                    match types.TryGetValue((name, i)) with
                                    | true, seen -> seen
                                    | false, _ ->
                                        let seen = System.Collections.Generic.HashSet<int>()
                                        types[(name, i)] <- seen
                                        seen

                                if seen.Add(identity (cut args[i])) then
                                    if seen.Count > parameterLimit then
                                        over.UnionWith(downstream analysis [ name, i ])

                                        // Each position read feeds one a member left reads, so this
                                        // one past the count put a member past it; what fed only
                                        // such members is read no more.
                                        let judged = live |> List.filter (pastCount >> not)

                                        if judged.Length < judging then
                                            judging <- judged.Length
                                            positions <- positionsFor judged
                                    else
                                        fresh <- true

                        fresh
                    | false, _ -> enteredOnce.Add name

                if fresh then
                    // An argument naming a parameter is applied where its owner declares none: no
                    // instantiation to judge, and that owner's own fault.
                    match found.TryGetValue name with
                    | true, those when not (Array.exists holdsVariable args) -> those.Add args
                    | _ -> ()

                    offer (successors analysis instantiation)

        // Where the budget stopped the walk, the declarations an instantiation left unread leads to:
        // those it did not read whole. Every other was read whole, so its members are judged.
        let unfinished =
            if exhausted then
                entering analysis (stack |> Seq.map fst |> Seq.distinct)
            else
                Set.empty

        // Whether the instantiations found of `d` are every one reached: each of its positions is
        // read, and the types found there multiply to as many as were found, so each combination
        // was. (A position past the count is read no more: it fed only members past it.)
        let everyFound (d: string) =
            match positions.TryGetValue d with
            | true, at when at.Length = (parameters analysis d).Length ->
                let count = int64 found[d].Count

                let product =
                    (1L, at)
                    ||> Array.fold (fun acc i ->
                        match types.TryGetValue((d, i)) with
                        | true, seen -> min (acc * int64 seen.Count) (count + 1L)
                        | false, _ -> 0L)

                product = count
            | _ -> false

        live
        |> List.map (fun m ->
            if pastCount m then
                Outcome.TooMany
            elif unfinished.Contains m.Declaration then
                Outcome.TooLong
            else
                judgedOver analysis m exact (everyFound m.Declaration) depth (List.ofSeq found[m.Declaration]))

    /// What becomes of each of `members` at the instantiations a document can hold a value of,
    /// reached from `roots` through the members the analysis's walk follows; `steps` counts the
    /// instantiations read, each kind of walk against [[stepLimit]], across every call sharing it.
    ///
    /// A member's answer at an instantiation reads only the arguments at the parameters its type
    /// names ([[Judged.Reads]]), and depends only on the TYPES each of those parameters takes, never
    /// on which go together: whether a literal fits is a conjunction of checks each reading one node
    /// of the member's type at the instantiation, and each node lies in the member's own type or in
    /// ONE parameter's argument (a set's element and a keyed map's key cannot be a parameter, so no
    /// substitution splits the subtree their check reads); whether a transparent case can encode to
    /// an object reads the outermost form of one argument. So the walk is kept per PARAMETER: it
    /// reads the arguments only at the positions that can become part of an argument a member reads
    /// ([[relevant]]), keeps for each such position the types found there, and enters what an
    /// instantiation enters only where it brings a type new at one of them. One whose every type
    /// was found before enters nothing new: each argument it would place was placed already, at the
    /// same position of the same reference, by the instantiation that first brought it. Each type
    /// found is carried by that instantiation, a real one reached, and a member is judged at those
    /// of its declaration, so every instantiation a refusal names is one the vocabulary reaches, and
    /// the types they place at each parameter are every type the parameter takes.
    ///
    /// Where the instantiations are finitely many ([[unbounded]]), every type a parameter takes is
    /// found. Where they are not, the walk is taken in CLASSES: each argument is read cut at a depth
    /// `k`, what lies below it left unbound, and two that agree to that depth are one type at the
    /// position. A substitution places an argument no shallower than it stood, so the classes are
    /// finitely many at any depth; a member the cut leaves undecided is walked for again a level
    /// deeper.
    ///
    /// A member is judged in groups too, so a literal is rendered once for many instantiations: the
    /// instantiations found are grouped by their arguments at the positions the member reads, cut at
    /// a depth, and each group is judged once, at its arguments cut there. An answer that reads
    /// nothing the cut left unbound is the answer at every instantiation of the group; one that does
    /// splits the group a level deeper (in classes, walks again). A member is refused once, naming
    /// the first instantiation found that it does not fit and how many more it does not fit: every
    /// other, where the instantiations found are every one reached (each position of the
    /// declaration is read, and the types found there multiply to the instantiations found), else
    /// how many more of those found, a lower bound, as in classes, where a class stands for many.
    ///
    /// Two counts bound the walk that finds each instantiation, and past each a member is judged in
    /// classes instead: a parameter that takes more than [[parameterLimit]] types, for each member
    /// reading it or a position an argument at it is placed at (a position an argument is placed at
    /// takes at least as many types as the one it is placed from, [[downstream]], so no type is lost
    /// to a member that is judged); and [[stepLimit]] instantiations read, a safety net, for each
    /// member whose declaration an instantiation left unread leads to (one read whole is judged).
    /// Every position read feeds one a member reads, so a position past the count puts a member past
    /// it, and what fed only members past it is read no more. In classes the same two counts refuse,
    /// never admitting a member unjudged: more than [[parameterLimit]] classes at a position at the
    /// depth reached, and [[stepLimit]] instantiations read by the walks in classes with one unread
    /// that leads to the member's declaration.
    ///
    /// The walk is depth-first: it follows each reference it enters before the next, so a parameter
    /// at the end of a long chain is given its types as the chain is entered.
    ///
    /// The cost, for declarations whose member types hold `s` nodes between them, `p` the argument
    /// positions read, `r` the most references one declaration enters and `N` [[parameterLimit]]:
    /// the analysis is linear in `s`; a walk enters what an instantiation enters at most once per
    /// type new at a position it reads, so at most `p N` times, and reads at most `r p N`
    /// instantiations besides the roots', each costing what its declaration's member types cost
    /// however large its arguments have grown; a member's literal is rendered once per group per
    /// depth. The classes walk is taken again only for the members its cut left undecided, which a
    /// literal's depth bounds. A vocabulary of `n` declarations each instantiated once costs
    /// O(s + n). Each kind of walk reads at most [[stepLimit]] instantiations a check.
    let judgeAtParameters
        (analysis: Analysis<IdlType>)
        (roots: IdlType list)
        (holdsVariable: IdlType -> bool)
        (steps: Steps)
        (members: Judged list)
        : Outcome list =
        let unbounded = unbounded analysis roots

        // What became of each of `live`: walked once where every instantiation is found (`exact`),
        // in classes from depth `k` otherwise, deepened for those the cut leaves undecided.
        let rec verdicts (live: (int * Judged) list) (exact: bool) (k: int) : (int * Outcome) list =
            if List.isEmpty live then
                []
            else
                let outcomes =
                    walk
                        analysis
                        roots
                        holdsVariable
                        (if exact then steps.Each else steps.InClasses)
                        (List.map snd live)
                        exact
                        k
                        (if exact then id else cutAt k)

                let answered, deeper =
                    List.zip live outcomes
                    |> List.partition (fun (_, o) ->
                        match o with
                        | Outcome.Deeper -> false
                        | Outcome.Admitted
                        | Outcome.Refused _
                        | Outcome.TooMany
                        | Outcome.TooLong -> true)

                (answered |> List.map (fun ((i, _), o) -> i, o))
                @ verdicts (List.map fst deeper) exact (k + 1)

        let finite, infinite =
            members
            |> List.indexed
            |> List.partition (fun (_, m) -> not (unbounded.Contains m.Declaration))

        // A member the walk that finds each instantiation could not judge, past one of its counts,
        // is judged in classes with those whose instantiations are infinitely many.
        let judged, again =
            verdicts finite true 1
            |> List.partition (fun (_, o) ->
                match o with
                | Outcome.TooMany
                | Outcome.TooLong -> false
                | Outcome.Admitted
                | Outcome.Refused _
                | Outcome.Deeper -> true)

        let byIndex = Array.ofList members

        let outcomes =
            judged
            @ verdicts (infinite @ (again |> List.map (fun (i, _) -> i, byIndex[i]))) false 1
            |> Map.ofList

        members |> List.mapi (fun i _ -> outcomes[i])
