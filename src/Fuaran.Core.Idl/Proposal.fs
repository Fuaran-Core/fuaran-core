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
type ProposalFixture =
    {
        /// A label telling this candidate apart from the proposal's others; read verbatim.
        Name: string
        /// The candidate document as canonical JSON text: the reader re-renders the
        /// document's `wire` member through `Canon.render`, so authored key order and
        /// spacing do not survive.
        Wire: string
    }

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
        /// The additive steps, applied in order by [[applyDelta]]; the first that collides
        /// or names a missing declaration refuses the whole delta. Empty is inadmissible.
        Delta: ProposalDelta list
        /// The candidate wire documents the change claims to make expressible (the document's
        /// `candidateFixtures`). Empty is inadmissible.
        Fixtures: ProposalFixture list
        /// The recorded signals the proposal rests on. Empty is inadmissible, and so is an
        /// entry with no `RunId`, no `PromptDigest` or a count below 1 ([[validate]]).
        Evidence: ProposalEvidence list
        /// Why the pattern is claimed not to reduce to an existing composition,
        /// role or variant. Drafted here, judged elsewhere.
        Irreducibility: string
        /// The cheaper dispositions priced instead. Each of [[requiredAlternatives]] must
        /// appear (matched case-insensitively on `Disposition`), and every entry must carry
        /// an argument.
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

/// Reading, validating and applying a [[Proposal]]. Reading checks structure, [[validate]]
/// checks the argument is complete, and [[applyDelta]] yields the post-change vocabulary as a
/// new value — none of them writes anything or reaches a verdict.
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
    //
    // Phase 310 — every member is read through the typed decode layer, so a refusal carries a
    // code and the path to the value at fault ([[ofJsonDetailed]]), and [[ofJson]] answers the
    // sentence this reader always answered. One reading changed: an OPTIONAL member that is
    // present and ill-typed (a `count` that is a string) is refused, where the hand-rolled readers
    // this replaced took it for absent and read the default in its place.

    let private under (step: PathSegment) (r: Result<'T, DecodeError>) : Result<'T, DecodeError> =
        r |> Result.mapError (DecodeError.under step)

    /// A refusal at the value being read, with this reader's own sentence.
    let private refuse (code: DecodeCode) (expected: string) (message: string) : Result<'T, DecodeError> =
        Error(DecodeError.make code expected message)

    /// A scalar read whose refusal carries `message` — the sentence this reader has always given
    /// for a member that is not there OR not of its kind.
    let private scalar (message: string) (d: Decoder<'T>) : Decoder<'T> =
        fun x -> d x |> Result.mapError (DecodeError.reword (fun _ -> message))

    /// The required member `name`, read with `d`; absent is `MissingField` with `message`.
    let private need (name: string) (message: string) (d: Decoder<'T>) (v: JVal) : Result<'T, DecodeError> =
        match v with
        | JObj _ ->
            match Decoder.tryMember name v with
            | None ->
                Error(
                    { Decoder.missing name with
                        Message = message }
                )
            | Some x -> d x |> under (PathSegment.Key name)
        | other ->
            Error(
                { Decoder.wrongKind "object" other with
                    Message = message }
            )

    /// The required string member `name`; absent or not a string, `message`.
    let private needStr (name: string) (message: string) (v: JVal) : Result<string, DecodeError> =
        need name message (scalar message Decoder.str) v

    /// The refusal of the absent required member `name`, with this reader's sentence (or of `v`
    /// not being an object at all).
    let private absent (name: string) (message: string) (v: JVal) : Result<'T, DecodeError> =
        match v with
        | JObj _ ->
            Error(
                { Decoder.missing name with
                    Message = message }
            )
        | other ->
            Error(
                { Decoder.wrongKind "object" other with
                    Message = message }
            )

    /// The `$type` discriminator, if present — a present non-string one is refused.
    let private tag (v: JVal) : Result<string option, DecodeError> = Decoder.optField "$type" Decoder.str v

    /// An absent discriminator, in this reader's sentence for the position.
    let private untagged (message: string) : Result<'T, DecodeError> =
        Error(
            { Decoder.missing "$type" with
                Message = message }
        )

    /// A discriminator naming no case of the position, in this reader's sentence.
    let private unknownTag (known: string list) (message: string) : Result<'T, DecodeError> =
        refuse
            DecodeCode.UnknownTag
            ("one of " + (known |> List.map (fun k -> "'" + k + "'") |> String.concat ", "))
            message
        |> under (PathSegment.Key "$type")

    /// The host-surface type tags a proposal is refused by name: known to the vocabulary, not
    /// admitted in a data-only delta.
    let private hostSurfaceTypes = [ "closure"; "fn"; "opaque"; "hosted"; "var"; "op" ]

    /// Read an IDL type from the artifact's own `type` vocabulary.
    ///
    /// **The wire-expressible subset only.** `closure`, `fn`, `opaque`, `hosted`,
    /// `var` and `op` are refused by name rather than silently mapped: each of
    /// them is a HOST declaration decision (what signature the slot has, which
    /// codec owns its content, what placeholder a decoder puts there) that a
    /// data-only proposal has no business making and that no reviewer could check
    /// from the proposal document. A demand that genuinely needs one of them is a
    /// design conversation, not a delta.
    let rec private readType (v: JVal) : Result<IdlType, DecodeError> =
        match tag v with
        | Error e -> Error e
        | Ok(Some "str") -> Ok TStr
        | Ok(Some "int") -> Ok TInt
        | Ok(Some "bool") -> Ok TBool
        | Ok(Some "float") -> Ok TFloat
        | Ok(Some "json") -> Ok TJson
        | Ok(Some "node") -> Ok TNode
        | Ok(Some "kind") -> Ok TKind
        | Ok(Some "enum") -> needStr "name" "enum type has no 'name'" v |> Result.map TEnum
        | Ok(Some "record") -> needStr "name" "record type has no 'name'" v |> Result.map TRecord
        | Ok(Some "list") -> need "of" "list type has no 'of'" readType v |> Result.map TList
        | Ok(Some "map") -> need "values" "map type has no 'values'" readType v |> Result.map TMap
        | Ok(Some "union") ->
            needStr "name" "union type has no 'name'" v
            |> Result.bind (fun n ->
                Decoder.fieldOr "args" [] (Decoder.list readType) v
                |> Result.map (fun args -> TUnion(n, args)))
        | Ok(Some other) ->
            let message =
                sprintf "type '%s' is a host-surface declaration, not wire data — a proposal cannot mint one" other

            if List.contains other hostSurfaceTypes then
                refuse DecodeCode.NotAdmitted "a wire-data type" message
                |> under (PathSegment.Key "$type")
            else
                unknownTag
                    [ "str"
                      "int"
                      "bool"
                      "float"
                      "json"
                      "node"
                      "kind"
                      "enum"
                      "record"
                      "list"
                      "map"
                      "union" ]
                    message
        | Ok None -> untagged "type has no '$type'"

    /// Read an authored default value. Scalars and enum cases only — the same set
    /// the F# generator can emit a default expression for, so a proposal cannot
    /// declare a default the generated layer would then fail to compile.
    let private readValue (v: JVal) : Result<IdlValue, DecodeError> =
        let value (message: string) (d: Decoder<'T>) =
            need "value" message (scalar message d) v

        match tag v with
        | Error e -> Error e
        | Ok(Some "str") -> value "str default has no string 'value'" Decoder.str |> Result.map VStr
        | Ok(Some "int") -> value "int default has no integer 'value'" Decoder.int |> Result.map VInt
        | Ok(Some "bool") -> value "bool default has no boolean 'value'" Decoder.bool |> Result.map VBool
        | Ok(Some "float") -> value "float default has no numeric 'value'" Decoder.float |> Result.map VFloat
        | Ok(Some "enum") -> needStr "case" "enum default has no 'case'" v |> Result.map VEnum
        | Ok(Some other) ->
            refuse
                DecodeCode.NotAdmitted
                "a scalar or enum default"
                (sprintf "default value of kind '%s' is not proposable" other)
            |> under (PathSegment.Key "$type")
        | Ok None -> untagged "default value has no '$type'"

    let private readOptionality (v: JVal) : Result<Optionality, DecodeError> =
        match tag v with
        | Error e -> Error e
        | Ok(Some "required") -> Ok Required
        | Ok(Some "optional") -> Ok Optional
        | Ok(Some "omitDefault") ->
            need "default" "omitDefault has no 'default'" readValue v
            |> Result.map OmitDefault
        | Ok(Some "hostOnly") ->
            // A host-only slot is wire-invisible by definition, so proposing one
            // proposes nothing a consumer can observe — and it requires a declared
            // host signature this format deliberately cannot carry.
            refuse
                DecodeCode.NotAdmitted
                "a wire-visible optionality"
                "'hostOnly' is not proposable — it declares a host slot with no wire projection"
            |> under (PathSegment.Key "$type")
        | Ok(Some other) ->
            unknownTag [ "required"; "optional"; "omitDefault" ] (sprintf "unknown optionality '%s'" other)
        | Ok None -> untagged "optionality has no '$type'"

    let private readField (v: JVal) : Result<IdlField, DecodeError> =
        // All three members are looked for BEFORE any is read, so a field missing one reports the
        // missing one, as it always has.
        match Decoder.tryMember "name" v, Decoder.tryMember "type" v, Decoder.tryMember "optionality" v with
        | Some _, Some _, Some _ ->
            needStr "name" "field has no 'name'" v
            |> Result.bind (fun name ->
                need "type" "field has no 'type'" readType v
                |> Result.bind (fun ty ->
                    need "optionality" "field has no 'optionality'" readOptionality v
                    |> Result.map (fun opt ->
                        // Phase 113 — a proposal proposes a SHAPE. Annotations are
                        // statements about a member that already exists (retirement,
                        // in-process-only, the version it arrived in), so a delta that
                        // MINTS a member has nothing to say with them.
                        { Name = name
                          Type = ty
                          Opt = opt
                          Annotations = Annotations.Empty })))
        | None, _, _ -> absent "name" "field has no 'name'" v
        | _, None, _ -> absent "type" "field has no 'type'" v
        | _, _, None -> absent "optionality" "field has no 'optionality'" v

    let private readFields (owner: JVal) : Result<IdlField list, DecodeError> =
        Decoder.fieldOr "fields" [] (Decoder.list readField) owner

    let private readOwner (v: JVal) : Result<ProposalOwner, DecodeError> =
        match tag v with
        | Error e -> Error e
        | Ok(Some "kind") -> needStr "name" "kind owner has no 'name'" v |> Result.map OwnerKind
        | Ok(Some "record") -> needStr "name" "record owner has no 'name'" v |> Result.map OwnerRecord
        | Ok(Some "op") -> needStr "name" "op owner has no 'name'" v |> Result.map OwnerOp
        | Ok(Some "unionCase") ->
            let message = "unionCase owner needs 'union' and 'case'"

            needStr "union" message v
            |> Result.bind (fun u -> needStr "case" message v |> Result.map (fun c -> OwnerUnionCase(u, c)))
        | Ok(Some "envelope") -> Ok OwnerEnvelope
        | Ok(Some other) ->
            unknownTag [ "kind"; "record"; "op"; "unionCase"; "envelope" ] (sprintf "unknown owner '%s'" other)
        | Ok None -> untagged "owner has no '$type'"

    let private readDelta (v: JVal) : Result<ProposalDelta, DecodeError> =
        let ops = [ "addKind"; "addUnionCase"; "addEnumCase"; "addField" ]

        match Decoder.optField "op" Decoder.str v with
        | Error e -> Error e
        | Ok(Some "addKind") ->
            need
                "kind"
                "addKind has no 'kind'"
                (fun k ->
                    needStr "tag" "addKind kind has no 'tag'" k
                    |> Result.bind (fun t ->
                        readFields k
                        |> Result.bind (fun fs ->
                            Decoder.fieldOr "category" "proposed" Decoder.str k
                            |> Result.map (fun category ->
                                AddKind
                                    { Tag = t
                                      Category = category
                                      Fields = fs
                                      Annotations = Annotations.Empty }))))
                v
        | Ok(Some "addUnionCase") ->
            let message = "addUnionCase needs 'union' and 'case'"

            needStr "union" message v
            |> Result.bind (fun u ->
                need
                    "case"
                    message
                    (fun c ->
                        needStr "tag" "addUnionCase case has no 'tag'" c
                        |> Result.bind (fun t ->
                            readFields c
                            |> Result.map (fun fs ->
                                AddUnionCase(
                                    u,
                                    { Tag = t
                                      Fields = fs
                                      Annotations = Annotations.Empty }
                                ))))
                    v)
        | Ok(Some "addEnumCase") ->
            let message = "addEnumCase needs 'enum' and 'wire'"

            needStr "enum" message v
            |> Result.bind (fun e ->
                needStr "wire" message v
                |> Result.bind (fun w ->
                    Decoder.optField "host" Decoder.str v
                    |> Result.map (fun h -> AddEnumCase(e, w, h))))
        | Ok(Some "addField") ->
            let message = "addField needs 'owner' and 'field'"

            match Decoder.tryMember "owner" v, Decoder.tryMember "field" v with
            | Some _, Some _ ->
                need "owner" message readOwner v
                |> Result.bind (fun owner ->
                    need "field" message readField v |> Result.map (fun fld -> AddField(owner, fld)))
            | None, _ -> absent "owner" message v
            | _, None -> absent "field" message v
        | Ok(Some other) ->
            refuse
                DecodeCode.UnknownTag
                ("one of " + (ops |> List.map (fun o -> "'" + o + "'") |> String.concat ", "))
                (sprintf "unknown delta op '%s'" other)
            |> under (PathSegment.Key "op")
        | Ok None ->
            Error(
                { Decoder.missing "op" with
                    Message = "delta entry has no 'op'" }
            )

    let private readEvidence (v: JVal) : Result<ProposalEvidence, DecodeError> =
        needStr "signal" "evidence entry has no 'signal'" v
        |> Result.bind (fun s ->
            Decoder.fieldOr "runId" "" Decoder.str v
            |> Result.bind (fun runId ->
                Decoder.fieldOr "promptDigest" "" Decoder.str v
                |> Result.bind (fun digest ->
                    Decoder.fieldOr "count" 0 Decoder.int v
                    |> Result.bind (fun count ->
                        Decoder.fieldOr "detail" "" Decoder.str v
                        |> Result.map (fun detail ->
                            { Signal = s
                              RunId = runId
                              PromptDigest = digest
                              Count = count
                              Detail = detail })))))

    let private readAlternative (v: JVal) : Result<ProposalAlternative, DecodeError> =
        needStr "disposition" "alternative has no 'disposition'" v
        |> Result.bind (fun d ->
            Decoder.fieldOr "verdict" "" Decoder.str v
            |> Result.bind (fun verdict ->
                Decoder.fieldOr "argument" "" Decoder.str v
                |> Result.map (fun argument ->
                    { Disposition = d
                      Verdict = verdict
                      Argument = argument })))

    let private readFixture (v: JVal) : Result<ProposalFixture, DecodeError> =
        needStr "name" "candidate fixture has no 'name'" v
        |> Result.bind (fun n ->
            need "wire" "candidate fixture has no 'wire'" Decoder.json v
            |> Result.map (fun w -> { Name = n; Wire = Canon.render w }))

    /// Read a proposal document, answering a typed refusal (Phase 310): its code, the path to
    /// the value at fault, and [[ofJson]]'s sentence. Structural failures only — a document that
    /// reads cleanly can still be an inadmissible proposal; that is [[validate]]'s job, and the
    /// two are separate so a defective document does not hide a defective argument behind a
    /// parse error.
    let ofJsonDetailed (v: JVal) : Result<Proposal, DecodeError> =
        let text (name: string) = Decoder.fieldOr name "" Decoder.str v

        needStr "id" "proposal has no 'id'" v
        |> Result.bind (fun id ->
            Decoder.fieldOr "delta" [] (Decoder.list readDelta) v
            |> Result.bind (fun delta ->
                Decoder.fieldOr "candidateFixtures" [] (Decoder.list readFixture) v
                |> Result.bind (fun fixtures ->
                    Decoder.fieldOr "evidence" [] (Decoder.list readEvidence) v
                    |> Result.bind (fun evidence ->
                        Decoder.fieldOr "alternatives" [] (Decoder.list readAlternative) v
                        |> Result.bind (fun alternatives ->
                            text "cluster"
                            |> Result.bind (fun cluster ->
                                text "draftedBy"
                                |> Result.bind (fun draftedBy ->
                                    text "draftedAt"
                                    |> Result.bind (fun draftedAt ->
                                        text "irreducibility"
                                        |> Result.bind (fun irreducibility ->
                                            text "normalisationDistinction"
                                            |> Result.bind (fun distinction ->
                                                text "confusionPlan"
                                                |> Result.map (fun confusionPlan ->
                                                    { Id = id
                                                      Cluster = cluster
                                                      DraftedBy = draftedBy
                                                      DraftedAt = draftedAt
                                                      Delta = delta
                                                      Fixtures = fixtures
                                                      Evidence = evidence
                                                      Irreducibility = irreducibility
                                                      Alternatives = alternatives
                                                      NormalisationDistinction = distinction
                                                      ConfusionPlan = confusionPlan })))))))))))

    /// Read a proposal document — the sentence of [[ofJsonDetailed]]'s refusal.
    let ofJson (v: JVal) : Result<Proposal, string> =
        ofJsonDetailed v |> Result.mapError DecodeError.describe

    /// [[ofJsonDetailed]] over text; a parse failure is refused at the root.
    let parseDetailed (text: string) : Result<Proposal, DecodeError> =
        Decoder.parse text |> Result.bind ofJsonDetailed

    /// Read a proposal document from JSON text — the sentence of [[parseDetailed]]'s refusal.
    /// Structural only: a document that parses can still fail [[validate]].
    let parse (text: string) : Result<Proposal, string> =
        parseDetailed text |> Result.mapError DecodeError.describe

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
    /// Refuses a vocabulary [[Declare.errors]] reports (Phase 292) — the result, so a base
    /// that is itself ill-formed is refused too.
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

        // Phase 292 — the vocabulary a delta produces is held to the same well-formedness
        // rules a loaded one is (DECISIONS D95): a proposal is authored data, and a delta
        // adding a kind with a dangling type name or a wrong-typed default must be refused
        // here, naming every error, rather than priced as though it were a vocabulary.
        List.fold step (Ok idl) delta
        |> Result.bind (fun proposed ->
            match Declare.errors proposed with
            | [] -> Ok proposed
            | errs ->
                Error(
                    sprintf
                        "the proposed vocabulary is not well-formed (%d error(s)): %s"
                        (List.length errs)
                        (String.concat "; " errs)
                ))

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
