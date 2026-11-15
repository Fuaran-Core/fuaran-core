namespace Fuaran.Core.Idl

open Fuaran.Core

// ---------------------------------------------------------------------------
// Phase 644 — contracts: what makes a declared contract well-formed.
//
// A contract is a named set of methods (`IdlContract`, `IdlMethod`, `IdlArgument` in `Idl.fs`). This
// file holds the rules a declaration must satisfy, each a NAMED refusal (`ContractFault`), and the
// two readings every consumer shares: a method's name (`Contract.methodName`) and a method at a
// schema version (`Contract.methodAt`). `Declare.errors` runs `Contract.faults` beside its own
// checks, so a vocabulary read from `idl.json` is refused for a malformed contract exactly as it is
// for a malformed kind. The projection of a method onto a capability is `ContractProjection`.
//
// What is NOT here, by design: nothing a runtime needs. No client, no server table, no protocol.
// ---------------------------------------------------------------------------

/// One way a declared contract is not well-formed (Phase 644). Each case is a refusal with a name
/// (`ContractFault.name`), so a test, a vector and a reader of a refused document name the rule
/// and not a sentence.
[<RequireQualifiedAccess>]
type ContractFault =
    /// A contract's name is not an identifier.
    | ContractNameNotIdentifier of contract: string
    /// Two contracts share a name.
    | DuplicateContract of contract: string
    /// A method's id is not an identifier.
    | MethodIdNotIdentifier of contract: string * methodId: string
    /// Two methods of one contract share an id.
    | DuplicateMethod of contract: string * methodId: string
    /// A method declares no authority. Open access is a declared value, never an absence.
    | NoAuthority of contract: string * methodId: string
    /// An authority value is empty, ill-formed text, or listed twice.
    | MalformedAuthority of contract: string * methodId: string * value: string
    /// A method serves no schema version.
    | ServesNoVersion of contract: string * methodId: string
    /// A served version is not positive, or is listed twice.
    | MalformedVersion of contract: string * methodId: string * version: int
    /// A stream declares a read policy: its answers are not one answer to reuse.
    | StreamWithReadPolicy of contract: string * methodId: string
    /// A stream declares no result: there is nothing for it to yield.
    | StreamWithoutResult of contract: string * methodId: string
    /// A method that writes declares a cache lifetime.
    | WriteWithCacheLifetime of contract: string * methodId: string
    /// A cache lifetime is not positive.
    | CacheLifetimeNotPositive of contract: string * methodId: string * seconds: int
    /// An invalidation names no method, or names one twice.
    | MalformedInvalidation of contract: string * methodId: string
    /// An invalidation names a method the contract does not declare.
    | InvalidatesUnknownMethod of contract: string * methodId: string * target: string
    /// A rate limit's count or window is not positive.
    | RateLimitNotPositive of contract: string * methodId: string
    /// An audit kind is empty or ill-formed text.
    | MalformedAuditKind of contract: string * methodId: string
    /// A description is ill-formed text (an unpaired surrogate), which has no canonical bytes.
    | IllFormedDescription of contract: string * owner: string
    /// An argument's name is not an identifier.
    | ArgumentNameNotIdentifier of contract: string * methodId: string * argument: string
    /// Two arguments of one method share a name.
    | DuplicateArgument of contract: string * methodId: string * argument: string
    /// An argument is declared host-only: it would never cross, so it is no argument.
    | HostOnlyArgument of contract: string * methodId: string * argument: string
    /// An argument's or a result's type has no wire form: it mentions a closure, a function, an
    /// opaque host value or a type variable. `slot` is the argument's name, or `result`.
    | TypeWithoutWireForm of contract: string * methodId: string * slot: string
    /// An argument or a result is of an enumeration that declares no case: no value could be sent.
    | EmptyEnumeration of contract: string * methodId: string * slot: string * enum: string
    /// A constraint does not fit its argument's type.
    | ConstraintMismatch of contract: string * methodId: string * argument: string
    /// A constraint admits no value: its lower bound exceeds its upper, a float bound is not
    /// finite, or a length is negative.
    | EmptyConstraint of contract: string * methodId: string * argument: string
    /// A declared default does not fit the argument's type, or lies outside its constraint.
    | DefaultOutOfType of contract: string * methodId: string * argument: string * detail: string

/// A lookup's refusal (Phase 644): what `Contract.methodAt` answers when there is no such method
/// to call.
[<RequireQualifiedAccess>]
type MethodRefusal =
    /// The vocabulary declares no contract of that name.
    | UnknownContract of contract: string
    /// The contract declares no method of that id.
    | UnknownMethod of contract: string * methodId: string
    /// The method does not serve the schema version asked for; `served` is what it does serve.
    | VersionNotServed of contract: string * methodId: string * version: int * served: int list

/// The `ContractFault` companions (Phase 644).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ContractFault =

    /// The refusal's NAME: the case's own, in lower camel case. What a vector and a refused
    /// document's detail carry.
    let name (f: ContractFault) : string =
        match f with
        | ContractFault.ContractNameNotIdentifier _ -> "contractNameNotIdentifier"
        | ContractFault.DuplicateContract _ -> "duplicateContract"
        | ContractFault.MethodIdNotIdentifier _ -> "methodIdNotIdentifier"
        | ContractFault.DuplicateMethod _ -> "duplicateMethod"
        | ContractFault.NoAuthority _ -> "noAuthority"
        | ContractFault.MalformedAuthority _ -> "malformedAuthority"
        | ContractFault.ServesNoVersion _ -> "servesNoVersion"
        | ContractFault.MalformedVersion _ -> "malformedVersion"
        | ContractFault.StreamWithReadPolicy _ -> "streamWithReadPolicy"
        | ContractFault.StreamWithoutResult _ -> "streamWithoutResult"
        | ContractFault.WriteWithCacheLifetime _ -> "writeWithCacheLifetime"
        | ContractFault.CacheLifetimeNotPositive _ -> "cacheLifetimeNotPositive"
        | ContractFault.MalformedInvalidation _ -> "malformedInvalidation"
        | ContractFault.InvalidatesUnknownMethod _ -> "invalidatesUnknownMethod"
        | ContractFault.RateLimitNotPositive _ -> "rateLimitNotPositive"
        | ContractFault.MalformedAuditKind _ -> "malformedAuditKind"
        | ContractFault.IllFormedDescription _ -> "illFormedDescription"
        | ContractFault.ArgumentNameNotIdentifier _ -> "argumentNameNotIdentifier"
        | ContractFault.DuplicateArgument _ -> "duplicateArgument"
        | ContractFault.HostOnlyArgument _ -> "hostOnlyArgument"
        | ContractFault.TypeWithoutWireForm _ -> "typeWithoutWireForm"
        | ContractFault.EmptyEnumeration _ -> "emptyEnumeration"
        | ContractFault.ConstraintMismatch _ -> "constraintMismatch"
        | ContractFault.EmptyConstraint _ -> "emptyConstraint"
        | ContractFault.DefaultOutOfType _ -> "defaultOutOfType"

    /// The refusal in one sentence, led by its name: `noAuthority: method 'orders.get' declares no
    /// authority; …`.
    let describe (f: ContractFault) : string =
        let m (c: string) (id: string) = "method '" + c + "." + id + "'"

        let slotOf (slot: string) =
            if slot = "result" then
                "its result"
            else
                "argument '" + slot + "'"

        let text =
            match f with
            | ContractFault.ContractNameNotIdentifier c ->
                "contract '"
                + c
                + "' is not an identifier (an ASCII letter or '_', then letters, digits, '_')"
            | ContractFault.DuplicateContract c -> "contract '" + c + "' is declared more than once"
            | ContractFault.MethodIdNotIdentifier(c, id) ->
                m c id
                + " has an id that is not an identifier (an ASCII letter or '_', then letters, digits, '_')"
            | ContractFault.DuplicateMethod(c, id) -> m c id + " is declared more than once"
            | ContractFault.NoAuthority(c, id) ->
                m c id
                + " declares no authority; a method open to every caller says so with a value its host defines"
            | ContractFault.MalformedAuthority(c, id, v) ->
                m c id
                + " declares the authority value '"
                + v
                + "', which is empty, ill-formed text, or listed twice"
            | ContractFault.ServesNoVersion(c, id) -> m c id + " serves no schema version"
            | ContractFault.MalformedVersion(c, id, v) ->
                m c id
                + " serves version "
                + string v
                + ", which is not positive or is listed twice"
            | ContractFault.StreamWithReadPolicy(c, id) ->
                m c id
                + " is a stream and declares a read policy; a stream's answers are not one answer to reuse"
            | ContractFault.StreamWithoutResult(c, id) -> m c id + " is a stream and declares no result to yield"
            | ContractFault.WriteWithCacheLifetime(c, id) ->
                m c id
                + " writes to its host and declares a cache lifetime; only a read's answer may be reused"
            | ContractFault.CacheLifetimeNotPositive(c, id, s) ->
                m c id
                + " declares a cache lifetime of "
                + string s
                + " seconds; a lifetime is positive"
            | ContractFault.MalformedInvalidation(c, id) ->
                m c id + " declares an invalidation that names no method, or names one twice"
            | ContractFault.InvalidatesUnknownMethod(c, id, t) ->
                m c id + " invalidates '" + t + "', which contract '" + c + "' does not declare"
            | ContractFault.RateLimitNotPositive(c, id) ->
                m c id + " declares a rate limit whose count or window is not positive"
            | ContractFault.MalformedAuditKind(c, id) ->
                m c id + " declares an audit kind that is empty or ill-formed text"
            | ContractFault.IllFormedDescription(c, owner) ->
                "contract '"
                + c
                + "': the description of "
                + owner
                + " is ill-formed text (an unpaired surrogate)"
            | ContractFault.ArgumentNameNotIdentifier(c, id, a) ->
                m c id
                + ": argument '"
                + a
                + "' is not an identifier (an ASCII letter or '_', then letters, digits, '_')"
            | ContractFault.DuplicateArgument(c, id, a) -> m c id + ": argument '" + a + "' is declared more than once"
            | ContractFault.HostOnlyArgument(c, id, a) ->
                m c id
                + ": argument '"
                + a
                + "' is host-only; an argument that never crosses is not an argument"
            | ContractFault.TypeWithoutWireForm(c, id, slot) ->
                m c id
                + ": the type of "
                + slotOf slot
                + " has no wire form (a closure, a function, an opaque host value or a type variable)"
            | ContractFault.EmptyEnumeration(c, id, slot, e) ->
                m c id
                + ": "
                + slotOf slot
                + " is of enum '"
                + e
                + "', which declares no case, so no value could be sent"
            | ContractFault.ConstraintMismatch(c, id, a) ->
                m c id
                + ": the constraint on argument '"
                + a
                + "' does not fit its type (an integer range fits an int, a float range a float, a length a string)"
            | ContractFault.EmptyConstraint(c, id, a) ->
                m c id
                + ": the constraint on argument '"
                + a
                + "' admits no value (its bounds are out of order, not finite, or negative)"
            | ContractFault.DefaultOutOfType(c, id, a, detail) ->
                m c id
                + ": the default of argument '"
                + a
                + "' is not a value it admits ("
                + detail
                + ")"

        name f + ": " + text

/// The contract rules and the readings every consumer shares (Phase 644).
[<RequireQualifiedAccess>]
module Contract =

    /// Whether `s` is a name every backend can spell bare as an identifier: an ASCII letter
    /// or `_`, then ASCII letters, digits and `_`. Every declared NAME the generators emit
    /// as an identifier (a kind or op tag, a type name, a case, a field, a type parameter, a
    /// contract, a method, an argument) is held to it — an identifier cannot be escaped, so a name
    /// that is not one is a splice of source text into the generated module.
    let internal isIdentifier (s: string) : bool =
        let letter (c: char) =
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c = '_'

        s.Length > 0
        && letter s[0]
        && s |> Seq.forall (fun c -> letter c || (c >= '0' && c <= '9'))

    /// A method's NAME, everywhere it is named: `<contract>.<method>`. Both halves are identifiers,
    /// which hold no `.`, so two methods have one name exactly when they are one method.
    let methodName (contract: string) (methodId: string) : string = contract + "." + methodId

    /// Whether a method WRITES: its effect reaches its host to change it.
    let writes (m: IdlMethod) : bool = m.Effect.Host = WritesHost

    /// The method `methodId` of `contract` as schema `version` reaches it, or the refusal that
    /// says why there is none: an unknown contract, an unknown method, or a version the method
    /// does not serve (`MethodRefusal.VersionNotServed`, naming the versions it does).
    let methodAt (idl: Idl) (contract: string) (methodId: string) (version: int) : Result<IdlMethod, MethodRefusal> =
        match idl.Contracts |> List.tryFind (fun c -> c.Name = contract) with
        | None -> Error(MethodRefusal.UnknownContract contract)
        | Some c ->
            match c.Methods |> List.tryFind (fun m -> m.Id = methodId) with
            | None -> Error(MethodRefusal.UnknownMethod(contract, methodId))
            | Some m ->
                if List.contains version m.Serves then
                    Ok m
                else
                    Error(MethodRefusal.VersionNotServed(contract, methodId, version, m.Serves))

    /// Whether a type expression mentions a type with no wire form of its own: a closure, a
    /// function, an opaque host value, or a type variable (a contract binds none).
    let rec internal lacksWireForm (t: IdlType) : bool =
        match t with
        | TClosure
        | TFn _
        | TOpaque
        | TVar _ -> true
        | TList inner
        | TMap inner
        | TSet inner -> lacksWireForm inner
        | TUnion(_, args)
        | TTuple args -> args |> List.exists lacksWireForm
        | TKeyedMap(k, v) -> lacksWireForm k || lacksWireForm v
        | _ -> false

    /// The repeated members of a list, each once, at its second occurrence's index.
    let private repeats (xs: 'a list) : (int * 'a) list =
        xs
        |> List.indexed
        |> List.filter (fun (i, x) -> xs |> List.take i |> List.contains x)
        |> List.distinctBy snd

    let private finite (f: float) =
        not (System.Double.IsNaN f || System.Double.IsInfinity f)

    /// What is wrong with a constraint at a type, if anything.
    let private constraintFault
        (t: IdlType)
        (c: ArgumentConstraint)
        : (string * string * string -> ContractFault) option =
        match t, c with
        | TInt, ArgumentConstraint.IntBetween(lo, hi) -> if lo > hi then Some ContractFault.EmptyConstraint else None
        | TFloat, ArgumentConstraint.FloatBetween(lo, hi) ->
            if not (finite lo && finite hi) || lo > hi then
                Some ContractFault.EmptyConstraint
            else
                None
        | TStr, ArgumentConstraint.LengthBetween(lo, hi) ->
            if lo > hi || hi < 0 then
                Some ContractFault.EmptyConstraint
            else
                None
        | _ -> Some ContractFault.ConstraintMismatch

    /// Whether a default the type admits also lies inside the argument's constraint.
    let private insideConstraint (c: ArgumentConstraint) (v: IdlValue) : bool =
        match c, v with
        | ArgumentConstraint.IntBetween(lo, hi), VInt i -> lo <= i && i <= hi
        | ArgumentConstraint.FloatBetween(lo, hi), VFloat f -> lo <= f && f <= hi
        | ArgumentConstraint.FloatBetween(lo, hi), VInt i -> lo <= float i && float i <= hi
        | ArgumentConstraint.LengthBetween(lo, hi), VStr s -> lo <= s.Length && s.Length <= hi
        | _ -> true

    /// Every way the vocabulary's contracts are not well-formed, each at its owner's path in
    /// `idl.json` (`$["contracts"][i]["methods"][j]…`), in declaration order. Empty for a
    /// vocabulary that declares no contract. A type that names an undeclared enum, union or record
    /// is `Declare.errors`' to report, with every other dangling name.
    let faults (idl: Idl) : (PathSegment list * ContractFault) list =
        let key (k: string) = PathSegment.Key k
        let item (i: int) = PathSegment.Index i
        let illFormed (s: string) = not (SourceLit.isWellFormed s)

        let slotFaults (c: string) (id: string) (path: PathSegment list) (slot: string) (t: IdlType) =
            [ if lacksWireForm t then
                  path, ContractFault.TypeWithoutWireForm(c, id, slot)
              else
                  match t with
                  | TEnum e ->
                      match IdlLookup.tryEnum idl e with
                      | Some decl when List.isEmpty decl.Cases -> path, ContractFault.EmptyEnumeration(c, id, slot, e)
                      | _ -> ()
                  | _ -> () ]

        [ for i, c in List.indexed idl.Contracts do
              let cAt = [ key "contracts"; item i ]

              if not (isIdentifier c.Name) then
                  cAt @ [ key "name" ], ContractFault.ContractNameNotIdentifier c.Name

              if idl.Contracts |> List.take i |> List.exists (fun o -> o.Name = c.Name) then
                  cAt @ [ key "name" ], ContractFault.DuplicateContract c.Name

              match c.Doc with
              | Some d when illFormed d ->
                  cAt @ [ key "doc" ], ContractFault.IllFormedDescription(c.Name, "the contract")
              | _ -> ()

              for j, m in List.indexed c.Methods do
                  let mAt = cAt @ [ key "methods"; item j ]

                  if not (isIdentifier m.Id) then
                      mAt @ [ key "id" ], ContractFault.MethodIdNotIdentifier(c.Name, m.Id)

                  if c.Methods |> List.take j |> List.exists (fun o -> o.Id = m.Id) then
                      mAt @ [ key "id" ], ContractFault.DuplicateMethod(c.Name, m.Id)

                  match m.Doc with
                  | Some d when illFormed d ->
                      mAt @ [ key "doc" ], ContractFault.IllFormedDescription(c.Name, "method '" + m.Id + "'")
                  | _ -> ()

                  // Authority: declared, and each value text a host could compare.
                  if List.isEmpty m.Authority then
                      mAt @ [ key "authority" ], ContractFault.NoAuthority(c.Name, m.Id)

                  for k, v in List.indexed m.Authority do
                      if v = "" || illFormed v then
                          mAt @ [ key "authority"; item k ], ContractFault.MalformedAuthority(c.Name, m.Id, v)

                  for k, v in repeats m.Authority do
                      mAt @ [ key "authority"; item k ], ContractFault.MalformedAuthority(c.Name, m.Id, v)

                  // Versions.
                  if List.isEmpty m.Serves then
                      mAt @ [ key "serves" ], ContractFault.ServesNoVersion(c.Name, m.Id)

                  for k, v in List.indexed m.Serves do
                      if v <= 0 then
                          mAt @ [ key "serves"; item k ], ContractFault.MalformedVersion(c.Name, m.Id, v)

                  for k, v in repeats m.Serves do
                      mAt @ [ key "serves"; item k ], ContractFault.MalformedVersion(c.Name, m.Id, v)

                  // Shape and read policy.
                  if m.Shape = MethodShape.Stream then
                      if m.ReadPolicy <> MethodReadPolicy.Unstated then
                          mAt @ [ key "readPolicy" ], ContractFault.StreamWithReadPolicy(c.Name, m.Id)

                      if m.Result.IsNone then
                          mAt @ [ key "result" ], ContractFault.StreamWithoutResult(c.Name, m.Id)

                  match m.ReadPolicy with
                  | MethodReadPolicy.Unstated -> ()
                  | MethodReadPolicy.Cached seconds ->
                      if writes m then
                          mAt @ [ key "readPolicy" ], ContractFault.WriteWithCacheLifetime(c.Name, m.Id)

                      if seconds <= 0 then
                          mAt @ [ key "readPolicy"; key "seconds" ],
                          ContractFault.CacheLifetimeNotPositive(c.Name, m.Id, seconds)
                  | MethodReadPolicy.Invalidates targets ->
                      if List.isEmpty targets || not (List.isEmpty (repeats targets)) then
                          mAt @ [ key "readPolicy"; key "methods" ], ContractFault.MalformedInvalidation(c.Name, m.Id)

                      for k, t in List.indexed targets do
                          if not (c.Methods |> List.exists (fun o -> o.Id = t)) then
                              mAt @ [ key "readPolicy"; key "methods"; item k ],
                              ContractFault.InvalidatesUnknownMethod(c.Name, m.Id, t)

                  match m.RateLimit with
                  | Some r when r.Calls <= 0 || r.Seconds <= 0 ->
                      mAt @ [ key "rateLimit" ], ContractFault.RateLimitNotPositive(c.Name, m.Id)
                  | _ -> ()

                  match m.AuditKind with
                  | Some a when a = "" || illFormed a ->
                      mAt @ [ key "auditKind" ], ContractFault.MalformedAuditKind(c.Name, m.Id)
                  | _ -> ()

                  // The result.
                  match m.Result with
                  | Some t -> yield! slotFaults c.Name m.Id (mAt @ [ key "result" ]) "result" t
                  | None -> ()

                  // The arguments.
                  for k, a in List.indexed m.Arguments do
                      let aAt = mAt @ [ key "arguments"; item k ]

                      if not (isIdentifier a.Name) then
                          aAt @ [ key "name" ], ContractFault.ArgumentNameNotIdentifier(c.Name, m.Id, a.Name)

                      if m.Arguments |> List.take k |> List.exists (fun o -> o.Name = a.Name) then
                          aAt @ [ key "name" ], ContractFault.DuplicateArgument(c.Name, m.Id, a.Name)

                      match a.Doc with
                      | Some d when illFormed d ->
                          aAt @ [ key "doc" ],
                          ContractFault.IllFormedDescription(
                              c.Name,
                              "argument '" + a.Name + "' of method '" + m.Id + "'"
                          )
                      | _ -> ()

                      let typeFaults = slotFaults c.Name m.Id (aAt @ [ key "type" ]) a.Name a.Type
                      yield! typeFaults

                      let constraintOk =
                          match a.Constraint with
                          | None -> true
                          | Some ac ->
                              match constraintFault a.Type ac with
                              | None -> true
                              | Some _ -> false

                      match a.Constraint |> Option.bind (constraintFault a.Type) with
                      | Some mk -> aAt @ [ key "constraint" ], mk (c.Name, m.Id, a.Name)
                      | None -> ()

                      match a.Opt with
                      | HostOnly -> aAt @ [ key "optionality" ], ContractFault.HostOnlyArgument(c.Name, m.Id, a.Name)
                      | OmitDefault d when List.isEmpty typeFaults ->
                          let at = aAt @ [ key "optionality"; key "default" ]

                          match Encode.renderValue idl a.Type d with
                          | Error detail -> at, ContractFault.DefaultOutOfType(c.Name, m.Id, a.Name, detail)
                          | Ok _ ->
                              match a.Constraint with
                              | Some ac when constraintOk && not (insideConstraint ac d) ->
                                  at,
                                  ContractFault.DefaultOutOfType(c.Name, m.Id, a.Name, "it lies outside the constraint")
                              | _ -> ()
                      | _ -> () ]
