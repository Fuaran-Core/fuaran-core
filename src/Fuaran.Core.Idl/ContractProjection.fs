namespace Fuaran.Core.Idl

open Fuaran.Core

// ---------------------------------------------------------------------------
// Phase 644 — the projection of a contract method onto a `Capability` (DECISIONS.md D260).
//
// A method is declared once, in the IDL. What a registry gates, a pipeline type-checks and a
// journal keys is a capability. This is the one function between them: total on every well-formed
// vocabulary, and the only place an IDL type is named to the function package, which takes no
// reference to this one. An IDL type crosses as the VALUE SPACE it projects to:
//
//   * a scalar the spaces state: a string, a 32-bit integer and a float as their own spaces, each
//     narrowed by the argument's constraint; a boolean and an enumeration as an `Enum` of their
//     wire spellings;
//   * a string-borne scalar (a 64-bit integer, a decimal, bytes, a time of day, a duration, a
//     hosted format): `AnyString`. The exact type stays here, in the contract;
//   * every structured type: `Document`, carrying the type's name where it has one and the
//     canonical text of its type expression where it has none.
// ---------------------------------------------------------------------------

/// The projection of a contract method onto a capability (Phase 644).
[<RequireQualifiedAccess>]
module ContractProjection =

    /// The two spellings of a boolean argument, in the order the space lists them.
    let private booleans = [ "false"; "true" ]

    /// THE canonical text of a type expression: the artifact's own encoding of the type
    /// (`idl.json`'s), rendered under the cross-host rules. One type expression has one text, so
    /// two holes carry the same text exactly when the IDL gives them one type. `None` for an
    /// expression the artifact cannot write, which no well-formed contract holds.
    let typeText (t: IdlType) : string option =
        match Artifact.idlType.Write t with
        | Ok json -> Some(Canonical.write Rendering.CrossHost json)
        | Error _ -> None

    /// What a document space carries for a structured type: the declared name of a record or of a
    /// union applied to no argument, and the canonical type text of every other.
    let private documentOf (t: IdlType) : ValueSpace =
        match t with
        | TRecord name
        | TUnion(name, []) -> Document(Some name)
        | other -> Document(typeText other)

    /// The value space a type projects to at a hole or a result, narrowed by `bound` where the
    /// argument declares one. Total: a name the vocabulary does not declare projects as the
    /// structured type it would be, and a well-formed vocabulary holds none.
    let rec spaceOf (idl: Idl) (t: IdlType) (bound: ArgumentConstraint option) : ValueSpace =
        match t, bound with
        | TInt, Some(ArgumentConstraint.IntBetween(lo, hi)) -> IntRange(lo, hi)
        | TInt, _ -> IntRange(System.Int32.MinValue, System.Int32.MaxValue)
        | TFloat, Some(ArgumentConstraint.FloatBetween(lo, hi)) -> FloatRange(lo, hi)
        // A float at the registry is FINITE: the IDL's three non-finite tokens are refused in an
        // argument, as every number at this seam is.
        | TFloat, _ -> FloatRange(-System.Double.MaxValue, System.Double.MaxValue)
        | TStr, Some(ArgumentConstraint.LengthBetween(lo, hi)) -> StringLen(lo, hi)
        | TStr, _ -> AnyString
        | TBool, _ -> Enum booleans
        | TEnum name, _ ->
            match IdlLookup.tryEnum idl name with
            | Some e -> Enum e.WireCases
            | None -> AnyString
        | (TInt64 | TDecimal | TBytes | TTimeOfDay | TDuration), _ -> AnyString
        | THosted { Wire = None; Format = None }, _ -> documentOf t
        | THosted { Wire = Some wire }, _ -> spaceOf idl wire None
        | THosted _, _ -> AnyString
        | (TClosure | TFn _ | TOpaque | TVar _), _ -> AnyString
        | (TUnion _ | TRecord _ | TNode | TKind | TOp | TList _ | TMap _ | TTuple _ | TKeyedMap _ | TSet _ | TJson), _ ->
            documentOf t

    /// One method as a capability at `placement`. Field by field:
    ///
    ///   * the id is the method's name, `<contract>.<method>` (`Contract.methodName`);
    ///   * the signature's name is that name; its holes are the arguments IN DECLARED ORDER, each
    ///     addressed and named by the argument's name, in the space its type and constraint project
    ///     to, and required exactly when the argument is;
    ///   * the effect class is the method's;
    ///   * the result is the space the method's result type projects to, and `Returns.Nothing` for
    ///     a method that answers nothing.
    ///
    /// Everything else a method declares (its shape, read policy, authority, recording, audit kind,
    /// rate limit, versions and descriptions) stays in the contract: a capability does not carry
    /// them, and a host that enforces them reads the contract.
    let capabilityOf (idl: Idl) (placement: Placement) (contract: IdlContract) (m: IdlMethod) : Capability =
        let name = Contract.methodName contract.Name m.Id

        let hole (a: IdlArgument) : SigEntry =
            { Addr = a.Name
              Name = a.Name
              Kind = ValueHole(spaceOf idl a.Type a.Constraint)
              Required = (a.Opt = Required) }

        Capability.create
            name
            { Name = name
              Holes = m.Arguments |> List.map hole
              Effect = m.Effect }
            placement
            (match m.Result with
             | Some t -> Returns.Value(spaceOf idl t None)
             | None -> Returns.Nothing)

    /// Every method of every contract as a capability at `placement`, contracts and methods in
    /// declared order: one capability per method, with distinct ids. A vocabulary that is not
    /// well-formed (`Declare.errors`) projects nothing and answers its faults.
    let capabilities (placement: Placement) (idl: Idl) : Result<Capability list, VocabularyFault list> =
        match Declare.errors idl with
        | [] ->
            Ok
                [ for c in idl.Contracts do
                      for m in c.Methods do
                          capabilityOf idl placement c m ]
        | faults -> Error faults
