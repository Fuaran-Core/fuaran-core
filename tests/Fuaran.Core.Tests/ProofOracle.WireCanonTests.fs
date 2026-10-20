module Fuaran.Core.Tests.ProofOracle.WireCanonTests

// The `Proofs.Oracle` differential for `proofs/WireCanon.fst`: the extracted model
// (`proofs/oracle/WireCanon.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges

let private canonWire: WireCanon.wire<int, float> =
    { int_str = fun i -> canonToChs (string i)
      float_str = fun f -> canonToChs (f.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
      fclass =
        fun f ->
            if System.Double.IsNaN f then
                WireCanon.FNaN
            elif System.Double.IsPositiveInfinity f then
                WireCanon.FPosInf
            elif System.Double.IsNegativeInfinity f then
                WireCanon.FNegInf
            else
                WireCanon.FFinite
      is_zero = fun f -> f = 0.0
      pos_zero = 0.0
      key_le = fun a b -> System.String.CompareOrdinal(canonFromChs a, canonFromChs b) <= 0
      tok_read =
        fun t ->
            match Json.parse (canonFromChs t) with
            | Result.Ok v -> WireCanon.Ok(canonToModel v)
            | Result.Error m -> WireCanon.Error m
      str_ok = fun s -> strictlyEncodable (canonFromChs s) }

/// The GO-RED instrument, and this family's counterpart to the fold family's blind footprint and
/// the decode family's blind integer bridge: rule 2's comparator REVERSED, so the sort the rule
/// mandates still runs but orders keys the other way. Every object carrying two distinct keys must
/// then disagree with production, and a document carrying none still agrees — so the comparison is
/// known to be both one that can lose and one that is narrow to the rule it is about.
let private canonWireGoRed: WireCanon.wire<int, float> =
    { canonWire with
        key_le = fun a b -> System.String.CompareOrdinal(canonFromChs a, canonFromChs b) >= 0 }

/// Rule 5's own slot rule, as a predicate on a value: a token with no `.`, no `e`/`E` and a
/// magnitude inside the int53 window "keeps integer identity", so a float whose canonical token
/// carries neither marker is INDISTINGUISHABLE ON THE WIRE from the integer of that token. That is
/// the model's `canonical`, and the subset every theorem in section 10 is stated over.
let rec private isCanonicalValue (v: JVal) : bool =
    match v with
    | JFloat f ->
        System.Double.IsFinite f
        && (let t = Canon.canonicalFloat f in t.Contains "." || t.Contains "E")
    | JArr xs -> xs |> List.forall isCanonicalValue
    | JObj fs -> fs |> List.forall (snd >> isCanonicalValue)
    | _ -> true

type private CanonTally =
    {
        Docs: int
        Diffs: string list
        /// Objects carrying two or more DISTINCT keys — the shape rule 2's sort is observable on,
        /// and therefore the shape the go-red needs to have met.
        SortableObjects: int
        /// Strings carrying a character rule 6 escapes.
        EscapedStrings: int
        /// Strings carrying a control character — the `\u00xx` arm specifically.
        ControlStrings: int
        /// Floats whose canonical token is in scientific notation — rule 5's other layout.
        ScientificFloats: int
        /// Canonical documents whose model round trip was checked.
        RoundTrips: int
    }

let private emptyCanonTally =
    { Docs = 0
      Diffs = []
      SortableObjects = 0
      EscapedStrings = 0
      ControlStrings = 0
      ScientificFloats = 0
      RoundTrips = 0 }

let rec private canonShape (v: JVal) (t: CanonTally) : CanonTally =
    match v with
    | JStr s ->
        let t =
            if s |> Seq.exists (fun c -> c = '"' || c = '\\' || c < ' ') then
                { t with
                    EscapedStrings = t.EscapedStrings + 1 }
            else
                t

        if s |> Seq.exists (fun c -> c < ' ') then
            { t with
                ControlStrings = t.ControlStrings + 1 }
        else
            t
    | JFloat f when System.Double.IsFinite f && (Canon.canonicalFloat f).Contains "E" ->
        { t with
            ScientificFloats = t.ScientificFloats + 1 }
    | JArr xs -> xs |> List.fold (fun acc x -> canonShape x acc) t
    | JObj fs ->
        let t =
            if (fs |> List.map fst |> List.distinct |> List.length) >= 2 then
                { t with
                    SortableObjects = t.SortableObjects + 1 }
            else
                t

        fs |> List.fold (fun acc (_, x) -> canonShape x acc) t
    | _ -> t

/// One document, asked of production and of the model. The comparison is the BYTES — which is the
/// only comparison that means anything for an encoder whose whole job is to produce a digest input.
let private canonProbe (w: WireCanon.wire<int, float>) (label: string) (v: JVal) (t: CanonTally) : CanonTally =
    let expected = Canon.render v
    let got = canonFromChs (WireCanon.render w (canonToModel v))

    let t = canonShape v { t with Docs = t.Docs + 1 }

    if expected = got then
        t
    else
        { t with
            Diffs =
                sprintf "%s: production rendered\n  %s\nthe model rendered\n  %s" label expected got
                :: t.Diffs }

/// The value-level round trip, on the canonical subset: the model's own reader over the model's
/// own rendering, against production's parser over production's rendering. A disagreement here
/// says the reader and the encoder have drifted apart, which is the one way the injectivity
/// theorem could be true of a model that is not this encoder.
let private canonRoundTrip (label: string) (v: JVal) (t: CanonTally) : CanonTally =
    if not (isCanonicalValue v) then
        t
    elif (Json.firstRepeatedMember v).IsSome then
        // Phase 462: the generated pool draws keys with replacement, so it reaches objects with a
        // repeated member. Section 6's reader reads exactly what `render` emits, repeated keys
        // included; production's parser refuses the text. The bridge theorem carries the
        // distinct-keys hypothesis, and here the refusal itself is what is held.
        match Json.read ReadOptions.strict (Canon.render v) with
        | Result.Error(JsonRefusal.Malformed e) when e.Kind = DuplicateMember -> t
        | other ->
            { t with
                Diffs =
                    sprintf "%s: a repeated member — production read %A, not a DuplicateMember refusal" label other
                    :: t.Diffs }
    else
        let t = { t with RoundTrips = t.RoundTrips + 1 }

        let modelSide =
            match WireCanon.read canonWire (WireCanon.render canonWire (canonToModel v)) with
            | WireCanon.Ok(mv, []) -> Result.Ok(canonOfModel mv)
            | WireCanon.Ok(_, rest) -> Result.Error(sprintf "the reader left %d characters unread" (List.length rest))
            | WireCanon.Error m -> Result.Error m

        let productionSide =
            match Json.parse (Canon.render v) with
            | Result.Ok pv -> Result.Ok pv
            | Result.Error m -> Result.Error m

        if modelSide = productionSide then
            t
        else
            { t with
                Diffs =
                    sprintf "%s: round trip — production read %A, the model read %A" label productionSide modelSide
                    :: t.Diffs }

// ---- the generated pool ----

let private canonKeys =
    [| "$type"
       "a"
       "b"
       "Z"
       "z"
       "0"
       "_x"
       "é"
       "\u00e9x"
       "\U0001D11E"
       "\uE000"
       "kind"
       "A"
       "aa" |]

let private canonChars =
    [| "a"
       "Z"
       "0"
       " "
       "/"
       "\""
       "\\"
       "\n"
       "\r"
       "\t"
       "\u0000"
       "\u0007"
       "\u001f"
       "é"
       "字"
       "\U0001D11E"
       "$"
       "E"
       "." |]

let private canonFloats =
    [| 0.5
       -1.25
       1e21
       1e-7
       1.602e-19
       5e-324
       0.1
       3.141592653589793
       -0.0
       0.0
       2.0
       1e17
       100.0 |]

let private genCanonValue (r: int ref) (depth: int) : JVal =
    let draw (n: int) =
        r.Value <- nextCanonSeed r.Value
        r.Value % n

    let rec go (depth: int) : JVal =
        match draw (if depth <= 0 then 5 else 7) with
        | 0 ->
            let n = draw 5
            JStr(String.concat "" [ for _ in 1..n -> canonChars[draw canonChars.Length] ])
        | 1 -> JInt(draw 2000 - 1000)
        | 2 -> JBool(draw 2 = 0)
        | 3 -> JFloat canonFloats[draw canonFloats.Length]
        | 4 -> JStr canonKeys[draw canonKeys.Length]
        | 5 -> JArr [ for _ in 1 .. draw 4 -> go (depth - 1) ]
        | _ -> JObj [ for _ in 1 .. draw 5 -> canonKeys[draw canonKeys.Length], go (depth - 1) ]

    go depth

let private canonGenerated (w: WireCanon.wire<int, float>) (seed: int) (trials: int) (roundTrip: bool) : CanonTally =
    let r = ref seed
    let mutable t = emptyCanonTally

    for i in 1..trials do
        let v = genCanonValue r 3
        t <- canonProbe w (sprintf "generated seed=%d iteration=%d" seed i) v t

        if roundTrip then
            t <- canonRoundTrip (sprintf "generated seed=%d iteration=%d" seed i) v t

    t

let private canonCorpus (w: WireCanon.wire<int, float>) (family: string) (roundTrip: bool) : CanonTally =
    let mutable t = emptyCanonTally

    for name, text in JsonParseDiff.corpusTexts family do
        match Json.parse text with
        | Result.Error m -> failtestf "the corpus fixture %s/%s did not parse: %s" family name m
        | Result.Ok v ->
            t <- canonProbe w (sprintf "%s/%s" family name) v t

            if roundTrip then
                t <- canonRoundTrip (sprintf "%s/%s" family name) v t

    t

// ---- the guard (Phase 165): `WireCanon.try_render` beside `Canon.tryRender` ----

/// The model names a refusal as DATA — a path of steps and the float it found — where production
/// emits one string. This renders the model's refusal in production's spelling so the two can be
/// compared as the `Result` a caller actually receives. The member key goes through the MODEL's
/// own `quoted` (rule 6's escape), not through `Canon`, so the bridge hands production nothing to
/// agree with itself about; the index crosses the `nat` → `int` width boundary here, where
/// `oracle/Prims.fs` says such a conversion belongs.
let private guardPathOfModel (p: WireCanon.pstep list) : string =
    "$"
    + (p
       |> List.map (fun s ->
           match s with
           | WireCanon.PItem i -> "[" + string (int i) + "]"
           | WireCanon.PMember k -> "[" + canonFromChs (WireCanon.quoted k) + "]")
       |> String.concat "")

let private guardModelSide (w: WireCanon.wire<int, float>) (v: JVal) : Result<string, string> =
    match WireCanon.try_render w (canonToModel v) with
    | WireCanon.Rendered bytes -> Result.Ok(canonFromChs bytes)
    | WireCanon.Refused(p, f) ->
        let tok =
            match w.fclass f with
            | WireCanon.FNaN -> "NaN"
            | WireCanon.FPosInf -> "Infinity"
            | WireCanon.FNegInf -> "-Infinity"
            | WireCanon.FFinite -> "<the model refused a float its own wire calls finite>"

        Result.Error(
            "non-finite float has no canonical rendering of its own: "
            + tok
            + " at "
            + guardPathOfModel p
        )
    | WireCanon.RefusedRepeated(p, k) ->
        // Phase 462. The model names the object's path and the repeated name; the sentence is
        // production's, with the name under `Json.escape` as `firstRepeatedMember` spells it.
        Result.Error(
            "repeated member has no canonical rendering of its own: the member name \""
            + Json.escape (canonFromChs k)
            + "\" is repeated at "
            + guardPathOfModel p
        )
    | WireCanon.RefusedString(p, s, isKey) ->
        // Phase 306. The model names the STRING and whether it is a member key; which unit of it
        // is unpaired is read off the string here, by a scan that is this host's own, so the
        // message compared is the one production emits and neither side is asked to agree with
        // itself about the index.
        let text = canonFromChs s

        let isHigh (c: char) = int c >= 0xD800 && int c <= 0xDBFF
        let isLow (c: char) = int c >= 0xDC00 && int c <= 0xDFFF

        let rec firstUnpaired (k: int) : int =
            if k >= text.Length then
                -1
            elif isHigh text[k] then
                (if k + 1 < text.Length && isLow text[k + 1] then
                     firstUnpaired (k + 2)
                 else
                     k)
            elif isLow text[k] then
                k
            else
                firstUnpaired (k + 1)

        let k = firstUnpaired 0

        if k < 0 then
            Result.Error "<the model refused a string this host finds well-formed>"
        else
            Result.Error(
                "ill-formed string has no canonical rendering of its own: "
                + (if isKey then "a member key" else "a string")
                + " holds the unpaired surrogate U+"
                + (int text[k]).ToString("X4")
                + " at unit "
                + string k
                + " at "
                + guardPathOfModel p
            )

/// The guard's predicate, written a THIRD time and independently of both sides: does the value
/// hold a non-finite float anywhere. "Refuses exactly" is a claim about this set, and asking
/// either side under test to define it would make the claim circular.
let rec private holdsNonFinite (v: JVal) : bool =
    match v with
    | JFloat f -> not (System.Double.IsFinite f)
    | JArr xs -> xs |> List.exists holdsNonFinite
    | JObj fs -> fs |> List.exists (snd >> holdsNonFinite)
    | _ -> false

/// The guard's SECOND predicate (Phase 306), written the same way: does the value hold a string or
/// a member key that is not well-formed UTF-16 — by the platform's strict encoder.
/// The guard's THIRD predicate (Phase 462): does the value hold an object carrying a member name
/// twice — production's own scan, which the model's `first_repeated` is held to.
let private holdsRepeated (v: JVal) : bool = (Json.firstRepeatedMember v).IsSome

let rec private holdsIllFormed (v: JVal) : bool =
    match v with
    | JStr s -> not (strictlyEncodable s)
    | JArr xs -> xs |> List.exists holdsIllFormed
    | JObj fs -> fs |> List.exists (fun (k, x) -> not (strictlyEncodable k) || holdsIllFormed x)
    | _ -> false

type private GuardTally =
    {
        Docs: int
        Diffs: string list
        /// Documents production refused.
        Refused: int
        /// Refusals whose path is two or more steps deep — the scan's recursion, not its leaf arm.
        DeepRefusals: int
        /// Refusals naming each of the three tokens.
        NaNs: int
        PosInfs: int
        NegInfs: int
        /// ACCEPTED documents carrying a float outside the canonical subset — an integer-shaped
        /// token or a zero — which is the set the guard must not refuse.
        AcceptedNormalised: int
        /// Refusals naming an ill-formed STRING (Phase 306), and those of them naming a member key.
        StringRefusals: int
        KeyRefusals: int
        /// Documents holding BOTH faults, refused — for the float, which is looked for first.
        BothFaults: int
    }

let private emptyGuardTally =
    { Docs = 0
      Diffs = []
      Refused = 0
      DeepRefusals = 0
      NaNs = 0
      PosInfs = 0
      NegInfs = 0
      AcceptedNormalised = 0
      StringRefusals = 0
      KeyRefusals = 0
      BothFaults = 0 }

/// One document, asked of production's guard and of the model's. Three comparisons, each of which
/// can lose on its own: the two `Result`s agree (message and path included); an `Ok` is exactly
/// `Canon.render`'s bytes; and the verdict is `Error` precisely when the independent predicate
/// says a non-finite float is present.
let private guardProbe (w: WireCanon.wire<int, float>) (label: string) (v: JVal) (t: GuardTally) : GuardTally =
    let production = Canon.tryRender v
    let model = guardModelSide w v
    let t = { t with Docs = t.Docs + 1 }

    let diff (what: string) (t: GuardTally) =
        { t with
            Diffs =
                sprintf "%s: %s\n  production: %A\n  the model:  %A" label what production model
                :: t.Diffs }

    let t =
        if production = model then
            t
        else
            diff "the guards disagree" t

    match production with
    | Result.Ok bytes ->
        let t =
            if bytes = Canon.render v then
                t
            else
                diff "an accepted value did not render to Canon.render's bytes" t

        let t =
            if holdsNonFinite v then
                diff "production ACCEPTED a value holding a non-finite float" t
            else
                t

        let t =
            if holdsIllFormed v then
                diff "production ACCEPTED a value holding an ill-formed string" t
            else
                t

        if isCanonicalValue v then
            t
        else
            { t with
                AcceptedNormalised = t.AcceptedNormalised + 1 }
    | Result.Error m ->
        let forString = m.StartsWith "ill-formed string"

        let t =
            if holdsNonFinite v || holdsIllFormed v || holdsRepeated v then
                t
            else
                diff
                    "production REFUSED a value holding no non-finite float, no ill-formed string and no repeated member"
                    t

        // The third refusal comes after the other two (Phase 462): a value holding either earlier
        // fault is never refused for a repeated member.
        let t =
            if (holdsNonFinite v || holdsIllFormed v) && m.StartsWith "repeated member" then
                diff "a value holding an earlier fault was refused for a repeated member" t
            else
                t

        // The two refusals are ORDERED: a value holding a non-finite float is refused for it,
        // whatever its strings are.
        let t =
            if holdsNonFinite v && forString then
                diff "a value holding a non-finite float was refused for a string" t
            else
                t

        let steps = m |> Seq.filter (fun c -> c = '[') |> Seq.length

        { t with
            Refused = t.Refused + 1
            DeepRefusals = t.DeepRefusals + (if steps >= 2 then 1 else 0)
            NaNs = t.NaNs + (if m.Contains ": NaN at " then 1 else 0)
            PosInfs = t.PosInfs + (if m.Contains ": Infinity at " then 1 else 0)
            NegInfs = t.NegInfs + (if m.Contains ": -Infinity at " then 1 else 0)
            StringRefusals = t.StringRefusals + (if forString then 1 else 0)
            KeyRefusals =
                t.KeyRefusals
                + (if forString && m.Contains ": a member key holds " then
                       1
                   else
                       0)
            BothFaults = t.BothFaults + (if holdsNonFinite v && holdsIllFormed v then 1 else 0) }

/// The Phase 149 pool carries no non-finite float — it was built to measure the renderer, which
/// has nothing to say about one. This walks a drawn value and replaces roughly one numeric leaf in
/// three with one of the three, so the refusals land at every depth and position the pool reaches
/// rather than only at the root.
let private poisonCanonValue (r: int ref) (v: JVal) : JVal =
    let draw (n: int) =
        r.Value <- nextCanonSeed r.Value
        r.Value % n

    let rec go (v: JVal) : JVal =
        match v with
        | JFloat _
        | JInt _ when draw 3 = 0 ->
            (match draw 3 with
             | 0 -> JFloat nan
             | 1 -> JFloat infinity
             | _ -> JFloat -infinity)
        | JArr xs -> JArr(xs |> List.map go)
        | JObj fs -> JObj(fs |> List.map (fun (k, x) -> k, go x))
        | other -> other

    go v

let private guardGenerated (w: WireCanon.wire<int, float>) (seed: int) (trials: int) : GuardTally =
    let r = ref seed
    let mutable t = emptyGuardTally

    for i in 1..trials do
        let v = poisonCanonValue r (genCanonValue r 3)
        t <- guardProbe w (sprintf "generated seed=%d iteration=%d" seed i) v t

    t

let private guardCorpus (w: WireCanon.wire<int, float>) (family: string) : GuardTally =
    let mutable t = emptyGuardTally

    for name, text in JsonParseDiff.corpusTexts family do
        match Json.parse text with
        | Result.Error m -> failtestf "the corpus fixture %s/%s did not parse: %s" family name m
        | Result.Ok v -> t <- guardProbe w (sprintf "%s/%s" family name) v t

    t

/// The GO-RED instrument for the guard: a wire that cannot see NaN — it classifies one as finite,
/// so the model's scan walks past it. Every document whose FIRST non-finite float is a NaN must
/// then disagree with production, and every other document — including one refused for an
/// infinity — must still agree, which is what says the instrument is narrow to the predicate.
let private guardWireGoRed: WireCanon.wire<int, float> =
    { canonWire with
        fclass =
            fun f ->
                if System.Double.IsNaN f then
                    WireCanon.FFinite
                else
                    canonWire.fclass f }

// ---- the guard's second refusal (Phase 306): ill-formed strings, as values and as keys ----

/// The strings the pool is poisoned with: a lone high, a lone low after text, a high followed by a
/// high (the pair that encoded as U+10000 until Phase 290), a well-formed pair with a stray low
/// after it — and a well-formed pair alone, which is a character and must NOT be refused.
let private surrogateStrings: string[] =
    [| units16 [ 0xD800 ]
       "a" + units16 [ 0xDFFF ]
       units16 [ 0xD801; 0xD800 ]
       units16 [ 0xD83D; 0xDE00; 0xDE00 ]
       units16 [ 0xD83D; 0xDE00 ] |]

/// Replaces roughly one string in four and one member key in six with one of `surrogateStrings`,
/// so an ill-formed string lands at every depth and in both positions.
let private poisonCanonStrings (r: int ref) (v: JVal) : JVal =
    let draw (n: int) =
        r.Value <- nextCanonSeed r.Value
        r.Value % n

    let pick () =
        surrogateStrings[draw surrogateStrings.Length]

    let rec go (v: JVal) : JVal =
        match v with
        | JStr _ when draw 4 = 0 -> JStr(pick ())
        | JArr xs -> JArr(xs |> List.map go)
        | JObj fs -> JObj(fs |> List.map (fun (k, x) -> (if draw 6 = 0 then pick () else k), go x))
        | other -> other

    go v

/// The Phase 165 pool, with its strings poisoned and — one document in three — its floats too, so
/// the order of the two refusals is measured as well as each on its own.
let private guardGeneratedIllFormed (w: WireCanon.wire<int, float>) (seed: int) (trials: int) : GuardTally =
    let r = ref seed
    let mutable t = emptyGuardTally

    for i in 1..trials do
        let v = poisonCanonStrings r (genCanonValue r 3)
        let v = if i % 3 = 0 then poisonCanonValue r v else v
        t <- guardProbe w (sprintf "generated seed=%d iteration=%d" seed i) v t

    t

/// The GO-RED instrument for the string refusal: a wire whose well-formedness test accepts every
/// string. The model then renders, or names a later float, wherever production refuses for a
/// string — and agrees on every other document.
let private guardWireStringBlind: WireCanon.wire<int, float> =
    { canonWire with
        str_ok = fun _ -> true }

// ---- the declared-key-order renderer (Phase 306): `WireCanon.render_ordered` beside
//      `Canon.renderOrdered` ----

type private OrderedTally =
    {
        Docs: int
        Diffs: string list
        /// Documents whose ordered rendering is NOT `Canon.render`'s — an object authored out of
        /// key order somewhere — without which this would be the Phase 149 differential again.
        Unsorted: int
        /// Canonical documents the model's reader returned EXACTLY, members in authored order.
        ReadBack: int
    }

let private orderedProbe
    (render: WireCanon.jval<int, float> -> WireCanon.ch list)
    (label: string)
    (v: JVal)
    (t: OrderedTally)
    : OrderedTally =
    let production = Canon.renderOrdered v
    let m = canonToModel v
    let model = canonFromChs (render m)

    let t =
        { t with
            Docs = t.Docs + 1
            Unsorted = t.Unsorted + (if production <> Canon.render v then 1 else 0) }

    let t =
        if production = model then
            t
        else
            { t with
                Diffs =
                    sprintf "%s: the renderers disagree\n  production: %s\n  the model:  %s" label production model
                    :: t.Diffs }

    // `render_ordered_injective`'s engine, measured: on the canonical subset the reader returns
    // the value itself — no normal form, the members where the author put them.
    if isCanonicalValue v && not (holdsNonFinite v) then
        match WireCanon.read canonWire (WireCanon.render_ordered canonWire m) with
        | WireCanon.Ok(back, []) when back = m -> { t with ReadBack = t.ReadBack + 1 }
        | other ->
            { t with
                Diffs =
                    sprintf "%s: the reader did not return the value it was rendered from: %A" label other
                    :: t.Diffs }
    else
        t

let private orderedGenerated
    (render: WireCanon.jval<int, float> -> WireCanon.ch list)
    (seed: int)
    (trials: int)
    : OrderedTally =
    let r = ref seed

    let mutable t =
        { Docs = 0
          Diffs = []
          Unsorted = 0
          ReadBack = 0 }

    for i in 1..trials do
        t <- orderedProbe render (sprintf "generated seed=%d iteration=%d" seed i) (genCanonValue r 3) t

    t

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- the canonical encoder, over the corpus and a generated pool (Phase 149) ----

          testCase "the canon oracle agrees with Canon.render over every nodes/ fixture"
          <| fun _ ->
              let t = onBigStack (fun () -> canonCorpus canonWire "nodes" true)

              Expect.isEmpty
                  t.Diffs
                  (sprintf "the canon oracle disagreed with production:\n%s" (renderCanonDiffs t.Diffs))

              Expect.isGreaterThan t.Docs 100 (sprintf "the nodes/ family was read at all (%d fixtures)" t.Docs)

              Expect.isGreaterThan
                  t.SortableObjects
                  100
                  (sprintf
                      "fixtures carrying an object with two or more DISTINCT keys (%d) — rule 2's sort is unobservable without one"
                      t.SortableObjects)

              Expect.isGreaterThan
                  t.RoundTrips
                  100
                  (sprintf "fixtures inside the canonical subset, whose round trip was checked (%d)" t.RoundTrips)

          testCase "the canon oracle agrees with Canon.render over every ops/ fixture"
          <| fun _ ->
              let t = onBigStack (fun () -> canonCorpus canonWire "ops" true)

              Expect.isEmpty
                  t.Diffs
                  (sprintf "the canon oracle disagreed with production:\n%s" (renderCanonDiffs t.Diffs))

              Expect.isGreaterThan t.Docs 10 (sprintf "the ops/ family was read at all (%d fixtures)" t.Docs)

          testCase "the canon oracle agrees with Canon.render over a generated JVal pool"
          <| fun _ ->
              // The pool is where the corpus is thin: rule 6's three escape classes (a quote, a
              // backslash, a control character), rule 2's comparator above the BMP (an astral key
              // beside a private-use one — the pair WIRE_FORMAT's own rule-2 note says UTF-16
              // order decides differently from code-point order), and rule 5's scientific layout.
              let t = onBigStack (fun () -> canonGenerated canonWire 1490 1200 true)

              Expect.isEmpty
                  t.Diffs
                  (sprintf "the canon oracle disagreed with production:\n%s" (renderCanonDiffs t.Diffs))

              // adequacy — a pool that never reached a shape measures nothing about the rule that
              // governs it. MEASURED at 1200 documents, seed 1490, and every threshold below sits
              // under its measurement with headroom and above zero.
              Expect.isGreaterThan
                  t.SortableObjects
                  100
                  (sprintf "documents carrying a sortable object (%d)" t.SortableObjects)

              Expect.isGreaterThan
                  t.EscapedStrings
                  100
                  (sprintf "strings carrying a character rule 6 escapes (%d)" t.EscapedStrings)

              Expect.isGreaterThan
                  t.ControlStrings
                  20
                  (sprintf "strings carrying a CONTROL character — the \\u00xx arm specifically (%d)" t.ControlStrings)

              Expect.isGreaterThan
                  t.ScientificFloats
                  20
                  (sprintf "floats whose canonical token is in scientific notation (%d)" t.ScientificFloats)

              Expect.isGreaterThan
                  t.RoundTrips
                  200
                  (sprintf "documents inside the canonical subset, whose round trip was checked (%d)" t.RoundTrips)

          testCase "a model with rule 2's key order REVERSED loses — the comparison can fail"
          <| fun _ ->
              // The go-red. `render_injective_up_to_key_order` and `render_deterministic` both turn
              // on the sort being a canonical choice, so the instrument that must lose is one whose
              // sort is a DIFFERENT choice: the same comparator, reversed. Every object carrying two
              // distinct keys must then disagree — and a document carrying none must still agree,
              // which is what says the instrument is narrow to the rule rather than broken.
              let t = onBigStack (fun () -> canonGenerated canonWireGoRed 1490 1200 false)

              Expect.isGreaterThan
                  t.SortableObjects
                  100
                  (sprintf
                      "the go-red run reached objects with two distinct keys at all (%d) — otherwise it proves nothing"
                      t.SortableObjects)

              Expect.isNonEmpty t.Diffs "a model that sorts object keys the other way MUST disagree with Canon.render"

              // and it must NOT disagree on everything: a document with no sortable object is
              // outside rule 2 entirely, and an instrument that reddened those too would be
              // measuring something other than the key order.
              Expect.isLessThan
                  (List.length t.Diffs)
                  t.Docs
                  (sprintf
                      "the reversed comparator disagreed on %d of %d documents — it must leave the ones with no sortable object alone"
                      (List.length t.Diffs)
                      t.Docs)

          // Phase 253 — `integral_float_reads_back`, the numeral premise section 7 adds for the
          // floats OUTSIDE the canonical subset, evaluated at production's own reader: a finite float
          // whose token carries neither marker reads back as a number rendering as that token. Until
          // Phase 253 it was false past 2^53, so the instrument also runs a reader carrying the old
          // guard, which must lose there and only there.
          testCase "a finite float outside the canonical subset reads back and re-renders as its token (Phase 253)"
          <| fun _ ->
              let premiseFails (w: WireCanon.wire<int, float>) (f: float) : string option =
                  let tok = WireCanon.canonical_float w f

                  if WireCanon.float_canonical w f then
                      None
                  else
                      match w.tok_read tok with
                      | WireCanon.Ok v when canonFromChs (WireCanon.render w v) = canonFromChs tok -> None
                      | other -> Some(sprintf "%s -> %A" (canonFromChs tok) other)

              let rnd = System.Random 253

              let pastInt53 =
                  [ for _ in 1..400 ->
                        let m = rnd.NextInt64(1L <<< 52, 1L <<< 53)
                        let f = float m * float (1L <<< (1 + rnd.Next 4))
                        if rnd.Next 2 = 0 then f else -f ]

              let pool =
                  [ 0.0
                    -0.0
                    2.0
                    -7.0
                    2147483647.0
                    2147483648.0
                    1e15
                    9007199254740992.0
                    9007199254740994.0
                    -9007199254740994.0
                    1e16
                    1.8205257897171752e16
                    99999999999999984.0 ]
                  @ pastInt53

              let outside =
                  pool |> List.filter (fun f -> not (WireCanon.float_canonical canonWire f))

              Expect.isGreaterThan
                  (outside |> List.filter (fun f -> abs f > 9007199254740992.0) |> List.length)
                  100
                  "the pool reached marker-less floats past 2^53 at all — otherwise the premise was asked nothing new"

              Expect.isEmpty
                  (pool |> List.choose (premiseFails canonWire))
                  "every finite float outside the canonical subset reads back as a number rendering as its token"

              // The go-red: production's reader with the guard it carried before Phase 253, which
              // refused every integer token past 2^53.
              let oldGuard: WireCanon.wire<int, float> =
                  { canonWire with
                      tok_read =
                          fun t ->
                              let s = canonFromChs t
                              let digits = s.TrimStart '-'

                              let integerPastInt53 =
                                  not (s.Contains "." || s.Contains "E")
                                  && (digits.Length > 16
                                      || (digits.Length = 16
                                          && System.String.CompareOrdinal(digits, "9007199254740992") > 0))

                              if integerPastInt53 then
                                  WireCanon.Error "integer literal outside the int53 safe range"
                              else
                                  canonWire.tok_read t }

              let lost = pool |> List.choose (premiseFails oldGuard)
              Expect.isNonEmpty lost "a reader with the old guard MUST fail the premise"

              Expect.isEmpty
                  (pool
                   |> List.filter (fun f -> abs f <= 9007199254740992.0)
                   |> List.choose (premiseFails oldGuard))
                  "and only past 2^53 — at or inside it the old reader satisfies the premise too"

          testCase "the four renderings that ALIAS — `render_injective` is false, and this is why"
          <| fun _ ->
              // The phase's finding, as assertions over PRODUCTION rather than as prose. The shard
              // asked for `render a == render b ==> a == b`; these are the four ways it fails, and
              // `proofs/WireCanon.fst` carries each as a lemma. They go red if the encoder ever
              // changes — which is the point: three of them are documented design choices that a
              // future session must not "fix" by accident, and the first is the one worth knowing.

              // 1. a non-finite float is a STRING on the wire, so a digest over `JFloat nan`
              //    collides with the digest over `JStr "NaN"`. `Canon.render` refuses nothing and
              //    its bytes are pinned, so this stays asserted; the guarded `Canon.tryRender`
              //    beside it (Phase 165) is the entry point that does refuse — see its own cases.
              Expect.equal
                  (Canon.render (JFloat nan))
                  (Canon.render (JStr "NaN"))
                  "rule 5 renders NaN as the quoted string \"NaN\", which is what that STRING renders as"

              Expect.equal (Canon.render (JFloat infinity)) (Canon.render (JStr "Infinity")) "the same, for +infinity"

              Expect.equal (Canon.render (JFloat -infinity)) (Canon.render (JStr "-Infinity")) "the same, for -infinity"

              // 2. an integral float is an INTEGER on the wire — the documented normalisation
              //    `JVal`'s own type doc names.
              Expect.equal (Canon.render (JFloat 2.0)) (Canon.render (JInt 2)) "rule 5: `render (JFloat 2.0)` emits `2`"

              // 3. the two zeroes are one token — rule 5's `-0` collapse.
              Expect.equal (Canon.render (JFloat -0.0)) (Canon.render (JFloat 0.0)) "rule 5 collapses -0 to 0"

              // 4. member order is not observable — rule 2's sort. Not a loss: it is the purpose of
              //    a canonical form, and the reason the theorem is stated up to member order.
              Expect.equal
                  (Canon.render (JObj [ "a", JInt 1; "b", JInt 2 ]))
                  (Canon.render (JObj [ "b", JInt 2; "a", JInt 1 ]))
                  "rule 2 sorts, so the authored member order does not reach the bytes"

              // and the model agrees with production on all four, which is what makes the lemmas
              // statements about THIS encoder rather than about a model of one.
              for a, b in
                  [ JFloat nan, JStr "NaN"
                    JFloat infinity, JStr "Infinity"
                    JFloat -infinity, JStr "-Infinity"
                    JFloat 2.0, JInt 2
                    JFloat -0.0, JFloat 0.0
                    JObj [ "a", JInt 1; "b", JInt 2 ], JObj [ "b", JInt 2; "a", JInt 1 ] ] do
                  Expect.equal
                      (canonFromChs (WireCanon.render canonWire (canonToModel a)))
                      (canonFromChs (WireCanon.render canonWire (canonToModel b)))
                      (sprintf "the model aliases the pair production aliases: %A / %A" a b)

          testCase "the canon oracle handles the non-canonical arms production reaches"
          <| fun _ ->
              // Outside the canonical subset the theorems say nothing, but the DIFFERENTIAL still
              // has to agree — the model is a model of the whole encoder, not only of the part the
              // theorem covers, and a model that diverged here would be a model of something else.
              let mutable t = emptyCanonTally

              for v in
                  [ JFloat nan
                    JFloat infinity
                    JFloat -infinity
                    JFloat -0.0
                    JFloat 0.0
                    JFloat 2.0
                    JFloat 1e17
                    JInt 0
                    JInt -2147483648
                    JInt 2147483647
                    JStr ""
                    JStr "\u0000\u001f\"\\/"
                    JArr []
                    JObj []
                    JObj [ "a", JFloat nan; "$type", JStr "X" ]
                    JArr [ JFloat infinity; JObj [ "b", JArr [ JFloat -0.0 ] ] ] ] do
                  t <- canonProbe canonWire "non-canonical arm" v t

              Expect.isEmpty
                  t.Diffs
                  (sprintf
                      "the canon oracle disagreed with production off the canonical subset:\n%s"
                      (renderCanonDiffs t.Diffs))

          // ---- the guard beside the canonical encoder (Phase 165) ----

          testCase "Canon.tryRender refuses exactly the non-finite alias witnesses, and names them by path"
          <| fun _ ->
              // Refutation 1 of the four above is the one that is not a documented design choice,
              // and it has THREE witnesses, one per non-finite class. Each is refused, at the root.
              Expect.equal
                  (Canon.tryRender (JFloat nan))
                  (Result.Error "non-finite float has no canonical rendering of its own: NaN at $")
                  "a NaN is refused, by token and by path"

              Expect.equal
                  (Canon.tryRender (JFloat infinity))
                  (Result.Error "non-finite float has no canonical rendering of its own: Infinity at $")
                  "+infinity is refused"

              Expect.equal
                  (Canon.tryRender (JFloat -infinity))
                  (Result.Error "non-finite float has no canonical rendering of its own: -Infinity at $")
                  "-infinity is refused"

              // ... and the STRING each one aliases is a perfectly good value, and is not refused.
              // So are refutations 2, 3 and 4 — the integer-shaped float, the two zeroes and the
              // member order — which the format documents and the guard must therefore leave alone.
              for v in
                  [ JStr "NaN"
                    JStr "Infinity"
                    JStr "-Infinity"
                    JFloat 2.0
                    JInt 2
                    JFloat -0.0
                    JFloat 0.0
                    JFloat 1e17
                    JObj [ "a", JInt 1; "b", JInt 2 ]
                    JObj [ "b", JInt 2; "a", JInt 1 ] ] do
                  Expect.equal
                      (Canon.tryRender v)
                      (Result.Ok(Canon.render v))
                      (sprintf "a value holding no non-finite float is exactly `Ok (render v)`: %A" v)

              // The path: an array by index, a member by its canonically escaped key, the FIRST
              // offender in document order — and document order is AUTHORED order, not the sorted
              // order `render` would emit, because the scan runs before any sort.
              Expect.equal
                  (Canon.tryRender (JObj [ "$type", JStr "X"; "a", JArr [ JInt 1; JFloat infinity ] ]))
                  (Result.Error "non-finite float has no canonical rendering of its own: Infinity at $[\"a\"][1]")
                  "a nested offender is named through the member and the index"

              Expect.equal
                  (Canon.tryRender (JObj [ "b", JFloat nan; "a", JFloat infinity ]))
                  (Result.Error "non-finite float has no canonical rendering of its own: NaN at $[\"b\"]")
                  "the first offender in AUTHORED order is the one named, though `a` sorts first"

              Expect.equal
                  (Canon.tryRender (JArr [ JArr []; JObj [ "k\"\u0001", JFloat -infinity ] ]))
                  (Result.Error
                      "non-finite float has no canonical rendering of its own: -Infinity at $[1][\"k\\\"\\u0001\"]")
                  "a key carrying a quote and a control character is escaped as rule 6 escapes it"

              // `render` is untouched: every value refused above still renders, to the aliasing bytes.
              Expect.equal (Canon.render (JFloat nan)) "\"NaN\"" "the unguarded renderer's bytes did not move"

          testCase "the guard oracle agrees with Canon.tryRender over the corpus — and refuses none of it"
          <| fun _ ->
              for family, floor in [ "nodes", 100; "ops", 10 ] do
                  let t = onBigStack (fun () -> guardCorpus canonWire family)

                  Expect.isEmpty
                      t.Diffs
                      (sprintf
                          "the guard oracle disagreed with production on %s/:\n%s"
                          family
                          (renderCanonDiffs t.Diffs))

                  Expect.isGreaterThan
                      t.Docs
                      floor
                      (sprintf "the %s/ family was read at all (%d fixtures)" family t.Docs)

                  // JSON cannot spell a non-finite float, so a parsed fixture cannot hold one: on
                  // the corpus the guard is `Ok (render v)` everywhere, which is the acceptance's
                  // "agrees with render on the corpus" measured rather than assumed.
                  Expect.equal t.Refused 0 (sprintf "no %s/ fixture is refused" family)

          testCase "the guard oracle agrees with Canon.tryRender over a generated pool carrying non-finite floats"
          <| fun _ ->
              let t = onBigStack (fun () -> guardGenerated canonWire 1650 2400)

              Expect.isEmpty
                  t.Diffs
                  (sprintf "the guard oracle disagreed with production:\n%s" (renderCanonDiffs t.Diffs))

              // adequacy — MEASURED at 2400 documents, seed 1650: 398 refused (78 of them two or
              // more steps deep; 132 NaN, 146 +infinity, 120 -infinity) and 2002 accepted, 103 of
              // those carrying a float outside the canonical subset. Every threshold sits under
              // its measurement with headroom and above zero.
              Expect.isGreaterThan t.Refused 200 (sprintf "documents the guard refused (%d)" t.Refused)

              Expect.isGreaterThan
                  (t.Docs - t.Refused)
                  200
                  (sprintf "documents the guard accepted (%d)" (t.Docs - t.Refused))

              Expect.isGreaterThan
                  t.DeepRefusals
                  40
                  (sprintf
                      "refusals two or more steps deep — the scan's recursion, not its leaf arm (%d)"
                      t.DeepRefusals)

              Expect.isGreaterThan t.NaNs 60 (sprintf "refusals naming NaN (%d)" t.NaNs)
              Expect.isGreaterThan t.PosInfs 60 (sprintf "refusals naming Infinity (%d)" t.PosInfs)
              Expect.isGreaterThan t.NegInfs 60 (sprintf "refusals naming -Infinity (%d)" t.NegInfs)

              Expect.isGreaterThan
                  t.AcceptedNormalised
                  50
                  (sprintf
                      "ACCEPTED documents carrying an integer-shaped float or a zero (%d) — the set the guard must not refuse"
                      t.AcceptedNormalised)

          testCase "a guard model that cannot see NaN loses — on exactly the documents a NaN decides"
          <| fun _ ->
              // The go-red. `tryrender_refuses_exactly_aliasing` turns on the guard's predicate
              // being `fclass f <> FFinite`, so the instrument that must lose is one whose predicate
              // is narrower by one class. It must disagree on every document whose FIRST non-finite
              // float is a NaN — the model walks past it and either renders or names a later
              // infinity — and on NO other document, a refusal for an infinity included.
              let t = onBigStack (fun () -> guardGenerated guardWireGoRed 1650 2400)

              Expect.isGreaterThan t.NaNs 60 (sprintf "the go-red run reached NaN refusals at all (%d)" t.NaNs)

              Expect.equal
                  (List.length t.Diffs)
                  t.NaNs
                  (sprintf
                      "the NaN-blind model disagreed on %d documents and production named a NaN on %d — they must be the same documents"
                      (List.length t.Diffs)
                      t.NaNs)

          // ---- the guard's second refusal: ill-formed strings (Phase 306) ----

          testCase "the guard oracle agrees with Canon.tryRender over a generated pool carrying ill-formed strings"
          <| fun _ ->
              let t = onBigStack (fun () -> guardGeneratedIllFormed canonWire 3060 2400)

              Expect.isEmpty
                  t.Diffs
                  (sprintf "the guard oracle disagreed with production:\n%s" (renderCanonDiffs t.Diffs))

              // adequacy — each class the refusal has, reached: a string refusal, one naming a
              // member key, one two or more steps deep, a document holding BOTH faults (refused for
              // its float, which `guardProbe` checks), and documents still accepted.
              Expect.isGreaterThan t.StringRefusals 150 (sprintf "refusals naming a string (%d)" t.StringRefusals)
              Expect.isGreaterThan t.KeyRefusals 20 (sprintf "… of them naming a member key (%d)" t.KeyRefusals)
              Expect.isGreaterThan t.BothFaults 20 (sprintf "documents holding both faults (%d)" t.BothFaults)
              Expect.isGreaterThan t.DeepRefusals 40 (sprintf "refusals two or more steps deep (%d)" t.DeepRefusals)

              Expect.isGreaterThan
                  (t.Docs - t.Refused)
                  200
                  (sprintf "documents the guard accepted (%d)" (t.Docs - t.Refused))

          testCase "a guard model that cannot see an ill-formed string loses — on exactly the documents one decides"
          <| fun _ ->
              // The go-red. The instrument's well-formedness test accepts everything, so it must
              // disagree on every document production refuses FOR A STRING and on no other — a
              // refusal for a float, in a document that also holds an ill-formed string, included.
              let t =
                  onBigStack (fun () -> guardGeneratedIllFormed guardWireStringBlind 3060 2400)

              Expect.isGreaterThan
                  t.StringRefusals
                  150
                  (sprintf "the go-red run reached string refusals (%d)" t.StringRefusals)

              Expect.equal
                  (List.length t.Diffs)
                  t.StringRefusals
                  (sprintf
                      "the string-blind model disagreed on %d documents and production refused %d for a string — they must be the same documents"
                      (List.length t.Diffs)
                      t.StringRefusals)

          // ---- the declared-key-order renderer (Phase 306) ----

          testCase "the ordered-render oracle agrees with Canon.renderOrdered, and its reader returns the value"
          <| fun _ ->
              let t =
                  onBigStack (fun () -> orderedGenerated (WireCanon.render_ordered canonWire) 3061 2400)

              Expect.isEmpty
                  t.Diffs
                  (sprintf "the ordered-render oracle disagreed with production:\n%s" (renderCanonDiffs t.Diffs))

              Expect.isGreaterThan
                  t.Unsorted
                  200
                  (sprintf "documents authored out of key order — where this renderer is not `render` (%d)" t.Unsorted)

              Expect.isGreaterThan
                  t.ReadBack
                  500
                  (sprintf "canonical documents the reader returned exactly, members in authored order (%d)" t.ReadBack)

          testCase
              "a model that SORTS loses against Canon.renderOrdered — on exactly the documents authored out of order"
          <| fun _ ->
              // The go-red: `WireCanon.render` — rule 2 included — standing in for the ordered
              // renderer. It must lose wherever production's two renderers differ, and nowhere else.
              let r = ref 3061
              let mutable unsorted = 0
              let mutable lost = 0

              for _ in 1..2400 do
                  let v = genCanonValue r 3
                  let production = Canon.renderOrdered v
                  let sorting = canonFromChs (WireCanon.render canonWire (canonToModel v))

                  if production <> Canon.render v then
                      unsorted <- unsorted + 1

                  if production <> sorting then
                      lost <- lost + 1

              Expect.isGreaterThan unsorted 200 "the pool carries documents authored out of key order"
              Expect.equal lost unsorted "the sorting model lost on those documents and on no other" ]
