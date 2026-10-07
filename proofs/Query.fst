(*
   Query — the data-acquisition seam, the sibling of the capability seam: `Fuaran.Core.Query`'s
   default-deny registry, its parameter validation, its host-supplied resolver and its capture
   key, modelled clause for clause and proved (fuaran-core Phase 187).

   WHAT IS MODELLED. `src/Fuaran.Core.Query/Query.fs` — the two modules a dispatch crosses:

     - the DECLARATION vocabulary (`QueryParam` / `Query` / `QueryError`, with the `ColumnType`
       and `Cell` constructors and the `EffectClass` a declaration carries) and `Query`'s four
       functions: the internal `cellType`, `determinismTag`, the private `cellFields` with
       `invocationKey` over it (through `Hash.canonicalFields`, Phase 225), `validateParams` (its local `checkArgs` walk and its
       required-params step) and `invoke`;
     - (Phase 398) the declared FILTER and ORDER: `ColumnPredicate` (all eight cases),
       `SortDirection` and `SortKey` as closed types, `Query.Where` / `Query.OrderBy` as members of
       the declaration, the admission checks `QueryShape.whereFault` / `orderFault` that
       `QueryRegistry.admissionFault` runs after the repeated-parameter check, and the shape
       fields `QueryShape.keyFields` adds to the capture key's pre-image;
     - the REGISTRY: `QueryRegistry.empty` / `register` / `tryFind` / `enumerate` / `dispatch`;
     - the `Deferred<'T>` envelope the resolver answers in (Phase 198) — its three cases, and
       none of its combinators, exactly as `Capability.fst` carries it for the other seam.

   Three things are PARAMETERS rather than clauses. The RESOLVER (`Query -> Deferred<QueryResult>`)
   is an argument of `invoke` and `dispatch` and stays outside every claim: nothing here is a
   theorem about any resolver, and the result payload is an abstract type. The RENDERERS the
   capture key is written through — `string` on an int, `Canon.canonicalFloat`, `Hash.fnv1a`,
   the ordinal string order `List.sortBy fst` sorts by, and (Phase 225) `Hash.canonicalField` —
   are a `renderers` record, as Phase 177 made the three scalar readers one; a float cell
   crosses as an opaque carrier. And a declaration's `Source` is an opaque carrier too: the seam
   never reads it.

   WHAT IS PROVED, over any registry, any renderers and any resolver:

     - `unregistered_refused` — `QueryRegistry.dispatch` of an id the registry does not hold is
       the typed `NoSuchQuery id known`, and the result is the SAME under every resolver, which
       is what "runs no resolver" means for a pure function.
     - `validate_before_resolve` — an argument set `validateParams` rejects makes `invoke` return
       that rejection for every resolver alike, and through the registry too
       (`dispatch_validates_first`); a settled, pending or `ExecutionFailed` result is reachable
       only past an accepted validation (`resolver_runs_only_validated`); the envelope has no
       fourth outcome (`invoke_never_ok_failed`, `dispatch_never_ok_failed`); and validation is
       characterised EXACTLY: it accepts precisely the sets whose every binding addresses a
       declared param with a `Null` or an in-type cell, that bind every required name, and (Phase
       226) that bind none of them only to `Null` (`validate_params_exact`), and each refusal is
       truthful (`refusal_is_truthful`, `unbound_required_truthful`, `null_required_truthful`).
     - `enumerate_is_registry` — an id is enumerable exactly when `tryFind` resolves it, and
       `dispatch` raises `NoSuchQuery` exactly off the enumeration (`no_such_iff_unregistered`);
       `register` refuses a held id, extends by one entry otherwise, and keeps ids distinct.
     - `invocation_key_deterministic` — the capture key reads the declaration through its `Id`,
       its `Where` and its `OrderBy` (Phase 398; through its `Id` alone before) and the arguments
       through their name-sorted canonical form alone: two declarations sharing an id, a filter
       and an order, and two argument lists binding the same names to the same cells in ANY
       order, key identically (`invocation_key_reads_id_and_shape` is the half with no premise).
       That the key reads no resolver answer and no clock is its TYPE — it is handed neither —
       and is said here rather than dressed as a lemma. `invocation_key_unshaped` is the
       compatibility claim: a declaration whose filter and order are empty keys exactly as the
       pre-398 key did, `Id ^ "#" ^ hash (canonical args)`, so every journal keyed before stays
       readable.
     - (Phase 316) `register_refuses_duplicate_params` — the registry refuses a declaration naming
       a parameter twice, so every query a registry built by `register` holds has distinct names
       (`register_keeps_params_distinct`) and `all_null_refusal_exact`'s premise is DISCHARGED for
       every registered query (`registered_all_null_refusal_exact`) rather than assumed;
       `invocation_key_page_injective` — the PAGE key's pre-image is injective over the pair
       (argument set, page token), so distinct tokens give distinct pre-images and the first page
       keys exactly as `invocationKey` (`invocation_key_page_none`); `dispatch_page_is_dispatch`
       carries every dispatch theorem to the paged verb; and the lifecycle — `unregister` undoes a
       registration (`unregister_register`), refuses an id not held (`unregister_refuses_unheld`),
       `restrict` keeps exactly the ids kept (`restrict_members`), and `union` refuses a shared id
       and otherwise holds exactly the ids of both (`union_members`).
     - `invocation_key_injective` (Phase 225; over the shape since Phase 398) — the capture key's
       pre-image is INJECTIVE: two (filter, order, argument list) triples with one pre-image have
       the same filter, the same order, and one name-sorted argument list holding the same
       bindings, so declarations differing in filter or order, and distinct argument sets, have
       distinct pre-images; `invocation_key_page_injective` extends it to the page token. It is
       proved over a reading of a string as its symbols (`symbols_faithful`, the reading
       `Chain.fst` takes) and states what it needs of the host functions in `key_premises`
       (section 8b).
     - (Phase 398) the admission: `register` refuses a declaration whose filter names an
       undeclared column, applies `contains` to a column that is not a string, compares with a
       `Null` or with a literal of another type than its column's, or whose order names an
       undeclared column or one column twice — after the id and the repeated-parameter checks,
       first fault first (`register_refuses_shape`), and admits exactly the declarations with no
       such fault (`register_extends`).

   THE FINDINGS read off the model, each proved and asserted on the shipped seam — and both
   now CLOSED:

     - `all_null_accepted` was the first, until Phase 226 closed it: EVERY declaration, whatever
       it marks required, accepted the argument set binding each param to `Null`, because the
       only required-params step asked whether the NAME was a key of the argument map. `Required`
       now means a VALUE: a third step refuses, as the distinct `RequiredParamsNull`, a required
       param that is present but bound only to `Null` (`null_required`), so a caller can tell
       "present but null" from "missing" (`RequiredParamsUnbound`). `required_is_non_null`
       (section 9) is the positive statement that replaced the finding — acceptance is exactly
       well-typed bindings plus every required name bound to a value (`has_value`) — and
       `all_null_refusal_exact` pins what the all-`Null` set now gets: refused as
       `RequiredParamsNull`, naming every required param, whenever there is one.
     - `key_collision` was the second, until Phase 225 closed it: the canonical string joined
       `name=cellKey` pairs with NO separator, so two distinct accepted argument sets could share
       it. The lemma is gone because it is no longer true; `invocation_key_injective` is what
       replaced it, and the two exhibits it was stated on (`collision_one`, `collision_two`) stay
       below so the shipped seam's closure case can name them.

   WHAT IS NOT CLAIMED. Anything about a resolver. Anything about the five renderers beyond the
   total-order premise `invocation_key_deterministic` states for the comparator and the
   `key_premises` `invocation_key_injective` states (`query-renderers-abstract`) — in particular
   nothing about whether two distinct pre-images hash apart, and nothing about the canonical
   JSON the shape fields carry beyond `key_premises`' injectivity of `render_where` /
   `render_order` (production's `Canon.render` over the predicate and order-key documents,
   measured on the shipped codec by the declaration round trip in `ProofOracleTests`). The
   admission's LITERAL-CARRIABILITY clause — `Table.validate`'s refusal of a non-finite float or
   of decimal, date or timestamp text that is not canonical, answered `IllFormedLiteral` with its
   reason — is the column layer's grammar (`DecimalText.fst`, `WireColumn.fst`) and is not
   restated here: the model takes a present, well-typed literal as carried, and the oracle draws
   carried literals only. The resolver's two Phase 398 refusals (`PredicateNotHonoured` /
   `OrderNotHonoured`) belong to the typed resolver, which this model does not carry, as it
   carries neither the policy gate's refusals nor `UnreadableArgs`. The ORDER `enumerate` returns — production's `Map` sorts by id,
   the model holds the map as a list, and the theorem is about membership. `QueryCodec`.

   HOW TO READ IT. Every definition names its F# counterpart. The module is SELF-CONTAINED like
   `Capability.fst` — it opens nothing, restates `outcome`, `deferred` and the list helpers it
   needs, and extracts beside the other models sharing only `Prims.fs` and the `option` shim.

   Apache-2.0, like everything beside it.
*)
module Query

(* ======================================================================================
   0. The list helpers, self-contained, each naming the FSharp.Core function it stands for.
   ====================================================================================== *)

(* F#: `Result<'a, 'e>`. *)
type outcome (a e:Type) =
  | Ok    : a -> outcome a e
  | Error : e -> outcome a e

(* F#: `List.contains` on strings. *)
let rec mem (x:string) (l:list string) : Tot bool =
  match l with
  | [] -> false
  | y :: t -> x = y || mem x t

(* F#: `Map.containsKey` on `Map.ofList l` — membership of the key, whichever binding wins. *)
let rec has_key (#a:Type) (k:string) (l:list (string & a)) : Tot bool =
  match l with
  | [] -> false
  | (k', _) :: t -> k = k' || has_key k t

(* F#: `List.map fst`. *)
let rec keys (#a:Type) (l:list (string & a)) : Tot (list string) =
  match l with
  | [] -> []
  | (k, _) :: t -> k :: keys t

(* F#: `List.distinct xs = xs`, as the predicate a registry's ids satisfy. *)
let rec distinct (l:list string) : Tot bool =
  match l with
  | [] -> true
  | x :: t -> not (mem x t) && distinct t

(* Every element of `l` is in `m`. *)
let rec all_in (l m:list string) : Tot bool =
  match l with
  | [] -> true
  | x :: t -> mem x m && all_in t m

(* ======================================================================================
   1. The declaration vocabulary — `ColumnType`, `Cell`, `EffectClass`, `QueryParam`, `Query`,
      `QueryError`.
   ====================================================================================== *)

(* F#: `ColumnType`. *)
type column_type =
  | IntType
  | FloatType
  | BoolType
  | StringType
  | DateType
  | TimestampType
  | DecimalType

(* F#: `Cell`. A float crosses as an opaque carrier — the seam reads its TYPE and hands the
   carrier to the float renderer, and nothing else. A decimal's carrier is its text, which is
   what the F# cell holds too: the seam reads it as it stands, exactly as it reads a date's. The
   F# declares `Decimal` after `Null`, so that the published cases keep their tags; the order of
   the cases is nothing any clause here reads. *)
type cell =
  | Int       : int -> cell
  | Float     : string -> cell
  | Bool      : bool -> cell
  | Str       : string -> cell
  | Date      : string -> cell
  | Timestamp : string -> cell
  | Null      : cell
  | Decimal   : string -> cell

(* F#: `HostEffect`. *)
type host_effect =
  | Pure
  | ReadsHost
  | WritesHost

(* F#: `DeterminismSource` = `Set<DeterminismFactor>` (`ClockFactor | RandomFactor | NetworkFactor`),
   represented by its characteristic vector over that alphabet (Phase 319): a factor is a member
   exactly when its flag is set, so the empty set — `Deterministic` — is the all-false vector. *)
type determinism_source = { has_clock: bool; has_random: bool; has_network: bool }

(* F#: `EffectClass`. *)
type effect_class = { host: host_effect; determinism: determinism_source }

(* F#: `QueryParam`. *)
type query_param = { p_name: string; p_type: column_type; p_required: bool }

(* F#: `ColumnPredicate` (Phase 398) — one typed predicate over a result column; every case, in
   the F#'s order. A comparison's literal is a `cell`. *)
type predicate =
  | EqualTo     : column:string -> value:cell -> predicate
  | GreaterThan : column:string -> value:cell -> predicate
  | AtLeast     : column:string -> value:cell -> predicate
  | LessThan    : column:string -> value:cell -> predicate
  | AtMost      : column:string -> value:cell -> predicate
  | Contains    : column:string -> text:string -> predicate
  | IsNull      : column:string -> predicate
  | IsNotNull   : column:string -> predicate

(* F#: `SortDirection` (Phase 398). *)
type sort_direction =
  | Ascending
  | Descending

(* F#: `SortKey` (Phase 398). *)
type sort_key = { k_column: string; k_direction: sort_direction }

(* F#: `Query`. `Source` is an opaque carrier: the seam never reads it. `Where` and `OrderBy`
   (Phase 398) are the closed types above, read by the admission and by the capture key. *)
type query = {
  q_id: string;
  q_params: list query_param;
  q_schema: list (string & column_type);
  q_effect: effect_class;
  q_source: string;
  q_timeout_ms: option int;
  q_page_size: option int;
  q_where: list predicate;
  q_order_by: list sort_key
}

(* F#: `QueryError`. *)
type query_error =
  | NoSuchQuery           : id:string -> known:list string -> query_error
  | DuplicateQuery        : id:string -> query_error
  | UnknownParam          : name:string -> declared:list string -> query_error
  | ParamTypeMismatch     : name:string -> expected:column_type -> got:column_type -> query_error
  | RequiredParamsUnbound : names:list string -> query_error
  | SourceNotResolved     : source:string -> query_error
  | ExecutionFailed       : detail:string -> recoverable:list string -> query_error
  | Timeout               : query_error
  | RequiredParamsNull    : names:list string -> query_error
  | DuplicateParam        : name:string -> query_error
  (* Phase 398 — the admission's refusals of a filter or an order. *)
  | UnknownColumn         : name:string -> declared:list string -> query_error
  | PredicateTypeMismatch : column:string -> expected:column_type -> got:column_type -> query_error
  | PredicateNotApplicable : predicate:string -> column:string -> column_type:column_type -> query_error
  | IllFormedLiteral      : column:string -> reason:string -> query_error
  | DuplicateSortColumn   : column:string -> query_error

(* F#: `(string * Cell) list` — a typed invocation's args, name → bound cell. *)
type arguments = list (string & cell)

(* ======================================================================================
   2. `Query.cellType`, `determinismTag`, and the capture key — `cellFields` / `invocationKey`,
      written through the renderers the F# reaches for.
   ====================================================================================== *)

(* F#: `Query.cellType`. *)
let cell_type (c:cell) : Tot (option column_type) =
  match c with
  | Int _ -> Some IntType
  | Float _ -> Some FloatType
  | Bool _ -> Some BoolType
  | Str _ -> Some StringType
  | Date _ -> Some DateType
  | Timestamp _ -> Some TimestampType
  | Decimal _ -> Some DecimalType
  | Null -> None

(* F#: `Effect.determinismTag` — the canonical label of a set: `"deterministic"` for the empty set,
   otherwise the member factors' names in the fixed order clock, random, network, joined by `+`.
   Written as the eight labels the rendering produces. Section 1 of `Capability.fst` proves this
   table a bijection and carries the lattice over the same type; this model reads the label only. *)
let determinism_tag (d:determinism_source) : Tot string =
  if d.has_clock then
    (if d.has_random then
       (if d.has_network then "clock+random+network" else "clock+random")
     else
       (if d.has_network then "clock+network" else "clock"))
  else
    (if d.has_random then
       (if d.has_network then "random+network" else "random")
     else
       (if d.has_network then "network" else "deterministic"))

(* The label is injective: two sets with one label are one set — the capture key cannot confuse
   two determinism classes. *)
let determinism_tag_injective (a b:determinism_source)
  : Lemma (requires determinism_tag a == determinism_tag b) (ensures a == b) = ()

(* F#: `Query.determinismTag`. *)
let determinism_tag_of (q:query) : Tot string = determinism_tag q.q_effect.determinism

(* The host functions the capture key is written through. F#: `string v` on an `int`,
   `Canon.canonicalFloat`, `Hash.fnv1a`, the ordinal order `List.sortBy fst` compares names by,
   and `Hash.canonicalField` — ONE field of the canonical pre-image, escaped and terminated
   (Phase 225). The escaper is a host function here for the reason the hash is: the model's
   `string` is primitive, so what it does to a field's symbols is stated as the premise
   `field_faithful` (section 8b) rather than computed. *)
noeq type renderers = {
  render_int:   int -> string;
  render_float: string -> string;
  hash:         string -> string;
  name_le:      string -> string -> bool;
  field:        string -> string;
  (* Phase 398 — F#: `Canon.render (JArr (where |> List.map QueryShape.predicateJson))` and the
     same over `QueryShape.sortKeyJson`: the canonical text of a filter and of an order. *)
  render_where: list predicate -> string;
  render_order: list sort_key -> string
}

(* F#: `Query.cellFields`, first component — the cell's constructor as a one-letter tag. A
   `Null` is tagged `n` and carries an empty payload (Phase 225; it was the one-character
   literal `"∅"` with no payload before). *)
let cell_tag (c:cell) : Tot string =
  match c with
  | Int _ -> "i"
  | Float _ -> "f"
  | Bool _ -> "b"
  | Str _ -> "s"
  | Date _ -> "d"
  | Timestamp _ -> "t"
  | Decimal _ -> "m"
  | Null -> "n"

(* F#: `Query.cellFields`, second component — the cell's scalar rendering. *)
let cell_payload (rn:renderers) (c:cell) : Tot string =
  match c with
  | Int v -> rn.render_int v
  | Float v -> rn.render_float v
  | Bool v -> if v then "1" else "0"
  | Str v -> v
  | Date v -> v
  | Timestamp v -> v
  | Decimal v -> v
  | Null -> ""

(* F#: `List.sortBy fst`'s insertion step — a STABLE sort, so a binding lands before the first
   binding whose name is not below its own. *)
let rec insert_arg (rn:renderers) (x:(string & cell)) (l:arguments) : Tot arguments =
  match l with
  | [] -> [x]
  | y :: t ->
    let (xn, _) = x in
    let (yn, _) = y in
    if rn.name_le xn yn then x :: l else y :: insert_arg rn x t

(* F#: `args |> List.sortBy fst`. *)
let rec sort_args (rn:renderers) (l:arguments) : Tot arguments =
  match l with
  | [] -> []
  | x :: t -> insert_arg rn x (sort_args rn t)

(* F#: `List.collect (fun (n, v) -> let tag, payload = cellFields v in [ n; tag; payload ])` —
   every binding is exactly three fields, which is what makes the flattening injective. *)
let rec arg_fields (rn:renderers) (l:arguments) : Tot (list string) =
  match l with
  | [] -> []
  | (n, v) :: t -> n :: cell_tag v :: cell_payload rn v :: arg_fields rn t

(* F#: `Hash.canonicalFields` — `List.map canonicalField |> String.concat ""`. The ONE
   canonicaliser both seams build their pre-image through (Phase 225). *)
let rec fields (rn:renderers) (l:list string) : Tot string =
  match l with
  | [] -> ""
  | x :: t -> rn.field x ^ fields rn t

(* The canonical pre-image of a (name-sorted) argument list. Before Phase 225 this joined
   `name=cellKey` pairs on the empty string, and `key_collision` read a shared pre-image off two
   different argument sets; every field is now self-delimiting, and `invocation_key_injective`
   (section 8b) is the theorem that replaced the finding. *)
let canonical (rn:renderers) (l:arguments) : Tot string = fields rn (arg_fields rn l)

(* F#: `QueryShape.keyFields` (Phase 398), in front of `rest`: a non-empty filter adds the triple
   (an empty name, the tag `w`, its canonical text), a non-empty order the triple with `o`, filter
   first; an empty one adds nothing. *)
let order_fields (rn:renderers) (o:list sort_key) (rest:list string) : Tot (list string) =
  match o with
  | [] -> rest
  | _ :: _ -> "" :: "o" :: rn.render_order o :: rest

let shape_fields (rn:renderers) (w:list predicate) (o:list sort_key) (rest:list string)
  : Tot (list string) =
  match w with
  | [] -> order_fields rn o rest
  | _ :: _ -> "" :: "w" :: rn.render_where w :: order_fields rn o rest

(* F#: the pre-image `Query.invocationKey` hands `Hash.canonicalFields` — the shape fields of the
   declaration, then the (name-sorted) argument fields. *)
let key_fields (rn:renderers) (q:query) (l:arguments) : Tot (list string) =
  shape_fields rn q.q_where q.q_order_by (arg_fields rn l)

(* F#: `Hash.canonicalFields` over the key fields. *)
let canonical_key (rn:renderers) (q:query) (l:arguments) : Tot string = fields rn (key_fields rn q l)

(* F#: `Query.invocationKey`. *)
let invocation_key (rn:renderers) (q:query) (a:arguments) : Tot string =
  q.q_id ^ "#" ^ rn.hash (canonical_key rn q (sort_args rn a))

(* ======================================================================================
   3. `Query.validateParams` and `Query.invoke`.
   ====================================================================================== *)

(* F#: `q.Params |> List.tryFind (fun p -> p.Name = name)`. Named rather than a closure, per
   theorem 10's authoring lesson: one function where the F# has one lambda. *)
let rec find_param (name:string) (ps:list query_param) : Tot (option query_param) =
  match ps with
  | [] -> None
  | p :: t -> if p.p_name = name then Some p else find_param name t

(* F#: `q.Params |> List.map (fun p -> p.Name)`. *)
let rec param_names (ps:list query_param) : Tot (list string) =
  match ps with
  | [] -> []
  | p :: t -> p.p_name :: param_names t

(* F#: `ColumnType.widens` — THE widening lattice (Phase 295): the identity, or one of the two
   lossless promotions, `int` into `float` and `int` into `decimal`. A cell of type `from` fills a
   parameter of type `target` exactly when it widens; until Phase 295 the seam asked type
   equality, refusing an `int` for a `float` parameter the column codec reads it into. *)
let widens (from target:column_type) : Tot bool =
  from = target || (from = IntType && target = FloatType) || (from = IntType && target = DecimalType)

(* F#: `validateParams`'s local `checkArgs` — step 1, in the caller's arg order. *)
let rec check_args (ps:list query_param) (declared:list string) (a:arguments)
  : Tot (outcome unit query_error) (decreases a) =
  match a with
  | [] -> Ok ()
  | (name, c) :: rest ->
    match find_param name ps with
    | None -> Error (UnknownParam name declared)
    | Some p ->
      match cell_type c with
      | None -> check_args ps declared rest
      | Some t ->
        if widens t p.p_type then check_args ps declared rest
        else Error (ParamTypeMismatch name p.p_type t)

(* F#: `validateParams`'s step 2 — the required params the args leave unbound. *)
let rec unbound_required (ps:list query_param) (a:arguments) : Tot (list string) =
  match ps with
  | [] -> []
  | p :: t ->
    if p.p_required && not (has_key p.p_name a) then p.p_name :: unbound_required t a
    else unbound_required t a

(* F#: `validateParams`'s `boundToValue` — some binding gives the name a non-`Null` cell. A `Null`
   binding is absence to step 1, so it binds the name to NO value (Phase 226). *)
let rec has_value (k:string) (a:arguments) : Tot bool =
  match a with
  | [] -> false
  | (k', c) :: t -> (k = k' && not (Null? c)) || has_value k t

(* F#: `validateParams`'s step 3 (Phase 226) — the required params the args bind, but only to
   `Null`: present, and bound to no value. *)
let rec null_required (ps:list query_param) (a:arguments) : Tot (list string) =
  match ps with
  | [] -> []
  | p :: t ->
    if p.p_required && has_key p.p_name a && not (has_value p.p_name a) then
      p.p_name :: null_required t a
    else null_required t a

(* F#: `Capability.repeatedAddrs` — every name the list binds again, at each repeat, in order. *)
let rec repeated (seen:list string) (ks:list string) : Tot (list string) (decreases ks) =
  match ks with
  | [] -> []
  | k :: t -> if mem k seen then k :: repeated seen t else repeated (k :: seen) t

(* F#: `Query.validateParams` — since Phase 307 a repeated name is refused first
   (`DuplicateParam`), before any cell is read. *)
let validate_params (q:query) (a:arguments) : Tot (outcome unit query_error) =
  match repeated [] (keys a) with
  | d :: _ -> Error (DuplicateParam d)
  | [] ->
  match check_args q.q_params (param_names q.q_params) a with
  | Error e -> Error e
  | Ok () ->
    match unbound_required q.q_params a with
    | [] ->
      (match null_required q.q_params a with
       | [] -> Ok ()
       | n -> Error (RequiredParamsNull n))
    | u -> Error (RequiredParamsUnbound u)

(* F#: `Deferred<'T>` — the async-result envelope the resolver answers in (Phase 198). Restated
   rather than opened, like `outcome` above. *)
type deferred (a:Type) =
  | Pending : deferred a
  | Ready   : a -> deferred a
  | Failed  : string -> deferred a

(* F#: `Query.invoke` — validate, THEN the host resolver; a resolver failure is named, never
   thrown, and never rides out inside an `Ok`. *)
let invoke (#v:Type) (q:query) (a:arguments) (resolve:query -> deferred v)
  : Tot (outcome (deferred v) query_error) =
  match validate_params q a with
  | Error e -> Error e
  | Ok () ->
    match resolve q with
    | Ready r -> Ok (Ready r)
    | Pending -> Ok Pending
    | Failed m -> Error (ExecutionFailed m [])

(* ======================================================================================
   4. `QueryRegistry` — 177's registry shape: a `Map<string, Query>` read as a finite map.
   ====================================================================================== *)

(* F#: `QueryRegistry`. *)
type registry = { queries: list query }

(* F#: `QueryRegistry.empty`. *)
let empty : registry = { queries = [] }

(* F#: `Map.tryFind id r.Queries`. *)
let rec find_query (id:string) (qs:list query) : Tot (option query) =
  match qs with
  | [] -> None
  | q :: t -> if q.q_id = id then Some q else find_query id t

(* F#: `r.Queries |> Map.toList |> List.map fst` — the ids, as `NoSuchQuery` names them. *)
let rec ids (qs:list query) : Tot (list string) =
  match qs with
  | [] -> []
  | q :: t -> q.q_id :: ids t

(* ---- Phase 398: the admission of a filter and an order (`QueryShape.whereFault` / `orderFault`) ---- *)

(* F#: `schema |> List.tryFind (fun (n, _) -> n = c)` — a column's declared type, first match. *)
let rec schema_type (c:string) (s:list (string & column_type)) : Tot (option column_type) =
  match s with
  | [] -> None
  | (n, t) :: r -> if n = c then Some t else schema_type c r

(* F#: `QueryShape.column`. *)
let predicate_column (p:predicate) : Tot string =
  match p with
  | EqualTo c _ | GreaterThan c _ | AtLeast c _ | LessThan c _ | AtMost c _ -> c
  | Contains c _ -> c
  | IsNull c | IsNotNull c -> c

(* F#: `QueryShape.literal`. *)
let predicate_literal (p:predicate) : Tot (option cell) =
  match p with
  | EqualTo _ v | GreaterThan _ v | AtLeast _ v | LessThan _ v | AtMost _ v -> Some v
  | Contains _ _ | IsNull _ | IsNotNull _ -> None

(* F#: the sentence `whereFault` gives a `Null` literal. *)
let null_literal_reason : string = "a null literal compares with nothing; test for absence with isNull"

(* F#: `whereFault`'s local `fault`, one predicate against the schema, in its order: an undeclared
   column, `contains` on a column that is not a string, a `Null` literal, a literal of another
   type. (Its fifth clause, `Table.validate`'s carriability, is not restated: header.) *)
let predicate_fault (s:list (string & column_type)) (p:predicate) : Tot (option query_error) =
  let c = predicate_column p in
  match schema_type c s with
  | None -> Some (UnknownColumn c (keys s))
  | Some ty ->
    match p with
    | Contains _ _ -> if ty = StringType then None else Some (PredicateNotApplicable "contains" c ty)
    | _ ->
      match predicate_literal p with
      | None -> None
      | Some Null -> Some (IllFormedLiteral c null_literal_reason)
      | Some v ->
        match cell_type v with
        | Some got -> if got = ty then None else Some (PredicateTypeMismatch c ty got)
        | None -> None

(* F#: `QueryShape.whereFault` — the first predicate's fault, predicates in order. *)
let rec where_fault (s:list (string & column_type)) (w:list predicate) : Tot (option query_error) =
  match w with
  | [] -> None
  | p :: r ->
    match predicate_fault s p with
    | Some e -> Some e
    | None -> where_fault s r

(* F#: `QueryShape.orderFault` — an undeclared column, then a column already named (`seen` holds
   the columns of the keys before this one). *)
let rec order_fault (s:list (string & column_type)) (seen:list string) (o:list sort_key)
  : Tot (option query_error) (decreases o) =
  match o with
  | [] -> None
  | k :: r ->
    if not (mem k.k_column (keys s)) then Some (UnknownColumn k.k_column (keys s))
    else if mem k.k_column seen then Some (DuplicateSortColumn k.k_column)
    else order_fault s (k.k_column :: seen) r

(* F#: `QueryRegistry.admissionFault` — the repeated parameter first (Phase 316), then the filter,
   then the order (Phase 398). *)
let admission_fault (q:query) : Tot (option query_error) =
  match repeated [] (param_names q.q_params) with
  | d :: _ -> Some (DuplicateParam d)
  | [] ->
    match where_fault q.q_schema q.q_where with
    | Some e -> Some e
    | None -> order_fault q.q_schema [] q.q_order_by

(* F#: `QueryRegistry.register` — additive, no silent overwrite, and only a declaration the
   admission gate admits: the id is checked first, then `admissionFault` (a repeated parameter,
   Phase 316; a filter or an order its schema does not admit, Phase 398). *)
let register (q:query) (r:registry) : Tot (outcome registry query_error) =
  match find_query q.q_id r.queries with
  | Some _ -> Error (DuplicateQuery q.q_id)
  | None ->
    match admission_fault q with
    | Some e -> Error e
    | None -> Ok { queries = q :: r.queries }

(* F#: `QueryRegistry.tryFind`. *)
let try_find_query (id:string) (r:registry) : Tot (option query) = find_query id r.queries

(* F#: `QueryRegistry.enumerate` — the discovery surface. Production sorts by id; the model
   returns the map's entries, and the theorem below is about membership. *)
let enumerate (r:registry) : Tot (list query) = r.queries

(* F#: `QueryRegistry.dispatch` — resolve the id (default-deny), then `Query.invoke`. *)
let dispatch (#v:Type) (r:registry) (id:string) (a:arguments) (resolve:query -> deferred v)
  : Tot (outcome (deferred v) query_error) =
  match find_query id r.queries with
  | None -> Error (NoSuchQuery id (ids r.queries))
  | Some q -> invoke q a resolve

(* ======================================================================================
   5. THE FIRST THEOREM — an unregistered id is refused, and no resolver runs.
   ====================================================================================== *)

let rec find_query_mem (id:string) (qs:list query)
  : Lemma (Some? (find_query id qs) <==> mem id (ids qs))
  = match qs with
    | [] -> ()
    | _ :: t -> find_query_mem id t

(* THE FIRST THEOREM. F#: `QueryRegistry.dispatch` on an id the registry does not hold. The
   refusal is the typed `NoSuchQuery`, naming the id and every id the registry does hold — and
   the result is the same under EVERY resolver, which is what "runs no resolver" means. *)
let unregistered_refused (#v:Type) (r:registry) (id:string) (a:arguments)
                         (resolve resolve':query -> deferred v)
  : Lemma (requires not (mem id (ids (enumerate r))))
          (ensures dispatch r id a resolve == Error (NoSuchQuery id (ids r.queries)) /\
                   dispatch r id a resolve == dispatch r id a resolve')
  = find_query_mem id r.queries

(* `validateParams` refuses in four classes and no other (the fourth, `RequiredParamsNull`, since
   Phase 226). *)
let rec check_args_shape (ps:list query_param) (declared:list string) (a:arguments)
  : Lemma (ensures (match check_args ps declared a with
                    | Ok () -> True
                    | Error e -> UnknownParam? e \/ ParamTypeMismatch? e))
          (decreases a)
  = match a with
    | [] -> ()
    | (name, c) :: rest ->
      (match find_param name ps with
       | None -> ()
       | Some p ->
         (match cell_type c with
          | None -> check_args_shape ps declared rest
          | Some t -> if widens t p.p_type then check_args_shape ps declared rest else ()))

let validate_params_shape (q:query) (a:arguments)
  : Lemma (ensures (match validate_params q a with
                    | Ok () -> True
                    | Error e -> DuplicateParam? e \/ UnknownParam? e \/ ParamTypeMismatch? e \/
                                 RequiredParamsUnbound? e \/ RequiredParamsNull? e))
  = check_args_shape q.q_params (param_names q.q_params) a

(* `invoke` never produces the registry's two refusals: the classes are disjoint. *)
let invoke_never_registry_refusal (#v:Type) (q:query) (a:arguments) (resolve:query -> deferred v)
  : Lemma (ensures (match invoke q a resolve with
                    | Error (NoSuchQuery _ _) -> False
                    | Error (DuplicateQuery _) -> False
                    | _ -> True))
  = validate_params_shape q a

(* ======================================================================================
   6. THE SECOND THEOREM — validation before resolution: no resolver runs on a rejected set.
   ====================================================================================== *)

(* THE SECOND THEOREM. F#: `Query.invoke` is `validateParams |> Result.bind (resolve)`. When
   validation rejects, `invoke` IS that rejection — for every resolver alike, so none ran. *)
let validate_before_resolve (#v:Type) (q:query) (a:arguments) (resolve resolve':query -> deferred v)
  : Lemma (requires Error? (validate_params q a))
          (ensures (match validate_params q a with
                    | Error e -> invoke q a resolve == Error e
                    | Ok () -> True) /\
                   invoke q a resolve == invoke q a resolve')
  = ()

(* The registry-level restatement: a dispatch that resolved its id still runs nothing on a set
   validation rejects. *)
let dispatch_validates_first (#v:Type) (r:registry) (id:string) (a:arguments)
                             (resolve resolve':query -> deferred v)
  : Lemma (requires (match find_query id r.queries with
                     | Some q -> Error? (validate_params q a)
                     | None -> False))
          (ensures (match find_query id r.queries with
                    | Some q ->
                      (match validate_params q a with
                       | Error e -> dispatch r id a resolve == Error e
                       | Ok () -> True)
                    | None -> True) /\
                   dispatch r id a resolve == dispatch r id a resolve')
  = ()

(* The converse: a resolver ran — an accepted result, or an `ExecutionFailed` — only past an
   accepted validation. *)
let resolver_runs_only_validated (#v:Type) (q:query) (a:arguments) (resolve:query -> deferred v)
  : Lemma (ensures (match invoke q a resolve with
                    | Ok _ -> validate_params q a == Ok ()
                    | Error (ExecutionFailed _ _) -> validate_params q a == Ok ()
                    | _ -> True))
  = validate_params_shape q a

(* The envelope's fourth outcome: THERE IS NONE. A resolver's `Failed` crossed into the typed
   `ExecutionFailed`, so `Ok (Failed _)` is unreachable, for every resolver and every argument
   set alike — at the seam, and through the registry where a host reaches it. This is the claim
   `queryLaws` samples; here it is over all of them. *)
let invoke_never_ok_failed (#v:Type) (q:query) (a:arguments) (resolve:query -> deferred v)
  : Lemma (ensures (match invoke q a resolve with
                    | Ok (Failed _) -> False
                    | _ -> True))
  = ()

let dispatch_never_ok_failed (#v:Type) (r:registry) (id:string) (a:arguments)
                             (resolve:query -> deferred v)
  : Lemma (ensures (match dispatch r id a resolve with
                    | Ok (Failed _) -> False
                    | _ -> True))
  = match find_query id r.queries with
    | None -> ()
    | Some q -> invoke_never_ok_failed q a resolve

(* What an accepted binding looks like: it addresses a declared param, and its cell is `Null`
   or of a type that widens to that param's type (Phase 295). *)
let well_typed (ps:list query_param) (b:(string & cell)) : Tot bool =
  let (name, c) = b in
  match find_param name ps with
  | None -> false
  | Some p ->
    (match cell_type c with
     | None -> true
     | Some t -> widens t p.p_type)

let rec all_well_typed (ps:list query_param) (a:arguments) : Tot bool =
  match a with
  | [] -> true
  | b :: t -> well_typed ps b && all_well_typed ps t

let rec check_args_exact (ps:list query_param) (declared:list string) (a:arguments)
  : Lemma (ensures (check_args ps declared a == Ok () <==> all_well_typed ps a))
          (decreases a)
  = match a with
    | [] -> ()
    | _ :: rest -> check_args_exact ps declared rest

(* Validation, characterised EXACTLY: accepted precisely when every binding is well typed against
   the declaration, no required name is left unbound, and (Phase 226) none is bound only to `Null`. *)
let validate_params_exact (q:query) (a:arguments)
  : Lemma (validate_params q a == Ok () <==>
           (repeated [] (keys a) == [] /\ all_well_typed q.q_params a /\
            unbound_required q.q_params a == [] /\ null_required q.q_params a == []))
  = check_args_exact q.q_params (param_names q.q_params) a

(* What a REFUSAL guarantees: an `UnknownParam` names a bound name no param declares, and lists
   the declared names; a `ParamTypeMismatch` names a declared param, its declared type, and a
   type the bound cell really has that does NOT widen to it (Phase 295). *)
let rec refusal_is_truthful (ps:list query_param) (declared:list string) (a:arguments)
  : Lemma (ensures (match check_args ps declared a with
                    | Error (UnknownParam name d) ->
                      None? (find_param name ps) /\ d == declared /\ has_key name a
                    | Error (ParamTypeMismatch name expected got) ->
                      has_key name a /\ expected <> got /\ not (widens got expected) /\
                      (match find_param name ps with
                       | Some p -> p.p_type == expected
                       | None -> False)
                    | _ -> True))
          (decreases a)
  = match a with
    | [] -> ()
    | (name, c) :: rest ->
      (match find_param name ps with
       | None -> ()
       | Some p ->
         (match cell_type c with
          | None -> refusal_is_truthful ps declared rest
          | Some t -> if widens t p.p_type then refusal_is_truthful ps declared rest else ()))

(* And a `RequiredParamsUnbound` names only declared names the args really leave unbound. *)
let rec unbound_required_truthful (ps:list query_param) (a:arguments) (n:string)
  : Lemma (requires mem n (unbound_required ps a))
          (ensures not (has_key n a) /\ mem n (param_names ps))
  = match ps with
    | [] -> ()
    | p :: t ->
      if p.p_required && not (has_key p.p_name a) then
        (if n = p.p_name then () else unbound_required_truthful t a n)
      else unbound_required_truthful t a n

(* And a `RequiredParamsNull` names only declared names the args bind, and bind to no value. *)
let rec null_required_truthful (ps:list query_param) (a:arguments) (n:string)
  : Lemma (requires mem n (null_required ps a))
          (ensures has_key n a /\ not (has_value n a) /\ mem n (param_names ps))
  = match ps with
    | [] -> ()
    | p :: t ->
      if p.p_required && has_key p.p_name a && not (has_value p.p_name a) then
        (if n = p.p_name then () else null_required_truthful t a n)
      else null_required_truthful t a n

(* ======================================================================================
   7. THE THIRD THEOREM — what is enumerable is exactly what is dispatchable.
   ====================================================================================== *)

(* THE THIRD THEOREM. F#: `QueryRegistry.enumerate` and `tryFind` read the same map. An id is in
   the enumeration exactly when `tryFind` resolves it — no hidden entry, no phantom one. *)
let enumerate_is_registry (r:registry) (id:string)
  : Lemma (mem id (ids (enumerate r)) <==> Some? (try_find_query id r))
  = find_query_mem id r.queries

(* And `dispatch` agrees: the `NoSuchQuery` refusal is raised exactly off the enumeration. *)
let no_such_iff_unregistered (#v:Type) (r:registry) (id:string) (a:arguments)
                             (resolve:query -> deferred v)
  : Lemma ((match dispatch r id a resolve with
            | Error (NoSuchQuery _ _) -> True
            | _ -> False) <==> not (mem id (ids (enumerate r))))
  = find_query_mem id r.queries;
    (match find_query id r.queries with
     | None -> ()
     | Some q -> invoke_never_registry_refusal q a resolve)

let rec find_query_id (id:string) (qs:list query)
  : Lemma (ensures (match find_query id qs with
                    | Some q -> q.q_id == id
                    | None -> True))
  = match qs with
    | [] -> ()
    | _ :: t -> find_query_id id t

(* A resolved id dispatches to ITS declaration's `invoke` — the entry enumeration showed. *)
let registered_dispatches (#v:Type) (r:registry) (id:string) (a:arguments)
                          (resolve:query -> deferred v)
  : Lemma (requires mem id (ids (enumerate r)))
          (ensures (match try_find_query id r with
                    | Some q -> q.q_id == id /\ dispatch r id a resolve == invoke q a resolve
                    | None -> False))
  = find_query_mem id r.queries; find_query_id id r.queries

(* `register` refuses a held id and extends by exactly one entry otherwise. *)
let register_refuses_duplicate (q:query) (r:registry)
  : Lemma (requires mem q.q_id (ids (enumerate r)))
          (ensures register q r == Error (DuplicateQuery q.q_id))
  = find_query_mem q.q_id r.queries

let register_extends (q:query) (r:registry)
  : Lemma (requires not (mem q.q_id (ids (enumerate r))) /\ admission_fault q == None)
          (ensures register q r == Ok { queries = q :: r.queries } /\
                   (forall (id:string). mem id (ids (q :: r.queries)) <==> (id = q.q_id \/ mem id (ids (enumerate r)))))
  = find_query_mem q.q_id r.queries

(* Phase 398 — and refuses, with the admission's first fault, a fresh declaration the gate does not
   admit: a repeated parameter, then a filter, then an order its schema does not admit. *)
let register_refuses_shape (q:query) (r:registry)
  : Lemma (requires not (mem q.q_id (ids (enumerate r))) /\ Some? (admission_fault q))
          (ensures register q r == Error (Some?.v (admission_fault q)))
  = find_query_mem q.q_id r.queries

(* A registry built by `register` holds distinct ids — so `find_query` is a function of the id,
   and an id names ONE declaration, which is what makes a key's `Id` prefix name one query. *)
let register_keeps_distinct (q:query) (r r':registry)
  : Lemma (requires distinct (ids (enumerate r)) /\ register q r == Ok r')
          (ensures distinct (ids (enumerate r')))
  = find_query_mem q.q_id r.queries

let empty_distinct () : Lemma (distinct (ids (enumerate empty))) = ()

(* ======================================================================================
   8. THE FOURTH THEOREM — the capture key is a function of the id and the canonical args.
   ====================================================================================== *)

(* The comparator premise: `name_le` is a total order. True of the ordinal order production
   sorts by; a parameter here, so a premise here (`query-renderers-abstract`). *)
let total_order (le:string -> string -> bool) : Tot prop =
  (forall (x y:string). le x y \/ le y x) /\
  (forall (x y:string). (le x y /\ le y x) ==> x == y) /\
  (forall (x y z:string). (le x y /\ le y z) ==> le x z)

(* Decidable membership of a binding — `cell` has decidable equality. *)
let rec mem_arg (x:(string & cell)) (l:arguments) : Tot bool =
  match l with
  | [] -> false
  | y :: t -> x = y || mem_arg x t

(* F#: the result of `List.sortBy fst` is ordered by name. *)
let rec sorted (le:string -> string -> bool) (l:arguments) : Tot bool =
  match l with
  | [] -> true
  | x :: tl ->
    (match tl with
     | [] -> true
     | y :: _ -> le (fst x) (fst y) && sorted le tl)

let rec insert_mem (rn:renderers) (x:(string & cell)) (l:arguments) (y:(string & cell))
  : Lemma (mem_arg y (insert_arg rn x l) = (y = x || mem_arg y l))
  = match l with
    | [] -> ()
    | _ :: t -> insert_mem rn x t y

let rec sort_mem (rn:renderers) (l:arguments) (y:(string & cell))
  : Lemma (mem_arg y (sort_args rn l) = mem_arg y l)
  = match l with
    | [] -> ()
    | x :: t -> sort_mem rn t y; insert_mem rn x (sort_args rn t) y

let rec insert_keys (rn:renderers) (x:(string & cell)) (l:arguments) (n:string)
  : Lemma (mem n (keys (insert_arg rn x l)) = (n = fst x || mem n (keys l)))
  = match l with
    | [] -> ()
    | _ :: t -> insert_keys rn x t n

let rec insert_distinct (rn:renderers) (x:(string & cell)) (l:arguments)
  : Lemma (requires distinct (keys l) /\ not (mem (fst x) (keys l)))
          (ensures distinct (keys (insert_arg rn x l)))
  = match l with
    | [] -> ()
    | y :: t ->
      if rn.name_le (fst x) (fst y) then ()
      else (insert_distinct rn x t; insert_keys rn x t (fst y))

let rec sort_keys (rn:renderers) (l:arguments) (n:string)
  : Lemma (mem n (keys (sort_args rn l)) = mem n (keys l))
  = match l with
    | [] -> ()
    | x :: t -> sort_keys rn t n; insert_keys rn x (sort_args rn t) n

let rec sort_distinct (rn:renderers) (l:arguments)
  : Lemma (requires distinct (keys l))
          (ensures distinct (keys (sort_args rn l)))
  = match l with
    | [] -> ()
    | x :: t ->
      sort_distinct rn t;
      sort_keys rn t (fst x);
      insert_distinct rn x (sort_args rn t)

let rec insert_sorted (rn:renderers) (x:(string & cell)) (l:arguments)
  : Lemma (requires total_order rn.name_le /\ sorted rn.name_le l)
          (ensures sorted rn.name_le (insert_arg rn x l))
  = match l with
    | [] -> ()
    | y :: t ->
      if rn.name_le (fst x) (fst y) then ()
      else insert_sorted rn x t

let rec sort_sorted (rn:renderers) (l:arguments)
  : Lemma (requires total_order rn.name_le)
          (ensures sorted rn.name_le (sort_args rn l))
  = match l with
    | [] -> ()
    | x :: t -> sort_sorted rn t; insert_sorted rn x (sort_args rn t)

(* In a sorted list the head is below every binding after it. *)
let rec sorted_head_le (le:string -> string -> bool) (x:(string & cell)) (t:arguments) (y:(string & cell))
  : Lemma (requires total_order le /\ sorted le (x :: t) /\ mem_arg y t)
          (ensures le (fst x) (fst y))
          (decreases t)
  = match t with
    | [] -> ()
    | h :: t' -> if y = h then () else sorted_head_le le h t' y

let rec mem_arg_key (x:(string & cell)) (l:arguments)
  : Lemma (requires mem_arg x l)
          (ensures mem (fst x) (keys l))
  = match l with
    | [] -> ()
    | y :: t -> if x = y then () else mem_arg_key x t

(* Two argument lists bind the same names to the same cells. *)
let same_bindings (a a':arguments) : Tot prop =
  forall (x:(string & cell)). mem_arg x a = mem_arg x a'

(* A name-sorted list with distinct names is determined by its bindings: two of them holding
   the same bindings are the same list. *)
let rec sorted_unique (le:string -> string -> bool) (s1 s2:arguments)
  : Lemma (requires total_order le /\ sorted le s1 /\ sorted le s2 /\
                    distinct (keys s1) /\ distinct (keys s2) /\ same_bindings s1 s2)
          (ensures s1 == s2)
          (decreases s1)
  = match s1, s2 with
    | [], [] -> ()
    | [], h :: _ -> assert (mem_arg h s1 = mem_arg h s2)
    | h :: _, [] -> assert (mem_arg h s1 = mem_arg h s2)
    | h1 :: t1, h2 :: t2 ->
      assert (mem_arg h1 s1 = mem_arg h1 s2);
      assert (mem_arg h2 s1 = mem_arg h2 s2);
      if h1 = h2 then begin
        let aux (x:(string & cell)) : Lemma (mem_arg x t1 = mem_arg x t2) =
          assert (mem_arg x s1 = mem_arg x s2);
          if x = h1 then begin
            (if mem_arg x t1 then mem_arg_key x t1 else ());
            (if mem_arg x t2 then mem_arg_key x t2 else ())
          end else ()
        in
        FStar.Classical.forall_intro aux;
        sorted_unique le t1 t2
      end else begin
        (* h1 is after h2 in s2, and h2 is after h1 in s1: the names are equal by antisymmetry,
           which the distinct names of s2 refuse. *)
        sorted_head_le le h2 t2 h1;
        sorted_head_le le h1 t1 h2;
        mem_arg_key h1 t2
      end

(* THE FOURTH THEOREM. F#: `Query.invocationKey`. The key reads the declaration through its id,
   its filter and its order (Phase 398 — through its id alone before), and the arguments through
   their name-sorted canonical form alone: two declarations sharing an id, a `Where` and an
   `OrderBy`, and two argument lists binding the same distinct names to the same cells in any
   order, key identically — whatever else the declarations say (parameters, schema, effect,
   source, timeout, page size), and whatever order the caller happened to write the bindings in. *)
let invocation_key_deterministic (rn:renderers) (q q':query) (a a':arguments)
  : Lemma (requires total_order rn.name_le /\ q.q_id == q'.q_id /\
                    q.q_where == q'.q_where /\ q.q_order_by == q'.q_order_by /\
                    distinct (keys a) /\ distinct (keys a') /\ same_bindings a a')
          (ensures invocation_key rn q a == invocation_key rn q' a')
  = let aux (x:(string & cell)) : Lemma (mem_arg x (sort_args rn a) = mem_arg x (sort_args rn a')) =
      sort_mem rn a x; sort_mem rn a' x;
      assert (mem_arg x a = mem_arg x a')
    in
    FStar.Classical.forall_intro aux;
    sort_sorted rn a; sort_sorted rn a';
    sort_distinct rn a; sort_distinct rn a';
    sorted_unique rn.name_le (sort_args rn a) (sort_args rn a')

(* Phase 307 — `repeated` finds nothing exactly over a list with distinct members none seen. *)
let rec repeated_none (seen:list string) (ks:list string)
  : Lemma (requires repeated seen ks == [])
          (ensures distinct ks /\ (forall (k:string). mem k ks ==> not (mem k seen)))
          (decreases ks)
  = match ks with
    | [] -> ()
    | k :: t -> repeated_none (k :: seen) t

(* Phase 307 — the hypothesis is a fact. F#: `validateParams` refuses a repeated name
   (`DuplicateParam`), so an ACCEPTED argument list has distinct names, and two accepted lists
   binding the same names to the same cells key identically, in any order. Before Phase 307 the
   theorem above carried `distinct (keys a)` as a premise nothing enforced: `[a = 1; a = Null]` was
   accepted for a required `a`, and its key depended on the order of the list. *)
let validate_params_distinct (q:query) (a:arguments)
  : Lemma (requires validate_params q a == Ok ())
          (ensures distinct (keys a))
  = match repeated [] (keys a) with
    | [] -> repeated_none [] (keys a)
    | _ -> ()

let accepted_invocation_key_deterministic (rn:renderers) (q:query) (a a':arguments)
  : Lemma (requires total_order rn.name_le /\ validate_params q a == Ok () /\
                    validate_params q a' == Ok () /\ same_bindings a a')
          (ensures invocation_key rn q a == invocation_key rn q a')
  = validate_params_distinct q a; validate_params_distinct q a';
    invocation_key_deterministic rn q q a a'

(* The declaration half on its own, with no premise at all: the key reads a declaration through
   its id, its filter and its order, and nothing else of it. *)
let invocation_key_reads_id_and_shape (rn:renderers) (q q':query) (a:arguments)
  : Lemma (requires q.q_id == q'.q_id /\ q.q_where == q'.q_where /\ q.q_order_by == q'.q_order_by)
          (ensures invocation_key rn q a == invocation_key rn q' a)
  = ()

(* THE COMPATIBILITY CLAIM (Phase 398). F#: a declaration whose `Where` and `OrderBy` are empty
   keys exactly as `Query.invocationKey` did before either existed — the id, `#`, and the hash of
   the arguments' canonical string alone — so every journal keyed before Phase 398 still replays. *)
let invocation_key_unshaped (rn:renderers) (q:query) (a:arguments)
  : Lemma (requires q.q_where == [] /\ q.q_order_by == [])
          (ensures invocation_key rn q a == q.q_id ^ "#" ^ rn.hash (canonical rn (sort_args rn a)))
  = ()

(* ======================================================================================
   8b. THE FIFTH THEOREM (Phase 225) — the capture key's pre-image is INJECTIVE: two argument
       lists with one canonical string are one list, so distinct argument sets have distinct
       pre-images. It replaced `key_collision`, the finding that the pre-image joined
       `name=cellKey` pairs on the empty string.
   ====================================================================================== *)

(* HOW A STRING IS READ. F*'s `string` is primitive and `^` is opaque, so no argument about
   where one field ends is possible without a reading of a string as its symbols — the same
   reading `Chain.fst` takes for its splices, restated here because this module opens nothing.
   `symbols_faithful reveal` says two things true of `System.String` by construction:
   concatenation is symbol-list append, and two strings with the same symbols are one string.
   It is a HYPOTHESIS in the `requires` of the lemmas that spend it, never an `assume`. *)
[@@ noextract_to "FSharp"]
let rec app (#a:Type) (x y:list a) : Tot (list a) =
  match x with
  | [] -> y
  | h :: t -> h :: app t y

[@@ noextract_to "FSharp"]
let symbols_faithful (#sym:eqtype) (reveal:string -> list sym) : prop =
  (forall (s t:string). reveal (s ^ t) == app (reveal s) (reveal t)) /\
  (forall (s t:string). reveal s == reveal t ==> s == t)

(* F#: `Hash.canonicalField`'s escaper, at the symbol level. The escape symbol `e` (U+0010, DLE)
   is written before every `e` and every terminator `t` (U+0001, `Hash.foldSep`) the field
   carries; every other symbol passes through. *)
[@@ noextract_to "FSharp"]
let rec esc (#sym:eqtype) (e t:sym) (l:list sym) : Tot (list sym) =
  match l with
  | [] -> []
  | x :: r -> if x = e || x = t then e :: x :: esc e t r else x :: esc e t r

(* F#: `Hash.canonicalFields` at the symbol level — each field escaped, then terminated. *)
[@@ noextract_to "FSharp"]
let rec enc (#sym:eqtype) (e t:sym) (l:list (list sym)) : Tot (list sym) =
  match l with
  | [] -> []
  | x :: r -> app (esc e t x) (t :: enc e t r)

(* THE ESCAPER PREMISE: production's `Hash.canonicalField` is `esc` then one terminator, read as
   symbols. A statement about a host function, like the hash's — and measured on the shipped
   seam by the decoder round trip in `ProofOracleTests`, over fields carrying both symbols. *)
[@@ noextract_to "FSharp"]
let field_faithful (#sym:eqtype) (reveal:string -> list sym) (e t:sym) (field:string -> string)
  : prop =
  forall (s:string). reveal (field s) == app (esc e t (reveal s)) [t]

[@@ noextract_to "FSharp"]
let injective (#a:eqtype) (f:a -> string) : prop = forall (x y:a). f x == f y ==> x == y

(* Every premise the injectivity theorem spends, named once. The two renderer clauses are the
   only claims about a numeral layout, and they are claims about the CARRIERS the model's cells
   hold (an int, a float's round-trip text), not about IEEE equality. *)
[@@ noextract_to "FSharp"]
let key_premises (#sym:eqtype) (reveal:string -> list sym) (e t:sym) (rn:renderers) : prop =
  symbols_faithful reveal /\ reveal "" == [] /\ ~(e == t) /\ field_faithful reveal e t rn.field /\
  injective rn.render_int /\ injective rn.render_float /\
  injective rn.render_where /\ injective rn.render_order

(* ---- the symbol level ---- *)

let app_cons_nonempty (#a:Type) (y:list a) (h:a) (r:list a)
  : Lemma (Cons? (app y (h :: r))) =
  match y with
  | [] -> ()
  | _ :: _ -> ()

let rec app_assoc (#a:Type) (x y z:list a)
  : Lemma (app (app x y) z == app x (app y z)) =
  match x with
  | [] -> ()
  | _ :: t -> app_assoc t y z

(* Two escaped fields, each followed by the terminator: the split point is forced, because the
   first UNESCAPED terminator is the end of each field. *)
let rec esc_split (#sym:eqtype) (e t:sym) (x r1 y r2:list sym)
  : Lemma (requires ~(e == t) /\ app (esc e t x) (t :: r1) == app (esc e t y) (t :: r2))
          (ensures x == y /\ r1 == r2) (decreases x) =
  match x, y with
  | [], [] -> ()
  | [], _ :: _ -> ()
  | _ :: _, [] -> ()
  | _ :: xt, _ :: yt -> esc_split e t xt r1 yt r2

let rec enc_injective (#sym:eqtype) (e t:sym) (l1 l2:list (list sym))
  : Lemma (requires ~(e == t) /\ enc e t l1 == enc e t l2) (ensures l1 == l2) =
  match l1, l2 with
  | [], [] -> ()
  | [], y :: q -> app_cons_nonempty (esc e t y) t (enc e t q)
  | x :: r, [] -> app_cons_nonempty (esc e t x) t (enc e t r)
  | x :: r, y :: q ->
    esc_split e t x (enc e t r) y (enc e t q);
    enc_injective e t r q

(* ---- lifting it to the strings production concatenates ---- *)

[@@ noextract_to "FSharp"]
let rec symbols (#sym:eqtype) (reveal:string -> list sym) (l:list string)
  : Tot (list (list sym)) =
  match l with
  | [] -> []
  | x :: t -> reveal x :: symbols reveal t

let rec symbols_injective (#sym:eqtype) (reveal:string -> list sym) (l1 l2:list string)
  : Lemma (requires symbols_faithful reveal /\ symbols reveal l1 == symbols reveal l2)
          (ensures l1 == l2) =
  match l1, l2 with
  | [], [] -> ()
  | [], _ :: _ -> ()
  | _ :: _, [] -> ()
  | _ :: t1, _ :: t2 -> symbols_injective reveal t1 t2

let rec reveal_fields (#sym:eqtype) (reveal:string -> list sym) (e t:sym) (rn:renderers)
  (l:list string)
  : Lemma (requires symbols_faithful reveal /\ reveal "" == [] /\ field_faithful reveal e t rn.field)
          (ensures reveal (fields rn l) == enc e t (symbols reveal l)) =
  match l with
  | [] -> ()
  | x :: r ->
    reveal_fields reveal e t rn r;
    app_assoc (esc e t (reveal x)) [t] (reveal (fields rn r))

(* `Hash.canonicalFields` is injective on field lists. Proved, not assumed. *)
let fields_injective (#sym:eqtype) (reveal:string -> list sym) (e t:sym) (rn:renderers)
  (l1 l2:list string)
  : Lemma (requires key_premises reveal e t rn /\ fields rn l1 == fields rn l2)
          (ensures l1 == l2) =
  reveal_fields reveal e t rn l1;
  reveal_fields reveal e t rn l2;
  enc_injective e t (symbols reveal l1) (symbols reveal l2);
  symbols_injective reveal l1 l2

(* A cell's tag and payload determine the cell. *)
let cell_fields_injective (rn:renderers) (c c':cell)
  : Lemma (requires injective rn.render_int /\ injective rn.render_float /\
                    cell_tag c == cell_tag c' /\ cell_payload rn c == cell_payload rn c')
          (ensures c == c') = ()

(* Three fields per binding, so the flattening is injective. *)
let rec arg_fields_injective (rn:renderers) (a1 a2:arguments)
  : Lemma (requires injective rn.render_int /\ injective rn.render_float /\
                    arg_fields rn a1 == arg_fields rn a2)
          (ensures a1 == a2) =
  match a1, a2 with
  | [], [] -> ()
  | [], _ :: _ -> ()
  | _ :: _, [] -> ()
  | (_, v1) :: t1, (_, v2) :: t2 ->
    cell_fields_injective rn v1 v2;
    arg_fields_injective rn t1 t2

(* The canonical string is injective on argument lists — any lists, sorted or not. *)
let canonical_injective (#sym:eqtype) (reveal:string -> list sym) (e t:sym) (rn:renderers)
  (a1 a2:arguments)
  : Lemma (requires key_premises reveal e t rn /\ canonical rn a1 == canonical rn a2)
          (ensures a1 == a2) =
  fields_injective reveal e t rn (arg_fields rn a1) (arg_fields rn a2);
  arg_fields_injective rn a1 a2

(* ---- Phase 398: the shape fields in front of the bindings ---- *)

(* The four renderer injectivities the field-list argument spends — the cell carriers' and the
   shape's canonical texts'. *)
[@@ noextract_to "FSharp"]
let shape_premises (rn:renderers) : prop =
  injective rn.render_int /\ injective rn.render_float /\
  injective rn.render_where /\ injective rn.render_order

(* No cell's tag is a shape tag or the page tag — what keeps a `w`, `o` or `p` triple from reading
   as a binding. *)
let cell_tag_not_shape (c:cell) : Lemma (cell_tag c <> "w" /\ cell_tag c <> "o" /\ cell_tag c <> "p") = ()

(* So the argument fields never open with one: their second field, when they have one, is a tag. *)
let arg_fields_not_shape (rn:renderers) (l:arguments) (x:string) (r:list string)
  : Lemma (requires (x == "w" \/ x == "o" \/ x == "p") /\ arg_fields rn l == "" :: x :: r)
          (ensures False)
  = match l with
    | [] -> ()
    | (_, v) :: _ -> cell_tag_not_shape v

(* The order triple and the bindings after it determine the order and the bindings. *)
let order_fields_injective (rn:renderers) (o1 o2:list sort_key) (a1 a2:arguments)
  : Lemma (requires shape_premises rn /\
                    order_fields rn o1 (arg_fields rn a1) == order_fields rn o2 (arg_fields rn a2))
          (ensures o1 == o2 /\ a1 == a2)
  = match o1, o2 with
    | [], [] -> arg_fields_injective rn a1 a2
    | _ :: _, _ :: _ -> arg_fields_injective rn a1 a2
    | [], _ :: _ -> arg_fields_not_shape rn a1 "o" (rn.render_order o2 :: arg_fields rn a2)
    | _ :: _, [] -> arg_fields_not_shape rn a2 "o" (rn.render_order o1 :: arg_fields rn a1)

(* Fields that open with the order triple, or with the bindings, never open with the filter's. *)
let order_fields_not_tag (rn:renderers) (o:list sort_key) (a:arguments) (x:string) (r:list string)
  : Lemma (requires (x == "w" \/ x == "p") /\ order_fields rn o (arg_fields rn a) == "" :: x :: r)
          (ensures False)
  = match o with
    | [] -> arg_fields_not_shape rn a x r
    | _ :: _ -> ()

(* The shape fields and the bindings after them determine the filter, the order and the bindings. *)
let shape_fields_injective (rn:renderers) (w1 w2:list predicate) (o1 o2:list sort_key)
  (a1 a2:arguments)
  : Lemma (requires shape_premises rn /\
                    shape_fields rn w1 o1 (arg_fields rn a1) == shape_fields rn w2 o2 (arg_fields rn a2))
          (ensures w1 == w2 /\ o1 == o2 /\ a1 == a2)
  = match w1, w2 with
    | [], [] -> order_fields_injective rn o1 o2 a1 a2
    | _ :: _, _ :: _ -> order_fields_injective rn o1 o2 a1 a2
    | [], _ :: _ ->
      order_fields_not_tag rn o1 a1 "w" (rn.render_where w2 :: order_fields rn o2 (arg_fields rn a2))
    | _ :: _, [] ->
      order_fields_not_tag rn o2 a2 "w" (rn.render_where w1 :: order_fields rn o1 (arg_fields rn a1))

(* The key fields determine the declaration's filter and order and the argument list. *)
let key_fields_injective (rn:renderers) (q q':query) (a a':arguments)
  : Lemma (requires shape_premises rn /\ key_fields rn q a == key_fields rn q' a')
          (ensures q.q_where == q'.q_where /\ q.q_order_by == q'.q_order_by /\ a == a')
  = shape_fields_injective rn q.q_where q'.q_where q.q_order_by q'.q_order_by a a'

(* THE FIFTH THEOREM. F#: `Query.invocationKey`'s pre-image (Phase 225; over the declaration's
   filter and order since Phase 398). Two (declaration, argument list) pairs whose pre-images agree
   have the SAME `Where` and the SAME `OrderBy`, and their name-sorted argument lists are the same
   list, holding exactly the same bindings: declarations differing in filter or order, and distinct
   argument sets, have distinct pre-images, for every declaration, accepted or not. It needs no
   premise about the comparator — the sort is a function, and equal outputs are all the argument
   reads. (The id is the key's prefix, outside the pre-image; two keys with distinct ids differ
   there.) Whether two distinct pre-images HASH apart is a claim about FNV-1a and is not made
   (`query-renderers-abstract`). *)
let invocation_key_injective (#sym:eqtype) (reveal:string -> list sym) (e t:sym) (rn:renderers)
  (q q':query) (a a':arguments)
  : Lemma (requires key_premises reveal e t rn /\
                    canonical_key rn q (sort_args rn a) == canonical_key rn q' (sort_args rn a'))
          (ensures q.q_where == q'.q_where /\ q.q_order_by == q'.q_order_by /\
                   sort_args rn a == sort_args rn a' /\
                   (forall (x:(string & cell)). mem_arg x a = mem_arg x a')) =
  fields_injective reveal e t rn (key_fields rn q (sort_args rn a)) (key_fields rn q' (sort_args rn a'));
  key_fields_injective rn q q' (sort_args rn a) (sort_args rn a');
  let aux (x:(string & cell)) : Lemma (mem_arg x a = mem_arg x a') =
    sort_mem rn a x; sort_mem rn a' x
  in
  FStar.Classical.forall_intro aux

(* ======================================================================================
   9. THE FINDINGS.
   ====================================================================================== *)

(* The argument set binding every declared param to `Null`. *)
let rec nulls_of (ps:list query_param) : Tot arguments =
  match ps with
  | [] -> []
  | p :: t -> (p.p_name, Null) :: nulls_of t

let rec find_param_mem (name:string) (ps:list query_param)
  : Lemma (Some? (find_param name ps) <==> mem name (param_names ps))
  = match ps with
    | [] -> ()
    | _ :: t -> find_param_mem name t

let rec nulls_keys (ps:list query_param)
  : Lemma (keys (nulls_of ps) == param_names ps)
  = match ps with
    | [] -> ()
    | _ :: t -> nulls_keys t

let rec has_key_keys (#a:Type) (k:string) (l:list (string & a))
  : Lemma (has_key k l = mem k (keys l))
  = match l with
    | [] -> ()
    | _ :: t -> has_key_keys k t

let rec all_in_cons (l m:list string) (x:string)
  : Lemma (requires all_in l m)
          (ensures all_in l (x :: m))
  = match l with
    | [] -> ()
    | _ :: t -> all_in_cons t m x

let rec all_in_refl (l:list string)
  : Lemma (all_in l l)
  = match l with
    | [] -> ()
    | x :: t -> all_in_refl t; all_in_cons t t x

let rec nulls_well_typed (ps sub:list query_param)
  : Lemma (requires all_in (param_names sub) (param_names ps))
          (ensures all_well_typed ps (nulls_of sub))
  = match sub with
    | [] -> ()
    | p :: t -> find_param_mem p.p_name ps; nulls_well_typed ps t

let rec nulls_bind_all (sub:list query_param) (a:arguments)
  : Lemma (requires all_in (param_names sub) (keys a))
          (ensures unbound_required sub a == [])
  = match sub with
    | [] -> ()
    | p :: t -> has_key_keys p.p_name a; nulls_bind_all t a

(* Every required param is bound to a value — the clause `Required` now carries. *)
let rec required_valued (ps:list query_param) (a:arguments) : Tot bool =
  match ps with
  | [] -> true
  | p :: t -> (not p.p_required || has_value p.p_name a) && required_valued t a

let rec has_value_has_key (k:string) (a:arguments)
  : Lemma (has_value k a ==> has_key k a)
  = match a with
    | [] -> ()
    | _ :: t -> has_value_has_key k t

let rec required_steps_iff (ps:list query_param) (a:arguments)
  : Lemma ((unbound_required ps a == [] /\ null_required ps a == []) <==> required_valued ps a)
  = match ps with
    | [] -> ()
    | p :: t -> has_value_has_key p.p_name a; required_steps_iff t a

(* THE FIRST FINDING, CLOSED (Phase 226; it was `all_null_accepted`). Validation accepts EXACTLY
   the argument sets whose every binding is well typed and that bind every required param to a
   VALUE — a `Null` binding no longer satisfies `Required`. Stated as the iff so the clause is
   visible where the finding said there was none: `required_valued` relates `Null` to
   `p_required` through `has_value`. *)
let required_is_non_null (q:query) (a:arguments)
  : Lemma (validate_params q a == Ok () <==>
           (repeated [] (keys a) == [] /\ all_well_typed q.q_params a /\ required_valued q.q_params a))
  = validate_params_exact q a;
    required_steps_iff q.q_params a

(* The names a declaration marks required, in declaration order. *)
let rec required_names (ps:list query_param) : Tot (list string) =
  match ps with
  | [] -> []
  | p :: t -> if p.p_required then p.p_name :: required_names t else required_names t

let rec has_value_nulls (sub:list query_param) (n:string)
  : Lemma (has_value n (nulls_of sub) == false)
  = match sub with
    | [] -> ()
    | _ :: t -> has_value_nulls t n

let rec null_nulls (ps sub:list query_param)
  : Lemma (requires all_in (param_names ps) (param_names sub))
          (ensures null_required ps (nulls_of sub) == required_names ps)
  = match ps with
    | [] -> ()
    | p :: t ->
      nulls_keys sub;
      has_key_keys p.p_name (nulls_of sub);
      has_value_nulls sub p.p_name;
      null_nulls t sub

(* The all-`Null` argument set, BUILT from the declaration: it passes step 1 (every name is
   declared, and `Null` is type-agnostic absence) and step 2 (every name is present), and step 3
   refuses it as `RequiredParamsNull`, naming EVERY required param in declaration order — so it is
   accepted only by a declaration that requires nothing. Since Phase 307 it is stated over a
   declaration whose parameter names are distinct: over a repeated name the built set binds that
   name twice, and step 0 refuses it `DuplicateParam` first. *)
let all_null_refusal_exact (q:query)
  : Lemma (requires repeated [] (param_names q.q_params) == [])
          (ensures validate_params q (nulls_of q.q_params) ==
           (match required_names q.q_params with
            | [] -> Ok ()
            | n -> Error (RequiredParamsNull n)))
  = nulls_keys q.q_params;
    all_in_refl (param_names q.q_params);
    nulls_well_typed q.q_params q.q_params;
    check_args_exact q.q_params (param_names q.q_params) (nulls_of q.q_params);
    nulls_keys q.q_params;
    nulls_bind_all q.q_params (nulls_of q.q_params);
    null_nulls q.q_params q.q_params

(* The declaration the former second finding (`key_collision`, closed by Phase 225) was exhibited
   on: `a` required, `b` optional, both strings. The two argument sets below shared a pre-image
   before Phase 225; `invocation_key_injective` says they no longer can, and the shipped seam's
   closure case keys them apart. *)
let collision_params : list query_param =
  [ { p_name = "a"; p_type = StringType; p_required = true };
    { p_name = "b"; p_type = StringType; p_required = false } ]

let collision_one : arguments = [ ("a", Str "1b=s2") ]
let collision_two : arguments = [ ("a", Str "1"); ("b", Str "2") ]

(* ======================================================================================
   10. PHASE 316 — the registry discharges the distinct-names premise; the page key; the
       lifecycle. A registry is a lattice, not an append log.
   ====================================================================================== *)

(* `register` refuses a fresh declaration that names a parameter twice, naming the first repeat. *)
let register_refuses_duplicate_params (q:query) (r:registry)
  : Lemma (requires not (mem q.q_id (ids (enumerate r))) /\ Cons? (repeated [] (param_names q.q_params)))
          (ensures register q r == Error (DuplicateParam (Cons?.hd (repeated [] (param_names q.q_params)))))
  = find_query_mem q.q_id r.queries

(* Every declaration the list holds names its parameters once. *)
let rec params_distinct (qs:list query) : Tot bool =
  match qs with
  | [] -> true
  | q :: t -> Nil? (repeated [] (param_names q.q_params)) && params_distinct t

(* The invariant `empty` starts and `register` keeps. *)
let register_keeps_params_distinct (q:query) (r r':registry)
  : Lemma (requires params_distinct r.queries /\ register q r == Ok r')
          (ensures params_distinct r'.queries)
  = ()

let rec find_query_params_distinct (id:string) (qs:list query)
  : Lemma (requires params_distinct qs)
          (ensures (match find_query id qs with
                    | Some q -> repeated [] (param_names q.q_params) == []
                    | None -> True))
  = match qs with
    | [] -> ()
    | _ :: t -> find_query_params_distinct id t

(* THE DISCHARGE. `all_null_refusal_exact` is stated over distinct names; a query a registry built
   by `register` resolves HAS distinct names, so the exact refusal of its all-`Null` set holds of
   every registered query with no premise left about the declaration. *)
let registered_all_null_refusal_exact (r:registry) (id:string)
  : Lemma (requires params_distinct r.queries)
          (ensures (match try_find_query id r with
                    | Some q ->
                      validate_params q (nulls_of q.q_params) ==
                      (match required_names q.q_params with
                       | [] -> Ok ()
                       | n -> Error (RequiredParamsNull n))
                    | None -> True))
  = find_query_params_distinct id r.queries;
    match try_find_query id r with
    | Some q -> all_null_refusal_exact q
    | None -> ()

(* F#: `Query.invocationKeyPage`'s pre-image fields — for the first page the key fields alone (the
   declaration's shape fields, Phase 398, then the arguments'), otherwise the page triple (an empty
   name, the tag `p` no cell carries and no shape triple carries, the token) in front of them. *)
let page_fields (rn:renderers) (tok:option string) (q:query) (l:arguments) : Tot (list string) =
  match tok with
  | None -> key_fields rn q l
  | Some t -> "" :: "p" :: t :: key_fields rn q l

(* F#: `Hash.canonicalFields` over the page fields. *)
let canonical_page (rn:renderers) (tok:option string) (q:query) (l:arguments) : Tot string =
  fields rn (page_fields rn tok q l)

(* F#: `Query.invocationKeyPage`. *)
let invocation_key_page (rn:renderers) (q:query) (a:arguments) (tok:option string) : Tot string =
  q.q_id ^ "#" ^ rn.hash (canonical_page rn tok q (sort_args rn a))

(* The first page keys exactly as `invocationKey`, so a journal keyed before paging still replays. *)
let invocation_key_page_none (rn:renderers) (q:query) (a:arguments)
  : Lemma (invocation_key_page rn q a None == invocation_key rn q a)
  = ()

(* No cell's tag is the page tag — which is what keeps a page triple from reading as a binding. *)
let cell_tag_not_page (c:cell) : Lemma (cell_tag c <> "p") = ()

(* The key fields never open with the page triple: they open with a shape triple (`w` or `o`) or
   with a binding, whose second field is a cell's tag. *)
let key_fields_not_page (rn:renderers) (q:query) (a:arguments) (r:list string)
  : Lemma (requires key_fields rn q a == "" :: "p" :: r) (ensures False)
  = match q.q_where with
    | [] -> order_fields_not_tag rn q.q_order_by a "p" r
    | _ :: _ -> ()

(* The page fields determine the token, the declaration's filter and order, and the argument list. *)
let page_fields_injective (rn:renderers) (t1 t2:option string) (q1 q2:query) (a1 a2:arguments)
  : Lemma (requires shape_premises rn /\ page_fields rn t1 q1 a1 == page_fields rn t2 q2 a2)
          (ensures t1 == t2 /\ q1.q_where == q2.q_where /\ q1.q_order_by == q2.q_order_by /\ a1 == a2)
  = match t1, t2 with
    | None, None -> key_fields_injective rn q1 q2 a1 a2
    | Some _, Some _ -> key_fields_injective rn q1 q2 a1 a2
    | None, Some t ->
      key_fields_not_page rn q1 a1 (t :: key_fields rn q2 a2)
    | Some t, None ->
      key_fields_not_page rn q2 a2 (t :: key_fields rn q1 a1)

(* THE PAGE KEY'S INJECTIVITY (Phase 316, extending 225's; over the declaration's filter and order
   since Phase 398). Two (declaration, argument set, token) triples whose page pre-images agree hold
   the SAME token, the SAME `Where`, the SAME `OrderBy` and the same bindings — so a paged query
   captured page by page keys every page apart, a declaration filtered or ordered differently keys
   apart page by page, and replay of page n reads page n's capture. Conditional exactly as
   `invocation_key_injective` is, on `key_premises`; whether two distinct pre-images HASH apart is a
   claim about FNV-1a and is not made. *)
let invocation_key_page_injective (#sym:eqtype) (reveal:string -> list sym) (e t:sym) (rn:renderers)
  (q q':query) (a a':arguments) (tok tok':option string)
  : Lemma (requires key_premises reveal e t rn /\
                    canonical_page rn tok q (sort_args rn a) == canonical_page rn tok' q' (sort_args rn a'))
          (ensures tok == tok' /\ q.q_where == q'.q_where /\ q.q_order_by == q'.q_order_by /\
                   sort_args rn a == sort_args rn a' /\
                   (forall (x:(string & cell)). mem_arg x a = mem_arg x a')) =
  fields_injective reveal e t rn (page_fields rn tok q (sort_args rn a)) (page_fields rn tok' q' (sort_args rn a'));
  page_fields_injective rn tok tok' q q' (sort_args rn a) (sort_args rn a');
  let aux (x:(string & cell)) : Lemma (mem_arg x a = mem_arg x a') =
    sort_mem rn a x; sort_mem rn a' x
  in
  FStar.Classical.forall_intro aux

(* The law a host reads: distinct tokens give distinct pre-images, whatever the declarations and
   the arguments. *)
let distinct_tokens_distinct_preimages (#sym:eqtype) (reveal:string -> list sym) (e t:sym)
  (rn:renderers) (q q':query) (a a':arguments) (tok tok':option string)
  : Lemma (requires key_premises reveal e t rn /\ ~(tok == tok'))
          (ensures ~(canonical_page rn tok q (sort_args rn a) == canonical_page rn tok' q' (sort_args rn a')))
  = FStar.Classical.move_requires (invocation_key_page_injective reveal e t rn q q' a a' tok) tok'

(* Phase 398 — and the same for the shape: a declaration filtered or ordered differently gives a
   distinct pre-image on every page, whatever the arguments and the tokens. *)
let distinct_shapes_distinct_preimages (#sym:eqtype) (reveal:string -> list sym) (e t:sym)
  (rn:renderers) (q q':query) (a a':arguments) (tok tok':option string)
  : Lemma (requires key_premises reveal e t rn /\
                    ~(q.q_where == q'.q_where /\ q.q_order_by == q'.q_order_by))
          (ensures ~(canonical_page rn tok q (sort_args rn a) == canonical_page rn tok' q' (sort_args rn a')))
  = FStar.Classical.move_requires (invocation_key_page_injective reveal e t rn q q' a a' tok) tok'

(* F#: `Query.invokePage` — `invoke`, with the token handed to the resolver. *)
let invoke_page (#v:Type) (q:query) (a:arguments) (tok:option string)
  (resolve:query -> option string -> deferred v) : Tot (outcome (deferred v) query_error) =
  invoke q a (fun q -> resolve q tok)

(* F#: `QueryRegistry.dispatchPage`. *)
let dispatch_page (#v:Type) (r:registry) (id:string) (a:arguments) (tok:option string)
  (resolve:query -> option string -> deferred v) : Tot (outcome (deferred v) query_error) =
  match find_query id r.queries with
  | None -> Error (NoSuchQuery id (ids r.queries))
  | Some q -> invoke_page q a tok resolve

(* The paged dispatch IS a dispatch, under the resolver that reads the token: default-deny,
   validation first and the three outcomes all carry over unchanged. *)
let dispatch_page_is_dispatch (#v:Type) (r:registry) (id:string) (a:arguments) (tok:option string)
  (resolve:query -> option string -> deferred v)
  : Lemma (dispatch_page r id a tok resolve == dispatch r id a (fun q -> resolve q tok))
  = ()

(* F#: `QueryRegistry.unregister` — the declaration under `id` removed, or `NoSuchQuery`. *)
let rec remove_query (id:string) (qs:list query) : Tot (list query) =
  match qs with
  | [] -> []
  | q :: t -> if q.q_id = id then remove_query id t else q :: remove_query id t

let unregister (id:string) (r:registry) : Tot (outcome registry query_error) =
  match find_query id r.queries with
  | None -> Error (NoSuchQuery id (ids r.queries))
  | Some _ -> Ok { queries = remove_query id r.queries }

let rec remove_absent (id:string) (qs:list query)
  : Lemma (requires not (mem id (ids qs))) (ensures remove_query id qs == qs)
  = match qs with
    | [] -> ()
    | _ :: t -> remove_absent id t

(* `unregister` undoes a registration: what it gives back holds exactly what the registry held
   before. *)
let unregister_register (q:query) (r r':registry)
  : Lemma (requires register q r == Ok r')
          (ensures (match unregister q.q_id r' with
                    | Ok r'' -> r''.queries == r.queries
                    | Error _ -> False))
  = find_query_mem q.q_id r.queries;
    remove_absent q.q_id r.queries

(* And refuses an id the registry does not hold, naming every id it holds. *)
let unregister_refuses_unheld (id:string) (r:registry)
  : Lemma (requires not (mem id (ids (enumerate r))))
          (ensures unregister id r == Error (NoSuchQuery id (ids r.queries)))
  = find_query_mem id r.queries

(* F#: `QueryRegistry.restrict` — the declarations whose id is kept. *)
let rec keep_queries (keep:list string) (qs:list query) : Tot (list query) =
  match qs with
  | [] -> []
  | q :: t -> if mem q.q_id keep then q :: keep_queries keep t else keep_queries keep t

let restrict (keep:list string) (r:registry) : Tot registry = { queries = keep_queries keep r.queries }

(* `restrict` keeps exactly the held ids that are kept — never widening. *)
let rec restrict_members_list (keep:list string) (qs:list query) (id:string)
  : Lemma (mem id (ids (keep_queries keep qs)) <==> (mem id keep /\ mem id (ids qs)))
  = match qs with
    | [] -> ()
    | _ :: t -> restrict_members_list keep t id

let restrict_members (keep:list string) (r:registry) (id:string)
  : Lemma (mem id (ids (enumerate (restrict keep r))) <==> (mem id keep /\ mem id (ids (enumerate r))))
  = restrict_members_list keep r.queries id

(* F#: `QueryRegistry.union` — refused at the first declaration of `b` whose id `a` holds,
   otherwise both. *)
let rec first_shared (a:list query) (b:list query) : Tot (option string) =
  match b with
  | [] -> None
  | q :: t -> if mem q.q_id (ids a) then Some q.q_id else first_shared a t

let rec app_queries (x y:list query) : Tot (list query) =
  match x with
  | [] -> y
  | h :: t -> h :: app_queries t y

let union (a b:registry) : Tot (outcome registry query_error) =
  match first_shared a.queries b.queries with
  | Some id -> Error (DuplicateQuery id)
  | None -> Ok { queries = app_queries a.queries b.queries }

let rec first_shared_some (a b:list query)
  : Lemma (ensures (match first_shared a b with
                    | Some id -> mem id (ids a) /\ mem id (ids b)
                    | None -> forall (id:string). ~(mem id (ids a) /\ mem id (ids b))))
  = match b with
    | [] -> ()
    | _ :: t -> first_shared_some a t

let rec app_ids (x y:list query) (id:string)
  : Lemma (mem id (ids (app_queries x y)) <==> (mem id (ids x) \/ mem id (ids y)))
  = match x with
    | [] -> ()
    | _ :: t -> app_ids t y id

(* `union` refuses exactly when the two registries share an id, naming one they share; otherwise
   its result holds exactly the ids of both. *)
let union_members (a b:registry) (id:string)
  : Lemma (ensures (match union a b with
                    | Error (DuplicateQuery d) -> mem d (ids a.queries) /\ mem d (ids b.queries)
                    | Error _ -> False
                    | Ok u ->
                      (forall (x:string). ~(mem x (ids a.queries) /\ mem x (ids b.queries))) /\
                      (mem id (ids u.queries) <==> (mem id (ids a.queries) \/ mem id (ids b.queries)))))
  = first_shared_some a.queries b.queries;
    app_ids a.queries b.queries id

(* ======================================================================================
   TWINS (Phase 309) — the extractor premise, sampled at this model.

   The leg's extraction diff makes "the oracle is the model" a checked claim about TEXT. Nothing
   in it says the F# the extractor emits COMPUTES what this model means: a mis-extraction that
   compiles would pass every other step. Each fixture below applies this model's own functions to
   a concrete input and compares the result with the value the model means there, and the
   assertion at the end is discharged by NORMALISATION — F*'s normaliser evaluates every closure
   to `true` under the model's own semantics. The list is extracted with the rest of the model,
   and the `Proofs.Oracle` family runs the extracted closures against the extracted oracle
   ("twin evaluation"): a closure that comes back `false` there is the F# backend disagreeing with
   the normaliser on that input. Sampled, never proved: the discharge holds on these inputs, which
   is where the `tested` rows already live. The kit's TWIN step (`kit/check-proof-leg.ps1`, step
   2c) refuses an extracted model that declares no twins.
   ====================================================================================== *)

noeq type twin = { tname : string; tholds : unit -> bool }

let rec twins_hold (l:list twin) : Tot bool =
  match l with
  | [] -> true
  | t :: r -> t.tholds () && twins_hold r
let twin_query : query =
  { q_id = "q";
    q_params = [ { p_name = "region"; p_type = StringType; p_required = true } ];
    q_schema = [ ("n", StringType) ];
    q_effect = { host = Pure; determinism = { has_clock = false; has_random = false; has_network = false } };
    q_source = "src";
    q_timeout_ms = None;
    q_page_size = None;
    q_where = [];
    q_order_by = [] }

let twin_param : query_param = { p_name = "region"; p_type = StringType; p_required = true }

(* A renderer record for the page-field twins: the fields they compare carry no number and are
   never hashed, so only the shape of the record matters. *)
let twin_rn : renderers =
  { render_int = (fun _ -> "");
    render_float = (fun x -> x);
    hash = (fun x -> x);
    name_le = (fun _ _ -> true);
    field = (fun x -> x);
    render_where = (fun _ -> "W");
    render_order = (fun _ -> "O") }

(* Phase 398 — a declaration filtered and ordered over its one column. *)
let twin_shaped : query =
  { twin_query with
      q_where = [ Contains "n" "eu" ];
      q_order_by = [ { k_column = "n"; k_direction = Descending } ] }

let twins : list twin = [
  { tname = "validate-params-accepts-a-bound-required-param";
    tholds = (fun () -> validate_params twin_query [ ("region", Str "eu") ] = Ok ()) };
  { tname = "validate-params-refuses-an-unbound-required-param";
    tholds = (fun () -> validate_params twin_query [] = Error (RequiredParamsUnbound [ "region" ])) };
  { tname = "validate-params-refuses-a-type-mismatch";
    tholds = (fun () ->
      validate_params twin_query [ ("region", Bool true) ] = Error (ParamTypeMismatch "region" StringType BoolType)) };
  { tname = "validate-params-widens-an-int-into-a-float-param";
    tholds = (fun () ->
      validate_params
        ({ twin_query with q_params = [ { p_name = "region"; p_type = FloatType; p_required = true } ] })
        [ ("region", Int 3) ] = Ok ()) };
  { tname = "validate-params-refuses-a-float-for-an-int-param";
    tholds = (fun () -> widens FloatType IntType = false) };
  { tname = "register-refuses-a-duplicate";
    tholds = (fun () -> register twin_query ({ queries = [ twin_query ] }) = Error (DuplicateQuery "q")) };
  { tname = "register-refuses-a-repeated-parameter-name";
    tholds = (fun () ->
      register ({ twin_query with q_params = [ twin_param; twin_param ] }) empty
      = Error (DuplicateParam "region")) };
  { tname = "unregister-undoes-register";
    tholds = (fun () -> unregister "q" ({ queries = [ twin_query ] }) = Ok empty) };
  { tname = "the-first-page-adds-no-field";
    tholds = (fun () -> page_fields twin_rn None twin_query [ ("a", Str "x") ] = [ "a"; "s"; "x" ]) };
  { tname = "a-later-page-leads-with-the-page-triple";
    tholds = (fun () -> page_fields twin_rn (Some "t") twin_query [] = [ ""; "p"; "t" ]) };
  { tname = "a-shaped-declaration-leads-with-its-filter-then-its-order";
    tholds = (fun () ->
      page_fields twin_rn (Some "t") twin_shaped [ ("a", Str "x") ]
      = [ ""; "p"; "t"; ""; "w"; "W"; ""; "o"; "O"; "a"; "s"; "x" ]) };
  { tname = "an-order-alone-adds-only-its-triple";
    tholds = (fun () ->
      key_fields twin_rn ({ twin_shaped with q_where = [] }) [] = [ ""; "o"; "O" ]) };
  { tname = "register-admits-a-well-formed-filter-and-order";
    tholds = (fun () -> register twin_shaped empty = Ok ({ queries = [ twin_shaped ] })) };
  { tname = "register-refuses-an-undeclared-filter-column";
    tholds = (fun () ->
      register ({ twin_query with q_where = [ IsNull "x" ] }) empty = Error (UnknownColumn "x" [ "n" ])) };
  { tname = "register-refuses-contains-on-a-column-that-is-not-a-string";
    tholds = (fun () ->
      register ({ twin_query with q_schema = [ ("n", IntType) ]; q_where = [ Contains "n" "1" ] }) empty
      = Error (PredicateNotApplicable "contains" "n" IntType)) };
  { tname = "register-refuses-a-null-literal";
    tholds = (fun () ->
      register ({ twin_query with q_where = [ EqualTo "n" Null ] }) empty
      = Error (IllFormedLiteral "n" null_literal_reason)) };
  { tname = "register-refuses-a-literal-of-another-type";
    tholds = (fun () ->
      register ({ twin_query with q_where = [ AtLeast "n" (Int 3) ] }) empty
      = Error (PredicateTypeMismatch "n" StringType IntType)) };
  { tname = "register-refuses-an-order-naming-a-column-twice";
    tholds = (fun () ->
      register
        ({ twin_query with
             q_order_by = [ { k_column = "n"; k_direction = Ascending }; { k_column = "n"; k_direction = Descending } ] })
        empty
      = Error (DuplicateSortColumn "n")) } ]

let _ = assert_norm (twins_hold twins == true)
