namespace Fuaran.Core

/// Why a guarded digest refused a string (Phase 306): the UTF-16 unit at `Index` is a surrogate
/// with no partner — a high one (`D800`–`DBFF`) not followed at once by a low one, or a low one
/// (`DC00`–`DFFF`) with no high one before it — so the string has no code point there, UTF-8 has no
/// encoding for it, and a digest over a substitute would be some other string's digest.
type IllFormedUtf16 =
    {
        /// The 0-based index of the unpaired surrogate, in UTF-16 units.
        Index: int
        /// The unit itself (`0xD800`–`0xDFFF`).
        Unit: int
    }

/// Deterministic content hashing. Two regimes, separately named so a call site says which one it is
/// in: `fnv1a` — 32-bit, fast, NOT cryptographic — for staleness fingerprints and the
/// content-hashed bounded-escape regions; `sha256Hex` / `sha256Bytes` — the pinned pure FIPS 180-4
/// digest — wherever an adversary would gain by forging the value. Both are FSharp.Core-only, both
/// compile under Fable, and both are certified to produce the SAME VALUE on either pipeline — see
/// `mul32` and `.+.`, the two places that certification is actually bought.
module Hash =

    /// 32-bit wrapping multiply that stays exact under Fable's float-backed numerics — the same
    /// problem `.+.` solves for addition, an order of magnitude worse. Fable emits `uint32` `*` as a
    /// plain JS multiply on doubles, and `h * 16777619u` reaches ~3.6e16: past the 2^53
    /// exact-integer ceiling, so precision is lost INSIDE the operation and a trailing
    /// `&&& 0xFFFFFFFFu` cannot recover it — by then the low bits are already gone. Measured before
    /// this existed: `fnv1a "a"` was `e40c292c` on .NET and `e40c2930` under Fable.
    ///
    /// The fix is to never form a product above 2^32. Split both operands into 16-bit halves: the
    /// `aHi*bHi` term is a multiple of 2^32 and vanishes mod 2^32, the cross terms contribute only
    /// their low 16 bits, and every partial product is at most (2^16-1)^2, comfortably exact as a
    /// double. Recombination is `* 65536u` rather than `<<< 16` so no signed-shift semantics enter.
    /// On .NET each step is ordinary `uint32` arithmetic and both masks are no-ops, so .NET values
    /// are UNCHANGED by this — the pinned vectors below hold them to that byte-for-byte.
    ///
    /// **Protected by measurement, not by the compile gate** — the same caveat `.+.` carries, for
    /// the same reason: a compile cannot disagree about a number. Reverting this to a plain `a * b`
    /// leaves the whole .NET suite green — measured — while 120 of a 124-entry corpus diverge. The
    /// .NET half is pinned by the `fnv1a` vectors and the independent 64-bit reference in
    /// `HashTests`; the cross-pipeline half is the parity leg (Phase 118; run by the Fable consumer
    /// since Phase 217 — STABILITY.md "Fable cleanliness"), which runs `ParityVectors` on both
    /// pipelines: the `fnv1a/*` vectors including the non-ASCII and `foldSep` cases, and the
    /// `hashSweep/*` rows — the 124-entry corpus of the retired by-hand probe, one column per
    /// implementation — so a divergence is caught AND its reach is shown in one run. It fails
    /// rather than skips when no JS runtime is present.
    let inline private mul32 (a: uint32) (b: uint32) : uint32 =
        let aLo = a &&& 0xFFFFu
        let aHi = a >>> 16
        let bLo = b &&& 0xFFFFu
        let bHi = b >>> 16
        // Masking the cross terms to 16 bits BEFORE recombining is what keeps the two pipelines
        // agreeing: .NET wraps that sum at 2^32 and JS does not, and the low 16 bits — the only
        // part that survives the shift — are identical either way.
        let cross = ((aLo * bHi) + (aHi * bLo)) &&& 0xFFFFu
        ((aLo * bLo) + (cross * 65536u)) &&& 0xFFFFFFFFu

    /// The raw 32-bit FNV-1a value `fnv1a` renders (Phase 315) — for a caller that buckets, mixes or
    /// compares the number rather than its text, and so used to copy `mul32` to get it. The same
    /// fold, the same unit (the UTF-16 code unit — read `fnv1a`), the same split-half multiply; the
    /// `fnv1a32/*` parity vectors hold it to one value on both pipelines.
    let fnv1a32 (s: string) : uint32 =
        let mutable h = 2166136261u

        for ch in s do
            h <- h ^^^ uint32 ch
            h <- mul32 h 16777619u

        h

    /// A 32-bit non-cryptographic content fingerprint (FNV-1a, lowercase hex). Cheap, and a second
    /// pre-image is seconds of search — so it belongs on a cache key or a rebuild stamp, never under
    /// a signature. Use `sha256Hex` for anything an adversary would gain by forging.
    ///
    /// **Value-identical on .NET and under Fable** — measured, not asserted. The multiply goes
    /// through `mul32` for that reason; read its comment before simplifying the loop.
    ///
    /// **THE UNIT IS THE UTF-16 CODE UNIT, not the byte and not the code point (stated by
    /// Phase 306; it was always so).** Each `char` of the string is folded in whole, as one 16-bit
    /// value: a BMP character is one step, an astral character is TWO (its high surrogate, then its
    /// low one), and nothing is UTF-8-encoded first. That is not the textbook FNV-1a, which folds
    /// bytes, and a host twin that folds UTF-8 bytes or code points agrees with this one on ASCII
    /// and on nothing else — silently, since ASCII is what most fixtures carry. The
    /// `fnv1a/astral-code-units` parity vector pins one astral character, the shortest input on
    /// which the three readings give three values, so a twin learns which it implemented. Every
    /// unit is folded as found, a lone surrogate included: this is a cache fingerprint, not a
    /// digest, and it refuses nothing.
    ///
    /// Defined over `fnv1a32` (Phase 315): the string is that value as eight lower-case hex digits.
    let fnv1a (s: string) : string = (fnv1a32 s).ToString("x8")

    /// The field TERMINATOR of every canonical pre-image on the spine (`canonicalField` below):
    /// the ASCII control byte `U+0001` (SOH). Since Phase 290 no key joins on it bare — every key
    /// in the roster at `canonicalFields` goes through the escaped, terminated field encoding,
    /// where a value that happens to spell the byte cannot run into the next field. Named once
    /// here so the parity-relevant constant is defined in a single place.
    let foldSep = "\u0001"

    /// The escape character `canonicalField` writes before a `foldSep` or a `fieldEsc` a field
    /// carries: `U+0010` (DLE, the data-link escape). Named beside `foldSep` so the two symbols of
    /// the field encoding are defined in one place.
    ///
    /// Both are written as `\u` escapes, never as the raw control byte (`foldSep` was, until
    /// Phase 290): a raw `U+0001` in the source is invisible in a diff and silently dropped or
    /// normalised by some editors, and a raw NUL beside it would make git classify the file as
    /// binary and stop end-of-line normalisation for it. The escape says what the byte is.
    let fieldEsc = "\u0010"

    /// ONE field of an injective canonical pre-image (Phase 225): the field with every `fieldEsc`
    /// and every `foldSep` it carries escaped by a preceding `fieldEsc`, then terminated by
    /// `foldSep`. The first UNESCAPED `foldSep` is therefore always the end of the field, whatever
    /// the field contains — which a bare separator cannot promise, because a string value can
    /// spell one. Escaping `fieldEsc` first is what keeps the escapes the second replacement
    /// inserts from being escaped again.
    let canonicalField (s: string) : string =
        s.Replace(fieldEsc, fieldEsc + fieldEsc).Replace(foldSep, fieldEsc + foldSep)
        + foldSep

    /// The canonical pre-image of a field sequence: each field through `canonicalField`, then
    /// concatenated. INJECTIVE — two field lists with one pre-image are one list — which is
    /// proved of this encoding (`invocation_key_injective` in `proofs/Query.fst` and
    /// `proofs/Capability.fst`).
    ///
    /// **THE KEY ROSTER (Phase 290).** Every key the spine mints that is not a chain hash builds
    /// its pre-image through this function, and the list below IS the roster: the `Hash.Roster`
    /// family (`tests/Fuaran.Core.Tests/HashRosterTests.fs`) reads it from this comment and holds
    /// it to the tree both ways — every call site of `canonicalFields` under `src/` is one of these
    /// definitions, and no definition under `src/` joins fields on a bare `foldSep` any more — so a
    /// new key cannot be minted without joining the list, and a listed key cannot quietly leave
    /// the encoding. Two of Phase 225's three were the first entries; the rest joined in Phase 290
    /// (until then they joined on the bare separator, which a value can spell).
    ///   - `Query.invocationKey`
    ///   - `Query.invocationKeyPage` (Phase 316: `Query.invocationKey`'s fields behind a page triple —
    ///     an empty name, the tag `p` no cell carries, the page token)
    ///   - `Capability.invocationKey`
    ///   - `CapabilityPipeline.nodeInvocationKey`
    ///   - `Function.memoKey`
    ///   - `Projection.digestOf`
    ///   - `Validator.canonicalCodes`
    ///   - `Tree.preimageWith` (the one pre-image `Tree.contentHash`, `Tree.encodePreimage` and
    ///     `Tree.encodeHash` share)
    ///   - `Tree.Index.fingerprintOf`
    ///   - `Tree.Index.fingerprintOfWith` (Phase 305: the same term with the caller's content encoder
    ///     between the kind and the child count — the stamp `buildWith` / `isFreshForWith` read)
    ///   - `Tree.ownDigest` (Phase 314: a node's own content — id key, kind, encoded shell — under
    ///     SHA-256; the `Own` map of `Tree.digests`)
    ///   - `Tree.frameDigest` (Phase 314: the own fields, then the child count and child id keys,
    ///     under SHA-256 — the `Frame` map, and `Projection.snapshotDigestOf` since Phase 314, which
    ///     carried this key from Phase 298 as the changed-since baseline)
    ///   - `Tree.digests` (Phase 314: the Merkle `Subtree` digest — the own digest, then each child's
    ///     subtree digest in order — under SHA-256)
    ///   - `Validator.encodeVerdict` (Phase 314: a gate verdict's cross-host encoding — the policy,
    ///     the block, then each introduced defect's code, location and severity)
    ///   - `ColumnValidator.ruleId` (Phase 298: a stock column rule's id over its parameters)
    ///   - `ColumnValidator.keyText` (Phase 298: one composite key of the `unique` rule)
    let canonicalFields (fields: string list) : string =
        fields |> List.map canonicalField |> String.concat ""

    // ---------------------------------------------------------------------------------------------
    //  SHA-256 (FIPS 180-4) — the spine's ONE cryptographic digest.
    //
    //  TWO HASH REGIMES, NAMED, NEVER MIXED. `fnv1a` above is a 32-bit non-cryptographic CACHE HASH:
    //  a staleness fingerprint over data the same process just produced, where nobody gains anything
    //  by forging it. `sha256Hex` is the CRYPTO DIGEST: anything an adversary would gain by forging —
    //  a content hash that becomes a signed head, a chain a dispute is read from. A second pre-image
    //  under a 32-bit checksum is seconds of search, so the two must never be interchanged; they are
    //  separately named here precisely so a call site says which regime it is in.
    //
    //  WHY A PURE IMPLEMENTATION AND NOT `System.Security.Cryptography`. That namespace does not exist
    //  under Fable, so a browser host needs a pure implementation regardless — and the load-bearing
    //  constraint is that a digest taken by a server must verify in a browser. Using the platform's on
    //  .NET and a pure one under Fable would create two code paths that must agree bit-for-bit on the
    //  one primitive whose divergence silently breaks cross-host verification. One implementation,
    //  pinned to the published FIPS 180-4 known-answer vectors AND proven byte-for-byte equal to the
    //  platform's over a Unicode/emoji/block-boundary corpus, removes that divergence surface.
    //  FSharp.Core-only, so the Fable-compile gate covers it like every other public surface.
    //
    //  WHAT IT IS FOR, AND NOT FOR. For content-addressing, corruption detection, and as the digest
    //  under a signing ceremony a host supplies. NOT for authentication or secrecy on its own: there
    //  is no HMAC, no keyed MAC and no signing here, and an unkeyed digest over data the store itself
    //  holds is recomputable by anyone who can write the store. Detecting an edit by someone with
    //  write access needs a secret the writer does not have — that is the host's attestation seam.
    //
    //  Deliberately `uint32`-only (no `uint64`, no `BigInt`) with a manual nibble table rather than
    //  `ToString("x8")` — the arithmetic subset Fable's numeric emulation carries unambiguously.
    //  Correct for any input under 512 MB.
    // ---------------------------------------------------------------------------------------------

    let private sha256K: uint32[] =
        [| 0x428a2f98u
           0x71374491u
           0xb5c0fbcfu
           0xe9b5dba5u
           0x3956c25bu
           0x59f111f1u
           0x923f82a4u
           0xab1c5ed5u
           0xd807aa98u
           0x12835b01u
           0x243185beu
           0x550c7dc3u
           0x72be5d74u
           0x80deb1feu
           0x9bdc06a7u
           0xc19bf174u
           0xe49b69c1u
           0xefbe4786u
           0x0fc19dc6u
           0x240ca1ccu
           0x2de92c6fu
           0x4a7484aau
           0x5cb0a9dcu
           0x76f988dau
           0x983e5152u
           0xa831c66du
           0xb00327c8u
           0xbf597fc7u
           0xc6e00bf3u
           0xd5a79147u
           0x06ca6351u
           0x14292967u
           0x27b70a85u
           0x2e1b2138u
           0x4d2c6dfcu
           0x53380d13u
           0x650a7354u
           0x766a0abbu
           0x81c2c92eu
           0x92722c85u
           0xa2bfe8a1u
           0xa81a664bu
           0xc24b8b70u
           0xc76c51a3u
           0xd192e819u
           0xd6990624u
           0xf40e3585u
           0x106aa070u
           0x19a4c116u
           0x1e376c08u
           0x2748774cu
           0x34b0bcb5u
           0x391c0cb3u
           0x4ed8aa4au
           0x5b9cca4fu
           0x682e6ff3u
           0x748f82eeu
           0x78a5636fu
           0x84c87814u
           0x8cc70208u
           0x90befffau
           0xa4506cebu
           0xbef9a3f7u
           0xc67178f2u |]

    let private rotr (x: uint32) (n: int) : uint32 = (x >>> n) ||| (x <<< (32 - n))

    /// 32-bit wrapping add that stays exact under Fable's float-backed numerics. Fable emits `uint32`
    /// `+` as a plain JS `+` (no wrap), and SHA-256's working variables roughly double every four
    /// rounds — a single block stays under 2^53, a SECOND block does not, so an unmasked carry loses
    /// precision and multi-block digests diverge in the browser while single-block ones look correct.
    /// On .NET the mask is a no-op (`uint32` addition wraps natively), so digests are identical either
    /// way.
    ///
    /// **Which is exactly why NOTHING IN THE .NET SUITE GUARDS IT.** Measured 2026-08-21 by removing
    /// it: every test in `HashTests` still passes, while under Fable the single-block vectors stay
    /// correct, the two-block vector goes wrong, and the one-million-`a` vector collapses to all
    /// zeros.
    ///
    /// **A GATE GUARDS IT NOW (Phase 118), and the guard is a runtime cross-pipeline comparison
    /// rather than a review.** The parity leg runs the `ParityVectors` table on both
    /// pipelines and byte-compares; its `sha256/two-block` vector is the 56-byte FIPS message,
    /// chosen because it is the shortest input that reaches a SECOND compression block, which is
    /// where the working variables first pass 2^53. Re-measured 2026-09-02 with the mask removed:
    /// `HashTests` stays 12/12 green and the committed .NET vector table stays green — the mask is
    /// a no-op on .NET, so neither can see it — while the parity leg reddens on exactly the
    /// two-block vectors and leaves every single-block one untouched. Do not "simplify" this line;
    /// the compile gate beside it still cannot disagree about a number, and never could.
    let inline private (.+.) (x: uint32) (y: uint32) : uint32 = (x + y) &&& 0xFFFFFFFFu

    /// UTF-8 encode a string to bytes (BMP + surrogate pairs), pure managed. `System.Text.Encoding`
    /// does not exist under Fable, and this is the encoder `sha256Hex` hashes through — exposed
    /// because a caller composing a digest pre-image out of parts needs the same bytes.
    ///
    /// **Byte-for-byte the platform's answer, ill-formed input included (Phase 290).** A high
    /// surrogate is a pair only when the NEXT unit is a low surrogate (`DC00..DFFF`); a lone
    /// surrogate of either half, or a high one followed by anything else, encodes as the
    /// replacement character's three bytes `EF BF BD` — which is what `System.Text.Encoding.UTF8`
    /// emits, and what the parity corpus (`ParityVectors`, the `utf8Bytes/ill-formed-*` rows)
    /// pins on both pipelines. Until Phase 290 the low half was consumed unchecked, so
    /// `"\uD801\uD800"` produced the four bytes of U+10000 and a lone surrogate was written
    /// CESU-style — the platform-parity claim was tested over a well-formed corpus only.
    ///
    /// **What replacement does NOT buy: injectivity.** The platform maps `"\uD800"`, `"\uDFFF"` and
    /// `"\uFFFD"` to ONE byte string, so a digest over ill-formed input has a second pre-image by
    /// construction. This is the UNGUARDED, platform-parity path, and it says so; wherever a
    /// digest must name one string, an ill-formed unit is refused before it reaches here —
    /// `tryUtf8Bytes` / `trySha256Hex` below are that guarded form (Phase 306).
    let utf8Bytes (s: string) : byte[] =
        let out = ResizeArray<byte>()
        let mutable i = 0

        while i < s.Length do
            let c = int s[i]

            let pairs =
                c >= 0xD800
                && c <= 0xDBFF
                && i + 1 < s.Length
                && (let lo = int s[i + 1] in lo >= 0xDC00 && lo <= 0xDFFF)

            if c < 0x80 then
                out.Add(byte c)
            elif c < 0x800 then
                out.Add(byte (0xC0 ||| (c >>> 6)))
                out.Add(byte (0x80 ||| (c &&& 0x3F)))
            elif pairs then
                let lo = int s[i + 1]
                let cp = 0x10000 + ((c - 0xD800) <<< 10) + (lo - 0xDC00)
                out.Add(byte (0xF0 ||| (cp >>> 18)))
                out.Add(byte (0x80 ||| ((cp >>> 12) &&& 0x3F)))
                out.Add(byte (0x80 ||| ((cp >>> 6) &&& 0x3F)))
                out.Add(byte (0x80 ||| (cp &&& 0x3F)))
                i <- i + 1
            elif c >= 0xD800 && c <= 0xDFFF then
                // A lone or ill-ordered surrogate: U+FFFD, as the platform encoder writes it.
                out.Add(byte 0xEF)
                out.Add(byte 0xBF)
                out.Add(byte 0xBD)
            else
                out.Add(byte (0xE0 ||| (c >>> 12)))
                out.Add(byte (0x80 ||| ((c >>> 6) &&& 0x3F)))
                out.Add(byte (0x80 ||| (c &&& 0x3F)))

            i <- i + 1

        out.ToArray()

    /// The first unpaired surrogate of `s`, or `None` where `s` is well-formed UTF-16 (Phase 306).
    /// The same rule the wire parser and the guarded canonical renderer refuse on, kept here as
    /// its own few lines because this package references nothing.
    let firstIllFormedUnit (s: string) : IllFormedUtf16 option =
        let mutable i = 0
        let mutable found = None

        while found.IsNone && i < s.Length do
            let c = int s[i]

            if c >= 0xD800 && c <= 0xDBFF then
                if i + 1 < s.Length && int s[i + 1] >= 0xDC00 && int s[i + 1] <= 0xDFFF then
                    i <- i + 2
                else
                    found <- Some { Index = i; Unit = c }
            elif c >= 0xDC00 && c <= 0xDFFF then
                found <- Some { Index = i; Unit = c }
            else
                i <- i + 1

        found

    /// `utf8Bytes`, GUARDED (Phase 306): the UTF-8 bytes of a well-formed string, or the first
    /// unpaired surrogate as a typed refusal. Over a well-formed string it is exactly
    /// `Ok (utf8Bytes s)`, and there the encoding is INJECTIVE — two well-formed strings with one
    /// byte string are one string (`proofs/Utf8.fst`, `utf8_injective`) — which the unguarded
    /// form cannot be, since replacement maps three strings to `EF BF BD`. This is the form a
    /// digest pre-image goes through wherever the digest must name one string.
    let tryUtf8Bytes (s: string) : Result<byte[], IllFormedUtf16> =
        match firstIllFormedUnit s with
        | Some bad -> Error bad
        | None -> Ok(utf8Bytes s)

    /// The compression function: pad per FIPS 180-4 §5.1.1 and fold, returning the eight state words.
    /// Both public forms below project from this, so the byte form and the hex form cannot drift.
    let private sha256Words (input: byte[]) : uint32[] =
        let data = ResizeArray<byte>()
        data.AddRange input
        let byteLen = input.Length
        // Padding: 0x80, then zeros, then the 64-bit big-endian bit length.
        data.Add 0x80uy

        while data.Count % 64 <> 56 do
            data.Add 0uy

        // The bit length as two `uint32` halves — no `uint64`, which Fable cannot carry exactly. The
        // high half is 0 for any input under 512 MB.
        let lo = uint32 byteLen <<< 3
        let hi = uint32 byteLen >>> 29

        for shift in [ 24; 16; 8; 0 ] do
            data.Add(byte ((hi >>> shift) &&& 0xFFu))

        for shift in [ 24; 16; 8; 0 ] do
            data.Add(byte ((lo >>> shift) &&& 0xFFu))

        let mutable h0 = 0x6a09e667u
        let mutable h1 = 0xbb67ae85u
        let mutable h2 = 0x3c6ef372u
        let mutable h3 = 0xa54ff53au
        let mutable h4 = 0x510e527fu
        let mutable h5 = 0x9b05688cu
        let mutable h6 = 0x1f83d9abu
        let mutable h7 = 0x5be0cd19u

        let w = Array.zeroCreate<uint32> 64
        let blocks = data.Count / 64

        for b in 0 .. blocks - 1 do
            let off = b * 64

            for t in 0..15 do
                w[t] <-
                    (uint32 data[off + t * 4] <<< 24)
                    ||| (uint32 data[off + t * 4 + 1] <<< 16)
                    ||| (uint32 data[off + t * 4 + 2] <<< 8)
                    ||| (uint32 data[off + t * 4 + 3])

            for t in 16..63 do
                let s0 = (rotr w[t - 15] 7) ^^^ (rotr w[t - 15] 18) ^^^ (w[t - 15] >>> 3)
                let s1 = (rotr w[t - 2] 17) ^^^ (rotr w[t - 2] 19) ^^^ (w[t - 2] >>> 10)
                w[t] <- w[t - 16] .+. s0 .+. w[t - 7] .+. s1

            let mutable a = h0
            let mutable bb = h1
            let mutable c = h2
            let mutable d = h3
            let mutable e = h4
            let mutable f = h5
            let mutable g = h6
            let mutable h = h7

            for t in 0..63 do
                let s1 = (rotr e 6) ^^^ (rotr e 11) ^^^ (rotr e 25)
                let ch = (e &&& f) ^^^ ((~~~e) &&& g)
                let temp1 = h .+. s1 .+. ch .+. sha256K[t] .+. w[t]
                let s0 = (rotr a 2) ^^^ (rotr a 13) ^^^ (rotr a 22)
                let maj = (a &&& bb) ^^^ (a &&& c) ^^^ (bb &&& c)
                let temp2 = s0 .+. maj
                h <- g
                g <- f
                f <- e
                e <- d .+. temp1
                d <- c
                c <- bb
                bb <- a
                a <- temp1 .+. temp2

            h0 <- h0 .+. a
            h1 <- h1 .+. bb
            h2 <- h2 .+. c
            h3 <- h3 .+. d
            h4 <- h4 .+. e
            h5 <- h5 .+. f
            h6 <- h6 .+. g
            h7 <- h7 .+. h

        [| h0; h1; h2; h3; h4; h5; h6; h7 |]

    let private hexChars = "0123456789abcdef"

    /// The raw 32-byte SHA-256 digest of arbitrary bytes. The byte-level form: a caller chaining a
    /// digest into another pre-image, or handing it to a host-side signer, wants the bytes rather
    /// than a hex round-trip.
    let sha256Bytes (input: byte[]) : byte[] =
        let words = sha256Words input
        let out = Array.zeroCreate<byte> 32

        for i in 0..7 do
            let v = words[i]
            out[i * 4] <- byte ((v >>> 24) &&& 0xFFu)
            out[i * 4 + 1] <- byte ((v >>> 16) &&& 0xFFu)
            out[i * 4 + 2] <- byte ((v >>> 8) &&& 0xFFu)
            out[i * 4 + 3] <- byte (v &&& 0xFFu)

        out

    /// Lowercase-hex SHA-256 of arbitrary bytes.
    let sha256HexOfBytes (input: byte[]) : string =
        let words = sha256Words input
        let sb = System.Text.StringBuilder()

        for v in words do
            for shift in [ 28; 24; 20; 16; 12; 8; 4; 0 ] do
                sb.Append(hexChars[int ((v >>> shift) &&& 0xFu)]) |> ignore

        sb.ToString()

    /// Lowercase-hex SHA-256 over the UTF-8 bytes of a string — the form nearly every call site
    /// wants. Byte-for-byte the platform's answer on .NET, ill-formed surrogates included (the
    /// replacement bytes `utf8Bytes` describes), and the same answer under Fable. Over an
    /// ill-formed string it therefore has the platform's second pre-images too; `trySha256Hex` is
    /// the guarded, refusing form.
    let sha256Hex (input: string) : string = sha256HexOfBytes (utf8Bytes input)

    /// `sha256Hex`, GUARDED (Phase 306): the digest of a well-formed string, or the first unpaired
    /// surrogate as a typed refusal. Over a well-formed string it is exactly `Ok (sha256Hex
    /// input)`; over an ill-formed one `sha256Hex` still answers — with the digest of whichever
    /// string the replacement bytes also spell — and this does not.
    let trySha256Hex (input: string) : Result<string, IllFormedUtf16> =
        tryUtf8Bytes input |> Result.map sha256HexOfBytes

/// The algorithm a `Digest` was taken under (Phase 382). One case: SHA-256, the spine's one
/// cryptographic digest (`Hash.sha256Hex`). A second algorithm would be a new case, and its tagged
/// spelling (`Digest.print`) a new prefix, so a digest written under one never reads as the other.
[<RequireQualifiedAccess>]
type DigestAlgorithm =
    /// SHA-256 (FIPS 180-4), 64 lowercase hex digits, tagged `sha256`: `Hash.sha256Hex`'s digest.
    | Sha256

/// A typed content digest (Phase 382, `DECISIONS.md` D122 as amended): the algorithm it was taken
/// under and its lowercase hex. Equal digests are one algorithm and one hex.
///
/// **Who can mint one.** The representation is internal, and so is the one constructor that hashes
/// arbitrary bytes, so a consumer cannot digest a rendering it did not pin. The public constructors
/// all start from a CANONICAL rendering: `Digest.tryOfFields` over `Hash.canonicalFields`'s injective
/// pre-image — the encoding every key and digest on the `canonicalFields` roster is minted through,
/// the Phase 314 digests among them — and, in `Fuaran.Core.ContentAddress`, the profile-pinned
/// constructor over `Canonical`'s text under a named `EncodingProfile`. Reading a digest that is
/// already stored is not minting one: `Digest.tryParse` reads the tagged text and `Digest.tryOfHex`
/// a bare hex under an algorithm the caller names; both refuse anything but lowercase hex of the
/// algorithm's length, so a stored digest reads back to exactly the bytes it was stored as.
type Digest =
    internal
        { Alg: DigestAlgorithm
          HexText: string }

    /// The algorithm the digest was taken under.
    member d.Algorithm: DigestAlgorithm = d.Alg

    /// The digest as lowercase hex, with no algorithm tag — the form `Hash.sha256Hex` returns and the
    /// Phase 314 digest maps store.
    member d.Hex: string = d.HexText

    /// The tagged text `Digest.print` writes.
    override d.ToString() =
        match d.Alg with
        | DigestAlgorithm.Sha256 -> "sha256:" + d.HexText

/// Minting, reading and writing a `Digest` (Phase 382).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Digest =

    /// The tag `print` writes before the hex, and the hex length, per algorithm.
    let private tagOf (a: DigestAlgorithm) : string =
        match a with
        | DigestAlgorithm.Sha256 -> "sha256"

    let private hexLength (a: DigestAlgorithm) : int =
        match a with
        | DigestAlgorithm.Sha256 -> 64

    /// THE BYTES-LEVEL CONSTRUCTOR — internal by the D122 ruling. Visible to `Fuaran.Core.Tree` and
    /// to `Fuaran.Core.ContentAddress` (its `InternalsVisibleTo`), which call it only over a
    /// canonical rendering; no other package can hand it bytes.
    let internal ofSha256Bytes (bytes: byte[]) : Digest =
        { Alg = DigestAlgorithm.Sha256
          HexText = Hash.sha256HexOfBytes bytes }

    /// The SHA-256 digest of a field sequence's canonical pre-image (`Hash.canonicalFields`) — the
    /// encoding every entry on the key roster is minted through. Over well-formed fields its `Hex` is
    /// exactly `Hash.sha256Hex (Hash.canonicalFields fields)`, so `Tree.ownDigest`, `Tree.frameDigest`
    /// and the `Subtree` digests of `Tree.digests` are each this digest of their own fields, byte for
    /// byte. GUARDED: a field carrying an unpaired surrogate is refused, typed, at its index in the
    /// pre-image, because a digest over the replacement bytes would be some other field list's.
    let tryOfFields (fields: string list) : Result<Digest, IllFormedUtf16> =
        Hash.tryUtf8Bytes (Hash.canonicalFields fields) |> Result.map ofSha256Bytes

    let private isLowerHex (s: string) : bool =
        s |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))

    /// Read a bare stored hex as a digest under the algorithm the caller names — the form the
    /// Phase 314 maps and `Hash.sha256Hex` store, which carries no algorithm of its own. Refuses
    /// anything but lowercase hex of the algorithm's length: an uppercase spelling is a different
    /// string, and accepting it would make `Hex` differ from the stored bytes.
    let tryOfHex (algorithm: DigestAlgorithm) (hex: string) : Result<Digest, string> =
        if isNull hex then
            Error "a digest's hex is null"
        elif hex.Length <> hexLength algorithm then
            Error(
                "a "
                + tagOf algorithm
                + " digest is "
                + string (hexLength algorithm)
                + " hex digits, got "
                + string hex.Length
            )
        elif not (isLowerHex hex) then
            Error("a digest's hex is lowercase 0-9a-f only: " + hex)
        else
            Ok { Alg = algorithm; HexText = hex }

    /// The tagged text of a digest: the algorithm's tag, a colon, the lowercase hex
    /// (`sha256:<64 hex>`) — the spelling the conformance vectors and the wire baselines store.
    let print (d: Digest) : string = d.ToString()

    /// Read the tagged text `print` writes. Exactly `<tag>:<hex>` for a known tag, the hex under
    /// `tryOfHex`'s rule; anything else — no tag, an unknown one, whitespace, uppercase — is refused
    /// with the reason. `print` after `tryParse` is the identity on every text it accepts.
    let tryParse (text: string) : Result<Digest, string> =
        if isNull text then
            Error "a digest's text is null"
        else
            match text.IndexOf ':' with
            | -1 -> Error("a digest's text is <algorithm>:<hex>, and has no ':': " + text)
            | i ->
                match text.Substring(0, i) with
                | "sha256" -> tryOfHex DigestAlgorithm.Sha256 (text.Substring(i + 1))
                | other -> Error("unknown digest algorithm: " + other)
