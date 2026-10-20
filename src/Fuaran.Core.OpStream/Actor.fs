namespace Fuaran.Core

/// The typed, hashed actor that produced an op (Phase 320). The `Human` / `Agent` distinction is
/// the load-bearing accountability fact: a `Human` is a person / account id; an `Agent`
/// additionally carries the `model` + `version` that emitted the op (a neutral attribution axis a
/// consumer may use for its own analytics or provenance reporting). The actor is folded into the chain
/// hash (`StreamConfig.Payload`), so altering it breaks the integrity chain — attribution is now
/// tamper-evident, not merely recorded. FSharp.Core-only + Fable-clean (the encoder is hand-rolled
/// canonical JSON, no `System.Text.Json`), so it hashes byte-identically on every host.
type Actor =
    /// A person or account, by its host-assigned `id`.
    | Human of id: string
    /// An automated emitter: the `model` and `version` that produced the op, and the `id` it acts
    /// under. All three are hashed, so a re-labelled model breaks the chain like any other edit.
    | Agent of model: string * version: string * id: string

/// Why an actor was refused (Phase 315) — an actor that names nobody. `Actor.validate` /
/// `Actor.human` / `Actor.agent` return it, and the JSONL decoder carries it in
/// `JsonlFaultReason.ActorInvalid`. A refusal-class envelope: a case may be added.
[<RequireQualifiedAccess>]
type ActorInvalid =
    /// The actor's `id` is empty — it attributes the op to nobody.
    | EmptyId

/// Companion helpers for `Actor` — the canonical hash pre-image (`encode`), the stable id
/// projection, and the pre-Phase-320 migration lift.
[<RequireQualifiedAccess>]
module Actor =

    /// `encode` under an explicit string quoter — the one shape both the canonical pre-image and
    /// the pre-287 legacy payload are built from, so the two can differ ONLY in how a string is
    /// spelled. Internal: the quoter is not a choice a consumer makes.
    let internal encodeWith (quote: string -> string) (a: Actor) : string =
        match a with
        | Human id -> "{\"kind\":\"human\",\"id\":" + quote id + "}"
        | Agent(model, version, id) ->
            "{\"kind\":\"agent\",\"model\":"
            + quote model
            + ",\"version\":"
            + quote version
            + ",\"id\":"
            + quote id
            + "}"

    /// The canonical JSON object the chain hash folds over. Field order is fixed (`kind` first,
    /// then the case fields in declaration order) so the pre-image is stable across hosts:
    ///   `Human`  → `{"kind":"human","id":<id>}`
    ///   `Agent`  → `{"kind":"agent","model":<model>,"version":<version>,"id":<id>}`
    /// Strings are spelled by `JsonString.quote` — every control character as `\u00xx` (Phase 287),
    /// so `Agent("m\n", "1", "id")` encodes to `{"kind":"agent","model":"m\u000a",…}` on every host.
    let encode (a: Actor) : string = encodeWith JsonString.quote a

    /// The stable attribution id of either case.
    let id (a: Actor) : string =
        match a with
        | Human id -> id
        | Agent(_, _, id) -> id

    /// Lift a pre-Phase-320 bare actor *string* (the old op-stream format recorded the actor as an
    /// unstructured string outside any Human/Agent distinction) to the typed `Human` case — the
    /// migration default. See `StreamConfig.legacyActor` / `Jsonl.readLegacyActor`.
    let ofLegacyString (s: string) : Actor = Human s

    // ---- Phase 315: an actor names somebody ----
    // An empty id attributes an op to nobody, and every reader that groups by `Actor.id` folds all
    // such ops into one anonymous author. The two cases cannot refuse it themselves — a union case
    // is a constructor, and closing them would break every construction site — so the refusal is
    // stated here, once, and applied at the two boundaries that admit an actor: these constructors,
    // and the JSONL decoder (`Jsonl.actorField`, which answers `JsonlFaultReason.ActorInvalid`).

    /// The actor, or the first reason it names nobody: an empty `id` (`ActorInvalid.EmptyId`).
    /// `model` and `version` are attribution detail and may be empty; the id is the identity.
    let validate (a: Actor) : Result<Actor, ActorInvalid> =
        if System.String.IsNullOrEmpty(id a) then
            Error ActorInvalid.EmptyId
        else
            Ok a

    /// `Human id`, refusing an empty id.
    let human (id: string) : Result<Actor, ActorInvalid> = validate (Human id)

    /// `Agent(model, version, id)`, refusing an empty id.
    let agent (model: string) (version: string) (id: string) : Result<Actor, ActorInvalid> =
        validate (Agent(model, version, id))
