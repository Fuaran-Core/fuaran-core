namespace Fuaran.Core.Idl

open Fuaran.Core

/// The closed set of string FORMATS a hosted slot's declared wire form may name
/// (Phase 252, [[HostedCodec.Format]]) — their names, and the one definition of what each
/// admits, so the interpreter, the sampler and the generated hosts cannot disagree on it.
///
/// **Closed, deliberately.** A format the engine does not know is one no leg can
/// check or draw from, which is the disagreement the declaration exists to remove; since
/// Phase 391 the type cannot hold one, and the `idl.json` reader refuses an unknown name. The three
/// are JSON Schema's spellings, read strictly: `date` is RFC 3339 `full-date` (a real
/// calendar day, years 0001–9999), `date-time` is RFC 3339 `date-time` (an offset is
/// required; `T`/`Z` in either case; no leap second), and `uuid` is the 8-4-4-4-12 hex
/// form in either case.
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module HostedFormat =

    /// Every format, in declaration order.
    let all: HostedFormat list =
        [ HostedFormat.Date; HostedFormat.DateTime; HostedFormat.Uuid ]

    /// The format's name on the wire and in a generated module: `date`, `date-time`, `uuid`.
    let name (format: HostedFormat) : string =
        match format with
        | HostedFormat.Date -> "date"
        | HostedFormat.DateTime -> "date-time"
        | HostedFormat.Uuid -> "uuid"

    /// Every format's name, in `all`'s order.
    let known: string list = all |> List.map name

    /// The format a name spells, or `None` for a name the engine does not know.
    let tryParse (s: string) : HostedFormat option =
        all |> List.tryFind (fun f -> name f = s)

    let private dateRx =
        System.Text.RegularExpressions.Regex("^([0-9]{4})-([0-9]{2})-([0-9]{2})$")

    let private dateTimeRx =
        System.Text.RegularExpressions.Regex(
            "^([0-9]{4})-([0-9]{2})-([0-9]{2})[Tt]([0-9]{2}):([0-9]{2}):([0-9]{2})(\\.[0-9]+)?([Zz]|[+-]([0-9]{2}):([0-9]{2}))$"
        )

    let private uuidRx =
        System.Text.RegularExpressions.Regex(
            "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"
        )

    let private validDay (y: int) (m: int) (d: int) =
        y >= 1 && m >= 1 && m <= 12 && d >= 1 && d <= System.DateTime.DaysInMonth(y, m)

    /// Whether the string `s` is in `format`.
    let admits (format: HostedFormat) (s: string) : bool =
        let num (g: System.Text.RegularExpressions.Group) = int g.Value

        match format with
        | HostedFormat.Date ->
            let m = dateRx.Match s
            m.Success && validDay (num m.Groups[1]) (num m.Groups[2]) (num m.Groups[3])
        | HostedFormat.DateTime ->
            let m = dateTimeRx.Match s

            m.Success
            && validDay (num m.Groups[1]) (num m.Groups[2]) (num m.Groups[3])
            && num m.Groups[4] <= 23
            && num m.Groups[5] <= 59
            && num m.Groups[6] <= 59
            && (not m.Groups[9].Success || (num m.Groups[9] <= 23 && num m.Groups[10] <= 59))
        | HostedFormat.Uuid -> uuidRx.IsMatch s
