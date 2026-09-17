namespace Fuaran.Core.Idl

open Fuaran.Core

// ---------------------------------------------------------------------------
// The F* PROOF-MODEL target (Phase 150) — the fourth backend of `Fuaran.Core.Idl.Codegen`,
// beside the F# structural layer, the TypeScript encoder and the JSON schema.
//
// WHAT IT EMITS, AND WHAT IT DELIBERATELY DOES NOT. It emits a PROOF MODEL: an F*
// module carrying a vocabulary's types, its discriminated encoder and its
// tag-dispatch decoder over Phase 135's `jval` value model and the `Decode`
// combinators modelled beside it — plus a second module carrying the round-trip and
// totality THEOREMS over exactly those definitions. It is not a host backend: no
// runtime package gains code, no host's decoder is replaced, and no build anywhere
// gains a generation step. The one artefact it produces is checked by the prover and
// read by nobody at runtime.
//
// WHY THE THEOREMS ARE GENERATED TOO. Phase 135 proved `decode (encode n) == Ok n` for
// a hand-written four-case reference vocabulary. The real vocabulary is an `idl.json`
// with dozens of kinds, and a theorem about the reference one says nothing about the
// kind that landed last week. A hand-written proof over the real vocabulary would say
// nothing about the one that lands next week either — so the proof script is emitted by
// the same walk that emits the model, and a new kind re-proves itself at the next
// `check.ps1` rather than waiting for a hand-written clause.
//
// WHY MONOMORPHISATION (the measured decision — see the module's own header). The IDL's
// `Binding<'T>` is generic and nearly every kind reaches it. Emitting it as a PARAMETRIC
// F* type would force an encoder/decoder pair to be threaded as a value argument, and the
// round-trip lemma would then need a higher-order hypothesis relating them — an induction
// over an unknown application. Instantiating it once per reached argument keeps every
// definition and every lemma FIRST-ORDER, which is what makes the emitted proof script a
// mechanical shape rather than a research problem.
//
// THE REFUSAL IS TYPED, like every other refusal in this package: an IDL construct with no
// wire-level meaning in the model (a closure in a wire-visible slot, a host codec whose
// wire is not JSON, an op or a bare kind slot) is a `CodegenError.UnmodellableInFStar`
// naming the construct and where it was reached — never a silently dropped member, which
// would make the theorem a statement about a document nobody sends.
// ---------------------------------------------------------------------------

/// The F* proof-model backend. `Gen` emits source a consumer compiles and ships; this
/// emits source a PROVER checks, which is why it is a module of its own rather than a
/// fourth function beside the three in `Gen`.
[<RequireQualifiedAccess>]
module FStarTarget =

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
    type private Member =
        {
            Name: string
            Slot: Slot
            /// `None` for a required member; `Some None` for an optional one; `Some (Some lit)`
            /// for an omit-default whose default renders as the F* literal `lit`.
            Presence: string option option
        }

    // -----------------------------------------------------------------------
    // 2. Resolution — IDL type to slot, with the refusals.
    // -----------------------------------------------------------------------

    let private unmodellable (construct: string) (where: string) =
        Error(CodegenError.UnmodellableInFStar(construct, where))

    let rec private resolve (subst: Map<string, Slot>) (where: string) (t: IdlType) : Result<Slot, CodegenError> =
        match t with
        | TStr -> Ok SStr
        | TInt -> Ok SInt
        | TBool -> Ok SBool
        | TFloat -> Ok SFloat
        | TJson -> Ok SJson
        | TNode -> Ok SNode
        | TEnum n -> Ok(SEnum n)
        | TRecord n -> Ok(SRecord n)
        | TList inner -> resolve subst where inner |> Result.map SList
        | TMap vt -> resolve subst where vt |> Result.map SMap
        | TVar v ->
            match Map.tryFind v subst with
            | Some s -> Ok s
            | None -> unmodellable (sprintf "an unresolved type parameter '%s" v) where
        | TUnion(n, args) ->
            let rec go acc rest =
                match rest with
                | [] -> Ok(List.rev acc)
                | a :: t ->
                    match resolve subst where a with
                    | Error e -> Error e
                    | Ok s -> go (s :: acc) t

            go [] args |> Result.map (fun ss -> SUnion(n, ss))
        | THosted h ->
            // A host codec whose wire is JSON is carried verbatim; one that is not has no
            // wire-level meaning to model at all.
            if h.FSharp = "" then
                unmodellable "a host codec with no declared host type" where
            else
                Ok SJson
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
    let private snake (s: string) : string =
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
    let rec private slotName (s: Slot) : string =
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
    let rec private slotType (s: Slot) : string =
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

    /// An F* string literal.
    let private lit (s: string) : string =
        let sb = System.Text.StringBuilder()
        sb.Append '"' |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append "\\\"" |> ignore
            | '\\' -> sb.Append "\\\\" |> ignore
            | '\n' -> sb.Append "\\n" |> ignore
            | '\r' -> sb.Append "\\r" |> ignore
            | '\t' -> sb.Append "\\t" |> ignore
            | c -> sb.Append c |> ignore

        sb.Append '"' |> ignore
        sb.ToString()

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
    let private binderName (name: string) : string =
        let s = snake name
        if reserved.Contains s then s + "_" else s

    /// A constructor name — uppercase-initial and unique across the module, which is what
    /// the `<owner>__<case>` shape buys: two unions may each declare a `Static`.
    let private ctorName (owner: string) (case: string) : string = "C__" + owner + "__" + case

    // -----------------------------------------------------------------------
    // 4. The closure — every slot the selected kinds reach, monomorphised.
    // -----------------------------------------------------------------------

    /// Whether a slot is one the emitted module DECLARES a type for (and therefore one the
    /// mutual recursion has to carry), as opposed to a structural spelling over others.
    let private declares (s: Slot) =
        match s with
        | SRecord _
        | SUnion _
        | SNode -> true
        | _ -> false

    /// Does this slot reach a declared type? A walker over such a slot joins the mutual
    /// group; one that does not is emitted standalone, which keeps the group as small as
    /// the vocabulary actually requires.
    let rec private reachesDeclared (s: Slot) =
        match s with
        | SRecord _
        | SUnion _
        | SNode -> true
        | SList inner
        | SMap inner -> reachesDeclared inner
        | _ -> false

    let private findRecord (idl: Idl) n =
        idl.Records |> List.tryFind (fun r -> r.Name = n)

    let private findUnion (idl: Idl) n =
        idl.Unions |> List.tryFind (fun u -> u.Name = n)

    let private findEnum (idl: Idl) n =
        idl.Enums |> List.tryFind (fun e -> e.Name = n)

    /// The declared default of an omit-default member, as an F* literal — the admissible set
    /// is deliberately narrower than the F# backend's, because the model's `num` and `flt` are
    /// OPAQUE: there is no F* literal for a number whose carrier is a type parameter. A
    /// numeric default is therefore refused here and is not a defect in the declaration.
    let rec private defaultLit (idl: Idl) (where: string) (s: Slot) (v: IdlValue) : Result<string, CodegenError> =
        match s, v with
        | SStr, VStr t -> Ok(lit t)
        | SBool, VBool b -> Ok(if b then "true" else "false")
        | SList _, VList [] -> Ok "[]"
        | SMap _, VMap [] -> Ok "[]"
        | SSentinel _, _ -> Ok "()"
        | SRecord n, VRecord authored ->
            match findRecord idl n with
            | None -> Error(CodegenError.UnsupportedDefault(TRecord n, v))
            | Some r ->
                members idl Map.empty (where + " (default)") r.Fields
                |> Result.bind (fun ms -> ctorLit idl where (ctorName (slotName s) "Mk") ms authored)
        | SUnion(n, args), VUnion(tag, authored) ->
            match findUnion idl n with
            | Some u when List.length u.Params = List.length args ->
                match u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                | None -> Error(CodegenError.UnsupportedDefault(TUnion(n, []), v))
                | Some c ->
                    let subst = Map.ofList (List.zip u.Params args)

                    members idl subst (where + " (default)") c.Fields
                    |> Result.bind (fun ms -> ctorLit idl where (ctorName (slotName s) tag) ms authored)
            | _ -> Error(CodegenError.UnsupportedDefault(TUnion(n, []), v))
        | SEnum n, VEnum spelling ->
            // A `VEnum` carries the WIRE spelling, and an enum that declares no mapping has
            // the two coincide — but a declared default is authored beside the F# backend,
            // which falls THROUGH `CaseOf` to the string it was handed. Both spellings are
            // therefore live in the corpus, and resolving either is the only reading that
            // does not refuse a default the F# backend accepts.
            match findEnum idl n with
            | Some e when List.contains spelling e.Cases -> Ok(ctorName ("e_" + snake n) spelling)
            | Some e ->
                match e.CaseOf spelling with
                | Some case -> Ok(ctorName ("e_" + snake n) case)
                | None -> Error(CodegenError.UnsupportedDefault(TEnum n, v))
            | None -> Error(CodegenError.UnsupportedDefault(TEnum n, v))
        | _ -> unmodellable (sprintf "a declared default the opaque numeric model cannot spell: %A" v) where

    /// A constructor literal for a declared default, with each member taken from the authored
    /// field list under its own presence rule — the constructor's argument order is the SORTED
    /// member order the type declaration uses, so this cannot drift from the declaration.
    and private ctorLit
        (idl: Idl)
        (where: string)
        (ctor: string)
        (ms: Member list)
        (authored: (string * IdlValue) list)
        : Result<string, CodegenError> =
        let sorted =
            ms |> List.sortWith (fun a b -> System.String.CompareOrdinal(a.Name, b.Name))

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

                let part =
                    match authoredValue, m.Presence with
                    | None, Some None -> Ok "None"
                    | None, Some(Some d) -> Ok d
                    | None, None -> unmodellable ("a default omitting the required member '" + m.Name + "'") where
                    | Some av, Some None -> defaultLit idl where m.Slot av |> Result.map (fun e -> "(Some " + e + ")")
                    | Some av, _ -> defaultLit idl where m.Slot av |> Result.map (fun e -> "(" + e + ")")

                match part with
                | Error e -> Error e
                | Ok p -> go (p :: acc) t

        go [] sorted
        |> Result.map (fun parts ->
            match parts with
            | [] -> ctor
            | _ -> "(" + ctor + " " + String.concat " " parts + ")")

    /// One field list resolved to members, host-only fields dropped.
    and private members (idl: Idl) (subst: Map<string, Slot>) (where: string) (fs: IdlField list) =
        let rec go acc rest =
            match rest with
            | [] -> Ok(List.rev acc)
            | (f: IdlField) :: t ->
                match f.Opt with
                | HostOnly -> go acc t
                | _ ->
                    match resolve subst (where + "." + f.Name) f.Type with
                    | Error e -> Error e
                    | Ok slot ->
                        let presence =
                            match f.Opt with
                            | Required -> Ok None
                            | Optional -> Ok(Some None)
                            | OmitDefault d -> defaultLit idl (where + "." + f.Name) slot d |> Result.map (Some << Some)
                            | HostOnly -> Ok None

                        match presence with
                        | Error e -> Error e
                        | Ok p ->
                            go
                                ({ Name = f.Name
                                   Slot = slot
                                   Presence = p }
                                 :: acc)
                                t

        go [] fs

    /// The transitive monomorphic closure of the slots a kind selection reaches, together
    /// with each declared type's resolved members. Ordered by first reach, so the emitted
    /// file's order is a function of the IDL's own declaration order.
    type private Closure =
        {
            Order: Slot list
            Members: Map<string, Member list>
            /// Union slot name to (case tag, members) in declaration order.
            Cases: Map<string, (string * Member list) list>
            /// The union slot names whose declared transparent case the model honours.
            Transparent: Map<string, string>
            Enums: string list
            Kinds: (string * Member list) list
            Envelope: Member list
        }

    // -----------------------------------------------------------------------
    // 5. Walking the vocabulary.
    // -----------------------------------------------------------------------

    let private walk (idl: Idl) (kindTags: string list) : Result<Closure, CodegenError> =
        let order = ResizeArray<Slot>()
        let seen = System.Collections.Generic.HashSet<string>()
        let memberMap = System.Collections.Generic.Dictionary<string, Member list>()

        let caseMap =
            System.Collections.Generic.Dictionary<string, (string * Member list) list>()

        let transparent = System.Collections.Generic.Dictionary<string, string>()
        let enums = ResizeArray<string>()
        let mutable failure: CodegenError option = None

        let fail e =
            if failure.IsNone then
                failure <- Some e

        let rec visitSlot (s: Slot) =
            if failure.IsSome then
                ()
            else
                match s with
                | SStr
                | SInt
                | SBool
                | SFloat
                | SJson
                | SSentinel _ -> ()
                | SEnum n ->
                    if not (enums.Contains n) then
                        enums.Add n
                | SList inner
                | SMap inner ->
                    let key = slotName s

                    if seen.Add key then
                        visitSlot inner
                        order.Add s
                | SNode ->
                    if seen.Add "node" then
                        // The node's own recursion: registered BEFORE its members are walked,
                        // so a kind reaching a node does not re-enter.
                        order.Add SNode
                        visitEnvelope ()
                        visitKinds ()
                | SRecord n ->
                    let key = slotName s

                    if seen.Add key then
                        order.Add s

                        match findRecord idl n with
                        | None ->
                            fail (CodegenError.UnmodellableInFStar("an undeclared record '" + n + "'", "record " + n))
                        | Some r ->
                            match members idl Map.empty ("record " + n) r.Fields with
                            | Error e -> fail e
                            | Ok ms ->
                                memberMap[key] <- ms
                                ms |> List.iter (fun m -> visitSlot m.Slot)
                | SUnion(n, args) ->
                    let key = slotName s

                    if seen.Add key then
                        order.Add s

                        match findUnion idl n with
                        | None ->
                            fail (CodegenError.UnmodellableInFStar("an undeclared union '" + n + "'", "union " + n))
                        | Some u when List.length u.Params <> List.length args ->
                            fail (
                                CodegenError.UnmodellableInFStar("a union arity mismatch on '" + n + "'", "union " + n)
                            )
                        | Some u ->
                            let subst = Map.ofList (List.zip u.Params args)

                            let resolved =
                                u.Cases
                                |> List.fold
                                    (fun acc c ->
                                        match acc with
                                        | Error e -> Error e
                                        | Ok cs ->
                                            members idl subst (n + "." + c.Tag) c.Fields
                                            |> Result.map (fun ms -> (c.Tag, ms) :: cs))
                                    (Ok [])
                                |> Result.map List.rev

                            match resolved with
                            | Error e -> fail e
                            | Ok cs ->
                                caseMap[key] <- cs

                                match TransparentUnion.tag idl.Harden u with
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
                                    | _ ->
                                        fail (
                                            CodegenError.UnmodellableInFStar(
                                                "a transparent union case that is not a single scalar member ('"
                                                + tag
                                                + "')",
                                                "union " + n
                                            )
                                        )
                                | None -> ()

                                cs |> List.iter (fun (_, ms) -> ms |> List.iter (fun m -> visitSlot m.Slot))

        and visitEnvelope () =
            match members idl Map.empty "node envelope" idl.NodeFields with
            | Error e -> fail e
            | Ok ms ->
                memberMap["node"] <- ms
                ms |> List.iter (fun m -> visitSlot m.Slot)

        and visitKinds () =
            let resolved =
                kindTags
                |> List.fold
                    (fun acc tag ->
                        match acc with
                        | Error e -> Error e
                        | Ok ks ->
                            match idl.Kinds |> List.tryFind (fun k -> k.Tag = tag) with
                            | None ->
                                Error(
                                    CodegenError.UnmodellableInFStar("an undeclared kind '" + tag + "'", "kind " + tag)
                                )
                            | Some k ->
                                members idl Map.empty ("kind " + tag) k.Fields
                                |> Result.map (fun ms -> (tag, ms) :: ks))
                    (Ok [])
                |> Result.map List.rev

            match resolved with
            | Error e -> fail e
            | Ok ks ->
                caseMap["vkind"] <- ks
                ks |> List.iter (fun (_, ms) -> ms |> List.iter (fun m -> visitSlot m.Slot))

        visitSlot SNode

        match failure with
        | Some e -> Error e
        | None ->
            Ok
                { Order = List.ofSeq order
                  Members = memberMap |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                  Cases = caseMap |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                  Transparent = transparent |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                  Enums = List.ofSeq enums
                  Kinds =
                    (match caseMap.TryGetValue "vkind" with
                     | true, ks -> ks
                     | _ -> [])
                  Envelope =
                    (match memberMap.TryGetValue "node" with
                     | true, ms -> ms
                     | _ -> []) }

    // -----------------------------------------------------------------------
    // 6. The expressibility partition — which of a vocabulary's kinds the target can
    //    model, and why each of the others cannot be. Computed, never declared: a kind
    //    that becomes expressible enters the model at the next regeneration, and one
    //    that does not is NAMED in the emitted header rather than silently missing.
    // -----------------------------------------------------------------------

    /// One kind's verdict.
    type Verdict = { Tag: string; Refusal: string option }

    /// Where a generated model COMES FROM, for the emitted header — so the artefact says what
    /// it was generated from, what regenerates it and what a theorem over it is a property
    /// OF, and a reader of the `.fst` never has to guess which vocabulary the theorem is
    /// about (fuaran-core Phase 173: the same generator now serves the engine's certification
    /// vocabulary in this repository and a domain's vocabulary in the domain's, and the two
    /// headers must not tell the same story). Pre-wrapped lines, indented by the emitter,
    /// because the caller knows its own paths and its own remedy and the generator does not.
    type Provenance =
        {
            /// The first paragraph: what the model is generated from, what regenerates it, and
            /// what holds the committed file to a fresh generation. Follows "GENERATED by
            /// Fuaran.Core.Idl.Codegen's F* target from" on the header's first line.
            Origin: string list
            /// What is proved here is a property of ... — the clause that closes "WHAT THIS IS
            /// NOT". Names the vocabulary, or the backend, or whatever the caller certifies.
            Proves: string list
        }

    [<RequireQualifiedAccess>]
    module Provenance =
        /// The provenance the un-suffixed entry points emit: honest about the one thing the
        /// generator knows, which is that an IDL was supplied and the committed file has to be
        /// held to it. A caller with a named source and a named remedy passes its own.
        let supplied: Provenance =
            { Origin =
                [ "the IDL its caller supplied. DO NOT EDIT: regenerate it from that IDL and hold the"
                  "committed file to a fresh generation — an IDL that moves without a regeneration is"
                  "VOCABULARY DRIFT." ]
              Proves = [ "VOCABULARY the IDL declares." ] }

    /// Every kind of the vocabulary, each with the reason the target cannot model it, or
    /// `None` when it can. The envelope is common to all of them, so a vocabulary whose
    /// node envelope is unmodellable has no expressible kind at all and says so on each.
    let partition (idl: Idl) : Verdict list =
        idl.Kinds
        |> List.map (fun k ->
            match walk idl [ k.Tag ] with
            | Ok _ -> { Tag = k.Tag; Refusal = None }
            | Error e ->
                { Tag = k.Tag
                  Refusal = Some(CodegenError.describe e) })

    /// The DECLARED TYPES a selection reaches — the slots the emitted module declares a type
    /// for, which is what decides the size of the mutual family every query carries.
    let private declaredTypes (idl: Idl) (kindTags: string list) : Set<string> =
        match walk idl kindTags with
        | Error _ -> Set.empty
        | Ok c -> c.Order |> List.filter declares |> List.map slotName |> Set.ofList

    /// The kinds the PROOF VOCABULARY covers: every kind the target can express **that
    /// introduces no declared type beyond the node envelope's own closure**, in the
    /// vocabulary's declaration order.
    ///
    /// **The second clause is a cost control, and it is stated as one.** The proof leg checks
    /// every module three times from a cold cache with every query proved three times over
    /// varying seeds, and the emitted model's cost turns superlinear in the size of the mutual
    /// family: measured on the pinned prover, the whole expressible vocabulary (42 of this
    /// corpus's 43 kinds) did not finish a single check in twenty-five minutes, where the set
    /// this rule selects costs about what one kind costs — because the node envelope's closure
    /// is paid by ANY kind at all, and a kind that reaches only types already in it adds one
    /// constructor to three definitions and nothing else.
    ///
    /// It is a RULE rather than a list, which is the property that matters: a kind added to the
    /// IDL over types the model already carries enters the proof vocabulary at the next
    /// regeneration and re-proves itself, and one that brings a new object of its own is NAMED
    /// in the emitted header with the reason. Widening it is a measured decision, not an edit —
    /// see `proofs/README.md`, theorem 1's cost note.
    let proofKinds (idl: Idl) : string list =
        let envelope = declaredTypes idl []

        partition idl
        |> List.filter (fun v -> v.Refusal.IsNone)
        |> List.map _.Tag
        |> List.filter (fun tag -> Set.isSubset (declaredTypes idl [ tag ]) envelope)

    /// Why a kind the target CAN express is nonetheless outside the proof vocabulary — the
    /// declared types it would add. `None` when it is inside it (or cannot be expressed at all,
    /// which `partition` already answers).
    let beyondEnvelope (idl: Idl) (tag: string) : string list =
        let envelope = declaredTypes idl []
        declaredTypes idl [ tag ] - envelope |> Set.toList

    // -----------------------------------------------------------------------
    // 7. Emission — the model.
    // -----------------------------------------------------------------------

    let private nl = "\n"

    /// Ordinal key order, matching `Canon.render`'s default: the model has no render step,
    /// so the object LITERAL is its canonical form and the order has to be built in.
    let private sortMembers (ms: Member list) =
        ms |> List.sortWith (fun a b -> System.String.CompareOrdinal(a.Name, b.Name))

    let private binders (ms: Member list) =
        ms |> List.mapi (fun i _ -> sprintf "f%d" i)

    /// The declaration of one constructor's argument list.
    let private ctorArgs (ms: Member list) (result: string) =
        let args =
            ms
            |> List.map (fun m ->
                let t =
                    match m.Presence with
                    | Some None -> "option (" + slotType m.Slot + ")"
                    | _ -> slotType m.Slot

                sprintf "%s:(%s)" (binderName m.Name) t)

        match args with
        | [] -> result
        | _ -> String.concat " -> " args + " -> " + result

    /// The encoding of one member's value, given the F* expression holding it.
    let private encApplied (s: Slot) (e: string) =
        match s with
        | SStr -> "JStr " + e
        | SInt -> "JInt " + e
        | SBool -> "JBool " + e
        | SFloat -> "JFloat " + e
        | SJson -> e
        | SSentinel sent -> "JStr " + lit sent
        | SEnum _
        | SRecord _
        | SUnion _
        | SNode -> "enc_" + slotName s + " " + e
        | SList _ -> "JArr (enc_items_" + slotName s + " " + e + ")"
        | SMap _ -> "JObj (enc_entries_" + slotName s + " " + e + ")"

    /// The object-member list of a declared field set — an inline `match` per conditional
    /// member rather than a higher-order builder, so the whole expression stays a literal
    /// the prover can normalise.
    let private encMembers (ms: Member list) (lead: string list) (bind: Member -> string) =
        let sorted = sortMembers ms

        let rec go rest =
            match rest with
            | [] -> "[]"
            | (m: Member) :: t ->
                let v = bind m
                let tail = go t

                match m.Presence with
                | None -> sprintf "(%s, %s) :: %s" (lit m.Name) (encApplied m.Slot v) tail
                | Some None ->
                    sprintf
                        "(match %s with | None -> %s | Some w -> (%s, %s) :: %s)"
                        v
                        tail
                        (lit m.Name)
                        (encApplied m.Slot "w")
                        tail
                | Some(Some d) ->
                    sprintf "(if %s = %s then %s else (%s, %s) :: %s)" v d tail (lit m.Name) (encApplied m.Slot v) tail

        let body = go sorted

        match lead with
        | [] -> body
        | _ -> (lead |> List.map (fun l -> l + " :: ") |> String.concat "") + body

    /// A member's index in the binder list, for the `f0 f1 …` pattern binders.
    let private bindOf (ms: Member list) =
        let idx = ms |> List.mapi (fun i m -> m.Name, sprintf "f%d" i) |> Map.ofList
        fun (m: Member) -> idx[m.Name]

    let private indent (n: int) (s: string) = String.replicate n "  " + s

    /// The decoder of one member off the object bound to `el`, as a `let`-bound outcome.
    /// Each member is read independently and combined at the end, so a vocabulary with
    /// twenty optional members emits twenty reads rather than 2^20 continuations.
    let private decMember (el: string) (i: int) (m: Member) =
        let name = lit m.Name
        let ty = slotType m.Slot

        let readInto (v: string) (k: string -> string) =
            match m.Slot with
            | SStr -> sprintf "(match as_string %s with | Error e -> Error e | Ok w -> %s)" v (k "w")
            | SInt -> sprintf "(match as_int %s with | Error e -> Error e | Ok w -> %s)" v (k "w")
            | SBool -> sprintf "(match as_bool %s with | Error e -> Error e | Ok w -> %s)" v (k "w")
            | SFloat ->
                // Deliberately narrower than `as_float`, which widens an integer through a
                // supplied `to_flt`: the model's carriers are OPAQUE, so there is no widening
                // to model, and the encoder never emits one.
                sprintf
                    "(match %s with | JFloat w -> %s | other -> Error (\"expected float, got \" ^ kind_name other))"
                    v
                    (k "w")
            | SJson -> sprintf "(let w = %s in %s)" v (k "w")
            // `dUnit` in the generated F#: the sentinel carries nothing, so the value is read
            // without inspecting it, and the member's PRESENCE is the only information there is.
            | SSentinel _ -> sprintf "(let _ = %s in let w = () in %s)" v (k "w")
            | SEnum _
            | SRecord _
            | SUnion _
            | SNode -> sprintf "(match dec_%s %s with | Error e -> Error e | Ok w -> %s)" (slotName m.Slot) v (k "w")
            | SList _ ->
                sprintf
                    "(match %s with | JArr ys -> (match dec_items_%s [] ys with | Error e -> Error e | Ok w -> %s) | other -> Error (\"expected array, got \" ^ kind_name other))"
                    v
                    (slotName m.Slot)
                    (k "w")
            | SMap _ ->
                sprintf
                    "(match %s with | JObj fs -> (match dec_entries_%s [] fs with | Error e -> Error e | Ok w -> %s) | other -> Error (\"expected object, got \" ^ kind_name other))"
                    v
                    (slotName m.Slot)
                    (k "w")

        let outcomeTy, body =
            match m.Presence with
            | None ->
                ty,
                sprintf
                    "(match get_prop %s %s with | Error e -> Error e | Ok v -> %s)"
                    name
                    el
                    (readInto "v" (fun w -> "Ok " + w))
            | Some None ->
                "option (" + ty + ")",
                sprintf
                    "(match get_prop %s %s with | Error _ -> Ok None | Ok v -> %s)"
                    name
                    el
                    (readInto "v" (fun w -> "Ok (Some " + w + ")"))
            | Some(Some d) ->
                ty,
                sprintf
                    "(match get_prop %s %s with | Error _ -> Ok %s | Ok v -> %s)"
                    name
                    el
                    d
                    (readInto "v" (fun w -> "Ok " + w))

        sprintf "let o%d : outcome (%s) = %s in" i outcomeTy body

    /// Combine the `let`-bound member outcomes into the constructor application.
    let private decCombine (ms: Member list) (ctor: string) =
        let rec go i rest =
            match rest with
            | [] ->
                let args = ms |> List.mapi (fun j _ -> sprintf "f%d" j) |> String.concat " "

                if args = "" then
                    sprintf "Ok %s" ctor
                else
                    sprintf "Ok (%s %s)" ctor args
            | _ :: t -> sprintf "(match o%d with | Error e -> Error e | Ok f%d -> %s)" i i (go (i + 1) t)

        go 0 ms

    // -----------------------------------------------------------------------
    // 8. The model module.
    // -----------------------------------------------------------------------

    let private header
        (provenance: Provenance)
        (moduleName: string)
        (idl: Idl)
        (kindTags: string list)
        (c: Closure)
        (refused: Verdict list)
        =
        let refusedLines =
            refused
            |> List.filter (fun v -> not (List.contains v.Tag kindTags))
            |> List.map (fun v ->
                match v.Refusal with
                | Some why -> sprintf "     - %s: %s" v.Tag why
                | None ->
                    // Expressible, and outside the proof vocabulary for the declared cost reason.
                    // Named with the types it would ADD, so widening the rule is a decision about
                    // a known quantity rather than a guess.
                    sprintf
                        "     - %s: expressible, but outside the proof vocabulary — it adds %s to the mutual family (see `FStarTarget.proofKinds`)"
                        v.Tag
                        (match beyondEnvelope idl v.Tag with
                         | [] -> "declared types"
                         | added -> String.concat ", " added))

        [ "(*"
          sprintf "   %s — GENERATED by Fuaran.Core.Idl.Codegen's F* target from" moduleName
          yield! provenance.Origin |> List.map (fun l -> "   " + l)
          ""
          "   WHAT THIS IS. The vocabulary's types, its discriminated encoder and its tag-dispatch"
          "   decoder, over Phase 135's `jval` value model and the `Decode` combinators modelled"
          sprintf "   beside it (`WireDecode.fst`). The theorems over it are in `%sProofs.fst`, which" moduleName
          "   the same generator emits from the same walk — so a kind added to the IDL re-proves"
          "   itself at the next `check.ps1` rather than waiting for a hand-written clause."
          ""
          "   WHAT THIS IS NOT. It is not any host's decoder and no host's build generates it. A"
          "   host keeps its own hand-written, tuned decoder and is certified against its corpus;"
          "   what is proved here is a property of the"
          yield! provenance.Proves |> List.map (fun l -> "   " + l)
          ""
          "   THE MODEL'S BOUNDARY, stated rather than implied."
          ""
          "     - The numeric carriers are OPAQUE (`num` / `flt`), as they are in `WireDecode`: no"
          "       definition here looks inside a number, it only moves one. A float member therefore"
          "       decodes from `JFloat` alone — the widening `as_float` performs through a supplied"
          "       `to_flt` has nothing to widen when the carriers are parameters, and the encoder"
          "       never emits an integer at a float slot."
          "     - A HOST-ONLY member has no wire projection and is absent from the model. The model"
          "       is of the wire; a member that is never on the wire is not part of it."
          "     - A `TMap` is an ASSOCIATION LIST in the authored order. A map has no declared key"
          "       order, and an assoc list is what the wire object is before a renderer sorts it."
          "     - A `TJson` member, and a host-codec member whose wire is JSON, is carried VERBATIM"
          "       as a `jval`. What such a member holds is the host codec's business, not the"
          "       schema's, and inventing an F* counterpart for a host type would model a fiction."
          "     - Generic unions are MONOMORPHISED at the arguments this vocabulary reaches, which"
          "       is what keeps every definition and every emitted lemma first-order."
          ""
          sprintf
              "   VOCABULARY. Discriminator %s, %s envelope, %s key order."
              (lit idl.Wire.Discriminator)
              (match idl.Wire.NodeEnvelope with
               | NodeEnvelopeShape.NestedKind -> "nested-kind"
               | NodeEnvelopeShape.FlatKind -> "flat-kind")
              (match idl.Wire.KeyOrder with
               | KeyOrder.Sorted -> "ordinal-sorted"
               | KeyOrder.Declared -> "declaration")
          sprintf
              "   %d of %d kinds are modelled; %d declared types and %d enums are reached."
              (List.length kindTags)
              (List.length idl.Kinds)
              (c.Order |> List.filter declares |> List.length)
              (List.length c.Enums) ]
        @ (if refusedLines.IsEmpty then
               [ "   Every kind the vocabulary declares is modelled." ]
           else
               [ "   The kinds NOT modelled — named here rather than silently missing, because a reader"
                 "   of the theorem needs to know what it does not cover. Two different reasons, and"
                 "   they are worth telling apart: a construct with no wire-level meaning in the model"
                 "   is a BOUNDARY, while a kind held out of the proof vocabulary is a COST CONTROL"
                 "   that a measurement could lift:"
                 "" ]
               @ refusedLines)
        @ [ ""
            "   THE COST OPTION IS PART OF THE ARTEFACT. `--ext context_pruning` is set below rather"
            "   than passed by the leg: this module opens the whole decode model and declares dozens"
            "   of types, so every query would otherwise carry the entire context. Measured on the"
            "   pinned prover it is the difference between a module the leg can afford and one it"
            "   cannot — the same finding `TreeOps`, `JsonParse`, `Preservation` and `TreeDiff` each"
            "   record in `modules.json`. If this module's budget is ever bumped, check the option is"
            "   still here before reading the growth as ordinary."
            ""
            "   Apache-2.0, like everything beside it."
            "*)"
            sprintf "module %s" moduleName
            ""
            "open WireDecode"
            ""
            "#set-options \"--ext context_pruning\""
            "" ]
        |> String.concat nl

    /// The whole model module, with its header naming where it came from.
    let vocabularyModuleFrom
        (provenance: Provenance)
        (moduleName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        match walk idl kindTags with
        | Error e -> Error e
        | Ok c ->

            let declared = c.Order |> List.filter declares
            let out = System.Text.StringBuilder()
            let line (s: string) = out.Append(s).Append(nl) |> ignore

            line (header provenance moduleName idl kindTags c (partition idl))

            line "(* ======================================================================================"
            line "   1. The closed string sets (`TEnum`) — a bare wire string, not an object."
            line "   ====================================================================================== *)"
            line ""

            for name in c.Enums do
                match findEnum idl name with
                | None -> ()
                | Some e ->
                    let tn = "e_" + snake name
                    let cases = List.zip e.Cases e.WireCases
                    line (sprintf "type %s =" tn)

                    for case, _ in cases do
                        line (sprintf "  | %s" (ctorName tn case))

                    line ""
                    line (sprintf "let enc_%s (#num #flt: eqtype) (x: %s) : Tot (jval num flt) =" tn tn)
                    line "  match x with"

                    for case, wire in cases do
                        line (sprintf "  | %s -> JStr %s" (ctorName tn case) (lit wire))

                    line ""
                    line (sprintf "let dec_%s (#num #flt: eqtype) (el: jval num flt) : Tot (outcome %s) =" tn tn)
                    line "  match as_string el with"
                    line "  | Error e -> Error e"
                    line "  | Ok s ->"

                    for case, wire in cases do
                        line (sprintf "    if s = %s then Ok %s else" (lit wire) (ctorName tn case))

                    line (sprintf "    Error (%s ^ s)" (lit (sprintf "unknown %s: " name)))
                    line ""

            line "(* ======================================================================================"
            line "   2. The vocabulary's types — one mutual group, because a record may hold a union, a"
            line "      union a node, and a node a list of nodes. Records are single-constructor types"
            line "      rather than F* records so that two objects declaring a `label` cannot collide."
            line "   ====================================================================================== *)"
            line ""

            let mutable firstType = true

            let typeHead (name: string) =
                let kw = if firstType then "type" else "and"
                firstType <- false
                sprintf "%s %s (num flt: eqtype) =" kw name

            // The node and its kind union lead the group: they are the vocabulary's root.
            line (typeHead "node")
            let envArgs = c.Envelope |> sortMembers

            let nodeArgs =
                (sprintf "id:string -> k:(vkind num flt)")
                + (envArgs
                   |> List.map (fun m ->
                       let t =
                           match m.Presence with
                           | Some None -> "option (" + slotType m.Slot + ")"
                           | _ -> slotType m.Slot

                       sprintf " -> %s:(%s)" (binderName m.Name) t)
                   |> String.concat "")

            line (sprintf "  | %s : %s -> node num flt" (ctorName "node" "Node") nodeArgs)
            line ""
            line (typeHead "vkind")

            for tag, ms in c.Kinds do
                line (sprintf "  | %s : %s" (ctorName "vkind" tag) (ctorArgs (sortMembers ms) "vkind num flt"))

            for s in declared do
                match s with
                | SNode -> ()
                | SRecord _ ->
                    let n = slotName s
                    let ms = c.Members[n] |> sortMembers
                    line ""
                    line (typeHead n)
                    line (sprintf "  | %s : %s" (ctorName n "Mk") (ctorArgs ms (n + " num flt")))
                | SUnion _ ->
                    let n = slotName s
                    line ""
                    line (typeHead n)

                    for tag, ms in c.Cases[n] do
                        line (sprintf "  | %s : %s" (ctorName n tag) (ctorArgs (sortMembers ms) (n + " num flt")))
                | _ -> ()

            line ""

            // ---- encoders ----------------------------------------------------
            line "(* ======================================================================================"
            line "   3. The encoder — the discriminated envelope, keys in the vocabulary's canonical"
            line "      order. The model has no render step, so the object LITERAL is the canonical form."
            line "      Recursion is on the MODEL value, where F*'s subterm order spans the whole family."
            line "   ====================================================================================== *)"
            line ""

            let disc = idl.Wire.Discriminator
            let mutable firstEnc = true

            let encHead (sig_: string) =
                let kw = if firstEnc then "let rec" else "and"
                firstEnc <- false
                sprintf "%s %s" kw sig_

            // the node
            let bindNode = bindOf envArgs

            let envList =
                encMembers envArgs [] (fun m ->
                    "e" + string (List.findIndex (fun (x: Member) -> x.Name = m.Name) envArgs))

            let envBinders =
                envArgs |> List.mapi (fun i _ -> sprintf "e%d" i) |> String.concat " "

            ignore bindNode

            line (
                encHead (sprintf "enc_node (#num #flt: eqtype) (x: node num flt) : Tot (jval num flt) (decreases x) =")
            )

            line "  match x with"
            line (sprintf "  | %s i k %s ->" (ctorName "node" "Node") envBinders)

            line (sprintf "    JObj ((%s, JStr i) :: (%s, enc_vkind k) :: %s)" (lit "id") (lit "kind") envList)

            line ""

            line (
                encHead (
                    sprintf "enc_vkind (#num #flt: eqtype) (x: vkind num flt) : Tot (jval num flt) (decreases x) ="
                )
            )

            line "  match x with"

            for tag, ms in c.Kinds do
                let sorted = sortMembers ms
                let bs = binders sorted
                let bind = bindOf sorted

                line (sprintf "  | %s %s ->" (ctorName "vkind" tag) (String.concat " " bs))

                line (sprintf "    JObj ((%s, JStr %s) :: %s)" (lit disc) (lit tag) (encMembers sorted [] bind))

            line ""

            for s in declared do
                match s with
                | SNode -> ()
                | SRecord _ ->
                    let n = slotName s
                    let ms = c.Members[n] |> sortMembers
                    let bs = binders ms
                    let bind = bindOf ms

                    line (
                        encHead (
                            sprintf
                                "enc_%s (#num #flt: eqtype) (x: %s num flt) : Tot (jval num flt) (decreases x) ="
                                n
                                n
                        )
                    )

                    line "  match x with"

                    line (
                        sprintf
                            "  | %s %s -> JObj (%s)"
                            (ctorName n "Mk")
                            (String.concat " " bs)
                            (encMembers ms [] bind)
                    )

                    line ""
                | SUnion _ ->
                    let n = slotName s

                    line (
                        encHead (
                            sprintf
                                "enc_%s (#num #flt: eqtype) (x: %s num flt) : Tot (jval num flt) (decreases x) ="
                                n
                                n
                        )
                    )

                    line "  match x with"

                    for tag, ms in c.Cases[n] do
                        let sorted = sortMembers ms
                        let bs = binders sorted
                        let bind = bindOf sorted

                        match c.Transparent.TryFind n with
                        | Some t when t = tag ->
                            // The declared transparent case rides BARE — no envelope at all.
                            let only = List.head sorted
                            line (sprintf "  | %s f0 -> %s" (ctorName n tag) (encApplied only.Slot "f0"))
                        | _ ->
                            line (sprintf "  | %s %s ->" (ctorName n tag) (String.concat " " bs))

                            line (
                                sprintf
                                    "    JObj ((%s, JStr %s) :: %s)"
                                    (lit disc)
                                    (lit tag)
                                    (encMembers sorted [] bind)
                            )

                    line ""
                | _ -> ()

            // list / map walkers
            for s in c.Order do
                match s with
                | SList inner ->
                    let n = slotName s

                    line (
                        encHead (
                            sprintf
                                "enc_items_%s (#num #flt: eqtype) (xs: list (%s)) : Tot (list (jval num flt)) (decreases xs) ="
                                n
                                (slotType inner)
                        )
                    )

                    line "  match xs with"
                    line "  | [] -> []"
                    line (sprintf "  | y :: t -> %s :: enc_items_%s t" (encApplied inner "y") n)
                    line ""
                | SMap inner ->
                    let n = slotName s

                    line (
                        encHead (
                            sprintf
                                "enc_entries_%s (#num #flt: eqtype) (es: list (string & %s)) : Tot (list (string & jval num flt)) (decreases es) ="
                                n
                                (slotType inner)
                        )
                    )

                    line "  match es with"
                    line "  | [] -> []"
                    line (sprintf "  | (k, v) :: t -> (k, %s) :: enc_entries_%s t" (encApplied inner "v") n)
                    line ""
                | _ -> ()

            // ---- decoders ----------------------------------------------------
            line "(* ======================================================================================"
            line "   4. The tag-dispatch decoder. Every cross-type call goes through `get_prop`, whose"
            line "      RETURN-TYPE refinement (`jsize (Ok?.v r) < jsize el`) is the termination argument:"
            line "      a decoder that descends by NAME into a value it looked up has no syntactic one."
            line "      Value decoders sit at tier 0 on `jsize`, walkers at tier 1 on `jsizes` / `fsize`."
            line "   ====================================================================================== *)"
            line ""

            let mutable firstDec = true

            let decHead (sig_: string) =
                let kw = if firstDec then "let rec" else "and"
                firstDec <- false
                sprintf "%s %s" kw sig_

            line (
                decHead (
                    "dec_node (#num #flt: eqtype) (el: jval num flt) : Tot (outcome (node num flt)) (decreases %[(jsize el <: nat); 0]) ="
                )
            )

            line (sprintf "  let oid : outcome string = str_field %s el in" (lit "id"))

            line (
                sprintf
                    "  let ok : outcome (vkind num flt) = (match get_prop %s el with | Error e -> Error e | Ok v -> dec_vkind v) in"
                    (lit "kind")
            )

            envArgs |> List.iteri (fun i m -> line (indent 1 (decMember "el" i m)))

            let envCombine =
                let rec go i rest =
                    match rest with
                    | [] ->
                        let args = envArgs |> List.mapi (fun j _ -> sprintf "f%d" j) |> String.concat " "

                        sprintf "Ok (%s i k %s)" (ctorName "node" "Node") args
                    | _ :: t -> sprintf "(match o%d with | Error e -> Error e | Ok f%d -> %s)" i i (go (i + 1) t)

                go 0 envArgs

            line (
                sprintf
                    "  (match oid with | Error e -> Error e | Ok i -> (match ok with | Error e -> Error e | Ok k -> %s))"
                    envCombine
            )

            line ""

            line (
                decHead (
                    "dec_vkind (#num #flt: eqtype) (el: jval num flt) : Tot (outcome (vkind num flt)) (decreases %[(jsize el <: nat); 0]) ="
                )
            )

            line (sprintf "  match str_field %s el with" (lit disc))
            line "  | Error e -> Error e"
            line "  | Ok tag ->"

            for tag, ms in c.Kinds do
                let sorted = sortMembers ms
                line (sprintf "    if tag = %s then" (lit tag))
                sorted |> List.iteri (fun i m -> line (indent 3 (decMember "el" i m)))
                line (indent 3 (decCombine sorted (ctorName "vkind" tag)))
                line "    else"

            line (sprintf "    Error (%s ^ tag)" (lit "unknown kind: "))
            line ""

            for s in declared do
                match s with
                | SNode -> ()
                | SRecord _ ->
                    let n = slotName s
                    let ms = c.Members[n] |> sortMembers

                    line (
                        decHead (
                            sprintf
                                "dec_%s (#num #flt: eqtype) (el: jval num flt) : Tot (outcome (%s num flt)) (decreases %%[(jsize el <: nat); 0]) ="
                                n
                                n
                        )
                    )

                    ms |> List.iteri (fun i m -> line (indent 1 (decMember "el" i m)))
                    line (indent 1 (decCombine ms (ctorName n "Mk")))
                    line ""
                | SUnion _ ->
                    let n = slotName s

                    line (
                        decHead (
                            sprintf
                                "dec_%s (#num #flt: eqtype) (el: jval num flt) : Tot (outcome (%s num flt)) (decreases %%[(jsize el <: nat); 0]) ="
                                n
                                n
                        )
                    )

                    let transparentArm =
                        match c.Transparent.TryFind n with
                        | None -> None
                        | Some tag ->
                            let _, ms = c.Cases[n] |> List.find (fun (t, _) -> t = tag)
                            let only = List.head ms

                            let read =
                                match only.Slot with
                                | SStr -> "as_string el"
                                | SInt -> "as_int el"
                                | SBool -> "as_bool el"
                                | SFloat ->
                                    "(match el with | JFloat w -> Ok w | other -> Error (\"expected float, got \" ^ kind_name other))"
                                | SSentinel _ -> "Ok ()"
                                | SEnum _ -> sprintf "dec_%s el" (slotName only.Slot)
                                | _ -> "as_string el"

                            Some(
                                sprintf "(match %s with | Error e -> Error e | Ok w -> Ok (%s w))" read (ctorName n tag)
                            )

                    match transparentArm with
                    | Some arm ->
                        line (sprintf "  match str_field %s el with" (lit disc))
                        line (sprintf "  | Error _ -> %s" arm)
                        line "  | Ok tag ->"
                    | None ->
                        line (sprintf "  match str_field %s el with" (lit disc))
                        line "  | Error e -> Error e"
                        line "  | Ok tag ->"

                    for tag, ms in c.Cases[n] do
                        match c.Transparent.TryFind n with
                        | Some t when t = tag -> ()
                        | _ ->
                            let sorted = sortMembers ms
                            line (sprintf "    if tag = %s then" (lit tag))
                            sorted |> List.iteri (fun i m -> line (indent 3 (decMember "el" i m)))
                            line (indent 3 (decCombine sorted (ctorName n tag)))
                            line "    else"

                    line (sprintf "    Error (%s ^ tag)" (lit (sprintf "unknown %s case: " n)))
                    line ""
                | _ -> ()

            for s in c.Order do
                match s with
                | SList inner ->
                    let n = slotName s
                    let ity = slotType inner

                    line (
                        decHead (
                            sprintf
                                "dec_items_%s (#num #flt: eqtype) (acc: list (%s)) (ys: list (jval num flt)) : Tot (outcome (list (%s))) (decreases %%[(jsizes ys <: nat); 1]) ="
                                n
                                ity
                                ity
                        )
                    )

                    line "  match ys with"
                    line "  | [] -> Ok (rev acc)"
                    line "  | y :: rest ->"

                    let readOne =
                        match inner with
                        | SStr -> "as_string y"
                        | SInt -> "as_int y"
                        | SBool -> "as_bool y"
                        | SFloat ->
                            "(match y with | JFloat w -> Ok w | other -> Error (\"expected float, got \" ^ kind_name other))"
                        | SJson -> "Ok y"
                        | SSentinel _ -> "Ok ()"
                        | SEnum _
                        | SRecord _
                        | SUnion _
                        | SNode -> sprintf "dec_%s y" (slotName inner)
                        | SList _ ->
                            sprintf
                                "(match y with | JArr zs -> dec_items_%s [] zs | other -> Error (\"expected array, got \" ^ kind_name other))"
                                (slotName inner)
                        | SMap _ ->
                            sprintf
                                "(match y with | JObj gs -> dec_entries_%s [] gs | other -> Error (\"expected object, got \" ^ kind_name other))"
                                (slotName inner)

                    line (sprintf "    (match %s with" readOne)
                    line (sprintf "     | Ok w -> dec_items_%s (w :: acc) rest" n)
                    line "     | Error e -> Error e)"
                    line ""
                | SMap inner ->
                    let n = slotName s
                    let ity = slotType inner

                    line (
                        decHead (
                            sprintf
                                "dec_entries_%s (#num #flt: eqtype) (acc: list (string & %s)) (fs: list (string & jval num flt)) : Tot (outcome (list (string & %s))) (decreases %%[(fsize fs <: nat); 1]) ="
                                n
                                ity
                                ity
                        )
                    )

                    line "  match fs with"
                    line "  | [] -> Ok (rev acc)"
                    line "  | (k, v) :: rest ->"

                    let readOne =
                        match inner with
                        | SStr -> "as_string v"
                        | SInt -> "as_int v"
                        | SBool -> "as_bool v"
                        | SFloat ->
                            "(match v with | JFloat w -> Ok w | other -> Error (\"expected float, got \" ^ kind_name other))"
                        | SJson -> "Ok v"
                        | SSentinel _ -> "Ok ()"
                        | SEnum _
                        | SRecord _
                        | SUnion _
                        | SNode -> sprintf "dec_%s v" (slotName inner)
                        | SList _ ->
                            sprintf
                                "(match v with | JArr zs -> dec_items_%s [] zs | other -> Error (\"expected array, got \" ^ kind_name other))"
                                (slotName inner)
                        | SMap _ ->
                            sprintf
                                "(match v with | JObj gs -> dec_entries_%s [] gs | other -> Error (\"expected object, got \" ^ kind_name other))"
                                (slotName inner)

                    line (sprintf "    (match %s with" readOne)
                    line (sprintf "     | Ok w -> dec_entries_%s ((k, w) :: acc) rest" n)
                    line "     | Error e -> Error e)"
                    line ""
                | _ -> ()

            Ok(out.ToString())

    /// The whole model module under the generator's own provenance — the entry point a caller
    /// with no named source uses. `vocabularyModuleFrom` is the one this repository calls,
    /// because its source is named (fuaran-core Phase 173).
    let vocabularyModule (moduleName: string) (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        vocabularyModuleFrom Provenance.supplied moduleName idl kindTags

    // -----------------------------------------------------------------------
    // 9. The proofs module — the theorems, emitted from the same walk.
    // -----------------------------------------------------------------------

    /// The lemma invocation that discharges one member's round trip, or `None` when the
    /// member's slot carries its own round trip definitionally (a scalar, or a value the
    /// model carries verbatim).
    let private memberLemma (s: Slot) (v: string) : string option =
        match s with
        | SStr
        | SInt
        | SBool
        | SFloat
        | SJson
        | SSentinel _ -> None
        // The implicits are passed EXPLICITLY at every lemma call. A lemma over a type that is
        // not itself parameterised by the numeric carriers — an enum, or a walker over a list of
        // strings — mentions `num` and `flt` only inside its own statement, so nothing at the
        // call site determines them and F* reports an unresolved implicit rather than guessing.
        | SEnum _
        | SRecord _
        | SUnion _
        | SNode -> Some(sprintf "rt_%s #num #flt %s" (slotName s) v)
        | SList _ -> Some(sprintf "rt_items_%s #num #flt [] %s" (slotName s) v)
        | SMap _ -> Some(sprintf "rt_entries_%s #num #flt [] %s" (slotName s) v)

    /// The lemma calls for one constructor's members, under their presence rules.
    let private memberLemmas (ms: Member list) =
        ms
        |> List.mapi (fun i m -> i, m)
        |> List.choose (fun (i, m) ->
            let b = sprintf "f%d" i

            match m.Presence with
            | Some None ->
                memberLemma m.Slot "w"
                |> Option.map (fun call -> sprintf "(match %s with | None -> () | Some w -> %s)" b call)
            | _ -> memberLemma m.Slot b)

    // -----------------------------------------------------------------------
    // 8b. The lemma SHAPE — one lemma per constructor, and the presence split LINEAR in the
    // conditional members (Phase 182, replacing Phase 168's per-pattern split).
    //
    // A constructor with k CONDITIONAL members — optional, or omitted at its default — encodes to
    // 2^k object shapes, and a round-trip lemma over the whole constructor puts all of them in one
    // query. Measured at a twenty-kind vocabulary whose node envelope carried five optional
    // members (Phase 150): the node lemma's 65-goal query failed a `--quake` seed, the widest
    // kind's arm failed outright, and raising the rlimit turned failing into grinding.
    // `--split_queries` is not settable as a pragma on the pinned prover, so the isolation is
    // done HERE, in the emitted shape.
    //
    // Phase 168 isolated a shape by PINNING every conditional member at once — one lemma per
    // presence pattern, 2^k of them. That is exactly right while k is small and unusable once it
    // is not: `fuaran#1754`, the kit's first adopter, measured 71,722 lemmas in a 114 MB proof
    // script at the UI vocabulary, one kind with sixteen conditional members contributing 65,536.
    // So the split is now LINEAR, and it splits by MEMBER rather than by pattern:
    //
    //   lk_<T>__<Ctor>__<member>            one member's LOOKUP off the encoded object, with every
    //   lk_<T>__<Ctor>__<member>__present   conditional member other than this one left FREE. A
    //   lk_<T>__<Ctor>__<member>__absent    required member gets one (its key is present, at a
    //                     position the conditionals before it move); a conditional member gets two,
    //                     one per side of its own presence test. Nothing before the FIRST
    //                     conditional member in key order needs one at all — `find_field` reaches
    //                     it without meeting a branch.
    //   rt_<T>            the round trip over the type — for a type with several constructors a
    //                     CASE SPLIT, each arm citing the constructor's own lemma;
    //   rt_<T>__<Ctor>    one constructor's arm alone, under `C__<T>__<Ctor>? x`, proved by citing
    //                     each member's lookup — the conditional ones under a two-way match on
    //                     that member alone — and then the members' own round trips.
    //
    // WHY IT WORKS, measured rather than assumed (Phase 182's probe, pinned prover, `--z3rlimit 40`,
    // a synthetic vocabulary whose widest kind carries k optional string members):
    //
    //   k=5   whole constructor in ONE query: 29 s, green. Per-pattern (2^5): 96 s, green.
    //   k=8   whole constructor in ONE query: FAILS at fuel 32 ("failed to prove").
    //         One member's lookup with the other seven FREE: green, under raised fuel.
    //   k=16  the MODEL alone will not check — the prover dies with `allocation failure during
    //         minor GC` after 719 s, because `encMembers` duplicates the tail per conditional
    //         member and the encoder's own text is 2^k. That limit is the MODEL emitter's and is
    //         untouched here; see `proofs/README.md` and DECISIONS for what it costs an adopter.
    //
    // The load-bearing fact the linear form rests on is that `find_field name` pushes through an
    // entry with a different key, so `find_field n (if c then t else (k, v) :: t)` is `find_field
    // n t` on BOTH sides of the test and the two branches merge instead of multiplying. It needs
    // FUEL to do it: the default two unfoldings do not reach past the second key, which is why the
    // lookup lemmas carry a scoped `--fuel` sized to the constructor rather than the file's.
    //
    // The family stays one mutual induction, so a kind's children still reach `rt_node`; the
    // termination measure is lexicographic, `%[x; tier]`, because the constructor lemmas recurse on
    // the SAME value and differ only in how much of it they have already fixed. The lookup lemmas
    // are NOT in the family — they recurse on nothing — which is what lets each carry its own
    // scoped options, since a mutual family admits only one set for all of it.
    // -----------------------------------------------------------------------

    /// RE-PURPOSED by Phase 182, not retired: it was the count at which the round trip was split
    /// one lemma per presence PATTERN, and it is now the count at which the LINEAR per-member
    /// split is used instead of proving the whole constructor in one query. Two, so that the split
    /// is exercised — and proved to discharge — by the certification set itself (the reference
    /// vocabulary's `Embed` and its node envelope both carry exactly two), not first met at an
    /// adopter's scale. A constructor below it is still proved in one query, which the k=5
    /// measurement above shows is the cheaper shape while it holds.
    let presenceSplitAt = 2

    /// One conditional member of a constructor: its binder in the constructor pattern, its IDL
    /// name (for the pattern lemma's caption), and how its two shapes are told apart — `None`
    /// for an optional member (`None?` / `Some?`), `Some lit` for an omit-at-default member
    /// (equality with the literal the encoder tests).
    type private Conditional =
        { Binder: string
          Name: string
          Default: string option }

    let private conditionals (ms: Member list) : Conditional list =
        ms
        |> List.mapi (fun i m -> i, m)
        |> List.choose (fun (i, m) ->
            match m.Presence with
            | None -> None
            | Some d ->
                Some
                    { Binder = sprintf "f%d" i
                      Name = m.Name
                      Default = d })

    /// The boolean a lookup lemma's `requires` carries for one member.
    let private condition (c: Conditional) (present: bool) =
        match c.Default, present with
        | None, true -> sprintf "Some? %s" c.Binder
        | None, false -> sprintf "None? %s" c.Binder
        | Some d, true -> sprintf "not (%s = %s)" c.Binder d
        | Some d, false -> sprintf "%s = %s" c.Binder d

    let private caption (c: Conditional) (present: bool) =
        match c.Default, present with
        | None, true -> c.Name + " present"
        | None, false -> c.Name + " absent"
        | Some _, true -> c.Name + " not at its default"
        | Some _, false -> c.Name + " at its default"

    /// The two-way citation that discharges one conditional member's lookup — an optional member
    /// by `match`, an omit-at-default one by the encoder's own equality test. One line of text per
    /// conditional member, where Phase 168 nested them into a 2^k tree.
    let private citeConditional (lk: string) (c: Conditional) : string =
        match c.Default with
        | None ->
            sprintf
                "(match %s with | None -> %s__absent #num #flt x | Some _ -> %s__present #num #flt x);"
                c.Binder
                lk
                lk
        | Some d -> sprintf "(if %s = %s then %s__absent #num #flt x else %s__present #num #flt x);" c.Binder d lk lk

    /// The fuel one constructor's lookup lemmas need. `find_field` walks the object's entries one
    /// unfolding at a time and the default two do not reach past the second key, so the figure is
    /// sized to the entries the walk can meet — the lead entries plus every member — with a margin
    /// for the `Ok?` refinement `get_prop` carries out of the lookup. Scoped to the lemma rather
    /// than set on the file: fuel the family does not need is fuel every other query pays for.
    let private lookupFuel (leads: int) (members: int) = max 8 (2 * (leads + members + 2))

    /// One constructor of a modelled type, as the proof emitter sees it: the model's constructor
    /// name, the binders that precede its members in the pattern (`i k` for the node), the
    /// members under their presence rules, and the lemma calls that precede the members' own
    /// (`rt_vkind k` for the node).
    type private Ctor =
        { Label: string
          CtorName: string
          Lead: string list
          Members: Member list
          Extra: string list }

    let private ctorMembers (c: Ctor) = sortMembers c.Members

    let private ctorPattern (c: Ctor) =
        String.concat
            " "
            (c.CtorName
             :: (c.Lead @ (ctorMembers c |> List.mapi (fun i _ -> sprintf "f%d" i))))

    /// The index of the first CONDITIONAL member in key order, or the member count when the
    /// constructor has none. Every member BEFORE it is reached by `find_field` without meeting a
    /// branch, so it needs no lookup lemma of its own — which is why a constructor's lemma count
    /// is `2k + r' + 1` and not `2k + r + 1`.
    let private firstConditional (ms: Member list) =
        match ms |> List.tryFindIndex (fun m -> m.Presence.IsSome) with
        | Some i -> i
        | None -> List.length ms

    /// One member's lookup lemma name — `__present` / `__absent` are appended for a conditional.
    let private lookupName (typeName: string) (label: string) (m: Member) =
        sprintf "lk_%s__%s__%s" typeName label (snake m.Name)

    /// The presence LOOKUPS of one type's constructors — see 8b above for the shape. Emitted ahead
    /// of the round-trip family and OUTSIDE it: they recurse on nothing, so each can carry the
    /// scoped fuel its own walk needs, where a mutual family admits one option set for all of it.
    let private emitLookups (line: string -> unit) (typeName: string) (fsType: string) (ctors: Ctor list) =
        let multi = List.length ctors > 1

        for c in ctors do
            let ms = ctorMembers c
            let cs = conditionals ms

            if List.length cs >= presenceSplitAt then
                let pattern = ctorPattern c
                let fuel = lookupFuel (List.length c.Lead) (List.length ms)
                let start = firstConditional ms

                let guarded (body: string) (fallback: string) =
                    if multi then
                        sprintf "match x with | %s -> %s | _ -> %s" pattern body fallback
                    else
                        sprintf "match x with | %s -> %s" pattern body

                let emit (name: string) (why: string) (requires: string option) (ensures: string) =
                    let req =
                        match requires with
                        | Some r -> sprintf "(requires (%s)) " r
                        | None -> ""

                    line (sprintf "(* %s — %s *)" name why)
                    line (sprintf "#push-options \"--fuel %d --ifuel 4\"" fuel)

                    line (
                        sprintf
                            "let %s (#num #flt: eqtype) (x: %s) : Lemma %s(ensures (%s)) = ()"
                            name
                            fsType
                            req
                            ensures
                    )

                    line "#pop-options"
                    line ""

                ms
                |> List.iteri (fun i m ->
                    if i >= start then
                        let lk = lookupName typeName c.Label m
                        let b = sprintf "f%d" i

                        let found (v: string) =
                            guarded
                                (sprintf
                                    "get_prop %s (enc_%s #num #flt x) == Ok (%s)"
                                    (lit m.Name)
                                    typeName
                                    (encApplied m.Slot v))
                                "True"

                        match m.Presence with
                        | None ->
                            emit
                                lk
                                (m.Name + " — always emitted, at a position the conditionals before it move")
                                (if multi then Some(sprintf "%s? x" c.CtorName) else None)
                                (found b)
                        | Some d ->
                            let cond =
                                { Binder = b
                                  Name = m.Name
                                  Default = d }

                            let present =
                                match d with
                                | None -> sprintf "(Some?.v %s)" b
                                | Some _ -> b

                            emit
                                (lk + "__present")
                                (caption cond true)
                                (Some(guarded (condition cond true) "false"))
                                (found present)

                            emit
                                (lk + "__absent")
                                (caption cond false)
                                (Some(guarded (condition cond false) "false"))
                                (sprintf "Error? (get_prop %s (enc_%s #num #flt x))" (lit m.Name) typeName))

    /// One type's round trip as a family of lemmas — see 8b above for the shape. `rtHead` supplies
    /// `let rec` / `and`; `line` receives the emitted text.
    let private emitFamily
        (line: string -> unit)
        (rtHead: string -> string)
        (typeName: string)
        (fsType: string)
        (ctors: Ctor list)
        =
        let rt = "rt_" + typeName

        let signature (name: string) (requires: string option) (tier: int) =
            let req =
                match requires with
                | Some r -> sprintf "(requires (%s)) " r
                | None -> ""

            sprintf
                "%s (#num #flt: eqtype) (x: %s) : Lemma %s(ensures dec_%s (enc_%s #num #flt x) == Ok x) (decreases %%[x; %d]) ="
                name
                fsType
                req
                typeName
                typeName
                tier

        let members (c: Ctor) = ctorMembers c

        let binders (c: Ctor) =
            c.Lead @ (members c |> List.mapi (fun i _ -> sprintf "f%d" i))

        let pattern (c: Ctor) = ctorPattern c

        let wildcard (c: Ctor) =
            String.concat " " (c.CtorName :: (binders c |> List.map (fun _ -> "_")))

        let direct (c: Ctor) =
            let calls = c.Extra @ memberLemmas (members c)
            if calls.IsEmpty then "()" else String.concat "; " calls

        let multi = List.length ctors > 1

        // The arm-level lemma: the constructor proved in one query when its conditional members
        // are few, else by citing each member's own lookup — one line per member, a two-way match
        // for a conditional one — and then the members' round trips.
        let armLemma (name: string) (requires: string option) (tier: int) (c: Ctor) =
            let ms = members c
            let cs = conditionals ms

            if List.length cs < presenceSplitAt then
                line (rtHead (signature name requires tier))
                line "  match x with"
                line (sprintf "  | %s -> %s" (pattern c) (direct c))
                line ""
            else
                let start = firstConditional ms

                line (rtHead (signature name requires tier))
                line "  match x with"
                line (sprintf "  | %s ->" (pattern c))

                ms
                |> List.iteri (fun i m ->
                    if i >= start then
                        let lk = lookupName typeName c.Label m

                        match m.Presence with
                        | None -> line (indent 2 (sprintf "%s #num #flt x;" lk))
                        | Some d ->
                            line (
                                indent
                                    2
                                    (citeConditional
                                        lk
                                        { Binder = sprintf "f%d" i
                                          Name = m.Name
                                          Default = d })
                            ))

                line (indent 2 (direct c))
                line ""

        if not multi then
            armLemma rt None 2 ctors.Head
        else
            line (rtHead (signature rt None 2))
            line "  match x with"

            for c in ctors do
                line (sprintf "  | %s -> %s__%s #num #flt x" (wildcard c) rt c.Label)

            line ""

            for c in ctors do
                armLemma (sprintf "%s__%s" rt c.Label) (Some(sprintf "%s? x" c.CtorName)) 1 c

    let private proofsHeader
        (provenance: Provenance)
        (moduleName: string)
        (modelName: string)
        (idl: Idl)
        (kindTags: string list)
        =
        [ "(*"
          sprintf "   %s — GENERATED by Fuaran.Core.Idl.Codegen's F* target, from the same walk that" moduleName
          sprintf "   emits `%s.fst`, over" modelName
          yield! provenance.Origin |> List.map (fun l -> "   " + l)
          ""
          "   WHAT IS PROVED, and why it is GENERATED rather than written."
          ""
          "   Phase 135 proved `decode (encode n) == Ok n` for a hand-written four-case REFERENCE"
          "   vocabulary, and was explicit about the boundary: a theorem about a reference vocabulary"
          "   is not a theorem about the one a host actually decodes. The real vocabulary has dozens"
          sprintf
              "   of kinds — %d of this one's %d are modelled — and it grows. A HAND-WRITTEN proof over"
              (List.length kindTags)
              (List.length idl.Kinds)
          "   it would be a theorem about the vocabulary as it stood on the day it was written, which"
          "   is the same defect one release later. So the proof script is emitted by the walk that"
          "   emits the model: a kind added to the IDL re-proves itself at the next `check.ps1`, and a"
          "   kind whose encoder and decoder disagree fails it."
          ""
          "     - `rt_node` and the family beside it — THE ROUND TRIP. For every value of every"
          "       modelled type, decoding its encoding returns that value: `dec_node (enc_node x) =="
          "       Ok x`, at every depth, through every list, map, optional member and omit-default."
          "     - `dec_node_total` — TOTALITY, and the exclusivity of the outcome. That the decoders"
          "       type-check at `Tot` is the termination proof; the lemma states that every input"
          "       reaches exactly one of `Ok` / `Error` and never both."
          ""
          "   THE SHAPE — one lemma per constructor, and the presence split LINEAR in the conditional"
          "   members (fuaran-core Phase 182, replacing Phase 168's per-pattern split)."
          "   A constructor with k conditional members (optional, or omitted at its default) encodes"
          "   to 2^k object shapes, and a lemma over the whole constructor puts all of them in ONE"
          "   query. Measured at a twenty-kind vocabulary whose node envelope carried five optional"
          "   members (Phase 150): the node lemma's 65-goal query failed a `--quake` seed at the leg's"
          "   rlimit, the widest kind's arm failed outright, and raising the rlimit turned failing into"
          "   grinding. `--split_queries` is not settable as a pragma on the pinned prover, so the"
          "   isolation is in the emitted shape instead. `rt_<T>` is the round trip over a type — for a"
          "   type with several constructors, a CASE SPLIT whose arms cite `rt_<T>__<Ctor>`, one"
          "   constructor's arm alone under `C__<T>__<Ctor>? x`. A constructor with"
          sprintf
              "   %d or more conditional members is proved from its members' LOOKUPS instead of in one"
              presenceSplitAt
          "   query: `lk_<T>__<Ctor>__<member>` reads one key off the encoded object with every OTHER"
          "   conditional member left free — one lemma for a member that is always emitted, two for a"
          "   conditional one — and the constructor's lemma cites them a member at a time. That is"
          "   2k + r' lemmas where Phase 168 emitted 2^k, and it is why a sixteen-conditional kind"
          "   costs thirty-odd lemmas rather than 65,536 (`fuaran#1754` measured 71,722 of them in a"
          "   114 MB script at the UI vocabulary). The family is still one mutual induction — a kind's"
          "   children reach `rt_node` — on the lexicographic measure `%[x; tier]`; the lookups are"
          "   NOT in it, since they recurse on nothing, which is what lets each carry the scoped"
          "   `--fuel` its own walk needs. The rlimit precedent Phase 150 took (`--z3rlimit 200` in"
          "   this file) stays retired: the leg's own rlimit is what these are checked under, and a"
          "   query that wants more is a query the split has failed to isolate."
          ""
          "   WHAT IS NOT PROVED HERE, and why. The `wf` CHARACTERISATION — `Ok? (dec el) == wf el`,"
          "   which Phase 135 carries for its hand-written reference vocabulary — is not restated over"
          "   the generated vocabulary. The only predicate this emitter could generate from the same"
          "   walk is the decoder's own accept set restated clause for clause, and `Ok? (dec el) =="
          "   (that predicate) el` is then a theorem about two renderings of one definition — true,"
          "   and empty. A characterisation worth the name needs an INDEPENDENT statement of the"
          "   accept set (a schema-shaped predicate), which is a model-sized artefact of its own;"
          "   `proofs/README.md` (theorem 1) records the halt. The round trip covers one direction"
          "   (everything the encoder can produce is read back exactly); the characterisation of what"
          "   ELSE is accepted is not covered, and is named here rather than left to be assumed."
          ""
          "   ONE COST OPTION IS PART OF THE ARTEFACT: `--ext context_pruning` is set below for the"
          "   reason the model module's header gives at greater length — a mutual induction over the"
          "   whole family would otherwise carry the whole family in every query. If this module is"
          "   ever slow enough to want investigating, check it is still in force first."
          ""
          "   Apache-2.0, like everything beside it."
          "*)"
          sprintf "module %s" moduleName
          ""
          "open WireDecode"
          sprintf "open %s" modelName
          ""
          "#set-options \"--ext context_pruning\""
          "" ]
        |> String.concat nl

    /// The theorems over a generated model — same IDL, same kind selection, or the two files
    /// are about different vocabularies — with the header naming where the model came from.
    let proofsModuleFrom
        (provenance: Provenance)
        (moduleName: string)
        (modelName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        match walk idl kindTags with
        | Error e -> Error e
        | Ok c ->

            let out = System.Text.StringBuilder()
            let line (s: string) = out.Append(s).Append(nl) |> ignore
            let declared = c.Order |> List.filter declares
            let envArgs = c.Envelope |> sortMembers

            line (proofsHeader provenance moduleName modelName idl kindTags)

            line "(* ======================================================================================"
            line "   1. The closed string sets. Each is a finite match on distinct literals, so its round"
            line "      trip is definitional — stated anyway, because the family below calls it by name."
            line "   ====================================================================================== *)"
            line ""

            for name in c.Enums do
                let tn = "e_" + snake name

                line (
                    sprintf
                        "let rt_%s (#num #flt: eqtype) (x: %s) : Lemma (ensures dec_%s (enc_%s #num #flt x) == Ok x) = ()"
                        tn
                        tn
                        tn
                        tn
                )

            line ""

            // The types the family covers, in emission order. Built ONCE and walked twice: the
            // lookups first (standalone lemmas, each with its own scoped fuel), then the mutual
            // round-trip family that cites them. Order is load-bearing in both walks — F* resolves
            // top to bottom, and `rt_node` must stay the family's first `let rec`.
            let families =
                [ // The node: one constructor, `id` and the kind ahead of the envelope members, and
                  // the kind's own round trip cited ahead of theirs.
                  "node",
                  "node num flt",
                  [ { Label = "Node"
                      CtorName = ctorName "node" "Node"
                      Lead = [ "i"; "k" ]
                      Members = envArgs
                      Extra = [ "rt_vkind #num #flt k" ] } ]
                  // The kinds: `rt_vkind` is the case split, `rt_vkind__<Kind>` each kind's arm
                  // alone — the per-kind lemma Phase 168 was cut for.
                  "vkind",
                  "vkind num flt",
                  [ for tag, ms in c.Kinds do
                        { Label = tag
                          CtorName = ctorName "vkind" tag
                          Lead = []
                          Members = ms
                          Extra = [] } ]
                  for s in declared do
                      match s with
                      | SNode -> ()
                      | SRecord _ ->
                          let n = slotName s

                          n,
                          (n + " num flt"),
                          [ { Label = "Mk"
                              CtorName = ctorName n "Mk"
                              Lead = []
                              Members = c.Members[n]
                              Extra = [] } ]
                      | SUnion _ ->
                          let n = slotName s

                          n,
                          (n + " num flt"),
                          [ for tag, ms in c.Cases[n] do
                                { Label = tag
                                  CtorName = ctorName n tag
                                  Lead = []
                                  Members = ms
                                  Extra = [] } ]
                      | _ -> () ]

            line "(* ======================================================================================"
            line "   2. THE PRESENCE LOOKUPS. One member's key read off the encoded object, with every"
            line "      conditional member other than that one left FREE — so a constructor with k of them"
            line "      costs 2k + r' lemmas rather than 2^k, and no query has to hold more than one key's"
            line "      walk. Each carries its own scoped fuel: `find_field` pushes through a key it is not"
            line "      looking for, and the default two unfoldings do not reach past the second. Emitted"
            line "      only for a constructor at or above the split threshold, and only from its first"
            line "      conditional member on — everything before that is reached without a branch."
            line "   ====================================================================================== *)"
            line ""

            for typeName, fsType, ctors in families do
                emitLookups line typeName fsType ctors

            line "(* ======================================================================================"
            line "   3. THE ROUND TRIP. One mutual induction over the whole family, recursing on the MODEL"
            line "      value — F*'s subterm order spans a mutual inductive family, so each case needs only"
            line "      the sub-lemmas of the members it carries. ONE LEMMA PER CONSTRUCTOR: a wide one is"
            line "      proved by citing section 2's lookups, a member at a time, rather than by carrying"
            line "      its object shapes into this query."
            line "   ====================================================================================== *)"
            line ""

            let mutable firstRt = true

            let rtHead (sig_: string) =
                let kw = if firstRt then "let rec" else "and"
                firstRt <- false
                sprintf "%s %s" kw sig_

            for typeName, fsType, ctors in families do
                emitFamily line rtHead typeName fsType ctors

            for s in c.Order do
                match s with
                | SList inner ->
                    let n = slotName s
                    let ity = slotType inner

                    line (
                        rtHead (
                            sprintf
                                "rt_items_%s (#num #flt: eqtype) (acc: list (%s)) (xs: list (%s)) : Lemma (ensures dec_items_%s acc (enc_items_%s #num #flt xs) == Ok (rev_app acc xs)) (decreases %%[xs; 2]) ="
                                n
                                ity
                                ity
                                n
                                n
                        )
                    )

                    line "  match xs with"
                    line "  | [] -> ()"

                    let innerCall =
                        match memberLemma inner "y" with
                        | Some call -> call + "; "
                        | None -> ""

                    line (sprintf "  | y :: t -> %srt_items_%s #num #flt (y :: acc) t" innerCall n)
                    line ""
                | SMap inner ->
                    let n = slotName s
                    let ity = slotType inner

                    line (
                        rtHead (
                            sprintf
                                "rt_entries_%s (#num #flt: eqtype) (acc: list (string & %s)) (es: list (string & %s)) : Lemma (ensures dec_entries_%s acc (enc_entries_%s #num #flt es) == Ok (rev_app acc es)) (decreases %%[es; 2]) ="
                                n
                                ity
                                ity
                                n
                                n
                        )
                    )

                    line "  match es with"
                    line "  | [] -> ()"

                    let innerCall =
                        match memberLemma inner "v" with
                        | Some call -> call + "; "
                        | None -> ""

                    line (sprintf "  | (k, v) :: t -> %srt_entries_%s #num #flt ((k, v) :: acc) t" innerCall n)
                    line ""
                | _ -> ()

            line "(* ======================================================================================"
            line "   4. TOTALITY. That the decoders type-check at `Tot` is the termination proof; what the"
            line "      lemma adds is that the outcome is exactly one of the two, for EVERY input — the"
            line "      failure classification is exhaustive rather than merely non-empty."
            line "   ====================================================================================== *)"
            line ""

            line "let dec_node_total (#num #flt: eqtype) (el: jval num flt)"
            line "  : Lemma (ensures (Ok? (dec_node el) \\/ Error? (dec_node el)) /\\"
            line "                   ~(Ok? (dec_node el) /\\ Error? (dec_node el)))"
            line "  = ()"
            line ""

            Ok(out.ToString())

    /// The theorems over a generated model under the generator's own provenance — the entry
    /// point a caller with no named source uses. `proofsModuleFrom` is the one this repository
    /// calls, because its source is named (fuaran-core Phase 173).
    let proofsModule
        (moduleName: string)
        (modelName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        proofsModuleFrom Provenance.supplied moduleName modelName idl kindTags
