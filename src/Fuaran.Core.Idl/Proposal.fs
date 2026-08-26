namespace Fuaran.Core.Idl

open Fuaran.Core

// ---------------------------------------------------------------------------
// A vocabulary-change PROPOSAL, and the spike that prices it.
//
// A domain's node vocabulary is a closed set, and growing it is the most
// expensive change such a domain can make: every host renders the new case, the
// wire corpus grows, the schema grows, and every downstream consumer carries one
// more near-synonym to disambiguate against. The expensive part of deciding is
// therefore not the decision — it is establishing, cheaply enough that anyone
// bothers, what the change would actually DO.
//
// This module is that cheap establishment. A proposal is DATA: a delta over the
// [[Idl]] expressed in the same vocabulary `Artifact.render` emits, a set of
// candidate wire fixtures, the evidence it rests on, and — mandatorily — the
// alternative dispositions the same demand could take instead. From those ~10
// lines the spike derives the generated legs, the corpus verdict, a generative
// cross-leg sweep over the delta, and the stability/obligation report, in
// process, in seconds.
//
// **Three invariants, and they are the point of the module rather than caveats
// on it.**
//
//   1. **Nothing here writes a vocabulary.** `applyDelta` returns a NEW `Idl`
//      value; no file is touched, no branch is cut, no declaration is edited.
//      A spike is an in-memory question, so a spike that is abandoned costs
//      exactly nothing and leaves exactly nothing behind. There is deliberately
//      no function in this module that persists a post-delta vocabulary.
//   2. **Nothing here decides.** The report says what the change costs and
//      whether the legs survive it. It carries no verdict field, no score, and
//      no recommendation, because a green spike is not an argument for
//      admission — it is the removal of one objection out of many, most of which
//      are judgements this file cannot make (semantic correctness, accessibility,
//      the confusion tax, whether the pattern is irreducible at all).
//   3. **A proposal that cites nothing is refused at the door.** Absence of a
//      signal is not a signal, so [[validate]] rejects an evidence entry whose
//      run reference or prompt digest is missing rather than accepting it as a
//      weaker citation. Likewise the alternative dispositions are a REQUIRED
//      section: the cheap axes must be priced before the expensive one, and a
//      drafter that may skip them will.
//
// The module is domain-generic, exactly as the rest of the engine is: a
// vocabulary is a plain `Idl` value the caller supplies, and a proposal is a
// plain JSON document the caller reads from wherever it keeps such things.
// ---------------------------------------------------------------------------

/// Which declaration a new field attaches to.
type ProposalOwner =
    /// A node kind, addressed by its `$type` tag.
    | OwnerKind of tag: string
    /// A non-discriminated record (`TRecord`'s target).
    | OwnerRecord of name: string
    /// One case of a `$type`-discriminated union.
    | OwnerUnionCase of union: string * case: string
    /// The node envelope — what every node carries beside `id` and `kind`.
    | OwnerEnvelope
    /// A tree-op case, addressed by its `$type` tag.
    | OwnerOp of tag: string

/// One additive step of a proposal.
///
/// **Additive only, by construction.** There is no `RemoveKind` and no
/// `RetypeField`, and that is a design position rather than an unfinished
/// surface: a removal or a rename is a breaking wire change whose whole cost
/// lives in migration and negotiation, none of which a spike can price, and
/// offering it here would invite a drafter to propose one as though it were the
/// same kind of act. The four cases below are the four shapes a *growth*
/// proposal can take, and they are deliberately ordered cheapest-last-first: the
/// enum case and the field are what most demand actually resolves to.
type ProposalDelta =
    /// A whole new node kind — the expensive axis.
    | AddKind of IdlKind
    /// A new case on an existing value union.
    | AddUnionCase of union: string * case: IdlUnionCase
    /// A new case on an existing closed string set. `host` is the host-language
    /// case identifier when it differs from the wire string.
    | AddEnumCase of enumName: string * wire: string * host: string option
    /// A new field on an existing declaration.
    | AddField of owner: ProposalOwner * field: IdlField

/// One recorded signal a proposal rests on.
///
/// `RunId` and `PromptDigest` are what make the citation CHECKABLE by someone
/// who was not there: the first names the recorded run the sighting came from,
/// the second pins the prompt that produced it, so "the model was taught this
/// and reached for that anyway" is a verifiable claim rather than a
/// recollection. Both are required — see [[validate]].
type ProposalEvidence =
    {
        /// The signal class this citation belongs to, in the vocabulary the
        /// consuming governance document defines. Free-form here on purpose: the
        /// engine is domain-generic and does not own another domain's admission law.
        Signal: string
        /// The recorded run the sighting is drawn from.
        RunId: string
        /// A digest of the prompt that produced it.
        PromptDigest: string
        /// How many sightings this citation covers.
        Count: int
        /// What was seen, in one sentence.
        Detail: string
    }

/// The same demand, priced as something OTHER than the proposed change.
///
/// A proposal must carry one of these per cheap axis (see [[requiredAlternatives]]),
/// because the failure mode this section exists to prevent is not a bad argument
/// — it is a good argument for the expensive axis that never mentions the cheap
/// ones, which is much harder to refuse and no more correct.
type ProposalAlternative =
    {
        /// Which cheaper axis this entry prices.
        Disposition: string
        /// The drafter's read: `cheaper`, `equivalent`, or `insufficient`.
        Verdict: string
        /// Why, in prose. The part only a reader can weigh.
        Argument: string
    }

/// A candidate wire document the change is supposed to make expressible.
///
/// The spike checks both halves of that claim: the fixture must FAIL to decode
/// against the base vocabulary (else the change is unnecessary — the pattern is
/// already expressible and the demand is a teaching problem) and must decode and
/// round-trip against the post vocabulary (else the change does not do what it
/// says).
type ProposalFixture = { Name: string; Wire: string }

/// A complete proposal.
type Proposal =
    {
        /// A stable slug identifying this proposal.
        Id: string
        /// The demand cluster it answers, named as the harvesting side names it.
        Cluster: string
        /// Who or what drafted it. A drafter has no authority; recording it is how
        /// a reader knows whose judgement they are reading.
        DraftedBy: string
        /// ISO-8601 instant.
        DraftedAt: string
        Delta: ProposalDelta list
        Fixtures: ProposalFixture list
        Evidence: ProposalEvidence list
        /// Why the pattern is claimed not to reduce to an existing composition,
        /// role or variant. Drafted here, judged elsewhere.
        Irreducibility: string
        Alternatives: ProposalAlternative list
        /// The distinction between RE-ADMITTING a previously-retired spelling and
        /// admitting a new normalisation of a spelling that was never in the
        /// vocabulary. Required whenever a `normalisation` alternative is priced,
        /// because the two acts have opposite consequences — one re-creates a
        /// confusion the vocabulary deliberately removed, the other does not — and
        /// they are trivially conflated.
        NormalisationDistinction: string
        /// How the pre/post confusion delta will be measured. A plan, not a result:
        /// the measurement is a separate act with its own cost, and a proposal that
        /// asserts the result it has not taken is the failure this field's name
        /// guards against.
        ConfusionPlan: string
    }

[<RequireQualifiedAccess>]
module Proposal =

    /// The proposal-document ENCODING version — bumped when this module's read /
    /// write shape changes, never when a vocabulary it describes changes.
    [<Literal>]
    let version = 1

    /// The cheap axes every proposal must price before the expensive one.
    ///
    /// Three, and each is a different KIND of cheaper answer: leniency absorbs
    /// the demand at the decoder without touching the vocabulary; teaching
    /// absorbs it at the prompt without touching anything; a variant absorbs it
    /// inside a choice the consumer has already made. A proposal silent on any
    /// of the three has not been argued, only asserted.
    let requiredAlternatives = [ "normalisation"; "teaching"; "variant" ]

    // -- reading -------------------------------------------------------------

    let private field (name: string) (v: JVal) : JVal option =
        match v with
        | JObj fields -> fields |> List.tryPick (fun (n, x) -> if n = name then Some x else None)
        | _ -> None

    let private str (name: string) (v: JVal) : string option =
        match field name v with
        | Some(JStr s) -> Some s
        | _ -> None

    let private intOf (name: string) (v: JVal) : int option =
        match field name v with
        | Some(JInt i) -> Some i
        | _ -> None

    let private arr (name: string) (v: JVal) : JVal list =
        match field name v with
        | Some(JArr xs) -> xs
        | _ -> []

    let private tag (v: JVal) : string option = str "$type" v

    /// Read an IDL type from the artifact's own `type` vocabulary.
    ///
    /// **The wire-expressible subset only.** `closure`, `fn`, `opaque`, `hosted`,
    /// `var` and `op` are refused by name rather than silently mapped: each of
    /// them is a HOST declaration decision (what signature the slot has, which
    /// codec owns its content, what placeholder a decoder puts there) that a
    /// data-only proposal has no business making and that no reviewer could check
    /// from the proposal document. A demand that genuinely needs one of them is a
    /// design conversation, not a delta.
    let rec private readType (v: JVal) : Result<IdlType, string> =
        match tag v with
        | Some "str" -> Ok TStr
        | Some "int" -> Ok TInt
        | Some "bool" -> Ok TBool
        | Some "float" -> Ok TFloat
        | Some "json" -> Ok TJson
        | Some "node" -> Ok TNode
        | Some "kind" -> Ok TKind
        | Some "enum" ->
            match str "name" v with
            | Some n -> Ok(TEnum n)
            | None -> Error "enum type has no 'name'"
        | Some "record" ->
            match str "name" v with
            | Some n -> Ok(TRecord n)
            | None -> Error "record type has no 'name'"
        | Some "list" ->
            match field "of" v with
            | Some inner -> readType inner |> Result.map TList
            | None -> Error "list type has no 'of'"
        | Some "map" ->
            match field "values" v with
            | Some inner -> readType inner |> Result.map TMap
            | None -> Error "map type has no 'values'"
        | Some "union" ->
            match str "name" v with
            | None -> Error "union type has no 'name'"
            | Some n ->
                let args = arr "args" v

                (Ok [], args)
                ||> List.fold (fun acc a ->
                    match acc, readType a with
                    | Error e, _ -> Error e
                    | _, Error e -> Error e
                    | Ok xs, Ok t -> Ok(t :: xs))
                |> Result.map (fun xs -> TUnion(n, List.rev xs))
        | Some other ->
            Error(sprintf "type '%s' is a host-surface declaration, not wire data — a proposal cannot mint one" other)
        | None -> Error "type has no '$type'"

    /// Read an authored default value. Scalars and enum cases only — the same set
    /// the F# generator can emit a default expression for, so a proposal cannot
    /// declare a default the generated layer would then fail to compile.
    let private readValue (v: JVal) : Result<IdlValue, string> =
        match tag v with
        | Some "str" ->
            match field "value" v with
            | Some(JStr s) -> Ok(VStr s)
            | _ -> Error "str default has no string 'value'"
        | Some "int" ->
            match field "value" v with
            | Some(JInt i) -> Ok(VInt i)
            | _ -> Error "int default has no integer 'value'"
        | Some "bool" ->
            match field "value" v with
            | Some(JBool b) -> Ok(VBool b)
            | _ -> Error "bool default has no boolean 'value'"
        | Some "float" ->
            match field "value" v with
            | Some(JFloat f) -> Ok(VFloat f)
            | Some(JInt i) -> Ok(VFloat(float i))
            | _ -> Error "float default has no numeric 'value'"
        | Some "enum" ->
            match str "case" v with
            | Some c -> Ok(VEnum c)
            | None -> Error "enum default has no 'case'"
        | Some other -> Error(sprintf "default value of kind '%s' is not proposable" other)
        | None -> Error "default value has no '$type'"

    let private readOptionality (v: JVal) : Result<Optionality, string> =
        match tag v with
        | Some "required" -> Ok Required
        | Some "optional" -> Ok Optional
        | Some "omitDefault" ->
            match field "default" v with
            | Some d -> readValue d |> Result.map OmitDefault
            | None -> Error "omitDefault has no 'default'"
        | Some "hostOnly" ->
            // A host-only slot is wire-invisible by definition, so proposing one
            // proposes nothing a consumer can observe — and it requires a declared
            // host signature this format deliberately cannot carry.
            Error "'hostOnly' is not proposable — it declares a host slot with no wire projection"
        | Some other -> Error(sprintf "unknown optionality '%s'" other)
        | None -> Error "optionality has no '$type'"

    let private sequence (results: Result<'a, string> list) : Result<'a list, string> =
        (Ok [], results)
        ||> List.fold (fun acc r ->
            match acc, r with
            | Error e, _ -> Error e
            | _, Error e -> Error e
            | Ok xs, Ok x -> Ok(x :: xs))
        |> Result.map List.rev

    let private readField (v: JVal) : Result<IdlField, string> =
        match str "name" v, field "type" v, field "optionality" v with
        | Some name, Some t, Some o ->
            readType t
            |> Result.bind (fun ty ->
                readOptionality o
                |> Result.map (fun opt -> { Name = name; Type = ty; Opt = opt }))
        | None, _, _ -> Error "field has no 'name'"
        | _, None, _ -> Error "field has no 'type'"
        | _, _, None -> Error "field has no 'optionality'"

    let private readFields (owner: JVal) : Result<IdlField list, string> =
        arr "fields" owner |> List.map readField |> sequence

    let private readOwner (v: JVal) : Result<ProposalOwner, string> =
        match tag v with
        | Some "kind" ->
            match str "name" v with
            | Some n -> Ok(OwnerKind n)
            | None -> Error "kind owner has no 'name'"
        | Some "record" ->
            match str "name" v with
            | Some n -> Ok(OwnerRecord n)
            | None -> Error "record owner has no 'name'"
        | Some "op" ->
            match str "name" v with
            | Some n -> Ok(OwnerOp n)
            | None -> Error "op owner has no 'name'"
        | Some "unionCase" ->
            match str "union" v, str "case" v with
            | Some u, Some c -> Ok(OwnerUnionCase(u, c))
            | _ -> Error "unionCase owner needs 'union' and 'case'"
        | Some "envelope" -> Ok OwnerEnvelope
        | Some other -> Error(sprintf "unknown owner '%s'" other)
        | None -> Error "owner has no '$type'"

    let private readDelta (v: JVal) : Result<ProposalDelta, string> =
        match str "op" v with
        | Some "addKind" ->
            match field "kind" v with
            | None -> Error "addKind has no 'kind'"
            | Some k ->
                match str "tag" k with
                | None -> Error "addKind kind has no 'tag'"
                | Some t ->
                    readFields k
                    |> Result.map (fun fs ->
                        AddKind
                            { Tag = t
                              Category = defaultArg (str "category" k) "proposed"
                              Fields = fs })
        | Some "addUnionCase" ->
            match str "union" v, field "case" v with
            | Some u, Some c ->
                match str "tag" c with
                | None -> Error "addUnionCase case has no 'tag'"
                | Some t -> readFields c |> Result.map (fun fs -> AddUnionCase(u, { Tag = t; Fields = fs }))
            | _ -> Error "addUnionCase needs 'union' and 'case'"
        | Some "addEnumCase" ->
            match str "enum" v, str "wire" v with
            | Some e, Some w -> Ok(AddEnumCase(e, w, str "host" v))
            | _ -> Error "addEnumCase needs 'enum' and 'wire'"
        | Some "addField" ->
            match field "owner" v, field "field" v with
            | Some o, Some f ->
                readOwner o
                |> Result.bind (fun owner -> readField f |> Result.map (fun fld -> AddField(owner, fld)))
            | _ -> Error "addField needs 'owner' and 'field'"
        | Some other -> Error(sprintf "unknown delta op '%s'" other)
        | None -> Error "delta entry has no 'op'"

    let private readEvidence (v: JVal) : Result<ProposalEvidence, string> =
        match str "signal" v with
        | None -> Error "evidence entry has no 'signal'"
        | Some s ->
            Ok
                { Signal = s
                  RunId = defaultArg (str "runId" v) ""
                  PromptDigest = defaultArg (str "promptDigest" v) ""
                  Count = defaultArg (intOf "count" v) 0
                  Detail = defaultArg (str "detail" v) "" }

    let private readAlternative (v: JVal) : Result<ProposalAlternative, string> =
        match str "disposition" v with
        | None -> Error "alternative has no 'disposition'"
        | Some d ->
            Ok
                { Disposition = d
                  Verdict = defaultArg (str "verdict" v) ""
                  Argument = defaultArg (str "argument" v) "" }

    let private readFixture (v: JVal) : Result<ProposalFixture, string> =
        match str "name" v, field "wire" v with
        | Some n, Some w -> Ok { Name = n; Wire = Canon.render w }
        | None, _ -> Error "candidate fixture has no 'name'"
        | _, None -> Error "candidate fixture has no 'wire'"

    /// Read a proposal document. Structural failures only — a document that reads
    /// cleanly can still be an inadmissible proposal; that is [[validate]]'s job,
    /// and the two are separate so a defective document does not hide a defective
    /// argument behind a parse error.
    let ofJson (v: JVal) : Result<Proposal, string> =
        match str "id" v with
        | None -> Error "proposal has no 'id'"
        | Some id ->
            arr "delta" v
            |> List.map readDelta
            |> sequence
            |> Result.bind (fun delta ->
                arr "candidateFixtures" v
                |> List.map readFixture
                |> sequence
                |> Result.bind (fun fixtures ->
                    arr "evidence" v
                    |> List.map readEvidence
                    |> sequence
                    |> Result.bind (fun evidence ->
                        arr "alternatives" v
                        |> List.map readAlternative
                        |> sequence
                        |> Result.map (fun alternatives ->
                            { Id = id
                              Cluster = defaultArg (str "cluster" v) ""
                              DraftedBy = defaultArg (str "draftedBy" v) ""
                              DraftedAt = defaultArg (str "draftedAt" v) ""
                              Delta = delta
                              Fixtures = fixtures
                              Evidence = evidence
                              Irreducibility = defaultArg (str "irreducibility" v) ""
                              Alternatives = alternatives
                              NormalisationDistinction = defaultArg (str "normalisationDistinction" v) ""
                              ConfusionPlan = defaultArg (str "confusionPlan" v) "" }))))

    let parse (text: string) : Result<Proposal, string> = Json.parse text |> Result.bind ofJson

    // -- validation ----------------------------------------------------------

    /// Every way this proposal is inadmissible AS A DOCUMENT, named. Empty means
    /// the argument is complete, never that it is right.
    ///
    /// The checks are deliberately about PRESENCE and CHECKABILITY, not merit:
    /// a machine can tell that a citation names no run, and cannot tell whether
    /// the run it names says what the drafter claims. Conflating the two would
    /// put a machine's name on a judgement it did not make, which is the one
    /// thing this pipeline must never do.
    let validate (p: Proposal) : string list =
        [ if System.String.IsNullOrWhiteSpace p.Id then
              yield "id is empty"

          if List.isEmpty p.Delta then
              yield "delta is empty — a proposal that changes nothing cannot be spiked"

          if List.isEmpty p.Fixtures then
              yield "candidateFixtures is empty — nothing states what the change makes expressible"

          if List.isEmpty p.Evidence then
              yield
                  "evidence is empty — a proposal with no cited demand is not admissible under any \
               demand-gated law"

          for e in p.Evidence do
              if System.String.IsNullOrWhiteSpace e.RunId then
                  yield sprintf "evidence '%s' cites no runId — absence of a reference is not a reference" e.Signal

              if System.String.IsNullOrWhiteSpace e.PromptDigest then
                  yield
                      sprintf
                          "evidence '%s' cites no promptDigest — the sighting cannot be re-read against the prompt \
                           that produced it"
                          e.Signal

              if e.Count <= 0 then
                  yield sprintf "evidence '%s' cites a count of %d" e.Signal e.Count

          if System.String.IsNullOrWhiteSpace p.Irreducibility then
              yield "irreducibility is empty"

          let priced =
              p.Alternatives
              |> List.map (fun a -> a.Disposition.ToLowerInvariant())
              |> Set.ofList

          for required in requiredAlternatives do
              if not (priced.Contains required) then
                  yield sprintf "no alternative disposition priced as '%s' — the cheap axes are mandatory" required

          for a in p.Alternatives do
              if System.String.IsNullOrWhiteSpace a.Argument then
                  yield sprintf "alternative '%s' carries no argument" a.Disposition

          if
              priced.Contains "normalisation"
              && System.String.IsNullOrWhiteSpace p.NormalisationDistinction
          then
              yield
                  "a normalisation alternative is priced but normalisationDistinction is empty — re-admitting a retired \
               spelling and admitting a new one are different acts and must be told apart explicitly"

          if System.String.IsNullOrWhiteSpace p.ConfusionPlan then
              yield "confusionPlan is empty — every vocabulary change owes a pre/post confusion delta" ]

    // -- applying ------------------------------------------------------------

    let private replaceUnion (name: string) (f: IdlUnion -> IdlUnion) (idl: Idl) =
        { idl with
            Unions = idl.Unions |> List.map (fun u -> if u.Name = name then f u else u) }

    /// Apply the delta to a vocabulary, returning a NEW value.
    ///
    /// Refuses a collision rather than overwriting: a proposal whose "new" kind
    /// tag already exists is not additive, and the interesting fact about it is
    /// exactly that — silently replacing the existing declaration would produce a
    /// spike report about a vocabulary nobody proposed.
    let applyDelta (idl: Idl) (delta: ProposalDelta list) : Result<Idl, string> =
        let step (acc: Result<Idl, string>) (op: ProposalDelta) =
            acc
            |> Result.bind (fun idl ->
                match op with
                | AddKind k ->
                    if idl.Kinds |> List.exists (fun x -> x.Tag = k.Tag) then
                        Error(sprintf "kind '%s' already exists — the delta is not additive" k.Tag)
                    else
                        Ok { idl with Kinds = idl.Kinds @ [ k ] }

                | AddUnionCase(uname, c) ->
                    match idl.Unions |> List.tryFind (fun u -> u.Name = uname) with
                    | None -> Error(sprintf "union '%s' does not exist" uname)
                    | Some u when u.Cases |> List.exists (fun x -> x.Tag = c.Tag) ->
                        Error(sprintf "union '%s' already has case '%s'" uname c.Tag)
                    | Some _ -> Ok(idl |> replaceUnion uname (fun u -> { u with Cases = u.Cases @ [ c ] }))

                | AddEnumCase(ename, wire, host) ->
                    match idl.Enums |> List.tryFind (fun e -> e.Name = ename) with
                    | None -> Error(sprintf "enum '%s' does not exist" ename)
                    | Some e when e.WireCases |> List.contains wire ->
                        Error(sprintf "enum '%s' already accepts '%s'" ename wire)
                    | Some e ->
                        // An enum either maps host names to wire strings for EVERY
                        // case or for none (`Wires = []` is the identity mapping).
                        // Adding a mapped case to an unmapped enum therefore has to
                        // materialise the identity for the existing cases first, or
                        // the two lists stop being positionally parallel — which is
                        // silent corruption rather than an error.
                        let hostName = defaultArg host wire

                        let updated =
                            if List.isEmpty e.Wires && hostName = wire then
                                { e with Cases = e.Cases @ [ wire ] }
                            else
                                let existingWires = if List.isEmpty e.Wires then e.Cases else e.Wires

                                { e with
                                    Cases = e.Cases @ [ hostName ]
                                    Wires = existingWires @ [ wire ] }

                        Ok
                            { idl with
                                Enums = idl.Enums |> List.map (fun x -> if x.Name = ename then updated else x) }

                | AddField(owner, f) ->
                    let clash (fs: IdlField list) =
                        fs |> List.exists (fun x -> x.Name = f.Name)

                    match owner with
                    | OwnerEnvelope ->
                        if clash idl.NodeFields then
                            Error(sprintf "the node envelope already carries '%s'" f.Name)
                        else
                            Ok
                                { idl with
                                    NodeFields = idl.NodeFields @ [ f ] }
                    | OwnerKind t ->
                        match idl.Kinds |> List.tryFind (fun k -> k.Tag = t) with
                        | None -> Error(sprintf "kind '%s' does not exist" t)
                        | Some k when clash k.Fields -> Error(sprintf "kind '%s' already carries '%s'" t f.Name)
                        | Some _ ->
                            Ok
                                { idl with
                                    Kinds =
                                        idl.Kinds
                                        |> List.map (fun k ->
                                            if k.Tag = t then
                                                { k with Fields = k.Fields @ [ f ] }
                                            else
                                                k) }
                    | OwnerOp t ->
                        match idl.Ops |> List.tryFind (fun k -> k.Tag = t) with
                        | None -> Error(sprintf "op '%s' does not exist" t)
                        | Some k when clash k.Fields -> Error(sprintf "op '%s' already carries '%s'" t f.Name)
                        | Some _ ->
                            Ok
                                { idl with
                                    Ops =
                                        idl.Ops
                                        |> List.map (fun k ->
                                            if k.Tag = t then
                                                { k with Fields = k.Fields @ [ f ] }
                                            else
                                                k) }
                    | OwnerRecord n ->
                        match idl.Records |> List.tryFind (fun r -> r.Name = n) with
                        | None -> Error(sprintf "record '%s' does not exist" n)
                        | Some r when clash r.Fields -> Error(sprintf "record '%s' already carries '%s'" n f.Name)
                        | Some _ ->
                            Ok
                                { idl with
                                    Records =
                                        idl.Records
                                        |> List.map (fun r ->
                                            if r.Name = n then
                                                { r with Fields = r.Fields @ [ f ] }
                                            else
                                                r) }
                    | OwnerUnionCase(u, c) ->
                        match idl.Unions |> List.tryFind (fun x -> x.Name = u) with
                        | None -> Error(sprintf "union '%s' does not exist" u)
                        | Some union ->
                            match union.Cases |> List.tryFind (fun x -> x.Tag = c) with
                            | None -> Error(sprintf "union '%s' has no case '%s'" u c)
                            | Some case when clash case.Fields ->
                                Error(sprintf "case '%s.%s' already carries '%s'" u c f.Name)
                            | Some _ ->
                                Ok(
                                    idl
                                    |> replaceUnion u (fun x ->
                                        { x with
                                            Cases =
                                                x.Cases
                                                |> List.map (fun k ->
                                                    if k.Tag = c then
                                                        { k with Fields = k.Fields @ [ f ] }
                                                    else
                                                        k) })
                                ))

        List.fold step (Ok idl) delta

    /// The kind tags a delta touches — the sampler's cross-section for the spike's
    /// generative leg. A delta that only widens a record or an enum touches no
    /// kind directly, so the tags of every kind that transitively references the
    /// changed declaration would be the ideal answer; the sampler is cheap enough
    /// that the honest approximation is to sweep everything in that case rather
    /// than to compute a reachability set and be quietly wrong about it.
    let touchedKinds (idl: Idl) (delta: ProposalDelta list) : string list =
        let direct =
            delta
            |> List.choose (function
                | AddKind k -> Some k.Tag
                | AddField(OwnerKind t, _) -> Some t
                | _ -> None)
            |> List.distinct

        let indirect =
            delta
            |> List.exists (function
                | AddKind _
                | AddField(OwnerKind _, _) -> false
                | _ -> true)

        if indirect then
            idl.Kinds |> List.map (fun k -> k.Tag)
        else
            direct
