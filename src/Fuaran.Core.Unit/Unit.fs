namespace Fuaran.Core

open System

/// An exact positive rational, always in lowest terms (Phase 426): the scale factor between two
/// compatible units. A float would be wrong by an ulp on one host and right on another, and a
/// conversion that composes (`km` to `m` to `mm`) would drift; a rational over arbitrary-precision
/// integers is exact on every host. Equal ratios are one numerator and one denominator.
[<NoComparison>]
type Ratio =
    internal
        { Num: bigint
          Den: bigint }

    /// The numerator, positive, coprime with `Denominator`.
    member r.Numerator: bigint = r.Num

    /// The denominator, positive, coprime with `Numerator`; `1` for a whole number.
    member r.Denominator: bigint = r.Den

    /// `n` for a whole number, else `n/d`, in ASCII digits.
    override r.ToString() =
        if r.Den = 1I then
            r.Num.ToString()
        else
            r.Num.ToString() + "/" + r.Den.ToString()

/// One factor of a unit: an atom (an admitted UCUM symbol, or a currency written `[GBP]`) under an
/// optional SI prefix. Ordered by symbol, then prefix, ordinally — the canonical factor order.
type internal UnitAtom = { Symbol: string; Prefix: string }

/// A unit as a value (Phase 426): a product of atoms, each under an optional SI prefix, with
/// non-zero integer exponents, held in ONE canonical form — factors ordered by symbol then prefix,
/// equal atoms merged, zero exponents dropped. Two units are equal exactly when they are one
/// product: `km/h` and `m/s` are different units, COMPATIBLE (one dimension) with an exact
/// `conversionFactor` between them. What a unit denotes — a scale factor times a product of base
/// dimensions — is what `compatible` and `conversionFactor` read; the product is what it is.
/// `Unit.parse` builds one, `Unit.render` writes its one canonical text. `docs/units.md` is the
/// design note.
type UnitOfMeasure =
    internal
        { Factors: (UnitAtom * int) list }

    /// The canonical text, as `Unit.render` writes it.
    override u.ToString() =
        let atomText (a: UnitAtom, e: int) =
            let e = abs e
            a.Prefix + a.Symbol + (if e = 1 then "" else string e)

        let num = u.Factors |> List.filter (fun (_, e) -> e > 0) |> List.map atomText
        let den = u.Factors |> List.filter (fun (_, e) -> e < 0) |> List.map atomText

        match num, den with
        | [], [] -> "1"
        | _ -> String.Join(".", num) + String.Join("", den |> List.map (fun t -> "/" + t))

/// Why a text is not a unit (Phase 426). Every case but `Empty` names the offending token as written
/// and its POSITION: the zero-based index of the token's first character in the text parsed.
[<RequireQualifiedAccess>]
type UnitRefusal =
    /// The text is empty, or null.
    | Empty
    /// The text leaves the grammar at `position`: `found` is what stands there (the empty string at
    /// the end of the text), and `expected` names what the grammar wanted instead.
    | Malformed of found: string * position: int * expected: string
    /// A symbol the admitted vocabulary does not hold, with or without a prefix.
    | UnknownAtom of token: string * position: int
    /// A UCUM annotation, `{...}`: text with no algebraic meaning, refused so that one unit keeps one
    /// text.
    | Annotation of token: string * position: int
    /// A UCUM arbitrary unit (`[IU]`, `[arb'U]`, `[CFU]`, …): defined by a procedure, not by a
    /// dimension, so no factor relates it to anything.
    | ArbitraryUnit of token: string * position: int
    /// A UCUM special unit whose conversion is not a ratio: affine temperatures (`Cel`, `[degF]`) and
    /// logarithmic units (`B`, `Np`, `[pH]`, …), prefixed or not.
    | NonRatioUnit of token: string * position: int
    /// An SI prefix written on an atom that takes none (`kmin`, `k%`).
    | PrefixNotAllowed of token: string * position: int
    /// A numeric factor other than `1` (`10*3`, `1000`): the subset carries scale in its atoms and
    /// prefixes, never as a free number.
    | NumericFactor of token: string * position: int

/// Why two units have no conversion factor (Phase 426).
[<RequireQualifiedAccess>]
type UnitConversionRefusal =
    /// The two units measure different dimensions — every currency is a dimension of its own, so two
    /// currencies are incompatible too. `source` and `target` are the units as given.
    | Incompatible of source: UnitOfMeasure * target: UnitOfMeasure

/// The vocabulary the parser admits, and the refused symbols it names by class. Internal: the
/// design note (`docs/units.md`) is its public statement, and the tests hold the two together.
module internal UnitVocabulary =

    /// An admitted atom: whether SI prefixes apply, its scale relative to the coherent base
    /// (metre, GRAM, second, ampere, kelvin, mole, candela) as `num/den`, and its exponents over
    /// `[length; mass; time; current; temperature; amount; luminous intensity]`.
    type AtomDef =
        { Metric: bool
          Num: bigint
          Den: bigint
          Dims: int list }

    let private def metric (num: bigint) (den: bigint) (dims: int list) =
        { Metric = metric
          Num = num
          Den = den
          Dims = dims }

    let private d l m t i th n j = [ l; m; t; i; th; n; j ]
    let private none = d 0 0 0 0 0 0 0

    /// Every admitted atom by its canonical symbol.
    let atoms: Map<string, AtomDef> =
        Map.ofList
            [ // The seven SI base dimensions; mass at the gram, UCUM's base.
              "m", def true 1I 1I (d 1 0 0 0 0 0 0)
              "g", def true 1I 1I (d 0 1 0 0 0 0 0)
              "s", def true 1I 1I (d 0 0 1 0 0 0 0)
              "A", def true 1I 1I (d 0 0 0 1 0 0 0)
              "K", def true 1I 1I (d 0 0 0 0 1 0 0)
              "mol", def true 1I 1I (d 0 0 0 0 0 1 0)
              "cd", def true 1I 1I (d 0 0 0 0 0 0 1)
              // The SI coherent derived units (degree Celsius excepted: it is affine).
              "rad", def true 1I 1I none
              "sr", def true 1I 1I none
              "Hz", def true 1I 1I (d 0 0 -1 0 0 0 0)
              "N", def true 1000I 1I (d 1 1 -2 0 0 0 0)
              "Pa", def true 1000I 1I (d -1 1 -2 0 0 0 0)
              "J", def true 1000I 1I (d 2 1 -2 0 0 0 0)
              "W", def true 1000I 1I (d 2 1 -3 0 0 0 0)
              "C", def true 1I 1I (d 0 0 1 1 0 0 0)
              "V", def true 1000I 1I (d 2 1 -3 -1 0 0 0)
              "F", def true 1I 1000I (d -2 -1 4 2 0 0 0)
              "Ohm", def true 1000I 1I (d 2 1 -3 -2 0 0 0)
              "S", def true 1I 1000I (d -2 -1 3 2 0 0 0)
              "Wb", def true 1000I 1I (d 2 1 -2 -1 0 0 0)
              "T", def true 1000I 1I (d 0 1 -2 -1 0 0 0)
              "H", def true 1000I 1I (d 2 1 -2 -2 0 0 0)
              "lm", def true 1I 1I (d 0 0 0 0 0 0 1)
              "lx", def true 1I 1I (d -2 0 0 0 0 0 1)
              "Bq", def true 1I 1I (d 0 0 -1 0 0 0 0)
              "Gy", def true 1I 1I (d 2 0 -2 0 0 0 0)
              "Sv", def true 1I 1I (d 2 0 -2 0 0 0 0)
              "kat", def true 1I 1I (d 0 0 -1 0 0 1 0)
              // Metric non-SI units.
              "L", def true 1I 1000I (d 3 0 0 0 0 0 0)
              "t", def true 1000000I 1I (d 0 1 0 0 0 0 0)
              "bar", def true 100000000I 1I (d -1 1 -2 0 0 0 0)
              // Non-metric units: time, ratios, and the international customary set.
              "min", def false 60I 1I (d 0 0 1 0 0 0 0)
              "h", def false 3600I 1I (d 0 0 1 0 0 0 0)
              "d", def false 86400I 1I (d 0 0 1 0 0 0 0)
              "wk", def false 604800I 1I (d 0 0 1 0 0 0 0)
              "a", def false 31557600I 1I (d 0 0 1 0 0 0 0)
              "%", def false 1I 100I none
              "[ppm]", def false 1I 1000000I none
              "[ppb]", def false 1I 1000000000I none
              "[in_i]", def false 127I 5000I (d 1 0 0 0 0 0 0)
              "[ft_i]", def false 381I 1250I (d 1 0 0 0 0 0 0)
              "[yd_i]", def false 1143I 1250I (d 1 0 0 0 0 0 0)
              "[mi_i]", def false 201168I 125I (d 1 0 0 0 0 0 0)
              "[lb_av]", def false 45359237I 100000I (d 0 1 0 0 0 0 0)
              "[oz_av]", def false 45359237I 1600000I (d 0 1 0 0 0 0 0) ]

    /// Spellings read as another atom: UCUM's lower-case litre. Rendered as the canonical symbol.
    let aliases: Map<string, string> = Map.ofList [ "l", "L" ]

    /// UCUM's twenty prefixes and their powers of ten, `da` first so that `dam` reads as decametre.
    let prefixes: (string * int) list =
        [ "da", 1
          "Y", 24
          "Z", 21
          "E", 18
          "P", 15
          "T", 12
          "G", 9
          "M", 6
          "k", 3
          "h", 2
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

    /// UCUM special units whose conversion is not a ratio: affine temperatures and logarithms.
    let nonRatio: Set<string> =
        Set.ofList
            [ "Cel"
              "[degF]"
              "[degRe]"
              "[pH]"
              "Np"
              "B"
              "B[SPL]"
              "B[V]"
              "B[mV]"
              "B[uV]"
              "B[10.nV]"
              "B[W]"
              "B[kW]"
              "bit_s"
              "[p'diop]"
              "%[slope]"
              "[hp_X]"
              "[hp_C]"
              "[hp_M]"
              "[hp_Q]"
              "[kp_X]"
              "[kp_C]"
              "[kp_M]"
              "[kp_Q]" ]

    /// UCUM's arbitrary units. Several have a currency's shape (`[CFU]`), so this set is read
    /// before the currency rule.
    let arbitrary: Set<string> =
        Set.ofList
            [ "[iU]"
              "[IU]"
              "[arb'U]"
              "[USP'U]"
              "[GPL'U]"
              "[MPL'U]"
              "[APL'U]"
              "[beth'U]"
              "[anti'Xa'U]"
              "[todd'U]"
              "[dye'U]"
              "[smgy'U]"
              "[bdsk'U]"
              "[ka'U]"
              "[knk'U]"
              "[mclg'U]"
              "[tb'U]"
              "[CCID_50]"
              "[TCID_50]"
              "[EID_50]"
              "[PFU]"
              "[FFU]"
              "[CFU]"
              "[IR]"
              "[BAU]"
              "[AU]"
              "[Amb'a'1'U]"
              "[PNU]"
              "[Lf]"
              "[D'ag'U]"
              "[FEU]"
              "[ELU]"
              "[EU]" ]

    /// An ISO 4217 alphabetic code in brackets: `[` three ASCII capitals `]`, and not one of UCUM's
    /// arbitrary units. The SHAPE is checked, not membership of the current code list, which moves
    /// on a schedule Core's releases do not follow; a retired code still labels the data written in
    /// it.
    let isCurrency (tok: string) : bool =
        tok.Length = 5
        && tok[0] = '['
        && tok[4] = ']'
        && (tok[1] >= 'A' && tok[1] <= 'Z')
        && (tok[2] >= 'A' && tok[2] <= 'Z')
        && (tok[3] >= 'A' && tok[3] <= 'Z')
        && not (arbitrary.Contains tok)

    /// The canonical symbol of an admitted atom, through the aliases.
    let tryAtom (tok: string) : string option =
        let canonical = aliases |> Map.tryFind tok |> Option.defaultValue tok
        if atoms.ContainsKey canonical then Some canonical else None

    /// The power of ten a prefix stands for.
    let prefixPower (p: string) : int =
        if p = "" then
            0
        else
            prefixes |> List.find (fun (q, _) -> q = p) |> snd

/// A unit's dimension: base-dimension exponents and one dimension per currency.
type internal DimKey =
    | Base of int
    | Currency of string

/// The unit algebra (Phase 426): parse, render, `mul`/`div`/`pow`, `compatible`, and the exact
/// `conversionFactor` between compatible units. Pure and total over its inputs, FSharp.Core only,
/// no host culture.
///
/// EXPONENTS are 32-bit integers in `±2147483647`. `parse` refuses a written exponent outside that
/// range; `mul`, `div` and `pow` raise `System.ArgumentException` (`invalidArg`, which every host
/// pipeline carries) where a result exponent would leave it, rather than wrap — no data carries such a unit, and a wrapped exponent is a wrong unit.
[<RequireQualifiedAccess>]
module Unit =

    let private maxExp = int64 Int32.MaxValue

    let private checkedExp (e: int64) : int =
        if e > maxExp || e < -maxExp then
            invalidArg "n" "a unit exponent left the range ±2147483647"
        else
            int e

    /// Merge two factor lists, the second scaled by `sign`; `None` where an exponent overflows.
    let private tryCombine (a: (UnitAtom * int) list) (sign: int) (b: (UnitAtom * int) list) =
        let folded =
            b
            |> List.fold
                (fun (acc: Map<UnitAtom, int64> option) (atom, e) ->
                    match acc with
                    | None -> None
                    | Some m ->
                        let sum = (m |> Map.tryFind atom |> Option.defaultValue 0L) + int64 sign * int64 e

                        if sum > maxExp || sum < -maxExp then
                            None
                        else
                            Some(Map.add atom sum m))
                (Some(a |> List.map (fun (k, e) -> k, int64 e) |> Map.ofList))

        folded
        |> Option.map (fun m ->
            m
            |> Map.toList
            |> List.filter (fun (_, e) -> e <> 0L)
            |> List.map (fun (k, e) -> k, int e))

    let private ofFactors (f: (UnitAtom * int) list) : UnitOfMeasure = { Factors = f }

    /// The dimensionless unit, `1`: the identity of `mul`.
    let dimensionless: UnitOfMeasure = ofFactors []

    /// The product of two units. Raises `System.ArgumentException` where an exponent would leave
    /// `±2147483647`.
    let mul (a: UnitOfMeasure) (b: UnitOfMeasure) : UnitOfMeasure =
        match tryCombine a.Factors 1 b.Factors with
        | Some f -> ofFactors f
        | None -> invalidArg "b" "a unit exponent left the range ±2147483647"

    /// The quotient `a / b`. Raises `System.ArgumentException` where an exponent would leave
    /// `±2147483647`.
    let div (a: UnitOfMeasure) (b: UnitOfMeasure) : UnitOfMeasure =
        match tryCombine a.Factors -1 b.Factors with
        | Some f -> ofFactors f
        | None -> invalidArg "b" "a unit exponent left the range ±2147483647"

    /// `u` raised to the integer power `n`; `pow u 0` is `dimensionless`. Raises
    /// `System.ArgumentException` where an exponent would leave `±2147483647`.
    let pow (u: UnitOfMeasure) (n: int) : UnitOfMeasure =
        if n = 0 then
            dimensionless
        else
            u.Factors
            |> List.map (fun (a, e) -> a, checkedExp (int64 e * int64 n))
            |> ofFactors

    /// The dimension a unit measures: non-zero exponents by base dimension and currency.
    let private dimension (u: UnitOfMeasure) : Map<DimKey, int64> =
        u.Factors
        |> List.collect (fun (a, e) ->
            if UnitVocabulary.isCurrency a.Symbol then
                [ Currency a.Symbol, int64 e ]
            else
                UnitVocabulary.atoms[a.Symbol].Dims
                |> List.mapi (fun i x -> Base i, int64 x * int64 e))
        |> List.fold (fun m (k, x) -> Map.add k ((m |> Map.tryFind k |> Option.defaultValue 0L) + x) m) Map.empty
        |> Map.filter (fun _ x -> x <> 0L)

    /// Whether two units measure one dimension, so that a value in one has a value in the other.
    /// Every currency is a dimension of its own: `[GBP]` is compatible with `k[GBP]` and with
    /// nothing priced in another currency.
    let compatible (a: UnitOfMeasure) (b: UnitOfMeasure) : bool = dimension a = dimension b

    let rec private gcd (a: bigint) (b: bigint) : bigint = if b = 0I then a else gcd b (a % b)

    /// `x` to the power `k >= 0`, by squaring.
    let private powNat (x: bigint) (k: int64) : bigint =
        let mutable result = 1I
        let mutable b = x
        let mutable k = k

        while k > 0L do
            if k % 2L = 1L then
                result <- result * b

            b <- b * b
            k <- k / 2L

        result

    /// The scale of a unit relative to the coherent base: the product of its atoms' scales and its
    /// prefixes' powers of ten, each to its exponent, in lowest terms.
    let private scale (u: UnitOfMeasure) : Ratio =
        let mutable num = 1I
        let mutable den = 1I
        let mutable ten = 0L

        for (a, e) in u.Factors do
            let e = int64 e
            ten <- ten + int64 (UnitVocabulary.prefixPower a.Prefix) * e

            if not (UnitVocabulary.isCurrency a.Symbol) then
                let atom = UnitVocabulary.atoms[a.Symbol]

                if e > 0L then
                    num <- num * powNat atom.Num e
                    den <- den * powNat atom.Den e
                else
                    num <- num * powNat atom.Den -e
                    den <- den * powNat atom.Num -e

        if ten > 0L then
            num <- num * powNat 10I ten
        elif ten < 0L then
            den <- den * powNat 10I -ten

        let g = gcd num den
        { Num = num / g; Den = den / g }

    /// The exact factor that turns a value in `source` into the same quantity in `target`:
    /// `conversionFactor km/h m/s` is `5/18`, which is `1/3.6`. Refused, naming both units, where
    /// they are not `compatible`.
    let conversionFactor (source: UnitOfMeasure) (target: UnitOfMeasure) : Result<Ratio, UnitConversionRefusal> =
        if compatible source target then
            Ok(scale (div source target))
        else
            Error(UnitConversionRefusal.Incompatible(source, target))

    /// The unit's one canonical text: the factors with a positive exponent, ordered by symbol then
    /// prefix and joined by `.`; then each factor with a negative exponent as `/` and the factor, in
    /// the same order; an exponent written as its magnitude in ASCII digits after the atom, and
    /// omitted where it is 1; `1` for the dimensionless unit, and a leading `/` where every exponent
    /// is negative. `km/h`, `kg.m/s2`, `/min`, `k[GBP]`. `parse` reads every text `render` writes
    /// back to the same unit.
    let render (u: UnitOfMeasure) : string = u.ToString()

    let private isLetter (c: char) =
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')

    let private isDigit (c: char) = c >= '0' && c <= '9'

    let private startsWith (prefix: string) (s: string) =
        s.Length > prefix.Length && s.Substring(0, prefix.Length) = prefix

    /// Resolve one symbol token to an atom, or to the refusal naming its class.
    let private resolve (tok: string) (pos: int) : Result<UnitAtom, UnitRefusal> =
        match UnitVocabulary.tryAtom tok with
        | Some sym -> Ok { Symbol = sym; Prefix = "" }
        | None ->
            if UnitVocabulary.nonRatio.Contains tok then
                Error(UnitRefusal.NonRatioUnit(tok, pos))
            elif UnitVocabulary.arbitrary.Contains tok then
                Error(UnitRefusal.ArbitraryUnit(tok, pos))
            elif UnitVocabulary.isCurrency tok then
                Ok { Symbol = tok; Prefix = "" }
            else
                let viaPrefix =
                    UnitVocabulary.prefixes
                    |> List.tryPick (fun (p, _) ->
                        if startsWith p tok then
                            let rest = tok.Substring p.Length

                            match UnitVocabulary.tryAtom rest with
                            | Some sym when UnitVocabulary.atoms[sym].Metric -> Some(Ok { Symbol = sym; Prefix = p })
                            | Some _ -> Some(Error(UnitRefusal.PrefixNotAllowed(tok, pos)))
                            | None ->
                                if UnitVocabulary.nonRatio.Contains rest then
                                    Some(Error(UnitRefusal.NonRatioUnit(tok, pos)))
                                elif UnitVocabulary.arbitrary.Contains rest then
                                    Some(Error(UnitRefusal.ArbitraryUnit(tok, pos)))
                                elif UnitVocabulary.isCurrency rest then
                                    Some(Ok { Symbol = rest; Prefix = p })
                                else
                                    None
                        else
                            None)

                viaPrefix |> Option.defaultValue (Error(UnitRefusal.UnknownAtom(tok, pos)))

    /// One open term of the parse: its product so far, the sign the next component takes (`.` or
    /// `/` before it), and the sign the whole term takes in the term that encloses it.
    type private Frame =
        { Acc: (UnitAtom * int) list
          Next: int
          Outer: int
          Opened: int }

    let private expRange = "an exponent within ±2147483647"

    /// Read a unit from its text (UCUM's case-sensitive syntax over the admitted subset): atoms under
    /// an optional prefix, each with an optional signed integer exponent; products `.`, quotients
    /// `/` (left-associative, with an optional leading `/`), parentheses, and the factor `1`. No
    /// white space is read. Every refusal is typed and names the token and its position.
    let parse (text: string) : Result<UnitOfMeasure, UnitRefusal> =
        if isNull text || text.Length = 0 then
            Error UnitRefusal.Empty
        else
            let n = text.Length
            let charAt i = if i < n then string text[i] else ""

            // An iterative parse over an explicit stack of open terms, so that nesting depth never
            // reaches the machine stack.
            let mutable stack =
                [ { Acc = []
                    Next = 1
                    Outer = 1
                    Opened = 0 } ]

            let mutable pos = 0
            let mutable expectComponent = true
            let mutable termStart = true
            let mutable result: Result<UnitOfMeasure, UnitRefusal> option = None

            while result.IsNone do
                if expectComponent then
                    if pos >= n then
                        result <- Some(Error(UnitRefusal.Malformed("", pos, "a unit")))
                    else
                        let c = text[pos]

                        if termStart && c = '/' then
                            stack <-
                                match stack with
                                | top :: rest -> { top with Next = -1 } :: rest
                                | [] -> []

                            pos <- pos + 1
                            termStart <- false
                        elif c = '(' then
                            let outer =
                                match stack with
                                | top :: _ -> top.Next
                                | [] -> 1

                            stack <-
                                { Acc = []
                                  Next = 1
                                  Outer = outer
                                  Opened = pos }
                                :: stack

                            pos <- pos + 1
                            termStart <- true
                        elif isDigit c then
                            let start = pos

                            while pos < n && isDigit text[pos] do
                                pos <- pos + 1

                            if pos < n && (text[pos] = '*' || text[pos] = '^') then
                                pos <- pos + 1

                                if pos < n && (text[pos] = '+' || text[pos] = '-') then
                                    pos <- pos + 1

                                while pos < n && isDigit text[pos] do
                                    pos <- pos + 1

                            let tok = text.Substring(start, pos - start)

                            if tok = "1" then
                                expectComponent <- false
                                termStart <- false
                            else
                                result <- Some(Error(UnitRefusal.NumericFactor(tok, start)))
                        elif c = '{' then
                            let close = text.IndexOf('}', pos)

                            if close < 0 then
                                result <-
                                    Some(
                                        Error(
                                            UnitRefusal.Malformed(text.Substring pos, pos, "'}' closing the annotation")
                                        )
                                    )
                            else
                                result <- Some(Error(UnitRefusal.Annotation(text.Substring(pos, close - pos + 1), pos)))
                        elif isLetter c || c = '[' || c = '%' then
                            let start = pos
                            let mutable bad = false

                            while not bad
                                  && pos < n
                                  && (isLetter text[pos]
                                      || text[pos] = '_'
                                      || text[pos] = '\''
                                      || text[pos] = '%'
                                      || text[pos] = '[') do
                                if text[pos] = '[' then
                                    let close = text.IndexOf(']', pos)

                                    if close < 0 then
                                        bad <- true

                                        result <-
                                            Some(
                                                Error(
                                                    UnitRefusal.Malformed(
                                                        text.Substring pos,
                                                        pos,
                                                        "']' closing the symbol"
                                                    )
                                                )
                                            )
                                    else
                                        pos <- close + 1
                                else
                                    pos <- pos + 1

                            if not bad then
                                let tok = text.Substring(start, pos - start)

                                match resolve tok start with
                                | Error r -> result <- Some(Error r)
                                | Ok atom ->
                                    let expStart = pos
                                    let mutable sign = 1L

                                    if pos < n && (text[pos] = '+' || text[pos] = '-') then
                                        if text[pos] = '-' then
                                            sign <- -1L

                                        pos <- pos + 1

                                    let digitsStart = pos
                                    let mutable value = 0L
                                    let mutable over = false

                                    while pos < n && isDigit text[pos] do
                                        if not over then
                                            value <- value * 10L + int64 (int text[pos] - int '0')

                                            if value > maxExp then
                                                over <- true

                                        pos <- pos + 1

                                    if pos = digitsStart && pos > expStart then
                                        result <-
                                            Some(
                                                Error(
                                                    UnitRefusal.Malformed(charAt pos, pos, "the digits of an exponent")
                                                )
                                            )
                                    elif over then
                                        result <-
                                            Some(
                                                Error(
                                                    UnitRefusal.Malformed(
                                                        text.Substring(expStart, pos - expStart),
                                                        expStart,
                                                        expRange
                                                    )
                                                )
                                            )
                                    else
                                        let e = if pos = expStart then 1L else sign * value
                                        let comp = if e = 0L then [] else [ atom, int e ]

                                        // Fold the component into the innermost open term.
                                        match stack with
                                        | top :: rest ->
                                            match tryCombine top.Acc top.Next comp with
                                            | Some acc -> stack <- { top with Acc = acc } :: rest
                                            | None ->
                                                result <-
                                                    Some(
                                                        Error(
                                                            UnitRefusal.Malformed(
                                                                text.Substring(start, pos - start),
                                                                start,
                                                                expRange
                                                            )
                                                        )
                                                    )
                                        | [] -> ()

                                        if result.IsNone then
                                            expectComponent <- false
                                            termStart <- false
                        else
                            result <- Some(Error(UnitRefusal.Malformed(string c, pos, "a unit")))
                else if pos >= n then
                    match stack with
                    | [ top ] -> result <- Some(Ok(ofFactors top.Acc))
                    | top :: _ ->
                        result <-
                            Some(Error(UnitRefusal.Malformed("", pos, sprintf "')' closing the '(' at %d" top.Opened)))
                    | [] -> result <- Some(Error(UnitRefusal.Malformed("", pos, "a unit")))
                else
                    match text[pos] with
                    | '.' ->
                        stack <-
                            match stack with
                            | top :: rest -> { top with Next = 1 } :: rest
                            | [] -> []

                        pos <- pos + 1
                        expectComponent <- true
                    | '/' ->
                        stack <-
                            match stack with
                            | top :: rest -> { top with Next = -1 } :: rest
                            | [] -> []

                        pos <- pos + 1
                        expectComponent <- true
                    | ')' ->
                        match stack with
                        | inner :: outer :: rest ->
                            let at = pos
                            pos <- pos + 1
                            stack <- outer :: rest

                            // The closed term is one component of the term that encloses it.
                            match tryCombine outer.Acc inner.Outer inner.Acc with
                            | Some acc -> stack <- { outer with Acc = acc } :: rest
                            | None -> result <- Some(Error(UnitRefusal.Malformed(")", at, expRange)))
                        | _ -> result <- Some(Error(UnitRefusal.Malformed(")", pos, "'.', '/' or the end")))
                    | '{' ->
                        let close = text.IndexOf('}', pos)

                        if close < 0 then
                            result <-
                                Some(
                                    Error(UnitRefusal.Malformed(text.Substring pos, pos, "'}' closing the annotation"))
                                )
                        else
                            result <- Some(Error(UnitRefusal.Annotation(text.Substring(pos, close - pos + 1), pos)))
                    | c ->
                        let expected =
                            if List.length stack > 1 then
                                "'.', '/' or ')'"
                            else
                                "'.', '/' or the end"

                        result <- Some(Error(UnitRefusal.Malformed(string c, pos, expected)))

            result.Value
