namespace Fuaran.Core

open System.Runtime.CompilerServices

/// The members this package reads from `Fuaran.Core.Function`'s internals (through that package's
/// `InternalsVisibleTo`) — the strict-read helpers of `SeamCodec`, the keyed-map algebra of
/// `KeyedRegistry` and the policy's `RegistryPolicy.admit` — behind the one boundary every such read
/// crosses.
///
/// **Why each is `NoInlining` (DECISIONS.md D129).** The F# optimiser inlines a public function's body
/// into the assembly that CALLS it, and it does not know that a member another assembly shares only
/// through `InternalsVisibleTo` is out of that caller's reach: `QueryRegistry.register` was one call
/// to `KeyedRegistry.register`, small enough to be copied into a Release-built consumer, which then
/// failed with `MethodAccessException`. A body marked `NoInlining` is never copied into another
/// assembly, so the internal reference stays here. Each forwarder takes its target's FULL argument
/// list, so the reference sits in its own body rather than in a closure it returns.
/// `CrossAssemblyInliningTests` fails on any reference to another Core assembly's internal member
/// from a function without the attribute.
module internal FunctionInternals =

    /// The strict-read helpers, `SeamCodec`'s — opened by the codec, so nothing else lives here.
    module Reads =

        /// `SeamCodec.quoteAll`: values in single quotes, comma-separated.
        [<MethodImpl(MethodImplOptions.NoInlining)>]
        let quoteAll (xs: string list) : string = SeamCodec.quoteAll xs

        /// `SeamCodec.tagOf`: the `"$type"` tag of `el`, when it carries a string one.
        [<MethodImpl(MethodImplOptions.NoInlining)>]
        let tagOf (el: JVal) : string option = SeamCodec.tagOf el

        /// `SeamCodec.members`: the first member of `el` outside `known`, as a refusal.
        [<MethodImpl(MethodImplOptions.NoInlining)>]
        let members (where: string) (known: string list) (el: JVal) : Result<unit, DecodeError> =
            SeamCodec.members where known el

        /// `SeamCodec.within`: check the member `name` of `el`, where it is present.
        [<MethodImpl(MethodImplOptions.NoInlining)>]
        let within (name: string) (check: Decoder<unit>) (el: JVal) : Result<unit, DecodeError> =
            SeamCodec.within name check el

        /// `SeamCodec.each`: check every element of an array.
        [<MethodImpl(MethodImplOptions.NoInlining)>]
        let each (check: Decoder<unit>) (el: JVal) : Result<unit, DecodeError> = SeamCodec.each check el

    /// The keyed-map algebra, `KeyedRegistry`'s, and the policy's admission.
    module Registry =

        /// `KeyedRegistry.register`.
        [<MethodImpl(MethodImplOptions.NoInlining)>]
        let register
            (duplicate: string -> 'E)
            (admit: 'V -> 'E option)
            (id: string)
            (v: 'V)
            (m: Map<string, 'V>)
            : Result<Map<string, 'V>, 'E> =
            KeyedRegistry.register duplicate admit id v m

        /// `KeyedRegistry.unregister`.
        [<MethodImpl(MethodImplOptions.NoInlining)>]
        let unregister
            (unknown: string -> string list -> 'E)
            (id: string)
            (m: Map<string, 'V>)
            : Result<Map<string, 'V>, 'E> =
            KeyedRegistry.unregister unknown id m

        /// `KeyedRegistry.replace`.
        [<MethodImpl(MethodImplOptions.NoInlining)>]
        let replace
            (unknown: string -> string list -> 'E)
            (admit: 'V -> 'E option)
            (id: string)
            (v: 'V)
            (m: Map<string, 'V>)
            : Result<Map<string, 'V>, 'E> =
            KeyedRegistry.replace unknown admit id v m

        /// `KeyedRegistry.restrict`.
        [<MethodImpl(MethodImplOptions.NoInlining)>]
        let restrict (keep: Set<string>) (m: Map<string, 'V>) : Map<string, 'V> = KeyedRegistry.restrict keep m

        /// `KeyedRegistry.union`.
        [<MethodImpl(MethodImplOptions.NoInlining)>]
        let union (duplicate: string -> 'E) (a: Map<string, 'V>) (b: Map<string, 'V>) : Result<Map<string, 'V>, 'E> =
            KeyedRegistry.union duplicate a b

        /// `RegistryPolicy.admit`: run the gates for one invocation.
        [<MethodImpl(MethodImplOptions.NoInlining)>]
        let admit
            (refused: string -> RejectionGuidance -> 'E)
            (approval: string -> 'E)
            (p: RegistryPolicy<'Decl, 'Args>)
            (id: string)
            (decl: 'Decl)
            (args: 'Args)
            : Result<unit, 'E> =
            RegistryPolicy.admit refused approval p id decl args
