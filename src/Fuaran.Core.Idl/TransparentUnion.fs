namespace Fuaran.Core.Idl

open Fuaran.Core

/// A "transparent" union case is encoded/decoded as a bare JSON value (its single
/// field's value) rather than a `$type`-tagged object — the Fuaran-UI 0.2.0
/// bare-string canonical `TextSource.Literal` (`{"$type":"Literal","text":"x"}` →
/// `"x"`). Keyed on the vocabulary's declared `HardenPolicy.TransparentUnions`; the transparent
/// case carries exactly one field. The `Bound` / non-transparent cases stay `$type`-tagged
/// objects.
///
/// **Public rather than internal since Phase 97**, because the split made the
/// dependency real: the emitters moved to `Fuaran.Core.Idl.Codegen`, and an emitter
/// must agree with this codec about which cases are bare or it generates a host that
/// disagrees with the reference implementation on the wire. `internal` had been
/// hiding a genuine contract behind an assembly boundary that no longer holds — and
/// the same fact is what any third-party emitter needs, so stating it is right.
///
/// **The wart is closed since Phase 116.** The rule used to be keyed on a hard-coded
/// vocabulary name (`TextSource`) inside an engine that is otherwise domain-generic
/// (D14); it is now read from [[HardenPolicy.TransparentUnions]], which the vocabulary
/// declares on its own [[Idl]] value and the artifact carries. Phase 116 kept the old
/// name reachable as a DEFAULT so every shipped corpus stayed byte-identical; Phase 180
/// deleted that default once both published artifacts declared their block, so a
/// vocabulary that names no transparent union now has none — which is what the empty
/// list has always said, and now the only thing it can say.
module TransparentUnion =
    /// The transparent case tag for a union under a declared policy, or `None` if the
    /// union has none. Pass the owning vocabulary's `idl.Harden`.
    let tag (policy: HardenPolicy) (u: IdlUnion) : string option =
        policy.TransparentUnions
        |> List.tryPick (fun (name, case) -> if name = u.Name then Some case else None)
