namespace Fuaran.Core

// ============================================================================
//  The null-tolerant read conformance family (Phase 102).
//
//  `Json.parseTolerantOfNull` erases an object-member `null` to member absence so a
//  Fuaran.Core consumer can read a foreign document that spells absent members with the
//  JSON `null` token. This family is what makes that a CONTRACT rather than an
//  implementation detail: the vectors pin the erasure equivalence, the positions where
//  no absence exists, the untouched strict path, and the round-trip discipline that keeps
//  the tolerance a read normalisation rather than a new emission.
//
//  It lives beside the op-algebra law kit for the same reason that kit exists — the
//  methodology is the reusable asset. Any host claiming the tolerant read (in any
//  language) satisfies these vectors or does not have it. FSharp.Core only, Fable-clean.
// ============================================================================

/// The read-side null-tolerance conformance vectors + their runner.
module WireNullTolerance =

    /// What a vector claims, stated against **both** read policies at once — the point being
    /// that a tolerance is only meaningful as a difference, so every claim pins what the strict
    /// path does as well as what the tolerant path does.
    type Claim =
        /// The tolerant read of the vector's JSON is structurally equal to the **strict** read of
        /// `nullFree` (its `null`-free spelling); the strict read of the vector's own JSON is a
        /// `NullNotRepresentable` rejection; and `Json.render` of the tolerant read is exactly
        /// `nullFree`, which re-parses under the strict policy. That last leg is the discipline:
        /// erasure normalises INTO canonical wire, it never mints a new emission.
        | ErasesTo of nullFree: string
        /// A `null` at a position with no absence to erase it to. Both policies reject it as
        /// `NullNotRepresentable`, at the same position — and the tolerant path's message must
        /// DIFFER from the strict one, so a consumer cannot mistake "there is nothing here to
        /// erase to" for the strict policy's blanket refusal. The two have different remedies.
        | NoAbsenceHere
        /// Malformed input rejected under both policies, with the classified kinds named. A
        /// near-miss of the `null` token is not quietly absorbed by the tolerant path.
        | Rejected of strictKind: JsonErrorKind * tolerantKind: JsonErrorKind
        /// A document with no `null` token anywhere: both policies must produce the identical
        /// result. The controls that pin "the tolerant path changed nothing else".
        | UnaffectedByPolicy

    type Vector =
        { Name: string
          Json: string
          Claim: Claim }

    /// A neutral foreign document of the shape that motivates the tolerance: an absent member
    /// spelled `null` at the root, two more nested a level down, alongside empty arrays and
    /// ordinary scalars. Held here so the substrate gate proves the case on its own vectors,
    /// with no external corpus required to be present.
    [<Literal>]
    let foreignDocument =
        """{"formatVersion":1,"issuer":null,"surface":{"enabled":false,"label":null,"limits":null,"offers":{"names":[],"routes":[]},"tags":[],"visibility":"summary"}}"""

    /// The same document as its author would have written it without the token.
    [<Literal>]
    let foreignDocumentNullFree =
        """{"formatVersion":1,"surface":{"enabled":false,"offers":{"names":[],"routes":[]},"tags":[],"visibility":"summary"}}"""

    let vectors: Vector list =
        [ { Name = "sole member erases to the empty object"
            Json = """{"a":null}"""
            Claim = ErasesTo "{}" }

          { Name = "erased member's siblings are preserved in author order"
            Json = """{"a":1,"b":null,"c":"x"}"""
            Claim = ErasesTo """{"a":1,"c":"x"}""" }

          { Name = "every member null erases"
            Json = """{"a":null,"b":null}"""
            Claim = ErasesTo "{}" }

          { Name = "erasure reaches nested objects"
            Json = """{"o":{"p":null,"q":[1,2]},"r":null}"""
            Claim = ErasesTo """{"o":{"q":[1,2]}}""" }

          { Name = "member position inside an array element is still member position"
            Json = """{"a":[{"b":null,"c":true}]}"""
            Claim = ErasesTo """{"a":[{"c":true}]}""" }

          { Name = "whitespace around the erased member is absorbed"
            Json = """{ "a" :  null , "b" : 2 }"""
            Claim = ErasesTo """{"b":2}""" }

          { Name = "foreign document of the motivating shape"
            Json = foreignDocument
            Claim = ErasesTo foreignDocumentNullFree }

          { Name = "a bare null has no absence to erase to"
            Json = "null"
            Claim = NoAbsenceHere }

          { Name = "an array element null has no absence to erase to"
            Json = "[1,null,3]"
            Claim = NoAbsenceHere }

          { Name = "an array element null under a member has no absence to erase to"
            Json = """{"a":[null]}"""
            Claim = NoAbsenceHere }

          { Name = "a truncated near-miss of the token is not absorbed"
            Json = """{"a":nul}"""
            Claim = Rejected(NullNotRepresentable, NullNotRepresentable) }

          { Name = "a trailing-garbage near-miss of the token is not absorbed"
            Json = """{"a":nullish}"""
            Claim = Rejected(NullNotRepresentable, ExpectedToken) }

          { Name = "control: a null-free object is read identically under both policies"
            Json = """{"a":1,"b":[true,"s",2.5],"c":{"d":"e"}}"""
            Claim = UnaffectedByPolicy }

          { Name = "control: the null-free spelling of the foreign document"
            Json = foreignDocumentNullFree
            Claim = UnaffectedByPolicy }

          { Name = "control: a malformed document fails identically under both policies"
            Json = """{"a":}"""
            Claim = UnaffectedByPolicy } ]

    let private pass (name: string) : Corpus.Outcome =
        { Name = name
          Passed = true
          Detail = "ok" }

    let private fail (name: string) (detail: string) : Corpus.Outcome =
        { Name = name
          Passed = false
          Detail = detail }

    let private checkErasesTo (v: Vector) (nullFree: string) : Corpus.Outcome =
        match Json.parseDetailedTolerantOfNull v.Json, Json.parseDetailed nullFree with
        | Error e, _ -> fail v.Name ("tolerant read rejected the vector: " + e.Message)
        | _, Error e -> fail v.Name ("the null-free spelling is not valid JSON: " + e.Message)
        | Ok tolerant, Ok strictOfNullFree ->
            if tolerant <> strictOfNullFree then
                fail v.Name "erasure did not reproduce the null-free spelling's parse"
            else
                // The strict path is untouched: the vector's own JSON is still refused by name.
                match Json.parseDetailed v.Json with
                | Ok _ -> fail v.Name "the strict policy accepted a document carrying null"
                | Error e when e.Kind <> NullNotRepresentable ->
                    fail v.Name ("the strict policy rejected for the wrong reason: " + e.Message)
                | Error _ ->
                    // Round-trip discipline: the tolerant read renders as the canonical
                    // null-free form, and that form parses under the STRICT policy — so nothing
                    // downstream can tell a tolerantly-read document from a null-free one.
                    let rendered = Json.render tolerant

                    if rendered <> nullFree then
                        fail v.Name ("tolerant read rendered " + rendered + ", expected " + nullFree)
                    else
                        match Json.parse rendered with
                        | Ok _ -> pass v.Name
                        | Error m -> fail v.Name ("the rendered form is not strictly readable: " + m)

    let private checkNoAbsenceHere (v: Vector) : Corpus.Outcome =
        match Json.parseDetailed v.Json, Json.parseDetailedTolerantOfNull v.Json with
        | Ok _, _ -> fail v.Name "the strict policy accepted a bare or array-element null"
        | _, Ok _ -> fail v.Name "the tolerant policy erased a null at a position with no absence"
        | Error s, Error t ->
            if s.Kind <> NullNotRepresentable || t.Kind <> NullNotRepresentable then
                fail v.Name "both policies must reject this position as NullNotRepresentable"
            elif s.Position <> t.Position then
                fail v.Name "the two policies rejected at different positions"
            elif s.Message = t.Message then
                fail v.Name "the tolerant rejection must not read as the strict policy's refusal"
            else
                pass v.Name

    let private checkRejected (v: Vector) (strictKind: JsonErrorKind) (tolerantKind: JsonErrorKind) : Corpus.Outcome =
        match Json.parseDetailed v.Json, Json.parseDetailedTolerantOfNull v.Json with
        | Ok _, _ -> fail v.Name "the strict policy accepted malformed input"
        | _, Ok _ -> fail v.Name "the tolerant policy accepted malformed input"
        | Error s, Error t ->
            if s.Kind <> strictKind then
                fail v.Name ("the strict policy classified it as " + string s.Kind)
            elif t.Kind <> tolerantKind then
                fail v.Name ("the tolerant policy classified it as " + string t.Kind)
            else
                pass v.Name

    let private checkUnaffected (v: Vector) : Corpus.Outcome =
        match Json.parseDetailed v.Json, Json.parseDetailedTolerantOfNull v.Json with
        | Ok a, Ok b when a = b -> pass v.Name
        | Error a, Error b when a = b -> pass v.Name
        | _ -> fail v.Name "the two policies disagreed on a document carrying no null"

    /// Run one vector.
    let runVector (v: Vector) : Corpus.Outcome =
        match v.Claim with
        | ErasesTo nullFree -> checkErasesTo v nullFree
        | NoAbsenceHere -> checkNoAbsenceHere v
        | Rejected(s, t) -> checkRejected v s t
        | UnaffectedByPolicy -> checkUnaffected v

    /// Run a vector set.
    let run (vs: Vector list) : Corpus.Outcome list = vs |> List.map runVector

    /// The whole family as a single verdict — `Ok` or the first failing vector, named.
    let check () : Result<unit, string> =
        match run vectors |> List.filter (fun o -> not o.Passed) with
        | [] -> Ok()
        | first :: _ -> Error(first.Name + ": " + first.Detail)
