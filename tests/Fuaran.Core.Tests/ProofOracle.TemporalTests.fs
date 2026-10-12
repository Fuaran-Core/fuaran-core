module Fuaran.Core.Tests.ProofOracle.TemporalTests

// The `Proofs.Oracle` differential for `proofs/Temporal.fst`: the extracted model
// (`proofs/oracle/Temporal.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges

// ---------------------------------------------------------------------------
//  Phase 279 — the DECIMAL-TEXT bridge: a .NET string read as the model's symbols, and back.
//
//  The model reads a text as its symbol CLASSES — the minus, the point, a digit by its value,
//  and `Other` for every character the grammar does not name — because `DecimalText.parts`
//  refuses a text holding one wherever it stands, so the class is all a theorem needs. The
//  bridge is the only place a host fact enters: that `'0'..'9'` are the ten consecutive code
//  units `int c - int '0'` reads them as, and that `string` on a digit writes the one character
//  the model's `of_digits` stands for. Neither is modelled; both are what this family measures.
// ---------------------------------------------------------------------------
// Phase 428 — the UNIT ALGEBRA beside its model (`proofs/Unit.fst`), and the FIELD CODEC beside
// its (`proofs/WireColumn.fst`, section 10). The unit model reads a list of symbols over an
// enumerated ASCII alphabet; the bridge maps a character to its symbol and back, and carries a
// production unit to the model through its canonical text — so a disagreement about a text is a
// disagreement about `parse`, never about the bridge.
// ---------------------------------------------------------------------------

// ------------------------------------------------------------------------------------------
//  Phase 430 — the temporal encodings beside Temporal.fst: `TemporalText` and `TimeUnit` asked of
//  production and of the extracted model over the same days, texts and instants, in every unit.
//  The model's integers are `bigint`; production's day is an `int`, its second an integer-valued
//  `float` and its fraction an `int` — converted at the edge, which is where the width assumption
//  lives (the ladder's `column-vector-is-its-contents` says the same of the vectors).
// ------------------------------------------------------------------------------------------

module private TemporalDiff =

    type Tally =
        {
            Draws: int
            Diffs: string list
            /// Days inside and outside the canonical range; texts canonical and not.
            InRange: int
            OutOfRange: int
            Canonical: int
            NotCanonical: int
            /// Instants with a fraction, and the units drawn.
            Fractional: int
            Units: Set<string>
        }

    let empty =
        { Draws = 0
          Diffs = []
          InRange = 0
          OutOfRange = 0
          Canonical = 0
          NotCanonical = 0
          Fractional = 0
          Units = Set.empty }

    let private diff (label: string) (what: string) (prod: string) (model: string) (t: Tally) : Tally =
        { t with
            Diffs =
                sprintf "%s: %s\n  production: %s\n  the model:  %s" label what prod model
                :: t.Diffs }

    let private same (label: string) (what: string) (prod: string) (model: string) (t: Tally) : Tally =
        if prod = model then t else diff label what prod model t

    let private showPair (o: (bigint * bigint) option) =
        match o with
        | Some(a, b) -> sprintf "Some (%A, %A)" a b
        | None -> "None"

    let private showOpt (o: 'a option) = sprintf "%A" o

    /// A day count, asked of both: its text, that text read back, the range check, and the
    /// calendar both ways. Drawn inside the zone where production's truncating division and the
    /// model's Euclidean one agree (every day from `-865565` on — the shifted dividend is
    /// non-negative there), which holds the whole canonical range with a margin either side.
    let probeDay (label: string) (d: int) (t: Tally) : Tally =
        let t = { t with Draws = t.Draws + 1 }
        let inRange = TemporalText.isDayInRange d

        let t =
            if inRange then
                { t with InRange = t.InRange + 1 }
            else
                { t with OutOfRange = t.OutOfRange + 1 }

        let prodText = TemporalText.dateText d
        let modelText = canonFromChs (Temporal.date_text (bigint d))
        let t = same label "dateText" prodText modelText t

        let t =
            same label "isDayInRange" (string inRange) (string (Temporal.is_day_in_range (bigint d))) t

        let t =
            same
                label
                "tryDays (dateText d)"
                (showOpt (TemporalText.tryDays prodText))
                (showOpt (ofMOpt (Temporal.try_days (canonToChs prodText)) |> Option.map int))
                t

        let c = TemporalText.civilOfDays d
        let mc = Temporal.civil_of_days (bigint d)

        let t =
            same
                label
                "civilOfDays"
                (sprintf "%d-%d-%d" c.Year c.Month c.Day)
                (sprintf "%A-%A-%A" mc.year mc.month mc.day)
                t

        same
            label
            "daysOfCivil (civilOfDays d)"
            (string (TemporalText.daysOfCivil c.Year c.Month c.Day))
            (string (Temporal.days_of_civil (bigint c.Year) (bigint c.Month) (bigint c.Day)))
            t

    /// A date text, asked of both: read to a day, or refused.
    let probeDateText (label: string) (s: string) (t: Tally) : Tally =
        let t = { t with Draws = t.Draws + 1 }
        let prod = TemporalText.tryDays s
        let model = ofMOpt (Temporal.try_days (canonToChs s)) |> Option.map int

        let t =
            if prod.IsSome then
                { t with Canonical = t.Canonical + 1 }
            else
                { t with
                    NotCanonical = t.NotCanonical + 1 }

        same label (sprintf "tryDays %A" s) (showOpt prod) (showOpt model) t

    /// A pair at a unit, asked of both: the range check, and in range its text, the text read back
    /// at the unit, and the coarsest unit of the text.
    let probeInstant (label: string) (u: TimeUnit) (second: float) (fraction: int) (t: Tally) : Tally =
        let mu = ColumnDiff.toModelUnit u

        let t =
            { t with
                Draws = t.Draws + 1
                Units = t.Units.Add(ColumnType.tag (TimestampType u))
                Fractional = t.Fractional + (if fraction <> 0 then 1 else 0) }

        let inRange = TemporalText.isInstantInRange u second fraction

        let t =
            same
                label
                "isInstantInRange"
                (string inRange)
                (string (Temporal.is_instant_in_range mu (bigint (int64 second)) (bigint fraction)))
                t

        if not inRange then
            { t with OutOfRange = t.OutOfRange + 1 }
        else
            let t = { t with InRange = t.InRange + 1 }
            let prodText = TemporalText.instantText u second fraction

            let modelText =
                canonFromChs (Temporal.instant_text mu (bigint (int64 second)) (bigint fraction))

            let t = same label "instantText" prodText modelText t

            let t =
                same
                    label
                    "tryInstant u (instantText u s f)"
                    (showOpt (
                        TemporalText.tryInstant u prodText
                        |> Option.map (fun (s, f) -> bigint (int64 s), bigint f)
                    ))
                    (showPair (ofMOpt (Temporal.try_instant mu (canonToChs prodText))))
                    t

            same
                label
                "unitOf"
                (ColumnType.tag (TimestampType(TemporalText.unitOf prodText)))
                (ColumnType.tag (TimestampType(ColumnDiff.ofModelUnit (Temporal.unit_of (canonToChs prodText)))))
                t

    /// An instant text, asked of both at every unit: read to a pair or refused, canonical or not,
    /// and its coarsest unit.
    let probeInstantText (label: string) (s: string) (t: Tally) : Tally =
        let t = { t with Draws = t.Draws + 1 }
        let canonical = TemporalText.isCanonicalTimestamp s

        let t =
            if canonical then
                { t with Canonical = t.Canonical + 1 }
            else
                { t with
                    NotCanonical = t.NotCanonical + 1 }

        let t =
            same
                label
                (sprintf "isCanonicalTimestamp %A" s)
                (string canonical)
                (string (Temporal.is_canonical_timestamp (canonToChs s)))
                t

        let t =
            same
                label
                (sprintf "unitOf %A" s)
                (ColumnType.tag (TimestampType(TemporalText.unitOf s)))
                (ColumnType.tag (TimestampType(ColumnDiff.ofModelUnit (Temporal.unit_of (canonToChs s)))))
                t

        [ TimeUnit.Seconds
          TimeUnit.Milliseconds
          TimeUnit.Microseconds
          TimeUnit.Nanoseconds ]
        |> List.fold
            (fun t u ->
                same
                    label
                    (sprintf "tryInstant %s %A" (ColumnType.tag (TimestampType u)) s)
                    (showOpt (
                        TemporalText.tryInstant u s
                        |> Option.map (fun (sec, f) -> bigint (int64 sec), bigint f)
                    ))
                    (showPair (ofMOpt (Temporal.try_instant (ColumnDiff.toModelUnit u) (canonToChs s))))
                    t)
            t

    /// Two texts, asked of both: THE order.
    let probeCompare (label: string) (a: string) (b: string) (t: Tally) : Tally =
        let t = { t with Draws = t.Draws + 1 }

        same
            label
            (sprintf "compareInstants %A %A" a b)
            (string (TemporalText.compareInstants a b))
            (string (Temporal.compare_instants (canonToChs a) (canonToChs b)))
            t

    let private units =
        [| TimeUnit.Seconds
           TimeUnit.Milliseconds
           TimeUnit.Microseconds
           TimeUnit.Nanoseconds |]

    /// The drawn pools: days across and beyond the range, texts canonical and corrupted, pairs in
    /// every unit at the range's ends and inside it, instant texts of every fraction length (and
    /// the ill-formed ones: a trailing zero, ten digits, no `Z`), and pairs of texts to order.
    /// The generator's draw reads the seed's HIGH bits: the low bits of this linear congruence cycle
    /// with a period of a few steps, so a `draw 4` followed by three more draws and another `draw 4`
    /// would answer in lockstep — which is how a first draft drew twenty fractional pairs in 1,500.
    let private drawFrom (r: int ref) (n: int) =
        r.Value <- nextCanonSeed r.Value
        (r.Value >>> 9) % n

    let generated (seed: int) (trials: int) : Tally =
        let r = ref seed
        let draw (n: int) = drawFrom r n

        let span = TemporalText.MaxDay - TemporalText.MinDay
        let mutable t = empty

        // the ends and their neighbours, then the drawn days
        for d in
            [ TemporalText.MinDay
              TemporalText.MaxDay
              TemporalText.MinDay - 1
              TemporalText.MaxDay + 1
              0
              -1
              -719468
              -146097 ] do
            t <- probeDay "the fixed days" d t

        for i in 1..trials do
            let d =
                match draw 6 with
                | 0 -> TemporalText.MinDay - 1 - draw 3000
                | 1 -> TemporalText.MaxDay + 1 + draw 3000
                | _ -> TemporalText.MinDay + draw span

            t <- probeDay (sprintf "generated seed=%d day=%d" seed i) d t

        // date texts: a canonical one, and the corruptions
        let dateTexts =
            [ "2026-02-28"
              "2026-02-30"
              "2024-02-29"
              "2023-02-29"
              "0000-01-01"
              "9999-12-31"
              "10000-01-01"
              "999-12-31"
              "2026-2-28"
              "2026/02/28"
              "2026-13-01"
              "2026-00-10"
              "2026-01-00"
              "-026-01-01"
              ""
              "nope" ]

        for s in dateTexts do
            t <- probeDateText "the fixed date texts" s t

        for i in 1..trials do
            let d = TemporalText.MinDay + draw span
            let text = TemporalText.dateText d

            let text =
                match draw 5 with
                | 0 -> text.Substring(0, 9)
                | 1 -> text + "Z"
                | 2 -> text.Replace('-', ':')
                | _ -> text

            t <- probeDateText (sprintf "generated seed=%d date-text=%d" seed i) text t

        // pairs per unit: the ends, inside, and out of range both ways
        for u in units do
            let scale = TimeUnit.scale u

            for second, fraction in
                [ TemporalText.minSecond, 0
                  TemporalText.maxSecond, scale - 1
                  TemporalText.minSecond - 1.0, 0
                  TemporalText.maxSecond + 1.0, 0
                  0.0, scale - 1
                  0.0, scale
                  0.0, -1
                  86399.0, 1 ] do
                t <- probeInstant (sprintf "the fixed pairs %s" (ColumnType.tag (TimestampType u))) u second fraction t

        for i in 1..trials do
            let u = units[draw units.Length]
            let scale = TimeUnit.scale u
            let day = TemporalText.MinDay + draw span
            let second = float day * 86400.0 + float (draw 86400)

            let fraction =
                match draw 4 with
                | 0 -> 0
                | 1 -> draw scale
                | 2 -> (draw 1000) * (scale / 1000)
                | _ -> scale - 1 - draw (min scale 50)

            t <- probeInstant (sprintf "generated seed=%d pair=%d" seed i) u second fraction t

        // instant texts: every fraction length, and the ill-formed shapes
        let instantTexts =
            [ "2026-06-22T17:00:00Z"
              "2026-06-22T17:00:00.5Z"
              "2026-06-22T17:00:00.05Z"
              "2026-06-22T17:00:00.005Z"
              "2026-06-22T17:00:00.0005Z"
              "2026-06-22T17:00:00.123456Z"
              "2026-06-22T17:00:00.1234567Z"
              "2026-06-22T17:00:00.123456789Z"
              "2026-06-22T17:00:00.1234567890Z"
              "2026-06-22T17:00:00.50Z"
              "2026-06-22T17:00:00.0Z"
              "2026-06-22T17:00:00.Z"
              "2026-06-22T17:00:00"
              "2026-06-22T24:00:00Z"
              "2026-06-22T17:60:00Z"
              "2026-06-22T17:00:60Z"
              "2026-06-22 17:00:00Z"
              "2026-02-30T00:00:00Z"
              "0000-01-01T00:00:00Z"
              "9999-12-31T23:59:59.999999999Z"
              "2026-06-22T17:00:00.5"
              "" ]

        for s in instantTexts do
            t <- probeInstantText "the fixed instant texts" s t

        let canonicalDraw () =
            let u = units[draw units.Length]
            let day = TemporalText.MinDay + draw span
            let second = float day * 86400.0 + float (draw 86400)
            let fraction = if draw 3 = 0 then 0 else draw (TimeUnit.scale u)
            TemporalText.instantText u second fraction

        for i in 1..trials do
            let text = canonicalDraw ()

            let text =
                match draw 6 with
                | 0 -> text.Substring(0, text.Length - 1)
                | 1 -> text.Replace("Z", "0Z")
                | 2 -> text.Replace('T', ' ')
                | _ -> text

            t <- probeInstantText (sprintf "generated seed=%d instant-text=%d" seed i) text t

        // the order: canonical pairs, close pairs (one second, one fraction apart), and equal ones
        for i in 1..trials do
            let a = canonicalDraw ()

            let b =
                match draw 4 with
                | 0 -> a
                | 1 ->
                    (match TemporalText.tryInstant TimeUnit.Nanoseconds a with
                     | Some(sec, f) ->
                         let f' = if f = 0 then 1 else f - 1
                         TemporalText.instantText TimeUnit.Nanoseconds sec f'
                     | None -> a)
                | 2 ->
                    (match TemporalText.tryInstant TimeUnit.Nanoseconds a with
                     | Some(sec, f) when sec < TemporalText.maxSecond ->
                         TemporalText.instantText TimeUnit.Nanoseconds (sec + 1.0) f
                     | _ -> a)
                | _ -> canonicalDraw ()

            t <- probeCompare (sprintf "generated seed=%d order=%d" seed i) a b t
            t <- probeCompare (sprintf "generated seed=%d order=%d reversed" seed i) b a t

        t

    /// The go-red: a model asked to read every text AT THE SECONDS UNIT loses on exactly the texts
    /// with a fraction — what `try_instant`'s unit arm is for.
    let secondsOnly (seed: int) (trials: int) : int * int =
        let r = ref seed
        let draw (n: int) = drawFrom r n

        let span = TemporalText.MaxDay - TemporalText.MinDay
        let mutable lost = 0
        let mutable fractional = 0

        for _ in 1..trials do
            let u = units[draw units.Length]
            let day = TemporalText.MinDay + draw span
            let second = float day * 86400.0 + float (draw 86400)
            let scale = TimeUnit.scale u
            let fraction = if scale = 1 || draw 2 = 0 then 0 else 1 + draw (scale - 1)
            let text = TemporalText.instantText u second fraction

            let prod =
                TemporalText.tryInstant u text
                |> Option.map (fun (s, f) -> bigint (int64 s), bigint f)

            let model = ofMOpt (Temporal.try_instant Temporal.Seconds (canonToChs text))

            if fraction <> 0 then
                fractional <- fractional + 1

            if showOpt prod <> showPair model then
                lost <- lost + 1

        lost, fractional

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- Phase 430: the temporal encodings beside Temporal.fst ----

          testCase
              "the temporal oracle agrees with TemporalText over drawn days, texts, instants in every unit and their order"
          <| fun _ ->
              let t = TemporalDiff.generated 4301 1500

              Expect.isEmpty
                  t.Diffs
                  (sprintf "the temporal oracle disagreed with production:\n%s" (renderCanonDiffs t.Diffs))

              // adequacy: both sides of every range and every unit were reached
              Expect.isGreaterThan t.InRange 2000 (sprintf "days and pairs in range (%d)" t.InRange)
              Expect.isGreaterThan t.OutOfRange 300 (sprintf "days and pairs out of range (%d)" t.OutOfRange)
              Expect.isGreaterThan t.Canonical 1000 (sprintf "canonical texts (%d)" t.Canonical)
              Expect.isGreaterThan t.NotCanonical 500 (sprintf "texts that are not canonical (%d)" t.NotCanonical)
              Expect.isGreaterThan t.Fractional 800 (sprintf "pairs with a fraction (%d)" t.Fractional)

              Expect.equal
                  t.Units
                  (Set.ofList [ "timestamp"; "timestamp_ms"; "timestamp_us"; "timestamp_ns" ])
                  "every unit was drawn"

          testCase "a temporal oracle that reads every text at the seconds unit loses — on exactly the fractional draws"
          <| fun _ ->
              let lost, fractional = TemporalDiff.secondsOnly 4302 1200
              Expect.isGreaterThan fractional 400 "the pool holds fractional instants"
              Expect.equal lost fractional "the seconds-only reading lost on the fractional draws, and on no other" ]
