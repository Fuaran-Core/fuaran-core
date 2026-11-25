namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Idl.Emit.Core

/// The F* proof-model target's MODEL (Phase 150; split out of `FStar.fs` by Phase 524): the
/// monomorphic slot, the resolution of an IDL type to a slot with its refusals, the naming every
/// emitted identifier takes, each reached declaration's members, the model over `Decl`, and the
/// expressibility partition. Pure functions of the IDL, so the emitted bytes are the same on every
/// machine. The published entries are `FStarTarget`'s.
module internal FStarModel =

    // -----------------------------------------------------------------------
    // 1. The monomorphic slot — an IDL type with every type variable resolved.
    // -----------------------------------------------------------------------

    /// A wire slot the model can express, with generic unions already instantiated.
    /// Everything the target refuses is absent by construction: there is no `SClosure`,
    /// so a closure cannot reach the emitter and be dropped there.
    type Slot =
        /// `TStr` — `JStr`.
        | SStr
        /// `TInt` — `JInt`, carried by the model's opaque `num`.
        | SInt
        /// `TBool` — `JBool`.
        | SBool
        /// `TFloat` — `JFloat`, carried by the model's opaque `flt`.
        | SFloat
        /// `TJson`, and a `THosted` slot whose wire is JSON: an arbitrary value carried
        /// verbatim in both directions. The host type behind a hosted slot has no F*
        /// counterpart and is deliberately not invented — what the WIRE carries is a
        /// value, and that is exactly what the model says.
        | SJson
        /// A wire-visible slot whose value is a FIXED SENTINEL STRING — `TClosure` / `TFn`
        /// (`"<closure>"`) and `TOpaque` (`"<opaque>"`). The host type behind it is a function
        /// or an `obj` the encoder cannot see; what the WIRE carries is one constant, so the
        /// model carries `unit`. That is not a weakening — it is the precise statement that
        /// the member holds no information, and its round trip is the one that says so.
        ///
        /// A HOST-ONLY closure is a different thing and is absent from the model entirely: it
        /// is never on the wire at all, where this one always is.
        | SSentinel of sentinel: string
        /// `TEnum` — a closed set of wire strings.
        | SEnum of enumName: string
        /// `TRecord` — a named object with no discriminator.
        | SRecord of recordName: string
        /// `TUnion`, instantiated: the union's name and its resolved type arguments.
        | SUnion of unionName: string * args: Slot list
        /// `TList` — a JSON array.
        | SList of Slot
        /// `TMap` — a JSON object with authored keys, modelled as an association list
        /// (a map has no declared order, and an assoc list is what the wire object IS).
        | SMap of Slot
        /// `TNode` — the vocabulary's own recursion.
        | SNode
        /// Phase 639 (D251) — a string-borne exact scalar (`TInt64`, `TDecimal`, `TBytes`,
        /// `TTimeOfDay`, `TDuration`), by its `idl.json` name: a `JStr` whose model type is the string
        /// REFINED by the canonical-spelling predicate the model declares, so the decoder refuses
        /// what every host refuses and the round trip holds by the refinement.
        | SText of scalar: string
        /// Phase 639 — a tuple of LEAF slots (a scalar, an enum, a string-borne scalar or a tuple of
        /// those): a `JArr` of one item per component, modelled as an F* tuple. A component that is a
        /// declared type would put the tuple inside the recursive family, which the model does not do;
        /// it is refused by name.
        | STuple of Slot list
        /// Phase 722 (D220.3) — a member a presence-keyed case SYNTHESISES: the wire carries one
        /// declared value there, spelled here as the F* `jval` literal of the interpreter's encoding
        /// of it. Like a sentinel it holds no information, so the model carries `unit`; unlike one,
        /// the decoder compares what it reads and refuses another value, as the reading does.
        | SConst of wire: string

    /// A member of the model: one declared field with its resolved slot. A host-only field
    /// has no wire projection and never becomes a member — the model is of the WIRE.
    type Member =
        {
            Name: string
            /// The declared type, closed (Phase 525): what a default literal is admitted at.
            Type: IdlType
            Slot: Slot
            /// `None` for a required member; `Some None` for an optional one; `Some (Some lit)`
            /// for an omit-default whose default renders as the F* literal `lit`.
            Presence: string option option
            /// The declared optionality — what the one presence rule ([[Decl.memberValue]]) reads.
            Opt: Optionality
            /// Phase 722 — the field of the holder this member was laid out from, when it is a member
            /// of an INLINE declaration (D220): an inline record or the picked case of a
            /// presence-keyed union. Neither has an object on the wire, so its members are the
            /// holder's own in the model, and an authored value of the holder still carries them
            /// one level down, under this field ([[authoredFor]]). `None` for the holder's own member.
            Held: string option
        }

    /// The authored field list the member `m` of a holder is looked up in: the holder's own, or,
    /// for a member laid out from an inline declaration, the record value or the case value the
    /// holder carries at [[Member.Held]] (nothing when the holder carries none there).
    let authoredFor (m: Member) (authored: (string * IdlValue) list) : (string * IdlValue) list =
        match m.Held with
        | None -> authored
        | Some held ->
            authored
            |> List.tryPick (fun (n, v) ->
                match v with
                | VRecord inner
                | VUnion(_, inner) when n = held -> Some inner
                | _ -> None)
            |> Option.defaultValue []

    /// Phase 722 (D220.3) — what one field holding a PRESENCE-KEYED union reads as in one
    /// constructor of its holder: the case whose members that constructor carries, or no case (an
    /// optional union with no key present). The wire object has one shape per choice, so the model
    /// states one constructor per choice ([[fieldVariants]]).
    type Pick =
        {
            /// The holder's field.
            Field: string
            /// The union the field holds, by its declared name.
            Union: string
            Case: string option
        }

    /// The case of the union at `field` an authored value of the holder carries, if it carries one.
    let authoredCase (field: string) (authored: (string * IdlValue) list) : string option =
        authored
        |> List.tryPick (fun (n, v) ->
            match v with
            | VUnion(case, _) when n = field -> Some case
            | _ -> None)

    // -----------------------------------------------------------------------
    // 2. Resolution — IDL type to slot, with the refusals.
    // -----------------------------------------------------------------------

    let unmodellable (construct: string) (where: string) =
        Error(CodegenError.UnmodellableInFStar(construct, where))

    /// Phase 525 — a CLOSED IDL type (every type variable already substituted, by the
    /// instantiation rule [[Decl]] applies) as the model's slot. A translation of one type
    /// expression: the declaration graph is walked once, by [[Decl]] (its wire reach), and
    /// never here.
    ///
    /// A HOSTED slot is modelled as every host decodes it. One that declares its wire form is
    /// that form — the F# and TypeScript decoders check it before the codec runs (Phase 252),
    /// so the model's decoder refuses what theirs refuse; a wire form no host can read is refused
    /// with the hosts' own refusal; a declared string FORMAT is refused by name, because the
    /// model's string carries no format predicate and a model that ignored it would accept what
    /// every host refuses. One that declares no wire form is carried verbatim, the known
    /// disagreement D109.5 names.
    /// Phase 639 — whether a slot may be a tuple's component: a scalar, an enum, a string-borne
    /// scalar or a tuple of those, whose codec sits outside the recursive family.
    let rec tupleLeaf (s: Slot) : bool =
        match s with
        | SStr
        | SInt
        | SBool
        | SFloat
        | SEnum _
        | SText _ -> true
        | STuple cs -> List.forall tupleLeaf cs
        | _ -> false

    let rec private slotOf (where: string) (t: IdlType) : Result<Slot, CodegenError> =
        match t with
        | TStr -> Ok SStr
        | TInt -> Ok SInt
        | TBool -> Ok SBool
        | TFloat -> Ok SFloat
        | TJson -> Ok SJson
        | TNode -> Ok SNode
        | TEnum n -> Ok(SEnum n)
        | TRecord(n, _) -> Ok(SRecord n)
        | TList inner -> slotOf where inner |> Result.map SList
        | TMap vt -> slotOf where vt |> Result.map SMap
        | TVar v -> unmodellable (sprintf "an unresolved type parameter '%s" v) where
        | TUnion(n, args) ->
            let rec go acc rest =
                match rest with
                | [] -> Ok(List.rev acc)
                | a :: t ->
                    match slotOf where a with
                    | Error e -> Error e
                    | Ok s -> go (s :: acc) t

            go [] args |> Result.map (fun ss -> SUnion(n, ss))
        | THosted h when h.FSharp = "" -> unmodellable "a host codec with no declared host type" where
        | THosted h ->
            match hostedWireRefusal "the F* proof model" h with
            | Some e -> Error e
            | None ->
                match h.Wire, h.Format with
                | Some _, Some f ->
                    unmodellable
                        (sprintf
                            "a hosted slot declaring the format '%s', which the model's string carries no predicate for"
                            (HostedFormat.name f))
                        where
                | Some w, None -> slotOf where w
                | None, _ -> Ok SJson
        | TClosure -> Ok(SSentinel "<closure>")
        | TFn _ -> Ok(SSentinel "<closure>")
        | TOpaque -> Ok(SSentinel "<opaque>")
        | TKind -> unmodellable "a bare kind slot" where
        | TOp -> unmodellable "a tree-op slot" where
        // Phase 639 (D251) — the wider data universe. A set is its wire array and a keyed map its
        // array of `[key, value]` tuples: the model carries the items in the encoder's order, and the
        // canonical order and the distinctness over canonical bytes are the hosts' rule, which a model
        // whose numbers are opaque cannot compute (the boundary a `TMap`'s key order already states).
        | TInt64 -> Ok(SText "int64")
        | TDecimal -> Ok(SText "decimal")
        | TBytes -> Ok(SText "bytes")
        | TTimeOfDay -> Ok(SText "timeOfDay")
        | TDuration -> Ok(SText "duration")
        | TTuple cs ->
            let rec go acc rest =
                match rest with
                | [] -> Ok(STuple(List.rev acc))
                | c :: tail ->
                    match slotOf where c with
                    | Error e -> Error e
                    | Ok s when tupleLeaf s -> go (s :: acc) tail
                    | Ok _ ->
                        unmodellable
                            (sprintf "a tuple component that is not a scalar, an enum or a tuple of those (%A)" c)
                            where

            go [] cs
        | TSet e -> slotOf where e |> Result.map SList
        | TKeyedMap(k, v) -> slotOf where (TTuple [ k; v ]) |> Result.map SList

    // -----------------------------------------------------------------------
    // 3. Naming. Every emitted identifier is a pure function of the IDL, so the
    //    generated bytes are the same on every machine and in every checkout.
    // -----------------------------------------------------------------------

    /// `PascalCase` / `camelCase` to the `snake_case` F* value namespace.
    let snake (s: string) : string =
        let sb = System.Text.StringBuilder()

        s
        |> Seq.iteri (fun i ch ->
            if System.Char.IsUpper ch then
                if i > 0 then
                    sb.Append '_' |> ignore

                sb.Append(System.Char.ToLowerInvariant ch) |> ignore
            elif System.Char.IsLetterOrDigit ch then
                sb.Append ch |> ignore
            else
                sb.Append '_' |> ignore)

        sb.ToString()

    /// The F* type name of a slot that DECLARES a type; the structural spelling otherwise.
    let rec slotName (s: Slot) : string =
        match s with
        | SStr -> "str"
        | SInt -> "int"
        | SBool -> "bool"
        | SFloat -> "flt"
        | SJson -> "json"
        | SSentinel "<opaque>" -> "opaque"
        | SSentinel _ -> "closure"
        | SEnum n -> "e_" + snake n
        | SRecord n -> "r_" + snake n
        | SUnion(n, []) -> "u_" + snake n
        | SUnion(n, args) -> "u_" + snake n + "__" + (args |> List.map slotName |> String.concat "_")
        | SList inner -> "l_" + slotName inner
        | SMap inner -> "m_" + slotName inner
        | SNode -> "node"
        | SText n -> "t_" + snake n
        // The arity leads, so a nested tuple's name reads one way (a prefix code over the names).
        | STuple cs -> sprintf "tp%d_" cs.Length + (cs |> List.map slotName |> String.concat "_")
        | SConst _ -> "const"

    /// The F* TYPE EXPRESSION of a slot. Every declared type is parameterised by the two
    /// opaque numeric carriers, because `jval` is — see `WireDecode`'s header on opacity.
    let rec slotType (s: Slot) : string =
        match s with
        | SStr -> "string"
        | SInt -> "num"
        | SBool -> "bool"
        | SFloat -> "flt"
        | SJson -> "jval num flt"
        | SSentinel _
        | SConst _ -> "unit"
        | SEnum n -> "e_" + snake n
        | SRecord _
        | SUnion _
        | SNode -> slotName s + " num flt"
        | SList inner -> "list (" + slotType inner + ")"
        | SMap inner -> "list (string & " + slotType inner + ")"
        | SText _ -> slotName s
        | STuple cs -> "(" + (cs |> List.map slotType |> String.concat " & ") + ")"

    /// An F* string literal — [[SourceLit.fstarString]] (Phase 292), the F* policy of the one
    /// escaper every emitter splices IDL-authored text through.
    let lit (s: string) : string = SourceLit.fstarString s

    /// F*'s reserved words, plus the primitive type names a constructor's own telescope
    /// refers to. A declared member called `open` or `type` is perfectly ordinary in a wire
    /// vocabulary and a syntax error in an F* binder position — `Disclosure.open` is the one
    /// in this corpus — and a member called `int` would shadow the type the NEXT binder in
    /// the same telescope is declared at, which is worse because it parses.
    let private reserved =
        set
            [ "and"
              "as"
              "assert"
              "assume"
              "attributes"
              "begin"
              "by"
              "calc"
              "class"
              "decreases"
              "default"
              "effect"
              "eliminate"
              "else"
              "end"
              "ensures"
              "exception"
              "exists"
              "false"
              "forall"
              "friend"
              "fun"
              "function"
              "if"
              "in"
              "include"
              "inline"
              "inline_for_extraction"
              "instance"
              "introduce"
              "irreducible"
              "let"
              "logic"
              "match"
              "module"
              "new"
              "new_effect"
              "noeq"
              "noextract"
              "of"
              "open"
              "opaque"
              "private"
              "quote"
              "range_of"
              "rec"
              "reifiable"
              "reify"
              "reflectable"
              "requires"
              "returns"
              "set_range_of"
              "sub_effect"
              "synth"
              "then"
              "total"
              "true"
              "try"
              "type"
              "unfold"
              "unopteq"
              "val"
              "when"
              "with"
              // the names the emitted telescopes themselves refer to
              "bool"
              "flt"
              "int"
              "list"
              "nat"
              "num"
              "option"
              "pos"
              "prop"
              "string"
              "unit"
              "jval"
              "outcome"
              "node"
              "vkind" ]

    /// A member's name as an F* BINDER — the snake spelling, suffixed when it would collide.
    let binderName (name: string) : string =
        let s = snake name
        if reserved.Contains s then s + "_" else s

    /// A constructor name — uppercase-initial and unique across the module, which is what
    /// the `<owner>__<case>` shape buys: two unions may each declare a `Static`.
    let ctorName (owner: string) (case: string) : string = "C__" + owner + "__" + case

    // -----------------------------------------------------------------------
    // 4. The model's members — each reached declaration's fields, as slots.
    // -----------------------------------------------------------------------

    /// Whether a slot is one the emitted module DECLARES a type for (and therefore one the
    /// mutual recursion has to carry), as opposed to a structural spelling over others.
    /// Phase 639 — the string-borne scalars and the tuples `order` reaches at any depth, components
    /// before the tuples holding them and each once: the codecs the model declares above its
    /// recursive family, beside the enums.
    let widerLeaves (order: Slot list) : Slot list =
        let out = ResizeArray<Slot>()
        let seen = System.Collections.Generic.HashSet<string>()

        let rec visit (s: Slot) =
            match s with
            | SText _ ->
                if seen.Add(slotName s) then
                    out.Add s
            | STuple cs ->
                List.iter visit cs

                if seen.Add(slotName s) then
                    out.Add s
            | SList inner
            | SMap inner -> visit inner
            | SUnion(_, args) -> List.iter visit args
            | _ -> ()

        List.iter visit order
        List.ofSeq out

    let declares (s: Slot) =
        match s with
        | SRecord _
        | SUnion _
        | SNode -> true
        | _ -> false

    /// Phase 293 — the declared key order, applied ONCE here to every member list the closure
    /// carries, so that every site that emits a constructor (its type, its encoder arm, its
    /// decoder reads, its binders and its lemmas) walks the same ordered list. `Sorted` is the
    /// Ordinal order `Canonical.write Rendering.CrossHost` applies; `Declared` is the authored order, which is what
    /// `Canonical.write Rendering.Declared` leaves alone.
    let orderMembers (order: KeyOrder) (ms: Member list) =
        match order with
        | KeyOrder.Sorted -> ms |> List.sortWith (fun a b -> System.String.CompareOrdinal(a.Name, b.Name))
        | KeyOrder.Declared -> ms

    // Phase 293 — the finders are the vocabulary's own (`CodegenLookup.tryRecord` / `tryUnion` /
    // `tryEnum`, one body since Phase 388): one spelling of a name lookup across every backend.

    /// The declared default of an omit-default member, as an F* literal. WHETHER a default is
    /// admitted is [[Decl.literal]]'s — the one admissibility the F# backend reads too (Phase 525;
    /// until then this target decided its own and admitted an enum default by its host name, an
    /// empty map, a sentinel and a union's transparent case, all of which the F# backend refuses).
    /// What is decided here is only what the MODEL can spell: the model's `num` and `flt` are
    /// OPAQUE, so there is no F* literal for a number whose carrier is a type parameter, and an
    /// admitted numeric default is refused as the model's boundary, not as a defect in the
    /// declaration.
    ///
    /// Phase 656 — at the wider data universe's types (DECISIONS.md D259): a tuple is the F* tuple of
    /// its items' literals and the empty set and keyed map are `[]` (the model's set and keyed map
    /// are their wire lists), so neither carries an obligation. A 64-bit integer, a decimal, bytes,
    /// a time of day and a duration are refused, by name, as the model's boundary: their model type
    /// is a string REFINED by its grammar, so a literal there must be proved to satisfy it, which no
    /// term this emitter prints discharges — the boundary the vectors already state for a value at
    /// those types ([[FStarVectors]]).
    /// A wire value as an F* `jval` term, or `None` when it holds a number: the model's `num` and
    /// `flt` are OPAQUE, so no literal spells one.
    let rec private jvalTerm (j: JVal) : string option =
        let all (xs: string option list) =
            if List.forall Option.isSome xs then
                Some(List.choose id xs)
            else
                None

        match j with
        | JStr s -> Some("(JStr " + lit s + ")")
        | JBool b -> Some(if b then "(JBool true)" else "(JBool false)")
        | JInt _
        | JFloat _ -> None
        | JArr xs ->
            all (List.map jvalTerm xs)
            |> Option.map (fun ts -> "(JArr [" + String.concat "; " ts + "])")
        | JObj ms ->
            all (
                ms
                |> List.map (fun (k, x) -> jvalTerm x |> Option.map (fun t -> "(" + lit k + ", " + t + ")"))
            )
            |> Option.map (fun ts -> "(JObj [" + String.concat "; " ts + "])")

    /// A wire value with every object's keys in the vocabulary's declared KEY ORDER: Ordinal-sorted
    /// under `Sorted`, as the canonical writer leaves it, and as the encoder built it under `Declared`.
    let rec private inKeyOrder (order: KeyOrder) (j: JVal) : JVal =
        match j with
        | JArr xs -> JArr(List.map (inKeyOrder order) xs)
        | JObj ms ->
            let inner = ms |> List.map (fun (k, x) -> k, inKeyOrder order x)

            match order with
            | KeyOrder.Sorted -> JObj(inner |> List.sortWith (fun (a, _) (b, _) -> System.String.CompareOrdinal(a, b)))
            | KeyOrder.Declared -> JObj inner
        | other -> other

    /// The suffix a constructor's name takes for the cases its presence-keyed fields hold: each
    /// picked case's name in field order, `No<Union>` where an optional union holds none. Empty
    /// for a constructor holding no such field, so its name is the one it always had.
    let variantSuffix (picks: Pick list) : string =
        picks
        |> List.map (fun p ->
            "__"
            + (match p.Case with
               | Some case -> case
               | None -> "No" + p.Union))
        |> String.concat ""

    let rec private defaultLit (idl: Idl) (where: string) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> =
        Decl.literal idl t v
        |> Result.bind (fun l ->
            match l with
            | Literal.Str s -> Ok(lit s)
            | Literal.Bool b -> Ok(if b then "true" else "false")
            | Literal.Int _
            | Literal.Float _ ->
                unmodellable (sprintf "a declared default the opaque numeric model cannot spell: %A" v) where
            | Literal.Enum(n, case) -> Ok(ctorName ("e_" + snake n) case)
            | Literal.EmptyList -> Ok "[]"
            // Phase 539 — the model's map is an association list, so the empty map is `[]`; a JSON
            // default is a `jval` term, spellable unless it holds a number (the opaque numeric
            // boundary the scalar arms above already state).
            | Literal.EmptyMap -> Ok "[]"
            | Literal.EmptySet
            | Literal.EmptyKeyedMap -> Ok "[]"
            | Literal.Int64 _
            | Literal.Text _
            | Literal.Bytes _ ->
                unmodellable
                    (sprintf
                        "a declared default at a string-borne scalar, whose refinement the model does not discharge: %A"
                        v)
                    where
            | Literal.Tuple(components, items) ->
                List.map2 (defaultLit idl where) components items
                |> sequenceR
                |> Result.map (fun xs -> "(" + String.concat ", " xs + ")")
            | Literal.Json j ->
                match jvalTerm j with
                | Some t -> Ok t
                | None ->
                    unmodellable
                        (sprintf "a declared JSON default holding a number the opaque numeric model cannot spell: %A" v)
                        where
            | Literal.Record(r, _, authored) ->
                variantFor idl Map.empty (where + " (default)") r.Fields authored
                |> Result.bind (fun (picks, ms) ->
                    ctorLit idl where (ctorName (slotName (SRecord r.Name)) ("Mk" + variantSuffix picks)) ms authored)
            | Literal.Union(u, args, c, authored) ->
                let subst = TypeParams.bind u args |> Option.defaultValue Map.empty

                slotOf where t
                |> Result.bind (fun s ->
                    variantFor idl subst (where + " (default)") c.Fields authored
                    |> Result.bind (fun (picks, ms) ->
                        ctorLit
                            idl
                            where
                            (ctorName (slotName s) (u.WireTagOf c.Tag + variantSuffix picks))
                            ms
                            authored)))

    /// A constructor literal for a declared default, with each member taken from the authored
    /// field list under its own presence rule — the constructor's argument order is the declared
    /// KEY ORDER the type declaration uses ([[orderMembers]]), so this cannot drift from it.
    and private ctorLit
        (idl: Idl)
        (where: string)
        (ctor: string)
        (ms: Member list)
        (authored: (string * IdlValue) list)
        : Result<string, CodegenError> =
        let sorted = orderMembers idl.Wire.KeyOrder ms

        let rec go acc rest =
            match rest with
            | [] -> Ok(List.rev acc)
            | (m: Member) :: t ->
                let authoredValue =
                    authoredFor m authored
                    |> List.tryPick (fun (n, av) ->
                        if n = m.Name then
                            match av with
                            | VAbsent -> None
                            | _ -> Some av
                        else
                            None)

                // Phase 524 (511.t4) — the F* printer of the one presence rule's LITERAL site. The
                // model's members carry no host-only field, so the only refusal is the required
                // member with no value.
                let part =
                    match m.Slot, Decl.memberValue ValueSite.Literal m.Opt authoredValue with
                    // A synthesised member carries nothing: the wire value is the declaration's.
                    | SConst _, _ -> Ok "()"
                    | _, MemberValue.Absent -> Ok "None"
                    | _, MemberValue.IdentityDefault d -> defaultLit idl where m.Type d
                    | _, MemberValue.Optional av ->
                        defaultLit idl where m.Type av |> Result.map (fun e -> "(Some " + e + ")")
                    | _, MemberValue.Carries av -> defaultLit idl where m.Type av |> Result.map (fun e -> "(" + e + ")")
                    | _, MemberValue.Refused _
                    | _, MemberValue.Placeholder
                    | _, MemberValue.Parameter ->
                        unmodellable ("a default omitting the required member '" + m.Name + "'") where

                match part with
                | Error e -> Error e
                | Ok p -> go (p :: acc) t

        go [] sorted
        |> Result.map (fun parts ->
            match parts with
            | [] -> ctor
            | _ -> "(" + ctor + " " + String.concat " " parts + ")")

    /// One declared field list as the model's members: each wire field's type closed under the
    /// instantiation `subst` and translated ([[slotOf]]), with its presence; host-only fields
    /// dropped, because the model is of the wire. A projection of fields [[Decl]] has already
    /// reached — it reaches nothing itself.
    ///
    /// Phase 722 (DECISIONS.md D283) — a field holding an INLINE record is laid out as the wire lays
    /// it out: the record's members, in its declared order, where the field stands, each under the
    /// holder's instantiation and its own presence. The model therefore states the holder's object
    /// exactly as a vocabulary declaring those members flat would, and the holder's round trip is
    /// the theorem over them; the record has no declaration of its own.
    and private fieldMembers
        (idl: Idl)
        (subst: Map<string, IdlType>)
        (where: string)
        (fs: IdlField list)
        : Result<Member list, CodegenError> =
        let rec go acc rest =
            match rest with
            | [] -> Ok(List.rev acc)
            | (f: IdlField) :: t when f.Opt = HostOnly -> go acc t
            | (f: IdlField) :: t when
                (match Decl.inlineSlot idl f.Type with
                 | Some(InlineSlot.Record _) -> true
                 | _ -> false)
                ->
                match Decl.inlineSlot idl f.Type with
                | Some(InlineSlot.Record r) ->
                    match fieldMembers idl subst (where + "." + f.Name) r.Fields with
                    | Error e -> Error e
                    | Ok laid -> go ((laid |> List.map (fun m -> { m with Held = Some f.Name }) |> List.rev) @ acc) t
                | _ -> go acc t
            | (f: IdlField) :: t ->
                let at = where + "." + f.Name
                let ft = TypeParams.substitute subst f.Type

                let presence =
                    match f.Opt with
                    | Optional -> Ok(Some None)
                    | OmitDefault d -> defaultLit idl at ft d |> Result.map (Some << Some)
                    | Required
                    | HostOnly -> Ok None

                match slotOf at ft with
                | Error e -> Error e
                | Ok slot ->
                    match presence with
                    | Error e -> Error e
                    | Ok p ->
                        go
                            ({ Name = f.Name
                               Type = ft
                               Slot = slot
                               Presence = p
                               Opt = f.Opt
                               Held = None }
                             :: acc)
                            t

        go [] fs

    /// Phase 722 (DECISIONS.md D220.3, D284) — one declared field list as the model's CONSTRUCTORS:
    /// the members [[fieldMembers]] yields, once for each choice of case the list's PRESENCE-KEYED
    /// fields admit, with the choice. A presence-keyed union has no object and no tag on the wire:
    /// the holding object carries the picked case's members and the members that case synthesises,
    /// in the union's roster order where the field stands, and nothing of the union when an optional
    /// one holds no case. So the holder's object has one shape per choice, and the model states
    /// one constructor per choice, each the members of that shape; a list holding no such field is
    /// its one constructor with no pick, exactly as before.
    ///
    /// A synthesised member is a member of the shape at its declared value ([[SConst]]). A case that
    /// declares ANOTHER case's key as a member of its own has no model with the round trip: a value
    /// carrying that member writes both keys, the reading refuses two cases left (D220.3), and
    /// `dec (enc x) == Ok x` is false of that value. The declaration check refuses such a union, and
    /// the target asks that check before it reaches here ([[Decl.layoutFault]]).
    and private fieldVariants
        (idl: Idl)
        (subst: Map<string, IdlType>)
        (where: string)
        (fs: IdlField list)
        : Result<(Pick list * Member list) list, CodegenError> =
        let alternatives (f: IdlField) (u: IdlUnion) (keyed: KeyedCase list) =
            let at = where + "." + f.Name
            let roster = Decl.keyedRoster u

            let position (name: string) =
                roster |> List.tryFindIndex ((=) name) |> Option.defaultValue roster.Length

            let synthesisedMember (case: IdlUnionCase) (name: string, wire: JVal) : Result<Member, CodegenError> =
                match jvalTerm (inKeyOrder idl.Wire.KeyOrder wire) with
                | Some term ->
                    Ok
                        { Name = name
                          Type =
                            u.Cases
                            |> List.tryPick (fun o -> o.Fields |> List.tryFind (fun g -> g.Name = name))
                            |> Option.map _.Type
                            |> Option.defaultValue TJson
                          Slot = SConst term
                          Presence = None
                          Opt = Required
                          Held = None }
                | None ->
                    unmodellable
                        (sprintf
                            "the synthesised member '%s' of union '%s' case '%s', whose value holds a number the opaque numeric model cannot spell"
                            name
                            u.Name
                            case.Tag)
                        at

            let ofCase (case: IdlUnionCase) : Result<Pick list * Member list, CodegenError> =
                // A case declaring another case's key as a member of its own has no round trip (a value
                // carrying it writes both keys). `Declare.layoutErrors` refuses it (Phase 722, D285.6),
                // and this target asks that check before it lays a declaration out, so none arrives here.
                match keyed |> List.tryFind (fun k -> k.Case = case.Tag) with
                | None ->
                    unmodellable
                        (sprintf "a presence-keyed union '%s' naming no key for its case '%s'" u.Name case.Tag)
                        at
                | Some rule ->
                    fieldMembers idl subst (at + "." + case.Tag) case.Fields
                    |> Result.bind (fun own ->
                        Decl.synthesisedJson idl u rule
                        |> Result.bind (List.map (synthesisedMember case) >> sequenceR)
                        |> Result.map (fun synthesised ->
                            [ { Field = f.Name
                                Union = u.Name
                                Case = Some case.Tag } ],
                            (own |> List.map (fun m -> { m with Held = Some f.Name })) @ synthesised
                            |> List.sortBy (fun m -> position m.Name)))

            (if f.Opt = Optional then
                 [ Ok(
                       [ { Field = f.Name
                           Union = u.Name
                           Case = None } ],
                       []
                   ) ]
             else
                 [])
            @ (u.Cases |> List.map ofCase)
            |> sequenceR

        let rec go (acc: (Pick list * Member list) list) (rest: IdlField list) =
            match rest with
            | [] -> Ok acc
            | (f: IdlField) :: t ->
                let here =
                    match
                        (if f.Opt = HostOnly then
                             None
                         else
                             Decl.inlineSlot idl f.Type)
                    with
                    | Some(InlineSlot.Keyed(u, keyed)) -> alternatives f u keyed
                    | _ -> fieldMembers idl subst where [ f ] |> Result.map (fun ms -> [ [], ms ])

                match here with
                | Error e -> Error e
                | Ok alts ->
                    go
                        [ for picks, ms in acc do
                              for picks', ms' in alts -> picks @ picks', ms @ ms' ]
                        t

        go [ [], [] ] fs

    /// The constructor of [[fieldVariants]] an authored value of the holder is a value of: the one
    /// whose picks are the cases the value carries at its presence-keyed fields.
    and private variantFor
        (idl: Idl)
        (subst: Map<string, IdlType>)
        (where: string)
        (fs: IdlField list)
        (authored: (string * IdlValue) list)
        : Result<Pick list * Member list, CodegenError> =
        fieldVariants idl subst where fs
        |> Result.bind (fun variants ->
            match
                variants
                |> List.tryFind (fun (picks, _) ->
                    picks |> List.forall (fun p -> authoredCase p.Field authored = p.Case))
            with
            | Some v -> Ok v
            | None ->
                unmodellable
                    "a value holding a case its presence-keyed union does not declare, or none where one is required"
                    where)

    /// One constructor of a holder of presence-keyed unions: the wire tag its object carries (its
    /// kind's or its case's — every constructor of one holder carries the same) and the cases it holds.
    type Variant = { Tag: string; Picks: Pick list }

    /// One presence-keyed union as the model reads it (D220.3): each case with its key and the keys
    /// of the other cases that SYNTHESISE that key, in declaration order.
    type Keyed =
        { Union: string
          Cases: (string * string * string list) list }

    /// The model of one kind selection: the declared types [[Decl]]'s wire reach yields, each
    /// with its members, in the reach's first-reach order. Every member list is in the
    /// vocabulary's declared KEY ORDER (Phase 293, [[orderMembers]]).
    type Model =
        {
            Order: Slot list
            /// Record slot name to its constructors, (label, members): `Mk` alone, or one per choice
            /// of case when the record holds a presence-keyed union (Phase 722).
            Records: Map<string, (string * Member list) list>
            /// Union slot name to its constructors, (label, members), in declaration order. The label
            /// is the case's WIRE tag (the case name under the default tag rule; Phase 550), suffixed
            /// per choice of case when the case holds a presence-keyed union ([[Variants]]).
            Cases: Map<string, (string * Member list) list>
            /// The union slot names whose declared transparent case the model honours.
            Transparent: Map<string, string>
            Enums: string list
            /// The kinds' constructors, (label, members): the kind's tag, suffixed per choice of case
            /// when the kind holds a presence-keyed union ([[Variants]]).
            Kinds: (string * Member list) list
            /// Phase 722 — the constructors that hold a presence-keyed union, by owner (`vkind`, or
            /// the record's or union's slot name) and label. A constructor absent from it holds none
            /// and its label is its wire tag.
            Variants: Map<string * string, Variant>
            /// The presence-keyed unions the selection reaches, in first-reach order.
            Keyed: Keyed list
            Envelope: Member list
            /// Phase 293 — the declared wire shape the model encodes: the node envelope's
            /// shape and the key order. Carried so the emitters need no second look at the IDL.
            Shape: NodeEnvelopeShape
            KeyOrder: KeyOrder
            /// Phase 525 — whether a member is a hosted slot modelled at its DECLARED wire form,
            /// which the header then states as part of the model's boundary.
            DeclaredWire: bool
        }

    /// Phase 293 — the model's node object under the FLAT envelope: the kind's members and
    /// the envelope's share one object, kind members first as `Encode.encodeNodeEnv` builds it,
    /// and the whole list is then put in the declared key order.
    let flatNodeMembers (c: Model) (kindMembers: Member list) =
        orderMembers c.KeyOrder (kindMembers @ c.Envelope)

    /// The wire tag the constructor `label` of `owner` carries: its label, unless it is one of the
    /// constructors a holder of a presence-keyed union has ([[Model.Variants]]).
    let wireTag (c: Model) (owner: string) (label: string) : string =
        match c.Variants.TryFind((owner, label)) with
        | Some v -> v.Tag
        | None -> label

    /// The cases the constructor `label` of `owner` holds at its presence-keyed fields.
    let picksOf (c: Model) (owner: string) (label: string) : Pick list =
        match c.Variants.TryFind((owner, label)) with
        | Some v -> v.Picks
        | None -> []

    /// The emitted names of one presence-keyed union's READING: the type of its answers, the
    /// function that reads it off an object, and the answer a pick is.
    let pickType (union: string) : string = "pk_" + slotName (SUnion(union, []))

    let pickFunction (union: string) : string = "pick_" + slotName (SUnion(union, []))

    let pickAnswer (p: Pick) : string =
        match p.Case with
        | Some case -> sprintf "PK__%s__%s" (slotName (SUnion(p.Union, []))) case
        | None -> "PN__" + slotName (SUnion(p.Union, []))

    /// The answer when two cases are left — the both-present refusal.
    let pickBoth (union: string) : string = "PB__" + slotName (SUnion(union, []))

    /// D220.3 over the keys an object carries: the cases whose key is present, less each whose key
    /// another present case synthesises. `Ok None` when none is left, the case when one is, and
    /// `Error` when two are.
    let readPick (k: Keyed) (has: string -> bool) : Result<string option, unit> =
        match k.Cases |> List.filter (fun (_, key, by) -> has key && not (List.exists has by)) with
        | [] -> Ok None
        | [ (case, _, _) ] -> Ok(Some case)
        | _ -> Error()

    /// The constructor among `ctors` of `owner` that a value with the wire tag `tag` is a value of,
    /// `held` answering the case it holds at a presence-keyed field (`None` when the reading has no
    /// answer there, which no constructor is).
    let ctorFor
        (c: Model)
        (owner: string)
        (ctors: (string * Member list) list)
        (tag: string)
        (held: Pick -> string option option)
        : (string * Member list) option =
        ctors
        |> List.tryFind (fun (label, _) ->
            wireTag c owner label = tag
            && picksOf c owner label |> List.forall (fun p -> held p = Some p.Case))

    // -----------------------------------------------------------------------
    // 5. The model over `Decl` — the reach is Decl's; this projects it.
    // -----------------------------------------------------------------------

    /// The model of `kindTags` over `idl`, or the FIRST refusal its declarations meet. The reach,
    /// its order and the instantiations are [[Decl.resolve]]'s (Phase 525, carrying Phase 511's
    /// task 2: the target's own slot walk is gone). What is decided here is what the model can
    /// express: each reached declaration is translated in the order it was reached — the envelope,
    /// what it reaches, the selected kinds, what they reach — so the refusal a kind is named with
    /// is the first one met in that order.
    let private modelOf (idl: Idl) (kindTags: string list) : Result<Model, CodegenError> =
        let resolved = Decl.resolve idl Map.empty kindTags
        let reach = resolved.Wire
        let mutable envelopeMembers: Member list = []

        let recordMap =
            System.Collections.Generic.Dictionary<string, (string * Member list) list>()

        let caseMap =
            System.Collections.Generic.Dictionary<string, (string * Member list) list>()

        let transparent = System.Collections.Generic.Dictionary<string, string>()
        let variants = System.Collections.Generic.Dictionary<string * string, Variant>()
        let keyedUnions = ResizeArray<Keyed>()

        // One holder's constructors: `label` suffixed per choice of case ([[variantSuffix]]), each
        // one that holds a presence-keyed union recorded under `owner` with the wire tag it carries.
        let constructors (owner: string) (label: string) (found: (Pick list * Member list) list) =
            found
            |> List.map (fun (picks, ms) ->
                let name = label + variantSuffix picks

                if not picks.IsEmpty then
                    variants[(owner, name)] <- { Tag = label; Picks = picks }

                name, ms)

        let rec sequence (steps: (unit -> Result<unit, CodegenError>) list) =
            match steps with
            | [] -> Ok()
            | step :: rest -> step () |> Result.bind (fun () -> sequence rest)

        // One reached declaration's members, recorded under its slot name. A node, a list or a
        // map declares no members of its own.
        let declare (t: IdlType) () : Result<unit, CodegenError> =
            match t with
            | TRecord(n, _) ->
                match CodegenLookup.tryRecord idl n with
                | None -> unmodellable ("an undeclared record '" + n + "'") ("record " + n)
                // Phase 722 — an inline record's members sit on its holder's object, where
                // [[fieldMembers]] lays them out: it declares nothing of its own.
                | Some r when r.Inline -> Ok()
                | Some r ->
                    fieldVariants idl Map.empty ("record " + n) r.Fields
                    |> Result.map (fun found ->
                        let key = slotName (SRecord n)
                        recordMap[key] <- constructors key "Mk" found)
            | TUnion(n, args) ->
                match CodegenLookup.tryUnion idl n with
                | None -> unmodellable ("an undeclared union '" + n + "'") ("union " + n)
                // Phase 722 — a presence-keyed union has no tag and no object of its own, so it
                // declares no type: its cases are laid out on each holder ([[fieldVariants]]). What
                // it does declare is its READING, which the decoder of every holder asks.
                | Some { Discrimination = UnionDiscrimination.ByPresence keyed } ->
                    if keyedUnions |> Seq.forall (fun k -> k.Union <> n) then
                        keyedUnions.Add
                            { Union = n
                              Cases =
                                keyed
                                |> List.map (fun k ->
                                    k.Case,
                                    k.Key,
                                    keyed
                                    |> List.filter (fun o ->
                                        o.Case <> k.Case && o.Synthesised |> List.exists (fun (m, _) -> m = k.Key))
                                    |> List.map _.Key) }

                    Ok()
                | Some u ->
                    match TypeParams.bind u args, slotOf ("union " + n) t with
                    | None, _ -> unmodellable ("a union arity mismatch on '" + n + "'") ("union " + n)
                    | _, Error e -> Error e
                    | Some subst, Ok s ->
                        let key = slotName s

                        u.Cases
                        |> List.fold
                            (fun acc c ->
                                match acc with
                                | Error e -> Error e
                                | Ok cs ->
                                    fieldVariants idl subst (n + "." + c.Tag) c.Fields
                                    |> Result.map (fun found ->
                                        List.rev (constructors key (u.WireTagOf c.Tag) found) @ cs))
                            (Ok [])
                        |> Result.map List.rev
                        |> Result.bind (fun cs ->
                            caseMap[key] <- cs

                            // Phase 550 — the model is of the WIRE: each case is keyed by its wire tag,
                            // the transparent case among them.
                            match TransparentUnion.tag idl.Harden u |> Option.map u.WireTagOf with
                            | None -> Ok()
                            | Some tag ->
                                // A transparent case encodes BARE, so its decoder arm reads the
                                // value itself rather than a member of it. That arm makes no
                                // recursive call only when the case carries one scalar-shaped
                                // member; anything else would decode `el` at its own size and
                                // the model would not terminate.
                                match cs |> List.tryFind (fun (t, _) -> t = tag) with
                                | Some(_, [ m ]) when
                                    (match m.Slot with
                                     | SStr
                                     | SInt
                                     | SBool
                                     | SFloat
                                     | SSentinel _
                                     | SEnum _ -> true
                                     | _ -> false)
                                    ->
                                    transparent[key] <- tag
                                    Ok()
                                | _ ->
                                    unmodellable
                                        ("a transparent union case that is not a single scalar member ('" + tag + "')")
                                        ("union " + n))
            | _ -> Ok()

        let envelope () =
            fieldMembers idl Map.empty "node envelope" idl.NodeFields
            |> Result.map (fun ms -> envelopeMembers <- ms)

        // Phase 722 — an inline declaration is modelled as the declaration check leaves it: where it
        // stands, one key per case that is a required member of that case and of no other, and what
        // a case may synthesise. The target is handed an `Idl` value, not a declaration that was
        // checked, so it asks before it lays one out: the one question every backend asks
        // ([[Decl.layoutFault]]).
        let layout () =
            match Decl.layoutFault resolved with
            | None -> Ok()
            | Some fault -> unmodellable (Decl.layoutFaultConstruct fault) "the vocabulary"

        let kinds () =
            kindTags
            |> List.fold
                (fun acc tag ->
                    match acc with
                    | Error e -> Error e
                    | Ok ks ->
                        match CodegenLookup.tryKind idl tag with
                        | None -> unmodellable ("an undeclared kind '" + tag + "'") ("kind " + tag)
                        | Some k ->
                            fieldVariants idl Map.empty ("kind " + tag) k.Fields
                            |> Result.map (fun found -> List.rev (constructors "vkind" tag found) @ ks))
                (Ok [])
            |> Result.map (fun ks -> caseMap["vkind"] <- List.rev ks)

        let translated =
            sequence (
                [ layout; envelope ]
                @ (reach.FromEnvelope |> List.map declare)
                @ [ kinds ]
                @ (reach.FromKinds |> List.map declare)
            )
            |> Result.bind (fun () ->
                // Every reached type has been met as a member's type by now, so none refuses.
                let rec slots acc rest =
                    match rest with
                    | [] -> Ok(List.rev acc)
                    | t :: tail ->
                        match slotOf "the wire reach" t with
                        | Error e -> Error e
                        | Ok s -> slots (s :: acc) tail

                // Phase 722 — an inline declaration is no slot: it is laid out on each holder.
                slots
                    []
                    (reach.FromEnvelope @ reach.FromKinds
                     |> List.filter (fun t -> (Decl.inlineSlot idl t).IsNone))
                // Phase 639 — a set is modelled as a list, so a set and a list of one element type
                // are one slot, declared once.
                |> Result.map (List.distinctBy slotName))

        match translated with
        | Error e -> Error e
        | Ok order ->
            // Phase 293 — the shape the model cannot express is REFUSED by name, never emitted. The
            // model's objects are literals, one entry per key, read back by `find_field` — so a key
            // that would appear twice in one object is a document the model cannot round-trip. Under
            // the FLAT envelope the node object carries the discriminator, `id`, the kind's members
            // and the envelope's; under the NESTED one the node object carries `id`, `kind` and the
            // envelope's members, and the kind object the discriminator and the kind's. A union
            // case's object carries the discriminator beside its members in both shapes.
            // `Declare.wireShapeErrors` refuses most of these at declaration; the target refuses them
            // again because it is handed an `Idl` value, not a declaration that was checked.
            let disc = idl.Wire.Discriminator
            let envelope = envelopeMembers
            let kinds = caseMap["vkind"]

            let collisions (where: string) (reserved: (string * string) list) (ms: Member list) =
                ms
                |> List.tryPick (fun m ->
                    reserved
                    |> List.tryFind (fun (key, _) -> key = m.Name)
                    |> Option.map (fun (_, holder) ->
                        CodegenError.UnmodellableInFStar(
                            sprintf "a member '%s' whose key the %s already carries" m.Name holder,
                            where
                        )))

            let nodeKeys =
                match idl.Wire.NodeEnvelope with
                | NodeEnvelopeShape.FlatKind -> [ disc, "node object"; "id", "node object" ]
                | NodeEnvelopeShape.NestedKind -> [ "id", "node object"; "kind", "node object" ]

            let kindFailure () =
                kinds
                |> List.tryPick (fun (tag, ms) ->
                    match idl.Wire.NodeEnvelope with
                    | NodeEnvelopeShape.FlatKind ->
                        collisions
                            ("kind " + tag)
                            (nodeKeys @ (envelope |> List.map (fun m -> m.Name, "node envelope")))
                            ms
                    | NodeEnvelopeShape.NestedKind -> collisions ("kind " + tag) [ disc, "kind object" ] ms)

            let unionFailure () =
                caseMap
                |> Seq.tryPick (fun kv ->
                    if kv.Key = "vkind" then
                        None
                    else
                        kv.Value
                        |> List.tryPick (fun (tag, ms) ->
                            match transparent.TryGetValue kv.Key with
                            | true, t when t = tag -> None
                            | _ -> collisions (sprintf "union %s case %s" kv.Key tag) [ disc, "case object" ] ms))

            // Phase 722 — a constructor's name is its label, and a holder of a presence-keyed union
            // has one per choice of case: two of one owner spelled alike would be one F* name.
            let labelFailure () =
                Seq.append caseMap recordMap
                |> Seq.tryPick (fun kv ->
                    kv.Value
                    |> List.countBy fst
                    |> List.tryFind (fun (_, n) -> n > 1)
                    |> Option.map (fun (label, _) ->
                        CodegenError.UnmodellableInFStar(sprintf "two constructors named '%s'" label, kv.Key)))

            let shapeFailure =
                collisions "node envelope" nodeKeys envelope
                |> Option.orElseWith kindFailure
                |> Option.orElseWith unionFailure
                |> Option.orElseWith labelFailure

            match shapeFailure with
            | Some e -> Error e
            | None ->
                let ordered = orderMembers idl.Wire.KeyOrder

                let rec declaredWire (t: IdlType) =
                    match t with
                    | THosted h -> h.Wire.IsSome
                    | TList inner
                    | TMap inner -> declaredWire inner
                    | TUnion(_, args) -> args |> List.exists declaredWire
                    | _ -> false

                let everyMember =
                    Seq.append caseMap.Values recordMap.Values
                    |> Seq.concat
                    |> Seq.collect snd
                    |> Seq.append envelope

                Ok
                    { Order = order
                      Records =
                        recordMap
                        |> Seq.map (fun kv -> kv.Key, kv.Value |> List.map (fun (t, ms) -> t, ordered ms))
                        |> Map.ofSeq
                      Cases =
                        caseMap
                        |> Seq.map (fun kv -> kv.Key, kv.Value |> List.map (fun (t, ms) -> t, ordered ms))
                        |> Map.ofSeq
                      Transparent = transparent |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                      Enums = reach.Enums
                      Kinds = kinds |> List.map (fun (t, ms) -> t, ordered ms)
                      Variants = variants |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                      Keyed = List.ofSeq keyedUnions
                      Envelope = ordered envelope
                      Shape = idl.Wire.NodeEnvelope
                      KeyOrder = idl.Wire.KeyOrder
                      DeclaredWire = everyMember |> Seq.exists (fun m -> declaredWire m.Type) }


    /// [[modelOf]] behind the Phase 539 refusal: the model's decoder reads each member under its one
    /// name and has no presence condition, so a vocabulary declaring either (or an enum alias) is
    /// refused by name rather than modelled as if it declared neither. And behind Phase 732's: a
    /// type-parameterised record is not modelled yet ([[Decl.parameterisedRecord]]).
    let model (idl: Idl) (kindTags: string list) : Result<Model, CodegenError> =
        match Decl.canonicalFormDeclarations idl, Decl.parameterisedRecord idl with
        | what :: _, _ -> unmodellable (what + " (a Phase 539 canonical-form declaration)") "the vocabulary"
        | [], Some name -> unmodellable (Decl.parameterisedRecordConstruct name) "the vocabulary"
        | [], None -> modelOf idl kindTags

    // -----------------------------------------------------------------------
    // 6. The expressibility partition — which of a vocabulary's kinds the target can
    //    model, and why each of the others cannot be. Computed, never declared: a kind
    //    that becomes expressible enters the model at the next regeneration, and one
    //    that does not is NAMED in the emitted header rather than silently missing.
    // -----------------------------------------------------------------------




    /// The implementation of `FStarTarget.partition` — the published entry, which carries its contract.
    let partition (idl: Idl) : KindVerdict list =
        idl.Kinds
        |> List.map (fun k ->
            match model idl [ k.Tag ] with
            | Ok _ -> { Tag = k.Tag; Refusal = None }
            | Error e ->
                { Tag = k.Tag
                  Refusal = Some(CodegenError.describe e) })

    /// The DECLARED TYPES a selection reaches — the slots the emitted module declares a type
    /// for, which is what decides the size of the mutual family every query carries.
    let private declaredTypes (idl: Idl) (kindTags: string list) : Set<string> =
        match model idl kindTags with
        | Error _ -> Set.empty
        | Ok c -> c.Order |> List.filter declares |> List.map slotName |> Set.ofList

    /// The implementation of `FStarTarget.beyondEnvelope` — the published entry, which carries its contract.
    let beyondEnvelope (idl: Idl) (tag: string) : string list =
        let envelope = declaredTypes idl []
        declaredTypes idl [ tag ] - envelope |> Set.toList

    /// The implementation of `FStarTarget.proofKinds` — the published entry, which carries its contract.
    let proofKinds (idl: Idl) : string list =
        partition idl
        |> List.filter _.Refusal.IsNone
        |> List.map _.Tag
        |> List.filter (fun tag -> List.isEmpty (beyondEnvelope idl tag))
