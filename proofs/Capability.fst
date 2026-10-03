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

   Two things are PARAMETERS rather than clauses, exactly as Phase 135 made `float i` a parameter
   and Phase 176 made the pipeline evaluator one. The WITNESS (`ArtifactWitness`'s `Holes`,
   `Effect`, `Bind`, the tree witness's `KindTag`, and `Tree.preorder` over it) is a record of
   functions over an abstract `node` type: the algebra reads holes off it and hands bindings back
   to it, and nothing here says what a domain's `Bind` does. And the three SCALAR READERS the
   value-space check needs — `System.Int32.TryParse`, `System.Double.TryParse` against a float
   range, and `String.Length` — are a `readers` record: the space vocabulary is modelled, the
   lexical parsers behind two of its five constructors are not, and a float range's bounds cross
   as opaque carriers. The two codecs are outside the model; `invocationKey` entered it with
   Phase 225 (section 12), over a `key_renderers` record — `Hash.fnv1a`, the address comparator
   and `Hash.canonicalField` — for the same reason.

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

   WHAT IS NOT CLAIMED. Anything about a domain's `Bind` — that a bound hole is cleared, that a
   slot's inner tree is where `compose` put it — which is the witness contract
   (`lawful-abstract-witness`). Anything about the two lexical parsers or a float range beyond
   the envelope the readers premise states (`capability-scalar-readers-abstract`). The ORDER
   `CapabilityRegistry.enumerate` returns — production's `Map` sorts by id, the model holds a finite map
   as a list, and the theorem is about membership. Whether two distinct capture-key pre-images
   HASH apart (a claim about FNV-1a). `CapabilityCodec`, the
   `FunctionRegistry`, `ContentPack` and `CapabilityPipeline` surfaces, and `applyMemo`.

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
            s_action = None; s_required = true } ] = Some (DuplicateHoleAddr "a")) } ]

let _ = assert_norm (twins_hold twins == true)
