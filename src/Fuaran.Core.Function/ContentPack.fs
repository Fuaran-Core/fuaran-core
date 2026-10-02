namespace Fuaran.Core

// ============================================================================
//  Content-pack packaging contract (Phase 57) — a content pack distributes as a
//  set of CURRIED artifact-functions (`FunctionRegistry.partiallyApply`, Phase
//  24/50 — the content-pack formalism) plus a manifest, loading into the
//  signature-typed registry through ONE mechanism across domains (music Artist
//  Packs, legal house-style, CAD manufacturability, a future Model domain's
//  regulatory packs, …) rather than a per-domain bespoke format.
//
//  The contract carries NO pack CONTENT (FGP 6, open-core boundary): a
//  `PackedFunction` names a base function by id, the holes it binds (by absolute
//  address — hygiene), and the base signature VERSION it was curried against. The
//  curried bodies + rules + payload stay registry-side / domain-side — none of it
//  rides on the manifest, which is just ids, addresses, and version tags. The
//  abstractions package therefore depends on no domain payload.
//
//  A pack pins the base signature's FINGERPRINT (its canonical tool-schema shape).
//  Loading recomputes the live fingerprint and refuses a mismatch, so a pack
//  authored against a base signature that has since changed shape fails the load
//  LOUDLY rather than binding against addresses that no longer mean what the pack
//  assumed ("never a silent stale binding" — the Fork-2 hygiene contract at the
//  distribution boundary).
//
//  Additive over the FROZEN registry (GP1/GP7): loading is `partiallyApply` +
//  `register`, the registry's existing default-deny posture, no new dispatch path.
//  FSharp.Core only, Fable-clean. Totality (GP4): a typed `PackLoadError`, never an
//  exception.
// ============================================================================

/// One curried artifact-function in a content pack: curry the base function `BaseId` (a registered
/// `FunctionEntry`) by binding `BoundAddrs` (absolute addresses — hygiene), registering the narrowed
/// result under `NewId` (its nominal tag in the catalogue). `BaseSignatureVersion` is the fingerprint of
/// the base signature the pack was authored against (canonically `ContentPack.signatureFingerprint`);
/// loading recomputes the live fingerprint and refuses a mismatch. Carries NO node / body — content-free
/// (FGP 6): it is the typed DECLARATION of a partial application, not the curried tree itself.
type PackedFunction =
    {
        /// The id the narrowed function registers under; a collision refuses the load `PackRegisterFailed`.
        NewId: string
        /// The function curried: any id in the registry when this entry loads, including one an earlier
        /// entry of the same pack registered.
        BaseId: string
        /// The base signature's fingerprint at authoring time; it must equal the live one at load.
        BaseSignatureVersion: string
        /// The data-hole addresses bound away; an action hole or an undeclared address refuses the load.
        BoundAddrs: Set<string>
    }

/// A content-pack manifest (Phase 57): a set of curried artifact-functions + metadata (`Domain`,
/// `PackId`, `PackVersion`). The whole distribution surface a domain ships — and it carries no pack
/// CONTENT (FGP 6): only ids, addresses, version tags. Loadable into any `FunctionRegistry` that holds
/// the pack's base functions, through one mechanism shared across every domain.
type PackManifest =
    {
        /// The pack's identity, named in every `PackLoadError` the load returns.
        PackId: string
        /// The domain the pack targets; metadata only, never checked by `load`.
        Domain: string
        /// The pack's own release number; metadata only, never compared by `load`.
        PackVersion: int
        /// The curried functions, loaded in list order; the first refusal stops the load.
        Functions: PackedFunction list
    }

/// Why a content pack was refused at load time — total, and (per the envelope discipline, GP5) it names
/// the failure and enumerates the alternatives where a closed set is expected. Default-deny by shape:
/// only a known base + a matching signature version + a non-duplicate id loads.
type PackLoadError =
    /// The base id is not registered; `known` lists the ids registered at that point of the load,
    /// earlier entries of this pack included.
    | UnknownBaseFunction of packId: string * baseId: string * known: string list
    /// The base's live signature fingerprint `actual` differs from the pack's `declared` one: the
    /// base changed shape since the pack was authored.
    | SignatureVersionMismatch of packId: string * baseId: string * declared: string * actual: string
    /// Currying or registering `newId` failed with the registry's own refusal — typically a
    /// `DuplicateCapability`, or a `NonTotalCapability` inherited from the base.
    | PackRegisterFailed of packId: string * newId: string * reason: InvokeError
    /// A `BoundAddrs` entry that is not a bindable hole of the base (Phase 295) — an address the
    /// base does not declare, or an action hole — naming the pack, the curried id, the address and
    /// the base's bindable holes. Until Phase 295 such an address was ignored and the pack
    /// registered an UN-narrowed signature under its new id: the silent stale binding this contract
    /// exists to refuse.
    | UnknownBoundAddr of packId: string * newId: string * addr: string * declared: string list

/// Build / fingerprint / load content packs over the Phase-50 signature-typed registry. Additive over
/// `FunctionRegistry`; FSharp.Core-only, Fable-clean.
module ContentPack =

    /// A stable fingerprint of a signature's SHAPE — the canonical "signature version" a pack pins.
    /// Derived from the canonical tool-schema projection (`Function.toSchema` → `Json.render`) hashed
    /// with the substrate's portable FNV-1a, so an unchanged name / holes / value-spaces / effect always
    /// gives the same fingerprint, and a change to any of them shifts it except on a 32-bit hash
    /// collision (the hash is not collision-resistant, so equal fingerprints are evidence, not proof,
    /// of an equal shape). A pack curried against an old shape therefore fails the load-time
    /// version check (a renamed / re-typed / dropped hole shifts the fingerprint) rather than binding
    /// against addresses that no longer mean what the pack assumed. Deterministic + Fable-clean (the
    /// same FNV-1a arithmetic class as the rest of the substrate's portable hashing).
    let signatureFingerprint (sg: Signature) : string =
        Function.toSchema sg |> Json.render |> Hash.fnv1a

    /// Build a `PackedFunction` against a base ENTRY, fingerprinting its current signature — the
    /// authoring helper, so a pack pins the live shape it was actually curried from (the honest path; a
    /// hand-written `BaseSignatureVersion` is still accepted by the `PackedFunction` literal, and a wrong
    /// one is exactly what the load-time check catches).
    let pack (newId: string) (boundAddrs: Set<string>) (baseEntry: FunctionEntry) : PackedFunction =
        { NewId = newId
          BaseId = baseEntry.Capability.Id
          BaseSignatureVersion = signatureFingerprint baseEntry.Capability.Signature
          BoundAddrs = boundAddrs }

    /// Load a content pack into a signature-typed registry (Phase 57). Each `PackedFunction` curries its
    /// base function (`FunctionRegistry.partiallyApply` — the content-pack formalism) and registers the
    /// narrowed entry under its `NewId`, through the registry's existing default-deny posture (no new
    /// dispatch path). Four guards, default-deny by shape:
    ///   1. the base function must be registered (an unknown base is `UnknownBaseFunction`, enumerating
    ///      the known ids) — a pack cannot conjure a function the host did not register;
    ///   2. the pack's declared base signature version must equal the registry's LIVE fingerprint for
    ///      that base (a mismatch is `SignatureVersionMismatch`, naming both declared + actual) — a pack
    ///      authored against a since-changed signature fails loudly, never binds stale;
    ///   3. every bound address must be a bindable hole of the base (`UnknownBoundAddr`, naming the
    ///      bindable holes — Phase 295);
    ///   4. the narrowed entry must register without collision (a duplicate `NewId` surfaces the
    ///      registry's own `DuplicateCapability` wrapped as `PackRegisterFailed`).
    /// Total — returns the extended registry or the FIRST `PackLoadError`; the registry threads by value
    /// (no global state, GP2). All-or-nothing: a failing entry returns the error WITHOUT handing back a
    /// partially-extended registry, so a rejected pack never half-loads (the caller keeps its original).
    let load (manifest: PackManifest) (reg: FunctionRegistry) : Result<FunctionRegistry, PackLoadError> =
        let rec go (acc: FunctionRegistry) =
            function
            | [] -> Ok acc
            | (pf: PackedFunction) :: rest ->
                match Map.tryFind pf.BaseId acc.Entries with
                | None ->
                    Error(UnknownBaseFunction(manifest.PackId, pf.BaseId, acc.Entries |> Map.toList |> List.map fst))
                | Some baseEntry ->
                    let actual = signatureFingerprint baseEntry.Capability.Signature

                    if actual <> pf.BaseSignatureVersion then
                        Error(SignatureVersionMismatch(manifest.PackId, pf.BaseId, pf.BaseSignatureVersion, actual))
                    else
                        match FunctionRegistry.partiallyApply pf.NewId pf.BoundAddrs baseEntry with
                        | Error(UnknownArg(addr, declared)) ->
                            Error(UnknownBoundAddr(manifest.PackId, pf.NewId, addr, declared))
                        | Error e -> Error(PackRegisterFailed(manifest.PackId, pf.NewId, e))
                        | Ok curried ->
                            match FunctionRegistry.register curried acc with
                            | Ok acc' -> go acc' rest
                            | Error e -> Error(PackRegisterFailed(manifest.PackId, pf.NewId, e))

        go reg manifest.Functions
