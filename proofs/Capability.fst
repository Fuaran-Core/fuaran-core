(*
   Capability — default-deny dispatch and the three function laws as theorems: the seam every
   AI-driven edit crosses, `Fuaran.Core.Function`, modelled clause for clause and proved
   (fuaran-core Phase 177).

   WHAT IS MODELLED. `src/Fuaran.Core.Function/Function.fs` — three surfaces of one file:

     - the EFFECT LATTICE (`Effect.join` / `Effect.covers` over a host chain beside a determinism SET,
       the set written as its characteristic vector since Phase 319), the VALUE SPACES
       (`Space.validate` / `Space.isBounded`) and the hole
       vocabulary (`HoleKind` / `HoleDecl` / `SigEntry` / `Signature` / `Arg` / `ApplyError`);
     - the FUNCTION ALGEBRA over the domain-witness record: `Function.signature`,
       `signatureExcluding`, `isTotal`, the private `guardTotal` / `validateArg` / `bindArgs` and
       the `apply` / `curry` that are its two faces, `compose`, `composedEffect`, `observedEffect`
       and `auditEffect`;
     - the CAPABILITY SEAM: `Capability.create` / `validateArgs` / `invoke`, `CapabilityRegistry.empty` /
       `register` / `tryFind` / `enumerate` / `dispatch`, the seven-case `InvokeError`, and — since
       Phase 210 — the `Deferred<'T>` envelope the host body answers in. The envelope's three cases
       are modelled; its COMBINATORS (`Deferred.map` / `bind` / `toResult` / `tryValue`) are not,
       being outside the seam.

   And since Phase 354, three sections the admission work of Phase 307 left unmodelled:

     - the HANDLER TABLE (section 14): `Function.bindHandlers` — the behaviour-axis binding of a
       host's handlers to an artifact's action holes — with the `Map` its table is;
     - the CAPABILITY PIPELINE (section 15): `CapabilityPipeline.typeCheck` with its cycle search,
       `eval`, `dirtySet` and `evalFrom` over an abstract host body, from
       `src/Fuaran.Core.Function/CapabilityPipeline.fs`;
     - the CODECS (section 16): `CapabilityCodec` for a signature and a capability and the
       pipeline's codec for a node, with `SpaceCodec` and `EffectCodec`, at the `JVal`.

   Two things are PARAMETERS rather than clauses, exactly as Phase 135 made `float i` a parameter
   and Phase 176 made the pipeline evaluator one. The WITNESS (`ArtifactWitness`'s `Holes`,
   `Effect`, `Bind`, the tree witness's `KindTag`, and `Tree.preorder` over it) is a record of
   functions over an abstract `node` type: the algebra reads holes off it and hands bindings back
   to it, and nothing here says what a domain's `Bind` does. And the three SCALAR READERS the
   value-space check needs — `System.Int32.TryParse`, `System.Double.TryParse` against a float
   range, and `String.Length` — are a `readers` record: the space vocabulary is modelled, the
   lexical parsers behind two of its five constructors are not, and a float range's bounds cross
   as opaque carriers. `invocationKey` entered the model with Phase 225 (section 12), over a
   `key_renderers` record — `Hash.fnv1a`, the address comparator and `Hash.canonicalField` — for
   the same reason. The Phase 354 sections add three more of the same kind and no other: the
   ordinal comparator an F# `Map<string, _>` keeps its keys by (`le`, under `total_order` where a
   theorem spends it), the two float comparisons `Space.subsumes` makes (`feed_readers`), and the
   one float read the codecs make (`codec_readers`). The pipeline's host body and the value it
   spells (`node_body`, `spell`) are parameters as `invoke`'s body is; a handler is an abstract
   type.

   WHAT IS PROVED, over any witness, any readers, any registry and any host body:

     - `unregistered_refused` — `CapabilityRegistry.dispatch` of an id the registry does not hold is the
       typed refusal `NoSuchCapability id known`, and the result is the SAME for every host body,
       which is what "runs no handler" means without instrumenting one.
     - `validate_before_invoke` — an argument set `validateArgs` rejects makes `invoke` return
       that rejection for every body alike; a `BodyFailed` or an accepted result is reachable only
       through an accepted validation (`body_runs_only_validated`); an accepted invocation carries
       the body's `Ready` or `Pending` and NOTHING else, so the envelope's fourth outcome
       `Ok (Failed _)` is unreachable at the seam and through the registry alike
       (`invoke_never_ok_failed`, `dispatch_never_ok_failed` — Phase 210); and what validation says is
       characterised: the refusal is one of its own four, an accepted set addresses declared
       holes only and binds every required one (`validate_args_sound`), and an `ArgOutOfSpace`
       names a value the space really refuses (`refusal_is_truthful`). And since Phase 229 the
       converse of Phase 177's finding: `slot_hole_invocable_in_space` — `signature` enters every
       `SlotHole` with the tree space `SlotTree` (`slot_entry_shape`), and an argument set that
       binds a slot to a wire tree of its kind, with every other argument in its space and every
       required entry bound, is ACCEPTED (`validate_args_complete`), while a tree of the wrong kind
       is the typed `ArgOutOfSpace` naming the constraint (`slot_wrong_kind_refused`) and a scalar
       is `UninvocableArg` (`slot_scalar_uninvocable`). A capability over a slotted artifact is
       invocable. What Phase 177 proved survives about the one entry shape `signature` no longer
       makes — a REQUIRED entry with NO space refuses every argument list
       (`spaceless_required_uninvocable`).
     - `enumerate_is_registry` — an id is enumerable exactly when it is dispatchable: there is
       no entry `enumerate` hides and none `dispatch` reaches past it (`no_such_iff_unregistered`).
       `register` refuses a held id and extends by exactly one entry otherwise, and a registry
       built by it holds distinct ids (`register_keeps_distinct`).
     - `totality_law` — law 1. `isTotal` over the derived signature agrees with the guard
       `apply` / `curry` run; when the guard fires the refusal is `NonTotal` at the first
       unbounded repeat and the witness's `Bind` is never consulted (`rejected_never_bound` — the
       result is the same under any `Bind` at all), which is "rejected, never run".
     - `hygiene_law` — law 2. Hole NAMES are inert: renaming every hole leaves `apply`, `curry`
       and `compose` unchanged, because the walk keys on the absolute address and nothing else.
       An argument at an undeclared address is refused by name before any binding
       (`undeclared_address_refused`), and binding a hole leaves a same-named hole at another
       address exactly as it was — removing that hole from the declaration changes nothing
       (`same_name_no_capture`).
     - `effect_law` — law 3. `composedEffect` is the componentwise join, the join is a
       commutative, associative, idempotent least upper bound with `pureDeterministic` as its
       identity, and `covers` is the order it is least for. `audit_effect_join` carries that to
       the audit: the observed effect covers every node's declared class, an `Ok` audit says the
       root covers every descendant, and an `Error` audit exhibits a descendant it does not.
     - `invocation_key_injective` (Phase 225) — the capture key's pre-image is INJECTIVE: two
       argument lists with one address-sorted canonical string are one sorted list and hold the
       same bindings, so distinct argument sets have distinct pre-images whatever their values
       contain. Proved over a reading of a string as its symbols (`symbols_faithful`, the one
       `Chain.fst` takes) and an escaper premise (`field_faithful`), named in `key_premises`.
     - `bind_handlers_exact` (Phase 354) — what `bindHandlers` computes: the handlers are accepted
       exactly when every key is a declared action hole's address and every action hole has a
       handler its ceiling covers, and the table is the handlers given (`bind_handlers_complete`
       is the converse); each refusal says what is refused and why, naming the first offender in
       the order the check walks. `bind_handlers_order_independent` — the whole result is a
       function of the bindings, never of the order the handlers arrive in.
     - `typecheck_topological` (Phase 354) — an accepted pipeline's declaration order is a
       topological order of its edges, and of two declarations of the same nodes each in such an
       order `typeCheck` accepts both or neither. `pipeline_refused_never_evaluated` and
       `pipeline_illtyped_iff_refused` — an evaluator answers `EvalIllTyped` exactly when the
       check refuses, and then no body runs. `pipeline_evalfrom_agrees` — `evalFrom` over a prior
       evaluation, under a body that agrees with the prior one off the change set, equals `eval`.
     - `signature_roundtrip`, `capability_roundtrip`, `node_roundtrip` (Phase 354) — decode after
       encode, EXACTLY, for every value: the identity on the well-formed ones
       (`…_roundtrip_identity`), a named refusal or a normal form on the others; and every
       document a reader accepts is a well-formed value that reads back as itself
       (`…_decoded_wf`).

   WHAT IS NOT CLAIMED. Anything about a domain's `Bind` — that a bound hole is cleared, that a
   slot's inner tree is where `compose` put it — which is the witness contract
   (`lawful-abstract-witness`). Anything about the two lexical parsers or a float range beyond
   the envelope the readers premise states (`capability-scalar-readers-abstract`). The ORDER
   `CapabilityRegistry.enumerate` returns — production's `Map` sorts by id, the model holds a finite map
   as a list, and the theorem is about membership. Whether two distinct capture-key pre-images
   HASH apart (a claim about FNV-1a). The `FunctionRegistry` and `ContentPack` surfaces, and
   `applyMemo`. Of the surfaces Phase 354 modelled: a handler's MESSAGE typing, which lives in the
   host language; that `dirtySet` is the LEAST closed set (its closure is what `evalFrom` needs —
   leastness is a cost claim, proved of the same-shaped loop in `Propagation.fst`); the pipeline's
   capture key `nodeInvocationKey`; the BYTES of any codec — `Canon.render` and `Json.parse` are
   `WireCanon.fst`'s and `JsonParse.fst`'s — the `Strict` read policy, the invocation, `Deferred`
   and `InvokeError` codecs, and a refusal's sentence. And NOT that a reader refuses whatever its
   writer never produces: the readers are lenient — an extra member, another member order, the
   descriptor spelling of a space are all read — so what is proved is that everything a reader
   ACCEPTS is a well-formed value, which is the claim that is true.

   HOW TO READ IT. Every definition names its F# counterpart, as in `Preservation.fst` and
   `ColumnOps.fst`. The module is SELF-CONTAINED like `ColumnOps.fst` — it opens nothing,
   restates `outcome` and the list helpers it needs, and extracts beside the other models into
   the same oracle assembly sharing only `Prims.fs` and the `option` shim.

   Apache-2.0, like everything beside it.
*)
module Capability

(* ======================================================================================
   0. The list helpers, self-contained, each naming the FSharp.Core function it stands for.
   ====================================================================================== *)

(* F#: `Result<'a, 'e>`. *)
type outcome (a e:Type) =
  | Ok    : a -> outcome a e
  | Error : e -> outcome a e

(* F#: `List.length`. *)
let rec len (#a:Type) (l:list a) : Tot nat =
  match l with
  | [] -> 0
  | _ :: t -> 1 + len t

(* F#: `@`. *)
let rec app (#a:Type) (l m:list a) : Tot (list a) =
  match l with
  | [] -> m
  | x :: t -> x :: app t m

(* F#: `List.rev`. *)
let rec rev (#a:Type) (l:list a) : Tot (list a) =
  match l with
  | [] -> []
  | x :: t -> app (rev t) [x]

(* F#: `List.map`. *)
let rec map (#a #b:Type) (f:a -> b) (l:list a) : Tot (list b) =
  match l with
  | [] -> []
  | x :: t -> f x :: map f t

(* F#: `List.filter`. *)
let rec filter (#a:Type) (p:a -> bool) (l:list a) : Tot (list a) =
  match l with
  | [] -> []
  | x :: t -> if p x then x :: filter p t else filter p t

(* F#: `List.forall`. *)
let rec for_all (#a:Type) (p:a -> bool) (l:list a) : Tot bool =
  match l with
  | [] -> true
  | x :: t -> p x && for_all p t

(* F#: `List.tryFind`. *)
let rec try_find (#a:Type) (p:a -> bool) (l:list a) : Tot (option a) =
  match l with
  | [] -> None
  | x :: t -> if p x then Some x else try_find p t

(* F#: `List.tryPick`. *)
let rec try_pick (#a #b:Type) (f:a -> option b) (l:list a) : Tot (option b) =
  match l with
  | [] -> None
  | x :: t ->
    match f x with
    | Some y -> Some y
    | None -> try_pick f t

(* F#: `List.fold`. *)
let rec fold_left (#a #b:Type) (f:b -> a -> b) (acc:b) (l:list a) : Tot b (decreases l) =
  match l with
  | [] -> acc
  | x :: t -> fold_left f (f acc x) t

(* F#: `List.contains` on strings. *)
let rec mem (x:string) (l:list string) : Tot bool =
  match l with
  | [] -> false
  | y :: t -> x = y || mem x t

(* Propositional membership, for the abstract node type (no decidable equality asked of it). *)
let rec memp (#a:Type) (x:a) (l:list a) : Tot prop =
  match l with
  | [] -> False
  | y :: t -> x == y \/ memp x t

(* F#: `Map.tryFind` on a map read as its key-ordered list. *)
let rec assoc (#a:Type) (k:string) (l:list (string & a)) : Tot (option a) =
  match l with
  | [] -> None
  | (k', v) :: t -> if k = k' then Some v else assoc k t

(* F#: `Map.containsKey`. *)
let rec has_key (#a:Type) (k:string) (l:list (string & a)) : Tot bool =
  match l with
  | [] -> false
  | (k', _) :: t -> k = k' || has_key k t

(* F#: `Map.toList |> List.map fst`. *)
let rec keys (#a:Type) (l:list (string & a)) : Tot (list string) =
  match l with
  | [] -> []
  | (k, _) :: t -> k :: keys t

(* F#: `List.distinct xs = xs`, as the predicate a registry's ids satisfy. *)
let rec distinct (l:list string) : Tot bool =
  match l with
  | [] -> true
  | x :: t -> not (mem x t) && distinct t

(* The named walks the clauses below would otherwise write as closures. Named rather than
   `List.tryFind (fun ...)` so that the prover sees one function where the F# has one lambda —
   a closure and a second closure with the same body are two terms to the encoding. Each names
   the F# expression it stands for. *)
(* ======================================================================================
   1. The effect lattice — `HostEffect`, `DeterminismSource`, `EffectClass`, `Effect.join`,
      `Effect.covers`. The host axis is written through the rank tables the F# uses; the
      determinism axis is a SET of factors (Phase 319), written as its characteristic vector.
   ====================================================================================== *)

(* F#: `HostEffect`. *)
type host_effect =
  | Pure
  | ReadsHost
  | WritesHost

(* F#: `DeterminismSource` = `Set<DeterminismFactor>`, `DeterminismFactor` being the closed three-case
   union `ClockFactor | RandomFactor | NetworkFactor`. The set is represented by its CHARACTERISTIC
   VECTOR over that alphabet: a factor is a member exactly when its flag is set, so `Set.empty` is
   the all-false vector — `Deterministic`. The representation is the model's; the differential host
   bridges it to the F# `Set` through membership, factor by factor. *)
type determinism_source = { has_clock: bool; has_random: bool; has_network: bool }

(* F#: `EffectClass`. *)
type effect_class = { host: host_effect; determinism: determinism_source }

(* F#: `Effect.deterministic` — `Set.empty`. *)
let deterministic : determinism_source = { has_clock = false; has_random = false; has_network = false }

(* F#: `Effect.pureDeterministic`. *)
let pure_deterministic : effect_class = { host = Pure; determinism = deterministic }

(* F#: `Effect.hostRank`. *)
let host_rank (h:host_effect) : Tot int =
  match h with
  | Pure -> 0
  | ReadsHost -> 1
  | WritesHost -> 2

(* F#: `Effect.hostOf` — `0 -> Pure | 1 -> ReadsHost | _ -> WritesHost`. *)
let host_of (n:int) : Tot host_effect =
  if n = 0 then Pure else if n = 1 then ReadsHost else WritesHost

(* F#: `max`. *)
let max_int (a b:int) : Tot int = if a >= b then a else b

(* F#: `Set.union` over the determinism factors — a factor is in the union when it is in either. *)
let det_union (a b:determinism_source) : Tot determinism_source =
  { has_clock = a.has_clock || b.has_clock;
    has_random = a.has_random || b.has_random;
    has_network = a.has_network || b.has_network }

(* F#: `Set.isSubset a b` — every factor of `a` is in `b`. *)
let det_subset (a b:determinism_source) : Tot bool =
  (not a.has_clock || b.has_clock) &&
  (not a.has_random || b.has_random) &&
  (not a.has_network || b.has_network)

(* F#: `Effect.join` — the host axis widest, the determinism axis the union. *)
let join (a b:effect_class) : Tot effect_class =
  { host = host_of (max_int (host_rank a.host) (host_rank b.host));
    determinism = det_union a.determinism b.determinism }

(* F#: `Effect.covers` — the declared host at least as wide, and the declared determinism a SUPERSET
   of the actual: `Set.isSubset actual.Determinism declared.Determinism`. *)
let covers (declared actual:effect_class) : Tot bool =
  host_rank declared.host >= host_rank actual.host &&
  det_subset actual.determinism declared.determinism

(* F#: `Effect.determinismTag` — the canonical label of a set: `"deterministic"` for the empty set,
   otherwise the member factors' names in the fixed order clock, random, network, joined by `+`.
   Written as the eight labels the rendering produces, so that nothing about the joining is left
   to the prover's string reasoning. *)
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

(* F#: `Effect.tryDeterminismOfTag` — the inverse: the set a CANONICAL label names, `None` for any
   other string (a reordered, repeated, empty or unknown member; `deterministic` beside a factor). *)
let det_of_tag (s:string) : Tot (option determinism_source) =
  if s = "deterministic" then Some deterministic
  else if s = "clock" then Some { has_clock = true; has_random = false; has_network = false }
  else if s = "random" then Some { has_clock = false; has_random = true; has_network = false }
  else if s = "network" then Some { has_clock = false; has_random = false; has_network = true }
  else if s = "clock+random" then Some { has_clock = true; has_random = true; has_network = false }
  else if s = "clock+network" then Some { has_clock = true; has_random = false; has_network = true }
  else if s = "random+network" then Some { has_clock = false; has_random = true; has_network = true }
  else if s = "clock+random+network" then Some { has_clock = true; has_random = true; has_network = true }
  else None

(* The host table is inverse on the ranks it produces — the fact the host half of every lattice
   law rests on. (The determinism axis needs no such fact: the union is pointwise.) *)
let host_of_rank (h:host_effect) : Lemma (host_of (host_rank h) == h) = ()

(* The label is a BIJECTION between the eight sets and the eight canonical labels, in both
   directions: a set has exactly one wire spelling, and a label names at most one set. *)
let det_tag_roundtrip (d:determinism_source) : Lemma (det_of_tag (determinism_tag d) == Some d) = ()

let det_tag_canonical (s:string) (d:determinism_source)
  : Lemma (requires det_of_tag s == Some d) (ensures determinism_tag d == s) = ()

let det_tag_injective (a b:determinism_source)
  : Lemma (requires determinism_tag a == determinism_tag b) (ensures a == b) = ()

let join_comm (a b:effect_class) : Lemma (join a b == join b a) = ()

let join_idem (a:effect_class) : Lemma (join a a == a) =
  host_of_rank a.host

let join_pure (a:effect_class)
  : Lemma (join pure_deterministic a == a /\ join a pure_deterministic == a)
  = host_of_rank a.host

let join_assoc (a b c:effect_class) : Lemma (join (join a b) c == join a (join b c)) = ()

let covers_refl (a:effect_class) : Lemma (covers a a) = ()

let covers_trans (a b c:effect_class)
  : Lemma (requires covers a b /\ covers b c) (ensures covers a c) = ()

let covers_antisym (a b:effect_class)
  : Lemma (requires covers a b /\ covers b a) (ensures a == b) = ()

(* The join is an upper bound of both parts ... *)
let covers_join (a b:effect_class) : Lemma (covers (join a b) a /\ covers (join a b) b) = ()

(* ... and the LEAST one: a class covers the join exactly when it covers both parts. This is
   "never lower" — any class assigned to a composition that is below a part fails `covers`. *)
let join_least (a b c:effect_class)
  : Lemma (covers c (join a b) <==> (covers c a /\ covers c b)) = ()

(* ======================================================================================
   2. The value spaces — `ValueSpace`, `Space.validate`, `Space.isBounded`, with the three
      scalar readers as a parameter (the readers premise).
   ====================================================================================== *)

(* F#: `ValueSpace`. `FloatRange`'s bounds are opaque carriers here (the bridge renders a float
   through the round-trip `R` format), because the model has no float and the reader below is
   what compares against them. *)
type value_space =
  | IntRange   : lo:int -> hi:int -> value_space
  | FloatRange : lo:string -> hi:string -> value_space
  | StringLen  : lo:int -> hi:int -> value_space
  | Enum       : list string -> value_space
  | AnyString  : value_space
  | SlotTree   : option string -> value_space

(* F#: `SpaceFault` (Phase 307) — why a space is not well-formed. *)
type space_fault =
  | SEmpty
  | SNonFinite

(* The readers premise: the five host functions `Space.validate` and `Space.wellFormed` reach for.
   `int_of`      — F#: `Space.readInt` — since Phase 295 an optional `-` and decimal digits read
                   under the INVARIANT culture (`Json.readInt32`), no `+`, no white space, no
                   culture's own minus sign. A function of the string ALONE: the same answer under
                   every culture, which the bridge holds by running under he-IL (Phase 307).
   `float_in`    — F#: `Space.readFloat` (the invariant JSON number grammar, finite) of the value,
                   then `lo <= v && v <= hi` against the two carriers' parsed floats.
   `str_len`     — F#: `String.Length`.
   `kind_of`     — F#: `Space.slotKindOf` (Phase 229): the `"kind"` tag of a wire document whose top
                   level is a kind-tagged object, `None` for anything else.
   `float_fault` — F#: `Space.wellFormed` over `FloatRange(lo, hi)` (Phase 307): `SNonFinite` when
                   either carrier is NaN or infinite, else `SEmpty` when `lo > hi`, else nothing. *)
noeq type readers = {
  int_of:      string -> option int;
  float_in:    string -> string -> string -> bool;
  str_len:     string -> nat;
  kind_of:     string -> option string;
  float_fault: string -> string -> option space_fault
}

(* F#: `Space.validate`. *)
let validate (rd:readers) (space:value_space) (s:string) : Tot bool =
  match space with
  | IntRange lo hi ->
    (match rd.int_of s with
     | Some v -> v >= lo && v <= hi
     | None -> false)
  | FloatRange lo hi -> rd.float_in lo hi s
  | StringLen lo hi -> rd.str_len s >= lo && rd.str_len s <= hi
  | Enum xs -> mem s xs
  | AnyString -> true
  | SlotTree c ->
    (match rd.kind_of s with
     | None -> false
     | Some k -> (match c with
                  | None -> true
                  | Some kc -> k = kc))

(* F#: `Space.maxRepeatCount` (Phase 307) — the declared cap a count space sits under. *)
let max_repeat_count : int = 1000000

(* F#: `Space.isCount` (Phase 307) — the totality criterion for repeats: a capped, non-empty,
   non-negative `IntRange`. It replaced `isBounded` ("neither `AnyString` nor `SlotTree`"), under
   which a repeat over `FloatRange(0, infinity)` was total. *)
let is_count (space:value_space) : Tot bool =
  match space with
  | IntRange lo hi -> 0 <= lo && lo <= hi && hi <= max_repeat_count
  | _ -> false

(* F#: `Space.wellFormed` (Phase 307). The float case is the readers premise's: the model has no
   float, and its carriers are opaque. *)
let space_wf (rd:readers) (space:value_space) : Tot (option space_fault) =
  match space with
  | IntRange lo hi -> if lo > hi then Some SEmpty else None
  | FloatRange lo hi -> rd.float_fault lo hi
  | StringLen lo hi -> if lo > hi || hi < 0 then Some SEmpty else None
  | Enum [] -> Some SEmpty
  | _ -> None

(* ======================================================================================
   3. Holes, the signature and the two error vocabularies.
   ====================================================================================== *)

(* F#: `HoleKind`. *)
type hole_kind =
  | ValueHole  : value_space -> hole_kind
  | SlotHole   : option string -> hole_kind
  | RepeatHole : value_space -> hole_kind
  | ActionHole : effect_class -> hole_kind

(* F#: `HoleDecl` — `Addr` is the absolute lexical address, the hygiene surface. *)
type hole_decl = { h_addr: string; h_name: string; h_kind: hole_kind }

(* F#: `SigEntry`. *)
type sig_entry = {
  s_addr: string;
  s_name: string;
  s_kind: string;
  s_space: option value_space;
  s_slot: option string;
  s_action: option effect_class;
  s_required: bool
}

(* F#: `Signature`. *)
type signature = { sg_name: string; sg_holes: list sig_entry; sg_effect: effect_class }

(* F#: `Arg<'Node>`. *)
type arg (node:Type) =
  | ValueArg : string -> arg node
  | SlotArg  : node -> arg node

(* F#: `DeclarationFault` (Phase 307) — the admission gate's vocabulary. *)
type decl_fault =
  | EmptySpace        : addr:string -> space:value_space -> decl_fault
  | NonFiniteBound    : addr:string -> decl_fault
  | DuplicateHoleAddr : addr:string -> decl_fault
  | HoleUnderSlot     : node:string -> decl_fault

(* F#: `ApplyError`. *)
type apply_error =
  | UnknownHoleAddr     : addr:string -> declared:list string -> apply_error
  | ValueOutOfSpace     : addr:string -> space:value_space -> got:string -> apply_error
  | RequiredHolesUnbound: addrs:list string -> apply_error
  | NotASlot            : addr:string -> apply_error
  | SlotKindMismatch    : addr:string -> expected:string -> got:string -> apply_error
  | NonTotal            : addr:string -> apply_error
  | BindFailed          : addr:string -> reason:string -> apply_error
  | SlotArgOpen         : addr:string -> holes:list string -> apply_error
  | IllFormedResult     : fault:decl_fault -> apply_error

(* F#: `InvokeError`. *)
type invoke_error =
  | NoSuchCapability   : id:string -> known:list string -> invoke_error
  | DuplicateCapability: id:string -> invoke_error
  | UnknownArg         : addr:string -> declared:list string -> invoke_error
  | ArgOutOfSpace      : addr:string -> space:value_space -> got:string -> invoke_error
  | RequiredArgsUnbound: addrs:list string -> invoke_error
  | UninvocableArg     : addr:string -> invoke_error
  | BodyFailed         : reason:string -> invoke_error
  | NonTotalCapability : id:string -> addrs:list string -> invoke_error
  | IllFormedCapability: id:string -> fault:decl_fault -> invoke_error
  | DuplicateArg       : addr:string -> invoke_error

(* F#: `holes |> List.tryFind (fun h -> h.Addr = addr)` on declared holes. *)
let rec find_hole (k:string) (holes:list hole_decl) : Tot (option hole_decl) =
  match holes with
  | [] -> None
  | h :: t -> if h.h_addr = k then Some h else find_hole k t

(* F#: `holes |> List.tryFind (fun h -> h.Addr = addr)` on signature entries. *)
let rec find_entry (k:string) (holes:list sig_entry) : Tot (option sig_entry) =
  match holes with
  | [] -> None
  | h :: t -> if h.s_addr = k then Some h else find_entry k t

(* F#: `holes |> List.map (fun h -> h.Addr)` on declared holes. *)
let addr_of (h:hole_decl) : Tot string = h.h_addr

(* F#: `holes |> List.map (fun h -> h.Addr)` on signature entries. *)
let rec entry_addrs (holes:list sig_entry) : Tot (list string) =
  match holes with
  | [] -> []
  | h :: t -> h.s_addr :: entry_addrs t

(* F#: `args |> Map.toList |> List.map fst |> List.tryFind (fun a -> not (List.contains a declared))`. *)
let rec first_unknown (declared:list string) (ks:list string) : Tot (option string) =
  match ks with
  | [] -> None
  | k :: t -> if not (mem k declared) then Some k else first_unknown declared t

(* F#: `sg.Holes |> List.filter (fun e -> not (boundAddrs.Contains e.Addr))`. *)
let rec excluding (bound:list string) (holes:list sig_entry) : Tot (list sig_entry) =
  match holes with
  | [] -> []
  | e :: t -> if not (mem e.s_addr bound) then e :: excluding bound t else excluding bound t

(* ======================================================================================
   4. The witness — `ArtifactWitness<'Node, 'Id>` as the algebra reads it — and the function
      algebra: `signature`, `signatureExcluding`, `isTotal`, `guardTotal`, `validateArg`,
      `bindArgs`, `apply`, `curry`, `composedEffect`, `compose`, `observedEffect`, `auditEffect`.
   ====================================================================================== *)

(* F#: the fields of `ArtifactWitness` the algebra reads (`Holes`, `Effect`, `Bind`), the
   `NodeWitness.KindTag` and `Children` under `Tree`, `Tree.preorder w.Tree` — the one derived walk
   `observedEffect` and `validate` make — and, since Phase 307, `IdW.ToString (Tree.Id n)`, the
   name `validate` gives the node a `HoleUnderSlot` is at. *)
noeq type witness (node:Type) = {
  holes:     node -> list hole_decl;
  eff:       node -> effect_class;
  bind_hole: string -> arg node -> node -> outcome node string;
  kind_tag:  node -> string;
  preorder:  node -> list node;
  children:  node -> list node;
  node_id:   node -> string
}

(* F#: `Function.signature`'s local `entry`. *)
let entry_of (h:hole_decl) : Tot sig_entry =
  match h.h_kind with
  | ValueHole s ->
    { s_addr = h.h_addr; s_name = h.h_name; s_kind = "value";
      s_space = Some s; s_slot = None; s_action = None; s_required = true }
  | SlotHole c ->
    { s_addr = h.h_addr; s_name = h.h_name; s_kind = "slot";
      s_space = Some (SlotTree c); s_slot = c; s_action = None; s_required = true }
  | RepeatHole s ->
    (* Phase 295: a TOTAL repeat is required, as strict `bind_args` demands it (Phase 307: a
       repeat over a count space). *)
    { s_addr = h.h_addr; s_name = h.h_name; s_kind = "repeat";
      s_space = Some s; s_slot = None; s_action = None; s_required = is_count s }
  | ActionHole e ->
    { s_addr = h.h_addr; s_name = h.h_name; s_kind = "action";
      s_space = None; s_slot = None; s_action = Some e; s_required = false }

(* F#: `Function.signature`. *)
let signature_of (#node:Type) (w:witness node) (name:string) (n:node) : Tot signature =
  { sg_name = name; sg_holes = map entry_of (w.holes n); sg_effect = w.eff n }

(* F#: `Function.signatureExcluding`. *)
let signature_excluding (bound:list string) (sg:signature) : Tot signature =
  { sg with sg_holes = excluding bound sg.sg_holes }

(* F#: `Function.isTotal`'s per-entry predicate, over `SigEntry.HoleKind` (Phase 295): a repeat over a
   bounded count is total, every other hole kind is, and an entry that projects to no hole kind (an
   unknown tag, or a tag without the payload it needs) is not. *)
let entry_total (e:sig_entry) : Tot bool =
  match e.s_kind, e.s_space, e.s_action with
  | "value", Some _, _ -> true
  | "slot", _, _ -> true
  | "repeat", Some s, _ -> is_count s
  | "action", _, Some _ -> true
  | _ -> false

(* F#: `Function.isTotal`. *)
let is_total (sg:signature) : Tot bool = for_all entry_total sg.sg_holes

(* F#: `Signature.validate`'s per-entry space check — the fault `Space.wellFormed` gives the entry's
   space, by address. *)
let entry_space_fault (rd:readers) (e:sig_entry) : Tot (option decl_fault) =
  match e.s_space with
  | None -> None
  | Some sp ->
    (match space_wf rd sp with
     | Some SEmpty -> Some (EmptySpace e.s_addr sp)
     | Some SNonFinite -> Some (NonFiniteBound e.s_addr)
     | None -> None)

(* F#: `Signature.validate` (Phase 307) — the first fault in declaration order: an address an
   earlier entry holds, or a space `Space.wellFormed` refuses. *)
let rec validate_entries (rd:readers) (seen:list string) (holes:list sig_entry)
  : Tot (option decl_fault) (decreases holes) =
  match holes with
  | [] -> None
  | e :: rest ->
    if mem e.s_addr seen then Some (DuplicateHoleAddr e.s_addr)
    else
      (match entry_space_fault rd e with
       | Some f -> Some f
       | None -> validate_entries rd (e.s_addr :: seen) rest)

let validate_signature (rd:readers) (sg:signature) : Tot (option decl_fault) =
  validate_entries rd [] sg.sg_holes

(* F#: `holeUnderSlot`'s count — the slot holes among a hole list. *)
let rec slot_count (hs:list hole_decl) : Tot nat =
  match hs with
  | [] -> 0
  | h :: t -> (if SlotHole? h.h_kind then 1 else 0) + slot_count t

let rec sum_slots (#node:Type) (w:witness node) (cs:list node) : Tot nat =
  match cs with
  | [] -> 0
  | c :: t -> slot_count (w.holes c) + sum_slots w t

let rec any_holes (#node:Type) (w:witness node) (cs:list node) : Tot bool =
  match cs with
  | [] -> false
  | c :: t -> Cons? (w.holes c) || any_holes w t

(* F#: `holeUnderSlot`'s per-node test — the node declares a slot of its own (its subtree holds
   more slots than its children's subtrees do) and its children's subtrees hold a hole. *)
let slot_over_holes (#node:Type) (w:witness node) (n:node) : Tot (option decl_fault) =
  let cs = w.children n in
  if any_holes w cs && slot_count (w.holes n) > sum_slots w cs then Some (HoleUnderSlot (w.node_id n))
  else None

(* F#: `Function.validate` (Phase 307) — `Signature.validate` over the derived signature, then the
   first node in preorder that declares a slot over holes. *)
let validate_decl (#node:Type) (rd:readers) (w:witness node) (n:node) : Tot (option decl_fault) =
  match validate_signature rd (signature_of w "" n) with
  | Some f -> Some f
  | None -> try_pick (slot_over_holes w) (w.preorder n)

(* F#: `guardTotal`'s per-hole pick. *)
let non_total (h:hole_decl) : Tot (option apply_error) =
  match h.h_kind with
  | RepeatHole s -> if not (is_count s) then Some (NonTotal h.h_addr) else None
  | _ -> None

(* F#: `guardTotal`. *)
let guard_total (holes:list hole_decl) : Tot (option apply_error) = try_pick non_total holes

(* F#: `validateArg`. *)
let validate_arg (#node:Type) (rd:readers) (w:witness node) (addr:string) (k:hole_kind) (a:arg node)
  : Tot (outcome unit apply_error) =
  match k, a with
  | ValueHole space, ValueArg s ->
    if validate rd space s then Ok () else Error (ValueOutOfSpace addr space s)
  | RepeatHole space, ValueArg s ->
    if not (is_count space) then Error (NonTotal addr)
    else if validate rd space s then Ok ()
    else Error (ValueOutOfSpace addr space s)
  | SlotHole c, SlotArg inner ->
    (match c with
     | Some kt -> if w.kind_tag inner <> kt then Error (SlotKindMismatch addr kt (w.kind_tag inner)) else Ok ()
     | None -> Ok ())
  | ValueHole _, SlotArg _ -> Error (NotASlot addr)
  | RepeatHole _, SlotArg _ -> Error (NotASlot addr)
  | SlotHole _, ValueArg _ -> Error (NotASlot addr)
  | ActionHole _, _ -> Error (NotASlot addr)

(* F#: `bindArgs`'s action-hole filter — the data axis only. *)
let is_data (h:hole_decl) : Tot bool =
  match h.h_kind with
  | ActionHole _ -> false
  | _ -> true

let data_holes (#node:Type) (w:witness node) (n:node) : Tot (list hole_decl) = filter is_data (w.holes n)

(* F#: `Map<string, Arg<'Node>>`, read as its key-ordered list. *)
type args (node:Type) = list (string & arg node)

(* F#: `bindArgs`'s local `go` — the fold over holes in declaration order, threading the
   re-derived tree, keyed on the absolute address and nothing else. *)
let rec bind_walk (#node:Type) (rd:readers) (w:witness node) (strict:bool) (a:args node)
                  (cur:node) (unbound:list string) (holes:list hole_decl)
  : Tot (outcome node apply_error) (decreases holes) =
  match holes with
  | [] ->
    (match unbound with
     | [] -> Ok cur
     | _ -> if strict then Error (RequiredHolesUnbound (rev unbound)) else Ok cur)
  | h :: rest ->
    match assoc h.h_addr a with
    | None -> bind_walk rd w strict a cur (h.h_addr :: unbound) rest
    | Some x ->
      match validate_arg rd w h.h_addr h.h_kind x with
      | Error e -> Error e
      | Ok () ->
        match w.bind_hole h.h_addr x cur with
        | Ok cur' -> bind_walk rd w strict a cur' unbound rest
        | Error m -> Error (BindFailed h.h_addr m)

(* F#: `bindArgs`'s strict pre-pass (Phase 307) — the first slot argument, in key order, whose tree
   still has open data holes. *)
let open_slot (#node:Type) (w:witness node) (b:(string & arg node)) : Tot (option apply_error) =
  match b with
  | (k, SlotArg sub) ->
    (match data_holes w sub with
     | [] -> None
     | hs -> Some (SlotArgOpen k (map addr_of hs)))
  | (_, ValueArg _) -> None

(* F#: `bindArgs`. *)
let bind_args (#node:Type) (rd:readers) (w:witness node) (strict:bool) (a:args node) (n:node)
  : Tot (outcome node apply_error) =
  let holes = data_holes w n in
  match guard_total holes with
  | Some e -> Error e
  | None ->
    let declared = map addr_of holes in
    match first_unknown declared (keys a) with
    | Some unknown -> Error (UnknownHoleAddr unknown declared)
    | None ->
      match (if strict then try_pick (open_slot w) a else None) with
      | Some e -> Error e
      | None -> bind_walk rd w strict a n [] holes

(* F#: `Function.apply`. *)
let apply (#node:Type) (rd:readers) (w:witness node) (a:args node) (n:node) : Tot (outcome node apply_error) =
  bind_args rd w true a n

(* F#: `Function.curry`. *)
let curry (#node:Type) (rd:readers) (w:witness node) (a:args node) (n:node) : Tot (outcome node apply_error) =
  bind_args rd w false a n

(* F#: `Function.composedEffect`. *)
let composed_effect (#node:Type) (w:witness node) (inner outer:node) : Tot effect_class =
  join (w.eff outer) (w.eff inner)

(* F#: `compose`'s tail — the one `Bind` it makes, at the slot's address, and since Phase 307 the
   check over the tree it built (`IllFormedResult`). *)
let wire (#node:Type) (rd:readers) (w:witness node) (slot_addr:string) (inner outer:node) : Tot (outcome node apply_error) =
  match w.bind_hole slot_addr (SlotArg inner) outer with
  | Ok n -> (match validate_decl rd w n with
             | None -> Ok n
             | Some f -> Error (IllFormedResult f))
  | Error m -> Error (BindFailed slot_addr m)

(* F#: `Function.compose` — since Phase 295 it checks totality first, on both parts, as
   `composeAcross` does: the outer's holes, then the inner's. *)
let compose (#node:Type) (rd:readers) (w:witness node) (slot_addr:string) (inner outer:node) : Tot (outcome node apply_error) =
  let holes = w.holes outer in
  match guard_total holes with
  | Some e -> Error e
  | None ->
  match guard_total (w.holes inner) with
  | Some e -> Error e
  | None ->
  match find_hole slot_addr holes with
  | None -> Error (UnknownHoleAddr slot_addr (map addr_of holes))
  | Some h ->
    match h.h_kind with
    | SlotHole c ->
      (match c with
       | Some kt -> if w.kind_tag inner <> kt then Error (SlotKindMismatch slot_addr kt (w.kind_tag inner)) else wire rd w slot_addr inner outer
       | None -> wire rd w slot_addr inner outer)
    | _ -> Error (NotASlot slot_addr)

(* F#: `Function.observedEffect` — the join over the preorder walk, `pureDeterministic` the identity. *)
let observed_effect (#node:Type) (w:witness node) (n:node) : Tot effect_class =
  fold_left join pure_deterministic (map w.eff (w.preorder n))

(* F#: `Function.auditEffect`. *)
let audit_effect (#node:Type) (w:witness node) (n:node) : Tot (outcome unit (effect_class & effect_class)) =
  let declared = w.eff n in
  let actual = observed_effect w n in
  if covers declared actual then Ok () else Error (declared, actual)

(* ======================================================================================
   5. The capability seam — `Capability`, `Capability.create` / `validateArgs` / `invoke`,
      `CapabilityRegistry` and its `empty` / `register` / `tryFind` / `enumerate` / `dispatch`.
   ====================================================================================== *)

(* F#: `IslandKind`. *)
type island_kind =
  | Pyodide
  | Fable
  | Js

(* F#: `Placement`. *)
type placement =
  | BuildTime
  | Server
  | ClientDeclarative
  | ClientIsland : island_kind -> placement
  | Precomputed

(* F#: `Capability`. *)
type capability = {
  c_id: string;
  c_signature: signature;
  c_determinism: determinism_source;
  c_placement: placement
}

(* F#: `Capability.create` — `Determinism` derived from the signature, never disagreeing with it. *)
let create (id:string) (sg:signature) (p:placement) : Tot capability =
  { c_id = id; c_signature = sg; c_determinism = sg.sg_effect.determinism; c_placement = p }

(* F#: `Capability.determinismTag`. *)
let determinism_tag_of (c:capability) : Tot string = determinism_tag c.c_determinism

(* THE CAPTURE RECORDS THE WHOLE CLASS (Phase 319). F#: the law `capabilityLaws` samples — the label
   a capture journals is `Capability.determinismTag`, it decodes back to EXACTLY the capability's
   declared determinism set, and so the effect a capture records covers every factor a body
   exercises that the declaration names. The chain this replaced could not say this: a `Random`
   class covered a clock read without naming it. *)
let capture_records_declared (c:capability)
  : Lemma (det_of_tag (determinism_tag_of c) == Some c.c_determinism)
  = det_tag_roundtrip c.c_determinism

(* A body exercising a set inside the declared one is covered by what the capture records, and a
   body exercising a factor outside it is NOT — the exercised set is named by the difference. *)
let capture_covers_exercised (c:capability) (host:host_effect) (exercised:determinism_source)
  : Lemma (
      (det_subset exercised c.c_determinism ==>
         covers { host = host; determinism = c.c_determinism } { host = host; determinism = exercised }) /\
      (not (det_subset exercised c.c_determinism) ==>
         not (covers { host = host; determinism = c.c_determinism } { host = host; determinism = exercised })))
  = ()

(* F#: `(string * string) list` — a typed invocation's args, addr → value. *)
type invocation = list (string & string)

(* F#: `Capability.argSpace` (Phase 307) — `Function.slotSpaceOf`: an entry's own space, or for a
   slot entry built by hand before Phase 229 (spaceless) the tree space of its constraint. *)
let arg_space (e:sig_entry) : Tot (option value_space) =
  match e.s_kind, e.s_space with
  | "slot", None -> Some (SlotTree e.s_slot)
  | _, sp -> sp

(* F#: `Capability.repeatedAddrs` — every key the list binds again, at each repeat, in order. *)
let rec repeated (seen:list string) (ks:list string) : Tot (list string) (decreases ks) =
  match ks with
  | [] -> []
  | k :: t -> if mem k seen then k :: repeated seen t else repeated (k :: seen) t

(* F#: `validateArgs`'s local `checkArgs` — step 1, in the caller's arg order. *)
let rec check_args (rd:readers) (holes:list sig_entry) (declared:list string) (a:invocation)
  : Tot (outcome unit invoke_error) (decreases a) =
  match a with
  | [] -> Ok ()
  | (addr, value) :: rest ->
    match find_entry addr holes with
    | None -> Error (UnknownArg addr declared)
    | Some h ->
      match arg_space h with
      | None -> Error (UninvocableArg addr)
      | Some space ->
        if SlotTree? space && None? (rd.kind_of value) then Error (UninvocableArg addr)
        else if validate rd space value then check_args rd holes declared rest
        else Error (ArgOutOfSpace addr space value)

(* F#: `validateArgs`'s step 2 — the required holes the args leave unbound. *)
let rec unbound_required (holes:list sig_entry) (a:invocation) : Tot (list string) =
  match holes with
  | [] -> []
  | h :: t ->
    if h.s_required && not (has_key h.s_addr a) then h.s_addr :: unbound_required t a
    else unbound_required t a

(* F#: `Capability.validateArgs`. *)
let validate_args (rd:readers) (c:capability) (a:invocation) : Tot (outcome unit invoke_error) =
  let holes = c.c_signature.sg_holes in
  let declared = entry_addrs holes in
  match repeated [] (keys a) with
  | d :: _ -> Error (DuplicateArg d)
  | [] ->
  match check_args rd holes declared a with
  | Error e -> Error e
  | Ok () ->
    match unbound_required holes a with
    | [] -> Ok ()
    | u -> Error (RequiredArgsUnbound u)

(* F#: `Deferred<'T>` — the async-result envelope the body answers in (Phase 210). The failure rides
   as a rendered string, which is what `BodyFailed` names it with; the TYPED error axis stays on the
   outer `outcome`. Restated here rather than opened, like `outcome` and the list helpers above. *)
type deferred (a:Type) =
  | Pending : deferred a
  | Ready   : a -> deferred a
  | Failed  : string -> deferred a

(* F#: `Capability.invoke` — validate, THEN the host body; a body failure is named, never thrown.
   Since Phase 210 the body answers in the envelope: `Ready` and `Pending` ride out unchanged inside
   an `Ok`, and only `Failed` crosses into the typed refusal. *)
let invoke (#v:Type) (rd:readers) (c:capability) (a:invocation) (body:unit -> deferred v)
  : Tot (outcome (deferred v) invoke_error) =
  match validate_args rd c a with
  | Error e -> Error e
  | Ok () ->
    match body () with
    | Ready x -> Ok (Ready x)
    | Pending -> Ok Pending
    | Failed m -> Error (BodyFailed m)

(* F#: `CapabilityRegistry` — a `Map<string, Capability>` keyed by `Id`, read as a finite map. *)
type registry = { capabilities: list capability }

(* F#: `CapabilityRegistry.empty`. *)
let empty : registry = { capabilities = [] }

(* F#: `Map.tryFind id r.Capabilities`. *)
let rec find_cap (id:string) (cs:list capability) : Tot (option capability) =
  match cs with
  | [] -> None
  | c :: t -> if c.c_id = id then Some c else find_cap id t

(* F#: `r.Capabilities |> Map.toList |> List.map fst` — the ids, as `NoSuchCapability` names them. *)
let rec ids (cs:list capability) : Tot (list string) =
  match cs with
  | [] -> []
  | c :: t -> c.c_id :: ids t

(* F#: `Capability.nonTotalAddrs` — the entries `entry_total` refuses, by address. *)
let rec non_total_addrs (holes:list sig_entry) : Tot (list string) =
  match holes with
  | [] -> []
  | e :: t -> if entry_total e then non_total_addrs t else e.s_addr :: non_total_addrs t

(* F#: `CapabilityRegistry.register` — additive, no silent overwrite, and (Phase 295) only a total
   capability: a non-total one is refused `NonTotalCapability`, naming its non-total entries; and
   (Phase 307) only a well-formed one: `IllFormedCapability` with `Signature.validate`'s fault. *)
let register (rd:readers) (c:capability) (r:registry) : Tot (outcome registry invoke_error) =
  match find_cap c.c_id r.capabilities with
  | Some _ -> Error (DuplicateCapability c.c_id)
  | None ->
    if not (is_total c.c_signature) then Error (NonTotalCapability c.c_id (non_total_addrs c.c_signature.sg_holes))
    else
      match validate_signature rd c.c_signature with
      | Some f -> Error (IllFormedCapability c.c_id f)
      | None -> Ok { capabilities = c :: r.capabilities }

(* F#: `CapabilityRegistry.tryFind`. *)
let try_find_cap (id:string) (r:registry) : Tot (option capability) = find_cap id r.capabilities

(* F#: `CapabilityRegistry.enumerate` — the discovery surface. Production sorts by id; the model returns
   the map's entries, and the theorem below is about membership. *)
let enumerate (r:registry) : Tot (list capability) = r.capabilities

(* F#: `CapabilityRegistry.dispatch` — resolve the id (default-deny), then `Capability.invoke`. *)
let dispatch (#v:Type) (rd:readers) (r:registry) (id:string) (a:invocation)
             (body:capability -> unit -> deferred v)
  : Tot (outcome (deferred v) invoke_error) =
  match find_cap id r.capabilities with
  | None -> Error (NoSuchCapability id (ids r.capabilities))
  | Some c -> invoke rd c a (body c)

(* ======================================================================================
   6. THE FIRST THEOREM — an unregistered id is refused, and no handler runs.
   ====================================================================================== *)

let rec find_cap_mem (id:string) (cs:list capability)
  : Lemma (Some? (find_cap id cs) <==> mem id (ids cs))
  = match cs with
    | [] -> ()
    | _ :: t -> find_cap_mem id t

(* THE FIRST THEOREM. F#: `CapabilityRegistry.dispatch` on an id the registry does not hold. The refusal is
   the typed `NoSuchCapability`, naming the id and every id it does hold — and the result is the
   same under EVERY host body, which is what "runs no handler" means for a pure function. *)
let unregistered_refused (#v:Type) (rd:readers) (r:registry) (id:string) (a:invocation)
                         (body body':capability -> unit -> deferred v)
  : Lemma (requires not (mem id (ids (enumerate r))))
          (ensures dispatch rd r id a body == Error (NoSuchCapability id (ids r.capabilities)) /\
                   dispatch rd r id a body == dispatch rd r id a body')
  = find_cap_mem id r.capabilities

(* `invoke` never produces the registry's two refusals: the classes are disjoint by construction. *)
let rec check_args_shape (rd:readers) (holes:list sig_entry) (declared:list string) (a:invocation)
  : Lemma (ensures (match check_args rd holes declared a with
                    | Ok () -> True
                    | Error e -> UnknownArg? e \/ UninvocableArg? e \/ ArgOutOfSpace? e))
          (decreases a)
  = match a with
    | [] -> ()
    | (addr, value) :: rest ->
      (match find_entry addr holes with
       | None -> ()
       | Some h ->
         (match arg_space h with
          | None -> ()
          | Some space -> if validate rd space value then check_args_shape rd holes declared rest else ()))

let validate_args_shape (rd:readers) (c:capability) (a:invocation)
  : Lemma (ensures (match validate_args rd c a with
                    | Ok () -> True
                    | Error e -> DuplicateArg? e \/ UnknownArg? e \/ UninvocableArg? e \/ ArgOutOfSpace? e \/ RequiredArgsUnbound? e))
  = check_args_shape rd c.c_signature.sg_holes (entry_addrs c.c_signature.sg_holes) a

let invoke_never_registry_refusal (#v:Type) (rd:readers) (c:capability) (a:invocation) (body:unit -> deferred v)
  : Lemma (ensures (match invoke rd c a body with
                    | Error (NoSuchCapability _ _) -> False
                    | Error (DuplicateCapability _) -> False
                    | _ -> True))
  = validate_args_shape rd c a

(* ======================================================================================
   7. THE SECOND THEOREM — validation before invocation: no handler runs on a rejected set.
   ====================================================================================== *)

(* THE SECOND THEOREM. F#: `Capability.invoke` is `validateArgs |> Result.bind (body)`. When
   validation rejects, `invoke` IS that rejection — for every body alike, so no body ran. *)
let validate_before_invoke (#v:Type) (rd:readers) (c:capability) (a:invocation)
                           (body body':unit -> deferred v)
  : Lemma (requires Error? (validate_args rd c a))
          (ensures (match validate_args rd c a with
                    | Error e -> invoke rd c a body == Error e
                    | Ok () -> True) /\
                   invoke rd c a body == invoke rd c a body')
  = ()

(* The registry-level restatement: a dispatch that resolved its id still runs nothing on a set
   validation rejects. *)
let dispatch_validates_first (#v:Type) (rd:readers) (r:registry) (id:string) (a:invocation)
                             (body body':capability -> unit -> deferred v)
  : Lemma (requires (match find_cap id r.capabilities with
                     | Some c -> Error? (validate_args rd c a)
                     | None -> False))
          (ensures (match find_cap id r.capabilities with
                    | Some c ->
                      (match validate_args rd c a with
                       | Error e -> dispatch rd r id a body == Error e
                       | Ok () -> True)
                    | None -> True) /\
                   dispatch rd r id a body == dispatch rd r id a body')
  = ()

(* The converse: a body ran — an accepted result, or a `BodyFailed` — only past an accepted
   validation. *)
let body_runs_only_validated (#v:Type) (rd:readers) (c:capability) (a:invocation) (body:unit -> deferred v)
  : Lemma (ensures (match invoke rd c a body with
                    | Ok _ -> validate_args rd c a == Ok ()
                    | Error (BodyFailed _) -> validate_args rd c a == Ok ()
                    | _ -> True))
  = validate_args_shape rd c a

(* Phase 210, the envelope's fourth outcome: THERE IS NONE. An accepted invocation carries the body's
   `Ready` or `Pending` and nothing else — a body's `Failed` crossed into the typed `BodyFailed`
   above, so `Ok (Failed _)` is not merely unproduced but unreachable, for every body and every
   argument set alike. This is the claim `capabilityLaws` samples; here it is over all of them. Its
   registry-level restatement follows, because a host reaches the seam through `dispatch`. *)
let invoke_never_ok_failed (#v:Type) (rd:readers) (c:capability) (a:invocation) (body:unit -> deferred v)
  : Lemma (ensures (match invoke rd c a body with
                    | Ok (Failed _) -> False
                    | _ -> True))
  = ()

let dispatch_never_ok_failed (#v:Type) (rd:readers) (r:registry) (id:string) (a:invocation)
                             (body:capability -> unit -> deferred v)
  : Lemma (ensures (match dispatch rd r id a body with
                    | Ok (Failed _) -> False
                    | _ -> True))
  = match find_cap id r.capabilities with
    | None -> ()
    | Some c -> invoke_never_ok_failed rd c a (body c)

(* Every key addresses a declared entry. *)
let rec all_declared (holes:list sig_entry) (ks:list string) : Tot bool =
  match ks with
  | [] -> true
  | k :: t -> Some? (find_entry k holes) && all_declared holes t

(* What an ACCEPTED validation guarantees: every arg addresses a declared hole, and every required
   hole is bound. *)
let rec check_args_ok_declared (rd:readers) (holes:list sig_entry) (declared:list string) (a:invocation)
  : Lemma (requires check_args rd holes declared a == Ok ())
          (ensures all_declared holes (keys a))
          (decreases a)
  = match a with
    | [] -> ()
    | (addr, value) :: rest ->
      (match find_entry addr holes with
       | None -> ()
       | Some h ->
         (match arg_space h with
          | None -> ()
          | Some space -> if validate rd space value then check_args_ok_declared rd holes declared rest else ()))

let validate_args_sound (rd:readers) (c:capability) (a:invocation)
  : Lemma (requires validate_args rd c a == Ok ())
          (ensures all_declared c.c_signature.sg_holes (keys a) /\
                   unbound_required c.c_signature.sg_holes a == [])
  = check_args_ok_declared rd c.c_signature.sg_holes (entry_addrs c.c_signature.sg_holes) a

(* What a REFUSAL guarantees: an `ArgOutOfSpace` names a value the named space really refuses, an
   `UnknownArg` names an address no hole declares, an `UninvocableArg` names a spaceless entry
   or a tree-spaced one (Phase 229: a slot bound to something that is no tree). *)
let rec refusal_is_truthful (rd:readers) (holes:list sig_entry) (declared:list string) (a:invocation)
  : Lemma (ensures (match check_args rd holes declared a with
                    | Error (ArgOutOfSpace addr space got) -> not (validate rd space got) /\ has_key addr a
                    | Error (UnknownArg addr d) -> None? (find_entry addr holes) /\ d == declared
                    | Error (UninvocableArg addr) ->
                      (match find_entry addr holes with
                       | Some h -> (match arg_space h with
                                    | None -> True
                                    | Some sp -> SlotTree? sp)
                       | None -> False)
                    | _ -> True))
          (decreases a)
  = match a with
    | [] -> ()
    | (addr, value) :: rest ->
      (match find_entry addr holes with
       | None -> ()
       | Some h ->
         (match arg_space h with
          | None -> ()
          | Some space -> if validate rd space value then refusal_is_truthful rd holes declared rest else ()))

(* What survives of Phase 177's finding. A signature entry that is REQUIRED and has NO value
   space makes the capability un-invocable by construction: an argument at its address is
   `UninvocableArg`, and no argument there is `RequiredArgsUnbound`. Until Phase 229 that was what
   `Function.signature` made of every slot hole; since then it makes none (`slot_entry_shape`), so
   this now characterises only a hand-built entry. *)
let rec check_args_hits_uninvocable (rd:readers) (holes:list sig_entry) (declared:list string) (a:invocation) (h:sig_entry)
  : Lemma (requires has_key h.s_addr a /\ find_entry h.s_addr holes == Some h /\ None? (arg_space h))
          (ensures Error? (check_args rd holes declared a))
          (decreases a)
  = match a with
    | [] -> ()
    | (addr, value) :: rest ->
      if addr = h.s_addr then ()
      else
        (match find_entry addr holes with
         | None -> ()
         | Some e ->
           (match arg_space e with
            | None -> ()
            | Some space -> if validate rd space value then check_args_hits_uninvocable rd holes declared rest h else ()))

let rec find_entry_memp (k:string) (holes:list sig_entry)
  : Lemma (ensures (match find_entry k holes with
                    | Some h -> memp h holes
                    | None -> True))
  = match holes with
    | [] -> ()
    | h :: t -> if h.s_addr = k then () else find_entry_memp k t

let rec unbound_required_memp (holes:list sig_entry) (a:invocation) (h:sig_entry)
  : Lemma (requires memp h holes /\ h.s_required /\ not (has_key h.s_addr a))
          (ensures Cons? (unbound_required holes a))
  = match holes with
    | [] -> ()
    | x :: t -> if x.s_required && not (has_key x.s_addr a) then () else unbound_required_memp t a h

let spaceless_required_uninvocable (rd:readers) (c:capability) (a:invocation) (h:sig_entry)
  : Lemma (requires find_entry h.s_addr c.c_signature.sg_holes == Some h /\ h.s_required /\ None? (arg_space h))
          (ensures Error? (validate_args rd c a))
  = let holes = c.c_signature.sg_holes in
    if has_key h.s_addr a then check_args_hits_uninvocable rd holes (entry_addrs holes) a h
    else (find_entry_memp h.s_addr holes; unbound_required_memp holes a h)

(* Phase 229. `signature` enters a `SlotHole` REQUIRED and WITH a value space: the tree space of
   its own constraint. *)
let slot_entry_shape (h:hole_decl)
  : Lemma (requires SlotHole? h.h_kind)
          (ensures (entry_of h).s_required /\
                   (entry_of h).s_space == Some (SlotTree (SlotHole?._0 h.h_kind)) /\
                   (entry_of h).s_slot == SlotHole?._0 h.h_kind)
  = ()

(* The tree space admits exactly the wire documents of the constrained kind (any kind when
   unconstrained), and nothing that is no tree. *)
let slot_space_exact (rd:readers) (c:option string) (v:string)
  : Lemma (validate rd (SlotTree c) v <==>
           (match rd.kind_of v with
            | Some k -> None? c \/ c == Some k
            | None -> False))
  = ()

(* Every argument addresses a declared entry that HAS a space, and lies in it. *)
let rec args_in_space (rd:readers) (holes:list sig_entry) (a:invocation) : Tot bool (decreases a) =
  match a with
  | [] -> true
  | (addr, value) :: rest ->
    (match find_entry addr holes with
     | Some h -> (match arg_space h with
                  | Some sp -> validate rd sp value
                  | None -> false)
     | None -> false) && args_in_space rd holes rest

let rec check_args_in_space_ok (rd:readers) (holes:list sig_entry) (declared:list string) (a:invocation)
  : Lemma (requires args_in_space rd holes a)
          (ensures check_args rd holes declared a == Ok ())
          (decreases a)
  = match a with
    | [] -> ()
    | _ :: rest -> check_args_in_space_ok rd holes declared rest

(* COMPLETENESS — the converse of `validate_args_sound`: an argument set every member of which
   lies in its entry's space, binding every required entry, is accepted. *)
let validate_args_complete (rd:readers) (c:capability) (a:invocation)
  : Lemma (requires repeated [] (keys a) == [] /\ args_in_space rd c.c_signature.sg_holes a /\
                    unbound_required c.c_signature.sg_holes a == [])
          (ensures validate_args rd c a == Ok ())
  = check_args_in_space_ok rd c.c_signature.sg_holes (entry_addrs c.c_signature.sg_holes) a

(* THE POSITIVE (Phase 229, restating Phase 177's `slot_hole_uninvocable`). A slot entry — which
   `signature` spaces with `SlotTree` of its constraint — bound to a wire tree of its kind, beside
   arguments that are otherwise in space and bind every required entry, is ACCEPTED: a capability
   over a slotted artifact is invocable. *)
let slot_hole_invocable_in_space (rd:readers) (c:capability) (a:invocation) (h:sig_entry) (v:string)
  : Lemma (requires find_entry h.s_addr c.c_signature.sg_holes == Some h /\
                    h.s_space == Some (SlotTree h.s_slot) /\
                    Some? (rd.kind_of v) /\
                    (None? h.s_slot \/ h.s_slot == rd.kind_of v) /\
                    repeated [] (keys ((h.s_addr, v) :: a)) == [] /\
                    args_in_space rd c.c_signature.sg_holes a /\
                    unbound_required c.c_signature.sg_holes ((h.s_addr, v) :: a) == [])
          (ensures validate_args rd c ((h.s_addr, v) :: a) == Ok ())
  = validate_args_complete rd c ((h.s_addr, v) :: a)

(* ... and refused BY NAME when it is not: a tree of the wrong kind is `ArgOutOfSpace` carrying
   the slot's constraint, and anything that is no tree is `UninvocableArg`. *)
let slot_wrong_kind_refused (rd:readers) (c:capability) (h:sig_entry) (kc:string) (v:string)
  : Lemma (requires find_entry h.s_addr c.c_signature.sg_holes == Some h /\
                    h.s_space == Some (SlotTree (Some kc)) /\
                    Some? (rd.kind_of v) /\ Some?.v (rd.kind_of v) <> kc)
          (ensures validate_args rd c [(h.s_addr, v)] == Error (ArgOutOfSpace h.s_addr (SlotTree (Some kc)) v))
  = ()

let slot_scalar_uninvocable (rd:readers) (c:capability) (h:sig_entry) (sc:option string) (v:string)
  : Lemma (requires find_entry h.s_addr c.c_signature.sg_holes == Some h /\
                    h.s_space == Some (SlotTree sc) /\ None? (rd.kind_of v))
          (ensures validate_args rd c [(h.s_addr, v)] == Error (UninvocableArg h.s_addr))
  = ()

(* ======================================================================================
   8. THE THIRD THEOREM — what is enumerable is exactly what is dispatchable.
   ====================================================================================== *)

(* THE THIRD THEOREM. F#: `CapabilityRegistry.enumerate` and `CapabilityRegistry.tryFind` read the same map. An id is
   in the enumeration exactly when `tryFind` resolves it — no hidden entry, no phantom one. *)
let enumerate_is_registry (r:registry) (id:string)
  : Lemma (mem id (ids (enumerate r)) <==> Some? (try_find_cap id r))
  = find_cap_mem id r.capabilities

(* And `dispatch` agrees: the `NoSuchCapability` refusal is raised exactly off the enumeration. *)
let no_such_iff_unregistered (#v:Type) (rd:readers) (r:registry) (id:string) (a:invocation)
                             (body:capability -> unit -> deferred v)
  : Lemma ((match dispatch rd r id a body with
            | Error (NoSuchCapability _ _) -> True
            | _ -> False) <==> not (mem id (ids (enumerate r))))
  = find_cap_mem id r.capabilities;
    (match find_cap id r.capabilities with
     | None -> ()
     | Some c -> invoke_never_registry_refusal rd c a (body c))

let rec find_cap_id (id:string) (cs:list capability)
  : Lemma (ensures (match find_cap id cs with
                    | Some c -> c.c_id == id
                    | None -> True))
  = match cs with
    | [] -> ()
    | _ :: t -> find_cap_id id t

(* A resolved id dispatches to ITS capability's `invoke` — the entry enumeration showed. *)
let registered_dispatches (#v:Type) (rd:readers) (r:registry) (id:string) (a:invocation)
                          (body:capability -> unit -> deferred v)
  : Lemma (requires mem id (ids (enumerate r)))
          (ensures (match try_find_cap id r with
                    | Some c -> c.c_id == id /\ dispatch rd r id a body == invoke rd c a (body c)
                    | None -> False))
  = find_cap_mem id r.capabilities; find_cap_id id r.capabilities

(* `register` refuses a held id and extends by exactly one entry otherwise. *)
let register_refuses_duplicate (rd:readers) (c:capability) (r:registry)
  : Lemma (requires mem c.c_id (ids (enumerate r)))
          (ensures register rd c r == Error (DuplicateCapability c.c_id))
  = find_cap_mem c.c_id r.capabilities

let register_extends (rd:readers) (c:capability) (r:registry)
  : Lemma (requires not (mem c.c_id (ids (enumerate r))) /\ is_total c.c_signature /\
                    None? (validate_signature rd c.c_signature))
          (ensures register rd c r == Ok { capabilities = c :: r.capabilities } /\
                   (forall (id:string). mem id (ids (c :: r.capabilities)) <==> (id = c.c_id \/ mem id (ids (enumerate r)))))
  = find_cap_mem c.c_id r.capabilities

(* A registry built by `register` holds distinct ids — the invariant `empty` starts and every
   accepted registration keeps, so `find_cap` is a function of the id and the enumeration never
   shows one id twice. *)
let register_keeps_distinct (rd:readers) (c:capability) (r r':registry)
  : Lemma (requires distinct (ids (enumerate r)) /\ register rd c r == Ok r')
          (ensures distinct (ids (enumerate r')))
  = find_cap_mem c.c_id r.capabilities

let empty_distinct () : Lemma (distinct (ids (enumerate empty))) = ()

(* `non_total_addrs` is empty exactly when the signature is total. *)
let rec non_total_addrs_empty (holes:list sig_entry)
  : Lemma (non_total_addrs holes == [] <==> for_all entry_total holes)
  = match holes with
    | [] -> ()
    | _ :: t -> non_total_addrs_empty t

(* Phase 295 — a registry admits only total capabilities. `register` refuses a fresh id whose
   signature is not total as `NonTotalCapability`, naming a non-empty list of its entries; so every
   capability a registry built by `register` holds is total, and `dispatch` never reaches a body over
   an unbounded repeat. *)
let register_refuses_non_total (rd:readers) (c:capability) (r:registry)
  : Lemma (requires not (mem c.c_id (ids (enumerate r))) /\ not (is_total c.c_signature))
          (ensures register rd c r == Error (NonTotalCapability c.c_id (non_total_addrs c.c_signature.sg_holes)) /\
                   Cons? (non_total_addrs c.c_signature.sg_holes))
  = find_cap_mem c.c_id r.capabilities; non_total_addrs_empty c.c_signature.sg_holes

let register_admits_total (rd:readers) (c:capability) (r r':registry)
  : Lemma (requires register rd c r == Ok r')
          (ensures is_total c.c_signature)
  = ()

(* Phase 307 — a registry admits only WELL-FORMED capabilities: a fresh, total capability whose
   signature `Signature.validate` refuses is `IllFormedCapability` with exactly that fault, and
   every capability `register` admits has a signature it accepts — distinct addresses, and spaces
   that admit a value and have finite bounds. *)
let register_refuses_ill_formed (rd:readers) (c:capability) (r:registry)
  : Lemma (requires not (mem c.c_id (ids (enumerate r))) /\ is_total c.c_signature /\
                    Some? (validate_signature rd c.c_signature))
          (ensures register rd c r == Error (IllFormedCapability c.c_id (Some?.v (validate_signature rd c.c_signature))))
  = find_cap_mem c.c_id r.capabilities

let register_admits_well_formed (rd:readers) (c:capability) (r r':registry)
  : Lemma (requires register rd c r == Ok r')
          (ensures None? (validate_signature rd c.c_signature))
  = ()

(* ======================================================================================
   9. THE FOURTH THEOREM — law 1, totality: an unbounded repeat is rejected, never run.
   ====================================================================================== *)

(* The per-hole reading of `isTotal` on the declaration side. *)
let hole_total (h:hole_decl) : Tot bool =
  match h.h_kind with
  | RepeatHole s -> is_count s
  | _ -> true

(* `guardTotal` fires exactly when some hole is an unbounded repeat, and what it fires is
   `NonTotal` at that hole's address. *)
let rec guard_total_shape (holes:list hole_decl)
  : Lemma (ensures (match guard_total holes with
                    | None -> for_all hole_total holes
                    | Some e -> NonTotal? e /\ not (for_all hole_total holes) /\ mem (NonTotal?.addr e) (map addr_of holes)))
  = match holes with
    | [] -> ()
    | h :: rest ->
      (match non_total h with
       | Some _ -> ()
       | None -> guard_total_shape rest)

(* `isTotal` over the signature reads the same hole set through `entry_of`: "repeat" entries
   carry their space, and nothing else is a repeat. *)
let rec is_total_entries (holes:list hole_decl)
  : Lemma (for_all entry_total (map entry_of holes) == for_all hole_total holes)
  = match holes with
    | [] -> ()
    | h :: rest -> is_total_entries rest

(* Action holes are total, so filtering them off the data axis changes nothing about totality. *)
let rec data_total (holes:list hole_decl)
  : Lemma (for_all hole_total (filter is_data holes) == for_all hole_total holes)
  = match holes with
    | [] -> ()
    | _ :: rest -> data_total rest

(* THE FOURTH THEOREM, part one. F#: `Function.isTotal (Function.signature w name node)` answers
   exactly the guard `apply` / `curry` run first; when the guard fires, both refuse with
   `NonTotal` at the FIRST unbounded repeat's address. *)
let totality_law (#node:Type) (rd:readers) (w:witness node) (name:string) (a:args node) (n:node)
  : Lemma (ensures (is_total (signature_of w name n) <==> None? (guard_total (data_holes w n))) /\
                   (match guard_total (data_holes w n) with
                    | Some e -> NonTotal? e /\ apply rd w a n == Error e /\ curry rd w a n == Error e
                    | None -> True))
  = is_total_entries (w.holes n);
    data_total (w.holes n);
    guard_total_shape (data_holes w n)

(* THE FOURTH THEOREM, part two — "never run": with the guard fired, the result is the same under
   ANY `Bind` whatsoever, so the witness was never asked to bind. *)
let rejected_never_bound (#node:Type) (rd:readers) (w:witness node) (strict:bool) (a:args node) (n:node)
                         (b:string -> arg node -> node -> outcome node string)
  : Lemma (requires Some? (guard_total (data_holes w n)))
          (ensures bind_args rd w strict a n == bind_args rd ({ w with bind_hole = b }) strict a n)
  = ()

(* ======================================================================================
   10. THE FIFTH THEOREM — law 2, hygiene: binding is by absolute address, never by name.
   ====================================================================================== *)

(* A renaming of every hole — the witness with its names rewritten and nothing else touched. *)
let rename (f:string -> string) (h:hole_decl) : Tot hole_decl = { h with h_name = f h.h_name }

let renamed (#node:Type) (f:string -> string) (w:witness node) : Tot (witness node) =
  { w with holes = (fun (m:node) -> map (rename f) (w.holes m)) }

let rec filter_rename (f:string -> string) (holes:list hole_decl)
  : Lemma (filter is_data (map (rename f) holes) == map (rename f) (filter is_data holes))
  = match holes with
    | [] -> ()
    | _ :: rest -> filter_rename f rest

let rec guard_rename (f:string -> string) (holes:list hole_decl)
  : Lemma (guard_total (map (rename f) holes) == guard_total holes)
  = match holes with
    | [] -> ()
    | h :: rest ->
      (match non_total h with
       | Some _ -> ()
       | None -> guard_rename f rest)

let rec addrs_rename (f:string -> string) (holes:list hole_decl)
  : Lemma (map addr_of (map (rename f) holes) == map addr_of holes)
  = match holes with
    | [] -> ()
    | _ :: rest -> addrs_rename f rest

(* The walk never reads a name: over renamed holes it makes the same lookups, the same
   validations and the same bindings. *)
let rec walk_rename (#node:Type) (rd:readers) (w:witness node) (f:string -> string) (strict:bool)
                    (a:args node) (cur:node) (unbound:list string) (holes:list hole_decl)
  : Lemma (ensures bind_walk rd (renamed f w) strict a cur unbound (map (rename f) holes) ==
                   bind_walk rd w strict a cur unbound holes)
          (decreases holes)
  = match holes with
    | [] -> ()
    | h :: rest ->
      (match assoc h.h_addr a with
       | None -> walk_rename rd w f strict a cur (h.h_addr :: unbound) rest
       | Some x ->
         (match validate_arg rd w h.h_addr h.h_kind x with
          | Error _ -> ()
          | Ok () ->
            (match w.bind_hole h.h_addr x cur with
             | Ok cur' -> walk_rename rd w f strict a cur' unbound rest
             | Error _ -> ())))

let rec find_rename (f:string -> string) (slot_addr:string) (holes:list hole_decl)
  : Lemma (ensures (match find_hole slot_addr (map (rename f) holes), find_hole slot_addr holes with
                    | Some h', Some h -> h' == rename f h
                    | None, None -> True
                    | _ -> False))
          (decreases holes)
  = match holes with
    | [] -> ()
    | h :: rest -> if h.h_addr = slot_addr then () else find_rename f slot_addr rest

(* Phase 307 — the admission check never reads a name either: `Signature.validate` reads addresses
   and spaces, and the slot test counts kinds. *)
let rec validate_entries_rename (rd:readers) (f:string -> string) (seen:list string) (holes:list hole_decl)
  : Lemma (ensures validate_entries rd seen (map entry_of (map (rename f) holes)) ==
                   validate_entries rd seen (map entry_of holes))
          (decreases holes)
  = match holes with
    | [] -> ()
    | h :: rest -> validate_entries_rename rd f (h.h_addr :: seen) rest

let rec slot_count_rename (f:string -> string) (hs:list hole_decl)
  : Lemma (slot_count (map (rename f) hs) == slot_count hs)
  = match hs with
    | [] -> ()
    | _ :: t -> slot_count_rename f t

let rec sum_slots_rename (#node:Type) (w:witness node) (f:string -> string) (cs:list node)
  : Lemma (sum_slots (renamed f w) cs == sum_slots w cs /\ any_holes (renamed f w) cs == any_holes w cs)
  = match cs with
    | [] -> ()
    | c :: t -> slot_count_rename f (w.holes c); sum_slots_rename w f t

let rec pick_slot_rename (#node:Type) (w:witness node) (f:string -> string) (l:list node)
  : Lemma (ensures try_pick (slot_over_holes (renamed f w)) l == try_pick (slot_over_holes w) l)
          (decreases l)
  = match l with
    | [] -> ()
    | m :: t ->
      slot_count_rename f (w.holes m); sum_slots_rename w f (w.children m);
      pick_slot_rename w f t

let validate_decl_rename (#node:Type) (rd:readers) (w:witness node) (f:string -> string) (n:node)
  : Lemma (validate_decl rd (renamed f w) n == validate_decl rd w n)
  = validate_entries_rename rd f [] (w.holes n); pick_slot_rename w f (w.preorder n)

let compose_rename (#node:Type) (rd:readers) (w:witness node) (f:string -> string) (slot_addr:string) (inner n:node)
  : Lemma (compose rd (renamed f w) slot_addr inner n == compose rd w slot_addr inner n)
  = guard_rename f (w.holes n); guard_rename f (w.holes inner);
    find_rename f slot_addr (w.holes n); addrs_rename f (w.holes n);
    (match w.bind_hole slot_addr (SlotArg inner) n with
     | Ok m -> validate_decl_rename rd w f m
     | Error _ -> ())

(* The strict pre-pass reads the open holes' ADDRESSES, which a renaming keeps. *)
let rec open_slot_rename (#node:Type) (w:witness node) (f:string -> string) (a:args node)
  : Lemma (ensures try_pick (open_slot (renamed f w)) a == try_pick (open_slot w) a)
          (decreases a)
  = match a with
    | [] -> ()
    | (k, x) :: t ->
      (match x with
       | SlotArg sub -> filter_rename f (w.holes sub); addrs_rename f (filter is_data (w.holes sub))
       | ValueArg _ -> ());
      open_slot_rename w f t

(* THE FIFTH THEOREM, part one. F#: hole names are inert to `apply`, `curry` and `compose` —
   every clause keys on `Addr`, and a bare `Name` selects nothing. *)
let hygiene_law (#node:Type) (rd:readers) (w:witness node) (f:string -> string) (a:args node)
                (slot_addr:string) (inner n:node)
  : Lemma (ensures apply rd (renamed f w) a n == apply rd w a n /\
                   curry rd (renamed f w) a n == curry rd w a n /\
                   compose rd (renamed f w) slot_addr inner n == compose rd w slot_addr inner n)
  = filter_rename f (w.holes n);
    guard_rename f (data_holes w n);
    addrs_rename f (data_holes w n);
    open_slot_rename w f a;
    walk_rename rd w f true a n [] (data_holes w n);
    walk_rename rd w f false a n [] (data_holes w n);
    compose_rename rd w f slot_addr inner n

(* THE FIFTH THEOREM, part two. An argument at an address no data hole declares is refused as
   `UnknownHoleAddr`, naming the address and the declared set, before any binding — the result is
   the same under any `Bind`. *)
let undeclared_address_refused (#node:Type) (rd:readers) (w:witness node) (strict:bool) (a:args node) (n:node)
                               (b:string -> arg node -> node -> outcome node string)
  : Lemma (requires None? (guard_total (data_holes w n)) /\
                    Some? (first_unknown (map addr_of (data_holes w n)) (keys a)))
          (ensures (match first_unknown (map addr_of (data_holes w n)) (keys a) with
                    | Some unknown -> bind_args rd w strict a n == Error (UnknownHoleAddr unknown (map addr_of (data_holes w n)))
                    | None -> True) /\
                   bind_args rd w strict a n == bind_args rd ({ w with bind_hole = b }) strict a n)
  = ()

(* THE FIFTH THEOREM, part three — no capture. Holes whose address is not an argument key are
   skipped by the walk ... *)
let rec assoc_none (#node:Type) (k:string) (a:args node)
  : Lemma (requires not (has_key k a)) (ensures None? (assoc k a)) (decreases a)
  = match a with
    | [] -> ()
    | _ :: t -> assoc_none k t

(* No hole's address is an argument key. *)
let rec none_keyed (#node:Type) (a:args node) (holes:list hole_decl) : Tot bool =
  match holes with
  | [] -> true
  | h :: t -> not (has_key h.h_addr a) && none_keyed a t

let rec walk_skips_unkeyed (#node:Type) (rd:readers) (w:witness node) (a:args node)
                           (cur:node) (unbound:list string) (holes:list hole_decl)
  : Lemma (requires none_keyed a holes)
          (ensures bind_walk rd w false a cur unbound holes == Ok cur)
          (decreases holes)
  = match holes with
    | [] -> ()
    | h :: rest -> assoc_none h.h_addr a; walk_skips_unkeyed rd w a cur (h.h_addr :: unbound) rest

(* ... so a single binding at address `k` lands at the hole declared at `k` and nowhere else: the
   walk's result is `Bind k v` (or its refusal) with every other hole untouched. *)
let single_binding (#node:Type) (rd:readers) (w:witness node) (k:string) (v:arg node) (h:hole_decl) (cur:node)
  : Tot (outcome node apply_error) =
  match validate_arg rd w k h.h_kind v with
  | Error e -> Error e
  | Ok () ->
    match w.bind_hole k v cur with
    | Ok cur' -> Ok cur'
    | Error m -> Error (BindFailed k m)

let rec unkeyed_single (#node:Type) (k:string) (v:arg node) (holes:list hole_decl)
  : Lemma (requires not (mem k (map addr_of holes)))
          (ensures none_keyed [(k, v)] holes)
          (decreases holes)
  = match holes with
    | [] -> ()
    | _ :: rest -> unkeyed_single k v rest

let rec walk_single (#node:Type) (rd:readers) (w:witness node) (k:string) (v:arg node)
                    (cur:node) (unbound:list string) (holes:list hole_decl)
  : Lemma (requires distinct (map addr_of holes) /\ mem k (map addr_of holes))
          (ensures (match find_hole k holes with
                    | Some h -> bind_walk rd w false [(k, v)] cur unbound holes == single_binding rd w k v h cur
                    | None -> False))
          (decreases holes)
  = match holes with
    | [] -> ()
    | h :: rest ->
      if h.h_addr = k then
        (unkeyed_single k v rest;
         (match validate_arg rd w k h.h_kind v with
          | Error _ -> ()
          | Ok () ->
            (match w.bind_hole k v cur with
             | Ok cur' -> walk_skips_unkeyed rd w [(k, v)] cur' unbound rest
             | Error _ -> ())))
      else walk_single rd w k v cur (h.h_addr :: unbound) rest

(* F#-side there is no such thing: the declaration with the hole at `k2` removed, for the
   no-capture statement. *)
let rec others (k2:string) (holes:list hole_decl) : Tot (list hole_decl) =
  match holes with
  | [] -> []
  | h :: t -> if h.h_addr <> k2 then h :: others k2 t else others k2 t

let rec mem_filter_addr (x k2:string) (holes:list hole_decl)
  : Lemma (requires not (mem x (map addr_of holes)))
          (ensures not (mem x (map addr_of (others k2 holes))))
          (decreases holes)
  = match holes with
    | [] -> ()
    | _ :: rest -> mem_filter_addr x k2 rest

let rec distinct_filter (k2:string) (holes:list hole_decl)
  : Lemma (requires distinct (map addr_of holes))
          (ensures distinct (map addr_of (others k2 holes)))
          (decreases holes)
  = match holes with
    | [] -> ()
    | h :: rest -> mem_filter_addr h.h_addr k2 rest; distinct_filter k2 rest

let rec mem_filter (k k2:string) (holes:list hole_decl)
  : Lemma (requires k <> k2 /\ mem k (map addr_of holes))
          (ensures mem k (map addr_of (others k2 holes)))
          (decreases holes)
  = match holes with
    | [] -> ()
    | h :: rest -> if h.h_addr = k then () else mem_filter k k2 rest

let rec guard_filter (k2:string) (holes:list hole_decl)
  : Lemma (requires None? (guard_total holes))
          (ensures None? (guard_total (others k2 holes)))
          (decreases holes)
  = match holes with
    | [] -> ()
    | h :: rest ->
      (match non_total h with
       | Some _ -> ()
       | None -> guard_filter k2 rest)

let rec find_filter (k k2:string) (holes:list hole_decl)
  : Lemma (requires k <> k2)
          (ensures find_hole k (others k2 holes) == find_hole k holes)
          (decreases holes)
  = match holes with
    | [] -> ()
    | h :: rest -> if h.h_addr = k then () else find_filter k k2 rest

let single_binding_witness (#node:Type) (rd:readers) (w w':witness node) (k:string) (v:arg node) (h:hole_decl) (n:node)
  : Lemma (requires w'.bind_hole == w.bind_hole /\ w'.kind_tag == w.kind_tag)
          (ensures single_binding rd w k v h n == single_binding rd w' k v h n)
  = ()

(* The two same-named holes: `w` declares both, `w'` declares only the one at `k`. Binding at
   `k` through `curry` is the same on both — the other hole's presence, name and all, is inert.
   (Stated over `curry`, whose non-strict walk is the one a partial binding takes; `apply` would
   refuse both for the second hole's absence, which is required-ness, not capture.) *)
let same_name_no_capture (#node:Type) (rd:readers) (w w':witness node) (k k2:string) (v:arg node) (n:node)
  : Lemma (requires k <> k2 /\
                    distinct (map addr_of (data_holes w n)) /\
                    mem k (map addr_of (data_holes w n)) /\
                    None? (guard_total (data_holes w n)) /\
                    data_holes w' n == others k2 (data_holes w n) /\
                    w'.bind_hole == w.bind_hole /\ w'.kind_tag == w.kind_tag)
          (ensures curry rd w [(k, v)] n == curry rd w' [(k, v)] n /\
                   (match find_hole k (data_holes w n) with
                    | Some h -> curry rd w [(k, v)] n == single_binding rd w k v h n
                    | None -> False))
  = let hs = data_holes w n in
    let hs' = data_holes w' n in
    distinct_filter k2 hs;
    mem_filter k k2 hs;
    guard_filter k2 hs;
    find_filter k k2 hs;
    walk_single rd w k v n [] hs;
    walk_single rd w' k v n [] hs';
    (match find_hole k hs with
     | Some h -> single_binding_witness rd w w' k v h n
     | None -> ())

(* The defensive `ActionHole` arm of `validateArg` is unreachable from `bindArgs`: the walk runs
   over the data axis, and every hole on it is a data hole. *)
let rec data_holes_all_data (holes:list hole_decl)
  : Lemma (for_all is_data (filter is_data holes))
  = match holes with
    | [] -> ()
    | _ :: rest -> data_holes_all_data rest

(* `signatureExcluding` removes exactly the bound addresses, keeping every other entry and the
   effect — what `curry` narrows a signature to. *)
let rec filter_mem_entry (bound:list string) (e:sig_entry) (holes:list sig_entry)
  : Lemma (memp e (excluding bound holes) <==> (memp e holes /\ not (mem e.s_addr bound)))
  = match holes with
    | [] -> ()
    | _ :: rest -> filter_mem_entry bound e rest

let signature_excluding_exact (bound:list string) (sg:signature) (e:sig_entry)
  : Lemma ((signature_excluding bound sg).sg_effect == sg.sg_effect /\
           (signature_excluding bound sg).sg_name == sg.sg_name /\
           (memp e (signature_excluding bound sg).sg_holes <==> (memp e sg.sg_holes /\ not (mem e.s_addr bound))))
  = filter_mem_entry bound e sg.sg_holes

(* ======================================================================================
   11. THE SIXTH THEOREM — law 3, the effect signature joins componentwise, never lower.
   ====================================================================================== *)

(* THE SIXTH THEOREM. F#: `Function.composedEffect` is `Effect.join`, and the join is the least
   class covering both parts: any class a composition is assigned that is below either part
   fails `covers`. With `join_comm` / `join_assoc` / `join_idem` / `join_pure` above, the effect
   classes under `join` are a bounded join-semilattice with `pureDeterministic` at the bottom. *)
let effect_law (#node:Type) (w:witness node) (inner outer:node) (c:effect_class)
  : Lemma (composed_effect w inner outer == join (w.eff outer) (w.eff inner) /\
           covers (composed_effect w inner outer) (w.eff outer) /\
           covers (composed_effect w inner outer) (w.eff inner) /\
           (covers c (composed_effect w inner outer) <==> (covers c (w.eff outer) /\ covers c (w.eff inner))))
  = ()

(* A class covers a join-fold exactly when it covers the seed and every element. *)
let rec all_covered (c:effect_class) (l:list effect_class) : Tot bool =
  match l with
  | [] -> true
  | x :: t -> covers c x && all_covered c t

let rec fold_covers (c:effect_class) (acc:effect_class) (l:list effect_class)
  : Lemma (ensures covers c (fold_left join acc l) <==> (covers c acc /\ all_covered c l))
          (decreases l)
  = match l with
    | [] -> ()
    | x :: t -> join_least acc x c; fold_covers c (join acc x) t

let rec for_all_map (#node:Type) (w:witness node) (c:effect_class) (l:list node)
  : Lemma (all_covered c (map w.eff l) <==> (forall (m:node). memp m l ==> covers c (w.eff m)))
  = match l with
    | [] -> ()
    | _ :: t -> for_all_map w c t

(* The observed effect is the least class covering every node on the walk. *)
let observed_least (#node:Type) (w:witness node) (n:node) (c:effect_class)
  : Lemma (covers c (observed_effect w n) <==> (forall (m:node). memp m (w.preorder n) ==> covers c (w.eff m)))
  = fold_covers c pure_deterministic (map w.eff (w.preorder n));
    for_all_map w c (w.preorder n)

(* THE SIXTH THEOREM, at the audit. F#: `Function.auditEffect`. The observed effect covers every
   node's declared class; an `Ok` audit says the root's declaration covers every descendant's;
   an `Error` audit names a declared root and an observed class it does not cover, and exhibits a
   node under the root the declaration is narrower than. *)
let audit_effect_join (#node:Type) (w:witness node) (n:node)
  : Lemma ((forall (m:node). memp m (w.preorder n) ==> covers (observed_effect w n) (w.eff m)) /\
           (audit_effect w n == Ok () ==> (forall (m:node). memp m (w.preorder n) ==> covers (w.eff n) (w.eff m))) /\
           (Error? (audit_effect w n) ==>
              (audit_effect w n == Error (w.eff n, observed_effect w n) /\
               (exists (m:node). memp m (w.preorder n) /\ not (covers (w.eff n) (w.eff m))))))
  = covers_refl (observed_effect w n);
    observed_least w n (observed_effect w n);
    observed_least w n (w.eff n)

(* ======================================================================================
   12. THE SEVENTH THEOREM (Phase 225) — `Capability.invocationKey`'s pre-image is INJECTIVE:
       distinct argument sets have distinct pre-images. The capture key was outside this model
       until Phase 225; it enters it with the canonicaliser both seams now share.
   ====================================================================================== *)

(* The host functions the capture key is written through. F#: `Hash.fnv1a`, the ordinal order
   `List.sortBy fst` compares addresses by, and `Hash.canonicalField` — one field of the
   pre-image, escaped and terminated — the same canonicaliser `Query.invocationKey` uses, which
   is the point of Phase 225: one encoding, so the two seams cannot drift apart again. *)
noeq type key_renderers = {
  k_hash:      string -> string;
  k_addr_le:   string -> string -> bool;
  k_field:     string -> string;
  (* F#: `Space.canonical` (Phase 295) — the one spelling of a value in a space, `None` outside it. *)
  k_canonical: value_space -> string -> option string
}

(* F#: `List.sortBy fst`'s insertion step — a STABLE sort. *)
let rec insert_binding (kr:key_renderers) (x:(string & string)) (l:invocation)
  : Tot invocation =
  match l with
  | [] -> [x]
  | y :: t ->
    let (xa, _) = x in
    let (ya, _) = y in
    if kr.k_addr_le xa ya then x :: l else y :: insert_binding kr x t

(* F#: `args |> List.sortBy fst`. *)
let rec sort_bindings (kr:key_renderers) (l:invocation) : Tot invocation =
  match l with
  | [] -> []
  | x :: t -> insert_binding kr x (sort_bindings kr t)

(* F#: `List.collect (fun (a, v) -> [ a; v ])` — two fields per binding. *)
let rec binding_fields (l:invocation) : Tot (list string) =
  match l with
  | [] -> []
  | (a, v) :: t -> a :: v :: binding_fields t

(* F#: `Hash.canonicalFields` — `List.map canonicalField |> String.concat ""`. *)
let rec key_fields (kr:key_renderers) (l:list string) : Tot string =
  match l with
  | [] -> ""
  | x :: t -> kr.k_field x ^ key_fields kr t

(* The canonical pre-image of an (address-sorted) argument list. *)
let key_canonical (kr:key_renderers) (l:invocation) : Tot string =
  key_fields kr (binding_fields l)

(* F#: `Capability.invocationKey`'s `spelled` (Phase 295) — an argument as it is keyed: its
   canonical spelling in its hole's space where it has one, its own spelling otherwise. *)
let spell (kr:key_renderers) (c:capability) (b:(string & string)) : Tot (string & string) =
  let (a, v) = b in
  match find_entry a c.c_signature.sg_holes with
  | Some e ->
    (match e.s_space with
     | Some sp -> (match kr.k_canonical sp v with Some v' -> (a, v') | None -> (a, v))
     | None -> (a, v))
  | None -> (a, v)

(* The argument list as it is keyed. *)
let keyed (kr:key_renderers) (c:capability) (a:invocation) : Tot invocation = map (spell kr c) a

(* F#: `Capability.invocationKey` — over the KEYED list (Phase 295), so one value has one key; the
   seventh theorem below is injectivity of the pre-image over the list it is given, which is the
   keyed list here. *)
let invocation_key (kr:key_renderers) (c:capability) (a:invocation) : Tot string =
  c.c_id ^ "#" ^ kr.k_hash (key_canonical kr (sort_bindings kr (keyed kr c a)))

(* HOW A STRING IS READ — the reading `Chain.fst` and `Query.fst` take, restated because this
   module opens nothing: concatenation is symbol-list append, and equal symbols are one string.
   A HYPOTHESIS in the `requires` of the lemmas that spend it, never an `assume`. *)
[@@ noextract_to "FSharp"]
let symbols_faithful (#sym:eqtype) (reveal:string -> list sym) : prop =
  (forall (s t:string). reveal (s ^ t) == app (reveal s) (reveal t)) /\
  (forall (s t:string). reveal s == reveal t ==> s == t)

(* F#: `Hash.canonicalField`'s escaper at the symbol level — `e` (U+0010) before every `e` and
   every terminator `t` (U+0001, `Hash.foldSep`) the field carries. *)
[@@ noextract_to "FSharp"]
let rec esc (#sym:eqtype) (e t:sym) (l:list sym) : Tot (list sym) =
  match l with
  | [] -> []
  | x :: r -> if x = e || x = t then e :: x :: esc e t r else x :: esc e t r

[@@ noextract_to "FSharp"]
let rec enc (#sym:eqtype) (e t:sym) (l:list (list sym)) : Tot (list sym) =
  match l with
  | [] -> []
  | x :: r -> app (esc e t x) (t :: enc e t r)

(* THE ESCAPER PREMISE — `Hash.canonicalField` is `esc` then one terminator, read as symbols. *)
[@@ noextract_to "FSharp"]
let field_faithful (#sym:eqtype) (reveal:string -> list sym) (e t:sym) (field:string -> string)
  : prop =
  forall (s:string). reveal (field s) == app (esc e t (reveal s)) [t]

(* Every premise the theorem spends. No numeral layout is among them: a capability argument is
   already a string. *)
[@@ noextract_to "FSharp"]
let key_premises (#sym:eqtype) (reveal:string -> list sym) (e t:sym) (kr:key_renderers) : prop =
  symbols_faithful reveal /\ reveal "" == [] /\ ~(e == t) /\ field_faithful reveal e t kr.k_field

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

(* The split point is forced: the first UNESCAPED terminator ends each field. *)
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

let rec reveal_key_fields (#sym:eqtype) (reveal:string -> list sym) (e t:sym)
  (kr:key_renderers) (l:list string)
  : Lemma (requires symbols_faithful reveal /\ reveal "" == [] /\
                    field_faithful reveal e t kr.k_field)
          (ensures reveal (key_fields kr l) == enc e t (symbols reveal l)) =
  match l with
  | [] -> ()
  | x :: r ->
    reveal_key_fields reveal e t kr r;
    app_assoc (esc e t (reveal x)) [t] (reveal (key_fields kr r))

(* `Hash.canonicalFields` is injective on field lists. Proved, not assumed. *)
let key_fields_injective (#sym:eqtype) (reveal:string -> list sym) (e t:sym)
  (kr:key_renderers) (l1 l2:list string)
  : Lemma (requires key_premises reveal e t kr /\ key_fields kr l1 == key_fields kr l2)
          (ensures l1 == l2) =
  reveal_key_fields reveal e t kr l1;
  reveal_key_fields reveal e t kr l2;
  enc_injective e t (symbols reveal l1) (symbols reveal l2);
  symbols_injective reveal l1 l2

(* Two fields per binding, so the flattening is injective. *)
let rec binding_fields_injective (a1 a2:invocation)
  : Lemma (requires binding_fields a1 == binding_fields a2) (ensures a1 == a2) =
  match a1, a2 with
  | [], [] -> ()
  | [], _ :: _ -> ()
  | _ :: _, [] -> ()
  | _ :: t1, _ :: t2 -> binding_fields_injective t1 t2

(* Decidable membership of a binding. *)
let rec mem_binding (x:(string & string)) (l:invocation) : Tot bool =
  match l with
  | [] -> false
  | y :: t -> x = y || mem_binding x t

let rec insert_binding_mem (kr:key_renderers) (x:(string & string)) (l:invocation)
  (y:(string & string))
  : Lemma (mem_binding y (insert_binding kr x l) = (y = x || mem_binding y l)) =
  match l with
  | [] -> ()
  | _ :: t -> insert_binding_mem kr x t y

let rec sort_bindings_mem (kr:key_renderers) (l:invocation) (y:(string & string))
  : Lemma (mem_binding y (sort_bindings kr l) = mem_binding y l) =
  match l with
  | [] -> ()
  | x :: t -> sort_bindings_mem kr t y; insert_binding_mem kr x (sort_bindings kr t) y

(* THE SEVENTH THEOREM. F#: `Capability.invocationKey`'s pre-image. Two argument lists whose
   address-sorted canonical strings agree ARE the same sorted list, and so hold exactly the same
   bindings: distinct argument sets have distinct pre-images, whatever their values contain —
   `=`, `U+0001` and `U+0010` included, which is where the pre-225 join (`addr=value` pairs on
   `U+0001`) collided. No premise about the comparator; whether distinct pre-images HASH apart is
   a claim about FNV-1a and is not made. *)
let invocation_key_injective (#sym:eqtype) (reveal:string -> list sym) (e t:sym)
  (kr:key_renderers) (a a':invocation)
  : Lemma (requires key_premises reveal e t kr /\
                    key_canonical kr (sort_bindings kr a) == key_canonical kr (sort_bindings kr a'))
          (ensures sort_bindings kr a == sort_bindings kr a' /\
                   (forall (x:(string & string)). mem_binding x a = mem_binding x a')) =
  key_fields_injective reveal e t kr (binding_fields (sort_bindings kr a))
    (binding_fields (sort_bindings kr a'));
  binding_fields_injective (sort_bindings kr a) (sort_bindings kr a');
  let aux (x:(string & string)) : Lemma (mem_binding x a = mem_binding x a') =
    sort_bindings_mem kr a x; sort_bindings_mem kr a' x
  in
  FStar.Classical.forall_intro aux

(* ======================================================================================
   13. THE ADMISSION GATE'S FACTS (Phase 307) — the hypotheses three proved rows rested on are
       facts the seam enforces: an accepted argument list has distinct addresses, the capture key
       is a function of the bindings alone, and over distinct hole addresses an accepted
       invocation reaches `apply` without the three refusals a well-formed declaration rules out.
   ====================================================================================== *)

(* `repeated` finds nothing exactly over a list with distinct members none of which was seen. *)
let rec repeated_none (seen:list string) (ks:list string)
  : Lemma (requires repeated seen ks == [])
          (ensures distinct ks /\ (forall (k:string). mem k ks ==> not (mem k seen)))
          (decreases ks)
  = match ks with
    | [] -> ()
    | k :: t -> repeated_none (k :: seen) t

(* F#: `validateArgs` refuses a repeated address (`DuplicateArg`), so an ACCEPTED argument list has
   distinct addresses — the hypothesis `invocation_key_deterministic` below needs, made a fact of
   every list the seam accepts. *)
let validate_args_distinct (rd:readers) (c:capability) (a:invocation)
  : Lemma (requires validate_args rd c a == Ok ())
          (ensures distinct (keys a))
  = match repeated [] (keys a) with
    | [] -> repeated_none [] (keys a)
    | _ -> ()

(* The comparator premise, as `Query.fst` states it: `k_addr_le` is a total order — true of the
   ordinal order production sorts by. *)
let total_order (le:string -> string -> bool) : Tot prop =
  (forall (x y:string). le x y \/ le y x) /\
  (forall (x y:string). (le x y /\ le y x) ==> x == y) /\
  (forall (x y z:string). (le x y /\ le y z) ==> le x z)

(* F#: the result of `List.sortBy fst` is ordered by address. *)
let rec sorted_b (le:string -> string -> bool) (l:invocation) : Tot bool =
  match l with
  | [] -> true
  | x :: tl ->
    (match tl with
     | [] -> true
     | y :: _ -> le (fst x) (fst y) && sorted_b le tl)

let rec insert_keys_b (kr:key_renderers) (x:(string & string)) (l:invocation) (n:string)
  : Lemma (mem n (keys (insert_binding kr x l)) = (n = fst x || mem n (keys l)))
  = match l with
    | [] -> ()
    | _ :: t -> insert_keys_b kr x t n

let rec insert_distinct_b (kr:key_renderers) (x:(string & string)) (l:invocation)
  : Lemma (requires distinct (keys l) /\ not (mem (fst x) (keys l)))
          (ensures distinct (keys (insert_binding kr x l)))
  = match l with
    | [] -> ()
    | y :: t ->
      if kr.k_addr_le (fst x) (fst y) then ()
      else (insert_distinct_b kr x t; insert_keys_b kr x t (fst y))

let rec sort_keys_b (kr:key_renderers) (l:invocation) (n:string)
  : Lemma (mem n (keys (sort_bindings kr l)) = mem n (keys l))
  = match l with
    | [] -> ()
    | x :: t -> sort_keys_b kr t n; insert_keys_b kr x (sort_bindings kr t) n

let rec sort_distinct_b (kr:key_renderers) (l:invocation)
  : Lemma (requires distinct (keys l))
          (ensures distinct (keys (sort_bindings kr l)))
  = match l with
    | [] -> ()
    | x :: t ->
      sort_distinct_b kr t;
      sort_keys_b kr t (fst x);
      insert_distinct_b kr x (sort_bindings kr t)

let rec insert_sorted_b (kr:key_renderers) (x:(string & string)) (l:invocation)
  : Lemma (requires total_order kr.k_addr_le /\ sorted_b kr.k_addr_le l)
          (ensures sorted_b kr.k_addr_le (insert_binding kr x l))
  = match l with
    | [] -> ()
    | y :: t ->
      if kr.k_addr_le (fst x) (fst y) then ()
      else insert_sorted_b kr x t

let rec sort_sorted_b (kr:key_renderers) (l:invocation)
  : Lemma (requires total_order kr.k_addr_le)
          (ensures sorted_b kr.k_addr_le (sort_bindings kr l))
  = match l with
    | [] -> ()
    | x :: t -> sort_sorted_b kr t; insert_sorted_b kr x (sort_bindings kr t)

let rec sorted_head_le_b (le:string -> string -> bool) (x:(string & string)) (t:invocation) (y:(string & string))
  : Lemma (requires total_order le /\ sorted_b le (x :: t) /\ mem_binding y t)
          (ensures le (fst x) (fst y))
          (decreases t)
  = match t with
    | [] -> ()
    | h :: t' -> if y = h then () else sorted_head_le_b le h t' y

let rec mem_binding_key (x:(string & string)) (l:invocation)
  : Lemma (requires mem_binding x l)
          (ensures mem (fst x) (keys l))
  = match l with
    | [] -> ()
    | y :: t -> if x = y then () else mem_binding_key x t

(* Two argument lists bind the same addresses to the same values. *)
let same_bindings_b (a a':invocation) : Tot prop =
  forall (x:(string & string)). mem_binding x a = mem_binding x a'

(* An address-sorted list with distinct addresses is determined by its bindings. *)
let rec sorted_unique_b (le:string -> string -> bool) (s1 s2:invocation)
  : Lemma (requires total_order le /\ sorted_b le s1 /\ sorted_b le s2 /\
                    distinct (keys s1) /\ distinct (keys s2) /\ same_bindings_b s1 s2)
          (ensures s1 == s2)
          (decreases s1)
  = match s1, s2 with
    | [], [] -> ()
    | [], h :: _ -> assert (mem_binding h s1 = mem_binding h s2)
    | h :: _, [] -> assert (mem_binding h s1 = mem_binding h s2)
    | h1 :: t1, h2 :: t2 ->
      assert (mem_binding h1 s1 = mem_binding h1 s2);
      assert (mem_binding h2 s1 = mem_binding h2 s2);
      if h1 = h2 then begin
        let aux (x:(string & string)) : Lemma (mem_binding x t1 = mem_binding x t2) =
          assert (mem_binding x s1 = mem_binding x s2);
          if x = h1 then begin
            (if mem_binding x t1 then mem_binding_key x t1 else ());
            (if mem_binding x t2 then mem_binding_key x t2 else ())
          end else ()
        in
        FStar.Classical.forall_intro aux;
        sorted_unique_b le t1 t2
      end else begin
        sorted_head_le_b le h2 t2 h1;
        sorted_head_le_b le h1 t1 h2;
        mem_binding_key h1 t2
      end

(* `spell` keeps the address, so it commutes with the address sort. *)
let spell_addr (kr:key_renderers) (c:capability) (b:(string & string))
  : Lemma (fst (spell kr c b) == fst b)
  = let (a, v) = b in
    match find_entry a c.c_signature.sg_holes with
    | Some e ->
      (match e.s_space with
       | Some sp -> (match kr.k_canonical sp v with Some _ -> () | None -> ())
       | None -> ())
    | None -> ()

let rec insert_map_spell (kr:key_renderers) (c:capability) (x:(string & string)) (l:invocation)
  : Lemma (insert_binding kr (spell kr c x) (map (spell kr c) l) == map (spell kr c) (insert_binding kr x l))
  = match l with
    | [] -> ()
    | y :: t -> spell_addr kr c x; spell_addr kr c y; insert_map_spell kr c x t

let rec sort_map_spell (kr:key_renderers) (c:capability) (l:invocation)
  : Lemma (sort_bindings kr (map (spell kr c) l) == map (spell kr c) (sort_bindings kr l))
  = match l with
    | [] -> ()
    | x :: t -> sort_map_spell kr c t; insert_map_spell kr c x (sort_bindings kr t)

(* THE EIGHTH THEOREM (Phase 307; `Query.fst`'s fourth, ported). F#: `Capability.invocationKey`.
   Two argument lists binding the same distinct addresses to the same values, in any order, key
   identically — the key is a function of the capability and the BINDINGS, never of the order the
   caller wrote them in. *)
let invocation_key_deterministic (kr:key_renderers) (c:capability) (a a':invocation)
  : Lemma (requires total_order kr.k_addr_le /\ distinct (keys a) /\ distinct (keys a') /\ same_bindings_b a a')
          (ensures invocation_key kr c a == invocation_key kr c a')
  = let aux (x:(string & string)) : Lemma (mem_binding x (sort_bindings kr a) = mem_binding x (sort_bindings kr a')) =
      sort_bindings_mem kr a x; sort_bindings_mem kr a' x;
      assert (mem_binding x a = mem_binding x a')
    in
    FStar.Classical.forall_intro aux;
    sort_sorted_b kr a; sort_sorted_b kr a';
    sort_distinct_b kr a; sort_distinct_b kr a';
    sorted_unique_b kr.k_addr_le (sort_bindings kr a) (sort_bindings kr a');
    sort_map_spell kr c a; sort_map_spell kr c a'

(* ... and the hypothesis is the seam's: two ACCEPTED invocations of one capability with the same
   bindings key identically. Before Phase 307 `validateArgs` accepted a repeated address, and the
   key of `[a = x; a = y]` differed from the key of `[a = y; a = x]`. *)
let accepted_invocation_key_deterministic (rd:readers) (kr:key_renderers) (c:capability) (a a':invocation)
  : Lemma (requires total_order kr.k_addr_le /\ validate_args rd c a == Ok () /\
                    validate_args rd c a' == Ok () /\ same_bindings_b a a')
          (ensures invocation_key kr c a == invocation_key kr c a')
  = validate_args_distinct rd c a; validate_args_distinct rd c a';
    invocation_key_deterministic kr c a a'

(* ---- wf_holes: distinct addresses, and an accepted invocation reaches `apply` cleanly ---- *)

(* An invocation as `apply`'s arguments: a value at a slot entry is the tree `tree` reads it as
   (F#: what a host does between the two seams — decode a tree argument into its node), every other
   value stays a scalar. *)
let lift (#node:Type) (tree:string -> node) (holes:list sig_entry) (b:(string & string)) : Tot (string & arg node) =
  let (k, v) = b in
  match find_entry k holes with
  | Some e -> if e.s_kind = "slot" then (k, SlotArg (tree v)) else (k, ValueArg v)
  | None -> (k, ValueArg v)

let lifted (#node:Type) (tree:string -> node) (holes:list sig_entry) (a:invocation) : Tot (args node) =
  map (lift tree holes) a

let rec keys_lifted (#node:Type) (tree:string -> node) (holes:list sig_entry) (a:invocation)
  : Lemma (keys (lifted tree holes a) == keys a)
  = match a with
    | [] -> ()
    | _ :: t -> keys_lifted tree holes t

let rec assoc_lifted (#node:Type) (tree:string -> node) (holes:list sig_entry) (k:string) (a:invocation)
  : Lemma (assoc k (lifted tree holes a) ==
           (match assoc k a with
            | Some v -> Some (snd (lift tree holes (k, v)))
            | None -> None))
  = match a with
    | [] -> ()
    | (k', _) :: t -> if k = k' then () else assoc_lifted tree holes k t

let rec has_key_assoc (k:string) (a:invocation)
  : Lemma (has_key k a ==> Some? (assoc k a))
  = match a with
    | [] -> ()
    | _ :: t -> has_key_assoc k t

(* `find_entry` over a derived signature is `find_hole` over the holes, through `entry_of`. *)
let rec find_entry_map (k:string) (hs:list hole_decl)
  : Lemma (find_entry k (map entry_of hs) ==
           (match find_hole k hs with
            | Some h -> Some (entry_of h)
            | None -> None))
  = match hs with
    | [] -> ()
    | _ :: t -> find_entry_map k t

let rec memp_addr (h:hole_decl) (hs:list hole_decl)
  : Lemma (memp h hs ==> mem h.h_addr (map addr_of hs))
  = match hs with
    | [] -> ()
    | _ :: t -> memp_addr h t

(* Over distinct addresses a member is the one hole its address finds. *)
let rec distinct_find (h:hole_decl) (hs:list hole_decl)
  : Lemma ((memp h hs /\ distinct (map addr_of hs)) ==> find_hole h.h_addr hs == Some h)
  = match hs with
    | [] -> ()
    | _ :: t -> memp_addr h t; distinct_find h t

let rec memp_filter_data (h:hole_decl) (hs:list hole_decl)
  : Lemma (memp h (filter is_data hs) ==> memp h hs /\ is_data h)
  = match hs with
    | [] -> ()
    | _ :: t -> memp_filter_data h t

let rec mem_filter_data_addr (k:string) (hs:list hole_decl)
  : Lemma (mem k (map addr_of (filter is_data hs)) ==> mem k (map addr_of hs))
  = match hs with
    | [] -> ()
    | _ :: t -> mem_filter_data_addr k t

(* Distinct addresses survive dropping the action holes — so `toJsonSchema`'s property keys, which
   are the data holes' addresses, are distinct. *)
let rec distinct_data (hs:list hole_decl)
  : Lemma (distinct (map addr_of hs) ==> distinct (map addr_of (filter is_data hs)))
  = match hs with
    | [] -> ()
    | h :: t -> mem_filter_data_addr h.h_addr t; distinct_data t

let rec for_all_memp (p:hole_decl -> bool) (h:hole_decl) (hs:list hole_decl)
  : Lemma ((for_all p hs /\ memp h hs) ==> p h)
  = match hs with
    | [] -> ()
    | _ :: t -> for_all_memp p h t

(* A total data hole is a required entry. *)
let data_required (h:hole_decl)
  : Lemma ((hole_total h /\ is_data h) ==> (entry_of h).s_required)
  = ()

let rec required_bound (hs:list hole_decl) (a:invocation) (h:hole_decl)
  : Lemma ((unbound_required (map entry_of hs) a == [] /\ memp h hs /\ (entry_of h).s_required) ==>
           has_key h.h_addr a)
  = match hs with
    | [] -> ()
    | _ :: t -> required_bound t a h

(* Every argument addresses an entry that takes a value. *)
let rec all_spaced (holes:list sig_entry) (ks:list string) : Tot bool =
  match ks with
  | [] -> true
  | k :: t ->
    (match find_entry k holes with
     | Some e -> Some? (arg_space e)
     | None -> false) && all_spaced holes t

let rec check_args_ok_spaced (rd:readers) (holes:list sig_entry) (declared:list string) (a:invocation)
  : Lemma (requires check_args rd holes declared a == Ok ())
          (ensures all_spaced holes (keys a))
          (decreases a)
  = match a with
    | [] -> ()
    | (addr, value) :: rest ->
      (match find_entry addr holes with
       | None -> ()
       | Some h ->
         (match arg_space h with
          | None -> ()
          | Some space -> if validate rd space value then check_args_ok_spaced rd holes declared rest else ()))

(* An address whose entry takes a value is a DATA hole's — an action entry takes none. *)
let rec spaced_data (k:string) (hs:list hole_decl)
  : Lemma ((match find_entry k (map entry_of hs) with
            | Some e -> Some? (arg_space e)
            | None -> false) ==> mem k (map addr_of (filter is_data hs)))
  = match hs with
    | [] -> ()
    | _ :: t -> spaced_data k t

let rec first_unknown_spaced (hs:list hole_decl) (ks:list string)
  : Lemma (all_spaced (map entry_of hs) ks ==> first_unknown (map addr_of (filter is_data hs)) ks == None)
  = match ks with
    | [] -> ()
    | k :: t -> spaced_data k hs; first_unknown_spaced hs t

(* The walk, over holes each of which is the one hole its address finds, is data, and is bound:
   it never refuses `NotASlot` (the lifted argument has the hole's own shape), never
   `RequiredHolesUnbound` (nothing is left unbound) and never `UnknownHoleAddr` (it makes none). *)
let rec walk_wf (#node:Type) (rd:readers) (w:witness node) (tree:string -> node) (all:list hole_decl)
                (a:invocation) (strict:bool) (cur:node) (holes:list hole_decl)
  : Lemma (requires forall (h:hole_decl). memp h holes ==>
                      (find_hole h.h_addr all == Some h /\ is_data h /\ has_key h.h_addr a))
          (ensures (match bind_walk rd w strict (lifted tree (map entry_of all) a) cur [] holes with
                    | Error (NotASlot _) -> False
                    | Error (RequiredHolesUnbound _) -> False
                    | Error (UnknownHoleAddr _ _) -> False
                    | _ -> True))
          (decreases holes)
  = match holes with
    | [] -> ()
    | h :: rest ->
      has_key_assoc h.h_addr a;
      assoc_lifted tree (map entry_of all) h.h_addr a;
      find_entry_map h.h_addr all;
      let x = Some?.v (assoc h.h_addr (lifted tree (map entry_of all) a)) in
      (match validate_arg rd w h.h_addr h.h_kind x with
       | Error _ -> ()
       | Ok () ->
         (match w.bind_hole h.h_addr x cur with
          | Ok cur' -> walk_wf rd w tree all a strict cur' rest
          | Error _ -> ()))

(* The strict pre-pass refuses only as `SlotArgOpen`. *)
let rec open_slot_shape (#node:Type) (w:witness node) (l:args node)
  : Lemma (match try_pick (open_slot w) l with
           | Some e -> SlotArgOpen? e
           | None -> True)
  = match l with
    | [] -> ()
    | (_, x) :: t ->
      (match x with
       | SlotArg sub -> (match data_holes w sub with [] -> open_slot_shape w t | _ -> ())
       | ValueArg _ -> open_slot_shape w t)

(* THE NINTH THEOREM (Phase 307), `wf_holes`. Over an artifact whose holes have DISTINCT addresses
   — what `Signature.validate` enforces at every admission (`DuplicateHoleAddr`) — the standard
   JSON Schema's property keys (the data holes' addresses) are distinct, and an invocation the
   capability seam ACCEPTS, lifted to `apply`'s arguments, never meets `NotASlot`,
   `RequiredHolesUnbound` or `UnknownHoleAddr` there: the two seams agree on which arguments an
   artifact takes. What `apply` may still answer is about the VALUES and the witness —
   `ValueOutOfSpace`, `SlotKindMismatch`, `NonTotal`, `SlotArgOpen`, `BindFailed` — and that a
   hole beneath a slot (`HoleUnderSlot`) surfaces as the witness's `BindFailed` is the witness
   contract's, which this model leaves abstract. *)
let wf_holes (#node:Type) (rd:readers) (w:witness node) (id name:string) (p:placement)
             (tree:string -> node) (a:invocation) (n:node)
  : Lemma (requires distinct (map addr_of (w.holes n)) /\
                    validate_args rd (create id (signature_of w name n) p) a == Ok ())
          (ensures distinct (map addr_of (data_holes w n)) /\
                   (match apply rd w (lifted tree (signature_of w name n).sg_holes a) n with
                    | Error (NotASlot _) -> False
                    | Error (RequiredHolesUnbound _) -> False
                    | Error (UnknownHoleAddr _ _) -> False
                    | _ -> True))
  = let all = w.holes n in
    let entries = map entry_of all in
    let dh = data_holes w n in
    let la = lifted tree entries a in
    distinct_data all;
    assert (repeated [] (keys a) == []);
    assert (check_args rd entries (entry_addrs entries) a == Ok ());
    assert (unbound_required entries a == []);
    check_args_ok_spaced rd entries (entry_addrs entries) a;
    first_unknown_spaced all (keys a);
    keys_lifted tree entries a;
    assert (first_unknown (map addr_of dh) (keys la) == None);
    guard_total_shape dh;
    (match guard_total dh with
     | Some _ -> ()
     | None ->
       let aux (h:hole_decl)
         : Lemma (memp h dh ==> (find_hole h.h_addr all == Some h /\ is_data h /\ has_key h.h_addr a)) =
         memp_filter_data h all; distinct_find h all; for_all_memp hole_total h dh;
         data_required h; required_bound all a h
       in
       FStar.Classical.forall_intro aux;
       walk_wf rd w tree all a true n dh;
       open_slot_shape w la;
       assert (apply rd w la n ==
               (match try_pick (open_slot w) la with
                | Some e -> Error e
                | None -> bind_walk rd w true la n [] dh)))

(* ======================================================================================
   14. THE HANDLER TABLE (Phase 354) — `Function.bindHandlers`, the behaviour-axis analogue of
       `apply`: the three checks clause for clause, the table a `Map` is, and what the operation
       computes — the acceptance characterised exactly, each refusal saying what is refused and
       why, and the whole result a function of the bindings rather than of their arrival order.
   ====================================================================================== *)

(* F#: `Set.isSubset` on lists read as sets — every member of `a` is in `b`. *)
let rec subset (a b:list string) : Tot bool =
  match a with
  | [] -> true
  | h :: t -> mem h b && subset t b

let rec subset_mem (a b:list string) (x:string)
  : Lemma (requires subset a b /\ mem x a) (ensures mem x b) =
  match a with
  | [] -> ()
  | h :: t -> if x = h then () else subset_mem t b x

let rec subset_intro (a b:list string)
  : Lemma (requires (forall (x:string). mem x a ==> mem x b)) (ensures subset a b) =
  match a with
  | [] -> ()
  | _ :: t -> subset_intro t b

(* F#: `HandlerBinding<'Handler>` — the host's handler, opaque, beside the effect it declares. *)
type handler_binding (h:Type) = { hb_handler: h; hb_effect: effect_class }

(* F#: `Map<string, HandlerBinding<'Handler>>`, read as its key-ordered list — as `args` reads
   `Map<string, Arg<'Node>>`. *)
type handler_map (h:Type) = list (string & handler_binding h)

(* F#: `HandlerTable<'Handler>`. *)
type handler_table (h:Type) = { ht_handlers: handler_map h }

(* F#: `BindHandlerError`. *)
type bind_handler_error =
  | UnknownActionAddr          : addr:string -> declared_actions:list string -> bind_handler_error
  | NotAnActionHole            : addr:string -> bind_handler_error
  | RequiredActionsUnbound     : addrs:list string -> bind_handler_error
  | HandlerEffectExceedsCeiling: addr:string -> ceiling:effect_class -> handler:effect_class -> bind_handler_error

(* F#: `bindHandlers`'s `actionHoles` — `List.choose` of the action holes, each with its ceiling,
   in declaration order. *)
let rec action_holes (hs:list hole_decl) : Tot (list (string & effect_class)) =
  match hs with
  | [] -> []
  | h :: t ->
    (match h.h_kind with
     | ActionHole e -> (h.h_addr, e) :: action_holes t
     | _ -> action_holes t)

(* F#: `bindHandlers`'s local `checkKeys` — check 1, over `Map.toList handlers` (key order). *)
let rec check_keys (#h:Type) (action_addrs all_addrs:list string) (hs:handler_map h)
  : Tot (outcome unit bind_handler_error) =
  match hs with
  | [] -> Ok ()
  | (addr, _) :: rest ->
    if mem addr action_addrs then check_keys action_addrs all_addrs rest
    else if mem addr all_addrs then Error (NotAnActionHole addr)
    else Error (UnknownActionAddr addr action_addrs)

(* F#: `bindHandlers`'s local `checkEffects` — check 2, over the action holes in declaration order. *)
let rec check_effects (#h:Type) (hs:handler_map h) (holes:list (string & effect_class))
  : Tot (outcome unit bind_handler_error) =
  match holes with
  | [] -> Ok ()
  | (addr, ceiling) :: rest ->
    (match assoc addr hs with
     | Some hb ->
       if not (covers ceiling hb.hb_effect) then Error (HandlerEffectExceedsCeiling addr ceiling hb.hb_effect)
       else check_effects hs rest
     | None -> check_effects hs rest)

(* F#: `fun a -> not (Map.containsKey a handlers)`. *)
let no_handler (#h:Type) (hs:handler_map h) (a:string) : Tot bool = not (has_key a hs)

(* F#: `Function.bindHandlers`. *)
let bind_handlers (#node #h:Type) (w:witness node) (hs:handler_map h) (n:node)
  : Tot (outcome (handler_table h) bind_handler_error) =
  let all_addrs = map addr_of (w.holes n) in
  let ah = action_holes (w.holes n) in
  let action_addrs = keys ah in
  match check_keys action_addrs all_addrs hs with
  | Error e -> Error e
  | Ok () ->
    match check_effects hs ah with
    | Error e -> Error e
    | Ok () ->
      match filter (no_handler hs) action_addrs with
      | [] -> Ok ({ ht_handlers = hs })
      | u -> Error (RequiredActionsUnbound u)

(* ---- the table a `Map` is ---- *)

(* F#: `Map.add` on a map read as its key-ordered list — an equal key is REPLACED, a new one takes
   its place in the order. *)
let rec map_add (#a:Type) (le:string -> string -> bool) (k:string) (v:a) (l:list (string & a))
  : Tot (list (string & a)) =
  match l with
  | [] -> [(k, v)]
  | (k', v') :: t ->
    if k = k' then (k, v) :: t
    else if le k k' then (k, v) :: l
    else (k', v') :: map_add le k v t

(* F#: `Map.ofList` — the fold of `Map.add` from the left, so a later binding of a key replaces an
   earlier one. *)
let rec map_fold (#a:Type) (le:string -> string -> bool) (acc l:list (string & a))
  : Tot (list (string & a)) (decreases l) =
  match l with
  | [] -> acc
  | (k, v) :: t -> map_fold le (map_add le k v acc) t

let map_of_list (#a:Type) (le:string -> string -> bool) (l:list (string & a)) : Tot (list (string & a)) =
  map_fold le [] l

(* The handlers as they ARRIVE — a list in the caller's order — bound: F#
   `Function.bindHandlers w (Map.ofList handlers) node`. *)
let bind_handlers_of (#node #h:Type) (le:string -> string -> bool) (w:witness node)
                     (arriving:handler_map h) (n:node)
  : Tot (outcome (handler_table h) bind_handler_error) =
  bind_handlers w (map_of_list le arriving) n

(* F#: a `Map`'s keys are ordered. *)
let rec sorted_k (#a:Type) (le:string -> string -> bool) (l:list (string & a)) : Tot bool =
  match l with
  | [] -> true
  | x :: tl ->
    (match tl with
     | [] -> true
     | y :: _ -> le (fst x) (fst y) && sorted_k le tl)

let rec map_add_keys (#a:Type) (le:string -> string -> bool) (k:string) (v:a) (l:list (string & a)) (n:string)
  : Lemma (mem n (keys (map_add le k v l)) = (n = k || mem n (keys l)))
  = match l with
    | [] -> ()
    | _ :: t -> map_add_keys le k v t n

(* Adding a NEW key keeps the keys distinct, keeps the order, and adds exactly the one binding. *)
let rec map_add_distinct (#a:Type) (le:string -> string -> bool) (k:string) (v:a) (l:list (string & a))
  : Lemma (requires distinct (keys l) /\ not (mem k (keys l)))
          (ensures distinct (keys (map_add le k v l)))
  = match l with
    | [] -> ()
    | (k', _) :: t ->
      if le k k' then ()
      else (map_add_distinct le k v t; map_add_keys le k v t k')

let rec map_add_sorted (#a:Type) (le:string -> string -> bool) (k:string) (v:a) (l:list (string & a))
  : Lemma (requires total_order le /\ sorted_k le l)
          (ensures sorted_k le (map_add le k v l))
  = match l with
    | [] -> ()
    | (k', _) :: t ->
      if k = k' then ()
      else if le k k' then ()
      else map_add_sorted le k v t

let rec map_add_memp (#a:Type) (le:string -> string -> bool) (k:string) (v:a) (l:list (string & a))
                     (x:(string & a))
  : Lemma (requires not (mem k (keys l)))
          (ensures memp x (map_add le k v l) <==> (x == (k, v) \/ memp x l))
  = match l with
    | [] -> ()
    | (k', _) :: t ->
      if le k k' then ()
      else map_add_memp le k v t x

(* The fold over DISTINCT new keys: ordered, distinct, and holding exactly the bindings given. *)
let rec map_fold_facts (#a:Type) (le:string -> string -> bool) (acc l:list (string & a))
  : Lemma (requires total_order le /\ sorted_k le acc /\ distinct (keys acc) /\ distinct (keys l) /\
                    (forall (n:string). mem n (keys l) ==> not (mem n (keys acc))))
          (ensures (let r = map_fold le acc l in
                    sorted_k le r /\ distinct (keys r) /\
                    (forall (x:(string & a)). memp x r <==> (memp x acc \/ memp x l))))
          (decreases l)
  = match l with
    | [] -> ()
    | (k, v) :: t ->
      let acc' = map_add le k v acc in
      map_add_sorted le k v acc;
      map_add_distinct le k v acc;
      FStar.Classical.forall_intro (map_add_keys le k v acc);
      FStar.Classical.forall_intro (FStar.Classical.move_requires (map_add_memp le k v acc));
      map_fold_facts le acc' t

let rec memp_key (#a:Type) (x:(string & a)) (l:list (string & a))
  : Lemma (memp x l ==> mem (fst x) (keys l))
  = match l with
    | [] -> ()
    | _ :: t -> memp_key x t

let rec sorted_head_le_k (#a:Type) (le:string -> string -> bool) (x:(string & a)) (t:list (string & a))
                         (y:(string & a))
  : Lemma (requires total_order le /\ sorted_k le (x :: t) /\ memp y t)
          (ensures le (fst x) (fst y))
          (decreases t)
  = match t with
    | [] -> ()
    | h :: t' ->
      FStar.Classical.or_elim #(y == h) #(memp y t') #(fun _ -> le (fst x) (fst y))
        (fun _ -> ())
        (fun _ -> sorted_head_le_k le h t' y)

(* A key-ordered list with distinct keys is determined by its bindings — `sorted_unique_b` over any
   value type, membership propositional because a handler has no decidable equality. *)
let rec sorted_unique_k (#a:Type) (le:string -> string -> bool) (s1 s2:list (string & a))
  : Lemma (requires total_order le /\ sorted_k le s1 /\ sorted_k le s2 /\
                    distinct (keys s1) /\ distinct (keys s2) /\
                    (forall (x:(string & a)). memp x s1 <==> memp x s2))
          (ensures s1 == s2)
          (decreases s1)
  = match s1, s2 with
    | [], [] -> ()
    | [], h :: _ -> assert (memp h s2)
    | h :: _, [] -> assert (memp h s1)
    | h1 :: t1, h2 :: t2 ->
      assert (memp h1 s1); assert (memp h2 s2);
      memp_key h1 t2; memp_key h2 t1;
      if fst h1 = fst h2 then begin
        assert (h1 == h2);
        let aux (x:(string & a)) : Lemma (memp x t1 <==> memp x t2) =
          memp_key x t1; memp_key x t2;
          assert (memp x s1 <==> memp x s2)
        in
        FStar.Classical.forall_intro aux;
        sorted_unique_k le t1 t2
      end else begin
        assert (memp h1 t2); assert (memp h2 t1);
        sorted_head_le_k le h2 t2 h1;
        sorted_head_le_k le h1 t1 h2
      end

(* `Map.ofList` of a list with distinct keys is a function of the BINDINGS, never of their order. *)
let map_of_list_order_independent (#a:Type) (le:string -> string -> bool) (l l':list (string & a))
  : Lemma (requires total_order le /\ distinct (keys l) /\ distinct (keys l') /\
                    (forall (x:(string & a)). memp x l <==> memp x l'))
          (ensures map_of_list le l == map_of_list le l')
  = map_fold_facts le [] l;
    map_fold_facts le [] l';
    sorted_unique_k le (map_of_list le l) (map_of_list le l')

(* ---- what the three checks say ---- *)

(* Check 1 passes exactly when every handler key is an action hole's address ... *)
let rec check_keys_ok (#h:Type) (action_addrs all_addrs:list string) (hs:handler_map h)
  : Lemma (check_keys action_addrs all_addrs hs == Ok () <==> subset (keys hs) action_addrs)
  = match hs with
    | [] -> ()
    | _ :: rest -> check_keys_ok action_addrs all_addrs rest

(* ... and otherwise refuses the FIRST key, in key order, that is not one — `NotAnActionHole` when
   the key is a declared hole of another kind, `UnknownActionAddr` naming the declared actions when
   it is no hole at all. `first_unknown` is the walk `bindArgs` names an undeclared address with. *)
let rec check_keys_refusal (#h:Type) (action_addrs all_addrs:list string) (hs:handler_map h)
  : Lemma (match check_keys action_addrs all_addrs hs with
           | Ok () -> first_unknown action_addrs (keys hs) == None
           | Error (NotAnActionHole a) ->
             first_unknown action_addrs (keys hs) == Some a /\ mem a all_addrs
           | Error (UnknownActionAddr a declared) ->
             first_unknown action_addrs (keys hs) == Some a /\ not (mem a all_addrs) /\
             declared == action_addrs
           | Error _ -> False)
  = match hs with
    | [] -> ()
    | (addr, _) :: rest -> if mem addr action_addrs then check_keys_refusal action_addrs all_addrs rest else ()

(* The hole's bound handler declares more than the hole's ceiling admits. *)
let exceeds (#h:Type) (hs:handler_map h) (hole:(string & effect_class)) : Tot bool =
  match assoc (fst hole) hs with
  | Some hb -> not (covers (snd hole) hb.hb_effect)
  | None -> false

(* `x` is the FIRST member of `l`, in order, that `bad` holds of. *)
let rec first_is (#a:Type) (bad:a -> bool) (x:a) (l:list a) : Tot prop =
  match l with
  | [] -> False
  | y :: t -> if bad y then x == y else first_is bad x t

let rec first_is_holds (#a:Type) (bad:a -> bool) (x:a) (l:list a)
  : Lemma (first_is bad x l ==> (memp x l /\ bad x))
  = match l with
    | [] -> ()
    | y :: t -> if bad y then () else first_is_holds bad x t

(* Check 2 passes exactly when no bound handler exceeds its hole's ceiling, and otherwise refuses
   the FIRST action hole, in declaration order, whose handler does — naming the address, the
   ceiling and the handler's own declared effect. *)
let rec check_effects_exact (#h:Type) (hs:handler_map h) (holes:list (string & effect_class))
  : Lemma (match check_effects hs holes with
           | Ok () -> forall (hole:(string & effect_class)). memp hole holes ==> not (exceeds hs hole)
           | Error (HandlerEffectExceedsCeiling a c e) ->
             first_is (exceeds hs) (a, c) holes /\
             (match assoc a hs with
              | Some hb -> hb.hb_effect == e /\ not (covers c e)
              | None -> False)
           | Error _ -> False)
  = match holes with
    | [] -> ()
    | (addr, ceiling) :: rest ->
      (match assoc addr hs with
       | Some hb -> if not (covers ceiling hb.hb_effect) then () else check_effects_exact hs rest
       | None -> check_effects_exact hs rest)

(* Check 3's list is exactly the action addresses no handler is bound to. *)
let rec unbound_exact (#h:Type) (hs:handler_map h) (addrs:list string) (a:string)
  : Lemma (mem a (filter (no_handler hs) addrs) = (mem a addrs && not (has_key a hs)))
  = match addrs with
    | [] -> ()
    | _ :: t -> unbound_exact hs t a

let rec memp_hole_key (hole:(string & effect_class)) (holes:list (string & effect_class))
  : Lemma (memp hole holes ==> mem (fst hole) (keys holes))
  = match holes with
    | [] -> ()
    | _ :: t -> memp_hole_key hole t

let rec has_key_assoc_g (#a:Type) (k:string) (l:list (string & a))
  : Lemma (has_key k l = Some? (assoc k l))
  = match l with
    | [] -> ()
    | _ :: t -> has_key_assoc_g k t

(* A hole's handler is bound and within the hole's ceiling. *)
let bound_within (#h:Type) (hs:handler_map h) (hole:(string & effect_class)) : Tot bool =
  match assoc (fst hole) hs with
  | Some hb -> covers (snd hole) hb.hb_effect
  | None -> false

let rec all_bound_filter (#h:Type) (hs:handler_map h) (holes:list (string & effect_class))
  : Lemma (requires forall (hole:(string & effect_class)). memp hole holes ==> bound_within hs hole)
          (ensures filter (no_handler hs) (keys holes) == [])
  = match holes with
    | [] -> ()
    | (a, c) :: t ->
      assert (memp (a, c) holes);
      assert (bound_within hs (a, c));
      has_key_assoc_g a hs;
      all_bound_filter hs t

(* THE TENTH THEOREM (Phase 354), `bind_handlers_exact`. F#: `Function.bindHandlers`.

   ACCEPTANCE. The handlers are accepted exactly when every handler key is a declared action
   hole's address and every declared action hole has a handler whose declared effect its ceiling
   covers — and the table handed back is the handlers given, unchanged: each action hole bound to
   exactly its handler, and no handler for anything else.

   REFUSAL, in the order the checks run. A handler key that is no action hole's address is refused
   by name, the first such in key order: `NotAnActionHole` when it is a declared hole of another
   kind, `UnknownActionAddr` — naming every declared action — when it is no hole at all. With
   every key an action hole's, a handler declaring more than its hole's ceiling is refused
   `HandlerEffectExceedsCeiling`, the first such hole in declaration order, naming the ceiling and
   the handler's effect. With every bound handler inside its ceiling, the action holes left without
   a handler are refused `RequiredActionsUnbound`, naming exactly those addresses. *)
let bind_handlers_exact (#node #h:Type) (w:witness node) (hs:handler_map h) (n:node)
  : Lemma (let holes = w.holes n in
           let ah = action_holes holes in
           let aa = keys ah in
           match bind_handlers w hs n with
           | Ok t ->
             t.ht_handlers == hs /\ subset (keys hs) aa /\
             (forall (hole:(string & effect_class)). memp hole ah ==> bound_within hs hole)
           | Error (NotAnActionHole a) ->
             first_unknown aa (keys hs) == Some a /\ mem a (map addr_of holes)
           | Error (UnknownActionAddr a declared) ->
             first_unknown aa (keys hs) == Some a /\ not (mem a (map addr_of holes)) /\ declared == aa
           | Error (HandlerEffectExceedsCeiling a c e) ->
             subset (keys hs) aa /\ first_is (exceeds hs) (a, c) ah /\
             (match assoc a hs with
              | Some hb -> hb.hb_effect == e /\ not (covers c e)
              | None -> False)
           | Error (RequiredActionsUnbound u) ->
             subset (keys hs) aa /\
             (forall (hole:(string & effect_class)). memp hole ah ==> not (exceeds hs hole)) /\
             Cons? u /\ (forall (a:string). mem a u <==> (mem a aa /\ not (has_key a hs))))
  = let holes = w.holes n in
    let ah = action_holes holes in
    let aa = keys ah in
    let all = map addr_of holes in
    check_keys_ok aa all hs;
    check_keys_refusal aa all hs;
    match check_keys aa all hs with
    | Error _ -> ()
    | Ok () ->
      check_effects_exact hs ah;
      (match check_effects hs ah with
       | Error _ -> ()
       | Ok () ->
         FStar.Classical.forall_intro (unbound_exact hs aa);
         (match filter (no_handler hs) aa with
          | [] ->
            let aux (hole:(string & effect_class)) : Lemma (memp hole ah ==> bound_within hs hole) =
              memp_hole_key hole ah; has_key_assoc_g (fst hole) hs;
              assert (mem (fst hole) (filter (no_handler hs) aa) = false)
            in
            FStar.Classical.forall_intro aux
          | _ -> ()))

(* ... and the acceptance condition is SUFFICIENT: the Ok arm above reads both ways. *)
let bind_handlers_complete (#node #h:Type) (w:witness node) (hs:handler_map h) (n:node)
  : Lemma (requires (let ah = action_holes (w.holes n) in
                     subset (keys hs) (keys ah) /\
                     (forall (hole:(string & effect_class)). memp hole ah ==> bound_within hs hole)))
          (ensures bind_handlers w hs n == Ok ({ ht_handlers = hs }))
  = let holes = w.holes n in
    let ah = action_holes holes in
    let aa = keys ah in
    check_keys_ok aa (map addr_of holes) hs;
    check_effects_exact hs ah;
    (match check_effects hs ah with
     | Error (HandlerEffectExceedsCeiling a c _) ->
       first_is_holds (exceeds hs) (a, c) ah;
       assert (bound_within hs (a, c))
     | _ -> ());
    all_bound_filter hs ah

(* THE ELEVENTH THEOREM (Phase 354), `bind_handlers_order_independent`. F#: `Function.bindHandlers`
   over `Map.ofList`. Two arrivals of the same handlers — distinct addresses, the same bindings, in
   any order — bind identically: the same table when accepted and the same refusal when not. The
   comparator premise is the one the capture key's determinism takes (`total_order`), true of the
   ordinal order an F# `Map<string, _>` keeps its keys in. *)
let bind_handlers_order_independent (#node #h:Type) (le:string -> string -> bool) (w:witness node)
                                    (l l':handler_map h) (n:node)
  : Lemma (requires total_order le /\ distinct (keys l) /\ distinct (keys l') /\
                    (forall (x:(string & handler_binding h)). memp x l <==> memp x l'))
          (ensures bind_handlers_of le w l n == bind_handlers_of le w l' n)
  = map_of_list_order_independent le l l'

(* ======================================================================================
   15. THE CAPABILITY PIPELINE (Phase 354) — `CapabilityPipeline.typeCheck` as Phase 295
       strengthened it, the reference evaluator `eval`, the dirty set and the incremental
       `evalFrom`, clause for clause; then what they compute: an accepted pipeline's declaration
       order is a topological order of its edges and the verdict does not depend on which such
       order was written, a refused pipeline runs no body, an accepted one never meets the
       evaluator's two "unreachable" arms, and `evalFrom` over a prior evaluation equals `eval`.
   ====================================================================================== *)

(* ---- sets as lists, each naming the F# `Set` function it stands for ---- *)

(* F#: `Set.difference a b`. *)
let rec diff (a b:list string) : Tot (list string) =
  match a with
  | [] -> []
  | h :: t -> if mem h b then diff t b else h :: diff t b

(* F#: `Set.union a b`, read as membership: `a`, then what `b` adds. *)
let union (a b:list string) : Tot (list string) = app a (diff b a)

(* A set holds an id once: `Set.ofList`. *)
let rec dedup (l:list string) : Tot (list string) =
  match l with
  | [] -> []
  | h :: t -> if mem h t then dedup t else h :: dedup t

let rec app_nil (#a:Type) (l:list a) : Lemma (app l [] == l) =
  match l with
  | [] -> ()
  | _ :: t -> app_nil t

let rec mem_app (x:string) (l m:list string) : Lemma (mem x (app l m) == (mem x l || mem x m)) =
  match l with
  | [] -> ()
  | _ :: t -> mem_app x t m

let rec mem_diff (x:string) (a b:list string) : Lemma (mem x (diff a b) == (mem x a && not (mem x b))) =
  match a with
  | [] -> ()
  | _ :: t -> mem_diff x t b

let mem_union (x:string) (a b:list string) : Lemma (mem x (union a b) == (mem x a || mem x b)) =
  mem_app x a (diff b a);
  mem_diff x b a

let rec mem_dedup (x:string) (l:list string) : Lemma (mem x (dedup l) == mem x l) =
  match l with
  | [] -> ()
  | _ :: t -> mem_dedup x t

let rec diff_mono (rng a a':list string)
  : Lemma (requires (forall (y:string). mem y a ==> mem y a')) (ensures len (diff rng a') <= len (diff rng a)) =
  match rng with
  | [] -> ()
  | _ :: t -> diff_mono t a a'

let rec diff_shrink (rng a a':list string) (x:string)
  : Lemma (requires (forall (y:string). mem y a ==> mem y a') /\ mem x rng /\ not (mem x a) /\ mem x a')
          (ensures len (diff rng a') < len (diff rng a)) =
  match rng with
  | [] -> ()
  | h :: t -> if h = x then diff_mono t a a' else diff_shrink t a a' x

(* ---- the vocabulary ---- *)

(* F#: `ArgSource`. *)
type arg_source =
  | Literal  : value:string -> arg_source
  | FromNode : node_id:string -> arg_source

(* F#: `PipelineNode`. *)
type pipeline_node =
  | Source : id:string -> data_ref:string -> output_type:value_space -> pipeline_node
  | Invoke : id:string -> capability_id:string -> output_type:value_space ->
             args:list (string & arg_source) -> pipeline_node

(* F#: `CapabilityPipeline`. *)
type pipeline = { p_nodes: list pipeline_node }

(* F#: `PipelineError`. *)
type pipeline_error =
  | DuplicateNode           : id:string -> pipeline_error
  | UnknownNode             : id:string -> pipeline_error
  | PipelineNoSuchCapability: id:string -> known:list string -> pipeline_error
  | PipelineArgRefused      : at:string -> reason:invoke_error -> pipeline_error
  | PipelineCycle           : at:string -> cycle:list string -> pipeline_error
  | EdgeTypeMismatch        : at:string -> addr:string -> producer:string -> consumer:string -> pipeline_error
  | PipelineForwardEdge     : at:string -> addr:string -> upstream:string -> pipeline_error

(* F#: `PipelineArg<'v>`. *)
type pipeline_arg (v:Type) =
  | FromUpstream : v -> pipeline_arg v
  | LiteralArg   : string -> pipeline_arg v

(* F#: `PipelineEvalError`. *)
type pipeline_eval_error =
  | EvalIllTyped  : reason:pipeline_error -> pipeline_eval_error
  | EvalNodeFailed: at:string -> message:string -> pipeline_eval_error
  | EvalArgRefused: at:string -> reason:invoke_error -> pipeline_eval_error

(* F#: `CapabilityLookup` — where a pipeline resolves its `Invoke` nodes, and the ids the refusal
   names. A record of a function and a list, as in production: a hand-built lookup may keep the
   two out of step, and nothing here assumes it does not. *)
noeq type capability_lookup = {
  lk_find:  string -> option capability;
  lk_known: list string
}

(* F#: `CapabilityLookup.ofRegistry` — the registry as a lookup. *)
let lookup_of_registry (r:registry) : Tot capability_lookup =
  { lk_find = (fun id -> find_cap id r.capabilities); lk_known = ids r.capabilities }

(* The two FLOAT comparisons `Space.subsumes` makes, a parameter for the reason `float_in` is one:
   the model has no float, and a float range's bounds cross as opaque carriers.
   `float_within rl rh al ah` — F#: `rl <= al && ah <= rh` over the four carriers' floats.
   `int_within rl rh al ah`   — F#: `rl <= float al && float ah <= rh`. *)
noeq type feed_readers = {
  float_within: string -> string -> string -> string -> bool;
  int_within:   string -> string -> int -> int -> bool
}

(* F#: `xs |> List.forall (fun x -> x.Length >= rl && x.Length <= rh)`. *)
let rec all_len_in (rd:readers) (lo hi:int) (xs:list string) : Tot bool =
  match xs with
  | [] -> true
  | x :: t -> rd.str_len x >= lo && rd.str_len x <= hi && all_len_in rd lo hi t

(* F#: `Space.subsumes required available` (Phase 295) — THE space relation. *)
let subsumes (fr:feed_readers) (rd:readers) (required available:value_space) : Tot bool =
  match required, available with
  | IntRange rl rh, IntRange al ah -> rl <= al && ah <= rh
  | FloatRange rl rh, FloatRange al ah -> fr.float_within rl rh al ah
  | FloatRange rl rh, IntRange al ah -> fr.int_within rl rh al ah
  | StringLen rl rh, StringLen al ah -> rl <= al && ah <= rh
  | StringLen rl rh, Enum xs -> all_len_in rd rl rh xs
  | Enum rs, Enum xs -> subset xs rs
  | AnyString, a -> not (SlotTree? a)
  | SlotTree None, SlotTree _ -> true
  | SlotTree (Some rk), SlotTree (Some ak) -> rk = ak
  | _ -> false

(* F#: `CapabilityPipeline.spaceTag` — the space FAMILY an `EdgeTypeMismatch` names. *)
let space_tag (s:value_space) : Tot string =
  match s with
  | IntRange _ _ -> "int"
  | FloatRange _ _ -> "float"
  | StringLen _ _ -> "string"
  | Enum _ -> "enum"
  | AnyString -> "anyString"
  | SlotTree _ -> "slotTree"

(* F#: `CapabilityPipeline.nodeId`. *)
let node_id (n:pipeline_node) : Tot string =
  match n with
  | Source id _ _ -> id
  | Invoke id _ _ _ -> id

(* F#: `CapabilityPipeline.nodeOutputType`. *)
let node_output (n:pipeline_node) : Tot value_space =
  match n with
  | Source _ _ ty -> ty
  | Invoke _ _ ty _ -> ty

(* F#: `upstreams`'s `List.choose` — the `FromNode` ids, in argument order. *)
let rec upstreams_of (a:list (string & arg_source)) : Tot (list string) =
  match a with
  | [] -> []
  | (_, FromNode up) :: t -> up :: upstreams_of t
  | (_, Literal _) :: t -> upstreams_of t

(* F#: `CapabilityPipeline.upstreams`. *)
let upstreams (n:pipeline_node) : Tot (list string) =
  match n with
  | Source _ _ _ -> []
  | Invoke _ _ _ a -> upstreams_of a

(* F#: `p.Nodes |> List.map nodeId`. *)
let rec node_ids (ns:list pipeline_node) : Tot (list string) =
  match ns with
  | [] -> []
  | n :: t -> node_id n :: node_ids t

(* F#: `Map.tryFind id nodeById`. The map is `Map.ofList`, which keeps the LAST node of a repeated
   id; this is the FIRST. The two agree over distinct ids, and `typeCheck` consults the map only
   after its duplicate scan has passed. *)
let rec find_node (id:string) (ns:list pipeline_node) : Tot (option pipeline_node) =
  match ns with
  | [] -> None
  | n :: t -> if node_id n = id then Some n else find_node id t

(* F#: `ids |> List.countBy id |> List.tryPick (fun (k, c) -> if c > 1 then Some k else None)` — the
   first id, in order of first occurrence, that occurs again. *)
let rec first_dup (l:list string) : Tot (option string) =
  match l with
  | [] -> None
  | x :: t -> if mem x t then Some x else first_dup t

(* F#: `position.[id]` — the index of a declared id (the list's length for one that is not). *)
let rec index_of (k:string) (l:list string) : Tot nat =
  match l with
  | [] -> 0
  | x :: t -> if x = k then 0 else 1 + index_of k t

let rec find_node_mem (id:string) (ns:list pipeline_node)
  : Lemma (Some? (find_node id ns) ==> mem id (node_ids ns))
  = match ns with
    | [] -> ()
    | _ :: t -> find_node_mem id t

(* A visited set only grows. *)
let superset (seen seen':list string) : Tot prop = forall (x:string). mem x seen ==> mem x seen'

(* F#: `typeCheck`'s local `pathTo`, its `walk` and `tryUps` — the search for a path from `start`
   back to `target` along `FromNode` edges, each node visited once. Production's recursion
   terminates because the visited set grows inside a finite pipeline; here that is the checked
   measure — the declared ids not yet visited — and the refinement on the result is what carries
   "only grows" from one call to the next. *)
let rec walk (ns:list pipeline_node) (target:string) (seen:list string) (cur:string)
  : Tot (r:(list string & option (list string)){superset seen (fst r)})
        (decreases %[len (diff (node_ids ns) seen); 0; 0])
  = if mem cur seen then (seen, None)
    else
      let seen1 = cur :: seen in
      match find_node cur ns with
      | None -> (seen1, None)
      | Some n ->
        let ups = upstreams n in
        if mem target ups then (seen1, Some [cur])
        else begin
          find_node_mem cur ns;
          diff_shrink (node_ids ns) seen seen1 cur;
          try_ups ns target cur seen1 ups
        end
and try_ups (ns:list pipeline_node) (target cur:string) (seen:list string) (ups:list string)
  : Tot (r:(list string & option (list string)){superset seen (fst r)})
        (decreases %[len (diff (node_ids ns) seen); 1; len ups])
  = match ups with
    | [] -> (seen, None)
    | u :: rest ->
      let r = walk ns target seen u in
      (match snd r with
       | Some path -> (fst r, Some (cur :: path))
       | None ->
         diff_mono (node_ids ns) seen (fst r);
         try_ups ns target cur (fst r) rest)

(* F#: `pathTo target start`. *)
let path_to (ns:list pipeline_node) (target start:string) : Tot (option (list string)) =
  snd (walk ns target [] start)

(* F#: `typeCheck`'s local `edgeFault` — a `FromNode up` edge into an argument of space
   `arg_space`: the upstream is a declared node, is not the node itself, comes EARLIER in
   declaration order (a later one closes a cycle or points forward), and its output feeds the
   argument. *)
let edge_fault (fr:feed_readers) (rd:readers) (ns:list pipeline_node) (all_ids:list string)
               (nid addr up:string) (arg_sp:value_space)
  : Tot (option pipeline_error) =
  match find_node up ns with
  | None -> Some (UnknownNode up)
  | Some up_node ->
    if up = nid then Some (PipelineCycle nid [nid])
    else if index_of up all_ids > index_of nid all_ids then
      (match path_to ns nid up with
       | Some path -> Some (PipelineCycle nid (nid :: path))
       | None -> Some (PipelineForwardEdge nid addr up))
    else if subsumes fr rd arg_sp (node_output up_node) then None
    else Some (EdgeTypeMismatch nid addr (space_tag (node_output up_node)) (space_tag arg_sp))

(* F#: `Capability.argFault` — the refusal one argument earns on its own, by `validateArgs`'s rules. *)
let arg_fault (rd:readers) (c:capability) (declared:list string) (addr value:string)
  : Tot (option invoke_error) =
  match find_entry addr c.c_signature.sg_holes with
  | None -> Some (UnknownArg addr declared)
  | Some h ->
    match arg_space h with
    | None -> Some (UninvocableArg addr)
    | Some space ->
      if SlotTree? space && None? (rd.kind_of value) then Some (UninvocableArg addr)
      else if validate rd space value then None
      else Some (ArgOutOfSpace addr space value)

(* F#: `typeCheck`'s local `argFault` — a literal is refused as the capability refuses it, an edge
   by the hole it addresses (declared, and taking a value) and then by `edgeFault`. *)
let pipe_arg_fault (fr:feed_readers) (rd:readers) (ns:list pipeline_node) (all_ids:list string)
                   (cap:capability) (declared:list string) (nid:string) (b:(string & arg_source))
  : Tot (option pipeline_error) =
  let (addr, src) = b in
  match src with
  | Literal v ->
    (match arg_fault rd cap declared addr v with
     | Some e -> Some (PipelineArgRefused nid e)
     | None -> None)
  | FromNode up ->
    (match find_entry addr cap.c_signature.sg_holes with
     | None -> Some (PipelineArgRefused nid (UnknownArg addr declared))
     | Some h ->
       (match h.s_space with
        | None -> Some (PipelineArgRefused nid (UninvocableArg addr))
        | Some sp -> edge_fault fr rd ns all_ids nid addr up sp))

(* F#: `holes |> List.filter (fun h -> h.Required && not (bound.Contains h.Addr)) |> List.map Addr`. *)
let rec unbound_keys (holes:list sig_entry) (ks:list string) : Tot (list string) =
  match holes with
  | [] -> []
  | h :: t ->
    if h.s_required && not (mem h.s_addr ks) then h.s_addr :: unbound_keys t ks
    else unbound_keys t ks

(* F#: one step of `typeCheck`'s local `go` — the refusal a node earns, or none. *)
let node_fault (fr:feed_readers) (rd:readers) (lk:capability_lookup) (ns:list pipeline_node)
               (all_ids:list string) (n:pipeline_node)
  : Tot (option pipeline_error) =
  match n with
  | Source _ _ _ -> None
  | Invoke nid cap_id _ a ->
    match lk.lk_find cap_id with
    | None -> Some (PipelineNoSuchCapability cap_id lk.lk_known)
    | Some cap ->
      let holes = cap.c_signature.sg_holes in
      let declared = entry_addrs holes in
      match repeated [] (keys a) with
      | d :: _ -> Some (PipelineArgRefused nid (DuplicateArg d))
      | [] ->
        match try_pick (pipe_arg_fault fr rd ns all_ids cap declared nid) a with
        | Some e -> Some e
        | None ->
          match unbound_keys holes (keys a) with
          | [] -> None
          | u -> Some (PipelineArgRefused nid (RequiredArgsUnbound u))

(* F#: `typeCheck`'s local `go` — the first refusal in declaration order. *)
let rec go_check (fr:feed_readers) (rd:readers) (lk:capability_lookup) (ns:list pipeline_node)
                 (all_ids:list string) (rest:list pipeline_node)
  : Tot (outcome unit pipeline_error) (decreases rest) =
  match rest with
  | [] -> Ok ()
  | n :: t ->
    match node_fault fr rd lk ns all_ids n with
    | Some e -> Error e
    | None -> go_check fr rd lk ns all_ids t

(* F#: `CapabilityPipeline.typeCheck`. *)
let type_check (fr:feed_readers) (rd:readers) (lk:capability_lookup) (p:pipeline)
  : Tot (outcome unit pipeline_error) =
  let all_ids = node_ids p.p_nodes in
  match first_dup all_ids with
  | Some d -> Error (DuplicateNode d)
  | None -> go_check fr rd lk p.p_nodes all_ids p.p_nodes

(* ---- evaluation ---- *)

(* F#: the host `body` — a node and its resolved arguments to a value, or a failure's message. *)
type node_body (v:Type) = pipeline_node -> list (string & pipeline_arg v) -> outcome v string

(* F#: `c.Signature.Holes |> List.tryPick (fun h -> if h.Addr = addr then h.Space else None)`. *)
let rec space_of (addr:string) (holes:list sig_entry) : Tot (option value_space) =
  match holes with
  | [] -> None
  | h :: t ->
    if h.s_addr = addr then
      (match h.s_space with
       | Some s -> Some s
       | None -> space_of addr t)
    else space_of addr t

(* F#: `runNode`'s local `spaceOf`. *)
let arg_space_of (lk:capability_lookup) (cap_id addr:string) : Tot (option value_space) =
  match lk.lk_find cap_id with
  | Some c -> space_of addr c.c_signature.sg_holes
  | None -> None

(* F#: `runNode`'s fold over the arguments — each edge resolved to its upstream's realised value,
   read through `spell` against the space of the hole it feeds; the accumulator is in reverse. *)
let rec resolve_args (#v:Type) (rd:readers) (lk:capability_lookup) (spell:v -> string)
                     (results:list (string & v)) (nid cap_id:string)
                     (a:list (string & arg_source)) (acc:list (string & pipeline_arg v))
  : Tot (outcome (list (string & pipeline_arg v)) pipeline_eval_error) (decreases a) =
  match a with
  | [] -> Ok acc
  | (addr, src) :: rest ->
    match src with
    | Literal s -> resolve_args rd lk spell results nid cap_id rest ((addr, LiteralArg s) :: acc)
    | FromNode up ->
      match assoc up results with
      | None -> Error (EvalIllTyped (PipelineForwardEdge nid addr up))
      | Some x ->
        match arg_space_of lk cap_id addr with
        | None -> Error (EvalIllTyped (PipelineArgRefused nid (UninvocableArg addr)))
        | Some space ->
          let spelled = spell x in
          if validate rd space spelled then
            resolve_args rd lk spell results nid cap_id rest ((addr, FromUpstream x) :: acc)
          else Error (EvalArgRefused nid (ArgOutOfSpace addr space spelled))

(* F#: `CapabilityPipeline.runNode` — resolve, then the host body; shared by `eval` and `evalFrom`. *)
let run_node (#v:Type) (rd:readers) (lk:capability_lookup) (spell:v -> string) (body:node_body v)
             (results:list (string & v)) (n:pipeline_node)
  : Tot (outcome v pipeline_eval_error) =
  let resolved : outcome (list (string & pipeline_arg v)) pipeline_eval_error =
    match n with
    | Source _ _ _ -> Ok []
    | Invoke nid cap_id _ a ->
      (match resolve_args rd lk spell results nid cap_id a [] with
       | Ok xs -> Ok (rev xs)
       | Error e -> Error e)
  in
  match resolved with
  | Error e -> Error e
  | Ok xs ->
    match body n xs with
    | Ok x -> Ok x
    | Error m -> Error (EvalNodeFailed (node_id n) m)

(* F#: `eval`'s local `go` — the fold in declaration order, threading the `id -> value` map
   (`Map.add` is the cons: `assoc` reads the latest binding of an id). *)
let rec eval_go (#v:Type) (rd:readers) (lk:capability_lookup) (spell:v -> string) (body:node_body v)
                (results:list (string & v)) (ns:list pipeline_node)
  : Tot (outcome (list (string & v)) pipeline_eval_error) (decreases ns) =
  match ns with
  | [] -> Ok results
  | n :: rest ->
    match run_node rd lk spell body results n with
    | Error e -> Error e
    | Ok x -> eval_go rd lk spell body ((node_id n, x) :: results) rest

(* F#: `CapabilityPipeline.eval` — it type-checks first (Phase 295). *)
let eval (#v:Type) (fr:feed_readers) (rd:readers) (lk:capability_lookup) (spell:v -> string)
         (body:node_body v) (p:pipeline)
  : Tot (outcome (list (string & v)) pipeline_eval_error) =
  match type_check fr rd lk p with
  | Error e -> Error (EvalIllTyped e)
  | Ok () -> eval_go rd lk spell body [] p.p_nodes

(* ---- the dirty set: `CapabilityPipeline.dirtySet` ---- *)

(* F#: each node id and the upstream ids it READS — the edges `dirtySet` inverts. *)
type dmap = list (string & list string)

let rec deps_of (ns:list pipeline_node) : Tot dmap =
  match ns with
  | [] -> []
  | n :: t -> (node_id n, upstreams n) :: deps_of t

(* `n` READS `r`: some entry of the map is `n`'s and holds `r`. *)
let rec edge (deps:dmap) (n r:string) : Tot bool =
  match deps with
  | [] -> false
  | (k, reads) :: t -> (k = n && mem r reads) || edge t n r

(* F#: `for _, src in args do match src with FromNode up -> yield up, id`. *)
let rec pairs_of (nd:string) (reads:list string) : Tot (list (string & string)) =
  match reads with
  | [] -> []
  | r :: t -> (r, nd) :: pairs_of nd t

(* F#: `dirtySet`'s list comprehension over the nodes. *)
let rec pairs (deps:dmap) : Tot (list (string & string)) =
  match deps with
  | [] -> []
  | (nd, reads) :: t -> app (pairs_of nd reads) (pairs t)

(* F#: `List.map fst`. *)
let rec firsts (ps:list (string & string)) : Tot (list string) =
  match ps with
  | [] -> []
  | (r, _) :: t -> r :: firsts t

(* F#: one group of `List.groupBy fst`, mapped through `List.map snd`. *)
let rec seconds_for (k:string) (ps:list (string & string)) : Tot (list string) =
  match ps with
  | [] -> []
  | (r, n) :: t -> if r = k then n :: seconds_for k t else seconds_for k t

(* F#: `List.map (fun (k, vs) -> k, vs |> List.map snd |> Set.ofList)` over the group keys. *)
let rec group_from (ks:list string) (ps:list (string & string)) : Tot dmap =
  match ks with
  | [] -> []
  | k :: t -> (k, dedup (seconds_for k ps)) :: group_from t ps

(* F#: `dirtySet`'s `dependents` — `id -> the ids with an edge from it`. *)
let dependents (deps:dmap) : Tot dmap =
  let ps = pairs deps in
  group_from (dedup (firsts ps)) ps

(* `Map.tryFind node dependents`, an absent id having none. *)
let dependents_of (d:dmap) (nd:string) : Tot (list string) =
  match assoc nd d with
  | Some ds -> ds
  | None -> []

let rec mem_pair (r n:string) (ps:list (string & string)) : Tot bool =
  match ps with
  | [] -> false
  | (r', n') :: t -> (r = r' && n = n') || mem_pair r n t

let rec mem_pair_app (r n:string) (p q:list (string & string))
  : Lemma (mem_pair r n (app p q) == (mem_pair r n p || mem_pair r n q)) =
  match p with
  | [] -> ()
  | _ :: t -> mem_pair_app r n t q

let rec mem_pairs_of (r n nd:string) (reads:list string)
  : Lemma (mem_pair r n (pairs_of nd reads) == (n = nd && mem r reads)) =
  match reads with
  | [] -> ()
  | _ :: t -> mem_pairs_of r n nd t

let rec pairs_edge (deps:dmap) (r n:string) : Lemma (mem_pair r n (pairs deps) == edge deps n r) =
  match deps with
  | [] -> ()
  | (nd, reads) :: t ->
    mem_pair_app r n (pairs_of nd reads) (pairs t);
    mem_pairs_of r n nd reads;
    pairs_edge t r n

let rec seconds_pair (k n:string) (ps:list (string & string))
  : Lemma (mem n (seconds_for k ps) == mem_pair k n ps) =
  match ps with
  | [] -> ()
  | _ :: t -> seconds_pair k n t

let rec pair_first (r n:string) (ps:list (string & string))
  : Lemma (requires mem_pair r n ps) (ensures mem r (firsts ps)) =
  match ps with
  | [] -> ()
  | (r', n') :: t -> if r = r' && n = n' then () else pair_first r n t

let rec group_lookup (k:string) (ks:list string) (ps:list (string & string))
  : Lemma (assoc k (group_from ks ps) == (if mem k ks then Some (dedup (seconds_for k ps)) else None)) =
  match ks with
  | [] -> ()
  | _ :: t -> group_lookup k t ps

(* THE INVERSION IS EXACT: `n` is among `r`'s dependents exactly when `n` reads `r`. *)
let dependents_edge (deps:dmap) (r n:string)
  : Lemma (mem n (dependents_of (dependents deps) r) == edge deps n r) =
  let ps = pairs deps in
  group_lookup r (dedup (firsts ps)) ps;
  mem_dedup r (firsts ps);
  mem_dedup n (seconds_for r ps);
  seconds_pair r n ps;
  pairs_edge deps r n;
  if mem_pair r n ps then pair_first r n ps else ()

(* F#: `(Set.empty, frontier) ||> Set.fold (fun s node -> match Map.tryFind node dependents with
   | Some deps -> Set.union s deps | None -> s)`. *)
let rec next_of (d:dmap) (frontier:list string) (s:list string) : Tot (list string) (decreases frontier) =
  match frontier with
  | [] -> s
  | nd :: t ->
    next_of d t (match assoc nd d with
                 | Some ds -> union s ds
                 | None -> s)

let rec any_dep (d:dmap) (frontier:list string) (x:string) : Tot bool =
  match frontier with
  | [] -> false
  | nd :: t -> mem x (dependents_of d nd) || any_dep d t x

let rec next_mem (d:dmap) (frontier s:list string) (x:string)
  : Lemma (ensures mem x (next_of d frontier s) == (mem x s || any_dep d frontier x)) (decreases frontier) =
  match frontier with
  | [] -> ()
  | nd :: t ->
    (match assoc nd d with
     | Some ds -> mem_union x s ds; next_mem d t (union s ds) x
     | None -> next_mem d t s x)

let rec any_dep_intro (d:dmap) (frontier:list string) (r x:string)
  : Lemma (requires mem r frontier /\ mem x (dependents_of d r)) (ensures any_dep d frontier x) =
  match frontier with
  | [] -> ()
  | nd :: t -> if r = nd then () else any_dep_intro d t r x

(* Every id a dependents map can ever contribute: the measure `grow` terminates by. *)
let rec range (d:dmap) : Tot (list string) =
  match d with
  | [] -> []
  | (_, vs) :: t -> app vs (range t)

let rec dependents_in_range (d:dmap) (nd x:string)
  : Lemma (requires mem x (dependents_of d nd)) (ensures mem x (range d)) =
  match d with
  | [] -> ()
  | (k, vs) :: t ->
    mem_app x vs (range t);
    if nd = k then () else dependents_in_range t nd x

let rec any_dep_in_range (d:dmap) (frontier:list string) (x:string)
  : Lemma (requires any_dep d frontier x) (ensures mem x (range d)) =
  match frontier with
  | [] -> ()
  | nd :: t ->
    if mem x (dependents_of d nd) then dependents_in_range d nd x else any_dep_in_range d t x

(* WHY THE LOOP STOPS: a round that finds nothing fresh hands on an empty frontier, and a round
   that finds something strictly shrinks the ids of the map not yet accumulated. *)
let grow_measure (d:dmap) (frontier acc:list string)
  : Lemma (ensures (let fresh = diff (next_of d frontier []) acc in
                    (Nil? fresh ==> union acc fresh == acc) /\
                    (Cons? fresh ==> len (diff (range d) (union acc fresh)) < len (diff (range d) acc)))) =
  let next = next_of d frontier [] in
  let fresh = diff next acc in
  match fresh with
  | [] -> app_nil acc
  | x :: _ ->
    mem_diff x next acc;
    next_mem d frontier [] x;
    any_dep_in_range d frontier x;
    let aux (y:string) : Lemma (mem y acc ==> mem y (union acc fresh)) = mem_union y acc fresh in
    FStar.Classical.forall_intro aux;
    mem_union x acc fresh;
    diff_shrink (range d) acc (union acc fresh) x

(* F#: `dirtySet`'s local `grow`. *)
let rec grow (d:dmap) (frontier acc:list string)
  : Tot (list string) (decreases %[len (diff (range d) acc); len frontier]) =
  match frontier with
  | [] -> acc
  | _ :: _ ->
    let next = next_of d frontier [] in
    let fresh = diff next acc in
    grow_measure d frontier acc;
    grow d fresh (union acc fresh)

(* F#: `CapabilityPipeline.dirtySet` — `changed` and every node downstream of it. *)
let dirty_set (changed:list string) (p:pipeline) : Tot (list string) =
  grow (dependents (deps_of p.p_nodes)) changed changed

(* A set closed under "reads": whoever reads a member is a member. *)
let closed (deps:dmap) (s:list string) : Tot prop =
  forall (n r:string). mem r s /\ edge deps n r ==> mem n s

let closed_except (deps:dmap) (acc frontier:list string) : Tot prop =
  forall (n r:string). mem r acc /\ not (mem r frontier) /\ edge deps n r ==> mem n acc

let grow_step_closed (deps:dmap) (frontier acc:list string) (n r:string)
  : Lemma (requires closed_except deps acc frontier)
          (ensures (let fresh = diff (next_of (dependents deps) frontier []) acc in
                    mem r (union acc fresh) /\ not (mem r fresh) /\ edge deps n r ==> mem n (union acc fresh))) =
  let d = dependents deps in
  let next = next_of d frontier [] in
  let fresh = diff next acc in
  mem_union r acc fresh;
  mem_union n acc fresh;
  mem_diff n next acc;
  next_mem d frontier [] n;
  dependents_edge deps r n;
  if mem r frontier && edge deps n r then any_dep_intro d frontier r n else ()

let rec grow_closed (deps:dmap) (frontier acc:list string)
  : Lemma (requires closed_except deps acc frontier)
          (ensures closed deps (grow (dependents deps) frontier acc) /\
                   (forall (x:string). mem x acc ==> mem x (grow (dependents deps) frontier acc)))
          (decreases %[len (diff (range (dependents deps)) acc); len frontier]) =
  match frontier with
  | [] -> ()
  | _ :: _ ->
    let d = dependents deps in
    let fresh = diff (next_of d frontier []) acc in
    grow_measure d frontier acc;
    FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 (grow_step_closed deps frontier acc));
    let aux (x:string) : Lemma (mem x acc ==> mem x (union acc fresh)) = mem_union x acc fresh in
    FStar.Classical.forall_intro aux;
    grow_closed deps fresh (union acc fresh)

(* The dirty set CONTAINS the change and is CLOSED downstream: a node with an edge from a dirty
   node is dirty. *)
let dirty_closed (changed:list string) (p:pipeline)
  : Lemma (subset changed (dirty_set changed p) /\ closed (deps_of p.p_nodes) (dirty_set changed p)) =
  grow_closed (deps_of p.p_nodes) changed changed;
  subset_intro changed (dirty_set changed p)

(* F#: `evalFrom`'s local `go` — a node that is not dirty and has a prior value reuses it
   (`not (Set.contains nid dirty) && Map.containsKey nid prior`, then `Map.find nid prior`); every
   other node runs. *)
let rec eval_from_go (#v:Type) (rd:readers) (lk:capability_lookup) (spell:v -> string) (body:node_body v)
                     (prior:list (string & v)) (dirty:list string)
                     (results:list (string & v)) (ns:list pipeline_node)
  : Tot (outcome (list (string & v)) pipeline_eval_error) (decreases ns) =
  match ns with
  | [] -> Ok results
  | n :: rest ->
    let nid = node_id n in
    match (if mem nid dirty then None else assoc nid prior) with
    | Some x -> eval_from_go rd lk spell body prior dirty ((nid, x) :: results) rest
    | None ->
      match run_node rd lk spell body results n with
      | Error e -> Error e
      | Ok x -> eval_from_go rd lk spell body prior dirty ((nid, x) :: results) rest

(* F#: `CapabilityPipeline.evalFrom` — it type-checks first, exactly as `eval` does. *)
let eval_from (#v:Type) (fr:feed_readers) (rd:readers) (lk:capability_lookup) (spell:v -> string)
              (body:node_body v) (prior:list (string & v)) (changed:list string) (p:pipeline)
  : Tot (outcome (list (string & v)) pipeline_eval_error) =
  match type_check fr rd lk p with
  | Error e -> Error (EvalIllTyped e)
  | Ok () -> eval_from_go rd lk spell body prior (dirty_set changed p) [] p.p_nodes

(* ---- what `typeCheck` accepts ---- *)

(* DECLARATION ORDER IS A TOPOLOGICAL ORDER of the `FromNode` edges: every node's id is new, and
   every edge it carries names a node declared strictly EARLIER. *)
let rec ordered (earlier:list string) (ns:list pipeline_node) : Tot bool (decreases ns) =
  match ns with
  | [] -> true
  | n :: rest ->
    not (mem (node_id n) earlier) && subset (upstreams n) earlier &&
    ordered (app earlier [node_id n]) rest

let rec first_dup_distinct (l:list string)
  : Lemma (first_dup l == None <==> distinct l)
  = match l with
    | [] -> ()
    | _ :: t -> first_dup_distinct t

let rec index_app (k:string) (a b:list string)
  : Lemma (index_of k (app a b) == (if mem k a then index_of k a else len a + index_of k b))
  = match a with
    | [] -> ()
    | _ :: t -> index_app k t b

let rec index_lt_len (k:string) (a:list string)
  : Lemma (mem k a ==> index_of k a < len a)
  = match a with
    | [] -> ()
    | _ :: t -> index_lt_len k t

let rec node_ids_app (a b:list pipeline_node)
  : Lemma (node_ids (app a b) == app (node_ids a) (node_ids b))
  = match a with
    | [] -> ()
    | _ :: t -> node_ids_app t b

let rec distinct_app_mid (a:list string) (x:string) (b:list string)
  : Lemma (requires distinct (app a (x :: b))) (ensures not (mem x a))
  = match a with
    | [] -> ()
    | h :: t -> mem_app h t (x :: b); distinct_app_mid t x b

let rec app_snoc (#a:Type) (l:list a) (x:a) (m:list a)
  : Lemma (app (app l [x]) m == app l (x :: m))
  = match l with
    | [] -> ()
    | _ :: t -> app_snoc t x m

(* An accepted argument list's edges all name ids declared before the node. *)
let rec args_upstreams_earlier (fr:feed_readers) (rd:readers) (ns:list pipeline_node)
                               (cap:capability) (declared:list string) (nid:string)
                               (a:list (string & arg_source)) (pre_ids post_ids:list string)
  : Lemma (requires not (mem nid pre_ids) /\
                    try_pick (pipe_arg_fault fr rd ns (app pre_ids (nid :: post_ids)) cap declared nid) a == None)
          (ensures subset (upstreams_of a) pre_ids)
          (decreases a)
  = match a with
    | [] -> ()
    | (_, src) :: rest ->
      (match src with
       | Literal _ -> ()
       | FromNode up ->
         index_app up pre_ids (nid :: post_ids);
         index_app nid pre_ids (nid :: post_ids);
         index_lt_len up pre_ids);
      args_upstreams_earlier fr rd ns cap declared nid rest pre_ids post_ids

let rec go_check_ordered (fr:feed_readers) (rd:readers) (lk:capability_lookup)
                         (all pre rest:list pipeline_node)
  : Lemma (requires all == app pre rest /\ distinct (node_ids all) /\
                    go_check fr rd lk all (node_ids all) rest == Ok ())
          (ensures ordered (node_ids pre) rest)
          (decreases rest)
  = match rest with
    | [] -> ()
    | n :: rest' ->
      node_ids_app pre rest;
      distinct_app_mid (node_ids pre) (node_id n) (node_ids rest');
      (match n with
       | Source _ _ _ -> ()
       | Invoke nid cap_id _ a ->
         (match lk.lk_find cap_id with
          | Some cap ->
            args_upstreams_earlier fr rd all cap (entry_addrs cap.c_signature.sg_holes) nid a
              (node_ids pre) (node_ids rest')
          | None -> ()));
      app_snoc pre n rest';
      node_ids_app pre [n];
      go_check_ordered fr rd lk all (app pre [n]) rest'

(* An accepted pipeline is in topological order. *)
let typecheck_ordered (fr:feed_readers) (rd:readers) (lk:capability_lookup) (p:pipeline)
  : Lemma (requires type_check fr rd lk p == Ok ())
          (ensures ordered [] p.p_nodes /\ distinct (node_ids p.p_nodes))
  = first_dup_distinct (node_ids p.p_nodes);
    go_check_ordered fr rd lk p.p_nodes [] p.p_nodes

(* Two pipelines hold the same nodes. *)
let same_nodes (ns ns':list pipeline_node) : Tot prop =
  forall (n:pipeline_node). memp n ns <==> memp n ns'

let rec go_check_all_none (fr:feed_readers) (rd:readers) (lk:capability_lookup) (ns:list pipeline_node)
                          (all_ids:list string) (rest:list pipeline_node) (n:pipeline_node)
  : Lemma (requires go_check fr rd lk ns all_ids rest == Ok () /\ memp n rest)
          (ensures node_fault fr rd lk ns all_ids n == None)
          (decreases rest)
  = match rest with
    | [] -> ()
    | m :: t ->
      FStar.Classical.or_elim #(n == m) #(memp n t) #(fun _ -> node_fault fr rd lk ns all_ids n == None)
        (fun _ -> ())
        (fun _ -> go_check_all_none fr rd lk ns all_ids t n)

let rec find_node_some (id:string) (ns:list pipeline_node)
  : Lemma (match find_node id ns with
           | Some n -> memp n ns /\ node_id n == id
           | None -> True)
  = match ns with
    | [] -> ()
    | _ :: t -> find_node_some id t

let rec memp_node_id (n:pipeline_node) (ns:list pipeline_node)
  : Lemma (memp n ns ==> mem (node_id n) (node_ids ns))
  = match ns with
    | [] -> ()
    | _ :: t -> memp_node_id n t

(* Over distinct ids a member is the one node its id finds. *)
let rec distinct_find_node (n:pipeline_node) (ns:list pipeline_node)
  : Lemma ((memp n ns /\ distinct (node_ids ns)) ==> find_node (node_id n) ns == Some n)
  = match ns with
    | [] -> ()
    | _ :: t -> memp_node_id n t; distinct_find_node n t

(* An argument list accepted in one declaration order is accepted in another that also puts its
   upstreams earlier: with the position test out of the way, every clause reads the node SET. *)
let rec args_transfer (fr:feed_readers) (rd:readers) (ns ns':list pipeline_node)
                      (cap:capability) (declared:list string) (nid:string)
                      (a:list (string & arg_source)) (pre_ids post_ids:list string)
  : Lemma (requires node_ids ns' == app pre_ids (nid :: post_ids) /\ not (mem nid pre_ids) /\
                    distinct (node_ids ns') /\ same_nodes ns ns' /\
                    subset (upstreams_of a) pre_ids /\
                    try_pick (pipe_arg_fault fr rd ns (node_ids ns) cap declared nid) a == None)
          (ensures try_pick (pipe_arg_fault fr rd ns' (node_ids ns') cap declared nid) a == None)
          (decreases a)
  = match a with
    | [] -> ()
    | (_, src) :: rest ->
      (match src with
       | Literal _ -> ()
       | FromNode up ->
         find_node_some up ns;
         (match find_node up ns with
          | Some un -> distinct_find_node un ns'
          | None -> ());
         index_app up pre_ids (nid :: post_ids);
         index_app nid pre_ids (nid :: post_ids);
         index_lt_len up pre_ids);
      args_transfer fr rd ns ns' cap declared nid rest pre_ids post_ids

let rec go_check_transfer (fr:feed_readers) (rd:readers) (lk:capability_lookup)
                          (ns ns' pre rest:list pipeline_node)
  : Lemma (requires ns' == app pre rest /\ distinct (node_ids ns') /\ same_nodes ns ns' /\
                    ordered (node_ids pre) rest /\
                    (forall (n:pipeline_node). memp n rest ==> node_fault fr rd lk ns (node_ids ns) n == None))
          (ensures go_check fr rd lk ns' (node_ids ns') rest == Ok ())
          (decreases rest)
  = match rest with
    | [] -> ()
    | n :: rest' ->
      node_ids_app pre rest;
      assert (memp n rest);
      (match n with
       | Source _ _ _ -> ()
       | Invoke nid cap_id _ a ->
         (match lk.lk_find cap_id with
          | Some cap ->
            args_transfer fr rd ns ns' cap (entry_addrs cap.c_signature.sg_holes) nid a
              (node_ids pre) (node_ids rest')
          | None -> ()));
      app_snoc pre n rest';
      node_ids_app pre [n];
      go_check_transfer fr rd lk ns ns' (app pre [n]) rest'

let rec ordered_distinct (earlier:list string) (ns:list pipeline_node)
  : Lemma (requires ordered earlier ns)
          (ensures distinct (node_ids ns) /\ (forall (x:string). mem x (node_ids ns) ==> not (mem x earlier)))
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: rest ->
      ordered_distinct (app earlier [node_id n]) rest;
      let aux (x:string) : Lemma (mem x (app earlier [node_id n]) == (mem x earlier || x = node_id n)) =
        mem_app x earlier [node_id n]
      in
      FStar.Classical.forall_intro aux

(* The verdict does not depend on WHICH topological order was declared: a pipeline `typeCheck`
   accepts is accepted in every order of the same nodes that puts each edge's upstream first. *)
let typecheck_order_independent (fr:feed_readers) (rd:readers) (lk:capability_lookup) (p p':pipeline)
  : Lemma (requires same_nodes p.p_nodes p'.p_nodes /\ type_check fr rd lk p == Ok () /\
                    ordered [] p'.p_nodes)
          (ensures type_check fr rd lk p' == Ok ())
  = first_dup_distinct (node_ids p.p_nodes);
    ordered_distinct [] p'.p_nodes;
    first_dup_distinct (node_ids p'.p_nodes);
    FStar.Classical.forall_intro
      (FStar.Classical.move_requires
         (go_check_all_none fr rd lk p.p_nodes (node_ids p.p_nodes) p.p_nodes));
    go_check_transfer fr rd lk p.p_nodes p'.p_nodes [] p'.p_nodes

(* THE TWELFTH THEOREM (Phase 354), `typecheck_topological`. F#: `CapabilityPipeline.typeCheck`.
   The check walks the nodes in DECLARATION order and accepts only where that order is a
   topological order of the `FromNode` edges: ids distinct, every edge naming a node declared
   strictly earlier — so an accepted pipeline is acyclic and each upstream has run before the node
   it feeds. And the verdict is a fact about the node SET: of two declarations of the same nodes,
   each in a topological order, `typeCheck` accepts both or refuses both. (A declaration that is
   NOT in topological order is refused whatever its nodes are — `PipelineCycle` or
   `PipelineForwardEdge` — which is the first half read backwards.) *)
let typecheck_topological (fr:feed_readers) (rd:readers) (lk:capability_lookup) (p p':pipeline)
  : Lemma ((type_check fr rd lk p == Ok () ==>
              (ordered [] p.p_nodes /\ distinct (node_ids p.p_nodes))) /\
           ((same_nodes p.p_nodes p'.p_nodes /\ ordered [] p.p_nodes /\ ordered [] p'.p_nodes) ==>
              (Ok? (type_check fr rd lk p) == Ok? (type_check fr rd lk p'))))
  = FStar.Classical.move_requires (typecheck_ordered fr rd lk) p;
    FStar.Classical.move_requires (typecheck_order_independent fr rd lk p) p';
    FStar.Classical.move_requires (typecheck_order_independent fr rd lk p') p

(* ---- what the evaluators compute ---- *)

(* A REFUSED PIPELINE RUNS NO BODY. F#: `eval` and `evalFrom` over a pipeline `typeCheck` refuses.
   Both answer `EvalIllTyped` carrying that refusal, and the answer is the same under EVERY host
   body, prior evaluation and change set — which is what "never reached" means for a pure
   function, as `unregistered_refused` says it of a dispatch. *)
let pipeline_refused_never_evaluated (#v:Type) (fr:feed_readers) (rd:readers) (lk:capability_lookup)
                                     (spell:v -> string) (body body':node_body v)
                                     (prior:list (string & v)) (changed:list string) (p:pipeline)
                                     (e:pipeline_error)
  : Lemma (requires type_check fr rd lk p == Error e)
          (ensures eval fr rd lk spell body p == Error (EvalIllTyped e) /\
                   eval_from fr rd lk spell body prior changed p == Error (EvalIllTyped e) /\
                   eval fr rd lk spell body p == eval fr rd lk spell body' p /\
                   eval_from fr rd lk spell body prior changed p ==
                     eval_from fr rd lk spell body' prior changed p)
  = ()

(* With nothing to reuse, the incremental walk IS the reference walk. *)
let rec eval_from_empty_prior (#v:Type) (rd:readers) (lk:capability_lookup) (spell:v -> string)
                              (body:node_body v) (dirty:list string)
                              (results:list (string & v)) (ns:list pipeline_node)
  : Lemma (ensures eval_from_go rd lk spell body [] dirty results ns == eval_go rd lk spell body results ns)
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: rest ->
      (match run_node rd lk spell body results n with
       | Error _ -> ()
       | Ok x -> eval_from_empty_prior rd lk spell body dirty ((node_id n, x) :: results) rest)

(* `tryFind` on the address, then its space, is what `tryPick` finds — where the first entry at the
   address carries a space. *)
let rec space_of_find (addr:string) (holes:list sig_entry)
  : Lemma (match find_entry addr holes with
           | Some h -> (Some? h.s_space ==> space_of addr holes == h.s_space)
           | None -> True)
  = match holes with
    | [] -> ()
    | h :: t -> if h.s_addr = addr then () else space_of_find addr t

(* Over an accepted argument list whose upstreams all have values, resolution never answers the
   evaluator's two type-check refusals. *)
let rec resolve_never_ill (#v:Type) (fr:feed_readers) (rd:readers) (lk:capability_lookup)
                          (spell:v -> string) (ns:list pipeline_node) (all_ids:list string)
                          (cap:capability) (declared:list string)
                          (results:list (string & v)) (nid cap_id:string)
                          (a:list (string & arg_source)) (acc:list (string & pipeline_arg v))
  : Lemma (requires lk.lk_find cap_id == Some cap /\
                    try_pick (pipe_arg_fault fr rd ns all_ids cap declared nid) a == None /\
                    (forall (up:string). mem up (upstreams_of a) ==> Some? (assoc up results)))
          (ensures (match resolve_args rd lk spell results nid cap_id a acc with
                    | Error (EvalIllTyped _) -> False
                    | _ -> True))
          (decreases a)
  = match a with
    | [] -> ()
    | (addr, src) :: rest ->
      (match src with
       | Literal s ->
         resolve_never_ill fr rd lk spell ns all_ids cap declared results nid cap_id rest
           ((addr, LiteralArg s) :: acc)
       | FromNode up ->
         space_of_find addr cap.c_signature.sg_holes;
         (match assoc up results with
          | None -> ()
          | Some x ->
            resolve_never_ill fr rd lk spell ns all_ids cap declared results nid cap_id rest
              ((addr, FromUpstream x) :: acc)))

let run_never_ill (#v:Type) (fr:feed_readers) (rd:readers) (lk:capability_lookup)
                  (spell:v -> string) (body:node_body v) (ns:list pipeline_node) (all_ids:list string)
                  (results:list (string & v)) (n:pipeline_node)
  : Lemma (requires node_fault fr rd lk ns all_ids n == None /\
                    (forall (up:string). mem up (upstreams n) ==> Some? (assoc up results)))
          (ensures (match run_node rd lk spell body results n with
                    | Error (EvalIllTyped _) -> False
                    | _ -> True))
  = match n with
    | Source _ _ _ -> ()
    | Invoke nid cap_id _ a ->
      (match lk.lk_find cap_id with
       | Some cap ->
         resolve_never_ill fr rd lk spell ns all_ids cap (entry_addrs cap.c_signature.sg_holes)
           results nid cap_id a []
       | None -> ())

let rec go_never_ill (#v:Type) (fr:feed_readers) (rd:readers) (lk:capability_lookup)
                     (spell:v -> string) (body:node_body v) (all:list pipeline_node)
                     (prior:list (string & v)) (dirty earlier:list string)
                     (results:list (string & v)) (rest:list pipeline_node)
  : Lemma (requires (forall (n:pipeline_node). memp n rest ==>
                       node_fault fr rd lk all (node_ids all) n == None) /\
                    ordered earlier rest /\
                    (forall (k:string). mem k earlier ==> Some? (assoc k results)))
          (ensures (match eval_from_go rd lk spell body prior dirty results rest with
                    | Error (EvalIllTyped _) -> False
                    | _ -> True))
          (decreases rest)
  = match rest with
    | [] -> ()
    | n :: rest' ->
      let nid = node_id n in
      assert (memp n rest);
      let ups (up:string) : Lemma (mem up (upstreams n) ==> Some? (assoc up results)) =
        if mem up (upstreams n) then subset_mem (upstreams n) earlier up else ()
      in
      FStar.Classical.forall_intro ups;
      run_never_ill fr rd lk spell body all (node_ids all) results n;
      let step (x:v) : Lemma (match eval_from_go rd lk spell body prior dirty ((nid, x) :: results) rest' with
                              | Error (EvalIllTyped _) -> False
                              | _ -> True) =
        let aux (k:string) : Lemma (mem k (app earlier [nid]) ==> Some? (assoc k ((nid, x) :: results))) =
          mem_app k earlier [nid]
        in
        FStar.Classical.forall_intro aux;
        go_never_ill fr rd lk spell body all prior dirty (app earlier [nid]) ((nid, x) :: results) rest'
      in
      FStar.Classical.forall_intro step

(* THE THIRTEENTH THEOREM (Phase 354), `pipeline_illtyped_iff_refused`. F#: `eval` and `evalFrom`.
   `EvalIllTyped` is the type check's refusal and nothing else: over a pipeline `typeCheck`
   ACCEPTS, neither evaluator answers it — the two arms `runNode` marks unreachable (an edge whose
   upstream has no value yet, an edge into a hole with no space) are unreachable — so what remains
   is a result, a host body's failure (`EvalNodeFailed`) or an upstream value outside the space of
   the hole it feeds (`EvalArgRefused`). With `pipeline_refused_never_evaluated` this is an
   equivalence: an evaluator answers `EvalIllTyped e` exactly when `typeCheck` refuses with `e`. *)
let pipeline_illtyped_iff_refused (#v:Type) (fr:feed_readers) (rd:readers) (lk:capability_lookup)
                                  (spell:v -> string) (body:node_body v)
                                  (prior:list (string & v)) (changed:list string) (p:pipeline)
  : Lemma (requires type_check fr rd lk p == Ok ())
          (ensures (match eval fr rd lk spell body p with
                    | Error (EvalIllTyped _) -> False
                    | _ -> True) /\
                   (match eval_from fr rd lk spell body prior changed p with
                    | Error (EvalIllTyped _) -> False
                    | _ -> True))
  = typecheck_ordered fr rd lk p;
    FStar.Classical.forall_intro
      (FStar.Classical.move_requires
         (go_check_all_none fr rd lk p.p_nodes (node_ids p.p_nodes) p.p_nodes));
    go_never_ill fr rd lk spell body p.p_nodes prior (dirty_set changed p) [] [] p.p_nodes;
    go_never_ill fr rd lk spell body p.p_nodes [] [] [] [] p.p_nodes;
    eval_from_empty_prior rd lk spell body [] [] p.p_nodes

(* A finished walk keeps every value it was handed for an id it did not declare. *)
let rec eval_go_keeps (#v:Type) (rd:readers) (lk:capability_lookup) (spell:v -> string) (body:node_body v)
                      (results:list (string & v)) (ns:list pipeline_node) (k:string)
  : Lemma (requires not (mem k (node_ids ns)))
          (ensures (match eval_go rd lk spell body results ns with
                    | Ok final -> assoc k final == assoc k results
                    | Error _ -> True))
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: rest ->
      (match run_node rd lk spell body results n with
       | Error _ -> ()
       | Ok x -> eval_go_keeps rd lk spell body ((node_id n, x) :: results) rest k)

(* Resolution reads the result map only at the argument list's upstreams. *)
let rec resolve_eq (#v:Type) (rd:readers) (lk:capability_lookup) (spell:v -> string)
                   (r0 r1:list (string & v)) (nid cap_id:string)
                   (a:list (string & arg_source)) (acc:list (string & pipeline_arg v))
  : Lemma (requires forall (up:string). mem up (upstreams_of a) ==> assoc up r0 == assoc up r1)
          (ensures resolve_args rd lk spell r0 nid cap_id a acc == resolve_args rd lk spell r1 nid cap_id a acc)
          (decreases a)
  = match a with
    | [] -> ()
    | (addr, src) :: rest ->
      (match src with
       | Literal s -> resolve_eq rd lk spell r0 r1 nid cap_id rest ((addr, LiteralArg s) :: acc)
       | FromNode up ->
         (match assoc up r0 with
          | None -> ()
          | Some x -> resolve_eq rd lk spell r0 r1 nid cap_id rest ((addr, FromUpstream x) :: acc)))

(* Two host bodies that agree off the change set: what "only these inputs changed" means. *)
let agree_off (#v:Type) (body0 body1:node_body v) (changed:list string) : Tot prop =
  forall (n:pipeline_node) (a:list (string & pipeline_arg v)).
    not (mem (node_id n) changed) ==> body0 n a == body1 n a

(* A node run against two result maps that agree at its upstreams, by two bodies that agree at the
   node, answers the same. *)
let run_node_eq (#v:Type) (rd:readers) (lk:capability_lookup) (spell:v -> string)
                (body0 body1:node_body v) (r0 r1:list (string & v)) (n:pipeline_node)
  : Lemma (requires (forall (up:string). mem up (upstreams n) ==> assoc up r0 == assoc up r1) /\
                    (forall (a:list (string & pipeline_arg v)). body0 n a == body1 n a))
          (ensures run_node rd lk spell body0 r0 n == run_node rd lk spell body1 r1 n)
  = match n with
    | Source _ _ _ -> assert (body0 n [] == body1 n [])
    | Invoke nid cap_id _ a ->
      resolve_eq rd lk spell r0 r1 nid cap_id a [];
      (match resolve_args rd lk spell r0 nid cap_id a [] with
       | Ok xs -> assert (body0 n (rev xs) == body1 n (rev xs))
       | Error _ -> ())

let rec deps_edge (ns:list pipeline_node) (n:pipeline_node) (up:string)
  : Lemma (requires memp n ns /\ mem up (upstreams n))
          (ensures edge (deps_of ns) (node_id n) up)
  = match ns with
    | [] -> ()
    | m :: t ->
      FStar.Classical.or_elim #(n == m) #(memp n t) #(fun _ -> edge (deps_of ns) (node_id n) up)
        (fun _ -> ())
        (fun _ -> deps_edge t n up)

(* THE INDUCTION. `r0` is the prior evaluation's map so far and `r1` the new one's; they agree at
   every CLEAN id, the prior evaluation finishes as `prior` from here, and from here the
   incremental walk and the reference walk under the new body are the same walk. *)
let rec go_agree (#v:Type) (rd:readers) (lk:capability_lookup) (spell:v -> string)
                 (body0 body1:node_body v) (all:list pipeline_node) (changed dirty:list string)
                 (prior r0 r1:list (string & v)) (rest:list pipeline_node)
  : Lemma (requires (forall (n:pipeline_node). memp n rest ==> memp n all) /\
                    distinct (node_ids rest) /\
                    closed (deps_of all) dirty /\ subset changed dirty /\
                    agree_off body0 body1 changed /\
                    (forall (k:string). not (mem k dirty) ==> assoc k r1 == assoc k r0) /\
                    eval_go rd lk spell body0 r0 rest == Ok prior)
          (ensures eval_from_go rd lk spell body1 prior dirty r1 rest == eval_go rd lk spell body1 r1 rest)
          (decreases rest)
  = match rest with
    | [] -> ()
    | n :: rest' ->
      let nid = node_id n in
      assert (memp n rest);
      (match run_node rd lk spell body0 r0 n with
       | Error _ -> ()
       | Ok v0 ->
         eval_go_keeps rd lk spell body0 ((nid, v0) :: r0) rest' nid;
         assert (assoc nid prior == Some v0);
         if mem nid dirty then
           (match run_node rd lk spell body1 r1 n with
            | Error _ -> ()
            | Ok v1 ->
              go_agree rd lk spell body0 body1 all changed dirty prior ((nid, v0) :: r0) ((nid, v1) :: r1) rest')
         else begin
           (if mem nid changed then subset_mem changed dirty nid else ());
           let ups (up:string) : Lemma (mem up (upstreams n) ==> assoc up r0 == assoc up r1) =
             if mem up (upstreams n) then deps_edge all n up else ()
           in
           FStar.Classical.forall_intro ups;
           run_node_eq rd lk spell body0 body1 r0 r1 n;
           go_agree rd lk spell body0 body1 all changed dirty prior ((nid, v0) :: r0) ((nid, v0) :: r1) rest'
         end)

(* THE FOURTEENTH THEOREM (Phase 354), `pipeline_evalfrom_agrees`. F#: `evalFrom` against `eval`.
   Let `prior` be what `eval` answered under a body `body0`, and let `body1` agree with `body0` at
   every node outside `changed`. Then re-evaluating incrementally — `evalFrom` under `body1`, from
   `prior` and `changed` — answers EXACTLY what a full `eval` under `body1` answers: the same result
   map, so at every node the value the whole evaluation assigns it, and the same refusal when a
   re-run node fails. Reuse is therefore sound: a node `evalFrom` does not re-run would have been
   handed the same arguments and answered the same value. The prior evaluation having succeeded is
   the type check having accepted, so nothing else is assumed of the pipeline; the contract on the
   bodies is `agree_off`, and without it the claim is false — a body that changes at a node the
   caller did not name is a stale reuse. *)
let pipeline_evalfrom_agrees (#v:Type) (fr:feed_readers) (rd:readers) (lk:capability_lookup)
                             (spell:v -> string) (body0 body1:node_body v)
                             (prior:list (string & v)) (changed:list string) (p:pipeline)
  : Lemma (requires eval fr rd lk spell body0 p == Ok prior /\ agree_off body0 body1 changed)
          (ensures eval_from fr rd lk spell body1 prior changed p == eval fr rd lk spell body1 p /\
                   (forall (k:string) (final:list (string & v)).
                      eval fr rd lk spell body1 p == Ok final ==>
                      (match eval_from fr rd lk spell body1 prior changed p with
                       | Ok inc -> assoc k inc == assoc k final
                       | Error _ -> False)))
  = match type_check fr rd lk p with
    | Error _ -> ()
    | Ok () ->
      first_dup_distinct (node_ids p.p_nodes);
      dirty_closed changed p;
      go_agree rd lk spell body0 body1 p.p_nodes changed (dirty_set changed p) prior [] [] p.p_nodes

(* ======================================================================================
   16. THE CODECS (Phase 354) — `CapabilityCodec` for a `Signature` and a `Capability`, and
       `CapabilityPipeline`'s for a `PipelineNode` and a pipeline, with the `SpaceCodec` and
       `EffectCodec` they share, clause for clause AT THE `JVal`: the writers, the lenient readers
       through the typed decode layer, and the refusal each reader raises as its code and path.
       Then what they compute: decode after encode, EXACTLY, for every value — the identity on the
       well-formed ones, a named refusal or a normal form on the others — and every document a
       reader accepts read as a well-formed value.

       The bytes are not here. `Canon.render` and `Json.parse` are `WireCanon.fst`'s and
       `JsonParse.fst`'s; what crosses is the `JVal` between them. A refusal's SENTENCE and its
       `Expected` phrase are not modelled: a refusal is its `DecodeCode` and its path.
   ====================================================================================== *)

(* F#: `JVal`. A `JFloat`'s payload is an opaque carrier, as a `FloatRange`'s bounds are. *)
type jval =
  | JStr   : string -> jval
  | JInt   : int -> jval
  | JBool  : bool -> jval
  | JFloat : string -> jval
  | JArr   : list jval -> jval
  | JObj   : list (string & jval) -> jval

(* F#: `PathSegment`. *)
type path_seg =
  | Key   : string -> path_seg
  | Index : nat -> path_seg

(* F#: `DecodeCode` — the closed code set a decode refusal carries. *)
type decode_code =
  | InvalidJson
  | MissingField
  | WrongKind
  | UnknownTag
  | OutOfRange
  | UndeclaredMember
  | LimitExceeded
  | NotAdmitted
  | SchemaFault

(* F#: `DecodeError`, as its `Code` and `Path`. *)
type decode_error = { d_code: decode_code; d_path: list path_seg }

(* F#: `Decoder<'T>`. *)
type decoder (a:Type) = jval -> outcome a decode_error

(* F#: `DecodeError.make` — a refusal at the value itself. *)
let refuse (c:decode_code) : Tot decode_error = { d_code = c; d_path = [] }

(* F#: `DecodeError.under` — the same refusal one step further from the root. *)
let under (s:path_seg) (e:decode_error) : Tot decode_error = { e with d_path = s :: e.d_path }

(* The one float read the codecs make: `float i`, the carrier of an integer token read as a
   number (`Decoder.float` on a `JInt`). A parameter, as every float operation here is. *)
noeq type codec_readers = { float_of_int: int -> string }

(* F#: `Decoder.tryMember` — the FIRST member of the key, `None` for a non-object. *)
let member (name:string) (el:jval) : Tot (option jval) =
  match el with
  | JObj fields -> assoc name fields
  | _ -> None

(* F#: `Decoder.str`. *)
let d_str (el:jval) : Tot (outcome string decode_error) =
  match el with
  | JStr s -> Ok s
  | _ -> Error (refuse WrongKind)

(* F#: `Decoder.int`. *)
let d_int (el:jval) : Tot (outcome int decode_error) =
  match el with
  | JInt i -> Ok i
  | _ -> Error (refuse WrongKind)

(* F#: `Decoder.bool`. *)
let d_bool (el:jval) : Tot (outcome bool decode_error) =
  match el with
  | JBool b -> Ok b
  | _ -> Error (refuse WrongKind)

(* F#: `Decoder.float` — a number, whichever constructor the parser chose. *)
let d_float (cr:codec_readers) (el:jval) : Tot (outcome string decode_error) =
  match el with
  | JFloat f -> Ok f
  | JInt i -> Ok (cr.float_of_int i)
  | _ -> Error (refuse WrongKind)

(* F#: `Decoder.field` — absent is `MissingField` naming the member, a non-object `WrongKind`. *)
let field (#a:Type) (name:string) (d:decoder a) (el:jval) : Tot (outcome a decode_error) =
  match el with
  | JObj fields ->
    (match assoc name fields with
     | Some x ->
       (match d x with
        | Ok y -> Ok y
        | Error e -> Error (under (Key name) e))
     | None -> Error ({ d_code = MissingField; d_path = [Key name] }))
  | _ -> Error (refuse WrongKind)

(* F#: `Decoder.optField` — absent is `Ok None`, present-and-refused is the refusal. *)
let opt_field (#a:Type) (name:string) (d:decoder a) (el:jval) : Tot (outcome (option a) decode_error) =
  match el with
  | JObj fields ->
    (match assoc name fields with
     | Some x ->
       (match d x with
        | Ok y -> Ok (Some y)
        | Error e -> Error (under (Key name) e))
     | None -> Ok None)
  | _ -> Error (refuse WrongKind)

(* F#: `Decoder.mapListIndexed`'s `go` — every item decoded, the first refusal under its index.
   Written as the direct recursion rather than with production's reversed accumulator: the same
   first refusal and the same list. *)
let rec list_from (#a:Type) (d:decoder a) (i:nat) (xs:list jval)
  : Tot (outcome (list a) decode_error) (decreases xs) =
  match xs with
  | [] -> Ok []
  | x :: rest ->
    match d x with
    | Error e -> Error (under (Index i) e)
    | Ok y ->
      match list_from d (i + 1) rest with
      | Ok ys -> Ok (y :: ys)
      | Error e -> Error e

(* F#: `Decoder.list`. *)
let d_list (#a:Type) (d:decoder a) (el:jval) : Tot (outcome (list a) decode_error) =
  match el with
  | JArr xs -> list_from d 0 xs
  | _ -> Error (refuse WrongKind)

(* F#: `xs |> List.map JStr`. *)
let rec strs (xs:list string) : Tot (list jval) =
  match xs with
  | [] -> []
  | x :: t -> JStr x :: strs t

(* ---- the value space: `SpaceCodec` ---- *)

(* F#: `SpaceCodec.toJson` — a wire DOCUMENT, discriminated by `"$type"`. *)
let space_json (s:value_space) : Tot jval =
  match s with
  | IntRange lo hi -> JObj [("$type", JStr "intRange"); ("min", JInt lo); ("max", JInt hi)]
  | FloatRange lo hi -> JObj [("$type", JStr "floatRange"); ("min", JFloat lo); ("max", JFloat hi)]
  | StringLen lo hi -> JObj [("$type", JStr "stringLen"); ("min", JInt lo); ("max", JInt hi)]
  | Enum xs -> JObj [("$type", JStr "enum"); ("values", JArr (strs xs))]
  | AnyString -> JObj [("$type", JStr "anyString")]
  | SlotTree c ->
    JObj (("$type", JStr "slotTree") ::
          (match c with
           | Some k -> [("slotKind", JStr k)]
           | None -> []))

(* F#: `SpaceCodec`'s `cases` under `dispatchOn key` — `Decoder.tagDispatch`: the discriminator read
   as a string, then the case's decoder over the SAME object; an unknown tag is `UnknownTag` at the
   discriminator. `len_lo` / `len_hi` are a string length's bound names in the spelling read. *)
let space_cases (cr:codec_readers) (key len_lo len_hi:string) (el:jval)
  : Tot (outcome value_space decode_error) =
  match field key d_str el with
  | Error e -> Error e
  | Ok t ->
    if t = "intRange" then
      (match field "min" d_int el with
       | Error e -> Error e
       | Ok lo ->
         (match field "max" d_int el with
          | Error e -> Error e
          | Ok hi -> Ok (IntRange lo hi)))
    else if t = "floatRange" then
      (match field "min" (d_float cr) el with
       | Error e -> Error e
       | Ok lo ->
         (match field "max" (d_float cr) el with
          | Error e -> Error e
          | Ok hi -> Ok (FloatRange lo hi)))
    else if t = "stringLen" then
      (match field len_lo d_int el with
       | Error e -> Error e
       | Ok lo ->
         (match field len_hi d_int el with
          | Error e -> Error e
          | Ok hi -> Ok (StringLen lo hi)))
    else if t = "enum" then
      (match field "values" (d_list d_str) el with
       | Error e -> Error e
       | Ok xs -> Ok (Enum xs))
    else if t = "anyString" then Ok AnyString
    else if t = "slotTree" then
      (match opt_field "slotKind" d_str el with
       | Error e -> Error e
       | Ok c -> Ok (SlotTree c))
    else Error (under (Key key) (refuse UnknownTag))

(* F#: `SpaceCodec.decoder` — a `"$type"` document as `toJson` writes it; an object with no
   `"$type"` and a `"kind"` in the descriptor spelling, leniently. *)
let space_of_j (cr:codec_readers) (el:jval) : Tot (outcome value_space decode_error) =
  match member "$type" el, member "kind" el with
  | None, Some _ -> space_cases cr "kind" "minLength" "maxLength" el
  | _ -> space_cases cr "$type" "min" "max" el

(* ---- the effect class: `EffectCodec` ---- *)

(* F#: `EffectCodec.hostTag`. *)
let host_tag (h:host_effect) : Tot string =
  match h with
  | Pure -> "pure"
  | ReadsHost -> "readsHost"
  | WritesHost -> "writesHost"

(* F#: `EffectCodec.hostDecoder`. *)
let host_of_j (el:jval) : Tot (outcome host_effect decode_error) =
  match d_str el with
  | Error e -> Error e
  | Ok s ->
    if s = "pure" then Ok Pure
    else if s = "readsHost" then Ok ReadsHost
    else if s = "writesHost" then Ok WritesHost
    else Error (refuse UnknownTag)

(* F#: `EffectCodec.determinismDecoder` — the canonical label only. *)
let det_of_j (el:jval) : Tot (outcome determinism_source decode_error) =
  match d_str el with
  | Error e -> Error e
  | Ok tag ->
    (match det_of_tag tag with
     | Some d -> Ok d
     | None -> Error (refuse UnknownTag))

(* F#: `EffectCodec.toJson`. *)
let effect_json (e:effect_class) : Tot jval =
  JObj [("host", JStr (host_tag e.host)); ("determinism", JStr (determinism_tag e.determinism))]

(* F#: `EffectCodec.decoder`. *)
let effect_of_j (el:jval) : Tot (outcome effect_class decode_error) =
  match field "host" host_of_j el with
  | Error e -> Error e
  | Ok h ->
    (match field "determinism" det_of_j el with
     | Error e -> Error e
     | Ok d -> Ok ({ host = h; determinism = d }))

(* ---- a signature entry and a signature: `CapabilityCodec` ---- *)

(* F#: `HoleKind.tags`. *)
let hole_tags : list string = ["value"; "slot"; "repeat"; "action"]

(* F#: `Function.derivedSlotSpace` — a slot entry carrying exactly the space its constraint derives. *)
let derived_slot_space (e:sig_entry) : Tot bool =
  e.s_kind = "slot" && e.s_space = Some (SlotTree e.s_slot)

(* F#: `entryJson`'s three conditional member lists. *)
let space_members (e:sig_entry) : Tot (list (string & jval)) =
  match e.s_space with
  | Some s -> if derived_slot_space e then [] else [("space", space_json s)]
  | None -> []

let slot_members (e:sig_entry) : Tot (list (string & jval)) =
  match e.s_slot with
  | Some k -> [("slotKind", JStr k)]
  | None -> []

let action_members (e:sig_entry) : Tot (list (string & jval)) =
  match e.s_action with
  | Some eff -> [("actionEffect", effect_json eff)]
  | None -> []

let entry_fields (e:sig_entry) : Tot (list (string & jval)) =
  ("addr", JStr e.s_addr) :: ("name", JStr e.s_name) :: ("kind", JStr e.s_kind) ::
  ("required", JBool e.s_required) ::
  app (space_members e) (app (slot_members e) (action_members e))

(* F#: `CapabilityCodec.entryJson`. *)
let entry_json (e:sig_entry) : Tot jval = JObj (entry_fields e)

(* F#: `tagged "unknown hole kind: " (HoleKind.tags …)` — a string from the closed set. *)
let kind_of_j (el:jval) : Tot (outcome string decode_error) =
  match d_str el with
  | Error e -> Error e
  | Ok s -> if mem s hole_tags then Ok s else Error (refuse UnknownTag)

(* F#: `CapabilityCodec.entryOf`. A slot entry travels without its derived space (Phase 229), so
   decoding restores it from the constraint. *)
let entry_of_j (cr:codec_readers) (el:jval) : Tot (outcome sig_entry decode_error) =
  match field "addr" d_str el with
  | Error e -> Error e
  | Ok addr ->
  match field "name" d_str el with
  | Error e -> Error e
  | Ok name ->
  match field "kind" kind_of_j el with
  | Error e -> Error e
  | Ok kind ->
  match field "required" d_bool el with
  | Error e -> Error e
  | Ok required ->
  match opt_field "space" (space_of_j cr) el with
  | Error e -> Error e
  | Ok sp ->
  match opt_field "actionEffect" effect_of_j el with
  | Error e -> Error e
  | Ok ac ->
  match opt_field "slotKind" d_str el with
  | Error e -> Error e
  | Ok slot ->
    let sp' : option value_space =
      match sp with
      | None -> if kind = "slot" then Some (SlotTree slot) else None
      | Some s -> Some s
    in
    Ok ({ s_addr = addr; s_name = name; s_kind = kind; s_space = sp'; s_slot = slot;
          s_action = ac; s_required = required })

(* F#: `sg.Holes |> List.map entryJson`. *)
let rec entries_json (es:list sig_entry) : Tot (list jval) =
  match es with
  | [] -> []
  | e :: t -> entry_json e :: entries_json t

(* F#: `CapabilityCodec.signatureJson`. *)
let signature_json (sg:signature) : Tot jval =
  JObj [("name", JStr sg.sg_name); ("effect", effect_json sg.sg_effect);
        ("holes", JArr (entries_json sg.sg_holes))]

(* F#: `CapabilityCodec.signatureOfDetailed` — since Phase 307 the reader runs the admission check
   the registries run, and a signature it refuses is `OutOfRange` at `holes`. *)
let signature_of_j (rd:readers) (cr:codec_readers) (el:jval) : Tot (outcome signature decode_error) =
  match field "name" d_str el with
  | Error e -> Error e
  | Ok name ->
  match field "effect" effect_of_j el with
  | Error e -> Error e
  | Ok eff ->
  match field "holes" (d_list (entry_of_j cr)) el with
  | Error e -> Error e
  | Ok holes ->
    let sg = { sg_name = name; sg_holes = holes; sg_effect = eff } in
    (match validate_signature rd sg with
     | None -> Ok sg
     | Some _ -> Error ({ d_code = OutOfRange; d_path = [Key "holes"] }))

(* ---- placement and the capability declaration ---- *)

(* F#: `islandTag`. *)
let island_tag (k:island_kind) : Tot string =
  match k with
  | Pyodide -> "pyodide"
  | Fable -> "fable"
  | Js -> "js"

(* F#: `islandOf`. *)
let island_of_j (el:jval) : Tot (outcome island_kind decode_error) =
  match d_str el with
  | Error e -> Error e
  | Ok s ->
    if s = "pyodide" then Ok Pyodide
    else if s = "fable" then Ok Fable
    else if s = "js" then Ok Js
    else Error (refuse UnknownTag)

(* F#: `placementJson`. *)
let placement_json (p:placement) : Tot jval =
  match p with
  | BuildTime -> JObj [("$type", JStr "buildTime")]
  | Server -> JObj [("$type", JStr "server")]
  | ClientDeclarative -> JObj [("$type", JStr "clientDeclarative")]
  | Precomputed -> JObj [("$type", JStr "precomputed")]
  | ClientIsland k -> JObj [("$type", JStr "clientIsland"); ("island", JStr (island_tag k))]

(* F#: `placementOf`. *)
let placement_of_j (el:jval) : Tot (outcome placement decode_error) =
  match field "$type" d_str el with
  | Error e -> Error e
  | Ok t ->
    if t = "buildTime" then Ok BuildTime
    else if t = "server" then Ok Server
    else if t = "clientDeclarative" then Ok ClientDeclarative
    else if t = "precomputed" then Ok Precomputed
    else if t = "clientIsland" then
      (match field "island" island_of_j el with
       | Error e -> Error e
       | Ok k -> Ok (ClientIsland k))
    else Error (under (Key "$type") (refuse UnknownTag))

(* F#: `CapabilityCodec.encodeJson`. *)
let capability_json (c:capability) : Tot jval =
  JObj [("$type", JStr "capability"); ("id", JStr c.c_id); ("signature", signature_json c.c_signature);
        ("determinism", JStr (determinism_tag c.c_determinism)); ("placement", placement_json c.c_placement)]

(* F#: `tagged "not a capability declaration: " [ "capability", () ]`. *)
let doc_tag_of_j (el:jval) : Tot (outcome unit decode_error) =
  match d_str el with
  | Error e -> Error e
  | Ok s -> if s = "capability" then Ok () else Error (refuse UnknownTag)

(* F#: the `determinism` member's check (Phase 44) — the wire label must be the one the decoded
   signature's effect derives; a disagreeing label is `OutOfRange`, never silently corrected. *)
let det_agrees_j (expected:string) (el:jval) : Tot (outcome unit decode_error) =
  match d_str el with
  | Error e -> Error e
  | Ok wire -> if wire <> expected then Error (refuse OutOfRange) else Ok ()

(* F#: `CapabilityCodec.decodeJsonDetailed`. *)
let capability_of_j (rd:readers) (cr:codec_readers) (el:jval) : Tot (outcome capability decode_error) =
  match field "$type" doc_tag_of_j el with
  | Error e -> Error e
  | Ok () ->
  match field "id" d_str el with
  | Error e -> Error e
  | Ok id ->
  match field "signature" (signature_of_j rd cr) el with
  | Error e -> Error e
  | Ok sg ->
  match field "determinism" (det_agrees_j (determinism_tag sg.sg_effect.determinism)) el with
  | Error e -> Error e
  | Ok () ->
  match field "placement" placement_of_j el with
  | Error e -> Error e
  | Ok pl ->
    Ok ({ c_id = id; c_signature = sg; c_determinism = sg.sg_effect.determinism; c_placement = pl })

(* ---- the pipeline node: `CapabilityPipeline`'s codec ---- *)

(* F#: `argSrcToJ`. *)
let arg_source_json (s:arg_source) : Tot jval =
  match s with
  | Literal x -> JObj [("$type", JStr "literal"); ("value", JStr x)]
  | FromNode n -> JObj [("$type", JStr "fromNode"); ("node", JStr n)]

(* F#: `argSrcFromJ`. *)
let arg_source_of_j (el:jval) : Tot (outcome arg_source decode_error) =
  match field "$type" d_str el with
  | Error e -> Error e
  | Ok t ->
    if t = "literal" then
      (match field "value" d_str el with
       | Error e -> Error e
       | Ok x -> Ok (Literal x))
    else if t = "fromNode" then
      (match field "node" d_str el with
       | Error e -> Error e
       | Ok n -> Ok (FromNode n))
    else Error (under (Key "$type") (refuse UnknownTag))

(* F#: `argToJ`. *)
let arg_json (b:(string & arg_source)) : Tot jval =
  JObj [("addr", JStr (fst b)); ("source", arg_source_json (snd b))]

(* F#: `argFromJ`. *)
let arg_of_j (el:jval) : Tot (outcome (string & arg_source) decode_error) =
  match field "addr" d_str el with
  | Error e -> Error e
  | Ok addr ->
    (match field "source" arg_source_of_j el with
     | Error e -> Error e
     | Ok s -> Ok (addr, s))

let rec args_json (a:list (string & arg_source)) : Tot (list jval) =
  match a with
  | [] -> []
  | b :: t -> arg_json b :: args_json t

(* F#: `spaceFromJ` — `SpaceCodec.decoder`, then (Phase 307) the admission check: an output space
   `Space.wellFormed` refuses is `OutOfRange`. *)
let out_space_of_j (rd:readers) (cr:codec_readers) (el:jval) : Tot (outcome value_space decode_error) =
  match space_of_j cr el with
  | Error e -> Error e
  | Ok sp ->
    (match space_wf rd sp with
     | None -> Ok sp
     | Some _ -> Error (refuse OutOfRange))

(* F#: `nodeToJ`. *)
let node_json (n:pipeline_node) : Tot jval =
  match n with
  | Source id dref ty ->
    JObj [("$type", JStr "source"); ("id", JStr id); ("dataRef", JStr dref); ("outputType", space_json ty)]
  | Invoke id cap_id ty a ->
    JObj [("$type", JStr "invoke"); ("id", JStr id); ("capabilityId", JStr cap_id);
          ("outputType", space_json ty); ("args", JArr (args_json a))]

(* F#: `nodeFromJ`. *)
let node_of_j (rd:readers) (cr:codec_readers) (el:jval) : Tot (outcome pipeline_node decode_error) =
  match field "$type" d_str el with
  | Error e -> Error e
  | Ok t ->
    if t = "source" then
      (match field "id" d_str el with
       | Error e -> Error e
       | Ok id ->
         (match field "dataRef" d_str el with
          | Error e -> Error e
          | Ok dref ->
            (match field "outputType" (out_space_of_j rd cr) el with
             | Error e -> Error e
             | Ok ty -> Ok (Source id dref ty))))
    else if t = "invoke" then
      (match field "id" d_str el with
       | Error e -> Error e
       | Ok id ->
         (match field "capabilityId" d_str el with
          | Error e -> Error e
          | Ok cap_id ->
            (match field "outputType" (out_space_of_j rd cr) el with
             | Error e -> Error e
             | Ok ty ->
               (match field "args" (d_list arg_of_j) el with
                | Error e -> Error e
                | Ok a -> Ok (Invoke id cap_id ty a)))))
    else Error (under (Key "$type") (refuse UnknownTag))

let rec nodes_json (ns:list pipeline_node) : Tot (list jval) =
  match ns with
  | [] -> []
  | n :: t -> node_json n :: nodes_json t

(* F#: `CapabilityPipeline.encode`'s `JVal`. *)
let pipeline_json (p:pipeline) : Tot jval = JObj [("nodes", JArr (nodes_json p.p_nodes))]

(* F#: `CapabilityPipeline.decodeDetailed`, past the parse. *)
let pipeline_of_j (rd:readers) (cr:codec_readers) (el:jval) : Tot (outcome pipeline decode_error) =
  match field "nodes" (d_list (node_of_j rd cr)) el with
  | Error e -> Error e
  | Ok ns -> Ok ({ p_nodes = ns })

(* ---- what the codecs compute ---- *)

let rec assoc_app (#a:Type) (k:string) (l m:list (string & a))
  : Lemma (assoc k (app l m) == (match assoc k l with
                                 | Some x -> Some x
                                 | None -> assoc k m))
  = match l with
    | [] -> ()
    | _ :: t -> assoc_app k t m

let rec strs_roundtrip (i:nat) (xs:list string)
  : Lemma (ensures list_from d_str i (strs xs) == Ok xs) (decreases xs)
  = match xs with
    | [] -> ()
    | _ :: t -> strs_roundtrip (i + 1) t

(* A value space reads back as itself — EVERY space, well-formed or not: the space codec makes no
   admission check, which is the signature reader's and the pipeline reader's to make. *)
let space_roundtrip (cr:codec_readers) (s:value_space)
  : Lemma (space_of_j cr (space_json s) == Ok s)
  = match s with
    | Enum xs -> strs_roundtrip 0 xs
    | _ -> ()

let effect_roundtrip (e:effect_class)
  : Lemma (effect_of_j (effect_json e) == Ok e)
  = det_tag_roundtrip e.determinism

let placement_roundtrip (p:placement)
  : Lemma (placement_of_j (placement_json p) == Ok p)
  = ()

(* The three conditional members of an entry, looked up through the four fixed ones. *)
let lk_space (e:sig_entry)
  : Lemma (assoc "space" (entry_fields e) ==
           (match e.s_space with
            | Some s -> if derived_slot_space e then None else Some (space_json s)
            | None -> None))
  = assoc_app "space" (space_members e) (app (slot_members e) (action_members e));
    assoc_app "space" (slot_members e) (action_members e)

let lk_slot (e:sig_entry)
  : Lemma (assoc "slotKind" (entry_fields e) ==
           (match e.s_slot with
            | Some k -> Some (JStr k)
            | None -> None))
  = assoc_app "slotKind" (space_members e) (app (slot_members e) (action_members e));
    assoc_app "slotKind" (slot_members e) (action_members e)

let lk_action (e:sig_entry)
  : Lemma (assoc "actionEffect" (entry_fields e) ==
           (match e.s_action with
            | Some eff -> Some (effect_json eff)
            | None -> None))
  = assoc_app "actionEffect" (space_members e) (app (slot_members e) (action_members e));
    assoc_app "actionEffect" (slot_members e) (action_members e)

(* The entry a decode yields for an entry: its space filled in where it is a slot's derived one
   (`Function.slotSpaceOf`, `arg_space` here). *)
let normal_entry (e:sig_entry) : Tot sig_entry = { e with s_space = arg_space e }

(* An entry the codec carries unchanged: its kind is a hole kind's tag, and it is not the spaceless
   slot a hand-built signature may hold. *)
let canonical_entry (e:sig_entry) : Tot bool = mem e.s_kind hole_tags && arg_space e = e.s_space

(* DECODE AFTER ENCODE, AN ENTRY, EXACTLY. A kind outside `HoleKind.tags` is refused `UnknownTag`
   at `kind`; every other entry reads back as its normal form — itself, unless it is the spaceless
   slot, which reads back with the `SlotTree` of its constraint. *)
let entry_roundtrip (cr:codec_readers) (e:sig_entry)
  : Lemma (entry_of_j cr (entry_json e) ==
           (if mem e.s_kind hole_tags then Ok (normal_entry e)
            else Error ({ d_code = UnknownTag; d_path = [Key "kind"] })))
  = lk_space e; lk_slot e; lk_action e;
    (match e.s_space with
     | Some s -> space_roundtrip cr s
     | None -> ());
    (match e.s_action with
     | Some eff -> effect_roundtrip eff
     | None -> ())

(* A decoded entry is canonical — whatever document it was read from. *)
let entry_decoded_canonical (cr:codec_readers) (el:jval) (e:sig_entry)
  : Lemma (requires entry_of_j cr el == Ok e) (ensures canonical_entry e)
  = ()

(* The index of the first entry whose kind is no hole kind's tag, counted from `i`. *)
let rec first_bad_kind (i:nat) (es:list sig_entry) : Tot (option nat) (decreases es) =
  match es with
  | [] -> None
  | e :: t -> if mem e.s_kind hole_tags then first_bad_kind (i + 1) t else Some i

let rec entries_roundtrip (cr:codec_readers) (i:nat) (es:list sig_entry)
  : Lemma (ensures list_from (entry_of_j cr) i (entries_json es) ==
                   (match first_bad_kind i es with
                    | Some j -> Error ({ d_code = UnknownTag; d_path = [Index j; Key "kind"] })
                    | None -> Ok (map normal_entry es)))
          (decreases es)
  = match es with
    | [] -> ()
    | e :: t -> entry_roundtrip cr e; entries_roundtrip cr (i + 1) t

let rec entries_decoded_canonical (cr:codec_readers) (i:nat) (xs:list jval) (es:list sig_entry)
  : Lemma (requires list_from (entry_of_j cr) i xs == Ok es)
          (ensures for_all canonical_entry es)
          (decreases xs)
  = match xs with
    | [] -> ()
    | x :: rest ->
      (match entry_of_j cr x with
       | Error _ -> ()
       | Ok e ->
         entry_decoded_canonical cr x e;
         (match list_from (entry_of_j cr) (i + 1) rest with
          | Ok es' -> entries_decoded_canonical cr (i + 1) rest es'
          | Error _ -> ()))

(* Normalising changes nothing the admission check reads: a slot's derived space is well-formed. *)
let rec validate_normal (rd:readers) (seen:list string) (es:list sig_entry)
  : Lemma (ensures validate_entries rd seen (map normal_entry es) == validate_entries rd seen es)
          (decreases es)
  = match es with
    | [] -> ()
    | e :: t -> validate_normal rd (e.s_addr :: seen) t

let rec normal_canonical (es:list sig_entry)
  : Lemma (requires for_all canonical_entry es) (ensures map normal_entry es == es)
  = match es with
    | [] -> ()
    | _ :: t -> normal_canonical t

(* The signature a decode yields for a signature. *)
let normal_signature (sg:signature) : Tot signature = { sg with sg_holes = map normal_entry sg.sg_holes }

(* A signature the codec carries unchanged: every entry canonical, and one `Signature.validate`
   admits. *)
let wf_signature (rd:readers) (sg:signature) : Tot bool =
  for_all canonical_entry sg.sg_holes && None? (validate_signature rd sg)

(* THE FIFTEENTH THEOREM (Phase 354), `signature_roundtrip`. F#: `CapabilityCodec.signatureOf` after
   `signatureJson`. DECODE AFTER ENCODE, EXACTLY, for every signature:
     - an entry whose kind is no hole kind's tag is refused `UnknownTag`, the path naming the first
       such entry and its `kind`;
     - otherwise a signature `Signature.validate` refuses is refused `OutOfRange` at `holes` — the
       reader runs the admission check the registries run;
     - otherwise it reads back as its normal form: itself, with each spaceless slot entry given the
       `SlotTree` of its constraint.
   So on a well-formed signature the round trip is the IDENTITY (`signature_roundtrip_identity`),
   and the hand-built spaceless slot is the one value that reads back as another. *)
let signature_roundtrip (rd:readers) (cr:codec_readers) (sg:signature)
  : Lemma (signature_of_j rd cr (signature_json sg) ==
           (match first_bad_kind 0 sg.sg_holes with
            | Some j -> Error ({ d_code = UnknownTag; d_path = [Key "holes"; Index j; Key "kind"] })
            | None ->
              (match validate_signature rd sg with
               | None -> Ok (normal_signature sg)
               | Some _ -> Error ({ d_code = OutOfRange; d_path = [Key "holes"] }))))
  = effect_roundtrip sg.sg_effect;
    entries_roundtrip cr 0 sg.sg_holes;
    validate_normal rd [] sg.sg_holes

let rec canonical_no_bad_kind (i:nat) (es:list sig_entry)
  : Lemma (requires for_all canonical_entry es) (ensures first_bad_kind i es == None) (decreases es)
  = match es with
    | [] -> ()
    | _ :: t -> canonical_no_bad_kind (i + 1) t

let signature_roundtrip_identity (rd:readers) (cr:codec_readers) (sg:signature)
  : Lemma (requires wf_signature rd sg)
          (ensures signature_of_j rd cr (signature_json sg) == Ok sg)
  = signature_roundtrip rd cr sg;
    canonical_no_bad_kind 0 sg.sg_holes;
    normal_canonical sg.sg_holes

(* EVERY DOCUMENT THE READER ACCEPTS IS A WELL-FORMED SIGNATURE — one a registry could admit on
   well-formedness, and one that encodes and reads back as itself. *)
let signature_decoded_wf (rd:readers) (cr:codec_readers) (el:jval) (sg:signature)
  : Lemma (requires signature_of_j rd cr el == Ok sg)
          (ensures wf_signature rd sg /\ signature_of_j rd cr (signature_json sg) == Ok sg)
  = (match el with
     | JObj fields ->
       (match assoc "holes" fields with
        | Some (JArr xs) ->
          (match list_from (entry_of_j cr) 0 xs with
           | Ok es -> entries_decoded_canonical cr 0 xs es
           | Error _ -> ())
        | _ -> ())
     | _ -> ());
    signature_roundtrip_identity rd cr sg

(* A capability the codec carries unchanged: a well-formed signature, and the determinism the
   signature's effect derives — which production's `Capability` cannot disagree with (the axis is
   a derived member since Phase 295) and the model's record, which still carries it, can. *)
let wf_capability (rd:readers) (c:capability) : Tot bool =
  wf_signature rd c.c_signature && c.c_determinism = c.c_signature.sg_effect.determinism

(* THE SIXTEENTH THEOREM (Phase 354), `capability_roundtrip`. F#: `CapabilityCodec.decodeJson` after
   `encodeJson`. DECODE AFTER ENCODE, EXACTLY, for every capability: the signature's refusal, under
   `signature`, where `signature_roundtrip` refuses it; `OutOfRange` at `determinism` where the
   label written disagrees with the one the signature's effect derives (Phase 44's cross-check);
   otherwise the capability with its signature in normal form. On a well-formed capability the
   round trip is the identity. *)
let capability_roundtrip (rd:readers) (cr:codec_readers) (c:capability)
  : Lemma (capability_of_j rd cr (capability_json c) ==
           (match signature_of_j rd cr (signature_json c.c_signature) with
            | Error e -> Error (under (Key "signature") e)
            | Ok sg ->
              if c.c_determinism = c.c_signature.sg_effect.determinism
              then Ok ({ c with c_signature = sg })
              else Error ({ d_code = OutOfRange; d_path = [Key "determinism"] })))
  = signature_roundtrip rd cr c.c_signature;
    placement_roundtrip c.c_placement;
    (if determinism_tag c.c_determinism = determinism_tag c.c_signature.sg_effect.determinism
     then det_tag_injective c.c_determinism c.c_signature.sg_effect.determinism
     else ())

let capability_roundtrip_identity (rd:readers) (cr:codec_readers) (c:capability)
  : Lemma (requires wf_capability rd c)
          (ensures capability_of_j rd cr (capability_json c) == Ok c)
  = capability_roundtrip rd cr c;
    signature_roundtrip_identity rd cr c.c_signature

(* Every document the reader accepts is a well-formed capability, and reads back as itself. *)
let capability_decoded_wf (rd:readers) (cr:codec_readers) (el:jval) (c:capability)
  : Lemma (requires capability_of_j rd cr el == Ok c)
          (ensures wf_capability rd c /\ capability_of_j rd cr (capability_json c) == Ok c)
  = (match el with
     | JObj fields ->
       (match assoc "signature" fields with
        | Some sj ->
          (match signature_of_j rd cr sj with
           | Ok sg -> signature_decoded_wf rd cr sj sg
           | Error _ -> ())
        | None -> ())
     | _ -> ());
    capability_roundtrip_identity rd cr c

let arg_roundtrip (b:(string & arg_source))
  : Lemma (arg_of_j (arg_json b) == Ok b)
  = ()

let rec args_roundtrip (i:nat) (a:list (string & arg_source))
  : Lemma (ensures list_from arg_of_j i (args_json a) == Ok a) (decreases a)
  = match a with
    | [] -> ()
    | b :: t -> arg_roundtrip b; args_roundtrip (i + 1) t

(* THE SEVENTEENTH THEOREM (Phase 354), `node_roundtrip`. F#: `CapabilityPipeline`'s `nodeFromJ`
   after `nodeToJ`. DECODE AFTER ENCODE, EXACTLY, for every node: a node whose output space
   `Space.wellFormed` refuses is refused `OutOfRange` at `outputType`, and every other node reads
   back as itself — its id, its data ref or capability id, its output space and its arguments in
   order, each edge and each literal. *)
let node_roundtrip (rd:readers) (cr:codec_readers) (n:pipeline_node)
  : Lemma (node_of_j rd cr (node_json n) ==
           (match space_wf rd (node_output n) with
            | None -> Ok n
            | Some _ -> Error ({ d_code = OutOfRange; d_path = [Key "outputType"] })))
  = space_roundtrip cr (node_output n);
    (match n with
     | Source _ _ _ -> ()
     | Invoke _ _ _ a -> args_roundtrip 0 a)

(* Every document the node reader accepts is a node with a well-formed output space, and reads
   back as itself. *)
let node_decoded_wf (rd:readers) (cr:codec_readers) (el:jval) (n:pipeline_node)
  : Lemma (requires node_of_j rd cr el == Ok n)
          (ensures None? (space_wf rd (node_output n)) /\ node_of_j rd cr (node_json n) == Ok n)
  = node_roundtrip rd cr n

(* The index of the first node whose output space is not well-formed, counted from `i`. *)
let rec first_bad_node (rd:readers) (i:nat) (ns:list pipeline_node) : Tot (option nat) (decreases ns) =
  match ns with
  | [] -> None
  | n :: t -> if None? (space_wf rd (node_output n)) then first_bad_node rd (i + 1) t else Some i

let rec nodes_roundtrip (rd:readers) (cr:codec_readers) (i:nat) (ns:list pipeline_node)
  : Lemma (ensures list_from (node_of_j rd cr) i (nodes_json ns) ==
                   (match first_bad_node rd i ns with
                    | Some j -> Error ({ d_code = OutOfRange; d_path = [Index j; Key "outputType"] })
                    | None -> Ok ns))
          (decreases ns)
  = match ns with
    | [] -> ()
    | n :: t -> node_roundtrip rd cr n; nodes_roundtrip rd cr (i + 1) t

(* ... and a whole pipeline: refused at the first node with an ill-formed output space, naming it;
   otherwise the identity, the nodes in declaration order. *)
let pipeline_roundtrip (rd:readers) (cr:codec_readers) (p:pipeline)
  : Lemma (pipeline_of_j rd cr (pipeline_json p) ==
           (match first_bad_node rd 0 p.p_nodes with
            | Some j -> Error ({ d_code = OutOfRange; d_path = [Key "nodes"; Index j; Key "outputType"] })
            | None -> Ok p))
  = nodes_roundtrip rd cr 0 p.p_nodes

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
let twins : list twin = [
  { tname = "determinism-tag-of-clock-and-network";
    tholds = (fun () ->
      determinism_tag ({ has_clock = true; has_random = false; has_network = true }) = "clock+network") };
  { tname = "det-of-tag-reads-its-canonical-order";
    tholds = (fun () ->
      det_of_tag "random+network" = Some ({ has_clock = false; has_random = true; has_network = true })) };
  { tname = "det-of-tag-refuses-another-order";
    tholds = (fun () -> det_of_tag "network+random" = None) };
  { tname = "a-bounded-repeat-is-required";
    tholds = (fun () ->
      (entry_of ({ h_addr = "r"; h_name = "r"; h_kind = RepeatHole (IntRange 0 3) })).s_required = true) };
  { tname = "an-unbounded-repeat-is-not-required";
    tholds = (fun () -> (entry_of ({ h_addr = "r"; h_name = "r"; h_kind = RepeatHole AnyString })).s_required = false) };
  { tname = "an-entry-of-no-hole-kind-is-not-total";
    tholds = (fun () ->
      entry_total ({ s_addr = "x"; s_name = "x"; s_kind = "int"; s_space = Some AnyString; s_slot = None;
                     s_action = None; s_required = true }) = false) };
  (* Phase 307 — the count space, the duplicate scan and the admission check. *)
  { tname = "a-repeat-over-a-float-range-is-not-required";
    tholds = (fun () ->
      (entry_of ({ h_addr = "r"; h_name = "r"; h_kind = RepeatHole (FloatRange "0" "1") })).s_required = false) };
  { tname = "a-repeat-past-the-cap-is-not-a-count";
    tholds = (fun () -> is_count (IntRange 0 1000001) = false && is_count (IntRange 0 1000000) = true) };
  { tname = "repeated-names-each-repeat-in-order";
    tholds = (fun () -> repeated [] ["a"; "b"; "a"; "b"; "a"] = ["a"; "b"; "a"]) };
  { tname = "a-second-address-is-the-duplicate";
    tholds = (fun () ->
      validate_entries ({ int_of = (fun _ -> None); float_in = (fun _ _ _ -> false); str_len = (fun _ -> 0);
                          kind_of = (fun _ -> None); float_fault = (fun _ _ -> None) }) []
        [ { s_addr = "a"; s_name = "a"; s_kind = "value"; s_space = Some (IntRange 0 1); s_slot = None;
            s_action = None; s_required = true };
          { s_addr = "a"; s_name = "b"; s_kind = "value"; s_space = Some (IntRange 5 1); s_slot = None;
            s_action = None; s_required = true } ] = Some (DuplicateHoleAddr "a")) };
  (* Phase 354 — the handler table, the pipeline and the codecs. *)
  { tname = "map-of-list-keeps-the-later-binding-of-a-repeated-key";
    tholds = (fun () ->
      map_of_list (fun a b -> a = "a" || b = "b") [("b", 1); ("a", 2); ("b", 3)] = [("a", 2); ("b", 3)]) };
  { tname = "a-handler-key-that-is-no-hole-is-refused-naming-the-declared-actions";
    tholds = (fun () ->
      check_keys #int ["go"] ["go"; "title"] [("stop", { hb_handler = 0; hb_effect = pure_deterministic })]
        = Error (UnknownActionAddr "stop" ["go"])) };
  { tname = "a-handler-on-a-data-hole-is-not-an-action-hole";
    tholds = (fun () ->
      check_keys #int ["go"] ["go"; "title"] [("title", { hb_handler = 0; hb_effect = pure_deterministic })]
        = Error (NotAnActionHole "title")) };
  { tname = "a-handler-past-its-ceiling-is-refused-naming-both-effects";
    tholds = (fun () ->
      check_effects #int [("go", { hb_handler = 0; hb_effect = { host = WritesHost; determinism = deterministic } })]
        [("go", pure_deterministic)]
        = Error (HandlerEffectExceedsCeiling "go" pure_deterministic
                   ({ host = WritesHost; determinism = deterministic }))) };
  { tname = "first-dup-is-the-first-id-seen-again";
    tholds = (fun () -> first_dup ["a"; "b"; "c"; "b"; "a"] = Some "a") };
  { tname = "an-edge-that-closes-a-cycle-is-named-from-the-node";
    tholds = (fun () ->
      edge_fault ({ float_within = (fun _ _ _ _ -> false); int_within = (fun _ _ _ _ -> false) })
        ({ int_of = (fun _ -> None); float_in = (fun _ _ _ -> false); str_len = (fun _ -> 0);
           kind_of = (fun _ -> None); float_fault = (fun _ _ -> None) })
        [ Invoke "a" "c" AnyString [("x", FromNode "b")]; Invoke "b" "c" AnyString [("x", FromNode "a")] ]
        ["a"; "b"] "a" "x" "b" AnyString = Some (PipelineCycle "a" ["a"; "b"])) };
  { tname = "a-later-upstream-that-closes-no-cycle-is-a-forward-edge";
    tholds = (fun () ->
      edge_fault ({ float_within = (fun _ _ _ _ -> false); int_within = (fun _ _ _ _ -> false) })
        ({ int_of = (fun _ -> None); float_in = (fun _ _ _ -> false); str_len = (fun _ -> 0);
           kind_of = (fun _ -> None); float_fault = (fun _ _ -> None) })
        [ Invoke "a" "c" AnyString [("x", FromNode "b")]; Source "b" "ref" AnyString ]
        ["a"; "b"] "a" "x" "b" AnyString = Some (PipelineForwardEdge "a" "x" "b")) };
  { tname = "the-dirty-set-is-the-change-and-everything-downstream";
    tholds = (fun () ->
      dirty_set ["s"]
        ({ p_nodes = [ Source "s" "ref" AnyString;
                       Invoke "a" "c" AnyString [("x", FromNode "s")];
                       Invoke "b" "c" AnyString [("x", FromNode "a")];
                       Source "t" "ref" AnyString ] }) = ["s"; "a"; "b"]) };
  { tname = "an-entry-kind-outside-the-tags-is-refused-at-kind";
    tholds = (fun () ->
      entry_of_j ({ float_of_int = (fun _ -> "0") })
        (entry_json ({ s_addr = "x"; s_name = "x"; s_kind = "int"; s_space = Some AnyString; s_slot = None;
                       s_action = None; s_required = true }))
        = Error ({ d_code = UnknownTag; d_path = [Key "kind"] })) };
  { tname = "a-spaceless-slot-reads-back-with-its-derived-space";
    tholds = (fun () ->
      entry_of_j ({ float_of_int = (fun _ -> "0") })
        (entry_json ({ s_addr = "s"; s_name = "s"; s_kind = "slot"; s_space = None; s_slot = Some "card";
                       s_action = None; s_required = true }))
        = Ok ({ s_addr = "s"; s_name = "s"; s_kind = "slot"; s_space = Some (SlotTree (Some "card"));
                s_slot = Some "card"; s_action = None; s_required = true })) };
  { tname = "the-descriptor-spelling-of-a-space-is-read-leniently";
    tholds = (fun () ->
      space_of_j ({ float_of_int = (fun _ -> "0") })
        (JObj [("kind", JStr "stringLen"); ("minLength", JInt 1); ("maxLength", JInt 3)]) = Ok (StringLen 1 3)) };
  { tname = "a-node-with-an-empty-output-space-is-refused-at-outputType";
    tholds = (fun () ->
      node_of_j ({ int_of = (fun _ -> None); float_in = (fun _ _ _ -> false); str_len = (fun _ -> 0);
                   kind_of = (fun _ -> None); float_fault = (fun _ _ -> None) })
        ({ float_of_int = (fun _ -> "0") })
        (node_json (Source "s" "ref" (IntRange 5 1)))
        = Error ({ d_code = OutOfRange; d_path = [Key "outputType"] })) } ]

let _ = assert_norm (twins_hold twins == true)


(* ======================================================================================
   THE POLICY GATE (Phase 318; F#: `PolicyDecision`, `PolicyDecision.rank` / `join`,
   `RegistryPolicy.decideNamed`, `CapabilityRegistry.withGate` / `dispatch`).

   A registry carries a list of named gates; `dispatch` resolves the id, validates the
   arguments, takes the JOIN of every gate's decision, and only under `Allow` runs the body.
   Two theorems, both over every gate list and every body:

     `policy_join_monotone` — raising either argument of `join` never lowers the result, in
     the order `Allow < NeedsApproval < Deny` (`policy_rank`). With `join_is_max` it is what makes
     `withGate` a tightening: the decision over a longer gate list is the join of the shorter
     one's with the new gate's, so it can only rise.

     `gate_before_body` — for an id the registry holds and arguments that validate, a gate
     list whose join is not `Allow` answers `ApprovalRequired n` or `PolicyRefused n m alts`
     for EVERY body alike (so no body ran — the first theorem's reading of "runs no handler"),
     and `n` is the name of a gate in the list whose own decision is exactly the one returned
     (`decide_named_names_a_gate`) — the refusal names the policy that refused it.

   The gated outcome wraps the ungated seam's refusals (`GRefused`) rather than widening
   `invoke_error`: production adds the two cases to `InvokeError` itself, and the wrapper is
   the same three-way split stated without touching the extracted type. Everything in this
   section is `noextract_to "FSharp"`, so the extracted oracle is unchanged; the bridge to
   production is the `Conformance.policyLaws` family, which samples the same two properties
   against the shipped registries. ====================================================== *)

[@@ noextract_to "FSharp"]
type decision =
  | DAllow : decision
  | DNeedsApproval : decision
  | DDeny : msg:string -> alts:list string -> decision

(* F#: `PolicyDecision.rank`. *)
[@@ noextract_to "FSharp"]
let policy_rank (d:decision) : Tot nat =
  match d with
  | DAllow -> 0
  | DNeedsApproval -> 1
  | DDeny _ _ -> 2

(* F#: `PolicyDecision.join` — the left one on a tie. *)
[@@ noextract_to "FSharp"]
let policy_join (a b:decision) : Tot decision = if policy_rank b > policy_rank a then b else a

(* The join is the maximum in rank. *)
let join_is_max (a b:decision)
  : Lemma (policy_rank (policy_join a b) == (if policy_rank a >= policy_rank b then policy_rank a else policy_rank b))
  = ()

(* THEOREM (Phase 318) — `policy_join_monotone`. *)
let policy_join_monotone (a a' b b':decision)
  : Lemma (requires policy_rank a <= policy_rank a' /\ policy_rank b <= policy_rank b')
          (ensures policy_rank (policy_join a b) <= policy_rank (policy_join a' b'))
  = ()

(* F#: `PolicyGate`. *)
[@@ noextract_to "FSharp"]
noeq type gate = { gname: string; gdecide: capability -> invocation -> decision }

(* F#: `RegistryPolicy.decideNamed` — the fold from `("", Allow)`, keeping the first gate of the
   highest rank. *)
[@@ noextract_to "FSharp"]
let rec decide_from (gs:list gate) (c:capability) (a:invocation) (acc:(string & decision))
  : Tot (string & decision) (decreases gs) =
  match gs with
  | [] -> acc
  | g :: t ->
    let d = g.gdecide c a in
    if policy_rank d > policy_rank (snd acc) then decide_from t c a (g.gname, d) else decide_from t c a acc

[@@ noextract_to "FSharp"]
let decide_named (gs:list gate) (c:capability) (a:invocation) : Tot (string & decision) =
  decide_from gs c a ("", DAllow)

[@@ noextract_to "FSharp"]
let rec gate_named (n:string) (d:decision) (gs:list gate) (c:capability) (a:invocation) : Tot prop =
  match gs with
  | [] -> False
  | g :: t -> (g.gname == n /\ g.gdecide c a == d) \/ gate_named n d t c a

let rec decide_from_names_a_gate (gs:list gate) (c:capability) (a:invocation) (acc:(string & decision))
  : Lemma (ensures (let (n, d) = decide_from gs c a acc in
                    (n == fst acc /\ d == snd acc) \/ gate_named n d gs c a))
          (decreases gs)
  = match gs with
    | [] -> ()
    | g :: t ->
      let d = g.gdecide c a in
      if policy_rank d > policy_rank (snd acc) then decide_from_names_a_gate t c a (g.gname, d)
      else decide_from_names_a_gate t c a acc

(* A decision other than `Allow` was made by a gate of the list, under the name it carries. *)
let decide_named_names_a_gate (gs:list gate) (c:capability) (a:invocation)
  : Lemma (ensures (let (n, d) = decide_named gs c a in DAllow? d \/ gate_named n d gs c a))
  = decide_from_names_a_gate gs c a ("", DAllow)

(* The decision only rises along the fold, so a longer gate list never decides lower. *)
let rec decide_from_rises (gs:list gate) (c:capability) (a:invocation) (acc:(string & decision))
  : Lemma (ensures policy_rank (snd (decide_from gs c a acc)) >= policy_rank (snd acc)) (decreases gs)
  = match gs with
    | [] -> ()
    | g :: t ->
      let d = g.gdecide c a in
      if policy_rank d > policy_rank (snd acc) then decide_from_rises t c a (g.gname, d)
      else decide_from_rises t c a acc

[@@ noextract_to "FSharp"]
type gated_error =
  | GRefused : invoke_error -> gated_error
  | GPolicyRefused : policy:string -> msg:string -> alts:list string -> gated_error
  | GApprovalRequired : policy:string -> gated_error

(* F#: `CapabilityRegistry.dispatch` over a registry carrying `gs`: resolve, validate, the gates'
   join, and only under `Allow` the body. *)
[@@ noextract_to "FSharp"]
let dispatch_gated (#v:Type) (rd:readers) (r:registry) (gs:list gate) (id:string) (a:invocation)
                   (body:capability -> unit -> deferred v)
  : Tot (outcome (deferred v) gated_error) =
  match find_cap id r.capabilities with
  | None -> Error (GRefused (NoSuchCapability id (ids r.capabilities)))
  | Some c ->
    match validate_args rd c a with
    | Error e -> Error (GRefused e)
    | Ok () ->
      match decide_named gs c a with
      | (_, DAllow) ->
        (match body c () with
         | Ready x -> Ok (Ready x)
         | Pending -> Ok Pending
         | Failed m -> Error (GRefused (BodyFailed m)))
      | (n, DNeedsApproval) -> Error (GApprovalRequired n)
      | (n, DDeny m alts) -> Error (GPolicyRefused n m alts)

(* THEOREM (Phase 318) — `gate_before_body`. For a held id and arguments that validate, a gate
   list whose join is not `Allow` is refused — `GApprovalRequired n` or `GPolicyRefused n m alts`
   — identically under every body, so no body runs; and `n` names a gate of the list whose own
   decision is the one the refusal reports. *)
let gate_before_body (#v:Type) (rd:readers) (r:registry) (gs:list gate) (id:string) (a:invocation)
                     (body body':capability -> unit -> deferred v)
  : Lemma (requires (match find_cap id r.capabilities with
                     | Some c -> validate_args rd c a == Ok () /\ ~(DAllow? (snd (decide_named gs c a)))
                     | None -> False))
          (ensures (match find_cap id r.capabilities with
                    | Some c ->
                      (let (n, d) = decide_named gs c a in
                       gate_named n d gs c a /\
                       (match d with
                        | DNeedsApproval -> dispatch_gated rd r gs id a body == Error (GApprovalRequired n)
                        | DDeny m alts -> dispatch_gated rd r gs id a body == Error (GPolicyRefused n m alts)
                        | DAllow -> False))
                    | None -> True) /\
                   dispatch_gated rd r gs id a body == dispatch_gated rd r gs id a body')
  = match find_cap id r.capabilities with
    | Some c -> decide_named_names_a_gate gs c a
    | None -> ()

(* With no gate the gated dispatch IS the seam's `dispatch`, its refusals wrapped: the gate is
   additive over the Phase 30 theorems. *)
let no_gate_is_dispatch (#v:Type) (rd:readers) (r:registry) (id:string) (a:invocation)
                        (body:capability -> unit -> deferred v)
  : Lemma (ensures (match dispatch rd r id a body with
                    | Ok x -> dispatch_gated rd r [] id a body == Ok x
                    | Error e -> dispatch_gated rd r [] id a body == Error (GRefused e)))
  = ()
