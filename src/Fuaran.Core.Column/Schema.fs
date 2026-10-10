namespace Fuaran.Core

// ---- schema compatibility (Phase 33) ----

/// One change to the METADATA of a field common to both schemas (Phase 427) — what `Schema.diff`
/// reports beside a `Retyped` entry when a column's unit, label, description or an extension member
/// moved, and what `Schema.patch` replays. Each case carries the member's value `before` and `after`
/// (`None` where the field stated none), so `patch` can check it is applied to the schema it was
/// computed against. Qualified because the case names are the members' plain names.
[<RequireQualifiedAccess>]
type FieldChange =
    /// The unit moved — a change of MEANING: `Schema.classify` reads one on a depended-on column as
    /// `Breaking` when a unit was stated before (replaced or withdrawn), and as compatible when none
    /// was (a statement added where the consumer had none to rely on).
    | Unit of before: UnitOfMeasure option * after: UnitOfMeasure option
    /// The label moved — presentation, never breaking.
    | Label of before: string option * after: string option
    /// The description moved — presentation, never breaking.
    | Description of before: string option * after: string option
    /// The extension member `key` moved (`None` where the field carried no such key). Core interprets
    /// no extension, so `classify` reads one on a depended-on column as `Unknown`.
    | Ext of key: string * before: string option * after: string option

/// A structured schema delta (Phase 33): what changed between an `old` `Schema` and a `target` one —
/// columns added (in `target`, absent from `old`), removed (in `old`, absent from `target`), retyped
/// (same name, different `ColumnType`), amended (same name, different metadata — Phase 427), and
/// whether the columns common to both appear in a different relative order. A *rename* surfaces as a
/// removed+added pair (the schema carries no rename intent; a consumer that knows a rename happened
/// reads it from that pair). Nullability is not a schema-level fact in this model (it is the per-cell
/// validity mask), so it is not part of the delta.
///
/// **`Order` makes the delta a transform (Phase 317).** `Reordered` says THAT the common columns
/// moved, not WHERE to — and an added column's position is not recorded by anything else — so
/// until this field a delta could be read but not replayed. `Order` is the target's column order,
/// left EMPTY exactly when the target's order is the one `Schema.patch` derives without it: the
/// surviving columns in their old order, then the added ones in the order `Added` lists them. So
/// `diff a a` is `Schema.identityDelta` — every list empty, `Reordered` false — and
/// `Schema.patch old (Schema.diff old target) = Ok target`. `Reordered` keeps its meaning and is
/// now implied by `Order` (a reorder of the common columns is never the derived order).
type SchemaDelta =
    {
        /// Columns only the target holds, in target order, each with its metadata; `Schema.patch`
        /// appends them in this order and refuses one the schema already holds.
        Added: Field list
        /// Columns only the old schema holds, in old order, each as the OLD schema spelled it —
        /// `patch` refuses to remove a column whose type disagrees.
        Removed: Field list
        /// Columns in both whose type changed, in old order, as `(name, from, to)`; `patch` checks
        /// `from` before retyping in place, keeping the field's metadata.
        Retyped: (string * ColumnType * ColumnType) list
        /// Columns in both whose metadata changed (Phase 427), in old order, one entry per member
        /// that moved — the unit, the label, the description, then each extension key in ordinal
        /// order — each with its value before and after; `patch` checks `before` before applying
        /// `after` in place. Empty for every delta between schemas without metadata, so such a
        /// delta's wire is byte-identical to the one it had before this member existed.
        Amended: (string * FieldChange) list
        /// Whether the columns common to both schemas appear in a different relative order. A report
        /// only — `patch` never reads it; `Order` carries where they go.
        Reordered: bool
        /// The target's full column order, or empty when it is the order `patch` derives without it;
        /// a non-empty `Order` must be a permutation of the resulting columns.
        Order: string list
    }

/// Why `Schema.patch` refused a delta (Phase 317) — named and enumerated (GP5). A refusal says the
/// delta was not computed against the schema it is applied to; `patch` never repairs one.
type SchemaError =
    /// The schema names a column twice, so no delta can address its columns by name.
    | DuplicateColumn of column: string
    /// A removed, retyped or amended column the schema does not hold.
    | AbsentColumn of column: string
    /// A removed or retyped column whose type in the schema is not the one the delta records.
    | TypeDisagrees of column: string * expected: ColumnType * got: ColumnType
    /// An amended column whose metadata member in the schema is not the `before` the change records
    /// (Phase 427); `change` is the entry that did not apply.
    | MetadataDisagrees of column: string * change: FieldChange
    /// An added column the schema already holds.
    | AlreadyPresent of column: string
    /// An `Order` that is not a permutation of the columns the delta leaves: `expected` is that
    /// column set in the derived order, `got` the order the delta carries.
    | OrderMismatch of expected: string list * got: string list

/// A compatibility verdict for a schema change relative to the columns a consumer actually depends on
/// (Phase 33) — the data-strand analogue of `verifyChain` for the op-stream. Recoverable + enumerated
/// (GP5): `Breaking` / `Unknown` name their reasons.
[<RequireQualifiedAccess>]
type SchemaCompat =
    /// No depended-on column was removed, every depended-on retype is a safe widening, and no
    /// depended-on column's stated unit was replaced or withdrawn.
    | Compatible
    /// A depended-on column was removed, retyped in a way that is NOT a safe widening, or (Phase 427)
    /// had a stated unit replaced or withdrawn — its values no longer mean what the consumer read.
    | Breaking of reasons: string list
    /// A depended-on column changed in a way whose safety cannot be classified. Produced since Phase
    /// 427 for a change to an extension member of a depended-on column: Core interprets no extension,
    /// so it cannot say whether the consumer's reading survives. Reserved otherwise for future type
    /// relations — the pinned lattice classifies every retype as widening-or-breaking.
    | Unknown of reasons: string list

/// Schema-level operations (Phase 33): a structural `diff`, its replay `patch`, a depended-on-column
/// compatibility verdict, and a stable cross-host `fingerprint`. (`ModuleSuffix` so the module
/// coexists with the `Schema` type abbreviation, the same idiom as `Option`/`List`.)
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Schema =

    let private ordinal (a: string) (b: string) = System.String.CompareOrdinal(a, b)

    /// The metadata changes from `before` to `after` (two fields of one name), in member order: the
    /// unit, the label, the description, then each extension key the two carry between them, in
    /// ordinal order. Empty exactly when the two fields' metadata agree.
    let private changes (before: Field) (after: Field) : FieldChange list =
        let moved (b: 'T option) (a: 'T option) (mk: 'T option -> 'T option -> FieldChange) =
            if b = a then [] else [ mk b a ]

        let extKeys =
            (before.Ext |> Map.toList |> List.map fst)
            @ (after.Ext |> Map.toList |> List.map fst)
            |> List.distinct
            |> List.sortWith ordinal

        moved before.Unit after.Unit (fun b a -> FieldChange.Unit(b, a))
        @ moved before.Label after.Label (fun b a -> FieldChange.Label(b, a))
        @ moved before.Description after.Description (fun b a -> FieldChange.Description(b, a))
        @ (extKeys
           |> List.collect (fun k ->
               let b = Map.tryFind k before.Ext
               let a = Map.tryFind k after.Ext
               if b = a then [] else [ FieldChange.Ext(k, b, a) ]))

    /// The structured delta from `old` to `target` (total). Order-aware: `Reordered` is set when the
    /// columns common to both schemas appear in a different relative order.
    let diff (old: Schema) (target: Schema) : SchemaDelta =
        let oldMap = old |> List.map (fun f -> f.Name, f) |> Map.ofList
        let newMap = target |> List.map (fun f -> f.Name, f) |> Map.ofList

        let added = target |> List.filter (fun f -> not (Map.containsKey f.Name oldMap))
        let removed = old |> List.filter (fun f -> not (Map.containsKey f.Name newMap))

        let retyped =
            old
            |> List.choose (fun f ->
                match Map.tryFind f.Name newMap with
                | Some nf when nf.Type <> f.Type -> Some(f.Name, f.Type, nf.Type)
                | _ -> None)

        let amended =
            old
            |> List.collect (fun f ->
                match Map.tryFind f.Name newMap with
                | Some nf -> changes f nf |> List.map (fun c -> f.Name, c)
                | None -> [])

        // common columns in each schema's own order — a difference is a reorder.
        let commonOld =
            old |> List.map _.Name |> List.filter (fun n -> Map.containsKey n newMap)

        let commonNew =
            target |> List.map _.Name |> List.filter (fun n -> Map.containsKey n oldMap)

        // The order `patch` derives without being told (Phase 317): the survivors in old order,
        // then the added columns in target order. `Order` is recorded only when the target differs.
        let derived = commonOld @ (added |> List.map _.Name)
        let targetOrder = target |> List.map _.Name

        { Added = added
          Removed = removed
          Retyped = retyped
          Amended = amended
          Reordered = commonOld <> commonNew
          Order = if targetOrder = derived then [] else targetOrder }

    /// The delta that changes nothing (Phase 317): every list empty, `Reordered` false. It is what
    /// `diff a a` returns for every `a`, and `patch s identityDelta = Ok s` for every schema `s`
    /// that names no column twice.
    let identityDelta: SchemaDelta =
        { Added = []
          Removed = []
          Retyped = []
          Amended = []
          Reordered = false
          Order = [] }

    /// The field `change` leaves when applied to `f`, or `None` when `f` does not carry the `before`
    /// the change records.
    let private applyChange (change: FieldChange) (f: Field) : Field option =
        match change with
        | FieldChange.Unit(before, after) when f.Unit = before ->
            Some(
                match after with
                | Some u -> Field.withUnit u f
                | None -> Field.withoutUnit f
            )
        | FieldChange.Label(before, after) when f.Label = before ->
            Some(
                match after with
                | Some l -> Field.withLabel l f
                | None -> Field.withoutLabel f
            )
        | FieldChange.Description(before, after) when f.Description = before ->
            Some(
                match after with
                | Some d -> Field.withDescription d f
                | None -> Field.withoutDescription f
            )
        | FieldChange.Ext(key, before, after) when Map.tryFind key f.Ext = before ->
            Some(
                match after with
                | Some v -> Field.withExt key v f
                | None -> Field.withoutExt key f
            )
        | _ -> None

    /// Apply a delta to a schema (Phase 317) — the transform `diff` is the report of, so a host that
    /// records deltas beside `fingerprint` can replay them. In order: every `Removed` column must be
    /// present with the recorded type and leaves; every `Retyped` column must be present with its
    /// recorded FROM type and takes its TO type in place, its metadata kept; every `Amended` entry
    /// (Phase 427) must find its column carrying the member's `before` and sets its `after` in
    /// place; every `Added` column must be absent and is appended, in the order listed, with its
    /// metadata; then a non-empty `Order` must be a permutation of the columns that result, and is
    /// their order. Anything else is a named `SchemaError`, never a repair.
    ///
    /// **The law:** `patch old (diff old target) = Ok target` for every pair of schemas that each name
    /// no column twice — including reorders, added columns placed anywhere, and metadata that moved —
    /// and `diff a a = identityDelta`. Total.
    let patch (old: Schema) (delta: SchemaDelta) : Result<Schema, SchemaError> =
        let find (s: Schema) (name: string) =
            s |> List.tryFind (fun f -> f.Name = name)

        let replace (s: Schema) (name: string) (f: Field) =
            s |> List.map (fun g -> if g.Name = name then f else g)

        let rec removeAll (s: Schema) (xs: Field list) =
            match xs with
            | [] -> Ok s
            | r :: rest ->
                match find s r.Name with
                | None -> Error(AbsentColumn r.Name)
                | Some f when f.Type <> r.Type -> Error(TypeDisagrees(r.Name, r.Type, f.Type))
                | Some _ -> removeAll (s |> List.filter (fun f -> f.Name <> r.Name)) rest

        let rec retypeAll (s: Schema) (xs: (string * ColumnType * ColumnType) list) =
            match xs with
            | [] -> Ok s
            | (name, fromTy, toTy) :: rest ->
                match find s name with
                | None -> Error(AbsentColumn name)
                | Some f when f.Type <> fromTy -> Error(TypeDisagrees(name, fromTy, f.Type))
                | Some f -> retypeAll (replace s name (Field.withType toTy f)) rest

        let rec amendAll (s: Schema) (xs: (string * FieldChange) list) =
            match xs with
            | [] -> Ok s
            | (name, change) :: rest ->
                match find s name with
                | None -> Error(AbsentColumn name)
                | Some f ->
                    match applyChange change f with
                    | None -> Error(MetadataDisagrees(name, change))
                    | Some f' -> amendAll (replace s name f') rest

        let rec addAll (s: Schema) (xs: Field list) =
            match xs with
            | [] -> Ok s
            | a :: rest ->
                match find s a.Name with
                | Some _ -> Error(AlreadyPresent a.Name)
                | None -> addAll (s @ [ a ]) rest

        let reorder (s: Schema) =
            match delta.Order with
            | [] -> Ok s
            | order ->
                let names = s |> List.map _.Name
                let key (xs: string list) = List.sortWith ordinal xs

                if List.length order <> List.length names || key order <> key names then
                    Error(OrderMismatch(names, order))
                else
                    Ok(order |> List.map (fun n -> (find s n).Value))

        match Table.firstDuplicate (old |> List.map _.Name) with
        | Some name -> Error(DuplicateColumn name)
        | None ->
            removeAll old delta.Removed
            |> Result.bind (fun s -> retypeAll s delta.Retyped)
            |> Result.bind (fun s -> amendAll s delta.Amended)
            |> Result.bind (fun s -> addAll s delta.Added)
            |> Result.bind reorder

    let private unitText (u: UnitOfMeasure option) : string =
        match u with
        | Some u -> Unit.render u
        | None -> "none"

    /// Classify a delta against the set of columns a consumer depends on (Phase 33). A removed
    /// depended-on column is `Breaking`; a retyped depended-on column is safe iff the change is a
    /// widening (`ColumnType.widens`), else `Breaking`; a depended-on column whose stated unit was
    /// replaced or withdrawn is `Breaking` (Phase 427 — its values no longer mean what the consumer
    /// read), where a unit stated for the first time is safe (the consumer relied on no statement);
    /// a change to an extension member of a depended-on column is `Unknown` (Core interprets none).
    /// Added columns, reorderings, label and description changes, and any change to a column the
    /// consumer does NOT read are all safe. `Breaking` outranks `Unknown`. Reasons are enumerated
    /// (GP5). Total.
    let classify (dependsOn: string list) (delta: SchemaDelta) : SchemaCompat =
        let deps = Set.ofList dependsOn

        let removedDep =
            delta.Removed
            |> List.filter (fun f -> deps.Contains f.Name)
            |> List.map (fun f -> "depended-on column removed: " + f.Name + " (" + ColumnType.tag f.Type + ")")

        let badRetype =
            delta.Retyped
            |> List.filter (fun (n, ot, nt) -> deps.Contains n && not (ColumnType.widens ot nt))
            |> List.map (fun (n, ot, nt) ->
                "depended-on column "
                + n
                + " retyped "
                + ColumnType.tag ot
                + " → "
                + ColumnType.tag nt
                + " (not a safe widening)")

        let unitMoved =
            delta.Amended
            |> List.choose (fun (n, c) ->
                match c with
                | FieldChange.Unit(Some before, after) when deps.Contains n ->
                    Some(
                        "depended-on column "
                        + n
                        + " unit changed "
                        + Unit.render before
                        + " → "
                        + unitText after
                        + " (its values no longer mean what was read)"
                    )
                | _ -> None)

        let extMoved =
            delta.Amended
            |> List.choose (fun (n, c) ->
                match c with
                | FieldChange.Ext(key, _, _) when deps.Contains n ->
                    Some(
                        "depended-on column "
                        + n
                        + " extension member "
                        + key
                        + " changed (not interpreted by the kit)"
                    )
                | _ -> None)

        match removedDep @ badRetype @ unitMoved, extMoved with
        | [], [] -> SchemaCompat.Compatible
        | [], unknown -> SchemaCompat.Unknown unknown
        | reasons, _ -> SchemaCompat.Breaking reasons

    // A DELIBERATE COPY of `Hash.fnv1a` (`Fuaran.Core.Tree`), kept because `Column` references only
    // `Wire` and taking a `Tree` dependency to reach one 8-line function would add a package edge
    // for every consumer. It must stay VALUE-IDENTICAL to the canonical one, which
    // the `hashSweep/*` rows of `ParityVectors` (Fuaran.Core.Conformance) compare over a shared corpus.
    //
    // The multiply is split into 16-bit halves — see `Hash.mul32` for why a plain `h * 16777619u`
    // is not portable (the product passes 2^53 under Fable's doubles, losing precision inside the
    // operation). .NET values are unchanged by the split. Do not "simplify" it back.
    let private fnv1a (s: string) : string =
        let mutable h = 2166136261u

        for ch in s do
            h <- h ^^^ uint32 ch
            // 16777619 = 0x01000193 = 256 * 65536 + 403, so the prime's halves are 256 and 403.
            let lo = h &&& 0xFFFFu
            let hi = h >>> 16
            let cross = ((lo * 256u) + (hi * 403u)) &&& 0xFFFFu
            h <- ((lo * 403u) + (cross * 65536u)) &&& 0xFFFFFFFFu

        h.ToString("x8")

    // A DELIBERATE COPY of `Hash.canonicalFields` (`Fuaran.Core.Tree`), for the reason `fnv1a` above
    // is one: `Column` references only `Wire`. Each field has every `fieldEsc` (U+0010) and every
    // `foldSep` (U+0001) it carries escaped by a preceding `fieldEsc`, and is then terminated by
    // `foldSep` — so the pre-image is INJECTIVE, where the bare U+0001 join it replaces (Phase 299)
    // was not: a column NAME can spell the separator, and then two different schemas shared a
    // pre-image. Both symbols are written as `\u` escapes, never as the raw control byte. It must
    // stay VALUE-IDENTICAL to the canonical encoding: the suite compares `fingerprint` against
    // `Hash.fnv1a (Hash.canonicalFields …)` over names carrying the separator and the escape, and the
    // `hashSweep/*` parity rows carry it through both pipelines.
    let private foldSep = "\u0001"
    let private fieldEsc = "\u0010"

    let private canonicalPreimage (fields: string list) : string =
        fields
        |> List.map (fun s ->
            s.Replace(fieldEsc, fieldEsc + fieldEsc).Replace(foldSep, fieldEsc + foldSep)
            + foldSep)
        |> String.concat ""

    /// The pre-image element a field's METADATA contributes (Phase 427): the canonical encoding of
    /// its members in order — the unit's canonical text, the label, the description, each prefixed by
    /// a letter that says the member is stated (so an absent member and an empty one differ), then
    /// each extension key and its value in ordinal key order. Every member passes through
    /// `canonicalPreimage`, so the element ENDS WITH `foldSep` — and a column element ends with a type
    /// tag's last character, never that — which is what keeps the two kinds of element apart in the
    /// outer list and the whole pre-image injective.
    let private metadataPreimage (f: Field) : string =
        let stated (tag: string) (v: string option) =
            match v with
            | Some s -> tag + s
            | None -> ""

        [ stated "u" (f.Unit |> Option.map Unit.render)
          stated "l" f.Label
          stated "d" f.Description ]
        @ (f.Ext
           |> Map.toList
           |> List.sortWith (fun (a, _) (b, _) -> ordinal a b)
           |> List.collect (fun (k, v) -> [ k; v ]))
        |> canonicalPreimage

    /// A stable, cross-host content `fingerprint` of a `Schema` (Phase 33): FNV-1a over the canonical
    /// field encoding of the `name:type` list — the pre-image `Hash.canonicalFields` builds, so a
    /// name carrying the separator cannot make two schemas collide (Phase 299; the list was joined
    /// on a bare U+0001 until then, and every fingerprint moved with the pre-image). Order-sensitive
    /// — column order is part of a schema's identity — so a reorder changes the fingerprint. A field
    /// that states metadata (Phase 427) contributes a second element after its `name:type`, so two
    /// schemas differing only in a unit have different fingerprints, and a schema with no metadata
    /// has the fingerprint it had before this phase. Byte-identical across hosts (no host hashing
    /// primitive); the schema-version stamp a consumer's provenance records to detect "same shape"
    /// cheaply.
    let fingerprint (s: Schema) : string =
        s
        |> List.collect (fun f ->
            (f.Name + ":" + ColumnType.tag f.Type)
            :: (if Field.hasMetadata f then [ metadataPreimage f ] else []))
        |> canonicalPreimage
        |> fnv1a
