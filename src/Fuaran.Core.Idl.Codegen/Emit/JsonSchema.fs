namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit.Core

/// The JSON-Schema emitter — every declared type as a `$def`, generic unions once per
/// instantiation the vocabulary names.
module internal JsonSchema =

    // -----------------------------------------------------------------------
    // fuaran#317 increment 4 — the `schema.json` leg: emit a Draft 2020-12 JSON
    // Schema describing the canonical wire, from the same IDL. The third of the
    // §11 "triple mirror" (encoder + decoder already IDL-driven), so one IDL now
    // drives all three.
    //
    // fuaran#1068 — a GENERIC union is emitted once per INSTANTIATED type argument
    // rather than once, type-erased. JSON Schema has no type parameters, which is
    // why the leg originally emitted a generic union's `'T`-typed fields as the
    // permissive `{}`; but a schema needs no type parameters to state the
    // instantiations, because the IDL names every one of them at the slot. The
    // erased form's cost was measurable and was measured: a `Binding<float>` slot
    // accepted a boolean and a `Binding<int>` slot accepted a §7 non-finite
    // sentinel, both of which the decoder refuses.
    // -----------------------------------------------------------------------

    /// The `$defs` NAME of a type: its instance name in the resolution ([[Decl.instName]]) —
    /// for a non-generic declaration its own name, for an instantiated generic union the name
    /// plus its mangled arguments (`Binding_float`). The instantiation walk names by this same
    /// function, so the set of names EMITTED and the set REFERENCED are computed one way, and a
    /// dangling `$ref` (an ERROR to a strict validator, never a permissive skip) cannot arise
    /// from the two disagreeing.
    let defName (t: IdlType) : string = Decl.instName t

    // -----------------------------------------------------------------------
    // Phase 704 — a declaration's `doc` annotation (Phase 255) is the JSON-Schema `description`: a
    // type's on its definition, a case's on its branch, a field's on its property. An absent doc
    // writes nothing, so a vocabulary that documents nothing emits the bytes it always did.
    //
    // What has no object of its own has nowhere to carry one: an enum case is a string in the
    // definition's `enum` array, and an inline record or a presence-keyed union lays its members
    // out on the object holding it (their fields' docs still reach those members).
    // -----------------------------------------------------------------------

    /// `schema` carrying `doc` as its `description`. On a definition or a branch it leads, as the
    /// schema of a method's arguments states its own; on a property it follows the keywords that
    /// say what the slot holds, as an argument's does. "Any JSON" (`true`) with a description is
    /// the object holding only the description, which admits exactly what `true` admits.
    let private described (leading: bool) (doc: string option) (schema: JVal) : JVal =
        match doc, schema with
        | None, s -> s
        | Some d, JObj fs when leading -> JObj(("description", JStr d) :: fs)
        | Some d, JObj fs -> JObj(fs @ [ "description", JStr d ])
        | Some d, _ -> JObj [ "description", JStr d ]

    /// A definition's or a branch's schema, carrying the declaration's doc.
    let private definitionDoc (ann: Annotations) (schema: JVal) : JVal = described true ann.Doc schema

    /// A property's schema, carrying the field's doc.
    let private propertyDoc (f: IdlField) (schema: JVal) : JVal =
        described false f.Annotations.Doc schema

    // -----------------------------------------------------------------------
    // Phase 639 (DECISIONS.md D251) — the string-borne scalars' canonical spellings as ECMA-262
    // patterns, each EXACT: a string matches exactly when every reader admits it, range included.
    // What JSON Schema cannot state is a set's and a keyed map's canonical ORDER (it has no
    // vocabulary for an order over items); `uniqueItems` states a set's distinctness, and the order
    // is each reader's to check.
    // -----------------------------------------------------------------------

    let private digitClass (lo: int) (hi: int) : string =
        if lo = hi then string lo else sprintf "[%d-%d]" lo hi

    let private digitAt (s: string) (i: int) : int = int s[i] - int '0'

    /// A pattern alternation matching the canonical naturals (no leading zero) at most `bound`,
    /// zero included when `zero`.
    let private naturalAtMost (bound: string) (zero: bool) : string =
        let n = bound.Length

        [ if zero then
              "0"
          if n >= 2 then
              sprintf "[1-9][0-9]{0,%d}" (n - 2)
          for i in 0 .. n - 1 do
              let lo = if i = 0 then 1 else 0
              let hi = digitAt bound i - 1

              if hi >= lo then
                  bound.Substring(0, i)
                  + digitClass lo hi
                  + (if n - i - 1 > 0 then
                         sprintf "[0-9]{%d}" (n - i - 1)
                     else
                         "")
          bound ]
        |> String.concat "|"

    /// A pattern alternation matching the canonical fractions (one to `bound.Length` digits, the last
    /// not zero) whose digits padded with zeros to `bound.Length` are at most `bound`.
    let private fractionAtMost (bound: string) : string =
        [ for n in 1 .. bound.Length do
              let p = bound.Substring(0, n)

              for i in 0 .. n - 1 do
                  let rest = n - i - 1
                  let lo = if rest = 0 then 1 else 0
                  let hi = digitAt p i - 1

                  if hi >= lo then
                      p.Substring(0, i)
                      + digitClass lo hi
                      + (if rest = 0 then ""
                         elif rest = 1 then "[1-9]"
                         else sprintf "[0-9]{%d}[1-9]" (rest - 1))

              if p[n - 1] <> '0' then
                  p ]
        |> String.concat "|"

    /// The exact pattern of each string-borne scalar's canonical spelling.
    let internal textPattern (t: IdlType) : string option =
        let fraction7 = @"\.[0-9]{0,6}[1-9]"

        let duration (fractionBound: string) =
            sprintf
                "(?:(?:%s)(?:%s)?|922337203685(?:\\.(?:%s))?)s"
                (naturalAtMost "922337203684" true)
                fraction7
                (fractionAtMost fractionBound)

        match t with
        | TInt64 ->
            Some(
                sprintf
                    "^(?:%s|-(?:%s))$"
                    (naturalAtMost "9223372036854775807" true)
                    (naturalAtMost "9223372036854775808" false)
            )
        | TDecimal -> Some @"^(?!-0$)-?(?:0|[1-9][0-9]*)(?:\.[0-9]*[1-9])?$"
        | TBytes -> Some @"^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/][AQgw]==|[A-Za-z0-9+/]{2}[AEIMQUYcgkosw048]=)?$"
        | TTimeOfDay -> Some(@"^(?:[01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9](?:" + fraction7 + ")?$")
        | TDuration -> Some(sprintf "^(?!-0s$)(?:%s|-%s)$" (duration "4775807") (duration "4775808"))
        | _ -> None

    let rec schemaOf (t: IdlType) : JVal =
        match t with
        | TInt64
        | TDecimal
        | TBytes
        | TTimeOfDay
        | TDuration ->
            JObj
                [ "type", JStr "string"
                  "pattern", JStr(textPattern t |> Option.defaultValue "") ]
        | TTuple components ->
            JObj
                [ "type", JStr "array"
                  "prefixItems", JArr(components |> List.map schemaOf)
                  "minItems", JInt components.Length
                  "maxItems", JInt components.Length ]
        | TSet element -> JObj [ "type", JStr "array"; "items", schemaOf element; "uniqueItems", JBool true ]
        | TKeyedMap(k, v) -> JObj [ "type", JStr "array"; "items", schemaOf (TTuple [ k; v ]) ]
        | TStr -> JObj [ "type", JStr "string" ]
        | TInt -> JObj [ "type", JStr "integer" ]
        | TBool -> JObj [ "type", JStr "boolean" ]
        // WIRE_FORMAT §5/§7 — a float slot admits a JSON number OR one of the three
        // quoted non-finite sentinels. `TInt` above is deliberately untouched: §7
        // stops at the float slot, so an integer slot still refuses them.
        | TFloat ->
            JObj
                [ "anyOf",
                  JArr
                      [ JObj [ "type", JStr "number" ]
                        JObj [ "enum", JArr [ JStr "NaN"; JStr "Infinity"; JStr "-Infinity" ] ] ] ]
        | TEnum n -> JObj [ "$ref", JStr("#/$defs/" + n) ]
        // fuaran#1068 — an instantiated generic union names ITS OWN definition. A
        // non-generic one is `defName`'s identity case, so this line is unchanged
        // for every union that has no parameters.
        | TUnion _ -> JObj [ "$ref", JStr("#/$defs/" + defName t) ]
        // Reached only for a parameter no instantiation bound — a declaration read
        // outside any instantiation walk. The permissive `{}` is the pre-1068
        // behaviour, kept as the honest answer to "this slot's type is not yet known".
        | TVar _ -> JObj []
        | TNode -> JObj [ "$ref", JStr "#/$defs/Node" ]
        | TKind -> JObj [ "$ref", JStr "#/$defs/NodeKind" ]
        | TOp -> JObj [ "$ref", JStr "#/$defs/TreeOp" ]
        | TList inner -> JObj [ "type", JStr "array"; "items", schemaOf inner ]
        // Closure / opaque fields are sentinel strings on the wire.
        | TClosure
        | TFn _ -> JObj [ "type", JStr "string"; "const", JStr "<closure>" ]
        | TOpaque -> JObj [ "type", JStr "string"; "const", JStr "<opaque>" ]
        // fuaran#676 — "any JSON": the schema deliberately does not constrain content
        // the encoder does not decompose, matching how the hand-written schema already
        // renders the rule-12 structured-payload positions. A hosted slot is the same
        // deliberate abstention: its content belongs to the host codec's own spec.
        | THosted { Wire = Some w; Format = fmt } ->
            // Phase 252 — a hosted slot that declares its wire form is stated as that type,
            // with its format beside it; only an undeclared one keeps the abstention below.
            match schemaOf w, fmt with
            | JObj fs, Some f -> JObj(fs @ [ "format", JStr(HostedFormat.name f) ])
            | s, _ -> s
        | TJson
        | THosted _ -> JBool true
        | TRecord n -> JObj [ "$ref", JStr("#/$defs/" + n) ]
        | TMap vt -> JObj [ "type", JStr "object"; "additionalProperties", schemaOf vt ]

    /// The wire-visible fields of a declaration. A [[HostOnly]] field is never on
    /// the wire in any state (fuaran#691), so it is not a property the schema
    /// describes — listing it would advertise a key no encoder emits.
    let wireFields (fields: IdlField list) =
        fields |> List.filter (fun f -> f.Opt <> HostOnly)

    /// The property/required pair shared by every object-shaped schema.
    ///
    /// **`additionalProperties` is deliberately absent** (fuaran#697). The decoder
    /// tolerates unknown keys — it looks fields up by name, `WIRE_FORMAT.md` §2.1
    /// rule 2 — and the published `schema.json` matches that tolerance. A generated
    /// schema that set `additionalProperties: false` would reject payloads the
    /// format accepts and the decoder round-trips, which makes it a fourth mirror
    /// disagreeing with the spec rather than a projection of it. Forward
    /// compatibility is the point: an older host validating a newer producer's
    /// output must not fail on a key it has not learned yet.
    ///
    /// Phase 474 — an INLINE declaration's members are the holding object's own properties, laid
    /// out where its field is declared: an inline record's wire members (required as declared), and
    /// every member a presence-keyed union's cases write, in its roster order, each stated at the
    /// type of the first case declaring it and REQUIRED only when every case writes it (a
    /// `Required` union, its key or a synthesised member every other case carries). The schema
    /// states which members may stand there; which case they spell is the decoder's reading.
    let rec wireMembers (idl: Idl) (fields: IdlField list) : (string * JVal * bool) list =
        [ for f in wireFields fields do
              match Decl.inlineSlot idl f.Type with
              | Some(InlineSlot.Record r) -> yield! wireMembers idl r.Fields
              | Some(InlineSlot.Keyed(u, keyed)) ->
                  let written (c: IdlUnionCase) =
                      (c.Fields |> List.filter (fun g -> g.Opt = Required) |> List.map _.Name)
                      @ (keyed
                         |> List.tryFind (fun k -> k.Case = c.Tag)
                         |> Option.map (fun k -> k.Synthesised |> List.map fst)
                         |> Option.defaultValue [])

                  for name in Decl.keyedRoster u do
                      let declared =
                          u.Cases
                          |> List.pick (fun c -> c.Fields |> List.tryFind (fun g -> g.Name = name))

                      let always =
                          f.Opt = Required
                          && u.Cases |> List.forall (fun c -> List.contains name (written c))

                      yield name, propertyDoc declared (schemaOf declared.Type), always
              | None -> yield f.Name, propertyDoc f (schemaOf f.Type), f.Opt = Required ]

    let objectBody (idl: Idl) (fields: IdlField list) =
        let wire = wireMembers idl fields

        [ "required", JArr(wire |> List.filter (fun (_, _, r) -> r) |> List.map (fun (n, _, _) -> JStr n))
          "properties", JObj(wire |> List.map (fun (n, sch, _) -> n, sch)) ]

    /// An object schema with a discriminator const (for a kind / union case) + its
    /// fields — the key is the vocabulary's DECLARED discriminator (Phase 108).
    let objectSchema (idl: Idl) (disc: string) (typeConst: string) (fields: IdlField list) : JVal =
        let wire = wireMembers idl fields

        let props =
            (disc, JObj [ "const", JStr typeConst ])
            :: (wire |> List.map (fun (n, sch, _) -> n, sch))

        let required =
            disc
            :: (wire |> List.filter (fun (_, _, r) -> r) |> List.map (fun (n, _, _) -> n))

        JObj
            [ "type", JStr "object"
              "required", JArr(required |> List.map JStr)
              "properties", JObj props ]

    /// A NON-discriminated object schema — a [[TRecord]] (`FormField`, `FilterSpec`,
    /// `TabHeader`, `ColumnErased`, …). No `$type` const: that is exactly what
    /// distinguishes a record from a union case on the wire.
    let recordSchema (idl: Idl) (r: IdlRecord) : JVal =
        JObj(("type", JStr "object") :: objectBody idl r.Fields)

    /// The schema's two parts: the root member, and every definition in emission order. One
    /// assembly, read by the whole-vocabulary schema and (Phase 644) by a method's arguments, so
    /// a type has one definition whichever schema names it.
    let private assemble (decl: Decl) : Result<(string * JVal) * (string * JVal) list, CodegenError> =
        let idl = decl.Idl

        let enumDef (e: IdlEnum) =
            // The `enum` array is a WIRE contract — wire strings, not host case names.
            e.Name,
            definitionDoc e.Annotations (JObj [ "type", JStr "string"; "enum", JArr(e.WireCases |> List.map JStr) ])

        /// One union definition, under a substitution for its type parameters
        /// (empty for a non-generic union, so its emission is byte-identical to
        /// what it always was).
        let unionDefWith (subst: Map<string, IdlType>) (name: string) (u: IdlUnion) =
            let fieldsOf (c: IdlUnionCase) =
                c.Fields
                |> List.map (fun f ->
                    { f with
                        Type = TypeParams.substitute subst f.Type })

            let tagged =
                u.Cases
                |> List.map (fun c ->
                    objectSchema idl idl.Wire.Discriminator (u.WireTagOf c.Tag) (fieldsOf c)
                    |> definitionDoc c.Annotations)

            // A transparent case is on the wire BARE — its single field's value with
            // no `$type` envelope (`TextSource.Literal`: `"x"`, not
            // `{"$type":"Literal","text":"x"}`). The codec legs already special-case
            // it; without reflecting it here the schema rejects the CANONICAL form of
            // every literal string in the corpus. The tagged branch stays: §16
            // lenient-accept admits the envelope on input.
            let bare =
                match Decl.transparentCase decl u with
                | None -> []
                | Some c -> fieldsOf c |> List.map (fun f -> definitionDoc c.Annotations (schemaOf f.Type))

            // A generic union's doc is every instantiation's: each is the union at one argument.
            name, definitionDoc u.Annotations (JObj [ "oneOf", JArr(bare @ tagged) ])

        let unionDef (u: IdlUnion) = unionDefWith Map.empty u.Name u

        // ── fuaran#1068: the instantiations of every GENERIC union ─────────────
        //
        // A generic union has no single wire shape — `Binding<float>` and `Binding<int>` accept
        // different `Static` payloads — so it is emitted once per instantiation the vocabulary
        // names, and never under its bare name. The instantiations are the resolution's
        // (`Decl.instantiations`, Phase 511), keyed by the same instance name `schemaOf`
        // references, so the two agree by construction.
        //
        // Phase 704 — those names (`Binding_float`, `Binding_list_SelectOption`) are already the
        // ones a host's hand-written schema publishes for the same instantiations, so no second
        // naming rule is declared beside them. A host definition that differs in SHAPE as well as
        // name (one instantiation read two ways) is a second declaration, not a renaming.
        let genericDefsR =
            Decl.instantiations idl
            |> Result.map (List.map (fun i -> unionDefWith i.Subst i.Name i.Union))

        let recordDef (r: IdlRecord) =
            r.Name, definitionDoc r.Annotations (recordSchema idl r)

        // A kind and an op are both an `IdlKind`, defined the same way.
        let kindDef (k: IdlKind) =
            k.Tag, definitionDoc k.Annotations (objectSchema idl idl.Wire.Discriminator k.Tag k.Fields)

        // Phase 511 — the node envelope from its layout: one object carrying `id`, the kind under
        // its key when the kind is nested, and the declared envelope members (fuaran#690: optional by
        // construction, so `required` names only those declared Required). Phase 109 — a MERGED
        // kind makes a node a kind object also carrying that envelope, which `allOf` states.
        let nodeDef =
            let kindKeys =
                match decl.Envelope.Kind with
                | KindPlacement.NestedUnder key -> [ key ]
                | KindPlacement.Merged -> []

            let envelopeObject =
                JObj
                    [ "type", JStr "object"
                      "required",
                      JArr(
                          JStr "id" :: (kindKeys |> List.map JStr)
                          @ (decl.Envelope.Members
                             |> List.filter (fun f -> f.Opt = Required)
                             |> List.map (fun f -> JStr f.Name))
                      )
                      "properties",
                      JObj(
                          ("id", JObj [ "type", JStr "string" ])
                          :: (kindKeys |> List.map (fun key -> key, JObj [ "$ref", JStr "#/$defs/NodeKind" ]))
                          @ (wireFields decl.Envelope.Members
                             |> List.map (fun f -> f.Name, propertyDoc f (schemaOf f.Type)))
                      ) ]

            match decl.Envelope.Kind with
            | KindPlacement.NestedUnder _ -> "Node", envelopeObject
            | KindPlacement.Merged ->
                "Node", JObj [ "allOf", JArr [ JObj [ "$ref", JStr "#/$defs/NodeKind" ]; envelopeObject ] ]

        // The kind alternation, named (fuaran#703). It was inlined into `Node.kind`,
        // which is equivalent for a node but leaves `TKind` — `EditNode.newKind`'s
        // type — with nothing to reference. Naming it also matches the published
        // schema, which has always carried a `NodeKind` definition.
        let nodeKindDef =
            "NodeKind",
            JObj [ "oneOf", JArr(decl.Kinds |> List.map (fun k -> JObj [ "$ref", JStr("#/$defs/" + k.Tag) ])) ]

        /// The op alternation. Absent when the domain declares no ops, so an
        /// op-free IDL's schema is exactly what it was.
        let treeOpDefs =
            if List.isEmpty idl.Ops then
                []
            else
                (idl.Ops |> List.map kindDef)
                @ [ "TreeOp",
                    JObj [ "oneOf", JArr(idl.Ops |> List.map (fun o -> JObj [ "$ref", JStr("#/$defs/" + o.Tag) ])) ] ]

        // The wire has TWO roots once ops are declared (`WIRE_FORMAT.md` §3.4) —
        // a payload is a Node or a TreeOp. They are distinguishable on structure
        // (a node carries `id` + `kind`, an op a top-level `$type`), so `oneOf`
        // states it exactly.
        let root =
            if List.isEmpty idl.Ops then
                "$ref", JStr "#/$defs/Node"
            else
                "oneOf", JArr [ JObj [ "$ref", JStr "#/$defs/Node" ]; JObj [ "$ref", JStr "#/$defs/TreeOp" ] ]

        // Records join the assembly (fuaran#697). Every `TRecord` slot emits a
        // `$ref` into `#/$defs/`, so omitting them left a dangling reference for
        // `FormField` / `FilterSpec` / `TabHeader` / `ColumnErased` / … — under a
        // strict validator an unresolvable `$ref` is an error, not a permissive
        // skip, so the leg could never have certified against the corpus.
        // A generic union contributes its INSTANTIATIONS (fuaran#1068), never its
        // bare erased name. An unparameterised union is unchanged.
        genericDefsR
        |> Result.map (fun genericDefs ->
            let defs =
                (decl.Enums |> List.map enumDef)
                // Phase 474 — an inline declaration has no object of its own, so no definition: its
                // members are stated on every object holding it ([[wireMembers]]).
                @ (decl.Unions
                   |> List.filter (fun u -> List.isEmpty u.Params && not (Decl.isKeyed u))
                   |> List.map unionDef)
                @ genericDefs
                @ (decl.Records |> List.filter (fun r -> not r.Inline) |> List.map recordDef)
                @ (decl.Kinds |> List.map kindDef)
                @ [ nodeKindDef; nodeDef ]
                @ treeOpDefs

            root, defs)

    let private draft = "$schema", JStr "https://json-schema.org/draft/2020-12/schema"

    /// Emit a Draft 2020-12 JSON Schema for the whole IDL's canonical wire.
    ///
    /// **Phase 195 — this returns a `Result` where it used to return a bare `string`**, for the
    /// reason [[fsharpTypes]] does: the leg has one construct it cannot emit — a generic union
    /// whose recursion GROWS its type argument, and so has no finite `$defs` — and answering
    /// that by throwing made a plain-string channel the one place the generator's refusal
    /// was an exception. The change is BREAKING for a caller that consumed the string directly.
    let jsonSchema (decl: Decl) : Result<string, CodegenError> =
        assemble decl
        |> Result.map (fun (root, defs) -> JObj [ draft; root; "$defs", JObj defs ] |> Json.render)

    // -----------------------------------------------------------------------
    // Phase 644 (DECISIONS.md D260.14, D260.18) — the JSON Schema of one contract method's
    // arguments, in the shape a tool definition takes: ONE object schema, a property an
    // argument. Nothing a runtime needs is emitted beside it.
    // -----------------------------------------------------------------------

    /// Every definition a schema value names (`#/$defs/<name>`), in the order met.
    let rec private refsIn (v: JVal) : string list =
        match v with
        | JObj fs ->
            fs
            |> List.collect (fun (k, x) ->
                match k, x with
                | "$ref", JStr r when r.StartsWith "#/$defs/" -> [ r.Substring 8 ]
                | _ -> refsIn x)
        | JArr xs -> xs |> List.collect refsIn
        | _ -> []

    /// The definitions `roots` reach, transitively, kept in emission order.
    let private reachable (defs: (string * JVal) list) (roots: JVal list) : (string * JVal) list =
        let byName = Map.ofList defs

        let rec close (seen: Set<string>) (frontier: string list) : Set<string> =
            match frontier |> List.filter (fun n -> not (seen.Contains n)) |> List.distinct with
            | [] -> seen
            | fresh ->
                close
                    (Set.union seen (Set.ofList fresh))
                    (fresh
                     |> List.collect (fun n -> byName |> Map.tryFind n |> Option.map refsIn |> Option.defaultValue []))

        let names = close Set.empty (roots |> List.collect refsIn)
        defs |> List.filter (fun (n, _) -> names.Contains n)

    /// An argument's type. A float argument is a NUMBER: the three non-finite tokens are refused
    /// at the registry (D260.7), so the schema does not offer them. Every other type is stated as
    /// the vocabulary's schema states it.
    let private argumentType (t: IdlType) : JVal =
        match t with
        | TFloat -> JObj [ "type", JStr "number" ]
        | other -> schemaOf other

    /// A bound, as the keywords JSON Schema has for it. A length bound is the tightest one that
    /// refuses no admitted value: the IDL counts UTF-16 code units and `minLength` / `maxLength`
    /// count characters, of which a string has at most as many and at least half as many.
    let private boundOf (c: ArgumentConstraint) : (string * JVal) list =
        match c with
        | ArgumentConstraint.IntBetween(lo, hi) -> [ "minimum", JInt lo; "maximum", JInt hi ]
        | ArgumentConstraint.FloatBetween(lo, hi) -> [ "minimum", JFloat lo; "maximum", JFloat hi ]
        | ArgumentConstraint.LengthBetween(lo, hi) -> [ "minLength", JInt((lo + 1) / 2); "maxLength", JInt hi ]

    /// The interpreter's encoding of a declared default at its argument's type (D129: the one call
    /// across the assembly boundary).
    [<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
    let private defaultAt (idl: Idl) (t: IdlType) (v: IdlValue) : Result<JVal, string> = Encode.valueJson idl t v

    /// The schema of `m`'s arguments: `title` the method's name (`<contract>.<method>`),
    /// `description` its description, a property an argument (its type's schema, then its
    /// description, its declared default and its bound), `required` the arguments a caller must
    /// supply, `additionalProperties` false (a binding the method does not declare is refused),
    /// and `$defs` the declared types the arguments reach and no other. A declared default that
    /// does not encode at its argument's type is refused, naming the argument.
    let methodArguments (decl: Decl) (contract: IdlContract) (m: IdlMethod) : Result<JVal, CodegenError> =
        let name = contract.Name + "." + m.Id

        let property (a: IdlArgument) : Result<string * JVal, CodegenError> =
            let stated =
                match argumentType a.Type with
                | JObj fs -> fs
                // "Any JSON" (`true`) states nothing; the keywords beside it are the whole schema.
                | _ -> []

            let described =
                match a.Doc with
                | Some d -> [ "description", JStr d ]
                | None -> []

            let bound = a.Constraint |> Option.map boundOf |> Option.defaultValue []

            let withDefault (fallback: (string * JVal) list) =
                a.Name, JObj(stated @ described @ fallback @ bound)

            match a.Opt with
            | OmitDefault v ->
                match defaultAt decl.Idl a.Type v with
                | Ok j -> Ok(withDefault [ "default", j ])
                | Error why ->
                    Error(
                        CodegenError.UnsupportedConstruct(
                            sprintf "the declared default of argument `%s` of method `%s`" a.Name name,
                            why,
                            "declare a default that encodes at the argument's type"
                        )
                    )
            | _ -> Ok(withDefault [])

        let properties =
            (Ok [], m.Arguments)
            ||> List.fold (fun acc a ->
                match acc, property a with
                | Error e, _
                | _, Error e -> Error e
                | Ok ps, Ok p -> Ok(ps @ [ p ]))

        match assemble decl, properties with
        | Error e, _
        | _, Error e -> Error e
        | Ok(_, defs), Ok props ->
            let reached = reachable defs (props |> List.map snd)

            Ok(
                JObj(
                    [ draft; "title", JStr name ]
                    @ (match m.Doc with
                       | Some d -> [ "description", JStr d ]
                       | None -> [])
                    @ [ "type", JStr "object"
                        "required",
                        JArr(
                            m.Arguments
                            |> List.filter (fun a -> a.Opt = Required)
                            |> List.map (fun a -> JStr a.Name)
                        )
                        "properties", JObj props
                        "additionalProperties", JBool false ]
                    @ (if reached.IsEmpty then [] else [ "$defs", JObj reached ])
                )
            )
