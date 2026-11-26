namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl.Codegen.Types

/// Phase 732 — the stability classifier's reading of a TYPE PARAMETER and a TYPE ARGUMENT, for a
/// union and a record alike.
///
/// The wire carries no type argument, so how a type is WRITTEN (which parameter a member names,
/// which arguments a reference applies) decides nothing about a stored document. What decides it
/// is the type each member is at the INSTANTIATION a reference applies. This module answers, for
/// one field both revisions carry, whether its type moved at an instantiation the vocabulary
/// reaches ([[TypeMove.Moved]]), is written differently and is the same type at every one
/// ([[TypeMove.Restated]]), or did not move at all.
module internal DiffInstances =

    /// What one field's type did between two revisions.
    [<RequireQualifiedAccess>]
    type TypeMove =
        /// The same type, written the same way up to the names of the owner's own parameters.
        | Unmoved
        /// Written differently; the same type at every instantiation the vocabulary reaches.
        | Restated
        /// A different type at an instantiation the vocabulary reaches, or one no reference shows.
        | Moved

    /// A union or a record of one revision, as this module reads it: its parameters and its
    /// member lists (a union's by case tag, a record's one list under `""`).
    type private Declaration =
        { Params: string list
          Members: Map<string, FieldSnap list> }

    let private declaration (s: Snapshot) (family: string) (name: string) : Declaration option =
        match family with
        | "union" ->
            s.Unions
            |> Map.tryFind name
            |> Option.map (fun u ->
                { Params = u.Params
                  Members = u.Cases |> Map.map (fun _ c -> c.Fields) })
        | _ ->
            s.Records
            |> Map.tryFind name
            |> Option.map (fun fields ->
                { Params = s.RecordParams |> Map.tryFind name |> Option.defaultValue []
                  Members = Map.ofList [ "", fields ] })

    /// A type object that refers to a union or a record: its family, its name and its arguments.
    let private reference (v: JVal) : (string * string * JVal list) option =
        match DiffSnapshot.str "$type" v, DiffSnapshot.str "name" v with
        | Some(("union" | "record") as family), Some name -> Some(family, name, DiffSnapshot.arr "args" v)
        | _ -> None

    /// `v` with every type variable `binding` names replaced by what it is bound to. A variable
    /// it does not name is left as written.
    let rec private substitute (binding: Map<string, JVal>) (v: JVal) : JVal =
        match v with
        | JObj fs ->
            match DiffSnapshot.str "$type" v, DiffSnapshot.str "name" v with
            | Some "var", Some n -> binding |> Map.tryFind n |> Option.defaultValue v
            | _ -> JObj(fs |> List.map (fun (k, x) -> k, substitute binding x))
        | JArr xs -> JArr(xs |> List.map (substitute binding))
        | _ -> v

    /// The binding a reference makes: one argument per declared parameter, or none when the
    /// count is wrong (a reference no value can be read at).
    let private bind (d: Declaration) (args: JVal list) : Map<string, JVal> option =
        if d.Params.Length = args.Length then
            Some(List.zip d.Params args |> Map.ofList)
        else
            None

    /// A member type with the owner's own parameters named by POSITION, so that renaming a
    /// parameter moves nothing and reordering two does.
    let private positional (pars: string list) (shape: JVal) : JVal =
        substitute
            (pars
             |> List.mapi (fun i p -> p, JObj [ "$type", JStr "var"; "index", JInt i ])
             |> Map.ofList)
            shape

    /// The members two revisions of one declaration both carry, paired by case tag and name.
    /// A member only one carries is that declaration's own added or removed row.
    let private paired (b: Declaration) (a: Declaration) : (FieldSnap * FieldSnap) list =
        [ for KeyValue(tag, bfs) in b.Members do
              match Map.tryFind tag a.Members with
              | Some afs ->
                  for bf in bfs do
                      match afs |> List.tryFind (fun af -> af.Name = bf.Name) with
                      | Some af -> yield bf, af
                      | None -> ()
              | None -> () ]

    /// One declaration at the arguments one position applies to it in each revision.
    type private Instance =
        { Family: string
          Name: string
          Before: JVal list
          After: JVal list }

        member this.Key =
            let text (xs: JVal list) =
                Canonical.write Rendering.CrossHost (JArr xs)

            this.Family + "|" + this.Name + "|" + text this.Before + "|" + text this.After

    /// A pair of references to one declaration that has something to instantiate: the declaration
    /// in each revision, unless neither declares a parameter and neither reference applies an
    /// argument (such a declaration's members are graded on its own rows, as they always were).
    let private instance
        (before: Snapshot)
        (after: Snapshot)
        (b: JVal)
        (a: JVal)
        : (Instance * Declaration * Declaration) option =
        match reference b, reference a with
        | Some(fb, nb, argsB), Some(fa, na, argsA) when fb = fa && nb = na ->
            match declaration before fb nb, declaration after fa na with
            | Some db, Some da when not (db.Params.IsEmpty && da.Params.IsEmpty && argsB.IsEmpty && argsA.IsEmpty) ->
                Some(
                    { Family = fb
                      Name = nb
                      Before = argsB
                      After = argsA },
                    db,
                    da
                )
            | _ -> None
        | _ -> None

    // -----------------------------------------------------------------------
    // Whether one revision's instantiations can all be named (`Instantiations`): asked before any
    // walk below starts, because a walk over them ends only where they are finitely many.
    // -----------------------------------------------------------------------

    /// A type shape as the instantiation analysis reads it: a parameter, a reference to a
    /// declaration at its arguments, or anything else with the shapes it holds.
    [<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
    let private shapeOf (v: JVal) : Fuaran.Core.Idl.Instantiations.Shape<JVal> =
        match reference v, v with
        | Some(family, name, args), _ -> Fuaran.Core.Idl.Instantiations.Shape.Reference(family + " " + name, args)
        | None, JObj fs ->
            match DiffSnapshot.str "$type" v, DiffSnapshot.str "name" v with
            | Some "var", Some n -> Fuaran.Core.Idl.Instantiations.Shape.Parameter n
            | _ -> Fuaran.Core.Idl.Instantiations.Shape.Holds(fs |> List.map snd)
        | None, JArr xs -> Fuaran.Core.Idl.Instantiations.Shape.Holds xs
        | None, _ -> Fuaran.Core.Idl.Instantiations.Shape.Holds []

    /// Every declaration of one revision: its key as [[shapeOf]] names it, its parameters and its
    /// members' type shapes.
    let private declarations (s: Snapshot) : (string * string list * JVal list) list =
        [ for KeyValue(name, u) in s.Unions do
              "union " + name,
              u.Params,
              [ for KeyValue(_, c) in u.Cases do
                    for f in c.Fields do
                        f.Wire.Shape ]
          for KeyValue(name, fields) in s.Records do
              "record " + name,
              s.RecordParams |> Map.tryFind name |> Option.defaultValue [],
              fields |> List.map (fun f -> f.Wire.Shape) ]

    /// A type shape as its head and the shapes it holds: an object's member values and an array's
    /// items, the rest of it (its keys, a scalar) the head.
    let private splitOf (v: JVal) : JVal * JVal list =
        match v with
        | JObj fs -> JObj(fs |> List.map (fun (k, _) -> k, JBool false)), fs |> List.map snd
        | JArr xs -> JArr(xs |> List.map (fun _ -> JBool false)), xs
        | _ -> v, []

    /// One revision's declarations, analysed.
    [<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
    let private analysed (s: Snapshot) : Fuaran.Core.Idl.Instantiations.Analysis<JVal> =
        Fuaran.Core.Idl.Instantiations.analyse (
            Fuaran.Core.Idl.Instantiations.reading (declarations s) shapeOf substitute splitOf false
        )

    /// Whether the instantiations one revision reaches from `roots` cannot all be named: they are
    /// infinitely many, more than an enumeration reads, or more than
    /// `Instantiations.sizeLimit` nodes between them, since the walks below print each one they
    /// enter (an [[Instance]]'s key).
    [<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
    let private unread (analysis: Fuaran.Core.Idl.Instantiations.Analysis<JVal>) (roots: JVal list) : bool =
        match Fuaran.Core.Idl.Instantiations.closure analysis roots with
        | Fuaran.Core.Idl.Instantiations.Closure.Finite(_, printed) ->
            printed > int64 Fuaran.Core.Idl.Instantiations.sizeLimit
        | Fuaran.Core.Idl.Instantiations.Closure.Infinite _
        | Fuaran.Core.Idl.Instantiations.Closure.Exceeds -> true

    /// Every type shape an owner that declares no parameter carries in one revision: where the
    /// walk of [[reached]] starts.
    let private roots (s: Snapshot) : JVal list =
        let shapes (fields: FieldSnap list) =
            fields |> List.map (fun f -> f.Wire.Shape)

        [ for KeyValue(_, fields) in s.Kinds do
              yield! shapes fields
          for KeyValue(_, fields) in s.Ops do
              yield! shapes fields
          yield! shapes s.NodeFields
          for family in [ "union"; "record" ] do
              for name in
                  (if family = "union" then
                       Map.keys s.Unions
                   else
                       Map.keys s.Records) do
                  match declaration s family name with
                  | Some d when d.Params.IsEmpty ->
                      for KeyValue(_, fields) in d.Members do
                          yield! shapes fields
                  | _ -> ()
          for KeyValue(_, c) in s.Contracts do
              for KeyValue(_, m) in c.Methods do
                  for x in m.Arguments do
                      x.Wire.Shape

                  match m.Result with
                  | Some r -> r.Shape
                  | None -> () ]

    /// Are `b` (a closed type of the old revision) and `a` (of the new) the same type to a stored
    /// document: equal as written, except that a reference to a generic declaration is compared
    /// by the type each paired member is at the reference's instantiation, not by its arguments.
    /// An instantiation pair met again is taken as agreeing (the comparison is coinductive, as the
    /// types are recursive): the answer is a conjunction over every pair reached, so one that
    /// differs anywhere is found wherever it is first met. The caller asks first that both types
    /// reach finitely many instantiations ([[unread]]); the pairs are then finitely many too.
    let private same (before: Snapshot) (after: Snapshot) (b: JVal) (a: JVal) : bool =
        let entered = System.Collections.Generic.HashSet<string>()

        let rec agree (b: JVal) (a: JVal) : bool =
            match instance before after b a with
            | Some(i, db, da) ->
                not (entered.Add i.Key)
                || match bind db i.Before, bind da i.After with
                   | Some bb, Some ba ->
                       paired db da
                       |> List.forall (fun (bf, af) ->
                           agree (substitute bb bf.Wire.Shape) (substitute ba af.Wire.Shape))
                   | _ -> false
            | None ->
                match b, a with
                | JObj bs, JObj afs when (reference b).IsNone && (reference a).IsNone ->
                    List.map fst bs = List.map fst afs
                    && List.forall2 (fun (_, x) (_, y) -> agree x y) bs afs
                | JArr bs, JArr afs -> bs.Length = afs.Length && List.forall2 agree bs afs
                | _ -> b = a

        agree b a

    /// Every instantiation pair the two revisions reach together: from each field, contract
    /// argument and contract result both carry on an owner that declares no parameter in either,
    /// down through the members of each generic declaration at the arguments applied. Positions
    /// whose types are not both references to one declaration are descended member by member
    /// where their shapes allow, and otherwise end the walk there: a position that changed shape
    /// has its own row. The caller asks first that each revision reaches finitely many
    /// instantiations ([[unread]]): a pair is one of each, so the walk ends.
    let private reached (before: Snapshot) (after: Snapshot) : Map<string * string, Instance list> =
        let rec walk (acc: Map<string, Instance>) (b: JVal) (a: JVal) : Map<string, Instance> =
            match instance before after b a with
            | Some(i, db, da) ->
                if acc.ContainsKey i.Key then
                    acc
                else
                    match bind db i.Before, bind da i.After with
                    | Some bb, Some ba ->
                        (acc.Add(i.Key, i), paired db da)
                        ||> List.fold (fun acc (bf, af) ->
                            walk acc (substitute bb bf.Wire.Shape) (substitute ba af.Wire.Shape))
                    | _ -> acc.Add(i.Key, i)
            | None ->
                match b, a with
                | JObj bs, JObj afs when (reference b).IsNone && (reference a).IsNone ->
                    (acc, bs)
                    ||> List.fold (fun acc (k, x) ->
                        match afs |> List.tryFind (fun (ak, _) -> ak = k) with
                        | Some(_, y) -> walk acc x y
                        | None -> acc)
                | JArr bs, JArr afs when bs.Length = afs.Length ->
                    (acc, List.zip bs afs) ||> List.fold (fun acc (x, y) -> walk acc x y)
                | _ -> acc

        let fields (acc: Map<string, Instance>) (bfs: FieldSnap list) (afs: FieldSnap list) =
            (acc, bfs)
            ||> List.fold (fun acc bf ->
                match afs |> List.tryFind (fun af -> af.Name = bf.Name) with
                | Some af -> walk acc bf.Wire.Shape af.Wire.Shape
                | None -> acc)

        let owners (acc: Map<string, Instance>) (b: Map<string, FieldSnap list>) (a: Map<string, FieldSnap list>) =
            (acc, b)
            ||> Map.fold (fun acc name bfs ->
                match Map.tryFind name a with
                | Some afs -> fields acc bfs afs
                | None -> acc)

        let closed (s: Snapshot) (family: string) : Map<string, FieldSnap list> =
            [ for name in
                  (if family = "union" then
                       s.Unions |> Map.keys |> List.ofSeq
                   else
                       s.Records |> Map.keys |> List.ofSeq) do
                  match declaration s family name with
                  | Some d when d.Params.IsEmpty ->
                      for KeyValue(tag, fs) in d.Members do
                          yield name + "." + tag, fs
                  | _ -> () ]
            |> Map.ofList

        let contracts (acc: Map<string, Instance>) =
            (acc, before.Contracts)
            ||> Map.fold (fun acc name bc ->
                match Map.tryFind name after.Contracts with
                | None -> acc
                | Some ac ->
                    (acc, bc.Methods)
                    ||> Map.fold (fun acc id bm ->
                        match Map.tryFind id ac.Methods with
                        | None -> acc
                        | Some am ->
                            let acc =
                                (acc, bm.Arguments)
                                ||> List.fold (fun acc bx ->
                                    match am.Arguments |> List.tryFind (fun ax -> ax.Name = bx.Name) with
                                    | Some ax -> walk acc bx.Wire.Shape ax.Wire.Shape
                                    | None -> acc)

                            match bm.Result, am.Result with
                            | Some br, Some ar -> walk acc br.Shape ar.Shape
                            | _ -> acc))

        let all =
            owners Map.empty before.Kinds after.Kinds
            |> fun acc -> owners acc before.Ops after.Ops
            |> fun acc -> fields acc before.NodeFields after.NodeFields
            |> fun acc -> owners acc (closed before "union") (closed after "union")
            |> fun acc -> owners acc (closed before "record") (closed after "record")
            |> contracts

        all
        |> Map.toList
        |> List.map snd
        |> List.groupBy (fun i -> i.Family, i.Name)
        |> Map.ofList

    /// The judge of one snapshot pair: what the type of a field both revisions carry did, by
    /// what an old document does. The reach is computed once, when a field first needs it.
    ///
    /// A revision whose instantiations cannot all be named (a declaration that reaches itself at a
    /// larger argument) has no reach to read a restated member at, and a type that reaches such a
    /// declaration cannot be compared to its end: each reads as MOVED, the grade that claims
    /// nothing of an old document.
    let judge (before: Snapshot) (after: Snapshot) : Owner -> FieldSnap -> FieldSnap -> TypeMove =
        let analyses = lazy (analysed before, analysed after)

        let reach =
            lazy
                (let old, now = analyses.Value

                 if unread old (roots before) || unread now (roots after) then
                     Map.empty
                 else
                     reached before after)

        let declared (s: Snapshot) (owner: Owner) : string list =
            match owner with
            | OUnionCase(u, _) -> s.Unions |> Map.tryFind u |> Option.map _.Params |> Option.defaultValue []
            | ORecord n -> s.RecordParams |> Map.tryFind n |> Option.defaultValue []
            | OKind _
            | OOp _
            | ONodeEnvelope -> []

        fun owner old now ->
            let agrees (bb: Map<string, JVal>) (ba: Map<string, JVal>) =
                let b, a = substitute bb old.Wire.Shape, substitute ba now.Wire.Shape
                let analysedOld, analysedNow = analyses.Value

                not (unread analysedOld [ b ])
                && not (unread analysedNow [ a ])
                && same before after b a

            match declared before owner, declared after owner with
            | [], [] ->
                if old.Wire = now.Wire then TypeMove.Unmoved
                elif agrees Map.empty Map.empty then TypeMove.Restated
                else TypeMove.Moved
            | pb, pa ->
                if positional pb old.Wire.Shape = positional pa now.Wire.Shape then
                    TypeMove.Unmoved
                else
                    let family, name =
                        match owner with
                        | OUnionCase(u, _) -> "union", u
                        | ORecord n -> "record", n
                        | OKind _
                        | OOp _
                        | ONodeEnvelope -> "", ""

                    match reach.Value |> Map.tryFind (family, name) with
                    | None
                    | Some [] -> TypeMove.Moved
                    | Some instances ->
                        let unmovedAtEvery =
                            instances
                            |> List.forall (fun i ->
                                List.length pb = i.Before.Length
                                && List.length pa = i.After.Length
                                && agrees (List.zip pb i.Before |> Map.ofList) (List.zip pa i.After |> Map.ofList))

                        if unmovedAtEvery then TypeMove.Restated else TypeMove.Moved
