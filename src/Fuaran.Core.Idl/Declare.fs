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

    /// What could bind a type variable where a field's type names one (Phase 732): the refusal of an
    /// unbound variable names what declares, or cannot declare, the parameter.
    [<RequireQualifiedAccess>]
    type private Binder =
        /// A union or a record: which of the two, and the type parameters it declares.
        | Declares of what: string * pars: string list
        /// An owner that declares no type parameter, as the refusal names it: a kind, an op, the
        /// node envelope, a contract's method.
        | DeclaresNone of owner: string

    /// One member an instantiation rule judges at the instantiations of its declaration, and how
    /// its refusal reads (Phase 732).
    type private Asked =
        {
            /// The path the fault is reported at.
            At: PathSegment list
            /// The member, as a sentence names it (`record 'Opt', field 'v'`).
            Name: string
            /// The fault's sentence, given the instantiation as spelled and why it fails.
            Refusal: string -> string -> string
            /// What the walk judges ([[Instantiations.judgeAtParameters]]).
            Judged: Instantiations.Judged
        }

    /// For each of `keys`, in order, whether it is the SECOND of its key: a key given more than
    /// once is refused once, where it is given again. Counted in one pass, so a vocabulary of `n`
    /// declarations costs O(n), never the O(n²) of reading every earlier one at each.
    let private givenAgain (keys: 'K list) : bool list when 'K: equality =
        // Keyed by the key wrapped, so a null one is counted as any other.
        let counts =
            System.Collections.Generic.Dictionary<'K option, int>(HashIdentity.Structural)

        keys
        |> List.map (fun k ->
            let before =
                match counts.TryGetValue(Some k) with
                | true, c -> c
                | false, _ -> 0

            counts[Some k] <- before + 1
            before = 1)

    /// An enum whose wire strings ARE its case names — the common case, and the
    /// shape every declaration had before fuaran#707.
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
            | WireForm.Text _ -> None
            | WireForm.Record r -> r.Args |> List.tryPick readable
            | WireForm.Union u -> u.Args |> List.tryPick readable
            | WireForm.Tuple components -> components |> List.tryPick readable
            | WireForm.Set inner -> readable inner
            | WireForm.KeyedMap(k, v) -> [ k; v ] |> List.tryPick readable
            | WireForm.Undeclared(Undeclared.Enum n | Undeclared.Record(n, _) | Undeclared.Union(n, _)) ->
                Some(sprintf "names '%s', which the vocabulary does not declare" n)
            | WireForm.List inner
            | WireForm.Map inner -> readable inner
            | _ -> Some(sprintf "is %A, which no leg but the host codec can read" t)

        let rec hostedIn (t: IdlType) : HostedCodec list =
            match t with
            | THosted h -> [ h ]
            | TList inner
            | TMap inner
            | TSet inner -> hostedIn inner
            | TUnion(_, args)
            | TRecord(_, args)
            | TTuple args -> args |> List.collect hostedIn
            | TKeyedMap(k, v) -> hostedIn k @ hostedIn v
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

    /// **Phase 474 (DECISIONS.md D220) — where an INLINE declaration may stand, and what it may declare.**
    /// An inline record ([[IdlRecord.Inline]]) and a presence-keyed union ([[UnionDiscrimination.ByPresence]])
    /// have no wire object of their own: their members are keys of the object holding them. So each is
    /// only ever a field's OWN type, on a field of a kind, an op, a record that is not inline or a case
    /// of a tagged union (never a list item, a map value, a type argument, a hosted wire form, the node
    /// envelope or another inline declaration); an inline record is `Required` (a host grouping is
    /// always there) and a presence-keyed union `Required` or `Optional` (no case's key present).
    /// Members an inline declaration contributes take no alias and no presence condition, and no
    /// member of the holding object may be spelled twice: not as a sibling member or the
    /// discriminator, and, for a kind in the flat node envelope, not as the node id or an envelope
    /// field, which are keys of the same object. The fault names both. No alias is spelt as a key the
    /// node envelope lays on its object (Phase 723, DECISIONS.md D292): the node id, the nested
    /// envelope's `kind`, and in the flat envelope the discriminator and the members of the envelope
    /// and of a kind beside each other. A presence-keyed union names
    /// one rule per case, in case order, whose key is a `Required` wire member of that case and no
    /// other case's key; a synthesised member is another case's member at a value of its type, and a
    /// case whose key is synthesised synthesises nothing (the reading would chain). Such a union takes
    /// no type parameter, no tag rule, no transparent case and no case-level host codec — it has no tag.
    /// Empty list ⇒ every inline declaration is well-placed and well-formed.
    let layoutErrors (idl: Idl) : VocabularyFault list =
        let keyedOf (t: IdlType) : IdlUnion option =
            match WireForm.ofType idl t with
            | WireForm.Union u when u.Decl.Discrimination <> UnionDiscrimination.Tagged -> Some u.Decl
            | _ -> None

        let describe (t: IdlType) : string option =
            match WireForm.inlineRecord idl t, keyedOf t with
            | Some r, _ -> Some(sprintf "inline record '%s'" r.Name)
            | _, Some u -> Some(sprintf "presence-keyed union '%s'" u.Name)
            | _ -> None

        // Every type position NESTED in `t` — never `t` itself.
        let rec nested (t: IdlType) : IdlType list =
            match t with
            | TList i
            | TMap i
            | TSet i -> i :: nested i
            | TUnion(_, args)
            | TRecord(_, args)
            | TTuple args -> args @ (args |> List.collect nested)
            | TKeyedMap(k, v) -> [ k; v ] @ nested k @ nested v
            | THosted { Wire = Some w } -> w :: nested w
            | _ -> []

        // The wire members an owner's fields put on its object, inline declarations expanded: each
        // key, the field that puts it there, and whether an inline declaration LAYS it there.
        let members (fields: IdlField list) : (string * string * bool) list =
            [ for f in fields do
                  if f.Opt <> HostOnly then
                      match WireForm.inlineRecord idl f.Type, keyedOf f.Type with
                      | Some r, _ ->
                          let bearer = sprintf "field '%s' (inline record '%s')" f.Name r.Name

                          for g in WireForm.instantiatedFields r do
                              if g.Opt <> HostOnly then
                                  for n in WireForm.spellings g do
                                      n, bearer, true
                      | _, Some u ->
                          let bearer = sprintf "field '%s' (presence-keyed union '%s')" f.Name u.Name

                          for n in WireForm.keyedRoster u do
                              n, bearer, true
                      | _ ->
                          for n in WireForm.spellings f do
                              n, sprintf "field '%s'" f.Name, false ]

        // `admits`: whether this owner may hold an inline declaration; `within`: whether the owner IS
        // an inline declaration's member list (its members take no alias and no condition).
        // `reserved`: the keys the owner's object carries before any field, each with what carries
        // it. `shared`: the keys of an object the owner's members are MERGED into (the node id, the
        // kind body, the discriminator, and the members of the node envelope and of a kind beside
        // it), which neither a member an inline declaration lays nor an alias may respell; a field
        // of the owner's own NAMED as one is [[wireShapeErrors]]' fault, as it always was.
        let ownerErrors
            (owner: string)
            (at: PathSegment list)
            (fieldAt: int -> PathSegment list)
            (admits: bool)
            (within: bool)
            (reserved: (string * string) list)
            (shared: (string * string) list)
            (fields: IdlField list)
            : VocabularyFault list =
            [ for j, f in List.indexed fields do
                  let path = fieldAt j
                  let label = sprintf "%s, field '%s'" owner f.Name

                  for n in nested f.Type do
                      match describe n with
                      | Some what ->
                          fault
                              (path @ [ key "type" ])
                              (sprintf
                                  "%s: %s is held inside another type — its members are keys of the object holding it, so it can only be a field's own type"
                                  label
                                  what)
                      | None -> ()

                  match describe f.Type with
                  | Some what ->
                      if not admits then
                          fault
                              (path @ [ key "type" ])
                              (sprintf
                                  "%s: %s cannot stand here — only on a field of a kind, an op, a record that is not inline or a case of a tagged union"
                                  label
                                  what)

                      match WireForm.inlineRecord idl f.Type, f.Opt with
                      | Some _, Required
                      | None, (Required | Optional) -> ()
                      | Some _, _ ->
                          fault
                              (path @ [ key "optionality" ])
                              (sprintf "%s: an %s is a host grouping that is always there, so it is Required" label what)
                      | None, _ ->
                          fault
                              (path @ [ key "optionality" ])
                              (sprintf
                                  "%s: a %s is Required or Optional (absent when no case's key is present)"
                                  label
                                  what)

                      if not (List.isEmpty f.Aliases) || f.Presence.IsSome then
                          fault
                              path
                              (sprintf
                                  "%s: a field holding an %s has no member of its own, so it takes no alias and no presence condition"
                                  label
                                  what)
                  | None -> ()

                  if within && (not (List.isEmpty f.Aliases) || f.Presence.IsSome) then
                      fault
                          path
                          (sprintf
                              "%s: a member of an inline declaration takes no alias and no presence condition"
                              label)

                  match f.Presence with
                  | Some(WhenPresent s | WhenAbsent s | WhenEquals(s, _)) ->
                      match fields |> List.tryFind (fun g -> g.Name = s) with
                      | Some g when (describe g.Type).IsSome ->
                          fault
                              (path @ [ key "presence" ])
                              (sprintf "%s: the presence condition names '%s', which holds no member of its own" label s)
                      | _ -> ()
                  | Some WhenNonEmpty
                  | None -> ()

              let own = members fields
              let carried = (reserved |> List.map (fun (n, bearer) -> n, bearer, false)) @ own

              let twice (n: string) (first: string) (second: string) =
                  fault
                      at
                      (sprintf
                          "%s: the member '%s' is on its object twice once inline declarations are laid out: %s and %s"
                          owner
                          n
                          first
                          second)

              for k, (n, bearer, _) in List.indexed carried do
                  match carried |> List.take k |> List.tryFind (fun (m, _, _) -> m = n) with
                  | Some(_, first, _) -> twice n first bearer
                  | None -> ()

              for n, bearer, laid in List.distinct own do
                  if laid then
                      match shared |> List.tryFind (fun (m, _) -> m = n) with
                      | Some(_, first) -> twice n first bearer
                      | None -> ()

              // Phase 723 — an alias is a key the reader reads its member under ([[WireForm.memberAt]]),
              // so one spelt as a key the object carries from elsewhere is read in the member's place:
              // a document holding that key reads as the member, and one leaving the member out reads
              // the other key's value as it. No document of such a vocabulary reads canonically.
              for j, f in List.indexed fields do
                  if f.Opt <> HostOnly then
                      for a, alias in List.indexed f.Aliases do
                          match shared |> List.tryFind (fun (m, _) -> m = alias) with
                          | Some(_, first) ->
                              fault
                                  (fieldAt j @ [ key "aliases"; item a ])
                                  (sprintf
                                      "%s, field '%s': the alias '%s' is a key its object already carries (%s), so a document holding that key would read it as this member"
                                      owner
                                      f.Name
                                      alias
                                      first)
                          | None -> () ]

        let disc = [ idl.Wire.Discriminator, "the discriminator" ]

        // In the flat node envelope the node id, the envelope's fields and a kind's members are keys
        // of ONE object, so a member an inline declaration lays there shares it with them.
        let envelope =
            match idl.Wire.NodeEnvelope with
            | NodeEnvelopeShape.NestedKind -> []
            | NodeEnvelopeShape.FlatKind ->
                ("id", "the node id")
                :: [ for g in idl.NodeFields do
                         if g.Opt <> HostOnly then
                             for n in WireForm.spellings g do
                                 n, sprintf "the node-envelope field '%s'" g.Name ]

        [ for i, k in List.indexed idl.Kinds do
              yield!
                  ownerErrors
                      (sprintf "kind '%s'" k.Tag)
                      [ key "kinds"; item i ]
                      (kindField i)
                      true
                      false
                      disc
                      envelope
                      k.Fields

          for i, o in List.indexed idl.Ops do
              yield! ownerErrors (sprintf "op '%s'" o.Tag) [ key "ops"; item i ] (opField i) true false disc [] o.Fields

          for i, r in List.indexed idl.Records do
              yield!
                  ownerErrors
                      (sprintf "record '%s'" r.Name)
                      [ key "records"; item i ]
                      (recordField i)
                      (not r.Inline)
                      r.Inline
                      []
                      []
                      r.Fields

          // The node envelope's members share the node object with the node id and, under the
          // nested envelope, the kind body; under the flat one with the discriminator and every
          // kind's members, each as the reader reads it.
          let beside =
              match idl.Wire.NodeEnvelope with
              | NodeEnvelopeShape.NestedKind -> [ "id", "the node id"; "kind", "the kind body" ]
              | NodeEnvelopeShape.FlatKind ->
                  ("id", "the node id") :: disc
                  @ [ for k in idl.Kinds do
                          for n, bearer, _ in members k.Fields do
                              n, sprintf "%s of kind '%s'" bearer k.Tag ]

          yield! ownerErrors "the node envelope" [ key "nodeFields" ] envelopeField false false [] beside idl.NodeFields

          for i, u in List.indexed idl.Unions do
              let at = [ key "unions"; item i ]
              let owner = sprintf "union '%s'" u.Name
              let transparent = TransparentUnion.tag idl.Harden u

              match u.Discrimination with
              | UnionDiscrimination.Tagged ->
                  for c, case in List.indexed u.Cases do
                      let isBare = transparent = Some case.Tag

                      yield!
                          ownerErrors
                              (sprintf "union '%s' case '%s'" u.Name case.Tag)
                              (at @ [ key "cases"; item c ])
                              (caseField i c)
                              (not isBare)
                              false
                              (if isBare then [] else disc)
                              []
                              case.Fields
              | UnionDiscrimination.ByPresence keyed ->
                  let path = at @ [ key "byPresence" ]

                  for c, case in List.indexed u.Cases do
                      yield!
                          ownerErrors
                              (sprintf "union '%s' case '%s'" u.Name case.Tag)
                              (at @ [ key "cases"; item c ])
                              (caseField i c)
                              false
                              true
                              []
                              []
                              case.Fields

                  if not (List.isEmpty u.Params) then
                      fault (at @ [ key "params" ]) (sprintf "%s: a presence-keyed union takes no type parameter" owner)

                  if u.TagRule <> UnionTagRule.CaseName then
                      fault
                          (at @ [ key "tagRule" ])
                          (sprintf "%s: a presence-keyed union has no tag, so no tag rule" owner)

                  if transparent.IsSome then
                      fault path (sprintf "%s: a presence-keyed union has no transparent case" owner)

                  for c, case in List.indexed u.Cases do
                      if case.Hosted.IsSome then
                          fault
                              (at @ [ key "cases"; item c; key "hosted" ])
                              (sprintf
                                  "%s: case '%s' of a presence-keyed union takes no case-level host codec"
                                  owner
                                  case.Tag)

                  if (keyed |> List.map _.Case) <> (u.Cases |> List.map _.Tag) then
                      fault
                          path
                          (sprintf "%s: a presence-keyed union names one rule per case, in the order of its cases" owner)

                  let synthesisedKeys = keyed |> List.collect (fun o -> o.Synthesised |> List.map fst)

                  for r, kc in List.indexed keyed do
                      let rp = path @ [ item r ]
                      let label = sprintf "%s, case '%s'" owner kc.Case

                      match u.Cases |> List.tryFind (fun c -> c.Tag = kc.Case) with
                      | None -> ()
                      | Some case ->
                          match case.Fields |> List.tryFind (fun f -> f.Name = kc.Key) with
                          | Some { Opt = Required } -> ()
                          | _ ->
                              fault
                                  (rp @ [ key "key" ])
                                  (sprintf "%s: the key '%s' is not a Required member of the case" label kc.Key)

                          if keyed |> List.take r |> List.exists (fun o -> o.Key = kc.Key) then
                              fault
                                  (rp @ [ key "key" ])
                                  (sprintf "%s: the key '%s' selects another case too" label kc.Key)

                          // Phase 722 (D285.6) — a member of this case spelled as ANOTHER case's key: a
                          // value carrying it writes both keys, and the reading (D220.3) refuses two
                          // cases left, so the declaration would have a value it does not read back.
                          for m, f in List.indexed case.Fields do
                              match
                                  keyed
                                  |> List.tryFind (fun o -> o.Case <> kc.Case && o.Key = f.Name && o.Key <> kc.Key)
                              with
                              | Some other when f.Opt <> HostOnly ->
                                  fault
                                      (at
                                       @ [ key "cases"; item (u.Cases |> List.findIndex (fun c -> c.Tag = case.Tag)) ]
                                       @ [ key "fields"; item m ])
                                      (sprintf
                                          "%s: the member '%s' is the key of case '%s', so a value carrying it writes both keys and is read as neither case"
                                          label
                                          f.Name
                                          other.Case)
                              | _ -> ()

                          if not (List.isEmpty kc.Synthesised) && List.contains kc.Key synthesisedKeys then
                              fault
                                  (rp @ [ key "synthesised" ])
                                  (sprintf
                                      "%s: its key is synthesised by another case, so it synthesises nothing (the reading would chain)"
                                      label)

                          for s, (name, v) in List.indexed kc.Synthesised do
                              let sp = rp @ [ key "synthesised"; key name ]

                              if case.Fields |> List.exists (fun f -> f.Name = name) then
                                  fault
                                      sp
                                      (sprintf "%s: the synthesised member '%s' is one of its own members" label name)
                              elif kc.Synthesised |> List.take s |> List.exists (fun (n, _) -> n = name) then
                                  fault sp (sprintf "%s: the synthesised member '%s' is declared twice" label name)
                              else
                                  match
                                      u.Cases
                                      |> List.tryPick (fun o ->
                                          if o.Tag = case.Tag then
                                              None
                                          else
                                              o.Fields |> List.tryFind (fun f -> f.Name = name))
                                  with
                                  | None ->
                                      fault
                                          sp
                                          (sprintf
                                              "%s: the synthesised member '%s' is no other case's member"
                                              label
                                              name)
                                  | Some f when f.Opt = HostOnly ->
                                      fault
                                          sp
                                          (sprintf "%s: the synthesised member '%s' is never on the wire" label name)
                                  | Some f ->
                                      match Encode.renderValue idl f.Type v with
                                      | Ok _ -> ()
                                      | Error m ->
                                          fault
                                              sp
                                              (sprintf
                                                  "%s: the synthesised value does not fit member '%s' (%s)"
                                                  label
                                                  name
                                                  m) ]

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
    /// - **references** — every `TEnum` / `TRecord` / `TUnion` name resolves, every union and
    ///   every record is applied to as many type arguments as it declares parameters, and a `TVar`
    ///   names a parameter of the union whose case declares it or of the record that declares it
    ///   (Phase 732), so none reaches the wire unsubstituted;
    /// - **cases** (Phase 713) — an enum or a union declares at least one case: one with none has
    ///   no value, so a slot of its type could never be written or read, and it is refused here,
    ///   by its name, rather than where something first samples it;
    /// - **duplicates** — kind tags, op tags, type names (enums, unions and records share
    ///   one namespace in every generated host), case tags, a union's or a record's type
    ///   parameters, field names
    ///   within one owner, and default entries;
    /// - **defaults** — an `OmitDefault` value, and an [[IdlDefault]] addressing a declared
    ///   field, are values the GUARDED encoder writes at the slot's type (Phase 520: checked
    ///   through the encoder's own guarded renderer, so "fits" means "encodes to wire its decoder
    ///   reads" — a map default carrying one key twice is refused HERE, at its path, not at the
    ///   first `encode`); an `OmitDefault` whose slot's type mentions a type variable is checked
    ///   at every instantiation of its declaration the vocabulary reaches, and refused at the
    ///   declaration naming the instantiation (Phase 732);
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
    /// A field-less kind or record is well-formed: a marker carries its meaning in its tag. A
    /// case-less enum or union is not: it has no tag to carry.
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

        // Phase 723 — a declared value is canonical only where no object in it holds a member whose
        // presence condition fails over that object (D190.2): the encoder drops such a member, so the
        // value its bytes read back to is another. A member at its absence (left out, or an
        // `OmitDefault` member at its own default in its value space) is dropped by nothing. Answers
        // the first such member, what holds it and its condition, outermost first.
        let rec dropped (t: IdlType) (v: IdlValue) : (IdlField * string * PresenceCondition) option =
            let within (holder: string) (fields: IdlField list) (authored: (string * IdlValue) list) =
                let held (f: IdlField) =
                    authored
                    |> List.tryPick (fun (n, x) -> if n = f.Name && x <> VAbsent then Some x else None)

                let atAbsence (f: IdlField) (x: IdlValue) =
                    match f.Opt with
                    | OmitDefault d ->
                        x = d
                        || (match Encode.renderValue idl f.Type x, Encode.renderValue idl f.Type d with
                            | Ok a, Ok b -> a = b
                            | _ -> false)
                    | _ -> false

                fields
                |> List.tryPick (fun f ->
                    match held f, f.Presence with
                    | Some x, Some c when
                        f.Opt <> HostOnly
                        && not (WireForm.holdsAuthored fields f.Name c authored)
                        && not (atAbsence f x)
                        ->
                        Some(f, holder, c)
                    | _ -> None)
                |> Option.orElseWith (fun () ->
                    fields |> List.tryPick (fun f -> held f |> Option.bind (dropped f.Type)))

            let kindNamed (tag: string) (authored: (string * IdlValue) list) =
                IdlLookup.tryKind idl tag
                |> Option.bind (fun k -> within (sprintf "kind '%s'" k.Tag) k.Fields authored)

            match WireForm.ofType idl t, v with
            | WireForm.Record r, VRecord authored ->
                r.Binding
                |> Option.bind (fun b ->
                    within (sprintf "record '%s'" r.Name) (WireForm.recordFields b r.Decl) authored)
            | WireForm.Union u, VUnion(tag, authored) ->
                match u.Binding, u.Decl.CaseByWireTag tag with
                | Some b, Some c ->
                    within (sprintf "union '%s' case '%s'" u.Name c.Tag) (WireForm.caseFields b c) authored
                | _ -> None
            | WireForm.Kind, VUnion(tag, authored)
            | WireForm.Node, VNode(_, tag, authored) -> kindNamed tag authored
            | WireForm.Node, VNodeEnv(_, envelope, tag, authored) ->
                within "the node envelope" idl.NodeFields envelope
                |> Option.orElseWith (fun () -> kindNamed tag authored)
            | WireForm.Op, VUnion(tag, authored) ->
                idl.Ops
                |> List.tryFind (fun o -> o.Tag = tag)
                |> Option.bind (fun o -> within (sprintf "op '%s'" o.Tag) o.Fields authored)
            | (WireForm.List item | WireForm.Set item), VList xs -> xs |> List.tryPick (dropped item)
            | WireForm.Map value, VMap entries -> entries |> List.tryPick (snd >> dropped value)
            | WireForm.Tuple components, VList xs when List.length xs = List.length components ->
                List.zip components xs |> List.tryPick (fun (c, x) -> dropped c x)
            | WireForm.KeyedMap(k, value), VList pairs ->
                pairs
                |> List.tryPick (function
                    | VList [ a; b ] -> dropped k a |> Option.orElseWith (fun () -> dropped value b)
                    | _ -> None)
            | _ -> None

        // What [[dropped]] answers, in the sentence's words.
        let droppedText (f: IdlField, holder: string, c: PresenceCondition) =
            let scalar (v: IdlValue) =
                match v with
                | VStr s
                | VEnum s -> sprintf "\"%s\"" s
                | VInt i -> string i
                | VBool b -> if b then "true" else "false"
                | other -> sprintf "%A" other

            let meaningful =
                match c with
                | WhenPresent s -> sprintf "'%s' is present" s
                | WhenAbsent s -> sprintf "'%s' is absent" s
                | WhenEquals(s, v) -> sprintf "'%s' holds %s" s (scalar v)
                | WhenNonEmpty -> "it holds a non-empty list"

            sprintf
                "it holds member '%s' of %s, which is meaningful only while %s, and there it is not, so the encoder drops it"
                f.Name
                holder
                meaningful

        // The refusal of a declared value [[dropped]] answers a member of.
        let notCanonical (at: string) (what: string) found =
            sprintf "%s: %s is not canonical: %s" at what (droppedText found)

        // At an instantiation, the same answer as the verdict's reason, told from an encoder's refusal.
        let notCanonicalWhy = "not canonical: "

        // Whether `t` names a type parameter. Each type object is asked once per check, so a type
        // shared in memory (an instantiation's argument, placed at each position that names its
        // parameter) costs a constant per node however large it prints.
        let mentioned =
            System.Collections.Generic.Dictionary<IdlType, bool>(HashIdentity.Reference)

        let rec mentionsVar (t: IdlType) =
            match mentioned.TryGetValue t with
            | true, answer -> answer
            | false, _ ->
                let answer =
                    match t with
                    | TVar _ -> true
                    | TList inner
                    | TMap inner
                    | TSet inner -> mentionsVar inner
                    | TUnion(_, args)
                    | TRecord(_, args)
                    | TTuple args -> args |> List.exists mentionsVar
                    | TKeyedMap(k, v) -> mentionsVar k || mentionsVar v
                    | _ -> false

                mentioned[t] <- answer
                answer

        // The transparent case's field of a union reference that instantiates it to an OBJECT: the
        // field names a type parameter, and at the reference's arguments its type can encode to
        // one. A field that names no parameter is judged where the case is declared.
        let objectAtReference (u: UnionForm) : IdlField option =
            match u.Binding, transparentField u with
            | Some binding, Some f when
                mentionsVar f.Type
                && WireForm.objectCapable idl (TypeParams.substitute binding f.Type)
                ->
                Some f
            | _ -> None

        // Phase 639 — whether a value of `t` can hold a node, a bare kind or a tree op at any depth,
        // through the records and unions it names (`seen` breaks declaration cycles).
        let rec holdsTree (seen: Set<string>) (t: IdlType) : bool =
            match t with
            | TNode
            | TKind
            | TOp -> true
            | TList i
            | TMap i
            | TSet i -> holdsTree seen i
            | TTuple args -> args |> List.exists (holdsTree seen)
            | TKeyedMap(k, v) -> holdsTree seen k || holdsTree seen v
            | THosted { Wire = Some w } -> holdsTree seen w
            | TRecord(n, args) when not (Set.contains ("r:" + n) seen) ->
                args |> List.exists (holdsTree seen)
                || (match IdlLookup.tryRecord idl n with
                    | Some r -> r.Fields |> List.exists (fun f -> holdsTree (Set.add ("r:" + n) seen) f.Type)
                    | None -> false)
            | TUnion(n, args) when not (Set.contains ("u:" + n) seen) ->
                args |> List.exists (holdsTree seen)
                || (match IdlLookup.tryUnion idl n with
                    | Some u ->
                        u.Cases
                        |> List.exists (fun c ->
                            c.Fields |> List.exists (fun f -> holdsTree (Set.add ("u:" + n) seen) f.Type))
                    | None -> false)
            | _ -> false

        // A reference that applies a union or a record to another number of type arguments than it
        // declares parameters (Phase 732: one sentence for both).
        let arityFault
            (path: PathSegment list)
            (at: string)
            (what: string)
            (name: string)
            (args: IdlType list)
            (declared: string list)
            : VocabularyFault =
            fault
                path
                (sprintf
                    "%s: applies %s '%s' to %d type argument(s); it declares %d"
                    at
                    what
                    name
                    (List.length args)
                    (List.length declared))

        // Why a transparent case may not encode to an object, in the words both refusals end in.
        let bareObject = "a bare object is indistinguishable from a tagged case"

        // `pars`: what binds a type variable at the field ([[Binder]]).
        let rec typeErrors (path: PathSegment list) (at: string) (pars: Binder) (t: IdlType) : VocabularyFault list =
            match WireForm.ofType idl t with
            | WireForm.Undeclared(Undeclared.Enum n) ->
                [ fault path (sprintf "%s: names enum '%s', which the vocabulary does not declare" at n) ]
            | WireForm.Undeclared(Undeclared.Record(n, args)) ->
                fault path (sprintf "%s: names record '%s', which the vocabulary does not declare" at n)
                :: (args |> List.collect (typeErrors path at pars))
            | WireForm.Undeclared(Undeclared.Union(n, args)) ->
                fault path (sprintf "%s: names union '%s', which the vocabulary does not declare" at n)
                :: (args |> List.collect (typeErrors path at pars))
            | WireForm.Record r ->
                let inner = r.Args |> List.collect (typeErrors path at pars)

                match r.Binding with
                | None -> arityFault path at "record" r.Name r.Args r.Decl.Params :: inner
                | Some _ -> inner
            | WireForm.Union u ->
                let inner = u.Args |> List.collect (typeErrors path at pars)

                match u.Binding with
                | None -> arityFault path at "union" u.Name u.Args u.Decl.Params :: inner
                | Some _ ->
                    match objectAtReference u with
                    | Some f ->
                        fault
                            path
                            (sprintf
                                "%s: instantiates union '%s' so that its transparent case's field '%s' can encode to an object — %s"
                                at
                                u.Name
                                f.Name
                                bareObject)
                        :: inner
                    | None -> inner
            | WireForm.Unbound v ->
                match pars with
                | Binder.Declares(_, declared) when List.contains v declared -> []
                | Binder.Declares(what, _) ->
                    [ fault path (sprintf "%s: type variable '%s' is not a parameter of the %s declaring it" at v what) ]
                // Until Phase 732 this owner was refused in the union's sentence, which named a
                // union where none declares the field.
                | Binder.DeclaresNone owner ->
                    [ fault
                          path
                          (sprintf "%s: type variable '%s' names a type parameter, and %s declares none" at v owner) ]
            | WireForm.List inner
            | WireForm.Map inner -> typeErrors path at pars inner
            // Phase 639 (D251) — a tuple has at least two components, and a set's element and a keyed
            // map's key are ORDERABLE (a type variable is not: its order would depend on the
            // instantiation). A map keyed by strings is `map`, so a keyed map's key is never a string.
            | WireForm.Tuple components when components |> List.exists (holdsTree Set.empty) ->
                [ fault
                      path
                      (sprintf
                          "%s: a tuple holds data, never a node, a bare kind or a tree op at any depth; a tree is held by a list, a record or a union, which every traversal walks"
                          at) ]
            | WireForm.Tuple components ->
                [ if List.length components < 2 then
                      fault
                          path
                          (sprintf
                              "%s: a tuple declares at least two components; this one declares %d"
                              at
                              components.Length) ]
                @ (components |> List.collect (typeErrors path at pars))
            | WireForm.Set element ->
                [ if not (WireForm.orderable idl element) then
                      fault
                          path
                          (sprintf
                              "%s: a set's element type must be orderable (a string, integer, boolean, enum, decimal, bytes, time of day, duration or a tuple of those); %A is not"
                              at
                              element) ]
                @ typeErrors path at pars element
            | WireForm.KeyedMap(k, v) ->
                [ if
                      (match WireForm.ofType idl k with
                       | WireForm.Str -> true
                       | _ -> false)
                  then
                      fault path (sprintf "%s: a map keyed by strings is declared `map` (TMap), never a keyed map" at)
                  elif not (WireForm.orderable idl k) then
                      fault
                          path
                          (sprintf
                              "%s: a keyed map's key type must be orderable (an integer, boolean, enum, decimal, bytes, time of day, duration or a tuple of orderable types); %A is not"
                              at
                              k) ]
                @ typeErrors path at pars k
                @ typeErrors path at pars v
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
                  if not (Contract.isIdentifier n) then
                      fault
                          path
                          (sprintf
                              "%s '%s' is not an identifier (an ASCII letter or '_', then letters, digits, '_')"
                              what
                              n)

              for (path, n), again in List.zip names (givenAgain (List.map snd names)) do
                  if again then
                      fault path (sprintf "%s '%s' is declared more than once" what n) ]

        let fieldErrors
            (owner: string)
            (fieldAt: int -> PathSegment list)
            (pars: Binder)
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

                      if not (Contract.isIdentifier n) then
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
                  // Phase 660 (DECISIONS.md D256) — the condition that reads the member itself: admitted
                  // only on an Optional list, the one member whose empty value is a spelling of its absence.
                  | Some WhenNonEmpty ->
                      match f.Opt, f.Type with
                      | Optional, TList _ -> ()
                      | _ ->
                          fault
                              (fieldAt j @ [ key "presence" ])
                              (sprintf
                                  "%s, field '%s': a non-empty condition needs an Optional list member — it reads the member itself, and only an optional list has an empty value that means its absence"
                                  owner
                                  f.Name)
                  | Some c ->
                      let path = fieldAt j @ [ key "presence" ]
                      let at = sprintf "%s, field '%s'" owner f.Name

                      let siblingName =
                          match c with
                          | WhenPresent s
                          | WhenAbsent s
                          | WhenEquals(s, _) -> s
                          | WhenNonEmpty -> f.Name

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
                      | Ok _ ->
                          match dropped t d with
                          | Some found ->
                              fault
                                  (path @ [ key "optionality"; key "default" ])
                                  (notCanonical at "the omit-at-default value" found)
                          | None -> ()
                      | Error m ->
                          fault
                              (path @ [ key "optionality"; key "default" ])
                              (sprintf "%s: the omit-at-default value does not fit the field's type (%s)" at m)
                  // A default at a type naming a parameter that the encoder writes without binding it:
                  // what it holds of its own is judged here, and the rest at each instantiation
                  // ([[instantiatedDefaultErrors]], asked only where the encoder needs the binding).
                  | OmitDefault d, t when Result.isOk (Encode.renderValue idl t d) ->
                      match dropped t d with
                      | Some found ->
                          fault
                              (path @ [ key "optionality"; key "default" ])
                              (notCanonical at "the omit-at-default value" found)
                      | None -> ()
                  | _ -> () ]

        /// Phase 494 — a declared keyed position ([[IdlField.Keyed]]) is admitted only where the derived
        /// rule would otherwise make the slot a STRUCTURAL child: on a node kind's field (`admitted`)
        /// holding a node or a node list the kind always carries. Anywhere else it declares nothing —
        /// an optional node, and a node in a record, a union case, an op or the envelope, is keyed
        /// already, and a slot holding no node has no position — and a marking on nothing is a typo.
        let keyedErrors
            (admitted: bool)
            (owner: string)
            (fieldAt: int -> PathSegment list)
            (fields: IdlField list)
            : VocabularyFault list =
            [ for j, f in List.indexed fields do
                  if f.Keyed then
                      let path = fieldAt j @ [ key "keyed" ]
                      let at = sprintf "%s, field '%s'" owner f.Name

                      if not admitted then
                          fault
                              path
                              (sprintf
                                  "%s: a keyed position is declared only on a node kind's field — every node held here is keyed already"
                                  at)
                      else
                          match f.Opt, f.Type with
                          | (Required | OmitDefault _), (TNode | TList TNode) -> ()
                          | Optional, (TNode | TList TNode) ->
                              fault
                                  path
                                  (sprintf
                                      "%s: an optional node is a keyed position already — the marking declares nothing"
                                      at)
                          | _ ->
                              fault
                                  path
                                  (sprintf
                                      "%s: a keyed position is declared only on a node or a node list the kind always carries"
                                      at) ]

        let kindErrors (what: string) (owner: PathSegment list) (k: IdlKind) : VocabularyFault list =
            let label = sprintf "%s '%s'" what k.Tag

            [ if controlOrBreak k.Category || illFormed k.Category then
                  fault
                      (owner @ [ key "category" ])
                      (sprintf "%s: the category carries a line break, a control character or ill-formed UTF-16" label)

              yield! annotationErrors owner label k.Annotations
              yield!
                  fieldErrors
                      label
                      (fun j -> owner @ [ key "fields"; item j ])
                      (Binder.DeclaresNone(if what = "kind" then "a kind" else "an op"))
                      k.Fields
              yield! keyedErrors (what = "kind") label (fun j -> owner @ [ key "fields"; item j ]) k.Fields ]

        // The analysis both instantiation rules read, of what a value on the wire holds: found once
        // per check, and only where a rule has a member to judge.
        let wireAnalysis =
            lazy (Instantiations.analyse (Instantiations.ofIdl Instantiations.wire idl))

        // How many instantiations the walks of this check have read, both rules' and every depth's
        // together, each kind of walk against [[Instantiations.stepLimit]].
        let stepsRead = Instantiations.Steps.None()

        /// Phase 732 — the walk the two rules below share ([[Instantiations.judgeAtParameters]]),
        /// and each member's fault from what became of it.
        ///
        /// The instantiations reached are those a document can hold a value of: from each owner that
        /// declares no parameter (a kind, an op, the node envelope, a union or a record declaring
        /// none, a contract method), through the wire members of each declaration at the arguments
        /// applied. An instantiation named only as an argument no member holds (`Phantom<Opt<int>>`,
        /// where `Phantom<T>` holds nothing at `T`) is not one: no document holds a value of it.
        /// What the walks share is found once per check (`wireAnalysis`), and both rules and every
        /// depth count against one step budget for each kind of walk (`stepsRead`).
        ///
        /// A member is refused in ONE fault: the first instantiation found that it does not fit,
        /// and how many more of those found it does not fit, always as a lower bound, `0` included
        /// (`… and at least 4095 more`, `… and at least 0 more`): not every instantiation reached is
        /// found, so the count never claims to be every one. A
        /// member the walk that reads each argument whole cannot judge, past one of its counts, is
        /// judged in classes; one the walks in classes cannot judge either, past
        /// [[Instantiations.parameterLimit]] classes at a parameter it reads or once they have
        /// read [[Instantiations.stepLimit]] instantiations short of its declaration, is refused in
        /// a sentence of its own, never admitted unjudged.
        let overInstantiations (what: string) (asked: Asked list) : VocabularyFault list =
            let wire = Instantiations.wire.Members

            let roots =
                [ for k in idl.Kinds @ idl.Ops do
                      yield! wire k.Fields
                  yield! wire idl.NodeFields
                  for u in idl.Unions do
                      if List.isEmpty u.Params then
                          yield! u.Cases |> List.collect (fun c -> wire c.Fields)
                  for r in idl.Records do
                      if List.isEmpty r.Params then
                          yield! wire r.Fields ]
                |> List.map _.Type
                |> fun owned ->
                    owned
                    @ (idl.Contracts
                       |> List.collect _.Methods
                       |> List.collect (fun m -> (m.Arguments |> List.map _.Type) @ Option.toList m.Result))

            Instantiations.judgeAtParameters
                (wireAnalysis.Force())
                roots
                mentionsVar
                stepsRead
                (asked |> List.map _.Judged)
            |> List.zip asked
            |> List.choose (fun (a, outcome) ->
                let declaration = a.Judged.Declaration

                match outcome with
                | Instantiations.Outcome.Admitted
                | Instantiations.Outcome.Deeper -> None
                | Instantiations.Outcome.Refused(instantiation, why, more) ->
                    let spelled =
                        Instantiations.spellAbbreviated instantiation
                        + sprintf " and at least %d more" more

                    Some
                        { At = a.At
                          Detail = a.Refusal spelled why }
                | Instantiations.Outcome.TooMany ->
                    Some
                        { At = a.At
                          Detail = Instantiations.tooManyToJudge declaration a.Name what }
                | Instantiations.Outcome.TooLong ->
                    Some
                        { At = a.At
                          Detail = Instantiations.tooLongToJudge declaration a.Name what })

        /// Phase 732 — an omit-at-default member whose type names a type parameter has no one type
        /// to be checked at where it is declared: its literal is a value of `T` at `Opt<string>` and
        /// is none at `Opt<int>`, where a document that leaves the member out read to a value no
        /// encoder writes. So it is refused at the declaration, naming the member and the
        /// instantiation, exactly where the vocabulary reaches an instantiation its literal does not
        /// fit ([[overInstantiations]]).
        ///
        /// A literal that fits its member's type with the parameters left UNBOUND (an empty list at
        /// `list<T>`) fits every instantiation: the encoder refuses an unbound parameter wherever it
        /// reads one, so it read none. Such a member asks nothing more, and a vocabulary holding no
        /// other is not walked at all. By the same property, a literal that fits the member's type at
        /// arguments cut at a depth fits it at every argument that agrees to that depth, and one the
        /// encoder refuses there for anything but an unbound parameter it refuses at each of them:
        /// up to that refusal it read only what they share.
        let instantiatedDefaultErrors () : VocabularyFault list =
            let atParameter (declaration: string) (fields: IdlField list) (place: int -> PathSegment list * string) =
                [ for j, f in List.indexed fields do
                      match f.Opt with
                      | OmitDefault d when mentionsVar f.Type && Result.isError (Encode.renderValue idl f.Type d) ->
                          let path, owner = place j
                          let name = sprintf "%s, field '%s'" owner f.Name

                          yield
                              { At = path @ [ key "optionality"; key "default" ]
                                Name = name
                                Judged =
                                  { Declaration = declaration
                                    Reads = Instantiations.positionsNamed (wireAnalysis.Force()) declaration f.Type
                                    Judge =
                                      fun cut binding ->
                                          let at = TypeParams.substitute binding f.Type

                                          match Encode.renderValue idl at d with
                                          // Phase 723 — and is canonical there ([[dropped]]): which
                                          // member holds a condition can be the instantiation's to say.
                                          | Ok _ ->
                                              match dropped at d with
                                              | Some found ->
                                                  Instantiations.Verdict.Fails(notCanonicalWhy + droppedText found)
                                              | None -> Instantiations.Verdict.Fits
                                          | Error m when cut && m = Encode.unsubstituted "" ->
                                              Instantiations.Verdict.Undecided
                                          | Error m -> Instantiations.Verdict.Fails m }
                                Refusal =
                                  fun instantiation why ->
                                      if why.StartsWith(notCanonicalWhy, System.StringComparison.Ordinal) then
                                          sprintf
                                              "%s: the omit-at-default value is not canonical at the instantiation %s: %s"
                                              name
                                              instantiation
                                              (why.Substring notCanonicalWhy.Length)
                                      else
                                          sprintf
                                              "%s: the omit-at-default value does not fit the field's type at the instantiation %s (%s)"
                                              name
                                              instantiation
                                              why }
                      | _ -> () ]

            // Each declaration holding such a member, by its key in the reading.
            let held: Asked list =
                [ for i, r in List.indexed idl.Records do
                      let declaration = Instantiations.recordKey r.Name

                      declaration,
                      atParameter declaration r.Fields (fun j -> recordField i j, sprintf "record '%s'" r.Name)

                  for i, u in List.indexed idl.Unions do
                      let declaration = Instantiations.unionKey u.Name

                      declaration,
                      [ for c, case in List.indexed u.Cases do
                            yield!
                                atParameter declaration case.Fields (fun j ->
                                    caseField i c j, sprintf "union '%s' case '%s'" u.Name case.Tag) ] ]
                |> List.filter (fun (_, members) -> not (List.isEmpty members))
                |> Map.ofList
                |> Map.toList
                |> List.collect snd

            if List.isEmpty held then
                []
            else
                overInstantiations "its default" held

        /// Phase 732 — a transparent case whose field is typed at a type parameter is refused at a
        /// reference that instantiates it to an object ([[typeErrors]]). A reference that applies the
        /// union to its HOLDER's own parameter (`Hold<T> { u: T1<T> }`) has no argument to ask there,
        /// and the holder may be instantiated to one (`Hold<Box<int>>`): the encoder then writes the
        /// case bare, as an object no decoder reads back. So it is asked again at every
        /// instantiation of the holder a document can hold a value of ([[overInstantiations]]), and
        /// refused at the holder's member that writes the reference, naming the instantiation.
        ///
        /// Judged there: each union reference WRITTEN in the member's type with an argument that names
        /// a parameter, where a value of the member holds one, as written (a walk over the member's
        /// type enters it: [[Instantiations.entered]]). A value written is what cannot be read back,
        /// and where the member holds no value of the union no bytes of it are written. A reference
        /// written with no parameter is the first rule's, wherever it stands.
        ///
        /// Whether a type can encode to an object is read off its outermost form, which a cut one
        /// level down or deeper keeps, so a group of instantiations is decided at its first member.
        let instantiatedTransparentErrors () : VocabularyFault list =
            // The union references written in `t` with an argument that names a parameter.
            let rec openReferences (t: IdlType) : IdlType list =
                match t with
                | TUnion(_, args) ->
                    (if List.exists mentionsVar args then [ t ] else [])
                    @ List.collect openReferences args
                | TRecord(_, args)
                | TTuple args -> List.collect openReferences args
                | TList inner
                | TMap inner
                | TSet inner -> openReferences inner
                | TKeyedMap(k, v) -> openReferences k @ openReferences v
                | TStr
                | TInt
                | TBool
                | TFloat
                | TEnum _
                | TVar _
                | TNode
                | TKind
                | TOp
                | TClosure
                | TOpaque
                | TFn _
                | TJson
                | THosted _
                | TInt64
                | TDecimal
                | TBytes
                | TTimeOfDay
                | TDuration -> []

            // The unions a reference can instantiate to an object: a transparent case whose one
            // field names a parameter.
            let transparentAtParameter: Set<string> =
                idl.Unions
                |> List.filter (fun u ->
                    match WireForm.ofType idl (TUnion(u.Name, u.Params |> List.map TVar)) with
                    | WireForm.Union form -> transparentField form |> Option.exists (fun f -> mentionsVar f.Type)
                    | _ -> false)
                |> List.map _.Name
                |> Set.ofList

            let atParameter (declaration: string) (fields: IdlField list) (place: int -> PathSegment list * string) =
                [ for j, f in List.indexed fields do
                      let candidates =
                          if f.Opt = HostOnly then
                              []
                          else
                              openReferences f.Type
                              |> List.filter (fun r ->
                                  match r with
                                  | TUnion(n, _) -> transparentAtParameter.Contains n
                                  | _ -> false)

                      if not (List.isEmpty candidates) then
                          let analysis = wireAnalysis.Force()
                          let entered = Instantiations.entered analysis f.Type

                          let references =
                              candidates
                              |> List.filter (fun r ->
                                  match r with
                                  | TUnion(n, args) -> List.contains (Instantiations.unionKey n, args) entered
                                  | _ -> false)

                          if not (List.isEmpty references) then
                              let path, owner = place j
                              let name = sprintf "%s, field '%s'" owner f.Name

                              yield
                                  { At = path @ [ key "type" ]
                                    Name = name
                                    Judged =
                                      { Declaration = declaration
                                        Reads =
                                          references
                                          |> List.collect (Instantiations.positionsNamed analysis declaration)
                                          |> List.distinct
                                          |> List.sort
                                        // Read off each argument's outermost form, which every cut
                                        // the walk makes keeps: never undecided.
                                        Judge =
                                          fun _ binding ->
                                              references
                                              |> List.tryPick (fun r ->
                                                  match WireForm.ofType idl (TypeParams.substitute binding r) with
                                                  | WireForm.Union u ->
                                                      objectAtReference u
                                                      |> Option.map (fun field ->
                                                          sprintf
                                                              "union '%s' so that its transparent case's field '%s'"
                                                              u.Name
                                                              field.Name)
                                                  | _ -> None)
                                              |> Option.map Instantiations.Verdict.Fails
                                              |> Option.defaultValue Instantiations.Verdict.Fits }
                                    Refusal =
                                      fun instantiation why ->
                                          sprintf
                                              "%s: at the instantiation %s, instantiates %s can encode to an object — %s"
                                              name
                                              instantiation
                                              why
                                              bareObject } ]

            // Each generic declaration holding such a member, by its key in the reading.
            let held: Asked list =
                [ for i, r in List.indexed idl.Records do
                      if not (List.isEmpty r.Params) then
                          let declaration = Instantiations.recordKey r.Name

                          declaration,
                          atParameter declaration r.Fields (fun j -> recordField i j, sprintf "record '%s'" r.Name)

                  for i, u in List.indexed idl.Unions do
                      if not (List.isEmpty u.Params) then
                          let declaration = Instantiations.unionKey u.Name

                          declaration,
                          [ for c, case in List.indexed u.Cases do
                                yield!
                                    atParameter declaration case.Fields (fun j ->
                                        caseField i c j, sprintf "union '%s' case '%s'" u.Name case.Tag) ] ]
                |> List.filter (fun (_, members) -> not (List.isEmpty members))
                |> Map.ofList
                |> Map.toList
                |> List.collect snd

            if List.isEmpty held then
                []
            else
                overInstantiations "the transparent case it holds" held

        [ yield! wireShapeErrors idl
          yield! enumWireErrors idl
          yield! hostedWireErrors idl
          yield! layoutErrors idl

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
              let owner = sprintf "record '%s'" r.Name

              yield!
                  nameErrors
                      (owner + ": type parameter")
                      (r.Params
                       |> List.mapi (fun p n -> [ key "records"; item i; key "params"; item p ], n))

              yield! fieldErrors owner (recordField i) (Binder.Declares("record", r.Params)) r.Fields
              yield! keyedErrors false owner (recordField i) r.Fields

          for i, u in List.indexed idl.Unions do
              let owner = sprintf "union '%s'" u.Name
              let at = [ key "unions"; item i ]

              if List.isEmpty u.Cases then
                  fault (at @ [ key "cases" ]) (sprintf "%s declares no case, so it has no value" owner)

              yield!
                  nameErrors
                      (owner + ": type parameter")
                      (u.Params |> List.mapi (fun p n -> at @ [ key "params"; item p ], n))

              yield!
                  nameErrors
                      (owner + ": case")
                      (u.Cases
                       |> List.mapi (fun c case -> at @ [ key "cases"; item c; key "tag" ], case.Tag))

              // Phase 550 — the tag-naming rule derives each case's WIRE tag from its name, so two
              // distinct names can derive one tag (`Abc` and `abc` under `lowerFirst`): the hosts
              // could not tell the two cases apart on the wire. A name given twice is refused above.
              for c, case in List.indexed u.Cases do
                  let wire = u.WireTagOf case.Tag

                  match
                      u.Cases
                      |> List.take c
                      |> List.tryFind (fun other -> other.Tag <> case.Tag && u.WireTagOf other.Tag = wire)
                  with
                  | Some other ->
                      fault
                          (at @ [ key "cases"; item c; key "tag" ])
                          (sprintf
                              "%s: case '%s' derives the wire tag '%s', which case '%s' already carries under the union's tag rule"
                              owner
                              case.Tag
                              wire
                              other.Tag)
                  | None -> ()

              // Phase 550 — a case-level host codec reads and writes the members its fields declare,
              // so it declares no wire form of its own; and a transparent case has no members to hand
              // it, being written bare.
              for c, case in List.indexed u.Cases do
                  match case.Hosted with
                  | Some h ->
                      let path = at @ [ key "cases"; item c; key "hosted" ]
                      let caseOwner = sprintf "union '%s' case '%s'" u.Name case.Tag

                      if h.Wire.IsSome || h.Format.IsSome then
                          fault
                              path
                              (sprintf
                                  "%s: a case-level host codec declares no wire form or format — the case's fields state its members"
                                  caseOwner)

                      if TransparentUnion.tag idl.Harden u = Some case.Tag then
                          fault
                              path
                              (sprintf
                                  "%s: a transparent case is written bare and has no members for a case-level host codec to write"
                                  caseOwner)
                  | None -> ()

              for c, case in List.indexed u.Cases do
                  let caseOwner = sprintf "union '%s' case '%s'" u.Name case.Tag
                  yield! annotationErrors (at @ [ key "cases"; item c ]) caseOwner case.Annotations
                  yield! fieldErrors caseOwner (caseField i c) (Binder.Declares("union", u.Params)) case.Fields
                  yield! keyedErrors false caseOwner (caseField i c) case.Fields

          for i, e in List.indexed idl.Enums do
              let owner = sprintf "enum '%s'" e.Name
              let at = enumAt i

              if List.isEmpty e.Cases then
                  fault (at @ [ key "cases" ]) (sprintf "%s declares no case, so it has no value" owner)

              // A case NAME is the host identifier; its WIRE string is data, escaped where
              // it is spliced, and held only to well-formedness.
              for c, name in List.indexed e.Cases do
                  if not (Contract.isIdentifier name) then
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

          yield! fieldErrors "the node envelope" envelopeField (Binder.DeclaresNone "the node envelope") idl.NodeFields
          yield! keyedErrors false "the node envelope" envelopeField idl.NodeFields
          yield! instantiatedDefaultErrors ()
          yield! instantiatedTransparentErrors ()

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
                  | Ok _ ->
                      match dropped f.Type d.Value with
                      | Some found -> fault [ key "defaults"; item i; key "value" ] (notCanonical at "the value" found)
                      | None -> ()
                  | Error m ->
                      fault
                          [ key "defaults"; item i; key "value" ]
                          (sprintf "%s: the value does not fit the field's type (%s)" at m)

          for k, (d, again) in
              List.indexed (List.zip idl.Defaults (givenAgain (idl.Defaults |> List.map (fun d -> d.Kind, d.Field)))) do
              if again then
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

          let transparent = idl.Harden.TransparentUnions

          for k, ((name, _), again) in List.indexed (List.zip transparent (givenAgain (List.map fst transparent))) do
              if again then
                  fault
                      [ key "harden"; key "transparentUnions"; item k ]
                      (sprintf "harden policy: union '%s' declares more than one transparent case" name)

          // Phase 644 — contracts. The names a method's types use are held to the vocabulary as a
          // field's are; the contract's own rules are `Contract.faults`, each a named refusal.
          for i, c in List.indexed idl.Contracts do
              for j, m in List.indexed c.Methods do
                  let at = sprintf "method '%s'" (Contract.methodName c.Name m.Id)
                  let mAt = [ key "contracts"; item i; key "methods"; item j ]

                  for k, a in List.indexed m.Arguments do
                      yield!
                          typeErrors
                              (mAt @ [ key "arguments"; item k; key "type" ])
                              (sprintf "%s: argument '%s'" at a.Name)
                              (Binder.DeclaresNone "a contract's method")
                              a.Type

                  match m.Result with
                  | Some t ->
                      yield!
                          typeErrors
                              (mAt @ [ key "result" ])
                              (at + ": its result")
                              (Binder.DeclaresNone "a contract's method")
                              t
                  | None -> ()

          for path, f in Contract.faults idl do
              fault path (ContractFault.describe f) ]
