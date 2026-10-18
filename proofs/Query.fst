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
       (section 8b) — and, since Phase 430, over argument lists whose timestamp cells carry the
       unit their text names (`units_named`, section 8b): the key writes a timestamp's text alone,
       as production does, and a cell's unit is production's `Cell.typeOf` reading of that text.
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
   the model holds the map as a list, and the theorem is about membership. `QueryCodec` until Phase 455; since then section 19 models it, and
   what it leaves out is its strict read policy and the column codec it is handed (a parameter).

   (Phase 455) THE CODEC, section 19: `QueryCodec`'s declaration, result-page, async-result and
   query-error pairs round trip EXACTLY (`query_roundtrip`, `result_roundtrip`,
   `deferred_result_roundtrip`, `query_error_roundtrip`) over a column-codec parameter, and every
   refusal their readers answer RESOLVES in the document read (`query_refusals_resolve` and its
   siblings).

   HOW TO READ IT. Every definition names its F# counterpart. Sections 1 to 18 are SELF-CONTAINED
   like `Capability.fst` — they open nothing, restating `outcome`, `deferred` and the list helpers
   they need. Section 19 (Phase 455) reads `Capability.fst` by name (module `C`): the `JVal`, the
   decode layer's combinators and the refusal law the two seams' codecs share, rather than a third
   restatement of them. The extracted oracle shares `Prims.fs`, the `option` shim and, since 455,
   `Capability.fs`, which compiles ahead of it.

   Apache-2.0, like everything beside it.
*)
module Query

open Twin
open ListAlgebra

(* Phase 455: the codec section (19) reads the capability seam's codec layer by name — its `JVal`,
   its decoders and the refusal law — rather than restating it a third time. *)
module C = Capability

(* ======================================================================================
   0. The list helpers, self-contained, each naming the FSharp.Core function it stands for.
   ====================================================================================== *)

(* F#: `Result<'a, 'e>`. *)
type outcome (a e:Type) =
  | Ok    : a -> outcome a e
  | Error : e -> outcome a e

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

(* Every element of `l` is in `m`. *)
let rec all_in (l m:list string) : Tot bool =
  match l with
  | [] -> true
  | x :: t -> mem x m && all_in t m

(* ======================================================================================
   1. The declaration vocabulary — `ColumnType`, `Cell`, `EffectClass`, `QueryParam`, `Query`,
      `QueryError`.
   ====================================================================================== *)

(* F#: `TimeUnit` (Phase 422), restated here as `outcome` is: the module opens no other model. The
   digits are what `TimeUnit.widens` compares. *)
type time_unit =
  | Seconds
  | Milliseconds
  | Microseconds
  | Nanoseconds

(* F#: `TimeUnit.digits`. *)
let unit_digits (u:time_unit) : Tot int =
  match u with
  | Seconds -> 0
  | Milliseconds -> 3
  | Microseconds -> 6
  | Nanoseconds -> 9

(* F#: `TimeUnit.widens` — a coarser unit into a finer one. *)
let unit_widens (from target:time_unit) : Tot bool = unit_digits from <= unit_digits target

(* F#: `ColumnType`. A timestamp type carries its unit (Phase 422; this model Phase 430). *)
type column_type =
  | IntType
  | FloatType
  | BoolType
  | StringType
  | DateType
  | TimestampType : time_unit -> column_type
  | DecimalType

(* F#: `Cell`. A float crosses as an opaque carrier — the seam reads its TYPE and hands the
   carrier to the float renderer, and nothing else. A decimal's carrier is its text, which is
   what the F# cell holds too: the seam reads it as it stands, exactly as it reads a date's. A
   TIMESTAMP crosses with the unit `Cell.typeOf` names for it beside its text (Phase 430): the
   coarsest unit that holds its instant, `TemporalText.unitOf` — digit arithmetic over the text
   this module does not model and `Temporal.fst` does; the seam reads the unit as the cell's type
   and the text as its payload, and the oracle host computes the unit from the text at the edge.
   The F# declares `Decimal` after `Null`, so that the published cases keep their tags; the order
   of the cases is nothing any clause here reads. *)
type cell =
  | Int       : int -> cell
  | Float     : string -> cell
  | Bool      : bool -> cell
  | Str       : string -> cell
  | Date      : string -> cell
  | Timestamp : time_unit -> string -> cell
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
  | Timestamp u _ -> Some (TimestampType u)
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
  | Timestamp _ _ -> "t"
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
  | Timestamp _ v -> v
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
  match from, target with
  | TimestampType a, TimestampType b -> unit_widens a b
  | _ -> from = target || (from = IntType && target = FloatType) || (from = IntType && target = DecimalType)

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
        | Some got ->
          (* A timestamp literal fits any column at least as fine as its own unit (Phase 422:
             `Cell.typeOf` names the coarsest unit); every other literal is held to its column's
             exact type — an `Int` on a float column is a mismatch here where `widens` would admit it. *)
          if (match got, ty with
              | TimestampType a, TimestampType b -> unit_widens a b
              | _ -> got = ty)
          then None else Some (PredicateTypeMismatch c ty got)
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
   reading `Chain.fst` takes for its splices, restated here because this module opens no other model.
   `symbols_faithful reveal` says two things true of `System.String` by construction:
   concatenation is symbol-list append, and two strings with the same symbols are one string.
   It is a HYPOTHESIS in the `requires` of the lemmas that spend it, never an `assume`. *)

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

(* THE UNIT-NAMED PREMISE (Phase 430). A timestamp cell carries the unit `Cell.typeOf` names for
   its text — `TemporalText.unitOf`, a reading of the text this module takes as a host function
   `uo`, as it takes the renderers — and the capture key writes the TEXT alone, as production does.
   So two cells with one tag and one payload are one cell exactly when their units are the text's:
   the injectivity chain below carries that as a premise on the argument lists, and every cell
   production builds satisfies it (the oracle host computes the unit from the text at the edge). *)
let unit_named (uo:string -> time_unit) (c:cell) : Tot bool =
  match c with
  | Timestamp u s -> u = uo s
  | _ -> true

let rec units_named (uo:string -> time_unit) (a:arguments) : Tot bool =
  match a with
  | [] -> true
  | (_, c) :: t -> unit_named uo c && units_named uo t

let rec insert_units_named (uo:string -> time_unit) (rn:renderers) (x:(string & cell)) (l:arguments)
  : Lemma (requires unit_named uo (snd x) /\ units_named uo l) (ensures units_named uo (insert_arg rn x l))
  = match l with
    | [] -> ()
    | _ :: t -> insert_units_named uo rn x t

let rec sort_units_named (uo:string -> time_unit) (rn:renderers) (l:arguments)
  : Lemma (requires units_named uo l) (ensures units_named uo (sort_args rn l))
  = match l with
    | [] -> ()
    | x :: t -> sort_units_named uo rn t; insert_units_named uo rn x (sort_args rn t)

(* A cell's tag and payload determine the cell, among cells whose unit is their text's. *)
let cell_fields_injective (rn:renderers) (uo:string -> time_unit) (c c':cell)
  : Lemma (requires injective rn.render_int /\ injective rn.render_float /\
                    unit_named uo c /\ unit_named uo c' /\
                    cell_tag c == cell_tag c' /\ cell_payload rn c == cell_payload rn c')
          (ensures c == c') = ()

(* Three fields per binding, so the flattening is injective. *)
let rec arg_fields_injective (rn:renderers) (uo:string -> time_unit) (a1 a2:arguments)
  : Lemma (requires injective rn.render_int /\ injective rn.render_float /\
                    units_named uo a1 /\ units_named uo a2 /\
                    arg_fields rn a1 == arg_fields rn a2)
          (ensures a1 == a2) =
  match a1, a2 with
  | [], [] -> ()
  | [], _ :: _ -> ()
  | _ :: _, [] -> ()
  | (_, v1) :: t1, (_, v2) :: t2 ->
    cell_fields_injective rn uo v1 v2;
    arg_fields_injective rn uo t1 t2

(* The canonical string is injective on argument lists — any lists, sorted or not. *)
let canonical_injective (#sym:eqtype) (reveal:string -> list sym) (e t:sym) (rn:renderers)
  (uo:string -> time_unit) (a1 a2:arguments)
  : Lemma (requires key_premises reveal e t rn /\ units_named uo a1 /\ units_named uo a2 /\
                    canonical rn a1 == canonical rn a2)
          (ensures a1 == a2) =
  fields_injective reveal e t rn (arg_fields rn a1) (arg_fields rn a2);
  arg_fields_injective rn uo a1 a2

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
let order_fields_injective (rn:renderers) (uo:string -> time_unit) (o1 o2:list sort_key) (a1 a2:arguments)
  : Lemma (requires shape_premises rn /\ units_named uo a1 /\ units_named uo a2 /\
                    order_fields rn o1 (arg_fields rn a1) == order_fields rn o2 (arg_fields rn a2))
          (ensures o1 == o2 /\ a1 == a2)
  = match o1, o2 with
    | [], [] -> arg_fields_injective rn uo a1 a2
    | _ :: _, _ :: _ -> arg_fields_injective rn uo a1 a2
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
let shape_fields_injective (rn:renderers) (uo:string -> time_unit) (w1 w2:list predicate) (o1 o2:list sort_key)
  (a1 a2:arguments)
  : Lemma (requires shape_premises rn /\ units_named uo a1 /\ units_named uo a2 /\
                    shape_fields rn w1 o1 (arg_fields rn a1) == shape_fields rn w2 o2 (arg_fields rn a2))
          (ensures w1 == w2 /\ o1 == o2 /\ a1 == a2)
  = match w1, w2 with
    | [], [] -> order_fields_injective rn uo o1 o2 a1 a2
    | _ :: _, _ :: _ -> order_fields_injective rn uo o1 o2 a1 a2
    | [], _ :: _ ->
      order_fields_not_tag rn o1 a1 "w" (rn.render_where w2 :: order_fields rn o2 (arg_fields rn a2))
    | _ :: _, [] ->
      order_fields_not_tag rn o2 a2 "w" (rn.render_where w1 :: order_fields rn o1 (arg_fields rn a1))

(* The key fields determine the declaration's filter and order and the argument list. *)
let key_fields_injective (rn:renderers) (uo:string -> time_unit) (q q':query) (a a':arguments)
  : Lemma (requires shape_premises rn /\ units_named uo a /\ units_named uo a' /\
                    key_fields rn q a == key_fields rn q' a')
          (ensures q.q_where == q'.q_where /\ q.q_order_by == q'.q_order_by /\ a == a')
  = shape_fields_injective rn uo q.q_where q'.q_where q.q_order_by q'.q_order_by a a'

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
  (uo:string -> time_unit) (q q':query) (a a':arguments)
  : Lemma (requires key_premises reveal e t rn /\ units_named uo a /\ units_named uo a' /\
                    canonical_key rn q (sort_args rn a) == canonical_key rn q' (sort_args rn a'))
          (ensures q.q_where == q'.q_where /\ q.q_order_by == q'.q_order_by /\
                   sort_args rn a == sort_args rn a' /\
                   (forall (x:(string & cell)). mem_arg x a = mem_arg x a')) =
  sort_units_named uo rn a;
  sort_units_named uo rn a';
  fields_injective reveal e t rn (key_fields rn q (sort_args rn a)) (key_fields rn q' (sort_args rn a'));
  key_fields_injective rn uo q q' (sort_args rn a) (sort_args rn a');
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
let page_fields_injective (rn:renderers) (uo:string -> time_unit) (t1 t2:option string) (q1 q2:query) (a1 a2:arguments)
  : Lemma (requires shape_premises rn /\ units_named uo a1 /\ units_named uo a2 /\
                    page_fields rn t1 q1 a1 == page_fields rn t2 q2 a2)
          (ensures t1 == t2 /\ q1.q_where == q2.q_where /\ q1.q_order_by == q2.q_order_by /\ a1 == a2)
  = match t1, t2 with
    | None, None -> key_fields_injective rn uo q1 q2 a1 a2
    | Some _, Some _ -> key_fields_injective rn uo q1 q2 a1 a2
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
  (uo:string -> time_unit) (q q':query) (a a':arguments) (tok tok':option string)
  : Lemma (requires key_premises reveal e t rn /\ units_named uo a /\ units_named uo a' /\
                    canonical_page rn tok q (sort_args rn a) == canonical_page rn tok' q' (sort_args rn a'))
          (ensures tok == tok' /\ q.q_where == q'.q_where /\ q.q_order_by == q'.q_order_by /\
                   sort_args rn a == sort_args rn a' /\
                   (forall (x:(string & cell)). mem_arg x a = mem_arg x a')) =
  sort_units_named uo rn a;
  sort_units_named uo rn a';
  fields_injective reveal e t rn (page_fields rn tok q (sort_args rn a)) (page_fields rn tok' q' (sort_args rn a'));
  page_fields_injective rn uo tok tok' q q' (sort_args rn a) (sort_args rn a');
  let aux (x:(string & cell)) : Lemma (mem_arg x a = mem_arg x a') =
    sort_mem rn a x; sort_mem rn a' x
  in
  FStar.Classical.forall_intro aux

(* The law a host reads: distinct tokens give distinct pre-images, whatever the declarations and
   the arguments. *)
let distinct_tokens_distinct_preimages (#sym:eqtype) (reveal:string -> list sym) (e t:sym)
  (rn:renderers) (uo:string -> time_unit) (q q':query) (a a':arguments) (tok tok':option string)
  : Lemma (requires key_premises reveal e t rn /\ units_named uo a /\ units_named uo a' /\ ~(tok == tok'))
          (ensures ~(canonical_page rn tok q (sort_args rn a) == canonical_page rn tok' q' (sort_args rn a')))
  = FStar.Classical.move_requires (invocation_key_page_injective reveal e t rn uo q q' a a' tok) tok'

(* Phase 398 — and the same for the shape: a declaration filtered or ordered differently gives a
   distinct pre-image on every page, whatever the arguments and the tokens. *)
let distinct_shapes_distinct_preimages (#sym:eqtype) (reveal:string -> list sym) (e t:sym)
  (rn:renderers) (uo:string -> time_unit) (q q':query) (a a':arguments) (tok tok':option string)
  : Lemma (requires key_premises reveal e t rn /\ units_named uo a /\ units_named uo a' /\
                    ~(q.q_where == q'.q_where /\ q.q_order_by == q'.q_order_by))
          (ensures ~(canonical_page rn tok q (sort_args rn a) == canonical_page rn tok' q' (sort_args rn a')))
  = FStar.Classical.move_requires (invocation_key_page_injective reveal e t rn uo q q' a a' tok) tok'

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
   TWINS (Phase 309) — the extractor premise, sampled at this model. What a twin is, why it
   is here and how the list is evaluated are `Twin`'s (Phase 504), which every model opens.
   ====================================================================================== *)

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
  { tname = "validate-params-widens-a-seconds-timestamp-into-a-nanoseconds-param";
    tholds = (fun () ->
      validate_params
        ({ twin_query with q_params = [ { p_name = "region"; p_type = TimestampType Nanoseconds; p_required = true } ] })
        [ ("region", Timestamp Seconds "2026-01-01T00:00:00Z") ] = Ok ()
      && widens (TimestampType Milliseconds) (TimestampType Seconds) = false) };
  { tname = "register-admits-a-coarser-timestamp-literal-and-refuses-a-finer";
    tholds = (fun () ->
      register ({ twin_query with q_schema = [ ("t", TimestampType Milliseconds) ];
                                  q_where = [ AtLeast "t" (Timestamp Seconds "2026-01-01T00:00:00Z") ] }) empty
      = Ok ({ queries = [ { twin_query with q_schema = [ ("t", TimestampType Milliseconds) ];
                                            q_where = [ AtLeast "t" (Timestamp Seconds "2026-01-01T00:00:00Z") ] } ] })
      && register ({ twin_query with q_schema = [ ("t", TimestampType Milliseconds) ];
                                     q_where = [ AtLeast "t" (Timestamp Microseconds "2026-01-01T00:00:00.000001Z") ] }) empty
         = Error (PredicateTypeMismatch "t" (TimestampType Milliseconds) (TimestampType Microseconds))) };
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

(* ======================================================================================
   19. THE QUERY CODEC (Phase 455) — `QueryCodec` and the `QueryErrorWire` / `QueryShape` codecs it
       composes, clause for clause at the `JVal`: the DECLARATION (`queryJson` / `queryOf`, with
       its parameters, effect, filter predicates and order keys, and THE admission gate the registry
       runs, raised at the member at fault), the RESULT PAGE (`resultJson` / `resultOf`), the async
       RESULT (`deferredResultJson` / `deferredResultOf`, the capability seam's envelope) and the
       typed REFUSAL (all twenty `QueryError` cases as `wire_query_error`, a decode refusal and a
       predicate inside them). For every pair: decode after encode, EXACTLY (`query_roundtrip`,
       `result_roundtrip`, `deferred_result_roundtrip`, `query_error_roundtrip`), and every refusal
       a reader answers RESOLVES in the document it read (`…_refusals_resolve`).

       PREMISE CHECKED FIRST: like the capability codec, this one is written over `JVal` with the
       typed decode layer's combinators, not built from the `Codec` blocks, so it is proved
       directly, over `Capability.fst` section 18's refusal law (module `C`).

       THE COLUMN CODEC IS A PARAMETER (`column_codec`): a result column's schema entry, the
       `Source`, a page's rows and a predicate literal are `ColumnCodec`'s to write and read, and
       production maps every column-codec refusal to one `OutOfRange` at the member it was reading.
       Its round trips are `column_codec_laws`, assumed here and proved in the column strand's model
       (`query-codec-column-premises`). A schema entry is the model's `(name, type)` pair: the
       field metadata `ColumnCodec.fieldJson` also carries is the column codec's.
   ====================================================================================== *)

(* ---- the wire vocabulary the codec reads beyond this model's types ---- *)

(* F#: `ColumnType.tag`. *)
let column_tag (t:column_type) : Tot string =
  match t with
  | IntType -> "int"
  | FloatType -> "float"
  | BoolType -> "bool"
  | StringType -> "string"
  | DateType -> "date"
  | TimestampType Seconds -> "timestamp"
  | TimestampType Milliseconds -> "timestamp_ms"
  | TimestampType Microseconds -> "timestamp_us"
  | TimestampType Nanoseconds -> "timestamp_ns"
  | DecimalType -> "decimal"

(* F#: `ColumnType.all |> List.map (fun t -> ColumnType.tag t, t)` looked up — `QueryShape.colTypeOf`'s
   table. *)
let column_of_tag (s:string) : Tot (option column_type) =
  if s = "int" then Some IntType
  else if s = "float" then Some FloatType
  else if s = "bool" then Some BoolType
  else if s = "string" then Some StringType
  else if s = "date" then Some DateType
  else if s = "timestamp" then Some (TimestampType Seconds)
  else if s = "decimal" then Some DecimalType
  else if s = "timestamp_ms" then Some (TimestampType Milliseconds)
  else if s = "timestamp_us" then Some (TimestampType Microseconds)
  else if s = "timestamp_ns" then Some (TimestampType Nanoseconds)
  else None

let column_tag_roundtrip (t:column_type) : Lemma (column_of_tag (column_tag t) == Some t) = ()

(* F#: `QueryShape.tagged` — a string from a closed set; a miss is `UnknownTag`, raised at the value
   by `andThen` (Phase 453). *)
let tagged_of (#a:Type) (look:string -> option a) : C.decoder a =
  fun el ->
    match C.d_str el with
    | C.Error e -> C.Error e
    | C.Ok s -> (match look s with Some v -> C.Ok v | None -> C.Error (C.refuse C.UnknownTag))

let tagged_sound (#a:Type) (look:string -> option a) : Lemma (C.sound (tagged_of look)) =
  C.sound_intro (tagged_of look) (fun x -> ())

(* F#: `QueryShape.colTypeOf`. *)
let col_type_of_j : C.decoder column_type = tagged_of column_of_tag

(* F#: `EffectCodec.hostTag` / `hostDecoder`, over this model's effect class. *)
let host_tag (h:host_effect) : Tot string =
  match h with
  | Pure -> "pure"
  | ReadsHost -> "readsHost"
  | WritesHost -> "writesHost"

let host_of_tag (s:string) : Tot (option host_effect) =
  if s = "pure" then Some Pure
  else if s = "readsHost" then Some ReadsHost
  else if s = "writesHost" then Some WritesHost
  else None

(* F#: `EffectCodec.determinismDecoder` — the canonical label only, read back by the label table. *)
let det_of_tag (s:string) : Tot (option determinism_source) =
  if s = "deterministic" then Some ({ has_clock = false; has_random = false; has_network = false })
  else if s = "network" then Some ({ has_clock = false; has_random = false; has_network = true })
  else if s = "random" then Some ({ has_clock = false; has_random = true; has_network = false })
  else if s = "random+network" then Some ({ has_clock = false; has_random = true; has_network = true })
  else if s = "clock" then Some ({ has_clock = true; has_random = false; has_network = false })
  else if s = "clock+network" then Some ({ has_clock = true; has_random = false; has_network = true })
  else if s = "clock+random" then Some ({ has_clock = true; has_random = true; has_network = false })
  else if s = "clock+random+network" then Some ({ has_clock = true; has_random = true; has_network = true })
  else None

let det_tag_roundtrip (d:determinism_source) : Lemma (det_of_tag (determinism_tag d) == Some d) = ()

(* F#: `EffectCodec.toJson` / `decoder`. *)
let effect_json (e:effect_class) : Tot C.jval =
  C.JObj [("host", C.JStr (host_tag e.host)); ("determinism", C.JStr (determinism_tag e.determinism))]

let effect_of_j : C.decoder effect_class =
  C.both (C.field "host" (tagged_of host_of_tag)) (C.field "determinism" (tagged_of det_of_tag))
         (fun h d -> { host = h; determinism = d })

let effect_roundtrip (e:effect_class) : Lemma (effect_of_j (effect_json e) == C.Ok e) =
  det_tag_roundtrip e.determinism

let effect_sound () : Lemma (C.sound effect_of_j) =
  tagged_sound host_of_tag; tagged_sound det_of_tag;
  C.field_sound "host" (tagged_of host_of_tag); C.field_sound "determinism" (tagged_of det_of_tag);
  C.both_sound (C.field "host" (tagged_of host_of_tag)) (C.field "determinism" (tagged_of det_of_tag))
               (fun h d -> { host = h; determinism = d })

(* ---- the column codec, as the parameter it is here ---- *)

(* The four places the query codec hands a value to `ColumnCodec`, the column strand's codec (proved
   in its own model, `WireColumn.fst`): a result column's schema entry, the declaration's `Source`, a
   result page's rows, and a predicate literal carried as a one-cell column. Each is a writer and a
   reader that answers a value or nothing — production maps every column-codec refusal to one
   `OutOfRange` at the member it was reading, so nothing the column codec says reaches the refusal
   beyond THAT it refused. *)
noeq type column_codec (r:Type) = {
  field_json:  (string & column_type) -> C.jval;
  field_of:    C.jval -> option (string & column_type);
  source_json: string -> C.jval;
  source_of:   C.jval -> option string;
  rows_json:   r -> C.jval;
  rows_of:     C.jval -> option r;
  lit_json:    column_type -> cell -> C.jval;
  lit_of:      string -> column_type -> C.jval -> option cell
}

(* What the query codec owes the column codec: each reader takes back what its writer wrote, and a
   literal of a type reads back at that type — the column strand's round trips, which `WireColumn.fst`
   proves and this model assumes (`query-codec-column-premises`). *)
let column_codec_laws (#r:Type) (cc:column_codec r) : prop =
  (forall (f:(string & column_type)). cc.field_of (cc.field_json f) == Some f) /\
  (forall (s:string). cc.source_of (cc.source_json s) == Some s) /\
  (forall (x:r). cc.rows_of (cc.rows_json x) == Some x) /\
  (forall (c:string) (v:cell) (ty:column_type). cell_type v == Some ty ==> cc.lit_of c ty (cc.lit_json ty v) == Some v)

(* A column-codec refusal, as the query codec raises it: `OutOfRange` at the value read. *)
let column_read (#a:Type) (rd:C.jval -> option a) : C.decoder a =
  fun el -> match rd el with Some x -> C.Ok x | None -> C.Error (C.refuse C.OutOfRange)

let column_read_sound (#a:Type) (rd:C.jval -> option a) : Lemma (C.sound (column_read rd)) =
  C.sound_intro (column_read rd) (fun x -> ())

(* ---- a parameter: `paramJson` / `paramOf` ---- *)

let param_json (p:query_param) : Tot C.jval =
  C.JObj [("name", C.JStr p.p_name); ("type", C.JStr (column_tag p.p_type)); ("required", C.JBool p.p_required)]

(* F#: `QueryCodec.paramOf` — its reworded `required` sentence is prose, and dropped. *)
let param_of_j : C.decoder query_param =
  C.both (C.field "name" C.d_str)
         (C.both (C.field "type" col_type_of_j) (C.field "required" C.d_bool) (fun t r -> (t, r)))
         (fun n (tr:(column_type & bool)) -> { p_name = n; p_type = fst tr; p_required = snd tr })

(* ---- a predicate and an order key: `QueryShape.predicateJson` / `predicateDecoder`,
        `sortKeyJson` / `sortKeyDecoder` ---- *)

let predicate_tag (p:predicate) : Tot string =
  match p with
  | EqualTo _ _ -> "equalTo"
  | GreaterThan _ _ -> "greaterThan"
  | AtLeast _ _ -> "atLeast"
  | LessThan _ _ -> "lessThan"
  | AtMost _ _ -> "atMost"
  | Contains _ _ -> "contains"
  | IsNull _ -> "isNull"
  | IsNotNull _ -> "isNotNull"

(* F#: `QueryShape.predicateJson` — a comparison's literal written at its own type
   (`Cell.typeOf v |> Option.defaultValue StringType`) by the column codec. *)
let predicate_json (#r:Type) (cc:column_codec r) (p:predicate) : Tot C.jval =
  let col = predicate_column p in
  match p with
  | Contains _ text -> C.JObj [("$type", C.JStr "contains"); ("column", C.JStr col); ("text", C.JStr text)]
  | IsNull _ -> C.JObj [("$type", C.JStr "isNull"); ("column", C.JStr col)]
  | IsNotNull _ -> C.JObj [("$type", C.JStr "isNotNull"); ("column", C.JStr col)]
  | _ ->
    (match predicate_literal p with
     | Some v ->
       let ty = (match cell_type v with Some t -> t | None -> StringType) in
       C.JObj [("$type", C.JStr (predicate_tag p)); ("column", C.JStr col);
               ("type", C.JStr (column_tag ty)); ("value", cc.lit_json ty v)]
     | None -> C.JObj [("$type", C.JStr (predicate_tag p)); ("column", C.JStr col)])

(* F#: `predicateDecoder`'s `compared` — the column, the stated type, then the literal read at that
   type; a value no column of the type carries is `OutOfRange` at `value`. *)
let compared (#r:Type) (cc:column_codec r) (make:string -> cell -> predicate) : C.decoder predicate =
  fun el ->
    match C.field "column" C.d_str el with
    | C.Error e -> C.Error e
    | C.Ok c ->
      match C.field "type" col_type_of_j el with
      | C.Error e -> C.Error e
      | C.Ok ty -> C.field "value" (column_read (cc.lit_of c ty)) el |> (fun res ->
          match res with C.Ok v -> C.Ok (make c v) | C.Error e -> C.Error e)

let predicate_cases (#r:Type) (cc:column_codec r) : list (string & C.decoder predicate) =
  [("equalTo", compared cc EqualTo);
   ("greaterThan", compared cc GreaterThan);
   ("atLeast", compared cc AtLeast);
   ("lessThan", compared cc LessThan);
   ("atMost", compared cc AtMost);
   ("contains", C.both (C.field "column" C.d_str) (C.field "text" C.d_str) (fun c t -> Contains c t));
   ("isNull", C.d_map IsNull (C.field "column" C.d_str));
   ("isNotNull", C.d_map IsNotNull (C.field "column" C.d_str))]

(* F#: `QueryShape.predicateDecoder`. *)
let predicate_of_j (#r:Type) (cc:column_codec r) : C.decoder predicate = C.tag_dispatch (predicate_cases cc)

let direction_tag (d:sort_direction) : Tot string =
  match d with Ascending -> "ascending" | Descending -> "descending"

let direction_of_tag (s:string) : Tot (option sort_direction) =
  if s = "ascending" then Some Ascending else if s = "descending" then Some Descending else None

let sort_key_json (k:sort_key) : Tot C.jval =
  C.JObj [("column", C.JStr k.k_column); ("direction", C.JStr (direction_tag k.k_direction))]

let sort_key_of_j : C.decoder sort_key =
  C.both (C.field "column" C.d_str) (C.field "direction" (tagged_of direction_of_tag))
         (fun c d -> { k_column = c; k_direction = d })

(* A predicate the codec carries: its literal, if it has one, is not `Null` — the literal admission
   admits (`IllFormedLiteral` refuses a `Null`). *)
let literal_carried (p:predicate) : Tot bool =
  match predicate_literal p with
  | Some Null -> false
  | _ -> true

#push-options "--fuel 9 --ifuel 1"
let predicate_roundtrip (#r:Type) (cc:column_codec r) (p:predicate)
  : Lemma (requires column_codec_laws cc /\ literal_carried p)
          (ensures predicate_of_j cc (predicate_json cc p) == C.Ok p)
  = match predicate_literal p with
    | Some v -> (match cell_type v with Some t -> column_tag_roundtrip t | None -> ())
    | None -> ()
#pop-options

let sort_key_roundtrip (k:sort_key) : Lemma (sort_key_of_j (sort_key_json k) == C.Ok k) = ()

(* ---- the declaration: `queryJson` / `queryOf` ---- *)

let rec params_json (ps:list query_param) : Tot (list C.jval) =
  match ps with [] -> [] | p :: t -> param_json p :: params_json t

let rec fields_json (#r:Type) (cc:column_codec r) (s:list (string & column_type)) : Tot (list C.jval) =
  match s with [] -> [] | f :: t -> cc.field_json f :: fields_json cc t

let rec preds_json (#r:Type) (cc:column_codec r) (w:list predicate) : Tot (list C.jval) =
  match w with [] -> [] | p :: t -> predicate_json cc p :: preds_json cc t

let rec keys_json (o:list sort_key) : Tot (list C.jval) =
  match o with [] -> [] | k :: t -> sort_key_json k :: keys_json t

(* F#: `optIntJson`. *)
let opt_int_json (name:string) (v:option int) : Tot (list (string & C.jval)) =
  match v with Some n -> [(name, C.JInt n)] | None -> []

(* The members `queryJson` writes after the six it always writes. *)
let query_tail (#r:Type) (cc:column_codec r) (q:query) : Tot (list (string & C.jval)) =
  app (opt_int_json "timeoutMs" q.q_timeout_ms)
      (app (opt_int_json "pageSize" q.q_page_size)
           (app (match q.q_where with [] -> [] | _ -> [("where", C.JArr (preds_json cc q.q_where))])
                (match q.q_order_by with [] -> [] | _ -> [("orderBy", C.JArr (keys_json q.q_order_by))])))

(* F#: `QueryCodec.queryJson`. *)
let query_json (#r:Type) (cc:column_codec r) (q:query) : Tot C.jval =
  C.JObj (("$type", C.JStr "query") :: ("id", C.JStr q.q_id) :: ("params", C.JArr (params_json q.q_params)) ::
          ("resultSchema", C.JArr (fields_json cc q.q_schema)) :: ("effect", effect_json q.q_effect) ::
          ("source", cc.source_json q.q_source) :: query_tail cc q)

(* F#: `QueryShape.whereFault` — the first predicate's fault WITH its index (the model's
   `where_fault` is it without). *)
let rec where_fault_at (s:list (string & column_type)) (i:nat) (w:list predicate)
  : Tot (option (nat & query_error)) (decreases w) =
  match w with
  | [] -> None
  | p :: t -> (match predicate_fault s p with Some e -> Some (i, e) | None -> where_fault_at s (i + 1) t)

(* F#: `QueryShape.orderFault`, with its index. *)
let rec order_fault_at (s:list (string & column_type)) (seen:list string) (i:nat) (o:list sort_key)
  : Tot (option (nat & query_error)) (decreases o) =
  match o with
  | [] -> None
  | k :: t ->
    if not (mem k.k_column (keys s)) then Some (i, UnknownColumn k.k_column (keys s))
    else if mem k.k_column seen then Some (i, DuplicateSortColumn k.k_column)
    else order_fault_at s (k.k_column :: seen) (i + 1) t

(* F#: `QueryRegistry.admissionFaultAt` — `admissionFault`'s refusal with the path to the member at
   fault, which the declaration reader reports it at. *)
let admission_fault_at (q:query) : Tot (option (list C.path_seg & query_error)) =
  match repeated [] (param_names q.q_params) with
  | d :: _ -> Some ([C.Key "params"], DuplicateParam d)
  | [] ->
    match where_fault_at q.q_schema 0 q.q_where with
    | Some (i, e) -> Some ([C.Key "where"; C.Index i], e)
    | None ->
      match order_fault_at q.q_schema [] 0 q.q_order_by with
      | Some (i, e) -> Some ([C.Key "orderBy"; C.Index i], e)
      | None -> None

let rec where_fault_at_agrees (s:list (string & column_type)) (i:nat) (w:list predicate)
  : Lemma (ensures (match where_fault_at s i w with
                    | Some (j, e) -> where_fault s w == Some e /\ j < i + len w
                    | None -> where_fault s w == None)) (decreases w)
  = match w with [] -> () | _ :: t -> where_fault_at_agrees s (i + 1) t

let rec order_fault_at_agrees (s:list (string & column_type)) (seen:list string) (i:nat) (o:list sort_key)
  : Lemma (ensures (match order_fault_at s seen i o with
                    | Some (j, e) -> order_fault s seen o == Some e /\ j < i + len o
                    | None -> order_fault s seen o == None)) (decreases o)
  = match o with [] -> () | k :: t -> order_fault_at_agrees s (k.k_column :: seen) (i + 1) t

(* The reader's refusal is the registry's: `admission_fault_at` refuses exactly what
   `admission_fault` — `register`'s gate — refuses, with the same error. *)
let admission_fault_at_agrees (q:query)
  : Lemma (match admission_fault_at q with
           | Some (_, e) -> admission_fault q == Some e
           | None -> admission_fault q == None)
  = where_fault_at_agrees q.q_schema 0 q.q_where;
    order_fault_at_agrees q.q_schema [] 0 q.q_order_by

let query_tag_of : C.decoder unit = tagged_of (fun s -> if s = "query" then Some () else None)

(* F#: `QueryCodec.queryOf`, member by member in production's order, the column codec's refusal of
   `source` at `source`, and THE admission gate the registry runs. *)
let query_of_j (#r:Type) (cc:column_codec r) : C.decoder query =
  fun el ->
    match C.field "$type" query_tag_of el with
    | C.Error e -> C.Error e
    | C.Ok () ->
    match C.field "id" C.d_str el with
    | C.Error e -> C.Error e
    | C.Ok id ->
    match C.field "params" (C.d_list param_of_j) el with
    | C.Error e -> C.Error e
    | C.Ok ps ->
    match C.field "resultSchema" (C.d_list (column_read cc.field_of)) el with
    | C.Error e -> C.Error e
    | C.Ok sch ->
    match C.field "effect" effect_of_j el with
    | C.Error e -> C.Error e
    | C.Ok eff ->
    match C.opt_field "timeoutMs" C.d_int el with
    | C.Error e -> C.Error e
    | C.Ok tmo ->
    match C.opt_field "pageSize" C.d_int el with
    | C.Error e -> C.Error e
    | C.Ok pg ->
    match C.opt_field "where" (C.d_list (predicate_of_j cc)) el with
    | C.Error e -> C.Error e
    | C.Ok wh ->
    match C.opt_field "orderBy" (C.d_list sort_key_of_j) el with
    | C.Error e -> C.Error e
    | C.Ok ob ->
    match C.field "source" (column_read cc.source_of) el with
    | C.Error e -> C.Error e
    | C.Ok src ->
      let q = { q_id = id; q_params = ps; q_schema = sch; q_effect = eff; q_source = src;
                q_timeout_ms = tmo; q_page_size = pg;
                q_where = (match wh with Some w -> w | None -> []);
                q_order_by = (match ob with Some o -> o | None -> []) } in
      match admission_fault_at q with
      | Some (p, _) -> C.Error ({ C.d_code = C.OutOfRange; C.d_path = p })
      | None -> C.Ok q

let rec params_roundtrip (i:nat) (ps:list query_param)
  : Lemma (ensures C.list_from param_of_j i (params_json ps) == C.Ok ps) (decreases ps)
  = match ps with [] -> () | p :: t -> column_tag_roundtrip p.p_type; params_roundtrip (i + 1) t

let rec fields_roundtrip (#r:Type) (cc:column_codec r) (i:nat) (s:list (string & column_type))
  : Lemma (requires column_codec_laws cc)
          (ensures C.list_from (column_read cc.field_of) i (fields_json cc s) == C.Ok s) (decreases s)
  = match s with [] -> () | _ :: t -> fields_roundtrip cc (i + 1) t

let rec all_carried (w:list predicate) : Tot bool =
  match w with [] -> true | p :: t -> literal_carried p && all_carried t

let rec preds_roundtrip (#r:Type) (cc:column_codec r) (i:nat) (w:list predicate)
  : Lemma (requires column_codec_laws cc /\ all_carried w)
          (ensures C.list_from (predicate_of_j cc) i (preds_json cc w) == C.Ok w) (decreases w)
  = match w with [] -> () | p :: t -> predicate_roundtrip cc p; preds_roundtrip cc (i + 1) t

let rec keys_roundtrip (i:nat) (o:list sort_key)
  : Lemma (ensures C.list_from sort_key_of_j i (keys_json o) == C.Ok o) (decreases o)
  = match o with [] -> () | k :: t -> sort_key_roundtrip k; keys_roundtrip (i + 1) t

(* The optional members, looked up in what `queryJson` writes after its fixed six. *)
let query_tail_members (#r:Type) (cc:column_codec r) (q:query)
  : Lemma (C.assoc "timeoutMs" (query_tail cc q) == (match q.q_timeout_ms with Some n -> Some (C.JInt n) | None -> None) /\
           C.assoc "pageSize" (query_tail cc q) == (match q.q_page_size with Some n -> Some (C.JInt n) | None -> None) /\
           C.assoc "where" (query_tail cc q) ==
             (match q.q_where with [] -> None | _ -> Some (C.JArr (preds_json cc q.q_where))) /\
           C.assoc "orderBy" (query_tail cc q) ==
             (match q.q_order_by with [] -> None | _ -> Some (C.JArr (keys_json q.q_order_by))))
  = let w = (match q.q_where with [] -> [] | _ -> [("where", C.JArr (preds_json cc q.q_where))]) in
    let o = (match q.q_order_by with [] -> [] | _ -> [("orderBy", C.JArr (keys_json q.q_order_by))]) in
    let pg = opt_int_json "pageSize" q.q_page_size in
    let tm = opt_int_json "timeoutMs" q.q_timeout_ms in
    C.assoc_app "timeoutMs" tm (app pg (app w o)); C.assoc_app "timeoutMs" pg (app w o); C.assoc_app "timeoutMs" w o;
    C.assoc_app "pageSize" tm (app pg (app w o)); C.assoc_app "pageSize" pg (app w o); C.assoc_app "pageSize" w o;
    C.assoc_app "where" tm (app pg (app w o)); C.assoc_app "where" pg (app w o); C.assoc_app "where" w o;
    C.assoc_app "orderBy" tm (app pg (app w o)); C.assoc_app "orderBy" pg (app w o); C.assoc_app "orderBy" w o

(* THE DECLARATION ROUND TRIP (Phase 455), `query_roundtrip`. F#: `QueryCodec.queryOf` after
   `queryJson`, EXACTLY, for every declaration whose filter literals are carried, over any column
   codec that reads back what it writes: the registry's admission refusal, `OutOfRange` at the member
   at fault (`params`, `where[i]`, `orderBy[i]`), where `admissionFault` refuses the declaration; the
   declaration itself otherwise. *)
#push-options "--fuel 8 --ifuel 2 --z3rlimit 80"
let query_roundtrip (#r:Type) (cc:column_codec r) (q:query)
  : Lemma (requires column_codec_laws cc /\ all_carried q.q_where)
          (ensures query_of_j cc (query_json cc q) ==
                   (match admission_fault_at q with
                    | Some (p, _) -> C.Error ({ C.d_code = C.OutOfRange; C.d_path = p })
                    | None -> C.Ok q))
  = params_roundtrip 0 q.q_params;
    fields_roundtrip cc 0 q.q_schema;
    effect_roundtrip q.q_effect;
    preds_roundtrip cc 0 q.q_where;
    keys_roundtrip 0 q.q_order_by;
    query_tail_members cc q
#pop-options

(* ... the identity exactly on the declarations `register`'s admission admits. *)
let query_roundtrip_identity (#r:Type) (cc:column_codec r) (q:query)
  : Lemma (requires column_codec_laws cc /\ all_carried q.q_where /\ admission_fault q == None)
          (ensures query_of_j cc (query_json cc q) == C.Ok q)
  = admission_fault_at_agrees q;
    query_roundtrip cc q

(* ---- the declaration reader's refusals resolve ---- *)

let rec list_from_len (#a:Type) (d:C.decoder a) (i:nat) (xs:list C.jval)
  : Lemma (ensures (match C.list_from d i xs with C.Ok ys -> len ys == len xs | C.Error _ -> True)) (decreases xs)
  = match xs with [] -> () | _ :: t -> list_from_len d (i + 1) t

(* An optional list member read as present: its value is an array of as many items as were read. *)
let opt_list_read (#a:Type) (name:string) (d:C.decoder a) (fs:list (string & C.jval))
  : Lemma (match C.opt_field name (C.d_list d) (C.JObj fs) with
           | C.Ok (Some ys) -> (match C.assoc name fs with
                                | Some (C.JArr xs) -> len xs == len ys
                                | _ -> False)
           | _ -> True)
  = match C.assoc name fs with
    | Some (C.JArr xs) -> list_from_len d 0 xs
    | _ -> ()

let param_sound () : Lemma (C.sound param_of_j) =
  C.str_sound (); C.bool_sound (); tagged_sound column_of_tag;
  C.field_sound "name" C.d_str; C.field_sound "type" col_type_of_j; C.field_sound "required" C.d_bool;
  C.both_sound (C.field "type" col_type_of_j) (C.field "required" C.d_bool) (fun t r -> (t, r));
  C.both_sound (C.field "name" C.d_str)
               (C.both (C.field "type" col_type_of_j) (C.field "required" C.d_bool) (fun t r -> (t, r)))
               (fun n (tr:(column_type & bool)) -> { p_name = n; p_type = fst tr; p_required = snd tr })

let compared_sound (#r:Type) (cc:column_codec r) (make:string -> cell -> predicate) : Lemma (C.sound (compared cc make)) =
  C.str_sound (); tagged_sound column_of_tag;
  C.sound_intro (compared cc make) (fun x ->
    C.field_good "column" C.d_str x; C.field_good "type" col_type_of_j x;
    match C.field "column" C.d_str x, C.field "type" col_type_of_j x with
    | C.Ok c, C.Ok ty -> column_read_sound (cc.lit_of c ty); C.field_good "value" (column_read (cc.lit_of c ty)) x
    | _, _ -> ())

#push-options "--fuel 9 --ifuel 1"
let predicate_sound (#r:Type) (cc:column_codec r) : Lemma (C.sound (predicate_of_j cc)) =
  C.str_sound ();
  compared_sound cc EqualTo; compared_sound cc GreaterThan; compared_sound cc AtLeast;
  compared_sound cc LessThan; compared_sound cc AtMost;
  C.field_sound "column" C.d_str; C.field_sound "text" C.d_str;
  C.both_sound (C.field "column" C.d_str) (C.field "text" C.d_str) (fun c t -> Contains c t);
  C.map_sound IsNull (C.field "column" C.d_str); C.map_sound IsNotNull (C.field "column" C.d_str);
  C.tag_dispatch_sound (predicate_cases cc)
#pop-options

let sort_key_sound () : Lemma (C.sound sort_key_of_j) =
  C.str_sound (); tagged_sound direction_of_tag;
  C.field_sound "column" C.d_str; C.field_sound "direction" (tagged_of direction_of_tag);
  C.both_sound (C.field "column" C.d_str) (C.field "direction" (tagged_of direction_of_tag))
               (fun c d -> { k_column = c; k_direction = d })

(* The admission refusal's index names an item the document holds. *)
let where_index_resolves (#r:Type) (cc:column_codec r) (sch:list (string & column_type)) (fs:list (string & C.jval))
  (wh:option (list predicate))
  : Lemma (requires C.opt_field "where" (C.d_list (predicate_of_j cc)) (C.JObj fs) == C.Ok wh)
          (ensures (match where_fault_at sch 0 (match wh with Some w -> w | None -> []) with
                    | Some (i, _) -> Some? (C.resolve_j [C.Key "where"; C.Index i] (C.JObj fs))
                    | None -> True))
  = opt_list_read "where" (predicate_of_j cc) fs;
    let w = (match wh with Some w -> w | None -> []) in
    where_fault_at_agrees sch 0 w;
    match where_fault_at sch 0 w, C.assoc "where" fs with
    | Some (i, _), Some (C.JArr xs) -> C.nth_some i xs
    | _, _ -> ()

let order_index_resolves (sch:list (string & column_type)) (fs:list (string & C.jval)) (ob:option (list sort_key))
  : Lemma (requires C.opt_field "orderBy" (C.d_list sort_key_of_j) (C.JObj fs) == C.Ok ob)
          (ensures (match order_fault_at sch [] 0 (match ob with Some o -> o | None -> []) with
                    | Some (i, _) -> Some? (C.resolve_j [C.Key "orderBy"; C.Index i] (C.JObj fs))
                    | None -> True))
  = opt_list_read "orderBy" sort_key_of_j fs;
    let o = (match ob with Some o -> o | None -> []) in
    order_fault_at_agrees sch [] 0 o;
    match order_fault_at sch [] 0 o, C.assoc "orderBy" fs with
    | Some (i, _), Some (C.JArr xs) -> C.nth_some i xs
    | _, _ -> ()

#push-options "--z3rlimit 120 --fuel 2 --ifuel 2"
let query_good (#r:Type) (cc:column_codec r) (x:C.jval) : Lemma (C.good_res x (query_of_j cc x)) =
  C.str_sound (); C.int_sound (); tagged_sound (fun s -> if s = "query" then Some () else None);
  param_sound (); C.list_sound param_of_j; column_read_sound cc.field_of; C.list_sound (column_read cc.field_of);
  effect_sound (); predicate_sound cc; C.list_sound (predicate_of_j cc); sort_key_sound (); C.list_sound sort_key_of_j;
  column_read_sound cc.source_of;
  C.field_good "$type" query_tag_of x; C.field_good "id" C.d_str x; C.field_good "params" (C.d_list param_of_j) x;
  C.field_good "resultSchema" (C.d_list (column_read cc.field_of)) x; C.field_good "effect" effect_of_j x;
  C.opt_field_good "timeoutMs" C.d_int x; C.opt_field_good "pageSize" C.d_int x;
  C.opt_field_good "where" (C.d_list (predicate_of_j cc)) x; C.opt_field_good "orderBy" (C.d_list sort_key_of_j) x;
  C.field_good "source" (column_read cc.source_of) x;
  match x with
  | C.JObj fs ->
    (match C.field "resultSchema" (C.d_list (column_read cc.field_of)) x,
           C.opt_field "where" (C.d_list (predicate_of_j cc)) x,
           C.opt_field "orderBy" (C.d_list sort_key_of_j) x with
     | C.Ok sch, C.Ok wh, C.Ok ob -> where_index_resolves cc sch fs wh; order_index_resolves sch fs ob
     | _, _, _ -> ())
  | _ -> ()
#pop-options

let query_refusals_resolve (#r:Type) (cc:column_codec r) : Lemma (C.sound (query_of_j cc)) =
  C.sound_intro (query_of_j cc) (fun x -> query_good cc x)

(* ---- a result page: `resultJson` / `resultOf` ---- *)

(* F#: `QueryResult`, its rows the column codec's embedded table, a carrier here. *)
type query_result (r:Type) = { qr_rows: r; qr_page_num: int; qr_total: option int; qr_next: option string }

(* The members `resultJson` writes after its fixed three. *)
let result_tail (#r:Type) (qr:query_result r) : Tot (list (string & C.jval)) =
  app (opt_int_json "totalRowCount" qr.qr_total)
      (match qr.qr_next with Some t -> [("nextPageToken", C.JStr t)] | None -> [])

(* F#: `QueryCodec.resultJson`. *)
let result_json (#r:Type) (cc:column_codec r) (qr:query_result r) : Tot C.jval =
  C.JObj (("$type", C.JStr "queryResult") :: ("rows", cc.rows_json qr.qr_rows) :: ("pageNum", C.JInt qr.qr_page_num) ::
          result_tail qr)

let result_tag_of : C.decoder unit = tagged_of (fun s -> if s = "queryResult" then Some () else None)

(* F#: `QueryCodec.resultOf` — a `rows` the column codec refuses, or reads as a `Ref`, is
   `OutOfRange` at `rows` (the reader answers no embedded table: `rows_of` is `None`). *)
let result_of_j (#r:Type) (cc:column_codec r) : C.decoder (query_result r) =
  fun el ->
    match C.field "$type" result_tag_of el with
    | C.Error e -> C.Error e
    | C.Ok () ->
    match C.field "rows" (column_read cc.rows_of) el with
    | C.Error e -> C.Error e
    | C.Ok rows ->
    match C.field "pageNum" C.d_int el with
    | C.Error e -> C.Error e
    | C.Ok pn ->
    match C.opt_field "totalRowCount" C.d_int el with
    | C.Error e -> C.Error e
    | C.Ok trc ->
    match C.opt_field "nextPageToken" C.d_str el with
    | C.Error e -> C.Error e
    | C.Ok tok -> C.Ok ({ qr_rows = rows; qr_page_num = pn; qr_total = trc; qr_next = tok })

(* THE RESULT ROUND TRIP (Phase 455), `result_roundtrip`: every result page reads back as itself. *)
let result_roundtrip (#r:Type) (cc:column_codec r) (qr:query_result r)
  : Lemma (requires column_codec_laws cc) (ensures result_of_j cc (result_json cc qr) == C.Ok qr)
  = let tl = (match qr.qr_next with Some t -> [("nextPageToken", C.JStr t)] | None -> []) in
    C.assoc_app "totalRowCount" (opt_int_json "totalRowCount" qr.qr_total) tl;
    C.assoc_app "nextPageToken" (opt_int_json "totalRowCount" qr.qr_total) tl

let result_good (#r:Type) (cc:column_codec r) (x:C.jval) : Lemma (C.good_res x (result_of_j cc x)) =
  C.str_sound (); C.int_sound (); tagged_sound (fun s -> if s = "queryResult" then Some () else None);
  column_read_sound cc.rows_of;
  C.field_good "$type" result_tag_of x; C.field_good "rows" (column_read cc.rows_of) x;
  C.field_good "pageNum" C.d_int x; C.opt_field_good "totalRowCount" C.d_int x;
  C.opt_field_good "nextPageToken" C.d_str x

let result_refusals_resolve (#r:Type) (cc:column_codec r) : Lemma (C.sound (result_of_j cc)) =
  C.sound_intro (result_of_j cc) (fun x -> result_good cc x)

(* ---- the async result: `deferredResultJson` / `deferredResultOf` ---- *)

(* F#: `QueryCodec.deferredResultJson` — the capability seam's envelope over `resultJson`. *)
let deferred_result_json (#r:Type) (cc:column_codec r) (d:C.deferred (query_result r)) : Tot C.jval =
  C.deferred_json (result_json cc) d

(* F#: `QueryCodec.deferredResultOf` (Phase 455) — the capability seam's TYPED envelope reader over
   `resultOf`, so a refusal inside the payload keeps its code and its path under `value`. *)
let deferred_result_of_j (#r:Type) (cc:column_codec r) : C.decoder (C.deferred (query_result r)) =
  C.deferred_dec (result_of_j cc)

let deferred_result_roundtrip (#r:Type) (cc:column_codec r) (d:C.deferred (query_result r))
  : Lemma (requires column_codec_laws cc) (ensures deferred_result_of_j cc (deferred_result_json cc d) == C.Ok d)
  = (match d with C.Ready v -> result_roundtrip cc v | _ -> ());
    C.deferred_roundtrip (result_json cc) (result_of_j cc) d

let deferred_result_refusals_resolve (#r:Type) (cc:column_codec r) : Lemma (C.sound (deferred_result_of_j cc)) =
  result_refusals_resolve cc;
  C.deferred_dec_sound (result_of_j cc)

(* ---- the typed refusal: `QueryErrorWire.toJson` / `decoder` ---- *)

(* F#: `PathSegment` as `DecodePath.toJson` writes it — an index is any integer the wire carries. *)
type wire_seg =
  | WKey   : string -> wire_seg
  | WIndex : int -> wire_seg

(* F#: `DecodeError`, its prose kept — the refusal an `UnreadableArgs` carries crosses whole. *)
type wire_decode_error = { we_code: C.decode_code; we_path: list wire_seg; we_expected: string; we_message: string }

(* F#: `QueryError`, all twenty cases: the fifteen this model's `query_error` carries, under
   `QRefused`, and the five it leaves to the parts it does not model (the policy gate, the argument
   reader, the typed resolver). *)
type wire_query_error =
  | QRefused              : query_error -> wire_query_error
  | QPolicyRefused        : policy:string -> reason:string -> allowed:list string -> wire_query_error
  | QApprovalRequired     : policy:string -> wire_query_error
  | QUnreadableArgs       : error:wire_decode_error -> wire_query_error
  | QPredicateNotHonoured : predicate:predicate -> wire_query_error
  | QOrderNotHonoured     : column:string -> wire_query_error

(* F#: `DecodeError.codeName`. *)
let code_name (c:C.decode_code) : Tot string =
  match c with
  | C.InvalidJson -> "InvalidJson" | C.MissingField -> "MissingField" | C.WrongKind -> "WrongKind"
  | C.UnknownTag -> "UnknownTag" | C.OutOfRange -> "OutOfRange" | C.UndeclaredMember -> "UndeclaredMember"
  | C.LimitExceeded -> "LimitExceeded" | C.NotAdmitted -> "NotAdmitted" | C.SchemaFault -> "SchemaFault"

(* F#: `DecodeError.codes |> List.map (fun c -> codeName c, c)`, looked up. *)
let code_of_name (s:string) : Tot (option C.decode_code) =
  if s = "InvalidJson" then Some C.InvalidJson else if s = "MissingField" then Some C.MissingField
  else if s = "WrongKind" then Some C.WrongKind else if s = "UnknownTag" then Some C.UnknownTag
  else if s = "OutOfRange" then Some C.OutOfRange else if s = "UndeclaredMember" then Some C.UndeclaredMember
  else if s = "LimitExceeded" then Some C.LimitExceeded else if s = "NotAdmitted" then Some C.NotAdmitted
  else if s = "SchemaFault" then Some C.SchemaFault else None

(* F#: `DecodePath.toJson`. *)
let rec path_json (p:list wire_seg) : Tot (list C.jval) =
  match p with
  | [] -> []
  | WKey k :: t -> C.JStr k :: path_json t
  | WIndex i :: t -> C.JInt i :: path_json t

(* F#: `DecodePath.ofJson`'s items — `None` at the first item that is neither a string nor an
   integer. *)
let rec path_items (xs:list C.jval) : Tot (option (list wire_seg)) =
  match xs with
  | [] -> Some []
  | C.JStr k :: t -> (match path_items t with Some p -> Some (WKey k :: p) | None -> None)
  | C.JInt i :: t -> (match path_items t with Some p -> Some (WIndex i :: p) | None -> None)
  | _ -> None

(* F#: `decodeErrorOf`'s `path` reader — a value that is not a path is `WrongKind` at the value. *)
let path_of_j : C.decoder (list wire_seg) =
  fun el ->
    match el with
    | C.JArr xs -> (match path_items xs with Some p -> C.Ok p | None -> C.Error (C.refuse C.WrongKind))
    | _ -> C.Error (C.refuse C.WrongKind)

(* F#: `DecodeError.toJson`. *)
let decode_error_json (e:wire_decode_error) : Tot C.jval =
  C.JObj [("code", C.JStr (code_name e.we_code)); ("path", C.JArr (path_json e.we_path));
          ("expected", C.JStr e.we_expected); ("message", C.JStr e.we_message)]

(* F#: `QueryErrorWire.decodeErrorOf`. *)
let decode_error_of_j : C.decoder wire_decode_error =
  C.both (C.field "code" (tagged_of code_of_name))
         (C.both (C.field "path" path_of_j)
                 (C.both (C.field "expected" C.d_str) (C.field "message" C.d_str) (fun x m -> (x, m)))
                 (fun p xm -> (p, xm)))
         (fun c (pxm:(list wire_seg & (string & string))) ->
            { we_code = c; we_path = fst pxm; we_expected = fst (snd pxm); we_message = snd (snd pxm) })

let strs_j (xs:list string) : Tot C.jval = C.JArr (C.strs xs)

(* F#: `QueryErrorWire.toJson`. *)
let query_error_json (#r:Type) (cc:column_codec r) (e:wire_query_error) : Tot C.jval =
  match e with
  | QRefused (NoSuchQuery id known) -> C.JObj [("$type", C.JStr "noSuchQuery"); ("id", C.JStr id); ("known", strs_j known)]
  | QRefused (DuplicateQuery id) -> C.JObj [("$type", C.JStr "duplicateQuery"); ("id", C.JStr id)]
  | QRefused (UnknownParam name declared) ->
    C.JObj [("$type", C.JStr "unknownParam"); ("name", C.JStr name); ("declared", strs_j declared)]
  | QRefused (ParamTypeMismatch name expected got) ->
    C.JObj [("$type", C.JStr "paramTypeMismatch"); ("name", C.JStr name);
            ("expected", C.JStr (column_tag expected)); ("got", C.JStr (column_tag got))]
  | QRefused (RequiredParamsUnbound names) -> C.JObj [("$type", C.JStr "requiredParamsUnbound"); ("names", strs_j names)]
  | QRefused (SourceNotResolved s) -> C.JObj [("$type", C.JStr "sourceNotResolved"); ("ref", C.JStr s)]
  | QRefused (ExecutionFailed detail recoverable) ->
    C.JObj [("$type", C.JStr "executionFailed"); ("detail", C.JStr detail); ("recoverable", strs_j recoverable)]
  | QRefused Timeout -> C.JObj [("$type", C.JStr "timeout")]
  | QRefused (RequiredParamsNull names) -> C.JObj [("$type", C.JStr "requiredParamsNull"); ("names", strs_j names)]
  | QRefused (DuplicateParam name) -> C.JObj [("$type", C.JStr "duplicateParam"); ("name", C.JStr name)]
  | QPolicyRefused policy reason allowed ->
    C.JObj [("$type", C.JStr "queryPolicyRefused"); ("policy", C.JStr policy); ("reason", C.JStr reason); ("allowed", strs_j allowed)]
  | QApprovalRequired policy -> C.JObj [("$type", C.JStr "queryApprovalRequired"); ("policy", C.JStr policy)]
  | QUnreadableArgs err -> C.JObj [("$type", C.JStr "unreadableArgs"); ("error", decode_error_json err)]
  | QRefused (UnknownColumn name declared) ->
    C.JObj [("$type", C.JStr "unknownColumn"); ("name", C.JStr name); ("declared", strs_j declared)]
  | QRefused (PredicateTypeMismatch column expected got) ->
    C.JObj [("$type", C.JStr "predicateTypeMismatch"); ("column", C.JStr column);
            ("expected", C.JStr (column_tag expected)); ("got", C.JStr (column_tag got))]
  | QRefused (PredicateNotApplicable predicate column ty) ->
    C.JObj [("$type", C.JStr "predicateNotApplicable"); ("predicate", C.JStr predicate); ("column", C.JStr column);
            ("columnType", C.JStr (column_tag ty))]
  | QRefused (IllFormedLiteral column reason) ->
    C.JObj [("$type", C.JStr "illFormedLiteral"); ("column", C.JStr column); ("reason", C.JStr reason)]
  | QRefused (DuplicateSortColumn column) -> C.JObj [("$type", C.JStr "duplicateSortColumn"); ("column", C.JStr column)]
  | QPredicateNotHonoured p -> C.JObj [("$type", C.JStr "predicateNotHonoured"); ("predicate", predicate_json cc p)]
  | QOrderNotHonoured column -> C.JObj [("$type", C.JStr "orderNotHonoured"); ("column", C.JStr column)]

let s_f (name:string) : C.decoder string = C.field name C.d_str
let sl_f (name:string) : C.decoder (list string) = C.field name (C.d_list C.d_str)
let ty_f (name:string) : C.decoder column_type = C.field name col_type_of_j

(* F#: `QueryShape.tags` looked up — `predicateNotApplicable`'s `predicate` member. *)
let predicate_tag_of (s:string) : Tot (option string) =
  if s = "equalTo" || s = "greaterThan" || s = "atLeast" || s = "lessThan" || s = "atMost" ||
     s = "contains" || s = "isNull" || s = "isNotNull" then Some s else None

let qrefused (#a:Type) (f:a -> query_error) : a -> wire_query_error = fun x -> QRefused (f x)

let query_error_cases (#r:Type) (cc:column_codec r) : list (string & C.decoder wire_query_error) =
  [("noSuchQuery", C.both (s_f "id") (sl_f "known") (fun id known -> QRefused (NoSuchQuery id known)));
   ("duplicateQuery", C.d_map (qrefused DuplicateQuery) (s_f "id"));
   ("unknownParam", C.both (s_f "name") (sl_f "declared") (fun n d -> QRefused (UnknownParam n d)));
   ("paramTypeMismatch",
    C.both (s_f "name") (C.both (ty_f "expected") (ty_f "got") (fun x g -> (x, g)))
           (fun n (xg:(column_type & column_type)) -> QRefused (ParamTypeMismatch n (fst xg) (snd xg))));
   ("requiredParamsUnbound", C.d_map (qrefused RequiredParamsUnbound) (sl_f "names"));
   ("sourceNotResolved", C.d_map (qrefused SourceNotResolved) (s_f "ref"));
   ("executionFailed", C.both (s_f "detail") (sl_f "recoverable") (fun d rc -> QRefused (ExecutionFailed d rc)));
   ("timeout", C.d_succeed (QRefused Timeout));
   ("requiredParamsNull", C.d_map (qrefused RequiredParamsNull) (sl_f "names"));
   ("duplicateParam", C.d_map (qrefused DuplicateParam) (s_f "name"));
   ("queryPolicyRefused",
    C.both (s_f "policy") (C.both (s_f "reason") (sl_f "allowed") (fun rs a -> (rs, a)))
           (fun p (ra:(string & list string)) -> QPolicyRefused p (fst ra) (snd ra)));
   ("queryApprovalRequired", C.d_map QApprovalRequired (s_f "policy"));
   ("unreadableArgs", C.d_map QUnreadableArgs (C.field "error" decode_error_of_j));
   ("unknownColumn", C.both (s_f "name") (sl_f "declared") (fun n d -> QRefused (UnknownColumn n d)));
   ("predicateTypeMismatch",
    C.both (s_f "column") (C.both (ty_f "expected") (ty_f "got") (fun x g -> (x, g)))
           (fun c (xg:(column_type & column_type)) -> QRefused (PredicateTypeMismatch c (fst xg) (snd xg))));
   ("predicateNotApplicable",
    C.both (C.field "predicate" (tagged_of predicate_tag_of)) (C.both (s_f "column") (ty_f "columnType") (fun c t -> (c, t)))
           (fun p (ct:(string & column_type)) -> QRefused (PredicateNotApplicable p (fst ct) (snd ct))));
   ("illFormedLiteral", C.both (s_f "column") (s_f "reason") (fun c rs -> QRefused (IllFormedLiteral c rs)));
   ("duplicateSortColumn", C.d_map (qrefused DuplicateSortColumn) (s_f "column"));
   ("predicateNotHonoured", C.d_map QPredicateNotHonoured (C.field "predicate" (predicate_of_j cc)));
   ("orderNotHonoured", C.d_map QOrderNotHonoured (s_f "column"))]

(* F#: `QueryErrorWire.decoder`. *)
let query_error_of_j (#r:Type) (cc:column_codec r) : C.decoder wire_query_error = C.tag_dispatch (query_error_cases cc)

(* A refusal the codec carries: a `predicateNotApplicable` names a predicate tag, an
   `unreadableArgs` refusal carries a code, and a `predicateNotHonoured` predicate's literal is
   carried. *)
let query_error_carried (e:wire_query_error) : Tot bool =
  match e with
  | QRefused (PredicateNotApplicable p _ _) -> Some? (predicate_tag_of p)
  | QPredicateNotHonoured p -> literal_carried p
  | _ -> true

let rec path_roundtrip (p:list wire_seg) : Lemma (path_items (path_json p) == Some p) =
  match p with [] -> () | _ :: t -> path_roundtrip t

let decode_error_roundtrip (e:wire_decode_error) : Lemma (decode_error_of_j (decode_error_json e) == C.Ok e) =
  path_roundtrip e.we_path

(* THE REFUSAL ROUND TRIP (Phase 455), `query_error_roundtrip`: every `QueryError` the codec
   carries — the twenty cases, a decode refusal and a predicate inside them — reads back as itself. *)
#push-options "--fuel 21 --ifuel 1 --z3rlimit 80"
let query_error_roundtrip (#r:Type) (cc:column_codec r) (e:wire_query_error)
  : Lemma (requires column_codec_laws cc /\ query_error_carried e)
          (ensures query_error_of_j cc (query_error_json cc e) == C.Ok e)
  = match e with
    | QRefused (NoSuchQuery _ xs) -> C.strs_roundtrip 0 xs
    | QRefused (UnknownParam _ xs) -> C.strs_roundtrip 0 xs
    | QRefused (ParamTypeMismatch _ a b) -> column_tag_roundtrip a; column_tag_roundtrip b
    | QRefused (RequiredParamsUnbound xs) -> C.strs_roundtrip 0 xs
    | QRefused (ExecutionFailed _ xs) -> C.strs_roundtrip 0 xs
    | QRefused (RequiredParamsNull xs) -> C.strs_roundtrip 0 xs
    | QPolicyRefused _ _ xs -> C.strs_roundtrip 0 xs
    | QUnreadableArgs err -> decode_error_roundtrip err
    | QRefused (UnknownColumn _ xs) -> C.strs_roundtrip 0 xs
    | QRefused (PredicateTypeMismatch _ a b) -> column_tag_roundtrip a; column_tag_roundtrip b
    | QRefused (PredicateNotApplicable _ _ t) -> column_tag_roundtrip t
    | QPredicateNotHonoured p -> predicate_roundtrip cc p
    | _ -> ()
#pop-options

let path_sound () : Lemma (C.sound path_of_j) = C.sound_intro path_of_j (fun x -> ())

let decode_error_sound () : Lemma (C.sound decode_error_of_j) =
  C.str_sound (); tagged_sound code_of_name; path_sound ();
  C.field_sound "code" (tagged_of code_of_name); C.field_sound "path" path_of_j;
  C.field_sound "expected" C.d_str; C.field_sound "message" C.d_str;
  C.both_sound (C.field "expected" C.d_str) (C.field "message" C.d_str) (fun x m -> (x, m));
  C.both_sound (C.field "path" path_of_j)
               (C.both (C.field "expected" C.d_str) (C.field "message" C.d_str) (fun x m -> (x, m)))
               (fun p xm -> (p, xm));
  C.both_sound (C.field "code" (tagged_of code_of_name))
               (C.both (C.field "path" path_of_j)
                       (C.both (C.field "expected" C.d_str) (C.field "message" C.d_str) (fun x m -> (x, m)))
                       (fun p xm -> (p, xm)))
               (fun c (pxm:(list wire_seg & (string & string))) ->
                  { we_code = c; we_path = fst pxm; we_expected = fst (snd pxm); we_message = snd (snd pxm) })

let s_f_sound (n:string) : Lemma (C.sound (s_f n)) = C.str_sound (); C.field_sound n C.d_str
let sl_f_sound (n:string) : Lemma (C.sound (sl_f n)) = C.str_sound (); C.list_sound C.d_str; C.field_sound n (C.d_list C.d_str)
let ty_f_sound (n:string) : Lemma (C.sound (ty_f n)) = tagged_sound column_of_tag; C.field_sound n col_type_of_j

(* Every refusal the query-error reader answers resolves in the document it read. *)
#push-options "--fuel 21 --ifuel 1 --z3rlimit 80"
let query_error_refusals_resolve (#r:Type) (cc:column_codec r) : Lemma (C.sound (query_error_of_j cc)) =
  s_f_sound "id"; s_f_sound "name"; s_f_sound "ref"; s_f_sound "detail"; s_f_sound "policy"; s_f_sound "reason";
  s_f_sound "column";
  sl_f_sound "known"; sl_f_sound "declared"; sl_f_sound "names"; sl_f_sound "recoverable"; sl_f_sound "allowed";
  ty_f_sound "expected"; ty_f_sound "got"; ty_f_sound "columnType";
  decode_error_sound (); C.field_sound "error" decode_error_of_j;
  predicate_sound cc; C.field_sound "predicate" (predicate_of_j cc);
  tagged_sound predicate_tag_of; C.field_sound "predicate" (tagged_of predicate_tag_of);
  C.both_sound (s_f "id") (sl_f "known") (fun id known -> QRefused (NoSuchQuery id known));
  C.map_sound (qrefused DuplicateQuery) (s_f "id");
  C.both_sound (s_f "name") (sl_f "declared") (fun n d -> QRefused (UnknownParam n d));
  C.both_sound (ty_f "expected") (ty_f "got") (fun x g -> (x, g));
  C.both_sound (s_f "name") (C.both (ty_f "expected") (ty_f "got") (fun x g -> (x, g)))
               (fun n (xg:(column_type & column_type)) -> QRefused (ParamTypeMismatch n (fst xg) (snd xg)));
  C.map_sound (qrefused RequiredParamsUnbound) (sl_f "names");
  C.map_sound (qrefused SourceNotResolved) (s_f "ref");
  C.both_sound (s_f "detail") (sl_f "recoverable") (fun d rc -> QRefused (ExecutionFailed d rc));
  C.succeed_sound (QRefused Timeout);
  C.map_sound (qrefused RequiredParamsNull) (sl_f "names");
  C.map_sound (qrefused DuplicateParam) (s_f "name");
  C.both_sound (s_f "reason") (sl_f "allowed") (fun rs a -> (rs, a));
  C.both_sound (s_f "policy") (C.both (s_f "reason") (sl_f "allowed") (fun rs a -> (rs, a)))
               (fun p (ra:(string & list string)) -> QPolicyRefused p (fst ra) (snd ra));
  C.map_sound QApprovalRequired (s_f "policy");
  C.map_sound QUnreadableArgs (C.field "error" decode_error_of_j);
  C.both_sound (s_f "name") (sl_f "declared") (fun n d -> QRefused (UnknownColumn n d));
  C.both_sound (s_f "column") (C.both (ty_f "expected") (ty_f "got") (fun x g -> (x, g)))
               (fun c (xg:(column_type & column_type)) -> QRefused (PredicateTypeMismatch c (fst xg) (snd xg)));
  C.both_sound (s_f "column") (ty_f "columnType") (fun c t -> (c, t));
  C.both_sound (C.field "predicate" (tagged_of predicate_tag_of)) (C.both (s_f "column") (ty_f "columnType") (fun c t -> (c, t)))
               (fun p (ct:(string & column_type)) -> QRefused (PredicateNotApplicable p (fst ct) (snd ct)));
  C.both_sound (s_f "column") (s_f "reason") (fun c rs -> QRefused (IllFormedLiteral c rs));
  C.map_sound (qrefused DuplicateSortColumn) (s_f "column");
  C.map_sound QPredicateNotHonoured (C.field "predicate" (predicate_of_j cc));
  C.map_sound QOrderNotHonoured (s_f "column");
  C.tag_dispatch_sound (query_error_cases cc)
#pop-options
