namespace Fuaran.Core

/// Wire versioning + the forward/backward-compatibility contract (Phase 319). A versioned
/// wire format lets an *older* consumer meet a *newer* artifact and **detect → preserve →
/// degrade** instead of crashing — while the authoring/generation surface stays closed and
/// exhaustive (no host can *emit* an unknown kind). Everything here is FSharp.Core-only and
/// Fable-clean: it composes the `Json` / `Canon` / `Decode` primitives, never re-implementing
/// the canonical byte rules. The split is load-bearing:
///   • the producer authors against a closed surface and stamps the artifact with its profile;
///   • tolerance lives **only** on the decode boundary of a consumer that is `Behind` — the
///     transport-only `Unknown` is reachable on decode, un-constructible on encode.
module Versioning =

    /// A wire profile id — `<name>@<major>.<minor>` (e.g. `core@1.0`). `Name` is the capability
    /// namespace; `Major` is the `/vN/` incompatibility boundary (a removal/rename mints a new
    /// major — old consumers cannot interpret it, see `negotiate`); `Minor` is the additive
    /// capability counter (a new kind/case/field bumps the minor — an older consumer tolerates
    /// it via the must-ignore-but-preserve rule).
    type Profile =
        {
            /// The capability namespace — a name of the grammar (`Profile.isValidName`) to cross the
            /// wire. Two profiles with different names are `Foreign` to each other.
            Name: string
            /// The incompatibility boundary: any difference makes `negotiate` answer `Foreign`.
            /// Non-negative on the wire.
            Major: int
            /// The additive counter: an artifact authored at a higher minor is `Behind`, and tolerated.
            /// Non-negative on the wire.
            Minor: int
        }

    /// The profile grammar: validation, the canonical rendering, and the parser that reads exactly
    /// the strings `render` writes.
    module Profile =

        // THE PROFILE GRAMMAR (Phase 306) — a BIJECTION between the valid profiles and their
        // canonical strings:
        //
        //     profile = name "@" number "." number
        //     name    = letter (letter | digit | "." | "_" | "-")*      ; ASCII only
        //     number  = "0" | nonzero digit*                            ; at most Int32.MaxValue
        //
        // `render` writes exactly this over a valid profile, and `tryParse` accepts exactly this:
        // `tryParse (render p) = Ok p` for every valid `p`, and `render q = s` whenever
        // `tryParse s = Ok q`. Until this phase the reader took strings the writer never wrote —
        // `core@01.0`, `core@+1.0`, a version followed by NUL characters, and any name at all, the
        // empty-looking and the control-character ones included — so two strings named one
        // profile, and a `requiredProfile` could not be compared as text.

        let private isNameStart (c: char) =
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')

        let private isNameChar (c: char) =
            isNameStart c || (c >= '0' && c <= '9') || c = '.' || c = '_' || c = '-'

        /// True where `name` is a profile name of the grammar: an ASCII letter, then ASCII letters,
        /// digits, `.`, `_` and `-`.
        let isValidName (name: string) : bool =
            not (isNull (box name))
            && name.Length > 0
            && isNameStart name[0]
            && name |> Seq.forall isNameChar

        /// True where `p` is a profile the wire can carry: a name of the grammar and two
        /// non-negative counters. The record is public, so one can be built that is not.
        let isValid (p: Profile) : bool =
            isValidName p.Name && p.Major >= 0 && p.Minor >= 0

        /// The canonical string form: `<name>@<major>.<minor>`. **Assumes a valid profile**
        /// (`isValid`) — over one, the string is the one `tryParse` reads back to it. `tryRender`
        /// is the guarded, total entry point for a profile built by hand.
        let render (p: Profile) : string =
            p.Name + "@" + string p.Major + "." + string p.Minor

        /// `render`, refusing a profile `tryParse` could not read back (Phase 306): a name outside
        /// the grammar or a negative counter. Over a valid profile it is exactly `Ok (render p)`.
        let tryRender (p: Profile) : Result<string, string> =
            if not (isValidName p.Name) then
                Error(
                    "profile name is outside the grammar (an ASCII letter, then letters, digits, '.', '_' or '-'): "
                    + (if isNull (box p.Name) then "<null>" else Json.escape p.Name)
                )
            elif p.Major < 0 || p.Minor < 0 then
                Error("profile version is negative: " + string p.Major + "." + string p.Minor)
            else
                Ok(render p)

        /// The base `core` profile — `core@1.0`.
        let coreV1: Profile = { Name = "core"; Major = 1; Minor = 0 }

        /// Parse `<name>@<major>.<minor>` — the canonical form and nothing else (the grammar
        /// above). Names a typed `Error` on any other shape — the same envelope discipline as the
        /// parser (no exceptions escape).
        let tryParse (s: string) : Result<Profile, string> =
            let at = s.LastIndexOf '@'

            if at <= 0 || at = s.Length - 1 then
                Error("malformed profile (expected '<name>@<major>.<minor>'): " + s)
            else
                let name = s.Substring(0, at)
                let ver = s.Substring(at + 1)
                let parts = ver.Split('.')

                // A canonical number: digits only, no sign, and no leading zero — so `01`, `+1`,
                // `-0` and a digit run followed by anything are refused before the wire's one
                // integer reader sees them; the reader then bounds the value to Int32.
                let parseInt (t: string) =
                    let canonical =
                        t.Length > 0
                        && t |> Seq.forall (fun c -> c >= '0' && c <= '9')
                        && (t.Length = 1 || t[0] <> '0')

                    if canonical then Json.readInt32 t else None

                if not (isValidName name) then
                    Error(
                        "malformed profile name (expected an ASCII letter, then letters, digits, '.', '_' or '-'): "
                        + Json.escape name
                    )
                else
                    match parts with
                    | [| maj; min |] ->
                        match parseInt maj, parseInt min with
                        | Some major, Some minor ->
                            Ok
                                { Name = name
                                  Major = major
                                  Minor = minor }
                        | _ ->
                            Error(
                                "malformed profile version (expected '<major>.<minor>', each a canonical non-negative integer within Int32): "
                                + Json.escape ver
                            )
                    | _ -> Error("malformed profile version (expected '<major>.<minor>'): " + Json.escape ver)

    /// The capability-negotiation outcome of a consumer reading an artifact's authored profile.
    type Compatibility =
        /// Authored at-or-below the consumer's profile (same name + major) — decode fully.
        | Current
        /// Authored *ahead* of the consumer (same name + major, higher minor) — the consumer
        /// may meet kinds it does not understand; it must tolerate (preserve + degrade), not crash.
        | Behind of authored: Profile
        /// A different namespace or a different major — an incompatible `/vN/` boundary the
        /// consumer cannot interpret at all (hard-refuse, never silently mis-decode).
        | Foreign of authored: Profile

    /// Negotiate a consumer's supported `Profile` against an artifact's authored `Profile`.
    /// Minor-ahead is `Behind` (tolerable); a different name or major is `Foreign` (refuse).
    let negotiate (consumer: Profile) (authored: Profile) : Compatibility =
        if authored.Name <> consumer.Name || authored.Major <> consumer.Major then
            Foreign authored
        elif authored.Minor > consumer.Minor then
            Behind authored
        else
            Current

    [<Literal>]
    let internal profileKey = "$profile"

    /// The envelope member that carries the artifact. `$`-prefixed, so under `Canon.render` it sorts
    /// before every lower-case data key, and before `$profile`.
    [<Literal>]
    let payloadKey = "$payload"

    [<Literal>]
    let internal requiredProfileKey = "requiredProfile"

    /// A versioned wire envelope: the producer's authored `Profile` + the artifact `Payload`
    /// (a `Node` / `TreeOp` JVal). `$profile` / `$payload` keys are `$`-prefixed so they sort
    /// before any lower-case data key under `Canon.render`. The envelope is the
    /// capability-negotiation carrier — a consumer reads `$profile`, `negotiate`s, then decodes
    /// `$payload` (tolerantly when `Behind`).
    type Envelope =
        {
            /// The profile the producer authored against — what a consumer `negotiate`s before it
            /// reads the payload.
            Profile: Profile
            /// The artifact, carried verbatim; `decode` does not interpret it.
            Payload: JVal
        }

    /// Build the canonical envelope JVal.
    let encode (env: Envelope) : JVal =
        JObj [ payloadKey, env.Payload; profileKey, JStr(Profile.render env.Profile) ]

    /// Render an envelope to canonical wire bytes. **Assumes a valid profile and a payload
    /// `Canon.tryRender` accepts** — `tryRender` is the guarded entry point.
    let render (env: Envelope) : string = Canon.render (encode env)

    /// `render`, guarded (Phase 306): the profile through `Profile.tryRender`, then the whole
    /// envelope through `Canon.tryRender` — so an envelope this returns `Ok` for is one `parse`
    /// reads back to the same profile. Over a valid profile and an accepted payload it is exactly
    /// `Ok (render env)`.
    let tryRender (env: Envelope) : Result<string, string> =
        Profile.tryRender env.Profile
        |> Result.bind (fun _ -> Canon.tryRender (encode env))

    /// Decode an envelope JVal — reads `$profile` (parsed) + the verbatim `$payload`.
    let decode (el: JVal) : Result<Envelope, string> =
        Decoder.describing (Decoder.field profileKey Decoder.str) el
        |> Result.bind Profile.tryParse
        |> Result.bind (fun p ->
            Decoder.describing (Decoder.field payloadKey Decoder.json) el
            |> Result.map (fun payload -> { Profile = p; Payload = payload }))

    /// Parse + decode an envelope from wire bytes.
    let parse (s: string) : Result<Envelope, string> = Json.parse s |> Result.bind decode

    /// A kind the consumer does not understand, captured on the decode boundary. **Transport-only**:
    /// it is reachable here and nowhere on the authoring/encode path — no host can construct one to
    /// emit. `Payload` is the *verbatim parsed object*, so re-rendering it reproduces the producer's
    /// bytes (must-ignore-but-preserve); `RequiredProfile` is the profile the artifact declared it
    /// needs (when present), so the consumer can name what it is missing in a degraded placeholder.
    type UnknownKind =
        {
            /// The unrecognised discriminator, as the decode's `tagOf` read it.
            Kind: string
            /// The whole artifact object as parsed, discriminator included — what `reencode` hands
            /// back unchanged.
            Payload: JVal
            /// The artifact's `requiredProfile` member when present and well-formed; a malformed one
            /// reads as `None`, not as a refusal.
            RequiredProfile: Profile option
        }

    /// The result of a tolerant decode: a fully-understood `'T`, or a preserved `Unknown`.
    type Decoded<'T> =
        /// A tag this consumer understands, fully decoded.
        | Known of 'T
        /// A tag this consumer does not know, preserved for re-encoding. Only a decode produces one.
        | Unknown of UnknownKind

    /// Read an optional `requiredProfile` declaration off an artifact object (the
    /// "artifact declares the profile it requires" shape). Malformed / absent ⇒ `None`.
    let private readRequiredProfile (el: JVal) : Profile option =
        match Decoder.tryMember requiredProfileKey el with
        | Some(JStr s) ->
            match Profile.tryParse s with
            | Ok p -> Some p
            | Error _ -> None
        | _ -> None

    /// Tolerantly decode one artifact object. `tagOf` reads its discriminator; `isKnown` reports
    /// whether this consumer understands that tag; `decodeKnown` decodes a known one. An
    /// *unrecognised* tag is NOT an error — it becomes a transport-only `Unknown` carrying the
    /// verbatim parsed `Payload` and any declared `requiredProfile`. This is the whole
    /// forward-compatibility seam: an older consumer reading a newer artifact detects the unknown
    /// kind here rather than hard-rejecting (`UNKNOWN_DU_CASE` / `WRONG_NODE_KIND`). A genuinely
    /// malformed object (no discriminator at all) still fails via `tagOf`.
    let decodeTolerant
        (tagOf: JVal -> Result<string, string>)
        (isKnown: string -> bool)
        (decodeKnown: JVal -> Result<'T, string>)
        (el: JVal)
        : Result<Decoded<'T>, string> =
        tagOf el
        |> Result.bind (fun tag ->
            if isKnown tag then
                decodeKnown el |> Result.map Known
            else
                Ok(
                    Unknown
                        { Kind = tag
                          Payload = el
                          RequiredProfile = readRequiredProfile el }
                ))

    /// Re-encode a tolerant decode back to a JVal. The `Unknown` branch returns its preserved
    /// `Payload` **verbatim** — must-ignore-but-preserve, so an old client cannot destroy data a
    /// newer producer authored. Composed with `Canon.render` (deterministic key order) the
    /// unknown artifact round-trips byte-for-byte, which is what makes preservation verifiable on
    /// the op-stream hash chain.
    let reencode (encodeKnown: 'T -> JVal) (d: Decoded<'T>) : JVal =
        match d with
        | Known v -> encodeKnown v
        | Unknown u -> u.Payload

    /// The classification of a capability change between two sets of kind tags (the IDL-diff
    /// shape the generator drives migration from). Additive-only — tags added, none removed or
    /// renamed — is a *minor* bump an older consumer tolerates via must-ignore-but-preserve. Any
    /// removal or rename (a tag present `before` and absent `after`) is *breaking* — a new `/vN/`
    /// major boundary requiring migration shims.
    [<RequireQualifiedAccess>]
    type Evolution =
        /// Tags only added, in ascending order — possibly none, which `bump` treats as no change.
        | Additive of added: string list
        /// At least one tag removed (a rename is a removal plus an addition); both lists in
        /// ascending order. Bumps the major and resets the minor.
        | Breaking of removed: string list * added: string list

    /// Classify the kind-tag delta `before` → `after`. No removals ⇒ `Additive`; any removal ⇒
    /// `Breaking`. (A *rename* surfaces as a removal + an add — correctly `Breaking`.)
    let classify (before: Set<string>) (after: Set<string>) : Evolution =
        let added = Set.difference after before |> Set.toList
        let removed = Set.difference before after |> Set.toList

        if List.isEmpty removed then
            Evolution.Additive added
        else
            Evolution.Breaking(removed, added)

    /// The profile a `baseProfile` bumps to under an `Evolution`: a no-op additive leaves it
    /// untouched; an additive bumps the minor (same major — older consumers stay compatible); a
    /// breaking change bumps the major and resets the minor (a `/vN/` boundary — older consumers
    /// become `Foreign`).
    ///
    /// REFUSES AT THE EDGE OF THE RANGE (Phase 306): a counter is an `int`, and one already at
    /// `Int32.MaxValue` has no successor the wire can carry. `baseProfile.Minor + 1` there wrapped
    /// to a NEGATIVE minor — a profile `render` then wrote as `core@1.-2147483648` and `tryParse`
    /// refused, and one `negotiate` read as older than every consumer. The refusal names the
    /// counter; `bump` is the saturating form for a caller with no error channel.
    let tryBump (baseProfile: Profile) (ev: Evolution) : Result<Profile, string> =
        match ev with
        | Evolution.Additive [] -> Ok baseProfile
        | Evolution.Additive _ ->
            if baseProfile.Minor = System.Int32.MaxValue then
                Error(
                    "the minor of "
                    + Profile.render baseProfile
                    + " is at Int32.MaxValue and cannot be bumped"
                )
            else
                Ok
                    { baseProfile with
                        Minor = baseProfile.Minor + 1 }
        | Evolution.Breaking _ ->
            if baseProfile.Major = System.Int32.MaxValue then
                Error(
                    "the major of "
                    + Profile.render baseProfile
                    + " is at Int32.MaxValue and cannot be bumped"
                )
            else
                Ok
                    { baseProfile with
                        Major = baseProfile.Major + 1
                        Minor = 0 }

    /// `tryBump`, SATURATING where it refuses: a counter at `Int32.MaxValue` stays there rather
    /// than wrapping negative, and the profile comes back unchanged. Everywhere below that edge it
    /// is the bump it always was. A caller that must know the bump did not happen uses `tryBump`.
    let bump (baseProfile: Profile) (ev: Evolution) : Profile =
        match tryBump baseProfile ev with
        | Ok p -> p
        | Error _ -> baseProfile
