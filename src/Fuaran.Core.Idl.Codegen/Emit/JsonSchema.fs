namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.Reach

/// The JSON-Schema emitter — every declared type as a `$def`, generic unions once per
/// instantiation the vocabulary names.
module internal JsonSchema =

    // -----------------------------------------------------------------------
    // Phase 317 increment 4 — the `schema.json` leg: emit a Draft 2020-12 JSON
    // Schema describing the canonical wire, from the same IDL. The third of the
    // §11 "triple mirror" (encoder + decoder already IDL-driven), so one IDL now
    // drives all three.
    //
    // Phase 1068 — a GENERIC union is emitted once per INSTANTIATED type argument
    // rather than once, type-erased. JSON Schema has no type parameters, which is
    // why the leg originally emitted a generic union's `'T`-typed fields as the
    // permissive `{}`; but a schema needs no type parameters to state the
    // instantiations, because the IDL names every one of them at the slot. The
    // erased form's cost was measurable and was measured: a `Binding<float>` slot
    // accepted a boolean and a `Binding<int>` slot accepted a §7 non-finite
    // sentinel, both of which the decoder refuses.
    // -----------------------------------------------------------------------

    /// The `$defs` NAME of a type — for a non-generic declaration its own name,
    /// and for an instantiated generic union the name plus its mangled arguments
    /// (`Binding_float`, `Binding_list_SelectOption`). Total over `IdlType`, which
    /// is what makes it safe for `schemaOf` to name a `$def` for any slot it meets:
    /// the instantiation walk below uses this same function, so the set of names
    /// EMITTED and the set of names REFERENCED are computed one way, and a dangling
    /// `$ref` (an ERROR to a strict validator, never a permissive skip) cannot arise
    /// from the two disagreeing.
    let rec defName (t: IdlType) : string =
        match t with
        | TStr -> "str"
        | TInt -> "int"
        | TBool -> "bool"
        | TFloat -> "float"
        | TJson -> "json"
        | THosted _ -> "hosted"
        | TOpaque -> "opaque"
        | TClosure
        | TFn _ -> "closure"
        | TNode -> "Node"
        | TKind -> "NodeKind"
        | TOp -> "TreeOp"
        | TEnum n
        | TRecord n -> n
        | TVar v -> v
        | TList inner -> "list_" + defName inner
        | TMap vt -> "map_" + defName vt
        | TUnion(n, []) -> n
        | TUnion(n, args) -> n + "_" + (args |> List.map defName |> String.concat "_")

    let rec schemaOf (t: IdlType) : JVal =
        match t with
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
        // Phase 1068 — an instantiated generic union names ITS OWN definition. A
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
        // Phase 676 — "any JSON": the schema deliberately does not constrain content
        // the encoder does not decompose, matching how the hand-written schema already
        // renders the rule-12 structured-payload positions. A hosted slot is the same
        // deliberate abstention: its content belongs to the host codec's own spec.
        | THosted { Wire = Some w; Format = fmt } ->
            // Phase 252 — a hosted slot that declares its wire form is stated as that type,
            // with its format beside it; only an undeclared one keeps the abstention below.
            match schemaOf w, fmt with
            | JObj fs, Some f -> JObj(fs @ [ "format", JStr f ])
            | s, _ -> s
        | TJson
        | THosted _ -> JBool true
        | TRecord n -> JObj [ "$ref", JStr("#/$defs/" + n) ]
        | TMap vt -> JObj [ "type", JStr "object"; "additionalProperties", schemaOf vt ]

    /// The wire-visible fields of a declaration. A [[HostOnly]] field is never on
    /// the wire in any state (Phase 691), so it is not a property the schema
    /// describes — listing it would advertise a key no encoder emits.
    let wireFields (fields: IdlField list) =
        fields |> List.filter (fun f -> f.Opt <> HostOnly)

    /// The property/required pair shared by every object-shaped schema.
    ///
    /// **`additionalProperties` is deliberately absent** (Phase 697). The decoder
    /// tolerates unknown keys — it looks fields up by name, `WIRE_FORMAT.md` §2.1
    /// rule 2 — and the published `schema.json` matches that tolerance. A generated
    /// schema that set `additionalProperties: false` would reject payloads the
    /// format accepts and the decoder round-trips, which makes it a fourth mirror
    /// disagreeing with the spec rather than a projection of it. Forward
    /// compatibility is the point: an older host validating a newer producer's
    /// output must not fail on a key it has not learned yet.
    let objectBody (fields: IdlField list) =
        let wire = wireFields fields

        [ "required",
          JArr(
              wire
              |> List.filter (fun f -> f.Opt = Required)
              |> List.map (fun f -> JStr f.Name)
          )
          "properties", JObj(wire |> List.map (fun f -> f.Name, schemaOf f.Type)) ]

    /// An object schema with a discriminator const (for a kind / union case) + its
    /// fields — the key is the vocabulary's DECLARED discriminator (Phase 108).
    let objectSchema (disc: string) (typeConst: string) (fields: IdlField list) : JVal =
        let wire = wireFields fields

        let props =
            (disc, JObj [ "const", JStr typeConst ])
            :: (wire |> List.map (fun f -> f.Name, schemaOf f.Type))

        let required =
            disc :: (wire |> List.filter (fun f -> f.Opt = Required) |> List.map _.Name)

        JObj
            [ "type", JStr "object"
              "required", JArr(required |> List.map JStr)
              "properties", JObj props ]

    /// A NON-discriminated object schema — a [[TRecord]] (`FormField`, `FilterSpec`,
    /// `TabHeader`, `ColumnErased`, …). No `$type` const: that is exactly what
    /// distinguishes a record from a union case on the wire.
    let recordSchema (r: IdlRecord) : JVal =
        JObj(("type", JStr "object") :: objectBody r.Fields)

    /// Emit a Draft 2020-12 JSON Schema for the whole IDL's canonical wire.
    ///
    /// **Phase 195 — this returns a `Result` where it used to return a bare `string`**, for the
    /// reason [[fsharpTypes]] does: the leg has one construct it cannot emit — a generic union
    /// whose recursion GROWS its type argument, and so has no finite `$defs` — and answering
    /// that by throwing made a plain-string channel the one place the generator's refusal
    /// was an exception. The change is BREAKING for a caller that consumed the string directly.
    let jsonSchema (idl: Idl) : Result<string, CodegenError> =
        let enumDef (e: IdlEnum) =
            // The `enum` array is a WIRE contract — wire strings, not host case names.
            e.Name, JObj [ "type", JStr "string"; "enum", JArr(e.WireCases |> List.map JStr) ]

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
                |> List.map (fun c -> objectSchema idl.Wire.Discriminator c.Tag (fieldsOf c))

            // A transparent case is on the wire BARE — its single field's value with
            // no `$type` envelope (`TextSource.Literal`: `"x"`, not
            // `{"$type":"Literal","text":"x"}`). The codec legs already special-case
            // it; without reflecting it here the schema rejects the CANONICAL form of
            // every literal string in the corpus. The tagged branch stays: §16
            // lenient-accept admits the envelope on input.
            let bare =
                match TransparentUnion.tag idl.Harden u with
                | None -> []
                | Some ttag ->
                    u.Cases
                    |> List.tryFind (fun c -> c.Tag = ttag)
                    |> Option.map (fun c -> fieldsOf c |> List.map (fun f -> schemaOf f.Type))
                    |> Option.defaultValue []

            name, JObj [ "oneOf", JArr(bare @ tagged) ]

        let unionDef (u: IdlUnion) = unionDefWith Map.empty u.Name u

        // ── Phase 1068: the instantiations of every GENERIC union ─────────────
        //
        // A generic union has no single wire shape — `Binding<float>` and
        // `Binding<int>` accept different `Static` payloads — so it is emitted once
        // per instantiation the vocabulary actually names, and never under its bare
        // name (nothing would reference it, and its `'T` slots would be the
        // permissive `{}` that made the erasure invisible).
        //
        // The walk is a worklist over the same traversal `schemaOf` performs, so
        // the two agree by construction (see `defName`). It closes because an
        // instantiation's own case fields are substituted before being walked, and
        // the vocabulary's recursion is regular: `Binding<'T>.Local.initialFrom` is
        // `Binding<'T>` at the SAME argument (a self-reference, already visited) and
        // `Binding<'T>.Format.source` is the FIXED `Binding<float>`. A vocabulary
        // whose recursion grows its argument (`Binding<'T>` containing a
        // `Binding<'T list>`) has no finite `$defs`, and the bound below says so out
        // loud rather than hanging.
        let generics =
            idl.Unions
            |> List.filter (fun u -> not (List.isEmpty u.Params))
            |> List.map (fun u -> u.Name, u)
            |> Map.ofList

        let instantiations: Result<Map<string, string * IdlUnion * Map<string, IdlType>>, CodegenError> =
            let mutable found = Map.empty
            let mutable queue: IdlType list = []

            let rec discover (t: IdlType) =
                match t with
                | TList inner -> discover inner
                | TMap vt -> discover vt
                | TUnion(n, args) when not (List.isEmpty args) ->
                    args |> List.iter discover
                    let key = defName t

                    if not (Map.containsKey key found) then
                        match Map.tryFind n generics with
                        | None -> ()
                        | Some u when List.length u.Params <> List.length args -> ()
                        | Some u ->
                            let subst = TypeParams.bind u args |> Option.defaultValue Map.empty
                            found <- Map.add key (key, u, subst) found
                            queue <- t :: queue
                | _ -> ()

            let discoverFields (fields: IdlField list) =
                for f in wireFields fields do
                    discover f.Type

            for u in idl.Unions do
                if List.isEmpty u.Params then
                    for c in u.Cases do
                        discoverFields c.Fields

            for r in idl.Records do
                discoverFields r.Fields

            for k in idl.Kinds do
                discoverFields k.Fields

            for o in idl.Ops do
                discoverFields o.Fields

            discoverFields idl.NodeFields

            // Expand: an instantiation's own case fields can name further ones.
            let mutable guard = 0

            // Phase 195 — the bound is REPORTED, not thrown at. Overflow stops the walk and
            // becomes the leg's typed refusal below: the set is genuinely infinite, so there
            // is no partial schema worth emitting (every unreached instantiation would leave
            // a dangling `$ref`, which a strict validator treats as an error and not a skip).
            while not (List.isEmpty queue) && guard <= 1000 do
                guard <- guard + 1

                if guard <= 1000 then
                    let head = List.head queue
                    queue <- List.tail queue

                    match Map.tryFind (defName head) found with
                    | None -> ()
                    | Some(_, u, subst) ->
                        for c in u.Cases do
                            for f in wireFields c.Fields do
                                discover (TypeParams.substitute subst f.Type)

            if guard > 1000 then
                Error(
                    CodegenError.UnsupportedConstruct(
                        "a generic union whose instantiation walk did not close after 1000 expansions",
                        "GP4: a typed value, not an exception",
                        "the vocabulary's recursion grows its type argument, so it has no finite JSON-Schema `$defs` — recurse at a FIXED argument instead"
                    )
                )
            else
                Ok found

        let genericDefsR =
            instantiations
            |> Result.map (Map.toList >> List.map (fun (_, (name, u, subst)) -> unionDefWith subst name u))

        let recordDef (r: IdlRecord) = r.Name, recordSchema r

        let kindDef (k: IdlKind) =
            k.Tag, objectSchema idl.Wire.Discriminator k.Tag k.Fields

        let nodeDef =
            match idl.Wire.NodeEnvelope with
            | NodeEnvelopeShape.NestedKind ->
                "Node",
                JObj
                    [ "type", JStr "object"
                      // Phase 690 — the envelope is optional by construction (`state` /
                      // `style` / `accessibility` are omitted when empty), so `required`
                      // stays `id` + `kind` unless an envelope field is declared Required.
                      "required",
                      JArr(
                          JStr "id"
                          :: JStr "kind"
                          :: (idl.NodeFields
                              |> List.filter (fun f -> f.Opt = Required)
                              |> List.map (fun f -> JStr f.Name))
                      )
                      "properties",
                      JObj(
                          [ "id", JObj [ "type", JStr "string" ]
                            "kind", JObj [ "$ref", JStr "#/$defs/NodeKind" ] ]
                          @ (wireFields idl.NodeFields |> List.map (fun f -> f.Name, schemaOf f.Type))
                      ) ]
            // Phase 109 — the FLAT shape: a node IS a kind object also carrying its
            // `id` (and any declared envelope), which `allOf` states exactly.
            | NodeEnvelopeShape.FlatKind ->
                "Node",
                JObj
                    [ "allOf",
                      JArr
                          [ JObj [ "$ref", JStr "#/$defs/NodeKind" ]
                            JObj
                                [ "type", JStr "object"
                                  "required",
                                  JArr(
                                      JStr "id"
                                      :: (idl.NodeFields
                                          |> List.filter (fun f -> f.Opt = Required)
                                          |> List.map (fun f -> JStr f.Name))
                                  )
                                  "properties",
                                  JObj(
                                      ("id", JObj [ "type", JStr "string" ])
                                      :: (wireFields idl.NodeFields |> List.map (fun f -> f.Name, schemaOf f.Type))
                                  ) ] ] ]

        // The kind alternation, named (Phase 703). It was inlined into `Node.kind`,
        // which is equivalent for a node but leaves `TKind` — `EditNode.newKind`'s
        // type — with nothing to reference. Naming it also matches the published
        // schema, which has always carried a `NodeKind` definition.
        let nodeKindDef =
            "NodeKind",
            JObj [ "oneOf", JArr(idl.Kinds |> List.map (fun k -> JObj [ "$ref", JStr("#/$defs/" + k.Tag) ])) ]

        let opDef (o: IdlKind) =
            o.Tag, objectSchema idl.Wire.Discriminator o.Tag o.Fields

        /// The op alternation. Absent when the domain declares no ops, so an
        /// op-free IDL's schema is exactly what it was.
        let treeOpDefs =
            if List.isEmpty idl.Ops then
                []
            else
                (idl.Ops |> List.map opDef)
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

        // Records join the assembly (Phase 697). Every `TRecord` slot emits a
        // `$ref` into `#/$defs/`, so omitting them left a dangling reference for
        // `FormField` / `FilterSpec` / `TabHeader` / `ColumnErased` / … — under a
        // strict validator an unresolvable `$ref` is an error, not a permissive
        // skip, so the leg could never have certified against the corpus.
        // A generic union contributes its INSTANTIATIONS (Phase 1068), never its
        // bare erased name. An unparameterised union is unchanged.
        genericDefsR
        |> Result.map (fun genericDefs ->
            let defs =
                (idl.Enums |> List.map enumDef)
                @ (idl.Unions |> List.filter (fun u -> List.isEmpty u.Params) |> List.map unionDef)
                @ genericDefs
                @ (idl.Records |> List.map recordDef)
                @ (idl.Kinds |> List.map kindDef)
                @ [ nodeKindDef; nodeDef ]
                @ treeOpDefs

            JObj
                [ "$schema", JStr "https://json-schema.org/draft/2020-12/schema"
                  root
                  "$defs", JObj defs ]
            |> Json.render)
