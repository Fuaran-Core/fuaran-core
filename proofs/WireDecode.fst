(*
   WireDecode — an F* model of Fuaran.Core's wire DECODE combinators, with decoder totality as a
   machine-checked theorem (fuaran-core Phase 135 — the attested-stack programme's theorem 1).

   WHAT IS MODELLED. `Fuaran.Core.Decode` (src/Fuaran.Core.Wire/Wire.fs), clause for clause:
   `getProp`, `asString` / `asInt` / `asBool` / `asFloat`, `kindOf`, `strField` / `intField`, and
   `mapList` — the array walker, in its accumulator-and-`rev` form, which is what production is.
   On top of them a REFERENCE VOCABULARY (`rnode`) with its encoder and the kind-dispatch node
   decoder a domain writes: read the `"kind"` tag, match it, read the declared members, walk the
   child array. And, separately, the Phase 102 read policy — see the boundary note below.

   WHAT IS PROVED.
     - `decode_total` — every combinator returns `Ok` or `Error` for EVERY input, never diverges
       and never throws, and WHICH outcome it returns is characterised structurally, so the
       failure classification is exhaustive rather than merely non-empty.
     - `decode_node_total` / `decode_node_wf` — the recursive node decoder is `Tot` on an
       arbitrary `jval` (its termination is the content: see the measure note below), and it
       succeeds on exactly the well-formed documents, `wf`.
     - `decode_encode_roundtrip` — `decode_node (encode n) == Ok n` for every `rnode`.
     - `lenient_agrees_off_policy` / `strict_unchanged_on_null_free` — the Phase 102 promise.

   WHAT IS NOT MODELLED, AND WHY — the theorem's boundary.

     - `Json.parse`, the string-to-`JVal` parser, is OUT OF SCOPE. Its totality is a property of a
       recursive-descent parser over bytes — the depth bound, escape handling, the int53 token
       guard, `MaxDepthExceeded` and `TrailingCharacters` — and is a separate phase. Everything
       here begins at a `JVal` that already exists. `Decode.parse` / `Decode.parseTolerantOfNull`
       are one-line delegates to it and are not modelled either.

     - THE PHASE 102 POLICY IS NOT A COMBINATOR PARAMETER, because in the shipped tree it is not
       one. `NullPolicy` is a parameter of `Json.parseDetailedWithPolicy` and of nothing else, and
       `JVal` has no null constructor, so no combinator can see a null. What the policy IS, at the
       layer this theorem is about, is a DOCUMENT-LEVEL READ NORMALISATION upstream of every
       combinator: erase object-member nulls, refuse a null that has no absence to erase it to.
       Section 7 models it there — `read : null_policy -> jvaln -> outcome jval`, from a document
       model that HAS a null into the wire model that does not, which is the type-level form of
       "tolerance is a read normalisation, never a new emission" (Wire.fs, `NullPolicy`'s doc).
       What section 7 therefore assumes, and states rather than hides, is that the parser's
       member-null absorption is equivalent to erasing member nulls from the document tree the
       strict grammar would otherwise produce. The near-miss tokens that make that an assumption
       rather than a theorem (`nul`, `nullish`) are grammar, and stay with `Json.parse`.

     - `Versioning.decodeTolerant` — the shipped GENERIC instance of the kind-dispatch pattern —
       is named here and not modelled: its `requiredProfile` read goes through
       `Versioning.Profile.tryParse`, string-splitting at a different layer. The pattern itself is
       modelled where a domain meets it, as `decode_node`.

     - THE NUMERIC PAYLOADS ARE OPAQUE. `jval` is parametric in `num` and `flt`, and `as_float`
       takes the widening `to_flt` as a parameter where F# writes `float i`. This is not a
       weakening: no combinator in `Decode` looks inside a number, it only moves one, so opaque
       carriers are the precise statement of what the layer does. It also keeps the extracted
       oracle dependent on `Prims` alone — F* has no F#-extractable float, and F*'s `int` is
       unbounded where .NET's is Int32.

   HOW TO READ IT. Every definition names its F# counterpart in the comment above it. Error
   MESSAGES are reproduced verbatim, not merely classified, because the differential host compares
   them: a model that agreed on `Ok`/`Error` alone would not notice a decoder that named the wrong
   expectation. Helpers are defined here rather than taken from `FStar.List.Tot` so that the
   extracted oracle depends on `Prims` alone.

   A NOTE ON THE MEASURE, because it is the theorem rather than an implementation detail. The node
   decoder recurses into a child array it obtained by NAME, so nothing structural is visible at the
   call site: `get_prop` therefore carries `Ok? r ==> jsize (Ok?.v r) < jsize el` in its RETURN
   TYPE, and that refinement is the whole termination argument. A `jval` is finite and a decoder
   that only ever descends into it cannot fail to stop — which is what "the decoder is total"
   means once the parser is out of scope.

   Apache-2.0, like everything beside it.
*)
module WireDecode

(* ======================================================================================
   0. The outcome (F#: `Result<'T, string>`, which every combinator in `Decode` returns —
      "so a failure NAMES what was expected", Wire.fs).
   ====================================================================================== *)

type outcome (a: Type) =
  | Ok    : v:a -> outcome a
  | Error : msg:string -> outcome a

(* F#: `Result.bind`, the `|>` the combinators are composed with. *)
let bind (#a #b: Type) (r: outcome a) (f: a -> outcome b) : Tot (outcome b) =
  match r with
  | Ok v -> f v
  | Error m -> Error m

(* ---- list helpers, self-contained so the extraction needs only `Prims` ---- *)

let rec rev_app (#a: Type) (l acc: list a) : Tot (list a) (decreases l) =
  match l with
  | [] -> acc
  | x :: t -> rev_app t (x :: acc)

(* F#: `List.rev` — the one `mapList` calls on its accumulator. *)
let rev (#a: Type) (l: list a) : Tot (list a) = rev_app l []

(* ======================================================================================
   1. The value model (F#: `JVal` in Wire.fs).

      Parametric in the two numeric carriers: see the header's note on opacity.
   ====================================================================================== *)

type jval (num flt: eqtype) =
  | JStr   : s:string -> jval num flt
  | JInt   : i:num -> jval num flt
  | JBool  : b:bool -> jval num flt
  | JFloat : f:flt -> jval num flt
  | JArr   : items:list (jval num flt) -> jval num flt
  | JObj   : fields:list (string & jval num flt) -> jval num flt

(* ======================================================================================
   2. The structural measure. PROOF-ONLY — erased at extraction, but the reason the
      recursive decoder below is accepted as `Tot` at all.
   ====================================================================================== *)

[@@ noextract_to "FSharp"]
let rec jsize (#num #flt: eqtype) (v: jval num flt) : Tot pos =
  match v with
  | JArr xs -> 1 + jsizes xs
  | JObj fs -> 1 + fsize fs
  | _ -> 1

and jsizes (#num #flt: eqtype) (xs: list (jval num flt)) : Tot nat =
  match xs with
  | [] -> 0
  | x :: t -> jsize x + jsizes t

and fsize (#num #flt: eqtype) (fs: list (string & jval num flt)) : Tot nat =
  match fs with
  | [] -> 0
  | (_, v) :: t -> jsize v + fsize t

(* The SMT solver reduces these on demand; the patterns spare every termination VC below the
   need to be told. Proof-only, like the measure itself. *)
[@@ noextract_to "FSharp"]
let jsize_at_least_one (#num #flt: eqtype) (v: jval num flt)
  : Lemma (ensures jsize v >= 1) [SMTPat (jsize v)] =
  match v with
  | JStr _ -> () | JInt _ -> () | JBool _ -> ()
  | JFloat _ -> () | JArr _ -> () | JObj _ -> ()

[@@ noextract_to "FSharp"]
let jsizes_cons (#num #flt: eqtype) (x: jval num flt) (t: list (jval num flt))
  : Lemma (ensures jsizes (x :: t) == jsize x + jsizes t) [SMTPat (jsizes (x :: t))] = ()

[@@ noextract_to "FSharp"]
let fsize_cons (#num #flt: eqtype) (k: string) (v: jval num flt) (t: list (string & jval num flt))
  : Lemma (ensures fsize ((k, v) :: t) == jsize v + fsize t) [SMTPat (fsize ((k, v) :: t))] = ()

(* ======================================================================================
   3. The combinators (F#: `module Decode`, Wire.fs), clause for clause.
   ====================================================================================== *)

(* F#: `Decode.kindName` — the word a failure names the actual shape with. *)
let kind_name (#num #flt: eqtype) (v: jval num flt) : Tot string =
  match v with
  | JStr _ -> "string"
  | JInt _ -> "int"
  | JBool _ -> "bool"
  | JFloat _ -> "float"
  | JArr _ -> "array"
  | JObj _ -> "object"

(* F#: the `List.tryFind` inside `getProp`, and its `None` arm's message. The refinement is
   what carries the subterm fact out of the lookup — see the header's note on the measure. *)
let rec find_field (#num #flt: eqtype) (name: string) (fs: list (string & jval num flt))
  : Tot (r: outcome (jval num flt) { Ok? r ==> jsize (Ok?.v r) <= fsize fs }) (decreases fs) =
  match fs with
  | [] -> Error ("missing property: " ^ name)
  | (k, v) :: t -> if k = name then Ok v else find_field name t

(* F#: `Decode.getProp`. *)
let get_prop (#num #flt: eqtype) (name: string) (el: jval num flt)
  : Tot (r: outcome (jval num flt) { Ok? r ==> jsize (Ok?.v r) < jsize el }) =
  match el with
  | JObj fields -> find_field name fields
  | other -> Error ("expected object, got " ^ kind_name other)

(* F#: `Decode.asString`. *)
let as_string (#num #flt: eqtype) (el: jval num flt) : Tot (outcome string) =
  match el with
  | JStr s -> Ok s
  | other -> Error ("expected string, got " ^ kind_name other)

(* F#: `Decode.asInt`. *)
let as_int (#num #flt: eqtype) (el: jval num flt) : Tot (outcome num) =
  match el with
  | JInt i -> Ok i
  | other -> Error ("expected int, got " ^ kind_name other)

(* F#: `Decode.asBool`. *)
let as_bool (#num #flt: eqtype) (el: jval num flt) : Tot (outcome bool) =
  match el with
  | JBool b -> Ok b
  | other -> Error ("expected bool, got " ^ kind_name other)

(* F#: `Decode.asFloat` — the numeric-normalisation clause. `to_flt` is F#'s `float i`; the
   model does not look inside a number (header, opacity). *)
let as_float (#num #flt: eqtype) (to_flt: num -> flt) (el: jval num flt) : Tot (outcome flt) =
  match el with
  | JFloat f -> Ok f
  | JInt i -> Ok (to_flt i)
  | other -> Error ("expected number, got " ^ kind_name other)

(* F#: `Decode.kindOf` — the discriminating `"kind"` tag of an object. *)
let kind_of (#num #flt: eqtype) (el: jval num flt) : Tot (outcome string) =
  bind (get_prop "kind" el) as_string

(* F#: `Decode.strField`. *)
let str_field (#num #flt: eqtype) (name: string) (el: jval num flt) : Tot (outcome string) =
  bind (get_prop name el) as_string

(* F#: `Decode.intField`. *)
let int_field (#num #flt: eqtype) (name: string) (el: jval num flt) : Tot (outcome num) =
  bind (get_prop name el) as_int

(* F#: the inner `go` of `Decode.mapList` — accumulate, short-circuit on the first error,
   `List.rev` at the end. Modelled in that form because that is the function production is. *)
let rec map_list_go (#num #flt: eqtype) (#t: Type)
  (d: jval num flt -> outcome t) (acc: list t) (xs: list (jval num flt))
  : Tot (outcome (list t)) (decreases xs) =
  match xs with
  | [] -> Ok (rev acc)
  | x :: rest ->
    (match d x with
     | Ok v -> map_list_go d (v :: acc) rest
     | Error m -> Error m)

(* F#: `Decode.mapList` — the array walker, and the only walker `Decode` has. *)
let map_list (#num #flt: eqtype) (#t: Type)
  (d: jval num flt -> outcome t) (el: jval num flt)
  : Tot (outcome (list t)) =
  match el with
  | JArr items -> map_list_go d [] items
  | other -> Error ("expected array, got " ^ kind_name other)

(* ======================================================================================
   4. A reference vocabulary and the kind-dispatch node decoder a domain builds from the
      combinators above (F#: the `kindObj` envelope of `Json.kindObj`, read back through
      `kindOf` + a match on the tag).

      One case per combinator: `RText` for `str_field`, `RFlag` for `get_prop` + `as_bool`,
      `RTags` for `map_list as_string` (the generic walker itself), `RGroup` for the
      recursive walk.
   ====================================================================================== *)

type rnode =
  | RText  : value:string -> rnode
  | RFlag  : on:bool -> rnode
  | RTags  : tags:list string -> rnode
  | RGroup : id:string -> items:list rnode -> rnode

(* The reference vocabulary's own measure — proof-only, and here for the same reason as
   `jsize`: it makes the two mutually-recursive round-trip lemmas below compare like with
   like instead of an `rnode` with a `list rnode`. *)
[@@ noextract_to "FSharp"]
let rec rsize (n: rnode) : Tot pos =
  match n with
  | RGroup _ items -> 1 + rsizes items
  | _ -> 1

and rsizes (ns: list rnode) : Tot nat =
  match ns with
  | [] -> 0
  | n :: t -> rsize n + rsizes t

[@@ noextract_to "FSharp"]
let rsize_at_least_one (n: rnode) : Lemma (ensures rsize n >= 1) [SMTPat (rsize n)] =
  match n with
  | RText _ -> () | RFlag _ -> () | RTags _ -> () | RGroup _ _ -> ()

[@@ noextract_to "FSharp"]
let rsizes_cons (n: rnode) (t: list rnode)
  : Lemma (ensures rsizes (n :: t) == rsize n + rsizes t) [SMTPat (rsizes (n :: t))] = ()

(* F#: `Json.kindObj tag fields` — the `"kind"` tag leads, the declared members follow in
   author order. *)
let rec encode (#num #flt: eqtype) (n: rnode) : Tot (jval num flt) (decreases n) =
  match n with
  | RText s -> JObj [("kind", JStr "text"); ("value", JStr s)]
  | RFlag b -> JObj [("kind", JStr "flag"); ("on", JBool b)]
  | RTags ts -> JObj [("kind", JStr "tags"); ("tags", JArr (encode_tags ts))]
  | RGroup id items ->
    JObj [("kind", JStr "group"); ("id", JStr id); ("items", JArr (encode_items items))]

and encode_tags (#num #flt: eqtype) (ts: list string) : Tot (list (jval num flt)) (decreases ts) =
  match ts with
  | [] -> []
  | s :: t -> JStr s :: encode_tags t

and encode_items (#num #flt: eqtype) (ns: list rnode) : Tot (list (jval num flt)) (decreases ns) =
  match ns with
  | [] -> []
  | n :: t -> encode n :: encode_items t

(* The node decoder. The `group` arm is written with explicit matches rather than `bind`
   because the refinement on `get_prop` — the termination argument — is not visible through a
   higher-order call, and `decode_items` is `map_list_go decode_node` INLINED for the same
   reason; `map_list_is_items` below proves the inlining costs no fidelity. *)
let rec decode_node (#num #flt: eqtype) (el: jval num flt)
  : Tot (outcome rnode) (decreases %[(jsize el <: nat); 0]) =
  match kind_of el with
  | Error m -> Error m
  | Ok tag ->
    if tag = "text" then
      (match str_field "value" el with
       | Error m -> Error m
       | Ok s -> Ok (RText s))
    else if tag = "flag" then
      (match get_prop "on" el with
       | Error m -> Error m
       | Ok v ->
         (match as_bool v with
          | Error m -> Error m
          | Ok b -> Ok (RFlag b)))
    else if tag = "tags" then
      (match get_prop "tags" el with
       | Error m -> Error m
       | Ok v ->
         (match map_list as_string v with
          | Error m -> Error m
          | Ok ts -> Ok (RTags ts)))
    else if tag = "group" then
      (match str_field "id" el with
       | Error m -> Error m
       | Ok id ->
         let r = get_prop "items" el in
         (match r with
          | Error m -> Error m
          | Ok v ->
            (match v with
             | JArr ys -> (match decode_items [] ys with
                           | Error m -> Error m
                           | Ok ns -> Ok (RGroup id ns))
             | other -> Error ("expected array, got " ^ kind_name other))))
    else Error ("unknown kind: " ^ tag)

and decode_items (#num #flt: eqtype) (acc: list rnode) (ys: list (jval num flt))
  : Tot (outcome (list rnode)) (decreases %[(jsizes ys <: nat); 1]) =
  match ys with
  | [] -> Ok (rev acc)
  | y :: rest ->
    (match decode_node y with
     | Ok n -> decode_items (n :: acc) rest
     | Error m -> Error m)

(* The inlined walk IS `mapList decodeNode`. *)
let rec map_list_is_items (#num #flt: eqtype) (acc: list rnode) (ys: list (jval num flt))
  : Lemma (ensures map_list_go (decode_node #num #flt) acc ys == decode_items acc ys)
          (decreases ys) =
  match ys with
  | [] -> ()
  | y :: rest ->
    (match decode_node y with
     | Ok n -> map_list_is_items (n :: acc) rest
     | Error _ -> ())

let map_list_decode_node (#num #flt: eqtype) (ys: list (jval num flt))
  : Lemma (ensures map_list (decode_node #num #flt) (JArr ys) == decode_items [] ys) =
  map_list_is_items [] ys

(* ======================================================================================
   5. THEOREM 1 — TOTALITY, and the exhaustive classification of the outcome.

      Totality of each combinator is carried by its `Tot` type: F* admits a definition only
      once it has shown the function is defined on every input of its domain and terminates,
      and `--report_assumes error` means nothing here is assumed. What a lemma adds is the
      CLASSIFICATION: which of the two outcomes each input reaches, decided structurally, so
      that "the failure classification is exhaustive" is a checked statement rather than an
      observation about a match having a catch-all arm.
   ====================================================================================== *)

(* Every combinator returns exactly one of `Ok` / `Error` — the two are exclusive and
   exhaustive — and which one is a structural property of the input. *)
let decode_total (#num #flt: eqtype) (to_flt: num -> flt) (name: string) (el: jval num flt)
  : Lemma (ensures
      (* exactly one outcome, per combinator *)
      (Ok? (as_string el) <==> ~(Error? (as_string el))) /\
      (Ok? (as_int el) <==> ~(Error? (as_int el))) /\
      (Ok? (as_bool el) <==> ~(Error? (as_bool el))) /\
      (Ok? (as_float to_flt el) <==> ~(Error? (as_float to_flt el))) /\
      (Ok? (get_prop name el) <==> ~(Error? (get_prop name el))) /\
      (* and WHICH one, structurally — the classification is exhaustive *)
      (Ok? (as_string el) <==> JStr? el) /\
      (Ok? (as_int el) <==> JInt? el) /\
      (Ok? (as_bool el) <==> JBool? el) /\
      (Ok? (as_float to_flt el) <==> (JFloat? el \/ JInt? el)) /\
      (Ok? (get_prop name el) <==> (JObj? el /\ Ok? (find_field name (JObj?.fields el)))) /\
      (Ok? (kind_of el) <==> (JObj? el /\ Ok? (find_field "kind" (JObj?.fields el)) /\
                              JStr? (Ok?.v (find_field "kind" (JObj?.fields el))))) /\
      (Ok? (str_field name el) <==> (JObj? el /\ Ok? (find_field name (JObj?.fields el)) /\
                                     JStr? (Ok?.v (find_field name (JObj?.fields el))))) /\
      (Ok? (int_field name el) <==> (JObj? el /\ Ok? (find_field name (JObj?.fields el)) /\
                                     JInt? (Ok?.v (find_field name (JObj?.fields el))))))
  = ()

(* The walker's classification: it succeeds exactly when the value is an array and every
   element decodes. `all_ok` is the model's reading of "short-circuits on the first error". *)
let rec all_ok (#num #flt: eqtype) (#t: Type)
  (d: jval num flt -> outcome t) (xs: list (jval num flt)) : Tot bool (decreases xs) =
  match xs with
  | [] -> true
  | x :: rest -> Ok? (d x) && all_ok d rest

let rec map_list_go_total (#num #flt: eqtype) (#t: Type)
  (d: jval num flt -> outcome t) (acc: list t) (xs: list (jval num flt))
  : Lemma (ensures Ok? (map_list_go d acc xs) == all_ok d xs) (decreases xs) =
  match xs with
  | [] -> ()
  | x :: rest ->
    (match d x with
     | Ok v -> map_list_go_total d (v :: acc) rest
     | Error _ -> ())

let map_list_total (#num #flt: eqtype) (#t: Type)
  (d: jval num flt -> outcome t) (el: jval num flt)
  : Lemma (ensures Ok? (map_list d el) == (JArr? el && all_ok d (JArr?.items el)))
  = match el with
    | JArr items -> map_list_go_total d [] items
    | _ -> ()

(* ---- the recursive decoder ---- *)

(* The documents `decode_node` accepts, as a structural predicate. It mentions only the
   NON-recursive combinators' success plus its own recursion, so it is a statement about the
   document's shape rather than a second copy of the decoder. *)
[@@ noextract_to "FSharp"]
let rec wf (#num #flt: eqtype) (el: jval num flt) : Tot bool (decreases %[(jsize el <: nat); 0]) =
  match kind_of el with
  | Error _ -> false
  | Ok tag ->
    if tag = "text" then Ok? (str_field "value" el)
    else if tag = "flag" then
      (match get_prop "on" el with
       | Error _ -> false
       | Ok v -> JBool? v)
    else if tag = "tags" then
      (match get_prop "tags" el with
       | Error _ -> false
       | Ok v -> Ok? (map_list (as_string #num #flt) v))
    else if tag = "group" then
      (Ok? (str_field "id" el) &&
       (match get_prop "items" el with
        | Error _ -> false
        | Ok v -> (match v with
                   | JArr ys -> wf_items ys
                   | _ -> false)))
    else false

and wf_items (#num #flt: eqtype) (ys: list (jval num flt))
  : Tot bool (decreases %[(jsizes ys <: nat); 1]) =
  match ys with
  | [] -> true
  | y :: rest -> wf y && wf_items rest

(* TOTALITY, stated: the decoder reaches an outcome on EVERY `jval`, and exactly one. That it
   type-checks at `Tot` is the proof that it terminates — the content of the theorem once
   `Json.parse` is out of scope, because a decoder that descends by NAME into a value it
   looked up has no syntactic guarantee of doing so. *)
let decode_node_total (#num #flt: eqtype) (el: jval num flt)
  : Lemma (ensures (Ok? (decode_node el) \/ Error? (decode_node el)) /\
                   ~(Ok? (decode_node el) /\ Error? (decode_node el)))
  = ()

(* … and the classification is exhaustive: it succeeds on exactly the well-formed documents,
   so every other document reaches a named `Error` and none reaches neither. *)
let rec decode_node_wf (#num #flt: eqtype) (el: jval num flt)
  : Lemma (ensures Ok? (decode_node el) == wf el) (decreases %[(jsize el <: nat); 0]) =
  match kind_of el with
  | Error _ -> ()
  | Ok tag ->
    if tag = "text" then ()
    else if tag = "flag" then ()
    else if tag = "tags" then ()
    else if tag = "group" then
      (match str_field "id" el with
       | Error _ -> ()
       | Ok _ ->
         (match get_prop "items" el with
          | Error _ -> ()
          | Ok v ->
            (match v with
             | JArr ys -> decode_items_wf [] ys
             | _ -> ())))
    else ()

and decode_items_wf (#num #flt: eqtype) (acc: list rnode) (ys: list (jval num flt))
  : Lemma (ensures Ok? (decode_items acc ys) == wf_items ys) (decreases %[(jsizes ys <: nat); 1]) =
  match ys with
  | [] -> ()
  | y :: rest ->
    decode_node_wf y;
    (match decode_node y with
     | Ok n -> decode_items_wf (n :: acc) rest
     | Error _ -> ())

(* ======================================================================================
   6. THE ROUND TRIP — the reference vocabulary's encoder is inverted by the decoder.
   ====================================================================================== *)

(* The walker's accumulator law over a list of encoded strings: `rev_app acc ts` is
   `reverse acc` followed by `ts`, which is what the `rev` at the end of `mapList` produces.
   The inductive step is definitional — `rev_app (s :: acc) t` unfolds to `rev_app acc (s :: t)`
   in one step — so the associativity this shape usually needs does not arise. *)
let rec map_list_encode_tags (#num #flt: eqtype) (acc: list string) (ts: list string)
  : Lemma (ensures map_list_go (as_string #num #flt) acc (encode_tags #num #flt ts) ==
                   Ok (rev_app acc ts))
          (decreases ts) =
  match ts with
  | [] -> ()
  | s :: t -> map_list_encode_tags #num #flt (s :: acc) t

(* THE ROUND TRIP. `encode` is the reference vocabulary's `Json.kindObj` envelope; the decoder
   above inverts it exactly, for every node, at every depth. *)
let rec decode_encode_roundtrip (#num #flt: eqtype) (n: rnode)
  : Lemma (ensures decode_node (encode #num #flt n) == Ok n) (decreases %[(rsize n <: nat); 0]) =
  match n with
  | RText _ -> ()
  | RFlag _ -> ()
  | RTags ts -> map_list_encode_tags #num #flt [] ts
  | RGroup _ items -> decode_encode_items #num #flt [] items

and decode_encode_items (#num #flt: eqtype) (acc: list rnode) (ns: list rnode)
  : Lemma (ensures decode_items acc (encode_items #num #flt ns) == Ok (rev_app acc ns))
          (decreases %[(rsizes ns <: nat); 1]) =
  match ns with
  | [] -> ()
  | n :: t ->
    decode_encode_roundtrip #num #flt n;
    decode_encode_items #num #flt (n :: acc) t

(* ======================================================================================
   7. The Phase 102 read policy — modelled where it lives, one layer above the combinators.

      See the header: `NullPolicy` is a parameter of `Json.parseDetailedWithPolicy` and of
      nothing else, and `JVal` has no null constructor, so a combinator cannot see a null.
      What the policy does to the value the combinators are handed is a normalisation on the
      DOCUMENT, and that is what is modelled: `jvaln` — the foreign document, which has a
      null — read into `jval`, which by its type cannot carry one.
   ====================================================================================== *)

(* F#: `NullPolicy` (Wire.fs). *)
type null_policy =
  | RejectNull
  | EraseMemberNull

(* The foreign document. F#: what the grammar of `Json.parse` accepts, before the policy
   decides what to do with the `null` token. *)
type jvaln (num flt: eqtype) =
  | NStr   : s:string -> jvaln num flt
  | NInt   : i:num -> jvaln num flt
  | NBool  : b:bool -> jvaln num flt
  | NFloat : f:flt -> jvaln num flt
  | NArr   : items:list (jvaln num flt) -> jvaln num flt
  | NObj   : fields:list (string & jvaln num flt) -> jvaln num flt
  | NNull  : jvaln num flt

[@@ noextract_to "FSharp"]
let rec nsize (#num #flt: eqtype) (d: jvaln num flt) : Tot pos =
  match d with
  | NArr xs -> 1 + nsizes xs
  | NObj fs -> 1 + fnsize fs
  | _ -> 1

and nsizes (#num #flt: eqtype) (xs: list (jvaln num flt)) : Tot nat =
  match xs with
  | [] -> 0
  | x :: t -> nsize x + nsizes t

and fnsize (#num #flt: eqtype) (fs: list (string & jvaln num flt)) : Tot nat =
  match fs with
  | [] -> 0
  | (_, v) :: t -> nsize v + fnsize t

[@@ noextract_to "FSharp"]
let nsize_at_least_one (#num #flt: eqtype) (d: jvaln num flt)
  : Lemma (ensures nsize d >= 1) [SMTPat (nsize d)] =
  match d with
  | NStr _ -> () | NInt _ -> () | NBool _ -> () | NFloat _ -> ()
  | NArr _ -> () | NObj _ -> () | NNull -> ()

[@@ noextract_to "FSharp"]
let nsizes_cons (#num #flt: eqtype) (x: jvaln num flt) (t: list (jvaln num flt))
  : Lemma (ensures nsizes (x :: t) == nsize x + nsizes t) [SMTPat (nsizes (x :: t))] = ()

[@@ noextract_to "FSharp"]
let fnsize_cons (#num #flt: eqtype) (k: string) (v: jvaln num flt) (t: list (string & jvaln num flt))
  : Lemma (ensures fnsize ((k, v) :: t) == nsize v + fnsize t) [SMTPat (fnsize ((k, v) :: t))] = ()

(* F#: the two messages `parseValue`'s `'n'` arm raises, verbatim — the tolerant policy names
   a DIFFERENT rejection at a position it declines to erase, deliberately, "since the remedy
   is different" (Wire.fs). Reproducing both is what lets the theorem below be honest about
   which half of the verdict is preserved. *)
let reject_msg: string = "null is not representable in the Fuaran wire JVal model"

let no_absence_msg: string =
  "null is not representable in the Fuaran wire JVal model, and this position has no absence to erase it to (only an object-member null is erased)"

(* F#: `Json.parseDetailedWithPolicy`, at the tree. The erase fork is the ONE behavioural
   difference between the policies, and it is in member position only. *)
let rec read (#num #flt: eqtype) (p: null_policy) (d: jvaln num flt)
  : Tot (outcome (jval num flt)) (decreases %[(nsize d <: nat); 0]) =
  match d with
  | NNull -> Error (if EraseMemberNull? p then no_absence_msg else reject_msg)
  | NStr s -> Ok (JStr s)
  | NInt i -> Ok (JInt i)
  | NBool b -> Ok (JBool b)
  | NFloat f -> Ok (JFloat f)
  | NArr xs ->
    (match read_items p xs with
     | Error m -> Error m
     | Ok vs -> Ok (JArr vs))
  | NObj fs ->
    (match read_fields p fs with
     | Error m -> Error m
     | Ok kvs -> Ok (JObj kvs))

and read_items (#num #flt: eqtype) (p: null_policy) (xs: list (jvaln num flt))
  : Tot (outcome (list (jval num flt))) (decreases %[(nsizes xs <: nat); 1]) =
  match xs with
  | [] -> Ok []
  | x :: t ->
    (match read p x with
     | Error m -> Error m
     | Ok v ->
       (match read_items p t with
        | Error m -> Error m
        | Ok vs -> Ok (v :: vs)))

and read_fields (#num #flt: eqtype) (p: null_policy) (fs: list (string & jvaln num flt))
  : Tot (outcome (list (string & jval num flt))) (decreases %[(fnsize fs <: nat); 1]) =
  match fs with
  | [] -> Ok []
  | (k, v) :: t ->
    if EraseMemberNull? p && NNull? v then
      read_fields p t
    else
      (match read p v with
       | Error m -> Error m
       | Ok jv ->
         (match read_fields p t with
          | Error m -> Error m
          | Ok r -> Ok ((k, jv) :: r)))

(* The inputs the policy NAMES: a `null` in object-member value position, anywhere in the
   document. Everything else is off-policy. *)
[@@ noextract_to "FSharp"]
let rec has_member_null (#num #flt: eqtype) (d: jvaln num flt) : Tot bool (decreases %[(nsize d <: nat); 0]) =
  match d with
  | NArr xs -> has_member_null_items xs
  | NObj fs -> has_member_null_fields fs
  | _ -> false

and has_member_null_items (#num #flt: eqtype) (xs: list (jvaln num flt))
  : Tot bool (decreases %[(nsizes xs <: nat); 1]) =
  match xs with
  | [] -> false
  | x :: t -> has_member_null x || has_member_null_items t

and has_member_null_fields (#num #flt: eqtype) (fs: list (string & jvaln num flt))
  : Tot bool (decreases %[(fnsize fs <: nat); 1]) =
  match fs with
  | [] -> false
  | (_, v) :: t -> NNull? v || has_member_null v || has_member_null_fields t

(* Any `null` at all, in any position. *)
[@@ noextract_to "FSharp"]
let rec has_null (#num #flt: eqtype) (d: jvaln num flt) : Tot bool (decreases %[(nsize d <: nat); 0]) =
  match d with
  | NNull -> true
  | NArr xs -> has_null_items xs
  | NObj fs -> has_null_fields fs
  | _ -> false

and has_null_items (#num #flt: eqtype) (xs: list (jvaln num flt))
  : Tot bool (decreases %[(nsizes xs <: nat); 1]) =
  match xs with
  | [] -> false
  | x :: t -> has_null x || has_null_items t

and has_null_fields (#num #flt: eqtype) (fs: list (string & jvaln num flt))
  : Tot bool (decreases %[(fnsize fs <: nat); 1]) =
  match fs with
  | [] -> false
  | (_, v) :: t -> has_null v || has_null_fields t

(* Two outcomes agree: the same value, or both refused. The message is deliberately NOT part
   of this — see `reject_msg` / `no_absence_msg` above, and the README's statement of exactly
   how far the Phase 102 promise reaches. *)
let agree (#a: Type) (o1 o2: outcome a) : prop =
  match o1, o2 with
  | Ok x, Ok y -> x == y
  | Error _, Error _ -> True
  | _, _ -> False

(* THEOREM: on every document the policy does not NAME — no `null` in object-member position
   anywhere — the tolerant reader's verdict is the strict reader's: the same value when both
   accept, and a refusal when either refuses. This is the Phase 102 promise, mechanised, and
   `agree` rather than `==` is where the promise actually stops: at the two positions the
   tolerant policy declines to erase (a bare root `null`, an array element) both readers
   refuse, and the tolerant one says so in different words on purpose. *)
let rec lenient_agrees_off_policy (#num #flt: eqtype) (d: jvaln num flt)
  : Lemma (requires not (has_member_null d))
          (ensures agree (read RejectNull d) (read EraseMemberNull d))
          (decreases %[(nsize d <: nat); 0]) =
  match d with
  | NArr xs -> lenient_agrees_items #num #flt xs
  | NObj fs -> lenient_agrees_fields #num #flt fs
  | _ -> ()

and lenient_agrees_items (#num #flt: eqtype) (xs: list (jvaln num flt))
  : Lemma (requires not (has_member_null_items xs))
          (ensures agree (read_items RejectNull xs) (read_items EraseMemberNull xs))
          (decreases %[(nsizes xs <: nat); 1]) =
  match xs with
  | [] -> ()
  | x :: t ->
    lenient_agrees_off_policy #num #flt x;
    lenient_agrees_items #num #flt t

and lenient_agrees_fields (#num #flt: eqtype) (fs: list (string & jvaln num flt))
  : Lemma (requires not (has_member_null_fields fs))
          (ensures agree (read_fields RejectNull fs) (read_fields EraseMemberNull fs))
          (decreases %[(fnsize fs <: nat); 1]) =
  match fs with
  | [] -> ()
  | (_, v) :: t ->
    lenient_agrees_off_policy #num #flt v;
    lenient_agrees_fields #num #flt t

(* … and on a document with no `null` at all the two readers are not merely in agreement,
   they are the same function — message included, there being no message. That is the
   sharpest form of "the policy governs exactly one thing". *)
let rec strict_unchanged_on_null_free (#num #flt: eqtype) (d: jvaln num flt)
  : Lemma (requires not (has_null d))
          (ensures read RejectNull d == read EraseMemberNull d)
          (decreases %[(nsize d <: nat); 0]) =
  match d with
  | NArr xs -> null_free_items #num #flt xs
  | NObj fs -> null_free_fields #num #flt fs
  | _ -> ()

and null_free_items (#num #flt: eqtype) (xs: list (jvaln num flt))
  : Lemma (requires not (has_null_items xs))
          (ensures read_items RejectNull xs == read_items EraseMemberNull xs)
          (decreases %[(nsizes xs <: nat); 1]) =
  match xs with
  | [] -> ()
  | x :: t ->
    strict_unchanged_on_null_free #num #flt x;
    null_free_items #num #flt t

and null_free_fields (#num #flt: eqtype) (fs: list (string & jvaln num flt))
  : Lemma (requires not (has_null_fields fs))
          (ensures read_fields RejectNull fs == read_fields EraseMemberNull fs)
          (decreases %[(fnsize fs <: nat); 1]) =
  match fs with
  | [] -> ()
  | (_, v) :: t ->
    strict_unchanged_on_null_free #num #flt v;
    null_free_fields #num #flt t
