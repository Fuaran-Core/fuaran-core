# Units — `Fuaran.Core.Unit` (Phase 426)

A number in a column says nothing about what it measures, and the commonest silent error in modelling
and scientific data follows from that: metres added to feet, a rate summed as if it were a count, pence
read as pounds. F#'s units of measure catch that class at compile time and erase it, so they cannot help
with data whose units arrive at run time, on the wire. `Fuaran.Core.Unit` is the same algebra as a
runtime VALUE. It is its own package with no project references, so a consumer that needs units without
columns takes only it; `Fuaran.Core.Column` depends on it (Phase 427), never the reverse.

This note is the package's design record. `tests/Fuaran.Core.Tests/UnitTests.fs` reads the atom table
below and holds the code to it row by row, so the table is the vocabulary, not a description of it.

## What a unit is

A unit is a product of **atoms**, each under an optional SI **prefix**, with non-zero integer exponents
(`km/h` is `km¹·h⁻¹`; `kg.m/s2` is `kg¹·m¹·s⁻²`). The type is `UnitOfMeasure`, and it is held in one
canonical form: factors ordered, equal atoms merged (`m.m` is `m2`), zero exponents dropped (`m/m` is
`1`).

**Identity is the product, not what it denotes.** Every unit DENOTES a scale factor times a product of
base dimensions — `km/h` denotes `5/18 · L·T⁻¹` — and that denotation is what `compatible` and
`conversionFactor` read. But two units are EQUAL only when they are one product. `km/h` and `m/s` are
different units, compatible, with the exact factor `5/18` between them; `N` and `kg.m/s2` are different
units with the factor `1`; `mL` and `cm3` likewise. The alternative — identity by denotation — was
rejected because it cannot give a unit back as it was written: the only text a denotation determines is
something like `5/18.m.s-1`, and a column declared in `km/h` or `mg/dL` must render as `km/h` or
`mg/dL`. Equality by denotation remains available to any caller as "compatible with factor `1`".

The type is named `UnitOfMeasure`, not `Unit`: FSharp.Core already defines `Unit` (the type of `()`),
and a `Fuaran.Core.Unit` type would shadow it in every file that opens `Fuaran.Core`, turning a
consumer's `Result<Unit, string>` into a different type. The functions live in the `Unit` module
(`Unit.parse`, `Unit.render`, …), which shadows nothing.

## The admitted subset

UCUM, the unit code system used across science and healthcare, is the grammar, because inventing one
would give every host a dialect to argue about. Its case-sensitive syntax is read over a STATED subset:

- the seven SI base units, with mass at the **gram** (UCUM's base; `kg` is `k` + `g`);
- the twenty UCUM SI prefixes on every atom marked metric below;
- the SI coherent derived units (degree Celsius excepted — see *Affine units*);
- the metric non-SI units `L` (and its UCUM spelling `l`, read as `L`), `t` and `bar`;
- the non-metric units of time `min`, `h`, `d`, `wk` and `a` (UCUM's mean Julian year, 365.25 days);
- the dimensionless ratios `%`, `[ppm]` and `[ppb]`;
- the international customary length and mass units `[in_i]`, `[ft_i]`, `[yd_i]`, `[mi_i]`, `[lb_av]`,
  `[oz_av]`, each defined exactly by the 1959 agreement;
- ISO 4217 currencies, written in brackets (`[GBP]`) — see *Currencies*;
- products `.`, quotients `/` (left-associative, with an optional leading `/`: `/min`), parentheses,
  integer exponents with an optional sign written directly after the atom (`m2`, `s-1`, `m+3`), and the
  factor `1`.

No white space is read anywhere: a unit has one text, and `" m"` is not it.

**One departure from UCUM:** UCUM defines the mole as the dimensionless number `6.0221367 × 10²³`. Here
`mol` is the SI base unit of its own dimension, amount of substance, so `mol/L` stays a concentration
rather than becoming a very large dimensionless number, and the conversion factor between two amounts
never carries the Avogadro constant (whose UCUM value predates the 2019 SI redefinition). `rad` and `sr`
follow SI too: dimensionless, where UCUM makes the radian a base unit.

### The admitted atoms

`Equals` is the atom's exact definition as `<factor> <unit>`: one of the atom equals `factor` of that
unit. The base atoms equal themselves.

| Atom | Name | Metric | Equals |
|---|---|---|---|
| `m` | metre | yes | `1 m` |
| `g` | gram | yes | `1 g` |
| `s` | second | yes | `1 s` |
| `A` | ampere | yes | `1 A` |
| `K` | kelvin | yes | `1 K` |
| `mol` | mole | yes | `1 mol` |
| `cd` | candela | yes | `1 cd` |
| `rad` | radian | yes | `1 1` |
| `sr` | steradian | yes | `1 1` |
| `Hz` | hertz | yes | `1 /s` |
| `N` | newton | yes | `1 kg.m/s2` |
| `Pa` | pascal | yes | `1 N/m2` |
| `J` | joule | yes | `1 N.m` |
| `W` | watt | yes | `1 J/s` |
| `C` | coulomb | yes | `1 A.s` |
| `V` | volt | yes | `1 W/A` |
| `F` | farad | yes | `1 C/V` |
| `Ohm` | ohm | yes | `1 V/A` |
| `S` | siemens | yes | `1 /Ohm` |
| `Wb` | weber | yes | `1 V.s` |
| `T` | tesla | yes | `1 Wb/m2` |
| `H` | henry | yes | `1 Wb/A` |
| `lm` | lumen | yes | `1 cd.sr` |
| `lx` | lux | yes | `1 lm/m2` |
| `Bq` | becquerel | yes | `1 /s` |
| `Gy` | gray | yes | `1 J/kg` |
| `Sv` | sievert | yes | `1 J/kg` |
| `kat` | katal | yes | `1 mol/s` |
| `L` | litre | yes | `1/1000 m3` |
| `t` | tonne | yes | `1000 kg` |
| `bar` | bar | yes | `100000 Pa` |
| `min` | minute | no | `60 s` |
| `h` | hour | no | `60 min` |
| `d` | day | no | `24 h` |
| `wk` | week | no | `7 d` |
| `a` | mean Julian year | no | `1461/4 d` |
| `%` | percent | no | `1/100 1` |
| `[ppm]` | parts per million | no | `1/1000000 1` |
| `[ppb]` | parts per billion | no | `1/1000000000 1` |
| `[in_i]` | international inch | no | `127/50 cm` |
| `[ft_i]` | international foot | no | `12 [in_i]` |
| `[yd_i]` | international yard | no | `3 [ft_i]` |
| `[mi_i]` | international mile | no | `5280 [ft_i]` |
| `[lb_av]` | avoirdupois pound | no | `45359237/100000 g` |
| `[oz_av]` | avoirdupois ounce | no | `1/16 [lb_av]` |

`Hz` and `Bq` are compatible with factor `1`, as are `Gy` and `Sv`, and `rad/s` and `Hz`: that is SI's
own reading of them, and a consumer that must keep them apart labels the column.

The prefixes, with the power of ten each stands for: `Y` 24, `Z` 21, `E` 18, `P` 15, `T` 12, `G` 9, `M`
6, `k` 3, `h` 2, `da` 1, `d` −1, `c` −2, `m` −3, `u` −6 (UCUM's ASCII micro), `n` −9, `p` −12, `f` −15,
`a` −18, `z` −21, `y` −24. A token is read as a whole atom first and as prefix + atom only when it is not
one (`Pa` is the pascal, `cd` the candela, `a` the year, `am` the attometre); `da` is tried before `d`.
The prefixes SI added in 2022 (`R`, `Q`, `r`, `q`) are not UCUM's and are not read.

## What is refused, and why

`Unit.parse` returns `Result<UnitOfMeasure, UnitRefusal>`. Every refusal but `Empty` names the
offending token as written and its position — the zero-based index of its first character.

| Refusal | Reached by | Why it is refused |
|---|---|---|
| `Empty` | `""`, null | no unit is written; the dimensionless unit is written `1` |
| `Malformed` | `m.`, `m/(s`, `m^2`, `m s`, `m-`, an exponent beyond ±2147483647 | the text leaves the grammar; the refusal names what stood there and what the grammar wanted |
| `UnknownAtom` | `furlong`, `deg`, `[degR]` | not in the subset. Admitting a unit later is additive; removing one would not be |
| `Annotation` | `{beats}/min`, `m{tall}` | UCUM annotations carry no algebraic meaning: admitting them would give one unit many texts, or carry uninterpreted text through the algebra |
| `ArbitraryUnit` | `[IU]`, `[arb'U]`, `[CFU]` | defined by a laboratory procedure, not a dimension; UCUM itself says no factor relates them to anything |
| `NonRatioUnit` | `Cel`, `[degF]`, `dB`, `Np`, `[pH]` | special units whose conversion is not a multiplication (affine or logarithmic); see below |
| `PrefixNotAllowed` | `kmin`, `k%`, `M[ft_i]` | a prefix on an atom UCUM marks non-metric |
| `NumericFactor` | `10*3`, `1000.m` | the subset carries scale in atoms and prefixes, never as a free number; a unit `10*3/L` would otherwise denote what `/mL` does under a second text |

`deg` (the angular degree) is refused as unknown rather than admitted, because its factor, π/180, is
irrational and no exact representation holds it.

## Affine units — refused outright

Degree Celsius and degree Fahrenheit are not ratios of the kelvin: `0 Cel` is `273.15 K`, so converting
one is `x · a + b`, and a product such as `Cel/s` or `Cel2` has no meaning a factor can carry. Two designs
were open: admit them as absolute values that refuse multiplication and composition, or refuse them.

**They are refused**, as `NonRatioUnit`. Admitting them would make `mul`, `div` and `pow` partial over
the whole type — every caller of the algebra would hold a `Result` to cover a case only temperatures
reach — and the group laws would hold only on the part of the type that is not temperature. Refusing is
also the reversible choice: admitting a token later is additive, while taking one back is a breaking
change. A column of Celsius readings is declared in `K` with its values converted, or carries the text
in its label; a later phase that needs affine scales can add them as a separate type beside this one.

## The scale factor — an exact rational

`Unit.conversionFactor source target` returns `Ok ratio` — the number that multiplies a value in
`source` to give the same quantity in `target` — or `Error (Incompatible (source, target))`.
`conversionFactor km/h m/s` is `5/18`, which is `1/3.6`.

`Ratio` is a positive rational over arbitrary-precision integers, always in lowest terms, so two equal
factors are one numerator and one denominator. Not a float: a binary float cannot hold `1/3.6` or the
inch, so the factor would be wrong by an ulp, by a different ulp on another host, and conversions that
compose (`km` to `m` to `mm`) would drift. Not a power of ten with an exact mantissa either: `60`, `3600`
and `1461/4` are not decimal fractions of anything, and `1/3.6` has no finite decimal. A rational is exact
on every host, and each host has one (a .NET or JavaScript big integer, Python's `int`, Go's `big.Rat`).
The factor's size grows with the exponents it is raised to; no unit a datum carries makes it large.

## Currencies

UCUM has no currencies. ISO 4217 alphabetic codes sit beside it, written in brackets — `[GBP]`, `[USD]` —
the shape UCUM gives atoms with non-alphabetic spellings, so a code can never be read as a prefix and an
atom. **Each currency is a dimension of its own**: `[GBP]` is compatible with nothing priced in another
currency, and with nothing physical, because two currencies are not related by a constant; the exchange
rate is data, and a conversion between them is a computation over that data, never a unit factor.

Currencies take SI prefixes: `k[GBP]` is a thousand pounds, compatible with `[GBP]` with factor `1000`,
and `c[GBP]` is a penny, compatible with factor `1/100` — so pence read as pounds is a unit mismatch the
algebra sees and converts exactly. Products are ordinary: `[GBP]/h`, `[USD]/[lb_av]`.

The parser checks the code's SHAPE — three ASCII capitals in brackets — and not membership of the
current ISO 4217 list, which changes on a schedule Core's releases do not follow; a retired code
(`[HRK]`) still labels the data that was written in it. UCUM's arbitrary units that share the shape
(`[CFU]`, `[PFU]`, `[FFU]`, `[BAU]`, `[PNU]`, `[FEU]`, `[ELU]`) are read as arbitrary units, never as
currencies.

## The canonical text

`Unit.render` writes exactly one text per unit, and `Unit.parse` reads every text it writes back to the
same unit:

1. Factors are ordered by atom symbol, then by prefix, ORDINALLY (by UTF-16 code unit, which over this
   ASCII vocabulary is byte order): `kg.m/s2`, `N.m`, `kW.h`, `mg/dL`.
2. The factors with a positive exponent come first, joined by `.`; then each factor with a negative
   exponent, as `/` followed by the factor, in the same order: `kg/m/s2` is `kg·m⁻¹·s⁻²`, which UCUM's
   left-associative `/` reads back correctly.
3. An exponent is written as its magnitude in ASCII digits directly after the atom, and omitted where it
   is 1 — never a sign, never a `+`.
4. The dimensionless unit is `1`. A unit with only negative exponents starts with `/` (`/min`, `/s2`).
5. A spelling alias is written as its canonical atom: `ml` renders `mL`.

Parsing is more liberal than rendering — it reads `s-1`, `m+2`, `(kg.m)/s2`, `1/s`, `kg/(m.s2)`, `m.m`,
and `ml` — and every one of those reads to the unit whose canonical text `render` then writes.

## Exponents

Exponents are 32-bit integers within `±2147483647`. `parse` refuses a written exponent outside that
range as `Malformed`; `mul`, `div` and `pow` raise `System.ArgumentException` where a result exponent
would leave it rather than wrap, because a wrapped exponent is a wrong unit and no data carries a unit
anywhere near the bound.

## The algebra

| Function | Meaning |
|---|---|
| `Unit.dimensionless` | `1`, the identity of `mul` |
| `Unit.mul a b` | the product; associative and commutative, with `dimensionless` its identity |
| `Unit.div a b` | the quotient; `mul a (div dimensionless a)` is `dimensionless` |
| `Unit.pow u n` | `u` to the integer power `n`; `pow u 0` is `dimensionless` |
| `Unit.compatible a b` | whether `a` and `b` measure one dimension |
| `Unit.conversionFactor a b` | the exact factor from `a` to `b`, refused where they are incompatible |

The laws are sampled by `UnitTests.fs` over a drawn pool; Phase 428 proves them over a model of this
file. Until then `proofs/coverage-exclusions.json` records the package as scheduled for that model.

## Publication

The package is registered where the release reads its roster: `Fuaran.Core.slnx` (which the publish
workflow packs) and the packable-project derivation the registry probe and the README table share. So
the next release tag packs and pushes `Fuaran.Core.Unit` with the rest. Checked on 2026-10-10 for the
first push of a NEW id, which is where a publish fails if anything does: `Fuaran.Core.Unit` is unclaimed
on nuget.org; the `Fuaran.Core` id prefix is reserved to the account that owns every `Fuaran.Core.*`
package (the registry marks each one verified), so only that owner can push the new id; and the trusted
publishing policy has already pushed a new id under the prefix — `Fuaran.Core.ContentAddress` first
appeared at `0.35.2`, through the same workflow — so the policy covers new ids, not only existing ones.

The one other first-time cost is downstream: the Fable compile and value legs run in a consumer that
owns a Fable toolchain, against the candidate packs, and that consumer's smoke project must reference
the new package before a cut, as it did for `Fuaran.Core.ContentAddress` at `0.35.2`.
