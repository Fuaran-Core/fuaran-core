namespace Fuaran.Core

/// Deterministic content hashing. Two regimes, separately named so a call site says which one it is
/// in: `fnv1a` — 32-bit, fast, NOT cryptographic — for staleness fingerprints and the
/// content-hashed bounded-escape regions; `sha256Hex` / `sha256Bytes` — the pinned pure FIPS 180-4
/// digest — wherever an adversary would gain by forging the value. Both are FSharp.Core-only and
/// both compile under Fable, but only `sha256*` is certified to produce the SAME VALUE on both
/// pipelines; see the warning on `fnv1a`.
module Hash =

    /// A 32-bit non-cryptographic content fingerprint (FNV-1a, lowercase hex). Cheap, and a second
    /// pre-image is seconds of search — so it belongs on a cache key or a rebuild stamp, never under
    /// a signature. Use `sha256Hex` for anything an adversary would gain by forging.
    ///
    /// **NOT .NET/Fable value-portable — measured, 2026-08-21.** `h * 16777619u` is emitted by Fable
    /// as a plain JS multiply. The product reaches ~3.6e16, past the 2^53 exact-integer ceiling, so
    /// precision is lost INSIDE the operation: `fnv1a "a"` is `e40c292c` on .NET and `e40c2930`
    /// under Fable. A trailing `&&& 0xFFFFFFFFu` does not repair it — by then the low bits are
    /// already gone — which is why this is left as a recorded limit rather than patched in passing;
    /// the fix is a split-half 32-bit multiply, and it changes every Fable-side value. The
    /// Fable-compile gate cannot see this: it proves the code transpiles, not that it agrees.
    /// So: a value hashed here on one pipeline must not be compared with one hashed on the other.
    /// `sha256Hex` has no such limit — it is byte-identical on both, which is what the masked add
    /// in its inner loop exists for.
    let fnv1a (s: string) : string =
        let mutable h = 2166136261u

        for ch in s do
            h <- h ^^^ uint32 ch
            h <- h * 16777619u

        h.ToString("x8")

    /// The fold/join separator shared by the content-hash folds (`Tree.contentHash` /
    /// `Tree.encodeHash`) and the cross-host parity projections (`Validator.canonicalCodes`):
    /// the ASCII unit-separator-class control byte `U+0001` (SOH). No canonical wire encoding
    /// emits it unescaped, so two adjacent fields cannot run together into a colliding pre-image.
    /// Named once here so the parity-relevant constant is defined in a single place.
    let foldSep = ""

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
    /// zeros. So this line is protected by review and by this comment, not by a gate — do not
    /// "simplify" it. Closing that hole needs a runtime .NET-vs-Fable value comparison; the
    /// repo's Fable step is a COMPILE gate, and a compile cannot disagree about a number.
    let inline private (.+.) (x: uint32) (y: uint32) : uint32 = (x + y) &&& 0xFFFFFFFFu

    /// UTF-8 encode a string to bytes (BMP + surrogate pairs), pure managed. `System.Text.Encoding`
    /// does not exist under Fable, and this is the encoder `sha256Hex` hashes through — exposed
    /// because a caller composing a digest pre-image out of parts needs the same bytes.
    let utf8Bytes (s: string) : byte[] =
        let out = ResizeArray<byte>()
        let mutable i = 0

        while i < s.Length do
            let c = int s[i]

            if c < 0x80 then
                out.Add(byte c)
            elif c < 0x800 then
                out.Add(byte (0xC0 ||| (c >>> 6)))
                out.Add(byte (0x80 ||| (c &&& 0x3F)))
            elif c >= 0xD800 && c <= 0xDBFF && i + 1 < s.Length then
                let lo = int s[i + 1]
                let cp = 0x10000 + ((c - 0xD800) <<< 10) + (lo - 0xDC00)
                out.Add(byte (0xF0 ||| (cp >>> 18)))
                out.Add(byte (0x80 ||| ((cp >>> 12) &&& 0x3F)))
                out.Add(byte (0x80 ||| ((cp >>> 6) &&& 0x3F)))
                out.Add(byte (0x80 ||| (cp &&& 0x3F)))
                i <- i + 1
            else
                out.Add(byte (0xE0 ||| (c >>> 12)))
                out.Add(byte (0x80 ||| ((c >>> 6) &&& 0x3F)))
                out.Add(byte (0x80 ||| (c &&& 0x3F)))

            i <- i + 1

        out.ToArray()

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
    /// wants. Byte-for-byte the platform's answer on .NET, and the same answer under Fable.
    let sha256Hex (input: string) : string = sha256HexOfBytes (utf8Bytes input)
