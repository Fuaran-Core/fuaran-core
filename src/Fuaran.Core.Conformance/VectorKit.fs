namespace Fuaran.Core

/// The vector families' shared verdicts and spellings (Phase 388) — `WireNullTolerance`,
/// `StringEscapeVectors` and `EncodingProfileVectors` each wrote these out, body for body. A vector
/// family is a runner over an enumerated table rather than a drawn `LawResult` family, and these are
/// the pieces every such runner builds its `Corpus.Outcome`s from. Internal: the outcomes are the
/// surface, and their bytes are what the families' tests pin.
module internal VectorKit =

    /// A passing outcome.
    let pass (name: string) : Corpus.Outcome =
        { Name = name
          Passed = true
          Detail = "ok" }

    /// A failing outcome, carrying what was wrong.
    let fail (name: string) (detail: string) : Corpus.Outcome =
        { Name = name
          Passed = false
          Detail = detail }

    /// `got` held to `expected`; a miss names what emitted it (`what`) and both spellings.
    let expect (name: string) (what: string) (expected: string) (got: string) : Corpus.Outcome =
        if got = expected then
            pass name
        else
            fail name (what + " emitted " + got + ", expected " + expected)

    /// An already-escaped body inside the quotes of a JSON string literal.
    let quoted (body: string) : string = "\"" + body + "\""

    /// `{"seq":0,"actor":<actor>,"op":{}}` — the linear chain pre-image for one actor.
    let payload (actor: string) : string =
        "{\"seq\":0,\"actor\":" + actor + ",\"op\":{}}"
