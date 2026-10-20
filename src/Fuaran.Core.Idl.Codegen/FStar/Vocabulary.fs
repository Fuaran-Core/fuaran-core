namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.FStarModel

/// The F* target's VOCABULARY MODULE emitter (Phase 150; split out of `FStar.fs` by Phase 524):
/// the types, the discriminated encoder and the tag-dispatch decoder over the wire model, emitted
/// from the model `FStarModel` computes.
module internal FStarVocabulary =

    // -----------------------------------------------------------------------
    // 7. Emission — the model.
    // -----------------------------------------------------------------------

    let nl = "\n"

    /// Phase 505 — an F* module's [[Stamp]]: one block comment heading the file, over everything
    /// below it.
    let fstarStamp (target: string) (vocabularies: Idl list) (body: string) : string =
        Stamp.generated (CommentStyle.Block("(*", "*)")) target vocabularies body

    /// Phase 293 — one entry of an emitted object literal: a FIXED key the shape places (the
    /// discriminator's tag, the node's `id`, the nested kind object), carrying the F* expression
    /// of its value, or a declared MEMBER under its presence rule. The model has no render step,
    /// so the object LITERAL is its canonical form and the declared order has to be built in —
    /// which is what [[layout]] does, for both the fixed keys and the members.
    type Entry =
        | Fixed of key: string * enc: string
        | Mem of Member

    let entryKey (e: Entry) =
        match e with
        | Fixed(key, _) -> key
        | Mem m -> m.Name

    /// An entry the encoder ALWAYS emits — a fixed key, or a required member.
    let alwaysEmitted (e: Entry) =
        match e with
        | Fixed _ -> true
        | Mem m -> m.Presence.IsNone

    /// The entries of one object in the vocabulary's declared key order: under `Declared` the
    /// fixed keys lead and the members follow in authored order, exactly as `Encode` builds the
    /// object; under `Sorted` the whole list is Ordinal-sorted, fixed keys included, exactly as
    /// `Canonical.write Rendering.CrossHost` leaves it. A member list handed in is already in the closure's order
    /// ([[orderMembers]]), so the sort here only merges the fixed keys into it.
    let layout (order: KeyOrder) (placed: (string * string) list) (ms: Member list) : Entry list =
        let entries = (placed |> List.map Fixed) @ (ms |> List.map Mem)

        match order with
        | KeyOrder.Declared -> entries
        | KeyOrder.Sorted ->
            entries
            |> List.sortWith (fun a b -> System.String.CompareOrdinal(entryKey a, entryKey b))

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
    let encApplied (s: Slot) (e: string) =
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

    /// The implementation of `FStarTarget.presenceSplitAt` — the published entry, which carries its contract.
    let presenceSplitAt = 2

    /// Phase 204 — a constructor whose member list is emitted as bound SUFFIXES (8a below).
    let private suffixed (ms: Member list) =
        (ms |> List.filter _.Presence.IsSome |> List.length) >= presenceSplitAt

    /// One conditional member's suffix: the top-level definition that conses the member onto
    /// the rest of the list, or passes the rest through when the encoder omits it.
    let private suffixName (typeName: string) (label: string) (m: Member) =
        sprintf "sfx_%s__%s__%s" typeName label (snake m.Name)

    /// Phase 224 — a member whose decode reads nothing in the decoder's mutual family: a scalar, a
    /// verbatim value, a sentinel, or a closed string set (whose decoder is top-level, above the
    /// family). Only such a member's read can be hoisted out of the family as a top-level
    /// definition; a record, union, node, list or map member's read calls the family itself.
    let private leafSlot (s: Slot) =
        match s with
        | SStr
        | SInt
        | SBool
        | SFloat
        | SJson
        | SSentinel _
        | SEnum _ -> true
        | SRecord _
        | SUnion _
        | SNode
        | SList _
        | SMap _ -> false

    /// Phase 224 — a conditional member of a SUFFIXED constructor (8a) whose read is a leaf: the
    /// decoder reads it through a named, opaque READER (8a'') and the round trip cites one value
    /// lemma for it. The same set of constructors as the suffixes, so a vocabulary with no suffixes
    /// emits byte-identical text.
    let hoisted (ms: Member list) (m: Member) =
        suffixed ms && m.Presence.IsSome && leafSlot m.Slot

    /// One hoisted member's opaque reader, and the lemma giving its value off the encoded object.
    let readerName (typeName: string) (label: string) (m: Member) =
        sprintf "rd_%s__%s__%s" typeName label (snake m.Name)

    let readerValueName (typeName: string) (label: string) (m: Member) =
        sprintf "rv_%s__%s__%s" typeName label (snake m.Name)

    /// Phase 222 — the per-slot option encoders a suffixed member list applies (8a below).
    let private optEncoderName (s: Slot) = "enc_opt_" + slotName s

    let private dfltEncoderName (s: Slot) = "enc_dflt_" + slotName s

    /// A conditional member's encoding as an OPTION — `None` exactly when the encoder omits it —
    /// which is what its suffix takes. The test is the one the inline form wrote: `None?` for an
    /// optional member, equality with the default literal for an omit-at-default one.
    ///
    /// Phase 222: it is an APPLICATION of a per-slot option encoder in the mutual family —
    /// `enc_opt_<slot>` for an optional member, `enc_dflt_<slot>` (the default passed as an
    /// argument) for an omit-at-default one — and never the `match` / `if` itself. A lookup lemma
    /// re-binds the suffix chain in its BODY, where a `match` in a `let`'s argument is a
    /// computation F* splits the verification condition on, one factor of two per member: 58.6
    /// units of rlimit for the first lookup at k=16. An application has no arms to split.
    ///
    /// Phase 256: for an omit-at-default member whose slot is NOT a leaf — a list, map, record,
    /// union or node, whose encoder is itself in the mutual family — the member's ENCODING is a
    /// third argument, built here on the member binder. Phase 222's wrapper encoded `v` itself
    /// under `(decreases v)`, a recursive call on the very value it decreases on, which the
    /// prover refuses (`Failed to prove: v << v`); here the recursion is on the member, a strict
    /// subterm of the value the calling encoder decreases on, and the wrapper calls nothing. A
    /// leaf slot's encoding calls nothing in the family, so its wrapper keeps the two-argument
    /// shape and every model with no non-leaf default is byte-identical.
    let private encOption (m: Member) (v: string) =
        match m.Presence with
        | Some None -> sprintf "(%s #num #flt %s)" (optEncoderName m.Slot) v
        | Some(Some d) when leafSlot m.Slot -> sprintf "(%s #num #flt (%s) %s)" (dfltEncoderName m.Slot) d v
        | Some(Some d) -> sprintf "(%s #num #flt (%s) %s (%s))" (dfltEncoderName m.Slot) d v (encApplied m.Slot v)
        | None -> invalidArg "m" "only a conditional member has an optional encoding"

    /// One link of a suffixed member list: the local the encoder binds it to, the suffix it
    /// applies, the conditional member it is about, and the two arguments — the member's optional
    /// encoding and the REST, which is the always-emitted members up to the next conditional one
    /// consed onto the next link's local (or onto `[]` for the last).
    type Link =
        { Local: string
          Suffix: string
          Member: Member
          Enc: string
          Rest: string }

    /// One entry's text in an object literal.
    let private entryText (bind: Member -> string) (e: Entry) =
        match e with
        | Fixed(key, enc) -> sprintf "(%s, %s)" (lit key) enc
        | Mem m -> sprintf "(%s, %s)" (lit m.Name) (encApplied m.Slot (bind m))

    /// A suffixed entry list as the always-emitted entries BEFORE the first conditional member (in
    /// key order, reached by `find_field` without meeting a test) and its links, outermost first.
    let suffixChain (typeName: string) (label: string) (entries: Entry list) (bind: Member -> string) =
        let entry = entryText bind

        let start = entries |> List.findIndex (fun e -> not (alwaysEmitted e))
        let prefix = entries |> List.take start

        // Each conditional member opens a group; the always-emitted entries after it ride in it.
        // Groups and their riders are both built reversed and reversed once, so the fold is linear
        // (Phase 293; the `rs @ [ m ]` form it replaces re-walked every rider per member).
        let groups =
            entries
            |> List.skip start
            |> List.fold
                (fun acc (e: Entry) ->
                    match e, acc with
                    | Mem m, _ when m.Presence.IsSome -> (m, []) :: acc
                    | _, (c, rs) :: t -> (c, e :: rs) :: t
                    | _, [] -> failwith "unreachable: the first entry after `start` is conditional")
                []
            |> List.rev
            |> List.map (fun (c, rs) -> c, List.rev rs)

        let n = List.length groups

        let links =
            groups
            |> List.mapi (fun t (c, rs) ->
                let next = if t + 1 < n then sprintf "s%d" (t + 1) else "[]"

                { Local = sprintf "s%d" t
                  Suffix = suffixName typeName label c
                  Member = c
                  Enc = encOption c (bind c)
                  Rest = (rs |> List.map (fun r -> entry r + " :: ") |> String.concat "") + next })

        prefix |> List.map entry, links

    /// The `let` chain binding a suffixed list's links, innermost first so each can name the next.
    let chainLets (links: Link list) =
        links
        |> List.rev
        |> List.map (fun l -> sprintf "let %s = %s #num #flt %s (%s) in" l.Local l.Suffix l.Enc l.Rest)

    /// The object-member list of a declared field set — an inline `match` per conditional
    /// member rather than a higher-order builder, so the whole expression stays a literal
    /// the prover can normalise. At `presenceSplitAt` or more conditional members it is the
    /// SUFFIX chain instead (Phase 204, section 8a): one `let` per conditional member, each
    /// binding that member's suffix applied to the next, so no tail is written twice.
    ///
    /// Phase 293: the FIXED keys the shape places (the discriminator, the node's `id`, the nested
    /// kind object) are entries of the same literal, positioned by [[layout]] under the declared
    /// key order rather than prepended — so a `Sorted` vocabulary whose discriminator sorts among
    /// the members, or whose envelope member sorts ahead of `id`, is emitted where the wire has it.
    let private encMembers
        (order: KeyOrder)
        (owner: string * string)
        (placed: (string * string) list)
        (ms: Member list)
        (bind: Member -> string)
        =
        let entries = layout order placed ms

        let rec go rest =
            match rest with
            | [] -> "[]"
            | Fixed(key, enc) :: t -> sprintf "(%s, %s) :: %s" (lit key) enc (go t)
            | Mem(m: Member) :: t ->
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

        if suffixed ms then
            let typeName, label = owner
            let prefix, links = suffixChain typeName label entries bind

            sprintf
                "(%s %s%s)"
                (chainLets links |> String.concat " ")
                (prefix |> List.map (fun e -> e + " :: ") |> String.concat "")
                (List.head links).Local
        else
            go entries

    /// A member's index in the binder list, for the `f0 f1 …` pattern binders.
    let bindOf (ms: Member list) =
        let idx = ms |> List.mapi (fun i m -> m.Name, sprintf "f%d" i) |> Map.ofList
        fun (m: Member) -> idx[m.Name]

    let indent (n: int) (s: string) = String.replicate n "  " + s

    /// The decoder of one member off the object bound to `el`, as a `let`-bound outcome.
    /// Each member is read independently and combined at the end, so a vocabulary with
    /// twenty optional members emits twenty reads rather than 2^20 continuations.
    ///
    /// Phase 224: this is the READ alone — the outcome's type and the expression — which a hoisted
    /// member's opaque reader (8a'') carries as its body and every other member writes inline.
    let private decRead (el: string) (m: Member) =
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
            // The sentinel carries nothing, so the model reads the member without inspecting it:
            // its PRESENCE is the only information there is. The generated F# also refuses a
            // value that is not the sentinel (`dSentinel`, Phase 347) — a refusal on input no
            // encoder writes, which the model's round trip has no need to state.
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

        outcomeTy, body

    /// The decoder of one member off the object bound to `el`, as a `let`-bound outcome.
    let private decMember (el: string) (i: int) (m: Member) =
        let outcomeTy, body = decRead el m
        sprintf "let o%d : outcome (%s) = %s in" i outcomeTy body

    /// Phase 224 — one member of the constructor `label` of `typeName` (its members `ms`): read
    /// through its opaque reader when it is hoisted (8a''), else written inline as before.
    let private decMemberOf (typeName: string) (label: string) (ms: Member list) (el: string) (i: int) (m: Member) =
        if hoisted ms m then
            let outcomeTy, _ = decRead el m
            sprintf "let o%d : outcome (%s) = %s #num #flt %s in" i outcomeTy (readerName typeName label m) el
        else
            decMember el i m

    /// Combine the `let`-bound member outcomes into the constructor application.
    /// `lead` are the binders that precede the members in the application (`i` for the node's id).
    let private decCombineWith (lead: string list) (ms: Member list) (ctor: string) =
        let rec go i rest =
            match rest with
            | [] ->
                let args =
                    lead @ (ms |> List.mapi (fun j _ -> sprintf "f%d" j)) |> String.concat " "

                if args = "" then
                    sprintf "Ok %s" ctor
                else
                    sprintf "Ok (%s %s)" ctor args
            | _ :: t -> sprintf "(match o%d with | Error e -> Error e | Ok f%d -> %s)" i i (go (i + 1) t)

        go 0 ms

    let private decCombine (ms: Member list) (ctor: string) = decCombineWith [] ms ctor

    // -----------------------------------------------------------------------
    // 8. The model module.
    // -----------------------------------------------------------------------

    let private header
        (provenance: Provenance)
        (moduleName: string)
        (idl: Idl)
        (kindTags: string list)
        (c: Model)
        (refused: KindVerdict list)
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
          sprintf "   %s — generated from" moduleName
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
          // Phase 525 — stated where it applies, so a vocabulary without such a slot keeps its bytes.
          if c.DeclaredWire then
              yield!
                  [ "     - A host-codec member that DECLARES its wire form is modelled AT that form: the"
                    "       decoder checks the declared type exactly as every host's decoder does before"
                    "       the codec runs. A declared string FORMAT is refused by name, because the"
                    "       model's string carries no format predicate." ]
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
          // Phase 293 — the shape is ENCODED, not announced: the two lines below say what the
          // encoder's literals carry, and the test suite holds them to the encoder's text.
          (match c.Shape with
           | NodeEnvelopeShape.NestedKind ->
               "   The node object carries `id`, the kind object under `kind` and the envelope's members;"
           | NodeEnvelopeShape.FlatKind ->
               "   The node is ONE object carrying the discriminator, `id`, the kind's members and the")
          (match c.Shape with
           | NodeEnvelopeShape.NestedKind -> "   the kind object carries the discriminator and the kind's members."
           | NodeEnvelopeShape.FlatKind ->
               "   envelope's: `node` has one constructor per kind, and there is no kind object.")
          (match c.KeyOrder with
           | KeyOrder.Sorted -> "   Every object literal is Ordinal-sorted, the fixed keys merged among the members."
           | KeyOrder.Declared ->
               "   Every object literal is in the encoder's construction order: fixed keys, then members")
          (match c.KeyOrder with
           | KeyOrder.Sorted -> "   A shape the literal cannot carry is refused by name, never emitted."
           | KeyOrder.Declared -> "   as declared. A shape the literal cannot carry is refused by name, never emitted.")
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

    /// The implementation of `FStarTarget.vocabularyModuleFrom` — the published entry, which carries its contract.
    let vocabularyModuleFrom
        (provenance: Provenance)
        (moduleName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        match model idl kindTags with
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
                match CodegenLookup.tryEnum idl name with
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
            //
            // Phase 293 — the node's TYPE follows the declared envelope. Under the NESTED
            // envelope the node is one constructor carrying `id`, the kind value and the
            // envelope's members, and `vkind` is the kind union whose object sits under `kind`.
            // Under the FLAT envelope there is no kind object on the wire — the discriminator,
            // the id, the kind's members and the envelope's share one object — so the node IS
            // the kind union: one constructor per kind carrying `id`, that kind's members and the
            // envelope's, and no `vkind` type at all. The model is of the wire, and the wire has
            // one object there.
            line (typeHead "node")
            let envArgs = c.Envelope
            let flat = c.Shape = NodeEnvelopeShape.FlatKind

            match c.Shape with
            | NodeEnvelopeShape.NestedKind ->
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
                    line (sprintf "  | %s : %s" (ctorName "vkind" tag) (ctorArgs ms "vkind num flt"))
            | NodeEnvelopeShape.FlatKind ->
                for tag, ms in c.Kinds do
                    line (
                        sprintf
                            "  | %s : id:string -> %s"
                            (ctorName "node" tag)
                            (ctorArgs (flatNodeMembers c ms) "node num flt")
                    )

            for s in declared do
                match s with
                | SNode -> ()
                | SRecord _ ->
                    let n = slotName s
                    let ms = c.Members[n]
                    line ""
                    line (typeHead n)
                    line (sprintf "  | %s : %s" (ctorName n "Mk") (ctorArgs ms (n + " num flt")))
                | SUnion _ ->
                    let n = slotName s
                    line ""
                    line (typeHead n)

                    for tag, ms in c.Cases[n] do
                        line (sprintf "  | %s : %s" (ctorName n tag) (ctorArgs ms (n + " num flt")))
                | _ -> ()

            line ""

            // ---- encoders ----------------------------------------------------
            line "(* ======================================================================================"
            line "   3. The encoder — the discriminated envelope, keys in the vocabulary's canonical"
            line "      order. The model has no render step, so the object LITERAL is the canonical form."
            line "      Recursion is on the MODEL value, where F*'s subterm order spans the whole family."
            line "   ====================================================================================== *)"
            line ""

            // ---- 8a. the suffixes (Phase 204) --------------------------------
            // A constructor at `presenceSplitAt` or more conditional members encodes its member
            // list through one SUFFIX per conditional member, bound once by the encoder's `let`
            // chain — where the inline form wrote the tail into both arms of every member's test
            // and emitted 2^k text (5,318,686 characters for one kind at k=16, which the pinned
            // prover could not load). Each suffix is top-level and NAMED, so it is a term a lemma
            // can be stated about, and OPAQUE to the SMT solver, so no query sees through it except
            // the three per-suffix lemmas the proof script proves by revealing it. It takes the
            // member already ENCODED (as an option) rather than the member itself, which is what
            // keeps it out of the recursive family: it calls nothing.
            let suffixOwners =
                [ if flat then
                      for tag, ms in c.Kinds do
                          yield "node", tag, flatNodeMembers c ms
                  else
                      yield "node", "Node", envArgs

                      for tag, ms in c.Kinds do
                          yield "vkind", tag, ms
                  for s in declared do
                      match s with
                      | SRecord _ ->
                          let n = slotName s
                          yield n, "Mk", c.Members[n]
                      | SUnion _ ->
                          let n = slotName s

                          for tag, ms in c.Cases[n] do
                              match c.Transparent.TryFind n with
                              | Some t when t = tag -> ()
                              | _ -> yield n, tag, ms
                      | _ -> () ]
                |> List.filter (fun (_, _, ms) -> suffixed ms)

            if not suffixOwners.IsEmpty then
                line "(* The member-list SUFFIXES — one per conditional member of a constructor carrying two or"
                line "   more, bound once by the encoder below rather than written into both arms of a test."
                line "   Opaque to the solver: only the per-suffix lemmas of the proof script look inside. *)"
                line ""

            for typeName, label, ms in suffixOwners do
                for m in ms do
                    if m.Presence.IsSome then
                        line "[@@\"opaque_to_smt\"]"

                        line (
                            sprintf
                                "let %s (#num #flt: eqtype) (e: option (jval num flt)) (rest: list (string & jval num flt)) : Tot (list (string & jval num flt)) ="
                                (suffixName typeName label m)
                        )

                        line (sprintf "  match e with | None -> rest | Some v -> (%s, v) :: rest" (lit m.Name))
                        line ""

            // ---- 8a''. the member readers (Phase 224) ------------------------
            // The decoder's side of the same move. Each hoisted member's READ is a
            // named, opaque top-level definition, and the proof script states its value off the
            // encoded object in one lemma, so the round trip threads k opaque applications rather
            // than unfolding k inlined reads inside the decoder's nest of outcomes. The boundary: a
            // member whose read CALLS the decoder family (a record, union, node, list or map) has
            // no definition above that family to be hoisted into, so it is read inline and its arm
            // keeps the two-way citation of its lookups, exactly as Phase 222 emitted it.
            let readers =
                [ for typeName, label, ms in suffixOwners do
                      for m in ms do
                          if hoisted ms m then
                              yield typeName, label, m ]

            if not readers.IsEmpty then
                line "(* The member READERS — one per conditional member of a suffixed constructor whose read calls"
                line "   nothing in the decoder family, applied by the decoder below rather than inlined into it."
                line "   Opaque to the solver: only the per-reader value lemmas of the proof script look inside. *)"
                line ""

            for typeName, label, m in readers do
                let outcomeTy, body = decRead "el" m
                line "[@@\"opaque_to_smt\"]"

                line (
                    sprintf
                        "let %s (#num #flt: eqtype) (el: jval num flt) : Tot (outcome (%s)) ="
                        (readerName typeName label m)
                        outcomeTy
                )

                line (sprintf "  %s" body)
                line ""

            let disc = idl.Wire.Discriminator
            let mutable firstEnc = true

            let encHead (sig_: string) =
                let kw = if firstEnc then "let rec" else "and"
                firstEnc <- false
                sprintf "%s %s" kw sig_

            // the node — Phase 293: the fixed keys the shape places are entries of the literal,
            // positioned by the declared key order (see `encMembers`), not a prefix.
            let order = c.KeyOrder

            line (
                encHead (sprintf "enc_node (#num #flt: eqtype) (x: node num flt) : Tot (jval num flt) (decreases x) =")
            )

            line "  match x with"

            match c.Shape with
            | NodeEnvelopeShape.NestedKind ->
                let envBinders =
                    envArgs |> List.mapi (fun i _ -> sprintf "e%d" i) |> String.concat " "

                let envList =
                    encMembers order ("node", "Node") [ "id", "JStr i"; "kind", "enc_vkind k" ] envArgs (fun m ->
                        "e" + string (List.findIndex (fun (x: Member) -> x.Name = m.Name) envArgs))

                line (sprintf "  | %s i k %s ->" (ctorName "node" "Node") envBinders)
                line (sprintf "    JObj (%s)" envList)
                line ""

                line (
                    encHead (
                        sprintf "enc_vkind (#num #flt: eqtype) (x: vkind num flt) : Tot (jval num flt) (decreases x) ="
                    )
                )

                line "  match x with"

                for tag, ms in c.Kinds do
                    let bs = binders ms
                    let bind = bindOf ms

                    line (sprintf "  | %s %s ->" (ctorName "vkind" tag) (String.concat " " bs))
                    line (sprintf "    JObj (%s)" (encMembers order ("vkind", tag) [ disc, "JStr " + lit tag ] ms bind))
            | NodeEnvelopeShape.FlatKind ->
                for tag, kindMs in c.Kinds do
                    let ms = flatNodeMembers c kindMs
                    let bs = binders ms
                    let bind = bindOf ms

                    line (sprintf "  | %s i %s ->" (ctorName "node" tag) (String.concat " " bs))

                    line (
                        sprintf
                            "    JObj (%s)"
                            (encMembers order ("node", tag) [ disc, "JStr " + lit tag; "id", "JStr i" ] ms bind)
                    )

            line ""

            for s in declared do
                match s with
                | SNode -> ()
                | SRecord _ ->
                    let n = slotName s
                    let ms = c.Members[n]
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
                            (encMembers order (n, "Mk") [] ms bind)
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
                        let bs = binders ms
                        let bind = bindOf ms

                        match c.Transparent.TryFind n with
                        | Some t when t = tag ->
                            // The declared transparent case rides BARE — no envelope at all.
                            let only = List.head ms
                            line (sprintf "  | %s f0 -> %s" (ctorName n tag) (encApplied only.Slot "f0"))
                        | _ ->
                            line (sprintf "  | %s %s ->" (ctorName n tag) (String.concat " " bs))

                            line (
                                sprintf "    JObj (%s)" (encMembers order (n, tag) [ disc, "JStr " + lit tag ] ms bind)
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

            // ---- 8a'. the option encoders (Phase 222) --------------------------
            // One per SLOT a suffixed constructor's conditional member carries, so that every
            // link of a suffix chain applies a function to its member instead of testing it: an
            // application where Phase 204 wrote a `match` (or an `if`, for an omit-at-default
            // member, whose default is an ARGUMENT so one encoder serves every default). In the
            // mutual family because the member's own encoder may be — a record, a union, a list.
            let optionEncoders =
                suffixOwners
                |> List.collect (fun (_, _, ms) ->
                    ms
                    |> List.choose (fun m ->
                        match m.Presence with
                        | Some None -> Some(true, m.Slot)
                        | Some(Some _) -> Some(false, m.Slot)
                        | None -> None))
                |> List.distinct

            for isOpt, s in optionEncoders do
                let ty = slotType s

                if isOpt then
                    line (
                        encHead (
                            sprintf
                                "%s (#num #flt: eqtype) (o: option (%s)) : Tot (option (jval num flt)) (decreases o) ="
                                (optEncoderName s)
                                ty
                        )
                    )

                    line (sprintf "  match o with | None -> None | Some w -> Some (%s)" (encApplied s "w"))
                elif not (leafSlot s) then
                    // Phase 256 — a non-leaf slot's wrapper RECEIVES the encoding (see `encOption`):
                    // encoding `v` here would recurse on the value this definition decreases on.
                    line (
                        encHead (
                            sprintf
                                "%s (#num #flt: eqtype) (d: %s) (v: %s) (e: jval num flt) : Tot (option (jval num flt)) (decreases v) ="
                                (dfltEncoderName s)
                                ty
                                ty
                        )
                    )

                    line "  if v = d then None else Some e"
                else
                    line (
                        encHead (
                            sprintf
                                "%s (#num #flt: eqtype) (d: %s) (v: %s) : Tot (option (jval num flt)) (decreases v) ="
                                (dfltEncoderName s)
                                ty
                                ty
                        )
                    )

                    line (sprintf "  if v = d then None else Some (%s)" (encApplied s "v"))

                line ""

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

            match c.Shape with
            | NodeEnvelopeShape.NestedKind ->
                line (
                    sprintf
                        "  let ok : outcome (vkind num flt) = (match get_prop %s el with | Error e -> Error e | Ok v -> dec_vkind v) in"
                        (lit "kind")
                )

                envArgs
                |> List.iteri (fun i m -> line (indent 1 (decMemberOf "node" "Node" envArgs "el" i m)))

                line (
                    sprintf
                        "  (match oid with | Error e -> Error e | Ok i -> (match ok with | Error e -> Error e | Ok k -> %s))"
                        (decCombineWith [ "i"; "k" ] envArgs (ctorName "node" "Node"))
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
                    line (sprintf "    if tag = %s then" (lit tag))

                    ms
                    |> List.iteri (fun i m -> line (indent 3 (decMemberOf "vkind" tag ms "el" i m)))

                    line (indent 3 (decCombine ms (ctorName "vkind" tag)))
                    line "    else"

                line (sprintf "    Error (%s ^ tag)" (lit "unknown kind: "))
            | NodeEnvelopeShape.FlatKind ->
                // Phase 293 — the flat envelope: the tag dispatch IS the node decoder, and each
                // arm reads that kind's members and the envelope's off the one object. `id` is
                // read ahead of the dispatch, as `Decode.decodeNode` reads it.
                line (sprintf "  match str_field %s el with" (lit disc))
                line "  | Error e -> Error e"
                line "  | Ok tag ->"

                for tag, kindMs in c.Kinds do
                    let ms = flatNodeMembers c kindMs
                    line (sprintf "    if tag = %s then" (lit tag))

                    ms
                    |> List.iteri (fun i m -> line (indent 3 (decMemberOf "node" tag ms "el" i m)))

                    line (
                        indent
                            3
                            (sprintf
                                "(match oid with | Error e -> Error e | Ok i -> %s)"
                                (decCombineWith [ "i" ] ms (ctorName "node" tag)))
                    )

                    line "    else"

                line (sprintf "    Error (%s ^ tag)" (lit "unknown kind: "))

            line ""

            for s in declared do
                match s with
                | SNode -> ()
                | SRecord _ ->
                    let n = slotName s
                    let ms = c.Members[n]

                    line (
                        decHead (
                            sprintf
                                "dec_%s (#num #flt: eqtype) (el: jval num flt) : Tot (outcome (%s num flt)) (decreases %%[(jsize el <: nat); 0]) ="
                                n
                                n
                        )
                    )

                    ms |> List.iteri (fun i m -> line (indent 1 (decMemberOf n "Mk" ms "el" i m)))
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
                            line (sprintf "    if tag = %s then" (lit tag))

                            ms |> List.iteri (fun i m -> line (indent 3 (decMemberOf n tag ms "el" i m)))

                            line (indent 3 (decCombine ms (ctorName n tag)))
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

            Ok(fstarStamp "F* model" [ idl ] (out.ToString()))

    /// The implementation of `FStarTarget.vocabularyModule` — the published entry, which carries its contract.
    let vocabularyModule (moduleName: string) (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        vocabularyModuleFrom Provenance.supplied moduleName idl kindTags
