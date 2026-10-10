module Fuaran.Core.Tests.UnitTests

// Phase 426 — a unit is a value: `Fuaran.Core.Unit`.
//
// Five things are held here. (1) The atom table in `docs/units.md` IS the vocabulary: every row
// parses as itself, renders as itself, carries the metric flag the code holds, and converts to its
// stated definition by exactly the stated factor — and the code admits no atom the table does not
// list. (2) The prefixes: every prefix on every metric atom round-trips and scales by its power of
// ten, and a prefix on a non-metric atom is refused. (3) The algebra laws over a drawn pool —
// associativity, commutativity, identity, inverses, powers — and parse/render round-trip over the
// same pool. (4) Conversion: `km/h` to `m/s` is `5/18`, factors compose, and a currency is
// compatible with nothing but itself under a prefix. (5) Every refusal class is reached, with the
// token and the position it names.

open System
open System.IO
open Expecto
open Fuaran.Core

let private ok (text: string) : UnitOfMeasure =
    match Unit.parse text with
    | Ok u -> u
    | Error e -> failwithf "expected %s to parse, refused %A" text e

/// A ratio as `(numerator, denominator)` in lowest terms, from `n` or `n/d` text.
let private ratioOf (text: string) : bigint * bigint =
    let n, d =
        match text.Split '/' with
        | [| n |] -> bigint.Parse n, 1I
        | [| n; d |] -> bigint.Parse n, bigint.Parse d
        | _ -> failwithf "not a ratio: %s" text

    let g = bigint.GreatestCommonDivisor(n, d)
    n / g, d / g

let private pair (r: Ratio) = r.Numerator, r.Denominator

let private factor (a: UnitOfMeasure) (b: UnitOfMeasure) : bigint * bigint =
    match Unit.conversionFactor a b with
    | Ok r -> pair r
    | Error e -> failwithf "expected %O and %O to be compatible, refused %A" a b e

let private times (a: bigint, b: bigint) (c: bigint, d: bigint) =
    let n, m = a * c, b * d
    let g = bigint.GreatestCommonDivisor(n, m)
    n / g, m / g

/// One row of the atom table: symbol, metric, and the definition `<factor> <unit>`.
type private AtomRow =
    { Symbol: string
      Metric: bool
      Factor: string
      Of: string }

/// The rows of `docs/units.md`'s "The admitted atoms" table.
let private atomRows: Lazy<AtomRow list> =
    lazy
        let lines = File.ReadAllLines(Snapshots.repoFile "docs/units.md") |> List.ofArray

        let section =
            lines
            |> List.skipWhile (fun l -> l <> "### The admitted atoms")
            |> List.skip 1
            |> List.takeWhile (fun l -> not (l.StartsWith "#"))

        let unquote (cell: string) =
            let c = cell.Trim()

            if c.StartsWith "`" && c.EndsWith "`" then
                c.Substring(1, c.Length - 2)
            else
                failwithf "expected a code cell, found %s" c

        section
        |> List.filter (fun l -> l.StartsWith "| `")
        |> List.map (fun l ->
            match l.Split '|' |> Array.map _.Trim() with
            | [| ""; sym; _name; metric; equals; "" |] ->
                let def = unquote equals
                let space = def.IndexOf ' '

                { Symbol = unquote sym
                  Metric = (metric = "yes")
                  Factor = def.Substring(0, space)
                  Of = def.Substring(space + 1) }
            | cells -> failwithf "an atom row with %d cells: %s" cells.Length l)

/// The UCUM prefixes and their powers of ten, as `docs/units.md` states them. The prefix tests
/// below hold the code to every one by its conversion factor, so a wrong power is red by name.
let private prefixes: (string * int) list =
    [ "Y", 24
      "Z", 21
      "E", 18
      "P", 15
      "T", 12
      "G", 9
      "M", 6
      "k", 3
      "h", 2
      "da", 1
      "d", -1
      "c", -2
      "m", -3
      "u", -6
      "n", -9
      "p", -12
      "f", -15
      "a", -18
      "z", -21
      "y", -24 ]

/// The atoms the SOURCE admits, read from `Unit.fs`'s vocabulary table as `(symbol, metric)`: the
/// other direction of "the table is the vocabulary", read from the code rather than through an
/// internals grant (a grant is a counted friend of the package).
let private sourceAtoms: Lazy<(string * bool) list> =
    lazy
        let row =
            Text.RegularExpressions.Regex(@"^\s+""(?<sym>[^""]+)"", def (?<metric>true|false) ")

        File.ReadAllLines(Snapshots.repoFile "src/Fuaran.Core.Unit/Unit.fs")
        |> Array.choose (fun l ->
            let m = row.Match l

            if m.Success then
                Some(m.Groups["sym"].Value, m.Groups["metric"].Value = "true")
            else
                None)
        |> List.ofArray

let private isAtom (text: string) =
    atomRows.Value |> List.exists (fun r -> r.Symbol = text)

/// Drawn units: products of one to three factors over the admitted atoms, three currencies and the
/// prefixes, exponents in -3..3. Seeded, so a red names a unit a rerun reproduces.
let private drawn: Lazy<UnitOfMeasure list> =
    lazy
        let rng = Random(426)

        let atoms =
            (atomRows.Value |> List.map (fun r -> r.Symbol, r.Metric))
            @ [ "[GBP]", true; "[USD]", true; "[JPY]", true ]
            |> Array.ofList

        let one () =
            let sym, metric = atoms[rng.Next atoms.Length]

            let p =
                if metric && rng.Next 3 = 0 then
                    fst prefixes[rng.Next prefixes.Length]
                else
                    ""

            let text = p + sym
            // `Pa` as prefix + atom would be the pascal, and `cd` the candela: a drawn text the
            // parser reads as another atom is redrawn as the bare atom.
            let u = ok text

            let u = if Unit.render u = text then u else ok sym

            Unit.pow u (rng.Next 7 - 3)

        [ for _ in 1..300 ->
              let k = 1 + rng.Next 3
              List.init k (fun _ -> one ()) |> List.fold Unit.mul Unit.dimensionless ]

let private refused (text: string) : UnitRefusal =
    match Unit.parse text with
    | Ok u -> failwithf "expected %s to be refused, it parsed to %O" text u
    | Error e -> e

[<Tests>]
let tests =
    testList
        "Unit"
        [ testList
              "the atom table is the vocabulary"
              [ test "the table lists every admitted atom, and no other" {
                    let documented = atomRows.Value |> List.map _.Symbol |> Set.ofList
                    let held = sourceAtoms.Value |> List.map fst |> Set.ofList
                    Expect.isGreaterThan documented.Count 40 "the table was read"
                    Expect.equal documented held "docs/units.md's atom table and the code's vocabulary"
                }

                test "every row parses as itself, renders as itself, and carries the code's metric flag" {
                    for row in atomRows.Value do
                        let u = ok row.Symbol
                        Expect.equal (Unit.render u) row.Symbol (sprintf "%s renders as itself" row.Symbol)
                        Expect.equal (Unit.parse (Unit.render u)) (Ok u) (sprintf "%s round-trips" row.Symbol)

                        Expect.equal
                            (sourceAtoms.Value |> List.find (fun (s, _) -> s = row.Symbol) |> snd)
                            row.Metric
                            (sprintf "%s's metric flag in the source" row.Symbol)

                        // And in behaviour: a prefix is read on a metric atom and refused on another.
                        match Unit.parse ("k" + row.Symbol), row.Metric with
                        | Ok _, true -> ()
                        | Error(UnitRefusal.PrefixNotAllowed _), false -> ()
                        | other, metric -> failwithf "k%s (metric %b): %A" row.Symbol metric other
                }

                test "every row converts to its stated definition by exactly the stated factor" {
                    for row in atomRows.Value do
                        let u = ok row.Symbol
                        let target = ok row.Of
                        Expect.isTrue (Unit.compatible u target) (sprintf "%s is compatible with %s" row.Symbol row.Of)

                        Expect.equal
                            (factor u target)
                            (ratioOf row.Factor)
                            (sprintf "1 %s = %s %s" row.Symbol row.Factor row.Of)
                }

                test "the UCUM spelling `l` reads as the litre and renders `L`" {
                    Expect.equal (ok "l") (ok "L") "one atom"
                    Expect.equal (Unit.render (ok "ml")) "mL" "the canonical symbol is written"
                } ]

          testList
              "prefixes"
              [ test "every prefix on every metric atom round-trips and scales by its power of ten" {
                    for row in atomRows.Value do
                        let sym = row.Symbol

                        if row.Metric then
                            for (p, power) in prefixes do
                                let text = p + sym
                                let u = ok text
                                Expect.equal (Unit.render u) text (sprintf "%s renders as written" text)

                                let expected =
                                    if power >= 0 then
                                        bigint.Pow(10I, power), 1I
                                    else
                                        1I, bigint.Pow(10I, -power)

                                Expect.equal (factor u (ok sym)) expected (sprintf "%s is 10^%d %s" text power sym)
                }

                test "a prefix on a non-metric atom is refused as PrefixNotAllowed, naming the whole token" {
                    for row in atomRows.Value do
                        let sym = row.Symbol

                        if not row.Metric then
                            for (p, _) in prefixes do
                                let text = p + sym
                                // `Pa` is the pascal and `cd` the candela: an admitted atom first.
                                if not (isAtom text) then
                                    Expect.equal
                                        (refused text)
                                        (UnitRefusal.PrefixNotAllowed(text, 0))
                                        (sprintf "%s is refused" text)
                }

                test "a token is a whole atom before it is a prefix and an atom" {
                    Expect.equal (Unit.render (ok "Pa")) "Pa" "the pascal"
                    Expect.equal (factor (ok "Pa") (ok "N/m2")) (1I, 1I) "the pascal is N/m2"
                    Expect.equal (factor (ok "cd") (ok "lm/sr")) (1I, 1I) "the candela"
                    Expect.equal (factor (ok "a") (ok "d")) (1461I, 4I) "the year"
                    Expect.equal (factor (ok "am") (ok "m")) (1I, bigint.Pow(10I, 18)) "the attometre"
                    Expect.equal (factor (ok "dam") (ok "m")) (10I, 1I) "`da` is read before `d`"
                } ]

          testList
              "the canonical text"
              [ test "one text per unit: factors ordered by symbol then prefix, denominators after" {
                    let cases =
                        [ "m.kg/s2", "kg.m/s2"
                          "s-1", "/s"
                          "1/s", "/s"
                          "1", "1"
                          "(kg.m)/s2", "kg.m/s2"
                          "kg/(m.s2)", "kg/m/s2"
                          "kg/m/s2", "kg/m/s2"
                          "m.m", "m2"
                          "m/m", "1"
                          "m0", "1"
                          "m+2", "m2"
                          "h.kW", "kW.h"
                          "m.km", "m.km"
                          "N.m", "N.m"
                          "/(/s)", "s"
                          "mg/dL", "mg/dL"
                          "[GBP]/h", "[GBP]/h"
                          "c[GBP]", "c[GBP]"
                          "/min", "/min"
                          "s-2.m-1", "/m/s2" ]

                    for (written, canonical) in cases do
                        let u = ok written
                        Expect.equal (Unit.render u) canonical (sprintf "%s renders %s" written canonical)
                        Expect.equal (Unit.parse canonical) (Ok u) (sprintf "%s reads back" canonical)
                }

                test "parse and render round-trip over the drawn pool" {
                    for u in drawn.Value do
                        let text = Unit.render u
                        Expect.equal (Unit.parse text) (Ok u) (sprintf "%s round-trips" text)
                        Expect.equal (string u) text "UnitOfMeasure.ToString is Unit.render"
                } ]

          testList
              "the algebra laws over the drawn pool"
              [ test "associativity, commutativity and identity of mul" {
                    let pool = drawn.Value |> Array.ofList

                    for i in 0 .. pool.Length - 3 do
                        let a, b, c = pool[i], pool[i + 1], pool[i + 2]
                        Expect.equal (Unit.mul (Unit.mul a b) c) (Unit.mul a (Unit.mul b c)) "associative"
                        Expect.equal (Unit.mul a b) (Unit.mul b a) "commutative"
                        Expect.equal (Unit.mul a Unit.dimensionless) a "right identity"
                        Expect.equal (Unit.mul Unit.dimensionless a) a "left identity"
                }

                test "inverses: div and pow -1" {
                    for a in drawn.Value do
                        let inv = Unit.div Unit.dimensionless a
                        Expect.equal (Unit.mul a inv) Unit.dimensionless "a · a⁻¹ = 1"
                        Expect.equal (Unit.div a a) Unit.dimensionless "a / a = 1"
                        Expect.equal (Unit.pow a -1) inv "pow a -1 is the inverse"
                        Expect.equal (Unit.div inv inv) Unit.dimensionless "the inverse has an inverse"
                        Expect.equal (Unit.div Unit.dimensionless inv) a "the inverse of the inverse"
                }

                test "pow is repeated mul, distributes over mul, and pow 0 is dimensionless" {
                    let pool = drawn.Value |> Array.ofList

                    for i in 0 .. pool.Length - 2 do
                        let a, b = pool[i], pool[i + 1]
                        Expect.equal (Unit.pow a 0) Unit.dimensionless "pow 0"
                        Expect.equal (Unit.pow a 1) a "pow 1"
                        Expect.equal (Unit.pow a 3) (Unit.mul a (Unit.mul a a)) "pow 3"

                        Expect.equal
                            (Unit.pow (Unit.mul a b) -2)
                            (Unit.mul (Unit.pow a -2) (Unit.pow b -2))
                            "distributes"

                        Expect.equal (Unit.pow (Unit.pow a 2) 3) (Unit.pow a 6) "powers compose"
                }

                test "compatibility is an equivalence that mul respects" {
                    let pool = drawn.Value |> Array.ofList

                    for i in 0 .. pool.Length - 2 do
                        let a, b = pool[i], pool[i + 1]
                        Expect.isTrue (Unit.compatible a a) "reflexive"
                        Expect.equal (Unit.compatible a b) (Unit.compatible b a) "symmetric"
                        // A prefix changes the scale, never the dimension.
                        let scaled = Unit.mul a (ok "km/m")
                        Expect.isTrue (Unit.compatible a scaled) "a scaled unit is compatible"
                        Expect.equal (factor scaled a) (1000I, 1I) "by exactly the prefix"
                        Expect.isTrue (Unit.compatible (Unit.mul a b) (Unit.mul scaled b)) "mul respects it"
                }

                test "an exponent leaving the 32-bit bound raises rather than wraps" {
                    let big = Unit.pow (ok "m") Int32.MaxValue
                    Expect.equal (Unit.render big) "m2147483647" "the bound itself is a unit"

                    Expect.throwsT<ArgumentException> (fun () -> Unit.mul big (ok "m") |> ignore) "mul"
                    Expect.throwsT<ArgumentException> (fun () -> Unit.div (Unit.pow big -1) (ok "m") |> ignore) "div"
                    Expect.throwsT<ArgumentException> (fun () -> Unit.pow (ok "m2") Int32.MaxValue |> ignore) "pow"
                } ]

          testList
              "conversion"
              [ test "km/h and m/s are compatible, with the factor 1/3.6" {
                    let kmh, ms = ok "km/h", ok "m/s"
                    Expect.isTrue (Unit.compatible kmh ms) "compatible"
                    Expect.notEqual kmh ms "two units, not one"
                    Expect.equal (factor kmh ms) (5I, 18I) "1/3.6 = 5/18"
                    Expect.equal (factor ms kmh) (18I, 5I) "and back"

                    match Unit.conversionFactor kmh ms with
                    | Ok r -> Expect.equal (string r) "5/18" "the ratio's text"
                    | Error e -> failwithf "%A" e
                }

                test "factors are exact, compose, and invert" {
                    let speeds =
                        [ "km/h"; "m/s"; "[mi_i]/h"; "[ft_i]/min"; "cm/s"; "[in_i]/wk"; "Mm/a" ]
                        |> List.map ok

                    for a in speeds do
                        Expect.equal (factor a a) (1I, 1I) "a to itself is 1"

                        for b in speeds do
                            Expect.equal (times (factor a b) (factor b a)) (1I, 1I) "a factor and its inverse"

                            for c in speeds do
                                Expect.equal (factor a c) (times (factor a b) (factor b c)) "factors compose"
                }

                test "factors are multiplicative over mul" {
                    let pairs = [ "km", "m"; "h", "s"; "[lb_av]", "g"; "k[GBP]", "[GBP]"; "L", "cm3" ]

                    for (a, a') in pairs do
                        for (b, b') in pairs do
                            let lhs = factor (Unit.mul (ok a) (ok b)) (Unit.mul (ok a') (ok b'))
                            Expect.equal lhs (times (factor (ok a) (ok a')) (factor (ok b) (ok b'))) (a + "·" + b)
                }

                test "metres and feet convert; pence and pounds convert; N and kg.m/s2 differ by nothing" {
                    Expect.equal (factor (ok "[ft_i]") (ok "m")) (381I, 1250I) "0.3048"
                    Expect.equal (factor (ok "c[GBP]") (ok "[GBP]")) (1I, 100I) "a penny"
                    Expect.equal (factor (ok "N") (ok "kg.m/s2")) (1I, 1I) "the newton"
                    Expect.notEqual (ok "N") (ok "kg.m/s2") "one denotation, two units"
                    Expect.equal (factor (ok "mL") (ok "cm3")) (1I, 1I) "the millilitre"
                    Expect.equal (factor (ok "%") (ok "1")) (1I, 100I) "percent"
                }

                test "a currency is incompatible with every other dimension and every other currency" {
                    let gbp = ok "[GBP]"

                    for row in atomRows.Value do
                        let u = ok row.Symbol
                        Expect.isFalse (Unit.compatible gbp u) (sprintf "[GBP] and %s" row.Symbol)

                        Expect.equal
                            (Unit.conversionFactor gbp u)
                            (Error(UnitConversionRefusal.Incompatible(gbp, u)))
                            "refused, naming both units"

                    for other in [ "[USD]"; "[EUR]"; "[JPY]"; "k[USD]"; "[GBP]/[USD]"; "[GBP]2"; "1" ] do
                        Expect.isFalse (Unit.compatible gbp (ok other)) (sprintf "[GBP] and %s" other)

                    Expect.isTrue (Unit.compatible gbp (ok "k[GBP]")) "a prefix keeps the currency"
                    Expect.equal (factor (ok "k[GBP]") gbp) (1000I, 1I) "a thousand pounds"
                    Expect.equal (factor (ok "[GBP]/h") (ok "[GBP]/min")) (1I, 60I) "a rate per hour"
                }

                test "incompatible units are refused, naming both" {
                    let m, s = ok "m", ok "s"
                    Expect.equal (Unit.conversionFactor m s) (Error(UnitConversionRefusal.Incompatible(m, s))) "m and s"
                    Expect.isFalse (Unit.compatible (ok "mol") (ok "1")) "the mole is a dimension here"
                } ]

          testList
              "every refusal class is reached"
              [ test "Empty" {
                    Expect.equal (refused "") UnitRefusal.Empty "empty"
                    Expect.equal (refused null) UnitRefusal.Empty "null"
                }

                test "Malformed names what stood there, where, and what was wanted" {
                    let cases =
                        [ "m.", "", 2
                          "m^2", "^", 1
                          "m s", " ", 1
                          " m", " ", 0
                          "m-", "", 2
                          "m+", "", 2
                          "(m", "", 2
                          "m)", ")", 1
                          "//s", "/", 1
                          "m..s", ".", 2
                          "(m.s)2", "2", 5
                          "m99999999999", "99999999999", 1
                          "m-2147483648", "-2147483648", 1
                          "[GBP", "[GBP", 0
                          "m.{x", "{x", 2
                          "m2147483647.m", "m", 12 ]

                    for (text, found, at) in cases do
                        match refused text with
                        | UnitRefusal.Malformed(f, p, expected) ->
                            Expect.equal (f, p) (found, at) (sprintf "%s: what and where" text)
                            Expect.isNonEmpty expected (sprintf "%s: what was wanted" text)
                        | other -> failwithf "%s: expected Malformed, got %A" text other
                }

                test "UnknownAtom" {
                    Expect.equal (refused "furlong") (UnitRefusal.UnknownAtom("furlong", 0)) "unknown"
                    Expect.equal (refused "m.foo") (UnitRefusal.UnknownAtom("foo", 2)) "after a product"
                    Expect.equal (refused "deg") (UnitRefusal.UnknownAtom("deg", 0)) "irrational factor"
                    Expect.equal (refused "[degR]") (UnitRefusal.UnknownAtom("[degR]", 0)) "not in the subset"
                    Expect.equal (refused "[gbp]") (UnitRefusal.UnknownAtom("[gbp]", 0)) "a code is capitals"
                }

                test "Annotation" {
                    Expect.equal (refused "{beats}/min") (UnitRefusal.Annotation("{beats}", 0)) "alone"
                    Expect.equal (refused "m{tall}") (UnitRefusal.Annotation("{tall}", 1)) "after an atom"
                    Expect.equal (refused "(m){x}") (UnitRefusal.Annotation("{x}", 3)) "after a group"
                }

                test "ArbitraryUnit" {
                    Expect.equal (refused "[IU]") (UnitRefusal.ArbitraryUnit("[IU]", 0)) "international unit"
                    Expect.equal (refused "k[IU]") (UnitRefusal.ArbitraryUnit("k[IU]", 0)) "prefixed"
                    Expect.equal (refused "[CFU]/mL") (UnitRefusal.ArbitraryUnit("[CFU]", 0)) "a currency's shape"
                }

                test "NonRatioUnit" {
                    Expect.equal (refused "Cel") (UnitRefusal.NonRatioUnit("Cel", 0)) "Celsius"
                    Expect.equal (refused "[degF]") (UnitRefusal.NonRatioUnit("[degF]", 0)) "Fahrenheit"
                    Expect.equal (refused "dB") (UnitRefusal.NonRatioUnit("dB", 0)) "a prefixed logarithm"
                    Expect.equal (refused "K/Cel") (UnitRefusal.NonRatioUnit("Cel", 2)) "in a quotient"
                    Expect.equal (refused "B[10.nV]") (UnitRefusal.NonRatioUnit("B[10.nV]", 0)) "a bracketed point"
                }

                test "PrefixNotAllowed" {
                    Expect.equal (refused "kmin") (UnitRefusal.PrefixNotAllowed("kmin", 0)) "minute"
                    Expect.equal (refused "k%") (UnitRefusal.PrefixNotAllowed("k%", 0)) "percent"
                    Expect.equal (refused "m/M[ft_i]") (UnitRefusal.PrefixNotAllowed("M[ft_i]", 2)) "customary"
                }

                test "NumericFactor" {
                    Expect.equal (refused "10*3/L") (UnitRefusal.NumericFactor("10*3", 0)) "a power of ten"
                    Expect.equal (refused "1000.m") (UnitRefusal.NumericFactor("1000", 0)) "a number"
                    Expect.equal (refused "m/10^-2") (UnitRefusal.NumericFactor("10^-2", 2)) "after a quotient"
                } ]

          testList
              "totality"
              [ test "parse answers every drawn string over the grammar's alphabet, and never throws" {
                    let rng = Random(4260)
                    let alphabet = "mkgsLh%[]{}()./+-0123456789 GBPdaCel*^_'"

                    for _ in 1..20000 do
                        let text =
                            String(Array.init (rng.Next 12) (fun _ -> alphabet[rng.Next alphabet.Length]))

                        match Unit.parse text with
                        | Ok u -> Expect.equal (Unit.parse (Unit.render u)) (Ok u) (sprintf "%s round-trips" text)
                        | Error _ -> ()
                }

                test "nesting depth never reaches the machine stack" {
                    let depth = 200000
                    let text = String('(', depth) + "m" + String(')', depth)
                    Expect.equal (Unit.parse text) (Ok(ok "m")) "deep parentheses"
                    let unclosed = String('(', depth) + "m"

                    match Unit.parse unclosed with
                    | Error(UnitRefusal.Malformed("", p, _)) -> Expect.equal p (depth + 1) "at the end"
                    | other -> failwithf "expected Malformed, got %A" other
                } ] ]
