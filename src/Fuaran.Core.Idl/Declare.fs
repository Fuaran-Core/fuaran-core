namespace Fuaran.Core.Idl

open Fuaran.Core

/// One way a declared vocabulary is not well-formed (Phase 520): the OWNER'S PATH — the
/// declaration at fault, in the vocabulary's own lists in declaration order, which is its path in
/// the `idl.json` it was read from (`$["kinds"][2]["fields"][0]["type"]`) — and a detail naming the
/// rule it breaks. `Declare.errors` answers a list of these; `Artifact.ofJsonDetailed` refuses each
/// as a `SchemaFault` at its path.
type VocabularyFault =
    {
        /// Where the declaration at fault is, root-first.
        At: PathSegment list
        /// What is wrong with it — a sentence for a reader, naming the owner and the rule.
        Detail: string
    }

/// The `VocabularyFault` companions (Phase 520).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module VocabularyFault =

    /// Path and detail on one line: `$["kinds"][0]["tag"]: kind 'a b' is not an identifier …`.
    let describe (f: VocabularyFault) : string =
        DecodePath.render f.At + ": " + f.Detail

/// Declaration helpers for the IDL's hand-authored parts.
[<RequireQualifiedAccess>]
module Declare =

    /// An enum whose wire strings ARE its case names — the common case, and the
    /// shape every declaration had before Phase 707.
    let enumOf (name: string) (cases: string list) : IdlEnum =
        { Name = name
          Cases = cases
          Wires = []
          CaseAnnotations = []
          Aliases = []
          Annotations = Annotations.Empty }

    /// An enum whose wire strings differ from its host case names, declared as
    /// `(case, wire)` pairs. Taking PAIRS rather than two lists is the point: the
    /// parallel-arity invariant [[Idl.IdlEnum]] carries cannot be stated wrongly
    /// here, so the only way to violate it is to build the record by hand — which
    /// [[enumWireErrors]] then catches.
    let enumWith (name: string) (cases: (string * string) list) : IdlEnum =
        { Name = name
          Cases = cases |> List.map fst
          Wires = cases |> List.map snd
          CaseAnnotations = []
          Aliases = []
          Annotations = Annotations.Empty }

    /// Declare what is true ABOUT some of an enum's cases (Phase 119), by HOST case
    /// name. Sparse — name only the cases that say something; the rest read as
    /// [[Annotations.Empty]] through [[IdlEnum.AnnotationsOf]].
    ///
    /// Present as a helper rather than left to `{ e with CaseAnnotations = … }` for the
    /// reason [[enumWith]] is: it is the one construction site that can check the case
    /// exists at the moment the claim is made, rather than leaving it to
    /// [[enumWireErrors]] to find later. A name the enum does not declare is refused
    /// here — an annotation on nothing is a typo, not a declaration.
    ///
    /// Phase 384 — the refusal is an ERROR LIST, as [[enumWireErrors]]' is and in its
    /// sentences: one per annotation naming a case the enum does not declare, and one when
    /// two entries name the same case. It used to raise, the one `Declare.*` function
    /// that did.
    let enumAnnotate (annotations: (string * Annotations) list) (e: IdlEnum) : Result<IdlEnum, string list> =
        let annotated = annotations |> List.map fst

        let errors =
            [ for c in annotated do
                  if not (List.contains c e.Cases) then
                      sprintf "enum '%s': annotation names case '%s', which the enum does not declare" e.Name c

              if List.length (List.distinct annotated) <> List.length annotated then
                  sprintf "enum '%s': two annotation entries name the same case" e.Name ]

        match errors with
        | [] -> Ok { e with CaseAnnotations = annotations }
        | _ -> Error errors

    // ---- the owners' paths (Phase 520) ----
    //
    // A fault names the position of the declaration at fault in the vocabulary's own lists, in
    // declaration order — the path the same declaration has in the `idl.json` it was read from,
    // whose members these are (`Artifact.json`).

    let private key (k: string) = PathSegment.Key k
    let private item (i: int) = PathSegment.Index i

    let private fault (at: PathSegment list) (detail: string) : VocabularyFault = { At = at; Detail = detail }

    /// `$["enums"][i]`.
    let private enumAt (i: int) = [ key "enums"; item i ]

    /// The member of an enum that carries its HOST case names: `hostCases` for a wire-mapped
    /// enum, `cases` (which are then the names too) for one that is not.
    let private hostCasesOf (e: IdlEnum) =
        if List.isEmpty e.Wires then "cases" else "hostCases"

    /// The path of field `j` of each declared owner of fields (Phase 520).
    let private kindField (i: int) (j: int) =
        [ key "kinds"; item i; key "fields"; item j ]

    let private opField (i: int) (j: int) =
        [ key "ops"; item i; key "fields"; item j ]

    let private recordField (i: int) (j: int) =
        [ key "records"; item i; key "fields"; item j ]

    let private caseField (i: int) (c: int) (j: int) =
        [ key "unions"; item i; key "cases"; item c; key "fields"; item j ]

    let private envelopeField (j: int) = [ key "nodeFields"; item j ]

    /// Well-formedness of every enum's case↔wire mapping — the backstop for a
    /// record built by literal rather than through [[enumOf]] / [[enumWith]].
    /// Empty list ⇒ well-formed. Checks the arity the pair-taking constructor
    /// makes unrepresentable, plus the two duplicate classes that would make the
    /// mapping non-invertible (a repeated case name, or two cases sharing one
    /// wire string — the latter silently collapses on decode). Each fault is at its
    /// enum's path (Phase 520).
    let enumWireErrors (idl: Idl) : VocabularyFault list =
        [ for i, e in List.indexed idl.Enums do
              let cases, wires = e.Cases, e.Wires
              let at = enumAt i

              if not (List.isEmpty wires) && List.length wires <> List.length cases then
                  fault
                      (at @ [ key "hostCases" ])
                      (sprintf
                          "enum '%s': %d case(s) but %d wire string(s) — the lists must be parallel (or Wires empty)"
                          e.Name
                          (List.length cases)
                          (List.length wires))

              if List.length (List.distinct cases) <> List.length cases then
                  fault (at @ [ key (hostCasesOf e) ]) (sprintf "enum '%s': duplicate case name" e.Name)

              if
                  not (List.isEmpty wires)
                  && List.length (List.distinct wires) <> List.length wires
              then
                  fault
                      (at @ [ key "cases" ])
                      (sprintf "enum '%s': two cases share a wire string — decoding would not be invertible" e.Name)

              // Phase 119 — the one class the sparse, keyed [[IdlEnum.CaseAnnotations]]
              // shape admits and the positional one could not: a name that is not a case.
              // [[Declare.enumAnnotate]] refuses it at the construction site; this is the
              // backstop for a record built by literal, exactly as the arity check above is.
              let annotated = e.CaseAnnotations |> List.map fst

              for c in annotated do
                  if not (List.contains c cases) then
                      fault
                          (at @ [ key "caseAnnotations" ])
                          (sprintf "enum '%s': annotation names case '%s', which the enum does not declare" e.Name c)

              if List.length (List.distinct annotated) <> List.length annotated then
                  fault
                      (at @ [ key "caseAnnotations" ])
                      (sprintf "enum '%s': two annotation entries name the same case" e.Name) ]

    /// Well-formedness of every hosted slot's declared wire form (Phase 252). Empty
    /// list ⇒ well-formed. A wire form is a type the other legs can read without the
    /// host codec — a scalar, a declared enum, record or union, or a list or map of
    /// those — so another erased slot (`json`, `hosted`, a closure, a sentinel), a
    /// node, a tree-op or a type variable is refused; a format needs a string wire (a
    /// format the engine does not know is unrepresentable since Phase 391, and the
    /// `idl.json` reader refuses its name). Each fault is at its field's `type` (Phase 520).
    let hostedWireErrors (idl: Idl) : VocabularyFault list =
        let rec readable (t: IdlType) : string option =
            match WireForm.ofType idl t with
            | WireForm.Str
            | WireForm.Int
            | WireForm.Bool
            | WireForm.Float
            | WireForm.Enum _
            | WireForm.Record _ -> None
            | WireForm.Union u -> u.Args |> List.tryPick readable
            | WireForm.Undeclared(Undeclared.Enum n | Undeclared.Record n | Undeclared.Union(n, _)) ->
                Some(sprintf "names '%s', which the vocabulary does not declare" n)
            | WireForm.List inner
            | WireForm.Map inner -> readable inner
            | _ -> Some(sprintf "is %A, which no leg but the host codec can read" t)

        let rec hostedIn (t: IdlType) : HostedCodec list =
            match t with
            | THosted h -> [ h ]
            | TList inner
            | TMap inner -> hostedIn inner
            | TUnion(_, args) -> args |> List.collect hostedIn
            | _ -> []

        let fieldSets =
            [ for i, k in List.indexed idl.Kinds -> "kind " + k.Tag, kindField i, k.Fields
              for i, o in List.indexed idl.Ops -> "op " + o.Tag, opField i, o.Fields
              for i, r in List.indexed idl.Records -> "record " + r.Name, recordField i, r.Fields
              for i, u in List.indexed idl.Unions do
                  for c, case in List.indexed u.Cases ->
                      sprintf "union %s.%s" u.Name case.Tag, caseField i c, case.Fields
              yield "the node envelope", envelopeField, idl.NodeFields ]

        [ for owner, fieldAt, fields in fieldSets do
              for j, f in List.indexed fields do
                  for h in hostedIn f.Type do
                      let path = fieldAt j @ [ key "type" ]
                      let at = sprintf "%s, field '%s' (hosted %s)" owner f.Name h.FSharp

                      match h.Wire |> Option.bind readable with
                      | Some why -> fault path (sprintf "%s: the declared wire form %s" at why)
                      | None -> ()

                      match h.Format, h.Wire with
                      | Some fmt, w when w <> Some TStr ->
                          fault
                              path
                              (sprintf
                                  "%s: format '%s' needs a string wire form (Wire = Some TStr)"
                                  at
                                  (HostedFormat.name fmt))
                      | _ -> () ]

    /// Well-formedness of the declared wire shape (Phases 108/109). Empty list ⇒
    /// well-formed. The discriminator shares an object with a tagged body's own
    /// fields in EVERY shape, and in [[NodeEnvelopeShape.FlatKind]] the node's
    /// `id`, its kind fields and its declared envelope all share one object — so
    /// the reserved names are checked here rather than colliding silently on the
    /// wire. A fault about the key is at `$["wire"]["discriminator"]`; a collision is at the
    /// colliding field (Phase 520).
    let wireShapeErrors (idl: Idl) : VocabularyFault list =
        let disc = idl.Wire.Discriminator
        let flat = idl.Wire.NodeEnvelope = NodeEnvelopeShape.FlatKind
        let discAt = [ key "wire"; key "discriminator" ]

        [ if System.String.IsNullOrWhiteSpace disc then
              fault discAt "wire shape: the discriminator key must be a non-empty string"

          // The emitters splice the key into generated JS/F# source literals, so
          // quote-class characters are refused at declaration rather than emitted.
          if
              disc
              |> Seq.exists (fun c -> c = '"' || c = '\'' || c = '\\' || System.Char.IsControl c)
          then
              fault
                  discAt
                  "wire shape: the discriminator key must not contain quotes, backslashes or control characters"

          if disc = "id" then
              fault discAt "wire shape: the discriminator key 'id' collides with the node id"

          let fieldClash (owner: string) (fieldAt: int -> PathSegment list) (fields: IdlField list) =
              [ for j, f in List.indexed fields do
                    if f.Name = disc then
                        fault
                            (fieldAt j)
                            (sprintf "%s: field '%s' collides with the declared discriminator key" owner f.Name)

                    if flat && f.Name = "id" then
                        fault (fieldAt j) (sprintf "%s: field 'id' is reserved in the flat node envelope" owner) ]

          for i, k in List.indexed idl.Kinds do
              yield! fieldClash ("kind '" + k.Tag + "'") (kindField i) k.Fields

          for i, o in List.indexed idl.Ops do
              yield! fieldClash ("op '" + o.Tag + "'") (opField i) o.Fields

          for i, u in List.indexed idl.Unions do
              for c, case in List.indexed u.Cases do
                  for j, f in List.indexed case.Fields do
                      if f.Name = disc then
                          fault
                              (caseField i c j)
                              (sprintf
                                  "union '%s' case '%s': field '%s' collides with the declared discriminator key"
                                  u.Name
                                  case.Tag
                                  f.Name)

          yield! fieldClash "node envelope" envelopeField idl.NodeFields

          // Flat only: the envelope and every kind body share one object, so an
          // envelope name reappearing as a kind field is ambiguous on the wire.
          if flat then
              let envNames = idl.NodeFields |> List.map _.Name |> Set.ofList

              for i, k in List.indexed idl.Kinds do
                  for j, f in List.indexed k.Fields do
                      if envNames.Contains f.Name then
                          fault
                              (kindField i j)
                              (sprintf
                                  "kind '%s': field '%s' collides with a node-envelope field in the flat shape"
                                  k.Tag
                                  f.Name) ]

    /// Whether `s` is a name every backend can spell bare as an identifier: an ASCII letter
    /// or `_`, then ASCII letters, digits and `_`. Every declared NAME the generators emit
    /// as an identifier (a kind or op tag, a type name, a case, a field, a type parameter)
    /// is held to it — an identifier cannot be escaped, so a name that is not one is a
    /// splice of source text into the generated module.
    let private isIdentifier (s: string) : bool =
        let letter (c: char) =
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c = '_'

        s.Length > 0
        && letter s[0]
        && s |> Seq.forall (fun c -> letter c || (c >= '0' && c <= '9'))

    /// Whether `s` holds an unpaired surrogate — text with no UTF-8 encoding, so no canonical
    /// bytes, and no spelling in an F# or F* literal.
    let private illFormed (s: string) : bool = not (SourceLit.isWellFormed s)

    /// The field names no vocabulary may declare (Phase 348, DECISIONS.md D114): every name a
    /// JavaScript object carries before anything is written to it — `Object.prototype`'s own
    /// members, `__proto__` among them. A generated TypeScript host holds a node, a spec, a
    /// record and a union case as plain objects, so a field of one of these names is not data
    /// there: `__proto__` written in an object literal SETS the prototype (the member is never
    /// held, and the encoder reads the prototype back), and an absent optional member of any
    /// other reads as the inherited function (the encoder then throws). The rule is the IDL's,
    /// so it binds every host: a vocabulary is one document, and a name one host cannot carry
    /// is a name the vocabulary does not have. `prototype` is NOT here — a plain object has no
    /// such member to inherit, and it measured clean on every path.
    let private inheritedMemberNames: Set<string> =
        set
            [ "__proto__"
              "__defineGetter__"
              "__defineSetter__"
              "__lookupGetter__"
              "__lookupSetter__"
              "constructor"
              "hasOwnProperty"
              "isPrototypeOf"
              "propertyIsEnumerable"
              "toLocaleString"
              "toString"
              "valueOf" ]

    /// Whether `s` carries a control character or a line break (U+0085, U+2028, U+2029
    /// included) — what a single-line slot spliced into a comment must not carry.
    let private controlOrBreak (s: string) : bool =
        s
        |> Seq.exists (fun c -> System.Char.IsControl c || c = '\u2028' || c = '\u2029')

    /// **Every well-formedness rule a vocabulary is held to, in one call (Phase 292)** —
    /// the union of [[wireShapeErrors]], [[enumWireErrors]] and [[hostedWireErrors]] with
    /// the referential and lexical checks nothing used to make. Empty list ⇒ well-formed.
    /// Each fault names its OWNER'S PATH (Phase 520) — the declaration at fault, in the
    /// vocabulary's own lists, which is its path in the `idl.json` it was read from — beside a
    /// detail naming the rule it breaks.
    ///
    /// An `idl.json` is UNTRUSTED input (DECISIONS D95): `Artifact.ofJson` and
    /// `Proposal.applyDelta` run this and refuse a vocabulary it reports, naming every
    /// error, so a hand-edited artifact never reaches an emitter. The checks beyond the
    /// three it unions:
    ///
    /// - **references** — every `TEnum` / `TRecord` / `TUnion` name resolves, every union is
    ///   applied to as many type arguments as it declares parameters, and a `TVar` names a
    ///   parameter of the union whose case declares it;
    /// - **duplicates** — kind tags, op tags, type names (enums, unions and records share
    ///   one namespace in every generated host), case tags, type parameters, field names
    ///   within one owner, and default entries;
    /// - **defaults** — an `OmitDefault` value, and an [[IdlDefault]] addressing a declared
    ///   field, are values the GUARDED encoder writes at the slot's type (Phase 520: checked
    ///   through the encoder's own guarded renderer, so "fits" means "encodes to wire its decoder
    ///   reads" — a map default carrying one key twice is refused HERE, at its path, not at the
    ///   first `encode`); a slot whose type mentions a type variable is checked where it is
    ///   instantiated, by the encoder;
    /// - **slots** — a `HostOnly` field is a `TFn`; a declared transparent case carries
    ///   exactly one field, and that field cannot encode to an object, at its declaration
    ///   or at any instantiation of its union;
    /// - **the envelope** — `id` is reserved in every shape and `kind` beside it under
    ///   [[NodeEnvelopeShape.NestedKind]], where an envelope field of either name emitted a
    ///   duplicate key or an undecodable node;
    /// - **inherited member names** (Phase 348) — no field anywhere is named `__proto__` or
    ///   any other member every JavaScript object carries (`constructor`, `toString`, …);
    /// - **text** — every emitted NAME is an identifier; a category and a deprecation's
    ///   replacement, message and version are single-line text; and no declared string is
    ///   ill-formed UTF-16. A `Doc` (Phase 255) is free prose and may span lines.
    ///
    /// A field-less kind or record is well-formed: a marker carries its meaning in its tag.
    let errors (idl: Idl) : VocabularyFault list =
        let unionOf (n: string) = IdlLookup.tryUnion idl n

        let transparentField (u: UnionForm) : IdlField option =
            match u.Transparent with
            | Some tag ->
                match u.Decl.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                | Some { Fields = [ single ] } -> Some single
                | _ -> None
            | None -> None

        // Phase 539 — the values a presence condition may compare a sibling with: a scalar at its type,
        // and for an enum its CANONICAL wire string (an alias spelling would make one condition two).
        let canonicalScalar (t: IdlType) (v: IdlValue) : bool =
            match v, WireForm.ofType idl t with
            | VEnum w, WireForm.Enum e -> List.contains w e.WireCases
            | _ -> not (List.isEmpty (WireForm.scalarSpellings idl t v))

        let rec mentionsVar (t: IdlType) =
            match t with
            | TVar _ -> true
            | TList inner
            | TMap inner -> mentionsVar inner
            | TUnion(_, args) -> args |> List.exists mentionsVar
            | _ -> false

        let rec typeErrors
            (path: PathSegment list)
            (at: string)
            (pars: string list)
            (t: IdlType)
            : VocabularyFault list =
            match WireForm.ofType idl t with
            | WireForm.Undeclared(Undeclared.Enum n) ->
                [ fault path (sprintf "%s: names enum '%s', which the vocabulary does not declare" at n) ]
            | WireForm.Undeclared(Undeclared.Record n) ->
                [ fault path (sprintf "%s: names record '%s', which the vocabulary does not declare" at n) ]
            | WireForm.Undeclared(Undeclared.Union(n, args)) ->
                fault path (sprintf "%s: names union '%s', which the vocabulary does not declare" at n)
                :: (args |> List.collect (typeErrors path at pars))
            | WireForm.Union u ->
                let inner = u.Args |> List.collect (typeErrors path at pars)

                match u.Binding with
                | None ->
                    fault
                        path
                        (sprintf
                            "%s: applies union '%s' to %d type argument(s); it declares %d"
                            at
                            u.Name
                            (List.length u.Args)
                            (List.length u.Decl.Params))
                    :: inner
                | Some binding ->
                    match transparentField u with
                    | Some f when
                        mentionsVar f.Type
                        && WireForm.objectCapable idl (TypeParams.substitute binding f.Type)
                        ->
                        fault
                            path
                            (sprintf
                                "%s: instantiates union '%s' so that its transparent case's field '%s' can encode to an object — a bare object is indistinguishable from a tagged case"
                                at
                                u.Name
                                f.Name)
                        :: inner
                    | _ -> inner
            | WireForm.Unbound v when not (List.contains v pars) ->
                [ fault path (sprintf "%s: type variable '%s' is not a parameter of the union declaring it" at v) ]
            | WireForm.List inner
            | WireForm.Map inner -> typeErrors path at pars inner
            | _ -> []

        let annotationErrors (path: PathSegment list) (at: string) (a: Annotations) : VocabularyFault list =
            let path = path @ [ key "annotations" ]

            [ let single (slot: string) (v: string option) =
                  [ match v with
                    | Some s when controlOrBreak s ->
                        fault path (sprintf "%s: the annotation's %s carries a line break or control character" at slot)
                    | Some s when illFormed s ->
                        fault path (sprintf "%s: the annotation's %s is ill-formed UTF-16" at slot)
                    | _ -> () ]

              match a.Deprecated with
              | Some d ->
                  yield! single "deprecation replacement" d.Replacement
                  yield! single "deprecation message" d.Message
              | None -> ()

              yield! single "version (since)" a.Since

              match a.Doc with
              | Some d when illFormed d -> fault path (sprintf "%s: the annotation's doc is ill-formed UTF-16" at)
              | _ -> () ]

        /// Each name at its own path; a name declared more than once, at every repeat after the first.
        let nameErrors (what: string) (names: (PathSegment list * string) list) : VocabularyFault list =
            [ for path, n in names do
                  if not (isIdentifier n) then
                      fault
                          path
                          (sprintf
                              "%s '%s' is not an identifier (an ASCII letter or '_', then letters, digits, '_')"
                              what
                              n)

              for k, (path, n) in List.indexed names do
                  let earlier = names |> List.take k |> List.filter (fun (_, m) -> m = n)

                  if List.length earlier = 1 then
                      fault path (sprintf "%s '%s' is declared more than once" what n) ]

        let fieldErrors
            (owner: string)
            (fieldAt: int -> PathSegment list)
            (pars: string list)
            (fields: IdlField list)
            : VocabularyFault list =
            [ yield!
                  nameErrors (owner + ": field") (fields |> List.mapi (fun j f -> fieldAt j @ [ key "name" ], f.Name))

              // Phase 539 — every alias a member is read under is an identifier, and no two members of
              // one owner share a spelling: a document carrying it could not say which member it is.
              let spellingsOf =
                  [ for j, f in List.indexed fields do
                        yield j, [], f.Name

                        for a, alias in List.indexed f.Aliases do
                            yield j, [ key "aliases"; item a ], alias ]

              for k, (j, tail, n) in List.indexed spellingsOf do
                  if not (List.isEmpty tail) then
                      let path = fieldAt j @ tail

                      if not (isIdentifier n) then
                          fault
                              path
                              (sprintf
                                  "%s: the alias '%s' of field '%s' is not an identifier (an ASCII letter or '_', then letters, digits, '_')"
                                  owner
                                  n
                                  fields[j].Name)

                      if
                          spellingsOf
                          |> List.indexed
                          |> List.exists (fun (k', (_, t, m)) -> k' <> k && m = n && (k' < k || List.isEmpty t))
                      then
                          fault
                              path
                              (sprintf
                                  "%s: the alias '%s' of field '%s' is another spelling of a member of the same owner"
                                  owner
                                  n
                                  fields[j].Name)

                      if inheritedMemberNames.Contains n then
                          fault
                              path
                              (sprintf
                                  "%s: the alias '%s' is reserved — every JavaScript object already carries a member of that name"
                                  owner
                                  n)

              // Phase 539 — a presence condition is admitted on a member that CAN be absent, over a
              // sibling that is declared, wire-visible, unconditional and not the member itself; a
              // presence test needs a sibling that can be absent, an equality test a canonical scalar.
              for j, f in List.indexed fields do
                  match f.Presence with
                  | None -> ()
                  | Some c ->
                      let path = fieldAt j @ [ key "presence" ]
                      let at = sprintf "%s, field '%s'" owner f.Name

                      let siblingName =
                          match c with
                          | WhenPresent s
                          | WhenAbsent s
                          | WhenEquals(s, _) -> s

                      match f.Opt with
                      | Optional
                      | OmitDefault _ -> ()
                      | _ ->
                          fault
                              path
                              (sprintf
                                  "%s: a presence condition needs an Optional or OmitDefault member — a member whose condition fails is absent, and only those two say what absence means"
                                  at)

                      match fields |> List.tryFind (fun g -> g.Name = siblingName) with
                      | None ->
                          fault
                              path
                              (sprintf "%s: the presence condition names '%s', which is not a sibling" at siblingName)
                      | Some g when g.Name = f.Name ->
                          fault path (sprintf "%s: a presence condition cannot name its own member" at)
                      | Some g when g.Presence.IsSome ->
                          fault
                              path
                              (sprintf "%s: the presence condition names '%s', which is itself conditional" at g.Name)
                      | Some g when g.Opt = HostOnly ->
                          fault
                              path
                              (sprintf "%s: the presence condition names '%s', which is never on the wire" at g.Name)
                      | Some g ->
                          match c with
                          | WhenPresent _
                          | WhenAbsent _ when g.Opt <> Optional ->
                              fault
                                  path
                                  (sprintf
                                      "%s: a presence test needs an Optional sibling, and '%s' is never absent"
                                      at
                                      g.Name)
                          | WhenEquals(_, v) when not (canonicalScalar g.Type v) ->
                              fault
                                  path
                                  (sprintf
                                      "%s: the presence condition compares '%s' with a value that is not a canonical scalar of its type (a string, an int, a bool or an enum's wire string)"
                                      at
                                      g.Name)
                          | _ -> ()

              for j, f in List.indexed fields do
                  if inheritedMemberNames.Contains f.Name then
                      fault
                          (fieldAt j @ [ key "name" ])
                          (sprintf
                              "%s: field '%s' is reserved — every JavaScript object already carries a member of that name, so a generated TypeScript host cannot hold it as data"
                              owner
                              f.Name)

              for j, f in List.indexed fields do
                  let path = fieldAt j
                  let at = sprintf "%s, field '%s'" owner f.Name
                  yield! typeErrors (path @ [ key "type" ]) at pars f.Type
                  yield! annotationErrors path at f.Annotations

                  match f.Opt, f.Type with
                  | HostOnly, TFn _ -> ()
                  | HostOnly, other ->
                      fault
                          (path @ [ key "optionality" ])
                          (sprintf
                              "%s: a HostOnly field must be a TFn (it carries the host type and the placeholder), not %A"
                              at
                              other)
                  | OmitDefault d, t when not (mentionsVar t) ->
                      match Encode.renderValue idl t d with
                      | Ok _ -> ()
                      | Error m ->
                          fault
                              (path @ [ key "optionality"; key "default" ])
                              (sprintf "%s: the omit-at-default value does not fit the field's type (%s)" at m)
                  | _ -> () ]

        let kindErrors (what: string) (owner: PathSegment list) (k: IdlKind) : VocabularyFault list =
            let label = sprintf "%s '%s'" what k.Tag

            [ if controlOrBreak k.Category || illFormed k.Category then
                  fault
                      (owner @ [ key "category" ])
                      (sprintf "%s: the category carries a line break, a control character or ill-formed UTF-16" label)

              yield! annotationErrors owner label k.Annotations
              yield! fieldErrors label (fun j -> owner @ [ key "fields"; item j ]) [] k.Fields ]

        [ yield! wireShapeErrors idl
          yield! enumWireErrors idl
          yield! hostedWireErrors idl

          // Phase 539 — an enum alias names a declared case, is single-line well-formed text, and is
          // no other spelling of the enum: a wire string or another alias would decode two ways.
          for i, e in List.indexed idl.Enums do
              let at = enumAt i @ [ key "aliases" ]
              let keyed = e.Aliases |> List.map fst

              for c in keyed do
                  if not (List.contains c e.Cases) then
                      fault
                          at
                          (sprintf "enum '%s': an alias entry names case '%s', which the enum does not declare" e.Name c)

              if List.length (List.distinct keyed) <> List.length keyed then
                  fault at (sprintf "enum '%s': two alias entries name the same case" e.Name)

              let all = e.Aliases |> List.collect snd

              for a in all do
                  if a = "" || controlOrBreak a || illFormed a then
                      fault
                          at
                          (sprintf
                              "enum '%s': an alias is empty, carries a line break or a control character, or is ill-formed UTF-16"
                              e.Name)
                  elif List.contains a e.WireCases then
                      fault at (sprintf "enum '%s': the alias '%s' is one of the enum's wire strings" e.Name a)

              if List.length (List.distinct all) <> List.length all then
                  fault at (sprintf "enum '%s': one alias is declared twice" e.Name)

          // Names, and the namespaces they share.
          yield! nameErrors "kind" (idl.Kinds |> List.mapi (fun i k -> [ key "kinds"; item i; key "tag" ], k.Tag))
          yield! nameErrors "op" (idl.Ops |> List.mapi (fun i o -> [ key "ops"; item i; key "tag" ], o.Tag))

          yield!
              nameErrors
                  "type name"
                  ((idl.Enums |> List.mapi (fun i e -> enumAt i @ [ key "name" ], e.Name))
                   @ (idl.Unions
                      |> List.mapi (fun i u -> [ key "unions"; item i; key "name" ], u.Name))
                   @ (idl.Records
                      |> List.mapi (fun i r -> [ key "records"; item i; key "name" ], r.Name)))

          for i, k in List.indexed idl.Kinds do
              yield! kindErrors "kind" [ key "kinds"; item i ] k

          for i, o in List.indexed idl.Ops do
              yield! kindErrors "op" [ key "ops"; item i ] o

          for i, r in List.indexed idl.Records do
              yield! fieldErrors (sprintf "record '%s'" r.Name) (recordField i) [] r.Fields

          for i, u in List.indexed idl.Unions do
              let owner = sprintf "union '%s'" u.Name
              let at = [ key "unions"; item i ]

              yield!
                  nameErrors
                      (owner + ": type parameter")
                      (u.Params |> List.mapi (fun p n -> at @ [ key "params"; item p ], n))

              yield!
                  nameErrors
                      (owner + ": case")
                      (u.Cases
                       |> List.mapi (fun c case -> at @ [ key "cases"; item c; key "tag" ], case.Tag))

              for c, case in List.indexed u.Cases do
                  let caseOwner = sprintf "union '%s' case '%s'" u.Name case.Tag
                  yield! annotationErrors (at @ [ key "cases"; item c ]) caseOwner case.Annotations
                  yield! fieldErrors caseOwner (caseField i c) u.Params case.Fields

          for i, e in List.indexed idl.Enums do
              let owner = sprintf "enum '%s'" e.Name
              let at = enumAt i
              // A case NAME is the host identifier; its WIRE string is data, escaped where
              // it is spliced, and held only to well-formedness.
              for c, name in List.indexed e.Cases do
                  if not (isIdentifier name) then
                      fault
                          (at @ [ key (hostCasesOf e); item c ])
                          (sprintf "%s: case '%s' is not an identifier" owner name)

              for c, w in List.indexed e.WireCases do
                  if illFormed w then
                      fault (at @ [ key "cases"; item c ]) (sprintf "%s: a wire string is ill-formed UTF-16" owner)

              for c, a in e.CaseAnnotations do
                  yield!
                      annotationErrors
                          (at @ [ key "caseAnnotations"; key (e.WireOf c) ])
                          (sprintf "%s case '%s'" owner c)
                          a

          yield! fieldErrors "the node envelope" envelopeField [] idl.NodeFields

          // The envelope's reserved names (Phase 292 amendment): `id` beside the node's own
          // id in every shape — the flat shape's rule is [[wireShapeErrors]]' — and `kind`
          // beside the nested kind body.
          if idl.Wire.NodeEnvelope = NodeEnvelopeShape.NestedKind then
              for j, f in List.indexed idl.NodeFields do
                  if f.Name = "id" || f.Name = "kind" then
                      fault
                          (envelopeField j @ [ key "name" ])
                          (sprintf "the node envelope: field '%s' is reserved beside the nested kind body" f.Name)

          if illFormed idl.Wire.Discriminator then
              fault [ key "wire"; key "discriminator" ] "wire shape: the discriminator key is ill-formed UTF-16"

          // Declared (authoring) defaults: a value the guarded encoder writes at its slot's type,
          // once per address. An address naming no field is inert — no constructor reads it —
          // and the artifact carries it verbatim, so it is not refused here.
          for i, d in List.indexed idl.Defaults do
              let owner =
                  if d.Kind = "" then
                      Some idl.NodeFields
                  else
                      idl.Kinds |> List.tryFind (fun k -> k.Tag = d.Kind) |> Option.map _.Fields

              let at =
                  if d.Kind = "" then
                      sprintf "default for envelope field '%s'" d.Field
                  else
                      sprintf "default for '%s.%s'" d.Kind d.Field

              match owner |> Option.bind (List.tryFind (fun f -> f.Name = d.Field)) with
              | None -> ()
              | Some f when mentionsVar f.Type -> ()
              | Some f ->
                  match Encode.renderValue idl f.Type d.Value with
                  | Ok _ -> ()
                  | Error m ->
                      fault
                          [ key "defaults"; item i; key "value" ]
                          (sprintf "%s: the value does not fit the field's type (%s)" at m)

          for k, d in List.indexed idl.Defaults do
              let earlier =
                  idl.Defaults
                  |> List.take k
                  |> List.filter (fun e -> e.Kind = d.Kind && e.Field = d.Field)

              if List.length earlier = 1 then
                  fault
                      [ key "defaults"; item k ]
                      (sprintf "default for '%s.%s' is declared more than once" d.Kind d.Field)

          // Declared transparent cases — their WIRE consequence. Whether a harden token names
          // a member at all is the hardener's to check where it uses one (`Trust`), since a
          // token naming nothing changes no encoding.
          for k, (name, tag) in List.indexed idl.Harden.TransparentUnions do
              let path = [ key "harden"; key "transparentUnions"; item k ]
              let at = sprintf "harden policy: transparent case '%s.%s'" name tag

              match
                  unionOf name
                  |> Option.bind (fun u -> u.Cases |> List.tryFind (fun c -> c.Tag = tag))
              with
              | None -> ()
              | Some c ->
                  match c with
                  | { Fields = [ single ] } ->
                      if not (mentionsVar single.Type) && WireForm.objectCapable idl single.Type then
                          fault
                              path
                              (sprintf
                                  "%s: its field '%s' can encode to an object — a bare object is indistinguishable from a tagged case, and the hosts disagree on it"
                                  at
                                  single.Name)
                  | _ -> fault path (sprintf "%s must carry exactly one field — it is on the wire BARE" at)

          for k, (name, _) in List.indexed idl.Harden.TransparentUnions do
              let earlier =
                  idl.Harden.TransparentUnions
                  |> List.take k
                  |> List.filter (fun (n, _) -> n = name)

              if List.length earlier = 1 then
                  fault
                      [ key "harden"; key "transparentUnions"; item k ]
                      (sprintf "harden policy: union '%s' declares more than one transparent case" name) ]
