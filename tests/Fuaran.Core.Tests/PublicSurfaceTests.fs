module Fuaran.Core.Tests.PublicSurfaceTests

// ---------------------------------------------------------------------------
// Phase 183 — a committed public-surface baseline per package, and the CLASS of a move
// computed rather than argued.
//
// The draft-slot rule asks one question of every commit that touches a package: does its
// public contract move, and if so in which class. On 2026-09-15 that question was answered
// twice by hand, in two workers' deviations records — "additive, and it advances because the
// slot is tagged"; "additive, rides the draft". Both were right; neither was checked, and
// neither could be: this repository had no surface baseline at all, so the only instrument
// was a reading of the diff.
//
// So: `api/<package>.txt`, one committed file per packable package, rendered from the built
// assembly's IL METADATA — the same source the pack-time guard reads, and the only
// one that sees what a consumer actually links against. A `PublicSurface` test family renders
// each package afresh and diffs it against its baseline; the diff is CLASSIFIED, and the test
// fails unless the commit also moved the baseline.
//
// **What this deliberately is NOT: a gate on the class.** Additive or breaking, a classified
// move passes. What is refused is an UNCLASSIFIED one — a surface that moved with its baseline
// standing still. The record-widening dispensation stands; the class line is what
// lets a reviewer apply it.
//
// ---- the six classes, and why the three that LOOK additive are not -------------------
//
// A naive differ calls a removed token breaking and an added token additive. That reading is
// what let the two shapes below occupy unchanged feed slots, so it is not the reading here:
//
//   removal             a baseline token with no counterpart — removed or renamed. Call sites
//                       stop resolving.
//   retype              the same member, rendered differently: a parameter or return type
//                       changed, or a record field's POSITION moved. Paired by identity, so it
//                       is reported as one move rather than as a removal beside an addition.
//   record-widening     a record field ADDED to a record the baseline published. Every
//                       full-literal construction stops compiling (FS0764) and the primary
//                       constructor widens.
//   union-widening      a case ADDED to a union the baseline published. Every exhaustive
//                       `match` becomes incomplete — and a consumer holding a stale
//                       same-version pack gets an `InvalidCastException` at run time instead,
//                       with no compile signal at all.
//   interface-widening  a member ADDED to an interface the baseline published. Every
//                       implementer stops compiling.
//   additive            genuinely additive growth: a new type, a new module function, a new
//                       member on a class — and a field or case on a type the baseline never
//                       published, which nobody could have constructed or matched.
//
// The middle three are read off the `CompilationMappingAttribute` the F# compiler already
// emits, which is what makes them computable at all: a union case addition REMOVES NOTHING, so
// a removal-only differ sees ordinary growth.
//
// ---- what it does not claim ---------------------------------------------------------
//
//   * SEMANTICS. A function whose signature is unchanged and whose behaviour reversed is
//     invisible here and always will be.
//   * The Fable SOURCE half of a package. Every packable project here also ships its `.fs`
//     sources under `fable/` for a Fable consumer to compile; this renders the managed
//     assembly, which is the .NET consumer's contract. A source-only change that is
//     Fable-visible and managed-invisible is not a shape F# can produce, but the boundary is
//     named rather than assumed.
//   * INTERNAL members, which cannot break a consumer and are excluded from the surface.
//
// ---- regenerating ------------------------------------------------------------------
//
//   CORE_APPROVE_API=1 dotnet run --project tests/Fuaran.Core.Tests            (every baseline)
//   CORE_APPROVE_API=Fuaran.Core.Wire dotnet run --project tests/Fuaran.Core.Tests   (one package id, or a comma list)
//
// The value is read by Approval.fs (Phase 396): 1/true is every baseline, a package id is that one. The
// forge precedent, and its hazard is the forge one too: the bare form rewrites EVERY baseline, not
// the one you were looking at, so an unrelated drift sitting in the tree lands in your commit
// silently. Stage the baselines you meant to move BY NAME and read the rest back out.
// ---------------------------------------------------------------------------

open System
open System.Collections.Immutable
open System.IO
open System.Reflection
open System.Reflection.Metadata
open System.Reflection.PortableExecutable
open System.Text
open Expecto

// ---- probe types: what the renderer is pinned against ---------------------
//
// Declared HERE, in the test assembly, so the renderer's output FORMAT has a case whose
// expected text is known in advance. Every other leg reads a real package's surface and
// compares it with a file generated by this same renderer — a pairing that would agree
// perfectly with itself if the renderer emitted nothing at all.

/// A record with two fields, in declaration order — the `record-field` token's shape and its
/// `#seq`.
type ProbeRecord = { Alpha: int; Beta: string }

/// A union with a nullary case and a carrying one — the `union-case` token's shape and `#tag`.
type ProbeUnion =
    | ProbeEmpty
    | ProbeCarrying of int * string

/// A single-case union with a NAMED field — its field property sits on the union type itself
/// and carries the two-argument `(Field, seq)` mapping (Phase 237).
type ProbeSingle = ProbeSingle of value: int

/// A struct union — its fields sit on the union type itself too, each carrying
/// `(Field, variant, seq)` (Phase 237).
[<Struct>]
type ProbeStructUnion =
    | ProbeStructA of a: int
    | ProbeStructB of b: string * c: int

/// Phase 237's go-red fixture pair, and its control. Three modules, each declaring the same
/// union; `Before` and `After` differ ONLY by one field's name, `Same` by nothing. Rendered from
/// this assembly's own IL, so the pair is a real compiled pair rather than two edited strings.
module ProbeFieldsBefore =
    type Shape =
        | Empty
        | Held of parent: int * kindTag: string

module ProbeFieldsAfter =
    type Shape =
        | Empty
        | Held of target: int * kindTag: string

module ProbeFieldsSame =
    type Shape =
        | Empty
        | Held of parent: int * kindTag: string

/// An interface — the `interface-marker` token, which is what lets the classifier call a
/// member added to a published interface breaking without re-reading the assembly.
type IProbeSeam =
    abstract Probe: int -> string

/// Phase 406's go-red pair, and its control: one union, bare in `Before` and `Same`, qualified in
/// `After`. Rendered from this assembly's own IL, so the pair is a real compiled pair.
module ProbeQualifyBefore =
    type Shape =
        | Ring
        | Disc of radius: int

module ProbeQualifyAfter =
    [<RequireQualifiedAccess>]
    type Shape =
        | Ring
        | Disc of radius: int

module ProbeQualifySame =
    type Shape =
        | Ring
        | Disc of radius: int

/// One probe per drawn attribute (Phase 406) — each must render its `attribute` line.
[<RequireQualifiedAccess>]
type ProbeQualifiedRecord = { Gamma: int }

[<RequireQualifiedAccess>]
module ProbeQualifiedModule =
    let probeQualified = 1

[<AutoOpen>]
module ProbeAutoOpenModule =
    let probeAutoOpened = 1

[<NoEquality; NoComparison>]
type ProbeNoEquality = { Delta: int -> int }

[<NoComparison>]
type ProbeNoComparison = { Epsilon: int }

[<ReferenceEquality>]
type ProbeReferenceEquality = { Zeta: int }

[<AllowNullLiteral>]
type ProbeNullable() =
    member _.Eta = 1

[<Sealed>]
type ProbeSealed() =
    member _.Theta = 1

[<AbstractClass>]
type ProbeAbstract() =
    abstract Iota: int

[<Measure>]
type probeUnit

[<Struct>]
type ProbeStructRecord = { Kappa: int }

/// The surface the renderer refuses rather than draws (Phase 406): a `ParamArray` parameter and an
/// F# optional argument, each of which changes which calls compile.
type ProbeRefusedSurface() =
    static member Spread([<ParamArray>] xs: int[]) = xs.Length
    static member Maybe(?x: int) = defaultArg x 0

// ---- the signature type provider ------------------------------------------
//
// `MetadataReader` hands signatures back as blobs; `DecodeSignature` walks one given a
// provider that names each type it meets. Rendering to STRING rather than to a reflection
// type is what keeps this resolution-free — a parameter typed from another assembly is named
// from its TypeReference row, with no need for that assembly to be loadable. That matters:
// several packable projects are not referenced by this test project at all, and
// `MetadataLoadContext` would need their whole dependency closure resolvable on disk.

type private SurfaceTypeProvider() =

    member private this.DefinitionName(r: MetadataReader, h: TypeDefinitionHandle) : string =
        let td = r.GetTypeDefinition h
        let name = r.GetString td.Name

        if td.IsNested then
            this.DefinitionName(r, td.GetDeclaringType()) + "+" + name
        else
            let ns = r.GetString td.Namespace
            if String.IsNullOrEmpty ns then name else ns + "." + name

    member private this.ReferenceName(r: MetadataReader, h: TypeReferenceHandle) : string =
        let tr = r.GetTypeReference h
        let name = r.GetString tr.Name

        if tr.ResolutionScope.Kind = HandleKind.TypeReference then
            let outer = TypeReferenceHandle.op_Explicit tr.ResolutionScope
            this.ReferenceName(r, outer) + "+" + name
        else
            let ns = r.GetString tr.Namespace
            if String.IsNullOrEmpty ns then name else ns + "." + name

    interface ISZArrayTypeProvider<string> with
        member _.GetSZArrayType(elementType) = elementType + "[]"

    interface ISimpleTypeProvider<string> with
        member _.GetPrimitiveType(code) = "System." + string code

        member this.GetTypeFromDefinition(r, handle, _rawTypeKind) = this.DefinitionName(r, handle)

        member this.GetTypeFromReference(r, handle, _rawTypeKind) = this.ReferenceName(r, handle)

    interface IConstructedTypeProvider<string> with
        member _.GetGenericInstantiation(genericType, typeArguments) =
            genericType + "<" + String.Join(", ", typeArguments) + ">"

        member _.GetArrayType(elementType, shape) =
            elementType + "[" + String.replicate (max 0 (shape.Rank - 1)) "," + "]"

        member _.GetByReferenceType(elementType) = elementType + "&"
        member _.GetPointerType(elementType) = elementType + "*"

    interface ISignatureTypeProvider<string, obj> with
        member _.GetFunctionPointerType(_signature) = "System.IntPtr(fnptr)"
        member _.GetGenericMethodParameter(_genericContext, index) = "!!" + string index
        member _.GetGenericTypeParameter(_genericContext, index) = "!" + string index

        member _.GetModifiedType(_modifier, unmodifiedType, _isRequired) = unmodifiedType

        member _.GetPinnedType(elementType) = elementType + " pinned"

        member this.GetTypeFromSpecification(r, genericContext, handle, _rawTypeKind) =
            let ts = r.GetTypeSpecification handle
            ts.DecodeSignature(this :> ISignatureTypeProvider<string, obj>, genericContext)

// ---- attribute decoding ---------------------------------------------------

/// The F# compiler stamps `CompilationMappingAttribute` on every construct it lowers, and all
/// three overloads take nothing but `Int32`s — `(flags)`, `(flags, seq)`,
/// `(flags, variant, seq)`. A custom-attribute blob for an Int32-only constructor has a fixed
/// layout (2-byte prolog, the fixed args, a 2-byte named-argument count), so it decodes
/// without the constructor's own signature. `None` when the blob is not that shape.
let private mappingInts (r: MetadataReader) (attrs: CustomAttributeHandleCollection) : int list option =
    let attributeName (ca: CustomAttribute) : string =
        try
            let ctor = ca.Constructor

            if ctor.Kind = HandleKind.MemberReference then
                let mr = r.GetMemberReference(MemberReferenceHandle.op_Explicit ctor)
                let parent = mr.Parent

                if parent.Kind = HandleKind.TypeReference then
                    r.GetString((r.GetTypeReference(TypeReferenceHandle.op_Explicit parent)).Name)
                elif parent.Kind = HandleKind.TypeDefinition then
                    r.GetString((r.GetTypeDefinition(TypeDefinitionHandle.op_Explicit parent)).Name)
                else
                    ""
            elif ctor.Kind = HandleKind.MethodDefinition then
                let md = r.GetMethodDefinition(MethodDefinitionHandle.op_Explicit ctor)
                r.GetString((r.GetTypeDefinition(md.GetDeclaringType())).Name)
            else
                ""
        with _ ->
            ""

    attrs
    |> Seq.tryPick (fun ah ->
        try
            let ca = r.GetCustomAttribute ah

            if attributeName ca <> "CompilationMappingAttribute" then
                None
            else
                let bytes = r.GetBlobBytes ca.Value

                if bytes.Length < 8 || bytes[0] <> 1uy || bytes[1] <> 0uy then
                    None
                else
                    let count = (bytes.Length - 4) / 4

                    if count < 1 then
                        None
                    else
                        Some [ for i in 0 .. count - 1 -> BitConverter.ToInt32(bytes, 2 + (i * 4)) ]
        with _ ->
            None)

let internal hasAttribute (r: MetadataReader) (attrs: CustomAttributeHandleCollection) (name: string) : bool =
    let attributeName (ca: CustomAttribute) : string =
        try
            let ctor = ca.Constructor

            if ctor.Kind = HandleKind.MemberReference then
                let mr = r.GetMemberReference(MemberReferenceHandle.op_Explicit ctor)
                let parent = mr.Parent

                if parent.Kind = HandleKind.TypeReference then
                    r.GetString((r.GetTypeReference(TypeReferenceHandle.op_Explicit parent)).Name)
                elif parent.Kind = HandleKind.TypeDefinition then
                    r.GetString((r.GetTypeDefinition(TypeDefinitionHandle.op_Explicit parent)).Name)
                else
                    ""
            elif ctor.Kind = HandleKind.MethodDefinition then
                let md = r.GetMethodDefinition(MethodDefinitionHandle.op_Explicit ctor)
                r.GetString((r.GetTypeDefinition(md.GetDeclaringType())).Name)
            else
                ""
        with _ ->
            ""

    attrs
    |> Seq.exists (fun ah ->
        try
            attributeName (r.GetCustomAttribute ah) = name
        with _ ->
            false)

// ---- consumer-visible attributes (Phase 406) --------------------------------
//
// Phase 386 put `[<RequireQualifiedAccess>]` on six public unions, and this gate printed nothing:
// the renderer drew a type by its kind and its members, never by the attributes the F# compiler
// reads off it to decide what consumer source is LEGAL. Qualifying a union breaks every consumer
// that names a case bare, and the class report read `unchanged`. So a type now renders one
// `attribute <type> <Name>` line per attribute below that it carries, and a move of one of those
// lines on a type that stays published is a `retype` with its reason printed.
//
// The rule for what is drawn: an attribute is drawn when adding or removing it changes which
// consumer source compiles. What is NOT drawn, and why (DECISIONS.md D134):
//
//   CustomEquality / CustomComparison      the type still satisfies `equality` / `comparison`;
//                                          what changes is the RESULT of `=`, a behaviour move.
//   StructuralEquality / StructuralComparison   assert the default; a type that cannot satisfy
//                                          them fails at its own definition, not at a consumer.
//   CompilationRepresentation(ModuleSuffix) the suffix is in the type's IL name, so the `type`
//                                          token already moves; F# source does not change.
//   member- and parameter-level attributes that change call syntax or compiled shape — see
//                                          `undrawnSurfaceAttributes`, which REFUSES them rather
//                                          than leaving them unseen.

/// The namespace-qualified name of a custom attribute's type — `""` when it cannot be read.
let private attributeQualifiedName (r: MetadataReader) (ca: CustomAttribute) : string =
    try
        let ctor = ca.Constructor

        let ofReference (h: TypeReferenceHandle) =
            let tr = r.GetTypeReference h
            r.GetString tr.Namespace + "." + r.GetString tr.Name

        let ofDefinition (h: TypeDefinitionHandle) =
            let td = r.GetTypeDefinition h
            r.GetString td.Namespace + "." + r.GetString td.Name

        if ctor.Kind = HandleKind.MemberReference then
            let parent = (r.GetMemberReference(MemberReferenceHandle.op_Explicit ctor)).Parent

            if parent.Kind = HandleKind.TypeReference then
                ofReference (TypeReferenceHandle.op_Explicit parent)
            elif parent.Kind = HandleKind.TypeDefinition then
                ofDefinition (TypeDefinitionHandle.op_Explicit parent)
            else
                ""
        elif ctor.Kind = HandleKind.MethodDefinition then
            ofDefinition ((r.GetMethodDefinition(MethodDefinitionHandle.op_Explicit ctor)).GetDeclaringType())
        else
            ""
    with _ ->
        ""

/// One attribute the renderer draws: the name its `attribute` line carries, and what a consumer
/// gains or loses when it moves — printed beside the move, so a reviewer reads the cost rather
/// than reconstructing it.
type internal DrawnAttribute = { Name: string; Reason: string }

/// The FSharp.Core attributes drawn off a type, keyed by their attribute type's qualified name.
let internal drawnTypeAttributes: (string * DrawnAttribute) list =
    let fsharpCore (name: string) (reason: string) =
        "Microsoft.FSharp.Core." + name + "Attribute", { Name = name; Reason = reason }

    [ fsharpCore
          "RequireQualifiedAccess"
          "a case, field or member must be named through its type or module, and a qualified module cannot be opened — adding it breaks every bare use; removing it lets a bare name shadow another in the consumer's scope"
      fsharpCore
          "AutoOpen"
          "the module is opened wherever its enclosing namespace or module is — adding it can shadow a consumer's own names; removing it breaks every use that relied on it"
      fsharpCore "NoEquality" "`=`, `<>`, `hash` and every `equality`-constrained use refuse the type"
      fsharpCore
          "NoComparison"
          "`compare`, `<`, `max`, a `Set` element or `Map` key and every `comparison`-constrained use refuse the type"
      fsharpCore "ReferenceEquality" "equality is identity and comparison is refused"
      fsharpCore "AllowNullLiteral" "`null` is a value of the type a consumer may write"
      fsharpCore "Sealed" "a consumer may not inherit the type"
      fsharpCore "AbstractClass" "a consumer may not construct the type, only inherit it"
      fsharpCore "Measure" "the type is a unit of measure, written only in measure position" ]

/// `Struct` is drawn off the IL rather than an attribute: a type whose base is `System.ValueType`.
let internal structAttribute: DrawnAttribute =
    { Name = "Struct"
      Reason =
        "the type is a value type — no `null`, copy semantics, a default value, and a different binary layout a consumer links against" }

/// The reason printed beside a moved `attribute` line, by the name the line carries.
let internal attributeReason (name: string) : string option =
    (structAttribute :: List.map snd drawnTypeAttributes)
    |> List.tryFind (fun a -> a.Name = name)
    |> Option.map _.Reason

/// The drawn attributes a type carries, by the name its `attribute` line carries, ordinal-sorted.
let private typeAttributeNames (r: MetadataReader) (td: TypeDefinition) : string list =
    let carried =
        td.GetCustomAttributes()
        |> Seq.choose (fun ah ->
            let q = attributeQualifiedName r (r.GetCustomAttribute ah)

            drawnTypeAttributes
            |> List.tryFind (fun (k, _) -> k = q)
            |> Option.map (snd >> _.Name))
        |> List.ofSeq

    let isValueType =
        let bt = td.BaseType

        not bt.IsNil
        && bt.Kind = HandleKind.TypeReference
        && (let tr = r.GetTypeReference(TypeReferenceHandle.op_Explicit bt)
            r.GetString tr.Namespace = "System" && r.GetString tr.Name = "ValueType")

    (if isValueType then
         structAttribute.Name :: carried
     else
         carried)
    |> List.distinct
    |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

// ---- the renderer ---------------------------------------------------------

[<Literal>]
let private SourceConstructMask = 0x1F

[<Literal>]
let private SourceConstructSumType = 1

[<Literal>]
let private SourceConstructRecordType = 2

[<Literal>]
let private SourceConstructField = 4

[<Literal>]
let private SourceConstructModule = 7

[<Literal>]
let private SourceConstructUnionCase = 8

let private visibleMethodAccess =
    set
        [ MethodAttributes.Public
          MethodAttributes.Family
          MethodAttributes.FamORAssem ]

/// The rendered public contract surface of one managed assembly, ordinal-sorted.
///
/// "Public surface" is what a consumer in ANOTHER assembly can reach: public and protected
/// members of externally-visible types. Internal members are excluded — they cannot break a
/// consumer. Compiler-generated members are excluded too, with ONE deliberate carve-out: a DU
/// case factory carries BOTH `CompilerGeneratedAttribute` and
/// `CompilationMappingAttribute(UnionCase)`, and dropping it would discard the one signal the
/// union-widening class turns on.
let internal renderAssembly (dllPath: string) : string list =
    let provider = SurfaceTypeProvider()
    let sigProvider = provider :> ISignatureTypeProvider<string, obj>
    let tokens = ResizeArray<string>()

    use stream = File.OpenRead dllPath
    use pe = new PEReader(stream)

    if not pe.HasMetadata then
        []
    else
        let r = pe.GetMetadataReader()

        // Full name with '+' between nesting levels, matching the provider's rendering, so a
        // type named in a signature and the same type's own header agree.
        let rec fullName (th: TypeDefinitionHandle) : string =
            let td = r.GetTypeDefinition th
            let name = r.GetString td.Name

            if td.IsNested then
                fullName (td.GetDeclaringType()) + "+" + name
            else
                let ns = r.GetString td.Namespace
                if String.IsNullOrEmpty ns then name else ns + "." + name

        // Externally visible = public at EVERY level of nesting. A public type nested in an
        // internal one is unreachable and must not be rendered.
        let rec visibleType (th: TypeDefinitionHandle) : bool =
            let td = r.GetTypeDefinition th
            let vis = td.Attributes &&& TypeAttributes.VisibilityMask

            if td.IsNested then
                (vis = TypeAttributes.NestedPublic
                 || vis = TypeAttributes.NestedFamily
                 || vis = TypeAttributes.NestedFamORAssem)
                && visibleType (td.GetDeclaringType())
            else
                vis = TypeAttributes.Public

        // Phase 237 — the FIELD NAMES of every union case, keyed by (union, tag).
        //
        // A case's factory signature carries its field TYPES only, and its parameter names
        // are a lossy spelling of the field names (`target` is emitted as `_target`), so they
        // are not read. The names come from the properties the compiler emits for each field,
        // which carry `CompilationMappingAttribute(Field, variant, seq)`: on the case's nested
        // class for a multi-case reference union, on the union type itself for a single-case
        // or a struct union. The (Field, seq) two-argument form is the single-case shape, and
        // its variant is 0. What the renderer does with a case whose fields it could not name
        // is the conservative thing — it renders the types alone — and the guard test
        // "every carrying union case renders its field names" turns that into a failure.
        let caseFieldNames =
            Collections.Generic.Dictionary<struct (string * int), ResizeArray<int * string>>()

        let isUnion (th: TypeDefinitionHandle) =
            match mappingInts r ((r.GetTypeDefinition th).GetCustomAttributes()) with
            | Some(flags :: _) -> (flags &&& SourceConstructMask) = SourceConstructSumType
            | _ -> false

        for th in r.TypeDefinitions do
            let td = r.GetTypeDefinition th

            let unionOwner =
                if isUnion th then
                    Some th
                elif td.IsNested && isUnion (td.GetDeclaringType()) then
                    Some(td.GetDeclaringType())
                else
                    None

            match unionOwner with
            | None -> ()
            | Some u ->
                for ph in td.GetProperties() do
                    let pd = r.GetPropertyDefinition ph

                    match mappingInts r (pd.GetCustomAttributes()) with
                    | Some(flags :: rest) when (flags &&& SourceConstructMask) = SourceConstructField ->
                        let variant, seq =
                            match rest with
                            | [ v; s ] -> v, s
                            | [ s ] -> 0, s
                            | _ -> -1, -1

                        if variant >= 0 then
                            let key = struct (fullName u, variant)

                            match caseFieldNames.TryGetValue key with
                            | true, xs -> xs.Add((seq, r.GetString pd.Name))
                            | _ -> caseFieldNames[key] <- ResizeArray [ (seq, r.GetString pd.Name) ]
                    | _ -> ()

        /// The factory's parameter list, each type prefixed with its field name when the
        /// case's fields were all named — `target: !0, kindTag: System.String`.
        ///
        /// Measured, not assumed: a multi-case union's field is reached TWICE by the pass
        /// above, once on the case class and once on the union type, both rows naming the same
        /// (variant, seq). Hence `distinct` — and a genuine disagreement (two names at one
        /// position) still fails the count below and falls back, which the guard test catches.
        let caseParameters (union: string) (tag: int) (types: ImmutableArray<string>) : string =
            let named =
                match caseFieldNames.TryGetValue(struct (union, tag)) with
                | true, xs ->
                    let names = xs |> Seq.distinct |> Seq.sortBy fst |> Seq.map snd |> List.ofSeq

                    if names.Length = types.Length then
                        Some(List.map2 (fun n t -> n + ": " + t) names (List.ofSeq types))
                    else
                        None
                | _ -> None

            match named with
            | Some ps -> String.Join(", ", ps)
            | None -> String.Join(", ", types)

        for th in r.TypeDefinitions do
            let td = r.GetTypeDefinition th

            // Closures, startup classes and other lowered artefacts. F# spells them with '@'
            // or '<'; neither can occur in a name a consumer writes.
            let full = fullName th

            if
                visibleType th
                && not (full.Contains '@')
                && not (full.Contains '<')
                && not (hasAttribute r (td.GetCustomAttributes()) "CompilerGeneratedAttribute")
            then
                let construct =
                    match mappingInts r (td.GetCustomAttributes()) with
                    | Some(flags :: _) -> flags &&& SourceConstructMask
                    | _ -> -1

                let isInterface = td.Attributes.HasFlag TypeAttributes.Interface

                let kind =
                    if construct = SourceConstructRecordType then "record"
                    elif construct = SourceConstructSumType then "union"
                    elif construct = SourceConstructModule then "module"
                    elif isInterface then "interface"
                    else "type"

                tokens.Add(sprintf "type %s (%s)" full kind)

                // Phase 406 — the attributes that decide what consumer source is legal.
                for a in typeAttributeNames r td do
                    tokens.Add(sprintf "attribute %s %s" full a)

                // -- methods, constructors and DU case factories --
                for mh in td.GetMethods() do
                    try
                        let md = r.GetMethodDefinition mh
                        let access = md.Attributes &&& MethodAttributes.MemberAccessMask

                        if visibleMethodAccess.Contains access then
                            let name = r.GetString md.Name
                            let attrs = md.GetCustomAttributes()
                            let map = mappingInts r attrs

                            let isUnionCase =
                                match map with
                                | Some(flags :: _) -> (flags &&& SourceConstructMask) = SourceConstructUnionCase
                                | _ -> false

                            let skip =
                                not isUnionCase
                                && (hasAttribute r attrs "CompilerGeneratedAttribute"
                                    || (md.Attributes.HasFlag MethodAttributes.SpecialName
                                        && (name.StartsWith("get_", StringComparison.Ordinal)
                                            || name.StartsWith("set_", StringComparison.Ordinal)
                                            || name.StartsWith("add_", StringComparison.Ordinal)
                                            || name.StartsWith("remove_", StringComparison.Ordinal))))

                            if not skip then
                                let signature = md.DecodeSignature(sigProvider, null)
                                let ps = String.Join(", ", signature.ParameterTypes)

                                let generic =
                                    if signature.GenericParameterCount > 0 then
                                        "`" + string signature.GenericParameterCount
                                    else
                                        ""

                                if isUnionCase then
                                    let tag =
                                        match map with
                                        | Some ints when ints.Length >= 2 -> List.last ints
                                        | _ -> 0

                                    // A NULLARY case is emitted as a static PROPERTY whose
                                    // GETTER carries the mapping attribute — the property
                                    // itself carries only `CompilerGenerated`, so the getter
                                    // is the only row that names the case at all. Strip the
                                    // accessor prefix so the token names the CASE: no F#
                                    // union case can be spelled `get_…` (a case name must
                                    // start upper-case), so this can never shadow a real one.
                                    let caseName =
                                        if name.StartsWith("get_", StringComparison.Ordinal) then
                                            name.Substring 4
                                        else
                                            name

                                    tokens.Add(
                                        sprintf
                                            "union-case %s.%s #%d(%s)"
                                            full
                                            caseName
                                            tag
                                            (caseParameters full tag signature.ParameterTypes)
                                    )
                                elif name = ".ctor" then
                                    tokens.Add(sprintf "ctor %s..ctor(%s)" full ps)
                                else
                                    tokens.Add(
                                        sprintf "method %s.%s%s(%s) : %s" full name generic ps signature.ReturnType
                                    )
                    with e ->
                        // A member this reader cannot decode is skipped rather than fatal:
                        // an unreadable row must not take the whole baseline down with it.
                        // It surfaces as a MISSING token at the next diff, which is the
                        // conservative direction.
                        eprintfn "surface: method skipped in %s — %s" full e.Message

                // -- properties: record fields, nullary DU cases, ordinary properties --
                for ph in td.GetProperties() do
                    try
                        let pd = r.GetPropertyDefinition ph
                        let name = r.GetString pd.Name
                        let accessors = pd.GetAccessors()

                        let visibleParts =
                            [ "get", accessors.Getter; "set", accessors.Setter ]
                            |> List.filter (fun (_, h) -> not h.IsNil)
                            |> List.filter (fun (_, h) ->
                                let am = r.GetMethodDefinition h
                                visibleMethodAccess.Contains(am.Attributes &&& MethodAttributes.MemberAccessMask))
                            |> List.map fst

                        if not visibleParts.IsEmpty then
                            let attrs = pd.GetCustomAttributes()
                            let map = mappingInts r attrs

                            let construct2 =
                                match map with
                                | Some(flags :: _) -> flags &&& SourceConstructMask
                                | _ -> -1

                            let isRecordField = kind = "record" && construct2 = SourceConstructField

                            let isUnionCase = construct2 = SourceConstructUnionCase

                            let skip =
                                not (isRecordField || isUnionCase)
                                && hasAttribute r attrs "CompilerGeneratedAttribute"

                            if not skip then
                                let ty = pd.DecodeSignature(sigProvider, null).ReturnType

                                let ordinal =
                                    match map with
                                    | Some ints when ints.Length >= 2 -> List.last ints
                                    | _ -> 0

                                if isRecordField then
                                    tokens.Add(sprintf "record-field %s.%s #%d : %s" full name ordinal ty)
                                elif isUnionCase then
                                    tokens.Add(sprintf "union-case %s.%s #%d()" full name ordinal)
                                else
                                    tokens.Add(
                                        sprintf
                                            "property %s.%s : %s { %s }"
                                            full
                                            name
                                            ty
                                            (String.concat "; " visibleParts)
                                    )
                    with e ->
                        eprintfn "surface: property skipped in %s — %s" full e.Message

                // -- fields --
                for fh in td.GetFields() do
                    try
                        let fd = r.GetFieldDefinition fh
                        let access = fd.Attributes &&& FieldAttributes.FieldAccessMask

                        let visible =
                            access = FieldAttributes.Public
                            || access = FieldAttributes.Family
                            || access = FieldAttributes.FamORAssem

                        let name = r.GetString fd.Name

                        if
                            visible
                            && not (name.Contains '@')
                            && not (hasAttribute r (fd.GetCustomAttributes()) "CompilerGeneratedAttribute")
                        then
                            let ty = fd.DecodeSignature(sigProvider, null)

                            let literal =
                                if fd.Attributes.HasFlag FieldAttributes.Literal then
                                    " (literal)"
                                else
                                    ""

                            tokens.Add(sprintf "field %s.%s : %s%s" full name ty literal)
                    with e ->
                        eprintfn "surface: field skipped in %s — %s" full e.Message

                if isInterface then
                    // Recorded so the differ can classify a member ADDED to this type as
                    // breaking without re-reading the assembly: every implementer of an
                    // interface stops compiling when the interface grows a member.
                    tokens.Add(sprintf "interface-marker %s" full)

        tokens |> List.ofSeq |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

// ---- the attributes the renderer refuses rather than draws (Phase 406) ---------
//
// Some attributes change what a consumer may write at a MEMBER or a PARAMETER: `ParamArray` and
// `[<Optional>]` / `?arg` change which calls compile, `[<Extension>]` admits `x.M()`,
// `CompilerMessage` can turn a use into an error, `RequiresExplicitTypeArguments` demands `f<T>`,
// and `CompilationRepresentation(Static | Instance | UseNullAsTrueValue)` moves a member between
// instance and static in IL, which the `method` token does not distinguish. None of them is used on
// the shipped surface. Drawing them would need a per-overload identity the token grammar does not
// have, for a shape nothing ships; leaving them unseen would be the blindness this phase removes.
// So the surface REFUSES them: the family below goes red the day one appears, naming it, and the
// renderer is taught to draw it then (DECISIONS.md D134).

/// The value of `CompilationRepresentationFlags.ModuleSuffix`, the one flag the surface admits —
/// it renames the module in IL, which the `type` token already shows.
[<Literal>]
let private ModuleSuffixFlag = 4

/// The attributes the surface refuses, by their attribute type's qualified name.
let internal refusedSurfaceAttributes: string list =
    [ "System.ParamArrayAttribute"
      "System.Runtime.CompilerServices.ExtensionAttribute"
      "System.Runtime.InteropServices.OptionalAttribute"
      "Microsoft.FSharp.Core.OptionalArgumentAttribute"
      "Microsoft.FSharp.Core.CompilerMessageAttribute"
      "Microsoft.FSharp.Core.RequiresExplicitTypeArgumentsAttribute" ]

/// Every refused attribute on the externally-visible surface of an assembly, one line per
/// carrier: `<type>[.<member>[(<parameter>)]]: <attribute>`. Empty is the only admitted answer.
let internal undrawnSurfaceAttributes (dllPath: string) : string list =
    use stream = File.OpenRead dllPath
    use pe = new PEReader(stream)

    if not pe.HasMetadata then
        []
    else
        let r = pe.GetMetadataReader()

        let rec visibleType (th: TypeDefinitionHandle) : bool =
            let td = r.GetTypeDefinition th
            let vis = td.Attributes &&& TypeAttributes.VisibilityMask

            if td.IsNested then
                (vis = TypeAttributes.NestedPublic
                 || vis = TypeAttributes.NestedFamily
                 || vis = TypeAttributes.NestedFamORAssem)
                && visibleType (td.GetDeclaringType())
            else
                vis = TypeAttributes.Public

        let rec fullName (th: TypeDefinitionHandle) : string =
            let td = r.GetTypeDefinition th
            let name = r.GetString td.Name

            if td.IsNested then
                fullName (td.GetDeclaringType()) + "+" + name
            else
                let ns = r.GetString td.Namespace
                if String.IsNullOrEmpty ns then name else ns + "." + name

        let refused (attrs: CustomAttributeHandleCollection) : string list =
            [ for ah in attrs do
                  let ca = r.GetCustomAttribute ah
                  let q = attributeQualifiedName r ca

                  if List.contains q refusedSurfaceAttributes then
                      yield q
                  elif q = "Microsoft.FSharp.Core.CompilationRepresentationAttribute" then
                      let bytes = r.GetBlobBytes ca.Value

                      let flags =
                          if bytes.Length >= 6 then
                              BitConverter.ToInt32(bytes, 2)
                          else
                              -1

                      if flags <> ModuleSuffixFlag then
                          yield sprintf "%s(%d)" q flags ]

        [ for th in r.TypeDefinitions do
              if visibleType th then
                  let td = r.GetTypeDefinition th
                  let full = fullName th

                  for a in refused (td.GetCustomAttributes()) do
                      yield sprintf "%s: %s" full a

                  for mh in td.GetMethods() do
                      let md = r.GetMethodDefinition mh

                      if visibleMethodAccess.Contains(md.Attributes &&& MethodAttributes.MemberAccessMask) then
                          let name = r.GetString md.Name

                          for a in refused (md.GetCustomAttributes()) do
                              yield sprintf "%s.%s: %s" full name a

                          for ph in md.GetParameters() do
                              let p = r.GetParameter ph

                              for a in refused (p.GetCustomAttributes()) do
                                  yield sprintf "%s.%s(%s): %s" full name (r.GetString p.Name) a

                  for ph in td.GetProperties() do
                      let pd = r.GetPropertyDefinition ph

                      for a in refused (pd.GetCustomAttributes()) do
                          yield sprintf "%s.%s: %s" full (r.GetString pd.Name) a ]
        |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

// ---- the baseline file ----------------------------------------------------

/// The header every baseline carries. Static apart from the package id — a timestamp or a
/// version here would make every regeneration a diff.
let internal baselineHeader (packageId: string) : string list =
    [ sprintf "# %s — public contract surface (Phase 183)." packageId
      "# GENERATED from the built assembly's IL metadata. Do not hand-edit: regenerate with"
      "#   CORE_APPROVE_API=1 dotnet run --project tests/Fuaran.Core.Tests"
      "# and stage the baselines you meant to move BY NAME — the switch rewrites them all."
      "# One ordinal-sorted line per externally-visible type, member, record field and union case."
      "# See STABILITY.md \"Public-surface baselines\" for what each move class means." ]

/// The exact bytes a baseline file holds, rendered without writing anything — the same
/// render/emit separation `LawVectorExport` keeps, and for the same reason: a byte-for-byte
/// guard has to be able to ask "is the committed file current?" on a clean tree, and one that
/// had to write in order to compare could not. LF, explicitly: `.gitattributes` pins LF in the
/// index and `WorkingCopyEolTests` fails a working copy that has drifted.
let internal renderBaseline (packageId: string) (tokens: string list) : string =
    (baselineHeader packageId @ tokens |> String.concat "\n") + "\n"

/// The tokens of a baseline text: every non-blank line that is not a comment.
let internal baselineTokens (text: string) : string list =
    text.Replace("\r\n", "\n").Split('\n')
    |> Array.toList
    |> List.map _.TrimEnd()
    |> List.filter (fun l -> l <> "" && not (l.StartsWith("#", StringComparison.Ordinal)))

// ---- the classifier -------------------------------------------------------

/// How a single moved token relates to the baseline.
///
/// The declaration order is NOT what `headline` reads — see `headline` for the order that
/// decides a surface's one-word class, and why it is a decision rather than a listing.
type internal MoveClass =
    | Removal
    | Retype
    | RecordWidening
    | UnionWidening
    | InterfaceWidening
    | Additive

let internal className (c: MoveClass) : string =
    match c with
    | Removal -> "removal"
    | Retype -> "retype"
    | RecordWidening -> "record-widening"
    | UnionWidening -> "union-widening"
    | InterfaceWidening -> "interface-widening"
    | Additive -> "additive"

/// `true` for every class that stops a pinned consumer compiling (or, for a union case against
/// a stale same-version pack, loading). Only `Additive` is safe.
let internal isBreaking (c: MoveClass) : bool = c <> Additive

type internal Move =
    {
        Class: MoveClass
        /// The baseline's token, where there was one.
        Before: string option
        /// The current surface's token, where there is one.
        After: string option
    }

/// The identity of a token — everything up to its signature, type or ordinal. Two tokens
/// sharing an identity are the same MEMBER rendered differently, which is what makes `retype`
/// distinguishable from a removal beside an unrelated addition.
let internal identity (token: string) : string =
    let space = token.IndexOf ' '

    if space < 0 then
        token
    else
        let kind = token.Substring(0, space)
        let rest = token.Substring(space + 1)

        let cuts =
            [ rest.IndexOf '('; rest.IndexOf " #"; rest.IndexOf " : "; rest.IndexOf " (" ]
            |> List.filter (fun i -> i >= 0)

        let body =
            match cuts with
            | [] -> rest
            | _ -> rest.Substring(0, List.min cuts)

        kind + " " + body.TrimEnd()

/// The owning type of a member token — the qualified name with its last segment removed.
/// `None` for a token that names no member (`type`, `interface-marker`).
///
/// Derived from `identity` rather than from the raw token, because a rendered signature
/// carries both spaces and dots (`method X.Go(System.Int32, System.String) : …`), so splitting
/// the token on whitespace and taking the last dot lands INSIDE a parameter type. That is not
/// a hypothetical: it read `X.Go(System` as the owner and silently classified a member added
/// to a published interface as additive.
let internal owner (token: string) : string option =
    let id = identity token
    let space = id.IndexOf ' '

    if space < 0 then
        None
    else
        let body = id.Substring(space + 1)
        let dot = body.LastIndexOf '.'

        if dot <= 0 then
            None
        else
            Some(body.Substring(0, dot).TrimEnd '.')

/// `true` for an `attribute <type> <Name>` line (Phase 406).
let internal isAttributeToken (token: string) : bool =
    token.StartsWith("attribute ", StringComparison.Ordinal)

/// An `attribute` line's type and attribute name. A rendered type name carries no space, so the
/// last space is the separator.
let internal attributeParts (token: string) : string * string =
    let body = token.Substring "attribute ".Length
    let space = body.LastIndexOf ' '

    if space < 0 then
        body, ""
    else
        body.Substring(0, space), body.Substring(space + 1)

/// Classify the drift from `before` to `after`, as a list of moves.
///
/// Three steps, and the middle one is what a set difference alone cannot do: removed and added
/// tokens sharing an identity are PAIRED into one `Retype`, but only when exactly one of each
/// carries that identity. An overload set where two members changed is left as separate
/// removals and additions rather than paired arbitrarily — a wrong pairing reads as a smaller
/// change than actually happened, which is the one direction this must not err in.
let internal classify (before: string list) (after: string list) : Move list =
    let beforeSet = Set.ofList before
    let afterSet = Set.ofList after

    let removedAll =
        before
        |> List.filter (fun t -> not (afterSet.Contains t))
        |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

    let addedAll =
        after
        |> List.filter (fun t -> not (beforeSet.Contains t))
        |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

    // Phase 406 — an `attribute` line is never paired: it is classed by whether its TYPE stays
    // published. On a type both sides publish, adding or removing it is a `retype`, because the
    // same type now admits different consumer source. With the type itself new it is additive,
    // and with the type gone it goes with it as a removal.
    let removedAttributes, removed = removedAll |> List.partition isAttributeToken
    let addedAttributes, added = addedAll |> List.partition isAttributeToken

    let publishedTypes (tokens: string list) =
        tokens
        |> List.choose (fun t ->
            if t.StartsWith("type ", StringComparison.Ordinal) then
                let body = t.Substring "type ".Length
                let paren = body.LastIndexOf " ("
                Some(if paren > 0 then body.Substring(0, paren) else body)
            else
                None)
        |> Set.ofList

    let typesBefore = publishedTypes before
    let typesAfter = publishedTypes after

    let attributeMoves =
        (removedAttributes
         |> List.map (fun t ->
             { Class =
                 if typesAfter.Contains(fst (attributeParts t)) then
                     Retype
                 else
                     Removal
               Before = Some t
               After = None }))
        @ (addedAttributes
           |> List.map (fun t ->
               { Class =
                   if typesBefore.Contains(fst (attributeParts t)) then
                       Retype
                   else
                       Additive
                 Before = None
                 After = Some t }))

    let countBy f xs =
        xs
        |> List.countBy f
        |> Map.ofList
        |> fun m k -> Map.tryFind k m |> Option.defaultValue 0

    let removedCount = countBy identity removed
    let addedCount = countBy identity added

    let pairable (t: string) =
        removedCount (identity t) = 1 && addedCount (identity t) = 1

    let retypes =
        removed
        |> List.filter pairable
        |> List.map (fun r ->
            let a = added |> List.find (fun t -> identity t = identity r)

            { Class = Retype
              Before = Some r
              After = Some a })

    // Interfaces the BASELINE published. An interface that is itself new carries its whole
    // member set as additions, and those are additive — nobody implements it yet.
    let baselineInterfaces =
        before
        |> List.choose (fun t ->
            if t.StartsWith("interface-marker ", StringComparison.Ordinal) then
                Some(t.Substring("interface-marker ".Length))
            else
                None)
        |> Set.ofList

    let publishedRecord (o: string) =
        beforeSet.Contains(sprintf "type %s (record)" o)

    let publishedUnion (o: string) =
        beforeSet.Contains(sprintf "type %s (union)" o)

    let classifyAdded (t: string) : MoveClass =
        // A field or case on a type the baseline never published is a NEW type: nobody could
        // have constructed or matched it, so it is additive. Checking the OWNER is what keeps
        // a whole new record from being reported as a pile of breaking widenings.
        match owner t with
        | Some o when t.StartsWith("record-field ", StringComparison.Ordinal) && publishedRecord o -> RecordWidening
        | Some o when t.StartsWith("union-case ", StringComparison.Ordinal) && publishedUnion o -> UnionWidening
        | Some o when
            (t.StartsWith("method ", StringComparison.Ordinal)
             || t.StartsWith("property ", StringComparison.Ordinal))
            && baselineInterfaces.Contains o
            ->
            InterfaceWidening
        | _ -> Additive

    let removals =
        removed
        |> List.filter (pairable >> not)
        |> List.map (fun t ->
            { Class = Removal
              Before = Some t
              After = None })

    let additions =
        added
        |> List.filter (pairable >> not)
        |> List.map (fun t ->
            { Class = classifyAdded t
              Before = None
              After = Some t })

    removals @ retypes @ attributeMoves @ additions

/// The one-word class of a whole surface move, or `None` when the surface did not move. This
/// is the "ride or advance" line's verdict.
///
/// The order below ranks by how much the word tells a reader about what the author DID, not by
/// how badly each shape breaks — the first five all break a pinned consumer, so a severity
/// ranking among them would be a distinction without a difference. `Retype` sits BELOW the
/// three widenings deliberately, because it is usually their shadow rather than an independent
/// move: adding a required record field emits the field AND widens the compiler-generated
/// primary constructor, so the surface shows one `record-widening` beside one `retype`. Ranking
/// retype first named that move after its side effect. Every move is printed regardless — the
/// headline summarises a list the reader is also shown, never one it replaces.
let internal headline (moves: Move list) : MoveClass option =
    let order =
        [ Removal; RecordWidening; UnionWidening; InterfaceWidening; Retype; Additive ]

    order |> List.tryFind (fun c -> moves |> List.exists (fun m -> m.Class = c))

// ---- union field names (Phase 237) ------------------------------------------
//
// A `union-case` token's parameter list reads `name: Type, …` since Phase 237. A rename of a
// field changes the token and leaves its identity (everything before the `(`) alone, so the
// pairing step above already classes it `retype`; what these add is the REPORT — naming the
// case and both field names rather than leaving a reader to spot the difference between two
// long tokens — and the legacy-format reading the since-tag report needs.

/// The top-level comma-separated items of a parameter list — a generic instantiation carries
/// `, ` inside its angle brackets, so a plain split would cut `Map<K, V>` in two.
let private splitParameters (ps: string) : string list =
    let items = ResizeArray<string>()
    let current = StringBuilder()
    let mutable depth = 0

    for c in ps do
        match c with
        | '<' ->
            depth <- depth + 1
            current.Append c |> ignore
        | '>' ->
            depth <- depth - 1
            current.Append c |> ignore
        | ',' when depth = 0 ->
            items.Add(current.ToString().Trim())
            current.Clear() |> ignore
        | _ -> current.Append c |> ignore

    let last = current.ToString().Trim()

    if last <> "" || items.Count > 0 then
        items.Add last

    List.ofSeq items

/// A `union-case` token's fields as `(name, type)` — `None` for the name when the token
/// carries none (the pre-Phase-237 format). Empty for a nullary case or any other token.
let internal unionCaseFields (token: string) : (string option * string) list =
    let o = token.IndexOf '('

    if
        not (token.StartsWith("union-case ", StringComparison.Ordinal))
        || o < 0
        || not (token.EndsWith ")")
    then
        []
    else
        token.Substring(o + 1, token.Length - o - 2)
        |> splitParameters
        |> List.map (fun p ->
            // A rendered type never carries `: `; a field name never carries `<`.
            let colon = p.IndexOf ": "
            let angle = p.IndexOf '<'

            if colon > 0 && (angle < 0 || colon < angle) then
                Some(p.Substring(0, colon)), p.Substring(colon + 2)
            else
                None, p)

/// The field renames a `retype` move carries: `(case, before, after)` for every position whose
/// name changed while its type did not. Empty for any other move — including a retype that
/// changed a field's TYPE, which `describe` reports as the plain before/after pair.
let internal fieldRenames (m: Move) : (string * string * string) list =
    match m.Class, m.Before, m.After with
    | Retype, Some b, Some a when b.StartsWith("union-case ", StringComparison.Ordinal) ->
        let bf = unionCaseFields b
        let af = unionCaseFields a

        if bf.Length <> af.Length then
            []
        else
            let case = (identity b).Substring("union-case ".Length)

            List.zip bf af
            |> List.choose (fun ((bn, bt), (an, at)) ->
                match bn, an with
                | Some x, Some y when x <> y && bt = at -> Some(case, x, y)
                | _ -> None)
    | _ -> []

/// A token with its union field names removed — the pre-Phase-237 rendering of the same
/// surface. Used ONLY to compare against a baseline that predates the names (the since-tag
/// report); the live gate never normalises, because a nameless comparison is exactly the
/// blindness Phase 237 removes.
let internal stripFieldNames (token: string) : string =
    match unionCaseFields token with
    | [] -> token
    | fields ->
        let o = token.IndexOf '('
        token.Substring(0, o + 1) + String.Join(", ", fields |> List.map snd) + ")"

/// `true` when a baseline predates field names: it carries a union case with fields and not
/// one of them names a field. A baseline with no carrying case at all is not legacy — there is
/// nothing a name would have been added to.
let internal predatesFieldNames (tokens: string list) : bool =
    let carrying =
        tokens |> List.map unionCaseFields |> List.filter (List.isEmpty >> not)

    not carrying.IsEmpty
    && carrying |> List.forall (List.forall (fst >> Option.isNone))

/// A move rendered for a human, one line.
let internal describe (m: Move) : string =
    match m.Before, m.After with
    | Some b, Some a when not (fieldRenames m).IsEmpty ->
        let renames =
            fieldRenames m
            |> List.map (fun (case, x, y) -> sprintf "field rename on %s: %s -> %s" case x y)
            |> String.concat "; "

        sprintf "%-18s %s  (%s  ->  %s)" (className m.Class) renames b a
    | Some b, None when m.Class = Retype && isAttributeToken b ->
        let name = snd (attributeParts b)

        sprintf
            "%-18s - %s  (attribute removed: %s)"
            (className m.Class)
            b
            (attributeReason name |> Option.defaultValue name)
    | None, Some a when m.Class = Retype && isAttributeToken a ->
        let name = snd (attributeParts a)

        sprintf
            "%-18s + %s  (attribute added: %s)"
            (className m.Class)
            a
            (attributeReason name |> Option.defaultValue name)
    | Some b, Some a -> sprintf "%-18s %s  ->  %s" (className m.Class) b a
    | Some b, None -> sprintf "%-18s - %s" (className m.Class) b
    | None, Some a -> sprintf "%-18s + %s" (className m.Class) a
    | None, None -> className m.Class

// ---- locating what to render ----------------------------------------------

let internal apiDir () : string = Snapshots.repoFile "api"

let internal baselinePath (packageId: string) : string =
    Path.Combine(apiDir (), packageId + ".txt")

/// The configuration this test binary was built into — `Debug` / `Release` — read off its own
/// output path, so the surface is rendered from the same build the suite is running under
/// rather than from whatever else happens to be on disk.
let internal ownConfiguration () : string option =
    let dir = DirectoryInfo AppContext.BaseDirectory

    match dir.Parent with
    | null -> None
    | tfmParent ->
        match tfmParent.Parent with
        | null -> None
        | configDir when configDir.Name = "bin" -> Some tfmParent.Name
        | configDir -> Some configDir.Name

/// The built assembly for a packable project, from THIS binary's own configuration and no other.
/// The gate is meant to verify the bytes that ship (Phase 395): a `Release` run that quietly fell
/// back to a `Debug` assembly left on disk by an earlier build would render, and pass, a surface it
/// never built. So when the configuration is known only that configuration's output is read, and its
/// absence is an `Error` naming what was looked for — a missing assembly means that configuration
/// was not built, a different failure from a surface that moved, and must not read as one. A binary
/// whose output path names no configuration reads whatever is under `bin/`.
let internal assemblyFor (root: string) (projectFile: string) (packageId: string) : Result<string, string> =
    let projectDir = Path.Combine(root, Path.GetDirectoryName(projectFile: string))
    let binDir = Path.Combine(projectDir, "bin")

    if not (Directory.Exists binDir) then
        Error(sprintf "%s: no bin/ under %s — build the solution first" packageId projectDir)
    else
        let candidates =
            Directory.GetFiles(binDir, packageId + ".dll", SearchOption.AllDirectories)
            |> Array.toList

        let chosen =
            match ownConfiguration () with
            | Some config ->
                candidates
                |> List.filter (fun p ->
                    p.Replace('\\', '/').Contains("/bin/" + config + "/", StringComparison.OrdinalIgnoreCase))
            | None -> candidates

        match chosen with
        | best :: _ -> Ok best
        | [] ->
            let inConfiguration =
                ownConfiguration ()
                |> Option.map (sprintf " in the %s configuration")
                |> Option.defaultValue ""

            Error(
                sprintf "%s: no %s.dll under %s%s — build the solution first" packageId packageId binDir inConfiguration
            )

let private repoRoot () : string = Snapshots.repoFile ""

// ---- git, for the since-the-newest-tag report -----------------------------

let private git (root: string) (arguments: string) : Result<string, string> = ChildProcess.git root arguments

/// Semantic ordering over `vX.Y.Z` tags. A tag this does not parse is dropped rather than
/// sorted lexically — `v0.9.0` above `v0.26.0` would name the wrong baseline to compare
/// against, and a wrong comparison is worse than none.
let internal newestVersionTag (tagLines: string list) : string option =
    tagLines
    |> List.map _.Trim()
    |> List.choose (fun t ->
        if not (t.StartsWith("v", StringComparison.Ordinal)) then
            None
        else
            match t.Substring(1).Split('.') with
            | [| a; b; c |] ->
                match Int32.TryParse a, Int32.TryParse b, Int32.TryParse c with
                | (true, x), (true, y), (true, z) -> Some((x, y, z), t)
                | _ -> None
            | _ -> None)
    |> function
        | [] -> None
        | xs -> xs |> List.maxBy fst |> snd |> Some

// ---- the suite ------------------------------------------------------------

/// The packable roster, from Phase 199's derivation. ONE spelling of "what ships" in this
/// repository, deliberately: a second would drift from the first exactly the way the documents
/// that derivation gates had drifted.
let private roster () =
    PackageRosterTests.packableProjects (repoRoot ())

/// One packable package's baseline read against a tag's (Phase 386 — the comparison the since-tag
/// report prints and the `OneDotZero` family's within-a-major law asserts, written once).
type internal SinceTag =
    {
        PackageId: string
        /// The moves from the tag's baseline to the committed one; `None` when the tag carries no
        /// baseline for this package (its first snapshot).
        Moves: Move list option
        /// The tag's baseline predates union field names (Phase 237), so both sides were compared
        /// without them and a field rename since that tag is not visible.
        NamesStripped: bool
        /// The tag's baselines predate `attribute` lines (Phase 406), so this package was compared
        /// without them and a qualification since that tag is not visible here.
        AttributesStripped: bool
    }

/// `true` when a tag's baselines predate `attribute` lines (Phase 406): not one of them carries
/// one. Read across the WHOLE tag rather than per package, because a single package can carry no
/// drawn attribute at all and still be current — while every tag cut since 406 carries the
/// qualified unions' lines somewhere, so an attribute-free tag is a tag cut before it.
let internal predatesAttributes (taggedBaselines: string list list) : bool =
    not taggedBaselines.IsEmpty
    && taggedBaselines |> List.forall (List.exists isAttributeToken >> not)

/// Every packable package whose committed baseline exists, read against `tag`'s baseline.
let internal sinceTag (root: string) (tag: string) : SinceTag list =
    let read =
        [ for id in roster () |> List.map _.PackageId do
              let path = baselinePath id

              if File.Exists path then
                  let current = baselineTokens (File.ReadAllText path)

                  match git root (sprintf "show %s:api/%s.txt" tag id) with
                  | Error _ -> yield id, current, None
                  | Ok text -> yield id, current, Some(baselineTokens text) ]

    // A tag cut before Phase 406 draws no attribute, so every qualified type would read as a
    // `retype` no consumer ever saw — the same shape, and the same answer, as 237's names below.
    let attributesStripped =
        predatesAttributes (read |> List.choose (fun (_, _, t) -> t))

    [ for id, current, tagged in read do
          match tagged with
          | None ->
              yield
                  { PackageId = id
                    Moves = None
                    NamesStripped = false
                    AttributesStripped = false }
          | Some tagged ->
              // A tag cut before Phase 237 carries nameless union cases. Read against it,
              // today's named baseline would report every carrying case as a `retype` that no
              // consumer ever saw. So the comparison drops the names — and says so, because a
              // field rename since that tag is invisible to it, exactly as it was to the gate
              // then. This expires by itself: the first tag cut after 237 carries named
              // baselines, and nothing is stripped against it. Attributes expire the same way at
              // the first tag cut after 406.
              let stripped = predatesFieldNames tagged

              let current =
                  current
                  |> List.filter (fun t -> not (attributesStripped && isAttributeToken t))
                  |> List.map (fun t -> if stripped then stripFieldNames t else t)

              yield
                  { PackageId = id
                    Moves = Some(classify tagged current)
                    NamesStripped = stripped
                    AttributesStripped = attributesStripped } ]

// ---- D101 made general: no case name is reachable unqualified from two public unions (Phase 386) ----
//
// D1 qualified `Severity` because its `Error` shadowed `Result.Error`; D101 stated the rule for every
// union and left it to review. A case name two unions both carry UNQUALIFIED is resolved by whichever
// was opened last — `Diff.fs`'s `Required` silently meant `Strength.Required` over
// `Optionality.Required` until this phase — so the rule is held here, over the shipped assemblies and
// FSharp.Core's own option and result cases. A union may carry a shared name when it is
// `[<RequireQualifiedAccess>]`; at most one union may carry it bare. (Since Phase 406 the surface
// renderer draws the attribute, so qualifying a union is a `retype` the class report names.)

/// One public union case: `(union full name, case name, the union is RequireQualifiedAccess)`.
type internal UnionCase = string * string * bool

/// Every case name carried unqualified by more than one union, with the unions that carry it.
let internal unqualifiedCollisions (cases: UnionCase list) : (string * string list) list =
    cases
    |> List.filter (fun (_, _, rqa) -> not rqa)
    |> List.groupBy (fun (_, case, _) -> case)
    |> List.choose (fun (case, owners) ->
        match owners |> List.map (fun (u, _, _) -> u) |> List.distinct |> List.sort with
        | _ :: _ :: _ as us -> Some(case, us)
        | _ -> None)
    |> List.sortBy fst

/// The public unions of `asm` (a case's nested class is not a union of its own).
let internal publicUnionCases (asm: Assembly) : UnionCase list =
    let isUnion (t: Type) =
        FSharp.Reflection.FSharpType.IsUnion(t, BindingFlags.Public ||| BindingFlags.NonPublic)

    [ for t in asm.GetExportedTypes() do
          let caseClass = t.IsNested && isUnion t.DeclaringType

          if not caseClass && isUnion t then
              let rqa = t.IsDefined(typeof<RequireQualifiedAccessAttribute>, false)

              for c in FSharp.Reflection.FSharpType.GetUnionCases(t, BindingFlags.Public ||| BindingFlags.NonPublic) do
                  yield t.FullName, c.Name, rqa ]

/// FSharp.Core's cases every consumer has open: a Core union carrying one of these bare shadows it.
let internal fsharpCoreCases: UnionCase list =
    [ "Microsoft.FSharp.Core.FSharpOption`1", "Some", false
      "Microsoft.FSharp.Core.FSharpOption`1", "None", false
      "Microsoft.FSharp.Core.FSharpValueOption`1", "ValueSome", false
      "Microsoft.FSharp.Core.FSharpValueOption`1", "ValueNone", false
      "Microsoft.FSharp.Core.FSharpResult`2", "Ok", false
      "Microsoft.FSharp.Core.FSharpResult`2", "Error", false ]

[<Tests>]
let tests =
    testList
        "Public surface"
        [ test "no case name is carried unqualified by two public unions (D101 made general, Phase 386)" {
              let ids = roster () |> List.map _.PackageId
              Expect.isNonEmpty ids "the packable roster was read"

              let cases =
                  ids
                  |> List.collect (fun id -> publicUnionCases (Assembly.Load(AssemblyName id)))

              Expect.isGreaterThan
                  (cases |> List.map (fun (u, _, _) -> u) |> List.distinct |> List.length)
                  50
                  "the shipped unions were read — a check that read none must not read as one that found none"

              let collisions = unqualifiedCollisions (fsharpCoreCases @ cases)

              Expect.isEmpty
                  collisions
                  (sprintf
                      "these case names are carried unqualified by more than one union, so the last one opened shadows the rest — mark all but one [<RequireQualifiedAccess>] (DECISIONS.md D101):\n       %s"
                      (collisions
                       |> List.map (fun (c, us) -> c + ": " + String.concat ", " us)
                       |> String.concat "\n       "))
          }

          test "go-red: a planted bare collision is named; one bare carrier beside qualified ones is not" {
              let planted: UnionCase list =
                  [ "A.Verdict", "Unknown", false
                    "B.Decoded", "Unknown", false
                    "C.Kind", "Known", true
                    "D.Other", "Known", false ]

              Expect.equal
                  (unqualifiedCollisions planted)
                  [ "Unknown", [ "A.Verdict"; "B.Decoded" ] ]
                  "two bare carriers collide; a qualified one beside a bare one does not"

              Expect.equal
                  (unqualifiedCollisions (fsharpCoreCases @ [ "E.Outcome", "Error", false ]))
                  [ "Error", [ "E.Outcome"; "Microsoft.FSharp.Core.FSharpResult`2" ] ]
                  "a bare `Error` shadows Result.Error — D1's own case"
          }


          // Phase 395 — the surface is rendered from the configuration under test, never from a stale
          // build of another. Over a synthetic tree holding ONLY the other configuration's assembly the
          // locator must refuse (go-red: the old preference-then-fallback returned it), and it must find
          // the assembly once this binary's own configuration has one.
          test "assemblyFor reads only the running configuration's output" {
              match ownConfiguration () with
              | None -> ()
              | Some own ->
                  let other = if own = "Release" then "Debug" else "Release"

                  let root =
                      Path.Combine(Path.GetTempPath(), "fuaran-core-assemblyFor-" + Guid.NewGuid().ToString("N"))

                  let place (config: string) =
                      let dir = Path.Combine(root, "src", "Fuaran.Core.Probe", "bin", config, "net10.0")
                      Directory.CreateDirectory dir |> ignore
                      File.WriteAllBytes(Path.Combine(dir, "Fuaran.Core.Probe.dll"), [||])
                      Path.Combine(dir, "Fuaran.Core.Probe.dll")

                  try
                      place other |> ignore
                      let project = Path.Combine("src", "Fuaran.Core.Probe", "Fuaran.Core.Probe.fsproj")

                      match assemblyFor root project "Fuaran.Core.Probe" with
                      | Ok found -> failtestf "read %s from the other configuration (%s)" found other
                      | Error why -> Expect.stringContains why own "the refusal names the configuration it wanted"

                      let wanted = place own

                      Expect.equal
                          (assemblyFor root project "Fuaran.Core.Probe")
                          (Ok wanted)
                          "the running configuration's assembly is the one read"
                  finally
                      if Directory.Exists root then
                          Directory.Delete(root, true)
          }

          test "every packable package has a committed baseline, and no baseline is orphaned" {
              let root = repoRoot ()
              let packable = roster () |> List.map _.PackageId
              Expect.isNonEmpty packable "at least one packable project was found under src/"

              let dir = apiDir ()

              let committed =
                  if Directory.Exists dir then
                      Directory.GetFiles(dir, "*.txt")
                      |> Array.map Path.GetFileNameWithoutExtension
                      |> Array.toList
                      |> List.sort
                  else
                      []

              let missing = packable |> List.filter (fun p -> not (List.contains p committed))
              let orphaned = committed |> List.filter (fun c -> not (List.contains c packable))

              match missing, orphaned with
              | [], [] -> ()
              | _ ->
                  let part label items =
                      if List.isEmpty items then
                          ""
                      else
                          sprintf "\n       %s: %s" label (String.concat ", " items)

                  failtestf
                      "the committed baselines under api/ are not the packable set.%s%s\n       Remedy: CORE_APPROVE_API=1 dotnet run --project tests/Fuaran.Core.Tests writes a baseline for every packable package; delete an orphan by hand. (root: %s)"
                      (part "packable but unbaselined" missing)
                      (part "baselined but not packable" orphaned)
                      root
          }

          test "every baseline is the surface its assembly renders today" {
              let root = repoRoot ()
              let projects = roster ()
              Expect.isNonEmpty projects "at least one packable project was found under src/"

              let dir = apiDir ()

              if Approval.requested Approval.Api then
                  // A filter naming no packable package is red by name, never a quiet no-op.
                  Approval.requireMatched Approval.Api (projects |> List.map _.PackageId)
                  Directory.CreateDirectory dir |> ignore

              let unbuilt = ResizeArray<string>()
              let drifted = ResizeArray<string>()
              let rewritten = ResizeArray<string>()

              for p in projects do
                  match assemblyFor root p.ProjectFile p.PackageId with
                  | Error why -> unbuilt.Add why
                  | Ok dll ->
                      let rendered = renderAssembly dll
                      let text = renderBaseline p.PackageId rendered
                      let path = baselinePath p.PackageId

                      if Approval.admits Approval.Api p.PackageId then
                          if Approval.write Approval.Api p.PackageId path text then
                              rewritten.Add p.PackageId
                      elif not (File.Exists path) then
                          // Reported by the completeness leg above; nothing to compare here.
                          ()
                      else
                          let baseline = baselineTokens (File.ReadAllText path)

                          match classify baseline rendered with
                          | [] -> ()
                          | moves ->
                              let cls = headline moves |> Option.map className |> Option.defaultValue "unchanged"

                              let shown =
                                  moves
                                  |> List.truncate 12
                                  |> List.map (fun m -> "         " + describe m)
                                  |> String.concat "\n"

                              let more =
                                  if moves.Length > 12 then
                                      sprintf "\n         ... and %d more" (moves.Length - 12)
                                  else
                                      ""

                              drifted.Add(sprintf "%s — %s (%d move(s))\n%s%s" p.PackageId cls moves.Length shown more)

              if Approval.requested Approval.Api then
                  if rewritten.Count = 0 then
                      printfn "CORE_APPROVE_API: every selected baseline was already current."
                  else
                      printfn
                          "CORE_APPROVE_API: rewrote %d baseline(s): %s — stage exactly these, by name."
                          rewritten.Count
                          (String.concat ", " rewritten)

              Expect.isEmpty
                  (List.ofSeq unbuilt)
                  (sprintf
                      "every packable project's assembly was found on disk; a missing one is an unbuilt solution, not a surface move:\n       %s"
                      (String.concat "\n       " unbuilt))

              // `drifted` holds only the packages the switch did not admit, so under a filter the
              // unselected packages still report their drift.
              if drifted.Count > 0 then
                  failtestf
                      "%d package(s) moved their public surface without moving their baseline:\n       %s\n       Remedy: the move is PERMITTED — what is refused is an UNCLASSIFIED one. Regenerate with\n       `CORE_APPROVE_API=1 dotnet run --project tests/Fuaran.Core.Tests`, stage the baselines you meant\n       to move BY NAME, and let the class above decide whether the change RIDES the standing draft slot\n       or ADVANCES <Version> (STABILITY.md, \"Public-surface baselines\")."
                      drifted.Count
                      (String.concat "\n       " (List.ofSeq drifted))
          }

          test "the class of every baseline moved since the newest tag" {
              // The "ride or advance" line, which STABILITY entries wrote by hand until now.
              // A REPORT and not a gate: the class is what a reviewer applies the
              // record-widening dispensation to, so a breaking class printed here is
              // information, never a refusal. What it does assert is that it MEASURED
              // something — a newest tag was resolved and every baseline was read — because a
              // report that silently compared nothing is the one outcome worse than no report.
              let root = repoRoot ()

              match git root "tag --list" with
              | Error why ->
                  printfn "PublicSurface since-tag report SKIPPED: %s" why
                  skiptestf "since-tag class report skipped — %s" why
              | Ok out ->
                  let tags =
                      out.Split('\n')
                      |> Array.toList
                      |> List.map _.Trim()
                      |> List.filter (fun l -> l <> "")

                  match newestVersionTag tags with
                  | None ->
                      printfn "PublicSurface since-tag report SKIPPED: no `vX.Y.Z` tag in this clone"
                      skiptest "since-tag class report skipped — this clone holds no `vX.Y.Z` tag"
                  | Some tag ->
                      let packable = roster () |> List.map _.PackageId
                      Expect.isNonEmpty packable "at least one packable project was found under src/"

                      printfn ""
                      printfn "==== public surface: class of every baseline moved since %s" tag

                      let read = sinceTag root tag
                      let mutable moved = 0

                      if read |> List.exists _.AttributesStripped then
                          printfn
                              "  (%s's baselines predate `attribute` lines — compared without them; an attribute added or removed since %s is not visible here, and the release ledger names it)"
                              tag
                              tag

                      for r in read do
                          if r.NamesStripped then
                              printfn
                                  "  %-28s (%s's baseline predates union field names — compared without them; a field rename since %s is not visible here)"
                                  r.PackageId
                                  tag
                                  tag

                          match r.Moves with
                          | None ->
                              printfn "  %-28s first snapshot — %s carries no baseline for this package" r.PackageId tag
                          | Some [] -> ()
                          | Some moves ->
                              moved <- moved + 1

                              let cls = headline moves |> Option.map className |> Option.defaultValue "unchanged"

                              printfn
                                  "  %-28s %-18s %d move(s)%s"
                                  r.PackageId
                                  cls
                                  moves.Length
                                  (if isBreaking (headline moves |> Option.defaultValue Additive) then
                                       "   ADVANCE <Version> — this move is breaking"
                                   else
                                       "   may RIDE a draft slot")

                              for m in moves |> List.truncate 8 do
                                  printfn "      %s" (describe m)

                              if moves.Length > 8 then
                                  printfn "      ... and %d more" (moves.Length - 8)

                      let read = read.Length

                      if moved = 0 then
                          printfn "  (no baseline has moved since %s)" tag

                      printfn "==== %d baseline(s) read, %d moved" read moved
                      printfn ""

                      Expect.isGreaterThan
                          read
                          0
                          "at least one committed baseline was read — a report that compared nothing must not read as a report that found nothing"
          }

          // ---- the renderer, pinned against types declared in this assembly --------
          //
          // Every leg above compares a rendered surface with a file THIS renderer wrote, a
          // pairing that would agree perfectly with itself if the renderer emitted nothing.
          // These read the test assembly's own metadata, where the expected text is known
          // from the declarations at the head of this file.

          test "the renderer emits the F# construct tokens the classifier turns on" {
              let own = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)
              Expect.isNonEmpty own "the test assembly's own surface rendered"

              let endsWith (suffix: string) (t: string) =
                  t.EndsWith(suffix, StringComparison.Ordinal)

              let recordType =
                  own
                  |> List.tryFind (fun t ->
                      t.StartsWith("type ", StringComparison.Ordinal)
                      && endsWith "+ProbeRecord (record)" t)

              Expect.isSome recordType "a record declared in this module renders as `type <full> (record)`"

              let fields =
                  own
                  |> List.filter (fun t ->
                      t.StartsWith("record-field ", StringComparison.Ordinal)
                      && t.Contains "+ProbeRecord.")
                  |> List.map (fun t -> t.Substring(t.IndexOf "+ProbeRecord." + "+ProbeRecord.".Length))
                  |> List.sort

              Expect.equal
                  fields
                  [ "Alpha #0 : System.Int32"; "Beta #1 : System.String" ]
                  "both record fields render with their declaration ORDER — the ordinal is what makes a reorder a `retype` rather than nothing"

              let unionType = own |> List.tryFind (fun t -> endsWith "+ProbeUnion (union)" t)

              Expect.isSome unionType "a union renders as `type <full> (union)`"

              let cases =
                  own
                  |> List.filter (fun t ->
                      t.StartsWith("union-case ", StringComparison.Ordinal)
                      && t.Contains "+ProbeUnion.")
                  |> List.map (fun t -> t.Substring(t.IndexOf "+ProbeUnion." + "+ProbeUnion.".Length))
                  |> List.sort

              Expect.equal
                  cases
                  [ "NewProbeCarrying #1(Item1: System.Int32, Item2: System.String)"
                    "ProbeEmpty #0()" ]
                  "a nullary case renders from its property and a carrying case from its factory — the factory is `CompilerGenerated`, so keeping it is the deliberate carve-out the union-widening class rests on; an unnamed field renders under the name the compiler gives it (Phase 237)"

              let caseOf (typeSuffix: string) =
                  own
                  |> List.filter (fun t ->
                      t.StartsWith("union-case ", StringComparison.Ordinal)
                      && t.Contains(typeSuffix + "."))
                  |> List.map (fun t -> t.Substring(t.IndexOf(typeSuffix + ".") + typeSuffix.Length + 1))
                  |> List.sort

              Expect.equal
                  (caseOf "+ProbeSingle")
                  [ "NewProbeSingle #0(value: System.Int32)" ]
                  "a single-case union's field is named — its property sits on the union type and carries the two-argument mapping (Phase 237)"

              Expect.equal
                  (caseOf "+ProbeStructUnion")
                  [ "NewProbeStructA #0(a: System.Int32)"
                    "NewProbeStructB #1(b: System.String, c: System.Int32)" ]
                  "a struct union's fields are named per case, in declaration order (Phase 237)"

              Expect.equal
                  (caseOf "+ProbeFieldsBefore+Shape")
                  [ "Empty #0()"; "NewHeld #1(parent: System.Int32, kindTag: System.String)" ]
                  "a multi-case union's fields are named from the case class's properties, in declaration order (Phase 237)"

              Expect.isSome
                  (own
                   |> List.tryFind (fun t ->
                       endsWith "+IProbeSeam" t
                       && t.StartsWith("interface-marker ", StringComparison.Ordinal)))
                  "an interface carries its marker, which is what lets an ADDED member on it be classified breaking"

              Expect.isSome
                  (own
                   |> List.tryFind (fun t ->
                       t.StartsWith("method ", StringComparison.Ordinal)
                       && t.Contains "+IProbeSeam.Probe"))
                  "the interface's own member is rendered"
          }

          test "the renderer excludes what a consumer cannot reach" {
              let own = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              // Measured over the token's IDENTITY — its declaring type and member name — and
              // not over the whole token: a rendered signature legitimately carries `<` for a
              // generic instantiation (`FSharpFunc`2<!0, System.String>`), so the whole-token
              // reading would call every generic member a lowered artefact.
              Expect.isEmpty
                  (own
                   |> List.filter (fun t ->
                       let id = identity t
                       id.Contains '@' || id.Contains '<'))
                  "no lowered artefact (closure, state machine, startup class) reaches the surface"

              Expect.isEmpty
                  (own |> List.filter (fun t -> t.Contains ".PackageRosterTests.isPackable"))
                  "an `internal` function is not surface — it cannot break a consumer in another assembly"

              Expect.isEmpty
                  (own
                   |> List.filter (fun t ->
                       t.StartsWith("method ", StringComparison.Ordinal)
                       && (t.Contains ".get_" || t.Contains ".set_")))
                  "property accessors are rendered with their property, never twice"
          }

          // ---- the go-red controls ------------------------------------------
          //
          // Each perturbation below is applied to a REAL rendered surface — the test
          // assembly's own — rather than to a hand-written fixture, so the classifier is shown
          // to fire on the token shapes the renderer actually produces. A synthetic table
          // would prove the classifier consistent with itself and nothing more.

          test "a removed token is classified `removal`" {
              let before = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              let victim =
                  before
                  |> List.find (fun t ->
                      t.StartsWith("record-field ", StringComparison.Ordinal)
                      && t.Contains "+ProbeRecord.Alpha")

              let after = before |> List.filter (fun t -> t <> victim)

              let moves = classify before after
              Expect.equal (headline moves) (Some Removal) "dropping a published record field is a removal"
              Expect.equal moves.Length 1 "exactly one move is reported"
              Expect.equal (moves.Head.Before) (Some victim) "the move names the token that went"
              Expect.isTrue (isBreaking Removal) "a removal is breaking"
          }

          test "a retyped member is classified `retype`, as ONE move rather than two" {
              let before = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              let victim =
                  before
                  |> List.find (fun t ->
                      t.StartsWith("record-field ", StringComparison.Ordinal)
                      && t.Contains "+ProbeRecord.Beta")

              let retyped = victim.Replace("System.String", "System.Int32")

              let after = before |> List.map (fun t -> if t = victim then retyped else t)

              let moves = classify before after
              Expect.equal (headline moves) (Some Retype) "the same member rendered differently is a retype"

              Expect.equal
                  moves.Length
                  1
                  "a retype is ONE move — reporting it as a removal beside an addition would double-count it and mis-name the addition's class"

              Expect.equal moves.Head.After (Some retyped) "the move carries both sides"
          }

          test "a reordered record field is classified `retype` too" {
              // The shape a set difference sees as nothing at all if the ordinal is dropped
              // from the token, which is why the renderer emits `#seq`.
              let before = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              let alpha =
                  before
                  |> List.find (fun t ->
                      t.StartsWith("record-field ", StringComparison.Ordinal)
                      && t.Contains "+ProbeRecord.Alpha")

              let after =
                  before |> List.map (fun t -> if t = alpha then alpha.Replace("#0", "#1") else t)

              Expect.equal
                  (headline (classify before after))
                  (Some Retype)
                  "a field that moved position is reported — positional construction would bind the wrong slot"
          }

          // ---- Phase 237: a union field rename is a surface move ----------------
          //
          // Phase 228 renamed `DiffError.TargetNotAContainer`'s field `parent` -> `target`, and
          // this gate printed nothing: a case rendered by its field TYPES only. These legs pin
          // the move on a compiled fixture pair, on its control, and on 228's rename itself.

          test
              "a union field rename is a breaking `retype` naming the case and both names; an identical pair is unchanged (Phase 237)" {
              let own = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              // One module's surface, re-homed under a common name so the three are comparable
              // as one package seen at two points in time.
              let surfaceOf (moduleName: string) =
                  own
                  |> List.filter (fun t -> t.Contains("+" + moduleName + "+"))
                  |> List.map (fun t -> t.Replace("+" + moduleName + "+", "+ProbeFields+"))

              let before = surfaceOf "ProbeFieldsBefore"
              let after = surfaceOf "ProbeFieldsAfter"
              let same = surfaceOf "ProbeFieldsSame"

              Expect.isNonEmpty before "the fixture's surface rendered"

              Expect.equal
                  (List.length before)
                  (List.length after)
                  "the pair differs by no token count — only by one field's name"

              Expect.isEmpty (classify before same) "a pair differing by nothing is classed unchanged"
              Expect.equal (headline (classify before same)) None "and has no headline"

              let moves = classify before after

              Expect.equal
                  (moves |> List.map _.Class)
                  [ Retype ]
                  "a pair differing only by a union field name is ONE move, a `retype` — not a removal beside an addition"

              Expect.isTrue
                  (headline moves |> Option.map isBreaking |> Option.defaultValue false)
                  "and it is BREAKING: a consumer constructing or matching the case by field name stops compiling"

              let case = "Fuaran.Core.Tests.PublicSurfaceTests+ProbeFields+Shape.NewHeld"

              Expect.equal
                  (moves |> List.collect fieldRenames)
                  [ case, "parent", "target" ]
                  "the move names the case and both field names"

              let line = describe (List.head moves)

              Expect.stringContains
                  line
                  (sprintf "field rename on %s: parent -> target" case)
                  "the report a reviewer reads names the case and both names"
          }

          test
              "Phase 228's rename of DiffError.TargetNotAContainer's field is caught as a breaking `retype` (named vector)" {
              let root = repoRoot ()

              let project = roster () |> List.find (fun p -> p.PackageId = "Fuaran.Core.Ops")

              match assemblyFor root project.ProjectFile project.PackageId with
              | Error why -> failtest why
              | Ok dll ->
                  let current = renderAssembly dll

                  let token =
                      current
                      |> List.filter (fun t ->
                          t.StartsWith("union-case ", StringComparison.Ordinal)
                          && t.Contains "+DiffError`1.NewTargetNotAContainer ")

                  Expect.equal
                      token
                      [ "union-case Fuaran.Core.Diff+DiffError`1.NewTargetNotAContainer #2(target: !0, kindTag: System.String)" ]
                      "the case as 228 left it renders with its field names"

                  // The surface as it stood before 228: the same case, its first field `parent`.
                  let before228 =
                      current
                      |> List.map (fun t ->
                          if t = List.head token then
                              t.Replace("(target: ", "(parent: ")
                          else
                              t)

                  let moves = classify before228 current

                  Expect.equal (moves |> List.map _.Class) [ Retype ] "228's rename is one `retype`"
                  Expect.equal (headline moves) (Some Retype) "and the package's headline says so"

                  Expect.equal
                      (moves |> List.collect fieldRenames)
                      [ "Fuaran.Core.Diff+DiffError`1.NewTargetNotAContainer", "parent", "target" ]
                      "naming the case and both names — the class 228's worker had to write by hand"

                  // The blindness this phase removes, stated as its own control: rendered the
                  // pre-237 way, the two surfaces are the same surface.
                  Expect.isEmpty
                      (classify (before228 |> List.map stripFieldNames) (current |> List.map stripFieldNames))
                      "without field names the rename is invisible — which is what the gate printed for 228"
          }

          test "every carrying union case in every packable package renders its field names (Phase 237)" {
              // The renderer falls back to types alone for a case whose fields it could not
              // name; this is what turns that fallback into a failure rather than a quiet
              // return of the blindness.
              let root = repoRoot ()

              let unnamed =
                  [ for p in roster () do
                        match assemblyFor root p.ProjectFile p.PackageId with
                        | Error _ -> () // reported by the baseline leg as an unbuilt solution
                        | Ok dll ->
                            for t in renderAssembly dll do
                                if unionCaseFields t |> List.exists (fst >> Option.isNone) then
                                    yield sprintf "%s: %s" p.PackageId t ]

              Expect.isEmpty unnamed "every union case with fields renders `name: Type` for each"
          }

          // ---- Phase 406: the attributes that decide what consumer source is legal ----------

          test
              "every drawn attribute renders its `attribute` line on a probe that carries it, and only there (Phase 406)" {
              let own = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              let attributesOf (typeSuffix: string) =
                  own
                  |> List.filter isAttributeToken
                  |> List.filter (fun t -> (fst (attributeParts t)).EndsWith(typeSuffix, StringComparison.Ordinal))
                  |> List.map (attributeParts >> snd)

              let expected =
                  [ "+ProbeQualifyAfter+Shape", [ "RequireQualifiedAccess" ]
                    "+ProbeQualifiedRecord", [ "RequireQualifiedAccess" ]
                    "+ProbeQualifiedModule", [ "RequireQualifiedAccess" ]
                    "+ProbeAutoOpenModule", [ "AutoOpen" ]
                    "+ProbeNoEquality", [ "NoComparison"; "NoEquality" ]
                    "+ProbeNoComparison", [ "NoComparison" ]
                    "+ProbeReferenceEquality", [ "ReferenceEquality" ]
                    "+ProbeNullable", [ "AllowNullLiteral" ]
                    "+ProbeSealed", [ "Sealed" ]
                    "+ProbeAbstract", [ "AbstractClass" ]
                    "+probeUnit", [ "Measure" ]
                    "+ProbeStructRecord", [ "Struct" ]
                    "+ProbeStructUnion", [ "Struct" ] ]

              for suffix, names in expected do
                  Expect.equal (attributesOf suffix) names (sprintf "%s renders exactly its drawn attributes" suffix)

              Expect.equal
                  (expected |> List.collect snd |> List.distinct |> List.sort)
                  ((structAttribute :: List.map snd drawnTypeAttributes)
                   |> List.map _.Name
                   |> List.sort)
                  "every drawn attribute has a probe — an attribute added to the drawn set without one is red here"

              Expect.isEmpty
                  (attributesOf "+ProbeQualifyBefore+Shape"
                   @ attributesOf "+ProbeRecord"
                   @ attributesOf "+ProbeUnion")
                  "a type carrying none renders none — the line is the attribute, not a property of every type"

              for name in expected |> List.collect snd |> List.distinct do
                  Expect.isSome (attributeReason name) (sprintf "%s carries the reason a moved line prints" name)
          }

          test
              "go-red: a union that GAINS `RequireQualifiedAccess` is a breaking `retype` with its reason; losing it is one too; an identical pair is unchanged (Phase 406)" {
              let own = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              // One module's surface, re-homed under a common name — one package at two points in time.
              let surfaceOf (moduleName: string) =
                  own
                  |> List.filter (fun t -> t.Contains("+" + moduleName + "+"))
                  |> List.map (fun t -> t.Replace("+" + moduleName + "+", "+ProbeQualify+"))

              let before = surfaceOf "ProbeQualifyBefore"
              let after = surfaceOf "ProbeQualifyAfter"
              let same = surfaceOf "ProbeQualifySame"

              Expect.isNonEmpty before "the fixture's surface rendered"
              Expect.isEmpty (classify before same) "a pair differing by nothing is classed unchanged"

              let line =
                  "attribute Fuaran.Core.Tests.PublicSurfaceTests+ProbeQualify+Shape RequireQualifiedAccess"

              let gained = classify before after

              Expect.equal
                  (gained |> List.map (fun m -> m.Class, m.Before, m.After))
                  [ Retype, None, Some line ]
                  "qualifying the union is ONE move, a `retype` naming the attribute line — the move the pre-406 renderer read as `unchanged`"

              Expect.equal (headline gained) (Some Retype) "the package's headline says so"
              Expect.isTrue (isBreaking Retype) "and it is breaking: every bare `Ring` / `Disc` stops compiling"

              Expect.stringContains
                  (describe gained.Head)
                  "attribute added: a case, field or member must be named through its type or module"
                  "the report prints the reason beside the move"

              let lost = classify after before

              Expect.equal
                  (lost |> List.map (fun m -> m.Class, m.Before, m.After))
                  [ Retype, Some line, None ]
                  "un-qualifying it is a `retype` too: a bare name may now shadow another in the consumer's scope"

              Expect.stringContains (describe lost.Head) "attribute removed: " "with its reason printed"

              // The blindness this phase removes, stated as its own control: without the attribute
              // lines the two surfaces are the same surface.
              Expect.isEmpty
                  (classify
                      (before |> List.filter (isAttributeToken >> not))
                      (after |> List.filter (isAttributeToken >> not)))
                  "without attribute lines the qualification is invisible — which is what the gate printed for Phase 386"
          }

          test
              "every drawn attribute, added to or removed from a published type, is a breaking `retype` with its reason (Phase 406)" {
              let own = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)
              let lines = own |> List.filter isAttributeToken

              Expect.isGreaterThanOrEqual lines.Length 13 "the probes' attribute lines rendered"

              for line in lines do
                  let without = own |> List.filter (fun t -> t <> line)
                  let name = snd (attributeParts line)

                  for moves, verb in [ classify own without, "removed"; classify without own, "added" ] do
                      Expect.equal
                          (moves |> List.map _.Class)
                          [ Retype ]
                          (sprintf "%s %s on a published type is one `retype`" name verb)

                      Expect.stringContains
                          (describe moves.Head)
                          (sprintf "attribute %s: %s" verb (attributeReason name |> Option.defaultValue "?"))
                          (sprintf "%s %s prints its reason" name verb)
          }

          test "an attribute on a NEW type is `additive`, and on a type that left it goes as a `removal` (Phase 406)" {
              let published = [ "type A.U (union)"; "union-case A.U.C #0()" ]
              let line = "attribute A.V RequireQualifiedAccess"
              let grown = published @ [ "type A.V (union)"; line; "union-case A.V.D #0()" ]

              Expect.equal
                  (classify published grown |> List.map _.Class |> List.distinct)
                  [ Additive ]
                  "a new qualified union is ordinary growth: nobody named its cases before"

              Expect.equal
                  (classify grown published
                   |> List.filter (fun m -> m.Before = Some line)
                   |> List.map _.Class)
                  [ Removal ]
                  "a qualified union that left takes its line with it as a removal"
          }

          test "the since-tag report reads a pre-406 tag without attribute lines, and only such a tag (Phase 406)" {
              let current = [ "attribute A.U RequireQualifiedAccess"; "type A.U (union)" ]

              Expect.isTrue
                  (predatesAttributes [ [ "type A.U (union)" ]; [ "type B.V (record)" ] ])
                  "a tag none of whose baselines carries an attribute line predates them"

              Expect.isFalse
                  (predatesAttributes [ [ "type A.U (union)" ]; current ])
                  "one baseline carrying one is enough: a package with no drawn attribute is not legacy on its own"

              Expect.isFalse (predatesAttributes []) "a tag that carries no baseline at all is not read as legacy"

              Expect.equal
                  (classify [ "type A.U (union)" ] current |> List.map _.Class)
                  [ Retype ]
                  "while the LIVE gate, which never strips, sees an attribute-free baseline as moved — so a stale baseline cannot hide a qualification"
          }

          test "no packable package carries an attribute the renderer refuses rather than draws (Phase 406)" {
              let root = repoRoot ()

              let carried =
                  [ for p in roster () do
                        match assemblyFor root p.ProjectFile p.PackageId with
                        | Error _ -> () // reported by the baseline leg as an unbuilt solution
                        | Ok dll ->
                            for c in undrawnSurfaceAttributes dll do
                                yield sprintf "%s: %s" p.PackageId c ]

              Expect.isEmpty
                  carried
                  "these change what a consumer may write and the surface does not draw them — teach the renderer to draw the attribute in the same change (DECISIONS.md D134)"
          }

          test
              "go-red: the refused-attribute scan names a `ParamArray` parameter and an F# optional argument (Phase 406)" {
              let own =
                  undrawnSurfaceAttributes (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)
                  |> List.filter (fun l -> l.Contains "+ProbeRefusedSurface.")

              Expect.equal
                  own
                  [ "Fuaran.Core.Tests.PublicSurfaceTests+ProbeRefusedSurface.Maybe(x): Microsoft.FSharp.Core.OptionalArgumentAttribute"
                    "Fuaran.Core.Tests.PublicSurfaceTests+ProbeRefusedSurface.Spread(xs): System.ParamArrayAttribute" ]
                  "both planted carriers are named, by member and parameter"
          }

          test "the since-tag report reads a pre-237 baseline without names, and only it (Phase 237)" {
              let named = [ "type A.U (union)"; "union-case A.U.NewC #0(target: System.Int32)" ]

              let legacy = named |> List.map stripFieldNames

              Expect.equal legacy [ "type A.U (union)"; "union-case A.U.NewC #0(System.Int32)" ] "names stripped"
              Expect.isTrue (predatesFieldNames legacy) "a nameless baseline with a carrying case predates the names"
              Expect.isFalse (predatesFieldNames named) "a named one does not"

              Expect.isFalse
                  (predatesFieldNames [ "type A.U (union)"; "union-case A.U.C #0()" ])
                  "a baseline with no carrying case has nothing a name was added to"

              Expect.isEmpty
                  (classify legacy (named |> List.map stripFieldNames))
                  "a legacy baseline read against a stripped current surface is unchanged"

              Expect.equal
                  (classify legacy named |> List.map _.Class)
                  [ Retype ]
                  "while the LIVE gate, which never strips, sees a nameless baseline as moved — so a stale baseline cannot hide a rename"
          }

          test "a field added to a PUBLISHED record is `record-widening`, and to a new one is `additive`" {
              let before = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              let recordType =
                  before
                  |> List.find (fun t -> t.EndsWith("+ProbeRecord (record)", StringComparison.Ordinal))

              let ownerName =
                  recordType.Substring("type ".Length, recordType.Length - "type ".Length - " (record)".Length)

              let widened =
                  (sprintf "record-field %s.Gamma #2 : System.Int32" ownerName) :: before

              Expect.equal
                  (headline (classify before widened))
                  (Some RecordWidening)
                  "a field on a record the baseline published breaks every full-literal construction (FS0764)"

              // The control: the SAME token shape on a type the baseline never had.
              let fresh =
                  [ "type Fuaran.Core.Probe.Brand (record)"
                    "record-field Fuaran.Core.Probe.Brand.Gamma #0 : System.Int32" ]
                  @ before

              Expect.equal
                  (headline (classify before fresh))
                  (Some Additive)
                  "a field on a type the baseline never published is additive — nobody could have constructed it"

              // The shape a REAL widening takes, measured rather than assumed: adding a
              // required field also widens the compiler-generated primary constructor, so the
              // surface carries a `retype` beside the `record-widening`. The headline must
              // name the CAUSE, not its side effect.
              let ctor =
                  before
                  |> List.find (fun t ->
                      t.StartsWith("ctor ", StringComparison.Ordinal)
                      && t.Contains "+ProbeRecord..ctor")

              let widenedWithCtor =
                  widened
                  |> List.map (fun t -> if t = ctor then ctor.Replace(")", ", System.Int32)") else t)

              let moves = classify before widenedWithCtor

              Expect.isTrue
                  (moves |> List.exists (fun m -> m.Class = Retype))
                  "the widened constructor is present as a retype"

              Expect.equal
                  (headline moves)
                  (Some RecordWidening)
                  "the headline names the widening, not the constructor retype it caused"
          }

          test "a case added to a PUBLISHED union is `union-widening`, and to a new one is `additive`" {
              let before = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              let unionType =
                  before
                  |> List.find (fun t -> t.EndsWith("+ProbeUnion (union)", StringComparison.Ordinal))

              let ownerName =
                  unionType.Substring("type ".Length, unionType.Length - "type ".Length - " (union)".Length)

              let widened =
                  (sprintf "union-case %s.NewProbeThird #2(System.Int32)" ownerName) :: before

              Expect.equal
                  (headline (classify before widened))
                  (Some UnionWidening)
                  "a case on a union the baseline published makes every exhaustive match incomplete — and REMOVES NOTHING, which is why a removal-only differ misses it"

              let fresh =
                  [ "type Fuaran.Core.Probe.Choice (union)"
                    "union-case Fuaran.Core.Probe.Choice.NewOne #0()" ]
                  @ before

              Expect.equal
                  (headline (classify before fresh))
                  (Some Additive)
                  "a case on a union the baseline never published is additive"
          }

          test "a member added to a PUBLISHED interface is `interface-widening`, and to a new one is `additive`" {
              let before = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              let marker =
                  before
                  |> List.find (fun t ->
                      t.StartsWith("interface-marker ", StringComparison.Ordinal)
                      && t.EndsWith("+IProbeSeam", StringComparison.Ordinal))

              let ownerName = marker.Substring("interface-marker ".Length)

              let widened =
                  (sprintf "method %s.Extra(System.Int32) : System.String" ownerName) :: before

              Expect.equal
                  (headline (classify before widened))
                  (Some InterfaceWidening)
                  "a member on an interface the baseline published stops every implementer compiling"

              let fresh =
                  [ "interface-marker Fuaran.Core.Probe.IFresh"
                    "type Fuaran.Core.Probe.IFresh (interface)"
                    "method Fuaran.Core.Probe.IFresh.Extra(System.Int32) : System.String" ]
                  @ before

              Expect.equal
                  (headline (classify before fresh))
                  (Some Additive)
                  "a member on an interface the baseline never published is additive"
          }

          test "ordinary growth is `additive`, and an unmoved surface reports nothing" {
              let before = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              Expect.isEmpty (classify before before) "an identical rebuild reports no move at all"

              let grown =
                  [ "type Fuaran.Core.Probe.Helper (module)"
                    "method Fuaran.Core.Probe.Helper.run(System.Int32) : System.Int32" ]
                  @ before

              Expect.equal
                  (headline (classify before grown))
                  (Some Additive)
                  "a new module and a new function on it are additive"

              Expect.isFalse (isBreaking Additive) "only `additive` is safe for a pinned consumer"
          }

          test "an ambiguous overload move is left as removal + addition rather than mis-paired" {
              // Pairing is by IDENTITY, and two overloads share one. Pairing them arbitrarily
              // would report ONE retype where TWO members moved — a smaller change than
              // happened, which is the direction this must not err in.
              let before =
                  [ "type X.T (type)"
                    "method X.T.Go(System.Int32) : System.Int32"
                    "method X.T.Go(System.String) : System.Int32" ]

              let after =
                  [ "type X.T (type)"
                    "method X.T.Go(System.Int64) : System.Int32"
                    "method X.T.Go(System.Double) : System.Int32" ]

              let moves = classify before after
              Expect.equal moves.Length 4 "two removals and two additions, unpaired"
              Expect.isEmpty (moves |> List.filter (fun m -> m.Class = Retype)) "nothing was paired"
              Expect.equal (headline moves) (Some Removal) "the headline is the most severe move present"
          }

          test "the baseline reader drops the header and keeps every token" {
              let rendered =
                  renderBaseline "Fuaran.Core.Probe" [ "type A.B (record)"; "record-field A.B.C #0 : System.Int32" ]

              Expect.isTrue (rendered.EndsWith("\n", StringComparison.Ordinal)) "the file ends with a newline"

              Expect.isFalse
                  (rendered.Contains "\r")
                  "LF only — .gitattributes pins it and WorkingCopyEolTests fails a CRLF working copy"

              Expect.equal
                  (baselineTokens rendered)
                  [ "type A.B (record)"; "record-field A.B.C #0 : System.Int32" ]
                  "the `#` header is not read as surface"

              Expect.isEmpty
                  (baselineTokens "# only a header\n\n   \n")
                  "a header-only file carries no tokens — which is why the completeness leg is a separate check"
          }

          test "identity cuts a token at its signature, ordinal or type" {
              Expect.equal (identity "type A.B (record)") "type A.B" "a type header"
              Expect.equal (identity "record-field A.B.C #3 : System.Int32") "record-field A.B.C" "a record field"
              Expect.equal (identity "union-case A.B.NewC #2(System.Int32)") "union-case A.B.NewC" "a union case"

              Expect.equal
                  (identity "method A.B.Go`1(System.Int32) : System.Int32")
                  "method A.B.Go`1"
                  "a generic method keeps its arity"

              Expect.equal (identity "ctor A.B..ctor(System.Int32)") "ctor A.B..ctor" "a constructor"
              Expect.equal (identity "property A.B.P : System.Int32 { get; set }") "property A.B.P" "a property"
              Expect.equal (identity "field A.B.F : System.Int32 (literal)") "field A.B.F" "a field"
              Expect.equal (identity "interface-marker A.B") "interface-marker A.B" "a marker"
          }

          test "the owner of a member token survives a signature full of dots and spaces" {
              // The go-red for the defect this classifier shipped with for one build: reading
              // the owner off `token.Split(' ')[1]` lands inside a PARAMETER TYPE, and the
              // interface-widening and record-widening classes both key off the owner, so the
              // mis-read reported a breaking move as additive — the one direction that matters.
              Expect.equal
                  (owner "method A.B+IS.Extra(System.Int32, System.String) : System.String")
                  (Some "A.B+IS")
                  "a two-parameter signature does not move the owner"

              Expect.equal
                  (owner "record-field A.B.C #0 : Microsoft.FSharp.Collections.FSharpList`1<System.Int32>")
                  (Some "A.B")
                  "a generic field type does not move the owner"

              Expect.equal
                  (owner "union-case A.B.NewC #1(System.Int32)")
                  (Some "A.B")
                  "a union case's owner is its union"

              Expect.equal (owner "ctor A.B..ctor(System.Int32)") (Some "A.B") "a constructor's owner is its type"

              Expect.equal
                  (owner "type A.B (record)")
                  (Some "A")
                  "a type header has no member, so its `owner` is its namespace"

              Expect.isNone (owner "interface-marker B") "an unqualified marker owns nothing"
          }

          test "the newest tag is chosen by version, not by string order" {
              Expect.equal
                  (newestVersionTag [ "v0.9.0"; "v0.26.0"; "v0.25.0" ])
                  (Some "v0.26.0")
                  "`v0.26.0` is newer than `v0.9.0` — a lexical sort would name the wrong baseline to compare against"

              Expect.equal
                  (newestVersionTag [ "v1.0.0"; "not-a-tag"; "v0.26.0"; "v1.0" ])
                  (Some "v1.0.0")
                  "a tag this does not parse is dropped rather than ordered by accident"

              Expect.isNone (newestVersionTag [ "nightly"; "release/2" ]) "a clone with no `vX.Y.Z` tag yields none"
          } ]
