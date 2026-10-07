namespace Fuaran.Core.Idl

open System.Runtime.CompilerServices

/// The declaration lookups this package reads from `Fuaran.Core.Idl`'s internal `IdlLookup` (through
/// that package's `InternalsVisibleTo`), behind the one boundary every such read crosses.
///
/// **Why each is `NoInlining` (DECISIONS.md D129).** The F# optimiser inlines a public function's body
/// into the assembly that CALLS it, and it does not know that a member another assembly shares only
/// through `InternalsVisibleTo` is out of that caller's reach. A body that names `IdlLookup` directly
/// and is inlined into a consumer built in Release fails there with `MethodAccessException`. So the
/// emitters never name `IdlLookup`: they call these, and a body marked `NoInlining` is never copied
/// into another assembly, so the internal reference stays in this one. `CrossAssemblyInliningTests`
/// fails on any reference to another Core assembly's internal member from a function without it.
module internal CodegenLookup =

    /// The enum declared as `name`.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let tryEnum (idl: Idl) (name: string) : IdlEnum option = IdlLookup.tryEnum idl name

    /// The union declared as `name`.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let tryUnion (idl: Idl) (name: string) : IdlUnion option = IdlLookup.tryUnion idl name

    /// The kind declared under the tag `tag`.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let tryKind (idl: Idl) (tag: string) : IdlKind option = IdlLookup.tryKind idl tag

    /// The record declared as `name`.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let tryRecord (idl: Idl) (name: string) : IdlRecord option = IdlLookup.tryRecord idl name
