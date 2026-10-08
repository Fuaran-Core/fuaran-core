namespace Fuaran.Core.Idl

open Fuaran.Core

/// Declaration helpers for the IDL's hand-authored parts.
[<RequireQualifiedAccess>]
module Declare =

    /// An enum whose wire strings ARE its case names — the common case, and the
    /// shape every declaration had before Phase 707.
    let enumOf (name: string) (cases: string list) : IdlEnum =
        { Name = name
          Cases = cases
          Wires = []
          CaseAnnotations = [] }

    /// An enum whose wire strings differ from its host case names, declared as
    /// `(case, wire)` pairs. Taking PAIRS rather than two lists is the point: the
    /// parallel-arity invariant [[Idl.IdlEnum]] carries cannot be stated wrongly
    /// here, so the only way to violate it is to build the record by hand — which
    /// [[enumWireErrors]] then catches.
    let enumWith (name: string) (cases: (string * string) list) : IdlEnum =
        { Name = name
          Cases = cases |> List.map fst
          Wires = cases |> List.map snd
          CaseAnnotations = [] }

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

    /// Well-formedness of every enum's case↔wire mapping — the backstop for a
    /// record built by literal rather than through [[enumOf]] / [[enumWith]].
    /// Empty list ⇒ well-formed. Checks the arity the pair-taking constructor
    /// makes unrepresentable, plus the two duplicate classes that would make the
    /// mapping non-invertible (a repeated case name, or two cases sharing one
    /// wire string — the latter silently collapses on decode).
    let enumWireErrors (idl: Idl) : string list =
        [ for e in idl.Enums do
              let cases, wires = e.Cases, e.Wires

              if not (List.isEmpty wires) && List.length wires <> List.length cases then
                  sprintf
                      "enum '%s': %d case(s) but %d wire string(s) — the lists must be parallel (or Wires empty)"
                      e.Name
                      (List.length cases)
                      (List.length wires)

              if List.length (List.distinct cases) <> List.length cases then
                  sprintf "enum '%s': duplicate case name" e.Name

              if
                  not (List.isEmpty wires)
                  && List.length (List.distinct wires) <> List.length wires
              then
                  sprintf "enum '%s': two cases share a wire string — decoding would not be invertible" e.Name

              // Phase 119 — the one class the sparse, keyed [[IdlEnum.CaseAnnotations]]
              // shape admits and the positional one could not: a name that is not a case.
              // [[Declare.enumAnnotate]] refuses it at the construction site; this is the
              // backstop for a record built by literal, exactly as the arity check above is.
              let annotated = e.CaseAnnotations |> List.map fst

              for c in annotated do
                  if not (List.contains c cases) then
                      sprintf "enum '%s': annotation names case '%s', which the enum does not declare" e.Name c

              if List.length (List.distinct annotated) <> List.length annotated then
                  sprintf "enum '%s': two annotation entries name the same case" e.Name ]

    /// Well-formedness of every hosted slot's declared wire form (Phase 252). Empty
    /// list ⇒ well-formed. A wire form is a type the other legs can read without the
    /// host codec — a scalar, a declared enum, record or union, or a list or map of
    /// those — so another erased slot (`json`, `hosted`, a closure, a sentinel), a
    /// node, a tree-op or a type variable is refused; a format needs a string wire (a
    /// format the engine does not know is unrepresentable since Phase 391, and the
    /// `idl.json` reader refuses its name).
    let hostedWireErrors (idl: Idl) : string list =
        let rec readable (t: IdlType) : string option =
            match t with
            | TStr
            | TInt
            | TBool
            | TFloat -> None
            | TEnum n when idl.Enums |> List.exists (fun e -> e.Name = n) -> None
            | TRecord n when idl.Records |> List.exists (fun r -> r.Name = n) -> None
            | TUnion(n, args) when idl.Unions |> List.exists (fun u -> u.Name = n) -> args |> List.tryPick readable
            | TEnum n
            | TRecord n
            | TUnion(n, _) -> Some(sprintf "names '%s', which the vocabulary does not declare" n)
            | TList inner
            | TMap inner -> readable inner
            | other -> Some(sprintf "is %A, which no leg but the host codec can read" other)

        let rec hostedIn (t: IdlType) : HostedCodec list =
            match t with
            | THosted h -> [ h ]
            | TList inner
            | TMap inner -> hostedIn inner
            | TUnion(_, args) -> args |> List.collect hostedIn
            | _ -> []

        let fieldSets =
            [ for k in idl.Kinds -> "kind " + k.Tag, k.Fields
              for o in idl.Ops -> "op " + o.Tag, o.Fields
              for r in idl.Records -> "record " + r.Name, r.Fields
              for u in idl.Unions do
                  for c in u.Cases -> sprintf "union %s.%s" u.Name c.Tag, c.Fields
              yield "the node envelope", idl.NodeFields ]

        [ for owner, fields in fieldSets do
              for f in fields do
                  for h in hostedIn f.Type do
                      let at = sprintf "%s, field '%s' (hosted %s)" owner f.Name h.FSharp

                      match h.Wire |> Option.bind readable with
                      | Some why -> sprintf "%s: the declared wire form %s" at why
                      | None -> ()

                      match h.Format, h.Wire with
                      | Some fmt, w when w <> Some TStr ->
                          sprintf
                              "%s: format '%s' needs a string wire form (Wire = Some TStr)"
                              at
                              (HostedFormat.name fmt)
                      | _ -> () ]

    /// Well-formedness of the declared wire shape (Phases 108/109). Empty list ⇒
    /// well-formed. The discriminator shares an object with a tagged body's own
    /// fields in EVERY shape, and in [[NodeEnvelopeShape.FlatKind]] the node's
    /// `id`, its kind fields and its declared envelope all share one object — so
    /// the reserved names are checked here rather than colliding silently on the
    /// wire.
    let wireShapeErrors (idl: Idl) : string list =
        let disc = idl.Wire.Discriminator
        let flat = idl.Wire.NodeEnvelope = NodeEnvelopeShape.FlatKind

        let fieldClash (owner: string) (fields: IdlField list) =
            [ for f in fields do
                  if f.Name = disc then
                      sprintf "%s: field '%s' collides with the declared discriminator key" owner f.Name

                  if flat && f.Name = "id" then
                      sprintf "%s: field 'id' is reserved in the flat node envelope" owner ]

        [ if System.String.IsNullOrWhiteSpace disc then
              "wire shape: the discriminator key must be a non-empty string"

          // The emitters splice the key into generated JS/F# source literals, so
          // quote-class characters are refused at declaration rather than emitted.
          if
              disc
              |> Seq.exists (fun c -> c = '"' || c = '\'' || c = '\\' || System.Char.IsControl c)
          then
              "wire shape: the discriminator key must not contain quotes, backslashes or control characters"

          if disc = "id" then
              "wire shape: the discriminator key 'id' collides with the node id"

          yield!
              idl.Kinds
              |> List.collect (fun k -> fieldClash ("kind '" + k.Tag + "'") k.Fields)
          yield! idl.Ops |> List.collect (fun o -> fieldClash ("op '" + o.Tag + "'") o.Fields)

          yield!
              idl.Unions
              |> List.collect (fun u ->
                  u.Cases
                  |> List.collect (fun c ->
                      [ for f in c.Fields do
                            if f.Name = disc then
                                sprintf
                                    "union '%s' case '%s': field '%s' collides with the declared discriminator key"
                                    u.Name
                                    c.Tag
                                    f.Name ]))

          yield! fieldClash "node envelope" idl.NodeFields

          // Flat only: the envelope and every kind body share one object, so an
          // envelope name reappearing as a kind field is ambiguous on the wire.
          if flat then
              let envNames = idl.NodeFields |> List.map _.Name |> Set.ofList

              yield!
                  idl.Kinds
                  |> List.collect (fun k ->
                      [ for f in k.Fields do
                            if envNames.Contains f.Name then
                                sprintf
                                    "kind '%s': field '%s' collides with a node-envelope field in the flat shape"
                                    k.Tag
                                    f.Name ]) ]

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

    /// Whether a value of `t` can encode to a JSON OBJECT — the shape a transparent case
    /// must not have (Phase 292 amendment): the case goes out BARE, and a bare object is
    /// what every other case looks like, so the hosts disagree on which case it is.
    let rec private objectCapable (t: IdlType) : bool =
        match t with
        | TJson
        | TRecord _
        | TMap _
        | TUnion _
        | TNode
        | TKind
        | TOp -> true
        | THosted h ->
            match h.Wire with
            | None -> true
            | Some w -> objectCapable w
        | _ -> false

    /// **Every well-formedness rule a vocabulary is held to, in one call (Phase 292)** —
    /// the union of [[wireShapeErrors]], [[enumWireErrors]] and [[hostedWireErrors]] with
    /// the referential and lexical checks nothing used to make. Empty list ⇒ well-formed.
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
    ///   field, are values of the slot's type (checked by the encoder itself, so "fits"
    ///   means "encodes"); a slot whose type mentions a type variable is checked where it is
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
    let errors (idl: Idl) : string list =
        let enumNames = idl.Enums |> List.map _.Name |> Set.ofList
        let recordNames = idl.Records |> List.map _.Name |> Set.ofList

        let unionOf (n: string) = IdlLookup.tryUnion idl n

        let transparentField (u: IdlUnion) : IdlField option =
            match TransparentUnion.tag idl.Harden u with
            | Some tag ->
                match u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                | Some { Fields = [ single ] } -> Some single
                | _ -> None
            | None -> None

        let rec mentionsVar (t: IdlType) =
            match t with
            | TVar _ -> true
            | TList inner
            | TMap inner -> mentionsVar inner
            | TUnion(_, args) -> args |> List.exists mentionsVar
            | _ -> false

        let rec typeErrors (at: string) (pars: string list) (t: IdlType) : string list =
            match t with
            | TEnum n when not (enumNames.Contains n) ->
                [ sprintf "%s: names enum '%s', which the vocabulary does not declare" at n ]
            | TRecord n when not (recordNames.Contains n) ->
                [ sprintf "%s: names record '%s', which the vocabulary does not declare" at n ]
            | TUnion(n, args) ->
                let inner = args |> List.collect (typeErrors at pars)

                match unionOf n with
                | None ->
                    sprintf "%s: names union '%s', which the vocabulary does not declare" at n
                    :: inner
                | Some u ->
                    match TypeParams.bind u args with
                    | None ->
                        sprintf
                            "%s: applies union '%s' to %d type argument(s); it declares %d"
                            at
                            n
                            (List.length args)
                            (List.length u.Params)
                        :: inner
                    | Some subst ->
                        match transparentField u with
                        | Some f when mentionsVar f.Type && objectCapable (TypeParams.substitute subst f.Type) ->
                            sprintf
                                "%s: instantiates union '%s' so that its transparent case's field '%s' can encode to an object — a bare object is indistinguishable from a tagged case"
                                at
                                n
                                f.Name
                            :: inner
                        | _ -> inner
            | TVar v when not (List.contains v pars) ->
                [ sprintf "%s: type variable '%s' is not a parameter of the union declaring it" at v ]
            | TList inner
            | TMap inner -> typeErrors at pars inner
            | _ -> []

        let annotationErrors (at: string) (a: Annotations) : string list =
            [ let single (slot: string) (v: string option) =
                  [ match v with
                    | Some s when controlOrBreak s ->
                        sprintf "%s: the annotation's %s carries a line break or control character" at slot
                    | Some s when illFormed s -> sprintf "%s: the annotation's %s is ill-formed UTF-16" at slot
                    | _ -> () ]

              match a.Deprecated with
              | Some d ->
                  yield! single "deprecation replacement" d.Replacement
                  yield! single "deprecation message" d.Message
              | None -> ()

              yield! single "version (since)" a.Since

              match a.Doc with
              | Some d when illFormed d -> sprintf "%s: the annotation's doc is ill-formed UTF-16" at
              | _ -> () ]

        let nameErrors (what: string) (names: string list) : string list =
            [ for n in names do
                  if not (isIdentifier n) then
                      sprintf "%s '%s' is not an identifier (an ASCII letter or '_', then letters, digits, '_')" what n

              for n in names |> List.countBy id |> List.filter (fun (_, c) -> c > 1) |> List.map fst do
                  sprintf "%s '%s' is declared more than once" what n ]

        let fieldErrors (owner: string) (pars: string list) (fields: IdlField list) : string list =
            [ yield! nameErrors (owner + ": field") (fields |> List.map _.Name)

              for f in fields do
                  if inheritedMemberNames.Contains f.Name then
                      sprintf
                          "%s: field '%s' is reserved — every JavaScript object already carries a member of that name, so a generated TypeScript host cannot hold it as data"
                          owner
                          f.Name

              for f in fields do
                  let at = sprintf "%s, field '%s'" owner f.Name
                  yield! typeErrors at pars f.Type
                  yield! annotationErrors at f.Annotations

                  match f.Opt, f.Type with
                  | HostOnly, TFn _ -> ()
                  | HostOnly, other ->
                      sprintf
                          "%s: a HostOnly field must be a TFn (it carries the host type and the placeholder), not %A"
                          at
                          other
                  | OmitDefault d, t when not (mentionsVar t) ->
                      match Encode.valueJson idl t d with
                      | Ok j when (Json.firstIllFormedString j).IsSome ->
                          sprintf "%s: the omit-at-default value holds an ill-formed UTF-16 string" at
                      | Ok _ -> ()
                      | Error m -> sprintf "%s: the omit-at-default value does not fit the field's type (%s)" at m
                  | _ -> () ]

        let kindErrors (what: string) (k: IdlKind) : string list =
            let owner = sprintf "%s '%s'" what k.Tag

            [ if controlOrBreak k.Category || illFormed k.Category then
                  sprintf "%s: the category carries a line break, a control character or ill-formed UTF-16" owner

              yield! annotationErrors owner k.Annotations
              yield! fieldErrors owner [] k.Fields ]

        [ yield! wireShapeErrors idl
          yield! enumWireErrors idl
          yield! hostedWireErrors idl

          // Names, and the namespaces they share.
          yield! nameErrors "kind" (idl.Kinds |> List.map _.Tag)
          yield! nameErrors "op" (idl.Ops |> List.map _.Tag)

          yield!
              nameErrors
                  "type name"
                  ((idl.Enums |> List.map _.Name)
                   @ (idl.Unions |> List.map _.Name)
                   @ (idl.Records |> List.map _.Name))

          for k in idl.Kinds do
              yield! kindErrors "kind" k

          for o in idl.Ops do
              yield! kindErrors "op" o

          for r in idl.Records do
              yield! fieldErrors (sprintf "record '%s'" r.Name) [] r.Fields

          for u in idl.Unions do
              let owner = sprintf "union '%s'" u.Name
              yield! nameErrors (owner + ": type parameter") u.Params
              yield! nameErrors (owner + ": case") (u.Cases |> List.map _.Tag)

              for c in u.Cases do
                  let caseOwner = sprintf "union '%s' case '%s'" u.Name c.Tag
                  yield! annotationErrors caseOwner c.Annotations
                  yield! fieldErrors caseOwner u.Params c.Fields

          for e in idl.Enums do
              let owner = sprintf "enum '%s'" e.Name
              // A case NAME is the host identifier; its WIRE string is data, escaped where
              // it is spliced, and held only to well-formedness.
              yield!
                  e.Cases
                  |> List.filter (fun c -> not (isIdentifier c))
                  |> List.map (fun c -> sprintf "%s: case '%s' is not an identifier" owner c)

              for w in e.WireCases do
                  if illFormed w then
                      sprintf "%s: a wire string is ill-formed UTF-16" owner

              for c, a in e.CaseAnnotations do
                  yield! annotationErrors (sprintf "%s case '%s'" owner c) a

          yield! fieldErrors "the node envelope" [] idl.NodeFields

          // The envelope's reserved names (Phase 292 amendment): `id` beside the node's own
          // id in every shape — the flat shape's rule is [[wireShapeErrors]]' — and `kind`
          // beside the nested kind body.
          if idl.Wire.NodeEnvelope = NodeEnvelopeShape.NestedKind then
              for f in idl.NodeFields do
                  if f.Name = "id" || f.Name = "kind" then
                      sprintf "the node envelope: field '%s' is reserved beside the nested kind body" f.Name

          if illFormed idl.Wire.Discriminator then
              "wire shape: the discriminator key is ill-formed UTF-16"

          // Declared (authoring) defaults: a value of its slot's type, once per address. An
          // address naming no field is inert — no constructor reads it — and the artifact
          // carries it verbatim, so it is not refused here.
          for d in idl.Defaults do
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
                  match Encode.valueJson idl f.Type d.Value with
                  | Ok j when (Json.firstIllFormedString j).IsSome ->
                      sprintf "%s: the value holds an ill-formed UTF-16 string" at
                  | Ok _ -> ()
                  | Error m -> sprintf "%s: the value does not fit the field's type (%s)" at m

          for (k, f), c in idl.Defaults |> List.countBy (fun d -> d.Kind, d.Field) do
              if c > 1 then
                  sprintf "default for '%s.%s' is declared more than once" k f

          // Declared transparent cases — their WIRE consequence. Whether a harden token names
          // a member at all is the hardener's to check where it uses one (`Trust`), since a
          // token naming nothing changes no encoding.
          for name, tag in idl.Harden.TransparentUnions do
              let at = sprintf "harden policy: transparent case '%s.%s'" name tag

              match
                  unionOf name
                  |> Option.bind (fun u -> u.Cases |> List.tryFind (fun c -> c.Tag = tag))
              with
              | None -> ()
              | Some c ->
                  match c with
                  | { Fields = [ single ] } ->
                      if not (mentionsVar single.Type) && objectCapable single.Type then
                          sprintf
                              "%s: its field '%s' can encode to an object — a bare object is indistinguishable from a tagged case, and the hosts disagree on it"
                              at
                              single.Name
                  | _ -> sprintf "%s must carry exactly one field — it is on the wire BARE" at

          for name, c in idl.Harden.TransparentUnions |> List.countBy fst do
              if c > 1 then
                  sprintf "harden policy: union '%s' declares more than one transparent case" name ]
