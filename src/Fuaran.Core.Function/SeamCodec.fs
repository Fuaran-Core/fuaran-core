namespace Fuaran.Core

/// The strict-read helpers every seam codec shares (Phase 388) — the present-member and every-item
/// checks a strict read composes with `Decoder.members` (Phase 520: the codecs' own sentences for an
/// undeclared member and an unknown tag went, and the refusal is `Decoder`'s), and the quoting a
/// refusal names a closed set with. They were written out once per codec, body for
/// body; `Fuaran.Core.Query` reads them through this package's `InternalsVisibleTo`. Internal: none of
/// it is surface, and a refusal's sentence is pinned by the wire-surface baselines that read the
/// documents these codecs emit, not by this module.
module internal SeamCodec =

    /// Values in single quotes, comma-separated — how a refusal names a closed set.
    let quoteAll (xs: string list) : string =
        xs |> List.map (fun x -> "'" + x + "'") |> String.concat ", "

    /// The `"$type"` tag of `el`, when it carries a string one.
    let tagOf (el: JVal) : string option =
        match Decoder.tryMember "$type" el with
        | Some(JStr t) -> Some t
        | _ -> None

    /// Check the member `name` of `el`, where it is present.
    let within (name: string) (check: Decoder<unit>) : Decoder<unit> =
        fun el ->
            match el with
            | JObj _ -> Decoder.optField name check el |> Result.map ignore
            | _ -> Ok()

    /// Check every element of an array.
    let each (check: Decoder<unit>) : Decoder<unit> =
        fun el ->
            match el with
            | JArr _ -> Decoder.list check el |> Result.map ignore
            | _ -> Ok()
