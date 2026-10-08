namespace Fuaran.Core.Idl

open System.Runtime.CompilerServices

/// The declaration lookups this package reads from `Fuaran.Core.Idl`'s internal `IdlLookup` (through
/// that package's `InternalsVisibleTo`), and since Phase 403 the field codec it reads from `Artifact`,
/// behind the one boundary every such read crosses.
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

    /// Phase 403 — a field list as `idl.json` renders it (`Artifact.fieldsJson`), so a projected
    /// record's declared fields read alike in the support document and the diff.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let fieldsJson (fs: IdlField list) : Fuaran.Core.JVal = Artifact.fieldsJson fs

    /// Phase 403 — one field object as `idl.json` reads it (`Artifact.readField`).
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let readField (v: Fuaran.Core.JVal) : Result<IdlField, Fuaran.Core.DecodeError> = Artifact.readField v
