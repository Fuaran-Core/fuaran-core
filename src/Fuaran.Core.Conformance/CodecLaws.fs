namespace Fuaran.Core

/// The codec family (Phase 442): the premises a domain owes for the parts it hands the `Codec`
/// blocks, sampled at the domain's OWN `Codec<'T>` over values its generator draws.
///
/// Phase 436 proved the blocks' round trip and refusal soundness CONDITIONAL on functions only the
/// domain writes — `map`'s `there` inverting its `back`, `refine`'s `check` admitting every value
/// `back` writes, a record's constructor rebuilding the value from its getters, a union case's
/// `inject` inverting its `project`, a `make` / `ofDecoder` reader reading back its writer, and a
/// refusal the domain raises itself being sound. A codec is opaque once built, so the family states
/// each premise by its consequence at the composed codec, in the cell whose failure that premise
/// produces: a refusal of a written value is a `refine` or a reader that does not admit its writer;
/// a value read back unequal is a `map`, a constructor or an `inject` that does not invert; a
/// refusal of a mutated document that names nothing in it is a reader, an `andThen` or a `fail`
/// the domain wrote.
///
/// It reuses the corpus harness rather than restating it: the text leg is `Corpus.roundTrip` over
/// the `Codec.corpus` bridge under every encoding profile, and the refusal leg walks
/// `Corpus.mutations` and judges each refusal with `DecodeError.resolvesIn`, the predicate
/// `Corpus.refusalLaws` judges with. Where `Corpus.refusalLaws` reads the strict reader's FIRST
/// refusal, this family reads every refusal the collecting reader answers.
module internal CodecLaws =

    /// The roster id the guards are labelled with.
    [<Literal>]
    let private family = "Conformance.codecLaws"

    /// The guard dimension the refusal arm is counted in.
    [<Literal>]
    let private mutationOutcome = "mutation outcome"

    /// Sound is the soundness Phase 436's theorem 22 proves of Core's own blocks: the refusal
    /// resolves in the document it was raised over, and it is not `InvalidJson`, which a reader of a
    /// parsed document never has cause to raise and which stops resolving one member down.
    let private sound (doc: JVal) (e: DecodeError) : bool =
        e.Code <> DecodeCode.InvalidJson && DecodeError.resolvesIn doc e

    let private firstDefect (es: DecodeError list) : string =
        match es with
        | e :: _ -> DecodeError.render e
        | [] -> "a refusal carrying no defect"

    /// The laws, over `iterations` values `gen` draws from a cursor seeded at `seed`. `'T` needs
    /// equality: "reads back as the value" is `=`.
    let codecLaws (codec: Codec<'T>) (gen: ConfRng.T -> 'T * ConfRng.T) (seed: int) (iterations: int) : LawResult list =
        let written =
            LawKit.LawCell "codec laws: every sampled value is written — the declaration covers it"

        let reads =
            LawKit.LawCell
                "codec laws: every written value reads with no defect — a refine admits every value its back writes, and a make or ofDecoder reader reads what its writer writes"

        let inverse =
            LawKit.LawCell
                "codec laws: every written value reads back as the value it was written from — a map's there inverts its back, a record's constructor rebuilds it from its getters, a union case's inject inverts its project"

        let text =
            LawKit.LawCell "codec laws: the value's canonical text under every encoding profile reads back as the value"

        let refusals =
            LawKit.LawCell(
                "codec laws: every refusal of a mutated written document resolves in that document with a sound code",
                Some mutationOutcome
            )

        let mutable refused = 0
        let mutable accepted = 0
        let mutable distinct: Set<string> = Set.empty

        LawKit.run iterations seed (fun rng _ at ->
            let v = rng.Draw gen

            match codec.Write v with
            | Error f ->
                written.Check(
                    false,
                    fun () -> at (sprintf "%A is refused by the writer: %s" v (CodecDeclarationFault.describe f))
                )
            | Ok doc ->
                written.Saw()
                distinct <- Set.add (Json.render doc) distinct

                match codec.ReadAll doc with
                | Error es ->
                    reads.Check(
                        false,
                        fun () ->
                            at (sprintf "%A is written as %s, which reads as %s" v (Json.render doc) (firstDefect es))
                    )
                | Ok back ->
                    reads.Saw()

                    inverse.Check(
                        (back = v),
                        fun () -> at (sprintf "%A is written as %s, which reads back as %A" v (Json.render doc) back)
                    )

                for p in EncodingProfile.all do
                    match Corpus.roundTrip (Codec.corpus p codec) v with
                    | Ok() -> text.Saw()
                    | Error m -> text.Fail(at (sprintf "%A under %s: %s" v (EncodingProfile.name p) m))

                for m in Corpus.mutations doc do
                    match codec.ReadAll m with
                    | Ok _ -> accepted <- accepted + 1
                    | Error es ->
                        refused <- refused + 1

                        let unsound =
                            match es with
                            | [] -> Some "a refusal carrying no defect"
                            | _ -> es |> List.tryFind (sound m >> not) |> Option.map DecodeError.render

                        refusals.Check(
                            unsound.IsNone,
                            fun () ->
                                at (
                                    sprintf
                                        "%s, a mutation of %A's document, is refused with %s, which does not resolve in it with a sound code"
                                        (Json.render m)
                                        v
                                        (Option.defaultValue "" unsound)
                                )
                        ))

        LawKit.results [ written; reads; inverse; text; refusals ]
        @ [ SampleAdequacy.reachedBeside family mutationOutcome seed [ "refused", refused ] [ "accepted", accepted ]
            SampleAdequacy.spanned family "distinct written values" 2 seed (Set.count distinct) iterations ]
