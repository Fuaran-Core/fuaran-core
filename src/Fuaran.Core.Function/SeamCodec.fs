namespace Fuaran.Core

/// The strict-read helpers every seam codec shares (Phase 388) — `CapabilityCodec`'s and
/// `QueryCodec`'s members check (Phase 251; `Decoder.members`, its generalisation, since Phase 310),
/// and the quoting a refusal names a closed set with. They were written out once per codec, body for
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

    /// The first member of `el` outside `known`, as a refusal naming it, the object it is in
    /// (`where`), and the members read.
    let members (where: string) (known: string list) : Decoder<unit> =
        fun el ->
            Decoder.members known el
            |> Result.mapError (fun e ->
                match List.tryLast e.Path with
                | Some(PathSegment.Key k) ->
                    { e with
                        Message =
                            "unknown member '"
                            + k
                            + "' in "
                            + where
                            + "; its members are "
                            + quoteAll (List.sort known) }
                | _ -> e)

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
