/// Phase 402 — no Core assembly reaches another's internal member from a body the F# optimiser may
/// copy into a third assembly (DECISIONS.md D129).
///
/// The defect this holds: in Release the F# optimiser inlines a public function's body into the
/// assembly that CALLS it, and it does not know that a member another assembly shares only through
/// `InternalsVisibleTo` is out of that caller's reach. `ConfRng.ofSeed` was one call to
/// `Fuaran.Core.Idl`'s internal `Xorshift32.seeded`, so every Release-built consumer of the
/// conformance kit failed with `MethodAccessException` — the first Release run of this suite found it
/// in nine families at once. The rule D129 adopts: a reference to another Core assembly's non-public
/// member sits only in a function marked `[<MethodImpl(MethodImplOptions.NoInlining)>]`, whose body is
/// never copied out of the assembly that declares it.
///
/// Read from the BUILT assemblies of the configuration under test, by decoding every method body's
/// IL and resolving each member, type and field operand: a site is any operand that resolves into
/// ANOTHER Core assembly and is not reachable from outside it. The rule is deliberately stricter than
/// "public functions only": a private helper with no attribute can be inlined, inside its own
/// assembly, into a public function that then carries the reference out, and that happens in Release
/// and not in Debug — so a check keyed on the helper's visibility would pass in one configuration and
/// not the other. Keyed on the attribute, it reads the same in both. A site inside a closure is red
/// too: a closure has no attribute to carry, so the remedy is a `NoInlining` forwarder taking the
/// target's full argument list.
module Fuaran.Core.Tests.CrossAssemblyInliningTests

open System
open System.Reflection
open System.Reflection.Emit
open System.Runtime.CompilerServices
open Expecto

/// One reference from a Core assembly to another Core assembly's non-public member.
type Site =
    {
        /// The assembly whose IL holds the reference.
        From: string
        /// The method holding it, `Type::Method`.
        Method: string
        /// Whether that method is marked `NoInlining`.
        Guarded: bool
        /// The member reached, as reflection prints it.
        Target: string
        /// The assembly that declares the member reached.
        Owner: string
    }

let private isCore (a: Assembly) : bool =
    a.GetName().Name.StartsWith("Fuaran.Core", StringComparison.Ordinal)

// The IL opcode table, read off `OpCodes` once: the one-byte opcodes by value and the two-byte
// (0xFE-prefixed) ones by their second byte.
let private opcodes: OpCode[] * OpCode[] =
    let one = Array.zeroCreate<OpCode> 256
    let two = Array.zeroCreate<OpCode> 256

    for f in typeof<OpCodes>.GetFields(BindingFlags.Public ||| BindingFlags.Static) do
        let op = f.GetValue null :?> OpCode
        let v = uint16 op.Value

        if v < 0x100us then
            one[int v] <- op
        elif v &&& 0xff00us = 0xfe00us then
            two[int (v &&& 0xffus)] <- op

    one, two

/// Whether `t` can be named from `from` without a friend grant — a type of `from` itself, a type
/// outside Core (not this check's business), or a visible Core type — and so can each of its generic
/// arguments: a public generic member instantiated at the caller's own internal type is the caller's
/// business, not a reach into another assembly.
let rec private reachable (from: Assembly) (t: Type) : bool =
    if t.IsGenericParameter then
        true
    elif t.HasElementType then
        reachable from (t.GetElementType())
    else
        // A constructed generic type is visible only when its arguments are, so the definition's own
        // visibility is read, and the arguments are judged below against `from`.
        let definition = if t.IsGenericType then t.GetGenericTypeDefinition() else t
        let own = t.Assembly = from || not (isCore t.Assembly) || definition.IsVisible

        own
        && (not t.IsGenericType || t.GetGenericArguments() |> Array.forall (reachable from))

/// Whether a resolved operand can be named from `from` without a friend grant.
let private memberReachable (from: Assembly) (m: MemberInfo) : bool =
    match m with
    | :? Type as t -> reachable from t
    | :? MethodBase as mb ->
        (mb.IsPublic || mb.IsFamily || mb.IsFamilyOrAssembly)
        && reachable from mb.DeclaringType
        && (not mb.IsGenericMethod
            || mb.GetGenericArguments() |> Array.forall (reachable from))
    | :? FieldInfo as fi -> fi.IsPublic && reachable from fi.DeclaringType
    | _ -> true

let private ownerOf (m: MemberInfo) : Assembly =
    match m with
    | :? Type as t -> t.Assembly
    | m -> m.DeclaringType.Assembly

let private declared =
    BindingFlags.Public
    ||| BindingFlags.NonPublic
    ||| BindingFlags.Instance
    ||| BindingFlags.Static
    ||| BindingFlags.DeclaredOnly

/// The member, type and field operands of `m`'s IL, resolved in `m`'s generic context. An operand that
/// does not resolve is skipped: every Core assembly's dependencies are beside this suite, so none
/// fails to here, and a resolution failure is no evidence of a cross-assembly reference.
let private operands (m: MethodBase) : MemberInfo list =
    match
        (try
            m.GetMethodBody()
         with _ ->
             null)
    with
    | null -> []
    | body ->
        let il = body.GetILAsByteArray()
        let one, two = opcodes

        let typeArgs =
            if m.DeclaringType.IsGenericType then
                m.DeclaringType.GetGenericArguments()
            else
                null

        let methodArgs = if m.IsGenericMethod then m.GetGenericArguments() else null

        let found = ResizeArray<MemberInfo>()
        let mutable i = 0

        while i < il.Length do
            let op =
                if il[i] = 0xFEuy then
                    i <- i + 2
                    two[int il[i - 1]]
                else
                    i <- i + 1
                    one[int il[i - 1]]

            let size =
                match op.OperandType with
                | OperandType.InlineNone -> 0
                | OperandType.ShortInlineBrTarget
                | OperandType.ShortInlineI
                | OperandType.ShortInlineVar -> 1
                | OperandType.InlineVar -> 2
                | OperandType.InlineI8
                | OperandType.InlineR -> 8
                | OperandType.InlineSwitch -> 4 + 4 * BitConverter.ToInt32(il, i)
                | _ -> 4

            match op.OperandType with
            | OperandType.InlineMethod
            | OperandType.InlineField
            | OperandType.InlineType
            | OperandType.InlineTok ->
                match
                    (try
                        Some(m.Module.ResolveMember(BitConverter.ToInt32(il, i), typeArgs, methodArgs))
                     with _ ->
                         None)
                with
                | Some target -> found.Add target
                | None -> ()
            | _ -> ()

            i <- i + size

        List.ofSeq found

/// Every reference `asm`'s IL makes to another Core assembly's non-public member.
let sitesIn (asm: Assembly) : Site list =
    let types =
        try
            asm.GetTypes()
        with :? ReflectionTypeLoadException as e ->
            e.Types |> Array.filter (isNull >> not)

    [ for t in types do
          let methods: MethodBase list =
              [ yield! t.GetMethods declared |> Seq.cast<MethodBase>
                yield! t.GetConstructors declared |> Seq.cast<MethodBase> ]

          for m in methods do
              // A type initialiser runs where it is declared and is never inlined anywhere.
              if not m.IsConstructor || not m.IsStatic then
                  for target in operands m do
                      let owner = ownerOf target

                      if owner <> asm && isCore owner && not (memberReachable asm target) then
                          { From = asm.GetName().Name
                            Method = t.FullName + "::" + m.Name
                            Guarded = m.MethodImplementationFlags.HasFlag MethodImplAttributes.NoInlining
                            Target = string target
                            Owner = owner.GetName().Name } ]
    |> List.distinct

/// The sites a guard predicate leaves unguarded — `Guarded` is the real one; the go-red test hands it
/// one that admits nothing, to show the check names every site when no guard stands.
let unguarded (guarded: Site -> bool) (sites: Site list) : Site list = sites |> List.filter (guarded >> not)

/// The shipped assemblies of the configuration under test, by package id. Errors name the package
/// whose assembly was not built, as the public-surface gate does.
let private shipped () : Result<(string * Assembly) list, string list> =
    let root = Snapshots.repoFile ""
    let projects = PackageRosterTests.packableProjects root

    let located =
        projects
        |> List.map (fun p -> p.PackageId, PublicSurfaceTests.assemblyFor root p.ProjectFile p.PackageId)

    match
        located
        |> List.choose (fun (_, r) ->
            match r with
            | Error e -> Some e
            | Ok _ -> None)
    with
    | [] ->
        Ok(
            located
            |> List.choose (fun (id, r) ->
                match r with
                | Ok dll -> Some(id, Assembly.LoadFrom dll)
                | Error _ -> None)
        )
    | errors -> Error errors

/// The friend grants between shipped assemblies: (grantor, friend) for every `InternalsVisibleTo`
/// one shipped assembly declares naming another.
let private grants (assemblies: (string * Assembly) list) : (string * string) list =
    let names = assemblies |> List.map fst |> Set.ofList

    [ for id, asm in assemblies do
          for a in asm.GetCustomAttributes<InternalsVisibleToAttribute>() do
              let friend = (a.AssemblyName.Split(',')[0]).Trim()

              if names.Contains friend then
                  id, friend ]
    |> List.distinct

let private describe (s: Site) : string =
    sprintf "%s: %s reaches %s's internal %s" s.From s.Method s.Owner s.Target

[<Tests>]
let tests =
    testList
        "CrossAssemblyInlining"
        [ test "no shipped assembly reaches another's internal member from a body that can be inlined (D129)" {
              match shipped () with
              | Error why -> failtestf "the shipped assemblies are not built:\n  %s" (String.concat "\n  " why)
              | Ok assemblies ->
                  let sites = assemblies |> List.collect (snd >> sitesIn)

                  match unguarded _.Guarded sites with
                  | [] -> ()
                  | bad ->
                      failtestf
                          "%d reference(s) to another Core assembly's internal member sit in a body the F# optimiser may copy into a caller's assembly, where the member is out of reach (MethodAccessException in a Release-built consumer). Move each into a function marked [<MethodImpl(MethodImplOptions.NoInlining)>] that takes the target's full argument list (DECISIONS.md D129):\n  %s"
                          bad.Length
                          (bad |> List.map describe |> String.concat "\n  ")
          }

          test "every friend grant between shipped assemblies is used, and only through guarded sites" {
              // The census is not vacuous: each `InternalsVisibleTo` one shipped assembly grants another
              // is read by at least one site, so a scan that stopped finding references (an opcode table
              // gone wrong, an operand kind dropped) goes red here rather than passing over nothing; and a
              // grant nothing reads is a widening of the internals with no reader, which is removed.
              match shipped () with
              | Error why -> failtestf "the shipped assemblies are not built:\n  %s" (String.concat "\n  " why)
              | Ok assemblies ->
                  let pairs = grants assemblies
                  Expect.isNonEmpty pairs "at least one shipped assembly grants another its internals"

                  let sites = assemblies |> List.collect (snd >> sitesIn)

                  let unread =
                      pairs
                      |> List.filter (fun (grantor, friend) ->
                          not (sites |> List.exists (fun s -> s.From = friend && s.Owner = grantor)))

                  Expect.isEmpty
                      unread
                      (sprintf
                          "these grants are read by no site — remove the grant, or the scan has stopped seeing references: %A"
                          unread)

                  let ungranted =
                      sites |> List.filter (fun s -> not (pairs |> List.contains (s.Owner, s.From)))

                  Expect.isEmpty
                      (ungranted |> List.map describe)
                      "every site is read through a declared grant between shipped assemblies"
          }

          test "the check names every site when no guard stands — it can go red" {
              match shipped () with
              | Error why -> failtestf "the shipped assemblies are not built:\n  %s" (String.concat "\n  " why)
              | Ok assemblies ->
                  let sites = assemblies |> List.collect (snd >> sitesIn)
                  Expect.isNonEmpty sites "the census finds the cross-assembly internal references"
                  Expect.equal (unguarded (fun _ -> false) sites) sites "an absent guard leaves every site named"

                  // The defect's own instance is in the census, guarded: the kernel the conformance kit
                  // draws through.
                  Expect.isTrue
                      (sites
                       |> List.exists (fun s ->
                           s.From = "Fuaran.Core.Conformance"
                           && s.Method = "Fuaran.Core.ConfRng::ofSeed"
                           && s.Guarded))
                      "ConfRng.ofSeed reaches Xorshift32.seeded, and carries the guard"
          } ]
