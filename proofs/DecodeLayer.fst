(*
   DecodeLayer — an F* model of Fuaran.Core's typed decode layer and its codec combinators, with
   the building blocks a domain composes proved GENERICALLY in what they build (fuaran-core
   Phase 436).

   WHAT IS MODELLED, clause for clause, over `WireDecode.fst`'s `jval` (parametric in its two
   numeric carriers, as there):
     - `src/Fuaran.Core.Wire/DecodePath.fs` — `PathSegment` and `DecodePath.resolve` with its
       first-member `step`.
     - `src/Fuaran.Core.Wire/DecodeError.fs` — `DecodeCode`, `RootDecodeCode` (Phase 443),
       `DecodeError.make` / `under` / `within` / `resolvesIn`.
     - `src/Fuaran.Core.Wire/Decoder.fs` — the carrier view `DecodeCarrier<'V>` and the algebra
       written once over it, `DecodeOver` (Phase 640), modelled ONCE over any carrier (Phase 658,
       section 2b and 3a), and `Decoder` as its instance at `jval` (section 3b): every combinator
       but the two that are not over a value (`parse`, which starts from TEXT and is `Json.read`
       with its refusal lifted, and `describing`, which answers the sentence) and the two Phase
       509 helpers held elsewhere (`pair` and `strings`, `coverage-exclusions.json`): `succeed`,
       `fail`, `map`, `bind`, `andThen`, `wrongKind`, `missing`, `tryMember`, the six kind
       readers, `field` / `optField` / `fieldOr`, `at`, `mapListIndexed` / `list` /
       `boundedList` / `listAll`, `sequence` / `all`, `oneOf`, `tagDispatch` /
       `tagDispatchWith` / `kindDispatch`, `intRange`, `undeclared` / `members` / `closed`. The
       algebra's instance at the lossless document (`JDocDecoder`) is `JsonDocument.fst`'s.
     - `src/Fuaran.Core.Wire/Codec.fs` and `CodecDeclarationFault.fs` — `Codec<'T>`'s `Write` and
       `ReadAll`, `CodecDeclarationFault.under`, `make` / `ofDecoder` / `decoder`, the five
       leaves, `enum`, `list`, `map`, `refine`, `record` / `field` / `optField` / `build`,
       `case` / `union`, with every private helper they reach (`one`, `under`, `both`,
       `repeats`, `traverse`, `strictObject`).

   WHAT IS DROPPED, AND WHY — the model's boundary, recorded as the `decode-layer-model-bridge`
   row in `../proofs.json`:
     - A refusal's PROSE. `DecodeError` keeps its `Code` and its `Path`; `Expected` and `Message`
       are dropped, as `WireColumn.fst` drops its refusals' prose. Every theorem here is about a
       code and a path, and `tagDispatchWith` — which rewrites only `Message` — is therefore
       `tag_dispatch` in the model. The differential compares refusals by code and path. The
       carrier view's `KindName` goes with it: it is read only by `wrongKind`, for its detail.
     - The codec's `Schema` (and a field's `Properties` / `Required`), which is descriptive: "the
       decoder, not the schema, is the authority on acceptance" (Codec.fs).
     - The integers a decoder reads. `intRange lo hi` reads `lo <= i <= hi` over the opaque `num`
       carrier as a host predicate `in_range`; `float` takes the widening `to_flt` as a parameter,
       as `WireDecode.as_float` does. A path index and `boundedList`'s bound are F* `int`s, which
       extract to `Prims.int` and are bridged at the edge.
     - Production's two filter-then-map pipelines (`undeclared`) and its `List.mapi` /
       `List.choose` / `List.collect` passes are written as the direct recursions they equal.
     - The codec's text layer (`Codec.write`, `Codec.read`, `Codec.corpus`), which is
       `Canonical`'s, modelled by `WireCanon.fst`.

   WHAT IS PROVED, AND WHAT IS NOT. The ladder states both, once: the rows of `../proofs.json`
   whose `evidence.model` is this file, each with its `statement` and its `boundary`. `README.md`
   renders them in this model's section.

   HOW TO READ IT. Every definition names its F# counterpart in the comment above it. Helpers are
   defined here rather than taken from `FStar.List.Tot` so that the extracted oracle depends on
   `Prims` and `WireDecode` alone.

   Apache-2.0, like everything beside it.
*)
module DecodeLayer

open Twin
open ListAlgebra

module W = WireDecode

(* ======================================================================================
   1. Paths, codes, refusals (F#: `PathSegment` in DecodePath.fs, `DecodeCode` and
      `DecodeError` in DecodeError.fs) and the result they ride in.
   ====================================================================================== *)

(* F#: `PathSegment`. *)
type seg =
  | Key   : k:string -> seg
  | Index : i:int -> seg

(* F#: `DecodeCode`, in declaration order. *)
type code =
  | InvalidJson
  | MissingField
  | WrongKind
  | UnknownTag
  | OutOfRange
  | UndeclaredMember
  | LimitExceeded
  | NotAdmitted
  | SchemaFault

(* F#: `RootDecodeCode` (Phase 443), in declaration order: every `DecodeCode` but `InvalidJson`
   and `MissingField`. F* constructors share one namespace per module, so each carries an `R`
   prefix the F# cases do not. *)
type root_code =
  | RWrongKind
  | RUnknownTag
  | ROutOfRange
  | RUndeclaredMember
  | RLimitExceeded
  | RNotAdmitted
  | RSchemaFault

(* F#: the match inside `Decoder.fail`, a root code as the `DecodeCode` it names. *)
let root (c: root_code) : code =
  match c with
  | RWrongKind -> WrongKind
  | RUnknownTag -> UnknownTag
  | ROutOfRange -> OutOfRange
  | RUndeclaredMember -> UndeclaredMember
  | RLimitExceeded -> LimitExceeded
  | RNotAdmitted -> NotAdmitted
  | RSchemaFault -> SchemaFault

(* F#: `DecodeError`, its `Expected` and `Message` dropped (see the header). *)
type derr = { code: code; path: list seg }

(* F#: `Result<'T, 'E>`. *)
type result (a: Type0) (e: Type0) =
  | Ok    : v:a -> result a e
  | Error : err:e -> result a e

(* ---- list helpers, self-contained so the extraction needs only `Prims`, `ListAlgebra` and `WireDecode` ---- *)

(* F#: `List.map`. *)
let rec map_l (#a #b: Type0) (f: a -> b) (l: list a) : Tot (list b) =
  match l with
  | [] -> []
  | x :: t -> f x :: map_l f t

(* F#: `List.filter`. *)
let rec filter_l (#a: Type0) (p: a -> bool) (l: list a) : Tot (list a) =
  match l with
  | [] -> []
  | x :: t -> if p x then x :: filter_l p t else filter_l p t

(* F#: `List.collect`. *)
let rec concat_map (#a #b: Type0) (f: a -> list b) (l: list a) : Tot (list b) =
  match l with
  | [] -> []
  | x :: t -> app (f x) (concat_map f t)

(* F#: `List.map fst` over an association list. *)
let rec keys (#a: Type0) (l: list (string & a)) : Tot (list string) =
  match l with
  | [] -> []
  | (k, _) :: t -> k :: keys t

(* F#: `Result.mapError`. *)
let map_err (#a #e1 #e2: Type0) (f: e1 -> e2) (r: result a e1) : Tot (result a e2) =
  match r with
  | Ok v -> Ok v
  | Error e -> Error (f e)

(* ======================================================================================
   2. Paths into a document and the refusal's own operations (F#: DecodePath.fs and
      DecodeError.fs).
   ====================================================================================== *)

(* F#: `DecodeError.make` — a refusal at the value being decoded. *)
let make (c: code) : derr = { code = c; path = [] }

(* F#: `DecodeError.under` — one step further from the root. *)
let under (s: seg) (e: derr) : derr = { e with path = s :: e.path }

(* F#: `DecodeError.within` — under a whole prefix. *)
let within (p: list seg) (e: derr) : derr = { e with path = app p e.path }

(* F#: the `List.tryFind (fun (k, _) -> k = name) |> Option.map snd` every member read is — the
   FIRST member of the key, where a foreign document repeats one. *)
let rec memb (#a: Type0) (k: string) (fs: list (string & a)) : Tot (option a) =
  match fs with
  | [] -> None
  | (n, v) :: t -> if n = k then Some v else memb k t

(* F#: `List.tryItem`, at a non-negative index (every caller guards the sign first). *)
let rec try_item (#a: Type0) (i: int) (l: list a) : Tot (option a) (decreases l) =
  match l with
  | [] -> None
  | x :: t -> if i = 0 then Some x else try_item (i - 1) t

(* F#: `DecodePath.step` (private). *)
let step (#num #flt: eqtype) (s: seg) (v: W.jval num flt) : Tot (option (W.jval num flt)) =
  match s, v with
  | Key k, W.JObj fs -> memb k fs
  | Index i, W.JArr xs -> if i >= 0 then try_item i xs else None
  | _, _ -> None

(* F#: `DecodePath.resolve`. *)
let rec resolve (#num #flt: eqtype) (p: list seg) (doc: W.jval num flt)
  : Tot (option (W.jval num flt)) (decreases p) =
  match p with
  | [] -> Some doc
  | s :: rest ->
      (match step s doc with
       | Some v -> resolve rest v
       | None -> None)

(* F#: the `List.exists (fun (n, _) -> n = k)` inside `resolvesIn`. *)
let rec has_key (#a: Type0) (k: string) (fs: list (string & a)) : Tot bool =
  match fs with
  | [] -> false
  | (n, _) :: t -> n = k || has_key k t

(* F#: `DecodeError.resolvesIn` — the law every decoder refusal answers: an `InvalidJson` names
   the root; a `MissingField` names a member of a resolving object that does not carry it; every
   other code names a value the document holds. `List.rev` is `WireDecode.rev`. *)
let resolves_in (#num #flt: eqtype) (doc: W.jval num flt) (e: derr) : Tot bool =
  match e.code with
  | InvalidJson -> (match e.path with [] -> true | _ -> false)
  | MissingField ->
      (match W.rev e.path with
       | Key k :: parent_rev ->
           (match resolve (W.rev parent_rev) doc with
            | Some (W.JObj fields) -> not (has_key k fields)
            | _ -> false)
       | _ -> false)
  | _ ->
      (match resolve e.path doc with
       | Some _ -> true
       | None -> false)

(* ======================================================================================
   2b. The carrier view (F#: `DecodeCarrier<'V>`, Decoder.fs, Phase 640) and the path law read
       through it.

       Production writes the decode algebra ONCE over a carrier view (`DecodeOver`), and
       instantiates it at `JVal` (`Decoder`) and at the lossless document (`JDocDecoder`). The
       model does the same (Phase 658): `carrier` is the view, its `KindName` dropped with the
       prose (the header); `step_over` / `resolve_over` / `resolves_in_over` are
       `DecodePath.step`'s rule (the FIRST member of a key, a non-negative index of an array)
       read through the view. A carrier's own path law is its own specification —
       `resolve` / `resolves_in` here for `jval`, `JsonDocument.fst`'s `dresolve` /
       `d_resolves_in` for the document — and the one law between the two is FAITHFULNESS: the
       view's members and items are the carrier's own object and array. It is proved per carrier
       (`ok_at_is_over` here), never assumed, so no theorem of the algebra is conditional on it.
   ====================================================================================== *)

(* F#: `DecodeCarrier<'V>` — an object's members in authored order, an array's items, a
   string's text, each `None` for any other kind. *)
noeq type carrier (v: Type0) = {
  members_of: v -> option (list (string & v));
  items_of: v -> option (list v);
  text_of: v -> option string
}

(* `DecodePath.step`'s rule, through the view. *)
let step_over (#v: Type0) (c: carrier v) (s: seg) (x: v) : Tot (option v) =
  match s with
  | Key k -> (match c.members_of x with Some fs -> memb k fs | None -> None)
  | Index i -> (match c.items_of x with Some xs -> if i >= 0 then try_item i xs else None | None -> None)

(* `DecodePath.resolve`, through the view. *)
let rec resolve_over (#v: Type0) (c: carrier v) (p: list seg) (doc: v) : Tot (option v) (decreases p) =
  match p with
  | [] -> Some doc
  | s :: rest ->
      (match step_over c s doc with
       | Some x -> resolve_over c rest x
       | None -> None)

(* `DecodeError.resolvesIn`, through the view. *)
let resolves_in_over (#v: Type0) (c: carrier v) (doc: v) (e: derr) : Tot bool =
  match e.code with
  | InvalidJson -> (match e.path with [] -> true | _ -> false)
  | MissingField ->
      (match W.rev e.path with
       | Key k :: parent_rev ->
           (match resolve_over c (W.rev parent_rev) doc with
            | Some o -> (match c.members_of o with Some fields -> not (has_key k fields) | None -> false)
            | None -> false)
       | _ -> false)
  | _ ->
      (match resolve_over c e.path doc with
       | Some _ -> true
       | None -> false)

(* ======================================================================================
   3. The decoders. 3a is the algebra over a carrier (F#: `module internal DecodeOver`,
      Decoder.fs), clause for clause; 3b is `Decoder<'T>` and `module Decoder`, its instance at
      `jval`, with the readers that are `jval`'s own (`json`, `int`, `float`, `bool`) and the
      compositions `Decoder` spells itself (`fieldOr`, `list`, `kindDispatch`, `intRange`,
      `closed`). The document instance is `JsonDocument.fst`'s.
   ====================================================================================== *)

(* F#: `'V -> Result<'T, DecodeError>`, the decoder over a carrier. *)
let dec_over (v: Type0) (a: Type0) = v -> Tot (result a derr)

(* F#: `DecodeOver.fail` (its two phrases dropped), its code a `RootDecodeCode` since Phase 443. *)
let fail_over (#v #a: Type0) (c: root_code) : dec_over v a = fun _ -> Error (make (root c))

(* F#: `DecodeOver.map`. *)
let map_over (#v #a #b: Type0) (g: a -> b) (d: dec_over v a) : dec_over v b =
  fun el ->
    match d el with
    | Ok x -> Ok (g x)
    | Error e -> Error e

(* F#: `DecodeOver.bind` — the second decoder reads the SAME value. *)
let bind_over (#v #a #b: Type0) (g: a -> dec_over v b) (d: dec_over v a) : dec_over v b =
  fun el ->
    match d el with
    | Ok x -> g x el
    | Error e -> Error e

(* F#: `DecodeOver.andThen` (its function's two phrases dropped) — since Phase 453 `g` refuses
   with a `RootDecodeCode`, and the refusal is raised AT the value, as `fail` raises its own. *)
let and_then_over (#v #a #b: Type0) (g: a -> result b root_code) (d: dec_over v a) : dec_over v b =
  fun el ->
    match d el with
    | Ok x -> (match g x with Ok u -> Ok u | Error c -> Error (make (root c)))
    | Error e -> Error e

(* F#: `DecodeOver.wrongKind` — its `found` names a kind in prose only. *)
let wrong_kind_over (#v: Type0) (c: carrier v) (found: v) : derr = make WrongKind

(* F#: `DecodeOver.missing` — the path NAMES the absent member. *)
let missing (name: string) : derr = { code = MissingField; path = [Key name] }

(* F#: `DecodeOver.tryMember`. *)
let try_member_over (#v: Type0) (c: carrier v) (name: string) (el: v) : Tot (option v) =
  match c.members_of el with
  | Some fields -> memb name fields
  | None -> None

(* F#: `DecodeOver.text`. *)
let text_over (#v: Type0) (c: carrier v) : dec_over v string =
  fun el ->
    match c.text_of el with
    | Some s -> Ok s
    | None -> Error (wrong_kind_over c el)

(* F#: `DecodeOver.items`. *)
let items_over (#v: Type0) (c: carrier v) : dec_over v (list v) =
  fun el ->
    match c.items_of el with
    | Some xs -> Ok xs
    | None -> Error (wrong_kind_over c el)

(* F#: `DecodeOver.members` — the object reader (`Decoder.obj`). *)
let members_over (#v: Type0) (c: carrier v) : dec_over v (list (string & v)) =
  fun el ->
    match c.members_of el with
    | Some fields -> Ok fields
    | None -> Error (wrong_kind_over c el)

(* F#: `DecodeOver.field`. *)
let field_over (#v #a: Type0) (c: carrier v) (name: string) (d: dec_over v a) : dec_over v a =
  fun el ->
    match c.members_of el with
    | Some _ ->
        (match try_member_over c name el with
         | Some x -> map_err (under (Key name)) (d x)
         | None -> Error (missing name))
    | None -> Error (wrong_kind_over c el)

(* F#: `DecodeOver.optField` — absent is `Ok None`, present-and-refused the refusal. *)
let opt_field_over (#v #a: Type0) (c: carrier v) (name: string) (d: dec_over v a) : dec_over v (option a) =
  fun el ->
    match c.members_of el with
    | Some _ ->
        (match try_member_over c name el with
         | Some x ->
             (match d x with
              | Ok y -> Ok (Some y)
              | Error e -> Error (under (Key name) e))
         | None -> Ok None)
    | None -> Error (wrong_kind_over c el)

(* F#: `DecodeOver.at`'s `go`. *)
let rec at_go_over (#v #a: Type0) (c: carrier v) (d: dec_over v a) (done_rev rest: list seg) (x: v)
  : Tot (result a derr) (decreases rest) =
  let here = W.rev done_rev in
  match rest with
  | [] -> map_err (within here) (d x)
  | Key k :: tail ->
      (match c.members_of x with
       | Some _ ->
           (match try_member_over c k x with
            | Some y -> at_go_over c d (Key k :: done_rev) tail y
            | None -> Error (within here (missing k)))
       | None -> Error (within here (wrong_kind_over c x)))
  | Index i :: tail ->
      (match c.items_of x with
       | Some xs ->
           (match (if i >= 0 then try_item i xs else None) with
            | Some y -> at_go_over c d (Index i :: done_rev) tail y
            | None -> Error (within here (make OutOfRange)))
       | None -> Error (within here (wrong_kind_over c x)))

(* F#: `DecodeOver.at`. *)
let at_over (#v #a: Type0) (c: carrier v) (path: list seg) (d: dec_over v a) : dec_over v a =
  fun el -> at_go_over c d [] path el

(* F#: `DecodeOver.mapListIndexed`'s `go`. *)
let rec mli_go (#v #a: Type0) (g: int -> dec_over v a) (i: int) (acc: list a) (xs: list v)
  : Tot (result (list a) derr) (decreases xs) =
  match xs with
  | [] -> Ok (W.rev acc)
  | x :: rest ->
      (match g i x with
       | Ok y -> mli_go g (i + 1) (y :: acc) rest
       | Error e -> Error (under (Index i) e))

(* F#: `DecodeOver.mapListIndexed`. *)
let map_list_indexed_over (#v #a: Type0) (c: carrier v) (g: int -> dec_over v a) : dec_over v (list a) =
  fun el ->
    match c.items_of el with
    | Some xs -> mli_go g 0 [] xs
    | None -> Error (wrong_kind_over c el)

(* F#: `DecodeOver.boundedList` — the bound checked before any item is read. *)
let bounded_list_over (#v #a: Type0) (c: carrier v) (max_items: int) (d: dec_over v a) : dec_over v (list a) =
  fun el ->
    match c.items_of el with
    | Some xs ->
        if len xs > max_items then Error (make LimitExceeded)
        else map_list_indexed_over c (fun _ -> d) el
    | None -> map_list_indexed_over c (fun _ -> d) el

(* F#: `errorsAndOks`'s `List.choose` of the refusals. *)
let rec errors_of (#a: Type0) (rs: list (result a derr)) : Tot (list derr) =
  match rs with
  | [] -> []
  | Error e :: t -> e :: errors_of t
  | Ok _ :: t -> errors_of t

(* F#: `errorsAndOks`'s `List.choose` of the answers. *)
let rec oks_of (#a #e: Type0) (rs: list (result a e)) : Tot (list a) =
  match rs with
  | [] -> []
  | Ok v :: t -> v :: oks_of t
  | Error _ :: t -> oks_of t

(* F#: `listAll`'s `List.mapi`. *)
let rec mapi_d (#v #a: Type0) (d: dec_over v a) (i: int) (xs: list v)
  : Tot (list (result a derr)) (decreases xs) =
  match xs with
  | [] -> []
  | x :: t -> map_err (under (Index i)) (d x) :: mapi_d d (i + 1) t

(* F#: `DecodeOver.listAll` — every refusal, at its item's index. *)
let list_all_over (#v #a: Type0) (c: carrier v) (d: dec_over v a) (el: v)
  : Tot (result (list a) (list derr)) =
  match c.items_of el with
  | Some xs ->
      let rs = mapi_d d 0 xs in
      (match errors_of rs with
       | [] -> Ok (oks_of rs)
       | errs -> Error errs)
  | None -> Error [wrong_kind_over c el]

(* F#: `DecodeOver.sequence`'s `go`. *)
let rec seq_go (#v #a: Type0) (el: v) (acc: list a) (ds: list (dec_over v a))
  : Tot (result (list a) derr) (decreases ds) =
  match ds with
  | [] -> Ok (W.rev acc)
  | d :: rest ->
      (match d el with
       | Ok x -> seq_go el (x :: acc) rest
       | Error e -> Error e)

(* F#: `DecodeOver.sequence`. *)
let sequence_over (#v #a: Type0) (ds: list (dec_over v a)) : dec_over v (list a) =
  fun el -> seq_go el [] ds

(* F#: `DecodeOver.all`'s `List.map (fun d -> d el)`. *)
let rec run_all (#v #a: Type0) (ds: list (dec_over v a)) (el: v) : Tot (list (result a derr)) =
  match ds with
  | [] -> []
  | d :: t -> d el :: run_all t el

(* F#: `DecodeOver.all`. *)
let all_over (#v #a: Type0) (ds: list (dec_over v a)) (el: v) : Tot (result (list a) (list derr)) =
  let rs = run_all ds el in
  match errors_of rs with
  | [] -> Ok (oks_of rs)
  | errs -> Error errs

(* F#: the `List.tryFind (fun (k, _) -> k = s)` over a case table. *)
let rec find_case (#a: Type0) (s: string) (cases: list (string & a)) : Tot (option a) =
  match cases with
  | [] -> None
  | (k, v) :: t -> if k = s then Some v else find_case s t

(* F#: `DecodeOver.oneOf`. *)
let one_of_over (#v #a: Type0) (c: carrier v) (cases: list (string & a)) : dec_over v a =
  fun el ->
    match c.text_of el with
    | Some s ->
        (match find_case s cases with
         | Some x -> Ok x
         | None -> Error (make UnknownTag))
    | None -> Error (wrong_kind_over c el)

(* F#: `DecodeOver.tagDispatch` — the case reads the SAME object. *)
let tag_dispatch_over (#v #a: Type0) (c: carrier v) (key: string) (cases: list (string & dec_over v a))
  : dec_over v a =
  fun el ->
    match field_over c key (text_over c) el with
    | Error e -> Error e
    | Ok t ->
        (match find_case t cases with
         | Some d -> d el
         | None -> Error (under (Key key) (make UnknownTag)))

(* F#: `DecodeOver.undeclared`'s filter-then-map, fused. *)
let rec undeclared_go (#v: Type0) (known: list string) (fields: list (string & v)) : Tot (list derr) =
  match fields with
  | [] -> []
  | (k, _) :: t ->
      if mem k known then undeclared_go known t
      else under (Key k) (make UndeclaredMember) :: undeclared_go known t

(* F#: `DecodeOver.undeclared`. *)
let undeclared_over (#v: Type0) (c: carrier v) (known: list string) (el: v) : Tot (list derr) =
  match c.members_of el with
  | Some fields -> undeclared_go known fields
  | None -> []

(* F#: `DecodeOver.membersOnly` — the strict policy (`Decoder.members`). *)
let members_only_over (#v: Type0) (c: carrier v) (known: list string) : dec_over v unit =
  fun el ->
    match undeclared_over c known el with
    | [] -> Ok ()
    | e :: _ -> Error e

(* ---- 3b. `Decoder`, the instance at `jval` ---- *)

(* F#: `Decoder<'T> = JVal -> Result<'T, DecodeError>`. *)
let dec (num flt: eqtype) (a: Type0) = dec_over (W.jval num flt) a

(* F#: `Decoder.carrier` (Phase 640), its three views. *)
let jmembers (#num #flt: eqtype) (x: W.jval num flt) : Tot (option (list (string & W.jval num flt))) =
  match x with
  | W.JObj fields -> Some fields
  | _ -> None

let jitems (#num #flt: eqtype) (x: W.jval num flt) : Tot (option (list (W.jval num flt))) =
  match x with
  | W.JArr xs -> Some xs
  | _ -> None

let jtext (#num #flt: eqtype) (x: W.jval num flt) : Tot (option string) =
  match x with
  | W.JStr s -> Some s
  | _ -> None

let jcarrier (#num #flt: eqtype) : carrier (W.jval num flt) =
  { members_of = jmembers; items_of = jitems; text_of = jtext }

(* F#: `Decoder.succeed`. *)
let succeed (#num #flt: eqtype) (#a: Type0) (v: a) : dec num flt a = fun _ -> Ok v

(* F#: `Decoder.fail`. *)
let fail (#num #flt: eqtype) (#a: Type0) (c: root_code) : dec num flt a = fun el -> fail_over c el

(* F#: `Decoder.map`. *)
let map (#num #flt: eqtype) (#a #b: Type0) (g: a -> b) (d: dec num flt a) : dec num flt b =
  fun el -> map_over g d el

(* F#: `Decoder.bind`. *)
let bind (#num #flt: eqtype) (#a #b: Type0) (g: a -> dec num flt b) (d: dec num flt a) : dec num flt b =
  fun el -> bind_over g d el

(* F#: `Decoder.andThen`. *)
let and_then (#num #flt: eqtype) (#a #b: Type0) (g: a -> result b root_code) (d: dec num flt a) : dec num flt b =
  fun el -> and_then_over g d el

(* F#: `Decoder.wrongKind`. *)
let wrong_kind (#num #flt: eqtype) (found: W.jval num flt) : derr = wrong_kind_over jcarrier found

(* F#: `Decoder.tryMember`. *)
let try_member (#num #flt: eqtype) (name: string) (el: W.jval num flt) : Tot (option (W.jval num flt)) =
  try_member_over jcarrier name el

(* F#: `Decoder.json`. *)
let json (#num #flt: eqtype) : dec num flt (W.jval num flt) = fun el -> Ok el

(* F#: `Decoder.str`. *)
let str (#num #flt: eqtype) : dec num flt string = fun el -> text_over jcarrier el

(* F#: `Decoder.int`. *)
let int_d (#num #flt: eqtype) : dec num flt num =
  fun el ->
    match el with
    | W.JInt i -> Ok i
    | other -> Error (wrong_kind other)

(* F#: `Decoder.float`, the widening `float i` a parameter. *)
let float_d (#num #flt: eqtype) (to_flt: num -> flt) : dec num flt flt =
  fun el ->
    match el with
    | W.JFloat f -> Ok f
    | W.JInt i -> Ok (to_flt i)
    | other -> Error (wrong_kind other)

(* F#: `Decoder.bool`. *)
let bool_d (#num #flt: eqtype) : dec num flt bool =
  fun el ->
    match el with
    | W.JBool b -> Ok b
    | other -> Error (wrong_kind other)

(* F#: `Decoder.items`. *)
let items (#num #flt: eqtype) : dec num flt (list (W.jval num flt)) = fun el -> items_over jcarrier el

(* F#: `Decoder.obj`. *)
let obj (#num #flt: eqtype) : dec num flt (list (string & W.jval num flt)) = fun el -> members_over jcarrier el

(* F#: `Decoder.field`. *)
let field (#num #flt: eqtype) (#a: Type0) (name: string) (d: dec num flt a) : dec num flt a =
  fun el -> field_over jcarrier name d el

(* F#: `Decoder.optField`. *)
let opt_field (#num #flt: eqtype) (#a: Type0) (name: string) (d: dec num flt a) : dec num flt (option a) =
  fun el -> opt_field_over jcarrier name d el

(* F#: `Decoder.fieldOr`. *)
let field_or (#num #flt: eqtype) (#a: Type0) (name: string) (fallback: a) (d: dec num flt a) : dec num flt a =
  map (fun (o: option a) -> match o with Some x -> x | None -> fallback) (opt_field name d)

(* F#: `Decoder.at`. *)
let at (#num #flt: eqtype) (#a: Type0) (path: list seg) (d: dec num flt a) : dec num flt a =
  fun el -> at_over jcarrier path d el

(* F#: `Decoder.mapListIndexed`. *)
let map_list_indexed (#num #flt: eqtype) (#a: Type0) (g: int -> dec num flt a) : dec num flt (list a) =
  fun el -> map_list_indexed_over jcarrier g el

(* F#: `Decoder.list`. *)
let list_d (#num #flt: eqtype) (#a: Type0) (d: dec num flt a) : dec num flt (list a) =
  map_list_indexed (fun _ -> d)

(* F#: `Decoder.boundedList`. *)
let bounded_list (#num #flt: eqtype) (#a: Type0) (max_items: int) (d: dec num flt a) : dec num flt (list a) =
  fun el -> bounded_list_over jcarrier max_items d el

(* F#: `Decoder.listAll`. *)
let list_all (#num #flt: eqtype) (#a: Type0) (d: dec num flt a) (el: W.jval num flt)
  : Tot (result (list a) (list derr)) =
  list_all_over jcarrier d el

(* F#: `Decoder.sequence`. *)
let sequence (#num #flt: eqtype) (#a: Type0) (ds: list (dec num flt a)) : dec num flt (list a) =
  fun el -> sequence_over ds el

(* F#: `Decoder.all`. *)
let all_d (#num #flt: eqtype) (#a: Type0) (ds: list (dec num flt a)) (el: W.jval num flt)
  : Tot (result (list a) (list derr)) =
  all_over ds el

(* F#: `Decoder.oneOf`. *)
let one_of (#num #flt: eqtype) (#a: Type0) (cases: list (string & a)) : dec num flt a =
  fun el -> one_of_over jcarrier cases el

(* F#: `Decoder.tagDispatch`. *)
let tag_dispatch (#num #flt: eqtype) (#a: Type0) (key: string) (cases: list (string & dec num flt a))
  : dec num flt a =
  fun el -> tag_dispatch_over jcarrier key cases el

(* F#: `Decoder.tagDispatchWith` — production rewrites the unknown tag's MESSAGE and nothing
   else, so with the prose dropped it is `tagDispatch` (the differential holds it to that). *)
let tag_dispatch_with (#num #flt: eqtype) (#a: Type0) (what: string) (key: string)
  (cases: list (string & dec num flt a)) : dec num flt a =
  fun el -> tag_dispatch key cases el

(* F#: `Decoder.kindDispatch`. *)
let kind_dispatch (#num #flt: eqtype) (#a: Type0) (cases: list (string & dec num flt a)) : dec num flt a =
  tag_dispatch "kind" cases

(* F#: `Decoder.intRange lo hi`, its `i >= lo && i <= hi` the host predicate `in_range`. *)
let int_range (#num #flt: eqtype) (in_range: num -> bool) : dec num flt num =
  fun el ->
    match int_d el with
    | Ok i -> if in_range i then Ok i else Error (make OutOfRange)
    | Error e -> Error e

(* F#: `Decoder.undeclared`. *)
let undeclared (#num #flt: eqtype) (known: list string) (el: W.jval num flt) : Tot (list derr) =
  undeclared_over jcarrier known el

(* F#: `Decoder.members`. *)
let members (#num #flt: eqtype) (known: list string) : dec num flt unit =
  fun el -> members_only_over jcarrier known el

(* F#: `Decoder.closed`. *)
let closed (#num #flt: eqtype) (#a: Type0) (known: list string) (d: dec num flt a) : dec num flt a =
  bind (fun () -> d) (members known)

(* ======================================================================================
   4. The codecs (F#: `Codec<'T>`, `CodecFields`, `CodecCase` and `module Codec`, Codec.fs;
      `CodecDeclarationFault`, CodecDeclarationFault.fs), clause for clause.
   ====================================================================================== *)

(* F#: `CodecDeclarationFault`. *)
type fault =
  | RepeatedSpelling      : spelling:string -> fault
  | RepeatedMember        : member_name:string -> in_case:option string -> fault
  | RepeatedTag           : repeated_tag:string -> fault
  | DiscriminatorAsMember : case_tag:string -> discriminator:string -> fault
  | Unrecognised          : at_path:list seg -> fault

(* F#: `CodecDeclarationFault.under`. *)
let fault_under (s: seg) (f: fault) : fault =
  match f with
  | Unrecognised p -> Unrecognised (s :: p)
  | other -> other

(* F#: `Codec<'T>`, its `Schema` dropped. *)
noeq type codec (num flt: eqtype) (a: Type0) = {
  write: a -> result (W.jval num flt) fault;
  read_all: W.jval num flt -> result a (list derr)
}

(* F#: `Codec.one` (private). *)
let one (#a: Type0) (r: result a derr) : Tot (result a (list derr)) =
  match r with
  | Ok v -> Ok v
  | Error e -> Error [e]

(* F#: `Codec.under` (private). *)
let under_all (#a: Type0) (s: seg) (r: result a (list derr)) : Tot (result a (list derr)) =
  match r with
  | Ok v -> Ok v
  | Error es -> Error (map_l (under s) es)

(* F#: `Codec.both` (private). *)
let both (#a #b: Type0) (fr: result (a -> b) (list derr)) (ar: result a (list derr))
  : Tot (result b (list derr)) =
  match fr, ar with
  | Ok g, Ok x -> Ok (g x)
  | Error e, Ok _ -> Error e
  | Ok _, Error e -> Error e
  | Error e1, Error e2 -> Error (app e1 e2)

(* F#: `Codec.make`. *)
let c_make (#num #flt: eqtype) (#a: Type0) (w: a -> W.jval num flt) (r: W.jval num flt -> result a (list derr))
  : codec num flt a =
  { write = (fun v -> Ok (w v)); read_all = r }

(* F#: `Codec.ofDecoder`. *)
let c_of_decoder (#num #flt: eqtype) (#a: Type0) (w: a -> W.jval num flt) (d: dec num flt a) : codec num flt a =
  c_make w (fun j -> one (d j))

(* F#: `Codec.decoder` — the strict reader. *)
let c_decoder (#num #flt: eqtype) (#a: Type0) (c: codec num flt a) : dec num flt a =
  fun j ->
    match c.read_all j with
    | Ok v -> Ok v
    | Error (e :: _) -> Error e
    | Error [] -> Error (make SchemaFault)

(* F#: `Codec.json`, `string`, `int`, `float`, `bool`. *)
let c_json (#num #flt: eqtype) : codec num flt (W.jval num flt) = c_make (fun j -> j) (fun j -> Ok j)
let c_string (#num #flt: eqtype) : codec num flt string = c_of_decoder (fun s -> W.JStr s) str
let c_int (#num #flt: eqtype) : codec num flt num = c_of_decoder (fun i -> W.JInt i) int_d
let c_float (#num #flt: eqtype) (to_flt: num -> flt) : codec num flt flt =
  c_of_decoder (fun f -> W.JFloat f) (float_d to_flt)
let c_bool (#num #flt: eqtype) : codec num flt bool = c_of_decoder (fun b -> W.JBool b) bool_d

(* F#: `Codec.repeats` (private) — every name occurring more than once, each once, in the order
   it first occurs: `List.countBy id |> List.filter (n > 1) |> List.map fst`. `countBy` keys in
   first-occurrence order, which is `dedup_go`; `n > 1` is `occurs_twice`. *)
let rec occurs_twice (x: string) (l: list string) : Tot bool =
  match l with
  | [] -> false
  | y :: t -> if y = x then mem x t else occurs_twice x t

let rec dedup_go (seen: list string) (l: list string) : Tot (list string) (decreases l) =
  match l with
  | [] -> []
  | x :: t -> if mem x seen then dedup_go seen t else x :: dedup_go (x :: seen) t

let repeats (names: list string) : Tot (list string) =
  filter_l (fun x -> occurs_twice x names) (dedup_go [] names)

(* F#: `enum`'s `List.tryFind (fun (_, x) -> x = v)`. *)
let rec find_spelling (#a: eqtype) (v: a) (cases: list (string & a)) : Tot (option string) =
  match cases with
  | [] -> None
  | (s, x) :: t -> if x = v then Some s else find_spelling v t

(* F#: `Codec.enum`. *)
let c_enum (#num #flt: eqtype) (#a: eqtype) (cases: list (string & a))
  : Tot (result (codec num flt a) (list fault)) =
  let repeated = repeats (keys cases) in
  match repeated with
  | [] ->
      Ok { write = (fun v ->
                      match find_spelling v cases with
                      | Some s -> Ok (W.JStr s)
                      | None -> Error (Unrecognised []));
           read_all = (fun j -> one (one_of cases j)) }
  | _ -> Error (map_l (fun s -> RepeatedSpelling s) repeated)

(* F#: `Codec.traverse` (private). *)
let rec traverse_go (#a #b: Type0) (w: int -> a -> result b fault) (i: int) (acc: list b) (xs: list a)
  : Tot (result (list b) fault) (decreases xs) =
  match xs with
  | [] -> Ok (W.rev acc)
  | x :: rest ->
      (match w i x with
       | Ok y -> traverse_go w (i + 1) (y :: acc) rest
       | Error f -> Error f)

let traverse (#a #b: Type0) (w: int -> a -> result b fault) (xs: list a) : Tot (result (list b) fault) =
  traverse_go w 0 [] xs

(* F#: `Codec.list`'s `List.mapi`. *)
let rec mapi_all (#num #flt: eqtype) (#a: Type0) (r: W.jval num flt -> result a (list derr)) (i: int)
  (xs: list (W.jval num flt))
  : Tot (list (result a (list derr))) (decreases xs) =
  match xs with
  | [] -> []
  | x :: t -> under_all (Index i) (r x) :: mapi_all r (i + 1) t

(* F#: `Codec.list`'s `List.collect`. *)
let rec collect_errors (#a: Type0) (rs: list (result a (list derr))) : Tot (list derr) =
  match rs with
  | [] -> []
  | Ok _ :: t -> collect_errors t
  | Error es :: t -> app es (collect_errors t)

(* F#: `Codec.list`'s item writer, `fun i x -> c.Write x |> Result.mapError (CodecDeclarationFault.under (Index i))`. *)
let item_write (#num #flt: eqtype) (#a: Type0) (c: codec num flt a) (i: int) (x: a) : Tot (result (W.jval num flt) fault) =
  map_err (fault_under (Index i)) (c.write x)

(* F#: `Codec.list`. *)
let c_list (#num #flt: eqtype) (#a: Type0) (c: codec num flt a) : codec num flt (list a) =
  { write = (fun xs ->
               match traverse (item_write c) xs with
               | Ok js -> Ok (W.JArr js)
               | Error f -> Error f);
    read_all = (fun j ->
                  match j with
                  | W.JArr xs ->
                      let rs = mapi_all c.read_all 0 xs in
                      let errors = collect_errors rs in
                      (match errors with
                       | [] -> Ok (oks_of rs)
                       | _ -> Error errors)
                  | other -> Error [wrong_kind other]) }

(* F#: `Codec.map`. *)
let c_map (#num #flt: eqtype) (#a #b: Type0) (there: a -> b) (back: b -> a) (c: codec num flt a)
  : codec num flt b =
  { write = (fun u -> c.write (back u));
    read_all = (fun j ->
                  match c.read_all j with
                  | Ok v -> Ok (there v)
                  | Error es -> Error es) }

(* F#: `Codec.refine` — `check`'s refusal is `OutOfRange` at the value (its sentence dropped). *)
let c_refine (#num #flt: eqtype) (#a #b: Type0) (expected: string) (check: a -> result b string)
  (back: b -> a) (c: codec num flt a)
  : codec num flt b =
  { write = (fun u -> c.write (back u));
    read_all = (fun j ->
                  match c.read_all j with
                  | Ok v ->
                      (match check v with
                       | Ok u -> Ok u
                       | Error _ -> Error [make OutOfRange])
                  | Error es -> Error es) }

(* F#: `CodecFields<'R, 'C>`, its `Properties` and `Required` dropped. *)
noeq type fields (num flt: eqtype) (r c: Type0) = {
  write_fields: r -> result (list (string & W.jval num flt)) fault;
  read_fields: list (string & W.jval num flt) -> result c (list derr);
  names: list string
}

(* F#: `Codec.record`. *)
let c_record (#num #flt: eqtype) (#r #c: Type0) (ctor: c) : fields num flt r c =
  { write_fields = (fun _ -> Ok []); read_fields = (fun _ -> Ok ctor); names = [] }

(* F#: `Codec.field` — REQUIRED: absent is `MissingField`. *)
let c_field (#num #flt: eqtype) (#r #a #c: Type0) (name: string) (get: r -> a) (cd: codec num flt a)
  (fs: fields num flt r (a -> c))
  : fields num flt r c =
  { write_fields = (fun x ->
                      match fs.write_fields x with
                      | Ok before ->
                          (match cd.write (get x) with
                           | Ok j -> Ok (app before [(name, j)])
                           | Error f -> Error (fault_under (Key name) f))
                      | Error f -> Error f);
    read_fields = (fun ms ->
                     let this =
                       match memb name ms with
                       | Some v -> under_all (Key name) (cd.read_all v)
                       | None -> Error [missing name] in
                     both (fs.read_fields ms) this);
    names = app fs.names [name] }

(* F#: `Codec.optField` — written only when `get` answers `Some`, read as `None` when absent. *)
let c_opt_field (#num #flt: eqtype) (#r #a #c: Type0) (name: string) (get: r -> option a)
  (cd: codec num flt a) (fs: fields num flt r (option a -> c))
  : fields num flt r c =
  { write_fields = (fun x ->
                      match get x with
                      | Some y ->
                          (match fs.write_fields x with
                           | Ok before ->
                               (match cd.write y with
                                | Ok j -> Ok (app before [(name, j)])
                                | Error f -> Error (fault_under (Key name) f))
                           | Error f -> Error f)
                      | None -> fs.write_fields x);
    read_fields = (fun ms ->
                     let this =
                       match memb name ms with
                       | Some v ->
                           (match under_all (Key name) (cd.read_all v) with
                            | Ok y -> Ok (Some y)
                            | Error es -> Error es)
                       | None -> Ok None in
                     both (fs.read_fields ms) this);
    names = app fs.names [name] }

(* F#: `Codec.strictObject` (private) — undeclared members first, then the read's own. *)
let strict_object (#num #flt: eqtype) (#a: Type0) (names: list string) (j: W.jval num flt)
  (read: list (string & W.jval num flt) -> result a (list derr))
  : Tot (result a (list derr)) =
  match j with
  | W.JObj ms ->
      (match undeclared names j, read ms with
       | [], r -> r
       | u, Ok _ -> Error u
       | u, Error e -> Error (app u e))
  | other -> Error [wrong_kind other]

(* F#: `Codec.build`. *)
let c_build (#num #flt: eqtype) (#r: Type0) (fs: fields num flt r r) : Tot (result (codec num flt r) (list fault)) =
  let repeated = repeats fs.names in
  match repeated with
  | [] ->
      Ok { write = (fun x ->
                      match fs.write_fields x with
                      | Ok ws -> Ok (W.JObj ws)
                      | Error f -> Error f);
           read_all = (fun j -> strict_object fs.names j fs.read_fields) }
  | _ -> Error (map_l (fun n -> RepeatedMember n None) repeated)

(* F#: `CodecCase<'T>`, its `CaseProperties` and `CaseRequired` dropped. *)
noeq type ccase (num flt: eqtype) (t: Type0) = {
  ctag: string;
  write_case: t -> option (result (list (string & W.jval num flt)) fault);
  read_case: list (string & W.jval num flt) -> result t (list derr);
  case_names: list string
}

(* F#: `Codec.case`. *)
let c_case (#num #flt: eqtype) (#t #p: Type0) (tag: string) (project: t -> option p) (inject: p -> t)
  (fs: fields num flt p p)
  : ccase num flt t =
  { ctag = tag;
    write_case = (fun v ->
                    match project v with
                    | Some x -> Some (fs.write_fields x)
                    | None -> None);
    read_case = (fun ms ->
                   match fs.read_fields ms with
                   | Ok x -> Ok (inject x)
                   | Error e -> Error e);
    case_names = fs.names }

(* F#: `cases |> List.map _.Tag`. *)
let rec tags (#num #flt: eqtype) (#t: Type0) (cases: list (ccase num flt t)) : Tot (list string) =
  match cases with
  | [] -> []
  | c :: rest -> c.ctag :: tags rest

(* F#: `union`'s fault comprehension, in its three passes. *)
let union_faults (#num #flt: eqtype) (#t: Type0) (key: string) (cases: list (ccase num flt t)) : Tot (list fault) =
  app (concat_map (fun (c: ccase num flt t) -> map_l (fun n -> RepeatedMember n (Some c.ctag)) (repeats c.case_names)) cases)
      (app (map_l (fun g -> RepeatedTag g) (repeats (tags cases)))
           (concat_map (fun (c: ccase num flt t) ->
                          if mem key c.case_names then [DiscriminatorAsMember c.ctag key] else []) cases))

(* F#: `union`'s dispatch table, `[ for c in cases -> c.Tag, (fun _ -> Ok c) ]`. *)
let rec case_table (#num #flt: eqtype) (#t: Type0) (cases: list (ccase num flt t))
  : Tot (list (string & dec num flt (ccase num flt t))) =
  match cases with
  | [] -> []
  | c :: rest -> (c.ctag, succeed c) :: case_table rest

(* F#: `union`'s `tryWrite`'s `List.tryPick` — the FIRST case that recognises the value, its tag
   written first. *)
let rec try_write_cases (#num #flt: eqtype) (#t: Type0) (key: string) (cases: list (ccase num flt t)) (v: t)
  : Tot (option (result (W.jval num flt) fault)) =
  match cases with
  | [] -> None
  | c :: rest ->
      (match c.write_case v with
       | Some r ->
           Some (match r with
                 | Ok fs -> Ok (W.JObj ((key, W.JStr c.ctag) :: fs))
                 | Error f -> Error f)
       | None -> try_write_cases key rest v)

(* F#: `union`'s `readAll`. *)
let union_read (#num #flt: eqtype) (#t: Type0) (key: string) (cases: list (ccase num flt t)) (j: W.jval num flt)
  : Tot (result t (list derr)) =
  match tag_dispatch key (case_table cases) j with
  | Error e -> Error [e]
  | Ok c -> strict_object (key :: c.case_names) j c.read_case

(* F#: `Codec.union`. *)
let c_union (#num #flt: eqtype) (#t: Type0) (key: string) (cases: list (ccase num flt t))
  : Tot (result (codec num flt t) (list fault)) =
  match union_faults key cases with
  | [] ->
      Ok { write = (fun v ->
                      match try_write_cases key cases v with
                      | Some r -> r
                      | None -> Error (Unrecognised []));
           read_all = union_read key cases }
  | faults -> Error faults

(* ======================================================================================
   5. What a refusal owes the document (theorem 3's statement) and the transport lemma.

      `ok_at x e`: the refusal `e`, raised over the value `x`, RESOLVES there
      (`DecodeError.resolvesIn`) and is not `InvalidJson` — the parser's code, which names only
      a root and so cannot survive a step (`root_invalid_json_stops_resolving`). A decoder is
      `sound` when every refusal it answers over every value is `ok_at` that value; a collecting
      reader is `sound_all` when every refusal list it answers is NON-EMPTY and every member of
      it is `ok_at` the value (the non-emptiness is theorem 2's half, section 7). PROOF-ONLY.

      5a states the same over a carrier (`ok_at_over`, `sound_over`, ...), with the transport
      lemma proved once there; 5b is the faithfulness of `jcarrier` (`ok_at_is_over`), which
      makes every statement over `jval` below the generic one at `jcarrier`.
   ====================================================================================== *)

[@@ noextract_to "FSharp"]
let ok_at (#num #flt: eqtype) (x: W.jval num flt) (e: derr) : bool =
  e.code <> InvalidJson && resolves_in x e

[@@ noextract_to "FSharp"]
let good_res (#num #flt: eqtype) (#a: Type0) (x: W.jval num flt) (r: result a derr) : bool =
  match r with
  | Ok _ -> true
  | Error e -> ok_at x e

[@@ noextract_to "FSharp"]
let sound (#num #flt: eqtype) (#a: Type0) (d: dec num flt a) : prop =
  forall (x: W.jval num flt). good_res x (d x)

[@@ noextract_to "FSharp"]
let rec all_ok (#num #flt: eqtype) (x: W.jval num flt) (es: list derr) : Tot bool =
  match es with
  | [] -> true
  | e :: t -> ok_at x e && all_ok x t

[@@ noextract_to "FSharp"]
let good_all (#num #flt: eqtype) (#a: Type0) (x: W.jval num flt) (r: result a (list derr)) : bool =
  match r with
  | Ok _ -> true
  | Error es -> (match es with [] -> false | _ -> all_ok x es)

[@@ noextract_to "FSharp"]
let sound_all (#num #flt: eqtype) (#a: Type0) (rd: W.jval num flt -> result a (list derr)) : prop =
  forall (x: W.jval num flt). good_all x (rd x)

(* A record's or a case's members are read from the object's member list: every refusal is owed
   to the OBJECT. *)
[@@ noextract_to "FSharp"]
let fields_sound (#num #flt: eqtype) (#r #c: Type0) (fs: fields num flt r c) : prop =
  forall (ms: list (string & W.jval num flt)). good_all (W.JObj ms) (fs.read_fields ms)

[@@ noextract_to "FSharp"]
let case_sound (#num #flt: eqtype) (#t: Type0) (cc: ccase num flt t) : prop =
  forall (ms: list (string & W.jval num flt)). good_all (W.JObj ms) (cc.read_case ms)

[@@ noextract_to "FSharp"]
let sound_intro (#num #flt: eqtype) (#a: Type0) (d: dec num flt a)
  (pf: (x: W.jval num flt) -> Lemma (good_res x (d x)))
  : Lemma (sound d) =
  FStar.Classical.forall_intro pf

[@@ noextract_to "FSharp"]
let sound_all_intro (#num #flt: eqtype) (#a: Type0) (rd: W.jval num flt -> result a (list derr))
  (pf: (x: W.jval num flt) -> Lemma (good_all x (rd x)))
  : Lemma (sound_all rd) =
  FStar.Classical.forall_intro pf

[@@ noextract_to "FSharp"]
let sound_elim (#num #flt: eqtype) (#a: Type0) (d: dec num flt a) (x: W.jval num flt)
  : Lemma (requires sound d) (ensures good_res x (d x)) = ()

[@@ noextract_to "FSharp"]
let sound_all_elim (#num #flt: eqtype) (#a: Type0) (rd: W.jval num flt -> result a (list derr)) (x: W.jval num flt)
  : Lemma (requires sound_all rd) (ensures good_all x (rd x)) = ()

(* ---- `List.rev` facts, over `WireDecode.rev` ---- *)

[@@ noextract_to "FSharp"]
let rec rev_app_app (#a: Type0) (l acc1 acc2: list a)
  : Lemma (ensures W.rev_app l (app acc1 acc2) == app (W.rev_app l acc1) acc2) (decreases l) =
  match l with
  | [] -> ()
  | x :: t -> rev_app_app t (x :: acc1) acc2

[@@ noextract_to "FSharp"]
let rev_cons (#a: Type0) (x: a) (l: list a) : Lemma (W.rev (x :: l) == app (W.rev l) [x]) =
  rev_app_app l [] [x]

[@@ noextract_to "FSharp"]
let rec rev_snoc (#a: Type0) (l: list a) (x: a) : Lemma (ensures W.rev (app l [x]) == x :: W.rev l) (decreases l) =
  match l with
  | [] -> ()
  | y :: t -> rev_cons y (app t [x]); rev_snoc t x; rev_cons y t

(* ---- member lookup facts ---- *)

[@@ noextract_to "FSharp"]
let rec memb_none_has_key (#a: Type0) (k: string) (fs: list (string & a))
  : Lemma (ensures (None? (memb k fs) <==> not (has_key k fs))) =
  match fs with
  | [] -> ()
  | _ :: t -> memb_none_has_key k t

(* ---- 5a. the same, over a carrier ---- *)

[@@ noextract_to "FSharp"]
let ok_at_over (#v: Type0) (c: carrier v) (x: v) (e: derr) : bool =
  e.code <> InvalidJson && resolves_in_over c x e

[@@ noextract_to "FSharp"]
let good_over (#v #a: Type0) (c: carrier v) (x: v) (r: result a derr) : bool =
  match r with
  | Ok _ -> true
  | Error e -> ok_at_over c x e

[@@ noextract_to "FSharp"]
let sound_over (#v #a: Type0) (c: carrier v) (d: dec_over v a) : prop =
  forall (x: v). good_over c x (d x)

[@@ noextract_to "FSharp"]
let rec all_ok_over (#v: Type0) (c: carrier v) (x: v) (es: list derr) : Tot bool =
  match es with
  | [] -> true
  | e :: t -> ok_at_over c x e && all_ok_over c x t

[@@ noextract_to "FSharp"]
let good_all_over (#v #a: Type0) (c: carrier v) (x: v) (r: result a (list derr)) : bool =
  match r with
  | Ok _ -> true
  | Error es -> (match es with [] -> false | _ -> all_ok_over c x es)

[@@ noextract_to "FSharp"]
let sound_all_over (#v #a: Type0) (c: carrier v) (rd: v -> result a (list derr)) : prop =
  forall (x: v). good_all_over c x (rd x)

[@@ noextract_to "FSharp"]
let sound_over_intro (#v #a: Type0) (c: carrier v) (d: dec_over v a) (pf: (x: v) -> Lemma (good_over c x (d x)))
  : Lemma (sound_over c d) =
  FStar.Classical.forall_intro pf

[@@ noextract_to "FSharp"]
let sound_all_over_intro (#v #a: Type0) (c: carrier v) (rd: v -> result a (list derr))
  (pf: (x: v) -> Lemma (good_all_over c x (rd x)))
  : Lemma (sound_all_over c rd) =
  FStar.Classical.forall_intro pf

[@@ noextract_to "FSharp"]
let sound_over_elim (#v #a: Type0) (c: carrier v) (d: dec_over v a) (x: v)
  : Lemma (requires sound_over c d) (ensures good_over c x (d x)) = ()

(* THE TRANSPORT LEMMA. A refusal owed to the value one step reaches is owed, under that step, to
   the value the step started from. The `MissingField` arm is the content: its path's LAST step
   names the absent member, so the parent the law resolves is the prefix, and a step prepended to
   the path is prepended to the prefix (`rev_cons`, `rev_snoc`). *)
let ok_at_under_over (#v: Type0) (c: carrier v) (s: seg) (doc x: v) (e: derr)
  : Lemma (requires step_over c s doc == Some x /\ ok_at_over c x e) (ensures ok_at_over c doc (under s e)) =
  match e.code with
  | MissingField ->
      rev_cons s e.path;
      (match W.rev e.path with
       | Key k :: pr -> rev_snoc pr s
       | _ -> ())
  | _ -> ()

(* The same, under a whole prefix — `at`'s `within`. *)
let rec ok_at_within_over (#v: Type0) (c: carrier v) (p: list seg) (doc x: v) (e: derr)
  : Lemma (requires resolve_over c p doc == Some x /\ ok_at_over c x e) (ensures ok_at_over c doc (within p e))
          (decreases p) =
  match p with
  | [] -> assert (within [] e == e)
  | s :: q ->
      (match step_over c s doc with
       | Some x1 ->
           ok_at_within_over c q x1 x e;
           assert (within (s :: q) e == under s (within q e));
           ok_at_under_over c s doc x1 (within q e)
       | None -> ())

[@@ noextract_to "FSharp"]
let rec all_ok_under_over (#v: Type0) (c: carrier v) (s: seg) (doc x: v) (es: list derr)
  : Lemma (requires step_over c s doc == Some x /\ all_ok_over c x es)
          (ensures all_ok_over c doc (map_l (under s) es)) =
  match es with
  | [] -> ()
  | e :: t -> ok_at_under_over c s doc x e; all_ok_under_over c s doc x t

(* A refusal at the value itself, of any code but the two whose law is not "the path resolves":
   `InvalidJson` and `MissingField`. *)
let ok_make_over (#v: Type0) (c: carrier v) (code_: code) (x: v)
  : Lemma (requires code_ <> InvalidJson /\ code_ <> MissingField) (ensures ok_at_over c x (make code_)) = ()

(* `missing name`, over a value the view presents as an object that does not carry `name`. *)
let ok_missing_over (#v: Type0) (c: carrier v) (name: string) (x: v) (fs: list (string & v))
  : Lemma (requires c.members_of x == Some fs /\ memb name fs == None) (ensures ok_at_over c x (missing name)) =
  memb_none_has_key name fs

[@@ noextract_to "FSharp"]
let rec resolve_app_over (#v: Type0) (c: carrier v) (p q: list seg) (doc: v)
  : Lemma (ensures resolve_over c (app p q) doc ==
                   (match resolve_over c p doc with Some x -> resolve_over c q x | None -> None)) (decreases p) =
  match p with
  | [] -> ()
  | s :: t -> (match step_over c s doc with Some x -> resolve_app_over c t q x | None -> ())

(* ---- 5b. FAITHFULNESS of the `jval` carrier: its view is `jval`'s own object, array and
   string, so the path law through it is `DecodePath.resolve` and `ok_at` is `ok_at_over`. ---- *)

let step_is_over (#num #flt: eqtype) (s: seg) (x: W.jval num flt) : Lemma (step s x == step_over jcarrier s x) = ()

let rec resolve_is_over (#num #flt: eqtype) (p: list seg) (x: W.jval num flt)
  : Lemma (ensures resolve p x == resolve_over jcarrier p x) (decreases p) =
  match p with
  | [] -> ()
  | s :: rest -> (match step s x with Some y -> resolve_is_over rest y | None -> ())

let ok_at_is_over (#num #flt: eqtype) (x: W.jval num flt) (e: derr) : Lemma (ok_at x e == ok_at_over jcarrier x e) =
  resolve_is_over e.path x;
  match W.rev e.path with
  | Key k :: parent_rev -> resolve_is_over (W.rev parent_rev) x
  | _ -> ()

[@@ noextract_to "FSharp"]
let rec all_ok_is_over (#num #flt: eqtype) (x: W.jval num flt) (es: list derr)
  : Lemma (ensures all_ok x es == all_ok_over jcarrier x es) =
  match es with
  | [] -> ()
  | e :: t -> ok_at_is_over x e; all_ok_is_over x t

[@@ noextract_to "FSharp"]
let good_is_over (#num #flt: eqtype) (#a: Type0) (x: W.jval num flt) (r: result a derr)
  : Lemma (good_res x r == good_over jcarrier x r) =
  match r with
  | Ok _ -> ()
  | Error e -> ok_at_is_over x e

[@@ noextract_to "FSharp"]
let good_all_is_over (#num #flt: eqtype) (#a: Type0) (x: W.jval num flt) (r: result a (list derr))
  : Lemma (good_all x r == good_all_over jcarrier x r) =
  match r with
  | Ok _ -> ()
  | Error es -> all_ok_is_over x es

[@@ noextract_to "FSharp"]
let sound_is_over (#num #flt: eqtype) (#a: Type0) (d: dec num flt a) : Lemma (sound d <==> sound_over jcarrier d) =
  let pf (x: W.jval num flt) : Lemma (good_res x (d x) == good_over jcarrier x (d x)) = good_is_over x (d x) in
  FStar.Classical.forall_intro pf

[@@ noextract_to "FSharp"]
let sound_all_is_over (#num #flt: eqtype) (#a: Type0) (rd: W.jval num flt -> result a (list derr))
  : Lemma (sound_all rd <==> sound_all_over jcarrier rd) =
  let pf (x: W.jval num flt) : Lemma (good_all x (rd x) == good_all_over jcarrier x (rd x)) = good_all_is_over x (rd x) in
  FStar.Classical.forall_intro pf

(* The transport lemma and its companions over `jval`: the generic ones at `jcarrier`. *)
let ok_at_under (#num #flt: eqtype) (s: seg) (doc v: W.jval num flt) (e: derr)
  : Lemma (requires step s doc == Some v /\ ok_at v e) (ensures ok_at doc (under s e)) =
  ok_at_is_over v e; ok_at_is_over doc (under s e); ok_at_under_over jcarrier s doc v e

let ok_at_within (#num #flt: eqtype) (p: list seg) (doc v: W.jval num flt) (e: derr)
  : Lemma (requires resolve p doc == Some v /\ ok_at v e) (ensures ok_at doc (within p e)) =
  resolve_is_over p doc; ok_at_is_over v e; ok_at_is_over doc (within p e); ok_at_within_over jcarrier p doc v e

[@@ noextract_to "FSharp"]
let all_ok_under (#num #flt: eqtype) (s: seg) (doc v: W.jval num flt) (es: list derr)
  : Lemma (requires step s doc == Some v /\ all_ok v es) (ensures all_ok doc (map_l (under s) es)) =
  all_ok_is_over v es; all_ok_is_over doc (map_l (under s) es); all_ok_under_over jcarrier s doc v es

[@@ noextract_to "FSharp"]
let rec all_ok_app (#num #flt: eqtype) (x: W.jval num flt) (e1 e2: list derr)
  : Lemma (ensures all_ok x (app e1 e2) == (all_ok x e1 && all_ok x e2)) =
  match e1 with
  | [] -> ()
  | _ :: t -> all_ok_app x t e2

let ok_make (#num #flt: eqtype) (c: code) (x: W.jval num flt)
  : Lemma (requires c <> InvalidJson /\ c <> MissingField) (ensures ok_at x (make c)) = ()

let ok_missing (#num #flt: eqtype) (name: string) (fs: list (string & W.jval num flt))
  : Lemma (requires memb name fs == None) (ensures ok_at (W.JObj fs) (missing name)) =
  memb_none_has_key name fs

(* ======================================================================================
   6. THEOREM 3 — REFUSAL PATHS RESOLVE, block by block. Each lemma: the block, built from
      sound parts, is sound. 6a proves each block of the algebra ONCE, over any carrier, with
      no premise on the carrier; 6b is each lemma at `jval`, the generic one at `jcarrier`
      through the faithfulness of 5b, its statement unchanged.
   ====================================================================================== *)

(* ---- 6a. the algebra over a carrier ---- *)

(* A root code is never one of the two `ok_make` excludes, which is what `RootDecodeCode` is for. *)
let root_excludes (c: root_code) : Lemma (root c <> InvalidJson /\ root c <> MissingField) = ()

(* `fail` is sound for EVERY code it accepts (Phase 443): its code is a `root_code`, and no root
   code is one of the two whose refusal at the empty path does not resolve (section 9). *)
let fail_sound_over (#v #a: Type0) (cv: carrier v) (c: root_code) : Lemma (sound_over cv (fail_over #v #a c)) =
  root_excludes c

let map_sound_over (#v #a #b: Type0) (c: carrier v) (g: a -> b) (d: dec_over v a)
  : Lemma (requires sound_over c d) (ensures sound_over c (map_over g d)) =
  sound_over_intro c (map_over g d) (fun x -> sound_over_elim c d x)

let bind_sound_over (#v #a #b: Type0) (c: carrier v) (g: a -> dec_over v b) (d: dec_over v a)
  : Lemma (requires sound_over c d /\ (forall (y: a). sound_over c (g y))) (ensures sound_over c (bind_over g d)) =
  sound_over_intro c (bind_over g d) (fun x ->
    sound_over_elim c d x;
    match d x with
    | Ok y -> sound_over_elim c (g y) x
    | Error _ -> ())

(* `andThen` is sound for EVERY function (Phase 453): its function refuses with a `root_code`,
   which `andThen` raises at the value exactly as `fail` does, so no premise on the caller's
   function remains. Until 453 the function answered a whole refusal, path included, and this
   lemma carried "every refusal it answers is a root refusal of a code `ok_make` admits" as a
   hypothesis (section 9 states why it was needed). *)
let and_then_sound_over (#v #a #b: Type0) (c: carrier v) (g: a -> result b root_code) (d: dec_over v a)
  : Lemma (requires sound_over c d) (ensures sound_over c (and_then_over g d)) =
  sound_over_intro c (and_then_over g d) (fun x ->
    sound_over_elim c d x;
    match d x with
    | Ok y -> (match g y with Ok _ -> () | Error r -> root_excludes r)
    | Error _ -> ())

let text_sound_over (#v: Type0) (c: carrier v) : Lemma (sound_over c (text_over c)) =
  sound_over_intro c (text_over c) (fun x -> ())

let items_sound_over (#v: Type0) (c: carrier v) : Lemma (sound_over c (items_over c)) =
  sound_over_intro c (items_over c) (fun x -> ())

let members_sound_over (#v: Type0) (c: carrier v) : Lemma (sound_over c (members_over c)) =
  sound_over_intro c (members_over c) (fun x -> ())

let field_sound_over (#v #a: Type0) (c: carrier v) (name: string) (d: dec_over v a)
  : Lemma (requires sound_over c d) (ensures sound_over c (field_over c name d)) =
  sound_over_intro c (field_over c name d) (fun x ->
    match c.members_of x with
    | Some fs ->
        (match memb name fs with
         | Some y ->
             sound_over_elim c d y;
             (match d y with
              | Error e -> ok_at_under_over c (Key name) x y e
              | Ok _ -> ())
         | None -> ok_missing_over c name x fs)
    | None -> ())

let opt_field_sound_over (#v #a: Type0) (c: carrier v) (name: string) (d: dec_over v a)
  : Lemma (requires sound_over c d) (ensures sound_over c (opt_field_over c name d)) =
  sound_over_intro c (opt_field_over c name d) (fun x ->
    match c.members_of x with
    | Some fs ->
        (match memb name fs with
         | Some y ->
             sound_over_elim c d y;
             (match d y with
              | Error e -> ok_at_under_over c (Key name) x y e
              | Ok _ -> ())
         | None -> ())
    | None -> ())

(* `at`: by induction on the path still to walk, with the walked prefix's value in hand. *)
[@@ noextract_to "FSharp"]
let rec at_go_sound_over (#v #a: Type0) (c: carrier v) (d: dec_over v a) (doc: v) (done_rev rest: list seg) (x: v)
  : Lemma (requires sound_over c d /\ resolve_over c (W.rev done_rev) doc == Some x)
          (ensures good_over c doc (at_go_over c d done_rev rest x)) (decreases rest) =
  let here = W.rev done_rev in
  match rest with
  | [] ->
      sound_over_elim c d x;
      (match d x with
       | Error e -> ok_at_within_over c here doc x e
       | Ok _ -> ())
  | Key k :: tail ->
      (match c.members_of x with
       | Some fs ->
           (match memb k fs with
            | Some y ->
                rev_cons (Key k) done_rev;
                resolve_app_over c here [Key k] doc;
                at_go_sound_over c d doc (Key k :: done_rev) tail y
            | None -> ok_missing_over c k x fs; ok_at_within_over c here doc x (missing k))
       | None -> ok_at_within_over c here doc x (wrong_kind_over c x))
  | Index i :: tail ->
      (match c.items_of x with
       | Some xs ->
           (match (if i >= 0 then try_item i xs else None) with
            | Some y ->
                rev_cons (Index i) done_rev;
                resolve_app_over c here [Index i] doc;
                at_go_sound_over c d doc (Index i :: done_rev) tail y
            | None -> ok_at_within_over c here doc x (make OutOfRange))
       | None -> ok_at_within_over c here doc x (wrong_kind_over c x))

let at_sound_over (#v #a: Type0) (c: carrier v) (p: list seg) (d: dec_over v a)
  : Lemma (requires sound_over c d) (ensures sound_over c (at_over c p d)) =
  sound_over_intro c (at_over c p d) (fun x -> at_go_sound_over c d x [] p x)

(* ---- the list walkers: an item's refusal is owed to the array, under its index ---- *)

[@@ noextract_to "FSharp"]
let rec try_item_app (#a: Type0) (pre: list a) (y: a) (t: list a)
  : Lemma (ensures try_item (len pre) (app pre (y :: t)) == Some y) (decreases pre) =
  match pre with
  | [] -> ()
  | _ :: r -> try_item_app r y t

[@@ noextract_to "FSharp"]
let rec app_snoc (#a: Type0) (pre: list a) (y: a) (t: list a)
  : Lemma (ensures app pre (y :: t) == app (app pre [y]) t /\ len (app pre [y]) == len pre + 1) (decreases pre) =
  match pre with
  | [] -> ()
  | _ :: r -> app_snoc r y t

(* The item at `len pre` of an array whose items are `app pre (x :: _)` is `x`, so a refusal owed
   to `x` is owed to the array under `Index (len pre)`. *)
[@@ noextract_to "FSharp"]
let ok_at_item_over (#v: Type0) (c: carrier v) (arr: v) (pre: list v) (x: v) (t: list v) (e: derr)
  : Lemma (requires c.items_of arr == Some (app pre (x :: t)) /\ ok_at_over c x e)
          (ensures ok_at_over c arr (under (Index (len pre)) e)) =
  try_item_app pre x t;
  ok_at_under_over c (Index (len pre)) arr x e

[@@ noextract_to "FSharp"]
let rec mli_go_sound_over (#v #a: Type0) (c: carrier v) (g: int -> dec_over v a) (arr: v) (pre: list v)
  (acc: list a) (xs: list v)
  : Lemma (requires (forall (i: int). sound_over c (g i)) /\ c.items_of arr == Some (app pre xs))
          (ensures good_over c arr (mli_go g (len pre) acc xs)) (decreases xs) =
  match xs with
  | [] -> ()
  | x :: rest ->
      sound_over_elim c (g (len pre)) x;
      (match g (len pre) x with
       | Ok y -> app_snoc pre x rest; mli_go_sound_over c g arr (app pre [x]) (y :: acc) rest
       | Error e -> ok_at_item_over c arr pre x rest e)

let map_list_indexed_sound_over (#v #a: Type0) (c: carrier v) (g: int -> dec_over v a)
  : Lemma (requires forall (i: int). sound_over c (g i)) (ensures sound_over c (map_list_indexed_over c g)) =
  sound_over_intro c (map_list_indexed_over c g) (fun x ->
    match c.items_of x with
    | Some xs -> mli_go_sound_over c g x [] [] xs
    | None -> ())

let bounded_list_sound_over (#v #a: Type0) (c: carrier v) (max_items: int) (d: dec_over v a)
  : Lemma (requires sound_over c d) (ensures sound_over c (bounded_list_over c max_items d)) =
  map_list_indexed_sound_over c (fun (_: int) -> d);
  sound_over_intro c (bounded_list_over c max_items d) (fun x ->
    sound_over_elim c (map_list_indexed_over c (fun (_: int) -> d)) x)

[@@ noextract_to "FSharp"]
let rec mapi_d_sound_over (#v #a: Type0) (c: carrier v) (d: dec_over v a) (arr: v) (pre xs: list v)
  : Lemma (requires sound_over c d /\ c.items_of arr == Some (app pre xs))
          (ensures all_ok_over c arr (errors_of (mapi_d d (len pre) xs))) (decreases xs) =
  match xs with
  | [] -> ()
  | x :: rest ->
      sound_over_elim c d x;
      app_snoc pre x rest;
      mapi_d_sound_over c d arr (app pre [x]) rest;
      (match d x with
       | Error e -> ok_at_item_over c arr pre x rest e
       | Ok _ -> ())

let list_all_sound_over (#v #a: Type0) (c: carrier v) (d: dec_over v a)
  : Lemma (requires sound_over c d) (ensures sound_all_over c (list_all_over c d)) =
  sound_all_over_intro c (list_all_over c d) (fun x ->
    match c.items_of x with
    | Some xs -> mapi_d_sound_over c d x [] xs
    | None -> ())

(* ---- several decoders over one value ---- *)

[@@ noextract_to "FSharp"]
let rec all_sound_over (#v #a: Type0) (c: carrier v) (ds: list (dec_over v a)) : prop =
  match ds with
  | [] -> True
  | d :: t -> sound_over c d /\ all_sound_over c t

[@@ noextract_to "FSharp"]
let rec seq_go_sound_over (#v #a: Type0) (c: carrier v) (el: v) (acc: list a) (ds: list (dec_over v a))
  : Lemma (requires all_sound_over c ds) (ensures good_over c el (seq_go el acc ds)) (decreases ds) =
  match ds with
  | [] -> ()
  | d :: rest ->
      sound_over_elim c d el;
      (match d el with
       | Ok y -> seq_go_sound_over c el (y :: acc) rest
       | Error _ -> ())

let sequence_sound_over (#v #a: Type0) (c: carrier v) (ds: list (dec_over v a))
  : Lemma (requires all_sound_over c ds) (ensures sound_over c (sequence_over ds)) =
  sound_over_intro c (sequence_over ds) (fun x -> seq_go_sound_over c x [] ds)

[@@ noextract_to "FSharp"]
let rec run_all_sound_over (#v #a: Type0) (c: carrier v) (ds: list (dec_over v a)) (el: v)
  : Lemma (requires all_sound_over c ds) (ensures all_ok_over c el (errors_of (run_all ds el))) =
  match ds with
  | [] -> ()
  | d :: t -> sound_over_elim c d el; run_all_sound_over c t el

let all_d_sound_over (#v #a: Type0) (c: carrier v) (ds: list (dec_over v a))
  : Lemma (requires all_sound_over c ds) (ensures sound_all_over c (all_over ds)) =
  sound_all_over_intro c (all_over ds) (fun x -> run_all_sound_over c ds x)

(* ---- tags and enumerations ---- *)

let one_of_sound_over (#v #a: Type0) (c: carrier v) (cases: list (string & a))
  : Lemma (sound_over c (one_of_over c cases)) =
  sound_over_intro c (one_of_over c cases) (fun x -> ())

[@@ noextract_to "FSharp"]
let rec table_sound_over (#v #a: Type0) (c: carrier v) (cases: list (string & dec_over v a)) : prop =
  match cases with
  | [] -> True
  | (_, d) :: t -> sound_over c d /\ table_sound_over c t

[@@ noextract_to "FSharp"]
let rec find_case_sound_over (#v #a: Type0) (c: carrier v) (s: string) (cases: list (string & dec_over v a))
  : Lemma (requires table_sound_over c cases)
          (ensures (match find_case s cases with Some d -> sound_over c d | None -> True)) =
  match cases with
  | [] -> ()
  | (k, _) :: t -> if k = s then () else find_case_sound_over c s t

(* An unknown tag is refused UNDER the discriminator, which the dispatch has just read — so the
   step resolves. *)
let tag_dispatch_sound_over (#v #a: Type0) (c: carrier v) (key: string) (cases: list (string & dec_over v a))
  : Lemma (requires table_sound_over c cases) (ensures sound_over c (tag_dispatch_over c key cases)) =
  text_sound_over c;
  field_sound_over c key (text_over c);
  sound_over_intro c (tag_dispatch_over c key cases) (fun x ->
    sound_over_elim c (field_over c key (text_over c)) x;
    match field_over c key (text_over c) x with
    | Error _ -> ()
    | Ok t ->
        find_case_sound_over c t cases;
        (match find_case t cases with
         | Some d -> sound_over_elim c d x
         | None ->
             (match c.members_of x with
              | Some fs ->
                  (match memb key fs with
                   | Some y -> ok_at_under_over c (Key key) x y (make UnknownTag)
                   | None -> ())
              | None -> ())))

(* ---- the strict member policy: a refused member is one the object carries ---- *)

[@@ noextract_to "FSharp"]
let rec keys_in (#a: Type0) (sfx full: list (string & a)) : Tot bool =
  match sfx with
  | [] -> true
  | (k, _) :: t -> has_key k full && keys_in t full

[@@ noextract_to "FSharp"]
let rec keys_in_cons (#a: Type0) (m: string & a) (sfx full: list (string & a))
  : Lemma (requires keys_in sfx full) (ensures keys_in sfx (m :: full)) =
  match sfx with
  | [] -> ()
  | _ :: t -> keys_in_cons m t full

[@@ noextract_to "FSharp"]
let rec keys_in_refl (#a: Type0) (fs: list (string & a)) : Lemma (ensures keys_in fs fs) =
  match fs with
  | [] -> ()
  | m :: t -> keys_in_refl t; keys_in_cons m t t

[@@ noextract_to "FSharp"]
let rec undeclared_go_sound_over (#v: Type0) (c: carrier v) (known: list string) (obj_: v) (sfx full: list (string & v))
  : Lemma (requires c.members_of obj_ == Some full /\ keys_in sfx full)
          (ensures all_ok_over c obj_ (undeclared_go known sfx)) =
  match sfx with
  | [] -> ()
  | (k, _) :: t ->
      undeclared_go_sound_over c known obj_ t full;
      memb_none_has_key k full;
      (match memb k full with
       | Some y -> ok_at_under_over c (Key k) obj_ y (make UndeclaredMember)
       | None -> ())

let undeclared_sound_over (#v: Type0) (c: carrier v) (known: list string) (x: v)
  : Lemma (ensures all_ok_over c x (undeclared_over c known x)) =
  match c.members_of x with
  | Some fs -> keys_in_refl fs; undeclared_go_sound_over c known x fs fs
  | None -> ()

let members_only_sound_over (#v: Type0) (c: carrier v) (known: list string)
  : Lemma (sound_over c (members_only_over c known)) =
  sound_over_intro c (members_only_over c known) (fun x -> undeclared_sound_over c known x)

(* THEOREM 3 over a carrier, the statement a caller reads: a sound decoder's every refusal over
   `x` resolves in `x` through the view, and is not the parser's code. *)
let refusal_paths_resolve_over (#v #a: Type0) (c: carrier v) (d: dec_over v a) (x: v)
  : Lemma (requires sound_over c d)
          (ensures (match d x with Error e -> resolves_in_over c x e /\ e.code <> InvalidJson | Ok _ -> True)) =
  sound_over_elim c d x

(* ---- 6b. the blocks at `jval`: each statement as Phase 436 stated it, its proof the generic
   lemma at `jcarrier` (5b). The readers `jval` owns (`json`, `int`, `float`, `bool`) and the
   compositions `Decoder` spells itself are proved here directly, as before. ---- *)

[@@ noextract_to "FSharp"]
let sound_to_over (#num #flt: eqtype) (#a: Type0) (d: dec num flt a)
  : Lemma (requires sound d) (ensures sound_over jcarrier d) =
  sound_is_over d

[@@ noextract_to "FSharp"]
let sound_of_over (#num #flt: eqtype) (#a: Type0) (d: dec num flt a)
  : Lemma (requires sound_over jcarrier d) (ensures sound d) =
  sound_is_over d

let succeed_sound (#num #flt: eqtype) (#a: Type0) (v: a) : Lemma (sound (succeed #num #flt v)) = ()

let fail_sound (#num #flt: eqtype) (#a: Type0) (c: root_code)
  : Lemma (ensures sound (fail #num #flt #a c)) =
  fail_sound_over #(W.jval num flt) #a jcarrier c;
  sound_of_over (fail #num #flt #a c)

let map_sound (#num #flt: eqtype) (#a #b: Type0) (g: a -> b) (d: dec num flt a)
  : Lemma (requires sound d) (ensures sound (map g d)) =
  sound_to_over d; map_sound_over jcarrier g d; sound_of_over (map g d)

let bind_sound (#num #flt: eqtype) (#a #b: Type0) (g: a -> dec num flt b) (d: dec num flt a)
  : Lemma (requires sound d /\ (forall (v: a). sound (g v))) (ensures sound (bind g d)) =
  sound_to_over d;
  let each (v: a) : Lemma (sound_over jcarrier (g v)) = sound_to_over (g v) in
  FStar.Classical.forall_intro each;
  bind_sound_over jcarrier g d;
  sound_of_over (bind g d)

let and_then_sound (#num #flt: eqtype) (#a #b: Type0) (g: a -> result b root_code) (d: dec num flt a)
  : Lemma (requires sound d) (ensures sound (and_then g d)) =
  sound_to_over d; and_then_sound_over jcarrier g d; sound_of_over (and_then g d)

let json_sound (#num #flt: eqtype) : Lemma (sound (json #num #flt)) = ()

let str_sound (#num #flt: eqtype) : Lemma (sound (str #num #flt)) =
  text_sound_over (jcarrier #num #flt); sound_of_over (str #num #flt)

let int_sound (#num #flt: eqtype) : Lemma (sound (int_d #num #flt)) =
  sound_intro (int_d #num #flt) (fun x -> ())

let float_sound (#num #flt: eqtype) (to_flt: num -> flt) : Lemma (sound (float_d #num #flt to_flt)) =
  sound_intro (float_d to_flt) (fun x -> ())

let bool_sound (#num #flt: eqtype) : Lemma (sound (bool_d #num #flt)) =
  sound_intro (bool_d #num #flt) (fun x -> ())

let items_sound (#num #flt: eqtype) : Lemma (sound (items #num #flt)) =
  items_sound_over (jcarrier #num #flt); sound_of_over (items #num #flt)

let obj_sound (#num #flt: eqtype) : Lemma (sound (obj #num #flt)) =
  members_sound_over (jcarrier #num #flt); sound_of_over (obj #num #flt)

let field_sound (#num #flt: eqtype) (#a: Type0) (name: string) (d: dec num flt a)
  : Lemma (requires sound d) (ensures sound (field name d)) =
  sound_to_over d; field_sound_over jcarrier name d; sound_of_over (field name d)

let opt_field_sound (#num #flt: eqtype) (#a: Type0) (name: string) (d: dec num flt a)
  : Lemma (requires sound d) (ensures sound (opt_field name d)) =
  sound_to_over d; opt_field_sound_over jcarrier name d; sound_of_over (opt_field name d)

let field_or_sound (#num #flt: eqtype) (#a: Type0) (name: string) (fallback: a) (d: dec num flt a)
  : Lemma (requires sound d) (ensures sound (field_or name fallback d)) =
  opt_field_sound name d;
  map_sound (fun (o: option a) -> match o with Some x -> x | None -> fallback) (opt_field name d)

let at_sound (#num #flt: eqtype) (#a: Type0) (p: list seg) (d: dec num flt a)
  : Lemma (requires sound d) (ensures sound (at p d)) =
  sound_to_over d; at_sound_over jcarrier p d; sound_of_over (at p d)

let map_list_indexed_sound (#num #flt: eqtype) (#a: Type0) (g: int -> dec num flt a)
  : Lemma (requires forall (i: int). sound (g i)) (ensures sound (map_list_indexed g)) =
  let each (i: int) : Lemma (sound_over jcarrier (g i)) = sound_to_over (g i) in
  FStar.Classical.forall_intro each;
  map_list_indexed_sound_over jcarrier g;
  sound_of_over (map_list_indexed g)

let list_sound (#num #flt: eqtype) (#a: Type0) (d: dec num flt a)
  : Lemma (requires sound d) (ensures sound (list_d d)) =
  map_list_indexed_sound #num #flt #a (fun _ -> d)

let bounded_list_sound (#num #flt: eqtype) (#a: Type0) (max_items: int) (d: dec num flt a)
  : Lemma (requires sound d) (ensures sound (bounded_list max_items d)) =
  sound_to_over d; bounded_list_sound_over jcarrier max_items d; sound_of_over (bounded_list max_items d)

let list_all_sound (#num #flt: eqtype) (#a: Type0) (d: dec num flt a)
  : Lemma (requires sound d) (ensures sound_all (list_all d)) =
  sound_to_over d; list_all_sound_over jcarrier d; sound_all_is_over (list_all d)

[@@ noextract_to "FSharp"]
let rec all_sound (#num #flt: eqtype) (#a: Type0) (ds: list (dec num flt a)) : prop =
  match ds with
  | [] -> True
  | d :: t -> sound d /\ all_sound t

[@@ noextract_to "FSharp"]
let rec all_sound_to_over (#num #flt: eqtype) (#a: Type0) (ds: list (dec num flt a))
  : Lemma (requires all_sound ds) (ensures all_sound_over jcarrier ds) =
  match ds with
  | [] -> ()
  | d :: t -> sound_to_over d; all_sound_to_over t

let sequence_sound (#num #flt: eqtype) (#a: Type0) (ds: list (dec num flt a))
  : Lemma (requires all_sound ds) (ensures sound (sequence ds)) =
  all_sound_to_over ds; sequence_sound_over jcarrier ds; sound_of_over (sequence ds)

let all_d_sound (#num #flt: eqtype) (#a: Type0) (ds: list (dec num flt a))
  : Lemma (requires all_sound ds) (ensures sound_all (all_d ds)) =
  all_sound_to_over ds; all_d_sound_over jcarrier ds; sound_all_is_over (all_d ds)

let one_of_sound (#num #flt: eqtype) (#a: Type0) (cases: list (string & a))
  : Lemma (sound (one_of #num #flt cases)) =
  one_of_sound_over (jcarrier #num #flt) cases; sound_of_over (one_of #num #flt cases)

[@@ noextract_to "FSharp"]
let rec table_sound (#num #flt: eqtype) (#a: Type0) (cases: list (string & dec num flt a)) : prop =
  match cases with
  | [] -> True
  | (_, d) :: t -> sound d /\ table_sound t

[@@ noextract_to "FSharp"]
let rec table_sound_to_over (#num #flt: eqtype) (#a: Type0) (cases: list (string & dec num flt a))
  : Lemma (requires table_sound cases) (ensures table_sound_over jcarrier cases) =
  match cases with
  | [] -> ()
  | (_, d) :: t -> sound_to_over d; table_sound_to_over t

let tag_dispatch_sound (#num #flt: eqtype) (#a: Type0) (key: string) (cases: list (string & dec num flt a))
  : Lemma (requires table_sound cases) (ensures sound (tag_dispatch key cases)) =
  table_sound_to_over cases; tag_dispatch_sound_over jcarrier key cases; sound_of_over (tag_dispatch key cases)

let tag_dispatch_with_sound (#num #flt: eqtype) (#a: Type0) (what key: string) (cases: list (string & dec num flt a))
  : Lemma (requires table_sound cases) (ensures sound (tag_dispatch_with what key cases)) =
  tag_dispatch_sound key cases;
  sound_intro (tag_dispatch_with what key cases) (fun x -> sound_elim (tag_dispatch key cases) x)

let kind_dispatch_sound (#num #flt: eqtype) (#a: Type0) (cases: list (string & dec num flt a))
  : Lemma (requires table_sound cases) (ensures sound (kind_dispatch cases)) =
  tag_dispatch_sound "kind" cases

let int_range_sound (#num #flt: eqtype) (in_range: num -> bool) : Lemma (sound (int_range #num #flt in_range)) =
  sound_intro (int_range #num #flt in_range) (fun x -> ())

let undeclared_sound (#num #flt: eqtype) (known: list string) (x: W.jval num flt)
  : Lemma (ensures all_ok x (undeclared known x)) =
  undeclared_sound_over jcarrier known x; all_ok_is_over x (undeclared known x)

let members_sound (#num #flt: eqtype) (known: list string) : Lemma (sound (members #num #flt known)) =
  members_only_sound_over (jcarrier #num #flt) known; sound_of_over (members #num #flt known)

let closed_sound (#num #flt: eqtype) (#a: Type0) (known: list string) (d: dec num flt a)
  : Lemma (requires sound d) (ensures sound (closed known d)) =
  members_sound #num #flt known;
  bind_sound (fun () -> d) (members known)

(* THEOREM 3, the statement a caller reads: a sound decoder's every refusal over `x` resolves in
   `x` (`DecodeError.resolvesIn`), and soundness is what every block above preserves. *)
let refusal_paths_resolve (#num #flt: eqtype) (#a: Type0) (d: dec num flt a) (x: W.jval num flt)
  : Lemma (requires sound d)
          (ensures (match d x with Error e -> resolves_in x e /\ e.code <> InvalidJson | Ok _ -> True)) =
  sound_elim d x

(* ---- theorem 3 for the codecs: the COLLECTING reader, every defect owed to the input ---- *)

[@@ noextract_to "FSharp"]
let codec_sound (#num #flt: eqtype) (#a: Type0) (c: codec num flt a) : prop = sound_all c.read_all

let of_decoder_sound (#num #flt: eqtype) (#a: Type0) (w: a -> W.jval num flt) (d: dec num flt a)
  : Lemma (requires sound d) (ensures codec_sound (c_of_decoder w d)) =
  sound_all_intro (c_of_decoder w d).read_all (fun x -> sound_elim d x)

(* `make`'s reader is the caller's: sound when the caller's is (`decode-layer-refusal-premises`). *)
let make_sound (#num #flt: eqtype) (#a: Type0) (w: a -> W.jval num flt) (r: W.jval num flt -> result a (list derr))
  : Lemma (requires sound_all r) (ensures codec_sound (c_make w r)) = ()

let json_codec_sound (#num #flt: eqtype) : Lemma (codec_sound (c_json #num #flt)) = ()

let string_codec_sound (#num #flt: eqtype) : Lemma (codec_sound (c_string #num #flt)) =
  str_sound #num #flt; of_decoder_sound (fun s -> W.JStr s) (str #num #flt)

let int_codec_sound (#num #flt: eqtype) : Lemma (codec_sound (c_int #num #flt)) =
  int_sound #num #flt; of_decoder_sound (fun i -> W.JInt i) (int_d #num #flt)

let float_codec_sound (#num #flt: eqtype) (to_flt: num -> flt) : Lemma (codec_sound (c_float #num #flt to_flt)) =
  float_sound #num #flt to_flt; of_decoder_sound (fun f -> W.JFloat f) (float_d #num #flt to_flt)

let bool_codec_sound (#num #flt: eqtype) : Lemma (codec_sound (c_bool #num #flt)) =
  bool_sound #num #flt; of_decoder_sound (fun b -> W.JBool b) (bool_d #num #flt)

let enum_sound (#num #flt: eqtype) (#a: eqtype) (cases: list (string & a))
  : Lemma (match c_enum #num #flt cases with Ok c -> codec_sound c | Error _ -> True) =
  match c_enum #num #flt cases with
  | Ok c ->
      one_of_sound #num #flt cases;
      sound_all_intro c.read_all (fun x -> sound_elim (one_of #num #flt cases) x)
  | Error _ -> ()

[@@ noextract_to "FSharp"]
let rec mapi_all_sound (#num #flt: eqtype) (#a: Type0) (r: W.jval num flt -> result a (list derr))
  (pre xs: list (W.jval num flt))
  : Lemma (requires sound_all r)
          (ensures all_ok (W.JArr (app pre xs)) (collect_errors (mapi_all r (len pre) xs))) (decreases xs) =
  match xs with
  | [] -> ()
  | x :: rest ->
      sound_all_elim r x;
      app_snoc pre x rest;
      mapi_all_sound r (app pre [x]) rest;
      (match r x with
       | Error es ->
           try_item_app pre x rest;
           all_ok_under (Index (len pre)) (W.JArr (app pre (x :: rest))) x es;
           all_ok_app (W.JArr (app pre xs)) (map_l (under (Index (len pre))) es)
             (collect_errors (mapi_all r (len pre + 1) rest))
       | Ok _ -> ())

let list_codec_sound (#num #flt: eqtype) (#a: Type0) (c: codec num flt a)
  : Lemma (requires codec_sound c) (ensures codec_sound (c_list c)) =
  sound_all_intro (c_list c).read_all (fun x ->
    match x with
    | W.JArr xs -> mapi_all_sound c.read_all [] xs
    | _ -> ())

let map_codec_sound (#num #flt: eqtype) (#a #b: Type0) (there: a -> b) (back: b -> a) (c: codec num flt a)
  : Lemma (requires codec_sound c) (ensures codec_sound (c_map there back c)) =
  sound_all_intro (c_map there back c).read_all (fun x -> sound_all_elim c.read_all x)

(* A refinement's refusal is `OutOfRange` at the value itself — always owed. *)
let refine_codec_sound (#num #flt: eqtype) (#a #b: Type0) (expected: string) (check: a -> result b string)
  (back: b -> a) (c: codec num flt a)
  : Lemma (requires codec_sound c) (ensures codec_sound (c_refine expected check back c)) =
  sound_all_intro (c_refine expected check back c).read_all (fun x ->
    sound_all_elim c.read_all x; ok_make OutOfRange x)

(* ---- records and unions: every member's defect is owed to the object ---- *)

[@@ noextract_to "FSharp"]
let both_good (#num #flt: eqtype) (#a #b: Type0) (x: W.jval num flt) (fr: result (a -> b) (list derr))
  (ar: result a (list derr))
  : Lemma (requires good_all x fr /\ good_all x ar) (ensures good_all x (both fr ar)) =
  match fr, ar with
  | Error e1, Error e2 -> all_ok_app x e1 e2
  | _, _ -> ()

let record_sound (#num #flt: eqtype) (#r #c: Type0) (ctor: c) : Lemma (fields_sound (c_record #num #flt #r ctor)) = ()

let field_codec_sound (#num #flt: eqtype) (#r #a #c: Type0) (name: string) (get: r -> a) (cd: codec num flt a)
  (fs: fields num flt r (a -> c))
  : Lemma (requires fields_sound fs /\ codec_sound cd) (ensures fields_sound (c_field name get cd fs)) =
  let f = c_field name get cd fs in
  FStar.Classical.forall_intro (fun (ms: list (string & W.jval num flt)) ->
    (let this : result a (list derr) =
       match memb name ms with
       | Some v -> under_all (Key name) (cd.read_all v)
       | None -> Error [missing name] in
     (match memb name ms with
      | Some v ->
          sound_all_elim cd.read_all v;
          (match cd.read_all v with
           | Error es -> all_ok_under (Key name) (W.JObj ms) v es
           | Ok _ -> ())
      | None -> ok_missing name ms);
     both_good (W.JObj ms) (fs.read_fields ms) this;
     assert (f.read_fields ms == both (fs.read_fields ms) this))
    <: Lemma (good_all (W.JObj ms) (f.read_fields ms)))

let opt_field_codec_sound (#num #flt: eqtype) (#r #a #c: Type0) (name: string) (get: r -> option a)
  (cd: codec num flt a) (fs: fields num flt r (option a -> c))
  : Lemma (requires fields_sound fs /\ codec_sound cd) (ensures fields_sound (c_opt_field name get cd fs)) =
  let f = c_opt_field name get cd fs in
  FStar.Classical.forall_intro (fun (ms: list (string & W.jval num flt)) ->
    (let this : result (option a) (list derr) =
       match memb name ms with
       | Some v ->
           (match under_all (Key name) (cd.read_all v) with
            | Ok y -> Ok (Some y)
            | Error es -> Error es)
       | None -> Ok None in
     (match memb name ms with
      | Some v ->
          sound_all_elim cd.read_all v;
          (match cd.read_all v with
           | Error es -> all_ok_under (Key name) (W.JObj ms) v es
           | Ok _ -> ())
      | None -> ());
     both_good (W.JObj ms) (fs.read_fields ms) this;
     assert (f.read_fields ms == both (fs.read_fields ms) this))
    <: Lemma (good_all (W.JObj ms) (f.read_fields ms)))

(* The strict object read: the undeclared members' defects first (each owed — `undeclared_sound`),
   then the declared members'. *)
let strict_object_sound (#num #flt: eqtype) (#a: Type0) (names: list string) (j: W.jval num flt)
  (read: list (string & W.jval num flt) -> result a (list derr))
  : Lemma (requires (match j with W.JObj ms -> good_all j (read ms) | _ -> True))
          (ensures good_all j (strict_object names j read)) =
  match j with
  | W.JObj ms ->
      undeclared_sound names j;
      (match undeclared names j, read ms with
       | [], _ -> ()
       | u, Ok _ -> ()
       | u, Error e -> all_ok_app j u e)
  | _ -> ()

let build_sound (#num #flt: eqtype) (#r: Type0) (fs: fields num flt r r)
  : Lemma (requires fields_sound fs) (ensures (match c_build fs with Ok c -> codec_sound c | Error _ -> True)) =
  match c_build fs with
  | Ok c ->
      sound_all_intro c.read_all (fun x ->
        match x with
        | W.JObj ms -> strict_object_sound fs.names x fs.read_fields
        | _ -> ())
  | Error _ -> ()

let case_codec_sound (#num #flt: eqtype) (#t #p: Type0) (tag: string) (project: t -> option p) (inject: p -> t)
  (fs: fields num flt p p)
  : Lemma (requires fields_sound fs) (ensures case_sound (c_case tag project inject fs)) = ()

[@@ noextract_to "FSharp"]
let rec cases_sound (#num #flt: eqtype) (#t: Type0) (cases: list (ccase num flt t)) : prop =
  match cases with
  | [] -> True
  | c :: rest -> case_sound c /\ cases_sound rest

[@@ noextract_to "FSharp"]
let rec case_table_sound (#num #flt: eqtype) (#t: Type0) (cases: list (ccase num flt t))
  : Lemma (ensures table_sound (case_table cases)) =
  match cases with
  | [] -> ()
  | c :: rest -> succeed_sound #num #flt c; case_table_sound rest

(* What the dispatch answers is a case of the declaration, and so a sound one. *)
[@@ noextract_to "FSharp"]
let rec find_case_table (#num #flt: eqtype) (#t: Type0) (s: string) (cases: list (ccase num flt t)) (x: W.jval num flt)
  : Lemma (requires cases_sound cases)
          (ensures (match find_case s (case_table cases) with
                    | Some d -> (match d x with Ok c -> case_sound c | Error _ -> True)
                    | None -> True)) =
  match cases with
  | [] -> ()
  | c :: rest -> if c.ctag = s then () else find_case_table s rest x

let union_sound (#num #flt: eqtype) (#t: Type0) (key: string) (cases: list (ccase num flt t))
  : Lemma (requires cases_sound cases) (ensures (match c_union key cases with Ok c -> codec_sound c | Error _ -> True)) =
  case_table_sound cases;
  tag_dispatch_sound key (case_table cases);
  sound_all_intro (union_read key cases) (fun x ->
    sound_elim (tag_dispatch key (case_table cases)) x;
    match tag_dispatch key (case_table cases) x with
    | Error _ -> ()
    | Ok c ->
        (match field key str x with
         | Ok tg -> find_case_table tg cases x
         | Error _ -> ());
        strict_object_sound (key :: c.case_names) x c.read_case)

(* The strict reader of a sound codec is sound. *)
let decoder_sound (#num #flt: eqtype) (#a: Type0) (c: codec num flt a)
  : Lemma (requires codec_sound c) (ensures sound (c_decoder c)) =
  sound_intro (c_decoder c) (fun x -> sound_all_elim c.read_all x; ok_make SchemaFault x)

(* ======================================================================================
   7. THEOREM 2 — DECODER TOTALITY.

      Every block above is a `Tot` function of the `jval`: F* accepted each definition at `Tot`,
      so none raises and none diverges — `at_go_over` terminates on the path it walks, `mli_go`,
      `mapi_d`, `mapi_all`, `seq_go` and `traverse_go` on their lists, and nothing in the layer
      has an exception path for a document to reach. What a typing cannot say, and what is
      proved here, is that the COLLECTING reader of a codec built from the blocks never answers
      the one outcome that names nothing — `Error []` — so `Codec.decoder`'s third arm, the
      `SchemaFault` "a collecting decoder answered no defect and no value", is unreachable for
      any such codec: the strict reader answers `Ok` exactly when the collecting one does, and
      otherwise the collecting one's FIRST defect, which is a defect of the input.
   ====================================================================================== *)

let strict_reader_total (#num #flt: eqtype) (#a: Type0) (c: codec num flt a) (j: W.jval num flt)
  : Lemma (requires codec_sound c)
          (ensures (match c.read_all j with
                    | Ok v -> c_decoder c j == Ok v
                    | Error es -> Cons? es /\ c_decoder c j == Error (Cons?.hd es))) =
  sound_all_elim c.read_all j

(* Every block answers on every value — the typing, stated. *)
let decoder_total (#num #flt: eqtype) (#a: Type0) (d: dec num flt a) (x: W.jval num flt)
  : Lemma (Ok? (d x) \/ Error? (d x)) = ()

(* ======================================================================================
   8. THEOREM 1 — COMPOSED ROUND TRIP.

      `rt c`: whatever `write` answers `Ok j` for, `read_all j` reads back as the value written.
      A record's members are written in declaration order and read by NAME, so the invariant a
      partly-declared record carries (`fields_rt`) is stated over every member list that AGREES
      with the written one on the declared names — which is what lets the next `field` append a
      member, and what lets a union read a case's members past its discriminator. `k` is the
      constructor applied to the getters so far: `record`'s `ctor`, then `k x (get x)` per
      member, and the premise `build` and `case` need is that the finished `k` is the identity.
   ====================================================================================== *)

[@@ noextract_to "FSharp"]
let rt (#num #flt: eqtype) (#a: Type0) (c: codec num flt a) : prop =
  forall (v: a). {:pattern (c.write v)}
    (match c.write v with
     | Ok j -> c.read_all j == Ok v
     | Error _ -> True)

[@@ noextract_to "FSharp"]
let rt_elim (#num #flt: eqtype) (#a: Type0) (c: codec num flt a) (v: a) (j: W.jval num flt)
  : Lemma (requires rt c /\ c.write v == Ok j) (ensures c.read_all j == Ok v) = ()

[@@ noextract_to "FSharp"]
let rec no_dups (l: list string) : Tot bool =
  match l with
  | [] -> true
  | x :: t -> not (mem x t) && no_dups t

(* ---- `repeats l == []` is `no_dups l`'s half the theorems need ---- *)

[@@ noextract_to "FSharp"]
let rec a_dup (l: list string{not (no_dups l)}) : Tot (x: string{occurs_twice x l /\ mem x l}) =
  match l with
  | y :: t -> if mem y t then y else a_dup t

[@@ noextract_to "FSharp"]
let rec dedup_mem (seen l: list string) (x: string)
  : Lemma (requires mem x l) (ensures mem x seen || mem x (dedup_go seen l)) (decreases l) =
  match l with
  | y :: t ->
      if y = x then ()
      else if mem y seen then dedup_mem seen t x
      else dedup_mem (y :: seen) t x

[@@ noextract_to "FSharp"]
let rec filter_mem (#a: eqtype) (p: a -> bool) (ys: list a) (x: a)
  : Lemma (requires mem x ys /\ p x) (ensures Cons? (filter_l p ys)) =
  match ys with
  | y :: t -> if p y then () else filter_mem p t x

[@@ noextract_to "FSharp"]
let repeats_nil (l: list string) : Lemma (requires repeats l == []) (ensures no_dups l) =
  if no_dups l then ()
  else begin
    let x = a_dup l in
    dedup_mem [] l x;
    filter_mem (fun y -> occurs_twice y l) (dedup_go [] l) x
  end

(* ---- list facts ---- *)

[@@ noextract_to "FSharp"]
let rec app_nil_r (#a: Type0) (l: list a) : Lemma (ensures app l [] == l) =
  match l with [] -> () | _ :: t -> app_nil_r t

[@@ noextract_to "FSharp"]
let rec app_assoc (#a: Type0) (l1 l2 l3: list a) : Lemma (ensures app (app l1 l2) l3 == app l1 (app l2 l3)) =
  match l1 with [] -> () | _ :: t -> app_assoc t l2 l3

[@@ noextract_to "FSharp"]
let rec mem_app (#a: eqtype) (x: a) (l1 l2: list a) : Lemma (ensures mem x (app l1 l2) == (mem x l1 || mem x l2)) =
  match l1 with [] -> () | _ :: t -> mem_app x t l2

[@@ noextract_to "FSharp"]
let rec no_dups_snoc (l: list string) (x: string)
  : Lemma (requires no_dups (app l [x])) (ensures no_dups l /\ not (mem x l)) =
  match l with
  | [] -> ()
  | y :: t -> mem_app y t [x]; no_dups_snoc t x

[@@ noextract_to "FSharp"]
let rec keys_within (#num #flt: eqtype) (ws: list (string & W.jval num flt)) (names: list string) : Tot bool =
  match ws with
  | [] -> true
  | (k, _) :: t -> mem k names && keys_within t names

[@@ noextract_to "FSharp"]
let rec keys_within_snoc (#num #flt: eqtype) (ws: list (string & W.jval num flt)) (names: list string)
  (name: string) (j: W.jval num flt)
  : Lemma (requires keys_within ws names) (ensures keys_within (app ws [(name, j)]) (app names [name])) =
  mem_app name names [name];
  match ws with
  | [] -> ()
  | (k, _) :: t -> mem_app k names [name]; keys_within_snoc t names name j

[@@ noextract_to "FSharp"]
let rec keys_within_extend (#num #flt: eqtype) (ws: list (string & W.jval num flt)) (names: list string) (name: string)
  : Lemma (requires keys_within ws names) (ensures keys_within ws (app names [name]) /\ keys_within ws (name :: names)) =
  match ws with
  | [] -> ()
  | (k, _) :: t -> mem_app k names [name]; keys_within_extend t names name

[@@ noextract_to "FSharp"]
let rec memb_outside (#num #flt: eqtype) (ws: list (string & W.jval num flt)) (names: list string) (n: string)
  : Lemma (requires keys_within ws names /\ not (mem n names)) (ensures memb n ws == None) =
  match ws with
  | [] -> ()
  | _ :: t -> memb_outside t names n

[@@ noextract_to "FSharp"]
let rec memb_snoc (#num #flt: eqtype) (ws: list (string & W.jval num flt)) (name n: string) (j: W.jval num flt)
  : Lemma (ensures memb n (app ws [(name, j)]) ==
                   (match memb n ws with Some v -> Some v | None -> if name = n then Some j else None)) =
  match ws with
  | [] -> ()
  | _ :: t -> memb_snoc t name n j

[@@ noextract_to "FSharp"]
let rec undeclared_nil (#num #flt: eqtype) (names: list string) (ws: list (string & W.jval num flt))
  : Lemma (requires keys_within ws names) (ensures undeclared_go names ws == []) =
  match ws with
  | [] -> ()
  | _ :: t -> undeclared_nil names t

(* ---- the record invariant ---- *)

[@@ noextract_to "FSharp"]
let agree_on (#num #flt: eqtype) (names: list string) (ms ws: list (string & W.jval num flt)) : prop =
  forall (n: string). {:pattern (memb n ms)} mem n names ==> memb n ms == memb n ws

[@@ noextract_to "FSharp"]
let reads_back (#num #flt: eqtype) (#r #c: Type0) (fs: fields num flt r c) (k: r -> c) (x: r)
  (ws: list (string & W.jval num flt)) : prop =
  forall (ms: list (string & W.jval num flt)). {:pattern (fs.read_fields ms)}
    agree_on fs.names ms ws ==> fs.read_fields ms == Ok (k x)

[@@ noextract_to "FSharp"]
let fields_rt (#num #flt: eqtype) (#r #c: Type0) (fs: fields num flt r c) (k: r -> c) : prop =
  forall (x: r). {:pattern (fs.write_fields x)}
    (match fs.write_fields x with
     | Ok ws -> keys_within ws fs.names /\ (no_dups fs.names ==> reads_back fs k x ws)
     | Error _ -> True)

[@@ noextract_to "FSharp"]
let fields_rt_elim (#num #flt: eqtype) (#r #c: Type0) (fs: fields num flt r c) (k: r -> c) (x: r)
  (ws: list (string & W.jval num flt))
  : Lemma (requires fields_rt fs k /\ fs.write_fields x == Ok ws)
          (ensures keys_within ws fs.names /\ (no_dups fs.names ==> reads_back fs k x ws)) = ()

[@@ noextract_to "FSharp"]
let reads_back_elim (#num #flt: eqtype) (#r #c: Type0) (fs: fields num flt r c) (k: r -> c) (x: r)
  (ws ms: list (string & W.jval num flt))
  : Lemma (requires reads_back fs k x ws /\ agree_on fs.names ms ws) (ensures fs.read_fields ms == Ok (k x)) = ()

(* Agreement on `names @ [name]` with `before @ [(name, j)]`, `name` outside `names`, is agreement
   on `names` with `before` and the member `name` read as `j`. *)
[@@ noextract_to "FSharp"]
let agree_shrink (#num #flt: eqtype) (names: list string) (name: string)
  (ms before: list (string & W.jval num flt)) (j: W.jval num flt)
  : Lemma (requires agree_on (app names [name]) ms (app before [(name, j)]) /\ not (mem name names) /\
                    keys_within before names)
          (ensures agree_on names ms before /\ memb name ms == Some j) =
  mem_app name names [name];
  memb_snoc before name name j;
  memb_outside before names name;
  introduce forall (n: string). mem n names ==> memb n ms == memb n before
  with introduce _ ==> _
  with (mem_app n names [name]; memb_snoc before name n j)

let record_round_trip (#num #flt: eqtype) (#r #c: Type0) (ctor: c)
  : Lemma (fields_rt (c_record #num #flt #r ctor) (fun _ -> ctor)) = ()

let field_round_trip (#num #flt: eqtype) (#r #a #c: Type0) (name: string) (get: r -> a) (cd: codec num flt a)
  (fs: fields num flt r (a -> c)) (k: r -> a -> c)
  : Lemma (requires fields_rt fs k /\ rt cd) (ensures fields_rt (c_field name get cd fs) (fun x -> k x (get x))) =
  let f = c_field name get cd fs in
  let k' : r -> c = fun x -> k x (get x) in
  introduce forall (x: r).
    (match f.write_fields x with
     | Ok ws -> keys_within ws f.names /\ (no_dups f.names ==> reads_back f k' x ws)
     | Error _ -> True)
  with begin
    match fs.write_fields x with
    | Ok before ->
        (match cd.write (get x) with
         | Ok j ->
             let ws = app before [(name, j)] in
             fields_rt_elim fs k x before;
             keys_within_snoc before fs.names name j;
             rt_elim cd (get x) j;
             introduce no_dups f.names ==> reads_back f k' x ws
             with begin
               no_dups_snoc fs.names name;
               introduce forall (ms: list (string & W.jval num flt)). agree_on f.names ms ws ==> f.read_fields ms == Ok (k' x)
               with introduce _ ==> _
               with begin
                 agree_shrink fs.names name ms before j;
                 reads_back_elim fs k x before ms
               end
             end
         | Error _ -> ())
    | Error _ -> ()
  end

let opt_field_round_trip (#num #flt: eqtype) (#r #a #c: Type0) (name: string) (get: r -> option a)
  (cd: codec num flt a) (fs: fields num flt r (option a -> c)) (k: r -> option a -> c)
  : Lemma (requires fields_rt fs k /\ rt cd) (ensures fields_rt (c_opt_field name get cd fs) (fun x -> k x (get x))) =
  let f = c_opt_field name get cd fs in
  let k' : r -> c = fun x -> k x (get x) in
  introduce forall (x: r).
    (match f.write_fields x with
     | Ok ws -> keys_within ws f.names /\ (no_dups f.names ==> reads_back f k' x ws)
     | Error _ -> True)
  with begin
    match get x with
    | Some y ->
        (match fs.write_fields x with
         | Ok before ->
             (match cd.write y with
              | Ok j ->
                  let ws = app before [(name, j)] in
                  fields_rt_elim fs k x before;
                  keys_within_snoc before fs.names name j;
                  rt_elim cd y j;
                  introduce no_dups f.names ==> reads_back f k' x ws
                  with begin
                    no_dups_snoc fs.names name;
                    introduce forall (ms: list (string & W.jval num flt)). agree_on f.names ms ws ==> f.read_fields ms == Ok (k' x)
                    with introduce _ ==> _
                    with begin
                      agree_shrink fs.names name ms before j;
                      reads_back_elim fs k x before ms
                    end
                  end
              | Error _ -> ())
         | Error _ -> ())
    | None ->
        (match fs.write_fields x with
         | Ok ws ->
             fields_rt_elim fs k x ws;
             keys_within_extend ws fs.names name;
             introduce no_dups f.names ==> reads_back f k' x ws
             with begin
               no_dups_snoc fs.names name;
               memb_outside ws fs.names name;
               introduce forall (ms: list (string & W.jval num flt)). agree_on f.names ms ws ==> f.read_fields ms == Ok (k' x)
               with introduce _ ==> _
               with begin
                 mem_app name fs.names [name];
                 introduce forall (n: string). mem n fs.names ==> memb n ms == memb n ws
                 with introduce _ ==> _
                 with mem_app n fs.names [name];
                 reads_back_elim fs k x ws ms
               end
             end
         | Error _ -> ())
  end

(* COMPOSED ROUND TRIP, for a record: `build` answers `Ok` only for a declaration naming no member
   twice, and over such a declaration the constructor applied to the getters (`k`) being the
   identity is all the round trip needs. *)
let build_round_trip (#num #flt: eqtype) (#r: Type0) (fs: fields num flt r r) (k: r -> r)
  : Lemma (requires fields_rt fs k /\ (forall (x: r). k x == x))
          (ensures (match c_build fs with Ok c -> rt c | Error _ -> True)) =
  match c_build fs with
  | Ok c ->
      repeats_nil fs.names;
      introduce forall (v: r). (match c.write v with Ok j -> c.read_all j == Ok v | Error _ -> True)
      with begin
        match fs.write_fields v with
        | Ok ws ->
            fields_rt_elim fs k v ws;
            undeclared_nil fs.names ws;
            reads_back_elim fs k v ws ws
        | Error _ -> ()
      end
  | Error _ -> ()

(* ---- unions ---- *)

[@@ noextract_to "FSharp"]
let case_rt (#num #flt: eqtype) (#t: Type0) (cc: ccase num flt t) : prop =
  forall (v: t). {:pattern (cc.write_case v)}
    (match cc.write_case v with
     | Some (Ok ws) ->
         keys_within ws cc.case_names /\
         (no_dups cc.case_names ==>
            (forall (ms: list (string & W.jval num flt)). {:pattern (cc.read_case ms)}
               agree_on cc.case_names ms ws ==> cc.read_case ms == Ok v))
     | _ -> True)

let case_round_trip (#num #flt: eqtype) (#t #p: Type0) (tag: string) (project: t -> option p) (inject: p -> t)
  (fs: fields num flt p p) (k: p -> p)
  : Lemma (requires fields_rt fs k /\ (forall (x: p). k x == x) /\
                    (forall (v: t) (x: p). project v == Some x ==> inject x == v))
          (ensures case_rt (c_case tag project inject fs)) =
  let cc = c_case tag project inject fs in
  introduce forall (v: t).
    (match cc.write_case v with
     | Some (Ok ws) ->
         keys_within ws cc.case_names /\
         (no_dups cc.case_names ==>
            (forall (ms: list (string & W.jval num flt)). agree_on cc.case_names ms ws ==> cc.read_case ms == Ok v))
     | _ -> True)
  with begin
    match project v with
    | Some x ->
        (match fs.write_fields x with
         | Ok ws ->
             fields_rt_elim fs k x ws;
             introduce no_dups cc.case_names ==>
               (forall (ms: list (string & W.jval num flt)). agree_on cc.case_names ms ws ==> cc.read_case ms == Ok v)
             with begin
               introduce forall (ms: list (string & W.jval num flt)). agree_on cc.case_names ms ws ==> cc.read_case ms == Ok v
               with introduce _ ==> _
               with reads_back_elim fs k x ws ms
             end
         | Error _ -> ())
    | None -> ()
  end

[@@ noextract_to "FSharp"]
let rec cases_rt (#num #flt: eqtype) (#t: Type0) (cases: list (ccase num flt t)) : prop =
  match cases with
  | [] -> True
  | c :: rest -> case_rt c /\ cases_rt rest

[@@ noextract_to "FSharp"]
let rec cases_ok (#num #flt: eqtype) (#t: Type0) (key: string) (cases: list (ccase num flt t)) : Tot bool =
  match cases with
  | [] -> true
  | c :: rest -> no_dups c.case_names && not (mem key c.case_names) && cases_ok key rest

[@@ noextract_to "FSharp"]
let app_nil_inv (#a: Type0) (l1 l2: list a)
  : Lemma (requires app l1 l2 == []) (ensures l1 == [] /\ l2 == []) =
  match l1 with [] -> () | _ -> ()

[@@ noextract_to "FSharp"]
let rec union_faults_nil_names (#num #flt: eqtype) (#t: Type0) (key: string) (cases: list (ccase num flt t))
  : Lemma (requires concat_map (fun (c: ccase num flt t) -> map_l (fun n -> RepeatedMember n (Some c.ctag)) (repeats c.case_names)) cases == [] /\
                    concat_map (fun (c: ccase num flt t) -> if mem key c.case_names then [DiscriminatorAsMember c.ctag key] else []) cases == [])
          (ensures cases_ok key cases) =
  match cases with
  | [] -> ()
  | c :: rest ->
      app_nil_inv (map_l (fun n -> RepeatedMember n (Some c.ctag)) (repeats c.case_names))
        (concat_map (fun (c: ccase num flt t) -> map_l (fun n -> RepeatedMember n (Some c.ctag)) (repeats c.case_names)) rest);
      app_nil_inv (if mem key c.case_names then [DiscriminatorAsMember c.ctag key] else [])
        (concat_map (fun (c: ccase num flt t) -> if mem key c.case_names then [DiscriminatorAsMember c.ctag key] else []) rest);
      repeats_nil c.case_names;
      union_faults_nil_names key rest

[@@ noextract_to "FSharp"]
let union_faults_nil (#num #flt: eqtype) (#t: Type0) (key: string) (cases: list (ccase num flt t))
  : Lemma (requires union_faults key cases == []) (ensures no_dups (tags cases) /\ cases_ok key cases) =
  let a = concat_map (fun (c: ccase num flt t) -> map_l (fun n -> RepeatedMember n (Some c.ctag)) (repeats c.case_names)) cases in
  let b = map_l (fun g -> RepeatedTag g) (repeats (tags cases)) in
  let c = concat_map (fun (c: ccase num flt t) -> if mem key c.case_names then [DiscriminatorAsMember c.ctag key] else []) cases in
  app_nil_inv a (app b c);
  app_nil_inv b c;
  repeats_nil (tags cases);
  union_faults_nil_names key cases

(* What a written union value looks like: an object whose FIRST member is the discriminator,
   spelling the tag of a case of the declaration. *)
[@@ noextract_to "FSharp"]
let rec written_tag (#num #flt: eqtype) (#t: Type0) (key: string) (cases: list (ccase num flt t)) (v: t) (j: W.jval num flt)
  : Lemma (requires try_write_cases key cases v == Some (Ok j))
          (ensures (match j with
                    | W.JObj ((k0, W.JStr tg) :: _) -> k0 = key /\ mem tg (tags cases)
                    | _ -> False)) =
  match cases with
  | c :: rest ->
      (match c.write_case v with
       | Some _ -> ()
       | None ->
           written_tag key rest v j;
           (match j with
            | W.JObj ((_, W.JStr tg) :: _) -> assert (mem tg (tags rest)); assert (tags cases == c.ctag :: tags rest)
            | _ -> ()))

[@@ noextract_to "FSharp"]
let rec union_read_rt (#num #flt: eqtype) (#t: Type0) (key: string) (cases: list (ccase num flt t)) (v: t) (j: W.jval num flt)
  : Lemma (requires no_dups (tags cases) /\ cases_ok key cases /\ cases_rt cases /\ try_write_cases key cases v == Some (Ok j))
          (ensures union_read key cases j == Ok v) =
  match cases with
  | c :: rest ->
      (match c.write_case v with
       | Some (Ok ws) ->
           assert (j == W.JObj ((key, W.JStr c.ctag) :: ws));
           assert (tag_dispatch key (case_table cases) j == Ok c);
           assert (keys_within ws c.case_names);
           keys_within_extend ws c.case_names key;
           undeclared_nil (key :: c.case_names) ws;
           assert (undeclared (key :: c.case_names) j == []);
           introduce forall (n: string). mem n c.case_names ==> memb n ((key, W.JStr c.ctag) :: ws) == memb n ws
           with introduce _ ==> _
           with ();
           assert (c.read_case ((key, W.JStr c.ctag) :: ws) == Ok v)
       | Some (Error _) -> ()
       | None ->
           written_tag key rest v j;
           union_read_rt key rest v j;
           (match j with
            | W.JObj ((_, W.JStr tg) :: _) ->
                assert (c.ctag <> tg);
                assert (tag_dispatch key (case_table cases) j == tag_dispatch key (case_table rest) j)
            | _ -> ()))

(* COMPOSED ROUND TRIP, for a union: `union` answers `Ok` only when no case repeats a member, no
   two cases share a tag and no case declares the discriminator — and those three facts, with each
   case round-tripping, are the whole proof. *)
let union_round_trip (#num #flt: eqtype) (#t: Type0) (key: string) (cases: list (ccase num flt t))
  : Lemma (requires cases_rt cases) (ensures (match c_union key cases with Ok c -> rt c | Error _ -> True)) =
  match c_union key cases with
  | Ok c ->
      union_faults_nil key cases;
      introduce forall (v: t). (match c.write v with Ok j -> c.read_all j == Ok v | Error _ -> True)
      with (match try_write_cases key cases v with
            | Some (Ok j) -> union_read_rt key cases v j
            | _ -> ())
  | Error _ -> ()

(* ---- the leaves, the list, the enumeration and the two isomorphisms ---- *)

let json_round_trip (#num #flt: eqtype) : Lemma (rt (c_json #num #flt)) = ()

let string_round_trip (#num #flt: eqtype) : Lemma (rt (c_string #num #flt)) = ()

let int_round_trip (#num #flt: eqtype) : Lemma (rt (c_int #num #flt)) = ()

let float_round_trip (#num #flt: eqtype) (to_flt: num -> flt) : Lemma (rt (c_float #num #flt to_flt)) = ()

let bool_round_trip (#num #flt: eqtype) : Lemma (rt (c_bool #num #flt)) = ()

(* `ofDecoder` and `make` take the caller's two faces; their agreement is the caller's
   (`decode-layer-round-trip-premises`). *)
let of_decoder_round_trip (#num #flt: eqtype) (#a: Type0) (w: a -> W.jval num flt) (d: dec num flt a)
  : Lemma (requires forall (v: a). d (w v) == Ok v) (ensures rt (c_of_decoder w d)) = ()

let make_round_trip (#num #flt: eqtype) (#a: Type0) (w: a -> W.jval num flt) (r: W.jval num flt -> result a (list derr))
  : Lemma (requires forall (v: a). r (w v) == Ok v) (ensures rt (c_make w r)) = ()

[@@ noextract_to "FSharp"]
let rec find_spelling_mem (#a: eqtype) (v: a) (cases: list (string & a)) (s: string)
  : Lemma (requires find_spelling v cases == Some s) (ensures mem s (keys cases)) =
  match cases with
  | (_, x) :: t -> if x = v then () else find_spelling_mem v t s

[@@ noextract_to "FSharp"]
let rec find_spelling_case (#a: eqtype) (v: a) (cases: list (string & a)) (s: string)
  : Lemma (requires no_dups (keys cases) /\ find_spelling v cases == Some s) (ensures find_case s cases == Some v) =
  match cases with
  | (_, x) :: t -> if x = v then () else (find_spelling_mem v t s; find_spelling_case v t s)

(* An enumeration `enum` admits — no spelling twice — reads every value back from the FIRST
   spelling it writes. *)
let enum_round_trip (#num #flt: eqtype) (#a: eqtype) (cases: list (string & a))
  : Lemma (match c_enum #num #flt cases with Ok c -> rt c | Error _ -> True) =
  match c_enum #num #flt cases with
  | Ok c ->
      repeats_nil (keys cases);
      introduce forall (v: a). (match c.write v with Ok j -> c.read_all j == Ok v | Error _ -> True)
      with (match find_spelling v cases with
            | Some s -> find_spelling_case v cases s
            | None -> ())
  | Error _ -> ()

(* `traverse`'s accumulator loop is the direct recursion, reversed onto its accumulator. *)
[@@ noextract_to "FSharp"]
let rec trav_d (#a #b: Type0) (w: int -> a -> result b fault) (i: int) (xs: list a)
  : Tot (result (list b) fault) (decreases xs) =
  match xs with
  | [] -> Ok []
  | x :: rest ->
      (match w i x with
       | Ok y -> (match trav_d w (i + 1) rest with Ok ys -> Ok (y :: ys) | Error f -> Error f)
       | Error f -> Error f)

[@@ noextract_to "FSharp"]
let rec traverse_go_eq (#a #b: Type0) (w: int -> a -> result b fault) (i: int) (acc: list b) (xs: list a)
  : Lemma (ensures traverse_go w i acc xs ==
                   (match trav_d w i xs with Ok ys -> Ok (app (W.rev acc) ys) | Error f -> Error f)) (decreases xs) =
  match xs with
  | [] -> app_nil_r (W.rev acc)
  | x :: rest ->
      (match w i x with
       | Ok y ->
           traverse_go_eq w (i + 1) (y :: acc) rest;
           rev_cons y acc;
           (match trav_d w (i + 1) rest with
            | Ok ys -> app_assoc (W.rev acc) [y] ys
            | Error _ -> ())
       | Error _ -> ())

[@@ noextract_to "FSharp"]
let rec list_items_rt (#num #flt: eqtype) (#a: Type0) (c: codec num flt a) (i: int) (xs: list a) (js: list (W.jval num flt))
  : Lemma (requires rt c /\ trav_d (item_write c) i xs == Ok js)
          (ensures collect_errors (mapi_all c.read_all i js) == [] /\ oks_of (mapi_all c.read_all i js) == xs) (decreases xs) =
  match xs with
  | [] -> ()
  | x :: rest ->
      (match c.write x with
       | Ok y ->
           rt_elim c x y;
           (match trav_d (item_write c) (i + 1) rest with
            | Ok ys -> list_items_rt c (i + 1) rest ys
            | Error _ -> ())
       | Error _ -> ())

let list_round_trip (#num #flt: eqtype) (#a: Type0) (c: codec num flt a)
  : Lemma (requires rt c) (ensures rt (c_list c)) =
  introduce forall (xs: list a). (match (c_list c).write xs with Ok j -> (c_list c).read_all j == Ok xs | Error _ -> True)
  with begin
    traverse_go_eq (item_write c) 0 [] xs;
    match trav_d (item_write c) 0 xs with
    | Ok js -> list_items_rt c 0 xs js
    | Error _ -> ()
  end

(* `map`'s premise: `there` inverts `back`. *)
let map_round_trip (#num #flt: eqtype) (#a #b: Type0) (there: a -> b) (back: b -> a) (c: codec num flt a)
  : Lemma (requires rt c /\ (forall (u: b). there (back u) == u)) (ensures rt (c_map there back c)) =
  introduce forall (u: b). (match (c_map there back c).write u with Ok j -> (c_map there back c).read_all j == Ok u | Error _ -> True)
  with (match c.write (back u) with Ok j -> rt_elim c (back u) j | Error _ -> ())

(* `refine`'s premise: `check` admits every value `back` writes — the refinement predicate holds on
   the representation of every refined value. *)
let refine_round_trip (#num #flt: eqtype) (#a #b: Type0) (expected: string) (check: a -> result b string)
  (back: b -> a) (c: codec num flt a)
  : Lemma (requires rt c /\ (forall (u: b). check (back u) == Ok u)) (ensures rt (c_refine expected check back c)) =
  let cr = c_refine expected check back c in
  introduce forall (u: b). (match cr.write u with Ok j -> cr.read_all j == Ok u | Error _ -> True)
  with (match c.write (back u) with Ok j -> rt_elim c (back u) j | Error _ -> ())

(* ======================================================================================
   9. WHAT THEOREM 3 WAS NOT. Phase 436's three counter-examples, each now a statement about a
      refusal production can no longer mint: the two `RootDecodeCode` keeps out of `fail`
      (Phase 443), and the pathed refusal it keeps out of `andThen`'s function (Phase 453).
   ====================================================================================== *)

(* A `MissingField` at the EMPTY path names no member: it resolves in no document at all. Until
   Phase 443 `Decoder.fail MissingField` answered exactly this refusal (Phase 436's
   `fail_missing_never_resolves`); `fail` now takes a `RootDecodeCode`, which has no such case
   (`root_excludes`), and a `MissingField` is raised by `Decoder.field`, whose path names the
   member (`ok_missing`). *)
let root_missing_never_resolves (#num #flt: eqtype) (x: W.jval num flt)
  : Lemma (~(resolves_in x (make MissingField))) = ()

(* An `InvalidJson` resolves at the root and stops resolving one member down, which is why
   `ok_at` excludes the parser's code. Until Phase 443 `Decoder.fail InvalidJson` raised it at
   any value a combinator reaches (Phase 436's `fail_invalid_json_stops_resolving`); now only the
   parse entry points raise it, at the root of the text they parse. `raw` below is the decoder
   `fail InvalidJson` used to be, written without `fail` because `fail` no longer takes the code. *)
let root_invalid_json_stops_resolving (#num #flt: eqtype) (#a: Type0) (k: string) (x: W.jval num flt)
  : Lemma (let raw : dec num flt a = fun _ -> Error (make InvalidJson) in
           resolves_in x (make InvalidJson) /\
           field k raw (W.JObj [(k, x)]) == Error (under (Key k) (make InvalidJson)) /\
           ~(resolves_in (W.JObj [(k, x)]) (under (Key k) (make InvalidJson)))) = ()

(* Until Phase 453 `Decoder.andThen` raised its function's refusal UNTOUCHED, so a function that
   refused with a path of its own refused wherever that path happened not to resolve: here, a
   `MissingField` naming a member the object DOES carry (Phase 436's counter-example). `andThen`'s
   function now answers a `root_code`, which carries no path and neither unsound code, so the
   refusal below can no longer be handed to it (`and_then_sound` has no premise). `raw` is the
   combinator `andThen` used to be, written out because `and_then` no longer takes such a
   function; a check that must point inside the value is `bind` to `at <path> (fail c)`, which
   walks the path in the document (`at_sound`). *)
let and_then_needs_root_refusals (#num #flt: eqtype) (k: string) (x: W.jval num flt)
  : Lemma (let raw (g: W.jval num flt -> result unit derr) (d: dec num flt (W.jval num flt)) : dec num flt unit =
             fun el -> (match d el with Ok v -> g v | Error e -> Error e) in
           raw (fun _ -> Error (missing k)) json (W.JObj [(k, x)]) == Error (missing k) /\
           ~(resolves_in (W.JObj [(k, x)]) (missing k))) = ()

(* The three, over ANY carrier (Phase 658): the first needs nothing of the carrier, the other two a
   value its view presents as a one-member object — at the document, a `DObj` (`JsonDocument.fst`). *)
let root_missing_never_resolves_over (#v: Type0) (c: carrier v) (x: v)
  : Lemma (~(resolves_in_over c x (make MissingField))) = ()

let root_invalid_json_stops_resolving_over (#v #a: Type0) (c: carrier v) (k: string) (x o: v)
  : Lemma (requires c.members_of o == Some [(k, x)])
          (ensures (let raw : dec_over v a = fun _ -> Error (make InvalidJson) in
                    resolves_in_over c x (make InvalidJson) /\
                    field_over c k raw o == Error (under (Key k) (make InvalidJson)) /\
                    ~(resolves_in_over c o (under (Key k) (make InvalidJson))))) = ()

let and_then_needs_root_refusals_over (#v: Type0) (c: carrier v) (k: string) (x o: v)
  : Lemma (requires c.members_of o == Some [(k, x)])
          (ensures (let raw (g: v -> result unit derr) (d: dec_over v v) : dec_over v unit =
                      fun el -> (match d el with Ok y -> g y | Error e -> Error e) in
                    raw (fun _ -> Error (missing k)) (fun el -> Ok el) o == Error (missing k) /\
                    ~(resolves_in_over c o (missing k)))) = ()

(* ======================================================================================
   10. TWINS (Phase 309) — the extractor premise, sampled at this model. See `WireDecode.fst`'s
       twins section for what a twin is: a closure the normaliser discharges to `true` under the
       model's own semantics, which the oracle host then runs as extracted F#.
   ====================================================================================== *)

type twin_pt = { px: string; py: option int }

let twin_pt_fields : fields int bool twin_pt twin_pt =
  c_opt_field "y" (fun (p: twin_pt) -> p.py) c_int
    (c_field "x" (fun (p: twin_pt) -> p.px) c_string
       (c_record (fun (x: string) (y: option int) -> { px = x; py = y })))

type twin_shape =
  | Circle : r:int -> twin_shape
  | Square : s:int -> twin_shape

let twin_payload : fields int bool int int = c_field "size" (fun (n: int) -> n) c_int (c_record (fun (n: int) -> n))

(* Named rather than written inline: the F# backend lays a multi-line lambda out so that the list
   cons after it no longer parses. *)
let twin_project_circle (v: twin_shape) : option int = match v with Circle r -> Some r | _ -> None
let twin_project_square (v: twin_shape) : option int = match v with Square s -> Some s | _ -> None

let twin_shapes : list (ccase int bool twin_shape) = [
  c_case "circle" twin_project_circle (fun (r: int) -> Circle r) twin_payload;
  c_case "square" twin_project_square (fun (s: int) -> Square s) twin_payload ]

let twin_doc : W.jval int bool = W.JObj [ ("a", W.JStr "x"); ("b", W.JArr [ W.JInt 1; W.JStr "y" ]) ]



(* Each fixture that matches is a named function: the F# backend lays a `match` inside a list
   element's lambda out so that the cons after it no longer parses. *)
let twin_record_round_trips () : bool =
  match c_build twin_pt_fields with
  | Ok c ->
      (match c.write ({ px = "p"; py = Some 3 }) with
       | Ok j -> c.read_all j = Ok ({ px = "p"; py = Some 3 })
       | Error _ -> false)
  | Error _ -> false

let twin_record_collects () : bool =
  match c_build twin_pt_fields with
  | Ok c ->
      c.read_all (W.JObj [ ("x", W.JInt 1); ("y", W.JStr "s"); ("z", W.JBool true) ])
      = Error [ { code = UndeclaredMember; path = [ Key "z" ] };
                { code = WrongKind; path = [ Key "x" ] };
                { code = WrongKind; path = [ Key "y" ] } ]
  | Error _ -> false

let twin_union_round_trips () : bool =
  match c_union "kind" twin_shapes with
  | Ok c ->
      c.write (Square 2) = Ok (W.JObj [ ("kind", W.JStr "square"); ("size", W.JInt 2) ])
      && c.read_all (W.JObj [ ("kind", W.JStr "square"); ("size", W.JInt 2) ]) = Ok (Square 2)
  | Error _ -> false

let twin_union_unknown_tag () : bool =
  match c_union "kind" twin_shapes with
  | Ok c -> c.read_all (W.JObj [ ("kind", W.JStr "tri") ]) = Error [ { code = UnknownTag; path = [ Key "kind" ] } ]
  | Error _ -> false

let twin_enum_repeated () : bool =
  match c_enum #int #bool [ ("a", 1); ("a", 2); ("b", 3) ] with
  | Ok _ -> false
  | Error fs -> fs = [ RepeatedSpelling "a" ]

let twin_union_discriminator () : bool =
  match c_union "size" twin_shapes with
  | Ok _ -> false
  | Error fs -> fs = [ DiscriminatorAsMember "circle" "size"; DiscriminatorAsMember "square" "size" ]

let twins : list twin = [
  { tname = "field-refuses-an-item-under-its-member-and-index";
    tholds = (fun () -> field "b" (list_d int_d) twin_doc = Error ({ code = WrongKind; path = [ Key "b"; Index 1 ] })) };
  { tname = "the-item-refusal-resolves";
    tholds = (fun () -> resolves_in twin_doc ({ code = WrongKind; path = [ Key "b"; Index 1 ] })) };
  { tname = "missing-names-the-absent-member-and-resolves";
    tholds = (fun () -> field "c" str twin_doc = Error (missing "c") && resolves_in twin_doc (missing "c")) };
  { tname = "missing-of-a-present-member-does-not-resolve";
    tholds = (fun () -> not (resolves_in twin_doc (missing "a"))) };
  { tname = "at-walks-into-an-item";
    tholds = (fun () -> at [ Key "b"; Index 0 ] int_d twin_doc = Ok 1) };
  { tname = "record-round-trips"; tholds = twin_record_round_trips };
  { tname = "record-collects-every-defect-undeclared-first"; tholds = twin_record_collects };
  { tname = "union-writes-its-tag-first-and-reads-back"; tholds = twin_union_round_trips };
  { tname = "union-refuses-an-unknown-tag-at-the-discriminator"; tholds = twin_union_unknown_tag };
  { tname = "enum-refuses-a-repeated-spelling"; tholds = twin_enum_repeated };
  { tname = "union-refuses-the-discriminator-as-a-member"; tholds = twin_union_discriminator } ]

let _ = assert_norm (twins_hold twins == true)
