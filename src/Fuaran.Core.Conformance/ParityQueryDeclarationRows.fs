/// Phase 699 — the query declaration family's rows of the cross-pipeline value table
/// (`laws/query-declaration-laws.json`), and the three ASCII helpers every row of the table prints through.
///
/// A part of `ParityVectors`, divided from it at a module boundary when the table passed the source-length
/// ceiling: `ParityVectors.vectors` ends with `queryDeclarationRows`, so the table, its order and every byte are
/// what they were. Internal: the public value stays `ParityVectors.vectors`, and a consumer that compiles the kit
/// with Fable compiles this file with it.
module internal Fuaran.Core.ParityQueryDeclarationRows

open Fuaran.Core

/// Printable ASCII — the module's own promise: a console-encoding difference must never read as a
/// value divergence.
let internal printable (s: string) =
    s |> Seq.forall (fun ch -> ch >= ' ' && ch <= '~')

/// `s` itself when it is printable ASCII, else `sha:` and its SHA-256: a message or a document that
/// may carry non-ASCII text is compared by digest, never echoed.
let internal asciiOr (s: string) : string =
    if printable s then s else "sha:" + Hash.sha256Hex s

/// A decode refusal as `<code>@<path>`.
let internal refusalOf (e: DecodeError) : string =
    DecodeError.codeName e.Code + "@" + asciiOr (DecodePath.render e.Path)

// -- Phase 699: the query declaration's refusals (`laws/query-declaration-laws.json`, Phase 680) --
// The family's vectors, each through the public surface its case group names: the declaration reader
// (`QueryCodec.decodeDetailedWith ReadPolicy.Lenient`, with the admission gate last), `QueryRegistry.admit`,
// `QueryRegistry.register`, `Query.validateParams` and `QueryRegistry.dispatch`. A row compares a refusal's code
// and path, or its error document with the sentence member left out, and never a sentence: the family pins none.
// An accepted declaration is compared by the digest of the text the writer writes for it. The gate's PATHS are
// what the reader's `gate-` rows compare; `admit` answers the refusal without one.

/// The family's `sales` declaration as text, its members in name order; `None` is a member it leaves out.
let private salesMembers: (string * string option) list =
    [ "$type", Some "\"query\""
      "effect", Some """{"determinism":"network","host":"readsHost"}"""
      "id", Some "\"sales\""
      "orderBy", None
      "pageSize", Some "50"
      "params",
      Some
          """[{"name":"n","required":true,"type":"int"},{"name":"rate","required":false,"type":"float"},{"name":"tag","required":false,"type":"string"},{"name":"cap","required":false,"type":"decimal"}]"""
      "resultSchema",
      Some
          """[{"name":"n","type":"int"},{"name":"region","type":"string"},{"name":"at","type":"timestamp_ms"},{"name":"amount","type":"decimal"},{"name":"day","type":"date"},{"name":"score","type":"float"}]"""
      "source", Some """{"ref":"sales","schema":[]}"""
      "timeoutMs", None
      "where", None ]

/// `sales` with the members `replace` names given as its texts (`None`: left out).
let private salesText (replace: (string * string option) list) : string =
    salesMembers
    |> List.choose (fun (name, text) ->
        replace
        |> List.tryFind (fst >> (=) name)
        |> Option.map snd
        |> Option.defaultValue text
        |> Option.map (fun t -> "\"" + name + "\":" + t))
    |> String.concat ","
    |> fun members -> "{" + members + "}"

/// The declaration reader's answer: `accepted:` and the digest of the written declaration, or `<code>@<path>`.
let private declRead (text: string) : string =
    match QueryCodec.decodeDetailedWith ReadPolicy.Lenient text with
    | Ok q -> "accepted:" + Hash.sha256Hex (QueryCodec.encode q)
    | Error e -> refusalOf e

let private salesRead (replace: (string * string option) list) : string = declRead (salesText replace)

/// A declaration of the family's registry, as the reader reads it.
let private declared (text: string) : Query =
    match QueryCodec.decode text with
    | Ok q -> q.Declaration
    | Error e -> failwith ("a parity declaration is refused: " + e)

let private bare699 =
    declared
        """{"$type":"query","effect":{"determinism":"network","host":"readsHost"},"id":"bare","params":[],"resultSchema":[],"source":{"ref":"bare","schema":[]}}"""

let private ledger699 =
    declared
        """{"$type":"query","effect":{"determinism":"network","host":"readsHost"},"id":"ledger","params":[{"name":"n","required":true,"type":"int"}],"resultSchema":[{"name":"n","type":"int"}],"source":{"ref":"ledger","schema":[]}}"""

let private payroll699 =
    declared
        """{"$type":"query","effect":{"determinism":"network","host":"readsHost"},"id":"payroll","params":[{"name":"n","required":true,"type":"int"}],"resultSchema":[{"name":"n","type":"int"}],"source":{"ref":"payroll","schema":[]}}"""

let private sales699 = declared (salesText [])

/// The family's registry and its gate table: `ceiling` parks a read of `ledger`, denies `payroll` naming
/// `ledger`, and allows the rest.
let private registry699: QueryRegistry<unit> =
    match
        [ bare699; ledger699; payroll699; sales699 ]
        |> List.fold (fun r q -> r |> Result.bind (QueryRegistry.register q)) (Ok QueryRegistry.empty)
    with
    | Error e -> failwith ("the parity registry did not build: " + QueryCodec.encodeQueryError e)
    | Ok r ->
        r
        |> QueryRegistry.withGate
            { Policy = "ceiling"
              Decide =
                fun () (q: Query) _ ->
                    match q.Id with
                    | "ledger" -> PolicyDecision.NeedsApproval
                    | "payroll" -> PolicyDecision.denyWith "payroll is closed to this reader" [ "ledger" ]
                    | _ -> PolicyDecision.Allow }

/// A query error as its document without the sentence member (`reason`): its code and every name it carries.
let private queryErrorCode (e: QueryError) : string =
    match QuerySeamCodec.encodeQueryError e with
    | JObj members -> JObj(members |> List.filter (fun (k, _) -> k <> "reason"))
    | other -> other
    |> Canonical.write Rendering.CrossHost
    |> asciiOr

/// The admission gate's answer: `admitted:` and the digest of the written declaration, or the refusal.
let private admitRow (q: Query) : string =
    match QueryRegistry.admit q with
    | Ok a -> "admitted:" + Hash.sha256Hex (QueryCodec.encode a)
    | Error e -> "refused:" + queryErrorCode e

let private salesWhere (where: ColumnPredicate list) : string =
    admitRow { sales699 with Where = where }

let private sortKey (column: string) (direction: SortDirection) : SortKey =
    { Column = column
      Direction = direction }

/// `sales`'s parameters with `rate` declared a second time.
let private rateTwice: QueryParam list =
    sales699.Params
    @ [ { Name = "rate"
          Type = StringType
          Required = false } ]

/// `sales` reading an embedded table of `schema` and `columns`, each column held as stated.
let private salesSourced (schema: (string * ColumnType) list) (columns: Column list) : Query =
    { sales699 with
        Source =
            Embedded
                { Schema = schema |> List.map (fun (n, t) -> Field.create n t)
                  Columns = columns } }

let private intsCol (name: string) (xs: int list) =
    Column.ofInts name (Vector.ofList xs) AllValid

let private strsCol (name: string) (xs: string list) =
    Column.ofStrs name (Vector.ofList xs) AllValid

let private registerRow (q: Query) : string =
    match QueryRegistry.register q registry699 with
    | Ok _ -> "registered"
    | Error e -> "refused:" + queryErrorCode e

let private validateRow (q: Query) (args: (string * Cell) list) : string =
    match Query.validateParams q args with
    | Ok() -> "accepted"
    | Error e -> "refused:" + queryErrorCode e

/// A dispatch through the registry: `performed:` or `refused:` with how many times the resolver ran.
let private dispatchRow (grant: string option) (id: string) (args: (string * Cell) list) : string =
    let ran = ref 0

    let outcome =
        QueryRegistry.dispatch registry699 () (grant |> Option.map Grant.ofInvocation) id args (fun _ ->
            ran.Value <- ran.Value + 1
            Deferred.Pending)

    match outcome with
    | Ok _ -> "performed:" + string ran.Value
    | Error e -> "refused:" + string ran.Value + ":" + queryErrorCode e

/// The family's rows, one per vector, in the family's order: `ParityVectors.vectors` appends them after every
/// earlier row.
let internal queryDeclarationRows: (string * string) list =
    [
      // Phase 699: the declaration reader, the admission gate last, over the family's texts.
      "queryDecl/read/not-json", declRead "{"
      "queryDecl/read/a-null", salesRead [ "pageSize", Some "null" ]
      "queryDecl/read/a-number-past-the-float-range", salesRead [ "pageSize", Some "1e999" ]
      "queryDecl/read/not-an-object", declRead "[]"
      "queryDecl/read/no-type", salesRead [ "$type", None ]
      "queryDecl/read/another-type", salesRead [ "$type", Some "\"capability\"" ]
      "queryDecl/read/an-id-not-a-string", salesRead [ "id", Some "1" ]
      "queryDecl/read/the-id-before-the-params", salesRead [ "id", None; "params", Some "7" ]
      "queryDecl/read/params-not-a-list", salesRead [ "params", Some "{}" ]
      "queryDecl/read/a-parameter-without-a-name", salesRead [ "params", Some """[{"required":true,"type":"int"}]""" ]
      "queryDecl/read/a-parameter-type-unknown",
      salesRead [ "params", Some """[{"name":"n","required":true,"type":"integer"}]""" ]
      "queryDecl/read/a-parameter-required-not-a-boolean",
      salesRead [ "params", Some """[{"name":"n","required":"yes","type":"int"}]""" ]
      "queryDecl/read/no-result-schema", salesRead [ "resultSchema", None ]
      "queryDecl/read/a-result-column-unread", salesRead [ "resultSchema", Some """[{"name":"n","type":"integer"}]""" ]
      "queryDecl/read/an-effect-host-unknown",
      salesRead [ "effect", Some """{"determinism":"network","host":"everywhere"}""" ]
      "queryDecl/read/a-timeout-with-a-fraction", salesRead [ "timeoutMs", Some "1.5" ]
      "queryDecl/read/a-page-size-as-a-string", salesRead [ "pageSize", Some "\"50\"" ]
      "queryDecl/read/a-filter-not-a-list", salesRead [ "where", Some "{}" ]
      "queryDecl/read/a-predicate-tag-unknown", salesRead [ "where", Some """[{"$type":"like","column":"region"}]""" ]
      "queryDecl/read/a-literal-its-column-cannot-carry",
      salesRead [ "where", Some """[{"$type":"equalTo","column":"day","type":"date","value":"2026-02-30"}]""" ]
      "queryDecl/read/a-literal-not-of-its-stated-type",
      salesRead [ "where", Some """[{"$type":"equalTo","column":"n","type":"int","value":1.5}]""" ]
      "queryDecl/read/an-order-direction-unknown", salesRead [ "orderBy", Some """[{"column":"n","direction":"up"}]""" ]
      "queryDecl/read/the-filter-before-the-source", salesRead [ "source", None; "where", Some "7" ]
      "queryDecl/read/no-source", salesRead [ "source", None ]
      "queryDecl/read/a-source-unread", salesRead [ "source", Some "5" ]
      "queryDecl/read/the-source-before-the-gate",
      salesRead [ "source", Some "5"; "where", Some """[{"$type":"isNull","column":"nope"}]""" ]
      "queryDecl/read/gate-a-parameter-declared-twice",
      salesRead
          [ "params",
            Some
                """[{"name":"n","required":true,"type":"int"},{"name":"rate","required":false,"type":"float"},{"name":"tag","required":false,"type":"string"},{"name":"cap","required":false,"type":"decimal"},{"name":"rate","required":false,"type":"string"}]"""
            "where", Some """[{"$type":"isNull","column":"nope"}]""" ]
      "queryDecl/read/gate-an-undeclared-column", salesRead [ "where", Some """[{"$type":"isNull","column":"nope"}]""" ]
      "queryDecl/read/gate-contains-off-a-string-column",
      salesRead [ "where", Some """[{"$type":"contains","column":"n","text":"x"}]""" ]
      "queryDecl/read/gate-a-literal-of-another-type",
      salesRead [ "where", Some """[{"$type":"equalTo","column":"region","type":"int","value":1}]""" ]
      "queryDecl/read/gate-the-second-predicate",
      salesRead [ "where", Some """[{"$type":"isNull","column":"n"},{"$type":"contains","column":"n","text":"x"}]""" ]
      "queryDecl/read/gate-an-order-on-an-undeclared-column",
      salesRead [ "orderBy", Some """[{"column":"nope","direction":"ascending"}]""" ]
      "queryDecl/read/gate-an-order-naming-a-column-twice",
      salesRead
          [ "orderBy",
            Some
                """[{"column":"n","direction":"ascending"},{"column":"region","direction":"ascending"},{"column":"n","direction":"descending"}]""" ]
      "queryDecl/read/gate-the-filter-before-the-order",
      salesRead
          [ "orderBy", Some """[{"column":"nope","direction":"ascending"}]"""
            "where", Some """[{"$type":"isNull","column":"nope"}]""" ]
      "queryDecl/read/decimal-text-read-canonical",
      salesRead [ "where", Some """[{"$type":"equalTo","column":"amount","type":"decimal","value":"01.50"}]""" ]
      "queryDecl/read/admitted",
      salesRead
          [ "orderBy", Some """[{"column":"at","direction":"descending"},{"column":"n","direction":"ascending"}]"""
            "where",
            Some
                """[{"$type":"contains","column":"region","text":"eu"},{"$type":"atLeast","column":"amount","type":"decimal","value":"12.5"},{"$type":"lessThan","column":"day","type":"date","value":"2026-11-15"},{"$type":"isNotNull","column":"score"}]""" ]
      "queryDecl/read/a-timeout-of-zero", salesRead [ "timeoutMs", Some "0" ]
      "queryDecl/read/a-timeout-below-zero", salesRead [ "timeoutMs", Some "-1" ]
      "queryDecl/read/a-page-size-of-zero", salesRead [ "pageSize", Some "0" ]
      "queryDecl/read/a-page-size-below-zero", salesRead [ "pageSize", Some "-1" ]
      "queryDecl/read/budgets-of-one", salesRead [ "pageSize", Some "1"; "timeoutMs", Some "1" ]
      "queryDecl/read/no-params", salesRead [ "params", None ]
      "queryDecl/read/a-result-schema-not-a-list", salesRead [ "resultSchema", Some "{}" ]
      "queryDecl/read/no-effect", salesRead [ "effect", None ]
      "queryDecl/read/a-date-not-in-canonical-form",
      salesRead [ "where", Some """[{"$type":"equalTo","column":"day","type":"date","value":"2026-2-1"}]""" ]
      "queryDecl/read/decimal-text-with-a-trailing-zero-read-canonical",
      salesRead [ "where", Some """[{"$type":"equalTo","column":"amount","type":"decimal","value":"12.50"}]""" ]
      // Phase 699: the admission gate, over a declaration stated as data (two of its refusals no text carries).
      "queryDecl/admit/a-parameter-declared-twice",
      admitRow
          { sales699 with
              Params = rateTwice
              Where = [ ColumnPredicate.IsNull "nope" ] }
      "queryDecl/admit/an-undeclared-column", salesWhere [ ColumnPredicate.IsNull "nope" ]
      "queryDecl/admit/an-undeclared-column-of-no-columns",
      admitRow
          { bare699 with
              Where = [ ColumnPredicate.IsNotNull "n" ] }
      "queryDecl/admit/contains-on-an-undeclared-column", salesWhere [ ColumnPredicate.Contains("nope", "x") ]
      "queryDecl/admit/contains-off-a-string-column", salesWhere [ ColumnPredicate.Contains("n", "x") ]
      "queryDecl/admit/contains-on-a-string-column", salesWhere [ ColumnPredicate.Contains("region", "eu") ]
      "queryDecl/admit/a-null-literal", salesWhere [ ColumnPredicate.EqualTo("n", Null) ]
      "queryDecl/admit/a-literal-of-another-type", salesWhere [ ColumnPredicate.EqualTo("region", Int 1) ]
      "queryDecl/admit/an-integer-on-a-decimal-column", salesWhere [ ColumnPredicate.EqualTo("amount", Int 1) ]
      "queryDecl/admit/an-integer-on-a-float-column", salesWhere [ ColumnPredicate.EqualTo("score", Int 1) ]
      "queryDecl/admit/a-timestamp-coarser-than-its-column",
      salesWhere [ ColumnPredicate.EqualTo("at", Timestamp "2026-11-15T09:30:00Z") ]
      "queryDecl/admit/a-timestamp-finer-than-its-column",
      salesWhere [ ColumnPredicate.EqualTo("at", Timestamp "2026-11-15T09:30:00.000001Z") ]
      "queryDecl/admit/a-date-that-names-no-day", salesWhere [ ColumnPredicate.EqualTo("day", Date "2026-02-30") ]
      "queryDecl/admit/a-timestamp-not-canonical",
      salesWhere [ ColumnPredicate.EqualTo("at", Timestamp "2026-11-15 09:30:00Z") ]
      "queryDecl/admit/decimal-text-not-canonical", salesWhere [ ColumnPredicate.EqualTo("amount", Decimal "01.50") ]
      "queryDecl/admit/a-float-not-finite", salesWhere [ ColumnPredicate.EqualTo("score", Float nan) ]
      "queryDecl/admit/a-float-negative-infinity", salesWhere [ ColumnPredicate.EqualTo("score", Float -infinity) ]
      "queryDecl/admit/a-date-not-in-canonical-form", salesWhere [ ColumnPredicate.EqualTo("day", Date "2026-2-1") ]
      "queryDecl/admit/decimal-text-with-a-trailing-zero",
      salesWhere [ ColumnPredicate.EqualTo("amount", Decimal "12.50") ]
      "queryDecl/admit/the-second-predicate",
      salesWhere [ ColumnPredicate.IsNull "n"; ColumnPredicate.Contains("n", "x") ]
      "queryDecl/admit/an-order-on-an-undeclared-column",
      admitRow
          { sales699 with
              OrderBy = [ sortKey "nope" SortDirection.Ascending ] }
      "queryDecl/admit/an-order-naming-a-column-twice",
      admitRow
          { sales699 with
              OrderBy =
                  [ sortKey "n" SortDirection.Ascending
                    sortKey "region" SortDirection.Ascending
                    sortKey "n" SortDirection.Descending ] }
      "queryDecl/admit/the-filter-before-the-order",
      admitRow
          { sales699 with
              Where = [ ColumnPredicate.IsNull "nope" ]
              OrderBy = [ sortKey "nope" SortDirection.Ascending ] }
      "queryDecl/admit/admitted",
      admitRow
          { sales699 with
              Where =
                  [ ColumnPredicate.Contains("region", "eu")
                    ColumnPredicate.AtLeast("amount", Decimal "12.5")
                    ColumnPredicate.LessThan("day", Date "2026-11-15")
                    ColumnPredicate.IsNotNull "score" ]
              OrderBy = [ sortKey "at" SortDirection.Descending; sortKey "n" SortDirection.Ascending ] }
      "queryDecl/admit/a-source-naming-a-schema-entry-twice",
      admitRow (salesSourced [ "a", IntType; "a", IntType ] [ intsCol "a" [ 1 ] ])
      "queryDecl/admit/a-source-naming-a-column-twice",
      admitRow (salesSourced [ "a", IntType ] [ intsCol "a" [ 1 ]; intsCol "a" [ 2 ] ])
      "queryDecl/admit/a-source-schema-entry-with-no-column",
      admitRow (salesSourced [ "a", IntType; "b", StringType ] [ intsCol "a" [ 1 ] ])
      "queryDecl/admit/a-source-column-absent-from-the-schema",
      admitRow (salesSourced [ "a", IntType ] [ intsCol "a" [ 1 ]; intsCol "z" [ 9 ] ])
      "queryDecl/admit/a-source-column-of-another-type",
      admitRow (salesSourced [ "a", FloatType ] [ intsCol "a" [ 1; 2 ] ])
      "queryDecl/admit/a-ragged-source-before-a-parameter-declared-twice",
      admitRow
          { salesSourced [ "a", IntType; "b", StringType ] [ intsCol "a" [ 1; 2 ]; strsCol "b" [ "x" ] ] with
              Params = rateTwice }
      "queryDecl/admit/a-source-float-not-finite",
      admitRow (salesSourced [ "a", FloatType ] [ Column.ofFloats "a" (Vector.ofList [ nan ]) AllValid ])
      "queryDecl/admit/a-source-decimal-text-not-canonical",
      admitRow (salesSourced [ "a", DecimalType ] [ Column.ofDecimals "a" (Vector.ofList [ "12.50" ]) AllValid ])
      "queryDecl/admit/a-source-in-schema-order",
      admitRow (salesSourced [ "a", IntType; "b", StringType ] [ intsCol "a" [ 1 ]; strsCol "b" [ "x" ] ])
      "queryDecl/admit/a-source-out-of-schema-order",
      admitRow (salesSourced [ "a", IntType; "b", StringType ] [ strsCol "b" [ "x" ]; intsCol "a" [ 1 ] ])
      // Phase 699: registration into the gated registry — an id it holds is refused before the gate.
      "queryDecl/register/a-held-id", registerRow ledger699
      "queryDecl/register/a-held-id-before-the-gate",
      registerRow
          { sales699 with
              Where = [ ColumnPredicate.IsNull "nope" ] }
      "queryDecl/register/a-new-id-held-to-the-gate",
      registerRow
          { sales699 with
              Id = "sales-eu"
              Where = [ ColumnPredicate.IsNull "nope" ] }
      "queryDecl/register/a-new-id", registerRow { sales699 with Id = "sales-eu" }
      // Phase 699: argument validation, in the family's order of refusals.
      "queryDecl/validate/accepted", validateRow sales699 [ "n", Int 1 ]
      "queryDecl/validate/an-integer-fills-a-float", validateRow sales699 [ "n", Int 1; "rate", Int 2 ]
      "queryDecl/validate/an-integer-fills-a-decimal", validateRow sales699 [ "n", Int 1; "cap", Int 2 ]
      "queryDecl/validate/a-null-binds-an-optional", validateRow sales699 [ "n", Int 1; "tag", Null ]
      "queryDecl/validate/a-name-bound-twice", validateRow sales699 [ "n", Int 1; "nope", Int 1; "n", Int 2 ]
      "queryDecl/validate/a-null-then-a-value-is-twice", validateRow sales699 [ "n", Null; "n", Int 1 ]
      "queryDecl/validate/an-unknown-name-before-a-later-type", validateRow sales699 [ "nope", Int 1; "n", Str "x" ]
      "queryDecl/validate/a-type-before-a-later-unknown-name", validateRow sales699 [ "n", Str "x"; "nope", Int 1 ]
      "queryDecl/validate/a-float-for-an-integer", validateRow sales699 [ "n", Float 1.0 ]
      "queryDecl/validate/a-decimal-for-a-float", validateRow sales699 [ "n", Int 1; "rate", Decimal "1.5" ]
      "queryDecl/validate/a-required-name-left-out", validateRow sales699 [ "rate", Float 1.5 ]
      "queryDecl/validate/a-type-before-a-name-left-out", validateRow sales699 [ "rate", Str "x" ]
      "queryDecl/validate/a-required-name-bound-to-null", validateRow sales699 [ "n", Null ]
      "queryDecl/validate/a-query-with-no-parameters", validateRow bare699 [ "x", Int 1 ]
      // Phase 699: dispatch — an unregistered id whatever is presented, then validation, then the gate; a
      // deny is lifted by no grant, and a park by a grant for exactly this read only.
      "queryDecl/dispatch/unregistered", dispatchRow None "nope" []
      "queryDecl/dispatch/unregistered-with-a-grant",
      dispatchRow (Some "ledger\u0001n\u0001i\u00017\u0001") "nope" [ "n", Int 7 ]
      "queryDecl/dispatch/validated-before-the-gate", dispatchRow None "payroll" [ "n", Str "9" ]
      "queryDecl/dispatch/denied", dispatchRow None "payroll" [ "n", Int 9 ]
      "queryDecl/dispatch/denied-with-its-own-grant",
      dispatchRow (Some "payroll\u0001n\u0001i\u00019\u0001") "payroll" [ "n", Int 9 ]
      "queryDecl/dispatch/parked", dispatchRow None "ledger" [ "n", Int 7 ]
      "queryDecl/dispatch/parked-with-another-reads-grant",
      dispatchRow (Some "ledger\u0001n\u0001i\u00016\u0001") "ledger" [ "n", Int 7 ]
      "queryDecl/dispatch/granted", dispatchRow (Some "ledger\u0001n\u0001i\u00017\u0001") "ledger" [ "n", Int 7 ]
      "queryDecl/dispatch/allowed", dispatchRow None "sales" [ "n", Int 1 ] ]
