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
       declared param with a `Null` or an in-type cell and that bind every required name
       (`validate_params_exact`), and each refusal is truthful (`refusal_is_truthful`).
     - `enumerate_is_registry` — an id is enumerable exactly when `tryFind` resolves it, and
       `dispatch` raises `NoSuchQuery` exactly off the enumeration (`no_such_iff_unregistered`);
       `register` refuses a held id, extends by one entry otherwise, and keeps ids distinct.
     - `invocation_key_deterministic` — the capture key reads the declaration through its `Id`
       ALONE and the arguments through their name-sorted canonical form alone: two declarations
       sharing an id, and two argument lists binding the same names to the same cells in ANY
       order, key identically. That the key reads no resolver answer and no clock is its TYPE —
       it is handed neither — and is said here rather than dressed as a lemma.
     - `invocation_key_injective` (Phase 225) — the capture key's pre-image is INJECTIVE: two
       argument lists with one name-sorted canonical string are one sorted list and hold the same
       bindings, so distinct argument sets have distinct pre-images. It is proved over a reading
       of a string as its symbols (`symbols_faithful`, the reading `Chain.fst` takes) and states
       what it needs of the host functions in `key_premises` (section 8b).

   THE FINDINGS read off the model, each proved and asserted on the shipped seam:

     - `all_null_accepted` — EVERY declaration, whatever it marks required, accepts the argument
       set binding each param to `Null`. `Required` constrains the presence of a NAME, never of
       a value: `QueryParam`'s doc comment says a bound cell may "be `Null` when not required",
       and the code accepts it when required as well.
     - `key_collision` was the second, until Phase 225 closed it: the canonical string joined
       `name=cellKey` pairs with NO separator, so two distinct accepted argument sets could share
       it. The lemma is gone because it is no longer true; `invocation_key_injective` is what
       replaced it, and the two exhibits it was stated on (`collision_one`, `collision_two`) stay
       below so the shipped seam's closure case can name them.

   WHAT IS NOT CLAIMED. Anything about a resolver. Anything about the five renderers beyond the
   total-order premise `invocation_key_deterministic` states for the comparator and the
   `key_premises` `invocation_key_injective` states (`query-renderers-abstract`) — in particular
   nothing about whether two distinct pre-images hash apart. The ORDER `enumerate` returns — production's `Map` sorts by id,
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

(* F#: `Cell`. A float crosses as an opaque carrier — the seam reads its TYPE and hands the
   carrier to the float renderer, and nothing else. *)
type cell =
  | Int       : int -> cell
  | Float     : string -> cell
  | Bool      : bool -> cell
  | Str       : string -> cell
  | Date      : string -> cell
  | Timestamp : string -> cell
  | Null      : cell

(* F#: `HostEffect`. *)
type host_effect =
  | Pure
  | ReadsHost
  | WritesHost

(* F#: `DeterminismSource`. *)
type determinism_source =
  | Deterministic
  | Clock
  | Random
  | Network

(* F#: `EffectClass`. *)
type effect_class = { host: host_effect; determinism: determinism_source }

(* F#: `QueryParam`. *)
type query_param = { p_name: string; p_type: column_type; p_required: bool }

(* F#: `Query`. `Source` is an opaque carrier: the seam never reads it. *)
type query = {
  q_id: string;
  q_params: list query_param;
  q_schema: list (string & column_type);
  q_effect: effect_class;
  q_source: string;
  q_timeout_ms: option int;
  q_page_size: option int
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
  | Null -> None

(* F#: `Effect.determinismTag`. *)
let determinism_tag (d:determinism_source) : Tot string =
  match d with
  | Deterministic -> "deterministic"
  | Clock -> "clock"
  | Random -> "random"
  | Network -> "network"

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
  field:        string -> string
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

(* F#: `Query.invocationKey`. *)
let invocation_key (rn:renderers) (q:query) (a:arguments) : Tot string =
  q.q_id ^ "#" ^ rn.hash (canonical rn (sort_args rn a))

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
        if t = p.p_type then check_args ps declared rest
        else Error (ParamTypeMismatch name p.p_type t)

(* F#: `validateParams`'s step 2 — the required params the args leave unbound. *)
let rec unbound_required (ps:list query_param) (a:arguments) : Tot (list string) =
  match ps with
  | [] -> []
  | p :: t ->
    if p.p_required && not (has_key p.p_name a) then p.p_name :: unbound_required t a
    else unbound_required t a

(* F#: `Query.validateParams`. *)
let validate_params (q:query) (a:arguments) : Tot (outcome unit query_error) =
  match check_args q.q_params (param_names q.q_params) a with
  | Error e -> Error e
  | Ok () ->
    match unbound_required q.q_params a with
    | [] -> Ok ()
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

(* F#: `QueryRegistry.register` — additive, no silent overwrite. *)
let register (q:query) (r:registry) : Tot (outcome registry query_error) =
  match find_query q.q_id r.queries with
  | Some _ -> Error (DuplicateQuery q.q_id)
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

(* `validateParams` refuses in three classes and no other. *)
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
          | Some t -> if t = p.p_type then check_args_shape ps declared rest else ()))

let validate_params_shape (q:query) (a:arguments)
  : Lemma (ensures (match validate_params q a with
                    | Ok () -> True
                    | Error e -> UnknownParam? e \/ ParamTypeMismatch? e \/ RequiredParamsUnbound? e))
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
   or of that param's type. *)
let well_typed (ps:list query_param) (b:(string & cell)) : Tot bool =
  let (name, c) = b in
  match find_param name ps with
  | None -> false
  | Some p ->
    (match cell_type c with
     | None -> true
     | Some t -> t = p.p_type)

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
   the declaration and no required name is left unbound. *)
let validate_params_exact (q:query) (a:arguments)
  : Lemma (validate_params q a == Ok () <==>
           (all_well_typed q.q_params a /\ unbound_required q.q_params a == []))
  = check_args_exact q.q_params (param_names q.q_params) a

(* What a REFUSAL guarantees: an `UnknownParam` names a bound name no param declares, and lists
   the declared names; a `ParamTypeMismatch` names a declared param, its declared type, and a
   different type the bound cell really has. *)
let rec refusal_is_truthful (ps:list query_param) (declared:list string) (a:arguments)
  : Lemma (ensures (match check_args ps declared a with
                    | Error (UnknownParam name d) ->
                      None? (find_param name ps) /\ d == declared /\ has_key name a
                    | Error (ParamTypeMismatch name expected got) ->
                      has_key name a /\ expected <> got /\
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
          | Some t -> if t = p.p_type then refusal_is_truthful ps declared rest else ()))

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
  : Lemma (requires not (mem q.q_id (ids (enumerate r))))
          (ensures register q r == Ok { queries = q :: r.queries } /\
                   (forall (id:string). mem id (ids (q :: r.queries)) <==> (id = q.q_id \/ mem id (ids (enumerate r)))))
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

(* THE FOURTH THEOREM. F#: `Query.invocationKey`. The key reads the declaration through its id
   alone, and the arguments through their name-sorted canonical form alone: two declarations
   sharing an id, and two argument lists binding the same distinct names to the same cells in
   any order, key identically — whatever else the declarations say, and whatever order the
   caller happened to write the bindings in. *)
let invocation_key_deterministic (rn:renderers) (q q':query) (a a':arguments)
  : Lemma (requires total_order rn.name_le /\ q.q_id == q'.q_id /\
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

(* The id-only half on its own, with no premise at all. *)
let invocation_key_id_only (rn:renderers) (q q':query) (a:arguments)
  : Lemma (requires q.q_id == q'.q_id)
          (ensures invocation_key rn q a == invocation_key rn q' a)
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
  injective rn.render_int /\ injective rn.render_float

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

(* THE FIFTH THEOREM. F#: `Query.invocationKey`'s pre-image. Two argument lists whose
   name-sorted canonical strings agree ARE the same sorted list, and so hold exactly the same
   bindings: distinct argument sets have distinct pre-images, for every declaration, accepted or
   not. It needs no premise about the comparator — the sort is a function, and equal outputs are
   all the argument reads. Whether two distinct pre-images HASH apart is a claim about FNV-1a and
   is not made (`query-renderers-abstract`). *)
let invocation_key_injective (#sym:eqtype) (reveal:string -> list sym) (e t:sym) (rn:renderers)
  (a a':arguments)
  : Lemma (requires key_premises reveal e t rn /\
                    canonical rn (sort_args rn a) == canonical rn (sort_args rn a'))
          (ensures sort_args rn a == sort_args rn a' /\
                   (forall (x:(string & cell)). mem_arg x a = mem_arg x a')) =
  canonical_injective reveal e t rn (sort_args rn a) (sort_args rn a');
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

(* THE FIRST FINDING. EVERY declaration — whatever it marks required — accepts the argument set
   binding each of its params to `Null`. `Required` constrains the presence of a NAME, never of
   a value: a `Null` cell is "type-agnostic absence" to step 1 and a bound name to step 2. *)
let all_null_accepted (q:query)
  : Lemma (validate_params q (nulls_of q.q_params) == Ok ())
  = all_in_refl (param_names q.q_params);
    nulls_well_typed q.q_params q.q_params;
    nulls_keys q.q_params;
    nulls_bind_all q.q_params (nulls_of q.q_params);
    validate_params_exact q (nulls_of q.q_params)

(* The declaration the former second finding (`key_collision`, closed by Phase 225) was exhibited
   on: `a` required, `b` optional, both strings. The two argument sets below shared a pre-image
   before Phase 225; `invocation_key_injective` says they no longer can, and the shipped seam's
   closure case keys them apart. *)
let collision_params : list query_param =
  [ { p_name = "a"; p_type = StringType; p_required = true };
    { p_name = "b"; p_type = StringType; p_required = false } ]

let collision_one : arguments = [ ("a", Str "1b=s2") ]
let collision_two : arguments = [ ("a", Str "1"); ("b", Str "2") ]
