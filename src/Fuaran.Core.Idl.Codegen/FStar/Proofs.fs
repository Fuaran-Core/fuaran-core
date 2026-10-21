namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.FStarModel
open Fuaran.Core.Idl.Emit.FStarVocabulary

/// The F* target's PROOFS MODULE emitter (Phase 150; split out of `FStar.fs` by Phase 524): the
/// round-trip and totality theorems over exactly the definitions `FStarVocabulary` emits, from
/// the same walk, with the lemma shape Phase 182 fixed.
module internal FStarProofs =

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
    // PHASE 204 removed that limit (8a: named, opaque suffixes; the model is linear in k and loads
    // at k=16 in 19 s) and proves each lookup as a chain of per-suffix steps. Measured on the same
    // probe: every lookup discharges at k=5, 8 and 12 under `--z3rlimit 40`, including the k=12
    // `__absent` Phase 182 could not prove. At k=16 the model loads and every per-suffix step
    // discharges, but the first lookup needs ~59 units of rlimit (3.7 at k=12): the let chain the
    // lookup BODY re-binds is a computation, and F* splits its verification condition on both
    // arms of every `match` in it — ~2x per member, the same exponent moved into the VC.
    //
    // PHASE 222 closed that: each link's argument is an APPLICATION of a per-slot option encoder
    // (8a'), not a `match`, so the body has no arms to split. Same probe: every k=16 lookup
    // discharges at under half a unit (the first: 58.6 -> 0.15). What remains at k=16 is the
    // ROUND-TRIP arm (345 units; 24.4 at k=12), and it is NOT the body's split — an arm citing
    // matchless per-member lemmas still costs 271. Successor: Phase 224. See `proofs/README.md`
    // and DECISIONS D54.
    //
    // PHASE 224 closed that: the cost was the DECODER's term — sixteen member reads inlined into
    // the kind's arm, which the arm's query unfolds inside a seventeen-deep nest of outcomes. Each
    // hoisted member's read is now a named, opaque reader (8a''), and one VALUE lemma per reader
    // (`rv_<T>__<Ctor>__<member>`, emitted beside the lookups) states it off the encoded object,
    // so the arm cites k value lemmas over k opaque applications. Same probe, `--z3rlimit 40`:
    // the k=16 round-trip arm 345 -> 0.33 (k = 5, 8, 12: 0.18, 0.22, 0.27), no lookup moved.
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
    /// sized to the entries the walk can meet with a margin for the `Ok?` refinement `get_prop`
    /// carries out of the lookup. Scoped to the lemma rather than set on the file: fuel the family
    /// does not need is fuel every other query pays for. SINCE PHASE 204 the walk no longer meets
    /// every member: a suffix is opaque and each is crossed by a cited step, so `find_field` unfolds
    /// only over the lead entries, the always-emitted members before the first conditional one, and
    /// the always-emitted members riding in the suffixes, and the fuel is sized to those. Measured
    /// at k=16 (`proofs/README.md`): cutting it from Phase 182's thirty-eight to eight did NOT change
    /// the verdict there — the cost is elsewhere, and the README names where — so this is sizing the
    /// fuel to the walk, not a remedy.
    let private lookupFuel (leads: int) (members: int) = max 8 (2 * (leads + members + 2))

    /// One constructor of a modelled type, as the proof emitter sees it: the model's constructor
    /// name, the binders that precede its members in the pattern (`i k` for the node), the
    /// members under their presence rules, and the lemma calls that precede the members' own
    /// (`rt_vkind k` for the node).
    type private Ctor =
        {
            Label: string
            CtorName: string
            Lead: string list
            /// Phase 293 — the fixed keys the shape places in this constructor's object, with
            /// the F* expression of each value: the entries of the literal that are not members.
            Fixed: (string * string) list
            Members: Member list
            Extra: string list
        }

    /// A constructor's members, in the closure's declared key order (already ordered there).
    let private ctorMembers (c: Ctor) = c.Members

    /// The entries of a constructor's object literal — fixed keys and members, in key order —
    /// exactly as the encoder lays them out, because the lookups below are about THAT literal.
    let private ctorEntries (order: KeyOrder) (c: Ctor) = layout order c.Fixed c.Members

    let private ctorPattern (c: Ctor) =
        String.concat
            " "
            (c.CtorName
             :: (c.Lead @ (ctorMembers c |> List.mapi (fun i _ -> sprintf "f%d" i))))

    /// The index of the first CONDITIONAL entry in key order, or the entry count when the
    /// constructor has none. Every entry BEFORE it is reached by `find_field` without meeting a
    /// branch, so it needs no lookup lemma of its own — which is why a constructor's lemma count
    /// is `2k + r' + 1` and not `2k + r + 1`.
    let private firstConditional (entries: Entry list) =
        match entries |> List.tryFindIndex (fun e -> not (alwaysEmitted e)) with
        | Some i -> i
        | None -> List.length entries

    /// One key's lookup lemma name — `__present` / `__absent` are appended for a conditional.
    let private lookupNameOf (typeName: string) (label: string) (key: string) =
        sprintf "lk_%s__%s__%s" typeName label (snake key)

    let private lookupName (typeName: string) (label: string) (m: Member) = lookupNameOf typeName label m.Name

    /// The presence LOOKUPS of one type's constructors — see 8b above for the shape. Emitted ahead
    /// of the round-trip family and OUTSIDE it: they recurse on nothing, so each can carry the
    /// scoped fuel its own walk needs, where a mutual family admits one option set for all of it.
    let private emitLookups
        (line: string -> unit)
        (order: KeyOrder)
        (typeName: string)
        (fsType: string)
        (ctors: Ctor list)
        =
        let multi = List.length ctors > 1

        for c in ctors do
            let ms = ctorMembers c
            let cs = conditionals ms

            if List.length cs >= presenceSplitAt then
                let pattern = ctorPattern c
                let entries = ctorEntries order c
                let bind = bindOf ms

                let fuel =
                    lookupFuel (List.length c.Fixed) (ms |> List.filter _.Presence.IsNone |> List.length)

                let start = firstConditional entries

                let guarded (body: string) (fallback: string) =
                    if multi then
                        sprintf "match x with | %s -> %s | _ -> %s" pattern body fallback
                    else
                        sprintf "match x with | %s -> %s" pattern body

                // Phase 204: the constructor's suffix chain, bound in each lookup's body exactly as
                // the encoder binds it, so the terms the steps below are cited at ARE the encoding's.
                let _, links = suffixChain typeName c.Label entries bind
                let lets = chainLets links

                let stepName (l: Link) (step: string) =
                    "sk_" + l.Suffix.Substring 4 + "__" + step

                let step (l: Link) (s: string) (key: string option) =
                    match key with
                    | Some k -> sprintf "%s #num #flt %s %s (%s)" (stepName l s) (lit k) l.Enc l.Rest
                    | None -> sprintf "%s #num #flt %s (%s)" (stepName l s) l.Enc l.Rest

                // The three per-suffix lemmas — the ONLY place an opaque suffix is looked inside.
                line (
                    sprintf "(* The suffixes of %s — each revealed once, here, and cited by name below. *)" c.CtorName
                )

                for l in links do
                    let args = "(e: option (jval num flt)) (rest: list (string & jval num flt))"

                    let reveal =
                        sprintf "  = reveal_opaque (`%%%s) (%s #num #flt e rest)" l.Suffix l.Suffix

                    line (
                        sprintf
                            "let %s (#num #flt: eqtype) (n: string) %s : Lemma (requires (n <> %s)) (ensures (find_field n (%s e rest) == find_field n rest))"
                            (stepName l "skip")
                            args
                            (lit l.Member.Name)
                            l.Suffix
                    )

                    line reveal

                    line (
                        sprintf
                            "let %s (#num #flt: eqtype) %s : Lemma (requires (Some? e)) (ensures (find_field %s (%s e rest) == Ok (Some?.v e)))"
                            (stepName l "hit")
                            args
                            (lit l.Member.Name)
                            l.Suffix
                    )

                    line reveal

                    line (
                        sprintf
                            "let %s (#num #flt: eqtype) %s : Lemma (requires (None? e)) (ensures (%s e rest == rest))"
                            (stepName l "none")
                            args
                            l.Suffix
                    )

                    line reveal

                line ""

                let emit (name: string) (why: string) (requires: string option) (ensures: string) (calls: string list) =
                    let req =
                        match requires with
                        | Some r -> sprintf "(requires (%s)) " r
                        | None -> ""

                    line (sprintf "(* %s — %s *)" name why)
                    line (sprintf "#push-options \"--fuel %d --ifuel 4\"" fuel)

                    line (
                        sprintf "let %s (#num #flt: eqtype) (x: %s) : Lemma %s(ensures (%s)) =" name fsType req ensures
                    )

                    line "  match x with"
                    line (sprintf "  | %s ->" pattern)

                    for l in lets do
                        line (indent 2 l)

                    calls
                    |> List.iteri (fun j call ->
                        line (indent 2 (if j + 1 < List.length calls then call + ";" else call)))

                    if multi then
                        line "  | _ -> ()"

                    line "#pop-options"
                    line ""

                // The link an entry rides in: its own for a conditional member, else that of the
                // last conditional member before it in key order. Positions are ENTRY positions
                // (Phase 293): a fixed key sitting after a conditional member rides in its link
                // and is looked up through the same steps as an always-emitted member there.
                let position (key: string) =
                    entries |> List.findIndex (fun e -> entryKey e = key)

                let linkOf (key: string) =
                    links |> List.findIndexBack (fun l -> position l.Member.Name <= position key)

                let skipsTo (t: int) (key: string) =
                    links |> List.take t |> List.map (fun l -> step l "skip" (Some key))

                let found (key: string) (enc: string) =
                    guarded (sprintf "get_prop %s (enc_%s #num #flt x) == Ok (%s)" (lit key) typeName enc) "True"

                entries
                |> List.iteri (fun i e ->
                    if i >= start then
                        match e with
                        | Fixed(key, enc) ->
                            let t = linkOf key

                            emit
                                (lookupNameOf typeName c.Label key)
                                (key + " — a fixed key, at a position the conditionals before it move")
                                (if multi then Some(sprintf "%s? x" c.CtorName) else None)
                                (found key enc)
                                (skipsTo (t + 1) key)
                        | Mem m ->
                            let lk = lookupName typeName c.Label m
                            let b = bind m
                            let t = linkOf m.Name

                            match m.Presence with
                            | None ->
                                emit
                                    lk
                                    (m.Name + " — always emitted, at a position the conditionals before it move")
                                    (if multi then Some(sprintf "%s? x" c.CtorName) else None)
                                    (found m.Name (encApplied m.Slot b))
                                    (skipsTo (t + 1) m.Name)
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
                                    (found m.Name (encApplied m.Slot present))
                                    (skipsTo t m.Name @ [ step links[t] "hit" None ])

                                // THE NEGATIVE LOOKUP, as a chain of k cheap steps over the named
                                // suffixes rather than one query over the 2^(k-1) shapes after it.
                                emit
                                    (lk + "__absent")
                                    (caption cond false)
                                    (Some(guarded (condition cond false) "false"))
                                    (sprintf "Error? (get_prop %s (enc_%s #num #flt x))" (lit m.Name) typeName)
                                    (skipsTo t m.Name
                                     @ [ step links[t] "none" None ]
                                     @ (links |> List.skip (t + 1) |> List.map (fun l -> step l "skip" (Some m.Name)))))

                // Phase 224 — one VALUE lemma per hoisted member's reader: the reader applied to the
                // encoded object is `Ok` the member, proved by revealing the reader once, here, and
                // citing the member's two lookups above. The round trip cites these by name and never
                // looks inside a reader, so its query holds k opaque applications and nothing more.
                ms
                |> List.iteri (fun i m ->
                    match m.Presence with
                    | Some d when hoisted ms m ->
                        let rd = readerName typeName c.Label m
                        let applied = sprintf "%s #num #flt (enc_%s #num #flt x)" rd typeName
                        let b = sprintf "f%d" i

                        let cite =
                            (citeConditional
                                (lookupName typeName c.Label m)
                                { Binder = b
                                  Name = m.Name
                                  Default = d })
                                .TrimEnd
                                ';'

                        // A closed string set's own round trip is a lemma of section 1, cited
                        // for the present value exactly as the constructor's arm cites it.
                        let own =
                            memberLemma m.Slot (if d.IsNone then "w" else b)
                            |> Option.map (fun call ->
                                if d.IsNone then
                                    sprintf "(match %s with | None -> () | Some w -> %s); " b call
                                else
                                    call + "; ")
                            |> Option.defaultValue ""

                        let req =
                            if multi then
                                sprintf "(requires (%s? x)) " c.CtorName
                            else
                                ""

                        line (
                            sprintf
                                "(* %s — the value of `%s` off the encoded object *)"
                                (readerValueName typeName c.Label m)
                                rd
                        )

                        line (
                            sprintf
                                "let %s (#num #flt: eqtype) (x: %s) : Lemma %s(ensures (%s)) ="
                                (readerValueName typeName c.Label m)
                                fsType
                                req
                                (guarded (sprintf "%s == Ok %s" applied b) "True")
                        )

                        line (sprintf "  reveal_opaque (`%%%s) (%s);" rd applied)
                        line "  match x with"
                        line (sprintf "  | %s -> %s%s" pattern own cite)

                        if multi then
                            line "  | _ -> ()"

                        line ""
                    | _ -> ())

    /// One type's round trip as a family of lemmas — see 8b above for the shape. `rtHead` supplies
    /// `let rec` / `and`; `line` receives the emitted text.
    let private emitFamily
        (line: string -> unit)
        (order: KeyOrder)
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
                let entries = ctorEntries order c
                let start = firstConditional entries
                let bind = bindOf ms

                line (rtHead (signature name requires tier))
                line "  match x with"
                line (sprintf "  | %s ->" (pattern c))

                entries
                |> List.iteri (fun i e ->
                    if i >= start then
                        match e with
                        // Phase 293: a fixed key after the first conditional member is cited by
                        // its own lookup, exactly as an always-emitted member there is.
                        | Fixed(key, _) ->
                            line (indent 2 (sprintf "%s #num #flt x;" (lookupNameOf typeName c.Label key)))
                        | Mem m ->
                            let lk = lookupName typeName c.Label m

                            match m.Presence with
                            | None -> line (indent 2 (sprintf "%s #num #flt x;" lk))
                            // Phase 224: a hoisted member is cited by its reader's value lemma, with
                            // no presence case in the arm and no read for the query to unfold.
                            | Some _ when hoisted ms m ->
                                line (indent 2 (sprintf "%s #num #flt x;" (readerValueName typeName c.Label m)))
                            | Some d ->
                                line (
                                    indent
                                        2
                                        (citeConditional
                                            lk
                                            { Binder = bind m
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
          sprintf "   %s — generated from the same walk that" moduleName
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
          "   Fuaran.Core's `vocabulary-round-trip` row records the halt. The round trip covers one direction"
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

    /// The implementation of `FStarTarget.proofsModuleFrom` — the published entry, which carries its contract.
    let proofsModuleFrom
        (provenance: Provenance)
        (moduleName: string)
        (modelName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        match model idl kindTags with
        | Error e -> Error e
        | Ok c ->

            let out = System.Text.StringBuilder()
            let line (s: string) = out.Append(s).Append(nl) |> ignore
            let declared = c.Order |> List.filter declares
            let envArgs = c.Envelope

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
            let disc = idl.Wire.Discriminator
            let tagged (tag: string) = disc, "JStr " + lit tag

            let families =
                [ match c.Shape with
                  | NodeEnvelopeShape.NestedKind ->
                      // The node: one constructor, `id` and the kind ahead of the envelope members,
                      // and the kind's own round trip cited ahead of theirs.
                      "node",
                      "node num flt",
                      [ { Label = "Node"
                          CtorName = ctorName "node" "Node"
                          Lead = [ "i"; "k" ]
                          Fixed = [ "id", "JStr i"; "kind", "enc_vkind k" ]
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
                              Fixed = [ tagged tag ]
                              Members = ms
                              Extra = [] } ]
                  | NodeEnvelopeShape.FlatKind ->
                      // Phase 293 — the flat envelope: the node IS the kind union, so `rt_node` is
                      // the case split and `rt_node__<Kind>` each kind's arm, over the one object
                      // that carries the discriminator, `id`, the kind's members and the envelope's.
                      "node",
                      "node num flt",
                      [ for tag, ms in c.Kinds do
                            { Label = tag
                              CtorName = ctorName "node" tag
                              Lead = [ "i" ]
                              Fixed = [ tagged tag; "id", "JStr i" ]
                              Members = flatNodeMembers c ms
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
                              Fixed = []
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
                                  Fixed =
                                    (match c.Transparent.TryFind n with
                                     | Some t when t = tag -> []
                                     | _ -> [ tagged tag ])
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
                emitLookups line c.KeyOrder typeName fsType ctors

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
                emitFamily line c.KeyOrder rtHead typeName fsType ctors

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

            Ok(fstarStamp "F* proofs" [ idl ] (out.ToString()))

    /// The implementation of `FStarTarget.proofsModule` — the published entry, which carries its contract.
    let proofsModule
        (moduleName: string)
        (modelName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        proofsModuleFrom Provenance.supplied moduleName modelName idl kindTags
