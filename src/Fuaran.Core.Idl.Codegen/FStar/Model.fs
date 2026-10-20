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
        }

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
    let rec private slotOf (where: string) (t: IdlType) : Result<Slot, CodegenError> =
        match t with
        | TStr -> Ok SStr
        | TInt -> Ok SInt
        | TBool -> Ok SBool
        | TFloat -> Ok SFloat
        | TJson -> Ok SJson
        | TNode -> Ok SNode
        | TEnum n -> Ok(SEnum n)
        | TRecord n -> Ok(SRecord n)
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

    /// The F* TYPE EXPRESSION of a slot. Every declared type is parameterised by the two
    /// opaque numeric carriers, because `jval` is — see `WireDecode`'s header on opacity.
    let rec slotType (s: Slot) : string =
        match s with
        | SStr -> "string"
        | SInt -> "num"
        | SBool -> "bool"
        | SFloat -> "flt"
        | SJson -> "jval num flt"
        | SSentinel _ -> "unit"
        | SEnum n -> "e_" + snake n
        | SRecord _
        | SUnion _
        | SNode -> slotName s + " num flt"
        | SList inner -> "list (" + slotType inner + ")"
        | SMap inner -> "list (string & " + slotType inner + ")"

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
            | Literal.Json j ->
                let rec term (j: JVal) : string option =
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
                        all (List.map term xs)
                        |> Option.map (fun ts -> "(JArr [" + String.concat "; " ts + "])")
                    | JObj ms ->
                        all (
                            ms
                            |> List.map (fun (k, x) -> term x |> Option.map (fun t -> "(" + lit k + ", " + t + ")"))
                        )
                        |> Option.map (fun ts -> "(JObj [" + String.concat "; " ts + "])")

                match term j with
                | Some t -> Ok t
                | None ->
                    unmodellable
                        (sprintf "a declared JSON default holding a number the opaque numeric model cannot spell: %A" v)
                        where
            | Literal.Record(r, authored) ->
                fieldMembers idl Map.empty (where + " (default)") r.Fields
                |> Result.bind (fun ms -> ctorLit idl where (ctorName (slotName (SRecord r.Name)) "Mk") ms authored)
            | Literal.Union(u, args, c, authored) ->
                let subst = TypeParams.bind u args |> Option.defaultValue Map.empty

                slotOf where t
                |> Result.bind (fun s ->
                    fieldMembers idl subst (where + " (default)") c.Fields
                    |> Result.bind (fun ms ->
                        ctorLit idl where (ctorName (slotName s) (u.WireTagOf c.Tag)) ms authored)))

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
                    authored
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
                    match Decl.memberValue ValueSite.Literal m.Opt authoredValue with
                    | MemberValue.Absent -> Ok "None"
                    | MemberValue.IdentityDefault d -> defaultLit idl where m.Type d
                    | MemberValue.Optional av ->
                        defaultLit idl where m.Type av |> Result.map (fun e -> "(Some " + e + ")")
                    | MemberValue.Carries av -> defaultLit idl where m.Type av |> Result.map (fun e -> "(" + e + ")")
                    | MemberValue.Refused _
                    | MemberValue.Placeholder
                    | MemberValue.Parameter ->
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
                               Opt = f.Opt }
                             :: acc)
                            t

        go [] fs

    /// The model of one kind selection: the declared types [[Decl]]'s wire reach yields, each
    /// with its members, in the reach's first-reach order. Every member list is in the
    /// vocabulary's declared KEY ORDER (Phase 293, [[orderMembers]]).
    type Model =
        {
            Order: Slot list
            Members: Map<string, Member list>
            /// Union slot name to (case WIRE tag, members) in declaration order (the case name under
            /// the default tag rule; Phase 550).
            Cases: Map<string, (string * Member list) list>
            /// The union slot names whose declared transparent case the model honours.
            Transparent: Map<string, string>
            Enums: string list
            Kinds: (string * Member list) list
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
        let reach = (Decl.resolve idl Map.empty kindTags).Wire
        let memberMap = System.Collections.Generic.Dictionary<string, Member list>()

        let caseMap =
            System.Collections.Generic.Dictionary<string, (string * Member list) list>()

        let transparent = System.Collections.Generic.Dictionary<string, string>()

        let rec sequence (steps: (unit -> Result<unit, CodegenError>) list) =
            match steps with
            | [] -> Ok()
            | step :: rest -> step () |> Result.bind (fun () -> sequence rest)

        // One reached declaration's members, recorded under its slot name. A node, a list or a
        // map declares no members of its own.
        let declare (t: IdlType) () : Result<unit, CodegenError> =
            match t with
            | TRecord n ->
                match CodegenLookup.tryRecord idl n with
                | None -> unmodellable ("an undeclared record '" + n + "'") ("record " + n)
                | Some r ->
                    fieldMembers idl Map.empty ("record " + n) r.Fields
                    |> Result.map (fun ms -> memberMap[slotName (SRecord n)] <- ms)
            | TUnion(n, args) ->
                match CodegenLookup.tryUnion idl n with
                | None -> unmodellable ("an undeclared union '" + n + "'") ("union " + n)
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
                                    fieldMembers idl subst (n + "." + c.Tag) c.Fields
                                    |> Result.map (fun ms -> (u.WireTagOf c.Tag, ms) :: cs))
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
            |> Result.map (fun ms -> memberMap["node"] <- ms)

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
                            fieldMembers idl Map.empty ("kind " + tag) k.Fields
                            |> Result.map (fun ms -> (tag, ms) :: ks))
                (Ok [])
            |> Result.map (fun ks -> caseMap["vkind"] <- List.rev ks)

        let translated =
            sequence (
                [ envelope ]
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

                slots [] (reach.FromEnvelope @ reach.FromKinds))

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
            let envelope = memberMap["node"]
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

            let shapeFailure =
                collisions "node envelope" nodeKeys envelope
                |> Option.orElseWith kindFailure
                |> Option.orElseWith unionFailure

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
                    Seq.append (Seq.concat memberMap.Values) (caseMap.Values |> Seq.concat |> Seq.collect snd)

                Ok
                    { Order = order
                      Members = memberMap |> Seq.map (fun kv -> kv.Key, ordered kv.Value) |> Map.ofSeq
                      Cases =
                        caseMap
                        |> Seq.map (fun kv -> kv.Key, kv.Value |> List.map (fun (t, ms) -> t, ordered ms))
                        |> Map.ofSeq
                      Transparent = transparent |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                      Enums = reach.Enums
                      Kinds = kinds |> List.map (fun (t, ms) -> t, ordered ms)
                      Envelope = ordered envelope
                      Shape = idl.Wire.NodeEnvelope
                      KeyOrder = idl.Wire.KeyOrder
                      DeclaredWire = everyMember |> Seq.exists (fun m -> declaredWire m.Type) }


    /// [[modelOf]] behind the Phase 539 refusal: the model's decoder reads each member under its one
    /// name and has no presence condition, so a vocabulary declaring either (or an enum alias) is
    /// refused by name rather than modelled as if it declared neither.
    let model (idl: Idl) (kindTags: string list) : Result<Model, CodegenError> =
        match Decl.canonicalFormDeclarations idl with
        | what :: _ -> unmodellable (what + " (a Phase 539 canonical-form declaration)") "the vocabulary"
        | [] -> modelOf idl kindTags

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
