namespace Fuaran.Core

// ---- schema compatibility (Phase 33) ----

/// A structured schema delta (Phase 33): what changed between an `old` `Schema` and a `target` one —
/// columns added (in `target`, absent from `old`), removed (in `old`, absent from `target`), retyped
/// (same name, different `ColumnType`), and whether the columns common to both appear in a different
/// relative order. A *rename* surfaces as a removed+added pair (the schema carries no rename intent; a
/// consumer that knows a rename happened reads it from that pair). Nullability is not a schema-level
/// fact in this model (it is the per-cell validity mask), so it is not part of the delta.
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
        /// Columns only the target holds, in target order; `Schema.patch` appends them in this order
        /// and refuses one the schema already holds.
        Added: (string * ColumnType) list
        /// Columns only the old schema holds, in old order, each with its OLD type — `patch` refuses
        /// to remove a column whose type disagrees.
        Removed: (string * ColumnType) list
        /// Columns in both whose type changed, in old order, as `(name, from, to)`; `patch` checks
        /// `from` before retyping in place.
        Retyped: (string * ColumnType * ColumnType) list
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
    /// A removed or retyped column the schema does not hold.
    | AbsentColumn of column: string
    /// A removed or retyped column whose type in the schema is not the one the delta records.
    | TypeDisagrees of column: string * expected: ColumnType * got: ColumnType
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
    /// No depended-on column was removed, and every depended-on retype is a safe widening.
    | Compatible
    /// A depended-on column was removed, or retyped in a way that is NOT a safe widening.
    | Breaking of reasons: string list
    /// A depended-on column changed in a way whose safety cannot be classified. Reserved for future
    /// type relations — the current pinned lattice classifies every retype as widening-or-breaking, so
    /// `classify` does not currently produce it; it exists so the verdict surface is complete (GP5).
    | Unknown of reasons: string list

/// Schema-level operations (Phase 33): a structural `diff`, a depended-on-column compatibility verdict,
/// and a stable cross-host `fingerprint`. (`ModuleSuffix` so the module coexists with the `Schema` type
/// abbreviation, the same idiom as `Option`/`List`.)
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Schema =

    /// The structured delta from `old` to `target` (total). Order-aware: `Reordered` is set when the
    /// columns common to both schemas appear in a different relative order.
    let diff (old: Schema) (target: Schema) : SchemaDelta =
        let oldMap = Map.ofList old
        let newMap = Map.ofList target

        let added = target |> List.filter (fun (n, _) -> not (Map.containsKey n oldMap))
        let removed = old |> List.filter (fun (n, _) -> not (Map.containsKey n newMap))

        let retyped =
            old
            |> List.choose (fun (n, ot) ->
                match Map.tryFind n newMap with
                | Some nt when nt <> ot -> Some(n, ot, nt)
                | _ -> None)

        // common columns in each schema's own order — a difference is a reorder.
        let commonOld =
            old |> List.map fst |> List.filter (fun n -> Map.containsKey n newMap)

        let commonNew =
            target |> List.map fst |> List.filter (fun n -> Map.containsKey n oldMap)

        // The order `patch` derives without being told (Phase 317): the survivors in old order,
        // then the added columns in target order. `Order` is recorded only when the target differs.
        let derived = commonOld @ (added |> List.map fst)
        let targetOrder = target |> List.map fst

        { Added = added
          Removed = removed
          Retyped = retyped
          Reordered = commonOld <> commonNew
          Order = if targetOrder = derived then [] else targetOrder }

    /// The delta that changes nothing (Phase 317): every list empty, `Reordered` false. It is what
    /// `diff a a` returns for every `a`, and `patch s identityDelta = Ok s` for every schema `s`
    /// that names no column twice.
    let identityDelta: SchemaDelta =
        { Added = []
          Removed = []
          Retyped = []
          Reordered = false
          Order = [] }

    /// Apply a delta to a schema (Phase 317) — the transform `diff` is the report of, so a host that
    /// records deltas beside `fingerprint` can replay them. In order: every `Removed` column must be
    /// present with the recorded type and leaves; every `Retyped` column must be present with its
    /// recorded FROM type and takes its TO type in place; every `Added` column must be absent and is
    /// appended, in the order listed; then a non-empty `Order` must be a permutation of the columns
    /// that result, and is their order. Anything else is a named `SchemaError`, never a repair.
    ///
    /// **The law:** `patch old (diff old target) = Ok target` for every pair of schemas that each name
    /// no column twice — including reorders and added columns placed anywhere — and
    /// `diff a a = identityDelta`. Total.
    let patch (old: Schema) (delta: SchemaDelta) : Result<Schema, SchemaError> =
        let typeOf (s: Schema) (name: string) =
            s |> List.tryPick (fun (n, t) -> if n = name then Some t else None)

        let rec removeAll (s: Schema) (xs: (string * ColumnType) list) =
            match xs with
            | [] -> Ok s
            | (name, ty) :: rest ->
                match typeOf s name with
                | None -> Error(AbsentColumn name)
                | Some t when t <> ty -> Error(TypeDisagrees(name, ty, t))
                | Some _ -> removeAll (s |> List.filter (fun (n, _) -> n <> name)) rest

        let rec retypeAll (s: Schema) (xs: (string * ColumnType * ColumnType) list) =
            match xs with
            | [] -> Ok s
            | (name, fromTy, toTy) :: rest ->
                match typeOf s name with
                | None -> Error(AbsentColumn name)
                | Some t when t <> fromTy -> Error(TypeDisagrees(name, fromTy, t))
                | Some _ -> retypeAll (s |> List.map (fun (n, t) -> if n = name then (n, toTy) else (n, t))) rest

        let rec addAll (s: Schema) (xs: (string * ColumnType) list) =
            match xs with
            | [] -> Ok s
            | (name, ty) :: rest ->
                match typeOf s name with
                | Some _ -> Error(AlreadyPresent name)
                | None -> addAll (s @ [ name, ty ]) rest

        let reorder (s: Schema) =
            match delta.Order with
            | [] -> Ok s
            | order ->
                let names = s |> List.map fst

                let key (xs: string list) =
                    List.sortWith (fun a b -> System.String.CompareOrdinal(a, b)) xs

                if List.length order <> List.length names || key order <> key names then
                    Error(OrderMismatch(names, order))
                else
                    Ok(order |> List.map (fun n -> n, (typeOf s n).Value))

        match Table.firstDuplicate (old |> List.map fst) with
        | Some name -> Error(DuplicateColumn name)
        | None ->
            removeAll old delta.Removed
            |> Result.bind (fun s -> retypeAll s delta.Retyped)
            |> Result.bind (fun s -> addAll s delta.Added)
            |> Result.bind reorder

    /// Classify a delta against the set of columns a consumer depends on (Phase 33). A removed
    /// depended-on column is `Breaking`; a retyped depended-on column is safe iff the change is a
    /// widening (`ColumnType.widens`), else `Breaking`. Added columns, reorderings, and any change to a
    /// column the consumer does NOT read are all safe. Reasons are enumerated (GP5). Total.
    let classify (dependsOn: string list) (delta: SchemaDelta) : SchemaCompat =
        let deps = Set.ofList dependsOn

        let removedDep =
            delta.Removed
            |> List.filter (fun (n, _) -> deps.Contains n)
            |> List.map (fun (n, t) -> "depended-on column removed: " + n + " (" + ColumnType.tag t + ")")

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

        match removedDep @ badRetype with
        | [] -> SchemaCompat.Compatible
        | reasons -> SchemaCompat.Breaking reasons

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

    /// A stable, cross-host content `fingerprint` of a `Schema` (Phase 33): FNV-1a over the canonical
    /// field encoding of the `name:type` list — the pre-image `Hash.canonicalFields` builds, so a
    /// name carrying the separator cannot make two schemas collide (Phase 299; the list was joined
    /// on a bare U+0001 until then, and every fingerprint moved with the pre-image). Order-sensitive
    /// — column order is part of a schema's identity — so a reorder changes the fingerprint.
    /// Byte-identical across hosts (no host hashing primitive); the schema-version stamp a
    /// consumer's provenance records to detect "same shape" cheaply.
    let fingerprint (s: Schema) : string =
        s
        |> List.map (fun (n, t) -> n + ":" + ColumnType.tag t)
        |> canonicalPreimage
        |> fnv1a
