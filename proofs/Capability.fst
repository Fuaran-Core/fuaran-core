(*
   Capability — default-deny dispatch and the three function laws as theorems: the seam every
   AI-driven edit crosses, `Fuaran.Core.Function`, modelled clause for clause and proved
   (fuaran-core Phase 177).

   WHAT IS MODELLED. `src/Fuaran.Core.Function/Function.fs` — three surfaces of one file:

     - the EFFECT LATTICE (`Effect.join` / `Effect.covers` and the two rank tables they are
       written through), the VALUE SPACES (`Space.validate` / `Space.isBounded`) and the hole
       vocabulary (`HoleKind` / `HoleDecl` / `SigEntry` / `Signature` / `Arg` / `ApplyError`);
     - the FUNCTION ALGEBRA over the domain-witness record: `Function.signature`,
       `signatureExcluding`, `isTotal`, the private `guardTotal` / `validateArg` / `bindArgs` and
       the `apply` / `curry` that are its two faces, `compose`, `composedEffect`, `observedEffect`
       and `auditEffect`;
     - the CAPABILITY SEAM: `Capability.create` / `validateArgs` / `invoke`, `Registry.empty` /
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

     - `unregistered_refused` — `Registry.dispatch` of an id the registry does not hold is the
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
       names a value the space really refuses (`refusal_is_truthful`). THE FINDING read off it,
       `slot_hole_uninvocable`: a required entry with no value space — which is what `signature`
       makes of every `SlotHole` — refuses EVERY argument list, so a capability declared over an
       artifact with a tree-typed slot can be registered and enumerated and never dispatched.
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
   `Registry.enumerate` returns — production's `Map` sorts by id, the model holds a finite map
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
      `Effect.covers`, written through the same two rank tables the F# uses.
   ====================================================================================== *)

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

(* F#: `Effect.pureDeterministic`. *)
let pure_deterministic : effect_class = { host = Pure; determinism = Deterministic }

(* F#: `Effect.hostRank`. *)
let host_rank (h:host_effect) : Tot int =
  match h with
  | Pure -> 0
  | ReadsHost -> 1
  | WritesHost -> 2

(* F#: `Effect.detRank`. *)
let det_rank (d:determinism_source) : Tot int =
  match d with
  | Deterministic -> 0
  | Clock -> 1
  | Random -> 2
  | Network -> 3

(* F#: `Effect.hostOf` — `0 -> Pure | 1 -> ReadsHost | _ -> WritesHost`. *)
let host_of (n:int) : Tot host_effect =
  if n = 0 then Pure else if n = 1 then ReadsHost else WritesHost

(* F#: `Effect.detOf` — `0 -> Deterministic | 1 -> Clock | 2 -> Random | _ -> Network`. *)
let det_of (n:int) : Tot determinism_source =
  if n = 0 then Deterministic else if n = 1 then Clock else if n = 2 then Random else Network

(* F#: `max`. *)
let max_int (a b:int) : Tot int = if a >= b then a else b

(* F#: `Effect.join` — componentwise widest. *)
let join (a b:effect_class) : Tot effect_class =
  { host = host_of (max_int (host_rank a.host) (host_rank b.host));
    determinism = det_of (max_int (det_rank a.determinism) (det_rank b.determinism)) }

(* F#: `Effect.covers` — declared at least as wide as actual, on both axes. *)
let covers (declared actual:effect_class) : Tot bool =
  host_rank declared.host >= host_rank actual.host &&
  det_rank declared.determinism >= det_rank actual.determinism

(* F#: `Effect.determinismTag`. *)
let determinism_tag (d:determinism_source) : Tot string =
  match d with
  | Deterministic -> "deterministic"
  | Clock -> "clock"
  | Random -> "random"
  | Network -> "network"

(* The two tables are inverse on the ranks they produce — the fact every lattice law rests on. *)
let host_of_rank (h:host_effect) : Lemma (host_of (host_rank h) == h) = ()
let det_of_rank (d:determinism_source) : Lemma (det_of (det_rank d) == d) = ()

let join_comm (a b:effect_class) : Lemma (join a b == join b a) = ()

let join_idem (a:effect_class) : Lemma (join a a == a) =
  host_of_rank a.host; det_of_rank a.determinism

let join_pure (a:effect_class)
  : Lemma (join pure_deterministic a == a /\ join a pure_deterministic == a)
  = host_of_rank a.host; det_of_rank a.determinism

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

(* The readers premise: the three host functions `Space.validate` reaches for.
   `int_of`   — F#: `System.Int32.TryParse`, as an option.
   `float_in` — F#: `System.Double.TryParse` (invariant, `NumberStyles.Float`) of the value, then
                `lo <= v && v <= hi` against the two carriers' parsed floats.
   `str_len`  — F#: `String.Length`. *)
noeq type readers = {
  int_of:   string -> option int;
  float_in: string -> string -> string -> bool;
  str_len:  string -> nat
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

(* F#: `Space.isBounded` — the totality criterion for repeats. *)
let is_bounded (space:value_space) : Tot bool =
  match space with
  | AnyString -> false
  | _ -> true

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

(* F#: `ApplyError`. *)
type apply_error =
  | UnknownHoleAddr     : addr:string -> declared:list string -> apply_error
  | ValueOutOfSpace     : addr:string -> space:value_space -> got:string -> apply_error
  | RequiredHolesUnbound: addrs:list string -> apply_error
  | NotASlot            : addr:string -> apply_error
  | SlotKindMismatch    : addr:string -> expected:string -> got:string -> apply_error
  | NonTotal            : addr:string -> apply_error
  | BindFailed          : addr:string -> reason:string -> apply_error

(* F#: `InvokeError`. *)
type invoke_error =
  | NoSuchCapability   : id:string -> known:list string -> invoke_error
  | DuplicateCapability: id:string -> invoke_error
  | UnknownArg         : addr:string -> declared:list string -> invoke_error
  | ArgOutOfSpace      : addr:string -> space:value_space -> got:string -> invoke_error
  | RequiredArgsUnbound: addrs:list string -> invoke_error
  | UninvocableArg     : addr:string -> invoke_error
  | BodyFailed         : reason:string -> invoke_error

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
   `NodeWitness.KindTag` under `Tree`, and `Tree.preorder w.Tree` — the one derived walk
   `observedEffect` makes. `IdW` is never read by any function modelled here. *)
noeq type witness (node:Type) = {
  holes:     node -> list hole_decl;
  eff:       node -> effect_class;
  bind_hole: string -> arg node -> node -> outcome node string;
  kind_tag:  node -> string;
  preorder:  node -> list node
}

(* F#: `Function.signature`'s local `entry`. *)
let entry_of (h:hole_decl) : Tot sig_entry =
  match h.h_kind with
  | ValueHole s ->
    { s_addr = h.h_addr; s_name = h.h_name; s_kind = "value";
      s_space = Some s; s_slot = None; s_action = None; s_required = true }
  | SlotHole c ->
    { s_addr = h.h_addr; s_name = h.h_name; s_kind = "slot";
      s_space = None; s_slot = c; s_action = None; s_required = true }
  | RepeatHole s ->
    { s_addr = h.h_addr; s_name = h.h_name; s_kind = "repeat";
      s_space = Some s; s_slot = None; s_action = None; s_required = false }
  | ActionHole e ->
    { s_addr = h.h_addr; s_name = h.h_name; s_kind = "action";
      s_space = None; s_slot = None; s_action = Some e; s_required = false }

(* F#: `Function.signature`. *)
let signature_of (#node:Type) (w:witness node) (name:string) (n:node) : Tot signature =
  { sg_name = name; sg_holes = map entry_of (w.holes n); sg_effect = w.eff n }

(* F#: `Function.signatureExcluding`. *)
let signature_excluding (bound:list string) (sg:signature) : Tot signature =
  { sg with sg_holes = excluding bound sg.sg_holes }

(* F#: `Function.isTotal`'s per-entry predicate. *)
let entry_total (e:sig_entry) : Tot bool =
  e.s_kind <> "repeat" ||
  (match e.s_space with
   | Some s -> is_bounded s
   | None -> false)

(* F#: `Function.isTotal`. *)
let is_total (sg:signature) : Tot bool = for_all entry_total sg.sg_holes

(* F#: `guardTotal`'s per-hole pick. *)
let non_total (h:hole_decl) : Tot (option apply_error) =
  match h.h_kind with
  | RepeatHole s -> if not (is_bounded s) then Some (NonTotal h.h_addr) else None
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
    if not (is_bounded space) then Error (NonTotal addr)
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

(* F#: `compose`'s tail — the one `Bind` it makes, at the slot's address. *)
let wire (#node:Type) (w:witness node) (slot_addr:string) (inner outer:node) : Tot (outcome node apply_error) =
  match w.bind_hole slot_addr (SlotArg inner) outer with
  | Ok n -> Ok n
  | Error m -> Error (BindFailed slot_addr m)

(* F#: `Function.compose`. *)
let compose (#node:Type) (w:witness node) (slot_addr:string) (inner outer:node) : Tot (outcome node apply_error) =
  let holes = w.holes outer in
  match find_hole slot_addr holes with
  | None -> Error (UnknownHoleAddr slot_addr (map addr_of holes))
  | Some h ->
    match h.h_kind with
    | SlotHole c ->
      (match c with
       | Some kt -> if w.kind_tag inner <> kt then Error (SlotKindMismatch slot_addr kt (w.kind_tag inner)) else wire w slot_addr inner outer
       | None -> wire w slot_addr inner outer)
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
      `CapabilityRegistry`, `Registry.empty` / `register` / `tryFind` / `enumerate` / `dispatch`.
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

(* F#: `(string * string) list` — a typed invocation's args, addr → value. *)
type invocation = list (string & string)

(* F#: `validateArgs`'s local `checkArgs` — step 1, in the caller's arg order. *)
let rec check_args (rd:readers) (holes:list sig_entry) (declared:list string) (a:invocation)
  : Tot (outcome unit invoke_error) (decreases a) =
  match a with
  | [] -> Ok ()
  | (addr, value) :: rest ->
    match find_entry addr holes with
    | None -> Error (UnknownArg addr declared)
    | Some h ->
      match h.s_space with
      | None -> Error (UninvocableArg addr)
      | Some space ->
        if validate rd space value then check_args rd holes declared rest
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

(* F#: `Registry.empty`. *)
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

(* F#: `Registry.register` — additive, no silent overwrite. *)
let register (c:capability) (r:registry) : Tot (outcome registry invoke_error) =
  match find_cap c.c_id r.capabilities with
  | Some _ -> Error (DuplicateCapability c.c_id)
  | None -> Ok { capabilities = c :: r.capabilities }

(* F#: `Registry.tryFind`. *)
let try_find_cap (id:string) (r:registry) : Tot (option capability) = find_cap id r.capabilities

(* F#: `Registry.enumerate` — the discovery surface. Production sorts by id; the model returns
   the map's entries, and the theorem below is about membership. *)
let enumerate (r:registry) : Tot (list capability) = r.capabilities

(* F#: `Registry.dispatch` — resolve the id (default-deny), then `Capability.invoke`. *)
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

(* THE FIRST THEOREM. F#: `Registry.dispatch` on an id the registry does not hold. The refusal is
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
         (match h.s_space with
          | None -> ()
          | Some space -> if validate rd space value then check_args_shape rd holes declared rest else ()))

let validate_args_shape (rd:readers) (c:capability) (a:invocation)
  : Lemma (ensures (match validate_args rd c a with
                    | Ok () -> True
                    | Error e -> UnknownArg? e \/ UninvocableArg? e \/ ArgOutOfSpace? e \/ RequiredArgsUnbound? e))
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
         (match h.s_space with
          | None -> ()
          | Some space -> if validate rd space value then check_args_ok_declared rd holes declared rest else ()))

let validate_args_sound (rd:readers) (c:capability) (a:invocation)
  : Lemma (requires validate_args rd c a == Ok ())
          (ensures all_declared c.c_signature.sg_holes (keys a) /\
                   unbound_required c.c_signature.sg_holes a == [])
  = check_args_ok_declared rd c.c_signature.sg_holes (entry_addrs c.c_signature.sg_holes) a

(* What a REFUSAL guarantees: an `ArgOutOfSpace` names a value the named space really refuses, an
   `UnknownArg` names an address no hole declares, an `UninvocableArg` names a slot hole. *)
let rec refusal_is_truthful (rd:readers) (holes:list sig_entry) (declared:list string) (a:invocation)
  : Lemma (ensures (match check_args rd holes declared a with
                    | Error (ArgOutOfSpace addr space got) -> not (validate rd space got) /\ has_key addr a
                    | Error (UnknownArg addr d) -> None? (find_entry addr holes) /\ d == declared
                    | Error (UninvocableArg addr) ->
                      (match find_entry addr holes with
                       | Some h -> None? h.s_space
                       | None -> False)
                    | _ -> True))
          (decreases a)
  = match a with
    | [] -> ()
    | (addr, value) :: rest ->
      (match find_entry addr holes with
       | None -> ()
       | Some h ->
         (match h.s_space with
          | None -> ()
          | Some space -> if validate rd space value then refusal_is_truthful rd holes declared rest else ()))

(* THE FINDING. A signature entry that is REQUIRED and has NO value space — a slot hole, which
   `Function.signature` marks required and spaceless — makes the capability un-invocable by
   construction: an argument at its address is `UninvocableArg`, and no argument there is
   `RequiredArgsUnbound`. Every argument list is refused. A capability declared over a template
   with a tree-typed slot can be registered and enumerated and never dispatched. *)
let rec check_args_hits_uninvocable (rd:readers) (holes:list sig_entry) (declared:list string) (a:invocation) (h:sig_entry)
  : Lemma (requires has_key h.s_addr a /\ find_entry h.s_addr holes == Some h /\ None? h.s_space)
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
           (match e.s_space with
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

let slot_hole_uninvocable (rd:readers) (c:capability) (a:invocation) (h:sig_entry)
  : Lemma (requires find_entry h.s_addr c.c_signature.sg_holes == Some h /\ h.s_required /\ None? h.s_space)
          (ensures Error? (validate_args rd c a))
  = let holes = c.c_signature.sg_holes in
    if has_key h.s_addr a then check_args_hits_uninvocable rd holes (entry_addrs holes) a h
    else (find_entry_memp h.s_addr holes; unbound_required_memp holes a h)

(* And `signature` is where such an entry comes from: a `SlotHole` is entered required and
   spaceless, so every artifact with a tree-typed slot yields an un-invocable capability. *)
let slot_entry_shape (h:hole_decl)
  : Lemma (requires SlotHole? h.h_kind)
          (ensures (entry_of h).s_required /\ None? (entry_of h).s_space)
  = ()

(* ======================================================================================
   8. THE THIRD THEOREM — what is enumerable is exactly what is dispatchable.
   ====================================================================================== *)

(* THE THIRD THEOREM. F#: `Registry.enumerate` and `Registry.tryFind` read the same map. An id is
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
let register_refuses_duplicate (c:capability) (r:registry)
  : Lemma (requires mem c.c_id (ids (enumerate r)))
          (ensures register c r == Error (DuplicateCapability c.c_id))
  = find_cap_mem c.c_id r.capabilities

let register_extends (c:capability) (r:registry)
  : Lemma (requires not (mem c.c_id (ids (enumerate r))))
          (ensures register c r == Ok { capabilities = c :: r.capabilities } /\
                   (forall (id:string). mem id (ids (c :: r.capabilities)) <==> (id = c.c_id \/ mem id (ids (enumerate r)))))
  = find_cap_mem c.c_id r.capabilities

(* A registry built by `register` holds distinct ids — the invariant `empty` starts and every
   accepted registration keeps, so `find_cap` is a function of the id and the enumeration never
   shows one id twice. *)
let register_keeps_distinct (c:capability) (r r':registry)
  : Lemma (requires distinct (ids (enumerate r)) /\ register c r == Ok r')
          (ensures distinct (ids (enumerate r')))
  = find_cap_mem c.c_id r.capabilities

let empty_distinct () : Lemma (distinct (ids (enumerate empty))) = ()

(* ======================================================================================
   9. THE FOURTH THEOREM — law 1, totality: an unbounded repeat is rejected, never run.
   ====================================================================================== *)

(* The per-hole reading of `isTotal` on the declaration side. *)
let hole_total (h:hole_decl) : Tot bool =
  match h.h_kind with
  | RepeatHole s -> is_bounded s
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

let compose_rename (#node:Type) (w:witness node) (f:string -> string) (slot_addr:string) (inner n:node)
  : Lemma (compose (renamed f w) slot_addr inner n == compose w slot_addr inner n)
  = find_rename f slot_addr (w.holes n); addrs_rename f (w.holes n)

(* THE FIFTH THEOREM, part one. F#: hole names are inert to `apply`, `curry` and `compose` —
   every clause keys on `Addr`, and a bare `Name` selects nothing. *)
let hygiene_law (#node:Type) (rd:readers) (w:witness node) (f:string -> string) (a:args node)
                (slot_addr:string) (inner n:node)
  : Lemma (ensures apply rd (renamed f w) a n == apply rd w a n /\
                   curry rd (renamed f w) a n == curry rd w a n /\
                   compose (renamed f w) slot_addr inner n == compose w slot_addr inner n)
  = filter_rename f (w.holes n);
    guard_rename f (data_holes w n);
    addrs_rename f (data_holes w n);
    walk_rename rd w f true a n [] (data_holes w n);
    walk_rename rd w f false a n [] (data_holes w n);
    compose_rename w f slot_addr inner n

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
  k_hash:    string -> string;
  k_addr_le: string -> string -> bool;
  k_field:   string -> string
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

(* F#: `Capability.invocationKey`. *)
let invocation_key (kr:key_renderers) (c:capability) (a:invocation) : Tot string =
  c.c_id ^ "#" ^ kr.k_hash (key_canonical kr (sort_bindings kr a))

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
